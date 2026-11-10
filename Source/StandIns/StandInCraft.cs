using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// STAND-IN (placeholder, Source/StandIns/README.md): the shapes of the stand-in craft. Not constructs, not vehicles: a
/// handful of coloured boxes on a scripted path, so a story's comings and goings can be seen while the real vehicle and
/// ship system is being redone. Delete this folder and the lines marked "STAND-IN" to take them out.
/// </summary>
public enum StandInKind : byte
{
    Shuttle, // a person on a long road (5 minutes or more): comes down out of the sky, flies off and jumps
    Hauler, // a person on a short road: a hover truck that runs along the ground
    Courier, // a faction's courier for a sealed Requisition Crate: a small flyer that sets down beside it
    Count,
}

/// <summary>STAND-IN: one craft's script, the same on the host and every client (all times are server ticks).</summary>
public struct StandInCraft
{
    public const byte FlagJump = 1; // leaves with a jump flash (another world)

    public ushort Id;
    public StandInKind Kind;
    public byte Flags;
    public uint Start; // the tick the approach begins
    public ushort In, Wait, Out; // ticks: coming in, standing, going away
    public Vector3 Land, Way; // where it stands (the ground), and the way it travels (along the ground, unit)

    /// <summary>Client: the ground along a hauler's run, worked out the first time it's drawn.</summary>
    public Vector3[]? Track;

    public readonly uint LandsAt => Start + In;
    public readonly uint LeavesAt => Start + In + (uint)Wait;
    public readonly uint EndsAt => LeavesAt + Out;
    public readonly bool Landed(uint tick) => tick >= LandsAt;

    /// <summary>How far a craft comes from, and how high (m).</summary>
    public static (float Far, float High) Reach(StandInKind k) => k switch
    {
        StandInKind.Shuttle => (320, 160),
        StandInKind.Courier => (220, 90),
        _ => (180, 0),
    };

    /// <summary>Seconds to come in and to go away.</summary>
    public static (float In, float Out) Times(StandInKind k) => k switch
    {
        StandInKind.Shuttle => (14, 12),
        StandInKind.Courier => (10, 9),
        _ => (16, 14),
    };
}

/// <summary>STAND-IN: one box of a craft's shape, in the craft's frame (X right, Y up, Z forward; the ground at Y 0).</summary>
public readonly record struct StandInBox(Vector3 Centre, Vector3 Half, Vector3 Colour, bool Glow = false, bool Spin = false);

/// <summary>
/// STAND-IN: the craft's shapes. Hazard yellow and black so nobody takes them for the real thing. Boxes that touch differ
/// by a few centimetres, so no two faces share a plane (CLAUDE.md "No coplanar faces").
/// </summary>
public static class StandInShapes
{
    private static readonly Vector3 Yellow = new(0.95f, 0.78f, 0.12f), Black = new(0.08f, 0.08f, 0.09f), Grey = new(0.42f, 0.44f, 0.47f),
        Glass = new(0.25f, 0.45f, 0.6f), Flame = new(1f, 0.55f, 0.15f), Blue = new(0.35f, 0.7f, 1f), White = new(0.9f, 0.9f, 0.88f);

    public static readonly StandInBox[] Shuttle =
    [
        new(new(0, 2.2f, 0), new(1.6f, 1.2f, 5f), Yellow), // the hull
        new(new(0, 2.2f, 0.4f), new(1.64f, 0.22f, 3.6f), Black), // a black band round its middle
        new(new(0, 2.9f, 4.1f), new(1.2f, 0.55f, 1.0f), Glass), // the cockpit glass
        new(new(0, 3.5f, -0.5f), new(0.9f, 0.08f, 3.2f), Grey), // a spine on top
        new(new(0, 1.9f, -1.2f), new(5.2f, 0.16f, 1.6f), Grey), // the wings
        new(new(5.0f, 2.0f, -1.2f), new(0.24f, 0.5f, 1.3f), Black), // wing tips
        new(new(-5.0f, 2.0f, -1.2f), new(0.24f, 0.5f, 1.3f), Black),
        new(new(0.9f, 2.1f, -5.15f), new(0.5f, 0.5f, 0.4f), Grey), // engines
        new(new(-0.9f, 2.1f, -5.15f), new(0.5f, 0.5f, 0.4f), Grey),
        new(new(0.9f, 2.1f, -5.62f), new(0.36f, 0.36f, 0.1f), Flame, Glow: true), // their glow
        new(new(-0.9f, 2.1f, -5.62f), new(0.36f, 0.36f, 0.1f), Flame, Glow: true),
        new(new(1.1f, 0.52f, 2.6f), new(0.14f, 0.52f, 0.14f), Black), // landing legs
        new(new(-1.1f, 0.52f, 2.6f), new(0.14f, 0.52f, 0.14f), Black),
        new(new(1.1f, 0.52f, -2.6f), new(0.14f, 0.52f, 0.14f), Black),
        new(new(-1.1f, 0.52f, -2.6f), new(0.14f, 0.52f, 0.14f), Black),
        new(new(1.66f, 2.0f, 1.2f), new(0.04f, 0.9f, 0.7f), Grey), // the side door
        new(new(0, 3.66f, -0.5f), new(0.12f, 0.12f, 0.12f), Blue, Glow: true), // a beacon
    ];

    public static readonly StandInBox[] Courier =
    [
        new(new(0, 1.3f, 0), new(1.0f, 0.65f, 2.2f), White), // the body
        new(new(0, 1.3f, 0.2f), new(1.04f, 0.16f, 1.5f), Yellow), // the faction-courier stripe
        new(new(0, 1.75f, 1.75f), new(0.7f, 0.32f, 0.5f), Glass),
        new(new(1.75f, 1.45f, 0), new(0.7f, 0.08f, 0.7f), Grey), // rotor arms
        new(new(-1.75f, 1.45f, 0), new(0.7f, 0.08f, 0.7f), Grey),
        new(new(1.75f, 1.57f, 0), new(0.62f, 0.03f, 0.08f), Black, Spin: true), // rotors
        new(new(-1.75f, 1.57f, 0), new(0.62f, 0.03f, 0.08f), Black, Spin: true),
        new(new(0.6f, 0.33f, 0), new(0.08f, 0.33f, 1.4f), Black), // skids
        new(new(-0.6f, 0.33f, 0), new(0.08f, 0.33f, 1.4f), Black),
        new(new(0, 0.62f, -0.4f), new(0.6f, 0.06f, 0.6f), Grey), // the crate clamp under it
        new(new(0, 2.02f, -1.0f), new(0.1f, 0.1f, 0.1f), Flame, Glow: true), // a beacon
    ];

    public static readonly StandInBox[] Hauler =
    [
        new(new(0, 1.9f, 2.6f), new(1.25f, 1.1f, 1.1f), Yellow), // the cab
        new(new(0, 2.4f, 3.62f), new(1.05f, 0.45f, 0.1f), Glass), // its windscreen
        new(new(0, 1.15f, -1.0f), new(1.3f, 0.3f, 2.7f), Black), // the bed
        new(new(0, 1.85f, -1.2f), new(1.2f, 0.42f, 2.2f), Grey), // a load under a sheet
        new(new(0, 2.32f, -1.2f), new(1.16f, 0.06f, 2.16f), Yellow), // the sheet's stripe
        new(new(1.05f, 0.55f, 2.4f), new(0.3f, 0.32f, 0.75f), Black), // hover pads
        new(new(-1.05f, 0.55f, 2.4f), new(0.3f, 0.32f, 0.75f), Black),
        new(new(1.05f, 0.55f, -2.1f), new(0.3f, 0.32f, 0.75f), Black),
        new(new(-1.05f, 0.55f, -2.1f), new(0.3f, 0.32f, 0.75f), Black),
        new(new(1.05f, 0.19f, 2.4f), new(0.22f, 0.03f, 0.6f), Blue, Glow: true), // their glow
        new(new(-1.05f, 0.19f, 2.4f), new(0.22f, 0.03f, 0.6f), Blue, Glow: true),
        new(new(1.05f, 0.19f, -2.1f), new(0.22f, 0.03f, 0.6f), Blue, Glow: true),
        new(new(-1.05f, 0.19f, -2.1f), new(0.22f, 0.03f, 0.6f), Blue, Glow: true),
        new(new(0, 3.05f, 2.4f), new(0.5f, 0.08f, 0.12f), Flame, Glow: true), // the roof light
    ];

    public static StandInBox[] Of(StandInKind k) => k switch
    {
        StandInKind.Shuttle => Shuttle,
        StandInKind.Courier => Courier,
        _ => Hauler,
    };
}

/// <summary>STAND-IN: where a craft is at a tick (the host and clients alike).</summary>
public static class StandInPath
{
    /// <summary>Ground under a point: a planetoid's along its radius, else the terrain's height.</summary>
    public static Vector3 Ground(Vector3 p)
    {
        var map = World.Map;
        if (map.Planetoid is { } pl)
            return pl.GroundUnder(p);
        return new Vector3(p.X, map.GroundHeight(p.X, p.Z), p.Z);
    }

    public static Vector3 Up(Vector3 p) => GravityFrames.UpAt(p);

    /// <summary>A tangent way at <paramref name="at"/> from a rough one (kept along the ground and unit length).</summary>
    public static Vector3 Along(Vector3 way, Vector3 at)
    {
        var up = Up(at);
        var v = way - up * Vector3.Dot(way, up);
        if (v.LengthSquared() < 1e-6f)
            v = MathF.Abs(up.X) < 0.9f ? Vector3.Cross(up, Vector3.UnitX) : Vector3.Cross(up, Vector3.UnitZ);
        return Vector3.Normalize(v);
    }

    public const float TrackStep = 6f; // m between a hauler's ground samples

    /// <summary>
    /// The craft's frame at <paramref name="tick"/> (rows: right, up, forward, then where), its engines' strength (0 to 1)
    /// and, while it jumps away, the flash's size. False: not to be seen now (not come yet, or gone).
    /// </summary>
    public static bool Pose(ref StandInCraft c, double tick, out Matrix4x4 frame, out float thrust, out float flash)
    {
        frame = default;
        thrust = 0;
        flash = 0;
        if (tick < c.Start || tick >= c.EndsAt)
            return false;
        var (far, high) = StandInCraft.Reach(c.Kind);
        var up = Up(c.Land);
        var fwd = c.Way;
        Vector3 at;
        if (c.Kind == StandInKind.Hauler)
        {
            float s; // m along the run, 0 at the stop
            if (tick < c.LandsAt)
            {
                var u = (float)((tick - c.Start) / Math.Max(1, (int)c.In));
                s = -far * (1 - u) * (1 - u); // (slowing to the stop)
                thrust = 1;
            }
            else if (tick < c.LeavesAt)
            {
                s = 0;
                thrust = 0.35f;
            }
            else
            {
                var u = (float)((tick - c.LeavesAt) / Math.Max(1, (int)c.Out));
                s = far * u * u; // (pulling away)
                thrust = 1;
            }
            var track = c.Track ??= MakeTrack(c.Land, c.Way, far);
            var a = TrackAt(track, far, s);
            var b = TrackAt(track, far, s + 3f);
            var back = TrackAt(track, far, s - 3f);
            at = a + Up(a) * 0.15f; // (hovering)
            up = Up(a);
            var d = b - back;
            if (d.LengthSquared() > 1e-4f)
                fwd = Vector3.Normalize(d);
        }
        else
        {
            if (tick < c.LandsAt)
            {
                var u = (float)((tick - c.Start) / Math.Max(1, (int)c.In));
                var e = 1 - u;
                at = c.Land - c.Way * (far * e * e) + up * (high * e * e * MathF.Sqrt(e)); // (in low and slow at the end)
                thrust = 1;
            }
            else if (tick < c.LeavesAt)
            {
                at = c.Land;
                thrust = 0.2f;
            }
            else
            {
                var u = (float)((tick - c.LeavesAt) / Math.Max(1, (int)c.Out));
                var jump = (c.Flags & StandInCraft.FlagJump) != 0;
                at = c.Land + up * ((jump ? 700 : 300) * MathF.Pow(u, 1.6f)) + c.Way * (far * u * u * u); // (up first, then away)
                thrust = 1;
                if (jump && u > 0.8f)
                    flash = (u - 0.8f) / 0.2f;
            }
        }
        var right = Vector3.Normalize(Vector3.Cross(up, fwd));
        fwd = Vector3.Cross(right, up);
        frame = new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, fwd.X, fwd.Y, fwd.Z, 0, at.X, at.Y, at.Z, 1);
        return true;
    }

    private static Vector3[] MakeTrack(Vector3 land, Vector3 way, float far)
    {
        var n = (int)(2 * far / TrackStep) + 1;
        var track = new Vector3[n];
        var up = Up(land);
        for (var i = 0; i < n; i++)
        {
            var p = land + way * (-far + i * TrackStep) + up * 60f;
            track[i] = Ground(p);
        }
        return track;
    }

    private static Vector3 TrackAt(Vector3[] track, float far, float s)
    {
        var f = Math.Clamp((s + far) / TrackStep, 0, track.Length - 1.001f);
        var i = (int)f;
        return Vector3.Lerp(track[i], track[Math.Min(i + 1, track.Length - 1)], f - i);
    }
}
