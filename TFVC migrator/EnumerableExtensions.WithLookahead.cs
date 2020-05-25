using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TfvcMigrator
{
    partial class EnumerableExtensions
    {
        public static IAsyncEnumerable<T> WithLookahead<T>(this IAsyncEnumerable<T> source)
        {
            return new WrapperEnumerable<T>(source, enumerator => new LookaheadEnumerator<T>(enumerator));
        }

        private sealed class WrapperEnumerable<T> : IAsyncEnumerable<T>
        {
            private readonly IAsyncEnumerable<T> source;
            private readonly Func<IAsyncEnumerator<T>, IAsyncEnumerator<T>> wrap;

            public WrapperEnumerable(IAsyncEnumerable<T> source, Func<IAsyncEnumerator<T>, IAsyncEnumerator<T>> wrap)
            {
                this.source = source;
                this.wrap = wrap;
            }

            public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return wrap(source.GetAsyncEnumerator(cancellationToken));
            }
        }

        private sealed class LookaheadEnumerator<T> : IAsyncEnumerator<T>
        {
            private static readonly ValueTask<bool> DetectOverlappingCalls = new ValueTask<bool>(
                Task.FromException<bool>(new InvalidOperationException("MoveNextAsync may not be called until the previously-returned task completes.")));

            private readonly IAsyncEnumerator<T> inner;
            private ValueTask<bool> nextTask;
            private bool useCachedCurrentValue;
            private T cachedCurrentValue = default!;

            public LookaheadEnumerator(IAsyncEnumerator<T> inner)
            {
                this.inner = inner;
                nextTask = inner.MoveNextAsync();
            }

            public T Current => useCachedCurrentValue ? cachedCurrentValue : inner.Current;

            public ValueTask<bool> MoveNextAsync()
            {
                var nextTask = this.nextTask;
                this.nextTask = DetectOverlappingCalls;

                if (nextTask.IsCompleted)
                {
                    var result = nextTask.Result;

                    if (nextTask.IsCompletedSuccessfully)
                        OnMoveNextCompleted(result);

                    return new ValueTask<bool>(result);
                }

                useCachedCurrentValue = false;
                return new ValueTask<bool>(HandleCompletion(nextTask));
            }

            private async Task<bool> HandleCompletion(ValueTask<bool> nextTask)
            {
                var result = await nextTask.ConfigureAwait(false);
                OnMoveNextCompleted(result);
                return result;
            }

            private void OnMoveNextCompleted(bool result)
            {
                cachedCurrentValue = result ? inner.Current : default!;
                useCachedCurrentValue = true;
                if (result) nextTask = inner.MoveNextAsync();
            }

            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
