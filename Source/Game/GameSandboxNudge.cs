using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The Nudge tool in the game (docs/sandbox/CONSTRUCTS.md, "The Nudge tool"; ConstructNudge.cs has the rules): the server's
/// handling of its requests, the client's requests, and the holder's drawn pose. Server-authoritative: the client asks for a
/// whole move and turn since the drag began, about 30 times a second and unreliably, and once for certain on letting go.
/// </summary>
public sealed partial class Game
{
    private readonly Construct?[] _ndBodies = new Construct?[Nudge.MaxBodies];
    private readonly Vector3[] _ndStartPos = new Vector3[Nudge.MaxBodies];
    private readonly Quaternion[] _ndStartRot = new Quaternion[Nudge.MaxBodies];
    private int _ndCount;
    private bool _ndDragging, _ndHave;
    private Vector3 _ndPivot, _ndMove, _ndWas, _ndSentMove;
    private Quaternion _ndTurn = Quaternion.Identity, _ndWasTurn = Quaternion.Identity, _ndSentTurn = Quaternion.Identity;
    private float _ndShare, _ndSpeed;
    private double _ndSentAt, _ndEndAt;

    /// <summary>The tool's keys (Program.SandboxNudge.cs).</summary>
    public string SandboxNudgeHint = "";

    /// <summary>The no-collide tool's keys (Program.SandboxNoCollide.cs).</summary>
    public string SandboxNoCollideHint = "";

    /// <summary>Client and host: why the Nudge tool can't take this construct, from what this machine knows (the server checks again).</summary>
    public string? SandboxNudgeRefusal(Construct c, byte player)
    {
        if (IsServer)
        {
            SandboxRules();
            return SandboxHands.NudgeRefusal(c, player);
        }
        foreach (var pl in Players.Values)
            if (pl.Id != player && SandboxHands.Hand(pl.Id).GrabBody == c.Id)
                return "someone else is holding it";
        if (SandboxAboardRefusal(c, player) is { } aboard)
            return aboard;
        if (!SandboxInfiniteParts)
        {
            if (c.Anchored && !c.Frozen)
                return "it is anchored to the ground";
            if (c.Mass > ConstructGrab.SurvivalMaxMass)
                return $"too heavy to nudge ({c.Mass:0} kg; {ConstructGrab.SurvivalMaxMass:0} kg at most in survival)";
            return ConstructEdits.SalvageRefusal(c, player) != null ? "that construct is someone else's" : ConstructGrab.Defended(c, player, SandboxAliveAt, SandboxNameOf);
        }
        return ConstructEdits.SalvageRefusal(c, player) != null ? "that construct is someone else's" : null;
    }

    /// <summary>Server: a Nudge tool request. Why it was refused, or null.</summary>
    private string? SandboxNudgeAct(Player p, CActKind kind, ref PacketReader r, Vector3 eye)
    {
        var hands = SandboxHands;
        switch (kind)
        {
            case CActKind.NudgeBegin:
            {
                int n = r.U8();
                if (n is 0 or > Nudge.MaxBodies)
                    return "nothing to nudge";
                Span<ushort> bodies = stackalloc ushort[n];
                for (var i = 0; i < n; i++)
                    bodies[i] = r.U16();
                return hands.NudgeBegin(Constructs, p.Id, eye, bodies, r.Vec());
            }
            case CActKind.NudgeSteer:
            {
                var move = r.Vec();
                var q = new Quaternion((short)r.U16() / 32767f, (short)r.U16() / 32767f, (short)r.U16() / 32767f, (short)r.U16() / 32767f);
                hands.NudgeSteer(p.Id, move, q);
                return null;
            }
            case CActKind.NudgeEnd:
            {
                var move = r.Vec();
                var q = new Quaternion(r.F32(), r.F32(), r.F32(), r.F32());
                hands.NudgeEnd(p.Id, move, q);
                return null;
            }
        }
        return null;
    }

    /// <summary>Client (or host): the drag starts on these bodies, about this point.</summary>
    public void SandboxNudgeBegin(ReadOnlySpan<Construct> bodies, Vector3 pivot)
    {
        _ndCount = 0;
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.NudgeBegin).U8((byte)bodies.Length);
        foreach (var c in bodies)
        {
            w.U16(c.Id);
            _ndBodies[_ndCount] = c;
            _ndStartPos[_ndCount] = c.Pos;
            _ndStartRot[_ndCount++] = c.Rot;
        }
        w.Vec(pivot);
        _ndPivot = pivot;
        _ndMove = _ndWas = _ndSentMove = Vector3.Zero;
        _ndTurn = _ndWasTurn = _ndSentTurn = Quaternion.Identity;
        _ndDragging = true;
        _ndHave = false;
        _ndShare = 1;
        _ndSpeed = 0;
        NudgeSend(w, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client (or host), every frame of a drag: what it asks for now (sent on a change, at most ~30 times a second).</summary>
    public void SandboxNudgeSteer(Vector3 move, Quaternion turn)
    {
        var now = SandboxNow;
        _ndMove = move;
        _ndTurn = turn;
        var same = Vector3.DistanceSquared(move, _ndSentMove) < 1e-8f && MathF.Abs(Quaternion.Dot(turn, _ndSentTurn)) > 0.9999999f;
        var since = now - _ndSentAt;
        if (IsServer)
        {
            SandboxHands.NudgeSteer(LocalId, move, turn);
            return;
        }
        if (since < 1.0 / 30 || same && since < 0.25)
            return;
        _ndSentMove = move;
        _ndSentTurn = turn;
        _ndSentAt = now;
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.NudgeSteer).Vec(move).U16(Unit16(turn.X)).U16(Unit16(turn.Y)).U16(Unit16(turn.Z)).U16(Unit16(turn.W));
        Net.SendToServer(w.Span, PacketReliability.UnreliableSequenced);
    }

    /// <summary>Client (or host): the drag is let go at this move and turn (cancelled: none at all).</summary>
    public void SandboxNudgeEnd(Vector3 move, Quaternion turn)
    {
        _ndMove = move;
        _ndTurn = turn;
        _ndDragging = false;
        _ndEndAt = SandboxNow + 1.0;
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.NudgeEnd).Vec(move).F32(turn.X).F32(turn.Y).F32(turn.Z).F32(turn.W);
        NudgeSend(w, PacketReliability.ReliableOrdered);
    }

    private void NudgeSend(PacketWriter w, PacketReliability how)
    {
        if (!IsServer)
        {
            Net.SendToServer(w.Span, how);
            return;
        }
        if (Local is { } me)
        {
            var r = new PacketReader(w.Span);
            r.U8();
            if (SandboxActGuarded(me, ref r) is { } why)
                SandboxSay(why);
        }
    }

    /// <summary>
    /// Client, every frame after the poses are blended: the bodies are drawn where the drag asks, not a round trip behind, but
    /// no further from the server's pose than the physics gun's lead allows (<see cref="ConstructGrab.LeadMin"/> plus the speed
    /// of the motion in hand over <see cref="ConstructGrab.LeadSeconds"/>): something in the way shows once the hand rests. After
    /// letting go it stays until the server's pose has come, so the piece never steps back to a late one.
    /// </summary>
    private void SandboxNudgePredict(float dt)
    {
        if (IsServer || _ndCount == 0)
            return;
        var moved = 0f;
        var turned = 0f;
        Construct? first = null;
        Vector3 want0 = default;
        Quaternion wantRot0 = default;
        for (var i = 0; i < _ndCount && first == null; i++)
            if (_ndBodies[i] is { } c && ReferenceEquals(Constructs.Get(c.Id), c))
            {
                first = c;
                Nudge.Pose(_ndStartPos[i], _ndStartRot[i], _ndPivot, _ndMove, _ndTurn, out want0, out wantRot0);
                moved = Vector3.Distance(c.Pos, want0);
                turned = Nudge.Angle(c.Rot, wantRot0);
            }
        if (first == null || !_ndDragging && (moved < 0.005f && turned < 0.002f || SandboxNow >= _ndEndAt))
        {
            SandboxNudgeForget();
            return;
        }
        // The hand's speed, held up for a moment when it drops, as the physics gun's is.
        var keep = _ndHave ? MathF.Exp(-dt / 0.3f) : 0f;
        var speed = _ndHave && dt > 1e-4f ? MathF.Min((Vector3.Distance(_ndMove, _ndWas) + Nudge.Angle(_ndTurn, _ndWasTurn) * MathF.Max(1f, first.Radius)) / dt, 60f) : 0f;
        _ndSpeed = MathF.Max(speed, _ndSpeed * keep);
        _ndWas = _ndMove;
        _ndWasTurn = _ndTurn;
        var lead = ConstructGrab.LeadSeconds + (float)((InterpDelayTicks + ConstructNet.BodyExtraDelayTicks) * World.TickDt);
        var allowed = MathF.Max(ConstructGrab.LeadMin * Math.Clamp(_ndSpeed / ConstructGrab.RestSpeed, 0, 1), ConstructGrab.RestLead) + _ndSpeed * lead;
        var gap = moved + turned * MathF.Max(1f, first.Radius);
        var share = gap > allowed ? allowed / gap : 1f;
        var a = _ndHave ? 1f - MathF.Exp(-dt / 0.1f) : 1f;
        _ndShare += (share - _ndShare) * a;
        _ndHave = true;
        for (var i = 0; i < _ndCount; i++)
        {
            if (_ndBodies[i] is not { } c || !ReferenceEquals(Constructs.Get(c.Id), c))
                continue;
            Nudge.Pose(_ndStartPos[i], _ndStartRot[i], _ndPivot, _ndMove, _ndTurn, out var at, out var rot);
            c.Pos = Vector3.Lerp(c.Pos, at, _ndShare);
            c.Rot = Quaternion.Normalize(Quaternion.Slerp(c.Rot, rot, _ndShare));
        }
    }

    /// <summary>Client: nothing more to draw for the nudge (it landed, or the bodies went).</summary>
    public void SandboxNudgeForget()
    {
        _ndCount = 0;
        Array.Clear(_ndBodies);
    }
}
