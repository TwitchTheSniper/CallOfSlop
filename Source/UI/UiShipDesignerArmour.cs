using System.Text.Json;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Ship Designer's plate screen (Karl, 2026-11-27): a stand-off armour plate seen flat, its length across and its height up, its top
/// and bottom edges' control points dragged up or down to sculpt its outline (DesignArmour.EdgeAt), its rams marked where they stand. The
/// page's armourOutline draws the same.
/// </summary>
public sealed partial class Ui
{
    private (bool Top, int K) _armDrag = (false, -1);
    private double _armSc, _armOy; // the view's scale and its middle's height, held while a point is dragged
    private double _armLast;

    private void ArmourOutline(JsonElement p, int pid)
    {
        double w = D(p, "armW"), h = D(p, "armH");
        var top = Arr(p, "armTop").Select(e => e.GetDouble()).ToArray();
        var bot = Arr(p, "armBot").Select(e => e.GetDouble()).ToArray();
        var n = top.Length;
        if (n < 2 || bot.Length != n)
            return;
        var W = ImGui.GetContentRegionAvail().X;
        var Hh = W * 0.62f;
        var pad = 16 * _scale;
        double yHi = h / 2 + Math.Max(0, top.Max()), yLo = -h / 2 - Math.Max(0, bot.Max());
        var sc = Math.Min((W - pad * 2) / Math.Max(w, 0.5), (Hh - pad * 2) / Math.Max(yHi - yLo, 0.5));
        var p0 = ImGui.GetCursorScreenPos();
        var oy = Hh / 2 + (yHi + yLo) / 2 * sc;
        if (_armDrag.K >= 0 && _armSc > 0)
            (sc, oy) = (_armSc, _armOy);
        var x0 = p0.X + W / 2 - w / 2 * sc;
        float X(double v) => (float)(x0 + v * w * sc);
        float Y(double y) => (float)(p0.Y + oy - y * sc);
        ImGui.InvisibleButton("##sdarmout"u8, new float2(W, Hh));
        var dl = ImGui.GetWindowDrawList();
        var pEnd = p0 + new float2(W, Hh);
        ImDrawListExtensions.AddRectFilled(dl, in p0, in pEnd, new ImColor8(14, 19, 23, 255), 4, default);
        // The plate's square outline, dashed, under its sculpted one.
        var r0 = new float2(X(0), Y(h / 2));
        var r1 = new float2(X(1), Y(-h / 2));
        ImDrawListExtensions.AddRect(dl, in r0, in r1, Hex8(0x33414b), 0, default, 1);
        var poly = new List<float2>();
        for (var k = 0; k < n; k++)
            poly.Add(new float2(X((double)k / (n - 1)), Y(h / 2 + top[k])));
        for (var k = n - 1; k >= 0; k--)
            poly.Add(new float2(X((double)k / (n - 1)), Y(-h / 2 - bot[k])));
        poly.Add(poly[0]);
        ImDrawListExtensions.AddPolyline(dl, poly.ToArray(), Hex8(0xffb02e), ImDrawFlags.None, 2);
        // Its rams, where they stand along its middle.
        var rams = Str(p, "armStruts") switch { "Arm" => 1, "Row of 4" => 4, _ => 3 };
        for (var b = 0; b < rams; b++)
        {
            var v = (b + 0.5) / rams;
            var mid = (ShipDesigner.DesignArmour.EdgeAt(top, v) - ShipDesigner.DesignArmour.EdgeAt(bot, v)) / 2;
            var c = new float2(X(v), Y(mid));
            ImDrawListExtensions.AddCircleFilled(dl, in c, (rams == 1 ? 9 : 6) * _scale, Hex8(0x6a7480), 16);
        }
        var mouse = ImGui.GetIO().MousePos;
        if (ImGui.IsItemActivated())
        {
            _armDrag = (false, -1);
            var best = 10 * _scale;
            for (var k = 0; k < n; k++)
                foreach (var isTop in new[] { true, false })
                {
                    var py = isTop ? Y(h / 2 + top[k]) : Y(-h / 2 - bot[k]);
                    var dd = MathF.Sqrt(MathF.Pow(mouse.X - X((double)k / (n - 1)), 2) + MathF.Pow(mouse.Y - py, 2));
                    if (dd < best)
                        (best, _armDrag) = (dd, (isTop, k));
                }
            (_dragFirst, _armSc, _armOy) = (true, sc, oy);
            _armLast = _armDrag.K < 0 ? 0 : _armDrag.Top ? top[_armDrag.K] : bot[_armDrag.K];
        }
        if (ImGui.IsItemActive() && _armDrag.K >= 0 && _armDrag.K < n)
        {
            var my = Math.Clamp(mouse.Y, p0.Y, p0.Y + Hh);
            var y = (p0.Y + oy - my) / sc;
            var at = _armDrag.Top ? y - h / 2 : -h / 2 - y; // (an edge's offset: out past the plate's height positive)
            at = Math.Clamp(Math.Round(at / 0.05) * 0.05, -h / 2 + ShipDesigner.DesignArmour.MinEdgeGap / 2, ShipDesigner.DesignArmour.EdgeMax);
            if (Math.Abs(at - _armLast) > 1e-4)
            {
                _armLast = at;
                DragCmd(C("setPiece", ("id", pid), ("armEdge", _armDrag.Top ? "top" : "bot"), ("k", _armDrag.K), ("at", at)));
            }
        }
        if (ImGui.IsItemDeactivated() && _armDrag.K >= 0)
        {
            (_armDrag, _armSc) = ((false, -1), 0);
            Flush();
        }
        for (var k = 0; k < n; k++)
            foreach (var isTop in new[] { true, false })
            {
                var c = new float2(X((double)k / (n - 1)), isTop ? Y(h / 2 + top[k]) : Y(-h / 2 - bot[k]));
                ImDrawListExtensions.AddCircleFilled(dl, in c, 7 * _scale, Hex8(0x1b2329), 16);
                ImDrawListExtensions.AddCircleFilled(dl, in c, 5.5f * _scale, Hex8(0xffb02e), 16);
            }
        var lab = new float2(p0.X + 8, p0.Y + Hh - ImGui.GetTextLineHeight() - 6);
        ImDrawListExtensions.AddText(dl, in lab, Hex8(0x93a3ad), $"{Fmt(w)} m along - drag the edges");
    }
}
