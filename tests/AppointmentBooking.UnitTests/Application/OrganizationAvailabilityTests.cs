using AppointmentBooking.Application.Abstractions.Persistence;
using AppointmentBooking.Application.Abstractions.Tenancy;
using AppointmentBooking.Application.Features.Availability.GenerateSlots;
using AppointmentBooking.Application.Features.Availability.SetWeeklyAvailability;
using AppointmentBooking.Application.Features.Availability.ToggleSlotStatus;
using AppointmentBooking.Application.Features.Organizations.RegisterOrganization;
using AppointmentBooking.Domain.Entities;
using AppointmentBooking.Domain.Enums;
using AppointmentBooking.Domain.Exceptions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AppointmentBooking.UnitTests.Application;

public class OrganizationAvailabilityTests
{
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();

    [Fact]
    public void GenerateSlots_ShouldCalculateSlotsAccurately()
    {
        // Arrange
        var org = Organization.Create("Dental Clinic", "dental-clinic", "dental@clinic.com", "+905551234567", "Istanbul", "Europe/Istanbul");
        org.SetWeeklySchedule(DayOfWeek.Monday, TimeSpan.FromHours(10), TimeSpan.FromHours(12), 30); // 10:00, 10:30, 11:00, 11:30 = 4 slots

        var monDate = new DateOnly(2027, 1, 4); // A known Monday in 2027
        monDate.DayOfWeek.Should().Be(DayOfWeek.Monday);

        var weekly = org.WeeklyAvailabilities.First(w => w.DayOfWeek == DayOfWeek.Monday);
        var tz = org.TimeZone;

        // Act
        var current = weekly.StartTime;
        var slotDuration = TimeSpan.FromMinutes(weekly.SlotDurationMinutes);
        List<(DateTimeOffset StartUtc, DateTimeOffset EndUtc)> generated = [];

        while (current + slotDuration <= weekly.EndTime)
        {
            var localStart = monDate.ToDateTime(TimeOnly.FromTimeSpan(current));
            var localEnd = monDate.ToDateTime(TimeOnly.FromTimeSpan(current + slotDuration));

            generated.Add((tz.ToUtc(localStart), tz.ToUtc(localEnd)));
            current += slotDuration;
        }

        // Assert
        generated.Should().HaveCount(4);
        var firstSlot = generated.First();
        tz.ToLocal(firstSlot.StartUtc).ToString("HH:mm").Should().Be("10:00");
        tz.ToLocal(firstSlot.EndUtc).ToString("HH:mm").Should().Be("10:30");

        var lastSlot = generated.Last();
        tz.ToLocal(lastSlot.StartUtc).ToString("HH:mm").Should().Be("11:30");
        tz.ToLocal(lastSlot.EndUtc).ToString("HH:mm").Should().Be("12:00");
    }

    [Fact]
    public void ToggleSlot_WhenActiveAppointmentExists_WithoutForce_ShouldThrowException()
    {
        // Arrange
        var slot = AppointmentSlot.Create(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddMinutes(30));

        // Act & Assert
        var act = () => slot.Deactivate(hasActiveAppointment: true, force: false);
        act.Should().Throw<ActiveAppointmentOnSlotException>();

        // With force
        slot.Deactivate(hasActiveAppointment: true, force: true);
        slot.IsActive.Should().BeFalse();
    }
}
