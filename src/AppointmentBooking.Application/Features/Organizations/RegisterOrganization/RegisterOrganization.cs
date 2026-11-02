using System.Security.Cryptography;
using System.Text;
using AppointmentBooking.Application.Abstractions.Persistence;
using AppointmentBooking.Application.Abstractions.Tenancy;
using AppointmentBooking.Application.Common;
using AppointmentBooking.Domain.Entities;
using AppointmentBooking.Domain.Enums;
using AppointmentBooking.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace AppointmentBooking.Application.Features.Organizations.RegisterOrganization;

public sealed record RegisterOrganizationCommand(
    string Name,
    string Slug,
    string Email,
    string PhoneNumber,
    string Address,
    string? TimeZone,
    string AdminEmail,
    string AdminPassword,
    string AdminFullName) : IRequest<RegisterOrganizationResponse>;

public sealed record RegisterOrganizationResponse(
    Guid Id,
    string Name,
    string Slug,
    string Email,
    string TimeZone,
    Guid AdminUserId,
    DateTimeOffset CreatedAt);

public sealed class RegisterOrganizationValidator : AbstractValidator<RegisterOrganizationCommand>
{
    public RegisterOrganizationValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(150)
            .Matches(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")
            .WithMessage("Slug must consist of lowercase alphanumeric characters separated by hyphens.");
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(32);
        RuleFor(x => x.AdminEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.AdminPassword).NotEmpty().MinimumLength(6);
        RuleFor(x => x.AdminFullName).NotEmpty().MaximumLength(200);
    }
}

public sealed class RegisterOrganizationHandler : IRequestHandler<RegisterOrganizationCommand, RegisterOrganizationResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;

    public RegisterOrganizationHandler(IApplicationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<RegisterOrganizationResponse> Handle(RegisterOrganizationCommand request, CancellationToken cancellationToken)
    {
        // Registration is system-level, enable SuperAdmin / bypass context
        _tenantContext.SetTenant(Guid.Empty, isSuperAdmin: true);

        var slugExists = await _context.Organizations
            .AnyAsync(o => o.Slug == Domain.ValueObjects.Slug.FromExisting(request.Slug.ToLowerInvariant()), cancellationToken);

        if (slugExists)
            throw new DomainValidationException($"Organization with slug '{request.Slug}' already exists.");

        var emailExists = await _context.Organizations
            .AnyAsync(o => o.Email == Domain.ValueObjects.Email.FromExisting(request.Email.ToLowerInvariant()), cancellationToken);

        if (emailExists)
            throw new DomainValidationException($"Organization with email '{request.Email}' already exists.");

        var organization = Organization.Create(
            request.Name,
            request.Slug,
            request.Email,
            request.PhoneNumber,
            request.Address,
            request.TimeZone ?? "Europe/Istanbul");

        var passwordHash = HashPassword(request.AdminPassword);
        var adminUser = organization.AddUser(
            request.AdminEmail,
            passwordHash,
            request.AdminFullName,
            OrganizationRole.OrganizationAdmin);

        // Seed default standard weekly schedule (Mon - Fri: 09:00 - 17:00, Sat: 10:00 - 14:00, 30 min slots)
        for (var day = DayOfWeek.Monday; day <= DayOfWeek.Friday; day++)
        {
            organization.SetWeeklySchedule(day, TimeSpan.FromHours(9), TimeSpan.FromHours(17), 30);
        }
        organization.SetWeeklySchedule(DayOfWeek.Saturday, TimeSpan.FromHours(10), TimeSpan.FromHours(14), 30);

        _context.Organizations.Add(organization);
        await _context.SaveChangesAsync(cancellationToken);

        return new RegisterOrganizationResponse(
            organization.Id,
            organization.Name,
            organization.Slug.Value,
            organization.Email.Value,
            organization.TimeZone.Value,
            adminUser.Id,
            organization.CreatedAt);
    }

    private static string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }
}

public sealed class RegisterOrganizationEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/organizations", async (RegisterOrganizationCommand command, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(command, ct);
            return Results.Created($"/api/organizations/{response.Id}", response);
        })
        .WithName("RegisterOrganization")
        .WithTags("Organizations")
        .Produces<RegisterOrganizationResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
