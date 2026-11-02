using AppointmentBooking.Domain.Common;

namespace AppointmentBooking.Domain.Entities;

public sealed class IdempotencyRecord : Entity<string>
{
    public Guid CustomerId { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public int ResponseStatusCode { get; private set; }
    public string ResponseBody { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    private IdempotencyRecord() { }

    public static string BuildCompositeKey(Guid customerId, string operation, string idempotencyKey)
    {
        return $"{customerId:N}:{operation.ToLowerInvariant()}:{idempotencyKey.Trim()}";
    }

    public static IdempotencyRecord Create(
        Guid customerId,
        string operation,
        string idempotencyKey,
        string requestHash,
        int responseStatusCode,
        string responseBody,
        TimeSpan timeToLive)
    {
        var now = DateTimeOffset.UtcNow;
        return new IdempotencyRecord
        {
            Id = BuildCompositeKey(customerId, operation, idempotencyKey),
            CustomerId = customerId,
            Operation = operation.ToLowerInvariant(),
            IdempotencyKey = idempotencyKey.Trim(),
            RequestHash = requestHash,
            ResponseStatusCode = responseStatusCode,
            ResponseBody = responseBody,
            CreatedAt = now,
            ExpiresAt = now.Add(timeToLive)
        };
    }
}

public sealed class OutboxMessage : Entity<Guid>
{
    public string Type { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public DateTimeOffset OccurredOnUtc { get; private set; }
    public DateTimeOffset? ProcessedOnUtc { get; private set; }
    public string? Error { get; private set; }
    public int RetryCount { get; private set; }

    private OutboxMessage() { }

    public static OutboxMessage Create(string type, string content, DateTimeOffset occurredOnUtc)
    {
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = type,
            Content = content,
            OccurredOnUtc = occurredOnUtc,
            RetryCount = 0
        };
    }

    public void MarkAsProcessed()
    {
        ProcessedOnUtc = DateTimeOffset.UtcNow;
        Error = null;
    }

    public void MarkAsFailed(string error)
    {
        Error = error;
        RetryCount++;
    }
}

public sealed class InboxMessage : Entity<Guid>
{
    public string Type { get; private set; } = string.Empty;
    public DateTimeOffset ProcessedOnUtc { get; private set; }

    private InboxMessage() { }

    public static InboxMessage Create(Guid id, string type)
    {
        return new InboxMessage
        {
            Id = id,
            Type = type,
            ProcessedOnUtc = DateTimeOffset.UtcNow
        };
    }
}
