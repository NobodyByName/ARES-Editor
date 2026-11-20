using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Tests;

/// <summary>
/// A small game data folder written by hand: one map with two sockets (one in a high-value area, one with a colour
/// variant, one without a container type), a placed container, a spawner, two loose item sockets (one on nature)
/// and two loot placement limiters (a volume around the first desk and the sofa, a sphere around the third desk). The
/// drawer type, the spawner and one limit leave members unset, so they take <see cref="SpawnDefaults"/>.
/// </summary>
internal static class SyntheticGameData
{
    public const string Map = "TestMap";
    public const string Drawer = "/Game/BP_Drawer.BP_Drawer_C";
    public const string DrawerClean = "/Game/BP_Drawer_Clean.BP_Drawer_Clean_C";
    public const string Box = "/Game/BP_Box.BP_Box_C";
    public const string Unlisted = "/Game/BP_Other.BP_Other_C";
    public const string DrawerTag = "Socket.LootContainer.Tech.Drawer";
    public const string BoxTag = "Socket.LootContainer.Raider.Box";
    public const string SocketId = "Cell1/Desk_1/StaticMeshComponent0/LootSocket";
    public const string VariantSocketId = "Cell1/Desk_2/StaticMeshComponent0/LootSocket";
    public const string UntypedSocketId = "/Desk_3/StaticMeshComponent0/LootSocket";
    public const string PlacedId = "/BP_Box_C_1";
    public const string UnlistedPlacedId = "/BP_Other_C_1";
    public const string SpawnerId = "/ActorSpawner_1";
    public const string LooseId = "Cell1/Sofa_1/StaticMeshComponent0/Loot.01";
    public const string LooseTag = "Socket.Loot.Sofa.01";
    public const string NatureLooseId = "/Pine_1/StaticMeshComponent0/Loot.02";
    public const string NatureLooseTag = "Socket.Loot.Nature.Pine";

    /// <summary>The shipped spawn settings' defaults for the game data's unset members.</summary>
    public static LootSpawnDefaults SpawnDefaults { get; } = new(2, LootLimiterShape.Sphere, 1000, true, 0, 1);

    public static void Write(TemporaryFolder folder)
    {
        folder.Write("manifest.json", """
            {"gameBuildId": 123, "extractorVersion": "2026110201", "files": [
              {"name": "loot-container-types.json", "sha256": "x", "rowCount": 2},
              {"name": "maps/TestMap/loot-sockets.json", "sha256": "x", "rowCount": 3},
              {"name": "maps/TestMap/world.json", "sha256": "x", "rowCount": 1}
            ]}
            """);
        // Items: a plain one with a name, a rifle and an armor (only the Ferro I and the Light Shield are container items),
        // an item with child slots, a currency and the unsupported zipline.
        folder.Write("usable-items.json", """
            [
              {"usableIndex": 0, "technicalName": "DA_Item_Battery", "assetId": 11, "baseAssetId": 11, "category": 11, "maxStack": 15, "hasChildSlots": false,
               "dropTags": ["Item.Drop.Type.Salvage.Material.Common"], "lootPlacements": [{"socketTag": "Socket.LootContainer.Tech", "excludeTags": []}],
               "lootAvoidGroups": []},
              {"usableIndex": 1, "technicalName": "DA_Item_BattleRifle_BreachAction_01", "assetId": 12, "baseAssetId": 99, "category": 1, "maxStack": 1, "hasChildSlots": true},
              {"usableIndex": 2, "technicalName": "DA_Item_BattleRifle_Other_01", "assetId": 13, "baseAssetId": 13, "category": 1, "maxStack": 1, "hasChildSlots": true},
              {"usableIndex": 3, "technicalName": "DA_Item_Armor_LightBody", "assetId": 14, "baseAssetId": 14, "category": 10, "maxStack": 1, "hasChildSlots": false},
              {"usableIndex": 4, "technicalName": "DA_Item_Armor_HeavyBody", "assetId": 15, "baseAssetId": 15, "category": 10, "maxStack": 1, "hasChildSlots": false},
              {"usableIndex": 5, "technicalName": "DA_Item_Backpack_01", "assetId": 16, "baseAssetId": 16, "category": 12, "maxStack": 1, "hasChildSlots": true},
              {"usableIndex": 6, "technicalName": "DA_Item_GrindCurrency", "assetId": 17, "baseAssetId": 17, "category": 17, "maxStack": 1000, "hasChildSlots": false},
              {"usableIndex": 7, "technicalName": "DA_Item_Zipline", "assetId": 18, "baseAssetId": 18, "category": 7, "maxStack": 3, "hasChildSlots": false},
              {"usableIndex": 8, "technicalName": "DA_Item_Wires", "assetId": 19, "baseAssetId": 19, "category": 11, "maxStack": 10, "hasChildSlots": false,
               "dropTags": ["Item.Drop.Type.Salvage.Material.Rare"], "lootPlacements": [{"socketTag": "Socket.LootContainer", "excludeTags": []},
               {"socketTag": "Socket.Loot", "excludeTags": ["Socket.Loot.Nature"]}], "lootAvoidGroups": []}
            ]
            """);
        folder.Write("item-values.json", """
            [
              {"assetId": 11, "name": "Battery", "scrapValue": 1, "economyValue": 1},
              {"assetId": 12, "name": "Ferro", "scrapValue": 1, "economyValue": 1}
            ]
            """);
        folder.Write("loot-container-types.json", $$"""
            [
              {"tag": "Socket.LootContainer.Raider.Box", "classes": [{"class": "{{Box}}", "archetype": "a"}], "variants": [], "maxPerGroup": 1},
              {"tag": "Socket.LootCOntainer.Tech.Drawer", "classes": [{"class": "{{Drawer}}", "archetype": "a"}],
               "variants": [{"material": "/Game/MI_Clean.MI_Clean", "classes": [{"class": "{{DrawerClean}}", "archetype": "a"}]}], "maxPerGroup": null}
            ]
            """);
        // One value table: Battery by name, every rare material by query (any of Item.Drop.Type.Salvage.Material.Rare).
        folder.Write("static-value-loot-tables.json", """
            [
              {"name": "StaticValueLootTable_Test", "path": "/Game/T", "entries": [
                {"queryType": 0, "itemQuery": null, "exactItem": "DA_Item_Battery", "qualityLevel": -1, "stackSize": {"min": 2, "max": 4},
                 "durability": {"min": 100, "max": 100}, "durabilityCap": {"min": 100, "max": 100}, "distribution": 1},
                {"queryType": 1, "itemQuery": {"tagDictionary": ["Item.Drop.Type.Salvage.Material.Rare"], "tokenStream": [0, 1, 1, 1, 0]}, "exactItem": null,
                 "qualityLevel": -1, "stackSize": {"min": 1, "max": 1}, "durability": {"min": 100, "max": 100}, "durabilityCap": {"min": 100, "max": 100}, "distribution": 1}
              ]}
            ]
            """);
        folder.Write("loot-container-classes.json", $$"""
            [
              {"container": {"class": "{{Drawer}}", "archetype": "a"}, "slotCount": 8},
              {"container": {"class": "{{DrawerClean}}", "archetype": "a"}, "slotCount": 8},
              {"container": {"class": "{{Box}}", "archetype": "a"}, "slotCount": 1}
            ]
            """);
        const string MapFolder = $"maps/{Map}/";
        folder.Write(MapFolder + "world.json", """[{"worldName": "TestMap"}]""");
        folder.Write(MapFolder + "loot-socket-meshes.json", """[{"path": "/Game/SM_Desk", "materials": ["/Game/MI_Dirty.MI_Dirty"]}]""");
        folder.Write(MapFolder + "points-of-interest.json", """
            [
              {"actor": "Area_1", "name": null, "parent": null, "lootValue": 3, "lootThemes": [], "hulls": [{"planes": [
                {"normal": {"x": 1, "y": 0, "z": 0}, "distance": 100},
                {"normal": {"x": -1, "y": 0, "z": 0}, "distance": 0}
              ]}]},
              {"actor": "Area_2", "name": null, "parent": null, "lootValue": 2, "lootThemes": [], "hulls": [{"planes": [
                {"normal": {"x": 1, "y": 0, "z": 0}, "distance": 1000}
              ]}]}
            ]
            """);
        folder.Write(MapFolder + "loot-sockets.json", """
            [
              {"cell": "Cell1", "actor": "Desk_1", "component": "StaticMeshComponent0", "socket": "LootSocket", "containerTag": "Socket.LootContainer.Tech.Drawer",
               "location": {"x": 50, "y": 1, "z": 2}, "rotation": {"pitch": 0, "yaw": 90, "roll": 0}, "scale": {"x": 1, "y": 1, "z": 1}, "mesh": 0,
               "overrideMaterials": [], "dataLayer": null},
              {"cell": "Cell1", "actor": "Desk_2", "component": "StaticMeshComponent0", "socket": "LootSocket", "containerTag": "Socket.LootContainer.Tech.Drawer",
               "location": {"x": 500, "y": 0, "z": 0}, "rotation": {"pitch": 0, "yaw": 0, "roll": 0}, "scale": {"x": 1, "y": 1, "z": 1}, "mesh": 0,
               "overrideMaterials": ["/Game/MI_Clean.MI_Clean"], "dataLayer": "DataLayer_A"},
              {"cell": "", "actor": "Desk_3", "component": "StaticMeshComponent0", "socket": "LootSocket", "containerTag": "Socket.LootContainer.Unknown",
               "location": {"x": 5000, "y": 0, "z": 0}, "rotation": {"pitch": 0, "yaw": 0, "roll": 0}, "scale": {"x": 1, "y": 1, "z": 1}, "mesh": 0,
               "overrideMaterials": [], "dataLayer": null}
            ]
            """);
        folder.Write(MapFolder + "placed-loot.json", $$"""
            [
              {"cell": "", "actor": "BP_Box_C_1", "class": "{{Box}}", "location": {"x": 10, "y": 0, "z": 0}, "rotation": {"pitch": 0, "yaw": 0, "roll": 0},
               "scale": {"x": 1, "y": 1, "z": 1}, "dataLayer": null},
              {"cell": "", "actor": "BP_Other_C_1", "class": "{{Unlisted}}", "location": {"x": 10, "y": 0, "z": 0}, "rotation": {"pitch": 0, "yaw": 0, "roll": 0},
               "scale": {"x": 1, "y": 1, "z": 1}, "dataLayer": null}
            ]
            """);
        folder.Write(MapFolder + "loose-item-sockets.json", """
            [
              {"cell": "Cell1", "actor": "Sofa_1", "component": "StaticMeshComponent0", "socket": "Loot.01", "tag": "Socket.Loot.Sofa.01",
               "location": {"x": 30, "y": 0, "z": 0}, "rotation": {"pitch": 0, "yaw": 0, "roll": 0}, "options": 1, "dataLayer": null},
              {"cell": "", "actor": "Pine_1", "component": "StaticMeshComponent0", "socket": "Loot.02", "tag": "Socket.Loot.Nature.Pine",
               "location": {"x": 5000, "y": 10, "z": -20}, "rotation": {"pitch": 5, "yaw": 45, "roll": 0}, "options": 5, "dataLayer": "DataLayer_A"}
            ]
            """);
        folder.Write(MapFolder + "loot-limiters.json", """
            [
              {"cell": "Cell1", "actor": "LootPlacementLimiter_1", "name": "LootPlacementLimiter_Office", "dataLayer": null, "shape": 3,
               "location": {"x": 0, "y": 0, "z": 0}, "rotation": {"pitch": 0, "yaw": 0, "roll": 0}, "radius": 1000,
               "volumes": [{"actor": "Volume_1", "hulls": [{"planes": [
                 {"normal": {"x": 1, "y": 0, "z": 0}, "distance": 100}, {"normal": {"x": -1, "y": 0, "z": 0}, "distance": 0},
                 {"normal": {"x": 0, "y": 1, "z": 0}, "distance": 50}, {"normal": {"x": 0, "y": -1, "z": 0}, "distance": 50},
                 {"normal": {"x": 0, "y": 0, "z": 1}, "distance": 50}, {"normal": {"x": 0, "y": 0, "z": -1}, "distance": 50}
               ]}]}],
               "limits": [{"tag": "Socket.LootContainer.Tech", "unlimited": false, "limit": 2}, {"tag": "Socket.Loot", "unlimited": false, "limit": 1},
                          {"tag": "Socket.LootContainer.Tech.Drawer", "unlimited": null, "limit": null}]},
              {"cell": "", "actor": "LootPlacementLimiter_2", "name": "LootPlacementLimiter_Field", "dataLayer": "DataLayer_A", "shape": 1,
               "location": {"x": 5000, "y": 0, "z": 0}, "rotation": {"pitch": 0, "yaw": 0, "roll": 0}, "radius": 100, "volumes": [],
               "limits": [{"tag": "Socket.LootContainer", "unlimited": false, "limit": 1}]}
            ]
            """);
        folder.Write(MapFolder + "dynamic-loot-spawners.json", $$"""
            [
              {"cell": "", "actor": "ActorSpawner_1", "dataLayer": null, "classes": [{"class": "{{Box}}", "weight": 1}, {"class": "{{Unlisted}}", "weight": 2}],
               "points": [{"location": {"x": 2000, "y": 0, "z": 0}, "rotation": {"pitch": 0, "yaw": 45, "roll": 0}, "weight": 1},
                          {"location": {"x": 20, "y": 0, "z": 0}, "rotation": {"pitch": 0, "yaw": 0, "roll": 0}, "weight": 3}],
               "count": null, "randomRotation": false}
            ]
            """);
        folder.Write(MapFolder + "dynamic-loot-groups.json", """
            [
              {"id": "ActivityNode_1/0", "path": ["TestMap", "Containers"], "rounds": ["Standard", "NightRaid"], "min": 1, "max": 1, "spawners": [0],
               "parentSelections": []},
              {"id": "ActivityNode_2/0", "path": ["TestMap", "Event"], "rounds": ["Coldsnap"], "min": 0, "max": 1, "spawners": [0, 7],
               "parentSelections": [{"node": "ActivityNode_3", "min": 1, "max": 1, "of": 2, "weighted": false}]}
            ]
            """);
    }
}
