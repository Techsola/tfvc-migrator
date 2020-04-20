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
using TaskTupleAwaiter;
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
                new Option<string>("--authors")
                {
                    Required = true,
                    Description = "Path to an authors file with lines mapping TFVC usernames to Git authors, e.g.: DOMAIN\\John = John Doe <john@doe.com>",
                },
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
                new Func<Uri, string, string, string?, int?, int?, ImmutableArray<RootPathChange>, Task>(MigrateAsync));

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
            string authors,
            string? outDir = null,
            int? minChangeset = null,
            int? maxChangeset = null,
            ImmutableArray<RootPathChange> rootPathChanges = default)
        {
            if (rootPathChanges.IsDefault) rootPathChanges = ImmutableArray<RootPathChange>.Empty;

            var outputDirectory = Path.GetFullPath(
                new[] { outDir, PathUtils.GetLeaf(rootPath), projectCollectionUrl.Segments.LastOrDefault() }
                    .First(name => !string.IsNullOrEmpty(name))!);

            Directory.CreateDirectory(outputDirectory);
            if (Directory.GetFileSystemEntries(outputDirectory).Any())
            {
                Console.WriteLine($"Cannot create Git repository at {outputDirectory} because the directory is not empty.");
                return;
            }

            var authorsLookup = LoadAuthors(authors);

            using var repo = new Repository(Repository.Init(outputDirectory));

            using var connection = new VssConnection(projectCollectionUrl, new VssCredentials());
            using var client = await connection.GetClientAsync<TfvcHttpClient>();

            var (changesets, labelsByChangeset) = await (
                client.GetChangesetsAsync(
                    maxCommentLength: int.MaxValue,
                    top: int.MaxValue,
                    orderby: "ID asc",
                    searchCriteria: new TfvcChangesetSearchCriteria
                    {
                        FollowRenames = true,
                        ItemPath = rootPath,
                        FromId = minChangeset ?? 0,
                        ToId = maxChangeset ?? 0,
                    }),
                GetLabelsByChangesetAsync(client, rootPath)
            ).ConfigureAwait(false);

            var unmappedAuthors = changesets.Select(c => c.Author)
                .Concat(changesets.Select(c => c.CheckedInBy))
                .Concat(labelsByChangeset.SelectMany(l => l.Labels, (_, l) => l.Owner))
                .Select(identity => identity.UniqueName)
                .Where(name => !authorsLookup.ContainsKey(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (unmappedAuthors.Any())
            {
                Console.WriteLine("An entry must be added to the authors file for each of the following TFVC users:");
                foreach (var user in unmappedAuthors)
                    Console.WriteLine(user);
                return;
            }

            Console.WriteLine("Downloading changesets and converting to commits...");


            var dummyBlob = repo.ObjectDatabase.CreateBlob(Stream.Null);

            var initialBranch = new BranchIdentity(changesets.First().ChangesetId, rootPath);

            var heads = new Dictionary<BranchIdentity, Branch>();

            var timedProgress = TimedProgress.Start();

            var commitsByChangeset = new Dictionary<int, List<(Commit Commit, BranchIdentity Branch)>>();

            await using var mappingStateAndItemsEnumerator =
                EnumerateMappingStatesAsync(client, rootPathChanges, changesets, initialBranch)
                    .SelectAwait(async state => (
                        MappingState: state,
                        // Make no attempt to reason about applying TFS item changes over time. Ask for the full set of files.
                        Items: await DownloadItemsAsync(
                            client,
                            PathUtils.GetNonOverlappingPaths(
                                state.BranchMappings.Values.Select(mapping => mapping.RootDirectory)),
                            state.Changeset)))
                    .WithLookahead()
                    .GetAsyncEnumerator();

            foreach (var changeset in changesets)
            {
                ReportProgress(changeset.ChangesetId, changesets.Count, timedProgress);

                if (!await mappingStateAndItemsEnumerator.MoveNextAsync())
                    throw new InvalidOperationException("There should be one mapping state for each changeset.");

                var (mappingState, currentItems) = mappingStateAndItemsEnumerator.Current;
                if (mappingState.Changeset != changeset.ChangesetId)
                    throw new InvalidOperationException("Enumerator and loop are out of sync");

                var branchesWithTopologicalOperations = new List<(BranchIdentity Branch, (int Changeset, BranchIdentity Branch)? AdditionalParent)>();

                foreach (var operation in mappingState.TopologicalOperations)
                {
                    switch (operation)
                    {
                        case BranchOperation branch:
                        {
                            // Don't copy to heads here because the previous head will be removed if not null.
                            branchesWithTopologicalOperations.Add((branch.NewBranch, AdditionalParent: (branch.SourceBranchChangeset, branch.SourceBranch)));
                            break;
                        }

                        case DeleteOperation delete:
                        {
                            if (!heads.Remove(delete.Branch, out var head)) throw new NotImplementedException();
                            repo.Branches.Remove(head);
                            break;
                        }

                        case MergeOperation merge:
                        {
                            branchesWithTopologicalOperations.Add((merge.TargetBranch, AdditionalParent: (merge.SourceBranchChangeset, merge.SourceBranch)));
                            break;
                        }

                        case RenameOperation rename:
                        {
                            if (!heads.Remove(rename.OldIdentity, out var head)) throw new NotImplementedException();
                            heads.Add(rename.NewIdentity, head);

                            branchesWithTopologicalOperations.Add((rename.NewIdentity, AdditionalParent: null));
                            break;
                        }
                    }
                }

                var mappedBranchesInTopologicalOrder = mappingState.BranchMappings.StableTopologicalSort(
                    keySelector: mappingByBranch => mappingByBranch.Key,
                    dependenciesSelector: mappingByBranch => branchesWithTopologicalOperations
                        .Where(b => b.Branch == mappingByBranch.Key)
                        .Select(b => b.AdditionalParent?.Branch)
                        .Values());

                var author = new Signature(authorsLookup[changeset.Author.UniqueName], changeset.CreatedDate);
                var committer = new Signature(authorsLookup[changeset.CheckedInBy.UniqueName], changeset.CreatedDate);
                var message = $"{changeset.Comment}\n\n[Migrated from CS{changeset.ChangesetId}]";
                var commits = new List<(Commit Commit, BranchIdentity Branch)>();

                foreach (var (branch, mapping) in mappedBranchesInTopologicalOrder)
                {
                    var builder = new TreeDefinition();

                    foreach (var item in currentItems)
                    {
                        if (item.IsFolder || item.IsBranch) continue;
                        if (item.IsSymbolicLink) throw new NotImplementedException("Handle symbolic links");

                        if (mappingState.BranchMappings.Keys.Any(otherBranch =>
                            otherBranch != branch
                            && PathUtils.IsOrContains(otherBranch.Path, item.Path)
                            && PathUtils.Contains(mapping.RootDirectory, otherBranch.Path)))
                        {
                            continue;
                        }

                        if (mapping.GetGitRepositoryPath(item.Path) is { } path)
                        {
                            builder.Add(path, dummyBlob, Mode.NonExecutableFile);
                        }
                    }

                    var requireCommit = false;
                    var parents = new List<Commit>();

                    // Workaround: use .NET Core extension method rather than buggy extension method exposed by Microsoft.VisualStudio.Services.Client package.
                    // https://developercommunity.visualstudio.com/content/problem/996912/client-nuget-package-microsoftvisualstudioservices.html
                    var head = CollectionExtensions.GetValueOrDefault(heads, branch);
                    if (head is { }) parents.Add(head.Tip);

                    foreach (var (_, additionalParent) in branchesWithTopologicalOperations.Where(t => t.Branch == branch))
                    {
                        requireCommit = true;

                        if (additionalParent is var (parentChangeset, parentBranch))
                        {
                            if (commitsByChangeset.TryGetValue(parentChangeset, out var createdChangesets)
                                && createdChangesets.SingleOrDefault(c => c.Branch == parentBranch).Commit is { } commit)
                            {
                                parents.Add(commit);
                            }
                            else
                            {
                                throw new InvalidOperationException("Should not be reachable. Earlier code should have sorted topologically or failed.");
                            }
                        }
                    }

                    if (!commits.Any()) commitsByChangeset.Add(changeset.ChangesetId, commits);

                    var tree = repo.ObjectDatabase.CreateTree(builder);

                    if (requireCommit || tree.Sha != head?.Tip.Tree.Sha)
                    {
                        var newBranchName = branch == mappingState.Master ? "master" : GetValidGitBranchName(branch.Path);
                        var commit = repo.ObjectDatabase.CreateCommit(author, committer, message, tree, parents, prettifyMessage: true);

                        commits.Add((commit, branch));

                        // Make sure HEAD is not pointed at a branch
                        repo.Refs.UpdateTarget(repo.Refs.Head, commit.Id);

                        if (head is { }) repo.Branches.Remove(head);
                        heads[branch] = repo.Branches.Add(newBranchName, commit);
                    }
                    else if (head is { })
                    {
                        // Even though there is not a new commit, make it possible to find the commit that should be the
                        // parent commit if the current changeset is a parent changeset.
                        commits.Add((head.Tip, branch));
                    }
                }

                timedProgress.Increment();
            }

            foreach (var (changeset, labels) in labelsByChangeset)
            {
                if (commitsByChangeset.TryGetValue(changeset, out var commits))
                {
                    if (commits.Count > 1)
                        throw new NotImplementedException("TODO: Add branch suffix to tags since same commit was replayed in multiple branches");

                    foreach (var label in labels)
                    {
                        repo.Tags.Add(
                            GetValidGitBranchName(label.Name),
                            commits.Single().Commit,
                            new Signature(authorsLookup[label.Owner.UniqueName], label.ModifiedDate),
                            label.Description);
                    }
                }
            }

            Console.WriteLine($"\rAll {changesets.Count} changesets migrated successfully.");
        }

        private static async Task<ImmutableArray<(int Changeset, ImmutableArray<TfvcLabelRef> Labels)>> GetLabelsByChangesetAsync(
            TfvcHttpClient client,
            string rootPath)
        {
            var labels = await client.GetLabelsAsync(
                new TfvcLabelRequestData
                {
                    MaxItemCount = int.MaxValue,
                    LabelScope = rootPath,
                },
                top: int.MaxValue);

            var changesetsByLabelIndex = await labels
                .SelectAwait(async label => (await client.GetLabelItemsAsync(label.Id.ToString(CultureInfo.InvariantCulture), top: int.MaxValue))
                    .Max(item => item.ChangesetVersion))
                .ToImmutableArrayAsync();

            return changesetsByLabelIndex
                .Zip(labels)
                .GroupBy(pair => pair.First, (changeset, pairs) => (
                    changeset,
                    pairs.Select(p => p.Second).ToImmutableArray()))
                .ToImmutableArray();
        }

        private static void ReportProgress(int changeset, int total, TimedProgress timedProgress)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var timing = new StringBuilder();

                if (timedProgress.GetAverageDuration() is { } avgDuration)
                    timing.Append($", {avgDuration.TotalMilliseconds:n0} ms/changeset");

                if (timedProgress.GetFriendlyEta(total) is { } eta)
                    timing.Append(", ETA ").Append(eta);

                Console.Write($"\rCS{changeset} ({timedProgress.GetPercent(total):p1}{timing})...");
            });
        }

        private static async IAsyncEnumerable<MappingState> EnumerateMappingStatesAsync(
            TfvcHttpClient client,
            ImmutableArray<RootPathChange> rootPathChanges,
            IReadOnlyList<TfvcChangesetRef> changesets,
            BranchIdentity initialBranch)
        {
            var master = initialBranch;

            var branchMappings = ImmutableDictionary.CreateBuilder<BranchIdentity, RepositoryBranchMapping>();
            branchMappings.Add(master, new RepositoryBranchMapping(master.Path, subdirectoryMapping: null));

            var topologyAnalyzer = new TopologyAnalyzer(master, rootPathChanges);

            await using var changesetChangesEnumerator = changesets
                .Skip(1)
                .SelectAwait(changeset => client.GetChangesetChangesAsync(changeset.ChangesetId, top: int.MaxValue - 1))
                .WithLookahead()
                .GetAsyncEnumerator();

            for (var i = 0; i < changesets.Count; i++)
            {
                var changeset = changesets[i];

                var topologicalOperations = ImmutableArray<TopologicalOperation>.Empty;

                if (i > 0)
                {
                    if (!await changesetChangesEnumerator.MoveNextAsync())
                        throw new InvalidOperationException("There should be one element for each changeset except the first.");

                    var changesetChanges = changesetChangesEnumerator.Current;
                    if (changesetChanges.First().Item.ChangesetVersion != changeset.ChangesetId)
                        throw new InvalidOperationException("Enumerator and loop are out of sync");

                    topologicalOperations = topologyAnalyzer.GetTopologicalOperations(changesetChanges).ToImmutableArray();

                    foreach (var operation in topologicalOperations)
                    {
                        switch (operation)
                        {
                            case BranchOperation branch:
                            {
                                var mapping = branchMappings[branch.SourceBranch];

                                if (PathUtils.IsOrContains(branch.SourceBranchPath, mapping.RootDirectory))
                                    mapping = mapping.RenameRootDirectory(branch.SourceBranchPath, branch.NewBranch.Path);

                                branchMappings.Add(branch.NewBranch, mapping);
                                break;
                            }

                            case DeleteOperation delete:
                            {
                                if (!branchMappings.Remove(delete.Branch)) throw new NotImplementedException();
                                break;
                            }

                            case RenameOperation rename:
                            {
                                if (!branchMappings.Remove(rename.OldIdentity, out var mapping)) throw new NotImplementedException();
                                branchMappings.Add(rename.NewIdentity, mapping.RenameRootDirectory(rename.OldIdentity.Path, rename.NewIdentity.Path));

                                if (master == rename.OldIdentity) master = rename.NewIdentity;
                                break;
                            }
                        }
                    }
                }

                yield return new MappingState(
                    changeset.ChangesetId,
                    topologicalOperations,
                    master,
                    branchMappings.ToImmutable());
            }
        }

        private static ImmutableDictionary<string, Identity> LoadAuthors(string authorsPath)
        {
            var builder = ImmutableDictionary.CreateBuilder<string, Identity>(StringComparer.OrdinalIgnoreCase);

            using (var reader = File.OpenText(authorsPath))
            {
                while (reader.ReadLine() is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var equalsIndex = line.IndexOf('=');
                    if (equalsIndex == -1) throw new NotImplementedException("Missing '=' in authors file");

                    var tfvcIdentity = line.AsSpan(0, equalsIndex).Trim().ToString();

                    var gitIdentity = line.AsSpan(equalsIndex + 1).Trim();
                    var openingAngleBracketIndex = gitIdentity.IndexOf('<');
                    if (openingAngleBracketIndex == -1) throw new NotImplementedException("Missing '<' in authors file");
                    if (gitIdentity[^1] != '>') throw new NotImplementedException("Line does not end with '>' in authors file");

                    var name = gitIdentity[..openingAngleBracketIndex].TrimEnd().ToString();
                    var email = gitIdentity[(openingAngleBracketIndex + 1)..^1].Trim().ToString();

                    builder.Add(tfvcIdentity, new Identity(name, email));
                }
            }

            return builder.ToImmutable();
        }

        private static async Task<ImmutableArray<TfvcItem>> DownloadItemsAsync(TfvcHttpClient client, IEnumerable<string> scopePaths, int changeset)
        {
            var union = PathUtils.GetNonOverlappingPaths(scopePaths);
            if (union.IsEmpty) return ImmutableArray<TfvcItem>.Empty;

            var version = new TfvcVersionDescriptor(
                TfvcVersionOption.None,
                TfvcVersionType.Changeset,
                changeset.ToString(CultureInfo.InvariantCulture));

            var results = await Task.WhenAll(union.Select(scopePath =>
                client.GetItemsAsync(scopePath, VersionControlRecursionType.Full, versionDescriptor: version)));

            return results.SelectMany(list => list).ToImmutableArray();
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
