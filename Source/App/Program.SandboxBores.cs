using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The holes bores leave (docs/sandbox/design/capital-battles.md; ConstructBores.cs): each one a dark scorched hole on the face,
/// a few millimetres proud of it (never coplanar), with a rim that glows white-hot and cools through orange to a dull red over
/// <see cref="BoreCool"/> seconds; the glow sits between the face and the scorch, so only its rim shows. One instanced batch each,
/// culled by distance and the view; nothing allocates.
/// </summary>
public static unsafe partial class Program
{
    private const float BoreCool = 30f; // seconds a hole's rim takes to cool
    private const float BoreSeen = 900f; // metres a hole is drawn out to (a 0.3 m hole is under a pixel past it)

    private static readonly Vector3 BoreScorch = new(0.05f, 0.045f, 0.04f);
    private static readonly Vector3 BoreSoot = new(0.17f, 0.14f, 0.12f); // (a real hole's rim: lighter than the dark opening, so the hole reads)

    /// <summary>The scorched holes (opaque, after the constructs).</summary>
    private static void DrawBoreScars(double now) => DrawBores(now, glow: false);

    /// <summary>The hot rims (in the glowing tracers' batch).</summary>
    private static void DrawBoreGlow(double now) => DrawBores(now, glow: true);

    private static void DrawBores(double now, bool glow)
    {
        var g = _game;
        var w = g.Constructs;
        if (w.BoreMarkCount == 0)
            return;
        Span<Vector4> view = stackalloc Vector4[6];
        TerrainRenderer.FrustumPlanes(Gfx.ViewProj, view);
        var eye = Gfx.CameraPosition;
        var alpha = g.TickAlpha;
        if (!glow)
            Gfx.BeginBatch();
        for (var k = 0; k < w.BoreMarkCount; k++)
        {
            ref var m = ref w.BoreMarks[k];
            if (m.Body == 0)
                continue;
            var age = (float)(now - m.Born);
            if (glow && age >= BoreCool)
                continue;
            if (!w.BoreMarkLive(k, out var c))
                continue;
            // Where it is drawn: its segment's frame, at the body's drawn pose (the host draws between its last two ticks).
            c.WorldFrame(c.Parts[m.Part].Seg, out var q, out var t);
            var at = t + Vector3.Transform(m.Pos, q);
            var n = Vector3.Transform(m.Normal, q);
            if (g.IsServer && c.PrevPos != c.Pos | c.PrevRot != c.Rot)
            {
                var bp = Vector3.Lerp(c.PrevPos, c.Pos, alpha);
                var turn = Quaternion.Slerp(c.PrevRot, c.Rot, alpha) * Quaternion.Conjugate(c.Rot);
                at = bp + Vector3.Transform(at - c.Pos, turn);
                n = Vector3.Transform(n, turn);
            }
            if (Vector3.DistanceSquared(at, eye) > BoreSeen * BoreSeen)
                continue;
            var r = m.Radius;
            if (!TerrainRenderer.Visible(view, at - new Vector3(r * 2), at + new Vector3(r * 2)))
                continue;
            var x = MathF.Abs(n.Y) < 0.9f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, n)) : Vector3.UnitX;
            var y = Vector3.Cross(n, x);
            if (w.BoreMarkHoled(k, c, out var ha, out var hu))
            {
                // A real hole (ConstructBores.Cut): a scorched hexagonal rim round the square opening, covering its corners, so a
                // hexagon is what shows; the hot rim just past it, never over the opening.
                ha = Vector3.Transform(ha, q);
                hu = Vector3.Transform(hu, q);
                if (g.IsServer && c.PrevPos != c.Pos | c.PrevRot != c.Rot)
                {
                    var turn = Quaternion.Slerp(c.PrevRot, c.Rot, alpha) * Quaternion.Conjugate(c.Rot);
                    ha = Vector3.Transform(ha, turn);
                    hu = Vector3.Transform(hu, turn);
                }
                if (!glow)
                {
                    BoreHex(ha, hu, n, HexInner, HexOuter, 0.0015f, at + n * 0.006f, BoreSoot, 1f, 0);
                    continue;
                }
                var hc = age / BoreCool;
                BoreHex(ha, hu, n, HexOuter - 0.1f, HexOuter + 0.3f - 0.12f * hc, 0.0005f, at + n * 0.0025f, BoreHeat(hc), (1f - hc) * (1f - hc), 2);
                continue;
            }
            if (!glow)
            {
                // Two squares turned 45 degrees: an octagon of scorch, 4.5-7.5 mm out of the face.
                Gfx.Draw(Basis(x, y, n, r, r, 0.0015f, at + n * 0.006f), BoreScorch, 1f, material: Material.Metal);
                Gfx.Draw(Basis((x + y) * 0.70710677f, (y - x) * 0.70710677f, n, r, r, 0.0015f, at + n * 0.006f), BoreScorch, 1f, material: Material.Metal);
                continue;
            }
            var cool = age / BoreCool;
            var heat = (1f - cool) * (1f - cool);
            var colour = BoreHeat(cool);
            var rim = r * (1.45f - 0.25f * cool);
            // Between the face and the scorch (2-3 mm out): only the rim round the scorch shows.
            Gfx.Draw(Basis(x, y, n, rim, rim, 0.0005f, at + n * 0.0025f), colour, heat, mode: 2);
            Gfx.Draw(Basis((x + y) * 0.70710677f, (y - x) * 0.70710677f, n, rim, rim, 0.0005f, at + n * 0.0025f), colour, heat, mode: 2);
        }
        if (!glow)
            Gfx.EndBatch();
    }

    /// <summary>A rim's colour as it cools (0: just made, 1: cold): white-hot, orange, dull red.</summary>
    private static Vector3 BoreHeat(float cool) => cool < 0.15f ? Vector3.Lerp(new Vector3(1f, 0.95f, 0.75f), new Vector3(1f, 0.55f, 0.15f), cool / 0.15f)
        : Vector3.Lerp(new Vector3(1f, 0.55f, 0.15f), new Vector3(0.55f, 0.07f, 0.02f), (cool - 0.15f) / 0.85f);

    // A hexagonal rim in units of the opening's half side: its inner corners on the square's side middles (a hexagon inside the
    // square), its outer edge past the square's corners (they are 1.41 out; the hexagon's edge at 45 degrees is 0.9 of its corners).
    private const float HexInner = 1f, HexOuter = 1.75f;

    /// <summary>
    /// A hexagonal rim flat on a face, from <paramref name="inner"/> to <paramref name="outer"/> (corner distances, in units of
    /// <paramref name="a"/> and <paramref name="b"/>, the opening's half sides on the face, corners along <paramref name="a"/>): six
    /// boxes, each with its inner edge on a side of the inner hexagon and running on past its corners (outside the hexagon, which is
    /// convex) to close the gap with the next; every other one a hair further out, so the overlaps never share a plane.
    /// </summary>
    private static void BoreHex(Vector3 a, Vector3 b, Vector3 n, float inner, float outer, float thick, Vector3 at, Vector3 colour, float alpha, int mode)
    {
        var wide = (outer - inner) * 0.8660254f; // (the rim's width square to each side)
        for (var s = 0; s < 6; s++)
        {
            // Side s runs from corner s to corner s+1; its middle is out along 30 + 60s degrees at the apothem.
            var (s0, c0) = MathF.SinCos(s * MathF.PI / 3f);
            var (s1, c1) = MathF.SinCos((s + 1) * MathF.PI / 3f);
            var (sm, cm) = MathF.SinCos((s * 2 + 1) * MathF.PI / 6f);
            var along = new Vector2(c1 - c0, s1 - s0) * inner; // (the side: as long as the corner distance)
            var outward = new Vector2(cm, sm);
            var mid = outward * (inner * 0.8660254f + wide * 0.5f);
            var len = 0.5f + wide * 0.57735027f / inner + 0.02f; // (half its length, as a share of the side: on past each corner by wide x tan 30)
            Vector3 W(Vector2 v) => a * v.X + b * v.Y;
            var m = Basis(W(along * len), W(outward * (wide * 0.5f)), n, 1f, 1f, thick, at + W(mid) + n * (s % 2 * 0.0004f));
            if (mode == 0)
                Gfx.Draw(m, colour, alpha, material: Material.Metal);
            else
                Gfx.Draw(m, colour, alpha, mode: mode);
        }
    }

    /// <summary>The unit cube as a box of half sizes <paramref name="hx"/>, <paramref name="hy"/>, <paramref name="hz"/> along x, y, n, centred at <paramref name="at"/>.</summary>
    private static Matrix4x4 Basis(Vector3 x, Vector3 y, Vector3 n, float hx, float hy, float hz, Vector3 at)
    {
        x *= hx * 2;
        y *= hy * 2;
        n *= hz * 2;
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, n.X, n.Y, n.Z, 0, at.X, at.Y, at.Z, 1);
    }
}
