using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using static BetterCustomerStock.StockLanguage;
using UnityEngine;

namespace BetterCustomerStock;

internal sealed class StockMethod
{
    internal readonly MethodInfo? Method;
    internal readonly string MethodName;
    internal readonly string Draws;
    internal readonly string[][] Categories;
    internal readonly StockProfile[] Profiles;
    internal readonly bool BeforeVisit;
    internal readonly StockChoice[] Choices;
    internal readonly StockProfile? FixedProfile;
    internal readonly string? SupplyVariant;
    internal StockBaseDraw?[] BaseDraws { get; private set; }
    internal (int Fixed, int Optional)? BarterOfferCounts { get; private set; }
    private readonly string[]? profileDraws;

    internal StockMethod(MethodInfo? method, string methodName, string draws, StockProfile[] profiles,
        bool beforeVisit = false, string? catalogKey = null, string[]? profileDraws = null, StockChoice[]? choices = null,
        StockProfile? fixedProfile = null)
    {
        Method = method;
        MethodName = methodName;
        Draws = draws;
        Profiles = profiles;
        BeforeVisit = beforeVisit;
        Choices = choices ?? Array.Empty<StockChoice>();
        FixedProfile = fixedProfile;
        SupplyVariant = catalogKey?.Split('/').Last();
        BaseDraws = new StockBaseDraw?[draws.Length];
        this.profileDraws = profileDraws;
        if (profileDraws != null && (profileDraws.Length != profiles.Length || profileDraws.Any(profile => profile.Length != draws.Length)))
            throw new InvalidOperationException(Line("顾客版本与抽取规则不匹配。{0}", "Customer variants do not match the stock rule. {0}", methodName));

        string[] names = StockCatalog.DrawIds.TryGetValue(catalogKey ?? methodName, out string catalog)
            ? catalog.Split(' ') : Array.Empty<string>();
        if (names.Length != draws.Count(draw => AxisFor(draw) != StockAxis.None))
            throw new InvalidOperationException(Line("供货类别与抽取规则不匹配。{0}", "Stock categories do not match the rule. {0}", catalogKey ?? methodName));

        Categories = new string[draws.Length][];
        int next = 0;
        for (int index = 0; index < draws.Length; index++)
            Categories[index] = AxisFor(draws[index]) == StockAxis.None ? Array.Empty<string>() : names[next++].Split('+');
    }

    internal StockAxis AxisFor(StockProfile profile, int site)
        => AxisFor(profileDraws == null ? Draws[site] : profileDraws[Array.IndexOf(Profiles, profile)][site]);

    internal StockChoice? ChoiceFor(int site)
        => Choices.FirstOrDefault(choice => choice.IndexOfSite(site) >= 0);

    internal StockChoice? ChoiceForTable(string? id)
        => Choices.FirstOrDefault(choice => choice.Kind == StockChoiceKind.Table && choice.Id == id);

    internal void ReadBaseDraws(IReadOnlyList<CodeInstruction> instructions, IReadOnlyList<int> calls)
    {
        BaseDraws = calls.Select((call, site) => StockBaseDraw.ReadNative(Draws[site], instructions, call)).ToArray();
        if (Method?.DeclaringType == typeof(BarterOfferList))
        {
            int optional = Draws.Count(draw => draw == 'P');
            int total = instructions.Count(instruction => instruction.Calls(StockPatches.AddOfferSet));
            if (total >= optional && BaseDraws.Where((_, site) => Draws[site] == 'P').All(draw => draw?.Chance.HasValue == true))
                BarterOfferCounts = (total - optional, optional);
        }
    }

    internal static StockAxis AxisFor(char draw) => draw switch
    {
        'P' or 'F' or 'A' => StockAxis.Appearance,
        'Q' or 'C' or 'N' or 'U' => StockAxis.Quantity,
        _ => StockAxis.None
    };

    public override string ToString() => MethodName;
}

internal sealed class StockPatches
{
    internal sealed class Target
    {
        internal readonly MethodBase Method;
        internal readonly StockMethod Rule;
        internal readonly int RuleIndex;
        internal bool Applied;

        internal Target(MethodBase method, StockMethod rule, int ruleIndex)
        {
            Method = method;
            Rule = rule;
            RuleIndex = ruleIndex;
        }
    }

    private static readonly MethodInfo IntRoll = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.AppearanceInt));
    private static readonly MethodInfo FloatRoll = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.AppearanceFloat));
    private static readonly MethodInfo CountRoll = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.QuantityRoll));
    private static readonly MethodInfo RequisitionAmmo = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.RequisitionAmmoRoll));
    private static readonly MethodInfo CountFloat = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.QuantityFloat));
    private static readonly MethodInfo CountInclusive = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.QuantityInclusive));
    private static readonly MethodInfo CountExclusive = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.QuantityUnityExclusive));
    private static readonly MethodInfo AquacultureRoll = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.AquacultureEquipment));
    private static readonly MethodInfo ButcherRoll = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.ButcherQuantityRoll));
    private static readonly MethodInfo InventorRoll = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.InventorRoll));
    private static readonly MethodInfo SpawnFromTable = AccessTools.DeclaredMethod(typeof(ItemSpawner), nameof(ItemSpawner.SpawnFromTable));
    private static readonly MethodInfo RollTable = AccessTools.DeclaredMethod(typeof(TableMaster), nameof(TableMaster.Roll));
    private static readonly MethodInfo ChoiceTableItem = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.ChoiceTableItem));
    private static readonly MethodInfo ChoiceTableEntry = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.ChoiceTableEntry));
    private static readonly MethodInfo NativeIntRoll = AccessTools.DeclaredMethod(typeof(RNG), nameof(RNG.Roll), new[] { typeof(int) });
    private static readonly MethodInfo? AquacultureHelper = AccessTools.DeclaredMethod("StoreClientListSpec:<CreateRetiredAquafarmer>g__StockEquipment|6_1");
    private static readonly FieldInfo? AquacultureFlag = FindAquacultureFlag();
    private static readonly MethodInfo CounterEmpty = AccessTools.DeclaredMethod(typeof(EmporiumEntry), nameof(EmporiumEntry.IsFrontCounterEmptyNonOwned));
    private static readonly MethodInfo StockEmpty = AccessTools.DeclaredMethod(typeof(StockRandom), nameof(StockRandom.IsStockEmpty));
    internal static readonly MethodInfo AddOfferSet = AccessTools.DeclaredMethod(typeof(BarterOffer), nameof(BarterOffer.AddOfferSet));

    private static readonly Dictionary<MethodBase, Target> installed = new Dictionary<MethodBase, Target>();
    private readonly Harmony harmony;
    private readonly ModLog log;
    private readonly List<Target> targets = new List<Target>();
    private readonly List<Target> patched = new List<Target>();

    internal StockPatches(string harmonyId, ModLog log)
    {
        harmony = new Harmony(harmonyId);
        this.log = log;
    }

    internal IReadOnlyList<Target> FindTargets(StockMethod[] rules)
    {
        targets.Clear();
        for (int index = 0; index < rules.Length; index++)
        {
            StockMethod rule = rules[index];
            try
            {
                Target? target = FindTarget(typeof(StoreClient).Assembly, rule, index);
                if (target == null)
                    log.Warning(Line("供货抽取与当前模组支持的规则不匹配，沿用游戏原有逻辑。{0}",
                        "Using the vanilla stock roll for a method outside the supported rules. {0}", rule));
                else
                    targets.Add(target);
            }
            catch (Exception error)
            {
                log.Warning(Line("无法读取供货方法 {0}，沿用游戏原有逻辑。{1}",
                    "Could not read stock method {0}. Using vanilla behavior. {1}", rule, error.Message));
            }
        }
        return targets;
    }

    internal static Target? FindTarget(Assembly assembly, StockMethod rule, int index)
    {
        var method = rule.Method;
        if (method == null || method.DeclaringType?.Assembly != assembly
            || (rule.Choices.Any(choice => choice.Kind == StockChoiceKind.Backpack)
                && method.GetMethodBody()?.LocalVariables.ElementAtOrDefault(2)?.LocalType != typeof(HashSet<string>)))
            return null;

        var instructions = PatchProcessor.GetOriginalInstructions(method);
        if (!TryGetDraws(instructions, rule, out var calls))
            return null;
        rule.ReadBaseDraws(instructions, calls);
        return new Target(method, rule, index);
    }

    internal int Install()
    {
        var transpiler = new HarmonyMethod(typeof(StockPatches), nameof(Transpile)) { priority = Priority.Last };
        foreach (Target target in targets)
        {
            if (installed.ContainsKey(target.Method))
            {
                log.Warning(Line("一个供货方法匹配到多条规则。{0}", "Multiple rules match one stock method. {0}", target.Rule));
                continue;
            }

            installed.Add(target.Method, target);
            try
            {
                harmony.Patch(target.Method, transpiler: transpiler);
                if (target.Applied)
                {
                    patched.Add(target);
                    continue;
                }

                log.Warning(Line("供货方法已被其它补丁改变，沿用现有抽取。{0}",
                    "Another patch changed the stock method. Keeping its current roll. {0}", target.Rule));
            }
            catch (Exception error)
            {
                log.Warning(Line("无法接入供货方法 {0}。{1}", "Could not connect stock method {0}. {1}", target.Rule, error.Message));
            }

            Unpatch(target);
        }
        StockSettingsWindow.Install(harmony, log);
        return patched.Count;
    }

    internal void Remove()
    {
        foreach (Target target in patched)
            Unpatch(target);
        patched.Clear();
        StockSettingsWindow.Remove(harmony);
    }

    private void Unpatch(Target target)
    {
        try
        {
            harmony.Unpatch(target.Method, HarmonyPatchType.Transpiler, harmony.Id);
        }
        catch (Exception error)
        {
            log.Warning(Line("恢复供货方法 {0} 时遇到问题。{1}", "Could not restore stock method {0}. {1}", target.Rule, error.Message));
        }
        finally
        {
            target.Applied = false;
            installed.Remove(target.Method);
        }
    }

    private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        if (!installed.TryGetValue(original, out var target))
            return instructions;
        return Rewrite(instructions.ToList(), target);
    }

    internal static IReadOnlyList<CodeInstruction> Rewrite(IReadOnlyList<CodeInstruction> instructions, Target target)
    {
        target.Applied = false;
        if (!TryGetDraws(instructions, target.Rule, out var calls))
            return instructions;

        var updated = new List<CodeInstruction>(instructions.Count + calls.Count);
        var tableSlots = new Dictionary<StockChoice, int>();
        int nextCall = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            CodeInstruction instruction = instructions[index];
            int site = nextCall < calls.Count && calls[nextCall] == index ? nextCall++ : -1;
            var choice = site < 0 ? null : target.Rule.ChoiceFor(site);
            MethodInfo? replacement;
            if (site < 0)
            {
                replacement = null;
                if (index > 0 && instructions[index - 1].opcode == OpCodes.Ldstr)
                {
                    choice = target.Rule.ChoiceForTable(instructions[index - 1].operand as string);
                    if (choice != null && IsTableRoll(instruction))
                        replacement = instruction.Calls(SpawnFromTable) ? ChoiceTableItem : ChoiceTableEntry;
                }
            }
            else
            {
                replacement = choice?.Replacement ?? (target.Rule.Draws[site] switch
                {
                    'P' or 'Q' when target.Rule.MethodName == nameof(StoreClientList.PlaceInventorInventory) => InventorRoll,
                    'P' when target.Rule.MethodName == nameof(StoreClientList.CreateSecSurplusMerchant)
                        && site == StockRules.RequisitionAmmoSite => RequisitionAmmo,
                    'P' => IntRoll,
                    'F' => FloatRoll,
                    'A' => AquacultureRoll,
                    'Q' when target.Rule.MethodName == "RollButcherCount" => ButcherRoll,
                    'Q' => CountRoll,
                    'C' => CountFloat,
                    'N' => CountInclusive,
                    'U' => CountExclusive,
                    'E' when StockRandom.HasNativePaging => StockEmpty,
                    _ => null
                });
            }
            if (replacement == null)
            {
                updated.Add(instruction);
                continue;
            }

            int first = updated.Count;
            if (replacement == AquacultureRoll)
            {
                // The verified helper takes a captured struct by reference and reads this flag.
                updated.Add(new CodeInstruction(OpCodes.Ldfld, AquacultureFlag));
            }
            else if (replacement == ButcherRoll)
            {
                // Keep the native count loop and identify the meat from its min/max arguments.
                updated.Add(new CodeInstruction(OpCodes.Ldarg_0));
                updated.Add(new CodeInstruction(OpCodes.Ldarg_1));
            }
            else if (choice?.Kind == StockChoiceKind.Backpack)
                updated.Add(new CodeInstruction(OpCodes.Ldloc_2));
            else if (choice?.Kind is StockChoiceKind.Inspectable or StockChoiceKind.Appraisal or StockChoiceKind.List)
            {
                var load = ListLoad(instructions, index, choice.Kind);
                if (load == null)
                    return instructions;
                updated.Add(new CodeInstruction(load.opcode, load.operand));
            }
            if (site < 0)
            {
                int slot = -1;
                if (choice?.Quota != null)
                {
                    tableSlots.TryGetValue(choice, out slot);
                    tableSlots[choice] = slot + 1;
                }
                updated.Add(new CodeInstruction(OpCodes.Ldc_I4, slot));
            }
            else if (choice?.Quota is { } quota && choice.Kind is StockChoiceKind.Range or StockChoiceKind.List)
            {
                var slot = LoopSlotLoad(instructions, index, quota.Total)!;
                updated.Add(new CodeInstruction(slot.opcode, slot.operand));
            }
            updated.Add(new CodeInstruction(OpCodes.Ldc_I4, target.RuleIndex));
            if (site >= 0 && replacement != StockEmpty)
                updated.Add(new CodeInstruction(OpCodes.Ldc_I4, site));
            var callRule = new CodeInstruction(OpCodes.Call, replacement);
            updated[first].labels.AddRange(instruction.labels);
            foreach (ExceptionBlock block in instruction.blocks)
            {
                var beforeOrAfter = block.blockType == ExceptionBlockType.EndExceptionBlock ? callRule : updated[first];
                beforeOrAfter.blocks.Add(block);
            }
            updated.Add(callRule);
        }
        target.Applied = true;
        return updated;
    }

    internal static bool TryGetDraws(IReadOnlyList<CodeInstruction> instructions, StockMethod rule, out List<int> calls)
    {
        calls = new List<int>(rule.Draws.Length);
        for (int index = 0; index < instructions.Count; index++)
        {
            CodeInstruction instruction = instructions[index];
            if ((instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt)
                || !(instruction.operand is MethodInfo call))
                continue;

            char kind = Kind(call);
            if (kind == '\0')
                continue;
            if (calls.Count >= rule.Draws.Length || Kind(rule.Draws[calls.Count]) != kind)
                return false;
            calls.Add(index);
        }
        if (calls.Count != rule.Draws.Length)
            return false;
        for (int site = 0; site < calls.Count; site++)
        {
            var choice = rule.ChoiceFor(site);
            if (choice?.Kind is StockChoiceKind.Inspectable or StockChoiceKind.Appraisal or StockChoiceKind.List
                && ListLoad(instructions, calls[site], choice.Kind) == null)
                return false;
            if (choice?.Quota is { } quota && choice.Kind is StockChoiceKind.Range or StockChoiceKind.List
                && LoopSlotLoad(instructions, calls[site], quota.Total) == null)
                return false;
        }
        foreach (StockChoice choice in rule.Choices.Where(choice => choice.Kind == StockChoiceKind.Table && choice.Quota != null))
            if (instructions.Where((instruction, index) => index > 0 && IsTableRoll(instruction)
                && instructions[index - 1].opcode == OpCodes.Ldstr && Equals(instructions[index - 1].operand, choice.Id)).Count()
                != choice.Quota!.Total)
                return false;
        return true;
    }

    private static bool IsTableRoll(CodeInstruction instruction) => instruction.Calls(SpawnFromTable) || instruction.Calls(RollTable);

    private static CodeInstruction? LoopSlotLoad(IReadOnlyList<CodeInstruction> instructions, int call, int quota)
    {
        // Match the native loop counter increment and its comparison against the stock limit.
        for (int index = call + 1; index + 6 < instructions.Count; index++)
        {
            var load = instructions[index];
            if (!load.IsLdloc() || !instructions[index + 1].LoadsConstant(1L) || instructions[index + 2].opcode != OpCodes.Add
                || !instructions[index + 3].IsStloc() || !instructions[index + 4].IsLdloc()
                || !instructions[index + 5].LoadsConstant((long)quota))
                continue;
            var branch = instructions[index + 6].opcode;
            int slot = load.LocalIndex();
            if ((branch == OpCodes.Blt || branch == OpCodes.Blt_S || branch == OpCodes.Bge || branch == OpCodes.Bge_S)
                && instructions[index + 3].LocalIndex() == slot && instructions[index + 4].LocalIndex() == slot)
                return load;
        }
        return null;
    }

    private static CodeInstruction? ListLoad(IReadOnlyList<CodeInstruction> instructions, int call, StockChoiceKind kind)
    {
        int count = call - (kind == StockChoiceKind.List ? 3 : 1);
        if (count < 1 || instructions[count].operand is not MethodInfo getter || getter.Name != "get_Count"
            || getter.DeclaringType?.IsGenericType != true || getter.DeclaringType.GetGenericTypeDefinition() != typeof(List<>))
            return null;

        var load = instructions[count - 1];
        return load.IsLdloc() ? load : null;
    }

    private static char Kind(char draw) => draw switch
    {
        'P' or 'Q' => 'i',
        'F' or 'C' => 'f',
        'A' => 'a',
        'N' => 'n',
        'U' => 'u',
        'E' => 'e',
        _ => draw
    };

    private static char Kind(MethodInfo call)
    {
        if (call == CounterEmpty)
            return 'e';
        if (call == AquacultureHelper && AquacultureFlag != null)
            return 'a';

        if (call.DeclaringType == typeof(RNG))
        {
            var parameters = call.GetParameters();
            return call.Name switch
            {
                nameof(RNG.Roll) when parameters.Length == 1 =>
                    parameters[0].ParameterType == typeof(int) ? 'i' : 'f',
                nameof(RNG.GetRandomInt) when parameters.Length == 2 => 'n',
                nameof(RNG.GetRandomIntExculsive) when parameters.Length == 2 => 'x',
                nameof(RNG.GetRandomDouble) => 'd',
                _ => '?'
            };
        }
        if (call.DeclaringType == typeof(UnityEngine.Random))
            return call.Name == nameof(UnityEngine.Random.Range) && call.ReturnType == typeof(int) ? 'u' : '?';
        return '\0';
    }

    private static FieldInfo? FindAquacultureFlag()
    {
        if (AquacultureHelper == null)
            return null;

        var body = PatchProcessor.GetOriginalInstructions(AquacultureHelper);
        if (body.Count != 8 || body[0].opcode != OpCodes.Ldarg_0 || body[1].opcode != OpCodes.Ldfld
            || (body[2].opcode != OpCodes.Brfalse && body[2].opcode != OpCodes.Brfalse_S)
            || body[2].operand is not Label branch || !body[6].labels.Contains(branch)
            || !body[3].LoadsConstant(25L) || !body[4].Calls(NativeIntRoll)
            || body[5].opcode != OpCodes.Ret || body[6].opcode != OpCodes.Ldc_I4_1 || body[7].opcode != OpCodes.Ret)
            return null;

        var flag = body[1].operand as FieldInfo;
        return flag?.FieldType == typeof(bool) ? flag : null;
    }
}
