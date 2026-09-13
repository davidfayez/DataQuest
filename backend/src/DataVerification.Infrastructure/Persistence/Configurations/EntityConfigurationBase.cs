using DataVerification.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataVerification.Infrastructure.Persistence.Configurations;

/// <summary>
/// Shared configuration for every entity: a client-generated GUID key and audit timestamps.
/// Keys are generated in the domain (<see cref="Entity"/>) rather than by the database so an
/// aggregate can be wired up completely in memory before the first round trip.
/// </summary>
public abstract class EntityConfigurationBase<TEntity> : IEntityTypeConfiguration<TEntity>
    where TEntity : Entity
{
    public void Configure(EntityTypeBuilder<TEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.CreatedAtUtc).IsRequired();
        builder.Property(e => e.UpdatedAtUtc);

        ConfigureEntity(builder);
    }

    protected abstract void ConfigureEntity(EntityTypeBuilder<TEntity> builder);
}

/// <summary>Base for the admin-managed lookups, which all carry Arabic/English names.</summary>
public abstract class LocalizedLookupConfigurationBase<TEntity> : EntityConfigurationBase<TEntity>
    where TEntity : LocalizedLookup
{
    protected override void ConfigureEntity(EntityTypeBuilder<TEntity> builder)
    {
        builder.Property(e => e.NameAr).IsRequired().HasMaxLength(200);
        builder.Property(e => e.NameEn).IsRequired().HasMaxLength(200);
        builder.Property(e => e.IsActive).HasDefaultValue(true);
        builder.HasIndex(e => e.IsActive);

        ConfigureLookup(builder);
    }

    protected abstract void ConfigureLookup(EntityTypeBuilder<TEntity> builder);
}
