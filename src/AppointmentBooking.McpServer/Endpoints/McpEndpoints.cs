using System.Text.Json;
using AppointmentBooking.McpServer.Protocol;
using AppointmentBooking.McpServer.Services;
using AppointmentBooking.McpServer.Services.Ollama;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AppointmentBooking.McpServer.Endpoints;

public static class McpEndpoints
{
    public static void MapMcpEndpoints(this IEndpointRouteBuilder app)
    {
        // 1. JSON-RPC 2.0 Single Entry Point (/mcp)
        app.MapPost("/mcp", async (
            HttpContext context,
            McpToolRegistry toolRegistry,
            IMcpFunctionCallingOrchestrator orchestrator,
            CancellationToken ct) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync(ct);

            if (string.IsNullOrWhiteSpace(body))
                return Results.BadRequest(JsonRpcResponse.CreateError(null, -32700, "Parse error: Empty request body"));

            JsonRpcRequest? rpcRequest;
            try
            {
                rpcRequest = JsonSerializer.Deserialize<JsonRpcRequest>(body);
                if (rpcRequest == null)
                    return Results.BadRequest(JsonRpcResponse.CreateError(null, -32600, "Invalid Request"));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(JsonRpcResponse.CreateError(null, -32700, $"Parse error: {ex.Message}"));
            }

            var response = await HandleJsonRpcRequestAsync(rpcRequest, toolRegistry, orchestrator, ct);
            return Results.Ok(response);
        })
        .WithName("McpJsonRpc")
        .WithTags("MCP")
        .Produces<JsonRpcResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // 2. Ollama AI Chat & Function Calling Endpoint (/mcp/chat)
        app.MapPost("/mcp/chat", async (
            McpChatClientRequest request,
            IMcpFunctionCallingOrchestrator orchestrator,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
                return Results.BadRequest(new { error = "Message cannot be empty." });

            var response = await orchestrator.ProcessChatAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("McpChat")
        .WithTags("MCP", "Ollama")
        .Produces<McpChatClientResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // 3. Server-Sent Events (SSE) Streamable Transport (/mcp/sse)
        app.MapGet("/mcp/sse", async (HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.Append("Content-Type", "text/event-stream");
            context.Response.Headers.Append("Cache-Control", "no-cache");
            context.Response.Headers.Append("Connection", "keep-alive");

            var sessionId = Guid.NewGuid().ToString("N");
            var endpointUri = $"/mcp/message?sessionId={sessionId}";

            // Send initial endpoint event
            await context.Response.WriteAsync($"event: endpoint\ndata: {endpointUri}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);

            // Keep connection alive with periodic heartbeats until disconnected
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(15000, ct);
                await context.Response.WriteAsync("event: ping\ndata: {}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
            }
        })
        .WithName("McpSseTransport")
        .WithTags("MCP");

        // 4. Health check
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "Healthy",
            service = "AppointmentBooking.McpServer",
            version = "1.0.0",
            protocol = "MCP-2024-11-05",
            ollama = "Integrated",
            timestamp = DateTimeOffset.UtcNow
        }))
        .WithName("McpHealth")
        .WithTags("Health");
    }

    private static async Task<JsonRpcResponse> HandleJsonRpcRequestAsync(
        JsonRpcRequest request,
        McpToolRegistry toolRegistry,
        IMcpFunctionCallingOrchestrator orchestrator,
        CancellationToken ct)
    {
        return request.Method switch
        {
            "initialize" => JsonRpcResponse.Success(request.Id, new
            {
                protocolVersion = "2024-11-05",
                capabilities = new
                {
                    tools = new { listChanged = true },
                    resources = new { subscribe = false, listChanged = false },
                    chat = new { enabled = true }
                },
                serverInfo = new
                {
                    name = "appointment-booking-mcp-server",
                    version = "1.0.0"
                }
            }),

            "ping" => JsonRpcResponse.Success(request.Id, new { }),

            "tools/list" => JsonRpcResponse.Success(request.Id, new
            {
                tools = McpToolRegistry.GetTools()
            }),

            "tools/call" => await ExecuteToolCallAsync(request, toolRegistry, ct),

            "chat" or "mcp/chat" => await ExecuteChatAsync(request, orchestrator, ct),

            "resources/list" => JsonRpcResponse.Success(request.Id, new
            {
                resources = new List<McpResource>
                {
                    new("appointment://system/organizations", "Organizations", "List of registered appointment organizations", "application/json"),
                    new("appointment://system/policies", "Booking Policies", "Rules and cancellation policies for the appointment system", "text/plain")
                }
            }),

            "resources/read" => JsonRpcResponse.Success(request.Id, new McpResourceReadResult(new List<McpResourceContent>
            {
                new("appointment://system/policies", "text/plain", "Appointments can be booked up to 30 days in advance. Cancellations within 2 hours of slot time may require customer service.")
            })),

            _ => JsonRpcResponse.CreateError(request.Id, -32601, $"Method not found: '{request.Method}'")
        };
    }

    private static async Task<JsonRpcResponse> ExecuteToolCallAsync(
        JsonRpcRequest request,
        McpToolRegistry toolRegistry,
        CancellationToken ct)
    {
        if (request.Params == null || !request.Params.Value.TryGetProperty("name", out var nameProp))
        {
            return JsonRpcResponse.CreateError(request.Id, -32602, "Invalid params: 'name' property is required for tools/call");
        }

        var toolName = nameProp.GetString();
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return JsonRpcResponse.CreateError(request.Id, -32602, "Tool name cannot be empty");
        }

        JsonElement? arguments = null;
        if (request.Params.Value.TryGetProperty("arguments", out var argsProp))
        {
            arguments = argsProp;
        }

        var result = await toolRegistry.ExecuteToolAsync(toolName, arguments, ct);
        return JsonRpcResponse.Success(request.Id, result);
    }

    private static async Task<JsonRpcResponse> ExecuteChatAsync(
        JsonRpcRequest request,
        IMcpFunctionCallingOrchestrator orchestrator,
        CancellationToken ct)
    {
        if (request.Params == null)
        {
            return JsonRpcResponse.CreateError(request.Id, -32602, "Invalid params: object with 'message' property is required for chat");
        }

        try
        {
            var chatReq = JsonSerializer.Deserialize<McpChatClientRequest>(request.Params.Value.GetRawText(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (chatReq == null || string.IsNullOrWhiteSpace(chatReq.Message))
            {
                return JsonRpcResponse.CreateError(request.Id, -32602, "'message' cannot be empty");
            }

            var response = await orchestrator.ProcessChatAsync(chatReq, ct);
            return JsonRpcResponse.Success(request.Id, response);
        }
        catch (Exception ex)
        {
            return JsonRpcResponse.CreateError(request.Id, -32603, $"Internal chat error: {ex.Message}");
        }
    }
}
