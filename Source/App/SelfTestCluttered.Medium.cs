using System.Diagnostics;
using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The Cluttered pack's medium map, "Crescent Bazaar": a bean-shaped walled market (smooth outline, one concave side),
/// mirrored about x = 0 (Blue in the west lobe, Red in the east). Checks the shape and its dent, the object counts
/// against density floors, that nothing was placed outside the outline, the hand-placed objectives, that every solid
/// prop has a mirrored twin, equal walks for both teams to every hill and the enemy flag, and a 16-player bot match.
/// </summary>
public static partial class SelfTest
{
    private const string ClutteredMediumMap = "Crescent Bazaar";

    static partial void CheckClutteredMedium()
    {
        var m = Maps.Find(ClutteredMediumMap);
        if (m == null)
        {
            Fail($"no \"{ClutteredMediumMap}\" map (Maps/Cluttered/02-CrescentBazaar.json)");
            return;
        }
        if (Maps.FindPack(ClutteredPack)?.Maps.Contains(m) != true)
            Fail($"{m.Name} isn't in the {ClutteredPack} pack");

        // ---- Shape: a smooth bean with a real dent (two inside points whose midpoint is outside) ----
        var b = m.Boundary;
        if (b == null || b.Spec.Shape != BoundaryShape.Smooth)
        {
            Fail($"{m.Name}: expected a \"smooth\" boundary (bean), got {(b == null ? "none" : b.Spec.Shape.ToString())}");
            return;
        }
        if (b.Spec.Points.Length is < 8 or > 14)
            Fail($"{m.Name}: {b.Spec.Points.Length} control points (a bean wants 8 to 14)");
        var dentSides = b.Contains(-28, -34) && b.Contains(28, -34);
        var dentGap = !b.Contains(0, -30);
        if (!dentSides || !dentGap)
            Fail($"{m.Name}: the outline has no concave side (both north lobes inside {dentSides}, the dent between them outside {dentGap})");
        if (m.Size != MapSize.Medium || m.MaxPlayers != 16 || m.MaxPlayersSetting != 16)
            Fail($"{m.Name}: {m.Size} with {m.MaxPlayers} players (expected Medium, \"maxPlayers\": 16)");

        // ---- Density ----
        var props = m.PropInstances;
        var solid = props.Count(p => p.Def.IsSolid);
        var breakable = props.Count(p => p.IsBreakable);
        var cosmetic = props.Count - solid;
        var boxes = Enumerable.Range(0, m.Solid.Count).Count(i => m.IsDrawnBox(i) && !m.IsTrunk(i));
        var plants = m.FoliageInstances.Length;
        ClutteredLine($"{m.Name}: {props.Count} props ({props.Count - m.ClutterStart} clutter): {solid} solid, {cosmetic} cosmetic, {breakable} breakable; "
            + $"{boxes} boxes, {m.TrunkCount} trees, {plants} plants, {m.MeshPlacements.Count} meshes, {b.SegmentCount} outline segments");
        if (solid < 250 || cosmetic < 600 || breakable < 150 || plants < 15000 || m.MeshPlacements.Count < 300 || m.TrunkCount < 20)
            Fail($"{m.Name}: not cluttered enough (solid props {solid}/250, cosmetic {cosmetic}/600, breakable {breakable}/150, plants {plants}/15000, "
                 + $"meshes {m.MeshPlacements.Count}/300, trees {m.TrunkCount}/20)");
        if (boxes > 400)
            Fail($"{m.Name}: {boxes} boxes (each is a draw call; keep it under 400 and use props and meshes)");

        // ---- Nothing outside the outline ----
        var outside = new List<string>();
        foreach (var p in props)
            if (!b.Contains(p.Position.X, p.Position.Z))
                outside.Add($"prop {p.Def.Name} ({p.Position.X:0.0},{p.Position.Z:0.0})");
        foreach (var f in m.FoliageInstances)
            if (!b.Contains(f.Position.X, f.Position.Z))
                outside.Add($"{f.Type} ({f.Position.X:0.0},{f.Position.Z:0.0})");
        foreach (var mp in m.MeshPlacements)
            if (!b.Contains(mp.Transform.Translation.X, mp.Transform.Translation.Z))
                outside.Add($"mesh {mp.Source} ({mp.Transform.Translation.X:0.0},{mp.Transform.Translation.Z:0.0})");
        for (var i = 0; i < m.Solid.Count; i++)
            if (m.IsDrawnBox(i) && !b.Contains(m.Solid[i].Center.X, m.Solid[i].Center.Z))
                outside.Add($"box ({m.Solid[i].Center.X:0.0},{m.Solid[i].Center.Z:0.0})");
        if (outside.Count > 0)
            Fail($"{m.Name}: {outside.Count} thing(s) outside the outline: {string.Join(", ", outside.Take(5))}");

        // ---- Spawns and objectives ----
        var o = m.Objectives;
        var authored = m.AuthoredSpawns >= 0 ? m.AuthoredSpawns : m.Spawns.Count;
        if (authored < 16)
            Fail($"{m.Name}: {authored} hand-placed spawns (16 players need 16)");
        if (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated)
            Fail($"{m.Name}: objectives should be hand-placed");
        if (o.TeamSpawns[0].Count < 8 || o.TeamSpawns[1].Count < 8 || o.Hills.Count != 3)
            Fail($"{m.Name}: {o.TeamSpawns[0].Count} + {o.TeamSpawns[1].Count} team spawns and {o.Hills.Count} hills (expected 8 + 8 and 3)");
        if (props.Skip(m.ClutterStart).Any(p => p.Def.IsSolid))
            Fail($"{m.Name}: Clutter placed solid props (solid cover is hand-placed in mirrored pairs; Clutter is for litter)");

        // Solid props keep off spawns, hill zones and flags.
        static float Dist(Box bx, Vector3 p)
        {
            var dx = MathF.Max(0, MathF.Max(bx.Min.X - p.X, p.X - bx.Max.X));
            var dz = MathF.Max(0, MathF.Max(bx.Min.Z - p.Z, p.Z - bx.Max.Z));
            return MathF.Sqrt(dx * dx + dz * dz);
        }
        var blocked = new List<string>();
        foreach (var p in props.Where(p => p.Def.IsSolid))
            for (var k = p.SolidStart; k < p.SolidStart + p.SolidCount; k++)
            {
                var bx = m.Solid[k];
                var why = m.Spawns.Any(s => Dist(bx, s) < 1f) ? "a spawn"
                    : o.Hills.Any(h => Dist(bx, h.Center) < h.Radius) ? "a hill zone"
                    : o.Flags.Any(f => Dist(bx, f) < 2f) ? "a flag" : null;
                if (why != null)
                {
                    blocked.Add($"{p.Def.Name} ({p.Position.X:0.0},{p.Position.Z:0.0}) on {why}");
                    break;
                }
            }
        if (blocked.Count > 0)
            Fail($"{m.Name}: {blocked.Count} solid prop(s) in the way: {string.Join("; ", blocked.Take(4))}");

        // ---- Fairness: every solid prop has a twin mirrored about x = 0 (same kind and size, yaw negated) ----
        var unmatched = new List<string>();
        var solidIdx = Enumerable.Range(0, props.Count).Where(i => props[i].Def.IsSolid).ToList();
        var used = new HashSet<int>();
        foreach (var i in solidIdx)
        {
            if (used.Contains(i))
                continue;
            var a = props[i];
            if (MathF.Abs(a.Position.X) < 0.05f)
            {
                used.Add(i); // on the mirror line: its own twin
                continue;
            }
            var twin = solidIdx.FirstOrDefault(j => j != i && !used.Contains(j) && props[j].Def == a.Def
                && MathF.Abs(props[j].Position.X + a.Position.X) < 0.05f && MathF.Abs(props[j].Position.Z - a.Position.Z) < 0.05f
                && MathF.Abs(props[j].Position.Y - a.Position.Y) < 0.05f && MathF.Abs(props[j].Scale - a.Scale) < 0.01f
                && MathF.Abs(MathF.IEEERemainder(props[j].Yaw + a.Yaw, MathF.Tau)) < 0.02f, -1);
            if (twin < 0)
                unmatched.Add($"{a.Def.Name} ({a.Position.X:0.0},{a.Position.Z:0.0})");
            else
            {
                used.Add(i);
                used.Add(twin);
            }
        }
        if (unmatched.Count > 0)
            Fail($"{m.Name}: {unmatched.Count} solid prop(s) without a mirrored twin: {string.Join(", ", unmatched.Take(5))}");

        // ---- Nav graph: everything reachable, and equal walks for both teams ----
        World.SetMap(m);
        for (var wait = 0; wait < 2000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        var g = NavGraph.Current;
        var search = new NavSearch();
        var path = new int[g.Nodes.Length];
        float Walk(Vector3 from, Vector3 to)
        {
            var n = search.Find(g, g.Nearest(from, needSight: false), g.Nearest(to, needSight: false), path, maxExpand: g.Nodes.Length);
            if (n == 0)
                return float.NaN;
            var len = 0f;
            for (var k = 1; k < n; k++)
                len += Vector3.Distance(g.Nodes[path[k - 1]], g.Nodes[path[k]]);
            return len;
        }
        // Average walk from a team's spawns to a point (every spawn counts, so the whole base is compared).
        float TeamWalk(int team, Vector3 to)
        {
            var sum = 0f;
            foreach (var s in o.TeamSpawns[team])
                sum += Walk(s, to);
            return sum / o.TeamSpawns[team].Count;
        }
        var walks = new List<string>();
        void Compare(string what, float blue, float red)
        {
            walks.Add($"{what} {blue:0}/{red:0} m");
            if (!float.IsFinite(blue) || !float.IsFinite(red))
                Fail($"{m.Name}: no route to {what} (blue {blue:0}, red {red:0})");
            else if (MathF.Abs(blue - red) > 0.1f * MathF.Max(blue, red))
                Fail($"{m.Name}: unfair walk to {what}: blue {blue:0.0} m, red {red:0.0} m (over 10% apart)");
        }
        for (var h = 0; h < o.Hills.Count; h++)
            Compare($"hill {h + 1}", TeamWalk(0, o.Hills[h].Center), TeamWalk(1, o.Hills[h].Center));
        Compare("enemy flag", TeamWalk(0, o.Flags[1]), TeamWalk(1, o.Flags[0]));
        ClutteredLine($"{m.Name}: nav {g.Nodes.Length} nodes, walks blue/red: {string.Join(", ", walks)}");
        var unreachable = m.Spawns.Concat(o.Hills.Select(h => h.Center)).Concat(o.Flags)
            .Count(p => g.Nearest(p, needSight: false) is var k && (k < 0 || g.Component[k] != g.MainComponent));
        if (unreachable > 0)
            Fail($"{m.Name}: {unreachable} spawn(s) or objective(s) off the main nav area");

        ClutteredMediumBotMatch(m);
    }

    /// <summary>A full 16-player free-for-all with bots for 60 s: kills, nobody stuck, outside or below the floor, ticks in budget.</summary>
    private static void ClutteredMediumBotMatch(Map map)
    {
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: map.MaxPlayers - 1, botSkill: BotDifficulty.Normal, mapId: map.Id);
        game.MapVoteEnabled = false;
        Game.SettleMemory();
        const double dt = 1.0 / World.TickRate;
        var now = 0.0;
        var moved = new Dictionary<byte, float>();
        var last = new Dictionary<byte, Vector3>();
        int outside = 0, fallen = 0, kills = 0, lastKills = 0;
        var lastMatch = game.MatchNumber;
        var cpu = new TickCpu();
        var b = map.Boundary!;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 60 * World.TickRate; i++)
        {
            now += dt;
            cpu.Start();
            var t0 = Stopwatch.GetTimestamp();
            game.Update(now, dt);
            var tick = i;
            cpu.Stop(Stopwatch.GetElapsedTime(t0).TotalMilliseconds, count: i > 60, () => $"t={tick / 60.0:0.00} s, {game.Phase}");
            if (game.MatchNumber != lastMatch)
            {
                kills += lastKills;
                lastMatch = game.MatchNumber;
            }
            lastKills = game.Players.Values.Sum(p => (int)p.Kills);
            foreach (var p in game.Players.Values)
            {
                if (!p.Alive)
                    continue;
                if (!float.IsFinite(p.Position.X + p.Position.Y + p.Position.Z))
                {
                    Fail($"{map.Name}: player {p.Id} position is NaN");
                    return;
                }
                if (b.SignedDistance(p.Position.X, p.Position.Z) > -World.PlayerRadius + 0.01f)
                    outside++;
                if (p.Position.Y < -0.5f)
                    fallen++;
            }
            if (i % 60 == 0)
                foreach (var p in game.Players.Values.Where(p => p.Bot != null && p.Alive))
                {
                    if (last.TryGetValue(p.Id, out var lp))
                        moved[p.Id] = moved.GetValueOrDefault(p.Id) + Vector3.Distance(lp, p.Position);
                    last[p.Id] = p.Position;
                }
        }
        kills += lastKills;
        var stuck = moved.Count(kv => kv.Value < 15f);
        ClutteredLine($"{map.Name}: bot match (60 s, {game.Players.Count} players): kills {kills}, avg bot travel {(moved.Count == 0 ? 0 : moved.Values.Average()):0} m, "
            + $"stuck {stuck}, player-ticks outside {outside}, below the floor {fallen}, worst tick {cpu.WorstText} ({sw.Elapsed.TotalSeconds:0.0} s)");
        if (game.Players.Count != map.MaxPlayers)
            Fail($"{map.Name}: {game.Players.Count} players in the bot match (expected {map.MaxPlayers})");
        if (kills == 0)
            Fail($"{map.Name}: no kills in 60 s with {game.Players.Count - 1} bots");
        if (stuck > 1)
            Fail($"{map.Name}: {stuck} bots barely moved (stuck in the clutter?)");
        if (outside > 0 || fallen > 0)
            Fail($"{map.Name}: players left the map ({outside} player-ticks outside the outline, {fallen} below the floor)");
        if (cpu.WorstMs > MaxTickWorkMs)
            Fail($"{map.Name}: a server tick did {cpu.WorstMs:0.0} ms of work (over {MaxTickWorkMs} ms; {cpu.WorstDetail})");
    }
}
