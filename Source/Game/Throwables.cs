using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

public enum ThrowableKind : byte
{
    None,
    Frag, // timed fuse, bounces, big blast
    Impact, // explodes on first contact, smaller blast
    Smoke, // pops a cloud that blocks vision (and bots' line of sight)
    Recon, // equipment: sticks where it lands and reveals nearby enemies to its owner through walls

    // Tactical equipment added later (TacticalGear.cs, GameTactical.cs): appended so saved names and packet bytes never move.
    Flash, // thrown: blinds and deafens whoever faces it
    Stun, // thrown, goes off on contact: slows movement, turning and aiming
    Decoy, // thrown: fake gunfire and a fake name tag where it lands
    MotionSensor, // thrown, sticks: pings enemies moving nearby to its owner's team
    Trophy, // placed: shoots down incoming grenades (limited charges)
    Cover, // placed: an energy wall that stops bullets and blasts
    Insertion, // placed: your next spawn point
    Stim, // used on yourself: instant heal and a short regen boost
}

public sealed class ThrowableDef
{
    public required ThrowableKind Kind;
    public required string Name;
    public required string Blurb;
    public float Fuse; // seconds until it goes off (Frag/Smoke), max flight time otherwise
    /// <summary>Launch speed along the aim direction (m/s); <see cref="Throwables.ThrowUp"/> and half the thrower's velocity are added on top.</summary>
    public float ThrowSpeed = Throwables.ThrowSpeed;
    public float Radius; // blast / cloud / reveal radius
    public float MaxDamage;
    /// <summary>Damage falls off as (1 - distance / Radius) to this power: 1 is a straight line, higher drops off faster.</summary>
    public float Falloff = 1.4f;
    /// <summary>Blast knockback: speed (m/s) added at the centre, falling off to nothing at <see cref="Radius"/> (0 = no push; Knockback.cs).</summary>
    public float Knockback;
    public int PerLife;
    public Vector3 Color;
    public bool IsEquipment => Kind == ThrowableKind.Recon || Kind >= ThrowableKind.Flash; // every tactical item goes in the Equipment slot
}

public static class Throwables
{
    /// <summary>
    /// The standard throw speed (m/s), each <see cref="ThrowableDef.ThrowSpeed"/>'s default. 22.5 carries about
    /// 34 m on flat ground at the best angle (about 40 degrees up; 45 degrees is nearly as far), 1.6x the old 17 m/s
    /// (about 21 m). The "Grenades" self-test measures it.
    /// </summary>
    public const float ThrowSpeed = 22.5f;
    /// <summary>Extra upward speed (m/s) every throw gets, so a level throw still arcs a little.</summary>
    public const float ThrowUp = 2.5f;
    /// <summary>A smoke grenade still in the air at its fuse pops when it first lands, or at this age at the latest (s).</summary>
    public const float SmokeMaxFlight = 4f;
    public const float Restitution = 0.35f;
    public const float ProjectileRadius = 0.1f;
    public const float SmokeDuration = 14f;
    public const float ReconDuration = 12f;
    public const byte KillIdBase = 240; // kill-feed "weapon" ids 240+ are throwables

    public static readonly ThrowableDef[] All =
    [
        new() { Kind = ThrowableKind.None, Name = "None", Blurb = "Nothing in this slot." },
        new() { Kind = ThrowableKind.Frag, Name = "Frag Grenade", Blurb = "2.5 s fuse, bounces off walls, throws about 34 m. Kills within about 4 m, hurts out to 9 m.",
            Fuse = 2.5f, Radius = 9f, MaxDamage = 200, Falloff = 1f, PerLife = 2, Color = new(0.2f, 0.35f, 0.15f), Knockback = 11f },
        new() { Kind = ThrowableKind.Impact, Name = "Impact Grenade", Blurb = "Explodes the moment it touches anything. Smaller blast: kills within about 2.5 m, hurts out to 6 m.",
            Fuse = 6f, Radius = 6f, MaxDamage = 170, Falloff = 1f, PerLife = 2, Color = new(0.75f, 0.2f, 0.15f), Knockback = 9f },
        new() { Kind = ThrowableKind.Smoke, Name = "Smoke Grenade", Blurb = "Pops where it lands: a thick 5 m cloud for 14 s. Blocks vision, and bots can't see through it.",
            Fuse = 1.2f, Radius = 5f, PerLife = 2, Color = new(0.6f, 0.6f, 0.62f) },
        new() { Kind = ThrowableKind.Recon, Name = "Recon Pulse", Blurb = "Throw it anywhere. For 12 s it reveals every enemy within 18 m to you, through walls, in their team colour.",
            Fuse = 8f, Radius = 18f, PerLife = 1, Color = new(0.2f, 0.85f, 0.95f) },
        .. TacticalGear.Defs, // Flashbang .. Stim, in enum order (TacticalGear.cs)
    ];

    /// <summary>Blast damage at <paramref name="dist"/> metres from the centre (before the self and prone multipliers).</summary>
    public static float BlastDamage(ThrowableDef def, float dist) =>
        dist >= def.Radius ? 0f : def.MaxDamage * MathF.Pow(1f - dist / def.Radius, def.Falloff);

    /// <summary>Furthest distance (m) at which the blast still does <paramref name="hp"/> damage.</summary>
    public static float LethalRange(ThrowableDef def, float hp) =>
        hp >= def.MaxDamage ? 0f : def.Radius * (1f - MathF.Pow(hp / def.MaxDamage, 1f / def.Falloff));

    /// <summary>Launch velocity of a throw along <paramref name="dir"/> (unit) by someone moving at <paramref name="throwerVel"/>.</summary>
    public static Vector3 LaunchVelocity(ThrowableDef def, Vector3 dir, Vector3 throwerVel) =>
        dir * def.ThrowSpeed + new Vector3(0, ThrowUp, 0) + throwerVel * 0.5f;

    /// <summary>Where a throw starts: just in front of the eye.</summary>
    public static Vector3 LaunchPoint(Vector3 eye, Vector3 dir) => eye + dir * 0.4f;

    public static ThrowableDef Get(ThrowableKind k) => All[(int)k < All.Length ? (int)k : 0];

    public static IEnumerable<ThrowableDef> Grenades => All.Where(d => d.Kind is ThrowableKind.Frag or ThrowableKind.Impact or ThrowableKind.Smoke);

    public static string KillName(byte killId) =>
        killId == Game.MeleeKillId ? "Melee" : killId == Game.SuicideKillId ? "Suicide" : killId == Game.LandmineKillId ? "Landmine" : killId >= KillIdBase ? Get((ThrowableKind)(killId - KillIdBase)).Name
        : KillStreaks.IsStreakKill(killId) ? KillStreaks.Get(KillStreaks.FromKillId(killId)).Name : Weapons.Get(killId).Name;

    /// <summary>Point-vs-box test for a small projectile: static colliders, then movers at their current pose (the terrain is handled by <see cref="TerrainContact"/>).</summary>
    private static bool InsideSolid(Vector3 p) =>
        World.Query.PointInside(p, ProjectileRadius, new QueryFilter(QueryLayers.StaticShapes | QueryLayers.Movers | QueryLayers.Boundary));

    /// <summary>One tick of ballistic flight with bounces. Returns true if it touched geometry.</summary>
    public static bool Step(Projectile p, float dt) => Step(p, dt, float.NaN);

    /// <summary>
    /// <see cref="Step(Projectile, float)"/>, or with a number for <paramref name="flatGround"/> the same flight over an
    /// endless flat floor at that height and nothing else (no map needed): used to aim bot throws.
    /// </summary>
    private static bool Step(Projectile p, float dt, float flatGround)
    {
        var flat = !float.IsNaN(flatGround);
        if (p.Stuck)
            return false;
        var touched = false;
        const int sub = 3;
        var h = dt / sub;
        for (var i = 0; i < sub; i++)
        {
            p.Vel.Y -= World.Gravity * h;
            var pos = p.Pos;

            pos.X += p.Vel.X * h;
            if (flat ? pos.Y < flatGround + ProjectileRadius : InsideSolid(pos))
            {
                pos.X = p.Pos.X;
                p.Vel.X *= -Restitution;
                touched = true;
            }
            pos.Z += p.Vel.Z * h;
            if (flat ? pos.Y < flatGround + ProjectileRadius : InsideSolid(pos))
            {
                pos.Z = p.Pos.Z;
                p.Vel.Z *= -Restitution;
                touched = true;
            }
            pos.Y += p.Vel.Y * h;
            if (flat ? pos.Y < flatGround + ProjectileRadius : InsideSolid(pos))
            {
                pos.Y = p.Pos.Y;
                var landing = p.Vel.Y < 0;
                p.Vel.Y *= -Restitution;
                if (landing)
                {
                    p.Vel.X *= 0.7f; // friction on the ground
                    p.Vel.Z *= 0.7f;
                    if (MathF.Abs(p.Vel.Y) < 1.2f)
                        p.Vel.Y = 0;
                }
                touched = true;
            }
            if (flat)
            {
                p.Pos = pos;
                continue;
            }
            pos.X = Math.Clamp(pos.X, -World.Map.HalfX, World.Map.HalfX);
            pos.Z = Math.Clamp(pos.Z, -World.Map.HalfZ, World.Map.HalfZ);
            if (World.Map.Boundary is { } boundary && boundary.KeepInside(ref pos, ref p.Vel, ProjectileRadius, Restitution))
                touched = true; // over the barrier: bounced back in off the outline
            if (World.Map.TerrainCollision is { } terrain && TerrainContact(p, ref pos, terrain, h))
                touched = true;
            p.Pos = pos;
        }
        return touched;
    }

    /// <summary>
    /// Flies a throw over flat ground at height <paramref name="groundY"/> until it goes off (the same rules as the
    /// server: a frag at its fuse, impact and recon on first contact, smoke at its fuse once it has landed). Returns
    /// where it goes off and when; <paramref name="firstTouch"/> is where it first hit the ground.
    /// </summary>
    public static Vector3 PredictFlat(ThrowableDef def, Vector3 origin, Vector3 vel, float groundY, out Vector3 firstTouch, out float time)
    {
        var p = new Projectile { Kind = def.Kind, Pos = origin, Vel = vel };
        var touched = false;
        firstTouch = origin;
        for (var tick = 1; tick <= 10 * World.TickRate; tick++)
        {
            var hit = Step(p, World.TickDt, groundY);
            if (hit && !touched)
                firstTouch = p.Pos;
            touched |= hit;
            time = tick * World.TickDt;
            if (GoesOff(def, time, hit, touched))
                return p.Pos;
        }
        time = 10;
        return p.Pos;
    }

    /// <summary>Does a projectile go off now? <paramref name="age"/> in seconds, <paramref name="touched"/> this tick, <paramref name="landed"/> ever.</summary>
    public static bool GoesOff(ThrowableDef def, double age, bool touched, bool landed) => def.Kind switch
    {
        ThrowableKind.Impact or ThrowableKind.Recon or ThrowableKind.Stun or ThrowableKind.Decoy or ThrowableKind.MotionSensor => touched || age > def.Fuse,
        ThrowableKind.Smoke => (age >= def.Fuse && landed) || age >= SmokeMaxFlight,
        _ => age >= def.Fuse,
    };

    /// <summary>
    /// The aim pitch (radians, up positive) for a throw from <paramref name="eye"/> that goes off as close as it can to
    /// <paramref name="target"/> (its feet), treating the ground as flat at the target's height and the thrower as
    /// standing still. Picks the flat arc (the fastest), allowing for bounces and roll, so a frag thrown short rolls
    /// up to the target. Returns false (and the longest throw) when the target is out of range.
    /// </summary>
    public static bool AimPitch(ThrowableDef def, Vector3 eye, Vector3 target, out float pitch)
    {
        var to = target - eye;
        var want = new Vector2(to.X, to.Z).Length();
        var flatDir = want > 1e-3f ? new Vector2(to.X, to.Z) / want : new Vector2(0, -1);
        float Miss(float a, out float reach)
        {
            var dir = new Vector3(flatDir.X * MathF.Cos(a), MathF.Sin(a), flatDir.Y * MathF.Cos(a));
            var start = LaunchPoint(eye, dir);
            var end = PredictFlat(def, start, LaunchVelocity(def, dir, Vector3.Zero), target.Y, out _, out _);
            reach = new Vector2(end.X - eye.X, end.Z - eye.Z).Length();
            return MathF.Abs(reach - want);
        }
        // Coarse sweep from steeply down to about 50 degrees up, then refine around the best.
        pitch = 0f;
        var best = float.MaxValue;
        var longest = 0f;
        var longestPitch = 0f;
        for (var a = -0.92f; a <= 0.88f; a += 0.04f)
        {
            var miss = Miss(a, out var reach);
            if (miss < best)
                (best, pitch) = (miss, a);
            if (reach > longest)
                (longest, longestPitch) = (reach, a);
        }
        var centre = pitch;
        for (var a = centre - 0.04f; a <= centre + 0.04f; a += 0.004f)
        {
            var miss = Miss(a, out _);
            if (miss < best)
                (best, pitch) = (miss, a);
        }
        if (want > longest + 1f)
        {
            pitch = longestPitch;
            return false;
        }
        return true;
    }

    /// <summary>Rolling friction on terrain (deceleration = this x gravity x cos(slope)): grenades stop on slopes up to about 38 degrees.</summary>
    public const float RollFriction = 0.8f;

    /// <summary>
    /// Bounce off and roll along the terrain using its surface normal: hard hits bounce (losing speed), soft
    /// contact keeps it on the surface, and friction brings it to rest unless the slope is too steep.
    /// </summary>
    private static bool TerrainContact(Projectile p, ref Vector3 pos, ITerrainCollision terrain, float h)
    {
        var walls = false;
        if (terrain.HasVoids)
        {
            // Holes and caves: bounce off their walls and ceilings first, then the floor (or surface) is the ground below.
            walls = VoidWalls(p, ref pos, terrain);
            terrain = terrain.Layer(pos.Y);
        }
        return TerrainFloor(p, ref pos, terrain, h) || walls;
    }

    /// <summary>A hole's sides and a cave's walls and ceiling: like box faces, the axis that ran into them bounces back.</summary>
    private static bool VoidWalls(Projectile p, ref Vector3 pos, ITerrainCollision terrain)
    {
        const float r = ProjectileRadius;
        bool Solid(Vector3 q) => terrain.GroundBelow(q.X, q.Z, q.Y) > q.Y - r || terrain.CeilingAbove(q.X, q.Z, q.Y) < q.Y + r;
        if (!Solid(pos))
            return false;
        var old = p.Pos;
        var touched = false;
        if (Solid(new Vector3(pos.X, old.Y, old.Z)) && !Solid(old))
        {
            pos.X = old.X;
            p.Vel.X *= -Restitution;
            touched = true;
        }
        if (Solid(new Vector3(pos.X, old.Y, pos.Z)) && !Solid(old))
        {
            pos.Z = old.Z;
            p.Vel.Z *= -Restitution;
            touched = true;
        }
        var ceiling = terrain.CeilingAbove(pos.X, pos.Z, old.Y);
        if (pos.Y + r > ceiling && p.Vel.Y > 0)
        {
            pos.Y = MathF.Min(old.Y, ceiling - r);
            p.Vel.Y *= -Restitution;
            touched = true;
        }
        return touched;
    }

    private static bool TerrainFloor(Projectile p, ref Vector3 pos, ITerrainCollision terrain, float h)
    {
        var ground = terrain.HeightAt(pos.X, pos.Z) + ProjectileRadius;
        if (pos.Y >= ground)
            return false;
        pos.Y = ground;
        var n = terrain.NormalAt(pos.X, pos.Z);
        var vn = Vector3.Dot(p.Vel, n);
        if (vn >= 0)
            return false; // already leaving the surface
        if (vn < -1.2f)
        {
            var tangent = p.Vel - vn * n;
            p.Vel = tangent * 0.7f - vn * Restitution * n; // bounce, with the same ground friction as boxes
        }
        else
        {
            p.Vel -= vn * n; // resting contact: lose the into-the-ground part
            var tangent = p.Vel;
            var speed = tangent.Length();
            var slow = RollFriction * World.Gravity * n.Y * h;
            p.Vel = speed <= slow ? Vector3.Zero : tangent * (1 - slow / speed);
        }
        return true;
    }
}

public sealed class Projectile
{
    public ushort Id;
    public ThrowableKind Kind;
    public byte Owner;
    public Vector3 Pos;
    public Vector3 Vel;
    public double Born;
    public bool Stuck;
    public double LastBounceSound;
    /// <summary>Server: has it touched anything yet (a smoke grenade waits for this before it pops).</summary>
    public bool Landed;
}

public readonly record struct SmokeCloud(Vector3 Pos, double Born, double Until);

public readonly record struct ReconSensor(Vector3 Pos, byte Owner, double Born, double Until);

public readonly record struct Blast(Vector3 Pos, double Born, float Radius, ThrowableKind Kind);

public sealed partial class Game
{
    public readonly Dictionary<ushort, Projectile> Projectiles = new();
    public readonly List<SmokeCloud> Smokes = new();
    public readonly List<ReconSensor> Sensors = new();
    public readonly List<Blast> Blasts = new();
    private ushort _nextProjectileId = 1;

    // Local player's throwables (the server keeps the authoritative counts)
    public ThrowableKind ChosenGrenade = ThrowableKind.None;
    public ThrowableKind ChosenEquipment = ThrowableKind.None;
    public ThrowableKind LocalGrenade = ThrowableKind.None;
    public ThrowableKind LocalEquipment = ThrowableKind.None;
    public int GrenadesLeft;
    public int EquipmentLeft;
    private double _nextThrow;

    /// <summary>Screen shake from nearby explosions (0..1, decays).</summary>
    public float Shake;

    private void RefillThrowables()
    {
        LocalGrenade = ChosenGrenade;
        LocalEquipment = ChosenEquipment;
        if (!Module.UsesThrowables)
            LocalGrenade = LocalEquipment = ThrowableKind.None; // Gun Game: the gun and nothing else
        GrenadesLeft = Throwables.Get(LocalGrenade).PerLife;
        EquipmentLeft = Throwables.Get(LocalEquipment).PerLife;
    }

    /// <summary>Local player presses the grenade or equipment key.</summary>
    public void TryThrow(bool equipment, double now)
    {
        if (Local is not { Alive: true } me || now < _nextThrow || Phase != MatchPhase.Playing)
            return;
        var kind = equipment ? LocalEquipment : LocalGrenade;
        if (kind == ThrowableKind.None)
            return;
        if (equipment ? EquipmentLeft <= 0 : GrenadesLeft <= 0)
        {
            Audio.Effect(Sfx.DryFire, 0.5f);
            return;
        }
        if (equipment)
            EquipmentLeft--;
        else
            GrenadesLeft--;
        _nextThrow = now + 0.8;
        Audio.Effect(Sfx.Throw, 0.6f);

        var dir = World.Forward(LookYaw, LookPitch);
        var origin = Throwables.LaunchPoint(me.RenderEye, dir);
        if (IsServer)
        {
            ServerThrow(me, kind, origin, dir, now);
        }
        else
        {
            _w.Begin(Msg.Throw).U8((byte)kind).Vec(origin).Vec(dir);
            Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
        }
    }

    // ---- Server ---------------------------------------------------------------------

    private void ServerThrow(Player p, ThrowableKind kind, Vector3 origin, Vector3 dir, double now)
    {
        if (!p.Alive || now < p.NextThrow || kind == ThrowableKind.None || !DamageEnabled)
            return;
        if (kind == p.Grenade && p.GrenadesLeft > 0)
            p.GrenadesLeft--;
        else if (kind == p.Equipment && p.EquipmentLeft > 0)
            p.EquipmentLeft--;
        else
            return; // not in their loadout, or none left
        p.NextThrow = now + 0.7;
        if (TacticalGear.UseOf(kind) != TacticalUse.Thrown)
        {
            ServerUseTactical(p, kind, dir, now); // placed devices and the stim (GameTactical.cs)
            return;
        }

        if (Vector3.Distance(origin, p.Eye) > 3f || !float.IsFinite(origin.X + origin.Y + origin.Z))
            origin = p.Eye;
        if (World.Query.OverlapBody(origin - new Vector3(0, 0.05f, 0), 0.1f, QueryFilter.StaticOnly))
            origin = p.Eye; // don't spawn it inside a wall
        dir = dir.LengthSquared() > 0.5f ? Vector3.Normalize(dir) : World.Forward(p.Yaw, p.Pitch);

        var proj = new Projectile
        {
            Id = _nextProjectileId++,
            Kind = kind,
            Owner = p.Id,
            Pos = origin,
            Vel = Throwables.LaunchVelocity(Throwables.Get(kind), dir, p.Sim.Velocity),
            Born = now,
        };
        if (_nextProjectileId == 0)
            _nextProjectileId = 1;
        Projectiles[proj.Id] = proj;
        p.LastCombatTime = now;

        _w.Begin(Msg.ProjSpawn).U16(proj.Id).U8((byte)kind).U8(p.Id).Vec(proj.Pos).Vec(proj.Vel);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        if (p.Id != LocalId)
            Audio.Effect3D(Sfx.Throw, proj.Pos, 0.6f);
    }

    /// <summary>Server tick for every live projectile: fly, bounce, detonate.</summary>
    private void ServerTickProjectiles(double now)
    {
        List<Projectile>? done = null;
        foreach (var proj in Projectiles.Values)
        {
            var touched = Throwables.Step(proj, World.TickDt);
            if (touched)
                BounceSound(proj, now);
            var def = Throwables.Get(proj.Kind);
            proj.Landed |= touched;
            if (Throwables.GoesOff(def, now - proj.Born, touched, proj.Landed))
                (done ??= new()).Add(proj);
        }
        if (done == null)
            return;
        foreach (var proj in done)
        {
            Projectiles.Remove(proj.Id);
            ServerDetonate(proj, now);
        }
    }

    private void ServerDetonate(Projectile proj, double now)
    {
        var def = Throwables.Get(proj.Kind);
        byte flags = 0;
        Players.TryGetValue(proj.Owner, out var owner);

        if (proj.Kind is ThrowableKind.Frag or ThrowableKind.Impact && owner != null)
        {
            var center = proj.Pos + new Vector3(0, 0.15f, 0);
            var radius = def.Radius * Perks.BlastRadiusMult(owner.Perks); // Demolitions: wider, the falloff curve stretched with it
            foreach (var victim in Players.Values.ToList())
            {
                if (!victim.Alive)
                    continue;
                var to = victim.Chest - center;
                var dist = to.Length();
                if (dist > radius)
                    continue;
                // Walls shield you from the blast.
                if (dist > 0.3f && World.Query.Raycast(center, to / dist, dist, QueryFilter.All) < dist - 0.05f)
                    continue;
                if (CoverBlocks(center, victim.Chest))
                    continue; // an enemy's Deployable Cover in between takes it (GameTactical.cs)
                var dmg = Throwables.BlastDamage(def, dist * def.Radius / radius);
                if (victim == owner)
                    dmg *= 0.5f; // your own grenade still hurts
                if (victim.Sim.Stance == Stance.Prone)
                    dmg *= 0.7f; // going flat helps
                var amount = (int)MathF.Round(dmg);
                if (amount <= 0)
                    continue;
                if (IsFriendlyHit(owner, victim))
                {
                    flags |= ShotHitFriendly; // a teammate in the blast: no damage, a friendly marker (FriendlyHits.cs)
                    continue;
                }
                flags |= 1;
                if (DamagePlayer(owner, victim, amount, false, (byte)(Throwables.KillIdBase + (byte)proj.Kind), now, DamageKind.Explosive, center) && victim != owner)
                    flags |= 4;
            }
            BlastKnockback(owner, center, radius, def.Knockback); // pushes the living (Knockback.cs); the dead ragdoll from the kill
            StreakBlastEntities(owner, center, radius, def.MaxDamage, now); // enemy turrets and drones too
            BlastProps(owner, center, radius, def.MaxDamage, PropDamage.Explosion, now); // breakable props (GameProps.cs)
            BlastBuildings(owner, center, radius, def.MaxDamage, PropDamage.Explosion, now); // building doors and windows (GameBuildings.cs)
            BlastZombies(owner, center, radius, def.MaxDamage * 3f, now); // Zombies (ZombieAI.cs): grenades are strong against them
            BlastTactical(owner, center, radius, def.MaxDamage, now); // enemy tactical devices (GameTactical.cs)
        }
        else if (TacticalGear.UseOf(proj.Kind) == TacticalUse.Thrown)
        {
            ServerTacticalDetonate(proj, owner, now); // flash, stun, decoy, motion sensor (GameTactical.cs)
        }

        _w.Begin(Msg.Explode).U16(proj.Id).U8((byte)proj.Kind).U8(proj.Owner).Vec(proj.Pos).U8(flags);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        OnDetonated(proj.Kind, proj.Owner, proj.Pos, flags, now);
    }

    // ---- Both sides -----------------------------------------------------------------

    /// <summary>Visual/audio effects and persistent results (smoke, sensors) of a detonation.</summary>
    private void OnDetonated(ThrowableKind kind, byte owner, Vector3 pos, byte flags, double now)
    {
        var def = Throwables.Get(kind);
        switch (kind)
        {
            case ThrowableKind.Frag:
            case ThrowableKind.Impact:
                var blastRadius = def.Radius * (Players.TryGetValue(owner, out var thrower) ? Perks.BlastRadiusMult(thrower.Perks) : 1f);
                Blasts.Add(new Blast(pos, now, blastRadius, kind));
                Ragdolls.Blast(pos + new Vector3(0, 0.15f, 0), blastRadius, def.Knockback, now); // corpses fly (cosmetic, Source/Render/Ragdolls.cs)
                PropDebris.Push(pos + new Vector3(0, 0.15f, 0), blastRadius, def.Knockback); // and loose debris
                Audio.Effect3D(Sfx.Explosion, pos, kind == ThrowableKind.Frag ? 1f : 0.8f);
                ImpactDecals.OnBlast(World.Map, pos, owner, now); // a scorch on the nearest surface (Source/Decals)
                if (Local is { } me)
                {
                    var d = Vector3.Distance(me.RenderEye, pos);
                    Shake = MathF.Max(Shake, Math.Clamp(1.2f - d / 16f, 0f, 1f));
                }
                break;
            case ThrowableKind.Smoke:
                Smokes.Add(new SmokeCloud(pos, now, now + Throwables.SmokeDuration));
                Audio.Effect3D(Sfx.SmokeHiss, pos, 0.8f);
                break;
            case ThrowableKind.Recon:
                Sensors.Add(new ReconSensor(pos, owner, now, now + Throwables.ReconDuration));
                Audio.Effect3D(Sfx.ReconBeep, pos, 0.7f);
                break;
            default:
                OnTacticalDetonated(kind, owner, pos, now); // the tactical items' bursts and sounds (GameTactical.cs)
                break;
        }
        if (owner == LocalId && flags != 0)
            OnOwnShotConfirmed(flags, now);
    }

    private void BounceSound(Projectile proj, double now)
    {
        if (now - proj.LastBounceSound < 0.12 || proj.Vel.LengthSquared() < 1.5f)
            return;
        proj.LastBounceSound = now;
        Audio.Effect3D(Sfx.Bounce, proj.Pos, 0.5f);
    }

    /// <summary>Client: fly projectiles locally between the server's spawn and detonate messages.</summary>
    private void ClientTickProjectiles(double now)
    {
        foreach (var proj in Projectiles.Values)
        {
            if (TacticalGear.Sticks(proj.Kind) && proj.Stuck)
                continue;
            if (Throwables.Step(proj, World.TickDt))
            {
                BounceSound(proj, now);
                if (TacticalGear.Sticks(proj.Kind))
                    proj.Stuck = true;
            }
            if (now - proj.Born > 10)
                proj.Stuck = true; // the server will clean it up
        }
    }

    /// <summary>Expire old smoke, sensors and blast flashes; recon beeps.</summary>
    private void UpdateThrowableEffects(double now, float dt)
    {
        Smokes.RemoveAll(s => now > s.Until);
        Blasts.RemoveAll(b => now - b.Born > 0.6);
        for (var i = Sensors.Count - 1; i >= 0; i--)
        {
            var s = Sensors[i];
            if (now > s.Until)
            {
                Sensors.RemoveAt(i);
                continue;
            }
            // Beep once a second (everyone can hear it, so it can be found).
            var age = now - s.Born;
            if ((int)age != (int)(age - dt) && age > 0.5)
                Audio.Effect3D(Sfx.ReconBeep, s.Pos, 0.45f);
        }
        Shake *= MathF.Exp(-4f * dt);
    }

    /// <summary>
    /// Is <paramref name="p"/> revealed to the local player by one of their Recon Pulses or a friendly Recon Sweep?
    /// Only the viewer's enemies are ever revealed (teammates keep their normal look); the overlay is drawn in the
    /// revealed player's own team colour (<see cref="ReconOverlay.ColorFor"/>).
    /// </summary>
    public bool IsRevealed(Player p, double now)
    {
        if (Perks.HiddenFromRecon(p.Perks, p.RenderStance, p.RenderVelocity))
            return false; // Low Profile: hidden from Recon while low; Ghost: while moving
        if (Local is not { } me || p.Id == LocalId || !p.Alive)
            return false;
        if (!AreEnemies(me, p))
            return false; // the viewer's teammates are never "revealed" (this used to colour allies near your pulse red)
        if (StreakJammed(Local, now))
            return false; // an enemy Counter Sweep blinds your recon
        if (LocalFlashHidesHud(now))
            return false; // flashed: the overlay is washed out too (GameTactical.cs)
        if (StreakSweepReveals(p, now))
            return true;
        foreach (var s in Sensors)
            if (s.Owner == LocalId && now <= s.Until && Vector3.Distance(s.Pos, p.RenderPosition) <= Throwables.Get(ThrowableKind.Recon).Radius)
                return true;
        return false;
    }

    /// <summary>Does the segment pass through a smoke cloud?</summary>
    public bool SmokeBlocks(Vector3 a, Vector3 b, double now)
    {
        var r = Throwables.Get(ThrowableKind.Smoke).Radius * 0.8f;
        foreach (var s in Smokes)
        {
            if (now - s.Born < 0.8 || now > s.Until)
                continue; // still billowing out (or a Smoke Screen canister still falling), or gone
            var ab = b - a;
            var t = Math.Clamp(Vector3.Dot(s.Pos + new Vector3(0, 1.5f, 0) - a, ab) / MathF.Max(ab.LengthSquared(), 1e-4f), 0f, 1f);
            if (Vector3.Distance(a + ab * t, s.Pos + new Vector3(0, 1.5f, 0)) < r)
                return true;
        }
        return false;
    }

    // ---- Packets --------------------------------------------------------------------

    private void OnThrowablePacket(byte id, ref PacketReader r, RakNetGUID sender, double now)
    {
        switch (id)
        {
            case (byte)Msg.Throw when IsServer && r.Has(25):
            {
                if (!_clients.TryGetValue(sender.G, out var c) || !Players.TryGetValue(c.Id, out var p))
                    break;
                var kind = (ThrowableKind)r.U8();
                var origin = r.Vec();
                var dir = r.Vec();
                ServerThrow(p, kind, origin, dir, now);
                break;
            }
            case (byte)Msg.ProjSpawn when !IsServer && r.Has(28):
            {
                var proj = new Projectile { Id = r.U16(), Kind = (ThrowableKind)r.U8(), Owner = r.U8(), Pos = r.Vec(), Vel = r.Vec(), Born = now };
                Projectiles[proj.Id] = proj;
                if (proj.Owner != LocalId)
                    Audio.Effect3D(Sfx.Throw, proj.Pos, 0.6f);
                break;
            }
            case (byte)Msg.Explode when !IsServer && r.Has(17):
            {
                var pid = r.U16();
                var kind = (ThrowableKind)r.U8();
                var owner = r.U8();
                var pos = r.Vec();
                var flags = r.U8();
                Projectiles.Remove(pid);
                OnDetonated(kind, owner, pos, flags, now);
                break;
            }
        }
    }
}
