namespace TfvcMigrator.Operations;

[DebuggerDisplay("{ToString(),nq}")]
public sealed record RenameOperation(BranchIdentity OldIdentity, BranchIdentity NewIdentity) : TopologicalOperation
{
    public override int Changeset => NewIdentity.CreationChangeset;

    public override string ToString() => $"CS{Changeset}: Rename {OldIdentity.Path} to {NewIdentity.Path}";
}
