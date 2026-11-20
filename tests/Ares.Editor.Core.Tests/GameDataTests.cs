using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Tests;

/// <summary>Reading a game data folder: ids, tags, areas and classes of each container, and the tag hierarchy.</summary>
public sealed class GameDataTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();

    public GameDataTests()
    {
        SyntheticGameData.Write(_folder);
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void The_manifest_gives_the_build_stamp_and_maps()
    {
        EditorGameData data = EditorGameData.Load(_folder.Path, SyntheticGameData.SpawnDefaults);

        Assert.Equal(_folder.Path, data.Directory);
        Assert.Equal(123, data.Manifest.GameBuildId);
        Assert.Equal("2026110201", data.Manifest.ExtractorVersion);
        Assert.Equal([SyntheticGameData.Map], data.Manifest.MapNames);
        Assert.Equal(SyntheticGameData.Map, Assert.Single(data.Maps).Name);
    }

    [Fact]
    public void Sockets_get_their_id_tag_area_and_variant_classes()
    {
        MapLoot map = EditorGameData.Load(_folder.Path, SyntheticGameData.SpawnDefaults).Maps[0];

        LootContainer socket = map.Find(SyntheticGameData.SocketId)!;
        Assert.Equal(LootContainerSource.Socket, socket.Source);
        Assert.Equal(new WorldVector(50, 1, 2), socket.Location);
        Assert.Equal(new WorldRotation(0, 90, 0), socket.Rotation);
        Assert.Equal([SyntheticGameData.DrawerTag], socket.Tags);
        Assert.Equal(LootArea.High, socket.Area);
        Assert.Equal([SyntheticGameData.Drawer], socket.ClassPaths);
        Assert.Null(socket.DataLayer);
        Assert.Empty(socket.Points);
        Assert.Empty(socket.SpawnerClasses);
        Assert.Empty(socket.SpawnerGroups);
        Assert.Equal(0, socket.SpawnerCount);

        LootContainer variant = map.Find(SyntheticGameData.VariantSocketId)!;
        Assert.Equal([SyntheticGameData.DrawerClean], variant.ClassPaths);
        Assert.Equal(LootArea.Medium, variant.Area);
        Assert.Equal("DataLayer_A", variant.DataLayer);

        LootContainer untyped = map.Find(SyntheticGameData.UntypedSocketId)!;
        Assert.Empty(untyped.ClassPaths);
        Assert.Equal(LootArea.Unknown, untyped.Area);
        Assert.Null(map.Find("missing"));
    }

    [Fact]
    public void Placed_containers_and_spawners_take_the_tags_of_their_classes()
    {
        EditorGameData data = EditorGameData.Load(_folder.Path, SyntheticGameData.SpawnDefaults);
        MapLoot map = data.Maps[0];

        Assert.Equal(SyntheticGameData.BoxTag, data.ContainerTagOf(SyntheticGameData.Box));
        Assert.Equal("Socket.LootCOntainer.Tech.Drawer", data.ContainerTagOf(SyntheticGameData.DrawerClean));
        Assert.Null(data.ContainerTagOf(SyntheticGameData.Unlisted));
        Assert.Equal([SyntheticGameData.BoxTag], map.Find(SyntheticGameData.PlacedId)!.Tags);
        Assert.Empty(map.Find(SyntheticGameData.UnlistedPlacedId)!.Tags);

        LootContainer spawner = map.Find(SyntheticGameData.SpawnerId)!;
        Assert.Equal(LootContainerSource.Spawner, spawner.Source);
        Assert.Equal([SyntheticGameData.BoxTag], spawner.Tags);
        Assert.Equal([SyntheticGameData.Box, SyntheticGameData.Unlisted], spawner.ClassPaths);
        Assert.Equal(new SpawnerClass(SyntheticGameData.Unlisted, 2, null), spawner.SpawnerClasses[1]);
        Assert.Equal(1, spawner.SpawnerCount);
        Assert.Equal(["ActivityNode_1/0", "ActivityNode_2/0"], spawner.SpawnerGroups.Select(group => group.Id));
        SpawnerGroup eventGroup = spawner.SpawnerGroups[1];
        Assert.Equal(["TestMap", "Event"], eventGroup.Path);
        Assert.Equal(["Coldsnap"], eventGroup.Rounds);
        Assert.Equal((0, 1, 2), (eventGroup.Min, eventGroup.Max, eventGroup.Spawners));
        Assert.Equal(new SpawnerGroupChoice("ActivityNode_3", 1, 1, 2), Assert.Single(eventGroup.Choices));
        Assert.Empty(spawner.SpawnerGroups[0].Choices);
        Assert.Equal(new WorldVector(2000, 0, 0), spawner.Location);
        Assert.Equal(new WorldRotation(0, 45, 0), spawner.Rotation);
        Assert.Equal(LootArea.Unknown, spawner.Area);
        Assert.Equal([LootArea.Unknown, LootArea.High], spawner.Points.Select(point => point.Area));
        Assert.Equal(3, spawner.Points[1].Weight);
        Assert.Equal(map.Containers.Count, map.Containers.Select(container => container.Id).Distinct().Count());
    }

    [Fact]
    public void Loose_item_sockets_are_read_with_their_place_options_and_area_but_are_not_containers()
    {
        EditorGameData data = EditorGameData.Load(_folder.Path, SyntheticGameData.SpawnDefaults);
        MapLoot map = data.Maps[0];

        Assert.Equal(
            [
                new LooseItemSocket(SyntheticGameData.LooseId, SyntheticGameData.LooseTag, new WorldVector(30, 0, 0), new WorldRotation(0, 0, 0),
                    LooseItemOptions.EnablePhysics, null, LootArea.High),
                new LooseItemSocket(SyntheticGameData.NatureLooseId, SyntheticGameData.NatureLooseTag, new WorldVector(5000, 10, -20), new WorldRotation(5, 45, 0),
                    LooseItemOptions.EnablePhysics | LooseItemOptions.IgnoreTiltLimit, "DataLayer_A", LootArea.Unknown),
            ],
            map.LooseItemSockets);
        Assert.Equal([false, true], map.LooseItemSockets.Select(socket => socket.IsNature));
        Assert.Equal([SyntheticGameData.LooseTag, SyntheticGameData.NatureLooseTag], map.LooseItemTags);
        Assert.Equal(1, map.IndexOfLooseItemSocket(SyntheticGameData.NatureLooseId));
        Assert.Equal(-1, map.IndexOfLooseItemSocket(SyntheticGameData.SocketId));
        Assert.Same(map.LooseItemSockets[0], map.FindLooseItemSocket(SyntheticGameData.LooseId));
        Assert.Null(map.Find(SyntheticGameData.LooseId));
        Assert.Null(map.FindLooseItemSocket(SyntheticGameData.PlacedId));
        Assert.True(data.HasLooseItemTag("socket.loot"));
        Assert.True(data.HasLooseItemTag(SyntheticGameData.LooseTag));
        Assert.False(data.HasLooseItemTag("Socket.Loot.Sofa.02"));
        Assert.False(data.HasLooseItemTag("Socket.LootContainer"));
        Assert.Null(data.TagTree.Find(SyntheticGameData.LooseTag));
    }

    [Fact]
    public void The_tag_tree_merges_tags_that_differ_only_in_case()
    {
        EditorGameData data = EditorGameData.Load(_folder.Path, SyntheticGameData.SpawnDefaults);

        LootTagNode root = Assert.Single(data.TagTree.Roots);
        Assert.Equal("Socket", root.Segment);
        LootTagNode loot = Assert.Single(root.Children);
        Assert.Equal("Socket.LootContainer", loot.Tag);
        Assert.Equal(["Raider", "Tech"], loot.Children.Select(child => child.Segment));
        Assert.Null(loot.ContainerType);
        LootTagNode drawer = data.TagTree.Find("socket.lootcontainer.tech.drawer")!;
        Assert.Equal(2, drawer.ContainerType!.MaxPerGroup);
        Assert.Equal([SyntheticGameData.Drawer, SyntheticGameData.DrawerClean], drawer.ContainerType.ClassPaths);
        Assert.Equal(2, data.ContainerTypes.Count);
        Assert.Null(data.TagTree.Find("Socket.Other"));
    }

    [Fact]
    public void Categories_are_the_first_tags_where_the_tree_branches()
    {
        LootTagTree tree = EditorGameData.Load(_folder.Path, SyntheticGameData.SpawnDefaults).TagTree;

        Assert.Equal(["Raider", "Tech"], tree.Categories.Select(category => category.Segment));
        Assert.Equal("Socket.LootCOntainer.Tech", tree.CategoryOf(SyntheticGameData.DrawerTag)!.Tag);
        Assert.Equal("Raider", tree.CategoryOf("socket.lootcontainer.raider")!.Segment);
        Assert.Null(tree.CategoryOf("Socket.LootContainer"));
        Assert.Null(tree.CategoryOf("Socket.LootContainer.TechX"));
        Assert.Null(tree.CategoryOf("Socket.LootContainer.Unknown"));
    }

    [Fact]
    public void Counts_by_tag_include_every_tag_above_a_containers_tags()
    {
        IReadOnlyDictionary<string, int> counts = EditorGameData.Load(_folder.Path, SyntheticGameData.SpawnDefaults).Maps[0].CountByTag();

        Assert.Equal(5, counts["Socket"]);
        Assert.Equal(5, counts["socket.lootcontainer"]);
        Assert.Equal(2, counts["Socket.LootContainer.Tech"]);
        Assert.Equal(2, counts[SyntheticGameData.DrawerTag]);
        Assert.Equal(2, counts["Socket.LootContainer.Raider.Box"]);
        Assert.Equal(1, counts["Socket.LootContainer.Unknown"]);
        Assert.False(counts.ContainsKey("Socket.Other"));
    }

    [Fact]
    public void A_missing_folder_or_table_is_refused()
    {
        Assert.Throws<EditorDataException>(() => EditorGameData.Load(Path.Combine(_folder.Path, "missing"), SyntheticGameData.SpawnDefaults));
        File.Delete(Path.Combine(_folder.Path, "maps", SyntheticGameData.Map, "placed-loot.json"));
        Assert.Throws<EditorDataException>(() => EditorGameData.Load(_folder.Path, SyntheticGameData.SpawnDefaults));
    }

    [Fact]
    public void A_malformed_table_is_refused()
    {
        _folder.Write("loot-container-types.json", """[{"tag": null}]""");

        EditorDataException error = Assert.Throws<EditorDataException>(() => EditorGameData.Load(_folder.Path, SyntheticGameData.SpawnDefaults));
        Assert.Contains("loot-container-types.json is malformed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Container_items_are_the_plain_supported_items_and_the_ferro_and_light_shield()
    {
        EditorGameData data = EditorGameData.Load(_folder.Path, SyntheticGameData.SpawnDefaults);

        Assert.Equal(
        [
            new ContainerItem("DA_Item_Battery", "Battery", 15),
            new ContainerItem("DA_Item_BattleRifle_BreachAction_01", "Ferro", 1),
            new ContainerItem("DA_Item_Armor_LightBody", null, 1),
            new ContainerItem("DA_Item_Wires", null, 10),
        ], data.ContainerItems);
    }

    [Fact]
    public void Item_search_matches_every_word_in_either_name_starts_first()
    {
        ContainerItem[] items =
        [
            new("DA_Item_Battery", "Battery", 15),
            new("DA_Item_BattleRifle_BreachAction_01", "Ferro", 1),
            new("DA_Item_Armor_LightBody", null, 1),
        ];

        Assert.Equal(["DA_Item_BattleRifle_BreachAction_01"], ContainerItemSearch.Find(items, "breachaction").Select(item => item.TechnicalName));
        Assert.Equal(["DA_Item_BattleRifle_BreachAction_01"], ContainerItemSearch.Find(items, "fer").Select(item => item.TechnicalName));
        Assert.Equal(["DA_Item_BattleRifle_BreachAction_01"], ContainerItemSearch.Find(items, "rifle 01").Select(item => item.TechnicalName));
        Assert.Equal(["DA_Item_Battery", "DA_Item_BattleRifle_BreachAction_01"], ContainerItemSearch.Find(items, "batt").Select(item => item.TechnicalName));
        Assert.Equal(["DA_Item_Armor_LightBody", "DA_Item_Battery", "DA_Item_BattleRifle_BreachAction_01"], ContainerItemSearch.Find(items, " ").Select(item => item.TechnicalName));
        Assert.Empty(ContainerItemSearch.Find(items, "zipline"));
    }
}
