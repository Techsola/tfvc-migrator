namespace TfvcMigrator.Operations;

[DebuggerDisplay("{ToString(),nq}")]
public sealed record MergeOperation : TopologicalOperation
{
    public MergeOperation(int changeset, BranchIdentity sourceBranch, int sourceBranchChangeset, string sourceBranchPath, BranchIdentity targetBranch, string targetBranchPath)
    {
        if (!PathUtils.IsOrContains(sourceBranch.Path, sourceBranchPath))
            throw new ArgumentException("The source branch path must be the same as or nested within the source branch identity path.");

        if (!PathUtils.IsOrContains(targetBranch.Path, targetBranchPath))
            throw new ArgumentException("The target branch path must be the same as or nested within the target branch identity path.");

        if (sourceBranchChangeset > changeset)
            throw new ArgumentException("The source branch changeset cannot be newer than the merge changeset.");

        Changeset = changeset;
        SourceBranch = sourceBranch;
        SourceBranchChangeset = sourceBranchChangeset;
        SourceBranchPath = sourceBranchPath;
        TargetBranch = targetBranch;
        TargetBranchPath = targetBranchPath;
    }

    public override int Changeset { get; }
    public BranchIdentity SourceBranch { get; }
    public int SourceBranchChangeset { get; }
    public string SourceBranchPath { get; }
    public BranchIdentity TargetBranch { get; }
    public string TargetBranchPath { get; }

    public override string ToString() => $"CS{Changeset}: Merge {SourceBranchPath} at {SourceBranchChangeset} to {TargetBranchPath}";

    public bool Equals(MergeOperation? other)
    {
        return other != null &&
               Changeset == other.Changeset &&
               SourceBranch.Equals(other.SourceBranch) &&
               SourceBranchChangeset == other.SourceBranchChangeset &&
               SourceBranchPath.Equals(other.SourceBranchPath, StringComparison.OrdinalIgnoreCase) &&
               TargetBranch.Equals(other.TargetBranch) &&
               TargetBranchPath.Equals(other.TargetBranchPath, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Changeset,
            SourceBranch,
            SourceBranchChangeset,
            SourceBranchPath.GetHashCode(StringComparison.OrdinalIgnoreCase),
            TargetBranch,
            TargetBranchPath.GetHashCode(StringComparison.OrdinalIgnoreCase));
    }
}
