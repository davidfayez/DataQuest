using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataVerification.Infrastructure.Persistence;

/// <summary>
/// One demo order carrying an application in every status, so the review workflow can be exercised
/// without first driving an applicant through the whole wizard by hand.
/// </summary>
/// <remarks>
/// Development only, and keyed on a fixed order id so re-running the seeder adds nothing. The
/// applications are placed with <see cref="VerificationApplication.TransitionTo"/> rather than the
/// applicant's own <c>Submit</c>/<c>MarkPaid</c> path: this is scenery, and demanding real uploads
/// and a funded wallet to produce it would make the seeder fragile for no benefit.
/// </remarks>
public sealed class DemoApplicationSeeder
{
    private static readonly Guid OrderId = new("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid WalletId = new("bbbbbbbb-0000-0000-0000-000000000002");

    /// <summary>Each demo application: the status to leave it in, and who it is addressed to.</summary>
    private static readonly (ApplicationStatus Status, string AddressedTo)[] Scenarios =
    [
        (ApplicationStatus.Draft, "Ministry of Higher Education"),
        (ApplicationStatus.PendingPayment, "Directorate of Passports"),
        (ApplicationStatus.Pending, "Chamber of Commerce"),
        (ApplicationStatus.InProgress, "Supreme Council of Universities"),
        (ApplicationStatus.MissedInfo, "Ministry of Foreign Affairs"),
        (ApplicationStatus.Success, "Ministry of Justice"),
        (ApplicationStatus.Failed, "General Authority for Investment"),
    ];

    private readonly ApplicationDbContext _db;
    private readonly IPasswordHashingService _passwordHasher;
    private readonly ILogger<DemoApplicationSeeder> _logger;

    public DemoApplicationSeeder(
        ApplicationDbContext db,
        IPasswordHashingService passwordHasher,
        ILogger<DemoApplicationSeeder> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await _db.Orders.AnyAsync(o => o.Id == OrderId, cancellationToken))
        {
            return;
        }

        var client = await _db.Clients.FirstOrDefaultAsync(cancellationToken);
        var service = await _db.ServiceTypes
            .Include(s => s.Costs)
            .FirstOrDefaultAsync(s => s.Id == DemoIds.StandardService, cancellationToken);

        // The cascade is seeded before this runs; if any of it is missing the environment is not
        // one where demo scenery makes sense, so skip rather than half-build it.
        if (client is null || service is null)
        {
            _logger.LogWarning("Demo applications skipped: the seeded service cascade is not present.");
            return;
        }

        var price = service.Costs.FirstOrDefault(c => c.CurrencyId == DemoIds.Egp);
        if (price is null)
        {
            _logger.LogWarning("Demo applications skipped: no EGP price on the seeded service.");
            return;
        }

        var order = new Order
        {
            Id = OrderId,
            ClientId = client.Id,
            Email = "demo.applicant@dataverification.local",
            OrderNumber = "NEN000000001",
            PasswordHash = _passwordHasher.Hash("Demo#12345"),
            LanguageCode = "en",
            VerificationCountryId = DemoIds.Egypt,
            CurrencyId = DemoIds.Egp,
        };

        _db.Orders.Add(order);

        var wallet = new Wallet { Id = WalletId, OrderId = OrderId, CurrencyId = DemoIds.Egp };
        wallet.Credit(50_000m, WalletTransactionType.TopUp, Actor.System(), null, "Demo wallet float");
        _db.Wallets.Add(wallet);

        var applicant = Actor.Applicant(OrderId, order.OrderNumber);
        var reviewer = Actor.Admin(DemoIds.SuperAdminUser, "Platform Administrator");

        var sequence = 1;
        foreach (var (status, addressedTo) in Scenarios)
        {
            _db.Applications.Add(BuildApplication(
                order, service, price, addressedTo, status, applicant, reviewer, sequence++));
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Seeded demo order {OrderNumber} with {Count} applications across the status lifecycle.",
            order.OrderNumber,
            Scenarios.Length);
    }

    private static VerificationApplication BuildApplication(
        Order order,
        ServiceType service,
        ServiceTypeCost price,
        string addressedTo,
        ApplicationStatus target,
        Actor applicant,
        Actor reviewer,
        int sequence)
    {
        var application = new VerificationApplication
        {
            OrderId = order.Id,
            ApplicationNumber = $"APP-DEMO-{sequence:0000}",
            AddressedTo = addressedTo,
            BirthDate = new DateOnly(1990, 5, 14),
            TransactionTypeId = DemoIds.Educational,
            SubTransactionTypeId = DemoIds.Bachelor,
            VerificationAuthorityId = DemoIds.SupremeCouncil,
        };

        application.Names.Add(new ApplicationName
        {
            LanguageType = NameLanguageType.Arabic,
            FirstName = "ليلى",
            LastName = "حسن",
        });

        application.Names.Add(new ApplicationName
        {
            LanguageType = NameLanguageType.English,
            FirstName = "Layla",
            LastName = "Hassan",
        });

        var line = new ApplicationService
        {
            ServiceTypeId = service.Id,
            Quantity = 1,
            LanguageCode = "en",
            IsExpress = false,
        };

        line.PriceFrom(service, price);
        application.Services.Add(line);
        application.RecalculateTotal();

        WalkTo(application, target, applicant, reviewer);
        return application;
    }

    /// <summary>
    /// Steps the application along the real state machine until it reaches <paramref name="target"/>,
    /// so its history reads like something that actually happened rather than a status dropped in.
    /// </summary>
    private static void WalkTo(
        VerificationApplication application,
        ApplicationStatus target,
        Actor applicant,
        Actor reviewer)
    {
        if (target == ApplicationStatus.Draft)
        {
            return;
        }

        application.TransitionTo(ApplicationStatus.PendingPayment, applicant);
        if (target == ApplicationStatus.PendingPayment)
        {
            return;
        }

        application.PaidAtUtc = DateTime.UtcNow.AddDays(-3);
        application.TransitionTo(ApplicationStatus.Pending, applicant, "Paid from wallet");
        if (target == ApplicationStatus.Pending)
        {
            return;
        }

        application.TransitionTo(ApplicationStatus.InProgress, reviewer, "Verification started");
        if (target == ApplicationStatus.InProgress)
        {
            return;
        }

        if (target == ApplicationStatus.MissedInfo)
        {
            application.TransitionTo(
                ApplicationStatus.MissedInfo,
                reviewer,
                "A clearer scan of the certificate is needed.");
            return;
        }

        application.TransitionTo(
            target,
            reviewer,
            target == ApplicationStatus.Success
                ? "Verified with the issuing authority."
                : "The authority could not confirm this document.");
    }

    /// <summary>Mirrors the fixed identifiers <see cref="DataSeeder"/> writes.</summary>
    private static class DemoIds
    {
        public static readonly Guid Egypt = new("22222222-0000-0000-0000-000000000001");
        public static readonly Guid Egp = new("22222222-0000-0000-0000-000000000002");
        public static readonly Guid Educational = new("44444444-0000-0000-0000-000000000001");
        public static readonly Guid Bachelor = new("55555555-0000-0000-0000-000000000001");
        public static readonly Guid SupremeCouncil = new("66666666-0000-0000-0000-000000000001");
        public static readonly Guid StandardService = new("77777777-0000-0000-0000-000000000001");
        public static readonly Guid SuperAdminUser = new("99999999-0000-0000-0000-000000000003");
    }
}
