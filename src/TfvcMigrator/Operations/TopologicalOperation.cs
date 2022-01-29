namespace TfvcMigrator.Operations;

public abstract class TopologicalOperation
{
    public abstract int Changeset { get; }

    public abstract override string ToString();
    public abstract override bool Equals(object? obj);
    public abstract override int GetHashCode();
}
