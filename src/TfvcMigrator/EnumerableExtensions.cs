namespace TfvcMigrator;

public static partial class EnumerableExtensions
{
    public static async IAsyncEnumerable<TResult> SelectAwait<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, Task<TResult>> selector)
    {
        foreach (var value in source)
        {
            yield return await selector(value);
        }
    }

    public static async IAsyncEnumerable<TResult> SelectAwait<TSource, TResult>(this IAsyncEnumerable<TSource> source, Func<TSource, Task<TResult>> selector)
    {
        await foreach (var value in source)
        {
            yield return await selector(value);
        }
    }

    public static async Task<ImmutableArray<T>> ToImmutableArrayAsync<T>(this IAsyncEnumerable<T> source)
    {
        var builder = ImmutableArray.CreateBuilder<T>();

        await foreach (var value in source)
        {
            builder.Add(value);
        }

        return builder.ToImmutable();
    }

    public static async Task<ImmutableArray<TResult>> SelectAwaitParallel<T, TResult>(this IEnumerable<T> source, Func<T, Task<TResult>> selector, int degreeOfParallelism, CancellationToken cancellationToken)
    {
        var builder = ImmutableArray.CreateBuilder<TResult>(
            source.TryGetNonEnumeratedCount(out var count) ? count : 0);

        await Parallel.ForEachAsync(
            source.Select((value, index) => (value, index)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = degreeOfParallelism,
                CancellationToken = cancellationToken,
            },
            async (valueAndIndex, _) =>
            {
                var result = await selector(valueAndIndex.value).ConfigureAwait(false);

                lock (builder)
                {
                    if (builder.Count <= valueAndIndex.index)
                        builder.Count = valueAndIndex.index + 1;

                    builder[valueAndIndex.index] = result;
                }
            });

        return builder.ToImmutable();
    }

    public static IEnumerable<T?> AsNullable<T>(this IEnumerable<T> source)
        where T : struct
    {
        return source.Select(value => (T?)value);
    }

    public static T? FirstOrNull<T>(this IEnumerable<T> source)
        where T : struct
    {
        return source.AsNullable().FirstOrDefault();
    }

    public static T? FirstOrNull<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        where T : struct
    {
        return source.Where(predicate).FirstOrNull();
    }
}
