using System;
using System.Collections.Generic;

namespace BetterCustomerStock;

internal static class StockCurve
{
    internal const int Limit = 100;

    // Equal settings preserve the native proportions and RNG, including when every choice is excluded.
    internal static double[]? ChoiceWeights(double[] original, int[] positions, bool linear = false)
    {
        int? firstPosition = null;
        bool samePosition = true;
        double originalTotal = 0;
        for (int index = 0; index < original.Length; index++)
        {
            if (original[index] <= 0)
                continue;

            originalTotal += original[index];
            firstPosition ??= positions[index];
            samePosition &= positions[index] == firstPosition.Value;
        }
        if (samePosition)
            return null;

        var probabilities = new double[original.Length];
        var levels = new SortedSet<int>();
        double baseTotal = 0;
        for (int index = 0; index < original.Length; index++)
        {
            if (original[index] <= 0)
                continue;

            int position = positions[index];
            int reduction = Math.Min(0, position);
            probabilities[index] = original[index] * (Limit + reduction) / (linear ? Limit : Limit - reduction);
            baseTotal += probabilities[index];
            if (position > 0)
                levels.Add(position);
        }

        if (linear)
        {
            double baseShare = 1 - levels.Max / (double)Limit;
            for (int index = 0; index < probabilities.Length; index++)
                probabilities[index] = baseShare * probabilities[index] / baseTotal;

            int previousLevel = 0;
            foreach (int level in levels)
            {
                double layerWeight = 0;
                for (int index = 0; index < original.Length; index++)
                    if (positions[index] >= level && original[index] > 0)
                        layerWeight += original[index];

                // Each positive layer directly contributes its width in probability mass.
                double share = (level - previousLevel) / (double)Limit;
                for (int index = 0; index < original.Length; index++)
                    if (positions[index] >= level && original[index] > 0)
                        probabilities[index] += share * original[index] / layerWeight;
                previousLevel = level;
            }
            return probabilities;
        }

        var bonusShares = new double[original.Length];
        double previousMultiplier = 0;
        double cumulativeBonus = 0;
        foreach (int level in levels)
        {
            double layerWeight = 0;
            for (int index = 0; index < original.Length; index++)
                if (positions[index] >= level && original[index] > 0)
                    layerWeight += original[index];

            double multiplier = level == Limit ? double.PositiveInfinity : 2.0 * level / (Limit - level);
            double increase = (multiplier - previousMultiplier) * layerWeight;
            double nextBonus = cumulativeBonus + increase;
            // Difference of h / (originalTotal + h) across the layer, including the limit at +100.
            double share = originalTotal / (originalTotal + cumulativeBonus);
            if (level < Limit)
                share *= increase / (originalTotal + nextBonus);

            for (int index = 0; index < original.Length; index++)
                if (positions[index] >= level && original[index] > 0)
                    bonusShares[index] += share * original[index] / layerWeight;

            previousMultiplier = multiplier;
            cumulativeBonus = nextBonus;
        }

        if (double.IsPositiveInfinity(cumulativeBonus))
            return bonusShares;

        double adjustedTotal = baseTotal + cumulativeBonus;
        double bonusScale = originalTotal + cumulativeBonus;
        for (int index = 0; index < probabilities.Length; index++)
            probabilities[index] = (probabilities[index] + bonusScale * bonusShares[index]) / adjustedTotal;
        return probabilities;
    }

    internal static int SelectChoice(double[] probabilities, double sample)
    {
        int lastEligible = -1;
        double cumulative = 0;
        for (int index = 0; index < probabilities.Length; index++)
        {
            if (probabilities[index] <= 0)
                continue;

            lastEligible = index;
            cumulative += probabilities[index];
            if (sample < cumulative)
                return index;
        }
        return lastEligible;
    }

    internal static double[] SequentialWeights(double[] chances)
    {
        var weights = new double[chances.Length + 1];
        double rest = 1;
        for (int index = 0; index < chances.Length; index++)
        {
            double probability = Math.Min(100, Math.Max(0, chances[index])) / 100;
            weights[index] = rest * probability;
            rest *= 1 - probability;
        }
        weights[chances.Length] = rest;
        return weights;
    }

    internal static double Probability(double original, int setting, bool linear = false)
    {
        if (original <= 0 || original >= 1 || setting == 0)
            return original;

        if (setting <= -Limit)
            return 0;
        if (setting >= Limit)
            return 1;

        if (linear)
            return setting < 0 ? original * (Limit + setting) / Limit
                : original + (1 - original) * setting / Limit;

        // The default curve scales odds by (100 + setting) / (100 - setting).
        double success = original * (Limit + setting);
        double failure = (1 - original) * (Limit - setting);
        return success / (success + failure);
    }

    // Sample the adjusted survival function of a native inclusive uniform count.
    // The same draw is used at every threshold so the counts remain nested.
    internal static int Quantity(int min, int max, int setting, double sample, bool linear = false)
    {
        if (min >= max || setting <= -Limit)
            return min;
        if (setting >= Limit)
            return max;

        long low = min;
        long high = max;
        double possibleCounts = high - low + 1;
        while (low < high)
        {
            long threshold = low + (high - low + 1) / 2;
            double original = (max - threshold + 1) / possibleCounts;
            if (sample < Probability(original, setting, linear))
                low = threshold;
            else
                high = threshold - 1;
        }
        return (int)low;
    }
}
