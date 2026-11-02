extern alias ChatApiAssembly;
extern alias McpServerAssembly;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AppointmentBooking.Domain.Entities;
using AppointmentBooking.Domain.Enums;
using AppointmentBooking.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ChatProgram = ChatApiAssembly::AppointmentBooking.ChatApi.ChatProgram;
using HttpMcpClient = ChatApiAssembly::AppointmentBooking.ChatApi.Services.HttpMcpClient;
using IMcpClient = ChatApiAssembly::AppointmentBooking.ChatApi.Services.IMcpClient;
using McpProgram = McpServerAssembly::AppointmentBooking.McpServer.McpProgram;

namespace AppointmentBooking.IntegrationTests.Chat;

public class ChatApiIntegrationTests : IClassFixture<WebApplicationFactory<ChatProgram>>, IClassFixture<WebApplicationFactory<McpProgram>>
{
    private readonly WebApplicationFactory<ChatProgram> _chatFactory;
    private readonly WebApplicationFactory<McpProgram> _mcpFactory;
    private readonly HttpClient _chatClient;

    public ChatApiIntegrationTests(
        WebApplicationFactory<ChatProgram> chatFactory,
        WebApplicationFactory<McpProgram> mcpFactory)
    {
        _mcpFactory = mcpFactory;

        // Route ChatApi's IMcpClient to McpServer's test HttpClient
        var mcpHttpClient = _mcpFactory.CreateClient();

        _chatFactory = chatFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // Replace IMcpClient with one pointing directly to McpServer's test server
                services.AddScoped<IMcpClient>(sp =>
                {
                    var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<HttpMcpClient>>();
                    return new HttpMcpClient(mcpHttpClient, logger);
                });
            });
        });

        _chatClient = _chatFactory.CreateClient();
    }

    [Fact]
    public async Task StartConversation_ShouldCreateConversationInDb()
    {
        var orgId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var response = await _chatClient.PostAsJsonAsync("/api/chat/conversations", new
        {
            OrganizationId = orgId,
            CustomerId = customerId
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        json.Should().NotBeNull();

        var convId = json!.RootElement.GetProperty("conversationId").GetGuid();
        convId.Should().NotBeEmpty();

        var message = json.RootElement.GetProperty("message").GetString();
        message.Should().Contain("Merhaba");
    }

    [Fact]
    public async Task MultiTurn_ConfirmationFlow_ShouldRequireConfirmationBeforeBooking()
    {
        // 1. Seed Organization & Slot
        using var scope = _chatFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var slug = "chat-clinic-" + Guid.NewGuid().ToString("N")[..8];
        var org = Organization.Create("Chat Clinic", slug, $"{slug}@test.com", "+905553334455", "Istanbul", "Europe/Istanbul");
        context.Organizations.Add(org);

        var startUtc = new DateTimeOffset(DateTime.UtcNow.AddDays(1).Date.AddHours(10), TimeSpan.Zero); // Tomorrow at 10:00 UTC (13:00 in Europe/Istanbul)
        var slot = AppointmentSlot.Create(org.Id, startUtc, startUtc.AddMinutes(30));
        context.AppointmentSlots.Add(slot);

        var uniquePhone = "+90555" + Random.Shared.Next(1000000, 9999999);
        var customer = Customer.Create("Ali Yilmaz", uniquePhone, $"{slug}-ali@test.com");
        customer.LinkToOrganization(org.Id);
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        // 2. Start Conversation
        var startResp = await _chatClient.PostAsJsonAsync("/api/chat/conversations", new
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id
        });
        var startDoc = await startResp.Content.ReadFromJsonAsync<JsonDocument>();
        var convId = startDoc!.RootElement.GetProperty("conversationId").GetGuid();

        // 3. User expresses intent to book 10:00 slot
        var msg1Resp = await _chatClient.PostAsJsonAsync("/api/chat/messages", new
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id,
            ConversationId = convId,
            Message = "13:00 için randevu almak istiyorum" // In Europe/Istanbul (UTC+3), 10:00 UTC is 13:00 local!
        });

        var errorBody = await msg1Resp.Content.ReadAsStringAsync();
        msg1Resp.StatusCode.Should().Be(HttpStatusCode.OK, errorBody);
        var msg1Doc = await msg1Resp.Content.ReadFromJsonAsync<JsonDocument>();

        // CRITICAL CONFIRMATION INVARIANT: Agent MUST propose and ask for confirmation FIRST!
        var requiresConf = msg1Doc!.RootElement.GetProperty("requiresConfirmation").GetBoolean();
        requiresConf.Should().BeTrue("AI Agent must strictly require user confirmation before executing booking!");

        var proposedAction = msg1Doc.RootElement.GetProperty("proposedAction").GetString();
        proposedAction.Should().Be("create_appointment");

        // Verify DB: No appointment must exist yet!
        using (var verifyScope = _chatFactory.Services.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var existingAppt = await verifyDb.Appointments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.SlotId == slot.Id);

            existingAppt.Should().BeNull("Slot must NOT be booked before customer confirms!");
        }

        // 4. Customer responds with explicit confirmation: "Evet, onaylıyorum"
        var confirmResp = await _chatClient.PostAsJsonAsync("/api/chat/messages", new
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id,
            ConversationId = convId,
            Message = "Evet, onaylıyorum"
        });

        confirmResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmDoc = await confirmResp.Content.ReadFromJsonAsync<JsonDocument>();

        var isCompleted = confirmDoc!.RootElement.GetProperty("isCompleted").GetBoolean();
        isCompleted.Should().BeTrue("Conversation booking flow should be completed after confirmation!");

        var confirmText = confirmDoc.RootElement.GetProperty("content").GetString();
        confirmText.Should().Contain("onaylandı ve oluşturuldu");

        // 5. Verify DB: Appointment now exists and slot is Booked!
        using (var verifyScope2 = _chatFactory.Services.CreateScope())
        {
            var verifyDb2 = verifyScope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var bookedAppt = await verifyDb2.Appointments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.SlotId == slot.Id);

            bookedAppt.Should().NotBeNull("Appointment must be safely created after confirmation!");
            bookedAppt!.Status.Should().Be(AppointmentStatus.Confirmed);

            var updatedSlot = await verifyDb2.AppointmentSlots
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == slot.Id);
            updatedSlot!.Status.Should().Be(SlotStatus.Booked);
        }
    }

    [Fact]
    public async Task MultiTurn_CancelFlow_WithMultipleAppointments_ShouldAskWhichAppointmentFirst()
    {
        // 1. Seed Organization & 2 Appointments for the customer
        using var scope = _chatFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var slug = "chat-cancel-" + Guid.NewGuid().ToString("N")[..8];
        var org = Organization.Create("Chat Clinic Cancel", slug, $"{slug}@test.com", "+905553334456", "Istanbul", "Europe/Istanbul");
        context.Organizations.Add(org);

        var startUtc1 = new DateTimeOffset(DateTime.UtcNow.AddDays(1).Date.AddHours(9), TimeSpan.Zero);
        var slot1 = AppointmentSlot.Create(org.Id, startUtc1, startUtc1.AddMinutes(30));
        slot1.Book();
        context.AppointmentSlots.Add(slot1);

        var startUtc2 = new DateTimeOffset(DateTime.UtcNow.AddDays(2).Date.AddHours(14), TimeSpan.Zero);
        var slot2 = AppointmentSlot.Create(org.Id, startUtc2, startUtc2.AddMinutes(30));
        slot2.Book();
        context.AppointmentSlots.Add(slot2);

        var uniquePhone = "+90555" + Random.Shared.Next(1000000, 9999999);
        var customer = Customer.Create("Ayse Demir", uniquePhone, $"{slug}-ayse@test.com");
        customer.LinkToOrganization(org.Id);
        context.Customers.Add(customer);

        var appt1 = Appointment.Create(org.Id, customer.Id, slot1.Id, "İlk muayene");
        var appt2 = Appointment.Create(org.Id, customer.Id, slot2.Id, "Kontrol");
        context.Appointments.AddRange(appt1, appt2);
        await context.SaveChangesAsync();

        // 2. Start Conversation
        var startResp = await _chatClient.PostAsJsonAsync("/api/chat/conversations", new
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id
        });
        var startDoc = await startResp.Content.ReadFromJsonAsync<JsonDocument>();
        var convId = startDoc!.RootElement.GetProperty("conversationId").GetGuid();

        // 3. User says: "Randevumu iptal et" without specifying which one
        var msg1Resp = await _chatClient.PostAsJsonAsync("/api/chat/messages", new
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id,
            ConversationId = convId,
            Message = "Randevumu iptal et"
        });

        msg1Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var msg1Doc = await msg1Resp.Content.ReadFromJsonAsync<JsonDocument>();

        // It must NOT blindly pick one and ask for confirmation yet; it must ask WHICH appointment!
        var msg1RequiresConf = msg1Doc!.RootElement.GetProperty("requiresConfirmation").GetBoolean();
        msg1RequiresConf.Should().BeFalse("Agent must first ask which appointment when multiple active appointments exist!");

        var msg1Content = msg1Doc.RootElement.GetProperty("content").GetString();
        msg1Content.Should().Contain("Hangi randevunuzu iptal etmek istersiniz?");
        msg1Content.Should().Contain(appt1.AppointmentNumber.Value);
        msg1Content.Should().Contain(appt2.AppointmentNumber.Value);

        // 4. User specifies the second appointment: "2."
        var msg2Resp = await _chatClient.PostAsJsonAsync("/api/chat/messages", new
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id,
            ConversationId = convId,
            Message = "2."
        });

        msg2Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var msg2Doc = await msg2Resp.Content.ReadFromJsonAsync<JsonDocument>();

        var msg2RequiresConf = msg2Doc!.RootElement.GetProperty("requiresConfirmation").GetBoolean();
        msg2RequiresConf.Should().BeTrue("After identifying which appointment, agent must ask for confirmation!");

        var msg2ProposedAction = msg2Doc.RootElement.GetProperty("proposedAction").GetString();
        msg2ProposedAction.Should().Be("cancel_appointment");

        var msg2Content = msg2Doc.RootElement.GetProperty("content").GetString();
        msg2Content.Should().Contain(appt2.AppointmentNumber.Value);
        msg2Content.Should().Contain("iptal etmek istediğinize emin misiniz?");

        // 5. User confirms cancellation: "Evet"
        var msg3Resp = await _chatClient.PostAsJsonAsync("/api/chat/messages", new
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id,
            ConversationId = convId,
            Message = "Evet"
        });

        msg3Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var msg3Doc = await msg3Resp.Content.ReadFromJsonAsync<JsonDocument>();

        var msg3IsCompleted = msg3Doc!.RootElement.GetProperty("isCompleted").GetBoolean();
        msg3IsCompleted.Should().BeTrue();

        var msg3Content = msg3Doc.RootElement.GetProperty("content").GetString();
        msg3Content.Should().Contain("iptal edildi");

        // Verify DB: appt2 cancelled, appt1 remains confirmed
        using (var verifyScope = _chatFactory.Services.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbAppt2 = await verifyDb.Appointments.IgnoreQueryFilters().FirstAsync(a => a.Id == appt2.Id);
            dbAppt2.Status.Should().Be(AppointmentStatus.Cancelled);

            var dbAppt1 = await verifyDb.Appointments.IgnoreQueryFilters().FirstAsync(a => a.Id == appt1.Id);
            dbAppt1.Status.Should().Be(AppointmentStatus.Confirmed);
        }
    }

    [Fact]
    public async Task MultiTurn_ConfirmFlow_WithMultipleAppointments_ShouldAskWhichAppointmentFirst()
    {
        // 1. Seed Organization & 2 Confirmed Appointments
        using var scope = _chatFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var slug = "chat-conf-" + Guid.NewGuid().ToString("N")[..8];
        var org = Organization.Create("Chat Clinic Confirm", slug, $"{slug}@test.com", "+905553334457", "Istanbul", "Europe/Istanbul");
        context.Organizations.Add(org);

        var startUtc1 = new DateTimeOffset(DateTime.UtcNow.AddDays(1).Date.AddHours(9), TimeSpan.Zero);
        var slot1 = AppointmentSlot.Create(org.Id, startUtc1, startUtc1.AddMinutes(30));
        slot1.Book();
        context.AppointmentSlots.Add(slot1);

        var startUtc2 = new DateTimeOffset(DateTime.UtcNow.AddDays(2).Date.AddHours(14), TimeSpan.Zero);
        var slot2 = AppointmentSlot.Create(org.Id, startUtc2, startUtc2.AddMinutes(30));
        slot2.Book();
        context.AppointmentSlots.Add(slot2);

        var uniquePhone = "+90555" + Random.Shared.Next(1000000, 9999999);
        var customer = Customer.Create("Fatma Sahin", uniquePhone, $"{slug}-fatma@test.com");
        customer.LinkToOrganization(org.Id);
        context.Customers.Add(customer);

        var appt1 = Appointment.Create(org.Id, customer.Id, slot1.Id, "İlk muayene");
        var appt2 = Appointment.Create(org.Id, customer.Id, slot2.Id, "Kontrol");
        context.Appointments.AddRange(appt1, appt2);
        await context.SaveChangesAsync();

        // 2. Start Conversation
        var startResp = await _chatClient.PostAsJsonAsync("/api/chat/conversations", new
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id
        });
        var startDoc = await startResp.Content.ReadFromJsonAsync<JsonDocument>();
        var convId = startDoc!.RootElement.GetProperty("conversationId").GetGuid();

        // 3. User says: "Randevumu onayla" without specifying which one
        var msg1Resp = await _chatClient.PostAsJsonAsync("/api/chat/messages", new
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id,
            ConversationId = convId,
            Message = "Randevumu onayla"
        });

        msg1Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var msg1Doc = await msg1Resp.Content.ReadFromJsonAsync<JsonDocument>();

        var msg1Content = msg1Doc!.RootElement.GetProperty("content").GetString();
        msg1Content.Should().Contain("Hangi randevunuzu onaylamak istersiniz?");
        msg1Content.Should().Contain(appt1.AppointmentNumber.Value);
        msg1Content.Should().Contain(appt2.AppointmentNumber.Value);

        // 4. User chooses the first appointment: "1"
        var msg2Resp = await _chatClient.PostAsJsonAsync("/api/chat/messages", new
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id,
            ConversationId = convId,
            Message = "1"
        });

        msg2Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var msg2Doc = await msg2Resp.Content.ReadFromJsonAsync<JsonDocument>();

        var msg2Content = msg2Doc!.RootElement.GetProperty("content").GetString();
        msg2Content.Should().Contain("zaten onaylanmış durumdadır");
        msg2Content.Should().Contain(appt1.AppointmentNumber.Value);
    }

    [Fact]
    public async Task CancelFlow_WithExplicitDateAndTime_ShouldTargetExactAppointment_NotConfusedByDayNumber()
    {
        using var scope = _chatFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var slug = "chat-datecancel-" + Guid.NewGuid().ToString("N")[..8];
        var org = Organization.Create("Clinic Date Cancel", slug, $"{slug}@test.com", "+905553334457", "Istanbul", "Europe/Istanbul");
        context.Organizations.Add(org);

        var targetDate = DateTime.UtcNow.Date.AddDays(1);
        var startUtc1 = new DateTimeOffset(targetDate.AddHours(9), TimeSpan.Zero); // 12:00 in UTC+3
        var slot1 = AppointmentSlot.Create(org.Id, startUtc1, startUtc1.AddMinutes(30));
        slot1.Book();

        var startUtc2 = new DateTimeOffset(targetDate.AddHours(11), TimeSpan.Zero); // 14:00 in UTC+3
        var slot2 = AppointmentSlot.Create(org.Id, startUtc2, startUtc2.AddMinutes(30));
        slot2.Book();

        context.AppointmentSlots.AddRange(slot1, slot2);

        var uniquePhone = "+90555" + Random.Shared.Next(1000000, 9999999);
        var customer = Customer.Create("Zeynep Kaya", uniquePhone, $"{slug}-zeynep@test.com");
        customer.LinkToOrganization(org.Id);
        context.Customers.Add(customer);

        var appt1 = Appointment.Create(org.Id, customer.Id, slot1.Id, "Kontrol 12:00");
        var appt2 = Appointment.Create(org.Id, customer.Id, slot2.Id, "Kontrol 14:00");
        context.Appointments.AddRange(appt1, appt2);
        await context.SaveChangesAsync();

        var startResp = await _chatClient.PostAsJsonAsync("/api/chat/conversations", new
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id
        });
        var startDoc = await startResp.Content.ReadFromJsonAsync<JsonDocument>();
        var convId = startDoc!.RootElement.GetProperty("conversationId").GetGuid();

        // Target appointment 2 (14:00) using date + time
        string[] trMonths = { "", "ocak", "şubat", "mart", "nisan", "mayıs", "haziran", "temmuz", "ağustos", "eylül", "ekim", "kasım", "aralık" };
        var monthName = trMonths[targetDate.Month];
        var msg = $"{targetDate.Day} {monthName} saat 14.00 randevumu iptal et";

        var msgResp = await _chatClient.PostAsJsonAsync("/api/chat/messages", new
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id,
            ConversationId = convId,
            Message = msg
        });

        msgResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var msgDoc = await msgResp.Content.ReadFromJsonAsync<JsonDocument>();

        var requiresConf = msgDoc!.RootElement.GetProperty("requiresConfirmation").GetBoolean();
        requiresConf.Should().BeTrue();

        var proposedAction = msgDoc.RootElement.GetProperty("proposedAction").GetString();
        proposedAction.Should().Be("cancel_appointment");

        var content = msgDoc.RootElement.GetProperty("content").GetString();
        content.Should().Contain(appt2.AppointmentNumber.Value, "Must match 14:00 appointment, not 12:00 appointment even if day number matches index 1 or 2!");
        content.Should().NotContain(appt1.AppointmentNumber.Value);
    }

    [Fact]
    public async Task WorkingHours_WhenSundayIsClosed_ShouldStateSundayClosed_EvenIfSlotsExist()
    {
        using var scope = _chatFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var slug = "chat-hours-" + Guid.NewGuid().ToString("N")[..8];
        var org = Organization.Create("Mustafa ALKAN Clinic", slug, $"{slug}@test.com", "+905553334458", "Istanbul", "Europe/Istanbul");

        // Monday-Friday active, Saturday active, Sunday CLOSED
        org.SetWeeklySchedule(DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(17), 30);
        org.SetWeeklySchedule(DayOfWeek.Tuesday, TimeSpan.FromHours(9), TimeSpan.FromHours(17), 30);
        org.SetWeeklySchedule(DayOfWeek.Wednesday, TimeSpan.FromHours(9), TimeSpan.FromHours(17), 30);
        org.SetWeeklySchedule(DayOfWeek.Thursday, TimeSpan.FromHours(9), TimeSpan.FromHours(17), 30);
        org.SetWeeklySchedule(DayOfWeek.Friday, TimeSpan.FromHours(9), TimeSpan.FromHours(16), 30);
        org.SetWeeklySchedule(DayOfWeek.Saturday, TimeSpan.FromHours(10), TimeSpan.FromHours(14), 30);

        // Explicitly set and close Sunday
        org.SetWeeklySchedule(DayOfWeek.Sunday, TimeSpan.FromHours(10), TimeSpan.FromHours(15), 30);
        org.CloseDayOfWeek(DayOfWeek.Sunday);

        context.Organizations.Add(org);

        // Intentionally add an old Sunday slot in database (replicating stale slots)
        var nextSunday = DateTime.UtcNow.Date;
        while (nextSunday.DayOfWeek != DayOfWeek.Sunday) nextSunday = nextSunday.AddDays(1);
        var sundayStartUtc = new DateTimeOffset(nextSunday.AddHours(7), TimeSpan.Zero); // 10:00 UTC+3
        var staleSundaySlot = AppointmentSlot.Create(org.Id, sundayStartUtc, sundayStartUtc.AddMinutes(30));
        context.AppointmentSlots.Add(staleSundaySlot);

        var uniquePhone = "+90555" + Random.Shared.Next(1000000, 9999999);
        var customer = Customer.Create("Ali Can", uniquePhone, $"{slug}-ali@test.com");
        customer.LinkToOrganization(org.Id);
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var startResp = await _chatClient.PostAsJsonAsync("/api/chat/conversations", new
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id
        });
        var startDoc = await startResp.Content.ReadFromJsonAsync<JsonDocument>();
        var convId = startDoc!.RootElement.GetProperty("conversationId").GetGuid();

        // 1. User asks general working hours: "çalışma saatleri ve günler neler"
        var msgResp = await _chatClient.PostAsJsonAsync("/api/chat/messages", new
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id,
            ConversationId = convId,
            Message = "çalışma saatleri ve günler neler"
        });

        msgResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var msgDoc = await msgResp.Content.ReadFromJsonAsync<JsonDocument>();
        var content = msgDoc!.RootElement.GetProperty("content").GetString()!;

        content.Should().Contain("- Pazar: Kapalı", "Sunday was closed by admin, so it must state Kapalı!");
        content.Should().NotContain("- Pazar: 10:00 - 15:00", "Stale slots must not override closed Sunday schedule!");

        // 2. User asks specifically about Sunday: "Pazar günü açık mısınız"
        var msgSundayResp = await _chatClient.PostAsJsonAsync("/api/chat/messages", new
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id,
            ConversationId = convId,
            Message = "Pazar günü açık mısınız"
        });

        msgSundayResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var msgSundayDoc = await msgSundayResp.Content.ReadFromJsonAsync<JsonDocument>();
        var sundayContent = msgSundayDoc!.RootElement.GetProperty("content").GetString()!;

        sundayContent.Should().Contain("Pazar günleri kapalıdır", "Agent must explicitly state Sunday is closed when asked directly!");
    }
}
