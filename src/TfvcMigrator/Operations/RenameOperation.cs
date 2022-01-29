namespace TfvcMigrator.Operations
{
    [DebuggerDisplay("{ToString(),nq}")]
    public sealed class RenameOperation : TopologicalOperation, IEquatable<RenameOperation?>
    {
        public RenameOperation(BranchIdentity oldIdentity, BranchIdentity newIdentity)
        {
            OldIdentity = oldIdentity;
            NewIdentity = newIdentity;
        }

        public override int Changeset => NewIdentity.CreationChangeset;
        public BranchIdentity OldIdentity { get; }
        public BranchIdentity NewIdentity { get; }

        public override string ToString() => $"CS{Changeset}: Rename {OldIdentity.Path} to {NewIdentity.Path}";

        public override bool Equals(object? obj)
        {
            return Equals(obj as RenameOperation);
        }

        public bool Equals(RenameOperation? other)
        {
            return other != null &&
                   OldIdentity.Equals(other.OldIdentity) &&
                   NewIdentity.Equals(other.NewIdentity);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(OldIdentity, NewIdentity);
        }
    }
}
