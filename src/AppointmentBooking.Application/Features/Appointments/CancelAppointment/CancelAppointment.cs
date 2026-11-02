using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AppointmentBooking.Application.Abstractions.DistributedLock;
using AppointmentBooking.Application.Abstractions.Persistence;
using AppointmentBooking.Application.Abstractions.Tenancy;
using AppointmentBooking.Application.Common;
using AppointmentBooking.Domain.Entities;
using AppointmentBooking.Domain.Enums;
using AppointmentBooking.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AppointmentBooking.Application.Features.Appointments.CancelAppointment;

public sealed record CancelAppointmentCommand(
    Guid OrganizationId,
    Guid AppointmentId,
    Guid CustomerId,
    string Reason,
    string IdempotencyKey) : IRequest<CancelAppointmentResult>;

public sealed record CancelAppointmentResult(
    bool Success,
    Guid AppointmentId,
    string Status,
    string Reason,
    DateTimeOffset CancelledAt,
    bool ReassignedToQueue,
    bool IsIdempotentReplay = false);

public sealed class CancelAppointmentValidator : AbstractValidator<CancelAppointmentCommand>
{
    public CancelAppointmentValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.AppointmentId).NotEmpty();
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(200);
    }
}

public sealed class CancelAppointmentHandler : IRequestHandler<CancelAppointmentCommand, CancelAppointmentResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IDistributedLockService _distributedLockService;
    private readonly ILogger<CancelAppointmentHandler> _logger;

    public CancelAppointmentHandler(
        IApplicationDbContext context,
        ITenantContext tenantContext,
        IDistributedLockService distributedLockService,
        ILogger<CancelAppointmentHandler> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _distributedLockService = distributedLockService;
        _logger = logger;
    }

    public async Task<CancelAppointmentResult> Handle(CancelAppointmentCommand request, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(request.OrganizationId);

        // Check Idempotency
        var idempotencyRecordKey = IdempotencyRecord.BuildCompositeKey(
            request.CustomerId, "cancelappointment", request.IdempotencyKey);

        var existingRecord = await _context.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == idempotencyRecordKey, cancellationToken);

        if (existingRecord != null)
        {
            var cached = JsonSerializer.Deserialize<CancelAppointmentResult>(existingRecord.ResponseBody);
            if (cached != null)
            {
                return cached with { IsIdempotentReplay = true };
            }
        }

        var appointment = await _context.Appointments
            .FirstOrDefaultAsync(a => a.Id == request.AppointmentId &&
                                      a.OrganizationId == request.OrganizationId &&
                                      a.CustomerId == request.CustomerId, cancellationToken);

        if (appointment == null)
            throw new DomainValidationException($"Appointment '{request.AppointmentId}' not found for customer.");

        if (appointment.Status == AppointmentStatus.Cancelled)
            throw new InvalidAppointmentStateException("Appointment is already cancelled.");

        if (appointment.Status == AppointmentStatus.Completed)
            throw new InvalidAppointmentStateException("Cannot cancel an already completed appointment.");

        // Check if appointment slot time has already passed
        var apptSlot = await _context.AppointmentSlots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == appointment.SlotId, cancellationToken);

        var startUtc = apptSlot?.TimeSlot.StartUtc ?? appointment.CreatedAt;
        if (startUtc <= DateTimeOffset.UtcNow)
        {
            throw new InvalidAppointmentStateException("Zamanı geçmiş randevular iptal edilemez.");
        }

        // Lock the slot
        var lockKey = $"appointment:slot:{request.OrganizationId}:{appointment.SlotId}";
        var lockHandle = await _distributedLockService.TryAcquireLockAsync(
            lockKey, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(2000), TimeSpan.FromMilliseconds(200), cancellationToken);

        if (lockHandle == null)
            throw new ConcurrencyConflictException("Could not acquire lock to process cancellation. Please retry.");

        await using (lockHandle)
        {
            await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

            try
            {
                appointment.Cancel(request.Reason);

                // Release slot in DB
                await _context.ExecuteConditionalSlotReleaseAsync(appointment.SlotId, cancellationToken);

                // Check if any customer is in WaitingQueue for this slot
                var nextInQueue = await _context.WaitingQueueEntries
                    .Where(q => q.SlotId == appointment.SlotId && q.Status == QueueStatus.Waiting)
                    .OrderByDescending(q => q.Priority)
                    .ThenBy(q => q.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                bool reassigned = false;
                if (nextInQueue != null)
                {
                    nextInQueue.StartProcessing();
                    nextInQueue.Reserve(TimeSpan.FromMinutes(10));

                    var slot = await _context.AppointmentSlots
                        .FirstOrDefaultAsync(s => s.Id == appointment.SlotId, cancellationToken);

                    slot?.Reserve(nextInQueue.CustomerId, TimeSpan.FromMinutes(10));
                    reassigned = true;
                    _logger.LogInformation("Freed slot {SlotId} successfully reassigned to queued customer {QueuedCustomerId}.",
                        appointment.SlotId, nextInQueue.CustomerId);
                }

                var result = new CancelAppointmentResult(
                    Success: true,
                    AppointmentId: appointment.Id,
                    Status: appointment.Status.ToString(),
                    Reason: appointment.CancellationReason ?? request.Reason,
                    CancelledAt: appointment.CancelledAt ?? DateTimeOffset.UtcNow,
                    ReassignedToQueue: reassigned);

                var responseJson = JsonSerializer.Serialize(result);
                var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(responseJson)));

                var idempotencyRecord = IdempotencyRecord.Create(
                    request.CustomerId,
                    "cancelappointment",
                    request.IdempotencyKey,
                    requestHash,
                    StatusCodes.Status200OK,
                    responseJson,
                    TimeSpan.FromHours(24));

                _context.IdempotencyRecords.Add(idempotencyRecord);

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cancellation failed for appointment {AppointmentId}.", request.AppointmentId);
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }
}

public sealed class CancelAppointmentEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/appointments/{appointmentId:guid}/cancel", async (
            Guid appointmentId,
            CancelAppointmentRequest body,
            ISender sender,
            CancellationToken ct) =>
        {
            var command = new CancelAppointmentCommand(
                body.OrganizationId,
                appointmentId,
                body.CustomerId,
                body.Reason,
                body.IdempotencyKey);

            var result = await sender.Send(command, ct);
            return Results.Ok(result);
        })
        .WithName("CancelAppointment")
        .WithTags("Appointments")
        .Produces<CancelAppointmentResult>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }
}

public sealed record CancelAppointmentRequest(Guid OrganizationId, Guid CustomerId, string Reason, string IdempotencyKey);
