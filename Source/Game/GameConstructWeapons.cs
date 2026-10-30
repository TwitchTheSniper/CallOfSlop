using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Construct weapons in the game (milestone 3; docs/sandbox/CONSTRUCTS.md, "Construct weapons"): the game is the guns'
/// targets (players and bots by their hit boxes, zombies when there is a horde: <see cref="IConstructTargets"/>), whose
/// damage is credited to the gunner, else to whoever last built on the construct (a sentry), else to nobody; the fire
/// events go out each tick (Msg.CFire) and are drawn where they happen: tracers from the gun's muzzle as each machine
/// draws it, muzzle flashes, blasts, the reports. The guns themselves are server-side in Source/Constructs/ConstructWeapons.cs.
/// </summary>
public sealed partial class Game : IConstructTargets
{
    private Zombie? _cwZombie; // the zombie the last RayTarget found
    private readonly CFireEvent[] _cwIn = new CFireEvent[256];

    /// <summary>Fire events drawn on this machine (the self-test: a client sees the server's guns fire).</summary>
    public int ConstructShotsSeen, ConstructBlastsSeen, ConstructShattersSeen; // (the self-test: fire events drawn here)

    private const int CwZombieTarget = 1000;

    bool IConstructTargets.RayTarget(Vector3 o, Vector3 d, float maxDist, ushort ignoreBody, out float dist, out int id)
    {
        dist = maxDist;
        id = -1;
        foreach (var p in Players.Values)
        {
            if (!p.Alive)
                continue;
            ref var h = ref SandboxHands.Hand(p.Id);
            if (h.Seated && h.SeatBody == ignoreBody)
                continue; // (its own riders: the gunner sits behind the gun)
            if (World.Hitbox(p.Position, p.Sim.Stance, p.Yaw).Raycast(o, d, dist) is { } td && td < dist)
            {
                dist = td;
                id = p.Id;
            }
        }
        if (Horde.Count > 0)
        {
            var zd = dist;
            if (ZombieRayHit(o, d, ref zd, ServerTick, out _) is { } z && zd < dist)
            {
                dist = zd;
                id = CwZombieTarget;
                _cwZombie = z;
            }
        }
        return id >= 0;
    }

    /// <summary>Who a construct's shots are credited to: the gunner in the gun's seat, else whoever last built on it, else nobody.</summary>
    private Player? ConstructCredit(Construct? by, int gunPart)
    {
        if (by == null)
            return null;
        var k = by.GunOf(gunPart);
        if (k >= 0 && by.Guns[k].Seat is var seat and >= 0 && seat < by.PartCount && by.Parts[seat].SitterPlus1 is var sp and > 0 && Players.TryGetValue((byte)(sp - 1), out var gunner))
            return gunner;
        return by.Owner != 255 && Players.TryGetValue(by.Owner, out var owner) ? owner : null;
    }

    private static byte GunKillId(Construct by, int gunPart) => by.GunOf(gunPart) is var k and >= 0 && by.Guns[k].Def is { } w ? w.Id : Weapons.DefaultPrimary.Id;

    void IConstructTargets.HitTarget(int id, float damage, GunEffect effects, Construct by, int gunPart, Vector3 at, Vector3 dir)
    {
        var credit = ConstructCredit(by, gunPart);
        var amount = (int)MathF.Round(damage);
        var killId = GunKillId(by, gunPart);
        var now = SandboxNow;
        if (id == CwZombieTarget)
        {
            if (_cwZombie is { } z && credit != null && amount > 0)
                DamageZombie(credit, z, amount, false, melee: false, now);
            _cwZombie = null;
            return;
        }
        if (!Players.TryGetValue((byte)id, out var victim) || !victim.Alive)
            return;
        var by2 = credit ?? victim; // (nobody to credit: like a driverless car, the victim's own)
        var hp = victim.Health;
        DamagePlayer(by2, victim, amount, false, killId, now, DamageKind.Bullet, at - dir * 2f);
        if ((effects & GunEffect.Incendiary) != 0 && victim.Alive && victim.Health < hp)
            IgnitePlayer(victim, by2, MathF.Max(ZRules.BurnMin, amount * ZRules.BurnFraction), killId, now);
    }

    void IConstructTargets.BlastTargets(Vector3 at, float radius, float damage, Construct? by)
    {
        var credit = by != null && by.Owner != 255 && Players.TryGetValue(by.Owner, out var o) ? o : null;
        var now = SandboxNow;
        foreach (var victim in Players.Values)
        {
            if (!victim.Alive)
                continue;
            var to = victim.Chest - at;
            var dist = to.Length();
            if (dist > radius)
                continue;
            if (dist > 0.3f && World.Query.Raycast(at, to / dist, dist, QueryFilter.All) < dist - 0.05f)
                continue; // walls shield
            var k = 1f - dist / radius;
            var amount = (int)MathF.Round(damage * k * k * 0.5f); // (players take half what parts do: a cannon shell shouldn't one-shot from across the yard)
            if (amount > 0)
                DamagePlayer(credit ?? victim, victim, amount, false, Throwables.KillIdBase, now, DamageKind.Explosive, at);
        }
        if (Horde.Count > 0 && credit != null)
            BlastZombies(credit, at, radius, damage, now);
    }

    /// <summary>Server, after the constructs' step: the tick's fire events go out (and the host draws them).</summary>
    private void SandboxFireFlush(double now)
    {
        var w = Constructs;
        // Glass that broke since the last flush (shots between ticks and the guns' rounds): shards on every PC.
        for (var k = 0; k < w.Shattered.Count && w.FireCount < w.Fires.Length; k++)
            w.Fires[w.FireCount++] = new CFireEvent { Kind = CFireKind.Shatter, At = w.Shattered[k].Centre, Half = w.Shattered[k].Half };
        w.Shattered.Clear();
        if (w.FireCount > 0)
        {
            ConstructNet.WriteFires(SbW, w);
            Net.Broadcast(SbW.Span, PacketReliability.ReliableOrdered);
            for (var k = 0; k < w.FireCount; k++)
                ConstructFireFx(w.Fires[k], now);
        }
        for (var from = 0; from < w.AmmoDirty.Count;)
        {
            from = ConstructNet.WriteAmmo(SbW, w, w.AmmoDirty, from);
            Net.Broadcast(SbW.Span, PacketReliability.ReliableOrdered);
        }
        w.AmmoDirty.Clear();
    }

    /// <summary>Client: a Msg.CFire: each event drawn.</summary>
    private void OnConstructFire(ref PacketReader r, double now)
    {
        var n = ConstructNet.ReadFires(ref r, _cwIn);
        for (var k = 0; k < n; k++)
            ConstructFireFx(_cwIn[k], now);
    }

    /// <summary>One fire event where it happens (host and clients): a shot's tracer from the gun's muzzle as drawn here, its flash and report; a blast or a cook-off.</summary>
    private void ConstructFireFx(in CFireEvent e, double now)
    {
        if (e.Kind == CFireKind.Shatter)
        {
            // A glass part broke: small shards burst out of where it was (the prop debris pool), and the glass sound.
            ConstructShattersSeen++;
            var half = Vector3.Max(e.Half, new Vector3(0.01f));
            var area = half.X * half.Y + half.Y * half.Z + half.X * half.Z;
            PropDebris.SpawnBox(new Box(e.At, half), e.At + new Vector3(0, 0.3f, 0), PropDamage.Bullet, (int)(e.At.X * 131 + e.At.Z * 977) ^ (int)(PropDebris.Spawned & 0xFFFF),
                Math.Clamp((int)(area * 10), 6, 24), 0.03f, 0.1f, 2.5f, Map.GlassColor, new Vector3(0.85f, 0.93f, 0.97f));
            Audio.Effect3D(Sfx.BreakGlass, e.At, 0.85f);
            return;
        }
        if (e.Kind != CFireKind.Shot)
        {
            ConstructBlastsSeen++;
            var r = MathF.Max(0.5f, e.Radius);
            Blasts.Add(new Blast(e.At, now, r, ThrowableKind.Frag, e.Kind == CFireKind.CookOff ? new Vector3(1f, 0.55f, 0.2f) : default));
            if (Blasts.Count > MaxBlasts)
                Blasts.RemoveAt(0);
            Audio.Effect3D(Sfx.Explosion, e.At, e.Kind == CFireKind.CookOff ? 1f : Math.Clamp(r / 3f, 0.4f, 1f));
            if (Local is { } me)
                Shake = MathF.Max(Shake, Math.Clamp(r / 3f - Vector3.Distance(me.RenderEye, e.At) / 20f, 0f, 1f));
            return;
        }
        ConstructShotsSeen++;
        if (Constructs.Get(e.Body) is not { } c || c.GunOf(e.Part) is not (var k and >= 0) || c.Guns[k].Def is not { } def)
            return;
        ConstructWeapons.MuzzleWorld(c, k, out var muzzle, out var fwd);
        ref var g = ref c.Guns[k];
        var (colour, width) = g.Fx switch
        {
            GunFx.Flame => (new Vector3(1f, 0.5f, 0.12f), 0.22f),
            GunFx.Cannon => (new Vector3(1f, 0.8f, 0.45f), 0.09f),
            GunFx.Rocket => (new Vector3(0.85f, 0.82f, 0.78f), 0.12f),
            _ => (new Vector3(1f, 0.85f, 0.5f), 0.025f + def.Caliber * 0.02f),
        };
        var speed = g.Speed > 0 ? g.Speed : g.Fx == GunFx.Flame ? 22f : 0f;
        var life = speed > 0 ? 0.25f + Vector3.Distance(muzzle, e.At) / speed : 0f;
        Tracers.Add(new Tracer { From = muzzle, To = e.At, Color = colour, Width = width, Born = now, Speed = speed, Life = MathF.Min(life, 1.5f) });
        ConstructRender.AddFlash(muzzle, fwd, now, g.Fx);
        // One report a pull (the first pellet's event), not one per pellet.
        if (_cwLastReport.Body != e.Body || _cwLastReport.Part != e.Part || now - _cwLastReport.At > 0.03)
        {
            _cwLastReport = (e.Body, e.Part, now);
            if (g.Fx == GunFx.Flame)
                Audio.Effect3D(Sfx.NapalmIgnite, muzzle, 0.5f);
            else
                Audio.Shot(def, muzzle);
        }
    }

    private (ushort Body, byte Part, double At) _cwLastReport;
}
