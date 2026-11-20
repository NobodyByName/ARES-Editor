using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;
using Ares.Editor.Core.Matching;
using Ares.Editor.Core.Paths;

namespace Ares.Editor.Core.Tests;

/// <summary>Real-data tier: the extracted game data (<c>ARES_GAMEDATA_DIR</c>) with the content ARES-Server ships.</summary>
public sealed class RealGameDataTests
{
    private static readonly Lazy<EditorWorkspace> s_workspace = new(() =>
        EditorWorkspace.Open(ServerLocation.FromDirectories(RealData.ContentDirectory!, RealData.GameDataDirectory!)));

    [Fact]
    public void The_dam_has_every_kind_of_container_with_unique_ids()
    {
        RealData.RequireGameData();
        RealData.ContentFile(LootRulesDocument.FileName);
        EditorWorkspace workspace = s_workspace.Value;

        Assert.True(workspace.GameData.Manifest.GameBuildId > 0);
        MapLoot dam = Assert.Single(workspace.GameData.Maps, map => map.Name.StartsWith("TheDam", StringComparison.Ordinal));
        Dictionary<LootContainerSource, int> counts = dam.Containers.GroupBy(container => container.Source).ToDictionary(group => group.Key, group => group.Count());
        TestContext.Current.SendDiagnosticMessage($"{dam.Name}: {string.Join(", ", counts.Select(entry => $"{entry.Key} {entry.Value}"))}");
        Assert.All(Enum.GetValues<LootContainerSource>(), source => Assert.True(counts.GetValueOrDefault(source) > 0, $"no {source}"));
        Assert.Equal(dam.Containers.Count, dam.Containers.Select(container => container.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(dam.Containers, container => container.Area != LootArea.Unknown);
    }

    [Fact]
    public void The_dams_loose_item_sockets_are_placed_and_mostly_on_nature()
    {
        RealData.RequireGameData();
        RealData.ContentFile(LootRulesDocument.FileName);
        EditorWorkspace workspace = s_workspace.Value;
        MapLoot dam = Assert.Single(workspace.GameData.Maps, map => map.Name.StartsWith("TheDam", StringComparison.Ordinal));

        int nature = dam.LooseItemSockets.Count(socket => socket.IsNature);
        TestContext.Current.SendDiagnosticMessage($"{dam.Name}: {dam.LooseItemSockets.Count} loose item sockets, {nature} on nature, {dam.LooseItemTags.Count} tags");
        Assert.True(dam.LooseItemSockets.Count > 1000);
        Assert.InRange(nature, dam.LooseItemSockets.Count / 4, dam.LooseItemSockets.Count - 100);
        Assert.All(dam.LooseItemSockets, socket => Assert.True(LootMatching.IsTagOrBelow(socket.Tag, "Socket.Loot"), socket.Tag));
        Assert.Contains(dam.LooseItemSockets, socket => socket.Location != default && socket.Area != LootArea.Unknown);
        Assert.Contains(dam.LooseItemSockets, socket => socket.Options.HasFlag(LooseItemOptions.EnablePhysics));
        Assert.Equal(dam.LooseItemSockets.Count, dam.LooseItemSockets.Select(socket => socket.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Null(dam.Find(dam.LooseItemSockets[0].Id));

        LooseItemMatch match = workspace.Match(dam.LooseItemSockets.First(socket => !socket.IsNature));
        Assert.NotNull(match.ChanceTag);
        Assert.InRange(match.Chance, 0, 1);
    }

    [Fact]
    public void The_shipped_rules_fill_server_racks_by_group()
    {
        RealData.RequireGameData();
        RealData.ContentFile(LootRulesDocument.FileName);
        EditorWorkspace workspace = s_workspace.Value;
        LootContainer rack = workspace.GameData.Maps.SelectMany(map => map.Containers)
            .First(container => container.Tags.Any(tag => LootMatching.IsTagOrBelow(tag, "Socket.LootContainer.Tech.Server.Door")));

        ContainerMatch match = workspace.Match(rack);

        Assert.Equal(SpawnDecision.GroupFill, match.Decision);
        Assert.Equal("Server racks", match.SpawnRule!.Name);
        Assert.Equal(workspace.Location.ContentDirectory, Path.GetDirectoryName(workspace.Rules.FilePath));
        Assert.NotNull(workspace.GameData.TagTree.Find("Socket.LootContainer.Tech.Server.Door"));
    }

    [Fact]
    public void The_shipped_profiles_are_valid_and_a_misspelled_tag_resolves_through_its_profile()
    {
        RealData.RequireGameData();
        EditorWorkspace workspace = s_workspace.Value;
        LootContentsDocument contents = LootContentsDocument.Load(RealData.ContentFile(LootContentsDocument.FileName));

        TestContext.Current.SendDiagnosticMessage($"{contents.Profiles.Count} shipped profiles");
        Assert.Empty(ContentProfiles.Validate(contents, workspace.GameData.Roll));
        string misspelled = workspace.GameData.Maps.SelectMany(map => map.Containers).SelectMany(container => container.Tags)
            .First(tag => tag.StartsWith("Socket.LootCOntainer.", StringComparison.Ordinal));
        string category = string.Join('.', ["Socket", "LootContainer", .. misspelled.Split('.').Skip(2).Take(1)]);
        LootProfile profile = contents.Profiles.FirstOrDefault(entry => string.Equals(entry.Tag, category, StringComparison.OrdinalIgnoreCase))
            ?? contents.AddProfile("Misspelled category", category, null);
        Assert.Contains(profile, ContentProfiles.ChainOf(contents, misspelled, null));
    }

    [Fact]
    public void Container_items_include_the_ferro_found_by_its_technical_name()
    {
        RealData.RequireGameData();
        RealData.ContentFile(LootRulesDocument.FileName);
        EditorWorkspace workspace = s_workspace.Value;
        HashSet<string> names = [.. workspace.GameData.ContainerItems.Select(item => item.TechnicalName)];

        TestContext.Current.SendDiagnosticMessage($"{names.Count} container items, {workspace.GameData.ContainerItems.Count(item => item.DisplayName is null)} without display name");
        Assert.Contains("DA_Item_BattleRifle_BreachAction_01", names);
        Assert.Equal("DA_Item_BattleRifle_BreachAction_01", ContainerItemSearch.Find(workspace.GameData.ContainerItems, "BreachAction")[0].TechnicalName);
    }
}
