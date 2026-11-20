using System.Text.Json.Nodes;

namespace Ares.Editor.Core.Content;

/// <summary>A whole-number range of a content file, both ends included (ARES-Server <c>LootCountDocument</c>).</summary>
public sealed class LootCount : JsonObjectView
{
    private static readonly string[] s_order = ["min", "max"];

    internal LootCount(JsonObject node)
        : base(node, s_order)
    {
        Expect("min", JsonMember.Integer, required: true);
        Expect("max", JsonMember.Integer, required: true);
    }

    /// <summary>The lowest value.</summary>
    public int Min
    {
        get => GetInteger("min");
        set => SetInteger("min", value);
    }

    /// <summary>The highest value.</summary>
    public int Max
    {
        get => GetInteger("max");
        set => SetInteger("max", value);
    }

    /// <summary>A new range object.</summary>
    internal static LootCount Create(int min, int max) => new(new JsonObject { ["min"] = JsonNumbers.Create(min), ["max"] = JsonNumbers.Create(max) });
}
