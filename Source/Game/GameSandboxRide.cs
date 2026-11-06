using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Contraption space on this machine (ConstructRideMove.cs, docs/sandbox/CONSTRUCTS.md, "Contraption space"): the local
/// rider's look is kept in the construct's frame, and <see cref="LookYaw"/> and <see cref="LookPitch"/> are worked out from it
/// as the construct is drawn, so every aim ray, tool and gun that reads them just works. A change to them (the mouse, recoil)
/// is taken as a change of the look in the frame, whichever way the construct is rolled. Riders are drawn from their feet in
/// the frame and the construct's drawn pose, so there is no lag between them.
/// </summary>
public sealed partial class Game
{
    private ushort _rideLookBody;
    private byte _rideLookSeg;
    private Vector3 _rideLookUp;
    private float _rideYaw, _ridePitch, _rideWroteYaw, _rideWrotePitch;

    private Quaternion _rideCamTurn = Quaternion.Identity;
    private Vector3 _rideCamUp, _rideCamWas;
    private ushort _rideCamBody;

    /// <summary>The local rider's look in the frame, for the input (zero body: not riding).</summary>
    private (ushort Body, float Yaw, float Pitch) RideInput() => _rideLookBody != 0 ? (_rideLookBody, _rideYaw, _ridePitch) : default;

    /// <summary>Takes any change to the look into the frame, and measures the look again as the construct is now.</summary>
    private void RideLookSync()
    {
        if (!SandboxOn || Local is not { } me || me.Sim.RideUp == Vector3.Zero || Constructs.Get(me.Sim.RideBody) is not { } c || me.Sim.RideSeg > c.JointCount)
        {
            _rideLookBody = 0;
            if (!IsServer && SandboxOn)
                SandboxHands.Hand(LocalId).RideBody = 0;
            return;
        }
        DrawnFrame(c, me.Sim.RideSeg, out var q, out _);
        var rider = ConstructRide.RiderRot(q, me.Sim.RideUp);
        if (_rideLookBody != me.Sim.RideBody || _rideLookSeg != me.Sim.RideSeg || _rideLookUp != me.Sim.RideUp)
        {
            ConstructRide.LookIn(rider, LookDir(LookYaw, LookPitch), out _rideYaw, out _ridePitch);
            _ridePitch = Math.Clamp(_ridePitch, -1.55f, 1.55f);
            (_rideLookBody, _rideLookSeg, _rideLookUp) = (me.Sim.RideBody, me.Sim.RideSeg, me.Sim.RideUp);
        }
        else
        {
            _rideYaw = World.WrapAngle(_rideYaw + World.WrapAngle(LookYaw - _rideWroteYaw));
            _ridePitch = Math.Clamp(_ridePitch + LookPitch - _rideWrotePitch, -1.55f, 1.55f);
        }
        var dir = Vector3.Transform(World.Forward(_rideYaw, _ridePitch), rider);
        (LookYaw, LookPitch) = GravityFrames.Curved ? GravityFrames.Angles(LookFrame, dir) : (MathF.Atan2(dir.X, -dir.Z), MathF.Asin(Math.Clamp(dir.Y, -1f, 1f)));
        LookPitch = Math.Clamp(LookPitch, -1.55f, 1.55f);
        RideLookRead();
        ref var h = ref SandboxHands.Hand(me.Id);
        if (!IsServer)
        {
            h.RideBody = me.Sim.RideBody;
            h.RideSeg = me.Sim.RideSeg;
        }
        h.RideTurn = Quaternion.Normalize(rider * ConstructGrab.YawQ(_rideYaw));
    }

    /// <summary>What <see cref="LookYaw"/> and <see cref="LookPitch"/> are now, so only later changes count (a planet's transport moves them too).</summary>
    private void RideLookRead() => (_rideWroteYaw, _rideWrotePitch) = (LookYaw, LookPitch);

    /// <summary>
    /// The server, a rider's input: their world look is their look in the frame as the server has the construct (a turn
    /// doesn't bend it by the round trip), so reach and aim on the server are the client's own, in the frame.
    /// </summary>
    private void RideLookIn(Player p, ref InputCmd cmd)
    {
        if (cmd.RideBody != p.Sim.RideBody || Constructs.Get(cmd.RideBody) is not { } c || p.Sim.RideSeg > c.JointCount)
            return;
        c.WorldFrame(p.Sim.RideSeg, out var q, out _);
        var dir = Vector3.Transform(World.Forward(cmd.RideYaw, Math.Clamp(cmd.RidePitch, -1.55f, 1.55f)), ConstructRide.RiderRot(q, p.Sim.RideUp));
        var (yaw, pitch) = GravityFrames.Angles(p.Position, dir);
        cmd.Yaw = yaw;
        cmd.Pitch = Math.Clamp(pitch, -1.55f, 1.55f);
    }

    /// <summary>Server: the frame a player stands in, on their hands (the tools' edits and the physics gun are kept in it).</summary>
    private void SandboxRiderHands(Player p, ref SandboxHand h)
    {
        if (p.Sim.RideUp == Vector3.Zero || !p.Alive || Constructs.Get(p.Sim.RideBody) is not { } c || p.Sim.RideSeg > c.JointCount)
        {
            h.RideBody = 0;
            return;
        }
        c.WorldFrame(p.Sim.RideSeg, out var q, out _);
        var rider = ConstructRide.RiderRot(q, p.Sim.RideUp);
        ConstructRide.LookIn(rider, GravityFrames.Forward(p.Position, p.Yaw, p.Pitch), out var yaw, out _);
        h.RideBody = p.Sim.RideBody;
        h.RideSeg = p.Sim.RideSeg;
        h.RideTurn = Quaternion.Normalize(rider * ConstructGrab.YawQ(yaw));
    }

    /// <summary>Server: the physics gun's aim, on top of the frame its holder stands in: a piece held inside a moving construct moves relative to it.</summary>
    private void SandboxRiderGrab(Player p)
    {
        ref var h = ref SandboxHands.Hand(p.Id);
        var look = GravityFrames.Forward(p.Position, p.Yaw, p.Pitch);
        var vel = Vector3.Zero;
        var spin = Vector3.Zero;
        if (h.RideBody != 0 && Constructs.Get(h.RideBody) is { } c && h.RideSeg <= c.JointCount)
        {
            vel = ConstructRide.SurfaceVel(c, h.RideSeg, p.Eye + look * h.GrabDist);
            spin = ConstructRide.SurfaceSpin(c, h.RideSeg);
        }
        SandboxHands.GrabTick(Constructs, p.Id, p.Eye, look, p.Yaw, vel, spin);
    }

    /// <summary>
    /// A shot from a moving construct: the client's world ray is a round trip behind the server's pose of it (20 m at 200
    /// m/s), so the server takes the shooter's own eye and look, which are exact in the frame. (Below walking speed the client's ray stands.)
    /// </summary>
    private void RideShot(Player shooter, ref Vector3 origin, ref Vector3 dir)
    {
        if (!SandboxOn || shooter.Sim.RideUp == Vector3.Zero || Constructs.Get(shooter.Sim.RideBody) is not { } c || shooter.Sim.RideSeg > c.JointCount)
            return;
        if (ConstructRide.SurfaceVel(c, shooter.Sim.RideSeg, shooter.Position).LengthSquared() < 4f)
            return;
        origin = shooter.Eye;
        dir = GravityFrames.Forward(shooter.Position, shooter.Yaw, shooter.Pitch);
    }

    /// <summary>A rewound target on the shooter's own deck stands where they stood on it, on the deck as it is now.</summary>
    private void RideTarget(Player shooter, ref HistoryEntry at)
    {
        if (at.RideBody == 0 || at.RideBody != shooter.Sim.RideBody || Constructs.Get(at.RideBody) is not { } c || at.RideSeg > c.JointCount)
            return;
        at.Position = ConstructRide.ToWorld(c, at.RideSeg, at.RideLocal);
    }

    private Construct? _rideDrawn;
    private Vector3 _rideTickPos;
    private Quaternion _rideTickRot;

    /// <summary>
    /// The host's input and overlays see the construct it rides as it is drawn this frame: a host draws between ticks, and at
    /// 200 m/s the tick's pose is up to 3 m from the drawn one, so an aim ray from the drawn eye would miss the parts it is on.
    /// (A client's bodies are at their drawn pose already.) <see cref="RideTickPose"/> puts the tick's pose back.
    /// </summary>
    internal void RideDrawnPose()
    {
        if (!IsServer || _rideDrawn != null || !SandboxOn || Local is not { } me)
            return;
        var body = me.Sim.RideUp != Vector3.Zero ? me.Sim.RideBody : SandboxHands.Hand(me.Id).SeatBody; // (standing in it, or seated: the eye is on the drawn pose either way)
        if (Constructs.Get(body) is not { } c)
            return;
        _rideDrawn = c;
        (_rideTickPos, _rideTickRot) = (c.Pos, c.Rot);
        c.Pos = Vector3.Lerp(c.PrevPos, c.Pos, _alpha);
        c.Rot = Quaternion.Normalize(Quaternion.Slerp(c.PrevRot, c.Rot, _alpha));
    }

    internal void RideTickPose()
    {
        if (_rideDrawn is not { } c)
            return;
        (c.Pos, c.Rot) = (_rideTickPos, _rideTickRot);
        _rideDrawn = null;
    }

    /// <summary>
    /// A tick that took a player into a frame (a landing, another segment): the draw between the ticks starts where the feet
    /// were, carried with the deck, not at the landing spot, so the last tick of a fall isn't skipped.
    /// </summary>
    private void RideEntryDraw(Player p)
    {
        ref var s = ref p.Sim;
        if (s.RideBody == 0 || (p.PrevRideBody == s.RideBody && p.PrevRideSeg == s.RideSeg) || Constructs.Get(s.RideBody) is not { } c || s.RideSeg > c.JointCount)
            return;
        var was = p.PrevPosition + ConstructRide.SurfaceVel(c, s.RideSeg, s.Position) * World.TickDt;
        if (Vector3.DistanceSquared(was, s.Position) > 4f)
            return; // (a teleport, a respawn: nothing to blend from)
        (p.PrevRideBody, p.PrevRideSeg, p.PrevRideLocal) = (s.RideBody, s.RideSeg, ConstructRide.ToLocal(c, s.RideSeg, was));
    }

    /// <summary>Server: after the constructs' step every rider is where their construct now is, whether or not an input came in this tick.</summary>
    private void SandboxRidersFollow()
    {
        foreach (var p in Players.Values)
        {
            if (!p.Alive || p.Sim.RideUp == Vector3.Zero || Constructs.Get(p.Sim.RideBody) is not { } c || p.Sim.RideSeg > c.JointCount)
                continue;
            var d = p.Sim.Position - p.Sim.RideWorld;
            ConstructRide.Place(ref p.Sim, c);
            p.Sim.Position += d;
        }
    }

    /// <summary>
    /// Every frame, a rider in contraption space: the body stands in the construct as it is drawn this frame, the feet from
    /// the frame (between ticks for this machine's own simulation, from the snapshots for a remote rider). The camera follows.
    /// </summary>
    private void SandboxFrameRender(Player p, bool remote)
    {
        var body = remote ? p.SnapRideBody : p.Sim.RideBody;
        var seg = remote ? p.SnapRideSeg : p.Sim.RideSeg;
        var up = remote ? p.SnapRideUp : p.Sim.RideUp;
        var local = remote ? p.SnapRideLocal : p.Sim.RideLocal;
        if (!remote && p.PrevRideBody == body && p.PrevRideSeg == seg)
            local = Vector3.Lerp(p.PrevRideLocal, local, _alpha);
        if (body == 0 || Constructs.Get(body) is not { } c || seg > c.JointCount)
            return;
        DrawnFrame(c, seg, out var q, out var t);
        p.RenderPosition = t + Vector3.Transform(local, q) + (remote || IsServer ? Vector3.Zero : p.Id == LocalId ? _correction : Vector3.Zero);
        var rider = ConstructRide.RiderRot(q, Vector3.Normalize(up));
        p.RenderRide = true;
        p.RenderRideRot = rider;
        if (p.Id == LocalId)
        {
            RideLookSync();
            p.RenderYaw = p.Yaw = LookYaw;
            p.Pitch = LookPitch;
            p.RenderPitch = _ridePitch;
            p.RenderRideYaw = _rideYaw;
        }
        else
        {
            ConstructRide.LookIn(rider, GravityFrames.Forward(p.RenderPosition, p.RenderYaw, p.RenderPitch), out var yaw, out var pitch);
            p.RenderRideYaw = yaw;
            p.RenderPitch = pitch;
        }
    }

    /// <summary>
    /// The camera's up: the construct's while riding, exactly as it is drawn, and turning onto or off a construct (or between
    /// its segments, or onto a grip face) rolls over about a quarter of a second, never snapping.
    /// </summary>
    public Vector3 RideCamUp(Vector3 up, float dt)
    {
        if (!SandboxOn)
            return up;
        if (Local is { RenderRide: true, Alive: true } me)
            up = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, me.RenderRideRot));
        var body = Local is { RenderRide: true } ? Local.Sim.RideBody : (ushort)0;
        var key = _rideLookUp;
        if ((body != _rideCamBody || key != _rideCamUp) && _rideCamWas != Vector3.Zero)
            _rideCamTurn = ConstructRide.TurnBetween(up, _rideCamWas);
        (_rideCamBody, _rideCamUp) = (body, key);
        if (_rideCamTurn == Quaternion.Identity)
            return _rideCamWas = up;
        _rideCamTurn = Quaternion.Slerp(_rideCamTurn, Quaternion.Identity, 1f - MathF.Exp(-dt * 10f));
        if (Quaternion.Dot(_rideCamTurn, Quaternion.Identity) > 0.99999f)
            _rideCamTurn = Quaternion.Identity;
        var shown = Vector3.Normalize(Vector3.Transform(up, _rideCamTurn));
        _rideCamWas = shown;
        return shown;
    }

    /// <summary>What this player wears from the backpack (PilotGear.cs). In a creative world, all of it: its backpack holds nothing.</summary>
    private byte SandboxWorn(Player p)
    {
        if (p.Bot != null)
            return 0;
        if (SandboxInfiniteParts)
            return PilotGear.Chute | PilotGear.Glider | PilotGear.Jet;
        var pack = SandboxHands.Pack(p.Id);
        byte gear = 0;
        for (var k = 0; k < pack.SlotCount; k++)
            if (pack.Count[k] > 0 && ConstructParts.Def(pack.Def[k]) is { } d)
                gear |= d.Wear;
        return gear;
    }

    /// <summary>An ejection seat fires: the rider goes straight up out of the craft at its speed plus the seat's kick, and a parachute opens on its own.</summary>
    private void SandboxEject(Player p, Construct c, int seat)
    {
        var feet = ConstructEdits.SeatFeet(c, seat);
        var vel = ConstructRide.SurfaceVel(c, c.Parts[seat].Seg, feet);
        c.WorldFrame(c.Parts[seat].Seg, out var q, out _);
        var up = Vector3.Transform(Rot24.Apply(c.Parts[seat].Rot, Vector3.UnitY), q);
        SandboxStand(p, teleport: false);
        var spot = feet + up * EjectClear;
        p.Teleport(World.ClampToArena(spot));
        p.Sim.RideBody = 0;
        p.Sim.RideUp = default;
        p.Sim.RideAir = p.Sim.RideClimb = 0;
        p.Sim.Velocity = vel + up * EjectKick;
        p.Sim.Grounded = false;
        p.Sim.RideFree = ConstructRide.FreeTicks; // (the craft's speed carries them until the parachute takes over)
        p.Sim.Canopy = 0;
        p.Sim.GearDelay = PilotGear.EjectDelay;
        Constructs.Events.Add($"construct {c.Id}: {p.Name} ejected");
    }

    private const float EjectClear = 2.2f, EjectKick = 24f; // m above the seat, m/s
}
