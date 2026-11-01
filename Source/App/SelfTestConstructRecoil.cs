using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Construct gun recoil (board item dev-construct-gun-recoil-gr01): a shot's kick is a real impulse at each barrel tip
    /// on the gun's segment. A cannon on a free buggy's left side yaws it left and pushes it back; on an anchored base the
    /// base holds still while the turntable under the gun deflects and comes back; a gun on a weak motor hinge climbs more
    /// than on a strong one and both recover; an MG's sustained fire walks an unpowered turntable (a powered one holds);
    /// the body and joints take exactly the kick (free in the air, on a turntable, a hinge and a piston); a splitter kicks
    /// per barrel and a merged receiver by the merge rules; sustained fire stays finite; no allocation and a cheap tick;
    /// a client sees the server's kicked turret where the server has it.
    /// </summary>
    private static void CheckConstructRecoil()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"construct gun recoil: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        RcBuggy();
        RcSums();
        RcTurntable();
        RcHinge();
        RcWalk();
        RcSplitMerge();
        RcSustained();
        RcNoAllocations();
        RcLoopback(yard);
    }

    private static Construct? RcBuild(ConstructWorld w, string steps, float x, float z, string tag, Vector3? air = null)
    {
        var bp = ConstructBuild.ParseBlueprint($$"""{ "name": "Recoil Test", "steps": [ {{steps}} ] }""", "recoil.json");
        var c = air is { } at ? ConstructBuild.Build(w, bp, at, Quaternion.Identity, out var why) : ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, x, z), 0, out why);
        if (c == null || c.GunCount == 0 || c.Guns[0].Def == null)
            Fail($"construct gun recoil: {tag}: the test build doesn't make a working gun: {(c == null ? why : c.GunCount == 0 ? "no gun" : c.Guns[0].Problem)}");
        return c is { GunCount: > 0 } && c.Guns[0].Def != null ? c : null;
    }

    /// <summary>The impulse one pull asks for, from the gun's own numbers (every barrel's kick backwards along the barrel).</summary>
    private static Vector3 RcPull(Construct c, int k)
    {
        ConstructWeapons.MuzzleWorld(c, k, out _, out var f);
        return -f * ConstructWeapons.PullKick(c.Guns[k]);
    }

    private static float RcDeg(float rad) => rad * 180 / MathF.PI;

    /// <summary>
    /// A Buggy with gun <paramref name="gun"/> bolted to its left rail's outside face, pointing forward (the first spot along
    /// the rail that takes it), settled on its wheels; null and a failure (with each refusal) when none does.
    /// </summary>
    private static Construct? RcGunBuggy(ConstructWorld w, string gun, float x, float z, out string spot)
    {
        var def = ConstructParts.Need(gun);
        var block = ConstructParts.Need("Frame Block");
        var why = new List<string>();
        spot = "";
        // Fits the gun on the left rail's outside: straight on its side face, or on 1 or 2 Frame Blocks (over the wheels).
        int Mount(ConstructWorld cw, Construct car, string socket, int blocks, int spin, out string no)
        {
            int Put(int on, string at, ConstructPartDef d, string by, int sp, out string refused)
            {
                var ts = Array.FindIndex(car.DefOf(on).Sockets, s => s.Name == at);
                var ds = Array.FindIndex(d.Sockets, s => s.Name == by);
                refused = ts < 0 || ds < 0 ? "no socket" : ConstructBuild.Place(car, on, ts, d, ds, (byte)sp, out var g) ?? "";
                if (refused.Length > 0)
                    return -1;
                ConstructBuild.Place(car, on, ts, d, ds, (byte)sp, out var g2);
                var i = ConstructBuild.Attach(cw, car, d, g2);
                car.Rebuild();
                return i;
            }
            if (blocks == 0)
                return Put(0, socket, def, "right", spin, out no);
            var on = Put(0, socket, block, "+x", 0, out no);
            for (var k = 1; k < blocks && on >= 0; k++)
                on = Put(on, "+y", block, "-y", 0, out no);
            return on < 0 ? -1 : Put(on, "+y", def, "bottom", spin, out no);
        }
        foreach (var (socket, blocks) in new[] { ("-x4", 0), ("-x5", 0), ("-x4", 1), ("-x4", 2), ("-x5", 2), ("-x3", 2) })
            for (var spin = 0; spin < 4; spin++)
            {
                var probe = new ConstructWorld();
                if (BuildBlueprint(probe, "Buggy", x, z) is not { } test)
                    return null;
                var gi = Mount(probe, test, socket, blocks, spin, out var no);
                if (gi < 0 || test.GunCount == 0 || test.Guns[0].Def == null)
                {
                    why.Add($"{socket}+{blocks}/{spin}: {(gi < 0 ? no : "no working gun")}");
                    continue;
                }
                var face = Vector3.Transform(test.SeatForward(), test.Rot);
                var right = Vector3.Normalize(Vector3.Cross(face, Vector3.UnitY));
                ConstructWeapons.MuzzleWorld(test, 0, out _, out var fwd);
                var side = Vector3.Dot(ConstructRender.PartCentre(test, gi) - test.WorldCom, right);
                if (Vector3.Dot(fwd, face) < 0.95f || side > -0.3f)
                {
                    why.Add($"{socket}+{blocks}/{spin}: points {Vector3.Dot(fwd, face):0.00} forward, {side:0.00} m to the right");
                    continue;
                }
                // The spot works: the same on the caller's world.
                if (BuildBlueprint(w, "Buggy", x, z) is not { } car || Mount(w, car, socket, blocks, spin, out _) < 0)
                    return null;
                spot = $"rail 0 {socket} on {blocks} block(s), spin {spin}, {side:0.00} m left of centre";
                StepConstructs(w, 150); // (settled on its wheels with the gun's weight)
                return car;
            }
        Fail($"construct gun recoil: no spot on a Buggy's left rail takes a forward-facing {gun}: {string.Join("; ", why)}");
        return null;
    }

    private static Vector3 RcFlatForward(Construct c)
    {
        var f = Vector3.Transform(c.SeatForward(), c.Rot);
        f.Y = 0;
        return Vector3.Normalize(f);
    }

    /// <summary>A Cannon bolted to a Buggy's left rail, pointing forward: fired, the buggy turns left and goes back.</summary>
    private static void RcBuggy()
    {
        var w = new ConstructWorld();
        if (RcGunBuggy(w, "Cannon", 6, 2, out var spot) is not { } car)
            return;
        var f0 = RcFlatForward(car);
        var p0 = car.WorldCom;
        var v0 = car.Vel;
        ConstructWeapons.Fire(w, car, 0, World.Query);
        StepConstructs(w, 1);
        var yawRate = car.AngVel.Y; // (+: anticlockwise from above, a turn to the left)
        var back = -Vector3.Dot(car.Vel - v0, f0);
        float Turn() => MathF.Asin(Math.Clamp(Vector3.Dot(Vector3.UnitY, Vector3.Cross(f0, RcFlatForward(car))), -1, 1));
        var turn = 0f;
        var moved = 0f;
        StepConstructs(w, 30, _ =>
        {
            turn = MathF.Max(turn, Turn());
            moved = MathF.Max(moved, -Vector3.Dot(car.WorldCom - p0, f0));
        });
        StepConstructs(w, 90);
        var settled = Turn();
        Line($"buggy: a {ConstructParts.Need("Cannon").GunKick:0} N s Cannon on the left rail ({spot}; the buggy {car.Mass:0} kg) fired once: yaw {yawRate:0.00} rad/s at once (left +), {back:0.00} m/s back; it turned up to {RcDeg(turn):0.00} deg left and rocked up to {moved * 100:0.0} cm back (its tyres hold it: {RcDeg(settled):0.00} deg left 2 s on)");
        if (yawRate < 0.05f || turn < 0.003f || back < 0.2f || moved < 0.005f || settled < 0)
            Fail("construct gun recoil: a cannon on a free buggy's left side doesn't yaw it left and push it back when fired");
    }

    /// <summary>
    /// Free in the air (no contacts): one pull's impulse is the body's momentum change exactly, with the gun on the body,
    /// on a turntable, on a motor hinge and on a piston (pushing into its stop): the joints pass the kick on.
    /// </summary>
    private static void RcSums()
    {
        var worst = 0f;
        var parts = new List<string>();
        foreach (var (name, steps) in new[]
        {
            ("body", """{ "part": "Frame Block" }, { "part": "Autocannon", "on": 0, "socket": "+y", "by": "bottom" }"""),
            ("turntable", """{ "part": "Frame Block" }, { "part": "Turntable", "on": 0, "socket": "+y", "by": "base" }, { "part": "Autocannon", "on": 1, "socket": "top", "by": "bottom" }"""),
            ("motor hinge", """{ "part": "Frame Block" }, { "part": "Motor Hinge", "on": 0, "socket": "+y", "by": "base" }, { "part": "Autocannon", "on": 1, "socket": "leaf", "by": "bottom" }"""),
            ("piston", """{ "part": "Frame Block" }, { "part": "Piston", "on": 0, "socket": "+y", "by": "base" }, { "part": "Autocannon", "on": 1, "socket": "top", "by": "back" }"""),
        })
        {
            var w = new ConstructWorld();
            if (RcBuild(w, steps, 0, 0, name, ConstructWorld.Ground(World.Query, -10, 12) + new Vector3(0, 40, 0)) is not { } c)
                continue;
            StepConstructs(w, 3);
            var want = RcPull(c, 0);
            var pa = c.Vel * c.Mass;
            StepConstructs(w, 1);
            var drift = c.Vel * c.Mass - pa; // (a tick without the shot: gravity, and the joint's own load on its stop)
            var p0 = c.Vel * c.Mass;
            var q0 = ConstructPhysics.RecoilApplied;
            ConstructWeapons.Fire(w, c, 0, World.Query);
            StepConstructs(w, 1);
            var got = c.Vel * c.Mass - p0 - drift;
            var applied = (float)(ConstructPhysics.RecoilApplied - q0);
            var err = (got - want).Length() / want.Length();
            worst = MathF.Max(worst, err);
            parts.Add($"{name} {got.Length():0.0} of {want.Length():0.0} N s ({err * 100:0.0}% off, applied {applied:0.0})");
            if (MathF.Abs(applied - want.Length()) > 0.01f * want.Length())
                Fail($"construct gun recoil: {name}: the physics put in {applied:0.0} N s for a {want.Length():0.0} N s pull");
        }
        Line($"sums: free in the air, one Autocannon pull's momentum change: {string.Join("; ", parts)}");
        if (parts.Count < 4 || worst > 0.05f)
            Fail("construct gun recoil: the body's momentum change in the air isn't the pull's impulse (the joints don't pass the kick on)");
    }

    /// <summary>
    /// An offset gun on a turntable on an anchored foundation: fired, the base doesn't move, the turntable deflects (the
    /// way the kick turns it) and comes back to where it was.
    /// </summary>
    private static Construct? RcTurret(ConstructWorld w, string gun, float x, float z)
    {
        return RcBuild(w, $$"""
            { "part": "Foundation" },
            { "part": "Turntable", "on": 0, "socket": "top11", "by": "base" },
            { "part": "Frame Beam", "on": 1, "socket": "top", "by": "y-1" },
            { "part": "{{gun}}", "on": 2, "socket": "x+3", "by": "left" }
            """, x, z, gun + " turret");
    }

    /// <summary>The turn about joint <paramref name="j"/>'s axis a pull of gun 0 asks for (N m s).</summary>
    private static float RcAxisTorque(Construct c, int j)
    {
        ref var jt = ref c.Joints[j];
        c.WorldFrame(jt.ParentSeg, out var pq, out var pt);
        var axis = Vector3.Transform(jt.Axis, pq);
        var pivot = pt + Vector3.Transform(jt.Pivot, pq);
        ConstructWeapons.MuzzleWorld(c, 0, out var m, out _);
        return Vector3.Dot(axis, Vector3.Cross(m - pivot, RcPull(c, 0)));
    }

    private static void RcTurntable()
    {
        var w = new ConstructWorld();
        if (RcTurret(w, "Cannon", 6, 2) is not { } t)
            return;
        StepConstructs(w, 60);
        var pos0 = t.Pos;
        var rot0 = t.Rot;
        var a0 = t.Joints[0].Angle;
        var tau = RcAxisTorque(t, 0);
        ConstructWeapons.Fire(w, t, 0, World.Query);
        var peak = 0f;
        StepConstructs(w, 30, _ =>
        {
            var d = World.WrapAngle(t.Joints[0].Angle - a0);
            if (MathF.Abs(d) > MathF.Abs(peak))
                peak = d;
        });
        StepConstructs(w, 180);
        var back = World.WrapAngle(t.Joints[0].Angle - a0);
        var baseMoved = Vector3.Distance(t.Pos, pos0) + MathF.Abs(1 - MathF.Abs(Quaternion.Dot(t.Rot, rot0)));
        Line($"turntable: a Cannon offset on an anchored turret ({tau:0} N m s about the turntable) fired once: the base moved {baseMoved:0.0000} (anchored {t.Anchored}); the turntable kicked {RcDeg(peak):0.00} deg (the kick's way {MathF.Sign(peak) == MathF.Sign(tau)}) and was {RcDeg(back):0.000} deg off where it was 3 s later");
        if (!t.Anchored || baseMoved > 1e-4f || MathF.Abs(tau) < 10 || MathF.Abs(peak) < 0.004f || MathF.Sign(peak) != MathF.Sign(tau) || MathF.Abs(back) > 0.002f)
            Fail("construct gun recoil: on an anchored base the cannon's kick doesn't deflect the turntable under it (and return) without moving the base");
    }

    /// <summary>A Cannon on an elevation motor hinge (anchored): the barrel climbs when fired, more on a weak hinge than a strong one, and both come back.</summary>
    private static void RcHinge()
    {
        var res = new List<string>();
        var climbs = new float[2];
        var backs = new float[2];
        var ok = true;
        var names = new[] { "weak", "strong" };
        var strengths = new[] { 3000f, 30000f };
        var loads = new float[2];
        for (var v = 0; v < 2; v++)
        {
            Construct? h = null;
            ConstructWorld? w = null;
            for (var spin = 0; spin < 4 && h == null; spin++)
            {
                w = new ConstructWorld();
                h = RcBuild(w, $$"""
                    { "part": "Foundation" },
                    { "part": "Frame Block", "on": 0, "socket": "top11", "by": "-y" },
                    { "part": "Motor Hinge 3x3 Short", "on": 1, "socket": "+y", "by": "base" },
                    { "part": "Cannon", "on": 2, "socket": "leaf", "by": "bottom", "spin": {{spin}} }
                    """, 6, 2 + v * 8, "elevation hinge");
                if (h == null)
                    return;
                h.WorldFrame(h.Joints[0].ParentSeg, out var pq, out _);
                ConstructWeapons.MuzzleWorld(h, 0, out _, out var f);
                if (MathF.Abs(Vector3.Dot(Vector3.Transform(h.Joints[0].Axis, pq), f)) > 0.2f || MathF.Abs(f.Y) > 0.3f)
                    h = null; // (the hinge has to tip the barrel up and down)
            }
            if (h == null || w == null)
            {
                Fail("construct gun recoil: no spin of a Cannon on a Motor Hinge 3x3 Short's leaf makes an elevation hinge");
                return;
            }
            StepConstructs(w, 60);
            if (v == 0)
                strengths[0] = MathF.Max(3000, MathF.Abs(h.Joints[0].Load) * 1.15f + 100); // (weak: it holds the gun's weight with little to spare)
            loads[v] = h.Joints[0].Load;
            h.Joints[0].Strength = strengths[v];
            StepConstructs(w, 120);
            float Pitch()
            {
                ConstructWeapons.MuzzleWorld(h, 0, out _, out var f);
                return MathF.Asin(Math.Clamp(f.Y, -1, 1));
            }
            var p0 = Pitch();
            ConstructWeapons.Fire(w, h, 0, World.Query);
            var peak = 0f;
            StepConstructs(w, 40, _ => { if (MathF.Abs(Pitch() - p0) > MathF.Abs(peak)) peak = Pitch() - p0; });
            StepConstructs(w, 240);
            climbs[v] = peak;
            backs[v] = Pitch() - p0;
            res.Add($"{names[v]} ({strengths[v]:0} N m, holding {MathF.Abs(loads[v]):0} N m of its weight; the kick {RcAxisTorque(h, 0):0.0} N m s about it) climbed {RcDeg(peak):0.00} deg and was {RcDeg(backs[v]):0.000} deg off 4 s later");
            ok &= MathF.Abs(backs[v]) < 0.005f;
        }
        Line($"hinge: a Cannon on an elevation Motor Hinge 3x3 Short fired once: {string.Join("; ", res)}");
        if (!ok || climbs[0] < 0.0015f || climbs[0] < MathF.Abs(climbs[1]) * 3 + 1e-4f || strengths[0] * 1.5f > strengths[1])
            Fail("construct gun recoil: a gun on a weak motor hinge doesn't climb more than on a strong one (or they don't come back)");
    }

    /// <summary>A Mounted MG's sustained fire walks an unpowered turntable round; the same powered turntable holds.</summary>
    private static void RcWalk()
    {
        var res = new string[2];
        var drift = new float[2];
        float tau = 0;
        for (var v = 0; v < 2; v++)
        {
            var w = new ConstructWorld { PowerRequired = v == 0 };
            if (RcTurret(w, "Mounted MG", 6, 2) is not { } t)
                return;
            StepConstructs(w, 60);
            tau = RcAxisTorque(t, 0);
            var a0 = t.Joints[0].Angle;
            var interval = Math.Max(1, (int)t.Guns[0].Interval);
            var shots = 0;
            StepConstructs(w, 180, tick =>
            {
                if (tick % interval == 0)
                {
                    ConstructWeapons.Fire(w, t, 0, World.Query);
                    shots++;
                }
            });
            drift[v] = World.WrapAngle(t.Joints[0].Angle - a0);
            res[v] = $"{(v == 0 ? "unpowered" : "powered")} {RcDeg(drift[v]):0.00} deg after {shots} shots";
        }
        Line($"walk: a Mounted MG offset on a turntable ({tau:0.0} N m s a shot about it), 3 s of fire: {res[0]}; {res[1]}");
        if (MathF.Abs(drift[0]) < 0.05f || MathF.Sign(drift[0]) != MathF.Sign(tau) || MathF.Abs(drift[1]) > 0.01f)
            Fail("construct gun recoil: an MG's sustained fire doesn't walk an unpowered turntable (or walks a powered one)");
    }

    /// <summary>A splitter kicks once a barrel; a merged receiver's kick scales by the merge rules (ReceiverMerge.json "recoil").</summary>
    private static void RcSplitMerge()
    {
        var res = new List<string>();
        float single = 0;
        foreach (var (n, spl) in new[] { (1, "Twin Splitter"), (1, "Quad Line Splitter"), (2, "Twin Splitter"), (3, "Pyramid Splitter") })
        {
            var def = MsDef(spl);
            if (def == null)
            {
                Fail($"construct gun recoil: no {spl} part");
                continue;
            }
            var m = MsBuild(n, def, 6 + n * 5);
            if (m.Problems.Count > 0 || m.C.GunCount == 0 || m.C.Guns[0].Def == null)
            {
                Fail($"construct gun recoil: the {n}x{n} {spl} gun doesn't build: {string.Join("; ", m.Problems)} {(m.C.GunCount > 0 ? m.C.Guns[0].Problem : "")}");
                continue;
            }
            var c = m.C;
            c.Pos += new Vector3(0, 30, 0); // (in the air: nothing else pushes it)
            c.Wake();
            StepConstructs(m.W, 1);
            ref var g = ref c.Guns[0];
            var barrels = Math.Max(1, (int)g.MuzzleCount);
            var want = RcPull(c, 0);
            var q0 = ConstructPhysics.RecoilQueued;
            var p0 = c.Vel * c.Mass;
            ConstructWeapons.Fire(m.W, c, 0, World.Query);
            var queued = (float)(ConstructPhysics.RecoilQueued - q0);
            StepConstructs(m.W, 1);
            var got = c.Vel * c.Mass - p0 + new Vector3(0, ConstructPhysics.GravityAccel * c.Mass * World.TickDt, 0) * 0.9995f;
            if (n == 1 && single == 0)
                single = g.Kick;
            var scale = single > 0 ? g.Kick / single : 0;
            var rule = ReceiverMergeRules.Times(n, ReceiverMergeRules.Current.Recoil);
            res.Add($"{n}x{n} {spl}: {barrels} barrels ({m.Outputs.Count} outputs), {queued:0.0} N s a pull ({g.Kick:0.00} a barrel, x{scale:0.00} the single, the rules x{rule:0.00}), the body took {got.Length():0.0}");
            if (barrels != m.Outputs.Count || MathF.Abs(queued - g.Kick * barrels) > 0.01f * queued + 1e-3f || (got - want).Length() > 0.05f * want.Length())
                Fail($"construct gun recoil: the {n}x{n} {spl} gun doesn't kick once a barrel ({barrels} of {m.Outputs.Count}, {queued:0.00} N s, the body {got.Length():0.00} of {want.Length():0.00})");
            if (MathF.Abs(scale - rule) > 0.01f * rule)
                Fail($"construct gun recoil: the {n}x{n} merged receiver's kick is x{scale:0.00} the single's, the merge rules say x{rule:0.00}");
        }
        Line($"splitters and merges: {string.Join("; ", res)}");
    }

    /// <summary>Sustained fire (a Cannon on a buggy, an Autocannon on an unpowered turret) stays finite and sane.</summary>
    private static void RcSustained()
    {
        var w = new ConstructWorld { PowerRequired = true };
        if (RcGunBuggy(w, "Cannon", 6, 2, out _) is not { } car || RcTurret(w, "Autocannon", 6, 14) is not { } t)
            return;
        var bad = 0;
        var fastest = 0f;
        var spin = 0f;
        var joint = 0f;
        StepConstructs(w, 600, tick =>
        {
            if (car.GunCount > 0 && tick % 30 == 0)
                ConstructWeapons.Fire(w, car, 0, World.Query);
            if (t.GunCount > 0 && tick % Math.Max(1, (int)t.Guns[0].Interval) == 0)
                ConstructWeapons.Fire(w, t, 0, World.Query);
            foreach (var c in new[] { car, t })
            {
                if (!float.IsFinite(c.Pos.X + c.Pos.Y + c.Pos.Z + c.Vel.X + c.Vel.Y + c.Vel.Z + c.AngVel.X + c.AngVel.Y + c.AngVel.Z))
                    bad++;
                for (var j = 0; j < c.JointCount; j++)
                    if (!float.IsFinite(c.Joints[j].Angle + c.Joints[j].Vel))
                        bad++;
                    else
                        joint = MathF.Max(joint, MathF.Abs(c.Joints[j].Vel));
                fastest = MathF.Max(fastest, c.Vel.Length());
                spin = MathF.Max(spin, c.AngVel.Length());
            }
        });
        Line($"sustained: 10 s of a Cannon every 0.5 s on a buggy and an Autocannon on an unpowered turret: {bad} bad numbers; fastest {fastest:0.0} m/s, spin {spin:0.0} rad/s, joint {joint:0.0} rad/s; the buggy in the world {w.Bodies.Contains(car)}");
        if (bad > 0 || fastest > 15 || spin > 6 || !w.Bodies.Contains(car))
            Fail("construct gun recoil: sustained fire made a body blow up (a bad number, or flung)");
    }

    /// <summary>Firing with recoil: no allocation in a steady state, and a cheap tick.</summary>
    private static void RcNoAllocations()
    {
        var w = new ConstructWorld { PowerRequired = true };
        if (RcTurret(w, "Mounted MG", 6, 2) is not { } t || RcGunBuggy(w, "Autocannon", 6, 12, out _) is not { } car || RcTurret(w, "Autocannon", 14, 2) is not { } t2)
            return;
        var guns = new[] { t, car, t2 };
        var tick = 0;
        void Tick()
        {
            foreach (var c in guns)
                if (c.GunCount > 0 && tick % Math.Max(1, (int)c.Guns[0].Interval) == 0)
                    ConstructWeapons.Fire(w, c, 0, World.Query);
            w.Step(World.Query, World.TickDt);
            tick++;
        }
        for (var k = 0; k < 120; k++)
            Tick();
        var shots0 = w.ShotsFired;
        var sw = Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 120; k++)
            Tick();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var ms = sw.Elapsed.TotalMilliseconds / 120;
        Line($"no allocation: an MG turret, an Autocannon turret and an Autocannon buggy firing {w.ShotsFired - shots0} shots in 120 ticks with recoil: {bytes} bytes allocated, {ms:0.000} ms a tick");
        if (bytes > 0)
            Fail($"construct gun recoil: {bytes} bytes allocated in 120 ticks of recoil (should be none)");
        if (w.ShotsFired - shots0 < 8)
            Fail("construct gun recoil: the allocation check's guns didn't fire");
        if (ms > 8)
            Fail($"construct gun recoil: {ms:0.00} ms a tick firing with recoil (over the 8 ms budget)");
    }

    /// <summary>A real host and client: the server's turret, walked by its MG's recoil, is where the server has it on the client; the gunner's shake.</summary>
    private static void RcLoopback(Map yard)
    {
        const ushort port = 26479;
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
        var w = server.Constructs;
        w.PowerRequired = true;
        if (RcTurret(w, "Mounted MG", 30, -30) is not { } t)
            return;
        Pump(server, [a], 1.0, ref now, clock);
        var a0 = t.Joints[0].Angle;
        var interval = Math.Max(1, (int)t.Guns[0].Interval) / 60.0;
        for (var k = 0; k < 20; k++)
        {
            ConstructWeapons.Fire(w, t, 0, World.Query);
            Pump(server, [a], interval, ref now, clock);
        }
        for (var wait = 0; wait < 40 && (MathF.Abs(t.Joints[0].Vel) > 0.002f || !t.Asleep); wait++)
            Pump(server, [a], 0.25, ref now, clock);
        var walked = World.WrapAngle(t.Joints[0].Angle - a0);
        var off = float.MaxValue;
        for (var wait = 0; wait < 20 && off > 0.003f; wait++)
        {
            Pump(server, [a], 0.25, ref now, clock);
            off = a.Constructs.Get(t.Id) is { JointCount: > 0 } copy ? MathF.Abs(World.WrapAngle(copy.Joints[0].Angle - t.Joints[0].Angle)) : float.MaxValue;
        }
        w.PowerRequired = false;
        var shakeMg = ConstructWeapons.GunnerShake(ConstructWeapons.PullKick(t.Guns[0]));
        var shakeCannon = ConstructWeapons.GunnerShake(ConstructParts.Need("Cannon").GunKick);
        Line($"loopback: the server's MG turret walked {RcDeg(walked):0.0} deg under 20 shots; the client's copy is {(off == float.MaxValue ? "missing" : RcDeg(off).ToString("0.000") + " deg")} off it; a gunner's shake: MG {shakeMg:0.00}, Cannon {shakeCannon:0.00}");
        if (MathF.Abs(walked) < 0.02f || off > 0.003f)
            Fail("construct gun recoil: a client doesn't see the server's recoil (the turret where the server has it)");
        if (!(shakeMg > 0 && shakeMg < 0.15f && shakeCannon > shakeMg * 3 && shakeCannon <= 0.6f))
            Fail("construct gun recoil: the gunner's shake isn't small for an MG and bigger for a cannon");
    }
}
