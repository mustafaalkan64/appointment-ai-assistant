using System.Text.Json;
using AppointmentBooking.McpServer.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AppointmentBooking.McpServer.Services.Ollama;

public sealed class McpFunctionCallingOrchestrator : IMcpFunctionCallingOrchestrator
{
    private readonly IOllamaClient _ollamaClient;
    private readonly McpToolRegistry _toolRegistry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<McpFunctionCallingOrchestrator> _logger;

    public McpFunctionCallingOrchestrator(
        IOllamaClient _ollamaClient,
        McpToolRegistry toolRegistry,
        IConfiguration configuration,
        ILogger<McpFunctionCallingOrchestrator> logger)
    {
        this._ollamaClient = _ollamaClient;
        _toolRegistry = toolRegistry;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<McpChatClientResponse> ProcessChatAsync(
        McpChatClientRequest request,
        CancellationToken ct = default)
    {
        var model = !string.IsNullOrWhiteSpace(request.Model)
            ? request.Model
            : _configuration["Ollama:Model"] ?? "qwen3.5:27b";

        _logger.LogInformation("Processing chat request with model: {Model}, OrganizationId: {OrgId}",
            model, request.OrganizationId);

        // 1. Build System Prompt with Context
        var systemPrompt = OllamaPromptBuilder.BuildSystemPrompt(
            request.OrganizationId,
            request.CustomerId);

        // Prepare the conversation supplied to the model. Tool selection is performed by
        // the model from the advertised schemas, never by matching user-message keywords.
        var messages = new List<OllamaChatMessage>
        {
            new("system", systemPrompt)
        };

        if (request.History != null)
        {
            foreach (var item in request.History)
            {
                messages.Add(new(item.Role, item.Content));
            }
        }

        messages.Add(new("user", request.Message));

        // Prepare MCP tools as Ollama function definitions.
        var mcpTools = McpToolRegistry.GetTools();
        var ollamaTools = mcpTools.Select(t => new OllamaTool(
            Type: "function",
            Function: new OllamaFunctionDefinition(
                Name: t.Name,
                Description: t.Description,
                Parameters: t.InputSchema
            )
        )).ToList();

        // Common availability questions do not need an LLM round-trip. This
        // prevents a simple "yarın için müsait saatler" request from spending
        // CPU on tool selection and response generation.
        if (TryBuildAvailabilityRequest(request.Message, out var fastToolName, out var fastArgs))
        {
            var enrichedArgs = EnrichArgumentsWithContext(fastArgs, request);
            var toolResult = await _toolRegistry.ExecuteToolAsync(fastToolName, enrichedArgs, ct);
            if (TryFormatReadOnlyReply(fastToolName, toolResult, out var directReply))
            {
                return new McpChatClientResponse(
                    Reply: directReply,
                    ToolExecuted: true,
                    ToolName: fastToolName,
                    ToolArguments: enrichedArgs,
                    ToolResult: toolResult,
                    ModelUsed: model,
                    UpdatedHistory: BuildUpdatedHistory(request.History, request.Message, directReply));
            }
        }

        var executedAnyTool = false;
        string? lastToolName = null;
        JsonElement? lastToolArguments = null;
        McpToolResult? lastToolResult = null;
        string responseModel = model;

        // A tool result must include its name so Ollama can associate it with the
        // preceding call. Keep chaining bounded: simple availability queries need
        // one tool call and one final response, not an unbounded reasoning loop.
        const int maxToolRounds = 2;
        for (var round = 0; round < maxToolRounds; round++)
        {
            var chatRequest = new OllamaChatRequest(
                Model: model,
                Messages: messages,
                Tools: ollamaTools,
                Stream: false,
                Options: new OllamaOptions(
                    Temperature: 0.1f,
                    NumPredict: _configuration.GetValue<int?>("Ollama:MaxOutputTokens") ?? 256,
                    Think: _configuration.GetValue<bool?>("Ollama:Think") ?? false));
            var response = await _ollamaClient.ChatAsync(chatRequest, ct);
            responseModel = response.Model;
            var assistantMsg = response.Message;
            var toolCalls = assistantMsg.ToolCalls;

            if (toolCalls is null || toolCalls.Count == 0)
            {
                var reply = !string.IsNullOrWhiteSpace(assistantMsg.Content)
                    ? assistantMsg.Content
                    : "Talebinizi aldım. Size nasıl yardımcı olabilirim?";
                return new McpChatClientResponse(
                    Reply: reply,
                    ToolExecuted: executedAnyTool,
                    ToolName: lastToolName,
                    ToolArguments: lastToolArguments,
                    ToolResult: lastToolResult,
                    ModelUsed: responseModel,
                    UpdatedHistory: BuildUpdatedHistory(request.History, request.Message, reply));
            }

            messages.Add(new OllamaChatMessage("assistant", assistantMsg.Content, toolCalls));
            if (toolCalls.Count > 1)
            {
                _logger.LogWarning(
                    "Ollama returned {ToolCount} tool calls in one response; executing only the first to bound work.",
                    toolCalls.Count);
            }

            foreach (var toolCall in toolCalls.Take(1))
            {
                var toolName = toolCall.Function.Name;
                var enrichedArgs = EnrichArgumentsWithContext(toolCall.Function.Arguments, request);
                lastToolName = toolName;
                lastToolArguments = enrichedArgs;

                _logger.LogInformation("Ollama selected MCP tool '{ToolName}' with args: {Args}",
                    toolName, enrichedArgs);

                if (McpToolRegistry.RequiresConfirmation(toolName) && request.RequireConfirmationForWrites)
                {
                    var proposal = string.IsNullOrWhiteSpace(assistantMsg.Content)
                        ? $"Bu işlemi gerçekleştirmemi onaylıyor musunuz? ({toolName})"
                        : assistantMsg.Content;
                    return new McpChatClientResponse(
                        Reply: proposal,
                        ToolExecuted: false,
                        ToolName: toolName,
                        ToolArguments: enrichedArgs,
                        ToolResult: null,
                        ModelUsed: responseModel,
                        UpdatedHistory: BuildUpdatedHistory(request.History, request.Message, proposal));
                }

                if (!request.AutoExecuteTools)
                {
                    var proposal = $"'{toolName}' aracı çağrılmak üzere seçildi.";
                    return new McpChatClientResponse(
                        Reply: proposal,
                        ToolExecuted: false,
                        ToolName: toolName,
                        ToolArguments: enrichedArgs,
                        ToolResult: null,
                        ModelUsed: responseModel,
                        UpdatedHistory: BuildUpdatedHistory(request.History, request.Message, proposal));
                }

                var toolResult = await _toolRegistry.ExecuteToolAsync(toolName, enrichedArgs, ct);
                executedAnyTool = true;
                lastToolResult = toolResult;

                // Availability results are already structured for the client. Do not
                // spend a second LLM generation merely to turn a list of times into
                // Turkish text.
                if (TryFormatReadOnlyReply(toolName, toolResult, out var directReply))
                {
                    return new McpChatClientResponse(
                        Reply: directReply,
                        ToolExecuted: true,
                        ToolName: lastToolName,
                        ToolArguments: lastToolArguments,
                        ToolResult: lastToolResult,
                        ModelUsed: responseModel,
                        UpdatedHistory: BuildUpdatedHistory(request.History, request.Message, directReply));
                }

                messages.Add(new OllamaChatMessage(
                    "tool",
                    toolResult.Content.FirstOrDefault()?.Text ?? string.Empty,
                    ToolName: toolName));
            }
        }

        const string exhaustedReply = "İsteğinizi işlerken araç çağrısı sınırına ulaşıldı. Lütfen isteğinizi daraltarak tekrar deneyin.";
        return new McpChatClientResponse(
            Reply: exhaustedReply,
            ToolExecuted: executedAnyTool,
            ToolName: lastToolName,
            ToolArguments: lastToolArguments,
            ToolResult: lastToolResult,
            ModelUsed: responseModel,
            UpdatedHistory: BuildUpdatedHistory(request.History, request.Message, exhaustedReply));
    }

    private static bool TryFormatReadOnlyReply(
        string toolName,
        McpToolResult toolResult,
        out string reply)
    {
        reply = string.Empty;
        if (toolResult.IsError || toolResult.Content.Count == 0)
            return false;

        var text = toolResult.Content[0].Text;
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (toolName == "get_available_slots" &&
                root.TryGetProperty("Slots", out var slots) &&
                slots.ValueKind == JsonValueKind.Array)
            {
                var displays = slots.EnumerateArray()
                    .Select(slot => slot.TryGetProperty("LocalDisplay", out var display)
                        ? display.GetString()
                        : null)
                    .Where(display => !string.IsNullOrWhiteSpace(display))
                    .ToList();

                reply = displays.Count == 0
                    ? "İstenen tarih aralığında müsait randevu saati bulunamadı."
                    : "Müsait saatler:\n" + string.Join('\n', displays.Select(display => $"• {display}"));
                return true;
            }

            if (toolName == "get_available_dates" &&
                root.TryGetProperty("AvailableDates", out var dates) &&
                dates.ValueKind == JsonValueKind.Array)
            {
                var values = dates.EnumerateArray()
                    .Select(date => date.GetString())
                    .Where(date => !string.IsNullOrWhiteSpace(date))
                    .ToList();

                reply = values.Count == 0
                    ? "İstenen tarih aralığında müsait gün bulunamadı."
                    : "Müsait günler:\n" + string.Join('\n', values.Select(date => $"• {date}"));
                return true;
            }
        }
        catch (JsonException)
        {
            // Let the normal model-based response path handle non-JSON tool output.
        }

        return false;
    }

    private static bool TryBuildAvailabilityRequest(
        string message,
        out string toolName,
        out JsonElement arguments)
    {
        toolName = string.Empty;
        arguments = default;

        var normalized = message.Trim().ToLowerInvariant();
        var asksAvailability = normalized.Contains("müsait") ||
                               normalized.Contains("uygun") ||
                               normalized.Contains("boş");
        if (!asksAvailability)
            return false;

        var hasExplicitDate = normalized.Contains("bugün") ||
                              normalized.Contains("yarın") ||
                              normalized.Contains("öbür gün");
        if (!hasExplicitDate)
            return false;

        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        if (normalized.Contains("yarın"))
            date = date.AddDays(1);
        else if (normalized.Contains("öbür gün"))
            date = date.AddDays(2);

        toolName = normalized.Contains("saat") || normalized.Contains("slot")
            ? "get_available_slots"
            : "get_available_dates";

        arguments = JsonSerializer.SerializeToElement(new
        {
            start_date = date.ToString("yyyy-MM-dd"),
            end_date = date.ToString("yyyy-MM-dd")
        });
        return true;
    }

    private JsonElement EnrichArgumentsWithContext(JsonElement rawArgs, McpChatClientRequest request)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (rawArgs.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in rawArgs.EnumerateObject())
            {
                // Normalize snake_case or camelCase key
                var normalizedKey = ToSnakeCase(prop.Name);

                if (prop.Value.ValueKind == JsonValueKind.String)
                    dict[normalizedKey] = prop.Value.GetString();
                else if (prop.Value.ValueKind == JsonValueKind.Number)
                    dict[normalizedKey] = prop.Value.GetInt64();
                else if (prop.Value.ValueKind == JsonValueKind.True)
                    dict[normalizedKey] = true;
                else if (prop.Value.ValueKind == JsonValueKind.False)
                    dict[normalizedKey] = false;
                else
                    dict[normalizedKey] = prop.Value.GetRawText();
            }
        }

        // Inject organization_id if context provides it and it is not already in args
        if (request.OrganizationId.HasValue &&
            (!dict.ContainsKey("organization_id") || string.IsNullOrWhiteSpace(dict["organization_id"]?.ToString())))
        {
            dict["organization_id"] = request.OrganizationId.Value.ToString();
        }

        // Inject customer_id if context provides it and it is not already in args
        if (request.CustomerId.HasValue &&
            (!dict.ContainsKey("customer_id") || string.IsNullOrWhiteSpace(dict["customer_id"]?.ToString())))
        {
            dict["customer_id"] = request.CustomerId.Value.ToString();
        }

        var json = JsonSerializer.Serialize(dict);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static List<McpChatMessageHistoryItem> BuildUpdatedHistory(
        List<McpChatMessageHistoryItem>? existingHistory,
        string userMessage,
        string assistantReply)
    {
        var updated = existingHistory != null ? new List<McpChatMessageHistoryItem>(existingHistory) : new();
        updated.Add(new("user", userMessage));
        updated.Add(new("assistant", assistantReply));
        return updated;
    }

    private static string ToSnakeCase(string str)
    {
        if (string.IsNullOrEmpty(str)) return str;
        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < str.Length; i++)
        {
            char c = str[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && str[i - 1] != '_')
                    builder.Append('_');
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }
}
