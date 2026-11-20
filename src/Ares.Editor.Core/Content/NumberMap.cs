using System.Collections;
using System.Text.Json.Nodes;

namespace Ares.Editor.Core.Content;

/// <summary>
/// An object of numbers by key in a content file (<c>chances</c>, <c>areaMultipliers</c>, <c>groupFill</c>), in file
/// order. Keys are kept as written; a value that is set to what it already is keeps its text.
/// </summary>
public sealed class NumberMap : IReadOnlyCollection<KeyValuePair<string, double>>
{
    private readonly JsonObjectView _owner;
    private readonly string _name;
    private readonly bool _required;

    internal NumberMap(JsonObjectView owner, string name, bool required)
    {
        _owner = owner;
        _name = name;
        _required = required;
    }

    /// <summary>The number of entries.</summary>
    public int Count => Map?.Count ?? 0;

    /// <summary>Whether the file has the member; an optional map without entries is left out of the file.</summary>
    public bool IsSet => Map is not null;

    /// <summary>The keys, in file order.</summary>
    public IReadOnlyList<string> Keys => Map is { } map ? [.. map.Select(entry => entry.Key)] : [];

    private JsonObject? Map => _owner.Node.TryGetPropertyValue(_name, out JsonNode? value) ? value as JsonObject : null;

    /// <summary>The value of a key (compared exactly), or null when the map does not have it.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The value, or null.</returns>
    public double? Get(string key) => Map is { } map && map.TryGetPropertyValue(key, out JsonNode? value) && value is not null ? value.GetValue<double>() : null;

    /// <summary>Sets the value of a key: in place when the key exists, else as a new last entry.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value; it must be finite.</param>
    public void Set(string key, double value)
    {
        ArgumentNullException.ThrowIfNull(key);
        JsonObject? map = Map;
        if (map is null)
        {
            map = [];
            _owner.Put(_name, map);
        }

        int index = map.IndexOf(key);
        if (index < 0)
        {
            map.Add(key, JsonNumbers.Create(value));
        }
        else if (!JsonNumbers.Holds(map[key], value))
        {
            map.SetAt(index, JsonNumbers.Create(value, map[key]));
        }
    }

    /// <summary>Removes a key; removing the last entry of an optional map removes the member.</summary>
    /// <param name="key">The key.</param>
    /// <returns>True when the map had the key.</returns>
    public bool Remove(string key)
    {
        if (Map is not { } map || !map.Remove(key))
        {
            return false;
        }

        if (map.Count == 0 && !_required)
        {
            _owner.RemoveMember(_name);
        }

        return true;
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, double>> GetEnumerator() =>
        (Map ?? []).Select(entry => new KeyValuePair<string, double>(entry.Key, entry.Value!.GetValue<double>())).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
