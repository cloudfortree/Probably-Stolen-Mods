using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static BetterCustomerStock.StockLanguage;

namespace BetterCustomerStock;

internal static class StockProductPreview
{
    internal delegate int PositionReader(StockMethod method, int site, StockAxis? axis = null, string? category = null);

    private const int SecurityStockThreshold = 80;
    internal const int WantedCrateBasePercent = 35;
    internal static readonly (int Day, int Units)[] MinerUnlocks = { (15, 1), (30, 2), (45, 1), (60, 1) };
    private static readonly (int Base, int? Floor, int Divisor) BlackLampOdds = (15, null, 2);

    private readonly struct Term
    {
        internal readonly StockBaseDraw Draw;
        internal readonly int Position;
        internal readonly int Repeats;
        internal Term(StockBaseDraw draw, int position, int repeats = 1)
        {
            Draw = draw;
            Position = position;
            Repeats = repeats;
        }
    }

    private sealed class Scenario
    {
        internal readonly string[] RequiredConditions;
        internal readonly int Fixed;
        internal readonly Term[] Terms;
        internal string[] AlternativeConditions = Array.Empty<string>();
        internal string Scope = "";
        internal (Scenario Low, Scenario High)? Variation;

        internal Scenario(string condition, int fixedCount, params Term[] terms)
            : this(condition.Length == 0 ? Array.Empty<string>() : new[] { condition }, fixedCount, terms)
        {
        }

        internal Scenario(string[] requiredConditions, int fixedCount, params Term[] terms)
        {
            RequiredConditions = requiredConditions;
            Fixed = fixedCount;
            Terms = terms;
        }

        internal Scenario WithRequirements(params string[] conditions)
            => new Scenario(conditions.Concat(RequiredConditions).Distinct().ToArray(), Fixed, Terms)
            { AlternativeConditions = AlternativeConditions, Scope = Scope, Variation = Variation };

        internal Scenario WithVariation(Scenario low, Scenario high)
            => new Scenario(RequiredConditions, Fixed, Terms)
            { AlternativeConditions = AlternativeConditions, Scope = Scope, Variation = (low, high) };

        internal (int Min, double[] Mass) Distribution(bool modified, bool linear)
        {
            int min = Fixed;
            double[] mass = { 1 };
            foreach (Term term in Terms)
            {
                double[] roll = term.Draw.Distribution(modified ? term.Position : 0, linear);
                for (int draw = 0; draw < term.Repeats; draw++)
                {
                    var next = new double[mass.Length + roll.Length - 1];
                    for (int i = 0; i < mass.Length; i++)
                        for (int j = 0; j < roll.Length; j++)
                            next[i + j] += mass[i] * roll[j];
                    mass = next;
                    min += term.Draw.Minimum;
                }
            }
            return (min, mass);
        }
    }

    internal static string Describe(IReadOnlyList<StockMethod> methods, StockProfile profile, StockAxis axis,
        string item, PositionReader position, bool linear)
    {
        var rows = new List<string> { ProbabilityComparison };
        bool offer = methods.Any(method => method.BarterOfferCounts.HasValue);
        foreach (Scenario scenario in ApplicableScenarios(methods, profile, axis, item, position))
        {
            var (min, original) = scenario.Distribution(false, linear);
            var (_, modified) = scenario.Distribution(true, linear);
            string title = ConditionText(scenario);
            if (scenario.Scope.Length != 0)
                title += (title.Length == 0 ? "" : "\n") + $"<b>{scenario.Scope}</b>";
            if (title.Length == 0)
                title = offer ? Text("<b>每次来访</b>", "<b>Per visit</b>")
                    : Text("<b>进入对应货物的原版抽选时</b>", "<b>When the vanilla stock roll occurs</b>");
            string separator = title.Contains("\n") ? "\n" : "　";
            if (axis == StockAxis.Appearance)
            {
                double originalZero = min == 0 ? original[0] : 0;
                double modifiedZero = min == 0 ? modified[0] : 0;
                rows.Add(title + separator + Line(offer ? "提供报价 <b>{0}</b>　未提供 {1}" : "携带 <b>{0}</b>　未携带 {1}",
                    offer ? "Offer available <b>{0}</b>  Not available {1}" : "Brought <b>{0}</b>  Not brought {1}",
                    Change(1 - originalZero, 1 - modifiedZero), Change(originalZero, modifiedZero))
                    + AppliedIfChanged(originalZero, modifiedZero));
                continue;
            }

            string outcomes = string.Join("　", original.Select((probability, index) =>
                    (probability, index)).Where(entry => entry.probability > 0 || modified[entry.index] > 0)
                .Select(entry => $"{StockAmount(min + entry.index)} "
                    + Change(entry.probability, modified[entry.index]) + AppliedIfChanged(entry.probability, modified[entry.index])));
            string expected = StockAmount("", $"{Number(Expected(min, original))} → {Number(Expected(min, modified))}");
            rows.Add(title + separator + ExpectedStock(expected) + "\n" + outcomes);
        }
        return rows.Count > 1 ? string.Join("\n\n", rows)
            : Text("这项抽选的原版概率取决于供货条件。", "The vanilla probability depends on the stock conditions.");
    }

    internal static bool HasEffect(IReadOnlyList<StockMethod> methods, StockProfile profile, StockAxis axis,
        string item, PositionReader position, bool linear, PositionReader? reference = null)
    {
        var current = ApplicableScenarios(methods, profile, axis, item, position);
        return reference == null ? current.Any(scenario => Differs(scenario, scenario))
            : current.Zip(ApplicableScenarios(methods, profile, axis, item, reference), Differs).Any(changed => changed);

        bool Differs(Scenario actual, Scenario original)
        {
            if (actual.Terms.Select(term => term.Position).SequenceEqual(original.Terms.Select(term => reference == null ? 0 : term.Position)))
                return false;
            var (min, previous) = original.Distribution(reference != null, linear);
            var (_, modified) = actual.Distribution(true, linear);
            return axis == StockAxis.Appearance ? min == 0 && ProbabilityChanged(previous[0], modified[0])
                : previous.Where((probability, index) => ProbabilityChanged(probability, modified[index])).Any();
        }
    }

    internal static bool ProbabilityChanged(double original, double modified) => Math.Abs(original - modified) > 1e-12;

    internal static string AppliedIfChanged(double original, double modified)
        => ProbabilityChanged(original, modified) ? AppliedSuffix : "";

    internal static IEnumerable<(string Condition, double Original, double Modified)> AppearanceScenarios(
        IReadOnlyList<StockMethod> methods, StockProfile profile, string item, PositionReader position, bool linear)
        => ApplicableScenarios(methods, profile, StockAxis.Appearance, item, position).Select(scenario =>
            (string.Join(" · ", scenario.RequiredConditions), ChanceAtLeast(scenario, 1, false, linear),
                ChanceAtLeast(scenario, 1, true, linear)));

    internal static (string Original, string Modified) Summarize(IReadOnlyList<StockMethod> methods,
        StockProfile profile, StockAxis axis, string item, PositionReader position, bool linear)
    {
        Scenario? baseline = ReferenceScenarios(methods, profile, axis, item, position).FirstOrDefault();
        if (baseline == null)
            return ("", "");

        var (min, original) = baseline.Distribution(false, linear);
        int[] counts = Enumerable.Range(0, original.Length).Where(index => original[index] > 0).ToArray();
        string conditions = ConditionText(baseline);
        string prerequisites = conditions.Length == 0 ? "" : conditions + "\n";
        string scope = baseline.Scope.Length == 0 ? "" : $"<b>{baseline.Scope}</b>\n";
        string modifiedScope = baseline.AlternativeConditions.Length == 0 ? "" : scope;
        bool offer = axis == StockAxis.Appearance && methods.Any(method => method.BarterOfferCounts.HasValue);
        return (prerequisites + scope + Format(false), modifiedScope + Format(true));

        string Format(bool modified)
        {
            string prefix = modified ? "当前" : "";
            int first = counts[0];
            int lowest = min + first;
            if (counts.Length == 1)
                return Line("{0}固定为 {1}（概率 <b>100%</b>）。", "{1} guaranteed (<b>100%</b>).",
                    prefix, StockAmount(item, lowest.ToString(CultureInfo.InvariantCulture)));

            int threshold = min + counts[1];
            var probability = MeasureRange(baseline, scenario => ChanceAtLeast(scenario, threshold, modified, linear));
            string hit = FormatRange(probability, value => Percent(value));
            if (offer)
                return Line("每次来访有 <b>{0}</b> 的概率提供这套报价，未提供的概率为 <b>{1}</b>。",
                    "Each visit has a <b>{0}</b> chance of including this offer and a <b>{1}</b> chance of omitting it.",
                    hit, FormatRange((1 - probability.High, 1 - probability.Low), value => Percent(value)));
            string expected = axis == StockAxis.Appearance ? "" : Text("", " ") + ExpectedStock(
                StockAmount(item, FormatRange(MeasureRange(baseline, scenario => Expected(scenario, modified, linear)), Number))) + Text("。", ".");
            if (axis == StockAxis.Appearance || counts.Length == 2)
            {
                int smallestDraw = min + counts[1];
                int largestDraw = min + counts[counts.Length - 1];
                string amount = smallestDraw.ToString(CultureInfo.InvariantCulture);
                if (largestDraw > smallestDraw)
                    amount = largestDraw - smallestDraw == counts.Length - 2 ? $"{smallestDraw}–{largestDraw}"
                        : JoinList(counts.Skip(1).Select(index => min + index));
                string miss = FormatRange((1 - probability.High, 1 - probability.Low), value => Percent(value));
                if (axis == StockAxis.Quantity || lowest > 0)
                    return Line("{0}生成 <b>{2}</b> 的概率为 <b>{1}</b>，生成 <b>{4}</b> 的概率为 <b>{3}</b>。",
                        "The chance of generating <b>{2}</b> is <b>{1}</b>. The chance of generating <b>{4}</b> is <b>{3}</b>.",
                        prefix, hit, StockAmount(item, amount), miss, StockAmount(item, lowest.ToString(CultureInfo.InvariantCulture))) + expected;
                return Line("{0}有 <b>{1}</b> 的概率抽中 <b>{2}</b>，其余 <b>{3}</b> 为 {4}。",
                    "{0} <b>{1}</b> chance of getting <b>{2}</b> and a <b>{3}</b> chance of getting {4}.",
                    Text(prefix, modified ? "There is currently a" : "There is a"), hit, StockAmount(item, amount), miss,
                    StockAmount(item, lowest.ToString(CultureInfo.InvariantCulture))) + expected;
            }
            return Line("{0}抽到至少 {1}的概率为 <b>{2}</b>，", "{0} of getting at least {1} is <b>{2}</b>.",
                Text(prefix, modified ? "The current chance" : "The chance"),
                StockAmount(item, threshold.ToString(CultureInfo.InvariantCulture)), hit) + expected;
        }
    }

    internal static string Compare(IReadOnlyList<StockMethod> methods, StockProfile profile, StockAxis axis,
        string item, PositionReader position, bool linear)
    {
        var scenarios = ReferenceScenarios(methods, profile, axis, item, position);
        if (axis == StockAxis.Appearance)
        {
            Scenario? baseline = scenarios.FirstOrDefault();
            if (baseline == null)
                return "";

            var original = MeasureRange(baseline, scenario => ChanceAtLeast(scenario, 1, false, linear));
            var modified = MeasureRange(baseline, scenario => ChanceAtLeast(scenario, 1, true, linear));
            return $"{FormatRange(original, ShortPercent)} → {FormatRange(modified, ShortPercent)}";
        }

        var expected = scenarios.Select(scenario => (Original: MeasureRange(scenario, stage => Expected(stage, false, linear)),
            Modified: MeasureRange(scenario, stage => Expected(stage, true, linear)))).ToArray();
        if (expected.Length == 0)
            return "";

        string originalCount = FormatRange((expected.Min(value => value.Original.Low), expected.Max(value => value.Original.High)), Number);
        string modifiedCount = FormatRange((expected.Min(value => value.Modified.Low), expected.Max(value => value.Modified.High)), Number);
        return $"{originalCount} → {StockAmount(item, modifiedCount)}";
    }

    private static (double Low, double High) MeasureRange(Scenario scenario, Func<Scenario, double> measure)
    {
        if (scenario.Variation is { } variation)
        {
            double first = measure(variation.Low);
            double last = measure(variation.High);
            return (Math.Min(first, last), Math.Max(first, last));
        }
        double value = measure(scenario);
        return (value, value);
    }

    private static string FormatRange((double Low, double High) range, Func<double, string> format)
    {
        string low = format(range.Low);
        string high = format(range.High);
        return low == high ? low : $"{low}–{high}";
    }

    private static IEnumerable<Scenario> ReferenceScenarios(IReadOnlyList<StockMethod> methods, StockProfile profile,
        StockAxis axis, string item, PositionReader position)
    {
        // Prefer the adjustable draw over fixed or locked stages. Summary and inline use the same conditions.
        Scenario? first = null;
        bool hasRandom = false;
        foreach (Scenario scenario in ApplicableScenarios(methods.OrderBy(method => method.SupplyVariant == "large"),
            profile, axis, item, position, compact: true))
        {
            first ??= scenario;
            if (!scenario.Terms.Any(term => term.Draw.Chance is > 0 and < 1
                || (term.Draw.Chance == null && term.Draw.Maximum > term.Draw.Minimum)))
                continue;

            hasRandom = true;
            yield return scenario;
        }
        if (!hasRandom && first != null)
            yield return first;
    }

    private static string AllConditions => Text("满足以下全部条件", "All of the following");
    private static string AnyCondition => Text("满足以下任意一个条件", "Any of the following");

    private static string FirstRollVanilla => Text("首份按原版概率", "First unit uses the vanilla probability");
    private static string RegularSurplus => Text("一般过剩", "Regular surplus");
    private static string SevereSurplus => Text("严重过剩", "Severe surplus");
    private static string ReputationOffsetNote(string faction, (int Base, int? Floor, int Divisor) odds)
    {
        string note = Line("基础概率为 <b>{0}</b>。{1}每 {2} 点加减 1 个百分点，向零取整。",
            "The base chance is <b>{0}</b>. Each {2} points of {1} adds or subtracts one percentage point, rounded toward zero.",
            Percent(odds.Base / 100d), FactionReputation(faction), odds.Divisor);
        return odds.Floor is int floor ? note + Line("最低调整为 <b>{0}</b> 个百分点。",
            " The adjustment is capped at <b>{0}</b> percentage points on the low end.", floor) : note;
    }

    private static string NeonFaction(string method)
        => method.StartsWith("AddScav", StringComparison.Ordinal) ? GameTerm("ClientName/faction_name_lower_level") : BlackMarket;

    private static string ReputationRange(string faction, int from, int? next)
    {
        if (from == int.MinValue)
            return next is int first ? ReputationBelow(faction, first) : FactionReputation(faction);
        return next is int stop ? Line("{0} {1}–{2}", "{0} {1}–{2}", FactionReputation(faction), from, stop - 1)
            : ReputationAtLeast(faction, from);
    }

    private static string ConditionText(Scenario scenario)
    {
        return string.Join("\n", new[]
        {
            ConditionGroup(scenario.RequiredConditions, AllConditions),
            ConditionGroup(scenario.AlternativeConditions, AnyCondition)
        }.Where(group => group.Length != 0));
    }

    private static string ConditionGroup(string[] conditions, string title) => conditions.Length switch
    {
        0 => "",
        1 => $"<b>{conditions[0]}</b>",
        _ => $"<b>{title}</b>\n" + string.Join("\n", conditions.Select(condition => "• " + condition.Replace("\n", Text("\n　", "\n  "))))
    };

    internal static string ReputationNote(StockMethod method, StockProfile profile, StockAxis axis, string item)
    {
        if (method.MethodName == nameof(StoreClientList.CreateThief) && item == "crate_random")
            return ThiefCrateGuarantee();
        if (UsesBlackMarketTiers(method.MethodName))
            return BlackMarketMultiplierNote();
        if (method.MethodName == nameof(StoreClientList.CreateSecSurplusMerchant) && item is "stun_gun" or "tazer" or "pink_injector")
            return Line("{0}达到 <b>{1}</b> 后，该批固定带 1 份。",
                "At {0} <b>{1}</b> or higher, this roll guarantees one unit.", FactionReputation(Security), SecurityStockThreshold);
        if (method.MethodName == nameof(StoreClientList.PlaceInventorInventory) && item == "black_lamp")
            return ReputationOffsetNote(BlackMarket, BlackLampOdds);
        return string.Join("\n", Sites(method, profile, axis, item).Select(site => NeonOdds(method.MethodName, site))
            .Where(odds => odds.HasValue).Select(odds => odds!.Value).Distinct()
            .Select(odds => ReputationOffsetNote(NeonFaction(method.MethodName), odds)));
    }

    internal static string ThiefCrateGuarantee()
    {
        var perk = StockReputationRules.ThiefCratePerk();
        string crate = GameTerm("Mechanic/type_supply_crate");
        if (perk.isAlwaysActive)
            return Line("原版固定带 1 个{0}。", "Vanilla always brings one {0}.", crate);
        return perk.perkThreshold >= 0
            ? Line("{0}达到 <b>{1}</b> 后，固定带 1 个{2}。", "At {0} <b>{1}</b> or higher, one {2} is guaranteed.",
                FactionReputation(BlackMarket), perk.perkThreshold, crate)
            : Line("{0}为 <b>{1}</b> 及以下时，固定带 1 个{2}。", "At {0} <b>{1}</b> or lower, one {2} is guaranteed.",
                FactionReputation(BlackMarket), perk.perkThreshold, crate);
    }

    internal static string BlackMarketMultiplierNote()
    {
        string reputation = FactionReputation(BlackMarket);
        if (!StockReputationRules.HasNativeTiers)
            return Line("基础概率按{0}乘以对应倍率。", "The base chance is multiplied according to {0}.", reputation);
        var tiers = StockReputationRules.BlackMarketTiers;
        if (tiers.Length == 1)
            return Line("基础概率的倍率固定为 <b>{0}</b>。", "The base chance has a fixed multiplier of <b>{0}</b>.", Multiplier(tiers[0].Multiplier));
        return Line("基础概率按{0}乘以对应倍率。低于 <b>{1}</b> 时为 <b>{2}</b>。达到 <b>{3}</b> 时，倍率依次为 <b>{4}</b>。",
            "The base chance uses a multiplier based on {0}. Below <b>{1}</b>, it is <b>{2}</b>. At <b>{3}</b>, the multipliers are <b>{4}</b> respectively.",
            reputation, tiers[1].Minimum, Multiplier(tiers[0].Multiplier), JoinList(tiers.Skip(1).Select(tier => tier.Minimum)),
            JoinList(tiers.Skip(1).Select(tier => Multiplier(tier.Multiplier))));

        static string Multiplier(double value) => "×" + value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static IEnumerable<Scenario> ApplicableScenarios(IEnumerable<StockMethod> methods, StockProfile profile,
        StockAxis axis, string item, PositionReader position, bool compact = false)
    {
        foreach (StockMethod method in methods)
        {
            int[] sites = Sites(method, profile, axis, item);
            if (sites.Length == 0)
                continue;

            string[] requirements = SupplyRequirements(method.MethodName, item, sites[0]);
            bool hasCases = false;
            foreach (Scenario scenario in Scenarios(method, profile, axis, item, sites, position, compact))
            {
                hasCases = true;
                yield return requirements.Length == 0 ? scenario : scenario.WithRequirements(requirements);
            }
            if (hasCases && requirements.Length != 0 && !compact)
                yield return new Scenario(Text("供货条件未满足", "Stock conditions unmet"), 0);
        }
    }

    internal static (string Original, string Modified) ExpectedCounts(StockMethod method, StockProfile profile, string item,
        PositionReader position, bool linear)
    {
        int[] sites = Sites(method, profile, StockAxis.Quantity, item);
        var scenarios = Scenarios(method, profile, StockAxis.Quantity, item, sites, position).ToArray();
        return (Expectations(false), Expectations(true));

        string Expectations(bool modified) => JoinList(scenarios.Select(scenario => Number(Expected(scenario, modified, linear))));
    }

    private static int[] Sites(StockMethod method, StockProfile profile, StockAxis axis, string item)
        => Enumerable.Range(0, method.Draws.Length)
            .Where(site => method.AxisFor(profile, site) == axis && method.Categories[site].Contains(item)).ToArray();

    private static double Expected(int min, double[] probabilities)
        => probabilities.Select((probability, index) => (min + index) * probability).Sum();

    private static double Expected(Scenario scenario, bool modified, bool linear)
    {
        var (min, mass) = scenario.Distribution(modified, linear);
        return Expected(min, mass);
    }

    private static double ChanceAtLeast(Scenario scenario, int count, bool modified, bool linear)
    {
        var (min, mass) = scenario.Distribution(modified, linear);
        return mass.Skip(Math.Max(0, count - min)).Sum();
    }

    private static string Change(double original, double modified) => $"{Percent(original)} → {Percent(modified)}";

    internal static string ShortProbabilityChange(double original, double modified)
        => $"{ShortPercent(original)} → {ShortPercent(modified)}";

    private static string ShortPercent(double value) => Percent(value, compact: true);

    internal static string Percent(double value, bool compact = false)
    {
        value = Math.Max(0, Math.Min(1, value));
        if (value > 1e-14 && value < 0.00005)
            return compact ? Text("＜0.01%", "<0.01%") : Text("低于 0.01%", "below 0.01%");
        if (value < 1 - 1e-14 && value > 0.99995)
            return compact ? Text("＞99.99%", ">99.99%") : Text("高于 99.99%", "above 99.99%");
        return Number(value * 100) + "%";
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static IEnumerable<Scenario> Scenarios(StockMethod method, StockProfile profile, StockAxis axis,
        string item, int[] sites, PositionReader position, bool compact = false)
    {
        string name = method.MethodName;
        int first = sites[0];
        Term Draw(int site, StockBaseDraw draw, int repeats = 1, StockAxis? axisOverride = null, string? specific = null)
            => new Term(draw, position(method, site, axisOverride, specific), repeats);
        int batchSize = axis == StockAxis.Appearance ? StockProductDetails.AppearanceBatchSize(name, item) : 1;
        Term Roll(int site, double percent, int? count = null, int repeats = 1)
            => Draw(site, StockBaseDraw.Roll(percent, count ?? batchSize), repeats);
        Term Native(int site)
        {
            var draw = method.BaseDraws[site]!.Value;
            return batchSize != 1 && draw.Chance is double chance ? Roll(site, chance * 100) : Draw(site, draw);
        }
        Term[] NativeSites(IEnumerable<int> selected) => selected.Select(Native).ToArray();

        if (name == nameof(StoreClientList.CreateMiner))
        {
            var draws = MinerUnlocks.Select((unlock, index) => Roll(index + 1,
                method.BaseDraws[index + 1]!.Value.Chance!.Value * 100, count: unlock.Units)).Prepend(Native(0)).ToArray();
            return DayStages(MinerUnlocks.Select(unlock => unlock.Day).ToArray(),
                stage => new Scenario("", 0, draws.Take(stage + 1).ToArray()));
        }

        if (name == nameof(StoreClientList.CreateThief) && item == "crate_random")
        {
            var perk = StockReputationRules.ThiefCratePerk();
            if (perk.isAlwaysActive)
                return new[] { new Scenario("", 1) };
            int threshold = perk.perkThreshold;
            int boundary = threshold < 0 ? threshold + 1 : threshold;
            var tiers = StockReputationRules.BlackMarketTiers;
            var starts = tiers.Select(tier => tier.Minimum).Where(rep => !Unlocked(rep))
                .Append(int.MinValue).Append(boundary).Distinct().OrderBy(rep => rep).ToArray();
            Scenario[] stages = starts.Select((rep, index) =>
            {
                string label = ReputationRange(BlackMarket, rep, index + 1 < starts.Length ? starts[index + 1] : (int?)null);
                if (Unlocked(rep))
                    return new Scenario(label, 1);
                double multiplier = tiers.Last(tier => tier.Minimum <= rep).Multiplier;
                return new Scenario(label, 0, Roll(first, method.BaseDraws[first]!.Value.Chance!.Value * 100 * multiplier));
            }).ToArray();
            if (!compact)
                return stages;
            var low = stages.Where(stage => stage.Terms.Length != 0).OrderBy(stage => stage.Terms[0].Draw.Chance).First();
            return new[] { new Scenario("", 0, low.Terms).WithVariation(low, new Scenario("", 1)) };

            bool Unlocked(int reputation) => threshold < 0 ? reputation <= threshold : reputation >= threshold;
        }

        if (UsesBlackMarketTiers(name))
        {
            var tiers = StockReputationRules.BlackMarketTiers;
            Scenario Tier(string label, double multiplier) => new Scenario(label, StockProductDetails.FixedQuantity(name, item),
                sites.Select(site => Roll(site, method.BaseDraws[site]!.Value.Chance!.Value * 100 * multiplier)).ToArray());
            if (!compact)
                return tiers.Select((tier, index) => Tier(ReputationRange(BlackMarket, tier.Minimum,
                    index + 1 < tiers.Length ? tiers[index + 1].Minimum : (int?)null), tier.Multiplier));
            var baseline = Tier("", tiers[0].Multiplier);
            return new[] { baseline.WithVariation(Tier("", tiers.Min(tier => tier.Multiplier)), Tier("", tiers.Max(tier => tier.Multiplier))) };
        }

        if (name == nameof(StoreClientListSpec.CreateRetiredAquafarmer))
        {
            string upgrade = GameTerm("NetworkUpgrade/network_AQUACULTURE_II_title");
            return new[] { new Scenario(Line("{0} 解锁前", "Before {0} unlocks", upgrade), 1),
                new Scenario(Line("{0} 解锁后", "After {0} unlocks", upgrade), 0, Native(first)) };
        }

        if (name == nameof(StoreClientList.PlaceSupplierInventory))
        {
            int offset = item == "cassette" ? 20 : 0;
            return Inputs(0, offset + 100,
                day => new Scenario("", 0, Roll(first, day - offset)), reference: compact ? offset + 1 : null);
        }

        if (name == nameof(StoreClientListWanted.CreateWanted1Return) && axis == StockAxis.Appearance)
            return Inputs(0, 100 - WantedCrateBasePercent,
                day => new Scenario("", 0, Roll(first, WantedCrateBasePercent + day)), reference: compact ? 1 : null);

        if (name == nameof(StoreClientListWanted.CreateWanted7))
            return Inputs(0, 38, day => new Scenario("", 1,
                sites.Select(site => Roll(site, 25 + 2 * day)).ToArray()), reference: compact ? 1 : null);

        if (name == "RollButcherCount")
        {
            (int min, int max) = item == "raw_meat" ? (0, 3) : item == "small_raw_meat" ? (1, 3) : (0, 2);
            return Inputs(0, 40, day => new Scenario("", min,
                Draw(first, StockBaseDraw.Roll((day * 5 + 1) / 2), max - min, specific: item)), reference: compact ? 1 : null);
        }

        if (name == nameof(StoreClientList.CreateStampSeller))
            return DayStages(new[] { 20, 50 }, stage => new Scenario("", 1, NativeSites(sites.Take(stage + 1))));

        if (name == nameof(StoreClientListEvent.CreateIceMiner))
            return DayStages(new[] { 15, 45 }, stage => new Scenario("", stage, Native(first)));

        if (name == nameof(StoreClientList.CreateWaterSeller) && item == "large_bottled_water")
            return DayStages(new[] { 20, 50 }, stage => new Scenario("", 1 + stage, NativeSites(sites)));

        if (name == nameof(StoreClientList.CreateWaterSeller) && item == "water_ration_small")
            return axis == StockAxis.Appearance ? new[] { new Scenario("", 0, Native(first)) }
                : new[] { new Scenario("", 0, new[] { new Term(StockBaseDraw.Roll(33), 0) }.Concat(NativeSites(sites)).ToArray())
                    { Scope = FirstRollVanilla } };

        if (name == nameof(StoreClientList.CreateShadyPharmacist))
            return DayStages(new[] { 20, 40 }, stage => new Scenario("", 2 + stage, Native(first)));

        if (name == nameof(StoreClientList.CreateScavCrate))
        {
            string[] priceGroups = { Text("商品原价超过 16", "Base price above 16"),
                Text("商品原价 11–16", "Base price 11–16"), Text("商品原价 10 及以下", "Base price 10 or below") };
            return DayStages(new[] { 20 }, stage => new Scenario("", 0, Draw(first, new StockBaseDraw(5 + stage, 6 + stage))))
                .SelectMany(scenario => priceGroups.Select((label, fixedCount) =>
                    new Scenario(scenario.RequiredConditions.Append(label).ToArray(), fixedCount, scenario.Terms)));
        }

        if (name == nameof(StoreClientList.CreateFarmerClientStandard))
        {
            string locked = Text("酿酒供货解锁前", "Before winemaking stock unlocks");
            string unlocked = Text("酿酒供货解锁后", "After winemaking stock unlocks");
            var cases = sites.Select(site => new Scenario(site == 2 ? locked : unlocked, 0, Native(site)));
            return item == "wine_berry" ? cases.Prepend(new Scenario(locked, 0)) : cases;
        }

        if (name == nameof(StoreClientListSurplus.CreateFoodSurplusClient))
            return new[] { new Scenario(sites.Length == 1 ? RegularSurplus : SevereSurplus,
                0, sites.Select(site => Draw(site, method.BaseDraws[site]!.Value, 5)).ToArray())
                { Scope = Text("随机批次", "Random batches") } };

        if (name == nameof(StoreClientList.CreateSecSurplusMerchant))
        {
            string permitI = WeaponPermit(1);
            string permitII = WeaponPermit(2);
            string licenseI = Line("持有 {0}", "Owns a {0}", permitI);
            string licenseII = Line("持有 {0}", "Owns a {0}", permitII);
            string onlyI = Line("仅持有 {0}", "Owns only a {0}", permitI);
            string onlyII = Line("仅持有 {0}", "Owns only a {0}", permitII);
            string neither = Line("{0}和 {1}均未持有", "Owns neither a {0} nor a {1}", permitI, permitII);
            if (item == "heavy_pistol_ammo")
            {
                int i = StockRules.RequisitionAmmoSite;
                if (axis == StockAxis.Appearance)
                {
                    if (compact)
                        return new[] { new Scenario("", 0, Native(i))
                            { AlternativeConditions = new[] { licenseI, licenseII } } };
                    return new[] { new Scenario(onlyI, 0, Native(i)),
                        new Scenario(licenseII, 0, Native(14)), new Scenario(neither, 0) };
                }
                return new[]
                {
                    new Scenario(onlyI, 0, new Term(StockBaseDraw.Roll(50), 0), Native(26)) { Scope = FirstRollVanilla },
                    new Scenario(onlyII, 0, new Term(StockBaseDraw.Roll(50), 0), Native(15)) { Scope = FirstRollVanilla },
                    new Scenario(new[] { licenseI, licenseII }, 0, new Term(StockBaseDraw.Roll(50), 0),
                        Native(15), Draw(i, method.BaseDraws[i]!.Value, axisOverride: StockAxis.Quantity), Native(26))
                        { Scope = FirstRollVanilla },
                    new Scenario(neither, 0)
                };
            }
            string noPermit = Text("未持有武器许可证", "No weapon permit owned");
            if (item == "heavy_pistol_ammo_nl")
            {
                var permitted = axis == StockAxis.Appearance ? new Scenario(licenseI, 0, Native(first))
                    : new Scenario(licenseI, 0, new Term(StockBaseDraw.Roll(50), 0), Native(first)) { Scope = FirstRollVanilla };
                string higherPermits = Line("只持有 {0}或 {1}", "Owns a {0} or {1} without a Class I permit", permitII, WeaponPermit(3));
                return new[] { permitted, new Scenario(noPermit, 3), new Scenario(higherPermits, 0) };
            }
            if (item is "stun_gun" or "tazer" or "pink_injector")
            {
                string belowThreshold = ReputationBelow(Security, SecurityStockThreshold);
                string atThreshold = ReputationAtLeast(Security, SecurityStockThreshold);
                string anyPermit = Text("持有武器许可证", "Owns a weapon permit");
                if (compact)
                {
                    var baseline = new Scenario(item == "pink_injector" ? anyPermit : "", 0, Native(first));
                    return new[] { baseline.WithVariation(baseline, new Scenario("", 1)) };
                }
                if (item != "pink_injector")
                    return new[] { new Scenario(belowThreshold, 0, Native(first)), new Scenario(atThreshold, 1) };
                return new[]
                {
                    new Scenario(new[] { anyPermit, belowThreshold }, 0, Native(first)),
                    new Scenario(new[] { anyPermit, atThreshold }, 1),
                    new Scenario(new[] { noPermit, belowThreshold }, 1, Native(first)),
                    new Scenario(new[] { noPermit, atThreshold }, 2)
                };
            }
        }

        if (name == nameof(StoreClientListSpec.CreateRetiredGunsmith) && first is >= 11 and <= 18)
        {
            double baseChance = first switch { 11 => 33, 14 or 16 => 20, 15 or 17 => 15, _ => 25 };
            int perPermit = first <= 13 ? 15 : 10;
            string permitII = WeaponPermit(2);
            string permitIII = WeaponPermit(3);
            if (compact)
            {
                var baseline = new Scenario("", 0, Roll(first, baseChance))
                {
                    Scope = Line("每持有一种 {0}或 {1}，出货概率增加 {2} 个百分点",
                        "Each owned permit adds {2} percentage points to the stock chance · {0}, {1}", permitII, permitIII, perPermit)
                };
                return new[] { baseline.WithVariation(baseline, new Scenario("", 0, Roll(first, baseChance + 2 * perPermit))) };
            }
            return Enumerable.Range(0, 3).Select(count =>
                new Scenario(Line("{0}和 {1}中持有 {2} 种", "Eligible permits owned · {2} of {0} and {1}",
                    permitII, permitIII, count), 0, Roll(first, baseChance + count * perPermit)));
        }

        if (name == nameof(StoreClientListSpec.CreateRetiredGunsmith) && first <= 5)
        {
            var early = new Scenario(BeforeDay(20), 0, NativeSites(sites.Skip(1)));
            var late = new Scenario(FromDay(20), 0, Native(first));
            return compact ? new[] { new Scenario("", 0, Native(first))
                { Scope = Text("第 20 天起，原版提高出货概率", "Vanilla stock chance increases from day 20") }
                .WithVariation(early, late) } : new[] { early, late };
        }

        if (name == nameof(StoreClientList.AddScavGeneralStock) && (first is >= 5 and <= 10 or >= 52 and <= 58))
        {
            int startDay = first <= 10 ? 7 : 10;
            return new[] { new Scenario(BeforeDay(startDay), 0),
                new Scenario(FromDay(startDay), 0, NativeSites(sites)) };
        }

        if (name == nameof(StoreClientList.PlaceInventorInventory))
        {
            int investment = Array.IndexOf(method.Profiles, profile);
            string owned = Line("已持有{0}", "You already own {0}", StockCatalog.Name(item));
            string notOwned = Line("尚未持有{0}", "You do not yet own {0}", StockCatalog.Name(item));
            string stockBundle = Text("套装", "Bundle");
            if (item == "mirage_projector")
                return Inputs(compact ? 10 : 0, 100,
                    day => new Scenario(Text("尚未持有该物品", "The item is not yet owned"), 0, Roll(first, day < 10 ? 0 : day)),
                    reference: compact ? 10 : null);
            if (item == "black_lamp")
                return Inputs(-200, 200, rep => new Scenario("", 0, Roll(first, ReputationAdjustedChance(BlackLampOdds, rep))),
                    reference: compact ? 0 : null, reputationFaction: BlackMarket);
            if (item == "storage_bay_expansion_kit")
                return new[] { new Scenario(Text("当前投资阶段", "Current investment stage"), 0, Roll(first, new[] { 15, 45, 75 }[investment])) };
            if (item == "storage_coordinator")
                return new[]
                {
                    new Scenario("", 0) { AlternativeConditions = new[] { BeforeDay(15), Text("已持有该物品", "The item is already owned") } },
                    new Scenario(new[] { FromDay(15), Text("尚未持有该物品", "The item is not yet owned") }, 0, Native(first))
                };
            if (axis == StockAxis.Quantity && item == "module_bay_expansion_kit")
                return DayStages(new[] { 20, 40 }, stage => new Scenario("", 0,
                    NativeSites(sites.Where(site => (site != 42 || stage >= 1) && (site != 43 || stage >= 2)))
                        .Prepend(new Term(StockBaseDraw.Roll(20), 0)).ToArray()) { Scope = FirstRollVanilla });
            if (axis == StockAxis.Quantity && item is "chem_module" or "water_tank")
                return new[] { new Scenario("", 0,
                    NativeSites(sites).Prepend(new Term(StockBaseDraw.Roll(50), 0)).ToArray()) { Scope = FirstRollVanilla } };
            if (axis == StockAxis.Appearance && item is "storage_bay" or "machine_bay" or "furnace" or "fridge"
                or "water_purifier" or "moisture_farm")
                return new[] { new Scenario(notOwned, 1),
                    new Scenario(Text("随机抽选时，必须已经持有该物品", "The random roll requires already owning this item"),
                        0, Native(first)) };
            if (axis == StockAxis.Appearance && item is "recharger" or "smuggler_bay" or "hydroponic" or "ras" or "livestock_feed_dispenser")
                return new[] { new Scenario(owned, 0, Native(first)), new Scenario(notOwned, 0, Roll(sites[1], 66)) };
            if (item == "uv_filter")
                return new[] { new Scenario(owned, 0, Native(first)), new Scenario(notOwned, 0, Native(sites[1])) };
            if (item == "desequencer")
                return new[] { new Scenario(owned, 0, Native(first)),
                    new Scenario(notOwned, 0, Roll(sites[1], 66)) { Scope = stockBundle } };
            if (item == "blank_keycard")
                return new[] { new Scenario(Line("已持有{0}时", "Already owns the {0}", StockCatalog.Name("desequencer")), 2),
                    new Scenario(Line("尚未持有{0}时", "Does not yet own the {0}", StockCatalog.Name("desequencer")), 0, Roll(first, 66))
                        { Scope = stockBundle } };
            if (item.StartsWith("crypto_module_", StringComparison.Ordinal) && sites.Length == 2)
            {
                string decoder = StockCatalog.Name("desequencer");
                string standalone = Line("单独抽选时，必须已经持有{0}", "An individual roll requires owning the {0}", decoder);
                string[] bundleConditions = { Line("尚未持有{0}", "Does not yet own the {0}", decoder),
                    Line("抽中{0}套装", "The {0} bundle is rolled", decoder) };
                if (compact)
                    return new[]
                    {
                        new Scenario("", 0, Native(first))
                        {
                            AlternativeConditions = new[] { standalone,
                                Text("随套装抽选时\n", "When rolled with the bundle\n") + ConditionGroup(bundleConditions, AllConditions) },
                            Scope = Text("单独抽选", "Individual roll")
                        }
                    };
                return new[] { new Scenario(standalone, 0, Native(first)),
                    new Scenario(bundleConditions, 0, Native(sites[1])) { Scope = Text("随套装抽选", "Rolled with the bundle") } };
            }
            if (item == "security_alarm")
                return new[] { new Scenario(Text("仅在尚未持有时抽选", "Rolls only before the item is owned"),
                    0, Roll(first, 66)), new Scenario(owned, 0) };
        }

        if (sites.Any(site => NeonOdds(name, site) != null))
            return Inputs(-200, 200,
                rep => new Scenario("", StockProductDetails.FixedQuantity(name, item), sites.Select(site =>
                {
                    if (NeonOdds(name, site) is { } odds)
                        return Roll(site, ReputationAdjustedChance(odds, rep));
                    return Native(site);
                }).ToArray()), reference: compact ? 0 : null, reputationFaction: NeonFaction(name));

        if (sites.Any(site => method.BaseDraws[site] == null))
            return Array.Empty<Scenario>();

        string label = name == nameof(StoreClientListWater.SetHonestWaterSellerDialog)
            ? Text("抽中对应水质货单", "The corresponding water-quality stock set is rolled")
            : method.SupplyVariant == "large" ? SevereSurplus : method.SupplyVariant == "regular" ? RegularSurplus : "";
        int fixedCount = StockProductDetails.FixedQuantity(name, item);
        if (name == nameof(StoreClientList.PlaceInventorInventory) && axis == StockAxis.Quantity)
        {
            int investment = Array.IndexOf(method.Profiles, profile);
            fixedCount = item == "energy_credit" ? 2 : item == "plant_dna_disk" ? 1 + investment
                : item.StartsWith("random_", StringComparison.Ordinal) ? investment : 0;
        }

        bool isBonus = axis == StockAxis.Quantity && Enumerable.Range(0, method.Draws.Length)
            .Any(site => method.AxisFor(profile, site) == StockAxis.Appearance && method.Categories[site].Contains(item));
        return new[] { new Scenario(label, fixedCount, NativeSites(sites))
            { Scope = isBonus ? Text("库存增量抽选", "Additional stock rolls") : "" } };
    }

    private static bool UsesBlackMarketTiers(string method)
        => method is nameof(StoreClientList.CreateThief) or nameof(StoreClientList.CreatePettyThief) or nameof(StoreClientList.CreateFoodThief);

    private static int ReputationAdjustedChance((int Base, int? Floor, int Divisor) odds, int reputation)
        => odds.Base + Math.Max(odds.Floor ?? int.MinValue, reputation / odds.Divisor);

    private static (int Base, int? Floor, int Divisor)? NeonOdds(string method, int site)
    {
        int roll = site + 1;
        return method switch
        {
            nameof(StoreClientListSupplier.AddScavFoodStock) => Normal(roll == 16),
            nameof(StoreClientListSupplier.AddScavHouseholdStock) => Normal(roll == 7),
            nameof(StoreClientListSupplier.AddScavMaterialStock) when roll is >= 4 and <= 8 or >= 10 and <= 13
                => Normal(roll == 12),
            nameof(StoreClientListSupplier.AddScavMedStock) => Normal(roll is 2 or 3 or 10 or 11 or 13),
            nameof(StoreClientListSupplier.AddThiefFoodStock) => Normal(roll is 7 or 9),
            nameof(StoreClientListSupplier.AddThiefHouseholdStock) => Normal(roll is 6 or 7),
            nameof(StoreClientListSupplier.AddThiefMaterialStock) => (50, -25, 2),
            nameof(StoreClientListSupplier.AddThiefMedStock) => roll >= 9 ? (10, -5, 5) : (50, -25, 2),
            _ => null
        };

        static (int, int?, int) Normal(bool rare) => rare ? (10, -5, 5) : (25, -15, 2);
    }

    private static string[] SupplyRequirements(string method, string item, int site)
    {
        if (method == nameof(StoreClientList.PlaceInventorInventory))
            return site switch
            {
                >= 47 and <= 55 => new[] { Text("须先解锁供水商供货", "Requires unlocked water supplier stock") },
                >= 56 and <= 64 => new[] { Line("须先解锁{0}供货", "Requires stock from the {0}",
                    GameTerm("NetworkUpgrade/network_RETIRED_FARMER_title")) },
                >= 65 and <= 68 => new[] { Line("须先解锁{0}供货", "Requires stock from the {0}",
                    GameTerm("NetworkUpgrade/network_RETIRED_CHEMIST_title")) },
                >= 69 and <= 75 => new[] { Line("须先解锁{0}相关供货", "Requires {0} stock",
                    GameTerm("NetworkUpgrade/network_PLANT_DNA_title")) },
                76 or 77 => new[] { Text("须先解锁酿酒供货", "Requires unlocked winemaking stock") },
                >= 78 and <= 81 => new[] { Line("须先解锁{0}", "Requires {0}", GameTerm("NetworkUpgrade/network_AQUACULTURE_title")) },
                83 => new[] { Text("须先开放改造体鉴定", "Requires unlocked augmentation appraisal") },
                _ => Array.Empty<string>()
            };
        if (method == nameof(StoreClientList.CreateSecSurplusMerchant) && item is not ("heavy_pistol_ammo" or "heavy_pistol_ammo_nl"))
            return site switch
            {
                < 12 => new[] { Line("须持有 {0}", "Requires a {0}", WeaponPermit(3)) },
                < 25 => new[] { Line("须持有 {0}", "Requires a {0}", WeaponPermit(2)) },
                < 33 => new[] { Line("须持有 {0}", "Requires a {0}", WeaponPermit(1)) },
                _ => Array.Empty<string>()
            };
        if (method == nameof(StoreClientList.AddScavGeneralStock))
        {
            if (item is "kotton_seed" or "bloomberry_seed" or "nutrifruit_seed_packet")
                return item switch
                {
                    "bloomberry_seed" => new[] { Text("已解锁耕作供货", "Farming stock unlocked"),
                        Text("已解锁酿酒供货", "Winemaking stock unlocked") },
                    "nutrifruit_seed_packet" => new[] { Line("须先抽中{0}批次", "Requires the {0} batch to be rolled first",
                        StockCatalog.Name("kotton_seed")) },
                    _ => new[] { Text("须先解锁耕作供货", "Requires unlocked farming stock") }
                };
        }
        if (item is "wine_yeast" or "wine_superyeast" or "wine_yeast_red" or "wine_bottle"
            && method is nameof(StoreClientList.AddScavGeneralStock) or nameof(StoreClientList.CreateJunker)
                or nameof(StoreClientList.CreatePettyThief) or nameof(StoreClientListSupplier.AddScavHouseholdStock)
                or nameof(StoreClientListSupplier.AddThiefHouseholdStock))
            return new[] { Text("须先解锁酿酒供货", "Requires unlocked winemaking stock") };
        if (item == "random_inspectable_injector" && method is nameof(StoreClientList.AddScavGeneralStock)
            or nameof(StoreClientListSupplier.AddScavMedStock))
            return new[] { Text("须先开放注射器鉴定", "Requires unlocked injector inspection") };
        if (method == nameof(StoreClientList.CreateWaterSeller))
            return new[] { item == "water_ration_small" ? Text("仅在供水商供货解锁前抽选", "Rolls only before water supplier stock unlocks")
                : Text("须先解锁供水商供货", "Requires unlocked water supplier stock") };
        if (method == nameof(StoreClientList.CreateShadyPharmacist))
            return new[] { Line("抽中{0}货单", "The {0} stock set is rolled", StockCatalog.Name("pink_injector")) };
        if (method is nameof(StoreClientList.CreateRareLowerLevelChemist) or nameof(StoreClientListBarter.CreateScavBarter))
            return new[] { Text("须先抽中对应货单", "Requires the corresponding stock set to be rolled") };
        return Array.Empty<string>();
    }

    private static IEnumerable<Scenario> DayStages(int[] starts, Func<int, Scenario> create)
    {
        for (int index = 0; index <= starts.Length; index++)
        {
            string label = index == 0 ? BeforeDay(starts[0]) : index == starts.Length
                ? FromDay(starts[index - 1]) : DayRange(starts[index - 1], starts[index] - 1);
            yield return create(index).WithRequirements(label);
        }
    }

    private static IEnumerable<Scenario> Inputs(int minimum, int maximum, Func<int, Scenario> create,
        int? reference = null, string? reputationFaction = null)
    {
        if (reference is int value)
        {
            // These callers use a monotone native roll, so endpoints also bound the adjusted result.
            yield return create(value).WithVariation(create(minimum), create(maximum));
            yield break;
        }
        int start = minimum;
        Scenario current = create(start);
        for (int input = minimum + 1; input <= maximum; input++)
        {
            Scenario next = create(input);
            if (current.Fixed == next.Fixed && current.Terms.SequenceEqual(next.Terms))
                continue;

            yield return Labeled(current, start, input - 1);
            current = next;
            start = input;
        }
        yield return Labeled(current, start, maximum, onward: true);

        Scenario Labeled(Scenario scenario, int first, int last, bool onward = false)
        {
            if (reputationFaction == null)
                return scenario.WithRequirements(onward ? FromDay(first) : first == last ? Day(first) : DayRange(first, last));

            string amount = first == last ? first.ToString(CultureInfo.InvariantCulture)
                : $"{first}{(first < 0 ? Text(" 至 ", " to ") : "–")}{last}";
            return scenario.WithRequirements(Line("{0} {1}", "{0} {1}",
                FactionReputation(reputationFaction), amount.Replace('-', '−')));
        }
    }
}
