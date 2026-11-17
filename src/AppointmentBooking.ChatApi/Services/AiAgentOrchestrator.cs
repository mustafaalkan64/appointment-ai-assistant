using System.Globalization;
using System.Text;
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

public sealed record PendingAppointmentActionContext(
    Guid AppointmentId,
    string AppointmentNumber,
    string LocalDisplay);

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
        if (conversationId is { } id && id != Guid.Empty)
        {
            conversation = await _context.Conversations
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == organizationId, ct)
                ?? throw new DomainValidationException($"Conversation '{id}' not found.");

            if (conversation.CustomerId != customerId)
                throw new DomainValidationException("Conversation does not belong to the specified customer.");
        }
        else
        {
            conversation = Conversation.Start(organizationId, customerId);
            _context.Conversations.Add(conversation);
        }

        conversation.AddMessage(MessageRole.User, messageText);

        var normalized = NormalizeTurkishText(messageText);
        AgentResponseDto response;

        if (conversation.Status == ConversationStatus.AwaitingConfirmation)
        {
            response = await HandleConfirmationResponseAsync(conversation, normalized, messageText, organization, nowLocal, orgTimeZone, ct);
        }
        else
        {
            response = await HandleGeneralMessageAsync(conversation, normalized, messageText, organization, nowLocal, orgTimeZone, ct);
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
        TimeZoneInfo orgTimeZone,
        CancellationToken ct)
    {
        bool isCancelPending = string.Equals(conversation.PendingOperation, "cancel_appointment", StringComparison.OrdinalIgnoreCase);

        bool isNegative;
        bool isAffirmative;

        if (isCancelPending)
        {
            isNegative = normalized.Contains("hayır") ||
                         normalized.Contains("hayir") ||
                         normalized.Contains("vazgeç") ||
                         normalized.Contains("vazgec") ||
                         normalized.Contains("kalsın") ||
                         normalized.Contains("kalsin") ||
                         normalized.Contains("iptal etme") ||
                         normalized.Contains("istemiyorum") ||
                         Regex.IsMatch(normalized, @"\b(no|keep)\b");

            isAffirmative = !isNegative && (
                            normalized.Contains("evet") ||
                            normalized.Contains("onayla") ||
                            normalized.Contains("onaylıyorum") ||
                            normalized.Contains("onayliyorum") ||
                            normalized.Contains("tamam") ||
                            normalized.Contains("iptal et") ||
                            normalized.Contains("kesin olarak iptal") ||
                            normalized.Contains("yes") ||
                            normalized.Contains("confirm") ||
                            normalized.Contains("olur"));
        }
        else
        {
            isNegative = normalized.Contains("hayır") ||
                         normalized.Contains("hayir") ||
                         normalized.Contains("vazgeç") ||
                         normalized.Contains("vazgec") ||
                         normalized.Contains("iptal") ||
                         normalized.Contains("istemiyorum") ||
                         normalized.Contains("farklı bir saat") ||
                         normalized.Contains("farkli bir saat") ||
                         normalized.Contains("başka bir saat") ||
                         normalized.Contains("baska bir saat") ||
                         normalized.Contains("saat değiştir") ||
                         normalized.Contains("saat degistir") ||
                         Regex.IsMatch(normalized, @"\b(no|cancel)\b");

            isAffirmative = !isNegative && (
                            normalized.Contains("evet") ||
                            normalized.Contains("onayla") ||
                            normalized.Contains("onaylıyorum") ||
                            normalized.Contains("onayliyorum") ||
                            normalized.Contains("tamam") ||
                            normalized.Contains("kabul") ||
                            normalized.Contains("yes") ||
                            normalized.Contains("confirm") ||
                            normalized.Contains("olur"));
        }

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
                    string apptNumber = string.Empty;
                    string localDisplay = context.LocalDisplay;
                    string status = "Confirmed";
                    Guid? createdApptId = null;

                    if (!string.IsNullOrWhiteSpace(mcpResult.Content))
                    {
                        try
                        {
                            using var resDoc = JsonDocument.Parse(mcpResult.Content);
                            if (TryGetProp(resDoc.RootElement, "AppointmentNumber", out var an) && an.ValueKind == JsonValueKind.String)
                                apptNumber = an.GetString() ?? string.Empty;
                            if (TryGetProp(resDoc.RootElement, "LocalDisplay", out var ld) && ld.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(ld.GetString()))
                                localDisplay = ld.GetString()!;
                            if (TryGetProp(resDoc.RootElement, "Status", out var st) && st.ValueKind == JsonValueKind.String)
                                status = st.GetString() ?? "Confirmed";
                            if (TryGetProp(resDoc.RootElement, "AppointmentId", out var aid) && aid.TryGetGuid(out var parsedId))
                                createdApptId = parsedId;
                        }
                        catch (JsonException) { }
                    }

                    // Ensure automatic confirmation if appointment was created in Pending state
                    if (string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase) && createdApptId.HasValue && createdApptId.Value != Guid.Empty)
                    {
                        await _mcpClient.CallToolAsync("confirm_appointment", new
                        {
                            organization_id = context.OrganizationId,
                            appointment_id = createdApptId.Value,
                            customer_id = context.CustomerId,
                            idempotency_key = $"chat-autoconfirm-{conversation.Id:N}-{createdApptId.Value:N}"
                        }, ct);
                    }

                    conversation.Complete();
                    var numberSegment = !string.IsNullOrWhiteSpace(apptNumber) ? $" Randevu No: {apptNumber}." : string.Empty;
                    var successText = $"Randevunuz başarıyla onaylandı ve oluşturuldu!{numberSegment} Saat: {localDisplay}. Sistemde kaydınız oluşturulmuştur.";
                    var summaryText = !string.IsNullOrWhiteSpace(apptNumber)
                        ? $"{apptNumber} - {localDisplay} Randevu Onaylandı"
                        : $"{localDisplay} Randevu Onaylandı";

                    var assistantMsg = conversation.AddMessage(MessageRole.Assistant, successText, "AppointmentConfirmed");

                    return new AgentResponseDto(
                        conversation.Id,
                        assistantMsg.Id,
                        "assistant",
                        successText,
                        RequiresConfirmation: false,
                        ProposedAction: "create_appointment",
                        Summary: summaryText,
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
                var apptId = TryGetProp(doc.RootElement, "AppointmentId", out var idProp) && idProp.TryGetGuid(out var g) ? g : Guid.Empty;
                var apptNum = TryGetProp(doc.RootElement, "AppointmentNumber", out var numProp) ? numProp.GetString() : null;
                var localDisp = TryGetProp(doc.RootElement, "LocalDisplay", out var dispProp) ? dispProp.GetString() : null;
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
                var detailPrefix = !string.IsNullOrWhiteSpace(apptNum)
                    ? $"{apptNum} numaralı" + (!string.IsNullOrWhiteSpace(localDisp) ? $" ({localDisp}) " : " ")
                    : string.Empty;

                var text = mcpResult.Success
                    ? $"{detailPrefix}randevunuz başarıyla iptal edildi. Randevu saati serbest bırakıldı."
                    : $"İptal işlemi tamamlanamadı: {mcpResult.ErrorMessage}";

                if (!string.IsNullOrEmpty(text))
                    text = char.ToUpper(text[0], new CultureInfo("tr-TR")) + text[1..];

                var assistantMsg = conversation.AddMessage(MessageRole.Assistant, text, "AppointmentCancelled");
                return new AgentResponseDto(
                    conversation.Id,
                    assistantMsg.Id,
                    "assistant",
                    text,
                    RequiresConfirmation: false,
                    ProposedAction: "cancel_appointment",
                    Summary: "Randevu İptal Edildi",
                    IsCompleted: mcpResult.Success);
            }
            else if (conversation.PendingOperation == "confirm_appointment")
            {
                var doc = JsonDocument.Parse(conversation.ContextJson);
                var apptId = TryGetProp(doc.RootElement, "AppointmentId", out var idProp) && idProp.TryGetGuid(out var g) ? g : Guid.Empty;
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
                return new AgentResponseDto(conversation.Id, assistantMsg.Id, "assistant", text, false, "confirm_appointment", null, mcpResult.Success);
            }
        }
        else if (isNegative)
        {
            conversation.ClearPendingConfirmation();

            // If user wants to change the hour or issued a new query right away, route to general handler
            if ((normalized.Contains("başka") || normalized.Contains("baska") ||
                 normalized.Contains("farklı") || normalized.Contains("farkli") ||
                 normalized.Contains("değiştir") || normalized.Contains("degistir") ||
                 normalized.Contains("diğer") || normalized.Contains("diger") ||
                 normalized.Contains("listele") ||
                 TryExtractTime(normalized) != null || ResolveTargetDate(normalized, nowLocal) != null) &&
                !normalized.Contains("oluşturma işlemini iptal et") &&
                !normalized.Contains("randevum kalsın") &&
                !normalized.Contains("randevum kalsin"))
            {
                return await HandleGeneralMessageAsync(conversation, normalized, originalMessage, organization, nowLocal, orgTimeZone, ct);
            }

            var text = isCancelPending
                ? "İptal işleminden vazgeçildi, randevunuz aktif olarak korunmaktadır. Başka nasıl yardımcı olabilirim?"
                : "İşleminiz iptal edildi. Size başka bir randevu veya saat konusunda nasıl yardımcı olabilirim?";
            var msg = conversation.AddMessage(MessageRole.Assistant, text, "ConfirmationRejected");
            return new AgentResponseDto(conversation.Id, msg.Id, "assistant", text, false, null, null, false);
        }

        // If user sent a completely new request (e.g. asking working hours, listing appointments, or picking a new date/time) while confirmation was pending
        if (TryExtractTime(normalized) != null ||
            ResolveTargetDate(normalized, nowLocal) != null ||
            normalized.Contains("çalışma saat") ||
            normalized.Contains("randevularım") ||
            normalized.Contains("müsait"))
        {
            conversation.ClearPendingConfirmation();
            return await HandleGeneralMessageAsync(conversation, normalized, originalMessage, organization, nowLocal, orgTimeZone, ct);
        }

        var clarifyText = "Lütfen işlemi onaylıyor musunuz belirtiniz (Evet / Hayır).";
        var clarifyMsg = conversation.AddMessage(MessageRole.Assistant, clarifyText, "ClarificationNeeded");
        return new AgentResponseDto(conversation.Id, clarifyMsg.Id, "assistant", clarifyText, true, conversation.PendingOperation, null, false);
    }

    private async Task<AgentResponseDto> HandleGeneralMessageAsync(
        Conversation conversation,
        string normalized,
        string originalMessage,
        Organization? organization,
        DateTime nowLocal,
        TimeZoneInfo orgTimeZone,
        CancellationToken ct)
    {
        var lastAssistantMsg = conversation.Messages
            .Where(m => m.Role == MessageRole.Assistant)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefault();

        bool answeringWhichToCancel = lastAssistantMsg?.Intent == "AskWhichAppointmentToCancel";
        bool answeringWhichToConfirm = lastAssistantMsg?.Intent == "AskWhichAppointmentToConfirm";

        // If user aborted disambiguation
        if ((answeringWhichToCancel || answeringWhichToConfirm) &&
            (normalized is "vazgeç" or "vazgectim" or "vazgeçtim" or "hayır" or "hayir" or "istemiyorum" or "boşver" or "bosver" or "gerek yok" ||
             Regex.IsMatch(normalized, @"\b(vazgeç|vazgec|iptal\s+etme|boşver|bosver|kalsın|kalsin)\b")) &&
            !normalized.Contains("randevu") && TryExtractTime(normalized) == null && ResolveTargetDate(normalized, nowLocal) == null)
        {
            var cancelReply = "İşleminiz iptal edildi. Size başka nasıl yardımcı olabilirim?";
            var cancelMsg = conversation.AddMessage(MessageRole.Assistant, cancelReply, "ActionCancelled");
            return new AgentResponseDto(conversation.Id, cancelMsg.Id, "assistant", cancelReply, false, null, null, false);
        }

        // Distinguish between referring to already-cancelled appointments ("iptal ettiğim randevular", "iptal edilenler")
        // vs requesting to cancel an active appointment ("randevumu iptal et", "yarınki randevumu iptal etmek istiyorum").
        bool refersToPastCancelledAppointments = Regex.IsMatch(
            normalized,
            @"\biptal\s+(?:ettiğim|ettigim|ettiğimiz|ettigimiz|edilen|edilenler|edilmiş|edilmis|olan|olanlar|olmuş|olmus|geçmişi|gecmisi|listesi)\b") ||
            (normalized.Contains("oluşturduğum") && normalized.Contains("iptal")) ||
            (normalized.Contains("olusturdugum") && normalized.Contains("iptal")) ||
            (normalized.Contains("aktif") && normalized.Contains("iptal")) ||
            (normalized.Contains("hem") && normalized.Contains("iptal"));

        bool hasExplicitCancelAction = Regex.IsMatch(
            normalized,
            @"\biptal\s+(?:et|etmek|eder|edelim|edebilir|edilsin|olsun|istiyorum|talebi|işlemi|islemi)\b") ||
            normalized.EndsWith("iptal") ||
            normalized.StartsWith("iptal ");

        bool isCancelIntent = !refersToPastCancelledAppointments &&
                              (hasExplicitCancelAction ||
                               ((normalized.Contains("iptal") || normalized.Contains("cancel")) &&
                                !Regex.IsMatch(normalized, @"\biptal\s+etme\b") &&
                                !normalized.Contains("iptal istemiyorum") &&
                                !normalized.Contains("listele") &&
                                !normalized.Contains("göster") &&
                                !normalized.Contains("goster") &&
                                !normalized.Contains("takip")));

        // Distinguish booking intent ("randevu almak istiyorum") from confirm existing appointment intent ("randevumu onayla")
        bool isBookingNewAppointment = normalized.Contains("almak") ||
                                       normalized.Contains("oluştur") ||
                                       normalized.Contains("olustur") ||
                                       normalized.Contains("rezerve") ||
                                       normalized.Contains("ayır") ||
                                       normalized.Contains("ayir") ||
                                       normalized.Contains("saatine randevu") ||
                                       normalized.Contains("için randevu") ||
                                       normalized.Contains("icin randevu");

        bool isConfirmIntent = !isBookingNewAppointment &&
                               !refersToPastCancelledAppointments &&
                               (normalized.Contains("onayla") ||
                                normalized.Contains("onaylamak") ||
                                normalized.Contains("confirm") ||
                                normalized.Contains("onay ver"));

        bool handleCancel = isCancelIntent || answeringWhichToCancel;
        bool handleConfirm = !handleCancel && (isConfirmIntent || answeringWhichToConfirm);

        // -----------------------------------------------------------------
        // 1. APPOINTMENT CANCELLATION FLOW (With Confirmation Screen)
        // -----------------------------------------------------------------
        if (handleCancel)
        {
            var apptsArray = await FetchCustomerAppointmentsAsync(conversation.OrganizationId, conversation.CustomerId, ct);

            var activeAppts = apptsArray.Where(a =>
            {
                var status = TryGetProp(a, "Status", out var st) ? st.GetString() : "";
                if (string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "NoShow", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

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

            var matchedAppt = FindMatchingAppointment(activeAppts, normalized, nowLocal, orgTimeZone);

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
            var apptNumber = TryGetProp(appt, "AppointmentNumber", out var numProp) ? numProp.GetString() ?? "APT" : "APT";
            var localTime = TryGetProp(appt, "LocalDisplay", out var locProp) ? locProp.GetString() ?? "" : "";

            var cancelContext = JsonSerializer.Serialize(new PendingAppointmentActionContext(apptId, apptNumber, localTime));
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
                Summary: $"{apptNumber} ({localTime}) randevusunu iptal et",
                IsCompleted: false);
        }

        // -----------------------------------------------------------------
        // 2. APPOINTMENT CONFIRMATION FLOW
        // -----------------------------------------------------------------
        if (handleConfirm)
        {
            var apptsArray = await FetchCustomerAppointmentsAsync(conversation.OrganizationId, conversation.CustomerId, ct);

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

            var matchedAppt = FindMatchingAppointment(activeAppts, normalized, nowLocal, orgTimeZone);

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
            var apptNumber = TryGetProp(appt, "AppointmentNumber", out var numProp) ? numProp.GetString() ?? "APT" : "APT";
            var localTime = TryGetProp(appt, "LocalDisplay", out var locProp) ? locProp.GetString() ?? "" : "";
            var currentStatus = TryGetProp(appt, "Status", out var stProp) ? stProp.GetString() ?? "" : "";

            if (string.Equals(currentStatus, "Confirmed", StringComparison.OrdinalIgnoreCase))
            {
                var text = $"{apptNumber} numaralı ({localTime}) randevunuz zaten onaylanmış durumdadır. Başka bir konuda yardımcı olabilir miyim?";
                var alreadyConfirmedMsg = conversation.AddMessage(MessageRole.Assistant, text, "AppointmentAlreadyConfirmed");
                return new AgentResponseDto(conversation.Id, alreadyConfirmedMsg.Id, "assistant", text, false, null, null, false);
            }

            var confirmContext = JsonSerializer.Serialize(new PendingAppointmentActionContext(apptId, apptNumber, localTime));
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
                Summary: $"{apptNumber} ({localTime}) randevusunu onayla",
                IsCompleted: false);
        }

        // -----------------------------------------------------------------
        // 3. LIST / TRACK APPOINTMENTS (Active, Created, Cancelled, Past)
        // -----------------------------------------------------------------
        bool isListAppointmentsIntent =
            refersToPastCancelledAppointments ||
            normalized.Contains("randevularım") ||
            normalized.Contains("randevularim") ||
            normalized.Contains("randevu geçmiş") ||
            normalized.Contains("randevu gecmis") ||
            normalized.Contains("my appointments") ||
            (!isBookingNewAppointment &&
             (normalized.Contains("randevular") || normalized.Contains("randevum") || normalized.Contains("randevu")) &&
             (normalized.Contains("listele") ||
              normalized.Contains("göster") ||
              normalized.Contains("goster") ||
              normalized.Contains("takip") ||
              normalized.Contains("var mı") ||
              normalized.Contains("var mi") ||
              normalized.Contains("mevcut") ||
              normalized.Contains("kayıtlı") ||
              normalized.Contains("kayitli") ||
              normalized.Contains("oluşturduğum") ||
              normalized.Contains("olusturdugum") ||
              normalized.Contains("aldığım") ||
              normalized.Contains("aldigim") ||
              normalized.Contains("geçmiş") ||
              normalized.Contains("gecmis") ||
              normalized.Contains("tüm") ||
              normalized.Contains("tum") ||
              normalized.Contains("hepsi") ||
              normalized.Contains("hangi randevu") ||
              normalized.Contains("randevularım ne")));

        if (isListAppointmentsIntent)
        {
            var appts = await FetchCustomerAppointmentsAsync(conversation.OrganizationId, conversation.CustomerId, ct);

            // If customer has no appointments in current org, also check across all orgs
            if (appts.Count == 0)
            {
                appts = await FetchCustomerAppointmentsAsync(null, conversation.CustomerId, ct);
            }

            if (appts.Count > 0)
            {
                var sortedAppts = appts.OrderBy(a =>
                {
                    if (TryGetProp(a, "StartUtc", out var su) && su.TryGetDateTimeOffset(out var dto))
                        return dto;
                    return DateTimeOffset.MaxValue;
                }).ToList();

                bool onlyCancelledRequested =
                    refersToPastCancelledAppointments &&
                    !normalized.Contains("oluşturduğum") &&
                    !normalized.Contains("olusturdugum") &&
                    !normalized.Contains("aktif") &&
                    !normalized.Contains("mevcut") &&
                    !normalized.Contains("tüm") &&
                    !normalized.Contains("tum") &&
                    !normalized.Contains("hepsi");

                var activeList = sortedAppts.Where(a =>
                {
                    var st = TryGetProp(a, "Status", out var sp) ? sp.GetString() : "";
                    return !string.Equals(st, "Cancelled", StringComparison.OrdinalIgnoreCase);
                }).ToList();

                var cancelledList = sortedAppts.Where(a =>
                {
                    var st = TryGetProp(a, "Status", out var sp) ? sp.GetString() : "";
                    return string.Equals(st, "Cancelled", StringComparison.OrdinalIgnoreCase);
                }).ToList();

                string FormatApptLine(JsonElement a)
                {
                    var num = TryGetProp(a, "AppointmentNumber", out var np) ? np.GetString() : "APT";
                    var time = TryGetProp(a, "LocalDisplay", out var tp) ? tp.GetString() : "";
                    var status = TryGetProp(a, "Status", out var sp) ? sp.GetString() : "";
                    var statusTr = TranslateStatus(status);
                    return $"- {num}: {time} ({statusTr})";
                }

                if (onlyCancelledRequested)
                {
                    if (cancelledList.Count == 0)
                    {
                        var noCancelText = "Sistemde iptal edilmiş bir randevunuz bulunmamaktadır.";
                        if (activeList.Count > 0)
                        {
                            noCancelText += $"\n\nKayıtlı aktif randevularınız ({activeList.Count} adet):\n" +
                                            string.Join("\n", activeList.Select(FormatApptLine));
                        }
                        var msg = conversation.AddMessage(MessageRole.Assistant, noCancelText, "ListAppointments");
                        return new AgentResponseDto(conversation.Id, msg.Id, "assistant", noCancelText, false, null, null, false);
                    }

                    var cancelLines = string.Join("\n", cancelledList.Select(FormatApptLine));
                    var cancelReply = $"İptal edilen randevularınız ({cancelledList.Count} adet):\n{cancelLines}";
                    var cancelMsg = conversation.AddMessage(MessageRole.Assistant, cancelReply, "ListAppointments");
                    return new AgentResponseDto(conversation.Id, cancelMsg.Id, "assistant", cancelReply, false, null, null, false);
                }

                var sb = new StringBuilder();
                sb.AppendLine($"Kayıtlı randevularınız (Toplam {sortedAppts.Count} — {activeList.Count} Aktif, {cancelledList.Count} İptal Edildi):");

                if (activeList.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Aktif / Oluşturulan Randevularınız:");
                    foreach (var a in activeList)
                        sb.AppendLine(FormatApptLine(a));
                }

                if (cancelledList.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("İptal Edilen Randevularınız:");
                    foreach (var a in cancelledList)
                        sb.AppendLine(FormatApptLine(a));
                }

                var reply = sb.ToString().TrimEnd();
                var listMsg = conversation.AddMessage(MessageRole.Assistant, reply, "ListAppointments");
                return new AgentResponseDto(conversation.Id, listMsg.Id, "assistant", reply, false, null, null, false);
            }

            var emptyReply = "Sistemde kayıtlı bir randevunuz bulunmuyor.";
            var emptyMsg = conversation.AddMessage(MessageRole.Assistant, emptyReply, "ListAppointmentsEmpty");
            return new AgentResponseDto(conversation.Id, emptyMsg.Id, "assistant", emptyReply, false, null, null, false);
        }

        // -----------------------------------------------------------------
        // 4. WORKING HOURS & OPEN DAYS
        // -----------------------------------------------------------------
        if (normalized.Contains("çalışma saat") ||
            normalized.Contains("calisma saat") ||
            normalized.Contains("çalışma gün") ||
            normalized.Contains("calisma gun") ||
            normalized.Contains("mesai") ||
            normalized.Contains("ne zaman açık") ||
            normalized.Contains("ne zaman acik") ||
            normalized.Contains("hangi günler açık") ||
            normalized.Contains("hangi gunler acik") ||
            normalized.Contains("kaçta açık") ||
            normalized.Contains("kacta acik") ||
            normalized.Contains("kaça kadar açık") ||
            normalized.Contains("kaca kadar acik") ||
            normalized.Contains("açık mısınız") ||
            normalized.Contains("acik misiniz") ||
            normalized.Contains("hizmet veriyor") ||
            normalized.Contains("hizmet veriyorsunuz") ||
            normalized.Contains("haftanın 7 günü") ||
            normalized.Contains("7 gün") ||
            normalized.Contains("haftasonu açık") ||
            normalized.Contains("hafta sonu açık") ||
            normalized.Contains("pazar açık") ||
            normalized.Contains("pazar günü açık") ||
            normalized.Contains("pazar gunu acik") ||
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
                            var targetDayOfWeek = (DayOfWeek)dayNum;
                            if (TryGetProp(slotsDoc.RootElement, "Slots", out var sProp) && sProp.ValueKind == JsonValueKind.Array)
                            {
                                var daySlots = sProp.EnumerateArray().Where(s =>
                                {
                                    if (TryGetProp(s, "StartUtc", out var su) && su.TryGetDateTimeOffset(out var sDto))
                                    {
                                        return TimeZoneInfo.ConvertTime(sDto, orgTimeZone).DayOfWeek == targetDayOfWeek;
                                    }
                                    if (TryGetProp(s, "Date", out var dProp) && DateOnly.TryParseExact(dProp.GetString(), "dd.MM.yyyy", out var dVal))
                                    {
                                        return dVal.DayOfWeek == targetDayOfWeek;
                                    }
                                    return false;
                                }).ToList();

                                if (daySlots.Count > 0)
                                {
                                    isOpen = true;
                                    var firstStart = daySlots.Select(GetSlotStartTime).Where(s => !string.IsNullOrEmpty(s)).OrderBy(s => s).FirstOrDefault() ?? "10:00";
                                    var lastEnd = daySlots.Select(GetSlotEndTime).Where(s => !string.IsNullOrEmpty(s)).OrderByDescending(s => s).FirstOrDefault() ?? "17:00";
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

                    var isSpecificallyAskingSunday = normalized.Contains("pazar") && !normalized.Contains("pazartesi");
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
                                       "\n\nRandevu almak istediğiniz tarihi veya saati belirtebilirsiniz (Örnek: 'Yarın saat 16:00 için randevu almak istiyorum').";

                    var msg = conversation.AddMessage(MessageRole.Assistant, scheduleText, "WorkingHours");
                    return new AgentResponseDto(conversation.Id, msg.Id, "assistant", scheduleText, false, null, null, false);
                }
            }
        }

        // -----------------------------------------------------------------
        // 5. ADDRESS / CONTACT / LOCATION
        // -----------------------------------------------------------------
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

        // -----------------------------------------------------------------
        // 6. SERVICES / PRICING / ABOUT
        // -----------------------------------------------------------------
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

        // -----------------------------------------------------------------
        // 7. GREETING (Only if not accompanied by a booking/date/time request)
        // -----------------------------------------------------------------
        var requestedDate = ResolveTargetDate(normalized, nowLocal);
        var requestedTime = TryExtractTime(normalized);

        bool hasActionWords = requestedDate.HasValue ||
                              !string.IsNullOrEmpty(requestedTime) ||
                              normalized.Contains("randevu") ||
                              normalized.Contains("müsait") ||
                              normalized.Contains("musait") ||
                              normalized.Contains("uygun") ||
                              normalized.Contains("saat") ||
                              normalized.Contains("gün") ||
                              normalized.Contains("gun") ||
                              normalized.Contains("boş") ||
                              normalized.Contains("bos");

        if (!hasActionWords &&
            (normalized.StartsWith("merhaba") ||
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
             normalized == "slm"))
        {
            var orgName = organization?.Name ?? "Kurumumuza";
            var greetingText = $"Merhaba! {orgName} randevu asistanına hoş geldiniz. Size nasıl yardımcı olabilirim?\n\n" +
                               "- 📅 **Randevu Al:** 'Yarın saat 16:00'da randevu almak istiyorum' veya 'Yarın öğleden sonra 4'te randevu'\n" +
                               "- 🗓️ **Müsait Gün & Saatler:** 'Müsait günleri ve saatleri listele'\n" +
                               "- 📋 **Randevu Takibi:** 'Oluşturduğum ve iptal ettiğim randevularımı listele'\n" +
                               "- ❌ **Randevu İptali:** 'Yarınki randevumu iptal etmek istiyorum'\n" +
                               "- 🕒 **Çalışma Saatleri:** 'Çalışma saatleriniz nedir?'";

            var msg = conversation.AddMessage(MessageRole.Assistant, greetingText, "Greeting");
            return new AgentResponseDto(conversation.Id, msg.Id, "assistant", greetingText, false, null, null, false);
        }

        // -----------------------------------------------------------------
        // 8. GRATITUDE / FAREWELL
        // -----------------------------------------------------------------
        if (!hasActionWords &&
            (normalized.Contains("teşekkür") ||
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
             normalized.Contains("iyi calismalar")))
        {
            var thanksText = "Rica ederim! Yardımcı olabileceğim başka bir konu veya randevu talebiniz olursa her zaman buradayım. Sağlıklı ve iyi günler dilerim!";
            var msg = conversation.AddMessage(MessageRole.Assistant, thanksText, "Gratitude");
            return new AgentResponseDto(conversation.Id, msg.Id, "assistant", thanksText, false, null, null, false);
        }

        // -----------------------------------------------------------------
        // 9. HELP / CAPABILITIES
        // -----------------------------------------------------------------
        if (normalized.Contains("yardım") ||
            normalized.Contains("yardim") ||
            normalized.Contains("ne yapabilirsin") ||
            normalized.Contains("neler yapabilirsin") ||
            normalized.Contains("nasıl randevu") ||
            normalized.Contains("nasil randevu") ||
            normalized.Contains("komutlar"))
        {
            var helpText = "Size şu konularda anında yardımcı olabilirim:\n" +
                           "- 📅 **Randevu Alma:** 'Yarın saat 16'da randevu almak istiyorum', 'Cuma öğleden sonra 4'te randevu'\n" +
                           "- 🗓️ **Müsait Gün ve Saatler:** 'Müsait günler ve saatler neler?', 'Yarın için uygun saatler'\n" +
                           "- 📋 **Randevu Takibi:** 'Mevcut randevularım', 'Oluşturduğum ve iptal ettiğim randevular'\n" +
                           "- ❌ **Randevu İptali:** 'Yarın saat 16'daki randevumu iptal etmek istiyorum'\n" +
                           "- 🕒 **Çalışma Saatleri:** 'Çalışma saatleriniz nedir?'\n" +
                           "- 📍 **İletişim & Adres:** 'Adresiniz ve telefon numaranız nedir?'\n\n" +
                           "Hangi konuda yardımcı olmamı istersiniz?";

            var msg = conversation.AddMessage(MessageRole.Assistant, helpText, "Help");
            return new AgentResponseDto(conversation.Id, msg.Id, "assistant", helpText, false, null, null, false);
        }

        // -----------------------------------------------------------------
        // 10. AVAILABLE DATES, AVAILABLE SLOTS, AND APPOINTMENT BOOKING FLOW
        // -----------------------------------------------------------------
        bool explicitDateInCurrentMessage = requestedDate.HasValue;

        // If user specified a time (or asked for other hours) without a date in this message,
        // inherit date context from recent conversation messages (e.g. previous slot listing).
        if (!requestedDate.HasValue && (!string.IsNullOrEmpty(requestedTime) || normalized.Contains("diğer uygun") || normalized.Contains("diger uygun") || normalized.Contains("farklı bir saat") || normalized.Contains("farkli bir saat")) && conversation.Messages.Any())
        {
            foreach (var m in conversation.Messages
                         .Where(x => x.Intent is "ListAvailableSlots" or "ProposeBooking" or null)
                         .OrderByDescending(x => x.CreatedAt)
                         .Take(6))
            {
                var prevDate = ResolveTargetDate(NormalizeTurkishText(m.Content), nowLocal);
                if (prevDate.HasValue)
                {
                    requestedDate = prevDate;
                    break;
                }
            }
        }

        bool asksAvailableDays = (normalized.Contains("gün") || normalized.Contains("gun") || normalized.Contains("takvim") || normalized.Contains("tarih")) &&
                                 (normalized.Contains("müsait") || normalized.Contains("musait") || normalized.Contains("uygun") || normalized.Contains("boş") || normalized.Contains("bos") || normalized.Contains("hangi") || normalized.Contains("listele") || normalized.Contains("göster") || normalized.Contains("goster"));

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
                                     normalized.Contains("almak") ||
                                     normalized.Contains("oluştur") ||
                                     normalized.Contains("olustur") ||
                                     normalized.Contains("rezerve") ||
                                     normalized.Contains("getir") ||
                                     normalized.Contains("göster") ||
                                     normalized.Contains("goster") ||
                                     normalized.Contains("listele") ||
                                     normalized.Contains("bakabilir") ||
                                     normalized.Contains("var mı") ||
                                     normalized.Contains("var mi") ||
                                     asksAvailableDays;

        bool isShortDateAnswer = requestedDate.HasValue && normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4;
        bool isSlotQueryIntent = hasAppointmentKeyword || isShortDateAnswer || !string.IsNullOrEmpty(requestedTime);

        if (!isSlotQueryIntent)
        {
            var fallbackText = "Size yardımcı olmaktan memnuniyet duyarım:\n" +
                               "- 📅 Randevu almak için gün ve saat belirtebilirsiniz (Örn: 'Yarın saat 16:00'da randevu almak istiyorum' veya 'Yarın öğleden sonra 4'te randevu')\n" +
                               "- 🗓️ Boş gün ve saatleri görmek için 'Müsait günleri ve saatleri listele' yazabilirsiniz\n" +
                               "- 📋 Oluşturduğunuz ve iptal ettiğiniz randevuları görmek için 'Randevularımı listele' yazabilirsiniz\n" +
                               "- 🕒 Kurum çalışma saatleri için 'Çalışma saatleriniz nedir?' yazabilirsiniz.";
            var fallbackMsg = conversation.AddMessage(MessageRole.Assistant, fallbackText, "GeneralFallback");
            return new AgentResponseDto(conversation.Id, fallbackMsg.Id, "assistant", fallbackText, false, null, null, false);
        }

        DateOnly queryStartDate;
        DateOnly queryEndDate;

        if (requestedDate.HasValue && (explicitDateInCurrentMessage || !asksAvailableDays))
        {
            queryStartDate = requestedDate.Value;
            queryEndDate = requestedDate.Value;
        }
        else
        {
            queryStartDate = DateOnly.FromDateTime(nowLocal);
            queryEndDate = DateOnly.FromDateTime(nowLocal.AddDays(14));
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

        if (!string.IsNullOrWhiteSpace(slotsResult.Content))
        {
            var doc = JsonDocument.Parse(slotsResult.Content);
            List<JsonElement> allSlots = new();

            if (TryGetProp(doc.RootElement, "Slots", out var slotsProp) && slotsProp.ValueKind == JsonValueKind.Array)
            {
                allSlots = slotsProp.EnumerateArray().ToList();
            }

            var cultureTr = new CultureInfo("tr-TR");

            // If user asked for available days/calendar without targeting a single specific day/time
            if (asksAvailableDays && !explicitDateInCurrentMessage && string.IsNullOrEmpty(requestedTime))
            {
                if (allSlots.Count == 0)
                {
                    var noDaysText = "Önümüzdeki 14 gün için müsait randevu günü ve saati bulunamadı.";
                    var noDaysMsg = conversation.AddMessage(MessageRole.Assistant, noDaysText, "NoSlotsAvailable");
                    return new AgentResponseDto(conversation.Id, noDaysMsg.Id, "assistant", noDaysText, false, null, null, false);
                }

                var groupedByDate = allSlots
                    .Select(s =>
                    {
                        var ld = TryGetProp(s, "LocalDisplay", out var d) ? d.GetString() ?? "" : "";
                        var datePrefix = ld.Length >= 10 ? ld[..10] : "";
                        return (Slot: s, DateStr: datePrefix, StartTime: GetSlotStartTime(s), EndTime: GetSlotEndTime(s));
                    })
                    .Where(x => !string.IsNullOrEmpty(x.DateStr))
                    .GroupBy(x => x.DateStr)
                    .Take(10)
                    .ToList();

                var sbDays = new StringBuilder();
                sbDays.AppendLine($"{organization?.Name ?? "Kurumumuz"} için müsait günler ve boş saat aralıkları:");
                sbDays.AppendLine();

                foreach (var grp in groupedByDate)
                {
                    string dayLabel = grp.Key;
                    if (DateOnly.TryParseExact(grp.Key, "dd.MM.yyyy", out var dOnly))
                    {
                        dayLabel = $"{dOnly.ToString("dd.MM.yyyy")} {ToTurkishDayName(dOnly.DayOfWeek)}";
                    }

                    var times = grp.Select(x => x.StartTime).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
                    var previewTimes = string.Join(", ", times.Take(6)) + (times.Count > 6 ? $" ... (+{times.Count - 6} saat)" : "");
                    sbDays.AppendLine($"• **{dayLabel}** ({times.Count} müsait saat): {previewTimes}");
                }

                // Also list the nearest day's slots so the user can click a slot button directly
                var firstDayGroup = groupedByDate.First();
                sbDays.AppendLine();
                sbDays.AppendLine($"{firstDayGroup.Key} günü için müsait randevu saatleri:");
                foreach (var item in firstDayGroup)
                {
                    sbDays.AppendLine($"- {item.StartTime} - {item.EndTime}");
                }

                sbDays.AppendLine();
                sbDays.Append("Randevu almak istediğiniz günü ve saati yazabilirsiniz (Örnek: 'Yarın saat 16:00 için randevu almak istiyorum' veya 'Yarın öğleden sonra 4'te randevu').");

                var daysReply = sbDays.ToString();
                var daysMsg = conversation.AddMessage(MessageRole.Assistant, daysReply, "ListAvailableSlots");
                return new AgentResponseDto(conversation.Id, daysMsg.Id, "assistant", daysReply, false, null, null, false);
            }

            List<JsonElement> slots = allSlots;

            // Filter slots to the requested date if specified
            if (requestedDate.HasValue)
            {
                var targetDateFormatted = requestedDate.Value.ToString("dd.MM.yyyy");
                slots = allSlots.Where(s =>
                {
                    var ld = TryGetProp(s, "LocalDisplay", out var d) ? d.GetString() ?? "" : "";
                    return ld.StartsWith(targetDateFormatted);
                }).ToList();
            }
            else if (!string.IsNullOrEmpty(requestedTime) && allSlots.Count > 0)
            {
                // User gave a time (e.g. "saat 16da randevu almak istiyorum") without a date:
                // find the earliest available slot matching that time
                var matchingTimeSlots = allSlots.Where(s => SlotMatchesRequestedTime(s, requestedTime)).ToList();
                if (matchingTimeSlots.Count > 0)
                {
                    slots = matchingTimeSlots;
                }
                else
                {
                    var firstSlotDisplay = TryGetProp(allSlots[0], "LocalDisplay", out var fd) ? fd.GetString() ?? "" : "";
                    if (firstSlotDisplay.Length >= 10)
                    {
                        var firstDatePrefix = firstSlotDisplay[..10];
                        slots = allSlots.Where(s =>
                        {
                            var ld = TryGetProp(s, "LocalDisplay", out var d) ? d.GetString() ?? "" : "";
                            return ld.StartsWith(firstDatePrefix);
                        }).ToList();
                    }
                }
            }
            else if (allSlots.Count > 0)
            {
                var firstSlotDisplay = TryGetProp(allSlots[0], "LocalDisplay", out var fd) ? fd.GetString() ?? "" : "";
                if (firstSlotDisplay.Length >= 10)
                {
                    var firstDatePrefix = firstSlotDisplay[..10];
                    slots = allSlots.Where(s =>
                    {
                        var ld = TryGetProp(s, "LocalDisplay", out var d) ? d.GetString() ?? "" : "";
                        return ld.StartsWith(firstDatePrefix);
                    }).ToList();
                }
            }

            // If user did NOT specify an exact hour, apply morning/afternoon filter if requested
            if (string.IsNullOrEmpty(requestedTime))
            {
                bool preferAfternoon = normalized.Contains("öğleden sonra") || normalized.Contains("ogleden sonra") || normalized.Contains("öğlen") || normalized.Contains("oglen") || normalized.Contains("akşam") || normalized.Contains("aksam");
                bool preferMorning = normalized.Contains("sabah") || normalized.Contains("öğleden önce") || normalized.Contains("ogleden once");

                if (preferAfternoon)
                {
                    var afternoon = slots.Where(s => string.Compare(GetSlotStartTime(s), "12:00", StringComparison.Ordinal) >= 0).ToList();
                    if (afternoon.Count > 0) slots = afternoon;
                }
                else if (preferMorning)
                {
                    var morning = slots.Where(s => string.Compare(GetSlotStartTime(s), "12:00", StringComparison.Ordinal) < 0).ToList();
                    if (morning.Count > 0) slots = morning;
                }
            }

            if (slots.Count == 0)
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
                    noSlotsText = "Önümüzdeki günler için uygun müsait randevu saati bulunamadı. Lütfen daha sonra tekrar deneyiniz.";
                }

                var noSlotsMsg = conversation.AddMessage(MessageRole.Assistant, noSlotsText, "NoSlotsAvailable");
                return new AgentResponseDto(conversation.Id, noSlotsMsg.Id, "assistant", noSlotsText, false, null, null, false);
            }

            // Check if user requested a specific time (e.g. "saat 16da", "öğleden sonra 4te", "13:00")
            if (!string.IsNullOrEmpty(requestedTime))
            {
                var matchedSlot = slots.FirstOrDefault(s => SlotMatchesRequestedTime(s, requestedTime));

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
            }

            // Otherwise, list available slots for the user
            string headerPrefix;
            if (!string.IsNullOrEmpty(requestedTime))
            {
                var dateName = requestedDate.HasValue
                    ? $"{requestedDate.Value.ToString("d MMMM yyyy", cultureTr)} günü "
                    : "";
                headerPrefix = $"Belirttiğiniz saat ({requestedTime}) {dateName}için maalesef dolu veya müsait değil. Müsait olan saatler:\n";
            }
            else if (requestedDate.HasValue)
            {
                var dayName = ToTurkishDayName(requestedDate.Value.DayOfWeek);
                headerPrefix = $"{requestedDate.Value.ToString("d MMMM yyyy", cultureTr)} {dayName} günü için müsait randevu saatleri:\n";
            }
            else
            {
                var firstSlotDisplay = TryGetProp(slots[0], "LocalDisplay", out var fd) ? fd.GetString() ?? "" : "";
                var firstSlotDate = firstSlotDisplay.Length >= 10 ? firstSlotDisplay[..10] : "En yakın";
                headerPrefix = $"{firstSlotDate} için müsait randevu saatleri:\n";
            }

            var morningSlots = slots.Where(s => string.Compare(GetSlotStartTime(s), "12:00", StringComparison.Ordinal) < 0).ToList();
            var afternoonSlots = slots.Where(s => string.Compare(GetSlotStartTime(s), "12:00", StringComparison.Ordinal) >= 0).ToList();

            var sb = new StringBuilder();
            sb.AppendLine(headerPrefix.TrimEnd());
            sb.AppendLine();

            if (morningSlots.Count > 0)
            {
                sb.AppendLine("Sabah:");
                foreach (var s in morningSlots)
                {
                    sb.AppendLine($"- {GetSlotStartTime(s)} - {GetSlotEndTime(s)}");
                }
            }

            if (afternoonSlots.Count > 0)
            {
                if (morningSlots.Count > 0) sb.AppendLine();
                sb.AppendLine("Öğleden Sonra:");
                foreach (var s in afternoonSlots)
                {
                    sb.AppendLine($"- {GetSlotStartTime(s)} - {GetSlotEndTime(s)}");
                }
            }

            if (morningSlots.Count == 0 && afternoonSlots.Count == 0)
            {
                foreach (var s in slots)
                {
                    var ld = TryGetProp(s, "LocalDisplay", out var p) ? p.GetString() : "Müsait Saat";
                    sb.AppendLine($"- {ld}");
                }
            }

            // If user didn't specify a single date and there are other available dates in allSlots, show them too
            if (!requestedDate.HasValue && allSlots.Count > slots.Count)
            {
                var otherDates = allSlots
                    .Select(s => TryGetProp(s, "LocalDisplay", out var d) ? d.GetString() ?? "" : "")
                    .Where(ld => ld.Length >= 10)
                    .Select(ld => ld[..10])
                    .Distinct()
                    .Skip(1)
                    .Take(6)
                    .Select(dStr => DateOnly.TryParseExact(dStr, "dd.MM.yyyy", out var dVal)
                        ? $"{dStr} ({ToTurkishDayName(dVal.DayOfWeek)})"
                        : dStr)
                    .ToList();

                if (otherDates.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"📅 Diğer müsait günler: {string.Join(", ", otherDates)}");
                }
            }

            var exampleSlotTime = slots.Select(GetSlotStartTime)
                                       .FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "10:00";
            sb.AppendLine();
            sb.Append($"Hangi saati rezerve etmek istersiniz? (Örnek: '{exampleSlotTime}' veya 'Saat 16:00'da randevu almak istiyorum' yazabilirsiniz)");

            var slotsListText = sb.ToString();
            var slotsListMsg = conversation.AddMessage(MessageRole.Assistant, slotsListText, "ListAvailableSlots");
            return new AgentResponseDto(conversation.Id, slotsListMsg.Id, "assistant", slotsListText, false, null, null, false);
        }

        var defaultText = "Merhaba! Randevu almak, müsait gün ve saatleri öğrenmek veya mevcut/iptal edilen randevularınızı sorgulamak için size yardımcı olabilirim.";
        var defaultMsg = conversation.AddMessage(MessageRole.Assistant, defaultText, "Greeting");
        return new AgentResponseDto(conversation.Id, defaultMsg.Id, "assistant", defaultText, false, null, null, false);
    }

    private async Task<List<JsonElement>> FetchCustomerAppointmentsAsync(
        Guid? organizationId,
        Guid customerId,
        CancellationToken ct)
    {
        var apptsResult = await _mcpClient.CallToolAsync("get_customer_appointments", new
        {
            organization_id = organizationId,
            customer_id = customerId
        }, ct);

        List<JsonElement> apptsArray = new();
        if (apptsResult.Success && !string.IsNullOrWhiteSpace(apptsResult.Content))
        {
            try
            {
                var doc = JsonDocument.Parse(apptsResult.Content);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    apptsArray = doc.RootElement.EnumerateArray().Select(x => x.Clone()).ToList();
                }
                else if (TryGetProp(doc.RootElement, "Appointments", out var aProp) && aProp.ValueKind == JsonValueKind.Array)
                {
                    apptsArray = aProp.EnumerateArray().Select(x => x.Clone()).ToList();
                }
            }
            catch (JsonException) { }
        }

        return apptsArray;
    }

    private static bool SlotMatchesRequestedTime(JsonElement slot, string requestedTime)
    {
        var startTime = GetSlotStartTime(slot);
        if (startTime == requestedTime)
            return true;

        // Fallback: if requestedTime is 01:00..11:59 (e.g. "04:00") and afternoon slot "16:00" exists
        if (requestedTime.Length == 5 &&
            int.TryParse(requestedTime[..2], out var hour) &&
            hour >= 1 && hour <= 11)
        {
            var pmTime = $"{hour + 12:D2}:{requestedTime[3..5]}";
            if (startTime == pmTime)
                return true;
        }

        return false;
    }

    private static string NormalizeTurkishText(string input)
    {
        return input.Trim().ToLower(new CultureInfo("tr-TR"));
    }

    private static DateOnly? ResolveTargetDate(string normalized, DateTime referenceLocal)
    {
        // 1. Relative keywords ("bugün", "bugünkü", "yarın", "yarınki", "yarından sonra", "öbür gün", "X gün sonra")
        if (normalized.Contains("bugün") || normalized.Contains("bugun"))
            return DateOnly.FromDateTime(referenceLocal);

        if (normalized.Contains("yarından sonra") || normalized.Contains("yarindan sonra") ||
            normalized.Contains("öbür gün") || normalized.Contains("obur gun") ||
            normalized.Contains("ertesi gün") || normalized.Contains("ertesi gun"))
        {
            return DateOnly.FromDateTime(referenceLocal.AddDays(2));
        }

        if (normalized.Contains("yarın") || normalized.Contains("yarin"))
            return DateOnly.FromDateTime(referenceLocal.AddDays(1));

        var daysLaterMatch = Regex.Match(normalized, @"\b(?<days>\d{1,2})\s*g[üu]n\s*sonra\b");
        if (daysLaterMatch.Success && int.TryParse(daysLaterMatch.Groups["days"].Value, out var daysLater) && daysLater >= 1 && daysLater <= 60)
        {
            return DateOnly.FromDateTime(referenceLocal.AddDays(daysLater));
        }

        // 2. ISO date format: yyyy-MM-dd
        var isoMatch = Regex.Match(normalized, @"\b(?<year>20\d{2})-(?<month>0[1-9]|1[0-2])-(?<day>0[1-9]|[12]\d|3[01])\b");
        if (isoMatch.Success && DateOnly.TryParseExact(isoMatch.Value, "yyyy-MM-dd", out var isoDate))
        {
            return isoDate;
        }

        // 3. Turkish month names with day: e.g. "4 kasım", "4 kasımda", "4 kasımdaki", "12 aralık 2026"
        var monthRegex = new Regex(@"\b(?<day>\d{1,2})\s*(?<month>ocak|şubat|subat|mart|nisan|mayıs|mayis|haziran|temmuz|ağustos|agustos|eylül|eylul|ekim|kasım|kasim|aralık|aralik)(?:\s+(?<year>20\d{2}))?", RegexOptions.IgnoreCase);
        var match = monthRegex.Match(normalized);
        if (match.Success && int.TryParse(match.Groups["day"].Value, out var day))
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
                var year = match.Groups["year"].Success && int.TryParse(match.Groups["year"].Value, out var y)
                    ? y
                    : referenceLocal.Year;
                try
                {
                    return new DateOnly(year, month, day);
                }
                catch { }
            }
        }

        // 4. Numeric dates: dd.MM.yyyy or dd/MM/yyyy or dd.MM (excluding time expressions like "saat 14.00" or "14.30")
        var fullDateMatch = Regex.Match(normalized, @"\b(?<day>0?[1-9]|[12]\d|3[01])[\.\/\-](?<month>0?[1-9]|1[0-2])[\.\/\-](?<year>20\d{2})\b");
        if (fullDateMatch.Success &&
            int.TryParse(fullDateMatch.Groups["day"].Value, out var fDay) &&
            int.TryParse(fullDateMatch.Groups["month"].Value, out var fMonth) &&
            int.TryParse(fullDateMatch.Groups["year"].Value, out var fYear))
        {
            try
            {
                return new DateOnly(fYear, fMonth, fDay);
            }
            catch { }
        }

        var shortNumericMatch = Regex.Match(normalized, @"(?<!saat\s*)(?<day>0?[1-9]|[12]\d|3[01])[\.\/](?<month>0?[1-9]|1[0-2])\b(?!\.\d)");
        if (shortNumericMatch.Success &&
            int.TryParse(shortNumericMatch.Groups["day"].Value, out var sDay) &&
            int.TryParse(shortNumericMatch.Groups["month"].Value, out var sMonth))
        {
            try
            {
                return new DateOnly(referenceLocal.Year, sMonth, sDay);
            }
            catch { }
        }

        // 5. Day of week (e.g. "pazartesi", "salı", "çarşamba", "perşembe", "cuma", "cumartesi", "pazar")
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

        if (normalized.Contains("hafta sonu") || normalized.Contains("haftasonu"))
        {
            var currentDay = referenceLocal.DayOfWeek;
            int daysUntilSat = ((int)DayOfWeek.Saturday - (int)currentDay + 7) % 7;
            if (daysUntilSat == 0 && referenceLocal.Hour >= 17) daysUntilSat = 7;
            return DateOnly.FromDateTime(referenceLocal.AddDays(daysUntilSat));
        }

        bool isNextWeek = normalized.Contains("haftaya") || normalized.Contains("gelecek hafta") || normalized.Contains("önümüzdeki hafta") || normalized.Contains("onumuzdeki hafta");

        foreach (var kvp in daysOfWeek.OrderByDescending(k => k.Key.Length))
        {
            bool matchedDay = kvp.Key == "pazar"
                ? Regex.IsMatch(normalized, @"\bpazar(?![tT][eE][sS][iI])", RegexOptions.IgnoreCase)
                : normalized.Contains(kvp.Key);

            if (matchedDay)
            {
                var currentDay = referenceLocal.DayOfWeek;
                int daysToAdd = ((int)kvp.Value - (int)currentDay + 7) % 7;
                if (daysToAdd == 0 && (referenceLocal.Hour >= 17 || isNextWeek))
                    daysToAdd = 7;
                else if (isNextWeek && daysToAdd < 7)
                    daysToAdd += 7;
                return DateOnly.FromDateTime(referenceLocal.AddDays(daysToAdd));
            }
        }

        return null;
    }

    private static string? TryExtractTime(string normalized)
    {
        bool isAfternoon = normalized.Contains("öğleden sonra") ||
                           normalized.Contains("ogleden sonra") ||
                           normalized.Contains("akşamüstü") ||
                           normalized.Contains("aksamustu") ||
                           normalized.Contains("akşam") ||
                           normalized.Contains("aksam") ||
                           normalized.Contains("öğlen") ||
                           normalized.Contains("oglen");

        bool isNightOrEarlyMorning = normalized.Contains("gece") || normalized.Contains("sabaha karşı");

        static string FormatNormalizedTime(int hour, int min, bool afternoon, bool nightOrEarly)
        {
            if (afternoon && hour >= 1 && hour <= 11)
                hour += 12;
            else if (!nightOrEarly && hour >= 1 && hour <= 7)
                hour += 12;

            return $"{hour:D2}:{min:D2}";
        }

        // 1. Match "öğleden sonra 4te", "öğleden sonra saat 4'te", "akşam 5'te", "sabah 9'da"
        var periodMatch = Regex.Match(
            normalized,
            @"\b(?:öğleden\s+sonra|ogleden\s+sonra|akşamüstü|aksamustu|akşam|aksam|sabah|öğlen|oglen)\s+(?:saat\s+)?(?<hour>[01]?\d|2[0-3])(?:\s*[:.]\s*(?<min>[0-5]\d)|\s*(?<half>buçuk|bucuk|yarım|yarim))?(?:'?(?:de|da|te|ta|deki|daki|teki|taki|dekini|dakini|tekini|takini|yi|yı|yu|yü|i|ı|u|ü|e|a|ye|ya))?(?=\s|[.,;!?]|$|\b)");
        if (periodMatch.Success)
        {
            var hour = int.Parse(periodMatch.Groups["hour"].Value);
            var min = periodMatch.Groups["min"].Success
                ? int.Parse(periodMatch.Groups["min"].Value)
                : (periodMatch.Groups["half"].Success ? 30 : 0);
            return FormatNormalizedTime(hour, min, isAfternoon, isNightOrEarlyMorning);
        }

        // 2. Match "saat 16da", "saat 16'da", "saat 4te", "saat 14:00", "saat 14.30", "saat 4 buçuk"
        var saatMatch = Regex.Match(
            normalized,
            @"\bsaat\s*(?<hour>[01]?\d|2[0-3])(?:\s*[:.]\s*(?<min>[0-5]\d)|\s*(?<half>buçuk|bucuk|yarım|yarim))?(?:'?(?:de|da|te|ta|deki|daki|teki|taki|dekini|dakini|tekini|takini|yi|yı|yu|yü|i|ı|u|ü|e|a|ye|ya))?(?=\s|[.,;!?]|$|\b)");
        if (saatMatch.Success)
        {
            var hour = int.Parse(saatMatch.Groups["hour"].Value);
            var min = saatMatch.Groups["min"].Success
                ? int.Parse(saatMatch.Groups["min"].Value)
                : (saatMatch.Groups["half"].Success ? 30 : 0);
            return FormatNormalizedTime(hour, min, isAfternoon, isNightOrEarlyMorning);
        }

        // 3. Match "14:00", "16:30", or "14.00", "14.30" (where dot minutes are slot minutes 00/15/20/30/40/45, never month numbers 01..12)
        var colonTimeMatch = Regex.Match(normalized, @"\b(?<hour>[01]?\d|2[0-3]):(?<min>[0-5]\d)\b");
        if (colonTimeMatch.Success)
        {
            var hour = int.Parse(colonTimeMatch.Groups["hour"].Value);
            var min = int.Parse(colonTimeMatch.Groups["min"].Value);
            return FormatNormalizedTime(hour, min, isAfternoon, isNightOrEarlyMorning);
        }

        var dotTimeMatch = Regex.Match(normalized, @"\b(?<hour>[01]?\d|2[0-3])\.(?<min>00|15|20|30|40|45)\b(?!\.\d)");
        if (dotTimeMatch.Success)
        {
            var hour = int.Parse(dotTimeMatch.Groups["hour"].Value);
            var min = int.Parse(dotTimeMatch.Groups["min"].Value);
            return FormatNormalizedTime(hour, min, isAfternoon, isNightOrEarlyMorning);
        }

        // 4. Match standalone hour with Turkish time suffixes: "16da", "16'da", "4te", "4'te", "16daki", "4'teki", "4 buçukta", "16 için randevu"
        var suffixMatch = Regex.Match(
            normalized,
            @"\b(?<hour>[01]?\d|2[0-3])(?:\s*(?<half>buçuk|bucuk)(?:'?(?:ta|te|da|de|taki|teki))?|'?(?:deki|daki|teki|taki|dekini|dakini|tekini|takini|de|da|te|ta|ye|ya)|(?=\s*(?:randevu|için\s+randevu|icin\s+randevu|saatine|saatinde|saatindeki)))(?=\s|[.,;!?]|$|\b)");
        if (suffixMatch.Success)
        {
            var hour = int.Parse(suffixMatch.Groups["hour"].Value);
            var min = suffixMatch.Groups["half"].Success ? 30 : 0;
            return FormatNormalizedTime(hour, min, isAfternoon, isNightOrEarlyMorning);
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
        if (element.ValueKind != JsonValueKind.Object)
        {
            result = default;
            return false;
        }

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
        DateTime nowLocal,
        TimeZoneInfo orgTimeZone)
    {
        if (appointments == null || appointments.Count == 0) return null;

        // 1. Check for exact or partial GUID match (Id or SlotId)
        foreach (var appt in appointments)
        {
            var id = GetAppointmentId(appt).ToString().ToLowerInvariant();
            if (!string.IsNullOrEmpty(id))
            {
                if (normalized.Contains(id)) return appt;
                var shortId = id.Length >= 8 ? id[..8] : id;
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

        if (string.IsNullOrEmpty(targetTime) && tokens.Length == 1 && int.TryParse(tokens[0], out var singleNum) && singleNum >= 8 && singleNum <= 22 && singleNum > appointments.Count)
        {
            targetTime = $"{singleNum:D2}:00";
        }

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
        if (targetDate.HasValue && !string.IsNullOrEmpty(targetTime))
        {
            var matchBoth = appointments.FirstOrDefault(a =>
                AppointmentMatchesDate(a, targetDate.Value, orgTimeZone) &&
                AppointmentMatchesTime(a, targetTime));
            if (matchBoth.ValueKind != JsonValueKind.Undefined)
                return matchBoth;
        }

        // 5. Match by Time only (e.g. "16:00", "saat 16da", "öğleden sonra 4te")
        if (!string.IsNullOrEmpty(targetTime) && !targetDate.HasValue)
        {
            var matchingTimeAppts = appointments.Where(a => AppointmentMatchesTime(a, targetTime)).ToList();
            if (matchingTimeAppts.Count == 1)
            {
                return matchingTimeAppts[0];
            }
            if (matchingTimeAppts.Count > 1)
            {
                var todayMatch = matchingTimeAppts.FirstOrDefault(a => AppointmentMatchesDate(a, DateOnly.FromDateTime(nowLocal), orgTimeZone));
                if (todayMatch.ValueKind != JsonValueKind.Undefined)
                    return todayMatch;

                return null;
            }
        }

        // 6. Match by Date only (e.g. "yarınki randevumu iptal et", "bugünkü randevumu iptal et", "3 kasım")
        if (targetDate.HasValue && string.IsNullOrEmpty(targetTime))
        {
            var matchingDateAppts = appointments.Where(a => AppointmentMatchesDate(a, targetDate.Value, orgTimeZone)).ToList();
            if (matchingDateAppts.Count == 1)
            {
                return matchingDateAppts[0];
            }
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

        if (tokens.Length == 1 && int.TryParse(tokens[0], out var singleIdx) && singleIdx >= 1 && singleIdx <= appointments.Count)
        {
            return appointments[singleIdx - 1];
        }

        for (int i = 0; i < appointments.Count; i++)
        {
            var idx = i + 1;
            if (Regex.IsMatch(normalized.Trim(), $@"^(?:lütfen\s+|lutfen\s+|sadece\s+)?{idx}\.?(?:\s+(?:randevu\w*|iptal\w*|onayla\w*|olsun|lütfen|lutfen))?$"))
            {
                return appointments[i];
            }
        }

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

        for (int i = 0; i < appointments.Count; i++)
        {
            var idx = i + 1;
            if (Regex.IsMatch(normalized, $@"(?<!\d){idx}(?:'|\s*)?(?:inci|ıncı|üncü|uncu|nci|ncı|ncu|ncü|si)\b"))
            {
                return appointments[i];
            }
        }

        for (int i = 0; i < appointments.Count; i++)
        {
            var idx = i + 1;
            if (Regex.IsMatch(normalized, $@"(?<!\d){idx}(?:\.|\s+)(?:randevu|sıradaki|siradaki|olan|secenek|seçenek|nolu|numaralı|numarali)\b"))
            {
                return appointments[i];
            }
        }

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
        if (!string.IsNullOrEmpty(display))
        {
            // LocalDisplay is "dd.MM.yyyy HH:mm - HH:mm"
            var parts = display.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[1] == targetTime)
                return true;
            if (display.Contains($" {targetTime}"))
                return true;
        }

        if (TryGetProp(appt, "StartTime", out var st) && st.GetString()?.StartsWith(targetTime) == true) return true;
        if (TryGetProp(appt, "LocalStartTime", out var lst) && lst.GetString()?.StartsWith(targetTime) == true) return true;

        return false;
    }

    private static bool AppointmentMatchesDate(JsonElement appt, DateOnly targetDate, TimeZoneInfo orgTimeZone)
    {
        var dateStr = targetDate.ToString("dd.MM.yyyy");
        var display = TryGetProp(appt, "LocalDisplay", out var dp) ? dp.GetString() : "";
        if (!string.IsNullOrEmpty(display) && display.Contains(dateStr)) return true;

        if (TryGetProp(appt, "Date", out var dt) && dt.GetString()?.Contains(dateStr) == true) return true;

        if (TryGetProp(appt, "StartUtc", out var su) && su.TryGetDateTimeOffset(out var dto))
        {
            var localDto = TimeZoneInfo.ConvertTime(dto, orgTimeZone);
            if (DateOnly.FromDateTime(localDto.DateTime) == targetDate) return true;
        }

        return false;
    }
}
