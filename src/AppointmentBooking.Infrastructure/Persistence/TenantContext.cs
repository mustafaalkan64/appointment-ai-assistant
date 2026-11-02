using AppointmentBooking.Application.Abstractions.Tenancy;

namespace AppointmentBooking.Infrastructure.Persistence;

public sealed class TenantContext : ITenantContext
{
    private readonly AsyncLocal<Guid> _currentTenantId = new();
    private readonly AsyncLocal<bool> _isSuperAdmin = new();
    private readonly AsyncLocal<bool> _isResolved = new();

    public Guid OrganizationId => _currentTenantId.Value;
    public bool IsResolved => _isResolved.Value;
    public bool IsSuperAdmin => _isSuperAdmin.Value;

    public void SetTenant(Guid organizationId, bool isSuperAdmin = false)
    {
        _currentTenantId.Value = organizationId;
        _isSuperAdmin.Value = isSuperAdmin;
        _isResolved.Value = true;
    }

    public IDisposable CreateBypassScope()
    {
        var originalSuperAdmin = _isSuperAdmin.Value;
        _isSuperAdmin.Value = true;
        return new TenantBypassScope(() => _isSuperAdmin.Value = originalSuperAdmin);
    }

    private sealed class TenantBypassScope(Action resetAction) : ITenantBypassScope
    {
        public void Dispose() => resetAction();
    }
}
