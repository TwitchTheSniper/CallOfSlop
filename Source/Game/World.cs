using System.Numerics;

namespace CallOfSlop;

/// <summary>Axis-aligned box used for walls, floor, cover and hitboxes.</summary>
public readonly record struct Box(Vector3 Center, Vector3 Half)
{
    public Vector3 Min => Center - Half;
    public Vector3 Max => Center + Half;

    /// <summary>Slab-method ray test. Returns distance along the (normalised) ray, or null.</summary>
    public float? Raycast(Vector3 origin, Vector3 dir, float maxDist)
    {
        float tMin = 0f, tMax = maxDist;
        for (var axis = 0; axis < 3; axis++)
        {
            float o = origin[axis], d = dir[axis], lo = Min[axis], hi = Max[axis];
            if (MathF.Abs(d) < 1e-6f)
            {
                if (o < lo || o > hi)
                    return null;
                continue;
            }
            var inv = 1f / d;
            var t1 = (lo - o) * inv;
            var t2 = (hi - o) * inv;
            if (t1 > t2)
                (t1, t2) = (t2, t1);
            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
            if (tMin > tMax)
                return null;
        }
        return tMin;
    }
}

/// <summary>Buttons held during one simulation tick.</summary>
[Flags]
public enum Buttons : byte
{
    None = 0,
    Forward = 1,
    Back = 2,
    Left = 4,
    Right = 8,
    Jump = 16,
    Sprint = 32,
    Crouch = 64,
    Prone = 128,
}

public enum Stance : byte
{
    Stand,
    Crouch,
    Prone,
}

/// <summary>One tick of player input. The client sends these; the server replays them.</summary>
public struct InputCmd
{
    public uint Seq;
    public Buttons Buttons;
    public float Yaw;
    public float Pitch;
    public byte Weapon; // weapon id currently held
    public byte SpeedPct; // movement speed in % (weapon mobility, aiming); the server clamps it
}

/// <summary>Everything movement depends on. Client prediction and the server run the same code on it.</summary>
public struct MoveState
{
    public Vector3 Position; // feet
    public Vector3 Velocity;
    public bool Grounded;
    public Stance Stance;
    public byte MantleTicks; // > 0 while climbing over a ledge or through a window
    public Vector3 MantleTo; // where the climb ends (feet)
    public PerkId MovePerk; // the movement perk being carried (set by the server on spawn)
    public byte BoostTicks; // > 0 while Head Start / Afterburner speed is active
}

/// <summary>Shared rules: tick rate, player physics, movement and ray casts against the current map.</summary>
public static class World
{
    // ---- Simulation clock ---------------------------------------------------
    public const int TickRate = 60; // fixed simulation ticks per second (client and server)
    public const float TickDt = 1f / TickRate;
    public const int SnapshotEvery = 2; // server sends a snapshot every 2 ticks (30 Hz)
    public const int MaxRewindTicks = 24; // lag compensation rewinds at most 400 ms

    // ---- Map -----------------------------------------------------------------
    /// <summary>The map being played. Client and server always load the same one (sent in the Welcome).</summary>
    public static Map Map { get; private set; } = Maps.All[0];

    public static void SetMap(Map map) => Map = map;

    public const float StepHeight = 0.55f; // walk up anything this tall (stairs are 0.4 m)

    // ---- Player ------------------------------------------------------------
    public const float PlayerRadius = 0.4f;
    public const float PlayerHeight = 1.8f; // standing
    public const float EyeHeight = 1.6f;
    public const float HeadStart = 1.4f; // standing: hits above this are headshots

    // ---- Stances -------------------------------------------------------------
    public static float HeightOf(Stance s) => s switch { Stance.Crouch => 1.2f, Stance.Prone => 0.6f, _ => PlayerHeight };
    public static float EyeOf(Stance s) => s switch { Stance.Crouch => 1.05f, Stance.Prone => 0.45f, _ => EyeHeight };
    public static float SpeedOf(Stance s) => s switch { Stance.Crouch => 0.5f, Stance.Prone => 0.22f, _ => 1f };
    public static float SpreadOf(Stance s) => s switch { Stance.Crouch => 0.75f, Stance.Prone => 0.5f, _ => 1f };
    public static float RecoilOf(Stance s) => s switch { Stance.Crouch => 0.85f, Stance.Prone => 0.6f, _ => 1f };
    public const float WalkSpeed = 6f;
    public const float SprintSpeed = 9f;
    public const float JumpSpeed = 6f;
    public const float Gravity = 18f;
    public const int MaxHealth = 100;
    public const float RespawnDelay = 3f;

    // ---- Shooting (per-weapon stats live in Weapons.cs) -------------------------
    public const float Range = 200f;
    public const float AdsSpeedMult = 0.7f;

    public static Vector3 Forward(float yaw, float pitch) =>
        new(MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch), -MathF.Cos(pitch) * MathF.Cos(yaw));

    public static Vector3 FlatForward(float yaw) => new(MathF.Sin(yaw), 0, -MathF.Cos(yaw));

    public static Vector3 FlatRight(float yaw) => new(MathF.Cos(yaw), 0, MathF.Sin(yaw));

    /// <summary>
    /// Advances one player by exactly one tick of input. This must stay deterministic: the client
    /// predicts with it and replays it during reconciliation, and the server runs it authoritatively.
    /// </summary>
    public static void Simulate(ref MoveState s, in InputCmd cmd)
    {
        var b = cmd.Buttons;
        if (s.BoostTicks > 0)
            s.BoostTicks--;

        // Mid-mantle: glide to the ledge, no other control. Memoryless (only ticks left + target),
        // so the server can send it in snapshots and prediction replays it exactly.
        if (s.MantleTicks > 0)
        {
            s.Position += (s.MantleTo - s.Position) / s.MantleTicks;
            s.MantleTicks--;
            s.Velocity = Vector3.Zero;
            if (s.MantleTicks == 0)
            {
                s.Position = s.MantleTo;
                s.Grounded = true;
                if ((b & Buttons.Forward) != 0)
                    s.Velocity = FlatForward(cmd.Yaw) * 3f; // keep going: off the sill and through the window
            }
            return;
        }

        var wish = Vector3.Zero;
        if ((b & Buttons.Forward) != 0) wish += FlatForward(cmd.Yaw);
        if ((b & Buttons.Back) != 0) wish -= FlatForward(cmd.Yaw);
        if ((b & Buttons.Right) != 0) wish += FlatRight(cmd.Yaw);
        if ((b & Buttons.Left) != 0) wish -= FlatRight(cmd.Yaw);
        if (wish.LengthSquared() > 0)
            wish = Vector3.Normalize(wish);

        // Stance: going lower is always allowed, getting up needs headroom (no standing up under a table).
        var want = (b & Buttons.Prone) != 0 ? Stance.Prone : (b & Buttons.Crouch) != 0 ? Stance.Crouch : Stance.Stand;
        if (want == Stance.Prone && !s.Grounded)
            want = Stance.Crouch; // can't go prone in mid-air
        if (want != s.Stance)
        {
            if (HeightOf(want) <= HeightOf(s.Stance) || !Blocked(s.Position, HeightOf(want)))
                s.Stance = want;
            else if (want == Stance.Stand && s.Stance == Stance.Prone && !Blocked(s.Position, HeightOf(Stance.Crouch)))
                s.Stance = Stance.Crouch; // only room to get halfway up
        }

        var sprint = (b & Buttons.Sprint) != 0 && s.Stance == Stance.Stand;
        var speed = (sprint ? SprintSpeed : WalkSpeed) * SpeedOf(s.Stance) * (cmd.SpeedPct == 0 ? 1f : cmd.SpeedPct / 100f);
        if (s.MovePerk == PerkId.LowProfile && s.Stance != Stance.Stand)
            speed *= Perks.LowProfileSpeed;
        if (s.BoostTicks > 0)
            speed *= Perks.BoostSpeed;
        var control = s.Grounded ? 14f : 3f; // snappy on the ground, floaty in the air
        var blend = 1f - MathF.Exp(-control * TickDt);
        s.Velocity.X += (wish.X * speed - s.Velocity.X) * blend;
        s.Velocity.Z += (wish.Z * speed - s.Velocity.Z) * blend;

        // Jump facing a ledge or window sill (while moving forward) = mantle instead of jumping.
        if ((b & Buttons.Jump) != 0 && (b & Buttons.Forward) != 0 && s.Stance != Stance.Prone && TryMantle(ref s, cmd.Yaw))
            return;

        if (s.Grounded && (b & Buttons.Jump) != 0 && s.Stance == Stance.Stand)
            s.Velocity.Y = JumpSpeed * (s.MovePerk == PerkId.Parkour ? Perks.ParkourJump : 1f);

        s.Position = Move(s.Position, ref s.Velocity, ref s.Grounded, TickDt, HeightOf(s.Stance));
    }

    private static readonly Stance[] CrouchOnly = [Stance.Crouch];
    private static readonly Stance[] StandOrCrouch = [Stance.Stand, Stance.Crouch];
    public const float MantleReach = 1.7f; // highest ledge you can climb, above your feet

    /// <summary>
    /// Looks for a ledge just in front (crate top, wall top, window sill) between step height and
    /// <see cref="MantleReach"/> with room to stand or crouch on it. Starts the climb if found.
    /// Windows are 1.3 m tall, so going through one puts you in a crouch on the sill.
    /// </summary>
    private static bool TryMantle(ref MoveState s, float yaw)
    {
        var f = FlatForward(yaw);
        var right = FlatRight(yaw);
        var feet = s.Position;
        var boxes = Map.Solid;
        var parkour = s.MovePerk == PerkId.Parkour;
        var maxReach = parkour ? Perks.ParkourReach : MantleReach;
        ReadOnlySpan<float> reach = [0.55f, 0.8f, 1.05f];
        ReadOnlySpan<float> side = [0f, 0.15f, -0.15f, 0.3f, -0.3f]; // small sideways nudge to line up with windows

        foreach (var d in reach)
        {
            var probe = feet + f * d;
            // Highest box top under the probe point that's a climbable height.
            var top = float.NegativeInfinity;
            for (var i = 0; i < boxes.Count; i++)
            {
                var bx = boxes[i];
                if (probe.X < bx.Min.X || probe.X > bx.Max.X || probe.Z < bx.Min.Z || probe.Z > bx.Max.Z)
                    continue;
                var t = bx.Max.Y;
                if (t > feet.Y + StepHeight && t <= feet.Y + maxReach && t > top)
                    top = t;
            }
            if (float.IsNegativeInfinity(top))
                continue;

            foreach (var n in side)
            {
                var target = ClampToArena(new Vector3(probe.X, top, probe.Z) + right * n);
                foreach (var stance in s.Stance == Stance.Crouch ? CrouchOnly : StandOrCrouch)
                {
                    var h = HeightOf(stance);
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

    public static Box Hitbox(Vector3 feet) => Hitbox(feet, Stance.Stand, 0);

    /// <summary>Damage box for a player. Prone players are a long, low box stretched along their facing.</summary>
    public static Box Hitbox(Vector3 feet, Stance stance, float yaw)
    {
        if (stance != Stance.Prone)
        {
            var h = HeightOf(stance);
            return new(feet + new Vector3(0, h / 2, 0), new Vector3(PlayerRadius, h / 2, PlayerRadius));
        }
        var f = FlatForward(yaw);
        // Matches the drawn body: from 0.8 m behind the feet to 1.0 m in front (head and gun).
        var half = new Vector3(MathF.Abs(f.X) * 0.9f + MathF.Abs(f.Z) * 0.3f, 0.3f, MathF.Abs(f.Z) * 0.9f + MathF.Abs(f.X) * 0.3f);
        return new(feet + f * 0.1f + new Vector3(0, 0.3f, 0), half);
    }

    /// <summary>Did a hit at <paramref name="point"/> land on the head?</summary>
    public static bool IsHeadshot(Vector3 feet, Stance stance, float yaw, Vector3 point)
    {
        var rel = point - feet;
        return stance switch
        {
            Stance.Crouch => rel.Y >= 0.85f,
            Stance.Prone => rel.Y >= 0.2f && Vector3.Dot(new Vector3(rel.X, 0, rel.Z), FlatForward(yaw)) >= 0.55f, // head is out front
            _ => rel.Y >= HeadStart,
        };
    }

    /// <summary>Nearest distance at which a ray hits static geometry (or maxDist if nothing).</summary>
    public static float RaycastWorld(Vector3 origin, Vector3 dir, float maxDist)
    {
        var best = maxDist;
        var boxes = Map.Solid;
        for (var i = 0; i < boxes.Count; i++)
            if (boxes[i].Raycast(origin, dir, best) is { } t && t < best)
                best = t;
        return best;
    }

    /// <summary>
    /// Moves a player's feet by velocity * dt against every box on the map, one axis at a time:
    /// land on anything, bump heads on ceilings, slide along walls and walk up steps of up to
    /// <see cref="StepHeight"/>. Deterministic, so prediction and the server always agree.
    /// </summary>
    public static Vector3 Move(Vector3 feet, ref Vector3 velocity, ref bool grounded, float dt, float height = PlayerHeight)
    {
        var boxes = Map.Solid;
        var H = height;

        // ---- Vertical ----
        velocity.Y -= Gravity * dt;
        var prevY = feet.Y;
        var newY = feet.Y + velocity.Y * dt;
        grounded = false;
        for (var i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
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
        feet.Y = newY;

        // ---- Horizontal, X then Z ----
        feet = MoveAxis(feet, velocity.X * dt, true, ref velocity, grounded, H);
        feet = MoveAxis(feet, velocity.Z * dt, false, ref velocity, grounded, H);

        return ClampToArena(feet);
    }

    private static Vector3 MoveAxis(Vector3 feet, float delta, bool alongX, ref Vector3 velocity, bool grounded, float height)
    {
        if (delta == 0)
            return feet;
        var boxes = Map.Solid;
        var p = feet;
        if (alongX)
            p.X += delta;
        else
            p.Z += delta;

        for (var i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            if (p.Y >= b.Max.Y - 0.001f || p.Y + height <= b.Min.Y + 0.001f || !OverlapsXZ(p, b))
                continue;

            // A step? Climb it if there's room for our head up there.
            var rise = b.Max.Y - p.Y;
            if (grounded && rise <= StepHeight && !Blocked(p with { Y = b.Max.Y }, height))
            {
                p.Y = b.Max.Y;
                continue;
            }

            // A wall: stop at its face.
            if (alongX)
            {
                p.X = delta > 0 ? b.Min.X - PlayerRadius - 0.001f : b.Max.X + PlayerRadius + 0.001f;
                velocity.X = 0;
            }
            else
            {
                p.Z = delta > 0 ? b.Min.Z - PlayerRadius - 0.001f : b.Max.Z + PlayerRadius + 0.001f;
                velocity.Z = 0;
            }
        }
        return p;
    }

    /// <summary>Would a player standing at <paramref name="feet"/> overlap any box?</summary>
    public static bool Blocked(Vector3 feet, float height = PlayerHeight)
    {
        var boxes = Map.Solid;
        for (var i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            if (feet.Y < b.Max.Y - 0.001f && feet.Y + height > b.Min.Y + 0.001f && OverlapsXZ(feet, b))
                return true;
        }
        return false;
    }

    public static Vector3 ClampToArena(Vector3 feet)
    {
        feet.X = Math.Clamp(feet.X, -Map.HalfX + PlayerRadius, Map.HalfX - PlayerRadius);
        feet.Z = Math.Clamp(feet.Z, -Map.HalfZ + PlayerRadius, Map.HalfZ - PlayerRadius);
        feet.Y = Math.Clamp(feet.Y, -5, 60);
        return feet;
    }

    private static bool OverlapsXZ(Vector3 feet, Box box) =>
        feet.X + PlayerRadius > box.Min.X
        && feet.X - PlayerRadius < box.Max.X
        && feet.Z + PlayerRadius > box.Min.Z
        && feet.Z - PlayerRadius < box.Max.Z;

    public static float WrapAngle(float a)
    {
        while (a > MathF.PI)
            a -= MathF.Tau;
        while (a < -MathF.PI)
            a += MathF.Tau;
        return a;
    }

    public static float LerpAngle(float a, float b, float t) => a + WrapAngle(b - a) * t;
}

/// <summary>Where a player was (and how they stood) at one server tick.</summary>
public struct HistoryEntry
{
    public Vector3 Position;
    public Stance Stance;
    public float Yaw;
}

/// <summary>A remote player's state at one server tick, buffered on clients for interpolation.</summary>
public struct Snap
{
    public Stance Stance;
    public uint Tick;
    public Vector3 Position;
    public Vector3 Velocity;
    public float Yaw;
    public float Pitch;
}

/// <summary>State for one player.</summary>
public sealed class Player
{
    public byte Id;
    public string Name = "";
    public BotBrain? Bot; // server-side AI, null for people
    public MoveState Sim; // authoritative on the server, predicted for the local player on a client
    public float Yaw;
    public float Pitch;
    public int Health = World.MaxHealth;
    public bool Alive = true;
    public ushort Kills;
    public ushort Deaths;
    public byte Team = Game.NoTeam; // team modes: 0 (Blue) or 1 (Red); replicated in the mode state
    public byte GunLevel; // Gun Game: step on the weapon ladder (replicated)
    public bool Ready; // warmup: ready for the match (replicated; bots count as ready)

    // Loadout and the weapon in hand (replicated so others see and hear the right gun)
    public byte Primary = Weapons.DefaultPrimary.Id;
    public byte Secondary = Weapons.DefaultSecondary.Id;
    public byte Weapon = Weapons.DefaultPrimary.Id;
    public uint PrimaryKit, SecondaryKit; // attachments on each gun (replicated)
    public PerkSet Perks = PerkSet.Empty; // replicated

    // Throwables (server-authoritative counts, refilled on spawn)
    public ThrowableKind Grenade, Equipment, WantGrenade, WantEquipment;
    public int GrenadesLeft, EquipmentLeft;
    public double NextThrow;
    public WeaponDef WeaponDef => DefFor(Weapon);

    /// <summary>A weapon's stats with this player's attachments on it.</summary>
    public WeaponDef DefFor(byte weaponId) =>
        Attachments.Apply(Weapons.Get(weaponId), weaponId == Primary ? PrimaryKit : weaponId == Secondary ? SecondaryKit : 0);
    public bool Owns(byte weaponId) => weaponId == Primary || weaponId == Secondary;

    public Vector3 Position
    {
        get => Sim.Position;
        set => Sim.Position = value;
    }

    /// <summary>Sim position one tick ago, so rendering can blend between ticks at any frame rate.</summary>
    public Vector3 PrevPosition;

    // What actually gets drawn this frame (tick-blended, interpolated or error-smoothed).
    public Vector3 RenderPosition;
    public float RenderYaw;
    public float RenderPitch;

    // ---- Server only --------------------------------------------------------
    public readonly List<InputCmd> InputQueue = new();
    public uint LastProcessedSeq;
    public uint HighestQueuedSeq;
    public InputCmd LastInput;
    public int StarvedTicks;
    public bool Buffering = true;
    public double RespawnAt;
    public double LastShotTime = -10;
    public double ShotClock; // fire-rate budget (see Game.ServerResolveShot)
    public double LastCombatTime = -100; // last time they shot or were shot (for instant loadout swaps)
    public byte WantPrimary = Weapons.DefaultPrimary.Id; // requested loadout, applied on (re)spawn
    public byte WantSecondary = Weapons.DefaultSecondary.Id;
    public uint WantPrimaryKit, WantSecondaryKit;
    public PerkSet WantPerks = PerkSet.Empty;
    public bool LastStandUsed; // Last Stand saves you once per life
    public double ImmuneUntil; // Last Stand's moment of immunity
    public double NextMelee;
    public bool WantsRespawn; // people respawn when they click Respawn; bots respawn on their own
    public uint SpawnTick;
    public StreakState Streak = new(); // kill streaks (server-owned; a client gets its own copy)
    public const int HistorySize = 128;
    public readonly HistoryEntry[] History = new HistoryEntry[HistorySize]; // per tick, for lag compensation

    // ---- Client only (remote players) ---------------------------------------
    public readonly List<Snap> Snaps = new();

    public Stance RenderStance; // stance being drawn (interpolated for remote players)
    public float RenderEyeHeight = World.EyeHeight; // smoothed so crouching doesn't snap the camera

    public Vector3 Eye => Position + new Vector3(0, World.EyeOf(Sim.Stance), 0);
    public Vector3 RenderEye => RenderPosition + new Vector3(0, RenderEyeHeight, 0);
    public Vector3 Chest => Position + new Vector3(0, World.HeightOf(Sim.Stance) * 0.65f, 0);

    public void Teleport(Vector3 feet)
    {
        Sim = new MoveState { Position = feet, Grounded = true };
        PrevPosition = feet;
        RenderPosition = feet;
        RenderStance = Stance.Stand;
        RenderEyeHeight = World.EyeHeight;
    }

    public static readonly Vector3[] Palette =
    [
        new(0.20f, 0.55f, 1.00f), // blue
        new(1.00f, 0.25f, 0.25f), // red
        new(0.25f, 0.90f, 0.35f), // green
        new(1.00f, 0.85f, 0.15f), // yellow
        new(0.75f, 0.35f, 1.00f), // purple
        new(0.10f, 0.90f, 0.90f), // cyan
        new(1.00f, 0.55f, 0.10f), // orange
        new(1.00f, 1.00f, 1.00f), // white
    ];

    public Vector3 Color => Team < 2 ? GameModes.TeamColors[Team] : Palette[Id % Palette.Length];
}
