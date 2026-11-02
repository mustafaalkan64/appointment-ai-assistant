namespace AppointmentBooking.Domain.Enums;

public enum OrganizationRole
{
    OrganizationStaff = 1,
    OrganizationAdmin = 2,
    SuperAdmin = 99
}

public enum SlotStatus
{
    Available = 1,
    Reserved = 2,
    Booked = 3,
    Blocked = 4,
    Cancelled = 5
}

public enum AppointmentStatus
{
    Pending = 1,
    Confirmed = 2,
    Cancelled = 3,
    Completed = 4,
    NoShow = 5
}

public enum QueueStatus
{
    Waiting = 1,
    Processing = 2,
    Reserved = 3,
    Confirmed = 4,
    Failed = 5,
    Expired = 6,
    Cancelled = 7
}

public enum ConversationStatus
{
    Active = 1,
    AwaitingConfirmation = 2,
    Completed = 3,
    Abandoned = 4
}

public enum MessageRole
{
    User = 1,
    Assistant = 2,
    System = 3,
    Tool = 4
}
