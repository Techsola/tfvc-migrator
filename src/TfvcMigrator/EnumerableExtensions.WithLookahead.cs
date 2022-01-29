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
            private static readonly ValueTask<bool> DetectOverlappingCalls = new(
                Task.FromException<bool>(new InvalidOperationException("MoveNextAsync may not be called until the previously-returned task completes.")));

            private readonly IAsyncEnumerator<T> inner;
            private ValueTask<bool> nextTask;

            public LookaheadEnumerator(IAsyncEnumerator<T> inner)
            {
                this.inner = inner;
                nextTask = inner.MoveNextAsync();
            }

            [AllowNull]
            public T Current { get; private set; }

            public ValueTask<bool> MoveNextAsync()
            {
                var nextTask = this.nextTask;
                this.nextTask = DetectOverlappingCalls;

                if (nextTask.IsCompleted)
                {
                    if (nextTask.IsCompletedSuccessfully)
                    {
                        var result = nextTask.Result;
                        OnMoveNextCompleted(succeededWithFalse: !result);
                        return new ValueTask<bool>(result);
                    }

                    OnMoveNextCompleted(succeededWithFalse: false);
                    return nextTask;
                }

                return new ValueTask<bool>(HandleCompletion(nextTask));
            }

            private async Task<bool> HandleCompletion(ValueTask<bool> nextTask)
            {
                var succeededWithFalse = false;
                try
                {
                    var result = await nextTask.ConfigureAwait(false);
                    succeededWithFalse = !result;
                    return result;
                }
                finally
                {
                    OnMoveNextCompleted(succeededWithFalse);
                }
            }

            private void OnMoveNextCompleted(bool succeededWithFalse)
            {
                Current = succeededWithFalse ? default : inner.Current;
                nextTask = succeededWithFalse ? new ValueTask<bool>(false) : inner.MoveNextAsync();
            }

            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
