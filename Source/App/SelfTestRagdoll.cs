using System.Diagnostics;
using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

public static partial class SelfTest
{
    private static readonly List<string> RagdollNotes = new();

    private static void RLine(string s)
    {
        Line("  " + s);
        RagdollNotes.Add(s);
    }

    /// <summary>
    /// Ragdolls and blast knockback (Source/Physics/Ragdoll.cs, Source/Render/Ragdolls.cs, Source/Game/Knockback.cs): the
    /// rig, 10 s falls on flat ground, stairs, a slope, against a thin wall and on a terrain map (no NaN, no tunnelling,
    /// links held, settled), determinism, the kill's push (headshot, blast), hats, blasts on bodies and line of sight,
    /// boxes and the sleeping cache, CPU and allocations for 16 awake bodies; knockback falloff, Flak Jacket, prone, walls,
    /// teammates, the knock message and the client's prediction replay.
    /// </summary>
    private static void CheckRagdolls()
    {
        RagdollNotes.Clear();
        try
        {
            RagdollRigChecks();
            RagdollFalls();
            RagdollPushes();
            RagdollCost();
            KnockbackChecks();
        }
        finally
        {
            Ragdolls.WorldOverride = null;
            Ragdolls.Clear();
            if (RagdollNotes.Count > 0)
                Line("::notice title=Ragdolls (results)::" + string.Join("%0A", RagdollNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        }
    }

    // ---- Scenes ---------------------------------------------------------------------------------------------------

    private static readonly Box Floor = new(new Vector3(0, -0.5f, 0), new Vector3(30, 0.5f, 30));

    private static (PhysicsWorld World, ContactShape[] Solids) RagdollScene(IEnumerable<Box> boxes, params Obb[] obbs)
    {
        var list = boxes.ToList();
        var w = new PhysicsWorld(list, null, 40, 40);
        foreach (var o in obbs)
            w.AddObb(o);
        var solids = list.Select((b, i) => ContactShape.FromObb(Obb.FromBox(b), new ShapeId(ShapeKind.Box, i)))
            .Concat(obbs.Select((o, i) => ContactShape.FromObb(o, new ShapeId(ShapeKind.Obb, i)))).ToArray();
        return (w, solids);
    }

    /// <summary>How far the deepest particle is inside any of <paramref name="solids"/> (m).</summary>
    private static float RagdollPenetration(Ragdoll r, ContactShape[] solids)
    {
        var worst = 0f;
        for (var i = 0; i < RagdollRig.Count; i++)
            foreach (var s in solids)
            {
                var p = r.Pos[i];
                if (Ragdoll.SpherePush(s, ref p, RagdollRig.Radius[i], out _))
                    worst = MathF.Max(worst, Vector3.Distance(p, r.Pos[i]));
            }
        return worst;
    }

    /// <summary>The worst link violation (m): a rigid link's length error or how far a range link is outside its range.</summary>
    private static float RagdollLinkError(ReadOnlySpan<Vector3> p)
    {
        var worst = 0f;
        foreach (var l in RagdollRig.Links)
        {
            var d = Vector3.Distance(p[l.A], p[l.B]);
            worst = MathF.Max(worst, MathF.Max(l.Min - d, l.Max == float.MaxValue ? 0 : d - l.Max));
        }
        return worst;
    }

    private static RagdollDeath Death(Vector3 feet, float yaw, Vector3 source, float power = 3f, bool head = false, Stance stance = Stance.Stand, byte killId = 0) => new()
    {
        Feet = feet, Yaw = yaw, Stance = stance, Victim = 7, Killer = 3, KillId = killId, Headshot = head, Source = source, HasSource = true, GunPower = power,
    };

    private static void RagdollRigChecks()
    {
        var worst = 0f;
        foreach (var pose in new[] { RagdollRig.Stand, RagdollRig.Crouch, RagdollRig.Prone })
            worst = MathF.Max(worst, RagdollLinkError(pose));
        var rigid = RagdollRig.Links.Count(l => l.Min == l.Max);
        RLine($"rig: {RagdollRig.Count} particles, {RagdollRig.Links.Length} links ({rigid} rigid), arm {RagdollRig.UpperArm:0.00}+{RagdollRig.Forearm:0.00} m; "
              + $"the stand / crouch / prone rest poses break links by {worst * 1000:0.0} mm");
        if (worst > 0.01f)
            Fail($"ragdolls: a rest pose breaks its own links by {worst:0.000} m");
        if (Ragdolls.MaxInstances < Ragdolls.Capacity * 40)
            Fail("ragdolls: the instance budget is too small for a full pool");
    }

    /// <summary>One body for 10 s: returns its lowest particle bottom relative to <paramref name="floorY"/> seen at any step.</summary>
    private static float RagdollRun(RagdollBody b, int steps, float floorY, Func<Ragdoll, bool>? everyStep = null)
    {
        var lowest = float.MaxValue;
        for (var s = 0; s < steps && b.Live && !b.Sim.Broken; s++)
        {
            Ragdolls.StepAll();
            for (var i = 0; i < RagdollRig.Count; i++)
                lowest = MathF.Min(lowest, b.Sim.Pos[i].Y - RagdollRig.Radius[i] - floorY);
            if (everyStep != null && !everyStep(b.Sim))
                break;
        }
        return lowest;
    }

    private static void RagdollFalls()
    {
        const int tenSeconds = 600;
        var color = new Vector3(0.2f, 0.55f, 1f);
        var flat = RagdollScene([Floor]);
        Ragdolls.Clear();
        Ragdolls.WorldOverride = flat.World;
        var front = new Vector3(0, 1.3f, -6);
        (string Name, RagdollDeath D)[] deaths =
        [
            ("body shot", Death(Vector3.Zero, 0, front)),
            ("headshot", Death(Vector3.Zero, 0, front, 4f, head: true)),
            ("crouched, shotgun", Death(Vector3.Zero, 1f, new Vector3(2, 1, 2), 6f, stance: Stance.Crouch)),
            ("prone", Death(Vector3.Zero, 2f, front, 2f, stance: Stance.Prone)),
            ("running", Death(Vector3.Zero, 0, front) with { Velocity = new Vector3(0, 0, -8) }),
            ("frag at 1 m", new RagdollDeath { Victim = 9, Killer = 3, KillId = (byte)(Throwables.KillIdBase + 1), BlastAt = new Vector3(0.8f, 0.2f, 0.6f), BlastStrength = 11, BlastRadius = 9 }),
        ];
        foreach (var (name, d) in deaths)
        {
            Ragdolls.Clear();
            var b = Ragdolls.SpawnForTest(d, RagdollLook.Soldier, 0, color);
            var lowest = RagdollRun(b, tenSeconds, 0);
            var err = RagdollLinkError(b.Sim.Pos);
            var pen = RagdollPenetration(b.Sim, flat.Solids);
            var slept = b.Sim.Asleep && b.Sim.AwakeSteps < Ragdoll.MaxAwakeSteps;
            var travel = Vector3.Distance(b.Sim.Pelvis with { Y = 0 }, d.Feet with { Y = 0 });
            RLine($"flat, {name}: {(b.Sim.Broken ? "BROKEN" : "ok")}, lowest {lowest * 100:0.0} cm, links {err * 1000:0.0} mm, inside {pen * 1000:0.0} mm, "
                  + $"{(slept ? $"asleep after {b.Sim.AwakeSteps / 60f:0.0} s" : "never settled")}, moved {travel:0.0} m");
            if (b.Sim.Broken || !b.Live || lowest < -0.08f || err > 0.03f || pen > 0.05f || !slept || travel > (d.BlastStrength > 0 ? 14f : 6f))
                Fail($"ragdolls: flat ground, {name}: unstable (broken {b.Sim.Broken}, lowest {lowest:0.000}, links {err:0.000}, inside {pen:0.000}, slept {slept}, moved {travel:0.0} m)");
        }

        // Determinism: the same death twice gives bit-identical bodies; another seed differs.
        float[] Snap(RagdollDeath d)
        {
            Ragdolls.Clear();
            var b = Ragdolls.SpawnForTest(d, RagdollLook.Soldier, 0, color);
            for (var s = 0; s < 180; s++)
                Ragdolls.StepAll();
            return b.Sim.Pos.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray();
        }
        var a1 = Snap(deaths[5].D);
        var a2 = Snap(deaths[5].D);
        var other = Snap(deaths[5].D with { Victim = 10 });
        var same = a1.AsSpan().SequenceEqual(a2);
        var differs = !a1.AsSpan().SequenceEqual(other);
        RLine($"determinism: the same death twice is bit-identical {same}; another victim falls differently {differs}; seed {Ragdolls.Seed(deaths[0].D):X8}");
        if (!same || !differs)
            Fail("ragdolls: the same death data doesn't give the same fall (or the seed is ignored)");

        // A thin wall: a point-blank shotgun into it while running at it. Nothing passes through.
        var wall = RagdollScene([Floor, new Box(new Vector3(1.2f, 1.5f, 0), new Vector3(0.1f, 1.5f, 4f))]);
        Ragdolls.Clear();
        Ragdolls.WorldOverride = wall.World;
        var wb = Ragdolls.SpawnForTest(Death(new Vector3(0.4f, 0, 0), MathF.PI / 2, new Vector3(-2, 1.2f, 0), 6f) with { Velocity = new Vector3(8, 0, 0) }, RagdollLook.Soldier, 0, color);
        var maxX = float.MinValue;
        RagdollRun(wb, tenSeconds, 0, r =>
        {
            for (var i = 0; i < RagdollRig.Count; i++)
                maxX = MathF.Max(maxX, r.Pos[i].X);
            return true;
        });
        var wallPen = RagdollPenetration(wb.Sim, wall.Solids);
        RLine($"thin wall (20 cm): furthest particle centre x {maxX:0.00} (wall face 1.10, far side 1.30), inside {wallPen * 1000:0.0} mm, {(wb.Sim.Asleep ? "asleep" : "awake")}");
        if (wb.Sim.Broken || maxX > 1.25f || wallPen > 0.05f)
            Fail($"ragdolls: a body went into or through a thin wall (x {maxX:0.00}, inside {wallPen:0.000})");

        // Stairs: 8 steps of 20 cm, shot off the landing down them.
        var stairs = new List<Box> { Floor };
        for (var k = 0; k < 8; k++)
            stairs.Add(new Box(new Vector3((k * 0.35f + 2.8f) / 2, (k + 1) * 0.1f, 0), new Vector3((2.8f - k * 0.35f) / 2, (k + 1) * 0.1f, 2f)));
        stairs.Add(new Box(new Vector3(4.4f, 0.8f, 0), new Vector3(1.6f, 0.8f, 2f)));
        var st = RagdollScene(stairs);
        Ragdolls.Clear();
        Ragdolls.WorldOverride = st.World;
        var sb = Ragdolls.SpawnForTest(Death(new Vector3(3.1f, 1.6f, 0), -MathF.PI / 2, new Vector3(6.5f, 2.9f, 0), 5f), RagdollLook.Soldier, 0, color);
        var stLow = RagdollRun(sb, tenSeconds, 0);
        var stPen = RagdollPenetration(sb.Sim, st.Solids);
        RLine($"stairs: {(sb.Sim.Broken ? "BROKEN" : "ok")}, ended at x {sb.Sim.Pelvis.X:0.00} y {sb.Sim.Pelvis.Y:0.00}, inside {stPen * 1000:0.0} mm, lowest {stLow * 100:0.0} cm above the floor, {(sb.Sim.Asleep ? "asleep" : "awake")}");
        if (sb.Sim.Broken || stPen > 0.05f || stLow < -0.08f || RagdollLinkError(sb.Sim.Pos) > 0.03f)
            Fail($"ragdolls: unstable on the stairs (inside {stPen:0.000}, lowest {stLow:0.000})");

        // A 25 degree slope (a rotated box).
        var slope = RagdollScene([Floor], new Obb(new Vector3(0, 0, 0), new Vector3(5, 0.5f, 4), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.44f)));
        Ragdolls.Clear();
        Ragdolls.WorldOverride = slope.World;
        var slb = Ragdolls.SpawnForTest(Death(new Vector3(0, 0.6f, 0), 0.3f, new Vector3(0, 1.8f, -5), 3f), RagdollLook.Soldier, 0, color);
        RagdollRun(slb, tenSeconds, -10);
        var slPen = RagdollPenetration(slb.Sim, slope.Solids);
        RLine($"25 degree slope: {(slb.Sim.Broken ? "BROKEN" : "ok")}, ended at x {slb.Sim.Pelvis.X:0.00} y {slb.Sim.Pelvis.Y:0.00}, inside {slPen * 1000:0.0} mm, links {RagdollLinkError(slb.Sim.Pos) * 1000:0.0} mm, {(slb.Sim.Asleep ? "asleep" : "awake")}");
        if (slb.Sim.Broken || slPen > 0.05f || slb.Sim.Pelvis.Y < -0.2f || RagdollLinkError(slb.Sim.Pos) > 0.03f)
            Fail($"ragdolls: unstable on a slope (inside {slPen:0.000}, pelvis y {slb.Sim.Pelvis.Y:0.00})");

        // A terrain map: the height field holds bodies up (no shapes needed).
        var tmap = Maps.All.FirstOrDefault(m => m.TerrainCollision != null);
        if (tmap != null)
        {
            World.SetMap(tmap);
            Ragdolls.WorldOverride = null;
            var terrain = tmap.TerrainCollision!;
            var rng = new Random(5);
            Vector3? spot = null;
            for (var k = 0; k < 200 && spot == null; k++)
            {
                var x = (float)(rng.NextDouble() - 0.5) * tmap.HalfX;
                var z = (float)(rng.NextDouble() - 0.5) * tmap.HalfZ;
                var h = terrain.HeightAt(x, z);
                var top = new Vector3(x, h + 30, z);
                if (MathF.Abs(World.RaycastWorld(top, -Vector3.UnitY, 40) - 30) < 0.05f && terrain.NormalAt(x, z).Y > 0.8f)
                    spot = new Vector3(x, h, z);
            }
            if (spot is { } at)
            {
                var tb = Ragdolls.SpawnForTest(Death(at, 0.5f, at + new Vector3(0, 1.3f, -6), 4f), RagdollLook.Soldier, 0, color);
                var lowest = float.MaxValue;
                for (var s = 0; s < tenSeconds && !tb.Sim.Broken; s++)
                {
                    Ragdolls.StepAll();
                    for (var i = 0; i < RagdollRig.Count; i++)
                    {
                        var p = tb.Sim.Pos[i];
                        lowest = MathF.Min(lowest, p.Y - RagdollRig.Radius[i] - terrain.HeightAt(p.X, p.Z));
                    }
                }
                RLine($"terrain ({tmap.Name}): {(tb.Sim.Broken ? "BROKEN" : "ok")}, lowest {lowest * 100:0.0} cm against the ground, {(tb.Sim.Asleep ? "asleep" : "awake")}");
                if (tb.Sim.Broken || lowest < -0.08f)
                    Fail($"ragdolls: a body sank into the terrain ({lowest:0.000} m)");
            }
            else
                RLine($"terrain ({tmap.Name}): no open spot found (skipped)");
            World.SetMap(Maps.All[0]);
        }
    }

    private static void RagdollPushes()
    {
        var color = new Vector3(1f, 0.25f, 0.25f);
        var wall = RagdollScene([Floor, new Box(new Vector3(-3, 1.5f, 0), new Vector3(0.2f, 1.5f, 6))]);
        Ragdolls.Clear();
        Ragdolls.WorldOverride = wall.World;

        // A headshot snaps the head back harder than the chest.
        var front = new Vector3(0, 1.6f, -8);
        var hb = Ragdolls.SpawnForTest(Death(Vector3.Zero, 0, front, 4f, head: true), RagdollLook.Soldier, 0, color);
        var head0 = hb.Sim.Pos[RagdollRig.Head];
        var chest0 = hb.Sim.Pos[RagdollRig.Belly];
        for (var s = 0; s < 6; s++)
            Ragdolls.StepAll();
        var headMove = Vector3.Dot(hb.Sim.Pos[RagdollRig.Head] - head0, Vector3.UnitZ);
        var chestMove = Vector3.Dot(hb.Sim.Pos[RagdollRig.Belly] - chest0, Vector3.UnitZ);
        RLine($"headshot from the front: in 0.1 s the head went back {headMove * 100:0.0} cm, the chest {chestMove * 100:0.0} cm");
        if (!(headMove > chestMove + 0.02f) || !(chestMove > 0))
            Fail("ragdolls: a headshot doesn't snap the head back");

        // Hats: a headshot knocks it off; it lands on its side on the floor.
        if (Cosmetics.Get(HatId.TallTopHat) is { } hat)
        {
            Ragdolls.Clear();
            var tb = Ragdolls.SpawnForTest(Death(new Vector3(1, 0, 3), 0, new Vector3(1, 1.6f, -5), 4f, head: true), RagdollLook.Soldier, 0, color, hat);
            for (var s = 0; s < 240; s++)
                Ragdolls.StepAll();
            var rest = MathF.Max(-hat.Min.X, hat.Max.X);
            RLine($"hat ({hat.Name}): knocked off {tb.HatOff}, landed {tb.HatResting} at height {tb.HatRestPos.Y:0.00} (lying on its side wants {rest:0.00})");
            if (!tb.HatOff || !tb.HatResting || MathF.Abs(tb.HatRestPos.Y - rest) > 0.05f)
                Fail("ragdolls: a headshot hat didn't come off and land on the floor");
            var bodyOnly = Ragdolls.CountBoxes(tb, new float[RagdollBody.MaxBoxes * PropDebris.FloatsPerPiece]);
            if (bodyOnly > RagdollBody.MaxBoxes)
                Fail($"ragdolls: a body with a hat draws {bodyOnly} boxes, more than {RagdollBody.MaxBoxes}");
        }

        // A blast on a sleeping body wakes it and throws it away; a wall in between shields it.
        Ragdolls.Clear();
        var open = Ragdolls.SpawnForTest(Death(new Vector3(2, 0, 0), 0, front), RagdollLook.Soldier, 0, color);
        var hidden = Ragdolls.SpawnForTest(Death(new Vector3(-5, 0, 0), 0, front) with { Victim = 8 }, RagdollLook.Soldier, 0, color);
        for (var s = 0; s < 600 && !(open.Sim.Asleep && hidden.Sim.Asleep); s++)
            Ragdolls.StepAll();
        var bothAsleep = open.Sim.Asleep && hidden.Sim.Asleep;
        var o0 = open.Sim.Pelvis;
        var h0 = hidden.Sim.Pelvis;
        Ragdolls.Blast(new Vector3(-1, 0.3f, 0), 6f, 11f, 10);
        var woke = !open.Sim.Asleep && hidden.Sim.Asleep;
        for (var s = 0; s < 30; s++)
            Ragdolls.StepAll();
        var flung = open.Sim.Pelvis.X - o0.X;
        var shielded = Vector3.Distance(hidden.Sim.Pelvis, h0);
        RLine($"blast between a body in the open and one behind a wall: both asleep first {bothAsleep}, only the open one woke {woke}, "
              + $"it flew {flung:0.00} m away in 0.5 s, the shielded one moved {shielded * 100:0.0} cm");
        if (!bothAsleep || !woke || !(flung > 0.3f) || shielded > 0.01f)
            Fail("ragdolls: a blast doesn't fling sleeping bodies, or walls don't shield them");

        // Boxes: a soldier and a zombie, the sleeping cache equals a fresh pose, the fade reaches the instance data.
        var buf = new float[(PropDebris.Capacity + Ragdolls.MaxInstances) * PropDebris.FloatsPerPiece];
        Ragdolls.Clear();
        var soldier = Ragdolls.SpawnForTest(Death(Vector3.Zero, 0, front), RagdollLook.Soldier, 0, color);
        var zombie = Ragdolls.SpawnForTest(Death(new Vector3(3, 0, 3), 0, front) with { Victim = 255 }, RagdollLook.Zombie, 0, color);
        for (var s = 0; s < 600 && !(soldier.Sim.Asleep && zombie.Sim.Asleep); s++)
            Ragdolls.StepAll();
        var soldierBoxes = Ragdolls.CountBoxes(soldier, buf);
        var zombieBoxes = Ragdolls.CountBoxes(zombie, buf);
        Ragdolls.Advance(0, 0);
        var n = Ragdolls.WriteInstances(buf, 0); // builds the caches
        var fresh = buf.AsSpan(0, n * PropDebris.FloatsPerPiece).ToArray();
        var n2 = Ragdolls.WriteInstances(buf, 0); // copies them
        var cached = buf.AsSpan(0, n2 * PropDebris.FloatsPerPiece).SequenceEqual(fresh);
        Ragdolls.Advance(soldier.Until - Ragdolls.FadeTime / 2, 0);
        Ragdolls.WriteInstances(buf, 0);
        var fade = buf[3];
        Ragdolls.Advance(soldier.Until + 0.01, 0);
        var gone = !soldier.Live && !zombie.Live;
        RLine($"boxes: soldier {soldierBoxes}, zombie {zombieBoxes}, {n} instances for both; sleeping cache matches {cached}; half-way through the fade {fade:0.00}; gone after {Ragdolls.PlayerLife:0} s {gone}");
        if (soldierBoxes is < 30 or > RagdollBody.MaxBoxes || zombieBoxes is < 10 or > RagdollBody.MaxBoxes || n != soldierBoxes + zombieBoxes || !cached || MathF.Abs(fade - 0.5f) > 0.05f || !gone)
            Fail("ragdolls: boxes, the sleeping cache, the fade or the lifetime are wrong");
        if (fresh.Any(f => !float.IsFinite(f)))
            Fail("ragdolls: a box has a non-finite number");

        // The pool: never more than Capacity bodies; zombies at most MaxZombieBodies; the oldest awake freeze past MaxAwake.
        Ragdolls.Clear();
        for (var i = 0; i < Ragdolls.Capacity + 6; i++)
            Ragdolls.SpawnForTest(Death(new Vector3(i % 6 * 2f - 5, 0, i / 6 * 2f - 5), i, front) with { Victim = (byte)i }, i % 2 == 0 ? RagdollLook.Zombie : RagdollLook.Soldier, 0, color);
        Ragdolls.StepAll();
        var zombies = Enumerable.Range(0, Ragdolls.Capacity).Count(i => Ragdolls.Body(i).Live && Ragdolls.Body(i).Look == RagdollLook.Zombie);
        RLine($"pool: {Ragdolls.LiveCount} bodies after {Ragdolls.Capacity + 6} deaths ({zombies} zombies), {Ragdolls.AwakeCount} awake");
        if (Ragdolls.LiveCount > Ragdolls.Capacity || zombies > Ragdolls.MaxZombieBodies || Ragdolls.AwakeCount > Ragdolls.MaxAwake)
            Fail("ragdolls: the pool caps don't hold");
    }

    private static void RagdollCost()
    {
        var color = new Vector3(0.25f, 0.9f, 0.35f);
        var arena = RagdollScene([Floor, new Box(new Vector3(0, 1.5f, -9), new Vector3(9, 1.5f, 0.3f)), new Box(new Vector3(0, 1.5f, 9), new Vector3(9, 1.5f, 0.3f)),
            new Box(new Vector3(-9, 1.5f, 0), new Vector3(0.3f, 1.5f, 9)), new Box(new Vector3(9, 1.5f, 0), new Vector3(0.3f, 1.5f, 9)), new Box(new Vector3(2, 0.5f, 1), new Vector3(1, 0.5f, 1))]);
        Ragdolls.WorldOverride = arena.World;
        var bodies = new List<RagdollBody>();
        var buf = new float[(PropDebris.Capacity + Ragdolls.MaxInstances) * PropDebris.FloatsPerPiece];
        var times = new List<double>(64);
        var poseTimes = new List<double>(64); // (sized up front: growing them would count as the ragdolls' allocations)
        var medians = new List<double>(4);
        var poseMedians = new List<double>(4);
        var worst = 0.0;
        long bytes = 0;
        var awake = 0;
        // Four identical runs: the first only warms up (JIT, first gathers); the median of the other three counts.
        for (var run = 0; run < 4; run++)
        {
            Ragdolls.Clear();
            bodies.Clear();
            for (var i = 0; i < Ragdolls.MaxAwake; i++)
            {
                var feet = new Vector3(i % 4 * 3f - 4.5f, 0, i / 4 * 3f - 4.5f);
                bodies.Add(Ragdolls.SpawnForTest(new RagdollDeath { Feet = feet, Yaw = i, Victim = (byte)i, Killer = 99, KillId = (byte)(Throwables.KillIdBase + 1),
                    BlastAt = feet + new Vector3(0.7f, 0.2f, 0.4f), BlastStrength = 11, BlastRadius = 9 }, RagdollLook.Soldier, 0, color));
            }
            for (var s = 0; s < 3; s++)
                Ragdolls.StepAll(); // the first steps gather each body's shapes
            Ragdolls.WriteInstances(buf, 0);
            times.Clear();
            poseTimes.Clear();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var s = 0; s < 40; s++)
            {
                var t0 = Stopwatch.GetTimestamp();
                Ragdolls.StepAll();
                var t1 = Stopwatch.GetTimestamp();
                Ragdolls.WriteInstances(buf, 0);
                var t2 = Stopwatch.GetTimestamp();
                times.Add((t1 - t0) * 1000.0 / Stopwatch.Frequency);
                poseTimes.Add((t2 - t1) * 1000.0 / Stopwatch.Frequency);
            }
            if (run == 0)
                continue;
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            awake = bodies.Count(b => !b.Sim.Asleep);
            times.Sort();
            poseTimes.Sort();
            medians.Add(times[times.Count / 2]);
            poseMedians.Add(poseTimes[poseTimes.Count / 2]);
            worst = Math.Max(worst, times[^1]);
        }
        medians.Sort();
        poseMedians.Sort();
        var median = medians[1];
        var poseMedian = poseMedians[1];
        // Then let them all settle: sleeping bodies cost nothing to step and a copy to draw.
        for (var s = 0; s < 700 && bodies.Any(b => !b.Sim.Asleep); s++)
            Ragdolls.StepAll();
        var t3 = Stopwatch.GetTimestamp();
        for (var s = 0; s < 20; s++)
        {
            Ragdolls.StepAll();
            Ragdolls.WriteInstances(buf, 0);
        }
        var sleepMs = (Stopwatch.GetTimestamp() - t3) * 1000.0 / Stopwatch.Frequency / 20;
        var asleep = bodies.Count(b => b.Sim.Asleep);
        var settled = bodies.Average(b => b.Sim.AwakeSteps) / 60.0;
        var costLine = $"cost: {awake} of {bodies.Count} bodies awake: one 60 Hz step {median:0.000} ms (median of 3 runs: {string.Join(" / ", medians.Select(m => m.ToString("0.000")))}; {worst:0.000} worst step), "
              + $"posing their boxes {poseMedian:0.000} ms; {bytes} bytes allocated over 3 x 40 steps; all {asleep} asleep (after {settled:0.0} s on average): step + draw {sleepMs:0.0000} ms; target under 0.3 ms per step";
        RLine(costLine);
        if (awake < bodies.Count - 2)
            Fail($"ragdolls: only {awake} of {bodies.Count} bodies were awake for the cost test");
        if (median > 0.6)
            Fail($"ragdolls: one step of {bodies.Count} awake bodies takes {median:0.000} ms, median of 3 runs (budget 0.3 ms, 0.6 ms with CI slack)");
        if (bytes > 0)
            Fail($"ragdolls: stepping and posing allocate ({bytes} bytes over 3 x 40 steps)");
        if (asleep != bodies.Count || sleepMs > 0.1)
            Fail($"ragdolls: sleeping bodies aren't nearly free ({asleep} asleep, {sleepMs:0.000} ms)");
    }

    private static void KnockbackChecks()
    {
        // ---- The push itself ----
        var c = Vector3.Zero;
        var near = Knockback.Impulse(c, 9, 11, new Vector3(2, 0, 0), false, false);
        var mid = Knockback.Impulse(c, 9, 11, new Vector3(5, 0, 0), false, false);
        var far = Knockback.Impulse(c, 9, 11, new Vector3(8.5f, 0, 0), false, false);
        var outside = Knockback.Impulse(c, 9, 11, new Vector3(9.5f, 0, 0), false, false);
        var flak = Knockback.Impulse(c, 9, 11, new Vector3(2, 0, 0), false, true);
        var prone = Knockback.Impulse(c, 9, 11, new Vector3(2, 0, 0), true, false);
        var huge = Knockback.Impulse(c, 9, 500, new Vector3(1, 0, 0), false, false);
        RLine($"knockback (frag {Throwables.Get(ThrowableKind.Frag).Knockback} m/s, 9 m): {near.Length():0.0} m/s at 2 m, {mid.Length():0.0} at 5 m, {far.Length():0.00} at 8.5 m, "
              + $"{outside.Length()} past the radius; Flak Jacket {flak.Length():0.0}, prone {prone.Length():0.0}, capped {huge.Length():0.0}; lift {near.Y / near.Length():0.00}");
        if (!(near.Length() > mid.Length() && mid.Length() > far.Length() && far.Length() > 0) || outside != Vector3.Zero || near.X <= 0 || near.Y <= 0
            || MathF.Abs(flak.Length() - near.Length() * Perks.FlakKnockMult) > 0.01f || MathF.Abs(prone.Length() - near.Length() * Knockback.ProneMult) > 0.01f
            || huge.Length() > Knockback.MaxImpulse + 0.001f)
            Fail("knockback: the falloff, direction, Flak Jacket, prone or the cap are wrong");
        var wallScene = RagdollScene([Floor, new Box(new Vector3(2, 1.5f, 0), new Vector3(0.1f, 1.5f, 3))]);
        var seen = Knockback.InSight(wallScene.World, new Vector3(0, 1, 0), new Vector3(1.5f, 1, 0));
        var blocked = Knockback.InSight(wallScene.World, new Vector3(0, 1, 0), new Vector3(3, 1, 0));
        if (!seen || blocked)
            Fail("knockback: line of sight through a wall is wrong");
        var ms = new MoveState { Velocity = new Vector3(2, 0, 0), Grounded = true };
        Knockback.Apply(ref ms, new Vector3(20, 20, 0));
        var mantle = new MoveState { MantleTicks = 5 };
        Knockback.Apply(ref mantle, new Vector3(5, 5, 0));
        if (ms.Grounded || ms.Velocity.X > Knockback.MaxHorizontal + 0.01f || ms.Velocity.Y > Knockback.MaxUp + 0.01f || mantle.Velocity != Vector3.Zero)
            Fail("knockback: Apply doesn't lift, cap, or leave a mantle alone");

        // ---- On a server: a grenade pushes enemies and you, never teammates; the floor shields ----
        using (var game = ModeGame(3, GameMode.TeamDeathmatch))
        {
            var me = game.Players[0];
            var mate = game.Players.Values.First(p => p != me && p.Team == me.Team);
            var foe = game.Players.Values.First(p => p.Team != me.Team);
            var spot = World.Map.Spawns.Count > 0 ? World.Map.Spawns[0] : Vector3.Zero;
            Vector3 Push(Player victim, Vector3 from, PerkSet perks, Player? owner)
            {
                foreach (var p in game.Players.Values)
                {
                    game.SelfTestPerks(p, PerkSet.Empty);
                    game.SelfTestPlace(p, spot + new Vector3(30 + p.Id * 3, 0, 30), 0); // out of the way
                }
                game.SelfTestPerks(victim, perks);
                game.SelfTestPlace(victim, spot, 0);
                victim.Sim.Velocity = Vector3.Zero;
                game.SelfTestBlastKnockback(owner, victim.Chest + from, 9f, 11f);
                return victim.Sim.Velocity;
            }
            // A direction with 7 m of open space beside the spot (the blast goes off on that side).
            game.SelfTestPlace(foe, spot, 0);
            var side = new Vector3(-3, 0, 0);
            for (var k = 0; k < 8; k++)
            {
                var dir = World.FlatForward(k * MathF.PI / 4);
                if (World.RaycastWorld(foe.Chest, dir, 7.5f) >= 7.5f && World.RaycastWorld(foe.Chest, -dir, 3f) >= 3f)
                {
                    side = dir * 3f;
                    break;
                }
            }
            var away = -Vector3.Normalize(side);
            var foePush = Push(foe, side, PerkSet.Empty, me);
            var foeFar = Push(foe, side * 2.2f, PerkSet.Empty, me);
            var foeFlak = Push(foe, side, PerkSet.Empty.With(PerkCategory.Survival, PerkId.FlakJacket), me);
            var matePush = Push(mate, side, PerkSet.Empty, me);
            var selfPush = Push(me, side, PerkSet.Empty, me);
            var underFloor = Push(foe, new Vector3(0, -World.HeightOf(Stance.Stand) * 0.65f - 1.5f, 0), PerkSet.Empty, me);
            RLine($"server (TDM): an enemy 3 m from the blast {foePush.Length():0.0} m/s (away {Vector3.Dot(foePush, away):0.0}, up {foePush.Y:0.0}), at 6.6 m {foeFar.Length():0.0}, "
                  + $"with Flak Jacket {foeFlak.Length():0.0}; a teammate {matePush.Length():0.0}; yourself {selfPush.Length():0.0}; the floor between {underFloor.Length():0.0}");
            if (!(Vector3.Dot(foePush, away) > 1f && foePush.Y > 0.5f) || !(foeFar.Length() < foePush.Length() && foeFar.Length() > 0) || MathF.Abs(foeFlak.Length() - foePush.Length() * Perks.FlakKnockMult) > 0.05f
                || matePush != Vector3.Zero || !(selfPush.Length() > 1f) || underFloor != Vector3.Zero)
                Fail("knockback: the server's push on enemies / teammates / yourself / through the floor is wrong");

            // The pushed player then flies away from the blast (a few ticks of normal movement).
            Push(foe, side, PerkSet.Empty, me);
            var from = foe.Position;
            double now = 0;
            foe.Bot = null; // (no AI steering it back; it stands still on no input)
            for (var k = 0; k < 20; k++)
            {
                var s = foe.Sim;
                World.Simulate(ref s, new InputCmd { Seq = (uint)k + 1 });
                foe.Sim = s;
            }
            var moved = Vector3.Dot(foe.Position - from, away);
            RLine($"after 20 ticks the pushed enemy is {moved:0.00} m further from the blast");
            if (!(moved > 0.4f))
                Fail("knockback: a pushed player doesn't move away");
            _ = now;
        }

        // ---- The knock message and the client's prediction replay ----
        var w = new PacketWriter();
        Game.WriteKnock(w, 123456, new Vector3(1.5f, 2.5f, -3.5f));
        var r = new PacketReader(w.Span[1..]);
        var seq = r.U32();
        var imp = r.Vec();
        RLine($"knock message: {w.Length} bytes (Msg.Knock {(byte)Msg.Knock}, protocol {ServerInfo.ProtocolVersion}), round trip {(seq == 123456 && imp == new Vector3(1.5f, 2.5f, -3.5f))}");
        if (w.Length != 1 + Game.KnockBytes || seq != 123456 || imp != new Vector3(1.5f, 2.5f, -3.5f) || w.Span[0] != (byte)Msg.Knock)
            Fail("knockback: the knock message doesn't round-trip");
        int protocol = ServerInfo.ProtocolVersion;
        if (protocol < 28)
            Fail("knockback: protocol version not bumped for Msg.Knock");

        World.SetMap(Maps.Get(0));
        var feet = World.Map.Spawns.Count > 0 ? World.Map.Spawns[0] : Vector3.Zero;
        var start = new MoveState { Position = feet, Grounded = true };
        for (var k = 0; k < 30; k++)
            World.Simulate(ref start, new InputCmd { Seq = 0 }); // settle onto the ground
        InputCmd Cmd(uint s) => new() { Seq = s, Buttons = s % 3 == 0 ? Buttons.Forward : Buttons.None, Yaw = 0.3f, SpeedPct = 100 };
        var knock = new Vector3(4, 4, 1);
        // The server: inputs 1-4, the push, inputs 5-12.
        var server = start;
        var at2 = start;
        var at6 = start;
        for (uint s = 1; s <= 12; s++)
        {
            World.Simulate(ref server, Cmd(s));
            if (s == 4)
                Knockback.Apply(ref server, knock);
            if (s == 2)
                at2 = server;
            if (s == 6)
                at6 = server;
        }
        var client = new Game(NetPeer.OfflineClient(), new Audio(), "Guest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        using (client)
        {
            client.LocalId = 3;
            client.Players[3] = new Player { Id = 3, Name = "Guest", Alive = true, Sim = start };
            var cme = client.Players[3];
            for (uint s = 1; s <= 12; s++)
                client.SelfTestPredictInput(Cmd(s)); // predicted without the push
            var before = Vector3.Distance(cme.Position, server.Position);
            client.SelfTestReconcile(2, at2); // a snapshot from before the push
            var stillOff = Vector3.Distance(cme.Position, server.Position);
            client.SelfTestKnock(4, knock); // the knock arrives: replayed in after input 4
            var predicted = Vector3.Distance(cme.Position, server.Position);
            var pending = client.PendingKnocks;
            client.SelfTestReconcile(6, at6); // a snapshot that includes it: dropped, not applied twice
            var after = Vector3.Distance(cme.Position, server.Position);
            client.SelfTestKnock(5, knock); // late duplicate: already in the acknowledged state
            var dup = Vector3.Distance(cme.Position, server.Position);
            RLine($"client prediction: {before:0.00} m off without the push, {stillOff:0.00} m after an older snapshot, {predicted * 1000:0.000} mm once the knock is replayed "
                  + $"({pending} pending), {after * 1000:0.000} mm after the snapshot with it ({client.PendingKnocks} pending), {dup * 1000:0.000} mm after a stale knock");
            if (!(before > 0.1f) || predicted > 0.001f || pending != 1 || after > 0.001f || client.PendingKnocks != 0 || dup > 0.001f)
                Fail("knockback: the client doesn't predict the push exactly (or applies it twice)");
            // Garbage knocks are dropped.
            client.SelfTestKnock(99, new Vector3(float.NaN, 0, 0));
            client.SelfTestKnock(99, new Vector3(1000, 0, 0));
            if (client.PendingKnocks != 0)
                Fail("knockback: the client accepted a garbage knock");
            client.SelfTestPacket(default(RakNetGUID), [(byte)Msg.Knock, 1, 2], 0); // too short: ignored, no exception
        }
    }
}
