using AppointmentBooking.Application;
using AppointmentBooking.Infrastructure;
using AppointmentBooking.McpServer.Endpoints;
using AppointmentBooking.McpServer.Services;
using AppointmentBooking.McpServer.Services.Ollama;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// 1. Core Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// 2. MCP Server Services & Ollama Function Calling
builder.Services.AddScoped<McpToolRegistry>();

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

// 4. CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

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

app.Run();

// For WebApplicationFactory integration testing
namespace AppointmentBooking.McpServer
{
    public partial class McpProgram { }
}
