using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The workbench's blueprint library (docs/zombies/WEAPON-BUILDER.md, "Blueprints"; <see cref="BlueprintStore"/>): a
/// scrollable list of the saved blueprints of the kind being built (guns, melee or grenades), with Save as blueprint,
/// Load, Save over, Rename, Duplicate and Delete (Save over and Delete ask for a second click). In the sandbox Load opens
/// the whole build; in a Zombies run it fills only the parts you own (<see cref="BlueprintFit"/>) and the rest are red
/// rings on the gun and a list (<see cref="BenchModel.Gaps"/>).
/// </summary>
public sealed partial class Ui
{
    private int _bpSel = -1;
    private int _bpArmed = -1; // which blueprint a first click on Delete / Save over armed
    private bool _bpArmedOverwrite;
    private double _bpArmedAt = -100;
    private int _bpRenaming = -1;
    private ImInputString? _bpRenameInput;
    private readonly List<BlueprintGap> _bpGaps = new();

    // The rows shown (indices into BlueprintStore.All of the kind being built) and their missing counts, cached.
    private readonly List<int> _bpRows = new();
    private readonly List<int> _bpMissing = new();
    private readonly List<string> _bpLabels = new();
    private string _bpHeader = "";
    private readonly List<string> _bpGapLines = new();
    private int _bpGapVersion = -1;
    private (int Library, int Stock, BuildKind Kind) _bpKey = (int.MinValue, 0, BuildKind.Gun);

    private bool BlueprintArmed(int i, bool overwrite) => _bpArmed == i && _bpArmedOverwrite == overwrite && _wbNow - _bpArmedAt < DisarmSeconds;

    private void RefreshBlueprintRows()
    {
        var key = (BlueprintStore.Version, Bm.Stock.Fingerprint, _wbKind);
        if (key == _bpKey)
            return;
        _bpKey = key;
        _bpRows.Clear();
        _bpMissing.Clear();
        _bpLabels.Clear();
        for (var i = 0; i < BlueprintStore.All.Count; i++)
        {
            var b = BlueprintStore.All[i];
            if (b.Kind != _wbKind)
                continue;
            _bpRows.Add(i);
            _bpMissing.Add(Bm.Stock.IsUnlimited ? 0 : MissingReport.Of(b, Bm.Stock).Total);
            _bpLabels.Add($"{b.Name}##bp{i}");
        }
        _bpHeader = $"Blueprints ({_bpRows.Count} {KindPlural(_wbKind)})###wbblueprints";
    }

    private static string KindPlural(BuildKind k) => k switch { BuildKind.Melee => "melee weapons", BuildKind.Grenade => "grenades", _ => "guns" };

    /// <summary>The library's collapsing section, in the sandbox's and a Zombies run's side panel.</summary>
    private void DrawBlueprints(WeaponBuild b)
    {
        BlueprintStore.EnsureLoaded();
        RefreshBlueprintRows();
        if (!ImGui.CollapsingHeader(_bpHeader))
            return;
        if (ImGui.Button("Save as blueprint"u8))
        {
            var i = BlueprintStore.Add(b, out var why);
            if (i < 0)
                WbSay(why ?? "Couldn't save it.", true);
            else
            {
                _bpSel = i;
                WbSay($"Saved the blueprint '{BlueprintStore.All[i].Name}'{(Bm.CanFinalize ? "." : " (unfinished: it's a plan).")}");
            }
        }
        if (ImGui.IsItemHovered())
            WbTip("Keeps a copy of this build in your blueprint library (any number, kept across sessions and updates). Unfinished builds can be saved too."u8);

        if (_bpRows.Count == 0)
        {
            ImGuiSafe.TextColored(Dim, $"No {KindPlural(_wbKind)} saved yet.");
            return;
        }
        var rows = Math.Min(_bpRows.Count, 8);
        if (ImGui.BeginChild("##wbbplist"u8, new float2(0, (rows * 21 + 8) * _scale), ImGuiChildFlags.Borders))
        {
            for (var r = 0; r < _bpRows.Count; r++)
            {
                var i = _bpRows[r];
                var bp = BlueprintStore.All[i];
                if (ImGui.Selectable(_bpLabels[r], _bpSel == i, ImGuiSelectableFlags.AllowDoubleClick))
                {
                    if (_bpSel != i)
                        _bpRenaming = -1;
                    _bpSel = i;
                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        LoadBlueprint(i);
                }
                if (ImGui.IsItemHovered())
                    WbTip($"{bp.PartCount} parts.{(_bpMissing[r] > 0 ? $" {_bpMissing[r]} of them you don't have: they'd be left as red empty sockets." : "")}\nDouble-click to load.");
                MissingBadge(_bpMissing[r]);
            }
        }
        ImGui.EndChild();

        var sel = _bpSel;
        if ((uint)sel >= (uint)BlueprintStore.All.Count || BlueprintStore.All[sel].Kind != _wbKind)
        {
            ImGui.TextColored(Dim, "Pick a blueprint to load, rename, copy or delete."u8);
            return;
        }
        var cur = BlueprintStore.All[sel];
        if (_bpRenaming == sel)
        {
            ImGui.SetNextItemWidth(-60 * _scale);
            var enter = ImGui.InputText("##bprename"u8, _bpRenameInput!, ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            if (ImGui.Button("OK##bpren"u8) || enter)
            {
                var name = BlueprintStore.Rename(sel, ImGuiSafe.Text(_bpRenameInput!));
                _bpRenaming = -1;
                WbSay($"Renamed it '{name}'.");
            }
            return;
        }
        if (ImGui.Button("Load##bp"u8))
            LoadBlueprint(sel);
        if (ImGui.IsItemHovered())
            WbTip(BenchRun != null ? "Builds it from the parts you own (loose parts and this slot's build). Parts you don't have are left as red empty sockets, listed under the gun."u8 : "Opens the whole build to edit (your current build stays as an Undo step)."u8);
        ImGui.SameLine();
        var overArmed = BlueprintArmed(sel, true);
        ImGui.BeginDisabled(b.Root == null);
        if (ImGui.Button(overArmed ? "Click again##bpover"u8 : "Save over##bpover"u8))
        {
            if (!overArmed)
                (_bpArmed, _bpArmedOverwrite, _bpArmedAt) = (sel, true, _wbNow);
            else
            {
                _bpArmed = -1;
                BlueprintStore.Overwrite(sel, b);
                WbSay($"Saved this build over the blueprint '{cur.Name}'.");
            }
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            WbTip($"Replaces the blueprint '{cur.Name}' with the build you're editing (it keeps its name). Click twice.");
        ImGui.SameLine();
        if (ImGui.Button("Rename##bp"u8))
        {
            _bpRenameInput ??= new ImInputString(33);
            ImGuiSafe.SetInput(_bpRenameInput, cur.Name);
            _bpRenaming = sel;
        }
        ImGui.SameLine();
        if (ImGui.Button("Duplicate##bp"u8))
        {
            var d = BlueprintStore.Duplicate(sel);
            if (d < 0)
                WbSay($"The library is full ({BlueprintStore.MaxCount} blueprints).", true);
            else
            {
                _bpSel = d;
                WbSay($"Copied it as '{BlueprintStore.All[d].Name}'.");
            }
        }
        ImGui.SameLine();
        var delArmed = BlueprintArmed(sel, false);
        if (delArmed)
            ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.62f, 0.12f, 0.1f, 1f));
        var del = ImGui.Button(delArmed ? "Click again: delete##bpdel"u8 : "Delete##bpdel"u8);
        if (delArmed)
            ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
            WbTip($"Deletes the blueprint '{cur.Name}' from your library (your slots and guns are untouched). Click twice.");
        if (del)
        {
            if (!delArmed)
            {
                (_bpArmed, _bpArmedOverwrite, _bpArmedAt) = (sel, false, _wbNow);
                WbSay($"Delete the blueprint '{cur.Name}'? Click Delete again to confirm.", true);
            }
            else
            {
                _bpArmed = -1;
                BlueprintStore.Delete(sel);
                _bpSel = -1;
                WbSay($"Deleted the blueprint '{cur.Name}'.");
            }
        }
    }

    /// <summary>Opens blueprint <paramref name="i"/> on the editor: whole in the sandbox, from your parts in a Zombies run.</summary>
    internal void LoadBlueprint(int i)
    {
        if ((uint)i >= (uint)BlueprintStore.All.Count || _bm == null)
            return;
        var bp = BlueprintStore.All[i];
        if (bp.Kind != _wbKind)
            return;
        WeaponBuild? b;
        if (BenchRun != null)
        {
            b = BlueprintFit.Fit(bp, Bm.Stock, _bpGaps);
            if (b == null)
            {
                WbSay($"You don't have the {bp.Root!.Part.Name} the blueprint '{bp.Name}' is built on.", true);
                return;
            }
        }
        else
        {
            b = bp.Clone();
            _bpGaps.Clear();
        }
        Bm.SetBuild(b, keepHistory: Bm.Build.Root != null);
        if (_bpGaps.Count > 0)
            Bm.SetGaps(_bpGaps);
        ImGuiSafe.SetInput(_wbNameInput!, b.Name);
        _wbPicking = false;
        _wbSel = null;
        ResetView();
        var missing = _bpGaps.Count;
        WbSay(missing > 0
            ? $"Loaded '{bp.Name}' from your parts: {missing} part{(missing == 1 ? "" : "s")} you don't have (red rings, listed under the gun)."
            : Bm.Missing.Total > 0 ? $"Loaded the blueprint '{bp.Name}'. {Bm.Missing.Text()}" : $"Loaded the blueprint '{bp.Name}'.", missing > 0 || Bm.Missing.Total > 0);
    }

    /// <summary>Under the gun: what a blueprint loaded in a run is still missing (one line per part, in red; the text is made when the build changes).</summary>
    private void DrawBlueprintGaps()
    {
        var gaps = Bm.Gaps;
        if (gaps.Count == 0)
            return;
        if (_bpGapVersion != Bm.Version)
        {
            _bpGapVersion = Bm.Version;
            _bpGapLines.Clear();
            _bpGapLines.Add($"From the blueprint, you don't have ({gaps.Count}):");
            var shown = Math.Min(gaps.Count, 6);
            for (var k = 0; k < shown; k++)
            {
                var g = gaps[k];
                var spot = Bm.SpotOf(g.ParentPath, g.Socket);
                var where = spot >= 0 ? $"{SocketLabel(Bm.Spots[spot].Socket)} of the {Bm.Spots[spot].Parent.Part.Name}" : g.Socket;
                _bpGapLines.Add($"  {g.Part.Name} ({where}){(g.Under > 0 ? $" and {g.Under} part{(g.Under == 1 ? "" : "s")} on it" : "")}");
            }
            if (gaps.Count > shown)
                _bpGapLines.Add($"  ...and {gaps.Count - shown} more");
        }
        foreach (var line in _bpGapLines)
            ImGuiSafe.TextColored(Red, line);
    }
}
