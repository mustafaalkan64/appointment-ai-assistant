extern alias McpServerAssembly;

using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using AppointmentBooking.Domain.Entities;
using AppointmentBooking.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using JsonRpcResponse = McpServerAssembly::AppointmentBooking.McpServer.Protocol.JsonRpcResponse;
using McpProgram = McpServerAssembly::AppointmentBooking.McpServer.McpProgram;

namespace AppointmentBooking.IntegrationTests.Mcp;

public class McpServerTests : IClassFixture<WebApplicationFactory<McpProgram>>
{
    private readonly WebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public McpServerTests(WebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Mcp_Initialize_ShouldReturnProtocolCapabilities()
    {
        var response = await PostMcpAsync("server/discover", null, 1);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rpcResponse = await ReadMcpResponseAsync(response);

        rpcResponse.Should().NotBeNull();
        rpcResponse!.JsonRpc.Should().Be("2.0");
        rpcResponse.Error.Should().BeNull();
        rpcResponse.Result.Should().NotBeNull();
    }

    [Fact]
    public async Task Mcp_ToolsList_ShouldReturnAllRegisteredTools()
    {
        var response = await PostMcpAsync("tools/list", null, 2);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rpcResponse = await ReadMcpResponseAsync(response);

        rpcResponse.Should().NotBeNull();
        rpcResponse!.Error.Should().BeNull();

        var resultDoc = JsonDocument.Parse(JsonSerializer.Serialize(rpcResponse.Result));
        var tools = resultDoc.RootElement.GetProperty("tools").EnumerateArray().ToList();

        tools.Should().HaveCountGreaterThanOrEqualTo(9);
        var toolNames = tools.Select(t => t.GetProperty("name").GetString()).ToList();
        toolNames.Should().Contain(new[]
        {
            "get_organization_info",
            "get_available_dates",
            "get_available_slots",
            "create_appointment",
            "get_appointment",
            "get_customer_appointments",
            "cancel_appointment",
            "get_queue_status",
            "cancel_queue_request"
        });

        var createTool = tools.Single(t => t.GetProperty("name").GetString() == "create_appointment");
        var annotations = createTool.GetProperty("annotations");
        annotations.GetProperty("readOnlyHint").GetBoolean().Should().BeFalse();
        annotations.GetProperty("destructiveHint").GetBoolean().Should().BeFalse();
        annotations.GetProperty("idempotentHint").GetBoolean().Should().BeTrue();

        var cancelTool = tools.Single(t => t.GetProperty("name").GetString() == "cancel_appointment");
        cancelTool.GetProperty("annotations").GetProperty("destructiveHint").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Mcp_ToolsCall_GetOrganizationInfo_ShouldReturnOrgDetails()
    {
        // Seed an organization
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var slug = "mcp-clinic-" + Guid.NewGuid().ToString("N")[..8];
        var org = Organization.Create("MCP Clinic", slug, $"{slug}@test.com", "+905551112233", "Ist", "Europe/Istanbul");
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        var callParams = new
        {
            name = "get_organization_info",
            arguments = new { slug }
        };

        var response = await PostMcpAsync("tools/call", callParams, 3, "get_organization_info");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rpcResponse = await ReadMcpResponseAsync(response);

        rpcResponse.Should().NotBeNull();
        rpcResponse!.Error.Should().BeNull();

        var resultDoc = JsonDocument.Parse(JsonSerializer.Serialize(rpcResponse.Result));
        var contentArray = resultDoc.RootElement.GetProperty("content").EnumerateArray().ToList();
        contentArray.Should().NotBeEmpty();

        var text = contentArray[0].GetProperty("text").GetString();
        text.Should().Contain(slug);
        text.Should().Contain("MCP Clinic");
    }

    [Fact]
    public async Task Mcp_ResourcesList_ShouldReturnSystemResources()
    {
        var response = await PostMcpAsync("resources/list", null, 4);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rpcResponse = await ReadMcpResponseAsync(response);

        rpcResponse.Should().NotBeNull();
        rpcResponse!.Error.Should().BeNull();

        var resultDoc = JsonDocument.Parse(JsonSerializer.Serialize(rpcResponse.Result));
        var resources = resultDoc.RootElement.GetProperty("resources").EnumerateArray().ToList();
        resources.Should().NotBeEmpty();
        resources.Select(x => x.GetProperty("uri").GetString())
            .Should().Contain("appointment://system/booking-guidelines");
    }

    [Fact]
    public async Task Mcp_ResourcesRead_ShouldReturnRequestedResource()
    {
        var parameters = new
        {
            uri = "appointment://system/booking-guidelines"
        };

        var response = await PostMcpAsync("resources/read", parameters, 5, "appointment://system/booking-guidelines");
        var rpcResponse = await ReadMcpResponseAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        rpcResponse!.Error.Should().BeNull();
        var resultDoc = JsonDocument.Parse(JsonSerializer.Serialize(rpcResponse.Result));
        resultDoc.RootElement.GetProperty("contents")[0].GetProperty("text").GetString()
            .Should().Contain("slot_id");
    }

    [Fact]
    public async Task Mcp_PromptsListAndGet_ShouldExposeReusableBookingPrompt()
    {
        var listResponse = await PostMcpAsync("prompts/list", null, 6);
        var listRpcResponse = await ReadMcpResponseAsync(listResponse);
        listRpcResponse!.Error.Should().BeNull();
        var prompts = JsonDocument.Parse(JsonSerializer.Serialize(listRpcResponse.Result))
            .RootElement.GetProperty("prompts").EnumerateArray().ToList();
        prompts.Select(x => x.GetProperty("name").GetString()).Should().Contain("book_appointment");

        var parameters = new
        {
            name = "book_appointment",
            arguments = new { requested_time = "2026-11-20 saat 10:00" }
        };
        var getResponse = await PostMcpAsync("prompts/get", parameters, 7, "book_appointment");
        var getRpcResponse = await ReadMcpResponseAsync(getResponse);

        getRpcResponse!.Error.Should().BeNull();
        var promptDoc = JsonDocument.Parse(JsonSerializer.Serialize(getRpcResponse.Result));
        promptDoc.RootElement.GetProperty("messages")[0].GetProperty("content").GetProperty("text")
            .GetString().Should().Contain("2026-11-20 saat 10:00");
    }

    [Fact]
    public async Task Mcp_Health_ShouldReturnHealthy()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Integrated");
    }

    [Fact]
    public async Task Mcp_Request_WithUnconfiguredOrigin_ShouldBeForbidden()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/list",
                @params = new { }
            })
        };
        request.Headers.TryAddWithoutValidation("Origin", "https://untrusted.example");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task McpChat_WithEmptyMessage_ShouldReturnBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/mcp/chat", new { message = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<HttpResponseMessage> PostMcpAsync(string method, object? parameters, int id, string? name = null)
    {
        var paramsNode = parameters is null
            ? new JsonObject()
            : JsonSerializer.SerializeToNode(parameters)!.AsObject();
        paramsNode["_meta"] = new JsonObject
        {
            ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
            ["io.modelcontextprotocol/clientInfo"] = new JsonObject
            {
                ["name"] = "AppointmentBooking.IntegrationTests",
                ["version"] = "1.0.0"
            },
            ["io.modelcontextprotocol/clientCapabilities"] = new JsonObject()
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = paramsNode
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", method);
        if (name is not null)
            request.Headers.TryAddWithoutValidation("Mcp-Name", name);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonRpcResponse?> ReadMcpResponseAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            body = string.Join("\n", body.Split('\n')
                .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
                .Select(line => line["data:".Length..].Trim()));
        }
        return JsonSerializer.Deserialize<JsonRpcResponse>(body);
    }
}
