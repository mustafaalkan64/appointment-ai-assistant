namespace AppointmentBooking.Application.Abstractions.DistributedLock;

public interface IDistributedLockService
{
    Task<IDistributedLockHandle?> TryAcquireLockAsync(
        string resourceKey,
        TimeSpan expiryTime,
        TimeSpan waitTime,
        TimeSpan retryTime,
        CancellationToken cancellationToken = default);

    Task<TResult> ExecuteWithLockAsync<TResult>(
        string resourceKey,
        TimeSpan expiryTime,
        TimeSpan waitTime,
        TimeSpan retryTime,
        Func<CancellationToken, Task<TResult>> action,
        Func<CancellationToken, Task<TResult>> onLockFailed,
        CancellationToken cancellationToken = default);
}

public interface IDistributedLockHandle : IAsyncDisposable
{
    string ResourceKey { get; }
    bool IsAcquired { get; }
}
