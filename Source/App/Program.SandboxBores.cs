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
            if (w.BoreMarkHoled(k, c, out var facing))
            {
                // A real hole (ConstructBores.Cut): a scorched ring round the opening, and the hot rim just past it, never over it.
                var inner = r * ConstructBores.HoleScale * 1.45f / facing;
                if (!glow)
                {
                    BoreRing(x, y, n, inner, inner + r * 0.7f, 0.0015f, at + n * 0.006f, BoreScorch, 1f, 0);
                    continue;
                }
                var hc = age / BoreCool;
                BoreRing(x, y, n, inner * 0.97f, inner + r * (0.9f - 0.15f * hc), 0.0005f, at + n * 0.0025f, BoreHeat(hc), (1f - hc) * (1f - hc), 2);
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

    /// <summary>An octagonal ring flat on a face (x, y in it, n out of it) from <paramref name="inner"/> to <paramref name="outer"/>: eight thin boxes.</summary>
    private static void BoreRing(Vector3 x, Vector3 y, Vector3 n, float inner, float outer, float thick, Vector3 at, Vector3 colour, float alpha, int mode)
    {
        var mid = (inner + outer) * 0.5f;
        var halfLen = outer * 0.41421357f; // (tan 22.5 degrees: the segments meet at the outer edge)
        var halfWide = (outer - inner) * 0.5f;
        for (var s = 0; s < 8; s++)
        {
            var (sin, cos) = MathF.SinCos(s * MathF.PI * 0.25f);
            var radial = x * cos + y * sin;
            var tangent = y * cos - x * sin;
            var m = Basis(tangent, radial, n, halfLen, halfWide, thick, at + radial * mid);
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
