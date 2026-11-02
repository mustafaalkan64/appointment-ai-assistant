using AppointmentBooking.Application.Abstractions.Persistence;
using AppointmentBooking.Application.Abstractions.Tenancy;
using AppointmentBooking.Application.Common;
using AppointmentBooking.Domain.Enums;
using AppointmentBooking.Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace AppointmentBooking.Application.Features.Availability.ToggleSlotStatus;

public sealed record ToggleSlotStatusCommand(
    Guid OrganizationId,
    Guid SlotId,
    bool? IsActive = null,
    bool Force = false) : IRequest<ToggleSlotStatusResponse>;

public sealed record ToggleSlotStatusResponse(
    Guid SlotId,
    bool IsActive,
    string Status);

public sealed class ToggleSlotStatusHandler : IRequestHandler<ToggleSlotStatusCommand, ToggleSlotStatusResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public ToggleSlotStatusHandler(IApplicationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<ToggleSlotStatusResponse> Handle(ToggleSlotStatusCommand request, CancellationToken cancellationToken)
    {
        var query = _context.AppointmentSlots.AsQueryable();

        if (request.OrganizationId != Guid.Empty)
        {
            _tenantContext.SetTenant(request.OrganizationId);
            query = query.Where(s => s.OrganizationId == request.OrganizationId);
        }
        else
        {
            _tenantContext.SetTenant(Guid.Empty, isSuperAdmin: true);
        }

        var slot = await query.FirstOrDefaultAsync(s => s.Id == request.SlotId, cancellationToken);

        if (slot == null)
            throw new DomainValidationException($"Slot '{request.SlotId}' not found.");

        var targetIsActive = request.IsActive ?? !slot.IsActive;

        if (targetIsActive)
        {
            slot.Activate();
            if (slot.Status == SlotStatus.Blocked)
            {
                slot.Unblock();
            }
        }
        else
        {
            var hasActiveAppointment = await _context.Appointments
                .AnyAsync(a => a.SlotId == request.SlotId &&
                               a.Status != AppointmentStatus.Cancelled &&
                               a.Status != AppointmentStatus.Completed,
                          cancellationToken);

            // Domain rule: Throws ActiveAppointmentOnSlotException if active appointment exists and force is false
            slot.Deactivate(hasActiveAppointment, request.Force);
            if (slot.Status == SlotStatus.Available)
            {
                slot.Block();
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new ToggleSlotStatusResponse(slot.Id, slot.IsActive, slot.Status.ToString());
    }
}

public sealed class ToggleSlotStatusEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // PUT /api/organizations/{organizationId}/slots/{slotId}/status
        app.MapPut("/api/organizations/{organizationId:guid}/slots/{slotId:guid}/status", HandleUpdateSlotStatus)
            .WithName("PutSlotStatusWithOrg")
            .WithTags("Availability")
            .Produces<ToggleSlotStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // PUT /api/organizations/{organizationId}/slots/{slotId}
        app.MapPut("/api/organizations/{organizationId:guid}/slots/{slotId:guid}", HandleUpdateSlotStatus)
            .WithName("PutSlotWithOrg")
            .WithTags("Availability")
            .Produces<ToggleSlotStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // PUT /api/organizations/{organizationId}/slots/{slotId}/toggle
        app.MapPut("/api/organizations/{organizationId:guid}/slots/{slotId:guid}/toggle", HandleUpdateSlotStatus)
            .WithName("PutSlotToggleWithOrg")
            .WithTags("Availability")
            .Produces<ToggleSlotStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // PATCH /api/organizations/{organizationId}/slots/{slotId}/toggle (backwards compatibility)
        app.MapPatch("/api/organizations/{organizationId:guid}/slots/{slotId:guid}/toggle", HandleUpdateSlotStatus)
            .WithName("PatchSlotToggleWithOrg")
            .WithTags("Availability")
            .Produces<ToggleSlotStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // PUT /api/slots/{slotId}/status
        app.MapPut("/api/slots/{slotId:guid}/status", async (
            Guid slotId,
            UpdateSlotStatusRequest? body,
            ISender sender,
            CancellationToken ct) =>
        {
            var targetActive = ResolveTargetActive(body);
            var command = new ToggleSlotStatusCommand(Guid.Empty, slotId, targetActive, body?.Force ?? false);
            var response = await sender.Send(command, ct);
            return Results.Ok(response);
        })
        .WithName("PutSlotStatus")
        .WithTags("Availability")
        .Produces<ToggleSlotStatusResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // PUT /api/slots/{slotId}
        app.MapPut("/api/slots/{slotId:guid}", async (
            Guid slotId,
            UpdateSlotStatusRequest? body,
            ISender sender,
            CancellationToken ct) =>
        {
            var targetActive = ResolveTargetActive(body);
            var command = new ToggleSlotStatusCommand(Guid.Empty, slotId, targetActive, body?.Force ?? false);
            var response = await sender.Send(command, ct);
            return Results.Ok(response);
        })
        .WithName("PutSlot")
        .WithTags("Availability")
        .Produces<ToggleSlotStatusResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> HandleUpdateSlotStatus(
        Guid organizationId,
        Guid slotId,
        UpdateSlotStatusRequest? body,
        ISender sender,
        CancellationToken ct)
    {
        var targetActive = ResolveTargetActive(body);
        var command = new ToggleSlotStatusCommand(organizationId, slotId, targetActive, body?.Force ?? false);
        var response = await sender.Send(command, ct);
        return Results.Ok(response);
    }

    private static bool? ResolveTargetActive(UpdateSlotStatusRequest? body)
    {
        if (body?.IsActive.HasValue == true)
            return body.IsActive.Value;

        if (!string.IsNullOrWhiteSpace(body?.Status))
        {
            return string.Equals(body.Status, "Available", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(body.Status, "Active", StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }
}

public sealed record UpdateSlotStatusRequest(bool? IsActive = null, string? Status = null, bool Force = false);
