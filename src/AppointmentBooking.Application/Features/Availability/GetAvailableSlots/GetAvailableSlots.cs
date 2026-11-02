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

namespace AppointmentBooking.Application.Features.Availability.GetAvailableSlots;

public sealed record GetAvailableSlotsQuery(
    Guid OrganizationId,
    DateOnly? Date,
    DateOnly? StartDate,
    DateOnly? EndDate,
    bool IncludeAll = false) : IRequest<GetAvailableSlotsResponse>;

public sealed record SlotDto(
    Guid Id,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string StartTime,
    string EndTime,
    string Date,
    string LocalStartTime,
    string LocalEndTime,
    string LocalDisplay,
    SlotStatus Status,
    bool IsActive = true,
    string? CustomerName = null,
    string? AppointmentNumber = null);

public sealed record GetAvailableSlotsResponse(
    Guid OrganizationId,
    string TimeZone,
    List<SlotDto> Slots);

public sealed class GetAvailableSlotsHandler : IRequestHandler<GetAvailableSlotsQuery, GetAvailableSlotsResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public GetAvailableSlotsHandler(IApplicationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<GetAvailableSlotsResponse> Handle(GetAvailableSlotsQuery request, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(request.OrganizationId);

        var organization = await _context.Organizations
            .Include(o => o.WeeklyAvailabilities)
            .Include(o => o.SpecialAvailabilities)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.OrganizationId, cancellationToken);

        if (organization == null)
            throw new DomainValidationException($"Organization '{request.OrganizationId}' not found.");

        var tz = organization.TimeZone;
        var nowUtc = DateTimeOffset.UtcNow;

        var fromDate = request.Date ?? request.StartDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var toDate = request.Date ?? request.EndDate ?? fromDate.AddDays(7);

        var startUtc = tz.ToUtc(fromDate.ToDateTime(TimeOnly.MinValue));
        var endUtc = tz.ToUtc(toDate.ToDateTime(TimeOnly.MaxValue));

        var query = _context.AppointmentSlots
            .Where(s => s.TimeSlot.StartUtc >= startUtc && s.TimeSlot.EndUtc <= endUtc);

        if (!request.IncludeAll)
        {
            query = query.Where(s => s.IsActive && s.Status == SlotStatus.Available && s.TimeSlot.StartUtc >= nowUtc);
        }

        var slots = await query
            .OrderBy(s => s.TimeSlot.StartUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (organization.WeeklyAvailabilities.Any() || organization.SpecialAvailabilities.Any())
        {
            var closedDays = organization.WeeklyAvailabilities
                .Where(w => !w.IsActive)
                .Select(w => w.DayOfWeek)
                .ToHashSet();

            var specialClosedDates = organization.SpecialAvailabilities
                .Where(s => s.IsClosed)
                .Select(s => s.Date)
                .ToHashSet();

            var specialOpenDates = organization.SpecialAvailabilities
                .Where(s => !s.IsClosed)
                .Select(s => s.Date)
                .ToHashSet();

            slots = slots.Where(s =>
            {
                var localDate = DateOnly.FromDateTime(tz.ToLocal(s.TimeSlot.StartUtc));
                if (specialOpenDates.Contains(localDate)) return true;

                bool isClosedDay = specialClosedDates.Contains(localDate) || closedDays.Contains(localDate.DayOfWeek);
                if (isClosedDay)
                {
                    // On a closed day, only booked/reserved appointments are retained if IncludeAll is requested.
                    // Unbooked empty/available slots must NEVER be returned for a closed day.
                    return request.IncludeAll && (s.Status == SlotStatus.Booked || s.Status == SlotStatus.Reserved);
                }

                return true;
            }).ToList();
        }

        // Fetch appointment details for booked/reserved slots if includeAll is requested
        Dictionary<Guid, (string? CustomerName, string? AppointmentNumber)> bookingDetails = new();
        if (request.IncludeAll && slots.Any(s => s.Status == SlotStatus.Booked || s.Status == SlotStatus.Reserved))
        {
            var bookedSlotIds = slots
                .Where(s => s.Status == SlotStatus.Booked || s.Status == SlotStatus.Reserved)
                .Select(s => s.Id)
                .ToList();

            var appointments = await _context.Appointments
                .Where(a => bookedSlotIds.Contains(a.SlotId) && a.Status != AppointmentStatus.Cancelled)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var custIds = appointments.Select(a => a.CustomerId).Distinct().ToList();
            var customers = await _context.Customers
                .Where(c => custIds.Contains(c.Id))
                .AsNoTracking()
                .ToDictionaryAsync(c => c.Id, c => c.FullName, cancellationToken);

            foreach (var appt in appointments)
            {
                var custName = customers.TryGetValue(appt.CustomerId, out var name) ? name : "Kayıtlı Müşteri";
                bookingDetails[appt.SlotId] = (custName, appt.AppointmentNumber.Value);
            }
        }

        var slotDtos = slots.Select(s =>
        {
            var localStart = tz.ToLocal(s.TimeSlot.StartUtc);
            var localEnd = tz.ToLocal(s.TimeSlot.EndUtc);

            var startStr = localStart.ToString("HH:mm");
            var endStr = localEnd.ToString("HH:mm");
            var dateStr = localStart.ToString("dd.MM.yyyy");

            bookingDetails.TryGetValue(s.Id, out var details);

            return new SlotDto(
                s.Id,
                s.TimeSlot.StartUtc,
                s.TimeSlot.EndUtc,
                startStr,
                endStr,
                dateStr,
                startStr,
                endStr,
                $"{dateStr} {startStr} - {endStr}",
                s.Status,
                s.IsActive,
                details.CustomerName,
                details.AppointmentNumber);
        }).ToList();

        return new GetAvailableSlotsResponse(organization.Id, tz.Value, slotDtos);
    }
}

public sealed class GetAvailableSlotsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/organizations/{organizationId:guid}/slots/available", async (
            Guid organizationId,
            DateOnly? date,
            DateOnly? startDate,
            DateOnly? endDate,
            bool? includeAll,
            ISender sender,
            CancellationToken ct) =>
        {
            var response = await sender.Send(new GetAvailableSlotsQuery(organizationId, date, startDate, endDate, includeAll ?? false), ct);
            return Results.Ok(response);
        })
        .WithName("GetAvailableSlots")
        .WithTags("Availability")
        .Produces<GetAvailableSlotsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
