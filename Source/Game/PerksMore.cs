using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The later perks' game-side pieces that don't fit in one existing spot: Scavenger's restock (server and local
/// counts), and the banners for Scavenger, Quick Fix and Hardline. The rest of the new perks are one-line hooks
/// next to the systems they change (see the helpers at the end of Perks.cs).
/// </summary>
public sealed partial class Game
{
    // ---- Scavenger -------------------------------------------------------------------------

    /// <summary>Server, after <paramref name="attacker"/> killed someone else: Scavenger gives back a grenade and an equipment charge.</summary>
    private void ScavengeOnKill(Player attacker)
    {
        if (!attacker.Alive || !attacker.Perks.Has(PerkId.Scavenger))
            return;
        attacker.GrenadesLeft = Math.Min(Throwables.Get(attacker.Grenade).PerLife, attacker.GrenadesLeft + Perks.ScavengerGrenades);
        attacker.EquipmentLeft = Math.Min(Throwables.Get(attacker.Equipment).PerLife, attacker.EquipmentLeft + Perks.ScavengerEquipment);
    }

    /// <summary>
    /// Both sides, for every kill message: the local player's own throwable counts follow the server's Scavenger
    /// restock (the counts aren't sent; both sides apply the same rule from the replicated perks).
    /// </summary>
    private void ScavengeLocal(in KillPacket kill, double now)
    {
        if (kill.Killer != LocalId || kill.Victim == LocalId || kill.KillerHp == 0 || Local is not { Alive: true } me || !me.Perks.Has(PerkId.Scavenger))
            return;
        var g = Math.Min(Throwables.Get(LocalGrenade).PerLife, GrenadesLeft + Perks.ScavengerGrenades);
        var e = Math.Min(Throwables.Get(LocalEquipment).PerLife, EquipmentLeft + Perks.ScavengerEquipment);
        if (g <= GrenadesLeft && e <= EquipmentLeft)
            return;
        GrenadesLeft = Math.Max(g, GrenadesLeft);
        EquipmentLeft = Math.Max(e, EquipmentLeft);
        ShowPerk("SCAVENGER - restocked", now);
    }

    // ---- Banners (local feedback) -----------------------------------------------------------

    private bool _wasRegenerating;
    private int _hardlineReady = -1; // -1: not seen yet (no banner for streaks that were already ready)

    /// <summary>Banners for Quick Fix (healing starts early) and Hardline (a streak earned a kill early). Called from UpdatePerkFeedback.</summary>
    private void UpdateMorePerkFeedback(Player me, double now)
    {
        if (!me.Alive)
        {
            _wasRegenerating = false;
            _hardlineReady = -1;
            return;
        }
        if (me.Regenerating && !_wasRegenerating && me.Perks.Has(PerkId.QuickFix) && me.Health < World.MaxHealth)
            ShowPerk("QUICK FIX - healing", now);
        _wasRegenerating = me.Regenerating;

        var ready = (int)me.Streak.Ready;
        if (_hardlineReady >= 0 && me.Perks.Has(PerkId.Hardline))
            foreach (var id in me.Streak.Slots.Ids)
                if ((ready & StreakState.Bit(id)) != 0 && (_hardlineReady & StreakState.Bit(id)) == 0)
                {
                    ShowPerk($"HARDLINE - {KillStreaks.Get(id).Name} one kill early", now);
                    break;
                }
        _hardlineReady = ready;
    }

    // ---- Self-test hooks --------------------------------------------------------------------

    /// <summary>A thrown <paramref name="kind"/> of <paramref name="owner"/>'s goes off at <paramref name="pos"/> (server).</summary>
    public void SelfTestDetonate(Player owner, ThrowableKind kind, Vector3 pos, double now) =>
        ServerDetonate(new Projectile { Id = 60000, Kind = kind, Owner = owner.Id, Pos = pos, Born = now }, now);

    /// <summary>Who an enemy turret or drone of <paramref name="owner"/>'s at <paramref name="from"/> would lock onto (no cone).</summary>
    public Player? SelfTestMachineTarget(Player owner, Vector3 from, float range) => StreakFindTarget(owner, from, range, 0, -2, byte.MaxValue);
}
