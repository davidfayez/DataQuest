namespace DataVerification.Domain.Common;

/// <summary>Base for every persisted aggregate/entity in the model.</summary>
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// Generated in the domain rather than by the database, so an aggregate can be wired up
    /// completely in memory. Settable so the seeder and tests can pin stable identifiers.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>Events raised during this unit of work, dispatched after a successful save.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>Marks entities that are hidden rather than physically removed.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }

    DateTime? DeletedAtUtc { get; set; }
}
