using System;
using System.Linq;
using static BetterCustomerStock.StockLanguage;

namespace BetterCustomerStock;

public sealed class ModEntry : IMod
{
    private StockPatches? patches;
    private StockSettings? settings;
    private ModLog? log;
    private ModManifest? manifest;
    private string manifestName = "";
    private string manifestDescription = "";

    public void Init(ModManifest manifest)
    {
        this.manifest = manifest;
        manifestName = manifest.Name;
        manifestDescription = manifest.Description;
        LocalizeManifest(manifest);
        log = new ModLog(manifest);
        patches = new StockPatches("mod." + manifest.ID, log);
        var targets = patches.FindTargets(StockRules.Methods);
        settings = new StockSettings(ModHelper.GetConfig(manifest.ID), targets.Select(target => target.Rule));
    }

    public void OnEnable()
    {
        if (patches == null || settings == null || StockRandom.Settings == settings)
            return;

        try
        {
            if (manifest != null)
                LocalizeManifest(manifest);
            int installed = patches.Install();
            StockRandom.Settings = settings;
            log?.Log(Line("已接入 {0} 个供货方法。", "Connected {0} stock methods.", installed));
        }
        catch (Exception)
        {
            patches.Remove();
            throw;
        }
    }

    public void OnDisable()
    {
        if (StockRandom.Settings == settings)
            StockRandom.Settings = null;
        patches?.Remove();
        if (manifest != null)
        {
            manifest.Name = manifestName;
            manifest.Description = manifestDescription;
        }
    }
}
