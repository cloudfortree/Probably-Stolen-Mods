using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using static BetterCustomerStock.StockLanguage;

namespace BetterCustomerStock;

[Flags]
internal enum StockAxis
{
    None = 0,
    Appearance = 1,
    Quantity = 2,
    Selection = 4,
    Allocation = 8
}

internal enum StockChangeStatus
{
    Effective,
    Pending,
    Superseded,
    Inactive
}

internal sealed class StockProfile
{
    internal readonly string Id;
    internal readonly string SourceLabel;
    internal readonly string[] ClientIds;
    internal StockProfile? DefaultProfile;
    internal Func<string>? FixedStockNotice;
    internal string Label => CustomerName(Id, SourceLabel);
    internal bool FixedStock => FixedStockNotice != null;

    internal StockProfile(string id, string label, params string[] otherIds)
    {
        Id = id;
        SourceLabel = label;
        ClientIds = new[] { id }.Concat(otherIds).ToArray();
    }

    internal bool Includes(string? id)
        => Array.IndexOf(ClientIds, id) >= 0;
}

internal sealed class StockCategoryMask
{
    internal const string AllChecked = "*";

    private readonly HashSet<string> exceptions = new HashSet<string>(StringComparer.Ordinal);
    internal bool DefaultChecked { get; private set; }

    internal StockCategoryMask(string value) => Load(value);

    internal void Load(string value)
    {
        exceptions.Clear();
        // A leading asterisk represents a selection with only a few unchecked rows.
        DefaultChecked = value == AllChecked || value.StartsWith(AllChecked + ",", StringComparison.Ordinal);
        foreach (string key in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (key != AllChecked)
                exceptions.Add(key);
        }
    }

    internal bool IsChecked(string customer, StockAxis axis, string category)
        => DefaultChecked != exceptions.Contains(Key(customer, axis, category));

    internal void SetChecked(string customer, StockAxis axis, string category, bool isChecked)
    {
        string choice = Key(customer, axis, category);
        if (isChecked == DefaultChecked)
            exceptions.Remove(choice);
        else
            exceptions.Add(choice);
    }

    internal string Save()
    {
        var sorted = exceptions.OrderBy(key => key, StringComparer.Ordinal);
        return string.Join(",", DefaultChecked ? sorted.Prepend(AllChecked) : sorted);
    }

    internal static string Key(string customer, StockAxis axis, string category)
        => customer + (axis switch
        {
            StockAxis.Appearance => ":appearance:",
            StockAxis.Selection => ":selection:",
            StockAxis.Allocation => ":allocation:",
            _ => ":quantity:"
        }) + category;
}

internal sealed class StockCategoryPositions
{
    private readonly Dictionary<string, int> positions = new Dictionary<string, int>(StringComparer.Ordinal);

    internal StockCategoryPositions(string value) => Load(value);

    internal void Load(string value)
    {
        positions.Clear();
        foreach (string entry in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = entry.IndexOf('=');
            if (separator > 0 && int.TryParse(entry.Substring(separator + 1), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int position))
                Set(entry.Substring(0, separator), position);
        }
    }

    internal int Get(string customer, StockAxis axis, string category)
        => positions.TryGetValue(StockCategoryMask.Key(customer, axis, category), out int position) ? position : 0;

    internal void Set(string customer, StockAxis axis, string category, int position)
        => Set(StockCategoryMask.Key(customer, axis, category), position);

    private void Set(string key, int position)
    {
        if (position == 0)
            positions.Remove(key);
        else
            positions[key] = Math.Min(StockCurve.Limit, Math.Max(-StockCurve.Limit, position));
    }

    internal string Save() => string.Join(",", positions.OrderBy(entry => entry.Key, StringComparer.Ordinal)
        .Select(entry => entry.Key + "=" + entry.Value.ToString(CultureInfo.InvariantCulture)));
}

internal sealed class StockSettings
{
    internal const string CustomerSection = "顾客设置";
    internal const string CategoryKey = "categories.checkboxes";
    internal const string CategoryPositionsKey = "categories.positions";
    internal const string IndividualCategoriesSuffix = ".individual_categories";
    internal const string SelectionEnabledSuffix = ".selection_enabled";
    internal const string SpecialGoodsEnabledSuffix = ".special_goods_enabled";
    internal const string AllIndividualCategoriesKey = "default.all_individual_categories";
    internal const string AllStockCategoriesKey = "default.all_stock_categories";
    internal const string AllSpecialGoodsKey = "default.all_special_goods";
    internal const string InvertCustomerKey = "default.invert_customers";
    internal const string InvertCategoryKey = "default.invert_categories";
    internal const string InlinePreviewKey = "default.inline_stock_preview";
    internal const string LinearChangesKey = "default.linear_changes";
    internal const string GlobalSection = "全局设置";
    private static string AppearanceTip() => Text("调整可选货物的出现概率。0 沿用原版，−100 调至 0%，+100 调至 100%。",
        "Adjust the chance of optional stock. A setting of 0 uses the vanilla chance. −100 sets the chance to 0%, and +100 sets it to 100%.");
    private static string QuantityTip() => Text("调整抽到更多份的概率。0 沿用原版，−100 取原版最低数量，+100 取原版最高数量。",
        "Adjust the chance of getting more units. A setting of 0 uses the vanilla probabilities. −100 gives the vanilla minimum, and +100 gives the vanilla maximum.");

    private static string InactiveSlidersTip() => Text(
        "该组的全局档位、顾客独立档位和逐项滑动条档位暂时停用，设定值会保留。",
        "The global, customer override, and individual goods slider values for these rolls are kept while their adjustments are off.");

    private static string InversionApplyTip()
        => Line("切换时保留原有勾选状态。点击「{0}」后，下次生成待售货物或以物易物报价时按新规则调整。",
            "Switching this option keeps the existing check states. Click {0} to use the new rules the next time the game generates goods for sale or a barter offer.",
            GameTerm("MainMenu/option_apply"));

    private static string LinearChangesTip()
        => Text(
            "关闭时使用默认曲线。可适度提高少见货物的抽中机会，同时保留随机抽选。\n"
            + "开启后使用线性曲线。出货判定和数量抽选在 0 的同一侧每调一档，概率变化的百分点数相同，便于按固定幅度调整。",
            "When off, the default curve can give rarer goods a better chance of appearing while keeping the draw random.\n"
            + "When on, each step on the same side of 0 changes a stock chance or quantity roll by the same number of percentage points.")
            + "\n\n" + CurveExample() + "\n\n"
            + Text("点击查看计算公式和分类权重规则。", "Click to see the formulas and category weight rules.");

    internal static string CurveName(bool linear)
        => linear ? Text("线性曲线", "Linear Curve") : Text("默认曲线", "Default Curve");

    internal static string CurveIntro(bool linear)
        => Line(
            "已应用的设置使用{0}。\n\n"
            + "出货判定和数量抽选的公式中，p 为原版概率（0 至 1），s 为档位。原版概率为 0% 或 100% 时保留原值。0 档沿用原版概率。",
            "The applied settings use the {0}.\n\n"
            + "For stock chance and quantity rolls, p is the vanilla probability from 0 to 1, and s is the slider setting. Vanilla probabilities of 0% and 100% stay fixed. Setting the slider to 0 keeps the vanilla probability.",
            CurveName(linear));

    internal static string CurveFormula(bool linear)
        => linear
            ? Line("负档的修正概率 = p × （{0} + s） ÷ {0}\n正档的修正概率 = p + （1 − p） × s ÷ {0}",
                "Adjusted probability at negative settings = p × ({0} + s) ÷ {0}\nAdjusted probability at positive settings = p + (1 − p) × s ÷ {0}", StockCurve.Limit)
            : Line("修正概率 = p × （{0} + s） ÷ ［p × （{0} + s） + （1 − p） × （{0} − s）］",
                "Adjusted probability = p × ({0} + s) ÷ [p × ({0} + s) + (1 − p) × ({0} − s)]", StockCurve.Limit);

    internal static string CurveExample()
    {
        const double exampleChance = 0.10;
        const int exampleSetting = StockCurve.Limit / 2;
        return Line(
            "例如，原版概率为 {0} 的抽选设为 {1} 档时，默认曲线得到 {2}，线性曲线得到 {3}。",
            "For example, at {1}, a vanilla chance of {0} becomes {2} on the default curve and {3} on the linear curve.",
            StockProductPreview.Percent(exampleChance), FormatPosition(exampleSetting),
            StockProductPreview.Percent(StockCurve.Probability(exampleChance, exampleSetting)),
            StockProductPreview.Percent(StockCurve.Probability(exampleChance, exampleSetting, linear: true)));
    }

    private static string IndividualCategoriesLabel()
        => Text("用滑动条逐项设置商品类别", "Use Sliders for Individual Goods Settings");

    private static string SelectionTip() => Text(
        "开启后，从每组候选中按设定的权重抽选。关闭后，按原版权重抽选。组内可抽项档位相同则沿用原版比例，包括全部设为 −100。\n\n"
        + "组内档位不同时，−100 排除该类别。只有一项为正且设为 +100 时，该项必定抽中。多项为正时按各自档位共同参与抽选。"
        + "例如，两项为 +100、其余为 0 或负档时，从这两项中按原版比例抽取。\n\n"
        + "点击分组的信息图标可查看当前规则。",
        "When on, each group rolls using its configured weights. When off, it uses vanilla weights. Eligible choices at the same setting keep their vanilla proportions, even at −100.\n\n"
        + "With mixed settings, −100 excludes that choice. A lone positive choice at +100 is guaranteed. Multiple positive choices compete according to their settings. "
        + "If two choices are at +100 and all others are at 0 or below, the two compete in their vanilla proportions.\n\n"
        + "Click the group's info icon to review its rules.");

    private static string SpecialGoodsToggle()
        => Text("启用特殊商品设置", "Enable Special Goods Settings");

    internal sealed class Draft
    {
        private readonly Func<string, bool?> readToggle;
        private readonly Func<string, int?> readPosition;
        internal readonly StockCategoryMask Mask;
        internal readonly StockCategoryPositions Positions;

        internal Draft(Func<string, bool?> readToggle, Func<string, int?> readPosition,
            StockCategoryMask mask, StockCategoryPositions positions)
        {
            this.readToggle = readToggle;
            this.readPosition = readPosition;
            Mask = mask;
            Positions = positions;
        }

        internal bool Read(ConfigEntry<bool> entry) => readToggle(entry.Key) ?? entry.Value;
        internal int Read(ConfigEntry<int> entry) => readPosition(entry.Key) ?? entry.Value;
    }

    private sealed class Options
    {
        internal readonly ConfigEntry<bool>? Enabled;
        internal readonly ConfigEntry<bool>? Override;
        internal readonly ConfigEntry<bool>? IndividualCategories;
        internal readonly ConfigEntry<bool>? SelectionEnabled;
        internal readonly ConfigEntry<bool>? SpecialGoodsEnabled;
        internal readonly ConfigEntry<int>? Appearance;
        internal readonly ConfigEntry<int>? Quantity;

        internal Options(ModConfig config, string prefix, string section, StockAxis axes,
            StockProfile? profile = null, ConfigEntry<bool>? invertCustomers = null, bool hasSelectionChoices = false,
            bool hasSpecialGoods = false)
        {
            if (profile != null)
            {
                Enabled = config.Define(prefix + ".enabled", false, section,
                    () => CustomerSwitchLabel(profile, invertCustomers?.Value == true),
                    () => CustomerSwitchTip(invertCustomers?.Value == true));
                if (axes != StockAxis.None)
                    Override = config.Define(prefix + ".override", false, section,
                        () => Text("使用独立设置", "Use Customer Overrides"),
                        () => Line("开启后，按下方档位调整该顾客的{0}或{1}。关闭后，沿用「{2}」的档位。",
                            "When on, the sliders below set this customer's {0} or {1} adjustment. When off, this customer uses the slider settings from {2}.",
                            StockLanguage.Appearance, StockLanguage.Quantity, profile.DefaultProfile?.Label ?? GlobalSettings));
            }
            if (axes.HasFlag(StockAxis.Appearance))
                Appearance = config.Define(prefix + ".appearance", 0, section, () => StockLanguage.Appearance,
                    -StockCurve.Limit, StockCurve.Limit, AppearanceTip);
            if (axes.HasFlag(StockAxis.Quantity))
                Quantity = config.Define(prefix + ".quantity", 0, section, () => StockLanguage.Quantity,
                    -StockCurve.Limit, StockCurve.Limit, QuantityTip);
            if (profile != null && axes != StockAxis.None)
                IndividualCategories = config.Define(prefix + IndividualCategoriesSuffix, false, section,
                    IndividualCategoriesLabel,
                    () => Line("开启后，「{0}」中的出货概率和货物数量按各项的滑动条档位调整，替代全局和顾客档位。该顾客的商品复选框和反转设置暂时停用。"
                        + "关闭后，使用复选框决定哪些货物使用顾客独立档位或沿用的档位。",
                        "When on, each stock chance or quantity entry under {0} uses its own slider in place of the global or customer setting. Goods checkboxes and their inversion setting are inactive for this customer. "
                        + "When off, the checkboxes determine which goods use the customer's own or inherited slider settings.",
                        ProductCategories));
            if (hasSelectionChoices)
                SelectionEnabled = config.Define(prefix + SelectionEnabledSuffix, false, section,
                    () => Text("启用货物分类调整", "Enable Category Adjustments"), SelectionTip);
            if (hasSpecialGoods)
                SpecialGoodsEnabled = config.Define(prefix + SpecialGoodsEnabledSuffix, false, section,
                    SpecialGoodsToggle,
                    () => Text("开启后，组内货物按设定的权重抽选，或按选定的件数生成。关闭后，按原版规则抽选。每组的总名额和单品上限沿用原版。",
                        "When on, the group uses the configured weights or quantities. When off, it follows vanilla draws. Both modes keep the vanilla total and per-item limits."));
        }

        internal int? Get(StockAxis axis, Draft? draft)
        {
            if (Override != null && !(draft?.Read(Override) ?? Override.Value))
                return null;

            var entry = axis == StockAxis.Appearance ? Appearance : Quantity;
            return entry == null ? null : draft?.Read(entry) ?? entry.Value;
        }
    }

    private readonly ConfigEntry<bool> enabled;
    private readonly ConfigEntry<bool> appearanceEnabled;
    private readonly ConfigEntry<bool> quantityEnabled;
    private readonly ConfigEntry<bool> invertCustomers;
    private readonly ConfigEntry<bool> invertCategories;
    private readonly ConfigEntry<bool> allIndividualCategories;
    private readonly ConfigEntry<bool> allStockCategories;
    private readonly ConfigEntry<bool> allSpecialGoods;
    private readonly ConfigEntry<bool> linearChanges;
    private readonly Options defaults;
    private readonly Dictionary<StockProfile, Options> profiles = new Dictionary<StockProfile, Options>();
    private readonly Dictionary<StockProfile, (string[] Appearance, string[] Quantity)> categories =
        new Dictionary<StockProfile, (string[], string[])>();
    private readonly Dictionary<StockProfile, List<StockChoice>> choices = new Dictionary<StockProfile, List<StockChoice>>();
    private readonly Dictionary<StockProfile, List<StockMethod>> supplyMethods = new Dictionary<StockProfile, List<StockMethod>>();
    private readonly StockCategoryMask mask;
    private readonly StockCategoryPositions positions;

    internal ModConfig Config { get; }
    internal IReadOnlyList<StockProfile> Customers { get; }

    internal StockSettings(ModConfig config, IEnumerable<StockMethod> methods)
    {
        Config = config;
        var savedValues = AccessTools.Field(typeof(ModConfig), "rawValues")?.GetValue(config) as IDictionary<string, string>;
        enabled = config.Define("default.enabled", true, GlobalSection, () => Text("启用模组效果", "Enable Stock Adjustments"),
            () => Text("关闭后，模组对所有顾客的供货调整均不生效。原有档位和勾选状态会保留。",
                "When off, the mod's adjustments are disabled for all customers. Slider values and checkbox selections are kept."));
        appearanceEnabled = config.Define("default.appearance.enabled", true, GlobalSection,
            () => Text("启用出货概率调整", "Enable Stock Chance Adjustments"),
            () => Line("关闭后，所有顾客按原版概率判定「{0}」中的可选货物和报价是否出现。{1}",
                "When off, all customers use vanilla probabilities to determine whether to bring the goods or make the offers listed under {0}. {1}",
                StockLanguage.Appearance, InactiveSlidersTip()));
        quantityEnabled = config.Define("default.quantity.enabled", true, GlobalSection,
            () => Text("启用货物数量调整", "Enable Stock Quantity Adjustments"),
            () => Line("关闭后，所有顾客按原版规则决定「{0}」中各项货物的件数。{1}",
                "When off, all customers use vanilla rolls to determine the quantities of goods listed under {0}. {1}",
                StockLanguage.Quantity, InactiveSlidersTip()));
        invertCustomers = config.Define(InvertCustomerKey, false, GlobalSection,
            () => Text("反转顾客设置的启用状态", "Invert Customer Toggles"),
            () => Text(
                    "开启时，未勾选的顾客启用供货调整，已勾选的顾客停用供货调整。顾客开关显示为「禁用」。\n"
                    + "关闭时，已勾选的顾客启用供货调整，未勾选的顾客停用供货调整。",
                    "When on, adjustments are enabled for unchecked customers and disabled for checked customers. Customer toggles are labeled Disable.\n"
                    + "When off, adjustments are enabled for checked customers and disabled for unchecked customers.")
                + "\n\n" + InversionApplyTip() + "\n\n"
                + Line(
                    "例如，默认所有顾客均未勾选。此时开启反转并应用，所有适用顾客都会启用调整。若只想停用「{0}」的调整，勾选该顾客的「禁用」开关并再次应用。",
                    "For example, all customer boxes start unchecked. Turning on inversion and applying it enables adjustments for every eligible customer. To disable adjustments for {0} alone, check that customer's Disable toggle and apply again.",
                    CustomerName("thief", "小偷（常规）")));
        invertCategories = config.Define(InvertCategoryKey, false, GlobalSection,
            () => Text("反转商品类别设置的启用状态", "Invert Goods Checkboxes"),
            () => Text(
                    "开启时，未勾选的商品按该顾客使用的档位调整，已勾选的商品停用对应调整。\n"
                    + "关闭时，已勾选的商品按该顾客使用的档位调整，未勾选的商品停用对应调整。\n"
                    + "此设置适用于已启用供货调整并使用商品复选框的顾客。",
                    "When on, unchecked goods use the customer's adjustment level and checked goods have their corresponding adjustment disabled.\n"
                    + "When off, checked goods use the customer's adjustment level and unchecked goods have their corresponding adjustment disabled.\n"
                    + "This applies to customers with adjustments enabled who use goods checkboxes.")
                + "\n\n" + InversionApplyTip() + "\n\n"
                + Line(
                    "例如，「{0}」已启用供货调整，「{1}」中的商品均未勾选。开启反转并应用后，这些商品按当前档位调整。若只想让「{2}」沿用原版{1}，勾选该商品并再次应用。",
                    "For example, suppose {0} has adjustments enabled and all goods under {1} are unchecked. Turning on inversion and applying it makes those goods use the current adjustment level. To keep the vanilla stock chance for {2} alone, check that item and apply again.",
                    CustomerName("thief", "小偷（常规）"), StockLanguage.Appearance, GameTerm("Item/item_eng_keycard_name")));
        allIndividualCategories = config.Define(AllIndividualCategoriesKey, false, GlobalSection,
            () => Text("统一使用滑动条替代", "Use Individual Goods Sliders for All Customers"),
            () => Line("为所有适用顾客启用「{0}」。已启用调整的顾客按各项滑动条档位生成货物。全局出货概率和货物数量滑动条及商品类别反转暂时停用。关闭后，各顾客恢复原来使用的复选框或逐项滑动条。",
                "Enables “{0}” for every applicable customer. Customers with adjustments enabled use the individual goods sliders. The global stock chance and quantity sliders and goods checkbox inversion are inactive. When off, each customer returns to their saved checkbox or individual slider mode.",
                IndividualCategoriesLabel()));
        allStockCategories = config.Define(AllStockCategoriesKey, false, GlobalSection,
            () => Text("开启全部货物分类", "Enable All Stock Category Adjustments"),
            () => Line("开启后，已启用调整的顾客按「{0}」各组设定的权重抽选货物。关闭后，各顾客按自己的开关使用已设定的权重或原版权重。",
                "When on, customers with adjustments enabled roll goods using the weights set under {0}. When off, each customer's switch determines whether to use configured or vanilla weights.",
                Classification));
        ImportSavedToggle(AllStockCategoriesKey, AllSpecialGoodsKey);
        allSpecialGoods = config.Define(AllSpecialGoodsKey, false, GlobalSection,
            () => Text("为全部顾客启用特殊商品设置", "Enable Special Goods Settings for All Customers"),
            () => Line("开启后，已启用调整的顾客按设定的权重或指定数量生成特殊商品。关闭后，各顾客按自己的「{0}」开关使用自设规则或原版规则。",
                "When on, customers with adjustments enabled use the configured weights or quantities for special goods. When off, each customer's {0} switch determines whether to use those settings or vanilla draws.",
                SpecialGoodsToggle()));
        defaults = new Options(config, "default", GlobalSection, StockAxis.Appearance | StockAxis.Quantity);
        linearChanges = config.Define(LinearChangesKey, false, GlobalSection, () => Text("线性变化", "Linear Adjustments"), LinearChangesTip);
        config.Define(InlinePreviewKey, true, GlobalSection, () => Text("实时显示原版和修正概率或数量", "Show Live Stock Comparisons"),
            () => Text("开启后，商品行随设置变化显示「原版 → 修正」。出货概率显示携带概率，货物分类与特殊商品的权重模式显示单次抽选的概率，货物数量显示期望份数。"
                + "随声望、天数等条件变化的结果会显示范围。详情在点击「应用」后更新。",
                "When on, each goods row updates its Vanilla → Mod comparison as you edit. Stock chance shows the chance of carrying the goods. Stock categories and special goods in weight mode show the chance per draw. Stock quantity shows expected units. "
                + "Values that vary with reputation, day, or other conditions are shown as ranges. Details update after you click Apply."));
        var categoriesByProfile = new Dictionary<StockProfile, (HashSet<string> Appearance, HashSet<string> Quantity)>();
        foreach (StockMethod method in methods)
        {
            if (method.FixedProfile is StockProfile fixedStock)
                categories[fixedStock] = (Array.Empty<string>(), Array.Empty<string>());

            foreach (StockProfile profile in method.Profiles)
            {
                if (!supplyMethods.TryGetValue(profile, out var supplies))
                    supplyMethods[profile] = supplies = new List<StockMethod>();
                supplies.Add(method);
                if (!categoriesByProfile.TryGetValue(profile, out var items))
                    categoriesByProfile[profile] = items = (new HashSet<string>(StringComparer.Ordinal),
                        new HashSet<string>(StringComparer.Ordinal));
                for (int site = 0; site < method.Draws.Length; site++)
                {
                    switch (method.AxisFor(profile, site))
                    {
                        case StockAxis.Appearance:
                            items.Appearance.UnionWith(method.Categories[site]);
                            break;
                        case StockAxis.Quantity:
                            items.Quantity.UnionWith(method.Categories[site]);
                            break;
                    }
                }
                foreach (StockChoice choice in method.Choices)
                {
                    if (!choices.TryGetValue(profile, out var available))
                        choices[profile] = available = new List<StockChoice>();
                    if (!available.Contains(choice))
                        available.Add(choice);
                }
            }
        }

        var labelOrder = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), false);
        foreach (var entry in categoriesByProfile)
            categories[entry.Key] = (SortCategories(entry.Value.Appearance), SortCategories(entry.Value.Quantity));
        Customers = categories.Keys.OrderBy(profile => profile.SourceLabel, labelOrder).ToArray();

        ImportInventorBackpackSettings(savedValues);
        var saved = config.Define(CategoryKey, "", CustomerSection, () => ProductCategories);
        mask = new StockCategoryMask(saved.Value);
        saved.OnChanged += mask.Load;
        var savedPositions = config.Define(CategoryPositionsKey, "", CustomerSection, () => Text("商品类别档位", "Goods Adjustment Levels"));
        positions = new StockCategoryPositions(savedPositions.Value);
        savedPositions.OnChanged += positions.Load;

        foreach (StockProfile profile in Customers.Where(profile => !profile.FixedStock))
        {
            var items = categories[profile];
            StockAxis axes = StockAxis.None;
            if (items.Appearance.Length != 0)
                axes |= StockAxis.Appearance;
            if (items.Quantity.Length != 0)
                axes |= StockAxis.Quantity;
            bool hasSelectionChoices = false;
            bool hasSpecialGoods = false;
            if (choices.TryGetValue(profile, out var available))
            {
                hasSelectionChoices = available.Any(choice => choice.Quota == null);
                hasSpecialGoods = available.Any(choice => choice.Quota != null);
                if (hasSpecialGoods)
                    ImportSavedToggle(profile.Id + SelectionEnabledSuffix, profile.Id + SpecialGoodsEnabledSuffix);
                bool hasSavedWeights = available.Any(choice => Enumerable.Range(0, choice.Categories.Length)
                    .Any(branch => positions.Get(profile.Id, StockAxis.Selection, choice.Key(branch)) != 0));
                if (hasSavedWeights)
                {
                    if (hasSelectionChoices)
                        ImportCheckedSwitch(profile.Id + SelectionEnabledSuffix);
                    if (hasSpecialGoods)
                        ImportCheckedSwitch(profile.Id + SpecialGoodsEnabledSuffix);
                }
            }
            profiles[profile] = new Options(config, profile.Id, CustomerSection, axes, profile, invertCustomers,
                hasSelectionChoices, hasSpecialGoods);
        }

        void ImportCheckedSwitch(string key)
        {
            if (savedValues != null && !savedValues.ContainsKey(key))
                savedValues[key] = "true";
        }

        // ModConfig reads persisted values when it registers a setting.
        // Seed the new key only when upgrading, so later edits to either switch remain independent.
        void ImportSavedToggle(string oldKey, string newKey)
        {
            if (savedValues != null && !savedValues.ContainsKey(newKey) && savedValues.TryGetValue(oldKey, out string oldValue))
                savedValues[newKey] = oldValue;
        }

        string[] SortCategories(IEnumerable<string> ids) => ids.OrderBy(id => StockCatalog.Items[id].Group)
            .ThenBy(id => StockCatalog.Items[id].Name, labelOrder)
            .ThenBy(id => id, StringComparer.Ordinal).ToArray();
    }

    private static void ImportInventorBackpackSettings(IDictionary<string, string>? savedValues)
    {
        if (savedValues == null)
            return;

        // The shared tier keeps the former in-shop keys. Backpacks were configured on the off-site tier.
        var savedPositions = new StockCategoryPositions(savedValues.TryGetValue(CategoryPositionsKey, out string current)
            ? current : "");
        var backpack = StockRules.InventorBackpack;
        bool movedPositions = false;
        foreach (var (outside, shared) in StockRules.LegacyInventorOutsideProfiles)
        {
            string selectionKey = shared + SelectionEnabledSuffix;
            if (!savedValues.ContainsKey(selectionKey)
                && savedValues.TryGetValue(outside + SelectionEnabledSuffix, out string enabled))
                savedValues.Add(selectionKey, enabled);

            for (int branch = 0; branch < backpack.Categories.Length; branch++)
            {
                string key = backpack.Key(branch);
                int previous = savedPositions.Get(outside, StockAxis.Selection, key);
                if (previous == 0)
                    continue;

                if (savedPositions.Get(shared, StockAxis.Selection, key) == 0)
                    savedPositions.Set(shared, StockAxis.Selection, key, previous);
                savedPositions.Set(outside, StockAxis.Selection, key, 0);
                movedPositions = true;
            }
        }

        if (movedPositions)
            savedValues[CategoryPositionsKey] = savedPositions.Save();
    }

    internal IReadOnlyList<string> CategoriesFor(StockProfile profile, StockAxis axis)
    {
        var items = categories[profile];
        return axis == StockAxis.Appearance ? items.Appearance : items.Quantity;
    }

    internal IReadOnlyList<StockChoice> ChoicesFor(StockProfile profile)
        => choices.TryGetValue(profile, out var available) ? available : (IReadOnlyList<StockChoice>)Array.Empty<StockChoice>();

    internal IReadOnlyList<StockChoice> ChoicesForProductDetails(StockProfile profile, string category)
        => ChoicesFor(profile).Where(choice => choice.ProductCategory == category || choice.Categories.Contains(category)).ToArray();

    internal bool HasProductRow(StockProfile profile, string category)
        => CategoriesFor(profile, StockAxis.Appearance).Contains(category) || CategoriesFor(profile, StockAxis.Quantity).Contains(category);

    internal bool UseLinearCurve(Draft? draft = null) => Read(linearChanges, draft);

    internal string? StockDetailsNotice(StockProfile profile)
        => supplyMethods.TryGetValue(profile, out var methods)
            && methods.Any(method => method.MethodName == nameof(StoreClientList.PlaceInventorInventory))
            ? Text("在杰克逊博士的外出商店，游戏会丢弃货架上摆不下的货物。Beta 版也按此规则处理。",
                "At Dr. Jackson's off-site shop, the game discards any goods that do not fit on the shelves. This also applies on the Beta branch.")
            : null;

    internal string ProductDetails(StockProfile profile, StockAxis axis, string category)
        => StockProductDetails.Describe(supplyMethods[profile], profile, axis, category, PreviewPositions(profile, null), UseLinearCurve());

    internal string ProductProbabilityDetails(StockProfile profile, StockAxis axis, string category)
        => StockProductPreview.Describe(supplyMethods[profile], profile, axis, category, PreviewPositions(profile, null), UseLinearCurve());

    internal string ProductComparison(StockProfile profile, StockAxis axis, string category, Draft? draft = null)
        => StockProductPreview.Compare(supplyMethods[profile], profile, axis, category, PreviewPositions(profile, draft), UseLinearCurve(draft));

    internal string? OptionalOfferSummary(StockProfile profile)
        => BarterMethod(profile)?.BarterOfferCounts is { } counts
            ? Line("固定提供 {0} 套报价。下方 {1} 套候选分别独立抽选，每抽中一套就增加一套可切换的报价。",
                "The customer always brings {0} fixed offers. Each of the {1} optional offers is rolled independently. Each success adds a separate offer.",
                counts.Fixed, counts.Optional)
            : null;

    internal (string Summary, string Offers)? OfferDetails(StockProfile profile)
    {
        var method = BarterMethod(profile);
        return method != null ? StockBarterDetails.Describe(method, profile, site => Get(method, profile, site), UseLinearCurve()) : null;
    }

    private StockMethod? BarterMethod(StockProfile profile)
        => supplyMethods.TryGetValue(profile, out var methods) ? methods.FirstOrDefault(method => method.BarterOfferCounts.HasValue) : null;

    private StockProductPreview.PositionReader PreviewPositions(StockProfile profile, Draft? draft)
        => (method, site, axisOverride, specificCategory) =>
            Get(profile, axisOverride ?? method.AxisFor(profile, site), method.Categories[site], specificCategory, draft);

    internal IEnumerable<(string Condition, double Original, double Modified)> ProductAppearanceScenarios(StockProfile profile, string category)
        => StockProductPreview.AppearanceScenarios(supplyMethods[profile], profile, category, PreviewPositions(profile, null), UseLinearCurve());

    internal bool HasProductSettings(StockProfile profile)
        => categories[profile].Appearance.Length != 0 || categories[profile].Quantity.Length != 0;

    internal int[]? GetChoicePositions(StockProfile profile, StockChoice choice, Draft? draft = null)
    {
        if (!ChoiceEnabled(profile, choice, draft) || UsesExactCounts(profile, choice, draft))
            return null;

        return ChoicePositions(profile, choice, draft?.Positions ?? positions);
    }

    internal bool ChoiceEnabled(StockProfile profile, StockChoice choice, Draft? draft = null)
        => Read(enabled, draft) && profiles.TryGetValue(profile, out var selected)
            && AllowsProfile(selected, draft) && ChoiceModeSelected(selected, choice.Quota != null, draft);

    internal bool ChoiceModeSelected(StockProfile profile, bool special, Draft? draft = null)
        => profiles.TryGetValue(profile, out var selected) && ChoiceModeSelected(selected, special, draft);

    private bool ChoiceModeSelected(Options options, bool special, Draft? draft = null)
        => special ? UsesMode(options.SpecialGoodsEnabled, allSpecialGoods, draft)
            : UsesMode(options.SelectionEnabled, allStockCategories, draft);

    internal bool UsesExactCounts(StockProfile profile, StockChoice choice, Draft? draft = null)
        => choice.Quota != null && (draft?.Positions ?? positions).Get(profile.Id, StockAxis.Allocation, choice.Id) != 0;

    // The positions store omits zero, so encode an explicit quantity as its value plus one.
    internal int StockCount(StockProfile profile, StockChoice choice, int branch, Draft? draft = null)
        => (draft?.Positions ?? positions).Get(profile.Id, StockAxis.Allocation, choice.Key(branch)) - 1;

    internal StockAllocation Allocation(StockProfile profile, StockChoice choice, Draft? draft = null)
        => new StockAllocation(choice, Enumerable.Range(0, choice.Categories.Length)
            .Select(branch => StockCount(profile, choice, branch, draft)).ToArray());

    internal StockAllocation? ActiveAllocation(StockProfile profile, StockChoice choice, IEnumerable<string> eligible)
    {
        if (!UsesExactCounts(profile, choice) || !ChoiceEnabled(profile, choice))
            return null;

        StockAllocation allocation = Allocation(profile, choice);
        return allocation.HasSpecifications && allocation.ValidationError(eligible) == null ? allocation : null;
    }

    internal string? AllocationError(Draft draft)
    {
        foreach (StockProfile profile in Customers)
            foreach (StockChoice choice in ChoicesFor(profile))
                if (UsesExactCounts(profile, choice, draft) && ChoiceEnabled(profile, choice, draft)
                    && Allocation(profile, choice, draft).ValidationError(StockChoiceDetails.EligibleCandidates(profile, choice)) is { } error)
                    return Line("「{0}」的{1}：{2}", "{0} · {1}: {2}", profile.Label, choice.Label, error);
        return null;
    }

    private static int[] ChoicePositions(StockProfile profile, StockChoice choice, StockCategoryPositions source)
        => Enumerable.Range(0, choice.Categories.Length)
            .Select(branch => source.Get(profile.Id, StockAxis.Selection, choice.Key(branch))).ToArray();

    internal IEnumerable<(StockChangeStatus Status, string Line)> GlobalChanges(Draft? draft = null)
    {
        foreach (ConfigEntry entry in Config.Entries.Where(entry => entry.Section == GlobalSection && entry.Key != InlinePreviewKey))
        {
            if (entry is ConfigEntry<bool> toggle)
            {
                bool next = Read(toggle, draft);
                if (next != toggle.Value)
                    yield return (StockChangeStatus.Pending, toggle.Label() + "　" + CheckChange(toggle.Value, next));
                else if (toggle.Value != toggle.Default)
                    yield return (GlobalStatus(toggle), toggle.Label() + "　" + CheckState(toggle.Value));
            }
            else if (entry is ConfigEntry<int> slider)
            {
                int next = draft?.Read(slider) ?? slider.Value;
                if (next != slider.Value)
                    yield return (StockChangeStatus.Pending, slider.Label() + "　" + PositionChange(slider.Value, next));
                else if (slider.Value != slider.Default)
                    yield return (GlobalStatus(slider), slider.Label() + "　" + FormatPosition(slider.Value));
            }
        }
    }

    private StockChangeStatus GlobalStatus(ConfigEntry entry)
    {
        if (entry == enabled)
            return StockChangeStatus.Effective;
        if (!enabled.Value || entry == defaults.Appearance && !appearanceEnabled.Value
            || entry == defaults.Quantity && !quantityEnabled.Value)
            return StockChangeStatus.Inactive;
        if (allIndividualCategories.Value && (entry == defaults.Appearance || entry == defaults.Quantity || entry == invertCategories))
            return StockChangeStatus.Superseded;
        if (entry == defaults.Appearance && !GlobalSliderAffectsStock(StockAxis.Appearance, entry.Key)
            || entry == defaults.Quantity && !GlobalSliderAffectsStock(StockAxis.Quantity, entry.Key))
            return StockChangeStatus.Inactive;
        return StockChangeStatus.Effective;
    }

    private bool GlobalSliderAffectsStock(StockAxis axis, string key)
    {
        var zeroGlobalSlider = new Draft(_ => null, candidate => candidate == key ? 0 : (int?)null, mask, positions);
        bool linear = UseLinearCurve();
        foreach (var profile in profiles)
        {
            if (!AllowsProfile(profile.Value))
                continue;
            var actual = PreviewPositions(profile.Key, null);
            var original = PreviewPositions(profile.Key, zeroGlobalSlider);
            if (CategoriesFor(profile.Key, axis).Any(item =>
                StockProductPreview.HasEffect(supplyMethods[profile.Key], profile.Key, axis, item, actual, linear, original)))
                return true;
        }
        return false;
    }

    internal IEnumerable<(StockProfile Profile, StockChangeStatus Status, IReadOnlyList<string> Lines)> CustomerChanges(Draft? draft = null)
    {
        StockCategoryMask? referenceMask = null;
        foreach (StockProfile profile in Customers)
        {
            if (!profiles.TryGetValue(profile, out Options options))
                continue;

            bool allowed = enabled.Value && AllowsProfile(options);
            bool individual = UsesMode(options.IndividualCategories, allIndividualCategories);
            bool ownSettings = options.Override?.Value == true;
            bool selection = ChoiceModeSelected(options, special: false);
            bool specialSelection = ChoiceModeSelected(options, special: true);
            bool classificationEnabled = allowed && selection;
            bool specialGoodsEnabled = allowed && specialSelection;
            var effective = new List<string>();
            var superseded = new List<string>();
            var inactive = new List<string>();
            var pending = new List<string>();
            var activeControls = new List<string>();
            var supersededControls = new List<string>();
            var inactiveControls = new List<string>();
            var pendingControls = new List<string>();
            var effects = new Dictionary<(StockAxis Axis, string Item), bool>();
            var positionReader = PreviewPositions(profile, null);
            bool linear = UseLinearCurve();
            bool configuredChoiceEffect = false;
            bool configuredSpecialEffect = false;
            bool overrideEffect = false;
            bool overrideSuperseded = false;
            bool savedCustomerDial = false;
            bool effectiveIndividualItem = false;
            var inheritedPositions = allowed && ownSettings && !individual ? WithToggle(options.Override!) : null;
            AddAxis(StockAxis.Appearance, Appearance, options.Appearance);
            AddAxis(StockAxis.Quantity, Quantity, options.Quantity);
            foreach (StockChoice choice in ChoicesFor(profile))
            {
                bool special = choice.Quota != null;
                bool choiceEnabled = special ? specialGoodsEnabled : classificationEnabled;
                bool exact = UsesExactCounts(profile, choice);
                int[] chosen = ChoicePositions(profile, choice, positions);
                bool[]? changed = chosen.Any(value => value != 0)
                    ? StockChoiceDetails.AffectedChoices(profile, choice, chosen, linear) : null;
                bool weightEffect = changed?.Any(value => value) == true;
                var activeItems = new List<string>();
                var inactiveItems = new List<string>();
                var pendingItems = new List<string>();
                for (int branch = 0; branch < choice.Categories.Length; branch++)
                {
                    string key = choice.Key(branch);
                    string item = StockCatalog.Name(choice.Categories[branch]);
                    int saved = chosen[branch];
                    int next = draft?.Positions.Get(profile.Id, StockAxis.Selection, key) ?? saved;
                    if (choiceEnabled && !exact && changed?[branch] == true)
                    {
                        activeItems.Add(item + "　" + (saved == 0
                            ? Text("受同组调整影响", "Affected by the group adjustment") : ChoiceLevel(FormatPosition(saved), special)));
                    }
                    else if (saved != 0)
                        inactiveItems.Add(item + "　" + ChoiceLevel(FormatPosition(saved), special));
                    if (next != saved)
                        pendingItems.Add(item + "　" + ChoiceLevel(PositionChange(saved, next), special));
                }
                string heading = choice.SettingsSection + " · " + choice.Label;
                AddGroup(effective, heading, activeItems);
                AddGroup(choiceEnabled && exact ? superseded : inactive, heading, inactiveItems);
                AddGroup(pending, heading, pendingItems);
                if (special)
                {
                    bool allocationEffect = AddAllocationChanges(choice, heading, exact, choiceEnabled, weightEffect);
                    configuredSpecialEffect |= !exact && weightEffect || allocationEffect;
                }
                else
                    configuredChoiceEffect |= weightEffect;
            }

            bool forcedIndividual = allIndividualCategories.Value && effectiveIndividualItem;
            bool modeEffect = !allIndividualCategories.Value && allowed && individual
                && AnyProductDifference(WithToggle(options.IndividualCategories!));
            bool customerSwitchEffect = enabled.Value && (allowed && (effective.Count != 0 || modeEffect)
                || options.Enabled is { } customer && customer.Value != mask.DefaultChecked
                    && (selection && configuredChoiceEffect || specialSelection && configuredSpecialEffect
                        || AnyProductDifference(WithToggle(customer))));
            AddSwitch(options.Enabled, customerSwitchEffect);
            AddSwitch(options.Override, overrideEffect, ownSettings && savedCustomerDial, overrideSuperseded);
            AddSwitch(options.IndividualCategories, modeEffect, isSuperseded: forcedIndividual);
            AddGroupSwitch(options.SelectionEnabled, classificationEnabled, allStockCategories.Value, configuredChoiceEffect);
            AddGroupSwitch(options.SpecialGoodsEnabled, specialGoodsEnabled, allSpecialGoods.Value, configuredSpecialEffect);
            effective.InsertRange(0, activeControls);
            superseded.InsertRange(0, supersededControls);
            inactive.InsertRange(0, inactiveControls);
            pending.InsertRange(0, pendingControls);
            if (effective.Count != 0)
                yield return (profile, StockChangeStatus.Effective, effective);
            if (pending.Count != 0)
                yield return (profile, StockChangeStatus.Pending, pending);
            if (superseded.Count != 0)
                yield return (profile, StockChangeStatus.Superseded, superseded);
            if (inactive.Count != 0)
                yield return (profile, StockChangeStatus.Inactive, inactive);

            void AddGroupSwitch(ConfigEntry<bool>? entry, bool active, bool forced, bool affectsStock)
                => AddSwitch(entry, !forced && active && affectsStock,
                    !forced && !active && affectsStock, forced && active && affectsStock);

            bool AddAllocationChanges(StockChoice choice, string heading, bool exact, bool choiceEnabled, bool weightEffect)
            {
                StockAllocation allocation = Allocation(profile, choice);
                string[] eligible = StockChoiceDetails.EligibleCandidates(profile, choice);
                bool valid = allocation.ValidationError(eligible) == null;
                bool affectsChoice = allocation.HasSpecifications && valid && eligible.Any(id => !allocation.IsRandom(id));
                bool applies = choiceEnabled && exact && affectsChoice;
                bool controlsResult = choiceEnabled && exact && valid && (affectsChoice || weightEffect);
                var activeItems = new List<string>();
                var inactiveItems = new List<string>();
                var pendingItems = new List<string>();
                if (controlsResult || exact && (allocation.HasSpecifications || weightEffect))
                    (controlsResult ? activeItems : inactiveItems).Add(StockAllocation.ModeLabel + "　" + CheckState(exact));
                bool nextExact = UsesExactCounts(profile, choice, draft);
                if (nextExact != exact)
                    pendingItems.Add(StockAllocation.ModeLabel + "　" + CheckChange(exact, nextExact));
                for (int branch = 0; branch < choice.Categories.Length; branch++)
                {
                    string id = choice.Categories[branch];
                    string item = StockCatalog.Name(id);
                    int saved = StockCount(profile, choice, branch);
                    int next = StockCount(profile, choice, branch, draft);
                    if (saved >= 0)
                        (applies && eligible.Contains(id) ? activeItems : inactiveItems)
                            .Add(item + "　" + StockAllocation.DescribeCount(saved));
                    if (next != saved)
                        pendingItems.Add(item + "　" + StockAllocation.DescribeCount(saved) + " → " + StockAllocation.DescribeCount(next));
                }
                heading += " · " + Text("指定数量", "Exact Quantities");
                AddGroup(effective, heading, activeItems);
                AddGroup(choiceEnabled && !exact ? superseded : inactive, heading, inactiveItems);
                AddGroup(pending, heading, pendingItems);
                return exact && affectsChoice;
            }

            bool ItemEffect(StockAxis axis, string item)
            {
                if (!allowed)
                    return false;
                if (!effects.TryGetValue((axis, item), out bool value))
                    effects[(axis, item)] = value = StockProductPreview.HasEffect(supplyMethods[profile], profile, axis, item, positionReader, linear);
                return value;
            }

            StockProductPreview.PositionReader WithToggle(ConfigEntry<bool> entry)
                => PreviewPositions(profile, new Draft(key => key == entry.Key ? !entry.Value : (bool?)null, _ => null, mask, positions));

            bool ProductDiffers(StockAxis axis, string item, StockProductPreview.PositionReader reference)
                => StockProductPreview.HasEffect(supplyMethods[profile], profile, axis, item, positionReader, linear, reference);

            bool AnyProductDifference(StockProductPreview.PositionReader reference)
                => CategoriesFor(profile, StockAxis.Appearance).Any(item => ProductDiffers(StockAxis.Appearance, item, reference))
                    || CategoriesFor(profile, StockAxis.Quantity).Any(item => ProductDiffers(StockAxis.Quantity, item, reference));

            void AddSwitch(ConfigEntry<bool>? entry, bool hasEffect, bool explainInactive = false, bool isSuperseded = false)
            {
                if (entry == null)
                    return;
                if (hasEffect || explainInactive || isSuperseded)
                    (hasEffect ? activeControls : isSuperseded ? supersededControls : inactiveControls)
                        .Add(entry.Label() + "　" + CheckState(entry.Value));
                bool next = Read(entry, draft);
                if (next != entry.Value)
                {
                    string label = entry == options.Enabled ? CustomerSwitchLabel(profile, Read(invertCustomers, draft)) : entry.Label();
                    pendingControls.Add(label + "　" + CheckChange(entry.Value, next));
                }
            }

            void AddAxis(StockAxis axis, string label, ConfigEntry<int>? ownPosition)
            {
                bool axisEnabled = allowed && (axis == StockAxis.Appearance ? appearanceEnabled : quantityEnabled).Value;
                string customerLabel = Line("「{0}」的{1}档位", "{1} adjustment for {0}", profile.Label, label);
                if (ownPosition != null)
                {
                    int saved = ownPosition.Value;
                    int next = draft?.Read(ownPosition) ?? saved;
                    bool overridesAxis = inheritedPositions != null && CategoriesFor(profile, axis)
                        .Any(item => ProductDiffers(axis, item, inheritedPositions));
                    overrideEffect |= overridesAxis;
                    bool applies = axisEnabled && ownSettings && !individual
                        && (overridesAxis || saved != 0 && CategoriesFor(profile, axis).Any(item => ItemEffect(axis, item)));
                    bool supersedesDial = axisEnabled && (individual || !ownSettings);
                    bool changedDial = saved != ownPosition.Default;
                    savedCustomerDial |= changedDial;
                    overrideSuperseded |= axisEnabled && individual && ownSettings && changedDial;
                    if (applies || changedDial)
                        (applies ? effective : supersedesDial ? superseded : inactive).Add(customerLabel + "　" + FormatPosition(saved));
                    if (next != saved)
                        pending.Add(customerLabel + "　" + PositionChange(saved, next));
                }

                var activeItems = new List<string>();
                var supersededItems = new List<string>();
                var inactiveItems = new List<string>();
                var pendingItems = new List<string>();
                foreach (string item in CategoriesFor(profile, axis))
                {
                    int saved = positions.Get(profile.Id, axis, item);
                    int next = draft?.Positions.Get(profile.Id, axis, item) ?? saved;
                    bool checkedItem = mask.IsChecked(profile.Id, axis, item);
                    bool nextChecked = draft?.Mask.IsChecked(profile.Id, axis, item) ?? checkedItem;
                    bool customizedCheck = checkedItem != mask.DefaultChecked;
                    if (saved == 0 && next == saved && !customizedCheck && nextChecked == checkedItem)
                        continue;

                    string name = StockCatalog.Name(item);
                    if (saved != 0)
                    {
                        bool applies = individual && ItemEffect(axis, item);
                        effectiveIndividualItem |= applies;
                        (applies ? activeItems
                            : axisEnabled && !individual ? supersededItems : inactiveItems).Add(name + "　"
                            + Line("独立档位 {0}", "Individual slider {0}", FormatPosition(saved)));
                    }
                    if (customizedCheck)
                        (axisEnabled && !individual && CheckboxAffectsStock(axis, item, checkedItem) ? activeItems
                            : axisEnabled && individual ? supersededItems : inactiveItems)
                            .Add(name + "　" + CheckState(checkedItem));
                    if (next != saved)
                        pendingItems.Add(name + "　" + Line("独立档位 {0}", "Individual slider {0}", PositionChange(saved, next)));
                    if (nextChecked != checkedItem)
                        pendingItems.Add(name + "　" + CheckChange(checkedItem, nextChecked));
                }
                AddGroup(effective, label, activeItems);
                AddGroup(superseded, label, supersededItems);
                AddGroup(inactive, label, inactiveItems);
                AddGroup(pending, label, pendingItems);
            }

            bool CheckboxAffectsStock(StockAxis axis, string item, bool isChecked)
            {
                referenceMask ??= new StockCategoryMask(mask.Save());
                referenceMask.SetChecked(profile.Id, axis, item, !isChecked);
                var reference = new Draft(_ => null, _ => null, referenceMask, positions);
                bool differs = ProductDiffers(axis, item, PreviewPositions(profile, reference));
                referenceMask.SetChecked(profile.Id, axis, item, isChecked);
                return differs;
            }
        }

        static void AddGroup(List<string> lines, string heading, List<string> changed)
        {
            if (changed.Count == 0)
                return;
            // Match the heading color used by the game's options window.
            lines.Add($"<color=#D1C8A5>{heading}</color>");
            lines.AddRange(changed);
        }

        static string ChoiceLevel(string position, bool special) => special
            ? Line("权重档位 {0}", "Weight adjustment {0}", position)
            : Line("分类档位 {0}", "Category adjustment {0}", position);
    }

    private static string CheckState(bool value) => value ? Text("已勾选", "Checked") : Text("未勾选", "Unchecked");
    private static string CheckChange(bool current, bool next) => CheckState(current) + " → " + CheckState(next);
    private static string PositionChange(int current, int next) => FormatPosition(current) + " → " + FormatPosition(next);

    internal static string FormatPosition(int position) => position.ToString("+0;-0;0", CultureInfo.InvariantCulture);

    internal static string CustomerSwitchLabel(StockProfile profile, bool inverted)
        => inverted ? Line("禁用「{0}」的调整", "Disable Adjustments for {0}", profile.Label)
            : Line("启用「{0}」的调整", "Enable Adjustments for {0}", profile.Label);

    internal static string CustomerSwitchTip(bool inverted)
        => inverted ? Text("勾选后停用该顾客的调整，取消勾选后启用。", "Check the box to disable adjustments for this customer. Clear it to enable them.")
            : Text("勾选后启用该顾客的调整，取消勾选后停用。", "Check the box to enable adjustments for this customer. Clear it to disable them.");

    internal static string CategorySwitchTip(bool inverted)
        => inverted ? Text("勾选某项后，停用该项的调整。取消勾选后，按适用档位调整。", "Check a box to disable that adjustment. Clear it to use the applicable slider setting.")
            : Text("勾选某项后，按适用档位调整。取消勾选后，停用该项的调整。", "Check a box to use the applicable slider setting. Clear it to disable that adjustment.");

    internal int Get(StockMethod method, StockAxis axis, int site = -1, string? category = null)
        => Get(method, axis, method.BeforeVisit ? method.Profiles[0].Id : CurrentStoreClient()?.identifier, site, category);

    internal int Get(StockMethod method, StockProfile profile, int site)
        => Get(profile, method.AxisFor(profile, site), method.Categories[site], null);

    internal bool UsesAdjustment(StockMethod method, string clientId)
    {
        for (int site = 0; site < method.Draws.Length; site++)
        {
            StockAxis axis = StockMethod.AxisFor(method.Draws[site]);
            if (axis != StockAxis.None && Get(method, axis, clientId, site) != 0)
                return true;
        }
        return false;
    }

    internal static StoreClient? CurrentStoreClient()
    {
        var store = PlayerStore.Instance;
        return store != null && store.playerLocation == PlayerStore.PlayerLocation.IN_STORE
            ? store.currentClientInstance?.GetClientBlueprint()
            : null;
    }

    internal int Get(StockMethod method, StockAxis axis, string? clientId, int site = -1, string? category = null)
    {
        if (clientId == null)
            return 0;

        foreach (StockProfile profile in method.Profiles)
            if (profile.Includes(clientId))
                return Get(profile, axis, site < 0 ? Array.Empty<string>() : method.Categories[site], category);

        return 0;
    }

    private int Get(StockProfile profile, StockAxis axis, string[] siteCategories, string? specificCategory, Draft? draft = null)
    {
        var axisEnabled = axis == StockAxis.Appearance ? appearanceEnabled : quantityEnabled;
        if (axis is not (StockAxis.Appearance or StockAxis.Quantity) || !Read(enabled, draft) || !Read(axisEnabled, draft)
            || !profiles.TryGetValue(profile, out var selected) || !AllowsProfile(selected, draft)
            || (axis == StockAxis.Appearance ? selected.Appearance : selected.Quantity) == null)
            return 0;

        if (UsesMode(selected.IndividualCategories, allIndividualCategories, draft))
            return GetCategoryPosition(draft?.Positions ?? positions, profile.Id, axis, siteCategories, specificCategory);

        StockCategoryMask activeMask = draft?.Mask ?? mask;
        bool inverted = Read(invertCategories, draft);
        if (specificCategory != null)
        {
            if (activeMask.IsChecked(profile.Id, axis, specificCategory) == inverted)
                return 0;
        }
        else
        {
            foreach (string category in siteCategories)
                if (activeMask.IsChecked(profile.Id, axis, category) == inverted)
                    return 0;
        }

        // A related customer can inherit the dial settings while keeping its own on/off switch.
        for (StockProfile? current = profile; current != null; current = current.DefaultProfile)
            if (profiles.TryGetValue(current, out var options) && options.Get(axis, draft) is int value)
                return value;

        return defaults.Get(axis, draft) ?? 0;
    }

    private static int GetCategoryPosition(StockCategoryPositions positions, string customer, StockAxis axis, string[] siteCategories,
        string? specificCategory)
    {
        if (specificCategory != null)
            return positions.Get(customer, axis, specificCategory);
        if (siteCategories.Length == 0)
            return 0;

        // One native roll can supply several categories together. Apply their shared position when they agree.
        int position = positions.Get(customer, axis, siteCategories[0]);
        for (int index = 1; index < siteCategories.Length; index++)
        {
            if (positions.Get(customer, axis, siteCategories[index]) != position)
                return 0;
        }
        return position;
    }

    private bool AllowsProfile(Options options, Draft? draft = null)
        => options.Enabled != null && Read(options.Enabled, draft) != Read(invertCustomers, draft);

    private static bool UsesMode(ConfigEntry<bool>? customer, ConfigEntry<bool> global, Draft? draft = null)
        => customer != null && (Read(global, draft) || Read(customer, draft));

    private static bool Read(ConfigEntry<bool> entry, Draft? draft) => draft?.Read(entry) ?? entry.Value;
}
