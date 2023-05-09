namespace TfvcMigrator.Operations;

/// <summary>
/// Represents a source folder being renamed, which is not itself a branch, but which is hidden when viewing the virtual
/// file system from the context of another branch.
/// </summary>
public sealed record SourceRenameOperation(int Changeset, string OldPath, string NewPath) : TopologicalOperation
{
    public override int Changeset { get; } = Changeset;

    public override string ToString() => $"CS{Changeset}: Rename branch source folder {OldPath} to {NewPath}";
}
