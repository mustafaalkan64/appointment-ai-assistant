using AppointmentBooking.McpServer.Services.Ollama;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AppointmentBooking.McpServer.Endpoints;

public static class McpEndpoints
{
    public static void MapMcpEndpoints(this IEndpointRouteBuilder app)
    {
        // Product-specific chat convenience endpoint; MCP protocol transport is mapped by the official SDK.
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

        app.MapGet("/health", () => Results.Ok(new
        {
            status = "Healthy",
            service = "AppointmentBooking.McpServer",
            version = "1.0.0",
            protocol = "MCP-2026-07-28",
            ollama = "Integrated",
            timestamp = DateTimeOffset.UtcNow
        }))
        .WithName("McpHealth")
        .WithTags("Health");
    }
}
