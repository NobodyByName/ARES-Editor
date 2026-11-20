using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ares.Editor.Core.Content;

/// <summary>
/// A typed view of one JSON object of a content file. The view reads and writes the object itself, so members it does
/// not know, their order and the text of numbers it does not change survive a save. A member it adds goes where the
/// server's files put it: after the known members that come before it.
/// </summary>
public abstract class JsonObjectView
{
    private readonly string[] _order;

    private protected JsonObjectView(JsonObject node, string[] order)
    {
        Node = node;
        _order = order;
    }

    /// <summary>The object the view reads and writes.</summary>
    internal JsonObject Node { get; }

    /// <summary>Sets a member, keeping its place when it exists and putting a new one in the known order.</summary>
    internal void Put(string name, JsonNode? value)
    {
        int existing = Node.IndexOf(name);
        if (existing >= 0)
        {
            Node.SetAt(existing, value);
            return;
        }

        Node.Insert(InsertionIndex(name), name, value);
    }

    /// <summary>Removes a member if it is there.</summary>
    internal void RemoveMember(string name) => Node.Remove(name);

    /// <summary>A member's value, or null when it is missing or JSON null.</summary>
    private protected JsonNode? Get(string name) => Node.TryGetPropertyValue(name, out JsonNode? value) ? value : null;

    private protected string? GetString(string name) => Get(name)?.GetValue<string>();

    private protected void SetString(string name, string? value)
    {
        if (value is null)
        {
            RemoveMember(name);
        }
        else
        {
            Put(name, JsonValue.Create(value));
        }
    }

    private protected double? GetNumber(string name) => Get(name)?.GetValue<double>();

    private protected void SetNumber(string name, double? value)
    {
        if (value is not { } number)
        {
            RemoveMember(name);
        }
        else if (!JsonNumbers.Holds(Get(name), number))
        {
            Put(name, JsonNumbers.Create(number, Get(name)));
        }
    }

    private protected int GetInteger(string name) => Get(name)?.GetValue<int>() ?? 0;

    private protected void SetInteger(string name, int value)
    {
        if (!JsonNumbers.Holds(Get(name), value))
        {
            Put(name, JsonNumbers.Create(value));
        }
    }

    private protected bool GetBoolean(string name) => Get(name)?.GetValue<bool>() ?? false;

    private protected void SetBoolean(string name, bool value)
    {
        if (Get(name)?.GetValue<bool>() != value)
        {
            Put(name, JsonValue.Create(value));
        }
    }

    private protected IReadOnlyList<string> GetStrings(string name) => Get(name) is JsonArray array ? [.. array.Select(entry => entry!.GetValue<string>())] : [];

    /// <summary>Replaces a list of strings; an empty list removes an optional member.</summary>
    private protected void SetStrings(string name, IEnumerable<string> values, bool required)
    {
        ArgumentNullException.ThrowIfNull(values);
        string[] list = [.. values];
        if (list.Length == 0 && !required)
        {
            RemoveMember(name);
            return;
        }

        if (GetStrings(name).SequenceEqual(list, StringComparer.Ordinal) && Get(name) is not null)
        {
            return;
        }

        Put(name, new JsonArray([.. list.Select(JsonNode? (value) => JsonValue.Create(value))]));
    }

    /// <summary>Checks that a member has the expected kind; JSON null counts as missing.</summary>
    /// <exception cref="JsonException">The member has another kind, or a required member is missing.</exception>
    private protected void Expect(string name, JsonMember kind, bool required)
    {
        JsonNode? value = Get(name);
        if (value is null)
        {
            if (required)
            {
                throw new JsonException($"{Node.GetPath()}: {name} is required");
            }

            return;
        }

        bool valid = kind switch
        {
            JsonMember.String => value.GetValueKind() == JsonValueKind.String,
            JsonMember.Number => value.GetValueKind() == JsonValueKind.Number,
            JsonMember.Integer => value.GetValueKind() == JsonValueKind.Number && value.AsValue().TryGetValue(out int _),
            JsonMember.Boolean => value.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
            JsonMember.Object => value is JsonObject,
            JsonMember.Strings => value is JsonArray strings && strings.All(entry => entry?.GetValueKind() == JsonValueKind.String),
            JsonMember.Numbers => value is JsonObject numbers && numbers.All(entry => entry.Value?.GetValueKind() == JsonValueKind.Number),
            JsonMember.Objects => value is JsonArray objects && objects.All(entry => entry is JsonObject),
            _ => false,
        };
        if (!valid)
        {
            throw new JsonException($"{value.GetPath()}: expected {Describe(kind)}");
        }
    }

    /// <summary>The views of a required list of objects.</summary>
    private protected ObjectList<T> RequiredList<T>(string name, Func<JsonObject, T> wrap)
        where T : JsonObjectView
    {
        Expect(name, JsonMember.Objects, required: true);
        return new ObjectList<T>((JsonArray)Get(name)!, wrap);
    }

    /// <summary>The view of a required object member.</summary>
    private protected T RequiredObject<T>(string name, Func<JsonObject, T> wrap)
        where T : JsonObjectView
    {
        Expect(name, JsonMember.Object, required: true);
        return wrap((JsonObject)Get(name)!);
    }

    /// <summary>The view of an optional object member, or null when it is missing.</summary>
    private protected T? OptionalObject<T>(string name, Func<JsonObject, T> wrap)
        where T : JsonObjectView
    {
        Expect(name, JsonMember.Object, required: false);
        return Get(name) is JsonObject value ? wrap(value) : null;
    }

    // Before the first member the known order puts after this one; unknown names go last.
    private int InsertionIndex(string name)
    {
        int rank = Array.IndexOf(_order, name);
        if (rank < 0)
        {
            return Node.Count;
        }

        for (int position = 0; position < Node.Count; position++)
        {
            if (Array.IndexOf(_order, Node.GetAt(position).Key) > rank)
            {
                return position;
            }
        }

        return Node.Count;
    }

    private static string Describe(JsonMember kind) => kind switch
    {
        JsonMember.String => "a string",
        JsonMember.Number => "a number",
        JsonMember.Integer => "a whole number",
        JsonMember.Boolean => "true or false",
        JsonMember.Object => "an object",
        JsonMember.Strings => "a list of strings",
        JsonMember.Numbers => "an object of numbers",
        _ => "a list of objects",
    };

    /// <summary>The JSON kinds content members have.</summary>
    private protected enum JsonMember
    {
        String,
        Number,
        Integer,
        Boolean,
        Object,
        Strings,
        Numbers,
        Objects,
    }
}

/// <summary>Number tokens: an unchanged value keeps its text; a new one is written in its shortest round-trip form.</summary>
internal static class JsonNumbers
{
    /// <summary>Whether a node is a number equal to <paramref name="value"/>.</summary>
    public static bool Holds(JsonNode? node, double value) =>
        node is JsonValue number && number.GetValueKind() == JsonValueKind.Number && number.GetValue<double>().Equals(value);

    /// <summary>
    /// A number token for a value, shortest round-trip, invariant culture. When it replaces a number written with a
    /// fraction (<c>1.0</c>), a whole value keeps that style.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite.</exception>
    public static JsonNode Create(double value, JsonNode? previous = null)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "JSON numbers must be finite.");
        }

        string text = value.ToString("R", CultureInfo.InvariantCulture);
        if (previous is JsonValue old && old.GetValueKind() == JsonValueKind.Number && old.ToJsonString().Contains('.', StringComparison.Ordinal)
            && text.AsSpan().IndexOfAny('.', 'E') < 0)
        {
            text += ".0";
        }

        return JsonNode.Parse(text)!;
    }

    /// <summary>A number token for a whole number.</summary>
    public static JsonNode Create(int value) => JsonNode.Parse(value.ToString(CultureInfo.InvariantCulture))!;
}
