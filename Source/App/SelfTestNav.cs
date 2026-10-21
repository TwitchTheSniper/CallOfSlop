using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Old collision code against new (the physics module's parity suite). On every map, random rays (long, short, straight
    /// down, along an axis, from outside the map; world and nav), body checks, projectile point tests and whole movement ticks
    /// (walking, jumping, mantling, starting inside a wall, long held-key input streams) must give bit-identical answers from
    /// the frozen old code (<see cref="LegacyCollision"/>: every-box scans and the 4 m collision grid) and from the new
    /// <see cref="PhysicsWorld"/> + <see cref="ClassicMover"/>. Maps with breakable props are checked again with half of them
    /// broken. Also times old scan, old grid and the new BVH.
    /// </summary>
    private static void CheckCollisionParity()
    {
        var rng = new Random(11);
        var mismatches = 0;
        var queries = 0;
        var moves = 0;
        long scanTicks = 0, gridTicks = 0, newTicks = 0, gridMoveTicks = 0, newMoveTicks = 0;
        long rayScan = 0, rayGrid = 0, rayNew = 0, bodyGrid = 0, bodyNew = 0;
        int rayCount = 0, bodyCount = 0;
        var previousMover = World.Mover;
        var sectionTime = Stopwatch.StartNew();
        try
        {
            foreach (var m in Maps.All)
            {
                World.SetMap(m);
                var phys = World.Physics;
                var top = 4f;
                foreach (var b in m.Solid)
                    top = MathF.Max(top, b.Max.Y + 2f);
                if (m.TerrainCollision is { } terrain)
                    top = MathF.Max(top, terrain.Data.MaxHeight + 2f);
                var bad = 0;
                var broken = false;
                for (var pass = 0; pass < 2; pass++)
                {
                    if (pass == 1)
                    {
                        // Second pass: half the breakable props broken (their boxes switched off in the new world, parked in the old).
                        if (m.BreakableProps.Length == 0)
                            break;
                        for (var k = 0; k < m.BreakableProps.Length; k += 2)
                            m.BreakProp(m.BreakableProps[k]);
                        broken = true;
                    }
                    var rays = pass == 0 ? 1500 : 400;
                    for (var i = 0; i < rays; i++)
                    {
                        var o = new Vector3(Rand(-m.HalfX - 6, m.HalfX + 6), Rand(-0.5f, top), Rand(-m.HalfZ - 6, m.HalfZ + 6));
                        if (i % 5 == 0) // on the old grid's lines, where columns meet
                            o = new Vector3(MathF.Round(o.X / CollisionGrid.Cell) * CollisionGrid.Cell, o.Y, MathF.Round(o.Z / CollisionGrid.Cell) * CollisionGrid.Cell);
                        var dir = (i % 6) switch
                        {
                            0 => -Vector3.UnitY,
                            1 => rng.Next(4) switch { 0 => Vector3.UnitX, 1 => -Vector3.UnitX, 2 => Vector3.UnitZ, _ => -Vector3.UnitZ },
                            2 => Vector3.Normalize(new Vector3(Rand(-1, 1), 0, Rand(-1, 1)) + new Vector3(1e-4f, 0, 0)),
                            _ => Vector3.Normalize(new Vector3(Rand(-1, 1), Rand(-1, 1), Rand(-1, 1)) + new Vector3(0, 1e-4f, 0)),
                        };
                        var len = i % 3 == 0 ? Rand(0.05f, 3f) : Rand(1f, 140f);
                        LegacyCollision.UseGrid = false;
                        var t0 = Stopwatch.GetTimestamp();
                        var want = LegacyCollision.RaycastWorld(o, dir, len);
                        var t1 = Stopwatch.GetTimestamp();
                        LegacyCollision.UseGrid = true;
                        var grid = LegacyCollision.RaycastWorld(o, dir, len);
                        var t2 = Stopwatch.GetTimestamp();
                        var got = World.RaycastWorld(o, dir, len);
                        var t3 = Stopwatch.GetTimestamp();
                        scanTicks += t1 - t0;
                        gridTicks += t2 - t1;
                        newTicks += t3 - t2;
                        rayScan += t1 - t0;
                        rayGrid += t2 - t1;
                        rayNew += t3 - t2;
                        rayCount++;
                        queries++;
                        if ((want != got || grid != want) && bad++ < 3)
                            Line($"  {m.Name}: ray {o} dir {dir} len {len:0.00}: every box {want} old grid {grid} new {got}");
                        // Bot navigation's ray (movers as sweep pillars) and the box-only ray.
                        if (i % 3 == 0)
                        {
                            if (LegacyCollision.RaycastNav(o, dir, len) != World.RaycastNav(o, dir, len) && bad++ < 3)
                                Line($"  {m.Name}: nav ray {o} dir {dir} len {len:0.00}: old {LegacyCollision.RaycastNav(o, dir, len)} new {World.RaycastNav(o, dir, len)}");
                            if (LegacyCollision.RaycastBoxes(o, dir, len) != World.RaycastBoxes(o, dir, len) && bad++ < 3)
                                Line($"  {m.Name}: box ray {o} dir {dir} len {len:0.00} differs");
                            queries += 2;
                        }

                        var feet = new Vector3(o.X, MathF.Round(o.Y * 2) / 2 + (i % 2 == 0 ? 0.01f : 0f), o.Z); // also exactly on box tops
                        var h = (i % 3) switch { 0 => World.PlayerHeight, 1 => World.PlayerHeight - World.StepHeight - 0.02f, _ => 1.2f };
                        LegacyCollision.UseGrid = false;
                        t0 = Stopwatch.GetTimestamp();
                        var wantB = LegacyCollision.Blocked(feet, h);
                        t1 = Stopwatch.GetTimestamp();
                        LegacyCollision.UseGrid = true;
                        var gridB = LegacyCollision.Blocked(feet, h);
                        t2 = Stopwatch.GetTimestamp();
                        var gotB = World.Blocked(feet, h);
                        t3 = Stopwatch.GetTimestamp();
                        scanTicks += t1 - t0;
                        gridTicks += t2 - t1;
                        newTicks += t3 - t2;
                        bodyGrid += t2 - t1;
                        bodyNew += t3 - t2;
                        bodyCount++;
                        queries++;
                        if ((wantB != gotB || gridB != wantB) && bad++ < 3)
                            Line($"  {m.Name}: body at {feet} height {h:0.00}: every box {wantB} old grid {gridB} new {gotB}");
                        if (i % 3 == 1 && LegacyCollision.BlockedByBoxes(feet, h) != World.BlockedByBoxes(feet, h) && bad++ < 3)
                            Line($"  {m.Name}: box-only body at {feet} height {h:0.00} differs");

                        var r = Rand(0.02f, 0.3f);
                        LegacyCollision.UseGrid = false;
                        var wantP = LegacyCollision.PointInside(o, r);
                        var gotP = phys.PointInside(o, r, new QueryFilter(QueryLayers.StaticShapes | QueryLayers.Movers));
                        if (gotP != wantP && bad++ < 3)
                            Line($"  {m.Name}: point {o} radius {r:0.00}: every box {wantP} new {gotP}");
                        queries += 2;
                    }

                    // Movement: a few ticks from random spots (on a surface, in the air, inside things), random keys, plus
                    // a few long streams of held keys (like a player or bot: hold a direction for a while, sprint, jump).
                    var runs = pass == 0 ? 150 : 40;
                    for (var run = 0; run < runs + 6; run++)
                    {
                        var stream = run >= runs;
                        var x = Rand(-m.HalfX + 1, m.HalfX - 1);
                        var z = Rand(-m.HalfZ + 1, m.HalfZ - 1);
                        var y = run % 3 == 0 && !stream ? Rand(-0.5f, top) : top - World.RaycastWorld(new Vector3(x, top, z), -Vector3.UnitY, top + 6f);
                        var s = new MoveState
                        {
                            Position = new Vector3(x, y, z),
                            Velocity = stream ? Vector3.Zero : new Vector3(Rand(-14, 14), Rand(-8, 6), Rand(-14, 14)),
                            Grounded = stream || run % 2 == 0,
                            Stance = stream ? Stance.Stand : (Stance)rng.Next(3),
                            MovePerk = run % 7 == 0 ? PerkId.Parkour : PerkId.None,
                        };
                        var a = s;
                        var yaw = Rand(-MathF.PI, MathF.PI);
                        var held = Buttons.Forward;
                        var ticks = stream ? 300 : 24;
                        for (var tick = 0; tick < ticks; tick++)
                        {
                            Buttons keys;
                            if (stream)
                            {
                                if (tick % 30 == 0)
                                    held = (Buttons)rng.Next(16) | (rng.Next(2) == 0 ? Buttons.Sprint : Buttons.None) | (rng.Next(6) == 0 ? Buttons.Crouch : Buttons.None);
                                keys = held | (rng.Next(20) == 0 ? Buttons.Jump : Buttons.None);
                                yaw += Rand(-0.08f, 0.08f);
                            }
                            else
                            {
                                keys = (Buttons)rng.Next(256) | (rng.Next(3) == 0 ? Buttons.Forward | Buttons.Jump : Buttons.None);
                                yaw += Rand(-0.3f, 0.3f);
                            }
                            var cmd = new InputCmd { Buttons = keys, Yaw = yaw, SpeedPct = 100 };
                            World.Mover = LegacyMover.Instance;
                            LegacyCollision.UseGrid = true;
                            var t0 = Stopwatch.GetTimestamp();
                            World.Simulate(ref s, cmd);
                            var t1 = Stopwatch.GetTimestamp();
                            World.Mover = ClassicMover.Instance;
                            World.Simulate(ref a, cmd);
                            newMoveTicks += Stopwatch.GetTimestamp() - t1;
                            gridMoveTicks += t1 - t0;
                            moves++;
                            if (s.Position != a.Position || s.Velocity != a.Velocity || s.Grounded != a.Grounded || s.Stance != a.Stance
                                || s.MantleTicks != a.MantleTicks || s.MantleTo != a.MantleTo)
                            {
                                if (bad++ < 3)
                                    Line($"  {m.Name}: movement tick {tick} from ({x:0.00}, {y:0.00}, {z:0.00}){(stream ? " (stream)" : "")}: old {s.Position} {s.Velocity}, new {a.Position} {a.Velocity}");
                                break;
                            }
                        }
                    }
                }
                if (broken)
                    m.RestoreProps();
                if (bad > 0)
                    Fail($"{m.Name}: {bad} collision answer(s) of the physics world differ from the old code");
                mismatches += bad;
            }
        }
        finally
        {
            LegacyCollision.UseGrid = true;
            World.Mover = previousMover;
        }
        var us = 1e6 / Stopwatch.Frequency;
        Line($"collision parity: {queries} rays, body and point checks and {moves} movement ticks on {Maps.All.Length} maps, "
            + $"{mismatches} different from the old code");
        Line($"query cost (mean): world ray every-box scan {rayScan * us / rayCount:0.00} us, old grid {rayGrid * us / rayCount:0.00} us, new BVH {rayNew * us / rayCount:0.00} us; "
            + $"body check old grid {bodyGrid * us / bodyCount:0.000} us, new BVH {bodyNew * us / bodyCount:0.000} us; "
            + $"movement tick old (grid) {gridMoveTicks * us / moves:0.00} us, new {newMoveTicks * us / moves:0.00} us");
        PhysicsSummary.Add($"parity {mismatches} diffs in {queries} queries + {moves} ticks; ray scan/grid/bvh {rayScan * us / rayCount:0.00}/{rayGrid * us / rayCount:0.00}/{rayNew * us / rayCount:0.00} us; "
            + $"body grid/bvh {bodyGrid * us / bodyCount:0.000}/{bodyNew * us / bodyCount:0.000} us; move old/new {gridMoveTicks * us / moves:0.00}/{newMoveTicks * us / moves:0.00} us; section {sectionTime.Elapsed.TotalSeconds:0.0} s");
        return;

        float Rand(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
    }
}
