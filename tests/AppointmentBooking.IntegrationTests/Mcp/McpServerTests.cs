extern alias McpServerAssembly;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AppointmentBooking.Domain.Entities;
using AppointmentBooking.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using JsonRpcRequest = McpServerAssembly::AppointmentBooking.McpServer.Protocol.JsonRpcRequest;
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
        var request = new JsonRpcRequest("2.0", "initialize", null, 1);
        var response = await _client.PostAsJsonAsync("/mcp", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var rpcResponse = JsonSerializer.Deserialize<JsonRpcResponse>(json);

        rpcResponse.Should().NotBeNull();
        rpcResponse!.JsonRpc.Should().Be("2.0");
        rpcResponse.Error.Should().BeNull();
        rpcResponse.Result.Should().NotBeNull();
    }

    [Fact]
    public async Task Mcp_ToolsList_ShouldReturnAllRegisteredTools()
    {
        var request = new JsonRpcRequest("2.0", "tools/list", null, 2);
        var response = await _client.PostAsJsonAsync("/mcp", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var rpcResponse = JsonSerializer.Deserialize<JsonRpcResponse>(json);

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

        var callParams = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            name = "get_organization_info",
            arguments = new { slug }
        })).RootElement;

        var request = new JsonRpcRequest("2.0", "tools/call", callParams, 3);
        var response = await _client.PostAsJsonAsync("/mcp", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var rpcResponse = JsonSerializer.Deserialize<JsonRpcResponse>(json);

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
        var request = new JsonRpcRequest("2.0", "resources/list", null, 4);
        var response = await _client.PostAsJsonAsync("/mcp", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var rpcResponse = JsonSerializer.Deserialize<JsonRpcResponse>(json);

        rpcResponse.Should().NotBeNull();
        rpcResponse!.Error.Should().BeNull();
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
    public async Task McpChat_WithEmptyMessage_ShouldReturnBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/mcp/chat", new { message = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
