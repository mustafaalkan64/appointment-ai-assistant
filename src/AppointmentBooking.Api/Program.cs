using AppointmentBooking.Api.Middleware;
using AppointmentBooking.Application;
using AppointmentBooking.Application.Common;
using AppointmentBooking.Infrastructure;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// 1. Core Services & Application / Infrastructure DI
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// 2. Discover and Register Vertical Slice Minimal API Endpoints
builder.Services.AddEndpoints(typeof(AppointmentBooking.Application.DependencyInjection).Assembly);

// 3. Middlewares
builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();
builder.Services.AddTransient<TenantResolutionMiddleware>();

// 4. OpenAPI / Swagger Documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Appointment Booking Platform API",
        Version = "v1",
        Description = "Multi-Tenant, High-Concurrency Appointment Booking Platform with AI Agent & MCP support"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
        }
    });
});

// 5. CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

// Configure the HTTP pipeline
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
app.UseMiddleware<TenantResolutionMiddleware>();

app.UseCors("AllowAll");

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Appointment Booking API v1");
});

// Redirect root "/" to "/swagger"
app.MapGet("/", () => Results.Redirect("/swagger"));

// Health Check Endpoint
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTimeOffset.UtcNow }))
    .WithName("HealthCheck")
    .WithTags("Health");

// Map all Vertical Slice Minimal API Endpoints
app.MapApplicationEndpoints();

app.Run();

public partial class Program { }
