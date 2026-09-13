using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataVerification.Infrastructure.Persistence.Configurations;

public sealed class TicketCategoryConfiguration : LocalizedLookupConfigurationBase<TicketCategory>
{
    protected override void ConfigureLookup(EntityTypeBuilder<TicketCategory> builder)
    {
        builder.ToTable("TicketCategories");
        builder.HasIndex(c => c.SortOrder);
    }
}

public sealed class TicketConfiguration : EntityConfigurationBase<Ticket>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Tickets");

        builder.Property(t => t.TicketNumber).IsRequired().HasMaxLength(20);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Email).IsRequired().HasMaxLength(256);
        builder.Property(t => t.PhoneCountryCode).HasMaxLength(8);
        builder.Property(t => t.PhoneNumber).HasMaxLength(32);
        builder.Property(t => t.Subject).IsRequired().HasMaxLength(300);
        builder.Property(t => t.Description).IsRequired().HasMaxLength(5000);
        builder.Property(t => t.LanguageCode).IsRequired().HasMaxLength(10).HasDefaultValue("en");
        builder.Property(t => t.Status).HasConversion<int>();

        // Composed from the two phone columns; storing it would let the parts and the whole drift.
        builder.Ignore(t => t.FullPhoneNumber);
        builder.Ignore(t => t.AcceptsReplies);

        // The reference is quoted in emails and searched on by support, so it has to be unique.
        builder.HasIndex(t => t.TicketNumber).IsUnique();

        builder.HasOne(t => t.Category)
            .WithMany(c => c.Tickets)
            .HasForeignKey(t => t.TicketCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Never Cascade: an enquiry outlives the things it was linked to, and deleting an admin
        // account or an order must not destroy the support history that referenced it.
        //
        // A colleague who leaves simply stops being the assignee, so that link nulls itself.
        builder.HasOne(t => t.AssignedTo)
            .WithMany()
            .HasForeignKey(t => t.AssignedToAdminUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // The order and application links are NoAction rather than SetNull because SQL Server
        // refuses two paths to the same table, and deleting an order already cascades into its
        // applications — which would reach Tickets twice. Neither is hard-deleted by the platform
        // (applications are soft-deleted, orders are never removed), so this constraint only ever
        // fires against a manual delete, where refusing is the better answer anyway.
        builder.HasOne(t => t.Order)
            .WithMany()
            .HasForeignKey(t => t.OrderId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(t => t.Application)
            .WithMany()
            .HasForeignKey(t => t.ApplicationId)
            .OnDelete(DeleteBehavior.NoAction);

        // The queue's default view: open tickets, newest first.
        builder.HasIndex(t => new { t.Status, t.CreatedAtUtc });
        builder.HasIndex(t => t.AssignedToAdminUserId);
        builder.HasIndex(t => t.OrderId);
        builder.HasIndex(t => t.Email);
    }
}

public sealed class TicketActionConfiguration : EntityConfigurationBase<TicketAction>
{
    protected override void ConfigureEntity(EntityTypeBuilder<TicketAction> builder)
    {
        builder.ToTable("TicketActions");

        builder.Property(a => a.Body).HasMaxLength(5000);
        builder.Property(a => a.AuthorName).HasMaxLength(200);
        builder.Property(a => a.NotifiedEmail).HasMaxLength(256);
        builder.Property(a => a.Visibility).HasConversion<int>();
        builder.Property(a => a.AuthorType).HasConversion<int>().HasDefaultValue(ActorType.Admin);
        builder.Property(a => a.ChangedStatusTo).HasConversion<int>();

        // All three are read from the columns above; none is stored.
        builder.Ignore(a => a.CanBeSentToSender);
        builder.Ignore(a => a.WasNotified);
        builder.Ignore(a => a.IsFromApplicant);

        builder.HasOne(a => a.Ticket)
            .WithMany(t => t.Actions)
            .HasForeignKey(a => a.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.TicketId, a.CreatedAtUtc });
    }
}

public sealed class TicketActionDocumentConfiguration : EntityConfigurationBase<TicketActionDocument>
{
    protected override void ConfigureEntity(EntityTypeBuilder<TicketActionDocument> builder)
    {
        builder.ToTable("TicketActionDocuments");

        builder.Property(d => d.Title).IsRequired().HasMaxLength(200);
        builder.Property(d => d.Description).HasMaxLength(2000);

        builder.HasOne(d => d.Action)
            .WithMany(a => a.Documents)
            .HasForeignKey(d => d.TicketActionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => new { d.TicketActionId, d.SortOrder });
    }
}

public sealed class TicketFileConfiguration : EntityConfigurationBase<TicketFile>
{
    protected override void ConfigureEntity(EntityTypeBuilder<TicketFile> builder)
    {
        builder.ToTable("TicketFiles");

        builder.Property(f => f.FileName).IsRequired().HasMaxLength(260);
        builder.Property(f => f.StoragePath).IsRequired().HasMaxLength(500);
        builder.Property(f => f.ContentType).IsRequired().HasMaxLength(120);
        builder.Property(f => f.UploadedByName).HasMaxLength(200);

        builder.HasOne(f => f.Ticket)
            .WithMany(t => t.Files)
            .HasForeignKey(f => f.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction, not Cascade: SQL Server refuses two cascade paths to the same table, and the
        // ticket already owns one. Removing a document deletes its files explicitly instead.
        builder.HasOne(f => f.Document)
            .WithMany(d => d.Files)
            .HasForeignKey(f => f.TicketActionDocumentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(f => f.TicketId);
        builder.HasIndex(f => f.TicketActionDocumentId);
    }
}
