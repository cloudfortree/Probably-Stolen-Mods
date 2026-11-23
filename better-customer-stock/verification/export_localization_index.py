"""Export the mod's display terms against the local game's extracted string tables."""

import argparse
import json
import re
from pathlib import Path

from localized_names import add_bilingual_names, runtime_source


MOD = Path(__file__).resolve().parents[1]

ITEM_ALIASES = {
    "loot_crate_evidence": ("evidence_box",),
    "loot_crate_medical": ("med_box",),
    "loot_crate_engineering": ("eng_box",),
    "loot_crate_security": ("sec_box",),
    "loot_crate_service": ("service_box",),
    "broken_furnace": ("furnace",),
    "broken_moisture_farm": ("moisture_farm",),
    "full_water_test_strip_box": ("water_test_container", "water_test_strip"),
    "full_water_purification_tablet_box": ("water_tablet_container", "water_tablet"),
    "portable_water_purifier_used": ("portable_water_purifier",),
    "genuine_genuine_injector": ("large_purple_injector",),
    "random_injector": ("large_purple_injector",),
    "random_inspectable_injector": ("large_purple_injector",),
    "real_expired_injector": ("large_purple_injector",),
    "random_cigarette": ("packet_red_cigarette",),
    "random_stamp": ("food_stamp",),
    "random_performance_module": ("system_module_performance",),
    "random_efficiency_module": ("system_module_efficiancy",),
    "random_quality_module": ("system_module_quality",),
    "random_overclock_module": ("system_module_overclock",),
    "random_fineness_module": ("system_module_fineness",),
    "random_eco_module": ("system_module_eco",),
    "random_node": ("node_medium",),
    "rat_growth": ("rat",),
    "rat_litter": ("rat",),
    "appraisal_random": ("hand", "ribwich", "plasma_fuel"),
    "crate_random": ("evidence_box", "med_box", "eng_box", "sec_box", "service_box"),
    "crate_evidence_security": ("sec_box", "evidence_box"),
    "set_stun_weapons": ("stun_gun", "tazer"),
    "set_water_equipment": ("water_filter_adv", "portable_water_purifier"),
    "set_mixed_modules": ("system_module_performance", "system_module_efficiancy", "system_module_quality"),
    "set_efficiency_modules": ("system_module_efficiancy",),
    "set_quality_modules": ("system_module_quality",),
    "set_performance_modules": ("system_module_performance",),
    "set_materials": ("wire", "nuts_metal", "scrap_metal", "common_electronic"),
    "set_household_goods": ("paper_towel", "shampoo", "toilet_paper"),
    "tools_trade": ("wire_cutter", "screwdriver"),
}

ITEM_EXTRA_KEYS = {
    "appraisal_random": ("Mechanic/mech_appraisal_cert_qualified",),
    "crate_random": ("Mechanic/type_supply_crate",),
    "crate_evidence_security": ("Mechanic/type_supply_crate",),
    "broken_furnace": ("Mechanic/mech_item_ruined",),
    "broken_moisture_farm": ("Mechanic/mech_item_ruined",),
    "cassette": ("Item/item_cassette_prefix",),
    "cassette_d": ("Item/item_cassette_prefix", "Item/item_cassette_corrupted"),
    "cassette_l": ("Item/item_cassette_prefix", "Item/item_cassette_damaged"),
    "householdTable": ("Mechanic/type_household_good",),
    "medicalTable": ("Mechanic/type_medical_supplies",),
    "packedFoodTable": ("Mechanic/type_processed_food",),
    "set_materials": ("Mechanic/type_material",),
    "set_household_goods": ("Mechanic/type_household_good",),
    "set_base_water": ("Mechanic/mech_condition_basewater",),
    "set_ghost_water": ("Mechanic/mech_condition_ghostwater",),
}

ITEM_NAME_KEYS = {
    "hand": "Item/item_hand",
    "rat": "Item/husbandry_rat_name",
}

WATER_CONDITIONS = {
    "基础水": "Mechanic/mech_condition_basewater",
    "幽灵水": "Mechanic/mech_condition_ghostwater",
    "优质水": "Mechanic/mech_condition_high_quality_water",
    "纯水": "Mechanic/mech_condition_pure_water",
}

GROUP_KEYS = {
    "物资箱": ("Mechanic/type_supply_crate",),
    "背包": ("Item/item_expedition_box_label_backpack",),
    "加工食品货表": ("Mechanic/type_processed_food",),
    "日用品货表": ("Mechanic/type_household_good",),
    "医疗用品货表": ("Mechanic/type_medical_supplies",),
    "材料货表": ("Mechanic/type_material",),
    "枪械": ("Mechanic/type_firearm",),
    "葡萄酒酵母": ("Item/item_wine_yeast_name", "Item/item_wine_superyeast_name"),
}

# Some client display names share the same simplified-Chinese value.
CLIENT_KEYS = {
    "scavGeneral": "name_scavenger",
    "scavStamp": "name_scav",
    "scavCrate": "name_scavenger",
    "scavInspectable": "name_scavenger",
    "scavAppraisalRandom": "name_scavenger",
    "scavBarter": "name_scav",
    "scavengerHousehold": "name_scavenger",
    "waterSellerScav": "name_scav",
    "waterHishonestSellerScav": "name_scav",
    "honestBadWaterSeller": "name_scav",
    "ll_gun_seller": "name_lower_level_citizen",
    "waterCompany": "name_lower_levels_citizen",
    "wanted1_return": "name_lower_levels_citizen",
    "wanted2_return": "name_lower_levels_citizen",
}

DETAIL_KEYS = (
    "ClientName/faction_name_black_market", "ClientName/faction_name_security", "ClientName/faction_name_revolution",
    "ClientName/faction_name_lower_level",
    "Item/item_permit_gun_1_name", "Item/item_permit_gun_2_name", "Item/item_permit_gun_3_name",
    "Item/item_desequencer_name", "Item/item_blank_keycard_name", "Item/item_black_lamp_name",
    "Item/item_energy_credit_name", "Item/item_food_stamp_name", "Item/item_expedition_box_label_backpack", "Item/item_satchel_name",
    "Mechanic/mech_appraisal_rank_junior", "Mechanic/mech_appraisal_rank_appraiser",
    "Mechanic/mech_appraisal_rank_senior", "Mechanic/mech_appraisal_rank_master",
    "Mechanic/type_access_card", "Mechanic/type_chemical_supplies", "Mechanic/type_medical_supplies",
    "Mechanic/type_module", "Mechanic/type_processed_food", "Mechanic/type_supply_crate", "Mechanic/type_household_good",
    "NetworkUpgrade/network_GUN_PERMIT_I_title", "NetworkUpgrade/network_GUN_PERMIT_II_title",
    "NetworkUpgrade/network_GUN_PERMIT_III_title", "NetworkUpgrade/network_JACKSON1_title",
    "NetworkUpgrade/network_JACKSON2_title", "NetworkUpgrade/network_AQUACULTURE_title",
    "NetworkUpgrade/network_AQUACULTURE_II_title", "NetworkUpgrade/network_PLANT_DNA_title",
    "NetworkUpgrade/network_RETIRED_FARMER_title", "NetworkUpgrade/network_RETIRED_CHEMIST_title",
    "NetworkUpgrade/network_WATER_TRADER_title", "NetworkUpgrade/network_WINEMAKER_title",
    "MainMenu/option_apply", "UI/ui_modcfg_reset",
)

DETAIL_ENTITY_IDS = {
    "ClientName/faction_name_black_market": "FACTION_BLACK_MARKET",
    "ClientName/faction_name_security": "FACTION_SECURITY",
    "ClientName/faction_name_revolution": "FACTION_REVOLUTION",
    "Item/item_permit_gun_1_name": "permit_gun_1",
    "Item/item_permit_gun_2_name": "permit_gun_2",
    "Item/item_permit_gun_3_name": "permit_gun_3",
    "Mechanic/type_access_card": "ACCESS_CARD",
    "Mechanic/type_supply_crate": "SUPPLY_CRATE",
    "Mechanic/type_processed_food": "PROCESSED_FOOD",
    "Mechanic/type_household_good": "HOUSEHOLD_GOOD",
}


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def export(kit):
    data = kit / "data"
    localization = data / "localization"
    tables = {
        table: read_json(localization / f"{table}.json")
        for table in ("Item", "Mechanic", "ClientName", "NetworkUpgrade", "MainMenu", "UI")
    }
    strings = {
        f"{table}/{entry['key']}": entry
        for table, document in tables.items()
        for entry in document["entries"]
    }
    directory = {entry["id"]: entry for entry in read_json(data / "item-catalog.json") if entry["id"]}
    used = set()

    def record(label, references, **fields):
        references = list(dict.fromkeys(references))
        for key in references:
            if key not in strings:
                raise ValueError(f"{label}: native string key {key!r} was not found")
        used.update(references)
        return {"简中": label, **fields, "词条": references}

    def item_references(item):
        direct = ITEM_NAME_KEYS.get(item, f"Item/item_{item}_name")
        if direct in strings:
            return [direct]
        return [f"Item/{ref['key']}" for ref in directory.get(item, {}).get("localizedReferences", [])
                if ref["table"] == "Item" and ref["key"].endswith("_name")]

    catalog = (MOD / "StockCatalog.cs").read_text(encoding="utf-8-sig")
    merchandise = {}
    for item, label in re.findall(r'\["([^"]+)"\] = \("([^"]+)", StockItemGroup\.', catalog):
        entities = ITEM_ALIASES.get(item)
        water_variant = item.startswith(("bottled_water_", "large_bottled_water_")) and not item_references(item)
        if water_variant:
            entities = ("large_bottled_water" if item.startswith("large_") else "bottled_water",)
        refs = list(ITEM_EXTRA_KEYS.get(item, ()))
        if entities is None:
            entities = (item,) if item_references(item) else ()
        for entity in entities:
            keys = item_references(entity)
            if not keys:
                raise ValueError(f"{item}: no localized name for {entity!r}")
            refs.extend(keys)
        if water_variant:
            refs.extend(key for name, key in WATER_CONDITIONS.items() if name in label)
        if not refs:
            # Some prefab IDs differ from the Item key even when the Chinese name is identical.
            refs = [key for key, entry in strings.items() if key.startswith("Item/")
                    and (key.endswith("_name") or key == "Item/item_cassette_prefix")
                    and entry["values"].get("zh") == label]
        if not refs:
            raise ValueError(f"{item}: missing localization mapping for {label!r}")
        arguments = []
        for key in refs:
            template = strings[key]["values"].get("zh", "")
            if "{0}" not in template:
                continue
            match = re.fullmatch(re.escape(template).replace(re.escape("{0}"), r"(\d+)"), label)
            if match:
                arguments.append({"词条": key, "参数": [int(match[1])]})
        fields = {"来源物品ID": list(entities)} if entities else {}
        if arguments:
            fields["格式参数"] = arguments
        merchandise[item] = record(label, refs, **fields)

    rules = (MOD / "StockRules.cs").read_text(encoding="utf-8-sig")
    clients = {}
    for profile, label in re.findall(
        r'(?:nameof\(\w+\.\w+\),|new StockProfile\()\s*"([^"]+)",\s*"([^"\n]*[\u4e00-\u9fff][^"\n]*)"',
        rules,
    ):
        base_name = label.split("（", 1)[0]
        key = CLIENT_KEYS.get(profile)
        if profile.startswith("scavGeneric"):
            key = "name_scavenger"
        refs = [f"ClientName/{key}"] if key else [
            name for name, entry in strings.items()
            if name.startswith("ClientName/") and entry["values"].get("zh") == base_name
        ]
        if len(refs) != 1:
            raise ValueError(f"{profile}: expected a unique native client name for {label!r}, got {refs}")
        refs.extend(key for key, entry in strings.items()
                    if key.startswith(("Item/item_sign_", "NetworkUpgrade/network_JACKSON", "NetworkUpgrade/network_AQUACULTURE"))
                    and entry["values"].get("zh") and label.endswith(entry["values"]["zh"] + "）"))
        for key in ("ClientName/name_eleanor", "ClientName/name_scavenger", "ClientName/name_james_bailey",
                    "ClientName/name_juan", "Item/item_food_stamp_name"):
            if key == "ClientName/name_scavenger" and profile != "eleanorScav":
                continue
            if strings[key]["values"]["zh"] in label:
                refs.append(key)
        clients[profile] = record(label, refs)

    groups = []
    for group, label, members in re.findall(
        r'(?:\bTable|new StockChoice)\("([^"]+)", "([^"]+)",\s*(?:StockChoiceKind\.\w+,\s*)?"([^"]+)"',
        rules,
    ):
        candidates = members.split()
        for candidate in candidates:
            if candidate not in merchandise:
                raise ValueError(f"{group}: unlisted candidate {candidate!r}")
        groups.append(record(label, GROUP_KEYS.get(label, ()), 分组ID=group, 候选项ID=candidates))

    item_names, client_keys = add_bilingual_names(merchandise, clients, groups, strings)
    for entry in (*merchandise.values(), *clients.values(), *groups):
        used.update(entry["词条"])
    details = [record(strings[key]["values"]["zh"], [key], 英文=strings[key]["values"]["en"],
                      **({"原版ID": DETAIL_ENTITY_IDS[key]} if key in DETAIL_ENTITY_IDS else {}))
               for key in DETAIL_KEYS]
    index = {
        "来源": "本地游戏提取的原版本地化表和供货目录",
        "模组界面语言": ["zh", "en"],
        "自拟文案回退语言": "en",
        "原版语言": sorted({entry["data"]["m_Identifier"]["m_Code"]
                          for entry in read_json(localization / "locales.json")}),
        "商品和抽选结果": merchandise,
        "顾客档案": clients,
        "货物分类": groups,
        "详情及原版界面术语": details,
        "模组设置术语": {
            key: {"简中": chinese, "英文": english}
            for key, chinese, english in (
                ("default.enabled", "启用模组效果", "Enable Stock Adjustments"),
                ("default.appearance.enabled", "启用出货概率调整", "Enable Stock Chance Adjustments"),
                ("default.quantity.enabled", "启用货物数量调整", "Enable Stock Quantity Adjustments"),
                ("default.appearance", "出货概率", "Stock Chance"),
                ("default.quantity", "货物数量", "Stock Quantity"),
                ("default.invert_customers", "反转顾客设置的启用状态", "Invert Customer Toggles"),
                ("default.invert_categories", "反转商品类别设置的启用状态", "Invert Goods Checkboxes"),
                ("default.linear_changes", "线性变化", "Linear Adjustments"),
                ("default.inline_stock_preview", "实时显示原版和修正概率或数量", "Show Live Stock Comparisons"),
                ("{顾客档案ID}.enabled", "启用或禁用顾客调整", "Enable or Disable Adjustments"),
                ("{顾客档案ID}.override", "使用独立设置", "Use Customer Overrides"),
                ("{顾客档案ID}.individual_categories", "用滑动条逐项设置商品类别", "Use Sliders for Individual Goods Settings"),
                ("{顾客档案ID}.selection_enabled", "启用货物分类调整", "Enable Category Adjustments"),
                ("customer_changes", "顾客修改总览", "Customer Changes"),
                ("customer_changes.effective", "已生效", "Effective"),
                ("customer_changes.pending", "待应用", "Pending Apply"),
                ("customer_changes.inactive", "已修改(未生效)", "Modified (Inactive)"),
                ("stock_details", "货物详情", "Stock Details"),
                ("stock_details.barter_generation", "生成明细", "Generation Details"),
                ("stock_details.fixed_offers", "固定报价", "Fixed Offers"),
                ("stock_details.optional_offers", "可选报价 · 分别独立抽选", "Optional Offers · Independent Rolls"),
            )
        },
        "本地化表GUID": {table: document["guid"] for table, document in tables.items()},
        "原版词条": {key: {"词条ID": str(strings[key]["id"]), "简中": strings[key]["values"]["zh"],
                          "英文": strings[key]["values"]["en"]}
                    for key in sorted(used)},
        "说明": [
            "界面跟随游戏语言。简中使用中文文案，其他语言使用英文文案，并读取所选语言中可用的原版术语。",
            "含多个词条的商品和顾客档案保留原版名称来源；随机、装满、来访路线等说明由模组文案组合。",
            "货物分类的分组ID和顾客档案ID来自模组。原版固定货表沿用游戏货表ID。",
            "词条为空的分组名是模组自拟文案，候选项可在商品和抽选结果中查对应词条。",
        ],
    }
    return index, runtime_source(strings, used, item_names, client_keys)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("kit", type=Path, help="directory of the local game's extracted data")
    parser.add_argument("--out", type=Path, default=MOD / "术语索引.json")
    parser.add_argument("--runtime-source", type=Path, default=MOD / "StockNativeTerms.Generated.cs")
    args = parser.parse_args()
    result, source = export(args.kit)
    args.out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    args.runtime_source.write_text(source, encoding="utf-8")
    print({name: len(result[name]) for name in ("商品和抽选结果", "顾客档案", "货物分类", "原版词条")})


if __name__ == "__main__":
    main()
