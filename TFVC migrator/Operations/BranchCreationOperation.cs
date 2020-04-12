using System;
using System.Diagnostics;

namespace TfvcMigrator.Operations
{
    [DebuggerDisplay("{ToString(),nq}")]
    public sealed class BranchCreationOperation : BranchingOperation, IEquatable<BranchCreationOperation>
    {
        public BranchCreationOperation(BranchIdentity sourceBranch, BranchIdentity newBranch)
        {
            SourceBranch = sourceBranch;
            NewBranch = newBranch;
        }

        public BranchIdentity SourceBranch { get; }
        public BranchIdentity NewBranch { get; }

        public override string ToString() => $"{SourceBranch} → {NewBranch}";

        public override bool Equals(object? obj)
        {
            return obj is BranchCreationOperation operation && Equals(operation);
        }

        public bool Equals(BranchCreationOperation other)
        {
            return SourceBranch.Equals(other.SourceBranch) &&
                   NewBranch.Equals(other.NewBranch);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SourceBranch, NewBranch);
        }
    }
}
