using System.Text.Json;
using DataVerification.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataVerification.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : EntityConfigurationBase<Order>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");

        builder.Property(o => o.Email).IsRequired().HasMaxLength(320);
        // Client code (up to 20) plus nine digits. Variable length, because the prefix is whatever
        // the owning client's code happens to be.
        builder.Property(o => o.OrderNumber).IsRequired().HasMaxLength(29);
        builder.Property(o => o.PasswordHash).IsRequired().HasMaxLength(500);
        // Base64 of a ~37-byte envelope for an 8-character password; 500 leaves room to spare.
        builder.Property(o => o.PasswordSecret).HasMaxLength(500);
        builder.Property(o => o.LanguageCode).IsRequired().HasMaxLength(10);

        // Nullable: orders set up before contact details were collected have none, and the columns
        // are only ever written by CompleteSetup.
        builder.Property(o => o.ContactPersonName).HasMaxLength(200);
        builder.Property(o => o.ContactPersonPhoneCountry).HasMaxLength(2).IsFixedLength();
        builder.Property(o => o.ContactPersonPhoneCode).HasMaxLength(8);
        builder.Property(o => o.ContactPersonPhoneNumber).HasMaxLength(20);
        builder.Ignore(o => o.ContactPersonPhone);

        // The order number is the applicant's login name, so it must be unique and fast to look up.
        builder.HasIndex(o => o.OrderNumber).IsUnique();
        builder.HasIndex(o => o.Email);

        builder.HasOne(o => o.Client)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.VerificationCountry)
            .WithMany()
            .HasForeignKey(o => o.VerificationCountryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Currency)
            .WithMany()
            .HasForeignKey(o => o.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Wallet)
            .WithOne(w => w.Order)
            .HasForeignKey<Wallet>(w => w.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(o => o.IsSetupComplete);
    }
}

public sealed class WalletConfiguration : EntityConfigurationBase<Wallet>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("Wallets");

        builder.Property(w => w.Balance).HasPrecision(18, 2).HasDefaultValue(0m);

        // Optimistic concurrency: two simultaneous payments cannot both spend the same balance.
        builder.Property(w => w.RowVersion).IsRowVersion();

        builder.HasOne(w => w.Currency)
            .WithMany()
            .HasForeignKey(w => w.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(w => w.OrderId).IsUnique();

        builder.Ignore(w => w.LedgerBalance);
    }
}

public sealed class WalletTransactionConfiguration : EntityConfigurationBase<WalletTransaction>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override void ConfigureEntity(EntityTypeBuilder<WalletTransaction> builder)
    {
        builder.ToTable("WalletTransactions");

        builder.Property(t => t.Amount).HasPrecision(18, 2);
        builder.Property(t => t.BalanceAfter).HasPrecision(18, 2);
        builder.Property(t => t.PerformedByName).HasMaxLength(200);
        builder.Property(t => t.Note).HasMaxLength(1000);

        // A single payment can settle several applications, so the references are stored as a
        // JSON array rather than a join table the ledger would never query relationally.
        builder.Property(t => t.ReferenceApplicationIds)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                ids => JsonSerializer.Serialize(ids, JsonOptions),
                json => JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions) ?? new List<Guid>(),
                new ValueComparer<List<Guid>>(
                    (left, right) => left != null && right != null && left.SequenceEqual(right),
                    ids => ids.Aggregate(0, (hash, id) => HashCode.Combine(hash, id.GetHashCode())),
                    ids => ids.ToList()));

        builder.HasOne(t => t.Wallet)
            .WithMany(w => w.Transactions)
            .HasForeignKey(t => t.WalletId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => new { t.WalletId, t.CreatedAtUtc });
    }
}

public sealed class WalletRequestConfiguration : EntityConfigurationBase<WalletRequest>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WalletRequest> builder)
    {
        builder.ToTable("WalletRequests");

        builder.Property(r => r.Amount).HasPrecision(18, 2);
        builder.Property(r => r.ApplicantNote).HasMaxLength(1000);
        builder.Property(r => r.ReviewerNote).HasMaxLength(1000);
        builder.Property(r => r.RequestedByName).HasMaxLength(200);
        builder.Property(r => r.ReviewedByName).HasMaxLength(200);
        builder.Property(r => r.ReferenceNumber).HasMaxLength(100);
        builder.Property(r => r.ConfirmedAmount).HasPrecision(18, 2);
        builder.Property(r => r.ConfirmedReference).HasMaxLength(100);

        // Derived from the two above; storing it would let the three disagree.
        builder.Ignore(r => r.EffectiveAmount);

        // Two administrators opening the queue at once must not both settle the same request.
        builder.Property(r => r.RowVersion).IsRowVersion();

        builder.HasOne(r => r.Order)
            .WithMany()
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // The wallet is deleted with its order, which already cascades above; a second cascade
        // path would be rejected by SQL Server.
        builder.HasOne(r => r.Wallet)
            .WithMany()
            .HasForeignKey(r => r.WalletId)
            .OnDelete(DeleteBehavior.Restrict);

        // A method may be retired but never deleted out from under the requests that quote it,
        // which is what keeps a decided request's paper trail readable years later.
        builder.HasOne(r => r.PaymentMethod)
            .WithMany()
            .HasForeignKey(r => r.PaymentMethodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.PaymentMethodAccount)
            .WithMany()
            .HasForeignKey(r => r.PaymentMethodAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // The queue is read by status, and an order's own history by order.
        builder.HasIndex(r => new { r.Status, r.CreatedAtUtc });
        builder.HasIndex(r => new { r.OrderId, r.Status });

        // One transfer receipt can only ever be claimed once. Filtered, because withdrawals and
        // the deposits raised before payment methods existed carry no reference at all, and SQL
        // Server would otherwise treat every one of those NULLs as a collision.
        builder.HasIndex(r => r.ReferenceNumber)
            .IsUnique()
            .HasFilter("[ReferenceNumber] IS NOT NULL");

        // A receipt may be credited only once. Scoped to approved rows: a rejected claim took no
        // money, so its reference stays free for the applicant to quote correctly next time.
        builder.HasIndex(r => r.ConfirmedReference)
            .IsUnique()
            .HasFilter("[ConfirmedReference] IS NOT NULL AND [Status] = 1");
    }
}
