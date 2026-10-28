using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "Map contract" section: one data-driven check of every map against what its file's optional "test" block says
/// (MapFile.Test.cs; Maps/README.md, "Testing a map"). It replaced the one-section-per-map-pair tests the Space pack
/// grew (shape, dressing floors, hand-placed objectives inside the outline and off the geometry, symmetry, nothing
/// outside, props clear of spawns and objectives, nav reach and fair walks, no nav link over the void, deck probes, the
/// fall kill, a short seeded bot match, the horizon fade). Every map gets the universal subset (objectives valid and
/// inside, spawns clear, nothing outside the outline); a map with a "test" block gets the rest, with that block's
/// thresholds. "--check-map FILE" and "--check-maps DIR" run the same checks on files, printing each check's result.
/// </summary>
public static partial class SelfTest
{
    private static void CheckMapContract()
    {
        var notes = new List<string>();
        var withBlock = 0;
        var sw = Stopwatch.StartNew();
        foreach (var m in Maps.All)
        {
            MapContractUniversal(m, verbose: false);
            if (m.TestSpec == null)
                continue;
            withBlock++;
            MapContractFull(m, verbose: false, notes);
        }
        World.SetMap(Maps.All[0]);
        Line($"{Maps.All.Length} maps checked, {withBlock} with a \"test\" block, in {sw.Elapsed.TotalSeconds:0.0} s");
        if (withBlock == 0)
            Fail("no map has a \"test\" block (the Space pack's maps should)");

        // The block itself: not fingerprinted, round-trips through a file, and a bad one is refused with a reason.
        var src = Maps.All.First(x => x.TestSpec != null);
        var text = File.ReadAllText(Path.Combine(Maps.Folder, src.SourceFile!));
        var start = text.IndexOf("\"test\":", StringComparison.Ordinal);
        var end = text.IndexOf("\n },", start, StringComparison.Ordinal) + 4;
        var stripped = MapFile.Load(text[..start] + text[end..], src.SourceFile!);
        var again = MapFile.Load(text, src.SourceFile!);
        var back = MapFile.Load(MapFile.ToJson(again), src.SourceFile!);
        stripped.Validate();
        again.Validate();
        back.Validate();
        if (stripped.TestSpec != null || again.TestSpec == null || back.TestSpec == null)
            Fail($"{src.Name}: the \"test\" block wasn't read (stripped {stripped.TestSpec != null}, again {again.TestSpec != null}, round trip {back.TestSpec != null})");
        if (stripped.ContentHash != again.ContentHash || again.ContentHash != back.ContentHash || again.ContentHash != src.ContentHash)
            Fail($"{src.Name}: the \"test\" block changes the fingerprint ({stripped.ContentHash:x8} without, {again.ContentHash:x8} with, {back.ContentHash:x8} round trip)");
        if (back.TestSpec is { } rt && (rt.Symmetry != src.TestSpec!.Symmetry || rt.Probes.Length != src.TestSpec.Probes.Length || rt.MinProps != src.TestSpec.MinProps))
            Fail($"{src.Name}: the \"test\" block changed in a file round trip");
        foreach (var (bad, expect) in new[]
                 {
                     ("\"test\": { \"shpae\": \"turn\" }", "unknown key"),
                     ("\"test\": { \"symmetry\": \"spin\" }", "one of"),
                     ("\"test\": { \"probes\": [[1, 2, 3, 4]] }", "name"),
                     ("\"test\": { \"botMatchSeconds\": 99 }", "0 to 60"),
                     ("\"test\": 7", "object"),
                 })
        {
            try
            {
                MapFile.Load($"{{ \"name\": \"X\", \"halfX\": 20, \"halfZ\": 20, {bad}, \"ops\": [ {{ \"op\": \"Spawn\", \"x\": 1, \"z\": 1 }} ] }}", "bad.json");
                Fail($"a map with a bad \"test\" block loaded without complaint ({bad})");
            }
            catch (MapFileException ex) when (ex.Message.Contains(expect, StringComparison.OrdinalIgnoreCase))
            {
            }
            catch (MapFileException ex)
            {
                Fail($"a bad \"test\" block gave the wrong error: {ex.Message} (expected '{expect}')");
            }
        }
        Line($"the \"test\" block isn't fingerprinted ({src.Name}: {src.ContentHash:x8} with and without), round-trips, and bad blocks are refused with a reason");
        if (notes.Count > 0)
            Line("::notice title=Map contract::" + string.Join("%0A", notes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
    }

    // ---- The --check-map / --check-maps tools -----------------------------------------------------------------

    /// <summary>
    /// Runs the contract checks on one loaded map for the command line (Maps.Check), printing each check's result and
    /// the fairness numbers to <paramref name="output"/>. Returns the number of failed checks.
    /// </summary>
    public static int CheckMapContractTool(Map m, TextWriter output)
    {
        var failures = 0;
        _lineSink = s => output.WriteLine(s);
        _failSink = s =>
        {
            failures++;
            output.WriteLine("  FAIL: " + s);
        };
        try
        {
            MapContractUniversal(m, verbose: true);
            if (m.TestSpec == null)
                output.WriteLine("  (no \"test\" block in the file: only the universal checks ran; see Maps/README.md, \"Testing a map\")");
            else
                MapContractFull(m, verbose: true, new List<string>());
        }
        finally
        {
            _lineSink = null;
            _failSink = null;
        }
        return failures;
    }

    // ---- One named check -----------------------------------------------------------------------------------------

    /// <summary>Runs one check; in verbose mode (the tools) says whether it passed, with its detail line. Quiet: the detail only in verbose mode.</summary>
    private static bool MapCheck(string name, bool verbose, Func<string?> body, bool quiet = false)
    {
        var before = _failures;
        string? detail;
        try
        {
            detail = body();
        }
        catch (Exception ex)
        {
            Fail($"{name} threw: {ex.Message}");
            detail = null;
        }
        var ok = _failures == before;
        if (verbose)
            Line($"  {(ok ? "ok  " : "FAIL")} {name}{(detail != null ? ": " + detail : "")}");
        else if (detail != null && quiet == false)
            Line($"    {name}: {detail}");
        return ok;
    }

    // ---- Universal: every map ------------------------------------------------------------------------------------

    private static void MapContractUniversal(Map m, bool verbose)
    {
        var o = m.Objectives;
        MapCheck("objectives", verbose, () =>
        {
            if (o.Hills.Count == 0)
                Fail($"{m.Name}: no hill");
            foreach (var h in o.Hills)
                if (m.IsBlocked(h.Center))
                    Fail($"{m.Name}: the hill at ({h.Center.X:0}, {h.Center.Z:0}) is inside geometry");
            foreach (var f in o.Flags)
                if (m.IsBlocked(f))
                    Fail($"{m.Name}: the flag at ({f.X:0}, {f.Z:0}) is inside geometry");
            var blocked = m.Spawns.Count(s => m.IsBlocked(s));
            if (blocked > 0)
                Fail($"{m.Name}: {blocked} spawn(s) inside geometry");
            if (m.Spawns.Count < m.WantedMaxPlayers)
                Fail($"{m.Name}: only {m.Spawns.Count} spawns (need {m.WantedMaxPlayers} for a full server)");
            if (o.TeamSpawns[0].Count == 0 || o.TeamSpawns[1].Count == 0)
                Fail($"{m.Name}: a team has no spawns");
            return $"{o.TeamSpawns[0].Count}+{o.TeamSpawns[1].Count} team spawns ({(o.TeamSpawnsGenerated ? "generated" : "placed")}), "
                   + $"{o.Hills.Count} hills ({(o.HillsGenerated ? "generated" : "placed")}), flags {(o.FlagsGenerated ? "generated" : "placed")}";
        }, quiet: true);
        if (m.Boundary is not { } bd)
            return;
        MapCheck("inside the outline", verbose, () =>
        {
            var outside = new List<string>();
            void In(string what, float x, float z, float margin = 0f)
            {
                if (!bd.Inside(x, z, margin))
                    outside.Add($"{what} ({x:0.0},{z:0.0})");
            }
            foreach (var s in m.Spawns)
                In("spawn", s.X, s.Z, 0.3f);
            foreach (var w in m.Waypoints)
                In("waypoint", w.X, w.Z);
            foreach (var h in o.Hills)
                In("hill", h.Center.X, h.Center.Z, h.Radius * 0.5f);
            for (var i = 0; i < o.Flags.Length; i++)
                In(i == 0 ? "blue flag" : "red flag", o.Flags[i].X, o.Flags[i].Z, 0.5f);
            // Props and drawn boxes stay inside too when the map's block asks (older packs dress the view past the edge on purpose).
            var dressed = 0;
            if (m.TestSpec is { PropsInside: true })
            {
                foreach (var p in m.PropInstances)
                    In(p.Def.Name, p.Position.X, p.Position.Z);
                for (var i = 0; i < m.Solid.Count; i++)
                    if (m.IsDrawnBox(i) && !bd.Contains(m.Solid[i].Center.X, m.Solid[i].Center.Z))
                        outside.Add($"box ({m.Solid[i].Center.X:0.0},{m.Solid[i].Center.Z:0.0})");
                dressed = 1;
            }
            if (outside.Count > 0)
                Fail($"{m.Name}: {outside.Count} thing(s) outside the outline: {string.Join(", ", outside.Take(6))}");
            return $"{bd.Spec.Shape.ToString().ToLowerInvariant()} outline, {bd.SegmentCount} segments, {bd.Area:0} m2; spawns, waypoints and objectives{(dressed > 0 ? ", props and boxes" : "")} inside";
        }, quiet: true);
    }

    // ---- The full contract: maps with a "test" block ---------------------------------------------------------------

    private static void MapContractFull(Map m, bool verbose, List<string> notes)
    {
        var t = m.TestSpec!;
        var o = m.Objectives;
        var bd = m.Boundary;
        var players = t.MaxPlayers ?? m.MaxPlayers;
        var isVoid = t.Void ?? (m.FallKillY != null && m.GroundIndex < 0 && !m.HasTerrain);
        var sw = Stopwatch.StartNew();
        if (!verbose)
            Line($"{m.Name} ({m.SourceFile}):");

        // Shape, size, players.
        MapCheck("shape", verbose, () =>
        {
            var shape = bd == null ? "rect" : bd.Spec.Shape.ToString().ToLowerInvariant();
            var wantShaped = t.Shape == "non-rectangular";
            if (wantShaped && (bd == null || bd.Spec.Shape == BoundaryShape.Rect))
                Fail($"{m.Name}: the outline should be a non-rectangular boundary (is {(bd == null ? "none, a rectangle" : shape)})");
            else if (t.Shape != "any" && !wantShaped && shape != t.Shape)
                Fail($"{m.Name}: expected a {t.Shape} outline, got {(bd == null ? "a rectangle" : shape)}");
            if (t.Area is { } area && (m.PlayArea < area.Min || m.PlayArea > area.Max))
                Fail($"{m.Name}: {m.PlayArea:0} m2 inside the outline (expected {area.Min:0}-{area.Max:0})");
            if (t.Size is { } size && m.Size != size)
                Fail($"{m.Name}: size class {m.Size} (expected {size})");
            if (t.MaxPlayers is { } mp && (m.MaxPlayers != mp || m.MaxPlayersSetting != mp))
                Fail($"{m.Name}: {m.MaxPlayers} players (\"maxPlayers\": {m.MaxPlayersSetting}; expected {mp})");
            if (isVoid && (m.FallKillY == null || m.GroundIndex >= 0 || m.HasTerrain))
                Fail($"{m.Name}: should be a void map (fallKill, no ground slab, no terrain)");
            return $"{shape}, {m.PlayArea:0} m2 ({m.HalfX * 2:0} x {m.HalfZ * 2:0} m, {m.Size}), max {m.MaxPlayers} players"
                   + (m.FallKillY is { } k ? $", fall kill below {k:0.0} m{(isVoid ? " (void map)" : "")}" : "") + (m.HasTerrain ? ", terrain" : "");
        });

        // Dressing floors.
        var props = m.PropInstances;
        var solid = props.Count(p => p.Def.IsSolid);
        var drawn = Enumerable.Range(0, m.Solid.Count).Count(i => m.IsDrawnBox(i) && !m.IsTrunk(i));
        var bake = DecalBaker.Bake(m);
        MapCheck("dressing", verbose, () =>
        {
            void AtLeast(string what, int have, int need)
            {
                if (need > 0 && have < need)
                    Fail($"{m.Name}: {have} {what} (the dressing floor is {need})");
            }
            var plants = m.FoliageInstances;
            var trees = plants.Count(f => Foliage.IsTree(f.Type));
            AtLeast("props", props.Count, t.MinProps);
            AtLeast("solid props", solid, t.MinSolidProps);
            AtLeast("cosmetic props", props.Count - solid, t.MinCosmeticProps);
            AtLeast("breakable props", props.Count(p => p.IsBreakable), t.MinBreakableProps);
            AtLeast("decals placed", bake.Placed, t.MinDecals);
            AtLeast("meshes", m.MeshPlacements.Count - m.BoundaryMeshCount, t.MinMeshes);
            AtLeast("plants", plants.Length, t.MinPlants);
            AtLeast("trees", trees, t.MinTrees);
            AtLeast("pack-local props", props.Count(p => p.Def.Pack != null), t.MinPackProps);
            if (t.MinDecalShare > 0 && bake.Requested > 0 && bake.Placed * 100 < bake.Requested * t.MinDecalShare)
                Fail($"{m.Name}: only {bake.Placed} of {bake.Requested} decals found a surface (wanted {t.MinDecalShare}%)");
            if (drawn > t.MaxDrawnBoxes)
                Fail($"{m.Name}: {drawn} boxes drawn one by one (keep them under {t.MaxDrawnBoxes}: big decks, meshes over hidden colliders, props)");
            foreach (var name in t.RequireProps)
                if (!props.Any(p => p.Def.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    Fail($"{m.Name}: no \"{name}\" prop placed");
            foreach (var prefix in t.RequireDecals)
                if (!m.DecalRequests.Any(r => r.Names.Any(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))))
                    Fail($"{m.Name}: no \"{prefix}\" decal placed");
            return $"{m.Solid.Count} boxes ({drawn} drawn one by one, {m.HiddenBoxCount} hidden), {props.Count} props ({solid} solid, {props.Count(p => p.IsBreakable)} breakable, "
                   + $"{props.Count - m.ClutterStart} clutter, {props.Count(p => p.Def.Pack != null)} pack-local), {m.MeshPlacements.Count - m.BoundaryMeshCount} meshes, "
                   + $"decals {bake.Placed}/{bake.Requested}, {plants.Length} plants ({trees} trees)";
        });

        // Hand-placed objectives for every mode, enough spawns.
        MapCheck("hand-placed objectives", verbose, () =>
        {
            if (t.HandPlaced && (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated))
                Fail($"{m.Name}: team spawns, hills and flags should all be hand-placed (team spawns {!o.TeamSpawnsGenerated}, hills {!o.HillsGenerated}, flags {!o.FlagsGenerated})");
            var minTeam = t.MinTeamSpawns >= 0 ? t.MinTeamSpawns : players / 2;
            if (o.TeamSpawns[0].Count < minTeam || o.TeamSpawns[1].Count < minTeam)
                Fail($"{m.Name}: team spawn lists are short ({o.TeamSpawns[0].Count} + {o.TeamSpawns[1].Count}; want at least {minTeam} each for {players} players)");
            if (o.Hills.Count < t.MinHills)
                Fail($"{m.Name}: only {o.Hills.Count} hill zone(s) (want {t.MinHills})");
            if (o.Flags.Length != 2)
                Fail($"{m.Name}: {o.Flags.Length} flags");
            var authored = m.AuthoredSpawns >= 0 ? m.AuthoredSpawns : m.Spawns.Count;
            if (t.HandPlaced && authored < players)
                Fail($"{m.Name}: {authored} hand-placed spawns for {players} players");
            if (m.FallKillY is { } kill && m.Spawns.Any(s => s.Y < kill + 0.5f))
                Fail($"{m.Name}: a spawn sits below the fall-kill height");
            return $"{authored} spawns placed (teams {o.TeamSpawns[0].Count}+{o.TeamSpawns[1].Count}), {o.Hills.Count} hills, {o.Flags.Length} flags, {m.Waypoints.Count} waypoints";
        });

        // Symmetry: Red's spawns, the flags and the hills are Blue's twins under the map's symmetry.
        if (t.Symmetry != "none")
            MapCheck($"symmetry ({t.Symmetry})", verbose, () =>
            {
                Vector2 Twin(float x, float z) => t.Symmetry switch
                {
                    "mirrorX" => new Vector2(-x, z),
                    "mirrorZ" => new Vector2(x, -z),
                    _ => new Vector2(-x, -z),
                };
                var tol = t.SymmetryTolerance;
                if (o.TeamSpawns[0].Count != o.TeamSpawns[1].Count)
                    Fail($"{m.Name}: teams have {o.TeamSpawns[0].Count} and {o.TeamSpawns[1].Count} spawns (a symmetric map gives both the same number)");
                var unpaired = o.TeamSpawns[0].Count(a => !o.TeamSpawns[1].Any(b => Vector2.Distance(Twin(a.X, a.Z), new Vector2(b.X, b.Z)) < tol));
                if (unpaired > t.UnpairedSpawns)
                    Fail($"{m.Name}: {unpaired} Blue spawn(s) have no Red twin at {TwinText(t.Symmetry)} (within {tol:0.0} m)");
                if (o.Flags.Length == 2 && Vector2.Distance(Twin(o.Flags[0].X, o.Flags[0].Z), new Vector2(o.Flags[1].X, o.Flags[1].Z)) > tol)
                    Fail($"{m.Name}: the flags aren't each other's twin at {TwinText(t.Symmetry)}");
                var unpairedHills = t.SymmetricHills ? o.Hills.Count(a => !o.Hills.Any(b => Vector2.Distance(Twin(a.Center.X, a.Center.Z), new Vector2(b.Center.X, b.Center.Z)) < tol)) : 0;
                if (unpairedHills > 0)
                    Fail($"{m.Name}: {unpairedHills} hill(s) have no twin at {TwinText(t.Symmetry)} (a hill on the symmetry line is its own twin)");
                var detail = $"spawns paired ({unpaired} unpaired, {t.UnpairedSpawns} allowed), flags{(t.SymmetricHills ? " and hills" : "")} twinned";
                if (t.SymmetricProps)
                {
                    // Every hand-placed solid prop has its twin: same prop, mirrored position and yaw (Clutter mirrors its own).
                    var mirrorX = t.Symmetry == "mirrorX";
                    var mirrorZ = t.Symmetry == "mirrorZ";
                    var unmatched = new List<string>();
                    var solidIdx = Enumerable.Range(0, Math.Min(m.ClutterStart, props.Count)).Where(i => props[i].Def.IsSolid).ToList();
                    var used = new HashSet<int>();
                    foreach (var i in solidIdx)
                    {
                        if (used.Contains(i))
                            continue;
                        var a = props[i];
                        var onAxis = mirrorX ? MathF.Abs(a.Position.X) < 0.05f : mirrorZ ? MathF.Abs(a.Position.Z) < 0.05f
                            : MathF.Abs(a.Position.X) < 0.05f && MathF.Abs(a.Position.Z) < 0.05f;
                        if (onAxis)
                        {
                            used.Add(i);
                            continue;
                        }
                        var want = Twin(a.Position.X, a.Position.Z);
                        var twin = solidIdx.FirstOrDefault(j => j != i && !used.Contains(j) && props[j].Def == a.Def
                            && MathF.Abs(props[j].Position.X - want.X) < 0.05f && MathF.Abs(props[j].Position.Z - want.Y) < 0.05f
                            && MathF.Abs(props[j].Position.Y - a.Position.Y) < 0.05f && MathF.Abs(props[j].Scale - a.Scale) < 0.01f
                            && MathF.Abs(MathF.IEEERemainder(mirrorX ? props[j].Yaw + a.Yaw : mirrorZ ? props[j].Yaw + a.Yaw - MathF.PI : props[j].Yaw - a.Yaw - MathF.PI, MathF.Tau)) < 0.02f, -1);
                        if (twin < 0)
                            unmatched.Add($"{a.Def.Name} ({a.Position.X:0.0},{a.Position.Z:0.0})");
                        else
                        {
                            used.Add(i);
                            used.Add(twin);
                        }
                    }
                    if (unmatched.Count > 0)
                        Fail($"{m.Name}: {unmatched.Count} solid prop(s) without a {(mirrorX || mirrorZ ? "mirrored" : "rotated")} twin: {string.Join(", ", unmatched.Take(5))}");
                    detail += $", {solidIdx.Count} hand-placed solid props twinned";
                }
                return detail;
            });

        // Solid props keep off spawns, hill zones and flags; explosives keep away from spawns.
        if (t.PropsClear || t.ExplosivesClear > 0)
            MapCheck("props clear of spawns and objectives", verbose, () =>
            {
                static float Dist(Box b, Vector3 p)
                {
                    var dx = MathF.Max(0, MathF.Max(b.Min.X - p.X, p.X - b.Max.X));
                    var dz = MathF.Max(0, MathF.Max(b.Min.Z - p.Z, p.Z - b.Max.Z));
                    return MathF.Sqrt(dx * dx + dz * dz);
                }
                var blocked = new List<string>();
                if (t.PropsClear)
                    foreach (var p in props.Where(p => p.Def.IsSolid))
                        for (var k = p.SolidStart; k < p.SolidStart + p.SolidCount; k++)
                        {
                            var b = m.Solid[k];
                            string? why = null;
                            foreach (var sp in m.Spawns)
                                if (why == null && Dist(b, sp) < 0.8f && b.Max.Y > sp.Y + 0.6f && b.Min.Y < sp.Y + 1.5f)
                                    why = $"spawn ({sp.X:0.0},{sp.Z:0.0})";
                            foreach (var h in o.Hills)
                                if (why == null && Dist(b, h.Center) < 1.5f && b.Max.Y > h.Center.Y + 0.6f && b.Min.Y < h.Center.Y + 1.5f)
                                    why = $"hill's centre ({h.Center.X:0},{h.Center.Z:0})";
                            foreach (var f in o.Flags)
                                if (why == null && Dist(b, f) < 1.5f && b.Max.Y > f.Y + 0.6f && b.Min.Y < f.Y + 1.5f)
                                    why = $"flag ({f.X:0},{f.Z:0})";
                            if (why != null)
                            {
                                blocked.Add($"{p.Def.Name} ({p.Position.X:0.0},{p.Position.Z:0.0}) on the {why}");
                                break;
                            }
                        }
                if (blocked.Count > 0)
                    Fail($"{m.Name}: {blocked.Count} solid prop(s) in the way: {string.Join("; ", blocked.Take(4))}");
                var explosives = 0;
                if (t.ExplosivesClear > 0)
                    foreach (var e in props.Where(p => p.Def.Breakable is { ExplodeRadius: > 0 }))
                    {
                        explosives++;
                        if (m.Spawns.Any(s => Vector3.Distance(s, e.Position) < t.ExplosivesClear))
                            Fail($"{m.Name}: explosive {e.Def.Name} at ({e.Position.X:0.0},{e.Position.Z:0.0}) is within {t.ExplosivesClear:0} m of a spawn");
                    }
                return $"{solid} solid props off the spawns, hill centres and flags" + (t.ExplosivesClear > 0 ? $"; {explosives} explosives {t.ExplosivesClear:0} m from spawns" : "");
            });

        // The nav graph, built whole.
        World.SetMap(m);
        var navWait = Stopwatch.StartNew();
        for (var wait = 0; wait < 4000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        if (NavGraph.Current == NavGraph.Empty)
            NavGraph.Pump(double.MaxValue); // (a cached graph read on a worker returns at once from a sliced pump: --check-map spun through the loop and checked an empty graph)
        var g = NavGraph.Current;
        var phys = World.Physics;
        float Floor(float x, float z, float fromY, float depth = 3f)
        {
            var hit = phys.Raycast(new Vector3(x, fromY, z), -Vector3.UnitY, depth, QueryFilter.Navigation);
            return hit >= depth ? float.NaN : fromY - hit;
        }

        // Spawns stand on clear ground (and not in a sideways gravity zone).
        if (t.SpawnsStand)
            MapCheck("spawns stand", verbose, () =>
            {
                var bad = new List<string>();
                foreach (var sp in m.Spawns)
                {
                    if (m.GravityZones is { } zones && !m.PlainUpAtPoint(sp) && zones.FieldAt(sp) is not PointGravity)
                        bad.Add($"({sp.X:0.0},{sp.Y:0.0},{sp.Z:0.0}) in a gravity zone with a sideways up"); // (on a rock is fine: its up is the rock's)
                    var st = new MoveState { Position = sp, Grounded = false };
                    for (var i = 0; i < 40; i++)
                        World.Simulate(ref st, new InputCmd { Yaw = 0, SpeedPct = 100 });
                    if (!st.Grounded || Vector3.Distance(st.Position, sp) > 0.5f || !World.BodyFits(st.Position, GravityFrames.UpAt(st.Position)))
                        bad.Add($"({sp.X:0.0},{sp.Y:0.0},{sp.Z:0.0}) settles {(st.Grounded ? "" : "in the air ")}at ({st.Position.X:0.0},{st.Position.Y:0.0},{st.Position.Z:0.0})");
                }
                if (bad.Count > 0)
                    Fail($"{m.Name}: {bad.Count} spawn(s) don't stand on clear ground: {string.Join("; ", bad.Take(4))}");
                return $"{m.Spawns.Count} spawns settle where they are, standing";
            });

        var search = new NavSearch();
        var path = new int[Math.Max(4096, g.Nodes.Length + 1)];
        float PathLength(int from, int to)
        {
            if (from < 0 || to < 0)
                return float.NaN;
            var n = search.Find(g, from, to, path, maxExpand: Math.Max(40000, g.Nodes.Length));
            if (n == 0)
                return float.NaN;
            var len = 0f;
            for (var i = 1; i < n; i++)
                len += Vector3.Distance(g.Nodes[path[i - 1]], g.Nodes[path[i]]);
            return len;
        }
        float Walk(Vector3 a, Vector3 b) => PathLength(g.Nearest(a, needSight: false), g.Nearest(b, needSight: false));
        // A hill is a zone, maybe a raised deck: the walk to it is the shortest walk to any main-area node inside it at
        // the hill's height (a node on the ground under a gantry doesn't count); without one, to the nearest node.
        float WalkToHill(Vector3 s, HillZone h)
        {
            if (t.HillWalks != "zone")
                return Walk(s, h.Center);
            var from = g.Nearest(s, needSight: false);
            var best = float.NaN;
            for (var k = 0; k < g.Nodes.Length; k++)
            {
                var n = g.Nodes[k];
                if (MathF.Abs(n.Y - h.Center.Y) >= 1.5f
                    || Vector2.Distance(new Vector2(n.X, n.Z), new Vector2(h.Center.X, h.Center.Z)) >= h.Radius)
                    continue;
                var len = PathLength(from, k);
                if (!float.IsNaN(len) && (float.IsNaN(best) || len < best))
                    best = len;
            }
            return float.IsNaN(best) ? Walk(s, h.Center) : best;
        }

        MapCheck("nav graph", verbose, () =>
        {
            var reach = MathF.Max(3f, g.Spacing * 1.5f);
            var missing = new List<string>();
            void Reach(string what, Vector3 p)
            {
                var k = g.Nearest(p, needSight: false);
                if (k < 0 || Vector3.Distance(g.Nodes[k], p) > reach || g.Component[k] != g.MainComponent && !t.NavPockets)
                    missing.Add($"{what} ({p.X:0},{p.Y:0.0},{p.Z:0}){(k < 0 ? " no node" : g.Component[k] != g.MainComponent ? " off the main area" : " no node near")}");
            }
            foreach (var s in m.Spawns)
                Reach("spawn", s);
            foreach (var h in o.Hills)
                Reach("hill", h.Center);
            for (var i = 0; i < o.Flags.Length; i++)
                Reach(i == 0 ? "blue flag" : "red flag", o.Flags[i]);
            if (missing.Count > 0)
                Fail($"{m.Name}: the nav graph's main area doesn't reach {string.Join(", ", missing.Take(8))}{(missing.Count > 8 ? $" and {missing.Count - 8} more" : "")}");
            var mainPct = g.Nodes.Length == 0 ? 0 : 100f * g.MainNodes.Length / g.Nodes.Length;
            if (mainPct < t.MinMainNavPct)
                Fail($"{m.Name}: only {mainPct:0}% of the nav nodes are on the main area (pockets bots can't leave? want {t.MinMainNavPct}%)");
            if (t.MinNavNodesAbove is { } above)
            {
                var high = Enumerable.Range(0, g.Nodes.Length).Count(i => g.Component[i] == g.MainComponent && g.Nodes[i].Y > above.Height);
                if (high < above.Count)
                    Fail($"{m.Name}: only {high} nav nodes above {above.Height:0.#} m in the main area (the raised floors should be on the graph; want {above.Count})");
            }
            if (t.MaxNavBuildMs > 0 && g.BuildMs > t.MaxNavBuildMs)
                Fail($"{m.Name}: the nav graph took {g.BuildMs:0} ms to build (over {t.MaxNavBuildMs}: mover walks {g.WalkChecked} links)");
            return $"{g.Nodes.Length} nodes ({g.MainNodes.Length} main, {mainPct:0}%), {g.EdgeCount} edges, {g.MantleEdges} mantles, built in {g.BuildMs:0} ms "
                   + $"({g.WalkChecked} links walked; {navWait.Elapsed.TotalSeconds:0.0} s waited); every spawn, hill and flag on the main area";
        });

        // Fair walks: each hill and the enemy flag about as far for both teams.
        var walks = new List<string>();
        MapCheck("fair walks", verbose, () =>
        {
            float Average(List<Vector3> from, Func<Vector3, float> walk)
            {
                var sum = 0f;
                foreach (var s in from)
                {
                    var w = walk(s);
                    if (float.IsNaN(w))
                        return float.NaN;
                    sum += w;
                }
                return sum / Math.Max(1, from.Count);
            }
            var targets = o.Hills.Select((h, i) => ($"hill {i + 1}", (Func<Vector3, float>)(s => WalkToHill(s, h)), (Func<Vector3, float>)(s => WalkToHill(s, h)))).ToList();
            if (o.Flags.Length == 2)
                targets.Add(("enemy flag", s => Walk(s, o.Flags[1]), s => Walk(s, o.Flags[0])));
            foreach (var (label, forBlue, forRed) in targets)
            {
                float blue = Average(o.TeamSpawns[0], forBlue), red = Average(o.TeamSpawns[1], forRed);
                walks.Add($"{label} {blue:0}/{red:0} m");
                if (float.IsNaN(blue) || float.IsNaN(red))
                    Fail($"{m.Name}: no route from a team's spawns to {label}: " + string.Join(", ",
                        o.TeamSpawns.SelectMany((l, team) => l.Where(sp => float.IsNaN((team == 0 ? forBlue : forRed)(sp)))
                            .Select(sp => g.Nearest(sp, needSight: false) is var k && k >= 0
                                ? $"({sp.X:0.0},{sp.Y:0.0},{sp.Z:0.0}) node ({g.Nodes[k].X:0.0},{g.Nodes[k].Y:0.00},{g.Nodes[k].Z:0.0}) c{g.Component[k]}/{g.MainComponent} {g.EdgeStart[k + 1] - g.EdgeStart[k]} links"
                                : $"({sp.X:0.0},{sp.Y:0.0},{sp.Z:0.0}) no node")).Take(6)));
                else if (t.WalkFairness > 0 && MathF.Abs(blue - red) > t.WalkFairness * MathF.Max(blue, red))
                    Fail($"{m.Name}: {label} is {blue:0} m from Blue and {red:0} m from Red (more than {t.WalkFairness * 100:0}% apart)");
            }
            return $"Blue/Red {string.Join(", ", walks)} (within {t.WalkFairness * 100:0}%)";
        });

        // Void maps: no nav link crosses the void, nothing below the fall-kill height; any fall-kill map: no link leads below it.
        if (m.FallKillY is { } killY)
            MapCheck(isVoid ? "void" : "fall-kill height", verbose, () =>
            {
                int links = 0, voidLinks = 0, intoVoid = 0;
                var example = "";
                for (var i = 0; i < g.Nodes.Length; i++)
                    for (var e = g.EdgeStart[i]; e < g.EdgeStart[i + 1]; e++)
                    {
                        var j = g.EdgeTo[e];
                        if (g.Nodes[i].Y >= killY && g.Nodes[j].Y < killY)
                            intoVoid++;
                        if (j < i || !isVoid)
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
                if (intoVoid > 0)
                    Fail($"{m.Name}: {intoVoid} nav links lead below the fall-kill height");
                if (voidLinks > 0)
                    Fail($"{m.Name}: {voidLinks} of {links} nav links cross the void, e.g. {example}");
                var low = isVoid ? g.Nodes.Count(n => n.Y < killY + 1f) : 0;
                if (low > 0)
                    Fail($"{m.Name}: {low} nav nodes below the fall-kill height");
                var open = new List<string>();
                var topY = m.Solid.Count == 0 ? killY + 60f : m.Solid.Max(b => b.Max.Y) + 1f;
                foreach (var v in t.VoidAt)
                    if (float.IsFinite(Floor(v.X, v.Y, topY, topY - killY)))
                        open.Add($"({v.X:0},{v.Y:0})");
                if (open.Count > 0)
                    Fail($"{m.Name}: floor found where the void should be open: {string.Join(", ", open)}");
                return isVoid ? $"{voidLinks}/{links} links over the void, {low} nodes below {killY:0.0} m, {intoVoid} links leading below it, {t.VoidAt.Length} void probes open"
                    : $"{intoVoid} links lead below {killY:0.0} m";
            });

        // Deck probes: floor where the map says, reached by the nav graph's main area.
        if (t.Probes.Length > 0)
            MapCheck("deck probes", verbose, () =>
            {
                var wrong = new List<string>();
                var unreached = new List<string>();
                var reach = MathF.Max(3f, g.Spacing * 1.5f);
                foreach (var (name, x, z, y) in t.Probes)
                {
                    var floor = Floor(x, z, y + 1.2f, 3f);
                    if (!(MathF.Abs(floor - y) < 0.1f))
                        wrong.Add($"{name} ({x:0},{z:0}): floor at {floor:0.00}, expected {y:0.00}");
                    var at = new Vector3(x, y, z);
                    var k = g.Nearest(at, needSight: false);
                    if (k < 0 || Vector3.Distance(g.Nodes[k], at) > reach || g.Component[k] != g.MainComponent && !t.NavPockets)
                        unreached.Add($"{name} ({x:0},{y:0.0},{z:0}): {(k < 0 ? "no node" : $"nearest node ({g.Nodes[k].X:0.0},{g.Nodes[k].Y:0.00},{g.Nodes[k].Z:0.0}) {(g.Component[k] != g.MainComponent ? "off the main area" : "too far")}")}");
                }
                if (wrong.Count > 0)
                    Fail($"{m.Name}: no deck where expected: {string.Join("; ", wrong.Take(6))}");
                if (unreached.Count > 0)
                    Fail($"{m.Name}: bots can't reach {string.Join("; ", unreached.Take(6))}");
                var ys = t.Probes.Select(p => p.Y).ToArray();
                return $"{t.Probes.Length} probes at {ys.Min():0.0}-{ys.Max():0.0} m, floor and nav where expected";
            });

        // The horizon (the all-maps "Map horizons" section's per-map check, here too so a focused run sees it).
        MapCheck("horizon", verbose, () =>
        {
            MapHorizonCheck(m);
            return bd == null ? "no boundary: the old fog" : m.HasTerrain ? "terrain runs past the outline" : m.FallKillY != null ? "void map: the sky below"
                : $"fog {m.FogStart:0}-{m.FogEnd:0} m, fade {m.OuterFog.Z:0}-{m.OuterFog.W:0} m from the centre, far plane {m.FarPlane:0} m";
        });

        // A short seeded bot match: nobody stuck, fallen or outside, no slow tick, no crash.
        string botText = "";
        if (t.BotMatchSeconds > 0 && m.Id < Maps.All.Length && ReferenceEquals(Maps.All[m.Id], m))
            MapCheck($"bot match ({t.BotMatchMode}, {t.BotMatchSeconds} s)", verbose, () =>
            {
                botText = MapContractBotMatch(m, t);
                return botText;
            });
        else if (t.BotMatchSeconds > 0)
            Line($"  (bot match skipped: the map isn't in the loaded list)");

        // The fall kill, in a game of its own: dropped at "drop" you die; standing at "safe" you don't.
        if (t.FallKill is var (drop, safe) && m.FallKillY is { } killHeight && m.Id < Maps.All.Length && ReferenceEquals(Maps.All[m.Id], m))
            MapCheck("fall kill", verbose, () =>
            {
                Game.SeedNextGame = 7100 + m.Id;
                using var game = ModeGame(0, GameMode.FreeForAll, m.Id);
                game.WarmupEnabled = true;
                var now = 0.0;
                Run(game, ref now, 1);
                game.ChooseLoadout(Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, ThrowableKind.None, ThrowableKind.None, 0, 0, now);
                Run(game, ref now, Game.CountdownSeconds + 0.3);
                var me = game.Local;
                if (me is not { Alive: true } || game.Phase != MatchPhase.Playing || World.Map != m)
                {
                    Fail($"{m.Name}: the fall-kill game didn't start");
                    return null;
                }
                me.ImmuneUntil = 0;
                var deaths = me.Deaths;
                me.Position = safe;
                me.Sim.Velocity = Vector3.Zero;
                Run(game, ref now, 1.5);
                var safeAlive = me.Alive && me.Position.Y > killHeight;
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
                if (!safeAlive)
                    Fail($"{m.Name}: a player standing at {safe} died or fell (kill height {killHeight}, now at y {safeY:0.0})");
                if (me.Alive || me.Deaths != deaths + 1 || game.FallKills < 1)
                    Fail($"{m.Name}: falling at {drop} didn't kill");
                return $"standing at ({safe.X:0},{safe.Z:0}) {(safeAlive ? $"alive at y {safeY:0.0}" : "DEAD")}; dropped at ({drop.X:0},{drop.Z:0}) {(me.Alive ? "still alive" : $"died after {fell:0.00} s")}, fall kills {game.FallKills}";
            });
        // Map logic: the test block's steps, played through on a server of its own (SelfTestMapLogic.cs).
        if (t.Events.Length > 0 && m.Id < Maps.All.Length && ReferenceEquals(Maps.All[m.Id], m))
            MapCheck($"map logic ({t.Events.Length} steps)", verbose, () => MapContractLogic(m, t));
        else if (t.Events.Length > 0)
            Line($"  (map logic steps skipped: the map isn't in the loaded list)");
        m.RestoreProps();
        World.SetMap(m);

        var summary = $"{m.Name}: {(bd == null ? "rect" : bd.Spec.Shape.ToString().ToLowerInvariant())} {m.PlayArea:0} m2 ({m.Size}), max {m.MaxPlayers}; {m.Solid.Count} boxes ({drawn} drawn), "
                      + $"{props.Count} props ({solid} solid), {m.MeshPlacements.Count} meshes, decals {bake.Placed}/{bake.Requested}, {m.Spawns.Count} spawns; "
                      + $"nav {g.Nodes.Length} nodes ({g.MainNodes.Length} main), {g.EdgeCount} edges, built in {g.BuildMs:0} ms; walks Blue/Red: {string.Join(", ", walks)}"
                      + (botText.Length > 0 ? "; bots: " + botText : "") + $" ({sw.Elapsed.TotalSeconds:0.0} s)";
        if (!verbose)
            Line("  " + summary);
        notes.Add(summary);
    }

    private static string TwinText(string symmetry) => symmetry switch { "mirrorX" => "(-x, z)", "mirrorZ" => "(x, -z)", _ => "(-x, -z)" };

    /// <summary>The seeded bot match: the block's mode, length and player count; returns the result line.</summary>
    private static string MapContractBotMatch(Map m, MapTestSpec t)
    {
        var players = Math.Clamp(t.BotMatchPlayers > 0 ? t.BotMatchPlayers : m.MaxPlayers, 2, m.MaxPlayers);
        var seconds = t.BotMatchSeconds;
        Game.SeedNextGame = 6500 + m.Id * 17 + players; // seeded: the same match every run (a flaky check is no check)
        var sw = Stopwatch.StartNew();
        using var game = ModeGame(players - 1, t.BotMatchMode, m.Id);
        NavGraph.Pump(double.MaxValue);
        Game.SettleMemory();
        const double dt = 1.0 / World.TickRate;
        var now = 0.0;
        var lastPos = new Dictionary<byte, Vector3>();
        var moved = new Dictionary<byte, float>();
        var cpu = new TickCpu();
        var maxTickMs = 0.0;
        var jitTicks = 0;
        int outsideTicks = 0, fallenTicks = 0;
        var b = m.Boundary;
        try
        {
            for (var i = 0; i < seconds * World.TickRate; i++)
            {
                now += dt;
                var jit0 = System.Runtime.JitInfo.GetCompilationTime(currentThread: true);
                cpu.Start();
                var t0 = Stopwatch.GetTimestamp();
                game.Update(now, dt);
                var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                var jit = (System.Runtime.JitInfo.GetCompilationTime(currentThread: true) - jit0).TotalMilliseconds > 1;
                if (jit)
                    jitTicks++;
                var measured = i > 60 && !jit;
                cpu.Stop(ms, count: measured);
                if (measured)
                    maxTickMs = Math.Max(maxTickMs, ms);
                foreach (var p in game.Players.Values)
                {
                    if (!p.Alive)
                        continue;
                    if (!float.IsFinite(p.Position.X + p.Position.Y + p.Position.Z))
                    {
                        Fail($"{m.Name}: player {p.Id} position is NaN");
                        return "NaN position";
                    }
                    if (b != null && !b.Inside(p.Position.X, p.Position.Z, -0.5f))
                        outsideTicks++;
                    if (m.FallKillY is { } kill ? p.Position.Y < kill - 0.5f : p.Position.Y < m.GroundHeight(p.Position.X, p.Position.Z) - 0.5f)
                        fallenTicks++;
                }
                if (i % 30 == 0)
                    foreach (var p in game.Players.Values.Where(p => p.Bot != null && p.Alive))
                    {
                        if (lastPos.TryGetValue(p.Id, out var lp))
                            moved[p.Id] = moved.GetValueOrDefault(p.Id) + Vector3.Distance(lp, p.Position);
                        lastPos[p.Id] = p.Position;
                    }
            }
        }
        catch (Exception ex)
        {
            Fail($"{m.Name}: {t.BotMatchMode} bot match crashed: {ex}");
            return "crashed";
        }
        var kills = game.Players.Values.Sum(p => (int)p.Kills);
        var stuck = moved.Count(kv => kv.Value < 8f);
        // The stuck watch's report (StuckSpots.cs): spots where a stuck spell reached a recovery stage. The limit counts the
        // ones a first hop and back-off didn't cure (stage 2 or more: 6 s stuck at the default timeout); a stage-1 spell is
        // usually two bots bumping in a doorway, and is listed but not counted.
        var spots = game.StuckSpotsThisMatch;
        var badSpots = spots.Where(x => x.MaxStage >= 2).ToList();
        var text = $"{t.BotMatchMode} {game.Players.Count} players, {seconds} s: kills {kills}, fall kills {game.FallKills}, avg bot travel {(moved.Count == 0 ? 0 : moved.Values.Average()):0} m, "
                   + $"stuck {stuck}, stuck spots {badSpots.Count} ({spots.Count} with stage 1){(spots.Count > 0 ? " [" + string.Join("; ", spots.Select(StuckSpots.Describe)) + "]" : "")}, player-ticks outside {outsideTicks}, below the floor {fallenTicks}, worst tick {maxTickMs:0.00} ms (work {cpu.WorstText}; {jitTicks} JIT ticks not counted), "
                   + $"broken props {m.BrokenProps.Count} ({sw.Elapsed.TotalSeconds:0.0} s)";
        if (game.Players.Count != players)
            Fail($"{m.Name}: the {t.BotMatchMode} match has {game.Players.Count} players (expected {players})");
        if (outsideTicks > 0 || fallenTicks > 0)
            Fail($"{m.Name}: players left the map in the bot match ({outsideTicks} player-ticks outside the outline, {fallenTicks} below the floor)");
        if (t.MaxStuckSpots >= 0 && badSpots.Count > t.MaxStuckSpots)
            Fail($"{m.Name}: bots got stuck (recovery stage 2 or more) at {badSpots.Count} spot(s) in the {t.BotMatchMode} bot match (allowed {t.MaxStuckSpots}): {string.Join("; ", badSpots.Select(StuckSpots.Describe))}");
        if (t.MaxStuckBots >= 0 && stuck > t.MaxStuckBots)
            Fail($"{m.Name}: {stuck} bots barely moved in a {t.BotMatchMode} match (allowed {t.MaxStuckBots})");
        if (cpu.WorstMs > MaxTickWorkMs)
            Fail($"{m.Name}: a {t.BotMatchMode} server tick did {cpu.WorstMs:0.0} ms of work (over {MaxTickWorkMs} ms; {cpu.WorstDetail})");
        m.RestoreProps();
        return text;
    }
}
