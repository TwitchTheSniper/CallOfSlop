using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What a <see cref="Msg.ZGrenade"/> carries (its first byte).</summary>
public enum ZGrenadeMsg : byte
{
    /// <summary>Client -> server: set off my remote-fuse grenades (the grenade key while one is out).</summary>
    Detonate,

    /// <summary>Server -> all: a lure's closing bang (position, payload, radius x10).</summary>
    Blast,
}

/// <summary>A lingering patch a built grenade leaves (fire, gas, a lure): pooled, a fixed number at most.</summary>
public struct GrenadeZone
{
    public bool Active;
    public PayloadKind Kind;
    public Vector3 Pos;
    public float Radius;
    public double Born, Until, NextTick;
    public byte Owner;

    /// <summary>Fire: the burn total each zombie inside gets; gas: damage a quarter-second tick; lure: the closing bang's damage.</summary>
    public float Amount;
}

/// <summary>
/// Modular grenades in Zombies (docs/zombies/WEAPON-BUILDER.md, "Modular grenades"): the grenade a player built at a
/// workbench (their <see cref="Player.GrenadeBuild"/>, the Zombies grenade slot) is a <see cref="ThrowableKind.Built"/>
/// projectile. The server throws it with the build's speed and bounce, runs its fuse (timer, cookable, impact,
/// proximity, remote), and sets off its payload (frag, fire, cryo, shock, gas, a lure, healing, ducks), its cluster's
/// bomblets and its aftershocks; clients fly the same arc from the same numbers and draw what the Explode message says.
/// Cheap with hordes of hundreds: blasts reuse the Zombies frag's loop (BlastZombies), lingering patches live in a fixed
/// pool ticked four times a second, at most <see cref="MaxBuiltLive"/> built grenades fly at once and the flashes are
/// capped; nothing here allocates per tick.
/// </summary>
public sealed partial class Game
{
    /// <summary>Most built grenades (with bomblets and aftershocks) in the air at once; more bomblets are skipped.</summary>
    public const int MaxBuiltLive = 96;

    /// <summary>Most lingering patches (fire, gas, lures) at once; a new one replaces the oldest.</summary>
    public const int MaxGrenadeZones = 24;

    /// <summary>Most blast flashes kept for drawing.</summary>
    public const int MaxBlasts = 32;

    /// <summary>Fires, clouds and lures (server: the gameplay; clients: the look, from the Explode message).</summary>
    public readonly GrenadeZone[] GrenadeZones = new GrenadeZone[MaxGrenadeZones];

    /// <summary>Self-test counters: bangs, bomblets and aftershocks spawned on this side.</summary>
    public int BuiltBangs, BuiltBomblets, BuiltAftershocks;

    /// <summary>The local player's built grenade (or null).</summary>
    public GrenadeDef? LocalGrenadeBuild => Local?.GrenadeBuild;

    // ---- Throwing ---------------------------------------------------------------------------------------------

    /// <summary>Server: a built grenade leaves <paramref name="p"/>'s hand: its numbers, launch speed and bounce; a cooked timer starts that far in.</summary>
    private void ServerBuiltThrown(Player p, Projectile proj, Vector3 dir, float cook, double now)
    {
        var g = p.GrenadeBuild!;
        proj.Grenade = g;
        proj.Bounce = g.Bounce;
        proj.StagesLeft = g.Stages;
        proj.Vel = dir * g.ThrowSpeed + new Vector3(0, Throwables.ThrowUp, 0) + p.Sim.Velocity * 0.5f;
        if (g.Cookable && cook > 0)
            proj.Born = now - Math.Clamp(cook, 0, g.FuseSeconds); // cooked: the fuse already ran in the hand
    }

    /// <summary>Client: a built grenade appeared: the thrower's numbers (what they had at the throw), so it bounces and sticks the same.</summary>
    private void ClientBuiltSpawned(Projectile proj, byte gen)
    {
        proj.Gen = gen;
        proj.Grenade = Players.TryGetValue(proj.Owner, out var o) ? o.GrenadeBuild : null;
        if (proj.Grenade is { } g)
            proj.Bounce = gen == 0 ? g.Bounce : Throwables.Restitution;
        if (gen != 0 && proj.Grenade is { Sticky: true })
            proj.Grenade = null; // (bomblets and aftershocks don't stick; drawn smaller)
    }

    /// <summary>Both sides: a sticky built grenade touched something: it stays there.</summary>
    private static void StickBuilt(Projectile proj)
    {
        proj.Stuck = true;
        proj.Vel = Vector3.Zero;
    }

    /// <summary>
    /// The grenade key on this PC (Program): a cookable built grenade starts cooking (let go to throw it); with a remote
    /// grenade out it sets them off; otherwise the throw as always.
    /// </summary>
    public void GrenadePress(double now)
    {
        if (LocalGrenade == ThrowableKind.Built && Local is { Alive: true } me && me.GrenadeBuild is { } g && Phase == MatchPhase.Playing)
        {
            if (g.Fuse == FuseKind.Remote && OwnRemoteOut())
            {
                RemoteDetonateRequest(now);
                return;
            }
            if (g.Cookable && GrenadesLeft > 0 && now >= _nextThrow)
            {
                CookingSince = now;
                return;
            }
        }
        TryThrow(equipment: false, now);
    }

    /// <summary>When the local player started cooking a built grenade (negative: not cooking).</summary>
    public double CookingSince = -1;

    /// <summary>Every frame (Program): while cooking, letting go of the key throws it; holding past the fuse throws it at your feet with no time left.</summary>
    public void GrenadeCookTick(bool held, double now)
    {
        if (CookingSince < 0)
            return;
        if (Local is not { Alive: true } me || me.GrenadeBuild is not { } g || LocalGrenade != ThrowableKind.Built)
        {
            CookingSince = -1;
            return;
        }
        var cooked = (float)(now - CookingSince);
        if (held && cooked < g.FuseSeconds)
            return;
        CookingSince = -1;
        TryThrow(equipment: false, now, MathF.Min(cooked, g.FuseSeconds));
    }

    /// <summary>Does the local player have a remote-fuse grenade waiting?</summary>
    private bool OwnRemoteOut()
    {
        foreach (var pr in Projectiles.Values)
            if (pr.Owner == LocalId && pr.Kind == ThrowableKind.Built && pr.Gen == 0 && pr.Grenade is { Fuse: FuseKind.Remote })
                return true;
        return false;
    }

    /// <summary>The local player sets off their remote grenades (the server decides).</summary>
    private void RemoteDetonateRequest(double now)
    {
        Audio.Effect(Sfx.HitTick, 0.7f);
        if (IsServer)
        {
            if (Local is { } me)
                ServerRemoteDetonate(me);
            return;
        }
        _w.Begin(Msg.ZGrenade).U8((byte)ZGrenadeMsg.Detonate);
        Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: every remote-fuse grenade <paramref name="p"/> has out goes off on the next tick.</summary>
    private void ServerRemoteDetonate(Player p)
    {
        foreach (var pr in Projectiles.Values)
            if (pr.Owner == p.Id && pr.Kind == ThrowableKind.Built && pr.Gen == 0 && pr.Grenade is { Fuse: FuseKind.Remote })
                pr.Detonate = true;
    }

    // ---- Fuses ---------------------------------------------------------------------------------------------------

    /// <summary>Server, every tick of a built grenade: does it go off now?</summary>
    private bool BuiltGoesOff(Projectile proj, bool touched, double now)
    {
        var age = now - proj.Born;
        if (touched && !proj.Landed)
        {
            proj.Landed = true;
            proj.LandedAt = now;
        }
        if (proj.Grenade is not { } g)
            return true;
        if (touched && g.Sticky && proj.Gen == 0)
            StickBuilt(proj);
        switch (proj.Gen)
        {
            case 1:
                return (touched && age > 0.15) || age >= 1.2; // a bomblet: on touching down
            case 2:
                return age >= 0.6; // an aftershock: at the top of its hop
        }
        return g.Fuse switch
        {
            FuseKind.Timer => age >= g.FuseSeconds,
            FuseKind.Impact => touched || age >= g.FuseSeconds,
            FuseKind.Proximity => age >= 45 || (proj.LandedAt >= 0 && now - proj.LandedAt >= g.FuseSeconds && (ServerTick + proj.Id) % 3 == 0 && ZombieNear(proj.Pos, g.Trigger)),
            _ => proj.Detonate || age >= g.FuseSeconds,
        };
    }

    /// <summary>Is a living zombie within <paramref name="r"/> m of <paramref name="at"/> (a proximity fuse)? No allocation.</summary>
    private bool ZombieNear(Vector3 at, float r)
    {
        var r2 = r * r;
        for (var i = 0; i < Horde.Count; i++)
        {
            var z = Horde[i];
            if (z.State != ZState.Dead && Vector3.DistanceSquared(z.Position + new Vector3(0, 0.9f, 0), at) <= r2)
                return true;
        }
        return false;
    }

    // ---- Going off -------------------------------------------------------------------------------------------

    /// <summary>Server: a built grenade goes off: its payload, then its bomblets and the next aftershock; everyone is told (Msg.Explode with the payload's look).</summary>
    private void ServerBuiltDetonate(Projectile proj, Player? owner, double now)
    {
        var g = proj.Grenade;
        var center = proj.Pos + new Vector3(0, 0.15f, 0);
        var radius = 0f;
        var duration = 0f;
        BuiltBangs++;
        if (g != null && owner != null)
        {
            radius = g.Radius * proj.RadiusScale * Perks.BlastRadiusMult(owner.Perks);
            duration = proj.Gen == 0 ? g.Duration : g.Duration * 0.5f;
            Payload(g, owner, center, radius, g.Damage * proj.DamageScale, duration, now);
            if (proj.Gen == 0 && g.Cluster > 0)
                SpawnBomblets(proj, g, owner, now);
            if (proj.Gen != 1 && proj.StagesLeft > 0)
                SpawnAftershock(proj, g, owner, now);
        }
        _w.Begin(Msg.Explode).U16(proj.Id).U8((byte)proj.Kind).U8(proj.Owner).Vec(proj.Pos).U8(0)
            .U8((byte)(g?.Payload ?? PayloadKind.Frag)).U8(Tenths(radius)).U8(Tenths(duration)).U8(proj.Gen); // the payload's look (protocol 67)
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        OnDetonated(proj.Kind, proj.Owner, proj.Pos, 0, now);
        OnBuiltDetonated(g?.Payload ?? PayloadKind.Frag, proj.Pos, radius, duration, proj.Owner, now);
    }

    private static byte Tenths(float v) => (byte)Math.Clamp((int)MathF.Round(v * 10f), 0, 255);

    /// <summary>Server: what the payload does at <paramref name="center"/> (zombies, players, props).</summary>
    private void Payload(GrenadeDef g, Player owner, Vector3 center, float radius, float damage, float duration, double now)
    {
        switch (g.Payload)
        {
            case PayloadKind.Frag:
                BlastZombies(owner, center, radius, damage, now); // the Zombies frag's loop: falloff, walls, limbs, the horde's scatter
                BlastWorld(owner, center, radius, damage, g.Knockback, now);
                break;
            case PayloadKind.Incendiary:
                BlastZombies(owner, center, radius, damage * 0.25f, now);
                AddZone(PayloadKind.Incendiary, center, radius, duration, owner.Id, damage, now);
                BlastWorld(owner, center, radius, damage * 0.25f, 4f, now);
                break;
            case PayloadKind.Gas:
                AddZone(PayloadKind.Gas, center, radius, duration, owner.Id, damage / MathF.Max(1, duration * 4f), now);
                break;
            case PayloadKind.Lure:
                AddZone(PayloadKind.Lure, center, radius, duration, owner.Id, damage, now);
                if (Mode == GameMode.Zombies && Horde.Count > 0)
                    HordeLure(center, radius * 3f, duration, now); // zombies come to it (the horde controller's lure group)
                break;
            case PayloadKind.Heal:
                foreach (var p in Players.Values)
                    if (p.Alive && Vector3.DistanceSquared(p.Chest, center) <= radius * radius)
                        p.Health = Math.Min(World.MaxHealth, p.Health + (int)MathF.Round(g.Strength));
                break;
            default:
                AreaZombies(g, owner, center, radius, damage, now); // cryo, shock, ducks
                if (g.Payload == PayloadKind.Duck)
                    BlastWorld(owner, center, radius, damage, g.Knockback, now);
                break;
        }
    }

    /// <summary>Server: the props, building doors and windows, destructible buildings and players' knockback a blast reaches.</summary>
    private void BlastWorld(Player owner, Vector3 center, float radius, float damage, float knockback, double now)
    {
        var obj = damage / 3f; // the Zombies frag does 3x its object damage to zombies
        BlastKnockback(owner, center, radius, knockback); // pushes the living (no damage: built grenades never hurt players)
        BlastProps(owner, center, radius, obj, PropDamage.Explosion, now);
        BlastBuildings(owner, center, radius, obj, PropDamage.Explosion, now);
        BlastStructures(owner, center, radius, obj, StructureHit.Grenade, (byte)(Throwables.KillIdBase + (byte)ThrowableKind.Built), now);
    }

    /// <summary>Server: cryo (freeze then slow), shock (zap and stun) and ducks (launch) on every zombie in range, walls shielding. No allocation.</summary>
    private void AreaZombies(GrenadeDef g, Player owner, Vector3 center, float radius, float damage, double now)
    {
        if (Mode != GameMode.Zombies || Horde.Count == 0)
            return;
        HordeExplosion(owner, center, radius, now);
        var r2 = radius * radius;
        for (var i = 0; i < Horde.Count; i++)
        {
            var z = Horde[i];
            if (z.State == ZState.Dead)
                continue;
            var to = z.Position + new Vector3(0, 1.1f, 0) - center;
            var d2 = to.LengthSquared();
            if (d2 > r2)
                continue;
            var dist = MathF.Sqrt(d2);
            if (dist > 0.3f && World.Query.Raycast(center, to / dist, dist, QueryFilter.Shots) < dist - 0.05f)
                continue;
            var amount = (int)MathF.Round(damage * MathF.Pow(1f - dist / radius, 1.2f));
            switch (g.Payload)
            {
                case PayloadKind.Cryo:
                    Stun(z, g.Strength, now); // frozen solid
                    z.SlowUntil = Math.Max(z.SlowUntil, now + g.Strength + g.Duration); // then thawing slowly
                    break;
                case PayloadKind.Shock:
                    Stun(z, g.Strength, now);
                    z.ZapUntil = Math.Max(z.ZapUntil, now + g.Strength);
                    break;
            }
            if (amount > 0)
                DamageZombie(owner, z, amount, false, melee: false, now, explosive: g.Payload == PayloadKind.Duck);
            if (g.Payload == PayloadKind.Duck && z.State != ZState.Dead)
                Launch(z, center, now); // up and away
        }
        if (g.Payload == PayloadKind.Shock)
            Fx(ZFxKind.Shock, center, Sfx.EmpBurst);
        else if (g.Payload == PayloadKind.Duck)
            Fx(ZFxKind.Squeak, center, Sfx.Bounce);
    }

    /// <summary>Server: <paramref name="count"/> bomblets fly out of a cluster grenade (fixed spread, no random), each a smaller copy of the payload.</summary>
    private void SpawnBomblets(Projectile parent, GrenadeDef g, Player owner, double now)
    {
        var live = BuiltLive();
        for (var i = 0; i < g.Cluster && live < MaxBuiltLive; i++, live++)
        {
            var a = i * MathF.Tau / g.Cluster + parent.Id * 0.37f;
            var vel = new Vector3(MathF.Cos(a) * 3.2f, 4.2f + i % 3 * 0.7f, MathF.Sin(a) * 3.2f);
            SpawnBuiltChild(parent, g, owner, 1, vel, GrenadeDef.BombletDamage, GrenadeDef.BombletRadius, 0, now);
            BuiltBomblets++;
        }
    }

    /// <summary>Server: the grenade hops up and goes off again (an aftershock, weaker each time).</summary>
    private void SpawnAftershock(Projectile parent, GrenadeDef g, Player owner, double now)
    {
        if (BuiltLive() >= MaxBuiltLive)
            return;
        SpawnBuiltChild(parent, g, owner, 2, new Vector3(0, 3.2f, 0), parent.DamageScale * GrenadeDef.StageDamage, parent.RadiusScale, parent.StagesLeft - 1, now);
        BuiltAftershocks++;
    }

    private void SpawnBuiltChild(Projectile parent, GrenadeDef g, Player owner, byte gen, Vector3 vel, float damage, float radius, int stages, double now)
    {
        var proj = new Projectile
        {
            Id = _nextProjectileId++,
            Kind = ThrowableKind.Built,
            Owner = owner.Id,
            Pos = parent.Pos + new Vector3(0, 0.2f, 0),
            Vel = vel,
            Born = now,
            Grenade = g,
            Gen = gen,
            DamageScale = damage,
            RadiusScale = radius,
            StagesLeft = stages,
        };
        if (_nextProjectileId == 0)
            _nextProjectileId = 1;
        Projectiles[proj.Id] = proj;
        _w.Begin(Msg.ProjSpawn).U16(proj.Id).U8((byte)proj.Kind).U8(owner.Id).Vec(proj.Pos).Vec(proj.Vel).U8(gen);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Built grenades in the air (server).</summary>
    private int BuiltLive()
    {
        var n = 0;
        foreach (var pr in Projectiles.Values)
            if (pr.Kind == ThrowableKind.Built)
                n++;
        return n;
    }

    // ---- Lingering patches -------------------------------------------------------------------------------------

    /// <summary>Both sides: a fire, cloud or lure starts (the oldest gives way when the pool is full).</summary>
    private void AddZone(PayloadKind kind, Vector3 at, float radius, float duration, byte owner, float amount, double now)
    {
        if (duration <= 0)
            return;
        var slot = 0;
        for (var i = 0; i < GrenadeZones.Length; i++)
        {
            if (!GrenadeZones[i].Active)
            {
                slot = i;
                break;
            }
            if (GrenadeZones[i].Born < GrenadeZones[slot].Born)
                slot = i;
        }
        GrenadeZones[slot] = new GrenadeZone { Active = true, Kind = kind, Pos = at, Radius = radius, Born = now, Until = now + duration, NextTick = now + 0.25, Owner = owner, Amount = amount };
    }

    /// <summary>Server, every tick: fires light and gas hurts the zombies inside a quarter-second at a time; a lure that ran out goes off. No allocation.</summary>
    private void ServerTickGrenadeZones(double now)
    {
        for (var i = 0; i < GrenadeZones.Length; i++)
        {
            ref var zn = ref GrenadeZones[i];
            if (!zn.Active)
                continue;
            Players.TryGetValue(zn.Owner, out var owner);
            if (now >= zn.Until)
            {
                zn.Active = false;
                if (zn.Kind == PayloadKind.Lure && owner != null)
                {
                    BlastZombies(owner, zn.Pos, zn.Radius, zn.Amount, now); // the closing bang
                    BlastWorld(owner, zn.Pos, zn.Radius, zn.Amount, 10f, now);
                    _w.Begin(Msg.ZGrenade).U8((byte)ZGrenadeMsg.Blast).Vec(zn.Pos).U8((byte)PayloadKind.Lure).U8(Tenths(zn.Radius));
                    Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
                    OnBuiltDetonated(PayloadKind.Frag, zn.Pos, zn.Radius, 0, zn.Owner, now);
                    BuiltBangs++;
                }
                continue;
            }
            if (now < zn.NextTick)
                continue;
            zn.NextTick += 0.25;
            if (owner == null || Mode != GameMode.Zombies || zn.Kind is not (PayloadKind.Incendiary or PayloadKind.Gas))
                continue;
            var r2 = zn.Radius * zn.Radius;
            for (var k = 0; k < Horde.Count; k++)
            {
                var z = Horde[k];
                if (z.State == ZState.Dead)
                    continue;
                var d = z.Position - zn.Pos;
                if (d.X * d.X + d.Z * d.Z > r2 || d.Y is < -2.5f or > 2.5f)
                    continue;
                if (zn.Kind == PayloadKind.Incendiary)
                    Ignite(z, owner, zn.Amount, now); // a burn (ZombiesAmmoEffects.cs): refreshed while it stands in the fire
                else
                    DamageZombie(owner, z, Math.Max(1, (int)MathF.Round(zn.Amount)), false, melee: false, now, dot: true);
            }
        }
    }

    /// <summary>Clients: patches fade on their clock (the server decides what they do).</summary>
    private void ClientTickGrenadeZones(double now)
    {
        for (var i = 0; i < GrenadeZones.Length; i++)
            if (GrenadeZones[i].Active && now >= GrenadeZones[i].Until)
                GrenadeZones[i].Active = false;
    }

    // ---- What everyone sees ------------------------------------------------------------------------------------

    /// <summary>Both sides: the payload's flash and sound; clients also start the patch it leaves.</summary>
    private void OnBuiltDetonated(PayloadKind payload, Vector3 pos, float radius, float duration, byte owner, double now)
    {
        var tint = GrenadeDef.PayloadTint(payload);
        if (Blasts.Count >= MaxBlasts)
            Blasts.RemoveAt(0);
        var r = MathF.Max(1f, radius);
        Blasts.Add(new Blast(pos, now, payload is PayloadKind.Heal or PayloadKind.Lure ? r * 0.5f : r, ThrowableKind.Built, tint));
        if (payload is PayloadKind.Frag or PayloadKind.Duck or PayloadKind.Incendiary)
        {
            Ragdolls.Blast(pos + new Vector3(0, 0.15f, 0), r, 10f, now);
            PropDebris.Push(pos + new Vector3(0, 0.15f, 0), r, 10f);
            ImpactDecals.OnBlast(World.Map, pos, owner, now);
        }
        var sound = payload switch
        {
            PayloadKind.Incendiary => Sfx.NapalmIgnite,
            PayloadKind.Cryo => Sfx.TrophyZap,
            PayloadKind.Shock => Sfx.EmpBurst,
            PayloadKind.Gas => Sfx.SmokeHiss,
            PayloadKind.Lure => Sfx.SensorBeep,
            PayloadKind.Heal => Sfx.StimHiss,
            PayloadKind.Duck => Sfx.Bounce,
            _ => Sfx.Explosion,
        };
        Audio.Effect3D(sound, pos, payload == PayloadKind.Frag ? MathF.Min(1f, 0.6f + radius / 20f) : 0.8f);
        if (payload is PayloadKind.Frag or PayloadKind.Duck && Local is { } me)
        {
            var d = Vector3.Distance(me.RenderEye, pos);
            Shake = MathF.Max(Shake, Math.Clamp(1.2f - d / 16f, 0f, 1f) * 0.8f);
        }
        if (!IsServer && duration > 0 && payload is PayloadKind.Incendiary or PayloadKind.Gas or PayloadKind.Lure)
            AddZone(payload, pos + new Vector3(0, 0.15f, 0), radius, duration, owner, 0, now);
    }

    /// <summary>Client: the tail a built grenade's Msg.Explode carries (payload, radius and duration in tenths, what it was).</summary>
    private void ReadBuiltExplode(ref PacketReader r, byte owner, Vector3 pos, double now)
    {
        if (!r.Has(4))
            return;
        var payload = (PayloadKind)Math.Min((int)r.U8(), (int)PayloadKind.Duck);
        var radius = r.U8() / 10f;
        var duration = r.U8() / 10f;
        r.U8(); // gen (thrown, bomblet, aftershock): nothing to do with it yet
        BuiltBangs++;
        OnBuiltDetonated(payload, pos, radius, duration, owner, now);
    }

    /// <summary>Both sides: <see cref="Msg.ZGrenade"/>.</summary>
    private void OnZGrenadePacket(ref PacketReader r, RakNetGUID sender, double now)
    {
        if (!r.Has(1))
            return;
        var kind = (ZGrenadeMsg)r.U8();
        switch (kind)
        {
            case ZGrenadeMsg.Detonate when IsServer:
                if (_clients.TryGetValue(sender.G, out var c) && Players.TryGetValue(c.Id, out var p))
                    ServerRemoteDetonate(p);
                break;
            case ZGrenadeMsg.Blast when !IsServer && r.Has(14):
            {
                var pos = r.Vec();
                r.U8();
                var radius = r.U8() / 10f;
                if (float.IsFinite(pos.X + pos.Y + pos.Z))
                    OnBuiltDetonated(PayloadKind.Frag, pos, radius, 0, 255, now);
                break;
            }
        }
    }

    // ---- Self-test hooks ---------------------------------------------------------------------------------------

    /// <summary>Self-test: the server's lingering patches for one tick at <paramref name="now"/>.</summary>
    public void SelfTestTickGrenadeZones(double now) => ServerTickGrenadeZones(now);

    /// <summary>Self-test: sets off <paramref name="p"/>'s remote grenades as their key would on the server.</summary>
    public void SelfTestRemoteDetonate(Player p) => ServerRemoteDetonate(p);

    /// <summary>Self-test: active patches of <paramref name="kind"/>.</summary>
    public int ActiveZones(PayloadKind kind)
    {
        var n = 0;
        foreach (var z in GrenadeZones)
            if (z.Active && z.Kind == kind)
                n++;
        return n;
    }
}
