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
                return hands.Grab(Constructs, p.Id, p.Eye, p.Yaw, body, local);
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
    public void SandboxGrab(ushort body, Vector3 local)
    {
        ref var h = ref SandboxHands.Hand(LocalId);
        if (!IsServer)
        {
            h.GrabBody = body; // (shown at once; the server answers with its own in the tail, or a refusal. The host's hands are the server's)
            h.GrabLocal = local;
        }
        h.GrabAskedAt = SandboxNow;
        _sbPredOn = false;
        _sbSteerSent = float.NaN;
        PhysGunSend(CActKind.Grab, body, local, 0);
    }

    /// <summary>Client (or host): let go, or freeze what is held and let go.</summary>
    public void SandboxLetGo(bool freeze)
    {
        ref var h = ref SandboxHands.Hand(LocalId);
        if (!IsServer)
            h.GrabBody = 0; // (shown at once; the host's own hands are the server's, which lets go itself)
        _sbPredOn = false;
        h.GrabAskedAt = SandboxNow;
        PhysGunSend(freeze ? CActKind.GrabFreeze : CActKind.GrabRelease, 0, default, 0);
    }

    /// <summary>Client (or host): unfreeze everything you froze (the host may ask for everything).</summary>
    public void SandboxUnfreezeAll(bool everyone) => PhysGunSend(CActKind.UnfreezeAll, 0, default, (byte)(everyone ? 1 : 0));

    private void PhysGunSend(CActKind kind, ushort body, Vector3 local, byte flag)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)kind);
        if (kind == CActKind.Grab)
            w.U16(body).U16(Cm16(local.X)).U16(Cm16(local.Y)).U16(Cm16(local.Z));
        else if (kind == CActKind.UnfreezeAll)
            w.U8(flag);
        if (IsServer)
        {
            if (Local is { } me)
            {
                var r = new PacketReader(w.Span[1..]);
                if (SandboxAct(me, ref r, SandboxNow) is { } why)
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
        if (IsServer || !_sbPredOn || Local is not { Alive: true } me)
        {
            _sbPredHave = false;
            return;
        }
        ref var h = ref SandboxHands.Hand(LocalId);
        if (h.Tool != ConstructTool.PhysicsGun || h.GrabBody == 0 || Constructs.Get(h.GrabBody) is not { } c)
            return;
        var look = LookDir(LookYaw, LookPitch);
        var target = me.RenderEye + look * _sbPredDist;
        var turn = ConstructGrab.YawQ(LookYaw) * _sbPredRel;
        float speed = 0, spin = 0;
        if (_sbPredHave && dt > 1e-4f)
        {
            speed = MathF.Min(Vector3.Distance(target, _sbPredTarget) / dt, 60f);
            spin = MathF.Min(2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(turn, _sbPredTurn)), 0f, 1f)) / dt, 30f);
        }
        _sbPredTarget = target;
        _sbPredTurn = turn;
        _sbPredHave = true;
        ConstructGrab.Predict(c, me.RenderEye, look, LookYaw, _sbPredDist, _sbPredRel, speed, spin);
    }

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

    private SbText _sbPhysHint;

    /// <summary>pw01: what the snap uses while Shift snaps a held piece ("snap: world", "snap: construct 4"); null when not snapping.</summary>
    public string? SandboxSnapLabel;

    /// <summary>The physics gun's key line (made again only when the binds' names or the holding change).</summary>
    public string SandboxPhysGunKeys = "LMB hold: grab, wheel: distance, F + mouse: turn (Shift snaps to the world), RMB: freeze, R R: unfreeze yours";

    public string SandboxPhysGunHint(in SandboxHand h)
    {
        if (_sbPhysHint.Stale((SandboxPhysGunKeys.GetHashCode() * 2L + (h.GrabBody != 0 ? 1 : 0)) * 31 + (SandboxSnapLabel?.GetHashCode() ?? 0)))
            _sbPhysHint.Text = h.GrabBody != 0 ? $"Holding construct {h.GrabBody}. {SandboxPhysGunKeys}{(SandboxSnapLabel != null ? $" [{SandboxSnapLabel}]" : "")}" : SandboxPhysGunKeys;
        return _sbPhysHint.Text;
    }
}
