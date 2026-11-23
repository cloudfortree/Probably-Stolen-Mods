using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection.Emit;
using HarmonyLib;

namespace BetterCustomerStock;

internal readonly struct StockBaseDraw
{
    internal readonly int Minimum;
    internal readonly int Maximum;
    internal readonly double? Chance;

    internal StockBaseDraw(int minimum, int maximum, double? chance = null)
    {
        Minimum = minimum;
        Maximum = maximum;
        Chance = chance;
    }

    internal static StockBaseDraw Roll(double percent, int count = 1)
        => new StockBaseDraw(0, count, Math.Min(100, Math.Max(0, percent)) / 100);

    internal double[] Distribution(int position, bool linear)
    {
        if (Chance is double chance)
        {
            double hit = StockCurve.Probability(chance, position, linear);
            var mass = new double[Maximum + 1];
            mass[0] = 1 - hit;
            mass[Maximum] = hit;
            return mass;
        }

        var uniform = new double[Maximum - Minimum + 1];
        double previous = 1;
        for (int index = 0; index < uniform.Length; index++)
        {
            double tail = StockCurve.Probability((uniform.Length - index - 1d) / uniform.Length, position, linear);
            uniform[index] = previous - tail;
            previous = tail;
        }
        return uniform;
    }

    // Extract the constant inputs from the same native call sites validated for stock adjustment.
    // Special cases with a calculated input are supplied by the corresponding preview scenarios.
    internal static StockBaseDraw? ReadNative(char draw, IReadOnlyList<CodeInstruction> instructions, int call)
    {
        if (draw == 'A')
            return Roll(25);
        if (draw is 'N' or 'U')
        {
            if (ReadConstant(call - 2) is double minimum && ReadConstant(call - 1) is double maximum)
                return new StockBaseDraw((int)minimum, (int)maximum - (draw == 'U' ? 1 : 0));
            return null;
        }

        if (draw is not ('P' or 'F' or 'Q' or 'C'))
            return null;

        if (ReadConstant(call - 1) is double percent)
            return Roll(percent);
        if (call >= 3 && instructions[call - 1].opcode == OpCodes.Mul && ReadConstant(call - 3) is double basePercent)
            return Roll(basePercent);
        return null;

        double? ReadConstant(int index) => index < 0 ? null : Constant(instructions[index]);
    }

    internal static double? Constant(CodeInstruction instruction)
    {
        var code = instruction.opcode;
        if (code == OpCodes.Ldc_I4 || code == OpCodes.Ldc_I4_S || code == OpCodes.Ldc_R4 || code == OpCodes.Ldc_R8)
            return Convert.ToDouble(instruction.operand, CultureInfo.InvariantCulture);
        if (code == OpCodes.Ldc_I4_M1)
            return -1;
        return code.Value >= OpCodes.Ldc_I4_0.Value && code.Value <= OpCodes.Ldc_I4_8.Value
            ? code.Value - OpCodes.Ldc_I4_0.Value : null;
    }
}
