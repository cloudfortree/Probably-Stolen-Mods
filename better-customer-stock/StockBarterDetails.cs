using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static BetterCustomerStock.StockLanguage;

namespace BetterCustomerStock;

internal static class StockBarterDetails
{
    private static readonly (string Item, int Units)[][] FirstVisitOffers =
    {
        new[] { ("wire", 4) },
        new[] { ("blood_bag", 3) },
        new[] { ("processed_meat", 2), ("soda_red", 1) }
    };

    private static readonly (string Item, int Units)[][] RegularOffers =
    {
        new[] { ("wire", 3) },
        new[] { ("blood_bag", 2) },
        new[] { ("glass_shard_shiv", 1), ("pipe_weapon", 1), ("box_cutter", 1) }
    };

    internal static (string Summary, string Offers) Describe(StockMethod method, StockProfile profile, Func<int, int> position, bool linear)
    {
        var counts = method.BarterOfferCounts!.Value;
        var optional = Enumerable.Range(0, method.Draws.Length).Where(site => method.AxisFor(profile, site) == StockAxis.Appearance)
            .Select(site =>
            {
                double chance = method.BaseDraws[site]!.Value.Chance!.Value;
                return (Item: method.Categories[site][0], Original: chance, Modified: StockCurve.Probability(chance, position(site), linear));
            }).ToArray();
        var original = NumberOfOffers(optional.Select(offer => offer.Original));
        var modified = NumberOfOffers(optional.Select(offer => offer.Modified));
        string summary = Line(
            "原版总数 {0} → 当前总数 <b>{1}</b>\n固定报价 {2} 套。可选报价中，必出 {3} 套、独立抽选 {4} 套、排除 {5} 套。\n每次来访的报价套数期望值为 {6} → {7} 套。",
            "Vanilla total {0} → Current total <b>{1}</b>\nThere are {2} fixed offers. Of the optional offers, {3} are guaranteed, {4} are rolled independently, and {5} are excluded.\nExpected offers per visit {6} → {7}.",
            OfferCount(counts.Fixed + original.Guaranteed, counts.Fixed + original.Available),
            OfferCount(counts.Fixed + modified.Guaranteed, counts.Fixed + modified.Available), counts.Fixed,
            modified.Guaranteed, modified.Available - modified.Guaranteed, counts.Optional - modified.Available,
            Number(counts.Fixed + optional.Sum(offer => offer.Original)), Number(counts.Fixed + optional.Sum(offer => offer.Modified)));

        var fixedOffers = method.MethodName == nameof(BarterOfferList.CreateAddictBarter) ? FirstVisitOffers : RegularOffers;
        var fixedRows = fixedOffers.Select(items => Row(Contents(items), 1, 1));
        var optionalRows = optional.Select(offer => Row(OptionalContents(method.MethodName, offer.Item), offer.Original, offer.Modified));
        string offers = ProbabilityComparison + "\n\n" + Text("<b>固定报价</b>", "<b>Fixed Offers</b>") + "\n" + string.Join("\n", fixedRows)
            + "\n\n" + Text("<b>可选报价 · 分别独立抽选</b>", "<b>Optional Offers · Independent Rolls</b>")
            + "\n" + string.Join("\n", optionalRows);
        return (summary, offers);

        static (int Guaranteed, int Available) NumberOfOffers(IEnumerable<double> chances)
            => (chances.Count(chance => chance >= 1), chances.Count(chance => chance > 0));
    }

    internal static string OptionalContents(string method, string item) => item switch
    {
        "tools_trade" => Contents(new[] { ("wire_cutter", 1), ("screwdriver", 1) }),
        "packedFoodTable" => Line("{0} ×{1}（从「{2}」连续独立抽选 {1} 次）",
            "{0} ×{1} ({1} independent rolls from {2})", GameTerm("Mechanic/type_processed_food"),
            StockProductDetails.AppearanceBatchSize(method, item), GroupName("加工食品货表")),
        _ => ItemCount(item, StockProductDetails.AppearanceBatchSize(method, item))
    };

    private static string Row(string contents, double original, double modified)
        => $"{contents}　{StockProductPreview.Percent(original)} → <b>{StockProductPreview.Percent(modified)}</b>"
            + StockProductPreview.AppliedIfChanged(original, modified);

    private static string Contents(IEnumerable<(string Item, int Units)> items)
        => JoinList(items.Select(item => ItemCount(item.Item, item.Units)));

    private static string ItemCount(string item, int count) => Format("{0} ×{1}", StockCatalog.Name(item), count);

    private static string OfferCount(int minimum, int maximum)
        => minimum == maximum ? Line("{0} 套", minimum == 1 ? "{0} offer" : "{0} offers", minimum)
            : Line("{0}–{1} 套", "{0}–{1} offers", minimum, maximum);

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
