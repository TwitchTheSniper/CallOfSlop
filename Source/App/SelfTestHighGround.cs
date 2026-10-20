using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "High Ground pack" self-test section: the six terrain maps in Maps/HighGround. Two of each size, every one on
/// real terrain with a few metres of relief, enough spawns for big lobbies (12 / 16 / 24 by size) on walkable ground,
/// hand-placed objectives for every mode on fair, standable ground, and a nav graph that reaches all of them.
/// (The general map, bot-match and objective checks run on these maps too, like on every map.)
/// </summary>
public static partial class SelfTest
{
    private const string HighGroundPack = "High Ground";

    private static void CheckHighGround()
    {
        if (!Directory.Exists(Maps.Folder))
            return;
        var pack = Maps.FindPack(HighGroundPack);
        if (pack == null)
        {
            Fail($"no {HighGroundPack} pack (Maps/HighGround)");
            return;
        }
        if (pack.Maps.Count != 6)
            Fail($"{HighGroundPack} has {pack.Maps.Count} maps (expected 6)");
        foreach (var size in new[] { MapSize.Small, MapSize.Medium, MapSize.Large })
            if (pack.Maps.Count(m => m.Size == size) != 2)
                Fail($"{HighGroundPack} has {pack.Maps.Count(m => m.Size == size)} {size} maps (expected 2)");
        if (pack.Order != 3 || pack.Description.Length == 0)
            Fail($"{HighGroundPack}/pack.json wasn't read (order {pack.Order})");

        foreach (var m in pack.Maps)
        {
            if (!m.HasTerrain)
            {
                Fail($"{m.Name}: no terrain");
                continue;
            }
            // Relief inside the playable area (the margin outside doesn't count).
            float lo = float.MaxValue, hi = float.MinValue;
            for (var x = -m.HalfX; x <= m.HalfX; x += 2f)
            for (var z = -m.HalfZ; z <= m.HalfZ; z += 2f)
            {
                var h = m.GroundHeight(x, z);
                lo = MathF.Min(lo, h);
                hi = MathF.Max(hi, h);
            }
            var need = m.Size switch { MapSize.Small => 12, MapSize.Medium => 16, _ => 24 };
            var steepSpawns = m.Spawns.Count(s => MathF.Abs(s.Y - m.GroundHeight(s.X, s.Z)) < 0.2f && m.IsSteep(s.X, s.Z));
            var o = m.Objectives;
            Line($"{m.Name,-12} {m.Size,-6} relief {lo:0.0}..{hi:0.0} m  spawns {m.Spawns.Count} (teams {o.TeamSpawns[0].Count}+{o.TeamSpawns[1].Count})  "
                 + $"hills {o.Hills.Count}  movers {m.Movers.Count}  foliage {(m.FoliageSection != null ? "yes" : "no")}  boxes {m.Solid.Count}");
            if (hi - lo < 6f)
                Fail($"{m.Name}: only {hi - lo:0.0} m of relief in the playable area (expected at least 6)");
            if (m.Spawns.Count < need)
                Fail($"{m.Name}: {m.Spawns.Count} spawns (a {m.Size} map needs {need})");
            if (steepSpawns > 0)
                Fail($"{m.Name}: {steepSpawns} spawn(s) on ground too steep to stand on");
            if (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated)
                Fail($"{m.Name}: objectives should be hand-placed (team spawns {!o.TeamSpawnsGenerated}, hills {!o.HillsGenerated}, flags {!o.FlagsGenerated})");
            if (o.TeamSpawns[0].Count < need / 2 - 2 || o.TeamSpawns[1].Count < need / 2 - 2)
                Fail($"{m.Name}: team spawn lists are short ({o.TeamSpawns[0].Count} + {o.TeamSpawns[1].Count})");
            if (o.Hills.Count < (m.Size == MapSize.Small ? 2 : 3))
                Fail($"{m.Name}: only {o.Hills.Count} hill zone(s)");
            // A hill zone must be standable all over: the ground inside stays within the zone's height band.
            foreach (var h in o.Hills)
                for (var k = 0; k < 8; k++)
                {
                    var a = k * MathF.PI / 4;
                    var p = h.Center + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * h.Radius * 0.8f;
                    var dy = m.GroundHeight(p.X, p.Z) - h.Center.Y;
                    if (dy is < -1f or > 3f)
                    {
                        Fail($"{m.Name}: hill at ({h.Center.X:0},{h.Center.Z:0}) is on ground too uneven for its zone ({dy:0.0} m at its edge)");
                        break;
                    }
                }

            // The nav graph reaches every spawn, hill and flag (all in the main connected area).
            World.SetMap(m);
            for (var wait = 0; wait < 2000 && NavGraph.Current == NavGraph.Empty; wait++)
                NavGraph.Pump(50);
            var g = NavGraph.Current;
            var missing = new List<string>();
            void Reach(string what, Vector3 p)
            {
                // Some node of the main area close by on the same level (Nearest alone may pick a crate top next to it).
                var reach = MathF.Max(3f, g.Spacing * 1.5f);
                for (var i = 0; i < g.Nodes.Length; i++)
                {
                    var q = g.Nodes[i];
                    if (g.Component[i] == g.MainComponent && MathF.Abs(q.Y - p.Y) < 1.2f && (q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z) < reach * reach)
                        return;
                }
                missing.Add($"{what} ({p.X:0},{p.Z:0})");
            }
            foreach (var s in m.Spawns)
                Reach("spawn", s);
            foreach (var h in o.Hills)
                Reach("hill", h.Center);
            for (var i = 0; i < o.Flags.Length; i++)
                Reach(i == 0 ? "blue flag" : "red flag", o.Flags[i]);
            if (missing.Count > 0)
                Fail($"{m.Name}: the nav graph doesn't reach {string.Join(", ", missing.Take(5))}{(missing.Count > 5 ? $" and {missing.Count - 5} more" : "")}");
        }
        World.SetMap(Maps.All[0]);
    }
}
