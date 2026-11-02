using AppointmentBooking.Application.Abstractions.Persistence;
using AppointmentBooking.Application.Abstractions.Tenancy;
using AppointmentBooking.Application.Common;
using AppointmentBooking.Domain.Entities;
using AppointmentBooking.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace AppointmentBooking.Application.Features.Availability.GenerateSlots;

public sealed record GenerateSlotsCommand(
    Guid OrganizationId,
    DateOnly StartDate,
    DateOnly EndDate,
    bool SkipExisting = true) : IRequest<GenerateSlotsResponse>;

public sealed record GenerateSlotsResponse(
    Guid OrganizationId,
    int TotalCreatedSlots,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed class GenerateSlotsValidator : AbstractValidator<GenerateSlotsCommand>
{
    public GenerateSlotsValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.EndDate).NotEmpty()
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("EndDate must be on or after StartDate.");
    }
}

public sealed class GenerateSlotsHandler : IRequestHandler<GenerateSlotsCommand, GenerateSlotsResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public GenerateSlotsHandler(IApplicationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<GenerateSlotsResponse> Handle(GenerateSlotsCommand request, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(request.OrganizationId);

        var organization = await _context.Organizations
            .Include(o => o.WeeklyAvailabilities)
            .Include(o => o.SpecialAvailabilities)
            .FirstOrDefaultAsync(o => o.Id == request.OrganizationId, cancellationToken);

        if (organization == null)
            throw new DomainValidationException($"Organization '{request.OrganizationId}' not found.");

        var tz = organization.TimeZone;
        var nowUtc = DateTimeOffset.UtcNow;

        // Fetch existing slots in the date range to prevent duplicate slot generation
        var rangeStartUtc = tz.ToUtc(request.StartDate.ToDateTime(TimeOnly.MinValue));
        var rangeEndUtc = tz.ToUtc(request.EndDate.ToDateTime(TimeOnly.MaxValue));

        var existingSlots = await _context.AppointmentSlots
            .Where(s => s.TimeSlot.StartUtc >= rangeStartUtc && s.TimeSlot.EndUtc <= rangeEndUtc)
            .Select(s => new { s.TimeSlot.StartUtc, s.TimeSlot.EndUtc })
            .ToListAsync(cancellationToken);

        var existingLookup = existingSlots
            .Select(s => (s.StartUtc, s.EndUtc))
            .ToHashSet();

        List<AppointmentSlot> newSlots = [];

        for (var date = request.StartDate; date <= request.EndDate; date = date.AddDays(1))
        {
            var special = organization.SpecialAvailabilities.FirstOrDefault(s => s.Date == date);
            if (special != null)
            {
                if (special.IsClosed) continue;

                if (special.StartTime.HasValue && special.EndTime.HasValue && special.SlotDurationMinutes.HasValue)
                {
                    GenerateDaySlots(special.StartTime.Value, special.EndTime.Value, special.SlotDurationMinutes.Value, date);
                }
                continue;
            }

            var weekly = organization.WeeklyAvailabilities.FirstOrDefault(w => w.DayOfWeek == date.DayOfWeek && w.IsActive);
            if (weekly == null) continue;

            GenerateDaySlots(weekly.StartTime, weekly.EndTime, weekly.SlotDurationMinutes, date);
        }

        if (newSlots.Count > 0)
        {
            _context.AppointmentSlots.AddRange(newSlots);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new GenerateSlotsResponse(organization.Id, newSlots.Count, request.StartDate, request.EndDate);

        void GenerateDaySlots(TimeSpan startTime, TimeSpan endTime, int durationMinutes, DateOnly date)
        {
            var slotDuration = TimeSpan.FromMinutes(durationMinutes);
            var current = startTime;

            while (current + slotDuration <= endTime)
            {
                var localStart = date.ToDateTime(TimeOnly.FromTimeSpan(current));
                var localEnd = date.ToDateTime(TimeOnly.FromTimeSpan(current + slotDuration));

                var utcStart = tz.ToUtc(localStart);
                var utcEnd = tz.ToUtc(localEnd);

                if (utcStart >= nowUtc)
                {
                    if (!request.SkipExisting || !existingLookup.Contains((utcStart, utcEnd)))
                    {
                        var slot = AppointmentSlot.Create(organization.Id, utcStart, utcEnd);
                        newSlots.Add(slot);
                        existingLookup.Add((utcStart, utcEnd));
                    }
                }

                current += slotDuration;
            }
        }
    }
}

public sealed class GenerateSlotsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/organizations/{organizationId:guid}/slots/generate", async (
            Guid organizationId,
            GenerateSlotsCommand command,
            ISender sender,
            CancellationToken ct) =>
        {
            if (organizationId != command.OrganizationId)
                return Results.BadRequest("Route organizationId does not match body.");

            var response = await sender.Send(command, ct);
            return Results.Ok(response);
        })
        .WithName("GenerateSlots")
        .WithTags("Availability")
        .Produces<GenerateSlotsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
