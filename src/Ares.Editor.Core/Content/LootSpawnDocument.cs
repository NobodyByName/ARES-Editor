using System.Text.Json.Nodes;
using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Content;

/// <summary>
/// <c>loot-spawn.json</c>: how loot containers spawn (ARES-Server <c>LootSpawnDocument</c>). Every socket rolls the chance
/// of its tag times the multiplier of its area's loot value (at most 1); placed containers roll the chance of their
/// class's tag; dynamic loot spawners place only <see cref="SpawnerClasses"/>; loose item sockets roll
/// <see cref="LooseChances"/> times the same multiplier.
/// </summary>
public sealed class LootSpawnDocument : ContentDocument
{
    /// <summary>The file's name in the content folder.</summary>
    public const string FileName = "loot-spawn.json";

    private static readonly string[] s_order =
        ["chances", "areaMultipliers", "groupCaps", "maxPerGroup", "limiters", "limiterDefaults", "spawnerClasses", "spawnerCount", "looseChances"];

    private LootSpawnDocument(JsonObject node, string filePath)
        : base(node, s_order, filePath)
    {
        Expect("chances", JsonMember.Numbers, required: true);
        Expect("areaMultipliers", JsonMember.Numbers, required: true);
        Expect("groupCaps", JsonMember.Boolean, required: true);
        Expect("maxPerGroup", JsonMember.Integer, required: true);
        Expect("limiters", JsonMember.Boolean, required: true);
        LimiterDefaults = RequiredObject("limiterDefaults", defaults => new LootLimiterDefaults(defaults));
        Expect("spawnerClasses", JsonMember.Strings, required: true);
        Expect("spawnerCount", JsonMember.Integer, required: true);
        Expect("looseChances", JsonMember.Numbers, required: false);
        Chances = new NumberMap(this, "chances", required: true);
        AreaMultipliers = new NumberMap(this, "areaMultipliers", required: true);
        LooseChances = new NumberMap(this, "looseChances", required: false);
    }

    /// <summary>
    /// Chance from 0 to 1 by container tag (keys compare without case); a tag without an entry uses its nearest parent's,
    /// else 0.
    /// </summary>
    public NumberMap Chances { get; }

    /// <summary>
    /// Factor on the chance by area loot value (<c>Unknown</c>, <c>Low</c>, <c>Medium</c>, <c>High</c>); a value without an
    /// entry uses the one of <c>Unknown</c>, else 1.
    /// </summary>
    public NumberMap AreaMultipliers { get; }

    /// <summary>
    /// Chance from 0 to 1 that a loose item socket gets an item, by socket tag (keys compare without case); a tag without
    /// an entry uses its nearest parent's, else 0. The area multipliers and the limiters apply as for container sockets.
    /// </summary>
    public NumberMap LooseChances { get; }

    /// <summary>Whether the game's most containers per group (a container type's max per group) apply to sockets.</summary>
    public bool GroupCaps
    {
        get => GetBoolean("groupCaps");
        set => SetBoolean("groupCaps", value);
    }

    /// <summary>Most containers per group of a container type whose game data sets none.</summary>
    public int MaxPerGroup
    {
        get => GetInteger("maxPerGroup");
        set => SetInteger("maxPerGroup", value);
    }

    /// <summary>What a placement limiter and its limit entries use for the members the game data leaves unset.</summary>
    public LootLimiterDefaults LimiterDefaults { get; }

    /// <summary>Containers a dynamic loot spawner whose game data sets no number places per run.</summary>
    public int SpawnerCount
    {
        get => GetInteger("spawnerCount");
        set => SetInteger("spawnerCount", value);
    }

    /// <summary>
    /// What the server refuses in the defaults of this file (ARES-Server <c>LootSettingsContent</c>): a negative
    /// <see cref="MaxPerGroup"/>, a <see cref="SpawnerCount"/> below 1, and limiter defaults that name no shape or have a
    /// negative radius or limit.
    /// </summary>
    public IReadOnlyList<string> Problems
    {
        get
        {
            List<string> problems = [];
            if (MaxPerGroup < 0)
            {
                problems.Add("maxPerGroup must be 0 or more");
            }

            if (SpawnerCount < 1)
            {
                problems.Add("spawnerCount must be 1 or more");
            }

            if (!Enum.GetNames<LootLimiterShape>().Contains(LimiterDefaults.Shape, StringComparer.Ordinal))
            {
                problems.Add($"limiterDefaults: {LimiterDefaults.Shape} is not a limiter shape ({string.Join(", ", Enum.GetNames<LootLimiterShape>())})");
            }

            if (!(double.IsFinite(LimiterDefaults.Radius) && LimiterDefaults.Radius >= 0) || LimiterDefaults.Limit < 0)
            {
                problems.Add("limiterDefaults: radius and limit must be 0 or more");
            }

            return problems;
        }
    }

    /// <summary>What the game data's unset members take from this file, as the editor applies it when it reads the game data.</summary>
    /// <exception cref="EditorDataException">The limiter defaults name no limiter shape.</exception>
    public LootSpawnDefaults Defaults => new(MaxPerGroup, LimiterDefaults.ShapeValue, LimiterDefaults.Radius, LimiterDefaults.Unlimited, LimiterDefaults.Limit, SpawnerCount);

    /// <summary>Whether the map's placement limiters cap the sockets they cover.</summary>
    public bool Limiters
    {
        get => GetBoolean("limiters");
        set => SetBoolean("limiters", value);
    }

    /// <summary>Container classes (class paths) dynamic loot spawners may place, unless a rule sets a spawner's chance.</summary>
    public IReadOnlyList<string> SpawnerClasses
    {
        get => GetStrings("spawnerClasses");
        set => SetStrings("spawnerClasses", value, required: true);
    }

    /// <summary>Reads the file.</summary>
    /// <param name="path">Path of <c>loot-spawn.json</c>.</param>
    /// <returns>The document.</returns>
    /// <exception cref="EditorDataException">The file is missing, unreadable or not a valid spawn file.</exception>
    public static LootSpawnDocument Load(string path) => Read(path, (node, full) => new LootSpawnDocument(node, full));
}

/// <summary>
/// <c>limiterDefaults</c> of <c>loot-spawn.json</c> (ARES-Server <c>LootLimiterDefaultsDocument</c>): what a placement
/// limiter and its limit entries use for the members the game data leaves unset.
/// </summary>
public sealed class LootLimiterDefaults : JsonObjectView
{
    private static readonly string[] s_order = ["shape", "radius", "unlimited", "limit"];

    internal LootLimiterDefaults(JsonObject node)
        : base(node, s_order)
    {
        Expect("shape", JsonMember.String, required: true);
        Expect("radius", JsonMember.Number, required: true);
        Expect("unlimited", JsonMember.Boolean, required: true);
        Expect("limit", JsonMember.Integer, required: true);
    }

    /// <summary>Name of the shape: <c>None</c>, <c>Sphere</c>, <c>Box</c> or <c>Volumes</c>.</summary>
    public string Shape
    {
        get => GetString("shape") ?? string.Empty;
        set => SetString("shape", value);
    }

    /// <summary>The shape <see cref="Shape"/> names.</summary>
    /// <exception cref="EditorDataException">It names no limiter shape.</exception>
    public LootLimiterShape ShapeValue => Enum.GetNames<LootLimiterShape>().Contains(Shape, StringComparer.Ordinal)
        ? Enum.Parse<LootLimiterShape>(Shape)
        : throw new EditorDataException($"limiterDefaults: {Shape} is not a limiter shape ({string.Join(", ", Enum.GetNames<LootLimiterShape>())}).");

    /// <summary>Sphere radius, or twice the box's half extent, in centimetres.</summary>
    public double Radius
    {
        get => GetNumber("radius") ?? 0;
        set => SetNumber("radius", value);
    }

    /// <summary>Whether a limit entry caps nothing.</summary>
    public bool Unlimited => GetBoolean("unlimited");

    /// <summary>Spawns a limit entry allows for its tag.</summary>
    public int Limit => GetInteger("limit");
}
