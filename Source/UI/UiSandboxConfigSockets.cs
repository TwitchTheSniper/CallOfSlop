using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// A Module Socket's panel (Sean 2026-11-21; docs/sandbox/design/module-sockets.md 2.3): the Style choice as four little side views
/// (Proud, Flush, Well, Opening: the hull, the plate and the module), and under the group a side view of this plate's settings as they
/// stand in the panel, with Depth and Room above measured off. Drawn from the panel's live values; allocates nothing.
/// </summary>
public sealed partial class Ui
{
    private static readonly ImColor8 SkHull = new(96, 102, 112, 255), SkHullDark = new(70, 75, 84, 255), SkPlate = new(216, 163, 32, 255),
        SkModule = new(120, 190, 235, 255), SkRoom = new(120, 230, 140, 200), SkDepth = new(240, 200, 90, 255), SkText = new(200, 205, 212, 255),
        SkCard = new(32, 35, 42, 255), SkHot = new(115, 190, 242, 255), SkEdge = new(60, 64, 74, 255);

    private readonly char[] _skText = new char[48];

    /// <summary>The Style row as four pictures; true and the pick when one was clicked.</summary>
    private bool SocketStyleIcons(int current, float width, out int pick)
    {
        pick = current;
        var dl = ImGui.GetWindowDrawList();
        var n = ModuleSocketTable.StyleLabels.Length;
        var gap = 4 * _scale;
        var w = MathF.Max(36 * _scale, (width - gap * (n - 1)) / n);
        var h = w * 0.8f + ImGui.GetFontSize() + 4 * _scale;
        var start = ImGui.GetCursorScreenPos();
        var clicked = false;
        for (var k = 0; k < n; k++)
        {
            var min = start + new float2(k * (w + gap), 0);
            var max = min + new float2(w, h);
            ImGui.SetCursorScreenPos(min);
            ImGui.PushID(k);
            ImGui.InvisibleButton("##sockstyle"u8, new float2(w, h));
            ImGui.PopID();
            var hot = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && k != current)
            {
                pick = k;
                clicked = true;
            }
            if (hot)
                ImGuiSafe.SetTooltip(StyleTip(k));
            dl.AddRectFilled(min, max, SkCard, 3 * _scale);
            StylePicture(dl, (ModStyle)k, min + new float2(4 * _scale, 4 * _scale), new float2(w - 8 * _scale, w * 0.8f - 6 * _scale));
            var label = ModuleSocketTable.StyleLabels[k];
            var ts = ImGui.CalcTextSize(label);
            dl.AddText(new float2(min.X + (w - ts.X) * 0.5f, max.Y - ImGui.GetFontSize() - 3 * _scale), k == current ? SkPlate : SkText, label);
            dl.AddRect(min, max, k == current ? CfAccent : hot ? SkHot : SkEdge, 3 * _scale, ImDrawFlags.None, (k == current ? 2f : 1f) * _scale);
        }
        ImGui.SetCursorScreenPos(new float2(start.X, start.Y + h + 4 * _scale));
        ImGui.Dummy(new float2(1, 1));
        return clicked;
    }

    private static string StyleTip(int k) => (ModStyle)k switch
    {
        ModStyle.Proud => "Proud: the plate sits on the hull and the module stands on it.",
        ModStyle.Flush => "Flush: the plate is a frame on the hull; it digs a well Depth deep and the module sits in it, its top level with the frame.",
        ModStyle.Opening => "Well: as Flush, but the module may stand up out of the well, as high as Room above.",
        _ => "Opening: the plate is a frame with a hole; it punches through what it is on, Depth on behind it, and the module reaches back through.",
    };

    /// <summary>One style's side view in a box at <paramref name="at"/> of <paramref name="size"/>: the hull, the plate, the module.</summary>
    private void StylePicture(ImDrawListPtr dl, ModStyle style, float2 at, float2 size)
    {
        float x0 = at.X, x1 = at.X + size.X, surface = at.Y + size.Y * 0.5f, bottom = at.Y + size.Y;
        float plateT = MathF.Max(2, size.Y * 0.08f);
        float mx0 = x0 + size.X * 0.28f, mx1 = x1 - size.X * 0.28f; // (the module's width)
        float wx0 = mx0 - size.X * 0.04f, wx1 = mx1 + size.X * 0.04f; // (the hole)
        var through = style == ModStyle.Through;
        var well = style is ModStyle.Flush or ModStyle.Opening;
        var floor = well ? surface + size.Y * 0.35f : surface;
        // The hull: solid under the surface, the hole cut out of it.
        if (style == ModStyle.Proud)
            dl.AddRectFilled(new float2(x0, surface), new float2(x1, bottom), SkHull);
        else
        {
            dl.AddRectFilled(new float2(x0, surface), new float2(wx0, bottom), SkHull);
            dl.AddRectFilled(new float2(wx1, surface), new float2(x1, bottom), SkHull);
            if (well)
                dl.AddRectFilled(new float2(wx0, floor), new float2(wx1, bottom), SkHullDark);
        }
        // The plate: a slab on the surface, or a frame round the hole.
        var pt = surface - plateT;
        if (style == ModStyle.Proud)
            dl.AddRectFilled(new float2(wx0, pt), new float2(wx1, surface), SkPlate);
        else
        {
            dl.AddRectFilled(new float2(wx0 - size.X * 0.06f, pt), new float2(wx0, surface), SkPlate);
            dl.AddRectFilled(new float2(wx1, pt), new float2(wx1 + size.X * 0.06f, surface), SkPlate);
        }
        // The module.
        var (top, foot) = style switch
        {
            ModStyle.Proud => (at.Y + size.Y * 0.1f, pt),
            ModStyle.Flush => (pt, floor),
            ModStyle.Opening => (at.Y + size.Y * 0.12f, floor),
            _ => (at.Y + size.Y * 0.22f, bottom - size.Y * 0.06f),
        };
        dl.AddRectFilled(new float2(mx0, top), new float2(mx1, foot), SkModule, 2);
        if (through)
            dl.AddLine(new float2(mx0, pt), new float2(mx1, pt), SkPlate, MathF.Max(1, _scale)); // (its plug, a frame on the plate's face)
    }

    /// <summary>
    /// The socket's side view under its group: the hull, the plate (or its frame), the well dug Depth deep (an Opening's Depth on behind the
    /// plate) and the room above it, measured off, from the panel's values as they stand.
    /// </summary>
    private void SocketSideView(ConstructPartDef d)
    {
        float Val(CfgKey k, float or) => d.CfgSlot[(int)k] is var sl and >= 0 ? _cfVals[sl] : or;
        var t = ModuleSocketTable.Current;
        var style = (ModStyle)Math.Clamp((int)Val(CfgKey.ModStyle, 0), 0, ModuleSocketTable.StyleLabels.Length - 1);
        var depth = style == ModStyle.Proud ? 0 : MathF.Max(t.DepthStep, Val(CfgKey.ModDepth, t.DepthStep));
        var above = Val(CfgKey.ModAbove, 0) is var a && a > 0 ? a : ModuleSocketTable.DefaultAbove(d.ModW);
        var thick = ModuleSockets.Thickness(d);
        var behind = style switch { ModStyle.Proud => 0, ModStyle.Through => thick + depth, _ => MathF.Max(depth, thick + 0.02f) };
        var room = style == ModStyle.Flush ? 0 : above;
        var dl = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;
        var hPix = 120 * _scale;
        var start = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new float2(width, hPix + 6 * _scale));
        dl.AddRectFilled(start, start + new float2(width, hPix), SkCard, 3 * _scale);
        // The scale: the room and the well fit in the box, and the hull shows at least 0.3 m under the well.
        var spanM = MathF.Max(1f, room + behind + 0.35f);
        var px = (hPix - 22 * _scale) / spanM;
        var surface = start.Y + 8 * _scale + room * px;
        float x0 = start.X + 6 * _scale, x1 = start.X + width - 84 * _scale;
        var mw = MathF.Min((x1 - x0) * 0.5f, d.ModW * px);
        var mid = (x0 + x1) * 0.5f;
        float wx0 = mid - mw * 0.5f - 3 * _scale, wx1 = mid + mw * 0.5f + 3 * _scale;
        var bottom = start.Y + hPix - 4 * _scale;
        var face = surface - thick * px; // (the plate's face: on the hull, its back on the surface)
        // The hull.
        if (behind <= 0)
            dl.AddRectFilled(new float2(x0, surface), new float2(x1, bottom), SkHull);
        else
        {
            var floorY = MathF.Min(bottom, face + behind * px);
            dl.AddRectFilled(new float2(x0, surface), new float2(wx0, bottom), SkHull);
            dl.AddRectFilled(new float2(wx1, surface), new float2(x1, bottom), SkHull);
            if (style != ModStyle.Through)
                dl.AddRectFilled(new float2(wx0, floorY), new float2(wx1, bottom), SkHullDark);
            // The depth marks down the well, every 0.25 m from the face.
            for (var k = 1; k * t.DepthStep < behind - 0.01f; k++)
            {
                var y = face + k * t.DepthStep * px;
                dl.AddLine(new float2(wx0 + 2, y), new float2(wx0 + (k % 4 == 0 ? 10 : 6) * _scale, y), SkDepth, MathF.Max(1, _scale));
            }
        }
        // The plate.
        if (style == ModStyle.Proud)
            dl.AddRectFilled(new float2(wx0, face), new float2(wx1, surface), SkPlate);
        else
        {
            dl.AddRectFilled(new float2(wx0 - 10 * _scale, face), new float2(wx0, surface), SkPlate);
            dl.AddRectFilled(new float2(wx1, face), new float2(wx1 + 10 * _scale, surface), SkPlate);
        }
        // The room, dashed: from the floor (or the face) up its height.
        var roomTop = face - room * px;
        var roomFoot = face + (style == ModStyle.Proud ? 0 : behind) * px;
        Dashed(dl, new float2(wx0 + 1, roomTop), new float2(wx1 - 1, roomTop));
        Dashed(dl, new float2(wx0 + 1, roomTop), new float2(wx0 + 1, roomFoot));
        Dashed(dl, new float2(wx1 - 1, roomTop), new float2(wx1 - 1, roomFoot));
        // The measures on the right.
        var mx = x1 + 8 * _scale;
        if (room > 0)
            Measure(dl, mx, roomTop, face, "Room above", room);
        if (behind > 0)
            Measure(dl, mx, face, face + behind * px, style == ModStyle.Through ? "Depth+plate" : "Depth", style == ModStyle.Through ? behind : depth);
        if (style == ModStyle.Flush)
            dl.AddText(new float2(mx, face - ImGui.GetFontSize() - 2), SkText, "level"u8);
    }

    private void Measure(ImDrawListPtr dl, float x, float y0, float y1, string label, float metres)
    {
        dl.AddLine(new float2(x, y0), new float2(x, y1), SkDepth, MathF.Max(1, _scale));
        dl.AddLine(new float2(x - 3 * _scale, y0), new float2(x + 3 * _scale, y0), SkDepth, MathF.Max(1, _scale));
        dl.AddLine(new float2(x - 3 * _scale, y1), new float2(x + 3 * _scale, y1), SkDepth, MathF.Max(1, _scale));
        _skText.AsSpan().TryWrite($"{label} {metres:0.##} m", out var len);
        var fs = ImGui.GetFontSize();
        var y = MathF.Max(y0, (y0 + y1) * 0.5f - fs * 0.5f);
        dl.AddText(new float2(x + 5 * _scale, y), SkText, _skText.AsSpan(0, len));
    }

    private void Dashed(ImDrawListPtr dl, float2 a, float2 b)
    {
        var d = b - a;
        var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        if (len < 1)
            return;
        var step = 6 * _scale;
        for (var s = 0f; s < len; s += step * 2)
        {
            var e = MathF.Min(len, s + step);
            dl.AddLine(a + d * (s / len), a + d * (e / len), SkRoom, MathF.Max(1, _scale));
        }
    }

    /// <summary>A socket's row that means nothing in its style as the panel has it: Depth on a Proud socket, Room above on a Flush one (greyed).</summary>
    private bool SocketRowIdle(ConstructPartDef d, CConfigDef e)
    {
        if (d.ModPart != ModPart.Socket || d.ModStand || e.Id is not (CfgKey.ModDepth or CfgKey.ModAbove) || d.CfgSlot[(int)CfgKey.ModStyle] is not (var sl and >= 0))
            return false;
        var style = (ModStyle)(int)_cfVals[sl];
        return e.Id == CfgKey.ModDepth ? style == ModStyle.Proud : style == ModStyle.Flush;
    }
}
