using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

// Run the real AddDirectSellingItemToTable and paging code. The fixture
// supplies a capacity-limited grid in memory, without creating Unity UI objects.
internal sealed class CounterFixture : IDisposable
{
    private static CounterFixture? current;
    internal static bool Active => current != null;

    private readonly Harmony fixture = new Harmony("better_customer_stock.verification.counter");
    private readonly Dictionary<GameGridInventory, List<GameItem>> grids = new Dictionary<GameGridInventory, List<GameItem>>();
    private readonly GridShape shape = new GridShapeBuilder(18, 4).SetDataFill(0).SetDataOutside(1).Build();
    private readonly GameGridInventory counter;
    private readonly List<GameItem> placed;
    internal EmporiumEntry Shop { get; }
    internal int Capacity { get; set; }
    internal int ReserveCapacity { get; set; } = 4;
    internal int Unplaced { get; private set; }
    internal int Placed => placed.Count;
    internal IReadOnlyList<GameItem> Visible => placed;

    internal CounterFixture(EmporiumEntry shop)
    {
        Shop = shop;
        counter = NewGrid();
        placed = grids[counter];
        AccessTools.Field(typeof(UnityEngine.Object), "m_CachedPtr").SetValue(shop, new IntPtr(1));
        AccessTools.Field(typeof(EmporiumEntry), "<frontInvinvElement>k__BackingField").SetValue(shop, counter);
        AccessTools.Field(typeof(EmporiumEntry), "<backInvinvElement>k__BackingField").SetValue(shop, NewGrid());
        current = this;

        Prefix(typeof(GameGridInventory).GetMethod("CanTakeAnything"), nameof(HasRoom));
        Prefix(typeof(DirectoryMaster).GetMethod("Item", new[] { typeof(string), typeof(bool) }), nameof(MakeItem));
        Prefix(typeof(GraphUtils).GetMethod("TryAcceptAll"), nameof(Place));
        Prefix(typeof(GraphUtils).GetMethod("DestroyIfUnplaced"), nameof(DestroyUnplaced));
        Prefix(typeof(GraphUtils).GetMethod("ReportUnplaced"), nameof(SkipUnplaced));
        Prefix(typeof(GameItemElement).GetMethod("IsDestroyed"), nameof(Alive));
        Prefix(typeof(GameItemElement).GetMethod("Destroy"), nameof(Destroy));
        Prefix(typeof(GameItem).GetMethod("CloseContentWindow"), nameof(Skip));
        Prefix(typeof(EmporiumEntry).GetMethod("DeselectDragItem"), nameof(Skip));
        Prefix(typeof(GeneralHelper).GetMethod("SetItemOwned"), nameof(Skip));
        Prefix(typeof(GeneralHelper).GetMethod("IsItemOwned", new[] { typeof(GameItem) }), nameof(Alive));
        Prefix(typeof(StartingPerkContent).GetMethod("HandleGamblingAddictionItemAdded"), nameof(Skip));
    }

    internal void Clear()
    {
        TakeVisible();
        Unplaced = 0;
    }

    internal GameItem[] TakeVisible()
    {
        GameItem[] removed = placed.ToArray();
        foreach (GameItem item in removed)
            item.parentInventory = null;
        placed.Clear();
        return removed;
    }

    internal GameGridInventory NewGrid()
    {
        var grid = VisitFixtures.Empty<GameGridInventory>();
        var items = new List<GameItem>();
        AccessTools.Field(typeof(GameGridInventory), "items").SetValue(grid, items);
        AccessTools.PropertySetter(typeof(GameGridInventory), "inventoryShape").Invoke(grid, new object[] { shape });
        grids.Add(grid, items);
        return grid;
    }

    private void Prefix(MethodInfo method, string prefix)
        => fixture.Patch(method, prefix: new HarmonyMethod(typeof(CounterFixture), prefix));

    private static bool HasRoom(ref bool __result)
    {
        __result = current!.Placed < current.Capacity;
        return false;
    }

    private static bool MakeItem(string identifier, ref GameItem __result)
    {
        __result = VisitFixtures.Item(identifier);
        return false;
    }

    private static bool Place(GraphNodeStorage currentNode, GameItem item, ref int __result)
    {
        CounterFixture scene = current!;
        var target = (GameGridInventory)currentNode;
        var items = scene.grids[target];
        int capacity = target == scene.counter ? scene.Capacity : scene.ReserveCapacity;
        bool fits = items.Count < capacity;
        if (fits)
        {
            if (item.parentInventory is GameGridInventory source)
                scene.grids[source].Remove(item);
            item.parentInventory = target;
            items.Add(item);
        }
        __result = fits ? 0 : item.unitCount;
        return false;
    }

    private static bool DestroyUnplaced(GameItem item, ref bool __result)
    {
        __result = item.parentInventory != null;
        if (!__result)
        {
            current!.Unplaced++;
            item.unitCount = 0;
        }
        return false;
    }

    private static bool SkipUnplaced()
    {
        current!.Unplaced++;
        return false;
    }

    private static bool Destroy(GameItemElement __instance)
    {
        if (__instance.parentInventory is GameGridInventory source)
            current!.grids[source].Remove(__instance);
        __instance.parentInventory = null;
        __instance.unitCount = 0;
        return false;
    }

    private static bool Alive(ref bool __result)
    {
        __result = false;
        return false;
    }

    private static bool Skip() => false;

    public void Dispose()
    {
        fixture.UnpatchAll(fixture.Id);
        current = null;
    }
}
