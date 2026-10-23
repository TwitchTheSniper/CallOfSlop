using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>The sky station's platforms (name, centre, deck height): every one must be on the bots' nav graph.</summary>
    private static readonly (string Name, float X, float Z, float Y)[] StationPlatforms =
    [
        ("North Dock", 0, -86, 30f), ("South Dock", 0, 86, 30f), ("Refinery N", 0, -56, 28f), ("Refinery S", 0, 56, 28f),
        ("Quarters N", -40, -70, 29f), ("Quarters S", -40, 70, 29f), ("Tank Farm N", -52, -40, 27.5f), ("Tank Farm S", -52, 40, 27.5f),
        ("Hangar N", 42, -72, 29.5f), ("Hangar S", 42, 72, 29.5f), ("Comms N", 50.6f, -48.5f, 27.5f), ("Comms S", 50.6f, 48.5f, 27.5f),
        ("Hub ring", 0, -15.5f, 26f), ("Hub core", 3, 0, 26f), ("West Pad", -47.5f, 0, 25f), ("East Pad", 47.5f, 0, 25f),
    ];

    /// <summary>
    /// Space pack, 01 Stratos Station (Maps/Space/01-SkyStation.json): a void map (fallKill, no ground) with a shaped
    /// outline, hand-placed objectives and real elevation; bots reach every platform from both teams' spawns; no nav
    /// link crosses the void (there's floor under every step of every link); the fall kill works (dropped into the hub's
    /// eye, you die, and standing on a deck you don't); the walks to the hills and the enemy flag are fair (within 10%);
    /// hills and flags stand on level deck; and what it all costs, as one CI notice. No bot matches.
    /// </summary>
    private static void CheckSpaceStation()
    {
        if (Maps.Find("Stratos Station") is not { } m)
        {
            Fail("Maps/Space/01-SkyStation.json ('Stratos Station') didn't load");
            return;
        }
        if (m.FallKillY is not { } killY || m.Boundary == null || m.Terrain != null || m.GroundIndex >= 0)
        {
            Fail("Stratos Station should be a void map: fallKill, an outline, no terrain and no ground slab");
            return;
        }
        if (Maps.All.Count(x => x.FallKillY != null) != 1)
            Line($"  note: {Maps.All.Count(x => x.FallKillY != null)} maps use fallKill");
        if (Maps.Find("Proto Box") is { FallKillY: not null })
            Fail("a map without \"fallKill\" got a fall-kill height");
        var o = m.Objectives;
        if (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated)
            Fail("Stratos Station: team spawns, hills and flags should all be hand-placed");
        if (o.Hills.Any(h => MathF.Abs(h.Center.Z) > 0.05f))
            Fail("Stratos Station: the hills should be on the mirror line z = 0");

        World.SetMap(m);
        for (var wait = 0; wait < 4000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        var g = NavGraph.Current;
        var phys = World.Physics;
        float Floor(float x, float z, float fromY, float depth = 3f)
        {
            var t = phys.Raycast(new Vector3(x, fromY, z), -Vector3.UnitY, depth, QueryFilter.Navigation);
            return t >= depth ? float.NaN : fromY - t;
        }

        // Elevation: decks from 25 to 30 m, the void below.
        var heights = StationPlatforms.Select(p => Floor(p.X, p.Z, p.Y + 0.5f)).ToArray();
        var bad = StationPlatforms.Where((p, i) => !(MathF.Abs(heights[i] - p.Y) < 0.1f)).Select(p => p.Name).ToList();
        if (bad.Count > 0)
            Fail("Stratos Station: no deck where expected at " + string.Join(", ", bad));
        if (heights.Where(float.IsFinite).DefaultIfEmpty(0).Max() - heights.Where(float.IsFinite).DefaultIfEmpty(0).Min() < 4f)
            Fail("Stratos Station: the decks should span at least 4 m of height");
        if (float.IsFinite(Floor(6.5f, 6.5f, 30f, 40f)))
            Fail("Stratos Station: the hub's eye should be open to the void");

        // Every platform reachable by bots from both teams' spawns.
        var search = new NavSearch();
        var path = new int[4096];
        float Walk(Vector3 a, Vector3 b)
        {
            var n = search.Find(g, g.Nearest(a, needSight: false), g.Nearest(b, needSight: false), path);
            if (n == 0)
                return float.NaN;
            var len = 0f;
            for (var i = 1; i < n; i++)
                len += Vector3.Distance(g.Nodes[path[i - 1]], g.Nodes[path[i]]);
            return len;
        }
        var unreached = new List<string>();
        foreach (var p in StationPlatforms)
        {
            var at = new Vector3(p.X, p.Y, p.Z);
            var k = g.Nearest(at, needSight: false);
            if (k < 0 || Vector3.Distance(g.Nodes[k], at) > 3f)
            {
                unreached.Add(p.Name + " (no node)");
                continue;
            }
            for (var t = 0; t < 2; t++)
                if (float.IsNaN(Walk(o.TeamSpawns[t][0], at)))
                    unreached.Add($"{p.Name} from {(t == 0 ? "Blue" : "Red")} (node ({g.Nodes[k].X:0.0}, {g.Nodes[k].Y:0.00}, {g.Nodes[k].Z:0.0}), "
                                  + $"component {g.Component[k]} of main {g.MainComponent}, {g.EdgeStart[k + 1] - g.EdgeStart[k]} links)");
        }
        if (unreached.Count > 0)
            Fail("Stratos Station: bots can't reach " + string.Join(", ", unreached));
        var notMain = StationPlatforms.Count(p => g.Nearest(new Vector3(p.X, p.Y, p.Z), needSight: false) is var k && k >= 0 && g.Component[k] != g.MainComponent);

        // No link over the void: floor under every 0.5 m of every link (within a drop bots would plan).
        int links = 0, voidLinks = 0;
        var example = "";
        for (var i = 0; i < g.Nodes.Length; i++)
            for (var e = g.EdgeStart[i]; e < g.EdgeStart[i + 1]; e++)
            {
                var j = g.EdgeTo[e];
                if (j < i)
                    continue;
                links++;
                Vector3 a = g.Nodes[i], b = g.Nodes[j];
                var top = MathF.Max(a.Y, b.Y) + World.MantleReach + 0.05f;
                var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z)) / 0.5f));
                for (var s = 0; s <= steps; s++)
                {
                    var q = Vector3.Lerp(a, b, s / (float)steps);
                    if (!float.IsNaN(Floor(q.X, q.Z, top, World.MantleReach + 0.05f + NavGraph.MaxDrop)))
                        continue;
                    if (voidLinks++ == 0)
                        example = $"({a.X:0.0},{a.Y:0.0},{a.Z:0.0}) -> ({b.X:0.0},{b.Y:0.0},{b.Z:0.0}) at ({q.X:0.0},{q.Z:0.0})";
                    break;
                }
            }
        if (voidLinks > 0)
            Fail($"Stratos Station: {voidLinks} of {links} nav links cross the void, e.g. {example}");
        var lowNodes = g.Nodes.Count(n => n.Y < killY + 1f);
        if (lowNodes > 0)
            Fail($"Stratos Station: {lowNodes} nav nodes below the fall-kill height");

        // Objectives on level deck: the hills' and flags' floor within 5 cm of level across them.
        foreach (var h in o.Hills)
            foreach (var (dx, dz) in new[] { (0f, 0f), (0.7f, 0f), (-0.7f, 0f), (0f, 0.7f), (0f, -0.7f) })
            {
                var y = Floor(h.Center.X + dx * h.Radius, h.Center.Z + dz * h.Radius, h.Center.Y + 0.6f, 1.2f);
                if (!(MathF.Abs(y - h.Center.Y) < 0.05f))
                {
                    Fail($"Stratos Station: the hill at ({h.Center.X:0}, {h.Center.Z:0}) isn't on level deck ({y:0.00} at {dx},{dz})");
                    break;
                }
            }
        foreach (var f in o.Flags)
            if (!(MathF.Abs(Floor(f.X, f.Z, f.Y + 0.6f, 1.2f) - f.Y) < 0.05f) || World.Physics.BlockedNav(f + new Vector3(0, 0.05f, 0)))
                Fail($"Stratos Station: the flag at ({f.X:0}, {f.Z:0}) isn't on open deck");

        // Fair walks: each hill and the enemy flag about as far for both teams.
        float Average(List<Vector3> from, Vector3 to)
        {
            var sum = 0f;
            foreach (var s in from)
            {
                var w = Walk(s, to);
                if (float.IsNaN(w))
                    return float.NaN;
                sum += w;
            }
            return sum / Math.Max(1, from.Count);
        }
        var walks = new List<string>();
        var targets = o.Hills.Select((h, i) => ($"hill {i + 1}", h.Center, h.Center)).ToList();
        targets.Add(("enemy flag", o.Flags[1], o.Flags[0]));
        foreach (var (label, forBlue, forRed) in targets)
        {
            float blue = Average(o.TeamSpawns[0], forBlue), red = Average(o.TeamSpawns[1], forRed);
            walks.Add($"{label} {blue:0}/{red:0} m");
            if (float.IsNaN(blue) || float.IsNaN(red))
                Fail($"Stratos Station: no route from a team's spawns to {label}");
            else if (MathF.Abs(blue - red) > 0.1f * MathF.Max(blue, red))
                Fail($"Stratos Station: {label} is {blue:0} m from Blue and {red:0} m from Red (more than 10% apart)");
        }

        // The fall kill: dropped into the hub's eye you die; standing on a deck you don't.
        var id = (byte)Array.IndexOf(Maps.All, m);
        var now = 0.0;
        using (var game = ModeGame(0, GameMode.FreeForAll, id))
        {
            game.WarmupEnabled = true;
            Run(game, ref now, 1);
            game.ChooseLoadout(Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, ThrowableKind.None, ThrowableKind.None, 0, 0, now);
            Run(game, ref now, Game.CountdownSeconds + 0.3);
            var me = game.Local;
            if (me is not { Alive: true } || game.Phase != MatchPhase.Playing || World.Map != m)
                Fail("Stratos Station: the fall-kill game didn't start");
            else
            {
                Run(game, ref now, 2);
                var safe = me.Alive && me.Position.Y > killY;
                var deaths = me.Deaths;
                me.ImmuneUntil = 0;
                me.Position = new Vector3(6.5f, 27f, 6.5f);
                me.Sim.Velocity = Vector3.Zero;
                me.Sim.Grounded = false;
                var fell = 0.0;
                for (var i = 0; i < 4 * World.TickRate && me.Alive; i++)
                {
                    Run(game, ref now, 1.0 / World.TickRate);
                    fell += 1.0 / World.TickRate;
                }
                Line($"fall kill: standing on a deck {(safe ? "alive" : "DEAD")}; dropped into the hub's eye: {(me.Alive ? "still alive" : $"died after {fell:0.00} s")}, "
                     + $"deaths {deaths}->{me.Deaths}, fall kills {game.FallKills}, kill height {killY:0.0}");
                if (!safe)
                    Fail("Stratos Station: a player standing on a deck died (or fell)");
                if (me.Alive || me.Deaths != deaths + 1 || game.FallKills < 1)
                    Fail("Stratos Station: falling into the void didn't kill");
            }
        }
        World.SetMap(m);

        var drawn = Enumerable.Range(0, m.Solid.Count).Count(m.IsDrawnBox);
        var shell = m.PlacedBuildings.Sum(b => b.ShellCount);
        var mesh = DecalBaker.Bake(m);
        var summary = $"Stratos Station: {m.Size} {m.HalfX * 2:0} x {m.HalfZ * 2:0} m ({m.PlayArea:0} m2 inside the outline), max {m.WantedMaxPlayers} players; "
                      + $"{StationPlatforms.Length} platforms at {heights.Where(float.IsFinite).DefaultIfEmpty(0).Min():0.0}-{heights.Where(float.IsFinite).DefaultIfEmpty(0).Max():0.0} m, fall kill below {killY:0.0} m; "
                      + $"{m.Solid.Count} boxes ({m.HiddenBoxCount} hidden colliders, {shell} building shell, {drawn} drawn one by one); {m.PlacedBuildings.Count} buildings; "
                      + $"{m.PropInstances.Count} props ({m.PropInstances.Count(p => p.Def.IsSolid)} solid); {m.MeshPlacements.Count} meshes; "
                      + $"decals {mesh.Placed}/{mesh.Requested} placed, {mesh.Triangles} triangles; {m.Spawns.Count} spawns; "
                      + $"nav {g.Nodes.Length} nodes, {links} links ({voidLinks} over the void), {notMain} platforms off the main area; walks Blue/Red: " + string.Join(", ", walks);
        Line(summary);
        Line("::notice title=Stratos Station (results)::" + summary.Replace("%", "%25"));
        if (notMain > 0)
            Fail($"Stratos Station: {notMain} platforms aren't in the nav graph's main area");
        if (mesh.Placed < mesh.Requested * 0.8)
            Fail($"Stratos Station: only {mesh.Placed}/{mesh.Requested} decals placed");
        if (drawn > 60)
            Fail($"Stratos Station: {drawn} boxes drawn one by one (keep them under 60: decks are meshes over hidden colliders)");
    }
}
