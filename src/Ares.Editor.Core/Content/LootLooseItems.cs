using System.Text.Json.Nodes;

namespace Ares.Editor.Core.Content;

/// <summary>
/// What loose item sockets hold (ARES-Server <c>LooseItemsDocument</c>): an item of the listed value loot tables whose
/// placement list accepts the socket's tag, or a stack from the entries of the socket's pool. Read-only in the editor.
/// </summary>
public sealed class LootLooseItems : JsonObjectView
{
    private static readonly string[] s_order = ["tables", "amount", "pools"];

    internal LootLooseItems(JsonObject node)
        : base(node, s_order)
    {
        Expect("tables", JsonMember.Strings, required: true);
        Amount = RequiredObject("amount", amount => new LootCount(amount));
        Pools = RequiredList("pools", pool => new LootLoosePool(pool));
    }

    /// <summary>Value loot table asset names of the table items.</summary>
    public IReadOnlyList<string> Tables => GetStrings("tables");

    /// <summary>Items per stack of a table item.</summary>
    public LootCount Amount { get; }

    /// <summary>The pools of loose item tags.</summary>
    public ObjectList<LootLoosePool> Pools { get; }
}

/// <summary>A pool of loose item tags (ARES-Server <c>LooseItemPoolDocument</c>).</summary>
public sealed class LootLoosePool : JsonObjectView
{
    private static readonly string[] s_order = ["name", "tags", "tableWeight", "entries"];

    internal LootLoosePool(JsonObject node)
        : base(node, s_order)
    {
        Expect("name", JsonMember.String, required: true);
        Expect("tags", JsonMember.Strings, required: true);
        Expect("tableWeight", JsonMember.Number, required: false);
        Entries = RequiredList("entries", entry => new LootTypeEntry(entry));
    }

    /// <summary>Name of the pool, as the server's logs show it.</summary>
    public string Name => GetString("name")!;

    /// <summary>Loose item tags of the pool, each with the tags below it that no other pool lists.</summary>
    public IReadOnlyList<string> Tags => GetStrings("tags");

    /// <summary>Weight of the table items against the entries; 1 when the file leaves it out.</summary>
    public double TableWeight => GetNumber("tableWeight") ?? 1;

    /// <summary>The entries, each with its own weight.</summary>
    public ObjectList<LootTypeEntry> Entries { get; }
}
