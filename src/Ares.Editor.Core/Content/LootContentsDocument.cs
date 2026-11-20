using System.Text.Json.Nodes;

namespace Ares.Editor.Core.Content;

/// <summary>
/// <c>loot-contents.json</c>: what loot containers hold (ARES-Server <c>LootContentsDocument</c>). A container class or
/// tag with a type pool uses it, otherwise its tag's table pool.
/// </summary>
public sealed class LootContentsDocument : ContentDocument
{
    /// <summary>The file's name in the content folder.</summary>
    public const string FileName = "loot-contents.json";

    private static readonly string[] s_order = ["excludedDropTags", "tiers", "firstStackTier", "targetStacks", "tablePools", "extraStack", "typePools", "profiles", "looseItems"];

    private ObjectList<LootProfile>? _profiles;

    private LootContentsDocument(JsonObject node, string filePath)
        : base(node, s_order, filePath)
    {
        Expect("excludedDropTags", JsonMember.Strings, required: true);
        Expect("firstStackTier", JsonMember.String, required: false);
        Tiers = RequiredList("tiers", tier => new LootTier(tier));
        TargetStacks = RequiredList("targetStacks", target => new LootTargetStack(target));
        TablePools = RequiredList("tablePools", pool => new LootTablePool(pool));
        ExtraStack = RequiredObject("extraStack", extra => new LootExtraStack(extra));
        TypePools = RequiredList("typePools", pool => new LootTypePool(pool));
        LooseItems = OptionalObject("looseItems", loose => new LootLooseItems(loose));
        Expect("profiles", JsonMember.Objects, required: false);
        _profiles = Get("profiles") is JsonArray profiles ? new ObjectList<LootProfile>(profiles, profile => new LootProfile(profile)) : null;
    }

    /// <summary>Items with one of these drop tags (or one below it) go in no container.</summary>
    public IReadOnlyList<string> ExcludedDropTags
    {
        get => GetStrings("excludedDropTags");
        set => SetStrings("excludedDropTags", value, required: true);
    }

    /// <summary>The tiers, in order: an item belongs to the first whose drop tag it carries, else to the one without a drop tag.</summary>
    public ObjectList<LootTier> Tiers { get; }

    /// <summary>Name of the tier of a container's first stack, or null for none.</summary>
    public string? FirstStackTier
    {
        get => GetString("firstStackTier");
        set => SetString("firstStackTier", value);
    }

    /// <summary>The weighted numbers of stacks of a table pool's container that sets no number of its own.</summary>
    public ObjectList<LootTargetStack> TargetStacks { get; }

    /// <summary>The value loot tables each container category draws from.</summary>
    public ObjectList<LootTablePool> TablePools { get; }

    /// <summary>The extra stack some containers of table pools get.</summary>
    public LootExtraStack ExtraStack { get; }

    /// <summary>Contents of containers no value loot table fills.</summary>
    public ObjectList<LootTypePool> TypePools { get; }

    /// <summary>
    /// The contents profiles, in file order (empty when the file has no <c>profiles</c>): per container tag, container
    /// class or loose item tag, how the contents differ from the defaults above.
    /// </summary>
    public IReadOnlyList<LootProfile> Profiles => (IReadOnlyList<LootProfile>?)_profiles ?? [];

    /// <summary>What loose item sockets hold, or null when the file leaves it out (the editor shows it, it does not edit it).</summary>
    public LootLooseItems? LooseItems { get; }

    /// <summary>Reads the file.</summary>
    /// <param name="path">Path of <c>loot-contents.json</c>.</param>
    /// <returns>The document.</returns>
    /// <exception cref="EditorDataException">The file is missing, unreadable or not a valid contents file.</exception>
    public static LootContentsDocument Load(string path) => Read(path, (node, full) => new LootContentsDocument(node, full));

    /// <summary>Appends a tier.</summary>
    /// <param name="name">The tier's name.</param>
    /// <param name="weight">Its relative weight.</param>
    /// <returns>The tier.</returns>
    public LootTier AddTier(string name, double weight)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Tiers.Add(new LootTier(new JsonObject { ["name"] = name, ["weight"] = JsonNumbers.Create(weight) }));
    }

    /// <summary>Appends a table pool without tables.</summary>
    /// <param name="name">The pool's name.</param>
    /// <param name="tag">The container tag of its category.</param>
    /// <returns>The pool.</returns>
    public LootTablePool AddTablePool(string name, string tag)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(tag);
        return TablePools.Add(new LootTablePool(new JsonObject { ["name"] = name, ["tag"] = tag, ["tables"] = new JsonArray() }));
    }

    /// <summary>Appends a profile that changes nothing yet, adding the <c>profiles</c> list when needed.</summary>
    /// <param name="name">The profile's name.</param>
    /// <param name="tag">The tag it applies to, or null with <paramref name="containerClass"/>.</param>
    /// <param name="containerClass">The container class it applies to, or null with <paramref name="tag"/>.</param>
    /// <returns>The profile.</returns>
    public LootProfile AddProfile(string name, string? tag, string? containerClass)
    {
        ArgumentNullException.ThrowIfNull(name);
        if ((tag is null) == (containerClass is null))
        {
            throw new ArgumentException("A profile has exactly one of a tag and a class.");
        }

        _profiles ??= CreateProfiles();
        return _profiles.Add(LootProfile.Create(name, tag, containerClass));
    }

    /// <summary>Removes a profile; the <c>profiles</c> list stays, empty when it was the last.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>True when the file had it.</returns>
    public bool RemoveProfile(LootProfile profile) => _profiles?.Remove(profile) ?? false;

    /// <summary>Appends a type pool without entries.</summary>
    /// <param name="name">The pool's name.</param>
    /// <param name="minStacks">Fewest stacks.</param>
    /// <param name="maxStacks">Most stacks.</param>
    /// <returns>The pool.</returns>
    public LootTypePool AddTypePool(string name, int minStacks, int maxStacks)
    {
        ArgumentNullException.ThrowIfNull(name);
        return TypePools.Add(new LootTypePool(new JsonObject
        {
            ["name"] = name,
            ["stacks"] = LootCount.Create(minStacks, maxStacks).Node,
            ["entries"] = new JsonArray(),
        }));
    }

    private ObjectList<LootProfile> CreateProfiles()
    {
        JsonArray array = [];
        Put("profiles", array);
        return new ObjectList<LootProfile>(array, profile => new LootProfile(profile));
    }
}

/// <summary>A weighted number of stacks (ARES-Server <c>LootTargetStacksDocument</c>).</summary>
public sealed class LootTargetStack : JsonObjectView
{
    private static readonly string[] s_order = ["target", "weight"];

    internal LootTargetStack(JsonObject node)
        : base(node, s_order)
    {
        Expect("target", JsonMember.Integer, required: true);
        Expect("weight", JsonMember.Number, required: true);
    }

    /// <summary>Number of stacks.</summary>
    public int Target => GetInteger("target");

    /// <summary>Relative weight of the number.</summary>
    public double Weight
    {
        get => GetNumber("weight") ?? 0;
        set => SetNumber("weight", value);
    }
}

/// <summary>A tier of loot items (ARES-Server <c>LootTierDocument</c>): how often a container's next stack comes from it.</summary>
public sealed class LootTier : JsonObjectView
{
    private static readonly string[] s_order = ["name", "dropTag", "weight", "areaMultipliers"];

    internal LootTier(JsonObject node)
        : base(node, s_order)
    {
        Expect("name", JsonMember.String, required: true);
        Expect("dropTag", JsonMember.String, required: false);
        Expect("weight", JsonMember.Number, required: true);
        Expect("areaMultipliers", JsonMember.Numbers, required: false);
        AreaMultipliers = new NumberMap(this, "areaMultipliers", required: false);
    }

    /// <summary>The tier's name.</summary>
    public string Name
    {
        get => GetString("name")!;
        set => SetString("name", value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Drop tag of its items (an item with this tag or one below it), or null for the tier of every other item.</summary>
    public string? DropTag
    {
        get => GetString("dropTag");
        set => SetString("dropTag", value);
    }

    /// <summary>Relative weight.</summary>
    public double Weight
    {
        get => GetNumber("weight") ?? 0;
        set => SetNumber("weight", value);
    }

    /// <summary>Factor on the weight by area loot value; 1 without an entry.</summary>
    public NumberMap AreaMultipliers { get; }
}

/// <summary>
/// The value loot tables a container category draws from (ARES-Server <c>LootTablePoolDocument</c>): the tag itself and
/// every tag below it that has no pool of its own.
/// </summary>
public sealed class LootTablePool : JsonObjectView
{
    private static readonly string[] s_order = ["name", "tag", "tables", "stacks", "extraStack"];

    internal LootTablePool(JsonObject node)
        : base(node, s_order)
    {
        Expect("name", JsonMember.String, required: true);
        Expect("tag", JsonMember.String, required: true);
        Expect("tables", JsonMember.Strings, required: true);
        Expect("extraStack", JsonMember.Boolean, required: false);
        Stacks = OptionalObject("stacks", stacks => new LootCount(stacks));
    }

    /// <summary>Name of the pool, as the server's logs show it.</summary>
    public string Name
    {
        get => GetString("name")!;
        set => SetString("name", value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Container tag of the category.</summary>
    public string Tag
    {
        get => GetString("tag")!;
        set => SetString("tag", value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Value loot table asset names.</summary>
    public IReadOnlyList<string> Tables
    {
        get => GetStrings("tables");
        set => SetStrings("tables", value, required: true);
    }

    /// <summary>Number of stacks, drawn uniformly, or null for the weighted targets (<see cref="LootContentsDocument.TargetStacks"/>).</summary>
    public LootCount? Stacks { get; private set; }

    /// <summary>Whether containers of the pool may get the extra stack; true when the file leaves it out.</summary>
    public bool ExtraStack
    {
        get => Get("extraStack") is null || GetBoolean("extraStack");
        set => SetBoolean("extraStack", value);
    }

    /// <summary>Sets the number of stacks, adding the member when needed.</summary>
    /// <param name="min">Fewest stacks.</param>
    /// <param name="max">Most stacks.</param>
    /// <returns>The range.</returns>
    public LootCount SetStacks(int min, int max)
    {
        Stacks = LootList.SetCount(this, "stacks", Stacks, min, max);
        return Stacks;
    }

    /// <summary>Removes the number of stacks, so the weighted targets apply.</summary>
    public void RemoveStacks()
    {
        RemoveMember("stacks");
        Stacks = null;
    }
}

/// <summary>The extra stack of <c>loot-contents.json</c> (ARES-Server <c>LootExtraStackDocument</c>).</summary>
public sealed class LootExtraStack : JsonObjectView
{
    private static readonly string[] s_order = ["chance", "entries"];

    internal LootExtraStack(JsonObject node)
        : base(node, s_order)
    {
        Expect("chance", JsonMember.Number, required: true);
        Entries = RequiredList("entries", entry => new LootTypeEntry(entry));
    }

    /// <summary>Chance from 0 to 1 that a container of a pool with the extra stack gets it.</summary>
    public double Chance
    {
        get => GetNumber("chance") ?? 0;
        set => SetNumber("chance", value);
    }

    /// <summary>What it draws from.</summary>
    public ObjectList<LootTypeEntry> Entries { get; }

    /// <summary>Appends an entry with weight 1.</summary>
    /// <param name="dropTag">Drop tag of its items.</param>
    /// <param name="min">Smallest stack.</param>
    /// <param name="max">Largest stack.</param>
    /// <returns>The entry.</returns>
    public LootTypeEntry AddEntry(string dropTag, int min, int max) => Entries.Add(LootTypeEntry.Create(dropTag, min, max));
}

/// <summary>
/// An entry of a type pool or of the extra stack (ARES-Server <c>LootTypeEntryDocument</c>): items chosen by drop tag,
/// and how each stack of them looks.
/// </summary>
public sealed class LootTypeEntry : JsonObjectView
{
    private static readonly string[] s_order = ["dropTag", "amount", "weight", "excludedDropTags"];

    internal LootTypeEntry(JsonObject node)
        : base(node, s_order)
    {
        Expect("dropTag", JsonMember.String, required: true);
        Expect("weight", JsonMember.Number, required: false);
        Expect("excludedDropTags", JsonMember.Strings, required: false);
        Amount = RequiredObject("amount", amount => new LootCount(amount));
    }

    /// <summary>Drop tag of the items: an item with this tag or one below it.</summary>
    public string DropTag
    {
        get => GetString("dropTag")!;
        set => SetString("dropTag", value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Items per stack.</summary>
    public LootCount Amount { get; }

    /// <summary>Relative weight among the entries of its pool; 1 when the file leaves it out.</summary>
    public double Weight
    {
        get => GetNumber("weight") ?? 1;
        set => SetNumber("weight", value);
    }

    /// <summary>Items with one of these drop tags (or one below it) are left out; an empty list leaves the member out.</summary>
    public IReadOnlyList<string> ExcludedDropTags
    {
        get => GetStrings("excludedDropTags");
        set => SetStrings("excludedDropTags", value, required: false);
    }

    internal static LootTypeEntry Create(string dropTag, int min, int max)
    {
        ArgumentNullException.ThrowIfNull(dropTag);
        return new LootTypeEntry(new JsonObject { ["dropTag"] = dropTag, ["amount"] = LootCount.Create(min, max).Node, ["weight"] = JsonNumbers.Create(1) });
    }
}

/// <summary>
/// Contents of containers no value loot table fills (ARES-Server <c>LootTypePoolDocument</c>): the listed classes and
/// the listed tags with every tag below them.
/// </summary>
public sealed class LootTypePool : JsonObjectView
{
    private static readonly string[] s_order = ["name", "classes", "tags", "stacks", "entries"];

    internal LootTypePool(JsonObject node)
        : base(node, s_order)
    {
        Expect("name", JsonMember.String, required: true);
        Expect("classes", JsonMember.Strings, required: false);
        Expect("tags", JsonMember.Strings, required: false);
        Stacks = RequiredObject("stacks", stacks => new LootCount(stacks));
        Entries = RequiredList("entries", entry => new LootTypeEntry(entry));
    }

    /// <summary>Name of the pool, as the server's logs show it.</summary>
    public string Name
    {
        get => GetString("name")!;
        set => SetString("name", value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Container class paths that use the pool; an empty list leaves the member out.</summary>
    public IReadOnlyList<string> Classes
    {
        get => GetStrings("classes");
        set => SetStrings("classes", value, required: false);
    }

    /// <summary>Container tags that use the pool, each with the tags below it; an empty list leaves the member out.</summary>
    public IReadOnlyList<string> Tags
    {
        get => GetStrings("tags");
        set => SetStrings("tags", value, required: false);
    }

    /// <summary>Number of stacks, drawn uniformly.</summary>
    public LootCount Stacks { get; }

    /// <summary>The entries; each stack draws one by weight, then one of its items evenly.</summary>
    public ObjectList<LootTypeEntry> Entries { get; }

    /// <summary>Appends an entry with weight 1.</summary>
    /// <param name="dropTag">Drop tag of its items.</param>
    /// <param name="min">Smallest stack.</param>
    /// <param name="max">Largest stack.</param>
    /// <returns>The entry.</returns>
    public LootTypeEntry AddEntry(string dropTag, int min, int max) => Entries.Add(LootTypeEntry.Create(dropTag, min, max));
}
