namespace TfvcMigrator
{
    internal static partial class TopologicalSortExtensions
    {
        public static IEnumerable<TItem> StableTopologicalSort<TItem, TKey>(
            this IEnumerable<TItem> items,
            Func<TItem, TKey> keySelector,
            Func<TItem, IEnumerable<TKey>> dependenciesSelector,
            IEqualityComparer<TKey>? comparer = null)
            where TKey : notnull
        {
            comparer ??= EqualityComparer<TKey>.Default;
            var returnedItems = new HashSet<TKey>(comparer);

            var dependentItemsByDependency = new Dictionary<TKey, List<DependentItemInfo<TItem, TKey>>>(comparer);

            foreach (var item in items)
            {
                var dependencies = dependenciesSelector.Invoke(item);

                if (dependencies != null)
                {
                    var itemDependencyList = dependencies.Where(depItem => depItem != null && !returnedItems.Contains(depItem)).ToList();
                    if (itemDependencyList.Any())
                    {
                        var dependentItemInfo = new DependentItemInfo<TItem, TKey>(item, keySelector.Invoke(item), outstandingDependencies: itemDependencyList);

                        foreach (var dependency in itemDependencyList)
                        {
                            if (!dependentItemsByDependency.TryGetValue(dependency, out var dependentItemList))
                                dependentItemsByDependency.Add(dependency, dependentItemList = new List<DependentItemInfo<TItem, TKey>>(1));
                            dependentItemList.Add(dependentItemInfo);
                        }

                        continue;
                    }
                }

                var recursionStack = new Stack<(TKey CompletedItemKey, IEnumerator<DependentItemInfo<TItem, TKey>> DependentItems)>();

                bool MoveNext()
                {
                    while (true)
                    {
                        if (!recursionStack.TryPeek(out var peeked)) return false;
                        if (peeked.DependentItems.MoveNext()) return true;
                        peeked.DependentItems.Dispose();
                        recursionStack.Pop();
                    }
                }

                var completedItem = item;
                var completedItemKey = keySelector.Invoke(item);

                while (true)
                {
                    yield return completedItem;
                    returnedItems.Add(completedItemKey);

                    if (dependentItemsByDependency.Remove(completedItemKey, out var dependentItems))
                        recursionStack.Push((completedItemKey, dependentItems.GetEnumerator()));

                    var hasNewCompletedItem = false;

                    while (MoveNext())
                    {
                        var dependentItemInfo = recursionStack.Peek().DependentItems.Current;

                        if (dependentItemInfo.OutstandingDependencies.Count == 1)
                        {
                            if (!comparer.Equals(recursionStack.Peek().CompletedItemKey, dependentItemInfo.OutstandingDependencies[0]))
                                throw new ArgumentException("A consistency error was detected in the comparer.", nameof(comparer));

                            completedItem = dependentItemInfo.Item;
                            completedItemKey = dependentItemInfo.Key;
                            hasNewCompletedItem = true;
                            break;
                        }

                        // Still has dependencies to resolve. Update the outstanding list which is shared with the other entries in deferredItemsByDependency.
                        if (!dependentItemInfo.RemoveOutstandingDependency(recursionStack.Peek().CompletedItemKey, comparer))
                            throw new ArgumentException("A consistency error was detected in the comparer.", nameof(comparer));
                    }

                    if (!hasNewCompletedItem) break;
                }
            }

            if (dependentItemsByDependency.Count != 0)
            {
                var cyclicalReferences = ImmutableArray.CreateBuilder<TItem>();

                foreach (var dependentItemList in dependentItemsByDependency.Values.ToArray())
                {
                    foreach (var dependentItem in dependentItemList)
                    {
                        if (dependentItemsByDependency.Remove(dependentItem.Key))
                            cyclicalReferences.Add(dependentItem.Item);
                    }
                }

                throw new TopologicalSortException<TItem, TKey>(
                    cyclicalReferences.ToImmutable(),
                    externalDependencies: dependentItemsByDependency.Keys.ToImmutableArray(),
                    externalDependents: dependentItemsByDependency.Values
                        .SelectMany(dependentItemList => dependentItemList)
                        .GroupBy(i => i.Key, (key, items) => items.First().Item, comparer)
                        .ToImmutableArray());
            }
        }
    }
}
