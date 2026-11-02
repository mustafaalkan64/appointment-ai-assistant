using AppointmentBooking.Domain.Entities;
using AppointmentBooking.Domain.Enums;
using AppointmentBooking.Domain.Events;
using AppointmentBooking.Domain.Exceptions;
using AppointmentBooking.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AppointmentBooking.UnitTests.Domain;

public class DomainTests
{
    [Fact]
    public void Organization_Create_ValidInputs_ShouldInitializeCorrectly()
    {
        // Act
        var org = Organization.Create(
            "Test Clinic",
            "test-clinic",
            "contact@testclinic.com",
            "+905551112233",
            "Istanbul, Turkey",
            "Europe/Istanbul");

        // Assert
        org.Should().NotBeNull();
        org.Name.Should().Be("Test Clinic");
        org.Slug.Value.Should().Be("test-clinic");
        org.Email.Value.Should().Be("contact@testclinic.com");
        org.PhoneNumber.Value.Should().Be("+905551112233");
        org.TimeZone.Value.Should().Be("Europe/Istanbul");
        org.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Organization_Create_InvalidName_ShouldThrowDomainValidationException(string invalidName)
    {
        // Act
        var act = () => Organization.Create(
            invalidName,
            "test-clinic",
            "contact@testclinic.com",
            "+905551112233",
            "Address",
            "Europe/Istanbul");

        // Assert
        act.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Organization_SetWeeklySchedule_InvalidTimes_ShouldThrowDomainValidationException()
    {
        // Arrange
        var org = Organization.Create("Clinic", "clinic", "test@test.com", "+905551112233", "Address");

        // Act - End time earlier than start time
        var act = () => org.SetWeeklySchedule(DayOfWeek.Monday, TimeSpan.FromHours(17), TimeSpan.FromHours(9), 30);

        // Assert
        act.Should().Throw<DomainValidationException>()
            .WithMessage("*must be greater than start time*");
    }

    [Fact]
    public void AppointmentSlot_Create_PastDate_ShouldThrowDomainValidationException()
    {
        // Arrange
        var pastStart = DateTimeOffset.UtcNow.AddHours(-2);
        var pastEnd = pastStart.AddMinutes(30);

        // Act
        var act = () => AppointmentSlot.Create(Guid.NewGuid(), pastStart, pastEnd);

        // Assert
        act.Should().Throw<DomainValidationException>()
            .WithMessage("*past*");
    }

    [Fact]
    public void AppointmentSlot_BookingLifecycle_ShouldTransitionCorrectly()
    {
        // Arrange
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var end = start.AddMinutes(30);
        var slot = AppointmentSlot.Create(Guid.NewGuid(), start, end);

        // Assert initial
        slot.Status.Should().Be(SlotStatus.Available);

        // Act: Reserve
        var customerId = Guid.NewGuid();
        slot.Reserve(customerId, TimeSpan.FromMinutes(5));
        slot.Status.Should().Be(SlotStatus.Reserved);
        slot.DomainEvents.Should().ContainSingle(e => e is SlotReservedDomainEvent);

        // Act: Book
        slot.Book();
        slot.Status.Should().Be(SlotStatus.Booked);

        // Act: Cannot re-book an already booked slot
        var doubleBook = () => slot.Book();
        doubleBook.Should().Throw<SlotNotAvailableException>();

        // Act: Release to available
        slot.ReleaseToAvailable();
        slot.Status.Should().Be(SlotStatus.Available);
        slot.DomainEvents.Should().Contain(e => e is SlotReleasedDomainEvent);
    }

    [Fact]
    public void AppointmentSlot_Deactivate_WithActiveAppointment_WithoutForce_ShouldThrowException()
    {
        // Arrange
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var slot = AppointmentSlot.Create(Guid.NewGuid(), start, start.AddMinutes(30));

        // Act & Assert
        var act = () => slot.Deactivate(hasActiveAppointment: true, force: false);
        act.Should().Throw<ActiveAppointmentOnSlotException>();

        // Act with force
        slot.Deactivate(hasActiveAppointment: true, force: true);
        slot.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Appointment_CreateAndCancel_ShouldEmitCorrectEvents()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var slotId = Guid.NewGuid();

        // Act: Create
        var appointment = Appointment.Create(orgId, customerId, slotId, "Routine Checkup");

        // Assert
        appointment.Status.Should().Be(AppointmentStatus.Confirmed);
        appointment.AppointmentNumber.Value.Should().StartWith("APT-");
        appointment.DomainEvents.Should().ContainSingle(e => e is AppointmentCreatedDomainEvent);

        // Act: Cancel
        appointment.Cancel("Customer requested reschedule");
        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        appointment.CancellationReason.Should().Be("Customer requested reschedule");
        appointment.CancelledAt.Should().NotBeNull();
        appointment.DomainEvents.Should().Contain(e => e is AppointmentCancelledDomainEvent);
    }

    [Fact]
    public void WaitingQueueEntry_Lifecycle_ShouldFollowFIFOStateProgression()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var slotId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        // Act: Enqueue
        var queueEntry = WaitingQueueEntry.Enqueue(orgId, slotId, customerId, "req-123", priority: 1);
        queueEntry.Status.Should().Be(QueueStatus.Waiting);
        queueEntry.DomainEvents.Should().ContainSingle(e => e is CustomerAddedToWaitingQueueDomainEvent);

        // Act: Start processing
        queueEntry.StartProcessing();
        queueEntry.Status.Should().Be(QueueStatus.Processing);

        // Act: Reserve with lease
        queueEntry.Reserve(TimeSpan.FromMinutes(5));
        queueEntry.Status.Should().Be(QueueStatus.Reserved);
        queueEntry.ReservedUntil.Should().BeAfter(DateTimeOffset.UtcNow);
        queueEntry.DomainEvents.Should().Contain(e => e is QueueEntryReservedDomainEvent);

        // Act: Confirm
        queueEntry.Confirm();
        queueEntry.Status.Should().Be(QueueStatus.Confirmed);
    }

    [Fact]
    public void ValueObjects_TimeSlot_Overlaps_ShouldDetectOverlapAccurately()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.AddDays(1);
        var slotA = TimeSlot.Create(baseTime.AddHours(10), baseTime.AddHours(11));
        var slotB = TimeSlot.Create(baseTime.AddHours(10).AddMinutes(30), baseTime.AddHours(11).AddMinutes(30));
        var slotC = TimeSlot.Create(baseTime.AddHours(11), baseTime.AddHours(12));

        // Assert
        slotA.Overlaps(slotB).Should().BeTrue();
        slotB.Overlaps(slotA).Should().BeTrue();
        slotA.Overlaps(slotC).Should().BeFalse(); // Adjacent, not overlapping
    }
}
