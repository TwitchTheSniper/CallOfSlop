using System.Diagnostics;
using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Destructible buildings (Source/Maps/MapDestruction*.cs, Source/Game/GameDestruction*.cs, Materials/destruction.json):
/// the material table, chunking (counts, determinism, budgets), the support graph standing as built, damage per material
/// (a frag breaks plaster but not concrete, a launcher holes brick, bullets chip wood only), holes letting bullets and
/// bodies through, collapse when the support goes (the floor falls, falling pieces hurt), replication (events replay the
/// same collapse, the join state), the reset each match, the host setting and Zombies, no allocations, the cost of a big
/// collapse, the World Tour maps within budget, and older packs untouched.
/// </summary>
public static partial class SelfTest
{
    private static void CheckDestruction()
    {
        var table = MaterialDestruction.Current;
        Line($"materials: {table.Materials.Count} from {table.Source} (hash {table.Hash:x8}); {string.Join(", ", table.Materials.Select(m => $"{m.Key} {m.Hp:0}"))}");
        foreach (var e in table.Errors)
            Fail($"destruction.json: {e}");
        foreach (var key in new[] { "Concrete", "Brick", "Wood", "Plaster", "Metal", "Tiles", "Default" })
            if (!table.Has(key))
                Fail($"destruction.json has no \"{key}\"");
        if (table.Source == "built-in")
            Fail("Materials/destruction.json wasn't found next to the game");
        if (table.IdOf("Grass") != table.IdOf("Dirt") || table.IdOf("NoSuchSurface") != table.IdOf("Default"))
            Fail("surface lookups don't fall back (Grass -> Dirt, unknown -> Default)");
        CheckChunker(table);
        if (Maps.Find("Destruction Test") is not { } map)
        {
            Fail("Maps/Dev/DestructionTest.json ('Destruction Test') didn't load");
            return;
        }
        CheckDestructionMap(map);
        CheckDestructionDamage(map);
        CheckDestructionCollapse(map);
        CheckDestructionReplication(map);
        CheckDestructionPacks();
        World.SetMap(Maps.All[0]);
    }

    /// <summary>Cutting boxes: counts, the world grid, no slivers, budgets, determinism.</summary>
    private static void CheckChunker(MaterialDestruction table)
    {
        var r = table.Rules;
        var pieces = new List<Box>();
        var wall = new Box(new Vector3(3, 1.5f, 0.15f), new Vector3(3, 1.5f, 0.15f)); // 6 x 3 m, 0.3 thick, from x 0
        var n = StructureChunker.Split(wall, r, 0, 1, pieces);
        var volume = pieces.Sum(p => p.Half.X * p.Half.Y * p.Half.Z * 8);
        var thinnest = pieces.Min(p => MathF.Min(p.Half.X, p.Half.Y) * 2);
        var again = new List<Box>();
        StructureChunker.Split(wall, r, 0, 1, again);
        var slab = new List<Box>();
        StructureChunker.Split(new Box(new Vector3(0, 3, 0), new Vector3(5, 0.1f, 4)), r, 0, 1, slab);
        var tiny = new List<Box>();
        StructureChunker.Split(new Box(new Vector3(0, 1, 0), new Vector3(0.2f, 0.2f, 0.2f)), r, 0, 1, tiny);
        Line($"chunker: a 6 x 3 m wall -> {n} panels (volume {volume:0.000} of 5.400, thinnest {thinnest:0.00} m), a 10 x 8 m slab -> {slab.Count} slabs, a small box -> {tiny.Count}; same again {pieces.SequenceEqual(again)}");
        if (n != 18 || MathF.Abs(volume - 5.4f) > 0.001f)
            Fail($"a 6 x 3 m wall should be 18 panels of 1 x 1.25 m (got {n}, volume {volume})");
        if (thinnest < r.MinPiece - 0.001f)
            Fail($"a sliver of {thinnest:0.000} m (minPiece {r.MinPiece})");
        if (!pieces.SequenceEqual(again))
            Fail("cutting the same box twice gave different chunks");
        if (slab.Count != 20 || tiny.Count != 1)
            Fail($"a 10 x 8 slab should be 20 slabs of 2 x 2 m and a small box 1 chunk (got {slab.Count}, {tiny.Count})");
        // A wall and its lining are cut in the same places (the grid is fixed in the world).
        var lining = new List<Box>();
        StructureChunker.Split(new Box(new Vector3(3, 1.5f, 0.31f), new Vector3(3, 1.5f, 0.01f)), r, 0, 1, lining);
        var aligned = lining.Count == pieces.Count && lining.Zip(pieces).All(z => MathF.Abs(z.First.Center.X - z.Second.Center.X) < 1e-4f && MathF.Abs(z.First.Center.Y - z.Second.Center.Y) < 1e-4f);
        if (!aligned)
            Fail("a wall's lining isn't cut where the wall is");
        // Huge boxes stay within the per-box cap.
        var huge = new List<Box>();
        StructureChunker.Split(new Box(new Vector3(0, 10, 0), new Vector3(60, 10, 0.2f)), r, 0, 1, huge);
        if (huge.Count > r.MaxPerBox)
            Fail($"a 120 m wall became {huge.Count} chunks (cap {r.MaxPerBox})");
    }

    private static List<int> ChunksOf(MapDestruction d, int s) => Enumerable.Range(d.Structures[s].ChunkStart, d.Structures[s].ChunkCount).ToList();

    private static int BlockAt(Map map, Vector3 p)
    {
        var d = map.Destruction!;
        foreach (var st in d.Structures)
            if (st.Building < 0 && p.X >= st.Bounds.Min.X - 0.01f && p.X <= st.Bounds.Max.X + 0.01f && p.Z >= st.Bounds.Min.Z - 0.01f && p.Z <= st.Bounds.Max.Z + 0.01f)
                return st.Index;
        return -1;
    }

    /// <summary>The Dev map: chunks, the support graph, standing as built, determinism, costs.</summary>
    private static void CheckDestructionMap(Map map)
    {
        var d = map.Destruction;
        if (d == null)
        {
            Fail("Destruction Test has no destructible chunks");
            return;
        }
        var buildings = d.Structures.Count(s => s.Building >= 0);
        var blocks = d.Structures.Count(s => s.Building < 0);
        var skins = Enumerable.Range(0, d.Count).Count(c => (d.Flags[c] & MapDestruction.FlagSkin) != 0);
        var fixedN = Enumerable.Range(0, d.Count).Count(c => (d.Flags[c] & MapDestruction.FlagFixed) != 0);
        var floors = Enumerable.Range(0, d.Count).Count(c => (d.Flags[c] & MapDestruction.FlagFloor) != 0);
        var staticN = Enumerable.Range(0, d.Count).Count(c => (d.Flags[c] & MapDestruction.FlagStatic) != 0);
        var orphanSkins = Enumerable.Range(0, d.Count).Count(c => (d.Flags[c] & MapDestruction.FlagSkin) != 0 && d.Host[c] < 0);
        Line($"Destruction Test: {buildings} destructible buildings + {blocks} blocks, {d.Count} chunks ({skins} linings, {orphanSkins} without a wall, {floors} floor/roof slabs, "
             + $"{fixedN} load-bearing, {staticN} free-standing trim), {d.EdgeCount} support links, {map.Solid.Count} boxes in all; graph built in {d.BuildMs:0.0} ms");
        foreach (var st in d.Structures.Where(s => s.Building >= 0))
            Line($"  {map.PlacedBuildings[st.Building].Def.Name,-22} {st.ChunkCount,5} chunks (was {map.PlacedBuildings[st.Building].Plan.Shell.Count} boxes), longest support path as built {st.MaxSpanAsBuilt:0.0} m");
        if (buildings != 7 || blocks != 5)
            Fail($"expected 7 destructible buildings and 5 blocks (the Small House with \"destructible\": false stays whole), got {buildings} and {blocks}");
        var whole = map.PlacedBuildings.FirstOrDefault(b => b.Structure < 0);
        if (whole == null || whole.ShellCount != whole.Plan.Shell.Count)
            Fail("the building with \"destructible\": false wasn't left as built");
        // Every chunk of a structure lies in its Solid range and is a building box drawn by the building renderer.
        var bad = Enumerable.Range(0, d.Count).Count(c => !map.IsBuildingBox(d.Solid[c]) || map.IsDrawnBox(d.Solid[c]) || d.ChunkOfSolid[d.Solid[c]] != c);
        if (bad > 0)
            Fail($"{bad} chunks aren't building boxes (or are drawn as map boxes)");
        // Standing as built: nothing falls on a fresh map.
        var falling = new List<int>();
        for (var s = 0; s < d.Structures.Count; s++)
            d.Unsupported(s, falling);
        if (falling.Count > 0)
            Fail($"{falling.Count} chunks fall on a fresh map (first: {d.Original[falling[0]].Center})");
        // Determinism: a second load cuts exactly the same chunks, with the same fingerprint.
        var copy = MapFile.Load(File.ReadAllText(Path.Combine(Maps.Folder, map.SourceFile!)), map.SourceFile!);
        var same = copy.Destruction is { } cd && cd.Count == d.Count && Enumerable.Range(0, d.Count).All(c => cd.Original[c] == d.Original[c] && cd.Mat[c] == d.Mat[c] && cd.Flags[c] == d.Flags[c])
                   && cd.AdjTo.SequenceEqual(d.AdjTo) && copy.ContentHash == map.ContentHash;
        Line($"a second load: the same chunks, links and fingerprint {same} ({map.ContentHash:x8})");
        if (!same)
            Fail("loading the Destruction Test twice cut different chunks");
        // The renderer's static instances: one run per building and block, broken chunks written with no size.
        var (_, runs) = BuildingRenderer.BuildStatic(map);
        if (runs.Length != map.PlacedBuildings.Count + blocks || runs.Sum(x => x.Count) != map.PlacedBuildings.Sum(b => b.ShellCount) + d.Structures.Where(s => s.Building < 0).Sum(s => s.SolidCount))
            Fail($"the building renderer has {runs.Length} runs of {runs.Sum(x => x.Count)} instances for {map.PlacedBuildings.Count} buildings + {blocks} blocks");
    }

    /// <summary>Damage per material: frag vs plaster and concrete, a launcher vs brick, bullets, melee, holes, the setting, Zombies, allocations.</summary>
    private static void CheckDestructionDamage(Map map)
    {
        World.SetMap(map);
        var d = map.Destruction!;
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id);
        var me = game.Players[0];
        me.Position = new Vector3(0, 0, -36);
        var frag = Throwables.Get(ThrowableKind.Frag);
        var fragKill = (byte)(Throwables.KillIdBase + (byte)ThrowableKind.Frag);
        int Broken(int s) => d.Structures[s].BrokenCount;

        var plaster = BlockAt(map, new Vector3(11, 0, 14.1f));
        var concrete = BlockAt(map, new Vector3(27, 0, 14.1f));
        var brick = BlockAt(map, new Vector3(3, 0, 14.1f));
        var wood = BlockAt(map, new Vector3(19, 0, 14.1f));
        if (plaster < 0 || concrete < 0 || brick < 0 || wood < 0)
        {
            Fail("the test walls of the Destruction Test aren't destructible blocks");
            return;
        }
        // A frag 0.6 m in front of each wall.
        game.BlastStructures(me, new Vector3(11, 1.2f, 13.4f), frag.Radius, frag.MaxDamage, StructureHit.Grenade, fragKill, 0);
        game.BlastStructures(me, new Vector3(27, 1.2f, 13.4f), frag.Radius, frag.MaxDamage, StructureHit.Grenade, fragKill, 0);
        Line($"a frag 0.6 m from a plaster wall breaks {Broken(plaster)} of its {d.Structures[plaster].ChunkCount} chunks; from a concrete wall {Broken(concrete)} of {d.Structures[concrete].ChunkCount}");
        if (Broken(plaster) == 0)
            Fail("a frag next to a plaster wall broke nothing");
        if (Broken(concrete) != 0)
            Fail("a frag broke a concrete wall");
        if (Broken(plaster) >= d.Structures[plaster].ChunkCount)
            Fail("a frag took down the whole plaster wall (it should blow a hole)");
        // The hole: bullets and sight pass where a chunk broke, and a body fits through a big enough one.
        var hole = Enumerable.Range(d.Structures[plaster].ChunkStart, d.Structures[plaster].ChunkCount).Where(c => d.Broken[c]).OrderBy(c => d.Original[c].Center.Y).First();
        var hb = d.Original[hole];
        var shot = World.Query.Raycast(new Vector3(hb.Center.X, hb.Center.Y, 13f), Vector3.UnitZ, 3f, QueryFilter.Shots);
        var intact = Enumerable.Range(d.Structures[plaster].ChunkStart, d.Structures[plaster].ChunkCount).First(c => !d.Broken[c]);
        var ib = d.Original[intact];
        var blocked = World.Query.Raycast(new Vector3(ib.Center.X, ib.Center.Y, 13f), Vector3.UnitZ, 3f, QueryFilter.Shots);
        Line($"through the hole a bullet flies {shot:0.00} m (the wall is 1 m away); at an intact chunk it stops at {blocked:0.00} m");
        if (shot < 2.9f)
            Fail("a bullet doesn't pass through a blasted hole");
        if (blocked > 1.05f)
            Fail("an intact chunk doesn't stop bullets");
        // A launcher rocket at a brick wall holes it; one at concrete only scratches.
        var rpg = Weapons.All.FirstOrDefault(w => w.IsLauncher && w.Name.Contains("RPG", StringComparison.OrdinalIgnoreCase)) ?? Weapons.All.First(w => w.IsLauncher);
        game.BlastStructures(me, new Vector3(3, 1.5f, 13.9f), rpg.LauncherRadius, rpg.Damage, StructureHit.Launcher, rpg.Id, 0);
        var concreteHp = Enumerable.Range(d.Structures[concrete].ChunkStart, d.Structures[concrete].ChunkCount).Min(c => d.Hp[c] / d.MaxHp[c]);
        game.BlastStructures(me, new Vector3(27, 1.5f, 13.9f), rpg.LauncherRadius, rpg.Damage, StructureHit.Launcher, rpg.Id, 0);
        var concreteAfter = Enumerable.Range(d.Structures[concrete].ChunkStart, d.Structures[concrete].ChunkCount).Min(c => d.Hp[c] / d.MaxHp[c]);
        Line($"a {rpg.Name} rocket at the brick wall breaks {Broken(brick)} chunks; at the concrete wall {Broken(concrete)} (its weakest chunk {concreteHp:P0} -> {concreteAfter:P0} health)");
        if (Broken(brick) == 0)
            Fail("a launcher rocket didn't hole a brick wall");
        if (concreteAfter >= concreteHp)
            Fail("a launcher rocket did no damage to concrete");
        // Bullets: wood chips and gives way, concrete takes nothing.
        var woodChunk = Enumerable.Range(d.Structures[wood].ChunkStart, d.Structures[wood].ChunkCount).First(c => d.Original[c].Min.Y > 0.5f);
        var wb = d.Original[woodChunk];
        var from = new Vector3(wb.Center.X, wb.Center.Y, 12f);
        var shots = 0;
        while (!d.Broken[woodChunk] && shots < 100)
        {
            var dist = World.Query.Raycast(from, Vector3.UnitZ, 10f, QueryFilter.Shots);
            game.ShotStructures(me, from, Vector3.UnitZ, dist, 30, Weapons.DefaultPrimary.Id, 0);
            shots++;
        }
        var cc = Enumerable.Range(d.Structures[concrete].ChunkStart, d.Structures[concrete].ChunkCount).First(c => !d.Broken[c] && d.Original[c].Min.Y > 0.5f);
        var cb = d.Original[cc];
        var hpBefore = d.Hp[cc];
        var cfrom = new Vector3(cb.Center.X, cb.Center.Y, 12f);
        for (var k = 0; k < 20; k++)
            game.ShotStructures(me, cfrom, Vector3.UnitZ, World.Query.Raycast(cfrom, Vector3.UnitZ, 10f, QueryFilter.Shots), 30, Weapons.DefaultPrimary.Id, 0);
        Line($"rifle bullets (30 damage): a wood panel gives way after {shots} hits; 20 hits on concrete take {hpBefore - d.Hp[cc]:0} health off it");
        if (!d.Broken[woodChunk] || shots > 40)
            Fail($"bullets should break a wood panel within 40 hits (took {shots})");
        if (d.Hp[cc] != hpBefore)
            Fail("bullets damaged concrete");
        // Melee: plaster gives way to a few strikes.
        var pc = Enumerable.Range(d.Structures[plaster].ChunkStart, d.Structures[plaster].ChunkCount).First(c => !d.Broken[c] && d.Original[c].Min.Y > 0.9f);
        var pb = d.Original[pc];
        var strikes = 0;
        while (!d.Broken[pc] && strikes < 20)
        {
            game.MeleeStructures(me, new Vector3(pb.Center.X, pb.Center.Y, 13.3f), Vector3.UnitZ, 0);
            strikes++;
        }
        Line($"melee: a plaster panel gives way after {strikes} strikes");
        if (!d.Broken[pc] || strikes > 8)
            Fail($"melee should break plaster within 8 strikes (took {strikes})");

        // The host setting and Zombies: nothing breaks.
        var before = d.Structures.Sum(s => s.BrokenCount);
        game.DestructionEnabled = false;
        game.BlastStructures(me, new Vector3(3, 1.5f, 13.9f), 9, 2000, StructureHit.Streak, fragKill, 0);
        game.DestructionEnabled = true;
        var zombiesLive = Game.DestructionAllowedIn(GameMode.Zombies);
        Line($"the host's Destruction setting off: a huge blast breaks {d.Structures.Sum(s => s.BrokenCount) - before}; Zombies: destruction live {zombiesLive}");
        if (d.Structures.Sum(s => s.BrokenCount) != before)
            Fail("with Destruction off, a blast still broke chunks");
        if (zombiesLive)
            Fail("destruction is on in Zombies");

        // No allocations in steady state: blasts and shots that break nothing, and the per-tick work.
        var concreteCentre = new Vector3(27, 1.2f, 12.5f); // 1.5 m out: it hurts the concrete a little, never enough to break it here
        for (var warm = 0; warm < 3; warm++)
        {
            game.BlastStructures(me, concreteCentre, frag.Radius, frag.MaxDamage, StructureHit.Grenade, fragKill, 0);
            game.ShotStructures(me, cfrom, Vector3.UnitZ, 2, 30, Weapons.DefaultPrimary.Id, 0);
            game.SelfTestTickDestruction(1);
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var k = 0; k < 10; k++)
        {
            game.BlastStructures(me, concreteCentre, frag.Radius, frag.MaxDamage, StructureHit.Grenade, fragKill, 0);
            game.ShotStructures(me, cfrom, Vector3.UnitZ, 2, 30, Weapons.DefaultPrimary.Id, 0);
            game.SelfTestTickDestruction(1);
        }
        var ms = sw.Elapsed.TotalMilliseconds / 10;
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Line($"steady state (a frag at concrete, a bullet, the tick): {bytes} bytes allocated over 10 rounds, {ms:0.000} ms a round");
        if (bytes > 0)
            Fail($"destruction allocates {bytes} bytes in steady state");
        if (ms > 2)
            Fail($"a frag next to a building costs {ms:0.00} ms of server time (budget 2 ms)");

        // A new match: every chunk is back, collision too.
        map.RestoreProps();
        var left = d.Structures.Sum(s => s.BrokenCount) + Enumerable.Range(0, d.Count).Count(c => d.Broken[c] || map.Solid[d.Solid[c]] != d.Original[c] || d.Hp[c] != d.MaxHp[c]);
        var back = World.Query.Raycast(new Vector3(hb.Center.X, hb.Center.Y, 13f), Vector3.UnitZ, 3f, QueryFilter.Shots);
        Line($"after the reset: {left} chunks not as built; the hole stops bullets again at {back:0.00} m");
        if (left > 0 || back > 1.05f)
            Fail("a new match didn't put the chunks back");
    }

    /// <summary>Collapse: the brick tower loses its base, the apartment block loses its ground floor; falling pieces hurt; the cost.</summary>
    private static void CheckDestructionCollapse(Map map)
    {
        World.SetMap(map);
        var d = map.Destruction!;
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id);
        var me = game.Players[0];

        // The brick tower: break the whole bottom row and the rest has nothing to stand on.
        var tower = BlockAt(map, new Vector3(35.5f, 0, 21.5f));
        if (tower < 0)
        {
            Fail("no brick tower on the Destruction Test");
            return;
        }
        var bottom = ChunksOf(d, tower).Where(c => d.Original[c].Min.Y < 0.05f).ToList();
        me.Position = new Vector3(0, 0, -36);
        game.SelfTestBreakChunks(bottom, StructureHit.Launcher, new Vector3(35.5f, 1, 18), 0);
        var towerLeft = ChunksOf(d, tower).Count(c => !d.Broken[c]);
        Line($"brick tower: {d.Structures[tower].ChunkCount} chunks; breaking its {bottom.Count} bottom chunks brings down the rest: {towerLeft} left standing, {game.ChunksFallen} fell, {game.FallsPending} falling pieces tracked");
        if (towerLeft != 0)
            Fail($"the tower stands on nothing ({towerLeft} chunks left)");

        // The apartment block (4 storeys): knock out every breakable ground-floor wall and the floors above fall in.
        var apt = map.PlacedBuildings.First(b => b.Def.Name == "Apartment Block" && b.Structure >= 0);
        var s = apt.Structure;
        var level1 = apt.Plan.BaseY + BuildingGen.GroundSlab + apt.Def.StoreyHeight;
        var ground = ChunksOf(d, s).Where(c => d.Original[c].Min.Y < level1 - 0.25f && d.Original[c].Max.Y > apt.Plan.BaseY + 0.3f
                                               && (d.Flags[c] & (MapDestruction.FlagFixed | MapDestruction.FlagSkin | MapDestruction.FlagFloor)) == 0).ToList();
        var floor1 = ChunksOf(d, s).Where(c => (d.Flags[c] & MapDestruction.FlagFloor) != 0 && MathF.Abs(d.Original[c].Max.Y - level1) < 0.05f).ToList();
        var fallenBefore = game.ChunksFallen;
        var sw = Stopwatch.StartNew();
        game.SelfTestBreakChunks(ground, StructureHit.Streak, apt.Plan.Bounds.Center + new Vector3(0, 0, 12), 1);
        var ms = sw.Elapsed.TotalMilliseconds;
        var fallen = game.ChunksFallen - fallenBefore;
        var floorFell = floor1.Count(c => d.Broken[c]);
        var pending = game.FallsPending;
        // Someone standing on the ground floor under a piece of floor 1 that is coming down (it lands about 0.7 s later).
        if (floor1.FirstOrDefault(c => d.Broken[c]) is var down && floorFell > 0)
        {
            var under = d.Original[down];
            me.Position = new Vector3(under.Center.X, apt.Plan.BaseY + BuildingGen.GroundSlab, under.Center.Z);
        }
        me.Health = World.MaxHealth;
        me.Alive = true;
        me.ImmuneUntil = 0;
        // Let the pieces land.
        for (var t = 1.0; t < 4.0; t += World.TickDt)
            game.SelfTestTickDestruction(t);
        Line($"apartment block, {apt.Plan.Floors} storeys, {d.Structures[s].ChunkCount} chunks: {ground.Count} ground-floor wall chunks broken -> {fallen} chunks fell "
             + $"({floorFell} of {floor1.Count} floor-1 slabs) in {ms:0.0} ms (break, flood, debris, scheduling); {pending} falling pieces tracked; "
             + $"the player under floor 1: {(me.Alive ? $"{me.Health} health" : "killed")}, {game.FallHits} hits");
        if (floorFell == 0)
            Fail("the floor above didn't fall when the ground floor's walls were gone");
        if (fallen < floor1.Count / 2)
            Fail($"only {fallen} chunks fell when the ground floor went");
        if (game.FallHits == 0 || (me.Alive && me.Health >= World.MaxHealth))
            Fail("a falling floor didn't hurt the player under it");
        if (ms > 60)
            Fail($"a four-storey collapse took {ms:0.0} ms on the server (budget 60 ms)");
        // Load-bearing corners never break.
        var corners = ChunksOf(d, s).Count(c => (d.Flags[c] & MapDestruction.FlagFixed) != 0 && d.Broken[c]);
        if (corners > 0)
            Fail($"{corners} load-bearing chunks broke");
        map.RestoreProps();
        if (ChunksOf(d, s).Any(c => d.Broken[c]))
            Fail("the reset didn't stand the apartment block back up");
    }

    /// <summary>Replication: a break event replays the same collapse on another copy of the map; the join state reproduces everything.</summary>
    private static void CheckDestructionReplication(Map map)
    {
        World.SetMap(map);
        var d = map.Destruction!;
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id);
        var me = game.Players[0];
        me.Position = new Vector3(0, 0, -36);
        var copy = MapFile.Load(File.ReadAllText(Path.Combine(Maps.Folder, map.SourceFile!)), map.SourceFile!);
        var cd = copy.Destruction!;
        // The Office Building: break a column of wall through every floor on the server, send it as an event to the copy.
        var office = map.PlacedBuildings.First(b => b.Def.Name == "Office Building" && b.Structure >= 0);
        var s = office.Structure;
        var st = d.Structures[s];
        var cx = office.Plan.Bounds.Center.X;
        var pick = ChunksOf(d, s).Where(c => (d.Flags[c] & (MapDestruction.FlagFixed | MapDestruction.FlagSkin | MapDestruction.FlagFloor)) == 0
                                             && MathF.Abs(d.Original[c].Center.X - cx) < 4f).ToList();
        game.SelfTestBreakChunks(pick, StructureHit.Launcher, office.Plan.Bounds.Center + new Vector3(0, 2, 10), 0);
        var w = new PacketWriter();
        Game.WriteStructureBreak(w, s, StructureHit.Launcher, office.Plan.Bounds.Center, st.ChunkStart, pick, 0, Math.Min(pick.Count, Game.StructBreakBatch));
        var size = w.Length;
        var pr = new PacketReader(w.Span[1..]);
        if (w.Span[0] != (byte)Msg.StructBreak)
            Fail("a break event has the wrong id");
        game.ReadStructureBreak(copy, ref pr);
        var sameAfterEvent = Enumerable.Range(st.ChunkStart, st.ChunkCount).All(c => cd.Broken[c] == d.Broken[c]);
        Line($"a break event ({pick.Count} chunks, {size} bytes) on another copy of the map: {st.BrokenCount} broken or fallen on the server, {cd.Structures[s].BrokenCount} on the copy; same chunks {sameAfterEvent}");
        if (!sameAfterEvent || pick.Count > Game.StructBreakBatch)
            Fail("a client replaying the break event didn't end up with the server's chunks (the collapse isn't deterministic)");
        // Join in progress: the state packets rebuild every structure on a third copy.
        game.BlastStructures(me, new Vector3(11, 1.2f, 13.4f), 9, 200, StructureHit.Grenade, 0, 0);
        var join = MapFile.Load(File.ReadAllText(Path.Combine(Maps.Folder, map.SourceFile!)), map.SourceFile!);
        var packets = 0;
        var biggest = 0;
        Game.WriteStructureState(w, map, pw =>
        {
            packets++;
            biggest = Math.Max(biggest, pw.Length);
            var r = new PacketReader(pw.Span[1..]);
            Game.ReadStructureState(join, ref r);
        });
        var jd = join.Destruction!;
        var same = Enumerable.Range(0, d.Count).All(c => jd.Broken[c] == d.Broken[c]) && Enumerable.Range(0, map.Solid.Count).All(i => join.Solid[i] == map.Solid[i]);
        Line($"join in progress: {d.Structures.Count(x => x.BrokenCount > 0)} damaged structures in {packets} packets (biggest {biggest} bytes): chunks and collision boxes match {same}; protocol {ServerInfo.ProtocolVersion}");
        if (!same)
            Fail("the join-in-progress state doesn't reproduce the broken chunks");
        if (biggest > 1400)
            Fail($"a structure state packet is {biggest} bytes (over 1400)");
        if (ServerInfo.ProtocolVersion < 32)
            Fail("the protocol wasn't bumped for destruction");
        // Junk doesn't throw.
        var junk = new byte[] { (byte)Msg.StructState, 0xFF, 0xFF, 0x05, 0x00, 1 };
        var jr = new PacketReader(junk.AsSpan(1));
        Game.ReadStructureState(join, ref jr);
        var junk2 = new byte[] { (byte)Msg.StructBreak, 0, 0, 9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF, 1, 2 };
        var jr2 = new PacketReader(junk2.AsSpan(1));
        game.ReadStructureBreak(join, ref jr2);
        map.RestoreProps();
    }

    /// <summary>The World Tour maps are destructible and within budget, stand as built; older packs are untouched.</summary>
    private static void CheckDestructionPacks()
    {
        var budget = MaterialDestruction.Current.Rules.MaxPerMap;
        foreach (var m in Maps.All.Where(m => m.Pack?.Folder == "World"))
        {
            var d = m.Destruction;
            if (d == null)
            {
                Fail($"{m.Name} (World Tour) has no destructible buildings");
                continue;
            }
            var falling = new List<int>();
            for (var s = 0; s < d.Structures.Count; s++)
                d.Unsupported(s, falling);
            Line($"  {m.Name,-14} {d.Structures.Count,3} destructible buildings, {d.Count,6} chunks of {m.Solid.Count,6} boxes, {d.EdgeCount,6} links, graph {d.BuildMs,5:0.0} ms, "
                 + $"{m.DestructionOverBudget} over budget, {falling.Count} falling as built");
            if (d.Count > budget)
                Fail($"{m.Name}: {d.Count} chunks (budget {budget})");
            if (falling.Count > 0)
                Fail($"{m.Name}: {falling.Count} chunks fall as built");
        }
        var untouched = Maps.All.Where(m => m.Pack?.Folder != "World" && m.Name != "Destruction Test").ToList();
        var touched = untouched.Count(m => m.Destruction != null || m.DestructionHash != 0 || m.Destructible);
        Line($"{untouched.Count} maps in other packs: {touched} with destruction (their fingerprints are pinned in \"Pack assets\")");
        if (touched > 0)
            Fail($"{touched} maps outside the World Tour have destruction on");
    }
}
