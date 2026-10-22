using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Health regeneration tuning. After <see cref="Delay"/> seconds without taking or dealing damage, health comes back up
/// to <see cref="World.MaxHealth"/>: at <see cref="Rate"/> points a second at first, speeding up by <see cref="Ramp"/>
/// every second to at most <see cref="MaxRate"/> (reached after 3 s). 1 -> 120 takes about 5.7 s once it starts
/// (<see cref="FullHealSeconds"/>; it was 8 HP/s flat before, 1 -> 100 in about 12 s). Taking or dealing damage
/// (a hit Last Stand's shield blocks counts too) restarts the wait; just firing doesn't.
/// </summary>
public static class Regen
{
    public const double Delay = 5.0;
    public const float Rate = 12f; // HP/s the moment regen starts
    public const float Ramp = 4f; // HP/s gained per second of regenerating
    public const float MaxRate = 24f; // HP/s cap

    /// <summary>Regen speed (HP/s) <paramref name="elapsed"/> seconds after it started.</summary>
    public static float RateAt(double elapsed) => MathF.Min(MaxRate, Rate + Ramp * (float)Math.Max(0, elapsed));

    /// <summary>Health regained in the first <paramref name="elapsed"/> seconds of regenerating (the integral of <see cref="RateAt"/>).</summary>
    public static float HealedBy(double elapsed)
    {
        var t = (float)Math.Max(0, elapsed);
        var rampEnd = (MaxRate - Rate) / Ramp;
        if (t <= rampEnd)
            return Rate * t + 0.5f * Ramp * t * t;
        return Rate * rampEnd + 0.5f * Ramp * rampEnd * rampEnd + MaxRate * (t - rampEnd);
    }

    /// <summary>Seconds of regenerating to get <paramref name="hp"/> back (the inverse of <see cref="HealedBy"/>).</summary>
    public static float FullHealSeconds(float hp)
    {
        var rampEnd = (MaxRate - Rate) / Ramp;
        var rampHeal = HealedBy(rampEnd);
        if (hp >= rampHeal)
            return rampEnd + (hp - rampHeal) / MaxRate;
        return (-Rate + MathF.Sqrt(Rate * Rate + 2f * Ramp * hp)) / Ramp;
    }
}

/// <summary>
/// Health on the server: regeneration, and Last Stand's shield (the lethal hit leaves you on 1 HP and the next
/// <see cref="Perks.LastStandCharges"/> bullet hits are blocked). Both are server-owned; the shield charges and the
/// regenerating flag ride in each player's snapshot flags, and every blocked hit is announced with
/// <see cref="Msg.ShieldHit"/> so everyone sees the shimmer and hears it.
/// </summary>
public sealed partial class Game
{
    /// <summary>Local player: a blocked hit tints the screen until then (Program draws it).</summary>
    public double ShieldFlashUntil;

    // ---- Server --------------------------------------------------------------------------------

    /// <summary>Last Stand kicks in: 1 HP and a full shield.</summary>
    private void StartLastStand(Player p, double now)
    {
        p.Health = 1;
        p.LastStandUsed = true;
        p.ShieldCharges = Perks.LastStandCharges;
        p.ShieldUntil = now + Perks.LastStandShieldTime;
    }

    /// <summary>
    /// A bullet hit (one shot: a shotgun blast is one hit) on <paramref name="victim"/>: if their Last Stand shield is
    /// up, it uses a charge and the hit does nothing. Returns true if it was blocked.
    /// </summary>
    private bool ShieldBlocks(Player victim, double now)
    {
        if (victim.ShieldCharges == 0)
            return false;
        if (now >= victim.ShieldUntil)
        {
            victim.ShieldCharges = 0; // ran out (the tick would have cleared it anyway)
            return false;
        }
        victim.ShieldCharges--;
        if (victim.Bot is { } brain)
            brain.LastHealth = int.MaxValue; // the bot reacts as if it had been hurt (turns to look for the shooter)
        WriteShieldHit(_w, victim.Id, victim.ShieldCharges);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        OnShieldHit(victim, now);
        return true;
    }

    /// <summary>Server, every tick: regenerate health and let Last Stand shields run out.</summary>
    private void ServerTickHealth(double now)
    {
        foreach (var p in Players.Values)
            RegenTick(p, now);
    }

    /// <summary>One tick of regeneration and shield timing for one player.</summary>
    public static void RegenTick(Player p, double now)
    {
        if (!p.Alive)
        {
            p.Regenerating = false;
            p.RegenCarry = 0;
            p.ShieldCharges = 0;
            return;
        }
        if (p.ShieldCharges > 0 && now >= p.ShieldUntil)
            p.ShieldCharges = 0;
        if (p.Health >= World.MaxHealth || now - p.LastHurtTime < Perks.RegenDelay(p.Perks)) // Quick Fix: sooner
        {
            p.Regenerating = false;
            p.RegenCarry = 0;
            return;
        }
        p.Regenerating = true;
        // Faster the longer it has been going: the regen started when the wait ran out (stateless, so it's deterministic).
        var elapsed = now - p.LastHurtTime - Perks.RegenDelay(p.Perks);
        p.RegenCarry += Regen.RateAt(elapsed) * World.TickDt;
        var whole = (int)p.RegenCarry;
        if (whole > 0)
        {
            p.RegenCarry -= whole;
            p.Health = Math.Min(World.MaxHealth, p.Health + whole);
        }
        if (p.Health >= World.MaxHealth)
        {
            p.Regenerating = false;
            p.RegenCarry = 0;
        }
    }

    // ---- Packets -------------------------------------------------------------------------------

    /// <summary>The shield hit event: player id and the charges left. 3 bytes with the message id.</summary>
    public static void WriteShieldHit(PacketWriter w, byte id, byte charges) => w.Begin(Msg.ShieldHit).U8(id).U8(charges);

    /// <summary>Reads a shield hit event (after the message id).</summary>
    public static (byte Id, byte Charges) ReadShieldHit(ref PacketReader r) => (r.U8(), r.U8());

    private void OnShieldHitPacket(ref PacketReader r, double now)
    {
        var (id, charges) = ReadShieldHit(ref r);
        if (!Players.TryGetValue(id, out var p))
            return;
        p.ShieldCharges = Math.Min(charges, Perks.LastStandCharges);
        OnShieldHit(p, now);
    }

    /// <summary>Both sides: the shimmer on the player and the zap (on your own screen too, if it's you).</summary>
    private void OnShieldHit(Player p, double now)
    {
        p.ShieldFlashTime = now;
        if (p.Id == LocalId)
        {
            ShieldFlashUntil = now + 0.3;
            Audio.Effect(Sfx.ShieldBlock, 0.8f);
        }
        else
        {
            Audio.Effect3D(Sfx.ShieldBlock, p.RenderPosition + new Vector3(0, 1.1f, 0), 1f);
        }
    }

    // ---- Self-test hooks -----------------------------------------------------------------------

    /// <summary>Self-test: one server tick of regeneration and shield timing at <paramref name="now"/>.</summary>
    public void SelfTestHealthTick(double now) => ServerTickHealth(now);
}
