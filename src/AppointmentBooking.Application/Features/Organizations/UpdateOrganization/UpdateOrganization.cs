using AppointmentBooking.Application.Abstractions.Persistence;
using AppointmentBooking.Application.Abstractions.Tenancy;
using AppointmentBooking.Application.Common;
using AppointmentBooking.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace AppointmentBooking.Application.Features.Organizations.UpdateOrganization;

public sealed record UpdateOrganizationRequest(
    string Name,
    string Email,
    string PhoneNumber,
    string Address,
    string? TimeZone);

public sealed record UpdateOrganizationCommand(
    Guid Id,
    string Name,
    string Email,
    string PhoneNumber,
    string Address,
    string? TimeZone) : IRequest<UpdateOrganizationResponse?>;

public sealed record UpdateOrganizationResponse(
    Guid Id,
    string Name,
    string Slug,
    string Email,
    string PhoneNumber,
    string Address,
    string TimeZone,
    bool IsActive);

public sealed class UpdateOrganizationValidator : AbstractValidator<UpdateOrganizationCommand>
{
    public UpdateOrganizationValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Address).MaximumLength(500);
    }
}

public sealed class UpdateOrganizationHandler : IRequestHandler<UpdateOrganizationCommand, UpdateOrganizationResponse?>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public UpdateOrganizationHandler(IApplicationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<UpdateOrganizationResponse?> Handle(UpdateOrganizationCommand request, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(Guid.Empty, isSuperAdmin: true);

        var organization = await _context.Organizations
            .FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);

        if (organization == null)
            return null;

        var emailObj = Domain.ValueObjects.Email.Create(request.Email);
        var emailExists = await _context.Organizations
            .AnyAsync(o => o.Id != request.Id && o.Email == emailObj, cancellationToken);

        if (emailExists)
            throw new DomainValidationException($"Organization with email '{request.Email}' already exists.");

        organization.UpdateDetails(request.Name, request.Address, request.TimeZone ?? "Europe/Istanbul");
        organization.UpdateContactInfo(request.Email, request.PhoneNumber);

        await _context.SaveChangesAsync(cancellationToken);

        return new UpdateOrganizationResponse(
            organization.Id,
            organization.Name,
            organization.Slug.Value,
            organization.Email.Value,
            organization.PhoneNumber.Value,
            organization.Address,
            organization.TimeZone.Value,
            organization.IsActive);
    }
}

public sealed class UpdateOrganizationEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/organizations/{id:guid}", async (
            Guid id,
            UpdateOrganizationRequest request,
            ISender sender,
            CancellationToken ct) =>
        {
            var command = new UpdateOrganizationCommand(
                id,
                request.Name,
                request.Email,
                request.PhoneNumber,
                request.Address,
                request.TimeZone);

            var response = await sender.Send(command, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("UpdateOrganization")
        .WithTags("Organizations")
        .Produces<UpdateOrganizationResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
