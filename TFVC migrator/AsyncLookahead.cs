using System;
using System.Threading.Tasks;

namespace TfvcMigrator
{
    internal static class AsyncLookahead
    {
        public static AsyncLookahead<TInput, TOutput> Create<TInput, TOutput>(
            Func<TInput, Task<TOutput>> startTask,
            TInput initialInput)
        {
            var lookahead = new AsyncLookahead<TInput, TOutput>(startTask);
            lookahead.StartNextTask(initialInput);
            return lookahead;
        }
    }

    internal struct AsyncLookahead<TInput, TOutput>
    {
        private readonly Func<TInput, Task<TOutput>> startTask;
        private Task<TOutput>? currentTask;

        public AsyncLookahead(Func<TInput, Task<TOutput>> startTask)
        {
            this.startTask = startTask;
            currentTask = null;
        }

        public Task<TOutput> CurrentTask => currentTask
            ?? throw new InvalidOperationException($"{nameof(CurrentTask)} must not be accessed before {nameof(StartNextTask)}.");

        public void StartNextTask(TInput input)
        {
            currentTask = startTask(input);
        }
    }
}
