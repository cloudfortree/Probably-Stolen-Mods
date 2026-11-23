using System;
using System.IO;
using System.Linq;
using System.Text;
using BetterCustomerStock;

internal static class TextAudit
{
    internal static void Write(StockMethod[] rules, string path)
    {
        var config = Verify.CreateConfig();
        var settings = new StockSettings(config, rules);
        string locale = "zh";
        string scenario = "vanilla";
        var readLocale = StockLanguage.ReadLocale;
        using var output = new StreamWriter(path, false, new UTF8Encoding(false));
        StockLanguage.ReadLocale = () => locale;
        int rows = 0;
        try
        {
            CaptureBothLanguages();
            scenario = "adjusted";
            Verify.EnableAllCustomers(config);
            Verify.SetConfig(config, StockSettings.CategoryKey, StockCategoryMask.AllChecked);
            Verify.SetConfig(config, "default.appearance", 50);
            Verify.SetConfig(config, "default.quantity", -50);
            Verify.SetConfig(config, "miner.quantity", -50);
            Verify.SetConfig(config, "thief.override", true);
            Verify.SetConfig(config, "thief.appearance", 50);
            var positions = new StockCategoryPositions("");
            foreach (StockProfile profile in settings.Customers.Where(profile => settings.ChoicesFor(profile).Count != 0))
            {
                var choices = settings.ChoicesFor(profile);
                if (choices.Any(choice => choice.Quota == null))
                    Verify.SetConfig(config, profile.Id + StockSettings.SelectionEnabledSuffix, true);
                if (choices.Any(choice => choice.Quota != null))
                    Verify.SetConfig(config, profile.Id + StockSettings.SpecialGoodsEnabledSuffix, true);
                foreach (StockChoice choice in choices)
                {
                    positions.Set(profile.Id, StockAxis.Selection, choice.Key(0), 50);
                    positions.Set(profile.Id, StockAxis.Selection, choice.Key(choice.Categories.Length - 1), -50);
                }
            }
            Verify.SetConfig(config, StockSettings.CategoryPositionsKey, positions.Save());
            var draft = new StockSettings.Draft(_ => null, key => key == "thief.appearance" ? 100 : (int?)null,
                new StockCategoryMask(StockCategoryMask.AllChecked), positions);
            CaptureBothLanguages(draft);
            scenario = "linear";
            Verify.SetConfig(config, StockSettings.LinearChangesKey, true);
            CaptureBothLanguages(draft);
        }
        finally
        {
            StockLanguage.ReadLocale = readLocale;
        }
        Console.WriteLine($"PASS exported {rows} formatted texts for review.");

        void CaptureBothLanguages(StockSettings.Draft? draft = null)
        {
            bool linear = settings.UseLinearCurve();
            foreach (string language in new[] { "zh", "en" })
            {
                locale = language;
                foreach (var entry in config.Entries)
                {
                    Add("设置名称", entry.Key, entry.Label());
                    if (entry.Tooltip != null)
                        Add("设置提示", entry.Key, entry.Tooltip());
                }
                foreach (string item in StockCatalog.Items.Keys)
                    Add("商品名称", item, StockCatalog.Name(item));
                Add("曲线计算", StockSettings.LinearChangesKey, string.Join("\n\n",
                    StockSettings.CurveIntro(linear), StockSettings.CurveFormula(false), StockSettings.CurveFormula(true),
                    StockSettings.CurveExample(), StockChoiceDetails.WeightCurveFormula(false), StockChoiceDetails.WeightModeNote(true)));
                foreach (StockProfile profile in settings.Customers)
                {
                    Add("顾客名称", profile.Id, profile.Label);
                    if (profile.FixedStockNotice != null)
                        Add("固定库存说明", profile.Id, profile.FixedStockNotice());
                    if (settings.OptionalOfferSummary(profile) is string offerSummary)
                        Add("易物报价提示", profile.Id, StockSettingsWindow.WithDetailHint(offerSummary));
                    if (settings.OfferDetails(profile) is { } offer)
                    {
                        Add("易物报价套数", profile.Id, offer.Summary);
                        Add("易物报价内容和概率", profile.Id, offer.Offers);
                    }
                    foreach (StockAxis axis in new[] { StockAxis.Appearance, StockAxis.Quantity })
                    {
                        string type = axis == StockAxis.Appearance ? "出货" : "数量";
                        foreach (string item in settings.CategoriesFor(profile, axis))
                        {
                            string id = profile.Id + "/" + item;
                            Add(type + "提示", id, StockSettingsWindow.WithDetailHint(settings.ProductDetails(profile, axis, item)));
                            Add(type + "概率明细", id, settings.ProductProbabilityDetails(profile, axis, item));
                            Add(type + "行内对照", id, settings.ProductComparison(profile, axis, item));
                        }
                    }
                    foreach (StockChoice choice in settings.ChoicesFor(profile))
                    {
                        string id = profile.Id + "/" + choice.Id;
                        Add("分类名称", id, choice.Label);
                        Add("分类提示·" + choice.Kind, id, StockSettingsWindow.WithDetailHint(StockChoiceDetails.HoverSummary(profile, choice)));
                        if (choice.Quota != null)
                        {
                            Add("分类权重曲线", id, StockChoiceDetails.WeightModeNote(linear));
                            Add("指定数量选项", id, StockAllocation.ModeLabel + "\n" + StockAllocation.QuantityTip);
                            int[] counts = Enumerable.Repeat(StockAllocation.Random, choice.Categories.Length).ToArray();
                            counts[0] = 1;
                            counts[counts.Length - 1] = 0;
                            Add("指定数量明细", id, StockChoiceDetails.AllocationDetails(profile, choice,
                                new StockAllocation(choice, counts), branch => counts[branch], true));
                        }
                        var current = settings.GetChoicePositions(profile, choice);
                        int[] levels = current ?? new int[choice.Categories.Length];
                        Add("分类行内对照", id, string.Join("\n", StockChoiceDetails.ProbabilityPreview(profile, choice, levels, current != null, linear)));
                        var appearance = choice.Id == "crate" && choice.ProductCategory is string category
                            ? settings.ProductAppearanceScenarios(profile, category) : null;
                        string[] pages = choice.Kind == StockChoiceKind.Backpack ? StockChoiceDetails.BackpackPages(choice) : new[] { "" };
                        for (int page = 0; page < pages.Length; page++)
                        {
                            string pageId = pages.Length == 1 ? id : id + "/" + page;
                            if (pages[page].Length != 0)
                                Add("背包持有选项", pageId, pages[page]);
                            Add("分类权重明细", pageId, StockChoiceDetails.DescribeReferenceWeights(profile, choice, levels, page, linear));
                            Add("分类概率明细·" + choice.Kind, pageId,
                                StockChoiceDetails.DescribeProbabilities(profile, choice, levels, current != null, appearance, page, linear));
                        }
                    }
                }
                foreach (var group in settings.GlobalChanges(draft).GroupBy(change => change.Status))
                    Add("全局总览·" + group.Key, "default", string.Join("\n", group.Select(change => change.Line)));
                foreach (var (profile, status, lines) in settings.CustomerChanges(draft))
                    Add("顾客总览·" + status, profile.Id, string.Join("\n", lines));
            }
        }

        void Add(string kind, string id, string content)
        {
            if (locale == "zh" && (content.Contains("(") || content.Contains(")") || content.Contains("[") || content.Contains("]")))
                throw new InvalidOperationException($"Chinese text contains half-width brackets: {kind} · {id} · {scenario}");
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
            output.WriteLine($"{kind}\t{id}\t{scenario}\t{locale}\t{encoded}");
            rows++;
        }
    }
}
