using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The cracks rounds leave in ship designs' windows (DesignGlassCracks.cs): each line a thin pale box through the pane, a few millimetres
/// proud of both its faces (never coplanar with the glass), drawn out from the point the round struck as the crack runs, so a hit is seen
/// to star the glass. One batch, culled by distance and the view; nothing allocates.
/// </summary>
public static unsafe partial class Program
{
    private const float CrackSeen = 160f; // metres cracks are drawn out to (a crack's lines are under a pixel past it)
    private static readonly Vector3 CrackColour = new(0.86f, 0.9f, 0.94f);

    private static void DrawGlassCracks(double now)
    {
        var g = _game;
        var w = g.Constructs;
        if (w.GlassCrackCount == 0)
            return;
        Span<Vector4> view = stackalloc Vector4[6];
        TerrainRenderer.FrustumPlanes(Gfx.ViewProj, view);
        var eye = Gfx.CameraPosition;
        var alpha = g.TickAlpha;
        var begun = false;
        for (var k = 0; k < w.GlassCrackCount; k++)
        {
            ref var m = ref w.GlassCracks[k];
            if (m.Body == 0 || !w.GlassCrackLive(k, out var c))
                continue;
            c.WorldFrame(c.Parts[m.Part].Seg, out var q, out var t);
            var at = t + Vector3.Transform(m.Pos, q);
            var x = Vector3.Transform(m.X, q);
            var y = Vector3.Transform(m.Y, q);
            var n = Vector3.Transform(m.N, q);
            if (g.IsServer && c.PrevPos != c.Pos | c.PrevRot != c.Rot)
            {
                // The body's drawn pose (the host draws between its last two ticks).
                var bp = Vector3.Lerp(c.PrevPos, c.Pos, alpha);
                var turn = Quaternion.Slerp(c.PrevRot, c.Rot, alpha) * Quaternion.Conjugate(c.Rot);
                at = bp + Vector3.Transform(at - c.Pos, turn);
                x = Vector3.Transform(x, turn);
                y = Vector3.Transform(y, turn);
                n = Vector3.Transform(n, turn);
            }
            if (Vector3.DistanceSquared(at, eye) > CrackSeen * CrackSeen)
                continue;
            var r = m.Radius;
            if (!TerrainRenderer.Visible(view, at - new Vector3(r), at + new Vector3(r)))
                continue;
            if (!begun)
            {
                Gfx.BeginBatch();
                begun = true;
            }
            var run = (float)(now - m.Born) * m.Speed; // (how far out the crack has run)
            var half = m.Depth * 0.5f + 0.003f; // (through the pane, 3 mm proud of each face)
            var mid = at - n * (m.Depth * 0.5f);
            var first = k * ConstructWorld.MaxGlassCrackSegs;
            for (var s = 0; s < m.Segs; s++)
            {
                ref var seg = ref w.GlassCrackSegs[first + s];
                if (run < seg.Start)
                    continue;
                var f = run >= seg.End ? 1f : (run - seg.Start) / (seg.End - seg.Start);
                var b = Vector2.Lerp(seg.A, seg.B, f);
                var d = b - seg.A;
                var len = d.Length();
                if (len < 1e-4f)
                    continue;
                d /= len;
                var along = x * d.X + y * d.Y;
                var side = Vector3.Cross(n, along);
                var centre = mid + x * ((seg.A.X + b.X) * 0.5f) + y * ((seg.A.Y + b.Y) * 0.5f);
                Gfx.Draw(Basis(along, side, n, len * 0.5f + seg.Width * 0.5f, seg.Width * 0.5f, half, centre), CrackColour, 1f, material: Material.Metal);
            }
        }
        if (begun)
            Gfx.EndBatch();
    }
}
