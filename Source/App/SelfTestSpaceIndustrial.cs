using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Space pack, 09 Hollowrock Refinery (Maps/Space/09-Hollowrock.json) and 10 Redvane Processor
    /// (Maps/Space/10-Redvane.json): both point-symmetric industrial maps with a shaped outline. For each: the outline
    /// isn't a rectangle, the objectives are hand-placed and inside the outline, the spawns are a mirrored pair of
    /// teams, the dressing is dense (props and decals above a floor), bots reach every hill and the enemy flag from
    /// both teams with fair walks (within 12%), and a short seeded FFA bot match runs without a crash, anyone outside
    /// the outline or a slow tick. Hollowrock also checks its fall kill (dropped into a chasm you die; in a drift you
    /// don't) and that no nav node sits below the kill height. No long bot matches.
    /// </summary>
    private static void CheckSpaceIndustrial()
    {
        var notes = new List<string>();
        SpaceIndustrialMap("Hollowrock Refinery", "Maps/Space/09-Hollowrock.json", BoundaryShape.Smooth, minProps: 220, minDecals: 150, notes,
            fallAt: new Vector3(-24f, 2f, -10f), safeAt: new Vector3(-38f, -2.5f, 8f));
        SpaceIndustrialMap("Redvane Processor", "Maps/Space/10-Redvane.json", BoundaryShape.Ellipse, minProps: 260, minDecals: 150, notes,
            fallAt: null, safeAt: null);
        World.SetMap(Maps.All[0]);
        if (notes.Count > 0)
            Line("::notice title=Space: refinery and processor::" + string.Join("%0A", notes.Select(n => n.Replace("%", "%25"))));
    }

    private static void SpaceIndustrialMap(string name, string file, BoundaryShape shape, int minProps, int minDecals, List<string> notes,
        Vector3? fallAt, Vector3? safeAt)
    {
        if (Maps.Find(name) is not { } m)
        {
            Fail($"{file} ('{name}') didn't load");
            return;
        }
        if (m.Boundary == null || m.Boundary.Spec.Shape != shape)
        {
            Fail($"{name}: expected a {shape} outline, got {(m.Boundary == null ? "a rectangle" : m.Boundary.Spec.Shape.ToString())}");
            return;
        }
        var o = m.Objectives;
        if (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated)
            Fail($"{name}: team spawns, hills and flags should all be hand-placed");
        if (o.Hills.Count < 3 || o.Flags.Length != 2)
            Fail($"{name}: expected 3 hills and 2 flags, got {o.Hills.Count} and {o.Flags.Length}");
        foreach (var h in o.Hills)
            if (!m.Boundary.Inside(h.Center.X, h.Center.Z, -h.Radius))
                Fail($"{name}: the hill at ({h.Center.X:0}, {h.Center.Z:0}) reaches outside the outline");
        foreach (var f in o.Flags)
            if (!m.Boundary.Inside(f.X, f.Z, -2f))
                Fail($"{name}: the flag at ({f.X:0}, {f.Z:0}) is outside the outline");
        // Point symmetry: Blue's spawns are Red's turned 180 degrees about the centre (the odd one nudged off a slope).
        if (o.TeamSpawns[0].Count != o.TeamSpawns[1].Count || o.TeamSpawns[0].Count < 8)
            Fail($"{name}: teams have {o.TeamSpawns[0].Count} and {o.TeamSpawns[1].Count} spawns (need the same number, at least 8)");
        var unpaired = o.TeamSpawns[0].Count(b => !o.TeamSpawns[1].Any(r => Vector2.Distance(new Vector2(-b.X, -b.Z), new Vector2(r.X, r.Z)) < 1.5f));
        if (unpaired > 1)
            Fail($"{name}: {unpaired} Blue spawns have no Red twin at (-x, -z)");
        if (m.FallKillY is { } kill && m.Spawns.Any(s => s.Y < kill + 0.5f))
            Fail($"{name}: a spawn sits below the fall-kill height");

        // Dressing.
        var props = m.PropInstances;
        var baked = DecalBaker.Bake(m);
        if (props.Count < minProps)
            Fail($"{name}: only {props.Count} props (expected at least {minProps})");
        if (baked.Placed < minDecals || baked.Placed < baked.Requested * 0.75)
            Fail($"{name}: only {baked.Placed}/{baked.Requested} decals placed (expected at least {minDecals} and 75%)");

        // Nav: every hill and the enemy flag from both teams, fair walks.
        World.SetMap(m);
        for (var wait = 0; wait < 4000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        var g = NavGraph.Current;
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
        // A hill is a zone, not a point: the walk to it is the shortest walk to any of five spots across it.
        float WalkTo(Vector3 s, Vector3 to, float radius)
        {
            var best = Walk(s, to);
            foreach (var (dx, dz) in new[] { (0.7f, 0f), (-0.7f, 0f), (0f, 0.7f), (0f, -0.7f) })
            {
                var w = Walk(s, to + new Vector3(dx * radius, 0, dz * radius));
                if (!float.IsNaN(w) && (float.IsNaN(best) || w < best))
                    best = w;
            }
            return best;
        }
        float Average(List<Vector3> from, Vector3 to, float radius = 0f)
        {
            var sum = 0f;
            foreach (var s in from)
            {
                var w = radius > 0 ? WalkTo(s, to, radius) : Walk(s, to);
                if (float.IsNaN(w))
                    return float.NaN;
                sum += w;
            }
            return sum / Math.Max(1, from.Count);
        }
        var walks = new List<string>();
        var targets = o.Hills.Select((h, i) => ($"hill {i + 1}", h.Center, h.Center, h.Radius)).ToList();
        targets.Add(("enemy flag", o.Flags[1], o.Flags[0], 0f));
        foreach (var (label, forBlue, forRed, radius) in targets)
        {
            float blue = Average(o.TeamSpawns[0], forBlue, radius), red = Average(o.TeamSpawns[1], forRed, radius);
            walks.Add($"{label} {blue:0}/{red:0} m");
            if (float.IsNaN(blue) || float.IsNaN(red))
                Fail($"{name}: no route from a team's spawns to {label}: " + string.Join(", ",
                    o.TeamSpawns.SelectMany((l, t) => l.Where(sp => float.IsNaN(Walk(sp, t == 0 ? forBlue : forRed)))
                        .Select(sp => g.Nearest(sp, needSight: false) is var k && k >= 0
                            ? $"({sp.X:0.0},{sp.Y:0.0},{sp.Z:0.0}) node ({g.Nodes[k].X:0.0},{g.Nodes[k].Y:0.00},{g.Nodes[k].Z:0.0}) c{g.Component[k]}/{g.MainComponent}"
                            : $"({sp.X:0.0},{sp.Y:0.0},{sp.Z:0.0}) no node"))));
            else if (MathF.Abs(blue - red) > 0.12f * MathF.Max(blue, red))
                Fail($"{name}: {label} is {blue:0} m from Blue and {red:0} m from Red (more than 12% apart)");
        }
        // No nav link leads down below the fall-kill height (a chasm floor may hold nodes, but nothing plans a route into it).
        var intoVoid = 0;
        if (m.FallKillY is { } killY2)
            for (var i = 0; i < g.Nodes.Length; i++)
                for (var e = g.EdgeStart[i]; e < g.EdgeStart[i + 1]; e++)
                    if (g.Nodes[i].Y >= killY2 && g.Nodes[g.EdgeTo[e]].Y < killY2)
                        intoVoid++;
        if (intoVoid > 0)
            Fail($"{name}: {intoVoid} nav links lead below the fall-kill height");

        // A short seeded FFA bot match: nobody leaves the outline, no slow tick, no crash.
        var id = (byte)Array.IndexOf(Maps.All, m);
        var now = 0.0;
        var fallText = "";
        var sw = Stopwatch.StartNew();
        using (var game = ModeGame(7, GameMode.FreeForAll, id))
        {
            Game.SettleMemory();
            var cpu = new TickCpu();
            const double dt = 1.0 / World.TickRate;
            try
            {
                for (var i = 0; i < 8 * World.TickRate; i++)
                {
                    now += dt;
                    var jit0 = System.Runtime.JitInfo.GetCompilationTime(currentThread: true);
                    cpu.Start();
                    var t0 = Stopwatch.GetTimestamp();
                    game.Update(now, dt);
                    var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                    var jit = (System.Runtime.JitInfo.GetCompilationTime(currentThread: true) - jit0).TotalMilliseconds > 1;
                    cpu.Stop(ms, count: i > 60 && !jit);
                }
            }
            catch (Exception ex)
            {
                Fail($"{name}: FFA bot match crashed: {ex}");
                return;
            }
            var escaped = game.Players.Values.Count(p => p.Alive && !m.Boundary.Inside(p.Position.X, p.Position.Z, -0.5f));
            var below = m.FallKillY is { } k2 ? game.Players.Values.Count(p => p.Alive && p.Position.Y < k2) : 0;
            if (game.Players.Count != 8)
                Fail($"{name}: the FFA match has {game.Players.Count} players (expected 8)");
            if (escaped > 0 || below > 0)
                Fail($"{name}: {escaped} player(s) outside the outline, {below} below the kill height after the bot match");
            if (cpu.WorstMs > MaxTickWorkMs)
                Fail($"{name}: a server tick did {cpu.WorstMs:0.0} ms of work (over {MaxTickWorkMs} ms; {cpu.WorstDetail})");
            notes.Add($"{name}: 8 s FFA with 7 bots, kills {game.Players.Values.Sum(p => (int)p.Kills)}, fall kills {game.FallKills}, worst tick work {cpu.WorstText} ({sw.Elapsed.TotalSeconds:0.0} s)");

        }
        // The fall kill, in a game of its own (the host doesn't respawn without a click): dropped into a chasm you
        // die; standing in a drift (below the ground, above the kill height) you don't.
        if (fallAt is { } drop && safeAt is { } safe && m.FallKillY is { } killY)
        {
            using var game = ModeGame(0, GameMode.FreeForAll, id);
            game.WarmupEnabled = true;
            Run(game, ref now, 1);
            game.ChooseLoadout(Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, ThrowableKind.None, ThrowableKind.None, 0, 0, now);
            Run(game, ref now, Game.CountdownSeconds + 0.3);
            var me = game.Local;
            if (me is not { Alive: true } || game.Phase != MatchPhase.Playing || World.Map != m)
                Fail($"{name}: the fall-kill game didn't start");
            else
            {
                me.ImmuneUntil = 0;
                var deaths = me.Deaths;
                me.Position = safe;
                me.Sim.Velocity = Vector3.Zero;
                Run(game, ref now, 1.5);
                var safeAlive = me.Alive && me.Position.Y > killY;
                var safeY = me.Position.Y;
                me.Position = drop;
                me.Sim.Velocity = Vector3.Zero;
                me.Sim.Grounded = false;
                var fell = 0.0;
                for (var i = 0; i < 4 * World.TickRate && me.Alive; i++)
                {
                    Run(game, ref now, 1.0 / World.TickRate);
                    fell += 1.0 / World.TickRate;
                }
                fallText = $"fall kill: in the drift {(safeAlive ? $"alive at y {safeY:0.0}" : "DEAD")}, dropped into the chasm {(me.Alive ? "still alive" : $"died after {fell:0.00} s")}, fall kills {game.FallKills}";
                if (!safeAlive)
                    Fail($"{name}: a player standing in the drift at {safe} died or fell (kill height {killY}, now at y {safeY:0.0})");
                if (me.Alive || me.Deaths != deaths + 1 || game.FallKills < 1)
                    Fail($"{name}: falling into the chasm at {drop} didn't kill");
            }
        }
        m.RestoreProps();
        World.SetMap(m);

        var drawn = Enumerable.Range(0, m.Solid.Count).Count(m.IsDrawnBox);
        var summary = $"{name}: {m.Size} {m.HalfX * 2:0} x {m.HalfZ * 2:0} m ({m.PlayArea:0} m2 inside the {shape.ToString().ToLowerInvariant()} outline), max {m.WantedMaxPlayers} players; "
                      + $"{m.Solid.Count} boxes ({drawn} drawn one by one, {m.HiddenBoxCount} hidden); {props.Count} props ({props.Count(p => p.Def.IsSolid)} solid); "
                      + $"{m.MeshPlacements.Count} meshes; decals {baked.Placed}/{baked.Requested}; {m.Spawns.Count} spawns; nav {g.Nodes.Length} nodes, build {g.BuildMs:0} ms; "
                      + $"walks Blue/Red: {string.Join(", ", walks)}" + (fallText.Length > 0 ? "; " + fallText : "");
        Line(summary);
        notes.Add(summary);
    }
}
