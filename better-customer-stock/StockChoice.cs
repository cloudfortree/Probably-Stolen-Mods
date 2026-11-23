using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace BetterCustomerStock;

internal enum StockChoiceKind
{
    IntegerRoll,
    FloatRoll,
    Range,
    Inspectable,
    Appraisal,
    Backpack,
    List,
    Table
}

internal sealed class StockChoice
{
    private static readonly MethodInfo IntegerRoll = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.ChoiceInt));
    private static readonly MethodInfo FloatRoll = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.ChoiceFloat));
    private static readonly MethodInfo Range = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.ChoiceRange));
    private static readonly MethodInfo RangeSlot = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.ChoiceRangeSlot));
    private static readonly MethodInfo Inspectable = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.ChoiceInspectable));
    private static readonly MethodInfo Appraisal = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.ChoiceAppraisal));
    private static readonly MethodInfo Backpack = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.ChoiceBackpack));
    private static readonly MethodInfo List = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.ChoiceList));

    internal readonly string Id;
    private readonly string sourceLabel;
    internal string Label => StockLanguage.GroupName(sourceLabel);
    internal readonly Func<string>? Tooltip;
    internal readonly string? ProductCategory;
    internal readonly StockChoiceKind Kind;
    internal readonly string[] Categories;
    internal readonly double[] NativeWeights;
    internal readonly double[] NativeChances;
    internal readonly int[] RangeStarts;
    internal readonly StockQuota? Quota;
    internal string SettingsSection => Quota == null ? StockLanguage.Classification : StockLanguage.SpecialGoodsSettings;
    private readonly int[] sites;

    internal StockChoice(string id, string label, StockChoiceKind kind, string categories, int[] sites,
        double[]? chances = null, int[]? rangeStarts = null, Func<string>? tooltip = null,
        string? productCategory = null, StockQuota? quota = null)
    {
        Id = id;
        sourceLabel = label;
        Tooltip = tooltip;
        ProductCategory = kind == StockChoiceKind.Table ? id : productCategory;
        Kind = kind;
        Categories = categories.Split(' ');
        this.sites = sites.Select(position => position - 1).ToArray();
        NativeChances = chances ?? Array.Empty<double>();
        RangeStarts = rangeStarts ?? Array.Empty<int>();
        Quota = quota;
        NativeWeights = chances != null ? StockCurve.SequentialWeights(chances) : Array.Empty<double>();
    }

    internal int IndexOfSite(int site) => Array.IndexOf(sites, site);

    internal double[] RangeWeights(int maxInclusive)
    {
        var weights = new double[Categories.Length];
        for (int index = 0; index < weights.Length; index++)
            weights[index] = Math.Max(0, Math.Min(maxInclusive + 1, RangeStarts[index + 1]) - RangeStarts[index]);
        return weights;
    }

    internal string Key(int branch) => Id + "/" + Categories[branch];

    internal MethodInfo Replacement => Kind switch
    {
        StockChoiceKind.IntegerRoll => IntegerRoll,
        StockChoiceKind.FloatRoll => FloatRoll,
        StockChoiceKind.Range => Quota == null ? Range : RangeSlot,
        StockChoiceKind.Inspectable => Inspectable,
        StockChoiceKind.Appraisal => Appraisal,
        StockChoiceKind.Backpack => Backpack,
        StockChoiceKind.List => List,
        _ => throw new ArgumentOutOfRangeException()
    };
}
