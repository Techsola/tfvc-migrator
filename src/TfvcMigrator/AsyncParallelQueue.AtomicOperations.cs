using System.Collections;

namespace TfvcMigrator;

internal sealed partial class AsyncParallelQueue<T>
{
    /// <summary>
    /// <para>
    /// The purpose of this class is to separate what must be done inside a lock (everything this class does) from
    /// what may or must be done outside the lock (everything <see cref="AsyncParallelQueue{T}"/> does).
    /// </para>
    /// <para>
    /// Do not lock on objects of this type from outside the type. It locks on itself by design.
    /// </para>
    /// <para>
    /// A lock-free implementation is not possible in this case due to the need to use <see cref="source"/> from
    /// multiple threads in a thread-safe manner.
    /// </para>
    /// </summary>
    private sealed class AtomicOperations
    {
        private readonly IEnumerator<Task<T>> source;

        private ArrayBuilder<Task<T>> completedTasks;
        private bool reachedEndOfEnumerator;
        private bool cancelFurtherEnumeration;

        /// <summary>
        /// Used to detect reentry within the same lock due to user code triggering <see cref="OnCancel"/> during a
        /// call to <see cref="IEnumerator.MoveNext"/> or <see cref="IEnumerator{T}.Current"/> (while inside <see
        /// cref="TryStartNext"/>).
        /// </summary>
        private bool isCallingMoveNextOrCurrent;

        /// <summary>
        /// Ensures that multiple calls to <see cref="OnCancel"/> or <see cref="TryStartNext"/> cannot result in
        /// more than call being assigned the responsibility of handling the completion.
        /// </summary>
        private bool completionHandled;

        /// <summary>
        /// Tracks which index the last enumerated task's result should occupy in the output array.
        /// </summary>
        private int startedTaskCount;

        /// <summary>
        /// Used to determine when all started tasks have completed.
        /// </summary>
        private int completedTaskCount;

        public AtomicOperations(IEnumerable<Task<T>> source)
        {
            this.source = source.GetEnumerator();

            completedTasks = new ArrayBuilder<Task<T>>(
                initialCapacity: source.TryGetNonEnumeratedCount(out var count) ? count : 0);
        }

        public NextOperation TryStartNext()
        {
            lock (this)
            {
                return TryStartNextOrTryComplete();
            }
        }

        public NextOperation OnCancel()
        {
            lock (this)
            {
                cancelFurtherEnumeration = true;

                if (isCallingMoveNextOrCurrent)
                {
                    // TryStartNextOrTryComplete is the current caller of the user code that called this method by
                    // canceling the token. It's too soon to complete because user code hasn't returned to
                    // TryStartNextOrTryComplete yet, so the resulting task hasn't been seen yet.

                    // Because cancelFurtherEnumeration is set, TryStartNextOrTryComplete will call TryComplete when
                    // the time is right.
                    return NextOperation.None;
                }

                return TryComplete();
            }
        }

        public NextOperation OnTaskCompleted(Task<T> completedTask, int taskIndex)
        {
            lock (this)
            {
                completedTaskCount++;
                completedTasks[taskIndex] = completedTask;
                return TryStartNextOrTryComplete();
            }
        }

        /// <summary>Only call from within a lock.</summary>
        private NextOperation TryStartNextOrTryComplete()
        {
            if (reachedEndOfEnumerator | cancelFurtherEnumeration)
                return TryComplete();

            var taskIndex = startedTaskCount;

            isCallingMoveNextOrCurrent = true;
            try
            {
                reachedEndOfEnumerator = !source.MoveNext();
            }
            catch (Exception ex)
            {
                startedTaskCount++;
                completedTaskCount++;
                completedTasks[taskIndex] = Task.FromException<T>(ex);
                reachedEndOfEnumerator = true;
            }
            finally
            {
                isCallingMoveNextOrCurrent = false;
            }

            if (reachedEndOfEnumerator) return TryComplete();

            startedTaskCount++;

            Task<T> task;
            isCallingMoveNextOrCurrent = true;
            try
            {
                // Even though MoveNext may have caused cancelFurtherEnumeration to become true, assume that
                // the task is already started even if we don't access Current and that therefore we should
                // observe it.
                task = source.Current;
            }
            catch (Exception ex)
            {
                task = Task.FromException<T>(ex);
            }
            finally
            {
                isCallingMoveNextOrCurrent = false;
            }

            if (task is null)
                task = Task.FromException<T>(new InvalidOperationException("The source task enumerator returned a null instance."));

            return NextOperation.Subscribe(task, taskIndex);
        }

        /// <summary>Only call from within a lock.</summary>
        private NextOperation TryComplete()
        {
            if (completionHandled || completedTaskCount != startedTaskCount)
                return NextOperation.None;

            try
            {
                source.Dispose();
            }
            catch (Exception ex)
            {
                var taskIndex = startedTaskCount;
                startedTaskCount++;
                completedTaskCount++;
                completedTasks[taskIndex] = Task.FromException<T>(ex);
            }

            completionHandled = true;
            return NextOperation.Complete(canceled: !reachedEndOfEnumerator, completedTasks.MoveToArraySegment());
        }
    }
}
