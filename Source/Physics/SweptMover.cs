using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A swept capsule character controller (server setting physics=swept). Each tick it gathers the shapes around the player
/// once (boxes, oriented boxes, mesh and terrain triangles, movers at the command's tick) and then only does capsule casts
/// against that list:
/// <list type="bullet">
/// <item>sweep and slide: cast along the move, stop short of the contact, slide the rest along the surface (up to 4 times,
/// following creases between two surfaces), so it never tunnels, even at sprint speed into a thin wall;</item>
/// <item>step-up: blocked by a wall while walking, it tries up <see cref="StepUp"/>, forward, then down, and keeps
/// that if it lands on walkable ground further along;</item>
/// <item>ground snap: walking off a stair or down a slope keeps it on the ground (down to <see cref="SnapDown"/>);</item>
/// <item>slopes: ground whose normal is within <see cref="World.MaxSlopeDeg"/> of "up" is walkable; steeper ground is a wall
/// when walking and something you slide down otherwise. Terrain follows <see cref="SlopeRules"/> instead (the footprint's
/// slope, grip, slower uphill, capped and steerable slides), exactly like the classic mover;</item>
/// <item>stances: stand and crouch are capsules of radius <see cref="World.PlayerRadius"/>, 1.8 and 1.2 m tall; prone is a
/// 0.6 m ball of radius 0.3. Getting up needs room for the taller capsule;</item>
/// <item>mantling: the classic ledge search with capsule room checks, then the same glide;</item>
/// <item>movers: carried and given momentum like the classic mover (<see cref="MoverPhysics.BeginTick"/>, EndTick), pushed by
/// them through depenetration at the start of the tick, never crushed (it is lifted clear).</item>
/// </list>
/// "Up" and gravity come from <see cref="PhysicsWorld.Gravity"/> at the player's feet, not a fixed -Y: a later gravity system can
/// change them per zone. Deterministic: plain float maths on sorted shape lists, no state outside <see cref="MoveState"/>.
/// </summary>
public sealed class SweptMover : IMover
{
    public static readonly SweptMover Instance = new();

    public PhysicsMode Mode => PhysicsMode.Swept;

    /// <summary>How far down walking players are kept on the ground (stairs, slopes).</summary>
    public const float SnapDown = World.StepHeight;

    /// <summary>
    /// How far the step-up lifts the capsule. Its round bottom then rolls over about 0.1 m more, so the effective step is about
    /// 0.6 m: 0.55 m steps climb, 0.70 m steps block (the same as the classic mover for every step a map uses).
    /// </summary>
    public const float StepUp = World.StepHeight - 0.05f;

    /// <summary>The step-up's forward probe goes at least this far, so the capsule's round bottom gets onto the step.</summary>
    public const float MinStepForward = 0.2f;

    public const float ProneRadius = 0.3f;

    /// <summary>
    /// On terrain, the feet may be at most this far above the ground under their middle to count as standing (the capsule's
    /// round bottom keeps them up to about 0.17 m above a 45 degree slope).
    /// </summary>
    private const float EdgeGap = 0.25f;

    private const int SlideIterations = 4;
    private const float Skin = CapsuleContact.Skin;

    private readonly List<ContactShape> _shapes = new();
    private readonly Func<Vector3, Stance, bool> _roomForLedge;
    private Vector3 _up = Vector3.UnitY;
    private uint _tick;

    /// <summary>The terrain this tick, when its slope rules apply (gravity straight down); null otherwise.</summary>
    private ITerrainCollision? _terrain;

    /// <summary>Optional per-tick statistics for the self-test (casts made).</summary>
    public int Casts;

    private SweptMover()
    {
        _roomForLedge = (feet, stance) => !World.Physics.OverlapCapsule(CapsuleAt(feet, stance, _up), new QueryFilter(QueryLayers.Static | QueryLayers.Movers | QueryLayers.Boundary, _tick));
    }

    /// <summary>Capsule radius of a stance.</summary>
    public static float RadiusOf(Stance s) => s == Stance.Prone ? ProneRadius : World.PlayerRadius;

    /// <summary>The body capsule of a stance standing at <paramref name="feet"/> with the given up direction.</summary>
    public static Capsule CapsuleAt(Vector3 feet, Stance stance, Vector3 up)
    {
        var r = RadiusOf(stance);
        var h = World.HeightOf(stance);
        return new Capsule(feet + up * r, feet + up * MathF.Max(r, h - r), r);
    }

    private Span<ContactShape> Shapes => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_shapes);

    public void Simulate(ref MoveState s, in InputCmd cmd)
    {
        var map = World.Map;
        var phys = World.Physics;
        var b = cmd.Buttons;
        var movers = map.Movers.Count > 0;
        _tick = cmd.Tick != 0 ? cmd.Tick : MoverPhysics.Clock;
        var platformVel = Vector3.Zero;
        var carried = movers ? MoverPhysics.BeginTick(ref s, _tick, World.HeightOf(s.Stance), out platformVel) : -1;
        if (s.BoostTicks > 0)
            s.BoostTicks--;
        if (s.KnockTicks > 0)
            s.KnockTicks--; // a blast push's stagger (Knockback.cs): counted per input, even while mantling

        if (s.MantleTicks > 0)
        {
            ClassicMover.MantleGlide(ref s, b, cmd.Yaw);
            return;
        }

        // Up and gravity at the feet (one uniform field today).
        var g = phys.Gravity.GravityAt(s.Position);
        var gLen = g.Length();
        var up = gLen > 1e-6f ? -g / gLen : Vector3.UnitY;
        _up = up;
        _terrain = up == Vector3.UnitY ? phys.Terrain?.Layer(s.Position.Y) : null; // (in a cave: its floor)
        var dt = World.TickDt;

        // Everything the capsule can touch this tick, gathered once.
        var reach = s.Velocity.Length() * dt + World.StepHeight + SnapDown + MinStepForward + 0.5f;
        var lo = Vector3.Min(s.Position, s.Position + up * World.PlayerHeight) - new Vector3(reach);
        var hi = Vector3.Max(s.Position, s.Position + up * World.PlayerHeight) + new Vector3(reach);
        phys.Gather(lo, hi, new QueryFilter(QueryLayers.Static | QueryLayers.Movers | QueryLayers.Boundary, _tick), _shapes);

        // Pushed by movers (or spawned a hair inside something): get clear first.
        s.Position = Unstick(s.Position, s.Stance);

        // Stance: going lower is always allowed, getting up needs room for the taller capsule.
        var want = MoveInput.WantedStance(b, s.Grounded);
        if (want != s.Stance)
        {
            if (World.HeightOf(want) <= World.HeightOf(s.Stance) || TryFit(ref s.Position, want))
                s.Stance = want;
            else if (want == Stance.Stand && s.Stance == Stance.Prone && TryFit(ref s.Position, Stance.Crouch))
                s.Stance = Stance.Crouch;
        }

        // Steering: the same input response as the classic mover, in the plane across "up".
        // On a planet the keys are read in the local tangent frame (yaw is measured from its north, GravityFrames.cs).
        var wish = GravityFrames.Curved ? GravityFrames.ToWorld(s.Position, MoveInput.Wish(b, cmd.Yaw)) : Flatten(MoveInput.Wish(b, cmd.Yaw), up);
        var speed = MoveInput.Speed(s, b, cmd.SpeedPct);
        var control = s.KnockTicks > 0
            ? (s.Grounded ? Knockback.StaggerGroundControl : Knockback.StaggerAirControl) // staggered by a blast: the push carries you
            : s.Grounded ? 14f : 3f;
        var target = wish * speed;
        // On terrain (the feet just above it, as high as the capsule's round bottom rests on that slope): the slope rules'
        // uphill speed and slide steering.
        var onTerrain = false;
        if (_terrain != null)
        {
            var gap = s.Position.Y - _terrain.HeightAt(s.Position.X, s.Position.Z);
            onTerrain = gap < 1f && gap < World.PlayerRadius * (1f / SlopeRules.SupportNormal(_terrain, s.Position.X, s.Position.Z).Y - 1f) + 0.15f;
        }
        if (onTerrain)
        {
            float tx = target.X, tz = target.Z;
            ClassicMover.SlopeSteer(_terrain!, s, wish, speed, ref tx, ref tz, ref control);
            target = new Vector3(tx, 0, tz);
        }
        var blend = 1f - MathF.Exp(-control * dt);
        var vUp = Vector3.Dot(s.Velocity, up);
        var vFlat = s.Velocity - up * vUp;
        vFlat += (target - vFlat) * blend;
        s.Velocity = vFlat + up * vUp;

        if ((b & Buttons.Jump) != 0 && (b & Buttons.Forward) != 0 && s.Stance != Stance.Prone && !GravityFrames.Curved // (the ledge search is Y-up only)
            && ClassicMover.FindLedge(s, cmd.Yaw, out var ledgeStance, out var ledgeTarget, out var ledgeTop, _roomForLedge))
        {
            ClassicMover.StartMantle(ref s, ledgeStance, ledgeTarget, ledgeTop);
            return;
        }

        var jumped = false;
        if (s.Grounded && (b & Buttons.Jump) != 0 && s.Stance == Stance.Stand)
        {
            s.Velocity = vFlat + up * (World.JumpSpeed * (s.MovePerk == PerkId.Parkour ? Perks.ParkourJump : 1f));
            s.Grounded = false;
            jumped = true;
        }

        if (s.Grounded)
        {
            // Walking: no gravity builds up; move across the ground, step up, then stay on the ground.
            s.Velocity = vFlat;
            var disp = vFlat * dt;
            if (onTerrain && _terrain is { } hill)
            {
                // Along the slope (as the classic mover follows the ground): uphill progress isn't lost to the slide up the
                // surface, and walking downhill stays on it.
                var n = SlopeRules.SupportNormal(hill, s.Position.X, s.Position.Z);
                if (SlopeRules.Walkable(n))
                    disp.Y = -(n.X * disp.X + n.Z * disp.Z) / n.Y;
            }
            else if (GravityFrames.Curved)
                disp = AlongGround(s.Position, s.Stance, disp);
            s.Position = Walk(s.Position, disp, s.Stance, ref s.Velocity);
            s.Grounded = Snap(ref s.Position, s.Stance, SnapDown, s.Velocity, true);
        }
        else
        {
            s.Velocity += g * dt;
            s.Position = Fly(s.Position, s.Velocity * dt, s.Stance, ref s.Velocity, ref s.Grounded);
            if (onTerrain && !s.Grounded && _terrain is { } slope)
            {
                var n = SlopeRules.SupportNormal(slope, s.Position.X, s.Position.Z);
                if (!SlopeRules.Walkable(n))
                    SlopeRules.CapSlide(n, ref s.Velocity); // sliding: no faster than the cap
            }
            if (!s.Grounded && !jumped && Vector3.Dot(s.Velocity, up) <= 0)
            {
                // Resting on something walkable a hair below?
                if (Snap(ref s.Position, s.Stance, Skin * 2, s.Velocity, false))
                {
                    s.Grounded = true;
                    var down = Vector3.Dot(s.Velocity, up);
                    if (down < 0)
                        s.Velocity -= up * down;
                }
            }
        }

        s.Position = Clamp(phys, s.Position);
        if (movers)
            MoverPhysics.EndTick(ref s, _tick, carried, platformVel);
    }

    /// <summary>
    /// A planet: the walk turned into the plane of the ground under the feet (same length). "Across up" is the sphere's
    /// tangent plane, but the ground is flat triangles tilted a little from it; walking into the tilt would scrape the
    /// capsule along the faces and catch on the ridges between them, so the move follows the face it stands on instead.
    /// </summary>
    private Vector3 AlongGround(Vector3 feet, Stance stance, Vector3 disp)
    {
        var len = disp.Length();
        if (len < 1e-6f || !Cast(feet, stance, -_up, Skin * 4 + 0.02f, out _, out var n) || Vector3.Dot(n, _up) < World.MinWalkNormalY)
            return disp;
        var along = disp - n * Vector3.Dot(disp, n);
        var l = along.Length();
        return l > 1e-6f ? along * (len / l) : disp;
    }

    /// <summary>The vector without its component along up (zero stays zero).</summary>
    private static Vector3 Flatten(Vector3 v, Vector3 up)
    {
        if (up == Vector3.UnitY)
            return new Vector3(v.X, 0, v.Z);
        var f = v - up * Vector3.Dot(v, up);
        var len = f.Length();
        return len > 1e-6f ? f * (v.Length() / len) : Vector3.Zero;
    }

    private bool Fits(Vector3 feet, Stance stance)
    {
        var c = CapsuleAt(feet, stance, _up);
        return !CapsuleContact.Overlaps(Shapes, c.A, c.B, c.Radius, 1e-3f);
    }

    /// <summary>
    /// Room for a taller stance here, or a hair away (prone is narrower: getting up beside a wall nudges you off it by up to
    /// 12 cm across the ground)? Moves the feet if needed.
    /// </summary>
    private bool TryFit(ref Vector3 feet, Stance stance)
    {
        if (Fits(feet, stance))
            return true;
        var c = CapsuleAt(feet, stance, _up);
        var push = CapsuleContact.Depenetrate(Shapes, c.A, c.B, c.Radius, 3, out var clear);
        if (!clear || push.Length() > 0.12f || MathF.Abs(Vector3.Dot(push, _up)) > 0.01f || !Fits(feet + push, stance))
            return false;
        feet += push;
        return true;
    }

    private bool Cast(Vector3 feet, Stance stance, Vector3 dir, float dist, out float toi, out Vector3 normal) =>
        Cast(feet, stance, dir, dist, out toi, out normal, out _, out _);

    private bool Cast(Vector3 feet, Stance stance, Vector3 dir, float dist, out float toi, out Vector3 normal, out Vector3 point, out int index)
    {
        Casts++;
        var c = CapsuleAt(feet, stance, _up);
        return CapsuleContact.Cast(Shapes, c.A, c.B, c.Radius, dir, dist, out toi, out normal, out point, out index);
    }

    /// <summary>
    /// Can the player stand on what a cast touched (normal <paramref name="n"/>, shape <paramref name="index"/>)? Terrain by
    /// <see cref="SlopeRules"/> where the feet are (<paramref name="point"/>: the feet's middle, like the classic mover's
    /// body, not the capsule's contact point, so a bank's rim doesn't count as walkable from below), with grip along
    /// <paramref name="dir"/> when <paramref name="grip"/>; everything else by <see cref="World.MinWalkNormalY"/>.
    /// </summary>
    private bool WalkableHit(Vector3 n, int index, Vector3 point, Vector3 dir, bool grip)
    {
        if (_terrain is { } t && index >= 0 && Shapes[index].Id.Kind == ShapeKind.Terrain)
        {
            // Resting on a bank's edge with the feet out over lower ground isn't standing on it.
            if (point.Y - t.HeightAt(point.X, point.Z) > EdgeGap)
                return false;
            return grip ? SlopeRules.CanStand(t, point.X, point.Z, dir.X, dir.Z) : SlopeRules.Walkable(t, point.X, point.Z);
        }
        return Vector3.Dot(n, _up) >= World.MinWalkNormalY;
    }

    /// <summary>Out of anything the capsule overlaps; if squeezed, lifted until it fits (up to 4 m), like the classic mover's Unstick.</summary>
    private Vector3 Unstick(Vector3 feet, Stance stance)
    {
        var c = CapsuleAt(feet, stance, _up);
        if (!CapsuleContact.Overlaps(Shapes, c.A, c.B, c.Radius, 1e-3f))
            return feet;
        var push = CapsuleContact.Depenetrate(Shapes, c.A, c.B, c.Radius, 4, out var clear);
        if (clear)
            return feet + push;
        for (var lift = 0.25f; lift <= 4f; lift += 0.25f)
            if (Fits(feet + _up * lift, stance))
                return feet + _up * lift;
        return feet + push;
    }

    /// <summary>
    /// Moves across the ground by <paramref name="disp"/> (across "up"): sweep and slide; if a wall (or too steep a slope)
    /// stopped it, try stepping up onto it. Walkable slopes are followed (the slide turns the move up or down them).
    /// </summary>
    private Vector3 Walk(Vector3 feet, Vector3 disp, Stance stance, ref Vector3 velocity)
    {
        var slid = Slide(feet, disp, stance, true, ref velocity, out var wall);
        if (wall > -0.2f || disp.LengthSquared() < 1e-12f)
            return slid; // no wall, or only brushing along one

        // Step up: as high as the step height allows (less under a ceiling), forward, then back down onto the step.
        var up = _up;
        var rise = StepUp;
        if (Cast(feet, stance, up, rise, out var t, out _))
            rise = t;
        if (rise < 0.05f)
            return slid;
        var raised = feet + up * rise;
        var len = disp.Length();
        var dir = disp / len;
        var fwd = MathF.Max(len, MinStepForward);
        var stepVel = velocity;
        var ahead = Slide(raised, dir * fwd, stance, true, ref stepVel, out _);
        if (!Cast(ahead, stance, -up, rise + World.StepHeight, out var down, out var n, out _, out var hit))
            return slid;
        var landed = ahead - up * down;
        if (!WalkableHit(n, hit, landed, dir, true))
            return slid;
        // Only worth it if it got further along than the plain slide.
        if (Vector3.Dot(landed - feet, dir) <= Vector3.Dot(slid - feet, dir) + 1e-3f)
            return slid;
        velocity = stepVel;
        return landed;
    }

    /// <summary>In the air: sweep and slide the full move; landing on walkable ground ends the fall.</summary>
    private Vector3 Fly(Vector3 feet, Vector3 disp, Stance stance, ref Vector3 velocity, ref bool grounded)
    {
        var up = _up;
        Span<Vector3> planes = stackalloc Vector3[SlideIterations];
        var count = 0;
        for (var i = 0; i < SlideIterations; i++)
        {
            var len = disp.Length();
            if (len < 1e-6f)
                break;
            var dir = disp / len;
            if (!Cast(feet, stance, dir, len, out var toi, out var n, out _, out var hit))
            {
                feet += disp;
                break;
            }
            feet += dir * toi;
            disp = dir * (len - toi);
            if (WalkableHit(n, hit, feet, velocity, false) && Vector3.Dot(velocity, up) <= 0)
            {
                // Landed: the fall stops, the rest of the move carries on along the ground.
                grounded = true;
                velocity -= up * Vector3.Dot(velocity, up);
            }
            velocity = Clip(velocity, n);
            disp = ClipMove(disp, n, planes, count);
            if (count < planes.Length)
                planes[count++] = n;
        }
        return feet;
    }

    /// <summary>
    /// Sweep and slide along the ground plane. A surface steeper than the slope limit (a wall) counts as vertical, so sliding
    /// along it never lifts the player; walkable surfaces are slid along as they are (walking up a ramp).
    /// </summary>
    /// <param name="wall">How squarely the move ran into a wall: the lowest dot of the move direction with a wall's normal (0 = none).</param>
    private Vector3 Slide(Vector3 feet, Vector3 disp, Stance stance, bool walking, ref Vector3 velocity, out float wall)
    {
        wall = 0;
        var up = _up;
        Span<Vector3> planes = stackalloc Vector3[SlideIterations];
        var count = 0;
        for (var i = 0; i < SlideIterations; i++)
        {
            var len = disp.Length();
            if (len < 1e-6f)
                break;
            var dir = disp / len;
            if (!Cast(feet, stance, dir, len, out var toi, out var n, out _, out var hit))
            {
                feet += disp;
                break;
            }
            feet += dir * toi;
            disp = dir * (len - toi);
            var ny = Vector3.Dot(n, up);
            if (walking && ny < World.MinWalkNormalY && !WalkableHit(n, hit, feet + dir * 0.1f, dir, true))
            {
                // Walls and too-steep slopes: slide along them horizontally (a ceiling edge: straight back).
                var flat = n - up * ny;
                var fl = flat.Length();
                n = fl > 1e-4f ? flat / fl : -dir;
                wall = MathF.Min(wall, Vector3.Dot(dir, n));
                velocity = Clip(velocity, n);
            }
            disp = ClipMove(disp, n, planes, count);
            if (count < planes.Length)
                planes[count++] = n;
        }
        return feet;
    }

    /// <summary>Removes the part of a velocity going into a surface.</summary>
    private static Vector3 Clip(Vector3 v, Vector3 n)
    {
        var into = Vector3.Dot(v, n);
        return into < 0 ? v - n * into : v;
    }

    /// <summary>The rest of a move after touching a surface: along it, and along the crease if it also runs into an earlier one.</summary>
    private static Vector3 ClipMove(Vector3 disp, Vector3 n, ReadOnlySpan<Vector3> planes, int count)
    {
        var d = Clip(disp, n);
        for (var k = 0; k < count; k++)
        {
            var p = planes[k];
            if (Vector3.Dot(d, p) >= -1e-6f || Vector3.Dot(p, n) > 0.999f)
                continue;
            // Into an earlier surface too: only the crease between the two is free.
            var crease = Vector3.Cross(p, n);
            var cl = crease.Length();
            if (cl < 1e-5f)
                return Vector3.Zero;
            crease /= cl;
            d = crease * Vector3.Dot(disp, crease);
            for (var j = 0; j < count; j++)
                if (j != k && Vector3.Dot(d, planes[j]) < -1e-6f)
                    return Vector3.Zero; // a corner: stuck this tick
            return d;
        }
        return d;
    }

    /// <summary>
    /// Keeps a walking player on the ground: casts down up to <paramref name="dist"/>; on walkable ground it moves there and
    /// returns true. Nothing walkable below: the player is falling (or sliding). On terrain, <paramref name="grip"/> lets a
    /// player moving along <paramref name="moveDir"/> keep walking over a narrow steep patch (<see cref="SlopeRules.Grip"/>).
    /// </summary>
    private bool Snap(ref Vector3 feet, Stance stance, float dist, Vector3 moveDir, bool grip)
    {
        if (!Cast(feet, stance, -_up, dist, out var t, out var n, out _, out var hit))
            return false;
        if (WalkableHit(n, hit, feet - _up * t, moveDir, grip))
        {
            feet -= _up * t;
            return true;
        }
        // Resting on an edge (walking off a stair) or a steep bit: slide the rest of the way down along it, then look for
        // walkable ground once more, never further down than dist in all.
        var p = feet - _up * t;
        var rest = dist - t;
        var along = -_up - n * Vector3.Dot(-_up, n);
        var len = along.Length();
        if (rest < 0.02f || len < 0.1f)
            return false;
        along /= len;
        var step = rest;
        if (Cast(p, stance, along, step, out var t2, out _))
            step = t2;
        var q = p + along * step;
        var left = dist - Vector3.Dot(feet - q, _up);
        if (left < 0 || !Cast(q, stance, -_up, left + Skin, out var t3, out var n3, out _, out var hit3) || !WalkableHit(n3, hit3, q - _up * t3, moveDir, grip))
            return false;
        feet = q - _up * t3;
        return true;
    }

    /// <summary>The arena bounds, and never far under the terrain (a safety net; casts keep players on top of it).</summary>
    private static Vector3 Clamp(PhysicsWorld phys, Vector3 feet)
    {
        if (phys.Gravity is PointGravity planet)
            return World.ClampToSky(feet, planet); // a planet: only a ceiling far above (the ground holds players up)
        feet.X = Math.Clamp(feet.X, -phys.HalfX + World.PlayerRadius, phys.HalfX - World.PlayerRadius);
        feet.Z = Math.Clamp(feet.Z, -phys.HalfZ + World.PlayerRadius, phys.HalfZ - World.PlayerRadius);
        if (phys.Map?.Boundary is { } boundary)
            feet = boundary.ClampFeet(feet, World.PlayerRadius); // the barrier stops the capsule; this is the safety net
        if (phys.Terrain is { } terrain)
        {
            feet.Y = Math.Clamp(feet.Y, terrain.Data.MinHeight - 5, terrain.Data.MaxHeight + 60);
            feet.Y = MathF.Max(feet.Y, terrain.GroundBelow(feet.X, feet.Z, feet.Y) - 0.3f);
        }
        else
            feet.Y = Math.Clamp(feet.Y, -5, 60);
        return feet;
    }
}
