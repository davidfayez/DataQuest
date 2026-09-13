using DataVerification.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataVerification.Infrastructure.Persistence.Configurations;

public sealed class ApplicationConfiguration : EntityConfigurationBase<VerificationApplication>
{
    protected override void ConfigureEntity(EntityTypeBuilder<VerificationApplication> builder)
    {
        builder.ToTable("Applications");

        builder.Property(a => a.ApplicationNumber).IsRequired().HasMaxLength(20);
        builder.Property(a => a.AddressedTo).IsRequired().HasMaxLength(500);
        builder.Property(a => a.BirthDate).HasColumnType("date");

        // Nullable like every other field a Draft may leave blank; completeness is insisted on at
        // submit by FindMissingRequiredFields, not by the column.
        builder.Property(a => a.ApplicantEmail).HasMaxLength(320);
        builder.Property(a => a.ApplicantPhoneCountry).HasMaxLength(2).IsFixedLength();
        builder.Property(a => a.ApplicantPhoneCode).HasMaxLength(8);
        builder.Property(a => a.ApplicantPhoneNumber).HasMaxLength(20);
        builder.Ignore(a => a.ApplicantPhone);

        builder.Property(a => a.Status).HasConversion<int>();
        builder.Property(a => a.TotalCost).HasPrecision(18, 2);

        // Paired with the wallet's row version so a double-submitted payment cannot charge twice.
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasIndex(a => a.ApplicationNumber).IsUnique();
        builder.HasIndex(a => new { a.OrderId, a.Status });
        builder.HasIndex(a => a.Status);

        builder.HasOne(a => a.Order)
            .WithMany(o => o.Applications)
            .HasForeignKey(a => a.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.TransactionType)
            .WithMany()
            .HasForeignKey(a => a.TransactionTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.SubTransactionType)
            .WithMany()
            .HasForeignKey(a => a.SubTransactionTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.VerificationAuthority)
            .WithMany()
            .HasForeignKey(a => a.VerificationAuthorityId)
            .OnDelete(DeleteBehavior.Restrict);

        // Deleted drafts stay in the table for audit purposes but disappear from every query.
        builder.HasQueryFilter(a => !a.IsDeleted);

        builder.Ignore(a => a.IsPaid);
        builder.Ignore(a => a.DomainEvents);
    }
}

public sealed class ApplicationNameConfiguration : EntityConfigurationBase<ApplicationName>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ApplicationName> builder)
    {
        builder.ToTable("ApplicationNames");

        builder.Property(n => n.LanguageType).HasConversion<int>();
        builder.Property(n => n.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(n => n.MiddleName).HasMaxLength(100);
        builder.Property(n => n.LastName).IsRequired().HasMaxLength(100);

        builder.HasOne(n => n.Application)
            .WithMany(a => a.Names)
            .HasForeignKey(n => n.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Exactly one Arabic row and one English row per application.
        builder.HasIndex(n => new { n.ApplicationId, n.LanguageType }).IsUnique();

        builder.Ignore(n => n.FullName);
    }
}

public sealed class ApplicationServiceConfiguration : EntityConfigurationBase<ApplicationService>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ApplicationService> builder)
    {
        builder.ToTable("ApplicationServices");

        builder.Property(s => s.LanguageCode).IsRequired().HasMaxLength(10);
        builder.Property(s => s.UnitCost).HasPrecision(18, 2);
        builder.Property(s => s.ExpressCost).HasPrecision(18, 2);
        builder.Property(s => s.LineTotal).HasPrecision(18, 2);

        builder.HasOne(s => s.Application)
            .WithMany(a => a.Services)
            .HasForeignKey(s => s.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.ServiceType)
            .WithMany()
            .HasForeignKey(s => s.ServiceTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => s.ApplicationId);
    }
}

public sealed class ApplicationDocumentValueConfiguration : EntityConfigurationBase<ApplicationDocumentValue>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ApplicationDocumentValue> builder)
    {
        builder.ToTable("ApplicationDocumentValues");

        builder.Property(v => v.Value).IsRequired().HasMaxLength(1000);

        builder.HasOne(v => v.Application)
            .WithMany(a => a.DocumentValues)
            .HasForeignKey(v => v.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a service line removes the documents it required, and their values with them.
        builder.HasOne(v => v.ApplicationService)
            .WithMany()
            .HasForeignKey(v => v.ApplicationServiceId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(v => v.Field)
            .WithMany()
            .HasForeignKey(v => v.RequiredFileFieldId)
            .OnDelete(DeleteBehavior.Cascade);

        // One value per field per service line — the applicant fills each document slot once.
        builder.HasIndex(v => new { v.ApplicationServiceId, v.RequiredFileFieldId }).IsUnique();
    }
}

public sealed class ApplicationFileConfiguration : EntityConfigurationBase<ApplicationFile>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ApplicationFile> builder)
    {
        builder.ToTable("ApplicationFiles");

        builder.Property(f => f.FileName).IsRequired().HasMaxLength(260);
        builder.Property(f => f.StoragePath).IsRequired().HasMaxLength(1000);
        builder.Property(f => f.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(f => f.Kind).HasConversion<int>();
        builder.Property(f => f.UploadedByType).HasConversion<int>();
        builder.Property(f => f.UploadedByName).HasMaxLength(200);

        builder.HasOne(f => f.Application)
            .WithMany(a => a.Files)
            .HasForeignKey(f => f.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting an application already cascades to its files. Letting the service line cascade
        // too would give SQL Server two delete paths to the same table, which it rejects outright
        // (error 1785), so this link is severed in memory by EF instead.
        builder.HasOne(f => f.ApplicationService)
            .WithMany(s => s.Files)
            .HasForeignKey(f => f.ApplicationServiceId)
            .OnDelete(DeleteBehavior.ClientSetNull);

        builder.HasOne(f => f.RequiredFile)
            .WithMany()
            .HasForeignKey(f => f.RequiredFileId)
            .OnDelete(DeleteBehavior.Restrict);

        // Same story as the service line above: the application already cascades to both tables, so
        // a second cascade path here is rejected by SQL Server (error 1785). Removing a document
        // therefore severs its files in memory — and the handler deletes them explicitly.
        builder.HasOne(f => f.Document)
            .WithMany(d => d.Files)
            .HasForeignKey(f => f.ApplicationDocumentId)
            .OnDelete(DeleteBehavior.ClientSetNull);

        // The results section on the applicant's page reads exactly this slice.
        builder.HasIndex(f => new { f.ApplicationId, f.Kind });
    }
}

public sealed class ApplicationDocumentConfiguration : EntityConfigurationBase<ApplicationDocument>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ApplicationDocument> builder)
    {
        builder.ToTable("ApplicationDocuments");

        builder.Property(d => d.NameAr).IsRequired().HasMaxLength(200);
        builder.Property(d => d.NameEn).IsRequired().HasMaxLength(200);
        builder.Property(d => d.AttachedAtStatus).HasConversion<int?>();
        builder.Property(d => d.UploadedByType).HasConversion<int>();
        builder.Property(d => d.UploadedByName).HasMaxLength(200);

        builder.HasOne(d => d.Application)
            .WithMany(a => a.Documents)
            .HasForeignKey(d => d.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        // The applicant's query filters on visibility, so it belongs in the index — the same
        // treatment internal comments get.
        builder.HasIndex(d => new { d.ApplicationId, d.IsVisibleToApplicant, d.SortOrder });

        // Attached documents are review artefacts, not lookup rows; there is nothing to deactivate.
        builder.Ignore(d => d.IsActive);
    }
}

public sealed class ApplicationDocumentFieldConfiguration
    : EntityConfigurationBase<ApplicationDocumentField>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ApplicationDocumentField> builder)
    {
        builder.ToTable("ApplicationDocumentFields");

        builder.Property(f => f.NameAr).IsRequired().HasMaxLength(200);
        builder.Property(f => f.NameEn).IsRequired().HasMaxLength(200);
        builder.Property(f => f.FieldType).HasConversion<int>();
        builder.Property(f => f.DateRule).HasConversion<int>();
        builder.Property(f => f.Value).HasMaxLength(1000);
        builder.Property(f => f.Pattern).HasMaxLength(500);
        builder.Property(f => f.MinValue).HasPrecision(18, 4);
        builder.Property(f => f.MaxValue).HasPrecision(18, 4);
        builder.Property(f => f.MinDate).HasColumnType("date");
        builder.Property(f => f.MaxDate).HasColumnType("date");

        builder.HasOne(f => f.Document)
            .WithMany(d => d.Fields)
            .HasForeignKey(f => f.ApplicationDocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => new { f.ApplicationDocumentId, f.SortOrder });

        builder.Ignore(f => f.IsActive);
    }
}

public sealed class ApplicationDocumentFieldOptionConfiguration
    : EntityConfigurationBase<ApplicationDocumentFieldOption>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ApplicationDocumentFieldOption> builder)
    {
        builder.ToTable("ApplicationDocumentFieldOptions");

        builder.Property(o => o.Value).IsRequired().HasMaxLength(200);
        builder.Property(o => o.LabelAr).IsRequired().HasMaxLength(200);
        builder.Property(o => o.LabelEn).IsRequired().HasMaxLength(200);

        builder.HasOne(o => o.Field)
            .WithMany(f => f.Options)
            .HasForeignKey(o => o.ApplicationDocumentFieldId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(o => new { o.ApplicationDocumentFieldId, o.SortOrder });
    }
}

public sealed class ApplicationCommentConfiguration : EntityConfigurationBase<ApplicationComment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ApplicationComment> builder)
    {
        builder.ToTable("ApplicationComments");

        builder.Property(c => c.AuthorType).HasConversion<int>();
        builder.Property(c => c.AuthorName).HasMaxLength(200);
        builder.Property(c => c.Visibility).HasConversion<int>();
        builder.Property(c => c.Body).IsRequired().HasMaxLength(4000);

        builder.HasOne(c => c.Application)
            .WithMany(a => a.Comments)
            .HasForeignKey(c => c.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Applicant queries always filter on visibility, so it belongs in the index.
        builder.HasIndex(c => new { c.ApplicationId, c.Visibility, c.CreatedAtUtc });
    }
}

public sealed class ApplicationStatusHistoryConfiguration
    : EntityConfigurationBase<ApplicationStatusHistory>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ApplicationStatusHistory> builder)
    {
        builder.ToTable("ApplicationStatusHistory");

        builder.Property(h => h.FromStatus).HasConversion<int>();
        builder.Property(h => h.ToStatus).HasConversion<int>();
        builder.Property(h => h.ChangedByType).HasConversion<int>();
        builder.Property(h => h.ChangedByName).HasMaxLength(200);
        builder.Property(h => h.Note).HasMaxLength(2000);

        builder.HasOne(h => h.Application)
            .WithMany(a => a.StatusHistory)
            .HasForeignKey(h => h.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(h => new { h.ApplicationId, h.CreatedAtUtc });
    }
}
