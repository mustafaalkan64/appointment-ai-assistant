using System.Text.Json;
using System.Text.RegularExpressions;
using AppointmentBooking.Application.Abstractions.Persistence;
using AppointmentBooking.Application.Abstractions.Tenancy;
using AppointmentBooking.Domain.Entities;
using AppointmentBooking.Domain.Enums;
using AppointmentBooking.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AppointmentBooking.ChatApi.Services;

public interface IAiAgentOrchestrator
{
    Task<AgentResponseDto> ProcessUserMessageAsync(
        Guid organizationId,
        Guid customerId,
        Guid? conversationId,
        string messageText,
        CancellationToken ct = default);
}

public sealed record AgentResponseDto(
    Guid ConversationId,
    Guid MessageId,
    string Role,
    string Content,
    bool RequiresConfirmation,
    string? ProposedAction,
    string? Summary,
    bool IsCompleted);

public sealed record PendingBookingContext(
    Guid OrganizationId,
    Guid CustomerId,
    Guid SlotId,
    string LocalDisplay,
    string? Note);

public sealed class AiAgentOrchestrator : IAiAgentOrchestrator
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IMcpClient _mcpClient;
    private readonly ILogger<AiAgentOrchestrator> _logger;

    public AiAgentOrchestrator(
        IApplicationDbContext context,
        ITenantContext tenantContext,
        IMcpClient mcpClient,
        ILogger<AiAgentOrchestrator> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _mcpClient = mcpClient;
        _logger = logger;
    }

    public async Task<AgentResponseDto> ProcessUserMessageAsync(
        Guid organizationId,
        Guid customerId,
        Guid? conversationId,
        string messageText,
        CancellationToken ct = default)
    {
        _tenantContext.SetTenant(organizationId);

        var organization = await _context.Organizations
            .Include(o => o.WeeklyAvailabilities)
            .Include(o => o.SpecialAvailabilities)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        var tzId = organization?.TimeZone?.Value ?? "Europe/Istanbul";
        TimeZoneInfo orgTimeZone;
        try
        {
            orgTimeZone = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        }
        catch
        {
            orgTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, orgTimeZone);

        Conversation conversation;
        if (conversationId.HasValue && conversationId.Value != Guid.Empty)
        {
            conversation = await _context.Conversations
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.Id == conversationId.Value && c.OrganizationId == organizationId, ct)
                ?? throw new DomainValidationException($"Conversation '{conversationId.Value}' not found.");
        }
        else
        {
            conversation = Conversation.Start(organizationId, customerId);
            _context.Conversations.Add(conversation);
        }

        // Add user message
        conversation.AddMessage(MessageRole.User, messageText);

        var normalized = messageText.Trim().ToLowerInvariant();
        AgentResponseDto response;

        // -------------------------------------------------------------
        // STATE 1: Awaiting Confirmation
        // -------------------------------------------------------------
        if (conversation.Status == ConversationStatus.AwaitingConfirmation)
        {
            response = await HandleConfirmationResponseAsync(conversation, normalized, messageText, organization, nowLocal, ct);
        }
        else
        {
            // ---------------------------------------------------------
            // STATE 2: Intent Classification & MCP Tool Execution
            // ---------------------------------------------------------
            response = await HandleGeneralMessageAsync(conversation, normalized, messageText, organization, nowLocal, ct);
        }

        await _context.SaveChangesAsync(ct);
        return response;
    }

    private async Task<AgentResponseDto> HandleConfirmationResponseAsync(
        Conversation conversation,
        string normalized,
        string originalMessage,
        Organization? organization,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var isAffirmative = normalized.Contains("evet") ||
                            normalized.Contains("onayla") ||
                            normalized.Contains("tamam") ||
                            normalized.Contains("yes") ||
                            normalized.Contains("confirm") ||
                            normalized.Contains("olur");

        var isNegative = normalized.Contains("hayır") ||
                         normalized.Contains("vazgeç") ||
                         normalized.Contains("iptal") ||
                         normalized.Contains("no") ||
                         normalized.Contains("cancel");

        if (isAffirmative)
        {
            if (conversation.PendingOperation == "create_appointment")
            {
                var context = JsonSerializer.Deserialize<PendingBookingContext>(conversation.ContextJson);
                if (context == null || context.SlotId == Guid.Empty)
                {
                    conversation.ClearPendingConfirmation();
                    var errMsg = "Randevu bilgileri doğrulanamadı. Lütfen baştan başlayınız.";
                    var errMessage = conversation.AddMessage(MessageRole.Assistant, errMsg);
                    return new AgentResponseDto(conversation.Id, errMessage.Id, "assistant", errMsg, false, null, null, false);
                }

                var idempotencyKey = $"chat-{conversation.Id:N}-{context.SlotId:N}";
                var mcpResult = await _mcpClient.CallToolAsync("create_appointment", new
                {
                    organization_id = context.OrganizationId,
                    customer_id = context.CustomerId,
                    slot_id = context.SlotId,
                    idempotency_key = idempotencyKey,
                    note = context.Note ?? "Chat API ile oluşturuldu"
                }, ct);

                if (mcpResult.Success)
                {
                    conversation.Complete();
                    var successText = $"Randevunuz başarıyla onaylandı ve oluşturuldu! Saat: {context.LocalDisplay}. Sistemde kaydınız oluşturulmuştur.";
                    var assistantMsg = conversation.AddMessage(MessageRole.Assistant, successText, "AppointmentConfirmed");

                    return new AgentResponseDto(
                        conversation.Id,
                        assistantMsg.Id,
                        "assistant",
                        successText,
                        RequiresConfirmation: false,
                        ProposedAction: null,
                        Summary: "Randevu Onaylandı",
                        IsCompleted: true);
                }
                else
                {
                    conversation.ClearPendingConfirmation();
                    var failText = $"Randevu oluşturulamadı: {mcpResult.ErrorMessage}. Dilerseniz başka bir saat için kontrol edebilirim.";
                    var assistantMsg = conversation.AddMessage(MessageRole.Assistant, failText, "BookingFailed");

                    return new AgentResponseDto(conversation.Id, assistantMsg.Id, "assistant", failText, false, null, null, false);
                }
            }
            else if (conversation.PendingOperation == "cancel_appointment")
            {
                var doc = JsonDocument.Parse(conversation.ContextJson);
                var apptId = doc.RootElement.GetProperty("AppointmentId").GetGuid();
                var idemKey = $"chat-cancel-{conversation.Id:N}-{apptId:N}";

                var mcpResult = await _mcpClient.CallToolAsync("cancel_appointment", new
                {
                    organization_id = conversation.OrganizationId,
                    appointment_id = apptId,
                    customer_id = conversation.CustomerId,
                    reason = "Müşteri Chat API üzerinden iptal etti",
                    idempotency_key = idemKey
                }, ct);

                conversation.Complete();
                var text = mcpResult.Success
                    ? "Randevunuz başarıyla iptal edildi. Randevu saati serbest bırakıldı."
                    : $"İptal işlemi tamamlanamadı: {mcpResult.ErrorMessage}";

                var assistantMsg = conversation.AddMessage(MessageRole.Assistant, text, "AppointmentCancelled");
                return new AgentResponseDto(conversation.Id, assistantMsg.Id, "assistant", text, false, null, null, true);
            }
            else if (conversation.PendingOperation == "confirm_appointment")
            {
                var doc = JsonDocument.Parse(conversation.ContextJson);
                var apptId = doc.RootElement.GetProperty("AppointmentId").GetGuid();
                var idemKey = $"chat-confirm-{conversation.Id:N}-{apptId:N}";

                var mcpResult = await _mcpClient.CallToolAsync("confirm_appointment", new
                {
                    organization_id = conversation.OrganizationId,
                    appointment_id = apptId,
                    customer_id = conversation.CustomerId,
                    idempotency_key = idemKey
                }, ct);

                conversation.Complete();
                var text = mcpResult.Success
                    ? "Randevunuz başarıyla onaylandı."
                    : $"Onay işlemi tamamlanamadı: {mcpResult.ErrorMessage}";

                var assistantMsg = conversation.AddMessage(MessageRole.Assistant, text, "AppointmentConfirmed");
                return new AgentResponseDto(conversation.Id, assistantMsg.Id, "assistant", text, false, null, null, true);
            }
        }
        else if (isNegative)
        {
            conversation.ClearPendingConfirmation();

            // If the user rejected this confirmation but simultaneously specified another slot/hour/appointment or correction
            if (normalized.Contains("başka") || normalized.Contains("farklı") || normalized.Contains("hangi") ||
                normalized.Contains("saat") || normalized.Contains("randevu") || normalized.Contains("iptal") ||
                normalized.Contains("onayla") || normalized.Contains("numara") || normalized.Contains("id") ||
                normalized.Contains("apt-") || TryExtractTime(normalized) != null || ResolveTargetDate(normalized, nowLocal) != null)
            {
                return await HandleGeneralMessageAsync(conversation, normalized, originalMessage, organization, nowLocal, ct);
            }

            var text = "İşleminiz iptal edildi. Size başka bir randevu konusunda nasıl yardımcı olabilirim?";
            var msg = conversation.AddMessage(MessageRole.Assistant, text, "ConfirmationRejected");
            return new AgentResponseDto(conversation.Id, msg.Id, "assistant", text, false, null, null, false);
        }

        // Unrecognized response during confirmation
        var clarifyText = "Lütfen onaylıyor musunuz belirtiniz (Evet / Hayır).";
        var clarifyMsg = conversation.AddMessage(MessageRole.Assistant, clarifyText, "ClarificationNeeded");
        return new AgentResponseDto(conversation.Id, clarifyMsg.Id, "assistant", clarifyText, true, conversation.PendingOperation, null, false);
    }

    private async Task<AgentResponseDto> HandleGeneralMessageAsync(
        Conversation conversation,
        string normalized,
        string originalMessage,
        Organization? organization,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var lastAssistantMsg = conversation.Messages
            .Where(m => m.Role == MessageRole.Assistant)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefault();

        bool answeringWhichToCancel = lastAssistantMsg?.Intent == "AskWhichAppointmentToCancel";
        bool answeringWhichToConfirm = lastAssistantMsg?.Intent == "AskWhichAppointmentToConfirm";

        // If user changed mind / cancelled the disambiguation action
        if ((answeringWhichToCancel || answeringWhichToConfirm) &&
            (normalized == "vazgeç" || normalized == "vazgectim" || normalized == "hayır" || normalized == "istemiyorum" || normalized == "boşver" || normalized == "gerek yok" ||
             Regex.IsMatch(normalized, @"\b(vazgeç|iptal\s+etme|boşver|bosver)\b")) &&
            !normalized.Contains("randevu") && TryExtractTime(normalized) == null && ResolveTargetDate(normalized, nowLocal) == null)
        {
            var cancelReply = "İşleminiz iptal edildi. Size başka nasıl yardımcı olabilirim?";
            var cancelMsg = conversation.AddMessage(MessageRole.Assistant, cancelReply, "ActionCancelled");
            return new AgentResponseDto(conversation.Id, cancelMsg.Id, "assistant", cancelReply, false, null, null, false);
        }

        bool isCancelIntent = (normalized.Contains("iptal") || normalized.Contains("cancel")) &&
                              !Regex.IsMatch(normalized, @"\biptal\s+etme\b") &&
                              !normalized.Contains("iptal istemiyorum");

        bool isConfirmIntent = normalized.Contains("onayla") ||
                               normalized.Contains("onaylamak") ||
                               normalized.Contains("confirm") ||
                               normalized.Contains("onay ver");

        bool handleCancel = isCancelIntent || answeringWhichToCancel;
        bool handleConfirm = !handleCancel && (isConfirmIntent || answeringWhichToConfirm);

        // 1. Check if user is asking for appointment cancellation (or answering which appointment to cancel)
        if (handleCancel)
        {
            var apptsResult = await _mcpClient.CallToolAsync("get_customer_appointments", new
            {
                organization_id = conversation.OrganizationId,
                customer_id = conversation.CustomerId
            }, ct);

            List<JsonElement> apptsArray = new();
            if (apptsResult.Success && !string.IsNullOrWhiteSpace(apptsResult.Content))
            {
                var doc = JsonDocument.Parse(apptsResult.Content);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    apptsArray = doc.RootElement.EnumerateArray().ToList();
                }
                else if (TryGetProp(doc.RootElement, "Appointments", out var aProp) && aProp.ValueKind == JsonValueKind.Array)
                {
                    apptsArray = aProp.EnumerateArray().ToList();
                }
            }

            var activeAppts = apptsArray.Where(a =>
            {
                var status = TryGetProp(a, "Status", out var st) ? st.GetString() : "";
                var isCancelled = string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase);
                var isCompleted = string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase);
                var isNoShow = string.Equals(status, "NoShow", StringComparison.OrdinalIgnoreCase);
                if (isCancelled || isCompleted || isNoShow) return false;

                // Also exclude appointments whose time has already passed
                if (TryGetProp(a, "StartUtc", out var su) && su.TryGetDateTimeOffset(out var startDto))
                {
                    if (startDto <= DateTimeOffset.UtcNow) return false;
                }
                return true;
            }).OrderBy(a =>
            {
                if (TryGetProp(a, "StartUtc", out var su) && su.TryGetDateTimeOffset(out var dto))
                    return dto;
                return DateTimeOffset.MaxValue;
            }).ToList();

            if (activeAppts.Count == 0)
            {
                var noApptText = "İptal edilecek aktif bir randevunuz bulunmamaktadır.";
                var noApptMsg = conversation.AddMessage(MessageRole.Assistant, noApptText, "NoActiveAppointment");
                return new AgentResponseDto(conversation.Id, noApptMsg.Id, "assistant", noApptText, false, null, null, false);
            }

            var matchedAppt = FindMatchingAppointment(activeAppts, normalized, nowLocal);

            if (matchedAppt == null)
            {
                if (activeAppts.Count > 1)
                {
                    var listItems = activeAppts.Select((a, idx) =>
                    {
                        var num = TryGetProp(a, "AppointmentNumber", out var np) ? np.GetString() : "APT";
                        var time = TryGetProp(a, "LocalDisplay", out var tp) ? tp.GetString() : "";
                        var status = TryGetProp(a, "Status", out var sp) ? sp.GetString() : "";
                        var statusTr = TranslateStatus(status);
                        return $"{idx + 1}. {num}: {time} ({statusTr})";
                    });

                    var prefix = answeringWhichToCancel
                        ? "Belirttiğiniz bilgilerle eşleşen aktif bir randevu bulunamadı.\n"
                        : "";

                    var reply = $"{prefix}Hangi randevunuzu iptal etmek istersiniz?\nKayıtlı aktif randevularınız:\n{string.Join("\n", listItems)}\n\nLütfen iptal etmek istediğiniz randevunun numarasını, tarihini veya saatini belirtiniz.";
                    var askMsg = conversation.AddMessage(MessageRole.Assistant, reply, "AskWhichAppointmentToCancel");
                    return new AgentResponseDto(conversation.Id, askMsg.Id, "assistant", reply, false, null, null, false);
                }
                else
                {
                    matchedAppt = activeAppts[0];
                }
            }

            var appt = matchedAppt.Value;
            var apptId = GetAppointmentId(appt);
            var apptNumber = TryGetProp(appt, "AppointmentNumber", out var numProp) ? numProp.GetString() : "APT";
            var localTime = TryGetProp(appt, "LocalDisplay", out var locProp) ? locProp.GetString() : "";

            var cancelContext = JsonSerializer.Serialize(new { AppointmentId = apptId, AppointmentNumber = apptNumber });
            conversation.SetPendingConfirmation(apptId, "cancel_appointment", cancelContext);

            var text = $"{apptNumber} numaralı ({localTime}) randevunuzu iptal etmek istediğinize emin misiniz? (Evet / Hayır)";
            var proposeCancelMsg = conversation.AddMessage(MessageRole.Assistant, text, "ProposeCancellation");

            return new AgentResponseDto(
                conversation.Id,
                proposeCancelMsg.Id,
                "assistant",
                text,
                RequiresConfirmation: true,
                ProposedAction: "cancel_appointment",
                Summary: $"{apptNumber} randevusunu iptal et",
                IsCompleted: false);
        }

        // 2. Check if user is asking for appointment confirmation (or answering which appointment to confirm)
        if (handleConfirm)
        {
            var apptsResult = await _mcpClient.CallToolAsync("get_customer_appointments", new
            {
                organization_id = conversation.OrganizationId,
                customer_id = conversation.CustomerId
            }, ct);

            List<JsonElement> apptsArray = new();
            if (apptsResult.Success && !string.IsNullOrWhiteSpace(apptsResult.Content))
            {
                var doc = JsonDocument.Parse(apptsResult.Content);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    apptsArray = doc.RootElement.EnumerateArray().ToList();
                }
                else if (TryGetProp(doc.RootElement, "Appointments", out var aProp) && aProp.ValueKind == JsonValueKind.Array)
                {
                    apptsArray = aProp.EnumerateArray().ToList();
                }
            }

            var activeAppts = apptsArray.Where(a =>
            {
                var status = TryGetProp(a, "Status", out var st) ? st.GetString() : "";
                return !string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(status, "NoShow", StringComparison.OrdinalIgnoreCase);
            }).OrderBy(a =>
            {
                if (TryGetProp(a, "StartUtc", out var su) && su.TryGetDateTimeOffset(out var dto))
                    return dto;
                return DateTimeOffset.MaxValue;
            }).ToList();

            if (activeAppts.Count == 0)
            {
                var noApptText = "Onaylanacak aktif bir randevunuz bulunmamaktadır.";
                var noApptMsg = conversation.AddMessage(MessageRole.Assistant, noApptText, "NoActiveAppointment");
                return new AgentResponseDto(conversation.Id, noApptMsg.Id, "assistant", noApptText, false, null, null, false);
            }

            var matchedAppt = FindMatchingAppointment(activeAppts, normalized, nowLocal);

            if (matchedAppt == null)
            {
                if (activeAppts.Count > 1)
                {
                    var listItems = activeAppts.Select((a, idx) =>
                    {
                        var num = TryGetProp(a, "AppointmentNumber", out var np) ? np.GetString() : "APT";
                        var time = TryGetProp(a, "LocalDisplay", out var tp) ? tp.GetString() : "";
                        var status = TryGetProp(a, "Status", out var sp) ? sp.GetString() : "";
                        var statusTr = TranslateStatus(status);
                        return $"{idx + 1}. {num}: {time} ({statusTr})";
                    });

                    var prefix = answeringWhichToConfirm
                        ? "Belirttiğiniz bilgilerle eşleşen aktif bir randevu bulunamadı.\n"
                        : "";

                    var reply = $"{prefix}Hangi randevunuzu onaylamak istersiniz?\nKayıtlı aktif randevularınız:\n{string.Join("\n", listItems)}\n\nLütfen işlem yapmak istediğiniz randevunun numarasını, tarihini veya saatini belirtiniz.";
                    var askConfirmMsg = conversation.AddMessage(MessageRole.Assistant, reply, "AskWhichAppointmentToConfirm");
                    return new AgentResponseDto(conversation.Id, askConfirmMsg.Id, "assistant", reply, false, null, null, false);
                }
                else
                {
                    matchedAppt = activeAppts[0];
                }
            }

            var appt = matchedAppt.Value;
            var apptId = GetAppointmentId(appt);
            var apptNumber = TryGetProp(appt, "AppointmentNumber", out var numProp) ? numProp.GetString() : "APT";
            var localTime = TryGetProp(appt, "LocalDisplay", out var locProp) ? locProp.GetString() : "";
            var currentStatus = TryGetProp(appt, "Status", out var stProp) ? stProp.GetString() : "";

            if (string.Equals(currentStatus, "Confirmed", StringComparison.OrdinalIgnoreCase))
            {
                var text = $"{apptNumber} numaralı ({localTime}) randevunuz zaten onaylanmış durumdadır. Başka bir konuda yardımcı olabilir miyim?";
                var alreadyConfirmedMsg = conversation.AddMessage(MessageRole.Assistant, text, "AppointmentAlreadyConfirmed");
                return new AgentResponseDto(conversation.Id, alreadyConfirmedMsg.Id, "assistant", text, false, null, null, false);
            }

            var confirmContext = JsonSerializer.Serialize(new { AppointmentId = apptId, AppointmentNumber = apptNumber });
            conversation.SetPendingConfirmation(apptId, "confirm_appointment", confirmContext);

            var proposeText = $"{apptNumber} numaralı ({localTime}) randevunuzu onaylamak istediğinize emin misiniz? (Evet / Hayır)";
            var proposeConfirmMsg = conversation.AddMessage(MessageRole.Assistant, proposeText, "ProposeConfirmation");

            return new AgentResponseDto(
                conversation.Id,
                proposeConfirmMsg.Id,
                "assistant",
                proposeText,
                RequiresConfirmation: true,
                ProposedAction: "confirm_appointment",
                Summary: $"{apptNumber} randevusunu onayla",
                IsCompleted: false);
        }

        // 3. Check if user is asking for my appointments
        if (normalized.Contains("randevular") || normalized.Contains("randevum") || (normalized.Contains("randevu") && (normalized.Contains("listele") || normalized.Contains("göster") || normalized.Contains("var mı") || normalized.Contains("mevcut") || normalized.Contains("hangi"))) || normalized.Contains("my appointments"))
        {
            var apptsResult = await _mcpClient.CallToolAsync("get_customer_appointments", new
            {
                organization_id = conversation.OrganizationId,
                customer_id = conversation.CustomerId
            }, ct);

            if (apptsResult.Success && !string.IsNullOrWhiteSpace(apptsResult.Content))
            {
                var doc = JsonDocument.Parse(apptsResult.Content);
                List<JsonElement> appts = new();
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    appts = doc.RootElement.EnumerateArray().ToList();
                }
                else if (TryGetProp(doc.RootElement, "Appointments", out var aProp) && aProp.ValueKind == JsonValueKind.Array)
                {
                    appts = aProp.EnumerateArray().ToList();
                }

                if (appts.Any())
                {
                    var list = string.Join("\n", appts.Select(a =>
                    {
                        var num = TryGetProp(a, "AppointmentNumber", out var np) ? np.GetString() : "APT";
                        var time = TryGetProp(a, "LocalDisplay", out var tp) ? tp.GetString() : "";
                        var status = TryGetProp(a, "Status", out var sp) ? sp.GetString() : "";
                        var statusTr = TranslateStatus(status);
                        return $"- {num}: {time} ({statusTr})";
                    }));
                    var reply = $"Kayıtlı randevularınız:\n{list}";
                    var msg = conversation.AddMessage(MessageRole.Assistant, reply, "ListAppointments");
                    return new AgentResponseDto(conversation.Id, msg.Id, "assistant", reply, false, null, null, false);
                }
            }

            var emptyReply = "Sistemde kayıtlı bir randevunuz bulunmuyor.";
            var emptyMsg = conversation.AddMessage(MessageRole.Assistant, emptyReply, "ListAppointmentsEmpty");
            return new AgentResponseDto(conversation.Id, emptyMsg.Id, "assistant", emptyReply, false, null, null, false);
        }

        // 3. Check if user is asking for working hours or schedule
        if (normalized.Contains("çalışma saat") ||
            normalized.Contains("çalışma gün") ||
            normalized.Contains("mesai") ||
            normalized.Contains("ne zaman açık") ||
            normalized.Contains("hangi günler") ||
            normalized.Contains("kaçta açık") ||
            normalized.Contains("açık mısınız") ||
            normalized.Contains("hizmet veriyor") ||
            normalized.Contains("hizmet veriyorsunuz") ||
            normalized.Contains("haftanın 7 günü") ||
            normalized.Contains("7 gün") ||
            normalized.Contains("haftasonu açık") ||
            normalized.Contains("hafta sonu açık") ||
            normalized.Contains("pazar açık") ||
            normalized.Contains("pazar günü açık") ||
            normalized.Contains("pazar günleri açık") ||
            normalized.Contains("pazar çalışıyor") ||
            normalized.Contains("pazar saatleri") ||
            normalized.Contains("pazar günü randevu var mı") ||
            normalized.Contains("pazar randevu var mı") ||
            normalized.Contains("cumartesi açık") ||
            normalized.Contains("working hours"))
        {
            var orgResult = await _mcpClient.CallToolAsync("get_organization_info", new
            {
                organization_id = conversation.OrganizationId
            }, ct);

            if (orgResult.Success && !string.IsNullOrWhiteSpace(orgResult.Content))
            {
                var doc = JsonDocument.Parse(orgResult.Content);
                var orgName = TryGetProp(doc.RootElement, "Name", out var n) ? n.GetString() : "Kurumumuz";

                if (TryGetProp(doc.RootElement, "WeeklyAvailabilities", out var wa) && wa.ValueKind == JsonValueKind.Array)
                {
                    var daysOrder = new List<(int DayOfWeek, string TurkishName)>
                    {
                        (1, "Pazartesi"),
                        (2, "Salı"),
                        (3, "Çarşamba"),
                        (4, "Perşembe"),
                        (5, "Cuma"),
                        (6, "Cumartesi"),
                        (0, "Pazar")
                    };

                    var lines = new List<string>();
                    var dayEntries = wa.EnumerateArray().ToList();

                    // Check if any day (Sunday, Saturday, etc.) has available slots generated even if not in weekly template
                    JsonDocument? slotsDoc = null;
                    try
                    {
                        var slotsCheck = await _mcpClient.CallToolAsync("get_available_slots", new
                        {
                            organization_id = conversation.OrganizationId,
                            start_date = DateOnly.FromDateTime(nowLocal).ToString("yyyy-MM-dd"),
                            end_date = DateOnly.FromDateTime(nowLocal.AddDays(14)).ToString("yyyy-MM-dd")
                        }, ct);
                        if (slotsCheck.Success && !string.IsNullOrWhiteSpace(slotsCheck.Content))
                        {
                            slotsDoc = JsonDocument.Parse(slotsCheck.Content);
                        }
                    }
                    catch { }

                    foreach (var (dayNum, turkName) in daysOrder)
                    {
                        var engName = ((DayOfWeek)dayNum).ToString();
                        var entry = dayEntries.FirstOrDefault(e =>
                        {
                            if (TryGetProp(e, "DayOfWeek", out var dow))
                            {
                                if (dow.ValueKind == JsonValueKind.Number && dow.GetInt32() == dayNum) return true;
                                if (dow.ValueKind == JsonValueKind.String)
                                {
                                    var str = dow.GetString();
                                    if (string.Equals(str, turkName, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(str, engName, StringComparison.OrdinalIgnoreCase)) return true;
                                }
                            }
                            if (TryGetProp(e, "DayName", out var dn) && dn.ValueKind == JsonValueKind.String)
                            {
                                var str = dn.GetString();
                                if (string.Equals(str, turkName, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(str, engName, StringComparison.OrdinalIgnoreCase)) return true;
                            }
                            return false;
                        });

                        bool isOpen = false;
                        string start = "09:00";
                        string end = "17:00";

                        if (entry.ValueKind != JsonValueKind.Undefined)
                        {
                            if (TryGetProp(entry, "IsActive", out var ia) && ia.GetBoolean())
                            {
                                isOpen = true;
                                start = TryGetProp(entry, "StartTime", out var st) ? st.GetString() ?? "09:00" : "09:00";
                                end = TryGetProp(entry, "EndTime", out var et) ? et.GetString() ?? "17:00" : "17:00";
                            }
                            else
                            {
                                isOpen = false;
                            }
                        }
                        else if (slotsDoc != null)
                        {
                            // If a day is completely missing from the weekly template, check if active slots exist for this DayOfWeek
                            var targetDayOfWeek = (DayOfWeek)dayNum;
                            if (TryGetProp(slotsDoc.RootElement, "Slots", out var sProp) && sProp.ValueKind == JsonValueKind.Array)
                            {
                                var daySlots = sProp.EnumerateArray().Where(s =>
                                {
                                    if (TryGetProp(s, "StartUtc", out var su) && su.TryGetDateTimeOffset(out var sDto))
                                    {
                                        return sDto.ToOffset(TimeSpan.FromHours(3)).DayOfWeek == targetDayOfWeek;
                                    }
                                    if (TryGetProp(s, "Date", out var dProp) && DateOnly.TryParse(dProp.GetString(), out var dVal))
                                    {
                                        return dVal.DayOfWeek == targetDayOfWeek;
                                    }
                                    return false;
                                }).ToList();

                                if (daySlots.Count > 0)
                                {
                                    isOpen = true;
                                    var firstStart = daySlots.Select(s => GetSlotStartTime(s)).Where(s => !string.IsNullOrEmpty(s)).OrderBy(s => s).FirstOrDefault() ?? "10:00";
                                    var lastEnd = daySlots.Select(s => GetSlotEndTime(s)).Where(s => !string.IsNullOrEmpty(s)).OrderByDescending(s => s).FirstOrDefault() ?? "17:00";
                                    start = firstStart;
                                    end = lastEnd;
                                }
                            }
                        }

                        if (isOpen)
                        {
                            if (start.Length > 5) start = start[..5];
                            if (end.Length > 5) end = end[..5];
                            lines.Add($"- {turkName}: {start} - {end}");
                        }
                        else
                        {
                            lines.Add($"- {turkName}: Kapalı");
                        }
                    }

                    // Check if user specifically asked about Sunday or weekend
                    var isSpecificallyAskingSunday = (normalized.Contains("pazar") && !normalized.Contains("pazartesi"));
                    string intro = $"{orgName} çalışma saatlerimiz ve açık olduğumuz günler aşağıdaki gibidir:\n";
                    if (isSpecificallyAskingSunday)
                    {
                        var sundayLine = lines.FirstOrDefault(l => l.StartsWith("- Pazar:"));
                        if (sundayLine != null && !sundayLine.Contains("Kapalı"))
                        {
                            var sundayHours = sundayLine.Replace("- Pazar:", "").Trim();
                            intro = $"Evet! {orgName} Pazar günleri de ({sundayHours}) hizmet vermektedir.\n\nHaftalık tüm çalışma saatlerimiz:\n";
                        }
                        else
                        {
                            intro = $"{orgName} Pazar günleri kapalıdır. Ancak diğer günlerimiz için randevu alabilirsiniz.\n\nÇalışma saatlerimiz:\n";
                        }
                    }

                    var scheduleText = intro +
                                       string.Join("\n", lines) +
                                       "\n\nRandevu almak istediğiniz tarihi veya saati belirtebilirsiniz (Örnek: 'Yarın saat 10:00 için randevu').";

                    var msg = conversation.AddMessage(MessageRole.Assistant, scheduleText, "WorkingHours");
                    return new AgentResponseDto(conversation.Id, msg.Id, "assistant", scheduleText, false, null, null, false);
                }
            }
        }

        // 4. Check if user is asking for address / contact / location
        if (normalized.Contains("adres") ||
            normalized.Contains("neredesiniz") ||
            normalized.Contains("nerede") ||
            normalized.Contains("konum") ||
            normalized.Contains("iletişim") ||
            normalized.Contains("iletisim") ||
            normalized.Contains("telefon") ||
            normalized.Contains("ulaşım") ||
            normalized.Contains("ulasim") ||
            normalized.Contains("mail") ||
            normalized.Contains("e-posta") ||
            normalized.Contains("eposta") ||
            normalized.Contains("hangi şehir") ||
            normalized.Contains("neredesin"))
        {
            var orgResult = await _mcpClient.CallToolAsync("get_organization_info", new
            {
                organization_id = conversation.OrganizationId
            }, ct);

            string orgName = organization?.Name ?? "Kurumumuz";
            string address = organization?.Address ?? "Belirtilmemiş";
            string phone = organization?.PhoneNumber?.Value ?? "Belirtilmemiş";
            string email = organization?.Email?.Value ?? "Belirtilmemiş";

            if (orgResult.Success && !string.IsNullOrWhiteSpace(orgResult.Content))
            {
                var doc = JsonDocument.Parse(orgResult.Content);
                if (TryGetProp(doc.RootElement, "Name", out var n) && n.GetString() is { } nameVal) orgName = nameVal;
                if (TryGetProp(doc.RootElement, "Address", out var a) && a.GetString() is { } addrVal) address = addrVal;
                if (TryGetProp(doc.RootElement, "PhoneNumber", out var p) && p.GetString() is { } phoneVal) phone = phoneVal;
                if (TryGetProp(doc.RootElement, "Email", out var e) && e.GetString() is { } emailVal) email = emailVal;
            }

            var contactText = $"{orgName} İletişim ve Adres Bilgileri:\n" +
                              $"📍 Adres: {address}\n" +
                              $"📞 Telefon: {phone}\n" +
                              $"✉️ E-posta: {email}\n\n" +
                              "Randevu almak veya çalışma saatlerimizi öğrenmek isterseniz size memnuniyetle yardımcı olabilirim.";

            var msg = conversation.AddMessage(MessageRole.Assistant, contactText, "OrganizationContact");
            return new AgentResponseDto(conversation.Id, msg.Id, "assistant", contactText, false, null, null, false);
        }

        // 5. Check if user is asking for services / doctor / pricing / about
        if (normalized.Contains("hizmet") ||
            normalized.Contains("fiyat") ||
            normalized.Contains("ücret") ||
            normalized.Contains("ucret") ||
            normalized.Contains("doktor") ||
            normalized.Contains("uzman") ||
            normalized.Contains("hekim") ||
            normalized.Contains("ne iş yap") ||
            normalized.Contains("neler var"))
        {
            var orgName = organization?.Name ?? "Kurumumuz";
            var phone = organization?.PhoneNumber?.Value ?? "iletişim numaramız";
            var infoText = $"{orgName} olarak sizlere profesyonel randevulu sağlık ve danışmanlık hizmeti sunmaktayız.\n\n" +
                           $"Hizmetlerimiz, uzmanlarımız ve muayene ücretlerimiz hakkında detaylı bilgi almak için kurumumuzla iletişime geçebilirsiniz:\n" +
                           $"📞 Telefon: {phone}\n\n" +
                           "Müsait bir randevu saati belirlemek isterseniz dilediğiniz günü belirtebilirsiniz (Örnek: 'Yarın için uygun saatler').";

            var msg = conversation.AddMessage(MessageRole.Assistant, infoText, "OrganizationServices");
            return new AgentResponseDto(conversation.Id, msg.Id, "assistant", infoText, false, null, null, false);
        }

        // 6. Check if user is greeting
        if (normalized.StartsWith("merhaba") ||
            normalized.StartsWith("selam") ||
            normalized.StartsWith("günaydın") ||
            normalized.StartsWith("gunaydin") ||
            normalized.StartsWith("iyi günler") ||
            normalized.StartsWith("iyi gunler") ||
            normalized.StartsWith("iyi akşamlar") ||
            normalized.StartsWith("iyi aksamlar") ||
            normalized.StartsWith("iyi geceler") ||
            normalized.Contains("nasılsın") ||
            normalized.Contains("nasılsınız") ||
            normalized.Contains("nasilsin") ||
            normalized == "sa" ||
            normalized == "slm")
        {
            var orgName = organization?.Name ?? "Kurumumuza";
            var greetingText = $"Merhaba! {orgName} randevu asistanına hoş geldiniz. Size nasıl yardımcı olabilirim?\n\n" +
                               "- 📅 Randevu almak için tarih veya saat belirtebilirsiniz (Örn: 'Yarın için uygun saatler')\n" +
                               "- 📋 Mevcut randevularınızı sorgulamak için 'Randevularımı listele' yazabilirsiniz\n" +
                               "- 🕒 Çalışma saatlerimizi öğrenmek için 'Çalışma saatleriniz nedir?' yazabilirsiniz\n" +
                               "- 📍 Adres ve iletişim bilgilerimiz için 'Adresiniz nerede?' yazabilirsiniz";

            var msg = conversation.AddMessage(MessageRole.Assistant, greetingText, "Greeting");
            return new AgentResponseDto(conversation.Id, msg.Id, "assistant", greetingText, false, null, null, false);
        }

        // 7. Check if user is saying thank you / farewell
        if (normalized.Contains("teşekkür") ||
            normalized.Contains("tesekkur") ||
            normalized.Contains("sağol") ||
            normalized.Contains("sagol") ||
            normalized.Contains("harika") ||
            normalized.Contains("tamamdır") ||
            normalized.Contains("tamamdir") ||
            normalized.Contains("anladım") ||
            normalized.Contains("anladim") ||
            normalized.Contains("görüşürüz") ||
            normalized.Contains("gorusuruz") ||
            normalized.Contains("kolay gelsin") ||
            normalized.Contains("iyi çalışmalar") ||
            normalized.Contains("iyi calismalar"))
        {
            var thanksText = "Rica ederim! Yardımcı olabileceğim başka bir konu veya randevu talebiniz olursa her zaman buradayım. Sağlıklı ve iyi günler dilerim!";
            var msg = conversation.AddMessage(MessageRole.Assistant, thanksText, "Gratitude");
            return new AgentResponseDto(conversation.Id, msg.Id, "assistant", thanksText, false, null, null, false);
        }

        // 8. Check if user asks for help / capabilities
        if (normalized.Contains("yardım") ||
            normalized.Contains("yardim") ||
            normalized.Contains("ne yapabilirsin") ||
            normalized.Contains("neler yapabilirsin") ||
            normalized.Contains("nasıl randevu") ||
            normalized.Contains("nasil randevu") ||
            normalized.Contains("komutlar"))
        {
            var helpText = "Size şu konularda yardımcı olabilirim:\n" +
                           "- 📅 **Randevu Alma:** 'Yarın için uygun saatler', '3 Kasım saat 10:00 için randevu'\n" +
                           "- 📋 **Randevu Sorgulama:** 'Mevcut randevularımı listele'\n" +
                           "- ❌ **Randevu İptali:** 'Randevumu iptal etmek istiyorum'\n" +
                           "- 🕒 **Çalışma Saatleri:** 'Çalışma saatleriniz nedir?'\n" +
                           "- 📍 **İletişim & Adres:** 'Adresiniz ve telefon numaranız nedir?'\n\n" +
                           "Hangi konuda yardımcı olmamı istersiniz?";

            var msg = conversation.AddMessage(MessageRole.Assistant, helpText, "Help");
            return new AgentResponseDto(conversation.Id, msg.Id, "assistant", helpText, false, null, null, false);
        }

        // 9. Resolve target date if requested (using local organization time)
        var requestedDate = ResolveTargetDate(normalized, nowLocal);

        // If no explicit date is specified in current message, inherit date context from recent messages in conversation
        if (!requestedDate.HasValue && conversation.Messages.Any())
        {
            foreach (var m in conversation.Messages.OrderByDescending(x => x.CreatedAt).Take(5))
            {
                var prevDate = ResolveTargetDate(m.Content.ToLowerInvariant(), nowLocal);
                if (prevDate.HasValue)
                {
                    requestedDate = prevDate;
                    break;
                }
            }
        }

        var requestedTime = TryExtractTime(normalized);

        bool hasAppointmentKeyword = normalized.Contains("randevu") ||
                                     normalized.Contains("müsait") ||
                                     normalized.Contains("musait") ||
                                     normalized.Contains("uygun") ||
                                     normalized.Contains("saat") ||
                                     normalized.Contains("slot") ||
                                     normalized.Contains("zaman") ||
                                     normalized.Contains("boş") ||
                                     normalized.Contains("bos") ||
                                     normalized.Contains("yer var") ||
                                     normalized.Contains("almak istiyorum") ||
                                     normalized.Contains("oluştur") ||
                                     normalized.Contains("olustur") ||
                                     normalized.Contains("rezerve") ||
                                     normalized.Contains("getir") ||
                                     normalized.Contains("göster") ||
                                     normalized.Contains("listele") ||
                                     normalized.Contains("bakabilir") ||
                                     normalized.Contains("var mı") ||
                                     normalized.Contains("var mi");

        bool isShortDateAnswer = requestedDate.HasValue && normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3;

        bool isSlotQueryIntent = hasAppointmentKeyword || isShortDateAnswer || !string.IsNullOrEmpty(requestedTime);

        if (!isSlotQueryIntent)
        {
            var fallbackText = "Size yardımcı olmaktan memnuniyet duyarım. Randevu almak için bir gün veya saat belirtebilir (Örn: 'Yarın için uygun saatler' veya '3 Kasım saat 10:00'), mevcut randevularınızı listeleyebilir veya çalışma saatlerimizi öğrenebilirsiniz. Nasıl yardımcı olabilirim?";
            var fallbackMsg = conversation.AddMessage(MessageRole.Assistant, fallbackText, "GeneralFallback");
            return new AgentResponseDto(conversation.Id, fallbackMsg.Id, "assistant", fallbackText, false, null, null, false);
        }

        DateOnly queryStartDate;
        DateOnly queryEndDate;

        if (requestedDate.HasValue)
        {
            queryStartDate = requestedDate.Value;
            queryEndDate = requestedDate.Value;
        }
        else
        {
            queryStartDate = DateOnly.FromDateTime(nowLocal);
            queryEndDate = DateOnly.FromDateTime(nowLocal.AddDays(7));
        }

        var slotsResult = await _mcpClient.CallToolAsync("get_available_slots", new
        {
            organization_id = conversation.OrganizationId,
            start_date = queryStartDate.ToString("yyyy-MM-dd"),
            end_date = queryEndDate.ToString("yyyy-MM-dd")
        }, ct);

        if (!slotsResult.Success)
        {
            var errText = $"Müsait randevu saatleri sorgulanamadı: {slotsResult.ErrorMessage ?? "Belirtilen kurum bulunamadı veya sistem yanıt vermedi."}";
            var errMsg = conversation.AddMessage(MessageRole.Assistant, errText, "SlotQueryError");
            return new AgentResponseDto(conversation.Id, errMsg.Id, "assistant", errText, false, null, null, false);
        }

        if (slotsResult.Success && !string.IsNullOrWhiteSpace(slotsResult.Content))
        {
            var doc = JsonDocument.Parse(slotsResult.Content);
            List<JsonElement> slots = new();

            if (TryGetProp(doc.RootElement, "Slots", out var slotsProp) && slotsProp.ValueKind == JsonValueKind.Array)
            {
                slots = slotsProp.EnumerateArray().ToList();
            }

            // If a specific date was requested, ensure slots strictly match that date
            if (requestedDate.HasValue)
            {
                var targetDateFormatted = requestedDate.Value.ToString("dd.MM.yyyy");
                slots = slots.Where(s =>
                {
                    var ld = TryGetProp(s, "LocalDisplay", out var d) ? d.GetString() ?? "" : "";
                    return ld.StartsWith(targetDateFormatted);
                }).ToList();
            }
            else if (slots.Any())
            {
                var firstSlotDisplay = TryGetProp(slots[0], "LocalDisplay", out var fd) ? fd.GetString() ?? "" : "";
                if (firstSlotDisplay.Length >= 10)
                {
                    var firstDatePrefix = firstSlotDisplay[..10];
                    slots = slots.Where(s =>
                    {
                        var ld = TryGetProp(s, "LocalDisplay", out var d) ? d.GetString() ?? "" : "";
                        return ld.StartsWith(firstDatePrefix);
                    }).ToList();
                }
            }

            bool preferAfternoon = normalized.Contains("öğleden sonra") || normalized.Contains("ogleden sonra") || normalized.Contains("öğlen") || normalized.Contains("oglen") || normalized.Contains("akşam") || normalized.Contains("aksam");
            bool preferMorning = normalized.Contains("sabah") || normalized.Contains("öğleden önce") || normalized.Contains("ogleden once");

            if (preferAfternoon)
            {
                var afternoon = slots.Where(s =>
                {
                    var st = GetSlotStartTime(s);
                    return string.Compare(st, "12:00", StringComparison.Ordinal) >= 0;
                }).ToList();
                if (afternoon.Any()) slots = afternoon;
            }
            else if (preferMorning)
            {
                var morning = slots.Where(s =>
                {
                    var st = GetSlotStartTime(s);
                    return string.Compare(st, "12:00", StringComparison.Ordinal) < 0;
                }).ToList();
                if (morning.Any()) slots = morning;
            }

            if (!slots.Any())
            {
                string noSlotsText;
                if (requestedDate.HasValue)
                {
                    var dayName = ToTurkishDayName(requestedDate.Value.DayOfWeek);
                    var special = organization?.SpecialAvailabilities?.FirstOrDefault(s => s.Date == requestedDate.Value);
                    bool isDayClosed = false;
                    if (special != null)
                    {
                        isDayClosed = special.IsClosed;
                    }
                    else
                    {
                        var weekly = organization?.WeeklyAvailabilities?.FirstOrDefault(w => w.DayOfWeek == requestedDate.Value.DayOfWeek);
                        if (weekly != null && !weekly.IsActive)
                        {
                            isDayClosed = true;
                        }
                    }

                    var cultureTr = new System.Globalization.CultureInfo("tr-TR");
                    if (isDayClosed)
                    {
                        noSlotsText = $"{organization?.Name ?? "Kurumumuz"} {requestedDate.Value.ToString("d MMMM yyyy", cultureTr)} {dayName} günleri kapalıdır. Açık olduğumuz diğer günler için randevu saati seçebilir veya çalışma saatlerimizi öğrenebilirsiniz.";
                    }
                    else
                    {
                        noSlotsText = $"{requestedDate.Value.ToString("d MMMM yyyy", cultureTr)} {dayName} tarihi için müsait randevu saati bulunamadı. Kurumumuzun çalışma saatlerini öğrenebilir veya başka bir gün için sorgulama yapabilirsiniz.";
                    }
                }
                else
                {
                    noSlotsText = "Önümüzdeki 7 gün için uygun müsait randevu saati bulunamadı. Lütfen daha sonra tekrar deneyiniz.";
                }

                var noSlotsMsg = conversation.AddMessage(MessageRole.Assistant, noSlotsText, "NoSlotsAvailable");
                return new AgentResponseDto(conversation.Id, noSlotsMsg.Id, "assistant", noSlotsText, false, null, null, false);
            }

            // Check if user requested a specific time (already extracted above)
            var matchedSlot = slots.FirstOrDefault(s =>
            {
                var startTime = GetSlotStartTime(s);
                if (!string.IsNullOrEmpty(requestedTime))
                {
                    return startTime == requestedTime;
                }
                return false;
            });

            // If a specific slot is matched, propose booking with confirmation
            if (matchedSlot.ValueKind != JsonValueKind.Undefined)
            {
                var slotId = TryGetProp(matchedSlot, "Id", out var idProp) ? idProp.GetGuid() : Guid.Empty;
                var localDisplay = TryGetProp(matchedSlot, "LocalDisplay", out var ld) ? ld.GetString() ?? "Randevu Saati" : "Randevu Saati";

                var pendingContext = new PendingBookingContext(
                    conversation.OrganizationId,
                    conversation.CustomerId,
                    slotId,
                    localDisplay,
                    originalMessage);

                conversation.SetPendingConfirmation(slotId, "create_appointment", JsonSerializer.Serialize(pendingContext));

                var confirmText = $"{localDisplay} için randevunuzu onaylıyor musunuz? (Evet / Hayır)";
                var confirmMsg = conversation.AddMessage(MessageRole.Assistant, confirmText, "ProposeBooking");

                return new AgentResponseDto(
                    conversation.Id,
                    confirmMsg.Id,
                    "assistant",
                    confirmText,
                    RequiresConfirmation: true,
                    ProposedAction: "create_appointment",
                    Summary: $"{localDisplay} randevusunu oluştur",
                    IsCompleted: false);
            }

            // List available slots for the user
            string headerPrefix;
            if (!string.IsNullOrEmpty(requestedTime))
            {
                var dateName = requestedDate.HasValue
                    ? $"{requestedDate.Value.ToString("d MMMM yyyy", new System.Globalization.CultureInfo("tr-TR"))} günü "
                    : "";
                headerPrefix = $"Belirttiğiniz saat ({requestedTime}) {dateName}için maalesef dolu veya müsait değil. Müsait olan saatler:\n";
            }
            else if (requestedDate.HasValue)
            {
                var dayName = ToTurkishDayName(requestedDate.Value.DayOfWeek);
                headerPrefix = $"{requestedDate.Value.ToString("d MMMM yyyy", new System.Globalization.CultureInfo("tr-TR"))} {dayName} günü için müsait randevu saatleri:\n";
            }
            else
            {
                var firstSlotDisplay = TryGetProp(slots[0], "LocalDisplay", out var fd) ? fd.GetString() ?? "" : "";
                var firstSlotDate = firstSlotDisplay.Length >= 10 ? firstSlotDisplay[..10] : "En yakın";
                headerPrefix = $"{firstSlotDate} için müsait randevu saatleri:\n";
            }

            var morningSlots = slots.Where(s =>
            {
                var st = GetSlotStartTime(s);
                return string.Compare(st, "12:00", StringComparison.Ordinal) < 0;
            }).ToList();

            var afternoonSlots = slots.Where(s =>
            {
                var st = GetSlotStartTime(s);
                return string.Compare(st, "12:00", StringComparison.Ordinal) >= 0;
            }).ToList();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(headerPrefix.TrimEnd());
            sb.AppendLine();

            if (morningSlots.Any())
            {
                sb.AppendLine("Sabah:");
                foreach (var s in morningSlots)
                {
                    var st = GetSlotStartTime(s);
                    var et = GetSlotEndTime(s);
                    sb.AppendLine($"- {st} - {et}");
                }
            }

            if (afternoonSlots.Any())
            {
                if (morningSlots.Any()) sb.AppendLine();
                sb.AppendLine("Öğleden Sonra:");
                foreach (var s in afternoonSlots)
                {
                    var st = GetSlotStartTime(s);
                    var et = GetSlotEndTime(s);
                    sb.AppendLine($"- {st} - {et}");
                }
            }

            if (!morningSlots.Any() && !afternoonSlots.Any())
            {
                foreach (var s in slots)
                {
                    var ld = TryGetProp(s, "LocalDisplay", out var p) ? p.GetString() : "Müsait Saat";
                    sb.AppendLine($"- {ld}");
                }
            }

            var exampleSlotTime = slots.Select(s => GetSlotStartTime(s))
                                       .FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "10:00";
            sb.AppendLine();
            sb.Append($"Hangi saati rezerve etmek istersiniz? (Örnek: '{exampleSlotTime}' yazabilirsiniz)");

            var slotsListText = sb.ToString();
            var slotsListMsg = conversation.AddMessage(MessageRole.Assistant, slotsListText, "ListAvailableSlots");
            return new AgentResponseDto(conversation.Id, slotsListMsg.Id, "assistant", slotsListText, false, null, null, false);
        }

        var defaultText = "Merhaba! Randevu almak veya mevcut randevularınızı sorgulamak için yardımcı olabilirim. Ne zaman için randevu düşünüyorsunuz?";
        var defaultMsg = conversation.AddMessage(MessageRole.Assistant, defaultText, "Greeting");
        return new AgentResponseDto(conversation.Id, defaultMsg.Id, "assistant", defaultText, false, null, null, false);
    }

    private static DateOnly? ResolveTargetDate(string normalized, DateTime referenceLocal)
    {
        // 1. Relative keywords
        if (normalized.Contains("bugün") || normalized.Contains("bugun"))
            return DateOnly.FromDateTime(referenceLocal);

        if (normalized.Contains("yarın") || normalized.Contains("yarin"))
        {
            if (!normalized.Contains("yarından sonra") && !normalized.Contains("yarindan sonra") && !normalized.Contains("öbür") && !normalized.Contains("obur"))
                return DateOnly.FromDateTime(referenceLocal.AddDays(1));
        }

        if (normalized.Contains("yarından sonra") || normalized.Contains("yarindan sonra") || normalized.Contains("öbür gün") || normalized.Contains("obur gun"))
            return DateOnly.FromDateTime(referenceLocal.AddDays(2));

        // 2. Turkish month names with day: e.g. "4 kasım", "4 kasımda", "4 kasımdaki", "12 aralık"
        var monthRegex = new Regex(@"(?<day>\d{1,2})\s*(?<month>ocak|şubat|subat|mart|nisan|mayıs|mayis|haziran|temmuz|ağustos|agustos|eylül|eylul|ekim|kasım|kasim|aralık|aralik)", RegexOptions.IgnoreCase);
        var match = monthRegex.Match(normalized);
        if (match.Success)
        {
            if (int.TryParse(match.Groups["day"].Value, out var day))
            {
                var monthName = match.Groups["month"].Value.ToLowerInvariant();
                var month = monthName switch
                {
                    "ocak" => 1,
                    "şubat" or "subat" => 2,
                    "mart" => 3,
                    "nisan" => 4,
                    "mayıs" or "mayis" => 5,
                    "haziran" => 6,
                    "temmuz" => 7,
                    "ağustos" or "agustos" => 8,
                    "eylül" or "eylul" => 9,
                    "ekim" => 10,
                    "kasım" or "kasim" => 11,
                    "aralık" or "aralik" => 12,
                    _ => 0
                };

                if (month > 0)
                {
                    var year = referenceLocal.Year;
                    try
                    {
                        return new DateOnly(year, month, day);
                    }
                    catch { }
                }
            }
        }

        // 3. Numeric dates: dd.MM or dd.MM.yyyy
        var numericMatch = Regex.Match(normalized, @"(?<!saat\s*)(?<day>\d{1,2})[\.\/](?<month>\d{1,2})(?:[\.\/](?<year>\d{4}))?");
        if (numericMatch.Success)
        {
            if (int.TryParse(numericMatch.Groups["day"].Value, out var day) &&
                int.TryParse(numericMatch.Groups["month"].Value, out var month))
            {
                var year = numericMatch.Groups["year"].Success ? int.Parse(numericMatch.Groups["year"].Value) : referenceLocal.Year;
                try
                {
                    return new DateOnly(year, month, day);
                }
                catch { }
            }
        }

        // 4. Day of week (e.g. "çarşamba", "pazartesi", "cuma")
        var daysOfWeek = new Dictionary<string, DayOfWeek>
        {
            ["pazartesi"] = DayOfWeek.Monday,
            ["salı"] = DayOfWeek.Tuesday,
            ["sali"] = DayOfWeek.Tuesday,
            ["çarşamba"] = DayOfWeek.Wednesday,
            ["carsamba"] = DayOfWeek.Wednesday,
            ["perşembe"] = DayOfWeek.Thursday,
            ["persembe"] = DayOfWeek.Thursday,
            ["cuma"] = DayOfWeek.Friday,
            ["cumartesi"] = DayOfWeek.Saturday,
            ["pazar"] = DayOfWeek.Sunday
        };

        // Hafta sonu / haftasonu check
        if (normalized.Contains("hafta sonu") || normalized.Contains("haftasonu"))
        {
            var currentDay = referenceLocal.DayOfWeek;
            int daysUntilSat = ((int)DayOfWeek.Saturday - (int)currentDay + 7) % 7;
            if (daysUntilSat == 0 && referenceLocal.Hour >= 17) daysUntilSat = 7;
            return DateOnly.FromDateTime(referenceLocal.AddDays(daysUntilSat));
        }

        foreach (var kvp in daysOfWeek.OrderByDescending(k => k.Key.Length))
        {
            if (kvp.Key == "pazar")
            {
                if (Regex.IsMatch(normalized, @"\bpazar(?![tT][eE][sS][iI])", RegexOptions.IgnoreCase))
                {
                    var currentDay = referenceLocal.DayOfWeek;
                    int daysToAdd = ((int)kvp.Value - (int)currentDay + 7) % 7;
                    if (daysToAdd == 0 && referenceLocal.Hour >= 17) daysToAdd = 7;
                    return DateOnly.FromDateTime(referenceLocal.AddDays(daysToAdd));
                }
            }
            else if (normalized.Contains(kvp.Key))
            {
                var currentDay = referenceLocal.DayOfWeek;
                int daysToAdd = ((int)kvp.Value - (int)currentDay + 7) % 7;
                if (daysToAdd == 0 && referenceLocal.Hour >= 17) daysToAdd = 7;
                return DateOnly.FromDateTime(referenceLocal.AddDays(daysToAdd));
            }
        }

        return null;
    }

    private static string? TryExtractTime(string normalized)
    {
        // 1. Match "saat 14", "saat 14:00", "saat 14.30", "saat 12dekini", "saat 12'deki", "saat 12'yi", "saat 9"
        var saatMatch = Regex.Match(normalized, @"\bsaat\s*(?<hour>[01]?\d|2[0-3])(?:\s*[:.]\s*(?<min>[0-5]\d))?(?:'?(?:de|da|te|ta|deki|daki|teki|taki|dekini|dakini|tekini|takini|yi|yı|yu|yü|i|ı|u|ü|e|a|ye|ya))?(?=\s|[.,;!?]|$|\b)");
        if (saatMatch.Success)
        {
            var hour = int.Parse(saatMatch.Groups["hour"].Value);
            var min = saatMatch.Groups["min"].Success ? int.Parse(saatMatch.Groups["min"].Value) : 0;
            return $"{hour:D2}:{min:D2}";
        }

        // 2. Match "14:00", "14.30", "11:00", "11.00", "11:00'deki" (ensure dot is not part of a date like dd.MM.yyyy)
        var timeMatch = Regex.Match(normalized, @"\b(?<hour>[01]?\d|2[0-3])(?::(?<min>[0-5]\d)|\.(?<min>[0-5]\d)(?!\.\d))");
        if (timeMatch.Success)
        {
            var hour = int.Parse(timeMatch.Groups["hour"].Value);
            var min = int.Parse(timeMatch.Groups["min"].Value);
            return $"{hour:D2}:{min:D2}";
        }

        // 3. Match standalone with suffix: "12deki", "12dekini", "12'deki", "12'dekini", "11'i", "14'ü", "14teki", "11 randevusu"
        var suffixMatch = Regex.Match(normalized, @"\b(?<hour>[01]?\d|2[0-3])(?:'?(?:deki|daki|teki|taki|dekini|dakini|tekini|takini|de|da|te|ta|i|ı|u|ü|yi|yı|yu|yü)|(?=\s*randevu))(?=\s|[.,;!?]|$|\b)");
        if (suffixMatch.Success)
        {
            var hour = int.Parse(suffixMatch.Groups["hour"].Value);
            return $"{hour:D2}:00";
        }

        return null;
    }

    private static string ToTurkishDayName(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => "Pazartesi",
        DayOfWeek.Tuesday => "Salı",
        DayOfWeek.Wednesday => "Çarşamba",
        DayOfWeek.Thursday => "Perşembe",
        DayOfWeek.Friday => "Cuma",
        DayOfWeek.Saturday => "Cumartesi",
        DayOfWeek.Sunday => "Pazar",
        _ => dayOfWeek.ToString()
    };

    private static bool TryGetProp(JsonElement element, string propName, out JsonElement result)
    {
        if (element.TryGetProperty(propName, out result))
            return true;

        var camel = char.ToLowerInvariant(propName[0]) + propName[1..];
        if (element.TryGetProperty(camel, out result))
            return true;

        var upper = char.ToUpperInvariant(propName[0]) + propName[1..];
        return element.TryGetProperty(upper, out result);
    }

    private static string GetSlotStartTime(JsonElement slot)
    {
        if (TryGetProp(slot, "StartTime", out var p) && p.ValueKind == JsonValueKind.String)
            return p.GetString() ?? "";
        if (TryGetProp(slot, "LocalStartTime", out var p2) && p2.ValueKind == JsonValueKind.String)
            return p2.GetString() ?? "";
        return "";
    }

    private static string GetSlotEndTime(JsonElement slot)
    {
        if (TryGetProp(slot, "EndTime", out var p) && p.ValueKind == JsonValueKind.String)
            return p.GetString() ?? "";
        if (TryGetProp(slot, "LocalEndTime", out var p2) && p2.ValueKind == JsonValueKind.String)
            return p2.GetString() ?? "";
        return "";
    }

    private static Guid GetAppointmentId(JsonElement appt)
    {
        if (TryGetProp(appt, "Id", out var idProp) && idProp.TryGetGuid(out var g1))
            return g1;
        if (TryGetProp(appt, "AppointmentId", out var aId) && aId.TryGetGuid(out var g2))
            return g2;
        return Guid.Empty;
    }

    private static string TranslateStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "pending" => "Onay Bekliyor",
            "confirmed" => "Onaylandı",
            "cancelled" => "İptal Edildi",
            "completed" => "Tamamlandı",
            "noshow" => "Gelinmedi",
            _ => status ?? ""
        };
    }

    private static JsonElement? FindMatchingAppointment(
        List<JsonElement> appointments,
        string normalized,
        DateTime nowLocal)
    {
        if (appointments == null || appointments.Count == 0) return null;

        // 1. Check for exact or partial GUID match (Id or SlotId)
        foreach (var appt in appointments)
        {
            var id = GetAppointmentId(appt).ToString().ToLowerInvariant();
            if (!string.IsNullOrEmpty(id))
            {
                if (normalized.Contains(id)) return appt;
                var shortId = id.Length >= 8 ? id[..8] : id; // e.g. "ec66f72f"
                if (normalized.Contains(shortId)) return appt;
            }

            var slotId = TryGetProp(appt, "SlotId", out var sp) && sp.TryGetGuid(out var sg) ? sg.ToString().ToLowerInvariant() : null;
            if (!string.IsNullOrEmpty(slotId))
            {
                if (normalized.Contains(slotId)) return appt;
                var shortSlot = slotId.Length >= 8 ? slotId[..8] : slotId;
                if (normalized.Contains(shortSlot)) return appt;
            }
        }

        // 2. Check for exact or partial AppointmentNumber match (e.g. "APT-20261101-34AC01" or "34AC01")
        foreach (var appt in appointments)
        {
            var num = TryGetProp(appt, "AppointmentNumber", out var np) ? np.GetString()?.ToLowerInvariant() : null;
            if (!string.IsNullOrEmpty(num))
            {
                if (normalized.Contains(num))
                    return appt;

                var parts = num.Split('-');
                if (parts.Length > 1)
                {
                    var suffix = parts[^1];
                    if (suffix.Length >= 4 && normalized.Contains(suffix))
                        return appt;
                }
            }
        }

        // 3. Extract target date and target time from message
        var tokens = normalized.Split(new[] { ' ', '.', ',', '-', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var targetDate = ResolveTargetDate(normalized, nowLocal);
        var targetTime = TryExtractTime(normalized);

        // If user typed a standalone number that is an hour (e.g. "11", "14") rather than an appointment index:
        if (string.IsNullOrEmpty(targetTime) && tokens.Length == 1 && int.TryParse(tokens[0], out var singleNum) && singleNum >= 8 && singleNum <= 22 && singleNum > appointments.Count)
        {
            targetTime = $"{singleNum:D2}:00";
        }

        // Also check if any appointment's explicit date is mentioned in text (e.g. "02.11", "03.11", "04.11")
        if (!targetDate.HasValue)
        {
            foreach (var appt in appointments)
            {
                var display = TryGetProp(appt, "LocalDisplay", out var dp) ? dp.GetString() : "";
                if (!string.IsNullOrEmpty(display))
                {
                    var parts = display.Split(' ');
                    if (parts.Length > 0 && DateOnly.TryParseExact(parts[0], "dd.MM.yyyy", out var parsedDate))
                    {
                        var dayMonth = parts[0].Length >= 5 ? parts[0][..5] : "";
                        if (normalized.Contains(parts[0]) || (!string.IsNullOrEmpty(dayMonth) && normalized.Contains(dayMonth)))
                        {
                            targetDate = parsedDate;
                            break;
                        }
                    }
                }
            }
        }

        // 4. Exact Match by Date and Time
        // If BOTH date and time are identified, match exact appointment
        if (targetDate.HasValue && !string.IsNullOrEmpty(targetTime))
        {
            var matchBoth = appointments.FirstOrDefault(a => AppointmentMatchesDate(a, targetDate.Value) && AppointmentMatchesTime(a, targetTime));
            if (matchBoth.ValueKind != JsonValueKind.Undefined) return matchBoth;
        }

        // 5. Match by Time only (e.g. "14:00", "saat 14", "12:30", "11")
        if (!string.IsNullOrEmpty(targetTime) && !targetDate.HasValue)
        {
            var matchingTimeAppts = appointments.Where(a => AppointmentMatchesTime(a, targetTime)).ToList();
            if (matchingTimeAppts.Count == 1)
            {
                return matchingTimeAppts[0];
            }
            if (matchingTimeAppts.Count > 1)
            {
                var todayMatch = matchingTimeAppts.FirstOrDefault(a => AppointmentMatchesDate(a, DateOnly.FromDateTime(nowLocal)));
                if (todayMatch.ValueKind != JsonValueKind.Undefined)
                    return todayMatch;

                return null;
            }
        }

        // 6. Match by Date only (e.g. "03.11", "3 kasım", "yarın")
        if (targetDate.HasValue && string.IsNullOrEmpty(targetTime))
        {
            var matchingDateAppts = appointments.Where(a => AppointmentMatchesDate(a, targetDate.Value)).ToList();
            if (matchingDateAppts.Count == 1)
            {
                return matchingDateAppts[0];
            }
            // If multiple appointments exist on this date (e.g. 02.11 has 12:00 and 14:00),
            // do NOT arbitrarily pick one; check if the user specified an ordinal for that date (e.g. "2 kasımdaki 1. randevu"),
            // otherwise return null so the system lists and asks which one.
            if (matchingDateAppts.Count > 1)
            {
                var ordinalOnDate = TryMatchOrdinal(matchingDateAppts, normalized);
                if (ordinalOnDate.HasValue)
                    return ordinalOnDate;

                return null;
            }
        }

        // 7. Check for ordinal / positional index (1. / 2. / 3. / ilk / ikinci / sonuncu)
        return TryMatchOrdinal(appointments, normalized);
    }

    private static JsonElement? TryMatchOrdinal(List<JsonElement> appointments, string normalized)
    {
        if (appointments == null || appointments.Count == 0) return null;

        var tokens = normalized.Split(new[] { ' ', '.', ',', '-', '/' }, StringSplitOptions.RemoveEmptyEntries);

        // A. Standalone single number: user replied "1", "2", "3" (or "1.", "2.")
        if (tokens.Length == 1 && int.TryParse(tokens[0], out var singleIdx) && singleIdx >= 1 && singleIdx <= appointments.Count)
        {
            return appointments[singleIdx - 1];
        }

        // B. Short message like "2 iptal", "2 onayla", "2 lütfen", "sadece 2", "2 olsun"
        for (int i = 0; i < appointments.Count; i++)
        {
            var idx = i + 1;
            if (Regex.IsMatch(normalized.Trim(), $@"^(?:lütfen\s+|lutfen\s+|sadece\s+)?{idx}\.?(?:\s+(?:randevu\w*|iptal\w*|onayla\w*|olsun|lütfen|lutfen))?$"))
            {
                return appointments[i];
            }
        }

        // C. Explicit Turkish word ordinals
        if (Regex.IsMatch(normalized, @"\b(ilk|birinci)\b"))
            return appointments.Count > 0 ? appointments[0] : null;

        if (Regex.IsMatch(normalized, @"\b(ikinci)\b"))
            return appointments.Count > 1 ? appointments[1] : null;

        if (Regex.IsMatch(normalized, @"\b(üçüncü|ucuncu)\b"))
            return appointments.Count > 2 ? appointments[2] : null;

        if (Regex.IsMatch(normalized, @"\b(dördüncü|dorduncu)\b"))
            return appointments.Count > 3 ? appointments[3] : null;

        if (Regex.IsMatch(normalized, @"\b(beşinci|besinci)\b"))
            return appointments.Count > 4 ? appointments[4] : null;

        if (Regex.IsMatch(normalized, @"\b(sonuncu|son|son randevu)\b"))
            return appointments.Count > 0 ? appointments[^1] : null;

        // D. Suffix-based ordinals: "1'inci", "1inci", "2'nci", "2nci", "3'üncü", "1.si", "2.si", "2'si"
        for (int i = 0; i < appointments.Count; i++)
        {
            var idx = i + 1;
            if (Regex.IsMatch(normalized, $@"(?<!\d){idx}(?:'|\s*)?(?:inci|ıncı|üncü|uncu|nci|ncı|ncu|ncü|si)\b"))
            {
                return appointments[i];
            }
        }

        // E. Index followed by appointment-related keywords: "1. randevu", "2 randevum", "2. olan", "2. sıradaki", "2 nolu", "2 numaralı"
        for (int i = 0; i < appointments.Count; i++)
        {
            var idx = i + 1;
            if (Regex.IsMatch(normalized, $@"(?<!\d){idx}(?:\.|\s+)(?:randevu|sıradaki|siradaki|olan|secenek|seçenek|nolu|numaralı|numarali)\b"))
            {
                return appointments[i];
            }
        }

        // F. Index with dot "1.", "2.", provided it is NOT a date component (not followed by digits or Turkish month names)
        for (int i = 0; i < appointments.Count; i++)
        {
            var idx = i + 1;
            if (Regex.IsMatch(normalized, $@"(^|\s){idx}\.(?!\d|\s*(?:ocak|şubat|subat|mart|nisan|mayıs|mayis|haziran|temmuz|ağustos|agustos|eylül|eylul|ekim|kasım|kasim|aralık|aralik))(\s|$)"))
            {
                return appointments[i];
            }
        }

        return null;
    }

    private static bool AppointmentMatchesTime(JsonElement appt, string targetTime)
    {
        var display = TryGetProp(appt, "LocalDisplay", out var dp) ? dp.GetString() : "";
        if (!string.IsNullOrEmpty(display) && display.Contains(targetTime)) return true;

        if (TryGetProp(appt, "StartTime", out var st) && st.GetString()?.StartsWith(targetTime) == true) return true;
        if (TryGetProp(appt, "LocalStartTime", out var lst) && lst.GetString()?.StartsWith(targetTime) == true) return true;

        return false;
    }

    private static bool AppointmentMatchesDate(JsonElement appt, DateOnly targetDate)
    {
        var dateStr = targetDate.ToString("dd.MM.yyyy");
        var display = TryGetProp(appt, "LocalDisplay", out var dp) ? dp.GetString() : "";
        if (!string.IsNullOrEmpty(display) && display.Contains(dateStr)) return true;

        if (TryGetProp(appt, "Date", out var dt) && dt.GetString()?.Contains(dateStr) == true) return true;

        if (TryGetProp(appt, "StartUtc", out var su) && su.TryGetDateTimeOffset(out var dto))
        {
            var localDto = dto.ToOffset(TimeSpan.FromHours(3));
            if (DateOnly.FromDateTime(localDto.DateTime) == targetDate) return true;
        }

        return false;
    }
}
