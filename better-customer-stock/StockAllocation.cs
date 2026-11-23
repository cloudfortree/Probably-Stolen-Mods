using System;
using System.Collections.Generic;
using System.Linq;
using static BetterCustomerStock.StockLanguage;

namespace BetterCustomerStock;

internal sealed class StockQuota
{
    internal readonly int Total;
    internal readonly string[] SingleUnit;

    internal StockQuota(int total, params string[] singleUnit)
    {
        Total = total;
        SingleUnit = singleUnit;
    }

    internal int LimitFor(string id) => SingleUnit.Contains(id) ? 1 : Total;
}

internal sealed class StockAllocation
{
    internal const int Random = -1;
    internal static string ModeLabel => Text("选择数量替代修改权重", "Select Quantities Instead of Adjusting Weights");
    internal static string RandomLabel => Text("随机", "Random");
    internal static string QuantityTip => Line(
        "用滑动条选择数量。选择「{0}」时，该货物参与剩余名额的原版抽选。选择 0 件会排除该货物，选择正整数会固定其件数。名额用完后，调低已指定的件数可为其他货物腾出名额。",
        "Use the slider to set a quantity. Select {0} to keep the item eligible for the remaining vanilla draws, 0 to exclude it, or a positive number to set its exact quantity. Once the group is full, lower a set quantity to free slots for other goods.",
        RandomLabel);

    internal static string DescribeSliderCount(int count)
        => count < 0 ? RandomLabel : Line("{0} 件", "{0}", count);

    private readonly Dictionary<string, int> specified;
    private readonly string[] fixedItems;
    private readonly StockQuota quota;

    internal StockAllocation(StockChoice choice, int[] counts)
    {
        quota = choice.Quota!;
        specified = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < counts.Length; index++)
            if (counts[index] >= 0)
                specified[choice.Categories[index]] = counts[index];

        fixedItems = choice.Categories.Where(specified.ContainsKey)
            .OrderBy(quota.LimitFor)
            .SelectMany(id => Enumerable.Repeat(id, specified[id])).ToArray();
    }

    internal bool HasSpecifications => specified.Count != 0;
    internal int FixedCount => fixedItems.Length;
    internal int Remaining => quota.Total - FixedCount;

    internal int EditableLimit(string id)
    {
        specified.TryGetValue(id, out int current);
        // Keep previously saved over-allocations editable downward.
        return Math.Min(quota.LimitFor(id), current + Math.Max(0, Remaining));
    }

    internal bool IsRandom(string id) => !specified.ContainsKey(id);
    internal string? FixedItem(int slot) => slot >= 0 && slot < FixedCount ? fixedItems[slot] : null;

    internal string? ValidationError(IEnumerable<string> eligible)
    {
        if (FixedCount > quota.Total)
            return Line("已指定 {0} 件，超出本组的 {1} 个名额。", "You selected {0} units for a group with {1} slots.", FixedCount, quota.Total);

        var available = new HashSet<string>(eligible, StringComparer.Ordinal);
        foreach (var entry in specified)
        {
            int limit = quota.LimitFor(entry.Key);
            if (entry.Value > limit)
                return Line("{0}在本组最多可选 {1} 件。", "You can select up to {1} of {0} in this group.", StockCatalog.Name(entry.Key), limit);
            if (entry.Value > 0 && !available.Contains(entry.Key))
                return Line("{0}当前不在候选中。", "{0} is currently ineligible.", StockCatalog.Name(entry.Key));
        }

        int capacity = available.Where(IsRandom).Sum(quota.LimitFor);
        return capacity < Remaining
            ? Line("剩余 {0} 个名额，当前候选最多可补 {1} 件。", "{0} slots remain, and eligible random choices can fill at most {1}.", Remaining, capacity)
            : null;
    }

    internal double[] RandomWeights(IReadOnlyList<string> ids, IReadOnlyList<double> nativeWeights)
    {
        double[] weights = ids.Select((id, index) => IsRandom(id) ? nativeWeights[index] : 0d).ToArray();
        double total = weights.Sum();
        if (total > 0)
            for (int index = 0; index < weights.Length; index++)
                weights[index] /= total;
        return weights;
    }

    internal string Summary()
        => Line("已指定 {0} / {1} 件，剩余 {2} 件按原版规则抽选。",
            "{0} of {1} units specified. The remaining {2} use the vanilla draw rules.", FixedCount, quota.Total, Remaining);

    internal static string DescribeCount(int count)
        => count < 0 ? Text("随原版抽选", "Use vanilla draws")
            : count == 0 ? Text("0 件（排除）", "0 (Excluded)")
            : Line("{0} 件", "{0} fixed", count);
}
