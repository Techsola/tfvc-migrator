using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TfvcMigrator
{
    internal static partial class EnumerableExtensions
    {
        public static async IAsyncEnumerable<TResult> SelectAwait<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, Task<TResult>> selector)
        {
            foreach (var value in source)
            {
                yield return await selector(value);
            }
        }

        public static Task<ImmutableArray<TResult>> SelectAwaitParallel<T, TResult>(this IEnumerable<T> source, Func<T, Task<TResult>> selector, int degreeOfParallelism, CancellationToken cancellationToken)
        {
            return new AsyncParallelQueue<TResult>(source.Select(selector), degreeOfParallelism, cancellationToken).WaitAllAsync();
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

        public static IEnumerable<T> Values<T>(this IEnumerable<T?> source)
            where T : struct
        {
            foreach (var value in source)
            {
                if (value is { } unwrapped)
                    yield return unwrapped;
            }
        }

        public static int FindSingleIndex<T>(this ImmutableArray<T> array, Func<T, bool> predicate)
        {
            var index = -1;

            for (var i = 0; i < array.Length; i++)
            {
                if (!predicate(array[i])) continue;

                if (index == -1)
                    index = i;
                else
                    throw new InvalidOperationException("The sequence contains more than one matching element.");
            }

            return index;
        }
    }
}
