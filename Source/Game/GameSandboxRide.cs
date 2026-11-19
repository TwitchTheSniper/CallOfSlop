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
            h.RideSeg = (byte)ConstructRide.Carrier(me.Sim); // (the surface carrying them, a lift's or a turntable's: what a drop or a held piece moves with)
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
        h.RideSeg = (byte)ConstructRide.Carrier(p.Sim); // (the surface carrying them, a lift's or a turntable's: what a drop or a held piece moves with)
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
        if (ConstructRide.RiderSurfaceVel(c, shooter.Sim, shooter.Position).LengthSquared() < 4f)
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
    private Construct[] _rideDrawnSet = new Construct[8]; // (the ridden body and the bodies that move with it: its carrier and what that carries)
    private Vector3[] _rideTickPosSet = new Vector3[8];
    private Quaternion[] _rideTickRotSet = new Quaternion[8];
    private int _rideDrawnCount;

    /// <summary>
    /// The host's input and overlays see the construct it rides as it is drawn this frame: a host draws between ticks, and at
    /// 200 m/s the tick's pose is up to 3 m from the drawn one, so an aim ray from the drawn eye would miss the parts it is on.
    /// The bodies that move with it go to their drawn pose too: the carrier it is docked in and everything that carrier
    /// carries (a Limpet in the Zero Day's bay). Left at the tick's pose, the carrier's keel stood up to a tick's travel
    /// through the docked craft's floor and the look jumped between the couch, the floor and the keel under them.
    /// (A client's bodies are at their drawn pose already.) <see cref="RideTickPose"/> puts the tick's poses back.
    /// </summary>
    internal void RideDrawnPose()
    {
        if (!IsServer || _rideDrawn != null || !SandboxOn || Local is not { } me)
            return;
        var body = me.Sim.RideUp != Vector3.Zero ? me.Sim.RideBody : SandboxHands.Hand(me.Id).SeatBody; // (standing in it, or seated: the eye is on the drawn pose either way)
        if (Constructs.Get(body) is not { } c)
            return;
        _rideDrawn = c;
        _rideDrawnCount = 0;
        RideDrawnAdd(c);
        var root = c.CarriedBy != 0 && Constructs.Get(c.CarriedBy) is { } carrier ? carrier : c; // (a carrier carries only while it isn't carried itself: ConstructCargo)
        if (root != c)
            RideDrawnAdd(root);
        if (root.CargoCount > 0 || root != c) // (the carrier's cargo list counts what it carries: most bodies carry nothing)
            foreach (var b in Constructs.Bodies) // (a list: no allocation)
                if (b.CarriedBy == root.Id && b != c)
                    RideDrawnAdd(b);
        if (root.SpaceShip != 0 || c.SpaceShip != 0 || SandboxHands.Hand(me.Id).GrabBody != 0)
            foreach (var b in Constructs.Bodies) // (a list: no allocation)
                if (b != c && b != root && b.CarriedBy != root.Id && ((b.SpaceRide != 0 && (b.SpaceRide == root.Id || b.SpaceRide == c.Id)) || b.GrabbedBy == me.Id))
                    RideDrawnAdd(b); // (loose in the ship's frame in space, or in this player's beam: they move with the deck, so they are aimed at as drawn too, or at orbital speed the ray from the drawn eye passed a tick's travel from them)
        Constructs.GridStale(); // (the broadphase sees these bodies where they are drawn until RideTickPose: one built in this window must not serve the next tick)
    }

    private void RideDrawnAdd(Construct c)
    {
        if (_rideDrawnCount == _rideDrawnSet.Length)
        {
            Array.Resize(ref _rideDrawnSet, _rideDrawnCount * 2); // (rare: a carrier with more craft aboard than any before; kept from then on)
            Array.Resize(ref _rideTickPosSet, _rideDrawnCount * 2);
            Array.Resize(ref _rideTickRotSet, _rideDrawnCount * 2);
        }
        _rideDrawnSet[_rideDrawnCount] = c;
        _rideTickPosSet[_rideDrawnCount] = c.Pos;
        _rideTickRotSet[_rideDrawnCount] = c.Rot;
        _rideDrawnCount++;
        c.Pos = Vector3.Lerp(c.PrevPos, c.Pos, _alpha);
        c.Rot = Quaternion.Normalize(Quaternion.Slerp(c.PrevRot, c.Rot, _alpha));
    }

    internal void RideTickPose()
    {
        if (_rideDrawn == null)
            return;
        for (var k = 0; k < _rideDrawnCount; k++)
        {
            (_rideDrawnSet[k].Pos, _rideDrawnSet[k].Rot) = (_rideTickPosSet[k], _rideTickRotSet[k]);
            _rideDrawnSet[k] = null!;
        }
        _rideDrawnCount = 0;
        _rideDrawn = null;
        Constructs.GridStale(); // (a grid built at the drawn poses goes: the server's next query builds it from the tick's)
    }

    /// <summary>
    /// Where a point of <paramref name="c"/> at its tick pose is drawn this frame: the host draws between its last two ticks,
    /// so a screen anchor taken from the tick's pose (the configure panel beside its part) jumped by up to a tick's travel
    /// each frame on a ship in orbit. Unchanged on a client (its bodies are drawn where they are) and inside RideDrawnPose.
    /// </summary>
    public Vector3 DrawnPoint(Construct c, Vector3 tickPoint)
    {
        if (!IsServer || RideDrawnHas(c))
            return tickPoint;
        var pos = Vector3.Lerp(c.PrevPos, c.Pos, _alpha);
        var rot = Quaternion.Normalize(Quaternion.Slerp(c.PrevRot, c.Rot, _alpha));
        return pos + Vector3.Transform(Vector3.Transform(tickPoint - c.Pos, Quaternion.Conjugate(c.Rot)), rot);
    }

    /// <summary>Is <paramref name="c"/> at its drawn pose now (RideDrawnPose's set)?</summary>
    private bool RideDrawnHas(Construct c)
    {
        for (var k = 0; k < _rideDrawnCount; k++)
            if (_rideDrawnSet[k] == c)
                return true;
        return false;
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

    /// <summary>
    /// The host's aim from the drawn eye into the tick's frame. The host draws between its last two ticks (<c>_alpha</c>), but
    /// constructs are hit, and reach measured, where the last tick put them: on a ship at orbital speed that is metres apart,
    /// so a part beside a seated or standing rider read as too far away, or the ray missed it. A rider's eye moves with the
    /// construct it is in (in the construct's frame, so the ship's speed drops out); anyone else's to their own tick position.
    /// A client draws its constructs where it hits them already, so nothing changes there. Inside <see cref="RideDrawnPose"/>
    /// the ridden body stands at its drawn pose, so the eye stays where it is drawn.
    /// </summary>
    internal void AimToTick(Player me, ref Vector3 eye, ref Vector3 dir)
    {
        if (!IsServer || !SandboxOn || !me.Alive)
            return;
        ref var h = ref SandboxHands.Hand(me.Id);
        var body = h.Seated ? h.SeatBody : me.Sim.RideBody;
        if (body != 0 && Constructs.Get(body) is { } c)
        {
            if (RideDrawnHas(c))
                return; // (RideDrawnPose has posed it with the ridden body, a seat's craft docked in a carrier too: RideDrawnPose has put this body where it is drawn, under the drawn eye already: moving the eye too put it (a - a^2) of a tick's travel off, a metre at orbital speed, so the look flickered on and off seats and ladders as the frame's blend changed)
            var pos = Vector3.Lerp(c.PrevPos, c.Pos, _alpha);
            var rot = Quaternion.Slerp(c.PrevRot, c.Rot, _alpha);
            var turn = Quaternion.Normalize(c.Rot * Quaternion.Conjugate(rot));
            eye = c.Pos + Vector3.Transform(eye - pos, turn); // (small numbers: the eye from the drawn body, then from the body at the tick)
            dir = Vector3.Normalize(Vector3.Transform(dir, turn));
            return;
        }
        eye += me.Position - me.RenderPosition; // (on foot: the drawn body is between the ticks too)
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
    /// pg02: the local rider's eye as <see cref="SandboxFrameRender"/> will draw it this frame (the feet in the construct's drawn
    /// frame), for what runs before it: the physics gun's client simulation. <see cref="Player.RenderEye"/> is still at the
    /// tick's pose then, metres from the drawn deck at a ship's speed. Not riding: the render eye.
    /// </summary>
    internal Vector3 RiderDrawnEye(Player me)
    {
        var body = me.Sim.RideBody;
        var seg = me.Sim.RideSeg;
        if (me.Sim.RideUp == Vector3.Zero || body == 0 || Constructs.Get(body) is not { } c || seg > c.JointCount)
            return me.RenderEye;
        var local = me.Sim.RideLocal;
        if (me.PrevRideBody == body && me.PrevRideSeg == seg)
            local = Vector3.Lerp(me.PrevRideLocal, local, _alpha);
        DrawnFrame(c, seg, out var q, out var t);
        var feet = t + Vector3.Transform(local, q) + (IsServer ? Vector3.Zero : _correction);
        return feet + Vector3.Transform(new Vector3(0, me.RenderEyeHeight, 0), ConstructRide.RiderRot(q, Vector3.Normalize(me.Sim.RideUp)));
    }

    /// <summary>pg02: the local rider's look taken into the construct's drawn frame now (as the ride render does after), so the physics gun's simulation aims as drawn.</summary>
    internal void RideLookNow()
    {
        if (Local is { } me && me.Sim.RideUp != Vector3.Zero)
            RideLookSync();
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
        DrawnFrame(c, seg, out var q, out var t, out var bodyPos, out var segRel);
        var rel = segRel + Vector3.Transform(local, q) + (remote || IsServer ? Vector3.Zero : p.Id == LocalId ? _correction : Vector3.Zero);
        p.RenderPosition = t + Vector3.Transform(local, q) + (remote || IsServer ? Vector3.Zero : p.Id == LocalId ? _correction : Vector3.Zero);
        p.RenderRideBody = bodyPos;
        p.RenderRideRel = rel;
        var rider = ConstructRide.RiderRot(q, Vector3.Normalize(up));
        p.RenderRide = true;
        p.RenderRideRot = rider;
        p.RenderRideFeet = Vector3.Transform(p.RenderPosition - t, Quaternion.Conjugate(rider));
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
        var seatUp = SeatFrameUp; // (seated where the look is kept in the seat's frame: its up, GameSandbox.cs)
        if (seatUp != Vector3.Zero)
            up = Vector3.Normalize(seatUp);
        var body = Local is { RenderRide: true } ? Local.Sim.RideBody : seatUp != Vector3.Zero ? (ushort)(_seatFrameKey >> 16) : (ushort)0;
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
        if ((p.Eva & Eva.Mmu) != 0)
            return PilotGear.Mmu; // (a spacewalker's pack: it stands in for the rest, GameSandboxEva.cs)
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
        p.Sim.RideAir = p.Sim.RideClimb = p.Sim.RideLadder = 0;
        p.Sim.Velocity = vel + up * EjectKick;
        p.Sim.Grounded = false;
        p.Sim.RideFree = ConstructRide.FreeTicks; // (the craft's speed carries them until the parachute takes over)
        p.Sim.Canopy = 0;
        p.Sim.GearDelay = PilotGear.EjectDelay;
        Constructs.Events.Add($"construct {c.Id}: {p.Name} ejected");
    }

    private const float EjectClear = 2.2f, EjectKick = 24f; // m above the seat, m/s
}
