using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Cluttered pack's map 12, "Powder Keg": a warehouse full of explosives on a noisy smooth outline (large, 16
/// players). It gets the checks every Cluttered map gets (<see cref="CheckClutteredSmallMap"/>: density quota, everything
/// inside the outline, hand-placed objectives, nav reach, free-for-all and team bot matches at the player limit), plus
/// its own: the outline and size class, the draw-call side of the counts, the explosives (hundreds of them, every one well
/// clear of every spawn), fairness for an asymmetric map (the nav walk from each team's spawns to every hill and the
/// enemy flag within 10%, no spawn seeing an enemy spawn, the cover and high ground along each team's routes), and a
/// chain reaction: one barrel shot in the biggest cluster, the ripple timed tick by tick against the work budget.
/// </summary>
public static partial class SelfTest
{
    private const string ClutteredPowderKegMap = "Powder Keg";

    private static void CheckClutteredPowderKeg()
    {
        var m = Maps.FindPack(ClutteredPack)?.Maps.FirstOrDefault(x => x.Name == ClutteredPowderKegMap);
        if (m == null)
        {
            Fail($"{ClutteredPack}: no map \"{ClutteredPowderKegMap}\" (12-PowderKeg.json)");
            return;
        }
        if (m.SourceFile == null || !Path.GetFileName(m.SourceFile).StartsWith("12-", StringComparison.Ordinal))
            Fail($"{m.Name}: file should be Maps/Cluttered/12-PowderKeg.json (is {m.SourceFile})");
        if (m.Boundary is not { } bd || bd.Spec.Shape != BoundaryShape.Smooth || bd.Spec.Noise is not { Amplitude: > 0 })
        {
            Fail($"{m.Name}: boundary should be a noisy smooth outline");
            return;
        }
        if (m.Size != MapSize.Large || bd.Area is < 16000 or > 22000)
            Fail($"{m.Name}: size class {m.Size}, {bd.Area:0} m2 (expected Large, 16,000-22,000 m2)");
        if (m.MaxPlayersSetting != 16 || m.MaxPlayers != 16)
            Fail($"{m.Name}: maxPlayers {m.MaxPlayersSetting} in the file, {m.MaxPlayers} in effect (expected 16)");

        // Draw-call side of the counts, and the props by set.
        var props = m.PropInstances;
        var drawn = Enumerable.Range(0, m.Solid.Count).Count(m.IsDrawnBox);
        var trees = m.FoliageInstances.Count(f => Foliage.IsTree(f.Type));
        var sets = string.Join(", ", props.GroupBy(p => p.Def.Category).OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} {g.Count(p => p.Def.IsSolid)}/{g.Count(p => !p.Def.IsSolid)}"));
        ClutteredLine($"{m.Name}: {drawn} boxes drawn one by one, {trees} trees, {props.Count(p => p.Def.Parts.Any(q => q.Anim != null))} animated props; by set (solid/cosmetic): {sets}");
        if (drawn > 700)
            Fail($"{m.Name}: {drawn} boxes drawn one by one (keep it under 700: use props, meshes and foliage)");
        if (trees < 15)
            Fail($"{m.Name}: {trees} trees (expected at least 15)");

        // The explosives: hundreds, in clusters, every one clear of every spawn by its blast radius plus 8 m.
        var boom = Enumerable.Range(0, props.Count).Where(i => props[i].Def.Breakable is { ExplodeRadius: > 0 }).ToList();
        var fused = boom.Count(i => props[i].Def.Breakable!.ExplodeFuse > 0);
        var spawnsAll = m.Spawns.Concat(m.Objectives.TeamSpawns[0]).Concat(m.Objectives.TeamSpawns[1]).ToList();
        var nearest = float.MaxValue;
        var tooClose = new List<string>();
        foreach (var i in boom)
        {
            var p = props[i];
            var r = p.Def.Breakable!.ExplodeRadius;
            foreach (var s in spawnsAll)
            {
                var d = Vector2.Distance(new Vector2(s.X, s.Z), new Vector2(p.Position.X, p.Position.Z));
                nearest = MathF.Min(nearest, d - r);
                if (d < r + 8f)
                {
                    tooClose.Add($"{p.Def.Name} ({p.Position.X:0.0},{p.Position.Z:0.0}) {d:0.0} m from a spawn");
                    break;
                }
            }
        }
        var byName = string.Join(", ", boom.GroupBy(i => props[i].Def.Name).OrderByDescending(g => g.Count()).Select(g => $"{g.Count()} {g.Key}"));
        ClutteredLine($"{m.Name} explosives: {boom.Count} ({fused} with a fuse: {byName}); nearest to a spawn {nearest:0.0} m beyond its blast radius");
        if (boom.Count < 250)
            Fail($"{m.Name}: only {boom.Count} explosive props (the warehouse wants hundreds)");
        if (tooClose.Count > 0)
            Fail($"{m.Name}: {tooClose.Count} explosive(s) too close to a spawn: {string.Join("; ", tooClose.Take(4))}");

        CheckClutteredSmallMap(m, new SmallClutterQuota(Props: 2500, SolidProps: 700, CosmeticProps: 1200, Breakable: 1000, Plants: 9000, Trees: 15, Boxes: 900));

        CheckPowderKegFairness(m);
        CheckPowderKegChain(m, boom);
    }

    /// <summary>
    /// Fairness without mirroring: the nav walk from each team's spawns (averaged) to every hill and the enemy flag within
    /// 10%, no Blue spawn with a line of sight to a Red one, and the cover and high ground along each team's routes to
    /// the hills (Blue's yard side has more cover, Red's office side the catwalks: logged, cover held to a floor).
    /// </summary>
    private static void CheckPowderKegFairness(Map m)
    {
        var o = m.Objectives;
        var g = ClutteredLargeNav(m);
        var search = new NavSearch();
        var path = new int[2048];
        var routes = new[] { new List<Vector3>(), new List<Vector3>() };
        float Walk(int team, Vector3 goal, bool keep)
        {
            var total = 0f;
            var gi = g.Nearest(goal, needSight: false);
            foreach (var s in o.TeamSpawns[team])
            {
                var count = search.Find(g, g.Nearest(s, needSight: false), gi, path, maxExpand: 30000);
                if (count == 0)
                    return float.PositiveInfinity;
                var len = Vector3.Distance(s, g.Nodes[path[0]]) + Vector3.Distance(g.Nodes[path[count - 1]], goal);
                for (var k = 1; k < count; k++)
                    len += Vector3.Distance(g.Nodes[path[k - 1]], g.Nodes[path[k]]);
                if (keep)
                    for (var k = 0; k < count; k++)
                        routes[team].Add(g.Nodes[path[k]]);
                total += len;
            }
            return total / Math.Max(1, o.TeamSpawns[team].Count);
        }
        var walks = new List<string>();
        void Check(string name, float blue, float red)
        {
            var diff = (blue - red) / MathF.Max(blue, red);
            walks.Add($"{name} {blue:0}/{red:0} m ({diff * 100:+0.0;-0.0;0.0}%)");
            if (!float.IsFinite(blue + red) || MathF.Abs(diff) > 0.1f)
                Fail($"{m.Name}: {name} is a {blue:0} m walk for Blue and {red:0} m for Red (more than 10% apart)");
        }
        for (var i = 0; i < o.Hills.Count; i++)
            Check($"hill {i + 1}", Walk(0, o.Hills[i].Center, true), Walk(1, o.Hills[i].Center, true));
        Check("enemy flag", Walk(0, o.Flags[1], false), Walk(1, o.Flags[0], false));

        // No spawn sees an enemy spawn (eye to eye through the world: walls, racks, trailers, containers).
        var seen = new List<string>();
        foreach (var a in o.TeamSpawns[0])
            foreach (var b in o.TeamSpawns[1])
            {
                var ea = a + new Vector3(0, World.EyeHeight, 0);
                var eb = b + new Vector3(0, World.EyeHeight, 0);
                var d = Vector3.Distance(ea, eb);
                if (World.RaycastWorld(ea, (eb - ea) / d, d) >= d - 0.05f)
                    seen.Add($"({a.X:0},{a.Z:0})-({b.X:0},{b.Z:0})");
            }

        // Cover (solid things at least 0.9 m tall) within 6 m of each team's routes to the hills, and how much of each
        // route runs on raised floors (the mezzanine, the catwalks: more than 2 m up).
        var cover = new List<Vector3>();
        foreach (var p in m.PropInstances.Where(p => p.Def.IsSolid && p.Bounds.Max.Y - p.Bounds.Min.Y >= 0.9f))
            cover.Add(p.Bounds.Center);
        for (var i = 0; i < m.Solid.Count; i++)
            if (m.IsDrawnBox(i) && m.Solid[i].Max.Y - m.Solid[i].Min.Y >= 0.9f && m.Solid[i].Min.Y < 1f)
                cover.Add(m.Solid[i].Center);
        int CoverNear(List<Vector3> route)
        {
            var c = 0;
            foreach (var q in cover)
                foreach (var r in route)
                    if ((q.X - r.X) * (q.X - r.X) + (q.Z - r.Z) * (q.Z - r.Z) < 36f && MathF.Abs(q.Y - r.Y) < 3f)
                    {
                        c++;
                        break;
                    }
            return c;
        }
        int coverBlue = CoverNear(routes[0]), coverRed = CoverNear(routes[1]);
        var ratio = MathF.Min(coverBlue, coverRed) / (float)Math.Max(1, Math.Max(coverBlue, coverRed));
        float High(List<Vector3> route) => route.Count == 0 ? 0 : 100f * route.Count(n => n.Y > 2f) / route.Count;
        var raised = g.Nodes.Count(n => n.Y > 2f);
        ClutteredLine($"{m.Name} balance (Blue/Red nav walk): {string.Join(", ", walks)}; spawn-to-spawn sight lines {seen.Count}; "
                      + $"cover within 6 m of the routes to the hills {coverBlue}/{coverRed} ({ratio:P0}); route nodes on raised floors {High(routes[0]):0}%/{High(routes[1]):0}%; "
                      + $"{raised} nav nodes above 2 m (mezzanine, catwalks, stairs)");
        if (seen.Count > 0)
            Fail($"{m.Name}: {seen.Count} Blue spawn(s) see a Red spawn: {string.Join(", ", seen.Take(4))}");
        if (ratio < 0.5f)
            Fail($"{m.Name}: the routes to the hills have very uneven cover (Blue {coverBlue}, Red {coverRed})");
        if (raised < 20)
            Fail($"{m.Name}: only {raised} nav nodes above 2 m: bots can't use the mezzanine and catwalks");
    }

    /// <summary>
    /// A chain reaction: the biggest cluster of explosives (each within 70% of a neighbour's blast radius), one of them
    /// shot. It must light (not go off at once), then go off after its fuse and set off a ripple through the cluster (walls
    /// and racks shield some of it, so at least ten must go);
    /// every server tick of the ripple is timed against the work budget, and the fireballs shown stay capped.
    /// </summary>
    private static void CheckPowderKegChain(Map m, List<int> boom)
    {
        var props = m.PropInstances;
        // Clusters by a flood fill over "within 70% of the blast radius" links.
        var seen = new HashSet<int>();
        var best = new List<int>();
        foreach (var start in boom)
        {
            if (!seen.Add(start))
                continue;
            var group = new List<int> { start };
            for (var k = 0; k < group.Count; k++)
            {
                var a = props[group[k]];
                foreach (var j in boom)
                {
                    if (seen.Contains(j))
                        continue;
                    var b = props[j];
                    var reach = 0.7f * MathF.Max(a.Def.Breakable!.ExplodeRadius, b.Def.Breakable!.ExplodeRadius);
                    if (Vector3.DistanceSquared(a.Bounds.Center, b.Bounds.Center) < reach * reach)
                    {
                        seen.Add(j);
                        group.Add(j);
                    }
                }
            }
            if (group.Count > best.Count)
                best = group;
        }
        if (best.Count < 10)
        {
            Fail($"{m.Name}: the biggest cluster of explosives has only {best.Count} props (chain reactions want clusters)");
            return;
        }

        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: m.Id);
        game.MapVoteEnabled = false;
        m.RestoreProps();
        var me = game.Players[0];
        // The first of the cluster with a fuse, shot from above (nothing between).
        var first = best.Where(i => props[i].Def.Breakable!.ExplodeFuse > 0).OrderBy(i => props[i].Position.Y).ThenBy(i => i).FirstOrDefault(best[0]);
        var c = props[first].Bounds.Center;
        var from = c + new Vector3(0.3f, 2.2f, 0.2f);
        var dir = Vector3.Normalize(c - from);
        var dist = World.RaycastWorld(from, dir, 10f);
        var health = props[first].Def.Breakable!.Health;
        game.ShotProps(me, from, dir, dist, health + 5, 0);
        var lit = game.IsPropLit(first) && !m.IsPropBroken(first) && game.PropFusesBurning == 1;
        var fuse = props[first].Def.Breakable!.ExplodeFuse;
        const double dt = 1.0 / World.TickRate;
        var now = 0.0;
        var cpu = new TickCpu();
        var wall = new List<double>();
        int maxBlasts = 0, maxBurning = 0, matchBefore = game.MatchNumber, jitTicks = 0;
        double wentOffAt = -1;
        var brokenBefore = m.BrokenProps.Count;
        var sw = Stopwatch.StartNew();
        for (var tick = 0; tick < 12 * World.TickRate; tick++)
        {
            now += dt;
            var jit0 = System.Runtime.JitInfo.GetCompilationTime(currentThread: true);
            cpu.Start();
            var t0 = Stopwatch.GetTimestamp();
            game.Update(now, dt);
            var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            var jit = (System.Runtime.JitInfo.GetCompilationTime(currentThread: true) - jit0).TotalMilliseconds > 1;
            jitTicks += jit ? 1 : 0;
            cpu.Stop(ms, count: !jit);
            if (!jit)
                wall.Add(ms);
            if (wentOffAt < 0 && m.IsPropBroken(first))
                wentOffAt = now;
            maxBlasts = Math.Max(maxBlasts, game.Blasts.Count);
            maxBurning = Math.Max(maxBurning, game.PropFusesBurning);
            if (tick > World.TickRate && game.PropFusesBurning == 0)
                break;
        }
        var gone = boom.Count(i => m.IsPropBroken(i));
        var inCluster = best.Count(i => m.IsPropBroken(i));
        wall.Sort();
        ClutteredLine($"{m.Name} chain reaction: shot a {props[first].Def.Name} at ({c.X:0.0},{c.Z:0.0}) in the biggest cluster ({best.Count} explosives); "
                      + $"lit first {lit}, went off after {wentOffAt:0.00} s (fuse {fuse:0.0} s); {game.PropDetonations} explosions ({inCluster}/{best.Count} of the cluster, "
                      + $"{gone} explosives in all), {m.BrokenProps.Count - brokenBefore} props broken, up to {maxBurning} fuses burning, fireballs shown {game.PropBlastsShown} "
                      + $"(at most {maxBlasts} at once); ticks: median {(wall.Count > 0 ? wall[wall.Count / 2] : 0):0.00} ms, p99 {(wall.Count > 0 ? wall[(int)(wall.Count * 0.99)] : 0):0.00} ms, "
                      + $"worst work {cpu.WorstText} ({jitTicks} JIT ticks not counted; {sw.Elapsed.TotalSeconds:0.0} s)");
        if (!lit)
            Fail($"{m.Name}: a shot {props[first].Def.Name} didn't light (it should burn {fuse:0.0} s before it goes off)");
        if (wentOffAt < fuse - 0.05 || wentOffAt > fuse + 0.5)
            Fail($"{m.Name}: the shot {props[first].Def.Name} went off after {wentOffAt:0.00} s (fuse {fuse:0.0} s)");
        if (inCluster < Math.Min(best.Count, 10) || game.PropDetonations < 10)
            Fail($"{m.Name}: the chain reaction fizzled ({inCluster} of {best.Count} in the cluster, {game.PropDetonations} explosions)");
        if (maxBlasts > Game.MaxPropBlastsShown + 2)
            Fail($"{m.Name}: {maxBlasts} fireballs at once (the cap is {Game.MaxPropBlastsShown})");
        if (game.MatchNumber != matchBefore)
            Fail($"{m.Name}: the match restarted during the chain reaction test");
        if (cpu.WorstMs > MaxTickWorkMs)
            Fail($"{m.Name}: a tick of the chain reaction did {cpu.WorstMs:0.0} ms of work (over {MaxTickWorkMs} ms; {cpu.WorstDetail})");
        m.RestoreProps();
    }
}
