using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static BetterCustomerStock.StockLanguage;

namespace BetterCustomerStock;

internal static class StockSettingsWindow
{
    // Match the units used by the game's Options window and reuse its prefabs.
    private const float SliderWidth = 306f;
    private const float DetailsDropdownWidth = 440f;
    private const float CategorySliderWidth = 224f;
    private const float PreviewWidth = 178f;
    private const float ValueWidth = 56f;
    private const float QuantityValueWidth = 88f;
    private const float ApplyWidth = 180f;
    private const float SubheadingScale = 0.76f;
    private const string DetailsIcon = " <sprite name=\"info\" tint=1>";
    private const string SelectedCustomerKey = "selected_customer";
    internal static string WithDetailHint(string summary)
    {
        string hint = Text("点击查看原版与模组的概率明细。", "Click to see the detailed vanilla and mod probabilities.");
        return string.IsNullOrWhiteSpace(summary) ? hint : summary.Trim() + "\n\n" + hint;
    }

    private static readonly MethodInfo OpenMethod = AccessTools.DeclaredMethod(typeof(ModConfigWindow),
        nameof(ModConfigWindow.Open), new[] { typeof(LoadedMod) });
    private static readonly MethodInfo PopulateModList = AccessTools.DeclaredMethod(typeof(ModUIManager),
        nameof(ModUIManager.PopulateLayoutList));
    private static readonly MethodInfo TooltipEnter = AccessTools.DeclaredMethod(typeof(CustomUITooltipTrigger),
        nameof(CustomUITooltipTrigger.OnPointerEnter));
    private static readonly ConditionalWeakTable<CustomUITooltipTrigger, Func<string>> DetailTooltips = new();

    internal static void Install(Harmony harmony, ModLog log)
    {
        if (OpenMethod == null)
            return;

        try
        {
            if (PopulateModList != null)
                harmony.Patch(PopulateModList, prefix: new HarmonyMethod(typeof(StockSettingsWindow), nameof(RefreshModMetadata)));
            harmony.Patch(TooltipEnter, prefix: new HarmonyMethod(typeof(StockSettingsWindow), nameof(UpdateDetailsTooltip)));
            harmony.Patch(OpenMethod, postfix: new HarmonyMethod(typeof(StockSettingsWindow), nameof(AfterOpen)));
        }
        catch (Exception error)
        {
            log.Warning(Line("无法调整设置窗口的布局。{0}", "Could not adjust the settings window layout. {0}", error.Message));
        }
    }

    internal static void Remove(Harmony harmony)
    {
        if (OpenMethod != null)
            harmony.Unpatch(OpenMethod, HarmonyPatchType.Postfix, harmony.Id);
        if (PopulateModList != null)
            harmony.Unpatch(PopulateModList, HarmonyPatchType.Prefix, harmony.Id);
        if (TooltipEnter != null)
            harmony.Unpatch(TooltipEnter, HarmonyPatchType.Prefix, harmony.Id);
    }

    private static void RefreshModMetadata()
    {
        if (StockRandom.Settings is { } settings
            && ModLoader.Mods?.FirstOrDefault(mod => mod.Manifest.ID == settings.Config.ModId) is { } loaded)
            LocalizeManifest(loaded.Manifest);
    }

    private static void UpdateDetailsTooltip(CustomUITooltipTrigger __instance)
    {
        if (DetailTooltips.TryGetValue(__instance, out var describe))
            __instance.tooltipText = describe();
    }

    private static void AfterOpen(LoadedMod mod, CustomUIWindow __result)
    {
        var settings = StockRandom.Settings;
        var window = __result;
        if (settings == null || mod?.Manifest?.ID != settings.Config.ModId || window == null || !window.IsAlive)
            return;

        var manager = CustomUIManager.Instance;
        if (manager == null)
            return;

        window.SetTitle(ModName);
        window.Get("section_" + StockSettings.GlobalSection)?.SetText(GlobalSettings);
        window.Get("section_" + StockSettings.CustomerSection)?.SetText(CustomerSettings);
        var disabledStyle = DisabledControlStyle.Read(manager, window);
        var rowsByCustomer = new Dictionary<string, List<(GameObject Root, Action<bool>? SetSharedControl)>>();
        var numbers = new List<(CustomUIElement Slider, CustomUIElement Label)>();
        var globalSharedControls = new List<Action<bool>>();
        foreach (ConfigEntry entry in settings.Config.Entries)
        {
            var widget = window.Get(entry.Key);
            if (widget?.Rect?.parent is not RectTransform row)
                continue;

            if (entry.Key is StockSettings.CategoryKey or StockSettings.CategoryPositionsKey)
            {
                if (widget.input != null)
                {
                    widget.input.characterLimit = 0;
                    widget.SetString(entry.Serialize());
                }
                row.gameObject.SetActive(false);
                continue;
            }

            bool hasSlider = entry is ConfigEntry<int> { HasRange: true };
            var number = hasSlider ? AddNumber(manager, window, row, widget) : null;
            if (number != null)
                numbers.Add((widget, number));

            if (entry.Section == StockSettings.CustomerSection)
            {
                string customer = entry.Key.Substring(0, entry.Key.LastIndexOf('.'));
                if (!rowsByCustomer.TryGetValue(customer, out var rows))
                    rowsByCustomer[customer] = rows = new List<(GameObject, Action<bool>?)>();
                bool shared = hasSlider || entry.Key.EndsWith(".override", StringComparison.Ordinal);
                rows.Add((row.gameObject, shared
                    ? BindInteraction(disabledStyle, widget, window.Get("label_" + entry.Key)?.text, number?.text)
                    : null));
            }
            else if (entry.Key is "default.appearance" or "default.quantity" or StockSettings.InvertCategoryKey)
                globalSharedControls.Add(BindInteraction(disabledStyle, widget, window.Get("label_" + entry.Key)?.text, number?.text));
        }

        AddCustomerSelector(manager, window, settings, rowsByCustomer, disabledStyle);
        BindCurveDetails(manager, window, settings);

        var invert = window.Get(StockSettings.InvertCustomerKey);
        var allIndividual = window.Get(StockSettings.AllIndividualCategoriesKey);
        var labels = settings.Customers.Where(profile => !profile.FixedStock).Select(profile => (Profile: profile,
            Label: window.Get("label_" + profile.Id + ".enabled"),
            Tooltip: "modcfg:" + settings.Config.ModId + ":" + profile.Id + ".enabled")).ToArray();

        void RefreshCustomerSwitches()
        {
            if (invert == null)
                return;

            bool inverted = invert.GetBool();
            Func<string> tooltip = () => StockSettings.CustomerSwitchTip(inverted);
            foreach (var (profile, label, tipId) in labels)
            {
                label?.SetText(StockSettings.CustomerSwitchLabel(profile, inverted)
                    + $" <link=\"{tipId}\"><sprite name=\"info\" tint=1></link>");
                TMPIconHover.CustomLinks[tipId] = tooltip;
            }
        }

        // The game updates widgets without sending change events on Apply and Reset.
        void RefreshGlobalControls()
        {
            bool sharedControlsEnabled = allIndividual?.GetBool() != true;
            foreach (var setEnabled in globalSharedControls)
                setEnabled(sharedControlsEnabled);
        }

        void RefreshSettings()
        {
            foreach (var (slider, label) in numbers)
                label.SetText(FormatPosition(slider.GetFloat()));
            RefreshCustomerSwitches();
            RefreshGlobalControls();
        }

        invert?.OnToggle(_ => RefreshCustomerSwitches());
        allIndividual?.OnToggle(_ => RefreshGlobalControls());
        window.Get("reset")?.OnClick(RefreshSettings);
        window.Get("apply")?.SetFixedWidth(ApplyWidth).OnClick(RefreshSettings);
        RefreshGlobalControls();
        CustomUIBuilder.RebuildLayout(window.ContentRoot);
    }

    private static Action<bool> BindInteraction(DisabledControlStyle? style, CustomUIElement widget,
        TMP_Text? label, TMP_Text? readout = null)
        => style?.Bind(widget, label, readout) ?? (active => widget.SetInteractable(active));

    private static Action<bool>? BindForcedCheck(CustomUIManager manager, CustomUIWindow window, DisabledControlStyle? style,
        string key, CustomUIElement? stored)
    {
        if (stored?.Rect?.parent is not RectTransform row)
            return null;

        CustomUIElement? checkedView = null;
        Action<bool>? setViewInteraction = null;
        return forced =>
        {
            if (forced && checkedView == null)
            {
                // The original widget stays registered for reading the user's own setting on Apply and Reset.
                var builder = new CustomUIBuilder(manager, window, row);
                builder.AddToggle("", true);
                checkedView = builder.Last;
                checkedView.Rect.SetSiblingIndex(stored.Rect.GetSiblingIndex());
                setViewInteraction = BindInteraction(style, checkedView, window.Get("label_" + key)?.text);
            }

            stored.SetVisible(!forced);
            checkedView?.SetVisible(forced);
            setViewInteraction?.Invoke(!forced);
        };
    }

    private static CustomUIElement AddNumber(CustomUIManager manager, CustomUIWindow window, RectTransform row,
        CustomUIElement slider, float width = SliderWidth, Func<float, string>? format = null, float numberWidth = ValueWidth)
    {
        format ??= FormatPosition;
        var builder = new CustomUIBuilder(manager, window, row);
        builder.AddLabel(format(slider.GetFloat()));
        var number = builder.Last.SetFixedWidth(numberWidth);
        if (number.text != null)
        {
            number.text.alignment = TextAlignmentOptions.Right;
            number.text.enableWordWrapping = false;
            number.text.overflowMode = TextOverflowModes.Overflow;
        }
        number.Rect?.SetSiblingIndex(slider.Rect.GetSiblingIndex());
        slider.SetFixedWidth(width).OnSlider(value => number.SetText(format(value)));
        return number;
    }

    private static void AddCustomerSelector(CustomUIManager manager, CustomUIWindow window,
        StockSettings settings, Dictionary<string, List<(GameObject Root, Action<bool>? SetSharedControl)>> rowsByCustomer,
        DisabledControlStyle? disabledStyle)
    {
        var customers = IsChinese ? settings.Customers
            : settings.Customers.OrderBy(customer => customer.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var groups = customers.Where(customer => customer.FixedStock || rowsByCustomer.ContainsKey(customer.Id))
            .Select(customer =>
            {
                string individualKey = customer.Id + StockSettings.IndividualCategoriesSuffix;
                string selectionKey = customer.Id + StockSettings.SelectionEnabledSuffix;
                string specialKey = customer.Id + StockSettings.SpecialGoodsEnabledSuffix;
                var choices = settings.ChoicesFor(customer);
                var individual = settings.HasProductSettings(customer) ? window.Get(individualKey) : null;
                var selection = choices.Any(choice => choice.Quota == null) ? window.Get(selectionKey) : null;
                var special = choices.Any(choice => choice.Quota != null) ? window.Get(specialKey) : null;
                return (Customer: customer,
                    Rows: rowsByCustomer.TryGetValue(customer.Id, out var rows) ? rows : null,
                    Individual: individual,
                    Selection: selection,
                    SpecialGoods: special,
                    ShowForcedIndividual: BindForcedCheck(manager, window, disabledStyle, individualKey, individual),
                    ShowForcedSelection: BindForcedCheck(manager, window, disabledStyle, selectionKey, selection),
                    ShowForcedSpecialGoods: BindForcedCheck(manager, window, disabledStyle, specialKey, special));
            }).ToArray();
        var firstRow = groups.FirstOrDefault(group => group.Rows != null).Rows?[0].Root.transform;
        if (firstRow == null || firstRow.parent is not RectTransform content)
            return;

        var maskValue = window.Get(StockSettings.CategoryKey);
        var positionValue = window.Get(StockSettings.CategoryPositionsKey);
        var inlinePreview = window.Get(StockSettings.InlinePreviewKey);
        var allIndividual = window.Get(StockSettings.AllIndividualCategoriesKey);
        var allCategories = window.Get(StockSettings.AllStockCategoriesKey);
        var allSpecialGoods = window.Get(StockSettings.AllSpecialGoodsKey);
        var categories = maskValue?.input != null && positionValue?.input != null
            ? new CategoryList(manager, window, content, settings, maskValue, positionValue, window.Get(StockSettings.InvertCategoryKey), disabledStyle)
            : null;

        // Keep the dropdown preference out of the native stock settings' Apply and Reset actions.
        var preferences = ModHelper.GetConfig(settings.Config.ModId + ".ui");
        var lastCustomer = preferences.Get(SelectedCustomerKey) as ConfigEntry<string>
            ?? preferences.Define(SelectedCustomerKey, "", StockSettings.CustomerSection,
                () => Text("上次选中的顾客", "Last Selected Customer"));
        string selectedId = StockRules.LegacyInventorOutsideProfiles.FirstOrDefault(alias => alias.Outside == lastCustomer.Value).Shared
            ?? lastCustomer.Value;
        int initialIndex = Math.Max(0, Array.FindIndex(groups, group => group.Customer.Id == selectedId));
        int activeIndex = initialIndex;
        string changesWindowId = window.id + ":customer_changes";
        CustomUIWindow? changesWindow = null;
        void OpenSettingsChanges()
        {
            categories?.ReadDraft();
            changesWindow = ShowSettingsChanges(manager, window, settings, categories?.WindowDraft, changesWindowId);
        }

        void SelectCustomer(int index, bool remember = false)
        {
            if (index < 0 || index >= groups.Length)
                return;

            activeIndex = index;
            var selection = groups[index];
            bool forceIndividual = allIndividual?.GetBool() == true;
            bool individual = categories != null && selection.Individual != null && (forceIndividual || selection.Individual.GetBool());
            for (int group = 0; group < groups.Length; group++)
            {
                bool visible = group == index;
                var rows = groups[group].Rows;
                if (rows == null)
                    continue;

                foreach (var row in rows)
                    row.Root.SetActive(visible);
            }
            selection.ShowForcedIndividual?.Invoke(forceIndividual);
            selection.ShowForcedSelection?.Invoke(allCategories?.GetBool() == true);
            selection.ShowForcedSpecialGoods?.Invoke(allSpecialGoods?.GetBool() == true);
            categories?.Show(selection.Customer, individual, inlinePreview?.GetBool() == true);
            CustomUIBuilder.RebuildLayout(content);
            if (selection.Rows != null)
                foreach (var row in selection.Rows)
                    row.SetSharedControl?.Invoke(!individual);
            if (remember)
                lastCustomer.Set(selection.Customer.Id);
        }

        var selector = new CustomUIBuilder(manager, window, content);
        var heading = window.Get("section_" + StockSettings.CustomerSection);
        if (heading != null)
        {
            heading.SetText(CustomerSettings + DetailsIcon).SetTooltip("");
            BindDetails(manager, heading, () => Text("点击查看全局设置和顾客独立设置的修改。被替代的设置由当前配置方式接管。未生效的设置可能处于停用状态，或当前没有改变原版结果。",
                "Review changes to global and individual customer settings. Superseded settings give way to the active configuration. Inactive settings are disabled or currently have no effect on the result."),
                OpenSettingsChanges);
        }
        selector.BeginRow(4f, TextAnchor.MiddleLeft);
        selector.AddLabel(Text("顾客", "Customer"));
        window.onClose += () => manager.CloseWindow(changesWindowId);
        window.Get("apply")?.OnClick(() =>
        {
            if (changesWindow?.IsAlive == true)
                OpenSettingsChanges();
        });
        selector.AddFlexibleSpace();
        selector.AddDropdown(groups.Select(group => group.Customer.Label).ToArray(), initialIndex,
            index => SelectCustomer(index, remember: true));
        FitDropdownText(selector.Last.SetFixedWidth(SliderWidth), centered: true);
        selector.End();
        if (selector.Last.Rect?.parent is RectTransform selectorRow)
        {
            selectorRow.SetSiblingIndex(firstRow.GetSiblingIndex());
            SelectCustomer(initialIndex);
            foreach (var group in groups)
            {
                void RefreshActiveCustomer(bool _)
                {
                    if (groups[activeIndex].Customer == group.Customer)
                        SelectCustomer(activeIndex);
                }
                group.Individual?.OnToggle(RefreshActiveCustomer);
                group.Selection?.OnToggle(RefreshActiveCustomer);
                group.SpecialGoods?.OnToggle(RefreshActiveCustomer);
            }
            allIndividual?.OnToggle(_ => SelectCustomer(activeIndex));
            allCategories?.OnToggle(_ => SelectCustomer(activeIndex));
            allSpecialGoods?.OnToggle(_ => SelectCustomer(activeIndex));
            inlinePreview?.OnToggle(_ => SelectCustomer(activeIndex));
            window.Get("reset")?.OnClick(() =>
            {
                categories?.ReadDraft();
                SelectCustomer(activeIndex);
                if (changesWindow?.IsAlive == true)
                    OpenSettingsChanges();
            });
            if (categories != null)
            {
                foreach (ConfigEntry entry in settings.Config.Entries)
                {
                    if (entry.Key is StockSettings.InlinePreviewKey or StockSettings.AllIndividualCategoriesKey
                        or StockSettings.AllStockCategoriesKey or StockSettings.AllSpecialGoodsKey
                        || entry.Key.EndsWith(StockSettings.IndividualCategoriesSuffix, StringComparison.Ordinal)
                        || entry.Key.EndsWith(StockSettings.SelectionEnabledSuffix, StringComparison.Ordinal)
                        || entry.Key.EndsWith(StockSettings.SpecialGoodsEnabledSuffix, StringComparison.Ordinal))
                        continue;

                    var control = window.Get(entry.Key);
                    if (entry.ValueType == typeof(bool))
                        control?.OnToggle(_ => categories.RefreshPreviews());
                    else if (entry is ConfigEntry<int> { HasRange: true })
                        control?.OnSlider(_ => categories.RefreshPreviews());
                }
                window.Get("apply")?.OnClick(categories.RefreshAppliedDetails);
            }
        }
    }

    private static CustomUIWindow ShowSettingsChanges(CustomUIManager manager, CustomUIWindow parent, StockSettings settings,
        StockSettings.Draft? draft, string id)
    {
        var detail = CreateDetailWindow(manager, parent, id, Text("修改总览", "Settings Changes"));
        var globalChanges = settings.GlobalChanges(draft).ToLookup(change => change.Status, change => change.Line);
        var customerChanges = settings.CustomerChanges(draft).ToLookup(change => change.Status);
        var statuses = new[]
        {
            (StockChangeStatus.Effective, Text("已生效", "Effective")),
            (StockChangeStatus.Pending, Text("待应用", "Pending Apply")),
            (StockChangeStatus.Superseded, Text("被替代", "Superseded")),
            (StockChangeStatus.Inactive, Text("未生效", "Inactive"))
        };
        detail.BeginScrollFlexible();
        if (globalChanges.Count == 0 && customerChanges.Count == 0)
            AddDetailText(detail, Text("暂无设置修改。", "There are no settings changes."));

        foreach (var (status, title) in statuses)
        {
            bool hasGlobal = globalChanges.Contains(status);
            bool hasCustomers = customerChanges.Contains(status);
            if (!hasGlobal && !hasCustomers)
                continue;

            detail.AddHeader(title);
            StyleSubheading(detail.Last, 1.2f);
            if (detail.Last.text is TMP_Text heading)
            {
                heading.lineSpacing += 6f;
                detail.Last.SetPreferredSize(-1f, heading.preferredHeight + 4f);
            }
            detail.AddSeparator(null, 5);
            if (hasGlobal)
            {
                AddScopeHeading(GlobalSettings);
                AddLines(globalChanges[status]);
            }
            if (hasCustomers)
            {
                AddScopeHeading(CustomerSettings);
                foreach (var (profile, _, lines) in customerChanges[status])
                {
                    detail.AddSeparator(null, 3);
                    detail.AddHeader(profile.Label);
                    StyleSubheading(detail.Last);
                    AddLines(lines);
                }
            }
            detail.AddSpace(8f);
        }

        detail.End();
        AddDetailFooter(detail, () => manager.CloseWindow(id));
        var opened = detail.Show();
        CustomUIBuilder.RebuildLayout(opened.ContentRoot);
        return opened;

        void AddScopeHeading(string title)
        {
            detail.AddSpace(4f);
            detail.AddHeader(title);
            StyleSubheading(detail.Last, (1f + SubheadingScale) / 2f);
        }

        void AddLines(IEnumerable<string> lines)
        {
            foreach (string block in DetailTextBlocks(string.Join("\n", lines)))
                AddDetailText(detail, block);
        }
    }

    private static void BindCurveDetails(CustomUIManager manager, CustomUIWindow window, StockSettings settings)
    {
        var label = window.Get("label_" + StockSettings.LinearChangesKey);
        if (label?.root == null || label.text == null)
            return;

        string id = window.id + ":stock_curves";
        label.text.raycastTarget = true;
        BindDetailClick(label.root, () =>
        {
            if (TooltipManager._instance != null)
                TooltipManager._instance.HideTooltip();
            var detail = CreateDetailWindow(manager, window, id, Text("曲线计算", "Adjustment Curves"));
            detail.BeginScrollFlexible();
            AddDetailText(detail, StockSettings.CurveIntro(settings.UseLinearCurve()));
            foreach (bool linear in new[] { false, true })
            {
                detail.AddSeparator(null, 3);
                detail.AddHeader(StockSettings.CurveName(linear));
                AddDetailText(detail, StockSettings.CurveFormula(linear));
            }
            AddDetailText(detail, StockSettings.CurveExample());
            detail.AddSeparator(null, 3);
            detail.AddHeader(Line("{0}与{1}", "{0} and {1}", Classification, SpecialGoodsSettings));
            AddDetailText(detail, StockChoiceDetails.WeightCurveFormula(false));
            AddDetailText(detail, StockChoiceDetails.WeightModeNote(true));
            detail.End();
            AddDetailFooter(detail, () => manager.CloseWindow(id));
            CustomUIBuilder.RebuildLayout(detail.Show().ContentRoot);
        });
        window.onClose += () => manager.CloseWindow(id);
    }

    private static CustomUIBuilder CreateDetailWindow(CustomUIManager manager, CustomUIWindow parent, string id, string title)
    {
        var detail = manager.CreateWindow(id, title, parent.layerId, manager.optionUIPrefab)
            .UseMetaPrefabs(parent.useMetaPrefabs).SetCloseOnEscape(parent.closeOnEscape);
        CopyBackground(parent.root.GetComponent<Image>(), detail.Handle.root.GetComponent<Image>());
        CopyBackground(WindowPanel(parent), WindowPanel(detail.Handle));
        return detail;

        static void CopyBackground(Image? source, Image? target)
        {
            if (source == null || target == null)
                return;
            target.sprite = source.sprite;
            target.overrideSprite = source.overrideSprite == source.sprite ? null : source.overrideSprite;
            target.type = source.type;
            target.color = source.color;
            target.material = source.material;
        }
    }

    private static Image? WindowPanel(CustomUIWindow window)
        => window.frame != null ? window.frame.contentRoot?.parent?.GetComponent<Image>() : null;

    private static void BindDetails(CustomUIManager manager, CustomUIElement element, Func<string> tooltip, Action show)
    {
        var label = element.root;
        if (label == null || label.GetComponent<CustomUITooltipTrigger>() is not CustomUITooltipTrigger trigger)
            return;

        DetailTooltips.Add(trigger, tooltip);
        BindDetailClick(label, () =>
        {
            manager.HideTooltip(trigger);
            show();
        });
    }

    private static void BindDetailClick(GameObject label, Action show)
    {
        var open = label.AddComponent<Button>();
        open.transition = Selectable.Transition.None;
        open.navigation = new Navigation { mode = Navigation.Mode.None };
        open.onClick.AddListener(() => show());
    }

    private static CustomUIElement AddDetailText(CustomUIBuilder detail, string text)
    {
        detail.AddLabel(text);
        detail.Last.SetFlexible(1f, -1f);
        if (detail.Last.text != null)
        {
            detail.Last.text.enableWordWrapping = true;
            detail.Last.text.overflowMode = TextOverflowModes.Overflow;
        }
        return detail.Last;
    }

    private static void StyleSubheading(CustomUIElement element, float scale = SubheadingScale)
    {
        if (element.text is not TMP_Text heading)
            return;
        heading.fontSizeMin *= scale;
        heading.fontSizeMax *= scale;
        heading.fontSize *= scale;
        heading.enableWordWrapping = true;
    }

    private static void AddDetailFooter(CustomUIBuilder detail, Action close)
    {
        detail.AddSeparator(null, 2);
        detail.BeginRow(4f, TextAnchor.MiddleCenter).AddButton(Text("关闭", "Close"), close);
        detail.Last.SetFixedWidth(ApplyWidth);
        detail.End();
    }

    private static IEnumerable<string> DetailTextBlocks(string text)
    {
        // Bounded text blocks keep long lists within TextMeshPro's mesh limits.
        string[] lines = text.Split('\n');
        const int linesPerBlock = 24;
        for (int start = 0; start < lines.Length; start += linesPerBlock)
            yield return string.Join("\n", lines, start, Math.Min(linesPerBlock, lines.Length - start));
    }

    private sealed class DisabledControlStyle
    {
        private readonly Color backdrop;
        private readonly float opacity;
        private readonly Color spriteTint;

        private DisabledControlStyle(Color backdrop, Color buttonColor, float opacity)
        {
            this.backdrop = backdrop;
            this.opacity = opacity;
            var disabled = Color.Lerp(backdrop, buttonColor, opacity);
            spriteTint = new Color(Ratio(disabled.r, buttonColor.r), Ratio(disabled.g, buttonColor.g),
                Ratio(disabled.b, buttonColor.b), 1f);

            static float Ratio(float disabled, float normal) => normal > 0f ? Mathf.Clamp01(disabled / normal) : 1f;
        }

        internal static DisabledControlStyle? Read(CustomUIManager manager, CustomUIWindow window)
        {
            var prefab = manager.GetPrefab(CustomUIManager.ElementKind.Button, window.useMetaPrefabs);
            var panel = WindowPanel(window);
            var backdrop = window.root.GetComponent<Image>();
            if (backdrop == null && manager.optionUIPrefab != null)
                backdrop = manager.optionUIPrefab.GetComponent<Image>();
            bool solidPanel = panel != null && panel.overrideSprite == null && panel.color.a > 0f;
            if (prefab == null || (!solidPanel && backdrop == null))
                return null;

            // The options root provides the base RGB of its textured panel.
            Color background = solidPanel ? panel!.color : backdrop!.color;
            if (!solidPanel && panel != null && panel.color.a > 0f)
                background *= panel.color;

            // The mod browser disables its Workshop buttons with this native API.
            var sample = UnityEngine.Object.Instantiate(prefab);
            try
            {
                sample.SetActive(false);
                var button = CustomUIElement.Bind(sample, null).SetInteractable(false);
                var fade = sample.GetComponent<CanvasGroup>();
                var accent = window.Get("apply")?.text ?? button.text;
                return accent != null && fade != null && fade.alpha < 1f
                    ? new DisabledControlStyle(background, accent.color, fade.alpha) : null;
            }
            finally
            {
                UnityEngine.Object.Destroy(sample);
            }
        }

        internal Action<bool> Bind(CustomUIElement control, TMP_Text? label = null, TMP_Text? readout = null)
        {
            Selectable? selectable = (Selectable?)control.slider ?? control.toggle;
            var colors = new List<(Graphic Graphic, Color Original, Color Disabled)>();
            Graphic? checkbox = null;
            Image? checkboxBacking = null;
            if (selectable != null && (selectable.transition == Selectable.Transition.None
                || (selectable.transition == Selectable.Transition.SpriteSwap && selectable.spriteState.disabledSprite == null)))
            {
                // A color-only tint keeps the slider handle opaque over its track and fill.
                if (control.slider != null)
                    foreach (Image graphic in control.slider.GetComponentsInChildren<Image>(includeInactive: true))
                        colors.Add((graphic, graphic.color, graphic.color * spriteTint));
                else
                {
                    if (selectable.targetGraphic is Graphic box)
                    {
                        // The native checkbox sprite contains the border and the opaque panel color.
                        // Blend it over a matching backing so the border fades and the panel color stays intact.
                        checkbox = box;
                        AddFadedImage(box);
                    }
                    if (control.toggle?.graphic is Graphic mark && mark != selectable.targetGraphic)
                        AddFadedImage(mark);
                }
            }
            AddText(label);
            AddText(readout);

            return active =>
            {
                control.SetInteractable(active);
                foreach (var (graphic, original, disabled) in colors)
                    graphic.color = active ? original : disabled;
                if (!active && checkbox != null)
                {
                    if (checkboxBacking == null)
                        checkboxBacking = AddCheckboxBacking(checkbox.rectTransform);
                    AlignBacking(checkboxBacking.rectTransform, checkbox.rectTransform);
                }
                if (checkboxBacking != null)
                    checkboxBacking.gameObject.SetActive(!active);
            };

            void AddFadedImage(Graphic graphic)
            {
                var disabled = graphic.color;
                disabled.a *= opacity;
                colors.Add((graphic, graphic.color, disabled));
            }

            void AddText(TMP_Text? text)
            {
                if (text == null)
                    return;
                var disabled = Color.Lerp(backdrop, text.color, opacity);
                disabled.a = text.color.a;
                colors.Add((text, text.color, disabled));
            }
        }

        private Image AddCheckboxBacking(RectTransform box)
        {
            var backing = new GameObject("Disabled Checkbox Backing", typeof(RectTransform));
            backing.SetActive(false);
            var rect = (RectTransform)backing.transform;
            rect.SetParent(box.parent, false);
            rect.SetSiblingIndex(box.GetSiblingIndex());
            if (box.parent.GetComponent<LayoutGroup>() != null)
                backing.AddComponent<LayoutElement>().ignoreLayout = true;
            var image = backing.AddComponent<Image>();
            image.raycastTarget = false;
            image.color = new Color(backdrop.r, backdrop.g, backdrop.b, 1f);
            return image;
        }

        private static void AlignBacking(RectTransform rect, RectTransform box)
        {
            rect.anchorMin = box.anchorMin;
            rect.anchorMax = box.anchorMax;
            rect.pivot = box.pivot;
            rect.sizeDelta = box.sizeDelta;
            rect.anchoredPosition3D = box.anchoredPosition3D;
            rect.localScale = box.localScale;
            rect.localRotation = box.localRotation;
        }
    }

    private static void FitDropdownText(CustomUIElement element, bool centered = false)
    {
        if (element.dropdown is not TMP_Dropdown dropdown)
            return;

        FitText(dropdown.captionText, centered);
        FitText(dropdown.itemText, centered);
        var scroll = dropdown.template?.GetComponent<ScrollRect>();
        if (dropdown.itemText != null && scroll?.verticalScrollbar != null
            && scroll.verticalScrollbarVisibility != ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport)
        {
            var margin = dropdown.itemText.margin;
            margin.z += scroll.verticalScrollbar.GetComponent<RectTransform>().rect.width;
            dropdown.itemText.margin = margin;
        }

        static void FitText(TMP_Text label, bool centered)
        {
            if (label == null)
                return;

            label.fontSizeMax = label.fontSize;
            label.fontSizeMin = label.fontSize * 0.6f;
            label.enableAutoSizing = true;
            label.enableWordWrapping = false;
            if (centered)
                label.horizontalAlignment = HorizontalAlignmentOptions.Center;
        }
    }

    private sealed class CategoryList
    {
        private sealed class DetailSection
        {
            internal readonly string Heading;
            internal readonly Func<int, string> Summary;
            internal readonly Func<int, string> Probabilities;

            internal DetailSection(string heading, Func<int, string> summary, Func<int, string> probabilities)
            {
                Heading = heading;
                Summary = summary;
                Probabilities = probabilities;
            }
        }

        private readonly CustomUIManager manager;
        private readonly CustomUIWindow window;
        private readonly StockSettings settings;
        private readonly DisabledControlStyle? disabledStyle;
        private readonly CustomUIElement maskValue;
        private readonly CustomUIElement positionValue;
        private readonly CustomUIElement? invert;
        private readonly RectTransform container;
        private readonly string detailWindowId;
        private readonly StockCategoryMask maskDraft;
        private readonly StockCategoryPositions positionDraft;
        private readonly StockSettings.Draft draft;
        private readonly List<Action> refreshPreviews = new List<Action>();
        private Action? refreshOpenDetails;
        private bool individual;
        private bool showPreview;
        private CustomUIElement? tip;
        private CustomUIElement? applyNotice;
        private bool applyBlocked;
        internal StockSettings.Draft WindowDraft => draft;

        internal CategoryList(CustomUIManager manager, CustomUIWindow window, RectTransform content, StockSettings settings,
            CustomUIElement maskValue, CustomUIElement positionValue, CustomUIElement? invert, DisabledControlStyle? disabledStyle)
        {
            this.manager = manager;
            this.window = window;
            this.settings = settings;
            this.disabledStyle = disabledStyle;
            this.maskValue = maskValue;
            this.positionValue = positionValue;
            this.invert = invert;
            detailWindowId = window.id + ":stock_details";
            maskDraft = new StockCategoryMask(maskValue.GetString());
            positionDraft = new StockCategoryPositions(positionValue.GetString());
            draft = new StockSettings.Draft(
                key => window.Get(key)?.GetBool(),
                key => window.Get(key)?.GetFloat() is float value ? Mathf.RoundToInt(value) : null,
                maskDraft, positionDraft);

            var builder = new CustomUIBuilder(manager, window, content);
            builder.BeginColumn(4f, TextAnchor.UpperLeft);
            container = builder.CurrentContainer;
            new CustomUIElement { root = container.gameObject }.SetFlexible(1f, -1f);
            builder.End();
            invert?.OnToggle(_ => RefreshTip());
            window.onClose += () => manager.CloseWindow(detailWindowId);
        }

        internal void Show(StockProfile profile, bool individual, bool showPreview)
        {
            this.individual = individual;
            this.showPreview = showPreview;
            manager.CloseWindow(detailWindowId);
            CustomUIBuilder.ClearContainer(container);
            refreshPreviews.Clear();
            tip = null;
            applyNotice = null;

            var builder = new CustomUIBuilder(manager, window, container);
            builder.BeginRow(4f, TextAnchor.MiddleLeft).AddHeader(ProductCategories).End();
            builder.AddSeparator(null, 2);
            if (profile.FixedStockNotice is { } notice)
            {
                builder.BeginRow(4f, TextAnchor.MiddleLeft).AddLabel(notice()).End();
            }
            else
            {
                AddChoices(builder, profile, special: false);
                AddChoices(builder, profile, special: true);
                if (settings.HasProductSettings(profile))
                {
                    builder.BeginRow(4f, TextAnchor.MiddleLeft).AddLabel("").End();
                    tip = builder.Last;
                    RefreshTip();
                    AddGroup(builder, profile, StockAxis.Appearance, Appearance);
                    AddGroup(builder, profile, StockAxis.Quantity, Quantity);
                }
            }
            builder.AddLabel("");
            applyNotice = builder.Last;
            RefreshPreviews();
        }

        private void AddGroup(CustomUIBuilder builder, StockProfile profile, StockAxis axis, string title)
        {
            var categoryIds = settings.CategoriesFor(profile, axis);
            if (categoryIds.Count == 0)
                return;

            builder.AddSpace(4f);
            builder.BeginRow(4f, TextAnchor.MiddleLeft).AddHeader(title).End();
            if (axis == StockAxis.Appearance && settings.OptionalOfferSummary(profile) != null)
            {
                string offers = GameTerm("UI/ui_nego_title_barter");
                builder.BeginRow(4f, TextAnchor.MiddleLeft).AddHeader(offers + DetailsIcon).WithTooltip("");
                StyleSubheading(builder.Last);
                BindDetails(builder.Last, () => settings.OptionalOfferSummary(profile) ?? "", () => ShowDetails(profile, offers,
                    new[] { new DetailSection(Text("生成明细", "Generation Details"), _ => settings.OfferDetails(profile)?.Summary ?? "",
                        _ => settings.OfferDetails(profile)?.Offers ?? "") }));
                builder.End();
            }
            foreach (string id in categoryIds)
            {
                builder.BeginRow(4f, TextAnchor.MiddleLeft);
                builder.AddLabel(StockCatalog.Name(id) + DetailsIcon).WithTooltip("");
                var label = builder.Last;
                BindDetails(label, () => settings.ProductDetails(profile, axis, id), () => ShowProductDetails(profile, id));
                var result = BeginOptionControls(builder, label);
                if (result != null)
                    refreshPreviews.Add(() => result.SetText(settings.ProductComparison(profile, axis, id, draft)));
                if (individual)
                    AddCategorySlider(builder, profile.Id, axis, id);
                else
                {
                    builder.AddToggle("", maskDraft.IsChecked(profile.Id, axis, id), isChecked =>
                    {
                        maskDraft.SetChecked(profile.Id, axis, id, isChecked);
                        maskValue.SetString(maskDraft.Save());
                        RefreshPreviews();
                    });
                }
                if (result != null)
                    builder.End();
                builder.End();
            }
        }

        private CustomUIElement? BeginOptionControls(CustomUIBuilder builder, CustomUIElement label)
        {
            if (!showPreview)
            {
                builder.AddFlexibleSpace();
                return null;
            }

            label.SetFlexible(1f, -1f);
            if (label.text != null)
                label.text.enableWordWrapping = true;
            builder.AddLabel("");
            var result = builder.Last.SetFixedWidth(PreviewWidth);
            if (result.text != null)
            {
                result.text.alignment = TextAlignmentOptions.Right;
                result.text.enableWordWrapping = false;
                result.text.overflowMode = TextOverflowModes.Ellipsis;
                result.text.fontSizeMax = result.text.fontSize;
                result.text.fontSizeMin = result.text.fontSize * 0.6f;
                result.text.enableAutoSizing = true;
            }
            builder.BeginRow(4f, TextAnchor.MiddleRight);
            new CustomUIElement { root = builder.CurrentContainer.gameObject }.SetFixedWidth(ValueWidth + 4f + CategorySliderWidth);
            return result;
        }

        private void BindDetails(CustomUIElement element, Func<string> summary, Action show)
            => StockSettingsWindow.BindDetails(manager, element, () => WithDetailHint(summary()), show);

        private void ShowProductDetails(StockProfile profile, string category)
        {
            var sections = new List<DetailSection>();
            AddProductSection(StockAxis.Appearance, Appearance);
            AddProductSection(StockAxis.Quantity, Quantity);
            var choices = settings.ChoicesForProductDetails(profile, category);
            foreach (StockChoice choice in choices)
                sections.Add(ChoiceSection(profile, choice, choice.ProductCategory == category ? choice.SettingsSection
                    : choice.SettingsSection + " · " + choice.Label));

            string name = choices.FirstOrDefault(choice => choice.ProductCategory == category)?.Label ?? StockCatalog.Name(category);
            ShowDetails(profile, name, sections, choices.FirstOrDefault(choice => choice.Kind == StockChoiceKind.Backpack));

            void AddProductSection(StockAxis axis, string heading)
            {
                if (settings.CategoriesFor(profile, axis).Contains(category))
                    sections.Add(new DetailSection(heading, _ => settings.ProductDetails(profile, axis, category),
                        _ => settings.ProductProbabilityDetails(profile, axis, category)));
            }
        }

        private DetailSection ChoiceSection(StockProfile profile, StockChoice choice, string heading)
        {
            return new DetailSection(heading, page =>
            {
                string note = StockChoiceDetails.Summary(profile, choice);
                if (settings.UsesExactCounts(profile, choice))
                    return note + "\n\n" + StockAllocation.QuantityTip;
                string weights = StockChoiceDetails.DescribeReferenceWeights(profile, choice, ChoicePositions(profile, choice),
                    choice.Kind == StockChoiceKind.Backpack ? page : null, settings.UseLinearCurve());
                if (choice.Quota != null)
                    note += "\n\n" + StockChoiceDetails.WeightModeNote(settings.UseLinearCurve());
                return note.Length == 0 ? weights : note + "\n\n" + weights;
            }, page =>
            {
                if (settings.UsesExactCounts(profile, choice))
                    return StockChoiceDetails.AllocationDetails(profile, choice, settings.Allocation(profile, choice),
                        branch => settings.StockCount(profile, choice, branch), settings.ChoiceEnabled(profile, choice));
                var appearance = choice.Id == "crate" && choice.ProductCategory is string category
                    ? settings.ProductAppearanceScenarios(profile, category) : null;
                return StockChoiceDetails.DescribeProbabilities(profile, choice, ChoicePositions(profile, choice), true,
                    appearance, page, settings.UseLinearCurve());
            });
        }

        private int[] ChoicePositions(StockProfile profile, StockChoice choice, StockSettings.Draft? source = null)
            => settings.GetChoicePositions(profile, choice, source) ?? new int[choice.Categories.Length];

        private void ShowDetails(StockProfile profile, string name, IReadOnlyList<DetailSection> sections,
            StockChoice? backpack = null, int? requestedPage = null)
        {
            var detail = CreateDetailWindow(manager, window, detailWindowId, Text("货物详情", "Stock Details"))
                .OnClose(() => refreshOpenDetails = null);
            int page = 0;
            if (backpack != null)
            {
                page = requestedPage ?? StockChoiceDetails.CurrentBackpackPage(backpack);
                detail.BeginRow(4f, TextAnchor.MiddleLeft).AddLabel(Text("持有情况", "Ownership"));
                detail.AddDropdown(StockChoiceDetails.BackpackPages(backpack), page,
                    selected => ShowDetails(profile, name, sections, backpack, selected));
                FitDropdownText(detail.Last.SetPreferredSize(DetailsDropdownWidth, 0f).SetFlexible(1f, -1f));
                detail.End();
            }
            detail.BeginScrollFlexible();
            AddDetailText(detail, profile.Label);
            AddDetailText(detail, $"<b>{name}</b>" + (sections.Count == 1 ? $"　{sections[0].Heading}" : ""));
            if (settings.StockDetailsNotice(profile) is { } notice)
                AddDetailText(detail, notice);
            var sectionElements = new List<(DetailSection Section, CustomUIElement Summary, CustomUIElement[] Blocks)>();
            foreach (DetailSection section in sections)
            {
                detail.AddSeparator(null, 3);
                if (sections.Count > 1)
                    AddDetailText(detail, $"<b>{section.Heading}</b>");
                var summary = AddDetailText(detail, section.Summary(page));
                detail.AddSeparator(null, 3);
                sectionElements.Add((section, summary,
                    DetailTextBlocks(section.Probabilities(page)).Select(text => AddDetailText(detail, text)).ToArray()));
            }
            detail.End();
            AddDetailFooter(detail, () => manager.CloseWindow(detailWindowId));
            var detailWindow = detail.Show();
            CustomUIBuilder.RebuildLayout(detailWindow.ContentRoot);
            refreshOpenDetails = () =>
            {
                if (!detailWindow.IsAlive)
                    return;
                foreach (var (section, summary, blocks) in sectionElements)
                {
                    summary.SetText(section.Summary(page));
                    string[] fresh = DetailTextBlocks(section.Probabilities(page)).ToArray();
                    if (fresh.Length != blocks.Length)
                    {
                        ShowDetails(profile, name, sections, backpack, page);
                        return;
                    }
                    for (int index = 0; index < blocks.Length; index++)
                        blocks[index].SetText(fresh[index]);
                }
                CustomUIBuilder.RebuildLayout(detailWindow.ContentRoot);
            };
        }

        private void AddChoices(CustomUIBuilder builder, StockProfile profile, bool special)
        {
            var choices = settings.ChoicesFor(profile).Where(choice => (choice.Quota != null) == special).ToArray();
            if (choices.Length == 0)
                return;

            bool enabled = settings.ChoiceModeSelected(profile, special, draft);
            builder.AddSpace(4f);
            builder.BeginRow(4f, TextAnchor.MiddleLeft).AddHeader(choices[0].SettingsSection).End();
            foreach (StockChoice choice in choices)
            {
                builder.AddSpace(4f);
                builder.BeginRow(4f, TextAnchor.MiddleLeft).AddHeader(choice.Label + DetailsIcon).WithTooltip("");
                var detail = builder.Last;
                StyleSubheading(detail);
                string Summary()
                {
                    string note = StockChoiceDetails.HoverSummary(profile, choice);
                    return settings.UsesExactCounts(profile, choice, draft)
                        ? note + "\n\n" + StockAllocation.QuantityTip
                        : note;
                }
                BindDetails(detail, Summary, () =>
                {
                    if (choice.Quota == null && choice.ProductCategory is string category && settings.HasProductRow(profile, category))
                        ShowProductDetails(profile, category);
                    else
                        ShowDetails(profile, choice.Label, new[] { ChoiceSection(profile, choice, choice.SettingsSection) },
                            choice.Kind == StockChoiceKind.Backpack ? choice : null);
                });
                builder.End();
                if (choice.Quota != null)
                {
                    bool exact = settings.UsesExactCounts(profile, choice, draft);
                    builder.BeginRow(4f, TextAnchor.MiddleLeft);
                    builder.AddLabel(StockAllocation.ModeLabel).WithTooltip(StockAllocation.QuantityTip);
                    var label = builder.Last;
                    builder.AddFlexibleSpace();
                    builder.AddToggle("", exact, value =>
                    {
                        positionDraft.Set(profile.Id, StockAxis.Allocation, choice.Id, value ? 1 : 0);
                        positionValue.SetString(positionDraft.Save());
                        Show(profile, individual, showPreview);
                        CustomUIBuilder.RebuildLayout(window.ContentRoot);
                    });
                    var toggle = builder.Last;
                    if (disabledStyle != null)
                        disabledStyle.Bind(toggle, label.text)(enabled);
                    else
                        toggle.SetInteractable(enabled);
                    builder.End();
                    if (exact)
                    {
                        AddAllocationSliders(builder, profile, choice, enabled);
                        continue;
                    }
                }
                var results = showPreview ? new List<CustomUIElement>(choice.Categories.Length) : null;
                for (int branch = 0; branch < choice.Categories.Length; branch++)
                {
                    string category = choice.Categories[branch];
                    bool hasProductDetail = settings.HasProductRow(profile, category);
                    builder.BeginRow(4f, TextAnchor.MiddleLeft);
                    builder.AddLabel(StockCatalog.Name(category) + (hasProductDetail ? DetailsIcon : ""));
                    var label = builder.Last;
                    if (hasProductDetail)
                    {
                        builder.WithTooltip("");
                        BindDetails(label, Summary, () => ShowProductDetails(profile, category));
                    }
                    var result = BeginOptionControls(builder, label);
                    AddCategorySlider(builder, profile.Id, StockAxis.Selection, choice.Key(branch), enabled);
                    if (result != null)
                    {
                        results!.Add(result);
                        builder.End();
                    }
                    builder.End();
                }
                if (results != null)
                    refreshPreviews.Add(() =>
                    {
                        var values = StockChoiceDetails.ProbabilityPreview(profile, choice, ChoicePositions(profile, choice, draft),
                            true, settings.UseLinearCurve(draft));
                        for (int index = 0; index < results.Count; index++)
                            results[index].SetText(values[index]);
                    });
            }
            builder.AddSpace(4f);
        }

        private void AddAllocationSliders(CustomUIBuilder builder, StockProfile profile, StockChoice choice, bool enabled)
        {
            builder.AddLabel("");
            var summary = builder.Last;
            StockQuota quota = choice.Quota!;
            var sliders = new List<(string Id, Slider Control, Action<bool> SetEnabled)>(choice.Categories.Length);
            for (int branch = 0; branch < choice.Categories.Length; branch++)
            {
                string id = choice.Categories[branch];
                string key = choice.Key(branch);
                int count = settings.StockCount(profile, choice, branch, draft);
                int limit = quota.LimitFor(id);
                int sliderCount = Mathf.Clamp(count, StockAllocation.Random, limit);
                builder.BeginRow(4f, TextAnchor.MiddleLeft);
                builder.AddLabel(StockCatalog.Name(id));
                builder.AddFlexibleSpace();
                if (limit < quota.Total)
                    builder.AddLabel(Line("最多 {0} 件", "Max {0}", limit));
                builder.AddSlider(StockAllocation.Random, limit, sliderCount, value =>
                {
                    positionDraft.Set(profile.Id, StockAxis.Allocation, key, Mathf.RoundToInt(value) + 1);
                    positionValue.SetString(positionDraft.Save());
                    RefreshPreviews();
                }, wholeNumbers: true);
                var slider = builder.Last;
                var number = AddCategoryReadout(builder, slider,
                    value => StockAllocation.DescribeSliderCount(Mathf.RoundToInt(value)), QuantityValueWidth);
                if (slider.slider != null)
                    sliders.Add((id, slider.slider, BindInteraction(disabledStyle, slider, null, number.text)));
                // Keep a legacy out-of-range value visible until this slider is edited.
                if (sliderCount != count)
                    number.SetText(StockAllocation.DescribeSliderCount(count));
                builder.End();
            }
            refreshPreviews.Add(() =>
            {
                StockAllocation allocation = settings.Allocation(profile, choice, draft);
                foreach (var (id, slider, setEnabled) in sliders)
                {
                    int limit = allocation.EditableLimit(id);
                    slider.maxValue = limit;
                    setEnabled(enabled && limit > 0);
                }
                string message = allocation.ValidationError(StockChoiceDetails.EligibleCandidates(profile, choice)) ?? allocation.Summary();
                summary.SetText(message);
            });
        }

        private void AddCategorySlider(CustomUIBuilder builder, string profile, StockAxis axis, string key, bool enabled = true)
        {
            builder.AddSlider(-StockCurve.Limit, StockCurve.Limit, positionDraft.Get(profile, axis, key), value =>
            {
                positionDraft.Set(profile, axis, key, Mathf.RoundToInt(value));
                positionValue.SetString(positionDraft.Save());
                RefreshPreviews();
            }, wholeNumbers: true);
            var slider = builder.Last;
            var number = AddCategoryReadout(builder, slider);
            if (!enabled && disabledStyle != null)
                disabledStyle.Bind(slider, readout: number.text)(false);
            else
                slider.SetInteractable(enabled);
        }

        private CustomUIElement AddCategoryReadout(CustomUIBuilder builder, CustomUIElement slider,
            Func<float, string>? format = null, float numberWidth = ValueWidth)
        {
            float width = CategorySliderWidth + ValueWidth - numberWidth;
            return AddNumber(manager, window, builder.CurrentContainer, slider, width, format, numberWidth);
        }

        internal void RefreshPreviews()
        {
            foreach (Action refresh in refreshPreviews)
                refresh();
            string? error = settings.AllocationError(draft);
            applyNotice?.SetText(error ?? "").SetVisible(error != null);
            bool blocked = error != null;
            if (blocked != applyBlocked)
            {
                window.Get("apply")?.SetInteractable(!blocked);
                applyBlocked = blocked;
            }
        }

        internal void RefreshAppliedDetails()
        {
            RefreshPreviews();
            refreshOpenDetails?.Invoke();
        }

        internal void ReadDraft()
        {
            // Reset writes defaults to both hidden native inputs before the selected customer is redrawn.
            maskDraft.Load(maskValue.GetString());
            positionDraft.Load(positionValue.GetString());
        }

        private void RefreshTip() => tip?.SetText(individual
            ? Text("0 沿用原版。共用一次抽选的货物需设为同一档位，才能调整这次抽选。",
                "A setting of 0 uses the vanilla probability. Goods that share a roll need the same setting for that roll to be adjusted.")
            : StockSettings.CategorySwitchTip(invert?.GetBool() == true));
    }

    internal static string FormatPosition(float value)
        => StockSettings.FormatPosition(Mathf.RoundToInt(value));
}
