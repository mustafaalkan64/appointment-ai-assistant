using AppointmentBooking.Domain.Entities;
using AppointmentBooking.Domain.Enums;
using AppointmentBooking.Infrastructure.Persistence;
using AppointmentBooking.Infrastructure.Persistence.Interceptors;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AppointmentBooking.IntegrationTests.Database;

public class PostgresDatabaseTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=appointment_db;Username=postgres;Password=postgrespassword";

    private ApplicationDbContext CreateContext(Guid tenantId, bool isSuperAdmin = false)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, isSuperAdmin);

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(ConnectionString)
            .AddInterceptors(new AuditableAndOutboxInterceptor(tenantContext));

        return new ApplicationDbContext(optionsBuilder.Options, tenantContext);
    }

    [Fact]
    public async Task Postgres_DatabaseConstraint_DoubleBooking_MustThrowUniqueConstraintException()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();

        await using var context = CreateContext(orgId);

        var org = Organization.Create("Clinic Concurrency", "clinic-conc-" + Guid.NewGuid().ToString("N")[..6], "conc@clinic.com", "+905559998877", "Address");
        context.Organizations.Add(org);

        var slot = AppointmentSlot.Create(org.Id, DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow.AddDays(2).AddMinutes(30));
        context.AppointmentSlots.Add(slot);
        await context.SaveChangesAsync();

        // Act: Create first appointment on slot
        var appt1 = Appointment.Create(org.Id, customerA, slot.Id, "First Booking");
        context.Appointments.Add(appt1);
        await context.SaveChangesAsync();

        // Act & Assert: Attempting to insert a second active appointment for the same slot
        await using var contextSecond = CreateContext(orgId);
        var appt2 = Appointment.Create(org.Id, customerB, slot.Id, "Double Booking Attempt");
        contextSecond.Appointments.Add(appt2);

        var act = async () => await contextSecond.SaveChangesAsync();

        var exception = await act.Should().ThrowAsync<DbUpdateException>();
        exception.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be("23505"); // 23505 = unique_violation
    }

    [Fact]
    public async Task Postgres_TenantIsolation_GlobalQueryFilter_ShouldHideOtherTenantData()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var contextA = CreateContext(tenantA);
        var orgA = Organization.Create("Tenant A", "tenant-a-" + Guid.NewGuid().ToString("N")[..6], "a@a.com", "+905550000001", "Address");
        contextA.Organizations.Add(orgA);

        var slotA = AppointmentSlot.Create(tenantA, DateTimeOffset.UtcNow.AddDays(3), DateTimeOffset.UtcNow.AddDays(3).AddMinutes(30));
        contextA.AppointmentSlots.Add(slotA);
        await contextA.SaveChangesAsync();

        // Act: Query as Tenant B
        await using var contextB = CreateContext(tenantB);
        var slotsForB = await contextB.AppointmentSlots.ToListAsync();

        // Assert: Tenant B must NOT see slotA
        slotsForB.Should().NotContain(s => s.Id == slotA.Id);

        // Act: Query as SuperAdmin
        await using var contextAdmin = CreateContext(Guid.Empty, isSuperAdmin: true);
        var allSlots = await contextAdmin.AppointmentSlots.ToListAsync();
        allSlots.Should().Contain(s => s.Id == slotA.Id);
    }
}
