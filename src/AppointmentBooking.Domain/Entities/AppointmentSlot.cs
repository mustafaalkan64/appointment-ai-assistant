using AppointmentBooking.Domain.Common;
using AppointmentBooking.Domain.Enums;
using AppointmentBooking.Domain.Events;
using AppointmentBooking.Domain.Exceptions;
using AppointmentBooking.Domain.ValueObjects;

namespace AppointmentBooking.Domain.Entities;

public sealed class AppointmentSlot : AggregateRoot<Guid>, IMustHaveTenant, IAuditableEntity
{
    public Guid OrganizationId { get; set; }
    public TimeSlot TimeSlot { get; private set; } = null!;
    public SlotStatus Status { get; private set; }
    public bool IsActive { get; private set; }
    public uint Version { get; private set; } // Concurrency token mapping to PostgreSQL xmin
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    private AppointmentSlot() { }

    public static AppointmentSlot Create(
        Guid organizationId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        if (startUtc < DateTimeOffset.UtcNow.AddMinutes(-5))
            throw new DomainValidationException("Cannot create appointment slots in the past.");

        var timeSlot = TimeSlot.Create(startUtc, endUtc);

        var slot = new AppointmentSlot
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            TimeSlot = timeSlot,
            Status = SlotStatus.Available,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        return slot;
    }

    public void Reserve(Guid customerId, TimeSpan reservationDuration)
    {
        if (!IsActive)
            throw new SlotNotAvailableException(Id, "Inactive");

        if (Status != SlotStatus.Available && Status != SlotStatus.Reserved)
            throw new SlotNotAvailableException(Id, Status.ToString());

        Status = SlotStatus.Reserved;
        UpdatedAt = DateTimeOffset.UtcNow;

        var expiresAt = DateTimeOffset.UtcNow.Add(reservationDuration);
        AddDomainEvent(new SlotReservedDomainEvent(Id, OrganizationId, customerId, expiresAt));
    }

    public void Book()
    {
        if (!IsActive)
            throw new SlotNotAvailableException(Id, "Inactive");

        if (Status != SlotStatus.Available && Status != SlotStatus.Reserved)
            throw new SlotNotAvailableException(Id, Status.ToString());

        Status = SlotStatus.Booked;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ReleaseToAvailable()
    {
        if (Status == SlotStatus.Blocked)
            throw new InvalidAppointmentStateException("Cannot release a blocked slot directly to available without unblocking.");

        Status = SlotStatus.Available;
        UpdatedAt = DateTimeOffset.UtcNow;
        AddDomainEvent(new SlotReleasedDomainEvent(Id, OrganizationId));
    }

    public void Block()
    {
        if (Status == SlotStatus.Booked)
            throw new InvalidAppointmentStateException("Cannot block a slot that has an active booking. Cancel the appointment first.");

        Status = SlotStatus.Blocked;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Unblock()
    {
        if (Status != SlotStatus.Blocked)
            throw new InvalidAppointmentStateException("Slot is not blocked.");

        Status = SlotStatus.Available;
        UpdatedAt = DateTimeOffset.UtcNow;
        AddDomainEvent(new SlotReleasedDomainEvent(Id, OrganizationId));
    }

    public void Deactivate(bool hasActiveAppointment = false, bool force = false)
    {
        if (hasActiveAppointment && !force)
            throw new ActiveAppointmentOnSlotException(Id);

        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
