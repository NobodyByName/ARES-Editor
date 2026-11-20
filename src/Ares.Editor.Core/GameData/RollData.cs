namespace Ares.Editor.Core.GameData;

/// <summary>A range of counts, both ends included (ARES-Server <c>LootRange</c>).</summary>
/// <param name="Min">Smallest.</param>
/// <param name="Max">Largest.</param>
public readonly record struct CountRange(int Min, int Max);

/// <summary>A container tag an item may spawn under, except below the excluded tags (ARES-Server <c>LootPlacement</c>).</summary>
/// <param name="SocketTag">The tag; the item may go in containers with this tag or one below it.</param>
/// <param name="ExcludeTags">Tags below it the item may not go in.</param>
public sealed record ItemPlacement(string SocketTag, IReadOnlyList<string> ExcludeTags);

/// <summary>
/// An item the default loot roll may put in a container or on a loose item socket: ARES-Server's
/// <c>LoadoutDefinitions.PlainItems</c>, with the members of <c>ItemDefinition</c> the roll reads.
/// </summary>
/// <param name="TechnicalName">The item's technical name (<c>DA_Item_…</c>).</param>
/// <param name="DisplayName">The item's in-game name, or null when the item value cache has none.</param>
/// <param name="MaxStack">The largest stack of the item.</param>
/// <param name="DropTags">Its drop tags (<c>Item.Drop.*</c>).</param>
/// <param name="Placements">The container tags it may spawn under.</param>
/// <param name="AvoidGroups">Drop tags it never shares a container with.</param>
public sealed record RollItem(
    string TechnicalName,
    string? DisplayName,
    int MaxStack,
    IReadOnlyList<string> DropTags,
    IReadOnlyList<ItemPlacement> Placements,
    IReadOnlyList<string> AvoidGroups)
{
    /// <summary>The in-game name, else the technical name.</summary>
    public string Label => DisplayName ?? TechnicalName;
}

/// <summary>An entry of a value loot table (ARES-Server <c>StaticValueLootTableEntry</c>).</summary>
/// <param name="ExactItem">The item it names, or null when it selects by query.</param>
/// <param name="Query">The query over drop tags, or null when it names an item or has none.</param>
/// <param name="StackSize">Items per stack.</param>
/// <param name="Durability">Durability in percent.</param>
public sealed record ValueTableEntry(string? ExactItem, TagQuery? Query, CountRange StackSize, CountRange Durability)
{
    /// <summary>Whether the entry selects an item.</summary>
    /// <param name="item">The item.</param>
    /// <returns>True when it selects it.</returns>
    public bool Selects(RollItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return ExactItem is not null ? string.Equals(ExactItem, item.TechnicalName, StringComparison.OrdinalIgnoreCase)
            : Query is not null && Query.Matches([.. item.DropTags]);
    }
}

/// <summary>A value loot table (<c>StaticValueLootTable_*</c>).</summary>
/// <param name="Name">Asset name.</param>
/// <param name="Entries">Entries in authored order.</param>
public sealed record ValueTable(string Name, IReadOnlyList<ValueTableEntry> Entries);

/// <summary>A weighted target number of stacks of a loot container (ARES-Server <c>LootTargetStacks</c>, <c>targetStacks</c> of <c>loot-contents.json</c>).</summary>
/// <param name="Target">Number of stacks.</param>
/// <param name="Weight">Relative weight.</param>
public readonly record struct TargetStacks(int Target, double Weight);

/// <summary>What the default loot roll reads from the game data.</summary>
/// <param name="Items">The plain items, by usable index.</param>
/// <param name="ValueTables">The value loot tables.</param>
/// <param name="SlotCounts">Slots of each container class that decodes, by class path.</param>
public sealed record RollData(
    IReadOnlyList<RollItem> Items,
    IReadOnlyList<ValueTable> ValueTables,
    IReadOnlyDictionary<string, int> SlotCounts)
{
    // ARES-Server LootContainerSupport.Unsupported: the container classes the server does not serve.
    private static readonly HashSet<string> s_unsupportedClasses = new(StringComparer.Ordinal)
    {
        "/Game/Pioneer/Environment/Nature/Locations/Generic/Bushes/Mullein_01/BP_SalvageContainer_GreatMullein.BP_SalvageContainer_GreatMullein_C",
        "/Game/Pioneer/Environment/Nature/Locations/Generic/Trees/ApricotTree_01/BP_SalvageContainer_Apricot.BP_SalvageContainer_Apricot_C",
        "/Game/Pioneer/Environment/Nature/Locations/Generic/Trees/LemonTree_01/BP_SalvageContainer_Lemon.BP_SalvageContainer_Lemon_C",
        "/Game/Pioneer/Environment/Nature/Locations/Generic/Trees/Olive_01/BP_SalvageContainer_Olive.BP_SalvageContainer_Olive_C",
        "/Game/Pioneer/Environment/Nature/Locations/MountainCompound/Snowpile_01/BP_SalvageContainer_Snowpile.BP_SalvageContainer_Snowpile_C",
        "/Game/Pioneer/Environment/Nature/Locations/TheBlueGate/Bushes/CandleBerries_01/BP_SocketContainer_CandleBerries.BP_SocketContainer_CandleBerries_C",
        "/Game/Pioneer/Environment/Props/Commercial/Planters/BP_SalvageContainer_Shrub_03_H.BP_SalvageContainer_Shrub_03_H_C",
        "/Game/Pioneer/Environment/Props/Dam/Planter_01/BP_SalvageContainer_Fertilizer.BP_SalvageContainer_Fertilizer_C",
        "/Game/Pioneer/Environment/Props/Dam/WaterTank_01/BP_SalvageContainer_Moss.BP_SalvageContainer_Moss_C",
        "/Game/Pioneer/Environment/Props/Interactables/Android_01/BP_SocketContainer_Android_01_Lid_A.BP_SocketContainer_Android_01_Lid_A_C",
        "/Game/Pioneer/Environment/Props/Interactables/Android_01/BP_SocketContainer_Android_01_Lid_B.BP_SocketContainer_Android_01_Lid_B_C",
        "/Game/Pioneer/Environment/Props/Raider/AmmoBox_01/BP_SocketContainer_Raider_AmmoBox_01_Lid_A_Dynamic.BP_SocketContainer_Raider_AmmoBox_01_Lid_A_Dynamic_C",
        "/Game/Pioneer/Environment/Props/Raider/AmmoBox_01/BP_SocketContainer_Raider_AmmoBox_01_Lid_A_DynamicHighTier.BP_SocketContainer_Raider_AmmoBox_01_Lid_A_DynamicHighTier_C",
        "/Game/Pioneer/Environment/Props/_Generic/BirdNest_01/BP_SocketContainer_BirdNest_A.BP_SocketContainer_BirdNest_A_C",
        "/Game/Pioneer/Environment/Props/_Generic/BirdNest_01/BP_SocketContainer_BirdNest_B.BP_SocketContainer_BirdNest_B_C",
        "/Game/Pioneer/Environment/Props/_Generic/BirdNest_01/BP_SocketContainer_BirdNest_C.BP_SocketContainer_BirdNest_C_C",
        "/Game/Pioneer/Environment/South/Base/Meshes/Rock_XL_07/BP_SalvageContainer_Moss_Rock.BP_SalvageContainer_Moss_Rock_C",
        "/Game/Pioneer/Environment/South/Base/Vegetation/Agave_01/BP_SalvageContainer_Agave.BP_SalvageContainer_Agave_C",
        "/Game/Pioneer/Environment/South/Forest/Vegetation/AleppoPine_01/BP_SalvageContainer_Resin.BP_SalvageContainer_Resin_C",
        "/Game/Pioneer/Environment/South/Swamp/Lootables/BP_SalvageContainer_MushroomCluster_01_A.BP_SalvageContainer_MushroomCluster_01_A_C",
        "/Game/Pioneer/Environment/South/VolcanicRed/Meshes/Cliff_XL_01/BP_SalvageContainer_VolcanicRock_Temp.BP_SalvageContainer_VolcanicRock_Temp_C",
    };

    /// <summary>
    /// The slots of a container class the server serves (ARES-Server <c>LootSocketClasses.Served</c>), or null when it does
    /// not serve it or it does not decode.
    /// </summary>
    /// <param name="classPath">The class path.</param>
    /// <returns>The slot count, or null.</returns>
    public int? ServedSlots(string classPath) =>
        !s_unsupportedClasses.Contains(classPath) && SlotCounts.TryGetValue(classPath, out int slots) ? slots : null;
}
