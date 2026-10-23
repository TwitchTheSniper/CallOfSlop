using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Blast knockback: every explosion (frag and impact grenades, exploding props, mortar and strafe-run strikes) pushes
/// what's around it away from the blast. The strength is data: <see cref="ThrowableDef.Knockback"/>,
/// <see cref="StreakDef.Knockback"/>, a launcher's "knockback" and a prop's "explode" "knockback" (m/s added at the centre),
/// falling off gently (square root) to <see cref="EdgeShare"/> at the blast radius, so the survivors (who are near the edge)
/// still get a real shove, always with lift so it leaves the ground. A push staggers (<see cref="StaggerTicks"/> of weak
/// steering, <see cref="MoveState.KnockTicks"/>), so the ground's grip doesn't cancel it in a few ticks; the local player
/// also gets camera shake, a view kick and a stumble (render only). Walls (and an enemy's Deployable Cover) shield you
/// exactly as they shield you from the damage.
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

    /// <summary>Upward push (m/s) per m/s of push strength, on top of <see cref="LiftBase"/>: every push lifts you off the ground.</summary>
    public const float Lift = 0.4f;

    /// <summary>Upward push (m/s) every blast in reach gives on top of <see cref="Lift"/> (before prone / Flak Jacket), so even a near miss hops you.</summary>
    public const float LiftBase = 1.6f;

    /// <summary>
    /// Share of the centre strength a body right at the edge of the radius still gets. The push falls off as the square root
    /// of the distance left (1 - distance / radius), not in the straight line the damage uses: whoever survives a blast is
    /// near its edge, and a straight line left them almost nothing (about 3 m/s from a frag at 6 m), which the ground's grip
    /// then cancelled in a few ticks.
    /// </summary>
    public const float EdgeShare = 0.15f;

    /// <summary>Prone players are pushed this much.</summary>
    public const float ProneMult = 0.5f;

    /// <summary>Caps on the velocity after a push: horizontal and upward (m/s).</summary>
    public const float MaxHorizontal = 15f, MaxUp = 9f;

    /// <summary>An exploding prop without its own "knockback" pushes this hard (m/s at the centre).</summary>
    public const float DefaultProp = 9f;

    /// <summary>
    /// Ticks a push staggers you for (<see cref="MoveState.KnockTicks"/>): your steering is weak (<see cref="StaggerAirControl"/>
    /// in the air, <see cref="StaggerGroundControl"/> on the ground), so the push carries you instead of being steered out in
    /// a few ticks. Fixed, so a client can work out its own stagger from when the push went in (<see cref="StaggerAt"/>).
    /// </summary>
    public const byte StaggerTicks = 27;

    /// <summary>A push at least this strong (m/s) staggers.</summary>
    public const float StaggerMin = 1.5f;

    /// <summary>Steering while staggered (per second; normal: 14 on the ground, 3 in the air).</summary>
    public const float StaggerGroundControl = 3.5f, StaggerAirControl = 0.8f;

    /// <summary>The push (m/s) a blast at <paramref name="center"/> gives a body whose chest is at <paramref name="target"/> (zero beyond the radius). Line of sight is the caller's.</summary>
    public static Vector3 Impulse(Vector3 center, float radius, float strength, Vector3 target, bool prone, bool flak)
    {
        if (!(strength > 0) || !(radius > 0))
            return Vector3.Zero;
        var to = target - center;
        var dist = to.Length();
        if (!(dist < radius))
            return Vector3.Zero;
        var mag = strength * (EdgeShare + (1f - EdgeShare) * MathF.Sqrt(1f - dist / radius));
        // Away from the blast across the ground (a blast straight below or above: no sideways push), and always up.
        var flat = new Vector3(to.X, 0, to.Z);
        var flatLen = flat.Length();
        var up = dist > 0.05f ? to.Y / dist : 1f;
        var push = flatLen > 0.05f ? flat / flatLen * (mag * flatLen / MathF.Max(dist, 0.05f)) : Vector3.Zero;
        push.Y = LiftBase + mag * MathF.Max(Lift, up);
        var scale = (prone ? ProneMult : 1f) * (flak ? Perks.FlakKnockMult : 1f);
        push *= scale;
        var len = push.Length();
        return len > MaxImpulse ? push * (MaxImpulse / len) : push;
    }

    /// <summary>Does a push of <paramref name="impulse"/> stagger (<see cref="StaggerTicks"/>)? The server and the client's replay agree on it.</summary>
    public static bool Staggers(Vector3 impulse) => impulse.LengthSquared() >= StaggerMin * StaggerMin;

    /// <summary>The stagger left after input <paramref name="ackSeq"/> from a push that went in right after input <paramref name="knockSeq"/> (0 = none).</summary>
    public static byte StaggerAt(uint knockSeq, uint ackSeq) =>
        knockSeq > ackSeq || ackSeq - knockSeq >= StaggerTicks ? (byte)0 : (byte)(StaggerTicks - (ackSeq - knockSeq));

    /// <summary>Is <paramref name="target"/> in the open from <paramref name="center"/> (no wall, roof or floor in between)?</summary>
    public static bool InSight(IWorldQuery world, Vector3 center, Vector3 target)
    {
        var to = target - center;
        var dist = to.Length();
        return dist <= 0.3f || world.Raycast(center, to / dist, dist, QueryFilter.All) >= dist - 0.05f;
    }

    /// <summary>
    /// Adds a push to a movement state: the velocity changes (capped), a push with any real lift leaves the ground, and a
    /// real push staggers (<see cref="StaggerTicks"/> of weak steering).
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
        if (impulse.Y > 0.5f)
            s.Grounded = false; // off the ground: the movers would drop a grounded player's upward speed
        if (Staggers(impulse))
            s.KnockTicks = StaggerTicks;
    }

    /// <summary>How hard (m/s at the centre) and how far a kill id's blast pushes: frag and impact grenades and the strikes (0 for everything else).</summary>
    public static (float Strength, float Radius) ForKill(byte killId)
    {
        if (killId == Game.LandmineKillId)
            return (Game.LandmineRagdollPush, Game.LandmineRagdollRadius); // mines and their chains (GameMines.cs)
        if (killId >= Throwables.KillIdBase && killId != Game.SuicideKillId)
        {
            var def = Throwables.Get((ThrowableKind)(killId - Throwables.KillIdBase));
            return (def.Knockback, def.Radius);
        }
        if (KillStreaks.IsStreakKill(killId))
        {
            var id = KillStreaks.FromKillId(killId);
            var def = KillStreaks.Get(id);
            return (def.Knockback, KillStreaks.BlastRadiusOf(id));
        }
        if (killId < Weapons.All.Length && Weapons.Get(killId) is { IsLauncher: true } launcher)
            return (LauncherStrength(launcher), launcher.PlayerBlastRadius); // launcher blasts (the player radius) (Source/Game/Launchers)
        return (0f, 0f);
    }

    /// <summary>A launcher blast's push (m/s at the centre): its "knockback" key, or (0) bigger warheads shove harder; capped like everything else.</summary>
    public static float LauncherStrength(WeaponDef d) => Math.Min(MaxImpulse, d.LauncherKnockback > 0 ? d.LauncherKnockback : 5f + d.Damage / 25f);
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
        if (p.Sim.MantleTicks > 0)
            return; // mantling ignores pushes (and its client must not hear of one, or it would stagger its prediction)
        Knockback.Apply(ref p.Sim, impulse);
        KnocksApplied++;
        LastKnock = impulse;
        if (p.Id == LocalId)
            KnockFeedback(impulse); // the host's own player
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
        if (Local is not { Alive: true } me || !_haveServerMove)
            return;
        KnockFeedback(impulse);
        if (Knockback.Staggers(impulse) && !HeardKnock(seq))
        {
            _knockSeqs[_knockSeqNext] = seq; // for the stagger in later acknowledged states (snapshots don't carry it)
            _knockSeqNext = (_knockSeqNext + 1) % _knockSeqs.Length;
            _knockSeqCount = Math.Min(_knockSeqCount + 1, _knockSeqs.Length);
        }
        if (seq <= _lastAckSeq)
        {
            // The acknowledged state already includes the push (the snapshot beat the message here), but not its stagger.
            if (Knockback.StaggerAt(seq, _lastAckSeq) > 0)
                Reconcile(me, _lastAckSeq, _lastServerMove, true, false);
            return;
        }
        if (_pendingKnocks.Count >= 32)
            _pendingKnocks.RemoveAt(0);
        _pendingKnocks.Add((seq, impulse));
        Reconcile(me, _lastAckSeq, _lastServerMove, true, false); // predict it now: the replay puts it in after input `seq`
    }

    // Client: the input sequences the recent staggering pushes went in after (a small ring; never allocates).
    private readonly uint[] _knockSeqs = new uint[8];
    private int _knockSeqNext, _knockSeqCount;

    /// <summary>
    /// Client: the stagger the server's state has after input <paramref name="ackSeq"/>. Snapshots don't carry
    /// <see cref="MoveState.KnockTicks"/>; the server counts it down once per input from the push, so it follows from when
    /// the push went in.
    /// </summary>
    private byte StaggerAt(uint ackSeq)
    {
        byte best = 0;
        for (var i = 0; i < _knockSeqCount; i++)
            best = Math.Max(best, Knockback.StaggerAt(_knockSeqs[i], ackSeq));
        return best;
    }

    private bool HeardKnock(uint seq)
    {
        for (var i = 0; i < _knockSeqCount; i++)
            if (_knockSeqs[i] == seq)
                return true;
        return false;
    }

    /// <summary>Client: forget the pushes (dead or respawned: the server's new state has no stagger).</summary>
    private void ForgetKnocks()
    {
        _knockSeqCount = _knockSeqNext = 0;
        _pendingKnocks.Clear();
    }

    /// <summary>Reconcile: the acknowledged state with the stagger the server has in it.</summary>
    private MoveState WithStagger(MoveState server, uint ackSeq, bool alive, bool respawned)
    {
        if (!alive || respawned)
            ForgetKnocks();
        server.KnockTicks = StaggerAt(ackSeq);
        return server;
    }

    // ---- Feedback on the local player: camera shake, a view kick away from the blast and a stumble (render only) ----

    private float _jolt, _joltPitch, _joltRoll, _joltYaw, _joltDrop;

    /// <summary>The blast push's view kick (render only; it never moves the aim). Add it to the recoil camera.</summary>
    public CameraKick KnockCamera => new() { Pitch = _joltPitch * _jolt, Yaw = _joltYaw * _jolt, Roll = _joltRoll * _jolt };

    /// <summary>Metres the rendered eye dips while stumbling from a push (rises and settles over about half a second).</summary>
    public float KnockDrop => _joltDrop * 4f * _jolt * (1f - _jolt);

    /// <summary>The last push's feedback strength (0-1, decaying; self-test).</summary>
    public float KnockJolt => _jolt;

    /// <summary>A push on the local player: shake, a kick of the view away from the blast and a stumble, scaled by its strength.</summary>
    private void KnockFeedback(Vector3 impulse)
    {
        var len = impulse.Length();
        if (!(len > 0.3f))
            return;
        var k = Math.Clamp(len / Knockback.MaxImpulse, 0.25f, 1f);
        var flat = new Vector3(impulse.X, 0, impulse.Z) / len;
        var forward = Vector3.Dot(flat, World.FlatForward(LookYaw)); // > 0: pushed the way you look (the blast was behind)
        var right = Vector3.Dot(flat, World.FlatRight(LookYaw));
        _jolt = 1f;
        _joltPitch = (0.03f - 0.07f * forward) * k; // the head snaps back from a blast in front, forward from one behind
        _joltRoll = 0.09f * right * k;
        _joltYaw = 0.03f * right * k;
        _joltDrop = 0.12f * k;
        Shake = MathF.Max(Shake, 0.45f + 0.55f * k);
    }

    /// <summary>Per frame: the feedback settles.</summary>
    private void UpdateKnockFeedback(float dt) => _jolt *= MathF.Exp(-5f * dt);

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

    /// <summary>Self-test: an explosive prop going off at <paramref name="center"/> (a mine with no attacker is neutral), as when it breaks.</summary>
    public void SelfTestPropExplode(Vector3 center, PropBreakable br, Player? attacker, double now) => PropExplode(center, br, attacker, false, now);

    /// <summary>Self-test: a blast's push from <paramref name="owner"/> at <paramref name="center"/>, as a grenade gives it.</summary>
    public void SelfTestBlastKnockback(Player? owner, Vector3 center, float radius, float strength, bool enemiesOnly = false) =>
        BlastKnockback(owner, center, radius, strength, enemiesOnly);
}
