using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppointmentBooking.McpServer.Protocol;

public sealed record JsonRpcRequest(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params,
    [property: JsonPropertyName("id")] object? Id);

public sealed record JsonRpcResponse(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] object? Id,
    [property: JsonPropertyName("result")] object? Result = null,
    [property: JsonPropertyName("error")] JsonRpcError? Error = null)
{
    public static JsonRpcResponse Success(object? id, object? result) =>
        new("2.0", id, Result: result);

    public static JsonRpcResponse CreateError(object? id, int code, string message, object? data = null) =>
        new("2.0", id, Error: new JsonRpcError(code, message, data));
}

public sealed record JsonRpcError(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("data")] object? Data = null);

public sealed record McpToolDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("inputSchema")] McpToolInputSchema InputSchema);

public sealed record McpToolInputSchema(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("properties")] Dictionary<string, McpToolPropertyDefinition> Properties,
    [property: JsonPropertyName("required")] List<string> Required);

public sealed record McpToolPropertyDefinition(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("enum")] List<string>? Enum = null);

public sealed record McpToolResult(
    [property: JsonPropertyName("content")] List<McpContentItem> Content,
    [property: JsonPropertyName("isError")] bool IsError = false);

public sealed record McpContentItem(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text);

public sealed record McpResource(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("mimeType")] string MimeType);

public sealed record McpResourceReadResult(
    [property: JsonPropertyName("contents")] List<McpResourceContent> Contents);

public sealed record McpResourceContent(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("mimeType")] string MimeType,
    [property: JsonPropertyName("text")] string Text);
