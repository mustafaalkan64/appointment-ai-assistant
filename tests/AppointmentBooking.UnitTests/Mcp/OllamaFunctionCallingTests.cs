using System.Text.Json;
using AppointmentBooking.McpServer.Protocol;
using AppointmentBooking.McpServer.Services;
using AppointmentBooking.McpServer.Services.Ollama;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AppointmentBooking.UnitTests.Mcp;

public class OllamaFunctionCallingTests
{
    [Fact]
    public void BuildSystemPrompt_ShouldIncludeContextAndTodayDate()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var custId = Guid.NewGuid();

        // Act
        var prompt = OllamaPromptBuilder.BuildSystemPrompt(orgId, custId, "Test Poliklinik");

        // Assert
        prompt.Should().Contain(orgId.ToString());
        prompt.Should().Contain(custId.ToString());
        prompt.Should().Contain("Test Poliklinik");
        prompt.Should().Contain(DateTime.UtcNow.ToString("yyyy-MM-dd"));
        prompt.Should().Contain("get_available_slots");
        prompt.Should().Contain("create_appointment");
    }

    [Fact]
    public async Task ProcessChatAsync_WhenOllamaReturnsToolCall_ShouldExecuteToolAndSynthesizeReply()
    {
        // Arrange
        var ollamaClient = Substitute.For<IOllamaClient>();
        var sender = Substitute.For<ISender>();
        var loggerTool = Substitute.For<ILogger<McpToolRegistry>>();
        var toolRegistry = new McpToolRegistry(sender, loggerTool);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ollama:Model"] = "qwen3.5:27b"
            })
            .Build();

        var loggerOrch = Substitute.For<ILogger<McpFunctionCallingOrchestrator>>();
        var orchestrator = new McpFunctionCallingOrchestrator(ollamaClient, toolRegistry, config, loggerOrch);

        var orgId = Guid.NewGuid();
        var custId = Guid.NewGuid();

        // Step 1: Mock Ollama routing to tool call get_available_slots
        var argsJson = JsonSerializer.SerializeToElement(new
        {
            organization_id = orgId.ToString(),
            start_date = "2026-11-01"
        });

        var toolCall = new OllamaToolCall(
            Id: "call_123",
            Function: new OllamaFunctionCall("get_available_slots", argsJson)
        );

        var firstResponse = new OllamaChatResponse(
            Model: "qwen3.5:27b",
            CreatedAt: DateTime.UtcNow.ToString("O"),
            Message: new OllamaResponseMessage("assistant", "", new List<OllamaToolCall> { toolCall }),
            Done: true
        );

        // Step 2: Mock Ollama second response (synthesizing tool result to user)
        var secondResponse = new OllamaChatResponse(
            Model: "qwen3.5:27b",
            CreatedAt: DateTime.UtcNow.ToString("O"),
            Message: new OllamaResponseMessage("assistant", "1 Kasım 2026 tarihi için 09:00 ve 10:00 saatleri müsaittir.", null),
            Done: true
        );

        ollamaClient.ChatAsync(Arg.Any<OllamaChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(firstResponse, secondResponse);

        var request = new McpChatClientRequest(
            Message: "Müsait randevuları listele",
            OrganizationId: orgId,
            CustomerId: custId,
            AutoExecuteTools: true
        );

        // Act
        var result = await orchestrator.ProcessChatAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.ToolExecuted.Should().BeTrue();
        result.ToolName.Should().Be("get_available_slots");
        result.Reply.Should().Contain("1 Kasım 2026");
        result.ModelUsed.Should().Be("qwen3.5:27b");
        result.UpdatedHistory.Should().HaveCount(2);
        result.UpdatedHistory[0].Role.Should().Be("user");
        result.UpdatedHistory[1].Role.Should().Be("assistant");
    }

    [Fact]
    public async Task ProcessChatAsync_WhenOllamaReturnsDirectMessage_ShouldReturnWithoutToolExecution()
    {
        // Arrange
        var ollamaClient = Substitute.For<IOllamaClient>();
        var sender = Substitute.For<ISender>();
        var loggerTool = Substitute.For<ILogger<McpToolRegistry>>();
        var toolRegistry = new McpToolRegistry(sender, loggerTool);

        var config = new ConfigurationBuilder().Build();
        var loggerOrch = Substitute.For<ILogger<McpFunctionCallingOrchestrator>>();
        var orchestrator = new McpFunctionCallingOrchestrator(ollamaClient, toolRegistry, config, loggerOrch);

        var directResponse = new OllamaChatResponse(
            Model: "qwen3.5:27b",
            CreatedAt: DateTime.UtcNow.ToString("O"),
            Message: new OllamaResponseMessage("assistant", "Merhaba! Size randevu işlemlerinizde nasıl yardımcı olabilirim?", null),
            Done: true
        );

        ollamaClient.ChatAsync(Arg.Any<OllamaChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(directResponse);

        var request = new McpChatClientRequest(
            Message: "Merhaba"
        );

        // Act
        var result = await orchestrator.ProcessChatAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.ToolExecuted.Should().BeFalse();
        result.ToolName.Should().BeNull();
        result.Reply.Should().Be("Merhaba! Size randevu işlemlerinizde nasıl yardımcı olabilirim?");
    }

    [Fact]
    public async Task ProcessChatAsync_WhenToolArgumentsMissingOrgId_ShouldEnrichFromContext()
    {
        // Arrange
        var ollamaClient = Substitute.For<IOllamaClient>();
        var sender = Substitute.For<ISender>();
        var loggerTool = Substitute.For<ILogger<McpToolRegistry>>();
        var toolRegistry = new McpToolRegistry(sender, loggerTool);

        var config = new ConfigurationBuilder().Build();
        var loggerOrch = Substitute.For<ILogger<McpFunctionCallingOrchestrator>>();
        var orchestrator = new McpFunctionCallingOrchestrator(ollamaClient, toolRegistry, config, loggerOrch);

        var orgId = Guid.NewGuid();

        // LLM returns tool call without organization_id (only start_date)
        var argsJson = JsonSerializer.SerializeToElement(new
        {
            start_date = "2026-11-05"
        });

        var toolCall = new OllamaToolCall(
            Id: "call_456",
            Function: new OllamaFunctionCall("get_available_dates", argsJson)
        );

        var response = new OllamaChatResponse(
            Model: "qwen3.5:27b",
            CreatedAt: DateTime.UtcNow.ToString("O"),
            Message: new OllamaResponseMessage("assistant", "", new List<OllamaToolCall> { toolCall }),
            Done: true
        );

        ollamaClient.ChatAsync(Arg.Any<OllamaChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(response);

        var request = new McpChatClientRequest(
            Message: "Müsait günleri göster",
            OrganizationId: orgId,
            AutoExecuteTools: false // do not execute, just check enriched arguments
        );

        // Act
        var result = await orchestrator.ProcessChatAsync(request);

        // Assert
        result.ToolName.Should().Be("get_available_dates");
        result.ToolArguments.Should().NotBeNull();
        result.ToolArguments!.Value.GetProperty("organization_id").GetString().Should().Be(orgId.ToString());
    }
}
