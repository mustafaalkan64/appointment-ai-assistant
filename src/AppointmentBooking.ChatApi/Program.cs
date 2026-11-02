using AppointmentBooking.Application;
using AppointmentBooking.ChatApi.Endpoints;
using AppointmentBooking.ChatApi.Services;
using AppointmentBooking.Infrastructure;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// 1. Core Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// 2. Chat API & MCP Client Services
builder.Services.AddHttpClient<IMcpClient, HttpMcpClient>(client =>
{
    var baseUrl = builder.Configuration["McpServer:BaseUrl"] ?? "http://localhost:5251";
    client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddScoped<IAiAgentOrchestrator, AiAgentOrchestrator>();

// 3. OpenAPI / Swagger Documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Appointment Booking Chat API",
        Version = "v1",
        Description = "AI Agent & Chat Orchestration API for Appointment Booking"
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
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Chat API v1");
});

// Redirect root "/" to "/swagger"
app.MapGet("/", () => Results.Redirect("/swagger"));

// 5. Map Endpoints
app.MapChatEndpoints();

app.Run();

// For WebApplicationFactory integration testing
namespace AppointmentBooking.ChatApi
{
    public partial class ChatProgram { }
}
