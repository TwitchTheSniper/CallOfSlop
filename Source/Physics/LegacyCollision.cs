using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// THE OLD COLLISION CODE, frozen: World.cs's movement, ray casts and body checks exactly as they were before the physics
/// module (full scans over every box, or the 4 m collision grid). Only the self-test uses it, as the reference the
/// "Collision parity" section compares <see cref="ClassicMover"/> and <see cref="PhysicsWorld"/> against. Don't change it;
/// delete it together with <see cref="CollisionGrid"/> once nobody needs the comparison any more.
/// </summary>
public static class LegacyCollision
{
    /// <summary>True: the collision grid (as the game ran); false: scan every box.</summary>
    public static bool UseGrid = true;

    private static CollisionGrid? _grid;

    /// <summary>The old collision grid of the current map.</summary>
    public static CollisionGrid Grid => _grid is { } g && g.IsFor(World.Map) ? g : _grid = new CollisionGrid(World.Map);

    internal static void SolidChanged(Map map, int index)
    {
        if (_grid is { } g && g.IsFor(map))
            g.ReplaceShrunkBox(index, map.Solid[index]);
    }

    internal static void Invalidate(Map map)
    {
        if (_grid is { } g && g.IsFor(map))
            _grid = null;
    }

    private static bool BlockedAll(Vector3 feet, float height, double tick) =>
        Blocked(feet, height) || (World.Map.Movers.Count > 0 && MoverPhysics.Overlaps(feet, height, tick));

    /// <summary>The old projectile test (boxes only, then movers at the clock).</summary>
    public static bool PointInside(Vector3 p, float r)
    {
        if (UseGrid)
        {
            if (Grid.PointInside(p, r))
                return true;
        }
        else
        {
            foreach (var b in World.Map.Solid)
                if (p.X > b.Min.X - r && p.X < b.Max.X + r && p.Y > b.Min.Y - r && p.Y < b.Max.Y + r && p.Z > b.Min.Z - r && p.Z < b.Max.Z + r)
                    return true;
        }
        return World.Map.Movers.Count > 0 && MoverPhysics.PointInside(p, r, MoverPhysics.Clock);
    }

    public static void Simulate(ref MoveState s, in InputCmd cmd)
    {
        var b = cmd.Buttons;
        // Movers (moving platforms): posed at the command's tick, so prediction, replay and the server agree.
        var movers = World.Map.Movers.Count > 0;
        _moverTick = cmd.Tick != 0 ? cmd.Tick : MoverPhysics.Clock;
        var platformVel = Vector3.Zero;
        var carried = movers ? MoverPhysics.BeginTick(ref s, _moverTick, World.HeightOf(s.Stance), out platformVel) : -1;
        if (s.BoostTicks > 0)
            s.BoostTicks--;

        // Mid-mantle: glide to the ledge, no other control. Memoryless (only ticks left + target),
        // so the server can send it in snapshots and prediction replays it exactly.
        if (s.MantleTicks > 0)
        {
            s.Position += (s.MantleTo - s.Position) / s.MantleTicks;
            if (World.Map.TerrainCollision is { } ground)
                s.Position.Y = MathF.Max(s.Position.Y, ground.HeightAt(s.Position.X, s.Position.Z)); // don't cut through a bank's edge
            s.MantleTicks--;
            s.Velocity = Vector3.Zero;
            if (s.MantleTicks == 0)
            {
                s.Position = s.MantleTo;
                s.Grounded = true;
                if ((b & Buttons.Forward) != 0)
                    s.Velocity = World.FlatForward(cmd.Yaw) * 3f; // keep going: off the sill and through the window
            }
            return;
        }

        var wish = Vector3.Zero;
        if ((b & Buttons.Forward) != 0) wish += World.FlatForward(cmd.Yaw);
        if ((b & Buttons.Back) != 0) wish -= World.FlatForward(cmd.Yaw);
        if ((b & Buttons.Right) != 0) wish += World.FlatRight(cmd.Yaw);
        if ((b & Buttons.Left) != 0) wish -= World.FlatRight(cmd.Yaw);
        if (wish.LengthSquared() > 0)
            wish = Vector3.Normalize(wish);

        // Stance: going lower is always allowed, getting up needs headroom (no standing up under a table).
        var want = (b & Buttons.Prone) != 0 ? Stance.Prone : (b & Buttons.Crouch) != 0 ? Stance.Crouch : Stance.Stand;
        if (want == Stance.Prone && !s.Grounded)
            want = Stance.Crouch; // can't go prone in mid-air
        if (want != s.Stance)
        {
            if (World.HeightOf(want) <= World.HeightOf(s.Stance) || !BlockedAll(s.Position, World.HeightOf(want), _moverTick))
                s.Stance = want;
            else if (want == Stance.Stand && s.Stance == Stance.Prone && !BlockedAll(s.Position, World.HeightOf(Stance.Crouch), _moverTick))
                s.Stance = Stance.Crouch; // only room to get halfway up
        }

        var sprint = (b & Buttons.Sprint) != 0 && s.Stance == Stance.Stand;
        var speed = (sprint ? World.SprintSpeed : World.WalkSpeed) * World.SpeedOf(s.Stance) * (cmd.SpeedPct == 0 ? 1f : cmd.SpeedPct / 100f);
        if (s.MovePerk == PerkId.LowProfile && s.Stance != Stance.Stand)
            speed *= Perks.LowProfileSpeed;
        if (s.BoostTicks > 0)
            speed *= Perks.BoostSpeed;
        var control = s.Grounded ? 14f : 3f; // snappy on the ground, floaty in the air
        var blend = 1f - MathF.Exp(-control * World.TickDt);
        s.Velocity.X += (wish.X * speed - s.Velocity.X) * blend;
        s.Velocity.Z += (wish.Z * speed - s.Velocity.Z) * blend;

        // Jump facing a ledge or window sill (while moving forward) = mantle instead of jumping.
        if ((b & Buttons.Jump) != 0 && (b & Buttons.Forward) != 0 && s.Stance != Stance.Prone && TryMantle(ref s, cmd.Yaw))
            return;

        if (s.Grounded && (b & Buttons.Jump) != 0 && s.Stance == Stance.Stand)
            s.Velocity.Y = World.JumpSpeed * (s.MovePerk == PerkId.Parkour ? Perks.ParkourJump : 1f);

        s.Position = Move(s.Position, ref s.Velocity, ref s.Grounded, World.TickDt, World.HeightOf(s.Stance));
        if (movers)
            MoverPhysics.EndTick(ref s, _moverTick, carried, platformVel);
    }

    /// <summary>The mover tick of the command being simulated (read by <see cref="Move"/>).</summary>
    private static uint _moverTick;

    private static readonly Stance[] CrouchOnly = [Stance.Crouch];
    private static readonly Stance[] StandOrCrouch = [Stance.Stand, Stance.Crouch];

    /// <summary>
    /// Looks for a ledge just in front (crate top, wall top, window sill) between step height and
    /// <see cref="World.MantleReach"/> with room to stand or crouch on it. Starts the climb if found.
    /// Windows are 1.3 m tall, so going through one puts you in a crouch on the sill.
    /// </summary>
    private static bool TryMantle(ref MoveState s, float yaw)
    {
        var f = World.FlatForward(yaw);
        var right = World.FlatRight(yaw);
        var feet = s.Position;
        var boxes = World.Map.Solid;
        var parkour = s.MovePerk == PerkId.Parkour;
        var maxReach = parkour ? Perks.ParkourReach : World.MantleReach;
        var terrain = World.Map.TerrainCollision;
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
                if (g > feet.Y + World.StepHeight && g <= feet.Y + maxReach && terrain.NormalAt(probe.X, probe.Z).Y >= World.MinWalkNormalY)
                    top = g;
            }
            if (d <= 1.05f)
            {
                // The highest top wins, so the order the boxes are looked at doesn't matter.
                if (UseGrid)
                {
                    var grid = Grid;
                    foreach (var i in grid.Near(probe.X, probe.Z, probe.X, probe.Z))
                        top = MantleTop(grid.Boxes[i], probe, feet, maxReach, top);
                }
                else
                {
                    for (var i = 0; i < boxes.Count; i++)
                        top = MantleTop(boxes[i], probe, feet, maxReach, top);
                }
            }
            if (float.IsNegativeInfinity(top))
                continue;

            foreach (var n in side)
            {
                var target = World.ClampToArena(new Vector3(probe.X, top, probe.Z) + right * n);
                foreach (var stance in s.Stance == Stance.Crouch ? CrouchOnly : StandOrCrouch)
                {
                    var h = World.HeightOf(stance);
                    // Room at the end, room to rise in place, and room halfway across.
                    if (Blocked(target, h) || Blocked(feet with { Y = top + 0.01f }, h) || Blocked(Vector3.Lerp(feet, target, 0.5f) with { Y = top + 0.01f }, h))
                        continue;
                    s.Stance = stance;
                    s.MantleTo = target;
                    var ticks = Math.Clamp((int)(8 + (top - feet.Y) * 9), 10, 28);
                    s.MantleTicks = (byte)(parkour ? Math.Max(6, ticks / 2) : ticks);
                    s.Velocity = Vector3.Zero;
                    s.Grounded = false;
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

    public static float RaycastWorld(Vector3 origin, Vector3 dir, float maxDist)
    {
        var best = RaycastBoxes(origin, dir, maxDist);
        if (World.Map.TerrainCollision is { } terrain && best > 0)
            best = MathF.Min(best, terrain.Raycast(origin, dir, best));
        if (World.Map.Movers.Count > 0 && best > 0)
            best = MoverPhysics.Raycast(origin, dir, best, MoverPhysics.Clock);
        return best;
    }

    /// <summary>
    /// Ray cast for bot navigation: static geometry, with each mover's whole sweep as a solid pillar (so nav graphs
    /// never route through moving platforms and don't depend on where they happen to be).
    /// </summary>
    public static float RaycastNav(Vector3 origin, Vector3 dir, float maxDist)
    {
        var best = RaycastBoxes(origin, dir, maxDist);
        if (World.Map.TerrainCollision is { } terrain && best > 0)
            best = MathF.Min(best, terrain.Raycast(origin, dir, best));
        if (World.Map.Movers.Count > 0 && best > 0)
            best = MoverPhysics.RaycastSweeps(origin, dir, best);
        return best;
    }

    /// <summary>Nearest distance at which a ray hits a box (terrain ignored), or maxDist.</summary>
    public static float RaycastBoxes(Vector3 origin, Vector3 dir, float maxDist)
    {
        if (UseGrid)
            return Grid.RaycastBoxes(origin, dir, maxDist);
        var best = maxDist;
        var boxes = World.Map.Solid;
        for (var i = 0; i < boxes.Count; i++)
            if (boxes[i].Raycast(origin, dir, best) is { } t && t < best)
                best = t;
        return best;
    }

    private static Vector3 Move(Vector3 feet, ref Vector3 velocity, ref bool grounded, float dt, float height = World.PlayerHeight)
    {
        var boxes = World.Map.Solid;
        var H = height;
        var wasGrounded = grounded;

        // ---- Vertical ----
        velocity.Y -= World.Gravity * dt * World.GravityScale;
        var prevY = feet.Y;
        var newY = feet.Y + velocity.Y * dt;
        grounded = false;
        // The boxes under and over the player, in map order (the grid's short list, or all of them).
        var grid = UseGrid ? Grid : null;
        var near = grid != null ? grid.Near(feet.X - World.PlayerRadius, feet.Z - World.PlayerRadius, feet.X + World.PlayerRadius, feet.Z + World.PlayerRadius) : default;
        var count = grid != null ? near.Length : boxes.Count;
        for (var k = 0; k < count; k++)
        {
            var b = grid != null ? grid.Boxes[near[k]] : boxes[k];
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
        if (World.Map.TerrainCollision is { } terrain)
            TerrainVertical(terrain, ref feet, ref velocity, ref grounded, wasGrounded, H, dt);

        // ---- Horizontal, X then Z ----
        feet = MoveAxis(feet, velocity.X * dt, true, ref velocity, grounded, H);
        feet = MoveAxis(feet, velocity.Z * dt, false, ref velocity, grounded, H);
        if (World.Map.Movers.Count > 0)
            feet = MoverPhysics.PushOut(feet, ref velocity, ref grounded, H, _moverTick); // pushed by (or stepping onto) moving platforms

        return World.ClampToArena(feet);
    }

    private static Vector3 MoveAxis(Vector3 feet, float delta, bool alongX, ref Vector3 velocity, bool grounded, float height)
    {
        if (delta == 0)
            return feet;
        var boxes = World.Map.Solid;
        var p = feet;
        if (alongX)
            p.X += delta;
        else
            p.Z += delta;

        // Only boxes that reach the strip swept by this move can stop it, so walk the grid's short list of those, in
        // map order. If a wall pushes us back out of the strip (we started inside a box), carry on with every box
        // after the current one, exactly as a scan over the whole map would.
        float x0 = MathF.Min(feet.X, p.X) - World.PlayerRadius, x1 = MathF.Max(feet.X, p.X) + World.PlayerRadius;
        float z0 = MathF.Min(feet.Z, p.Z) - World.PlayerRadius, z1 = MathF.Max(feet.Z, p.Z) + World.PlayerRadius;
        var grid = UseGrid ? Grid : null;
        var near = grid != null ? grid.Near(x0, z0, x1, z1) : default;
        var k = 0;
        var i = -1;
        while (true)
        {
            if (grid == null)
            {
                if (++i >= boxes.Count)
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
            if (grounded && rise <= World.StepHeight && !Blocked(p with { Y = b.Max.Y }, height))
            {
                p.Y = b.Max.Y;
                continue;
            }

            // A wall: stop at its face.
            if (alongX)
            {
                p.X = delta > 0 ? b.Min.X - World.PlayerRadius - 0.001f : b.Max.X + World.PlayerRadius + 0.001f;
                velocity.X = 0;
            }
            else
            {
                p.Z = delta > 0 ? b.Min.Z - World.PlayerRadius - 0.001f : b.Max.Z + World.PlayerRadius + 0.001f;
                velocity.Z = 0;
            }
            if (grid != null && (p.X - World.PlayerRadius < x0 || p.X + World.PlayerRadius > x1 || p.Z - World.PlayerRadius < z0 || p.Z + World.PlayerRadius > z1))
                grid = null; // out of the strip: every remaining box from here on
        }

        // Terrain rising in front: walk up it if it's gentle (and there's headroom), otherwise it's a wall.
        if (World.Map.TerrainCollision is { } terrain)
        {
            var g = terrain.HeightAt(p.X, p.Z);
            if (g > p.Y + 0.001f)
            {
                if (g - p.Y <= World.StepHeight && terrain.NormalAt(p.X, p.Z).Y >= World.MinWalkNormalY && !BlockedByBoxes(p with { Y = g }, height))
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
        }
        return p;
    }

    /// <summary>
    /// The ground under a player after the vertical move: land on it, stick to it walking downhill (so you don't
    /// hop down slopes), and slide off anything steeper than <see cref="World.MaxSlopeDeg"/>.
    /// </summary>
    private static void TerrainVertical(ITerrainCollision terrain, ref Vector3 feet, ref Vector3 velocity, ref bool grounded, bool wasGrounded, float height, float dt)
    {
        var g = terrain.HeightAt(feet.X, feet.Z);
        var gap = feet.Y - g;
        if (gap > 0)
        {
            // In the air above the ground: snap down only when we were walking on it last tick and it's walkable.
            var speed = MathF.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
            if (grounded || !wasGrounded || velocity.Y > 0 || gap > 0.12f + speed * dt * 1.2f)
                return;
            if (terrain.NormalAt(feet.X, feet.Z).Y < World.MinWalkNormalY || BlockedByBoxes(feet with { Y = g }, height))
                return;
        }
        var n = terrain.NormalAt(feet.X, feet.Z);
        feet.Y = g;
        if (velocity.Y < 0)
            velocity.Y = 0;
        if (n.Y >= World.MinWalkNormalY)
            grounded = true;
        else if (!grounded)
        {
            // Too steep to stand on: gravity pulls you down the slope.
            var push = World.Gravity * n.Y * dt * World.GravityScale;
            velocity.X += n.X * push;
            velocity.Z += n.Z * push;
        }
    }

    /// <summary>Would a player standing at <paramref name="feet"/> overlap any box, or be under the terrain?</summary>
    public static bool Blocked(Vector3 feet, float height = World.PlayerHeight)
    {
        if (World.Map.TerrainCollision is { } terrain && terrain.HeightAt(feet.X, feet.Z) > feet.Y + 0.05f)
            return true;
        return BlockedByBoxes(feet, height);
    }

    /// <summary>Would a player standing at <paramref name="feet"/> overlap any box (terrain ignored)?</summary>
    public static bool BlockedByBoxes(Vector3 feet, float height = World.PlayerHeight)
    {
        if (UseGrid)
            return Grid.BlockedByBoxes(feet, height);
        var boxes = World.Map.Solid;
        for (var i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            if (feet.Y < b.Max.Y - 0.001f && feet.Y + height > b.Min.Y + 0.001f && OverlapsXZ(feet, b))
                return true;
        }
        return false;
    }

    private static bool OverlapsXZ(Vector3 feet, Box box) =>
        feet.X + World.PlayerRadius > box.Min.X
        && feet.X - World.PlayerRadius < box.Max.X
        && feet.Z + World.PlayerRadius > box.Min.Z
        && feet.Z - World.PlayerRadius < box.Max.Z;
}

/// <summary>The old movement as an <see cref="IMover"/> (self-test reference only).</summary>
public sealed class LegacyMover : IMover
{
    public static readonly LegacyMover Instance = new();

    public PhysicsMode Mode => PhysicsMode.Classic;

    public void Simulate(ref MoveState s, in InputCmd cmd) => LegacyCollision.Simulate(ref s, cmd);
}
