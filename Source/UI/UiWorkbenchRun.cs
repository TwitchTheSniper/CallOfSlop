using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>The workbench opened at a Zombies workbench: which match, and which slot is being edited (0-3 guns, 4 melee).</summary>
public sealed class ZBenchSession
{
    public required Game Game;
    public int Slot;
}

/// <summary>
/// The workbench in a Zombies run (docs/zombies/ZOMBIES-MODE.md, "Workbench"): the same editor, but only with the parts
/// you own this run (your loose parts plus the ones on the slot's current build), your own slots instead of the saved
/// builds, standard builds as plans (with what you'd be missing), and scrap: break loose parts down, or craft parts up to
/// the tier the round allows, including every missing part of a build in one go. Apply sends the build's text to the
/// server, which checks the parts again (it is the authority) and puts it in your slot.
/// </summary>
public sealed partial class Ui
{
    /// <summary>Set while the workbench is open in a Zombies match (null: the main menu's sandbox workbench).</summary>
    public ZBenchSession? BenchRun;

    private BenchStock? _runStock;
    private int _runStockKey = int.MinValue;
    private double _runStockAt;

    /// <summary>Opens the workbench at a Zombies workbench, on the slot in your hands.</summary>
    public void OpenBench(Game game)
    {
        BenchRun = new ZBenchSession { Game = game, Slot = Math.Clamp(game.ActiveSlot, 0, 3) };
        WorkbenchOpen = true;
        BenchLoad(BenchRun.Slot);
    }

    /// <summary>Closes the Zombies workbench (back to the match).</summary>
    public void CloseBench()
    {
        BenchRun = null;
        WorkbenchOpen = false;
        Preview = null;
        CancelDrag();
    }

    private ZPlayer? BenchPlayer => BenchRun?.Game is { Local: { } me } g ? g.ZP(me) : null;

    private void BenchLoad(int slot)
    {
        if (BenchRun == null)
            return;
        BenchRun.Slot = slot;
        _wbMelee = slot == 4;
        _runStockKey = int.MinValue;
        var current = BenchPlayer?.Slots[slot];
        StartEditing(current?.Clone(), _wbMelee ? "Melee" : $"Slot {slot + 1}");
    }

    /// <summary>
    /// The run's parts for the slot being edited: loose parts plus the slot's current build (the server hands those back
    /// when the build is replaced). Rebuilt only when your parts or the slot change (a cheap fingerprint per frame).
    /// </summary>
    private BenchStock BenchRunStock()
    {
        if (BenchRun == null || BenchPlayer is not { } zp)
            return _runStock ??= BenchStock.Of([]);
        var slotBuild = zp.Slots[BenchRun.Slot];
        if (_runStock != null && _runStockKey != int.MinValue && _wbNow - _runStockAt < 0.25)
            return _runStock; // your parts change a few times a minute at most: look 4 times a second
        _runStockAt = _wbNow;
        var key = slotBuild == null ? 17 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(slotBuild);
        foreach (var (name, n) in zp.Parts)
            key = unchecked(key * 31 + name.Length * 131 + name[0] + n * 7);
        key = unchecked(key * 31 + BenchRun.Slot);
        if (key != _runStockKey || _runStock == null)
        {
            _runStockKey = key;
            _runStock = BenchStock.FromRun(zp.Parts, slotBuild, Parts.Find);
        }
        return _runStock;
    }

    private WorkbenchLaunch? DrawBenchRunSide(WeaponBuild b)
    {
        var run = BenchRun!;
        var game = run.Game;
        var zp = BenchPlayer;
        ImGui.Separator();
        ImGui.TextColored(Orange, "YOUR SLOTS (THIS RUN)"u8);
        var slots = game.Local is { ExtraSlots: true } ? 4 : 2;
        for (var i = 0; i < 5; i++)
        {
            if (i >= slots && i < 4)
                continue;
            var label = i == 4 ? $"Melee: {zp?.Slots[4]?.Name ?? "(none)"}##bslot4" : $"{i + 1}. {zp?.Slots[i]?.Name ?? "(a stock gun)"}##bslot{i}";
            if (ImGui.Selectable(label, run.Slot == i))
                BenchLoad(i);
            if (run.Slot == i)
                MissingBadge(Bm.Missing.Total);
        }
        var ready = Bm.CanFinalize;
        var missing = Bm.Missing;
        var scrap = zp?.Scrap ?? 0;
        if (missing.Total > 0 && Bm.Asm.Problems.Count == 0)
        {
            // Missing parts can be crafted with scrap (the workbench's economy), if the round allows their tier.
            var cap = game.CraftTierCap(game.ZRound);
            var cost = missing.CraftCost(cap);
            ImGui.BeginDisabled(cost < 0 || scrap < cost);
            var craft = ImGui.Button(cost >= 0 ? $"CRAFT MISSING & APPLY ({cost} scrap)" : "CRAFT MISSING (not possible)", new float2(-1, 34 * _scale));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGuiSafe.SetTooltip(missing.Text() + (cost < 0 ? $"\nSome of them can't be crafted (wonder parts, or above {PartDef.RarityName(cap)}: round {game.ZRound} crafts up to that tier)."
                    : scrap < cost ? $"\nNot enough scrap: you have {scrap}, it costs {cost}." : $"\nCrafts them for {cost} scrap, then puts the build in the slot."));
            if (craft && cost >= 0 && scrap >= cost)
            {
                foreach (var m in missing.Parts)
                    for (var k = 0; k < m.Count; k++)
                        game.BenchRequest(2, 0, m.Part.Name); // the server checks scrap and tier again
                game.BenchRequest(0, run.Slot, b.ToText());
                _runStockKey = int.MinValue;
                WbSay($"Crafted {missing.Total} part(s) for {cost} scrap and sent '{b.Name}' to {(run.Slot == 4 ? "the melee slot" : $"slot {run.Slot + 1}")}.");
            }
        }
        ImGui.BeginDisabled(!ready);
        if (ImGui.Button($"APPLY TO {(run.Slot == 4 ? "THE MELEE SLOT" : $"SLOT {run.Slot + 1}")}", new float2(-1, 34 * _scale)) && ready)
        {
            game.BenchRequest(0, run.Slot, b.ToText());
            WbSay($"Sent '{b.Name}' to slot {(run.Slot == 4 ? "melee" : (run.Slot + 1).ToString())}. Parts you took off go back to your loose parts.");
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGuiSafe.SetTooltip(ready ? "Puts the build in your slot (the server checks you own every part)." : Bm.FinalizeTip());
        if (ImGui.Button("Revert"u8))
            BenchLoad(run.Slot);
        DrawWorkbenchPresets(); // standard builds as plans, with what you'd be missing
        ImGui.Separator();

        ImGuiSafe.TextColored(Orange, $"SCRAP: {scrap}");
        if (ImGui.CollapsingHeader("Loose parts (break down for scrap)"u8, ImGuiTreeNodeFlags.None) && zp != null)
        {
            if (zp.Parts.Count == 0)
                ImGui.TextColored(Dim, "None yet: zombies drop parts, later rounds rarer ones."u8);
            foreach (var (name, n) in zp.Parts.ToList())
            {
                if (Parts.Find(name) is not { } part)
                    continue;
                ImGuiSafe.TextColored(WbCol(PartDef.RarityColor(part.Rarity)), $"{name} x{n}");
                ImGui.SameLine();
                if (ImGui.SmallButton($"scrap +{Scrap.SalvageValue(part)}##salv{name}"))
                    game.BenchRequest(1, 0, name);
            }
        }
        if (ImGui.CollapsingHeader("Craft a part (scrap)"u8, ImGuiTreeNodeFlags.None))
        {
            var cap = game.CraftTierCap(game.ZRound);
            ImGuiSafe.TextColored(Dim, $"Round {game.ZRound} crafts up to {PartDef.RarityName(cap)} parts.");
            var cat = _wbFilter >= 0 ? (PartCategory)_wbFilter : (PartCategory?)null;
            foreach (var part in Parts.All.Where(p => (cat == null || p.Category == cat) && p.IsMeleePart == _wbMelee && p.Rarity <= cap && Scrap.CraftCost(p) > 0))
            {
                var cost = Scrap.CraftCost(part);
                ImGui.BeginDisabled(scrap < cost);
                if (ImGui.SmallButton($"craft {part.Name} ({cost})##craft{part.Name}"))
                    game.BenchRequest(2, 0, part.Name);
                ImGui.EndDisabled();
            }
        }
        ImGui.TextColored(Dim, "The zombies don't wait while you work."u8);
        return null;
    }
}
