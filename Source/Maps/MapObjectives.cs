using System.Numerics;

namespace CallOfSlop;

/// <summary>A King of the Hill zone: a circle on the ground (feet height <c>Center.Y</c>).</summary>
public readonly record struct HillZone(Vector3 Center, float Radius);

/// <summary>
/// Where the game modes happen on a map: two teams' spawn points, the hill zones (in rotation order) and the two flags.
/// A map file can place them by hand ("teamSpawns", "hills", "flags"); whatever it leaves out is worked out from the
/// map deterministically, from data the map's fingerprint covers (size, boxes, spawns, waypoints), so host and clients
/// always agree. Nothing here depends on terrain or foliage.
/// </summary>
public sealed class MapObjectives
{
    public required List<Vector3>[] TeamSpawns; // [team] -> spawn points
    public required List<HillZone> Hills;
    public required Vector3[] Flags; // [team] -> flag stand
    public bool TeamSpawnsGenerated, HillsGenerated, FlagsGenerated;

    public static MapObjectives Build(Map m)
    {
        var handTeams = m.TeamSpawnData is { Length: 2 } ts && ts[0].Count > 0 && ts[1].Count > 0;
        var teams = handTeams ? new[] { new List<Vector3>(m.TeamSpawnData![0]), new List<Vector3>(m.TeamSpawnData[1]) } :SplitSpawns(m.Spawns);
        var flags = m.FlagData is { Length: 2 } fd ? (Vector3[])fd.Clone() : DeepFlags(teams);
        var hills = m.HillData is { Count: > 0 } hd ? new List<HillZone>(hd) : GenerateHills(m, teams);
        return new MapObjectives
        {
            TeamSpawns = teams,
            Hills = hills,
            Flags = flags,
            TeamSpawnsGenerated = !handTeams,
            HillsGenerated = m.HillData is not { Count: > 0 },
            FlagsGenerated = m.FlagData is not { Length: 2 },
        };
    }

    private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    private static Vector3 Centroid(List<Vector3> pts)
    {
        var c = Vector3.Zero;
        foreach (var p in pts)
            c += p;
        return pts.Count > 0 ? c / pts.Count : Vector3.Zero;
    }

    /// <summary>
    /// Two far-apart groups: the two spawns furthest from each other anchor the teams, and every spawn joins the anchor
    /// it's relatively closer to, split evenly (the extra one, if odd, goes to the first team).
    /// </summary>
    public static List<Vector3>[] SplitSpawns(IReadOnlyList<Vector3> spawns)
    {
        var result = new[] { new List<Vector3>(), new List<Vector3>() };
        if (spawns.Count == 0)
            return result;
        if (spawns.Count == 1)
        {
            result[0].Add(spawns[0]);
            result[1].Add(spawns[0]);
            return result;
        }
        int ai = 0, bi = 1;
        var best = -1f;
        for (var i = 0; i < spawns.Count; i++)
            for (var j = i + 1; j < spawns.Count; j++)
            {
                var d = Flat(spawns[i], spawns[j]);
                if (d > best + 1e-4f)
                {
                    best = d;
                    ai = i;
                    bi = j;
                }
            }
        var a = spawns[ai];
        var b = spawns[bi];
        var order = Enumerable.Range(0, spawns.Count).OrderBy(i => Flat(spawns[i], a) - Flat(spawns[i], b)).ThenBy(i => i).ToList();
        var half = (spawns.Count + 1) / 2;
        var team = new int[spawns.Count];
        for (var k = 0; k < order.Count; k++)
            team[order[k]] = k < half ? 0 : 1;
        for (var i = 0; i < spawns.Count; i++)
            result[team[i]].Add(spawns[i]); // original order within each team
        return result;
    }

    /// <summary>Each flag on the team's spawn that's deepest in its own half (furthest from the other team's spawns).</summary>
    public static Vector3[] DeepFlags(List<Vector3>[] teams)
    {
        var flags = new Vector3[2];
        for (var t = 0; t < 2; t++)
        {
            var mine = teams[t];
            var other = Centroid(teams[1 - t]);
            var best = -1f;
            foreach (var s in mine)
            {
                var d = Flat(s, other);
                if (d > best + 1e-4f)
                {
                    best = d;
                    flags[t] = s;
                }
            }
        }
        return flags;
    }

    /// <summary>Hill radius for a map size.</summary>
    public static float HillRadius(Map m) => m.Size switch
    {
        MapSize.Small => 3.5f,
        MapSize.Medium => 4.5f,
        _ => 6f,
    };

    /// <summary>
    /// Hills near the most travelled central waypoints on open ground: every waypoint is scored by how many straight
    /// lines between the two teams' spawns pass close to it (the routes people run), how central and how fair (equally
    /// far from both teams) it is, and how open the ground around it is. The best few, spread apart, become the hills.
    /// </summary>
    public static List<HillZone> GenerateHills(Map m, List<Vector3>[] teams)
    {
        var radius = HillRadius(m);
        var diag = MathF.Sqrt(m.HalfX * m.HalfX + m.HalfZ * m.HalfZ) * 2f;
        var candidates = m.Waypoints.Count > 0 ? m.Waypoints : m.Spawns;
        var c0 = Centroid(teams[0]);
        var c1 = Centroid(teams[1]);
        var mid = (c0 + c1) / 2;
        var lane = MathF.Max(6f, diag * 0.06f);

        var scored = new List<(Vector3 P, float Score)>();
        foreach (var w in candidates)
        {
            if (m.IsBlocked(w))
                continue;
            // Open ground: probes in a ring around the spot.
            var open = 0;
            for (var k = 0; k < 8; k++)
            {
                var a = k * MathF.Tau / 8;
                if (!m.IsBlocked(w + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * radius * 0.7f))
                    open++;
            }
            if (open < 5)
                continue;
            var travel = 0;
            foreach (var s0 in teams[0])
                foreach (var s1 in teams[1])
                    if (SegmentDistance(w, s0, s1) < lane)
                        travel++;
            var pairs = Math.Max(1, teams[0].Count * teams[1].Count);
            var central = Flat(w, mid) / diag;
            var fair = MathF.Abs(Flat(w, c0) - Flat(w, c1)) / diag;
            var score = travel / (float)pairs * 3f - central * 4f - fair * 3f + open * 0.1f;
            scored.Add((w, score));
        }

        var count = m.Size == MapSize.Small ? 2 : 3;
        var spacing = MathF.Max(12f, diag * 0.2f);
        var hills = new List<HillZone>();
        foreach (var (p, _) in scored.OrderByDescending(s => s.Score).ThenBy(s => s.P.X).ThenBy(s => s.P.Z))
        {
            if (hills.Count >= count)
                break;
            if (hills.Any(h => Flat(h.Center, p) < spacing))
                continue;
            hills.Add(new HillZone(p, radius));
        }
        if (hills.Count == 0)
            hills.Add(new HillZone(candidates.Count > 0 ? candidates.OrderBy(c => Flat(c, mid)).First() : Vector3.Zero, radius));
        return hills;
    }

    /// <summary>Horizontal distance from p to the segment a-b.</summary>
    private static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = new Vector2(b.X - a.X, b.Z - a.Z);
        var ap = new Vector2(p.X - a.X, p.Z - a.Z);
        var len2 = ab.LengthSquared();
        var t = len2 < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(ap, ab) / len2, 0f, 1f);
        return (ap - ab * t).Length();
    }
}

public sealed partial class Map
{
    /// <summary>Hand-placed team spawns from the map file ("teamSpawns"), or null to split the spawns automatically.</summary>
    public List<Vector3>[]? TeamSpawnData;

    /// <summary>Hand-placed hills from the map file ("hills"), or null to place them automatically.</summary>
    public List<HillZone>? HillData;

    /// <summary>Hand-placed flags from the map file ("flags": team 0's then team 1's), or null to place them automatically.</summary>
    public Vector3[]? FlagData;

    private MapObjectives? _objectives;

    /// <summary>Game mode objectives (worked out once, the first time they're needed).</summary>
    public MapObjectives Objectives => _objectives ??= MapObjectives.Build(this);

    /// <summary>Forget the cached objectives (after editing the map's data in code).</summary>
    public void InvalidateObjectives() => _objectives = null;

    /// <summary>Feeds what objectives are built from (beyond boxes and spawns) into the map fingerprint.</summary>
    private void MixObjectiveData(Action<float> mix)
    {
        foreach (var w in Waypoints)
        {
            mix(w.X);
            mix(w.Y);
            mix(w.Z);
        }
        if (TeamSpawnData != null)
            foreach (var list in TeamSpawnData)
            {
                mix(list.Count);
                foreach (var s in list)
                {
                    mix(s.X);
                    mix(s.Y);
                    mix(s.Z);
                }
            }
        if (HillData != null)
            foreach (var h in HillData)
            {
                mix(h.Center.X);
                mix(h.Center.Y);
                mix(h.Center.Z);
                mix(h.Radius);
            }
        if (FlagData != null)
            foreach (var f in FlagData)
            {
                mix(f.X);
                mix(f.Y);
                mix(f.Z);
            }
    }
}

/// <summary>
/// The optional objective fields of a map file:
///   "teamSpawns": [ [ [x, y, z], ... ], [ ... ] ]   two lists of spawn points (y may be left out: [x, z])
///   "hills":      [ { "x": 0, "z": 0, "y": 0, "radius": 4.5 }, ... ]   in rotation order (y and radius optional)
///   "flags":      [ [x, y, z], [x, y, z] ]   team 1's flag, then team 2's
/// </summary>
public static class MapObjectiveJson
{
    private static string Num(float f) => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private static string Pt(Vector3 p) => $"[{Num(p.X)}, {Num(p.Y)}, {Num(p.Z)}]";

    public static void Write(System.Text.StringBuilder sb, Map m)
    {
        if (m.TeamSpawnData is { Length: 2 } ts)
            sb.Append("  \"teamSpawns\": [").Append(string.Join(", ", ts.Select(l => "[" + string.Join(", ", l.Select(Pt)) + "]"))).Append("],\n");
        if (m.HillData is { Count: > 0 } hills)
            sb.Append("  \"hills\": [").Append(string.Join(", ", hills.Select(h =>
                $"{{ \"x\": {Num(h.Center.X)}, \"y\": {Num(h.Center.Y)}, \"z\": {Num(h.Center.Z)}, \"radius\": {Num(h.Radius)} }}"))).Append("],\n");
        if (m.FlagData is { Length: 2 } flags)
            sb.Append("  \"flags\": [").Append(string.Join(", ", flags.Select(Pt))).Append("],\n");
    }

    public static void Read(System.Text.Json.JsonElement root, Map map, string source)
    {
        System.Text.Json.JsonElement? Prop(System.Text.Json.JsonElement obj, string name)
        {
            if (obj.ValueKind != System.Text.Json.JsonValueKind.Object)
                return null;
            foreach (var p in obj.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    return p.Value;
            return null;
        }
        float Num(System.Text.Json.JsonElement e, string where) =>
            e.ValueKind == System.Text.Json.JsonValueKind.Number ? e.GetSingle() : throw new MapFileException($"{source}: {where}: expected a number, got {e}");
        Vector3 Point(System.Text.Json.JsonElement e, string where)
        {
            if (e.ValueKind != System.Text.Json.JsonValueKind.Array || e.GetArrayLength() is not (2 or 3))
                throw new MapFileException($"{source}: {where}: a point is [x, y, z] or [x, z]");
            return e.GetArrayLength() == 2 ? new Vector3(Num(e[0], where), 0, Num(e[1], where)) : new Vector3(Num(e[0], where), Num(e[1], where), Num(e[2], where));
        }

        if (Prop(root, "teamSpawns") is { } ts)
        {
            if (ts.ValueKind != System.Text.Json.JsonValueKind.Array || ts.GetArrayLength() != 2)
                throw new MapFileException($"{source}: \"teamSpawns\" must be two lists of points, one per team");
            map.TeamSpawnData = new List<Vector3>[2];
            for (var t = 0; t < 2; t++)
            {
                if (ts[t].ValueKind != System.Text.Json.JsonValueKind.Array || ts[t].GetArrayLength() == 0)
                    throw new MapFileException($"{source}: \"teamSpawns\" team {t + 1} needs at least one point");
                map.TeamSpawnData[t] = ts[t].EnumerateArray().Select(e => Point(e, $"teamSpawns team {t + 1}")).ToList();
            }
        }
        if (Prop(root, "hills") is { } hills)
        {
            if (hills.ValueKind != System.Text.Json.JsonValueKind.Array || hills.GetArrayLength() == 0)
                throw new MapFileException($"{source}: \"hills\" must be a list of {{ \"x\", \"z\" }} zones");
            var list = new List<HillZone>();
            foreach (var h in hills.EnumerateArray())
            {
                if (Prop(h, "x") is not { } x || Prop(h, "z") is not { } z)
                    throw new MapFileException($"{source}: each hill needs \"x\" and \"z\"");
                var y = Prop(h, "y") is { } ye ? Num(ye, "hill y") : 0f;
                var r = Prop(h, "radius") is { } re ? Math.Clamp(Num(re, "hill radius"), 1.5f, 30f) : MapObjectives.HillRadius(map);
                list.Add(new HillZone(new Vector3(Num(x, "hill x"), y, Num(z, "hill z")), r));
            }
            map.HillData = list;
        }
        if (Prop(root, "flags") is { } flags)
        {
            if (flags.ValueKind != System.Text.Json.JsonValueKind.Array || flags.GetArrayLength() != 2)
                throw new MapFileException($"{source}: \"flags\" must be two points: team 1's flag, then team 2's");
            map.FlagData = [Point(flags[0], "flags"), Point(flags[1], "flags")];
        }
    }
}
