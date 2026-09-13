using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataVerification.Infrastructure.Persistence.Configurations;

/// <summary>Editable landing-page marketing cards, ordered and toggled from the admin panel.</summary>
public sealed class LandingFeatureConfiguration : EntityConfigurationBase<LandingFeature>
{
    protected override void ConfigureEntity(EntityTypeBuilder<LandingFeature> builder)
    {
        builder.ToTable("LandingFeatures");
        builder.Property(f => f.Icon).IsRequired().HasMaxLength(50);

        // The copy lives in per-language child rows; deleting a card removes its translations.
        builder.HasMany(f => f.Translations)
            .WithOne(t => t.LandingFeature!)
            .HasForeignKey(t => t.LandingFeatureId)
            .OnDelete(DeleteBehavior.Cascade);

        // The public read is always "published cards in display order".
        builder.HasIndex(f => new { f.IsPublished, f.SortOrder });
    }
}

/// <summary>One language's title/body for a landing card.</summary>
public sealed class LandingFeatureTranslationConfiguration : EntityConfigurationBase<LandingFeatureTranslation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<LandingFeatureTranslation> builder)
    {
        builder.ToTable("LandingFeatureTranslations");
        builder.Property(t => t.LanguageCode).IsRequired().HasMaxLength(10);
        builder.Property(t => t.Title).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Body).IsRequired().HasMaxLength(1000);

        // One translation per language per card.
        builder.HasIndex(t => new { t.LandingFeatureId, t.LanguageCode }).IsUnique();
    }
}

/// <summary>The figures in the landing page's statistics strip.</summary>
public sealed class LandingStatConfiguration : EntityConfigurationBase<LandingStat>
{
    protected override void ConfigureEntity(EntityTypeBuilder<LandingStat> builder)
    {
        builder.ToTable("LandingStats");
        builder.Property(s => s.Icon).HasMaxLength(50);

        builder.HasMany(s => s.Translations)
            .WithOne(t => t.LandingStat!)
            .HasForeignKey(t => t.LandingStatId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.IsPublished, s.SortOrder });
    }
}

/// <summary>One language's figure and caption for a landing statistic.</summary>
public sealed class LandingStatTranslationConfiguration : EntityConfigurationBase<LandingStatTranslation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<LandingStatTranslation> builder)
    {
        builder.ToTable("LandingStatTranslations");
        builder.Property(t => t.LanguageCode).IsRequired().HasMaxLength(10);
        builder.Property(t => t.Value).IsRequired().HasMaxLength(50);
        builder.Property(t => t.Label).IsRequired().HasMaxLength(200);

        builder.HasIndex(t => new { t.LandingStatId, t.LanguageCode }).IsUnique();
    }
}

/// <summary>The steps in the landing page's "how it works" section.</summary>
public sealed class LandingStepConfiguration : EntityConfigurationBase<LandingStep>
{
    protected override void ConfigureEntity(EntityTypeBuilder<LandingStep> builder)
    {
        builder.ToTable("LandingSteps");
        builder.Property(s => s.Icon).IsRequired().HasMaxLength(50);

        builder.HasMany(s => s.Translations)
            .WithOne(t => t.LandingStep!)
            .HasForeignKey(t => t.LandingStepId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.IsPublished, s.SortOrder });
    }
}

/// <summary>One language's title/body for a landing step.</summary>
public sealed class LandingStepTranslationConfiguration : EntityConfigurationBase<LandingStepTranslation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<LandingStepTranslation> builder)
    {
        builder.ToTable("LandingStepTranslations");
        builder.Property(t => t.LanguageCode).IsRequired().HasMaxLength(10);
        builder.Property(t => t.Title).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Body).IsRequired().HasMaxLength(1000);

        builder.HasIndex(t => new { t.LandingStepId, t.LanguageCode }).IsUnique();
    }
}

/// <summary>The bodies named in the landing page's "trusted for verification with" strip.</summary>
public sealed class LandingTrustEntryConfiguration : EntityConfigurationBase<LandingTrustEntry>
{
    protected override void ConfigureEntity(EntityTypeBuilder<LandingTrustEntry> builder)
    {
        builder.ToTable("LandingTrustEntries");
        builder.Property(e => e.Icon).HasMaxLength(50);

        builder.HasMany(e => e.Translations)
            .WithOne(t => t.LandingTrustEntry!)
            .HasForeignKey(t => t.LandingTrustEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.IsPublished, e.SortOrder });
    }
}

/// <summary>One language's name for a trust-strip entry.</summary>
public sealed class LandingTrustEntryTranslationConfiguration
    : EntityConfigurationBase<LandingTrustEntryTranslation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<LandingTrustEntryTranslation> builder)
    {
        builder.ToTable("LandingTrustEntryTranslations");
        builder.Property(t => t.LanguageCode).IsRequired().HasMaxLength(10);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);

        builder.HasIndex(t => new { t.LandingTrustEntryId, t.LanguageCode }).IsUnique();
    }
}

/// <summary>Video and image entries on the public "how to use the platform" page.</summary>
public sealed class ToolResourceConfiguration : EntityConfigurationBase<ToolResource>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ToolResource> builder)
    {
        builder.ToTable("ToolResources");

        // Both are optional at the row level: which one is required follows from Kind, and an
        // image entry exists briefly before its file has been uploaded.
        builder.Property(r => r.VideoUrl).HasMaxLength(2000);
        builder.Property(r => r.ImageStoragePath).HasMaxLength(400);
        builder.Property(r => r.ImageContentType).HasMaxLength(100);
        builder.Property(r => r.ImageFileName).HasMaxLength(260);

        builder.HasMany(r => r.Translations)
            .WithOne(t => t.ToolResource!)
            .HasForeignKey(t => t.ToolResourceId)
            .OnDelete(DeleteBehavior.Cascade);

        // The public read is always "published entries in display order".
        builder.HasIndex(r => new { r.IsPublished, r.SortOrder });

        builder.Ignore(r => r.HasImage);
        builder.Ignore(r => r.IsShowable);
    }
}

/// <summary>One language's name/description for a tool entry.</summary>
public sealed class ToolResourceTranslationConfiguration : EntityConfigurationBase<ToolResourceTranslation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ToolResourceTranslation> builder)
    {
        builder.ToTable("ToolResourceTranslations");
        builder.Property(t => t.LanguageCode).IsRequired().HasMaxLength(10);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).IsRequired().HasMaxLength(2000);

        builder.HasIndex(t => new { t.ToolResourceId, t.LanguageCode }).IsUnique();
    }
}

/// <summary>Generic key/value site copy (currently the landing section heading).</summary>
public sealed class SiteSettingConfiguration : EntityConfigurationBase<SiteSetting>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SiteSetting> builder)
    {
        builder.ToTable("SiteSettings");
        builder.Property(s => s.Key).IsRequired().HasMaxLength(100);
        builder.Property(s => s.Value).IsRequired().HasMaxLength(1000);
        builder.HasIndex(s => s.Key).IsUnique();
    }
}

public sealed class EmailTypeSettingConfiguration : EntityConfigurationBase<EmailTypeSetting>
{
    protected override void ConfigureEntity(EntityTypeBuilder<EmailTypeSetting> builder)
    {
        builder.ToTable("EmailTypeSettings");

        builder.Property(setting => setting.Type).HasConversion<int>();
        builder.Property(setting => setting.FromAddress).HasMaxLength(256);
        builder.Property(setting => setting.FromName).HasMaxLength(200);

        // One row per kind of email; a second would make "which sender wins" arbitrary.
        builder.HasIndex(setting => setting.Type).IsUnique();
    }
}

public sealed class EmailBccRecipientConfiguration : EntityConfigurationBase<EmailBccRecipient>
{
    protected override void ConfigureEntity(EntityTypeBuilder<EmailBccRecipient> builder)
    {
        builder.ToTable("EmailBccRecipients");

        builder.Property(recipient => recipient.Email).IsRequired().HasMaxLength(256);
        builder.Property(recipient => recipient.DisplayName).HasMaxLength(200);

        builder.HasOne(recipient => recipient.EmailTypeSetting)
            .WithMany(setting => setting.BccRecipients)
            .HasForeignKey(recipient => recipient.EmailTypeSettingId)
            .OnDelete(DeleteBehavior.Cascade);

        // The same mailbox twice would copy it twice on every send.
        builder.HasIndex(recipient => new { recipient.EmailTypeSettingId, recipient.Email })
            .IsUnique();
    }
}

public sealed class ContactDirectoryEntryConfiguration : EntityConfigurationBase<ContactDirectoryEntry>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ContactDirectoryEntry> builder)
    {
        builder.ToTable("ContactDirectoryEntries");

        builder.Property(e => e.Kind).HasConversion<int>();
        builder.Property(e => e.TitleAr).HasMaxLength(200);
        builder.Property(e => e.TitleEn).HasMaxLength(200);
        builder.Property(e => e.AddressAr).HasMaxLength(500);
        builder.Property(e => e.AddressEn).HasMaxLength(500);
        builder.Property(e => e.Phone).HasMaxLength(40);
        builder.Property(e => e.Email).HasMaxLength(256);
        builder.Property(e => e.IsActive).HasDefaultValue(true);

        // Read from the contact columns rather than stored, so the two cannot drift.
        builder.Ignore(e => e.IsReachable);

        // SetNull, not Cascade: retiring a country from the lookup must not silently delete the
        // agent listed in it — the entry survives, unattached, for someone to correct.
        builder.HasOne(e => e.Country)
            .WithMany()
            .HasForeignKey(e => e.CountryId)
            .OnDelete(DeleteBehavior.SetNull);

        // How the public page reads it: by group, then by the operator's own arrangement.
        builder.HasIndex(e => new { e.Kind, e.IsActive, e.SortOrder });
    }
}

public sealed class SocialLinkConfiguration : EntityConfigurationBase<SocialLink>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SocialLink> builder)
    {
        builder.ToTable("SocialLinks");

        builder.Property(e => e.Placement).HasConversion<int>();
        builder.Property(e => e.Platform).HasConversion<int>();
        builder.Property(e => e.Url).IsRequired().HasMaxLength(500);
        builder.Property(e => e.IsActive).HasDefaultValue(true);

        // Derived from the address, so it cannot drift from one.
        builder.Ignore(e => e.IsShowable);

        // How the footer reads it: one row at a time, in the operator's own arrangement.
        builder.HasIndex(e => new { e.Placement, e.IsActive, e.SortOrder });

        // The same platform may appear in both rows — Telegram is a channel to follow and an
        // address to message — but listing it twice in one row is a mistake rather than a choice.
        builder.HasIndex(e => new { e.Placement, e.Platform }).IsUnique();
    }
}

/// <summary>The rows in the site footer's link columns.</summary>
public sealed class FooterLinkConfiguration : EntityConfigurationBase<FooterLink>
{
    protected override void ConfigureEntity(EntityTypeBuilder<FooterLink> builder)
    {
        builder.ToTable("FooterLinks");

        builder.Property(l => l.Column).HasConversion<int>();
        builder.Property(l => l.Visibility).HasConversion<int>();
        builder.Property(l => l.Url).IsRequired().HasMaxLength(500);
        builder.Property(l => l.IsActive).HasDefaultValue(true);

        builder.HasMany(l => l.Translations)
            .WithOne(t => t.FooterLink!)
            .HasForeignKey(t => t.FooterLinkId)
            .OnDelete(DeleteBehavior.Cascade);

        // How the footer reads them: one column at a time, in the operator's own arrangement.
        builder.HasIndex(l => new { l.Column, l.IsActive, l.SortOrder });
    }
}

/// <summary>One language's label for a footer link.</summary>
public sealed class FooterLinkTranslationConfiguration : EntityConfigurationBase<FooterLinkTranslation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<FooterLinkTranslation> builder)
    {
        builder.ToTable("FooterLinkTranslations");
        builder.Property(t => t.LanguageCode).IsRequired().HasMaxLength(10);
        builder.Property(t => t.Label).IsRequired().HasMaxLength(120);

        builder.HasIndex(t => new { t.FooterLinkId, t.LanguageCode }).IsUnique();
    }
}

/// <summary>The entries in the site header, in the order an operator arranged them.</summary>
public sealed class HeaderLinkConfiguration : EntityConfigurationBase<HeaderLink>
{
    protected override void ConfigureEntity(EntityTypeBuilder<HeaderLink> builder)
    {
        builder.ToTable("HeaderLinks");

        builder.Property(l => l.Key).HasMaxLength(40);
        builder.Property(l => l.Visibility).HasConversion<int>();
        builder.Property(l => l.Url).IsRequired().HasMaxLength(500);
        builder.Property(l => l.IsActive).HasDefaultValue(true);

        builder.HasMany(l => l.Translations)
            .WithOne(t => t.HeaderLink!)
            .HasForeignKey(t => t.HeaderLinkId)
            .OnDelete(DeleteBehavior.Cascade);

        // How the header reads them: the active rows, in the operator's arrangement.
        builder.HasIndex(l => new { l.IsActive, l.SortOrder });

        // A built-in entry exists once. Rows an operator adds carry no key, and SQL Server's
        // filtered index keeps those out of the constraint rather than colliding on null.
        builder.HasIndex(l => l.Key).IsUnique().HasFilter("[Key] IS NOT NULL");
    }
}

/// <summary>One language's label override for a header entry.</summary>
public sealed class HeaderLinkTranslationConfiguration : EntityConfigurationBase<HeaderLinkTranslation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<HeaderLinkTranslation> builder)
    {
        builder.ToTable("HeaderLinkTranslations");
        builder.Property(t => t.LanguageCode).IsRequired().HasMaxLength(10);
        builder.Property(t => t.Label).IsRequired().HasMaxLength(120);

        builder.HasIndex(t => new { t.HeaderLinkId, t.LanguageCode }).IsUnique();
    }
}

/// <summary>The marks shown beside the footer's brand block.</summary>
public sealed class FooterLogoConfiguration : EntityConfigurationBase<FooterLogo>
{
    protected override void ConfigureEntity(EntityTypeBuilder<FooterLogo> builder)
    {
        builder.ToTable("FooterLogos");

        builder.Property(l => l.ImageStoragePath).HasMaxLength(500);
        builder.Property(l => l.ImageContentType).HasMaxLength(100);
        builder.Property(l => l.ImageFileName).HasMaxLength(260);
        builder.Property(l => l.Url).HasMaxLength(500);
        builder.Property(l => l.IsActive).HasDefaultValue(true);

        // Both derived from the stored path, so neither can drift from it.
        builder.Ignore(l => l.HasImage);
        builder.Ignore(l => l.IsShowable);

        builder.HasMany(l => l.Translations)
            .WithOne(t => t.FooterLogo!)
            .HasForeignKey(t => t.FooterLogoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => new { l.IsActive, l.SortOrder });
    }
}

/// <summary>One language's alternative text for a footer mark.</summary>
public sealed class FooterLogoTranslationConfiguration : EntityConfigurationBase<FooterLogoTranslation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<FooterLogoTranslation> builder)
    {
        builder.ToTable("FooterLogoTranslations");
        builder.Property(t => t.LanguageCode).IsRequired().HasMaxLength(10);
        builder.Property(t => t.Alt).IsRequired().HasMaxLength(200);

        builder.HasIndex(t => new { t.FooterLogoId, t.LanguageCode }).IsUnique();
    }
}

/// <summary>The countries marked on the landing page's coverage map.</summary>
public sealed class CoverageEntryConfiguration : EntityConfigurationBase<CoverageEntry>
{
    protected override void ConfigureEntity(EntityTypeBuilder<CoverageEntry> builder)
    {
        builder.ToTable("CoverageEntries");

        builder.Property(e => e.IsPublished).HasDefaultValue(true);
        builder.Property(e => e.Marker).HasDefaultValue(CoverageMarker.Spot);

        builder.HasOne(e => e.Country!)
            .WithMany()
            .HasForeignKey(e => e.CountryId)
            // Restrict, not Cascade: a country in use by the marketing map is one somebody would
            // want to know about before deleting it, and the lookup already deactivates rather
            // than deletes anything an order depends on.
            .OnDelete(DeleteBehavior.Restrict);

        // A country is either covered or it is not; two rows for one country would draw the same
        // spot twice and read as a duplicate in the list beside the map.
        builder.HasIndex(e => e.CountryId).IsUnique();
        builder.HasIndex(e => new { e.IsPublished, e.SortOrder });
    }
}

/// <summary>Every press of "forgot password", and what came of the password it offered.</summary>
public sealed class PasswordResetRequestConfiguration : EntityConfigurationBase<PasswordResetRequest>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PasswordResetRequest> builder)
    {
        builder.ToTable("PasswordResetRequests");

        builder.Property(r => r.OrderNumber).IsRequired().HasMaxLength(29);
        builder.Property(r => r.MaskedEmail).HasMaxLength(320);
        // Long enough for IPv6 with a scope, and for a proxy chain's first hop.
        builder.Property(r => r.IpAddress).HasMaxLength(64);
        builder.Property(r => r.UserAgent).HasMaxLength(512);
        builder.Property(r => r.Browser).HasMaxLength(100);
        builder.Property(r => r.OperatingSystem).HasMaxLength(100);
        builder.Property(r => r.DeviceType).HasMaxLength(20);
        builder.Property(r => r.Country).HasMaxLength(100);
        builder.Property(r => r.CountryCode).HasMaxLength(2);
        builder.Property(r => r.City).HasMaxLength(100);
        builder.Property(r => r.Continent).HasMaxLength(50);
        builder.Property(r => r.ContinentCode).HasMaxLength(2);
        builder.Property(r => r.Region).HasMaxLength(50);
        builder.Property(r => r.RegionName).HasMaxLength(100);
        builder.Property(r => r.District).HasMaxLength(100);
        builder.Property(r => r.PostalCode).HasMaxLength(20);
        builder.Property(r => r.TimeZone).HasMaxLength(64);
        builder.Property(r => r.Currency).HasMaxLength(3);
        builder.Property(r => r.Isp).HasMaxLength(200);
        builder.Property(r => r.Organisation).HasMaxLength(200);
        builder.Property(r => r.AutonomousSystem).HasMaxLength(200);
        // A reverse-DNS name is a hostname, and 253 is the longest one DNS permits.
        builder.Property(r => r.ReverseDns).HasMaxLength(253);
        builder.Property(r => r.AcceptLanguage).HasMaxLength(100);
        builder.Property(r => r.LanguageCode).HasMaxLength(10);

        // The order may be deleted; the record of the attempt should outlive it, so the link is
        // optional and severed rather than cascading the row away.
        builder.HasOne(r => r.Order!)
            .WithMany()
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.SetNull);

        // The log is read newest-first, and filtered by order number when investigating one.
        builder.HasIndex(r => r.CreatedAtUtc);
        builder.HasIndex(r => r.OrderNumber);
    }
}
