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

        // 2. Prepare Messages
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

        // 3. Prepare MCP Tools as Ollama Tool Definitions
        var mcpTools = McpToolRegistry.GetTools();
        var ollamaTools = mcpTools.Select(t => new OllamaTool(
            Type: "function",
            Function: new OllamaFunctionDefinition(
                Name: t.Name,
                Description: t.Description,
                Parameters: t.InputSchema
            )
        )).ToList();

        // 4. Send Request to Ollama
        var chatRequest = new OllamaChatRequest(
            Model: model,
            Messages: messages,
            Tools: ollamaTools,
            Stream: false,
            Options: new OllamaOptions(Temperature: 0.1f)
        );

        var response = await _ollamaClient.ChatAsync(chatRequest, ct);
        var assistantMsg = response.Message;

        // 5. Check if Ollama Decided to Call a Tool
        if (assistantMsg.ToolCalls != null && assistantMsg.ToolCalls.Count > 0)
        {
            var toolCall = assistantMsg.ToolCalls[0];
            var toolName = toolCall.Function.Name;
            var rawArgs = toolCall.Function.Arguments;

            _logger.LogInformation("Ollama routed to function: '{ToolName}' with args: {Args}",
                toolName, rawArgs.ToString());

            // Enrich Arguments with Context (e.g. inject OrganizationId/CustomerId if missing)
            var enrichedArgs = EnrichArgumentsWithContext(rawArgs, request);

            if (!request.AutoExecuteTools)
            {
                // Return tool call proposal directly to client without executing
                var historyWithoutExec = BuildUpdatedHistory(request.History, request.Message,
                    $"Fonksiyon çağrısı önerildi: {toolName}");

                return new McpChatClientResponse(
                    Reply: $"'{toolName}' fonksiyonunun çağrılması planlandı.",
                    ToolExecuted: false,
                    ToolName: toolName,
                    ToolArguments: enrichedArgs,
                    ToolResult: null,
                    ModelUsed: response.Model,
                    UpdatedHistory: historyWithoutExec);
            }

            // Execute the Tool via McpToolRegistry
            var toolResult = await _toolRegistry.ExecuteToolAsync(toolName, enrichedArgs, ct);

            // Synthesize Final User-Friendly Reply using the Tool Result
            var finalReply = await SynthesizeFinalReplyAsync(
                messages,
                toolName,
                enrichedArgs,
                toolResult,
                response.Model,
                ct);

            var updatedHistory = BuildUpdatedHistory(request.History, request.Message, finalReply);

            return new McpChatClientResponse(
                Reply: finalReply,
                ToolExecuted: true,
                ToolName: toolName,
                ToolArguments: enrichedArgs,
                ToolResult: toolResult,
                ModelUsed: response.Model,
                UpdatedHistory: updatedHistory);
        }

        // 6. Direct Text Response (No tool call needed)
        var replyText = !string.IsNullOrWhiteSpace(assistantMsg.Content)
            ? assistantMsg.Content
            : "Talebinizi aldım. Size nasıl yardımcı olabilirim?";

        var directHistory = BuildUpdatedHistory(request.History, request.Message, replyText);

        return new McpChatClientResponse(
            Reply: replyText,
            ToolExecuted: false,
            ToolName: null,
            ToolArguments: null,
            ToolResult: null,
            ModelUsed: response.Model,
            UpdatedHistory: directHistory);
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

    private async Task<string> SynthesizeFinalReplyAsync(
        List<OllamaChatMessage> conversationMessages,
        string toolName,
        JsonElement arguments,
        McpToolResult toolResult,
        string model,
        CancellationToken ct)
    {
        var toolOutputText = toolResult.Content.FirstOrDefault()?.Text ?? string.Empty;

        // If tool resulted in error, provide a clear friendly error message
        if (toolResult.IsError)
        {
            return $"İşlem gerçekleştirilemedi: {toolOutputText}. Lütfen parametreleri kontrol edip tekrar deneyiniz.";
        }

        try
        {
            // Ask Ollama to summarize the tool execution result for the user in natural Turkish
            var synthesisMessages = new List<OllamaChatMessage>(conversationMessages)
            {
                new("assistant", $"[Function Call: {toolName}({arguments})]")
            };

            var promptText = $"""
            Aşağıdaki '{toolName}' fonksiyon çağrısı başarıyla tamamlandı. 
            Fonksiyon Çıktısı (JSON):
            {toolOutputText}

            Talimat: Bu sonucu kullanıcıya son derece nazik, anlaşılır, şık ve Türkçe olarak özetle.
            Gereksiz teknik detayları veya JSON formatını kullanıcıya yansıtma.
            """;

            synthesisMessages.Add(new("user", promptText));

            var synthesisRequest = new OllamaChatRequest(
                Model: model,
                Messages: synthesisMessages,
                Tools: null,
                Stream: false,
                Options: new OllamaOptions(Temperature: 0.2f)
            );

            var response = await _ollamaClient.ChatAsync(synthesisRequest, ct);
            if (!string.IsNullOrWhiteSpace(response.Message.Content))
            {
                return response.Message.Content;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to synthesize final reply with LLM. Falling back to direct tool output.");
        }

        // Fallback if synthesis failed or returned empty
        return $"İşlem tamamlandı:\n{toolOutputText}";
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
