using AppointmentBooking.Application.Abstractions.Persistence;
using AppointmentBooking.Application.Abstractions.Tenancy;
using AppointmentBooking.Application.Common;
using AppointmentBooking.Domain.Entities;
using AppointmentBooking.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace AppointmentBooking.Application.Features.Customers.RegisterCustomer;

public sealed record RegisterCustomerCommand(
    Guid OrganizationId,
    string FullName,
    string PhoneNumber,
    string Email,
    string? ExternalCustomerId = null) : IRequest<RegisterCustomerResponse>;

public sealed record RegisterCustomerResponse(
    Guid Id,
    string FullName,
    string PhoneNumber,
    string Email,
    string? ExternalCustomerId,
    bool IsNewCustomer);

public sealed class RegisterCustomerValidator : AbstractValidator<RegisterCustomerCommand>
{
    public RegisterCustomerValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
    }
}

public sealed class RegisterCustomerHandler : IRequestHandler<RegisterCustomerCommand, RegisterCustomerResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public RegisterCustomerHandler(IApplicationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<RegisterCustomerResponse> Handle(RegisterCustomerCommand request, CancellationToken cancellationToken)
    {
        // Customer lookup is global across organizations
        _tenantContext.SetTenant(Guid.Empty, isSuperAdmin: true);

        var phone = PhoneNumber.Create(request.PhoneNumber);
        var existingCustomer = await _context.Customers
            .Include(c => c.OrganizationMemberships)
            .FirstOrDefaultAsync(c => c.PhoneNumber == phone, cancellationToken);

        if (existingCustomer != null)
        {
            // Link to current organization if not already linked
            existingCustomer.LinkToOrganization(request.OrganizationId);
            await _context.SaveChangesAsync(cancellationToken);

            return new RegisterCustomerResponse(
                existingCustomer.Id,
                existingCustomer.FullName,
                existingCustomer.PhoneNumber.Value,
                existingCustomer.Email.Value,
                existingCustomer.ExternalCustomerId,
                IsNewCustomer: false);
        }

        var customer = Customer.Create(
            request.FullName,
            request.PhoneNumber,
            request.Email,
            request.ExternalCustomerId);

        customer.LinkToOrganization(request.OrganizationId);

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(cancellationToken);

        return new RegisterCustomerResponse(
            customer.Id,
            customer.FullName,
            customer.PhoneNumber.Value,
            customer.Email.Value,
            customer.ExternalCustomerId,
            IsNewCustomer: true);
    }
}

public sealed class RegisterCustomerEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/customers", async (RegisterCustomerCommand command, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(command, ct);
            return Results.Ok(response);
        })
        .WithName("RegisterCustomer")
        .WithTags("Customers")
        .Produces<RegisterCustomerResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem();
    }
}
