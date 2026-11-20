using System.Globalization;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// What the F9 window's recording (Source/UI/UiPerfCapture.cs) keeps of the constructs' physics: how many bodies the
/// physics stepped each tick (from <see cref="ConstructFar.SimNear"/> and <see cref="ConstructFar.SimFar"/>), and four
/// times a second which bodies were awake and what kept each one from sleeping (the tests in
/// <c>ConstructPhysics.Sleep</c>). A body that never sleeps steps its contacts every tick: an idle ship's physics cost is
/// usually one of these. Host or single player only (a client steps no physics). The dictionary is made once with room
/// for <see cref="MaxTracked"/> bodies, so a sample allocates nothing.
/// </summary>
public sealed class PerfCaptureBodies
{
    /// <summary>Most bodies tracked in one recording (the rest are counted, not named).</summary>
    public const int MaxTracked = 256;

    /// <summary>Seconds between samples of the awake bodies.</summary>
    public const double Every = 0.25;

    [Flags]
    public enum Why : ushort
    {
        None = 0,
        Moving = 1, // faster than still (ConstructPhysics.StillSpeed2)
        DriverInput = 2, // a driver's throttle, steering, lift or roll keys
        EngineSignal = 4, // a wired engine signal
        SteerSignal = 8, // wired steering
        Hover = 16, // the hover hold
        WireSignal = 32, // a wire asks a hub motor or a thruster for something (ActiveSignal)
        Stabilizer = 64, // a flight stabilizer at work
        Thrust = 128, // thrusters pushing
        Rotor = 256, // a rotor turning or asked to
        Joint = 512, // a joint moving, or its motor pushing
        Held = 1024, // in a physics gun's beam
    }

    private static readonly string[] WhyNames =
        ["moving", "driver keys", "engine signal", "steer signal", "hover hold", "wire signal (ActiveSignal)", "stabilizer", "thrusters", "rotor", "joint or motor", "physics gun"];

    private struct Seen
    {
        public int Samples; // samples it was awake in
        public Why Why; // every reason seen
        public string Name;
        public int Parts;
        public float Mass, TopSpeed;
    }

    private readonly Dictionary<ushort, Seen> _seen = new(MaxTracked);
    private long _sim0;
    private double _nextAt;
    private bool _server;

    /// <summary>Samples taken, and the most bodies awake in one, and bodies awake past <see cref="MaxTracked"/>.</summary>
    public int Samples, MostAwake, Untracked;

    public void Begin(Game? game, double now)
    {
        _seen.Clear();
        Samples = MostAwake = Untracked = 0;
        _sim0 = ConstructFar.SimNear + ConstructFar.SimFar;
        _nextAt = now;
        _server = game is { IsServer: true };
    }

    /// <summary>Every frame while recording: a sample when one is due.</summary>
    public void Frame(Game? game, double now)
    {
        if (now < _nextAt || game is not { IsServer: true } g)
            return;
        _nextAt = now + Every;
        Samples++;
        var awake = 0;
        foreach (var c in g.Constructs.Bodies)
        {
            if (c.Asleep || c.Anchored || c.CarriedBy != 0 || c.SpaceShip != 0 || c.PartCount == 0)
                continue;
            awake++;
            var why = WhyAwake(c);
            if (_seen.TryGetValue(c.Id, out var s))
            {
                s.Samples++;
                s.Why |= why;
                s.TopSpeed = MathF.Max(s.TopSpeed, c.Vel.Length());
                _seen[c.Id] = s;
            }
            else if (_seen.Count < MaxTracked)
                _seen[c.Id] = new Seen { Samples = 1, Why = why, Name = c.DefOf(0).Name, Parts = c.PartCount, Mass = c.Mass, TopSpeed = c.Vel.Length() };
            else
                Untracked++;
        }
        MostAwake = Math.Max(MostAwake, awake);
    }

    /// <summary>What keeps a body awake now: the tests <c>ConstructPhysics.Sleep</c> makes (None: still, about to sleep).</summary>
    private static Why WhyAwake(Construct c)
    {
        var why = Why.None;
        var framed = c.Framed;
        var v = framed ? c.Vel - c.FrameV : c.Vel;
        var w = framed ? c.AngVel - c.FrameW : c.AngVel;
        if (v.LengthSquared() >= ConstructPhysics.StillSpeed2 || w.LengthSquared() * MathF.Max(1, c.Radius * c.Radius / 64f) >= ConstructPhysics.StillSpeed2)
            why |= Why.Moving;
        if (c.Driven && (c.Throttle != 0 || c.SteerInput != 0 || c.LiftInput != 0 || c.RollInput != 0))
            why |= Why.DriverInput;
        if (c.EngineWired && c.EngineSignal != 0)
            why |= Why.EngineSignal;
        if (c.SteerSignal != 0)
            why |= Why.SteerSignal;
        if (c.HoverOn)
            why |= Why.Hover;
        if (c.ActiveSignal)
            why |= Why.WireSignal;
        if (c.StabLive)
            why |= Why.Stabilizer;
        if (c.ThrustShow > 0)
            why |= Why.Thrust;
        if (c.RotorLive)
            why |= Why.Rotor;
        if (c.GrabbedBy != 255)
            why |= Why.Held;
        for (var j = 0; j < c.JointCount; j++)
            if (MathF.Abs(c.Joints[j].Vel) >= 0.05f || c.Joints[j].Motorised && !c.Joints[j].Frozen)
            {
                why |= Why.Joint;
                break;
            }
        return why;
    }

    /// <summary>The report's lines (allocates: only when the recording stops).</summary>
    public void Report(StringBuilder sb, int ticks)
    {
        var c = CultureInfo.InvariantCulture;
        if (!_server)
        {
            sb.Append("construct physics: not the host (a joined client steps no physics)\n");
            return;
        }
        var simulated = ConstructFar.SimNear + ConstructFar.SimFar - _sim0;
        sb.Append(c, $"construct physics: {(ticks > 0 ? (double)simulated / ticks : 0):0.0} bodies in the step per tick on average, of {ConstructPhysics.LastWorldBodies} (a small world steps them all; a big one the awake ones and what they touch); most awake in one sample {MostAwake} ({Samples} samples, every {Every:0.00} s)\n");
        if (_seen.Count == 0)
        {
            sb.Append("  no body was awake in any sample (every construct slept)\n");
            return;
        }
        sb.Append("  awake bodies, most often first (share of samples awake; what kept it awake, any time it was seen):\n");
        foreach (var (id, s) in _seen.OrderByDescending(kv => kv.Value.Samples).ThenByDescending(kv => kv.Value.Parts).Take(20))
        {
            var reasons = new StringBuilder();
            for (var b = 0; b < WhyNames.Length; b++)
                if (((int)s.Why & (1 << b)) != 0)
                    reasons.Append(reasons.Length > 0 ? ", " : "").Append(WhyNames[b]);
            if (reasons.Length == 0)
                reasons.Append("nothing: still, falling asleep");
            sb.Append(c, $"    body {id} {s.Name} x{s.Parts}, {s.Mass:0} kg: awake {100.0 * s.Samples / Math.Max(1, Samples):0}%, top speed {s.TopSpeed:0.000} m/s; {reasons}\n");
        }
        if (_seen.Count > 20)
            sb.Append(c, $"    ... and {_seen.Count - 20} more\n");
        if (Untracked > 0)
            sb.Append(c, $"    ({Untracked} sightings of bodies past the first {MaxTracked} were not named)\n");
    }
}
