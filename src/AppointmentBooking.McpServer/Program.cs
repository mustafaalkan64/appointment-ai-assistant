using AppointmentBooking.Application;
using AppointmentBooking.Infrastructure;
using AppointmentBooking.McpServer.Endpoints;
using AppointmentBooking.McpServer;
using AppointmentBooking.McpServer.Services;
using AppointmentBooking.McpServer.Services.Ollama;
using Microsoft.OpenApi;
using ModelContextProtocol.AspNetCore;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 1. Core Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// 2. MCP Server Services & Ollama Function Calling
builder.Services.AddScoped<McpToolRegistry>();
builder.Services.AddMcpServer()
    // The 2026-07-28 Streamable HTTP transport is stateless. Use SDK default
    // explicitly so the transport contract is obvious and doesn't depend on defaults.
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithTools<McpSdkTools>()
    .WithResources<AppointmentResources>()
    .WithPrompts<AppointmentPrompts>();

builder.Services.AddHttpClient<IOllamaClient, OllamaClient>(client =>
{
    var baseUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
    client.BaseAddress = new Uri(baseUrl);
    var timeoutSeconds = builder.Configuration.GetValue<int?>("Ollama:TimeoutSeconds") ?? 120;
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});
builder.Services.AddScoped<IMcpFunctionCallingOrchestrator, McpFunctionCallingOrchestrator>();

// 3. OpenAPI / Swagger Documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Appointment Booking MCP Server API",
        Version = "v1",
        Description = "Model Context Protocol (MCP) & Ollama Function Calling Server"
    });
});

// 4. CORS and MCP Origin validation
var allowedOrigins = builder.Configuration
    .GetSection("McpServer:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});

var app = builder.Build();

// MCP requires servers to validate Origin to prevent DNS rebinding attacks.
// Requests without Origin (typical for non-browser MCP clients) remain supported.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/mcp") &&
        context.Request.Headers.TryGetValue("Origin", out var originHeader))
    {
        var origin = originHeader.ToString();
        var validOrigin = Uri.TryCreate(origin, UriKind.Absolute, out var parsedOrigin) &&
                          (parsedOrigin.Scheme == Uri.UriSchemeHttp || parsedOrigin.Scheme == Uri.UriSchemeHttps) &&
                          string.IsNullOrEmpty(parsedOrigin.UserInfo) &&
                          string.IsNullOrEmpty(parsedOrigin.AbsolutePath.TrimEnd('/')) &&
                          string.IsNullOrEmpty(parsedOrigin.Query) &&
                          string.IsNullOrEmpty(parsedOrigin.Fragment);
        var isAllowed = validOrigin &&
                        allowedOrigins.Any(configured =>
                            Uri.TryCreate(configured, UriKind.Absolute, out var allowed) &&
                            string.Equals(allowed.GetLeftPart(UriPartial.Authority),
                                parsedOrigin!.GetLeftPart(UriPartial.Authority),
                                StringComparison.OrdinalIgnoreCase));

        if (!validOrigin || !isAllowed)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Origin is not allowed.");
            return;
        }
    }

    await next();
});

app.UseCors();

// Enable Swagger UI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MCP Server API v1");
});

// Redirect root "/" to "/swagger"
app.MapGet("/", () => Results.Redirect("/swagger"));

// 5. Map MCP Endpoints
app.MapMcpEndpoints();
app.MapMcp("/mcp");

app.Run();

// For WebApplicationFactory integration testing
namespace AppointmentBooking.McpServer
{
    public partial class McpProgram { }
}
