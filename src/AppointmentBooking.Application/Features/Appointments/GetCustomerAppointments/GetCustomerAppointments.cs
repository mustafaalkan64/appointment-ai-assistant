using AppointmentBooking.Application.Abstractions.Persistence;
using AppointmentBooking.Application.Abstractions.Tenancy;
using AppointmentBooking.Application.Common;
using AppointmentBooking.Application.Features.Appointments.GetAppointment;
using AppointmentBooking.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace AppointmentBooking.Application.Features.Appointments.GetCustomerAppointments;

public sealed record GetCustomerAppointmentsQuery(
    Guid CustomerId,
    Guid? OrganizationId = null,
    AppointmentStatus? Status = null) : IRequest<List<AppointmentDetailDto>>;

public sealed class GetCustomerAppointmentsHandler : IRequestHandler<GetCustomerAppointmentsQuery, List<AppointmentDetailDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public GetCustomerAppointmentsHandler(IApplicationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<List<AppointmentDetailDto>> Handle(GetCustomerAppointmentsQuery request, CancellationToken cancellationToken)
    {
        // When customer queries their own appointments across organizations, bypass single-tenant filter if OrganizationId is null
        if (!request.OrganizationId.HasValue)
        {
            _tenantContext.SetTenant(Guid.Empty, isSuperAdmin: true);
        }
        else
        {
            _tenantContext.SetTenant(request.OrganizationId.Value);
        }

        var query = _context.Appointments
            .Where(a => a.CustomerId == request.CustomerId);

        if (request.OrganizationId.HasValue)
        {
            query = query.Where(a => a.OrganizationId == request.OrganizationId.Value);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(a => a.Status == request.Status.Value);
        }

        var appointments = await query
            .OrderByDescending(a => a.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (appointments.Count == 0) return [];

        var slotIds = appointments.Select(a => a.SlotId).Distinct().ToList();
        var slots = await _context.AppointmentSlots
            .Where(s => slotIds.Contains(s.Id))
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var orgIds = appointments.Select(a => a.OrganizationId).Distinct().ToList();
        var orgs = await _context.Organizations
            .Where(o => orgIds.Contains(o.Id))
            .AsNoTracking()
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        var custIds = appointments.Select(a => a.CustomerId).Distinct().ToList();
        var customers = await _context.Customers
            .Where(c => custIds.Contains(c.Id))
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        List<AppointmentDetailDto> result = [];

        foreach (var appt in appointments)
        {
            slots.TryGetValue(appt.SlotId, out var slot);
            orgs.TryGetValue(appt.OrganizationId, out var org);
            customers.TryGetValue(appt.CustomerId, out var customer);

            var tz = org?.TimeZone ?? Domain.ValueObjects.TimeZoneId.Create("Europe/Istanbul");
            var startUtc = slot?.TimeSlot.StartUtc ?? appt.CreatedAt;
            var endUtc = slot?.TimeSlot.EndUtc ?? appt.CreatedAt.AddMinutes(30);
            var localStart = tz.ToLocal(startUtc);
            var localEnd = tz.ToLocal(endUtc);

            result.Add(new AppointmentDetailDto(
                appt.Id,
                appt.OrganizationId,
                org?.Name ?? "Organization",
                appt.CustomerId,
                appt.SlotId,
                appt.AppointmentNumber.Value,
                appt.Status.ToString(),
                appt.Description,
                startUtc,
                endUtc,
                $"{localStart:dd.MM.yyyy HH:mm} - {localEnd:HH:mm}",
                appt.CreatedAt,
                appt.CancelledAt,
                appt.CancellationReason,
                customer?.FullName,
                customer?.PhoneNumber.Value,
                customer?.Email.Value));
        }

        return result;
    }
}

public sealed record GetOrganizationAppointmentsQuery(
    Guid OrganizationId,
    AppointmentStatus? Status = null) : IRequest<List<AppointmentDetailDto>>;

public sealed class GetOrganizationAppointmentsHandler : IRequestHandler<GetOrganizationAppointmentsQuery, List<AppointmentDetailDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public GetOrganizationAppointmentsHandler(IApplicationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<List<AppointmentDetailDto>> Handle(GetOrganizationAppointmentsQuery request, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(request.OrganizationId);

        var query = _context.Appointments
            .Where(a => a.OrganizationId == request.OrganizationId);

        if (request.Status.HasValue)
        {
            query = query.Where(a => a.Status == request.Status.Value);
        }

        var appointments = await query
            .OrderByDescending(a => a.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (appointments.Count == 0) return [];

        var slotIds = appointments.Select(a => a.SlotId).Distinct().ToList();
        var slots = await _context.AppointmentSlots
            .Where(s => slotIds.Contains(s.Id))
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var org = await _context.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.OrganizationId, cancellationToken);

        var custIds = appointments.Select(a => a.CustomerId).Distinct().ToList();
        var customers = await _context.Customers
            .Where(c => custIds.Contains(c.Id))
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var tz = org?.TimeZone ?? Domain.ValueObjects.TimeZoneId.Create("Europe/Istanbul");
        var result = new List<AppointmentDetailDto>();

        foreach (var appt in appointments)
        {
            slots.TryGetValue(appt.SlotId, out var slot);
            customers.TryGetValue(appt.CustomerId, out var customer);

            var startUtc = slot?.TimeSlot.StartUtc ?? appt.CreatedAt;
            var endUtc = slot?.TimeSlot.EndUtc ?? appt.CreatedAt.AddMinutes(30);
            var localStart = tz.ToLocal(startUtc);
            var localEnd = tz.ToLocal(endUtc);

            result.Add(new AppointmentDetailDto(
                appt.Id,
                appt.OrganizationId,
                org?.Name ?? "Organization",
                appt.CustomerId,
                appt.SlotId,
                appt.AppointmentNumber.Value,
                appt.Status.ToString(),
                appt.Description,
                startUtc,
                endUtc,
                $"{localStart:dd.MM.yyyy HH:mm} - {localEnd:HH:mm}",
                appt.CreatedAt,
                appt.CancelledAt,
                appt.CancellationReason,
                customer?.FullName,
                customer?.PhoneNumber.Value,
                customer?.Email.Value));
        }

        return result;
    }
}

public sealed class GetCustomerAppointmentsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/customers/{customerId:guid}/appointments", async (
            Guid customerId,
            Guid? organizationId,
            AppointmentStatus? status,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetCustomerAppointmentsQuery(customerId, organizationId, status), ct);
            return Results.Ok(result);
        })
        .WithName("GetCustomerAppointments")
        .WithTags("Appointments")
        .Produces<List<AppointmentDetailDto>>(StatusCodes.Status200OK);

        app.MapGet("/api/organizations/{organizationId:guid}/appointments", async (
            Guid organizationId,
            AppointmentStatus? status,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetOrganizationAppointmentsQuery(organizationId, status), ct);
            return Results.Ok(result);
        })
        .WithName("GetOrganizationAppointments")
        .WithTags("Appointments")
        .Produces<List<AppointmentDetailDto>>(StatusCodes.Status200OK);
    }
}
