using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Random = UnityEngine.Random;

namespace BetterCustomerStock;

internal static class StockRandom
{
    private static readonly Func<StoreClient, bool>? HasHeldStock = BindNativeHeldStock();
    private static readonly Func<string, bool>? IsTurnedAway = BindInspectableSign();
    private static readonly Action<LootRollContext>? FireLootRoll = BindLootRoll();

    internal static StockSettings? Settings;
    internal static bool HasNativePaging => HasHeldStock != null;
    private static bool LinearChanges => Settings?.UseLinearCurve() == true;

    internal static bool AppearanceInt(int chance, int rule, int site)
        => RollInt(chance, Get(rule, site, StockAxis.Appearance));

    internal static bool AppearanceFloat(float chance, int rule, int site)
        => RollFloat(chance, Get(rule, site, StockAxis.Appearance));

    internal static bool QuantityFloat(float chance, int rule, int site)
        => RollFloat(chance, Get(rule, site, StockAxis.Quantity));

    internal static bool AquacultureEquipment(bool aquacultureII, int rule, int site)
        => !aquacultureII || AppearanceInt(25, rule, site);

    private static bool RollFloat(float chance, int setting)
    {
        if (setting == 0 || !(chance > 0 && chance < 100))
            return RNG.Roll(chance);

        if (setting == StockCurve.Limit || setting == -StockCurve.Limit)
        {
            RNG.Roll(chance);
            return setting > 0;
        }

        return RNG.Roll((float)(100 * StockCurve.Probability(chance / 100.0, setting, LinearChanges)));
    }

    internal static bool QuantityRoll(int chance, int rule, int site)
        => RollInt(chance, Get(rule, site, StockAxis.Quantity));

    internal static bool RequisitionAmmoRoll(int chance, int rule, int site)
    {
        // The second licence adds another independent pair of the same ammunition.
        StockAxis axis = PermitHelper.IsGunPermitII() ? StockAxis.Quantity : StockAxis.Appearance;
        return RollInt(chance, Get(rule, site, axis));
    }

    internal static bool InventorRoll(int chance, int rule, int site)
    {
        var settings = Settings;
        if (settings == null)
            return RNG.Roll(chance);

        var method = StockRules.Methods[rule];
        return RollInt(chance, settings.Get(method, InventorProfile(method), site));
    }

    internal static bool ChoiceInt(int chance, int rule, int site)
    {
        double? probability = ChoiceBranch(chance, rule, site);
        return probability.HasValue ? ChoiceSample() < probability.Value : RNG.Roll(chance);
    }

    internal static bool ChoiceFloat(float chance, int rule, int site)
    {
        double? probability = ChoiceBranch(chance, rule, site);
        return probability.HasValue ? ChoiceSample() < probability.Value : RNG.Roll(chance);
    }

    private static double? ChoiceBranch(double chance, int rule, int site)
    {
        var method = StockRules.Methods[rule];
        var choice = method.ChoiceFor(site)!;
        int branch = choice.NativeChances.Length == 1 ? 0 : choice.IndexOfSite(site);
        if (chance != choice.NativeChances[branch])
            return null;

        var allocation = Settings?.ActiveAllocation(method.Profiles[0], choice, choice.Categories);
        if (allocation != null)
        {
            string first = choice.Categories[0];
            string? fixedItem = allocation.FixedItem(choice.IndexOfSite(site));
            if (fixedItem != null)
                return fixedItem == first ? 1d : 0d;
            return allocation.RandomWeights(choice.Categories, choice.NativeWeights)[0];
        }

        double[]? weights = AdjustedWeights(method.Profiles[0], choice, choice.NativeWeights);
        return weights == null ? null : RemainingProbability(weights, branch);
    }

    internal static int ChoiceRangeSlot(int min, int max, int slot, int rule, int site)
    {
        var method = StockRules.Methods[rule];
        var choice = method.ChoiceFor(site)!;
        if (min == 0 && max == choice.Categories.Length - 1
            && Settings?.ActiveAllocation(method.Profiles[0], choice, choice.Categories) is { } allocation)
        {
            int index = AllocatedUniformIndex(allocation, slot, choice.Categories);
            if (index >= 0)
                return index;
        }

        return ChoiceRange(min, max, rule, site);
    }

    internal static int ChoiceRange(int min, int max, int rule, int site)
    {
        var method = StockRules.Methods[rule];
        var choice = method.ChoiceFor(site)!;
        var starts = choice.RangeStarts;
        if (min != 0 || max >= starts[starts.Length - 1] || max < 0)
            return RNG.GetRandomInt(min, max);

        double[]? weights = AdjustedWeights(method.Profiles[0], choice, choice.RangeWeights(max));
        return weights == null ? RNG.GetRandomInt(min, max) : starts[StockCurve.SelectChoice(weights, ChoiceSample())];
    }

    internal static int ChoiceInspectable(int min, int max, IList candidates, int rule, int site)
    {
        var store = PlayerStore.Instance;
        if (Settings == null || store == null || min != 0 || max != candidates.Count)
            return RNG.GetRandomIntExculsive(min, max);

        var method = StockRules.Methods[rule];
        var choice = method.ChoiceFor(site)!;
        int[]? positions = Settings.GetChoicePositions(method.Profiles[0], choice);
        if (positions == null || !positions.Any(value => value != 0))
            return RNG.GetRandomIntExculsive(min, max);

        bool[]? unlocked = InspectableCandidates(store, candidates is not List<Action>);
        if (unlocked == null)
            return RNG.GetRandomIntExculsive(min, max);
        positions = positions.Where((_, index) => unlocked[index]).ToArray();
        if (positions.Length != max)
            return RNG.GetRandomIntExculsive(min, max);

        double[]? weights = StockCurve.ChoiceWeights(Enumerable.Repeat(1d, max).ToArray(), positions, LinearChanges);
        return weights == null ? RNG.GetRandomIntExculsive(min, max) : StockCurve.SelectChoice(weights, ChoiceSample());
    }

    internal static bool[]? InspectableCandidates(PlayerStore store, bool useSign)
    {
        bool[] unlocked = { store.isStampIntroduced, store.isVisualInspectionIntroduced, store.isInjectorIntroduced };
        if (!useSign)
            return unlocked;
        if (IsTurnedAway == null)
            return null;

        // Beta uses the unlocked types if its sign would remove the entire pool.
        bool[] offered = { unlocked[0] && !IsTurnedAway("inspectable_stamp"),
            unlocked[1] && !IsTurnedAway("inspectable_cigarette"), unlocked[2] && !IsTurnedAway("inspectable_injector") };
        return offered.Any(value => value) ? offered : unlocked;
    }

    internal static int ChoiceList(int min, int max, List<string> candidates, int slot, int rule, int site)
    {
        if (min != 0 || max != candidates.Count - 1)
            return RNG.GetRandomInt(min, max);

        var method = StockRules.Methods[rule];
        var choice = method.ChoiceFor(site)!;
        if (Settings?.ActiveAllocation(method.Profiles[0], choice, choice.Categories) is { } allocation)
        {
            int index = AllocatedUniformIndex(allocation, slot, candidates);
            if (index >= 0)
                return index;
        }
        int[]? positions = EntryPositions(method.Profiles[0], choice, candidates);
        double[]? weights = positions == null ? null : StockCurve.ChoiceWeights(Enumerable.Repeat(1d, candidates.Count).ToArray(), positions, LinearChanges);
        return weights == null ? RNG.GetRandomInt(min, max) : StockCurve.SelectChoice(weights, ChoiceSample());
    }

    private static int AllocatedUniformIndex(StockAllocation allocation, int slot, IList<string> candidates)
    {
        if (allocation.FixedItem(slot) is { } item)
            return candidates.IndexOf(item);

        int[] eligible = Enumerable.Range(0, candidates.Count).Where(index => allocation.IsRandom(candidates[index])).ToArray();
        return eligible.Length == 0 ? -1 : eligible[RNG.GetRandomInt(0, eligible.Length - 1)];
    }

    internal static int ChoiceAppraisal(int min, int max, List<AppraisalItemType> candidates, int rule, int site)
    {
        // Use the pool already filtered by the certificate, rank and last-allowed-item rule.
        if (min != 0 || max != candidates.Count || candidates.Count <= 1)
            return Random.Range(min, max);

        var method = StockRules.Methods[rule];
        var choice = method.ChoiceFor(site)!;
        int[]? positions = EntryPositions(method.Profiles[0], choice, candidates.Select(candidate => candidate.id));
        double[]? weights = positions == null ? null : StockCurve.ChoiceWeights(Enumerable.Repeat(1d, candidates.Count).ToArray(), positions, LinearChanges);
        return weights == null ? Random.Range(min, max) : StockCurve.SelectChoice(weights, ChoiceSample());
    }

    internal static GameItem ChoiceTableItem(string tableId, int slot, int rule)
        => TryAdjustTableEntry(tableId, slot, rule, out string? itemId) ? ItemSpawner.Spawn(itemId) : ItemSpawner.SpawnFromTable(tableId);

    internal static string ChoiceTableEntry(string tableId, int slot, int rule)
        => TryAdjustTableEntry(tableId, slot, rule, out string? itemId) ? itemId! : TableMaster.Roll(tableId);

    private static bool TryAdjustTableEntry(string tableId, int slot, int rule, out string? itemId)
    {
        itemId = null;
        if (Settings == null || FireLootRoll == null)
            return false;

        var method = StockRules.Methods[rule];
        var choice = method.ChoiceForTable(tableId)!;
        var profile = method.Profiles.Length == 1 ? method.Profiles[0]
            : method.Profiles.FirstOrDefault(candidate => candidate.Includes(StockSettings.CurrentStoreClient()?.identifier));
        if (profile == null)
            return false;

        bool exact = slot >= 0 && Settings.UsesExactCounts(profile, choice);
        int[]? positions = Settings.GetChoicePositions(profile, choice);
        if (!exact && (positions == null || !positions.Any(value => value != 0)))
            return false;
        List<LootEntry> entries = LootRegistry.GetTableEntries(tableId);
        string[] ids = entries.Select(entry => entry.id).ToArray();
        double[] original = entries.Select(entry => (double)entry.weight).ToArray();
        string? selected = null;
        if (exact)
        {
            var allocation = Settings.ActiveAllocation(profile, choice, ids.Where((_, index) => original[index] > 0));
            if (allocation != null)
                selected = allocation.FixedItem(slot) ?? Pick(allocation.RandomWeights(ids, original));
        }
        else
        {
            double[]? weights = StockCurve.ChoiceWeights(original, EntryPositions(choice, ids, positions!), LinearChanges);
            if (weights != null)
                selected = Pick(weights);
        }

        if (selected == null)
            return false;
        var context = new LootRollContext { tableId = tableId, itemId = selected };
        FireLootRoll(context);
        itemId = context.itemId;
        return true;

        string? Pick(double[] weights)
        {
            int index = StockCurve.SelectChoice(weights, ChoiceSample());
            return index < 0 ? null : ids[index];
        }
    }

    private static int[]? EntryPositions(StockProfile profile, StockChoice choice, IEnumerable<string> ids)
    {
        int[]? positions = Settings?.GetChoicePositions(profile, choice);
        return positions == null ? null : EntryPositions(choice, ids, positions);
    }

    internal static int[] EntryPositions(StockChoice choice, IEnumerable<string> ids, int[] positions)
        => ids.Select(id =>
        {
            int index = Array.IndexOf(choice.Categories, id);
            return index >= 0 ? positions[index] : 0;
        }).ToArray();

    internal static bool ChoiceBackpack(int chance, HashSet<string> owned, int rule, int site)
    {
        if (Settings == null)
            return RNG.Roll(chance);

        var method = StockRules.Methods[rule];
        var choice = method.ChoiceFor(site)!;
        double[] chances = BackpackChances();
        int branch = choice.IndexOfSite(site);
        if (chances[branch] != chance)
            return RNG.Roll(chance);

        double[] original = BackpackWeights(choice, chances, owned, out double noBackpack);
        double[]? weights = AdjustedWeights(InventorProfile(method), choice, original);
        if (weights == null)
            return RNG.Roll(chance);

        // Redistribute the existing chance of a backpack and keep the chance of no backpack.
        Array.Resize(ref weights, chances.Length + 1);
        for (int index = 0; index < chances.Length; index++)
            weights[index] *= 1 - noBackpack;
        weights[chances.Length] = noBackpack;
        return ChoiceSample() < RemainingProbability(weights, branch);
    }

    internal static double[] BackpackChances()
    {
        int day = StoreStation.GetDayCounter();
        int bonus = StoreClientData.GetDrJacksonInvestmentBonus();
        return new double[] { day - 30 + bonus, day - 12 + bonus, 2 * day + 10 + bonus };
    }

    internal static double[] BackpackWeights(StockChoice choice, double[] chances, HashSet<string> owned, out double noBackpack)
    {
        for (int index = 0; index < chances.Length; index++)
            if (owned.Contains(choice.Categories[index]))
                chances[index] = 0;

        double[] weights = StockCurve.SequentialWeights(chances);
        noBackpack = weights[weights.Length - 1];
        Array.Resize(ref weights, chances.Length);
        return weights;
    }

    private static StockProfile InventorProfile(StockMethod method)
    {
        int investment = NetworkUpgrade.IsJackson2Unlocked() ? 2 : NetworkUpgrade.IsJackson1Unlocked() ? 1 : 0;
        return method.Profiles[investment];
    }

    private static double[]? AdjustedWeights(StockProfile profile, StockChoice choice, double[] original)
    {
        int[]? positions = Settings?.GetChoicePositions(profile, choice);
        return positions == null ? null : StockCurve.ChoiceWeights(original, positions, LinearChanges);
    }

    private static double RemainingProbability(double[] weights, int branch)
    {
        if (weights[branch] <= 0)
            return 0;

        double remaining = 0;
        for (int index = branch; index < weights.Length; index++)
            remaining += weights[index];
        return weights[branch] / remaining;
    }

    private static double ChoiceSample() => RNG.GetRandomInt(0, 16_777_215) / 16_777_216.0;

    internal static bool ButcherQuantityRoll(int chance, int min, int max, int rule, int site)
    {
        string? category = (min, max) switch
        {
            (0, 3) => "raw_meat",
            (1, 3) => "small_raw_meat",
            (0, 2) => "fat_meat",
            _ => null
        };
        return RollInt(chance, category == null ? 0 : Get(rule, site, StockAxis.Quantity, category));
    }

    private static bool RollInt(int chance, int setting)
    {
        if (setting == 0 || chance <= 0 || chance >= 100)
            return RNG.Roll(chance);

        if (setting == StockCurve.Limit || setting == -StockCurve.Limit)
        {
            RNG.Roll(chance);
            return setting > 0;
        }

        return RNG.Roll((float)(100 * StockCurve.Probability(chance / 100.0, setting, LinearChanges)));
    }

    internal static int QuantityInclusive(int min, int max, int rule, int site)
    {
        int setting = Get(rule, site, StockAxis.Quantity);
        return setting == 0 || min >= max
            ? RNG.GetRandomInt(min, max)
            : StockCurve.Quantity(min, max, setting, RNG.GetRandomFloat(0f, 1f), LinearChanges);
    }

    internal static int QuantityUnityExclusive(int min, int maxExclusive, int rule, int site)
    {
        int setting = Get(rule, site, StockAxis.Quantity);
        return setting == 0 || (long)maxExclusive - min <= 1
            ? Random.Range(min, maxExclusive)
            : StockCurve.Quantity(min, maxExclusive - 1, setting, RNG.GetRandomFloat(0f, 1f), LinearChanges);
    }

    internal static bool IsStockEmpty(EmporiumEntry counter, int rule)
    {
        // Keep the original counter check, including any result from the legacy pager.
        if (!counter.IsFrontCounterEmptyNonOwned())
            return false;

        var settings = Settings;
        if (settings == null || HasHeldStock == null)
            return true;

        StoreClient? client = StockSettings.CurrentStoreClient();
        return client == null || !settings.UsesAdjustment(StockRules.Methods[rule], client.identifier)
            || !HasHeldStock(client);
    }

    private static Func<StoreClient, bool>? BindNativeHeldStock()
    {
        // The trade query calls the public reserve API in Beta. Take the exact
        // game method reference from that call, so this assembly can also load on stable.
        var query = AccessTools.DeclaredMethod(typeof(StoreClientInstance),
            nameof(StoreClientInstance.IsCurrentClientStillHaveThingToSell));
        if (query == null)
            return null;

        var api = PatchProcessor.GetOriginalInstructions(query)
            .Select(instruction => instruction.operand).OfType<MethodInfo>()
            .FirstOrDefault(method => method.Name == "HasHeldStock" && method.IsPublic && method.IsStatic
                && method.DeclaringType?.FullName == "StockPageHelper"
                && method.DeclaringType.Assembly == typeof(StoreClient).Assembly
                && method.ReturnType == typeof(bool)
                && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { typeof(StoreClient) }));
        return api == null ? null : (Func<StoreClient, bool>)Delegate.CreateDelegate(typeof(Func<StoreClient, bool>), api);
    }

    private static Action<LootRollContext>? BindLootRoll()
    {
        var method = AccessTools.DeclaredMethod(typeof(ModHook), "FireOnLootRolled", new[] { typeof(LootRollContext) });
        return method == null ? null : (Action<LootRollContext>)Delegate.CreateDelegate(typeof(Action<LootRollContext>), method);
    }

    private static Func<string, bool>? BindInspectableSign()
    {
        var stock = AccessTools.DeclaredMethod(typeof(StoreClientListSupplier), nameof(StoreClientListSupplier.AddScavMedStock));
        if (stock == null)
            return null;

        var api = PatchProcessor.GetOriginalInstructions(stock).Select(instruction => instruction.operand).OfType<MethodInfo>()
            .FirstOrDefault(method => method.Name == "IsInspectableKindTurnedAway" && method.IsStatic
                && method.DeclaringType?.FullName == "OutOfStockSignHelper" && method.DeclaringType.Assembly == typeof(StoreClient).Assembly
                && method.ReturnType == typeof(bool) && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { typeof(string) }));
        return api == null ? null : (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), api);
    }

    private static int Get(int rule, int site, StockAxis axis, string? category = null)
        => Settings?.Get(StockRules.Methods[rule], axis, site, category) ?? 0;
}
