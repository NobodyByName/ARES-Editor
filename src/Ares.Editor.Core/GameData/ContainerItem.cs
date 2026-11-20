namespace Ares.Editor.Core.GameData;

/// <summary>An item a loot rule may put in a container.</summary>
/// <param name="TechnicalName">The item's technical name (<c>DA_Item_…</c>), as loot rules name it.</param>
/// <param name="DisplayName">The item's in-game name, or null when the item value cache has none.</param>
/// <param name="MaxStack">The largest stack of the item.</param>
public sealed record ContainerItem(string TechnicalName, string? DisplayName, int MaxStack)
{
    // ARES-Server ItemCategory values of the categories with gameplay of their own.
    private const int Launcher = 0;
    private const int Rifle = 1;
    private const int Armor = 10;
    private const int MeleeWeapon = 15;
    private const int Generator = 16;
    private const int Currency = 17;
    private const int MetaItem = 19;
    private const int Unarmed = 20;
    private const int Augment = 21;

    // ARES-Server ItemTechnicalNames.FerroI and LightShield: the weapon and shield ARES carries.
    private const string FerroI = "DA_Item_BattleRifle_BreachAction_01";
    private const string LightShield = "DA_Item_Armor_LightBody";

    // ARES-Server ItemSupport.Unsupported.
    private static readonly HashSet<string> s_unsupported = new(StringComparer.Ordinal) { "DA_Item_Zipline" };

    /// <summary>
    /// The catalogue items a world loot container can hold, by usable index, as ARES-Server's
    /// <c>LoadoutDefinitions.ContainerItems</c> selects them: the supported items without child slots outside the
    /// categories with gameplay of their own, and the Ferro I and the Light Shield.
    /// </summary>
    internal static List<ContainerItem> Select(List<UsableItemRow> catalogue, List<ItemValueRow> values)
    {
        Dictionary<long, string> names = [];
        foreach (ItemValueRow value in values)
        {
            names.TryAdd(value.AssetId, value.Name);
        }

        return [.. catalogue.Where(IsContainerItem).OrderBy(item => item.UsableIndex)
            .Select(item => new ContainerItem(item.TechnicalName, names.GetValueOrDefault(item.AssetId), item.MaxStack))];
    }

    /// <summary>
    /// The items the default loot roll draws from, by usable index, as ARES-Server's <c>LoadoutDefinitions.PlainItems</c>
    /// selects them: the supported items without child slots outside the categories with gameplay of their own.
    /// </summary>
    internal static List<RollItem> SelectPlain(List<UsableItemRow> catalogue, List<ItemValueRow> values)
    {
        Dictionary<long, string> names = [];
        foreach (ItemValueRow value in values)
        {
            names.TryAdd(value.AssetId, value.Name);
        }

        return [.. catalogue.Where(IsPlain).OrderBy(item => item.UsableIndex)
            .Select(item => new RollItem(item.TechnicalName, names.GetValueOrDefault(item.AssetId), item.MaxStack, item.DropTags ?? [],
                [.. (item.LootPlacements ?? []).Select(placement => new ItemPlacement(placement.SocketTag, placement.ExcludeTags))], item.LootAvoidGroups ?? []))];
    }

    private static bool IsContainerItem(UsableItemRow item) => item.TechnicalName is FerroI or LightShield || IsPlain(item);

    private static bool IsPlain(UsableItemRow item) =>
        item is { HasChildSlots: false, Category: not (Launcher or Rifle or Armor or Augment or MeleeWeapon or Unarmed or Currency or Generator or MetaItem) }
        && !s_unsupported.Contains(item.TechnicalName);
}
