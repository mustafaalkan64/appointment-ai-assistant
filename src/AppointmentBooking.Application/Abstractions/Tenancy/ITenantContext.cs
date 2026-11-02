namespace AppointmentBooking.Application.Abstractions.Tenancy;

public interface ITenantContext
{
    Guid OrganizationId { get; }
    bool IsResolved { get; }
    bool IsSuperAdmin { get; }
    void SetTenant(Guid organizationId, bool isSuperAdmin = false);
}

public interface ITenantBypassScope : IDisposable
{
}
