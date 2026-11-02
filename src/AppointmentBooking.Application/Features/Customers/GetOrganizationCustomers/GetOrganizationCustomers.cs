using AppointmentBooking.Application.Abstractions.Persistence;
using AppointmentBooking.Application.Abstractions.Tenancy;
using AppointmentBooking.Application.Common;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace AppointmentBooking.Application.Features.Customers.GetOrganizationCustomers;

public sealed record CustomerSummaryDto(
    Guid Id,
    string FullName,
    string PhoneNumber,
    string Email,
    bool IsPhoneVerified,
    DateTimeOffset CreatedAt,
    int AppointmentCount,
    DateTimeOffset? LastAppointmentAt);

public sealed record GetOrganizationCustomersQuery(Guid OrganizationId) : IRequest<List<CustomerSummaryDto>>;

public sealed class GetOrganizationCustomersHandler : IRequestHandler<GetOrganizationCustomersQuery, List<CustomerSummaryDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public GetOrganizationCustomersHandler(IApplicationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<List<CustomerSummaryDto>> Handle(GetOrganizationCustomersQuery request, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(request.OrganizationId);

        // Find customer IDs linked via OrganizationCustomers or Appointments
        var linkedCustomerIds = await _context.OrganizationCustomers
            .Where(oc => oc.OrganizationId == request.OrganizationId)
            .Select(oc => oc.CustomerId)
            .ToListAsync(cancellationToken);

        var appointmentCustomerIds = await _context.Appointments
            .Where(a => a.OrganizationId == request.OrganizationId)
            .Select(a => a.CustomerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var allCustomerIds = linkedCustomerIds.Union(appointmentCustomerIds).Distinct().ToList();

        if (allCustomerIds.Count == 0)
        {
            // Fallback: If no organization-specific links exist, return customers
            allCustomerIds = await _context.Customers
                .Select(c => c.Id)
                .Take(50)
                .ToListAsync(cancellationToken);
        }

        var customers = await _context.Customers
            .Where(c => allCustomerIds.Contains(c.Id))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var appointments = await _context.Appointments
            .Where(a => a.OrganizationId == request.OrganizationId && allCustomerIds.Contains(a.CustomerId))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var result = customers.Select(c =>
        {
            var custAppts = appointments.Where(a => a.CustomerId == c.Id).ToList();
            var lastAppt = custAppts.OrderByDescending(a => a.CreatedAt).FirstOrDefault()?.CreatedAt;

            return new CustomerSummaryDto(
                c.Id,
                c.FullName,
                c.PhoneNumber.Value,
                c.Email.Value,
                c.IsPhoneVerified,
                c.CreatedAt,
                custAppts.Count,
                lastAppt);
        }).OrderByDescending(c => c.AppointmentCount).ThenBy(c => c.FullName).ToList();

        return result;
    }
}

public sealed class GetOrganizationCustomersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/organizations/{organizationId:guid}/customers", async (
            Guid organizationId,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetOrganizationCustomersQuery(organizationId), ct);
            return Results.Ok(result);
        })
        .WithName("GetOrganizationCustomers")
        .WithTags("Customers")
        .Produces<List<CustomerSummaryDto>>(StatusCodes.Status200OK);
    }
}
