using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Sandbox: the material painter's list (docs/sandbox/CONSTRUCTS.md, "The material painter"; R with the tool out, or "Materials..."
/// on its ring): every material it offers (Extrusion.json's "painter"), each a row with its swatch, its name and what a cubic metre
/// of it is (kg, health, armour a cm and at most, what it salvages as), and for the part last aimed at what it would become. Clicking
/// a row puts it in hand and closes the list; Close and Esc leave it as it was. The lines are made when it opens; a frame of it
/// makes nothing.
/// </summary>
public sealed partial class Ui
{
    public bool SandboxMaterialPickerOpen { get; private set; }

    private string[] _mtIds = [], _mtNames = [], _mtStats = [], _mtPart = [];
    private string _mtPartName = "";

    /// <summary>Opens the list; <paramref name="part"/> (the part last aimed at, or null) gets a line per material for what it would be.</summary>
    public void OpenSandboxMaterialPicker(ConstructPartDef? part)
    {
        SandboxMaterialPickerOpen = true;
        var all = PartMaterials.Offered;
        var t = ExtrusionTable.Current;
        var names = RecyclingTable.Current;
        _mtIds = new string[all.Length];
        _mtNames = new string[all.Length];
        _mtStats = new string[all.Length];
        _mtPart = new string[all.Length];
        var paintable = part != null && PartMaterials.Paintable(part);
        _mtPartName = paintable ? PartMaterials.BaseOf(part!).Name : "";
        for (var k = 0; k < all.Length; k++)
        {
            _mtIds[k] = $"##mt{k}";
            _mtNames[k] = char.ToUpperInvariant(all[k][0]) + all[k][1..];
            if (t.FindByName(all[k]) is { } row)
            {
                var kg = row.UnitsPerM3 * names.WeightOf(names.IdOf(all[k]));
                _mtStats[k] = $"{kg:0} kg/m3, {row.HealthPerM3:0} hp/m3, armour {row.ArmourPerCm:0.#}/cm (at most {row.MaxArmour:0}), salvages as {row.Class}";
            }
            else
                _mtStats[k] = "";
            _mtPart[k] = paintable && PartMaterials.Variant(part!, k) is { } v ? $"{v.Mass:0.#} kg, {v.Health:0} hp, armour {v.Armour:0.#}, {PartMaterials.UnitsFor(v)} {all[k]} to make" : "";
        }
    }

    public void CloseSandboxMaterialPicker() => SandboxMaterialPickerOpen = false;

    /// <summary>
    /// The list's window. Returns true while it stays open; <paramref name="chosen"/> is the material clicked this frame (else -1).
    /// <paramref name="current"/> is the one in hand (outlined).
    /// </summary>
    public bool DrawSandboxMaterialPicker(int current, out int chosen)
    {
        chosen = -1;
        if (!SandboxMaterialPickerOpen)
            return false;
        var open = true;
        var vp = ImGui.GetMainViewport();
        var w = 620 * _scale;
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + vp.WorkSize.X * 0.5f - w * 0.5f, vp.WorkPos.Y + vp.WorkSize.Y * 0.5f - 230 * _scale), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new float2(w, 460 * _scale), ImGuiCond.Appearing);
        ImGui.SetNextWindowBgAlpha(0.92f);
        if (ImGui.Begin("Materials###sbmatpick"u8, ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse))
        {
            if (_mtPartName.Length > 0)
                ImGuiSafe.TextColored(Dim, _mtPartName);
            else
                ImGui.TextColored(Dim, "Aim at a frame, panel or foundation to see what it would become."u8);
            ImGui.Separator();
            if (ImGui.BeginChild("##mtlist"u8, new float2(0, -ImGui.GetFrameHeightWithSpacing()), ImGuiChildFlags.None))
            {
                var dl = ImGui.GetWindowDrawList();
                var rowH = (_mtPartName.Length > 0 ? 62 : 44) * _scale;
                var width = ImGui.GetContentRegionAvail().X;
                for (var k = 0; k < _mtIds.Length; k++)
                {
                    var at = ImGui.GetCursorScreenPos();
                    if (ImGui.InvisibleButton(_mtIds[k], new float2(width, rowH)))
                    {
                        chosen = k;
                        open = false;
                    }
                    var hot = ImGui.IsItemHovered();
                    var p1 = new float2(at.X + width, at.Y + rowH);
                    dl.AddRectFilled(at, p1, k == current ? SbCardSel : hot ? SbCardHot : SbCard, 5 * _scale);
                    var pad = 6 * _scale;
                    var sw = rowH - 2 * pad;
                    dl.AddRectFilled(new float2(at.X + pad, at.Y + pad), new float2(at.X + pad + sw, at.Y + pad + sw), SandboxMaterialWheel.Swatch(k), 4 * _scale);
                    var clip = new float4(at.X, at.Y, p1.X - pad, p1.Y);
                    var tx = at.X + 2 * pad + sw;
                    DlText(dl, new float2(tx, at.Y + pad), SbText, _mtNames[k], clip);
                    DlText(dl, new float2(tx, at.Y + pad + 18 * _scale), SbDim, _mtStats[k], clip);
                    if (_mtPartName.Length > 0)
                        DlText(dl, new float2(tx, at.Y + pad + 36 * _scale), _mtPart[k].Length > 0 ? SbText : SbDim, _mtPart[k].Length > 0 ? _mtPart[k] : "its own material", clip);
                    if (k == current)
                        dl.AddRect(at, p1, SbOutline, 5 * _scale, ImDrawFlags.None, 2f * _scale);
                    else if (hot)
                        dl.AddRect(at, p1, SbHotOutline, 5 * _scale, ImDrawFlags.None, 1.5f * _scale);
                    ImGui.Dummy(new float2(0, 3 * _scale));
                }
                if (_mtIds.Length == 0)
                    ImGui.TextColored(Dim, "The material painter offers nothing (Constructs/Extrusion.json, \"painter\")."u8);
            }
            ImGui.EndChild();
            ImGui.TextColored(Dim, "Click a material to take it, then click parts to remake them in it."u8);
            ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - 90 * _scale);
            if (ImGui.Button("Close"u8, new float2(90 * _scale, 0)))
                open = false;
        }
        ImGui.End();
        if (!open)
            SandboxMaterialPickerOpen = false;
        return SandboxMaterialPickerOpen;
    }
}
