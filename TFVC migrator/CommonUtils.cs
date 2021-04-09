using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace TfvcMigrator
{
    internal static class CommonUtils
    {
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="enumerable"/> is <see langword="null"/>.
        /// </exception>
        public static bool TryGetCollectionCount<T>(IEnumerable<T> enumerable, out int count)
        {
            switch (enumerable)
            {
                case null:
                    throw new ArgumentNullException(nameof(enumerable));

                case IReadOnlyCollection<T> collection:
                    count = collection.Count;
                    return true;

                case ICollection<T> collection:
                    count = collection.Count;
                    return true;

                case ICollection collection:
                    count = collection.Count;
                    return true;

                default:
                    count = default;
                    return false;
            }
        }

        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="minCapacity"/> is less than zero.
        /// </exception>
        /// <exception cref="OutOfMemoryException">
        /// Thrown when <paramref name="minCapacity"/> is greater than the maximum available array length.
        /// </exception>
        public static void EnsureCapacity<T>([NotNull] ref T[]? array, int minCapacity)
        {
            if (minCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(minCapacity), minCapacity, "Minimum capacity must be greater than or equal to zero.");

            if (array != null && array.Length >= minCapacity) return;

            var newCapacity = array is null || array.Length == 0 ? 4 : array.Length * 2;

            const int maxArrayLength = 0x7FEFFFFF;
            if ((uint)newCapacity > maxArrayLength) newCapacity = maxArrayLength;
            if (newCapacity < minCapacity) newCapacity = minCapacity;

            Array.Resize(ref array, newCapacity);
        }
    }
}
