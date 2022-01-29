namespace TfvcMigrator
{
    [DebuggerDisplay("{ToString(),nq}")]
    public sealed class RootPathChange
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

        public override string ToString() => $"CS{Changeset}:{NewSourceRootPath}";
    }
}
