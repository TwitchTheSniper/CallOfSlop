using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>How a ragdoll is drawn.</summary>
public enum RagdollLook : byte
{
    /// <summary>A player or bot: the blocky soldier (torso, head, legs, arms, plate carrier, hat).</summary>
    Soldier,

    /// <summary>A zombie: torn shirt, green skin.</summary>
    Zombie,
}

/// <summary>
/// Everything a ragdoll starts from: where the body was and how it moved (the drawn pose every PC already has), and the
/// kill (killer, kill id, headshot, where the damage came from; all in the <see cref="KillPacket"/> every client gets).
/// The same death gives the same fall (<see cref="Ragdolls.Seed"/>).
/// </summary>
public struct RagdollDeath
{
    public Vector3 Feet;
    public float Yaw;
    public Stance Stance;
    public Vector3 Velocity;
    public byte Victim, Killer, KillId;
    public bool Headshot;

    /// <summary>Where the damage came from (the killer's eye, the swing), if known.</summary>
    public Vector3 Source;
    public bool HasSource;

    /// <summary>A shot's push (m/s, <see cref="Ragdolls.GunPower"/>); 0 = a default.</summary>
    public float GunPower;

    /// <summary>A blast kill: where, how hard (m/s at the centre) and how far it pushes (0 strength = not a blast).</summary>
    public Vector3 BlastAt;
    public float BlastStrength, BlastRadius;
}

/// <summary>One body in the <see cref="Ragdolls"/> pool: the simulation, its look, the hat and the cached boxes of a sleeping body.</summary>
public sealed class RagdollBody
{
    /// <summary>Most boxes one body draws (soldier: torso, head, pelvis, legs, arms, hands and a hat).</summary>
    public const int MaxBoxes = 80;

    public readonly Ragdoll Sim = new();
    public bool Live;
    public RagdollLook Look;

    /// <summary>The body was blown apart (<see cref="Gibs"/>): this slot only carries its hat, flying off as one more gib.</summary>
    public bool Gibbed;

    /// <summary>The player whose body it is (255 for zombies), and when it appeared and goes.</summary>
    public byte Owner = 255;
    public double Born, Until;
    public long Order;

    /// <summary>The blast that killed it, if one did (the same blast arriving again just after is not applied twice).</summary>
    public Vector3 BlastAt;
    public bool HasBlast;

    // Look
    public readonly BodyParts Torso = new(), Head = new();
    public LegColors Legs;
    public ArmColors Arms;
    public Vector3 ZShirt, ZSkin, ZPants;

    /// <summary>The body's final blood marks: an amount per <see cref="BloodPart"/> (0-1; BodyBloodRender.RagdollAmounts) and a pattern seed.</summary>
    public readonly float[] Blood = new float[8];
    public float BloodSeed;

    // The prestige hat: on the head, or knocked off (flying, then lying on its side).
    public HatDef? Hat;
    public bool HatOff, HatResting;
    public Vector3 HatPos, HatVel, HatAxis, HatRestPos;
    public Quaternion HatRot, HatRestRot;
    public float HatSpin, HatLand;

    /// <summary>A sleeping body's boxes, written once (<see cref="PropDebris.FloatsPerPiece"/> floats each), or -1 to rebuild.</summary>
    public readonly float[] Cache = new float[MaxBoxes * PropDebris.FloatsPerPiece];
    public int CacheCount = -1;

    /// <summary>Is everything about this body still (nothing to recompute per frame)?</summary>
    public bool Still => Sim.Asleep && (Hat == null || !HatOff || HatResting && HatLand >= 1f);
}

/// <summary>
/// Ragdolls on death: when a player, bot or zombie dies its blocky body becomes a <see cref="Ragdoll"/> that falls the
/// way the kill pushed it (the bullet's direction scaled by the gun's damage and calibre, a headshot snapping the head
/// back, a melee shove, a blast throwing it from the explosion), on top of how it was moving. Bodies collide with the
/// map, settle, fall asleep (then they cost nothing but a copy of their cached boxes), and fade out (dithered, no
/// sorting) after <see cref="PlayerLife"/> / <see cref="ZombieLife"/> seconds. At most <see cref="Capacity"/> bodies
/// (the oldest goes first; zombies at most <see cref="MaxZombieBodies"/>) and <see cref="MaxAwake"/> awake at once
/// (the oldest awake one freezes). Later blasts fling bodies again (<see cref="Blast"/>).
///
/// Cosmetic and client-side: each PC builds its bodies from the kill message and the drawn pose it already has, so
/// everyone sees roughly the same fall; nothing is sent per tick, and bodies never block bullets or movement.
/// Drawn in one instanced call with the prop debris (<see cref="PropRenderer"/>), or immediately when that's unavailable.
/// Turn them off with CALLOFSLOP_RAGDOLLS=0 (the old flat corpse box).
/// </summary>
public static class Ragdolls
{
    public const int Capacity = 24, MaxAwake = 16, MaxZombieBodies = 10;
    public const float PlayerLife = 14f, ZombieLife = 7f, FadeTime = 1.2f;

    /// <summary>Most boxes all bodies draw together (the instance buffer's share).</summary>
    public const int MaxInstances = Capacity * RagdollBody.MaxBoxes;

    /// <summary>Seconds after a kill message within which a body that's dead becomes a ragdoll (later: it was already dead, e.g. on the loadout screen).</summary>
    public const double KillWindow = 3.0;

    public static bool Enabled = Environment.GetEnvironmentVariable("CALLOFSLOP_RAGDOLLS") != "0";

    /// <summary>Self-test: the world bodies collide with instead of the current map's.</summary>
    public static PhysicsWorld? WorldOverride;

    private static PhysicsWorld Physics => WorldOverride ?? World.Physics;

    private static readonly RagdollBody[] Bodies = MakeBodies();
    private static readonly bool[] Owns = new bool[256];
    private static readonly KillPacket[] Kills = new KillPacket[256];
    private static readonly double[] KillTimes = MakeTimes();
    private static long _order;
    private static double _acc, _now;
    private static float _alpha = 1f;

    // Stats (since the last read of Stats)
    private static long _simTicks, _worstSimTicks;
    private static int _frames, _steps, _spawned;

    private static RagdollBody[] MakeBodies()
    {
        var b = new RagdollBody[Capacity];
        for (var i = 0; i < b.Length; i++)
            b[i] = new RagdollBody();
        return b;
    }

    private static double[] MakeTimes()
    {
        var t = new double[256];
        Array.Fill(t, double.NegativeInfinity);
        return t;
    }

    /// <summary>Removes every body (a new map, a new match).</summary>
    public static void Clear()
    {
        foreach (var b in Bodies)
            b.Live = false;
        Array.Clear(Owns);
        Array.Fill(KillTimes, double.NegativeInfinity);
        _acc = 0;
    }

    /// <summary>A kill message (server and clients): the victim's next death pose becomes a ragdoll pushed by it.</summary>
    public static void NoteKill(in KillPacket kill, double now)
    {
        Kills[kill.Victim] = kill;
        KillTimes[kill.Victim] = now;
    }

    /// <summary>Is a ragdoll (or one that already faded) showing <paramref name="id"/>'s body for this death? Then the player rig draws nothing.</summary>
    public static bool OwnsBody(byte id) => Enabled && Owns[id];

    /// <summary>Bodies in the pool right now, and how many are awake.</summary>
    public static int LiveCount
    {
        get
        {
            var n = 0;
            foreach (var b in Bodies)
                if (b.Live)
                    n++;
            return n;
        }
    }

    public static int AwakeCount
    {
        get
        {
            var n = 0;
            foreach (var b in Bodies)
                if (b.Live && !b.Sim.Asleep)
                    n++;
            return n;
        }
    }

    /// <summary>Is anything to draw?</summary>
    public static bool HasInstances => Enabled && LiveCount > 0;

    /// <summary>Where player <paramref name="id"/>'s newest ragdoll lies (its hips), if one is showing (the death camera looks at it).</summary>
    public static Vector3? BodyPoint(byte id)
    {
        RagdollBody? best = null, newest = null;
        foreach (var b in Bodies)
        {
            if (!b.Live || b.Owner != id || b.Look != RagdollLook.Soldier)
                continue;
            if (newest == null || b.Order > newest.Order)
                newest = b;
            if (!b.Gibbed && (best == null || b.Order > best.Order))
                best = b;
        }
        if (newest is { Gibbed: true } && Gibs.BodyPoint(id) is { } torso)
            return torso; // blown apart (only the hat is a body slot): the torso chunk
        return best?.Sim.Pelvis ?? Gibs.BodyPoint(id);
    }

    /// <summary>The pool's slots (self-test).</summary>
    public static RagdollBody Body(int i) => Bodies[i];

    // ---- Per frame -------------------------------------------------------------------------------------------

    /// <summary>
    /// Every rendered frame, before the world is drawn: new bodies for players and zombies that just died, fixed 60 Hz
    /// steps for the awake ones, hats, and bodies whose time is up removed.
    /// </summary>
    public static void Frame(Game game, double now, float dt)
    {
        _now = now;
        if (!Enabled)
            return;
        foreach (var p in game.Players.Values)
        {
            if (p.Alive)
            {
                Owns[p.Id] = false;
                continue;
            }
            if (Owns[p.Id] || now - KillTimes[p.Id] > KillWindow)
                continue; // already a ragdoll, or dead without a fresh kill (waiting on the loadout screen: the old corpse)
            Owns[p.Id] = true;
            SpawnPlayer(game, p, Kills[p.Id], now);
        }
        foreach (var z in game.Horde)
            if (z.State == ZState.Dead && !z.Ragdolled)
            {
                z.Ragdolled = true;
                SpawnZombie(game, z, now);
            }
        Advance(now, dt);
    }

    /// <summary>Runs the fixed steps owed for <paramref name="dt"/> seconds (at most 4 per frame) and expires bodies.</summary>
    public static void Advance(double now, float dt)
    {
        _now = now;
        foreach (var b in Bodies)
            if (b.Live && now >= b.Until)
                b.Live = false;
        _acc += Math.Clamp(dt, 0f, 0.1f);
        var steps = (int)(_acc / Ragdoll.Step);
        if (steps > 4)
        {
            steps = 4;
            _acc = 0;
        }
        else
            _acc -= steps * Ragdoll.Step;
        var t0 = Stopwatch.GetTimestamp();
        for (var s = 0; s < steps; s++)
            StepAll();
        var ticks = Stopwatch.GetTimestamp() - t0;
        _simTicks += ticks;
        _worstSimTicks = Math.Max(_worstSimTicks, ticks);
        _frames++;
        _steps += steps;
        _alpha = (float)Math.Clamp(_acc / Ragdoll.Step, 0, 1);
    }

    /// <summary>One 60 Hz step of every awake body and flying hat (the oldest awake bodies beyond <see cref="MaxAwake"/> freeze first).</summary>
    public static void StepAll()
    {
        var awake = AwakeCount;
        while (awake > MaxAwake)
        {
            RagdollBody? oldest = null;
            foreach (var b in Bodies)
                if (b.Live && !b.Sim.Asleep && (oldest == null || b.Order < oldest.Order))
                    oldest = b;
            oldest!.Sim.Sleep();
            oldest.CacheCount = -1;
            awake--;
        }
        var world = Physics;
        foreach (var b in Bodies)
        {
            if (!b.Live)
                continue;
            if (!b.Sim.Asleep)
            {
                b.Sim.Advance(world);
                if (b.Sim.Broken)
                {
                    b.Live = false;
                    continue;
                }
                if (b.Sim.Asleep)
                    b.CacheCount = -1; // just fell asleep: cache its final pose
            }
            StepHat(b, world);
        }
    }

    // ---- Spawning ---------------------------------------------------------------------------------------------

    /// <summary>A shot's push on a body (m/s): bigger for hard-hitting, big-calibre guns (shotguns and snipers throw bodies, pistols nudge them).</summary>
    public static float GunPower(WeaponDef def) => Math.Clamp(def.ShotDamage * (0.6f + def.Caliber) / 18f, 1.2f, 6f);

    private static void SpawnPlayer(Game game, Player p, in KillPacket kill, double now)
    {
        var killer = game.Players.GetValueOrDefault(kill.Killer);
        var d = new RagdollDeath
        {
            Feet = p.RenderPosition, Yaw = p.RenderYaw, Stance = p.RenderStance, Velocity = p.RenderVelocity,
            Victim = p.Id, Killer = kill.Killer, KillId = kill.KillId, Headshot = kill.Headshot,
        };
        if (kill.Source is { } src)
        {
            d.Source = src;
            d.HasSource = true;
        }
        if (KillInfo.IsGunId(kill.KillId))
            d.GunPower = GunPower(killer?.DefFor(kill.KillId) ?? Weapons.Get(kill.KillId));
        var (strength, radius) = Knockback.ForKill(kill.KillId);
        if (strength > 0)
        {
            // The blast itself (its drawn flash has the exact spot and radius, Demolitions included), else the packet's source.
            if (RecentBlast(game, d.Feet, now, 1.0) is { } b)
                (d.BlastAt, d.BlastRadius) = (b.Pos, b.Radius);
            else if (d.HasSource && kill.KillId >= Throwables.KillIdBase)
                (d.BlastAt, d.BlastRadius) = (d.Source, radius);
            else
                strength = 0;
            d.BlastStrength = strength;
        }
        if (kill.Gibbed && TryGibPlayer(p, d, kill, now))
            return; // blown apart: gibs instead of a ragdoll
        var body = Alloc(false);
        body.Look = RagdollLook.Soldier;
        body.Owner = p.Id;
        body.Hat = HatRender.HatFor(p);
        BuildSoldierLook(body, p.Name, p.Id, p.Color, p.Look);
        SetBlood(body, p.Id, p.Blood); // the body keeps its final blood marks (Source/Render/BodyBloodRender.cs)
        Spawn(body, d, now, PlayerLife);
    }

    private static void SpawnZombie(Game game, Zombie z, double now)
    {
        var vel = z.Sim.Velocity;
        if (z.Snaps.Count >= 2)
        {
            var (t1, p1, _) = z.Snaps[^1];
            var (t0, p0, _) = z.Snaps[^2];
            if (t1 > t0)
                vel = (p1 - p0) / (float)((t1 - t0) * World.TickDt);
        }
        var d = new RagdollDeath
        {
            Feet = z.RenderPos, Yaw = z.RenderYaw, Stance = Stance.Stand, Velocity = vel, Victim = 255, Killer = 255, Headshot = z.KilledByHead,
            KillId = (byte)(z.Id & 0x7F), // only seeds the jitter
        };
        if (RecentBlast(game, d.Feet, now, 0.5) is { } b)
        {
            (d.BlastAt, d.BlastRadius) = (b.Pos, b.Radius);
            d.BlastStrength = MathF.Max(Throwables.Get(b.Kind).Knockback, 6f);
            if (ZombieGibbed(b.Pos, b.Radius, d.Feet))
            {
                GibZombie(d);
                return; // blown apart (clients don't get the server's damage, so it's how close the blast was)
            }
        }
        else
        {
            // Shot (clients don't know by whom): pushed away from the nearest living player.
            Player? near = null;
            foreach (var p in game.Players.Values)
                if (p.Alive && (near == null || Vector3.DistanceSquared(p.RenderPosition, d.Feet) < Vector3.DistanceSquared(near.RenderPosition, d.Feet)))
                    near = p;
            if (near != null)
            {
                d.Source = near.RenderEye;
                d.HasSource = true;
                d.GunPower = 2.5f;
            }
        }
        var body = Alloc(true);
        body.Look = RagdollLook.Zombie;
        body.Owner = 255;
        body.Hat = null;
        (body.ZShirt, body.ZSkin, body.ZPants) = (ZombieShirt, ZombieSkin, ZombiePants);
        SetBlood(body, 0x10000u + z.Id, 0);
        Spawn(body, d, now, ZombieLife);
    }

    /// <summary>A dead body's blood: every mark shows (health 0), by the owner's recorded hits; Off: none.</summary>
    public static void SetBlood(RagdollBody body, uint owner, uint blood)
    {
        BodyBloodRender.RagdollAmounts(owner, blood, 0f, BodyBloodRender.Mode, body.Blood);
        body.BloodSeed = (BloodMarks.Hash(owner, blood, 7) & 0xFFFF) / 65535f;
    }

    // ---- Gibs (blast kills that blow the body apart) -----------------------------------------------------------

    /// <summary>Bodies blown apart into gibs since start-up (players, bots and zombies).</summary>
    public static int GibbedBodies;

    /// <summary>A zombie killed by a blast within this fraction of its radius (from its chest) is blown apart.</summary>
    public const float ZombieGibFraction = 0.45f;

    /// <summary>Is a zombie at <paramref name="feet"/> close enough to the blast that killed it to be blown apart (and gore on)?</summary>
    public static bool ZombieGibbed(Vector3 blast, float radius, Vector3 feet) =>
        Gibs.Mode != GoreMode.Off && Vector3.Distance(blast, feet + new Vector3(0, 1.1f, 0)) <= radius * ZombieGibFraction;

    /// <summary>
    /// A player's body blown apart (the kill's "gibbed" flag): gibs in their colours, flung from the blast, and their
    /// prestige hat flying off on its own. False on Off gore (the caller makes a normal ragdoll).
    /// </summary>
    private static bool TryGibPlayer(Player p, in RagdollDeath d, in KillPacket kill, double now)
    {
        if (Gibs.Mode == GoreMode.Off)
            return false;
        var v = BodyModel.VariantFor(p);
        var bc = BodyColors.For(p.Color, v);
        var lc = LegColors.For(p.Color);
        var ac = ArmColors.For(p.Color * 0.75f);
        var strength = d.BlastStrength > 0 ? d.BlastStrength : MathF.Max(Knockback.ForKill(kill.KillId).Strength, 9f);
        Gibs.SpawnBody(new GibBody
        {
            Feet = d.Feet, Yaw = d.Yaw, Stance = d.Stance, Velocity = d.Velocity, Owner = p.Id, Seed = Seed(d),
            BlastAt = d.BlastStrength > 0 ? d.BlastAt : d.HasSource ? d.Source : d.Feet, Strength = strength,
            Vest = bc.Shirt, Sleeve = ac.Sleeve, Skin = bc.Skin, Hair = bc.Hair, Trousers = lc.Trousers, Boot = lc.Boot, Glove = ac.Glove,
        });
        GibbedBodies++;
        if (HatRender.HatFor(p) is { } hat)
            SpawnHatGib(hat, d, now, p.Id, p.Name, p.Color, p.Look);
        return true;
    }

    /// <summary>A gibbed player's hat: a body slot that only draws the hat, knocked off by the blast (the ragdoll's hat physics).</summary>
    public static RagdollBody SpawnHatGib(HatDef hat, in RagdollDeath d, double now, byte owner, string name, Vector3 color, PlayerLook look = default)
    {
        _now = now;
        var body = Alloc(false);
        body.Look = RagdollLook.Soldier;
        body.Owner = owner;
        body.Hat = hat;
        BuildSoldierLook(body, name, owner, color, look);
        Spawn(body, d, now, PlayerLife);
        body.Gibbed = true;
        if (!body.HatOff)
        {
            var rng = new Foliage.Rng(Seed(d) ^ 0x4a7u);
            PopHat(body, ref rng);
        }
        body.Sim.Sleep(); // no body left to simulate
        body.CacheCount = -1;
        return body;
    }

    /// <summary>A zombie blown apart: gibs in zombie colours (torn shirt, green skin), no ragdoll.</summary>
    private static void GibZombie(in RagdollDeath d)
    {
        Gibs.SpawnBody(new GibBody
        {
            Feet = d.Feet, Yaw = d.Yaw, Stance = Stance.Stand, Velocity = d.Velocity, Owner = 255, Seed = Seed(d), BlastAt = d.BlastAt, Strength = d.BlastStrength,
            Vest = ZombieShirt, Sleeve = ZombieSkin * 0.92f, Skin = ZombieSkin, Hair = ZombieSkin * 0.5f, Trousers = ZombiePants, Boot = ZombiePants * 0.6f, Glove = ZombieSkin,
        });
        GibbedBodies++;
    }

    /// <summary>Zombie colours (set once by the game, the same as the living zombies).</summary>
    public static Vector3 ZombieShirt = new(0.33f, 0.3f, 0.26f), ZombieSkin = new(0.36f, 0.52f, 0.3f), ZombiePants = new(0.18f, 0.2f, 0.27f);

    /// <summary>The blast nearest a body that went off in the last <paramref name="within"/> seconds and reaches it, if any.</summary>
    private static Blast? RecentBlast(Game game, Vector3 feet, double now, double within)
    {
        Blast? best = null;
        var bestD = float.MaxValue;
        foreach (var b in game.Blasts)
        {
            if (now - b.Born > within)
                continue;
            var dist = Vector3.Distance(b.Pos, feet + new Vector3(0, 1f, 0));
            if (dist < b.Radius + 1.5f && dist < bestD)
                (best, bestD) = (b, dist);
        }
        return best;
    }

    /// <summary>The soldier's torso, head, leg and arm colours for a player (the same look as the living body).</summary>
    public static void BuildSoldierLook(RagdollBody body, string name, byte id, Vector3 color, PlayerLook look = default)
    {
        var v = BodyModel.VariantFor(name, id, look); // the player's APPEARANCE picks (Auto: the name's look)
        BodyModel.Build(body.Torso, body.Head, 0, v, BodyColors.For(color, v), body.Hat != null);
        body.Torso.ShowDetail = body.Head.ShowDetail = true;
        body.Legs = LegColors.For(color);
        body.Arms = ArmColors.For(color * 0.75f);
    }

    /// <summary>A free slot, or the oldest body (for a zombie past <see cref="MaxZombieBodies"/>: the oldest zombie).</summary>
    private static RagdollBody Alloc(bool zombie)
    {
        RagdollBody? oldest = null, oldestZombie = null;
        var zombies = 0;
        foreach (var b in Bodies)
        {
            if (!b.Live)
            {
                oldest ??= b;
                continue;
            }
            if (b.Look == RagdollLook.Zombie)
            {
                zombies++;
                if (oldestZombie == null || b.Order < oldestZombie.Order)
                    oldestZombie = b;
            }
        }
        if (zombie && zombies >= MaxZombieBodies && oldestZombie != null)
            return oldestZombie;
        if (oldest != null)
            return oldest;
        foreach (var b in Bodies)
            if (oldest == null || b.Order < oldest.Order)
                oldest = b;
        return oldest!;
    }

    /// <summary>A deterministic seed for a death (the same death data gives the same seed on every PC; positions to 25 cm).</summary>
    public static uint Seed(in RagdollDeath d)
    {
        var h = 2166136261u;
        void Mix(int v) => h = (h ^ (uint)v) * 16777619u;
        Mix(d.Victim);
        Mix(d.Killer);
        Mix(d.KillId);
        Mix(d.Headshot ? 1 : 0);
        Mix((int)MathF.Round(d.Feet.X * 4));
        Mix((int)MathF.Round(d.Feet.Z * 4));
        Mix((int)MathF.Round(d.Yaw * 8));
        return h;
    }

    /// <summary>Places a body for a death and gives it the kill's push. Also used by the self-test.</summary>
    public static void Spawn(RagdollBody body, in RagdollDeath d, double now, float life)
    {
        body.Live = true;
        body.Born = now;
        body.Until = now + life;
        body.Order = ++_order;
        body.CacheCount = -1;
        body.HasBlast = false;
        body.Gibbed = false;
        body.HatOff = body.HatResting = false;
        _spawned++;
        var pose = d.Stance switch { Stance.Prone => RagdollRig.Prone, Stance.Crouch => RagdollRig.Crouch, _ => RagdollRig.Stand };
        var toWorld = GravityFrames.Curved // a planet: the body falls from standing on its curve
            ? Matrix4x4.CreateRotationY(-d.Yaw) * GravityFrames.Basis(d.Feet) * Matrix4x4.CreateTranslation(d.Feet)
            : Matrix4x4.CreateRotationY(-d.Yaw) * Matrix4x4.CreateTranslation(d.Feet);
        var vel = d.Velocity;
        if (!float.IsFinite(vel.X + vel.Y + vel.Z))
            vel = Vector3.Zero;
        if (vel.LengthSquared() > 144f)
            vel = Vector3.Normalize(vel) * 12f;
        body.Sim.Init(pose, toWorld, vel);
        var rng = new Foliage.Rng(Seed(d));
        ApplyDeath(body, d, ref rng);
    }

    /// <summary>Self-test: what a PC does for a player's fresh death (a ragdoll, or gibs for a "gibbed" kill).</summary>
    public static void SpawnPlayerForTest(Game game, Player p, in KillPacket kill, double now)
    {
        _now = now;
        SpawnPlayer(game, p, kill, now);
    }

    /// <summary>Self-test: a body for a death without a game (soldier look in <paramref name="color"/>).</summary>
    public static RagdollBody SpawnForTest(in RagdollDeath d, RagdollLook look, double now, Vector3 color, HatDef? hat = null)
    {
        _now = now;
        var body = Alloc(look == RagdollLook.Zombie);
        body.Look = look;
        body.Owner = d.Victim;
        body.Hat = hat;
        if (look == RagdollLook.Soldier)
            BuildSoldierLook(body, "Test", d.Victim, color);
        else
            (body.ZShirt, body.ZSkin, body.ZPants) = (ZombieShirt, ZombieSkin, ZombiePants);
        Array.Clear(body.Blood); // clean unless the test sets some (SetBlood)
        body.BloodSeed = 0;
        Spawn(body, d, now, look == RagdollLook.Zombie ? ZombieLife : PlayerLife);
        return body;
    }

    private static Vector3 Jitter(ref Foliage.Rng rng, float amount) =>
        new Vector3(rng.Next() - 0.5f, rng.Next() - 0.5f, rng.Next() - 0.5f) * (2f * amount);

    private static readonly float[] BodyShot = [0.6f, 0.6f, 1f, 1f, 1f, 1f, 0.8f, 0.6f, 0.6f, 0.6f, 0.6f, 0.3f, 0.15f, 0.3f, 0.15f];
    private static readonly float[] HeadShot = [0.35f, 0.35f, 0.7f, 0.7f, 0.5f, 0.5f, 2.4f, 0.4f, 0.4f, 0.4f, 0.4f, 0.2f, 0.1f, 0.2f, 0.1f];

    /// <summary>The kill's push: knees give way and a little seeded tumble for every death, then the shot, the swing or the blast.</summary>
    private static void ApplyDeath(RagdollBody body, in RagdollDeath d, ref Foliage.Rng rng)
    {
        var sim = body.Sim;
        var fwd = sim.Forward();
        for (var i = 0; i < RagdollRig.Count; i++)
            sim.Push(i, Jitter(ref rng, 0.35f));
        sim.Push(RagdollRig.KneeR, fwd * (0.8f + rng.Next() * 0.6f));
        sim.Push(RagdollRig.KneeL, fwd * (0.8f + rng.Next() * 0.6f));
        var hatOff = d.Headshot || rng.Next() < 0.3f;

        if (d.BlastStrength > 0)
        {
            body.HasBlast = true;
            body.BlastAt = d.BlastAt;
            BlastBody(body, d.BlastAt, d.BlastRadius, d.BlastStrength, true);
            hatOff = true;
        }
        else if (d.HasSource)
        {
            var chest = (sim.Pos[RagdollRig.ShoulderR] + sim.Pos[RagdollRig.ShoulderL] + sim.Pos[RagdollRig.Belly] + sim.Pos[RagdollRig.Back]) * 0.25f;
            if (d.KillId == Game.MeleeKillId && d.Killer != 255)
            {
                // A shove: flat away from the swing, a little up; the upper body takes it.
                var away = chest - d.Source;
                away.Y = 0;
                away = away.LengthSquared() > 1e-4f ? Vector3.Normalize(away) : -fwd;
                away = Vector3.Normalize(away + new Vector3(0, 0.3f, 0));
                for (var i = 0; i < RagdollRig.Count; i++)
                    sim.Push(i, away * (i <= RagdollRig.Head ? 3.2f : 1.2f));
            }
            else if (d.KillId != Game.SuicideKillId)
            {
                var target = d.Headshot ? sim.Pos[RagdollRig.Head] : chest;
                var dir = target - d.Source;
                dir = dir.LengthSquared() > 1e-4f ? Vector3.Normalize(dir) : -fwd;
                var power = d.GunPower > 0 ? d.GunPower : 2f;
                var w = d.Headshot ? HeadShot : BodyShot;
                for (var i = 0; i < RagdollRig.Count; i++)
                    sim.Push(i, dir * (power * w[i]));
                if (d.Headshot)
                    sim.Push(RagdollRig.Head, new Vector3(0, power * 0.4f, 0)); // the head snaps back and up
            }
        }
        if (hatOff && body.Hat != null && body.Look == RagdollLook.Soldier)
            PopHat(body, ref rng);
    }

    // ---- Blasts -------------------------------------------------------------------------------------------------

    /// <summary>
    /// A blast on every PC (grenade, exploding prop, strike): flings the bodies around it outwards, waking sleeping ones.
    /// Walls shield bodies too. A body just killed by this very blast already got it.
    /// </summary>
    public static void Blast(Vector3 center, float radius, float strength, double now)
    {
        if (!Enabled || !(strength > 0) || !(radius > 0))
            return;
        foreach (var b in Bodies)
        {
            if (!b.Live || b.HasBlast && now - b.Born < 0.35 && Vector3.DistanceSquared(b.BlastAt, center) < 1f)
                continue;
            BlastBody(b, center, radius, strength, false);
        }
    }

    /// <summary>Pushes one body's particles away from the blast (more near it). <paramref name="killed"/>: the kill's own blast (no line-of-sight test, always some push).</summary>
    private static bool BlastBody(RagdollBody body, Vector3 center, float radius, float strength, bool killed)
    {
        var sim = body.Sim;
        if (body.Gibbed)
        {
            // Only a hat left: a resting one flies again.
            if (!body.HatOff || !body.HatResting || Vector3.Distance(body.HatPos, center) > radius)
                return false;
            body.HatResting = false;
            body.HatVel = Vector3.Normalize(body.HatPos - center + new Vector3(0, 1f, 0)) * (strength * 0.6f);
            body.CacheCount = -1;
            return true;
        }
        var pelvis = sim.Pelvis;
        if (Vector3.Distance(pelvis, center) > radius + 1.5f)
            return false;
        if (!killed && !Knockback.InSight(Physics, center, pelvis + new Vector3(0, 0.3f, 0)))
            return false;
        for (var i = 0; i < RagdollRig.Count; i++)
        {
            var to = sim.Pos[i] - center;
            var dist = to.Length();
            var f = Math.Clamp(1f - dist / (radius + 0.5f), 0f, 1f);
            if (killed)
                f = MathF.Max(f, 0.2f);
            if (f <= 0)
                continue;
            var dir = dist > 0.05f ? to / dist : Vector3.UnitY;
            sim.Push(i, Vector3.Normalize(dir + (GravityFrames.Curved ? GravityFrames.UpAt(sim.Pos[i]) * 0.5f : new Vector3(0, 0.5f, 0))) * (strength * (0.3f + 0.7f * f)));
        }
        body.CacheCount = -1;
        if (body.Hat != null && !body.HatOff)
        {
            var rng = new Foliage.Rng((uint)body.Order * 2654435761u);
            PopHat(body, ref rng);
        }
        else if (body.HatOff && body.HatResting)
        {
            body.HatResting = false; // the hat flies again
            body.HatVel = Vector3.Normalize(body.HatPos - center + new Vector3(0, 1f, 0)) * (strength * 0.6f);
        }
        return true;
    }

    // ---- Hats ---------------------------------------------------------------------------------------------------

    private static Vector3 HeadSize => BodyModel.Stances[0].HeadSize;

    /// <summary>Knocks the hat off: it leaves the head with the head's speed, a hop and a spin.</summary>
    private static void PopHat(RagdollBody body, ref Foliage.Rng rng)
    {
        var sim = body.Sim;
        HeadFrame(sim.Pos, out var head);
        var frame = Matrix4x4.CreateTranslation(0, HeadSize.Y / 2, 0) * head;
        body.HatOff = true;
        body.HatResting = false;
        body.HatLand = 0;
        body.HatPos = frame.Translation;
        body.HatRot = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(frame));
        body.HatVel = sim.Velocity(RagdollRig.Head) * 1.1f + new Vector3((rng.Next() - 0.5f) * 1.5f, 2.2f + rng.Next(), (rng.Next() - 0.5f) * 1.5f);
        var axis = new Vector3(rng.Next() - 0.5f, rng.Next() - 0.5f, rng.Next() - 0.5f);
        body.HatAxis = axis.LengthSquared() > 1e-4f ? Vector3.Normalize(axis) : Vector3.UnitX;
        body.HatSpin = 5f + rng.Next() * 6f;
        body.CacheCount = -1;
    }

    /// <summary>One step of a flying hat: gravity, spin, and a short ray along its path; on something walkable it lies down on its side.</summary>
    private static void StepHat(RagdollBody b, PhysicsWorld world)
    {
        if (b.Hat == null || !b.HatOff)
            return;
        if (b.HatResting)
        {
            if (b.HatLand < 1f)
            {
                b.HatLand = MathF.Min(1f, b.HatLand + Ragdoll.Step / 0.15f);
                b.CacheCount = -1;
            }
            return;
        }
        if (GravityFrames.Curved)
            b.HatVel += GravityFrames.GravityAt(b.HatPos) * Ragdoll.Step; // a planet: towards its centre
        else
            b.HatVel.Y -= World.Gravity * Ragdoll.Step * World.GravityScale;
        var move = b.HatVel * Ragdoll.Step;
        var len = move.Length();
        if (len > 1e-5f && world.Raycast(b.HatPos, move / len, len + 0.06f, QueryFilter.All, out var hit))
        {
            if (GravityFrames.Curved && Vector3.Dot(hit.Normal, GravityFrames.UpAt(hit.Point)) > 0.5f)
            {
                // A planet: rest on the curved ground, upright to it.
                var upH = GravityFrames.UpAt(hit.Point);
                b.HatResting = true;
                b.HatRestPos = hit.Point + upH * MathF.Max(-b.Hat.Min.X, b.Hat.Max.X);
                b.HatRestRot = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateRotationZ(-MathF.PI / 2) * GravityFrames.Basis(hit.Point)));
                b.HatPos = hit.Point + upH * 0.05f;
                b.HatLand = 0;
                return;
            }
            if (!GravityFrames.Curved && hit.Normal.Y > 0.5f)
            {
                var rest = MathF.Max(-b.Hat.Min.X, b.Hat.Max.X);
                var e = Vector3.Transform(Vector3.UnitZ, b.HatRot);
                var yaw = MathF.Atan2(e.X, e.Z);
                b.HatResting = true;
                b.HatRestPos = hit.Point + new Vector3(0, rest, 0);
                b.HatRestRot = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateRotationZ(-MathF.PI / 2) * Matrix4x4.CreateRotationY(yaw));
                b.HatPos = hit.Point + new Vector3(0, 0.05f, 0);
                b.HatLand = 0;
                return;
            }
            b.HatVel = Vector3.Reflect(b.HatVel, hit.Normal) * 0.3f; // off a wall
            return;
        }
        b.HatPos += move;
        b.HatRot = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(b.HatAxis, b.HatSpin * Ragdoll.Step) * b.HatRot);
        if (b.HatPos.Y < -500f)
            b.HatResting = true;
    }

    // ---- Drawing --------------------------------------------------------------------------------------------------

    /// <summary>Where boxes go: the instance buffer (16 floats each, like <see cref="PropDebris.Write"/>) or straight to the renderer.</summary>
    private struct Sink
    {
        public float[]? Out;
        public int Start, Max, Count;
        public Renderer? G;
        public float Fade;

        /// <summary>Blood amount for the next boxes (negative: zombie ichor) and the body's pattern seed (instance floats 15 and 11).</summary>
        public float Blood, Seed;

        public void Box(Vector3 size, Vector3 center, in Matrix4x4 frame, Vector3 color)
        {
            if (G != null)
            {
                G.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(center) * frame, color);
                Count++;
                return;
            }
            if (Out == null || Count >= Max)
                return;
            var o = (Start + Count) * PropDebris.FloatsPerPiece;
            var c = Vector3.Transform(center, frame);
            var q = Quaternion.CreateFromRotationMatrix(frame);
            Write(o, c, q, size, color);
        }

        /// <summary>A box with a full local matrix (scale, rotation, translation; hat boxes) under a rigid frame.</summary>
        public void Box(in Matrix4x4 local, in Matrix4x4 frame, Vector3 color)
        {
            if (G != null)
            {
                G.Draw(local * frame, color);
                Count++;
                return;
            }
            if (Out == null || Count >= Max || !Matrix4x4.Decompose(local, out var scale, out var rot, out var at))
                return;
            var o = (Start + Count) * PropDebris.FloatsPerPiece;
            var keep = Blood;
            Blood = 0; // hats stay clean
            Write(o, Vector3.Transform(at, frame), Quaternion.Concatenate(rot, Quaternion.CreateFromRotationMatrix(frame)), scale, color);
            Blood = keep;
        }

        private void Write(int o, Vector3 c, Quaternion q, Vector3 size, Vector3 color)
        {
            var s = Out!.AsSpan(o, PropDebris.FloatsPerPiece);
            s[0] = c.X;
            s[1] = c.Y;
            s[2] = c.Z;
            s[3] = Fade;
            s[4] = q.X;
            s[5] = q.Y;
            s[6] = q.Z;
            s[7] = q.W;
            s[8] = size.X;
            s[9] = size.Y;
            s[10] = size.Z;
            s[11] = Seed;
            s[12] = color.X;
            s[13] = color.Y;
            s[14] = color.Z;
            s[15] = Blood;
            Count++;
        }
    }

    private static float FadeOf(RagdollBody b) => (float)Math.Clamp((b.Until - _now) / FadeTime, 0, 1);

    /// <summary>
    /// Appends every body's boxes to the instance data after the first <paramref name="at"/> pieces (debris); returns the new
    /// count. A body that is completely still copies its cached boxes; a moving one is posed between its last two steps.
    /// </summary>
    public static int WriteInstances(float[] into, int at)
    {
        if (!Enabled)
            return at;
        const int f = PropDebris.FloatsPerPiece;
        foreach (var b in Bodies)
        {
            if (!b.Live)
                continue;
            var room = into.Length / f - at;
            if (room <= 0)
                break;
            var fade = FadeOf(b);
            if (b.Still)
            {
                if (b.CacheCount < 0)
                {
                    var cache = new Sink { Out = b.Cache, Max = RagdollBody.MaxBoxes, Fade = 1f };
                    Emit(b, 1f, ref cache);
                    b.CacheCount = cache.Count;
                }
                var n = Math.Min(b.CacheCount, room);
                Array.Copy(b.Cache, 0, into, at * f, n * f);
                if (fade < 1f)
                    for (var k = 0; k < n; k++)
                        into[(at + k) * f + 3] = fade;
                at += n;
                continue;
            }
            var sink = new Sink { Out = into, Start = at, Max = room, Fade = fade };
            Emit(b, b.Sim.Asleep ? 1f : _alpha, ref sink);
            at += sink.Count;
        }
        return at;
    }

    /// <summary>Fallback when the instanced path is unavailable: draws every body box by box (no fade: gone at half-faded).</summary>
    public static int DrawImmediate(Renderer g)
    {
        if (!Enabled)
            return 0;
        var n = 0;
        foreach (var b in Bodies)
        {
            if (!b.Live || FadeOf(b) < 0.5f)
                continue;
            var sink = new Sink { G = g, Fade = 1f };
            Emit(b, b.Sim.Asleep ? 1f : _alpha, ref sink);
            n += sink.Count;
        }
        return n;
    }

    /// <summary>Self-test: how many boxes a body draws (written into a scratch buffer), and the first one's centre and fade.</summary>
    public static int CountBoxes(RagdollBody b, float[] scratch)
    {
        var sink = new Sink { Out = scratch, Max = scratch.Length / PropDebris.FloatsPerPiece, Fade = 1f };
        Emit(b, 1f, ref sink);
        return sink.Count;
    }

    /// <summary>A rigid frame from three orthonormal axes and an origin (row vectors, like <see cref="Arms.BoneFrame"/>).</summary>
    private static Matrix4x4 Frame(Vector3 x, Vector3 y, Vector3 z, Vector3 at) =>
        new(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, at.X, at.Y, at.Z, 1);

    /// <summary>The head's frame: at the head particle, up from the shoulders' midpoint through it, right like the torso.</summary>
    private static void HeadFrame(ReadOnlySpan<Vector3> q, out Matrix4x4 head)
    {
        Ragdoll.TorsoAxes(q, out var right, out var up, out _);
        var neck = (q[RagdollRig.ShoulderR] + q[RagdollRig.ShoulderL]) * 0.5f;
        var hu = q[RagdollRig.Head] - neck;
        hu = hu.LengthSquared() > 1e-8f ? Vector3.Normalize(hu) : up;
        var hr = right - hu * Vector3.Dot(right, hu);
        hr = hr.LengthSquared() > 1e-8f ? Vector3.Normalize(hr) : right;
        head = Frame(hr, hu, Vector3.Cross(hr, hu), q[RagdollRig.Head]);
    }

    private static readonly Matrix4x4[] Bones = new Matrix4x4[3];

    /// <summary>All of a body's boxes, posed <paramref name="alpha"/> of the way from its last step to its current one.</summary>
    private static void Emit(RagdollBody b, float alpha, ref Sink sink)
    {
        if (b.Gibbed)
        {
            // Blown apart (the gibs draw themselves): only the hat, flying or lying on its side.
            if (b.Hat is { } gibHat && b.HatOff)
            {
                var land = b.HatResting ? b.HatLand : 0f;
                var gf = Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(b.HatRot, b.HatRestRot, land));
                gf.Translation = b.HatResting ? Vector3.Lerp(b.HatPos, b.HatRestPos, land) : b.HatPos;
                foreach (var box in gibHat.Boxes)
                    sink.Box(box.Local, gf, box.Color);
            }
            return;
        }
        var sim = b.Sim;
        Span<Vector3> q = stackalloc Vector3[RagdollRig.Count];
        for (var i = 0; i < RagdollRig.Count; i++)
            q[i] = alpha >= 1f ? sim.Pos[i] : Vector3.Lerp(sim.Last[i], sim.Pos[i], alpha);
        Ragdoll.TorsoAxes(q, out var right, out var up, out var back);
        var midHip = (q[RagdollRig.HipR] + q[RagdollRig.HipL]) * 0.5f;
        var midSh = (q[RagdollRig.ShoulderR] + q[RagdollRig.ShoulderL]) * 0.5f;
        var fwd = -back;
        var zombie = b.Look == RagdollLook.Zombie;
        var bl = b.Blood;
        float Bleed(BloodPart part) => zombie ? -bl[(int)part] : bl[(int)part];
        sink.Seed = b.BloodSeed;

        // Legs (both looks): thigh, shin and a foot pointing the way the body faces.
        var bones = Bones;
        for (var side = 0; side < 2; side++)
        {
            var hip = q[RagdollRig.HipR + side];
            var knee = q[RagdollRig.KneeR + side * 2];
            var foot = q[RagdollRig.FootR + side * 2];
            bones[0] = Arms.BoneFrame(hip, knee, fwd);
            bones[1] = Arms.BoneFrame(knee, foot, fwd);
            var shinUp = knee - foot;
            shinUp = shinUp.LengthSquared() > 1e-8f ? Vector3.Normalize(shinUp) : up;
            var toes = fwd - shinUp * Vector3.Dot(fwd, shinUp);
            toes = toes.LengthSquared() > 1e-6f ? Vector3.Normalize(toes) : -up;
            bones[2] = Arms.BoneFrame(foot, foot + toes, shinUp);
            sink.Blood = Bleed(side == 0 ? BloodPart.LegR : BloodPart.LegL);
            if (b.Look == RagdollLook.Zombie)
            {
                var thigh = Vector3.Distance(hip, knee);
                var shin = Vector3.Distance(knee, foot);
                sink.Box(new Vector3(0.22f, 0.24f, thigh + 0.08f), new Vector3(0, 0, -thigh / 2), bones[0], b.ZPants);
                sink.Box(new Vector3(0.2f, 0.22f, shin + 0.06f), new Vector3(0, 0, -shin / 2), bones[1], b.ZPants * 0.9f);
                continue;
            }
            foreach (var part in LegAnimator.LegParts)
                sink.Box(part.Size, part.Center, bones[part.Bone - 1], b.Legs.Of(part.Paint));
        }

        // Arms: upper arm and forearm from shoulder, elbow and hand, the elbow's pole outwards and down.
        for (var side = 0; side < 2; side++)
        {
            var sgn = side == 0 ? 1f : -1f;
            var sh = q[RagdollRig.ShoulderR + side];
            var el = q[RagdollRig.ElbowR + side * 2];
            var ha = q[RagdollRig.HandR + side * 2];
            var pole = right * sgn - up * 0.8f + back * 0.3f;
            var ua = Arms.BoneFrame(sh, el, pole);
            var fa = Arms.BoneFrame(el, ha, pole);
            float u = RagdollRig.UpperArm, fl = RagdollRig.Forearm;
            sink.Blood = Bleed(side == 0 ? BloodPart.ArmR : BloodPart.ArmL);
            if (b.Look == RagdollLook.Zombie)
            {
                var skin = b.ZSkin * 0.92f;
                sink.Box(new Vector3(0.13f, 0.13f, u + 0.04f), new Vector3(0, 0, -u / 2), ua, skin);
                sink.Box(new Vector3(0.12f, 0.12f, fl + 0.06f), new Vector3(0, 0, -fl / 2), fa, skin);
                continue;
            }
            const float t = 0.11f, k = 1.3f;
            var c = b.Arms;
            sink.Box(new Vector3(t, t, u + t * 0.3f), new Vector3(0, 0, -u * 0.5f), ua, c.Sleeve);
            sink.Box(new Vector3(t * 0.95f), Vector3.Zero, fa, c.Sleeve * 0.92f); // elbow
            sink.Box(new Vector3(t * 0.84f, t * 0.8f, fl * 0.8f), new Vector3(0, 0, -fl * 0.4f), fa, c.Sleeve);
            sink.Box(new Vector3(t * 0.9f, t * 0.86f, fl * 0.26f), new Vector3(0, 0, -fl * 0.87f), fa, c.Cuff);
            var wrist = Arms.Wrists[(int)HandShape.Flat];
            var hand = fa;
            hand.Translation = ha - Vector3.TransformNormal(new Vector3(wrist.X * sgn, wrist.Y, wrist.Z) * k, fa);
            foreach (var p in Arms.Shapes[(int)HandShape.Flat])
                sink.Box(p.Size * k, new Vector3(p.Center.X * sgn, p.Center.Y, p.Center.Z) * k, hand, p.Finger ? c.Fingers : c.Glove);
        }

        if (b.Look == RagdollLook.Zombie)
        {
            // Torn shirt, belly and head ride on the hips (Program.DrawZombie's upper-body frame).
            var upper = Frame(right, up, back, midHip);
            var zc = BodyColors.Zombie(b.ZShirt, b.ZSkin);
            foreach (var p in BodyModel.ZombieParts)
            {
                sink.Blood = Bleed(p.Center.Y > 0.6f ? BloodPart.Head : bl[(int)BloodPart.Front] >= bl[(int)BloodPart.Back] ? BloodPart.Front : BloodPart.Back);
                sink.Box(p.Size, p.Center, upper, zc.Of(p.Paint));
            }
            sink.Blood = 0;
            return;
        }

        // Pelvis, torso and head (the living body's parts, stance 0).
        var pp = LegAnimator.PelvisPart;
        sink.Blood = 0.5f * MathF.Max(bl[(int)BloodPart.LegR], bl[(int)BloodPart.LegL]);
        sink.Box(pp.Size, pp.Center, Frame(right, up, back, midHip + up * 0.04f), b.Legs.Of(pp.Paint));
        var torso = Frame(right, up, back, Vector3.Lerp(midHip, midSh, RagdollRig.TorsoAlong));
        EmitParts(b.Torso, torso, ref sink, bl, torsoParts: true);
        HeadFrame(q, out var head);
        EmitParts(b.Head, head, ref sink, bl, torsoParts: false);
        sink.Blood = 0;

        // The hat: on the head, flying, or lying on its side.
        if (b.Hat is not { } hat)
            return;
        Matrix4x4 hf;
        if (!b.HatOff)
            hf = Matrix4x4.CreateTranslation(0, HeadSize.Y / 2, 0) * head;
        else
        {
            var land = b.HatResting ? b.HatLand : 0f;
            hf = Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(b.HatRot, b.HatRestRot, land));
            hf.Translation = b.HatResting ? Vector3.Lerp(b.HatPos, b.HatRestPos, land) : b.HatPos;
        }
        foreach (var box in hat.Boxes)
            sink.Box(box.Local, hf, box.Color);
    }

    private static void EmitParts(BodyParts parts, in Matrix4x4 frame, ref Sink sink, float[] blood, bool torsoParts)
    {
        float front = blood[(int)BloodPart.Front], backB = blood[(int)BloodPart.Back];
        var sides = 0.6f * MathF.Max(blood[(int)BloodPart.ArmL], blood[(int)BloodPart.ArmR]);
        for (var i = 0; i < parts.Count; i++)
        {
            ref readonly var l = ref parts.Local[i];
            // Torso boxes: front plate and pouches take the front's blood, the back plate the back's, the rest the worse of both (and the arms');
            // head boxes the head's.
            var z = l.Translation.Z;
            sink.Blood = !torsoParts ? blood[(int)BloodPart.Head] : z < -0.12f ? front : z > 0.12f ? backB : MathF.Max(MathF.Max(front, backB), sides);
            sink.Box(new Vector3(l.M11, l.M22, l.M33), l.Translation, frame, parts.Draws[i].Color);
        }
    }

    // ---- Stats --------------------------------------------------------------------------------------------------

    /// <summary>One line for the smoke log: bodies, awake, steps and the simulation's CPU per frame since the last read.</summary>
    public static string Stats
    {
        get
        {
            var fr = Math.Max(_frames, 1);
            var s = $"{LiveCount} bodies ({AwakeCount} awake), {_spawned} spawned, {_steps} steps in {_frames} frames, sim {_simTicks * 1000.0 / Stopwatch.Frequency / fr:0.000} ms/frame avg, "
                    + $"{_worstSimTicks * 1000.0 / Stopwatch.Frequency:0.000} ms worst, {Ragdoll.Gathers} gathers";
            _simTicks = _worstSimTicks = 0;
            _frames = _steps = _spawned = 0;
            return s;
        }
    }
}
