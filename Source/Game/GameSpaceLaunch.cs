using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Leaving the bubble (docs/space/SPACE.md, "Leaving the bubble"). On a planetoid map in a star system, a construct whose
/// centre of mass climbs past the sky's edge (the map's half size from the planet's centre, where players' sky ends:
/// <see cref="World.ClampToSky"/>) becomes a ship of the system's flight (<see cref="SpaceShips"/>): a test particle in the
/// gravity of every body on rails, coasting on the symplectic step (or on rails) under the space clock, at any warp.
/// The construct stays in the World as the ship's picture: its physics sleeps (<see cref="Construct.SpaceShip"/>), the
/// host poses it from the ship every tick, and the poses go to clients the usual way, so its riders stay in their seats
/// and see the planetoid turn below. Its thrusters push the ship (W and S by how they face the seat, Shift those that push
/// up: <see cref="ConstructPhysics.SpaceThrust"/>), A and D turn it, and it is kept level over the ground below. Coming
/// back under the edge it is a physics body again, with the ship's speed, and the hover hold catches it where it came in.
/// Host only; nothing new goes over the wire.
/// </summary>
public sealed partial class Game
{
    /// <summary>A ship comes back into the bubble this far under the edge (m: so it can't flicker in and out).</summary>
    public const float SpaceEdgeHysteresis = 200f;

    /// <summary>How fast A and D turn a ship in space (rad/s), and how fast it is brought level over the ground below (1/s).</summary>
    public const float SpaceYawRate = 0.8f, SpaceLevelRate = 2f;

    /// <summary>Host: constructs that left the bubble and their ships, by construct id (counted, so the tick needs no search when none have).</summary>
    public int SpaceLaunched { get; private set; }

    /// <summary>
    /// The bubble's edge (m from the planet's centre), or 0 when this map can't be left: a planetoid map played in a star
    /// system, as the system's body (the sky ends at the map's half size).
    /// </summary>
    public float SpaceEdge => Space != null && World.Map.Planetoid is { } pl && GravityFrames.Point != null
        ? MathF.Max(MathF.Max(World.Map.HalfX, World.Map.HalfZ), (float)pl.Radius + 500f) : 0f;

    /// <summary>Is this construct in space (out of the bubble, posed by its ship)?</summary>
    public static bool InSpace(Construct c) => c.SpaceShip != 0;

    /// <summary>Host, before the ships step: each ship's thrust from its construct's thrusters, and its turn.</summary>
    internal void SpaceLaunchBefore(SpaceFlight f)
    {
        if (SpaceLaunched == 0)
            return;
        var pose = Space!.Bubble.PoseAt(f.Ships.Time);
        var dt = World.TickDt;
        foreach (var c in Constructs.Bodies)
        {
            if (c.SpaceShip == 0)
                continue;
            var i = f.Ships.IndexOfId(c.SpaceShip);
            if (i < 0)
                continue;
            if (c.Dirty)
                c.Rebuild();
            var force = c.ThrusterCount > 0 ? ConstructPhysics.SpaceThrust(c, dt) : Vector3.Zero;
            var a = c.Mass > 0 ? Vec3d.From(force) / c.Mass : Vec3d.Zero;
            f.Ships.Thrust[i] = pose.Rot.Rotate(a); // (game axes to the ecliptic's)
        }
    }

    /// <summary>
    /// Host, after the ships step: constructs past the edge become ships, ships pose their constructs, and those back
    /// under the edge (or whose ship went) are physics bodies again.
    /// </summary>
    internal void SpaceLaunchAfter(SpaceFlight f)
    {
        var edge = SpaceEdge;
        if (edge <= 0 || !SandboxOn)
        {
            if (SpaceLaunched > 0)
                SpaceLandAll(f);
            return;
        }
        var centre = GravityFrames.Point!.Center;
        var pose = Space!.Bubble.PoseAt(f.Ships.Time);
        var dt = World.TickDt;
        var launched = 0;
        foreach (var c in Constructs.Bodies)
        {
            if (c.SpaceShip == 0)
            {
                if (c.Anchored || c.Asleep || c.PartCount == 0 || c.CarriedBy != 0 || c.GrabbedBy != 255 || c.Frozen || c.Pinned)
                    continue;
                var com = c.WorldCom;
                if (Vector3.DistanceSquared(com, centre) <= edge * edge)
                    continue;
                if (!SpaceLeave(f, pose, c))
                    continue;
            }
            var i = f.Ships.IndexOfId(c.SpaceShip);
            if (i < 0)
            {
                SpaceEnter(c, null, 0, pose);
                continue;
            }
            var at = pose.FromEcliptic(f.Ships.PositionOf(i));
            var vel = pose.VelocityFromEcliptic(f.Ships.PositionOf(i), f.Ships.VelocityOf(i));
            var p = at.ToVector3();
            if (Vector3.DistanceSquared(p, centre) < (edge - SpaceEdgeHysteresis) * (edge - SpaceEdgeHysteresis))
            {
                SpaceEnter(c, f.Ships, i, pose);
                continue;
            }
            SpacePose(c, p, vel.ToVector3(), centre, dt);
            launched++;
        }
        SpaceLaunched = launched;
    }

    /// <summary>Host: a construct past the edge becomes a ship (false: the system holds no more ships).</summary>
    private bool SpaceLeave(SpaceFlight f, in FramePose pose, Construct c)
    {
        var com = Vec3d.From(c.WorldCom);
        var i = f.Ships.Add(pose.ToEcliptic(com), pose.VelocityToEcliptic(com, Vec3d.From(c.Vel)));
        if (i < 0)
            return false;
        c.SpaceShip = f.Ships.Ids[i];
        c.HoverOn = false; // (nothing to hold a height over in space; it catches again on the way back in)
        c.AngVel = Vector3.Zero;
        c.Wake();
        Console.WriteLine($"[space] construct {c.Id} left the bubble at {ConstructPhysics.HoldShown(ConstructPhysics.HoldHeight(c.WorldCom)):0} m, {c.Vel.Length():0} m/s: ship {c.SpaceShip} round {f.System.Bodies[f.Ships.PrimaryOf(i)].Name}");
        return true;
    }

    /// <summary>Host: back into the bubble (or its ship went): a physics body again at the ship's place and speed, and the hover hold catches it.</summary>
    private void SpaceEnter(Construct c, SpaceShips? ships, int i, in FramePose pose)
    {
        var ship = c.SpaceShip;
        c.SpaceShip = 0;
        if (ships != null)
        {
            var at = pose.FromEcliptic(ships.PositionOf(i)).ToVector3();
            c.Vel = pose.VelocityFromEcliptic(ships.PositionOf(i), ships.VelocityOf(i)).ToVector3();
            c.Pos = at - Vector3.Transform(c.Com, c.Rot);
            ships.Remove(i);
        }
        c.AngVel = Vector3.Zero;
        c.PrevPos = c.Pos;
        c.PrevRot = c.Rot;
        var lifts = false;
        for (var k = 0; k < c.ThrusterCount; k++)
            lifts |= c.Thrusters[k].Lifts;
        c.HoverOn = lifts;
        c.HoverY = ConstructPhysics.HoldHeight(c.WorldCom);
        c.Wake();
        Console.WriteLine($"[space] ship {ship} came back into the bubble as construct {c.Id} at {ConstructPhysics.HoldShown(c.HoverY):0} m, {c.Vel.Length():0} m/s");
    }

    /// <summary>Host: every ship's construct back in the bubble (the map changed, or Sandbox stopped).</summary>
    private void SpaceLandAll(SpaceFlight f)
    {
        foreach (var c in Constructs.Bodies)
            if (c.SpaceShip != 0)
            {
                var i = f.Ships.IndexOfId(c.SpaceShip);
                if (i >= 0)
                    f.Ships.Remove(i);
                c.SpaceShip = 0;
                c.Wake();
            }
        SpaceLaunched = 0;
    }

    /// <summary>
    /// Host: a ship's construct at its place: the centre of mass where the ship is, its speed, turned by the driver's A and
    /// D about the local up and brought level over the ground below (the ship has no attitude of its own yet).
    /// </summary>
    private static void SpacePose(Construct c, Vector3 com, Vector3 vel, Vector3 centre, float dt)
    {
        var up = Vector3.Normalize(com - centre);
        var mine = Vector3.Transform(Vector3.UnitY, c.Rot);
        var turn = Quaternion.Identity;
        var axis = Vector3.Cross(mine, up);
        var s = axis.Length();
        if (s > 1e-6f)
        {
            var angle = MathF.Atan2(s, Vector3.Dot(mine, up));
            turn = Quaternion.CreateFromAxisAngle(axis / s, angle * MathF.Min(1, SpaceLevelRate * dt));
        }
        var steer = c.Driver == 255 ? 0f : ((c.DriveKeys & Buttons.Right) != 0 ? 1f : 0f) - ((c.DriveKeys & Buttons.Left) != 0 ? 1f : 0f);
        if (steer != 0)
            turn = Quaternion.CreateFromAxisAngle(up, -steer * SpaceYawRate * dt) * turn;
        var rot = Quaternion.Normalize(turn * c.Rot);
        // The spin that turn is, for clients carrying the pose on between packets.
        var dq = rot * Quaternion.Conjugate(c.Rot);
        if (dq.W < 0)
            dq = Quaternion.Negate(dq);
        var v = new Vector3(dq.X, dq.Y, dq.Z);
        var sv = v.Length();
        c.AngVel = sv > 1e-7f ? v / sv * (2 * MathF.Atan2(sv, dq.W) / dt) : Vector3.Zero;
        c.Rot = rot;
        c.Pos = com - Vector3.Transform(c.Com, rot);
        c.Vel = vel;
        c.Asleep = false;
    }

    /// <summary>
    /// The local player's seat as a render origin (Program.Render, Renderer.SetCameraAt): the construct's drawn position,
    /// exactly as ConstructRender places it, and the eye from there, worked out without the world's big numbers. False
    /// when the local player isn't seated.
    /// </summary>
    internal bool LocalSeatAnchor(out Vector3 origin, out Vector3 eyeFromOrigin)
    {
        origin = eyeFromOrigin = Vector3.Zero;
        if (!SandboxOn || Local is not { Alive: true, RenderSeat: not SeatKind.None } me)
            return false;
        ref var h = ref SandboxHands.Hand(me.Id);
        if (!h.Seated || Constructs.Get(h.SeatBody) is not { } c || h.SeatPart >= c.PartCount || c.Parts[h.SeatPart].D.Role != PartRole.Seat)
            return false;
        ConstructRender.SeatPoseFrom(c, h.SeatPart, _alpha, IsServer, out var q, out var feet, out origin);
        eyeFromOrigin = feet + Vector3.Transform(new Vector3(0, me.RenderEyeHeight, 0), q);
        return true;
    }

    /// <summary>The player sits in a construct that is in space (they can't get out there).</summary>
    internal bool SpaceSeated(Player p)
    {
        if (!SandboxOn)
            return false;
        ref var h = ref SandboxHands.Hand(p.Id);
        return h.Seated && Constructs.Get(h.SeatBody) is { SpaceShip: not 0 };
    }

    private SbText _sbOrbit;

    /// <summary>
    /// Any peer: the driver's orbit line on a planetoid in a star system, worked out from the construct's own pose and
    /// speed (so a client needs nothing new): height, speed round the body, and the lowest and highest points of the orbit
    /// it is on (its two-body conic round the body). Null when there's no bubble or it is low and slow.
    /// </summary>
    internal string? SpaceOrbitLine(Construct c)
    {
        if (Space is not { } s || World.Map.Planetoid is not { } pl || GravityFrames.Point == null)
            return null;
        var body = s.Bubble.Body;
        var t = SpaceRenderTime;
        var pose = s.Bubble.PoseAt(t);
        var com = Vec3d.From(c.WorldCom);
        s.System.StateOf(body, t, out var bp, out var bv);
        var r = pose.ToEcliptic(com) - bp;
        var v = pose.VelocityToEcliptic(com, Vec3d.From(c.Vel)) - bv;
        var mu = s.System.Bodies[body].Mu;
        var rad = (float)pl.Radius;
        var height = (float)r.Length - rad;
        if (height < 1000 && c.SpaceShip == 0)
            return null;
        var energy = v.LengthSquared / 2 - mu / r.Length;
        var h = Vec3d.Cross(r, v).Length;
        var e = Math.Sqrt(Math.Max(0, 1 + 2 * energy * h * h / (mu * mu)));
        var p = h * h / mu; // (the semi-latus rectum: periapsis p / (1 + e), apoapsis p / (1 - e))
        var peri = (float)(p / (1 + e)) - rad;
        var apo = e < 1 ? (float)(p / (1 - e)) - rad : float.PositiveInfinity;
        var edge = SpaceEdge;
        var key = (long)MathF.Round(height / 10) ^ ((long)MathF.Round((float)v.Length) << 20) ^ ((long)MathF.Round(peri / 10) << 34)
                  ^ (float.IsFinite(apo) ? (long)MathF.Round(apo / 10) << 48 : 1L << 62) ^ (c.SpaceShip != 0 ? 1L << 63 : 0);
        if (_sbOrbit.Stale(key))
        {
            var where = c.SpaceShip != 0 || edge > 0 && r.Length > edge ? $"In space round {s.System.Bodies[body].Name}" : "Climbing";
            var hi = float.IsFinite(apo) ? $"{apo / 1000:0.0} km" : "escaping";
            var low = peri < 0 ? "under the ground" : $"{peri / 1000:0.0} km";
            _sbOrbit.Text = $"{where}: {height / 1000:0.00} km up, {v.Length:0} m/s; orbit low {low}, high {hi}" + (where == "Climbing" ? $" (space starts {(edge - rad) / 1000:0.0} km up)" : "");
        }
        return _sbOrbit.Text;
    }
}
