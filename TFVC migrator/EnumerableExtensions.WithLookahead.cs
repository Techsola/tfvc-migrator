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
                this.nextTask = default;

                if (nextTask.IsCompleted)
                {
                    var result = nextTask.Result;

                    if (nextTask.IsCompletedSuccessfully)
                        OnMoveNextCompleted(result);

                    return new ValueTask<bool>(result);
                }

                var task = nextTask.AsTask();
                task.ContinueWith(OnReturnedTaskCompleted, TaskContinuationOptions.OnlyOnRanToCompletion);

                useCachedCurrentValue = false;
                return new ValueTask<bool>(task);
            }

            private void OnReturnedTaskCompleted(Task<bool> succeededTask)
            {
                OnMoveNextCompleted(succeededTask.Result);
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
