using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The physics gun (board item dev-sandbox-physics-gun-pg01; docs/sandbox/CONSTRUCTS.md, "The physics gun"): its numbers,
/// the spring that pulls a held construct after the aim (run inside the physics step, so what it holds still collides
/// and pushes rather than passing through things), the rotation helpers the client turns a held thing with, and the
/// contested rule every editing tool shares (decision 19). The server's grab, steer, freeze and unfreeze rules are in
/// <see cref="ConstructEdits"/> (below). Nothing here runs for a construct nobody holds.
/// </summary>
public static class ConstructGrab
{
    /// <summary>How far the beam reaches (metres): creative, survival.</summary>
    public const float Range = 30f, SurvivalRange = 12f;

    /// <summary>The nearest a held thing comes (the mouse wheel pulls it in this far at most).</summary>
    public const float MinDist = 1.2f;

    /// <summary>
    /// How hard the beam pulls (newtons) and the heaviest thing it takes (kg): creative, survival. pr02: the spring of
    /// before pr01 again, stronger (was 200 kN and 8 kN): a 15 t truck now gets 100 m/s2 in creative, 500 kg 40 m/s2 in survival.
    /// </summary>
    public const float Strength = 1_500_000f, MaxMass = 15_000f, SurvivalStrength = 20_000f, SurvivalMaxMass = 500f;

    /// <summary>The most the beam accelerates anything (m/s2, on top of holding it up against gravity; was 40): light parts don't snap about.</summary>
    public const float MaxAccel = 120f;

    /// <summary>The spring: the gap closes over about this long (s), at most this fast (m/s); the turn likewise (s, rad/s). Were 0.1 s, 18 m/s, 0.12 s, 9 rad/s.</summary>
    public const float SettleTime = 0.06f, MaxPull = 30f, TurnTime = 0.07f, MaxTurn = 15f;

    /// <summary>The share of the beam's acceleration the spring plans to brake with (the rest is margin for gravity and contacts).</summary>
    public const float BrakeShare = 0.7f;

    /// <summary>Beside another construct the held thing moves at most this far a tick (m) and turns this fast (rad/s): overlap contacts can't be jumped.</summary>
    public const float NearStep = 0.2f, NearTurn = 12f;

    /// <summary>The acceleration the beam can give a construct of this mass.</summary>
    public static float AccelFor(float mass, bool creative) => MathF.Min(MaxAccel, (creative ? Strength : SurvivalStrength) / MathF.Max(mass, 1f));

    /// <summary>How fast the beam can change the held thing's spin (rad/s2): strong enough to beat the floor's and the wheels' twist on a heavy body.</summary>
    public static float SpinFor(float accel) => Math.Clamp(accel * 4f, 20f, 480f);

    /// <summary>
    /// The client's drawn pose of what it holds may lead the server's by this much (m, rad) plus the faster of the server's
    /// motion and the aim's own motion (m/s, rad/s) times <see cref="LeadSeconds"/> (about a round trip).
    /// </summary>
    public const float LeadMin = 0.15f, LeadAngleMin = 0.1f, LeadSeconds = 0.2f;

    /// <summary>Decision 19: an enemy construct is contested while its owner is alive within this many metres (plus its own size).</summary>
    public static float ContestRadius = 40f;

    /// <summary>Two presses of reload within this long (s) with the physics gun out unfreeze everything you froze.</summary>
    public const double DoubleTap = 0.35;

    /// <summary>The rotation snap's step (Shift held while turning).</summary>
    public const float SnapStep = MathF.PI / 4;

    /// <summary>The look's yaw as a turn about up (World.FlatForward(yaw) is this turn of -Z).</summary>
    public static Quaternion YawQ(float yaw) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw);

    /// <summary>
    /// Inside the physics step, after gravity (the spring of before pr01, stronger: pr02): the held construct's velocity is
    /// pushed towards the one that brings its grab point onto the beam's point, and its spin towards the one that brings its
    /// turn onto the wanted turn, by no more than the beam's strength allows this tick (plus gravity, so it hangs in the
    /// air). The contacts are solved after this, so it still stops on what it meets.
    /// </summary>
    public static void Drive(Construct c, float dt, bool nearOther = false)
    {
        var maxPull = nearOther ? MathF.Min(MaxPull, NearStep / dt) : MaxPull; // (another construct close by: a step short enough that the overlap tests can't be jumped)
        var maxTurn = nearOther ? MathF.Min(MaxTurn, NearTurn) : MaxTurn;
        // The turn first: the rotation still to go, as an axis and angle, closed over TurnTime.
        var q = c.GrabRot * Quaternion.Conjugate(c.Rot);
        if (q.W < 0)
            q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
        var s = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z);
        var w = Vector3.Zero;
        if (s > 1e-6f)
        {
            var angle = 2f * MathF.Atan2(s, q.W);
            w = new Vector3(q.X, q.Y, q.Z) / s * MathF.Min(angle / TurnTime, maxTurn);
        }
        var dw = w - c.AngVel;
        var spin = SpinFor(c.GrabAccel) * dt;
        if (dw.LengthSquared() > spin * spin)
            dw *= spin / dw.Length();
        if (float.IsFinite(dw.X + dw.Y + dw.Z))
            c.AngVel += dw;
        // The move: the grab point's wanted velocity closes the gap over SettleTime; the centre takes it, less what the
        // spin already gives the grab point (pr02: a point off the centre is carried, not swung round the centre).
        var at = c.ToWorld(c.GrabLocal);
        var gap = c.GrabTarget - at;
        var want = gap / SettleTime;
        // At most the top speed, and no faster than the beam can brake from within the gap (a stronger beam overshot otherwise).
        var cap = MathF.Min(maxPull, MathF.Sqrt(2f * BrakeShare * c.GrabAccel * gap.Length()));
        if (want.LengthSquared() > cap * cap)
            want *= cap / want.Length();
        if (!float.IsFinite(want.X + want.Y + want.Z))
            return;
        var dv = want - Vector3.Cross(c.AngVel, at - c.WorldCom) - c.Vel;
        var lim = (c.GrabAccel + ConstructPhysics.GravityAccel) * dt;
        if (dv.LengthSquared() > lim * lim)
            dv *= lim / dv.Length();
        c.Vel += dv;
        if (nearOther && c.Vel.LengthSquared() > maxPull * maxPull)
            c.Vel *= maxPull / c.Vel.Length();
    }

    /// <summary>
    /// The holder's own screen (client, every frame, after the poses are blended): the held thing drawn at the pose the
    /// aim asks for, now, instead of where the server's last snapshots put it, but never further from the server's pose
    /// than <see cref="LeadMin"/> plus the speed of the motion in hand times <see cref="LeadSeconds"/> (a wall the server
    /// stopped it at shows as a wall once the aim rests, and a disagreement beyond that blends back to the server).
    /// <paramref name="aimSpeed"/> and <paramref name="aimTurn"/> are how fast the aim's point and turn are moving (m/s, rad/s).
    /// </summary>
    public static void Predict(Construct c, Vector3 eye, Vector3 look, float yaw, float dist, Quaternion rel, float aimSpeed, float aimTurn)
    {
        if (!float.IsFinite(eye.X + look.X + dist + rel.X + rel.W))
            return;
        var wantRot = Quaternion.Normalize(YawQ(yaw) * rel);
        var rot = c.Rot;
        var dot = Quaternion.Dot(rot, wantRot);
        var angle = 2f * MathF.Acos(Math.Clamp(MathF.Abs(dot), 0f, 1f));
        var allowedAngle = LeadAngleMin + MathF.Max(c.AngVel.Length(), aimTurn) * LeadSeconds;
        if (angle > 1e-5f)
            rot = angle <= allowedAngle ? wantRot : Quaternion.Slerp(rot, dot < 0 ? -wantRot : wantRot, allowedAngle / angle);
        var wantPos = eye + look * dist - Vector3.Transform(c.GrabLocal, rot);
        var lead = wantPos - c.Pos;
        var allowed = LeadMin + MathF.Max(c.Vel.Length(), aimSpeed) * LeadSeconds;
        var l2 = lead.LengthSquared();
        if (l2 > allowed * allowed)
            wantPos = c.Pos + lead * (allowed / MathF.Sqrt(l2));
        c.Rot = Quaternion.Normalize(rot);
        c.Pos = wantPos;
    }

    /// <summary>A held thing turned by the mouse, in the look's yaw frame: about up (<paramref name="yaw"/>) and about the look's right (<paramref name="pitch"/>).</summary>
    public static Quaternion Turn(Quaternion rel, float yaw, float pitch) =>
        Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw) * rel);

    /// <summary>The look's right as a direction in the world (the held thing's turn up and down is about the axis nearest it).</summary>
    public static Vector3 RightOf(float yaw) => Vector3.Transform(Vector3.UnitX, YawQ(yaw));

    /// <summary>A world turn as the turn relative to the look's yaw (what the hands hold and the server applies).</summary>
    public static Quaternion RelOf(float yaw, Quaternion world) => Quaternion.Normalize(Quaternion.Conjugate(YawQ(yaw)) * world);

    /// <summary>
    /// pw01: the turn snapped to whole 45 degree steps about the axes of <paramref name="frame"/> (identity: the world's
    /// own axes; another construct's turn: that one's axes), as a world turn.
    /// </summary>
    public static Quaternion SnapIn(Quaternion frame, Quaternion world)
    {
        var local = Quaternion.Conjugate(frame) * world;
        return Quaternion.Normalize(frame * Snap(local));
    }

    /// <summary>
    /// pw01: one snapped turn step of <paramref name="world"/>: <paramref name="yawStep"/> about the frame's up, then
    /// <paramref name="pitchStep"/> about the frame's horizontal axis (X or Z, either way round) nearest the look's right
    /// (<paramref name="viewRight"/>), each re-snapped, so every step is a clean 45 or 90 degrees in the frame's terms.
    /// </summary>
    public static Quaternion TurnSnapped(Quaternion world, Quaternion frame, float yawStep, float pitchStep, Vector3 viewRight)
    {
        var q = world;
        if (yawStep != 0)
            q = SnapIn(frame, Quaternion.CreateFromAxisAngle(Vector3.Transform(Vector3.UnitY, frame), yawStep) * q);
        if (pitchStep != 0)
        {
            var fx = Vector3.Transform(Vector3.UnitX, frame);
            var fz = Vector3.Transform(Vector3.UnitZ, frame);
            var dx = Vector3.Dot(fx, viewRight);
            var dz = Vector3.Dot(fz, viewRight);
            var axis = MathF.Abs(dx) >= MathF.Abs(dz) ? fx * MathF.Sign(dx) : fz * MathF.Sign(dz);
            q = SnapIn(frame, Quaternion.CreateFromAxisAngle(axis, pitchStep) * q);
        }
        return q;
    }

    /// <summary>
    /// pw01: how close another construct must be (m between their bounds) for the snap to use its axes instead of the world's.
    /// The nearest other (not loose) construct to <paramref name="held"/> within that, or null (the world's axes).
    /// </summary>
    public static Construct? SnapReference(ConstructWorld world, Construct held)
    {
        Construct? best = null;
        var bestGap = SnapNear;
        var bodies = world.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var o = bodies[i];
            if (o == held || o.Loose || o.PartCount == 0)
                continue;
            var gap = Vector3.Distance(held.WorldCom, o.WorldCom) - held.Radius - o.Radius;
            if (gap < bestGap)
            {
                bestGap = gap;
                best = o;
            }
        }
        return best;
    }

    public const float SnapNear = 2f;

    private static Quaternion[]? _snaps;

    /// <summary>The nearest turn whose yaw, pitch and roll are all whole 45 degree steps (Shift snaps the turn to these).</summary>
    public static Quaternion Snap(Quaternion q)
    {
        var all = _snaps ??= MakeSnaps();
        q = Quaternion.Normalize(q);
        var best = Quaternion.Identity;
        var bestDot = -1f;
        foreach (var s in all)
        {
            var d = MathF.Abs(Quaternion.Dot(s, q));
            if (d > bestDot + 1e-6f)
            {
                bestDot = d;
                best = s;
            }
        }
        return Quaternion.Dot(best, q) < 0 ? -best : best;
    }

    /// <summary>Is this turn one of the snapped ones (within <paramref name="tolerance"/> of the dot product)?</summary>
    public static bool IsSnapped(Quaternion q, float tolerance = 1e-4f) => MathF.Abs(Quaternion.Dot(Snap(q), Quaternion.Normalize(q))) > 1 - tolerance;

    private static Quaternion[] MakeSnaps()
    {
        var list = new List<Quaternion>(512);
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
        for (var z = 0; z < 8; z++)
        {
            var q = Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(y * SnapStep, x * SnapStep, z * SnapStep));
            var dup = false;
            foreach (var o in list)
                if (MathF.Abs(Quaternion.Dot(o, q)) > 0.99999f)
                {
                    dup = true;
                    break;
                }
            if (!dup)
                list.Add(q);
        }
        return list.ToArray();
    }

    /// <summary>
    /// Decision 19's contested rule, for every editing tool (the physics gun, the wrench, the welder, the linking tool; a
    /// tool still to come calls it too): someone else's construct is refused while its owner (players or bots; factions
    /// don't exist yet, so the owner is the faction) is alive within <see cref="ContestRadius"/> of it or inside it. The
    /// map's constructs (nobody's), wrecks and your own are never contested. <paramref name="aliveAt"/> says where a player
    /// is (null: dead or gone). Null, or why not ("Owned by Name - defended"). Survival only: creative calls nothing.
    /// </summary>
    public static string? Defended(Construct c, byte player, Func<byte, Vector3?>? aliveAt, Func<byte, string>? nameOf)
    {
        if (c.Owner == 255 || c.Owner == player || c.Wreck || aliveAt == null || aliveAt(c.Owner) is not { } at)
            return null;
        var reach = ContestRadius + c.Radius;
        if (Vector3.DistanceSquared(at, c.WorldCom) > reach * reach)
            return null;
        return $"Owned by {nameOf?.Invoke(c.Owner) ?? $"player {c.Owner}"} - defended";
    }
}

/// <summary>The physics gun on the server (ConstructGrab.cs): grab, steer, let go, freeze, unfreeze; and the contested rule.</summary>
public sealed partial class ConstructEdits
{
    /// <summary>Where a player is (null: dead or gone), for the contested rule (the server sets it; the client's own look text too).</summary>
    public Func<byte, Vector3?>? AliveAt;

    /// <summary>A player's name, for "Owned by Name - defended".</summary>
    public Func<byte, string>? NameOf;

    /// <summary>Decision 19 (survival only; creative is unrestricted): why this player may not edit or grab this construct now, or null.</summary>
    public string? Contested(Construct c, byte player) => InfiniteParts ? null : ConstructGrab.Defended(c, player, AliveAt, NameOf);

    public float GrabRange => InfiniteParts ? ConstructGrab.Range : ConstructGrab.SurvivalRange;

    /// <summary>
    /// Why the physics gun can't take hold of this construct (or null): not players (a seat in use), not something anchored
    /// to the ground (a foundation, a pinned construct), not one someone else holds, not too heavy (survival's cap is much
    /// lower), not a contested enemy construct in survival. A frozen one can be taken (it unfreezes).
    /// </summary>
    public string? GrabRefusal(Construct c, byte player)
    {
        if (c.GrabbedBy != 255 && c.GrabbedBy != player)
            return "someone else is holding it";
        if (c.Driver != 255)
            return "someone is in it";
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].SitterPlus1 != 0)
                return "someone is in it";
        if (c.Anchored && !c.Frozen)
            return "it is anchored to the ground";
        var cap = InfiniteParts ? ConstructGrab.MaxMass : ConstructGrab.SurvivalMaxMass;
        if (c.Mass > cap)
            return $"too heavy for the physics gun ({c.Mass:0} kg; {cap:0} kg at most{(InfiniteParts ? "" : " in survival")})";
        return Contested(c, player);
    }

    /// <summary>
    /// The physics gun takes hold of construct <paramref name="body"/> at <paramref name="local"/> (its body frame, where
    /// the beam met it), keeping that point's distance and the thing's turn relative to the look. Null, or why not.
    /// </summary>
    public string? Grab(ConstructWorld world, byte player, Vector3 eye, float yaw, ushort body, Vector3 local)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.PhysicsGun)
            return "take out the physics gun to grab things";
        if (world.Get(body) is not { } c || c.PartCount == 0)
            return "that is gone";
        if (!float.IsFinite(local.X + local.Y + local.Z) || Vector3.Clamp(local, c.LocalMin - new Vector3(0.3f), c.LocalMax + new Vector3(0.3f)) != local)
            return "the beam missed it";
        if (GrabRefusal(c, player) is { } why)
            return why;
        var at = c.ToWorld(local);
        var dist = Vector3.Distance(eye, at);
        if (dist > GrabRange + 1f)
            return "too far for the beam";
        if (h.GrabBody != 0 && h.GrabBody != body)
            Release(world, player);
        if (c.Frozen)
            Unfreeze(world, c);
        if (c.CarriedBy != 0)
            ConstructCargo.Release(world, c); // (taken out of a frozen cargo volume: it isn't caught again while held)
        c.GrabbedBy = player;
        c.GrabLocal = local;
        c.GrabTarget = at;
        c.GrabRot = c.Rot;
        c.GrabAccel = ConstructGrab.AccelFor(c.Mass, InfiniteParts);
        c.Wake();
        h.GrabBody = body;
        h.GrabLocal = local;
        h.GrabDist = Math.Clamp(dist, ConstructGrab.MinDist, GrabRange);
        h.GrabRel = Quaternion.Normalize(Quaternion.Conjugate(ConstructGrab.YawQ(yaw)) * c.Rot);
        h.Edit = EditKind.None;
        Done.Add($"player {player}: grabbed construct {body} with the physics gun");
        return null;
    }

    /// <summary>The client's wheel and turn: the held thing's distance and its turn relative to the look's yaw.</summary>
    public void SteerGrab(byte player, float dist, Quaternion rel)
    {
        ref var h = ref Hands[player];
        if (h.GrabBody == 0 || !float.IsFinite(dist + rel.X + rel.Y + rel.Z + rel.W) || rel.LengthSquared() < 0.5f)
            return;
        h.GrabDist = Math.Clamp(dist, ConstructGrab.MinDist, GrabRange);
        h.GrabRel = Quaternion.Normalize(rel);
    }

    /// <summary>Lets go (it keeps the speed it had: a flick throws it, as before pr01).</summary>
    public void Release(ConstructWorld world, byte player)
    {
        ref var h = ref Hands[player];
        if (h.GrabBody == 0)
            return;
        if (world.Get(h.GrabBody) is { } c && c.GrabbedBy == player)
            LetGo(c);
        h.GrabBody = 0;
    }

    /// <summary>Let go: it keeps the speed it had (pr02: as before pr01; a flick throws it).</summary>
    private static void LetGo(Construct c)
    {
        c.GrabbedBy = 255;
        c.Wake();
    }

    /// <summary>Every tick for every living player: the held thing follows their look (or is let go when it can't be held any more).</summary>
    public void GrabTick(ConstructWorld world, byte player, Vector3 eye, Vector3 look, float yaw)
    {
        ref var h = ref Hands[player];
        if (h.GrabBody == 0)
            return;
        var c = world.Get(h.GrabBody);
        if (c == null || c.GrabbedBy != player || h.Tool != ConstructTool.PhysicsGun || h.Seated || c.Driver != 255 || c.Anchored)
        {
            if (c != null && c.GrabbedBy == player)
                LetGo(c);
            h.GrabBody = 0;
            return;
        }
        c.GrabTarget = eye + look * h.GrabDist;
        c.GrabRot = ConstructGrab.YawQ(yaw) * h.GrabRel;
        c.GrabAccel = ConstructGrab.AccelFor(c.Mass, InfiniteParts);
    }

    /// <summary>Aim while holding: the held thing freezes where it is (held still like an anchored construct, tinted) and is let go.</summary>
    public string? Freeze(ConstructWorld world, byte player)
    {
        ref var h = ref Hands[player];
        if (h.GrabBody == 0 || world.Get(h.GrabBody) is not { } c || c.GrabbedBy != player)
            return "hold something with the physics gun to freeze it";
        Release(world, player);
        c.Frozen = true;
        c.FrozenBy = player;
        c.Anchored = true;
        c.Vel = c.AngVel = Vector3.Zero;
        world.StructureDirty.Add(c.Id); // (its frozen flag and pose go out with its structure)
        Done.Add($"player {player}: froze construct {c.Id}");
        return null;
    }

    /// <summary>A frozen construct moves again (grabbed, or unfreeze-all).</summary>
    public static void Unfreeze(ConstructWorld world, Construct c)
    {
        if (!c.Frozen)
            return;
        c.Frozen = false;
        c.FrozenBy = 255;
        c.Anchored = c.Pinned;
        c.Dirty = true; // (a foundation anchors it again when it rebuilds)
        c.Wake();
        world.StructureDirty.Add(c.Id);
    }

    /// <summary>Double-tap reload with the physics gun out: everything this player froze moves again (<paramref name="everyone"/>: the host's option, everything). Returns how many.</summary>
    public int UnfreezeAll(ConstructWorld world, byte player, bool everyone)
    {
        var n = 0;
        foreach (var c in world.Bodies)
            if (c.Frozen && (everyone || c.FrozenBy == player))
            {
                Unfreeze(world, c);
                n++;
            }
        if (n > 0)
            Done.Add($"player {player}: unfroze {n} construct{(n == 1 ? "" : "s")}{(everyone ? " (everyone's)" : "")}");
        return n;
    }

    /// <summary>A player leaves: what they hold is let go, and what they froze is nobody's (the host's unfreeze-all still reaches it).</summary>
    public void GrabLeave(ConstructWorld world, byte player)
    {
        Release(world, player);
        foreach (var c in world.Bodies)
            if (c.FrozenBy == player)
                c.FrozenBy = 255;
    }
}
