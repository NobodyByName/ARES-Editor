using System.Globalization;

namespace Ares.Editor.Core.Content;

/// <summary>One number of filled sockets of a group fill and its chance.</summary>
/// <param name="Count">How many sockets of a group get a container.</param>
/// <param name="Key">The key as the file writes it.</param>
/// <param name="Percent">Chance from 0 to 100 that a group draws this number.</param>
public sealed record FillShare(int Count, string Key, double Percent);

/// <summary>
/// A rule's group fill as chances in percent that total 100, for editing with linked sliders. ARES-Server draws a number
/// by weight (<c>Ares.Common.Game.Loot.LootFill.Draw</c>: chance = weight / sum of the weights, at most the group's
/// sockets), so writing the percentages as the weights keeps the chances. Keys and weights are checked as
/// <c>Ares.Common.Game.Content.LootRulesContent</c> (<c>Fill</c>) checks them: each key a whole number 0 or more that
/// appears once, each weight 0 or more, and a total above 0. Keep it in step with those members.
/// </summary>
public sealed class GroupFillShares
{
    /// <summary>The step of an edited chance, in percent.</summary>
    public const double Step = 0.1;

    private const int StepsPerPercent = 10;
    private const int Whole = 100 * StepsPerPercent;

    private readonly FillShare[] _shares;
    private readonly double[] _weights;

    private GroupFillShares(FillShare[] shares, double[] weights)
    {
        _shares = shares;
        _weights = weights;
    }

    /// <summary>The numbers in ascending order with their chances: those with a valid key, the first spelling of each.</summary>
    public IReadOnlyList<FillShare> Shares => _shares;

    /// <summary>The number <see cref="Add"/> adds: one more than the highest, or 0.</summary>
    public int NextCount => _shares.Length == 0 ? 0 : _shares[^1].Count + 1;

    /// <summary>Reads a rule's group fill.</summary>
    /// <param name="groupFill">The rule's <see cref="LootRule.GroupFill"/>.</param>
    /// <returns>The chances: weight / sum (a negative weight counts as 0), or equal chances when no weight is above 0.</returns>
    public static GroupFillShares Of(NumberMap groupFill)
    {
        ArgumentNullException.ThrowIfNull(groupFill);
        List<(int Count, string Key, double Weight)> entries = [];
        foreach ((string key, double weight) in groupFill)
        {
            if (TryCount(key, out int count) && !entries.Exists(entry => entry.Count == count))
            {
                entries.Add((count, key, weight));
            }
        }

        entries.Sort((left, right) => left.Count.CompareTo(right.Count));
        return Create([.. entries.Select(entry => (entry.Count, entry.Key))], [.. entries.Select(entry => entry.Weight)]);
    }

    /// <summary>Why ARES-Server would refuse a group fill, in its words; empty when it accepts it.</summary>
    /// <param name="groupFill">The rule's <see cref="LootRule.GroupFill"/>.</param>
    /// <returns>The problems.</returns>
    public static IReadOnlyList<string> Problems(NumberMap groupFill)
    {
        ArgumentNullException.ThrowIfNull(groupFill);
        if (!groupFill.IsSet)
        {
            return [];
        }

        List<string> problems = [];
        HashSet<int> counts = [];
        double total = 0;
        foreach ((string key, double weight) in groupFill)
        {
            if (!TryCount(key, out int count))
            {
                problems.Add($"groupFill key {key} must be a number of sockets (0 or more)");
            }
            else if (weight < 0 || !counts.Add(count))
            {
                problems.Add($"groupFill {key} must appear once with a weight of 0 or more");
            }
            else
            {
                total += weight;
            }
        }

        if (problems.Count == 0 && total <= 0)
        {
            problems.Add("groupFill needs a weight above 0");
        }

        return problems;
    }

    /// <summary>Whether a key is a number of sockets as ARES-Server reads it (digits only, invariant culture).</summary>
    /// <param name="key">The key.</param>
    /// <param name="count">The number.</param>
    /// <returns>False when the server refuses the key.</returns>
    public static bool TryCount(string key, out int count) => int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out count);

    /// <summary>How many sockets of a group get a container on average: the sum of chance × the number, at most the group's sockets.</summary>
    /// <param name="sockets">The group's sockets.</param>
    /// <param name="areaMultiplier">Factor of the loot value of the group's area (see <see cref="InArea"/>).</param>
    /// <returns>The expected number.</returns>
    public double ExpectedFilled(int sockets, double areaMultiplier = 1) =>
        InArea(sockets, areaMultiplier).Sum(share => share.Percent / 100 * Math.Min(share.Count, sockets));

    /// <summary>
    /// The chances of a group in an area, ported from ARES-Server's <c>Ares.Common.Game.Loot.LootFill.InArea</c>: each
    /// number's weight times <paramref name="areaMultiplier"/> to the power of the sockets it fills (the number, at most
    /// <paramref name="sockets"/>), which multiplies every socket's odds by the area's factor. The chances here are
    /// those outside every area with a loot value (factor 1). Keep it in step with that member.
    /// </summary>
    /// <param name="sockets">The group's sockets.</param>
    /// <param name="areaMultiplier">Factor of the loot value of the group's area.</param>
    /// <returns>The numbers with their chances in the area; this instance's chances at factor 1.</returns>
    public IReadOnlyList<FillShare> InArea(int sockets, double areaMultiplier)
    {
        if (areaMultiplier is 1d || _shares.Length == 0)
        {
            return _shares;
        }

        int capped = Math.Max(sockets, 0);
        double[] weights;
        if (!(areaMultiplier > 0))
        {
            weights = [.. _shares.Select(share => Math.Min(share.Count, capped) == 0 ? share.Percent : 0)];
        }
        else
        {
            // In logarithms, less the largest, so a high factor on a large group cannot overflow.
            double factor = Math.Log(areaMultiplier);
            double[] logs = [.. _shares.Select(share => share.Percent > 0 ? Math.Log(share.Percent) + (Math.Min(share.Count, capped) * factor) : double.NegativeInfinity)];
            double largest = logs.Max();
            weights = [.. logs.Select(log => double.IsNegativeInfinity(log) ? 0 : Math.Exp(log - largest))];
        }

        double total = weights.Sum();
        return [.. _shares.Select((share, index) => share with { Percent = total > 0 ? weights[index] * 100 / total : 0 })];
    }

    /// <summary>
    /// Sets one number's chance and rescales the others to keep the total at 100: in proportion to their chances, or
    /// evenly when they are all 0. Chances are rounded to <see cref="Step"/>; the rounding remainder goes to the other
    /// number with the largest share. A single number always has 100.
    /// </summary>
    /// <param name="count">The number.</param>
    /// <param name="percent">Its new chance; clamped to 0..100.</param>
    /// <returns>The new chances, or this instance when the rounded chance does not change or the number is not here.</returns>
    public GroupFillShares With(int count, double percent)
    {
        int position = Array.FindIndex(_shares, share => share.Count == count);
        if (position < 0 || !double.IsFinite(percent))
        {
            return this;
        }

        int steps = _shares.Length == 1 ? Whole : (int)Math.Round(Math.Clamp(percent, 0, 100) * StepsPerPercent, MidpointRounding.AwayFromZero);
        if (steps == Steps(_shares[position].Percent))
        {
            return this;
        }

        int[] result = new int[_shares.Length];
        result[position] = steps;
        Spread(result, position, Whole - steps, index => _shares[index].Percent);
        return FromSteps(_shares, result);
    }

    /// <summary>
    /// Removes a number; the others keep their weights, so they share its chance in proportion. When they are all 0 they
    /// share 100 evenly instead.
    /// </summary>
    /// <param name="count">The number.</param>
    /// <returns>The new chances, or this instance when the number is not here.</returns>
    public GroupFillShares Without(int count)
    {
        int position = Array.FindIndex(_shares, share => share.Count == count);
        if (position < 0)
        {
            return this;
        }

        FillShare[] kept = [.. _shares.Where((_, index) => index != position)];
        double[] weights = [.. _weights.Where((_, index) => index != position)];
        if (kept.Length == 0 || weights.Any(weight => weight > 0))
        {
            return Create([.. kept.Select(share => (share.Count, share.Key))], weights);
        }

        int[] steps = new int[kept.Length];
        Spread(steps, -1, Whole, _ => 0);
        return FromSteps(kept, steps);
    }

    /// <summary>Adds <see cref="NextCount"/> with chance 0, or with 100 when it is the first number.</summary>
    /// <returns>The new chances.</returns>
    public GroupFillShares Add()
    {
        int count = NextCount;
        return Create(
            [.. _shares.Select(share => (share.Count, share.Key)), (count, count.ToString(CultureInfo.InvariantCulture))],
            [.. _weights, _shares.Length == 0 ? 100 : 0]);
    }

    /// <summary>
    /// Writes the weights to a rule's group fill: each number's weight (a value it already has keeps its text) and no
    /// other key, so keys the server would refuse go. Without numbers the member goes.
    /// </summary>
    /// <param name="groupFill">The rule's <see cref="LootRule.GroupFill"/>.</param>
    public void WriteTo(NumberMap groupFill)
    {
        ArgumentNullException.ThrowIfNull(groupFill);
        for (int index = 0; index < _shares.Length; index++)
        {
            groupFill.Set(_shares[index].Key, _weights[index]);
        }

        foreach (string key in groupFill.Keys.Where(key => !Array.Exists(_shares, share => share.Key == key)))
        {
            groupFill.Remove(key);
        }
    }

    private static GroupFillShares Create((int Count, string Key)[] entries, double[] weights)
    {
        double total = weights.Sum(weight => Math.Max(weight, 0));
        FillShare[] shares = new FillShare[entries.Length];
        for (int index = 0; index < entries.Length; index++)
        {
            double percent = total > 0 ? Math.Max(weights[index], 0) * 100 / total : 100.0 / entries.Length;
            shares[index] = new FillShare(entries[index].Count, entries[index].Key, percent);
        }

        return new GroupFillShares(shares, weights);
    }

    private static GroupFillShares FromSteps(FillShare[] shares, int[] steps)
    {
        // The steps total 100 %, so each weight is its chance.
        double[] weights = [.. steps.Select(step => step / (double)StepsPerPercent)];
        return new GroupFillShares([.. shares.Select((share, index) => share with { Percent = weights[index] })], weights);
    }

    private static int Steps(double percent) => (int)Math.Round(percent * StepsPerPercent, MidpointRounding.AwayFromZero);

    // Shares a number of steps among every position but one, in proportion to their current chances (evenly when they
    // are all 0); the rounding remainder goes to the position with the largest exact share.
    private static void Spread(int[] steps, int skip, int remainder, Func<int, double> current)
    {
        int[] others = [.. Enumerable.Range(0, steps.Length).Where(index => index != skip)];
        if (others.Length == 0)
        {
            return;
        }

        double total = others.Sum(current);
        double[] exact = [.. others.Select(index => total > 0 ? current(index) / total * remainder : (double)remainder / others.Length)];
        int largest = 0;
        int given = 0;
        for (int position = 0; position < others.Length; position++)
        {
            steps[others[position]] = (int)Math.Round(exact[position], MidpointRounding.AwayFromZero);
            given += steps[others[position]];
            if (exact[position] > exact[largest])
            {
                largest = position;
            }
        }

        steps[others[largest]] += remainder - given;
    }
}
