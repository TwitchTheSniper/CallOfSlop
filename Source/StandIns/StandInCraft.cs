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
    Freighter, // the traffic's big ship (GameTraffic.cs): a faction's trader that lands, or one passing high overhead
    Pad, // the traffic's placeholder station: a pad, a mast and some crates that stand where a real place will be
    Count,
}

/// <summary>STAND-IN: one craft's script, the same on the host and every client (all times are server ticks).</summary>
public struct StandInCraft
{
    public const byte FlagJump = 1; // from another world: warps in with a flash, a shock ring and a boom, and leaves with a jump flash
    public const byte FlagRoute = 2; // the traffic's leg (GameTraffic.cs): From to Land in In ticks, then stands Wait
    public const byte FlagOver = 4; // with FlagRoute: straight through the sky from From to Land, never landing

    public ushort Id;
    public StandInKind Kind;
    public byte Flags;
    public uint Start; // the tick the approach begins
    public ushort In, Wait, Out; // ticks: coming in, standing, going away
    public Vector3 Land, Way; // where it stands (the ground), and the way it travels (along the ground, unit)
    public Vector3 From; // FlagRoute: where the leg starts
    public Vector3 Tint; // its owner's colour on the stripe boxes (zero: plain hazard yellow)

    /// <summary>Client: the ground along a hauler's run, worked out the first time it's drawn.</summary>
    public Vector3[]? Track;

    /// <summary>This PC only: which of its booms have been heard (1 the warp in, 2 the jump away).</summary>
    public byte Boomed;

    public readonly bool Jumps => (Flags & FlagJump) != 0;

    public readonly uint LandsAt => Start + In;
    public readonly uint LeavesAt => Start + In + (uint)Wait;
    public readonly uint EndsAt => LeavesAt + Out;
    public readonly bool Landed(uint tick) => tick >= LandsAt;
    public readonly bool Route => (Flags & FlagRoute) != 0;
    public readonly bool Over => (Flags & (FlagRoute | FlagOver)) == (FlagRoute | FlagOver);

    /// <summary>How far a craft comes from, and how high (m).</summary>
    public static (float Far, float High) Reach(StandInKind k) => k switch
    {
        StandInKind.Shuttle => (320, 160),
        StandInKind.Courier => (220, 90),
        StandInKind.Freighter => (520, 240),
        _ => (180, 0),
    };

    /// <summary>Seconds to come in and to go away.</summary>
    public static (float In, float Out) Times(StandInKind k) => k switch
    {
        StandInKind.Shuttle => (14, 12),
        StandInKind.Courier => (10, 9),
        StandInKind.Freighter => (24, 18),
        _ => (16, 14),
    };
}

/// <summary>STAND-IN: one box of a craft's shape, in the craft's frame (X right, Y up, Z forward; the ground at Y 0).</summary>
public readonly record struct StandInBox(Vector3 Centre, Vector3 Half, Vector3 Colour, bool Glow = false, bool Spin = false, bool Livery = false);

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
        new(new(0, 2.2f, 0.4f), new(1.64f, 0.22f, 3.6f), Black, Livery: true), // a black band round its middle (its owner's colour)
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
        new(new(0, 1.3f, 0.2f), new(1.04f, 0.16f, 1.5f), Yellow, Livery: true), // the faction-courier stripe
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
        new(new(0, 2.32f, -1.2f), new(1.16f, 0.06f, 2.16f), Yellow, Livery: true), // the sheet's stripe
        new(new(0, 1.6f, 2.5f), new(1.28f, 0.2f, 0.7f), Black, Livery: true), // a band round the cab
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

    /// <summary>A trader or bulk carrier, 44 m long: a spine of containers in its owner's colour between a bridge and four engines.</summary>
    public static readonly StandInBox[] Freighter =
    [
        new(new(0, 4.2f, 0), new(3.2f, 1.1f, 20f), Grey), // the keel
        new(new(0, 6.5f, 17f), new(3.6f, 2.2f, 4.2f), Yellow), // the bridge block
        new(new(0, 7.6f, 20.6f), new(2.8f, 0.7f, 0.7f), Glass),
        new(new(0, 6.3f, 17.2f), new(3.64f, 0.3f, 3.4f), Black, Livery: true), // its band
        new(new(0, 6.6f, 7.5f), new(2.9f, 1.3f, 4.3f), Yellow, Livery: true), // containers
        new(new(0, 6.6f, -2f), new(2.9f, 1.3f, 4.3f), Grey),
        new(new(0, 6.6f, -11.5f), new(2.9f, 1.3f, 4.3f), Yellow, Livery: true),
        new(new(0, 4.4f, -17f), new(8.5f, 0.35f, 2.6f), Black), // the engine pylons
        new(new(8.2f, 4.4f, -18f), new(1.3f, 1.3f, 3.2f), Yellow),
        new(new(-8.2f, 4.4f, -18f), new(1.3f, 1.3f, 3.2f), Yellow),
        new(new(3.3f, 4.5f, -20.6f), new(1.1f, 1.0f, 1.2f), Grey),
        new(new(-3.3f, 4.5f, -20.6f), new(1.1f, 1.0f, 1.2f), Grey),
        new(new(8.2f, 4.4f, -21.4f), new(0.95f, 0.95f, 0.25f), Flame, Glow: true),
        new(new(-8.2f, 4.4f, -21.4f), new(0.95f, 0.95f, 0.25f), Flame, Glow: true),
        new(new(3.3f, 4.5f, -21.95f), new(0.8f, 0.7f, 0.2f), Flame, Glow: true),
        new(new(-3.3f, 4.5f, -21.95f), new(0.8f, 0.7f, 0.2f), Flame, Glow: true),
        new(new(2.6f, 1.55f, 13f), new(0.3f, 1.55f, 0.3f), Black), // landing legs
        new(new(-2.6f, 1.55f, 13f), new(0.3f, 1.55f, 0.3f), Black),
        new(new(2.6f, 1.55f, -13f), new(0.3f, 1.55f, 0.3f), Black),
        new(new(-2.6f, 1.55f, -13f), new(0.3f, 1.55f, 0.3f), Black),
        new(new(0, 8.9f, 17f), new(0.2f, 0.2f, 0.2f), Blue, Glow: true), // beacons
        new(new(0, 3.0f, -19f), new(0.2f, 0.1f, 0.2f), Flame, Glow: true),
    ];

    /// <summary>A placeholder station: a plinth with hazard corners, a mast with a lamp, a hut and a few crates.</summary>
    public static readonly StandInBox[] Pad =
    [
        new(new(0, 0.2f, 0), new(7f, 0.5f, 7f), Grey), // the plinth (sunk a little, for sloping ground)
        new(new(6.4f, 0.4f, 6.4f), new(0.7f, 0.36f, 0.7f), Yellow), // hazard corners
        new(new(-6.4f, 0.4f, 6.4f), new(0.7f, 0.36f, 0.7f), Black),
        new(new(6.4f, 0.4f, -6.4f), new(0.7f, 0.36f, 0.7f), Black),
        new(new(-6.4f, 0.4f, -6.4f), new(0.7f, 0.36f, 0.7f), Yellow),
        new(new(-9.5f, 5.5f, 0), new(0.18f, 5.5f, 0.18f), Black), // the mast, beside the pad
        new(new(-9.5f, 9.2f, 0.75f), new(0.06f, 0.9f, 0.6f), Yellow, Livery: true), // its flag
        new(new(-9.5f, 11.2f, 0), new(0.3f, 0.3f, 0.3f), Flame, Glow: true), // its lamp
        new(new(-9.5f, 1.4f, -4.5f), new(1.6f, 1.4f, 2.1f), Yellow), // the hut
        new(new(-9.5f, 1.9f, -4.5f), new(1.64f, 0.25f, 2.14f), Black, Livery: true),
        new(new(-9.5f, 2.9f, -4.5f), new(1.8f, 0.1f, 2.3f), Black), // its roof
        new(new(-9.2f, 0.6f, 4.6f), new(0.6f, 0.6f, 0.6f), Grey), // crates
        new(new(-10.5f, 0.55f, 5.4f), new(0.55f, 0.55f, 0.55f), Yellow),
        new(new(-9.3f, 1.65f, 4.7f), new(0.45f, 0.45f, 0.45f), Yellow, Livery: true),
    ];

    public static StandInBox[] Of(StandInKind k) => k switch
    {
        StandInKind.Shuttle => Shuttle,
        StandInKind.Courier => Courier,
        StandInKind.Freighter => Freighter,
        StandInKind.Pad => Pad,
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
        if (c.Kind == StandInKind.Pad)
        {
            if (tick >= c.EndsAt)
                return false; // (the host renews a pad's time before it runs out)
            frame = Frame(Up(c.Land), c.Way, c.Land);
            thrust = 0.5f + 0.5f * MathF.Sin((float)(tick * 0.05)); // (the lamp)
            return true;
        }
        if (tick < c.Start || tick >= c.EndsAt)
            return false;
        if (c.Route)
        {
            if (c.Over && tick >= c.LandsAt)
                return false; // (jumped away; its script stays a few seconds for the boom to be heard)
            RoutePose(ref c, tick, out frame, out thrust);
            return true;
        }
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
        frame = Frame(up, fwd, at);
        return true;
    }

    private static Matrix4x4 Frame(Vector3 up, Vector3 fwd, Vector3 at)
    {
        var right = Vector3.Cross(up, fwd);
        right = right.LengthSquared() > 1e-8f ? Vector3.Normalize(right) : Along(Vector3.UnitX, at);
        fwd = Vector3.Cross(right, up);
        return new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, fwd.X, fwd.Y, fwd.Z, 0, at.X, at.Y, at.Z, 1);
    }

    // ---- The traffic's legs (GameTraffic.cs): From to Land, then standing -------------------------------------------------

    /// <summary>How high a flyer's hop between two stations goes (m): higher for a longer hop, up to the kind's own height.</summary>
    public static float HopHeight(StandInKind k, float dist) => MathF.Min(StandInCraft.Reach(k).High * 0.7f, 12f + dist * 0.18f);

    private static void RoutePose(ref StandInCraft c, double tick, out Matrix4x4 frame, out float thrust)
    {
        var u = Math.Clamp((float)((tick - c.Start) / Math.Max(1, (int)c.In)), 0f, 1f);
        thrust = u < 1 ? 1 : 0.3f;
        Vector3 at, fwd, up;
        if (c.Over)
        {
            at = Vector3.Lerp(c.From, c.Land, u);
            up = Up(at);
            fwd = c.Land - c.From;
        }
        else if (c.Kind == StandInKind.Hauler)
        {
            var track = c.Track ??= RouteTrack(c.From, c.Land);
            var len = (track.Length - 1) * RouteStep(c.From, c.Land, track.Length);
            var s = len * (u * u * (3 - 2 * u)); // (pulling away, then slowing to the stop)
            var a = RouteAt(track, len, s);
            at = a + Up(a) * 0.15f;
            up = Up(a);
            fwd = RouteAt(track, len, s + 3f) - RouteAt(track, len, s - 3f);
        }
        else
        {
            at = HopAt(ref c, u, out up);
            fwd = u < 0.98f ? HopAt(ref c, u + 0.02f, out _) - at : c.Land - c.From;
            fwd -= up * Vector3.Dot(fwd, up) * 0.6f; // (the nose mostly level)
        }
        if (fwd.LengthSquared() < 1e-6f)
            fwd = c.Way;
        frame = Frame(up, Vector3.Normalize(fwd), at);
    }

    private static Vector3 HopAt(ref StandInCraft c, float u, out Vector3 up)
    {
        var e = u * u * (3 - 2 * u);
        var flat = Vector3.Lerp(c.From, c.Land, e);
        up = Up(flat);
        var arc = 4 * e * (1 - e);
        return flat + up * (HopHeight(c.Kind, Vector3.Distance(c.From, c.Land)) * MathF.Sqrt(arc));
    }

    // The ground along a leg is the same every time a round comes by: kept, so a lap costs its ground once.
    private static readonly Dictionary<(Vector3, Vector3), Vector3[]> _routes = new();
    public const int RouteMaxSamples = 200, RouteMaxKept = 48;

    /// <summary>Forgets the kept legs (a new world).</summary>
    public static void ForgetRoutes() => _routes.Clear();

    private static float RouteStep(Vector3 from, Vector3 to, int n) => Vector3.Distance(from, to) / Math.Max(1, n - 1);

    private static Vector3[] RouteTrack(Vector3 from, Vector3 to)
    {
        if (_routes.TryGetValue((from, to), out var kept))
            return kept;
        if (_routes.TryGetValue((to, from), out var back))
        {
            kept = (Vector3[])back.Clone();
            Array.Reverse(kept);
        }
        else
        {
            var n = Math.Clamp((int)(Vector3.Distance(from, to) / TrackStep) + 2, 2, RouteMaxSamples);
            kept = new Vector3[n];
            for (var i = 0; i < n; i++)
            {
                var p = Vector3.Lerp(from, to, i / (float)(n - 1));
                kept[i] = Ground(p + Up(p) * 60f);
            }
            kept[0] = from;
            kept[n - 1] = to;
        }
        if (_routes.Count >= RouteMaxKept)
            _routes.Clear();
        _routes[(from, to)] = kept;
        return kept;
    }

    private static Vector3 RouteAt(Vector3[] track, float len, float s)
    {
        var f = Math.Clamp(s / MathF.Max(len, 0.01f) * (track.Length - 1), 0, track.Length - 1.001f);
        var i = (int)f;
        return Vector3.Lerp(track[i], track[i + 1], f - i);
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
