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

namespace AppointmentBooking.Application.Features.WaitingQueue.GetQueueStatus;

public sealed record GetQueueStatusQuery(
    Guid OrganizationId,
    Guid SlotId,
    Guid? CustomerId = null,
    string? RequestId = null) : IRequest<QueueStatusResponse>;

public sealed record QueueStatusResponse(
    Guid QueueEntryId,
    Guid OrganizationId,
    Guid SlotId,
    Guid CustomerId,
    string RequestId,
    int Priority,
    string Status,
    int? QueuePosition,
    DateTimeOffset? ReservedUntil,
    int? EstimatedWaitMinutes,
    DateTimeOffset CreatedAt);

public sealed class GetQueueStatusValidator : AbstractValidator<GetQueueStatusQuery>
{
    public GetQueueStatusValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.SlotId).NotEmpty();
        RuleFor(x => x)
            .Must(x => (x.CustomerId.HasValue && x.CustomerId.Value != Guid.Empty) || !string.IsNullOrWhiteSpace(x.RequestId))
            .WithMessage("Either CustomerId or RequestId must be provided.");
    }
}

public sealed class GetQueueStatusHandler : IRequestHandler<GetQueueStatusQuery, QueueStatusResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public GetQueueStatusHandler(IApplicationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<QueueStatusResponse> Handle(GetQueueStatusQuery request, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(request.OrganizationId);

        var query = _context.WaitingQueueEntries
            .AsNoTracking()
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
            throw new DomainValidationException("No queue entry found for the specified criteria.");

        int? position = null;
        int? estimatedWait = null;

        if (entry.Status == QueueStatus.Waiting)
        {
            // Position is 1 + number of waiting entries with higher priority or earlier timestamp
            var aheadCount = await _context.WaitingQueueEntries
                .AsNoTracking()
                .CountAsync(q => q.SlotId == entry.SlotId &&
                                 q.Status == QueueStatus.Waiting &&
                                 (q.Priority > entry.Priority ||
                                  (q.Priority == entry.Priority && q.CreatedAt < entry.CreatedAt)),
                            cancellationToken);

            position = aheadCount + 1;
            estimatedWait = position.Value * 15; // Average 15 mins per slot rotation
        }

        return new QueueStatusResponse(
            QueueEntryId: entry.Id,
            OrganizationId: entry.OrganizationId,
            SlotId: entry.SlotId,
            CustomerId: entry.CustomerId,
            RequestId: entry.RequestId,
            Priority: entry.Priority,
            Status: entry.Status.ToString(),
            QueuePosition: position,
            ReservedUntil: entry.ReservedUntil,
            EstimatedWaitMinutes: estimatedWait,
            CreatedAt: entry.CreatedAt);
    }
}

public sealed class GetQueueStatusEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/queue/status", async (
            Guid organizationId,
            Guid slotId,
            Guid? customerId,
            string? requestId,
            ISender sender,
            CancellationToken ct) =>
        {
            var query = new GetQueueStatusQuery(organizationId, slotId, customerId, requestId);
            var result = await sender.Send(query, ct);
            return Results.Ok(result);
        })
        .WithName("GetQueueStatus")
        .WithTags("WaitingQueue")
        .Produces<QueueStatusResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
