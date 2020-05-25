using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace TfvcMigrator
{
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
}
