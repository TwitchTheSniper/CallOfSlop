using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// A Sentry Turret or Attack Drone. The server runs them; clients get a compact update (~22 bytes each) with every
/// snapshot, and draw tracers and sounds themselves while the "firing" flag is set.
/// </summary>
public sealed class StreakEntity
{
    public byte Id;
    public StreakId Kind;
    public byte Owner;
    public Vector3 Pos;
    public float BaseYaw; // turret: the way it was placed (its sight cone is centred on this)
    public float Yaw, Pitch; // where the gun points
    public int Health;
    public double Born, Until;
    public byte Target = byte.MaxValue;
    public bool Firing;

    // ---- Server ----
    public double NextScan, NextShot, AcquiredAt, LastShot = -10;
    public int BurstLeft;
    public float OrbitAngle, OrbitRadius, OrbitDir = 1;
    public uint Seed;

    // ---- Client ----
    public Vector3 RenderPos;
    public float RenderYaw, RenderPitch;
    public double LastUpdate;
    public double NextFx;

    public int MaxHealth => Kind == StreakId.SentryTurret ? KillStreaks.TurretHealth : KillStreaks.DroneHealth;

    /// <summary>What bullets and blasts hit.</summary>
    public Box Hitbox => Kind == StreakId.SentryTurret
        ? new Box(Pos + new Vector3(0, 0.6f, 0), new Vector3(0.45f, 0.6f, 0.45f))
        : new Box(Pos, new Vector3(0.75f, 0.3f, 0.75f));

    /// <summary>Where its shots come from.</summary>
    public Vector3 Muzzle(Vector3 pos, float yaw, float pitch) => Kind == StreakId.SentryTurret
        ? pos + new Vector3(0, 1.05f, 0) + World.Forward(yaw, pitch) * 0.7f
        : pos - new Vector3(0, 0.4f, 0);

    public const int NetBytes = 22;
}

public sealed partial class Game
{
    public readonly Dictionary<byte, StreakEntity> StreakEntities = new();
    private byte _nextEntityId = 1;
    private bool _entitiesSent; // did the last update have any (so one empty update follows)?
    private uint _lastEntTick;

    private StreakEntity NewEntity(Player owner, StreakId kind, Vector3 pos, double now, double life)
    {
        var e = new StreakEntity
        {
            Id = _nextEntityId, Kind = kind, Owner = owner.Id, Pos = pos, RenderPos = pos, Born = now, Until = now + life,
            Seed = (uint)_rng.Next(),
        };
        e.Health = e.MaxHealth;
        _nextEntityId = (byte)(_nextEntityId % 250 + 1);
        StreakEntities[e.Id] = e;
        return e;
    }

    /// <summary>Puts a turret on the floor just in front of the owner, facing where they look.</summary>
    private StreakEntity SpawnTurret(Player p, float yaw, double now)
    {
        var fwd = World.FlatForward(yaw);
        var spot = p.Position + fwd * 1.3f;
        // Land it on whatever is under that spot (no lower than the player's feet minus a step, never inside a wall).
        var down = World.RaycastWorld(spot + new Vector3(0, 1.2f, 0), -Vector3.UnitY, 2.5f);
        spot.Y = spot.Y + 1.2f - down;
        var look = spot + Vector3.UnitY - p.Eye;
        var lookLen = look.Length();
        if (down >= 2.49f || World.Blocked(spot, 1.1f) || World.RaycastWorld(p.Eye, look / lookLen, lookLen) < lookLen - 0.05f)
            spot = p.Position; // no room in front (or it's through a wall): at their feet
        var e = NewEntity(p, StreakId.SentryTurret, spot, now, KillStreaks.TurretSeconds);
        e.BaseYaw = e.Yaw = e.RenderYaw = yaw;
        e.NextScan = now + 0.8; // a moment to unfold
        return e;
    }

    /// <summary>Launches a drone that circles the middle of the map above the rooftops.</summary>
    private StreakEntity SpawnDrone(Player p, double now)
    {
        var map = World.Map;
        var radius = Math.Clamp(MathF.Min(map.HalfX, map.HalfZ) * 0.45f, 8f, 45f);
        var e = NewEntity(p, StreakId.AttackDrone, Vector3.Zero, now, KillStreaks.DroneSeconds);
        e.OrbitRadius = radius;
        e.OrbitAngle = MathF.Atan2(p.Position.Z, p.Position.X);
        e.OrbitDir = _rng.NextDouble() < 0.5 ? -1 : 1;
        e.Pos = e.RenderPos = DronePosition(e);
        e.NextScan = now + 1.5;
        return e;
    }

    /// <summary>Drone flight height: above most rooftops, within reason.</summary>
    private static float DroneAltitude()
    {
        var map = World.Map;
        var top = 0f;
        foreach (var b in map.Solid)
            if (b.Max.Y > top && b.Half.X < map.HalfX * 0.5f) // ignore giant boundary walls
                top = b.Max.Y;
        return Math.Clamp(top + 4f, 10f, 40f);
    }

    private float _droneAltitude = -1;
    private Map? _droneAltitudeMap;

    private Vector3 DronePosition(StreakEntity e)
    {
        if (_droneAltitudeMap != World.Map)
        {
            _droneAltitudeMap = World.Map;
            _droneAltitude = DroneAltitude();
        }
        return new Vector3(MathF.Cos(e.OrbitAngle) * e.OrbitRadius, _droneAltitude, MathF.Sin(e.OrbitAngle) * e.OrbitRadius);
    }

    // =====================================================================
    // Server
    // =====================================================================

    private void ServerTickStreakEntities(double now)
    {
        if (StreakEntities.Count == 0)
            return;
        List<StreakEntity>? gone = null;
        foreach (var e in StreakEntities.Values)
        {
            if (now >= e.Until || !Players.TryGetValue(e.Owner, out var owner))
            {
                (gone ??= new()).Add(e);
                continue;
            }
            if (e.Kind == StreakId.SentryTurret)
                TickTurret(e, owner, now);
            else
                TickDrone(e, owner, now);
            e.RenderPos = e.Pos;
            e.RenderYaw = e.Yaw;
            e.RenderPitch = e.Pitch;
        }
        if (gone != null)
            foreach (var e in gone)
                RemoveStreakEntity(e, byte.MaxValue, now); // ran out (or its owner left)
    }

    /// <summary>The nearest enemy of <paramref name="owner"/> in clear sight of <paramref name="from"/>, optionally inside a cone.</summary>
    private Player? StreakFindTarget(Player owner, Vector3 from, float range, float coneYaw, float coneCos, byte current)
    {
        Player? best = null;
        var bestScore = float.MaxValue;
        foreach (var o in Players.Values)
        {
            if (!o.Alive || !StreakIsEnemy(owner, o))
                continue;
            var to = o.Chest - from;
            var dist = to.Length();
            if (dist > range || dist < 0.1f)
                continue;
            if (coneCos > -1)
            {
                var flat = new Vector3(to.X, 0, to.Z);
                if (flat.LengthSquared() > 0.01f && Vector3.Dot(Vector3.Normalize(flat), World.FlatForward(coneYaw)) < coneCos)
                    continue;
            }
            if (World.RaycastWorld(from, to / dist, dist) < dist - 0.05f || SmokeBlocks(from, o.Chest, Brutal.GlfwApi.Glfw.Time))
                continue;
            var score = dist - (o.Id == current ? 4f : 0f);
            if (score < bestScore)
            {
                bestScore = score;
                best = o;
            }
        }
        return best;
    }

    private void TickTurret(StreakEntity e, Player owner, double now)
    {
        var dt = World.TickDt;
        var head = e.Pos + new Vector3(0, 1.05f, 0);
        if (now >= e.NextScan)
        {
            e.NextScan = now + 0.15;
            var t = StreakFindTarget(owner, head, KillStreaks.TurretRange, e.BaseYaw, KillStreaks.TurretConeCos, e.Target);
            var id = t?.Id ?? byte.MaxValue;
            if (id != e.Target)
                e.AcquiredAt = now;
            e.Target = id;
        }
        if (Players.TryGetValue(e.Target, out var target) && target.Alive)
        {
            var to = target.Chest + target.Sim.Velocity * 0.05f - head;
            var wantYaw = MathF.Atan2(to.X, -to.Z);
            var wantPitch = MathF.Asin(Math.Clamp(to.Y / MathF.Max(to.Length(), 0.01f), -1f, 1f));
            var dy = World.WrapAngle(wantYaw - e.Yaw);
            var turn = KillStreaks.TurretTurnRate * dt;
            e.Yaw = World.WrapAngle(e.Yaw + Math.Clamp(dy, -turn, turn));
            e.Pitch += Math.Clamp(wantPitch - e.Pitch, -turn, turn);
            if (MathF.Abs(dy) < 0.08f && now - e.AcquiredAt >= KillStreaks.TurretWarmup && now >= e.NextShot)
            {
                e.NextShot = now + KillStreaks.TurretInterval;
                StreakEntityShoot(e, owner, World.Forward(e.Yaw, e.Pitch), KillStreaks.TurretSpread, KillStreaks.TurretDamage, now);
            }
        }
        else
        {
            e.Target = byte.MaxValue;
            // Idle: slowly scan back and forth across its arc.
            var want = World.WrapAngle(e.BaseYaw + MathF.Sin((float)(now - e.Born) * 0.7f) * 0.9f);
            e.Yaw = World.WrapAngle(e.Yaw + Math.Clamp(World.WrapAngle(want - e.Yaw), -dt * 1.2f, dt * 1.2f));
            e.Pitch *= 1f - dt * 2f;
        }
        e.Firing = now - e.LastShot < 0.25;
    }

    private void TickDrone(StreakEntity e, Player owner, double now)
    {
        e.OrbitAngle += e.OrbitDir * KillStreaks.DroneSpeed / e.OrbitRadius * World.TickDt;
        e.Pos = DronePosition(e);
        Players.TryGetValue(e.Target, out var target);
        if (e.BurstLeft == 0 && now >= e.NextScan)
        {
            target = StreakFindTarget(owner, e.Muzzle(e.Pos, 0, 0), KillStreaks.DroneRange, 0, -2, e.Target);
            e.Target = target?.Id ?? byte.MaxValue;
            if (target != null)
            {
                e.BurstLeft = KillStreaks.DroneBurst;
                e.NextShot = now + KillStreaks.DroneWindup;
            }
            else
            {
                e.NextScan = now + 0.3;
            }
        }
        // Body faces along the orbit; the gun follows the target.
        e.Yaw = MathF.Atan2(-MathF.Sin(e.OrbitAngle) * e.OrbitDir, -MathF.Cos(e.OrbitAngle) * e.OrbitDir);
        if (target is { Alive: true })
        {
            var to = target.Chest - e.Muzzle(e.Pos, 0, 0);
            e.Pitch = MathF.Asin(Math.Clamp(to.Y / MathF.Max(to.Length(), 0.01f), -1f, 1f));
            if (e.BurstLeft > 0 && now >= e.NextShot)
            {
                e.BurstLeft--;
                e.NextShot = now + KillStreaks.DroneBurstGap;
                StreakEntityShoot(e, owner, Vector3.Normalize(to + target.Sim.Velocity * 0.04f), KillStreaks.DroneSpread, KillStreaks.DroneDamage, now);
                if (e.BurstLeft == 0)
                    e.NextScan = now + KillStreaks.DroneCooldown;
            }
        }
        else if (e.BurstLeft > 0)
        {
            e.BurstLeft = 0;
            e.NextScan = now + 0.3;
        }
        e.Firing = e.BurstLeft > 0 || now - e.LastShot < 0.15;
    }

    /// <summary>One hitscan shot from a turret or drone (no lag compensation: it sees the server's present).</summary>
    private void StreakEntityShoot(StreakEntity e, Player owner, Vector3 aim, float spread, float damage, double now)
    {
        e.LastShot = now;
        var origin = e.Muzzle(e.Pos, e.Yaw, e.Pitch);
        var d = Weapons.SpreadDirection(aim, spread, e.Seed++, 0);
        var dist = World.RaycastWorld(origin, d, KillStreaks.DroneRange + 10);
        Player? hit = null;
        foreach (var o in Players.Values)
        {
            if (!o.Alive || !StreakIsEnemy(owner, o))
                continue;
            if (World.Hitbox(o.Position, o.Sim.Stance, o.Yaw).Raycast(origin, d, dist) is { } t && t < dist)
            {
                dist = t;
                hit = o;
            }
        }
        if (hit != null)
        {
            var killId = KillStreaks.KillId(e.Kind);
            if (DamagePlayer(owner, hit, (int)MathF.Round(damage), false, killId, now, DamageKind.Bullet) && owner.Id == LocalId)
                OnOwnShotConfirmed(5, now);
        }
        // Host-side effects (clients make their own from the firing flag).
        AddStreakTracer(origin, origin + d * dist, now);
        Audio.Effect3D(Sfx.TurretShot, origin, e.Kind == StreakId.SentryTurret ? 0.55f : 0.7f);
    }

    private void AddStreakTracer(Vector3 from, Vector3 to, double now) =>
        Tracers.Add(new Tracer { From = from, To = to, Color = new Vector3(1f, 0.55f, 0.3f), Width = 0.03f, Born = now });

    /// <summary>
    /// For shots (from ServerResolveShot): the nearest enemy streak entity along a ray, closer than <paramref name="dist"/>
    /// (which is shortened to it). The shooter's own turret and drone don't stop their bullets.
    /// </summary>
    private StreakEntity? StreakRaycast(Vector3 origin, Vector3 dir, ref float dist, Player shooter)
    {
        StreakEntity? best = null;
        foreach (var e in StreakEntities.Values)
        {
            if (!Players.TryGetValue(e.Owner, out var owner) || !StreakIsEnemy(owner, shooter))
                continue;
            if (e.Hitbox.Raycast(origin, dir, dist) is { } t && t < dist)
            {
                dist = t;
                best = e;
            }
        }
        return best;
    }

    /// <summary>Damages a turret or drone. Returns true if that destroyed it.</summary>
    private bool DamageStreakEntity(StreakEntity e, Player attacker, int amount, double now)
    {
        if (amount <= 0 || !StreakEntities.ContainsKey(e.Id) || !DamageEnabled)
            return false;
        e.Health -= amount;
        if (e.Health > 0)
            return false;
        RemoveStreakEntity(e, attacker.Id, now);
        return true;
    }

    /// <summary>Grenade and strike blasts also hurt the turrets and drones of the thrower's enemies (walls shield them too).</summary>
    private void StreakBlastEntities(Player attacker, Vector3 center, float radius, float maxDamage, double now)
    {
        if (StreakEntities.Count == 0)
            return;
        foreach (var e in StreakEntities.Values.ToList())
        {
            if (!Players.TryGetValue(e.Owner, out var owner) || !StreakIsEnemy(owner, attacker))
                continue;
            DamageStreakEntity(e, attacker, (int)MathF.Round(BlastDamage(center, e.Hitbox.Center, radius, maxDamage)), now);
        }
    }

    private void RemoveStreakEntity(StreakEntity e, byte by, double now)
    {
        if (!StreakEntities.Remove(e.Id))
            return;
        KillStreaks.WriteEvent(_w, 1, e.Owner, e.Kind, e.Pos, 0, e.Id, by);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        OnStreakEntityDestroyed(e.Owner, e.Kind, e.Pos, by, now);
    }

    /// <summary>Server -> all: every live turret and drone, with each snapshot (one empty update after the last one goes).</summary>
    private void SendStreakEntities()
    {
        if (!IsServer || Net.IsOffline || (StreakEntities.Count == 0 && !_entitiesSent))
            return;
        _entitiesSent = StreakEntities.Count > 0;
        _w.Begin(Msg.StreakEnts).U32(ServerTick).U8((byte)StreakEntities.Count);
        foreach (var e in StreakEntities.Values)
            WriteEntity(_w, e);
        Net.Broadcast(_w.Span, PacketReliability.Unreliable);
    }

    /// <summary>One entity in an update: id, kind, owner, position, aim, health, target and flags.</summary>
    public static void WriteEntity(PacketWriter w, StreakEntity e)
    {
        w.U8(e.Id).U8((byte)e.Kind).U8(e.Owner).Vec(e.Pos)
            .U16((ushort)Math.Clamp(World.WrapAngle(e.Yaw) / MathF.Tau * 65536f + 32768f, 0, 65535))
            .U16((ushort)Math.Clamp(e.Pitch / MathF.PI * 65536f + 32768f, 0, 65535))
            .U8((byte)Math.Clamp(e.Health * 255 / Math.Max(1, e.MaxHealth), 0, 255))
            .U8(e.Target)
            .U8((byte)(e.Firing ? 1 : 0));
    }

    public static StreakEntity ReadEntity(ref PacketReader r)
    {
        var e = new StreakEntity { Id = r.U8(), Kind = (StreakId)r.U8(), Owner = r.U8(), Pos = r.Vec() };
        e.Yaw = (r.U16() - 32768f) / 65536f * MathF.Tau;
        e.Pitch = (r.U16() - 32768f) / 65536f * MathF.PI;
        e.Health = r.U8() * e.MaxHealth / 255;
        e.Target = r.U8();
        e.Firing = (r.U8() & 1) != 0;
        return e;
    }

    // =====================================================================
    // Client
    // =====================================================================

    private void ReadStreakEntities(ref PacketReader r, double now)
    {
        var tick = r.U32();
        if (tick < _lastEntTick)
            return; // out of order
        _lastEntTick = tick;
        int n = r.U8();
        var seen = new HashSet<byte>();
        for (var i = 0; i < n && r.Has(StreakEntity.NetBytes); i++)
        {
            var u = ReadEntity(ref r);
            seen.Add(u.Id);
            if (!StreakEntities.TryGetValue(u.Id, out var e))
            {
                u.RenderPos = u.Pos;
                u.RenderYaw = u.Yaw;
                u.RenderPitch = u.Pitch;
                u.Born = now;
                StreakEntities[u.Id] = e = u;
            }
            e.Kind = u.Kind;
            e.Owner = u.Owner;
            e.Pos = u.Pos;
            e.Yaw = u.Yaw;
            e.Pitch = u.Pitch;
            e.Health = u.Health;
            e.Target = u.Target;
            e.Firing = u.Firing;
            e.LastUpdate = now;
        }
        foreach (var stale in StreakEntities.Keys.Where(k => !seen.Contains(k)).ToList())
            StreakEntities.Remove(stale);
    }

    /// <summary>Clients: smooth movement and make the shots' tracers and sounds while an entity is firing.</summary>
    private void UpdateStreakEntitiesVisual(double now, float dt)
    {
        if (IsServer || StreakEntities.Count == 0)
            return;
        var k = 1f - MathF.Exp(-15f * dt);
        List<byte>? stale = null;
        foreach (var e in StreakEntities.Values)
        {
            if (now - e.LastUpdate > 1.0)
            {
                (stale ??= new()).Add(e.Id); // updates stopped (the empty one got lost)
                continue;
            }
            e.RenderPos += (e.Pos - e.RenderPos) * k;
            e.RenderYaw = World.LerpAngle(e.RenderYaw, e.Yaw, k);
            e.RenderPitch += (e.Pitch - e.RenderPitch) * k;
            if (e.Firing && now >= e.NextFx)
            {
                e.NextFx = now + (e.Kind == StreakId.SentryTurret ? KillStreaks.TurretInterval : KillStreaks.DroneBurstGap);
                var from = e.Muzzle(e.RenderPos, e.RenderYaw, e.RenderPitch);
                var to = Players.TryGetValue(e.Target, out var t) && t.Alive
                    ? t.RenderPosition + new Vector3(0, World.HeightOf(t.RenderStance) * 0.6f, 0)
                    : from + World.Forward(e.RenderYaw, e.RenderPitch) * 30f;
                var jitter = new Vector3((float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f) * 0.5f;
                AddStreakTracer(from, to + jitter, now);
                Audio.Effect3D(Sfx.TurretShot, from, e.Kind == StreakId.SentryTurret ? 0.55f : 0.7f);
            }
        }
        if (stale != null)
            foreach (var id in stale)
                StreakEntities.Remove(id);
    }

    /// <summary>Both sides: a turret or drone is gone (destroyed, or expired when <paramref name="by"/> is 255).</summary>
    private void OnStreakEntityDestroyed(byte owner, StreakId kind, Vector3 pos, byte by, double now)
    {
        if (!IsServer)
            foreach (var e in StreakEntities.Values.Where(e => e.Owner == owner && e.Kind == kind && Vector3.DistanceSquared(e.Pos, pos) < 4f).ToList())
                StreakEntities.Remove(e.Id);
        var name = KillStreaks.Get(kind).Name;
        if (by == byte.MaxValue)
        {
            if (owner == LocalId)
                Log($"Your {name} is out of time");
            return;
        }
        Blasts.Add(new Blast(pos + new Vector3(0, kind == StreakId.SentryTurret ? 0.6f : 0, 0), now, 2.5f, ThrowableKind.Impact));
        Audio.Effect3D(Sfx.Explosion, pos, 0.7f);
        string Who(byte id) => id == LocalId ? "You" : NameOf(id);
        var whose = owner == LocalId ? "your" : $"{NameOf(owner)}'s";
        Log($"{Who(by)} destroyed {whose} {name}", by == LocalId || owner == LocalId);
        if (by == LocalId)
            Audio.Effect(Sfx.Kill, 0.7f);
    }
}
