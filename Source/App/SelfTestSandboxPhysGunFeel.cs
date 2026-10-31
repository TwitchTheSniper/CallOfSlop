using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Sandbox physics gun feel" (board items dev-physgun-responsive-pr01, dev-physgun-world-snap-pw01 and pr02): the held
    /// piece follows the aim on the stronger spring with no overshoot (a 15 t truck within 0.6 s; survival slower when
    /// heavy), grabbed off centre (a big vehicle, a small part, a buggy and its trailer on the hitch, dragged along the
    /// floor or sunk into it) it follows and keeps its turn unless the turn control asks, a flick throws it, a piece driven
    /// into the ground or another construct stops there, the
    /// holder's predicted pose matches the server's within 3 cm at 100 ms lag; Shift snaps the WORLD turn to 45 degree
    /// steps about the world's axes (or a nearby construct's), the view turning doesn't change it, F + mouse steps are 45
    /// degrees about world up; and over a real host and client the same snapped turn reaches both.
    /// </summary>
    private static void CheckSandboxPhysGunFeel()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"physics gun feel: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        FeelFollow();
        FeelOffCentre();
        FeelWallAndRelease();
        FeelPrediction();
        FeelSnapMaths();
        FeelSnapLoopback(yard);
    }

    /// <summary>One run: a piece of this mass held, the aim moved by (0, 2.2, 2) m and stopped.</summary>
    private static (float TimeToReach, float Overshoot, float Residual, float Final) FeelRun(float mass, bool creative, int ticks, Func<Construct, float>? probe = null)
    {
        var w = new ConstructWorld();
        var hands = new ConstructEdits { InfiniteParts = creative };
        hands.Hand(0).Tool = ConstructTool.PhysicsGun;
        var spot = OpenSpot(w)!.Value + new Vector3(0, 3f, 0);
        var a = ConstructBuild.PlaceNew(w, ConstructParts.Need("Frame Block"), spot, 0)!;
        a.Anchored = false;
        a.Mass = mass;
        var dt = World.TickDt;
        var eye = a.WorldCom + new Vector3(-4f, 0.5f, 0);
        var (_, yaw0) = Aim(eye, a.WorldCom);
        hands.Grab(w, 0, eye, yaw0, a.Id, a.Com);
        var dist = hands.Hand(0).GrabDist;
        var goal = eye + Vector3.Normalize(a.WorldCom + new Vector3(0, 2.2f, 2f) - eye) * dist;
        var (dir, yaw) = Aim(eye, goal);
        var start = a.ToWorld(a.Com);
        var move = Vector3.Normalize(goal - start);
        float reach = -1, over = 0;
        for (var t = 1; t <= ticks; t++)
        {
            hands.GrabTick(w, 0, eye, dir, yaw);
            w.Step(World.Query, dt);
            var off = goal - a.ToWorld(a.Com);
            if (reach < 0 && off.Length() < 0.02f)
                reach = t * dt;
            over = MathF.Max(over, -Vector3.Dot(off, move));
        }
        // (the grab point is the centre of mass here, but the beam's turn is held too)
        return (reach, over, a.Vel.Length(), probe?.Invoke(a) ?? 0);
    }

    /// <summary>
    /// pr02 ("sometimes the physics gun rotates the piece instead of moving it"): a held thing grabbed far off its centre,
    /// with wheels on the ground or a trailer on its hitch, follows the aim and keeps the turn it was grabbed with (relative
    /// to the look's yaw) unless the turn control asks for another. One run: grab <paramref name="c"/> at
    /// <paramref name="local"/>, sweep the aim left, right and up for about a second (the grab point's target moves in
    /// steps of a few cm a tick), and measure the worst gap from the grab point to the beam's point and the worst turn off
    /// the wanted one, once the first 10 ticks have passed (the trailer settling on its ball).
    /// </summary>
    private static (float Gap, float Turn, float Moved) FeelSweep(ConstructWorld w, ConstructEdits hands, Construct c, Vector3 local, float lift = 0.3f)
    {
        var dt = World.TickDt;
        var at = c.ToWorld(local);
        var flat = Vector3.Normalize(new Vector3(at.X - c.WorldCom.X, 0, at.Z - c.WorldCom.Z) + new Vector3(0.001f, 0, 0));
        var eye = at + flat * 5f + new Vector3(0, 1.0f, 0); // (standing 5 m off the side the grab point is on)
        var (_, yaw0) = Aim(eye, at);
        if (hands.Grab(w, 0, eye, yaw0, c.Id, local) is { } why)
        {
            Fail($"physics gun feel: the grab of construct {c.Id} was refused: {why}");
            return (float.NaN, float.NaN, 0);
        }
        var dist = hands.Hand(0).GrabDist;
        var start = c.WorldCom;
        float gap = 0, turn = 0;
        const int ticks = 66;
        for (var t = 1; t <= ticks; t++)
        {
            // Left 0.35 rad, then right 0.35 rad, while pitching up 0.3 rad over the second.
            var s = t / (float)ticks;
            var dyaw = s < 0.5f ? -0.7f * s : -0.35f + 1.4f * (s - 0.5f);
            var look0 = Vector3.Normalize(at - eye);
            var pitch0 = MathF.Asin(Math.Clamp(look0.Y, -1, 1));
            var yaw = yaw0 + dyaw;
            var pitch = pitch0 + lift * s;
            var dir = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), -MathF.Cos(yaw) * MathF.Cos(pitch));
            hands.GrabTick(w, 0, eye, dir, yaw);
            w.Step(World.Query, dt);
            if (t <= 10)
                continue;
            gap = MathF.Max(gap, Vector3.Distance(c.ToWorld(local), eye + dir * dist));
            turn = MathF.Max(turn, AngleBetween(c.Rot, ConstructGrab.YawQ(yaw) * hands.Hand(0).GrabRel));
        }
        return (gap, turn, Vector3.Distance(start, c.WorldCom));
    }

    private static void FeelOffCentre()
    {
        var dt = World.TickDt;
        // The big vehicle on its wheels, grabbed at the far end of its side; a small part at its corner; the buggy and its
        // trailer coupled on the tow ball, grabbed at the buggy's nose and at the trailer's back corner.
        var w = new ConstructWorld();
        var hands = new ConstructEdits { InfiniteParts = true };
        hands.Hand(0).Tool = ConstructTool.PhysicsGun;
        var truckSpot = OpenSpot(w)!.Value;
        if (BuildBlueprint(w, "4x4 Truck", truckSpot.X, truckSpot.Z, MathF.PI / 2) is not { } truck)
            return;
        StepConstructs(w, 40);
        var truckAt = new Vector3(truck.LocalMax.X * 0.9f, truck.Com.Y, truck.LocalMax.Z * 0.9f);
        var (tg, tt, tm) = FeelSweep(w, hands, truck, truckAt);
        hands.Release(w, 0);

        var w2 = new ConstructWorld();
        var spot = OpenSpot(w2)!.Value;
        var block = ConstructBuild.PlaceNew(w2, ConstructParts.Need("Frame Block"), spot, 0)!;
        StepConstructs(w2, 30);
        var hands2 = new ConstructEdits { InfiniteParts = true };
        hands2.Hand(0).Tool = ConstructTool.PhysicsGun;
        var (bg, bt, bm) = FeelSweep(w2, hands2, block, block.LocalMax * 0.9f);

        var w3 = new ConstructWorld();
        if (Hitched(w3, -24, 0) is not { } pair)
            return;
        StepConstructs(w3, 40);
        var hands3 = new ConstructEdits { InfiniteParts = true };
        hands3.Hand(0).Tool = ConstructTool.PhysicsGun;
        var car = pair.Car;
        var (cg, ct, cm) = FeelSweep(w3, hands3, car, new Vector3(car.LocalMax.X * 0.9f, car.Com.Y, car.LocalMax.Z * 0.9f));
        var hitchCar = HitchGap(w3);
        hands3.Release(w3, 0);
        StepConstructs(w3, 40);
        var tr = pair.Trailer;
        var (rg, rt, rm) = FeelSweep(w3, hands3, tr, new Vector3(tr.LocalMin.X * 0.9f, tr.Com.Y, tr.LocalMin.Z * 0.9f));
        var hitchTr = HitchGap(w3);
        Line($"physics gun feel: grabbed off centre and swept left, right and up for 1.1 s: truck ({truck.Mass:0} kg, {truck.PartCount} parts) moved {tm:0.00} m, grab point within {tg * 100:0.0} cm, turn off {tt:0.0} deg; small part {bm:0.00} m, {bg * 100:0.0} cm, {bt:0.0} deg; buggy with trailer {cm:0.00} m, {cg * 100:0.0} cm, {ct:0.0} deg (hitch gap {hitchCar * 100:0.0} cm); the trailer itself {rm:0.00} m, {rg * 100:0.0} cm, {rt:0.0} deg (hitch gap {hitchTr * 100:0.0} cm)");
        // (the spring trails a moving aim by its speed times SettleTime: up to about half a metre in this sweep)
        if (tm < 0.5f || tg > 0.75f || tt > 10f)
            Fail("physics gun feel: a big vehicle grabbed off centre turned instead of following the aim (pr02)");
        if (bm < 0.5f || bg > 0.5f || bt > 10f)
            Fail("physics gun feel: a small part grabbed at its corner turned instead of following the aim (pr02)");
        if (cm < 0.5f || cg > 0.75f || ct > 10f || rm < 0.5f || rg > 0.75f || rt > 10f)
            Fail("physics gun feel: a vehicle or its trailer, coupled on the tow ball, turned about the hitch instead of following the aim (pr02)");
        if (!(hitchCar < 0.15f) || !(hitchTr < 0.15f))
            Fail("physics gun feel: carrying one of a coupled pair pulled the hitch apart (the other should follow on the ball)");

        // On the floor: the buggy and trailer dragged along it (the aim swept sideways at the grab point's height, never lifting), and a
        // part sunk 4 cm into the floor dragged the same way; creative and survival (a share of the gap a tick, so a
        // contact's torque would have time to tip it).
        var dragLine = "";
        foreach (var creative in new[] { true, false })
        {
            var w4 = new ConstructWorld();
            if (Hitched(w4, -24, 8) is not { } pair4)
                return;
            StepConstructs(w4, 40);
            var hands4 = new ConstructEdits { InfiniteParts = creative };
            hands4.Hand(0).Tool = ConstructTool.PhysicsGun;
            var c4 = creative ? pair4.Car : pair4.Trailer; // (the buggy is over survival's 500 kg)
            var (dg, dtn, dm) = FeelSweep(w4, hands4, c4, new Vector3(c4.LocalMax.X * 0.9f, c4.LocalMin.Y + 0.1f, c4.LocalMax.Z * 0.9f), 0f);
            var w5 = new ConstructWorld();
            var s5 = OpenSpot(w5)!.Value;
            var sunk = ConstructBuild.PlaceNew(w5, ConstructParts.Need("Frame Block"), s5, 0)!;
            StepConstructs(w5, 30);
            sunk.Pos -= new Vector3(0, 0.04f, 0);
            sunk.PrevPos = sunk.Pos;
            var hands5 = new ConstructEdits { InfiniteParts = creative };
            hands5.Hand(0).Tool = ConstructTool.PhysicsGun;
            var (sg, stn, sm) = FeelSweep(w5, hands5, sunk, new Vector3(sunk.LocalMax.X * 0.9f, sunk.LocalMin.Y + 0.05f, sunk.LocalMax.Z * 0.9f), 0f);
            dragLine += $"{(creative ? "creative: buggy with trailer" : "; survival: trailer on the buggy")} moved {dm:0.00} m, {dg * 100:0.0} cm, {dtn:0.0} deg; part sunk 4 cm {sm:0.00} m, {sg * 100:0.0} cm, {stn:0.0} deg";
            // (survival follows at a share of the gap: the bound on the grab point is looser there, the turn's is the same)
            var gapCap = creative ? 0.75f : 0.9f;
            if (dm < 0.5f || dg > gapCap || dtn > 10f || sm < 0.5f || sg > gapCap || stn > 10f)
                Fail($"physics gun feel: dragged along the floor ({(creative ? "creative" : "survival")}), a vehicle with its trailer or a part sunk into the floor turned instead of following the aim (pr02)");
        }
        Line($"physics gun feel: dragged along the floor off centre, {dragLine}");

        // With the turn control: the same grab does turn, by the asked amount.
        var rel0 = hands2.Hand(0).GrabRel;
        var eye2 = block.WorldCom + new Vector3(-4, 1, 0);
        var (d2, y2) = Aim(eye2, block.WorldCom);
        hands2.SteerGrab(0, Vector3.Distance(eye2, block.ToWorld(hands2.Hand(0).GrabLocal)), ConstructGrab.Turn(rel0, 0.8f, 0));
        var before = block.Rot;
        var turned = 0f;
        for (var t = 0; t < 120 && turned < 40f; t++)
        {
            hands2.GrabTick(w2, 0, eye2, d2, y2);
            w2.Step(World.Query, dt);
            turned = AngleBetween(before, block.Rot);
        }
        // Let go while the turn control is still held, grab again at once: the new grab keeps the piece's turn as it is.
        hands2.Release(w2, 0);
        var atLetGo = block.Rot;
        hands2.Grab(w2, 0, eye2, y2, block.Id, block.ToLocal(block.WorldCom + new Vector3(0.2f, 0.1f, 0)));
        var regrabTurn = 0f;
        for (var t = 0; t < 40; t++)
        {
            hands2.GrabTick(w2, 0, eye2, d2, y2);
            w2.Step(World.Query, dt);
            regrabTurn = MathF.Max(regrabTurn, AngleBetween(atLetGo, block.Rot));
        }
        Line($"physics gun feel: with the turn control it turned {turned:0.0} deg (asked 45.8); grabbed again right after letting go mid-turn it turned {regrabTurn:0.0} deg");
        if (turned < 40f || regrabTurn > 5f)
            Fail("physics gun feel: the turn control didn't turn the held piece, or a fresh grab after a turn kept turning it (pr02)");
    }

    private static void FeelFollow()
    {
        var (t1, o1, r1, _) = FeelRun(1000, true, 60);
        var (t15, o15, r15, _) = FeelRun(15000, true, 60);
        var (tl, ol, rl, _) = FeelRun(100, false, 240);
        var (th, oh, rh, _) = FeelRun(450, false, 240);
        Line($"physics gun feel: creative 1 t piece reached the aim 3 m off in {t1 * 1000:0} ms (overshoot {o1 * 100:0.00} cm, residual {r1:0.000} m/s); 15 t truck {t15 * 1000:0} ms ({o15 * 100:0.00} cm, {r15:0.000}); survival 100 kg {tl * 1000:0} ms ({ol * 100:0.00} cm, {rl:0.000}), 450 kg {th * 1000:0} ms ({oh * 100:0.00} cm, {rh:0.000})");
        // pr02: the spring of before pr01, stronger. Within 2 cm of an aim 3 m off: a 1 t piece in under 0.5 s, and the strength
        // check, a 15 t truck in under 0.6 s (the old 200 kN beam gave it 13 m/s2: a second or more for 3 m); the spring brakes
        // within what the beam can, so neither overshoots by more than 3 cm.
        if (t1 < 0 || t1 > 0.5f || o1 > 0.03f || r1 > 0.05f)
            Fail("physics gun feel: a 1 t piece (creative) didn't reach the aim within 0.5 s with under 3 cm overshoot and no residual speed");
        if (t15 < 0 || t15 > 0.6f || o15 > 0.03f || r15 > 0.05f)
            Fail("physics gun feel: a 15 t truck (creative) wasn't lifted onto the aim within 0.6 s (the beam's strength) or overshot");
        if (tl < 0 || th < 0 || th <= tl || ol > 0.03f || oh > 0.03f || rl > 0.05f || rh > 0.05f)
            Fail("physics gun feel: in survival a heavier piece should be slower but still stop with under 3 cm overshoot and no drift");
    }

    private static void FeelWallAndRelease()
    {
        var w = new ConstructWorld();
        var hands = new ConstructEdits();
        hands.Hand(0).Tool = ConstructTool.PhysicsGun;
        var dt = World.TickDt;
        var spot = OpenSpot(w)!.Value;
        var block = ConstructParts.Need("Frame Block");
        var a = ConstructBuild.PlaceNew(w, block, spot, 0)!;
        for (var t = 0; t < 30; t++)
            w.Step(World.Query, dt);
        var rest = a.WorldCom;
        var eye = rest + new Vector3(-4f, 1f, 0);
        var (_, yaw0) = Aim(eye, rest);
        hands.Grab(w, 0, eye, yaw0, a.Id, a.Com);
        var dist = hands.Hand(0).GrabDist;
        // Aim 3 m down into the ground: the piece stops on it (and stays, not sinking).
        var into = eye + Vector3.Normalize(rest + new Vector3(0, -3f, 0) - eye) * dist;
        var (dir, yaw) = Aim(eye, into);
        var low = rest.Y;
        for (var t = 0; t < 90; t++)
        {
            hands.GrabTick(w, 0, eye, dir, yaw);
            w.Step(World.Query, dt);
            low = MathF.Min(low, a.WorldCom.Y);
        }
        var sank = rest.Y - low;
        // Let go: pressed into the ground it stays where it is, no drift sideways.
        hands.Release(w, 0);
        var stopped = a.Vel.Length() < 0.05f && a.AngVel.Length() < 0.05f;
        var at = a.WorldCom;
        for (var t = 0; t < 30; t++)
            w.Step(World.Query, dt);
        var drift = MathF.Sqrt(MathF.Pow(a.WorldCom.X - at.X, 2) + MathF.Pow(a.WorldCom.Z - at.Z, 2));
        // Held in the air, aim swept sideways and let go mid-motion: it keeps its speed (a flick throws it, as before pr01).
        hands.Grab(w, 0, eye, yaw0, a.Id, a.ToLocal(a.WorldCom));
        var goal = eye + Vector3.Normalize(rest + new Vector3(0, 3f, 4f) - eye) * dist;
        var (d2, y2) = Aim(eye, goal);
        for (var t = 0; t < 4; t++)
        {
            hands.GrabTick(w, 0, eye, d2, y2);
            w.Step(World.Query, dt);
        }
        var moving = a.Vel.Length();
        hands.Release(w, 0);
        var flung = a.Vel.Length();
        // Into another construct: an anchored block in the way.
        var w2 = new ConstructWorld();
        var hands2 = new ConstructEdits();
        hands2.Hand(0).Tool = ConstructTool.PhysicsGun;
        var s2 = OpenSpot(w2)!.Value;
        var p = ConstructBuild.PlaceNew(w2, block, s2, 0)!;
        var wall = ConstructBuild.PlaceNew(w2, block, s2 + new Vector3(3f, 0, 0), 0)!;
        for (var t = 0; t < 30; t++)
            w2.Step(World.Query, dt);
        wall.Anchored = true;
        var e2 = p.WorldCom + new Vector3(-4f, 0, 0);
        var (_, yy) = Aim(e2, p.WorldCom);
        hands2.Grab(w2, 0, e2, yy, p.Id, p.Com);
        var far = e2 + Vector3.UnitX * (hands2.Hand(0).GrabDist + 6f);
        hands2.SteerGrab(0, hands2.Hand(0).GrabDist + 6f, hands2.Hand(0).GrabRel);
        for (var t = 0; t < 90; t++)
        {
            hands2.GrabTick(w2, 0, e2, Vector3.UnitX, yy);
            w2.Step(World.Query, dt);
        }
        _ = far;
        var through = p.WorldCom.X > wall.WorldCom.X - 0.2f;
        Line($"physics gun feel: aimed 3 m into the ground it sank {sank * 100:0.0} cm; let go it stopped {stopped} and drifted {drift * 100:0.0} cm in 0.5 s; released at {moving:0.0} m/s it kept {flung:0.00} (thrown); pushed at a wall it ended {wall.WorldCom.X - p.WorldCom.X:0.00} m short of the wall's centre (through: {through})");
        if (sank > 0.1f || !stopped || drift > 0.01f || moving < 1f || flung < moving * 0.9f || through)
            Fail("physics gun feel: a held piece went through the ground or another construct, letting go drifted, or a flick didn't throw it");
    }

    /// <summary>The holder's predicted pose against the server's, with the poses the holder hears 100 ms old.</summary>
    private static void FeelPrediction()
    {
        var w = new ConstructWorld();
        var cw = new ConstructWorld();
        var hands = new ConstructEdits();
        hands.Hand(0).Tool = ConstructTool.PhysicsGun;
        var dt = World.TickDt;
        var spot = OpenSpot(w)!.Value + new Vector3(0, 3f, 0);
        var block = ConstructParts.Need("Frame Block");
        var a = ConstructBuild.PlaceNew(w, block, spot, 0)!;
        a.Anchored = false;
        var cl = ConstructBuild.PlaceNew(cw, block, spot, 0)!;
        var eye = a.WorldCom + new Vector3(-4f, 0.5f, 0);
        var (_, yaw0) = Aim(eye, a.WorldCom);
        hands.Grab(w, 0, eye, yaw0, a.Id, a.Com);
        var dist = hands.Hand(0).GrabDist;
        var rel = hands.Hand(0).GrabRel;
        var goal = eye + Vector3.Normalize(a.WorldCom + new Vector3(0, 2.2f, 2f) - eye) * dist;
        var from = a.WorldCom;
        const int lag = 6; // 100 ms
        var ring = new (Vector3 Pos, Quaternion Rot, Vector3 Vel, Vector3 Ang)[lag + 1];
        for (var i = 0; i < ring.Length; i++)
            ring[i] = (a.Pos, a.Rot, a.Vel, a.AngVel);
        float aheadWorst = 0, midErr = 0, endErr = 0, endAng = 0;
        var prevTarget = a.WorldCom;
        for (var t = 1; t <= 60; t++)
        {
            // The aim glides to the goal over 9 ticks (20 m/s), then rests.
            var f = MathF.Min(t / 9f, 1f);
            var at = Vector3.Lerp(from, goal, f);
            var (dir, yaw) = Aim(eye, at);
            hands.GrabTick(w, 0, eye, dir, yaw);
            w.Step(World.Query, dt);
            ring[t % ring.Length] = (a.Pos, a.Rot, a.Vel, a.AngVel);
            var old = ring[(t + 1) % ring.Length]; // (lag ticks ago)
            cl.Pos = old.Pos;
            cl.Rot = old.Rot;
            cl.Vel = old.Vel;
            cl.AngVel = old.Ang;
            var speed = Vector3.Distance(at, prevTarget) / dt;
            prevTarget = at;
            ConstructGrab.Predict(cl, eye, dir, yaw, dist, rel, speed, 0);
            var predicted = cl.ToWorld(cl.Com);
            if (t == 6)
                midErr = Vector3.Distance(predicted, eye + dir * dist);
            aheadWorst = MathF.Max(aheadWorst, Vector3.Distance(old.Pos, a.Pos) - Vector3.Distance(cl.Pos, a.Pos));
            if (t == 60)
            {
                endErr = Vector3.Distance(cl.Pos, a.Pos);
                endAng = 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(cl.Rot, a.Rot)), 0f, 1f));
            }
        }
        // Blocked: the server's piece rests on the ground while the aim is far below; the prediction stays within its lead.
        var cw2 = new ConstructWorld();
        var cl2 = ConstructBuild.PlaceNew(cw2, block, spot, 0)!;
        cl2.Pos = a.Pos;
        cl2.Rot = a.Rot;
        var deep = a.WorldCom + new Vector3(0, -5f, 0);
        var (dd, dy) = Aim(eye, eye + Vector3.Normalize(deep - eye) * dist);
        ConstructGrab.Predict(cl2, eye, dd, dy, dist, rel, 0, 0);
        var lead = Vector3.Distance(cl2.Pos, a.Pos);
        Line($"physics gun feel: predicted pose at 100 ms lag: {midErr * 100:0.0} cm from the aim 0.1 s in (moving at 20 m/s), {endErr * 100:0.00} cm / {endAng * 57.3f:0.00} deg from the server's pose once it rests; a blocked piece drawn {lead * 100:0.0} cm past the server's (limit {ConstructGrab.LeadMin * 100:0})");
        if (midErr > 0.05f || endErr > 0.03f || endAng > 0.02f || lead > ConstructGrab.LeadMin + 0.01f)
            Fail("physics gun feel: the holder's predicted pose is late, or doesn't match the server's within 3 cm, or ignores a server that stopped the piece");
    }

    /// <summary>How far (degrees) a turn is from the nearest whole-45-degree one.</summary>
    private static float SnapOff(Quaternion q) => AngleBetween(q, ConstructGrab.Snap(q));

    private static float AngleBetween(Quaternion a, Quaternion b) => 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))), 0f, 1f)) * 57.29578f;

    private static void FeelSnapMaths()
    {
        var yaw = 37f * MathF.PI / 180f;
        // A piece turned any old way, the player looking at yaw 37: snapped in the WORLD it ends aligned to the world's axes.
        var rel = ConstructGrab.Turn(Quaternion.Identity, 0.31f, 0.52f);
        var worldBefore = ConstructGrab.YawQ(yaw) * rel;
        var snapped = ConstructGrab.SnapIn(Quaternion.Identity, worldBefore);
        var off = SnapOff(snapped);
        var rotated = ConstructGrab.IsSnapped(snapped);
        // The old, view-relative snap would not have been aligned (the view is 37 degrees off).
        var oldWorld = ConstructGrab.YawQ(yaw) * ConstructGrab.Snap(rel);
        var oldOff = SnapOff(oldWorld);
        // The view turns while Shift is held: the world turn is stored back as the relative turn, so nothing changes.
        var held = snapped;
        float worstDrift = 0;
        for (var k = 1; k <= 20; k++)
        {
            var y2 = yaw + k * 0.17f;
            var rel2 = ConstructGrab.RelOf(y2, held);
            var world2 = ConstructGrab.YawQ(y2) * rel2;
            worstDrift = MathF.Max(worstDrift, AngleBetween(world2, held));
        }
        // One notch of F + mouse: exactly 45 degrees about world up (and the pitch notch about the world axis nearest the right).
        var right = ConstructGrab.RightOf(yaw);
        var step = ConstructGrab.TurnSnapped(snapped, Quaternion.Identity, ConstructGrab.SnapStep, 0, right);
        var about = Quaternion.Normalize(step * Quaternion.Conjugate(snapped));
        var yawAngle = AngleBetween(step, snapped);
        var upDot = MathF.Abs(Vector3.Dot(new Vector3(about.X, about.Y, about.Z) / MathF.Max(1e-6f, new Vector3(about.X, about.Y, about.Z).Length()), Vector3.UnitY));
        var square = Quaternion.Identity; // (a piece squared to the world: a pitch notch is then exactly 45 degrees about a world horizontal axis)
        var pitch = ConstructGrab.TurnSnapped(square, Quaternion.Identity, 0, ConstructGrab.SnapStep, right);
        var pAbout = Quaternion.Normalize(pitch * Quaternion.Conjugate(square));
        var pAxis = new Vector3(pAbout.X, pAbout.Y, pAbout.Z);
        pAxis /= MathF.Max(1e-6f, pAxis.Length());
        var pitchAngle = AngleBetween(pitch, square);
        var pitchWorldAxis = MathF.Max(MathF.Abs(pAxis.X), MathF.Abs(pAxis.Z)) > 0.9999f;
        // Near a construct turned 20 degrees: its axes, not the world's.
        var w = new ConstructWorld();
        var spot = OpenSpot(w)!.Value;
        var block = ConstructParts.Need("Frame Block");
        var other = ConstructBuild.PlaceNew(w, block, spot, 0)!;
        other.Rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 20f * MathF.PI / 180f);
        var mine = ConstructBuild.PlaceNew(w, block, spot + new Vector3(3f, 0, 0), 0)!;
        var far = ConstructBuild.PlaceNew(w, block, spot + new Vector3(40f, 0, 0), 0)!;
        other.Rebuild();
        mine.Rebuild();
        far.Rebuild();
        other.Anchored = true; // (a placed building, not a loose part; after the rebuild, which sets it from the foundations)
        var near = ConstructGrab.SnapReference(w, mine);
        var farRef = ConstructGrab.SnapReference(w, far);
        var inFrame = ConstructGrab.SnapIn(other.Rot, worldBefore);
        var rel20 = Quaternion.Normalize(Quaternion.Conjugate(other.Rot) * inFrame);
        var aligned = ConstructGrab.IsSnapped(rel20) && AngleBetween(inFrame, ConstructGrab.SnapIn(Quaternion.Identity, inFrame)) > 1f;
        Line($"physics gun feel: looking at yaw 37, a snapped piece is {off:0.000} deg off the world's axes (the old view-relative snap {oldOff:0.0}); the view turning 200 degrees moved it {worstDrift:0.000} deg; a notch is {yawAngle:0.00} deg about up ({upDot:0.0000}) and {pitchAngle:0.00} deg about a world horizontal axis ({pitchWorldAxis}); beside a construct turned 20 degrees: reference {(near == other ? "it" : near == null ? "none" : "other")}, aligned to its axes {aligned}, far from any: {(farRef == null ? "world" : "other")}");
        if (off > 0.1f || !rotated || oldOff < 5f || worstDrift > 0.1f || MathF.Abs(yawAngle - 45f) > 0.1f || upDot < 0.9999f || MathF.Abs(pitchAngle - 45f) > 0.1f || !pitchWorldAxis
            || near != other || !aligned || farRef != null)
            Fail("physics gun feel: the snap isn't in world axes (or a nearby construct's), or a notch isn't exactly 45 degrees, or the view turning moved the piece");
    }

    /// <summary>A real host and a separate client: the client, looking at yaw 37, holds a piece snapped in the world; the host's copy agrees, and a client's predicted draw settles on it.</summary>
    private static void FeelSnapLoopback(Map yard)
    {
        const ushort port = 26378;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.5, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            Pump(server, [a], 0.25, ref now, clock);
        Pump(server, [a], 0.5, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("physics gun feel loopback: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        var part = server.Constructs.Bodies.FirstOrDefault(b => b.Loose && !b.Anchored);
        if (part == null)
        {
            Fail("physics gun feel loopback: the yard has no loose part");
            return;
        }
        // (Every step below waits for what it needs, up to a deadline, instead of a fixed time: on a slow runner the client's
        // snapshot, the tool message and the yaw arrive late, and the old fixed 0.4 s pumps then grabbed or measured too soon.)
        bool WaitFor(Func<bool> ready, double seconds = 6.0)
        {
            for (var waited = 0.0; waited < seconds; waited += 0.05)
            {
                if (ready())
                    return true;
                Pump(server, [a], 0.05, ref now, clock);
            }
            return ready();
        }
        if (!WaitFor(() => a.Local is { Alive: true }))
        {
            Fail("physics gun feel loopback: the client never saw itself alive");
            return;
        }
        var feet = ConstructWorld.Ground(World.Query, part.WorldCom.X - 3f, part.WorldCom.Z);
        sp.Teleport(feet);
        a.LookYaw = 37f * MathF.PI / 180f;
        a.LookPitch = 0;
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.PhysicsGun);
        var yaw = a.LookYaw;
        if (!WaitFor(() => server.SandboxHands.Hand(a.LocalId).Tool == ConstructTool.PhysicsGun && MathF.Abs(server.Players[a.LocalId].Yaw - yaw) < 0.01f
                           && Vector3.Distance(server.Players[a.LocalId].Position, feet) < 0.5f))
        {
            Fail("physics gun feel loopback: the host never got the client's tool, yaw and position");
            return;
        }
        a.SandboxGrab(part.Id, part.Com);
        var held = WaitFor(() => part.GrabbedBy == a.LocalId, 3.0);
        // The client snaps in the world at its yaw and sends the relative turn; the host's yaw is the client's (it is sent every tick).
        var world = ConstructGrab.SnapIn(Quaternion.Identity, ConstructGrab.YawQ(yaw) * server.SandboxHands.Hand(a.LocalId).GrabRel);
        var rel = ConstructGrab.RelOf(yaw, world);
        var d0 = server.SandboxHands.Hand(a.LocalId).GrabDist;
        float hostOff = 99, hostErr = 99, clientErr = 99;
        bool yawSeen = false;
        for (var k = 0; k < 80; k++) // (steering goes on until the copies agree: at least 24 steps, at most 4 s)
        {
            a.SandboxSteer(d0, rel, now);
            Pump(server, [a], 0.05, ref now, clock);
            hostOff = SnapOff(part.Rot);
            hostErr = AngleBetween(part.Rot, world);
            var clientCopy = a.Constructs.Get(part.Id);
            clientErr = clientCopy == null ? 99f : AngleBetween(clientCopy.Rot, world);
            yawSeen = MathF.Abs(server.Players[a.LocalId].Yaw - yaw) < 0.01f;
            if (k >= 23 && held && yawSeen && hostOff <= 0.5f && hostErr <= 0.5f && clientErr <= 0.5f)
                break;
        }
        a.SandboxLetGo(freeze: false);
        var released = WaitFor(() => part.GrabbedBy == 255 && part.Vel.Length() < 0.5f, 3.0);
        Line($"physics gun feel loopback: client at yaw 37 (host sees {yawSeen}) holds {held}; host's piece {hostOff:0.00} deg off the world's axes ({hostErr:0.00} deg from the client's snapped turn), client's copy {clientErr:0.00} deg; let go at rest {released}");
        if (!held || !yawSeen || hostOff > 0.5f || hostErr > 0.5f || clientErr > 0.5f || !released)
            Fail("physics gun feel loopback: the host's and the client's copies of a world-snapped held piece disagree, or it isn't aligned to the world");
    }
}
