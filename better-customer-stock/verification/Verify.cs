using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BetterCustomerStock;
using HarmonyLib;

internal static class Verify
{
    private static int checks;

    private static int Main(string[] args)
    {
        string managed = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
        {
            string path = Path.Combine(managed, new AssemblyName(request.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try
        {
            Run(args.Contains("--install-patches"), args[1], args.Length > 3 ? args[3] : null);
            return 0;
        }
        catch (Exception error)
        {
            for (Exception? current = error; current != null; current = current.InnerException)
                Console.Error.WriteLine(current.GetType().FullName + " " + current.Message + "\n" + current.StackTrace);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(bool installPatches, string pagerPath, string? package)
    {
        StockLanguage.ReadLocale = () => "zh";
        StockLanguage.ReadNativeTerm = null;
        Console.WriteLine($"Game assembly {typeof(StoreClient).Assembly.Location}; native paging {StockRandom.HasNativePaging}.");
        TestCurve();
        TestChoiceCurve();
        var targets = TestTargets();
        Console.WriteLine("PASS original stock IL coverage and branch preservation.");
        TestDefaultSelections(targets.Select(target => target.Rule).ToArray());
        TestSettings(targets);
        TestCustomerChanges(targets.Select(target => target.Rule));
        TestGlobalStockModes(targets.Select(target => target.Rule).ToArray());
        TestLanguageAdaptation(targets.Select(target => target.Rule));
        if (Environment.GetEnvironmentVariable("BETTER_CUSTOMER_STOCK_TEXT_AUDIT") is { Length: > 0 } auditPath)
            TextAudit.Write(targets.Select(target => target.Rule).ToArray(), auditPath);
        Console.WriteLine("PASS old settings, separate category masks, inversion, customer overrides and signed slider labels.");
        Assembly? pager = File.Exists(pagerPath) ? TestPager(pagerPath, targets) : null;
        if (installPatches)
            TestHarmony(targets, pager);
        if (package != null)
            TestPackage(package, typeof(StoreClient).Assembly.Location, typeof(object).Assembly.Location);

        int profiles = targets.SelectMany(target => target.Rule.Profiles).Distinct().Count();
        int draws = targets.Sum(target => target.Rule.Draws.Count(draw => char.IsUpper(draw) && draw != 'E'));
        Console.WriteLine($"PASS {checks} checks, {targets.Count} supply methods, {profiles} client profiles, {draws} adjustable draws.");
    }

    private static void TestLanguageAdaptation(IEnumerable<StockMethod> rules)
    {
        string locale = "zh";
        StockLanguage.ReadLocale = () => locale;
        try
        {
            var config = CreateConfig();
            var settings = new StockSettings(config, rules);
            StockProfile thief = settings.Customers.Single(profile => profile.Id == "thief");
            StockProfile scavenger = settings.Customers.Single(profile => profile.Id == "scavAppraisalRandom");
            StockProfile jackson = settings.Customers.Single(profile => profile.Id == "inventorVisitInvestment2");
            const string sliderKey = "thief" + StockSettings.IndividualCategoriesSuffix;
            ConfigEntry slider = config.Get(sliderKey);
            Require(slider.Label() == "用滑动条逐项设置商品类别" && scavenger.Label == "拾荒客（鉴定服务）",
                "Chinese settings and composed customer names use Chinese");
            Require(config.Get(StockSettings.AllIndividualCategoriesKey).Label() == "统一使用滑动条替代"
                && config.Get(StockSettings.AllStockCategoriesKey).Label() == "开启全部货物分类"
                && config.Get(StockSettings.AllSpecialGoodsKey).Label() == "为全部顾客启用特殊商品设置"
                && config.Get("hypertech_smuggler" + StockSettings.SpecialGoodsEnabledSuffix).Label() == "启用特殊商品设置",
                "the Chinese global label distinguishes its scope from the customer switch");
            SetConfig(config, "default.appearance", 50);
            SetConfig(config, "thief.enabled", true);
            SetConfig(config, sliderKey, true);
            SetConfig(config, StockSettings.CategoryPositionsKey, "thief:appearance:turbo_booster_adv=50");
            string chineseProbabilities = settings.ProductProbabilityDetails(thief, StockAxis.Appearance, "turbo_booster_adv");
            string savedPosition = config.Get(StockSettings.CategoryPositionsKey).Serialize();

            locale = "en";
            Require(slider.Label() == "Use Sliders for Individual Goods Settings"
                && slider.Tooltip().Contains("in place of the global or customer setting"),
                "the same slider-mode entry switches label and description with the game setting");
            Require(scavenger.Label == "Scavenger (Appraisal Service)"
                && jackson.Label == "Dr. Jackson (Jackson Investment II)",
                "composite English names combine the native customer name with the right qualifier");
            Require(StockCatalog.Name("loot_crate_engineering") == "Engineering Foundry Supply Crate"
                && StockCatalog.Name("loot_crate_evidence") == "Security Department Evidence Locker"
                && StockCatalog.Name("turbo_booster_adv") == "Advanced Turbo Booster",
                "English goods use the installed game's terminology");
            Require(config.Get(StockSettings.CategoryPositionsKey).Serialize() == savedPosition
                && ((ConfigEntry<bool>)slider).Value, "changing language preserves applied checkbox and slider settings");
            string englishProbabilities = settings.ProductProbabilityDetails(thief, StockAxis.Appearance, "turbo_booster_adv");
            Require(englishProbabilities.Contains("Black Market reputation below 40")
                && englishProbabilities.Contains("Black Market reputation 100 or higher")
                && Percentages(chineseProbabilities).SequenceEqual(Percentages(englishProbabilities)),
                "the English reputation details preserve the same applied probabilities");
            var effective = settings.CustomerChanges().Single(change => change.Status == StockChangeStatus.Effective);
            Require(effective.Lines.Any(line => line.Contains("Use Sliders for Individual Goods Settings"))
                && effective.Lines.Any(line => line.Contains("Advanced Turbo Booster")),
                "the applied changes overview uses the same labels and stock names");

            foreach (ConfigEntry entry in config.Entries)
            {
                English(entry.Label());
                if (entry.Tooltip != null)
                    English(entry.Tooltip());
            }
            foreach (string item in StockCatalog.Items.Keys)
                English(StockCatalog.Name(item));

            int productRows = 0;
            int choiceGroups = 0;
            foreach (StockProfile profile in settings.Customers)
            {
                English(profile.Label);
                if (profile.FixedStockNotice != null)
                    English(profile.FixedStockNotice());
                if (settings.OptionalOfferSummary(profile) is string offerSummary)
                {
                    English(offerSummary);
                    English(settings.OfferDetails(profile)!.Value.Summary);
                    English(settings.OfferDetails(profile)!.Value.Offers);
                }
                foreach (StockAxis axis in new[] { StockAxis.Appearance, StockAxis.Quantity })
                {
                    foreach (string item in settings.CategoriesFor(profile, axis))
                    {
                        English(settings.ProductDetails(profile, axis, item));
                        English(settings.ProductProbabilityDetails(profile, axis, item));
                        English(settings.ProductComparison(profile, axis, item));
                        productRows++;
                    }
                }
                foreach (StockChoice choice in settings.ChoicesFor(profile))
                {
                    int[] positions = new int[choice.Categories.Length];
                    English(choice.Label);
                    English(StockChoiceDetails.HoverSummary(profile, choice));
                    English(StockChoiceDetails.DescribeReferenceWeights(profile, choice, positions, null));
                    English(StockChoiceDetails.DescribeProbabilities(profile, choice, positions, true));
                    if (choice.Kind == StockChoiceKind.Backpack)
                        foreach (string page in StockChoiceDetails.BackpackPages(choice))
                            English(page);
                    choiceGroups++;
                }
            }

            locale = "fr";
            StockLanguage.ReadNativeTerm = (table, key, fallback, arguments) => (table, key) switch
            {
                ("ClientName", "name_scavenger") => "Charognard",
                ("UI", "ui_nego_title_appraisal") => "Service d’évaluation",
                ("Item", "item_eng_box_name") => "Caisse de la fonderie",
                _ => ""
            };
            Require(scavenger.Label == "Charognard (Service d’évaluation)"
                && StockCatalog.Name("loot_crate_engineering") == "Caisse de la fonderie"
                && StockCatalog.Name("loot_crate_evidence") == "Security Department Evidence Locker"
                && slider.Label() == "Use Sliders for Individual Goods Settings",
                "another game language uses its native terms with English mod copy and term fallback");
            locale = "zh-Hans";
            StockLanguage.ReadNativeTerm = null;
            Require(slider.Label() == "用滑动条逐项设置商品类别" && scavenger.Label == "拾荒客（鉴定服务）",
                "switching back to Chinese reuses the same settings and customer recipe");
            StockLanguage.ReadNativeTerm = (table, key, fallback, _) => (table, key) switch
            {
                ("ClientName", "name_scavenger") => locale == "en" ? "Scavenger (Visitor)" : "拾荒客 (来访)",
                ("Item", "item_hypertech_electronic_name") => locale == "en" ? "Electronic Parts (Hypertech)" : "电子元件(超科技)",
                _ => fallback
            };
            Require(StockLanguage.GameTerm("ClientName/name_scavenger") == "拾荒客（来访）"
                && StockLanguage.GameTerm("Item/item_hypertech_electronic_name") == "电子元件（超科技）",
                "Chinese native terms use full-width parentheses in their displayed names");
            string curveTip = config.Get(StockSettings.LinearChangesKey).Tooltip();
            Require(curveTip.Contains("百分点数相同") && curveTip.Contains("保留随机抽选")
                && curveTip.Contains("25%") && curveTip.Contains("55%") && curveTip.Contains("点击查看")
                && !curveTip.Contains("÷"), "curve hover explains both modes and directs the player to formulas");
            Require(StockSettings.CurveFormula(false).Contains("÷ ［p × （100 + s）")
                && StockSettings.CurveFormula(true).Contains("p + （1 − p） × s ÷ 100")
                && StockChoiceDetails.WeightModeNote(false).Contains("（100 + s） ÷ （100 − s）"),
                "Chinese formulas use full-width parentheses");
            locale = "en";
            Require(StockLanguage.GameTerm("Item/item_hypertech_electronic_name") == "Electronic Parts (Hypertech)"
                && StockSettings.CurveFormula(false).Contains("÷ [p × (100 + s)")
                && StockSettings.CurveIntro(true).Contains("Linear Curve")
                && StockSettings.CurveIntro(false).Contains("Default Curve")
                && StockChoiceDetails.WeightModeNote(false).Contains("(100 + s) ÷ (100 − s)"),
                "English terms and formulas use half-width parentheses");
            Console.WriteLine($"PASS English for {StockCatalog.Items.Count} goods, {settings.Customers.Count} customer profiles, "
                + $"{productRows} stock rows and {choiceGroups} stock groups. Verified locale changes and native term fallback.");
        }
        finally
        {
            StockLanguage.ReadLocale = () => "zh";
            StockLanguage.ReadNativeTerm = null;
        }

        static IEnumerable<string> Percentages(string text)
            => Regex.Matches(text, @"\d+(?:\.\d+)?%").Cast<Match>().Select(match => match.Value);

        static void English(string text)
            => Require(!Regex.IsMatch(text, @"[\u3400-\u9fff]"), "English output contains Chinese text: " + text);
    }

    private static void TestChoiceCurve()
    {
        double[] original = { 21, 20, 20, 15, 25 };
        foreach (bool linear in new[] { false, true })
            for (int position = -100; position <= 100; position++)
                Require(StockCurve.ChoiceWeights(original, Enumerable.Repeat(position, 5).ToArray(), linear) == null,
                    "every common setting uses the original RNG path");
        double[] forced = StockCurve.ChoiceWeights(original, new[] { 100, 0, 0, 100, -100 })!;
        Near(21d / 36, forced[0], "equal maximum settings retain their original ratio");
        Near(15d / 36, forced[3], "two maximum settings take the pool when all others are nonpositive");
        Require(StockCurve.SelectChoice(forced, 0) == 0 && StockCurve.SelectChoice(forced, 1) == 3,
            "sample endpoints never select a zero weight");

        double[] shared = StockCurve.ChoiceWeights(original, new[] { 100, 99, 0, 100, -100 })!;
        Near(3960d / 11189, shared[1], "a third positive box takes its share below the maximum tier");
        Near(21d / 15, shared[0] / shared[3], "maximum categories maintain their ratio with a third competitor");
        Near(30d / 173, StockCurve.ChoiceWeights(original, new[] { 100, 0, 0, 50, 0 })![3],
            "documented +100 and +50 comparison");
        Near(2970d / 7229, StockCurve.ChoiceWeights(original, new[] { 100, 0, 0, 99, 0 })![3],
            "the +99 result approaches the common +100 result");
        double[] mixed = StockCurve.ChoiceWeights(original, new[] { 75, -50, 0, 50, 0 })!;
        Near(30915d / 126463, mixed[3], "documented redistribution into two finite positive layers");
        Near(20d / 731, mixed[1], "the negative setting reduces the base weight in the mixed example");
        var linearLayers = StockCurve.ChoiceWeights(original, new[] { 100, 0, 0, 50, 0 }, linear: true)!;
        Near(19d / 24, linearLayers[0], "+100 receives the full top half and its native share of the bottom half");
        Near(5d / 24, linearLayers[3], "+50 receives its native share of only the bottom half");

        double previous = -1;
        for (int setting = -100; setting <= 100; setting++)
        {
            double[] weights = StockCurve.ChoiceWeights(original, new[] { setting, 0, 0, 0, 0 })
                ?? original.Select(weight => weight / original.Sum()).ToArray();
            Near(1, weights.Sum(), "normalized class probability");
            Near(StockCurve.Probability(21d / 101, setting), weights[0], "a single nonzero setting preserves the old curve");
            Require(weights[0] > previous, "every slider step increases the selected relative weight");
            if (setting < 100)
                Near(20d / 15, weights[1] / weights[3], "unadjusted categories retain their relative proportions");
            previous = weights[0];
        }

        double[] eligible = StockCurve.ChoiceWeights(new[] { 0d, 1d, 2d }, new[] { 100, -100, 0 })!;
        Require(eligible.SequenceEqual(new[] { 0d, 0d, 1d }), "ineligible categories cannot force an outcome");
        Require(StockCurve.ChoiceWeights(new[] { 0d, 1d, 2d }, new[] { 100, -100, 0 }, linear: true)!.SequenceEqual(eligible),
            "linear layers cannot introduce unavailable candidates, including if all enabled settings are nonpositive");
        double[] shipments = StockCurve.SequentialWeights(new[] { 25d, 25d });
        Require(shipments.SequenceEqual(new[] { 0.25, 0.1875, 0.5625 }), "sequential game rolls use unconditional probabilities");
    }

    private static void TestPackage(string package, string gameAssembly, string coreAssembly)
    {
        string dll = Path.Combine(package, "BetterCustomerStock.dll");
        Require(File.ReadAllBytes(dll).SequenceEqual(File.ReadAllBytes(typeof(ModEntry).Assembly.Location)),
            "scanned package has the same build as the executed code");
        var gameAssemblies = new[] { gameAssembly, coreAssembly }.Select(Path.GetDirectoryName).Distinct()
            .SelectMany(folder => System.IO.Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly))
            .Select(Path.GetFileNameWithoutExtension);
        var scan = ModScanner.Scan(new[] { dll }, allowedReferences: gameAssemblies, folderPath: package, localFolder: true);
        Require(!scan.Refused && scan.Capabilities.Count == 0 && !scan.UsesDeprecatedCapability,
            "game's own scanner accepts the package with zero requested capabilities: " + scan.FormatFindings(25));
        Console.WriteLine($"PASS native ModScanner on the installation folder ({scan.MethodsScanned} methods).");
    }

    private static void TestCurve()
    {
        Near(0.0714285714285714, StockCurve.Probability(0.025, 50), "rare item at +50");
        Near(1.0 / 6, StockCurve.Probability(0.0625, 50), "booster at +50");
        Near(9.0 / 26, StockCurve.Probability(0.15, 50), "module at +50");
        Near(0.75, StockCurve.Probability(0.5, 50), "two extra ore at +50");
        Near(0.5, StockCurve.Probability(0.5, 0), "vanilla quantity");
        Near(0.25, StockCurve.Probability(0.5, -50), "two extra ore at -50");
        Near(0.05, StockCurve.Probability(0.10, -50, linear: true), "linear negative midpoint");
        Near(0.55, StockCurve.Probability(0.10, 50, linear: true), "linear positive midpoint");
        Require(StockCurve.Quantity(3, 6, 50, 0.60) == 5
            && StockCurve.Quantity(3, 6, 50, 0.60, linear: true) == 6,
            "the selected curve also affects multi-count thresholds");

        foreach (double baseline in new[] { 0.00001, 0.025, 0.0625, 0.15, 0.5, 0.99999 })
        {
            Near(0, StockCurve.Probability(baseline, -100), "minimum chance");
            Near(baseline, StockCurve.Probability(baseline, 0), "neutral chance");
            Near(1, StockCurve.Probability(baseline, 100), "maximum chance");
            for (int setting = -100; setting < 100; setting++)
            {
                Require(StockCurve.Probability(baseline, setting) < StockCurve.Probability(baseline, setting + 1),
                    "chance must be strictly increasing");
                Near(1 - StockCurve.Probability(1 - baseline, -setting),
                    StockCurve.Probability(baseline, setting), "symmetric scaling");
            }
        }

        for (int setting = -100; setting <= 100; setting++)
        {
            Near(0, StockCurve.Probability(0, setting), "impossible stock stays impossible");
            Near(1, StockCurve.Probability(1, setting), "guaranteed stock stays guaranteed");
            Require(StockCurve.Quantity(3, 3, setting, 0.5) == 3, "fixed quantity");
        }

        foreach (int setting in new[] { -100, -75, 0, 50, 100 })
        {
            var counts = new int[4];
            for (int i = 0; i < 10000; i++)
            {
                int quantity = StockCurve.Quantity(3, 6, setting, (i + 0.5) / 10000);
                Require(quantity >= 3 && quantity <= 6, "vanilla quantity range");
                counts[quantity - 3]++;
            }
            for (int threshold = 4; threshold <= 6; threshold++)
            {
                double expected = StockCurve.Probability((7 - threshold) / 4.0, setting);
                double observed = counts.Skip(threshold - 3).Sum() / 10000.0;
                Require(Math.Abs(expected - observed) <= 0.0001, "count probability at each threshold");
            }
        }
    }

    private static List<StockPatches.Target> TestTargets()
    {
        var targets = new List<StockPatches.Target>();
        var seen = new HashSet<MethodBase>();
        for (var i = 0; i < StockRules.Methods.Length; i++)
        {
            StockMethod rule = StockRules.Methods[i];
            var target = StockPatches.FindTarget(typeof(StoreClient).Assembly, rule, i)
                ?? throw new InvalidOperationException("No unique matching game method " + rule + " " + rule.Draws);
            Require(seen.Add(target.Method), "duplicate patch target");
            List<CodeInstruction> original = PatchProcessor.GetOriginalInstructions(target.Method);
            var result = StockPatches.Rewrite(original, target);
            Require(StockPatches.TryGetDraws(original, rule, out var sites), "same native call order as the category map");
            int expected = 0;
            int extra = 0;
            int tableCalls = original.Where((instruction, index) => index > 0
                && instruction.operand is MethodInfo method && IsTableCall(method)
                && rule.ChoiceForTable(original[index - 1].operand as string) != null).Count();
            expected += tableCalls;
            extra += tableCalls * 2;
            foreach (StockChoice choice in rule.Choices)
                Require(choice.Categories.All(id => StockCatalog.Items.ContainsKey(id)), "every selectable outcome has a localized label");
            for (int site = 0; site < rule.Draws.Length; site++)
            {
                char marker = rule.Draws[site];
                bool adjustable = StockMethod.AxisFor(marker) != StockAxis.None;
                Require(adjustable == (rule.Categories[site].Length != 0), "category entries match adjustable draws only");
                Require(rule.Categories[site].All(id => StockCatalog.Items.TryGetValue(id, out var item) && !string.IsNullOrWhiteSpace(item.Name)),
                    "every category uses a localized label");
                var choice = rule.ChoiceFor(site);
                if (choice == null && !adjustable && (marker != 'E' || !StockRandom.HasNativePaging))
                    continue;
                expected++;
                extra += marker == 'E' ? 1 : rule.MethodName == "RollButcherCount" ? 4
                    : marker == 'A' || choice?.Kind is StockChoiceKind.Inspectable or StockChoiceKind.Appraisal
                        or StockChoiceKind.List or StockChoiceKind.Backpack ? 3 : 2;
                if (choice?.Quota != null && choice.Kind is StockChoiceKind.List or StockChoiceKind.Range)
                    extra++;
            }
            Require(target.Applied, "missing stock rewrite");
            Require(result.Count == original.Count + extra, "net IL insertions");

            int originals = 0;
            int replacements = 0;
            for (var step = 0; step < original.Count; step++)
            {
                CodeInstruction before = original[step];
                CodeInstruction after = result[originals++];
                if (ReferenceEquals(before, after))
                    continue;

                Require(after.labels.SequenceEqual(before.labels), "branch labels intact");
                int site = sites.IndexOf(step);
                char marker = site >= 0 ? rule.Draws[site] : '\0';
                StockChoice? choice = site >= 0 ? rule.ChoiceFor(site) : null;
                Type? extraInput = null;
                if (marker == 'A')
                {
                    Require(after.opcode == OpCodes.Ldfld && ((FieldInfo)after.operand).FieldType == typeof(bool),
                        "advanced aquaculture condition still read from the original closure");
                    after = result[originals++];
                }
                else if (rule.MethodName == "RollButcherCount")
                {
                    Require(after.opcode == OpCodes.Ldarg_0 && result[originals++].opcode == OpCodes.Ldarg_1,
                        "butcher category uses the native count ranges");
                    after = result[originals++];
                }
                else if (choice?.Kind == StockChoiceKind.Backpack)
                {
                    Require(after.opcode == OpCodes.Ldloc_2, "Jackson's backpacks use the original ownership set");
                    extraInput = typeof(HashSet<string>);
                    after = result[originals++];
                }
                else if (choice?.Kind is StockChoiceKind.Inspectable or StockChoiceKind.Appraisal or StockChoiceKind.List)
                {
                    Require(after.IsLdloc(), "preserve the current native candidates");
                    extraInput = choice.Kind switch
                    {
                        StockChoiceKind.List => typeof(List<string>),
                        StockChoiceKind.Appraisal => typeof(List<AppraisalItemType>),
                        _ => typeof(System.Collections.IList)
                    };
                    after = result[originals++];
                }
                bool hasSlot = site < 0 || choice?.Quota != null && choice.Kind is StockChoiceKind.Range or StockChoiceKind.List;
                if (hasSlot)
                {
                    Require(site < 0 ? after.opcode == OpCodes.Ldc_I4 : after.IsLdloc(),
                        "quota slots use the static table order or native loop counter");
                    after = result[originals++];
                }
                Require(after.opcode == OpCodes.Ldc_I4 && (int)after.operand == i, "correct stock rule encoded");
                if (site >= 0 && marker != 'E')
                {
                    var categorySite = result[originals++];
                    Require(categorySite.opcode == OpCodes.Ldc_I4 && (int)categorySite.operand == site, "correct category encoded");
                }
                CodeInstruction replacement = result[originals++];
                var vanillaCall = (MethodInfo)before.operand;
                var replacementCall = (MethodInfo)replacement.operand;
                Require(replacementCall.DeclaringType == typeof(StockRandom), "only stock calls replaced");
                Require(replacementCall.ReturnType == vanillaCall.ReturnType, "native return type");
                IEnumerable<Type> nativeParameters = vanillaCall.GetParameters().Select(parameter => parameter.ParameterType);
                if (!vanillaCall.IsStatic)
                    nativeParameters = nativeParameters.Prepend(vanillaCall.DeclaringType!);
                if (marker == 'A')
                    nativeParameters = new[] { typeof(bool) };
                else if (rule.MethodName == "RollButcherCount")
                    nativeParameters = nativeParameters.Concat(new[] { typeof(int), typeof(int) });
                if (extraInput != null)
                    nativeParameters = nativeParameters.Append(extraInput);
                if (hasSlot)
                    nativeParameters = nativeParameters.Append(typeof(int));
                nativeParameters = nativeParameters.Append(typeof(int));
                if (site >= 0 && marker != 'E')
                    nativeParameters = nativeParameters.Append(typeof(int));
                Require(nativeParameters.SequenceEqual(replacementCall.GetParameters().Select(parameter => parameter.ParameterType)),
                    "native argument types kept");
                replacements++;
            }
            Require(replacements == expected, "precise replacement count");
            targets.Add(target);
            Console.WriteLine($"{target.Method.DeclaringType!.FullName}:{target.Method.Name} | {rule.MethodName} | {rule.Draws}");
        }

        var thief = targets.Single(target => target.Rule.Profiles.Any(profile => profile.Id == "thief"));
        var thiefIl = PatchProcessor.GetOriginalInstructions(thief.Method);
        var afterThief = StockPatches.Rewrite(thiefIl, thief);
        Require(afterThief.Count(instruction => instruction.operand is MethodInfo method && method.Name == nameof(StockRandom.ChoiceRange)) == 2,
            "both native crate routes share the conditional choice");
        Require(afterThief.Count(instruction => instruction.operand is MethodInfo method && method.Name == nameof(StockRandom.ChoiceInt)) == 1,
            "fallback choice is separate from appearance and crate type");

        string[] fixedJackson = { nameof(StoreClientListStory.CreateInventorIntro), nameof(StoreClientListStory.CreateInventorModuleIntro),
            nameof(StoreClientList.CreateInventorStorage), nameof(StoreClientList.PlaceInventorSpecialStock) };
        Require(targets.All(target => fixedJackson.All(name => !target.Method.Name.Contains(name))),
            "Jackson's teaching, expansion and Monday shipments keep their own native routines");
        string fallbackCheck = StockRandom.HasNativePaging ? "IsStockEmpty" : "IsFrontCounterEmptyNonOwned";
        Require(afterThief.Count(instruction => instruction.operand is MethodInfo call && call.Name == fallbackCheck) == 1,
            "native empty counter check retained via the applicable path");

        var changed = thiefIl.ToList();
        changed.Insert(0, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RNG), "Roll", new[] { typeof(int) })));
        Require(ReferenceEquals(changed, StockPatches.Rewrite(changed, thief)), "changed native draw layout falls back to vanilla");
        return targets;

        static bool IsTableCall(MethodInfo method)
            => (method.DeclaringType == typeof(ItemSpawner) && method.Name == nameof(ItemSpawner.SpawnFromTable))
                || (method.DeclaringType == typeof(TableMaster) && method.Name == nameof(TableMaster.Roll));
    }

    private static void TestSettings(IEnumerable<StockPatches.Target> targets)
    {
        ModConfig config = CreateConfig();
        var previous = (Dictionary<string, string>)AccessTools.Field(typeof(ModConfig), "rawValues").GetValue(config);
        previous.Add("default.appearance", "61");
        previous.Add("default.quantity", "-33");
        previous.Add("default.detailed_stock_tooltips", "true");
        previous.Add("scavGeneral.override", "true");
        previous.Add("scavGeneral.appearance", "-88");
        previous.Add(StockSettings.CategoryPositionsKey,
            "thief:selection:crate/loot_crate_security=50,shadyPharma:selection:shipment/pink_injector=100");
        previous.Add("shadyPharma" + StockSettings.SelectionEnabledSuffix, "false");
        previous.Add(StockSettings.CategoryKey, StockCategoryMask.AllChecked);
        var rules = targets.Select(target => target.Rule).ToArray();
        var settings = new StockSettings(config, rules);
        EnableAllCustomers(config);
        var scav = rules.Single(rule => rule.MethodName == "AddScavGeneralStock");
        var thief = rules.Single(rule => rule.MethodName == "CreateThief");
        var miner = rules.Single(rule => rule.MethodName == "CreateMiner");
        var material = rules.Single(rule => rule.MethodName == "AddScavMaterialStock").Profiles.Single();
        var gunsmith = rules.Single(rule => rule.MethodName == "CreateGunsmith");
        var earlyVisits = rules.Where(rule => rule.BeforeVisit).ToArray();

        int described = 0;
        int classifications = 0;
        foreach (StockProfile profile in settings.Customers)
        {
            foreach (StockAxis axis in new[] { StockAxis.Appearance, StockAxis.Quantity })
                foreach (string category in settings.CategoriesFor(profile, axis))
                {
                    string detail = settings.ProductDetails(profile, axis, category);
                    Require(detail.Contains("<b>原版</b>") && detail.Contains("<b>修正</b>"),
                        "each product row has an original quantity and adjustment scope");
                    Require(!detail.Contains("跳过随机批次") && !detail.Contains("取可调部分的下限"),
                        $"detail for {profile.Id}/{axis}/{category} omits the common endpoint instructions");
                    string preview = settings.ProductComparison(profile, axis, category);
                    Require(preview.Contains(" → ") && !preview.Contains('\n') && (axis != StockAxis.Appearance || preview.Contains('%')),
                        $"one-line comparison for {profile.Id}/{axis}/{category}");
                    string[] summary = detail.Split(new[] { "\n\n<b>修正</b>" }, StringSplitOptions.None);
                    Require(summary.Length == 2 && summary[0].Contains("%")
                        && (summary[1].Contains("%") || summary[1].Contains("未应用")
                            || (profile == miner.Profiles[0] && summary[1].Contains("库存数量期望值"))),
                        $"compact numeric coverage for {profile.Id}/{axis}/{category}");
                    string probabilities = settings.ProductProbabilityDetails(profile, axis, category);
                    Require(probabilities.Contains("原版 → 模组") && probabilities.Contains("%"),
                        $"detailed probability coverage for {profile.Id}/{axis}/{category}");
                    VerifyProbabilityStages(probabilities, $"{profile.Id}/{axis}/{category}");
                    described++;
                }
            foreach (StockChoice choice in settings.ChoicesFor(profile))
            {
                var positions = new int[choice.Categories.Length];
                var weights = StockChoiceDetails.WeightPreview(profile, choice, positions, true);
                Require(weights.Weights.Length == positions.Length && weights.Weights.All(value => value.Contains(" → ") && !value.Contains("%")),
                    "every classification group renders original and modified weights without probabilities");
                string[] comparison = StockChoiceDetails.ProbabilityPreview(profile, choice, positions, true);
                Require(comparison.Length == positions.Length && comparison.All(value => value.Contains("% → ") && value.EndsWith("%")),
                    $"each candidate has a compact percentage in {profile.Id}/{choice.Id}");
                string reference = StockChoiceDetails.DescribeReferenceWeights(profile, choice, positions, null);
                Require(reference.StartsWith(weights.Caption) && weights.Weights.All(weight => reference.Contains(weight)),
                    $"choice reference and weights remain available in the details for {profile.Id}/{choice.Id}");
                string probabilities = StockChoiceDetails.DescribeProbabilities(profile, choice, positions, true);
                Require(probabilities.Contains("原版 → 模组") && probabilities.Contains("%"),
                    "every classification group renders original and modified probabilities");
                VerifyProbabilityStages(probabilities, $"{profile.Id}/{choice.Id}");
                classifications++;
            }
        }
        Require(config.Get("default.detailed_stock_tooltips") == null, "legacy detail preferences no longer register a setting");
        Require(config.Get(StockSettings.InlinePreviewKey) is ConfigEntry<bool> { Default: true, Value: true } inline
            && inline.Section == "全局设置", "inline previews are enabled by default in global settings");
        Require(config.Get(StockSettings.LinearChangesKey) is ConfigEntry<bool> linearOption && !linearOption.Value
            && !(bool)linearOption.BoxedDefault && linearOption.Section == "全局设置",
            "upgraded configurations keep the default curve until linear changes are applied");
        var oreProfile = miner.Profiles[0];
        string savedOreDetail = settings.ProductDetails(oreProfile, StockAxis.Quantity, "common_ore");
        string savedOreProbabilities = settings.ProductProbabilityDetails(oreProfile, StockAxis.Quantity, "common_ore");
        var draftToggles = new Dictionary<string, bool>();
        var draftPositions = new Dictionary<string, int> { ["default.quantity"] = 50, ["default.appearance"] = 50 };
        var previewDraft = new StockSettings.Draft(key => draftToggles.TryGetValue(key, out bool value) ? value : null,
            key => draftPositions.TryGetValue(key, out int value) ? value : null,
            new StockCategoryMask(StockCategoryMask.AllChecked), new StockCategoryPositions(""));
        Require(settings.ProductComparison(oreProfile, StockAxis.Quantity, "common_ore", previewDraft) == "3.5–6 → 3.75–7.5 份"
            && settings.ProductDetails(oreProfile, StockAxis.Quantity, "common_ore") == savedOreDetail
            && settings.ProductProbabilityDetails(oreProfile, StockAxis.Quantity, "common_ore") == savedOreProbabilities
            && ((ConfigEntry<int>)config.Get("default.quantity")).Value == -33,
            "only the inline ore comparison follows the unapplied quantity slider");
        SetConfig(config, "default.quantity", 50);
        string oreDetail = settings.ProductDetails(oreProfile, StockAxis.Quantity, "common_ore");
        string oreProbability = settings.ProductProbabilityDetails(oreProfile, StockAxis.Quantity, "common_ore");
        Require(oreDetail.Contains("3–4 份") && oreDetail.Contains("第 30 天起，按 50% 抽选 1 次。抽中时库存数量增加 2 份")
            && oreDetail.Contains("3.5、4、5、5.5、6 份") && oreDetail.Contains("3.75、4.5、6、6.75、7.5 份"),
            "after applying the slider, ore help compares original and adjusted expectation at all five stages");
        Require(System.Text.RegularExpressions.Regex.Matches(oreProbability, @"\b\d+ 份 \d+(?:\.\d+)?% →").Count == 23,
            "ore probability detail includes all 23 possible stage and quantity pairs");
        Require(oreProbability.Contains("3 份 50% → 25%") && oreProbability.Contains("9 份 3.13% → 23.73%"),
            "ore probability detail keeps the first and last outcome probabilities");
        foreach (int slider in new[] { -100, -50, 0, 50, 100 })
        {
            SetConfig(config, "default.quantity", slider);
            VerifyMinerOutcomes(settings.ProductProbabilityDetails(oreProfile, StockAxis.Quantity, "common_ore"), slider);
        }
        Require(settings.ProductDetails(oreProfile, StockAxis.Quantity, "common_ore").Contains("4、5、7、8、9 份"),
            "applying the maximum ore slider gives the five unlocked upper limits");
        SetConfig(config, "default.quantity", 0);
        Require(settings.ProductDetails(oreProfile, StockAxis.Quantity, "common_ore")
            .Contains("各天数档的库存数量期望值依次为 <b>3.5、4、5、5.5、6 份"),
            "applying the neutral ore slider preserves native expected quantities");
        SetConfig(config, "default.quantity", -33);
        draftPositions["default.quantity"] = 0;
        Require(settings.ProductComparison(oreProfile, StockAxis.Quantity, "common_ore", previewDraft) == "3.5–6 → 3.5–6 份",
            "ore default previews native expected quantity");
        draftPositions["default.quantity"] = 100;
        draftToggles[StockSettings.InvertCustomerKey] = true;
        draftToggles["miner.enabled"] = true;
        draftToggles["miner" + StockSettings.IndividualCategoriesSuffix] = true;
        previewDraft.Positions.Set("miner", StockAxis.Quantity, "common_ore", 100);
        Require(settings.ProductComparison(oreProfile, StockAxis.Quantity, "common_ore", previewDraft) == "3.5–6 → 3.5–6 份",
            "the inverted miner switch overrides the individual +100 draft");
        draftToggles["miner.enabled"] = false;
        Require(settings.ProductComparison(oreProfile, StockAxis.Quantity, "common_ore", previewDraft) == "3.5–6 → 4–9 份",
            "clearing the miner disable switch immediately previews the individual +100 draft");
        draftToggles.Clear();
        var thiefProfile = thief.Profiles[0];
        SetConfig(config, "default.appearance", 50);
        string boxDetail = settings.ProductDetails(thiefProfile, StockAxis.Appearance, "crate_random");
        Require(boxDetail.Contains("有 <b>30%–100%</b> 的概率抽中 <b>1 个</b>，其余 <b>0%–70%</b> 为 0 个")
            && boxDetail.Contains("当前有 <b>56.25%–100%</b>") && boxDetail.Contains("达到 <b>40</b> 后，固定带 1 个"),
            "crate preview honors the original guaranteed stock at reputation 40");
        string boosterSummary = settings.ProductDetails(thiefProfile, StockAxis.Appearance, "turbo_booster_adv");
        Require(StockReputationRules.HasNativeTiers, "reputation tiers were read from the same native method used by sellers");
        Require(boosterSummary.Contains("有 <b>5%–12.5%</b> 的概率抽中 <b>1 份</b>，其余 <b>87.5%–95%</b> 为 0 份")
            && boosterSummary.Contains("当前有 <b>13.64%–30%</b>")
            && boosterSummary.Contains("达到 <b>40、60、80、100</b>") && boosterSummary.Contains("×1.25、×1.5、×1.75、×2.5"),
            "booster summary states both probability ranges and the native reputation thresholds");
        var rare = rules.Single(rule => rule.MethodName == nameof(StoreClientList.CreateLowerLevelRareMerchant)).Profiles.Single();
        string extractor = settings.ProductDetails(rare, StockAxis.Appearance, "module_extractor_advanced");
        Require(extractor.Contains("有 <b>33%</b> 的概率抽中 <b>1 份</b>，其余 <b>67%</b> 为 0 份")
            && extractor.Contains("当前有 <b>59.64%</b>") && !extractor.Contains("满足以下"),
            "unconditional extractor shows native and adjusted odds with no condition list");
        Require(settings.ProductComparison(rare, StockAxis.Appearance, "module_extractor_advanced", previewDraft) == "33% → 59.64%",
            "the inline appearance preview uses the summary's reference probability");
        string cotton = settings.ProductDetails(scav.Profiles[0], StockAxis.Appearance, "kotton_seed");
        Require(cotton.Contains("须先解锁耕作供货") && cotton.Contains("的概率抽中 <b>2 份</b>") && !cotton.Contains("满足以下"),
            "compact appearance odds keep gated multi-item batch sizes");
        string bloomberry = settings.ProductDetails(scav.Profiles[0], StockAxis.Appearance, "bloomberry_seed");
        Require(bloomberry.Contains("<b>满足以下全部条件</b>\n• 已解锁耕作供货\n• 已解锁酿酒供货")
            && !bloomberry.Contains("满足以下任意一个条件"), "both seed unlocks are required in one flat condition list");
        var requisition = rules.Single(rule => rule.MethodName == nameof(StoreClientList.CreateSecSurplusMerchant)).Profiles.Single();
        string ammo = settings.ProductDetails(requisition, StockAxis.Appearance, "heavy_pistol_ammo");
        Require(ammo.Contains("<b>满足以下任意一个条件</b>\n• 持有 I级武器许可证\n• 持有 II级武器许可证")
            && !ammo.Contains("满足以下全部条件"), "either permit qualifies for the appearance roll");
        string ammoQuantities = settings.ProductProbabilityDetails(requisition, StockAxis.Quantity, "heavy_pistol_ammo");
        Require(ammoQuantities.Contains("<b>满足以下全部条件</b>\n• 持有 I级武器许可证\n• 持有 II级武器许可证"),
            "ammo details identify the combined permits for additional quantities");
        foreach (string item in new[] { "stun_gun", "tazer", "pink_injector" })
        {
            string summary = settings.ProductDetails(requisition, StockAxis.Quantity, item);
            Require(summary.Contains(item == "pink_injector" ? "50%–100%" : "15%–100%")
                && summary.Contains("声望达到 <b>80</b> 后，该批固定带 1 份"),
                "requisition summaries explain the native reputation guarantee for each affected item");
        }
        string boosterDetail = settings.ProductProbabilityDetails(thiefProfile, StockAxis.Appearance, "turbo_booster_adv");
        Require(boosterDetail.Contains("黑市声望低于 40") && boosterDetail.Contains("黑市声望 40–59")
            && boosterDetail.Contains("黑市声望 60–79") && boosterDetail.Contains("黑市声望 80–99")
            && boosterDetail.Contains("黑市声望 100 及以上") && boosterDetail.Contains("5% → 13.64%")
            && boosterDetail.Contains("6.25% → 16.67%") && boosterDetail.Contains("7.5% → 19.57%")
            && boosterDetail.Contains("8.75% → 22.34%") && boosterDetail.Contains("12.5% → 30%"),
            "booster shows each native reputation multiplier and adjustment");
        draftToggles[StockSettings.LinearChangesKey] = true;
        Require(settings.ProductComparison(thiefProfile, StockAxis.Appearance, "turbo_booster_adv", previewDraft)
                == "5%–12.5% → 52.5%–56.25%"
            && settings.ProductDetails(thiefProfile, StockAxis.Appearance, "turbo_booster_adv") == boosterSummary
            && settings.ProductProbabilityDetails(thiefProfile, StockAxis.Appearance, "turbo_booster_adv") == boosterDetail,
            "only the inline comparison follows the unapplied curve switch");
        SetConfig(config, StockSettings.LinearChangesKey, true);
        Require(settings.ProductDetails(thiefProfile, StockAxis.Appearance, "turbo_booster_adv").Contains("当前有 <b>52.5%–56.25%</b>")
            && settings.ProductProbabilityDetails(thiefProfile, StockAxis.Appearance, "turbo_booster_adv").Contains("12.5% → 56.25%"),
            "applying the linear switch refreshes both kinds of detail");
        SetConfig(config, StockSettings.LinearChangesKey, false);
        draftToggles.Clear();
        draftToggles["default.appearance.enabled"] = false;
        Require(settings.ProductComparison(thiefProfile, StockAxis.Appearance, "crate_random", previewDraft) == "30%–100% → 30%–100%"
            && settings.ProductDetails(thiefProfile, StockAxis.Appearance, "crate_random") == boxDetail,
            "an unapplied appearance switch only changes the inline crate range");
        SetConfig(config, "default.appearance.enabled", false);
        Require(settings.ProductDetails(thiefProfile, StockAxis.Appearance, "crate_random").Contains("（未应用）")
            && !settings.ProductProbabilityDetails(thiefProfile, StockAxis.Appearance, "crate_random").Contains("（已应用）"),
            "a disabled applied adjustment restores the native range");
        string scrapDetail = settings.ProductDetails(material, StockAxis.Quantity, "scrap_metal");
        Require(scrapDetail.Contains("固定库存数量为 1 份")
            && scrapDetail.Contains("按 50% 连续独立抽选 4 次。每次抽中时库存数量增加 1 份"),
            "material scavenger descriptions distinguish repeated rolls from a multi-unit roll");
        var retiredGunsmith = rules.Single(rule => rule.MethodName == "CreateRetiredGunsmith").Profiles.Single();
        string retiredChip = settings.ProductProbabilityDetails(retiredGunsmith, StockAxis.Appearance, "printer_chip_rds");
        Require(retiredChip.Contains("第 20 天前</b>　携带 <b>15%")
            && retiredChip.Contains("第 20 天起</b>　携带 <b>40%"),
            "only one of the retired gunsmith's two native chip rolls applies");
        StockChoice crates = thief.Choices.Single(choice => choice.Id == "crate");
        StockChoice fallback = thief.Choices.Single(choice => choice.Id == "fallback");
        Require(StockChoiceDetails.HoverSummary(thiefProfile, fallback) == StockChoiceDetails.Summary(thiefProfile, fallback)
            && StockChoiceDetails.DescribeProbabilities(thiefProfile, fallback, new int[2], true).Contains("未触发柜台补货"),
            "fallback hover states the trigger once and the detailed outcomes name the fallback decision");
        Require(StockCatalog.Items["crate_random"].Name == "任意随机物资箱" && crates.ProductCategory == "crate_random"
            && settings.ChoicesForProductDetails(thiefProfile, "crate_random").SequenceEqual(new[] { crates }),
            "the thief's appearance and classification share one crate detail destination");
        Require(settings.ChoicesForProductDetails(scav.Profiles[0], "packedFoodTable").Single().Id == "packedFoodTable"
            && settings.CategoriesFor(scav.Profiles[0], StockAxis.Appearance).Contains("packedFoodTable")
            && settings.CategoriesFor(scav.Profiles[0], StockAxis.Quantity).Contains("packedFoodTable"),
            "the same product ID links the food table, appearance and quantity sections");
        int[] twoBoosts = { 100, 0, 0, 50, 0 };
        var boosted = StockChoiceDetails.WeightPreview(thiefProfile, crates, twoBoosts, true);
        var disabled = StockChoiceDetails.WeightPreview(thiefProfile, crates, twoBoosts, false);
        Require(StockChoiceDetails.ProbabilityPreview(thiefProfile, crates, twoBoosts, true)[0] == "20.79% → 82.66%"
            && StockChoiceDetails.ProbabilityPreview(thiefProfile, crates, twoBoosts, false)[0] == "20.79% → 20.79%",
            "the compact crate classification preview is conditional on carrying a crate");
        Require(boosted.Weights[0] == "21 → 83.4855" && boosted.Weights[3] == "15 → 17.5145"
            && StockChoiceDetails.Summary(thiefProfile, crates).Contains("黑市声望达到 <b>40</b>"), "crate weights preserve the original scale");
        Require(disabled.Weights.SequenceEqual(new[] { "21 → 21", "20 → 20", "20 → 20", "15 → 15", "25 → 25" }),
            "a disabled preview shows unadjusted weights");
        SetConfig(config, "default.appearance.enabled", true);
        string boxProbabilities = StockChoiceDetails.DescribeProbabilities(thiefProfile, crates, twoBoosts, true,
            settings.ProductAppearanceScenarios(thiefProfile, "crate_random"));
        Require(boxProbabilities.Contains("黑市声望低于 40") && boxProbabilities.Contains("黑市声望 40 及以上")
            && boxProbabilities.Contains("治安部物证柜　6.24% → <b>46.5%</b>")
            && boxProbabilities.Contains("治安部物证柜　20.79% → <b>82.66%</b>")
            && boxProbabilities.Contains("未携带上述物资箱　70% → <b>43.75%</b>")
            && boxProbabilities.Contains("未携带上述物资箱　0% → <b>0%</b>"),
            "crate classification details preserve appearance and type as two conditional stages");
        VerifyCrateOutcomes(boxProbabilities);
        SetConfig(config, "default.appearance.enabled", false);
        var pharmacistRule = rules.Single(rule => rule.MethodName == "CreateShadyPharmacist");
        var injectorChoice = pharmacistRule.Choices.Single();
        Require(settings.ChoicesForProductDetails(pharmacistRule.Profiles[0], "pink_injector").SequenceEqual(new[] { injectorChoice }),
            "an explicitly matching classification candidate also links to its quantity details");
        string injector = StockChoiceDetails.DescribeProbabilities(pharmacistRule.Profiles[0], injectorChoice, new[] { 100, -100 }, true);
        Require(injector.Contains("已解锁随机注射器") && injector.Contains("未解锁随机注射器")
            && injector.Contains(StockCatalog.Items["pink_injector"].Name + "　100% → <b>100%</b>"),
            "the locked doctor outcome keeps its native fixed shipment");
        var appraisalRule = rules.Single(rule => rule.MethodName == "CreateAppraisalRandom");
        Require(settings.ChoicesForProductDetails(appraisalRule.Profiles[0], "appraisal_random").SequenceEqual(appraisalRule.Choices),
            "appraisal type and quantity link to the same details");
        string appraisal = StockChoiceDetails.DescribeProbabilities(appraisalRule.Profiles[0], appraisalRule.Choices[0],
            new[] { -100, -100, -100 }, true);
        Require(appraisal.Contains(StockCatalog.Items["hand"].Name + "　100% → <b>100%</b>") && appraisal.Contains("鉴定大师"),
            "the certificate with one available type keeps its native result");
        var inventorForDetails = rules.Single(rule => rule.MethodName == nameof(StoreClientList.PlaceInventorInventory));
        var baseInventor = inventorForDetails.Profiles[0];
        foreach (StockProfile variant in inventorForDetails.Profiles)
        {
            Require(settings.StockDetailsNotice(variant) is { } notice && notice.Contains("外出商店")
                && notice.Contains("丢弃") && notice.Contains("Beta"), "every Jackson investment stage explains off-site overflow");
            var chipIds = settings.CategoriesFor(variant, StockAxis.Appearance)
                .Where(id => id.StartsWith("crypto_module_", StringComparison.Ordinal)).ToArray();
            Require(chipIds.Length == 5, "every Jackson stock variant lists the five desequencer chips");
            foreach (string chip in chipIds)
            {
                string summary = settings.ProductDetails(variant, StockAxis.Appearance, chip);
                Require(summary.Contains("<b>满足以下任意一个条件</b>\n• 单独抽选时，必须已经持有密码解序器")
                    && summary.Contains("• 随套装抽选时\n　<b>满足以下全部条件</b>\n　• 尚未持有密码解序器\n　• 抽中密码解序器套装")
                    && summary.Contains("<b>单独抽选</b>\n有 <b>"), "the chip summary scopes its reference odds and nests the bundle prerequisites");
                string details = settings.ProductProbabilityDetails(variant, StockAxis.Appearance, chip);
                Require(details.Contains("<b>满足以下全部条件</b>\n• 尚未持有密码解序器\n• 抽中密码解序器套装\n<b>随套装抽选</b>"),
                    "the chip details group the prerequisites for the separate bundle odds");
            }
        }
        string securityChipDetails = settings.ProductProbabilityDetails(baseInventor, StockAxis.Appearance, "crypto_module_sec");
        Require(securityChipDetails.Contains("携带 <b>10% → 10%</b>") && securityChipDetails.Contains("携带 <b>5% → 5%</b>"),
            "the security chip odds differ between standalone and bundle draws");
        var backpack = inventorForDetails.Choices.Single();
        Require(StockChoiceDetails.BackpackPages(backpack).Length == 8
            && StockChoiceDetails.DescribeProbabilities(baseInventor, backpack, new[] { 100, 100, 100 }, true, backpackPage: 7)
                .Contains("未携带上述背包　100% → <b>100%</b>"), "all backpack ownership combinations are accessible in the preview");
        Require(StockChoiceDetails.HoverSummary(baseInventor, backpack).Contains("参考 · 第 1 天 · 未持有背包")
            && StockChoiceDetails.DescribeReferenceWeights(baseInventor, backpack, new int[3], 7).Contains("已持有")
            && StockChoiceDetails.Summary(baseInventor, backpack).Contains("背包权重仅在外出拜访时生效")
            && StockChoiceDetails.Summary(baseInventor, backpack).Contains("挎包（小）"),
            "backpack details explain the off-site stock and use the selected ownership");
        StockProfile smuggler = settings.Customers.Single(profile => profile.Id == "hypertech_smuggler");
        StockChoice shipment = settings.ChoicesFor(smuggler).Single();
        var levels = new int[shipment.Categories.Length];
        string smugglerHover = StockSettingsWindow.WithDetailHint(StockChoiceDetails.HoverSummary(smuggler, shipment));
        Require(smugglerHover.Contains("生成 5 件货物") && smugglerHover.Contains("原版首次抽选时每种概率均为 16.67%")
            && smugglerHover.Contains("在超科技走私贩每次来访时，每种最多出现 1 件")
            && new[] { "超科技储藏区", "心智重编程器", "神经核心模组（无限制）" }.All(name => smugglerHover.Contains(name))
            && !smugglerHover.Split('\n').Contains("首次抽选"), "the smuggler hover explains first-roll numbers and the three goods in full sentences");
        string smugglerOdds = StockChoiceDetails.DescribeProbabilities(smuggler, shipment, levels, true);
        Require(StockChoiceDetails.DescribeReferenceWeights(smuggler, shipment, levels, null).StartsWith("首次抽选　")
            && smugglerOdds.Contains("下一次抽选的概率")
            && smugglerOdds.Contains("<b>本次已抽中 超科技储藏区</b>\n电子元件（超科技）　20% → <b>20%</b>"),
            "the first-roll title accompanies its weights and later odds use the remaining pool");
        SetConfig(config, "default.appearance", 61);
        SetConfig(config, "default.appearance.enabled", true);
        Console.WriteLine($"PASS original and modified details for {described} product rows and {classifications} classification groups.");

        Require(config.Entries.Where(entry => entry.ValueType == typeof(int)).All(entry => entry.HasRange && entry.Min == -100
            && entry.Max == 100 && (int)entry.BoxedDefault == 0), "native integer sliders default to zero");
        Require(config.Entries.Select(entry => entry.Key).Distinct().Count() == config.Entries.Count, "unique configuration keys");
        var labelOrder = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), false);
        foreach (StockAxis axis in new[] { StockAxis.Appearance, StockAxis.Quantity })
        {
            Require(settings.Customers.All(profile => settings.CategoriesFor(profile, axis).SequenceEqual(rules
                .Where(rule => rule.Profiles.Contains(profile))
                .SelectMany(rule => rule.Categories.Where((_, site) => rule.AxisFor(profile, site) == axis))
                .SelectMany(ids => ids).Distinct().OrderBy(id => StockCatalog.Items[id].Group)
                .ThenBy(id => StockCatalog.Items[id].Name, labelOrder)
                .ThenBy(id => id, StringComparer.Ordinal))), "all native category draws reach only their supported control groups");
        }
        var thiefCategories = settings.CategoriesFor(thief.Profiles[0], StockAxis.Appearance);
        RequireAdjacent(thiefCategories,
            "random_performance_module", "random_overclock_module",
            "random_quality_module", "random_fineness_module",
            "random_efficiency_module", "random_eco_module");
        var barter = rules.Single(rule => rule.MethodName == "CreateAddictGeneralBarter");
        var offers = settings.CategoriesFor(barter.Profiles[0], StockAxis.Appearance);
        Require(barter.BarterOfferCounts == (3, 21) && offers.Count == 21
            && offers.Contains("system_capped_neural_core") && offers.Contains("system_module_quality")
            && settings.CategoriesFor(barter.Profiles[0], StockAxis.Quantity).Count == 0
            && settings.ChoicesFor(barter.Profiles[0]).Select(choice => choice.Id).SequenceEqual(new[] { "packedFoodTable" }),
            "optional offers are independent appearances, with only the food contents in an exclusive table");
        const string corePosition = "addictGeneralBarter:appearance:system_capped_neural_core";
        string savedPositions = ((ConfigEntry<string>)config.Get(StockSettings.CategoryPositionsKey)).Value;
        Set(StockSettings.CategoryPositionsKey, savedPositions + "," + corePosition + "=50");
        Set("addictGeneralBarter" + StockSettings.IndividualCategoriesSuffix, true);
        Require(settings.ProductComparison(barter.Profiles[0], StockAxis.Appearance, "system_capped_neural_core") == "1% → 2.94%"
            && settings.ProductComparison(barter.Profiles[0], StockAxis.Appearance, "system_module_quality") == "6% → 6%",
            "middle settings change just the corresponding native offer, and retain the saved slider key");
        Set(StockSettings.CategoryPositionsKey, string.Join(",", offers.Select(item => "addictGeneralBarter:appearance:" + item
            + (item is "system_capped_neural_core" or "system_module_quality" ? "=100" : "=-100"))));
        var offerDetails = settings.OfferDetails(barter.Profiles[0])!.Value;
        Require(offerDetails.Summary.Contains("3–24 套 → 当前总数 <b>5 套</b>")
            && offerDetails.Offers.Contains("玻璃刀 ×1、铅管 ×1、美工刀 ×1　100% → <b>100%</b>")
            && offerDetails.Offers.Contains("质量模组 ×1　6% → <b>100%</b>（已应用）")
            && settings.ProductDetails(barter.Profiles[0], StockAxis.Appearance, "packedFoodTable").Contains("连续独立抽选 3 次")
            && settings.ProductDetails(barter.Profiles[0], StockAxis.Appearance, "system_module_quality").Contains("质量模组 ×1"),
            "barter details distinguish the set total, within-set quantity, and offer inclusion chance");
        Set(StockSettings.CategoryPositionsKey, savedPositions);
        Set("addictGeneralBarter" + StockSettings.IndividualCategoriesSuffix, false);
        foreach (var (group, expectedCount) in new[]
        {
            (StockItemGroup.AccessCard, 5),
            (StockItemGroup.DesequencerChip, 5),
        })
        {
            var indices = Enumerable.Range(0, thiefCategories.Count)
                .Where(index => StockCatalog.Items[thiefCategories[index]].Group == group).ToArray();
            Require(indices.Length == expectedCount && indices[expectedCount - 1] - indices[0] == expectedCount - 1,
                $"related thief goods appear together: {group}");
        }
        var gunSeller = rules.Single(rule => rule.MethodName == "CreateLLGunSeller").Profiles.Single();
        var gunSellerCategories = settings.CategoriesFor(gunSeller, StockAxis.Appearance).ToArray();
        foreach (var series in new[]
        {
            new[] { "rds", "rds_makeshift" },
            new[] { "silencer", "silencer_makeshift" },
            new[] { "internal_springs", "internal_springs_improved" },
        })
        {
            var indices = series.Select(id => Array.IndexOf(gunSellerCategories, id)).ToArray();
            Require(indices.Min() >= 0 && indices.Max() - indices.Min() == 1,
                $"gun attachment variants appear together: {string.Join(", ", series)}");
        }
        Require(settings.CategoriesFor(material, StockAxis.Quantity).ToHashSet().SetEquals(new[] { "scrap_metal", "printer_plastic", "wire", "flux_agent" })
            && settings.CategoriesFor(material, StockAxis.Appearance).Contains("box_cutter")
            && !settings.CategoriesFor(material, StockAxis.Appearance).Contains("scrap_metal"), "material seller separates scrap count from box cutter appearance");
        Require(settings.CategoriesFor(gunsmith.Profiles[0], StockAxis.Appearance).Contains("handmade_pistol")
            && settings.CategoriesFor(gunsmith.Profiles[0], StockAxis.Quantity).Contains("handmade_pistol"), "a category can appear in both groups");
        var categoryEntry = (ConfigEntry<string>)config.Get(StockSettings.CategoryKey);
        Require(categoryEntry.Default == "" && categoryEntry.Value == StockCategoryMask.AllChecked,
            "the all-checked configuration retains its saved selections while reset starts empty");
        Require(new[] { StockSettings.InvertCustomerKey, StockSettings.InvertCategoryKey }
            .All(key => config.Get(key) is ConfigEntry<bool> { Default: false, Value: false }), "both inversions default off");
        Require(config.Entries.Where(entry => entry.Key.StartsWith("default.", StringComparison.Ordinal) && entry.Key.EndsWith(".enabled", StringComparison.Ordinal))
            .All(entry => entry.BoxedValue is true && entry.BoxedDefault is true), "global feature availability defaults on");
        Require(settings.Customers.Count == rules.SelectMany(rule => rule.Profiles).Distinct().Count() + rules.Count(rule => rule.FixedProfile != null)
            && settings.Customers.Where(profile => !profile.FixedStock).All(profile => config.Get(profile.Id + ".enabled") is ConfigEntry<bool> { Value: true }),
            "each supported customer has a switch");
        Require(settings.Customers.Where(settings.HasProductSettings).All(profile =>
            config.Get(profile.Id + StockSettings.IndividualCategoriesSuffix) is ConfigEntry<bool> { Value: false, Default: false }),
            "existing configurations start with category checkboxes");
        Require(settings.Customers.All(profile => (config.Get(profile.Id + StockSettings.SelectionEnabledSuffix) is ConfigEntry<bool>)
            == settings.ChoicesFor(profile).Any(choice => choice.Quota == null)), "the category checkbox belongs to ordinary choice groups");
        Require(settings.Customers.All(profile => (config.Get(profile.Id + StockSettings.SpecialGoodsEnabledSuffix) is ConfigEntry<bool>)
            == settings.ChoicesFor(profile).Any(choice => choice.Quota != null))
            && shipment.SettingsSection == "特殊商品设置",
            "fixed stock groups have their own checkbox and settings chapter");
        var crateChoice = thief.Choices.Single(choice => choice.Id == "crate");
        string thiefSelectionKey = "thief" + StockSettings.SelectionEnabledSuffix;
        Require(settings.GetChoicePositions(thief.Profiles[0], crateChoice)?[3] == 50,
            "previous classification settings are enabled during migration");
        previewDraft.Positions.Set("thief", StockAxis.Selection, crateChoice.Key(0), 100);
        draftToggles[thiefSelectionKey] = true;
        Require(settings.GetChoicePositions(thiefProfile, crateChoice, previewDraft)?[0] == 100
            && settings.GetChoicePositions(thiefProfile, crateChoice)?[0] == 0, "choice comparisons use the uncommitted window positions");
        draftToggles[thiefSelectionKey] = false;
        Require(settings.GetChoicePositions(thiefProfile, crateChoice, previewDraft) == null
            && settings.GetChoicePositions(thiefProfile, crateChoice) != null, "disabling an inline choice preview leaves the applied switch alone");
        draftToggles.Remove(thiefSelectionKey);
        var shady = rules.Single(rule => rule.MethodName == nameof(StoreClientList.CreateShadyPharmacist));
        Require(settings.GetChoicePositions(shady.Profiles[0], shady.Choices.Single()) == null,
            "an explicitly disabled classification stays disabled with saved nonzero values");
        Set(thiefSelectionKey, false);
        Require(settings.GetChoicePositions(thief.Profiles[0], crateChoice) == null, "the customer checkbox gates classification rolls");
        Set(thiefSelectionKey, true);
        Require(settings.GetChoicePositions(thief.Profiles[0], crateChoice)?[3] == 50, "re-enabling restores the saved dial");
        var inventor = rules.Single(rule => rule.MethodName == nameof(StoreClientList.PlaceInventorInventory));
        Require(inventor.Profiles.Length == 3 && inventor.Profiles.All(profile => profile.DefaultProfile == null
            && settings.CategoriesFor(profile, StockAxis.Quantity).Contains("chem_module")
            && settings.ChoicesFor(profile).Single().Id == "backpack"),
            "each independent investment stage has chemistry quantities and an off-site backpack group");
        Require(inventor.AxisFor(inventor.Profiles[0], 65) == StockAxis.None && inventor.AxisFor(inventor.Profiles[1], 65) == StockAxis.None
            && inventor.AxisFor(inventor.Profiles[2], 65) == StockAxis.Quantity && inventor.AxisFor(inventor.Profiles[0], 67) == StockAxis.Appearance,
            "Jackson tier-specific chemistry extra calls and the ordinary chance use their proper axes");
        Require(inventor.Categories[20].SequenceEqual(new[] { "desequencer", "blank_keycard" })
            && inventor.Categories.Skip(21).Take(5).All(ids => ids.Length == 1 && ids[0].StartsWith("crypto_module_", StringComparison.Ordinal)),
            "Jackson's decoder and blank card share the bundle, and its chips use independent rolls");
        var oldJacksonConfig = CreateConfig();
        var oldJackson = (Dictionary<string, string>)AccessTools.Field(typeof(ModConfig), "rawValues").GetValue(oldJacksonConfig);
        oldJackson["inventorVisitInvestment1.enabled"] = "true";
        oldJackson["inventorVisitInvestment1.override"] = "true";
        oldJackson["inventorVisitInvestment1.quantity"] = "100";
        oldJackson["inventorOutsideInvestment1.enabled"] = "false";
        oldJackson["inventorOutsideInvestment1" + StockSettings.SelectionEnabledSuffix] = "true";
        oldJackson[StockSettings.InvertCategoryKey] = "true";
        oldJackson[StockSettings.CategoryPositionsKey] = "inventorOutsideInvestment1:selection:backpack/backpack_large=65";
        var sharedJackson = new StockSettings(oldJacksonConfig, new[] { inventor });
        var investment1 = inventor.Profiles[1];
        Require(sharedJackson.Get(inventor, investment1, 68) == 100 && sharedJackson.Get(inventor, inventor.Profiles[2], 68) == 0
            && sharedJackson.GetChoicePositions(investment1, inventor.Choices.Single())?[0] == 65
            && oldJacksonConfig.Get("inventorOutsideInvestment1.enabled") == null,
            "old in-shop levels and off-site backpack weights merge under the same tier switch");
        SetConfig(oldJacksonConfig, "inventorVisitInvestment1.enabled", false);
        Require(sharedJackson.Get(inventor, investment1, 68) == 0
            && sharedJackson.GetChoicePositions(investment1, inventor.Choices.Single()) == null,
            "turning a shared tier off disables both normal and backpack adjustments");
        var rancher = rules.Single(rule => rule.MethodName == nameof(StoreClientListSpec.CreateRetiredRancher));
        foreach (string category in new[] { "rat_growth", "rat_litter" })
            Require(settings.CategoriesFor(rancher.Profiles[0], StockAxis.Appearance).Contains(category)
                && settings.CategoriesFor(rancher.Profiles[0], StockAxis.Quantity).Contains(category),
                "each enhanced rat type has an appearance roll and a separate bonus count");
        Require(settings.Customers.Contains(StockRules.InventorSpecial) && rules.All(rule => rule.MethodName != nameof(StoreClientList.PlaceInventorSpecialStock))
            && settings.ChoicesFor(StockRules.InventorSpecial).Count == 0, "fixed Monday assortment gets an informational entry only");
        Require(config.Entries.Where(entry => entry.Key.StartsWith(material.Id + ".", StringComparison.Ordinal))
            .Select(entry => entry.Key.Substring(material.Id.Length))
            .SequenceEqual(new[] { ".enabled", ".override", ".appearance", ".quantity", StockSettings.IndividualCategoriesSuffix }),
            "both customer dials immediately follow the independent settings switch");
        Require(new[] { -100, -23, 0, 42, 100 }.Select(value => StockSettingsWindow.FormatPosition(value))
            .SequenceEqual(new[] { "-100", "-23", "0", "+42", "+100" }), "slider values show the signed setting across its range");
        Require(settings.Get(thief, StockAxis.Appearance, "thief") == 61, "global chance");
        Require(settings.Get(miner, StockAxis.Quantity, "miner") == -33, "independent global count");

        static void RequireAdjacent(IReadOnlyList<string> categories, params string[] expected)
            => Require(categories.SkipWhile(id => id != expected[0]).Take(expected.Length).SequenceEqual(expected),
                "module variants appear together in effect order");

        int booster = Array.FindIndex(thief.Categories, ids => ids.Contains("turbo_booster_adv"));
        int perfume = Array.FindIndex(thief.Categories, ids => ids.Contains("neuroactive_perfume"));
        var draft = new StockCategoryMask(categoryEntry.Value);
        draft.SetChecked("thief", StockAxis.Appearance, "turbo_booster_adv", false);
        draft.SetChecked("miner", StockAxis.Quantity, "common_ore", false);
        Require(!draft.IsChecked("thief", StockAxis.Appearance, "turbo_booster_adv") && draft.IsChecked("thief", StockAxis.Appearance, "neuroactive_perfume"),
            "category drafts survive selecting another customer");
        Require(settings.Get(thief, StockAxis.Appearance, "thief", booster) == 61, "draft stays separate until a game Apply");
        Set(StockSettings.CategoryKey, draft.Save());
        Require(settings.Get(thief, StockAxis.Appearance, "thief", booster) == 0
            && settings.Get(thief, StockAxis.Appearance, "thief", perfume) == 61, "unchecking reverts only the selected category");
        Require(!settings.UsesAdjustment(miner, "miner"), "every unchecked quantity draw retains its native settings");
        var reopened = new StockCategoryMask(categoryEntry.Value);
        Require(!reopened.IsChecked("miner", StockAxis.Quantity, "common_ore")
            && !reopened.IsChecked("thief", StockAxis.Appearance, "turbo_booster_adv"), "saved masks survive reopening");

        Set(StockSettings.InvertCategoryKey, true);
        Require(settings.Get(thief, StockAxis.Appearance, "thief", booster) == 61
            && settings.Get(thief, StockAxis.Appearance, "thief", perfume) == 0
            && settings.UsesAdjustment(miner, "miner"), "category inversion uses stored checkboxes separately per axis");
        Require((bool)config.Get("thief.enabled").BoxedValue && reopened.Save() == categoryEntry.Value,
            "category inversion preserves raw product and customer selections");
        Set(StockSettings.InvertCategoryKey, false);

        reopened.Load(categoryEntry.Default);
        Require(!reopened.IsChecked("miner", StockAxis.Quantity, "common_ore")
            && !reopened.IsChecked("thief", StockAxis.Appearance, "turbo_booster_adv")
            && settings.Get(thief, StockAxis.Appearance, "thief", booster) == 0, "Reset drafts every customer without changing active settings");
        Set(StockSettings.CategoryKey, reopened.Save());
        Require(settings.Get(thief, StockAxis.Appearance, "thief", booster) == 0 && !settings.UsesAdjustment(miner, "miner"),
            "applying restored defaults clears category masks for every customer");

        const string gunsmithId = "lower_level_gunsmith";
        const string handmade = "handmade_pistol";
        int pistolChance = Site(gunsmith, StockAxis.Appearance, handmade);
        int pistolCount = Site(gunsmith, StockAxis.Quantity, handmade);
        Set(StockSettings.CategoryKey, AllGoodsExcept(StockCategoryMask.Key(gunsmithId, StockAxis.Appearance, handmade),
            StockCategoryMask.Key(gunsmithId, StockAxis.Quantity, handmade)));
        var mask = new StockCategoryMask(categoryEntry.Value);
        Require(settings.Get(gunsmith, StockAxis.Appearance, gunsmithId, pistolChance) == 0
            && settings.Get(gunsmith, StockAxis.Quantity, gunsmithId, pistolCount) == 0, "the saved mask disables both axes independently");
        mask.SetChecked(gunsmithId, StockAxis.Quantity, handmade, true);
        Set(StockSettings.CategoryKey, mask.Save());
        Require(settings.Get(gunsmith, StockAxis.Appearance, gunsmithId, pistolChance) == 0
            && settings.Get(gunsmith, StockAxis.Quantity, gunsmithId, pistolCount) == -33, "rechecking quantity does not change appearance");

        int fruitSeeds = Site(scav, StockAxis.Appearance, "nutrifruit_seed_packet");
        int cottonSeeds = Site(scav, StockAxis.Appearance, "kotton_seed");
        Set(StockSettings.CategoryKey, AllGoodsExcept("scavGeneral:appearance:nutrifruit_seed_packet"));
        Require(fruitSeeds != cottonSeeds && settings.Get(scav, StockAxis.Appearance, "scavGeneral", fruitSeeds) == 0
            && settings.Get(scav, StockAxis.Appearance, "scavGeneral", cottonSeeds) == -88
            && settings.Get(scav, StockAxis.Appearance, "eleanor6", fruitSeeds) == -88,
            "the seed packet has its own stock roll and Eleanor keeps her own category mask");
        Set(StockSettings.CategoryKey, StockCategoryMask.AllChecked);

        Require(settings.Get(scav, StockAxis.Appearance, "scavGeneral") == -88, "per-client override");
        Require(settings.Get(scav, StockAxis.Appearance, "eleanor6") == -88, "Eleanor chapter visit inherits");
        Require(settings.Get(scav, StockAxis.Appearance, "eleanorScav") == -88, "Eleanor regular visit inherits");
        Set("eleanorScav.override", true);
        Set("eleanorScav.appearance", 22);
        Require(settings.Get(scav, StockAxis.Appearance, "eleanor6") == 22, "Eleanor override");
        Require(settings.Get(scav, StockAxis.Appearance, "scavGeneral") == -88, "Eleanor override does not affect ordinary scavenger");

        Set("default.enabled", false);
        Require(!settings.UsesAdjustment(scav, "scavGeneral") && !settings.UsesAdjustment(thief, "thief")
            && !settings.UsesAdjustment(miner, "miner"), "master switch blocks both features and overrides");
        Set("default.enabled", true);
        Set("default.appearance.enabled", false);
        Require(settings.Get(thief, StockAxis.Appearance, "thief") == 0
            && settings.Get(scav, StockAxis.Appearance, "eleanor6") == 0
            && settings.Get(miner, StockAxis.Quantity, "miner") == -33, "appearance switch overrides global and independent settings only on that axis");
        Set("default.appearance.enabled", true);
        Set("default.quantity.enabled", false);
        Require(settings.Get(miner, StockAxis.Quantity, "miner") == 0
            && settings.Get(thief, StockAxis.Appearance, "thief") == 61, "quantity switch leaves appearance active");
        Set("default.quantity.enabled", true);

        Set("scavGeneral.enabled", false);
        Require(!settings.UsesAdjustment(scav, "scavGeneral"), "customer switch blocks the customer's own override and inherited defaults");
        Set("eleanorScav.override", false);
        Require(settings.Get(scav, StockAxis.Appearance, "eleanor6") == -88, "Eleanor inherits the regular scavenger's dial, with her own switch");
        Set("eleanorScav.enabled", false);
        Require(!settings.UsesAdjustment(scav, "eleanor6") && !settings.UsesAdjustment(scav, "eleanorScav"),
            "the story and regular appearances share Eleanor's switch");
        Set("eleanorScav.enabled", true);
        Set("eleanorScav.override", true);
        Set("scavGeneral.enabled", true);
        Require(settings.Get(scav, StockAxis.Appearance, "eleanor6") == 22
            && settings.Get(scav, StockAxis.Appearance, "scavGeneral") == -88, "switches preserve independent values");

        Set("scavGeneral.enabled", false);
        Set(StockSettings.CategoryKey, AllGoodsExcept("scavGeneral:appearance:nutrifruit_seed_packet"));
        Set(StockSettings.InvertCustomerKey, true);
        var materialSwitch = config.Get(material.Id + ".enabled");
        Require(materialSwitch.Label().StartsWith("禁用「拾荒客（材料霓虹灯牌）」", StringComparison.Ordinal)
            && materialSwitch.Tooltip() == StockSettings.CustomerSwitchTip(true)
            && StockSettings.CategorySwitchTip(true).StartsWith("勾选某项后，停用该项的调整", StringComparison.Ordinal),
            "inversion explains the resulting meaning using the local game name");
        Require(!settings.UsesAdjustment(thief, "thief") && settings.Get(scav, StockAxis.Appearance, "scavGeneral") == -88
            && settings.Get(scav, StockAxis.Appearance, "scavGeneral", fruitSeeds) == 0,
            "customer inversion swaps eligibility while preserving category masks and independent dials");
        Require(!(bool)config.Get("scavGeneral.enabled").BoxedValue && (bool)config.Get("scavGeneral.override").BoxedValue,
            "customer inversion preserves raw customer and override checkboxes");
        Set(StockSettings.InvertCustomerKey, false);
        Set("scavGeneral.enabled", true);
        Set(StockSettings.CategoryKey, StockCategoryMask.AllChecked);

        const string thiefMode = "thief" + StockSettings.IndividualCategoriesSuffix;
        string thiefMask = AllGoodsExcept("thief:appearance:turbo_booster_adv");
        var positionDraft = new StockCategoryPositions("");
        positionDraft.Set("thief", StockAxis.Appearance, "turbo_booster_adv", 80);
        positionDraft.Set("thief", StockAxis.Appearance, "neuroactive_perfume", -50);
        positionDraft.Set(gunsmithId, StockAxis.Appearance, handmade, 30);
        positionDraft.Set(gunsmithId, StockAxis.Quantity, handmade, -70);
        positionDraft.Set("scavGeneral", StockAxis.Appearance, "nutrifruit_seed_packet", 55);
        positionDraft.Set("scavGeneral", StockAxis.Appearance, "kotton_seed", -20);
        Set(StockSettings.CategoryKey, thiefMask);
        Set(StockSettings.InvertCategoryKey, true);
        Set("thief.override", true);
        Set("thief.appearance", -35);
        Set(thiefMode, true);
        Require(settings.Get(thief, StockAxis.Appearance, "thief", booster) == 0,
            "unapplied slider edits do not affect active stock");
        Set(StockSettings.CategoryPositionsKey, positionDraft.Save());
        Require(settings.Get(thief, StockAxis.Appearance, "thief", booster) == 80
            && settings.Get(thief, StockAxis.Appearance, "thief", perfume) == -50,
            "individual positions supersede both dials and the inverted checkbox mask");
        Set("default.appearance.enabled", false);
        Require(!settings.UsesAdjustment(thief, "thief"), "the global feature switch still controls individual sliders");
        Set("default.appearance.enabled", true);
        Set(thiefMode, false);
        Require(categoryEntry.Value == thiefMask && settings.Get(thief, StockAxis.Appearance, "thief", booster) == -35
            && settings.Get(thief, StockAxis.Appearance, "thief", perfume) == 0,
            "returning to checkboxes restores the previous mask and inversion");
        Set(thiefMode, true);
        Require(settings.Get(thief, StockAxis.Appearance, "thief", booster) == 80,
            "returning to individual sliders restores the saved position");
        Set(gunsmithId + StockSettings.IndividualCategoriesSuffix, true);
        Require(settings.Get(gunsmith, StockAxis.Appearance, gunsmithId, pistolChance) == 30
            && settings.Get(gunsmith, StockAxis.Quantity, gunsmithId, pistolCount) == -70,
            "the same product keeps separate appearance and quantity positions");
        Set("scavGeneral" + StockSettings.IndividualCategoriesSuffix, true);
        Require(settings.Get(scav, StockAxis.Appearance, "scavGeneral", fruitSeeds) == 55
            && settings.Get(scav, StockAxis.Appearance, "scavGeneral", cottonSeeds) == -20,
            "the seed packet and cotton seeds use their own category positions");
        Set(StockSettings.CategoryPositionsKey, "");
        Require(!settings.UsesAdjustment(thief, "thief"), "reset positions start at the original game");
        Set(StockSettings.CategoryKey, StockCategoryMask.AllChecked);
        Set(StockSettings.InvertCategoryKey, false);

        foreach (StockMethod early in earlyVisits.Where(method => settings.CategoriesFor(method.Profiles[0], StockAxis.Appearance).Count != 0))
        {
            Require(settings.Get(early, StockAxis.Appearance) == 61, "pre-visit generation reads the configured profile");
            Set(early.Profiles[0].Id + ".enabled", false);
            Require(settings.Get(early, StockAxis.Appearance) == 0, "customer switch controls pre-visit generation");
        }

        foreach (string id in new[] { "eleanor5", "georgio3", "georgio4", "georgio_luck_sale", "waterStockpiler",
                     "junker2", "evidenceSeller", "inventorIntro", "merchant", "unknown_supplier" })
        {
            Require(rules.All(rule => settings.Get(rule, StockAxis.Appearance, id) == 0
                && settings.Get(rule, StockAxis.Quantity, id) == 0), "fixed or unrelated visit excluded " + id);
        }

        void Set<T>(string key, T value)
        {
            var entry = SetConfig(config, key, value);
            Require(Equals(entry.Value, value), "native configuration applies " + key);
        }

        static int Site(StockMethod method, StockAxis axis, string id) => Enumerable.Range(0, method.Draws.Length)
            .Single(site => StockMethod.AxisFor(method.Draws[site]) == axis && method.Categories[site].Contains(id));
    }

    private static Assembly TestPager(string pagerPath, List<StockPatches.Target> targets)
    {
        var assembly = Assembly.LoadFrom(pagerPath);
        string[] patchNames = assembly.GetTypes().SelectMany(type => type.GetCustomAttributesData())
            .Where(attribute => attribute.AttributeType == typeof(HarmonyPatch))
            .SelectMany(attribute => attribute.ConstructorArguments)
            .Where(argument => argument.ArgumentType == typeof(string))
            .Select(argument => (string)argument.Value)
            .ToArray();
        Require(patchNames.Contains("TryAddDirectSellingItemToTable"), "installed pager wraps native placement");
        Require(patchNames.Contains("TryAcceptAllOrDestroy"), "installed pager wraps native overflow");
        Require(patchNames.Contains("IsFrontCounterEmptyNonOwned"), "installed pager wraps native fallback visibility");
        Require(targets.All(target => !patchNames.Contains(target.Method.Name)), "pager patches are downstream of stock decisions");
        Require((bool)assembly.GetType("MerchantStockPager.Stock").GetMethod("IsSupportedClient",
            BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { "thief" }),
            "installed pager handles the ordinary thief");
        Console.WriteLine("PASS installed Merchant Stock Pager patch paths.");
        return assembly;
    }

    private static void TestHarmony(List<StockPatches.Target> targets, Assembly? pager)
    {
        var manifest = new ModManifest { Name = "更好的顾客库存", ID = "better_customer_stock.verification" };
        string id = "mod." + manifest.ID;
        var configWindow = typeof(ModConfigWindow).GetMethod(nameof(ModConfigWindow.Open));
        using var fixture = new VisitFixtures(targets);
        fixture.RedirectConfig();
        IMod entry = new ModEntry();
        entry.Init(manifest);
        string[] vanilla = fixture.Thief(40, 0, 45);
        try
        {
            entry.OnEnable();
            var config = StockRandom.Settings!.Config;
            EnableAllCustomers(config);
            SetConfig(config, StockSettings.CategoryKey, StockCategoryMask.AllChecked);
            Require(targets.All(target => Harmony.GetPatchInfo(target.Method)?.Owners.Contains(id) == true),
                "Harmony detours present");
            Require(Harmony.GetPatchInfo(configWindow)?.Postfixes.Any(patch => patch.owner == id) == true,
                "settings enhancements are attached to the game's config window");
            TestVisits(fixture, vanilla);
            TestBarterOffers(fixture, targets);
            using CounterFixture counter = fixture.Counter();
            if (StockRandom.HasNativePaging)
                TestNativeCounter(fixture, counter, vanilla);
            if (pager != null)
                TestWorkshopPager(fixture, counter, pager, targets, id);
        }
        finally
        {
            entry.OnDisable();
        }
        Require(targets.All(target => Harmony.GetPatchInfo(target.Method)?.Owners.Contains(id) != true),
            "Harmony restores every stock method");
        Require(Harmony.GetPatchInfo(configWindow)?.Postfixes.Any(patch => patch.owner == id) != true,
            "Harmony restores the normal settings window on disable");
        Require(fixture.Thief(40, 100, 45).SequenceEqual(vanilla), "removed patches restore vanilla stock");
        entry.OnEnable();
        try
        {
            Require(fixture.Thief(40, -100, 45).Length == 1, "reactivation restores adjustments");
        }
        finally
        {
            entry.OnDisable();
        }
        Console.WriteLine("PASS Harmony install and removal against local assemblies.");
    }

    private static void TestBarterOffers(VisitFixtures fixture, IReadOnlyList<StockPatches.Target> targets)
    {
        var config = VisitFixtures.ActiveConfig!;
        const string profile = "addictGeneralBarter";
        const string core = "system_capped_neural_core";
        const string quality = "system_module_quality";
        const string food = "packedFoodTable";
        const string firstProfile = "addictBarter";
        string[] items = Items(nameof(BarterOfferList.CreateAddictGeneralBarter));
        string[] firstItems = Items(nameof(BarterOfferList.CreateAddictBarter));
        string[] baseline = { "wire,wire,wire", "blood_bag,blood_bag", "glass_shard_shiv,pipe_weapon,box_cutter" };
        var foodItems = LootRegistry.GetTableEntries(food).Select(entry => entry.id).ToHashSet();
        SetConfig(config, profile + StockSettings.IndividualCategoriesSuffix, true);
        SetConfig(config, firstProfile + StockSettings.IndividualCategoriesSuffix, true);
        foreach (bool linear in new[] { false, true })
        {
            SetConfig(config, StockSettings.LinearChangesKey, linear);
            SetConfig(config, StockSettings.CategoryPositionsKey, Positions(profile, items, _ => -100));
            string[][] noExtras = fixture.AddictOffers(45);
            Require(noExtras.Select(set => string.Join(",", set)).SequenceEqual(baseline), "all exclusions preserve the three vanilla offers");

            SetConfig(config, "default.quantity", -100);
            SetConfig(config, StockSettings.CategoryPositionsKey, Positions(profile, items, item => item is core or quality ? 100 : -100));
            string[][] both = fixture.AddictOffers(45);
            Require(both.Select(set => string.Join(",", set)).SequenceEqual(baseline.Concat(new[] { quality, core })),
                "two guaranteed offers add two separate quotes in native order regardless of quantity settings");
            SetConfig(config, "default.quantity", 0);

            SetConfig(config, StockSettings.CategoryPositionsKey, Positions(profile, items, item => item == food ? 100 : -100));
            string[][] foods = fixture.AddictOffers(45);
            Require(foods.Length == 4 && foods[3].Length == 3
                && foods[3].All(foodItems.Contains),
                "three food rolls belong to a single optional offer");
            SetConfig(config, StockSettings.CategoryPositionsKey, Positions(profile, items, _ => 100));
            Require(fixture.Addict(45).OfferSetCount() == 24, "all 21 guarantees add their own offers without a limit");

            SetConfig(config, StockSettings.CategoryPositionsKey, Positions(firstProfile, firstItems, _ => 100));
            Require(fixture.Addict(45, initialVisit: true).OfferSetCount() == 6, "first visit includes all three optional offers");
            SetConfig(config, StockSettings.CategoryPositionsKey, Positions(firstProfile, firstItems, _ => -100));
            Require(fixture.Addict(45, initialVisit: true).OfferSetCount() == 3, "first visit excludes all three optional offers");
        }
        SetConfig(config, StockSettings.LinearChangesKey, false);
        SetConfig(config, profile + StockSettings.IndividualCategoriesSuffix, false);
        SetConfig(config, firstProfile + StockSettings.IndividualCategoriesSuffix, false);
        SetConfig(config, StockSettings.AllIndividualCategoriesKey, true);
        SetConfig(config, StockSettings.CategoryPositionsKey, Positions(profile, items, item => item is core or quality ? 100 : -100));
        Require(fixture.AddictOffers(45).Select(set => string.Join(",", set)).SequenceEqual(baseline.Concat(new[] { quality, core })),
            "global individual mode uses goods sliders for the native independent offers");
        SetConfig(config, StockSettings.AllIndividualCategoriesKey, false);
        SetConfig(config, StockSettings.CategoryPositionsKey, "");
        Console.WriteLine("PASS independent barter offers, three fixed sets, 24 possible offers, and food rolls within one set.");

        string[] Items(string factory) => targets.Single(target => target.Rule.MethodName == factory).Rule.Categories
            .SelectMany(ids => ids).ToArray();

        static string Positions(string customer, IEnumerable<string> categories, Func<string, int> position)
            => string.Join(",", categories.Select(item => $"{customer}:appearance:{item}={position(item)}"));
    }

    private static void TestWorkshopPager(VisitFixtures fixture, CounterFixture counter, Assembly pager,
        IReadOnlyList<StockPatches.Target> targets, string id)
    {
        counter.Capacity = 2;
        if (!StockRandom.HasNativePaging)
        {
            fixture.Thief(40, 100, 45);
            Require(counter.Placed == 2 && counter.Unplaced == 24, "vanilla refuses or destroys overflow");
        }

        var pagerHarmony = new Harmony(id + ".pager");
        FieldInfo enabled = AccessTools.Field(pager.GetType("MerchantStockPager.Mod"), "Enabled");
        Type stock = pager.GetType("MerchantStockPager.Stock");
        var pending = (List<GameItem>)AccessTools.Field(stock, "Pending").GetValue(null);
        try
        {
            pagerHarmony.PatchAll(pager);
            enabled.SetValue(null, true);
            Require(Harmony.GetPatchInfo(typeof(PlayerStore).GetMethod("TryAddDirectSellingItemToTable",
                new[] { typeof(GameItem), typeof(bool), typeof(bool), typeof(bool), typeof(int) }))
                .Owners.Contains(pagerHarmony.Id), "installed pager retains its placement hook");
            Require(targets.All(target => Harmony.GetPatchInfo(target.Method).Owners.Contains(id)), "both mods installed");
            if (StockRandom.HasNativePaging)
            {
                var native = new NativePagesFixture(fixture, counter);
                native.Reset();
                counter.Capacity = 0;
                fixture.Thief(40, 100, 45);
                Require(native.Held.Length == 26 && pending.Count == 0 && counter.Unplaced == 0,
                    "native stock pages own the overflow even if the workshop pager is enabled");
                native.Clear();
            }
            else
            {
                counter.Clear();
                fixture.Thief(40, 100, 45);
                Require(counter.Placed == 2 && counter.Unplaced == 0 && pending.Count == 24
                    && pending.Any(item => item.identifier.StartsWith("crate:")), "actual pager retains all excess stock");

                AccessTools.Method(stock, "ForgetScene").Invoke(null, null);
                counter.Clear();
                counter.Capacity = 0;
                fixture.Thief(40, 100, 45);
                Require(counter.Placed == 0 && counter.Unplaced == 0 && pending.Count == 26
                    && !pending.Any(item => item.identifier == "common_chemical" || item.identifier == "common_medical"),
                    "actual pager reserve prevents the native empty-stock fallback");
            }
        }
        finally
        {
            enabled.SetValue(null, false);
            pagerHarmony.UnpatchAll(pagerHarmony.Id);
        }
        Console.WriteLine("PASS placement with the installed Workshop pager.");
    }

    private static void TestNativeCounter(VisitFixtures fixture, CounterFixture counter, string[] vanilla)
    {
        var pager = new NativePagesFixture(fixture, counter);
        pager.Reset();
        counter.Capacity = 0;
        fixture.Thief(40, 0, 45);
        Require(pager.Held.Length == vanilla.Length + 3, "both sliders at zero preserve even the game's stock-only-in-reserve behavior");

        pager.Reset();
        fixture.Thief(40, 0, 45, quantity: 100);
        Require(pager.Held.Length == vanilla.Length + 3, "an axis that does not apply leaves the vanilla visit unchanged");

        pager.Reset();
        fixture.Thief(0, -100, 18);
        Require(pager.Held.Length == 3 && pager.Held.All(item => item.identifier == pager.Held[0].identifier)
            && (pager.Held[0].identifier == "common_medical" || pager.Held[0].identifier == "common_chemical"),
            "the genuinely empty native visit still gets the original fallback");

        pager.Reset();
        Require(pager.Held.Length == 0, "native cleanup removes the previous visit's held items");
        fixture.Thief(40, 100, 45);
        Require(counter.Unplaced == 0 && counter.Placed == 0 && pager.Held.Length == 26
            && pager.Held.Count(item => item.identifier.StartsWith("crate:")) == 1,
            "all offers retained without false empty-stock fallback when nothing fits on the front counter");
        Require(StoreClientInstance.IsCurrentClientStillHaveThingToSell(), "native trade sees hidden goods");
        Require(pager.PageCount > 1 && pager.PageCount <= 12, "native page count");
        var originalOffers = new HashSet<GameItem>(pager.Held);
        counter.Capacity = counter.ReserveCapacity;
        Require(pager.Turn(1) && counter.Placed > 0, "native next page displays stock");
        Require(pager.Turn(-1), "native previous page");
        Require(originalOffers.SetEquals(pager.Held.Concat(counter.Visible)), "paging keeps the actual original objects");
        GameItem[] bought = counter.TakeVisible();
        pager.PurchaseDone();
        Require(bought.Length > 0 && counter.Placed > 0 && originalOffers.Except(bought)
            .ToHashSet().SetEquals(pager.Held.Concat(counter.Visible)), "native post-purchase refill uses the same stock");

        pager.Reset();
        counter.Capacity = 1;
        counter.ReserveCapacity = 1;
        fixture.Thief(40, 100, 45);
        Require(pager.PageCount == 12 && pager.Held.Length == 11 && counter.Placed == 1 && counter.Unplaced == 14,
            "native 12-page limit and final discard still apply");
        fixture.Client.clientIntent = StoreClient.ClientIntent.BARTER;
        Require(!pager.IsPagingFor(false) && !pager.IsPagingFor(true), "native barter and gifts bypass stock pages");
        counter.ReserveCapacity = 4;
        pager.Reset();
        counter.Capacity = 2;
        Console.WriteLine("PASS native reserve, page turns, post-purchase refill, fallback and page limit.");
    }

    private static void TestVisits(VisitFixtures fixture, string[] vanilla)
    {
        Require(fixture.Thief(40, 0, 45).SequenceEqual(vanilla), "thief zero matches the vanilla visit and draw order");
        string[] low = fixture.Thief(40, -100, 45);
        string[] high = fixture.Thief(40, 100, 45);
        Require(low.Length == 1 && low[0].StartsWith("crate:"), "guaranteed random box at -100");
        Require(high.Length == 26 && high.Count(item => item.StartsWith("crate:")) == 1
            && high.Contains("turbo_booster_adv") && high.Contains("crypto_module_cmd"),
            "all 25 optional offers and one box at +100");
        var config = VisitFixtures.ActiveConfig!;
        SetConfig(config, "default.enabled", false);
        Require(fixture.Thief(40, 100, 45).SequenceEqual(vanilla), "master switch restores the native visit and draw order");
        Require(fixture.Miner(30, 100) == fixture.Miner(30, 0), "master switch bypasses native quantity adjustment");
        SetConfig(config, "default.enabled", true);
        SetConfig(config, "thief.enabled", false);
        Require(fixture.Thief(40, 100, 45).SequenceEqual(vanilla) && fixture.Miner(30, 100) == 7,
            "customer switch restores only the selected native visit");
        SetConfig(config, "thief.enabled", true);
        SetConfig(config, StockSettings.InvertCustomerKey, true);
        Require(fixture.Thief(40, 100, 45).SequenceEqual(vanilla), "a previously checked customer follows the original game when inverted");
        SetConfig(config, "thief.enabled", false);
        Require(fixture.Thief(40, 100, 45).SequenceEqual(high) && fixture.Miner(30, 100) == fixture.Miner(30, 0),
            "inverted customer choice opts in only the customer unchecked by the player");
        SetConfig(config, StockSettings.InvertCustomerKey, false);
        SetConfig(config, "thief.enabled", true);

        const string item = "turbo_booster_adv";
        int misses = Enumerable.Range(0, 200).First(seed => !fixture.Thief(40, 0, seed).Contains(item));
        int hits = Enumerable.Range(0, 200).First(seed => fixture.Thief(40, 0, seed).Contains(item));
        string[] originalMiss = fixture.Thief(40, 0, misses);
        SetConfig(config, StockSettings.InvertCustomerKey, true);
        SetConfig(config, StockSettings.InvertCategoryKey, true);
        SetConfig(config, StockSettings.CategoryKey, AllGoodsExcept("thief:appearance:" + item));
        Require(fixture.Thief(40, 100, misses).SequenceEqual(originalMiss), "an opted-in category still requires an enabled customer");
        SetConfig(config, "thief.enabled", false);
        string[] isolated = fixture.Thief(40, 100, misses);
        Require(isolated.Count(id => id == item) == 1 && isolated.Where(id => id != item).SequenceEqual(originalMiss),
            "opting in one customer and one appearance category changes only the selected stock");
        SetConfig(config, StockSettings.InvertCustomerKey, false);
        SetConfig(config, StockSettings.InvertCategoryKey, false);
        SetConfig(config, "thief.enabled", true);

        SetConfig(config, StockSettings.CategoryKey, StockCategoryMask.AllChecked);
        SetConfig(config, StockSettings.InvertCategoryKey, true);
        SetConfig(config, "thief" + StockSettings.IndividualCategoriesSuffix, true);
        SetConfig(config, "miner" + StockSettings.IndividualCategoriesSuffix, true);
        SetConfig(config, StockSettings.CategoryPositionsKey, "thief:appearance:" + item + "=100,miner:quantity:common_ore=-100");
        string[] individual = fixture.Thief(40, -100, misses);
        Require(individual.Count(id => id == item) == 1 && individual.Where(id => id != item).SequenceEqual(originalMiss),
            "the native visit uses the individual appearance dial despite the inverted mask and global minimum");
        Require(fixture.Miner(30, 100) == 3, "the native visit uses the individual quantity dial despite the global maximum");
        SetConfig(config, StockSettings.CategoryPositionsKey, "");
        SetConfig(config, "thief" + StockSettings.IndividualCategoriesSuffix, false);
        SetConfig(config, "miner" + StockSettings.IndividualCategoriesSuffix, false);
        SetConfig(config, StockSettings.InvertCategoryKey, false);

        SetConfig(config, StockSettings.CategoryKey, AllGoodsExcept("thief:appearance:" + item, "miner:quantity:common_ore"));
        var filteredHigh = fixture.Thief(40, 100, misses);
        var filteredLow = fixture.Thief(40, -100, hits);
        Require(!filteredHigh.Contains(item) && filteredHigh.Contains("crypto_module_cmd")
            && filteredLow.Contains(item) && !filteredLow.Contains("crypto_module_cmd"),
            "unchecked booster can be missed at +100 and offered at -100 on its vanilla draw");
        Require(fixture.Miner(30, 100) == fixture.Miner(30, 0), "unchecked ore keeps the native count rolls");

        SetConfig(config, StockSettings.CategoryKey, AllGoodsExcept("retired_aquafarmer:appearance:ras", "wanted2_return:quantity:small_raw_meat"));
        int noRas = Enumerable.Range(0, 50).First(seed => !fixture.Aquafarmer(true, 0, seed).Contains("ras"));
        string[] equipment = fixture.Aquafarmer(true, 100, noRas);
        Require(!equipment.Contains("ras") && equipment.Contains("auto_butcher") && equipment.Contains("marking_pen"),
            "aquaculture equipment uses individual masks at the verified shared helper call sites");
        Require(fixture.Aquafarmer(false, -100, 45).Contains("ras"), "the original guaranteed equipment tier is preserved");
        Require(fixture.Butcher(1, 3, 100) == fixture.Butcher(1, 3, 0) && fixture.Butcher(0, 3, 100) == 3
            && fixture.Butcher(0, 2, -100) == 0, "butcher meat categories control separate native count ranges");

        var ordinaryThief = StockRules.Methods.Single(rule => rule.MethodName == "CreateThief");
        string allThiefCategories = AllGoodsExcept(ordinaryThief.Categories.SelectMany(category => category).Distinct()
            .SelectMany(category => new[] { "thief:appearance:" + category, "thief:quantity:" + category }).ToArray());
        SetConfig(config, StockSettings.CategoryKey, allThiefCategories);
        Require(Enumerable.Range(0, 40).All(seed => fixture.Thief(0, 100, seed).SequenceEqual(fixture.Thief(0, 0, seed))),
            "turning off every category preserves the original random draw order, fallback and offers without perks");
        SetConfig(config, StockSettings.CategoryKey, StockCategoryMask.AllChecked);
        string[] fallback = fixture.Thief(0, -100, 18);
        Require(fallback.Length == 3 && fallback.Distinct().Count() == 1
            && (fallback[0] == "common_chemical" || fallback[0] == "common_medical"), "vanilla fallback");

        TestNativeChoices(fixture, config);

        foreach (var (day, maximum) in new[] { (14, 4), (15, 5), (30, 7), (45, 8), (60, 9) })
        {
            Require(fixture.Miner(day, -100) == 3, "every ore stage retains a minimum of three");
            Require(fixture.Miner(day, 100) == maximum, "ore upper endpoints match the progression explained in the tooltip");
        }
        for (int seed = 0; seed < 30; seed++)
        {
            string[] minimum = fixture.Pharmacist(-100, seed);
            string[] maximum = fixture.Pharmacist(100, seed);
            Require(minimum.Distinct().Count() == 1 && maximum.Distinct().Count() == 1 && minimum[0] == maximum[0],
                "only one vanilla shipment selected");
            Require(maximum.Length > minimum.Length, "bonus copies of the selected shipment");
        }

        Require(fixture.MaterialScavenger(-100, -100).SequenceEqual(new[] { "scrap_metal" }), "guaranteed scrap minimum");
        string[] quantities = fixture.MaterialScavenger(-100, 100);
        Require(quantities.Count(id => id == "scrap_metal") == 5 && quantities.Count(id => id == "printer_plastic") == 2
            && quantities.Count(id => id == "wire") == 1 && quantities.Count(id => id == "flux_agent") == 1
            && !quantities.Contains("box_cutter"), "quantity adjusts additional independent stock slots");
        string[] appearances = fixture.MaterialScavenger(100, -100);
        Require(appearances.Length == 10 && appearances.Count(id => id == "scrap_metal") == 1,
            "appearance setting does not set the quantity of guaranteed scrap");
        Require(fixture.Scavenger("scavGeneral", -100).SequenceEqual(fixture.Scavenger("eleanor6", -100))
            && fixture.Scavenger("scavGeneral", -100).SequenceEqual(fixture.Scavenger("eleanorScav", -100)),
            "Eleanor visits execute the same changed stock rule");
        if (StockRandom.HasNativePaging)
        {
            Require(fixture.MedicalScavenger(false).Contains("fixture_random_injector"),
                "Beta allows the original random syringe offer when the sign allows it");
            Require(!fixture.MedicalScavenger(true).Contains("fixture_random_injector"),
                "Beta's out-of-stock sign still filters the original offer at +100");
        }
        Console.WriteLine("PASS native stock callbacks for thief, miner, pharmacist, material scavenger and Eleanor.");
    }

    private static void TestNativeChoices(VisitFixtures fixture, ModConfig config)
    {
        const string evidence = "crate:LootCrateEvidence";
        const string security = "crate:LootCrateSecurity";
        const string water = "honestBadWaterSeller";
        int waterRule = Array.FindIndex(StockRules.Methods, rule => rule.BeforeVisit
            && rule.MethodName == nameof(StoreClientListWater.SetHonestWaterSellerDialog));
        var waterMethod = StockRules.Methods[waterRule];
        var waterChoice = waterMethod.Choices.Single();
        Require(waterChoice.Categories.SequenceEqual(new[] { "set_base_water", "set_ghost_water" })
            && waterChoice.NativeWeights.SequenceEqual(new[] { 0.75, 0.25 }), "the original water stock sets are mutually exclusive");
        SetConfig(config, water + StockSettings.SelectionEnabledSuffix, true);
        SetConfig(config, StockSettings.CategoryPositionsKey, water + ":selection:water_quality/set_base_water=100");
        Require(StockRandom.ChoiceInt(75, waterRule, 0), "base water can be selected before the customer's dialogue is created");
        SetConfig(config, StockSettings.CategoryPositionsKey, water + ":selection:water_quality/set_ghost_water=100");
        Require(!StockRandom.ChoiceInt(75, waterRule, 0), "the ghostwater setting selects the other native stock set");
        foreach (string customer in new[] { "thief", "rareLowerLevelChemist", "shadyPharma", "inventor",
            "scavCrate", "scavAppraisalRandom" })
            SetConfig(config, customer + StockSettings.SelectionEnabledSuffix, true);
        SetConfig(config, "hypertech_smuggler" + StockSettings.SpecialGoodsEnabledSuffix, true);
        SetConfig(config, StockSettings.CategoryPositionsKey, "thief:selection:crate/loot_crate_security=100");
        Require(!fixture.Thief(0, -100, 45).Any(id => id.StartsWith("crate:")), "forcing a type does not supply an absent optional crate");
        Require(fixture.Thief(0, 100, 45).Contains(security) && fixture.Thief(40, -100, 45).SequenceEqual(new[] { security }),
            "optional and guaranteed crate cases use the same subtype choice");
        SetConfig(config, "thief" + StockSettings.IndividualCategoriesSuffix, true);
        SetConfig(config, StockSettings.CategoryPositionsKey,
            "thief:selection:crate/loot_crate_security=100,thief:appearance:crate_random=-100");
        Require(!fixture.Thief(0, 100, 45).Any(id => id.StartsWith("crate:")), "the individual appearance slider still controls crate eligibility");
        SetConfig(config, "thief" + StockSettings.IndividualCategoriesSuffix, false);
        SetConfig(config, StockSettings.CategoryKey, AllGoodsExcept("thief:appearance:crate_random"));
        int nativeMiss = Enumerable.Range(0, 60).First(seed => !fixture.Thief(0, 0, seed).Any(id => id.StartsWith("crate:")));
        Require(!fixture.Thief(0, 100, nativeMiss).Any(id => id.StartsWith("crate:")), "the appearance checkbox retains the original crate gate");
        SetConfig(config, StockSettings.CategoryKey, StockCategoryMask.AllChecked);
        SetConfig(config, StockSettings.CategoryPositionsKey,
            "thief:selection:crate/loot_crate_evidence=100,thief:selection:crate/loot_crate_security=100,rareLowerLevelChemist:selection:shipment/pink_injector=100");
        var selected = Enumerable.Range(0, 70).Select(seed => fixture.Thief(40, -100, seed).Single()).ToHashSet();
        Require(selected.SetEquals(new[] { evidence, security }), "forced crate types share the exclusive native slot");
        Require(fixture.Pharmacist(-100, 45).SequenceEqual(Enumerable.Repeat("pink_injector", 2))
            && fixture.Pharmacist(100, 45).SequenceEqual(Enumerable.Repeat("pink_injector", 4)),
            "forced shipment can still adjust its own quantity without producing other shipments");
        SetConfig(config, StockSettings.CategoryPositionsKey,
            "thief:selection:crate/loot_crate_evidence=100,thief:selection:crate/loot_crate_security=50,"
            + "rareLowerLevelChemist:selection:shipment/dream_dust=100,rareLowerLevelChemist:selection:shipment/pink_injector=50");
        Require(Enumerable.Range(0, 100).Select(seed => fixture.Thief(40, -100, seed).Single()).ToHashSet()
            .SetEquals(new[] { evidence, security }), "a finite positive crate setting competes with +100");
        var shipments = Enumerable.Range(0, 100).Select(seed => fixture.Pharmacist(-100, seed)).ToArray();
        Require(shipments.All(shipment => shipment.Distinct().Count() == 1)
            && shipments.Select(shipment => shipment[0]).ToHashSet().SetEquals(new[] { "dream_dust", "pink_injector" }),
            "a finite positive medicine setting competes with +100 in the native shipment sequence");
        SetConfig(config, StockSettings.CategoryPositionsKey,
            "thief:selection:crate/loot_crate_evidence=-100,thief:selection:crate/loot_crate_security=-100");
        Require(Enumerable.Range(0, 20).All(seed => !new[] { evidence, security }.Contains(fixture.Thief(40, -100, seed).Single())),
            "excluded crate types never appear");
        TestCrateClassifications(fixture, config);

        SetConfig(config, StockSettings.CategoryPositionsKey,
            "shadyPharma:selection:shipment/random_inspectable_injector=100");
        Require(!fixture.UpperLevelPharmacist(false).Contains("fixture_random_injector")
            && fixture.UpperLevelPharmacist(true).Contains("fixture_random_injector"),
            "the upper-level pharmacist weights only the unlocked shipment variants");
        SetConfig(config, StockSettings.CategoryPositionsKey,
            "scavAppraisalRandom:selection:appraisal/hand=100,scavAppraisalRandom:selection:appraisal/ribwich=-100");
        Require(Enumerable.Range(0, 20).All(seed => fixture.AppraisalChoice(3, seed, "hand") == "plasma_fuel"),
            "the examination certificate removes unchecked types before slider weighting");
        Require(fixture.AppraisalChoice(0, 45) == "hand", "locked appraisal types never enter the pool");
        string[] originalAppraisal = Enumerable.Range(0, 20).Select(seed => fixture.AppraisalChoice(3, seed, "ribwich")).ToArray();
        SetConfig(config, StockSettings.CategoryPositionsKey,
            "scavAppraisalRandom:selection:appraisal/hand=-100,scavAppraisalRandom:selection:appraisal/ribwich=-100,"
            + "scavAppraisalRandom:selection:appraisal/plasma_fuel=-100");
        Require(fixture.AppraisalChoice(3, 45, "hand", "plasma_fuel") == "ribwich",
            "the sole enabled certificate type stays native even at -100");
        var nativeAppraisal = Enumerable.Range(0, 20).Select(seed => fixture.AppraisalChoice(3, seed, "ribwich")).ToArray();
        SetConfig(config, "scavAppraisalRandom" + StockSettings.SelectionEnabledSuffix, false);
        Require(nativeAppraisal.SequenceEqual(Enumerable.Range(0, 20).Select(seed => fixture.AppraisalChoice(3, seed, "ribwich")))
            && originalAppraisal.All(id => id == "hand"), "all -100 keeps the filtered native draw and its random path");
        Require(fixture.RequisitionAmmoRoll(false) && !fixture.RequisitionAmmoRoll(true),
            "tier I ammunition changes from appearance to quantity when tier II supplies the same ammunition");

        SetConfig(config, "inventor" + StockSettings.IndividualCategoriesSuffix, true);
        SetConfig(config, "inventorVisitInvestment2" + StockSettings.IndividualCategoriesSuffix, true);
        SetConfig(config, "inventorVisitInvestment1.override", true);
        SetConfig(config, "inventorVisitInvestment1.quantity", 100);
        SetConfig(config, StockSettings.CategoryPositionsKey,
            "inventor:quantity:chem_module=100,inventorVisitInvestment2:quantity:chem_module=-100");
        Require(fixture.JacksonChemistryRoll(true, 0, 68) && !fixture.JacksonChemistryRoll(true, 2, 65)
            && !fixture.JacksonChemistryRoll(false, 2, 65)
            && fixture.JacksonChemistryRoll(true, 1, 68) && fixture.JacksonChemistryRoll(false, 1, 68),
            "Jackson uses the same adjustment in both locations, with separate investment stages");

        SetConfig(config, StockSettings.CategoryPositionsKey,
            "inventor:selection:backpack/backpack_large=100,inventor:selection:backpack/backpack_medium=-100,hypertech_smuggler:selection:shipment/hypertech_bay=100");
        Require(fixture.JacksonBackpackRoll(60, 0) && !fixture.JacksonBackpackRoll(78, 1, "backpack_large")
            && fixture.JacksonBackpackRoll(190, 2, "backpack_large"), "outdoor selection keeps the original stock eligibility");
        string[] hypertech = fixture.Hypertech(45);
        Require(hypertech.Length == 5 && hypertech[0] == "hypertech_bay" && hypertech.Count(id => id == "hypertech_bay") == 1,
            "one-per-visit offers are removed from the native pool even when forced");

        TestTableChoices(config);
        TestExactQuantities(fixture, config);
        SetConfig(config, StockSettings.CategoryPositionsKey, "");
        Console.WriteLine("PASS conditional crates, exclusive pharmacy shipments, and shared Jackson settings by investment stage.");
    }

    private static void TestExactQuantities(VisitFixtures fixture, ModConfig config)
    {
        const string smuggler = "hypertech_smuggler";
        const string electronics = "hypertech_electronic";
        const string turbo = "turbo_booster_hypertech";
        const string bay = "hypertech_bay";
        const string addon = "addictGeneralBarter";
        const string tab = "packedFoodTable";
        var settings = StockRandom.Settings!;
        var smugglerProfile = settings.Customers.Single(customer => customer.Id == smuggler);
        var shipment = settings.ChoicesFor(smugglerProfile).Single();
        var seeds = Enumerable.Range(0, 32).ToArray();
        string Mode(string customer, string group) => $"{customer}:allocation:{group}=1";
        string Count(string customer, string group, string item, int count) => $"{customer}:allocation:{group}/{item}={count + 1}";
        void Positions(params string[] entries) => SetConfig(config, StockSettings.CategoryPositionsKey, string.Join(",", entries));

        Positions();
        string[][] vanilla = seeds.Select(fixture.Hypertech).ToArray();
        Require(vanilla.All(visit => visit.Length == 5), "the native shipment has five slots");
        double[] equalWeights = Enumerable.Repeat(1d, shipment.Categories.Length).ToArray();
        var settingsForFirst = new int[equalWeights.Length];
        foreach (var (level, nonLinear, linear) in new[] { (-100, 0d, 0d), (-50, 0.0625, 1d / 11), (50, 0.375, 7d / 12), (100, 1d, 1d) })
        {
            settingsForFirst[0] = level;
            Require(Math.Abs(StockCurve.ChoiceWeights(equalWeights, settingsForFirst)![0] - nonLinear) < 1e-10
                && Math.Abs(StockCurve.ChoiceWeights(equalWeights, settingsForFirst, true)![0] - linear) < 1e-10,
                "the documented first-roll values match both curves");
        }
        string exact = Mode(smuggler, shipment.Id);
        string savedWeight = $"{smuggler}:selection:{shipment.Id}/{turbo}=100";
        Positions(exact, savedWeight);
        Require(seeds.All(seed => fixture.Hypertech(seed).SequenceEqual(vanilla[seed])), "blank quantities ignore stored sliders and preserve native seeded output");
        Positions(exact, savedWeight, Count(smuggler, shipment.Id, electronics, 5));
        Require(seeds.All(seed => fixture.Hypertech(seed).SequenceEqual(Enumerable.Repeat(electronics, 5))), "five specified electronics fill the shipment");
        Positions(exact, savedWeight, Count(smuggler, shipment.Id, electronics, 4), Count(smuggler, shipment.Id, turbo, 1));
        Require(seeds.All(seed => fixture.Hypertech(seed).SequenceEqual(Enumerable.Repeat(electronics, 4).Append(turbo))),
            "exact quantities give four electronics and one turbo on every visit");
        Require(settings.GetChoicePositions(smugglerProfile, shipment) == null, "the saved weight is inactive in exact mode");
        Require(settings.CustomerChanges().Any(change => change.Profile == smugglerProfile && change.Status == StockChangeStatus.Effective
            && change.Lines.Contains("<color=#D1C8A5>特殊商品设置 · 货物 · 指定数量</color>")
            && change.Lines.Any(line => line.Contains(StockAllocation.ModeLabel))
            && change.Lines.Any(line => line.Contains(StockCatalog.Name(electronics)) && line.Contains(StockAllocation.DescribeCount(4)))),
            "the overview labels the effective mode and quantities");
        Require(settings.CustomerChanges().Any(change => change.Profile == smugglerProfile && change.Status == StockChangeStatus.Superseded
            && change.Lines.Any(line => line.Contains(StockCatalog.Name(turbo)) && line.Contains("+100"))),
            "quantity mode supersedes the saved weight for the same group");
        var draft = new StockSettings.Draft(_ => null, _ => null, new StockCategoryMask(StockCategoryMask.AllChecked),
            new StockCategoryPositions(string.Join(",", new[] { exact, Count(smuggler, shipment.Id, electronics, 3) })));
        Require(settings.AllocationError(draft) == null && settings.CustomerChanges(draft).Any(change =>
            change.Profile == smugglerProfile && change.Status == StockChangeStatus.Pending
            && change.Lines.Any(line => line.Contains(StockAllocation.DescribeCount(4) + " → " + StockAllocation.DescribeCount(3)))),
            "the overview uses current draft quantities while native stock uses the applied values");
        draft.Positions.Set(smuggler, StockAxis.Allocation, shipment.Key(Array.IndexOf(shipment.Categories, bay)), 3);
        Require(settings.AllocationError(draft)?.Contains(StockCatalog.Name(bay)) == true, "apply rejects a draft above an item limit");
        Positions(exact, Count(smuggler, shipment.Id, electronics, 2), Count(smuggler, shipment.Id, bay, 1), Count(smuggler, shipment.Id, turbo, 0));
        Require(seeds.All(seed =>
        {
            string[] visit = fixture.Hypertech(seed);
            return visit.Length == 5 && visit[0] == bay && visit.Count(id => id == electronics) == 2
                && !visit.Contains(turbo) && shipment.Quota!.SingleUnit.All(id => visit.Count(item => item == id) <= 1);
        }), "capped items come first and only blank candidates can fill remaining slots");
        var mixed = settings.Allocation(smugglerProfile, shipment);
        Require(mixed.FixedCount == 3 && mixed.Remaining == 2
            && StockChoiceDetails.AllocationDetails(smugglerProfile, shipment, mixed,
                branch => settings.StockCount(smugglerProfile, shipment, branch), true).Contains(StockAllocation.DescribeCount(0)),
            "mixed exact detail renders total, remaining, and exclusion");
        Positions(exact, Count(smuggler, shipment.Id, bay, 2));
        Require(seeds.All(seed => fixture.Hypertech(seed).SequenceEqual(vanilla[seed])), "invalid persisted quantities safely use the native visit");
        Positions(savedWeight, Count(smuggler, shipment.Id, electronics, 4));
        Require(fixture.Hypertech(45).SequenceEqual(Enumerable.Repeat(turbo, 5)), "switching off exact quantities restores previous weights");
        Require(settings.CustomerChanges().Any(change => change.Profile == smugglerProfile && change.Status == StockChangeStatus.Superseded
            && change.Lines.Any(line => line.Contains(StockCatalog.Name(electronics)) && line.Contains(StockAllocation.DescribeCount(4)))),
            "weight mode supersedes previously specified quantities");
        SetConfig(config, smuggler + StockSettings.SpecialGoodsEnabledSuffix, false);
        var disabledGroup = settings.CustomerChanges().Where(change => change.Profile == smugglerProfile).ToArray();
        Require(disabledGroup.Length == 1 && disabledGroup[0].Status == StockChangeStatus.Inactive
            && disabledGroup[0].Lines.Any(line => line.Contains(StockCatalog.Name(electronics))),
            "disabled category adjustments are inactive rather than superseded");
        SetConfig(config, StockSettings.AllStockCategoriesKey, true);
        Require(fixture.Hypertech(0).SequenceEqual(vanilla[0]), "ordinary category activation leaves special goods on the native path");
        SetConfig(config, StockSettings.AllStockCategoriesKey, false);
        SetConfig(config, StockSettings.AllSpecialGoodsKey, true);
        Require(fixture.Hypertech(45).SequenceEqual(Enumerable.Repeat(turbo, 5)),
            "global special goods activation enables the weights with the customer switch off");
        Positions(exact, Count(smuggler, shipment.Id, electronics, 5));
        Require(fixture.Hypertech(45).SequenceEqual(Enumerable.Repeat(electronics, 5)),
            "global special goods activation honors exact quantities");
        SetConfig(config, StockSettings.AllSpecialGoodsKey, false);
        Require(fixture.Hypertech(0).SequenceEqual(vanilla[0])
            && settings.ActiveAllocation(smugglerProfile, shipment, shipment.Categories) == null,
            "switching global special goods activation off restores the customer's own setting");
        SetConfig(config, smuggler + StockSettings.SpecialGoodsEnabledSuffix, true);

        const string fish = "retired_aquafarmer";
        const string wine = "retired_winemaker";
        const string eleanor = "eleanor5";
        foreach (string customer in new[] { addon, fish, wine, eleanor })
            SetConfig(config, customer + StockSettings.SpecialGoodsEnabledSuffix, true);
        Positions(Mode(fish, "fish"), Count(fish, "fish", "eel", 2), Count(fish, "fish", "sturgeon", 1),
            Mode(wine, "yeast"), Count(wine, "yeast", "wine_superyeast", 1), Count(wine, "yeast", "wine_yeast", 1));
        Require(fixture.Aquafarmer(true, 0, 45).Where(id => id.StartsWith("fish:")).SequenceEqual(new[] { "fish:0", "fish:0", "fish:2" })
            && !fixture.Aquafarmer(false, 0, 45).Any(id => id.StartsWith("fish:")), "fixed fish composition preserves the native upgrade gate");
        var yeast = fixture.Winemaker(45);
        Require(yeast.Count(id => id == "wine_superyeast") == 1 && yeast.Count(id => id == "wine_yeast") == 3,
            "specified additional yeast preserves the two fixed vanilla units");

        const string source = "better_customer_stock.verification";
        LootRegistry.RegisterTable(source, "junkTable", ("glass_shard", 1f), ("junk", 1f));
        LootRegistry.RegisterTable(source, "materialTable", ("wire", 1f), ("scrap_metal", 1f));
        Positions(Mode(eleanor, "junkTable"), Count(eleanor, "junkTable", "glass_shard", 4),
            Mode(eleanor, "materialTable"), Count(eleanor, "materialTable", "wire", 2));
        Require(fixture.EleanorMaterials(45).SequenceEqual(Enumerable.Repeat("glass_shard", 4).Concat(new[] { "wire", "wire", "scrap_metal" })),
            "Eleanor's two tables fill their own quantities with independent exact modes");

        SetConfig(config, addon + StockSettings.IndividualCategoriesSuffix, true);
        var optional = StockRules.Methods.Single(rule => rule.MethodName == nameof(BarterOfferList.CreateAddictGeneralBarter))
            .Categories.SelectMany(ids => ids).Select(id => $"{addon}:appearance:{id}={(id == tab ? 100 : -100)}");
        Positions(optional.Concat(new[] { Mode(addon, tab), Count(addon, tab, "cup_noodle", 2) }).ToArray());
        string[][] offers = fixture.AddictOffers(45);
        Require(offers.Length == 4 && offers[3].SequenceEqual(new[] { "cup_noodle", "cup_noodle", "cat_bar" }),
            "quantity mode controls the three food units within a single quote");
        Positions(optional.Select(value => value.Replace("=100", "=-100")).Append(Mode(addon, tab)).Append(Count(addon, tab, "cup_noodle", 3)).ToArray());
        Require(fixture.AddictOffers(45).Length == 3, "quantity configuration does not create a new optional quote");

        SetConfig(config, addon + StockSettings.IndividualCategoriesSuffix, false);
        Positions();
        Console.WriteLine("PASS exact quantities for six stock groups, native limits, split modes, and separate barter offers.");

        foreach (StockChoice choice in settings.Customers.SelectMany(settings.ChoicesFor).Where(choice => choice.Quota != null))
        {
            StockQuota quota = choice.Quota!;
            string[] items = choice.Categories;
            int primary = Array.FindIndex(items, item => quota.LimitFor(item) == quota.Total);
            int secondary = Array.FindIndex(items, item => item != items[primary]);
            int[] counts = Enumerable.Repeat(StockAllocation.Random, items.Length).ToArray();
            counts[primary] = quota.Total;
            counts[secondary] = 0;
            var full = new StockAllocation(choice, counts);
            Require(full.EditableLimit(items[primary]) == quota.Total && items.Where((_, index) => index != primary)
                .All(item => full.EditableLimit(item) == 0), "a full group disables every unallocated slider");
            counts[primary]--;
            var partial = new StockAllocation(choice, counts);
            Require(partial.EditableLimit(items[primary]) == quota.Total && !partial.IsRandom(items[secondary])
                && items.Where((_, index) => index != primary).All(item => partial.EditableLimit(item) == 1),
                "freeing a slot restores the other sliders and preserves the zero or random settings");
            counts[secondary] = 1;
            var split = new StockAllocation(choice, counts);
            Require(split.EditableLimit(items[primary]) == quota.Total - 1 && split.EditableLimit(items[secondary]) == 1
                && items.Where((_, index) => index != primary && index != secondary).All(item => split.EditableLimit(item) == 0),
                "both allocated sliders can be reduced without exceeding their shared total");
            counts[primary] = quota.Total;
            var overfilled = new StockAllocation(choice, counts);
            Require(overfilled.EditableLimit(items[primary]) == quota.Total && overfilled.EditableLimit(items[secondary]) == 1
                && overfilled.ValidationError(items) != null, "an overfilled saved group remains editable downward");
        }
        Require(mixed.EditableLimit(bay) == 1, "available slots cannot raise a limited item above one");
        Console.WriteLine("PASS live slider limits, full groups, and freed capacity across all six exact-quantity groups.");
    }

    private static void TestTableChoices(ModConfig config)
    {
        const string table = "packedFoodTable";
        const string source = "better_customer_stock.verification";
        LootRegistry.RegisterTable(source, table, ("cup_noodle", 1f), ("cat_bar", 5f));
        int rule = Array.FindIndex(StockRules.Methods, method => method.MethodName == nameof(StoreClientList.CreateScavCrate));
        var profile = StockRules.Methods[rule].Profiles.Single();
        var choice = StockRules.Methods[rule].Choices.Single();
        var draft = new int[choice.Categories.Length];
        draft[Array.IndexOf(choice.Categories, "cup_noodle")] = 100;
        draft[Array.IndexOf(choice.Categories, "cat_bar")] = 50;
        string preview = StockChoiceDetails.DescribeProbabilities(profile, choice, draft, true);
        Require(preview.Contains(StockCatalog.Items["cup_noodle"].Name + "　16.67% → <b>44.44%</b>")
            && preview.Contains(StockCatalog.Items["cat_bar"].Name + "　83.33% → <b>55.56%</b>"),
            "table previews use the actual registered eligible weights alongside the in-window drafts");
        SetConfig(config, StockSettings.CategoryPositionsKey, "scavCrate:selection:packedFoodTable/cup_noodle=100");
        int hooks = 0;
        void OnLootRolled(LootRollContext context)
        {
            Require(context.tableId == table && context.itemId == "cup_noodle", "native hook sees the selected table entry");
            context.itemId = "cat_bar";
            hooks++;
        }

        ModHook.OnLootRolled += OnLootRolled;
        try
        {
            Require(StockRandom.ChoiceTableEntry(table, -1, rule) == "cat_bar" && hooks == 1,
                "stock-table selection fires the native loot event and returns its result exactly once");
        }
        finally
        {
            ModHook.OnLootRolled -= OnLootRolled;
        }
        Require(LootRegistry.GetTableEntries(table).Select(entry => entry.weight).SequenceEqual(new[] { 1f, 5f }),
            "customer settings keep the shared loot table unchanged");

        int pettyRule = Array.FindIndex(StockRules.Methods, method => method.MethodName == nameof(StoreClientList.CreatePettyThief));
        var petty = StockRules.Methods[pettyRule];
        Require(petty.Choices.Select(group => group.Id).SequenceEqual(new[] { "packedFoodTable", "householdTable", "medicalTable" })
            && petty.Choices.Select(group => group.Categories.Length).SequenceEqual(new[] { 13, 8, 8 })
            && petty.Choices.SelectMany(group => group.Categories).Distinct().Count() == 29,
            "the petty thief's three native tables keep distinct groups and candidates");
        LootRegistry.RegisterTable(source, "householdTable", ("caffeine_pill", 1f), ("shampoo", 1f));
        LootRegistry.RegisterTable(source, "medicalTable", ("bandage_item", 1f), ("surgery_tool", 1f));
        SetConfig(config, "pettyThief" + StockSettings.SelectionEnabledSuffix, true);
        foreach (var (changedTable, selected) in new[] { ("packedFoodTable", "cup_noodle"), ("householdTable", "shampoo"),
                     ("medicalTable", "surgery_tool") })
        {
            SetConfig(config, StockSettings.CategoryPositionsKey, $"pettyThief:selection:{changedTable}/{selected}=100");
            foreach (StockChoice choiceGroup in petty.Choices)
                for (int seed = 0; seed < 12; seed++)
                {
                    UnityEngine.Random.InitState(seed);
                    string native = TableMaster.Roll(choiceGroup.Id);
                    UnityEngine.Random.InitState(seed);
                    string actual = StockRandom.ChoiceTableEntry(choiceGroup.Id, -1, pettyRule);
                    Require(actual == (choiceGroup.Id == changedTable ? selected : native),
                        "a petty thief classification setting only changes its own native table");
                }
        }
        Console.WriteLine("PASS independent petty thief tables and shared native loot events.");
    }

    private static void TestCrateClassifications(VisitFixtures fixture, ModConfig config)
    {
        var rule = StockRules.Methods.Single(method => method.MethodName == nameof(StoreClientList.CreateThief));
        var crates = rule.Choices.Single(choice => choice.Id == "crate");
        string[] outputs = { "crate:LootCrateEvidence", "crate:LootCrateMedical", "crate:LootCrateEngineering",
            "crate:LootCrateSecurity", "crate:LootCrateService" };
        var single = new[] { 0d, 0d, 1d, 0d, 0d };
        var pair = new[] { 21d / 41, 0d, 20d / 41, 0d, 0d };
        var cases = new[]
        {
            (new[] { 0, 0, 100, 0, 0 }, single, single),
            (new[] { 100, 0, 100, 0, 0 }, pair, pair),
            (new[] { -100, -100, 0, -100, -100 }, single, single),
            (new[] { -50, -50, 50, -50, -50 }, new[] { 21d / 261, 20d / 261, 20d / 29, 15d / 261, 25d / 261 },
                new[] { 21d / 242, 20d / 242, 161d / 242, 15d / 242, 25d / 242 }),
            (new[] { 50, -50, 50, -50, -50 }, new[] { 63d / 143, 20d / 429, 60d / 143, 15d / 429, 25d / 429 },
                new[] { 1176d / 2911, 20d / 284, 1120d / 2911, 15d / 284, 25d / 284 }),
        };
        foreach (bool linear in new[] { false, true })
        {
            SetConfig(config, StockSettings.LinearChangesKey, linear);
            foreach (var (positions, originalCurve, linearCurve) in cases)
            {
                double[] expected = linear ? linearCurve : originalCurve;
                SetConfig(config, StockSettings.CategoryPositionsKey, string.Join(",",
                    positions.Select((position, index) => $"thief:selection:{crates.Key(index)}={position}")));
                var weights = StockCurve.ChoiceWeights(crates.RangeWeights(100), positions, linear)!;
                const int sampleCount = 256;
                var picked = Enumerable.Range(0, sampleCount).Select(seed => fixture.Thief(40, -100, seed).Single()).ToArray();
                for (int index = 0; index < outputs.Length; index++)
                {
                    Near(expected[index], weights[index], "expected conditional crate probability");
                    double frequency = picked.Count(item => item == outputs[index]) / (double)sampleCount;
                    Require(Math.Abs(expected[index] - frequency) <= (expected[index] is 0 or 1 ? 0 : 0.09),
                        $"crate callback agrees with the distribution for {outputs[index]} at {string.Join(",", positions)}");
                }
            }
        }
        SetConfig(config, StockSettings.LinearChangesKey, false);
        Console.WriteLine("PASS five crate classifications with both curves and actual supply callbacks.");
    }

    private static void TestDefaultSelections(StockMethod[] rules)
    {
        var config = CreateConfig();
        var settings = new StockSettings(config, rules);
        var selected = new StockCategoryMask(config.Get(StockSettings.CategoryKey).Serialize());
        Require(settings.Customers.Where(profile => !profile.FixedStock)
            .All(profile => config.Get(profile.Id + ".enabled") is ConfigEntry<bool> { Default: false, Value: false }),
            "fresh customer checkboxes start unchecked");
        Require(settings.Customers.All(profile => new[] { StockAxis.Appearance, StockAxis.Quantity }.All(axis =>
            settings.CategoriesFor(profile, axis).All(item => !selected.IsChecked(profile.Id, axis, item)))),
            "fresh goods checkboxes start unchecked");
        Require(!settings.GlobalChanges().Any() && !settings.CustomerChanges().Any(), "fresh settings start with an empty overview");
        var previewOff = new StockSettings.Draft(key => key == StockSettings.InlinePreviewKey ? false : (bool?)null,
            _ => null, selected, new StockCategoryPositions(""));
        Require(!settings.GlobalChanges(previewOff).Any(), "a pending live preview change stays out of the overview");
        SetConfig(config, StockSettings.InlinePreviewKey, false);
        Require(!settings.GlobalChanges().Any(), "a saved live preview change stays out of the overview");

        var thiefRule = rules.Single(rule => rule.MethodName == nameof(StoreClientList.CreateThief));
        var thief = thiefRule.Profiles.Single();
        int engineering = Array.FindIndex(thiefRule.Categories, categories => categories.Contains("eng_keycard"));
        int medical = Array.FindIndex(thiefRule.Categories, categories => categories.Contains("med_keycard"));
        Require(engineering >= 0 && medical >= 0, "the example uses two separate native keycard rolls");
        SetConfig(config, "default.appearance", 50);
        Require(settings.Get(thiefRule, thief, engineering) == 0
            && settings.GlobalChanges().Contains((StockChangeStatus.Inactive, "出货概率　+50")),
            "a global slider without any selections remains inactive");

        SetConfig(config, "thief.enabled", true);
        SetConfig(config, "thief.override", true);
        SetConfig(config, "thief.appearance", 100);
        selected.SetChecked(thief.Id, StockAxis.Appearance, "eng_keycard", true);
        SetConfig(config, StockSettings.CategoryKey, selected.Save());
        Require(settings.Get(thiefRule, thief, engineering) == 100 && settings.Get(thiefRule, thief, medical) == 0
            && settings.CustomerChanges().Single(change => change.Status == StockChangeStatus.Effective).Lines
                .Contains("一次性钥匙卡（工程）　已勾选"), "one customer and one good can be selected with both inversions off");

        var apply = AccessTools.Method(typeof(ConfigEntry), "ApplyBoxed");
        foreach (ConfigEntry entry in config.Entries)
            apply.Invoke(entry, new[] { entry.BoxedDefault });
        Require(!settings.GlobalChanges().Any() && !settings.CustomerChanges().Any()
            && config.Get(StockSettings.InlinePreviewKey) is ConfigEntry<bool> { Value: true }
            && settings.Get(thiefRule, thief, engineering) == 0, "reset restores live previews and clears stock adjustments");
        SetConfig(config, StockSettings.InvertCustomerKey, true);
        SetConfig(config, StockSettings.InvertCategoryKey, true);
        SetConfig(config, "default.appearance", 50);
        Require(settings.Get(thiefRule, thief, engineering) == 50 && settings.Get(thiefRule, thief, medical) == 50,
            "both inversions allow the global slider to affect fresh unchecked goods");
        SetConfig(config, "thief.enabled", true);
        Require(settings.Get(thiefRule, thief, engineering) == 0 && settings.Get(thiefRule, thief, medical) == 0,
            "checking one customer under inversion excludes that customer");
        Console.WriteLine("PASS opt-in defaults, single-customer setup, global exclusions, reset, and overview status.");
    }

    private static void TestGlobalStockModes(StockMethod[] rules)
    {
        ModConfig config = CreateConfig();
        var settings = new StockSettings(config, rules);
        const string goodsMode = StockSettings.AllIndividualCategoriesKey;
        const string categoryMode = StockSettings.AllStockCategoriesKey;
        const string specialMode = StockSettings.AllSpecialGoodsKey;
        var thiefRule = rules.Single(rule => rule.MethodName == nameof(StoreClientList.CreateThief));
        StockProfile thief = thiefRule.Profiles.Single();
        var minerRule = rules.Single(rule => rule.MethodName == nameof(StoreClientList.CreateMiner));
        StockProfile miner = minerRule.Profiles.Single();
        StockProfile smuggler = settings.Customers.Single(profile => profile.Id == "hypertech_smuggler");
        StockChoice crate = settings.ChoicesFor(thief).Single(choice => choice.Id == "crate");
        StockChoice shipment = settings.ChoicesFor(smuggler).Single();
        int engineeringCard = Array.FindIndex(thiefRule.Categories, categories => categories.Contains("eng_keycard"));
        int ore = Array.FindIndex(minerRule.Categories, categories => categories.Contains("common_ore"));

        Require(new[] { goodsMode, categoryMode, specialMode }.All(key => !((ConfigEntry<bool>)config.Get(key)).Value),
            "fresh configurations start with global modes off");
        EnableAllCustomers(config);
        SetConfig(config, "default.appearance", 70);
        SetConfig(config, "default.quantity", -80);
        SetConfig(config, StockSettings.InvertCategoryKey, true);
        SetConfig(config, "thief.override", true);
        SetConfig(config, "thief.appearance", 25);
        SetConfig(config, "addictGeneralBarter" + StockSettings.IndividualCategoriesSuffix, true);
        SetConfig(config, "pettyThief" + StockSettings.SelectionEnabledSuffix, true);
        string savedMask = AllGoodsExcept("thief:appearance:eng_keycard", "miner:quantity:common_ore");
        const string savedPositions = "thief:appearance:eng_keycard=100,miner:quantity:common_ore=100,"
            + "thief:selection:crate/loot_crate_engineering=100,hypertech_smuggler:allocation:shipment=1,"
            + "hypertech_smuggler:allocation:shipment/hypertech_electronic=6";
        SetConfig(config, StockSettings.CategoryKey, savedMask);
        SetConfig(config, StockSettings.CategoryPositionsKey, savedPositions);
        Require(settings.Get(thiefRule, thief, engineeringCard) == 25 && settings.Get(minerRule, miner, ore) == -80
            && !settings.ChoiceEnabled(thief, crate),
            "the saved checkbox mask and customer settings are used before the global switches are enabled");

        var localModes = config.Entries.OfType<ConfigEntry<bool>>().Where(entry =>
            entry.Key.EndsWith(StockSettings.IndividualCategoriesSuffix, StringComparison.Ordinal)
            || entry.Key.EndsWith(StockSettings.SelectionEnabledSuffix, StringComparison.Ordinal)
            || entry.Key.EndsWith(StockSettings.SpecialGoodsEnabledSuffix, StringComparison.Ordinal))
            .Select(entry => (Entry: entry, Value: entry.Value)).ToArray();
        int[] baselinePositions = StockPositions();
        bool[] baselineCategories = CategoryStates();
        var draft = new StockSettings.Draft(key => key == goodsMode || key == categoryMode || key == specialMode ? true : (bool?)null,
            _ => null, new StockCategoryMask(savedMask), new StockCategoryPositions(savedPositions));
        const string globalGoods = "统一使用滑动条替代";
        const string globalCategories = "开启全部货物分类";
        const string globalSpecial = "为全部顾客启用特殊商品设置";
        Require(settings.GlobalChanges(draft).Where(change => change.Status == StockChangeStatus.Pending)
            .Select(change => change.Line).SequenceEqual(new[]
            {
                globalGoods + "　未勾选 → 已勾选",
                globalCategories + "　未勾选 → 已勾选",
                globalSpecial + "　未勾选 → 已勾选"
            }), "global mode drafts belong to the global chapter");
        string pendingCard = settings.ProductComparison(thief, StockAxis.Appearance, "eng_keycard", draft);
        Require(pendingCard != settings.ProductComparison(thief, StockAxis.Appearance, "eng_keycard")
            && settings.GetChoicePositions(thief, crate, draft)?.Contains(100) == true && !settings.ChoiceEnabled(thief, crate),
            "live comparisons use the draft while native stock keeps the applied modes");

        SetConfig(config, categoryMode, true);
        Require(settings.ChoiceEnabled(thief, crate) && settings.Get(thiefRule, thief, engineeringCard) == 25
            && settings.ActiveAllocation(smuggler, shipment, shipment.Categories) == null,
            "the category switch works independently of both individual goods and special goods");
        SetConfig(config, categoryMode, false);
        SetConfig(config, specialMode, true);
        Require(!settings.ChoiceEnabled(thief, crate) && settings.ChoiceEnabled(smuggler, shipment)
            && settings.ActiveAllocation(smuggler, shipment, shipment.Categories)?.FixedCount == 5,
            "special goods activation honors the saved exact quantities without enabling ordinary category rolls");
        SetConfig(config, specialMode, false);
        SetConfig(config, goodsMode, true);
        Require(settings.Get(thiefRule, thief, engineeringCard) == 100 && settings.Get(minerRule, miner, ore) == 100
            && !settings.ChoiceEnabled(thief, crate),
            "global goods sliders bypass the checkbox inversion and leave the category setting independent");
        Require(pendingCard == settings.ProductComparison(thief, StockAxis.Appearance, "eng_keycard"),
            "the applied goods mode matches its live preview");
        SetConfig(config, categoryMode, true);
        SetConfig(config, specialMode, true);
        Require(settings.ActiveAllocation(smuggler, shipment, shipment.Categories)?.FixedCount == 5
            && settings.GetChoicePositions(smuggler, shipment) == null,
            "special goods preserve the exact-quantity mode alongside category adjustment");
        var globalOverview = settings.GlobalChanges().ToLookup(change => change.Status, change => change.Line);
        var customerOverview = settings.CustomerChanges().ToArray();
        var overview = customerOverview.Where(change => change.Profile == thief)
            .ToDictionary(change => change.Status, change => change.Lines);
        Require(globalOverview[StockChangeStatus.Effective].Count(line => line == globalGoods + "　已勾选") == 1
            && globalOverview[StockChangeStatus.Effective].Count(line => line == globalCategories + "　已勾选") == 1
            && globalOverview[StockChangeStatus.Effective].Count(line => line == globalSpecial + "　已勾选") == 1
            && globalOverview[StockChangeStatus.Superseded].Contains("出货概率　+70")
            && globalOverview[StockChangeStatus.Superseded].Contains("货物数量　-80"),
            "global settings appear once in their own chapter and superseded global sliders stay there");
        Require(customerOverview.SelectMany(change => change.Lines)
            .All(line => !line.StartsWith(globalGoods, StringComparison.Ordinal) && !line.StartsWith(globalCategories, StringComparison.Ordinal)
                && !line.StartsWith(globalSpecial, StringComparison.Ordinal))
            && overview[StockChangeStatus.Effective].Contains("一次性钥匙卡（工程）　独立档位 +100")
            && customerOverview.Any(change => change.Profile == miner && change.Status == StockChangeStatus.Effective)
            && customerOverview.Any(change => change.Profile == smuggler && change.Status == StockChangeStatus.Superseded
                && change.Lines.Contains("启用特殊商品设置　未勾选"))
            && overview[StockChangeStatus.Superseded].Contains("用滑动条逐项设置商品类别　未勾选")
            && overview[StockChangeStatus.Superseded].Contains("启用货物分类调整　未勾选"),
            "per-customer changes retain local effects and superseded checkboxes without repeating the global settings");
        Require(localModes.All(mode => mode.Entry.Value == mode.Value)
            && config.Get(StockSettings.CategoryKey).Serialize() == savedMask
            && config.Get(StockSettings.CategoryPositionsKey).Serialize() == savedPositions,
            "global modes do not rewrite any customer settings");
        int[] forcedPositions = StockPositions();
        bool[] forcedCategories = CategoryStates();
        SetConfig(config, goodsMode, false);
        SetConfig(config, categoryMode, false);
        SetConfig(config, specialMode, false);
        Require(StockPositions().SequenceEqual(baselinePositions) && CategoryStates().SequenceEqual(baselineCategories),
            "turning global modes off restores mixed customer settings and previous defaults");
        foreach (var (entry, _) in localModes)
            SetConfig(config, entry.Key, true);
        Require(StockPositions().SequenceEqual(forcedPositions) && CategoryStates().SequenceEqual(forcedCategories),
            "global modes have the same effect across all supported customer profiles as enabling every local mode");

        SetConfig(config, goodsMode, true);
        SetConfig(config, categoryMode, true);
        SetConfig(config, specialMode, true);
        SetConfig(config, "default.appearance.enabled", false);
        Require(settings.Get(thiefRule, thief, engineeringCard) == 0 && settings.ChoiceEnabled(thief, crate),
            "the stock chance switch still controls individual stock chance");
        Require(settings.GlobalChanges().Contains((StockChangeStatus.Inactive, "出货概率　+70")),
            "a disabled global slider is inactive");
        SetConfig(config, "default.appearance.enabled", true);
        SetConfig(config, "default.quantity.enabled", false);
        Require(settings.Get(minerRule, miner, ore) == 0 && settings.Get(thiefRule, thief, engineeringCard) == 100,
            "the stock quantity switch still controls individual quantities independently");
        SetConfig(config, "default.quantity.enabled", true);
        SetConfig(config, "thief.enabled", false);
        Require(settings.Get(thiefRule, thief, engineeringCard) == 0 && !settings.ChoiceEnabled(thief, crate),
            "the global modes honor disabled customers");
        SetConfig(config, StockSettings.InvertCustomerKey, true);
        Require(settings.Get(thiefRule, thief, engineeringCard) == 100 && settings.ChoiceEnabled(thief, crate),
            "customer switch inversion also applies to both modes");
        SetConfig(config, "default.enabled", false);
        Require(settings.Get(thiefRule, thief, engineeringCard) == 0 && !settings.ChoiceEnabled(thief, crate)
            && !settings.ChoiceEnabled(smuggler, shipment), "the overall mod toggle still disables all global modes");
        Require(settings.GlobalChanges().Contains((StockChangeStatus.Inactive, globalCategories + "　已勾选")),
            "a saved global mode is inactive while the mod is disabled");
        TestSpecialGoodsMigration(rules);
        Console.WriteLine("PASS global goods and category modes, independent gates, saved customer settings, and live previews.");

        int[] StockPositions() => rules.SelectMany(rule => rule.Profiles.SelectMany(profile =>
            Enumerable.Range(0, rule.Draws.Length).Select(site => settings.Get(rule, profile, site)))).ToArray();
        bool[] CategoryStates() => settings.Customers.SelectMany(profile => settings.ChoicesFor(profile)
            .Select(choice => settings.ChoiceEnabled(profile, choice))).ToArray();
    }

    private static void TestSpecialGoodsMigration(StockMethod[] rules)
    {
        const string smugglerId = "hypertech_smuggler";
        const string eleanorId = "eleanor5";
        ModConfig config = CreateConfig();
        var saved = (Dictionary<string, string>)AccessTools.Field(typeof(ModConfig), "rawValues").GetValue(config);
        saved[StockSettings.AllStockCategoriesKey] = "true";
        saved[smugglerId + ".enabled"] = "true";
        saved[eleanorId + ".enabled"] = "true";
        saved[smugglerId + StockSettings.SelectionEnabledSuffix] = "false";
        saved[eleanorId + StockSettings.SelectionEnabledSuffix] = "true";
        saved[StockSettings.CategoryPositionsKey] = smugglerId + ":allocation:shipment=1,"
            + smugglerId + ":allocation:shipment/hypertech_electronic=6,"
            + eleanorId + ":selection:junkTable/glass_shard=100";

        var settings = new StockSettings(config, rules);
        var smuggler = settings.Customers.Single(profile => profile.Id == smugglerId);
        var eleanor = settings.Customers.Single(profile => profile.Id == eleanorId);
        var shipment = settings.ChoicesFor(smuggler).Single();
        var junk = settings.ChoicesFor(eleanor).Single(choice => choice.Id == "junkTable");
        Require(config.Get(StockSettings.AllSpecialGoodsKey) is ConfigEntry<bool> { Value: true, Default: false }
            && config.Get(smugglerId + StockSettings.SpecialGoodsEnabledSuffix) is ConfigEntry<bool> { Value: false }
            && config.Get(eleanorId + StockSettings.SpecialGoodsEnabledSuffix) is ConfigEntry<bool> { Value: true }
            && settings.ActiveAllocation(smuggler, shipment, shipment.Categories)?.FixedCount == 5,
            "migration preserves the old forced global state, stored local flags, and exact quantities");

        SetConfig(config, StockSettings.AllSpecialGoodsKey, false);
        Require(!settings.ChoiceEnabled(smuggler, shipment) && settings.GetChoicePositions(eleanor, junk)?.Contains(100) == true,
            "closing the migrated global option restores each customer's saved setting and weights");

        ModConfig reopened = CreateConfig();
        var reopenedValues = (Dictionary<string, string>)AccessTools.Field(typeof(ModConfig), "rawValues").GetValue(reopened);
        foreach (var entry in saved)
            reopenedValues[entry.Key] = entry.Value;
        foreach (ConfigEntry entry in config.Entries)
            reopenedValues[entry.Key] = entry.Serialize();
        var reopenedSettings = new StockSettings(reopened, rules);
        Require(!reopenedSettings.ChoiceEnabled(smuggler, shipment)
            && reopened.Get(StockSettings.AllStockCategoriesKey) is ConfigEntry<bool> { Value: true },
            "reopening the saved configuration respects independent global settings");
        Console.WriteLine("PASS special goods migration and independent stock category settings.");
    }

    private static void TestCustomerChanges(IEnumerable<StockMethod> rules)
    {
        var config = CreateConfig();
        var settings = new StockSettings(config, rules);
        Require(!settings.GlobalChanges().Any() && !settings.CustomerChanges().Any(), "the default overview is empty");
        SetConfig(config, "default.appearance", 50);
        SetConfig(config, StockSettings.InvertCustomerKey, true);
        SetConfig(config, StockSettings.InvertCategoryKey, true);
        Require(settings.GlobalChanges().Contains((StockChangeStatus.Effective, "出货概率　+50"))
            && !settings.CustomerChanges().Any(), "global-only changes leave the customer chapter empty");
        EnableAllCustomers(config);
        SetConfig(config, "thief.enabled", false);
        SetConfig(config, "thief.override", true);
        SetConfig(config, "thief.appearance", 100);
        SetConfig(config, "thief" + StockSettings.IndividualCategoriesSuffix, true);
        SetConfig(config, StockSettings.CategoryKey, AllGoodsExcept("thief:appearance:turbo_booster_adv"));
        SetConfig(config, StockSettings.CategoryPositionsKey,
            "thief:appearance:turbo_booster_adv=25,thief:selection:crate/loot_crate_engineering=50,obsolete:quantity:removed=100");
        SetConfig(config, "miner.override", true);
        SetConfig(config, "miner.quantity", -50);
        var changes = settings.CustomerChanges().ToDictionary(change => (change.Profile.Id, change.Status), change => change.Lines);
        Require(changes[("thief", StockChangeStatus.Effective)].Contains("<color=#D1C8A5>出货概率</color>")
            && changes[("thief", StockChangeStatus.Effective)].Contains("高级涡轮增压器　独立档位 +25")
            && changes[("thief", StockChangeStatus.Superseded)].Contains("高级涡轮增压器　未勾选")
            && changes[("thief", StockChangeStatus.Superseded)].Contains("「小偷（常规）」的出货概率档位　+100")
            && changes[("thief", StockChangeStatus.Superseded)].Contains("使用独立设置　已勾选")
            && changes[("thief", StockChangeStatus.Inactive)].Contains("<color=#D1C8A5>货物分类 · 物资箱</color>")
            && changes[("thief", StockChangeStatus.Inactive)].Contains("启用货物分类调整　未勾选")
            && changes[("thief", StockChangeStatus.Inactive)].Contains("工程铸造厂物资箱　分类档位 +50")
            && changes[("miner", StockChangeStatus.Inactive)].Contains("「矿工」的货物数量档位　-50"),
            "the overview distinguishes settings superseded by individual sliders from disabled settings");
        SetConfig(config, "default.appearance.enabled", false);
        var disabledAxis = settings.CustomerChanges().Single(change => change.Profile.Id == "thief" && change.Status == StockChangeStatus.Inactive);
        Require(disabledAxis.Lines.Contains("「小偷（常规）」的出货概率档位　+100")
            && disabledAxis.Lines.Contains("高级涡轮增压器　未勾选")
            && settings.CustomerChanges().All(change => change.Profile.Id != "thief" || change.Status != StockChangeStatus.Superseded),
            "disabled stock chance takes precedence over the choice of configuration mode");
        SetConfig(config, "default.appearance.enabled", true);
        SetConfig(config, "thief" + StockSettings.IndividualCategoriesSuffix, false);
        SetConfig(config, "thief" + StockSettings.SelectionEnabledSuffix, true);
        var refreshed = settings.CustomerChanges().Where(change => change.Profile.Id == "thief").ToDictionary(change => change.Status, change => change.Lines);
        Require(refreshed[StockChangeStatus.Superseded].Contains("高级涡轮增压器　独立档位 +25")
            && refreshed[StockChangeStatus.Effective].Contains("高级涡轮增压器　未勾选")
            && refreshed[StockChangeStatus.Effective].Contains("启用货物分类调整　已勾选")
            && refreshed[StockChangeStatus.Effective].Contains("治安部物证柜　受同组调整影响"),
            "superseded sliders and effective inverted checkboxes remain distinct, including indirect group changes");
        SetConfig(config, "thief.override", false);
        Require(settings.CustomerChanges().Single(change => change.Profile.Id == "thief"
            && change.Status == StockChangeStatus.Superseded).Lines.Contains("「小偷（常规）」的出货概率档位　+100"),
            "the inherited default supersedes an unused customer dial");
        SetConfig(config, "thief.override", true);

        var draft = new StockSettings.Draft(key => key == "miner.enabled" ? false : (bool?)null,
            key => key == "miner.quantity" ? -70 : (int?)null,
            new StockCategoryMask(StockCategoryMask.AllChecked), new StockCategoryPositions("thief:selection:crate/loot_crate_engineering=100"));
        var pending = settings.CustomerChanges(draft).Where(change => change.Status == StockChangeStatus.Pending)
            .ToDictionary(change => change.Profile.Id, change => change.Lines);
        Require(pending["thief"].Contains("工程铸造厂物资箱　分类档位 +50 → +100")
            && pending["thief"].Contains("高级涡轮增压器　独立档位 +25 → 0")
            && pending["thief"].Contains("高级涡轮增压器　未勾选 → 已勾选")
            && pending["miner"].Contains("「矿工」的货物数量档位　-50 → -70")
            && pending["miner"].Any(line => line.Contains("已勾选 → 未勾选"))
            && settings.CustomerChanges().All(change => change.Status != StockChangeStatus.Pending),
            "pending slider, checkbox, and toggle changes compare with saved values and never mutate them");

        StockProfile thief = settings.Customers.Single(profile => profile.Id == "thief");
        StockChoice crates = settings.ChoicesFor(thief).Single(choice => choice.Id == "crate");
        string box = StockChoiceDetails.DescribeProbabilities(thief, crates, new[] { 0, 0, 100, 0, 0 }, true,
            settings.ProductAppearanceScenarios(thief, "crate_random"));
        Require(box.Contains("治安部物证柜　20.79% → <b>0%</b>（已应用）")
            && box.Contains("未携带上述物资箱　70% → <b>70%</b>\n")
            && !StockChoiceDetails.DescribeProbabilities(thief, crates, Enumerable.Repeat(100, 5).ToArray(), true)
                .Contains("（已应用）")
            && !StockChoiceDetails.DescribeProbabilities(thief, crates, new[] { 0, 0, 100, 0, 0 }, false)
                .Contains("（已应用）"),
            "only changed outcomes are tagged, including indirect effects and equal-weight fallback");
        Require(settings.ProductDetails(thief, StockAxis.Appearance, "turbo_booster_adv").Contains("（已应用）")
            && settings.ProductDetails(thief, StockAxis.Appearance, "crypto_module_med").Contains("（未应用）"),
            "adjustment labels distinguish the selected goods from neighboring unchanged options");

        var migratedConfig = CreateConfig();
        var saved = (Dictionary<string, string>)AccessTools.Field(typeof(ModConfig), "rawValues").GetValue(migratedConfig);
        saved.Add(StockSettings.CategoryPositionsKey, "thief:selection:crate/loot_crate_engineering=100");
        saved.Add("thief.enabled", "true");
        saved.Add("addictGeneralBarter.enabled", "true");
        var migrated = new StockSettings(migratedConfig, rules);
        var switchEntry = (ConfigEntry<bool>)migratedConfig.Get("thief" + StockSettings.SelectionEnabledSuffix);
        Require(!switchEntry.Default && switchEntry.Value && migrated.CustomerChanges().Single().Lines
            .Contains("启用货物分类调整　已勾选"), "an active choice shows its enabling switch even at the migrated default");
        SetConfig(migratedConfig, "default.appearance", 50);
        SetConfig(migratedConfig, "thief.override", true);
        SetConfig(migratedConfig, "miner" + StockSettings.IndividualCategoriesSuffix, true);
        SetConfig(migratedConfig, StockSettings.CategoryKey, AllGoodsExcept("addictGeneralBarter:appearance:system_capped_neural_core"));
        var effects = migrated.CustomerChanges().Where(change => change.Status == StockChangeStatus.Effective)
            .ToDictionary(change => change.Profile.Id, change => change.Lines);
        Require(effects["thief"].Contains("使用独立设置　已勾选") && effects["thief"].Contains("「小偷（常规）」的出货概率档位　0")
            && effects["addictGeneralBarter"].Contains("神经核心模组（受限）　未勾选")
            && !migrated.CustomerChanges().Any(change => change.Profile.Id == "miner"),
            "show switches and overrides that change inherited results and hide switches with no effect");
        Console.WriteLine("PASS effective, pending, superseded, and inactive customer changes.");
    }

    internal static ModConfig CreateConfig()
        => (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { "better_customer_stock.verification" }, null);

    internal static void EnableAllCustomers(ModConfig config)
    {
        foreach (ConfigEntry entry in config.Entries.Where(entry => entry.Section == StockSettings.CustomerSection
            && entry.Key.EndsWith(".enabled", StringComparison.Ordinal)))
            SetConfig(config, entry.Key, true);
    }

    private static string AllGoodsExcept(params string[] uncheckedGoods)
        => string.Join(",", uncheckedGoods.Prepend(StockCategoryMask.AllChecked));

    internal static ConfigEntry<T> SetConfig<T>(ModConfig config, string key, T value)
    {
        var entry = (ConfigEntry<T>)config.Get(key);
        typeof(ConfigEntry<T>).GetMethod("ApplyValue", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(entry, new object?[] { value });
        return entry;
    }

    private static void VerifyMinerOutcomes(string detail, int slider)
    {
        int[] units = { 1, 1, 2, 1, 1 };
        string[] stages = detail.Split(new[] { "\n\n" }, StringSplitOptions.None).Skip(1).ToArray();
        Require(stages.Length == units.Length, "miner detail has five day ranges");
        int displayed = 0;
        for (int stage = 0; stage < stages.Length; stage++)
        {
            var original = new double[units.Take(stage + 1).Sum() + 1];
            var modified = new double[original.Length];
            int combinations = 1 << (stage + 1);
            double adjustedChance = (slider + 100) / 200d;
            for (int flags = 0; flags < combinations; flags++)
            {
                int extra = 0;
                double weight = 1;
                for (int draw = 0; draw <= stage; draw++)
                {
                    bool hit = (flags & (1 << draw)) != 0;
                    if (hit)
                        extra += units[draw];
                    weight *= hit ? adjustedChance : 1 - adjustedChance;
                }
                original[extra] += 1d / combinations;
                modified[extra] += weight;
            }
            Near(1, original.Sum(), "all native miner outcomes sum to one");
            Near(1, modified.Sum(), "all adjusted miner outcomes sum to one");

            var matches = System.Text.RegularExpressions.Regex.Matches(stages[stage],
                @"(\d+) 份 (\d+(?:\.\d+)?)% → (\d+(?:\.\d+)?)%");
            Require(matches.Count == original.Length, "every possible ore count is listed in this stage");
            double originalSum = 0;
            double modifiedSum = 0;
            for (int outcome = 0; outcome < original.Length; outcome++)
            {
                var row = matches[outcome].Groups;
                double nativePercent = double.Parse(row[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                double modPercent = double.Parse(row[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                Require(int.Parse(row[1].Value) == 3 + outcome
                    && Math.Abs(nativePercent - 100 * original[outcome]) <= 0.005001
                    && Math.Abs(modPercent - 100 * modified[outcome]) <= 0.005001,
                    $"miner outcome {stage}/{3 + outcome} at {slider}");
                originalSum += nativePercent;
                modifiedSum += modPercent;
            }
            Require(Math.Abs(originalSum - 100) <= original.Length * 0.005001
                && Math.Abs(modifiedSum - 100) <= modified.Length * 0.005001,
                "rendered probability totals agree within display rounding");
            displayed += matches.Count;
        }
        Require(displayed == 23, $"all 23 ore outcomes are rendered at {slider}");
    }

    private static void VerifyCrateOutcomes(string detail)
    {
        string[] tiers = detail.Split(new[] { "\n\n" }, StringSplitOptions.None).Skip(1).ToArray();
        Require(tiers.Length == 2, "crate detail has two reputation tiers");
        foreach (string tier in tiers)
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(tier, @"(\d+(?:\.\d+)?)% → <b>(\d+(?:\.\d+)?)%</b>");
            Require(matches.Count == 6, "each tier lists five crate types and the no-crate result");
            for (int column = 1; column <= 2; column++)
            {
                double total = matches.Cast<System.Text.RegularExpressions.Match>().Sum(row =>
                    double.Parse(row.Groups[column].Value, System.Globalization.CultureInfo.InvariantCulture));
                Require(Math.Abs(total - 100) <= matches.Count * 0.005001, "crate outcomes including absence sum to 100%");
            }
        }
    }

    private static void VerifyProbabilityStages(string detail, string label)
    {
        var stages = detail.Split(new[] { "\n\n" }, StringSplitOptions.None).Skip(1);
        foreach (string stage in stages)
        {
            var pairs = System.Text.RegularExpressions.Regex.Matches(stage,
                @"(\d+(?:\.\d+)?)% → (?:<b>)?(?:(?:低于|高于) )?(\d+(?:\.\d+)?)%");
            Require(pairs.Count > 0, $"numeric presentation in every scenario for {label}");
            for (int column = 1; column <= 2; column++)
            {
                double total = pairs.Cast<System.Text.RegularExpressions.Match>().Sum(row =>
                    double.Parse(row.Groups[column].Value, System.Globalization.CultureInfo.InvariantCulture));
                Require(Math.Abs(total - 100) <= pairs.Count * 0.010001,
                    $"displayed probabilities sum to 100% within rounding for {label}");
            }
        }
    }

    private static void Near(double expected, double actual, string label)
        => Require(Math.Abs(expected - actual) < 1e-12, label);

    private static void Require(bool condition, string label)
    {
        if (!condition)
            throw new InvalidOperationException(label);
        checks++;
    }
}
