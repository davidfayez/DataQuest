using System.Reflection;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.Storage;

namespace DataVerification.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for the platform. Entity configuration lives in
/// <c>Persistence/Configurations</c> (Fluent API only — no data annotations on domain types),
/// which keeps the Domain project free of any persistence concern.
/// </summary>
public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Addressee> Addressees => Set<Addressee>();
    public DbSet<CountryCurrency> CountryCurrencies => Set<CountryCurrency>();

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<WalletRequest> WalletRequests => Set<WalletRequest>();
    public DbSet<WalletRequestFile> WalletRequestFiles => Set<WalletRequestFile>();

    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<PaymentMethodType> PaymentMethodTypes => Set<PaymentMethodType>();
    public DbSet<PaymentMethodAccount> PaymentMethodAccounts => Set<PaymentMethodAccount>();
    public DbSet<PaymentMethodCountry> PaymentMethodCountries => Set<PaymentMethodCountry>();
    public DbSet<PaymentMethodCurrency> PaymentMethodCurrencies => Set<PaymentMethodCurrency>();
    public DbSet<Bank> Banks => Set<Bank>();
    public DbSet<PaymentMethodNotificationEmail> PaymentMethodNotificationEmails =>
        Set<PaymentMethodNotificationEmail>();

    public DbSet<PaymentMethodIntegration> PaymentMethodIntegrations =>
        Set<PaymentMethodIntegration>();

    public DbSet<PaymentGatewayIntegration> PaymentGatewayIntegrations =>
        Set<PaymentGatewayIntegration>();

    public DbSet<TransactionType> TransactionTypes => Set<TransactionType>();
    public DbSet<TransactionTypeCountry> TransactionTypeCountries => Set<TransactionTypeCountry>();
    public DbSet<SubTransactionType> SubTransactionTypes => Set<SubTransactionType>();
    public DbSet<SubTransactionTypeCountry> SubTransactionTypeCountries => Set<SubTransactionTypeCountry>();
    public DbSet<VerificationAuthority> VerificationAuthorities => Set<VerificationAuthority>();
    public DbSet<AuthoritySubTransactionType> AuthoritySubTransactionTypes =>
        Set<AuthoritySubTransactionType>();
    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();
    public DbSet<ServiceTypeCost> ServiceTypeCosts => Set<ServiceTypeCost>();
    public DbSet<ServiceTypeLanguage> ServiceTypeLanguages => Set<ServiceTypeLanguage>();
    public DbSet<ServiceTypeRequiredFile> ServiceTypeRequiredFiles => Set<ServiceTypeRequiredFile>();
    public DbSet<RequiredFileField> RequiredFileFields => Set<RequiredFileField>();
    public DbSet<RequiredFileAllowedType> RequiredFileAllowedTypes => Set<RequiredFileAllowedType>();
    public DbSet<RequiredFileSample> RequiredFileSamples => Set<RequiredFileSample>();
    public DbSet<WalletRequestDocumentValue> WalletRequestDocumentValues => Set<WalletRequestDocumentValue>();
    public DbSet<RequiredFileFieldOption> RequiredFileFieldOptions => Set<RequiredFileFieldOption>();
    public DbSet<ApplicationDocumentValue> ApplicationDocumentValues => Set<ApplicationDocumentValue>();

    public DbSet<VerificationApplication> Applications => Set<VerificationApplication>();
    public DbSet<ApplicationName> ApplicationNames => Set<ApplicationName>();
    public DbSet<ApplicationService> ApplicationServices => Set<ApplicationService>();
    public DbSet<ApplicationFile> ApplicationFiles => Set<ApplicationFile>();
    public DbSet<ApplicationDocument> ApplicationDocuments => Set<ApplicationDocument>();
    public DbSet<ApplicationDocumentField> ApplicationDocumentFields =>
        Set<ApplicationDocumentField>();
    public DbSet<ApplicationDocumentFieldOption> ApplicationDocumentFieldOptions =>
        Set<ApplicationDocumentFieldOption>();
    public DbSet<ApplicationComment> ApplicationComments => Set<ApplicationComment>();
    public DbSet<ApplicationStatusHistory> ApplicationStatusHistory => Set<ApplicationStatusHistory>();

    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AdminUserRole> AdminUserRoles => Set<AdminUserRole>();
    public DbSet<AdminUserPermission> AdminUserPermissions => Set<AdminUserPermission>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    public DbSet<LandingFeature> LandingFeatures => Set<LandingFeature>();
    public DbSet<LandingFeatureTranslation> LandingFeatureTranslations => Set<LandingFeatureTranslation>();

    public DbSet<LandingStat> LandingStats => Set<LandingStat>();

    public DbSet<LandingStatTranslation> LandingStatTranslations => Set<LandingStatTranslation>();

    public DbSet<LandingTrustEntry> LandingTrustEntries => Set<LandingTrustEntry>();

    public DbSet<LandingTrustEntryTranslation> LandingTrustEntryTranslations => Set<LandingTrustEntryTranslation>();

    public DbSet<LandingStep> LandingSteps => Set<LandingStep>();

    public DbSet<LandingStepTranslation> LandingStepTranslations => Set<LandingStepTranslation>();

    public DbSet<FooterLink> FooterLinks => Set<FooterLink>();

    public DbSet<FooterLinkTranslation> FooterLinkTranslations => Set<FooterLinkTranslation>();

    public DbSet<HeaderLink> HeaderLinks => Set<HeaderLink>();

    public DbSet<HeaderLinkTranslation> HeaderLinkTranslations => Set<HeaderLinkTranslation>();

    public DbSet<FooterLogo> FooterLogos => Set<FooterLogo>();

    public DbSet<CoverageEntry> CoverageEntries => Set<CoverageEntry>();

    public DbSet<PasswordResetRequest> PasswordResetRequests => Set<PasswordResetRequest>();

    public DbSet<FooterLogoTranslation> FooterLogoTranslations => Set<FooterLogoTranslation>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();
    public DbSet<EmailTypeSetting> EmailTypeSettings => Set<EmailTypeSetting>();
    public DbSet<EmailBccRecipient> EmailBccRecipients => Set<EmailBccRecipient>();
    public DbSet<ToolResource> ToolResources => Set<ToolResource>();
    public DbSet<ContactDirectoryEntry> ContactDirectoryEntries => Set<ContactDirectoryEntry>();

    public DbSet<SocialLink> SocialLinks => Set<SocialLink>();

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketCategory> TicketCategories => Set<TicketCategory>();
    public DbSet<TicketAction> TicketActions => Set<TicketAction>();
    public DbSet<TicketActionDocument> TicketActionDocuments => Set<TicketActionDocument>();
    public DbSet<TicketFile> TicketFiles => Set<TicketFile>();
    public DbSet<ToolResourceTranslation> ToolResourceTranslations => Set<ToolResourceTranslation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // datetime2 carries no zone, so EF materialises every timestamp as Unspecified — which
        // then travels as an unlabelled instant and gets read as local time by anything that
        // consumes it. Everything stored here is UTC, so reads are labelled as such. The write
        // side is deliberately left alone: shifting on the way in would move real data, and there
        // is nothing to shift, because nothing writes local time.
        var utcOnRead = new ValueConverter<DateTime, DateTime>(
            written => written,
            read => DateTime.SpecifyKind(read, DateTimeKind.Utc));

        var nullableUtcOnRead = new ValueConverter<DateTime?, DateTime?>(
            written => written,
            read => read.HasValue ? DateTime.SpecifyKind(read.Value, DateTimeKind.Utc) : read);

        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties()))
        {
            if (property.ClrType == typeof(DateTime))
            {
                property.SetValueConverter(utcOnRead);
            }
            else if (property.ClrType == typeof(DateTime?))
            {
                property.SetValueConverter(nullableUtcOnRead);
            }
        }

        // Money is decimal(18,2) everywhere; setting it centrally avoids silent truncation
        // if a new monetary column is added without an explicit configuration.
        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetColumnType("decimal(18,2)");
        }
    }

    /// <summary>Stamps <c>UpdatedAtUtc</c> so callers never have to remember to.</summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var strategy = Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async ct =>
        {
            // A retry replays the whole unit, so anything the previous attempt tracked has to go —
            // otherwise stale entities would be written on the second pass.
            ChangeTracker.Clear();

            await using var transaction = await Database.BeginTransactionAsync(ct);
            var result = await operation(ct);
            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);
    }
}
