using AppointmentBooking.Application.Abstractions.DistributedLock;
using AppointmentBooking.Application.Abstractions.Persistence;
using AppointmentBooking.Application.Abstractions.Tenancy;
using AppointmentBooking.Application.Common;
using AppointmentBooking.Domain.Enums;
using AppointmentBooking.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AppointmentBooking.Application.Features.WaitingQueue.CancelQueueRequest;

public sealed record CancelQueueRequestCommand(
    Guid OrganizationId,
    Guid SlotId,
    Guid? CustomerId = null,
    string? RequestId = null) : IRequest<CancelQueueRequestResult>;

public sealed record CancelQueueRequestResult(
    bool Success,
    Guid QueueEntryId,
    string Status,
    string Message);

public sealed class CancelQueueRequestValidator : AbstractValidator<CancelQueueRequestCommand>
{
    public CancelQueueRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.SlotId).NotEmpty();
        RuleFor(x => x)
            .Must(x => (x.CustomerId.HasValue && x.CustomerId.Value != Guid.Empty) || !string.IsNullOrWhiteSpace(x.RequestId))
            .WithMessage("Either CustomerId or RequestId must be provided.");
    }
}

public sealed class CancelQueueRequestHandler : IRequestHandler<CancelQueueRequestCommand, CancelQueueRequestResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IDistributedLockService _distributedLockService;
    private readonly ILogger<CancelQueueRequestHandler> _logger;

    public CancelQueueRequestHandler(
        IApplicationDbContext context,
        ITenantContext tenantContext,
        IDistributedLockService distributedLockService,
        ILogger<CancelQueueRequestHandler> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _distributedLockService = distributedLockService;
        _logger = logger;
    }

    public async Task<CancelQueueRequestResult> Handle(CancelQueueRequestCommand request, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(request.OrganizationId);

        var query = _context.WaitingQueueEntries
            .Where(q => q.OrganizationId == request.OrganizationId && q.SlotId == request.SlotId);

        if (request.CustomerId.HasValue && request.CustomerId.Value != Guid.Empty)
        {
            query = query.Where(q => q.CustomerId == request.CustomerId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(request.RequestId))
        {
            query = query.Where(q => q.RequestId == request.RequestId);
        }

        var entry = await query
            .OrderByDescending(q => q.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (entry == null)
            throw new DomainValidationException("No matching queue entry found to cancel.");

        if (entry.Status == QueueStatus.Cancelled)
            return new CancelQueueRequestResult(true, entry.Id, entry.Status.ToString(), "Queue entry is already cancelled.");

        if (entry.Status == QueueStatus.Confirmed)
            throw new InvalidQueueStateException("Cannot cancel an already confirmed queue entry.");

        var wasReserved = entry.Status == QueueStatus.Reserved;
        entry.Cancel();

        // If it was reserved, free or reassign the slot under distributed lock
        if (wasReserved)
        {
            var lockKey = $"appointment:slot:{request.OrganizationId}:{request.SlotId}";
            var lockHandle = await _distributedLockService.TryAcquireLockAsync(
                lockKey, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(2000), TimeSpan.FromMilliseconds(200), cancellationToken);

            if (lockHandle != null)
            {
                await using (lockHandle)
                {
                    var nextInQueue = await _context.WaitingQueueEntries
                        .Where(q => q.SlotId == request.SlotId && q.Status == QueueStatus.Waiting)
                        .OrderByDescending(q => q.Priority)
                        .ThenBy(q => q.CreatedAt)
                        .FirstOrDefaultAsync(cancellationToken);

                    var slot = await _context.AppointmentSlots
                        .FirstOrDefaultAsync(s => s.Id == request.SlotId, cancellationToken);

                    if (slot != null)
                    {
                        if (nextInQueue != null)
                        {
                            nextInQueue.StartProcessing();
                            nextInQueue.Reserve(TimeSpan.FromMinutes(5));
                            slot.Reserve(nextInQueue.CustomerId, TimeSpan.FromMinutes(5));
                            _logger.LogInformation("Cancelled reservation reassigned to next queued customer {CustomerId}.", nextInQueue.CustomerId);
                        }
                        else
                        {
                            await _context.ExecuteConditionalSlotReleaseAsync(slot.Id, cancellationToken);
                        }
                    }

                    await _context.SaveChangesAsync(cancellationToken);
                }

                return new CancelQueueRequestResult(true, entry.Id, entry.Status.ToString(), "Queue entry cancelled and slot updated.");
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return new CancelQueueRequestResult(true, entry.Id, entry.Status.ToString(), "Queue entry cancelled successfully.");
    }
}

public sealed class CancelQueueRequestEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/queue/cancel", async (
            CancelQueueRequestBody body,
            ISender sender,
            CancellationToken ct) =>
        {
            var command = new CancelQueueRequestCommand(body.OrganizationId, body.SlotId, body.CustomerId, body.RequestId);
            var result = await sender.Send(command, ct);
            return Results.Ok(result);
        })
        .WithName("CancelQueueRequest")
        .WithTags("WaitingQueue")
        .Produces<CancelQueueRequestResult>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}

public sealed record CancelQueueRequestBody(Guid OrganizationId, Guid SlotId, Guid? CustomerId, string? RequestId);
