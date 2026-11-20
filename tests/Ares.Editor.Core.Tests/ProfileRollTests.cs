using System.Text.Json;
using System.Text.Json.Nodes;
using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;
using Ares.Editor.Core.Matching;
using Ares.Editor.Core.Paths;

namespace Ares.Editor.Core.Tests;

/// <summary>What contents profiles change in the estimated contents of containers and loose item sockets.</summary>
public sealed class ProfileRollTests : IDisposable
{
    // The synthetic game data plus Gear, a rare material any container takes, and Seeds, which only residential
    // containers take and no table offers.
    private const string ExtraItems = """
        [
          {"usableIndex": 9, "technicalName": "DA_Item_Gear", "assetId": 20, "baseAssetId": 20, "category": 11, "maxStack": 10, "hasChildSlots": false,
           "dropTags": ["Item.Drop.Type.Salvage.Material.Rare"], "lootPlacements": [{"socketTag": "Socket.LootContainer", "excludeTags": []}], "lootAvoidGroups": []},
          {"usableIndex": 10, "technicalName": "DA_Item_Seeds", "assetId": 21, "baseAssetId": 21, "category": 11, "maxStack": 5, "hasChildSlots": false,
           "dropTags": ["Item.Drop.Type.Salvage.Seeds"], "lootPlacements": [{"socketTag": "Socket.LootContainer.Residential", "excludeTags": []}], "lootAvoidGroups": []}
        ]
        """;

    private const string Spawn = """
        {"chances": {"Socket.LootContainer": 1}, "areaMultipliers": {}, "groupCaps": false, "maxPerGroup": 2, "limiters": false, "limiterDefaults": {"shape": "Sphere", "radius": 1000, "unlimited": true, "limit": 0}, "spawnerClasses": [], "spawnerCount": 1,
         "looseChances": {"Socket.Loot": 1}}
        """;

    private const string ExtraGroup = "Extra stack: Item.Drop.Type.Salvage.Material";

    private readonly TemporaryFolder _folder = new();

    public ProfileRollTests()
    {
        SyntheticGameData.Write(_folder);
        JsonArray items = (JsonArray)JsonNode.Parse(File.ReadAllText(Path.Combine(_folder.Path, "usable-items.json")))!;
        foreach (JsonNode? item in (JsonArray)JsonNode.Parse(ExtraItems)!)
        {
            items.Add(item!.DeepClone());
        }

        _folder.Write("usable-items.json", items.ToJsonString());
        _folder.Write("content/" + LootSpawnDocument.FileName, Spawn);
        _folder.Write("content/" + LootRulesDocument.FileName, """{"rules": []}""");
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Without_profiles_the_odds_are_those_of_the_default_roll()
    {
        string[] none = Snapshots(null);
        ContentOdds drawer = Container(null, SyntheticGameData.SocketId);
        Assert.Equal(1.0, Assert.Single(Assert.Single(drawer.Groups, group => group.Group == "Common").Items).Chance);
        Assert.DoesNotContain(drawer.Groups, group => group.Group.StartsWith("Extra stack", StringComparison.Ordinal));

        Assert.Equal(none, Snapshots(string.Empty));
        Assert.Equal(none, Snapshots($$"""
            {"name": "Tech", "tag": "Socket.LootContainer.Tech"}, {"name": "Box", "class": "{{SyntheticGameData.Box}}"}, {"name": "Sofa", "tag": "Socket.Loot.Sofa"}
            """));
    }

    [Fact]
    public void Only_keeps_the_matching_items_whatever_the_tag_case()
    {
        ContentOdds odds = Container("""{"name": "Tech", "tag": "socket.lootcontainer.TECH", "only": [{"item": "DA_Item_Wires"}]}""", SyntheticGameData.SocketId);

        GroupOdds rare = Assert.Single(odds.Groups);
        Assert.Equal(("Rare", "DA_Item_Wires", 1.0), (rare.Group, Assert.Single(rare.Items).Item, rare.Chance));
        Assert.Equal([new OddsShare("1", 1)], odds.StackCounts);
    }

    [Fact]
    public void A_multiplier_of_0_removes_an_item_and_others_weight_the_pick_within_the_tier()
    {
        ContentOdds odds = Container("""
            {"name": "Tech", "tag": "Socket.LootContainer.Tech", "stacks": {"min": 1, "max": 1},
             "weights": [{"item": "DA_Item_Battery", "multiplier": 0}, {"item": "DA_Item_Gear", "multiplier": 3}]}
            """, SyntheticGameData.SocketId);

        Assert.Equal([new OddsShare("1", 1)], odds.StackCounts);
        GroupOdds rare = Assert.Single(odds.Groups);
        Assert.InRange(Assert.Single(rare.Items, item => item.Item == "DA_Item_Gear").Chance, 0.72, 0.78);
    }

    [Fact]
    public void An_addition_bypasses_the_placement_list_with_its_amount_and_tier_and_is_marked()
    {
        ContentOdds odds = Container("""
            {"name": "Tech", "tag": "Socket.LootContainer.Tech", "only": [{"dropTag": "Item.Drop.Type.Salvage.Seeds"}],
             "add": [{"item": "DA_Item_Seeds", "amount": {"min": 3, "max": 3}, "tier": "Rare"}]}
            """, SyntheticGameData.SocketId);

        GroupOdds rare = Assert.Single(odds.Groups);
        ItemOdds seeds = Assert.Single(rare.Items);
        Assert.Equal(("Rare", "DA_Item_Seeds", 1.0, 3, 3, true), (rare.Group, seeds.Item, seeds.Chance, seeds.MinAmount, seeds.MaxAmount, seeds.Added));
        Assert.Equal(["Profile Tech adds DA_Item_Seeds (DA_Item_Seeds), whose placement list does not accept Socket.LootContainer.Tech.Drawer."], odds.Warnings);
    }

    [Fact]
    public void First_stack_tier_null_stops_forcing_and_tier_multipliers_scale_the_tiers()
    {
        ContentOdds off = Container("""{"name": "Tech", "tag": "Socket.LootContainer.Tech", "firstStackTier": null}""", SyntheticGameData.SocketId);
        Assert.InRange(Battery(off), 0.72, 0.78);

        ContentOdds scaled = Container("""{"name": "Tech", "tag": "Socket.LootContainer.Tech", "firstStackTier": null, "tiers": {"Common": 3}}""",
            SyntheticGameData.SocketId);
        Assert.InRange(Battery(scaled), 0.91, 0.97);
    }

    [Fact]
    public void When_only_tiers_weighing_zero_remain_nothing_more_is_drawn()
    {
        const string None = """{"Common": 0, "Rare": 0, "Other": 0}""";
        ContentOdds drawer = Container($$"""{"name": "Tech", "tag": "Socket.LootContainer.Tech", "firstStackTier": null, "tiers": {{None}}}""",
            SyntheticGameData.SocketId);
        ContentOdds sofa = Loose($$"""{"name": "Sofa", "tag": "Socket.Loot.Sofa", "tiers": {{None}}}""");

        Assert.Empty(drawer.Groups);
        Assert.Empty(sofa.Groups);
    }

    [Fact]
    public void A_profile_extra_stack_gives_it_where_the_pool_has_none_with_merged_chance_and_entries()
    {
        ContentOdds given = Container("""{"name": "Tech", "tag": "Socket.LootContainer.Tech", "extraStack": {}}""", SyntheticGameData.SocketId);
        Assert.Equal(1.0, Assert.Single(given.Groups, group => group.Group == ExtraGroup).Chance);

        ContentOdds off = Container("""{"name": "Tech", "tag": "Socket.LootContainer.Tech", "extraStack": {"chance": 0}}""", SyntheticGameData.SocketId);
        Assert.DoesNotContain(off.Groups, group => group.Group.StartsWith("Extra stack", StringComparison.Ordinal));

        ContentOdds seeds = Container("""
            {"name": "Top", "tag": "Socket.LootContainer", "extraStack": {"entries": [{"dropTag": "Item.Drop.Type.Salvage.Seeds", "amount": {"min": 1, "max": 1}}]}},
            {"name": "Tech", "tag": "Socket.LootContainer.Tech", "extraStack": {"chance": 0.5}}
            """, SyntheticGameData.SocketId);
        GroupOdds extra = Assert.Single(seeds.Groups, group => group.Group == "Extra stack: Item.Drop.Type.Salvage.Seeds");
        Assert.InRange(extra.Chance, 0.47, 0.53);
    }

    [Fact]
    public void Type_pool_entries_are_filtered_and_additions_join_as_entries()
    {
        ContentOdds odds = Container($$$"""
            {"name": "Box", "class": "{{{SyntheticGameData.Box}}}", "stacks": {"min": 3, "max": 3},
             "weights": [{"dropTag": "Item.Drop.Type.Salvage.Material.Rare", "multiplier": 0}],
             "add": [{"item": "DA_Item_Seeds", "amount": {"min": 2, "max": 2}}]}
            """, SyntheticGameData.PlacedId);

        Assert.Equal([new OddsShare("1", 1)], odds.StackCounts);
        Assert.Equal("DA_Item_Battery", Assert.Single(Assert.Single(odds.Groups, group => group.Group == "Item.Drop.Type.Salvage.Material").Items).Item);
        ItemOdds seeds = Assert.Single(Assert.Single(odds.Groups, group => group.Group == "Added: DA_Item_Seeds").Items);
        Assert.InRange(seeds.Chance, 0.47, 0.53);
        Assert.Equal((2, true), (seeds.MinAmount, seeds.Added));
    }

    [Fact]
    public void A_loose_socket_profile_adds_and_filters_its_table_items()
    {
        ContentOdds odds = Loose("""
            {"name": "Sofa", "tag": "Socket.Loot.Sofa", "weights": [{"item": "DA_Item_Wires", "multiplier": 0}],
             "add": [{"item": "DA_Item_Battery", "amount": {"min": 2, "max": 2}}]}
            """);

        GroupOdds common = Assert.Single(odds.Groups);
        ItemOdds battery = Assert.Single(common.Items);
        Assert.Equal(("Table: Common", "DA_Item_Battery", 1.0, 2, true), (common.Group, battery.Item, battery.Chance, battery.MinAmount, battery.Added));
        Assert.Single(odds.Warnings);
    }

    [Fact]
    public void Profile_problems_show_with_the_odds()
    {
        ContentOdds odds = Container("""{"name": "Tech", "tag": "Socket.LootContainer.Tech", "tiers": {"Gold": 2}}""", SyntheticGameData.SocketId);

        Assert.Contains("Profile Tech: tiers names the unknown tier Gold; the server refuses to start.", odds.Problems);
    }

    private static double Battery(ContentOdds odds) => Assert.Single(odds.Groups, group => group.Group == "Common").Items.Single().Chance;

    // The odds of the drawer socket, the box and the sofa socket, as JSON.
    private string[] Snapshots(string? profiles) =>
    [
        JsonSerializer.Serialize(Container(profiles, SyntheticGameData.SocketId)),
        JsonSerializer.Serialize(Container(profiles, SyntheticGameData.PlacedId)),
        JsonSerializer.Serialize(Loose(profiles)),
    ];

    private ContentOdds Container(string? profiles, string id)
    {
        EditorWorkspace workspace = Open(profiles);
        LootContainer container = workspace.GameData.Maps[0].Containers.Single(entry => entry.Id == id);
        return ContentOdds.ForContainer(workspace.GameData, workspace.Spawn, new ContainerContentRolls(workspace.GameData.Roll, workspace.Contents), container,
            workspace.Match(container));
    }

    private ContentOdds Loose(string? profiles)
    {
        EditorWorkspace workspace = Open(profiles);
        LooseItemSocket sofa = workspace.GameData.Maps[0].LooseItemSockets.Single(socket => socket.Id == SyntheticGameData.LooseId);
        return ContentOdds.ForLoose(new LooseItemRolls(workspace.GameData.Roll, workspace.Contents), sofa, workspace.Match(sofa));
    }

    // A contents file with the table pool Tech (no extra stack of its own), the extra stack always drawing a material, the
    // box's type pool, and the given profiles (null: no profiles member).
    private EditorWorkspace Open(string? profiles)
    {
        string list = profiles is null ? string.Empty : $"\"profiles\": [{profiles}],";
        _folder.Write("content/" + LootContentsDocument.FileName, $$$"""
            {
              "excludedDropTags": [],
              "tiers": [
                {"name": "Common", "dropTag": "Item.Drop.Type.Salvage.Material.Common", "weight": 1},
                {"name": "Rare", "dropTag": "Item.Drop.Type.Salvage.Material.Rare", "weight": 1},
                {"name": "Other", "weight": 1}
              ],
              "firstStackTier": "Common",
              "targetStacks": [{"target": 2, "weight": 1}],
              "tablePools": [{"name": "Tech", "tag": "Socket.LootContainer.Tech", "tables": ["StaticValueLootTable_Test"], "extraStack": false}],
              "extraStack": {"chance": 1, "entries": [{"dropTag": "Item.Drop.Type.Salvage.Material", "amount": {"min": 1, "max": 1}}]},
              "typePools": [{"name": "BoxPool", "classes": ["{{{SyntheticGameData.Box}}}"], "stacks": {"min": 1, "max": 1},
                "entries": [{"dropTag": "Item.Drop.Type.Salvage.Material", "amount": {"min": 1, "max": 1}}]}],
              {{{list}}}
              "looseItems": {"tables": ["StaticValueLootTable_Test"], "amount": {"min": 1, "max": 1}, "pools": []}
            }
            """);
        return EditorWorkspace.Open(ServerLocation.FromDirectories(Path.Combine(_folder.Path, "content"), _folder.Path));
    }
}
