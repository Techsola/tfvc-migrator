using LibGit2Sharp;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics.CodeAnalysis;
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

            var unmappedAuthors = changesets.Select(c => c.Author)
                .Concat(changesets.Select(c => c.CheckedInBy))
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

            var dummyBlob = repo.ObjectDatabase.CreateBlob(Stream.Null);

            var master = new BranchIdentity(changesets.First().ChangesetId, rootPath);

            var mappings = new Dictionary<BranchIdentity, RepositoryBranchMapping>
            {
                [master] = new RepositoryBranchMapping(master.Path, subdirectoryMapping: null),
            };

            var heads = new Dictionary<BranchIdentity, Branch>();

            var topologyAnalyzer = new TopologyAnalyzer(master, rootPathChanges);

            var completedChangesetCount = 0;

            foreach (var changeset in changesets)
            {
                Console.Write($"\rDownloading CS{changeset.ChangesetId} ({completedChangesetCount / (double)changesets.Count:p1})...");

                var changesetChanges = await client.GetChangesetChangesAsync(changeset.ChangesetId, top: int.MaxValue - 1);

                var hasTopologicalOperation = new List<(BranchIdentity Branch, Commit? AdditionalParent)>();

                if (completedChangesetCount > 0)
                {
                    foreach (var operation in topologyAnalyzer.GetTopologicalOperations(changesetChanges))
                    {
                        switch (operation)
                        {
                            case BranchOperation branch:
                            {
                                heads.Add(branch.NewBranch, heads[branch.SourceBranch]);

                                var mapping = mappings[branch.SourceBranch];

                                if (PathUtils.IsOrContains(branch.SourceBranchPath, mapping.RootDirectory))
                                    mapping = mapping.RenameRootDirectory(branch.SourceBranchPath, branch.NewBranch.Path);

                                mappings.Add(branch.NewBranch, mapping);

                                hasTopologicalOperation.Add((branch.NewBranch, AdditionalParent: null));
                                break;
                            }

                            case DeleteOperation delete:
                            {
                                if (!heads.Remove(delete.Branch, out var head)) throw new NotImplementedException();
                                repo.Branches.Remove(head);

                                if (!mappings.Remove(delete.Branch)) throw new NotImplementedException();
                                break;
                            }

                            case MergeOperation merge:
                            {
                                hasTopologicalOperation.Add((merge.TargetBranch, AdditionalParent: heads[merge.SourceBranch].Tip));
                                break;
                            }

                            case RenameOperation rename:
                            {
                                if (!heads.Remove(rename.OldIdentity, out var head)) throw new NotImplementedException();
                                heads.Add(rename.NewIdentity, head);

                                if (!mappings.Remove(rename.OldIdentity, out var mapping)) throw new NotImplementedException();
                                mappings.Add(rename.NewIdentity, mapping.RenameRootDirectory(rename.OldIdentity.Path, rename.NewIdentity.Path));

                                hasTopologicalOperation.Add((rename.NewIdentity, AdditionalParent: null));

                                if (master == rename.OldIdentity) master = rename.NewIdentity;
                                break;
                            }
                        }
                    }
                }

                // Make no attempt to reason about applying TFS item changes over time. Ask for the full set of files.
                var currentItems = await DownloadItemsAsync(
                    client,
                    mappings.Values.Select(mapping => mapping.RootDirectory),
                    changeset.ChangesetId);

                var author = new Signature(authorsLookup[changeset.Author.UniqueName], changeset.CreatedDate);
                var committer = new Signature(authorsLookup[changeset.CheckedInBy.UniqueName], changeset.CreatedDate);
                var message = changeset.Comment + "\n\nMigrated from CS" + changeset.ChangesetId;

                foreach (var (branch, mapping) in mappings.ToList())
                {
                    var builder = new TreeDefinition();

                    foreach (var item in currentItems)
                    {
                        if (item.IsFolder || item.IsBranch) continue;
                        if (item.IsSymbolicLink) throw new NotImplementedException("Handle symbolic links");

                        if (mappings.Keys.Any(otherBranch =>
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

                    foreach (var (_, additionalParent) in hasTopologicalOperation.Where(t => t.Branch == branch))
                    {
                        requireCommit = true;
                        if (additionalParent is { }) parents.Add(additionalParent);
                    }

                    var tree = repo.ObjectDatabase.CreateTree(builder);

                    if (requireCommit || tree.Sha != head?.Tip.Tree.Sha)
                    {
                        var newBranchName = branch == master ? "master" : GetValidGitBranchName(branch.Path);
                        var commit = repo.ObjectDatabase.CreateCommit(author, committer, message, tree, parents, prettifyMessage: true);

                        // Make sure HEAD is not pointed at a branch
                        repo.Refs.UpdateTarget(repo.Refs.Head, commit.Id);

                        if (head is { }) repo.Branches.Remove(head);
                        heads[branch] = repo.Branches.Add(newBranchName, commit);
                    }
                }

                completedChangesetCount++;
            }

            Console.WriteLine($"\rAll {completedChangesetCount} changesets migrated successfully.");
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
            var version = new TfvcVersionDescriptor(
                TfvcVersionOption.None,
                TfvcVersionType.Changeset,
                changeset.ToString(CultureInfo.InvariantCulture));

            var results = await Task.WhenAll(
                GetNonOverlappingPaths(scopePaths).Select(scopePath =>
                     client.GetItemsAsync(scopePath, VersionControlRecursionType.Full, versionDescriptor: version)));

            return results.SelectMany(list => list).ToImmutableArray();
        }

        private static ImmutableArray<string> GetNonOverlappingPaths(IEnumerable<string> paths)
        {
            var nonOverlappingPaths = ImmutableArray.CreateBuilder<string>();

            foreach (var path in paths)
            {
                if (!PathUtils.IsAbsolute(path))
                    throw new ArgumentException("Paths must be absolute.", nameof(paths));

                if (nonOverlappingPaths.Any(previous => PathUtils.IsOrContains(previous, path)))
                    continue;

                for (var i = nonOverlappingPaths.Count - 1; i >= 0; i--)
                {
                    if (PathUtils.Contains(path, nonOverlappingPaths[i]))
                        nonOverlappingPaths.RemoveAt(i);
                }

                nonOverlappingPaths.Add(path);
            }

            return nonOverlappingPaths.ToImmutable();
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
