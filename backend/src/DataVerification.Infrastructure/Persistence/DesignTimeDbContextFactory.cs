using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DataVerification.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c>. Building the API host at design time would also run startup
/// migration and seeding, so the tooling gets its own minimal context instead. The connection
/// string only has to be syntactically valid — scaffolding never opens it.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DV_DESIGNTIME_CONNECTION")
            ?? "Server=localhost,1433;Database=DataVerification;User Id=sa;Password=Your_strong_Passw0rd;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new ApplicationDbContext(options);
    }
}
