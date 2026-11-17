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

                // Never surface raw JSON/technical field descriptions to the user.
                // For structured read tools, replace such replies with a short Turkish
                // summary generated from the actual tool result.
                if (executedAnyTool &&
                    lastToolName is not null &&
                    lastToolResult is not null &&
                    McpToolResultSummarizer.LooksLikeTechnicalJsonExplanation(reply) &&
                    McpToolResultSummarizer.TrySummarize(lastToolName, lastToolResult, out var summary))
                {
                    reply = summary;
                }

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

                // Feed the tool result back to the model so it can decide whether to
                // answer directly or continue with another tool (for example, after
                // get_available_slots the model may select create_appointment).
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
