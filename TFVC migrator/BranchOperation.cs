using System;
using System.Diagnostics;

namespace TfvcMigrator
{
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct BranchOperation : IEquatable<BranchOperation>
    {
        public BranchOperation(BranchIdentity sourceBranch, BranchIdentity newBranch)
        {
            SourceBranch = sourceBranch;
            NewBranch = newBranch;
        }

        public BranchIdentity SourceBranch { get; }
        public BranchIdentity NewBranch { get; }

        public override string ToString() => $"{SourceBranch} → {NewBranch}";

        public override bool Equals(object? obj)
        {
            return obj is BranchOperation operation && Equals(operation);
        }

        public bool Equals(BranchOperation other)
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
