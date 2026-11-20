using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;
using Ares.Editor.Core.Matching;
using Ares.Editor.Core.Paths;

namespace Ares.Editor.Core.Tests;

/// <summary>Which chance and rules apply to a container, as ARES-Server decides it.</summary>
public sealed class MatchingTests : IDisposable
{
    private const string Spawn = """
        {
          "chances": {
            "Socket.LootContainer": 0.1,
            "socket.lootcontainer.tech": 0.4
          },
          "areaMultipliers": {
            "Unknown": 1.0,
            "High": 3
          },
          "groupCaps": true,
          "maxPerGroup": 2,
          "limiters": true,
          "limiterDefaults": {
            "shape": "Sphere",
            "radius": 1000,
            "unlimited": true,
            "limit": 0
          },
          "spawnerClasses": [],
          "spawnerCount": 1,
          "looseChances": {
            "Socket.Loot": 0.2,
            "socket.loot.nature": 0.01
          }
        }
        """;

    private readonly TemporaryFolder _folder = new();
    private readonly EditorWorkspace _workspace;

    public MatchingTests()
    {
        SyntheticGameData.Write(_folder);
        _folder.Write("content/" + LootSpawnDocument.FileName, Spawn);
        _folder.Write("content/" + LootContentsDocument.FileName,
            """{"excludedDropTags":[],"tiers":[],"targetStacks":[{"target":2,"weight":1}],"tablePools":[],"extraStack":{"chance":0.1,"entries":[]},"typePools":[]}""");
        _folder.Write("content/" + LootRulesDocument.FileName, """{"rules":[]}""");
        _workspace = EditorWorkspace.Open(ServerLocation.FromDirectories(Path.Combine(_folder.Path, "content"), _folder.Path));
    }

    private MapLoot Map => _workspace.GameData.Maps[0];

    public void Dispose() => _folder.Dispose();

    [Theory]
    [InlineData("Socket.LootContainer.Tech", "Socket.LootContainer", true)]
    [InlineData("socket.lootcontainer", "Socket.LootContainer", true)]
    [InlineData("Socket.LootContainerX", "Socket.LootContainer", false)]
    [InlineData("Socket", "Socket.LootContainer", false)]
    public void Tags_match_themselves_and_the_tags_below(string tag, string parent, bool expected)
    {
        Assert.Equal(expected, LootMatching.IsTagOrBelow(tag, parent));
    }

    [Fact]
    public void A_tag_uses_its_nearest_chance_and_the_area_factor()
    {
        Assert.Equal("socket.lootcontainer.tech", LootMatching.ChanceOf(_workspace.Spawn, SyntheticGameData.DrawerTag)!.Value.Key);
        Assert.Equal(0.1, LootMatching.ChanceOf(_workspace.Spawn, SyntheticGameData.BoxTag)!.Value.Value);
        Assert.Null(LootMatching.ChanceOf(_workspace.Spawn, "Other.Tag"));
        Assert.Equal(3, LootMatching.MultiplierOf(_workspace.Spawn, LootArea.High));
        Assert.Equal(1, LootMatching.MultiplierOf(_workspace.Spawn, LootArea.Medium));
        _workspace.Spawn.AreaMultipliers.Remove("Unknown");
        Assert.Equal(1, LootMatching.MultiplierOf(_workspace.Spawn, LootArea.Low));

        ContainerMatch match = _workspace.Match(Map.Find(SyntheticGameData.SocketId)!);
        Assert.Equal(SpawnDecision.Default, match.Decision);
        Assert.Equal(("socket.lootcontainer.tech", 0.4, 3.0), (match.ChanceTag, match.TagChance, match.AreaMultiplier));
        Assert.Equal(1, match.Chance);
        Assert.Empty(match.CoveringRules);
        Assert.Null(match.SpawnRule);
    }

    [Fact]
    public void The_last_covering_rule_that_sets_a_setting_wins()
    {
        LootRule all = _workspace.Rules.AddRule("All");
        all.SpawnChance = 0.5;
        LootRule tech = _workspace.Rules.AddRule("Tech");
        tech.Tags = ["Socket.LootContainer.Tech"];
        tech.SpawnChance = 0.9;
        tech.SetLoot().AddItem("DA_Item_A");
        LootRule other = _workspace.Rules.AddRule("Other");
        other.Tags = ["Socket.LootContainer.Raider"];
        other.RevealDelaySeconds = 2;

        ContainerMatch socket = _workspace.Match(Map.Find(SyntheticGameData.SocketId)!);
        Assert.Equal([all, tech], socket.CoveringRules);
        Assert.Equal((SpawnDecision.RuleChance, tech, 0.9), (socket.Decision, socket.SpawnRule, socket.Chance));
        Assert.Same(tech, socket.LootRule);
        Assert.Null(socket.RevealDelayRule);

        ContainerMatch placed = _workspace.Match(Map.Find(SyntheticGameData.PlacedId)!);
        Assert.Equal((SpawnDecision.RuleChance, all), (placed.Decision, placed.SpawnRule));
        Assert.Same(other, placed.RevealDelayRule);
        Assert.Null(placed.LootRule);
    }

    [Fact]
    public void A_group_fill_decides_sockets_but_not_placed_containers()
    {
        LootRule fill = _workspace.Rules.AddRule("Fill");
        fill.GroupFill.Set("1", 1);
        LootRule listed = _workspace.Rules.AddRule("Listed");
        listed.Containers = [SyntheticGameData.VariantSocketId];
        listed.SpawnChance = 0.2;

        ContainerMatch socket = _workspace.Match(Map.Find(SyntheticGameData.SocketId)!);
        Assert.Equal((SpawnDecision.GroupFill, fill), (socket.Decision, socket.SpawnRule));
        Assert.Null(socket.Chance);
        ContainerMatch byId = _workspace.Match(Map.Find(SyntheticGameData.VariantSocketId)!);
        Assert.Equal((SpawnDecision.RuleChance, listed, 0.2), (byId.Decision, byId.SpawnRule, byId.Chance));
        ContainerMatch placed = _workspace.Match(Map.Find(SyntheticGameData.PlacedId)!);
        Assert.Equal(SpawnDecision.Default, placed.Decision);
        Assert.True(LootMatching.Covers(fill, null, []));
        Assert.False(LootMatching.Covers(listed, null, [SyntheticGameData.DrawerTag]));
    }

    [Fact]
    public void A_spawner_chance_is_its_pick_chance_times_its_enabled_class_share()
    {
        LootContainer spawner = Map.Find(SyntheticGameData.SpawnerId)!;
        // Standard: one group that always picks its one spawner. Coldsnap: a choice keeping 1 of 2 branches (0.5), then
        // 0 or 1 of 2 spawners (0.25).
        Assert.Equal(1, SpawnerChances.RunChance(spawner, "Standard"), 6);
        Assert.Equal(0.125, SpawnerChances.RunChance(spawner, "Coldsnap"), 6);
        Assert.Equal(0, SpawnerChances.RunChance(spawner, "ToxicSwamp"), 6);
        Assert.Equal(["Standard", "Coldsnap", "NightRaid"], SpawnerChances.Rounds(spawner));

        // The box weighs 1 of 3; the unlisted class places nothing even when enabled.
        _workspace.Spawn.SpawnerClasses = [SyntheticGameData.Box, SyntheticGameData.Unlisted];
        Assert.Equal(1.0 / 3, _workspace.Match(spawner).Chance!.Value, 6);
        Assert.Equal(0.125 / 3, SpawnerChances.Chance(spawner, _workspace.Spawn, "Coldsnap"), 6);
    }

    [Fact]
    public void Spawners_run_by_their_groups_and_untyped_containers_never_spawn()
    {
        ContainerMatch spawner = _workspace.Match(Map.Find(SyntheticGameData.SpawnerId)!);
        Assert.Equal((SpawnDecision.SpawnerGroups, 0.0), (spawner.Decision, spawner.Chance));
        _workspace.Rules.AddRule("Boxes").SpawnChance = 0.3;
        Assert.Equal(0.3, _workspace.Match(Map.Find(SyntheticGameData.SpawnerId)!).Chance);

        Assert.Equal(SpawnDecision.Never, _workspace.Match(Map.Find(SyntheticGameData.UntypedSocketId)!).Decision);
        ContainerMatch unlisted = _workspace.Match(Map.Find(SyntheticGameData.UnlistedPlacedId)!);
        Assert.Equal((SpawnDecision.Never, 0.0), (unlisted.Decision, unlisted.Chance));
    }

    [Fact]
    public void Highlights_list_the_containers_of_a_tag_or_a_rule_in_map_order()
    {
        int[] all = [.. Enumerable.Range(0, Map.Containers.Count)];
        int[] boxes = LootMatching.IndicesWithTag(Map, "socket.lootcontainer.raider");
        Assert.Equal([SyntheticGameData.PlacedId, SyntheticGameData.SpawnerId], boxes.Select(index => Map.Containers[index].Id));
        Assert.Empty(LootMatching.IndicesWithTag(Map, "Socket.LootContainer.Rai"));

        LootRule rule = _workspace.Rules.AddRule("Listed");
        Assert.Equal(all, LootMatching.IndicesCoveredBy(Map, rule));
        rule.Tags = ["Socket.LootContainer.Raider"];
        rule.Containers = [SyntheticGameData.VariantSocketId];
        Assert.Equal([SyntheticGameData.VariantSocketId, SyntheticGameData.PlacedId, SyntheticGameData.SpawnerId],
            LootMatching.IndicesCoveredBy(Map, rule).Select(index => Map.Containers[index].Id));
    }

    [Fact]
    public void A_loose_item_socket_rolls_its_loose_chance_until_a_covering_rule_sets_one()
    {
        LooseItemSocket sofa = Map.FindLooseItemSocket(SyntheticGameData.LooseId)!;
        LooseItemSocket pine = Map.FindLooseItemSocket(SyntheticGameData.NatureLooseId)!;

        LooseItemMatch sofaDefault = _workspace.Match(sofa);
        Assert.Equal(("Socket.Loot", 0.2, 3.0), (sofaDefault.ChanceTag, sofaDefault.TagChance, sofaDefault.AreaMultiplier));
        Assert.Equal(0.6, sofaDefault.Chance, 9);
        Assert.Empty(sofaDefault.CoveringRules);
        Assert.Null(sofaDefault.SpawnRule);
        Assert.Null(sofaDefault.LootRule);
        Assert.Equal(("socket.loot.nature", 0.01), (_workspace.Match(pine).ChanceTag, _workspace.Match(pine).Chance));

        LootRule containers = _workspace.Rules.AddRule("Containers");
        containers.Tags = ["Socket.LootContainer"];
        containers.SpawnChance = 1;
        LootRule loose = _workspace.Rules.AddRule("Loose");
        loose.Tags = ["socket.loot"];
        loose.SpawnChance = 0.5;
        loose.SetLoot().AddItem("DA_Item_A");
        LootRule fill = _workspace.Rules.AddRule("Fill");
        fill.Containers = [SyntheticGameData.LooseId];
        fill.GroupFill.Set("1", 1);
        fill.RevealDelaySeconds = 3;
        LootRule listed = _workspace.Rules.AddRule("Listed");
        listed.Containers = [SyntheticGameData.NatureLooseId];
        listed.SpawnChance = 0.25;

        LooseItemMatch sofaRuled = _workspace.Match(sofa);
        Assert.Equal([loose, fill], sofaRuled.CoveringRules);
        Assert.Equal((loose, 0.5), (sofaRuled.SpawnRule, sofaRuled.Chance));
        Assert.Null(sofaRuled.ChanceTag);
        Assert.Same(loose, sofaRuled.LootRule);
        LooseItemMatch pineRuled = _workspace.Match(pine);
        Assert.Equal([loose, listed], pineRuled.CoveringRules);
        Assert.Equal((listed, 0.25), (pineRuled.SpawnRule, pineRuled.Chance));
        Assert.Same(loose, pineRuled.LootRule);

        Assert.Equal([0, 1], LootMatching.LooseIndicesCoveredBy(Map, loose));
        Assert.Equal([1], LootMatching.LooseIndicesCoveredBy(Map, listed));
        Assert.Empty(LootMatching.LooseIndicesCoveredBy(Map, containers));
        Assert.Equal([1], LootMatching.LooseIndicesWithTag(Map, LooseItemSocket.NatureTag));
        Assert.Empty(LootMatching.LooseIndicesWithTag(Map, "Socket.LootContainer"));
    }

    [Fact]
    public void Saving_the_workspace_writes_the_edited_files()
    {
        _workspace.Rules.AddRule("Saved");
        _workspace.Contents.FirstStackTier = "Common";
        _workspace.SaveAll();

        Assert.Contains("\"name\": \"Saved\"", File.ReadAllText(_workspace.Rules.FilePath), StringComparison.Ordinal);
        Assert.Contains("\"firstStackTier\": \"Common\"", File.ReadAllText(_workspace.Contents.FilePath), StringComparison.Ordinal);
        Assert.Equal(Spawn + "\n", File.ReadAllText(_workspace.Spawn.FilePath));
    }
}
