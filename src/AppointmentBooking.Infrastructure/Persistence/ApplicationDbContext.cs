using System.Linq.Expressions;
using System.Reflection;
using AppointmentBooking.Application.Abstractions.Persistence;
using AppointmentBooking.Application.Abstractions.Tenancy;
using AppointmentBooking.Domain.Common;
using AppointmentBooking.Domain.Entities;
using AppointmentBooking.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace AppointmentBooking.Infrastructure.Persistence;

public sealed class ApplicationDbContext : DbContext, IApplicationDbContext
{
    private readonly ITenantContext _tenantContext;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationUser> OrganizationUsers => Set<OrganizationUser>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<OrganizationCustomer> OrganizationCustomers => Set<OrganizationCustomer>();
    public DbSet<WeeklyAvailability> WeeklyAvailabilities => Set<WeeklyAvailability>();
    public DbSet<SpecialAvailability> SpecialAvailabilities => Set<SpecialAvailability>();
    public DbSet<AppointmentSlot> AppointmentSlots => Set<AppointmentSlot>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<WaitingQueueEntry> WaitingQueueEntries => Set<WaitingQueueEntry>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task<int> ExecuteConditionalSlotBookingAsync(Guid slotId, CancellationToken cancellationToken = default)
    {
        // Layer 2: Atomic State Transition Conditional SQL Update
        const string sql = """
            UPDATE "AppointmentSlots"
            SET "Status" = @BookedStatus, "UpdatedAt" = NOW()
            WHERE "Id" = @SlotId AND ("Status" = @AvailableStatus OR "Status" = @ReservedStatus) AND "IsActive" = TRUE;
            """;

        return await Database.ExecuteSqlRawAsync(
            sql,
            new object[]
            {
                new NpgsqlParameter("BookedStatus", (int)SlotStatus.Booked),
                new NpgsqlParameter("SlotId", slotId),
                new NpgsqlParameter("AvailableStatus", (int)SlotStatus.Available),
                new NpgsqlParameter("ReservedStatus", (int)SlotStatus.Reserved)
            },
            cancellationToken);
    }

    public async Task<int> ExecuteConditionalSlotReleaseAsync(Guid slotId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE "AppointmentSlots"
            SET "Status" = @AvailableStatus, "UpdatedAt" = NOW()
            WHERE "Id" = @SlotId AND "Status" = @BookedStatus;
            """;

        return await Database.ExecuteSqlRawAsync(
            sql,
            new object[]
            {
                new NpgsqlParameter("AvailableStatus", (int)SlotStatus.Available),
                new NpgsqlParameter("SlotId", slotId),
                new NpgsqlParameter("BookedStatus", (int)SlotStatus.Booked)
            },
            cancellationToken);
    }

    public Guid CurrentOrganizationId => _tenantContext.OrganizationId;
    public bool IsSuperAdmin => _tenantContext.IsSuperAdmin;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all entity type configurations
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Configure Global Query Filters for Tenant Isolation and Soft Deletes
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            var isTenantEntity = typeof(IMustHaveTenant).IsAssignableFrom(clrType);
            var isSoftDeleteEntity = typeof(ISoftDeletable).IsAssignableFrom(clrType);

            if (isTenantEntity || isSoftDeleteEntity)
            {
                var parameter = Expression.Parameter(clrType, "e");
                Expression? filter = null;

                if (isTenantEntity)
                {
                    // Access through 'this' DbContext so EF Core parameterizes it per DbContext instance
                    var tenantProp = Expression.Property(parameter, nameof(IMustHaveTenant.OrganizationId));
                    var currentTenantExpr = Expression.Property(Expression.Constant(this), nameof(CurrentOrganizationId));
                    var isSuperAdminExpr = Expression.Property(Expression.Constant(this), nameof(IsSuperAdmin));

                    var tenantEquals = Expression.Equal(tenantProp, currentTenantExpr);
                    filter = Expression.OrElse(isSuperAdminExpr, tenantEquals);
                }

                if (isSoftDeleteEntity)
                {
                    // e => !e.IsDeleted
                    var softDeleteProp = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
                    var notDeleted = Expression.Not(softDeleteProp);

                    filter = filter != null ? Expression.AndAlso(filter, notDeleted) : notDeleted;
                }

                if (filter != null)
                {
                    var lambda = Expression.Lambda(filter, parameter);
                    entityType.SetQueryFilter(lambda);
                }
            }
        }
    }
}
