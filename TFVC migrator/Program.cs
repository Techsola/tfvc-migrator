using LibGit2Sharp;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
                new Option<string?>("--out-dir") { Description = "The directory path at which to create a new Git repository. Defaults to the last segment in the root path under the current directory." },
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
                new Func<Uri, string, string?, int?, int?, ImmutableArray<RootPathChange>, Task>(MigrateAsync));

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
            string? outDir = null,
            int? minChangeset = null,
            int? maxChangeset = null,
            ImmutableArray<RootPathChange> rootPathChanges = default)
        {
            if (rootPathChanges.IsDefault) rootPathChanges = ImmutableArray<RootPathChange>.Empty;

            var outputDirectory = Path.GetFullPath(
                new[] { outDir, PathUtils.GetLeaf(rootPath), projectCollectionUrl.Segments.LastOrDefault() }
                    .First(name => !string.IsNullOrEmpty(name))!);

            using var repo = new Repository(Repository.Init(outputDirectory));

            using var connection = new VssConnection(projectCollectionUrl, new VssCredentials());
            using var client = await connection.GetClientAsync<TfvcHttpClient>();

            var changesets = await client.GetChangesetsAsync(
                maxCommentLength: int.MaxValue,
                top: int.MaxValue,
                orderby: "ID asc",
                searchCriteria: new TfvcChangesetSearchCriteria
                {
                    FollowRenames = true,
                    ItemPath = rootPath,
                    FromId = minChangeset ?? 0,
                    ToId = maxChangeset ?? 0,
                }).ConfigureAwait(false);

            var changesByChangeset = await DownloadChangesAsync(client, rootPath, changesets);

            var topologicalOperations = GetTopologicalOperations(rootPath, rootPathChanges, changesByChangeset)
                .ToLookup(operation => operation.Changeset);


            var dummyTree = repo.ObjectDatabase.CreateTree(new TreeDefinition());

            var heads = new Dictionary<BranchIdentity, Commit>();

            var initialChangeset = changesByChangeset.First().First().Item.ChangesetVersion;
            var master = new BranchIdentity(initialChangeset, rootPath);

            foreach (var changeset in changesets)
            {
                var author = new Signature(changeset.Author.DisplayName, changeset.Author.UniqueName, changeset.CreatedDate);
                var committer = new Signature(changeset.CheckedInBy.DisplayName, changeset.CheckedInBy.UniqueName, changeset.CreatedDate);
                var message = changeset.Comment + "\n\nMigrated from CS" + changeset.ChangesetId;

                Commit CreateCommit(IEnumerable<Commit> parents)
                {
                    return repo.ObjectDatabase.CreateCommit(author, committer, message, tree: dummyTree, parents, prettifyMessage: true);
                }

                if (!heads.Any())
                {
                    heads.Add(master, CreateCommit(Enumerable.Empty<Commit>()));
                }

                foreach (var operation in topologicalOperations[changeset.ChangesetId])
                {
                    switch (operation)
                    {
                        case BranchOperation branch:
                            heads.Add(branch.NewBranch, CreateCommit(new[] { heads[branch.SourceBranch] }));
                            break;

                        case DeleteOperation delete:
                            if (!heads.Remove(delete.Branch)) throw new NotImplementedException();
                            break;

                        case MergeOperation merge:
                            heads[merge.TargetBranch] = CreateCommit(new[] { heads[merge.TargetBranch], heads[merge.SourceBranch] });
                            break;

                        case RenameOperation rename:
                            if (!heads.Remove(rename.OldIdentity, out var head)) throw new NotImplementedException();

                            heads.Add(rename.NewIdentity, CreateCommit(new[] { head }));

                            if (master == rename.OldIdentity) master = rename.NewIdentity;
                            break;
                    }
                }
            }

            foreach (var (branch, head) in heads)
            {
                repo.CreateBranch(branch == master ? "master" : GetValidGitBranchName(branch.Path), head);
            }
        }

        private static async Task<ImmutableArray<ImmutableArray<TfvcChange>>> DownloadChangesAsync(TfvcHttpClient client, string sourcePath, IReadOnlyCollection<TfvcChangesetRef> changesets)
        {
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

        private static ImmutableArray<MigrationOperation> GetTopologicalOperations(string rootPath, ImmutableArray<RootPathChange> rootPathChanges, ImmutableArray<ImmutableArray<TfvcChange>> changesByChangeset)
        {
            var rootPathChangeStack = new Stack<RootPathChange>(rootPathChanges.OrderByDescending(c => c.Changeset));
            if (rootPathChangeStack.Zip(rootPathChangeStack.Skip(1)).Any(pair => pair.First.Changeset == pair.Second.Changeset))
                throw new ArgumentException("There is more than one root path change for the same changeset.", nameof(rootPathChanges));

            var operations = ImmutableArray.CreateBuilder<MigrationOperation>();

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

            return operations.ToImmutable();
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

        private static string GetValidGitBranchName(string tfsBranchName)
        {
            var leaf = PathUtils.GetLeaf(tfsBranchName);
            var name = new StringBuilder(leaf.Length);
            var skipping = false;

            foreach (var c in leaf)
            {
                var skip = c <= ' ';
                if (!skip)
                {
                    switch (c)
                    {
                        case '-': // Not illegal, but collapse with the rest
                        case '\\':
                        case '?':
                        case '*':
                        case '[':
                        case '~':
                        case '^':
                        case ':':
                        case '\x7f':
                            skip = true;
                            break;
                    }
                }

                if (skip)
                {
                    skipping = true;
                }
                else
                {
                    if (skipping)
                    {
                        name.Append('-');
                        skipping = false;
                    }
                    name.Append(c);
                }
            }

            return name.ToString();
        }
    }
}
