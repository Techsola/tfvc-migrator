namespace TfvcMigrator
{
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct BranchIdentity : IEquatable<BranchIdentity>
    {
        public BranchIdentity(int creationChangeset, string path)
        {
            if (creationChangeset < 1)
                throw new ArgumentOutOfRangeException(nameof(creationChangeset), creationChangeset, "A valid changeset must be provided.");

            if (path.Length < 3 || path[0] != '$' || path[1] != '/')
                throw new ArgumentException("The full path to a branch must be specified.", nameof(path));

            CreationChangeset = creationChangeset;
            Path = path;
        }

        public int CreationChangeset { get; }
        public string Path { get; }

        public bool IsOrContains(string path) => PathUtils.IsOrContains(Path, path);

        public bool Contains(string path)
        {
            return path.Length > Path.Length + 1
               && path.StartsWith(Path, StringComparison.OrdinalIgnoreCase)
               && path[Path.Length] == '/';
        }

        public override string ToString()
        {
            return $"CS{CreationChangeset}:{Path}";
        }

        public override bool Equals(object? obj)
        {
            return obj is BranchIdentity identity && Equals(identity);
        }

        public bool Equals(BranchIdentity other)
        {
            return CreationChangeset == other.CreationChangeset
                && Path.Equals(other.Path, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(CreationChangeset, Path.GetHashCode(StringComparison.OrdinalIgnoreCase));
        }

        public static bool operator ==(BranchIdentity left, BranchIdentity right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(BranchIdentity left, BranchIdentity right)
        {
            return !(left == right);
        }
    }
}
