using AppointmentBooking.Application.Abstractions.Tenancy;
using Microsoft.AspNetCore.Http;

namespace AppointmentBooking.Api.Middleware;

public sealed class TenantResolutionMiddleware : IMiddleware
{
    private readonly ITenantContext _tenantContext;

    public TenantResolutionMiddleware(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Headers.TryGetValue("X-Organization-Id", out var tenantHeader) &&
            Guid.TryParse(tenantHeader, out var tenantId))
        {
            var isSuperAdmin = context.User.IsInRole("SuperAdmin");
            _tenantContext.SetTenant(tenantId, isSuperAdmin);
        }

        await next(context);
    }
}
