namespace TfvcMigrator.Operations;

public abstract record TopologicalOperation
{
    public abstract int Changeset { get; }

    public abstract override string ToString();
}
