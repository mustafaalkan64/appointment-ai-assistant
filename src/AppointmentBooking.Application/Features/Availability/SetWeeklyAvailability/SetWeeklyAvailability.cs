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

namespace AppointmentBooking.Application.Features.Availability.SetWeeklyAvailability;

public sealed record DayScheduleDto(DayOfWeek DayOfWeek, TimeSpan StartTime, TimeSpan EndTime, int SlotDurationMinutes, bool IsClosed);

public sealed record SetWeeklyAvailabilityCommand(
    Guid OrganizationId,
    List<DayScheduleDto> Days) : IRequest<SetWeeklyAvailabilityResponse>;

public sealed record SetWeeklyAvailabilityResponse(Guid OrganizationId, int UpdatedDaysCount);

public sealed class SetWeeklyAvailabilityValidator : AbstractValidator<SetWeeklyAvailabilityCommand>
{
    public SetWeeklyAvailabilityValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Days).NotEmpty();
        RuleForEach(x => x.Days).ChildRules(day =>
        {
            day.When(d => !d.IsClosed, () =>
            {
                day.RuleFor(d => d.EndTime).GreaterThan(d => d.StartTime)
                    .WithMessage("EndTime must be greater than StartTime.");
                day.RuleFor(d => d.SlotDurationMinutes).InclusiveBetween(5, 240)
                    .WithMessage("SlotDurationMinutes must be between 5 and 240 minutes.");
            });
        });
    }
}

public sealed class SetWeeklyAvailabilityHandler : IRequestHandler<SetWeeklyAvailabilityCommand, SetWeeklyAvailabilityResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public SetWeeklyAvailabilityHandler(IApplicationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<SetWeeklyAvailabilityResponse> Handle(SetWeeklyAvailabilityCommand request, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(request.OrganizationId);

        var organization = await _context.Organizations
            .Include(o => o.WeeklyAvailabilities)
            .FirstOrDefaultAsync(o => o.Id == request.OrganizationId, cancellationToken);

        if (organization == null)
            throw new DomainValidationException($"Organization '{request.OrganizationId}' not found.");

        int count = 0;
        var closedDaysList = request.Days.Where(d => d.IsClosed).Select(d => d.DayOfWeek).ToHashSet();
        if (closedDaysList.Any())
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var tz = organization.TimeZone;
            var futureAvailableSlots = await _context.AppointmentSlots
                .Where(s => s.IsActive && s.Status == SlotStatus.Available && s.TimeSlot.StartUtc >= nowUtc)
                .ToListAsync(cancellationToken);

            var slotsToDeactivate = futureAvailableSlots
                .Where(s => closedDaysList.Contains(tz.ToLocal(s.TimeSlot.StartUtc).DayOfWeek))
                .ToList();

            foreach (var s in slotsToDeactivate)
            {
                s.Deactivate();
            }
        }

        foreach (var day in request.Days)
        {
            var existing = organization.WeeklyAvailabilities.FirstOrDefault(w => w.DayOfWeek == day.DayOfWeek);
            if (day.IsClosed)
            {
                if (existing != null)
                {
                    existing.Deactivate();
                }
            }
            else
            {
                if (existing != null)
                {
                    existing.Update(day.StartTime, day.EndTime, day.SlotDurationMinutes);
                }
                else
                {
                    var newSchedule = WeeklyAvailability.Create(organization.Id, day.DayOfWeek, day.StartTime, day.EndTime, day.SlotDurationMinutes);
                    _context.WeeklyAvailabilities.Add(newSchedule);
                }
            }
            count++;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new SetWeeklyAvailabilityResponse(organization.Id, count);
    }
}

public sealed class SetWeeklyAvailabilityEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/organizations/{organizationId:guid}/availability/weekly", async (
            Guid organizationId,
            SetWeeklyAvailabilityCommand command,
            ISender sender,
            CancellationToken ct) =>
        {
            if (organizationId != command.OrganizationId)
                return Results.BadRequest("Route organizationId does not match body.");

            var response = await sender.Send(command, ct);
            return Results.Ok(response);
        })
        .WithName("SetWeeklyAvailability")
        .WithTags("Availability")
        .Produces<SetWeeklyAvailabilityResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
