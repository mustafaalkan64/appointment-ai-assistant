namespace AppointmentBooking.Domain.Exceptions;

public abstract class DomainException : Exception
{
    public string Code { get; }

    protected DomainException(string message, string code = "DOMAIN_ERROR") : base(message)
    {
        Code = code;
    }
}

public sealed class SlotNotAvailableException : DomainException
{
    public Guid SlotId { get; }

    public SlotNotAvailableException(Guid slotId, string currentStatus)
        : base($"Slot '{slotId}' is not available for booking. Current status: '{currentStatus}'.", "SLOT_NOT_AVAILABLE")
    {
        SlotId = slotId;
    }
}

public sealed class ActiveAppointmentOnSlotException : DomainException
{
    public Guid SlotId { get; }

    public ActiveAppointmentOnSlotException(Guid slotId)
        : base($"Cannot deactivate slot '{slotId}' because an active appointment exists on it. Cancel the appointment first or use explicit administrative override.", "ACTIVE_APPOINTMENT_EXISTS")
    {
        SlotId = slotId;
    }
}

public sealed class InvalidAppointmentStateException : DomainException
{
    public InvalidAppointmentStateException(string message)
        : base(message, "INVALID_APPOINTMENT_STATE")
    {
    }
}

public sealed class InvalidQueueStateException : DomainException
{
    public InvalidQueueStateException(string message)
        : base(message, "INVALID_QUEUE_STATE")
    {
    }
}

public sealed class TenantMismatchException : DomainException
{
    public TenantMismatchException(Guid expectedTenant, Guid actualTenant)
        : base($"Tenant mismatch: Entity belongs to organization '{actualTenant}', but operation was scoped to '{expectedTenant}'.", "TENANT_MISMATCH")
    {
    }
}

public sealed class ConcurrencyConflictException : DomainException
{
    public ConcurrencyConflictException(string message)
        : base(message, "CONCURRENCY_CONFLICT")
    {
    }
}

public sealed class DomainValidationException : DomainException
{
    public DomainValidationException(string message)
        : base(message, "VALIDATION_FAILED")
    {
    }
}
