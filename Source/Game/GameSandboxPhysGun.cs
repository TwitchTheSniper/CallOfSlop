using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The physics gun in the game (docs/sandbox/CONSTRUCTS.md, "The physics gun"; ConstructGrab.cs has the rules): the
/// server's handling of its requests, the client's requests (the steer unreliably, like a seat's look), the contested
/// rule's player lookups, and its HUD line. Server-authoritative: the client only asks; one holder per construct.
/// </summary>
public sealed partial class Game
{
    private Func<byte, Vector3?>? _sbAliveAt;
    private Func<byte, string>? _sbNameOf;

    /// <summary>Where a living player stands (null: dead or gone), for the contested rule; made once.</summary>
    public Func<byte, Vector3?> SandboxAliveAt => _sbAliveAt ??= id => Players.TryGetValue(id, out var pl) && pl.Alive ? pl.Position : null;

    /// <summary>A player's name ("player 3" when they are gone), for "Owned by Name - defended"; made once.</summary>
    public Func<byte, string> SandboxNameOf => _sbNameOf ??= id => Players.TryGetValue(id, out var pl) && pl.Name.Length > 0 ? pl.Name : $"player {id}";

    /// <summary>Server, with the other rules: the contested rule's lookups.</summary>
    private void SandboxPhysGunRules()
    {
        SandboxHands.AliveAt = SandboxAliveAt;
        SandboxHands.NameOf = SandboxNameOf;
    }

    /// <summary>Server: a physics gun request. Returns why it was refused (or, for unfreeze-all, what it did), or null.</summary>
    private string? SandboxPhysGunAct(Player p, CActKind kind, ref PacketReader r)
    {
        var hands = SandboxHands;
        switch (kind)
        {
            case CActKind.Grab:
            {
                var body = r.U16();
                var local = new Vector3((short)r.U16(), (short)r.U16(), (short)r.U16()) / 100f;
                int part = r.U16();
                var leaf = r.U8() != 0;
                return hands.Grab(Constructs, p.Id, p.Eye, p.Yaw, body, local, part == ushort.MaxValue ? -1 : part, leaf);
            }
            case CActKind.GrabSteer:
            {
                var dist = r.U16() / 100f;
                var q = new Quaternion((short)r.U16() / 32767f, (short)r.U16() / 32767f, (short)r.U16() / 32767f, (short)r.U16() / 32767f);
                hands.SteerGrab(p.Id, dist, q);
                return null;
            }
            case CActKind.GrabRelease:
                hands.Release(Constructs, p.Id);
                return null;
            case CActKind.GrabFreeze:
                return hands.Freeze(Constructs, p.Id);
            case CActKind.FreezePiece:
            {
                var body = r.U16();
                var seg = r.U8();
                return hands.FreezePiece(Constructs, p.Id, p.Eye, body, seg);
            }
            case CActKind.UnfreezeAll:
            {
                var everyone = r.U8() != 0 && p.Id == LocalId; // (the host's option: everything, anyone's)
                var n = hands.UnfreezeAll(Constructs, p.Id, everyone);
                return n == 0 ? everyone ? "nothing is frozen" : "nothing you froze is frozen" : $"Unfroze {n} object{(n == 1 ? "" : "s")}{(everyone ? " (everyone's)" : "")}";
            }
        }
        return null;
    }

    /// <summary>Client (or host): take hold of a construct at a point on it (its body frame).</summary>
    public void SandboxGrab(ushort body, Vector3 local, int part = -1, bool leaf = false, byte seg = 0)
    {
        ref var h = ref SandboxHands.Hand(LocalId);
        if (!IsServer)
        {
            h.GrabBody = body; // (shown at once; the server answers with its own in the tail, or a refusal. The host's hands are the server's)
            h.GrabLocal = local;
            h.GrabSeg = seg;
        }
        h.GrabAskedAt = SandboxNow;
        _sbPredOn = false;
        _sbSteerSent = float.NaN;
        PhysGunSend(CActKind.Grab, body, local, 0, part, leaf);
    }

    /// <summary>Client (or host): let go, or freeze what is held and let go.</summary>
    public void SandboxLetGo(bool freeze)
    {
        ref var h = ref SandboxHands.Hand(LocalId);
        if (!IsServer)
        {
            h.GrabBody = 0; // (shown at once; the host's own hands are the server's, which lets go itself)
            h.GrabSeg = 0;
        }
        _sbPredOn = false;
        h.GrabAskedAt = SandboxNow;
        PhysGunSend(freeze ? CActKind.GrabFreeze : CActKind.GrabRelease, 0, default, 0);
    }

    /// <summary>Client (or host): freeze or unfreeze one piece of a mechanism (segment <paramref name="seg"/>; 0: the base).</summary>
    public void SandboxFreezePiece(ushort body, int seg) => PhysGunSend(CActKind.FreezePiece, body, default, (byte)seg);

    /// <summary>Client (or host): unfreeze everything you froze (the host may ask for everything).</summary>
    public void SandboxUnfreezeAll(bool everyone) => PhysGunSend(CActKind.UnfreezeAll, 0, default, (byte)(everyone ? 1 : 0));

    private void PhysGunSend(CActKind kind, ushort body, Vector3 local, byte flag, int part = -1, bool leaf = false)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)kind);
        if (kind == CActKind.Grab)
            w.U16(body).U16(Cm16(local.X)).U16(Cm16(local.Y)).U16(Cm16(local.Z)).U16((ushort)(part < 0 ? ushort.MaxValue : part)).U8((byte)(leaf ? 1 : 0));
        else if (kind is CActKind.UnfreezeAll)
            w.U8(flag);
        else if (kind == CActKind.FreezePiece)
            w.U16(body).U8(flag);
        if (IsServer)
        {
            if (Local is { } me)
            {
                var r = new PacketReader(w.Span[1..]);
                if (SandboxActGuarded(me, ref r) is { } why)
                    SandboxSay(why);
            }
            return;
        }
        Net.SendToServer(w.Span, PacketReliability.ReliableOrdered);
    }

    private static ushort Cm16(float v) => unchecked((ushort)(short)Math.Clamp(MathF.Round(v * 100), short.MinValue, short.MaxValue));

    private static ushort Unit16(float v) => unchecked((ushort)(short)Math.Clamp((int)MathF.Round(v * 32767f), -32767, 32767));

    private float _sbPredDist;
    private Quaternion _sbPredRel = Quaternion.Identity;
    private bool _sbPredOn, _sbPredHave;
    private Vector3 _sbPredTarget;
    private Quaternion _sbPredTurn = Quaternion.Identity;

    /// <summary>
    /// Client, every frame after the poses are blended (pr01): what this player holds is drawn at the pose their own aim asks
    /// for now, not where the server's snapshots (a round trip behind) put it; <see cref="ConstructGrab.Predict"/> keeps it
    /// within a short lead of the server's pose. The server stays authoritative: the next blend starts from its pose again.
    /// </summary>
    public void SandboxPredictHeld(float dt)
    {
        SandboxNudgePredict(dt);
        ushort body = 0;
        Construct? c = null;
        Player? me = null;
        if (!IsServer && _sbPredOn && Local is { Alive: true } lp)
        {
            ref var h = ref SandboxHands.Hand(LocalId);
            if (h is { Tool: ConstructTool.PhysicsGun, GrabSeg: not 0 } && Constructs.Get(h.GrabBody) is { } carried && h.GrabSeg <= carried.JointCount)
            {
                if (!ConstructGrab.InChain(carried, h.GrabSeg))
                    PredictJoint(carried, h.GrabSeg, h.GrabLocal, lp, dt); // (in a chain only the server knows how the pull shares out: it draws the server's, smoothly)
                _sbPredHave = false;
                return;
            }
            _sbJointHave = false;
            if (h.Tool == ConstructTool.PhysicsGun && h.GrabBody != 0 && Constructs.Get(h.GrabBody) is { } held)
            {
                held.GrabLocal = h.GrabLocal; // (cj01: the client's copy never had it, so pr01 put the body's origin on the aim, not the point held)
                body = h.GrabBody;
                c = held;
                me = lp;
            }
        }
        // cj01: let go (or the hold ended): the piece doesn't jump back to the server's pose, a round trip and the bodies'
        // delay behind; the gap closes over a fraction of a second.
        if (_sbPredHave && _sbPredBody != body)
        {
            if (Constructs.Get(_sbPredBody) is { } was)
            {
                _sbFadeBody = _sbPredBody;
                _sbFadePos = _sbPredDrawnPos - was.Pos;
                _sbFadeRot = Quaternion.Normalize(_sbPredDrawnRot * Quaternion.Conjugate(was.Rot));
            }
            _sbPredHave = false;
        }
        if (_sbFadeBody != 0 && _sbFadeBody != body)
        {
            if (Constructs.Get(_sbFadeBody) is { } f && _sbFadePos.LengthSquared() + (1 - MathF.Abs(_sbFadeRot.W)) > 1e-6f)
            {
                var k = MathF.Exp(-dt / FadeSeconds);
                _sbFadePos *= k;
                _sbFadeRot = Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, _sbFadeRot, k));
                f.Pos += _sbFadePos;
                f.Rot = Quaternion.Normalize(_sbFadeRot * f.Rot);
            }
            else
                _sbFadeBody = 0;
        }
        if (c == null || me == null)
        {
            _sbPredHave = false;
            return;
        }
        if (_sbFadeBody == body)
            _sbFadeBody = 0;
        var look = LookDir(LookYaw, LookPitch);
        var eye = me.RenderEye;
        var target = eye + look * _sbPredDist;
        ref var hand = ref SandboxHands.Hand(LocalId);
        var lookQ = ConstructGrab.LookQ(hand, LookYaw);
        var turn = Quaternion.Normalize(lookQ * _sbPredRel);
        float speed = 0, spin = 0;
        if (_sbPredHave && dt > 1e-4f)
        {
            speed = MathF.Min(Vector3.Distance(target, _sbPredTarget) / dt, 60f);
            spin = MathF.Min(2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(turn, _sbPredTurn)), 0f, 1f)) / dt, 30f);
        }
        // The aim's speed, held up for a moment when it drops (one slow frame mustn't shrink the lead the piece is allowed).
        var keep = _sbPredHave ? MathF.Exp(-dt / 0.3f) : 0f;
        _sbPredSpeed = MathF.Max(speed, _sbPredSpeed * keep);
        _sbPredSpin = MathF.Max(spin, _sbPredSpin * keep);
        _sbPredTarget = target;
        _sbPredTurn = turn;
        // The aim's own pose, and where Predict lets it be drawn against the server's (c.Pos/Rot: the blended pose; the
        // lead allowed covers the round trip and the bodies' delay behind the server).
        var aimPos = target - Vector3.Transform(c.GrabLocal, turn);
        var lead = ConstructGrab.LeadSeconds + (float)((InterpDelayTicks + ConstructNet.BodyExtraDelayTicks) * World.TickDt);
        // The body's own speed and spin, held up like the aim's: its pose velocity jumps from packet to packet (a buggy's
        // wheels on the ground), and the lead allowed, and so the drawn piece wherever the server's pose was held back,
        // jumped with it.
        _sbPredBodySpeed = MathF.Max(c.Vel.Length(), _sbPredBodySpeed * keep);
        _sbPredBodySpin = MathF.Max(c.AngVel.Length(), _sbPredBodySpin * keep);
        ConstructGrab.Predict(c, eye, look, lookQ, _sbPredDist, _sbPredRel, _sbPredSpeed, _sbPredSpin, lead, _sbPredBodySpeed, _sbPredBodySpin);
        // cj01: drawn at the aim's pose plus a correction that follows the server smoothly (pr01 drew Predict's pose straight,
        // so every pose packet, a round trip behind, pulled it back and the aim pushed it on: it jiggled on clients).
        var errPos = c.Pos - aimPos;
        var errRot = Quaternion.Normalize(c.Rot * Quaternion.Conjugate(turn));
        if (errRot.W < 0)
            errRot = -errRot;
        if (!_sbPredHave)
        {
            _sbPredErrPos = errPos;
            _sbPredErrRot = errRot;
        }
        else
        {
            var a = 1f - MathF.Exp(-dt / CorrectSeconds);
            _sbPredErrPos += (errPos - _sbPredErrPos) * a;
            _sbPredErrRot = Quaternion.Normalize(Quaternion.Slerp(_sbPredErrRot, errRot, a));
        }
        c.Rot = Quaternion.Normalize(_sbPredErrRot * turn);
        c.Pos = aimPos + _sbPredErrPos;
        _sbPredHave = true;
        _sbPredBody = body;
        _sbPredDrawnPos = c.Pos;
        _sbPredDrawnRot = c.Rot;
    }

    private bool _sbJointHave;
    private float _sbJointErr, _sbJointWas, _sbJointSpeed;

    /// <summary>
    /// Client, every frame: a free joint this player holds is drawn at the angle their aim asks for now, plus a correction that
    /// follows the server's smoothly, as <see cref="SandboxPredictHeld"/> draws a held body. Its lead on the server is bounded
    /// the same way, so a stop or a wall the server held it at shows once the aim rests.
    /// </summary>
    private void PredictJoint(Construct c, int seg, Vector3 local, Player me, float dt)
    {
        ref var jt = ref c.Joints[seg - 1];
        var server = jt.Angle;
        var target = ConstructGrab.JointTarget(c, seg, local, me.RenderEye + LookDir(LookYaw, LookPitch) * _sbPredDist, out _);
        var wraps = !float.IsFinite(jt.Min);
        var speed = _sbJointHave && dt > 1e-4f ? MathF.Abs(wraps ? World.WrapAngle(target - _sbJointWas) : target - _sbJointWas) / dt : 0;
        _sbJointSpeed = MathF.Max(speed, _sbJointHave ? _sbJointSpeed * MathF.Exp(-dt / 0.3f) : 0);
        _sbJointWas = target;
        var lead = ConstructGrab.LeadSeconds + (float)((InterpDelayTicks + ConstructNet.BodyExtraDelayTicks) * World.TickDt);
        var allowed = MathF.Max(ConstructGrab.LeadMin * Math.Clamp(_sbJointSpeed / ConstructGrab.RestSpeed, 0, 1), ConstructGrab.RestLead) + _sbJointSpeed * lead;
        var ahead = wraps ? World.WrapAngle(target - server) : target - server;
        var at = server + Math.Clamp(ahead, -allowed, allowed);
        var err = wraps ? World.WrapAngle(at - target) : at - target;
        _sbJointErr = _sbJointHave ? _sbJointErr + (err - _sbJointErr) * (1f - MathF.Exp(-dt / CorrectSeconds)) : err;
        _sbJointHave = true;
        jt.Angle = target + _sbJointErr;
        if (!wraps)
            jt.Angle = Math.Clamp(jt.Angle, jt.Min, jt.Max);
    }

    /// <summary>cj01: how fast the holder's drawn piece takes up a change in the server's correction, and closes the gap once let go (s).</summary>
    private const float CorrectSeconds = 0.1f, FadeSeconds = 0.12f;

    private float _sbPredSpeed, _sbPredSpin, _sbPredBodySpeed, _sbPredBodySpin;
    private Vector3 _sbPredErrPos, _sbPredDrawnPos, _sbFadePos;
    private Quaternion _sbPredErrRot = Quaternion.Identity, _sbPredDrawnRot = Quaternion.Identity, _sbFadeRot = Quaternion.Identity;
    private ushort _sbPredBody, _sbFadeBody;

    private float _sbSteerSent = float.NaN;
    private Quaternion _sbSteerRelSent;
    private double _sbSteerAt;

    /// <summary>
    /// Client (or host), every frame while holding: the held thing's distance and turn to the server, on a change at most
    /// ~30 times a second and every 0.25 s anyway (unreliable, like a seat's look). The host's go straight into its hands.
    /// </summary>
    public void SandboxSteer(float dist, Quaternion rel, double now)
    {
        _sbPredDist = dist;
        _sbPredRel = rel;
        _sbPredOn = true;
        if (IsServer)
        {
            SandboxHands.SteerGrab(LocalId, dist, rel);
            return;
        }
        var since = now - _sbSteerAt;
        var same = MathF.Abs(dist - _sbSteerSent) < 0.01f && MathF.Abs(Quaternion.Dot(rel, _sbSteerRelSent)) > 0.99999f;
        if (since < 1.0 / 30 || (same && since < 0.25))
            return;
        _sbSteerSent = dist;
        _sbSteerRelSent = rel;
        _sbSteerAt = now;
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.GrabSteer).U16((ushort)Math.Clamp(MathF.Round(dist * 100), 0, 65535))
            .U16(Unit16(rel.X)).U16(Unit16(rel.Y)).U16(Unit16(rel.Z)).U16(Unit16(rel.W));
        Net.SendToServer(w.Span, PacketReliability.UnreliableSequenced);
    }

    /// <summary>
    /// Client and host: why the physics gun can't take hold of this construct, from what this machine knows (the server
    /// checks again): the same refusal the server would give, the contested rule included (survival only).
    /// </summary>
    public string? SandboxGrabRefusal(Construct c, byte player)
    {
        if (IsServer)
        {
            SandboxRules();
            return SandboxHands.GrabRefusal(c, player);
        }
        var creative = SandboxInfiniteParts;
        if (c.GrabbedBy != 255 && c.GrabbedBy != player)
            return "someone else is holding it";
        // (a client knows who holds what from the hands in the tail, not from the construct)
        foreach (var pl in Players.Values)
            if (pl.Id != player && SandboxHands.Hand(pl.Id).GrabBody == c.Id)
                return "someone else is holding it";
        if (c.Driver != 255)
            return "someone is in it";
        if (c.Anchored && !c.Frozen)
            return "it is anchored to the ground";
        var cap = creative ? ConstructGrab.MaxMass : ConstructGrab.SurvivalMaxMass;
        if (c.Mass > cap)
            return $"too heavy for the physics gun ({c.Mass:0} kg; {cap:0} kg at most{(creative ? "" : " in survival")})";
        return creative ? null : ConstructGrab.Defended(c, player, SandboxAliveAt, SandboxNameOf);
    }

    /// <summary>Client and host: why the beam can't move this carriage or leaf on its own, from what this machine knows (the server checks again).</summary>
    public string? SandboxLeafRefusal(Construct c, int part, bool leaf, byte player)
    {
        var creative = IsServer ? SandboxHands.InfiniteParts : SandboxInfiniteParts;
        if (ConstructGrab.LeafRefusal(c, part, leaf, creative) is { } why)
            return why;
        var seg = ConstructGrab.LeafJoint(c, part, leaf) + 1;
        foreach (var pl in Players.Values)
            if (pl.Id != player && SandboxHands.Hand(pl.Id).GrabBody == c.Id && (SandboxHands.Hand(pl.Id).GrabSeg == seg || SandboxHands.Hand(pl.Id).GrabSeg == 0))
                return "someone else is holding it";
        return creative ? null : ConstructGrab.Defended(c, player, SandboxAliveAt, SandboxNameOf);
    }

    private SbText _sbPhysHint;

    /// <summary>pw01: what the snap uses while Shift snaps a held piece ("snap: world", "snap: construct 4"); null when not snapping.</summary>
    public string? SandboxSnapLabel;

    /// <summary>The physics gun's key line (made again only when the binds' names or the holding change).</summary>

    public string SandboxPhysGunHint(in SandboxHand h)
    {
        if (_sbPhysHint.Stale((SandboxKeys.Stamp * 2L + (h.GrabBody != 0 ? 1 : 0)) * 31 + (SandboxSnapLabel?.GetHashCode() ?? 0)))
            _sbPhysHint.Text = h.GrabBody != 0 && SandboxSnapLabel != null ? $"{SandboxKeys.PhysGun} [{SandboxSnapLabel}]" : SandboxKeys.PhysGun;
        return _sbPhysHint.Text;
    }
}
