using AppointmentBooking.Domain.Common;
using AppointmentBooking.Domain.Enums;
using AppointmentBooking.Domain.Events;
using AppointmentBooking.Domain.Exceptions;

namespace AppointmentBooking.Domain.Entities;

public sealed class WaitingQueueEntry : AggregateRoot<Guid>, IMustHaveTenant, IAuditableEntity
{
    public Guid OrganizationId { get; set; }
    public Guid SlotId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string RequestId { get; private set; } = string.Empty;
    public int Priority { get; private set; }
    public QueueStatus Status { get; private set; }
    public DateTimeOffset? ReservedUntil { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int RetryCount { get; private set; }
    public string? FailureReason { get; private set; }

    private WaitingQueueEntry() { }

    public static WaitingQueueEntry Enqueue(
        Guid organizationId,
        Guid slotId,
        Guid customerId,
        string requestId,
        int priority = 0)
    {
        if (organizationId == Guid.Empty)
            throw new DomainValidationException("OrganizationId is required.");

        if (slotId == Guid.Empty)
            throw new DomainValidationException("SlotId is required.");

        if (customerId == Guid.Empty)
            throw new DomainValidationException("CustomerId is required.");

        var entry = new WaitingQueueEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            SlotId = slotId,
            CustomerId = customerId,
            RequestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId,
            Priority = priority,
            Status = QueueStatus.Waiting,
            CreatedAt = DateTimeOffset.UtcNow,
            RetryCount = 0
        };

        entry.AddDomainEvent(new CustomerAddedToWaitingQueueDomainEvent(
            entry.Id,
            organizationId,
            slotId,
            customerId,
            priority));

        return entry;
    }

    public void StartProcessing()
    {
        if (Status != QueueStatus.Waiting)
            throw new InvalidQueueStateException($"Cannot start processing queue entry in status '{Status}'. Must be Waiting.");

        Status = QueueStatus.Processing;
        ProcessedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Reserve(TimeSpan leaseDuration)
    {
        if (Status != QueueStatus.Processing && Status != QueueStatus.Waiting)
            throw new InvalidQueueStateException($"Cannot reserve queue entry in status '{Status}'.");

        Status = QueueStatus.Reserved;
        ReservedUntil = DateTimeOffset.UtcNow.Add(leaseDuration);
        UpdatedAt = DateTimeOffset.UtcNow;

        AddDomainEvent(new QueueEntryReservedDomainEvent(
            Id,
            OrganizationId,
            SlotId,
            CustomerId,
            ReservedUntil.Value));
    }

    public void Confirm()
    {
        if (Status != QueueStatus.Reserved && Status != QueueStatus.Processing)
            throw new InvalidQueueStateException($"Cannot confirm queue entry in status '{Status}'.");

        Status = QueueStatus.Confirmed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Expire()
    {
        if (Status != QueueStatus.Reserved)
            throw new InvalidQueueStateException($"Only Reserved entries can expire, but current status is '{Status}'.");

        Status = QueueStatus.Expired;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string reason)
    {
        Status = QueueStatus.Failed;
        FailureReason = reason?.Trim();
        RetryCount++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel()
    {
        if (Status == QueueStatus.Confirmed)
            throw new InvalidQueueStateException("Cannot cancel an already confirmed queue entry.");

        Status = QueueStatus.Cancelled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
