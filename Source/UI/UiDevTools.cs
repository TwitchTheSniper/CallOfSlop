using System.Numerics;
using System.Text.Json.Nodes;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The dev tools' windows (Program.DevTools.cs has the keys and the ray): the look-at inspector, a small panel naming what
/// the crosshair is on with its outline drawn over the world, and the prop editor for the placed prop under it. Dev
/// builds always have them; a release build with Settings > Graphics > "Allow the dev tools". Closed, one bool check each.
/// </summary>
public sealed partial class Ui
{
    /// <summary>The look-at inspector panel is up.</summary>
    public bool LookAtOpen;

    /// <summary>The prop editor window is up.</summary>
    public bool PropEditorOpen;

    private int _peSel;
    private bool _peSameAnim = true;
    private string _peMsg = "";
    private double _peMsgAt = -10;
    private string? _pePackage;
    private ImInputString? _peNote;
    private PropEditSession? _peFor;
    private static readonly string[] AxisNames = ["x", "y", "z", "-x", "-y", "-z"];

    private static readonly ImColor8 LookOutline = new(255, 220, 60, 255);
    private static readonly ImColor8 EditOutline = new(80, 220, 255, 255);

    /// <summary>The inspector panel and the outline of what was hit (call with the frame's camera).</summary>
    public void DrawLookAt(Map map, in LookAtHit hit, string reference, List<string> details, in Matrix4x4 viewProj, int copyBind, int editBind, int closeBind)
    {
        if (!LookAtOpen)
            return;
        if (hit.Found && !PropEditorOpen)
            DrawHitOutline(map, hit, viewProj, LookOutline);
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + vp.WorkSize.X - 16 * _scale, vp.WorkPos.Y + 120 * _scale), ImGuiCond.Always, new float2(1, 0));
        ImGui.SetNextWindowBgAlpha(0.72f);
        if (ImGui.Begin("##lookat"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings
                                      | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav))
        {
            ImGuiSafe.TextColored(Orange, $"LOOK-AT ({Keybinds.Name(closeBind)})");
            foreach (var line in details)
                ImGui.Text(line);
            ImGui.Separator();
            ImGui.PushTextWrapPos(560 * _scale);
            ImGuiSafe.TextColored(Dim, reference);
            ImGui.PopTextWrapPos();
            ImGuiSafe.TextColored(Dim, hit.Kind == LookAtKind.Prop
                ? $"{Keybinds.Name(copyBind)}: copy this line for a report.  {Keybinds.Name(editBind)}: edit this prop."
                : $"{Keybinds.Name(copyBind)}: copy this line for a report.");
        }
        ImGui.End();
    }

    /// <summary>A flash message under the inspector (copied, refused...).</summary>
    public void DevToolsMessage(string text, double now)
    {
        _peMsg = text;
        _peMsgAt = now;
        Console.WriteLine($"[devtools] {text}");
    }

    /// <summary>The last dev-tools message while it's fresh (Program.DevTools.cs shows it when no window is open).</summary>
    public string? DevToolsFlash(double now) => now - _peMsgAt < 4 && _peMsg.Length > 0 ? _peMsg : null;

    /// <summary>A dev-tools message over the HUD (no window open), a little below the middle of the screen.</summary>
    public void DrawDevFlash(string text)
    {
        var vp = ImGui.GetMainViewport();
        DlText(ImGui.GetForegroundDrawList(), new float2(vp.WorkPos.X + 16 * _scale, vp.WorkPos.Y + vp.WorkSize.Y * 0.62f), new ImColor8(255, 230, 120, 255), text);
    }

    private void DrawHitOutline(Map map, in LookAtHit hit, in Matrix4x4 viewProj, ImColor8 col)
    {
        if (hit.Kind == LookAtKind.Prop)
            DrawPartOutline(map, hit.Prop, hit.Part, viewProj, col);
        else if (hit.Box >= 0 && hit.Box < map.Solid.Count)
        {
            var b = map.Solid[hit.Box];
            Span<Vector3> c = stackalloc Vector3[8];
            for (var i = 0; i < 8; i++)
                c[i] = b.Center + new Vector3((i & 1) == 0 ? -b.Half.X : b.Half.X, (i & 2) == 0 ? -b.Half.Y : b.Half.Y, (i & 4) == 0 ? -b.Half.Z : b.Half.Z);
            DrawCorners(c, viewProj, col);
        }
    }

    /// <summary>Outlines one part of a placed prop (its turned box, in the world).</summary>
    private void DrawPartOutline(Map map, int prop, int part, in Matrix4x4 viewProj, ImColor8 col)
    {
        if ((uint)prop >= (uint)map.PropInstances.Count)
            return;
        var inst = map.PropInstances[prop];
        if ((uint)part >= (uint)inst.Def.Parts.Count)
            return;
        var p = inst.Def.Parts[part];
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, inst.Yaw);
        Span<Vector3> c = stackalloc Vector3[8];
        for (var i = 0; i < 8; i++)
        {
            Vector3 local;
            if (p.Corners is { Length: 8 } cs)
                local = cs[i];
            else
            {
                var h = p.Size / 2;
                local = p.Offset + Vector3.Transform(new Vector3((i & 1) == 0 ? -h.X : h.X, (i & 2) == 0 ? -h.Y : h.Y, (i & 4) == 0 ? -h.Z : h.Z), p.Rotation);
            }
            c[i] = inst.Position + Vector3.Transform(local * inst.Scale, turn);
        }
        DrawCorners(c, viewProj, col);
    }

    /// <summary>The 12 edges of a box given by its 8 corners (bit 0: +x, bit 1: +y, bit 2: +z), over the world.</summary>
    private static void DrawCorners(ReadOnlySpan<Vector3> c, in Matrix4x4 viewProj, ImColor8 col)
    {
        var vp = ImGui.GetMainViewport();
        float2 org = vp.WorkPos, area = vp.WorkSize;
        Span<Vector2> s = stackalloc Vector2[8];
        Span<bool> ok = stackalloc bool[8];
        for (var i = 0; i < 8; i++)
            ok[i] = Hud.Project(viewProj, c[i], area.X, area.Y, out s[i], out var depth, 4000f) || depth > 0.05f;
        var dl = ImGui.GetForegroundDrawList();
        for (var i = 0; i < 8; i++)
            for (var bit = 1; bit < 8; bit <<= 1)
            {
                var j = i | bit;
                if (j == i || !ok[i] || !ok[j])
                    continue;
                dl.AddLine(new float2(org.X + s[i].X, org.Y + s[i].Y), new float2(org.X + s[j].X, org.Y + s[j].Y), col, 2f);
            }
    }

    // ---- The prop editor -----------------------------------------------------------------------------

    /// <summary>
    /// Draws the prop editor for <paramref name="s"/>. Returns true when the player asked to close it (the session stays
    /// live until the map changes or they revert). Every change is checked by the prop loader as it is made.
    /// </summary>
    public void DrawPropEditor(PropEditSession? s, in Matrix4x4 viewProj, double now, int bind)
    {
        if (!PropEditorOpen || s == null)
            return;
        if (!ReferenceEquals(s, _peFor))
        {
            _peFor = s;
            _peSel = 0;
            _pePackage = null;
            ImGui.SetNextWindowFocus();
        }
        _peSel = Math.Clamp(_peSel, 0, s.Def.Parts.Count - 1);
        DrawPartOutline(s.Map, s.Instance, _peSel, viewProj, EditOutline);
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + 24 * _scale, vp.WorkPos.Y + 40 * _scale), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new float2(MathF.Min(860 * _scale, vp.WorkSize.X * 0.9f), MathF.Min(760 * _scale, vp.WorkSize.Y * 0.9f)), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.94f);
        if (ImGui.Begin("Prop editor###propeditor"u8, ref PropEditorOpen))
        {
            ImGuiSafe.TextColored(Orange, $"{s.Def.Name}  -  {s.PropPath}");
            ImGuiSafe.TextColored(Dim, $"Placed prop #{s.Instance + 1} on {s.Map.Name} ({LookAt.MapPath(s.Map)} {LookAt.OpText(s.Map, s.Map.FileOpOfProp(s.Instance))}). {Keybinds.Name(bind)} or Esc closes.");
            ImGuiSafe.TextColored(Dim, "Live on this PC only: every copy of this prop changes; collisions stay as the map built them. Nothing is written until Save package."u8);
            DrawEditToolbar(s, now);
            if (s.Error.Length > 0)
            {
                ImGui.PushTextWrapPos(0);
                ImGuiSafe.TextColored(Red, "Refused: " + s.Error);
                ImGui.PopTextWrapPos();
            }
            if (now - _peMsgAt < 6 && _peMsg.Length > 0)
                ImGuiSafe.TextColored(Green, _peMsg);
            ImGui.Separator();
            DrawPlacement(s, now);
            ImGui.Separator();
            var avail = ImGui.GetContentRegionAvail();
            var listW = 250 * _scale;
            if (ImGui.BeginChild("##pelist"u8, new float2(listW, avail.Y), ImGuiChildFlags.Borders))
                DrawPartList(s);
            ImGui.EndChild();
            ImGui.SameLine();
            if (ImGui.BeginChild("##pepart"u8, new float2(avail.X - listW - 8 * _scale, avail.Y), ImGuiChildFlags.None))
                DrawPartFields(s, now);
            ImGui.EndChild();
        }
        ImGui.End();
    }

    private void DrawEditToolbar(PropEditSession s, double now)
    {
        ImGui.BeginDisabled(!s.CanUndo);
        if (ImGui.Button("Undo"u8))
            s.Undo();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!s.CanRedo);
        if (ImGui.Button("Redo"u8))
            s.Redo();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!s.Changed);
        if (ImGui.Button("Revert all"u8))
        {
            s.Revert();
            DevToolsMessage($"reverted \"{s.Def.Name}\" to its file", now);
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        _peNote ??= new ImInputString(161);
        ImGui.SetNextItemWidth(220 * _scale);
        ImGui.InputTextWithHint("##penote"u8, "Note for the report (optional)"u8, _peNote);
        ImGui.SameLine();
        ImGui.BeginDisabled(!s.Changed);
        if (ImGui.Button("Save package"u8))
        {
            try
            {
                _pePackage = DevEditPackage.Write(s, DateTime.Now, ImGuiSafe.Text(_peNote).Trim());
                var problems = DevEditPackage.Validate(_pePackage);
                DevToolsMessage(problems.Count == 0 ? $"saved and checked: {_pePackage}" : $"saved {_pePackage}, but: {problems[0]}", now);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DevToolsMessage($"couldn't save the package: {ex.Message}", now);
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetItemTooltip("Writes a change package in DevEdits in the save folder: edit.json (what changed), the edited prop file and a readme. Attach it to the Playtest Desk report. Shipped files are never changed."u8);
        ImGui.SameLine();
        if (ImGui.Button("Open folder"u8))
            DevEditPackage.OpenFolder(_pePackage ?? DevEditPackage.Folder);
    }

    private static float3 F3(Vector3 v) => new(v.X, v.Y, v.Z);

    private static Vector3 V3(float3 v) => new(v.X, v.Y, v.Z);

    private void DrawPlacement(PropEditSession s, double now)
    {
        ImGui.Text("Placed prop (this copy only)"u8);
        var pos = F3(s.Position);
        float yaw = s.YawDeg, scale = s.Scale;
        var changed = ImGui.DragFloat3("Position##pepos"u8, ref pos, 0.01f, -5000f, 5000f, "%.3f", ImGuiSliderFlags.None);
        changed |= ImGui.DragFloat("Yaw (degrees)##peyaw"u8, ref yaw, 0.5f, -360f, 360f, "%.1f", ImGuiSliderFlags.None);
        changed |= ImGui.DragFloat("Scale##pescale"u8, ref scale, 0.005f, 0.1f, 10f, "%.3f", ImGuiSliderFlags.AlwaysClamp);
        if (changed)
            s.SetPlacement(V3(pos), yaw, scale, now);
        var why = "";
        var patch = s.PlacementChanged ? s.MapOpPatch(out why) : null;
        if (s.PlacementChanged)
            ImGuiSafe.TextColored(Dim, patch is { } p ? $"Saved as a patch of map op #{p.OpNumber}." : why);
    }

    private void DrawPartList(PropEditSession s)
    {
        var parts = s.Def.Parts;
        for (var i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            var anim = LookAt.AnimText(p);
            var label = $"#{i + 1} {PropEditSession.Num(p.Size.X)} x {PropEditSession.Num(p.Size.Y)} x {PropEditSession.Num(p.Size.Z)}{(anim.Length > 0 ? "  " + anim : "")}{(s.PartEditable(i) ? "" : "  (prefab)")}##pe{i}";
            if (ImGui.Selectable(label, i == _peSel))
                _peSel = i;
        }
    }

    private void DrawPartFields(PropEditSession s, double now)
    {
        var i = _peSel;
        var part = s.Def.Parts[i];
        ImGuiSafe.TextColored(Orange, $"Part #{i + 1} of {s.Def.Parts.Count}");
        if (ImGui.Button("Add box"u8) && s.AddPart() is var added and >= 0)
            _peSel = added;
        ImGui.SameLine();
        ImGui.BeginDisabled(!s.PartEditable(i));
        if (ImGui.Button("Duplicate"u8) && s.DuplicatePart(i) is var dup and >= 0)
            _peSel = dup;
        ImGui.SameLine();
        if (ImGui.Button("Delete"u8))
            s.DeletePart(i);
        ImGui.EndDisabled();
        if (!s.PartEditable(i))
        {
            s.PartJson(i, out var why);
            ImGui.PushTextWrapPos(0);
            ImGuiSafe.TextColored(Dim, why);
            ImGui.PopTextWrapPos();
            return;
        }
        i = Math.Clamp(_peSel, 0, s.Def.Parts.Count - 1);
        part = s.Def.Parts[i];

        var size = F3(part.Size);
        if (ImGui.DragFloat3("Size##pesize"u8, ref size, 0.005f, 0.001f, PropFile.MaxSize, "%.3f", ImGuiSliderFlags.None))
            s.SetPartVector(i, "size", V3(size), now);
        var offset = F3(part.Offset);
        if (ImGui.DragFloat3("Offset##peoff"u8, ref offset, 0.005f, -PropFile.MaxSize, PropFile.MaxSize, "%.3f", ImGuiSliderFlags.None))
            s.SetPartVector(i, "offset", V3(offset), now);
        var rot = F3(part.RotationDeg);
        if (ImGui.DragFloat3("Rotation (pitch, yaw, roll)##perot"u8, ref rot, 0.5f, -360f, 360f, "%.1f", ImGuiSliderFlags.None))
            s.SetPartVector(i, "rotation", V3(rot), now);
        var col = F3(part.Color);
        if (ImGui.ColorEdit3("Colour##pecol"u8, ref col, ImGuiColorEditFlags.None))
            s.SetPartColor(i, V3(col), now);
        var current = part.Layer >= 0 ? PropFile.Materials.FirstOrDefault(m => m.Value == part.Layer).Key ?? "?" : "(plain colour)";
        if (ImGui.BeginCombo("Material##pemat"u8, current, ImGuiComboFlags.None))
        {
            if (ImGui.Selectable("(plain colour)"u8, part.Layer < 0))
                s.SetPartMaterial(i, null);
            foreach (var name in PropFile.Materials.Keys)
                if (ImGui.Selectable(name, name == current))
                    s.SetPartMaterial(i, name);
            ImGui.EndCombo();
        }

        ImGui.Separator();
        if (part.Anim is not { } anim)
        {
            ImGuiSafe.TextColored(Dim, "No \"anim\" block: a still part."u8);
            return;
        }
        ImGui.Text($"Animation: {LookAt.AnimText(part)}");
        ImGui.Checkbox("Change every part with the same anim block"u8, ref _peSameAnim);
        if (ImGui.IsItemHovered())
            ImGui.SetItemTooltip("A spinning rotor is many parts with the same anim block: change them together (on), or this part only (off)."u8);
        ImGui.Text("Axis:"u8);
        foreach (var a in AxisNames)
        {
            ImGui.SameLine();
            var on = LookAt.AxisText(anim.Axis) == a;
            if (on)
                ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.25f, 0.55f, 0.8f, 1f));
            if (ImGui.SmallButton(a))
                s.SetAnim(i, "axis", JsonValue.Create(a), _peSameAnim);
            if (on)
                ImGui.PopStyleColor();
        }
        var pivot = F3(anim.Pivot ?? part.Offset);
        if (ImGui.DragFloat3("Pivot##pepiv"u8, ref pivot, 0.005f, -PropFile.MaxSize, PropFile.MaxSize, "%.3f", ImGuiSliderFlags.None))
            s.SetAnim(i, "pivot", PropEditSession.Vec(V3(pivot)), _peSameAnim, now);
        if (anim.Type == PropAnimType.Spin)
        {
            var rpm = anim.Rpm;
            if (ImGui.DragFloat("Rpm##perpm"u8, ref rpm, 0.5f, -600f, 600f, "%.1f", ImGuiSliderFlags.AlwaysClamp))
                s.SetAnim(i, "rpm", JsonValue.Create(Math.Round(rpm, 1)), _peSameAnim, now);
        }
        else if (anim.Type == PropAnimType.Swing)
        {
            var angle = anim.AngleDeg;
            if (ImGui.DragFloat("Angle (degrees)##peang"u8, ref angle, 0.5f, 0.1f, 180f, "%.1f", ImGuiSliderFlags.AlwaysClamp))
                s.SetAnim(i, "angle", JsonValue.Create(Math.Round(angle, 1)), _peSameAnim, now);
        }

        var changes = s.PropChanges();
        if (changes.Count > 0 && ImGui.CollapsingHeader($"{changes.Count} change(s) to the prop file###pechanges", ImGuiTreeNodeFlags.None))
            foreach (var (path, from, to) in changes)
                ImGuiSafe.TextColored(Dim, $"{path}: {from?.ToJsonString() ?? "(none)"} -> {to?.ToJsonString() ?? "(removed)"}");
    }
}
