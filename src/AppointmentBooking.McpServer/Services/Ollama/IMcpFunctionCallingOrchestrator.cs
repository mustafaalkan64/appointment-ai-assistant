namespace AppointmentBooking.McpServer.Services.Ollama;

public interface IMcpFunctionCallingOrchestrator
{
    Task<McpChatClientResponse> ProcessChatAsync(
        McpChatClientRequest request,
        CancellationToken ct = default);
}
