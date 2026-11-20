using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;
using Ares.Editor.Core.Matching;
using Ares.Editor.Core.Paths;

namespace Ares.Editor.Core.Tests;

/// <summary>What a container or loose item socket holds, estimated with the server's roll.</summary>
public sealed class ContentOddsTests : IDisposable
{
    // Battery is a common material any tech container takes; Wires a rare one any container or loose socket (not on
    // nature) takes. The Box class has its own type pool.
    private const string Contents = $$$"""
        {
          "excludedDropTags": [],
          "tiers": [
            {"name": "Common", "dropTag": "Item.Drop.Type.Salvage.Material.Common", "weight": 1},
            {"name": "Rare", "dropTag": "Item.Drop.Type.Salvage.Material.Rare", "weight": 1},
            {"name": "Other", "weight": 1}
          ],
          "firstStackTier": "Common",
          "targetStacks": [{"target": 2, "weight": 1}],
          "tablePools": [{"name": "Tech", "tag": "Socket.LootContainer.Tech", "tables": ["StaticValueLootTable_Test"]}],
          "extraStack": {"chance": 0, "entries": []},
          "typePools": [{"name": "BoxPool", "classes": ["{{{SyntheticGameData.Box}}}"], "stacks": {"min": 1, "max": 1},
            "entries": [{"dropTag": "Item.Drop.Type.Salvage.Material", "amount": {"min": 1, "max": 1}}]}],
          "looseItems": {"tables": ["StaticValueLootTable_Test"], "amount": {"min": 1, "max": 1}, "pools": []}
        }
        """;

    private const string Spawn = """
        {"chances": {"Socket.LootContainer": 1}, "areaMultipliers": {}, "groupCaps": false, "maxPerGroup": 2, "limiters": false, "limiterDefaults": {"shape": "Sphere", "radius": 1000, "unlimited": true, "limit": 0}, "spawnerClasses": [], "spawnerCount": 1,
         "looseChances": {"Socket.Loot": 1}}
        """;

    private readonly TemporaryFolder _folder = new();
    private readonly EditorWorkspace _workspace;

    public ContentOddsTests()
    {
        SyntheticGameData.Write(_folder);
        _folder.Write("content/" + LootSpawnDocument.FileName, Spawn);
        _folder.Write("content/" + LootContentsDocument.FileName, Contents);
        _folder.Write("content/" + LootRulesDocument.FileName, $$"""
            {"rules": [{"name": "Wired", "containers": ["{{SyntheticGameData.VariantSocketId}}"],
              "loot": {"items": [{"item": "DA_Item_Battery", "weight": 1}, {"item": "DA_Item_Wires", "weight": 3}], "stacks": {"min": 1, "max": 1} } } ]}
            """);
        _workspace = EditorWorkspace.Open(ServerLocation.FromDirectories(Path.Combine(_folder.Path, "content"), _folder.Path));
    }

    public void Dispose() => _folder.Dispose();

    [Theory]
    [InlineData(new[] { "Item.Drop.Type.Salvage.Material.Rare" }, true)]
    [InlineData(new[] { "item.drop.type.salvage.material.rare.extra" }, true)]
    [InlineData(new[] { "Item.Drop.Type.Salvage.Material" }, false)]
    public void A_tag_query_matches_its_tags_and_the_tags_below(string[] tags, bool expected)
    {
        ValueTableEntry entry = _workspace.GameData.Roll.ValueTables.Single().Entries[1];
        Assert.Equal(expected, entry.Query!.Matches(tags));
    }

    [Fact]
    public void A_table_pool_draws_the_first_stack_tier_first_then_never_the_same_item_twice()
    {
        ContentOdds odds = ForContainer(SyntheticGameData.SocketId);

        Assert.Null(odds.Note);
        Assert.Equal([new OddsShare("Tech", 1)], odds.Pools);
        Assert.Equal([new OddsShare("2", 1)], odds.StackCounts);
        GroupOdds common = Assert.Single(odds.Groups, group => group.Group == "Common");
        ItemOdds battery = Assert.Single(common.Items);
        Assert.Equal(("DA_Item_Battery", "Battery", 1.0, 2, 4), (battery.Item, battery.Label, battery.Chance, battery.MinAmount, battery.MaxAmount));
        Assert.Equal(1, Assert.Single(odds.Groups, group => group.Group == "Rare").ExpectedStacks);
    }

    [Fact]
    public void A_type_pool_and_a_rule_list_draw_by_weight()
    {
        ContentOdds box = ForContainer(SyntheticGameData.PlacedId);
        GroupOdds entry = Assert.Single(box.Groups);
        Assert.Equal("Item.Drop.Type.Salvage.Material", entry.Group);
        Assert.All(entry.Items, item => Assert.InRange(item.Chance, 0.47, 0.53));

        ContentOdds ruled = ForContainer(SyntheticGameData.VariantSocketId);
        Assert.Equal("rule Wired", Assert.Single(ruled.Pools).Name);
        Assert.InRange(Assert.Single(ruled.Groups).Items.Single(item => item.Item == "DA_Item_Wires").Chance, 0.72, 0.78);
    }

    [Fact]
    public void A_loose_socket_gets_a_table_item_its_tag_accepts()
    {
        LooseItemRolls rolls = new(_workspace.GameData.Roll, _workspace.Contents);
        LooseItemSocket sofa = _workspace.GameData.Maps[0].LooseItemSockets.Single(socket => socket.Id == SyntheticGameData.LooseId);
        ContentOdds odds = ContentOdds.ForLoose(rolls, sofa, _workspace.Match(sofa));
        Assert.Equal(("Table: Rare", "DA_Item_Wires", 1.0), (Assert.Single(odds.Groups).Group, odds.Groups[0].Items.Single().Item, odds.Groups[0].Chance));

        LooseItemSocket pine = _workspace.GameData.Maps[0].LooseItemSockets.Single(socket => socket.Id == SyntheticGameData.NatureLooseId);
        Assert.NotNull(ContentOdds.ForLoose(rolls, pine, _workspace.Match(pine)).Note);
    }

    private ContentOdds ForContainer(string id)
    {
        LootContainer container = _workspace.GameData.Maps[0].Containers.Single(entry => entry.Id == id);
        return ContentOdds.ForContainer(_workspace.GameData, _workspace.Spawn, new ContainerContentRolls(_workspace.GameData.Roll, _workspace.Contents), container,
            _workspace.Match(container));
    }
}
