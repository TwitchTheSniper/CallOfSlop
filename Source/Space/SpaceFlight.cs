using System.Buffers.Binary;

namespace CallOfSlop.Space;

/// <summary>
/// The warp rule for friends (docs/space/SPACE.md, "Time warp"). <b>Provisional:</b> this is the architecture's proposal
/// (section 4) and Karl hasn't confirmed it yet (section 12); it's one function so it can change in one place.
/// Only the host sets the warp; above 1x only while nobody is busy (a ship in space burning: Game.SpaceAnyoneBusy); never past what the ships can
/// follow accurately (<see cref="SpaceShips.MaxWarp"/>). Warp 0 (paused) is always allowed to the host.
/// </summary>
public static class SpaceWarpRule
{
    public static int Effective(int requested, bool anyoneBusy, int shipLimit)
    {
        var w = Math.Clamp(requested, 0, SpaceClock.MaxWarp);
        if (w <= 1)
            return w;
        if (anyoneBusy)
            return 1;
        return Math.Max(1, Math.Min(w, shipLimit));
    }
}

/// <summary>
/// Space on the host (docs/space/SPACE.md): a star system on rails, the space clock and the ships, stepped once per game
/// tick. Phase 2 runs it headless (the self-test, <c>--space-ships</c>); phase 3 ties a bubble to it.
/// </summary>
public sealed class SpaceFlight
{
    public readonly StarSystem System;
    public readonly SpaceClock Clock;
    public readonly SpaceShips Ships;

    /// <summary>The warp the host asked for (clients' requests are ignored: <see cref="RequestWarp"/>).</summary>
    public int RequestedWarp { get; private set; } = 1;

    /// <summary>Someone is doing something that needs real-time physics (set by the game each tick; phase 3).</summary>
    public bool AnyoneBusy;

    public SpaceFlight(StarSystem system, int shipCapacity, SpaceTime start = default)
    {
        System = system;
        Clock = new SpaceClock(start);
        Ships = new SpaceShips(system, shipCapacity, start);
        Wells = new ConstructGravity.SpaceWells(Ships);
        Ships.Forces.Add(Wells);
    }

    /// <summary>Gravity wells on ships in space (phase 5): they pull the other ships (ConstructGravity.cs).</summary>
    public readonly ConstructGravity.SpaceWells Wells;

    /// <summary>A warp request; only the host's counts. Returns whether it was taken.</summary>
    public bool RequestWarp(int warp, bool fromHost)
    {
        if (!fromHost)
            return false;
        RequestedWarp = Math.Clamp(warp, 0, SpaceClock.MaxWarp);
        return true;
    }

    /// <summary>
    /// One game tick: the warp the rule allows, the clock, then every ship. <paramref name="shipLimit"/> is the other
    /// systems' ships' own limit (phase 6: their flights follow this clock, <see cref="Follow"/>).
    /// </summary>
    public void Tick(int shipLimit = int.MaxValue)
    {
        Clock.Warp = SpaceWarpRule.Effective(RequestedWarp, AnyoneBusy, Math.Min(Ships.MaxWarp, shipLimit));
        Clock.Tick();
        Wells.Refresh(); // (where the wells' ships are as this step starts)
        Ships.Advance(Clock.Now);
    }

    /// <summary>
    /// Another system's flight in step with the bubble's clock (phase 6, docs/space/GALAXY.md: one galaxy time, every
    /// system's epoch the same): takes its time and warp, then steps this system's ships to it.
    /// </summary>
    public void Follow(SpaceClock master)
    {
        Clock.Warp = master.Warp;
        Clock.Set(master.Now);
        Wells.Refresh();
        Ships.Advance(master.Now);
    }
}

/// <summary>One ship's state as the host sends it: the time, its primary and its offset and velocity in the primary's frame.</summary>
public readonly record struct ShipState(SpaceTime Time, int Primary, Vec3d R, Vec3d V)
{
    /// <summary>Bytes on the wire: time (8), primary (2), offset and velocity (6 doubles).</summary>
    public const int Size = 8 + 2 + 48;

    public void Write(Span<byte> b)
    {
        BinaryPrimitives.WriteInt64LittleEndian(b, Time.Ns);
        BinaryPrimitives.WriteUInt16LittleEndian(b[8..], (ushort)Primary);
        var o = 10;
        foreach (var d in (ReadOnlySpan<double>)[R.X, R.Y, R.Z, V.X, V.Y, V.Z])
        {
            BinaryPrimitives.WriteDoubleLittleEndian(b[o..], d);
            o += 8;
        }
    }

    public static ShipState Read(ReadOnlySpan<byte> b)
    {
        Span<double> d = stackalloc double[6];
        for (var k = 0; k < 6; k++)
            d[k] = BinaryPrimitives.ReadDoubleLittleEndian(b[(10 + 8 * k)..]);
        return new ShipState(new SpaceTime(BinaryPrimitives.ReadInt64LittleEndian(b)), BinaryPrimitives.ReadUInt16LittleEndian(b[8..]),
            new Vec3d(d[0], d[1], d[2]), new Vec3d(d[3], d[4], d[5]));
    }

    public static ShipState Of(SpaceShips ships, int i)
    {
        ships.RelativeOf(i, out var p, out var r, out var v);
        return new ShipState(ships.Time, p, r, v);
    }
}

/// <summary>
/// A client's view of one host ship (docs/space/SPACE.md, "Multiplayer"): the last few states the host sent. A sample at
/// any time takes the two states either side, carries each one there along its two-body orbit round its primary (exact
/// for a coasting ship, whatever the warp), and blends them by time, so the path is smooth and stays on the orbit even when
/// a packet covers minutes of warped time. Past the newest state it carries that one on. No allocation.
/// </summary>
public sealed class ShipTrack
{
    private readonly ShipState[] _ring = new ShipState[4];
    private int _count, _head;

    public int Count => _count;

    public void Clear() => _count = 0;

    /// <summary>A state from the host (older or repeated times are dropped).</summary>
    public void Push(in ShipState s)
    {
        if (_count > 0 && s.Time.Ns <= Newest.Time.Ns)
            return;
        _head = (_head + 1) % _ring.Length;
        _ring[_head] = s;
        if (_count < _ring.Length)
            _count++;
    }

    public ShipState Newest => _ring[_head];

    private ShipState At(int ageIndex) => _ring[(_head - ageIndex + _ring.Length) % _ring.Length];

    /// <summary>The ship's ecliptic position and velocity at a time. False with no state yet.</summary>
    public bool Sample(StarSystem sys, SpaceTime t, out SpacePos pos, out Vec3d vel)
    {
        pos = default;
        vel = default;
        if (_count == 0)
            return false;
        // The newest state at or before t, and the one after it.
        var older = _count - 1;
        for (var k = 0; k < _count; k++)
        {
            if (At(k).Time.Ns <= t.Ns)
            {
                older = k;
                break;
            }
        }
        var a = At(older);
        Carry(sys, a, t, out pos, out vel);
        if (older == 0 || a.Time.Ns > t.Ns)
            return true;
        var b = At(older - 1);
        Carry(sys, b, t, out var pb, out var vb);
        var u = (t.Ns - a.Time.Ns) / (double)(b.Time.Ns - a.Time.Ns);
        pos += (pb - pos) * u;
        vel += (vb - vel) * u;
        return true;
    }

    private static void Carry(StarSystem sys, in ShipState s, SpaceTime t, out SpacePos pos, out Vec3d vel)
    {
        var mu = sys.Bodies[s.Primary].Mu;
        if (!UniversalKepler.PropagateNs(mu, s.R, s.V, t.Ns - s.Time.Ns, out var r, out var v))
        {
            r = s.R;
            v = s.V;
        }
        sys.StateOf(s.Primary, t, out var bp, out var bv);
        pos = bp + r;
        vel = bv + v;
    }
}
