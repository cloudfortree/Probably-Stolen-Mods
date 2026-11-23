using System;
using System.Collections.Generic;
using HarmonyLib;

// Replace only the Unity grids in the native pool. Page logic is the game's own.
internal sealed class NativePagesFixture
{
    private readonly Type pager = typeof(StoreClient).Assembly.GetType("StockPageHelper", throwOnError: true);
    private readonly StoreClient client;
    private readonly CounterFixture counter;

    internal NativePagesFixture(VisitFixtures visit, CounterFixture counter)
    {
        client = visit.Client;
        this.counter = counter;
        AccessTools.Field(pager, "owner").SetValue(null, counter.Shop);
        var pool = (List<GameGridInventory>)AccessTools.Field(pager, "pool").GetValue(null);
        pool.Clear();
        int maxPages = (int)AccessTools.Field(pager, "MAX_PAGES").GetRawConstantValue();
        for (int i = 0; i < maxPages; i++)
            pool.Add(counter.NewGrid());
    }

    internal GameItem[] Held => ((List<GameItem>)Call("GetHeldItems", client)!).ToArray();
    internal int PageCount => (int)Call("GetPageCount", client)!;
    internal bool IsPagingFor(bool owned) => (bool)Call("IsPagingFor", owned)!;
    internal bool Turn(int offset) => (bool)Call("ShowPageByOffset", client, offset)!;
    internal void PurchaseDone() => Call("OnPurchaseDone", client);
    internal void Clear() => Call("Clear");

    internal void Reset()
    {
        Clear();
        counter.Clear();
    }

    private object? Call(string method, params object[] args)
        => AccessTools.Method(pager, method).Invoke(null, args);
}
