using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TfvcMigrator
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            await Main(collectionBaseUrl: new Uri(args[0]), sourcePath: args[1]);
        }

        public static async Task Main(Uri collectionBaseUrl, string sourcePath)
        {
            var changesByChangeset = await DownloadChangesAsync(collectionBaseUrl, sourcePath, maxChangesetId: 5142);

            var initialFolderCreationChange = changesByChangeset.First().Single(change =>
                change.Item.Path.Equals(sourcePath, StringComparison.OrdinalIgnoreCase));

            var branchIdentifier = new BranchIdentifier(initialFolder: new BranchIdentity(
                initialFolderCreationChange.Item.ChangesetVersion,
                initialFolderCreationChange.Item.Path));

            var currentBranchPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var changes in changesByChangeset.Skip(1))
            {
                branchIdentifier.NoFurtherChangesUpTo(changes[0].Item.ChangesetVersion - 1);

                foreach (var change in changes)
                {
                    if (change.ChangeType.HasFlag(VersionControlChangeType.Delete)
                        && currentBranchPaths.Remove(change.Item.Path))
                    {
                        branchIdentifier.Delete(change.Item.ChangesetVersion, change.Item.Path);
                    }
                }

                var branchOperations = GetBranchOperations(changes, branchIdentifier);

                foreach (var operation in branchOperations)
                {
                    branchIdentifier.Add(operation.NewBranch);
                    currentBranchPaths.Add(operation.NewBranch.Path);
                }
            }
        }

        private static async Task<ImmutableArray<ImmutableArray<TfvcChange>>> DownloadChangesAsync(Uri collectionBaseUrl, string sourcePath, int? maxChangesetId = null)
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
                    ToId = maxChangesetId ?? 0,
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

        private static ImmutableHashSet<BranchOperation> GetBranchOperations(IReadOnlyCollection<TfvcChange> changes, BranchIdentifier branchIdentifier)
        {
            var builder = ImmutableHashSet.CreateBuilder<BranchOperation>();

            foreach (var change in changes)
            {
                if (!(change.MergeSources?.Single() is { } source)) continue;

                if (!source.IsRename && (change.ChangeType & (VersionControlChangeType.Rename | VersionControlChangeType.SourceRename | VersionControlChangeType.TargetRename)) != 0)
                    throw new NotImplementedException();

                if (!(branchIdentifier.FindBranchIdentity(source.VersionTo - 1, source.ServerItem) is { } sourceBranch))
                {
                    if (source.IsRename) continue;
                    throw new NotImplementedException();
                }

                var (mergeSource, mergeTarget) = RemoveCommonTrailingSegments(source.ServerItem, change.Item.Path);

                if (!source.IsRename || sourceBranch.Path.Equals(mergeSource, StringComparison.OrdinalIgnoreCase))
                {
                    builder.Add(new BranchOperation(sourceBranch, newBranch: new BranchIdentity(change.Item.ChangesetVersion, mergeTarget)));
                }
            }

            return builder.ToImmutable();
        }

        private static (string SourcePath, string TargetPath) RemoveCommonTrailingSegments(
            ReadOnlySpan<char> sourcePath,
            ReadOnlySpan<char> targetPath)
        {
            var targetLengthOffset = targetPath.Length - sourcePath.Length;

            while (true)
            {
                var sourceSlashIndex = sourcePath.LastIndexOf('/');
                if (sourceSlashIndex == -1) break;

                if (!targetPath.EndsWith(sourcePath.Slice(sourceSlashIndex), StringComparison.OrdinalIgnoreCase))
                {
                    return (sourcePath.ToString(), targetPath.ToString());
                }

                sourcePath = sourcePath.Slice(0, sourceSlashIndex);
                targetPath = targetPath.Slice(0, sourceSlashIndex + targetLengthOffset);
            }

            if (!targetPath.Equals(sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                return (sourcePath.ToString(), targetPath.ToString());
            }

            return (string.Empty, string.Empty);
        }
    }
}
