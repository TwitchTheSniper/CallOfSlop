using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Player movement for one tick: <see cref="World.Simulate"/> hands every command to the current mover. A mover must be
/// deterministic (client prediction replays it) and keep everything it needs in <see cref="MoveState"/> (snapshots carry it).
/// </summary>
public interface IMover
{
    PhysicsMode Mode { get; }

    /// <summary>Advances one player by exactly one tick of input.</summary>
    void Simulate(ref MoveState s, in InputCmd cmd);
}

/// <summary>
/// The original movement: a box-shaped body moved one axis at a time against the map's boxes (land on anything, bump heads,
/// slide along walls, walk up steps of up to <see cref="World.StepHeight"/>), the terrain under the feet with the slope rules of
/// <see cref="SlopeRules"/> (since protocol 14), mantling onto ledges and the movers. Ported onto <see cref="PhysicsWorld"/>:
/// the boxes come from its BVH in map order, so on maps without terrain every answer is bit-identical to the old full scans
/// (the "Collision parity" self-test checks it; on terrain maps it checks the queries, and "Slopes" checks the movement).
/// </summary>
public sealed class ClassicMover : IMover
{
    public static readonly ClassicMover Instance = new();

    public PhysicsMode Mode => PhysicsMode.Classic;

    /// <summary>The mover tick of the command being simulated.</summary>
    private uint _moverTick;

    private static readonly Stance[] CrouchOnly = [Stance.Crouch];
    private static readonly Stance[] StandOrCrouch = [Stance.Stand, Stance.Crouch];

    // Candidate lists are copied out of the physics world's shared buffer (MoveAxis calls Blocked, which doesn't use it,
    // but the copy keeps each loop independent of any query made inside it).
    private int[] _near = new int[64];

    public void Simulate(ref MoveState s, in InputCmd cmd)
    {
        var map = World.Map;
        var b = cmd.Buttons;
        // Movers (moving platforms): posed at the command's tick, so prediction, replay and the server agree.
        var movers = map.Movers.Count > 0;
        _moverTick = cmd.Tick != 0 ? cmd.Tick : MoverPhysics.Clock;
        var platformVel = Vector3.Zero;
        var carried = movers ? MoverPhysics.BeginTick(ref s, _moverTick, World.HeightOf(s.Stance), out platformVel) : -1;
        if (s.BoostTicks > 0)
            s.BoostTicks--;
        if (s.KnockTicks > 0)
            s.KnockTicks--; // a blast push's stagger (Knockback.cs): counted per input, even while mantling

        // Mid-mantle: glide to the ledge, no other control. Memoryless (only ticks left + target),
        // so the server can send it in snapshots and prediction replays it exactly.
        if (s.MantleTicks > 0)
        {
            MantleGlide(ref s, b, cmd.Yaw);
            return;
        }

        var wish = MoveInput.Wish(b, cmd.Yaw);

        // Stance: going lower is always allowed, getting up needs headroom (no standing up under a table).
        var want = MoveInput.WantedStance(b, s.Grounded);
        if (want != s.Stance)
        {
            if (World.HeightOf(want) <= World.HeightOf(s.Stance) || !MoverPhysics.BlockedAll(s.Position, World.HeightOf(want), _moverTick))
                s.Stance = want;
            else if (want == Stance.Stand && s.Stance == Stance.Prone && !MoverPhysics.BlockedAll(s.Position, World.HeightOf(Stance.Crouch), _moverTick))
                s.Stance = Stance.Crouch; // only room to get halfway up
        }

        var speed = MoveInput.Speed(s, b, cmd.SpeedPct);
        var control = s.KnockTicks > 0
            ? (s.Grounded ? Knockback.StaggerGroundControl : Knockback.StaggerAirControl) // staggered by a blast: the push carries you
            : s.Grounded ? 14f : 3f; // snappy on the ground, floaty in the air
        var targetX = wish.X * speed;
        var targetZ = wish.Z * speed;
        if (map.TerrainCollision?.Layer(s.Position.Y) is { } ground && s.Position.Y - ground.HeightAt(s.Position.X, s.Position.Z) < SlopeRules.OnGround)
            SlopeSteer(ground, s, wish, speed, ref targetX, ref targetZ, ref control);
        var blend = 1f - MathF.Exp(-control * World.TickDt);
        s.Velocity.X += (targetX - s.Velocity.X) * blend;
        s.Velocity.Z += (targetZ - s.Velocity.Z) * blend;

        // Jump facing a ledge or window sill (while moving forward) = mantle instead of jumping.
        if ((b & Buttons.Jump) != 0 && (b & Buttons.Forward) != 0 && s.Stance != Stance.Prone && TryMantle(ref s, cmd.Yaw))
            return;

        if (s.Grounded && (b & Buttons.Jump) != 0 && s.Stance == Stance.Stand)
            s.Velocity.Y = World.JumpSpeed * (s.MovePerk == PerkId.Parkour ? Perks.ParkourJump : 1f);

        s.Position = Move(s.Position, ref s.Velocity, ref s.Grounded, World.TickDt, World.HeightOf(s.Stance));
        if (movers)
            MoverPhysics.EndTick(ref s, _moverTick, carried, platformVel);
    }

    /// <summary>
    /// On terrain (<see cref="SlopeRules"/>): walking up a slope is slower, and sliding down ground too steep to stand on
    /// steers across and down it (never up). Shared with the swept mover.
    /// </summary>
    internal static void SlopeSteer(ITerrainCollision ground, in MoveState s, Vector3 wish, float speed, ref float targetX, ref float targetZ, ref float control)
    {
        var n = SlopeRules.SupportNormal(ground, s.Position.X, s.Position.Z);
        if (s.Grounded)
        {
            var f = SlopeRules.SpeedFactor(n, wish);
            targetX *= f;
            targetZ *= f;
        }
        else if (!SlopeRules.Walkable(n))
        {
            var t = SlopeRules.SlideTarget(n, wish, speed, s.Velocity);
            targetX = t.X;
            targetZ = t.Y;
            control = SlopeRules.SlideControl;
        }
    }

    /// <summary>One tick of a mantle in progress (shared with the swept mover: the glide is the same for both).</summary>
    internal static void MantleGlide(ref MoveState s, Buttons b, float yaw)
    {
        s.Position += (s.MantleTo - s.Position) / s.MantleTicks;
        if (World.Map.TerrainCollision is { } ground)
            s.Position.Y = MathF.Max(s.Position.Y, ground.GroundBelow(s.Position.X, s.Position.Z, s.Position.Y)); // don't cut through a bank's edge
        s.MantleTicks--;
        s.Velocity = Vector3.Zero;
        if (s.MantleTicks == 0)
        {
            s.Position = s.MantleTo;
            s.Grounded = true;
            if ((b & Buttons.Forward) != 0)
                s.Velocity = World.FlatForward(yaw) * 3f; // keep going: off the sill and through the window
        }
    }

    /// <summary>
    /// Looks for a ledge just in front (crate top, wall top, window sill) between step height and
    /// <see cref="World.MantleReach"/> with room to stand or crouch on it. Starts the climb if found.
    /// Windows are 1.3 m tall, so going through one puts you in a crouch on the sill.
    /// </summary>
    private bool TryMantle(ref MoveState s, float yaw)
    {
        var ledge = FindLedge(s, yaw, out var stance, out var target, out var top);
        if (!ledge)
            return false;
        StartMantle(ref s, stance, target, top);
        return true;
    }

    internal static void StartMantle(ref MoveState s, Stance stance, Vector3 target, float top)
    {
        var parkour = s.MovePerk == PerkId.Parkour;
        var feet = s.Position;
        s.Stance = stance;
        s.MantleTo = target;
        var ticks = Math.Clamp((int)(8 + (top - feet.Y) * 9), 10, 28);
        s.MantleTicks = (byte)(parkour ? Math.Max(6, ticks / 2) : ticks);
        s.Velocity = Vector3.Zero;
        s.Grounded = false;
    }

    /// <summary>
    /// The ledge search of <see cref="TryMantle"/>. <paramref name="room"/> says whether a body of a stance fits at a spot;
    /// null = the classic body check (<see cref="World.Blocked"/>).
    /// </summary>
    internal static bool FindLedge(in MoveState s, float yaw, out Stance stance, out Vector3 target, out float ledgeTop, Func<Vector3, Stance, bool>? room = null)
    {
        stance = s.Stance;
        target = default;
        ledgeTop = 0;
        var f = World.FlatForward(yaw);
        var right = World.FlatRight(yaw);
        var feet = s.Position;
        var phys = World.Physics;
        var boxes = phys.Boxes;
        var parkour = s.MovePerk == PerkId.Parkour;
        var maxReach = parkour ? Perks.ParkourReach : World.MantleReach;
        var terrain = phys.Terrain?.Layer(feet.Y); // (in a cave: its floor, not the surface over it)
        // Terrain banks are a cell wide or more, so on terrain maps also look a little further for the top.
        ReadOnlySpan<float> reach = terrain == null ? [0.55f, 0.8f, 1.05f] : [0.55f, 0.8f, 1.05f, 1.35f, 1.65f];
        ReadOnlySpan<float> side = [0f, 0.15f, -0.15f, 0.3f, -0.3f]; // small sideways nudge to line up with windows

        foreach (var d in reach)
        {
            var probe = feet + f * d;
            // Highest box top under the probe point that's a climbable height.
            var top = float.NegativeInfinity;
            if (terrain != null)
            {
                // A terrain ledge counts if its top is flat enough to stand on (so you can't mantle up a cliff face).
                var g = terrain.HeightAt(probe.X, probe.Z);
                if (g > feet.Y + World.StepHeight && g <= feet.Y + maxReach
                    && (terrain.NormalAt(probe.X, probe.Z).Y >= World.MinWalkNormalY || SlopeRules.Walkable(terrain, probe.X, probe.Z)))
                    top = g;
            }
            if (d <= 1.05f)
            {
                // The highest top wins, so the order the boxes are looked at doesn't matter.
                foreach (var i in phys.CollectBoxes(probe.X, probe.Z, probe.X, probe.Z))
                    top = MantleTop(boxes[i], probe, feet, maxReach, top);
            }
            if (float.IsNegativeInfinity(top))
                continue;

            foreach (var n in side)
            {
                var spot = World.ClampToArena(new Vector3(probe.X, top, probe.Z) + right * n);
                foreach (var st in s.Stance == Stance.Crouch ? CrouchOnly : StandOrCrouch)
                {
                    // Room at the end, room to rise in place, and room halfway across.
                    var rise = feet with { Y = top + 0.01f };
                    var half = Vector3.Lerp(feet, spot, 0.5f) with { Y = top + 0.01f };
                    if (room == null)
                    {
                        var h = World.HeightOf(st);
                        if (World.Blocked(spot, h) || World.Blocked(rise, h) || World.Blocked(half, h))
                            continue;
                    }
                    else if (!room(spot, st) || !room(rise, st) || !room(half, st))
                        continue;
                    stance = st;
                    target = spot;
                    ledgeTop = top;
                    return true;
                }
            }
        }
        return false;
    }

    private static float MantleTop(in Box bx, Vector3 probe, Vector3 feet, float maxReach, float top)
    {
        if (probe.X < bx.Min.X || probe.X > bx.Max.X || probe.Z < bx.Min.Z || probe.Z > bx.Max.Z)
            return top;
        var t = bx.Max.Y;
        return t > feet.Y + World.StepHeight && t <= feet.Y + maxReach && t > top ? t : top;
    }

    private ReadOnlySpan<int> Near(PhysicsWorld phys, float x0, float z0, float x1, float z1)
    {
        var src = phys.CollectBoxes(x0, z0, x1, z1);
        if (_near.Length < src.Length)
            _near = new int[Math.Max(src.Length, _near.Length * 2)];
        src.CopyTo(_near);
        return _near.AsSpan(0, src.Length);
    }

    /// <summary>
    /// Moves a player's feet by velocity * dt against every box on the map, one axis at a time:
    /// land on anything, bump heads on ceilings, slide along walls and walk up steps of up to
    /// <see cref="World.StepHeight"/>. Deterministic, so prediction and the server always agree.
    /// </summary>
    public Vector3 Move(Vector3 feet, ref Vector3 velocity, ref bool grounded, float dt, float height = World.PlayerHeight)
    {
        var phys = World.Physics;
        var boxes = phys.Boxes;
        var H = height;
        var wasGrounded = grounded;
        const float r = World.PlayerRadius;

        // ---- Vertical ----
        velocity.Y -= World.Gravity * dt;
        var prevY = feet.Y;
        var newY = feet.Y + velocity.Y * dt;
        grounded = false;
        // The boxes under and over the player, in map order.
        foreach (var k in Near(phys, feet.X - r, feet.Z - r, feet.X + r, feet.Z + r))
        {
            var b = boxes[k];
            if (!OverlapsXZ(feet, b))
                continue;
            if (velocity.Y <= 0 && prevY >= b.Max.Y - 0.02f && newY < b.Max.Y)
            {
                newY = b.Max.Y; // landed
                velocity.Y = 0;
                grounded = true;
            }
            else if (velocity.Y > 0 && prevY + H <= b.Min.Y + 0.02f && newY + H > b.Min.Y)
            {
                newY = b.Min.Y - H; // head hit a ceiling
                velocity.Y = 0;
            }
        }
        if (World.Map.Movers.Count > 0)
            MoverPhysics.Vertical(feet, prevY, ref newY, ref velocity, ref grounded, H, _moverTick);
        feet.Y = newY;
        if (phys.Terrain is { } terrain)
        {
            TerrainVertical(terrain.Layer(feet.Y), ref feet, ref velocity, ref grounded, wasGrounded, H, dt);
            if (terrain.HasVoids)
            {
                // A cave's ceiling: bump the head on it.
                var ceiling = terrain.CeilingAbove(feet.X, feet.Z, feet.Y);
                if (feet.Y + H > ceiling)
                {
                    feet.Y = MathF.Max(ceiling - H, terrain.GroundBelow(feet.X, feet.Z, feet.Y));
                    if (velocity.Y > 0)
                        velocity.Y = 0;
                }
            }
        }

        // ---- Horizontal, X then Z ----
        feet = MoveAxis(phys, feet, velocity.X * dt, true, ref velocity, grounded, H);
        feet = MoveAxis(phys, feet, velocity.Z * dt, false, ref velocity, grounded, H);
        if (World.Map.Movers.Count > 0)
            feet = MoverPhysics.PushOut(feet, ref velocity, ref grounded, H, _moverTick); // pushed by (or stepping onto) moving platforms

        return World.ClampToArena(feet);
    }

    private Vector3 MoveAxis(PhysicsWorld phys, Vector3 feet, float delta, bool alongX, ref Vector3 velocity, bool grounded, float height)
    {
        if (delta == 0)
            return feet;
        var boxes = phys.Boxes;
        var p = feet;
        if (alongX)
            p.X += delta;
        else
            p.Z += delta;

        // Only boxes that reach the strip swept by this move can stop it, so walk the short list of those, in map order. If
        // a wall pushes us back out of the strip (we started inside a box), carry on with every box after the current one,
        // exactly as a scan over the whole map would.
        const float r = World.PlayerRadius;
        float x0 = MathF.Min(feet.X, p.X) - r, x1 = MathF.Max(feet.X, p.X) + r;
        float z0 = MathF.Min(feet.Z, p.Z) - r, z1 = MathF.Max(feet.Z, p.Z) + r;
        var near = Near(phys, x0, z0, x1, z1);
        var all = false;
        var k = 0;
        var i = -1;
        while (true)
        {
            if (all)
            {
                if (++i >= boxes.Length)
                    break;
            }
            else
            {
                if (k >= near.Length)
                    break;
                i = near[k++];
            }
            var b = boxes[i];
            if (p.Y >= b.Max.Y - 0.001f || p.Y + height <= b.Min.Y + 0.001f || !OverlapsXZ(p, b))
                continue;

            // A step? Climb it if there's room for our head up there.
            var rise = b.Max.Y - p.Y;
            if (grounded && rise <= World.StepHeight && !World.Blocked(p with { Y = b.Max.Y }, height))
            {
                p.Y = b.Max.Y;
                continue;
            }

            // A wall: stop at its face.
            if (alongX)
            {
                p.X = delta > 0 ? b.Min.X - r - 0.001f : b.Max.X + r + 0.001f;
                velocity.X = 0;
            }
            else
            {
                p.Z = delta > 0 ? b.Min.Z - r - 0.001f : b.Max.Z + r + 0.001f;
                velocity.Z = 0;
            }
            if (!all && (p.X - r < x0 || p.X + r > x1 || p.Z - r < z0 || p.Z + r > z1))
                all = true; // out of the strip: every remaining box from here on
        }

        // Terrain rising in front: walk up it if it's walkable or a lip within a step (and there's headroom), otherwise it's
        // a wall (SlopeRules).
        if (phys.Terrain?.Layer(feet.Y) is { } terrain)
        {
            var g = terrain.HeightAt(p.X, p.Z);
            if (g > p.Y + 0.001f)
            {
                if (g - p.Y <= World.StepHeight && SlopeRules.CanStand(terrain, p.X, p.Z, velocity.X, velocity.Z) && !World.BlockedByBoxes(p with { Y = g }, height))
                    p.Y = g;
                else
                {
                    p = feet; // no progress along this axis this tick
                    if (alongX)
                        velocity.X = 0;
                    else
                        velocity.Z = 0;
                }
            }
            // A cave's ceiling coming down lower than the head (where it meets the walls): a wall too. Only when it's lower
            // than where we stand, so nobody gets stuck under one.
            if (terrain.HasVoids && p != feet)
            {
                var ceiling = terrain.CeilingAbove(p.X, p.Z, p.Y);
                if (p.Y + height > ceiling && ceiling < terrain.CeilingAbove(feet.X, feet.Z, feet.Y) - 0.001f)
                {
                    p = feet;
                    if (alongX)
                        velocity.X = 0;
                    else
                        velocity.Z = 0;
                }
            }
        }
        return p;
    }

    /// <summary>
    /// The ground under a player after the vertical move: land on it, stick to it walking over crests and downhill (so you
    /// don't hop down slopes), and slide off anything too steep to stand on (<see cref="SlopeRules"/>: the footprint's slope,
    /// with grip while walking; the slide is capped).
    /// </summary>
    private static void TerrainVertical(ITerrainCollision terrain, ref Vector3 feet, ref Vector3 velocity, ref bool grounded, bool wasGrounded, float height, float dt)
    {
        var g = terrain.HeightAt(feet.X, feet.Z);
        var gap = feet.Y - g;
        if (gap > 0)
        {
            // In the air above the ground: snap down only when we were walking on it last tick and it's walkable, or when
            // sliding down ground too steep to stand on (so the slide stays on the slope and gravity keeps pulling along it).
            var speed = MathF.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
            if (grounded || velocity.Y > 0 || World.BlockedByBoxes(feet with { Y = g }, height))
                return;
            if (wasGrounded && gap <= 0.12f + speed * dt * 1.2f)
            {
                if (!SlopeRules.CanStand(terrain, feet.X, feet.Z, velocity.X, velocity.Z))
                    return;
            }
            else if (gap > 0.12f + speed * dt * 2f || SlopeRules.Walkable(terrain, feet.X, feet.Z))
                return;
        }
        var n = SlopeRules.SupportNormal(terrain, feet.X, feet.Z);
        feet.Y = g;
        if (velocity.Y < 0)
            velocity.Y = 0;
        if (SlopeRules.Walkable(n) || (wasGrounded && SlopeRules.Grip(terrain, feet.X, feet.Z, velocity.X, velocity.Z)))
            grounded = true;
        else if (!grounded)
        {
            // Too steep to stand on: gravity pulls you down the slope (no faster than the slide cap).
            var push = World.Gravity * n.Y * dt;
            velocity.X += n.X * push;
            velocity.Z += n.Z * push;
            SlopeRules.CapSlide(n, ref velocity);
        }
    }

    private static bool OverlapsXZ(Vector3 feet, Box box) =>
        feet.X + World.PlayerRadius > box.Min.X
        && feet.X - World.PlayerRadius < box.Max.X
        && feet.Z + World.PlayerRadius > box.Min.Z
        && feet.Z - World.PlayerRadius < box.Max.Z;
}

/// <summary>The input side of movement, the same for every mover: wish direction, wanted stance, speed.</summary>
public static class MoveInput
{
    /// <summary>The unit direction the keys ask for in the ground plane (zero when no key or opposite keys).</summary>
    public static Vector3 Wish(Buttons b, float yaw)
    {
        var wish = Vector3.Zero;
        if ((b & Buttons.Forward) != 0) wish += World.FlatForward(yaw);
        if ((b & Buttons.Back) != 0) wish -= World.FlatForward(yaw);
        if ((b & Buttons.Right) != 0) wish += World.FlatRight(yaw);
        if ((b & Buttons.Left) != 0) wish -= World.FlatRight(yaw);
        if (wish.LengthSquared() > 0)
            wish = Vector3.Normalize(wish);
        return wish;
    }

    /// <summary>The stance the keys ask for (prone needs the ground under you).</summary>
    public static Stance WantedStance(Buttons b, bool grounded)
    {
        var want = (b & Buttons.Prone) != 0 ? Stance.Prone : (b & Buttons.Crouch) != 0 ? Stance.Crouch : Stance.Stand;
        if (want == Stance.Prone && !grounded)
            want = Stance.Crouch; // can't go prone in mid-air
        return want;
    }

    /// <summary>Target ground speed: sprint or walk, stance, weapon mobility, perks.</summary>
    public static float Speed(in MoveState s, Buttons b, byte speedPct)
    {
        var sprint = (b & Buttons.Sprint) != 0 && s.Stance == Stance.Stand;
        var speed = (sprint ? World.SprintSpeed : World.WalkSpeed) * World.SpeedOf(s.Stance) * (speedPct == 0 ? 1f : speedPct / 100f);
        if (s.MovePerk == PerkId.LowProfile && s.Stance != Stance.Stand)
            speed *= Perks.LowProfileSpeed;
        if (s.BoostTicks > 0)
            speed *= Perks.BoostSpeed;
        return speed;
    }
}
