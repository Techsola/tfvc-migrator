namespace TfvcMigrator;

public readonly struct RepositoryBranchMapping
{
    public RepositoryBranchMapping(string rootDirectory, (string BranchDirectory, string TargetDirectory)? subdirectoryMapping)
    {
        if (!PathUtils.IsAbsolute(rootDirectory))
            throw new ArgumentException("An absolute root directory path must be specified.", nameof(rootDirectory));

        if (subdirectoryMapping is var (branch, target))
        {
            if (!PathUtils.IsAbsolute(branch))
                throw new ArgumentException("Branch directory path must be absolute.", nameof(subdirectoryMapping));

            if (!PathUtils.IsAbsolute(target))
                throw new ArgumentException("Target directory path must be absolute.", nameof(subdirectoryMapping));

            if (!PathUtils.Contains(rootDirectory, branch))
                throw new ArgumentException("Branch directory path must be a subdirectory of the root directory.", nameof(subdirectoryMapping));

            if (!PathUtils.Contains(rootDirectory, target))
                throw new ArgumentException("Target directory path must be a subdirectory of the root directory.", nameof(subdirectoryMapping));

            if (PathUtils.Overlaps(branch, target))
                throw new ArgumentException("Branch and target directories must not overlap.", nameof(subdirectoryMapping));
        }

        RootDirectory = rootDirectory;
        SubdirectoryMapping = subdirectoryMapping;
    }

    /// <summary>
    /// Absolute path to the TFVC directory that becomes the repository root in the Git commits that are created
    /// using this mapping.
    /// </summary>
    public string RootDirectory { get; }

    /// <summary>
    /// Absolute paths to the branch subdirectory (if there is one) and the branch target directory. Paths under the
    /// branch subdirectory are mapped to the branch target directory in the Git commits that are created using this
    /// mapping.
    /// </summary>
    public (string BranchDirectory, string TargetDirectory)? SubdirectoryMapping { get; }

    public RepositoryBranchMapping RenameRootDirectory(string oldPath, string newPath)
    {
        if (!PathUtils.IsAbsolute(oldPath))
            throw new ArgumentException("Old path must be absolute.", nameof(oldPath));

        if (!PathUtils.IsAbsolute(newPath))
            throw new ArgumentException("New path must be absolute.", nameof(newPath));

        if (!PathUtils.IsOrContains(oldPath, RootDirectory))
            throw new InvalidOperationException("The rename does not apply to this mapping.");

        if (SubdirectoryMapping is not null)
            throw new NotImplementedException("Research: Renaming and branching might behave differently when subdirectory mapping is involved.");

        return new RepositoryBranchMapping(
            PathUtils.ReplaceContainingPath(RootDirectory, oldPath, newPath),
            subdirectoryMapping: null);
    }

    public RepositoryBranchMapping WithSubdirectoryMapping(string branchDirectory, string targetDirectory)
    {
        return new RepositoryBranchMapping(RootDirectory, subdirectoryMapping: (branchDirectory, targetDirectory));
    }

    public string? GetGitRepositoryPath(string itemPath)
    {
        if (!PathUtils.IsAbsolute(itemPath))
            throw new ArgumentException("Item path must be absolute.", nameof(itemPath));

        if (SubdirectoryMapping is var (branch, target))
        {
            if (PathUtils.IsOrContains(itemPath, target))
                return null;

            if (PathUtils.IsOrContains(branch, itemPath))
                itemPath = PathUtils.ReplaceContainingPath(itemPath, branch, target);
        }

        return PathUtils.IsOrContains(RootDirectory, itemPath)
            ? PathUtils.RemoveContainingPath(itemPath, RootDirectory)
            : null;
    }
}
