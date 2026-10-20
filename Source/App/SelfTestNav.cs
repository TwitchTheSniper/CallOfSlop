using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// World collision asks <see cref="CollisionGrid"/> (boxes in a grid) instead of scanning every box on the map. On
    /// every map, random rays (long, short, straight down, along an axis, from outside the map), body checks, projectile
    /// point tests and whole movement ticks (walking, jumping, mantling, starting inside a wall) must give exactly the
    /// same answers both ways.
    /// </summary>
    private static void CheckCollisionGrid()
    {
        var rng = new Random(11);
        var mismatches = 0;
        var queries = 0;
        var moves = 0;
        long scanTicks = 0, gridTicks = 0, scanMoveTicks = 0, gridMoveTicks = 0;
        try
        {
            foreach (var m in Maps.All)
            {
                World.SetMap(m);
                var c = World.Grid;
                var top = 4f;
                foreach (var b in m.Solid)
                    top = MathF.Max(top, b.Max.Y + 2f);
                if (m.TerrainCollision is { } terrain)
                    top = MathF.Max(top, terrain.Data.MaxHeight + 2f);
                var bad = 0;
                for (var i = 0; i < 1500; i++)
                {
                    var o = new Vector3(Rand(-m.HalfX - 6, m.HalfX + 6), Rand(-0.5f, top), Rand(-m.HalfZ - 6, m.HalfZ + 6));
                    if (i % 5 == 0) // on the grid's lines, where columns meet
                        o = new Vector3(MathF.Round(o.X / CollisionGrid.Cell) * CollisionGrid.Cell, o.Y, MathF.Round(o.Z / CollisionGrid.Cell) * CollisionGrid.Cell);
                    var dir = (i % 6) switch
                    {
                        0 => -Vector3.UnitY,
                        1 => rng.Next(4) switch { 0 => Vector3.UnitX, 1 => -Vector3.UnitX, 2 => Vector3.UnitZ, _ => -Vector3.UnitZ },
                        2 => Vector3.Normalize(new Vector3(Rand(-1, 1), 0, Rand(-1, 1)) + new Vector3(1e-4f, 0, 0)),
                        _ => Vector3.Normalize(new Vector3(Rand(-1, 1), Rand(-1, 1), Rand(-1, 1)) + new Vector3(0, 1e-4f, 0)),
                    };
                    var len = i % 3 == 0 ? Rand(0.05f, 3f) : Rand(1f, 140f);
                    World.UseGrid = false;
                    var t0 = Stopwatch.GetTimestamp();
                    var want = World.RaycastWorld(o, dir, len);
                    var t1 = Stopwatch.GetTimestamp();
                    World.UseGrid = true;
                    var got = World.RaycastWorld(o, dir, len);
                    gridTicks += Stopwatch.GetTimestamp() - t1;
                    scanTicks += t1 - t0;
                    queries++;
                    if (want != got && bad++ < 3)
                        Line($"  {m.Name}: ray {o} dir {dir} len {len:0.00}: every box {want} grid {got}");

                    var feet = new Vector3(o.X, MathF.Round(o.Y * 2) / 2 + (i % 2 == 0 ? 0.01f : 0f), o.Z); // also exactly on box tops
                    var h = (i % 3) switch { 0 => World.PlayerHeight, 1 => World.PlayerHeight - World.StepHeight - 0.02f, _ => 1.2f };
                    World.UseGrid = false;
                    t0 = Stopwatch.GetTimestamp();
                    var wantB = World.Blocked(feet, h);
                    t1 = Stopwatch.GetTimestamp();
                    World.UseGrid = true;
                    var gotB = World.Blocked(feet, h);
                    gridTicks += Stopwatch.GetTimestamp() - t1;
                    scanTicks += t1 - t0;
                    queries++;
                    if (wantB != gotB && bad++ < 3)
                        Line($"  {m.Name}: body at {feet} height {h:0.00}: every box {wantB} grid {gotB}");
                    if (c.Blocked(feet, h) != gotB && bad++ < 3)
                        Line($"  {m.Name}: body at {feet} height {h:0.00}: World.Blocked and the grid disagree");

                    var r = Rand(0.02f, 0.3f);
                    var wantP = false;
                    foreach (var b in m.Solid)
                        wantP |= o.X > b.Min.X - r && o.X < b.Max.X + r && o.Y > b.Min.Y - r && o.Y < b.Max.Y + r && o.Z > b.Min.Z - r && o.Z < b.Max.Z + r;
                    if (c.PointInside(o, r) != wantP && bad++ < 3)
                        Line($"  {m.Name}: point {o} radius {r:0.00}: every box {wantP} grid {!wantP}");
                    queries++;
                }

                // Movement: a few ticks from random spots (on a surface, in the air, inside things), random keys.
                for (var run = 0; run < 150; run++)
                {
                    var x = Rand(-m.HalfX + 1, m.HalfX - 1);
                    var z = Rand(-m.HalfZ + 1, m.HalfZ - 1);
                    var y = run % 3 == 0 ? Rand(-0.5f, top) : top - World.RaycastWorld(new Vector3(x, top, z), -Vector3.UnitY, top + 6f);
                    var s = new MoveState
                    {
                        Position = new Vector3(x, y, z),
                        Velocity = new Vector3(Rand(-14, 14), Rand(-8, 6), Rand(-14, 14)),
                        Grounded = run % 2 == 0,
                        Stance = (Stance)rng.Next(3),
                        MovePerk = run % 7 == 0 ? PerkId.Parkour : PerkId.None,
                    };
                    var a = s;
                    var yaw = Rand(-MathF.PI, MathF.PI);
                    for (var tick = 0; tick < 24; tick++)
                    {
                        var cmd = new InputCmd
                        {
                            Buttons = (Buttons)rng.Next(256) | (rng.Next(3) == 0 ? Buttons.Forward | Buttons.Jump : Buttons.None),
                            Yaw = yaw += Rand(-0.3f, 0.3f),
                            SpeedPct = 100,
                        };
                        World.UseGrid = false;
                        var t0 = Stopwatch.GetTimestamp();
                        World.Simulate(ref s, cmd);
                        var t1 = Stopwatch.GetTimestamp();
                        World.UseGrid = true;
                        World.Simulate(ref a, cmd);
                        gridMoveTicks += Stopwatch.GetTimestamp() - t1;
                        scanMoveTicks += t1 - t0;
                        moves++;
                        if (s.Position != a.Position || s.Velocity != a.Velocity || s.Grounded != a.Grounded || s.Stance != a.Stance
                            || s.MantleTicks != a.MantleTicks || s.MantleTo != a.MantleTo)
                        {
                            if (bad++ < 3)
                                Line($"  {m.Name}: movement tick {tick} from ({x:0.00}, {y:0.00}, {z:0.00}): every box {s.Position} {s.Velocity}, grid {a.Position} {a.Velocity}");
                            break;
                        }
                    }
                }
                if (bad > 0)
                    Fail($"{m.Name}: {bad} collision grid answer(s) differ from scanning every box");
                mismatches += bad;
            }
        }
        finally
        {
            World.UseGrid = true;
        }
        var speedup = gridTicks == 0 ? 0 : scanTicks / (double)gridTicks;
        var moveSpeedup = gridMoveTicks == 0 ? 0 : scanMoveTicks / (double)gridMoveTicks;
        Line($"collision grid: {queries} rays, body and point checks and {moves} movement ticks on {Maps.All.Length} maps, "
            + $"{mismatches} different from scanning every box; queries {speedup:0.0}x faster, movement {moveSpeedup:0.0}x");
        return;

        float Rand(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
    }
}
