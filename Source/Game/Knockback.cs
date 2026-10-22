using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Blast knockback: every explosion (frag and impact grenades, exploding props, mortar and strafe-run strikes) pushes
/// what's around it away from the blast. The strength is data: <see cref="ThrowableDef.Knockback"/>,
/// <see cref="StreakDef.Knockback"/> and a prop's "explode" "knockback" (m/s added at the centre), falling off in a
/// straight line to nothing at the blast radius, with an upward lift so a push leaves the ground. Walls (and an enemy's
/// Deployable Cover) shield you exactly as they shield you from the damage.
///
/// Living players: the server changes the victim's velocity (<see cref="Apply"/>, shared by the server and the client's
/// prediction), so it replicates through the normal snapshots; a person also gets a small reliable
/// <see cref="Msg.Knock"/> (the input sequence it went in after and the impulse), which their client puts into its
/// prediction replay at exactly that point, so the shove is predicted from the moment the client hears of it and never
/// corrected again. Rules: one push is capped at <see cref="MaxImpulse"/>, Flak Jacket takes <see cref="Perks.FlakKnockMult"/>
/// of it, prone players are pushed half as far, mantling players not at all. Teammates are never pushed by each other's
/// blasts (the game has no friendly fire, and a shove would be griefing); your own grenade pushes you (grenade jumps).
/// Strikes push only the caller's enemies, like their damage.
///
/// Corpses (<see cref="Ragdolls.Blast"/>) and prop debris (<see cref="PropDebris.Push"/>) are pushed on every PC from the
/// explosion events they already get (cosmetic, nothing extra on the wire).
/// </summary>
public static class Knockback
{
    /// <summary>Most speed (m/s) one blast adds to a living player.</summary>
    public const float MaxImpulse = 12f;

    /// <summary>Upward share added to the push direction (so a push lifts you off the ground instead of sliding you).</summary>
    public const float Lift = 0.45f;

    /// <summary>Prone players are pushed this much.</summary>
    public const float ProneMult = 0.5f;

    /// <summary>Caps on the velocity after a push: horizontal and upward (m/s).</summary>
    public const float MaxHorizontal = 15f, MaxUp = 9f;

    /// <summary>An exploding prop without its own "knockback" pushes this hard (m/s at the centre).</summary>
    public const float DefaultProp = 9f;

    /// <summary>The push (m/s) a blast at <paramref name="center"/> gives a body whose chest is at <paramref name="target"/> (zero beyond the radius). Line of sight is the caller's.</summary>
    public static Vector3 Impulse(Vector3 center, float radius, float strength, Vector3 target, bool prone, bool flak)
    {
        if (!(strength > 0) || !(radius > 0))
            return Vector3.Zero;
        var to = target - center;
        var dist = to.Length();
        if (!(dist < radius))
            return Vector3.Zero;
        var dir = dist > 0.05f ? to / dist : Vector3.UnitY;
        var push = Vector3.Normalize(dir + new Vector3(0, Lift, 0) + new Vector3(0, 1e-4f, 0));
        var mag = strength * (1f - dist / radius);
        if (prone)
            mag *= ProneMult;
        if (flak)
            mag *= Perks.FlakKnockMult;
        return push * MathF.Min(mag, MaxImpulse);
    }

    /// <summary>Is <paramref name="target"/> in the open from <paramref name="center"/> (no wall, roof or floor in between)?</summary>
    public static bool InSight(IWorldQuery world, Vector3 center, Vector3 target)
    {
        var to = target - center;
        var dist = to.Length();
        return dist <= 0.3f || world.Raycast(center, to / dist, dist, QueryFilter.All) >= dist - 0.05f;
    }

    /// <summary>
    /// Adds a push to a movement state: the velocity changes (capped), and a push with any real lift leaves the ground.
    /// The server and the client's prediction replay both call this, so they agree exactly. Mantling ignores it.
    /// </summary>
    public static void Apply(ref MoveState s, Vector3 impulse)
    {
        if (s.MantleTicks > 0 || !float.IsFinite(impulse.X + impulse.Y + impulse.Z))
            return;
        var v = s.Velocity + impulse;
        var flat = MathF.Sqrt(v.X * v.X + v.Z * v.Z);
        if (flat > MaxHorizontal)
        {
            v.X *= MaxHorizontal / flat;
            v.Z *= MaxHorizontal / flat;
        }
        v.Y = MathF.Min(v.Y, MaxUp);
        s.Velocity = v;
        if (impulse.Y > 1f)
            s.Grounded = false;
    }

    /// <summary>How hard (m/s at the centre) and how far a kill id's blast pushes: frag and impact grenades and the strikes (0 for everything else).</summary>
    public static (float Strength, float Radius) ForKill(byte killId)
    {
        if (killId >= Throwables.KillIdBase && killId != Game.SuicideKillId)
        {
            var def = Throwables.Get((ThrowableKind)(killId - Throwables.KillIdBase));
            return (def.Knockback, def.Radius);
        }
        if (KillStreaks.IsStreakKill(killId))
        {
            var id = KillStreaks.FromKillId(killId);
            var def = KillStreaks.Get(id);
            return (def.Knockback, id == StreakId.MortarBarrage ? KillStreaks.MortarRadius : id == StreakId.StrafeRun ? KillStreaks.StrafeRadius : 0f);
        }
        return (0f, 0f);
    }
}

public sealed partial class Game
{
    /// <summary>Pushes the server has applied to living players (self-test, smoke log).</summary>
    public int KnocksApplied;

    /// <summary>The last push applied (self-test).</summary>
    public Vector3 LastKnock;

    /// <summary>Bytes after the message id in a <see cref="Msg.Knock"/>: the input sequence (4) and the impulse (12).</summary>
    public const int KnockBytes = 16;

    // Client: the pushes the server has sent that its acknowledged state doesn't include yet (sequence, impulse).
    private readonly List<(uint Seq, Vector3 Impulse)> _pendingKnocks = new();
    private MoveState _lastServerMove;
    private uint _lastAckSeq;
    private bool _haveServerMove;

    /// <summary>
    /// Server: a blast's push on every living player in reach and in the open. <paramref name="enemiesOnly"/>: a strike
    /// (pushes only the caller's enemies). Without an owner (a prop whose breaker left) everyone in reach is pushed.
    /// </summary>
    internal void BlastKnockback(Player? owner, Vector3 center, float radius, float strength, bool enemiesOnly = false)
    {
        if (!IsServer || !(strength > 0) || !(radius > 0))
            return;
        foreach (var victim in Players.Values)
        {
            if (!victim.Alive)
                continue;
            if (owner != null && (enemiesOnly ? !StreakIsEnemy(owner, victim) : !CanDamage(owner, victim)))
                continue; // teammates: no friendly fire, so no friendly shove either
            var chest = victim.Chest;
            var push = Knockback.Impulse(center, radius, strength, chest, victim.Sim.Stance == Stance.Prone, victim.Perks.Has(PerkId.FlakJacket));
            if (push == Vector3.Zero || !Knockback.InSight(World.Query, center, chest) || CoverBlocks(center, chest))
                continue;
            Knock(victim, push);
        }
    }

    /// <summary>Server: pushes one player (and tells their client exactly when, for its prediction).</summary>
    private void Knock(Player p, Vector3 impulse)
    {
        Knockback.Apply(ref p.Sim, impulse);
        KnocksApplied++;
        LastKnock = impulse;
        if (p.Bot != null || p.Id == LocalId)
            return;
        foreach (var (guid, id) in _clients.Values)
        {
            if (id != p.Id)
                continue;
            WriteKnock(_w, p.LastProcessedSeq, impulse);
            Net.SendTo(guid, _w.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>The knock message: the last input sequence the server had applied when the push went in, and the push.</summary>
    public static void WriteKnock(PacketWriter w, uint seq, Vector3 impulse) => w.Begin(Msg.Knock).U32(seq).Vec(impulse);

    /// <summary>Client: a push on the local player. Replays the prediction with it, unless the server's state already has it.</summary>
    private void OnKnock(uint seq, Vector3 impulse)
    {
        if (!float.IsFinite(impulse.X + impulse.Y + impulse.Z) || impulse.LengthSquared() > Knockback.MaxImpulse * Knockback.MaxImpulse * 2.25f)
            return; // garbage
        if (Local is not { Alive: true } me || !_haveServerMove || seq <= _lastAckSeq)
            return; // the acknowledged state already includes it
        if (_pendingKnocks.Count >= 32)
            _pendingKnocks.RemoveAt(0);
        _pendingKnocks.Add((seq, impulse));
        Reconcile(me, _lastAckSeq, _lastServerMove, true, false); // predict it now: the replay puts it in after input `seq`
    }

    /// <summary>Prediction replay: the pushes that went in right after input <paramref name="seq"/>.</summary>
    private void ApplyPendingKnocks(uint seq, ref MoveState state)
    {
        for (var i = 0; i < _pendingKnocks.Count; i++)
            if (_pendingKnocks[i].Seq == seq)
                Knockback.Apply(ref state, _pendingKnocks[i].Impulse);
    }

    /// <summary>Drops the pushes the acknowledged state includes.</summary>
    private void DropAckedKnocks(uint ackSeq)
    {
        for (var i = _pendingKnocks.Count - 1; i >= 0; i--)
            if (_pendingKnocks[i].Seq <= ackSeq)
                _pendingKnocks.RemoveAt(i);
    }

    /// <summary>Self-test: the client's knock handling (as if a <see cref="Msg.Knock"/> arrived), and how many pushes it holds.</summary>
    public void SelfTestKnock(uint seq, Vector3 impulse) => OnKnock(seq, impulse);

    public int PendingKnocks => _pendingKnocks.Count;

    /// <summary>Self-test: one predicted input on a client (what <see cref="ClientTickStep"/> does, without the network).</summary>
    public void SelfTestPredictInput(InputCmd cmd)
    {
        _pendingInputs.Add(cmd);
        if (Local is { Alive: true } me)
            World.Simulate(ref me.Sim, cmd);
    }

    /// <summary>Self-test: a snapshot's acknowledged state arriving on a client (the reconciliation it triggers).</summary>
    public void SelfTestReconcile(uint ackSeq, MoveState server) => Reconcile(Local!, ackSeq, server, true, false);

    /// <summary>Self-test: a blast's push from <paramref name="owner"/> at <paramref name="center"/>, as a grenade gives it.</summary>
    public void SelfTestBlastKnockback(Player? owner, Vector3 center, float radius, float strength, bool enemiesOnly = false) =>
        BlastKnockback(owner, center, radius, strength, enemiesOnly);
}
