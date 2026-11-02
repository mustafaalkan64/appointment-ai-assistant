using AppointmentBooking.Domain.Common;

namespace AppointmentBooking.Domain.Events;

public sealed record AppointmentCreatedDomainEvent(
    Guid AppointmentId,
    Guid OrganizationId,
    Guid CustomerId,
    Guid SlotId,
    string AppointmentNumber,
    DateTimeOffset CreatedAt) : IDomainEvent;

public sealed record AppointmentCancelledDomainEvent(
    Guid AppointmentId,
    Guid OrganizationId,
    Guid CustomerId,
    Guid SlotId,
    string Reason,
    DateTimeOffset CancelledAt) : IDomainEvent;

public sealed record AppointmentConfirmedDomainEvent(
    Guid AppointmentId,
    Guid OrganizationId,
    Guid SlotId) : IDomainEvent;

public sealed record SlotReservedDomainEvent(
    Guid SlotId,
    Guid OrganizationId,
    Guid CustomerId,
    DateTimeOffset ExpiresAt) : IDomainEvent;

public sealed record SlotReleasedDomainEvent(
    Guid SlotId,
    Guid OrganizationId) : IDomainEvent;

public sealed record CustomerAddedToWaitingQueueDomainEvent(
    Guid QueueEntryId,
    Guid OrganizationId,
    Guid SlotId,
    Guid CustomerId,
    int Priority) : IDomainEvent;

public sealed record QueueEntryReservedDomainEvent(
    Guid QueueEntryId,
    Guid OrganizationId,
    Guid SlotId,
    Guid CustomerId,
    DateTimeOffset ReservedUntil) : IDomainEvent;
