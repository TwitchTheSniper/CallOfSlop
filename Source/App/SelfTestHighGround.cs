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
            // Hand-placed spawns for the size class (12 / 16 / 24) and for the map's player limit, none generated.
            var need = Math.Max(m.Size switch { MapSize.Small => 12, MapSize.Medium => 16, _ => 24 }, m.MaxPlayers);
            var steepSpawns = m.Spawns.Count(s => MathF.Abs(s.Y - m.GroundHeight(s.X, s.Z)) < 0.2f && m.IsSteep(s.X, s.Z));
            var o = m.Objectives;
            Line($"{m.Name,-12} {m.Size,-6} relief {lo:0.0}..{hi:0.0} m  max {m.MaxPlayers}  spawns {m.Spawns.Count} (teams {o.TeamSpawns[0].Count}+{o.TeamSpawns[1].Count})  "
                 + $"hills {o.Hills.Count}  movers {m.Movers.Count}  foliage {(m.FoliageSection != null ? "yes" : "no")}  boxes {m.Solid.Count}");
            if (hi - lo < 6f)
                Fail($"{m.Name}: only {hi - lo:0.0} m of relief in the playable area (expected at least 6)");
            var authored = m.AuthoredSpawns >= 0 ? m.AuthoredSpawns : m.Spawns.Count;
            if (authored < need)
                Fail($"{m.Name}: {authored} hand-placed spawns (a {m.Size} map for {m.MaxPlayers} players needs {need})");
            if (m.MaxPlayersSetting == 0)
                Fail($"{m.Name}: no \"maxPlayers\" in the file");
            if (steepSpawns > 0)
                Fail($"{m.Name}: {steepSpawns} spawn(s) on ground too steep to stand on");
            if (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated)
                Fail($"{m.Name}: objectives should be hand-placed (team spawns {!o.TeamSpawnsGenerated}, hills {!o.HillsGenerated}, flags {!o.FlagsGenerated})");
            if (o.TeamSpawns[0].Count < m.MaxPlayers / 2 - 2 || o.TeamSpawns[1].Count < m.MaxPlayers / 2 - 2)
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

            CheckHighGroundProps(m, o);

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

    /// <summary>
    /// The pack's props: every map is dressed with hand-placed props and clutter, no solid prop stands on a spawn, a hill
    /// zone or a flag, explosive barrels are few and away from spawns, and the props are mirrored like the map (each one has
    /// a twin of the same kind at (-x, -z), turned 180 degrees), so both teams get the same cover.
    /// </summary>
    private static void CheckHighGroundProps(Map m, MapObjectives o)
    {
        var props = m.PropInstances;
        var placedByHand = m.ClutterStart;
        var clutter = props.Count - m.ClutterStart;
        var explosive = props.Where(p => p.Def.Breakable is { ExplodeRadius: > 0 }).ToList();
        Line($"  props {props.Count}: {placedByHand} placed by hand ({props.Take(placedByHand).Count(p => p.Def.IsSolid)} solid), {clutter} clutter "
             + $"({props.Skip(placedByHand).Count(p => p.Def.IsSolid)} solid), {props.Count(p => p.IsBreakable)} breakable, {explosive.Count} explosive");
        if (placedByHand < 10 || clutter < 10)
            Fail($"{m.Name}: barely dressed ({placedByHand} hand-placed props, {clutter} clutter; expected at least 10 of each)");
        if (explosive.Count > 2)
            Fail($"{m.Name}: {explosive.Count} explosive props (at most 2)");
        foreach (var e in explosive)
            if (m.Spawns.Any(s => Vector2.Distance(new Vector2(s.X, s.Z), new Vector2(e.Position.X, e.Position.Z)) < 10f))
                Fail($"{m.Name}: explosive {e.Def.Name} at ({e.Position.X:0.0},{e.Position.Z:0.0}) is within 10 m of a spawn");

        // Solid props keep off spawns (a player's standing room), hill zones (their floor band) and flags.
        static float Dist(Box b, Vector3 p)
        {
            var dx = MathF.Max(0, MathF.Max(b.Min.X - p.X, p.X - b.Max.X));
            var dz = MathF.Max(0, MathF.Max(b.Min.Z - p.Z, p.Z - b.Max.Z));
            return MathF.Sqrt(dx * dx + dz * dz);
        }
        var blocked = new List<string>();
        foreach (var p in props.Where(p => p.Def.IsSolid))
            for (var k = p.SolidStart; k < p.SolidStart + p.SolidCount; k++)
            {
                var b = m.Solid[k];
                string? why = null;
                foreach (var sp in m.Spawns)
                    if (why == null && Dist(b, sp) < 1f && b.Max.Y > sp.Y + 0.05f && b.Min.Y < sp.Y + World.PlayerHeight)
                        why = $"spawn ({sp.X:0.0},{sp.Z:0.0})";
                foreach (var h in o.Hills)
                    if (why == null && Dist(b, h.Center) < h.Radius && b.Max.Y > h.Center.Y - 1f && b.Min.Y < h.Center.Y + 3f)
                        why = $"hill zone ({h.Center.X:0},{h.Center.Z:0})";
                foreach (var f in o.Flags)
                    if (why == null && Dist(b, f) < 2f)
                        why = $"flag ({f.X:0},{f.Z:0})";
                if (why != null)
                {
                    blocked.Add($"{p.Def.Name} ({p.Position.X:0.0},{p.Position.Z:0.0}) on the {why}");
                    break;
                }
            }
        if (blocked.Count > 0)
            Fail($"{m.Name}: {blocked.Count} solid prop(s) in the way: {string.Join("; ", blocked.Take(4))}");

        // Mirrored: every prop has its twin turned 180 degrees about the centre (same kind and size, the same height above
        // the ground: the terrain's fine noise isn't mirrored, so absolute heights differ a little).
        float Above(PropInstance p) => p.Position.Y - m.GroundHeight(p.Position.X, p.Position.Z);
        var unmatched = new List<string>();
        var used = new bool[props.Count];
        for (var i = 0; i < props.Count; i++)
        {
            if (used[i])
                continue;
            var a = props[i];
            var found = -1;
            for (var j = 0; j < props.Count && found < 0; j++)
            {
                if (j == i || used[j])
                    continue;
                var b = props[j];
                var turn = MathF.Abs(MathF.IEEERemainder(b.Yaw - a.Yaw - MathF.PI, MathF.Tau));
                if (b.Def == a.Def && MathF.Abs(b.Position.X + a.Position.X) < 0.05f && MathF.Abs(b.Position.Z + a.Position.Z) < 0.05f
                    && MathF.Abs(Above(b) - Above(a)) < 0.5f && turn < 0.02f && MathF.Abs(b.Scale - a.Scale) < 0.01f)
                    found = j;
            }
            if (found < 0)
                unmatched.Add($"{a.Def.Name} ({a.Position.X:0.0},{a.Position.Z:0.0})");
            else
                used[i] = used[found] = true;
        }
        if (unmatched.Count > 0)
            Fail($"{m.Name}: {unmatched.Count} prop(s) without a mirrored twin: {string.Join(", ", unmatched.Take(5))}");
    }
}
