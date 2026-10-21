using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "Cluttered pack" self-test section: the three maps in Maps/Cluttered (small circle, medium bean, large irregular
/// sprawl), each packed with props and foliage. Each map has its own method (<c>CheckClutteredSmall</c>,
/// <c>CheckClutteredMedium</c>, <c>CheckClutteredLarge</c>, in their own files); the shared helpers below check what
/// every map in the pack must have: a boundary of the assigned shape (never a rectangle), object counts over a
/// density threshold, everything inside the outline, spawns and objectives, and a bot match.
/// </summary>
public static partial class SelfTest
{
    private const string ClutteredPack = "Cluttered";

    private static readonly List<string> ClutteredNotes = new();

    /// <summary>A result line, also collected for the section's GitHub notice.</summary>
    private static void ClutteredLine(string text)
    {
        Line(text);
        ClutteredNotes.Add(text);
    }

    private static void CheckCluttered()
    {
        if (!Directory.Exists(Maps.Folder))
            return;
        if (Maps.FindPack(ClutteredPack) == null)
        {
            Fail($"no {ClutteredPack} pack (Maps/Cluttered)");
            return;
        }
        ClutteredNotes.Clear();
        CheckClutteredSmall();
        CheckClutteredMedium();
        CheckClutteredLarge();
        for (int i = 0, part = 1; i < ClutteredNotes.Count; i += 8)
            Line($"::notice title=Cluttered pack {part++}::" + string.Join("%0A", ClutteredNotes.Skip(i).Take(8).Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        World.SetMap(Maps.All[0]);
    }

    static partial void CheckClutteredSmall();

    static partial void CheckClutteredMedium();

    static partial void CheckClutteredLarge();

    /// <summary>
    /// Checks shared by the pack's maps: the boundary shape, the player limit and hand-placed spawns for it, hand-placed
    /// objectives (with level hill zones and no solid prop on a spawn, hill or flag), density thresholds and that every
    /// spawn, waypoint, prop, plant and objective is inside the outline. Returns false if the map can't be checked further.
    /// </summary>
    private static bool CheckClutteredBasics(Map m, BoundaryShape shape, int maxPlayers, int minProps, int minSolidProps, int minPlants)
    {
        var b = m.Boundary;
        if (b == null || b.Spec.Shape == BoundaryShape.Rect)
        {
            Fail($"{m.Name}: needs a {shape} boundary, has {(b == null ? "the plain rectangle" : "a rect boundary")}");
            return false;
        }
        if (b.Spec.Shape != shape)
            Fail($"{m.Name}: boundary is {b.Spec.Shape}, expected {shape}");
        if (m.MaxPlayers != maxPlayers || m.MaxPlayersSetting != maxPlayers)
            Fail($"{m.Name}: player limit {m.MaxPlayers} (file {m.MaxPlayersSetting}), expected {maxPlayers}");
        var authored = m.AuthoredSpawns >= 0 ? m.AuthoredSpawns : m.Spawns.Count;
        if (authored < maxPlayers)
            Fail($"{m.Name}: {authored} hand-placed spawns for {maxPlayers} players");
        var o = m.Objectives;
        if (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated)
            Fail($"{m.Name}: objectives should be hand-placed (team spawns {!o.TeamSpawnsGenerated}, hills {!o.HillsGenerated}, flags {!o.FlagsGenerated})");

        var props = m.PropInstances;
        var solid = props.Count(p => p.Def.IsSolid);
        var plants = m.FoliageInstances.Length;
        if (props.Count < minProps || solid < minSolidProps || plants < minPlants)
            Fail($"{m.Name}: not cluttered enough ({props.Count} props, {solid} solid, {plants} plants; expected at least {minProps}, {minSolidProps}, {minPlants})");

        // Everything inside the outline (the cosmetic scenery meshes beyond the edge are deliberate, so they don't count).
        var outside = new List<string>();
        void In(string what, float x, float z)
        {
            if (!b.Inside(x, z))
                outside.Add($"{what} ({x:0.0},{z:0.0})");
        }
        foreach (var s in m.Spawns)
            In("spawn", s.X, s.Z);
        foreach (var w in m.Waypoints)
            In("waypoint", w.X, w.Z);
        foreach (var p in props)
            In(p.Def.Name, p.Position.X, p.Position.Z);
        foreach (var f in m.FoliageInstances)
            In(f.Type.ToString(), f.Position.X, f.Position.Z);
        foreach (var team in o.TeamSpawns)
            foreach (var s in team)
                In("team spawn", s.X, s.Z);
        foreach (var h in o.Hills)
            In("hill", h.Center.X, h.Center.Z);
        foreach (var f in o.Flags)
            In("flag", f.X, f.Z);
        if (outside.Count > 0)
            Fail($"{m.Name}: {outside.Count} thing(s) outside the outline: {string.Join(", ", outside.Take(5))}");

        // Hill zones on level ground with no solid prop or box in them; nothing solid on a spawn or a flag.
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
        static float Dist(Box bx, Vector3 p)
        {
            var dx = MathF.Max(0, MathF.Max(bx.Min.X - p.X, p.X - bx.Max.X));
            var dz = MathF.Max(0, MathF.Max(bx.Min.Z - p.Z, p.Z - bx.Max.Z));
            return MathF.Sqrt(dx * dx + dz * dz);
        }
        var blocked = new List<string>();
        for (var i = 0; i < m.Solid.Count; i++)
        {
            if (m.Boundary != null && i == m.GroundIndex)
                continue;
            var bx = m.Solid[i];
            string? why = null;
            foreach (var sp in m.Spawns)
                if (why == null && Dist(bx, sp) < 0.5f && bx.Max.Y > sp.Y + 0.6f && bx.Min.Y < sp.Y + World.PlayerHeight)
                    why = $"spawn ({sp.X:0.0},{sp.Z:0.0})";
            foreach (var h in o.Hills)
                if (why == null && Dist(bx, h.Center) < h.Radius && bx.Max.Y > h.Center.Y - 1f + World.StepHeight && bx.Min.Y < h.Center.Y + 3f)
                    why = $"hill zone ({h.Center.X:0},{h.Center.Z:0})";
            foreach (var f in o.Flags)
                if (why == null && Dist(bx, f) < 1.5f && bx.Max.Y > f.Y + 0.6f)
                    why = $"flag ({f.X:0},{f.Z:0})";
            if (why != null)
                blocked.Add($"{(m.IsPropCollider(i) ? "prop" : "box")} at ({bx.Center.X:0.0},{bx.Center.Z:0.0}) on the {why}");
        }
        if (blocked.Count > 0)
            Fail($"{m.Name}: {blocked.Count} solid thing(s) in the way: {string.Join("; ", blocked.Take(4))}");
        return true;
    }

    /// <summary>Waits for the map's nav graph (built in the background) and returns it.</summary>
    private static NavGraph ClutteredNav(Map m)
    {
        World.SetMap(m);
        for (var wait = 0; wait < 2000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        return NavGraph.Current;
    }

    /// <summary>
    /// A bot match on the map with <paramref name="players"/> players (host + bots) for <paramref name="seconds"/>:
    /// nobody stuck, outside the outline or under the ground, and no server tick over the CPU budget. Returns the kills.
    /// </summary>
    private static int ClutteredBotMatch(Map m, int players, double seconds, bool needKills)
    {
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: players - 1, botSkill: BotDifficulty.Normal, mapId: m.Id);
        game.MapVoteEnabled = false;
        Game.SettleMemory();
        const double dt = 1.0 / World.TickRate;
        var now = 0.0;
        var moved = new Dictionary<byte, float>();
        var last = new Dictionary<byte, Vector3>();
        int outside = 0, fallen = 0, kills = 0, lastKills = 0, lastMatch = game.MatchNumber;
        var cpu = new TickCpu();
        var wall = new List<double>();
        var b = m.Boundary;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < seconds * World.TickRate; i++)
        {
            now += dt;
            cpu.Start();
            var t0 = Stopwatch.GetTimestamp();
            game.Update(now, dt);
            var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            var tick = i;
            cpu.Stop(ms, count: i > 60, () => $"t={tick / 60.0:0.00} s, {game.Phase}");
            if (i > 60)
                wall.Add(ms);
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
                    Fail($"{m.Name}: player {p.Id} position is NaN");
                    return kills;
                }
                if (b != null && b.SignedDistance(p.Position.X, p.Position.Z) > -World.PlayerRadius + 0.01f)
                    outside++;
                if (p.Position.Y < m.GroundHeight(p.Position.X, p.Position.Z) - 0.5f)
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
        wall.Sort();
        var stuck = moved.Count(kv => kv.Value < 15f);
        ClutteredLine($"bot match on {m.Name} ({seconds:0} s, {game.Players.Count} players): kills {kills}, avg bot travel {(moved.Count == 0 ? 0 : moved.Values.Average()):0} m, "
            + $"stuck {stuck}, player-ticks outside {outside}, below ground {fallen}, tick wall median {(wall.Count > 0 ? wall[wall.Count / 2] : 0):0.00} ms, "
            + $"p99 {(wall.Count > 0 ? wall[(int)(wall.Count * 0.99)] : 0):0.00} ms, worst work {cpu.WorstText} ({sw.Elapsed.TotalSeconds:0.0} s)");
        if (game.Players.Count != players)
            Fail($"{m.Name}: {game.Players.Count} players in the bot match, expected {players}");
        if (stuck > Math.Max(1, players / 16))
            Fail($"{m.Name}: {stuck} bots barely moved (stuck on clutter?)");
        if (outside > 0 || fallen > 0)
            Fail($"{m.Name}: players left the map ({outside} player-ticks outside the outline, {fallen} below the ground)");
        if (needKills && kills == 0)
            Fail($"{m.Name}: no kills in {seconds:0} s with {players - 1} bots");
        if (cpu.WorstMs > MaxTickWorkMs)
            Fail($"{m.Name}: a server tick with {game.Players.Count} players did {cpu.WorstMs:0.0} ms of work (over {MaxTickWorkMs} ms; {cpu.WorstDetail})");
        return kills;
    }
}
