using AppointmentBooking.Application.Abstractions.Persistence;
using AppointmentBooking.Application.Abstractions.Tenancy;
using AppointmentBooking.ChatApi.Services;
using AppointmentBooking.Domain.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace AppointmentBooking.ChatApi.Endpoints;

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        // 1. Send Message to Agent
        app.MapPost("/api/chat/messages", async (
            SendMessageRequest request,
            IAiAgentOrchestrator orchestrator,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
                return Results.BadRequest(new { error = "Message cannot be empty." });

            var response = await orchestrator.ProcessUserMessageAsync(
                request.OrganizationId,
                request.CustomerId,
                request.ConversationId,
                request.Message,
                ct);

            return Results.Ok(response);
        })
        .WithName("SendMessage")
        .WithTags("Chat")
        .Produces<AgentResponseDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // 2. Start Conversation
        app.MapPost("/api/chat/conversations", async (
            StartConversationRequest request,
            IApplicationDbContext context,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            tenantContext.SetTenant(request.OrganizationId);

            var conversation = Conversation.Start(request.OrganizationId, request.CustomerId);
            var welcomeText = "Merhaba! Randevu almak veya mevcut randevularınızı yönetmek için size nasıl yardımcı olabilirim?";
            conversation.AddMessage(Domain.Enums.MessageRole.Assistant, welcomeText, "Welcome");

            context.Conversations.Add(conversation);
            await context.SaveChangesAsync(ct);

            return Results.Created($"/api/chat/conversations/{conversation.Id}", new
            {
                ConversationId = conversation.Id,
                OrganizationId = conversation.OrganizationId,
                CustomerId = conversation.CustomerId,
                Status = conversation.Status.ToString(),
                Message = welcomeText
            });
        })
        .WithName("StartConversation")
        .WithTags("Chat")
        .Produces(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // 3. Get Conversation History
        app.MapGet("/api/chat/conversations/{conversationId:guid}", async (
            Guid conversationId,
            IApplicationDbContext context,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            var conversation = await context.Conversations
                .IgnoreQueryFilters()
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

            if (conversation == null)
                return Results.NotFound(new { error = "Conversation not found." });

            return Results.Ok(new
            {
                conversation.Id,
                conversation.OrganizationId,
                conversation.CustomerId,
                Status = conversation.Status.ToString(),
                conversation.CreatedAt,
                Messages = conversation.Messages.OrderBy(m => m.CreatedAt).Select(m => new
                {
                    m.Id,
                    Role = m.Role.ToString().ToLowerInvariant(),
                    m.Content,
                    m.Intent,
                    m.CreatedAt
                })
            });
        })
        .WithName("GetConversation")
        .WithTags("Chat")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // 4. Health Check
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "Healthy",
            service = "AppointmentBooking.ChatApi",
            version = "1.0.0",
            timestamp = DateTimeOffset.UtcNow
        }))
        .WithName("ChatApiHealth")
        .WithTags("Health");
    }
}

public sealed record SendMessageRequest(
    Guid OrganizationId,
    Guid CustomerId,
    string Message,
    Guid? ConversationId = null);

public sealed record StartConversationRequest(
    Guid OrganizationId,
    Guid CustomerId);
