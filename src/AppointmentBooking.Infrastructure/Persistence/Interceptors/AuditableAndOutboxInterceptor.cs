using System.Text.Json;
using AppointmentBooking.Application.Abstractions.Tenancy;
using AppointmentBooking.Domain.Common;
using AppointmentBooking.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AppointmentBooking.Infrastructure.Persistence.Interceptors;

public sealed class AuditableAndOutboxInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenantContext;

    public AuditableAndOutboxInterceptor(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ProcessEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ProcessEntities(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ProcessEntities(DbContext? context)
    {
        if (context == null) return;

        var now = DateTimeOffset.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<IMustHaveTenant>())
        {
            if (entry.State == EntityState.Added && entry.Entity.OrganizationId == Guid.Empty && _tenantContext.IsResolved)
            {
                entry.Entity.OrganizationId = _tenantContext.OrganizationId;
            }
        }

        // Conversation messages are immutable append-only records
        foreach (var entry in context.ChangeTracker.Entries<ConversationMessage>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.State = EntityState.Added;
            }
        }

        // Outbox pattern: extract domain events from aggregates
        var aggregateEntries = context.ChangeTracker
            .Entries()
            .Where(e => e.Entity.GetType().BaseType is { IsGenericType: true } baseType &&
                        baseType.GetGenericTypeDefinition() == typeof(AggregateRoot<>))
            .ToList();

        List<OutboxMessage> outboxMessages = [];

        foreach (var entry in aggregateEntries)
        {
            var entity = entry.Entity;
            var domainEventsProperty = entity.GetType().GetProperty(nameof(AggregateRoot<Guid>.DomainEvents));
            if (domainEventsProperty?.GetValue(entity) is IReadOnlyCollection<IDomainEvent> domainEvents && domainEvents.Count > 0)
            {
                foreach (var domainEvent in domainEvents)
                {
                    var eventType = domainEvent.GetType().FullName ?? domainEvent.GetType().Name;
                    var content = JsonSerializer.Serialize(domainEvent, domainEvent.GetType());
                    outboxMessages.Add(OutboxMessage.Create(eventType, content, domainEvent.OccurredOnUtc));
                }

                var clearMethod = entity.GetType().GetMethod(nameof(AggregateRoot<Guid>.ClearDomainEvents));
                clearMethod?.Invoke(entity, null);
            }
        }

        if (outboxMessages.Count > 0)
        {
            context.Set<OutboxMessage>().AddRange(outboxMessages);
        }
    }
}
