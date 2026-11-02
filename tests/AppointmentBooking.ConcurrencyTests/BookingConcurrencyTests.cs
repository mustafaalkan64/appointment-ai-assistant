using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using AppointmentBooking.Application.Features.Appointments.CancelAppointment;
using AppointmentBooking.Application.Features.Appointments.CreateAppointment;
using AppointmentBooking.Application.Features.Availability.GenerateSlots;
using AppointmentBooking.Application.Features.Organizations.RegisterOrganization;
using AppointmentBooking.Domain.Entities;
using AppointmentBooking.Domain.Enums;
using AppointmentBooking.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace AppointmentBooking.ConcurrencyTests;

public class BookingConcurrencyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly ITestOutputHelper _output;

    public BookingConcurrencyTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _output = output;
    }

    private async Task<(Guid OrganizationId, Guid SlotId)> SetupOrganizationAndSlotAsync()
    {
        var slug = "conc-org-" + Guid.NewGuid().ToString("N")[..8];
        var regCommand = new RegisterOrganizationCommand(
            Name: "Concurrency Clinic",
            Slug: slug,
            Email: $"{slug}@conc.com",
            PhoneNumber: "+905551239999",
            Address: "Test Street",
            TimeZone: "Europe/Istanbul",
            AdminEmail: $"admin@{slug}.com",
            AdminPassword: "Password123!",
            AdminFullName: "Admin");

        var regResp = await _client.PostAsJsonAsync("/api/organizations", regCommand);
        regResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var regContent = await regResp.Content.ReadFromJsonAsync<RegisterOrganizationResponse>();
        var orgId = regContent!.Id;

        // Create a slot directly in DB for precise timing
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var startUtc = DateTimeOffset.UtcNow.AddDays(5);
        var endUtc = startUtc.AddMinutes(30);

        var slot = AppointmentSlot.Create(orgId, startUtc, endUtc);
        context.AppointmentSlots.Add(slot);
        await context.SaveChangesAsync();

        return (orgId, slot.Id);
    }

    [Fact]
    public async Task Concurrency_10_SimultaneousRequests_ShouldYieldExactlyOneBooking()
    {
        await RunConcurrencyTestAsync(10);
    }

    [Fact]
    public async Task Concurrency_50_SimultaneousRequests_ZeroDoubleBooking()
    {
        await RunConcurrencyTestAsync(50);
    }

    [Fact]
    public async Task Concurrency_100_SimultaneousRequests_ZeroDoubleBooking()
    {
        await RunConcurrencyTestAsync(100);
    }

    [Fact]
    public async Task Concurrency_500_SimultaneousRequests_ZeroDoubleBooking()
    {
        await RunConcurrencyTestAsync(500);
    }

    private async Task RunConcurrencyTestAsync(int totalRequests)
    {
        var (orgId, slotId) = await SetupOrganizationAndSlotAsync();

        _output.WriteLine($"[START] Testing {totalRequests} simultaneous requests for Slot: {slotId}");

        var tasks = new List<Task<HttpResponseMessage>>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < totalRequests; i++)
        {
            var customerId = Guid.NewGuid();
            var idempotencyKey = $"req-{i}-{Guid.NewGuid():N}";
            var command = new CreateAppointmentCommand(orgId, customerId, slotId, idempotencyKey, $"Concurrent Client {i}");

            tasks.Add(Task.Run(() => _client.PostAsJsonAsync("/api/appointments", command)));
        }

        var responses = await Task.WhenAll(tasks);
        stopwatch.Stop();

        _output.WriteLine($"[DONE] {totalRequests} requests completed in {stopwatch.ElapsedMilliseconds}ms ({stopwatch.ElapsedMilliseconds / (double)totalRequests:F2}ms/req)");

        int createdCount = 0;
        int queuedCount = 0;
        int conflictCount = 0;

        foreach (var r in responses)
        {
            if (r.StatusCode == HttpStatusCode.Created)
            {
                createdCount++;
            }
            else if (r.StatusCode == HttpStatusCode.Accepted)
            {
                queuedCount++;
            }
            else if (r.StatusCode == HttpStatusCode.Conflict || r.StatusCode == HttpStatusCode.BadRequest)
            {
                conflictCount++;
            }
        }

        _output.WriteLine($"Results: Created={createdCount}, Queued={queuedCount}, Conflict={conflictCount}");

        // CRITICAL CONCURRENCY INVARIANT 1: Exactly ONE booking succeeded!
        createdCount.Should().Be(1, "Exactly one customer must acquire the slot, no more!");

        // CRITICAL CONCURRENCY INVARIANT 2: Database physically contains ONLY ONE active appointment!
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dbAppointments = await context.Appointments
            .IgnoreQueryFilters()
            .Where(a => a.SlotId == slotId && a.Status != AppointmentStatus.Cancelled)
            .ToListAsync();

        dbAppointments.Should().HaveCount(1, "Database partial unique index and distributed lock guarantee zero double-booking!");

        var slotInDb = await context.AppointmentSlots
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == slotId);
        slotInDb!.Status.Should().Be(SlotStatus.Booked);
    }

    [Fact]
    public async Task Concurrency_IdempotentRetries_SameCustomer_ShouldReturnSameBooking()
    {
        var (orgId, slotId) = await SetupOrganizationAndSlotAsync();
        var customerId = Guid.NewGuid();
        var sharedKey = $"idempotent-key-{Guid.NewGuid():N}";

        var tasks = new List<Task<HttpResponseMessage>>();

        // Customer fires 5 duplicate requests simultaneously (e.g. rapid double-clicking or network retries)
        for (int i = 0; i < 5; i++)
        {
            var command = new CreateAppointmentCommand(orgId, customerId, slotId, sharedKey, "Duplicate clicks");
            tasks.Add(Task.Run(() => _client.PostAsJsonAsync("/api/appointments", command)));
        }

        var responses = await Task.WhenAll(tasks);

        var successfulResponses = responses.Where(r => r.StatusCode == HttpStatusCode.Created).ToList();
        successfulResponses.Should().NotBeEmpty();

        var firstResult = await successfulResponses[0].Content.ReadFromJsonAsync<CreateAppointmentResult>();
        firstResult.Should().NotBeNull();

        // Database must have only 1 record
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var count = await context.Appointments.IgnoreQueryFilters().CountAsync(a => a.SlotId == slotId);
        count.Should().Be(1);
    }

    [Fact]
    public async Task Concurrency_SlotCancellation_ShouldReassignSlotToQueuedCustomer()
    {
        var (orgId, slotId) = await SetupOrganizationAndSlotAsync();

        var customerA = Guid.NewGuid();
        var keyA = $"key-a-{Guid.NewGuid():N}";
        var bookResp = await _client.PostAsJsonAsync("/api/appointments", new CreateAppointmentCommand(orgId, customerA, slotId, keyA));
        bookResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var apptResult = await bookResp.Content.ReadFromJsonAsync<CreateAppointmentResult>();

        // Customer B is in waiting queue
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var customerB = Guid.NewGuid();
            var queueEntry = WaitingQueueEntry.Enqueue(orgId, slotId, customerB, "queue-req-b");
            context.WaitingQueueEntries.Add(queueEntry);
            await context.SaveChangesAsync();
        }

        // Customer A cancels appointment
        var cancelCommand = new CancelAppointmentRequest(orgId, customerA, "Plans changed", $"cancel-key-{Guid.NewGuid():N}");
        var cancelResp = await _client.PostAsJsonAsync($"/api/appointments/{apptResult!.AppointmentId}/cancel", cancelCommand);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var cancelResult = await cancelResp.Content.ReadFromJsonAsync<CancelAppointmentResult>();
        cancelResult.Should().NotBeNull();
        cancelResult!.ReassignedToQueue.Should().BeTrue();

        // Check slot is now Reserved for the next customer
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var slot = await context.AppointmentSlots.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == slotId);
            slot!.Status.Should().Be(SlotStatus.Reserved);

            var activeQueue = await context.WaitingQueueEntries.IgnoreQueryFilters().FirstAsync(q => q.SlotId == slotId);
            activeQueue.Status.Should().Be(QueueStatus.Reserved);
        }
    }
}
