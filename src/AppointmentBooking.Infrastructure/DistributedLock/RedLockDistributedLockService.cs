using AppointmentBooking.Application.Abstractions.DistributedLock;
using Microsoft.Extensions.Logging;
using RedLockNet;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;

namespace AppointmentBooking.Infrastructure.DistributedLock;

public sealed class RedLockDistributedLockService : IDistributedLockService, IAsyncDisposable
{
    private readonly RedLockFactory _redLockFactory;
    private readonly ILogger<RedLockDistributedLockService> _logger;

    public RedLockDistributedLockService(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RedLockDistributedLockService> logger)
    {
        _logger = logger;
        var multiplexers = new List<RedLockMultiplexer> { new(connectionMultiplexer) };
        _redLockFactory = RedLockFactory.Create(multiplexers);
    }

    public async Task<IDistributedLockHandle?> TryAcquireLockAsync(
        string resourceKey,
        TimeSpan expiryTime,
        TimeSpan waitTime,
        TimeSpan retryTime,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Attempting to acquire distributed lock for resource: {ResourceKey}", resourceKey);

        try
        {
            var redLock = await _redLockFactory.CreateLockAsync(
                resourceKey,
                expiryTime,
                waitTime,
                retryTime,
                cancellationToken);

            if (redLock.IsAcquired)
            {
                _logger.LogInformation("Distributed lock acquired for resource: {ResourceKey}", resourceKey);
                return new RedLockHandle(redLock, resourceKey, _logger);
            }

            _logger.LogWarning("Failed to acquire distributed lock for resource: {ResourceKey} within wait time: {WaitTime}ms",
                resourceKey, waitTime.TotalMilliseconds);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while acquiring distributed lock for resource: {ResourceKey}", resourceKey);
            return null;
        }
    }

    public async Task<TResult> ExecuteWithLockAsync<TResult>(
        string resourceKey,
        TimeSpan expiryTime,
        TimeSpan waitTime,
        TimeSpan retryTime,
        Func<CancellationToken, Task<TResult>> action,
        Func<CancellationToken, Task<TResult>> onLockFailed,
        CancellationToken cancellationToken = default)
    {
        var lockHandle = await TryAcquireLockAsync(resourceKey, expiryTime, waitTime, retryTime, cancellationToken);
        if (lockHandle == null)
        {
            return await onLockFailed(cancellationToken);
        }

        await using (lockHandle)
        {
            return await action(cancellationToken);
        }
    }

    public ValueTask DisposeAsync()
    {
        _redLockFactory.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class RedLockHandle : IDistributedLockHandle
    {
        private readonly IRedLock _redLock;
        private readonly ILogger _logger;
        private bool _disposed;

        public string ResourceKey { get; }
        public bool IsAcquired => _redLock.IsAcquired;

        public RedLockHandle(IRedLock redLock, string resourceKey, ILogger logger)
        {
            _redLock = redLock;
            ResourceKey = resourceKey;
            _logger = logger;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _redLock.Dispose();
                _logger.LogInformation("Distributed lock released for resource: {ResourceKey}", ResourceKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error releasing distributed lock for resource: {ResourceKey}", ResourceKey);
            }

            await Task.CompletedTask;
        }
    }
}
