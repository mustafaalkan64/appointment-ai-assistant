using AppointmentBooking.Application.Abstractions.Persistence;
using AppointmentBooking.Application.Abstractions.Tenancy;
using AppointmentBooking.Application.Common;
using AppointmentBooking.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace AppointmentBooking.Application.Features.Organizations.GetOrganization;

public sealed record GetOrganizationQuery(Guid? Id, string? Slug) : IRequest<GetOrganizationResponse?>;

public sealed record WeeklyAvailabilityDto(DayOfWeek DayOfWeek, string DayName, TimeSpan StartTime, TimeSpan EndTime, int SlotDurationMinutes, bool IsActive);

public sealed record GetOrganizationResponse(
    Guid Id,
    string Name,
    string Slug,
    string Email,
    string PhoneNumber,
    string Address,
    string TimeZone,
    bool IsActive,
    List<WeeklyAvailabilityDto> WeeklyAvailabilities);

public sealed class GetOrganizationHandler : IRequestHandler<GetOrganizationQuery, GetOrganizationResponse?>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public GetOrganizationHandler(IApplicationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<GetOrganizationResponse?> Handle(GetOrganizationQuery request, CancellationToken cancellationToken)
    {
        // Public querying uses bypass filter so prospective customers / AI agents can fetch info
        _tenantContext.SetTenant(Guid.Empty, isSuperAdmin: true);

        var query = _context.Organizations
            .Include(o => o.WeeklyAvailabilities)
            .AsNoTracking();

        var organization = request.Id.HasValue
            ? await query.FirstOrDefaultAsync(o => o.Id == request.Id.Value, cancellationToken)
            : await query.FirstOrDefaultAsync(o => o.Slug == Slug.FromExisting(request.Slug!.ToLowerInvariant()), cancellationToken);

        if (organization == null) return null;

        var weekly = organization.WeeklyAvailabilities
            .OrderBy(w => w.DayOfWeek)
            .Select(w => new WeeklyAvailabilityDto(w.DayOfWeek, w.DayOfWeek.ToString(), w.StartTime, w.EndTime, w.SlotDurationMinutes, w.IsActive))
            .ToList();

        return new GetOrganizationResponse(
            organization.Id,
            organization.Name,
            organization.Slug.Value,
            organization.Email.Value,
            organization.PhoneNumber.Value,
            organization.Address,
            organization.TimeZone.Value,
            organization.IsActive,
            weekly);
    }
}

public sealed record GetAllOrganizationsQuery : IRequest<List<GetOrganizationResponse>>;

public sealed class GetAllOrganizationsHandler : IRequestHandler<GetAllOrganizationsQuery, List<GetOrganizationResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public GetAllOrganizationsHandler(IApplicationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<List<GetOrganizationResponse>> Handle(GetAllOrganizationsQuery request, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(Guid.Empty, isSuperAdmin: true);

        var orgs = await _context.Organizations
            .Include(o => o.WeeklyAvailabilities)
            .Where(o => o.IsActive)
            .OrderBy(o => o.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return orgs.Select(org => new GetOrganizationResponse(
            org.Id,
            org.Name,
            org.Slug.Value,
            org.Email.Value,
            org.PhoneNumber.Value,
            org.Address,
            org.TimeZone.Value,
            org.IsActive,
            org.WeeklyAvailabilities
                .OrderBy(w => w.DayOfWeek)
                .Select(w => new WeeklyAvailabilityDto(w.DayOfWeek, w.DayOfWeek.ToString(), w.StartTime, w.EndTime, w.SlotDurationMinutes, w.IsActive))
                .ToList()
        )).ToList();
    }
}

public sealed class GetOrganizationEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/organizations", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetAllOrganizationsQuery(), ct);
            return Results.Ok(result);
        })
        .WithName("GetAllOrganizations")
        .WithTags("Organizations")
        .Produces<List<GetOrganizationResponse>>(StatusCodes.Status200OK);

        app.MapGet("/api/organizations/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetOrganizationQuery(id, null), ct);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        })
        .WithName("GetOrganizationById")
        .WithTags("Organizations")
        .Produces<GetOrganizationResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/organizations/by-slug/{slug}", async (string slug, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetOrganizationQuery(null, slug), ct);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        })
        .WithName("GetOrganizationBySlug")
        .WithTags("Organizations")
        .Produces<GetOrganizationResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }
}
