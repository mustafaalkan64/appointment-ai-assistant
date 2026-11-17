using System.Text.Json;
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

public sealed class AiAgentOrchestrator : IAiAgentOrchestrator
{
    private static readonly HashSet<string> ConfirmationRequiredTools = new(StringComparer.Ordinal)
    {
        "create_appointment",
        "cancel_appointment",
        "confirm_appointment",
        "cancel_queue_request"
    };

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

        Conversation conversation;
        if (conversationId is { } id && id != Guid.Empty)
        {
            conversation = await _context.Conversations
                .Include(x => x.Messages)
                .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, ct)
                ?? throw new DomainValidationException($"Conversation '{id}' not found.");

            if (conversation.CustomerId != customerId)
                throw new DomainValidationException("Conversation does not belong to the specified customer.");
        }
        else
        {
            conversation = Conversation.Start(organizationId, customerId);
            _context.Conversations.Add(conversation);
        }

        var userMessage = conversation.AddMessage(MessageRole.User, messageText);
        AgentResponseDto response;

        if (conversation.Status == ConversationStatus.AwaitingConfirmation)
        {
            response = await HandlePendingConfirmationAsync(conversation, messageText, ct);
        }
        else
        {
            var history = BuildModelHistory(conversation, userMessage.Id);
            var modelResult = await _mcpClient.ChatAsync(
                messageText,
                organizationId,
                customerId,
                history,
                ct);

            if (!modelResult.Success)
            {
                var error = "Şu anda isteğinizi işlerken bir sorun oluştu. Lütfen biraz sonra tekrar deneyin.";
                var errorMessage = conversation.AddMessage(MessageRole.Assistant, error, "AgentUnavailable");
                response = ToResponse(conversation, errorMessage, error);
            }
            else if (modelResult.ToolName is { } toolName &&
                     !modelResult.ToolExecuted &&
                     modelResult.ToolArguments is { } arguments &&
                     ConfirmationRequiredTools.Contains(toolName))
            {
                var pendingId = GetActionId(arguments);
                if (pendingId == Guid.Empty)
                {
                    var clarification = "İşlem için gerekli kayıt kimliğini doğrulayamadım. Lütfen isteğinizi netleştirir misiniz?";
                    var clarificationMessage = conversation.AddMessage(MessageRole.Assistant, clarification, "ClarificationNeeded");
                    response = ToResponse(conversation, clarificationMessage, clarification);
                }
                else
                {
                    conversation.SetPendingConfirmation(pendingId, toolName, arguments.GetRawText());
                    var confirmationText = string.IsNullOrWhiteSpace(modelResult.Reply)
                        ? $"Bu işlemi gerçekleştirmemi onaylıyor musunuz? (Evet / Hayır)"
                        : $"{modelResult.Reply.Trim()}\n\nİşlemi gerçekleştirmemi onaylıyor musunuz? (Evet / Hayır)";
                    var confirmationMessage = conversation.AddMessage(
                        MessageRole.Assistant,
                        confirmationText,
                        "ProposeAction",
                        JsonSerializer.Serialize(new { toolName, arguments }));
                    response = new AgentResponseDto(
                        conversation.Id,
                        confirmationMessage.Id,
                        "assistant",
                        confirmationText,
                        RequiresConfirmation: true,
                        ProposedAction: toolName,
                        Summary: confirmationText,
                        IsCompleted: false);
                }
            }
            else
            {
                var reply = string.IsNullOrWhiteSpace(modelResult.Reply)
                    ? "Size nasıl yardımcı olabilirim?"
                    : modelResult.Reply.Trim();
                var toolTrace = modelResult.ToolExecuted
                    ? JsonSerializer.Serialize(new { modelResult.ToolName, modelResult.ToolResult })
                    : null;
                var assistantMessage = conversation.AddMessage(
                    MessageRole.Assistant,
                    reply,
                    modelResult.ToolExecuted ? "ToolResult" : null,
                    toolTrace);
                response = ToResponse(conversation, assistantMessage, reply);
            }
        }

        await _context.SaveChangesAsync(ct);
        return response;
    }

    private async Task<AgentResponseDto> HandlePendingConfirmationAsync(
        Conversation conversation,
        string messageText,
        CancellationToken ct)
    {
        var decision = ParseConfirmation(messageText);
        if (decision == ConfirmationDecision.No)
        {
            conversation.ClearPendingConfirmation();
            var reply = "İşlem iptal edildi. Başka nasıl yardımcı olabilirim?";
            var message = conversation.AddMessage(MessageRole.Assistant, reply, "ConfirmationRejected");
            return ToResponse(conversation, message, reply);
        }

        if (decision != ConfirmationDecision.Yes)
        {
            var clarification = "İşlemi gerçekleştirmemi onaylıyor musunuz? Lütfen Evet veya Hayır olarak yanıtlayın.";
            var message = conversation.AddMessage(MessageRole.Assistant, clarification, "ClarificationNeeded");
            return new AgentResponseDto(
                conversation.Id, message.Id, "assistant", clarification,
                RequiresConfirmation: true,
                ProposedAction: conversation.PendingOperation,
                Summary: null,
                IsCompleted: false);
        }

        if (string.IsNullOrWhiteSpace(conversation.PendingOperation) ||
            !ConfirmationRequiredTools.Contains(conversation.PendingOperation))
        {
            conversation.ClearPendingConfirmation();
            var invalid = "Bekleyen işlem doğrulanamadı. Lütfen işlemi yeniden başlatın.";
            var invalidMessage = conversation.AddMessage(MessageRole.Assistant, invalid, "PendingActionInvalid");
            return ToResponse(conversation, invalidMessage, invalid);
        }

        try
        {
            var arguments = JsonDocument.Parse(conversation.ContextJson).RootElement.Clone();
            var toolName = conversation.PendingOperation;
            var result = await _mcpClient.CallToolAsync(toolName, arguments, ct);
            if (!result.Success)
            {
                conversation.ClearPendingConfirmation();
                var errorText = $"İşlem tamamlanamadı: {result.ErrorMessage ?? result.Content}";
                var errorMessage = conversation.AddMessage(MessageRole.Assistant, errorText, "ToolExecutionFailed");
                return ToResponse(conversation, errorMessage, errorText);
            }

            conversation.Complete();
            var completionText = FormatCompletedAction(toolName, result.Content);
            var completedMessage = conversation.AddMessage(
                MessageRole.Assistant,
                completionText,
                "ConfirmedActionCompleted",
                JsonSerializer.Serialize(new { toolName, result = result.Content }));
            return new AgentResponseDto(
                conversation.Id,
                completedMessage.Id,
                "assistant",
                completionText,
                RequiresConfirmation: false,
                ProposedAction: null,
                Summary: "İşlem tamamlandı",
                IsCompleted: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute the confirmed MCP tool for conversation {ConversationId}.", conversation.Id);
            conversation.ClearPendingConfirmation();
            var errorText = "Onaylanan işlem tamamlanamadı. Lütfen yeniden deneyin.";
            var errorMessage = conversation.AddMessage(MessageRole.Assistant, errorText, "ToolExecutionFailed");
            return ToResponse(conversation, errorMessage, errorText);
        }
    }

    private static List<McpChatHistoryItem> BuildModelHistory(Conversation conversation, Guid currentUserMessageId)
    {
        var history = new List<McpChatHistoryItem>();
        foreach (var message in conversation.Messages
                     .Where(x => x.Id != currentUserMessageId)
                     .OrderBy(x => x.CreatedAt)
                     .TakeLast(12))
        {
            var role = message.Role.ToString().ToLowerInvariant();
            if (role is not ("user" or "assistant"))
                continue;

            // Do not feed the raw tool trace back into the model. Availability
            // responses can contain hundreds of slot records and make every
            // subsequent request disproportionately expensive.
            var content = message.Content;
            if (role == "assistant" && !string.IsNullOrWhiteSpace(message.ToolCallsJson))
            {
                content += "\n[Önceki mesajda araç kullanıldı; güncel bilgi için aracı yeniden çağır.]";
            }

            const int maxHistoryMessageChars = 2000;
            if (content.Length > maxHistoryMessageChars)
                content = content[..maxHistoryMessageChars] + "…";

            history.Add(new McpChatHistoryItem(role, content));
        }

        return history;
    }

    private static Guid GetActionId(JsonElement arguments)
    {
        foreach (var name in new[] { "slot_id", "appointment_id" })
        {
            if (arguments.TryGetProperty(name, out var value) &&
                Guid.TryParse(value.GetString(), out var id) &&
                id != Guid.Empty)
            {
                return id;
            }
        }
        return Guid.Empty;
    }

    private static ConfirmationDecision ParseConfirmation(string message)
    {
        var value = string.Join(' ', message
            .ToLowerInvariant()
            .Replace(',', ' ')
            .Replace('.', ' ')
            .Replace('!', ' ')
            .Replace('?', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (value is "evet" or "evet onaylıyorum" or "evet onayliyorum" or "onaylıyorum" or
            "onayliyorum" or "onayla" or "olur" or "tamam" or "kabul ediyorum" or "yes" or "confirm")
            return ConfirmationDecision.Yes;
        if (value is "hayır" or "hayir" or "vazgeç" or "vazgec" or "istemiyorum" or
            "iptal et" or "hayır vazgeçtim" or "hayir vazgectim" or "no" or "cancel")
            return ConfirmationDecision.No;
        return ConfirmationDecision.Unclear;
    }

    private static string FormatCompletedAction(string toolName, string rawResult)
    {
        return toolName switch
        {
            "create_appointment" => $"Randevunuz başarıyla onaylandı ve oluşturuldu. {TryExtractDisplay(rawResult)}",
            "cancel_appointment" => "Randevunuz başarıyla iptal edildi.",
            "confirm_appointment" => "Randevunuz başarıyla onaylandı.",
            "cancel_queue_request" => "Bekleme sırası talebiniz iptal edildi.",
            _ => "Onayladığınız işlem başarıyla tamamlandı."
        };
    }

    private static string TryExtractDisplay(string rawResult)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawResult);
            foreach (var name in new[] { "LocalDisplay", "AppointmentNumber" })
            {
                if (doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString() ?? string.Empty;
            }
        }
        catch (JsonException) { }
        return string.Empty;
    }

    private static AgentResponseDto ToResponse(Conversation conversation, ConversationMessage message, string text) =>
        new(conversation.Id, message.Id, "assistant", text, false, null, null, false);

    private enum ConfirmationDecision
    {
        Unclear,
        Yes,
        No
    }
}
