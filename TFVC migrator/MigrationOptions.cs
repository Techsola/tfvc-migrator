using System;
using System.Collections.Generic;

namespace TfvcMigrator
{
    public sealed class MigrationOptions
    {
        public MigrationOptions(Uri collectionBaseUrl, string rootSourcePath)
        {
            if (!PathUtils.IsAbsolute(rootSourcePath))
                throw new ArgumentException("An absolute root source path must be specified.", nameof(rootSourcePath));

            CollectionBaseUrl = collectionBaseUrl ?? throw new ArgumentNullException(nameof(collectionBaseUrl));
            RootSourcePath = rootSourcePath;
        }

        public Uri CollectionBaseUrl { get; }
        public string RootSourcePath { get; }

        private int? minChangeset;
        public int? MinChangeset
        {
            get => minChangeset;
            set => minChangeset = value < 1
                ? throw new ArgumentOutOfRangeException(nameof(value), value, "The minimum changeset must be null or greater than zero.")
                : value;
        }

        private int? maxChangeset;
        public int? MaxChangeset
        {
            get => maxChangeset;
            set => maxChangeset = value < 1
                ? throw new ArgumentOutOfRangeException(nameof(value), value, "The maximum changeset must be null or greater than zero.")
                : value;
        }

        public ICollection<RootPathChange> RootPathChanges { get; } = new List<RootPathChange>();
    }
}
