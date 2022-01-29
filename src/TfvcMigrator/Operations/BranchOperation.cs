namespace TfvcMigrator.Operations
{
    [DebuggerDisplay("{ToString(),nq}")]
    public sealed class BranchOperation : TopologicalOperation, IEquatable<BranchOperation?>
    {
        public BranchOperation(BranchIdentity sourceBranch, int sourceBranchChangeset, string sourceBranchPath, BranchIdentity newBranch)
        {
            if (!PathUtils.IsOrContains(sourceBranch.Path, sourceBranchPath))
                throw new ArgumentException("The source branch path must be the same as or nested within the source branch identity path.");

            if (sourceBranchChangeset > newBranch.CreationChangeset)
                throw new ArgumentException("The source branch changeset cannot be newer than the new branch creation changeset.");

            SourceBranch = sourceBranch;
            SourceBranchChangeset = sourceBranchChangeset;
            SourceBranchPath = sourceBranchPath;
            NewBranch = newBranch;
        }

        public override int Changeset => NewBranch.CreationChangeset;

        public BranchIdentity SourceBranch { get; }
        public int SourceBranchChangeset { get; }
        public string SourceBranchPath { get; }

        public BranchIdentity NewBranch { get; }

        public override string ToString() => $"CS{Changeset}: Branch {SourceBranchPath} at CS{SourceBranchChangeset} to {NewBranch.Path}";

        public override bool Equals(object? obj)
        {
            return Equals(obj as BranchOperation);
        }

        public bool Equals(BranchOperation? other)
        {
            return other != null &&
                   SourceBranch.Equals(other.SourceBranch) &&
                   SourceBranchChangeset == other.SourceBranchChangeset &&
                   SourceBranchPath.Equals(other.SourceBranchPath, StringComparison.OrdinalIgnoreCase) &&
                   NewBranch.Equals(other.NewBranch);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SourceBranch, SourceBranchChangeset, SourceBranchPath.GetHashCode(StringComparison.OrdinalIgnoreCase), NewBranch);
        }
    }
}
