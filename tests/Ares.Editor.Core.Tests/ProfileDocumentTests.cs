using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;
using Ares.Editor.Core.Matching;

namespace Ares.Editor.Core.Tests;

/// <summary>Contents profiles in <c>loot-contents.json</c>: reading and writing them, merging a chain, and what the server refuses.</summary>
public sealed class ProfileDocumentTests : IDisposable
{
    private const string WithProfiles = """
        {
          "excludedDropTags": [],
          "tiers": [
            {
              "name": "Common",
              "weight": 1
            }
          ],
          "targetStacks": [
            {
              "target": 2,
              "weight": 1
            }
          ],
          "tablePools": [],
          "extraStack": {
            "chance": 0,
            "entries": []
          },
          "typePools": [],
          "profiles": [
            {
              "note": "kept",
              "tag": "Socket.LootContainer.Tech",
              "name": "Tech",
              "weights": [
                {
                  "multiplier": 2.0,
                  "item": "DA_Item_Wires"
                }
              ],
              "firstStackTier": null,
              "extraStack": {
                "chance": 0.30
              }
            }
          ],
          "looseItems": {
            "tables": [],
            "amount": {
              "min": 1,
              "max": 1
            },
            "pools": []
          }
        }

        """;

    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Profiles_keep_their_layout_unknown_members_and_number_text()
    {
        LootContentsDocument document = LootContentsDocument.Load(_folder.Write(LootContentsDocument.FileName, WithProfiles));

        LootProfile profile = Assert.Single(document.Profiles);
        Assert.Equal(("Tech", "Socket.LootContainer.Tech", 2.0), (profile.Name, profile.Tag, Assert.Single(profile.Weights!).Multiplier));
        Assert.True(profile.SetsFirstStackTier);
        Assert.Null(profile.FirstStackTier);
        Assert.Equal(0.3, profile.ExtraStack!.Chance);
        Assert.Null(profile.ExtraStack.Entries);
        Assert.Equal(WithProfiles, document.ToJson());
    }

    [Fact]
    public void A_new_profile_list_goes_after_the_type_pools_and_stays_when_emptied()
    {
        string without = WithProfiles[..WithProfiles.IndexOf("  \"profiles\"", StringComparison.Ordinal)]
            + WithProfiles[(WithProfiles.IndexOf("  \"looseItems\"", StringComparison.Ordinal))..];
        LootContentsDocument document = LootContentsDocument.Load(_folder.Write(LootContentsDocument.FileName, without));
        Assert.Empty(document.Profiles);

        LootProfile profile = document.AddProfile("Boxes", null, SyntheticGameData.Box);
        profile.AddWeight("Item.Drop.Type.Salvage", null, 0);
        profile.AddAddition(null, "DA_Item_Battery", 1, 3).Tier = "Common";
        profile.SetFirstStackTier("Common");
        profile.SetStacks(1, 2);

        string json = document.ToJson();
        Assert.True(json.IndexOf("\"typePools\"", StringComparison.Ordinal) < json.IndexOf("\"profiles\"", StringComparison.Ordinal));
        Assert.True(json.IndexOf("\"profiles\"", StringComparison.Ordinal) < json.IndexOf("\"looseItems\"", StringComparison.Ordinal));
        Assert.Contains("\"class\": \"/Game/BP_Box.BP_Box_C\",\n      \"weights\"", json, StringComparison.Ordinal);
        Assert.Contains("\"amount\": {\n            \"min\": 1,\n            \"max\": 3\n          },\n          \"tier\": \"Common\"", json, StringComparison.Ordinal);
        Assert.Contains("\"firstStackTier\": \"Common\",\n      \"stacks\"", json, StringComparison.Ordinal);

        profile.SetFirstStackTier(null);
        Assert.Contains("\"firstStackTier\": null", document.ToJson(), StringComparison.Ordinal);
        profile.InheritFirstStackTier();
        Assert.DoesNotContain("firstStackTier", document.ToJson(), StringComparison.Ordinal);

        Assert.True(document.RemoveProfile(profile));
        Assert.Equal(without.Replace("  \"looseItems\"", "  \"profiles\": [],\n  \"looseItems\"", StringComparison.Ordinal), document.ToJson());
    }

    [Fact]
    public void Removing_the_last_entry_of_a_list_makes_it_inherit()
    {
        LootContentsDocument document = LootContentsDocument.Load(_folder.Write(LootContentsDocument.FileName, WithProfiles));
        LootProfile profile = document.Profiles[0];

        LootMatch only = profile.AddOnly("Item.Drop.Type.Salvage", null);
        profile.RemoveOnly(only);
        profile.RemoveWeight(profile.Weights![0]);

        Assert.Null(profile.Only);
        Assert.Null(profile.Weights);
        Assert.DoesNotContain("\"only\"", document.ToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain("\"weights\"", document.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_chain_runs_from_the_top_tag_down_ignoring_case_then_the_class()
    {
        LootContentsDocument document = Contents("""
            {"name": "Class", "class": "/Game/BP_Bag.BP_Bag_C"},
            {"name": "Bag", "tag": "Socket.LootContainer.Medical.Bag"},
            {"name": "Top", "tag": "socket.lootcontainer"},
            {"name": "Medical", "tag": "Socket.LootContainer.Medical"},
            {"name": "Other class", "class": "/Game/BP_BAG.BP_Bag_C"}
            """);

        // The game spells one tag Socket.LootCOntainer.Medical...: it still resolves through the Medical profile.
        Assert.Equal(["Top", "Medical", "Bag", "Class"],
            ContentProfiles.ChainOf(document, "Socket.LootCOntainer.Medical.Bag", "/Game/BP_Bag.BP_Bag_C").Select(profile => profile.Name));
        Assert.Equal(["Top", "Medical"], ContentProfiles.ChainOf(document, "Socket.LootContainer.Medical.Kit", null).Select(profile => profile.Name));
        Assert.Equal(["Class"], ContentProfiles.ChainOf(document, null, "/Game/BP_Bag.BP_Bag_C").Select(profile => profile.Name));
        Assert.Empty(ContentProfiles.ChainOf(document, "Socket.LootContainerX", null));
    }

    [Fact]
    public void Nearer_profiles_win_per_value_tier_and_match_key()
    {
        LootContentsDocument document = Contents("""
            {"name": "Top", "tag": "Socket.LootContainer", "only": [{"dropTag": "Item.Drop.Type.Salvage"}], "stacks": {"min": 1, "max": 1},
             "tiers": {"Common": 0.5, "Rare": 2}, "firstStackTier": "Rare", "extraStack": {"chance": 0.2, "entries": [{"dropTag": "Item.Drop", "amount": {"min": 1, "max": 1}}]},
             "weights": [{"dropTag": "item.drop.type.salvage.material", "multiplier": 3}, {"item": "DA_Item_Wires", "multiplier": 0.5}],
             "add": [{"item": "DA_Item_Battery", "amount": {"min": 1, "max": 1}}]},
            {"name": "Tech", "tag": "Socket.LootContainer.Tech", "tiers": {"Rare": 0}, "firstStackTier": null, "extraStack": {"chance": 0.9},
             "weights": [{"dropTag": "Item.Drop.Type.Salvage.Material", "multiplier": 1}],
             "add": [{"item": "DA_Item_Battery", "amount": {"min": 2, "max": 2}}, {"dropTag": "Item.Drop.Type.Salvage", "amount": {"min": 1, "max": 1}}]}
            """);
        MergedProfile merged = ContentProfiles.Resolve(document, "Socket.LootContainer.Tech.Drawer", null);

        Assert.Equal("Top", merged.Only!.Profile.Name);
        Assert.Equal(new CountRange(1, 1), merged.Stacks!.Value);
        Assert.Equal((0.5, 0.0, 1.0), (merged.TierMultiplier("Common"), merged.TierMultiplier("Rare"), merged.TierMultiplier("Other")));
        Assert.Equal(("Tech", null), (merged.FirstStackTier!.Profile.Name, merged.FirstStackTier.Value));
        Assert.True(merged.SetsExtraStack);
        Assert.Equal((0.9, "Top"), (merged.ExtraChance!.Value, merged.ExtraEntries!.Profile.Name));

        // The Tech weight replaces Top's on the same drop tag (back to 1); the Wires weight still applies.
        RollItem wires = Item("DA_Item_Wires", "Item.Drop.Type.Salvage.Material.Rare");
        Assert.Equal(0.5, merged.Multiplier(wires));
        Assert.Equal(1, merged.Multiplier(Item("DA_Item_Battery", "Item.Drop.Type.Salvage.Material.Common")));
        Assert.False(merged.Keeps(Item("DA_Item_Gun", "Item.Drop.Type.Weapon")));

        // Additions: nearest first, Tech's Battery replacing Top's.
        Assert.Equal([("DA_Item_Battery", 2, "Tech"), ("Item.Drop.Type.Salvage", 1, "Tech")],
            merged.Additions.Select(entry => (entry.Value.Text, entry.Value.Amount.Min, entry.Profile.Name)));
    }

    [Fact]
    public void Drop_tag_additions_leave_out_excluded_items_and_item_additions_do_not()
    {
        RollItem banned = Item("DA_Item_Banned", "Item.Drop.Type.Salvage.Banned");
        RollItem kept = Item("DA_Item_Kept", "Item.Drop.Type.Salvage.Material");
        RollItem[] items = [banned, kept];
        LootContentsDocument document = Contents("""
            {"name": "Top", "tag": "Socket.LootContainer", "add": [{"dropTag": "Item.Drop.Type.Salvage", "amount": {"min": 1, "max": 1}},
              {"item": "DA_Item_Banned", "amount": {"min": 1, "max": 1}}]}
            """);
        IReadOnlyList<ProfileValue<LootAddition>> additions = ContentProfiles.Resolve(document, "Socket.LootContainer", null).Additions;

        Assert.Equal([kept], ContentProfiles.Brings(additions[0].Value, items, [kept]));
        Assert.Equal([banned], ContentProfiles.Brings(additions[1].Value, items, [kept]));
    }

    [Fact]
    public void Validation_reports_what_the_server_refuses()
    {
        LootContentsDocument document = Contents("""
            {"name": "Dup", "tag": "Socket.LootContainer.Tech"},
            {"name": "Dup", "tag": "socket.lootcontainer.tech"},
            {"name": "Both", "tag": "Socket.LootContainer", "class": "/Game/BP_Drawer.BP_Drawer_C"},
            {"name": "Outside", "tag": "Socket.Other"},
            {"name": "Unserved", "class": "/Game/BP_Other.BP_Other_C"},
            {"name": "Matches", "tag": "Socket.LootContainer.Raider", "only": [{"dropTag": "Item.Drop.Type.Salvage", "item": "DA_Item_Wires"}, {"item": "DA_Item_Nope"},
              {"dropTag": "Item.Drop.Nobody"}], "weights": [{"item": "DA_Item_Wires", "multiplier": -1}],
              "add": [{"item": "DA_Item_Battery", "amount": {"min": 0, "max": 1}, "tier": "Mythic"}], "tiers": {"Gold": 1, "Common": -1},
              "firstStackTier": "Gold", "stacks": {"min": 3, "max": 2}, "extraStack": {"chance": 2}},
            {"name": "Loose", "tag": "Socket.Loot.Sofa", "stacks": {"min": 1, "max": 1}, "firstStackTier": null},
            {"name": "Fine", "class": "/Game/BP_Box.BP_Box_C", "weights": [{"dropTag": "Item.Drop.Type.Salvage.Material", "multiplier": 0}]}
            """);
        EditorGameData gameData = SyntheticData();

        string[] problems = [.. ContentProfiles.Validate(document, gameData.Roll).Select(problem => $"{problem.Profile.Name}: {problem.Message}")];

        Assert.Equal(
        [
            "Dup: another profile has the same name",
            "Dup: another profile has the tag socket.lootcontainer.tech",
            "Both: it needs exactly one of tag and class",
            "Outside: its tag Socket.Other is not Socket.LootContainer or Socket.Loot or below",
            "Unserved: its class /Game/BP_Other.BP_Other_C is not a container class the server serves",
            "Matches: a match needs exactly one of dropTag and item",
            "Matches: no item the roll draws from is named DA_Item_Nope",
            "Matches: no item carries the drop tag Item.Drop.Nobody",
            "Matches: the weight of DA_Item_Wires is below 0",
            "Matches: the amount of DA_Item_Battery starts below 1",
            "Matches: DA_Item_Battery names the unknown tier Mythic",
            "Matches: tiers names the unknown tier Gold",
            "Matches: the multiplier of tier Common is below 0",
            "Matches: firstStackTier names the unknown tier Gold",
            "Matches: stacks has its min above its max",
            "Matches: the extra stack's chance is not between 0 and 1",
            "Loose: a loose item profile sets no firstStackTier, stacks or extraStack",
        ], problems);
    }

    private static RollItem Item(string name, string dropTag) => new(name, null, 10, [dropTag], [], []);

    private EditorGameData SyntheticData()
    {
        SyntheticGameData.Write(_folder);
        return EditorGameData.Load(_folder.Path, SyntheticGameData.SpawnDefaults);
    }

    private LootContentsDocument Contents(string profiles) => LootContentsDocument.Load(_folder.Write(LootContentsDocument.FileName, $$"""
        {
          "excludedDropTags": [],
          "tiers": [{"name": "Common", "dropTag": "Item.Drop.Type.Salvage.Material.Common", "weight": 1}, {"name": "Rare", "weight": 1}, {"name": "Other", "weight": 1}],
          "targetStacks": [{"target": 2, "weight": 1}],
          "tablePools": [],
          "extraStack": {"chance": 0, "entries": []},
          "typePools": [],
          "profiles": [{{profiles}}]
        }
        """));
}
