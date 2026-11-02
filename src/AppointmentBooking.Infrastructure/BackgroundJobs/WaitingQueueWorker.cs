using AppointmentBooking.Application.Abstractions.DistributedLock;
using AppointmentBooking.Application.Abstractions.Persistence;
using AppointmentBooking.Application.Abstractions.Tenancy;
using AppointmentBooking.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AppointmentBooking.Infrastructure.BackgroundJobs;

public sealed class WaitingQueueWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WaitingQueueWorker> _logger;
    private readonly TimeSpan _pollingInterval;

    public WaitingQueueWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<WaitingQueueWorker> logger,
        TimeSpan? pollingInterval = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pollingInterval = pollingInterval ?? TimeSpan.FromSeconds(5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WaitingQueueWorker background service started with interval {Interval}s.", _pollingInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredReservationsAsync(stoppingToken);
                await ProcessAvailableSlotsWithWaitingQueueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in WaitingQueueWorker loop execution.");
            }

            try
            {
                await Task.Delay(_pollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("WaitingQueueWorker background service is stopping.");
    }

    public async Task ProcessExpiredReservationsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var distributedLock = scope.ServiceProvider.GetRequiredService<IDistributedLockService>();

        // SuperAdmin bypass ensures queue worker scans across all tenants
        tenantContext.SetTenant(Guid.Empty, isSuperAdmin: true);

        var now = DateTimeOffset.UtcNow;
        var expiredEntries = await context.WaitingQueueEntries
            .Where(q => q.Status == QueueStatus.Reserved && q.ReservedUntil.HasValue && q.ReservedUntil.Value <= now)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (!expiredEntries.Any())
            return;

        _logger.LogInformation("Found {Count} expired reservations in WaitingQueue to process.", expiredEntries.Count);

        foreach (var entry in expiredEntries)
        {
            var lockKey = $"appointment:slot:{entry.OrganizationId}:{entry.SlotId}";
            var lockHandle = await distributedLock.TryAcquireLockAsync(
                lockKey, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(150), cancellationToken);

            if (lockHandle == null)
            {
                _logger.LogWarning("Could not acquire lock for slot {SlotId} during reservation expiration.", entry.SlotId);
                continue;
            }

            await using (lockHandle)
            {
                entry.Expire();

                var slot = await context.AppointmentSlots
                    .FirstOrDefaultAsync(s => s.Id == entry.SlotId, cancellationToken);

                if (slot != null)
                {
                    // Check if an active appointment exists for this slot
                    var hasActiveAppointment = await context.Appointments
                        .AnyAsync(a => a.SlotId == entry.SlotId && a.Status != AppointmentStatus.Cancelled, cancellationToken);

                    if (!hasActiveAppointment)
                    {
                        // Check if another waiting customer exists
                        var nextInQueue = await context.WaitingQueueEntries
                            .Where(q => q.SlotId == entry.SlotId && q.Status == QueueStatus.Waiting)
                            .OrderByDescending(q => q.Priority)
                            .ThenBy(q => q.CreatedAt)
                            .FirstOrDefaultAsync(cancellationToken);

                        if (nextInQueue != null)
                        {
                            nextInQueue.StartProcessing();
                            nextInQueue.Reserve(TimeSpan.FromMinutes(5));
                            slot.Reserve(nextInQueue.CustomerId, TimeSpan.FromMinutes(5));
                            _logger.LogInformation("Slot {SlotId} expired reservation handed over to next customer {CustomerId}.",
                                slot.Id, nextInQueue.CustomerId);
                        }
                        else
                        {
                            slot.ReleaseToAvailable();
                            await context.ExecuteConditionalSlotReleaseAsync(slot.Id, cancellationToken);
                            _logger.LogInformation("Slot {SlotId} expired reservation returned to Available status.", slot.Id);
                        }
                    }
                }

                await context.SaveChangesAsync(cancellationToken);
            }
        }
    }

    public async Task ProcessAvailableSlotsWithWaitingQueueAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var distributedLock = scope.ServiceProvider.GetRequiredService<IDistributedLockService>();

        tenantContext.SetTenant(Guid.Empty, isSuperAdmin: true);

        // Find slot IDs that have waiting entries
        var slotsWithWaiting = await context.WaitingQueueEntries
            .Where(q => q.Status == QueueStatus.Waiting)
            .Select(q => new { q.SlotId, q.OrganizationId })
            .Distinct()
            .Take(20)
            .ToListAsync(cancellationToken);

        if (!slotsWithWaiting.Any())
            return;

        foreach (var item in slotsWithWaiting)
        {
            var slot = await context.AppointmentSlots
                .FirstOrDefaultAsync(s => s.Id == item.SlotId && s.Status == SlotStatus.Available && s.IsActive, cancellationToken);

            if (slot == null)
                continue;

            var lockKey = $"appointment:slot:{item.OrganizationId}:{item.SlotId}";
            var lockHandle = await distributedLock.TryAcquireLockAsync(
                lockKey, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(150), cancellationToken);

            if (lockHandle == null)
                continue;

            await using (lockHandle)
            {
                // Re-verify slot is still Available
                if (slot.Status != SlotStatus.Available)
                    continue;

                var nextInQueue = await context.WaitingQueueEntries
                    .Where(q => q.SlotId == item.SlotId && q.Status == QueueStatus.Waiting)
                    .OrderByDescending(q => q.Priority)
                    .ThenBy(q => q.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (nextInQueue != null)
                {
                    nextInQueue.StartProcessing();
                    nextInQueue.Reserve(TimeSpan.FromMinutes(5));
                    slot.Reserve(nextInQueue.CustomerId, TimeSpan.FromMinutes(5));

                    _logger.LogInformation("Allocated available slot {SlotId} to queued customer {CustomerId}.",
                        slot.Id, nextInQueue.CustomerId);

                    await context.SaveChangesAsync(cancellationToken);
                }
            }
        }
    }
}
