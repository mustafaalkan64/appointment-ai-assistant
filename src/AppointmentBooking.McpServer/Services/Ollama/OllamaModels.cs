using System.Text.Json;
using System.Text.Json.Serialization;
using AppointmentBooking.McpServer.Protocol;

namespace AppointmentBooking.McpServer.Services.Ollama;

public sealed record OllamaChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] List<OllamaChatMessage> Messages,
    [property: JsonPropertyName("tools")] List<OllamaTool>? Tools = null,
    [property: JsonPropertyName("stream")] bool Stream = false,
    [property: JsonPropertyName("options")] OllamaOptions? Options = null);

public sealed record OllamaOptions(
    [property: JsonPropertyName("temperature")] float? Temperature = 0.1f,
    [property: JsonPropertyName("num_predict")] int? NumPredict = null);

public sealed record OllamaChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("tool_calls")] List<OllamaToolCall>? ToolCalls = null);

public sealed record OllamaTool(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] OllamaFunctionDefinition Function);

public sealed record OllamaFunctionDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("parameters")] McpToolInputSchema Parameters);

public sealed record OllamaChatResponse(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("message")] OllamaResponseMessage Message,
    [property: JsonPropertyName("done")] bool Done,
    [property: JsonPropertyName("done_reason")] string? DoneReason = null);

public sealed record OllamaResponseMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("tool_calls")] List<OllamaToolCall>? ToolCalls = null);

public sealed record OllamaToolCall(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("function")] OllamaFunctionCall Function);

public sealed record OllamaFunctionCall(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] JsonElement Arguments);

// Client DTOs for MCP Chat endpoint
public sealed record McpChatClientRequest(
    string Message,
    Guid? OrganizationId = null,
    Guid? CustomerId = null,
    string? Model = null,
    List<McpChatMessageHistoryItem>? History = null,
    bool AutoExecuteTools = true);

public sealed record McpChatMessageHistoryItem(
    string Role,
    string Content);

public sealed record McpChatClientResponse(
    string Reply,
    bool ToolExecuted,
    string? ToolName,
    JsonElement? ToolArguments,
    McpToolResult? ToolResult,
    string ModelUsed,
    List<McpChatMessageHistoryItem> UpdatedHistory);
