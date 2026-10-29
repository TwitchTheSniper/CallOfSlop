using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Soft ragdoll joints, the relax pull and the stronger shot push (Source/Physics/Ragdoll.cs, RagdollTuning.cs,
    /// Ragdolls/Ragdolls.json): the file loads and a bad one is refused; every joint limit pushed past its soft start at
    /// speed is driven back and never bends past its hard backstop (knees too); a body lying spread-eagled drifts into the
    /// relaxed pose and sleeps; the gun push follows its formula and cap and lands hardest on the hit particle; and a
    /// scripted fall down a ledge with shots and a blast stays finite, out of the floor and settles.
    /// </summary>
    private static void CheckRagdollSoft()
    {
        try
        {
            RagdollSoftFile();
            RagdollSoftLimits();
            RagdollRelax();
            RagdollShotPush();
            RagdollScriptedFall();
        }
        finally
        {
            RagdollTuning.Reload();
            Ragdolls.WorldOverride = null;
            Ragdolls.Clear();
        }
    }

    private static void RagdollSoftFile()
    {
        RagdollTuning.Reload();
        var errors = RagdollTuning.LoadErrors.ToList();
        var defaults = RagdollTuning.Params.All(p => RagdollTuning.Get(p.Id) == p.Default);
        string? refused = null;
        try
        {
            RagdollTuning.Load("{\"format\": \"call-of-slop-ragdolls/1\", \"tuning\": {\"gunCap\": 99}}", "test");
        }
        catch (WeaponFileException ex)
        {
            refused = ex.Message;
        }
        string? unknown = null;
        try
        {
            RagdollTuning.Load("{\"tuning\": {\"softness\": 1}}", "test");
        }
        catch (WeaponFileException ex)
        {
            unknown = ex.Message;
        }
        RagdollTuning.Reload();
        Line($"  file: {RagdollTuning.Params.Length} values, {errors.Count} error(s){(errors.Count > 0 ? ": " + errors[0] : "")}; the shipped file matches the defaults {defaults}; "
             + $"gunCap 99 refused: {refused ?? "NO"}; an unknown key refused: {(unknown != null)}");
        if (errors.Count > 0 || refused == null || unknown == null)
            Fail("ragdoll soft limits: Ragdolls/Ragdolls.json doesn't load cleanly, or bad values aren't refused");
        if (!defaults)
            Fail("ragdoll soft limits: Ragdolls/Ragdolls.json and the defaults in RagdollTuning.cs differ (keep them the same)");
    }

    /// <summary>A body standing in mid-air (no floor near) for the joint tests.</summary>
    private static RagdollBody AirBody(Vector3 color)
    {
        Ragdolls.Clear();
        var b = Ragdolls.SpawnForTest(new RagdollDeath { Feet = new Vector3(0, 40, 0), Victim = 7, Killer = 3, KillId = Game.SuicideKillId }, RagdollLook.Soldier, 0, color);
        b.Sim.Init(RagdollRig.Stand, Matrix4x4.CreateTranslation(0, 40, 0), Vector3.Zero);
        return b;
    }

    private static void RagdollSoftLimits()
    {
        var color = new Vector3(0.9f, 0.6f, 0.2f);
        Ragdolls.WorldOverride = RagdollScene([Floor]).World;
        var worstPast = 0f;
        var backWorst = "";
        var notBack = 0;
        var past = 0;
        var notes = new List<string>();
        for (var r = 0; r < Ragdoll.RangeCount; r++)
        {
            var (a, bIdx, min) = Ragdoll.RangeLink(r);
            var (soft, back) = Ragdoll.SoftZone(r);
            var body = AirBody(color);
            var sim = body.Sim;
            // Slam the two ends together at 9 m/s each (a hard hit folding the joint).
            var n = Vector3.Normalize(sim.Pos[bIdx] - sim.Pos[a]);
            sim.Push(a, n * 9f);
            sim.Push(bIdx, -n * 9f);
            var low = float.MaxValue;
            for (var s = 0; s < 30; s++)
            {
                Ragdolls.StepAll();
                low = MathF.Min(low, Vector3.Distance(sim.Pos[a], sim.Pos[bIdx]));
            }
            var end = Vector3.Distance(sim.Pos[a], sim.Pos[bIdx]);
            if (low < min - 0.002f)
                past++;
            var over = back - low;
            if (over > worstPast)
                (worstPast, backWorst) = (over, $"{a}-{bIdx}");
            if (end < min - 0.01f)
                notBack++;
            if (r < 4 || low < min)
                notes.Add($"{a}-{bIdx}: limit {min:0.00}, soft from {soft:0.00}, backstop {back:0.00}, closest {low:0.000}, after 0.5 s {end:0.000}");
        }
        Line($"  joint limits ({Ragdoll.RangeCount} range links slammed shut at 9 m/s): {past} went past their limit, worst past the backstop {worstPast * 1000:0.0} mm ({(backWorst == "" ? "none" : backWorst)}), {notBack} not driven back to the limit in 0.5 s");
        foreach (var s in notes.Take(8))
            Line("    " + s);
        if (worstPast > 0.003f)
            Fail($"ragdoll soft limits: link {backWorst} bent {worstPast:0.000} m past its hard backstop");
        if (notBack > 0)
            Fail($"ragdoll soft limits: {notBack} joint(s) pushed past their limit weren't driven back");

        // The knee: kicked backwards hard, it stops at the backstop and springs back in front of the hip-foot line.
        var kb = AirBody(color);
        var ks = kb.Sim;
        ks.Push(RagdollRig.KneeR, ks.Forward() * -12f);
        var worstAhead = float.MaxValue;
        float Ahead(Ragdoll q) => Vector3.Dot(q.Pos[RagdollRig.KneeR] - (q.Pos[RagdollRig.HipR] + q.Pos[RagdollRig.FootR]) * 0.5f, q.Forward());
        for (var s = 0; s < 40; s++)
        {
            Ragdolls.StepAll();
            worstAhead = MathF.Min(worstAhead, Ahead(ks));
        }
        var kneeBack = -Ragdoll.KneeSlack - RagdollTuning.Get(RT.KneeHard);
        Line($"  knee kicked backwards at 12 m/s: furthest behind the hip-foot line {-worstAhead * 100:0.0} cm (backstop {-kneeBack * 100:0.0} cm), after 0.7 s {Ahead(ks) * 100:0.0} cm in front");
        if (worstAhead < kneeBack - 0.01f || Ahead(ks) < -Ragdoll.KneeSlack - 0.005f)
            Fail("ragdoll soft limits: a knee bent backwards past its backstop, or didn't come back");
    }

    private static void RagdollRelax()
    {
        var color = new Vector3(0.3f, 0.8f, 0.9f);
        var flat = RagdollScene([Floor]);
        Ragdolls.WorldOverride = flat.World;
        // Spread-eagled on its back: arms straight out at the sides, legs apart, lying on the floor.
        var pose = RagdollRig.Stand.ToArray();
        for (var s = 0; s < 2; s++)
        {
            var x = s == 0 ? 1f : -1f;
            var sh = pose[RagdollRig.ShoulderR + s];
            pose[RagdollRig.ElbowR + s * 2] = sh + new Vector3(x * RagdollRig.UpperArm, 0, 0);
            pose[RagdollRig.HandR + s * 2] = sh + new Vector3(x * (RagdollRig.UpperArm + RagdollRig.Forearm), 0, 0);
            var hip = pose[RagdollRig.HipR + s];
            var thigh = Vector3.Distance(RagdollRig.Stand[RagdollRig.HipR], RagdollRig.Stand[RagdollRig.KneeR]);
            var shin = Vector3.Distance(RagdollRig.Stand[RagdollRig.KneeR], RagdollRig.Stand[RagdollRig.FootR]);
            var leg = Vector3.Normalize(new Vector3(x * 0.45f, -1, -0.04f));
            pose[RagdollRig.KneeR + s * 2] = hip + leg * thigh;
            pose[RagdollRig.FootR + s * 2] = hip + leg * (thigh + shin);
        }
        // Tipped onto its back (up -> +Z, back -> down), just above the floor.
        var lie = Matrix4x4.CreateRotationX(MathF.PI / 2) * Matrix4x4.CreateTranslation(0, 0.3f, 0);
        (float Before, float After, float Seconds, bool Asleep, float Pen) Run(bool pull)
        {
            var saved = RagdollTuning.Get(RT.NeutralPull);
            if (!pull)
                RagdollTuning.Values[(int)RT.NeutralPull] = 0;
            try
            {
                var b = AirBody(color);
                b.Sim.Init(pose, lie, Vector3.Zero);
                var before = b.Sim.RelaxError();
                RagdollRun(b, 600, 0, r => !r.Asleep);
                return (before, b.Sim.RelaxError(), b.Sim.AwakeSteps / 60f, b.Sim.Asleep && b.Sim.AwakeSteps < Ragdoll.MaxAwakeSteps, RagdollPenetration(b.Sim, flat.Solids));
            }
            finally
            {
                RagdollTuning.Values[(int)RT.NeutralPull] = saved;
            }
        }
        var with = Run(true);
        var without = Run(false);
        Line($"  relaxing, spread-eagled on its back: {with.Before * 100:0.0} cm from the relaxed pose; with the pull {with.After * 100:0.0} cm, asleep {with.Asleep} after {with.Seconds:0.0} s; "
             + $"without it {without.After * 100:0.0} cm, asleep after {without.Seconds:0.0} s; inside the floor {with.Pen * 1000:0.0} mm");
        if (!with.Asleep || with.Seconds > 5f || with.Pen > 0.03f)
            Fail($"ragdoll soft limits: a relaxing body didn't sleep within 5 s ({with.Seconds:0.0} s, asleep {with.Asleep}) or sank into the floor");
        if (!(with.After < with.Before * 0.6f) || !(with.After < without.After * 0.75f))
            Fail("ragdoll soft limits: the relax pull doesn't bring a body near the relaxed pose");

        // A normal death: the pull doesn't keep it awake long.
        Ragdolls.Clear();
        var d = Ragdolls.SpawnForTest(Death(Vector3.Zero, 0.4f, new Vector3(0, 1.3f, -6), 4f), RagdollLook.Soldier, 0, color);
        RagdollRun(d, 600, 0, r => !r.Asleep);
        Line($"  a body shot on flat ground sleeps after {d.Sim.AwakeSteps / 60f:0.0} s, {d.Sim.RelaxError() * 100:0.0} cm from the relaxed pose");
        if (!d.Sim.Asleep || d.Sim.AwakeSteps > 6 * 60)
            Fail("ragdoll soft limits: a shot body takes more than 6 s to fall asleep");
    }

    private static void RagdollShotPush()
    {
        var color = new Vector3(0.8f, 0.3f, 0.7f);
        // The formula: damage x (0.6 + calibre) / 18 x scale, clamped.
        var scale = RagdollTuning.Get(RT.GunScale);
        float Old(float dmg, float cal) => Math.Clamp(dmg * (0.6f + cal) / 18f, 1.2f, 6f);
        var formulaOk = true;
        var shown = new List<string>();
        foreach (var w in Weapons.All.Where(w => !w.IsLauncher).GroupBy(w => w.Class).Select(g => g.First()))
        {
            var p = Ragdolls.GunPower(w);
            var want = Math.Clamp(w.ShotDamage * (0.6f + w.Caliber) / 18f * scale, RagdollTuning.Get(RT.GunMin), RagdollTuning.Get(RT.GunCap));
            formulaOk &= MathF.Abs(p - want) < 1e-4f;
            shown.Add($"{w.Name} {Old(w.ShotDamage, w.Caliber):0.0} -> {p:0.0}");
        }
        var cap = Ragdolls.GunPower(500, 2);
        var floor = Ragdolls.GunPower(1, 0);
        Line($"  gun push (m/s, old -> new): {string.Join(", ", shown)}; the hardest gun {cap:0.0} (cap {RagdollTuning.Get(RT.GunCap):0}), the softest {floor:0.0}");
        if (!formulaOk || cap != RagdollTuning.Get(RT.GunCap) || floor != RagdollTuning.Get(RT.GunMin) || RagdollTuning.Get(RT.GunCap) < 9.9f || scale < 1.5f)
            Fail("ragdoll soft limits: the gun push doesn't follow damage x (0.6 + calibre) / 18 x scale within its min and cap");

        // At the hit particle: a shot into the right shoulder moves it most, lifts it, and twists the body.
        var b = AirBody(color);
        var sim = b.Sim;
        var dir = new Vector3(0, 0, 1); // from in front (the body faces -Z) to the back
        Ragdolls.ShotPush(sim, RagdollRig.ShoulderR, dir, 8f, false);
        var hit = sim.Velocity(RagdollRig.ShoulderR);
        var other = sim.Velocity(RagdollRig.ShoulderL);
        var foot = sim.Velocity(RagdollRig.FootL);
        Line($"  a shot (8 m/s) into the right shoulder: it moves {Vector3.Dot(hit, dir):0.0} m/s back and {hit.Y:0.0} up, the left shoulder {Vector3.Dot(other, dir):0.0}, the left foot {Vector3.Dot(foot, dir):0.0}");
        if (!(Vector3.Dot(hit, dir) > Vector3.Dot(other, dir) * 1.5f) || !(hit.Y > 0.3f) || !(Vector3.Dot(other, dir) > Vector3.Dot(foot, dir)))
            Fail("ragdoll soft limits: a shot doesn't push hardest at the hit particle (with lift)");

        // Stronger than before: a body shot with the shipped push moves the body further than the old one did.
        float Travel(float gunScale)
        {
            var saved = RagdollTuning.Values.ToArray();
            try
            {
                RagdollTuning.Values[(int)RT.GunScale] = gunScale;
                if (gunScale == 1f)
                {
                    RagdollTuning.Values[(int)RT.BodyShare] = 1f; // the old push: the whole body, no hit particle
                    RagdollTuning.Values[(int)RT.HitShare] = RagdollTuning.Values[(int)RT.NeighbourShare] = RagdollTuning.Values[(int)RT.HitLift] = 0;
                }
                Ragdolls.WorldOverride = RagdollScene([Floor]).World;
                Ragdolls.Clear();
                var rifle = Weapons.All.First(w => w.Class == WeaponClass.AssaultRifle);
                var body = Ragdolls.SpawnForTest(Death(Vector3.Zero, 0, new Vector3(0, 1.3f, -6), Ragdolls.GunPower(rifle)), RagdollLook.Soldier, 0, color);
                for (var s = 0; s < 30; s++)
                    Ragdolls.StepAll();
                return body.Sim.Pelvis.Z;
            }
            finally
            {
                Array.Copy(saved, RagdollTuning.Values, saved.Length);
            }
        }
        var oldTravel = Travel(1f);
        var newTravel = Travel(scale);
        Line($"  an assault rifle body shot: the hips moved {newTravel:0.00} m in 0.5 s (the old push: {oldTravel:0.00} m)");
        if (!(newTravel > oldTravel * 1.2f))
            Fail("ragdoll soft limits: shots don't move bodies visibly more than before");
    }

    private static void RagdollScriptedFall()
    {
        var color = new Vector3(0.5f, 0.5f, 0.9f);
        // A 3 m ledge over a floor, a sniper shot off it, a second shot mid-air and a blast where it lands.
        var scene = RagdollScene([Floor, new Box(new Vector3(-3, 1.5f, 0), new Vector3(3, 1.5f, 3)), new Box(new Vector3(4, 0.3f, 0), new Vector3(0.5f, 0.3f, 2))]);
        Ragdolls.Clear();
        Ragdolls.WorldOverride = scene.World;
        var b = Ragdolls.SpawnForTest(Death(new Vector3(-0.4f, 3, 0), MathF.PI / 2, new Vector3(-6, 4.3f, 0.5f), Ragdolls.GunPower(500, 2)), RagdollLook.Soldier, 0, color);
        var finite = true;
        var worstPen = 0f;
        for (var s = 0; s < 600 && !b.Sim.Broken; s++)
        {
            if (s == 20)
                Ragdolls.ShotPush(b.Sim, RagdollRig.Belly, Vector3.Normalize(new Vector3(1, -0.2f, 0.3f)), RagdollTuning.Get(RT.GunCap), false);
            if (s == 70)
                Ragdolls.Blast(b.Sim.Pelvis + new Vector3(0.8f, -0.3f, 0.4f), 6f, 11f, s / 60.0);
            Ragdolls.StepAll();
            foreach (var p in b.Sim.Pos)
                finite &= float.IsFinite(p.X + p.Y + p.Z);
            if (s % 10 == 0)
                worstPen = MathF.Max(worstPen, RagdollPenetration(b.Sim, scene.Solids));
        }
        Line($"  scripted fall (a 3 m ledge, two shots, a blast): finite {finite}, {(b.Sim.Broken ? "BROKEN" : "whole")}, deepest inside a box {worstPen * 1000:0.0} mm, links {RagdollLinkError(b.Sim.Pos) * 1000:0.0} mm, "
             + $"{(b.Sim.Asleep ? $"asleep after {b.Sim.AwakeSteps / 60f:0.0} s" : "awake")} at {b.Sim.Pelvis:0.0}");
        if (!finite || b.Sim.Broken || worstPen > 0.05f || !b.Sim.Asleep || RagdollLinkError(b.Sim.Pos) > 0.03f)
            Fail("ragdoll soft limits: the scripted fall went wrong (NaN, broken, tunnelled, links or never settled)");
    }
}
