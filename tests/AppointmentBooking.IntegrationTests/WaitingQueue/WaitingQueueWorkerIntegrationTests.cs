using AppointmentBooking.Application.Features.Organizations.RegisterOrganization;
using AppointmentBooking.Domain.Entities;
using AppointmentBooking.Domain.Enums;
using AppointmentBooking.Infrastructure.BackgroundJobs;
using AppointmentBooking.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AppointmentBooking.IntegrationTests.WaitingQueue;

public class WaitingQueueWorkerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WaitingQueueWorkerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Worker_ShouldExpireStaleReservation_AndPromoteNextWaitingCustomer()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<WaitingQueueWorker>>();

        // Setup test Organization and Slot
        var org = Organization.Create(
            "Queue Test Clinic",
            "queue-org-" + Guid.NewGuid().ToString("N")[..8],
            "queue@test.com",
            "+905559871122",
            "Queue St.",
            "Europe/Istanbul");

        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        var slotStart = DateTimeOffset.UtcNow.AddDays(2);
        var slot = AppointmentSlot.Create(org.Id, slotStart, slotStart.AddMinutes(30));
        context.AppointmentSlots.Add(slot);
        await context.SaveChangesAsync();

        // 1. Customer A: Expired reservation
        var customerAId = Guid.NewGuid();
        var entryA = WaitingQueueEntry.Enqueue(org.Id, slot.Id, customerAId, "req-A", priority: 1);
        entryA.StartProcessing();
        entryA.Reserve(TimeSpan.FromSeconds(-5)); // Expired in the past!
        slot.Reserve(customerAId, TimeSpan.FromSeconds(-5));

        // 2. Customer B: Waiting in queue
        var customerBId = Guid.NewGuid();
        var entryB = WaitingQueueEntry.Enqueue(org.Id, slot.Id, customerBId, "req-B", priority: 2);

        context.WaitingQueueEntries.AddRange(entryA, entryB);
        await context.SaveChangesAsync();

        // Instantiate worker manually to run single cycle
        var worker = new WaitingQueueWorker(scopeFactory, logger, TimeSpan.FromSeconds(5));

        // Act: Process expired reservations
        await worker.ProcessExpiredReservationsAsync(CancellationToken.None);

        // Assert
        using var verifyScope = _factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var updatedA = await verifyContext.WaitingQueueEntries
            .IgnoreQueryFilters()
            .FirstAsync(q => q.Id == entryA.Id);

        var updatedB = await verifyContext.WaitingQueueEntries
            .IgnoreQueryFilters()
            .FirstAsync(q => q.Id == entryB.Id);

        var updatedSlot = await verifyContext.AppointmentSlots
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == slot.Id);

        // Entry A should be marked Expired
        updatedA.Status.Should().Be(QueueStatus.Expired);

        // Entry B should have been promoted to Reserved with a future expiration
        updatedB.Status.Should().Be(QueueStatus.Reserved);
        updatedB.ReservedUntil.Should().BeAfter(DateTimeOffset.UtcNow);

        // Slot remains Reserved for Customer B
        updatedSlot.Status.Should().Be(SlotStatus.Reserved);
    }

    [Fact]
    public async Task Worker_WhenQueueEmpty_ShouldReleaseExpiredSlotToAvailable()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<WaitingQueueWorker>>();

        var org = Organization.Create(
            "Queue Solo Clinic",
            "solo-org-" + Guid.NewGuid().ToString("N")[..8],
            "solo@test.com",
            "+905559871133",
            "Solo St.",
            "Europe/Istanbul");

        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        var slotStart = DateTimeOffset.UtcNow.AddDays(3);
        var slot = AppointmentSlot.Create(org.Id, slotStart, slotStart.AddMinutes(30));
        context.AppointmentSlots.Add(slot);
        await context.SaveChangesAsync();

        var customerId = Guid.NewGuid();
        var entry = WaitingQueueEntry.Enqueue(org.Id, slot.Id, customerId, "req-solo", priority: 1);
        entry.StartProcessing();
        entry.Reserve(TimeSpan.FromSeconds(-10)); // Expired!
        slot.Reserve(customerId, TimeSpan.FromSeconds(-10));

        context.WaitingQueueEntries.Add(entry);
        await context.SaveChangesAsync();

        var worker = new WaitingQueueWorker(scopeFactory, logger, TimeSpan.FromSeconds(5));
        await worker.ProcessExpiredReservationsAsync(CancellationToken.None);

        // Assert: No other queue entries exist, slot must return to Available
        using var verifyScope = _factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var updatedEntry = await verifyContext.WaitingQueueEntries
            .IgnoreQueryFilters()
            .FirstAsync(q => q.Id == entry.Id);

        var updatedSlot = await verifyContext.AppointmentSlots
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == slot.Id);

        updatedEntry.Status.Should().Be(QueueStatus.Expired);
        updatedSlot.Status.Should().Be(SlotStatus.Available);
    }
}
