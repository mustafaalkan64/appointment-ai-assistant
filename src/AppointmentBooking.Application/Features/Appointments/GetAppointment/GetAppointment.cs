using AppointmentBooking.Application.Abstractions.Persistence;
using AppointmentBooking.Application.Abstractions.Tenancy;
using AppointmentBooking.Application.Common;
using AppointmentBooking.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace AppointmentBooking.Application.Features.Appointments.GetAppointment;

public sealed record GetAppointmentQuery(
    Guid OrganizationId,
    Guid AppointmentId,
    Guid? CustomerId = null) : IRequest<AppointmentDetailDto?>;

public sealed record AppointmentDetailDto(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    Guid CustomerId,
    Guid SlotId,
    string AppointmentNumber,
    string Status,
    string? Description,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string LocalDisplay,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    string? CustomerName = null,
    string? CustomerPhone = null,
    string? CustomerEmail = null);

public sealed class GetAppointmentHandler : IRequestHandler<GetAppointmentQuery, AppointmentDetailDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public GetAppointmentHandler(IApplicationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<AppointmentDetailDto?> Handle(GetAppointmentQuery request, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(request.OrganizationId);

        var query = _context.Appointments
            .Where(a => a.Id == request.AppointmentId && a.OrganizationId == request.OrganizationId);

        if (request.CustomerId.HasValue)
        {
            query = query.Where(a => a.CustomerId == request.CustomerId.Value);
        }

        var appointment = await query.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (appointment == null) return null;

        var slot = await _context.AppointmentSlots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == appointment.SlotId, cancellationToken);

        var org = await _context.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.OrganizationId, cancellationToken);

        var customer = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == appointment.CustomerId, cancellationToken);

        var tz = org?.TimeZone ?? Domain.ValueObjects.TimeZoneId.Create("Europe/Istanbul");

        var startUtc = slot?.TimeSlot.StartUtc ?? DateTimeOffset.UtcNow;
        var endUtc = slot?.TimeSlot.EndUtc ?? DateTimeOffset.UtcNow;
        var localStart = tz.ToLocal(startUtc);
        var localEnd = tz.ToLocal(endUtc);

        return new AppointmentDetailDto(
            appointment.Id,
            appointment.OrganizationId,
            org?.Name ?? "Organization",
            appointment.CustomerId,
            appointment.SlotId,
            appointment.AppointmentNumber.Value,
            appointment.Status.ToString(),
            appointment.Description,
            startUtc,
            endUtc,
            $"{localStart:dd.MM.yyyy HH:mm} - {localEnd:HH:mm}",
            appointment.CreatedAt,
            appointment.CancelledAt,
            appointment.CancellationReason,
            customer?.FullName,
            customer?.PhoneNumber.Value,
            customer?.Email.Value);
    }
}

public sealed class GetAppointmentEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/appointments/{appointmentId:guid}", async (
            Guid appointmentId,
            Guid? customerId,
            Guid? organizationId,
            ISender sender,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            var orgId = organizationId ?? tenantContext.OrganizationId;
            var result = await sender.Send(new GetAppointmentQuery(orgId, appointmentId, customerId), ct);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        })
        .WithName("GetAppointment")
        .WithTags("Appointments")
        .Produces<AppointmentDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }
}
