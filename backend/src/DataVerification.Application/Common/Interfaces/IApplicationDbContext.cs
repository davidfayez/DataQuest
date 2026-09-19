using DataVerification.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Common.Interfaces;

/// <summary>
/// The persistence surface the Application layer is allowed to see. Handlers depend on this rather
/// than on the EF context directly, which keeps CQRS testable and stops infrastructure concerns
/// leaking into feature code.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Client> Clients { get; }
    DbSet<Country> Countries { get; }
    DbSet<Currency> Currencies { get; }
    DbSet<Addressee> Addressees { get; }
    DbSet<CountryCurrency> CountryCurrencies { get; }

    DbSet<Order> Orders { get; }
    DbSet<Wallet> Wallets { get; }
    DbSet<WalletTransaction> WalletTransactions { get; }
    DbSet<WalletRequest> WalletRequests { get; }
    DbSet<WalletRequestFile> WalletRequestFiles { get; }

    DbSet<PaymentMethod> PaymentMethods { get; }
    DbSet<PaymentMethodType> PaymentMethodTypes { get; }
    DbSet<PaymentMethodAccount> PaymentMethodAccounts { get; }
    DbSet<PaymentMethodCountry> PaymentMethodCountries { get; }
    DbSet<PaymentMethodCurrency> PaymentMethodCurrencies { get; }
    DbSet<Bank> Banks { get; }
    DbSet<PaymentMethodNotificationEmail> PaymentMethodNotificationEmails { get; }
    DbSet<PaymentMethodIntegration> PaymentMethodIntegrations { get; }

    DbSet<PaymentGatewayIntegration> PaymentGatewayIntegrations { get; }

    DbSet<TransactionType> TransactionTypes { get; }
    DbSet<TransactionTypeCountry> TransactionTypeCountries { get; }
    DbSet<SubTransactionType> SubTransactionTypes { get; }
    DbSet<SubTransactionTypeCountry> SubTransactionTypeCountries { get; }
    DbSet<VerificationAuthority> VerificationAuthorities { get; }
    DbSet<AuthoritySubTransactionType> AuthoritySubTransactionTypes { get; }
    DbSet<ServiceType> ServiceTypes { get; }
    DbSet<ServiceTypeCost> ServiceTypeCosts { get; }
    DbSet<ServiceTypeLanguage> ServiceTypeLanguages { get; }
    DbSet<ServiceTypeRequiredFile> ServiceTypeRequiredFiles { get; }
    DbSet<RequiredFileField> RequiredFileFields { get; }
    DbSet<RequiredFileAllowedType> RequiredFileAllowedTypes { get; }

    DbSet<RequiredFileSample> RequiredFileSamples { get; }

    DbSet<WalletRequestDocumentValue> WalletRequestDocumentValues { get; }
    DbSet<RequiredFileFieldOption> RequiredFileFieldOptions { get; }
    DbSet<ApplicationDocumentValue> ApplicationDocumentValues { get; }

    DbSet<VerificationApplication> Applications { get; }
    DbSet<ApplicationName> ApplicationNames { get; }
    DbSet<ApplicationService> ApplicationServices { get; }
    DbSet<ApplicationFile> ApplicationFiles { get; }
    DbSet<ApplicationDocument> ApplicationDocuments { get; }
    DbSet<ApplicationDocumentField> ApplicationDocumentFields { get; }
    DbSet<ApplicationDocumentFieldOption> ApplicationDocumentFieldOptions { get; }
    DbSet<ApplicationComment> ApplicationComments { get; }
    DbSet<ApplicationStatusHistory> ApplicationStatusHistory { get; }

    DbSet<AdminUser> AdminUsers { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<AdminUserRole> AdminUserRoles { get; }
    DbSet<AdminUserPermission> AdminUserPermissions { get; }
    DbSet<AuditLogEntry> AuditLog { get; }

    DbSet<LandingFeature> LandingFeatures { get; }

    /// <summary>The figures in the landing page statistics strip.</summary>
    DbSet<LandingStat> LandingStats { get; }

    /// <summary>The bodies named in the landing "trusted for verification with" strip.</summary>
    DbSet<LandingTrustEntry> LandingTrustEntries { get; }

    /// <summary>The steps in the landing "how it works" section.</summary>
    DbSet<LandingStep> LandingSteps { get; }

    /// <summary>The rows in the site footer's link columns.</summary>
    DbSet<FooterLink> FooterLinks { get; }

    DbSet<HeaderLink> HeaderLinks { get; }

    /// <summary>The marks shown beside the footer's brand block.</summary>
    DbSet<FooterLogo> FooterLogos { get; }

    /// <summary>The countries marked on the landing page's coverage map.</summary>
    DbSet<CoverageEntry> CoverageEntries { get; }

    /// <summary>Every press of "forgot password", successful or not.</summary>
    DbSet<PasswordResetRequest> PasswordResetRequests { get; }
    DbSet<SiteSetting> SiteSettings { get; }
    DbSet<EmailTypeSetting> EmailTypeSettings { get; }
    DbSet<EmailBccRecipient> EmailBccRecipients { get; }
    DbSet<ToolResource> ToolResources { get; }
    DbSet<ContactDirectoryEntry> ContactDirectoryEntries { get; }

    /// <summary>The channels in the public footer, under "Follow us" and "Message us".</summary>
    DbSet<SocialLink> SocialLinks { get; }

    DbSet<Ticket> Tickets { get; }
    DbSet<TicketCategory> TicketCategories { get; }
    DbSet<TicketAction> TicketActions { get; }
    DbSet<TicketActionDocument> TicketActionDocuments { get; }
    DbSet<TicketFile> TicketFiles { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside a database transaction as a single retriable unit.
    /// Connection resiliency is enabled, and a retrying execution strategy refuses user-initiated
    /// transactions, so this is the only supported way to span several aggregates atomically.
    /// The operation must perform its own reads: it may be executed more than once, and the change
    /// tracker is reset before each attempt.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
