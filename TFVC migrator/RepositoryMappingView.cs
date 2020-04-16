using System;
using System.Collections.Immutable;
using System.Linq;

namespace TfvcMigrator
{
    public sealed class RepositoryMappingView
    {
        public RepositoryMappingView(string rootDirectory)
            : this(rootDirectory, remappedDirectories: ImmutableArray<(string, string?)>.Empty)
        {
            if (!PathUtils.IsAbsolute(rootDirectory))
                throw new ArgumentException("An absolute root directory path must be specified.", nameof(rootDirectory));
        }

        private RepositoryMappingView(string rootDirectory, ImmutableArray<(string ActualTfvcPath, string? MappedTfvcPath)> remappedDirectories)
        {
            RootDirectory = rootDirectory;
            RemappedDirectories = remappedDirectories;
        }

        /// <summary>
        /// Absolute path to the TFVC directories that becomes the repository root in the Git commits that are created
        /// using this mapping.
        /// </summary>
        public string RootDirectory { get; }

        /// <summary>
        /// Absolute paths to TFVC directories that are excluded or renamed in the Git commits that are created using
        /// this mapping.
        /// </summary>
        public ImmutableArray<(string ActualTfvcPath, string? MappedTfvcPath)> RemappedDirectories { get; }

        public RepositoryMappingView WithRootDirectory(string rootDirectory)
        {
            if (!PathUtils.IsAbsolute(rootDirectory))
                throw new ArgumentException("An absolute root directory path must be specified.", nameof(rootDirectory));

            return new RepositoryMappingView(rootDirectory, RemappedDirectories);
        }

        public RepositoryMappingView AddDirectoryMapping(string actualTfvcPath, string? mappedTfvcPath)
        {
            if (!PathUtils.IsAbsolute(actualTfvcPath))
                throw new ArgumentException("Actual directory path must be absolute.", nameof(actualTfvcPath));

            if (mappedTfvcPath is { } && !PathUtils.IsAbsolute(mappedTfvcPath))
                throw new ArgumentException("Mapped directory path must be absolute.", nameof(mappedTfvcPath));

            if (RemappedDirectories.Any(d => PathUtils.Overlaps(actualTfvcPath, d.ActualTfvcPath)))
                throw new NotImplementedException("Handle overlapping mappings");

            return new RepositoryMappingView(
                RootDirectory,
                RemappedDirectories.Add((actualTfvcPath, mappedTfvcPath)));
        }

        public RepositoryMappingView RemoveDirectoryMapping(string actualTfvcPath)
        {
            if (!PathUtils.IsAbsolute(actualTfvcPath))
                throw new ArgumentException("Actual directory path must be absolute.", nameof(actualTfvcPath));

            var index = RemappedDirectories.FindSingleIndex(d => d.ActualTfvcPath.Equals(actualTfvcPath, StringComparison.OrdinalIgnoreCase));
            if (index == -1)
                throw new ArgumentException("The specified mapping does not exist.", nameof(actualTfvcPath));

            return new RepositoryMappingView(
                RootDirectory,
                RemappedDirectories.RemoveAt(index));
        }

        public string? GetGitRepositoryPath(string itemPath)
        {
            if (!PathUtils.IsAbsolute(itemPath))
                throw new ArgumentException("Item path must be absolute.", nameof(itemPath));

            foreach (var (actualPath, mappedPath) in RemappedDirectories)
            {
                if (PathUtils.IsOrContains(actualPath, itemPath))
                {
                    if (mappedPath is null) return null;
                    itemPath = PathUtils.ReplaceContainingPath(itemPath, actualPath, mappedPath);
                    break;
                }
            }

            return PathUtils.IsOrContains(RootDirectory, itemPath)
                ? PathUtils.RemoveContainingPath(itemPath, RootDirectory)
                : null;
        }
    }
}
