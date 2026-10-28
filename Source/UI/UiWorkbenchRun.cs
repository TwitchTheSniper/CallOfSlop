using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>The workbench opened at a Zombies workbench: which match, and which slot is being edited (0-3 guns, 4 melee, 5 the grenade).</summary>
public sealed class ZBenchSession
{
    public required Game Game;
    public int Slot;
}

/// <summary>
/// The workbench in a Zombies run (docs/zombies/ZOMBIES-MODE.md, "Workbench"): the same editor, but only with the parts
/// you own this run (your loose parts plus the ones on the slot's current build), your own slots instead of the saved
/// builds, standard builds as plans (with what you'd be missing), and your loose parts (Zombies has no scrap since
/// 2026-10-27: parts come from zombie drops, salvage piles, the slot machines and taking builds apart). Apply sends the build's text to the
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
        _disUndo = null;
        _disArmedSlot = -1;
        WorkbenchOpen = true;
        BenchLoad(BenchRun.Slot);
    }

    /// <summary>Closes the Zombies workbench (back to the match).</summary>
    public void CloseBench()
    {
        BenchRun = null;
        _disUndo = null; // Undo disassemble only lasts while the bench is open
        _disArmedSlot = -1;
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
        _wbKind = Game.SlotKind(slot);
        _runStockKey = int.MinValue;
        var current = BenchPlayer?.Slots[slot];
        StartEditing(current?.Clone(), SlotTitle(slot));
    }

    /// <summary>
    /// The run's parts for the slot being edited: loose parts plus the slot's current build (the server hands those back
    /// when the build is replaced). Rebuilt only when your parts or the slot change (a cheap fingerprint per frame).
    /// </summary>
    /// <summary>A new build's name for a slot: "Slot 2", "Melee", "Grenade".</summary>
    private static string SlotTitle(int slot) => slot == Game.MeleeSlot ? "Melee" : slot == Game.GrenadeSlot ? "Grenade" : $"Slot {slot + 1}";

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
        for (var i = 0; i < Game.BuildSlots; i++)
        {
            if (i >= slots && i < 4)
                continue;
            var none = i < 4 && game.Local != null && !Game.HasGunIn(game.Local, i) ? "(empty)" : "(a stock gun)"; // empty: taken apart
            var label = i == 4 ? $"Melee: {zp?.Slots[4]?.Name ?? "(none)"}##bslot4"
                : i == Game.GrenadeSlot ? $"Grenade: {zp?.Slots[i]?.Name ?? "(the stock frag)"}##bslot5"
                : $"{i + 1}. {zp?.Slots[i]?.Name ?? none}##bslot{i}";
            if (ImGui.Selectable(label, run.Slot == i))
                BenchLoad(i);
            if (run.Slot == i)
                MissingBadge(Bm.Missing.Total);
        }
        var ready = Bm.CanFinalize;
        ImGui.BeginDisabled(!ready);
        if (ImGui.Button($"APPLY TO {(run.Slot == 4 ? "THE MELEE SLOT" : run.Slot == Game.GrenadeSlot ? "THE GRENADE SLOT" : $"SLOT {run.Slot + 1}")}", new float2(-1, 34 * _scale)) && ready)
        {
            game.BenchRequest(0, run.Slot, b.ToText());
            WbSay($"Sent '{b.Name}' to slot {Game.SlotWord(run.Slot)}. Parts you took off go back to your loose parts.");
        }
        ImGui.EndDisabled();
        WbRefusedClick(ready); // a click on the greyed-out Apply: red rings on the empty sockets that need a part
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            WbTip(ready ? "Puts the build in your slot (the server checks you own every part)." : Bm.FinalizeTip());
        if (ImGui.Button("Revert"u8))
            BenchLoad(run.Slot);
        DrawBlueprints(b); // your blueprint library: loads with only the parts you own (UiWorkbenchBlueprints.cs)
        DrawWorkbenchPresets(); // standard builds as plans, with what you'd be missing
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Loose parts"u8, ImGuiTreeNodeFlags.None) && zp != null)
        {
            if (zp.Parts.Count == 0)
                ImGui.TextColored(Dim, "None yet: zombies drop parts (later rounds rarer ones); so do salvage piles and the slot machines."u8);
            foreach (var (name, n) in zp.Parts)
                if (Parts.Find(name) is { } part)
                    ImGuiSafe.TextColored(WbCol(PartDef.RarityColor(part.Rarity)), $"{name} x{n}");
        }
        ImGui.TextColored(Dim, "The zombies don't wait while you work."u8);
        return null;
    }

    // ---- Disassemble: the slot's whole build into your loose parts (docs/zombies/ZOMBIES-MODE.md) ----------------

    /// <summary>How long the first click (or D) on Disassemble stays armed waiting for the confirming second one.</summary>
    private const double DisarmSeconds = 4;

    private int _disArmedSlot = -1;
    private double _disArmedAt = -100;

    /// <summary>The build taken apart last (its slot and a copy), for one Undo while the bench stays open.</summary>
    private (int Slot, WeaponBuild Build)? _disUndo;

    private bool DisassembleArmed(int slot) => _disArmedSlot == slot && _wbNow - _disArmedAt < DisarmSeconds;

    private static string SlotName(int slot) => slot == 4 ? "the melee slot" : slot == Game.GrenadeSlot ? "the grenade slot" : $"slot {slot + 1}";

    /// <summary>The DISASSEMBLE button (click twice: it takes the whole build apart) in the workbench's top button row (where Auto-complete is in the sandbox), for the selected slot.</summary>
    private void DrawBenchDisassemble(ZBenchSession run, ZPlayer? zp)
    {
        var current = zp?.Slots[run.Slot];
        var armed = current != null && DisassembleArmed(run.Slot);
        ImGui.BeginDisabled(current == null);
        if (armed)
            ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.62f, 0.12f, 0.1f, 1f));
        var slotLabel = run.Slot == 4 ? "melee" : run.Slot == Game.GrenadeSlot ? "grenade" : $"slot {run.Slot + 1}";
        var label = current == null ? $"Disassemble {slotLabel}" : armed ? $"CLICK AGAIN: take {slotLabel} apart" : $"Disassemble {slotLabel}";
        var clicked = ImGui.Button($"{label}##wbdisasm");
        if (armed)
            ImGui.PopStyleColor();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            WbTip(current == null
                ? $"Nothing in {SlotName(run.Slot)} to take apart."
                : $"Takes the {current.Name} completely apart: all {current.Nodes.Count()} parts, the {(current.IsMelee ? "hilt" : current.IsGrenade ? "casing" : "grip")} too, go to your loose parts and {SlotName(run.Slot)} is left empty"
                  + (run.Slot == 4 ? " (the melee key falls back to the standard strike)." : run.Slot == Game.GrenadeSlot ? " (your grenades are stock frags again; you keep how many you carry)." : " (another gun goes in your hands, or nothing: the melee key still works).")
                  + "\nKey: D. Click twice (or press D twice) to confirm. Undo puts it back while the bench is open.");
        if (clicked && current != null)
            BenchDisassemble(run, current);
    }

    /// <summary>The D key at a Zombies workbench: arms Disassemble, a second press within a few seconds confirms it.</summary>
    private void BenchDisassembleKey()
    {
        if (BenchRun is { } run && BenchPlayer?.Slots[run.Slot] is { } current)
            BenchDisassemble(run, current);
    }

    /// <summary>First call arms it (says what will happen); the second, within <see cref="DisarmSeconds"/>, sends it to the server.</summary>
    private void BenchDisassemble(ZBenchSession run, WeaponBuild current)
    {
        var n = current.Nodes.Count();
        if (!DisassembleArmed(run.Slot))
        {
            _disArmedSlot = run.Slot;
            _disArmedAt = _wbNow;
            WbSay($"Take the {current.Name} apart? Click Disassemble again (or press D) to put all {n} of its parts in your loose parts and leave {SlotName(run.Slot)} empty.", true);
            return;
        }
        _disArmedSlot = -1;
        _disUndo = (run.Slot, current.Clone());
        run.Game.BenchRequest(Game.BenchDisassemble, run.Slot, ""); // the server checks the bench and the slot, and moves the parts
        _runStockKey = int.MinValue;
        if (run.Game.IsServer && BenchPlayer?.Slots[run.Slot] != null)
        {
            _disUndo = null; // refused (the host hears why at once; a client gets a feed line)
            BenchLoad(run.Slot);
            return;
        }
        StartEditing(null, SlotTitle(run.Slot)); // the slot is empty now: start a new build from your parts
        WbSay($"Took the {current.Name} apart: {n} part{(n == 1 ? "" : "s")} went to your loose parts. Undo (Ctrl+Z) puts it back.");
    }

    /// <summary>Is there a disassembly Undo can put back (the bench is still open and the slot is still empty)?</summary>
    private bool CanUndoDisassemble => BenchRun != null && _disUndo is { } u && BenchPlayer is { } zp && zp.Slots[u.Slot] == null;

    /// <summary>Undo the last disassembly: the same build goes back in the slot from your loose parts (the server checks them as for any Apply).</summary>
    private bool UndoDisassemble()
    {
        if (!CanUndoDisassemble || BenchRun is not { } run || _disUndo is not { } u)
            return false;
        _disUndo = null;
        run.Game.BenchRequest(0, u.Slot, u.Build.ToText());
        _runStockKey = int.MinValue;
        if (run.Slot != u.Slot)
            run.Slot = u.Slot;
        _wbKind = Game.SlotKind(u.Slot);
        StartEditing(u.Build.Clone(), u.Build.Name);
        WbSay($"Put the {u.Build.Name} back together in {SlotName(u.Slot)}.");
        return true;
    }

    /// <summary>
    /// The run's bar beside CLOSE, on every workbench screen: Undo disassemble while it can, and (at the grip picker,
    /// where the slot list isn't shown) the slots to move to.
    /// </summary>
    private void DrawBenchRunFooter()
    {
        if (BenchRun is not { } run)
            return;
        if (CanUndoDisassemble && _disUndo is { } u)
        {
            ImGui.SameLine();
            if (ImGui.Button($"Undo disassemble ({u.Build.Name})##wbundodis", new float2(0, 32 * _scale)))
                UndoDisassemble();
        }
        if (!_wbPicking)
            return;
        var slots = run.Game.Local is { ExtraSlots: true } ? 4 : 2;
        for (var i = 0; i < Game.BuildSlots; i++)
        {
            if (i >= slots && i < 4)
                continue;
            ImGui.SameLine();
            if (ImGui.Button(i == 4 ? $"Melee##wbfslot4" : i == Game.GrenadeSlot ? "Grenade##wbfslot5" : $"Slot {i + 1}##wbfslot{i}", new float2(0, 32 * _scale)) && i != run.Slot)
                BenchLoad(i);
        }
    }
}
