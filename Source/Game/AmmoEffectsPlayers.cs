using System.Numerics;

namespace CallOfSlop;

/// <summary>One player a shot or swing with special ammo hit: the damage they took, whether they died, the ray that hit them.</summary>
public readonly record struct PFxHit(Player P, int Damage, bool Killed, Vector3 O, Vector3 D, float Dist);

/// <summary>
/// Special ammo effects against PLAYERS (developer answer, 2026-10-26; docs/zombies/ZOMBIES-MODE.md, "Special ammo
/// effects"): a gun or melee build whose parts carry effect flags hurts players the way it hurts zombies, wherever it can
/// be used against them (any mode where a build meets a player: the Workbench Range's targets today, PvP with builds
/// later). The same numbers as against zombies (ZombiesRules.json "ammoEffects"), and friendly fire is respected: every
/// bit of damage goes through <see cref="DamagePlayer"/> (no damage to teammates in team modes or co-op), and area
/// effects only pick players the shooter may damage (a teammate never soaks up a chain or a bounce). What applies:
/// burns and bleeds (damage over time), the splash, the shock chain, the pierce, the bounce, the bayonet, the cup holder's
/// sip, and the knockbacks (rocket booster, frozen fish, the toaster's pop; through the predicted knock, Knockback.cs);
/// stuns (shock, squeak, bonk, chainsaw) hold bots still as a melee build's stun does. The plasma slow and the party
/// balloons stay zombie-only (a player's movement is predicted; a slow would need its own replicated state). The shooter
/// is never hit by their own effects. No allocation per shot (reused lists).
/// </summary>
public sealed partial class Game
{
    /// <summary>The effects that also work on players.</summary>
    private const ZEffect PlayerEffects = ZEffect.Incendiary | ZEffect.Explosive | ZEffect.Shock | ZEffect.Penetrating | ZEffect.Bouncy | ZEffect.Squeaky
                                          | ZEffect.Toasty | ZEffect.Rocket | ZEffect.Nails | ZEffect.Chainsaw | ZEffect.Bonk | ZEffect.Fish
                                          | ZEffect.Refreshing | ZEffect.Bayonet;

    private readonly List<PFxHit> _pFxHits = new();
    private readonly List<(Player P, Vector3 O, Vector3 D, float Dist)> _pFxRays = new();
    private readonly List<(Player P, float D)> _pFxNear = new();
    private int _playersBurning;

    /// <summary>Does a build with these flags have any effect on players?</summary>
    public static bool HasPlayerEffects(string[] flags) => flags.Length > 0 && ((ZEffect)ZEffectFlags(flags) & PlayerEffects) != 0;

    /// <summary>The effect flags that work on players (for the docs and the self-test).</summary>
    public static IEnumerable<string> PlayerEffectFlags => EffectByFlag.Where(kv => (kv.Value & PlayerEffects) != 0).Select(kv => kv.Key);

    /// <summary>Server, per pellet that hit a player with an effect gun: remember the first ray that reached them (the pierce goes on along it).</summary>
    private void NotePlayerFxRay(Player p, Vector3 o, Vector3 d, float dist)
    {
        foreach (var r in _pFxRays)
            if (r.P == p)
                return;
        _pFxRays.Add((p, o, d, dist));
    }

    private (Vector3 O, Vector3 D, float Dist) PlayerFxRay(Player shooter, Player p)
    {
        foreach (var r in _pFxRays)
            if (r.P == p)
                return (r.O, r.D, r.Dist);
        var to = p.Chest - shooter.Eye;
        var len = MathF.Max(0.01f, to.Length());
        return (shooter.Eye, to / len, len);
    }

    /// <summary>Bayonet: a hit on a player within reach does more (as on zombies).</summary>
    private float BayonetPlayer(string[] flags, Player shooter, Player victim, float dmg) =>
        ((ZEffect)ZEffectFlags(flags) & ZEffect.Bayonet) != 0 && Vector3.Distance(shooter.Position, victim.Position) <= ZRules.BayonetRange ? dmg * ZRules.BayonetMult : dmg;

    /// <summary>Server: the special ammo effects of one trigger pull or swing on the players it hurt (<paramref name="hits"/>).</summary>
    private void ApplyPlayerEffects(Player by, string[] flags, List<PFxHit> hits, byte killId, double now)
    {
        var e = (ZEffect)ZEffectFlags(flags) & PlayerEffects;
        if (e == ZEffect.None || hits.Count == 0)
            return;
        var main = hits[0];
        foreach (var h in hits)
            if (h.Damage > main.Damage)
                main = h;

        // On each player hit (once each).
        foreach (var h in hits)
        {
            var p = h.P;
            if (h.Killed)
            {
                if ((e & ZEffect.Refreshing) != 0 && by.Alive)
                    by.Health = Math.Min(World.MaxHealth, by.Health + ZRules.RefreshHealth); // the cup holder: a sip per kill
                continue;
            }
            if (!p.Alive)
                continue;
            if ((e & ZEffect.Incendiary) != 0)
                IgnitePlayer(p, by, MathF.Max(ZRules.BurnMin, h.Damage * ZRules.BurnFraction), killId, now);
            if ((e & ZEffect.Toasty) != 0)
            {
                IgnitePlayer(p, by, MathF.Max(ZRules.BurnMin * 0.5f, h.Damage * ZRules.BurnFraction * 0.5f), killId, now);
                Knock(p, GravityFrames.UpAt(p.Position) * 4.5f); // the toaster pops them up
            }
            if ((e & (ZEffect.Nails | ZEffect.Chainsaw)) != 0)
                IgnitePlayer(p, by, h.Damage * ZRules.BleedFraction, killId, now); // a bleed (one slot with a burn: the stronger ticks)
            if ((e & ZEffect.Chainsaw) != 0)
                StunPlayer(p, 0.3, now);
            if ((e & ZEffect.Squeaky) != 0)
                StunPlayer(p, ZRules.SqueakStun, now);
            if ((e & ZEffect.Bonk) != 0)
                StunPlayer(p, ZRules.BonkStun, now);
            if ((e & (ZEffect.Rocket | ZEffect.Fish)) != 0)
            {
                var away = p.Position - h.O;
                away.Y = 0;
                away = away.LengthSquared() > 1e-4f ? Vector3.Normalize(away) : -World.FlatForward(p.Yaw);
                var speed = (e & ZEffect.Rocket) != 0 ? ZRules.Knockback : ZRules.Knockback * 0.75f;
                Knock(p, away * speed + new Vector3(0, 2.5f, 0));
            }
        }
        var at = main.P.Chest;
        if ((e & (ZEffect.Incendiary | ZEffect.Toasty)) != 0 && !main.Killed)
            Fx(ZFxKind.Ignite, at, Sfx.NapalmIgnite);
        if ((e & ZEffect.Bonk) != 0)
            Fx(ZFxKind.Bonk, at, Sfx.FriendlyHit);
        if ((e & ZEffect.Squeaky) != 0)
            Fx(ZFxKind.Squeak, at, Sfx.Bounce);
        if ((e & ZEffect.Fish) != 0)
            Fx(ZFxKind.Fish, at, Sfx.GibSquelch);

        // Once per shot, from the player who took the most.
        if ((e & ZEffect.Explosive) != 0)
            SplashPlayers(by, main.P, at, main.Damage * ZRules.SplashFraction, hits, killId, now);
        if ((e & ZEffect.Shock) != 0)
            ChainPlayers(by, main.P, main.Damage * ZRules.ShockFraction, hits, killId, now);
        if ((e & ZEffect.Penetrating) != 0)
            PiercePlayers(by, main, hits, killId, now);
        if ((e & ZEffect.Bouncy) != 0)
            BouncePlayers(by, main.P, main.Damage * ZRules.BounceFraction, hits, killId, now);
    }

    private static bool InHits(List<PFxHit> hits, Player p)
    {
        foreach (var h in hits)
            if (h.P == p)
                return true;
        return false;
    }

    /// <summary>A player an area effect from <paramref name="by"/> may reach: alive, not the shooter, not hit by the shot itself, and someone they may damage (friendly fire).</summary>
    private bool AreaTarget(Player by, Player p, Player except, List<PFxHit> hits) =>
        p.Alive && p != by && p != except && !InHits(hits, p) && CanDamage(by, p);

    /// <summary>A burn (or bleed) of <paramref name="total"/> damage over the burn time; a new one replaces a weaker one and restarts the clock, never stacks.</summary>
    private void IgnitePlayer(Player p, Player by, float total, byte killId, double now)
    {
        if (total <= 0 || !p.Alive || !CanDamage(by, p) || p == by)
            return;
        var perTick = total / MathF.Max(1, ZRules.BurnSeconds * 4f);
        var burning = p.BurnUntil > now;
        if (!burning || perTick >= p.BurnPerTick)
        {
            p.BurnPerTick = perTick;
            p.BurnBy = by.Id;
            p.BurnKillId = killId;
        }
        if (!burning)
            p.BurnNext = now + 0.25;
        p.BurnUntil = now + ZRules.BurnSeconds;
        _playersBurning++;
    }

    /// <summary>A stun holds a bot still (as a melee build's stun does; a person's movement is their own).</summary>
    private static void StunPlayer(Player p, double seconds, double now)
    {
        if (seconds > 0)
            p.StunnedUntil = Math.Max(p.StunnedUntil, now + seconds);
    }

    /// <summary>Server, every tick: burning and bleeding players take a quarter-second tick of damage from whoever lit them. Nothing to do (one compare) when nobody burns.</summary>
    private void ServerTickBurns(double now)
    {
        if (_playersBurning == 0)
            return;
        var still = 0;
        foreach (var p in Players.Values)
        {
            if (p.BurnUntil <= 0)
                continue;
            if (!p.Alive)
            {
                p.BurnUntil = 0;
                p.BurnPerTick = 0;
                continue;
            }
            if (now >= p.BurnNext && p.BurnNext <= p.BurnUntil + 1e-6)
            {
                p.BurnNext += 0.25;
                var by = Players.TryGetValue(p.BurnBy, out var b) ? b : p; // (the shooter left: the burn is nobody's but the victim's)
                DamagePlayer(by, p, Math.Max(1, (int)MathF.Round(p.BurnPerTick)), false, p.BurnKillId, now, DamageKind.Explosive); // like napalm: Flak Jacket helps
            }
            if (now > p.BurnUntil || !p.Alive)
            {
                p.BurnUntil = 0;
                p.BurnPerTick = 0;
            }
            else
                still++;
        }
        _playersBurning = still;
    }

    /// <summary>Explosive: the players round the one hit (not those the shot hit itself) take damage falling off with distance; walls shield.</summary>
    private void SplashPlayers(Player by, Player centre, Vector3 at, float damage, List<PFxHit> hits, byte killId, double now)
    {
        Fx(ZFxKind.Splash, at, Sfx.Explosion);
        var r = ZRules.SplashRadius;
        foreach (var p in Players.Values)
        {
            if (!AreaTarget(by, p, centre, hits))
                continue;
            var to = p.Chest - at;
            var d = to.Length();
            if (d > r)
                continue;
            if (d > 0.3f && World.Query.Raycast(at, to / d, d, QueryFilter.Shots) < d - 0.05f)
                continue;
            DamagePlayer(by, p, Math.Max(1, (int)MathF.Round(damage * (1f - d / r * 0.6f))), false, killId, now, DamageKind.Explosive, at);
        }
    }

    /// <summary>Shock: arcs from the player hit to the nearest others in range (not through walls), stunning them.</summary>
    private void ChainPlayers(Player by, Player from, float damage, List<PFxHit> hits, byte killId, double now)
    {
        _pFxNear.Clear();
        var r2 = ZRules.ShockRange * ZRules.ShockRange;
        foreach (var p in Players.Values)
        {
            if (!AreaTarget(by, p, from, hits))
                continue;
            var d2 = Vector3.DistanceSquared(p.Position, from.Position);
            if (d2 <= r2 && Knockback.InSight(World.Query, from.Chest, p.Chest))
                _pFxNear.Add((p, d2));
        }
        _pFxNear.Sort((a, b) => a.D != b.D ? a.D.CompareTo(b.D) : a.P.Id.CompareTo(b.P.Id));
        Fx(ZFxKind.Shock, from.Chest, Sfx.EmpBurst);
        for (var i = 0; i < _pFxNear.Count && i < ZRules.ShockTargets; i++)
        {
            var p = _pFxNear[i].P;
            StunPlayer(p, ZRules.ShockStun, now);
            Fx(ZFxKind.Shock, p.Chest, Sfx.HitTick);
            DamagePlayer(by, p, Math.Max(1, (int)MathF.Round(damage)), false, killId, now);
        }
    }

    /// <summary>Penetrating: the round goes on along its ray through the next players behind (until a wall).</summary>
    private void PiercePlayers(Player by, PFxHit main, List<PFxHit> hits, byte killId, double now)
    {
        var (o, d, dist) = (main.O, main.D, main.Dist);
        if (d.LengthSquared() < 0.5f)
            return;
        var end = MathF.Min(dist + ZRules.PierceRange, World.Query.Raycast(o, d, dist + ZRules.PierceRange, QueryFilter.Shots));
        _pFxNear.Clear();
        foreach (var p in Players.Values)
        {
            if (!AreaTarget(by, p, main.P, hits))
                continue;
            if (World.Hitbox(p.Position, p.Sim.Stance, p.Yaw).Raycast(o, d, end) is { } t && t > dist)
                _pFxNear.Add((p, t));
        }
        _pFxNear.Sort((a, b) => a.D != b.D ? a.D.CompareTo(b.D) : a.P.Id.CompareTo(b.P.Id));
        for (var i = 0; i < _pFxNear.Count && i < ZRules.PierceTargets; i++)
        {
            Fx(ZFxKind.Pierce, o + d * _pFxNear[i].D, Sfx.FleshHit3D);
            DamagePlayer(by, _pFxNear[i].P, Math.Max(1, (int)MathF.Round(main.Damage * ZRules.PierceFraction)), false, killId, now);
        }
    }

    /// <summary>Bouncy: the round (a rubber duck) bounces on to the nearest other player in range and in sight.</summary>
    private void BouncePlayers(Player by, Player from, float damage, List<PFxHit> hits, byte killId, double now)
    {
        Player? best = null;
        var bestD = ZRules.BounceRange * ZRules.BounceRange;
        foreach (var p in Players.Values)
        {
            if (!AreaTarget(by, p, from, hits))
                continue;
            var d2 = Vector3.DistanceSquared(p.Position, from.Position);
            if (d2 < bestD && Knockback.InSight(World.Query, from.Chest, p.Chest))
            {
                bestD = d2;
                best = p;
            }
        }
        if (best == null)
            return;
        Fx(ZFxKind.Bounce, best.Chest, Sfx.Bounce);
        DamagePlayer(by, best, Math.Max(1, (int)MathF.Round(damage)), false, killId, now);
    }

    // ---- Self-test hooks ------------------------------------------------------------------------------

    /// <summary>Self-test: the burn ticks alone.</summary>
    public void SelfTestTickBurns(double now) => ServerTickBurns(now);

    /// <summary>Self-test: a built melee swing by <paramref name="attacker"/> (as the server resolves one).</summary>
    public void SelfTestBuiltMelee(Player attacker, MeleeDef m, double now) => ServerBuiltMelee(attacker, m, attacker.Yaw, attacker.Pitch, ServerTick, now);
}
