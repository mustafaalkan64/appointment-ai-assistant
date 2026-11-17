using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace AppointmentBooking.ChatApi.Services;

public interface IMcpClient
{
    Task<McpCallResult> CallToolAsync(string toolName, object arguments, CancellationToken ct = default);
    Task<McpChatResult> ChatAsync(
        string message,
        Guid organizationId,
        Guid customerId,
        IReadOnlyList<McpChatHistoryItem> history,
        CancellationToken ct = default);
}

public sealed record McpCallResult(bool Success, string Content, string? ErrorMessage);
public sealed record McpChatHistoryItem(string Role, string Content);
public sealed record McpChatResult(
    bool Success,
    string Reply,
    bool ToolExecuted,
    string? ToolName,
    JsonElement? ToolArguments,
    string? ToolResult,
    string? ErrorMessage);

public sealed class HttpMcpClient : IMcpClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpMcpClient> _logger;

    public HttpMcpClient(HttpClient httpClient, ILogger<HttpMcpClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<McpCallResult> CallToolAsync(string toolName, object arguments, CancellationToken ct = default)
    {
        try
        {
            var rpcRequest = new
            {
                jsonrpc = "2.0",
                method = "tools/call",
                @params = new
                {
                    name = toolName,
                    arguments
                },
                id = Guid.NewGuid().ToString("N")[..8]
            };

            var body = JsonSerializer.SerializeToNode(rpcRequest)!.AsObject();
            body["params"]!["_meta"] = new JsonObject
            {
                ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                ["io.modelcontextprotocol/clientInfo"] = new JsonObject
                {
                    ["name"] = "AppointmentBooking.ChatApi",
                    ["version"] = "1.0.0"
                },
                ["io.modelcontextprotocol/clientCapabilities"] = new JsonObject()
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/mcp")
            {
                Content = JsonContent.Create(body)
            };
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            httpRequest.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2026-07-28");
            httpRequest.Headers.TryAddWithoutValidation("Mcp-Method", "tools/call");
            httpRequest.Headers.TryAddWithoutValidation("Mcp-Name", toolName);

            var response = await _httpClient.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("MCP call failed with HTTP {StatusCode}: {Error}", response.StatusCode, errorText);
                return new McpCallResult(false, string.Empty, $"MCP HTTP error: {response.StatusCode}");
            }

            var responseText = await response.Content.ReadAsStringAsync(ct);
            if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            {
                responseText = string.Join("\n", responseText.Split('\n')
                    .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
                    .Select(line => line["data:".Length..].Trim()));
            }

            using var doc = JsonDocument.Parse(responseText);
            if (doc == null)
                return new McpCallResult(false, string.Empty, "Empty response from MCP Server");

            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var errorProp) && errorProp.ValueKind != JsonValueKind.Null)
            {
                var msg = errorProp.GetProperty("message").GetString() ?? "Unknown MCP error";
                return new McpCallResult(false, string.Empty, msg);
            }

            if (root.TryGetProperty("result", out var resultProp))
            {
                if (resultProp.TryGetProperty("content", out var contentArray) && contentArray.GetArrayLength() > 0)
                {
                    var text = contentArray[0].GetProperty("text").GetString() ?? string.Empty;
                    var isError = resultProp.TryGetProperty("isError", out var err) && err.GetBoolean();
                    return new McpCallResult(!isError, text, isError ? text : null);
                }
            }

            return new McpCallResult(true, root.ToString(), null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call MCP tool {ToolName}.", toolName);
            return new McpCallResult(false, string.Empty, ex.Message);
        }
    }

    public async Task<McpChatResult> ChatAsync(
        string message,
        Guid organizationId,
        Guid customerId,
        IReadOnlyList<McpChatHistoryItem> history,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/mcp/chat", new
            {
                message,
                organizationId,
                customerId,
                history,
                autoExecuteTools = true,
                requireConfirmationForWrites = true
            }, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(ct);
                return new McpChatResult(false, string.Empty, false, null, null, null,
                    $"MCP chat HTTP error: {response.StatusCode}: {errorText}");
            }

            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            if (document is null)
                return new McpChatResult(false, string.Empty, false, null, null, null, "Empty MCP chat response.");

            var root = document.RootElement;
            var args = root.TryGetProperty("toolArguments", out var argsElement) &&
                       argsElement.ValueKind == JsonValueKind.Object
                ? argsElement.Clone()
                : (JsonElement?)null;
            var toolResult = root.TryGetProperty("toolResult", out var resultElement) &&
                             resultElement.ValueKind == JsonValueKind.Object &&
                             resultElement.TryGetProperty("content", out var content) &&
                             content.ValueKind == JsonValueKind.Array &&
                             content.GetArrayLength() > 0 &&
                             content[0].TryGetProperty("text", out var textElement)
                ? textElement.GetString()
                : null;

            return new McpChatResult(
                true,
                root.TryGetProperty("reply", out var reply) ? reply.GetString() ?? string.Empty : string.Empty,
                root.TryGetProperty("toolExecuted", out var executed) && executed.GetBoolean(),
                root.TryGetProperty("toolName", out var name) ? name.GetString() : null,
                args,
                toolResult,
                null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process chat through MCP LLM tool calling.");
            return new McpChatResult(false, string.Empty, false, null, null, null, ex.Message);
        }
    }
}
