namespace TfvcMigrator.Operations;

[DebuggerDisplay("{ToString(),nq}")]
public sealed record DeleteOperation(int Changeset, BranchIdentity Branch) : TopologicalOperation
{
    public override int Changeset { get; } = Changeset;

    public override string ToString() => $"CS{Changeset}: Delete {Branch.Path}";
}
