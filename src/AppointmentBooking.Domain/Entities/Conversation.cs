using AppointmentBooking.Domain.Common;
using AppointmentBooking.Domain.Enums;
using AppointmentBooking.Domain.Exceptions;

namespace AppointmentBooking.Domain.Entities;

public sealed class Conversation : AggregateRoot<Guid>, IMustHaveTenant, IAuditableEntity
{
    private readonly List<ConversationMessage> _messages = [];

    public Guid OrganizationId { get; set; }
    public Guid CustomerId { get; private set; }
    public ConversationStatus Status { get; private set; }
    public string ContextJson { get; private set; } = "{}";
    public Guid? PendingSlotId { get; private set; }
    public string? PendingOperation { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public IReadOnlyCollection<ConversationMessage> Messages => _messages.AsReadOnly();

    private Conversation() { }

    public static Conversation Start(Guid organizationId, Guid customerId)
    {
        if (organizationId == Guid.Empty)
            throw new DomainValidationException("OrganizationId is required.");

        if (customerId == Guid.Empty)
            throw new DomainValidationException("CustomerId is required.");

        return new Conversation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            CustomerId = customerId,
            Status = ConversationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public ConversationMessage AddMessage(
        MessageRole role,
        string content,
        string? intent = null,
        string? toolCallsJson = null)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new DomainValidationException("Message content cannot be empty.");

        var message = ConversationMessage.Create(Id, role, content, intent, toolCallsJson);
        _messages.Add(message);
        UpdatedAt = DateTimeOffset.UtcNow;
        return message;
    }

    public void SetPendingConfirmation(Guid slotId, string operation, string contextJson)
    {
        PendingSlotId = slotId;
        PendingOperation = operation;
        ContextJson = contextJson;
        Status = ConversationStatus.AwaitingConfirmation;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClearPendingConfirmation()
    {
        PendingSlotId = null;
        PendingOperation = null;
        Status = ConversationStatus.Active;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Complete()
    {
        Status = ConversationStatus.Completed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class ConversationMessage : Entity<Guid>
{
    public Guid ConversationId { get; private set; }
    public MessageRole Role { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string? Intent { get; private set; }
    public string? ToolCallsJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ConversationMessage() { }

    internal static ConversationMessage Create(
        Guid conversationId,
        MessageRole role,
        string content,
        string? intent,
        string? toolCallsJson)
    {
        return new ConversationMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = role,
            Content = content.Trim(),
            Intent = intent?.Trim(),
            ToolCallsJson = toolCallsJson,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
