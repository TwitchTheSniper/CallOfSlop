using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A walker's feet on the wire (protocol 229; docs/sandbox/GIANTS.md, "Walker feet on clients"). The server decides where
/// every foot is (ConstructWalk.cs); a walker's pose carries its gait state (walking, a group in the air, the feet put down,
/// the problem, top speed and leg power) and its feet: each foot that moved in the last <see cref="HotWalkTicks"/> ticks
/// (a swinging foot, one carried by a fallen body) plus <see cref="FeetRefresh"/> more in turn, so a planted foot costs
/// nothing after its last few poses and a lost packet is healed by the turn. A foot is its leg part and flags and its
/// point in centimetres from the pose's own position. A client keeps each pose's feet with the body's history and draws
/// them at the body's drawn tick (<see cref="SampleFeet"/>), so the drawn feet, the legs' hit boxes (LegRay) and the pads
/// a player stands on (FeetBlockPlayer) are the server's. Nothing allocates (a problem string is made only when it changes).
/// </summary>
public static partial class ConstructNet
{
    public const byte FootSwinging = 1, FootGrounded = 2, FootHold = 4;
    public const int HotWalkTicks = PoseEvery * 4; // a foot that moved this many ticks ago still goes in every pose (three lost in a row are bridged)
    public const int FeetRefresh = 3; // feet a pose carries in turn besides the ones that moved
    public const int PoseFeet = 24; // feet a pose carries at most (a body with more legs takes turns)
    private const int WalkHeadBytes = 1 + 1 + 1 + 2 + 4 + 1, FootBytes = 2 + 1 + 6;
    private const byte PoseAsleep = 1, PoseWalker = 2; // the pose's asleep byte: bit 1 says a walk block follows (protocol 229)
    private const byte WalkingBit = 1, SteppingBit = 2, FeetSetBit = 4, PowerHeldBit = 8; // (8: protocol 241)

    // The walk problems the server sets (ConstructWalk.cs), by number on the wire; 1 + their count: "N working legs".
    private static readonly string[] Problems =
    [
        "",
        ConstructPhysics.NoLegPower,
        ConstructPhysics.ProblemNotRound,
        ConstructPhysics.ProblemTooHeavy,
        ConstructPhysics.ProblemNoGround,
        ConstructPhysics.ProblemNoFooting,
    ];

    private static int WalkSize(Construct c) => c.LegCount == 0 ? 0 : WalkHeadBytes + Math.Min(c.LegCount, PoseFeet) * FootBytes;

    private static byte FootFlags(in CLeg leg) => (byte)((leg.Swinging ? FootSwinging : 0) | (leg.Grounded ? FootGrounded : 0) | (leg.Hold ? FootHold : 0));

    /// <summary>Server, after the gait brain's tick: which feet moved (their place or flags), stamped on the walk clock.</summary>
    public static void NoteFeet(Construct c)
    {
        for (var k = 0; k < c.LegCount; k++)
        {
            ref var leg = ref c.Legs[k];
            var flags = FootFlags(leg);
            if (leg.Foot == leg.NetFoot && flags == leg.NetFlags)
                continue;
            leg.NetFoot = leg.Foot;
            leg.NetFlags = flags;
            leg.NetMoved = c.WalkTicks;
        }
    }

    private static byte ProblemCode(Construct c, out byte arg)
    {
        arg = 0;
        var p = c.WalkProblem;
        for (var i = 0; i < Problems.Length; i++)
            if (ReferenceEquals(p, Problems[i]) || p == Problems[i])
                return (byte)i;
        arg = (byte)Math.Clamp(c.WalkLive, 0, 255); // ("N working legs with power": the only one with a number in it)
        return (byte)Problems.Length;
    }

    /// <summary>Server: a walker's gait state and feet, into its pose.</summary>
    private static void WriteWalk(PacketWriter w, Construct c)
    {
        var code = ProblemCode(c, out var arg);
        w.U8((byte)((c.Walking ? WalkingBit : 0) | (c.WalkStepping ? SteppingBit : 0) | (c.FeetSet ? FeetSetBit : 0) | (c.WalkPowerHeld ? PowerHeldBit : 0)))
            .U8(code).U8(arg)
            .U16((ushort)Math.Clamp(MathF.Round(c.WalkTop * 100), 0, 65535))
            .F32(c.LegWatts);
        var countAt = w.Length;
        w.U8(0);
        var n = 0;
        var count = c.LegCount;
        var start = (c.PoseFeetAt % count + count) % count;
        var next = (start + FeetRefresh) % count;
        for (var k2 = 0; k2 < count; k2++)
        {
            var k = (start + k2) % count;
            ref var leg = ref c.Legs[k];
            if (k2 >= FeetRefresh && c.WalkTicks - leg.NetMoved > HotWalkTicks)
                continue; // (planted, and its last moves went out: its turn comes)
            if (n == PoseFeet)
            {
                next = k; // (more than a pose holds: the next one starts here)
                break;
            }
            var d = leg.Foot - c.Pos;
            w.U16(leg.Part).U8(FootFlags(leg)).U16(S16(Cm(d.X))).U16(S16(Cm(d.Y))).U16(S16(Cm(d.Z)));
            n++;
        }
        c.PoseFeetAt = next;
        w.PatchU8(countAt, (byte)n);
    }

    /// <summary>Client: a walker's block of a pose (<paramref name="c"/> null or stale: read past). <paramref name="pos"/> is the pose's position.</summary>
    private static void ReadWalk(ref PacketReader r, Construct? c, Vector3 pos)
    {
        var flags = r.U8();
        var code = r.U8();
        var arg = r.U8();
        var top = r.U16() / 100f;
        var watts = r.F32();
        int n = r.U8();
        if (c != null)
        {
            c.Walking = (flags & WalkingBit) != 0;
            c.WalkStepping = (flags & SteppingBit) != 0;
            c.NetFeetSet = (flags & FeetSetBit) != 0;
            c.WalkPowerHeld = (flags & PowerHeldBit) != 0;
            c.WalkTop = top;
            c.LegWatts = float.IsFinite(watts) ? watts : 0;
            if (code != c.NetProblem || arg != c.NetProblemArg || code == 0 && c.WalkProblem.Length > 0)
            {
                c.NetProblem = code;
                c.NetProblemArg = arg;
                c.WalkProblem = code < Problems.Length ? Problems[code] : ConstructPhysics.ProblemLegs(arg); // (made only when it changes)
            }
        }
        for (var i = 0; i < n; i++)
        {
            var part = r.U16();
            var ff = r.U8();
            var d = new Vector3(FromCm(R16(r.U16())), FromCm(R16(r.U16())), FromCm(R16(r.U16())));
            if (c == null)
                continue;
            for (var k = 0; k < c.LegCount; k++)
            {
                ref var leg = ref c.Legs[k];
                if (leg.Part != part)
                    continue;
                leg.NetFoot = pos + d;
                leg.NetFlags = ff;
                if (!leg.NetHeard)
                    FirstHeard(c, k); // (the poses before had no word of it: they hold it where it is now)
                leg.NetHeard = true;
                break;
            }
        }
    }

    /// <summary>Client, as a pose goes into the body's history (Construct.NetHistAdd): its feet, row <paramref name="row"/>.</summary>
    internal static void KeepFeet(Construct c, int row)
    {
        var legs = c.LegCount;
        if (legs == 0)
            return;
        if (c.NetHistFeet.Length != Construct.NetHistMax * legs)
        {
            // (the legs changed: every row starts from the feet as last heard)
            c.NetHistFeet = new Vector3[Construct.NetHistMax * legs];
            c.NetHistFootFlags = new byte[Construct.NetHistMax * legs];
            for (var r = 0; r < Construct.NetHistMax; r++)
                for (var k = 0; k < legs; k++)
                {
                    c.NetHistFeet[r * legs + k] = c.Legs[k].NetFoot;
                    c.NetHistFootFlags[r * legs + k] = c.Legs[k].NetFlags;
                }
        }
        for (var k = 0; k < legs; k++)
        {
            c.NetHistFeet[row * legs + k] = c.Legs[k].NetFoot;
            c.NetHistFootFlags[row * legs + k] = c.Legs[k].NetFlags;
        }
    }

    /// <summary>Client: leg <paramref name="k"/>'s foot heard for the first time: every row of the history holds it there.</summary>
    private static void FirstHeard(Construct c, int k)
    {
        var legs = c.LegCount;
        if (c.NetHistFeet.Length != Construct.NetHistMax * legs)
            return;
        for (var r = 0; r < Construct.NetHistMax; r++)
        {
            c.NetHistFeet[r * legs + k] = c.Legs[k].NetFoot;
            c.NetHistFootFlags[r * legs + k] = c.Legs[k].NetFlags;
        }
    }

    /// <summary>Client: the history's rows moved down one (the oldest pose went).</summary>
    internal static void ShiftFeet(Construct c)
    {
        var legs = c.LegCount;
        if (legs == 0 || c.NetHistFeet.Length != Construct.NetHistMax * legs)
            return;
        Array.Copy(c.NetHistFeet, legs, c.NetHistFeet, 0, (Construct.NetHistMax - 1) * legs);
        Array.Copy(c.NetHistFootFlags, legs, c.NetHistFootFlags, 0, (Construct.NetHistMax - 1) * legs);
    }

    /// <summary>
    /// Client, every frame after <see cref="SampleAt"/>: the feet at server tick <paramref name="rt"/> (NaN: as last heard),
    /// along a line between the two poses either side (a swing is smooth enough over a pose's three ticks; a planted foot
    /// is the same in both, so it stays exactly still). A foot never heard stands at its rest point under the body.
    /// </summary>
    public static void SampleFeet(Construct c, double rt)
    {
        var legs = c.LegCount;
        if (legs == 0)
            return;
        var all = true;
        for (var k = 0; k < legs; k++)
            all &= c.Legs[k].NetHeard;
        c.FeetSet = c.NetFeetSet && all;
        var n = c.NetHistCount;
        var hist = c.NetHistFeet;
        var flags = c.NetHistFootFlags;
        var rows = !double.IsNaN(rt) && n > 0 && hist.Length == Construct.NetHistMax * legs;
        var ticks = c.NetHistTick;
        int a, b;
        var t = 0f;
        if (!rows)
            a = b = -1;
        else if (rt >= ticks[n - 1])
        {
            // (late poses: a swinging foot goes on at its last speed a little way, as the body does: SampleAt)
            a = n > 1 ? n - 2 : n - 1;
            b = n - 1;
            t = n > 1 ? 1 + (float)(Math.Min(rt - ticks[n - 1], MaxAheadTicks) / Math.Max(1u, ticks[n - 1] - ticks[n - 2])) : 0;
        }
        else if (rt <= ticks[0])
            a = b = 0;
        else
        {
            b = n - 1;
            while (b > 0 && ticks[b - 1] > rt)
                b--;
            a = b - 1;
            t = (float)((rt - ticks[a]) / Math.Max(1u, ticks[b] - ticks[a]));
        }
        for (var k = 0; k < legs; k++)
        {
            ref var leg = ref c.Legs[k];
            byte f;
            if (a < 0)
            {
                leg.Foot = leg.NetFoot;
                f = leg.NetFlags;
            }
            else
            {
                leg.Foot = Vector3.Lerp(hist[a * legs + k], hist[b * legs + k], t);
                f = flags[(t > 1 ? b : a) * legs + k]; // (a foot swings from the pose it lifted at to the one it landed at)
                if (t > 1 && (f & FootSwinging) == 0)
                    leg.Foot = hist[b * legs + k]; // (down: it stays put)
            }
            leg.Swinging = (f & FootSwinging) != 0;
            leg.Grounded = (f & FootGrounded) != 0;
            leg.Hold = (f & FootHold) != 0;
            leg.From = leg.To = leg.Foot;
        }
    }
}
