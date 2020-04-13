using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TfvcMigrator.Operations;

namespace TfvcMigrator
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            await MigrateAsync(new MigrationOptions(
                collectionBaseUrl: new Uri(args[0]),
                rootSourcePath: args[1])
            {
                RootPathChanges =
                {
                    new RootPathChange(changeset: 5322, newSourceRootPath: "$/Exactis/Root"),
                }
            });
        }

        public static async Task MigrateAsync(MigrationOptions options)
        {
            var changesByChangeset = await DownloadChangesAsync(
                options.CollectionBaseUrl,
                options.RootSourcePath,
                options.MinChangeset,
                options.MaxChangeset);

            var currentRootPath = options.RootSourcePath;
            var rootPathChanges = new Stack<RootPathChange>(options.RootPathChanges.OrderByDescending(c => c.Changeset));
            if (rootPathChanges.Zip(rootPathChanges.Skip(1)).Any(pair => pair.First.Changeset == pair.Second.Changeset))
                throw new ArgumentException("There is more than one root path change for the same changeset.", nameof(options));

            var operations = new List<BranchingOperation>();

            var initialFolderCreationChange = changesByChangeset.First().Single(change =>
                change.Item.Path.Equals(currentRootPath, StringComparison.OrdinalIgnoreCase));

            var branchIdentifier = new BranchIdentifier(initialFolder: new BranchIdentity(
                initialFolderCreationChange.Item.ChangesetVersion,
                initialFolderCreationChange.Item.Path));

            var currentBranchPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { currentRootPath };

            foreach (var changes in changesByChangeset.Skip(1))
            {
                var changeset = changes[0].Item.ChangesetVersion;

                if (rootPathChanges.TryPeek(out var nextRootPathChange))
                {
                    if (nextRootPathChange.Changeset < changeset)
                        throw new NotImplementedException("Move root path outside the original root path");

                    if (nextRootPathChange.Changeset == changeset)
                    {
                        rootPathChanges.Pop();
                        if (!currentBranchPaths.Remove(currentRootPath)) throw new NotImplementedException();

                        branchIdentifier.Rename(changeset, currentRootPath, nextRootPathChange.NewSourceRootPath, out var oldIdentity);
                        operations.Add(new RenameOperation(oldIdentity, new BranchIdentity(changeset, nextRootPathChange.NewSourceRootPath)));

                        currentRootPath = nextRootPathChange.NewSourceRootPath;
                        currentBranchPaths.Add(currentRootPath);
                    }
                }

                branchIdentifier.NoFurtherChangesUpTo(changeset - 1);

                foreach (var change in changes)
                {
                    if (change.ChangeType.HasFlag(VersionControlChangeType.Rename) && currentBranchPaths.Remove(change.SourceServerItem))
                    {
                        if (change.ChangeType != VersionControlChangeType.Rename)
                            throw new NotImplementedException("Poorly-understood combination");

                        branchIdentifier.Rename(changeset, change.SourceServerItem, change.Item.Path, out var oldIdentity);
                        operations.Add(new RenameOperation(oldIdentity, new BranchIdentity(changeset, change.Item.Path)));
                        currentBranchPaths.Add(change.Item.Path);
                    }
                }

                foreach (var change in changes)
                {
                    if (change.ChangeType.HasFlag(VersionControlChangeType.Delete) && currentBranchPaths.Remove(change.Item.Path))
                    {
                        if (change.ChangeType != VersionControlChangeType.Delete)
                            throw new NotImplementedException("Poorly-understood combination");

                        var deletedBranch = branchIdentifier.Delete(changeset, change.Item.Path);
                        operations.Add(new DeleteOperation(changeset, deletedBranch));
                    }
                }

                var (branches, merges) = GetBranchAndMergeOperations(changes, branchIdentifier);

                foreach (var operation in branches)
                {
                    operations.Add(operation);
                    branchIdentifier.Add(operation.NewBranch);
                    currentBranchPaths.Add(operation.NewBranch.Path);
                }

                foreach (var operation in merges)
                {
                    operations.Add(operation);
                }
            }
        }

        private static async Task<ImmutableArray<ImmutableArray<TfvcChange>>> DownloadChangesAsync(
            Uri collectionBaseUrl,
            string sourcePath,
            int? minChangeset,
            int? maxChangeset)
        {
            using var connection = new VssConnection(collectionBaseUrl, new VssCredentials());
            using var client = await connection.GetClientAsync<TfvcHttpClient>();

            var changesets = await client.GetChangesetsAsync(
                maxCommentLength: 0,
                top: int.MaxValue,
                orderby: "ID asc",
                searchCriteria: new TfvcChangesetSearchCriteria
                {
                    FollowRenames = true,
                    ItemPath = sourcePath,
                    FromId = minChangeset ?? 0,
                    ToId = maxChangeset ?? 0,
                }).ConfigureAwait(false);

            var changesetsDownloaded = 0;

            return await changesets.SelectAwaitParallel(
                async changeset =>
                {
                    var progress = Interlocked.Increment(ref changesetsDownloaded) - 1;
                    Console.Write($"\rDownloading CS{changeset.ChangesetId} ({progress / (double)changesets.Count:p1})...");

                    var changes = await client.GetChangesetChangesAsync(changeset.ChangesetId, top: int.MaxValue - 1);
                    return changes
                        .Where(c => PathUtils.IsOrContains(sourcePath, c.Item.Path))
                        .ToImmutableArray();
                },
                degreeOfParallelism: 5,
                CancellationToken.None);
        }

        private static (ImmutableHashSet<BranchCreationOperation> Branches, ImmutableHashSet<MergeOperation> Merges)
            GetBranchAndMergeOperations(IReadOnlyCollection<TfvcChange> changes, BranchIdentifier branchIdentifier)
        {
            var branches = ImmutableHashSet.CreateBuilder<BranchCreationOperation>();
            var merges = ImmutableHashSet.CreateBuilder<MergeOperation>();

            foreach (var change in changes)
            {
                if (!(change.MergeSources?.SingleOrDefault(s => !s.IsRename) is { } source)) continue;

                var sourceBranch = branchIdentifier.FindBranchIdentity(source.VersionTo - 1, source.ServerItem)
                    ?? throw new NotImplementedException();

                var (sourcePath, targetPath) = PathUtils.RemoveCommonTrailingSegments(source.ServerItem, change.Item.Path);

                if (change.ChangeType.HasFlag(VersionControlChangeType.Merge))
                {
                    var targetBranch = branchIdentifier.FindBranchIdentity(change.Item.ChangesetVersion - 1, change.Item.Path)
                        ?? throw new NotImplementedException();

                    merges.Add(new MergeOperation(change.Item.ChangesetVersion, sourceBranch, sourcePath, targetBranch, targetPath));
                }
                else
                {
                    branches.Add(new BranchCreationOperation(
                        sourceBranch,
                        sourcePath,
                        newBranch: new BranchIdentity(change.Item.ChangesetVersion, targetPath)));
                }
            }

            if (merges.Count > 1)
            {
                // Two items is very rare and three hasn't been seen yet, so don't spare anything towards optimization.
                merges.ExceptWith(merges
                    .GroupBy(m => (m.SourceBranch, m.TargetBranch))
                    .SelectMany(mergesWithSameSourceAndTargetBranch =>
                        mergesWithSameSourceAndTargetBranch.Where(merge =>
                            mergesWithSameSourceAndTargetBranch.Any(otherMerge =>
                                otherMerge != merge
                                && PathUtils.IsOrContains(otherMerge.SourceBranchPath, merge.SourceBranchPath)
                                && PathUtils.IsOrContains(otherMerge.TargetBranchPath, merge.TargetBranchPath)))));
            }

            return (branches.ToImmutable(), merges.ToImmutable());
        }
    }
}
