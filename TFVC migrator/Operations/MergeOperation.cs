using System;
using System.Diagnostics;

namespace TfvcMigrator.Operations
{
    [DebuggerDisplay("{ToString(),nq}")]
    public sealed class MergeOperation : BranchingOperation, IEquatable<MergeOperation?>
    {
        public MergeOperation(int changeset, BranchIdentity sourceBranch, string sourceBranchPath, BranchIdentity targetBranch, string targetBranchPath)
        {
            if (!PathUtils.IsOrContains(sourceBranch.Path, sourceBranchPath))
                throw new ArgumentException("The source branch path must be the same as or nested within the source branch identity path.");

            if (!PathUtils.IsOrContains(targetBranch.Path, targetBranchPath))
                throw new ArgumentException("The target branch path must be the same as or nested within the target branch identity path.");

            Changeset = changeset;
            SourceBranch = sourceBranch;
            SourceBranchPath = sourceBranchPath;
            TargetBranch = targetBranch;
            TargetBranchPath = targetBranchPath;
        }

        public override int Changeset { get; }
        public BranchIdentity SourceBranch { get; }
        public string SourceBranchPath { get; }
        public BranchIdentity TargetBranch { get; }
        public string TargetBranchPath { get; }

        public override string ToString() => $"CS{Changeset}: Merge {SourceBranchPath} to {TargetBranchPath}";

        public override bool Equals(object? obj)
        {
            return Equals(obj as MergeOperation);
        }

        public bool Equals(MergeOperation? other)
        {
            return other != null &&
                   Changeset == other.Changeset &&
                   SourceBranch.Equals(other.SourceBranch) &&
                   SourceBranchPath.Equals(other.SourceBranchPath, StringComparison.OrdinalIgnoreCase) &&
                   TargetBranch.Equals(other.TargetBranch) &&
                   TargetBranchPath.Equals(other.TargetBranchPath, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                Changeset,
                SourceBranch,
                SourceBranchPath.GetHashCode(StringComparison.OrdinalIgnoreCase),
                TargetBranch,
                TargetBranchPath.GetHashCode(StringComparison.OrdinalIgnoreCase));
        }
    }
}
