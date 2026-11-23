using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace BetterCustomerStock;

internal static class StockReputationRules
{
    private static readonly Lazy<(int Minimum, double Multiplier)[]> NativeTiers = new(() =>
    {
        var method = AccessTools.DeclaredMethod(typeof(StoreClientList), nameof(StoreClientList.GetBMRepStockMult));
        return method == null ? Array.Empty<(int, double)>() : ReadTiers(PatchProcessor.GetOriginalInstructions(method));
    });

    private static readonly (int Minimum, double Multiplier)[] ReferenceTiers =
    {
        (int.MinValue, 1), (40, 1.25), (60, 1.5), (80, 1.75), (100, 2.5)
    };

    internal static bool HasNativeTiers => NativeTiers.Value.Length != 0;
    internal static (int Minimum, double Multiplier)[] BlackMarketTiers => HasNativeTiers ? NativeTiers.Value : ReferenceTiers;

    internal static StoreReputationPerk ThiefCratePerk()
    {
        return (PlayerStore.HasInstance && PlayerStore.Instance.storeReputations != null
            ? StoreReputation.GetPerkByID(StoreReputationPerk.BM_RELIABLE) : null) ?? StoreReputationPerk.BMReliable();
    }

    // The stock multiplier is a chain of minimum-reputation checks followed by a default return.
    // Read the installed game's implementation without sampling or modifying the player's reputation.
    private static (int, double)[] ReadTiers(IReadOnlyList<CodeInstruction> code)
    {
        var tiers = new List<(int Minimum, double Multiplier)>();
        if (code.Count < 5
            || code[0].operand is not MethodInfo faction || faction.DeclaringType != typeof(StoreReputation)
            || faction.Name != nameof(StoreReputation.GetBMFaction)
            || code[1].operand is not MethodInfo reputation || reputation.DeclaringType != typeof(StoreReputation)
            || reputation.Name != nameof(StoreReputation.GetReputation)
            || code[2].opcode != OpCodes.Stloc_0)
            return Array.Empty<(int, double)>();

        int index = 3;
        int previous = int.MaxValue;
        for (; index + 5 < code.Count; index += 5)
        {
            var branch = code[index + 2];
            if (code[index].opcode != OpCodes.Ldloc_0 || StockBaseDraw.Constant(code[index + 1]) is not double minimum
                || minimum != Math.Truncate(minimum) || minimum <= int.MinValue || minimum >= previous
                || (branch.opcode != OpCodes.Blt && branch.opcode != OpCodes.Blt_S)
                || branch.operand is not Label next || !code[index + 5].labels.Contains(next)
                || StockBaseDraw.Constant(code[index + 3]) is not double multiplier || multiplier < 0 || double.IsNaN(multiplier)
                || code[index + 4].opcode != OpCodes.Ret)
                return Array.Empty<(int, double)>();

            previous = (int)minimum;
            tiers.Add((previous, multiplier));
        }

        if (index + 2 != code.Count || StockBaseDraw.Constant(code[index]) is not double baseline
            || baseline < 0 || double.IsNaN(baseline) || code[index + 1].opcode != OpCodes.Ret)
            return Array.Empty<(int, double)>();

        tiers.Add((int.MinValue, baseline));
        tiers.Reverse();
        return tiers.ToArray();
    }
}
