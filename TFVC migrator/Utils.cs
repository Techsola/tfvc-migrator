using System;
using System.Threading;

namespace TfvcMigrator
{
    internal static class Utils
    {
        public static T InterlockedUpdate<T, TState>(ref T location, TState state, Func<T, TState, T> applyUpdate)
            where T : class?
        {
            for (var comparand = Volatile.Read(ref location);;)
            {
                var value = applyUpdate(comparand, state);
                var result = Interlocked.CompareExchange(ref location, value, comparand);
                if (result == comparand) return value;
                comparand = result;
            }
        }
    }
}
