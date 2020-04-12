using System;
using System.Diagnostics;

namespace TfvcMigrator.Operations
{
    [DebuggerDisplay("{ToString(),nq}")]
    public sealed class DeleteOperation : BranchingOperation, IEquatable<DeleteOperation?>
    {
        public DeleteOperation(int changeset, BranchIdentity branch)
        {
            Changeset = changeset;
            Branch = branch;
        }

        public override int Changeset { get; }
        public BranchIdentity Branch { get; }

        public override bool Equals(object? obj)
        {
            return Equals(obj as DeleteOperation);
        }

        public bool Equals(DeleteOperation? other)
        {
            return other != null &&
                   Changeset == other.Changeset &&
                   Branch.Equals(other.Branch);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Changeset, Branch);
        }

        public override string ToString() => $"CS{Changeset}: Delete {Branch.Path}";
    }
}
