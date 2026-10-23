using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Hat helpers shared by the APPEARANCE screen (UiAppearance.cs), the lobby room and the scoreboard: the list of hat
/// choices, their unlock text and a small drawing of a hat on a head. The hat is one choice for the whole profile
/// (<see cref="AppearanceStore.Hat"/>); locked hats are greyed with the prestige that unlocks them.
/// </summary>
public sealed partial class Ui
{
    /// <summary>Width of each card in the loadout overview's Lethal / Tactical row.</summary>
    internal static float OverviewHalfWidth(float width, float gap) => MathF.Max(0, (width - gap) / 2);

    /// <summary>The hat choices in order: No hat, then the catalog in prestige order.</summary>
    internal static HatId[] HatChoices() => [HatId.None, .. Cosmetics.Hats.Select(h => h.Id)];

    /// <summary>"Prestige 3" style requirement, or "" for no hat.</summary>
    private static string HatRequirement(HatId id) => Cosmetics.Get(id) is { } def ? $"Prestige {def.Prestige}" : "";

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
