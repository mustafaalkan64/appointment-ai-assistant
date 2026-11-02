using System.Net;
using System.Text.Json;
using AppointmentBooking.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AppointmentBooking.Api.Middleware;

public sealed class GlobalExceptionHandlerMiddleware : IMiddleware
{
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";

        var (statusCode, title, detail, errors) = exception switch
        {
            ValidationException valEx => (
                HttpStatusCode.BadRequest,
                "Validation Error",
                "One or more validation failures have occurred.",
                valEx.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())
            ),
            SlotNotAvailableException slotEx => (
                HttpStatusCode.Conflict,
                "Slot Not Available",
                slotEx.Message,
                null
            ),
            ActiveAppointmentOnSlotException activeEx => (
                HttpStatusCode.Conflict,
                "Active Appointment Conflict",
                activeEx.Message,
                null
            ),
            InvalidAppointmentStateException stateEx => (
                HttpStatusCode.BadRequest,
                "Invalid Appointment State",
                stateEx.Message,
                null
            ),
            DomainValidationException domainEx => (
                HttpStatusCode.BadRequest,
                "Domain Validation Failed",
                domainEx.Message,
                null
            ),
            TenantMismatchException tenantEx => (
                HttpStatusCode.Forbidden,
                "Tenant Access Denied",
                tenantEx.Message,
                null
            ),
            _ => (
                HttpStatusCode.InternalServerError,
                "Server Error",
                "An unexpected internal error occurred.",
                null
            )
        };

        context.Response.StatusCode = (int)statusCode;

        var problemDetails = new
        {
            type = $"https://httpstatuses.com/{(int)statusCode}",
            title,
            status = (int)statusCode,
            detail,
            instance = context.Request.Path.Value,
            errors
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails));
    }
}
