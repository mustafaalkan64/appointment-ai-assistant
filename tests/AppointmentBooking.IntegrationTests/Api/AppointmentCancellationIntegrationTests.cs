using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AppointmentBooking.Application.Features.Appointments.CancelAppointment;
using AppointmentBooking.Domain.Entities;
using AppointmentBooking.Domain.Enums;
using AppointmentBooking.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppointmentBooking.IntegrationTests.Api;

public class AppointmentCancellationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public AppointmentCancellationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CancelAppointment_WhenAppointmentTimeHasPassed_ShouldFailWithBadRequest()
    {
        // 1. Setup organization, customer, past slot, and confirmed appointment directly in DB
        Guid orgId;
        Guid custId;
        Guid apptId;

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var slug = "past-org-" + Guid.NewGuid().ToString("N")[..8];
            var org = Organization.Create("Past Clinic", slug, $"{slug}@clinic.com", "+90555" + Random.Shared.Next(1000000, 9999999), "Istanbul", "Europe/Istanbul");
            context.Organizations.Add(org);

            var customer = Customer.Create("Zaman Yolcusu", "+90555" + Random.Shared.Next(1000000, 9999999), $"{slug}@test.com");
            customer.LinkToOrganization(org.Id);
            context.Customers.Add(customer);

            // Create a valid slot and appointment first
            var slot = AppointmentSlot.Create(org.Id, DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow.AddMinutes(35));
            slot.Book();
            context.AppointmentSlots.Add(slot);

            // Create a confirmed appointment pointing to this slot
            var appt = Appointment.Create(org.Id, customer.Id, slot.Id, "Past Test Appointment", requireConfirmation: false);
            context.Appointments.Add(appt);

            await context.SaveChangesAsync();

            // Simulate that time has elapsed and the appointment is now in the past
            await context.Database.ExecuteSqlAsync(
                $"UPDATE \"AppointmentSlots\" SET \"StartUtc\" = {DateTimeOffset.UtcNow.AddHours(-2)}, \"EndUtc\" = {DateTimeOffset.UtcNow.AddHours(-1.5)} WHERE \"Id\" = {slot.Id}");

            orgId = org.Id;
            custId = customer.Id;
            apptId = appt.Id;
        }

        // 2. Try to cancel this past appointment via HTTP endpoint
        var cancelReq = new CancelAppointmentRequest(
            OrganizationId: orgId,
            CustomerId: custId,
            Reason: "Geçmiş randevuyu iptal etmeyi deniyorum",
            IdempotencyKey: $"cancel-past-{Guid.NewGuid():N}");

        var cancelResp = await _client.PostAsJsonAsync($"/api/appointments/{apptId}/cancel", cancelReq);

        // Assert: It must not be cancellable!
        cancelResp.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);

        var doc = await cancelResp.Content.ReadFromJsonAsync<JsonDocument>();
        doc.Should().NotBeNull();
        var detail = doc!.RootElement.GetProperty("detail").GetString();
        detail.Should().Be("Zamanı geçmiş randevular iptal edilemez.");
    }

    [Fact]
    public async Task CancelAppointment_WhenAppointmentIsInFuture_ShouldSucceedAndFreeSlot()
    {
        // 1. Setup organization, customer, future slot, and confirmed appointment in DB
        Guid orgId;
        Guid custId;
        Guid apptId;
        Guid slotId;

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var slug = "future-org-" + Guid.NewGuid().ToString("N")[..8];
            var org = Organization.Create("Future Clinic", slug, $"{slug}@clinic.com", "+90555" + Random.Shared.Next(1000000, 9999999), "Istanbul", "Europe/Istanbul");
            context.Organizations.Add(org);

            var customer = Customer.Create("Gelecek Müşteri", "+90555" + Random.Shared.Next(1000000, 9999999), $"{slug}@test.com");
            customer.LinkToOrganization(org.Id);
            context.Customers.Add(customer);

            // Create a slot in the future (e.g. 2 days from now)
            var futureStart = DateTimeOffset.UtcNow.AddDays(2);
            var futureEnd = futureStart.AddMinutes(30);
            var futureSlot = AppointmentSlot.Create(org.Id, futureStart, futureEnd);
            futureSlot.Book();
            context.AppointmentSlots.Add(futureSlot);

            // Create a confirmed appointment pointing to this future slot
            var appt = Appointment.Create(org.Id, customer.Id, futureSlot.Id, "Future Test Appointment", requireConfirmation: false);
            context.Appointments.Add(appt);

            await context.SaveChangesAsync();

            orgId = org.Id;
            custId = customer.Id;
            apptId = appt.Id;
            slotId = futureSlot.Id;
        }

        // 2. Cancel future appointment via HTTP endpoint
        var cancelReq = new CancelAppointmentRequest(
            OrganizationId: orgId,
            CustomerId: custId,
            Reason: "Gelecek randevumu iptal etmek istiyorum",
            IdempotencyKey: $"cancel-future-{Guid.NewGuid():N}");

        var cancelResp = await _client.PostAsJsonAsync($"/api/appointments/{apptId}/cancel", cancelReq);

        // Assert: Future cancellation must succeed
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify in DB that appointment is Cancelled and slot is freed (Available)
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var updatedAppt = await verifyDb.Appointments.IgnoreQueryFilters().FirstAsync(a => a.Id == apptId);
            updatedAppt.Status.Should().Be(AppointmentStatus.Cancelled);

            var updatedSlot = await verifyDb.AppointmentSlots.IgnoreQueryFilters().FirstAsync(s => s.Id == slotId);
            updatedSlot.Status.Should().Be(SlotStatus.Available);
        }
    }
}
