using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>A special ammo effect players see go off (sent in a <see cref="ZEventKind.Fx"/>; drawn as a short flash).</summary>
public enum ZFxKind : byte
{
    Splash,
    Shock,
    Ignite,
    Pierce,
    Bounce,
    Bonk,
    Squeak,
    Fish,
    Party,
    COUNT,
}

/// <summary>One zombie a shot or swing hit, for the special ammo effects: the damage it took, whether it died, the ray that hit it.</summary>
public readonly record struct ZFxHit(Zombie Z, int Damage, bool Killed, (Vector3 O, Vector3 D, float Dist) Ray);

/// <summary>
/// Special ammo effects against zombies (milestone 3; docs/zombies/ZOMBIES-MODE.md, "Special ammo effects"): the flags
/// parts carry (Parts/*: "flags") come alive in Zombies. A build's flags are a set (two incendiary receivers are still
/// one "incendiary"), and each effect goes off at most once per trigger pull or swing: effects on the zombie hit (a
/// burn, a slow, a stun, a knockback) once per zombie hit, area effects (a splash, a chain, a pierce, a bounce) once per
/// shot, from the zombie that took the most damage. Their own damage never sets off more effects. The numbers are in
/// Parts/Tables/ZombiesRules.json ("ammoEffects", proposed). Server-side; clients see a flash and hear it
/// (<see cref="ZEventKind.Fx"/>), and burning or zapped zombies glow (two bits in <see cref="Msg.ZEnts"/>).
/// </summary>
public sealed partial class Game
{
    [Flags]
    private enum ZEffect : uint
    {
        None = 0,
        Incendiary = 1,
        Explosive = 2,
        Shock = 4,
        Penetrating = 8,
        Plasma = 16,
        Bouncy = 32,
        Squeaky = 64,
        Toasty = 128,
        Rocket = 256,
        Nails = 512,
        Chainsaw = 1024,
        Bonk = 2048,
        Fish = 4096,
        Party = 8192,
        Refreshing = 16384,
        Bayonet = 32768,
        Launch = 65536,
    }

    /// <summary>The part flags that do something to zombies (the rest, like "suppressed" or "cart", don't).</summary>
    private static readonly Dictionary<string, ZEffect> EffectByFlag = new(StringComparer.OrdinalIgnoreCase)
    {
        ["incendiary"] = ZEffect.Incendiary, ["explosive"] = ZEffect.Explosive, ["shock"] = ZEffect.Shock, ["penetrating"] = ZEffect.Penetrating,
        ["plasma"] = ZEffect.Plasma, ["bouncy"] = ZEffect.Bouncy, ["squeaky"] = ZEffect.Squeaky, ["toasty"] = ZEffect.Toasty,
        ["rocket"] = ZEffect.Rocket, ["nails"] = ZEffect.Nails, ["chainsaw"] = ZEffect.Chainsaw, ["bonk"] = ZEffect.Bonk,
        ["fish"] = ZEffect.Fish, ["party"] = ZEffect.Party, ["refreshing"] = ZEffect.Refreshing, ["bayonet"] = ZEffect.Bayonet,
        ["launch"] = ZEffect.Launch,
    };

    /// <summary>The gun in hand has the "marks" flag (the All-Seeing Eye): the HUD marks every zombie through walls (TacticalHud.cs).</summary>
    public bool MarksZombies => Array.IndexOf(Active.Def.BuildFlags, "marks") >= 0;

    /// <summary>The flags with a Zombies effect (for the docs and the self-test).</summary>
    public static IReadOnlyCollection<string> ZombieEffectFlags => EffectByFlag.Keys;

    private static uint ZEffectFlags(string[] flags)
    {
        uint e = 0;
        foreach (var f in flags)
            if (EffectByFlag.TryGetValue(f, out var k))
                e |= (uint)k;
        return e;
    }

    private readonly List<ZFxHit> _zFxHits = new();
    private readonly List<(Zombie Z, float D)> _zFxNear = new();

    /// <summary>Effects players see: (local time, kind, where), the last second's worth.</summary>
    public readonly List<(double Time, ZFxKind Kind, Vector3 At)> ZFxList = new();

    /// <summary>Server: the special ammo effects of one trigger pull or swing (<paramref name="hits"/>: every zombie it hit).</summary>
    private void ApplyZombieEffects(Player by, string[] flags, List<ZFxHit> hits, double now)
    {
        var e = (ZEffect)ZEffectFlags(flags);
        if (e == ZEffect.None || hits.Count == 0)
            return;
        var main = hits[0];
        foreach (var h in hits)
            if (h.Damage > main.Damage)
                main = h;

        // On each zombie hit (once each).
        foreach (var h in hits)
        {
            var z = h.Z;
            if (h.Killed)
            {
                if ((e & ZEffect.Party) != 0)
                {
                    z.Floaty = true; // helium balloons: the body floats off
                    Fx(ZFxKind.Party, z.Position + new Vector3(0, 1.6f, 0), Sfx.Bounce);
                }
                if ((e & ZEffect.Refreshing) != 0 && by.Alive)
                    by.Health = Math.Min(World.MaxHealth, by.Health + ZRules.RefreshHealth); // the cup holder: a sip per kill
                continue;
            }
            if ((e & ZEffect.Incendiary) != 0)
                Ignite(z, by, MathF.Max(ZRules.BurnMin, h.Damage * ZRules.BurnFraction), now);
            if ((e & ZEffect.Toasty) != 0)
            {
                Ignite(z, by, MathF.Max(ZRules.BurnMin * 0.5f, h.Damage * ZRules.BurnFraction * 0.5f), now);
                Pop(z, now); // the toaster pops it up
            }
            if ((e & (ZEffect.Nails | ZEffect.Chainsaw)) != 0)
                Ignite(z, by, h.Damage * ZRules.BleedFraction, now); // a bleed (the same slot as a burn: the stronger one ticks)
            if ((e & ZEffect.Chainsaw) != 0)
                Stun(z, 0.3, now);
            if ((e & ZEffect.Plasma) != 0)
            {
                z.SlowUntil = Math.Max(z.SlowUntil, now + ZRules.SlowSeconds);
                z.ZapUntil = Math.Max(z.ZapUntil, now + ZRules.SlowSeconds);
            }
            if ((e & ZEffect.Squeaky) != 0)
                Stun(z, ZRules.SqueakStun, now);
            if ((e & ZEffect.Bonk) != 0)
                Stun(z, ZRules.BonkStun, now);
            if ((e & ZEffect.Launch) != 0)
                Launch(z, h.Ray.O, now); // (the late-game heads and rounds: a much bigger throw than a knockback, up and away)
            else if ((e & (ZEffect.Rocket | ZEffect.Fish)) != 0)
                Knock(z, h.Ray.O, (e & ZEffect.Rocket) != 0 ? ZRules.Knockback : ZRules.Knockback * 0.75f, now);
        }
        var at = main.Z.Position + new Vector3(0, 1.2f, 0);
        if ((e & (ZEffect.Incendiary | ZEffect.Toasty)) != 0 && !main.Killed)
            Fx(ZFxKind.Ignite, at, Sfx.NapalmIgnite);
        if ((e & ZEffect.Bonk) != 0)
            Fx(ZFxKind.Bonk, at, Sfx.FriendlyHit);
        if ((e & ZEffect.Squeaky) != 0)
            Fx(ZFxKind.Squeak, at, Sfx.Bounce);
        if ((e & ZEffect.Fish) != 0)
            Fx(ZFxKind.Fish, at, Sfx.GibSquelch);

        // Once per shot, from the zombie that took the most.
        if ((e & ZEffect.Explosive) != 0)
            Splash(by, main.Z, at, main.Damage * ZRules.SplashFraction, hits, now);
        if ((e & ZEffect.Shock) != 0)
            Chain(by, main.Z, main.Damage * ZRules.ShockFraction, hits, now);
        if ((e & ZEffect.Penetrating) != 0)
            Pierce(by, main, hits, now);
        if ((e & ZEffect.Bouncy) != 0)
            Bounce(by, main.Z, main.Damage * ZRules.BounceFraction, hits, now);
    }

    private static bool InHits(List<ZFxHit> hits, Zombie z)
    {
        foreach (var h in hits)
            if (h.Z == z)
                return true;
        return false;
    }

    /// <summary>A burn (or bleed) of <paramref name="total"/> damage over the burn time. A new one replaces a weaker one and restarts the clock; never stacks.</summary>
    private void Ignite(Zombie z, Player by, float total, double now)
    {
        if (total <= 0 || z.State == ZState.Dead)
            return;
        var perTick = total / MathF.Max(1, ZRules.BurnSeconds * 4f);
        var burning = z.BurnUntil > now;
        if (!burning || perTick >= z.BurnPerTick)
        {
            z.BurnPerTick = perTick;
            z.BurnBy = by.Id;
        }
        if (!burning)
            z.BurnNext = now + 0.25;
        z.BurnUntil = now + ZRules.BurnSeconds;
    }

    /// <summary>Server, per zombie per tick while it burns: a quarter-second tick of damage for whoever lit it.</summary>
    private void BurnTick(Zombie z, double now)
    {
        if (now >= z.BurnNext && z.BurnNext <= z.BurnUntil + 1e-6)
        {
            z.BurnNext += 0.25;
            if (Players.TryGetValue(z.BurnBy, out var by))
                DamageZombie(by, z, Math.Max(1, (int)MathF.Round(z.BurnPerTick)), false, melee: false, now, dot: true);
        }
        if (now > z.BurnUntil)
        {
            z.BurnUntil = 0;
            z.BurnPerTick = 0;
        }
    }

    /// <summary>Holds a zombie still for a moment (not while it climbs in or rises: it stays on its path).</summary>
    private static void Stun(Zombie z, double seconds, double now)
    {
        if (seconds <= 0 || z.State is ZState.Climbing or ZState.Rising or ZState.Dead)
            return;
        z.StunnedUntil = Math.Max(z.StunnedUntil, now + seconds);
    }

    /// <summary>Throws a zombie away from <paramref name="from"/> (a rocket booster, a frozen fish).</summary>
    private static void Knock(Zombie z, Vector3 from, float speed, double now)
    {
        if (speed <= 0 || z.State is not (ZState.Chase or ZState.Attack or ZState.ToWindow))
            return;
        var away = z.Position - from;
        away.Y = 0;
        away = away.LengthSquared() > 1e-4f ? Vector3.Normalize(away) : World.FlatForward(z.Yaw) * -1f;
        z.Sim.Velocity = away * speed + new Vector3(0, 2.5f, 0);
        z.Sim.Grounded = false;
        Stun(z, 0.5, now);
    }

    /// <summary>Launch (Wrecking Ball, Swordfish, Grandfather Clock, Jet Engine, Bowling Ball): the zombie is thrown away and up, and stunned while it flies.</summary>
    private void Launch(Zombie z, Vector3 from, double now)
    {
        if (z.State is not (ZState.Chase or ZState.Attack or ZState.ToWindow))
            return;
        var away = z.Position - from;
        away.Y = 0;
        away = away.LengthSquared() > 1e-4f ? Vector3.Normalize(away) : World.FlatForward(z.Yaw) * -1f;
        z.Sim.Velocity = away * ZRules.LaunchSpeed + new Vector3(0, ZRules.LaunchUp, 0);
        z.Sim.Grounded = false;
        Stun(z, ZRules.LaunchStun, now);
    }

    /// <summary>The toaster: the zombie pops up off the floor.</summary>
    private static void Pop(Zombie z, double now)
    {
        if (z.State is not (ZState.Chase or ZState.Attack or ZState.ToWindow))
            return;
        z.Sim.Velocity.Y = 4.5f;
        z.Sim.Grounded = false;
        Stun(z, 0.4, now);
    }

    /// <summary>Explosive: the zombies round the one hit (not those the shot hit itself) take damage falling off with distance; walls shield.</summary>
    private void Splash(Player by, Zombie centre, Vector3 at, float damage, List<ZFxHit> hits, double now)
    {
        Fx(ZFxKind.Splash, at, Sfx.Explosion);
        var r = ZRules.SplashRadius;
        foreach (var z in Horde)
        {
            if (z == centre || z.State == ZState.Dead || InHits(hits, z))
                continue;
            var to = z.Position + new Vector3(0, 1.1f, 0) - at;
            var d = to.Length();
            if (d > r)
                continue;
            if (d > 0.3f && World.Query.Raycast(at, to / d, d, QueryFilter.Shots) < d - 0.05f)
                continue;
            DamageZombie(by, z, Math.Max(1, (int)MathF.Round(damage * (1f - d / r * 0.6f))), false, melee: false, now, explosive: true);
        }
    }

    /// <summary>Shock: arcs from the zombie hit to the nearest others in range (not through walls), stunning them.</summary>
    private void Chain(Player by, Zombie from, float damage, List<ZFxHit> hits, double now)
    {
        _zFxNear.Clear();
        var r2 = ZRules.ShockRange * ZRules.ShockRange;
        foreach (var z in Horde)
        {
            if (z == from || z.State == ZState.Dead || InHits(hits, z))
                continue;
            var d2 = Vector3.DistanceSquared(z.Position, from.Position);
            if (d2 <= r2 && ZClearShot(from.Position + new Vector3(0, 1.2f, 0), z.Position + new Vector3(0, 1.2f, 0)))
                _zFxNear.Add((z, d2));
        }
        _zFxNear.Sort((a, b) => a.D != b.D ? a.D.CompareTo(b.D) : a.Z.Id.CompareTo(b.Z.Id));
        from.ZapUntil = Math.Max(from.ZapUntil, now + 0.5);
        Fx(ZFxKind.Shock, from.Position + new Vector3(0, 1.2f, 0), Sfx.EmpBurst);
        for (var i = 0; i < _zFxNear.Count && i < ZRules.ShockTargets; i++)
        {
            var z = _zFxNear[i].Z;
            z.ZapUntil = Math.Max(z.ZapUntil, now + 0.5);
            Stun(z, ZRules.ShockStun, now);
            Fx(ZFxKind.Shock, z.Position + new Vector3(0, 1.2f, 0), Sfx.HitTick);
            DamageZombie(by, z, Math.Max(1, (int)MathF.Round(damage)), false, melee: false, now);
        }
    }

    /// <summary>Penetrating: the round goes on along its ray through the next zombies behind (until a wall).</summary>
    private void Pierce(Player by, ZFxHit main, List<ZFxHit> hits, double now)
    {
        var (o, d, dist) = main.Ray;
        if (d.LengthSquared() < 0.5f)
            return;
        var end = MathF.Min(dist + ZRules.PierceRange, World.Query.Raycast(o, d, dist + ZRules.PierceRange, QueryFilter.Shots));
        _zFxNear.Clear();
        foreach (var z in Horde)
        {
            if (z.State == ZState.Dead || InHits(hits, z))
                continue;
            if (World.Hitbox(z.Position, Stance.Stand, z.Yaw).Raycast(o, d, end) is { } t && t > dist)
                _zFxNear.Add((z, t));
        }
        if (_zFxNear.Count == 0)
            return;
        _zFxNear.Sort((a, b) => a.D != b.D ? a.D.CompareTo(b.D) : a.Z.Id.CompareTo(b.Z.Id));
        for (var i = 0; i < _zFxNear.Count && i < ZRules.PierceTargets; i++)
        {
            Fx(ZFxKind.Pierce, o + d * _zFxNear[i].D, Sfx.FleshHit3D);
            DamageZombie(by, _zFxNear[i].Z, Math.Max(1, (int)MathF.Round(main.Damage * ZRules.PierceFraction)), false, melee: false, now);
        }
    }

    /// <summary>Bouncy: the round (a rubber duck) bounces on to the nearest other zombie in range.</summary>
    private void Bounce(Player by, Zombie from, float damage, List<ZFxHit> hits, double now)
    {
        Zombie? best = null;
        var bestD = ZRules.BounceRange * ZRules.BounceRange;
        foreach (var z in Horde)
        {
            if (z == from || z.State == ZState.Dead || InHits(hits, z))
                continue;
            var d2 = Vector3.DistanceSquared(z.Position, from.Position);
            if (d2 < bestD && ZClearShot(from.Position + new Vector3(0, 1.2f, 0), z.Position + new Vector3(0, 1.2f, 0)))
            {
                bestD = d2;
                best = z;
            }
        }
        if (best == null)
            return;
        Fx(ZFxKind.Bounce, best.Position + new Vector3(0, 1.2f, 0), Sfx.Bounce);
        DamageZombie(by, best, Math.Max(1, (int)MathF.Round(damage)), false, melee: false, now);
    }

    // ---- What players see ---------------------------------------------------------------------------------

    /// <summary>Server: an effect went off at <paramref name="at"/>: the host sees and hears it, every client is told.</summary>
    private void Fx(ZFxKind kind, Vector3 at, Sfx sound)
    {
        LocalFx(kind, at, sound, Brutal.GlfwApi.Glfw.Time);
        if (Net.IsOffline)
            return;
        _w.Begin(Msg.ZEvent).U8((byte)ZEventKind.Fx).U16((ushort)kind).U8((byte)sound).Str("").F32(at.X).F32(at.Y).F32(at.Z);
        Net.Broadcast(_w.Span, PacketReliability.Unreliable);
    }

    /// <summary>Both sides: an effect's flash and sound here.</summary>
    private void LocalFx(ZFxKind kind, Vector3 at, Sfx sound, double now)
    {
        for (var i = ZFxList.Count - 1; i >= 0; i--)
            if (now - ZFxList[i].Time > 1.0 || now < ZFxList[i].Time - 1.0)
                ZFxList.RemoveAt(i);
        if (ZFxList.Count >= 48)
            ZFxList.RemoveAt(0);
        ZFxList.Add((now, kind, at));
        if (sound < Sfx.COUNT)
            Audio.Effect3D(sound, at, kind == ZFxKind.Splash ? 0.55f : 0.75f, MixBus.Zombies);
    }

    // ---- Self-test hooks ------------------------------------------------------------------------------

    /// <summary>Self-test: the special effects of a hit with these flags on <paramref name="z"/> (as if a shot from <paramref name="by"/> did <paramref name="damage"/>).</summary>
    public void SelfTestEffects(Player by, string[] flags, Zombie z, int damage, double now)
    {
        var killed = DamageZombie(by, z, damage, false, melee: false, now);
        var hits = new List<ZFxHit> { new(z, damage, killed, (by.Eye, Vector3.Normalize(z.Position + new Vector3(0, 1.2f, 0) - by.Eye), Vector3.Distance(by.Eye, z.Position + new Vector3(0, 1.2f, 0)))) };
        ApplyZombieEffects(by, flags, hits, now);
    }

    /// <summary>Self-test: one server tick of the horde (burns tick here).</summary>
    public void SelfTestTickHorde(double now)
    {
        ServerTick++;
        _zNow = now;
        if (World.Map.Zombies is { } zm)
            TickHorde(zm, now);
    }
}
