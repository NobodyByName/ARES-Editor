using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Tests;

/// <summary>Real-data tier: the content files ARES-Server ships load and save back byte for byte.</summary>
public sealed class ContentRoundTripTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void The_shipped_loot_rules_save_unchanged()
    {
        string source = RealData.ContentFile(LootRulesDocument.FileName);
        LootRulesDocument document = LootRulesDocument.Load(Copy(source));

        Assert.Equal(["0", "1", "2"], Assert.Single(document.Rules, rule => rule.Name == "Server racks").GroupFill.Keys);
        Assert.NotEmpty(Assert.Single(document.Rules, rule => rule.Name == "Locker racks").GroupFill.Keys);
        SavesUnchanged(document, source);
    }

    [Fact]
    public void The_shipped_loot_spawn_saves_unchanged()
    {
        string source = RealData.ContentFile(LootSpawnDocument.FileName);
        LootSpawnDocument document = LootSpawnDocument.Load(Copy(source));

        Assert.InRange(Assert.NotNull(document.Chances.Get("Socket.LootContainer")), 0, 1);
        Assert.Equal(1.0, document.AreaMultipliers.Get("Unknown"));
        Assert.True(document.GroupCaps);
        Assert.True(document.Limiters);
        Assert.NotEmpty(document.SpawnerClasses);
        Assert.Equal(new LootSpawnDefaults(2, LootLimiterShape.Sphere, 1000, true, 0, 1), document.Defaults);
        SavesUnchanged(document, source);
    }

    [Fact]
    public void The_shipped_loot_contents_save_unchanged()
    {
        string source = RealData.ContentFile(LootContentsDocument.FileName);
        LootContentsDocument document = LootContentsDocument.Load(Copy(source));

        Assert.Single(document.Tiers, tier => tier.DropTag is null);
        Assert.Contains(document.Tiers, tier => string.Equals(tier.Name, document.FirstStackTier, StringComparison.Ordinal));
        Assert.NotEmpty(document.ExcludedDropTags);
        Assert.All(document.TablePools, pool => Assert.NotEmpty(pool.Tables));
        Assert.InRange(document.ExtraStack.Chance, 0, 1);
        Assert.NotEmpty(document.ExtraStack.Entries);
        Assert.All(document.TypePools, pool => Assert.NotEmpty(pool.Entries));
        Assert.Equal([(2, 2.0), (3, 3.0)], document.TargetStacks.Select(target => (target.Target, target.Weight)));
        SavesUnchanged(document, source);
    }

    private static void SavesUnchanged(ContentDocument document, string source)
    {
        byte[] expected = File.ReadAllBytes(source);
        document.Save();
        Assert.Equal(expected, File.ReadAllBytes(document.FilePath));
        Assert.Equal(File.ReadAllText(source), document.ToJson());
    }

    private string Copy(string source)
    {
        string copy = Path.Combine(_folder.Path, Path.GetFileName(source));
        File.Copy(source, copy);
        return copy;
    }
}
