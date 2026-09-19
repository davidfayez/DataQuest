using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataVerification.Infrastructure.Persistence.Configurations;

public sealed class ClientConfiguration : EntityConfigurationBase<Client>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Client> builder)
    {
        builder.ToTable("Clients");
        builder.Property(c => c.Code).IsRequired().HasMaxLength(20);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.HasIndex(c => c.Code).IsUnique();

        // Filtered, so it constrains only the rows that carry the flag: any number of clients may
        // have it false, but two cannot have it true. This is the guarantee itself rather than a
        // convenience — the handler clears the previous holder, and this is what holds if anything
        // else ever writes the column.
        builder.HasIndex(c => c.IsLocalOrder)
            .IsUnique()
            .HasFilter("[IsLocalOrder] = 1");
    }
}

public sealed class CountryConfiguration : LocalizedLookupConfigurationBase<Country>
{
    protected override void ConfigureLookup(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("Countries");
        builder.Property(c => c.Code).IsRequired().HasMaxLength(2).IsFixedLength();
        builder.Property(c => c.PhoneCode).IsRequired().HasMaxLength(8).HasDefaultValue(string.Empty);
        builder.HasIndex(c => c.Code).IsUnique();
    }
}

public sealed class CurrencyConfiguration : LocalizedLookupConfigurationBase<Currency>
{
    protected override void ConfigureLookup(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("Currencies");
        builder.Property(c => c.Code).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(c => c.Symbol).IsRequired().HasMaxLength(10);
        builder.HasIndex(c => c.Code).IsUnique();
    }
}

/// <summary>The bodies an application can be addressed to, offered to applicants as a list.</summary>
public sealed class AddresseeConfiguration : LocalizedLookupConfigurationBase<Addressee>
{
    protected override void ConfigureLookup(EntityTypeBuilder<Addressee> builder)
    {
        builder.ToTable("Addressees");

        // The list is read in this order on every new application, and it is the only way it is
        // ever read.
        builder.HasIndex(a => new { a.SortOrder, a.NameEn });
    }
}

public sealed class CountryCurrencyConfiguration : EntityConfigurationBase<CountryCurrency>
{
    protected override void ConfigureEntity(EntityTypeBuilder<CountryCurrency> builder)
    {
        builder.ToTable("CountryCurrencies");

        builder.HasOne(cc => cc.Country)
            .WithMany(c => c.CountryCurrencies)
            .HasForeignKey(cc => cc.CountryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cc => cc.Currency)
            .WithMany(c => c.CountryCurrencies)
            .HasForeignKey(cc => cc.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        // A currency may appear at most once per country.
        builder.HasIndex(cc => new { cc.CountryId, cc.CurrencyId }).IsUnique();

        // And a country has at most one main currency.
        builder.Property(cc => cc.IsDefault).HasDefaultValue(false);
        builder.HasIndex(cc => cc.CountryId, "IX_CountryCurrencies_MainPerCountry")
            .IsUnique()
            .HasFilter("[IsDefault] = 1");
    }
}

public sealed class TransactionTypeConfiguration : LocalizedLookupConfigurationBase<TransactionType>
{
    protected override void ConfigureLookup(EntityTypeBuilder<TransactionType> builder)
    {
        builder.ToTable("TransactionTypes");
        builder.Property(t => t.Code).IsRequired().HasMaxLength(LookupCode.MaxLength);
        // Unique within the kind. Codes are stored upper-case, and the default collation compares
        // case-insensitively as well, so "edu" and "EDU" can never both exist.
        builder.HasIndex(t => t.Code).IsUnique();
        builder.Property(t => t.DescriptionAr).HasMaxLength(2000);
        builder.Property(t => t.DescriptionEn).HasMaxLength(2000);
        builder.HasIndex(t => t.IsActive);
    }
}

public sealed class TransactionTypeCountryConfiguration
    : EntityConfigurationBase<TransactionTypeCountry>
{
    protected override void ConfigureEntity(EntityTypeBuilder<TransactionTypeCountry> builder)
    {
        builder.ToTable("TransactionTypeCountries");

        builder.HasOne(link => link.TransactionType)
            .WithMany(type => type.CountryLinks)
            .HasForeignKey(link => link.TransactionTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(link => link.Country)
            .WithMany(country => country.TransactionTypeLinks)
            .HasForeignKey(link => link.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(link => new { link.TransactionTypeId, link.CountryId }).IsUnique();
        builder.HasIndex(link => new { link.CountryId, link.TransactionTypeId });
    }
}

public sealed class SubTransactionTypeConfiguration : LocalizedLookupConfigurationBase<SubTransactionType>
{
    protected override void ConfigureLookup(EntityTypeBuilder<SubTransactionType> builder)
    {
        builder.ToTable("SubTransactionTypes");
        builder.Property(s => s.Code).IsRequired().HasMaxLength(LookupCode.MaxLength);
        // Unique within the kind. Codes are stored upper-case, and the default collation compares
        // case-insensitively as well, so "edu" and "EDU" can never both exist.
        builder.HasIndex(s => s.Code).IsUnique();
        builder.Property(s => s.DescriptionAr).HasMaxLength(2000);
        builder.Property(s => s.DescriptionEn).HasMaxLength(2000);

        builder.HasOne(s => s.TransactionType)
            .WithMany(t => t.SubTransactionTypes)
            .HasForeignKey(s => s.TransactionTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.TransactionTypeId, s.IsActive });
    }
}

public sealed class SubTransactionTypeCountryConfiguration
    : EntityConfigurationBase<SubTransactionTypeCountry>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SubTransactionTypeCountry> builder)
    {
        builder.ToTable("SubTransactionTypeCountries");

        builder.HasOne(link => link.SubTransactionType)
            .WithMany(type => type.CountryLinks)
            .HasForeignKey(link => link.SubTransactionTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, matching the transaction-type link: a country in use must not vanish from
        // under a sub-type because someone deleted it from the catalogue.
        builder.HasOne(link => link.Country)
            .WithMany()
            .HasForeignKey(link => link.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(link => new { link.SubTransactionTypeId, link.CountryId }).IsUnique();
        builder.HasIndex(link => new { link.CountryId, link.SubTransactionTypeId });
    }
}

public sealed class VerificationAuthorityConfiguration
    : LocalizedLookupConfigurationBase<VerificationAuthority>
{
    protected override void ConfigureLookup(EntityTypeBuilder<VerificationAuthority> builder)
    {
        builder.ToTable("VerificationAuthorities");
        builder.Property(a => a.Code).IsRequired().HasMaxLength(LookupCode.MaxLength);
        // Unique within the kind. Codes are stored upper-case, and the default collation compares
        // case-insensitively as well, so "edu" and "EDU" can never both exist.
        builder.HasIndex(a => a.Code).IsUnique();
        builder.Property(a => a.DescriptionAr).HasMaxLength(2000);
        builder.Property(a => a.DescriptionEn).HasMaxLength(2000);

        builder.HasOne(a => a.Country)
            .WithMany(c => c.VerificationAuthorities)
            .HasForeignKey(a => a.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.CountryId, a.IsActive });
    }
}

public sealed class AuthoritySubTransactionTypeConfiguration
    : EntityConfigurationBase<AuthoritySubTransactionType>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AuthoritySubTransactionType> builder)
    {
        builder.ToTable("AuthoritySubTransactionTypes");

        builder.HasOne(a => a.VerificationAuthority)
            .WithMany(v => v.SubTransactionTypeLinks)
            .HasForeignKey(a => a.VerificationAuthorityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.SubTransactionType)
            .WithMany(s => s.AuthorityLinks)
            .HasForeignKey(a => a.SubTransactionTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.SubTransactionTypeId, a.VerificationAuthorityId }).IsUnique();
    }
}

public sealed class ServiceTypeConfiguration : LocalizedLookupConfigurationBase<ServiceType>
{
    protected override void ConfigureLookup(EntityTypeBuilder<ServiceType> builder)
    {
        builder.ToTable("ServiceTypes");
        builder.Property(s => s.Code).IsRequired().HasMaxLength(LookupCode.MaxLength);
        // Unique within the kind. Codes are stored upper-case, and the default collation compares
        // case-insensitively as well, so "edu" and "EDU" can never both exist.
        builder.HasIndex(s => s.Code).IsUnique();
        builder.Property(s => s.DescriptionAr).HasMaxLength(2000);
        builder.Property(s => s.DescriptionEn).HasMaxLength(2000);
        builder.Property(s => s.ExpressNoteAr).HasMaxLength(500);
        builder.Property(s => s.ExpressNoteEn).HasMaxLength(500);
        builder.Property(s => s.Cost).HasPrecision(18, 2);
        builder.Property(s => s.ExpressCost).HasPrecision(18, 2);
        builder.Property(s => s.ShowOnLanding).HasDefaultValue(true);

        builder.HasOne(s => s.VerificationAuthority)
            .WithMany(a => a.ServiceTypes)
            .HasForeignKey(s => s.VerificationAuthorityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.SubTransactionType)
            .WithMany()
            .HasForeignKey(s => s.SubTransactionTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.VerificationAuthorityId, s.IsActive });
        builder.HasIndex(s => new { s.SubTransactionTypeId, s.VerificationAuthorityId, s.IsActive });
    }
}

public sealed class ServiceTypeLanguageConfiguration : EntityConfigurationBase<ServiceTypeLanguage>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ServiceTypeLanguage> builder)
    {
        builder.ToTable("ServiceTypeLanguages");
        builder.Property(l => l.LanguageCode).IsRequired().HasMaxLength(10);

        builder.HasOne(l => l.ServiceType)
            .WithMany(s => s.OutputLanguages)
            .HasForeignKey(l => l.ServiceTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => new { l.ServiceTypeId, l.LanguageCode }).IsUnique();
    }
}

public sealed class ServiceTypeCostConfiguration : EntityConfigurationBase<ServiceTypeCost>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ServiceTypeCost> builder)
    {
        builder.ToTable("ServiceTypeCosts");

        // Every price saved before this existed was on sale, so it starts active.
        builder.Property(c => c.IsActive).HasDefaultValue(true);
        builder.Property(c => c.Cost).HasPrecision(18, 2);
        builder.Property(c => c.ExpressCost).HasPrecision(18, 2);

        builder.HasOne(c => c.ServiceType)
            .WithMany(s => s.Costs)
            .HasForeignKey(c => c.ServiceTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Currency)
            .WithMany()
            .HasForeignKey(c => c.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.ServiceTypeId, c.CurrencyId }).IsUnique();
    }
}

public sealed class ServiceTypeRequiredFileConfiguration
    : LocalizedLookupConfigurationBase<ServiceTypeRequiredFile>
{
    protected override void ConfigureLookup(EntityTypeBuilder<ServiceTypeRequiredFile> builder)
    {
        // One owner, never both and never neither: a document belongs to a service type or to a
        // payment method.
        builder.ToTable("ServiceTypeRequiredFiles", table => table.HasCheckConstraint(
            "CK_ServiceTypeRequiredFiles_SingleOwner",
            "([ServiceTypeId] IS NOT NULL AND [PaymentMethodId] IS NULL) "
            + "OR ([ServiceTypeId] IS NULL AND [PaymentMethodId] IS NOT NULL)"));

        builder.HasOne(f => f.ServiceType)
            .WithMany(s => s.RequiredFiles)
            .HasForeignKey(f => f.ServiceTypeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.PaymentMethod)
            .WithMany(m => m.RequiredFiles)
            .HasForeignKey(f => f.PaymentMethodId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => f.ServiceTypeId);
        builder.HasIndex(f => f.PaymentMethodId);
    }
}

/// <summary>The reference files an administrator attaches to one required document.</summary>
public sealed class RequiredFileSampleConfiguration : EntityConfigurationBase<RequiredFileSample>
{
    protected override void ConfigureEntity(EntityTypeBuilder<RequiredFileSample> builder)
    {
        builder.ToTable("RequiredFileSamples");

        builder.Property(s => s.LabelAr).IsRequired().HasMaxLength(RequiredFileSampleLimits.MaxLabelLength);
        builder.Property(s => s.LabelEn).IsRequired().HasMaxLength(RequiredFileSampleLimits.MaxLabelLength);
        builder.Property(s => s.FileName).IsRequired().HasMaxLength(260);
        builder.Property(s => s.StoragePath).IsRequired().HasMaxLength(500);
        builder.Property(s => s.ContentType).IsRequired().HasMaxLength(200);

        // The row goes with its document; the bytes are removed by the command that removes it.
        builder.HasOne(s => s.RequiredFile)
            .WithMany(f => f.Samples)
            .HasForeignKey(s => s.RequiredFileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.RequiredFileId, s.SortOrder });
    }
}

/// <summary>The upload formats one required document accepts.</summary>
public sealed class RequiredFileAllowedTypeConfiguration
    : EntityConfigurationBase<RequiredFileAllowedType>
{
    protected override void ConfigureEntity(EntityTypeBuilder<RequiredFileAllowedType> builder)
    {
        builder.ToTable("RequiredFileAllowedTypes");

        builder.Property(t => t.FileTypeCode).IsRequired().HasMaxLength(20);

        builder.HasOne(t => t.RequiredFile)
            .WithMany(f => f.AllowedFileTypes)
            .HasForeignKey(t => t.RequiredFileId)
            .OnDelete(DeleteBehavior.Cascade);

        // One row per format per document: the set is a set.
        builder.HasIndex(t => new { t.RequiredFileId, t.FileTypeCode }).IsUnique();
    }
}

public sealed class RequiredFileFieldConfiguration : LocalizedLookupConfigurationBase<RequiredFileField>
{
    protected override void ConfigureLookup(EntityTypeBuilder<RequiredFileField> builder)
    {
        builder.ToTable("RequiredFileFields");

        builder.Property(f => f.FieldType).HasConversion<int>();
        builder.Property(f => f.DateRule).HasConversion<int>();
        builder.Property(f => f.Pattern).HasMaxLength(400);
        builder.Property(f => f.MinValue).HasPrecision(18, 4);
        builder.Property(f => f.MaxValue).HasPrecision(18, 4);
        builder.Property(f => f.MinDate).HasColumnType("date");
        builder.Property(f => f.MaxDate).HasColumnType("date");

        builder.HasOne(f => f.RequiredFile)
            .WithMany(r => r.Fields)
            .HasForeignKey(f => f.RequiredFileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => new { f.RequiredFileId, f.SortOrder });
    }
}

public sealed class RequiredFileFieldOptionConfiguration : EntityConfigurationBase<RequiredFileFieldOption>
{
    protected override void ConfigureEntity(EntityTypeBuilder<RequiredFileFieldOption> builder)
    {
        builder.ToTable("RequiredFileFieldOptions");

        builder.Property(o => o.Value).IsRequired().HasMaxLength(200);
        builder.Property(o => o.LabelAr).IsRequired().HasMaxLength(200);
        builder.Property(o => o.LabelEn).IsRequired().HasMaxLength(200);

        builder.HasOne(o => o.Field)
            .WithMany(f => f.Options)
            .HasForeignKey(o => o.RequiredFileFieldId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(o => new { o.RequiredFileFieldId, o.SortOrder });
    }
}
