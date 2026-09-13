using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace DataVerification.IntegrationTests;

/// <summary>
/// Boots the real API in memory against a real SQL Server database.
///
/// SQL Server rather than the in-memory provider on purpose: the behaviour worth testing here
/// lives in the database — the retrying execution strategy wrapping wallet payments, rowversion
/// concurrency, unique indexes, and the cascade paths the migration had to work around. The
/// in-memory provider silently accepts all of that, so a suite built on it would pass while the
/// deployed system failed.
///
/// The database is reused between runs and migrations are applied on boot, so a second run costs
/// nothing. Every test therefore has to be idempotent — each one registers its own order rather
/// than assuming an empty table.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@dataverification.local";
    public const string AdminPassword = "Admin#12345";

    private readonly string _storageRoot = Path.Combine(
        Path.GetTempPath(),
        "dataverification-integration-storage");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Development is what runs migrations and seeding on boot, and what echoes the generated
        // order password back in the registration response so a test can sign in as the applicant.
        builder.UseEnvironment("Development");

        Directory.CreateDirectory(_storageRoot);

        // Environment variables rather than an in-memory configuration source, because the API is
        // built with WebApplication.CreateBuilder: it adds appsettings.json to the configuration
        // itself, and a source contributed from here lands underneath it. Environment variables are
        // added after appsettings by that same builder, so they are the one layer guaranteed to win.
        var settings = new Dictionary<string, string>
        {
            ["ConnectionStrings__DefaultConnection"] =
                Environment.GetEnvironmentVariable("INTEGRATION_TESTS_CONNECTION")
                ?? "Server=localhost;Database=DataVerification_IntegrationTests;"
                   + "Integrated Security=true;TrustServerCertificate=True;Encrypt=False;"
                   + "MultipleActiveResultSets=true",

            ["Jwt__SigningKey"] = "integration-tests-signing-key-at-least-32-bytes-long",

            // Uploaded evidence is written to a temp directory rather than the repository.
            ["Storage__RootPath"] = _storageRoot,

            // Registration, sign-in and uploads are deliberately throttled in production. A suite
            // that registers an order per test, and attaches evidence to most of them, trips all
            // three and then fails for the wrong reason — a 429 on the sixtieth upload says
            // nothing about the code under test, and which test draws it depends only on the order
            // xUnit happened to run them in.
            ["RateLimiting__Registration__PermitLimit"] = "100000",
            ["RateLimiting__Authentication__PermitLimit"] = "100000",
            ["RateLimiting__Uploads__PermitLimit"] = "100000",

            ["Smtp__Enabled"] = "false",
            ["App__EchoCredentialsInResponse"] = "true",
        };

        foreach (var (key, value) in settings)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
