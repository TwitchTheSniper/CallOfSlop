using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Cable Trays and Clamps (docs/sandbox/CONSTRUCTS.md, "Cable alignment"): parts with lanes a link lies in. A node placed on one snaps
/// to its nearest lane, the auto-router prefers its lanes, and what runs in one takes a fifth of the damage: a hose in a tray doesn't
/// get cut by a stray round. A part's <c>tray</c> block gives the lanes in its own frame. A construct with none costs nothing.
/// </summary>
public static class ConstructTrays
{
    public const float Shield = 0.8f; // the share of a hit a tray takes off a link running all the way through its guard
    private const int Samples = 5; // points along a segment

    /// <summary>The middle of lane <paramref name="lane"/> of part <paramref name="part"/> (the part's own frame).</summary>
    private static Vector3 Lane(Construct c, int part, int lane)
    {
        var d = c.DefOf(part);
        return d.TrayAt + d.TrayAcross * (lane - (d.TrayLanes - 1) * 0.5f) * d.TrayPitch;
    }

    /// <summary>Lane <paramref name="lane"/>'s two ends on the construct, in the frame of the part's segment.</summary>
    public static void LaneEnds(Construct c, int part, int lane, out Vector3 a, out Vector3 b)
    {
        ref var p = ref c.Parts[part];
        var d = c.DefOf(part);
        var scale = c.ScaleOf(part);
        var mid = Lane(c, part, lane);
        a = p.Pos + Rot24.Apply(p.Rot, (mid - d.TrayAxis * d.TrayLength * 0.5f) * scale);
        b = p.Pos + Rot24.Apply(p.Rot, (mid + d.TrayAxis * d.TrayLength * 0.5f) * scale);
    }

    /// <summary>A node aimed at <paramref name="p"/> on tray <paramref name="part"/>: the nearest point of its nearest lane. False when the part has no lanes.</summary>
    public static bool Snap(Construct c, int part, Vector3 p, out Vector3 at)
    {
        at = p;
        var d = c.DefOf(part);
        if (d.TrayLanes <= 0)
            return false;
        var best = float.MaxValue;
        for (var lane = 0; lane < d.TrayLanes; lane++)
        {
            LaneEnds(c, part, lane, out var a, out var b);
            var ab = b - a;
            var near = a + ab * Math.Clamp(Vector3.Dot(p - a, ab) / MathF.Max(1e-6f, ab.LengthSquared()), 0f, 1f);
            var dist = Vector3.DistanceSquared(p, near);
            if (dist < best)
            {
                best = dist;
                at = near;
            }
        }
        at = ConstructWorld.Quantise(at);
        return true;
    }

    /// <summary>How much of the run from <paramref name="a"/> to <paramref name="b"/> (rest frame) lies in a tray's or clamp's guard, 0 to 1.</summary>
    public static float Cover(Construct c, Vector3 a, Vector3 b)
    {
        var inside = 0;
        for (var s = 0; s < Samples; s++)
        {
            var p = Vector3.Lerp(a, b, (s + 0.5f) / Samples);
            for (var t = 0; t < c.TrayCount; t++)
                if (Guards(c, c.Trays![t], p))
                {
                    inside++;
                    break;
                }
        }
        return inside / (float)Samples;
    }

    private static bool Guards(Construct c, int part, Vector3 rest)
    {
        var d = c.DefOf(part);
        ref var pt = ref c.Parts[part];
        var scale = c.ScaleOf(part);
        var mid = c.ToRest(pt.Seg, pt.Pos + Rot24.Apply(pt.Rot, d.TrayAt * scale));
        var ax = Vector3.Normalize(c.ToRest(pt.Seg, pt.Pos + Rot24.Apply(pt.Rot, (d.TrayAt + d.TrayAxis) * scale)) - mid);
        var across = Vector3.Normalize(c.ToRest(pt.Seg, pt.Pos + Rot24.Apply(pt.Rot, (d.TrayAt + d.TrayAcross) * scale)) - mid);
        var v = rest - mid;
        var along = MathF.Abs(Vector3.Dot(v, ax));
        var side = MathF.Abs(Vector3.Dot(v, across));
        var up = (v - ax * Vector3.Dot(v, ax) - across * Vector3.Dot(v, across)).Length();
        return along <= d.TrayGuard * 0.5f * scale && side <= d.TrayPitch * d.TrayLanes * 0.5f * scale && up <= d.TrayPitch * scale;
    }

    /// <summary>A part file's <c>tray</c> block.</summary>
    public static void Parse(ConstructPartDef d, JsonElement root, string file)
    {
        if (ConstructParts.Opt(root, "tray") is not { } t)
            return;
        ConstructParts.Known(t, ["at", "axis", "across", "lanes", "pitch", "length", "guard"], $"{file}, tray");
        d.TrayAt = ConstructParts.Vec(ConstructParts.Need(t, "at", file), file, "tray.at");
        d.TrayAxis = ConstructParts.Vec(ConstructParts.Need(t, "axis", file), file, "tray.axis");
        d.TrayAcross = ConstructParts.Vec(ConstructParts.Need(t, "across", file), file, "tray.across");
        d.TrayLanes = (int)ConstructParts.Num(ConstructParts.Need(t, "lanes", file), file, "tray.lanes");
        d.TrayPitch = ConstructParts.Num(ConstructParts.Need(t, "pitch", file), file, "tray.pitch");
        d.TrayLength = ConstructParts.Num(ConstructParts.Need(t, "length", file), file, "tray.length");
        d.TrayGuard = ConstructParts.Opt(t, "guard") is { } g ? ConstructParts.Num(g, file, "tray.guard") : d.TrayLength;
        if (d.TrayLanes is < 1 or > 32 || d.TrayPitch <= 0 || d.TrayLength <= 0 || d.TrayGuard <= 0)
            throw new ConstructPartException($"{file}: a tray needs 1 to 32 lanes, a pitch and a length");
        if (MathF.Abs(d.TrayAxis.Length() - 1) > 1e-3f || MathF.Abs(d.TrayAcross.Length() - 1) > 1e-3f || MathF.Abs(Vector3.Dot(d.TrayAxis, d.TrayAcross)) > 1e-3f)
            throw new ConstructPartException($"{file}: a tray's axis and across are unit vectors at right angles");
    }
}
