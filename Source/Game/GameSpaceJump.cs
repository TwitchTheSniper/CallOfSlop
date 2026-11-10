using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Jumping between star systems (phase 6, docs/space/GALAXY.md; Karl, 2026-11-04: a jump drive part built into a ship).
/// A ship in space (a construct out of the bubble: GameSpaceLaunch.cs) with a working, powered Jump Drive charges it (the
/// drive seat's jump key, F on it, or a rising signal; again to stop), and once charged jumps to the system its
/// <c>jumpTarget</c> names (the nth nearest other system, <see cref="Galaxy.Nearest"/>) if that is within the drive's
/// range and the batteries hold the jump's energy (kJ a tonne a light-year). The ship arrives at the target's edge in
/// orbit round its star, or round the world the system's file names (<see cref="Galaxy.Arrive"/>), keeping its ship id.
/// <para>
/// <b>Parked ships.</b> A ship in another system than the bubble's, or more than <see cref="SpaceFarDistance"/> from the
/// World's origin in its own (floats there are metres apart: the World can't hold it), is <i>parked</i>
/// (<see cref="Construct.SpaceAway"/>): its construct waits out of the way high over the map, its physics asleep, drawn only
/// for its own riders, who see that system's sky round them (<see cref="SpaceAwayView"/>: the World's axes held fixed in
/// that system, game +Y its ecliptic north) and none of the map. Another system's ships are stepped by that system's own
/// flight on the same galaxy time (<see cref="SpaceSession.Away"/>). Its thrusters push the ship and A and D turn it as
/// in the bubble; a parked ship that comes back within reach of the bubble is posed from its ship again, and can fly back
/// down. A parked ship that comes within the sky of a world of the system it is in lands there, the whole group with it
/// (GameSpaceLanding.cs): the bubble moves to that system.
/// </para>
/// </summary>
public sealed partial class Game
{
    /// <summary>A ship farther than this from the World's origin (m) is parked (and back under 90% of it, posed from its ship again).</summary>
    public const double SpaceFarDistance = 200_000;

    /// <summary>The World's axes in another system's ecliptic (game +Y its north, +X its +X, +Z its -Y): fixed, the same on every peer.</summary>
    public static readonly QuatD AwayRot = QuatD.RotX(Math.PI / 2);

    /// <summary>Host: which construct each ship id pictures (0: none), for the ships packets; filled each tick.</summary>
    private readonly int[] _spaceConstructOf = new int[ushort.MaxValue + 1]; // (the construct id, plus 1 << 16 while parked)
    private readonly ushort[] _spaceConstructIds = new ushort[SpaceShipCapacity];
    private int _spaceConstructCount;
    private readonly int[] _spaceAwayCursor = new int[Galaxy.MaxSystems];

    /// <summary>A construct's system's galaxy index (the bubble's, or the one it jumped to).</summary>
    private static int SystemOfConstruct(SpaceSession s, Construct c) => c.SpaceAway == 0 ? s.GalaxyIndex : c.SpaceAway - 1;

    /// <summary>Host, before the ships step: thrust for the parked ships (in their system's axes: <see cref="AwayRot"/>).</summary>
    internal void SpaceAwayBefore(SpaceSession s)
    {
        if (SpaceLaunched == 0 && _spaceConstructCount == 0)
            return;
        var dt = World.TickDt;
        foreach (var c in Constructs.Bodies)
        {
            if (c.SpaceShip == 0 || c.SpaceAway == 0 || s.FlightOf(c.SpaceAway - 1) is not { } f)
                continue;
            var i = f.Ships.IndexOfId(c.SpaceShip);
            if (i < 0)
                continue;
            if (c.Dirty)
                c.Rebuild();
            var force = c.ThrusterCount > 0 ? ConstructPhysics.SpaceThrust(c, dt) : Vector3.Zero;
            var a = c.Mass > 0 ? Vec3d.From(force) / c.Mass : Vec3d.Zero;
            f.Ships.Thrust[i] = AwayRot.Rotate(a);
        }
    }

    /// <summary>
    /// Host, after the ships step: jump drives charge and jump, constructs in other systems are posed at their park, and
    /// the ship-to-construct table for the packets is filled.
    /// </summary>
    internal void SpaceJumpStep(SpaceSession s)
    {
        for (var k = 0; k < _spaceConstructCount; k++)
            _spaceConstructOf[_spaceConstructIds[k]] = 0;
        _spaceConstructCount = 0;
        if (!SandboxOn)
            return;
        var dt = World.TickDt;
        foreach (var c in Constructs.Bodies)
        {
            if (c.JumpCount > 0)
                StepJumpDrives(s, c);
            if (c.SpaceShip == 0)
            {
                c.SpaceAway = 0;
                continue;
            }
            if (c.SpaceAway == 0)
            {
                Note(c);
                continue;
            }
            if (c.SpaceAway - 1 == s.GalaxyIndex && s.Flight!.Ships.IndexOfId(c.SpaceShip) is var hi and >= 0
                && s.Bubble.PoseAt(s.Flight.Ships.Time).FromEcliptic(s.Flight.Ships.PositionOf(hi)).Length < SpaceFarDistance * 0.9)
            {
                c.SpaceAway = 0; // (back within the bubble's reach: GameSpaceLaunch.cs poses it from its ship again)
                continue;
            }
            // Parked in the World while its ship flies far out: upright (game +Y is that system's north), A and D turn it.
            SpacePose(c, c.SpaceParkAt, Vector3.Zero, c.SpaceParkAt - Vector3.UnitY * 1000f, dt);
            SpaceLaunched++; // (counted with the bubble's own: the warp rule and the tick look at it)
            Note(c);
        }
    }

    /// <summary>Host: this construct's ship in the ships packets' table.</summary>
    private void Note(Construct c)
    {
        if (_spaceConstructCount >= _spaceConstructIds.Length)
            return;
        _spaceConstructIds[_spaceConstructCount++] = c.SpaceShip;
        _spaceConstructOf[c.SpaceShip] = c.Id | (c.SpaceAway != 0 ? 1 << 16 : 0);
    }

    /// <summary>Host: a construct's jump drives: charge while asked (in space, working and powered), jump when full.</summary>
    private void StepJumpDrives(SpaceSession s, Construct c)
    {
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            var d = p.D;
            if (!d.IsJumpDrive)
                continue;
            if ((p.State & 1) == 0)
            {
                p.Count = 0;
                continue;
            }
            if (c.SpaceShip == 0 || p.Broken || JumpProblem(s, c, i, out _, out _, out _) is { } why)
            {
                if (c.SpaceShip != 0)
                    Console.WriteLine($"[space] construct {c.Id}'s {d.Name} stopped: {JumpProblem(s, c, i, out _, out _, out _) ?? "wrecked"}");
                p.State &= unchecked((byte)~1);
                p.Count = 0;
                continue;
            }
            if (!ConstructPower.On(c, i))
                continue; // (no power: the charge waits)
            if (++p.Count < (int)MathF.Ceiling(d.JumpCharge * World.TickRate))
                continue;
            p.State &= unchecked((byte)~1);
            p.Count = 0;
            JumpProblem(s, c, i, out var to, out var ly, out var cost);
            Jump(s, c, to, ly, cost);
        }
    }

    /// <summary>
    /// Any peer: why a jump drive can't take its ship where its target names (null: it can), and where, how far and the
    /// energy it would take (kJ). Works from what every peer has (the galaxy, the part's setting, the construct's mass and
    /// charge, which system it is in), so the HUD says the same as the host.
    /// </summary>
    public string? JumpProblem(SpaceSession s, Construct c, int part, out int to, out double ly, out float cost)
    {
        to = -1;
        ly = 0;
        cost = 0;
        var d = c.DefOf(part);
        var from = SystemOfConstruct(s, c);
        if (from < 0)
            return "this star system isn't in the galaxy";
        var nth = Math.Max(1, (int)MathF.Round(ConstructConfig.Get(c.Parts[part], d, CfgKey.JumpTarget, 1)));
        if (!Galaxy.Nearest(from, nth, out to, out ly))
            return Galaxy.Count <= 1 ? "there is no other star system" : $"there are only {Galaxy.Count - 1} other star systems (target {nth})";
        var name = Galaxy.System(to).Name;
        if (ly > d.JumpRange)
            return FormattableString.Invariant($"{name} is {ly:0.0} ly away, past the drive's {d.JumpRange:0.#} ly");
        cost = d.JumpEnergy * c.Mass / 1000f * (float)ly;
        if (!c.Electrical || c.Charge < cost)
            return FormattableString.Invariant($"the jump to {name} takes {cost:0} kJ, the batteries hold {(c.Electrical ? c.Charge : 0):0}");
        return null;
    }

    /// <summary>Host: the jump itself: the ship leaves its system's flight and arrives in the target's, keeping its id.</summary>
    private void Jump(SpaceSession s, Construct c, int to, double ly, float cost)
    {
        var from = SystemOfConstruct(s, c);
        var ff = s.FlightOf(from);
        var i = ff?.Ships.IndexOfId(c.SpaceShip) ?? -1;
        if (ff == null || i < 0)
            return;
        var tf = s.AwayFlight(to);
        var id = c.SpaceShip;
        ff.Ships.Remove(i);
        Galaxy.Arrive(from, to, tf.Ships.Time, out var pos, out var vel);
        var j = tf.Ships.AddWithId(id, pos, vel);
        if (j < 0)
        {
            Console.WriteLine($"[space] construct {c.Id} couldn't jump: {Galaxy.System(to).Name} holds no more ships");
            ff.Ships.Add(pos, vel); // (can't happen in practice: capacity 256 a system)
            return;
        }
        c.UseCharge(cost);
        SpacePark(c, to); // (a jump always arrives far out: at the edge of a system)
        c.AngVel = Vector3.Zero;
        c.Wake();
        var sys = Galaxy.System(to);
        StoryJump(c, sys.Name); // (GameStory.cs)
        Console.WriteLine(FormattableString.Invariant(
            $"[space] construct {c.Id} (ship {id}) jumped {ly:0.00} ly from {Galaxy.System(from).Name} to {sys.Name}: {Far(sys.Arrival)} from {sys.Bodies[sys.ArrivalBody].Name}, {cost:0} kJ"));
    }

    /// <summary>Host: a construct's ship is parked in galaxy system <paramref name="galaxy"/> (its picture moves to its park spot).</summary>
    internal void SpacePark(Construct c, int galaxy)
    {
        if (c.SpaceAway == 0)
            c.SpaceParkAt = ParkSpot(c);
        c.SpaceAway = (byte)(galaxy + 1);
    }

    /// <summary>Where a construct waits in the World while its ship is parked: high over the map, each in its own spot.</summary>
    private Vector3 ParkSpot(Construct c)
    {
        var up = MathF.Max(SpaceEdge, MathF.Max(World.Map.HalfX, World.Map.HalfZ)) + 3000f;
        var k = c.Id % 64;
        return new Vector3((k % 8 - 3.5f) * 800f, up, (k / 8 - 3.5f) * 800f);
    }

    /// <summary>
    /// Any peer: a construct's ship in another system: that system (galaxy index) and the ship's ecliptic position and
    /// velocity now. False when it is in the bubble's system or its ship isn't known here.
    /// </summary>
    public bool SpaceAwayShip(Construct c, SpaceTime t, out int galaxy, out SpacePos pos, out Vec3d vel)
    {
        galaxy = -1;
        pos = default;
        vel = default;
        if (Space is not { } s || c.SpaceAway == 0)
            return false;
        galaxy = c.SpaceAway - 1;
        if (s.IsHost)
        {
            var f = s.FlightOf(galaxy);
            var i = f?.Ships.IndexOfId(c.SpaceShip) ?? -1;
            if (f == null || i < 0)
                return false;
            pos = f.Ships.PositionOf(i);
            vel = f.Ships.VelocityOf(i);
            return true;
        }
        foreach (var (_, v) in s.Ships)
            if (v.Construct == c.Id && v.System == galaxy)
                return v.Track.Sample(s.SystemOf(v), t, out pos, out vel);
        return false;
    }

    /// <summary>
    /// Any peer, each frame: is the local player riding a ship in another system? Then that system, the frame the World's
    /// coordinates are in there (<see cref="AwayRot"/>, the construct's centre of mass on the ship), its galaxy index and
    /// the construct. The renderer draws that system's sky and only that construct (Program.DrawArena).
    /// </summary>
    public bool SpaceAwayView(SpaceTime t, out StarSystem sys, out FramePose pose, out int galaxy, out Construct? body)
    {
        sys = null!;
        pose = default;
        galaxy = -1;
        body = null;
        if (!SandboxOn || Space == null || Local is not { Alive: true } me)
            return false;
        ref var h = ref SandboxHands.Hand(me.Id);
        if (!h.Seated || Constructs.Get(h.SeatBody) is not { SpaceAway: not 0 } c || !SpaceAwayShip(c, t, out galaxy, out var pos, out var vel))
            return false;
        sys = Galaxy.System(galaxy);
        pose = new FramePose(pos - AwayRot.Rotate(Vec3d.From(c.WorldCom)), AwayRot, vel, Vec3d.Zero);
        body = c;
        return true;
    }

    private SbText _sbAway, _sbJump;

    /// <summary>Any peer: the driver's line for a ship in another system: where it is there.</summary>
    internal string? SpaceAwayLine(Construct c)
    {
        if (c.SpaceAway == 0 || !SpaceAwayShip(c, SpaceRenderTime, out var g, out var pos, out var vel))
            return null;
        var sys = Galaxy.System(g);
        sys.StateOf(0, SpaceRenderTime, out var star, out var starVel);
        var r = (pos - star).Length;
        var near = -1;
        var nearD = double.MaxValue;
        for (var b = 1; b < sys.Count; b++)
        {
            var dd = (sys.PositionOf(b, SpaceRenderTime) - pos).Length;
            if (dd < nearD)
                (near, nearD) = (b, dd);
        }
        var key = (long)(r / 1e6) ^ ((long)Math.Round((vel - starVel).Length) << 32) ^ ((long)g << 56) ^ (near >= 0 ? (long)(nearD / 1e6) << 12 : 0);
        if (_sbAway.Stale(key))
            _sbAway.Text = FormattableString.Invariant($"In space in {sys.Name}: {r / StarSystemFile.Au:0.000} AU from {sys.Bodies[0].Name}, {(vel - starVel).Length:0} m/s")
                           + (near >= 0 ? FormattableString.Invariant($"; nearest {sys.Bodies[near].Name}, {Far(nearD)}") : "");
        return _sbAway.Text;
    }

    private static string Far(double m) => m >= 0.05 * StarSystemFile.Au ? FormattableString.Invariant($"{m / StarSystemFile.Au:0.00} AU") : FormattableString.Invariant($"{m / 1000:0} km");

    /// <summary>Any peer: the driver's line for a ship's first jump drive: its target and whether it can go, or how far charged.</summary>
    internal string? SpaceJumpLine(Construct c)
    {
        if (c.JumpCount == 0 || Space is not { } s)
            return null;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (!p.D.IsJumpDrive)
                continue;
            var why = JumpProblem(s, c, i, out var to, out var ly, out var cost);
            var inSpace = c.SpaceAway != 0 || IsServer ? c.SpaceShip != 0 : SpaceShipOfConstruct(s, c);
            var charging = (p.State & 1) != 0;
            var key = ((long)to << 2) ^ (long)(ly * 100) << 8 ^ ((long)MathF.Round(p.Out * 100) << 30) ^ (charging ? 1L << 40 : 0) ^ (inSpace ? 1L << 41 : 0)
                      ^ (p.Broken ? 1L << 42 : 0) ^ ((long)(why?.GetHashCode() ?? 0) << 43);
            if (_sbJump.Stale(key))
                _sbJump.Text = p.Broken ? "Jump drive wrecked"
                    : to >= 0 && why == null ? FormattableString.Invariant($"Jump drive: {Galaxy.System(to).Name}, {ly:0.0} ly, {cost:0} kJ")
                        + (charging ? $": charging {p.Out * 100:0}%" : inSpace ? $" ({SandboxKeys.JumpKey} to charge)" : " (in space only)")
                    : $"Jump drive: {why}";
            return _sbJump.Text;
        }
        return null;
    }

    /// <summary>Client: does a host ship picture this construct (it is in space)?</summary>
    private static bool SpaceShipOfConstruct(SpaceSession s, Construct c)
    {
        foreach (var (_, v) in s.Ships)
            if (v.Construct == c.Id)
                return true;
        return false;
    }
}
