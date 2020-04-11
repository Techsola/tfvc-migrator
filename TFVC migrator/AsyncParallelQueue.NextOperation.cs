using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace TfvcMigrator
{
    internal sealed partial class AsyncParallelQueue<T>
    {
        /// <summary>
        /// A discriminated union which holds information which must be read within the lock inside <see
        /// cref="AtomicOperations"/> but which must not be acted on inside that lock.
        /// </summary>
        private readonly struct NextOperation
        {
            public static NextOperation None => default;

            public bool IsNone => kind == 0;

            public static NextOperation Subscribe(Task<T> task, int taskIndex)
            {
                return new NextOperation(1, task, taskIndex, default, default);
            }

            public bool IsSubscribe([NotNullWhen(true)] out Task<T>? task, out int taskIndex)
            {
                task = this.task;
                taskIndex = this.taskIndex;
                return kind == 1;
            }

            public static NextOperation Complete(bool canceled, ArraySegment<Task<T>> completedTasks)
            {
                return new NextOperation(2, default, default, canceled, completedTasks);
            }

            public bool IsComplete(out bool canceled, out ArraySegment<Task<T>> completedTasks)
            {
                canceled = this.canceled;
                completedTasks = this.completedTasks;
                return kind == 2;
            }

            private readonly int kind;
            private readonly Task<T>? task;
            private readonly int taskIndex;
            private readonly bool canceled;
            private readonly ArraySegment<Task<T>> completedTasks;

            private NextOperation(int kind, Task<T>? task, int taskIndex, bool canceled, ArraySegment<Task<T>> completedTasks)
            {
                this.kind = kind;
                this.task = task;
                this.taskIndex = taskIndex;
                this.canceled = canceled;
                this.completedTasks = completedTasks;
            }
        }
    }
}
