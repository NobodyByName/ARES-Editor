using System.Text.Json.Nodes;

namespace Ares.Editor.Core.Content;

/// <summary>
/// A contents profile of <c>loot-contents.json</c> (ARES-Server <c>LootProfileDocument</c>): how the containers of one tag
/// (with the tags below it) or one class, or the loose item sockets of one tag, shape what their item source offers. A
/// member the profile leaves out inherits from the profiles above it, then from the file's defaults.
/// </summary>
public sealed class LootProfile : JsonObjectView
{
    private static readonly string[] s_order = ["name", "tag", "class", "only", "weights", "add", "tiers", "firstStackTier", "stacks", "extraStack"];

    internal LootProfile(JsonObject node)
        : base(node, s_order)
    {
        Expect("name", JsonMember.String, required: true);
        Expect("tag", JsonMember.String, required: false);
        Expect("class", JsonMember.String, required: false);
        Expect("tiers", JsonMember.Numbers, required: false);
        Expect("firstStackTier", JsonMember.String, required: false);
        Only = OptionalList("only", match => new LootMatch(match));
        Weights = OptionalList("weights", weight => new LootWeight(weight));
        Additions = OptionalList("add", addition => new LootAddition(addition));
        Tiers = new NumberMap(this, "tiers", required: false);
        Stacks = OptionalObject("stacks", stacks => new LootCount(stacks));
        ExtraStack = OptionalObject("extraStack", extra => new LootProfileExtraStack(extra));
    }

    /// <summary>The profile's name, unique in the file.</summary>
    public string Name
    {
        get => GetString("name")!;
        set => SetString("name", value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>The container or loose item tag it applies to (with the tags below it), or null for a class profile.</summary>
    public string? Tag => GetString("tag");

    /// <summary>The container class path it applies to, or null for a tag profile.</summary>
    public string? Class => GetString("class");

    /// <summary>Keeps only the candidates matching one of these; null when the profile leaves it out (inherits).</summary>
    public ObjectList<LootMatch>? Only { get; private set; }

    /// <summary>Multipliers on the pick weight of matching items; null when the profile has none.</summary>
    public ObjectList<LootWeight>? Weights { get; private set; }

    /// <summary>Items the source does not offer, added with their amount and tier; null when the profile has none.</summary>
    public ObjectList<LootAddition>? Additions { get; private set; }

    /// <summary>Multiplier on each tier's weight, by tier name.</summary>
    public NumberMap Tiers { get; }

    /// <summary>
    /// Whether the profile sets the first stack's tier: to a tier name, or to none with JSON <c>null</c>. False when the
    /// member is left out (inherits).
    /// </summary>
    public bool SetsFirstStackTier => Node.ContainsKey("firstStackTier");

    /// <summary>The first stack's tier when <see cref="SetsFirstStackTier"/>, null for none.</summary>
    public string? FirstStackTier => GetString("firstStackTier");

    /// <summary>Number of stacks, or null to inherit.</summary>
    public LootCount? Stacks { get; private set; }

    /// <summary>The extra stack's chance and entries, or null to inherit both.</summary>
    public LootProfileExtraStack? ExtraStack { get; private set; }

    /// <summary>Sets the first stack's tier: a tier name, or null to turn the forced first tier off.</summary>
    /// <param name="tier">The tier name, or null.</param>
    public void SetFirstStackTier(string? tier) => Put("firstStackTier", tier is null ? null : JsonValue.Create(tier));

    /// <summary>Removes the first stack's tier, so it inherits.</summary>
    public void InheritFirstStackTier() => RemoveMember("firstStackTier");

    /// <summary>Appends an <c>only</c> match, adding the list when needed.</summary>
    /// <param name="dropTag">Drop tag to match, or null with <paramref name="item"/>.</param>
    /// <param name="item">Technical name to match, or null with <paramref name="dropTag"/>.</param>
    /// <returns>The match.</returns>
    public LootMatch AddOnly(string? dropTag, string? item)
    {
        Only ??= CreateList("only", match => new LootMatch(match));
        return Only.Add(new LootMatch(LootMatch.CreateNode(dropTag, item)));
    }

    /// <summary>Removes an <c>only</c> match; removing the last one removes the member, so the profile inherits.</summary>
    /// <param name="match">The match.</param>
    public void RemoveOnly(LootMatch match)
    {
        if (Only is null || !Only.Remove(match) || Only.Count > 0)
        {
            return;
        }

        RemoveMember("only");
        Only = null;
    }

    /// <summary>Appends a weight, adding the list when needed.</summary>
    /// <param name="dropTag">Drop tag to match, or null with <paramref name="item"/>.</param>
    /// <param name="item">Technical name to match, or null with <paramref name="dropTag"/>.</param>
    /// <param name="multiplier">Multiplier on the matching items' pick weight.</param>
    public void AddWeight(string? dropTag, string? item, double multiplier)
    {
        Weights ??= CreateList("weights", weight => new LootWeight(weight));
        JsonObject node = LootMatch.CreateNode(dropTag, item);
        node["multiplier"] = JsonNumbers.Create(multiplier);
        Weights.Add(new LootWeight(node));
    }

    /// <summary>Removes a weight; removing the last one removes the member.</summary>
    /// <param name="weight">The weight.</param>
    public void RemoveWeight(LootWeight weight)
    {
        if (Weights is null || !Weights.Remove(weight) || Weights.Count > 0)
        {
            return;
        }

        RemoveMember("weights");
        Weights = null;
    }

    /// <summary>Appends an added item or drop tag, adding the list when needed.</summary>
    /// <param name="dropTag">Drop tag of the items to add, or null with <paramref name="item"/>.</param>
    /// <param name="item">Technical name of the item to add, or null with <paramref name="dropTag"/>.</param>
    /// <param name="min">Smallest stack.</param>
    /// <param name="max">Largest stack.</param>
    /// <returns>The addition.</returns>
    public LootAddition AddAddition(string? dropTag, string? item, int min, int max)
    {
        Additions ??= CreateList("add", addition => new LootAddition(addition));
        JsonObject node = LootMatch.CreateNode(dropTag, item);
        node["amount"] = LootCount.Create(min, max).Node;
        return Additions.Add(new LootAddition(node));
    }

    /// <summary>Removes an addition; removing the last one removes the member.</summary>
    /// <param name="addition">The addition.</param>
    public void RemoveAddition(LootAddition addition)
    {
        if (Additions is null || !Additions.Remove(addition) || Additions.Count > 0)
        {
            return;
        }

        RemoveMember("add");
        Additions = null;
    }

    /// <summary>Sets the number of stacks, adding the member when needed.</summary>
    /// <param name="min">Fewest stacks.</param>
    /// <param name="max">Most stacks.</param>
    public void SetStacks(int min, int max) => Stacks = LootList.SetCount(this, "stacks", Stacks, min, max);

    /// <summary>Removes the number of stacks, so it inherits.</summary>
    public void RemoveStacks()
    {
        RemoveMember("stacks");
        Stacks = null;
    }

    /// <summary>Gives the profile an extra stack member (setting neither part yet) when it has none.</summary>
    public void SetExtraStack()
    {
        if (ExtraStack is not null)
        {
            return;
        }

        ExtraStack = new LootProfileExtraStack([]);
        Put("extraStack", ExtraStack.Node);
    }

    /// <summary>Removes the extra stack member, so both parts inherit.</summary>
    public void RemoveExtraStack()
    {
        RemoveMember("extraStack");
        ExtraStack = null;
    }

    internal static LootProfile Create(string name, string? tag, string? containerClass)
    {
        JsonObject node = new() { ["name"] = name };
        if (tag is not null)
        {
            node["tag"] = tag;
        }

        if (containerClass is not null)
        {
            node["class"] = containerClass;
        }

        return new LootProfile(node);
    }

    private ObjectList<T>? OptionalList<T>(string name, Func<JsonObject, T> wrap)
        where T : JsonObjectView
    {
        Expect(name, JsonMember.Objects, required: false);
        return Get(name) is JsonArray array ? new ObjectList<T>(array, wrap) : null;
    }

    private ObjectList<T> CreateList<T>(string name, Func<JsonObject, T> wrap)
        where T : JsonObjectView
    {
        JsonArray array = [];
        Put(name, array);
        return new ObjectList<T>(array, wrap);
    }
}

/// <summary>
/// A match of a contents profile (ARES-Server <c>LootMatchDocument</c>): the items carrying a drop tag (or one below it),
/// or one item by technical name. A valid match sets exactly one of the two.
/// </summary>
public class LootMatch : JsonObjectView
{
    private static readonly string[] s_order = ["dropTag", "item"];

    internal LootMatch(JsonObject node)
        : this(node, s_order)
    {
    }

    private protected LootMatch(JsonObject node, string[] order)
        : base(node, order)
    {
        Expect("dropTag", JsonMember.String, required: false);
        Expect("item", JsonMember.String, required: false);
    }

    /// <summary>The drop tag it matches, or null.</summary>
    public string? DropTag => GetString("dropTag");

    /// <summary>The technical name it matches, or null.</summary>
    public string? Item => GetString("item");

    /// <summary>What the match shows: the drop tag or the technical name.</summary>
    public string Text => DropTag ?? Item ?? string.Empty;

    /// <summary>
    /// The key profiles merge it by: the same drop tag (ignoring case) or the same item. A nearer profile's entry replaces
    /// a farther one's with the same key.
    /// </summary>
    public string Key => DropTag is { } tag ? "dropTag:" + tag.ToUpperInvariant() : "item:" + Item;

    internal static JsonObject CreateNode(string? dropTag, string? item)
    {
        if ((dropTag is null) == (item is null))
        {
            throw new ArgumentException("A match has exactly one of a drop tag and an item.");
        }

        return dropTag is not null ? new JsonObject { ["dropTag"] = dropTag } : new JsonObject { ["item"] = item };
    }
}

/// <summary>A weight of a contents profile (ARES-Server <c>LootProfileWeightDocument</c>): a multiplier on the matching items' pick weight.</summary>
public sealed class LootWeight : LootMatch
{
    private static readonly string[] s_order = ["dropTag", "item", "multiplier"];

    internal LootWeight(JsonObject node)
        : base(node, s_order)
    {
        Expect("multiplier", JsonMember.Number, required: true);
    }

    /// <summary>The multiplier, at least 0; 0 removes the items.</summary>
    public double Multiplier
    {
        get => GetNumber("multiplier") ?? 1;
        set => SetNumber("multiplier", value);
    }
}

/// <summary>
/// An addition of a contents profile (ARES-Server <c>LootProfileAddDocument</c>): items the source does not offer, whatever
/// their placement lists say, with a stack size and optionally a tier.
/// </summary>
public sealed class LootAddition : LootMatch
{
    private static readonly string[] s_order = ["dropTag", "item", "amount", "tier"];

    internal LootAddition(JsonObject node)
        : base(node, s_order)
    {
        Expect("tier", JsonMember.String, required: false);
        Amount = RequiredObject("amount", amount => new LootCount(amount));
    }

    /// <summary>Items per stack.</summary>
    public LootCount Amount { get; }

    /// <summary>The tier the items count in, or null for each item's own tier.</summary>
    public string? Tier
    {
        get => GetString("tier");
        set => SetString("tier", value);
    }
}

/// <summary>The extra stack of a contents profile: each part it leaves out inherits.</summary>
public sealed class LootProfileExtraStack : JsonObjectView
{
    private static readonly string[] s_order = ["chance", "entries"];

    internal LootProfileExtraStack(JsonObject node)
        : base(node, s_order)
    {
        Expect("chance", JsonMember.Number, required: false);
        Expect("entries", JsonMember.Objects, required: false);
        Entries = Get("entries") is JsonArray array ? new ObjectList<LootTypeEntry>(array, entry => new LootTypeEntry(entry)) : null;
    }

    /// <summary>Chance from 0 to 1 of the extra stack, or null to inherit; 0 turns it off.</summary>
    public double? Chance
    {
        get => GetNumber("chance");
        set => SetNumber("chance", value);
    }

    /// <summary>What it draws from, or null to inherit.</summary>
    public ObjectList<LootTypeEntry>? Entries { get; private set; }

    /// <summary>Gives the extra stack its own entries, copies of <paramref name="from"/>, replacing any it has.</summary>
    /// <param name="from">The entries to copy (usually the inherited ones).</param>
    /// <returns>The entries.</returns>
    public ObjectList<LootTypeEntry> SetEntries(IEnumerable<LootTypeEntry> from)
    {
        ArgumentNullException.ThrowIfNull(from);
        JsonArray array = [.. from.Select(JsonNode? (entry) => entry.Node.DeepClone())];
        Put("entries", array);
        Entries = new ObjectList<LootTypeEntry>(array, entry => new LootTypeEntry(entry));
        return Entries;
    }

    /// <summary>Removes the entries, so they inherit.</summary>
    public void RemoveEntries()
    {
        RemoveMember("entries");
        Entries = null;
    }

    /// <summary>Appends an entry with weight 1.</summary>
    /// <param name="dropTag">Drop tag of its items.</param>
    /// <param name="min">Smallest stack.</param>
    /// <param name="max">Largest stack.</param>
    public void AddEntry(string dropTag, int min, int max) => (Entries ?? SetEntries([])).Add(LootTypeEntry.Create(dropTag, min, max));
}
