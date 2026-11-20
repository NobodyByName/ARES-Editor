using System.Text.Json.Nodes;

namespace Ares.Editor.Core.Content;

/// <summary>
/// <c>loot-rules.json</c>: the operator's loot rules, in order (ARES-Server <c>LootRulesDocument</c>). For each setting,
/// the last rule that covers a container and sets it wins.
/// </summary>
public sealed class LootRulesDocument : ContentDocument
{
    /// <summary>The file's name in the content folder.</summary>
    public const string FileName = "loot-rules.json";

    private static readonly string[] s_order = ["rules"];

    private LootRulesDocument(JsonObject node, string filePath)
        : base(node, s_order, filePath)
    {
        Rules = RequiredList("rules", rule => new LootRule(rule));
    }

    /// <summary>The rules, in file order.</summary>
    public ObjectList<LootRule> Rules { get; }

    /// <summary>Reads the file.</summary>
    /// <param name="path">Path of <c>loot-rules.json</c>.</param>
    /// <returns>The document.</returns>
    /// <exception cref="EditorDataException">The file is missing, unreadable or not a valid rules file.</exception>
    public static LootRulesDocument Load(string path) => Read(path, (node, full) => new LootRulesDocument(node, full));

    /// <summary>Appends a new rule that sets nothing yet (the server refuses a rule that sets nothing).</summary>
    /// <param name="name">The rule's name.</param>
    /// <returns>The rule.</returns>
    public LootRule AddRule(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Rules.Add(new LootRule(new JsonObject { ["name"] = name }));
    }
}

/// <summary>
/// One loot rule (ARES-Server <c>LootRuleDocument</c>): it covers the containers listed by id, those whose container tag
/// is one of its tags or below it, or every container when it lists neither; and sets any of a spawn chance, a group
/// fill, contents and a reveal delay for them.
/// </summary>
public sealed class LootRule : JsonObjectView
{
    private static readonly string[] s_order = ["name", "tags", "containers", "spawnChance", "loot", "groupFill", "revealDelaySeconds"];

    internal LootRule(JsonObject node)
        : base(node, s_order)
    {
        Expect("name", JsonMember.String, required: false);
        Expect("tags", JsonMember.Strings, required: false);
        Expect("containers", JsonMember.Strings, required: false);
        Expect("spawnChance", JsonMember.Number, required: false);
        Expect("groupFill", JsonMember.Numbers, required: false);
        Expect("revealDelaySeconds", JsonMember.Number, required: false);
        Loot = OptionalObject("loot", loot => new LootList(loot));
        GroupFill = new NumberMap(this, "groupFill", required: false);
    }

    /// <summary>Name the server's logs show, or null to name the rule by its position (<c>#1</c>, ...).</summary>
    public string? Name
    {
        get => GetString("name");
        set => SetString("name", string.IsNullOrEmpty(value) ? null : value);
    }

    /// <summary>Container tags the rule covers, each with the tags below it; an empty list leaves the member out.</summary>
    public IReadOnlyList<string> Tags
    {
        get => GetStrings("tags");
        set => SetStrings("tags", value, required: false);
    }

    /// <summary>Ids of the containers the rule covers (see <c>LootContainer.Id</c>); an empty list leaves the member out.</summary>
    public IReadOnlyList<string> Containers
    {
        get => GetStrings("containers");
        set => SetStrings("containers", value, required: false);
    }

    /// <summary>
    /// Chance from 0 to 1 that a covered container spawns, replacing the game's decision (chance, group caps, limiters),
    /// or null to keep it.
    /// </summary>
    public double? SpawnChance
    {
        get => GetNumber("spawnChance");
        set => SetNumber("spawnChance", value);
    }

    /// <summary>Contents a covered container gets instead of the default roll, or null to keep the roll.</summary>
    public LootList? Loot { get; private set; }

    /// <summary>
    /// Weight of each number of filled sockets per group, keyed by the number (<c>"0"</c>, <c>"1"</c>, ...): how many
    /// sockets of each group the rule covers get a container. Sockets only; empty to keep the game's decision.
    /// </summary>
    public NumberMap GroupFill { get; }

    /// <summary>
    /// Wait in seconds after a first search's hold before a covered container's contents start to reveal, or null to keep
    /// the container class's own delay.
    /// </summary>
    public double? RevealDelaySeconds
    {
        get => GetNumber("revealDelaySeconds");
        set => SetNumber("revealDelaySeconds", value);
    }

    /// <summary>Gives the rule contents (an empty item list) when it has none.</summary>
    /// <returns>The contents.</returns>
    public LootList SetLoot()
    {
        if (Loot is not null)
        {
            return Loot;
        }

        Loot = new LootList(new JsonObject { ["items"] = new JsonArray() });
        Put("loot", Loot.Node);
        return Loot;
    }

    /// <summary>Removes the rule's contents, so covered containers keep the default roll.</summary>
    public void RemoveLoot()
    {
        RemoveMember("loot");
        Loot = null;
    }
}

/// <summary>
/// The contents a loot rule gives (ARES-Server <c>LootListDocument</c>): a number of stacks, each an item drawn by
/// weight from <see cref="Items"/>.
/// </summary>
public sealed class LootList : JsonObjectView
{
    private static readonly string[] s_order = ["items", "stacks"];

    internal LootList(JsonObject node)
        : base(node, s_order)
    {
        Items = RequiredList("items", item => new LootItem(item));
        Stacks = OptionalObject("stacks", stacks => new LootCount(stacks));
    }

    /// <summary>The items to draw from; the server needs at least one.</summary>
    public ObjectList<LootItem> Items { get; }

    /// <summary>How many stacks, or null for the contents file's weighted targets.</summary>
    public LootCount? Stacks { get; private set; }

    /// <summary>Appends an item with weight 1 and amount 1.</summary>
    /// <param name="item">The item's technical name (<c>DA_Item_…</c>).</param>
    /// <returns>The entry.</returns>
    public LootItem AddItem(string item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Items.Add(new LootItem(new JsonObject { ["item"] = item }));
    }

    /// <summary>Sets the number of stacks, adding the member when needed.</summary>
    /// <param name="min">Fewest stacks.</param>
    /// <param name="max">Most stacks.</param>
    /// <returns>The range.</returns>
    public LootCount SetStacks(int min, int max)
    {
        Stacks = SetCount(this, "stacks", Stacks, min, max);
        return Stacks;
    }

    /// <summary>Removes the number of stacks, so the contents file's weighted targets apply.</summary>
    public void RemoveStacks()
    {
        RemoveMember("stacks");
        Stacks = null;
    }

    /// <summary>Sets a range member of a view: in place when it exists, else as a new member.</summary>
    internal static LootCount SetCount(JsonObjectView owner, string name, LootCount? existing, int min, int max)
    {
        if (existing is not null)
        {
            existing.Min = min;
            existing.Max = max;
            return existing;
        }

        LootCount count = LootCount.Create(min, max);
        owner.Put(name, count.Node);
        return count;
    }
}

/// <summary>An item of a loot rule's contents (ARES-Server <c>LootItemDocument</c>).</summary>
public sealed class LootItem : JsonObjectView
{
    private static readonly string[] s_order = ["item", "weight", "amount"];

    internal LootItem(JsonObject node)
        : base(node, s_order)
    {
        Expect("item", JsonMember.String, required: true);
        Expect("weight", JsonMember.Number, required: false);
        Amount = OptionalObject("amount", amount => new LootCount(amount));
    }

    /// <summary>The item's technical name (<c>DA_Item_…</c>).</summary>
    public string Item
    {
        get => GetString("item")!;
        set => SetString("item", value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Relative chance among the rule's items; 1 when the file leaves it out.</summary>
    public double Weight
    {
        get => GetNumber("weight") ?? 1;
        set => SetNumber("weight", value);
    }

    /// <summary>Stack size range, or null for 1.</summary>
    public LootCount? Amount { get; private set; }

    /// <summary>Sets the stack size range, adding the member when needed.</summary>
    /// <param name="min">Smallest stack.</param>
    /// <param name="max">Largest stack.</param>
    /// <returns>The range.</returns>
    public LootCount SetAmount(int min, int max)
    {
        Amount = LootList.SetCount(this, "amount", Amount, min, max);
        return Amount;
    }

    /// <summary>Removes the stack size range, so the amount is 1.</summary>
    public void RemoveAmount()
    {
        RemoveMember("amount");
        Amount = null;
    }
}
