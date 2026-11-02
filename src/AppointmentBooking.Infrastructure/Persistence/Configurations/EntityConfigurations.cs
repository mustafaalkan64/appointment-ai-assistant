using AppointmentBooking.Domain.Entities;
using AppointmentBooking.Domain.Enums;
using AppointmentBooking.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppointmentBooking.Infrastructure.Persistence.Configurations;

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(o => o.Slug)
            .HasConversion(s => s.Value, v => Slug.FromExisting(v))
            .HasMaxLength(150)
            .IsRequired();

        builder.HasIndex(o => o.Slug)
            .IsUnique();

        builder.Property(o => o.Email)
            .HasConversion(e => e.Value, v => Email.FromExisting(v))
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(o => o.PhoneNumber)
            .HasConversion(p => p.Value, v => PhoneNumber.FromExisting(v))
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(o => o.Address)
            .HasMaxLength(500);

        builder.Property(o => o.TimeZone)
            .HasConversion(t => t.Value, v => TimeZoneId.FromExisting(v))
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(o => o.IsActive)
            .IsRequired();

        builder.HasMany(o => o.Users)
            .WithOne()
            .HasForeignKey(u => u.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(o => o.WeeklyAvailabilities)
            .WithOne()
            .HasForeignKey(w => w.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(o => o.SpecialAvailabilities)
            .WithOne()
            .HasForeignKey(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class OrganizationUserConfiguration : IEntityTypeConfiguration<OrganizationUser>
{
    public void Configure(EntityTypeBuilder<OrganizationUser> builder)
    {
        builder.ToTable("OrganizationUsers");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email)
            .HasMaxLength(256)
            .IsRequired();

        builder.HasIndex(u => new { u.OrganizationId, u.Email })
            .IsUnique();

        builder.Property(u => u.FullName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(u => u.PasswordHash)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(u => u.Role)
            .HasConversion<int>()
            .IsRequired();
    }
}

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.FullName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(c => c.PhoneNumber)
            .HasConversion(p => p.Value, v => PhoneNumber.FromExisting(v))
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(c => c.PhoneNumber)
            .IsUnique();

        builder.Property(c => c.Email)
            .HasConversion(e => e.Value, v => Email.FromExisting(v))
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(c => c.ExternalCustomerId)
            .HasMaxLength(150);

        builder.HasMany(c => c.OrganizationMemberships)
            .WithOne()
            .HasForeignKey(m => m.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class OrganizationCustomerConfiguration : IEntityTypeConfiguration<OrganizationCustomer>
{
    public void Configure(EntityTypeBuilder<OrganizationCustomer> builder)
    {
        builder.ToTable("OrganizationCustomers");

        builder.HasKey(oc => oc.Id);

        builder.HasIndex(oc => new { oc.OrganizationId, oc.CustomerId })
            .IsUnique();

        builder.Property(oc => oc.Notes)
            .HasMaxLength(1000);
    }
}

public sealed class WeeklyAvailabilityConfiguration : IEntityTypeConfiguration<WeeklyAvailability>
{
    public void Configure(EntityTypeBuilder<WeeklyAvailability> builder)
    {
        builder.ToTable("WeeklyAvailabilities");

        builder.HasKey(w => w.Id);

        builder.HasIndex(w => new { w.OrganizationId, w.DayOfWeek })
            .IsUnique();

        builder.Property(w => w.StartTime)
            .IsRequired();

        builder.Property(w => w.EndTime)
            .IsRequired();

        builder.Property(w => w.SlotDurationMinutes)
            .IsRequired();
    }
}

public sealed class SpecialAvailabilityConfiguration : IEntityTypeConfiguration<SpecialAvailability>
{
    public void Configure(EntityTypeBuilder<SpecialAvailability> builder)
    {
        builder.ToTable("SpecialAvailabilities");

        builder.HasKey(s => s.Id);

        builder.HasIndex(s => new { s.OrganizationId, s.Date })
            .IsUnique();

        builder.Property(s => s.Reason)
            .HasMaxLength(300);
    }
}

public sealed class AppointmentSlotConfiguration : IEntityTypeConfiguration<AppointmentSlot>
{
    public void Configure(EntityTypeBuilder<AppointmentSlot> builder)
    {
        builder.ToTable("AppointmentSlots");

        builder.HasKey(s => s.Id);

        builder.OwnsOne(s => s.TimeSlot, ts =>
        {
            ts.Property(p => p.StartUtc)
                .HasColumnName("StartUtc")
                .IsRequired();

            ts.Property(p => p.EndUtc)
                .HasColumnName("EndUtc")
                .IsRequired();
        });

        builder.Property(s => s.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(s => s.IsActive)
            .IsRequired();

        // PostgreSQL xmin system column as row version concurrency token
        builder.Property(s => s.Version)
            .IsRowVersion();

        builder.HasIndex(s => new { s.OrganizationId, s.Status, s.IsActive });
    }
}

public sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.AppointmentNumber)
            .HasConversion(n => n.Value, v => AppointmentNumber.FromExisting(v))
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(a => a.AppointmentNumber)
            .IsUnique();

        builder.Property(a => a.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(a => a.Description)
            .HasMaxLength(1000);

        builder.Property(a => a.CancellationReason)
            .HasMaxLength(500);

        builder.HasIndex(a => new { a.OrganizationId, a.CustomerId });
        builder.HasIndex(a => new { a.OrganizationId, a.Status });

        // CRITICAL DEFENSE LAYER 3: PostgreSQL Partial Unique Index
        // Guarantees zero double booking: A slot can only have AT MOST ONE active appointment!
        // 3 is AppointmentStatus.Cancelled
        builder.HasIndex(a => a.SlotId)
            .IsUnique()
            .HasFilter("\"Status\" != 3")
            .HasDatabaseName("IX_Appointments_SlotId_Active");
    }
}

public sealed class WaitingQueueEntryConfiguration : IEntityTypeConfiguration<WaitingQueueEntry>
{
    public void Configure(EntityTypeBuilder<WaitingQueueEntry> builder)
    {
        builder.ToTable("WaitingQueueEntries");

        builder.HasKey(q => q.Id);

        builder.Property(q => q.RequestId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(q => q.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(q => q.FailureReason)
            .HasMaxLength(500);

        // FIFO and Priority scanning index
        builder.HasIndex(q => new { q.SlotId, q.Status, q.Priority, q.CreatedAt });

        // Prevent same customer queue spamming for the same slot while active (1=Waiting, 2=Processing, 3=Reserved)
        builder.HasIndex(q => new { q.SlotId, q.CustomerId })
            .IsUnique()
            .HasFilter("\"Status\" IN (1, 2, 3)")
            .HasDatabaseName("IX_WaitingQueueEntries_Slot_Customer_Active");
    }
}

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(c => c.ContextJson)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(c => c.PendingOperation)
            .HasMaxLength(100);

        builder.HasIndex(c => new { c.OrganizationId, c.CustomerId, c.Status });

        builder.HasMany(c => c.Messages)
            .WithOne()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ConversationMessageConfiguration : IEntityTypeConfiguration<ConversationMessage>
{
    public void Configure(EntityTypeBuilder<ConversationMessage> builder)
    {
        builder.ToTable("ConversationMessages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Role)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(m => m.Content)
            .IsRequired();

        builder.Property(m => m.Intent)
            .HasMaxLength(100);

        builder.Property(m => m.ToolCallsJson)
            .HasColumnType("jsonb");

        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
    }
}

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Operation)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.IdempotencyKey)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(r => r.RequestHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(r => r.ResponseBody)
            .IsRequired();

        builder.HasIndex(r => r.ExpiresAt);
        builder.HasIndex(r => new { r.CustomerId, r.Operation, r.IdempotencyKey })
            .IsUnique();
    }
}

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Type)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(o => o.Content)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.HasIndex(o => new { o.ProcessedOnUtc, o.OccurredOnUtc });
    }
}

public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("InboxMessages");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Type)
            .HasMaxLength(200)
            .IsRequired();
    }
}
