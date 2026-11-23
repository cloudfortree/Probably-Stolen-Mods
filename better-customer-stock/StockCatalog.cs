using System.Collections.Generic;

namespace BetterCustomerStock;

internal enum StockItemGroup
{
    SupplyCrate,
    AccessCard,
    DesequencerChip,
    PerformanceModule,
    OverclockModule,
    QualityModule,
    FinenessModule,
    EfficiencyModule,
    EcoModule,
    DamagedModule,
    UniversalModule,
    MachineModule,
    HydroponicModule,
    PrinterModule,
    DnaManipulatorModule,
    Node,
    PrinterMagazine22Chip,
    PrinterMagazine10Chip,
    PrinterGunChip,
    PrinterGunModChip,
    PrinterMeleeChip,
    DnaDataDisk,
    Machine,
    Storage,
    EnergyCell,
    GraduatedCylinder,
    Dropper,
    ModuleExtractor,
    ExpansionKit,
    Tool,
    FluxAgent,
    Material,
    ChemicalSupply,
    Water,
    WaterTesting,
    WaterFilter,
    WaterPurifier,
    WaterStorage,
    WaterPurificationSupply,
    Food,
    Seed,
    BrewingSupply,
    Husbandry,
    Bandage,
    BloodSupply,
    Injector,
    Medical,
    HouseholdGood,
    LuxuryItem,
    Alcohol,
    Tobacco,
    Firearm,
    GunRedDotSight,
    GunLaser,
    GunSilencer,
    GunStock,
    GunSpring,
    GunMod,
    Magazine22,
    Magazine10,
    SmgMagazine10,
    Ammunition22,
    Ammunition10,
    StunWeapon,
    MeleeWeapon,
    Munition,
    Armor,
    MusicCassette,
    Lottery,
    FoodStamp,
    Appraisal,
}

// Draw order and item names were checked against the locally unpacked game.
// Each token names one adjustable call. A plus links categories that share the same draw.
internal static class StockCatalog
{
    internal static string Name(string id) => StockLanguage.ItemName(id, Items[id].Name);

    // Enum order keeps related item families and paired module effects together in the settings.
    internal static readonly IReadOnlyDictionary<string, (string Name, StockItemGroup Group)> Items =
        new Dictionary<string, (string, StockItemGroup)>
        {
            ["100ml_cylinder"] = ("100毫升量筒", StockItemGroup.GraduatedCylinder),
            ["1ml_dropper"] = ("1毫升滴管", StockItemGroup.Dropper),
            ["25ml_cylinder"] = ("25毫升量筒", StockItemGroup.GraduatedCylinder),
            ["50ml_cylinder"] = ("50毫升量筒", StockItemGroup.GraduatedCylinder),
            ["5ml_dropper"] = ("5毫升滴管", StockItemGroup.Dropper),
            ["advanced_flux_agent"] = ("助熔剂（高级）", StockItemGroup.FluxAgent),
            ["alarm_module_transmitter"] = ("警报传输模组", StockItemGroup.MachineModule),
            ["ammo_display"] = ("弹药显示器", StockItemGroup.GunMod),
            ["appraisal_random"] = ("待鉴定物品（每次随机选一种）", StockItemGroup.Appraisal),
            ["hand"] = ("仿生机械手", StockItemGroup.Appraisal),
            ["ribwich"] = ("袋装肋排三明治", StockItemGroup.Appraisal),
            ["plasma_fuel"] = ("等离子燃料匣", StockItemGroup.Appraisal),
            ["aquascan"] = ("水质扫描仪", StockItemGroup.WaterTesting),
            ["armor_lining"] = ("韧棉衬垫", StockItemGroup.Armor),
            ["aug_scanner"] = ("改造体扫描仪", StockItemGroup.Tool),
            ["auto_butcher"] = ("自动屠宰机", StockItemGroup.Machine),
            ["backpack_large"] = ("背包（大）", StockItemGroup.Storage),
            ["backpack_medium"] = ("背包（基础）", StockItemGroup.Storage),
            ["backpack_small"] = ("背包（小）", StockItemGroup.Storage),
            ["bandage_item"] = ("基础绷带", StockItemGroup.Bandage),
            ["beis_icecream"] = ("贝氏优质香草冰淇淋", StockItemGroup.Food),
            ["bipod"] = ("两脚架", StockItemGroup.GunMod),
            ["birth_control_pill"] = ("避孕药", StockItemGroup.Medical),
            ["black_injector"] = ("赛纳普斯注射器", StockItemGroup.Injector),
            ["black_lamp"] = ("暗灯", StockItemGroup.Tool),
            ["blank_keycard"] = ("一次性钥匙卡（空白）", StockItemGroup.AccessCard),
            ["blood_bag"] = ("血袋", StockItemGroup.BloodSupply),
            ["bloomberry_seed"] = ("荧光莓种子", StockItemGroup.Seed),
            ["blue_blood_bag"] = ("一袋“蓝血”", StockItemGroup.BloodSupply),
            ["bottled_water_base"] = ("瓶装水（标准）（基础水）", StockItemGroup.Water),
            ["bottled_water_ghost"] = ("瓶装水（标准）（幽灵水）", StockItemGroup.Water),
            ["bottled_water_ghost_as_base"] = ("瓶装水（标准）（伪装成基础水的幽灵水）", StockItemGroup.Water),
            ["bottled_water_ghost_as_pure"] = ("瓶装水（标准）（伪装成纯水的幽灵水）", StockItemGroup.Water),
            ["bottled_water_high"] = ("瓶装水（标准）（优质水）", StockItemGroup.Water),
            ["bottled_water_premium"] = ("瓶装泉水", StockItemGroup.Water),
            ["bottled_water_random"] = ("瓶装水（标准）（水质随机）", StockItemGroup.Water),
            ["box_cutter"] = ("美工刀", StockItemGroup.MeleeWeapon),
            ["box_tampon"] = ("一盒帕克斯卫生棉条", StockItemGroup.HouseholdGood),
            ["broken_furnace"] = ("熔炉（已报废）", StockItemGroup.Machine),
            ["broken_moisture_farm"] = ("水分收集器（已报废）", StockItemGroup.Machine),
            ["c4"] = ("塑性炸药", StockItemGroup.Munition),
            ["caffeine_pill"] = ("咖啡因药片", StockItemGroup.Medical),
            ["cassette"] = ("音乐磁带 （完好）", StockItemGroup.MusicCassette),
            ["cassette_d"] = ("音乐磁带 （损坏）", StockItemGroup.MusicCassette),
            ["cassette_l"] = ("音乐磁带 （受损）", StockItemGroup.MusicCassette),
            ["cat_bar"] = ("猫咪巧克力棒", StockItemGroup.Food),
            ["chem_module"] = ("化学设备模组", StockItemGroup.MachineModule),
            ["chem_scanner"] = ("成分光谱仪", StockItemGroup.Tool),
            ["cmd_keycard"] = ("一次性钥匙卡（指挥）", StockItemGroup.AccessCard),
            ["coffee_bean"] = ("咖啡豆", StockItemGroup.Seed),
            ["combat_knife"] = ("战斗匕首", StockItemGroup.MeleeWeapon),
            ["common_chemical"] = ("化学用品（普通）", StockItemGroup.ChemicalSupply),
            ["common_electronic"] = ("电子元件（普通）", StockItemGroup.Material),
            ["common_medical"] = ("医疗用品（普通）", StockItemGroup.Medical),
            ["common_ore"] = ("金属矿石", StockItemGroup.Material),
            ["compensator"] = ("补偿器", StockItemGroup.GunMod),
            ["crate_evidence_security"] = ("治安部物资箱或治安部物证柜（随机）", StockItemGroup.SupplyCrate),
            ["crate_random"] = ("任意随机物资箱", StockItemGroup.SupplyCrate),
            ["loot_crate_evidence"] = ("治安部物证柜", StockItemGroup.SupplyCrate),
            ["loot_crate_medical"] = ("医疗舱物资箱", StockItemGroup.SupplyCrate),
            ["loot_crate_engineering"] = ("工程铸造厂物资箱", StockItemGroup.SupplyCrate),
            ["loot_crate_security"] = ("治安部物资箱", StockItemGroup.SupplyCrate),
            ["loot_crate_service"] = ("后勤服务部物资箱", StockItemGroup.SupplyCrate),
            ["crowbar"] = ("撬棍", StockItemGroup.MeleeWeapon),
            ["cup_noodle"] = ("速食拉面", StockItemGroup.Food),
            ["crypto_module_cmd"] = ("密码解序器芯片（指挥）", StockItemGroup.DesequencerChip),
            ["crypto_module_eng"] = ("密码解序器芯片（工程）", StockItemGroup.DesequencerChip),
            ["crypto_module_med"] = ("密码解序器芯片（医疗）", StockItemGroup.DesequencerChip),
            ["crypto_module_sec"] = ("密码解序器芯片（治安）", StockItemGroup.DesequencerChip),
            ["crypto_module_ser"] = ("密码解序器芯片（服务）", StockItemGroup.DesequencerChip),
            ["desequencer"] = ("密码解序器", StockItemGroup.Machine),
            ["dna_manipulator_module_negative_filter"] = ("负面特性筛选模组", StockItemGroup.DnaManipulatorModule),
            ["dna_manipulator_module_positive_filter"] = ("正面特性筛选模组", StockItemGroup.DnaManipulatorModule),
            ["dna_manipulator_module_precision"] = ("精准提取模组", StockItemGroup.DnaManipulatorModule),
            ["dna_manipulator_module_stabilizer"] = ("基因稳定模组", StockItemGroup.DnaManipulatorModule),
            ["dream_cap"] = ("梦帽菇", StockItemGroup.ChemicalSupply),
            ["dream_dust"] = ("一小瓶“梦尘”", StockItemGroup.Medical),
            ["eel"] = ("鳗鱼", StockItemGroup.Husbandry),
            ["electric_eel"] = ("电鳗", StockItemGroup.Husbandry),
            ["empty_beer_bottle"] = ("空啤酒瓶", StockItemGroup.BrewingSupply),
            ["energy_credit"] = ("能量电池", StockItemGroup.EnergyCell),
            ["energy_credit_breeder"] = ("自充电能量电池", StockItemGroup.EnergyCell),
            ["energy_credit_dis"] = ("一次性电池", StockItemGroup.EnergyCell),
            ["energy_credit_hyp"] = ("超科技能源单元", StockItemGroup.EnergyCell),
            ["energy_drink"] = ("能量饮料", StockItemGroup.Food),
            ["eng_keycard"] = ("一次性钥匙卡（工程）", StockItemGroup.AccessCard),
            ["evaporator_module_solvent_recovery"] = ("蒸发器溶剂回收模组", StockItemGroup.MachineModule),
            ["fanta_pill"] = ("芬太片", StockItemGroup.Medical),
            ["farm_module_dream_cap"] = ("梦帽菇中和水培模组", StockItemGroup.HydroponicModule),
            ["farm_module_fillerweed_production"] = ("填料草增产水培模组", StockItemGroup.HydroponicModule),
            ["farm_module_hydroreed_filter"] = ("水培芦苇滤芯模组", StockItemGroup.HydroponicModule),
            ["farm_module_kotton_loom"] = ("韧棉自动织造水培模组", StockItemGroup.HydroponicModule),
            ["farm_module_nightmare_cap"] = ("梦帽菇梦魇化水培模组", StockItemGroup.HydroponicModule),
            ["farm_module_nutrifruit"] = ("营养果水培模组", StockItemGroup.HydroponicModule),
            ["farm_module_smuggler"] = ("水培走私模组", StockItemGroup.HydroponicModule),
            ["fat_meat"] = ("动物脂肪", StockItemGroup.Food),
            ["flashlight"] = ("手电筒", StockItemGroup.Tool),
            ["flux_agent"] = ("助熔剂（基础）", StockItemGroup.FluxAgent),
            ["fridge"] = ("冰箱", StockItemGroup.Machine),
            ["furnace"] = ("熔炉", StockItemGroup.Machine),
            ["furnace_module_blast"] = ("熔炉模组（高炉）", StockItemGroup.MachineModule),
            ["furnace_module_junk"] = ("熔炉模组（垃圾处理）", StockItemGroup.MachineModule),
            ["galaxy_blend"] = ("繁星果糊", StockItemGroup.Food),
            ["genuine_genuine_injector"] = ("免疫宁注射器（正品）", StockItemGroup.Injector),
            ["gl"] = ("25毫米下挂式榴弹发射器", StockItemGroup.GunMod),
            ["glock_receiver"] = ("哨兵3型。", StockItemGroup.Firearm),
            ["glass_shard"] = ("碎玻璃", StockItemGroup.Material),
            ["glass_shard_shiv"] = ("玻璃刀", StockItemGroup.MeleeWeapon),
            ["full_water_purification_tablet_box"] = ("一瓶净水药片（装满）", StockItemGroup.WaterPurificationSupply),
            ["full_water_test_strip_box"] = ("水质检测试剂盒（装满）", StockItemGroup.WaterTesting),
            ["hypertech_bay"] = ("超科技储藏区", StockItemGroup.Storage),
            ["hypertech_electronic"] = ("电子元件（超科技）", StockItemGroup.Material),
            ["junk"] = ("垃圾", StockItemGroup.Material),
            ["mind_reprogrammer"] = ("心智重编程器", StockItemGroup.Machine),
            ["nuts_metal"] = ("金属螺丝", StockItemGroup.Material),
            ["red_beer"] = ("红魔鬼啤酒", StockItemGroup.Alcohol),
            ["soda_red"] = ("汽水", StockItemGroup.Food),
            ["sturgeon"] = ("鲟鱼", StockItemGroup.Husbandry),
            ["system_uncapped_neural_core"] = ("神经核心模组（无限制）", StockItemGroup.MachineModule),
            ["turbo_booster_hypertech"] = ("超科技涡轮增压器", StockItemGroup.MachineModule),
            ["grip"] = ("前握把", StockItemGroup.GunMod),
            ["grip_gel"] = ("触感记忆握把凝胶", StockItemGroup.GunMod),
            ["gun_part"] = ("枪械零件", StockItemGroup.Material),
            ["handmade_pistol"] = ("“巢火”紧凑型手枪", StockItemGroup.Firearm),
            ["hatchet"] = ("短柄斧", StockItemGroup.MeleeWeapon),
            ["heavy_handmade_pistol"] = ("“铁铸10”手枪", StockItemGroup.Firearm),
            ["heavy_pistol_ammo"] = ("10毫米弹药", StockItemGroup.Ammunition10),
            ["heavy_pistol_ammo_nl"] = ("10毫米弹药（非致命）", StockItemGroup.Ammunition10),
            ["heavy_pistol_ammo_p"] = ("10毫米弹药（穿甲弹）", StockItemGroup.Ammunition10),
            ["hemostatic_bandage_item"] = ("止血绷带", StockItemGroup.Bandage),
            ["householdTable"] = ("日用品（随机）", StockItemGroup.HouseholdGood),
            ["hydroponic"] = ("水培系统", StockItemGroup.Machine),
            ["internal_springs"] = ("高耐久内部弹簧组", StockItemGroup.GunSpring),
            ["internal_springs_improved"] = ("改良内部弹簧组", StockItemGroup.GunSpring),
            ["kitchen_cleaver"] = ("切肉刀", StockItemGroup.MeleeWeapon),
            ["kitchen_knife"] = ("厨刀", StockItemGroup.MeleeWeapon),
            ["kotton_fabric"] = ("韧棉织物", StockItemGroup.Material),
            ["kotton_seed"] = ("韧棉种子", StockItemGroup.Seed),
            ["large_bottled_water"] = ("瓶装水（大）", StockItemGroup.Water),
            ["large_bottled_water_base"] = ("瓶装水（大）（基础水）", StockItemGroup.Water),
            ["large_bottled_water_ghost_as_base"] = ("瓶装水（大）（伪装成基础水的幽灵水）", StockItemGroup.Water),
            ["laser"] = ("激光瞄准器", StockItemGroup.GunLaser),
            ["laser2"] = ("目标捕获装置", StockItemGroup.GunLaser),
            ["laser_toy"] = ("玩具激光笔", StockItemGroup.GunLaser),
            ["li_eat_snackbar"] = ("轻食能量棒", StockItemGroup.Food),
            ["livestock_feed_dispenser"] = ("牲畜饲料分配器", StockItemGroup.Machine),
            ["machine_bay"] = ("机器区", StockItemGroup.Storage),
            ["mag_10mm"] = ("10毫米弹匣", StockItemGroup.Magazine10),
            ["mag_10mm_ext"] = ("加长10毫米弹匣", StockItemGroup.Magazine10),
            ["mag_10mm_high"] = ("大容量10毫米弹匣", StockItemGroup.Magazine10),
            ["mag_10mm_smg"] = ("10毫米冲锋枪弹匣", StockItemGroup.SmgMagazine10),
            ["mag_10mm_smg_box"] = ("10毫米冲锋枪箱式弹匣", StockItemGroup.SmgMagazine10),
            ["mag_10mm_smg_high"] = ("大容量10毫米冲锋枪弹匣", StockItemGroup.SmgMagazine10),
            ["mag_22"] = (".22弹匣", StockItemGroup.Magazine22),
            ["mag_22_ext"] = ("加长.22弹匣", StockItemGroup.Magazine22),
            ["mag_22_high"] = ("大容量.22弹匣", StockItemGroup.Magazine22),
            ["marking_pen"] = ("标记笔", StockItemGroup.Husbandry),
            ["match_barrel"] = ("比赛级枪管", StockItemGroup.GunMod),
            ["med_keycard"] = ("一次性钥匙卡（医疗）", StockItemGroup.AccessCard),
            ["medicalTable"] = ("医疗用品（随机）", StockItemGroup.Medical),
            ["metal_scanner"] = ("拾荒扫描仪", StockItemGroup.Tool),
            ["mirage_projector"] = ("幻影投影仪", StockItemGroup.Machine),
            ["module_bay_expansion_kit"] = ("模组舱扩建套件", StockItemGroup.ExpansionKit),
            ["module_extractor"] = ("模组提取器（基础）", StockItemGroup.ModuleExtractor),
            ["module_extractor_advanced"] = ("模组提取器（高级）", StockItemGroup.ModuleExtractor),
            ["moisture_farm"] = ("水分收集器", StockItemGroup.Machine),
            ["mouse_trap"] = ("捕鼠夹", StockItemGroup.Husbandry),
            ["neuroactive_perfume"] = ("“同步”神经活性香水", StockItemGroup.LuxuryItem),
            ["nightmare_cap"] = ("梦魇帽菇", StockItemGroup.ChemicalSupply),
            ["nudka"] = ("努特加", StockItemGroup.Alcohol),
            ["nutrifruit"] = ("营养果", StockItemGroup.Food),
            ["nutrifruit_seed_packet"] = ("一包营养果种子", StockItemGroup.Seed),
            ["pack_condom"] = ("一盒瑞克斯避孕套", StockItemGroup.HouseholdGood),
            ["packedFoodTable"] = ("加工食品（随机）", StockItemGroup.Food),
            ["paper_towel"] = ("一卷纸巾", StockItemGroup.HouseholdGood),
            ["pheromone_perfume"] = ("“本源”费洛蒙香水", StockItemGroup.LuxuryItem),
            ["pink_injector"] = ("奥克西莫注射器", StockItemGroup.Injector),
            ["pipe_weapon"] = ("铅管", StockItemGroup.MeleeWeapon),
            ["pistol_switch"] = ("手枪自动阻铁", StockItemGroup.GunMod),
            ["plant_dna_disk"] = ("DNA数据盘", StockItemGroup.DnaDataDisk),
            ["plant_dna_manipulator"] = ("DNA编辑器", StockItemGroup.Machine),
            ["portable_water_purifier"] = ("海德拉科技微型净水器", StockItemGroup.WaterPurifier),
            ["portable_water_purifier_used"] = ("海德拉科技微型净水器（二手）", StockItemGroup.WaterPurifier),
            ["powerblock"] = ("能量阵列", StockItemGroup.Machine),
            ["printer_chip_10_mag"] = ("3D打印机芯片（x2 10毫米弹匣）", StockItemGroup.PrinterMagazine10Chip),
            ["printer_chip_10_mag_ext"] = ("3D打印机芯片（x1 加长10毫米弹匣）", StockItemGroup.PrinterMagazine10Chip),
            ["printer_chip_10_mag_high"] = ("3D打印机芯片（x1 大容量10毫米弹匣）", StockItemGroup.PrinterMagazine10Chip),
            ["printer_chip_22_mag"] = ("3D打印机芯片（x2 .22弹匣）", StockItemGroup.PrinterMagazine22Chip),
            ["printer_chip_22_mag_ext"] = ("3D打印机芯片（x2 加长.22弹匣）", StockItemGroup.PrinterMagazine22Chip),
            ["printer_chip_22_mag_high"] = ("3D打印机芯片（x2 大容量.22弹匣）", StockItemGroup.PrinterMagazine22Chip),
            ["printer_chip_combat_knife"] = ("3D打印机芯片（战斗匕首×5）", StockItemGroup.PrinterMeleeChip),
            ["printer_chip_combat_machete"] = ("3D打印机芯片（战斗砍刀×3）", StockItemGroup.PrinterMeleeChip),
            ["printer_chip_gun_part"] = ("3D打印机芯片（枪械零件）", StockItemGroup.PrinterGunChip),
            ["printer_chip_handmade_pistol_frame"] = ("3D打印机芯片（“巢火”紧凑型手枪框架）", StockItemGroup.PrinterGunChip),
            ["printer_chip_hatchet"] = ("3D打印机芯片（短柄斧×4）", StockItemGroup.PrinterMeleeChip),
            ["printer_chip_heavy_handmade_pistol_frame"] = ("3D打印机芯片（“铁铸10”手枪框架）", StockItemGroup.PrinterGunChip),
            ["printer_chip_kitchen_cleaver"] = ("3D打印机芯片（切肉刀×4）", StockItemGroup.PrinterMeleeChip),
            ["printer_chip_laser_sight"] = ("3D打印机芯片（x2 激光瞄准器）", StockItemGroup.PrinterGunModChip),
            ["printer_chip_pistol_double"] = ("3D打印机芯片（.22手枪×2）", StockItemGroup.PrinterGunChip),
            ["printer_chip_rds"] = ("3D打印机芯片（x2 红点瞄准镜）", StockItemGroup.PrinterGunModChip),
            ["printer_chip_silencer"] = ("3D打印机芯片（x2 消音器）", StockItemGroup.PrinterGunModChip),
            ["printer_module_cnc"] = ("计算机数控模组", StockItemGroup.PrinterModule),
            ["printer_module_melee_batch"] = ("近战武器批量打印模组", StockItemGroup.PrinterModule),
            ["printer_module_metal"] = ("金属打印模组", StockItemGroup.PrinterModule),
            ["printer_plastic"] = ("3D打印耗材", StockItemGroup.Material),
            ["processed_cheese"] = ("合成奶酪", StockItemGroup.Food),
            ["processed_juice"] = ("代糖果汁", StockItemGroup.Food),
            ["processed_meat"] = ("“泥炭”合成肉", StockItemGroup.Food),
            ["processed_milk"] = ("碳酸代乳", StockItemGroup.Food),
            ["pure_white_injector"] = ("纳洛通注射器", StockItemGroup.Injector),
            ["random_cigarette"] = ("一包海明威香烟（真伪随机）", StockItemGroup.Tobacco),
            ["random_eco_module"] = ("环保模组（形状和效果随机）", StockItemGroup.EcoModule),
            ["random_efficiency_module"] = ("效率模组（形状和效果随机）", StockItemGroup.EfficiencyModule),
            ["random_fineness_module"] = ("精炼模组（形状和效果随机）", StockItemGroup.FinenessModule),
            ["random_inspectable_injector"] = ("免疫宁注射器（实际状态随机）", StockItemGroup.Injector),
            ["random_node"] = ("节点（中）（效果随机）", StockItemGroup.Node),
            ["random_overclock_module"] = ("超频模组（形状和效果随机）", StockItemGroup.OverclockModule),
            ["random_performance_module"] = ("性能模组（形状和效果随机）", StockItemGroup.PerformanceModule),
            ["random_quality_module"] = ("质量模组（形状和效果随机）", StockItemGroup.QualityModule),
            ["random_stamp"] = ("食品券（面额和真伪随机）", StockItemGroup.FoodStamp),
            ["random_injector"] = ("免疫宁注射器（真伪随机）", StockItemGroup.Injector),
            ["ras"] = ("循环水养殖系统", StockItemGroup.Machine),
            ["rat_growth"] = ("老鼠（生长速度强化）", StockItemGroup.Husbandry),
            ["rat_litter"] = ("老鼠（预期产仔数提升）", StockItemGroup.Husbandry),
            ["raw_meat"] = ("生肉（标准）", StockItemGroup.Food),
            ["rds"] = ("红点瞄准镜", StockItemGroup.GunRedDotSight),
            ["rds_makeshift"] = ("简易红点瞄准镜", StockItemGroup.GunRedDotSight),
            ["real_expired_injector"] = ("免疫宁注射器（已过期）", StockItemGroup.Injector),
            ["recharger"] = ("充能器", StockItemGroup.Machine),
            ["restricted_chemical"] = ("化学用品（管制）", StockItemGroup.ChemicalSupply),
            ["revolver"] = ("兰斯维尔左轮手枪", StockItemGroup.Firearm),
            ["rubbing_alcohol"] = ("医用酒精", StockItemGroup.Medical),
            ["salve"] = ("南丁格尔软膏", StockItemGroup.Medical),
            ["scrap_metal"] = ("废金属", StockItemGroup.Material),
            ["set_stun_weapons"] = ("电击武器组合", StockItemGroup.StunWeapon),
            ["set_water_equipment"] = ("净水设备组合", StockItemGroup.WaterPurifier),
            ["set_mixed_modules"] = ("性能、效率和质量模组组合", StockItemGroup.MachineModule),
            ["set_efficiency_modules"] = ("效率模组组合", StockItemGroup.EfficiencyModule),
            ["set_quality_modules"] = ("质量模组组合", StockItemGroup.QualityModule),
            ["set_performance_modules"] = ("性能模组组合", StockItemGroup.PerformanceModule),
            ["set_materials"] = ("材料组合", StockItemGroup.Material),
            ["set_household_goods"] = ("日用品组合", StockItemGroup.HouseholdGood),
            ["set_base_water"] = ("基础水货单", StockItemGroup.Water),
            ["set_ghost_water"] = ("幽灵水货单", StockItemGroup.Water),
            ["scratcher"] = ("刮刮乐彩票", StockItemGroup.Lottery),
            ["scratcher_stack"] = ("一叠刮刮乐彩票（5张）", StockItemGroup.Lottery),
            ["screwdriver"] = ("螺丝刀", StockItemGroup.Tool),
            ["sec_keycard"] = ("一次性钥匙卡（治安）", StockItemGroup.AccessCard),
            ["security_alarm"] = ("警报系统", StockItemGroup.Machine),
            ["ser_keycard"] = ("一次性钥匙卡（服务）", StockItemGroup.AccessCard),
            ["shampoo"] = ("一瓶四合一洗涤剂", StockItemGroup.HouseholdGood),
            ["short_stock"] = ("“小圆盾”枪托", StockItemGroup.GunStock),
            ["shotgun"] = ("“壁垒”双动式霰弹枪", StockItemGroup.Firearm),
            ["silencer"] = ("消音器", StockItemGroup.GunSilencer),
            ["silencer2"] = ("夜枭消音器", StockItemGroup.GunSilencer),
            ["silencer_makeshift"] = ("简易消音器", StockItemGroup.GunSilencer),
            ["skincare_cream"] = ("“永恒”焕活精华液", StockItemGroup.LuxuryItem),
            ["small_pistol_ammo"] = (".22 SR弹药", StockItemGroup.Ammunition22),
            ["small_pistol_ammo_nl"] = (".22 SR弹药（非致命）", StockItemGroup.Ammunition22),
            ["small_pistol_ammo_p"] = (".22 SR弹药（穿甲）", StockItemGroup.Ammunition22),
            ["small_raw_meat"] = ("生肉（小）", StockItemGroup.Food),
            ["smelling_salt"] = ("嗅盐", StockItemGroup.Medical),
            ["smg"] = ("“和平使者”重型冲锋枪", StockItemGroup.Firearm),
            ["smuggler_bay"] = ("走私者暗格", StockItemGroup.Storage),
            ["stock"] = ("“守护者”枪托", StockItemGroup.GunStock),
            ["storage_bay"] = ("储藏区", StockItemGroup.Storage),
            ["storage_bay_chem"] = ("化学师储藏区", StockItemGroup.Storage),
            ["storage_bay_expansion_kit"] = ("储藏区扩建套件", StockItemGroup.ExpansionKit),
            ["storage_coordinator"] = ("储物协调器", StockItemGroup.Machine),
            ["stun_gun"] = ("“脉冲卫士”电击枪", StockItemGroup.StunWeapon),
            ["surgery_tool"] = ("外科手术工具套装", StockItemGroup.Medical),
            ["system_capped_neural_core"] = ("神经核心模组（受限）", StockItemGroup.UniversalModule),
            ["system_module_corrupt"] = ("损坏模组", StockItemGroup.DamagedModule),
            ["system_module_eco"] = ("环保模组", StockItemGroup.EcoModule),
            ["system_module_efficiancy"] = ("效率模组", StockItemGroup.EfficiencyModule),
            ["system_module_fineness"] = ("精炼模组", StockItemGroup.FinenessModule),
            ["system_module_overclock"] = ("超频模组", StockItemGroup.OverclockModule),
            ["system_module_performance"] = ("性能模组", StockItemGroup.PerformanceModule),
            ["system_module_quality"] = ("质量模组", StockItemGroup.QualityModule),
            ["system_module_ruined"] = ("报废模组", StockItemGroup.DamagedModule),
            ["tazer"] = ("电击枪", StockItemGroup.StunWeapon),
            ["toilet_paper"] = ("一卷厕纸", StockItemGroup.HouseholdGood),
            ["tools_trade"] = ("剪线钳与螺丝刀（同一套）", StockItemGroup.Tool),
            ["toothpaste"] = ("一支牙膏", StockItemGroup.HouseholdGood),
            ["topical_bandage_item"] = ("外用药膏绷带", StockItemGroup.Bandage),
            ["trigger_group"] = ("精调扳机组", StockItemGroup.GunMod),
            ["turbo_booster_adv"] = ("高级涡轮增压器", StockItemGroup.Tool),
            ["uv_filter"] = ("紫外线灯", StockItemGroup.WaterPurificationSupply),
            ["water_filter"] = ("滤水器", StockItemGroup.WaterFilter),
            ["water_filter_adv"] = ("高级滤水器", StockItemGroup.WaterFilter),
            ["water_jug"] = ("桶装水", StockItemGroup.Water),
            ["water_pitcher"] = ("水罐", StockItemGroup.WaterStorage),
            ["water_purifier"] = ("净水器", StockItemGroup.WaterPurifier),
            ["water_ration"] = ("密封水配给包（标准）", StockItemGroup.Water),
            ["water_ration_small"] = ("密封水配给包（小）", StockItemGroup.Water),
            ["water_tablet_container"] = ("一瓶净水药片", StockItemGroup.WaterPurificationSupply),
            ["water_tank"] = ("储水罐", StockItemGroup.WaterStorage),
            ["water_test_container"] = ("水质检测试剂盒", StockItemGroup.WaterTesting),
            ["welder"] = ("等离子切割机", StockItemGroup.Tool),
            ["wine_berry"] = ("荧光莓", StockItemGroup.Food),
            ["wine_bottle"] = ("酒瓶", StockItemGroup.BrewingSupply),
            ["wine_rack"] = ("催陈器", StockItemGroup.Machine),
            ["wine_superyeast"] = ("超级酵母", StockItemGroup.BrewingSupply),
            ["wine_yeast"] = ("酵母", StockItemGroup.BrewingSupply),
            ["wine_yeast_infinite"] = ("酵母生物反应器", StockItemGroup.BrewingSupply),
            ["wine_yeast_red"] = ("合成酵母", StockItemGroup.BrewingSupply),
            ["wire"] = ("线缆", StockItemGroup.Material),
            ["wire_cutter"] = ("剪线钳", StockItemGroup.Tool),
            ["zerochew"] = ("一板零嚼", StockItemGroup.Food),
        };

    internal static readonly IReadOnlyDictionary<string, string> DrawIds = new Dictionary<string, string>
    {
        ["AddScavGeneralStock"] =
            "packedFoodTable packedFoodTable packedFoodTable packedFoodTable processed_cheese kitchen_knife combat_knife " +
            "box_cutter kitchen_cleaver pipe_weapon hatchet screwdriver welder crowbar wire_cutter printer_plastic " +
            "printer_plastic printer_plastic water_ration_small water_ration_small kotton_fabric pink_injector flux_agent " +
            "common_electronic wire wire toilet_paper toothpaste shampoo birth_control_pill birth_control_pill " +
            "birth_control_pill random_cigarette random_cigarette random_inspectable_injector box_tampon " +
            "water_test_container water_tablet_container portable_water_purifier_used kotton_seed " +
            "kotton_seed nutrifruit_seed_packet bloomberry_seed energy_credit water_filter water_filter water_pitcher " +
            "nudka common_medical common_chemical common_chemical scratcher system_module_corrupt " +
            "random_performance_module random_efficiency_module random_quality_module system_module_ruined " +
            "system_module_corrupt module_extractor pure_white_injector rubbing_alcohol smelling_salt " +
            "hemostatic_bandage_item surgery_tool wine_bottle wine_bottle wine_yeast wine_yeast",
        ["CreateThief"] =
            "bottled_water_premium skincare_cream neuroactive_perfume pheromone_perfume turbo_booster_adv cmd_keycard " +
            "scratcher_stack sec_keycard med_keycard eng_keycard ser_keycard random_performance_module " +
            "random_efficiency_module random_quality_module random_overclock_module random_eco_module " +
            "random_fineness_module portable_water_purifier water_filter_adv genuine_genuine_injector crate_random " +
            "crypto_module_sec crypto_module_cmd crypto_module_med crypto_module_eng crypto_module_ser",
        ["CreatePettyThief"] =
            "packedFoodTable packedFoodTable packedFoodTable packedFoodTable householdTable householdTable householdTable " +
            "householdTable medicalTable medicalTable wine_yeast_red wine_yeast_red wine_yeast_red",
        ["CreateFoodThief"] =
            "galaxy_blend galaxy_blend galaxy_blend processed_milk processed_milk processed_milk processed_milk " +
            "processed_meat processed_meat processed_meat processed_meat nutrifruit nutrifruit nutrifruit nutrifruit " +
            "nutrifruit processed_juice processed_juice processed_juice processed_juice",
        ["AddScavFoodStock"] =
            "packedFoodTable packedFoodTable packedFoodTable packedFoodTable zerochew zerochew processed_cheese " +
            "processed_cheese cat_bar cat_bar li_eat_snackbar li_eat_snackbar nutrifruit nutrifruit nutrifruit " +
            "processed_cheese",
        ["AddScavHouseholdStock"] =
            "toilet_paper toilet_paper toothpaste toothpaste shampoo shampoo box_tampon water_test_container " +
            "water_tablet_container water_filter water_pitcher wine_yeast wine_yeast",
        ["AddScavMaterialStock"] =
            "screwdriver welder wire_cutter printer_plastic printer_plastic printer_plastic flux_agent flux_agent " +
            "common_electronic wire wire box_cutter kotton_fabric scrap_metal scrap_metal scrap_metal scrap_metal",
        ["AddScavMedStock"] =
            "birth_control_pill birth_control_pill birth_control_pill pink_injector pure_white_injector rubbing_alcohol " +
            "smelling_salt hemostatic_bandage_item hemostatic_bandage_item surgery_tool surgery_tool common_medical " +
            "random_inspectable_injector",
        ["AddThiefFoodStock"] =
            "nudka nudka galaxy_blend galaxy_blend galaxy_blend galaxy_blend raw_meat small_raw_meat processed_cheese",
        ["AddThiefHouseholdStock"] =
            "box_tampon box_tampon box_tampon pack_condom pack_condom aquascan kitchen_cleaver wine_superyeast " +
            "wine_superyeast wine_yeast_red wine_yeast_red",
        ["AddThiefMaterialStock"] =
            "common_electronic common_electronic common_electronic common_electronic gun_part gun_part",
        ["AddThiefMedStock"] =
            "blood_bag blood_bag rubbing_alcohol rubbing_alcohol pink_injector pink_injector common_medical common_medical " +
            "surgery_tool surgery_tool salve salve",
        ["CreateJunker"] =
            "scrap_metal common_electronic wire printer_plastic wine_bottle wine_bottle wine_bottle",
        ["CreateScrapper"] =
            "broken_furnace broken_moisture_farm",
        ["CreateFarmerClientStandard"] =
            "wine_berry nutrifruit nutrifruit",
        ["CreateMiner"] =
            "common_ore common_ore common_ore common_ore common_ore",
        ["CreateStampSeller"] =
            "random_stamp random_stamp random_stamp",
        ["CreateScavCrate"] =
            "packedFoodTable",
        ["CreateWaterSeller"] =
            "large_bottled_water large_bottled_water large_bottled_water water_jug water_ration_small water_ration_small " +
            "water_ration_small water_ration_small water_ration_small",
        ["CreateShadyPharmacist"] =
            "pink_injector",
        ["CreateRareLowerLevelChemist"] =
            "dream_dust dream_dust pink_injector pink_injector black_injector fanta_pill fanta_pill",
        ["CreateGunsmith"] =
            "heavy_handmade_pistol heavy_handmade_pistol handmade_pistol handmade_pistol revolver small_pistol_ammo " +
            "small_pistol_ammo small_pistol_ammo small_pistol_ammo heavy_pistol_ammo heavy_pistol_ammo heavy_pistol_ammo " +
            "heavy_pistol_ammo",
        ["CreateLowerLevelRareMerchant"] =
            "advanced_flux_agent neuroactive_perfume module_extractor_advanced turbo_booster_adv",
        ["CreateSecSurplusMerchant"] =
            "smg smg shotgun shotgun stock short_stock gl bipod mag_10mm_smg mag_10mm_smg mag_10mm_smg_high " +
            "mag_10mm_smg_box glock_receiver glock_receiver heavy_pistol_ammo heavy_pistol_ammo heavy_pistol_ammo_p " +
            "heavy_pistol_ammo_p mag_10mm mag_10mm mag_10mm_high mag_10mm_ext grip laser2 silencer2 heavy_pistol_ammo " +
            "heavy_pistol_ammo heavy_pistol_ammo_nl heavy_pistol_ammo_nl rds laser compensator silencer stun_gun tazer " +
            "pink_injector",
        ["PlaceInventorInventory"] =
            "mirage_projector security_alarm storage_bay machine_bay recharger recharger smuggler_bay smuggler_bay furnace " +
            "fridge desequencer crypto_module_sec crypto_module_cmd crypto_module_med crypto_module_eng crypto_module_ser " +
            "desequencer+blank_keycard " +
            "crypto_module_sec crypto_module_cmd crypto_module_med crypto_module_eng crypto_module_ser powerblock " +
            "alarm_module_transmitter furnace_module_blast furnace_module_junk random_performance_module " +
            "random_efficiency_module random_quality_module random_overclock_module random_eco_module " +
            "random_fineness_module random_node energy_credit_breeder energy_credit metal_scanner flashlight " +
            "module_bay_expansion_kit module_bay_expansion_kit module_bay_expansion_kit storage_bay_expansion_kit " +
            "storage_coordinator module_extractor_advanced water_filter_adv portable_water_purifier_used uv_filter " +
            "uv_filter water_purifier water_tank water_tank water_tank moisture_farm hydroponic hydroponic " +
            "farm_module_nutrifruit farm_module_dream_cap farm_module_kotton_loom farm_module_nightmare_cap " +
            "farm_module_fillerweed_production farm_module_hydroreed_filter farm_module_smuggler chem_module chem_module " +
            "chem_module chem_module plant_dna_manipulator plant_dna_disk plant_dna_disk dna_manipulator_module_stabilizer " +
            "dna_manipulator_module_positive_filter dna_manipulator_module_negative_filter " +
            "dna_manipulator_module_precision wine_rack wine_yeast_infinite ras ras livestock_feed_dispenser " +
            "livestock_feed_dispenser cassette_d aug_scanner mouse_trap black_lamp module_bay_expansion_kit " +
            "module_bay_expansion_kit",
        ["PlaceSupplierInventory"] =
            "cassette_l cassette",
        ["CreateAppraisalRandom"] =
            "appraisal_random",
        ["CreateScavBarter"] =
            "wire scrap_metal scrap_metal paper_towel shampoo toilet_paper paper_towel shampoo toilet_paper",
        ["CreateAddictBarter"] =
            "c4 box_tampon combat_knife",
        ["CreateAddictGeneralBarter"] =
            "birth_control_pill packedFoodTable energy_credit tools_trade box_tampon c4 kotton_fabric " +
            "system_module_efficiancy system_module_performance system_module_quality system_module_overclock " +
            "system_module_eco system_module_fineness real_expired_injector genuine_genuine_injector " +
            "system_capped_neural_core advanced_flux_agent energy_credit_breeder common_medical common_chemical " +
            "combat_knife",
        ["CreateShadyFarmer"] =
            "dream_cap dream_cap coffee_bean coffee_bean kotton_fabric nightmare_cap",
        ["CreateIceMiner"] =
            "bottled_water_high",
        ["CreateScavengerHouseholdClient"] =
            "toothpaste toothpaste toothpaste shampoo shampoo shampoo toilet_paper toilet_paper",
        ["CreateFarmerSeedClient"] =
            "nutrifruit nutrifruit",
        ["CreateLLGunSeller"] =
            "mag_22 mag_22_high mag_22_ext laser laser_toy rds rds_makeshift silencer silencer_makeshift pistol_switch " +
            "internal_springs internal_springs_improved trigger_group small_pistol_ammo_nl small_pistol_ammo_nl " +
            "small_pistol_ammo_nl small_pistol_ammo_nl small_pistol_ammo small_pistol_ammo small_pistol_ammo " +
            "small_pistol_ammo small_pistol_ammo_p small_pistol_ammo_p small_pistol_ammo_p small_pistol_ammo_p",
        ["CreateMercGunSeller"] =
            "mag_10mm mag_10mm_high mag_10mm_ext laser rds silencer internal_springs internal_springs_improved " +
            "trigger_group match_barrel pistol_switch ammo_display grip_gel heavy_pistol_ammo_nl heavy_pistol_ammo_nl " +
            "heavy_pistol_ammo_nl heavy_pistol_ammo_nl heavy_pistol_ammo heavy_pistol_ammo heavy_pistol_ammo " +
            "heavy_pistol_ammo heavy_pistol_ammo_p heavy_pistol_ammo_p heavy_pistol_ammo_p heavy_pistol_ammo_p",
        ["CreateULGunSeller"] =
            "mag_10mm mag_10mm_high mag_10mm_ext mag_10mm_smg mag_10mm_smg_high laser laser2 rds silencer silencer2 " +
            "compensator internal_springs internal_springs_improved trigger_group match_barrel pistol_switch ammo_display " +
            "grip_gel heavy_pistol_ammo heavy_pistol_ammo heavy_pistol_ammo heavy_pistol_ammo heavy_pistol_ammo_p " +
            "heavy_pistol_ammo_p heavy_pistol_ammo_p heavy_pistol_ammo_p",
        ["CreateRevRaider"] =
            "energy_credit_dis energy_credit_breeder turbo_booster_adv advanced_flux_agent module_extractor_advanced " +
            "module_bay_expansion_kit random_performance_module random_quality_module random_efficiency_module " +
            "random_overclock_module random_eco_module random_fineness_module blue_blood_bag common_electronic " +
            "common_electronic printer_plastic printer_plastic glock_receiver smg small_raw_meat small_raw_meat " +
            "small_raw_meat common_medical common_medical toilet_paper toothpaste shampoo birth_control_pill " +
            "birth_control_pill birth_control_pill common_chemical restricted_chemical module_extractor",
        ["CreateRetiredGunsmith"] =
            "printer_chip_rds printer_chip_laser_sight printer_chip_silencer printer_chip_hatchet " +
            "printer_chip_kitchen_cleaver printer_chip_pistol_double printer_chip_rds printer_chip_laser_sight " +
            "printer_chip_silencer printer_chip_hatchet printer_chip_kitchen_cleaver printer_module_metal " +
            "printer_module_melee_batch printer_module_cnc printer_chip_combat_knife printer_chip_combat_machete " +
            "printer_chip_handmade_pistol_frame printer_chip_heavy_handmade_pistol_frame printer_chip_gun_part " +
            "printer_chip_22_mag printer_chip_22_mag_ext printer_chip_22_mag_high printer_chip_10_mag " +
            "printer_chip_10_mag_ext printer_chip_10_mag_high silencer2 laser2 compensator mag_10mm mag_22_high " +
            "mag_10mm_high mag_10mm_ext internal_springs internal_springs_improved trigger_group match_barrel " +
            "pistol_switch ammo_display grip_gel",
        ["CreateRetiredFarmer"] =
            "farm_module_nutrifruit farm_module_dream_cap farm_module_kotton_loom farm_module_nightmare_cap " +
            "farm_module_fillerweed_production farm_module_hydroreed_filter farm_module_smuggler",
        ["CreateRetiredAquafarmer"] =
            "ras livestock_feed_dispenser auto_butcher marking_pen kitchen_cleaver",
        ["CreateRetiredRancher"] =
            "rat_growth rat_growth rat_litter rat_litter",
        ["CreateRetiredWaterMerchant"] =
            "water_test_container water_tablet_container water_filter water_filter_adv",
        ["CreateRetiredWinemaker"] =
            "wine_yeast_infinite",
        ["CreateRetiredJunker"] =
            "flux_agent flux_agent flux_agent",
        ["CreateRetiredChemist"] =
            "storage_bay_chem evaporator_module_solvent_recovery restricted_chemical chem_scanner 1ml_dropper 5ml_dropper " +
            "25ml_cylinder 50ml_cylinder 100ml_cylinder module_extractor water_filter water_filter kotton_fabric " +
            "water_pitcher water_filter chem_module chem_module chem_module",
        ["CreateFoodSurplusClient/large"] =
            "packedFoodTable packedFoodTable",
        ["CreateFoodSurplusClient/regular"] =
            "packedFoodTable",
        ["CreateMedicalSurplusClient/large"] =
            "bandage_item topical_bandage_item pink_injector caffeine_pill birth_control_pill salve bandage_item " +
            "topical_bandage_item pink_injector caffeine_pill birth_control_pill salve",
        ["CreateMedicalSurplusClient/regular"] =
            "bandage_item topical_bandage_item pink_injector caffeine_pill birth_control_pill salve",
        ["CreateSubstanceSurplusClient/large"] =
            "fanta_pill dream_dust pink_injector fanta_pill dream_dust pink_injector fanta_pill dream_dust pink_injector",
        ["CreateSubstanceSurplusClient/regular"] =
            "fanta_pill dream_dust pink_injector fanta_pill dream_dust pink_injector",
        ["CreateWeaponArmorSurplusClient/large"] =
            "combat_knife box_cutter hatchet combat_knife box_cutter hatchet armor_lining armor_lining armor_lining",
        ["CreateWeaponArmorSurplusClient/regular"] =
            "combat_knife box_cutter hatchet combat_knife box_cutter hatchet armor_lining armor_lining",
        ["CreateMaterialSurplusClient/large"] =
            "common_ore common_ore common_ore scrap_metal scrap_metal scrap_metal wire wire wire common_electronic " +
            "common_electronic",
        ["CreateMaterialSurplusClient/regular"] =
            "common_ore common_ore scrap_metal scrap_metal wire wire common_electronic",
        ["CreateWaterCompany"] =
            "bottled_water_random bottled_water_ghost_as_base large_bottled_water_base large_bottled_water_ghost_as_base " +
            "bottled_water_random",
        ["CreateWaterSellScav"] =
            "bottled_water_random",
        ["CreateDishonestWaterSellScav"] =
            "bottled_water_base bottled_water_ghost_as_base bottled_water_ghost_as_base bottled_water_ghost_as_pure " +
            "bottled_water_base",
        ["SetHonestWaterSellerDialog/base"] =
            "bottled_water_base water_ration",
        ["SetHonestWaterSellerDialog/ghost"] =
            "bottled_water_ghost",
        ["CreateWanted1Return"] =
            "glock_receiver sec_keycard crate_evidence_security stun_gun heavy_pistol_ammo",
        ["RollButcherCount"] =
            "raw_meat+small_raw_meat+fat_meat",
        ["CreateWanted7"] =
            "processed_meat processed_meat processed_meat processed_meat processed_milk processed_milk processed_milk " +
            "processed_milk processed_juice processed_juice processed_juice processed_juice",
    };
}
