using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TfvcMigrator
{
    internal sealed partial class AsyncParallelQueue<T>
    {
        private readonly AtomicOperations atomicOperations;
        private readonly TaskCompletionSource<ImmutableArray<T>> taskCompletionSource = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncParallelQueue{T}"/> class and begins enumerating all tasks
        /// from the <paramref name="source"/> parameter in the background.
        /// </summary>
        /// <param name="source">
        /// <para>
        /// <see cref="IEnumerator.MoveNext"/> will be called each time a new task needs to be started in order to reach
        /// the specified level of parallelism. This means that the enumerable you provide must create tasks on demand.
        /// </para>
        /// <para>
        /// For example, <c>data.Select(async d => await FooAsync(d))</c>. <b>Do not</b> use <c>.ToList()</c> or
        /// otherwise eagerly buffer the tasks into a list, or else all the tasks will be started in parallel with an
        /// infinite degree of parallelism before you even call <see cref="AsyncParallelQueue{T}"/>.
        /// </para>
        /// <para>
        /// Only one thread will interact with the enumerator at a time.
        /// </para>
        /// </param>
        /// <param name="degreeOfParallelism">
        /// The maximum number of incomplete tasks enumerated from the <paramref name="source"/> parameter at a time.
        /// </param>
        /// <param name="cancellationToken">
        /// If enumeration has not already ended, prevents further calls to <see cref="IEnumerator.MoveNext"/> and
        /// causes <see cref="WaitAllAsync"/> to result in cancellation instead of success once the already-started
        /// tasks complete.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="source"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="degreeOfParallelism"/> is less than zero.
        /// </exception>
        public AsyncParallelQueue(IEnumerable<Task<T>> source, int degreeOfParallelism, CancellationToken cancellationToken)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            if (degreeOfParallelism < 1)
                throw new ArgumentOutOfRangeException(nameof(degreeOfParallelism), degreeOfParallelism, "Degree of parallelism must be greater than or equal to one.");

            if (cancellationToken.IsCancellationRequested)
            {
                taskCompletionSource.SetCanceled(cancellationToken);
                atomicOperations = null!;
            }
            else
            {
                atomicOperations = new AtomicOperations(source);

                cancellationToken.Register(OnCancel);

                for (var i = 0; i < degreeOfParallelism; i++)
                {
                    DoNextOperation(atomicOperations.TryStartNext());
                }
            }
        }

        /// <summary>
        /// <para>
        /// Asynchronously waits for all enumerated tasks and returns a collection of the task results in the same order
        /// that the tasks were enumerated.
        /// </para>
        /// <para>
        /// If any task fails or the enumerator misbehaves, the result will be a failed task aggregating all inner
        /// exceptions once all tasks are no longer running. Otherwise, if any task is externally canceled or the
        /// cancellation token passed to the constructor is canceled before the enumerator ends, the result will be a
        /// canceled task once all tasks are no longer running.
        /// </para>
        /// </summary>
        public Task<ImmutableArray<T>> WaitAllAsync() => taskCompletionSource.Task;

        private void OnCancel()
        {
            DoNextOperation(atomicOperations.OnCancel());
        }

        private void DoNextOperation(NextOperation nextOperation)
        {
            if (nextOperation.IsSubscribe(out var task, out var taskIndex))
            {
                task.ContinueWith(OnTaskCompleted, state: taskIndex, TaskContinuationOptions.ExecuteSynchronously);
            }
            else if (nextOperation.IsComplete(out var canceled, out var completedTasks))
            {
                Complete(canceled, completedTasks);
            }
        }

        private void OnTaskCompleted(Task<T> completedTask, object? state)
        {
            DoNextOperation(atomicOperations.OnTaskCompleted(completedTask, taskIndex: (int)state!));
        }

        private void Complete(bool canceled, ArraySegment<Task<T>> completedTasks)
        {
            var exceptions = new List<Exception>();
            var anyTaskWasCancelledExternally = false;
            var results = ImmutableArray.CreateBuilder<T>(completedTasks.Count);

            foreach (var completedTask in completedTasks)
            {
                switch (completedTask.Status)
                {
                    case TaskStatus.RanToCompletion:
                        results.Add(completedTask.Result);
                        break;
                    case TaskStatus.Canceled:
                        anyTaskWasCancelledExternally = true;
                        break;
                    case TaskStatus.Faulted:
                        exceptions.AddRange(completedTask.Exception!.InnerExceptions);
                        break;
                }
            }

            if (exceptions.Any())
                taskCompletionSource.TrySetException(exceptions);
            else if (anyTaskWasCancelledExternally || canceled)
                taskCompletionSource.SetCanceled();
            else
                taskCompletionSource.SetResult(results.MoveToImmutable());
        }
    }
}
