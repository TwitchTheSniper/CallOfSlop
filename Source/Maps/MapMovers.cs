using System.Numerics;

namespace CallOfSlop;

/// <summary>How a mover is written in a map file (the motion itself is always a path plus a spin, see <see cref="MoverDef"/>).</summary>
public enum MoverKind : byte
{
    Rotate,
    Slide,
    Elevator,
}

/// <summary>
/// Where a mover is at one moment: its origin and its yaw (radians, the same convention as a player's yaw, so a
/// point straight ahead of the origin at yaw 0 turns to <see cref="World.FlatForward"/>(yaw)). Movers only turn
/// about the vertical axis.
/// </summary>
public readonly record struct MoverPose(Vector3 Origin, float Yaw)
{
    /// <summary>A point in the mover's own frame -> world.</summary>
    public Vector3 ToWorld(Vector3 local)
    {
        var (s, c) = MathF.SinCos(Yaw);
        return Origin + new Vector3(local.X * c - local.Z * s, local.Y, local.X * s + local.Z * c);
    }

    /// <summary>A world point -> the mover's own frame.</summary>
    public Vector3 ToLocal(Vector3 world)
    {
        var (s, c) = MathF.SinCos(Yaw);
        var d = world - Origin;
        return new Vector3(d.X * c + d.Z * s, d.Y, -d.X * s + d.Z * c);
    }
}

/// <summary>
/// The source of a mover's pose. Map movers compute it from the server tick (<see cref="MoverDef"/>); a later
/// piloted vehicle can implement it from networked state and reuse the same carrying, pushing and ray code.
/// </summary>
public interface IMoverPose
{
    /// <summary>The pose at a (possibly fractional) server tick. Must be a pure function of the tick for prediction.</summary>
    MoverPose PoseAt(double tick);
}

/// <summary>One solid box of a mover, in the mover's frame: centre, half size and its own yaw (radians) in that frame.</summary>
public readonly record struct MoverPart(Vector3 Center, Vector3 Half, float Yaw, Vector3 Color);

/// <summary>
/// A moving solid from a map file's "movers" section: rotating platforms, sliding platforms and elevators
/// (see Maps/README.md, "Movers"). Its pose is a pure function of the server tick, so the host and every client
/// work out the same pose from the synced clock with no per-mover network traffic.
///
/// Motion = the origin travelling along <see cref="Points"/> (eased into and out of every point, pausing
/// <see cref="Dwell"/> seconds there; ping-pong or looping) plus a constant spin about the vertical axis.
/// </summary>
public sealed class MoverDef : IMoverPose
{
    public MoverKind Kind;
    public string Name = "";
    /// <summary>Path of the origin (world). One point = it stays put (a rotating platform's pivot).</summary>
    public Vector3[] Points = [Vector3.Zero];
    /// <summary>Travel speed along the path in m/s (average; eased legs peak at 1.5x this).</summary>
    public float Speed = 2f;
    /// <summary>Spin in radians per second (positive turns the same way as a player turning right).</summary>
    public float Spin;
    /// <summary>Yaw at tick 0 (radians).</summary>
    public float Yaw0;
    /// <summary>Seconds paused at each path point.</summary>
    public float Dwell;
    /// <summary>true: after the last point go straight back to the first; false: ping-pong.</summary>
    public bool Loop;
    /// <summary>Seconds into the cycle at tick 0 (offsets movers that share a path).</summary>
    public float Phase;
    /// <summary>Elevators: how far the platform rises (only used to write the file back out).</summary>
    public float Rise;
    public MoverPart[] Parts = [];

    /// <summary>
    /// false ("running": false in the map file): it stands still at the start of its loop until map logic starts it
    /// (Maps/README.md, "Map logic"). Every mover can be started, stopped and advanced by map logic.
    /// </summary>
    public bool StartsRunning = true;

    // The control state (map logic, GameLogic.cs): the mover's own clock is RunBase seconds at tick RunSince, running
    // forward from then, and stops when it reaches RunStop (+infinity: never). The default (0, 0, +infinity) is the clock
    // every mover always had, the server tick, so a mover nothing controls moves exactly as before. Set by the server's
    // logic and sent to clients (Msg.Logic), so the pose stays a pure function of the tick for prediction.
    [NotBaked] public double RunBase;
    [NotBaked] public double RunSince;
    [NotBaked] public double RunStop = double.PositiveInfinity;

    /// <summary>The mover's own clock (seconds) at a server tick.</summary>
    public double TimeAt(double tick) => RunSince == 0 && RunBase == 0 && double.IsPositiveInfinity(RunStop)
        ? tick / World.TickRate // (nothing controls it: exactly the clock it always had)
        : Math.Min(RunBase + Math.Max(0.0, tick - RunSince) / World.TickRate, RunStop);

    /// <summary>Is it moving along its loop at <paramref name="tick"/> (not stopped by map logic)?</summary>
    public bool RunningAt(double tick) => TimeAt(tick) < RunStop;

    /// <summary>Back to how the map starts it: running on the server clock, or stopped at the start of its loop.</summary>
    public void ResetControl()
    {
        RunBase = 0;
        RunSince = 0;
        RunStop = StartsRunning ? double.PositiveInfinity : 0;
    }

    /// <summary>Map logic: starts it at tick <paramref name="at"/> (from where it stands). False if it's already running.</summary>
    public bool ControlStart(double at)
    {
        if (RunningAt(at) && double.IsPositiveInfinity(RunStop))
            return false;
        var t = TimeAt(at);
        RunBase = t;
        RunSince = at;
        RunStop = double.PositiveInfinity;
        return true;
    }

    /// <summary>Map logic: stops it where it is at tick <paramref name="at"/>. False if it's already stopped then.</summary>
    public bool ControlStop(double at)
    {
        if (!RunningAt(at))
            return false;
        var t = TimeAt(at);
        RunBase = t;
        RunSince = at;
        RunStop = t;
        return true;
    }

    /// <summary>Map logic: back to the start of its loop at tick <paramref name="at"/> (running or stopped, as the map starts it).</summary>
    public void ControlReset(double at)
    {
        RunBase = 0;
        RunSince = at;
        RunStop = StartsRunning ? double.PositiveInfinity : 0;
    }

    /// <summary>
    /// Map logic: travels to its next stop (the next point of its path, for an elevator the other end) from tick
    /// <paramref name="at"/>, skipping the wait there, and stops. Returns the tick it arrives, or -1 (a mover that only spins).
    /// </summary>
    public double ControlAdvance(double at)
    {
        if (_legs == null)
            Prepare();
        if (_cycle <= 1e-6)
            return -1;
        var t = TimeAt(at);
        var c = (t + Phase) % _cycle;
        if (c < 0)
            c += _cycle;
        foreach (var leg in _legs!)
        {
            var travelStart = leg.Start + leg.Pause;
            var end = travelStart + leg.Travel;
            if (end <= c + 1e-6)
                continue; // (stopped right at the end of this leg: the next one)
            var from = c < travelStart && Spin == 0 ? t + (travelStart - c) : t; // (a mover that spins keeps its wait: skipping it would jump its turn)
            RunBase = from;
            RunSince = at;
            RunStop = t + (end - c);
            return at + (RunStop - from) * World.TickRate;
        }
        // At the very end of the loop: the first leg of the next one.
        var first = _legs![0];
        var start = t + (_cycle - c) + (Spin == 0 ? first.Start + first.Pause : 0);
        RunBase = start;
        RunSince = at;
        RunStop = t + (_cycle - c) + first.Start + first.Pause + first.Travel;
        return at + (RunStop - start) * World.TickRate;
    }

    private (Vector3 From, Vector3 To, double Start, double Pause, double Travel)[]? _legs;
    private double _cycle;

    /// <summary>Builds the timeline. Call after changing the fields (the map file reader does).</summary>
    public void Prepare()
    {
        var pts = Points;
        var order = new List<int>();
        if (pts.Length >= 2)
        {
            for (var i = 0; i < pts.Length; i++)
                order.Add(i);
            if (Loop)
                order.Add(0);
            else
                for (var i = pts.Length - 2; i >= 0; i--)
                    order.Add(i);
        }
        var legs = new List<(Vector3, Vector3, double, double, double)>();
        var t = 0.0;
        var speed = Math.Max(0.05, (double)Speed);
        for (var k = 0; k + 1 < order.Count; k++)
        {
            var a = pts[order[k]];
            var b = pts[order[k + 1]];
            var travel = Vector3.Distance(a, b) / speed;
            legs.Add((a, b, t, Math.Max(0, Dwell), travel));
            t += Math.Max(0, Dwell) + travel;
        }
        _legs = legs.ToArray();
        _cycle = t;
    }

    /// <summary>Seconds for one full trip (0 for a mover that only spins).</summary>
    public double Cycle
    {
        get
        {
            if (_legs == null)
                Prepare();
            return _cycle;
        }
    }

    /// <inheritdoc />
    public MoverPose PoseAt(double tick)
    {
        if (_legs == null)
            Prepare();
        var time = TimeAt(tick); // (tick / TickRate unless map logic stopped or started it)
        var origin = Points[0];
        if (_cycle > 1e-6)
        {
            var t = (time + Phase) % _cycle;
            if (t < 0)
                t += _cycle;
            foreach (var leg in _legs!)
            {
                if (t > leg.Start + leg.Pause + leg.Travel)
                    continue;
                var u = leg.Travel <= 1e-9 ? 1.0 : Math.Clamp((t - leg.Start - leg.Pause) / leg.Travel, 0.0, 1.0);
                var s = (float)(u * u * (3 - 2 * u)); // smoothstep: eased out of and into each point
                origin = Vector3.Lerp(leg.From, leg.To, s);
                break;
            }
        }
        var yaw = Yaw0;
        if (Spin != 0)
        {
            var turn = (double)Spin * time % (Math.PI * 2); // in double so a long-running server stays precise
            yaw = World.WrapAngle(Yaw0 + (float)turn);
        }
        return new MoverPose(origin, yaw);
    }

    /// <summary>Furthest any part reaches from the origin, horizontally.</summary>
    public float Reach
    {
        get
        {
            var r = 0f;
            foreach (var p in Parts)
                r = MathF.Max(r, new Vector2(p.Center.X, p.Center.Z).Length() + new Vector2(p.Half.X, p.Half.Z).Length());
            return r;
        }
    }

    /// <summary>
    /// Everything this mover can ever occupy, as a world box (horizontal footprint of the whole path and turn;
    /// vertically from its lowest point up). Bots treat it as a no-go area and never plan routes through it.
    /// </summary>
    public Box Sweep
    {
        get
        {
            var r = Reach;
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            var lowest = float.MaxValue;
            var highest = float.MinValue;
            foreach (var p in Parts)
            {
                lowest = MathF.Min(lowest, p.Center.Y - p.Half.Y);
                highest = MathF.Max(highest, p.Center.Y + p.Half.Y);
            }
            foreach (var pt in Points)
            {
                min = Vector3.Min(min, pt);
                max = Vector3.Max(max, pt);
            }
            var lo = new Vector3(min.X - r, min.Y + lowest, min.Z - r);
            var hi = new Vector3(max.X + r, max.Y + highest, max.Z + r);
            return new Box((lo + hi) / 2, (hi - lo) / 2);
        }
    }
}

public sealed partial class Map
{
    /// <summary>Moving solids from the map file's "movers" section (empty on most maps). See <see cref="MoverPhysics"/>.</summary>
    public readonly List<MoverDef> Movers = new();

    /// <summary>Fingerprint of the movers (0 when there are none), mixed into <see cref="ContentHash"/>.</summary>
    public uint MoverHash
    {
        get
        {
            if (Movers.Count == 0)
                return 0;
            var h = 2166136261u;
            void Mix(float f)
            {
                var bits = BitConverter.SingleToUInt32Bits(MathF.Round(f * 1000f) / 1000f);
                for (var i = 0; i < 4; i++)
                {
                    h ^= (bits >> (i * 8)) & 0xFF;
                    h *= 16777619u;
                }
            }
            foreach (var m in Movers)
            {
                Mix((float)m.Kind);
                foreach (var p in m.Points)
                {
                    Mix(p.X); Mix(p.Y); Mix(p.Z);
                }
                Mix(m.Speed); Mix(m.Spin); Mix(m.Yaw0); Mix(m.Dwell); Mix(m.Loop ? 1 : 0); Mix(m.Phase);
                if (!m.StartsRunning)
                    Mix(-1); // (movers that start running keep their old fingerprint)
                foreach (var p in m.Parts)
                {
                    Mix(p.Center.X); Mix(p.Center.Y); Mix(p.Center.Z);
                    Mix(p.Half.X); Mix(p.Half.Y); Mix(p.Half.Z); Mix(p.Yaw);
                }
            }
            return h == 0 ? 1 : h;
        }
    }

    /// <summary>Is <paramref name="p"/> (feet) inside some mover's sweep, widened by <paramref name="margin"/>?</summary>
    public bool InMoverSweep(Vector3 p, float margin = 0.6f)
    {
        foreach (var m in Movers)
        {
            var s = m.Sweep;
            if (p.X > s.Min.X - margin && p.X < s.Max.X + margin && p.Z > s.Min.Z - margin && p.Z < s.Max.Z + margin
                && p.Y < s.Max.Y + 0.5f && p.Y + World.PlayerHeight > s.Min.Y)
                return true;
        }
        return false;
    }
}
