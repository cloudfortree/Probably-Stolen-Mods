"""Name recipes shared by the in-game glossary and the terminology index."""

from dataclasses import dataclass
import json


@dataclass(frozen=True)
class Name:
    pattern: str
    parts: tuple = ()
    native: bool = False
    trim: str = ""

    def keys(self):
        if self.native:
            yield self.pattern
        for part in self.parts:
            if isinstance(part, Name):
                yield from part.keys()

    def english(self, strings):
        pattern = strings[self.pattern]["values"]["en"] if self.native else self.pattern
        result = pattern.format(*(part.english(strings) if isinstance(part, Name) else part for part in self.parts))
        return result.rstrip(self.trim) if self.trim else result

    def csharp(self):
        arguments = ", ".join(part.csharp() if isinstance(part, Name) else literal(part) for part in self.parts)
        if self.native:
            result = "GameTerm(" + literal(self.pattern) + (", " + arguments if arguments else "") + ")"
        elif self.parts:
            result = f"Format({literal(self.pattern)}, {arguments})"
        else:
            result = literal(self.pattern)
        return result + ".TrimEnd(" + ", ".join(repr(char) for char in self.trim) + ")" if self.trim else result


def literal(value):
    return json.dumps(value, ensure_ascii=False)


def game(key, *args):
    return Name(key, args, native=True)


def item(key):
    return game("Item/item_" + key + "_name")


def mechanic(key):
    return game("Mechanic/" + key)


def descriptor(name, suffix):
    return Name("{0} ({1})", (name, suffix))


BOTTLE = item("bottled_water")
LARGE_BOTTLE = item("large_bottled_water")
BASEWATER = mechanic("mech_condition_basewater")
GHOSTWATER = mechanic("mech_condition_ghostwater")
PURE_WATER = mechanic("mech_condition_pure_water")
INJECTOR = item("large_purple_injector")
CASSETTE = Name("Item/item_cassette_prefix", native=True, trim=" -–—")
SUPPLY_CRATE = mechanic("type_supply_crate")

ITEM_VARIANTS = {
    "barter_offer_count": Name("Optional Offer Count"),
    "appraisal_random": Name("Appraisal Item (One Random Type per Visit)"),
    "bottled_water_base": descriptor(BOTTLE, BASEWATER),
    "bottled_water_ghost": descriptor(BOTTLE, GHOSTWATER),
    "bottled_water_ghost_as_base": descriptor(BOTTLE, Name("{0} Disguised as {1}", (GHOSTWATER, BASEWATER))),
    "bottled_water_ghost_as_pure": descriptor(BOTTLE, Name("{0} Disguised as {1}", (GHOSTWATER, PURE_WATER))),
    "bottled_water_high": descriptor(BOTTLE, mechanic("mech_condition_high_quality_water")),
    "bottled_water_random": descriptor(BOTTLE, "Random Water Quality"),
    "broken_furnace": descriptor(item("furnace"), mechanic("mech_item_ruined")),
    "broken_moisture_farm": descriptor(item("moisture_farm"), mechanic("mech_item_ruined")),
    "cassette": descriptor(CASSETTE, "Intact"),
    "cassette_d": Name("{0}{1}", (CASSETTE, game("Item/item_cassette_corrupted"))),
    "cassette_l": Name("{0}{1}", (CASSETTE, game("Item/item_cassette_damaged"))),
    "crate_evidence_security": Name("{0} or {1} (Random)", (item("sec_box"), item("evidence_box"))),
    "crate_random": Name("Any Random {0}", (SUPPLY_CRATE,)),
    "full_water_purification_tablet_box": descriptor(item("water_tablet_container"), "Full"),
    "full_water_test_strip_box": descriptor(item("water_test_container"), "Full"),
    "genuine_genuine_injector": descriptor(INJECTOR, "Genuine"),
    "householdTable": descriptor(mechanic("type_household_good"), "Random"),
    "large_bottled_water_base": descriptor(LARGE_BOTTLE, BASEWATER),
    "large_bottled_water_ghost_as_base": descriptor(LARGE_BOTTLE, Name("{0} Disguised as {1}", (GHOSTWATER, BASEWATER))),
    "medicalTable": descriptor(mechanic("type_medical_supplies"), "Random"),
    "packedFoodTable": descriptor(mechanic("type_processed_food"), "Random"),
    "portable_water_purifier_used": descriptor(item("portable_water_purifier"), "Used"),
    "random_cigarette": descriptor(item("packet_red_cigarette"), "Random Authenticity"),
    "random_inspectable_injector": descriptor(INJECTOR, "Random Condition"),
    "random_node": descriptor(item("node_medium"), "Random Effect"),
    "random_stamp": descriptor(item("food_stamp"), "Random Value and Authenticity"),
    "random_injector": descriptor(INJECTOR, "Random Authenticity"),
    "rat_growth": descriptor(game("Item/husbandry_rat_name"), "Faster Growth"),
    "rat_litter": descriptor(game("Item/husbandry_rat_name"), "Higher Expected Litter Size"),
    "real_expired_injector": descriptor(INJECTOR, "Expired"),
    "set_stun_weapons": Name("Stun Weapon Bundle"),
    "set_water_equipment": Name("Water Purification Equipment Bundle"),
    "set_mixed_modules": Name("{0}, {1}, and {2} Bundle", (
        item("system_module_performance"), item("system_module_efficiancy"), item("system_module_quality"))),
    "set_efficiency_modules": Name("{0} Bundle", (item("system_module_efficiancy"),)),
    "set_quality_modules": Name("{0} Bundle", (item("system_module_quality"),)),
    "set_performance_modules": Name("{0} Bundle", (item("system_module_performance"),)),
    "set_materials": Name("{0} Bundle", (mechanic("type_material"),)),
    "set_household_goods": Name("{0} Bundle", (mechanic("type_household_good"),)),
    "set_base_water": Name("{0} Stock Set", (BASEWATER,)),
    "set_ghost_water": Name("{0} Stock Set", (GHOSTWATER,)),
    "tools_trade": Name("{0} and {1} (Same Bundle)", (item("wire_cutter"), item("screwdriver"))),
}
ITEM_VARIANTS.update({
    "random_" + kind + "_module": descriptor(item("system_module_" + base), "Random Shape and Effect")
    for kind, base in (
        ("eco", "eco"), ("efficiency", "efficiancy"), ("fineness", "fineness"),
        ("overclock", "overclock"), ("performance", "performance"), ("quality", "quality"),
    )
})

QUALIFIERS = {
    "买卖": game("UI/ui_nego_title_buy_sell"),
    "以物易物": game("UI/ui_nego_title_barter"),
    "供水商": Name("Water Supplier"),
    "出售货物": game("UI/ui_nego_title_selling"),
    "初访": Name("First Visit"),
    "医疗用品过剩": Name("Medical Supply Surplus"),
    "医疗霓虹灯牌": item("sign_medical"),
    "周一特供": game("NetworkUpgrade/network_JACKSON_SPECIAL_STOCK_title"),
    "售水": Name("Selling Water"),
    "如实说明水质": Name("Disclosed Water Quality"),
    "屠夫回访": Name("Butcher Follow-up"),
    "常规": Name("Regular"),
    "基础": Name("Base"),
    "待鉴定货物": Name("Goods for Inspection"),
    "成批食品": Name("Food in Bulk"),
    "拾荒客": game("ClientName/name_scavenger"),
    "日用品事件": Name("Household Goods Event"),
    "日用品霓虹灯牌": item("sign_household"),
    "材料供货": Name("Material Stock"),
    "材料过剩": Name("Material Surplus"),
    "材料霓虹灯牌": item("sign_material"),
    "杰克逊投资 I": game("NetworkUpgrade/network_JACKSON1_title"),
    "杰克逊投资 II": game("NetworkUpgrade/network_JACKSON2_title"),
    "枪械供货": Name("Firearm Stock"),
    "武器和护甲过剩": Name("Weapon and Armor Surplus"),
    "水产养殖": game("NetworkUpgrade/network_AQUACULTURE_title"),
    "水产养殖 II": game("NetworkUpgrade/network_AQUACULTURE_II_title"),
    "种子交易": Name("Seed Trade"),
    "胡安": game("ClientName/name_juan"),
    "药品过剩": Name("Drug Surplus"),
    "詹姆斯·贝利回访": Name("{0} Follow-up", (game("ClientName/name_james_bailey"),)),
    "鉴定服务": game("UI/ui_nego_title_appraisal"),
    "隐瞒水质": Name("Concealed Water Quality"),
    "食品券": item("food_stamp"),
    "食品霓虹灯牌": item("sign_food"),
    "食物过剩": Name("Food Surplus"),
}

GROUPS = {
    "可选易物货单": Name("Optional Barter Offers"),
    "以物易物货单": Name("Barter Stock Sets"),
    "加工食品货表": Name("{0} Table", (mechanic("type_processed_food"),)),
    "医疗用品货表": Name("{0} Table", (mechanic("type_medical_supplies"),)),
    "待鉴定物品": Name("Items for Appraisal"),
    "待鉴定货物": Name("Goods for Inspection"),
    "成鱼": Name("Adult Fish"),
    "日用品货表": Name("{0} Table", (mechanic("type_household_good"),)),
    "杂物货表": Name("Junk Table"),
    "材料货表": Name("{0} Table", (mechanic("type_material"),)),
    "枪械": mechanic("type_firearm"),
    "柜台无待售货物时补货": Name("Empty Counter Restock"),
    "注射器货单": Name("Injector Stock Sets"),
    "物资箱": SUPPLY_CRATE,
    "背包": game("Item/item_expedition_box_label_backpack"),
    "药品货单": Name("Drug Stock Sets"),
    "葡萄酒酵母": Name("Wine Yeast"),
    "货单": Name("Stock Sets"),
    "水质货单": Name("Water Stock Sets"),
    "货物": Name("Goods"),
}


def item_recipe(identifier, entry, strings):
    if identifier in ITEM_VARIANTS:
        return ITEM_VARIANTS[identifier]
    if entry.get("格式参数"):
        form, = entry["格式参数"]
        return game(form["词条"], *form["参数"])
    for key in entry["词条"]:
        if strings[key]["values"]["zh"] == entry["简中"]:
            return game(key)
    raise ValueError(f"{identifier}: no English name recipe for {entry['简中']}")


def profile_qualifiers(label):
    base, separator, suffix = label.partition("（")
    if not separator:
        return ()
    return tuple(suffix.removesuffix("）").split(" · "))


def add_bilingual_names(merchandise, clients, groups, strings):
    item_names = {key: item_recipe(key, entry, strings) for key, entry in merchandise.items()}
    client_keys = {}
    for key, recipe in item_names.items():
        entry = merchandise[key]
        entry["英文"] = recipe.english(strings)
        entry["词条"] = list(dict.fromkeys([*entry["词条"], *recipe.keys()]))

    for key, entry in clients.items():
        source = entry["词条"][0]
        client_keys[key] = source
        base = game(source).english(strings)
        parts = profile_qualifiers(entry["简中"])
        if parts:
            names = [QUALIFIERS[part] for part in parts]
            entry["英文"] = base + " (" + " · ".join(name.english(strings) for name in names) + ")"
            entry["类型说明"] = [
                {"简中": part, "英文": name.english(strings), "词条": list(name.keys())}
                for part, name in zip(parts, names)
            ]
            entry["词条"] = list(dict.fromkeys([*entry["词条"], *(key for name in names for key in name.keys())]))
        else:
            entry["英文"] = base

    for entry in groups:
        recipe = GROUPS[entry["简中"]]
        entry["英文"] = recipe.english(strings)
        entry["词条"] = list(dict.fromkeys([*entry["词条"], *recipe.keys()]))

    return item_names, client_keys


def runtime_source(strings, used, item_names, client_keys):
    lines = [
        "// Generated by verification/export_localization_index.py.",
        "using System;",
        "using System.Collections.Generic;",
        "",
        "namespace BetterCustomerStock;",
        "",
        "internal static partial class StockLanguage",
        "{",
    ]

    def dictionary(name, value_type, entries):
        lines.extend((f"    private static readonly Dictionary<string, {value_type}> {name} = new(StringComparer.Ordinal)",
                      "    {"))
        lines.extend(f"        [{literal(key)}] = {value}," for key, value in sorted(entries.items()))
        lines.extend(("    };", ""))

    keys = set(used)
    keys.update(key for recipe in (*item_names.values(), *QUALIFIERS.values(), *GROUPS.values()) for key in recipe.keys())
    dictionary("NativeTerms", "(string Chinese, string English)", {
        key: "(" + literal(strings[key]["values"]["zh"]) + ", " + literal(strings[key]["values"]["en"]) + ")"
        for key in keys
    })
    dictionary("ItemNames", "Func<string>", {key: "() => " + recipe.csharp() for key, recipe in item_names.items()})
    dictionary("ClientNameKeys", "string", {key: literal(value) for key, value in client_keys.items()})
    dictionary("CustomerQualifiers", "Func<string>", {
        key: f"() => Text({literal(key)}, {recipe.csharp()})" if not list(recipe.keys()) else
        "() => " + (f"Text({literal(key)}, {recipe.csharp()})" if strings[list(recipe.keys())[0]]["values"]["zh"] != key
                   else recipe.csharp())
        for key, recipe in QUALIFIERS.items()
    })
    dictionary("GroupNames", "Func<string>", {key: "() => " + recipe.csharp() for key, recipe in GROUPS.items()})
    return "\n".join((*lines[:-1], "}", ""))
