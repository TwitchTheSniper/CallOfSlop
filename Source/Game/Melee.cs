using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What hurt someone (survival perks care: Flak Jacket only stops explosions, Entrenched only bullets).</summary>
public enum DamageKind : byte
{
    Bullet,
    Explosive,
    Melee,
}

/// <summary>
/// Melee (a quick rifle-butt / pistol-whip on V) and the server-side parts of perks.
/// Melee is resolved on the server with the same lag compensation as shots: you hit whoever you
/// were looking at, within reach, on your screen. Two hits kill; one from behind always does.
/// </summary>
public sealed partial class Game
{
    public const float MeleeRange = 2.4f; // from your eyes to the edge of their body
    public const float MeleeDamage = 55f; // two hits
    public const float MeleeBackstabDamage = 150f; // from behind: always a kill
    public const double MeleeCooldown = 0.8;
    public const double MeleeRecovery = 0.45; // can't shoot for this long after a swing
    public const float MeleeConeCos = 0.77f; // ~40 degrees either side of where you look
    public const byte MeleeKillId = 239; // kill-feed id (weapons are below, throwables 240+)

    /// <summary>When the local swing started (drives the viewmodel animation).</summary>
    public double MeleeStarted = -10;
    private double _nextMelee;

    /// <summary>0..1 through the local swing animation, or -1 when not swinging.</summary>
    public float MeleeProgress(double now)
    {
        var t = (now - MeleeStarted) / 0.35;
        return t is >= 0 and < 1 ? (float)t : -1f;
    }

    /// <summary>Melee key pressed.</summary>
    public void TryMelee(double now)
    {
        if (Local is not { Alive: true } me || Phase != MatchPhase.Playing || now < _nextMelee || me.Sim.MantleTicks > 0)
            return;
        _nextMelee = now + MeleeCooldown;
        MeleeStarted = now;
        Active.ReloadUntil = 0; // a swing cancels a reload
        _burstLeft = 0;
        NextFireTime = Math.Max(NextFireTime, now + MeleeRecovery);
        Audio.Effect(Sfx.MeleeSwing, 0.7f);

        if (IsServer)
        {
            ServerMelee(me, LookYaw, LookPitch, Math.Max(0.0, (double)ServerTick - 1 + _alpha), now);
            return;
        }
        var tickBase = (uint)Math.Max(0, Math.Floor(RenderTick));
        _w.Begin(Msg.Melee).F32(LookYaw).F32(LookPitch).U32(tickBase).F32((float)(RenderTick - tickBase));
        Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: resolve a swing against where everyone was on the attacker's screen.</summary>
    private void ServerMelee(Player attacker, float yaw, float pitch, double viewTick, double now)
    {
        if (!attacker.Alive || now < attacker.NextMelee || Phase != MatchPhase.Playing)
            return;
        attacker.NextMelee = now + MeleeCooldown * 0.85; // a little slack for packet timing
        attacker.LastCombatTime = now;

        var origin = attacker.Eye;
        var dir = World.Forward(yaw, pitch);
        var rewindTo = Math.Max(viewTick, (double)ServerTick - World.MaxRewindTicks);
        Player? victim = null;
        HistoryEntry victimAt = default;
        var best = float.MaxValue;
        foreach (var p in Players.Values)
        {
            if (p == attacker || !p.Alive || p.SpawnTick > rewindTo)
                continue;
            var at = HistoricState(p, rewindTo);
            var box = World.Hitbox(at.Position, at.Stance, at.Yaw);
            // Closest point of their body to our eyes: within reach and roughly where we're looking.
            var closest = Vector3.Clamp(origin, box.Min, box.Max);
            var to = closest - origin;
            var dist = to.Length();
            if (dist > MeleeRange)
                continue;
            var toCenter = box.Center - origin;
            var aimed = box.Raycast(origin, dir, MeleeRange) != null
                        || (toCenter.LengthSquared() > 1e-4f && Vector3.Dot(Vector3.Normalize(toCenter), dir) >= MeleeConeCos);
            if (!aimed)
                continue;
            // No hitting through walls.
            if (toCenter.Length() > 0.3f && World.RaycastWorld(origin, Vector3.Normalize(toCenter), toCenter.Length()) < toCenter.Length() - 0.35f)
                continue;
            if (dist < best)
            {
                best = dist;
                victim = p;
                victimAt = at;
            }
        }

        byte flags = 0;
        if (victim != null)
        {
            var backstab = Perks.IsFromBehind(dir, victimAt.Yaw);
            var dmg = (backstab ? MeleeBackstabDamage : MeleeDamage)
                      * Perks.AttackMult(attacker.Perks, best, dir, victimAt.Yaw, victim.Health, melee: true);
            flags |= 1;
            if (backstab)
                flags |= 8;
            if (DamagePlayer(attacker, victim, (int)MathF.Round(dmg), false, MeleeKillId, now, DamageKind.Melee))
                flags |= 4;
        }

        if (attacker.Id == LocalId)
            OnOwnMelee(flags, now);
        else
            PlayMeleeFx(attacker, flags);
        _w.Begin(Msg.MeleeFx).U8(attacker.Id).U8(flags);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    private void OnOwnMelee(byte flags, double now)
    {
        if ((flags & 1) == 0)
            return;
        Audio.Effect(Sfx.MeleeHit, 0.8f);
        OnOwnShotConfirmed(flags, now);
    }

    private void PlayMeleeFx(Player attacker, byte flags)
    {
        Audio.Effect3D(Sfx.MeleeSwing, attacker.RenderEye, 0.6f);
        if ((flags & 1) != 0)
            Audio.Effect3D(Sfx.MeleeHit, attacker.RenderEye, 0.8f);
    }

    private void OnMeleePacket(byte id, ref PacketReader r, RakNetGUID sender, double now)
    {
        switch (id)
        {
            case (byte)Msg.Melee when IsServer && r.Has(16):
            {
                if (!_clients.TryGetValue(sender.G, out var c) || !Players.TryGetValue(c.Id, out var p))
                    break;
                var yaw = r.F32();
                var pitch = r.F32();
                var viewTick = r.U32() + (double)Math.Clamp(r.F32(), 0f, 1f);
                if (float.IsFinite(yaw + pitch))
                    ServerMelee(p, World.WrapAngle(yaw), Math.Clamp(pitch, -1.55f, 1.55f), viewTick, now);
                break;
            }
            case (byte)Msg.MeleeFx when !IsServer && r.Has(2):
            {
                var who = r.U8();
                var flags = r.U8();
                if (who == LocalId)
                    OnOwnMelee(flags, now); // we already heard our own swing
                else if (Players.TryGetValue(who, out var p))
                    PlayMeleeFx(p, flags);
                break;
            }
        }
    }

    /// <summary>Bots: swing at anyone who gets right up close instead of shooting.</summary>
    private bool BotTryMelee(Player bot, BotBrain b, Player target, float dist, float yaw, float pitch, double now)
    {
        if (dist > MeleeRange + 0.2f || now < bot.NextMelee || b.Skill == BotDifficulty.Easy && _rng.NextDouble() < 0.7)
            return false;
        ServerMelee(bot, yaw, pitch, ServerTick, now);
        b.NextFire = Math.Max(b.NextFire, now + MeleeRecovery);
        return true;
    }

    // ---- Perks (server side) -------------------------------------------------------------

    /// <summary>Gives a player their perks for this life.</summary>
    private void ApplyPerks(Player p, PerkSet perks)
    {
        p.Perks = perks.Sanitized();
        p.Sim.MovePerk = p.Perks.Movement;
        p.Sim.BoostTicks = p.Perks.Has(PerkId.HeadStart) ? Perks.BoostTicks : (byte)0;
        p.LastStandUsed = false;
        p.ImmuneUntil = 0;
    }

    /// <summary>Entrenched: crouched or prone and (nearly) still.</summary>
    public static bool IsDugIn(Player p) =>
        p.Sim.Stance != Stance.Stand && new Vector2(p.Sim.Velocity.X, p.Sim.Velocity.Z).Length() < 0.6f && p.Sim.Grounded;

    /// <summary>Bots carry a random perk from each category.</summary>
    private PerkSet RandomPerks()
    {
        PerkId Pick(PerkCategory c)
        {
            var options = Perks.In(c).ToArray();
            return options[_rng.Next(options.Length)].Id;
        }
        return new PerkSet(Pick(PerkCategory.Movement), Pick(PerkCategory.Survival), Pick(PerkCategory.Damage));
    }

    // ---- Self-test hooks --------------------------------------------------------------------

    public bool SelfTestDamage(Player attacker, Player victim, int amount, DamageKind kind, double now) =>
        DamagePlayer(attacker, victim, amount, false, 0, now, kind);

    public void SelfTestPerks(Player p, PerkSet perks) => ApplyPerks(p, perks);

    public void SelfTestMelee(Player attacker, float yaw, float pitch, double now) => ServerMelee(attacker, yaw, pitch, ServerTick, now);

    /// <summary>Puts a player alive at a spot, facing <paramref name="yaw"/>, with matching lag-compensation history.</summary>
    public void SelfTestPlace(Player p, Vector3 feet, float yaw)
    {
        var perks = p.Perks;
        p.Teleport(feet);
        p.Yaw = yaw;
        p.Alive = true;
        p.Health = World.MaxHealth;
        p.SpawnTick = 0;
        ApplyPerks(p, perks);
        for (var i = 0; i < Player.HistorySize; i++)
            p.History[i] = new HistoryEntry { Position = feet, Yaw = yaw };
    }

    // ---- Perks (local feedback) ------------------------------------------------------------

    /// <summary>Banner when one of your perks kicks in ("LAST STAND", "AFTERBURNER"...).</summary>
    public string PerkBanner = "";
    public double PerkBannerTime = -100;
    private bool _lastStandShown;
    private byte _lastBoost;
    private int _lastPerkHealth = World.MaxHealth;

    private void UpdatePerkFeedback(double now)
    {
        if (Local is not { } me)
            return;
        if (!me.Alive)
        {
            _lastStandShown = false;
            _lastBoost = 0;
            _lastPerkHealth = World.MaxHealth;
            return;
        }
        if (me.Perks.Has(PerkId.LastStand) && me.Health == 1 && !_lastStandShown)
        {
            _lastStandShown = true;
            ShowPerk("LAST STAND - you can't be hurt for a moment, get to cover!", now);
        }
        if (me.Sim.BoostTicks > _lastBoost + 20 && me.Perks.Has(PerkId.Afterburner) && _lastBoost < Perks.BoostTicks - 20)
            ShowPerk("AFTERBURNER", now);
        if (me.Perks.Has(PerkId.Bloodthirst) && me.Health == World.MaxHealth && _lastPerkHealth < World.MaxHealth && _lastPerkHealth > 0)
            ShowPerk("BLOODTHIRST - healed", now);
        _lastBoost = me.Sim.BoostTicks;
        _lastPerkHealth = me.Health;
    }

    private void ShowPerk(string text, double now)
    {
        PerkBanner = text;
        PerkBannerTime = now;
        Audio.Effect(Sfx.HitTick, 0.5f);
    }
}
