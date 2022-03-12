namespace TfvcMigrator;

[DebuggerDisplay("{ToString(),nq}")]
public sealed class RootPathChange : IEquatable<RootPathChange?>
{
    public RootPathChange(int changeset, string newSourceRootPath)
    {
        if (changeset < 1)
            throw new ArgumentOutOfRangeException(nameof(changeset), changeset, "Changeset must be greater than zero.");

        if (!PathUtils.IsAbsolute(newSourceRootPath))
            throw new ArgumentException("An absolute root source path must be specified.", nameof(newSourceRootPath));

        Changeset = changeset;
        NewSourceRootPath = newSourceRootPath;
    }

    public int Changeset { get; }
    public string NewSourceRootPath { get; }

    public override bool Equals(object? obj)
    {
        return Equals(obj as RootPathChange);
    }

    public bool Equals(RootPathChange? other)
    {
        return other != null &&
               Changeset == other.Changeset &&
               NewSourceRootPath == other.NewSourceRootPath;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Changeset, NewSourceRootPath);
    }

    public override string ToString() => $"CS{Changeset}:{NewSourceRootPath}";
}
