using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AppointmentBooking.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        const string connectionString = "Host=localhost;Port=5432;Database=appointment_db;Username=postgres;Password=postgrespassword";

        optionsBuilder.UseNpgsql(connectionString, npgsqlOptions =>
        {
            npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
        });

        var dummyTenantContext = new TenantContext();
        dummyTenantContext.SetTenant(Guid.Empty, isSuperAdmin: true);

        return new ApplicationDbContext(optionsBuilder.Options, dummyTenantContext);
    }
}
