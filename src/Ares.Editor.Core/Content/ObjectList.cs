using System.Collections;
using System.Text.Json.Nodes;

namespace Ares.Editor.Core.Content;

/// <summary>
/// A list of objects of a content file (rules, tiers, pools, items...), in file order. Each entry keeps one view for
/// as long as it is in the list, and every change applies to the file's JSON at once.
/// </summary>
/// <typeparam name="T">The view of an entry.</typeparam>
public sealed class ObjectList<T> : IReadOnlyList<T>
    where T : JsonObjectView
{
    private readonly JsonArray _array;
    private readonly List<T> _items;

    internal ObjectList(JsonArray array, Func<JsonObject, T> wrap)
    {
        _array = array;
        _items = [.. array.Select(entry => wrap((JsonObject)entry!))];
    }

    /// <summary>The number of entries.</summary>
    public int Count => _items.Count;

    /// <summary>The entry at a position.</summary>
    /// <param name="index">The position, from 0.</param>
    public T this[int index] => _items[index];

    /// <summary>The position of an entry, or -1 when it is not in the list.</summary>
    /// <param name="item">The entry.</param>
    /// <returns>The position.</returns>
    public int IndexOf(T item) => _items.IndexOf(item);

    /// <summary>Removes the entry at a position.</summary>
    /// <param name="index">The position, from 0.</param>
    public void RemoveAt(int index)
    {
        _array.RemoveAt(index);
        _items.RemoveAt(index);
    }

    /// <summary>Removes an entry.</summary>
    /// <param name="item">The entry.</param>
    /// <returns>True when it was in the list.</returns>
    public bool Remove(T item)
    {
        int index = _items.IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    /// <summary>Moves an entry to another position, shifting the ones between (rules apply in list order).</summary>
    /// <param name="from">The entry's position.</param>
    /// <param name="to">Its new position.</param>
    public void Move(int from, int to)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(to);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(to, _items.Count);
        T item = _items[from];
        RemoveAt(from);
        _array.Insert(to, item.Node);
        _items.Insert(to, item);
    }

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Appends a new entry.</summary>
    internal T Add(T item)
    {
        _array.Add(item.Node);
        _items.Add(item);
        return item;
    }
}
