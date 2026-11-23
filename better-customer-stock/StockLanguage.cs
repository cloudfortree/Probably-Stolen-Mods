using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BetterCustomerStock;

internal static partial class StockLanguage
{
    internal static Func<string> ReadLocale { get; set; } = LocHelper.GetCurrentLocaleCode;
    internal static Func<string, string, string, object[], string>? ReadNativeTerm { get; set; } = LocHelper.GetLocalizedOrDefault;

    internal static bool IsChinese
    {
        get
        {
            string locale = ReadLocale();
            return string.Equals(locale, "zh", StringComparison.OrdinalIgnoreCase)
                || (locale?.StartsWith("zh-", StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }

    internal static string Text(string chinese, string english) => IsChinese ? chinese : english;

    internal static string Line(string chinese, string english, params object[] arguments)
        => Format(Text(chinese, english), arguments);

    internal static string Format(string pattern, params object[] arguments)
        => arguments.Length == 0 ? pattern : string.Format(CultureInfo.InvariantCulture, pattern, arguments);

    internal static string GameTerm(string reference, params object[] arguments)
    {
        var term = NativeTerms[reference];
        string fallback = Text(term.Chinese, term.English);
        if (ReadNativeTerm is { } read)
        {
            int separator = reference.IndexOf('/');
            string key = reference.Substring(separator + 1);
            string value = read(reference.Substring(0, separator), key, fallback, arguments);
            if (!string.IsNullOrWhiteSpace(value) && value != key)
                return IsChinese ? value.Replace(" (", "（").Replace('(', '（').Replace(')', '）') : value;
        }

        return Format(fallback, arguments);
    }

    internal static string ItemName(string id, string chinese)
        => !IsChinese && ItemNames.TryGetValue(id, out var name) ? name() : chinese;

    internal static string CustomerName(string id, string chinese)
    {
        if (!ClientNameKeys.TryGetValue(id, out string source))
            return chinese;

        string name = GameTerm(source);
        int opening = chinese.IndexOf('（');
        if (opening < 0)
            return name;

        string[] parts = chinese.Substring(opening + 1, chinese.Length - opening - 2)
            .Split(new[] { " · " }, StringSplitOptions.None);
        string qualifier = string.Join(" · ", parts.Select(part => CustomerQualifiers[part]()));
        return Line("{0}（{1}）", "{0} ({1})", name, qualifier);
    }

    internal static string GroupName(string chinese)
        => !IsChinese && GroupNames.TryGetValue(chinese, out var name) ? name() : chinese;

    internal static string JoinList<T>(IEnumerable<T> values) => string.Join(Text("、", ", "), values);

    internal static string ModName => Text("更好的顾客库存", "Better Customer Stock");
    internal static string ModDescription => Text(
        "按顾客调整出货概率、货物数量和分类权重。档位为 −100 到 +100，0 沿用原版。「特殊商品设置」可调整共用固定名额的货物权重或指定数量。",
        "Adjust each customer's stock chance, quantity, and category weights. Sliders range from −100 to +100, with 0 using vanilla behavior. Use Special Goods Settings to adjust weights or set exact quantities for groups with a shared number of slots.");

    internal static void LocalizeManifest(ModManifest manifest)
    {
        manifest.Name = ModName;
        manifest.Description = ModDescription;
    }

    internal static string GlobalSettings => Text("全局设置", "Global Settings");
    internal static string CustomerSettings => Text("顾客设置", "Customer Settings");
    internal static string Appearance => Text("出货概率", "Stock Chance");
    internal static string Quantity => Text("货物数量", "Stock Quantity");
    internal static string Classification => Text("货物分类", "Stock Categories");
    internal static string SpecialGoodsSettings => Text("特殊商品设置", "Special Goods Settings");
    internal static string ProductCategories => Text("商品类别", "Goods Settings");
    internal static string ProbabilityComparison => Text("<b>原版 → 模组</b>", "<b>Vanilla → Mod</b>");
    internal static string AppliedSuffix => Text("（已应用）", " (Applied)");
    internal static string UnappliedSuffix => Text("（未应用）", " (Not Applied)");

    internal static string Day(int day) => Line("第 {0} 天", "Day {0}", day);
    internal static string BeforeDay(int day) => Line("第 {0} 天前", "Before day {0}", day);
    internal static string FromDay(int day) => Line("第 {0} 天起", "Day {0} onward", day);
    internal static string DayRange(int first, int last) => Line("第 {0}–{1} 天", "Days {0}–{1}", first, last);
    internal static string BlackMarket => GameTerm("ClientName/faction_name_black_market");
    internal static string Security => GameTerm("ClientName/faction_name_security");
    internal static string WeaponPermit(int rank) => GameTerm("Item/item_permit_gun_" + rank + "_name");
    internal static string FactionReputation(string faction) => Line("{0}声望", "{0} reputation", faction);
    internal static string ReputationBelow(string faction, int amount)
        => Line("{0}低于 {1}", "{0} below {1}", FactionReputation(faction), amount);
    internal static string ReputationAtLeast(string faction, int amount)
        => Line("{0} {1} 及以上", "{0} {1} or higher", FactionReputation(faction), amount);

    internal static string StockAmount(string item, string count)
    {
        var (chinese, english) = item switch
        {
            "crate_random" or "crate_evidence_security" => ("个", "crate"),
            "scratcher_stack" => ("叠", "stack"),
            "tools_trade" => ("套", "set"),
            "nutrifruit_seed_packet" => ("包", "packet"),
            "desequencer" => ("台", "unit"),
            "blank_keycard" => ("张", "card"),
            _ => ("份", "unit")
        };
        return count + " " + Text(chinese, count == "1" ? english : english + "s");
    }

    internal static string StockAmount(int count) => StockAmount("", Format("{0}", count));

    internal static string StockRange(int first, int last) => first == last
        ? StockAmount(first) : StockAmount("", Format("{0}–{1}", first, last));

    internal static string GuaranteedStock(int count)
        => Line("固定库存数量为 {0}。", "Guaranteed stock is {0}.", StockAmount(count));

    internal static string RolledStock(string quantity)
        => Line("库存数量通过抽选确定，范围为 {0}。", "Stock quantity is rolled in the range of {0}.", quantity);

    internal static string RolledIncrease(string quantity)
        => Line("通过抽选，库存数量可增加 {0}。", "Rolls can increase stock by {0}.", quantity);

    internal static string StockIncrease(string quantity)
        => Line("库存数量增加 {0}。", "Stock increases by {0}.", quantity);

    internal static string StockIncreaseRolls(double chance, int times, int units)
    {
        string probability = StockProductPreview.Percent(chance);
        string quantity = StockAmount(units);
        return times == 1
            ? Line("按 {0} 抽选 {1} 次。抽中时库存数量增加 {2}。",
                "Roll once at {0}. A successful roll adds {2} to stock.", probability, times, quantity)
            : Line("按 {0} 连续独立抽选 {1} 次。每次抽中时库存数量增加 {2}。",
                "Make {1} independent rolls at {0} each. Each success adds {2} to stock.", probability, times, quantity);
    }

    internal static string ExpectedStock(string quantity)
        => Line("库存数量期望值为 <b>{0}</b>", "The expected stock quantity is <b>{0}</b>", quantity);
}
