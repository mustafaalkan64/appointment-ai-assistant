using AppointmentBooking.Domain.Entities;
using AppointmentBooking.Domain.Enums;
using AppointmentBooking.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace AppointmentBooking.UnitTests.Application;

public class WaitingQueueUnitTests
{
    [Fact]
    public void Enqueue_ValidInput_ShouldCreateWaitingEntryWithDomainEvent()
    {
        var orgId = Guid.NewGuid();
        var slotId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var entry = WaitingQueueEntry.Enqueue(orgId, slotId, customerId, "req-123", priority: 5);

        entry.Should().NotBeNull();
        entry.OrganizationId.Should().Be(orgId);
        entry.SlotId.Should().Be(slotId);
        entry.CustomerId.Should().Be(customerId);
        entry.RequestId.Should().Be("req-123");
        entry.Priority.Should().Be(5);
        entry.Status.Should().Be(QueueStatus.Waiting);
        entry.DomainEvents.Should().HaveCount(1);
    }

    [Fact]
    public void Lifecycle_WaitingToReservedToConfirmed_ShouldTransitionCorrectly()
    {
        var entry = WaitingQueueEntry.Enqueue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "req-1");

        entry.StartProcessing();
        entry.Status.Should().Be(QueueStatus.Processing);
        entry.ProcessedAt.Should().NotBeNull();

        entry.Reserve(TimeSpan.FromMinutes(5));
        entry.Status.Should().Be(QueueStatus.Reserved);
        entry.ReservedUntil.Should().BeAfter(DateTimeOffset.UtcNow);

        entry.Confirm();
        entry.Status.Should().Be(QueueStatus.Confirmed);
    }

    [Fact]
    public void Lifecycle_ReservedToExpired_ShouldTransitionCorrectly()
    {
        var entry = WaitingQueueEntry.Enqueue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "req-2");
        entry.StartProcessing();
        entry.Reserve(TimeSpan.FromMinutes(1));

        entry.Expire();
        entry.Status.Should().Be(QueueStatus.Expired);
    }

    [Fact]
    public void Cancel_ConfirmedEntry_ShouldThrowInvalidQueueStateException()
    {
        var entry = WaitingQueueEntry.Enqueue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "req-3");
        entry.StartProcessing();
        entry.Reserve(TimeSpan.FromMinutes(5));
        entry.Confirm();

        var act = () => entry.Cancel();
        act.Should().Throw<InvalidQueueStateException>()
            .WithMessage("*Cannot cancel an already confirmed queue entry.*");
    }

    [Fact]
    public void Expire_WaitingEntry_ShouldThrowInvalidQueueStateException()
    {
        var entry = WaitingQueueEntry.Enqueue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "req-4");

        var act = () => entry.Expire();
        act.Should().Throw<InvalidQueueStateException>()
            .WithMessage("*Only Reserved entries can expire*");
    }
}
