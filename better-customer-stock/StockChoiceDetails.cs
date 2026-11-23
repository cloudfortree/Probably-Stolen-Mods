using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static BetterCustomerStock.StockLanguage;

namespace BetterCustomerStock;

internal static class StockChoiceDetails
{
    // Reference weights from the native tables, in StockRules order.
    private static readonly double[] PackedFood =
        { 50.0000076, 81.8181839, 81.8181839, 81.8181763, 81.8181763, 81.8181839, 50, 81.8181763,
            81.8181839, 81.8181839, 81.8181763, 81.8181839, 81.8181763 };
    private static readonly double[] Medical = { 150, 150, 100, 150, 100, 149.999969, 50, 150 };
    private static readonly double[] Junk = { 29.9999981, 30.0000019, 850, 29.9999981, 29.9999981, 29.9999981 };
    private static readonly string[] AppraisalRanks =
    {
        "Mechanic/mech_appraisal_rank_junior", "Mechanic/mech_appraisal_rank_appraiser",
        "Mechanic/mech_appraisal_rank_senior", "Mechanic/mech_appraisal_rank_master"
    };

    private sealed class Pool
    {
        internal readonly string[] Ids;
        internal readonly double[] Weights;
        internal readonly string Caption;
        internal readonly bool ShowReferenceInSummary;

        internal Pool(string[] ids, double[] weights, string caption = "", bool showReferenceInSummary = false)
        {
            Ids = ids;
            Weights = weights;
            Caption = caption;
            ShowReferenceInSummary = showReferenceInSummary;
        }
    }

    internal static string Summary(StockProfile profile, StockChoice choice) => choice.Tooltip?.Invoke() ?? (profile.Id, choice.Id) switch
    {
        ("thief", "crate") => StockProductPreview.ThiefCrateGuarantee() + " " + CrateChoiceNote(),
        ("wanted1_return", "crate") => Line("携带{0}的基础概率为 {1}，每天增加 1 个百分点。{2}固定携带一种。",
            "The base chance of carrying a {0} is {1}, plus one percentage point per day. One is guaranteed from day {3}.",
            GameTerm("Mechanic/type_supply_crate"), StockProductPreview.Percent(StockProductPreview.WantedCrateBasePercent / 100d),
            FromDay(100 - StockProductPreview.WantedCrateBasePercent), 100 - StockProductPreview.WantedCrateBasePercent)
            + " " + CrateChoiceNote(),
        ("pettyThief", _) => PettyThiefNote(profile, choice),
        ("addictGeneralBarter", "packedFoodTable") => Line("抽中{0}报价后，从这张货表独立抽选 {1} 次。每次按权重选出一项，同一种商品可以重复抽中。",
            "When the {0} offer is rolled, this table supplies {1} independent draws. Each draw picks one item by weight, and the same item can appear more than once.",
            GameTerm("Mechanic/type_processed_food"),
            choice.Quota!.Total),
        ("shadyPharma", "shipment") => Line("随机注射器解锁后才参与抽选。解锁前固定使用{0}货单。",
            "The random injector is eligible once unlocked. Before then, the customer always uses the {0} stock set.", ItemName("pink_injector")),
        ("lowerLevelChemist", "shipment") => Line("可携带违禁品时按权重抽选。限售违禁品时固定使用{0}货单。",
            "The weights apply when contraband is allowed. When it is restricted, the customer always uses the {0} stock set.", ItemName("salve")),
        ("lowerLevelChemistBarter", "shipment") => Line("限售违禁品时，{0}货单从候选中移除。",
            "The {0} stock set is excluded when contraband is restricted.", ItemName("dream_dust")),
        (_, "inspectable") => Text("原版只从已开放的鉴定类别中抽选。同次来访的追加货物沿用抽中的类别。",
            "Vanilla rolls among unlocked inspection categories. Any extra goods on the same visit use the chosen category."),
        (_, "backpack") => Line(
            "背包权重仅在外出拜访时生效。原版按天数和投资阶段判定是否提供背包，已持有的背包从候选中移除。提供背包时，按组内权重抽取一种。\n外出拜访时，若尚未持有{0}，博士另外固定提供 {1} 个。",
            "Backpack weights apply only to off-site visits. The day and investment stage determine whether a backpack is offered. Backpacks you already own are excluded. If one is offered, the group weights determine its type.\nOn an off-site visit, Dr. Jackson also offers {1} {0} if you do not already own one.",
            GameTerm("Item/item_satchel_name"), 1),
        ("hypertech_smuggler", _) => Line(
            "每次来访逐件抽选，共生成 {1} 件货物。{4}这 {2} 种货物在{0}每次来访时，每种最多出现 {3} 件。抽中其中一种后，该品种即从本次候选中移除。其他货物可以重复抽中。下次来访恢复全部 {5} 种候选，原版首次抽选时每种概率均为 {6}。",
            "Each visit generates {1} items, rolled one at a time. During a visit from the {0}, up to {3} unit of each of these {2} goods can appear ({4}). The roll removes that item from the pool for the rest of the visit. Other goods can appear more than once. Each new visit starts with all {5} choices, each with a vanilla first-roll probability of {6}.",
            profile.Label, choice.Quota!.Total, choice.Quota.SingleUnit.Length, 1, Names(choice.Quota.SingleUnit), choice.Categories.Length,
            StockProductPreview.Percent(1d / choice.Categories.Length)),
        (_, "fish") => Line("{0}阶段按单个成鱼位置显示权重，每个位置独立抽取。",
            "Weights for {0} are shown per adult fish slot. Each slot rolls independently.", GameTerm("NetworkUpgrade/network_AQUACULTURE_II_title")),
        (_, _) when choice.Quota is { } quota => Line(
            "本组逐件抽选，共生成 {0} 件货物。同一种货物可以重复抽中。",
            "This group rolls {0} items, one at a time. The same item can be drawn more than once.", quota.Total),
        _ => ""
    };

    private static string CrateChoiceNote()
        => Line("携带{0}时，按组内权重抽取箱型。",
            "When a {0} is brought, the weights determine its type.", GameTerm("Mechanic/type_supply_crate"));

    private static string PettyThiefNote(StockProfile profile, StockChoice choice)
    {
        var method = StockRules.Methods.First(rule => rule.Profiles.Contains(profile) && rule.Choices.Contains(choice));
        var draws = Enumerable.Range(0, method.Categories.Length)
            .Where(site => method.Categories[site].Contains(choice.Id)).ToArray();
        int guaranteed = StockProductDetails.FixedQuantity(method.MethodName, choice.Id);
        string batch = guaranteed > 0 ? Line("固定从本货表抽取 {0} 份。", "This table always provides {0} unit.", guaranteed) + " " : "";
        string basis = "";
        if (draws.Length != 0)
        {
            var baseChances = draws.Select(site => method.BaseDraws[site]?.Chance).Distinct().ToArray();
            basis = baseChances.Length == 1 && baseChances[0] is double chance
                ? Line("另有 {0} 次独立抽选，每次基础概率为 <b>{1}</b>。",
                    "There are {0} separate rolls, each with a base chance of <b>{1}</b>.", draws.Length, StockProductPreview.Percent(chance))
                : Line("本货表有 {0} 次独立抽选。", "This table has {0} separate rolls.", draws.Length);
            if (guaranteed == 0)
                basis = basis.Replace("另有", "有");
        }
        return string.Join(" ", new[] { batch.TrimEnd(), basis, StockProductPreview.BlackMarketMultiplierNote(),
            Text("每次抽中后，按本组权重选出商品。", "The weights determine the goods for each successful roll.") }
            .Where(text => text.Length != 0));
    }

    internal static string HoverSummary(StockProfile profile, StockChoice choice)
    {
        string note = Summary(profile, choice);
        Pool pool = CurrentPool(profile, choice);
        if (note.Length == 0)
            note = Text("每次抽选都按权重从符合条件的候选中选出一项。", "Each roll selects one eligible choice according to its weight.");
        return pool.ShowReferenceInSummary && pool.Caption.Length != 0
            ? note + "\n\n" + Line("列表中的概率对应「{0}」。", "The list shows probabilities for “{0}”.", pool.Caption)
            : note;
    }

    internal static string[] ProbabilityPreview(StockProfile profile, StockChoice choice, int[] positions, bool enabled, bool linear = false)
        => CandidateProbabilities(CurrentPool(profile, choice), choice, positions, enabled, linear)
            .Select(chance => StockProductPreview.ShortProbabilityChange(chance.Original, chance.Modified)).ToArray();

    internal static string[] EligibleCandidates(StockProfile profile, StockChoice choice)
    {
        Pool pool = CurrentPool(profile, choice);
        return pool.Ids.Where((_, index) => pool.Weights[index] > 0).ToArray();
    }

    internal static string AllocationDetails(StockProfile profile, StockChoice choice, StockAllocation allocation,
        Func<int, int> countFor, bool enabled)
    {
        Pool pool = CurrentPool(profile, choice);
        bool active = enabled && allocation.ValidationError(pool.Ids.Where((_, index) => pool.Weights[index] > 0)) == null;
        string status = active ? allocation.Summary()
            : Text("当前按原版规则抽选。本组保存的指定数量如下。", "Vanilla draw rules currently apply. Saved quantities are listed below.");
        var lines = new List<string> { status };
        if (active && choice.Quota!.SingleUnit.Length != 0)
            lines.Add(Text("先生成已指定数量的限量货物，再生成其他已指定数量的货物。抽选剩余名额时，限量货物抽中后从本次候选移除。",
                "Specified limited goods are placed first, then other specified goods. A limited good is removed from the remaining draws as soon as it appears."));
        double[]? remainingWeights = active && allocation.Remaining > 0 ? allocation.RandomWeights(pool.Ids, pool.Weights) : null;
        if (remainingWeights != null)
            lines.Add(Text("随机候选后列出剩余名额首次抽选的概率。", "Random choices show the probability for the first unfilled slot."));
        for (int branch = 0; branch < choice.Categories.Length; branch++)
        {
            string id = choice.Categories[branch];
            int count = countFor(branch);
            string result = StockAllocation.DescribeCount(count);
            if (count < 0 && active)
                result = remainingWeights == null ? Text("本组名额已分配完毕", "All slots allocated")
                    : result + "　" + StockProductPreview.Percent(ReadWeight(pool.Ids, remainingWeights, id));
            lines.Add(ItemName(id) + "　" + result);
        }
        return string.Join("\n", lines);
    }

    internal static string WeightCurveFormula(bool linear)
        => linear
            ? Line("开启「线性变化」时，负档 s 将原版权重乘以 （{0} + s） ÷ {0}。正档各层的分配额度按相邻档位的差计算，1 档对应 1 个百分点。",
                "With Linear Adjustments on, a negative setting s multiplies the vanilla weight by ({0} + s) ÷ {0}. Each positive layer gets one percentage point for each step between adjacent settings.",
                StockCurve.Limit)
            : Line("默认曲线下，负档 s 将原版权重乘以 （{0} + s） ÷ （{0} − s）。只有一项设为正档且其余为 0 时，同一公式也适用于该项。多项为正档时，按档位差分配增加的概率。",
                "On the default curve, a negative setting s multiplies the vanilla weight by ({0} + s) ÷ ({0} − s). The same formula applies to one positive choice when all the others are at 0. With several positive choices, the gaps between levels determine the shares to distribute.",
                StockCurve.Limit);

    internal static string WeightModeNote(bool linear)
        => WeightCurveFormula(linear) + "\n\n" + Text(
            "正档每层由达到该档位的候选按原版权重分配。0 保留原版基础权重。−100 排除该候选。若只有一项为正且为 +100，该项在有资格时必定抽中。两项为 +100、其余为 0 或负档时，两项按原版权重竞争每个名额。所有当前候选档位相同则沿用原版比例。",
            "Each positive layer is allocated among the choices that reach it, in proportion to their vanilla weights. A setting of 0 keeps the base weight. −100 excludes the choice. One eligible positive choice at +100 wins its roll. If two are at +100 and the others are at 0 or below, both compete for each slot using their vanilla weight ratio. If every eligible choice has the same setting, vanilla proportions apply.");

    internal static bool[] AffectedChoices(StockProfile profile, StockChoice choice, int[] positions, bool linear)
        => CandidateProbabilities(CurrentPool(profile, choice), choice, positions, true, linear)
            .Select(chance => StockProductPreview.ProbabilityChanged(chance.Original, chance.Modified)).ToArray();

    private static IEnumerable<(double Original, double Modified)> CandidateProbabilities(Pool pool, StockChoice choice,
        int[] positions, bool enabled, bool linear)
    {
        double total = pool.Weights.Sum();
        double[] modified = Adjusted(choice, pool, positions, enabled, linear);
        return choice.Categories.Select(id =>
            (total > 0 ? ReadWeight(pool.Ids, pool.Weights, id) / total : 0, ReadWeight(pool.Ids, modified, id)));
    }

    internal static string DescribeReferenceWeights(StockProfile profile, StockChoice choice, int[] positions, int? backpackPage,
        bool linear = false)
    {
        var (caption, weights) = WeightPreview(profile, choice, positions, true, backpackPage, linear, showStatus: true);
        return caption + "\n" + string.Join("\n", choice.Categories.Select((id, index) => $"{ItemName(id)}　{weights[index]}"));
    }

    internal static (string Caption, string[] Weights) WeightPreview(StockProfile profile, StockChoice choice, int[] positions, bool enabled,
        int? backpackPage = null, bool linear = false, bool showStatus = false)
    {
        Pool pool = CurrentPool(profile, choice, backpackPage);
        double total = pool.Weights.Sum();
        var adjusted = Adjusted(choice, pool, positions, enabled, linear);
        string caption = Text("<b>原版权重 → 修正后权重</b>", "<b>Vanilla Weight → Mod Weight</b>");
        if (pool.Caption.Length > 0)
            caption = pool.Caption + "　" + caption;

        return (caption, choice.Categories.Select(id =>
        {
            double original = ReadWeight(pool.Ids, pool.Weights, id);
            double modified = ReadWeight(pool.Ids, adjusted, id);
            string status = showStatus && total > 0 ? StockProductPreview.AppliedIfChanged(original / total, modified) : "";
            return $"{Weight(original)} → {Weight(modified * total)}{status}";
        }).ToArray());
    }

    internal static string[] BackpackPages(StockChoice choice)
        => Enumerable.Range(0, 1 << choice.Categories.Length)
            .Select(mask => BackpackOwnership(SelectMask(choice.Categories, mask))).ToArray();

    private static string BackpackOwnership(string[] owned)
        => owned.Length == 0 ? Text("未持有背包", "No Backpacks Owned")
            : Line("已持有 {0}", "Owned · {0}", Names(owned));

    internal static int CurrentBackpackPage(StockChoice choice)
    {
        if (!HasStore())
            return 0;
        var owned = EmporiumEntry.Instance.GetAllOwnedItems().Select(item => item.identifier).ToHashSet();
        int mask = 0;
        for (int index = 0; index < choice.Categories.Length; index++)
            if (owned.Contains(choice.Categories[index]))
                mask |= 1 << index;
        return mask;
    }

    internal static string DescribeProbabilities(StockProfile profile, StockChoice choice, int[] positions, bool enabled,
        IEnumerable<(string Condition, double Original, double Modified)>? appearance = null, int backpackPage = 0, bool linear = false)
    {
        var sections = new List<string> { ProbabilityComparison };
        switch (profile.Id, choice.Id)
        {
            case (_, "crate") when appearance != null:
                Pool crates = BasePool(choice);
                string noCrate = Line("未携带上述{0}", "None of the listed crates brought", GameTerm("Mechanic/type_supply_crate"));
                foreach (var (condition, original, modified) in appearance)
                    Add(condition.Length == 0 ? Text("固定供货", "Guaranteed stock") : condition, crates, original, modified, noCrate);
                break;
            case ("thief", "fallback"):
                Add(Text("柜台无待售货物", "No goods for sale on the counter"), BasePool(choice));
                Add(Text("柜台有待售货物", "Goods for sale on the counter"), BasePool(choice), 0, 0,
                    Text("未触发柜台补货", "No counter restock"));
                break;
            case ("shadyPharma", "shipment"):
                Add(Text("已解锁随机注射器", "Random injector unlocked"), BasePool(choice));
                Add(Text("未解锁随机注射器", "Random injector locked"), new Pool(choice.Categories, new[] { 0d, 1d }));
                break;
            case ("lowerLevelChemist", "shipment"):
                Add(Text("可携带违禁品", "Contraband allowed"), BasePool(choice));
                Add(Text("限售违禁品", "Contraband restricted"), new Pool(choice.Categories, new[] { 0d, 1d }));
                break;
            case ("lowerLevelChemistBarter", "shipment"):
                Add(Text("可携带违禁品", "Contraband allowed"), BasePool(choice));
                Add(Text("限售违禁品", "Contraband restricted"), new Pool(choice.Categories, choice.RangeWeights(1)));
                break;
            default:
                switch (choice.Kind)
                {
                    case StockChoiceKind.Inspectable:
                        for (int mask = 1; mask < 1 << choice.Categories.Length; mask++)
                        {
                            string[] available = SelectMask(choice.Categories, mask);
                            Add(Line("可抽取 {0}", "Eligible choices · {0}", Names(available)), EqualPool(available));
                        }
                        break;
                    case StockChoiceKind.Appraisal:
                        foreach (Pool pool in AppraisalPools(choice))
                            Add(pool.Caption, pool);
                        break;
                    case StockChoiceKind.Backpack:
                        var owned = new HashSet<string>(SelectMask(choice.Categories, backpackPage));
                        int lastDay = 130 - InvestmentBonus(profile);
                        AddBackpackDays(owned, lastDay);
                        break;
                    case StockChoiceKind.List:
                        string[] onePerVisit = choice.Quota!.SingleUnit;
                        int remainingAfterFirst = choice.Categories.Length - 1;
                        sections[0] += "\n" + Line("下表按本次来访已经抽中的货物，分别列出下一次抽选的概率。例如，先抽中{0}，原版下一抽从剩余的 {1} 种候选中等概率抽选，每种概率为 {2}。",
                            "Each table shows the probabilities for the next roll given the goods already rolled on this visit. For example, rolling {0} first leaves {1} choices in vanilla, each with a {2} chance.",
                            ItemName(onePerVisit[0]), remainingAfterFirst, StockProductPreview.Percent(1d / remainingAfterFirst));
                        for (int mask = 0; mask < 1 << onePerVisit.Length; mask++)
                        {
                            string[] chosen = SelectMask(onePerVisit, mask);
                            string[] remaining = choice.Categories.Where(id => !chosen.Contains(id)).ToArray();
                            Add(mask == 0 ? Line("上述 {0} 种货物仍在候选中", "All {0} once-per-visit goods are still eligible", onePerVisit.Length)
                                : Line("本次已抽中 {0}", "Already rolled on this visit · {0}", Names(chosen)), EqualPool(remaining));
                        }
                        break;
                    default:
                        Pool current = CurrentPool(profile, choice);
                        Add(current.Caption.Length == 0 ? Text("单次分类抽选", "Single category roll") : current.Caption, current);
                        break;
                }
                break;
        }
        return string.Join("\n\n", sections);

        void Add(string heading, Pool pool, double nativeAppearance = 1, double modAppearance = 1, string? absent = null)
        {
            double total = pool.Weights.Sum();
            double[] original = total > 0 ? pool.Weights.Select(weight => weight / total).ToArray() : new double[pool.Ids.Length];
            var adjusted = Adjusted(choice, pool, positions, enabled, linear);
            var rows = new List<string> { $"<b>{heading}</b>" };
            foreach (string id in choice.Categories)
                rows.Add(Row(ItemName(id), ReadWeight(pool.Ids, original, id) * nativeAppearance,
                    ReadWeight(pool.Ids, adjusted, id) * modAppearance));

            int[] other = Enumerable.Range(0, pool.Ids.Length).Where(index => !choice.Categories.Contains(pool.Ids[index])).ToArray();
            if (other.Length > 0)
                rows.Add(Row(Text("其他候选", "Other choices"), other.Sum(index => original[index]) * nativeAppearance,
                    other.Sum(index => adjusted[index]) * modAppearance));
            if (absent != null)
                rows.Add(Row(absent, 1 - nativeAppearance, 1 - modAppearance));
            if (total <= 0 && absent == null)
                rows.Add(Text("当前没有符合原版条件的候选。", "No choices meet the current vanilla conditions."));
            sections.Add(string.Join("\n", rows));
        }

        void AddBackpackDays(HashSet<string> owned, int lastDay)
        {
            string noBackpack = Text("未携带上述背包", "None of the listed backpacks brought");
            int firstDay = 0;
            Pool pool = BackpackPool(profile, choice, 0, owned, out double appears);
            for (int day = 1; day <= lastDay; day++)
            {
                Pool next = BackpackPool(profile, choice, day, owned, out double nextAppears);
                if (pool.Weights.SequenceEqual(next.Weights))
                    continue;
                Add(firstDay == day - 1 ? Day(firstDay) : DayRange(firstDay, day - 1), pool, appears, appears, noBackpack);
                firstDay = day;
                pool = next;
                appears = nextAppears;
            }
            Add(FromDay(firstDay), pool, appears, appears, noBackpack);
        }
    }

    private static Pool CurrentPool(StockProfile profile, StockChoice choice, int? backpackPage = null)
    {
        bool inStore = HasStore();
        if (profile.Id == "shadyPharma" && inStore)
            return PlayerStore.Instance.isInjectorIntroduced
                ? new Pool(choice.Categories, choice.NativeWeights, Text("当前已解锁随机注射器", "Random injector currently unlocked"),
                    showReferenceInSummary: true)
                : new Pool(choice.Categories, new[] { 0d, 1d }, Text("当前未解锁随机注射器", "Random injector currently locked"),
                    showReferenceInSummary: true);
        if (profile.Id == "shadyPharma")
            return new Pool(choice.Categories, choice.NativeWeights, Text("随机注射器解锁后", "After the random injector unlocks"));
        if (profile.Id is "lowerLevelChemist" or "lowerLevelChemistBarter")
            return BasePool(choice, Text("可携带违禁品时", "When contraband is allowed"));
        if (choice.Id == "crate")
            return BasePool(choice, Text("携带物资箱时", "When carrying a supply crate"));
        switch (choice.Kind)
        {
            case StockChoiceKind.Inspectable:
                var available = inStore ? StockRandom.InspectableCandidates(PlayerStore.Instance, StockRandom.HasNativePaging) : null;
                return available == null ? EqualPool(choice.Categories, AllInspectionCategories(), showReferenceInSummary: true)
                    : new Pool(choice.Categories, available.Select(value => value ? 1d : 0d).ToArray(),
                        Text("当前已开放的货物", "Currently unlocked goods"));
            case StockChoiceKind.Appraisal:
                return inStore ? EqualPool(AppraisalItemRegistry.GetPool(AppraisalRankHelper.GetRank()).Select(item => item.id).ToArray(),
                    Text("按当前鉴定证书筛选", "Filtered by the current appraisal certificate"))
                    : EqualPool(choice.Categories, Text("参考 · 所列待鉴定物品", "Reference · Listed appraisal items"),
                        showReferenceInSummary: true);
            case StockChoiceKind.Backpack:
                int day = inStore ? StoreStation.GetDayCounter() : 1;
                string[] selected = SelectMask(choice.Categories, backpackPage ?? CurrentBackpackPage(choice));
                string reference = inStore ? "" : Text("参考 · ", "Reference · ");
                string caption = reference + Line("{0} · {1} · 携带背包时", "{0} · {1} · When carrying a backpack",
                    Day(day), BackpackOwnership(selected));
                return BackpackPool(profile, choice, day, new HashSet<string>(selected), out _, caption);
            case StockChoiceKind.Table:
                var entries = LootRegistry.GetTableEntries(choice.Id);
                if (entries.Count != 0 || LootRegistry.HasTable(choice.Id))
                    return new Pool(entries.Select(entry => entry.id).ToArray(), entries.Select(entry => (double)entry.weight).ToArray());
                return new Pool(choice.Categories, choice.Id switch
                {
                    "packedFoodTable" => PackedFood,
                    "medicalTable" => Medical,
                    "junkTable" => Junk,
                    _ => Enumerable.Repeat(125d, choice.Categories.Length).ToArray()
                }, Text("参考 · 原版货表", "Reference · Vanilla loot table"));
            case StockChoiceKind.List:
                return EqualPool(choice.Categories, Text("首次抽选", "First roll"));
            default:
                return BasePool(choice);
        }

        static string AllInspectionCategories() => Text("参考 · 全部鉴定类别开放", "Reference · All inspection categories unlocked");
    }

    private static IEnumerable<Pool> AppraisalPools(StockChoice choice)
    {
        int choices = 1 << choice.Categories.Length;
        bool inStore = HasStore();
        for (int rank = 0; rank <= 3; rank++)
        {
            string rankName = GameTerm(AppraisalRanks[rank]);
            var unlocked = AppraisalItemRegistry.GetUnlocked(rank);
            if (unlocked.Count == 0)
            {
                yield return EqualPool(new[] { AppraisalItemRegistry.GetLowestRankType()?.id ?? "hand" },
                    Line("{0} · 原版保底", "{0} · Vanilla fallback", rankName));
                continue;
            }
            var blockedByOtherCertificates = unlocked.Where(item => inStore && !choice.Categories.Contains(item.id)
                && AppraisalItemRegistry.IsBlocked(item, rank)).Select(item => item.id).ToHashSet();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int mask = 0; mask < choices; mask++)
            {
                var excluded = SelectMask(choice.Categories, mask);
                string[] available = unlocked.Where(item => !blockedByOtherCertificates.Contains(item.id)
                        && (!excluded.Contains(item.id) || !AppraisalItemRegistry.CanBlock(item, rank)))
                    .Select(item => item.id).ToArray();
                if (available.Length == 0)
                    available = unlocked.Select(item => item.id).ToArray();
                if (seen.Add(string.Join(",", available)))
                    yield return EqualPool(available, Line("{0} · 允许 {1}", "{0} · Eligible · {1}", rankName, Names(available)));
            }
        }
    }

    private static int InvestmentBonus(StockProfile profile)
        => profile.Id.EndsWith("Investment2", StringComparison.Ordinal) ? 30
            : profile.Id.EndsWith("Investment1", StringComparison.Ordinal) ? 10 : 0;

    private static Pool BackpackPool(StockProfile profile, StockChoice choice, int day, HashSet<string> owned,
        out double appears, string caption = "")
    {
        int bonus = InvestmentBonus(profile);
        double[] chances = { day - 30 + bonus, day - 12 + bonus, 2 * day + 10 + bonus };
        double[] weights = StockRandom.BackpackWeights(choice, chances, owned, out double none);
        appears = 1 - none;
        return new Pool(choice.Categories, weights, caption, showReferenceInSummary: true);
    }

    private static bool HasStore() => EmporiumEntry.Instance != null && EmporiumEntry.Instance.invElement != null && PlayerStore.Instance != null;

    private static Pool BasePool(StockChoice choice, string caption = "")
        => new Pool(choice.Categories, choice.Kind == StockChoiceKind.Range
            ? choice.RangeWeights(choice.RangeStarts[choice.RangeStarts.Length - 1] - 1) : choice.NativeWeights, caption);

    private static Pool EqualPool(string[] ids, string caption = "", bool showReferenceInSummary = false)
        => new Pool(ids, Enumerable.Repeat(1d, ids.Length).ToArray(), caption, showReferenceInSummary);

    private static double[] Adjusted(StockChoice choice, Pool pool, int[] positions, bool enabled, bool linear)
    {
        double total = pool.Weights.Sum();
        if (total <= 0)
            return new double[pool.Ids.Length];
        double[]? adjusted = enabled ? StockCurve.ChoiceWeights(pool.Weights, StockRandom.EntryPositions(choice, pool.Ids, positions), linear) : null;
        return adjusted ?? pool.Weights.Select(weight => weight / total).ToArray();
    }

    private static string[] SelectMask(string[] ids, int mask)
        => ids.Where((_, index) => (mask & (1 << index)) != 0).ToArray();

    private static double ReadWeight(string[] ids, double[] weights, string id)
        => weights.Where((_, index) => ids[index] == id).Sum();

    private static string ItemName(string id) => StockCatalog.Items.ContainsKey(id) ? StockCatalog.Name(id) : Text("其他候选", "Other choices");

    private static string Names(IEnumerable<string> ids) => JoinList(ids.Select(ItemName));

    private static string Weight(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Row(string name, double original, double modified)
        => $"{name}　{StockProductPreview.Percent(original)} → <b>{StockProductPreview.Percent(modified)}</b>"
            + StockProductPreview.AppliedIfChanged(original, modified);
}
