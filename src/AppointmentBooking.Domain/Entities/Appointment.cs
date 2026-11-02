using AppointmentBooking.Domain.Common;
using AppointmentBooking.Domain.Enums;
using AppointmentBooking.Domain.Events;
using AppointmentBooking.Domain.Exceptions;
using AppointmentBooking.Domain.ValueObjects;

namespace AppointmentBooking.Domain.Entities;

public sealed class Appointment : AggregateRoot<Guid>, IMustHaveTenant, IAuditableEntity
{
    public Guid OrganizationId { get; set; }
    public Guid CustomerId { get; private set; }
    public Guid SlotId { get; private set; }
    public AppointmentNumber AppointmentNumber { get; private set; } = null!;
    public AppointmentStatus Status { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

    private Appointment() { }

    public static Appointment Create(
        Guid organizationId,
        Guid customerId,
        Guid slotId,
        string? description = null,
        bool requireConfirmation = false)
    {
        if (organizationId == Guid.Empty)
            throw new DomainValidationException("OrganizationId is required.");

        if (customerId == Guid.Empty)
            throw new DomainValidationException("CustomerId is required.");

        if (slotId == Guid.Empty)
            throw new DomainValidationException("SlotId is required.");

        var now = DateTimeOffset.UtcNow;
        var appointmentNumber = AppointmentNumber.Create(now);
        var initialStatus = requireConfirmation ? AppointmentStatus.Pending : AppointmentStatus.Confirmed;

        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            CustomerId = customerId,
            SlotId = slotId,
            AppointmentNumber = appointmentNumber,
            Status = initialStatus,
            Description = description?.Trim(),
            CreatedAt = now
        };

        appointment.AddDomainEvent(new AppointmentCreatedDomainEvent(
            appointment.Id,
            organizationId,
            customerId,
            slotId,
            appointmentNumber.Value,
            now));

        return appointment;
    }

    public void Confirm()
    {
        if (Status != AppointmentStatus.Pending)
            throw new InvalidAppointmentStateException($"Cannot confirm appointment with status '{Status}'. Only Pending appointments can be confirmed.");

        Status = AppointmentStatus.Confirmed;
        UpdatedAt = DateTimeOffset.UtcNow;

        AddDomainEvent(new AppointmentConfirmedDomainEvent(Id, OrganizationId, SlotId));
    }

    public void Cancel(string reason, DateTimeOffset? cancelledAt = null)
    {
        if (Status == AppointmentStatus.Cancelled)
            throw new InvalidAppointmentStateException("Appointment is already cancelled.");

        if (Status == AppointmentStatus.Completed)
            throw new InvalidAppointmentStateException("Cannot cancel an already completed appointment.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainValidationException("Cancellation reason must be provided.");

        Status = AppointmentStatus.Cancelled;
        CancelledAt = cancelledAt ?? DateTimeOffset.UtcNow;
        CancellationReason = reason.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;

        AddDomainEvent(new AppointmentCancelledDomainEvent(
            Id,
            OrganizationId,
            CustomerId,
            SlotId,
            CancellationReason,
            CancelledAt.Value));
    }

    public void Complete()
    {
        if (Status != AppointmentStatus.Confirmed)
            throw new InvalidAppointmentStateException($"Cannot complete an appointment with status '{Status}'. It must be Confirmed.");

        Status = AppointmentStatus.Completed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkNoShow()
    {
        if (Status != AppointmentStatus.Confirmed)
            throw new InvalidAppointmentStateException($"Cannot mark NoShow on appointment with status '{Status}'. It must be Confirmed.");

        Status = AppointmentStatus.NoShow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
