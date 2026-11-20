using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Tests;

/// <summary>Edits of the content documents change only what they set and keep the server's layout.</summary>
public sealed class ContentEditTests : IDisposable
{
    private const string Rules = """
        {
          "rules": [
            {
              "name": "Racks",
              "tags": [
                "Socket.LootContainer.Tech"
              ],
              "groupFill": {
                "0": 6,
                "2": 9.0
              },
              "note": "kept"
            }
          ],
          "comment": {
            "nested": [
              1,
              2.50
            ]
          }
        }

        """;

    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Unknown_members_and_number_text_survive_a_save()
    {
        LootRulesDocument document = LootRulesDocument.Load(_folder.Write(LootRulesDocument.FileName, Rules));

        Assert.Equal(Rules, document.ToJson());
    }

    [Fact]
    public void A_new_member_goes_where_the_server_files_put_it()
    {
        LootRulesDocument document = LootRulesDocument.Load(_folder.Write(LootRulesDocument.FileName, Rules));
        LootRule rule = document.Rules[0];

        rule.SpawnChance = 0.25;
        rule.Containers = ["/Actor_1"];

        string json = document.ToJson();
        Assert.True(json.IndexOf("\"tags\"", StringComparison.Ordinal) < json.IndexOf("\"containers\"", StringComparison.Ordinal));
        Assert.True(json.IndexOf("\"containers\"", StringComparison.Ordinal) < json.IndexOf("\"spawnChance\": 0.25", StringComparison.Ordinal));
        Assert.True(json.IndexOf("\"spawnChance\"", StringComparison.Ordinal) < json.IndexOf("\"groupFill\"", StringComparison.Ordinal));
        Assert.True(json.IndexOf("\"groupFill\"", StringComparison.Ordinal) < json.IndexOf("\"note\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Changed_numbers_keep_their_style_and_unchanged_ones_their_text()
    {
        LootRulesDocument document = LootRulesDocument.Load(_folder.Write(LootRulesDocument.FileName, Rules));
        NumberMap fill = document.Rules[0].GroupFill;

        fill.Set("0", 6);
        fill.Set("2", 4);
        fill.Set("5", 0.5);

        string json = document.ToJson();
        Assert.Contains("\"0\": 6,", json, StringComparison.Ordinal);
        Assert.Contains("\"2\": 4.0,", json, StringComparison.Ordinal);
        Assert.Contains("\"5\": 0.5", json, StringComparison.Ordinal);
        Assert.Equal(3, fill.Count);
        Assert.Equal(4, fill.Get("2"));
        Assert.Null(fill.Get("9"));
    }

    [Fact]
    public void Emptying_an_optional_member_removes_it()
    {
        LootRulesDocument document = LootRulesDocument.Load(_folder.Write(LootRulesDocument.FileName, Rules));
        LootRule rule = document.Rules[0];

        Assert.True(rule.GroupFill.Remove("0"));
        Assert.True(rule.GroupFill.Remove("2"));
        Assert.False(rule.GroupFill.Remove("2"));
        rule.Tags = [];
        rule.Name = string.Empty;

        Assert.False(rule.GroupFill.IsSet);
        Assert.Empty(rule.Tags);
        Assert.Null(rule.Name);
        Assert.DoesNotContain("groupFill", document.ToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain("\"tags\"", document.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void Rules_with_contents_can_be_added_moved_and_removed()
    {
        string path = _folder.Write(LootRulesDocument.FileName, Rules);
        LootRulesDocument document = LootRulesDocument.Load(path);

        LootRule added = document.AddRule("Medical");
        LootList loot = added.SetLoot();
        Assert.Same(loot, added.SetLoot());
        LootItem item = loot.AddItem("DA_Item_Bandage");
        item.Weight = 2;
        item.SetAmount(1, 3).Max = 4;
        item.Item = "DA_Item_Bandage_Large";
        Assert.Equal(2, loot.SetStacks(1, 2).Max);
        added.RevealDelaySeconds = 1.5;
        document.Rules.Move(1, 0);
        document.Save();

        LootRulesDocument reread = LootRulesDocument.Load(path);
        LootRule first = reread.Rules[0];
        Assert.Equal("Medical", first.Name);
        Assert.Equal(1.5, first.RevealDelaySeconds);
        LootItem stored = Assert.Single(first.Loot!.Items);
        Assert.Equal("DA_Item_Bandage_Large", stored.Item);
        Assert.Equal(2, stored.Weight);
        Assert.Equal((1, 4), (stored.Amount!.Min, stored.Amount.Max));
        Assert.Equal((1, 2), (first.Loot.Stacks!.Min, first.Loot.Stacks.Max));
        Assert.Equal(0, reread.Rules.IndexOf(first));

        stored.RemoveAmount();
        first.Loot.RemoveStacks();
        Assert.Null(stored.Amount);
        Assert.Null(first.Loot.Stacks);
        first.RemoveLoot();
        Assert.Null(first.Loot);
        Assert.True(reread.Rules.Remove(first));
        Assert.False(reread.Rules.Remove(first));
        reread.Rules.RemoveAt(0);
        Assert.Empty(reread.Rules);
        Assert.Equal("{\n  \"rules\": [],\n  \"comment\": {\n    \"nested\": [\n      1,\n      2.50\n    ]\n  }\n}\n", reread.ToJson());
    }

    [Fact]
    public void Spawn_defaults_the_server_refuses_are_problems()
    {
        LootSpawnDocument spawn = LootSpawnDocument.Load(_folder.Write(LootSpawnDocument.FileName,
            """{"chances":{},"areaMultipliers":{},"groupCaps":true,"maxPerGroup":2,"limiters":true,"limiterDefaults":{"shape":"Sphere","radius":1000,"unlimited":true,"limit":0},"spawnerClasses":[],"spawnerCount":1}"""));
        Assert.Empty(spawn.Problems);

        spawn.MaxPerGroup = -1;
        spawn.SpawnerCount = 0;
        spawn.LimiterDefaults.Shape = "Cone";
        spawn.LimiterDefaults.Radius = -5;
        Assert.Equal(
            [
                "maxPerGroup must be 0 or more",
                "spawnerCount must be 1 or more",
                "limiterDefaults: Cone is not a limiter shape (None, Sphere, Box, Volumes)",
                "limiterDefaults: radius and limit must be 0 or more",
            ],
            spawn.Problems);
    }

    [Fact]
    public void Spawn_and_contents_settings_can_be_edited()
    {
        LootSpawnDocument spawn = LootSpawnDocument.Load(_folder.Write(LootSpawnDocument.FileName,
            """{"chances":{"Socket.A":0.5},"areaMultipliers":{"Unknown":1.0},"groupCaps":true,"maxPerGroup":2,"limiters":false,"limiterDefaults":{"shape":"Sphere","radius":1000,"unlimited":true,"limit":0},"spawnerClasses":[],"spawnerCount":1}"""));
        spawn.GroupCaps = false;
        spawn.Limiters = true;
        spawn.SpawnerClasses = ["/Game/BP_Box.BP_Box_C"];
        spawn.AreaMultipliers.Set("High", 3);
        spawn.MaxPerGroup = 3;
        spawn.LimiterDefaults.Shape = "Box";
        spawn.LimiterDefaults.Radius = 500;
        spawn.SpawnerCount = 2;
        Assert.Equal(["Unknown", "High"], spawn.AreaMultipliers.Keys);
        Assert.Equal(["/Game/BP_Box.BP_Box_C"], spawn.SpawnerClasses);
        Assert.False(spawn.GroupCaps);
        Assert.True(spawn.Limiters);
        Assert.Equal(new LootSpawnDefaults(3, LootLimiterShape.Box, 500, true, 0, 2), spawn.Defaults);
        Assert.Contains("\"maxPerGroup\": 3,\n  \"limiters\": true,\n  \"limiterDefaults\": {\n    \"shape\": \"Box\",\n    \"radius\": 500,", spawn.ToJson(), StringComparison.Ordinal);

        LootContentsDocument contents = LootContentsDocument.Load(_folder.Write(LootContentsDocument.FileName,
            """{"excludedDropTags":[],"tiers":[],"targetStacks":[{"target":2,"weight":1}],"tablePools":[],"extraStack":{"chance":0.1,"entries":[]},"typePools":[]}"""));
        contents.ExcludedDropTags = [];
        contents.FirstStackTier = "Common";
        LootTier tier = contents.AddTier("Common", 3);
        tier.Name = "Rare";
        tier.DropTag = "Item.Drop.Rare";
        tier.Weight = 2;
        tier.AreaMultipliers.Set("High", 2);
        LootTablePool table = contents.AddTablePool("Tech", "Socket.A");
        table.Name = "Tech2";
        table.Tag = "Socket.B";
        table.Tables = ["StaticValueLootTable_A"];
        Assert.True(table.ExtraStack);
        table.ExtraStack = false;
        LootCount stacks = table.SetStacks(1, 2);
        Assert.Same(stacks, table.SetStacks(2, 3));
        Assert.Equal(3, table.Stacks!.Max);
        table.RemoveStacks();
        contents.ExtraStack.Chance = 0.2;
        contents.TargetStacks[0].Weight = 4;
        LootTypeEntry extra = contents.ExtraStack.AddEntry("Item.Drop.Ammo", 5, 10);
        extra.DropTag = "Item.Drop.Ammo.Light";
        extra.Weight = 2;
        extra.ExcludedDropTags = ["Item.Drop.Ammo.Heavy"];
        LootTypePool pool = contents.AddTypePool("Raider", 1, 3);
        pool.Name = "RaiderAmmo";
        pool.Classes = ["/Game/BP_Box.BP_Box_C"];
        pool.Tags = ["Socket.A"];
        pool.AddEntry("Item.Drop.Ammo", 10, 30).Amount.Min = 15;

        Assert.Equal("Rare", tier.Name);
        Assert.Equal(("Item.Drop.Rare", 2.0, 2.0), (tier.DropTag, tier.Weight, tier.AreaMultipliers.Get("High")));
        Assert.Equal(("Tech2", "Socket.B", false), (table.Name, table.Tag, table.ExtraStack));
        Assert.Null(table.Stacks);
        Assert.Equal(["StaticValueLootTable_A"], table.Tables);
        Assert.Equal(0.2, contents.ExtraStack.Chance);
        Assert.Equal((2, 4.0), (contents.TargetStacks[0].Target, contents.TargetStacks[0].Weight));
        Assert.Equal(("Item.Drop.Ammo.Light", 2.0, 5), (extra.DropTag, extra.Weight, extra.Amount.Min));
        Assert.Equal(["Item.Drop.Ammo.Heavy"], extra.ExcludedDropTags);
        Assert.Equal(("RaiderAmmo", 1, 3), (pool.Name, pool.Stacks.Min, pool.Stacks.Max));
        Assert.Equal(["/Game/BP_Box.BP_Box_C"], pool.Classes);
        Assert.Equal(["Socket.A"], pool.Tags);
        Assert.Equal(15, Assert.Single(pool.Entries).Amount.Min);
        Assert.Contains("\"name\": \"Rare\",\n      \"dropTag\": \"Item.Drop.Rare\",\n      \"weight\": 2,", contents.ToJson(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"rules":[{"tags":"Socket.A"}]}""", "$.rules[0].tags: expected a list of strings")]
    [InlineData("""{"rules":[{"loot":{"items":[{"weight":1}]}}]}""", "$.rules[0].loot.items[0]: item is required")]
    [InlineData("""{"rules":[{"loot":{"items":[{"item":"A","amount":{"min":1,"max":2.5}}]}}]}""", "$.rules[0].loot.items[0].amount.max: expected a whole number")]
    [InlineData("[]", "the file is not a JSON object")]
    [InlineData("""{"rules":[1]}""", "$.rules: expected a list of objects")]
    public void Malformed_files_are_refused_with_the_member(string json, string problem)
    {
        string path = _folder.Write(LootRulesDocument.FileName, json);

        EditorDataException error = Assert.Throws<EditorDataException>(() => LootRulesDocument.Load(path));
        Assert.Contains(problem, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_file_is_refused()
    {
        Assert.Throws<EditorDataException>(() => LootSpawnDocument.Load(Path.Combine(_folder.Path, LootSpawnDocument.FileName)));
    }
}
