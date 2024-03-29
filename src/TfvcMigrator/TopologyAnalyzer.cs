﻿using Microsoft.TeamFoundation.SourceControl.WebApi;
using TfvcMigrator.Operations;

namespace TfvcMigrator;

public sealed class TopologyAnalyzer
{
    private readonly Stack<RootPathChange> rootPathChangeStack;
    private readonly BranchIdentifier branchIdentifier;
    private readonly HashSet<string> currentBranchPaths;
    private string currentRootPath;

    public TopologyAnalyzer(BranchIdentity initialBranch, ImmutableArray<RootPathChange> rootPathChanges)
    {
        rootPathChangeStack = new Stack<RootPathChange>(rootPathChanges.OrderByDescending(c => c.Changeset));
        if (rootPathChangeStack.Zip(rootPathChangeStack.Skip(1)).Any(pair => pair.First.Changeset == pair.Second.Changeset))
            throw new ArgumentException("There is more than one root path change for the same changeset.", nameof(rootPathChanges));

        branchIdentifier = new BranchIdentifier(initialBranch);

        currentRootPath = initialBranch.Path;

        currentBranchPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { currentRootPath };
    }

    public IEnumerable<TopologicalOperation> GetTopologicalOperations(IReadOnlyList<TfvcChange> changesetChanges)
    {
        if (!changesetChanges.Any()) yield break;

        var changeset = changesetChanges.First().Item.ChangesetVersion;

        if (rootPathChangeStack.TryPeek(out var nextRootPathChange))
        {
            if (nextRootPathChange.Changeset < changeset)
                throw new NotImplementedException("Move root path outside the original root path");

            if (nextRootPathChange.Changeset == changeset)
            {
                rootPathChangeStack.Pop();
                if (!currentBranchPaths.Remove(currentRootPath)) throw new NotImplementedException();

                var newIdentity = new BranchIdentity(changeset, nextRootPathChange.NewSourceRootPath);
                branchIdentifier.Rename(changeset, currentRootPath, nextRootPathChange.NewSourceRootPath, out var oldIdentity);
                yield return new RenameOperation(oldIdentity, newIdentity);

                currentRootPath = nextRootPathChange.NewSourceRootPath;
                currentBranchPaths.Add(currentRootPath);
            }
        }

        foreach (var change in changesetChanges)
        {
            if (change.ChangeType.HasFlag(VersionControlChangeType.Rename) && currentBranchPaths.Remove(change.SourceServerItem))
            {
                if (change.ChangeType != VersionControlChangeType.Rename)
                    throw new NotImplementedException("Poorly-understood combination");

                var newIdentity = new BranchIdentity(changeset, change.Item.Path);
                branchIdentifier.Rename(changeset, change.SourceServerItem, change.Item.Path, out var oldIdentity);
                yield return new RenameOperation(oldIdentity, newIdentity);
                currentBranchPaths.Add(change.Item.Path);
            }
        }

        branchIdentifier.NoFurtherChangesUpTo(changeset - 1);

        var (branches, merges) = GetBranchAndMergeOperations(changesetChanges, branchIdentifier);

        foreach (var operation in branches)
        {
            yield return operation;
            branchIdentifier.Add(operation.NewBranch);
            currentBranchPaths.Add(operation.NewBranch.Path);
        }

        foreach (var operation in merges)
        {
            yield return operation;
        }

        foreach (var change in changesetChanges)
        {
            if (change.ChangeType.HasFlag(VersionControlChangeType.Delete) && currentBranchPaths.Remove(change.Item.Path))
            {
                if (change.ChangeType != VersionControlChangeType.Delete)
                    throw new NotImplementedException("Poorly-understood combination");

                var deletedBranch = branchIdentifier.Delete(changeset, change.Item.Path);
                yield return new DeleteOperation(changeset, deletedBranch);
            }
        }
    }

    private static (ImmutableArray<BranchOperation> Branches, ImmutableArray<MergeOperation> Merges)
        GetBranchAndMergeOperations(IReadOnlyCollection<TfvcChange> changesetChanges, BranchIdentifier branchIdentifier)
    {
        var maxChangesetsByBranchOperation = ImmutableDictionary.CreateBuilder<
            (BranchIdentity SourceBranch, string SourceBranchPath, string TargetPath),
            int>();

        var maxChangesetsByMergeOperation = ImmutableDictionary.CreateBuilder<
            (BranchIdentity SourceBranch, string SourceBranchPath, BranchIdentity TargetBranch, string TargetBranchPath),
            int>();

        var changeset = changesetChanges.First().Item.ChangesetVersion;

        foreach (var change in changesetChanges)
        {
            if (change.MergeSources?.SingleOrDefault(s => !s.IsRename) is not { } source) continue;

            if (branchIdentifier.FindBranchIdentity(source.VersionTo - 1, source.ServerItem) is not { } sourceBranch)
            {
                if (branchIdentifier.FindBranchIdentity(changeset - 1, change.Item.Path) is not null)
                    throw new NotImplementedException("Merge from outside a known branch");

                continue;
            }

            var (sourcePath, targetPath) = PathUtils.RemoveCommonTrailingSegments(source.ServerItem, change.Item.Path);

            if (change.ChangeType.HasFlag(VersionControlChangeType.Merge))
            {
                var targetBranch = branchIdentifier.FindBranchIdentity(changeset - 1, change.Item.Path)
                    ?? throw new NotImplementedException();

                var operation = (sourceBranch, sourcePath, targetBranch, targetPath);

                if (!maxChangesetsByMergeOperation.TryGetValue(operation, out var max) || max < source.VersionTo)
                    maxChangesetsByMergeOperation[operation] = source.VersionTo;
            }
            else
            {
                var operation = (sourceBranch, sourcePath, targetPath);

                if (!maxChangesetsByBranchOperation.TryGetValue(operation, out var max) || max < source.VersionTo)
                    maxChangesetsByBranchOperation[operation] = source.VersionTo;
            }
        }

        var branches = maxChangesetsByBranchOperation
            .Select(maxChangesetByBranchOperation => new BranchOperation(
                maxChangesetByBranchOperation.Key.SourceBranch,
                sourceBranchChangeset: maxChangesetByBranchOperation.Value,
                maxChangesetByBranchOperation.Key.SourceBranchPath,
                newBranch: new BranchIdentity(changeset, maxChangesetByBranchOperation.Key.TargetPath)))
            .ToImmutableArray();

        var merges = maxChangesetsByMergeOperation
            .Select(maxChangesetByMergeOperation => new MergeOperation(
                changeset,
                maxChangesetByMergeOperation.Key.SourceBranch,
                sourceBranchChangeset: maxChangesetByMergeOperation.Value,
                maxChangesetByMergeOperation.Key.SourceBranchPath,
                maxChangesetByMergeOperation.Key.TargetBranch,
                maxChangesetByMergeOperation.Key.TargetBranchPath))
            .ToImmutableArray();

        if (merges.Length > 1)
        {
            // Two items is very rare and three hasn't been seen yet, so don't spare anything towards optimization.
            merges = merges.RemoveRange(merges
                .GroupBy(m => (m.SourceBranch, m.TargetBranch))
                .SelectMany(mergesWithSameSourceAndTargetBranch =>
                    mergesWithSameSourceAndTargetBranch.Where(merge =>
                        mergesWithSameSourceAndTargetBranch.Any(otherMerge =>
                            otherMerge != merge
                            && PathUtils.IsOrContains(otherMerge.SourceBranchPath, merge.SourceBranchPath)
                            && PathUtils.IsOrContains(otherMerge.TargetBranchPath, merge.TargetBranchPath)))));
        }

        return (branches, merges);
    }
}
