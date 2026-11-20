using System.Diagnostics;
using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;
using Ares.Editor.Core.Matching;
using Ares.Editor.Core.Paths;

namespace Ares.Editor.Core.Tests;

/// <summary>Group fills: which sockets the server fills together, their expected fill, and the chances as linked percentages.</summary>
public sealed class FillGroupTests : IDisposable
{
    private const string Rack1 = "Cell1/Rack_1/StaticMeshComponent0/";
    private const string Rack2 = "Cell1/Rack_2/StaticMeshComponent0/";

    private static readonly Lazy<EditorWorkspace> s_real = new(() =>
        EditorWorkspace.Open(ServerLocation.FromDirectories(RealData.ContentDirectory!, RealData.GameDataDirectory!)));

    private readonly TemporaryFolder _folder = new();
    private readonly EditorWorkspace _workspace;

    public FillGroupTests()
    {
        SyntheticGameData.Write(_folder);
        // Two racks: three drawer doors (one tag spelled in another case) and two drawer doors plus a box lid.
        _folder.Write($"maps/{SyntheticGameData.Map}/loot-sockets.json", $"""
            [
              {Socket("Rack_1", "Door_1", "Socket.LootContainer.Tech.Drawer", 0)},
              {Socket("Rack_1", "Door_2", "socket.lootcontainer.tech.drawer", 1)},
              {Socket("Rack_1", "Door_3", "Socket.LootContainer.Tech.Drawer", 2)},
              {Socket("Rack_2", "Door_1", "Socket.LootContainer.Tech.Drawer", 10)},
              {Socket("Rack_2", "Door_2", "Socket.LootContainer.Tech.Drawer", 11)},
              {Socket("Rack_2", "Lid", "Socket.LootContainer.Raider.Box", 12)},
              {Socket("Rack_3", "Door_1", "Socket.LootContainer.Unknown", 20)}
            ]
            """);
        _folder.Write("content/" + LootSpawnDocument.FileName, """{"chances":{},"areaMultipliers":{},"groupCaps":true,"maxPerGroup":2,"limiters":true,"limiterDefaults":{"shape":"Sphere","radius":1000,"unlimited":true,"limit":0},"spawnerClasses":[],"spawnerCount":1}""");
        _folder.Write("content/" + LootContentsDocument.FileName,
            """{"excludedDropTags":[],"tiers":[],"targetStacks":[{"target":2,"weight":1}],"tablePools":[],"extraStack":{"chance":0.1,"entries":[]},"typePools":[]}""");
        _folder.Write("content/" + LootRulesDocument.FileName, """{"rules":[]}""");
        _workspace = EditorWorkspace.Open(ServerLocation.FromDirectories(Path.Combine(_folder.Path, "content"), _folder.Path));
    }

    private MapLoot Map => _workspace.GameData.Maps[0];

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void A_group_is_one_props_sockets_of_a_tag_or_the_listed_containers()
    {
        LootRule racks = _workspace.Rules.AddRule("Racks");
        racks.Tags = ["Socket.LootContainer"];
        Set(racks.GroupFill, ("0", 32), ("1", 5), ("2", 3));
        LootRule chance = _workspace.Rules.AddRule("Chance");
        chance.Containers = [Rack1 + "Door_1"];
        chance.SpawnChance = 0.5;
        LootRule listed = _workspace.Rules.AddRule("Listed");
        listed.Containers = [Rack1 + "Door_3", Rack2 + "Door_1"];
        listed.Tags = ["Socket.LootContainer.Raider"];
        Set(listed.GroupFill, ("1", 1));

        LootFillGroups groups = Groups();

        Assert.Equal(
            [$"Racks {Rack1}SOCKET.LOOTCONTAINER.TECH.DRAWER: {Rack1}Door_2",
             $"Listed : {Rack1}Door_3, {Rack2}Door_1",
             $"Racks {Rack2}SOCKET.LOOTCONTAINER.TECH.DRAWER: {Rack2}Door_2",
             $"Listed {Rack2}SOCKET.LOOTCONTAINER.RAIDER.BOX: {Rack2}Lid"],
            groups.Groups.Select(group => $"{group.Rule.Name} {group.Key}: {string.Join(", ", group.Members.Select(index => Map.Containers[index].Id))}"));
        LootFillGroup both = groups.Groups[1];
        Assert.True(both.IsListed);
        Assert.Equal(SyntheticGameData.DrawerTag, both.Tag);
        Assert.Same(both, groups.GroupOf(Index(Rack2 + "Door_1")));
        Assert.Null(groups.GroupOf(Index(Rack1 + "Door_1")));
        Assert.Null(groups.GroupOf(Index("Cell1/Rack_3/StaticMeshComponent0/Door_1")));
        Assert.Null(groups.GroupOf(Index(SyntheticGameData.PlacedId)));
        Assert.Null(groups.GroupOf(-1));
        Assert.Null(groups.GroupOf(Map.Containers.Count));
        Assert.Equal([groups.Groups[1], groups.Groups[3]], groups.Of(listed));
        Assert.Null(LootFillGroups.Empty.GroupOf(0));
        Assert.Throws<ArgumentException>(() => LootFillGroups.Of(Map, []));
    }

    [Fact]
    public void A_group_fills_at_most_its_size_and_a_rule_sums_its_groups()
    {
        LootRule racks = _workspace.Rules.AddRule("Racks");
        racks.Tags = ["Socket.LootContainer.Tech"];
        Set(racks.GroupFill, ("0", 32), ("1", 5), ("2", 3), ("3", 0));

        LootFillGroups groups = Groups();
        LootFillSummary summary = groups.Summarize(racks);

        // 80 % none, 12.5 % one, 7.5 % two: 0.275 of a group of 3 or 2.
        Assert.Equal(0.275, groups.Groups[0].ExpectedFilled, 9);
        Assert.Equal((2, 5), (summary.Groups, summary.Containers));
        Assert.Equal(SyntheticGameData.DrawerTag, summary.Tag, ignoreCase: true);
        Assert.Equal([new KeyValuePair<int, int>(2, 1), new KeyValuePair<int, int>(3, 1)], summary.Sizes);
        Assert.Equal(0.55, summary.ExpectedFilled, 9);

        Set(racks.GroupFill, ("0", 1), ("5", 1));
        Assert.Equal(1.0, Groups().Groups[1].ExpectedFilled, 9);
        racks.Tags = ["Socket.LootContainer"];
        Assert.Null(Groups().Summarize(racks).Tag);
        Assert.Equal(0, Groups().Summarize(_workspace.Rules.AddRule("None")).Groups);
    }

    [Fact]
    public void A_groups_area_multiplies_each_sockets_odds_as_the_server_does()
    {
        LootRule racks = _workspace.Rules.AddRule("Racks");
        racks.Tags = ["Socket.LootContainer.Tech"];
        Set(racks.GroupFill, ("0", 6), ("1", 4), ("2", 9));
        GroupFillShares shares = GroupFillShares.Of(racks.GroupFill);

        // Weight × factor^filled (LootFill.InArea): 6, 12, 81 at ×3; a group of 1 counts 2 as 1: 6, 12, 27.
        Assert.Same(shares.Shares, shares.InArea(3, 1));
        Assert.Equal([600 / 99.0, 1200 / 99.0, 8100 / 99.0], shares.InArea(3, 3).Select(share => share.Percent), (left, right) => Math.Abs(left - right) < 1e-9);
        Assert.Equal([600 / 45.0, 1200 / 45.0, 2700 / 45.0], shares.InArea(1, 3).Select(share => share.Percent), (left, right) => Math.Abs(left - right) < 1e-9);
        Assert.Equal([100.0, 0, 0], shares.InArea(3, 0).Select(share => share.Percent));
        Assert.Equal([0, 0, 100.0], shares.InArea(3, 1e300).Select(share => share.Percent), (left, right) => Math.Abs(left - right) < 1e-9);

        // Rack 1 (3 doors) lies in a High area, rack 2 in a Medium one without an entry: it falls back to Unknown's.
        _workspace.Spawn.AreaMultipliers.Set("Unknown", 3);
        _workspace.Spawn.AreaMultipliers.Set("High", 5);
        LootFillGroups groups = Groups();
        Assert.Equal([(LootArea.High, 5.0), (LootArea.Medium, 3.0)], groups.Groups.Select(group => (group.Area, group.AreaMultiplier)));
        Assert.Equal((20 + 450) / 251.0, groups.Groups[0].ExpectedFilled, 9);
        Assert.Equal((12 + 162) / 99.0, groups.Groups[1].ExpectedFilled, 9);
        Assert.Equal(groups.Groups.Sum(group => group.ExpectedFilled), groups.Summarize(racks).ExpectedFilled, 9);
    }

    [Fact]
    public void Chances_are_the_weights_share_and_moving_one_rescales_the_others()
    {
        NumberMap fill = Fill(("0", 32), ("1", 5), ("2", 3));
        GroupFillShares shares = GroupFillShares.Of(fill);
        Assert.Equal([80, 12.5, 7.5], shares.Shares.Select(share => share.Percent));
        Assert.Same(shares, shares.With(0, 80.04));
        Assert.Same(shares, shares.With(7, 10));

        GroupFillShares half = shares.With(0, 50);
        Assert.Equal([50, 31.2, 18.8], half.Shares.Select(share => share.Percent));
        half.WriteTo(fill);
        Assert.Equal(Entries(("0", 50), ("1", 31.2), ("2", 18.8)), fill);

        GroupFillShares all = half.With(1, 120);
        Assert.Equal([0, 100, 0], all.Shares.Select(share => share.Percent));
        Assert.Equal([50, 0, 50], all.With(1, 0).Shares.Select(share => share.Percent));
        Assert.Equal([100], GroupFillShares.Of(Fill(("4", 2))).With(4, 30).Shares.Select(share => share.Percent));
    }

    [Fact]
    public void Linked_chances_always_total_100()
    {
        GroupFillShares shares = GroupFillShares.Of(Fill(("0", 6), ("1", 4), ("2", 9), ("3", 1)));
        Random random = new(7);
        for (int step = 0; step < 500; step++)
        {
            shares = shares.With(random.Next(4), random.NextDouble() * 110 - 5);
            Assert.Equal(1000, shares.Shares.Sum(share => (int)Math.Round(share.Percent * 10)));
            Assert.All(shares.Shares, share => Assert.InRange(share.Percent, 0, 100));
            Assert.All(shares.Shares, share => Assert.Equal(Math.Round(share.Percent, 1), share.Percent));
        }
    }

    [Fact]
    public void Adding_and_removing_numbers_keeps_a_fill_the_server_accepts()
    {
        NumberMap fill = Fill(("0", 32), ("1", 5), ("2", 3));
        GroupFillShares shares = GroupFillShares.Of(fill);

        GroupFillShares added = shares.Add();
        Assert.Equal((3, 0.0), (added.Shares[^1].Count, added.Shares[^1].Percent));
        Assert.Equal(4, added.NextCount);
        GroupFillShares removed = added.Without(1);
        Assert.Equal([0, 2, 3], removed.Shares.Select(share => share.Count));
        Assert.Equal(32.0 / 35 * 100, removed.Shares[0].Percent, 9);
        Assert.Same(removed, removed.Without(9));
        removed.WriteTo(fill);
        Assert.Equal(Entries(("0", 32), ("2", 3), ("3", 0)), fill);
        Assert.Empty(GroupFillShares.Problems(fill));

        GroupFillShares lonely = GroupFillShares.Of(Fill(("0", 0), ("1", 1), ("2", 0))).Without(1);
        Assert.Equal([50, 50], lonely.Shares.Select(share => share.Percent));
        Assert.Empty(GroupFillShares.Of(Fill(("0", 1))).Without(0).Shares);

        NumberMap empty = _workspace.Rules.AddRule("Empty").GroupFill;
        GroupFillShares first = GroupFillShares.Of(empty).Add();
        first.WriteTo(empty);
        Assert.Equal(Entries(("0", 100)), empty);
        first.Without(0).WriteTo(empty);
        Assert.False(empty.IsSet);
    }

    [Fact]
    public void Fills_the_server_refuses_are_reported_and_edits_repair_them()
    {
        Assert.Empty(GroupFillShares.Problems(_workspace.Rules.AddRule("Unset").GroupFill));
        Assert.Equal(["groupFill key x must be a number of sockets (0 or more)"], GroupFillShares.Problems(Fill(("x", 1), ("1", 1))));
        Assert.Equal(["groupFill 01 must appear once with a weight of 0 or more"], GroupFillShares.Problems(Fill(("1", 1), ("01", 2))));
        Assert.Equal(["groupFill 0 must appear once with a weight of 0 or more"], GroupFillShares.Problems(Fill(("0", -1), ("1", 1))));
        Assert.Equal(["groupFill needs a weight above 0"], GroupFillShares.Problems(Fill(("0", 0), ("1", 0))));
        Assert.False(GroupFillShares.TryCount("-1", out _));
        Assert.True(GroupFillShares.TryCount("01", out int count) && count == 1);

        NumberMap zero = Fill(("0", 0), ("1", 0));
        Assert.Equal([50, 50], GroupFillShares.Of(zero).Shares.Select(share => share.Percent));
        NumberMap broken = Fill(("x", 1), ("1", 1), ("01", 2), ("0", -1));
        GroupFillShares.Of(broken).With(1, 25).WriteTo(broken);
        Assert.Equal(Entries(("1", 25), ("0", 75)), broken);
        Assert.Empty(GroupFillShares.Problems(broken));
    }

    [Fact]
    public void An_untouched_fill_is_written_back_unchanged()
    {
        LootRule rule = _workspace.Rules.AddRule("Racks");
        Set(rule.GroupFill, ("0", 6), ("1", 4), ("2", 9));
        string before = _workspace.Rules.ToJson();

        GroupFillShares shares = GroupFillShares.Of(rule.GroupFill);
        shares.With(2, 47.4).WriteTo(rule.GroupFill);

        Assert.Equal(before, _workspace.Rules.ToJson());
        Assert.Equal(0.1, GroupFillShares.Step);
    }

    [Fact]
    public void The_dam_fills_whole_racks()
    {
        RealData.RequireGameData();
        RealData.ContentFile(LootRulesDocument.FileName);
        EditorWorkspace workspace = s_real.Value;
        MapLoot dam = Assert.Single(workspace.GameData.Maps, map => map.Name.StartsWith("TheDam", StringComparison.Ordinal));
        ContainerMatch[] matches = [.. dam.Containers.Select(workspace.Match)];

        Stopwatch watch = Stopwatch.StartNew();
        LootFillGroups groups = LootFillGroups.Of(dam, matches);
        watch.Stop();

        TestContext.Current.SendDiagnosticMessage($"{dam.Containers.Count} containers grouped in {watch.Elapsed.TotalMilliseconds:0.0} ms");
        foreach (string name in new[] { "Server racks", "Locker racks" })
        {
            LootRule rule = workspace.Rules.Rules.First(candidate => candidate.Name == name);
            LootFillSummary summary = groups.Summarize(rule);
            TestContext.Current.SendDiagnosticMessage(
                $"{name}: {summary.Groups} groups, {summary.Containers} sockets, sizes {string.Join(", ", summary.Sizes.Select(size => $"{size.Value}×{size.Key}"))}, " +
                $"tag {summary.Tag}, {summary.ExpectedFilled:0.00} filled per raid");
            Assert.True(summary.Groups > 1, name);
            Assert.NotNull(summary.Tag);
            Assert.All(groups.Of(rule), group => Assert.Single(group.Members.Select(index => dam.Containers[index].Prop).Distinct()));
        }
    }

    private static string Socket(string actor, string socket, string tag, double x) => $$"""
        {"cell": "Cell1", "actor": "{{actor}}", "component": "StaticMeshComponent0", "socket": "{{socket}}", "containerTag": "{{tag}}",
         "location": {"x": {{x * 100}}, "y": 0, "z": 0}, "rotation": {"pitch": 0, "yaw": 0, "roll": 0}, "scale": {"x": 1, "y": 1, "z": 1}, "mesh": 0,
         "overrideMaterials": [], "dataLayer": null}
        """;

    private static KeyValuePair<string, double>[] Entries(params (string Key, double Value)[] entries) =>
        [.. entries.Select(entry => new KeyValuePair<string, double>(entry.Key, entry.Value))];

    private static void Set(NumberMap map, params (string Key, double Value)[] entries)
    {
        foreach (string key in map.Keys)
        {
            map.Remove(key);
        }

        foreach ((string key, double value) in entries)
        {
            map.Set(key, value);
        }
    }

    private NumberMap Fill(params (string Key, double Value)[] entries)
    {
        NumberMap map = _workspace.Rules.AddRule("Fill").GroupFill;
        Set(map, entries);
        return map;
    }

    private LootFillGroups Groups() => LootFillGroups.Of(Map, [.. Map.Containers.Select(_workspace.Match)]);

    private int Index(string id) => Map.Containers.ToList().FindIndex(container => container.Id == id);
}
