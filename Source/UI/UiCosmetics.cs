using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The cosmetic side of the loadout screen: the Hat card on the overview (beside Lethal and Tactical) and the COSMETICS
/// page, with a card for "No hat" and each prestige hat (a small drawing of the hat on a head, its name and whether it's
/// yours). The hat is one choice for the whole profile (<see cref="LoadoutStore.Hat"/>): it's who you are, so it goes
/// with every loadout. Locked hats are greyed with the prestige that unlocks them.
/// </summary>
public sealed partial class Ui
{
    /// <summary>Hot-card key of the overview's hat card (the other cards use 0-5 and 10+).</summary>
    private const int HatCardKey = 6;

    /// <summary>Cards on the cosmetics page: columns x rows (No hat + the five hats).</summary>
    internal const int HatCols = 3, HatRows = 2;

    /// <summary>The smallest a hat card may be (unscaled px): the drawing plus three lines of text.</summary>
    internal const float HatCardMinW = 200, HatCardMinH = 150;

    /// <summary>Room the page's heading and its line of help take above the cards (unscaled px, two wrapped lines; the self-test's budget).</summary>
    internal const float HatHeaderH = 64;

    /// <summary>Height of a hat card's text block (unscaled px), under the drawing.</summary>
    private const float HatTextH = 74;

    /// <summary>Width of each card in the overview's Lethal / Tactical / Hat row.</summary>
    internal static float OverviewThirdWidth(float width, float gap) => MathF.Max(0, (width - 2 * gap) / 3);

    /// <summary>
    /// Where the hat cards go in an area (screen px): <see cref="HatCols"/> x <see cref="HatRows"/>, left to right then
    /// down, separated by <paramref name="gap"/>; not taller than they need to be. Pure (the self-test checks it at the
    /// user's resolutions: inside the area, no overlaps, at least the minimum size).
    /// </summary>
    internal static (float2 Min, float2 Max)[] HatCardRects(float2 origin, float2 area, float scale, float gap)
    {
        var w = MathF.Max(0, (area.X - gap * (HatCols - 1)) / HatCols);
        var fit = MathF.Max(0, (area.Y - gap * (HatRows - 1)) / HatRows);
        var h = MathF.Min(fit, MathF.Max(w * 0.8f, HatCardMinH * scale));
        var rects = new (float2, float2)[HatCols * HatRows];
        for (var i = 0; i < rects.Length; i++)
        {
            var min = origin + new float2((i % HatCols) * (w + gap), (i / HatCols) * (h + gap));
            rects[i] = (min, min + new float2(w, h));
        }
        return rects;
    }

    /// <summary>The cards in order: No hat, then the catalog in prestige order.</summary>
    internal static HatId[] HatChoices() => [HatId.None, .. Cosmetics.Hats.Select(h => h.Id)];

    /// <summary>"Prestige 3" style requirement, or "" for no hat.</summary>
    private static string HatRequirement(HatId id) => Cosmetics.Get(id) is { } def ? $"Prestige {def.Prestige}" : "";

    // ---- Overview card ---------------------------------------------------------------------------

    /// <summary>The Hat card on the overview: the hat you'll wear (with a little drawing). Returns true when clicked.</summary>
    private bool HatCard(HatId hat, float width, float height, ref int hot)
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, _hotCard == HatCardKey ? CardHover : CardBg);
        if (ImGui.BeginChild("##hatcard"u8, new float2(width, height), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.TextColored(Orange, "Hat"u8);
            ImGuiSafe.TextColored(new float4(1, 1, 1, 1), Cosmetics.Get(hat)?.Name ?? "No hat");
            ImGui.TextColored(Dim, "All loadouts"u8);
            var r = height * 0.42f;
            DrawHatPreview(ImGui.GetWindowDrawList(), hat, pos + new float2(width - r - 10 * _scale, height / 2), r, false);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
            hot = HatCardKey;
        return ImGui.IsItemClicked(ImGuiMouseButton.Left) && !SecondClick;
    }

    // ---- Cosmetics page --------------------------------------------------------------------------

    /// <summary>The COSMETICS page: pick No hat or any hat your prestige unlocks (click or double-click a card).</summary>
    private void DrawCosmeticsPicker(Audio audio, float2 area)
    {
        var prestige = CosmeticPrestige.Of(Progress);
        var top = ImGui.GetCursorScreenPos().Y;
        ImGui.PushFont(default, 20);
        ImGui.TextColored(Orange, "HAT"u8);
        ImGui.PopFont();
        ImGui.PushStyleColor(ImGuiCol.Text, Dim);
        ImGuiSafe.TextWrapped($"Cosmetic only, worn with every loadout. Hats unlock with prestige (max level, then prestige on the progression page). Your prestige: {prestige}.");
        ImGui.PopStyleColor();
        ImGui.Spacing();
        var origin = ImGui.GetCursorScreenPos();
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var gridArea = new float2(area.X, MathF.Max(0, area.Y - (origin.Y - top)));
        var rects = HatCardRects(origin, gridArea, _scale, gap);
        var choices = HatChoices();
        var dl = ImGui.GetWindowDrawList();
        for (var i = 0; i < rects.Length && i < choices.Length; i++)
        {
            var id = choices[i];
            var (min, max) = rects[i];
            var unlocked = Cosmetics.IsUnlocked(id, prestige);
            var selected = LoadoutStore.Hat == id;
            var worn = LoadoutStore.EffectiveHat(Progress) == id;
            ImGui.SetCursorScreenPos(min);
            ImGui.InvisibleButton($"##hat{i}", new float2(MathF.Max(1, max.X - min.X), MathF.Max(1, max.Y - min.Y)));
            var hot = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !SecondClick)
            {
                if (!unlocked)
                    LockedFeedback(audio);
                else if (!selected)
                {
                    LoadoutStore.Hat = id;
                    LoadoutStore.Save();
                    audio.Effect(Sfx.HitTick, 0.4f);
                }
            }

            var rr = 4 * _scale;
            var bg = worn ? new ImColor8(41, 51, 66, 248) : !unlocked ? new ImColor8(18, 18, 20, 220) : hot ? Hover8 : Card8;
            dl.AddRectFilled(min, max, bg, rr);
            dl.AddRect(min, max, worn ? Orange8 : hot && unlocked ? new ImColor8(115, 190, 242, 255) : new ImColor8(60, 64, 74, 255), rr, ImDrawFlags.None,
                (worn ? 2f : 1f) * _scale);
            var clip = new float4(min.X, min.Y, max.X, max.Y);
            var pad = 10 * _scale;
            var textH = HatTextH * _scale;
            var viewMax = new float2(max.X - pad, max.Y - textH);
            var viewMin = min + new float2(pad, pad);
            var r = MathF.Max(4, MathF.Min(viewMax.X - viewMin.X, viewMax.Y - viewMin.Y) * 0.5f);
            DrawHatPreview(dl, id, (viewMin + viewMax) * 0.5f, r, !unlocked);
            if (!unlocked)
                DrawPadlock(dl, new float2(max.X - 22 * _scale, min.Y + 24 * _scale), 22 * _scale, new ImColor8(150, 150, 158, 255));
            else if (worn)
                DrawStar(dl, new float2(max.X - 18 * _scale, min.Y + 18 * _scale), 8 * _scale, Orange8);

            var def = Cosmetics.Get(id);
            var y = max.Y - textH;
            ImGui.PushFont(default, 20);
            DlText(dl, new float2(min.X + pad, y), unlocked ? White8 : Dim8, def?.Name ?? "No hat", clip);
            ImGui.PopFont();
            ImGui.PushFont(default, 15);
            var status = worn ? "Wearing" : unlocked ? def == null ? "Bare-headed" : "Unlocked - click to wear" : $"LOCKED - {HatRequirement(id)}";
            DlText(dl, new float2(min.X + pad, y + 26 * _scale), worn ? Orange8 : unlocked ? new ImColor8(115, 190, 242, 255) : new ImColor8(220, 120, 90, 255), status, clip);
            var reason = def == null ? "Nothing on your head."
                : unlocked ? $"Earned at prestige {def.Prestige}."
                : $"Max level, then prestige {def.Prestige} time{(def.Prestige == 1 ? "" : "s")} (you: {prestige}).";
            DlText(dl, new float2(min.X + pad, y + 48 * _scale), Dim8, reason, clip);
            ImGui.PopFont();
        }
    }

    // ---- The hat drawing -------------------------------------------------------------------------

    private static readonly HatBox HeadBox = new(Matrix4x4.CreateScale(0.42f, 0.5f, 0.42f) * Matrix4x4.CreateTranslation(0, -0.25f, 0), new Vector3(0.78f, 0.62f, 0.5f));

    /// <summary>Unit-cube faces (corner indices: bit 0 = x, bit 1 = y, bit 2 = z).</summary>
    private static readonly int[][] CubeFaces = [[0, 2, 6, 4], [1, 5, 7, 3], [0, 4, 5, 1], [2, 3, 7, 6], [0, 1, 3, 2], [4, 6, 7, 5]];

    /// <summary>
    /// A small drawing of a hat on a head (three-quarter view, a little from above), fitted in a circle of radius
    /// <paramref name="r"/> around <paramref name="c"/>: each box of the hat's model, its visible faces shaded, far boxes first.
    /// Greyed when <paramref name="locked"/>. No hat: just the head.
    /// </summary>
    private static void DrawHatPreview(ImDrawListPtr dl, HatId id, float2 c, float r, bool locked)
    {
        var def = Cosmetics.Get(id);
        var view = Matrix4x4.CreateRotationY(MathF.PI + 0.6f) * Matrix4x4.CreateRotationX(0.32f); // face turned towards us, from a bit above
        var boxes = new List<HatBox>(1 + (def?.Boxes.Count ?? 0)) { HeadBox };
        if (def != null)
            boxes.AddRange(def.Boxes);

        // Fit everything in the circle.
        var lo = new Vector2(float.MaxValue);
        var hi = new Vector2(float.MinValue);
        Span<Vector3> pts = stackalloc Vector3[8];
        foreach (var b in boxes)
        {
            Corners(b.Local * view, pts);
            foreach (var p in pts)
            {
                lo = Vector2.Min(lo, new Vector2(p.X, p.Y));
                hi = Vector2.Max(hi, new Vector2(p.X, p.Y));
            }
        }
        var ext = hi - lo;
        var k = 2 * r / MathF.Max(MathF.Max(ext.X, ext.Y), 0.01f);
        var mid = (lo + hi) * 0.5f;
        float2 Screen(Vector3 p) => new(c.X + (p.X - mid.X) * k, c.Y - (p.Y - mid.Y) * k);

        // Head first (the hat sits on it), then the hat's boxes far to near.
        var order = Enumerable.Range(1, boxes.Count - 1)
            .OrderBy(i => Vector3.Transform(Vector3.Zero, boxes[i].Local * view).Z).Prepend(0);
        Span<float2> quad = stackalloc float2[4];
        var light = Vector3.Normalize(new Vector3(-0.4f, 0.8f, 0.6f));
        foreach (var i in order)
        {
            var b = boxes[i];
            Corners(b.Local * view, pts);
            var centre = Vector3.Zero;
            foreach (var p in pts)
                centre += p / 8;
            foreach (var f in CubeFaces)
            {
                var n = Vector3.Cross(pts[f[1]] - pts[f[0]], pts[f[3]] - pts[f[0]]);
                if (Vector3.Dot(n, (pts[f[0]] + pts[f[2]]) * 0.5f - centre) < 0)
                    n = -n; // point it out of the box
                if (n.Z <= 1e-7f)
                    continue; // facing away (the camera looks down -Z)
                n = Vector3.Normalize(n);
                var shade = 0.55f + 0.45f * MathF.Max(0, Vector3.Dot(n, light));
                var col = b.Color * shade;
                if (locked)
                {
                    var g = (col.X + col.Y + col.Z) / 3 * 0.6f + 0.08f;
                    col = new Vector3(g, g, g * 1.05f);
                }
                for (var q = 0; q < 4; q++)
                    quad[q] = Screen(pts[f[q]]);
                ImDrawListExtensions.AddConvexPolyFilled(dl, quad, new ImColor8(
                    (byte)Math.Clamp(col.X * 255, 0, 255), (byte)Math.Clamp(col.Y * 255, 0, 255), (byte)Math.Clamp(col.Z * 255, 0, 255), 255));
            }
            if (i == 0)
            {
                // Eyes, so you can tell which way the head faces.
                var eyeCol = new ImColor8(25, 25, 30, 255);
                foreach (var ex in new[] { -0.09f, 0.09f })
                {
                    var e = Screen(Vector3.Transform(new Vector3(ex, -0.2f, -0.215f), view));
                    ImDrawListExtensions.AddCircleFilled(dl, in e, MathF.Max(1f, 0.03f * k), eyeCol, 8);
                }
            }
        }
    }

    /// <summary>The 8 corners of a transformed unit cube (bit 0 = x, bit 1 = y, bit 2 = z).</summary>
    private static void Corners(Matrix4x4 m, Span<Vector3> pts)
    {
        for (var c = 0; c < 8; c++)
            pts[c] = Vector3.Transform(new Vector3((c & 1) - 0.5f, ((c >> 1) & 1) - 0.5f, ((c >> 2) & 1) - 0.5f), m);
    }
}
