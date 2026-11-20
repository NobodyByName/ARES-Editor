using Ares.Editor.Core.GameData;

namespace Ares.Editor.App.Editing;

/// <summary>Which containers the 3D view draws through walls and floors. The numbers are the modes of scene.js <c>setXrayMode</c>.</summary>
public enum XrayMode
{
    /// <summary>None: the map hides containers behind it.</summary>
    Off = 0,

    /// <summary>Those within <see cref="SceneOptions.XrayDistance"/> of the camera; farther ones are hidden by the map.</summary>
    Near = 1,

    /// <summary>All of them.</summary>
    All = 2,
}

/// <summary>What changed in a <see cref="SceneOptions"/>.</summary>
[Flags]
public enum SceneOptionChanges
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>The X-ray mode or distance.</summary>
    Xray = 1,

    /// <summary>Whether the loot value areas are shown.</summary>
    Areas = 2,

    /// <summary>The loot value whose areas are drawn brighter.</summary>
    AreaHighlight = 4,

    /// <summary>Whether the loose item sockets, and those on nature, are shown.</summary>
    LooseSockets = 8,

    /// <summary>Whether the loot placement limiters are shown.</summary>
    Limiters = 16,

    /// <summary>The limiter that is drawn brighter.</summary>
    LimiterHighlight = 32,
}

/// <summary>
/// The operator's display choices for the 3D view besides the map geometry: X-ray containers, the loot value areas, the
/// loose item sockets and the loot placement limiters. Every change raises <see cref="Changed"/>.
/// </summary>
public sealed class SceneOptions
{
    // The X-ray distance range, in metres.
    private const double MinimumXrayDistance = 5;
    private const double MaximumXrayDistance = 500;

    /// <summary>The number of steps of the X-ray distance slider; the distance grows by the same factor at each step.</summary>
    public const int XraySliderSteps = 100;

    /// <summary>Raised after every change, with what changed.</summary>
    public event Action<SceneOptionChanges>? Changed;

    /// <summary>Which containers are drawn through the map.</summary>
    public XrayMode XrayMode { get; private set; } = XrayMode.Near;

    /// <summary>For <see cref="XrayMode.Near"/>: how close to the camera a container is drawn through the map, in whole metres.</summary>
    public double XrayDistance { get; private set; } = 50;

    /// <summary>The slider step of <see cref="XrayDistance"/>, from 0 to <see cref="XraySliderSteps"/>.</summary>
    public int XraySliderPosition =>
        (int)Math.Round(XraySliderSteps * Math.Log(XrayDistance / MinimumXrayDistance) / Math.Log(MaximumXrayDistance / MinimumXrayDistance));

    /// <summary>Whether the loot value areas are drawn.</summary>
    public bool AreasVisible { get; private set; } = true;

    /// <summary>The loot value whose areas are drawn brighter, or <see cref="LootArea.Unknown"/> for none.</summary>
    public LootArea HighlightedArea { get; private set; } = LootArea.Unknown;

    /// <summary>Whether the loose item sockets not on nature are drawn.</summary>
    public bool LooseSocketsVisible { get; private set; } = true;

    /// <summary>
    /// Whether the loose item sockets on nature (<see cref="LooseItemSocket.NatureTag"/> and below: trees, rocks, plants)
    /// are drawn; off by default, as they outnumber the others.
    /// </summary>
    public bool NatureSocketsVisible { get; private set; }

    /// <summary>Whether the loot placement limiters are drawn; off by default, as most share their volumes with the loot value areas.</summary>
    public bool LimitersVisible { get; private set; }

    /// <summary>The position in <see cref="MapLoot.Limiters"/> of the limiter drawn brighter and opened in the list, or -1 for none.</summary>
    public int HighlightedLimiter { get; private set; } = -1;

    /// <summary>Changes which containers are drawn through the map.</summary>
    /// <param name="mode">The mode.</param>
    public void SetXrayMode(XrayMode mode)
    {
        if (mode == XrayMode)
        {
            return;
        }

        XrayMode = mode;
        Raise(SceneOptionChanges.Xray);
    }

    /// <summary>Sets the X-ray distance from a slider step, rounded to whole metres.</summary>
    /// <param name="position">The step, from 0 to <see cref="XraySliderSteps"/> (clamped).</param>
    public void SetXraySliderPosition(int position)
    {
        double fraction = Math.Clamp(position, 0, XraySliderSteps) / (double)XraySliderSteps;
        double distance = Math.Round(MinimumXrayDistance * Math.Pow(MaximumXrayDistance / MinimumXrayDistance, fraction));
        if (distance.Equals(XrayDistance))
        {
            return;
        }

        XrayDistance = distance;
        Raise(SceneOptionChanges.Xray);
    }

    /// <summary>Shows or hides the loot value areas.</summary>
    /// <param name="visible">Whether they are drawn.</param>
    public void SetAreasVisible(bool visible)
    {
        if (visible == AreasVisible)
        {
            return;
        }

        AreasVisible = visible;
        Raise(SceneOptionChanges.Areas);
    }

    /// <summary>Shows or hides the loose item sockets not on nature.</summary>
    /// <param name="visible">Whether they are drawn.</param>
    public void SetLooseSocketsVisible(bool visible)
    {
        if (visible == LooseSocketsVisible)
        {
            return;
        }

        LooseSocketsVisible = visible;
        Raise(SceneOptionChanges.LooseSockets);
    }

    /// <summary>Shows or hides the loose item sockets on nature.</summary>
    /// <param name="visible">Whether they are drawn.</param>
    public void SetNatureSocketsVisible(bool visible)
    {
        if (visible == NatureSocketsVisible)
        {
            return;
        }

        NatureSocketsVisible = visible;
        Raise(SceneOptionChanges.LooseSockets);
    }

    /// <summary>Draws the areas of a loot value brighter.</summary>
    /// <param name="value">The value, or <see cref="LootArea.Unknown"/> for none.</param>
    public void HighlightArea(LootArea value)
    {
        if (value == HighlightedArea)
        {
            return;
        }

        HighlightedArea = value;
        Raise(SceneOptionChanges.AreaHighlight);
    }

    /// <summary>Shows or hides the loot placement limiters.</summary>
    /// <param name="visible">Whether they are drawn.</param>
    public void SetLimitersVisible(bool visible)
    {
        if (visible == LimitersVisible)
        {
            return;
        }

        LimitersVisible = visible;
        Raise(SceneOptionChanges.Limiters);
    }

    /// <summary>Draws a limiter brighter and opens it in the list.</summary>
    /// <param name="index">Its position in <see cref="MapLoot.Limiters"/>, or -1 for none.</param>
    public void HighlightLimiter(int index)
    {
        index = Math.Max(index, -1);
        if (index == HighlightedLimiter)
        {
            return;
        }

        HighlightedLimiter = index;
        Raise(SceneOptionChanges.LimiterHighlight);
    }

    private void Raise(SceneOptionChanges changes) => Changed?.Invoke(changes);
}
