using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Panel Designer's flat plan (docs/sandbox/design/laser-cutter.md section 3 and 6; the user, 2026-11-13: "Flat plan next"): the sheet laid
/// flat and seen from above, L along (the back edge on the left, the nose on the right) by W across (its left edge at the bottom), a 5 cm grid,
/// the panel's facet lines faint, and the cuts with drag handles. A cut is moved by dragging it and sized by its handles (an opening's four
/// sides, each moving the side opposite with it, since a cut is its middle and its size; a cut corner's two tips), on a 5 cm snap (1 cm with
/// Shift): whole centimetres within the ranges <see cref="HullLoft.CutRefusal"/> takes, so a value the plan makes is never refused for its
/// own range, only for a rule between cuts or edges, and then that cut turns red with the words at its handle. The slider rows beside it are the
/// same values: the plan writes <c>_pdCuts</c> before the rows are drawn, so the rows show a drag at once, and a row moved shows on the plan
/// next frame. A frame of dragging makes nothing: the cut is a value, and the key and the preview are made again only when a snapped value
/// changes (the preview's own queue and budget, <see cref="HullLoft.PanelPreview"/>).
/// </summary>
public sealed partial class Ui
{
    private int _pdSel = -1, _pdDrag = -1, _pdDragWhat; // the selected cut; the cut a handle is dragging and which (0 move, 1 long, 2 wide, 3 a corner's along, 4 its across)
    private float _pdGrabU, _pdGrabV; // a move: the cut's middle less the point it was taken at, cm
    private ulong _pdBadMask; // the cuts a refused key's words are about (BadCuts), a bit each
    private float2 _pdPlanO; // the sheet's back left corner (U 0, V W) on screen as drawn last frame
    private float _pdPlanS; // points a cm on the plan last frame; 0: not drawn
    private string? _pdWhyFor;
    private float _pdWhyW;

    private const int PlanSnapCm = 5; // the plan's snap (the user: "5 cm snap"), 1 cm with Shift

    /// <summary>The sheet's cm to the plan's window points this frame (U to the right, V up): a value, so the drawing helpers take it with no delegate made.</summary>
    private readonly struct PlanMap(float2 o, float s, int w)
    {
        public float X(double u) => o.X + (float)u * s;
        public float Y(double v) => o.Y + (float)(w - v) * s;
    }

    /// <summary>The window tool: a point on the sheet (U along, V across, cm) as a window point on the plan as drawn last frame; null before it is.</summary>
    public float2? DesignerPlanPoint(double u, double v) =>
        _pdPlanS <= 0 ? null : new float2(_pdPlanO.X + (float)u * _pdPlanS, _pdPlanO.Y + (float)(_pdW - v) * _pdPlanS);

    /// <summary>The window tool: the plan's state, the cuts as the rows have them, which is selected and which are red.</summary>
    public string DesignerPlanText
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(_pdPlate ? "blank plate" : "blank panel").Append($"; sheet {_pdL} x {_pdW} cm; selected {_pdSel}; drag {_pdDrag}");
            for (var k = 0; k < _pdCuts.Count; k++)
                sb.Append($"; [{k}] {_pdCuts[k].Text}{((_pdBadMask >> k & 1) != 0 ? " RED" : "")}{(k < _pdGlazed.Count && _pdGlazed[k] ? " glazed" : "")}");
            if (_pdCutWhy.Length > 0)
                sb.Append($"; why: {_pdCutWhy}");
            sb.Append("; readouts [").Append(string.Join(" | ", _pdFits.Concat(_pdCutFits))).Append("]; ").Append(_pdNumbers.Replace('\n', ' '));
            return sb.ToString();
        }
    }

    private static readonly ImColor8 PlanBack = new(26, 30, 38, 255), PlanSheet = new(92, 102, 116, 255), PlanGrid = new(120, 130, 146, 60),
        PlanGridMetre = new(150, 160, 176, 120), PlanFacet = new(150, 185, 235, 80), PlanCut = new(205, 228, 255, 255), PlanSel = new(255, 200, 90, 255),
        PlanBad = new(235, 90, 80, 255), PlanHandle = new(240, 244, 250, 255), PlanText = new(225, 230, 238, 255), PlanTextBack = new(20, 22, 28, 230);

    /// <summary>The plan with its tools, in the middle under the preview (between NewFrame and Render). Adds and deletes are only noted, for CutApply.</summary>
    private void FlatPlan(ref int dropCut, ref int addCut)
    {
        // ---- The tools: each adds its cut in the sheet's middle (CutApply), selected, to drag where it goes.
        ImGui.TextColored(PdHead, "Plan"u8);
        ImGui.SameLine();
        ImGui.BeginDisabled(_pdCuts.Count >= HullLoft.MaxCuts);
        if (ImGui.SmallButton("Rectangle"u8))
            addCut = 1;
        ImGui.SameLine();
        if (ImGui.SmallButton("Round"u8))
            addCut = 5;
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("An eight-sided opening: a square with its corners cut back by 0.29 of its side."u8);
        ImGui.SameLine();
        if (ImGui.SmallButton("Slot"u8))
            addCut = 6;
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("A long opening 10 cm wide: a firing slot or a vent."u8);
        ImGui.SameLine();
        ImGui.BeginDisabled(CornerCut(0) && CornerCut(1) && CornerCut(2) && CornerCut(3));
        if (ImGui.SmallButton("Corner"u8))
            addCut = 2;
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("A triangle cut off a corner of the sheet not yet cut; drag its two tips."u8);
        ImGui.SameLine();
        ImGui.BeginDisabled(_pdCuts.Count == 0);
        if (ImGui.SmallButton("Mirror"u8))
            addCut = 3;
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Adds a twin of the selected cut (or the last) across the sheet's centre line."u8);
        ImGui.EndDisabled();
        ImGui.SameLine();
        var sel = _pdSel >= 0 && _pdSel < _pdCuts.Count ? _pdSel : -1;
        ImGui.BeginDisabled(sel < 0);
        if (ImGui.SmallButton("Delete"u8))
            dropCut = sel;
        ImGui.EndDisabled();
        if (sel >= 0 && !_pdCuts[sel].Corner)
        {
            ImGui.SameLine();
            GlazedBox(sel); // (offered only for an opening wholly inside the sheet: its words in the tooltip)
        }
        // ---- The sheet.
        var at = ImGui.GetCursorScreenPos();
        var size = ImGui.GetContentRegionAvail();
        size.Y -= ImGui.GetTextLineHeightWithSpacing();
        if (size.X < 40 || size.Y < 40)
        {
            _pdPlanS = 0;
            return;
        }
        ImGui.InvisibleButton("##pdplan"u8, size);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var pressed = ImGui.IsItemActivated();
        var io = ImGui.GetIO();
        int l = _pdL, w = _pdW;
        var pad = 20 * _scale;
        var s = MathF.Min((size.X - 2 * pad) / l, (size.Y - 2 * pad) / w);
        if (s <= 0)
        {
            _pdPlanS = 0;
            return;
        }
        var o = new float2(at.X + (size.X - l * s) * 0.5f, at.Y + (size.Y - w * s) * 0.5f);
        (_pdPlanO, _pdPlanS) = (o, s);
        var m = new PlanMap(o, s, w);
        var mu = (io.MousePos.X - o.X) / s;
        var mv = w - (io.MousePos.Y - o.Y) / s;
        // ---- Input: a press picks a handle (the selected cut's first), else a cut's body, else nothing; a drag moves what it picked.
        if (pressed)
        {
            _pdDrag = -1;
            var hit = PlanHandleAt(io.MousePos, m, sel, out var what);
            for (var k = _pdCuts.Count - 1; k >= 0 && hit < 0; k--)
                if (k != sel)
                    hit = PlanHandleAt(io.MousePos, m, k, out what);
            if (hit < 0)
            {
                // A body: the selected one first, then the top one (the last drawn).
                if (sel >= 0 && PlanInside(sel, mu, mv))
                    hit = sel;
                for (var k = _pdCuts.Count - 1; k >= 0 && hit < 0; k--)
                    if (PlanInside(k, mu, mv))
                        hit = k;
                what = 0;
            }
            _pdSel = hit;
            if (hit >= 0 && (what != 0 || !_pdCuts[hit].Corner))
            {
                (_pdDrag, _pdDragWhat) = (hit, what);
                (_pdGrabU, _pdGrabV) = (_pdCuts[hit].U - mu, _pdCuts[hit].V - mv);
            }
            sel = _pdSel;
        }
        if (!active)
            _pdDrag = -1;
        else if (_pdDrag >= 0 && _pdDrag < _pdCuts.Count)
        {
            var step = io.KeyShift ? 1 : PlanSnapCm;
            int Snap(float x) => (int)MathF.Round(x / step) * step;
            var c = _pdCuts[_pdDrag];
            var now = c;
            switch (_pdDragWhat)
            {
                case 0:
                    now = c with { U = Math.Clamp(Snap(mu + _pdGrabU), 0, l), V = Math.Clamp(Snap(mv + _pdGrabV), 0, w) };
                    break;
                case 1:
                    now = c with { A = Math.Clamp(Snap(2 * MathF.Abs(mu - c.U)), HullLoft.CutMinCm, Math.Max(HullLoft.CutMinCm, Math.Min(HullLoft.CutMaxCm, l))) };
                    break;
                case 2:
                    now = c with { B = Math.Clamp(Snap(2 * MathF.Abs(mv - c.V)), HullLoft.CutMinCm, Math.Max(HullLoft.CutMinCm, Math.Min(HullLoft.CutMaxCm, w))) };
                    break;
                case 3:
                    now = c with { A = Math.Clamp(Snap(MathF.Abs(mu - (c.U is 0 or 1 ? 0 : l))), HullLoft.CutMinCm, Math.Max(HullLoft.CutMinCm, l - HullLoft.CutMinCm)) };
                    break;
                case 4:
                    now = c with { B = Math.Clamp(Snap(MathF.Abs(mv - (c.U is 0 or 3 ? 0 : w))), HullLoft.CutMinCm, Math.Max(HullLoft.CutMinCm, w - HullLoft.CutMinCm)) };
                    break;
            }
            if (!now.Corner)
                now = now with { C = Math.Min(now.C, Math.Min(now.A, now.B) / 2) }; // (a smaller opening keeps its corners within half its side)
            if (now != c)
                _pdCuts[_pdDrag] = now;
        }
        if (hovered && sel >= 0 && (ImGui.IsKeyPressed(ImGuiKey.Delete, false) || ImGui.IsKeyPressed(ImGuiKey.Backspace, false)))
            dropCut = sel;
        // ---- Drawing: the ground, the sheet, its grid and facet lines, the cuts as holes with outlines, the selected one's handles.
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(at, new float2(at.X + size.X, at.Y + size.Y), PlanBack, 4 * _scale);
        dl.AddRectFilled(new float2(m.X(0), m.Y(w)), new float2(m.X(l), m.Y(0)), PlanSheet);
        var g = 5 * s >= 6 ? 5 : 10 * s >= 6 ? 10 : 25;
        for (var u = g; u < l; u += g)
            dl.AddLine(new float2(m.X(u), m.Y(w)), new float2(m.X(u), m.Y(0)), u % 100 == 0 ? PlanGridMetre : PlanGrid, 1);
        for (var v = g; v < w; v += g)
            dl.AddLine(new float2(m.X(0), m.Y(v)), new float2(m.X(l), m.Y(v)), v % 100 == 0 ? PlanGridMetre : PlanGrid, 1);
        var shape = DesignerShape;
        if (!_pdPlate && HullLoft.PanelRefusal(shape) == null)
        {
            var (nu, nv) = HullLoft.PanelGrid(shape);
            for (var i = 1; i < nu; i++)
            {
                var u = HullLoft.PanelRowS(shape, i, nu) * l;
                dl.AddLine(new float2(m.X(u), m.Y(w)), new float2(m.X(u), m.Y(0)), PlanFacet, 1);
            }
            for (var j = 1; j < nv; j++)
                dl.AddLine(new float2(m.X(0), m.Y(j * (double)w / nv)), new float2(m.X(l), m.Y(j * (double)w / nv)), PlanFacet, 1);
        }
        dl.AddText(new float2(m.X(0), m.Y(w) - ImGui.GetTextLineHeight() - 1), PlanText, "back");
        dl.AddText(new float2(m.X(l) - 34 * _scale, m.Y(w) - ImGui.GetTextLineHeight() - 1), PlanText, "nose");
        var whyAt = -1;
        for (var k = 0; k < _pdCuts.Count; k++)
        {
            var bad = (_pdBadMask >> k & 1) != 0;
            PlanCutDraw(dl, k, m, bad ? PlanBad : k == sel ? PlanSel : PlanCut, k == sel ? 2.5f : 1.5f);
            if (bad && (whyAt < 0 || k == sel))
                whyAt = k;
        }
        if (sel >= 0)
            PlanHandlesDraw(dl, sel, m);
        // ---- A refused cut says why at its handle (the selected one when it is red, else the first red one).
        if (whyAt >= 0 && _pdCutWhy.Length > 0)
        {
            if (!ReferenceEquals(_pdWhyFor, _pdCutWhy))
            {
                _pdWhyFor = _pdCutWhy;
                _pdWhyW = ImGui.CalcTextSize(_pdCutWhy).X; // (once a refusal: the words only change with the key)
            }
            PlanHandleSpot(whyAt, 0, out var hu, out var hv);
            var tx = Math.Clamp(m.X(hu) + 8 * _scale, at.X + 2, MathF.Max(at.X + 2, at.X + size.X - _pdWhyW - 6));
            var ty = Math.Clamp(m.Y(hv) - ImGui.GetTextLineHeight() - 6 * _scale, at.Y + 2, at.Y + size.Y - ImGui.GetTextLineHeight() - 4);
            dl.AddRectFilled(new float2(tx - 3, ty - 1), new float2(tx + _pdWhyW + 3, ty + ImGui.GetTextLineHeight() + 1), PlanTextBack, 3 * _scale);
            dl.AddText(new float2(tx, ty), PlanBad, _pdCutWhy);
        }
        ImGui.TextColored(Dim, "From above, back edge left. Drag a cut to move it, its handles to size it: 5 cm snap, 1 cm with Shift; Delete."u8);
    }

    /// <summary>Is the point (cm on the sheet) inside cut <paramref name="k"/>'s outline (an opening's box, a corner's triangle)?</summary>
    private bool PlanInside(int k, float u, float v)
    {
        var c = _pdCuts[k];
        if (!c.Corner)
            return MathF.Abs(u - c.U) <= c.A * 0.5f && MathF.Abs(v - c.V) <= c.B * 0.5f;
        var du = c.U is 0 or 1 ? u : _pdL - u;
        var dv = c.U is 0 or 3 ? v : _pdW - v;
        return du >= 0 && dv >= 0 && du / c.A + dv / c.B <= 1;
    }

    /// <summary>
    /// Handle <paramref name="what"/> of cut <paramref name="k"/> on the sheet, cm: an opening's 0 its middle, 1 its nose side (long), 2 its right
    /// side (wide), 5 its back side, 6 its left side; a corner's 0 its corner, 3 its tip along the edge, 4 its tip across.
    /// </summary>
    private void PlanHandleSpot(int k, int what, out double u, out double v)
    {
        var c = _pdCuts[k];
        if (c.Corner)
        {
            double cu = c.U is 0 or 1 ? 0 : _pdL, cv = c.U is 0 or 3 ? 0 : _pdW;
            double su = c.U is 0 or 1 ? 1 : -1, sv = c.U is 0 or 3 ? 1 : -1;
            (u, v) = what switch { 3 => (cu + su * c.A, cv), 4 => (cu, cv + sv * c.B), _ => (cu + su * c.A / 3.0, cv + sv * c.B / 3.0) };
            return;
        }
        (u, v) = (c.U, c.V);
        if (what == 1)
            u += c.A / 2.0;
        else if (what == 5)
            u -= c.A / 2.0;
        else if (what == 2)
            v += c.B / 2.0;
        else if (what == 6)
            v -= c.B / 2.0;
    }

    /// <summary>Cut <paramref name="k"/>'s handle under the mouse: the cut, and which drag it starts (1 long, 2 wide, 3 and 4 a corner's tips); -1: none.</summary>
    private int PlanHandleAt(float2 mouse, in PlanMap m, int k, out int what)
    {
        what = 0;
        if (k < 0 || k >= _pdCuts.Count)
            return -1;
        var r = 7 * _scale;
        var corner = _pdCuts[k].Corner;
        for (var h = 0; h < 4; h++)
        {
            var id = corner ? (h < 2 ? h + 3 : 0) : h switch { 0 => 1, 1 => 5, 2 => 2, _ => 6 };
            if (id == 0)
                continue;
            PlanHandleSpot(k, id, out var u, out var v);
            if (MathF.Abs(m.X(u) - mouse.X) <= r && MathF.Abs(m.Y(v) - mouse.Y) <= r)
            {
                what = id is 5 ? 1 : id is 6 ? 2 : id;
                return k;
            }
        }
        return -1;
    }

    /// <summary>Cut <paramref name="k"/> drawn as a hole in the sheet (the ground's colour, so a notch past an edge reads as one) with its outline.</summary>
    private void PlanCutDraw(ImDrawListPtr dl, int k, in PlanMap m, ImColor8 col, float thick)
    {
        var c = _pdCuts[k];
        Span<float2> p = stackalloc float2[8];
        var n = 0;
        if (c.Corner)
        {
            PlanHandleSpot(k, 3, out var au, out var av);
            PlanHandleSpot(k, 4, out var bu, out var bv);
            double cu = c.U is 0 or 1 ? 0 : _pdL, cv = c.U is 0 or 3 ? 0 : _pdW;
            p[n++] = new float2(m.X(cu), m.Y(cv));
            p[n++] = new float2(m.X(au), m.Y(av));
            p[n++] = new float2(m.X(bu), m.Y(bv));
        }
        else
        {
            double u0 = c.U - c.A / 2.0, u1 = c.U + c.A / 2.0, v0 = c.V - c.B / 2.0, v1 = c.V + c.B / 2.0, e = c.C;
            if (e <= 0)
            {
                p[n++] = new float2(m.X(u0), m.Y(v0));
                p[n++] = new float2(m.X(u1), m.Y(v0));
                p[n++] = new float2(m.X(u1), m.Y(v1));
                p[n++] = new float2(m.X(u0), m.Y(v1));
            }
            else
            {
                p[n++] = new float2(m.X(u0 + e), m.Y(v0));
                p[n++] = new float2(m.X(u1 - e), m.Y(v0));
                p[n++] = new float2(m.X(u1), m.Y(v0 + e));
                p[n++] = new float2(m.X(u1), m.Y(v1 - e));
                p[n++] = new float2(m.X(u1 - e), m.Y(v1));
                p[n++] = new float2(m.X(u0 + e), m.Y(v1));
                p[n++] = new float2(m.X(u0), m.Y(v1 - e));
                p[n++] = new float2(m.X(u0), m.Y(v0 + e));
            }
        }
        ImDrawListExtensions.AddConcavePolyFilled(dl, p[..n], PlanBack);
        for (var i = 0; i < n; i++)
            dl.AddLine(p[i], p[(i + 1) % n], col, thick * _scale);
    }

    /// <summary>The selected cut's handles: small squares on an opening's four sides and its middle, on a corner's two tips.</summary>
    private void PlanHandlesDraw(ImDrawListPtr dl, int k, in PlanMap m)
    {
        var corner = _pdCuts[k].Corner;
        var hs = 4 * _scale;
        for (var h = 0; h < 5; h++)
        {
            var id = corner ? h switch { 0 => 3, 1 => 4, _ => -1 } : h switch { 0 => 1, 1 => 5, 2 => 2, 3 => 6, _ => 0 };
            if (id < 0)
                continue;
            PlanHandleSpot(k, id, out var u, out var v);
            var c = new float2(m.X(u), m.Y(v));
            var active = _pdDrag == k && (_pdDragWhat == id || id == 5 && _pdDragWhat == 1 || id == 6 && _pdDragWhat == 2 || id == 0 && _pdDragWhat == 0);
            dl.AddRectFilled(new float2(c.X - hs, c.Y - hs), new float2(c.X + hs, c.Y + hs), active ? PlanSel : PlanHandle, id == 0 ? hs : 1);
        }
    }
}
