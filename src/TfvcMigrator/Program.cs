using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Globalization;
using System.Text;
using LibGit2Sharp;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using TfvcMigrator.Operations;

namespace TfvcMigrator;

public static class Program
{
    public static Task<int> Main(string[] args)
    {
        var command = new RootCommand("Migrates TFVC source history to idiomatic Git history while preserving branch topology.")
        {
            new Argument<Uri>("project-collection-url") { Description = "The URL of the Azure DevOps project collection." },
            new Argument<string>("root-path") { Description = "The source path within the TFVC repository to migrate as a Git repository." },
            new Option<string>("--authors")
            {
                IsRequired = true,
                Description = "Path to an authors file with lines mapping TFVC usernames to Git authors, e.g.: DOMAIN\\John = John Doe <john@doe.com>",
            },
            new Option<string?>("--out-dir") { Description = "The directory path at which to create a new Git repository. Defaults to the last segment in the root path under the current directory." },
            new Option<int?>("--min-changeset") { Description = "The changeset defining the initial commit. Defaults to the first changeset under the given source path." },
            new Option<int?>("--max-changeset") { Description = "The last changeset to migrate. Defaults to the most recent changeset under the given source path." },
            new Option<ImmutableArray<RootPathChange>>(
                "--root-path-changes",
                parseArgument: result => result.Tokens.Select(token => ParseRootPathChange(token.Value)).ToImmutableArray())
            {
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true,
                Description = "Followed by one or more arguments with the format CS1234:$/New/Path. Changes the path that is mapped as the Git repository root to a new path during a specified changeset.",
            },
            new Option<string?>("--pat") { Description = "Personal access token, required to access TFVC repositories hosted on Azure DevOps Services. If not provided, default client credentials will be used which are only suitable for repositories hosted on Azure DevOps Server on-premises." },
        };

        command.Handler = CommandHandler.Create(CommandVerifier.Intercept(MigrateAsync));

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

    public static async Task<int> MigrateAsync(
        Uri projectCollectionUrl,
        string rootPath,
        string authors,
        string? outDir = null,
        int? minChangeset = null,
        int? maxChangeset = null,
        ImmutableArray<RootPathChange> rootPathChanges = default,
        string? pat = null)
    {
        if (rootPathChanges.IsDefault) rootPathChanges = ImmutableArray<RootPathChange>.Empty;

        var outputDirectory = Path.GetFullPath(
            new[] { outDir, PathUtils.GetLeaf(rootPath), projectCollectionUrl.Segments.LastOrDefault() }
                .First(name => !string.IsNullOrEmpty(name))!);

        using var repo = InitRepository(outputDirectory);
        if (repo is null)
            return 1;

        var authorsLookup = LoadAuthors(authors);

        Console.WriteLine("Connecting...");

        using var connection = new VssConnection(
            projectCollectionUrl,
            pat is not null
                ? new VssBasicCredential(userName: null, password: pat)
                : new VssCredentials());

        using var client = await connection.GetClientAsync<TfvcHttpClient>();

        Console.WriteLine("Downloading changeset and label metadata...");

        var (changesets, allLabels) = await (
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
            client.GetLabelsAsync(
                new TfvcLabelRequestData
                {
                    MaxItemCount = int.MaxValue,
                    LabelScope = rootPath,
                },
                top: int.MaxValue)
        ).ConfigureAwait(false);

        var unmappedAuthors = changesets.Select(c => c.Author)
            .Concat(changesets.Select(c => c.CheckedInBy))
            .Concat(allLabels.Select(l => l.Owner))
            .Select(identity => identity.UniqueName)
            .Where(name => !authorsLookup.ContainsKey(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unmappedAuthors.Any())
        {
            Console.WriteLine("An entry must be added to the authors file for each of the following TFVC users:");
            foreach (var user in unmappedAuthors)
                Console.WriteLine(user);
            return 1;
        }

        Console.WriteLine("Downloading changesets and converting to commits...");

        var labelsByChangesetTask = GetLabelsByChangesetAsync(client, allLabels);

        var emptyBlob = new Lazy<Blob>(() => repo.ObjectDatabase.CreateBlob(Stream.Null));

        var initialBranch = new BranchIdentity(changesets.First().ChangesetId, rootPath);

        var heads = new Dictionary<BranchIdentity, Branch>();

        var timedProgress = TimedProgress.Start();

        var downloadedBlobsByHash = new Dictionary<string, Blob>();
        var commitsByChangeset = new Dictionary<int, List<(Commit Commit, BranchIdentity Branch, bool WasCreatedForChangeset)>>();

        await using var mappingStateAndItemsEnumerator =
            EnumerateMappingStatesAsync(client, rootPathChanges, changesets, initialBranch)
                .SelectAwait(async state => (
                    MappingState: state,
                    // Make no attempt to reason about applying TFS item changes over time. Ask for the full set of files.
                    Items: await DownloadItemsAsync(
                        client,
                        PathUtils.GetNonOverlappingPaths(
                            state.BranchMappingsInDependentOperationOrder.Select(branchMapping => branchMapping.Mapping.RootDirectory)),
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

            var mappedItems = MapItemsToDownloadSources(mappingState.BranchMappingsInDependentOperationOrder, currentItems);

            var toDownload = mappedItems.Values
                .SelectMany(items => items, (_, item) => item.DownloadSource)
                .Where(source => source.Size > 0 && !downloadedBlobsByHash.ContainsKey(source.HashValue))
                .GroupBy(source => source.HashValue, (_, sources) => sources.First())
                .ToImmutableArray();

            if (toDownload.Any())
            {
                var results = await toDownload.SelectAwaitParallel(
                    async source =>
                    {
                        var versionDescriptor = new TfvcVersionDescriptor(
                            TfvcVersionOption.None,
                            TfvcVersionType.Changeset,
                            source.ChangesetVersion.ToString(CultureInfo.InvariantCulture));

                        await using var stream = await client.GetItemContentAsync(source.Path, versionDescriptor: versionDescriptor).ConfigureAwait(false);

                        Blob blob;
                        lock (downloadedBlobsByHash)
                            blob = repo.ObjectDatabase.CreateBlob(stream);

                        if (blob.Size != source.Size)
                            throw new NotImplementedException("Download stream length does not match expected file size.");

                        if (!blob.IsBinary)
                        {
                            await using var blobStream = (UnmanagedMemoryStream)blob.GetContentStream();
                            await using var renormalizedStream = Utils.RenormalizeCrlfIfNeeded(blobStream);

                            if (renormalizedStream is not null)
                            {
                                lock (downloadedBlobsByHash)
                                    blob = repo.ObjectDatabase.CreateBlob(renormalizedStream);
                            }
                        }

                        return (source.HashValue, blob);
                    },
                    degreeOfParallelism: 10,
                    CancellationToken.None).ConfigureAwait(false);

                foreach (var (hash, blob) in results)
                    downloadedBlobsByHash.Add(hash, blob);
            }

            foreach (var operation in mappingState.TopologicalOperations)
            {
                switch (operation)
                {
                    case DeleteOperation delete:
                    {
                        if (!heads.Remove(delete.Branch, out var head)) throw new NotImplementedException();
                        repo.Branches.Remove(head);
                        break;
                    }

                    case RenameOperation rename:
                    {
                        if (!heads.Remove(rename.OldIdentity, out var head)) throw new NotImplementedException();
                        heads.Add(rename.NewIdentity, head);
                        break;
                    }
                }
            }

            var author = new Signature(authorsLookup[changeset.Author.UniqueName], changeset.CreatedDate);
            var committer = new Signature(authorsLookup[changeset.CheckedInBy.UniqueName], changeset.CreatedDate);
            var message = $"{changeset.Comment}\n\n[Migrated from CS{changeset.ChangesetId}]";
            var commits = new List<(Commit Commit, BranchIdentity Branch, bool WasCreatedForChangeset)>();

            foreach (var (branch, _) in mappingState.BranchMappingsInDependentOperationOrder)
            {
                var builder = new TreeDefinition();

                foreach (var (gitRepositoryPath, downloadSource) in mappedItems[branch])
                {
                    var blob = downloadSource.Size > 0
                        ? downloadedBlobsByHash[downloadSource.HashValue]
                        : emptyBlob.Value;

                    builder.Add(gitRepositoryPath, blob, Mode.NonExecutableFile);
                }

                var parents = new List<Commit>();

                // Workaround: use .NET Core extension method rather than buggy extension method exposed by Microsoft.VisualStudio.Services.Client package.
                // https://developercommunity.visualstudio.com/content/problem/996912/client-nuget-package-microsoftvisualstudioservices.html
                var head = CollectionExtensions.GetValueOrDefault(heads, branch);
                if (head is not null) parents.Add(head.Tip);

                foreach (var (_, parentChangeset, parentBranch) in mappingState.AdditionalParents.Where(t => t.Branch == branch))
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

                if (!commits.Any()) commitsByChangeset.Add(changeset.ChangesetId, commits);

                var tree = repo.ObjectDatabase.CreateTree(builder);

                var requireCommit = mappingState.TopologicalOperations.Any(operation =>
                    branch == (
                        (operation as MergeOperation)?.TargetBranch
                        ?? (operation as BranchOperation)?.NewBranch
                        ?? (operation as RenameOperation)?.NewIdentity));

                if (requireCommit || tree.Sha != head?.Tip.Tree.Sha)
                {
                    var newBranchName = branch == mappingState.Trunk ? "main" : GetValidGitBranchName(branch.Path);
                    var commit = repo.ObjectDatabase.CreateCommit(author, committer, message, tree, parents, prettifyMessage: true);

                    commits.Add((commit, branch, WasCreatedForChangeset: true));

                    // Make sure HEAD is not pointed at a branch
                    repo.Refs.UpdateTarget(repo.Refs.Head, commit.Id);

                    if (head is not null) repo.Branches.Remove(head);
                    heads[branch] = repo.Branches.Add(newBranchName, commit);
                }
                else if (head is not null)
                {
                    // Even though there is not a new commit, make it possible to find the commit that should be the
                    // parent commit if the current changeset is a parent changeset.
                    commits.Add((head.Tip, branch, WasCreatedForChangeset: false));
                }
            }

            timedProgress.Increment();
        }

        foreach (var (changeset, labels) in await labelsByChangesetTask.ConfigureAwait(false))
        {
            if (commitsByChangeset.TryGetValue(changeset, out var commits))
            {
                var commitsCreatedForChangeset = commits.Where(c => c.WasCreatedForChangeset).ToList();

                foreach (var (commit, branch, _) in commitsCreatedForChangeset)
                {
                    foreach (var label in labels)
                    {
                        repo.Tags.Add(
                            GetValidGitBranchName(commitsCreatedForChangeset.Count > 1
                                ? label.Name + '-' + PathUtils.GetLeaf(branch.Path)
                                : label.Name),
                            commit,
                            new Signature(authorsLookup[label.Owner.UniqueName], label.ModifiedDate),
                            label.Description);
                    }
                }
            }
        }

        Console.WriteLine($"\rAll {changesets.Count} changesets migrated successfully.");
        return 0;
    }

    private static Repository? InitRepository(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var existingFileSystemEntries = Directory.GetFileSystemEntries(outputDirectory);
        if (!existingFileSystemEntries.Any())
            return new Repository(Repository.Init(outputDirectory));

        if (existingFileSystemEntries is not [var singleFileSystemEntry]
            || !".git".Equals(Path.GetFileName(singleFileSystemEntry), StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Cannot create Git repository at {outputDirectory} because the directory is not empty.");
            return null;
        }

        var repository = new Repository(singleFileSystemEntry);
        if (repository.ObjectDatabase.Any())
        {
            repository.Dispose();
            Console.WriteLine($"A Git repository at {outputDirectory} already exists and is not empty.");
            return null;
        }

        return repository;
    }

    private static ImmutableDictionary<BranchIdentity, ImmutableArray<(string GitRepositoryPath, TfvcItem DownloadSource)>> MapItemsToDownloadSources(
        ImmutableArray<(BranchIdentity Branch, RepositoryBranchMapping Mapping)> branchMappingsInDependentOperationOrder,
        ImmutableArray<TfvcItem> currentItems)
    {
        var builder = ImmutableDictionary.CreateBuilder<BranchIdentity, ImmutableArray<(string GitRepositoryPath, TfvcItem DownloadSource)>>();

        var itemsBuilder = ImmutableArray.CreateBuilder<(string GitRepositoryPath, TfvcItem DownloadSource)>();
        var itemPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (branch, mapping) in branchMappingsInDependentOperationOrder)
        {
            foreach (var item in currentItems)
            {
                if (item.IsFolder || item.IsBranch) continue;
                if (item.IsSymbolicLink) throw new NotImplementedException("Handle symbolic links");

                if (branchMappingsInDependentOperationOrder.Any(other =>
                    other.Branch != branch
                    && PathUtils.IsOrContains(other.Branch.Path, item.Path)
                    && PathUtils.Contains(mapping.RootDirectory, other.Branch.Path)))
                {
                    continue;
                }

                if (mapping.GetGitRepositoryPath(item.Path) is { } path)
                {
                    if (!itemPaths.Add(path))
                        throw new InvalidOperationException("The same Git repository path is being added with two different TFVC sources.");

                    itemsBuilder.Add((path, item));
                }
            }

            builder.Add(branch, itemsBuilder.ToImmutable());
            itemsBuilder.Clear();
            itemPaths.Clear();
        }

        return builder.ToImmutable();
    }

    private static async Task<ImmutableArray<(int Changeset, ImmutableArray<TfvcLabelRef> Labels)>> GetLabelsByChangesetAsync(
        TfvcHttpClient client,
        List<TfvcLabelRef> allLabels)
    {
        var changesetsByLabelIndex = await allLabels
            .SelectAwait(async label => (await client.GetLabelItemsAsync(label.Id.ToString(CultureInfo.InvariantCulture), top: int.MaxValue))
                .Max(item => item.ChangesetVersion))
            .ToImmutableArrayAsync();

        return changesetsByLabelIndex
            .Zip(allLabels)
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
        var trunk = initialBranch;

        var branchMappings = ImmutableDictionary.CreateBuilder<BranchIdentity, RepositoryBranchMapping>();
        branchMappings.Add(trunk, new RepositoryBranchMapping(trunk.Path, subdirectoryMapping: null));

        var topologyAnalyzer = new TopologyAnalyzer(trunk, rootPathChanges);

        await using var changesetChangesEnumerator = changesets
            .Skip(1)
            .SelectAwait(changeset => client.GetChangesetChangesAsync(changeset.ChangesetId, top: int.MaxValue - 1))
            .WithLookahead()
            .GetAsyncEnumerator();

        var additionalParents = ImmutableArray.CreateBuilder<(BranchIdentity Branch, int ParentChangeset, BranchIdentity ParentBranch)>();

        for (var i = 0; i < changesets.Count; i++)
        {
            var changeset = changesets[i];
            var topologicalOperations = ImmutableArray<TopologicalOperation>.Empty;
            additionalParents.Clear();

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
                            additionalParents.Add((branch.NewBranch, branch.SourceBranchChangeset, branch.SourceBranch));

                            var mapping = branchMappings[branch.SourceBranch];

                            mapping = PathUtils.IsOrContains(branch.SourceBranchPath, mapping.RootDirectory)
                                ? mapping.RenameRootDirectory(branch.SourceBranchPath, branch.NewBranch.Path)
                                : mapping.WithSubdirectoryMapping(branch.NewBranch.Path, branch.SourceBranchPath);

                            branchMappings.Add(branch.NewBranch, mapping);
                            break;
                        }

                        case MergeOperation merge:
                        {
                            additionalParents.Add((merge.TargetBranch, merge.SourceBranchChangeset, merge.SourceBranch));
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

                            if (trunk == rename.OldIdentity) trunk = rename.NewIdentity;
                            break;
                        }
                    }
                }
            }

            var branchMappingsInTopologicalOrder = branchMappings
                .Select(mappingsByBranch => (Branch: mappingsByBranch.Key, Mapping: mappingsByBranch.Value))
                .StableTopologicalSort(
                    keySelector: mappingByBranch => mappingByBranch.Branch,
                    dependenciesSelector: mappingByBranch => additionalParents
                        .Where(b => b.Branch == mappingByBranch.Branch)
                        .Select(b => b.ParentBranch))
                .ToImmutableArray();

            yield return new MappingState(
                changeset.ChangesetId,
                topologicalOperations,
                additionalParents.ToImmutable(),
                trunk,
                branchMappingsInTopologicalOrder);
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
