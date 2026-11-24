using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Local-frame physics (docs/sandbox/design/local-frame-physics.md, option B): a loose body aboard a moving construct (a
/// crate in a hold, a part dropped on a deck, a piece in the physics gun) keeps its pose and motion in that construct's frame
/// as its truth (<see cref="Construct.Frame"/>, <see cref="Construct.FramePos"/>, <see cref="Construct.FrameRot"/>,
/// <see cref="Construct.FrameVel"/>, <see cref="Construct.FrameSpin"/>), as contraption space does for players. Its world
/// pose and velocity are derived from the frame's after every step, for drawing, rays and shots, contacts with things outside
/// and leaving.
/// <para>
/// The physics still steps it with everything else (its contacts with the hull, with other bodies aboard and with outside
/// things are the solver's), but every rule that reads a speed reads it against the frame (damping, the speed limits, sleep:
/// <see cref="ConstructPhysics"/>), its pull is the one a player standing there feels (the deck's gravity along the rider's
/// up, a gravity generator's; in space the ship's free fall on top, so an orbit is rest), and after the step (<see cref="After"/>)
/// its move through the frame is taken and it is placed by the frame's new pose. So a steady cruise is rest, and the frame's
/// changes of motion (braking, a turn, a hit, a thrust in space) are felt in full as the world's maths gives them; a body asleep
/// aboard is carried with no step at all, until the frame's acceleration wakes it.
/// </para>
/// <para>
/// Two passes a tick (server): <see cref="After"/> with <c>posed: false</c> at the end of the world's step, for frames the
/// physics stepped (or carried: cargo); with <c>posed: true</c> after the space step has posed the ships out of the bubble
/// (GameSpace.cs), for those. Joining and leaving keep the world pose and velocity as they are, so neither jumps. Nothing
/// here allocates.
/// </para>
/// </summary>
public static class ConstructFrames
{
    /// <summary>A body joins a frame inside its bounds padded this much (m), or this far over its top (the spawn box: a block on the ground beside a truck stays the world's).</summary>
    public const float JoinPad = ConstructRide.SpawnPad, JoinTop = ConstructRide.SpawnAbove;

    /// <summary>An awake loose body not in a frame looks for one every this many ticks (staggered by id).</summary>
    public const int SearchEvery = 4;

    /// <summary>A body asleep in a frame wakes when the frame's acceleration at it (less the free fall) passes this (m/s2, smoothed): braking, a turn, a hit.</summary>
    public const float WakeFelt = 1.5f;

    /// <summary>A body's own sum of its moves through its frame is trusted unless the world's pose says it moved this much more (m): something outside the step moved it.</summary>
    public const float MoveTrust = 0.05f;

    /// <summary>A body touching the map leaves a frame moving faster than this past it (m/s): the ground through an open bay holds it, not the ship.</summary>
    public const float GroundLeaveSpeed = 1f;

    /// <summary>The space clock's speed (the host sets it before the posed pass): past 1x a ship's change of motion is not felt (time runs faster out there than in the bubble).</summary>
    public static float SpaceTimeSpeed = 1;

    private const float StillSpeed2 = 0.05f * 0.05f; // (ConstructRide.StillSpeed: a frame this slow isn't moving)

    private static readonly List<Construct> _near = new(32);
    private static uint _tick;

    /// <summary>The last passes' counts (the ride check, the F9 window): bodies in frames, joins, leaves.</summary>
    public static int LastBodies, Joins, Leaves;

    /// <summary>May <paramref name="c"/> ride a frame (a loose, free body that carries none itself)?</summary>
    public static bool CanRide(Construct c) =>
        c.PartCount > 0 && !c.Anchored && c.CarriedBy == 0 && c.SpaceShip == 0 && !c.Frozen && !c.Pinned && (c.HeldBy == 0 || c.GantryHold) && c.NudgeBy == 255 && c.LegCount == 0; // (a walker's feet are drawn in the world: not in a frame)

    /// <summary>May <paramref name="f"/> be <paramref name="c"/>'s frame (free, bigger and heavier, in no frame itself, not moved by the nudge tool: a nudged pose is set, not stepped, so the step can't carry what rides it)?</summary>
    public static bool CanCarry(Construct f, Construct c) =>
        f != c && f.PartCount > 0 && !f.Anchored && f.CarriedBy == 0 && f.Frame == 0 && f.SpaceAway == 0 && f.NudgeBy == 255
        && f.Radius > c.Radius * 1.5f && f.Mass > c.Mass * 2f;

    private static bool Moving(Construct f) =>
        f.SpaceShip != 0 || !f.Asleep && (f.Vel.LengthSquared() > StillSpeed2 || f.AngVel.LengthSquared() > StillSpeed2);

    /// <summary>
    /// The moving construct whose frame (its bounds, padded by <see cref="JoinPad"/>, <see cref="JoinTop"/> over its top) holds
    /// <paramref name="c"/>'s centre, the smallest when they nest; only ships out of the bubble when <paramref name="posed"/>,
    /// only others when not. Null: none.
    /// </summary>
    public static Construct? Find(ConstructWorld w, Construct c, bool posed)
    {
        var at = c.WorldCom;
        var near = w.Near(at, ConstructRide.BoundsAbove + 1f, _near);
        Construct? best = null;
        var bestSize = float.MaxValue;
        var up = GravityFrames.UpAt(at);
        for (var i = 0; i < near.Count; i++)
        {
            var f = near[i];
            if (!CanCarry(f, c) || (f.SpaceShip != 0) != posed || !Moving(f) || f.Frame != 0)
                continue;
            var iq = Quaternion.Conjugate(f.Rot);
            if (!ConstructRide.InFrame(f, Vector3.Transform(at - f.Pos, iq), Vector3.Transform(up, iq), JoinPad, JoinTop))
                continue;
            var size = (f.LocalMax - f.LocalMin).LengthSquared();
            if (size < bestSize)
                (best, bestSize) = (f, size);
        }
        return best;
    }

    /// <summary><paramref name="c"/> comes into <paramref name="f"/>'s frame where it is now, moving as it moves now (nothing jumps).</summary>
    public static void Join(Construct c, Construct f)
    {
        if (c.Dirty)
            c.Rebuild();
        c.Frame = f.Id;
        c.FrameNew = false;
        var iq = Quaternion.Conjugate(f.Rot);
        c.FramePos = Vector3.Transform(c.Pos - f.Pos, iq);
        c.FrameRot = Quaternion.Normalize(iq * c.Rot);
        var com = c.WorldCom;
        c.FrameUp = ConstructRide.UpFor(f, GravityFrames.UpAt(com)); // (the up a rider takes there: one direction for the whole construct)
        c.FrameDt = 0;
        c.FrameFelt = 0;
        Note(c, f, com);
        c.FrameVel = Vector3.Transform(c.Vel - c.FrameV, iq);
        c.FrameSpin = Vector3.Transform(c.AngVel - f.AngVel, iq);
        Pull(c, f, com);
        FreeFall(c, f, seed: true);
        Joins++;
    }

    /// <summary><paramref name="c"/> leaves its frame: its world pose and velocity are what they are (its own speed and the frame's there), kept past the speed limit.</summary>
    public static void Leave(Construct c)
    {
        if (c.Frame == 0)
            return;
        if (c.LooseFrom == c.Frame)
            (c.LooseFrom, c.LooseClear) = (0, 0); // (out of the frame of the ship that let it go: clear of it)
        c.Frame = 0;
        c.FrameNew = false;
        c.FrameDt = 0;
        if (c.FrameFarSlept)
        {
            c.FrameFarSlept = false;
            c.Wake(); // (put to sleep only for its frame's sake: the world's own rules from here)
        }
        c.KeepSpeed(); // (a ship's speed: past the physics' limit for a free body, Construct.SpeedKept)
        Leaves++;
    }

    /// <summary>The frame's motion at <paramref name="c"/>'s centre now, and where both are: what the next carry measures from.</summary>
    private static void Note(Construct c, Construct f, Vector3 com)
    {
        c.FrameMove = Vector3.Zero;
        c.FrameV = f.PointVel(com);
        c.FrameW = f.AngVel;
        c.FrameX0 = com;
        c.FrameC0 = f.WorldCom;
        c.FrameVc0 = f.Vel;
        c.FrameQ0 = f.Rot;
    }

    /// <summary>
    /// The pull on <paramref name="c"/> in its frame for the next step (world axes): where a player stands there, the deck's gravity
    /// along the up they take (a gravity generator's box gives its own), and wells as everywhere; in space the free fall the ship
    /// itself feels (<see cref="Construct.FrameFree"/>), so the orbit is rest in the frame.
    /// </summary>
    private static void Pull(Construct c, Construct f, Vector3 com, bool grounded = false)
    {
        if (c.LooseFrom == f.Id)
            c.FrameG = Vector3.Zero; // (just let go by this ship's dock: its contacts with the hull it may lie into are dropped, so no deck pull drops it through; ConstructPhysics.LooseFromCarrier)
        else if (grounded && f.SpaceShip == 0)
            c.FrameG = GravityFrames.Curved || GravityFrames.HookCount > 0 ? ConstructGravity.GravityFor(c, com) : new Vector3(0, -ConstructPhysics.GravityAccel, 0); // (held by the map: the world's pull, not a tilted deck's; a wheel fallen off a car that leans on that corner topples as it would beside it)
        else if (ConstructGravity.Generators > 0 && ConstructGravity.GenAt(com, c.Id) >= 0)
            c.FrameG = ConstructGravity.GravityFor(c, com);
        else if (ShipGravity.Mode != ShipGravityMode.Deck && ShipGravity.Active(f))
        {
            // Ship gravity (ShipGravity.cs): no deck pull. In space the ship's own change of motion reaches the body as its
            // flight's (FreeFall: the drive and hits); in the bubble the solver meets the hull's acceleration, so the world's pull is all.
            c.FrameG = f.SpaceShip != 0 ? Vector3.Zero : ShipGravity.WorldPull(com);
            if (ConstructGravity.Wells > 0)
                c.FrameG += ConstructGravity.WellsAt(com, c.Id);
        }
        else
        {
            c.FrameG = -Vector3.Transform(c.FrameUp, f.Rot) * (World.Gravity * World.GravityScale);
            if (ConstructGravity.Wells > 0)
                c.FrameG += ConstructGravity.WellsAt(com, c.Id);
        }
    }

    /// <summary>
    /// A ship out of the bubble is posed after the step, so its change of motion can't meet its loose bodies in the solve. The
    /// carry gives them its whole change of velocity (an orbit's fall and all), and what of it they should feel goes into their
    /// next step as a pull (<see cref="Construct.FrameFree"/>, like the deck's gravity, so the contacts take it smoothly): the
    /// change less the ship's free fall (its change less what its drive gave, smoothed, <see cref="Construct.FrameGrav"/>),
    /// so the drive and a hit are felt and an orbit is rest. Nothing is felt at a raised time speed (a tick is more space time).
    /// </summary>
    private static void FreeFall(Construct c, Construct f, bool seed)
    {
        c.FrameFree = Vector3.Zero;
        if (f.SpaceShip == 0)
            return;
        if (seed)
        {
            c.FrameGrav = GravityFrames.Point is { } pg && pg.FreeFall(f.WorldCom, out var g) ? g : Vector3.Zero;
            return;
        }
        if (SpaceTimeSpeed > 1)
            return;
        // The drive's push is felt as it is (the ship's own sum); the rest of the change is the fall, smoothed, which the flight's
        // integrator gives a tick at a time a little unevenly; only a change far off it (a hit, a ram) is felt as well.
        var a = (f.Vel - c.FrameVc0) * World.TickRate;
        var rest = a - f.SpaceAccel - c.FrameGrav;
        c.FrameGrav += rest * 0.1f;
        c.FrameFree = -f.SpaceAccel - (rest.LengthSquared() > HitFelt * HitFelt ? rest : Vector3.Zero);
    }

    /// <summary>A ship out of the bubble's change of motion this far (m/s2) off its drive and its fall is a hit, and is felt.</summary>
    public const float HitFelt = 3f;

    /// <summary>
    /// Server, after a step: every body in a frame of the pass's kind (<paramref name="posed"/>: ships out of the bubble, posed
    /// after the step) is carried by it, and loose bodies not in one look for one (in turn) or leave theirs. Bounded per body:
    /// a few transforms each, a broadphase query for a search; nothing allocates.
    /// </summary>
    public static void After(ConstructWorld w, bool posed, bool search = true)
    {
        var bodies = w.Bodies;
        if (!posed)
        {
            _tick++;
            ShipGravity.NewTick();
            for (var i = 0; i < bodies.Count; i++)
                bodies[i].FrameRiders = 0;
            LastBodies = 0;
            for (var i = 0; i < bodies.Count; i++)
                if (bodies[i].Frame != 0 && w.Get(bodies[i].Frame) is { } f)
                {
                    f.FrameRiders++;
                    LastBodies++;
                }
        }
        // The moving constructs that could be frames in this pass: a loose body is looked up in the broadphase only near one
        // (a thousand pieces of debris on the ground with nothing moving cost a test each, no query).
        _candCount = 0;
        var overflow = false;
        if (search)
            for (var i = 0; i < bodies.Count; i++)
            {
                var f = bodies[i];
                if ((f.SpaceShip != 0) != posed || f.PartCount == 0 || f.Anchored || f.CarriedBy != 0 || f.Frame != 0 || f.SpaceAway != 0 || f.NudgeBy != 255 || !Moving(f))
                    continue;
                if (_candCount == _cand.Length)
                {
                    overflow = true;
                    break;
                }
                _cand[_candCount++] = f;
            }
        for (var i = 0; i < bodies.Count; i++)
        {
            var c = bodies[i];
            if (c.Frame != 0)
            {
                var f = w.Get(c.Frame);
                if (f == null || f.PartCount == 0 || f.Anchored || f.CarriedBy != 0 || f.Frame != 0 || f.NudgeBy != 255 || !CanRide(c) || f.Asleep && f.SpaceShip == 0)
                {
                    Leave(c); // (the frame went, froze, was caught or stopped and sleeps: a still frame is the world's; or the body was caught, frozen or pinned)
                    continue;
                }
                if ((f.SpaceShip != 0) != posed)
                    continue; // (the other pass's)
                Carry(c, f, posed, FarFromPeople(w, c, f));
                continue;
            }
            if (!search || _candCount == 0 || c.Asleep || !CanRide(c) || (_tick + c.Id) % SearchEvery != 0 || !overflow && !NearCandidate(c))
                continue;
            if (Find(w, c, posed) is { } frame)
            {
                Join(c, frame);
                if (c.FrameRiders > 0)
                    Rehome(w, c, frame); // (a truck with a crate aboard driven into a moving carrier: the crate comes into the carrier's frame too)
            }
        }
    }

    /// <summary>
    /// Is frame <paramref name="f"/> (with rider <paramref name="c"/>) further than <see cref="ConstructFar.Range"/> from every person? Its riders
    /// are then carried asleep, with no step (Sean, 2026-11-24: "I don't need to care about the hauler's cargo sliding around inside of it
    /// when it's way off in the sky"), and settle when someone comes near. <see cref="ConstructFar.Hysteresis"/> either side of the line.
    /// </summary>
    private static bool FarFromPeople(ConstructWorld w, Construct c, Construct f)
    {
        if (ConstructFar.Range <= 0 || w.PeopleCount == 0 || c.GrabbedBy != 255 || c.NudgeBy != 255)
            return false;
        var reach = (c.FrameFarSlept ? ConstructFar.Range - ConstructFar.Hysteresis : ConstructFar.Range + ConstructFar.Hysteresis) + f.Radius;
        var at = f.WorldCom;
        for (var k = 0; k < w.PeopleCount; k++)
            if (Vector3.DistanceSquared(w.People[k], at) < reach * reach)
                return false;
        return true;
    }

    private static readonly Construct[] _cand = new Construct[64];
    private static int _candCount;

    /// <summary>Is <paramref name="c"/> within reach of one of this pass's moving constructs (their spheres, padded as a frame is)?</summary>
    private static bool NearCandidate(Construct c)
    {
        var at = c.WorldCom;
        for (var k = 0; k < _candCount; k++)
        {
            var f = _cand[k];
            if (f == c)
                continue;
            var reach = f.Radius + c.Radius + ConstructRide.BoundsAbove + 1f;
            if (Vector3.DistanceSquared(at, f.WorldCom) <= reach * reach)
                return true;
        }
        return false;
    }

    /// <summary>
    /// <paramref name="c"/> has just joined <paramref name="frame"/> and carried bodies of its own: they come into that frame
    /// too, where they are (frames don't nest; a crate in a truck in a carrier rides the carrier's, and meets the truck's bed
    /// in the solve as two bodies of one frame). Rare: a whole-list pass, no allocation.
    /// </summary>
    private static void Rehome(ConstructWorld w, Construct c, Construct frame)
    {
        foreach (var b in w.Bodies)
            if (b.Frame == c.Id)
                Join(b, frame);
        frame.FrameRiders += c.FrameRiders;
        c.FrameRiders = 0;
    }

    /// <summary>
    /// <paramref name="c"/> carried by frame <paramref name="f"/> after a step: its move through the frame taken (a frame the step
    /// moved: where it is in the frame's new pose; a posed one, which stood at its last pose through the step and met it at its
    /// velocity there: its move less the frame's motion), then placed by the frame's new pose, its velocity what it is in the
    /// world (so the frame's change of motion is felt), its last pose where the frame's last pose held it (the host's blend).
    /// </summary>
    private static void Carry(Construct c, Construct f, bool posed, bool far)
    {
        if (c.FrameNew)
        {
            if (c.FrameLoaded)
            {
                // From a save: placed by its saved frame pose (a ship in space comes back posed by its flight, not where its
                // construct was saved), still with the frame, awake so it settles.
                if (c.Dirty)
                    c.Rebuild();
                c.Rot = Quaternion.Normalize(f.Rot * c.FrameRot);
                c.Pos = f.Pos + Vector3.Transform(c.FramePos, f.Rot);
                (c.PrevPos, c.PrevRot) = (c.Pos, c.Rot);
                c.Vel = f.PointVel(c.WorldCom);
                c.AngVel = f.AngVel;
                c.Wake();
                c.FrameLoaded = false;
            }
            Join(c, f); // (or joined in the space step: its frame pose from the world as it is now)
            return;
        }
        if (c.Dirty)
            c.Rebuild();
        if ((f.SpaceAway != 0 || far) && !c.Asleep)
        {
            c.Asleep = true; // (its ship parked far out or in another system: carried there, never stepped at those floats' grain; or nobody is near it: FarFromPeople)
            c.SleepTicks = 0;
        }
        if (far)
            c.FrameFarSlept = true;
        else if (c.FrameFarSlept)
        {
            c.FrameFarSlept = false;
            c.Wake(); // (someone came near: it settles from where it was put to sleep, a step mid-slide or mid-fall)
        }
        var dt = c.FrameDt;
        c.FrameDt = 0;
        var frameDv = posed ? f.Vel - c.FrameVc0 : Vector3.Zero; // (a posed frame's change of velocity since the last carry)
        FreeFall(c, f, seed: false);
        var (lastPos, lastRot) = (c.FramePos, c.FrameRot);
        var stepped = dt > 0 && !c.Asleep;
        var com = c.WorldCom;
        if (stepped)
        {
            // Its move through the frame is the physics' own sum of its relative steps (ConstructPhysics.IntegrateFramed,
            // Construct.FrameMove: small numbers), never the difference of two world positions kilometres out, which loses a
            // millimetre a tick to the floats' grain and shook a block pressed to a bulkhead. Something else that moved it
            // (a tool, a push outside the step) shows as the world's pose disagreeing, and that is taken instead.
            var lastComL = lastPos + Vector3.Transform(c.Com, lastRot);
            Vector3 comL;
            if (posed || f.SpaceAway != 0)
            {
                comL = lastComL + Vector3.Transform(c.FrameMove, Quaternion.Conjugate(c.FrameQ0));
                var own = com - c.FrameX0 - c.FrameV * dt; // (the world's word: its move less the hull's motion where it was)
                if (Vector3.DistanceSquared(own, c.FrameMove) > MoveTrust * MoveTrust)
                    comL = lastComL + Vector3.Transform(own, Quaternion.Conjugate(c.FrameQ0));
                var spun = c.FrameW * dt;
                var turned = Quaternion.Normalize(new Quaternion(spun * 0.5f, 1f) * c.FrameQ0); // (the frame's turn in the step, as the solver met it)
                c.FrameRot = Quaternion.Normalize(Quaternion.Conjugate(turned) * c.Rot);
            }
            else
            {
                var iq = Quaternion.Conjugate(f.Rot);
                var r0 = Vector3.Transform(lastComL - f.Com, c.FrameQ0); // (from the frame's centre, in the world's axes, at the last carry)
                comL = Vector3.Transform(r0 + c.FrameMove, iq) + f.Com;
                var world = Vector3.Transform(com - f.Pos, iq);
                if (Vector3.DistanceSquared(world, comL) > MoveTrust * MoveTrust)
                    comL = world;
                c.FrameRot = Quaternion.Normalize(iq * c.Rot);
            }
            c.FramePos = comL - Vector3.Transform(c.Com, c.FrameRot);
        }
        c.Rot = Quaternion.Normalize(f.Rot * c.FrameRot);
        c.Pos = f.Pos + Vector3.Transform(c.FramePos, f.Rot);
        var at = c.WorldCom;
        var fv = f.PointVel(at);
        var fiq = Quaternion.Conjugate(f.Rot);
        if (c.Asleep)
        {
            // Carried with no step: woken when the frame's acceleration at it (the free fall in space aside) would move it.
            if (!far && (SpaceTimeSpeed <= 1 || !posed))
            {
                var a = (fv - c.FrameV - frameDv) * World.TickRate - c.FrameFree; // (the frame's turning at it, and what of its change is felt)
                c.FrameFelt += (a.Length() - c.FrameFelt) * 0.3f;
                if (c.FrameFelt > WakeFelt && ShipGravity.MayWake(f))
                    c.Wake(); // (with ship gravity on, a few a tick: a burn doesn't wake a whole hold at once, ShipGravity.cs)
            }
            c.FrameVel = c.FrameSpin = Vector3.Zero;
        }
        else if (stepped)
        {
            c.FrameFelt = 0; // (awake: what it feels is its motion; the measure starts again when it next sleeps)
            var v = c.Vel + frameDv; // (a posed frame's change carried; what of it is felt comes in the next step, FreeFall)
            c.FrameVel = Vector3.Transform(v - fv, fiq);
            c.FrameSpin = Vector3.Transform(c.AngVel - f.AngVel, fiq);
        }
        c.Vel = fv + Vector3.Transform(c.FrameVel, f.Rot);
        c.AngVel = f.AngVel + Vector3.Transform(c.FrameSpin, f.Rot);
        c.PrevPos = f.PrevPos + Vector3.Transform(lastPos, f.PrevRot);
        c.PrevRot = Quaternion.Normalize(f.PrevRot * lastRot);
        var grounded = c.FrameGround;
        c.FrameGround = false;
        Note(c, f, at);
        Pull(c, f, at, grounded);
        var local = Vector3.Transform(at - f.Pos, fiq);
        if (!ConstructRide.InFrame(f, local, c.FrameUp) || grounded && fv.LengthSquared() > GroundLeaveSpeed * GroundLeaveSpeed)
            Leave(c); // (out through a hatch, off an open deck, or held by the ground under it: the world's from here, at its own speed)
    }
}
