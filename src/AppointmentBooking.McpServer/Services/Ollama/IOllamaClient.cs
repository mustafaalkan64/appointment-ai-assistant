namespace AppointmentBooking.McpServer.Services.Ollama;

public interface IOllamaClient
{
    Task<OllamaChatResponse> ChatAsync(
        OllamaChatRequest request,
        CancellationToken ct = default);
}
