using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The nav graph build asks <see cref="NavCollision"/> (boxes in a grid) instead of the world's every-box scan. On
    /// every map, random rays (long, short, straight down, along an axis, from outside the map) and body checks must
    /// give exactly the same answers both ways.
    /// </summary>
    private static void CheckNavCollision()
    {
        var rng = new Random(11);
        var mismatches = 0;
        var queries = 0;
        long worldTicks = 0, gridTicks = 0;
        foreach (var m in Maps.All)
        {
            World.SetMap(m);
            var c = new NavCollision(m);
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
                    o = new Vector3(MathF.Round(o.X / NavCollision.Cell) * NavCollision.Cell, o.Y, MathF.Round(o.Z / NavCollision.Cell) * NavCollision.Cell);
                var dir = (i % 6) switch
                {
                    0 => -Vector3.UnitY,
                    1 => rng.Next(4) switch { 0 => Vector3.UnitX, 1 => -Vector3.UnitX, 2 => Vector3.UnitZ, _ => -Vector3.UnitZ },
                    2 => Vector3.Normalize(new Vector3(Rand(-1, 1), 0, Rand(-1, 1)) + new Vector3(1e-4f, 0, 0)),
                    _ => Vector3.Normalize(new Vector3(Rand(-1, 1), Rand(-1, 1), Rand(-1, 1)) + new Vector3(0, 1e-4f, 0)),
                };
                var len = i % 3 == 0 ? Rand(0.05f, 3f) : Rand(1f, 140f);
                var t0 = Stopwatch.GetTimestamp();
                var want = World.RaycastWorld(o, dir, len);
                var t1 = Stopwatch.GetTimestamp();
                var got = c.Raycast(o, dir, len);
                gridTicks += Stopwatch.GetTimestamp() - t1;
                worldTicks += t1 - t0;
                queries++;
                if (want != got && bad++ < 3)
                    Line($"  {m.Name}: ray {o} dir {dir} len {len:0.00}: world {want} grid {got}");

                var feet = new Vector3(o.X, MathF.Round(o.Y * 2) / 2 + (i % 2 == 0 ? 0.01f : 0f), o.Z); // also exactly on box tops
                var h = (i % 3) switch { 0 => World.PlayerHeight, 1 => World.PlayerHeight - World.StepHeight - 0.02f, _ => 1.2f };
                t0 = Stopwatch.GetTimestamp();
                var wantB = World.Blocked(feet, h);
                t1 = Stopwatch.GetTimestamp();
                var gotB = c.Blocked(feet, h);
                gridTicks += Stopwatch.GetTimestamp() - t1;
                worldTicks += t1 - t0;
                queries++;
                if (wantB != gotB && bad++ < 3)
                    Line($"  {m.Name}: body at {feet} height {h:0.00}: world {wantB} grid {gotB}");
            }
            if (bad > 0)
                Fail($"{m.Name}: {bad} nav collision answer(s) differ from the world's");
            mismatches += bad;
        }
        var speedup = gridTicks == 0 ? 0 : worldTicks / (double)gridTicks;
        Line($"nav collision: {queries} rays and body checks on {Maps.All.Length} maps, {mismatches} different from the world, {speedup:0.0}x faster");
        return;

        float Rand(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
    }
}
