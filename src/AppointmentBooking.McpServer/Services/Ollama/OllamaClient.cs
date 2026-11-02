using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AppointmentBooking.McpServer.Services.Ollama;

public sealed class OllamaClient : IOllamaClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OllamaClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public OllamaClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OllamaClient> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<OllamaChatResponse> ChatAsync(
        OllamaChatRequest request,
        CancellationToken ct = default)
    {
        var primaryModel = request.Model;
        var fallbackModel = _configuration["Ollama:FallbackModel"];

        try
        {
            return await ExecuteChatRequestAsync(request, ct);
        }
        catch (Exception ex) when (!string.IsNullOrWhiteSpace(fallbackModel) &&
                                  !string.Equals(primaryModel, fallbackModel, StringComparison.OrdinalIgnoreCase) &&
                                  (ex is TaskCanceledException or HttpRequestException or TimeoutException))
        {
            _logger.LogWarning(ex,
                "Ollama chat with model '{Model}' timed out or failed. Retrying with fallback model '{FallbackModel}'...",
                primaryModel, fallbackModel);

            var fallbackRequest = request with { Model = fallbackModel };
            return await ExecuteChatRequestAsync(fallbackRequest, ct);
        }
    }

    private async Task<OllamaChatResponse> ExecuteChatRequestAsync(
        OllamaChatRequest request,
        CancellationToken ct)
    {
        _logger.LogInformation("Sending chat request to Ollama with model '{Model}'. Messages: {Count}, Tools: {ToolCount}",
            request.Model, request.Messages.Count, request.Tools?.Count ?? 0);

        var response = await _httpClient.PostAsJsonAsync("/api/chat", request, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Ollama returned HTTP {StatusCode}: {ErrorBody}", response.StatusCode, errorBody);
            throw new HttpRequestException($"Ollama API returned HTTP {response.StatusCode}: {errorBody}");
        }

        var chatResponse = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(JsonOptions, ct);
        if (chatResponse == null)
        {
            throw new InvalidOperationException("Failed to deserialize Ollama response.");
        }

        // Post-process response to ensure tool calls are captured if returned as raw JSON in content
        chatResponse = NormalizeToolCalls(chatResponse);

        return chatResponse;
    }

    private OllamaChatResponse NormalizeToolCalls(OllamaChatResponse response)
    {
        var message = response.Message;
        if (message.ToolCalls != null && message.ToolCalls.Count > 0)
        {
            return response;
        }

        // Check if content itself is a JSON tool call
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            var content = message.Content.Trim();

            // Extract from markdown code fence if present ```json ... ```
            var match = Regex.Match(content, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.Singleline);
            var jsonCandidate = match.Success ? match.Groups[1].Value : content;

            if (jsonCandidate.StartsWith('{') && jsonCandidate.EndsWith('}'))
            {
                try
                {
                    using var doc = JsonDocument.Parse(jsonCandidate);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("name", out var nameProp) &&
                        root.TryGetProperty("arguments", out var argsProp))
                    {
                        var toolName = nameProp.GetString();
                        if (!string.IsNullOrWhiteSpace(toolName))
                        {
                            var synthesizedToolCall = new OllamaToolCall(
                                Id: $"call_{Guid.NewGuid():N}"[..12],
                                Function: new OllamaFunctionCall(toolName, argsProp.Clone())
                            );

                            var updatedMessage = message with
                            {
                                ToolCalls = new List<OllamaToolCall> { synthesizedToolCall }
                            };

                            _logger.LogInformation("Extracted tool call '{ToolName}' from message content JSON.", toolName);
                            return response with { Message = updatedMessage };
                        }
                    }
                }
                catch (JsonException)
                {
                    // Not valid JSON tool call; treat as normal text content
                }
            }
        }

        return response;
    }
}
