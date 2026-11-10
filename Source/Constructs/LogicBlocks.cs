namespace CallOfSlop;

/// <summary>
/// The logic blocks past the first gates (docs/sandbox/CONSTRUCTS.md, "Logic blocks"): one tick of each, from what the logic
/// step added up, in the same rule: a wire carries last tick's value and nothing allocates. What a block must keep over a save
/// or a join is a setting (a Counter's or Memory's value: <see cref="CfgKey.Value"/>, so it rides in CBody and blueprints) or its
/// <see cref="CPart.State"/>. Parts with more data ports than the step has sums (<see cref="ConstructPartDef.WidePorts"/>) have a
/// run of <see cref="PortWidth"/> floats in <see cref="Construct.PortIn"/> and <see cref="Construct.PortOut"/>.
/// </summary>
public static class LogicBlocks
{
    public const int PortWidth = 17, BusWidth = 8; // (a Flight Stabilizer's outputs; a link's byte holds more: ConstructRoutes.MaxDataPorts)
    private const int EdgeA = 2, EdgeB = 4, EdgeC = 8; // State bits: the input last tick (bit 0 is the block's own)
    private static uint _rng = 0x9E3779B9;

    /// <summary>The named ports a kind has. <paramref name="wide"/>: "logic.ports", a lamp bank's lamps.</summary>
    public static void Ports(ConstructPartDef d, int wide)
    {
        switch (d.Logic)
        {
            case CLogic.MathBlock:
                d.DataIn = ["a", "b"];
                break;
            case CLogic.Cipher: // (a data centre's terminal: GameStoryFragments.cs)
                d.DataIn = ["key"];
                d.DataOut = ["carrier"];
                break;
            case CLogic.Latch:
                d.DataIn = ["set", "reset"];
                break;
            case CLogic.Counter:
                d.DataIn = ["up", "down", "reset"];
                d.DataOut = ["count", "at limit"];
                break;
            case CLogic.Memory:
                d.DataIn = ["value", "store"];
                break;
            case CLogic.Mux:
                d.DataIn = ["select", "1", "2", "3", "4", "5", "6"];
                break;
            case CLogic.Numpad:
                d.DataOut = ["value", "accepted"];
                break;
            case CLogic.TiltSensor:
                d.DataOut = ["pitch", "roll"];
                break;
            case CLogic.LevelSensor:
                d.DataOut = ["level", ConstructRoutes.WatchName];
                break;
            case CLogic.DamageSensor:
                d.DataOut = ["hit", ConstructRoutes.WatchName];
                break;
            case CLogic.BusIn:
                d.DataIn = Numbered(BusWidth);
                break;
            case CLogic.BusOut:
                d.DataOut = Numbered(BusWidth);
                break;
            case CLogic.Display when wide > 1:
                d.DataIn = Numbered(Math.Min(wide, BusWidth));
                break;
        }
    }

    private static string[] Numbered(int n) => Enumerable.Range(1, n).Select(k => k.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    /// <summary>With the construct's other caches: the part lists these blocks walk, and room for their ports.</summary>
    public static void Rebuild(Construct c)
    {
        int reading = 0, graphs = 0, radios = 0, wide = 0;
        foreach (var i in c.Specials) // (logic parts are special parts: ConstructRebuild.cs)
        {
            var d = c.Parts[i].D;
            if (d.Role != PartRole.Logic)
                continue;
            reading += d.HasReading ? 1 : 0;
            graphs += d.Logic == CLogic.Display && LogicDisplays.IsGraph(d) ? 1 : 0;
            radios += d.Logic == CLogic.RadioTx ? 1 : 0;
            wide += d.WidePorts ? 1 : 0;
        }
        if (c.ReadingParts.Length != reading)
            c.ReadingParts = reading == 0 ? [] : new ushort[reading];
        if (c.GraphParts.Length != graphs)
            c.GraphParts = graphs == 0 ? [] : new ushort[graphs];
        if (c.RadioParts.Length != radios)
            c.RadioParts = radios == 0 ? [] : new ushort[radios];
        if (wide == 0)
        {
            c.PortBase = [];
            c.PortIn = c.PortOut = [];
        }
        else
        {
            if (c.PortBase.Length != c.PartCount)
                c.PortBase = new int[c.PartCount];
            if (c.PortIn.Length != wide * PortWidth)
            {
                c.PortIn = new float[wide * PortWidth];
                c.PortOut = new float[wide * PortWidth];
            }
        }
        int ra = 0, gr = 0, rd = 0, at = 0;
        if (wide > 0)
            for (var i = 0; i < c.PartCount; i++)
                c.PortBase[i] = c.Parts[i].D.WidePorts ? at++ * PortWidth : -1; // (every part: -1 for most)
        foreach (var i in c.Specials)
        {
            var d = c.Parts[i].D;
            if (d.Role != PartRole.Logic)
                continue;
            if (d.HasReading)
                c.ReadingParts[ra++] = (ushort)i;
            if (d.Logic == CLogic.Display && LogicDisplays.IsGraph(d))
                c.GraphParts[gr++] = (ushort)i;
            if (d.Logic == CLogic.RadioTx)
                c.RadioParts[rd++] = (ushort)i;
        }
    }

    /// <summary>What output port 2 and up of a part puts out (a bus breakout's).</summary>
    public static float OutPort(Construct c, int part, int port) =>
        c.PortBase.Length > 0 && c.PortBase[part] >= 0 ? c.PortOut[c.PortBase[part] + Math.Min(port, PortWidth - 1)] : 0;

    private static bool Rising(ref CPart p, int bit, bool on)
    {
        var rise = on && (p.State & bit) == 0;
        p.State = (byte)(on ? p.State | bit : p.State & ~bit);
        return rise;
    }

    private static void Keep(ref CPart p, ConstructPartDef d, float v)
    {
        var slot = d.CfgSlot[(int)CfgKey.Value];
        if (slot >= 0)
            p.Cfg[slot] = v;
    }

    /// <summary>
    /// One block, one tick. <paramref name="a"/>, <paramref name="b"/> and <paramref name="x"/>: the sums into ports 0, 1 and 2 and up;
    /// <paramref name="na"/>, <paramref name="nb"/>: how many wires into the first two; <paramref name="high"/>: of port 0's, how many are
    /// on; <paramref name="from"/>: the part wired into port 0 (ushort.MaxValue: none here).
    /// </summary>
    public static float Step(ConstructWorld world, Construct c, int i, ref CPart p, ConstructPartDef d, float a, float b, float x, int na, int nb, int high, int from, float dt)
    {
        switch (d.Logic)
        {
            case CLogic.Xor:
                return high % 2;
            case CLogic.Nand:
                return na > 0 && high == na ? 0 : 1;
            case CLogic.MathBlock:
            {
                var rhs = nb > 0 ? b : ConstructConfig.Get(p, d, CfgKey.Offset, 0);
                return (int)ConstructConfig.Get(p, d, CfgKey.Operator, 0) switch
                {
                    1 => a - rhs,
                    2 => a * rhs,
                    3 => rhs != 0 ? a / rhs : 0,
                    4 => MathF.Min(a, rhs),
                    5 => MathF.Max(a, rhs),
                    _ => a + rhs,
                };
            }
            case CLogic.Latch:
                if (b >= 0.5f)
                    p.State = (byte)(p.State & ~1);
                else if (a >= 0.5f)
                    p.State |= 1;
                return p.State & 1;
            case CLogic.Counter:
                return CounterStep(ref p, d, a >= 0.5f, b >= 0.5f, x >= 0.5f);
            case CLogic.Memory:
                if (Rising(ref p, EdgeB, b >= 0.5f))
                    Keep(ref p, d, a);
                return ConstructConfig.Get(p, d, CfgKey.Value, 0);
            case CLogic.Filter:
            {
                var lo = ConstructConfig.Get(p, d, CfgKey.Low, -ConstructLogic.MaxSignal);
                var hi = ConstructConfig.Get(p, d, CfgKey.High, ConstructLogic.MaxSignal);
                var want = Math.Clamp(a, MathF.Min(lo, hi), MathF.Max(lo, hi));
                var tau = ConstructConfig.Get(p, d, CfgKey.Seconds, 0);
                if (tau > 0)
                    want = float.Lerp(p.Out, want, 1 - MathF.Exp(-dt / tau));
                var rate = ConstructConfig.Get(p, d, CfgKey.Rate, 0) * dt;
                return rate > 0 ? Math.Clamp(want, p.Out - rate, p.Out + rate) : want;
            }
            case CLogic.Mux:
            {
                var at = c.PortBase[i];
                var pick = na > 0 ? Math.Clamp((int)MathF.Round(a), 1, 6) : 1;
                return c.PortIn[at + pick];
            }
            case CLogic.RandomPulse:
                if (Rising(ref p, EdgeA, a >= 0.5f))
                {
                    _rng ^= _rng << 13;
                    _rng ^= _rng >> 17;
                    _rng ^= _rng << 5;
                    Keep(ref p, d, float.Lerp(ConstructConfig.Get(p, d, CfgKey.Low, 0), ConstructConfig.Get(p, d, CfgKey.High, 1), (_rng >> 8) / (float)(1 << 24)));
                }
                return ConstructConfig.Get(p, d, CfgKey.Value, 0);
            case CLogic.Numpad:
            case CLogic.CodeLock:
                return LogicInputs.Step(ref p, d);
            case CLogic.Display:
                if (d.DataIn.Length > 1)
                {
                    // A lamp bank: a bit a lamp.
                    var at = c.PortBase[i];
                    var bits = a >= 0.5f ? 1 : 0;
                    for (var k = 1; k < d.DataIn.Length; k++)
                        bits |= c.PortIn[at + k] >= 0.5f ? 1 << k : 0;
                    p.Reading = bits;
                }
                else
                    p.Reading = a;
                return 0;
            case CLogic.Instrument:
                return 0;
            case CLogic.AltitudeSensor:
            case CLogic.TiltSensor:
            case CLogic.HeadingSensor:
                return LogicSensors.Pose(c, i, ref p, d);
            case CLogic.LevelSensor:
                return p.Watch == 0 ? 0 : LogicSensors.Level(world, c, p.Watch - 1) * 100;
            case CLogic.DamageSensor:
                return LogicSensors.Damage(c, ref p, d);
            case CLogic.OccupancySensor:
            {
                var seat = from < c.PartCount && c.Parts[from].D.Role == PartRole.Seat ? from : c.DriveSeat;
                return seat >= 0 && seat < c.PartCount && (c.Parts[seat].Keys & CPart.Occupied) != 0 ? 1 : 0;
            }
            case CLogic.RadioTx:
                p.Mem = a;
                return 0;
            case CLogic.RadioRx:
                return world.RadioHeard(c, i, ref p, d);
            case CLogic.BusIn:
            {
                var at = c.PortBase[i];
                c.PortOut[at] = a;
                for (var k = 1; k < BusWidth; k++)
                    c.PortOut[at + k] = c.PortIn[at + k];
                return 0;
            }
            case CLogic.BusOut:
            {
                var at = c.PortBase[i];
                var src = from < c.PartCount && c.Parts[from].D.Logic == CLogic.BusIn && !c.Parts[from].Broken ? c.PortBase[from] : -1;
                for (var k = 0; k < BusWidth; k++)
                    c.PortOut[at + k] = src >= 0 ? c.PortOut[src + k] : 0;
                p.Out1 = c.PortOut[at + 1];
                return c.PortOut[at];
            }
        }
        return 0;
    }

    private static float CounterStep(ref CPart p, ConstructPartDef d, bool up, bool down, bool reset)
    {
        var v = ConstructConfig.Get(p, d, CfgKey.Value, 0);
        var limit = ConstructConfig.Get(p, d, CfgKey.Limit, 0);
        var wrap = ConstructConfig.Get(p, d, CfgKey.Wrap, 0) >= 0.5f;
        if (Rising(ref p, EdgeA, up))
            v = limit > 0 && v + 1 > limit ? wrap ? 0 : limit : v + 1;
        if (Rising(ref p, EdgeB, down))
            v = limit > 0 && v - 1 < 0 ? wrap ? limit : 0 : v - 1;
        if (Rising(ref p, EdgeC, reset))
            v = 0;
        Keep(ref p, d, v);
        p.Out1 = limit > 0 && v >= limit ? 1 : 0;
        return v;
    }
}

/// <summary>What the pose and watch sensors read (<see cref="CLogic.AltitudeSensor"/> and on). Client and server: the panels read the same.</summary>
public static class LogicSensors
{
    /// <summary>The way a part's front (its -z) faces, world.</summary>
    public static System.Numerics.Vector3 Front(Construct c, int part, int axis = 2)
    {
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out _);
        var local = axis == 2 ? -System.Numerics.Vector3.UnitZ : System.Numerics.Vector3.UnitX;
        return System.Numerics.Vector3.Transform(Rot24.Apply(p.Rot, local), q);
    }

    /// <summary>Degrees clockwise from north (the world's -z), 0 to 360.</summary>
    public static float Heading(Construct c, int part)
    {
        var f = Front(c, part);
        GravityFrames.Frame(c.WorldCom, out var right, out _, out var north);
        var a = MathF.Atan2(System.Numerics.Vector3.Dot(f, right), System.Numerics.Vector3.Dot(f, north)) * 180 / MathF.PI;
        return a < 0 ? a + 360 : a;
    }

    public static float Altitude(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out var t);
        return ConstructPhysics.HoldShown(ConstructPhysics.HoldHeight(t + System.Numerics.Vector3.Transform(p.Pos, q)));
    }

    public static float Pose(Construct c, int i, ref CPart p, ConstructPartDef d)
    {
        switch (d.Logic)
        {
            case CLogic.AltitudeSensor:
                return Altitude(c, i);
            case CLogic.HeadingSensor:
                return Heading(c, i);
            default:
                var up = ConstructPhysics.HoldUp(c.WorldCom); // (level is against the gravity where it is)
                p.Out1 = MathF.Asin(Math.Clamp(System.Numerics.Vector3.Dot(Front(c, i, 0), up), -1, 1)) * 180 / MathF.PI;
                return MathF.Asin(Math.Clamp(System.Numerics.Vector3.Dot(Front(c, i), up), -1, 1)) * 180 / MathF.PI;
        }
    }

    /// <summary>How full a part is, 0 to 1: a fluid tank or a Storage Volume's fill; a fuel tank's, the construct's fuel; a store's bulk or slots.</summary>
    public static float Level(ConstructWorld world, Construct c, int part)
    {
        if (part >= c.PartCount)
            return 0;
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (ConstructConfig.Has(d, CfgKey.Fill))
            return Math.Clamp(ConstructConfig.Get(p, d, CfgKey.Fill, 0) / 100f, 0, 1);
        if (d.Role == PartRole.FuelTank)
        {
            var cap = c.FuelCapacity() + c.FluidFuelCap;
            return cap > 0 ? Math.Clamp(c.FuelLeft / cap, 0, 1) : 0;
        }
        if (world.StoreOf(c, part) is { } s)
            return s.Fullness(d);
        return 0;
    }

    /// <summary>On for its hold time after the watched part lost health; its second port isn't a signal (the link).</summary>
    public static float Damage(Construct c, ref CPart p, ConstructPartDef d)
    {
        if (p.Watch == 0 || p.Watch > c.PartCount)
            return 0;
        var health = c.Parts[p.Watch - 1].Health;
        if (health < p.Mem)
            p.Count = (ushort)Math.Max(1, (int)MathF.Round(ConstructConfig.Get(p, d, CfgKey.Hold, 1) * World.TickRate));
        p.Mem = health;
        if (p.Count == 0)
            return 0;
        p.Count--;
        return 1;
    }
}
