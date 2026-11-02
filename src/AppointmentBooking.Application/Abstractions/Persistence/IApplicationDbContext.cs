using AppointmentBooking.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace AppointmentBooking.Application.Abstractions.Persistence;

public interface IApplicationDbContext
{
    DbSet<Organization> Organizations { get; }
    DbSet<OrganizationUser> OrganizationUsers { get; }
    DbSet<Customer> Customers { get; }
    DbSet<OrganizationCustomer> OrganizationCustomers { get; }
    DbSet<WeeklyAvailability> WeeklyAvailabilities { get; }
    DbSet<SpecialAvailability> SpecialAvailabilities { get; }
    DbSet<AppointmentSlot> AppointmentSlots { get; }
    DbSet<Appointment> Appointments { get; }
    DbSet<WaitingQueueEntry> WaitingQueueEntries { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<ConversationMessage> ConversationMessages { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<InboxMessage> InboxMessages { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task<int> ExecuteConditionalSlotBookingAsync(Guid slotId, CancellationToken cancellationToken = default);
    Task<int> ExecuteConditionalSlotReleaseAsync(Guid slotId, CancellationToken cancellationToken = default);
}
