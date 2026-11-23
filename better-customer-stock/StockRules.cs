using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using static BetterCustomerStock.StockLanguage;

namespace BetterCustomerStock;

internal static class StockRules
{
    internal const int RequisitionAmmoSite = 25;

    internal static readonly StockProfile InventorSpecial = new StockProfile("inventorSpecial", "杰克逊博士（周一特供）")
    {
        FixedStockNotice = () => Text("周一特供的货物种类和数量按原版规则生成。",
            "Monday's special stock uses the vanilla goods and quantities.")
    };

    internal static readonly (string Outside, string Shared)[] LegacyInventorOutsideProfiles =
    {
        ("inventorOutside", "inventor"),
        ("inventorOutsideInvestment1", "inventorVisitInvestment1"),
        ("inventorOutsideInvestment2", "inventorVisitInvestment2")
    };

    internal static readonly StockChoice InventorBackpack = new StockChoice("backpack", "背包", StockChoiceKind.Backpack,
        "backpack_large backpack_medium backpack_small", new[] { 1, 2, 3 });

    internal static readonly StockMethod[] Methods = Create();

    private static StockMethod[] Create()
    {
        var rules = new List<StockMethod>();

        // These bindings are shared by the verified release and Beta unless noted.
        // Uppercase records a stock decision to adjust. Lowercase records a native
        // random draw to retain. Counting both also guards against shifted call sites.
        // P/F = integer/float appearance, Q/C = integer/float bonus count,
        // N/U = inclusive/exclusive count range, A = aquaculture equipment, E = empty-stock check.
        var scavenger = new StockProfile("scavGeneral", "拾荒客（常规）");
        var eleanor = new StockProfile("eleanorScav", "埃莉诺（拾荒客）", "eleanor6")
        {
            DefaultProfile = scavenger
        };
        var packedFood = Table("packedFoodTable", "加工食品货表",
            "beis_icecream cat_bar cup_noodle energy_drink galaxy_blend li_eat_snackbar nudka processed_juice processed_meat processed_milk red_beer soda_red zerochew");
        var barterFood = Table(packedFood.Id, "加工食品货表", string.Join(" ", packedFood.Categories), slots: 3);
        var household = Table("householdTable", "日用品货表",
            "caffeine_pill full_water_purification_tablet_box full_water_test_strip_box paper_towel shampoo toilet_paper toothpaste water_filter");
        var medical = Table("medicalTable", "医疗用品货表",
            "bandage_item hemostatic_bandage_item pink_injector rubbing_alcohol salve smelling_salt surgery_tool topical_bandage_item");
        rules.Add(new StockMethod(AccessTools.DeclaredMethod("StoreClientList:AddScavGeneralStock"),
            nameof(StoreClientList.AddScavGeneralStock), Bonus(Repeated('P', 40) + "Q" + Repeated('P', 27) + "E",
                2, 3, 4, 17, 18, 20, 26, 31, 32, 34, 46, 51, 58, 66, 68),
            new[] { scavenger, eleanor }, choices: new[] { packedFood }));
        Visit(Callback(AccessTools.DeclaredMethod(typeof(StoreClientUniqueList), nameof(StoreClientUniqueList.Eleanor5))),
            nameof(StoreClientUniqueList.Eleanor5), "eleanor5", "埃莉诺（材料供货）", "",
            Table("junkTable", "杂物货表", "empty_beer_bottle glass_shard junk pipe_weapon scratcher system_module_ruined", slots: 4),
            Table("materialTable", "材料货表", "common_electronic flux_agent gun_part kotton_fabric nuts_metal printer_plastic scrap_metal wire", slots: 3));

        // The crate appearance roll and the crate type are separate decisions.
        Visit(AccessTools.DeclaredMethod("StoreClientList+<>c:<CreateThief>b__56_0"),
            nameof(StoreClientList.CreateThief), "thief", "小偷（常规）",
            Repeated('F', 20) + "nFn" + Repeated('F', 5) + "Ei",
            new StockChoice("crate", "物资箱", StockChoiceKind.Range,
                "loot_crate_evidence loot_crate_medical loot_crate_engineering loot_crate_security loot_crate_service",
                new[] { 21, 23 }, rangeStarts: new[] { 0, 21, 41, 61, 76, 101 }, productCategory: "crate_random"),
            new StockChoice("fallback", "柜台无待售货物时补货", StockChoiceKind.IntegerRoll, "common_chemical common_medical",
                new[] { 30 }, new[] { 50d },
                tooltip: () => Text("小偷摆出常规货物后检查柜台。柜台没有待售货物时，抽取一种补货并摆出 3 份。",
                    "The thief checks the counter after laying out the regular stock. If the counter has no goods for sale, the thief picks one restock type and puts out 3 units.")));
        Visit(AccessTools.DeclaredMethod("StoreClientList+<>c:<CreatePettyThief>b__57_0"),
            nameof(StoreClientList.CreatePettyThief), "pettyThief", "扒手",
            Bonus("CCCC" + Repeated('F', 9), 6, 7, 8, 10, 12, 13), packedFood, household, medical);
        Visit(AccessTools.DeclaredMethod("StoreClientList+<>c:<CreateFoodThief>b__58_0"),
            nameof(StoreClientList.CreateFoodThief), "foodThief", "食物窃贼",
            Bonus(Repeated('F', 11) + "CCCCC" + "FFFF", 2, 3, 5, 6, 7, 9, 10, 11, 18, 19, 20));
        Visit(AccessTools.DeclaredMethod("StoreClientListSupplier:AddScavFoodStock"),
            nameof(StoreClientListSupplier.AddScavFoodStock), "scavGenericFoodSeller", "拾荒客（食品霓虹灯牌）",
            Bonus(Repeated('P', 12) + "QQQP", 2, 3, 4, 6, 8, 10, 12, 16), packedFood);
        Visit(AccessTools.DeclaredMethod("StoreClientListSupplier:AddScavHouseholdStock"),
            nameof(StoreClientListSupplier.AddScavHouseholdStock), "scavGenericHouseholdSeller", "拾荒客（日用品霓虹灯牌）",
            Bonus("QQ" + Repeated('P', 11), 4, 6, 13));
        Visit(AccessTools.DeclaredMethod("StoreClientListSupplier:AddScavMaterialStock"),
            nameof(StoreClientListSupplier.AddScavMaterialStock), "scavGenericMaterialSeller", "拾荒客（材料霓虹灯牌）",
            Bonus(Repeated('P', 13) + "QQQQ", 5, 6, 8, 11));
        Visit(AccessTools.DeclaredMethod("StoreClientListSupplier:AddScavMedStock"),
            nameof(StoreClientListSupplier.AddScavMedStock), "scavGenericMedSeller", "拾荒客（医疗霓虹灯牌）",
            Bonus("PPPPPPPQQPPPP", 2, 3, 11));
        Visit(AccessTools.DeclaredMethod("StoreClientListSupplier:AddThiefFoodStock"),
            nameof(StoreClientListSupplier.AddThiefFoodStock), "thiefGenericFoodSeller", "小偷（食品霓虹灯牌）",
            Bonus("PPPPPPPQP", 2, 4, 5, 6));
        Visit(AccessTools.DeclaredMethod("StoreClientListSupplier:AddThiefHouseholdStock"),
            nameof(StoreClientListSupplier.AddThiefHouseholdStock), "thiefGenericHouseholdSeller", "小偷（日用品霓虹灯牌）",
            Bonus(Repeated('P', 11), 2, 3, 5, 9, 11));
        Visit(AccessTools.DeclaredMethod("StoreClientListSupplier:AddThiefMaterialStock"),
            nameof(StoreClientListSupplier.AddThiefMaterialStock), "thiefGenericMaterialSeller", "小偷（材料霓虹灯牌）", "QQQQPQ");
        Visit(AccessTools.DeclaredMethod("StoreClientListSupplier:AddThiefMedStock"),
            nameof(StoreClientListSupplier.AddThiefMedStock), "thiefGenericMedSeller", "小偷（医疗霓虹灯牌）",
            Bonus("PPPPQQPPPPPP", 2, 4, 8, 10, 12));

        Visit(AccessTools.DeclaredMethod("StoreClientList+<>c:<CreateJunker>b__19_0"),
            nameof(StoreClientList.CreateJunker), "junker", "废料客", "QQQQPQQ");
        Visit(AccessTools.DeclaredMethod("StoreClientList+<>c:<CreateScrapper>b__43_0"),
            nameof(StoreClientList.CreateScrapper), "machine_scrapper", "废料拆解工", "PP");
        Visit(AccessTools.DeclaredMethod("StoreClientList+<>c:<CreateFarmerClientStandard>b__21_0"),
            nameof(StoreClientList.CreateFarmerClientStandard), "farmer_standard", "水培农场主", "NNN");
        Visit(AccessTools.DeclaredMethod("StoreClientList+<>c:<CreateMiner>b__46_0"),
            nameof(StoreClientList.CreateMiner), "miner", "矿工", "NQQQQ");
        Visit(AccessTools.DeclaredMethod("StoreClientList+<>c:<CreateStampSeller>b__22_0"),
            nameof(StoreClientList.CreateStampSeller), "scavStamp", "拾荒客（食品券）", "QQQ");
        Visit(AccessTools.DeclaredMethod("StoreClientList+<>c__DisplayClass51_0:<CreateScavCrate>b__0"),
            nameof(StoreClientList.CreateScavCrate), "scavCrate", "拾荒客（成批食品）", "N", packedFood);
        Visit(AccessTools.DeclaredMethod("StoreClientList+<>c:<CreateWaterSeller>b__63_0"),
            nameof(StoreClientList.CreateWaterSeller), "waterSeller", "水贩", "QQQPPQQQQ");
        Visit(AccessTools.DeclaredMethod("StoreClientList+<>c:<CreateShadyPharmacist>b__42_0"),
            nameof(StoreClientList.CreateShadyPharmacist), "shadyPharma", "上层区药商", "iQ",
            new StockChoice("shipment", "注射器货单", StockChoiceKind.IntegerRoll,
                "random_inspectable_injector pink_injector", new[] { 1 }, new[] { 50d }));
        Visit(AccessTools.DeclaredMethod("StoreClientList+<>c:<CreateRareLowerLevelChemist>b__75_0"),
            nameof(StoreClientList.CreateRareLowerLevelChemist), "rareLowerLevelChemist", "药商", "iQQiQQiQQQ",
            new StockChoice("shipment", "药品货单", StockChoiceKind.IntegerRoll, "dream_dust pink_injector black_injector fanta_pill",
                new[] { 1, 4, 7 }, new[] { 25d, 25d, 25d }));
        Visit(Callback(AccessTools.DeclaredMethod(typeof(StoreClientList), nameof(StoreClientList.CreateLowerLevelChemist), new[] { typeof(bool) })),
            nameof(StoreClientList.CreateLowerLevelChemist), "lowerLevelChemist", "下层区化学家（买卖）", "i",
            new StockChoice("shipment", "药品货单", StockChoiceKind.IntegerRoll, "dream_dust salve", new[] { 1 }, new[] { 50d }));
        Visit(Callback(AccessTools.DeclaredMethod(typeof(StoreClientListBarter), nameof(StoreClientListBarter.CreateLowerLevelChemistBarter), new[] { typeof(bool) })),
            nameof(StoreClientListBarter.CreateLowerLevelChemistBarter), "lowerLevelChemistBarter", "下层区化学家（以物易物）", "nn",
            new StockChoice("shipment", "以物易物货单", StockChoiceKind.Range, "salve caffeine_pill dream_dust",
                new[] { 1, 2 }, rangeStarts: new[] { 0, 1, 2, 3 }));
        Visit(Callback(AccessTools.DeclaredMethod(typeof(StoreClientList), nameof(StoreClientList.CreateInsScav))),
            nameof(StoreClientList.CreateInsScav), "scavInspectable", "拾荒客（待鉴定货物）", "xe",
            new StockChoice("inspectable", "待鉴定货物", StockChoiceKind.Inspectable,
                "random_stamp random_cigarette random_injector", new[] { 1 }));
        Visit(Callback(AccessTools.DeclaredMethod(typeof(StoreClientListSupplier), nameof(StoreClientListSupplier.CreateHypertechSmuggler))),
            nameof(StoreClientListSupplier.CreateHypertechSmuggler), "hypertech_smuggler", "超科技走私贩", "n",
            new StockChoice("shipment", "货物", StockChoiceKind.List,
                "hypertech_electronic energy_credit_hyp turbo_booster_hypertech hypertech_bay mind_reprogrammer system_uncapped_neural_core", new[] { 1 },
                quota: new StockQuota(StoreClientListSupplier.HYPERTECH_SMUGGLER_STOCK_COUNT,
                    "hypertech_bay", "mind_reprogrammer", "system_uncapped_neural_core")));
        Visit(Callback(AccessTools.DeclaredMethod(typeof(StoreClientList), nameof(StoreClientList.CreateBrokeUpperLevel))),
            nameof(StoreClientList.CreateBrokeUpperLevel), "brokeUpperLevel", "上层区公民（出售货物）", Repeated('f', 7),
            new StockChoice("shipment", "货单", StockChoiceKind.FloatRoll,
                "set_stun_weapons set_water_equipment energy_credit_breeder set_mixed_modules set_efficiency_modules set_quality_modules set_performance_modules blue_blood_bag",
                Enumerable.Range(1, 7).ToArray(), Enumerable.Repeat((double)14.28f, 7).ToArray()));
        Visit(AccessTools.DeclaredMethod("StoreClientList+<>c:<CreateGunsmith>b__73_0"),
            nameof(StoreClientList.CreateGunsmith), "lower_level_gunsmith", "下层区枪匠",
            Bonus("PPQ" + Repeated('P', 10), 2, 7, 8, 9, 11, 12, 13));
        Visit(AccessTools.DeclaredMethod("StoreClientList+<>c:<CreateLowerLevelRareMerchant>b__74_0"),
            nameof(StoreClientList.CreateLowerLevelRareMerchant), "lower_level_rare_merchant", "稀有品商人", "PPPP");
        Visit(AccessTools.DeclaredMethod("StoreClientList+<>c:<CreateSecSurplusMerchant>b__76_0"),
            nameof(StoreClientList.CreateSecSurplusMerchant), "secSurplusMerchant", "治安部征用官",
            Bonus(Repeated('P', 36), 2, 4, 10, 14, 16, 18, 20, 27, 29, 34, 35, 36));
        rules.Add(CreateInventorRule());
        Direct(AccessTools.DeclaredMethod("StoreClientList:PlaceSupplierInventory"),
            nameof(StoreClientList.PlaceSupplierInventory), "nPnPn",
            new StockProfile("supplier", "补给运输员"), new StockProfile("supplierIntro", "供货商"));

        Visit(AccessTools.DeclaredMethod("StoreClientListAppraisal+<>c:<CreateAppraisalRandom>b__3_0"),
            nameof(StoreClientListAppraisal.CreateAppraisalRandom), "scavAppraisalRandom", "拾荒客（鉴定服务）", "uU",
            new StockChoice("appraisal", "待鉴定物品", StockChoiceKind.Appraisal,
                "hand ribwich plasma_fuel", new[] { 1 },
                tooltip: () => Text("按原版鉴定证书和等级确定候选。只有一种候选时，按原版抽取。",
                    "Eligible goods depend on the vanilla appraisal certificate and rank. With one eligible choice, the vanilla roll applies."),
                productCategory: "appraisal_random"));
        Visit(AccessTools.DeclaredMethod("StoreClientListBarter+<>c:<CreateScavBarter>b__4_0"),
            nameof(StoreClientListBarter.CreateScavBarter), "scavBarter", "拾荒客（以物易物）", "iQQQPPPQQQ",
            new StockChoice("shipment", "以物易物货单", StockChoiceKind.IntegerRoll,
                "set_materials set_household_goods", new[] { 1 }, new[] { 50d }));
        // Each successful native roll adds a separate offer. Its contents are generated by the original factory.
        rules.Add(new StockMethod(AccessTools.DeclaredMethod(typeof(BarterOfferList), nameof(BarterOfferList.CreateAddictBarter)),
            nameof(BarterOfferList.CreateAddictBarter), "PPP", new[] { new StockProfile("addictBarter", "瘾君子拾荒客（初访）") },
            beforeVisit: true));
        rules.Add(new StockMethod(AccessTools.DeclaredMethod(typeof(BarterOfferList), nameof(BarterOfferList.CreateAddictGeneralBarter)),
            nameof(BarterOfferList.CreateAddictGeneralBarter), "PPPn" + Repeated('P', 18),
            new[] { new StockProfile("addictGeneralBarter", "瘾君子拾荒客") }, beforeVisit: true, choices: new[] { barterFood }));

        Visit(AccessTools.DeclaredMethod("StoreClientListCartel+<>c:<CreateShadyFarmer>b__22_0")
                ?? AccessTools.DeclaredMethod("StoreClientListCartel+<>c:<CreateShadyFarmer>b__20_0"),
            nameof(StoreClientListCartel.CreateShadyFarmer), "shadyFarmer", "可疑的农场主", "QQPQPP");
        Visit(AccessTools.DeclaredMethod("StoreClientListEvent+<>c:<CreateIceMiner>b__2_0"),
            nameof(StoreClientListEvent.CreateIceMiner), "iceMiner", "冰矿工", "N");
        Visit(AccessTools.DeclaredMethod("StoreClientListEvent+<>c:<CreateScavengerHouseholdClient>b__8_0"),
            nameof(StoreClientListEvent.CreateScavengerHouseholdClient), "scavengerHousehold", "拾荒客（日用品事件）", "PQQPQQQQ");
        Visit(AccessTools.DeclaredMethod("StoreClientListEvent+<>c:<CreateFarmerSeedClient>b__21_0"),
            nameof(StoreClientListEvent.CreateFarmerSeedClient), "farmerSeedBuyer", "农场主（种子交易）", "QQ");

        // Gun type can change where the seller chooses between distinct weapons. Conditions and fitted mods use the native draws.
        Visit(AccessTools.DeclaredMethod("StoreClientListGun+<>c:<CreateLLGunSeller>b__28_0"),
            nameof(StoreClientListGun.CreateLLGunSeller), "ll_gun_seller", "下层区公民（枪械供货）",
            Bonus("i" + Repeated('P', 25), 16, 17, 18, 20, 21, 22, 24, 25, 26));
        Visit(AccessTools.DeclaredMethod("StoreClientListGun+<>c:<CreateMercGunSeller>b__29_0"),
            nameof(StoreClientListGun.CreateMercGunSeller), "merc_gun_seller", "雇佣兵（枪械供货）",
            Bonus("ii" + Repeated('P', 25), 17, 18, 19, 21, 22, 23, 25, 26, 27),
            new StockChoice("firearm", "枪械", StockChoiceKind.IntegerRoll,
                "revolver heavy_handmade_pistol", new[] { 2 }, new[] { 25d }));
        Visit(AccessTools.DeclaredMethod("StoreClientListGun+<>c:<CreateULGunSeller>b__30_0"),
            nameof(StoreClientListGun.CreateULGunSeller), "ul_gun_seller", "上层区公民（枪械供货）",
            Bonus("iiiii" + Repeated('P', 26), 25, 26, 27, 29, 30, 31),
            new StockChoice("firearm", "枪械", StockChoiceKind.IntegerRoll,
                "glock_receiver smg", new[] { 1 }, new[] { 75d }));
        Visit(AccessTools.DeclaredMethod("StoreClientListRev+<>c:<CreateRevRaider>b__0_0"),
            nameof(StoreClientListRev.CreateRevRaider), "revRaider", "革命军游击队",
            Bonus("PPFFPPFFFFFFFFFFFPFFFFFFCFFFFFPFF", 15, 17, 21, 22, 24, 29, 30));
        Visit(Callback(AccessTools.DeclaredMethod(typeof(StoreClientListRev), nameof(StoreClientListRev.CreateRevQuartermaster))),
            nameof(StoreClientListRev.CreateRevQuartermaster), "revQuartermaster", "革命军军需官", "ii",
            new StockChoice("shipment", "货单", StockChoiceKind.IntegerRoll,
                "common_ore scrap_metal nutrifruit", new[] { 1, 2 }, new[] { 25d, 25d }));

        Visit(AccessTools.DeclaredMethod("StoreClientListSpec+<>c:<CreateRetiredGunsmith>b__2_0"),
            nameof(StoreClientListSpec.CreateRetiredGunsmith), "retired_gunsmith", "退休枪匠", Repeated('P', 39));
        Visit(AccessTools.DeclaredMethod("StoreClientListSpec+<>c:<CreateRetiredFarmer>b__3_0"),
            nameof(StoreClientListSpec.CreateRetiredFarmer), "retired_farmer", "退休农夫", "PPPPPPP");
        rules.Add(new StockMethod(AccessTools.DeclaredMethod("StoreClientListSpec+<>c:<CreateRetiredAquafarmer>b__6_0"),
            nameof(StoreClientListSpec.CreateRetiredAquafarmer), "nAAAAA",
            new[] { new StockProfile("retired_aquafarmer", "退休水产养殖户（水产养殖 II）") }, choices: new[]
            {
                new StockChoice("fish", "成鱼", StockChoiceKind.Range, "eel electric_eel sturgeon", new[] { 1 },
                    rangeStarts: new[] { 0, 1, 2, 3 }, quota: new StockQuota(3))
            }, fixedProfile: new StockProfile("retired_aquafarmer_basic", "退休水产养殖户（水产养殖）")
            {
                FixedStockNotice = () => Text("水产养殖阶段的货物种类和数量按原版规则生成。",
                    "The Aquaculture stage uses the vanilla goods and quantities.")
            }));
        Visit(AccessTools.DeclaredMethod("StoreClientListSpec+<>c:<CreateRetiredRancher>b__7_0"),
            nameof(StoreClientListSpec.CreateRetiredRancher), "retired_rancher", "退休养鼠人", "PdQdPdQd");
        Visit(AccessTools.DeclaredMethod("StoreClientListSpec+<>c:<CreateRetiredWaterMerchant>b__8_0"),
            nameof(StoreClientListSpec.CreateRetiredWaterMerchant), "retired_water_merchant", "退休水商", "QQQP");
        Visit(AccessTools.DeclaredMethod("StoreClientListSpec+<>c:<CreateRetiredWinemaker>b__9_0"),
            nameof(StoreClientListSpec.CreateRetiredWinemaker), "retired_winemaker", "退休酿酒师", "iiP",
            new StockChoice("yeast", "葡萄酒酵母", StockChoiceKind.IntegerRoll,
                "wine_superyeast wine_yeast", new[] { 1, 2 }, new[] { 25d },
                tooltip: () => Line("每次固定携带 {0} 件{1}。本组配置另外 {0} 件酵母，权重按单个名额计算。",
                    "The customer always brings {0} units of {1}. This group controls the {0} additional units of yeast, with weights calculated per slot.",
                    2, StockCatalog.Name("wine_yeast")), quota: new StockQuota(2)));
        Visit(AccessTools.DeclaredMethod("StoreClientListSpec+<>c:<CreateRetiredJunker>b__10_0"),
            nameof(StoreClientListSpec.CreateRetiredJunker), "retired_junker", "退休废料客", "QQQ");
        Visit(AccessTools.DeclaredMethod("StoreClientListSpec+<>c:<CreateRetiredChemist>b__12_0"),
            nameof(StoreClientListSpec.CreateRetiredChemist), "retired_chemist", "退休化学家",
            Bonus(Repeated('P', 15) + "QQQ", 12, 15));

        Surplus(AccessTools.DeclaredMethod("StoreClientListSurplus+<>c:<CreateFoodSurplusClient>b__0_0"),
            AccessTools.DeclaredMethod("StoreClientListSurplus+<>c:<CreateFoodSurplusClient>b__0_1"),
            nameof(StoreClientListSurplus.CreateFoodSurplusClient), "food_surplus_client", "下层区商人（食物过剩）", "QQ", "Q", packedFood);
        Surplus(AccessTools.DeclaredMethod("StoreClientListSurplus+<>c:<CreateMedicalSurplusClient>b__1_0"),
            AccessTools.DeclaredMethod("StoreClientListSurplus+<>c:<CreateMedicalSurplusClient>b__1_1"),
            nameof(StoreClientListSurplus.CreateMedicalSurplusClient), "medical_surplus_client", "下层区摊贩（医疗用品过剩）",
            "QPPPPPQQQQQQ", "QPPPPP");
        Surplus(AccessTools.DeclaredMethod("StoreClientListSurplus+<>c:<CreateSubstanceSurplusClient>b__2_0"),
            AccessTools.DeclaredMethod("StoreClientListSurplus+<>c:<CreateSubstanceSurplusClient>b__2_1"),
            nameof(StoreClientListSurplus.CreateSubstanceSurplusClient), "substance_surplus_client", "狡猾的商人（药品过剩）",
            "PQPQQQQQQ", "PQPQQQ");
        Surplus(AccessTools.DeclaredMethod("StoreClientListSurplus+<>c:<CreateWeaponArmorSurplusClient>b__3_0"),
            AccessTools.DeclaredMethod("StoreClientListSurplus+<>c:<CreateWeaponArmorSurplusClient>b__3_1"),
            nameof(StoreClientListSurplus.CreateWeaponArmorSurplusClient), "weapon_armor_surplus_client", "下层区武器商人（武器和护甲过剩）",
            "PPPQQQPQQ", "PPPQQQPQ");
        Surplus(AccessTools.DeclaredMethod("StoreClientListSurplus+<>c:<CreateMaterialSurplusClient>b__4_0"),
            AccessTools.DeclaredMethod("StoreClientListSurplus+<>c:<CreateMaterialSurplusClient>b__4_1"),
            nameof(StoreClientListSurplus.CreateMaterialSurplusClient), "material_surplus_client", "下层区商人（材料过剩）",
            "PQQQQQPQQPQ", "PQQQPQP");

        Visit(AccessTools.DeclaredMethod("StoreClientListWater+<>c:<CreateWaterCompany>b__1_0"),
            nameof(StoreClientListWater.CreateWaterCompany), "waterCompany", "下层区公民（供水商）", "NPPPN");
        Visit(AccessTools.DeclaredMethod("StoreClientListWater+<>c:<CreateWaterSellScav>b__2_0"),
            nameof(StoreClientListWater.CreateWaterSellScav), "waterSellerScav", "拾荒客（售水）", "N");
        Visit(AccessTools.DeclaredMethod("StoreClientListWater+<>c:<CreateDishonestWaterSellScav>b__3_0"),
            nameof(StoreClientListWater.CreateDishonestWaterSellScav), "waterHishonestSellerScav", "拾荒客（隐瞒水质）", "QPQPQ");
        var honest = new StockProfile("honestBadWaterSeller", "拾荒客（如实说明水质）");
        rules.Add(new StockMethod(AccessTools.DeclaredMethod(typeof(StoreClientListWater), nameof(StoreClientListWater.SetHonestWaterSellerDialog)),
            nameof(StoreClientListWater.SetHonestWaterSellerDialog), "i", new[] { honest }, beforeVisit: true, choices: new[]
            {
                new StockChoice("water_quality", "水质货单", StockChoiceKind.IntegerRoll,
                    "set_base_water set_ghost_water", new[] { 1 }, new[] { 75d },
                    tooltip: () => Line("每次来访只抽取一套水质货单。抽中{0}且尚未解锁「{1}」时，改为供应{2}。",
                        "Each visit uses one water stock set. If the {0} is chosen before {1} is unlocked, the customer supplies {2}.",
                        StockCatalog.Name("set_base_water"), GameTerm("NetworkUpgrade/network_WATER_TRADER_title"), StockCatalog.Name("water_ration")))
            }));
        rules.Add(new StockMethod(AccessTools.DeclaredMethod("StoreClientListWater+<>c:<SetHonestWaterSellerDialog>b__5_0"),
            nameof(StoreClientListWater.SetHonestWaterSellerDialog), "NN", new[] { honest }, catalogKey: "SetHonestWaterSellerDialog/base"));
        rules.Add(new StockMethod(AccessTools.DeclaredMethod("StoreClientListWater+<>c:<SetHonestWaterSellerDialog>b__5_1"),
            nameof(StoreClientListWater.SetHonestWaterSellerDialog), "N", new[] { honest }, catalogKey: "SetHonestWaterSellerDialog/ghost"));

        Visit(AccessTools.DeclaredMethod("StoreClientListWanted+<>c:<CreateWanted1Return>b__5_0"),
            nameof(StoreClientListWanted.CreateWanted1Return), "wanted1_return", "下层区公民（詹姆斯·贝利回访）", "PPPiPN",
            new StockChoice("crate", "物资箱", StockChoiceKind.IntegerRoll,
                "loot_crate_security loot_crate_evidence", new[] { 4 }, new[] { 50d }, productCategory: "crate_evidence_security"));
        Direct(AccessTools.DeclaredMethod("StoreClientListWanted:RollButcherCount"),
            "RollButcherCount", "Q", new StockProfile("wanted2_return", "下层区公民（屠夫回访）"));
        Visit(AccessTools.DeclaredMethod("StoreClientListWanted+<>c:<CreateWanted7>b__41_2"),
            nameof(StoreClientListWanted.CreateWanted7), "wanted7", "下层区劳工（胡安）", Repeated('Q', 12));

        return rules.ToArray();

        void Visit(MethodInfo? target, string method, string id, string label, string draws, params StockChoice[] choices)
            => rules.Add(new StockMethod(target, method, draws, new[] { new StockProfile(id, label) }, choices: choices));

        void Direct(MethodInfo? target, string method, string draws, params StockProfile[] profiles)
            => rules.Add(new StockMethod(target, method, draws, profiles));

        void Surplus(MethodInfo? severe, MethodInfo? regular, string method, string id, string label,
            string severeDraws, string regularDraws, params StockChoice[] choices)
        {
            var profile = new StockProfile(id, label);
            rules.Add(new StockMethod(severe, method, severeDraws, new[] { profile }, catalogKey: method + "/large", choices: choices));
            rules.Add(new StockMethod(regular, method, regularDraws, new[] { profile }, catalogKey: method + "/regular", choices: choices));
        }
    }

    private static StockChoice Table(string id, string label, string categories, int slots = 0)
        => new StockChoice(id, label, StockChoiceKind.Table, categories, Array.Empty<int>(),
            quota: slots > 0 ? new StockQuota(slots) : null);

    private static string Repeated(char draw, int count) => new string(draw, count);

    // The ordinals refer to the native draw order, including draws handled by the game itself.
    private static string Bonus(string draws, params int[] positions)
    {
        char[] adjusted = draws.ToCharArray();
        foreach (int position in positions)
            adjusted[position - 1] = adjusted[position - 1] == 'F' ? 'C' : 'Q';
        return new string(adjusted);
    }

    private static MethodInfo? Callback(MethodInfo? method)
        => method == null ? null : PatchProcessor.GetOriginalInstructions(method)
            .Where(instruction => instruction.opcode == OpCodes.Ldftn).Select(instruction => instruction.operand).OfType<MethodInfo>()
            .FirstOrDefault(callback => callback.Name.StartsWith("<" + method.Name + ">b__", StringComparison.Ordinal));

    private static StockMethod CreateInventorRule()
    {
        var draws = Repeated('P', 88).ToCharArray();
        // The first three draws select an optional backpack outside the shop visit. The fourth is discarded.
        for (int index = 0; index < 4; index++)
            draws[index] = 'i';

        // Bonus copies of cells, kits, tanks, chemistry modules and DNA disks.
        foreach (int position in new[] { 39, 43, 44, 54, 55, 66, 67, 69, 71, 72, 87, 88 })
            draws[position - 1] = 'Q';

        var profiles = new[]
        {
            new StockProfile("inventor", "杰克逊博士（基础）"),
            new StockProfile("inventorVisitInvestment1", "杰克逊博士（杰克逊投资 I）"),
            new StockProfile("inventorVisitInvestment2", "杰克逊博士（杰克逊投资 II）")
        };
        string[] variants = Enumerable.Range(0, profiles.Length).Select(DrawsForInvestment).ToArray();
        return new StockMethod(AccessTools.DeclaredMethod(typeof(StoreClientList), nameof(StoreClientList.PlaceInventorInventory)),
            nameof(StoreClientList.PlaceInventorInventory), new string(draws), profiles, profileDraws: variants, fixedProfile: InventorSpecial,
            choices: new[] { InventorBackpack });

        string DrawsForInvestment(int tier)
        {
            var variant = (char[])draws.Clone();
            if (tier < 2)
                foreach (int position in new[] { 55, 66, 88 })
                    variant[position - 1] = 'i';
            if (tier == 0)
                foreach (int position in new[] { 54, 67, 87 })
                    variant[position - 1] = 'i';

            if (tier >= 1)
                foreach (int position in new[] { 31, 32, 33, 37 })
                    variant[position - 1] = 'Q';
            if (tier == 2)
                foreach (int position in new[] { 34, 35, 36 })
                    variant[position - 1] = 'Q';
            return new string(variant);
        }
    }
}
