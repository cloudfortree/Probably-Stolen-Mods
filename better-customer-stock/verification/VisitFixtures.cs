using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BetterCustomerStock;
using HarmonyLib;

// The native visit callbacks run on the shipped Mono. Only Unity scene and
// item-placement inputs are substituted in this temporary verification process.
internal sealed class VisitFixtures : IDisposable
{
    private static readonly List<string> offers = new List<string>();
    private static readonly Type[] stringOffer = { typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(int) };
    private static readonly Type[] itemOffer = { typeof(GameItem), typeof(bool), typeof(bool), typeof(bool), typeof(int) };
    private static bool turnAwayInjector;
    private static bool aquacultureII;
    private static int jacksonInvestment;

    private readonly Harmony fixture = new Harmony("better_customer_stock.verification.scene");
    private readonly ModConfig config = Verify.CreateConfig();
    private readonly PlayerStore store = Empty<PlayerStore>();
    private readonly StoreStation station = Empty<StoreStation>();
    private readonly StoreClientInstance guest = Empty<StoreClientInstance>();
    private readonly StoreClient client = Empty<StoreClient>();
    private readonly EmporiumEntry shop = Empty<EmporiumEntry>();
    private readonly StoreReputation blackMarket = Empty<StoreReputation>();
    private readonly StoreReputationPerk reliable = new StoreReputationPerk { perkId = "BM_RELIABLE" };
    private readonly List<StockPatches.Target> targets;
    internal StoreClient Client => client;

    internal VisitFixtures(IReadOnlyList<StockPatches.Target> resolved)
    {
        targets = resolved.ToList();
        blackMarket.factionId = "FACTION_BLACK_MARKET";
        blackMarket.perks = new List<StoreReputationPerk> { reliable };
        store.storeReputations = new List<StoreReputation>
        {
            blackMarket, new StoreReputation { factionId = "FACTION_LOWER_LEVEL", perks = new List<StoreReputationPerk>() }
        };
        store.storeClientData = new StoreClientData();
        AccessTools.Field(typeof(PlayerStore), "instance").SetValue(null, store);
        AccessTools.Field(typeof(StoreStation), "instance").SetValue(null, station);
        AccessTools.Field(typeof(StoreClientInstance), "storeClient").SetValue(guest, client);
        AccessTools.Field(typeof(EmporiumEntry), "<Instance>k__BackingField").SetValue(null, shop);
        store.currentClientInstance = guest;

        Prefix(typeof(PlayerStore).GetMethod("AddDirectSellingItemToTable", stringOffer), nameof(OfferString));
        Prefix(typeof(PlayerStore).GetMethod("AddDirectSellingItemToTable", itemOffer), nameof(OfferItem));
        Prefix(typeof(EmporiumEntry).GetMethod("IsFrontCounterEmptyNonOwned"), nameof(CounterEmpty));
        Prefix(typeof(ItemSpawner).GetMethod("Spawn"), nameof(Spawn));
        Prefix(typeof(ItemSpawner).GetMethod("SpawnFromTable"), nameof(SpawnFromTable));
        Prefix(typeof(LocHelper).GetMethod(nameof(LocHelper.GetLocalizedDialogue)), nameof(LocalizationKey));
        Prefix(typeof(LocHelper).GetMethod(nameof(LocHelper.GetLocalizedItem)), nameof(LocalizationKey));
        Prefix(typeof(TypeHelper).GetMethod(nameof(TypeHelper.GetTypeDisplayName)), nameof(TypeName));
        Prefix(typeof(NetworkUpgrade).GetMethod("IsWinemakerUnlocked"), nameof(ReturnFalse));
        Prefix(typeof(NetworkUpgrade).GetMethod("IsAquacultureIIUnlocked"), nameof(AquacultureTier));
        Prefix(typeof(NetworkUpgrade).GetMethod("IsJackson1Unlocked"), nameof(JacksonTierI));
        Prefix(typeof(NetworkUpgrade).GetMethod("IsJackson2Unlocked"), nameof(JacksonTierII));
        Prefix(AccessTools.DeclaredMethod(typeof(StoreClientListSpec), "SpawnBigFishForSale"), nameof(RecordFish));
        Prefix(typeof(AquacultureItems).GetMethod("SpawnFertilizedRoe"), nameof(SkipItem));
        Prefix(typeof(MachineRAS).GetMethod("SpawnRASWithEmptyTank"), nameof(Ras));
        Prefix(typeof(PreBuiltItemHelper).GetMethod("CreateRealGenuineInjector"), nameof(Injector));
        Prefix(typeof(PreBuiltItemHelper).GetMethod("CreateRandomInjector"), nameof(RandomInjector));
        MethodInfo? sign = AccessTools.Method(typeof(StoreClient).Assembly.GetType("OutOfStockSignHelper"), "IsInspectableKindTurnedAway");
        if (sign != null)
        {
            Prefix(sign, nameof(TurnAway));
        }
        foreach (string name in new[] { "LootCrateEvidence", "LootCrateMedical", "LootCrateEngineering", "LootCrateSecurity", "LootCrateService" })
            Prefix(typeof(PreBuiltItemHelper).GetMethod(name), nameof(Crate));
    }

    internal static ModConfig? ActiveConfig;

    internal void RedirectConfig()
    {
        ActiveConfig = config;
        Prefix(typeof(ModConfig).GetMethod("For"), nameof(LoadConfig));
    }

    internal string[] Thief(int rep, int appearance, int seed, int quantity = 0)
    {
        AccessTools.Field(typeof(StoreReputation), "amount").SetValue(blackMarket, (double)rep);
        reliable.isUnlocked = rep >= 40;
        return Visit("thief", "CreateThief", appearance, quantity, seed);
    }

    internal BarterOffer Addict(int seed, bool initialVisit = false)
    {
        UnityEngine.Random.InitState(seed);
        return initialVisit ? BarterOfferList.CreateAddictBarter() : BarterOfferList.CreateAddictGeneralBarter();
    }

    internal string[][] AddictOffers(int seed)
    {
        return Addict(seed).offerSets.Select(show =>
        {
            offers.Clear();
            show();
            return offers.ToArray();
        }).ToArray();
    }

    private static bool LocalizationKey(string key, ref string __result)
    {
        __result = key;
        return false;
    }

    private static bool TypeName(string typeConst, ref string __result) => LocalizationKey(typeConst, ref __result);

    internal int Miner(int day, int quantity)
    {
        station.dayCounter = day;
        return Visit("miner", "CreateMiner", 0, quantity, 45).Count(id => id == "common_ore");
    }

    internal string[] Pharmacist(int quantity, int seed)
        => Visit("rareLowerLevelChemist", "CreateRareLowerLevelChemist", 0, quantity, seed);

    internal string[] UpperLevelPharmacist(bool injectorIntroduced)
    {
        store.isInjectorIntroduced = injectorIntroduced;
        try
        {
            return Visit("shadyPharma", nameof(StoreClientList.CreateShadyPharmacist), 0, -100, 45);
        }
        finally
        {
            store.isInjectorIntroduced = false;
        }
    }

    internal string AppraisalChoice(int rank, int seed, params string[] excluded)
    {
        var previous = store.appraisalBlacklist;
        store.appraisalBlacklist = excluded.ToList();
        try
        {
            UnityEngine.Random.InitState(seed);
            var pool = AppraisalItemRegistry.GetPool(rank);
            int rule = targets.Single(target => target.Rule.MethodName == nameof(StoreClientListAppraisal.CreateAppraisalRandom)).RuleIndex;
            return pool[StockRandom.ChoiceAppraisal(0, pool.Count, pool, rule, 0)].id;
        }
        finally
        {
            store.appraisalBlacklist = previous;
        }
    }

    internal bool RequisitionAmmoRoll(bool secondPermit)
    {
        var previous = store.ActivePermits;
        store.ActivePermits = new List<(string, int)> { ("FIREARM", 1) };
        if (secondPermit)
            store.ActivePermits.Add(("FIREARM", 2));
        try
        {
            client.identifier = "secSurplusMerchant";
            Verify.SetConfig(config, "default.appearance", 100);
            Verify.SetConfig(config, "default.quantity", -100);
            int rule = targets.Single(target => target.Rule.MethodName == nameof(StoreClientList.CreateSecSurplusMerchant)).RuleIndex;
            return StockRandom.RequisitionAmmoRoll(50, rule, StockRules.RequisitionAmmoSite);
        }
        finally
        {
            store.ActivePermits = previous;
        }
    }

    internal string[] Hypertech(int seed)
        => Visit("hypertech_smuggler", nameof(StoreClientListSupplier.CreateHypertechSmuggler), 0, 0, seed);

    internal string[] EleanorMaterials(int seed)
        => Visit("eleanor5", nameof(StoreClientUniqueList.Eleanor5), 0, 0, seed);

    internal string[] Winemaker(int seed)
        => Visit("retired_winemaker", nameof(StoreClientListSpec.CreateRetiredWinemaker), 0, 0, seed);

    internal bool JacksonBackpackRoll(int chance, int site, params string[] owned)
    {
        station.dayCounter = 90;
        UnityEngine.Random.InitState(45);
        int rule = targets.Single(target => target.Rule.MethodName == nameof(StoreClientList.PlaceInventorInventory)).RuleIndex;
        return StockRandom.ChoiceBackpack(chance, new HashSet<string>(owned), rule, site);
    }

    internal bool JacksonChemistryRoll(bool visiting, int investment, int site)
    {
        jacksonInvestment = investment;
        store.playerLocation = visiting ? PlayerStore.PlayerLocation.IN_STORE : PlayerStore.PlayerLocation.INVENTOR;
        client.identifier = "thief";
        UnityEngine.Random.InitState(45);
        try
        {
            int rule = targets.Single(target => target.Rule.MethodName == nameof(StoreClientList.PlaceInventorInventory)).RuleIndex;
            return StockRandom.InventorRoll(50, rule, site);
        }
        finally
        {
            store.playerLocation = PlayerStore.PlayerLocation.IN_STORE;
            jacksonInvestment = 0;
        }
    }

    internal string[] MaterialScavenger(int appearance, int quantity)
        => Visit("scavGenericMaterialSeller", "AddScavMaterialStock", appearance, quantity, 45);

    internal string[] Aquafarmer(bool advanced, int appearance, int seed)
    {
        aquacultureII = advanced;
        return Visit("retired_aquafarmer", "CreateRetiredAquafarmer", appearance, 0, seed);
    }

    internal int Butcher(int min, int max, int quantity)
    {
        client.identifier = "wanted2_return";
        UnityEngine.Random.InitState(45);
        Verify.SetConfig(config, "default.quantity", quantity);
        var method = targets.Single(target => target.Rule.MethodName == "RollButcherCount").Method;
        return (int)method.Invoke(null, new object[] { min, max, 50 });
    }

    internal string[] MedicalScavenger(bool turnAway)
    {
        store.isInjectorIntroduced = true;
        turnAwayInjector = turnAway;
        try
        {
            return Visit("scavGenericMedSeller", "AddScavMedStock", 100, 0, 45);
        }
        finally
        {
            store.isInjectorIntroduced = false;
            turnAwayInjector = false;
        }
    }

    internal string[] Scavenger(string id, int setting)
    {
        station.dayCounter = 1;
        return Visit(id, "AddScavGeneralStock", setting, setting, 45);
    }

    internal CounterFixture Counter() => new CounterFixture(shop);

    private string[] Visit(string id, string methodName, int appearance, int quantity, int seed)
    {
        client.identifier = id;
        client.clientIntent = StoreClient.ClientIntent.SELL;
        offers.Clear();
        UnityEngine.Random.InitState(seed);
        Verify.SetConfig(config, "default.appearance", appearance);
        Verify.SetConfig(config, "default.quantity", quantity);
        MethodBase method = targets.Single(target => target.Rule.MethodName == methodName).Method;
        method.Invoke(method.IsStatic ? null : FormatterServices.GetUninitializedObject(method.DeclaringType!), null);
        return offers.ToArray();
    }

    private void Prefix(MethodInfo method, string prefix)
    {
        if (method == null)
            throw new InvalidOperationException("Fixture method not found " + prefix);
        fixture.Patch(method, prefix: new HarmonyMethod(typeof(VisitFixtures), prefix));
    }

    private static bool OfferString(string identifier)
    {
        if (CounterFixture.Active)
            return true;
        offers.Add(identifier);
        return false;
    }

    private static bool OfferItem(GameItem gameItem) => OfferString(gameItem.identifier);

    private static bool CounterEmpty(ref bool __result)
    {
        if (CounterFixture.Active)
            return true;
        __result = offers.Count == 0;
        return false;
    }

    private static bool ReturnFalse(ref bool __result)
    {
        __result = false;
        return false;
    }

    private static bool AquacultureTier(ref bool __result)
    {
        __result = aquacultureII;
        return false;
    }

    private static bool JacksonTierI(ref bool __result)
    {
        __result = jacksonInvestment >= 1;
        return false;
    }

    private static bool JacksonTierII(ref bool __result)
    {
        __result = jacksonInvestment >= 2;
        return false;
    }

    private static bool SkipItem(ref GameItem? __result)
    {
        __result = null;
        return false;
    }

    private static bool RecordFish(int pick, ref GameItem? __result)
    {
        offers.Add("fish:" + pick);
        __result = null;
        return false;
    }

    private static bool Ras(ref GameItem __result) => Spawn("ras", ref __result);

    private static bool LoadConfig(ref ModConfig __result)
    {
        __result = ActiveConfig!;
        return false;
    }

    private static bool Spawn(string itemID, ref GameItem __result)
    {
        __result = Item(itemID);
        return false;
    }

    private static bool SpawnFromTable(string tableID, ref GameItem __result)
        => Spawn(tableID, ref __result);

    private static bool Injector(ref GameItem __result) => Spawn("large_purple_injector", ref __result);

    private static bool RandomInjector(ref GameItem __result) => Spawn("fixture_random_injector", ref __result);

    private static bool TurnAway(string kindId, ref bool __result)
    {
        __result = turnAwayInjector && kindId == "inspectable_injector";
        return false;
    }

    private static bool Crate(MethodBase __originalMethod, ref GameItem __result)
        => Spawn("crate:" + __originalMethod.Name, ref __result);

    internal static GameItem Item(string id)
    {
        GameItem item = Empty<GameItemElement>();
        item.identifier = id;
        item.unitCount = 1;
        return item;
    }

    internal static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

    public void Dispose()
    {
        ActiveConfig = null;
        fixture.UnpatchAll(fixture.Id);
    }
}
