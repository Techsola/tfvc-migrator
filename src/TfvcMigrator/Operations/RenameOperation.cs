namespace TfvcMigrator.Operations;

/// <summary>
/// Represents a branch being renamed.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public sealed record RenameOperation(BranchIdentity OldIdentity, BranchIdentity NewIdentity) : TopologicalOperation
{
    public override int Changeset => NewIdentity.CreationChangeset;

    public override string ToString() => $"CS{Changeset}: Rename branch {OldIdentity.Path} to {NewIdentity.Path}";
}
