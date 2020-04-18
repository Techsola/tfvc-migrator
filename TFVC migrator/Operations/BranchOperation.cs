using System;
using System.Diagnostics;

namespace TfvcMigrator.Operations
{
    [DebuggerDisplay("{ToString(),nq}")]
    public sealed class BranchOperation : TopologicalOperation, IEquatable<BranchOperation?>
    {
        public BranchOperation(BranchIdentity sourceBranch, string sourceBranchPath, BranchIdentity newBranch)
        {
            if (!PathUtils.IsOrContains(sourceBranch.Path, sourceBranchPath))
                throw new ArgumentException("The source branch path must be the same as or nested within the source branch identity path.");

            SourceBranch = sourceBranch;
            SourceBranchPath = sourceBranchPath;
            NewBranch = newBranch;
        }

        public override int Changeset => NewBranch.CreationChangeset;
        public BranchIdentity SourceBranch { get; }
        public string SourceBranchPath { get; }
        public BranchIdentity NewBranch { get; }

        public override string ToString() => $"CS{Changeset}: Branch {SourceBranchPath} to {NewBranch.Path}";

        public override bool Equals(object? obj)
        {
            return Equals(obj as BranchOperation);
        }

        public bool Equals(BranchOperation? other)
        {
            return other != null &&
                   SourceBranch.Equals(other.SourceBranch) &&
                   SourceBranchPath.Equals(other.SourceBranchPath, StringComparison.OrdinalIgnoreCase) &&
                   NewBranch.Equals(other.NewBranch);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SourceBranch, SourceBranchPath.GetHashCode(StringComparison.OrdinalIgnoreCase), NewBranch);
        }
    }
}
