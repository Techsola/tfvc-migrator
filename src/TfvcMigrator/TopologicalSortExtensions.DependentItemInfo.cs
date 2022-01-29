namespace TfvcMigrator;

internal static partial class TopologicalSortExtensions
{
    private readonly struct DependentItemInfo<TItem, TKey>
    {
        public TItem Item { get; }
        public TKey Key { get; }
        public List<TKey> OutstandingDependencies { get; }

        public DependentItemInfo(TItem item, TKey key, List<TKey> outstandingDependencies)
        {
            Item = item;
            Key = key;
            OutstandingDependencies = outstandingDependencies;
        }

        public bool RemoveOutstandingDependency(TKey dependency, IEqualityComparer<TKey> comparer)
        {
            for (var i = 0; i < OutstandingDependencies.Count; i++)
            {
                if (comparer.Equals(dependency, OutstandingDependencies[i]))
                {
                    OutstandingDependencies.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }
    }
}
