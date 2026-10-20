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
}

public sealed class ThrowableDef
{
    public required ThrowableKind Kind;
    public required string Name;
    public required string Blurb;
    public float Fuse; // seconds until it goes off (Frag/Smoke), max flight time otherwise
    public float Radius; // blast / cloud / reveal radius
    public float MaxDamage;
    public int PerLife;
    public Vector3 Color;
    public bool IsEquipment => Kind == ThrowableKind.Recon;
}

public static class Throwables
{
    public const float ThrowSpeed = 17f;
    public const float Restitution = 0.35f;
    public const float ProjectileRadius = 0.1f;
    public const float SmokeDuration = 14f;
    public const float ReconDuration = 12f;
    public const byte KillIdBase = 240; // kill-feed "weapon" ids 240+ are throwables

    public static readonly ThrowableDef[] All =
    [
        new() { Kind = ThrowableKind.None, Name = "None", Blurb = "Nothing in this slot." },
        new() { Kind = ThrowableKind.Frag, Name = "Frag Grenade", Blurb = "2.2 s fuse, bounces off walls. Kills within 3 m, hurts out to 7 m.",
            Fuse = 2.2f, Radius = 7f, MaxDamage = 150, PerLife = 2, Color = new(0.2f, 0.35f, 0.15f) },
        new() { Kind = ThrowableKind.Impact, Name = "Impact Grenade", Blurb = "Explodes the moment it touches anything. Smaller blast.",
            Fuse = 6f, Radius = 4.5f, MaxDamage = 110, PerLife = 2, Color = new(0.75f, 0.2f, 0.15f) },
        new() { Kind = ThrowableKind.Smoke, Name = "Smoke Grenade", Blurb = "Thick 5 m cloud for 14 s. Blocks vision, and bots can't see through it.",
            Fuse = 1.2f, Radius = 5f, PerLife = 2, Color = new(0.6f, 0.6f, 0.62f) },
        new() { Kind = ThrowableKind.Recon, Name = "Recon Pulse", Blurb = "Throw it anywhere. For 12 s it reveals every enemy within 18 m to you, through walls.",
            Fuse = 8f, Radius = 18f, PerLife = 1, Color = new(0.2f, 0.85f, 0.95f) },
    ];

    public static ThrowableDef Get(ThrowableKind k) => All[(int)k < All.Length ? (int)k : 0];

    public static IEnumerable<ThrowableDef> Grenades => All.Where(d => d.Kind is ThrowableKind.Frag or ThrowableKind.Impact or ThrowableKind.Smoke);

    public static string KillName(byte killId) =>
        killId == Game.MeleeKillId ? "Melee" : killId >= KillIdBase ? Get((ThrowableKind)(killId - KillIdBase)).Name
        : KillStreaks.IsStreakKill(killId) ? KillStreaks.Get(KillStreaks.FromKillId(killId)).Name : Weapons.Get(killId).Name;

    /// <summary>Point-vs-box test for a small projectile.</summary>
    private static bool InsideSolid(Vector3 p)
    {
        var r = ProjectileRadius;
        foreach (var b in World.Map.Solid)
            if (p.X > b.Min.X - r && p.X < b.Max.X + r && p.Y > b.Min.Y - r && p.Y < b.Max.Y + r && p.Z > b.Min.Z - r && p.Z < b.Max.Z + r)
                return true;
        return false;
    }

    /// <summary>One tick of ballistic flight with bounces. Returns true if it touched geometry.</summary>
    public static bool Step(Projectile p, float dt)
    {
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
            if (InsideSolid(pos))
            {
                pos.X = p.Pos.X;
                p.Vel.X *= -Restitution;
                touched = true;
            }
            pos.Z += p.Vel.Z * h;
            if (InsideSolid(pos))
            {
                pos.Z = p.Pos.Z;
                p.Vel.Z *= -Restitution;
                touched = true;
            }
            pos.Y += p.Vel.Y * h;
            if (InsideSolid(pos))
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
            pos.X = Math.Clamp(pos.X, -World.Map.HalfX, World.Map.HalfX);
            pos.Z = Math.Clamp(pos.Z, -World.Map.HalfZ, World.Map.HalfZ);
            if (World.Map.TerrainCollision is { } terrain && TerrainContact(p, ref pos, terrain, h))
                touched = true;
            p.Pos = pos;
        }
        return touched;
    }

    /// <summary>Rolling friction on terrain (deceleration = this x gravity x cos(slope)): grenades stop on slopes up to about 38 degrees.</summary>
    public const float RollFriction = 0.8f;

    /// <summary>
    /// Bounce off and roll along the terrain using its surface normal: hard hits bounce (losing speed), soft
    /// contact keeps it on the surface, and friction brings it to rest unless the slope is too steep.
    /// </summary>
    private static bool TerrainContact(Projectile p, ref Vector3 pos, ITerrainCollision terrain, float h)
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
        if (Mode == GameMode.GunGame)
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
        var origin = me.RenderEye + dir * 0.4f;
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

        if (Vector3.Distance(origin, p.Eye) > 3f || !float.IsFinite(origin.X + origin.Y + origin.Z))
            origin = p.Eye;
        if (World.Blocked(origin - new Vector3(0, 0.05f, 0), 0.1f))
            origin = p.Eye; // don't spawn it inside a wall
        dir = dir.LengthSquared() > 0.5f ? Vector3.Normalize(dir) : World.Forward(p.Yaw, p.Pitch);

        var proj = new Projectile
        {
            Id = _nextProjectileId++,
            Kind = kind,
            Owner = p.Id,
            Pos = origin,
            Vel = dir * Throwables.ThrowSpeed + new Vector3(0, 2.5f, 0) + p.Sim.Velocity * 0.5f,
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
            var age = now - proj.Born;
            var go = proj.Kind switch
            {
                ThrowableKind.Impact => touched || age > def.Fuse,
                ThrowableKind.Recon => touched || age > def.Fuse,
                _ => age >= def.Fuse,
            };
            if (go)
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
            foreach (var victim in Players.Values.ToList())
            {
                if (!victim.Alive)
                    continue;
                var to = victim.Chest - center;
                var dist = to.Length();
                if (dist > def.Radius)
                    continue;
                // Walls shield you from the blast.
                if (dist > 0.3f && World.RaycastWorld(center, to / dist, dist) < dist - 0.05f)
                    continue;
                var dmg = def.MaxDamage * MathF.Pow(1f - dist / def.Radius, 1.4f);
                if (victim == owner)
                    dmg *= 0.5f; // your own grenade still hurts
                if (victim.Sim.Stance == Stance.Prone)
                    dmg *= 0.7f; // going flat helps
                var amount = (int)MathF.Round(dmg);
                if (amount <= 0)
                    continue;
                flags |= 1;
                if (DamagePlayer(owner, victim, amount, false, (byte)(Throwables.KillIdBase + (byte)proj.Kind), now, DamageKind.Explosive, center) && victim != owner)
                    flags |= 4;
            }
            StreakBlastEntities(owner, center, def.Radius, def.MaxDamage, now); // enemy turrets and drones too
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
                Blasts.Add(new Blast(pos, now, def.Radius, kind));
                Audio.Effect3D(Sfx.Explosion, pos, kind == ThrowableKind.Frag ? 1f : 0.8f);
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
            if (proj.Kind == ThrowableKind.Recon && proj.Stuck)
                continue;
            if (Throwables.Step(proj, World.TickDt))
            {
                BounceSound(proj, now);
                if (proj.Kind == ThrowableKind.Recon)
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

    /// <summary>Is <paramref name="p"/> revealed to the local player by one of their Recon Pulses?</summary>
    public bool IsRevealed(Player p, double now)
    {
        if (p.Perks.Has(PerkId.LowProfile) && p.RenderStance != Stance.Stand)
            return false; // Low Profile: hidden from Recon while low
        if (p.Id == LocalId || !p.Alive)
            return false;
        if (StreakJammed(Local, now))
            return false; // an enemy Counter Sweep blinds your recon
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
            if (now - s.Born < 0.8)
                continue; // still billowing out
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
