namespace TfvcMigrator;

/// <summary>
/// Enables building an array by setting indexes in any order without having to use an <c>Add</c> method.
/// </summary>
public struct ArrayBuilder<T>
{
    private T[]? array;
    private int usedLength;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrayBuilder{T}"/> struct.
    /// </summary>
    /// <param name="initialCapacity">
    /// If non-zero, creates a starting array at the specified size. If zero, an array is not created until the
    /// first index is set.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="initialCapacity"/> is less than zero.
    /// </exception>
    public ArrayBuilder(int initialCapacity)
    {
        if (initialCapacity <= 0)
        {
            if (initialCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity), initialCapacity, "Initial capacity must be greater than or equal to zero.");

            array = null;
        }
        else
        {
            array = new T[initialCapacity];
        }

        usedLength = 0;
    }

    /// <summary>
    /// <para>
    /// Gets or sets a value at the specified index in the array being built. The array is expanded as necessary to
    /// accommodate setting any index.
    /// </para>
    /// <para>
    /// Getting an index past the current end does not expand the array. It returns the default value of
    /// <typeparamref name="T"/>, which is what getting an unset index within the array would return.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="index"/> is less than zero.
    /// </exception>
    [MaybeNull]
    public T this[int index]
    {
        get
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index), index, "Index must be greater than or equal to zero.");

            return array != null && index < array.Length ? array[index] : default;
        }
        set
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index), index, "Index must be greater than or equal to zero.");

            var requiredLength = index + 1;
            if (usedLength < requiredLength)
            {
                usedLength = requiredLength;
                CommonUtils.EnsureCapacity(ref array, requiredLength);
            }

            array![index] = value;
        }
    }

    /// <summary>
    /// Obtains the built result in <see cref="ArraySegment{T}"/> form and clears the builder.
    /// </summary>
    public ArraySegment<T> MoveToArraySegment()
    {
        var segment = new ArraySegment<T>(array ?? Array.Empty<T>(), offset: 0, usedLength);
        this = default;
        return segment;
    }
}
