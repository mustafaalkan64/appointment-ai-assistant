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

namespace AppointmentBooking.Application.Features.Appointments.CreateAppointment;

public sealed record CreateAppointmentCommand(
    Guid OrganizationId,
    Guid CustomerId,
    Guid SlotId,
    string IdempotencyKey,
    string? Description = null) : IRequest<CreateAppointmentResult>;

public sealed record CreateAppointmentResult(
    bool Success,
    Guid? AppointmentId,
    string? AppointmentNumber,
    string Status,
    DateTimeOffset? StartUtc,
    DateTimeOffset? EndUtc,
    string? LocalDisplay,
    Guid? QueueEntryId,
    string Message,
    bool IsIdempotentReplay = false);

public sealed class CreateAppointmentValidator : AbstractValidator<CreateAppointmentCommand>
{
    public CreateAppointmentValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.SlotId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreateAppointmentHandler : IRequestHandler<CreateAppointmentCommand, CreateAppointmentResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IDistributedLockService _distributedLockService;
    private readonly ILogger<CreateAppointmentHandler> _logger;

    public CreateAppointmentHandler(
        IApplicationDbContext context,
        ITenantContext tenantContext,
        IDistributedLockService distributedLockService,
        ILogger<CreateAppointmentHandler> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _distributedLockService = distributedLockService;
        _logger = logger;
    }

    public async Task<CreateAppointmentResult> Handle(CreateAppointmentCommand request, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(request.OrganizationId);

        // -------------------------------------------------------------
        // LAYER 1: Redis Distributed Lock (RedLock.net)
        // -------------------------------------------------------------
        var lockKey = $"appointment:slot:{request.OrganizationId}:{request.SlotId}";
        var expiryTime = TimeSpan.FromSeconds(10);
        var waitTime = TimeSpan.FromMilliseconds(1500);
        var retryTime = TimeSpan.FromMilliseconds(200);

        var lockHandle = await _distributedLockService.TryAcquireLockAsync(
            lockKey, expiryTime, waitTime, retryTime, cancellationToken);

        if (lockHandle == null)
        {
            // ---------------------------------------------------------
            // LAYER 6: Waiting Queue Fallback under extreme concurrency
            // ---------------------------------------------------------
            _logger.LogWarning("Lock acquisition timed out for slot {SlotId}. Routing customer {CustomerId} to Waiting Queue.",
                request.SlotId, request.CustomerId);

            var queueEntry = WaitingQueueEntry.Enqueue(
                request.OrganizationId,
                request.SlotId,
                request.CustomerId,
                request.IdempotencyKey);

            _context.WaitingQueueEntries.Add(queueEntry);
            await _context.SaveChangesAsync(cancellationToken);

            return new CreateAppointmentResult(
                Success: true,
                AppointmentId: null,
                AppointmentNumber: null,
                Status: "EnqueuedInWaitingQueue",
                StartUtc: DateTimeOffset.UtcNow,
                EndUtc: DateTimeOffset.UtcNow.AddMinutes(30),
                LocalDisplay: "Waiting Queue Position",
                QueueEntryId: queueEntry.Id,
                Message: "Slot is currently experiencing high demand. You have been placed in the waiting queue and will be notified as soon as a slot confirms.");
        }

        await using (lockHandle)
        {
            // ---------------------------------------------------------
            // LAYER 5: Idempotency Key Verification (Protected inside Lock)
            // ---------------------------------------------------------
            var idempotencyRecordKey = IdempotencyRecord.BuildCompositeKey(
                request.CustomerId, "createappointment", request.IdempotencyKey);

            var existingRecord = await _context.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == idempotencyRecordKey, cancellationToken);

            if (existingRecord != null)
            {
                _logger.LogInformation("Idempotent request detected for key: {Key}. Returning cached response.", idempotencyRecordKey);
                var cached = JsonSerializer.Deserialize<CreateAppointmentResult>(existingRecord.ResponseBody);
                if (cached != null)
                {
                    return cached with { IsIdempotentReplay = true };
                }
            }

            // Verify slot exists and fetch details for display
            var slot = await _context.AppointmentSlots
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.SlotId && s.OrganizationId == request.OrganizationId, cancellationToken);

            if (slot == null)
                throw new DomainValidationException($"Slot '{request.SlotId}' was not found.");

            if (!slot.IsActive || (slot.Status != SlotStatus.Available && slot.Status != SlotStatus.Reserved))
                throw new SlotNotAvailableException(slot.Id, slot.Status.ToString());

            var organization = await _context.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == request.OrganizationId, cancellationToken);

            var tz = organization?.TimeZone ?? Domain.ValueObjects.TimeZoneId.Create("Europe/Istanbul");

            // ---------------------------------------------------------
            // LAYER 2: Database Transaction & Atomic Conditional SQL Update
            // ---------------------------------------------------------
            await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

            try
            {
                var rowsAffected = await _context.ExecuteConditionalSlotBookingAsync(request.SlotId, cancellationToken);
                if (rowsAffected != 1)
                {
                    _logger.LogWarning("Atomic conditional update failed for slot {SlotId}. Rows affected: {RowsAffected}",
                        request.SlotId, rowsAffected);
                    throw new SlotNotAvailableException(request.SlotId, "BookedByAnotherCustomer");
                }

                // -----------------------------------------------------
                // LAYER 3 & 4: Insert Appointment & Filtered Unique Index Guard
                // -----------------------------------------------------
                var appointment = Appointment.Create(
                    request.OrganizationId,
                    request.CustomerId,
                    request.SlotId,
                    request.Description);

                _context.Appointments.Add(appointment);

                // Update waiting queue entry if customer was previously waiting
                var activeQueueEntry = await _context.WaitingQueueEntries
                    .FirstOrDefaultAsync(q => q.SlotId == request.SlotId &&
                                              q.CustomerId == request.CustomerId &&
                                              (q.Status == QueueStatus.Waiting || q.Status == QueueStatus.Reserved),
                                         cancellationToken);

                activeQueueEntry?.Confirm();

                var localStart = tz.ToLocal(slot.TimeSlot.StartUtc);
                var localEnd = tz.ToLocal(slot.TimeSlot.EndUtc);
                var localDisplay = $"{localStart:dd.MM.yyyy HH:mm} - {localEnd:HH:mm}";

                var result = new CreateAppointmentResult(
                    Success: true,
                    AppointmentId: appointment.Id,
                    AppointmentNumber: appointment.AppointmentNumber.Value,
                    Status: appointment.Status.ToString(),
                    StartUtc: slot.TimeSlot.StartUtc,
                    EndUtc: slot.TimeSlot.EndUtc,
                    LocalDisplay: localDisplay,
                    QueueEntryId: null,
                    Message: "Appointment successfully created and confirmed.");

                // Save Idempotency Record within the same transaction
                var responseJson = JsonSerializer.Serialize(result);
                var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(responseJson)));

                var idempotencyRecord = IdempotencyRecord.Create(
                    request.CustomerId,
                    "createappointment",
                    request.IdempotencyKey,
                    requestHash,
                    StatusCodes.Status201Created,
                    responseJson,
                    TimeSpan.FromHours(24));

                _context.IdempotencyRecords.Add(idempotencyRecord);

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation("Appointment {AppointmentNumber} successfully booked for customer {CustomerId} on slot {SlotId}.",
                    appointment.AppointmentNumber.Value, request.CustomerId, request.SlotId);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transaction failed for slot {SlotId}. Rolling back.", request.SlotId);
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }
}

public sealed class CreateAppointmentEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/appointments", async (CreateAppointmentCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            if (result.QueueEntryId.HasValue)
            {
                return Results.Accepted($"/api/queue/{result.QueueEntryId.Value}", result);
            }

            return Results.Created($"/api/appointments/{result.AppointmentId}", result);
        })
        .WithName("CreateAppointment")
        .WithTags("Appointments")
        .Produces<CreateAppointmentResult>(StatusCodes.Status201Created)
        .Produces<CreateAppointmentResult>(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesValidationProblem();
    }
}
