using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TfvcMigrator.Operations;

namespace TfvcMigrator
{
    public static class Program
    {
        public static Task Main(string[] args)
        {
            var command = new RootCommand("Migrates TFVC source history to idiomatic Git history while preserving branch topology.")
            {
                new Argument<Uri>("project-collection-url") { Description = "The URL of the Azure DevOps project collection." },
                new Argument<string>("root-path") { Description = "The source path within the TFVC repository to migrate as a Git repository." },
                new Option<int?>("--min-changeset") { Description = "The changeset defining the initial commit. Defaults to the first changeset under the given source path." },
                new Option<int?>("--max-changeset") { Description = "The last changeset to migrate. Defaults to the most recent changeset under the given source path." },
                new Option<ImmutableArray<RootPathChange>>(
                    "--root-path-changes",
                    parseArgument: result => result.Tokens.Select(token => ParseRootPathChange(token.Value)).ToImmutableArray())
                {
                    Argument = { Arity = ArgumentArity.OneOrMore },
                    Description = "Followed by one or more arguments with the format CS1234:$/New/Path. Changes the path that is mapped as the Git repository root to a new path during a specified changeset."
                },
            };

            command.Handler = CommandHandler.Create(
                new Func<Uri, string, int?, int?, ImmutableArray<RootPathChange>, Task>(MigrateAsync));

            return command.InvokeAsync(args);
        }

        private static RootPathChange ParseRootPathChange(string token)
        {
            var colonIndex = token.IndexOf(':');
            if (colonIndex == -1)
                throw new ArgumentException("Expected a colon in the argument to --root-path-change.", nameof(token));

            var changesetSpan = token.AsSpan(0, colonIndex);
            if (changesetSpan.StartsWith("CS", StringComparison.OrdinalIgnoreCase))
                changesetSpan = changesetSpan["CS".Length..];

            if (!int.TryParse(changesetSpan, NumberStyles.None, CultureInfo.CurrentCulture, out var changeset))
                throw new ArgumentException("Expected a valid changeset number before the colon.", nameof(token));

            return new RootPathChange(changeset, token[(colonIndex + 1)..]);
        }

        public static async Task MigrateAsync(
            Uri projectCollectionUrl,
            string rootPath,
            int? minChangeset = null,
            int? maxChangeset = null,
            ImmutableArray<RootPathChange> rootPathChanges = default)
        {
            if (rootPathChanges.IsDefault) rootPathChanges = ImmutableArray<RootPathChange>.Empty;

            var changesByChangeset = await DownloadChangesAsync(projectCollectionUrl, rootPath, minChangeset, maxChangeset);

            var rootPathChangeStack = new Stack<RootPathChange>(rootPathChanges.OrderByDescending(c => c.Changeset));
            if (rootPathChangeStack.Zip(rootPathChangeStack.Skip(1)).Any(pair => pair.First.Changeset == pair.Second.Changeset))
                throw new ArgumentException("There is more than one root path change for the same changeset.", nameof(rootPathChanges));

            var operations = new List<MigrationOperation>();

            var currentRootPath = rootPath;
            var initialFolderCreationChange = changesByChangeset.First().Single(change =>
                change.Item.Path.Equals(currentRootPath, StringComparison.OrdinalIgnoreCase));

            var branchIdentifier = new BranchIdentifier(new BranchIdentity(
                initialFolderCreationChange.Item.ChangesetVersion,
                initialFolderCreationChange.Item.Path));

            var currentBranchPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { currentRootPath };

            foreach (var changes in changesByChangeset.Skip(1))
            {
                var changeset = changes[0].Item.ChangesetVersion;

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
                        operations.Add(new RenameOperation(oldIdentity, newIdentity));

                        currentRootPath = nextRootPathChange.NewSourceRootPath;
                        currentBranchPaths.Add(currentRootPath);
                    }
                }

                foreach (var change in changes)
                {
                    if (change.ChangeType.HasFlag(VersionControlChangeType.Rename) && currentBranchPaths.Remove(change.SourceServerItem))
                    {
                        if (change.ChangeType != VersionControlChangeType.Rename)
                            throw new NotImplementedException("Poorly-understood combination");

                        var newIdentity = new BranchIdentity(changeset, change.Item.Path);
                        branchIdentifier.Rename(changeset, change.SourceServerItem, change.Item.Path, out var oldIdentity);
                        operations.Add(new RenameOperation(oldIdentity, newIdentity));
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

                branchIdentifier.NoFurtherChangesUpTo(changeset - 1);

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

        private static (ImmutableHashSet<BranchOperation> Branches, ImmutableHashSet<MergeOperation> Merges)
            GetBranchAndMergeOperations(IReadOnlyCollection<TfvcChange> changes, BranchIdentifier branchIdentifier)
        {
            var branches = ImmutableHashSet.CreateBuilder<BranchOperation>();
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
                    branches.Add(new BranchOperation(
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
