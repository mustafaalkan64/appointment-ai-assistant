using System.Net.Http.Json;
using System.Text.Json;

namespace AppointmentBooking.ChatApi.Services;

public interface IMcpClient
{
    Task<McpCallResult> CallToolAsync(string toolName, object arguments, CancellationToken ct = default);
}

public sealed record McpCallResult(bool Success, string Content, string? ErrorMessage);

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

            var response = await _httpClient.PostAsJsonAsync("/mcp", rpcRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("MCP call failed with HTTP {StatusCode}: {Error}", response.StatusCode, errorText);
                return new McpCallResult(false, string.Empty, $"MCP HTTP error: {response.StatusCode}");
            }

            var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
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
}
