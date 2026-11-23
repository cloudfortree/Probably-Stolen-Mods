using System;
using System.Collections.Generic;
using System.Linq;
using static BetterCustomerStock.StockLanguage;

namespace BetterCustomerStock;

internal static class StockProductDetails
{
    internal static string Describe(IReadOnlyList<StockMethod> methods, StockProfile profile, StockAxis axis, string item,
        StockProductPreview.PositionReader position, bool linear)
    {
        var applicable = methods.Where(method => Sites(method, profile, axis, item) != 0).ToArray();
        string methodName = applicable[0].MethodName;
        bool applied = StockProductPreview.HasEffect(applicable, profile, axis, item, position, linear);
        if (axis == StockAxis.Appearance)
        {
            var (original, modified) = StockProductPreview.Summarize(applicable, profile, axis, item, position, linear);
            string initial = Lines(original, AppearanceNote(methodName, item),
                StockProductPreview.ReputationNote(applicable[0], profile, axis, item));
            string scope = methodName == nameof(StoreClientList.CreateThief) && item == "crate_random"
                ? Text("此项调整未解锁物资箱保底时的携带概率。", "This adjusts the chance of carrying a crate before the guarantee unlocks.")
                : "";
            string bonus = applicable.Any(method => Sites(method, profile, StockAxis.Quantity, item) != 0)
                ? Line("库存增量抽选见「{0}」。", "See {0} for additional stock rolls.", StockLanguage.Quantity) : "";
            return Sections(Lines(initial, bonus), Lines(modified, scope), applied);
        }

        if (methodName == nameof(StoreClientList.CreateMiner))
        {
            var method = applicable[0];
            var (original, modified) = StockProductPreview.ExpectedCounts(method, profile, item, position, linear);
            var units = method.BaseDraws[0]!.Value;
            var unlocks = StockProductPreview.MinerUnlocks;
            string batches = string.Join("\n", unlocks.Select((unlock, index) =>
                Line("{0}，{1}", "Day {2} unlocks this stock roll. {1}", FromDay(unlock.Day),
                    StockIncreaseRolls(method.BaseDraws[index + 1]!.Value.Chance!.Value, 1, unlock.Units), unlock.Day)));
            string originalDetails = Lines(
                Line("基础库存数量为 <b>{0}</b>，各数量的概率均为 {1}。",
                    "Base stock is <b>{0}</b>, with a {1} chance of each quantity.",
                    StockRange(units.Minimum, units.Maximum), StockProductPreview.Percent(1d / (units.Maximum - units.Minimum + 1))),
                batches,
                ExpectedByDay(original));
            return Sections(originalDetails, ExpectedByDay(modified), applied);
        }

        int[] extraDraws = applicable.Select(method => Sites(method, profile, StockAxis.Quantity, item)).ToArray();
        string quantity = Quantity(methodName, item, applicable[0], profile, extraDraws.Min(), extraDraws.Max());
        var (baseQuantity, adjustedQuantity) = StockProductPreview.Summarize(applicable, profile, axis, item, position, linear);
        return Sections(Lines(quantity, baseQuantity, StockProductPreview.ReputationNote(applicable[0], profile, axis, item)),
            adjustedQuantity, applied);

        static string ExpectedByDay(string counts)
            => Line("各天数档的库存数量期望值依次为 <b>{0}</b>。",
                "The expected stock quantities by day range are <b>{0}</b>, respectively.", StockAmount("", counts));
    }

    private static string AppearanceNote(string method, string item) => (method, item) switch
    {
        (nameof(StoreClientList.CreateThief), "scratcher_stack") => Text("每叠有 5 张。", "Each stack contains 5 tickets."),
        (nameof(BarterOfferList.CreateAddictGeneralBarter) or nameof(BarterOfferList.CreateAddictBarter), _)
            => Line("这套报价包含 {0}。", "This offer contains {0}.", StockBarterDetails.OptionalContents(method, item)),
        (nameof(StoreClientList.CreateSecSurplusMerchant), "heavy_pistol_ammo") => Line("同时持有 {0}和 {1}时，另一批计入「货物数量」。",
            "When both the {0} and {1} are owned, the additional batch is covered under Stock Quantity.", WeaponPermit(1), WeaponPermit(2)),
        (nameof(StoreClientList.CreateSecSurplusMerchant), "heavy_pistol_ammo_nl") => Text("未持有武器许可证时，原版固定带 3 份。",
            "Vanilla guarantees 3 units when no weapon permit is owned."),
        (nameof(StoreClientList.PlaceInventorInventory), "desequencer") => Line("尚未持有时，抽中这批带 1 台{0}和 1 张{1}。持有后另抽 0–1 台。",
            "Before the {0} is owned, this roll brings one {0} and one {1}. Once owned, a separate roll can bring 0–1 desequencers.",
            StockCatalog.Name("desequencer"), StockCatalog.Name("blank_keycard")),
        (nameof(StoreClientList.PlaceInventorInventory), "blank_keycard") => Line("首次配套出货时带 1 张。已持有{0}时原版固定带 2 张。",
            "The initial bundle includes one card. Vanilla guarantees two cards once the {0} is owned.", StockCatalog.Name("desequencer")),
        (nameof(StoreClientList.PlaceInventorInventory), "mirage_projector") => Text("第 10 天起开放抽选，原版概率随天数提高。",
            "This roll unlocks on day 10. Its vanilla chance rises with the day count."),
        (nameof(StoreClientList.PlaceInventorInventory), "storage_bay" or "machine_bay" or "furnace" or "fridge" or "water_purifier" or "moisture_farm")
            => Text("满足供货条件且尚未持有时固定带 1 份。", "One unit is guaranteed when the stock conditions are met and the item is not yet owned."),
        (nameof(StoreClientList.PlaceSupplierInventory), _) => Text("原版抽选概率随天数增加。", "The vanilla chance rises with the day count."),
        (nameof(StoreClientListWanted.CreateWanted1Return), _) => Text("原版抽选概率随天数增加，第 65 天起固定带 1 份。",
            "The vanilla chance rises with the day count. One unit is guaranteed from day 65."),
        _ => ""
    };

    private static string Quantity(string methodName, string item, StockMethod method, StockProfile profile, int minExtra, int extra)
    {
        switch (methodName, item)
        {
            case (nameof(StoreClientList.CreateFarmerClientStandard), "wine_berry"):
                return Text("有酿酒供货时为 5–6 份。", "Brings 5–6 units when winemaking stock is unlocked.");
            case (nameof(StoreClientList.CreateFarmerClientStandard), "nutrifruit"):
                return Text("有酿酒供货时为 4–5 份，其余来访为 5–9 份。",
                    "Brings 4–5 units when winemaking stock is unlocked, or 5–9 units on other visits.");
            case (nameof(StoreClientList.CreateScavCrate), "packedFoodTable"):
                return Line("{0}，同一种食品的库存数量为 {1}。{2}为 {3}。符合原版低价条件时，{4}",
                    "Before day {5}, stock is {1} of one food type. From day {5}, it is {3}. When vanilla's low-price conditions are met, {4}",
                    BeforeDay(20), StockRange(5, 6), FromDay(20), StockRange(6, 7), StockIncrease(StockRange(1, 2)), 20);
            case (nameof(StoreClientListAppraisal.CreateAppraisalRandom), "appraisal_random"):
                return Line("抽中类别后，库存数量为 {0}。", "The chosen category provides {0}.", NativeCount());
            case (nameof(StoreClientListEvent.CreateIceMiner), "bottled_water_high"):
                return Text("普通瓶装水原版随机为 0–1 份，第 15 天及第 45 天各固定增加 1 份。大瓶另固定带 1 份。",
                    "Vanilla rolls for 0–1 standard bottles, with one guaranteed extra from day 15 and another from day 45. One large bottle is guaranteed separately.");
            case (nameof(StoreClientListWater.CreateWaterCompany), "bottled_water_random"):
                return Text("分别抽两批，每批 1–2 份，共 2–4 份。", "Two batches roll separately. Each gives 1–2 units, for 2–4 units in total.");
            case (nameof(StoreClientListWater.CreateWaterSellScav), "bottled_water_random"):
                return RolledStock(NativeCount());
            case (nameof(StoreClientListWater.SetHonestWaterSellerDialog), "bottled_water_base"):
                return Text("该水质货单中带 1–2 份普通瓶装水，另固定带 1 份大瓶。",
                    "This water-quality stock set brings 1–2 standard bottles and one guaranteed large bottle.");
            case (nameof(StoreClientListWater.SetHonestWaterSellerDialog), "water_ration"):
                return Text("抽中配给水货单时带 3–5 份。", "Brings 3–5 units when the water ration stock set is rolled.");
            case (nameof(StoreClientListWater.SetHonestWaterSellerDialog), "bottled_water_ghost"):
                return Text("该水质货单中带 2–4 份普通瓶装水，另固定带 1 份大瓶。",
                    "This water-quality stock set brings 2–4 standard bottles and one guaranteed large bottle.");
            case (nameof(StoreClientListWanted.CreateWanted1Return), "heavy_pistol_ammo"):
                return RolledStock(NativeCount());
            case ("RollButcherCount", "raw_meat" or "small_raw_meat" or "fat_meat"):
                string range = item == "raw_meat" ? "0–3" : item == "small_raw_meat" ? "1–3" : "0–2";
                return RolledStock(StockAmount("", range)) + " " + Line("{0}固定为范围上限。",
                    "Vanilla guarantees the upper limit from day {1}.", FromDay(40), 40);
            case (nameof(StoreClientList.CreateWaterSeller), "large_bottled_water"):
                return Line("有{0}供货时，{1}\n第 {2} 天分别增加 {3}。{4}",
                    "When {0} is offered, base stock is guaranteed at {5}.\nDays {2} each add {3}. {4}",
                    StockCatalog.Name("bottled_water_base"), GuaranteedStock(1), JoinList(new[] { 20, 50 }), StockAmount(1),
                    RolledIncrease(StockRange(0, extra)), StockAmount(1));
            case (nameof(StoreClientList.CreateWaterSeller), "water_ration_small"):
                return Line("抽中{0}货单后，首次抽选可生成 {1}。{2}",
                    "For the {0} stock set, the initial roll can generate {1}. {2}",
                    StockCatalog.Name("water_ration"), StockRange(0, 1), RolledIncrease(StockRange(0, extra)));
            case (nameof(StoreClientList.CreateStampSeller), "random_stamp"):
                return Lines(GuaranteedStock(1), RolledIncrease(StockRange(0, extra)),
                    Line("第 {0} 天分别开放一次库存增量抽选。", "One additional stock roll unlocks on each of days {0}.",
                        JoinList(new[] { 20, 50 })));
            case (nameof(StoreClientList.CreateShadyPharmacist), "pink_injector"):
                return Line("抽中{0}货单时，最初固定 2 份，第 20、40 天各固定加 1 份。另随机加 0–1 份。",
                    "The {0} stock set guarantees two units initially, with one extra from day 20 and another from day 40. A separate roll adds 0–1 units.",
                    StockCatalog.Name("pink_injector"));
            case (nameof(StoreClientList.CreateSecSurplusMerchant), "heavy_pistol_ammo"):
                return Line("{0}和 {1}只持有其一时为 0–2 份，其中最多 1 份受此项控制。两级都持有时为 0–4 份，其中最多 3 份受此项控制。",
                    "With either the {0} or the {1}, stock is 0–2 units and this setting affects up to one. With both permits, stock is 0–4 units and this setting affects up to three.",
                    WeaponPermit(1), WeaponPermit(2));
            case (nameof(StoreClientList.CreateSecSurplusMerchant), "heavy_pistol_ammo_nl"):
                return Line("持有 {0}时为 0–2 份，其中最多 1 份受此项控制。未持有武器许可证时固定带 3 份。",
                    "With a {0}, stock is 0–2 units and this setting affects up to one. With no weapon permit, three units are guaranteed.",
                    WeaponPermit(1));
            case (nameof(StoreClientList.CreateSecSurplusMerchant), "stun_gun" or "tazer"):
                return "";
            case (nameof(StoreClientList.CreateSecSurplusMerchant), "pink_injector"):
                return Text("未持有武器许可证时另固定带 1 份。", "An extra unit is guaranteed when no weapon permit is owned.");
            case (nameof(StoreClientListSurplus.CreateFoodSurplusClient), "packedFoodTable"):
                return Text("一般过剩时从货表抽 0–5 份，严重过剩时抽 0–10 份。固定供货另算。",
                    "Rolls for 0–5 units from the loot table during a regular surplus and 0–10 during a severe surplus. Guaranteed stock is counted separately.");
            case (nameof(StoreClientListCartel.CreateShadyFarmer), "dream_cap"):
                return Text("固定带 2 份小号和 1 份普通大小，另随机加 0–2 份普通大小。",
                    "Two small and one standard unit are guaranteed, with a roll for 0–2 extra standard units.");
            case (nameof(StoreClientListWanted.CreateWanted7), _):
                return Lines(GuaranteedStock(1), RolledIncrease(StockRange(0, extra)),
                    Line("{0}固定库存数量为 {1}。", "From day {2}, stock is guaranteed at {1}.", FromDay(38), StockAmount(1 + extra), 38));
            case (nameof(StoreClientList.PlaceInventorInventory), _):
                return InventorQuantity(method, profile, item, extra);
        }

        int fixedCount = FixedQuantity(methodName, item);
        string source = methodName == nameof(StoreClientList.CreateRareLowerLevelChemist)
            || methodName == nameof(StoreClientListBarter.CreateScavBarter)
                ? Text("适用于抽中相应货单时。", "Applies when the corresponding stock set is rolled.") : "";
        string initial = fixedCount > 0 ? GuaranteedStock(fixedCount) : "";
        bool hasAppearance = Sites(method, profile, StockAxis.Appearance, item) != 0;
        if (hasAppearance)
            initial = Lines(initial, Line("首次出货抽选可生成 {0}。", "The initial stock roll can generate {0}.",
                StockRange(0, AppearanceBatchSize(methodName, item))));
        string bonus = fixedCount > 0 || hasAppearance ? RolledIncrease(StockRange(0, extra)) : RolledStock(StockRange(0, extra));
        if (methodName == nameof(StoreClientListSupplier.AddScavMaterialStock) && item == "scrap_metal")
        {
            var chances = Enumerable.Range(0, method.Draws.Length).Where(site =>
                method.AxisFor(profile, site) == StockAxis.Quantity && method.Categories[site].Contains(item))
                .Select(site => method.BaseDraws[site]?.Chance).Distinct().ToArray();
            if (chances.Length == 1 && chances[0] is double chance)
                bonus = StockIncreaseRolls(chance, extra, 1);
        }
        if (minExtra != extra)
            bonus = Line("一般过剩时，抽选可使库存数量增加 {0}。严重过剩时可增加 {1}。",
                "Rolls can increase stock by {0} during a regular surplus and by {1} during a severe surplus.",
                StockRange(0, minExtra), StockRange(0, extra));
        else if (methodName == nameof(StoreClientListSurplus.CreateMedicalSurplusClient) && item != "bandage_item"
            || methodName == nameof(StoreClientListSurplus.CreateMaterialSurplusClient) && item == "common_electronic")
            bonus = Lines(bonus, Text("适用于严重过剩时。", "Applies during a severe surplus."));
        return Lines(source, initial, bonus);

        string NativeCount()
        {
            int site = Enumerable.Range(0, method.Draws.Length).First(index =>
                method.AxisFor(profile, index) == StockAxis.Quantity && method.Categories[index].Contains(item));
            var draw = method.BaseDraws[site]!.Value;
            return StockRange(draw.Minimum, draw.Maximum);
        }
    }

    internal static int AppearanceBatchSize(string method, string item) => (method, item) switch
    {
        (nameof(StoreClientList.AddScavGeneralStock), "kotton_seed" or "bloomberry_seed") => 2,
        (nameof(BarterOfferList.CreateAddictGeneralBarter), "birth_control_pill" or "packedFoodTable") => 3,
        (nameof(BarterOfferList.CreateAddictGeneralBarter), "kotton_fabric" or "common_medical" or "common_chemical") => 2,
        _ => 1
    };

    private static string InventorQuantity(StockMethod method, StockProfile profile, string item, int extra)
    {
        int investment = Array.IndexOf(method.Profiles, profile) % 3;
        return item switch
        {
            "energy_credit" => Line("固定带 2 份{0}，随机加 0–1 份充满电的{0}。",
                "Two units of {0} are guaranteed, with a roll for 0–1 fully charged extras.", StockCatalog.Name("energy_credit")),
            "module_bay_expansion_kit" => Lines(
                Line("首次出货抽选可生成 {0}。", "The initial stock roll can generate {0}.", StockRange(0, 1)),
                RolledIncrease(StockRange(0, extra)),
                Line("第 {0} 天各开放一次库存增量抽选。", "One additional stock roll unlocks on each of days {0}.",
                    JoinList(new[] { 20, 40 })),
                investment == 0 ? "" : Line("本投资阶段再开放 {0} 次抽选。", "This investment stage unlocks {0} more rolls.", investment)),
            "water_tank" or "chem_module" => Line("解锁相应供货后，首次可带 0–1 份，本投资阶段另抽 0–{0} 份。",
                "Once this stock is unlocked, the initial roll can bring 0–1 units. This investment stage adds rolls for 0–{0} extras.", extra),
            "plant_dna_disk" => Line("解锁相应供货后，本投资阶段固定带 {0} 份，另抽 0–{1} 份。",
                "Once this stock is unlocked, this investment stage guarantees {0} and rolls for 0–{1} extras.",
                NativeQuantity(1 + investment), extra),
            _ => Line("本投资阶段固定带 {0} 份，另抽 0–{1} 份。",
                "This investment stage guarantees {0} and rolls for 0–{1} extras.", NativeQuantity(investment), extra)
        };

        static string NativeQuantity(int count) => Line("{0}", count == 1 ? "{0} unit" : "{0} units", count);
    }

    // Fixed offers that share a category with independent bonus draws.
    internal static int FixedQuantity(string method, string item) => (method, item) switch
    {
        (nameof(StoreClientList.CreateJunker), "scrap_metal") => 3,
        (nameof(StoreClientList.CreateJunker), "wire" or "printer_plastic") => 2,
        (nameof(StoreClientList.CreateJunker), "common_electronic") => 1,
        (nameof(StoreClientList.CreatePettyThief), "packedFoodTable") => 1,
        (nameof(StoreClientList.CreateFoodThief) or nameof(StoreClientListSupplier.AddScavFoodStock), "nutrifruit") => 1,
        (nameof(StoreClientListSupplier.AddScavHouseholdStock) or nameof(StoreClientListEvent.CreateScavengerHouseholdClient)
            or nameof(StoreClientListRev.CreateRevRaider), "toilet_paper") => 1,
        (nameof(StoreClientListSupplier.AddScavMaterialStock), "scrap_metal") => 1,
        (nameof(StoreClientListSupplier.AddScavMedStock), "hemostatic_bandage_item") => 1,
        (nameof(StoreClientListSupplier.AddThiefFoodStock), "small_raw_meat") => 1,
        (nameof(StoreClientListSupplier.AddThiefMaterialStock), "common_electronic") => 1,
        (nameof(StoreClientListSupplier.AddThiefMedStock), "pink_injector") => 1,
        (nameof(StoreClientList.CreateRareLowerLevelChemist), "dream_dust" or "fanta_pill") => 4,
        (nameof(StoreClientList.CreateRareLowerLevelChemist), "pink_injector") => 2,
        (nameof(StoreClientList.CreateRareLowerLevelChemist), "black_injector") => 1,
        (nameof(StoreClientList.CreateGunsmith), "handmade_pistol") => 1,
        (nameof(StoreClientListBarter.CreateScavBarter), "wire") => 1,
        (nameof(StoreClientListBarter.CreateScavBarter), "scrap_metal") => 2,
        (nameof(StoreClientListEvent.CreateFarmerSeedClient), "nutrifruit") => 5,
        (nameof(StoreClientListSpec.CreateRetiredWaterMerchant), "water_test_container" or "water_tablet_container") => 1,
        (nameof(StoreClientListSpec.CreateRetiredWaterMerchant), "water_filter") => 2,
        (nameof(StoreClientListSpec.CreateRetiredJunker), "flux_agent") => 1,
        (nameof(StoreClientListSpec.CreateRetiredChemist), "chem_module") => 1,
        (nameof(StoreClientListSurplus.CreateMedicalSurplusClient), "bandage_item") => 1,
        (nameof(StoreClientListSurplus.CreateSubstanceSurplusClient), "dream_dust") => 1,
        (nameof(StoreClientListSurplus.CreateMaterialSurplusClient), "scrap_metal") => 1,
        (nameof(StoreClientListWater.CreateDishonestWaterSellScav), "bottled_water_base") => 1,
        _ => 0
    };

    private static int Sites(StockMethod method, StockProfile profile, StockAxis axis, string category)
        => Enumerable.Range(0, method.Draws.Length)
            .Count(site => method.AxisFor(profile, site) == axis && method.Categories[site].Contains(category));

    private static string Sections(string original, string modified, bool applied)
        => Line("<b>原版</b>\n{0}\n\n<b>修正</b>{1}\n{2}", "<b>Vanilla</b>\n{0}\n\n<b>Mod</b>{1}\n{2}",
            original, applied ? AppliedSuffix : UnappliedSuffix,
            applied ? modified : Text("沿用原版。", "Uses vanilla values."));

    private static string Lines(params string[] lines) => string.Join("\n", lines.Where(line => line.Length != 0));
}
