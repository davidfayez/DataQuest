using DataVerification.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataVerification.Infrastructure.Persistence.Configurations;

public sealed class PaymentMethodTypeConfiguration : LocalizedLookupConfigurationBase<PaymentMethodType>
{
    protected override void ConfigureLookup(EntityTypeBuilder<PaymentMethodType> builder)
    {
        builder.ToTable("PaymentMethodTypes");
        builder.Property(t => t.Kind).HasConversion<int>();

        // Nullable so the types that predate descriptions stay valid; the admin command requires
        // both on every save.
        builder.Property(t => t.DescriptionAr).HasMaxLength(2000);
        builder.Property(t => t.DescriptionEn).HasMaxLength(2000);

// Columns now, not computed: what a provider needs is configuration, not a consequence
        // of its kind.
        builder.Property(t => t.RequiresAccountNumber).HasDefaultValue(false);
        builder.Property(t => t.RequiresBarcode).HasDefaultValue(false);
        builder.Property(t => t.RequiresBank).HasDefaultValue(false);
        builder.Property(t => t.RequiresExternalUrl).HasDefaultValue(false);

        // Read from the four above, so it is not a column of its own.
        builder.Ignore(t => t.UsesProviderCredentials);

        builder.HasIndex(t => new { t.Kind, t.IsActive });
    }
}

public sealed class PaymentMethodConfiguration : LocalizedLookupConfigurationBase<PaymentMethod>
{
    protected override void ConfigureLookup(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("PaymentMethods");

        builder.Property(m => m.DescriptionAr).HasMaxLength(2000);
        builder.Property(m => m.DescriptionEn).HasMaxLength(2000);
        builder.Property(m => m.PublicNoteAr).HasMaxLength(2000);
        builder.Property(m => m.PublicNoteEn).HasMaxLength(2000);
        builder.Property(m => m.PrivateNoteAr).HasMaxLength(2000);
        builder.Property(m => m.PrivateNoteEn).HasMaxLength(2000);
        builder.Property(m => m.ExternalUrl).HasMaxLength(2000);
        builder.Property(m => m.GatewaySettingsJson).IsRequired();
        builder.Property(m => m.GatewaySecretsJson).IsRequired();

        // Restrict, not Cascade: retiring a payment type must not silently take every method
        // configured against it — the type is deactivated instead.
        builder.HasOne(m => m.Type)
            .WithMany(t => t.PaymentMethods)
            .HasForeignKey(m => m.PaymentMethodTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.PaymentMethodTypeId, m.IsActive });
        builder.HasIndex(m => m.SortOrder);

        // Restrict: an integration still in use is deactivated, never removed from under a method.
        builder.HasOne(m => m.GatewayIntegration)
            .WithMany(i => i.PaymentMethods)
            .HasForeignKey(m => m.GatewayIntegrationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PaymentGatewayIntegrationConfiguration
    : LocalizedLookupConfigurationBase<PaymentGatewayIntegration>
{
    protected override void ConfigureLookup(EntityTypeBuilder<PaymentGatewayIntegration> builder)
    {
        builder.ToTable("PaymentGatewayIntegrations");

        builder.Property(i => i.DescriptionAr).HasMaxLength(2000);
        builder.Property(i => i.DescriptionEn).HasMaxLength(2000);
        builder.Property(i => i.GatewayCode).HasMaxLength(60).IsRequired();
        builder.Property(i => i.Mode).HasConversion<int>();

        builder.HasIndex(i => new { i.GatewayCode, i.IsActive });
    }
}

public sealed class PaymentMethodAccountConfiguration : EntityConfigurationBase<PaymentMethodAccount>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PaymentMethodAccount> builder)
    {
        builder.ToTable("PaymentMethodAccounts");

        builder.Property(a => a.LabelAr).IsRequired().HasMaxLength(200).HasDefaultValue(string.Empty);
        builder.Property(a => a.LabelEn).IsRequired().HasMaxLength(200).HasDefaultValue(string.Empty);
        builder.Property(a => a.AccountNumber).IsRequired().HasMaxLength(120);
        builder.Property(a => a.AccountHolder).HasMaxLength(200);
        builder.Property(a => a.BarcodeStoragePath).HasMaxLength(500);
        builder.Property(a => a.BarcodeContentType).HasMaxLength(120);
        builder.Property(a => a.BarcodeFileName).HasMaxLength(260);
        builder.Property(a => a.IsActive).HasDefaultValue(true);

        builder.HasOne(a => a.PaymentMethod)
            .WithMany(m => m.Accounts)
            .HasForeignKey(a => a.PaymentMethodId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: a bank still named by a receiving account must not vanish from the catalogue.
        builder.HasOne(a => a.Bank)
            .WithMany()
            .HasForeignKey(a => a.BankId)
            .OnDelete(DeleteBehavior.Restrict);

        // The same number twice on one method is always a mistake, and would make the applicant's
        // choice of "which account did I pay" ambiguous.
        builder.HasIndex(a => new { a.PaymentMethodId, a.AccountNumber }).IsUnique();
        builder.HasIndex(a => new { a.PaymentMethodId, a.SortOrder });
    }
}

public sealed class PaymentMethodCountryConfiguration : EntityConfigurationBase<PaymentMethodCountry>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PaymentMethodCountry> builder)
    {
        builder.ToTable("PaymentMethodCountries");

        builder.HasOne(link => link.PaymentMethod)
            .WithMany(method => method.CountryLinks)
            .HasForeignKey(link => link.PaymentMethodId)
            .OnDelete(DeleteBehavior.Cascade);

        // Matching the rest of the cascade: a country in use must not vanish from under a method.
        builder.HasOne(link => link.Country)
            .WithMany()
            .HasForeignKey(link => link.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(link => new { link.PaymentMethodId, link.CountryId }).IsUnique();
        builder.HasIndex(link => link.CountryId);
    }
}

public sealed class PaymentMethodCurrencyConfiguration : EntityConfigurationBase<PaymentMethodCurrency>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PaymentMethodCurrency> builder)
    {
        builder.ToTable("PaymentMethodCurrencies");

        builder.HasOne(link => link.PaymentMethod)
            .WithMany(method => method.CurrencyLinks)
            .HasForeignKey(link => link.PaymentMethodId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(link => link.Currency)
            .WithMany()
            .HasForeignKey(link => link.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(link => new { link.PaymentMethodId, link.CurrencyId }).IsUnique();
        builder.HasIndex(link => link.CurrencyId);
    }
}

public sealed class WalletRequestFileConfiguration : EntityConfigurationBase<WalletRequestFile>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WalletRequestFile> builder)
    {
        builder.ToTable("WalletRequestFiles");

        builder.Property(f => f.FileName).IsRequired().HasMaxLength(260);
        builder.Property(f => f.ContentType).IsRequired().HasMaxLength(120);
        builder.Property(f => f.StoragePath).IsRequired().HasMaxLength(500);
        builder.Property(f => f.UploadedByName).HasMaxLength(200);

        // Copied, not referenced: the request has to keep reading correctly after the document is
        // renamed or removed.
        builder.Property(f => f.DocumentNameAr).HasMaxLength(200);
        builder.Property(f => f.DocumentNameEn).HasMaxLength(200);

        builder.HasOne(f => f.WalletRequest)
            .WithMany(r => r.Files)
            .HasForeignKey(f => f.WalletRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => f.WalletRequestId);
    }
}

public sealed class WalletRequestDocumentValueConfiguration : EntityConfigurationBase<WalletRequestDocumentValue>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WalletRequestDocumentValue> builder)
    {
        builder.ToTable("WalletRequestDocumentValues");

        builder.Property(v => v.DocumentNameAr).IsRequired().HasMaxLength(200);
        builder.Property(v => v.DocumentNameEn).IsRequired().HasMaxLength(200);
        builder.Property(v => v.FieldNameAr).IsRequired().HasMaxLength(200);
        builder.Property(v => v.FieldNameEn).IsRequired().HasMaxLength(200);
        builder.Property(v => v.Value).IsRequired().HasMaxLength(1000);
        builder.Property(v => v.ValueLabelAr).HasMaxLength(200);
        builder.Property(v => v.ValueLabelEn).HasMaxLength(200);

        builder.HasOne(v => v.WalletRequest)
            .WithMany(r => r.DocumentValues)
            .HasForeignKey(v => v.WalletRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(v => v.WalletRequestId);
        builder.HasIndex(v => v.RequiredFileId);
    }
}

public sealed class BankConfiguration : LocalizedLookupConfigurationBase<Bank>
{
    protected override void ConfigureLookup(EntityTypeBuilder<Bank> builder)
    {
        builder.ToTable("Banks");

        builder.Property(b => b.SwiftCode).HasMaxLength(11);

        builder.HasOne(b => b.Country)
            .WithMany()
            .HasForeignKey(b => b.CountryId)
            .OnDelete(DeleteBehavior.Cascade);

        // One bank name per country; the same name in two countries is a different institution.
        builder.HasIndex(b => new { b.CountryId, b.NameEn }).IsUnique();
        builder.HasIndex(b => new { b.CountryId, b.SortOrder });
    }
}

public sealed class PaymentMethodNotificationEmailConfiguration
    : EntityConfigurationBase<PaymentMethodNotificationEmail>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PaymentMethodNotificationEmail> builder)
    {
        builder.ToTable("PaymentMethodNotificationEmails");

        builder.Property(r => r.Email).IsRequired().HasMaxLength(256);
        builder.Property(r => r.DisplayName).HasMaxLength(200);
        builder.Property(r => r.NotifyOnSubmitted).HasDefaultValue(true);
        builder.Property(r => r.NotifyOnApproved).HasDefaultValue(true);
        builder.Property(r => r.NotifyOnRejected).HasDefaultValue(true);

        builder.HasOne(r => r.PaymentMethod)
            .WithMany(m => m.NotificationEmails)
            .HasForeignKey(r => r.PaymentMethodId)
            .OnDelete(DeleteBehavior.Cascade);

        // The same mailbox listed twice would send every notification twice.
        builder.HasIndex(r => new { r.PaymentMethodId, r.Email }).IsUnique();
    }
}

public sealed class PaymentMethodIntegrationConfiguration
    : EntityConfigurationBase<PaymentMethodIntegration>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PaymentMethodIntegration> builder)
    {
        builder.ToTable("PaymentMethodIntegrations");

        builder.Property(i => i.Provider).HasMaxLength(120);
        builder.Property(i => i.MerchantId).HasMaxLength(200);
        builder.Property(i => i.IntegrationId).HasMaxLength(200);
        builder.Property(i => i.Mode).HasConversion<int>();

        // Ciphertext is longer than the secret it protects, so these are generous.
        builder.Property(i => i.ApiKeySecret).HasMaxLength(2000);
        builder.Property(i => i.PasswordSecret).HasMaxLength(2000);
        builder.Property(i => i.WebhookSecret).HasMaxLength(2000);

        builder.Property(i => i.BaseUrl).HasMaxLength(2000);
        builder.Property(i => i.RedirectUrl).HasMaxLength(2000);
        builder.Property(i => i.CancelUrl).HasMaxLength(2000);
        builder.Property(i => i.CallbackUrl).HasMaxLength(2000);

        // All four are read from the columns above; none is stored.
        builder.Ignore(i => i.HasApiKey);
        builder.Ignore(i => i.HasPassword);
        builder.Ignore(i => i.HasWebhookSecret);
        builder.Ignore(i => i.IsUsable);
        builder.Ignore(i => i.CallbackIsUnverified);

        // One integration per method, and it dies with the method it belongs to — credentials
        // outliving the channel they were issued for would be a liability, not an asset.
        builder.HasOne(i => i.PaymentMethod)
            .WithOne(m => m.Integration)
            .HasForeignKey<PaymentMethodIntegration>(i => i.PaymentMethodId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.PaymentMethodId).IsUnique();
    }
}
