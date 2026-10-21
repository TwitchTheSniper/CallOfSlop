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
/// you own this run (your loose parts plus the ones already on the build you're editing), your own slots instead of the
/// saved builds, and scrap: break loose parts down, or craft parts up to the tier the round allows. Apply sends the
/// build to the server, which checks the parts again and puts it in your slot.
/// </summary>
public sealed partial class Ui
{
    /// <summary>Set while the workbench is open in a Zombies match (null: the main menu's sandbox workbench).</summary>
    public ZBenchSession? BenchRun;

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
    }

    private ZPlayer? BenchPlayer => BenchRun?.Game is { Local: { } me } g ? g.ZP(me) : null;

    private void BenchLoad(int slot)
    {
        if (BenchRun == null)
            return;
        BenchRun.Slot = slot;
        _wbMelee = slot == 4;
        _wbCat = _wbMelee ? PartCategory.Head : PartCategory.Grip;
        var current = BenchPlayer?.Slots[slot];
        _wb = current?.Clone() ?? new WeaponBuild(_wbMelee ? "Melee" : $"Slot {slot + 1}", Parts.InCategory(_wbMelee ? PartCategory.Hilt : PartCategory.Grip).First(p => p.IsRoot));
        _wbNameInput ??= new ImInputString(33);
        _wbNameInput.SetValue(_wb.Name.AsSpan());
        _wbSocketParent = _wbSocketName = null;
        _wbPart = null;
        WorkbenchChanged();
    }

    /// <summary>How many of <paramref name="p"/> you could still fit: loose ones, plus the slot's own, minus those on the build being edited.</summary>
    private int BenchAvailable(PartDef p, WeaponBuild editing)
    {
        if (BenchRun == null || BenchPlayer is not { } zp)
            return int.MaxValue;
        static int In(WeaponBuild? b, PartDef p) => b?.Nodes.Count(n => n.Part == p) ?? 0;
        return zp.Parts.GetValueOrDefault(p.Name) + In(zp.Slots[BenchRun.Slot], p) - In(editing, p);
    }

    /// <summary>Is <paramref name="p"/> listed on the run's workbench (you own one, or it's on the build)?</summary>
    private bool BenchShows(PartDef p, WeaponBuild editing) =>
        BenchRun == null || BenchAvailable(p, editing) > 0 || editing.Nodes.Any(n => n.Part == p);

    private WorkbenchLaunch? DrawBenchRunSide(WeaponBuild b)
    {
        var run = BenchRun!;
        var game = run.Game;
        var zp = BenchPlayer;
        ImGui.TextColored(Orange, "YOUR SLOTS (THIS RUN)"u8);
        var slots = game.Local is { ExtraSlots: true } ? 4 : 2;
        for (var i = 0; i < 5; i++)
        {
            if (i >= slots && i < 4)
                continue;
            var label = i == 4 ? $"Melee: {zp?.Slots[4]?.Name ?? "(none)"}##bslot4" : $"{i + 1}. {zp?.Slots[i]?.Name ?? "(a stock gun)"}##bslot{i}";
            if (ImGui.Selectable(label, run.Slot == i))
                BenchLoad(i);
        }
        ImGui.Separator();
        if (_wbMelee)
            DrawWorkbenchMeleeStats(b);
        else
            DrawWorkbenchGunStats(b);
        var problems = _wbAsm?.Problems ?? [];
        foreach (var p in problems.Take(6))
            ImGuiSafe.TextColored(p.Incomplete ? Yellow : Red, (p.Incomplete ? "Needs: " : "Problem: ") + p.Message);
        var ready = _wbMelee ? _wbAsm?.Melee != null : _wbAsm?.Def != null;
        ImGui.BeginDisabled(!ready);
        if (ImGui.Button($"APPLY TO {(run.Slot == 4 ? "THE MELEE SLOT" : $"SLOT {run.Slot + 1}")}", new float2(-1, 36 * _scale)) && ready)
        {
            game.BenchRequest(0, run.Slot, b.ToText());
            _wbMessage = $"Sent '{b.Name}' to slot {(run.Slot == 4 ? "melee" : (run.Slot + 1).ToString())}. Parts you took off go back to your loose parts.";
        }
        ImGui.EndDisabled();
        if (ImGui.Button("Revert"u8))
            BenchLoad(run.Slot);
        ImGui.Separator();

        ImGuiSafe.TextColored(Orange, $"SCRAP: {zp?.Scrap ?? 0}");
        if (ImGui.CollapsingHeader("Loose parts (break down for scrap)"u8, ImGuiTreeNodeFlags.DefaultOpen) && zp != null)
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
            foreach (var part in Parts.InCategory(_wbCat).Where(p => p.Rarity <= cap && Scrap.CraftCost(p) > 0))
            {
                var cost = Scrap.CraftCost(part);
                ImGui.BeginDisabled((zp?.Scrap ?? 0) < cost);
                if (ImGui.SmallButton($"craft {part.Name} ({cost})##craft{part.Name}"))
                    game.BenchRequest(2, 0, part.Name);
                ImGui.EndDisabled();
            }
        }
        ImGui.Separator();
        if (ImGui.Button("CLOSE"u8, new float2(-1, 30 * _scale)))
            CloseBench();
        ImGui.TextColored(Dim, "The zombies don't wait while you work."u8);
        return null;
    }
}
