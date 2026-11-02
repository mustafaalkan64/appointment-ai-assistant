using AppointmentBooking.Domain.Common;
using AppointmentBooking.Domain.Exceptions;
using AppointmentBooking.Domain.ValueObjects;

namespace AppointmentBooking.Domain.Entities;

public sealed class Customer : AggregateRoot<Guid>, IAuditableEntity, ISoftDeletable
{
    private readonly List<OrganizationCustomer> _organizationMemberships = [];

    public string FullName { get; private set; } = string.Empty;
    public PhoneNumber PhoneNumber { get; private set; } = null!;
    public Email Email { get; private set; } = null!;
    public string? ExternalCustomerId { get; private set; }
    public bool IsPhoneVerified { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public IReadOnlyCollection<OrganizationCustomer> OrganizationMemberships => _organizationMemberships.AsReadOnly();

    private Customer() { }

    public static Customer Create(
        string fullName,
        string phoneNumber,
        string email,
        string? externalCustomerId = null)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainValidationException("Customer full name cannot be empty.");

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            FullName = fullName.Trim(),
            PhoneNumber = PhoneNumber.Create(phoneNumber),
            Email = Email.Create(email),
            ExternalCustomerId = externalCustomerId?.Trim(),
            IsPhoneVerified = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        return customer;
    }

    public void UpdateContactInfo(string fullName, string phoneNumber, string email)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainValidationException("Customer full name cannot be empty.");

        FullName = fullName.Trim();
        PhoneNumber = PhoneNumber.Create(phoneNumber);
        Email = Email.Create(email);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void VerifyPhoneNumber()
    {
        IsPhoneVerified = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void LinkToOrganization(Guid organizationId, string? notes = null)
    {
        var existing = _organizationMemberships.FirstOrDefault(m => m.OrganizationId == organizationId);
        if (existing == null)
        {
            _organizationMemberships.Add(OrganizationCustomer.Create(organizationId, Id, notes));
        }
        else
        {
            existing.UpdateLastSeen();
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class OrganizationCustomer : Entity<Guid>, IMustHaveTenant
{
    public Guid OrganizationId { get; set; }
    public Guid CustomerId { get; private set; }
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset? LastAppointmentAt { get; private set; }
    public string? Notes { get; private set; }

    private OrganizationCustomer() { }

    internal static OrganizationCustomer Create(Guid organizationId, Guid customerId, string? notes = null)
    {
        return new OrganizationCustomer
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            CustomerId = customerId,
            FirstSeenAt = DateTimeOffset.UtcNow,
            Notes = notes?.Trim()
        };
    }

    internal void UpdateLastSeen(DateTimeOffset? appointmentDate = null)
    {
        if (appointmentDate.HasValue)
        {
            LastAppointmentAt = appointmentDate.Value;
        }
    }
}
