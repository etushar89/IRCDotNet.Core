using System.Collections;
using System.Collections.Concurrent;

namespace IRCDotNet.Core.Utilities;

/// <summary>
/// Thread-safe HashSet implementation backed by a <see cref="ConcurrentDictionary{T, Byte}"/>.
/// </summary>
/// <typeparam name="T">The type of elements in the set.</typeparam>
public class ConcurrentHashSet<T> : ISet<T>, IReadOnlySet<T> where T : notnull
{
    private readonly ConcurrentDictionary<T, byte> _dictionary = new();

    /// <inheritdoc />
    public int Count => _dictionary.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public bool Add(T item)
    {
        return _dictionary.TryAdd(item, 0);
    }

    /// <inheritdoc />
    public void Clear()
    {
        _dictionary.Clear();
    }

    /// <inheritdoc />
    public bool Contains(T item)
    {
        return _dictionary.ContainsKey(item);
    }

    /// <inheritdoc />
    public void CopyTo(T[] array, int arrayIndex)
    {
        if (array == null)
            throw new ArgumentNullException(nameof(array));
        if (arrayIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (array.Length - arrayIndex < Count)
            throw new ArgumentException("Array is too small");

        var items = _dictionary.Keys.ToArray();
        Array.Copy(items, 0, array, arrayIndex, items.Length);
    }

    /// <inheritdoc />
    public void ExceptWith(IEnumerable<T> other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));

        foreach (var item in other)
        {
            _dictionary.TryRemove(item, out _);
        }
    }

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator()
    {
        return _dictionary.Keys.GetEnumerator();
    }

    /// <inheritdoc />
    public void IntersectWith(IEnumerable<T> other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));

        var otherSet = new HashSet<T>(other);
        var toRemove = new List<T>();

        foreach (var item in _dictionary.Keys)
        {
            if (!otherSet.Contains(item))
            {
                toRemove.Add(item);
            }
        }

        foreach (var item in toRemove)
        {
            _dictionary.TryRemove(item, out _);
        }
    }

    /// <inheritdoc />
    public bool IsProperSubsetOf(IEnumerable<T> other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));

        var otherSet = new HashSet<T>(other);
        return Count < otherSet.Count && IsSubsetOf(otherSet);
    }

    /// <inheritdoc />
    public bool IsProperSupersetOf(IEnumerable<T> other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));

        var otherSet = new HashSet<T>(other);
        return Count > otherSet.Count && IsSupersetOf(otherSet);
    }

    /// <inheritdoc />
    public bool IsSubsetOf(IEnumerable<T> other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));

        var otherSet = new HashSet<T>(other);
        return _dictionary.Keys.All(otherSet.Contains);
    }

    /// <inheritdoc />
    public bool IsSupersetOf(IEnumerable<T> other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));

        return other.All(Contains);
    }

    /// <inheritdoc />
    public bool Overlaps(IEnumerable<T> other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));

        return other.Any(Contains);
    }

    /// <inheritdoc />
    public bool Remove(T item)
    {
        return _dictionary.TryRemove(item, out _);
    }

    /// <inheritdoc />
    public bool SetEquals(IEnumerable<T> other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));

        var otherSet = new HashSet<T>(other);
        return Count == otherSet.Count && _dictionary.Keys.All(otherSet.Contains);
    }

    /// <inheritdoc />
    public void SymmetricExceptWith(IEnumerable<T> other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));

        foreach (var item in other)
        {
            if (!_dictionary.TryRemove(item, out _))
            {
                _dictionary.TryAdd(item, 0);
            }
        }
    }

    /// <inheritdoc />
    public void UnionWith(IEnumerable<T> other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));

        foreach (var item in other)
        {
            _dictionary.TryAdd(item, 0);
        }
    }

    void ICollection<T>.Add(T item)
    {
        Add(item);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>
    /// Creates a snapshot of the current items as a regular HashSet.
    /// </summary>
    /// <returns>A new <see cref="HashSet{T}"/> containing the current items.</returns>
    public HashSet<T> ToHashSet()
    {
        return new HashSet<T>(_dictionary.Keys);
    }

    /// <summary>
    /// Creates a snapshot of the current items as an array.
    /// </summary>
    /// <returns>An array containing the current items.</returns>
    public T[] ToArray()
    {
        return _dictionary.Keys.ToArray();
    }
}
