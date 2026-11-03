using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    private const string GatherMap = "Proving Ground";

    private static (uint Key, Vector3 At)? FirstTree(ResourceField f, float half)
    {
        var n = (int)(half / ResourceField.TreeSpacing);
        for (var iz = -n; iz < n; iz++)
            for (var ix = -n; ix < n; ix++)
                if (f.TreeAt(ix, iz, out var x, out var z, out _))
                    return (ResourceField.TreeKey(ix, iz), new Vector3(x, f.Ground(x, z), z));
        return null;
    }

    private static Vector2? FirstUnderground(ResourceField f, float half, bool wanted, string? name = null)
    {
        for (var z = -half; z < half; z += 8)
            for (var x = -half; x < half; x += 8)
            {
                var k = f.KindAt(x, z, false);
                if ((k != null) == wanted && (name == null || k?.Name == name))
                    return new Vector2(x, z);
            }
        return null;
    }

    private static Construct GatherBody(ConstructWorld w, string part, Vector3 workAt)
    {
        var c = ConstructBuild.PlaceNew(w, ConstructParts.Need(part), workAt, 0)!;
        c.Anchored = true;
        c.Dirty = true;
        c.Rebuild();
        ConstructGatherer.WorkBox(c, 0, out var at, out _, out _);
        c.Pos += workAt - at;
        c.Vel = Vector3.Zero;
        return c;
    }

    /// <summary>
    /// "Gathering" (decision 14; docs/sandbox/CONSTRUCTS.md "Gathering"): the same seed gives the same field on two builds of
    /// it, deposits are grouped (a tree has neighbours), a small world generates nothing, a map can turn generation off and
    /// place its own, bad tables and map blocks are refused; a Saw Module at a tree cuts it and fells it, wood by the
    /// table's units; away from trees it idles; a Drill Module over a vein fills with ore, and off a deposit or off the
    /// ground it doesn't; survival with no power stops both; a full bin stops; a felled tree stays felled until reset; the
    /// step allocates nothing while drilling. A felled tree leaves a stump; trees are solid to everything that collides and
    /// aren't map data; they grow back by the table (GatherStump, GatherSolid, GatherRegrow).
    /// </summary>
    private static void CheckGathering()
    {
        if (Maps.Find(GatherMap) is not { } map || map.Sandbox == null || Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox == null)
        {
            Fail("gathering: the Sandbox maps are missing");
            return;
        }
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        map.RestoreProps();
        World.SetMap(map);
        var table = ResourceTable.Current;
        Want(table.Problem.Length == 0 && table.Kinds.Count >= 3, $"the table: \"{table.Problem}\", {table.Kinds.Count} kinds");
        Want(ConstructParts.Need("Saw Module").IsGatherer && ConstructParts.Need("Drill Module").IsGatherer, "the parts aren't gathering modules");
        var f = map.Resources!;
        var f2 = new ResourceField(map, table, map.Sandbox.Resources);
        var trees = new List<Vector2>();
        var half = (int)(map.HalfX / ResourceField.TreeSpacing);
        for (var iz = -half; iz < half; iz++)
            for (var ix = -half; ix < half; ix++)
            {
                var a = f.TreeAt(ix, iz, out var x, out var z, out _);
                Want(a == f2.TreeAt(ix, iz, out var x2, out var z2, out _) && (!a || x == x2 && z == z2), $"two fields differ at {ix},{iz}");
                if (a)
                    trees.Add(new Vector2(x, z));
            }
        var near = trees.Count(t => trees.Count(o => Vector2.Distance(o, t) < 15f) >= 4);
        Line($"gathering: {f.DepositCount} deposits on {GatherMap}, {trees.Count} trees, {near} with neighbours; first at {(trees.Count > 0 ? trees[0] : default)}");
        Want(trees.Count >= 150, $"only {trees.Count} trees on {GatherMap}: not plentiful");
        Want(near >= trees.Count * 0.85f, $"trees aren't grouped: {near} of {trees.Count} have neighbours");
        Want(new ResourceField(yard, table, null).DepositCount == 0, "the small yard generated deposits");
        f.DrawAround(new Vector3(float.MaxValue / 2));
        f.DrawAround(Vector3.Zero);
        var canopies = f.Drawn.Count(d => Foliage.IsTree(d.Type));
        var trunks = f.Drawn.Count(d => d.Type == FoliageType.Trunk);
        Want(canopies > 0 && trunks == canopies, $"drawn trees: {canopies} canopies, {trunks} trunks");
        Want(FoliageMeshes.Triangles(FoliageType.Trunk, 0) > 0, "the trunk mesh is empty");
        GatherOutcrops(map, f, f2, Want); // (SelfTestGatheringDrill.cs)
        var rules = new SandboxResources { Generate = false, Deposits = [new SandboxDeposit("forest", 0, 0, 20, 1, 0)] };
        var placed = new ResourceField(map, table, rules);
        Want(placed.DepositCount == 1 && placed.KindAt(0, 0, true)?.Name == "forest", "a map's own deposit wasn't used alone");
        Want(ResourceTable.Parse("{\"format\":\"nope\"}").Problem.Length > 0, "a bad table was accepted");
        Want(ResourceTable.Parse("{\"format\":\"" + ResourceTable.Format + "\",\"kinds\":[{\"name\":\"x\",\"material\":\"gold\",\"layer\":\"surface\",\"cell\":99,\"chance\":1,\"radius\":[9,9],\"aspect\":[1,1],\"units\":1}]}").Problem.Length > 0, "an unknown material was accepted");

        var w = new ConstructWorld { Resources = f, CreativeRecycling = true };
        var q = World.Query;
        void Run(float seconds)
        {
            for (var t = 0; t < seconds * World.TickRate; t++)
                w.Step(q, World.TickDt);
        }
        if (FirstTree(f, map.HalfX) is not { } tree || FirstUnderground(f, map.HalfX, true, "iron vein") is not { } vein || FirstUnderground(f, map.HalfX, false) is not { } bare)
        {
            Fail("gathering: no tree, vein or bare ground on the map");
            return;
        }
        var wood = RecyclingTable.Current.IdOf("wood");
        var ore = RecyclingTable.Current.IdOf("iron ore");
        Want(ore != 0, "iron ore isn't a material");

        // ---- The saw.
        var saw = GatherBody(w, "Saw Module", tree.At + new Vector3(0, 1.5f, 0));
        var st = w.StoreOf(saw, 0)!;
        Run(1.5f);
        Want(st.Busy && st.Material == wood && st.Amount > 0, $"the saw isn't cutting (busy {st.Busy}, {st.Amount} of {st.Material})");
        var whole = table.Kinds.Find(k => k.Surface)!.Units;
        Run(whole / ConstructParts.Need("Saw Module").GatherRate + 0.5f);
        Want(f.Felled.Contains(tree.Key), "the tree didn't fall");
        Want(w.FelledTrees.Contains(tree.Key), "the fall wasn't queued for the clients");
        Want(st.Amount >= whole, $"a tree gave {st.Amount}, want at least {whole}");
        w.Remove(saw);

        // ---- The drill.
        var drill = GatherBody(w, "Drill Module", new Vector3(vein.X, f.Ground(vein.X, vein.Y) - 0.3f, vein.Y));
        var ds = w.StoreOf(drill, 0)!;
        Run(2);
        Want(ds.Material == ore && ds.Amount > 0, $"the drill got {ds.Amount} of {ds.Material} over a vein");
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 0; t < 60 * 6; t += ConstructGatherer.Every)
            for (var s = 0; s < ConstructGatherer.Every; s++)
                ConstructGatherer.Step(w);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Line($"gathering: drill {ds.Amount} {RecyclingTable.Current.NameOf(ds.Material)}, step allocated {bytes} B");
        Want(bytes == 0, $"the drilling step allocates ({bytes} B)");
        w.CreativeRecycling = false;
        var held = ds.Amount;
        Run(1);
        Want(ds.Amount == held && !ds.Busy, "a drill with no power kept working in survival");
        w.CreativeRecycling = true;
        ds.Amount = ConstructParts.Need("Drill Module").BulkCapacity;
        Run(0.5f);
        Want(ds.Amount == ConstructParts.Need("Drill Module").BulkCapacity && !ds.Busy, "a full drill kept working");
        w.Remove(drill);
        var off = GatherBody(w, "Drill Module", new Vector3(bare.X, f.Ground(bare.X, bare.Y) - 0.3f, bare.Y));
        var high = GatherBody(w, "Drill Module", new Vector3(vein.X + 3, f.Ground(vein.X + 3, vein.Y) + 3, vein.Y));
        for (var t = 0; t < World.TickRate; t++)
            ConstructGatherer.Step(w); // (the gather step alone: a body hanging in the air would fall to the ground otherwise)
        Want(w.StoreOf(off, 0)!.Amount == 0, "a drill off any deposit gathered");
        Want(w.StoreOf(high, 0)!.Amount == 0, "a drill off the ground gathered");
        Want(f.NearestUnderground(bare.X, bare.Y).Contains(" m "), $"the look hint: \"{f.NearestUnderground(bare.X, bare.Y)}\"");
        GatherStump(f, tree, Want);
        GatherShaft(map, f, vein, Want); // (SelfTestGatheringDrill.cs)
        map.RestoreProps();
        Want(f.Felled.Count == 0, "a new match kept the felled trees");
        GatherTable(Want);
        GatherStreamed(table, Want);
        GatherSolid(yard, Want);
        GatherRegrow(yard, Want);
        foreach (var p in problems)
            Fail($"gathering: {p}");
    }

    /// <summary>A felled tree leaves a stump where it stood; a stump is drawn, not solid, no saw's work, and no map file can place one.</summary>
    private static void GatherStump(ResourceField f, (uint Key, Vector3 At) tree, Action<bool, string> want)
    {
        f.DrawAround(new Vector3(float.MaxValue / 2));
        f.DrawAround(Vector3.Zero);
        var stumps = f.Drawn.Where(d => d.Type == FoliageType.Stump).ToList();
        var canopies = f.Drawn.Count(d => Foliage.IsTree(d.Type));
        var trunks = f.Drawn.Count(d => d.Type == FoliageType.Trunk);
        want(stumps.Count == f.FelledCount && stumps.Any(d => Vector2.Distance(new Vector2(d.Position.X, d.Position.Z), new Vector2(tree.At.X, tree.At.Z)) < 0.01f), $"{stumps.Count} stumps drawn for {f.FelledCount} felled trees");
        want(canopies == trunks, $"a felled tree still draws: {canopies} canopies, {trunks} trunks");
        want(FoliageMeshes.Triangles(FoliageType.Stump, 0) > 0, "the stump mesh is empty");
        want(!f.NearestTree(tree.At, 0.5f, out _), "a saw would cut a stump");
        want(Foliage.DensityFor(FoliageType.Stump, FoliageQuality.Low) == 1 && Foliage.DensityFor(FoliageType.Trunk, FoliageQuality.Low) == 1, "low quality thins out drawn trunks or stumps");
        var json = MapFile.ToJson(Maps.Find("Foliage Test")!);
        var any = System.Text.RegularExpressions.Regex.Match(json, "\"type\":\\s*\"(grass|bush|shrub|tree|pine)\"");
        want(any.Success, "no foliage type in the test map's file");
        foreach (var name in new[] { "stump", "trunk" })
        {
            var refused = false;
            try
            {
                MapFile.Load(json[..any.Index] + "\"type\": \"" + name + "\"" + json[(any.Index + any.Length)..], "Foliage Test (stump)");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                refused = true;
            }
            want(refused, $"a map file placed a {name}");
        }
    }

    private static string RegrowTable(int seconds) =>
        "{\"format\":\"" + ResourceTable.Format + "\",\"kinds\":[{\"name\":\"forest\",\"material\":\"wood\",\"layer\":\"surface\",\"cell\":240,\"chance\":0,\"radius\":[55,105],\"aspect\":[0.6,1],\"units\":12,\"regrowSeconds\":" + seconds + "}]}";

    private static void GatherTable(Action<bool, string> want)
    {
        want(ResourceTable.Current.Kinds.Find(k => k.Surface)?.RegrowSeconds == 1800, "the shipped forest doesn't regrow in 1800 s");
        want(ResourceTable.Parse(RegrowTable(5)).Kinds[0].RegrowSeconds == 5, "regrowSeconds wasn't read");
        want(ResourceTable.Parse(RegrowTable(-1)).Problem.Length > 0, "a negative regrowSeconds was accepted");
        want(ResourceTable.Parse(RegrowTable(5).Replace(",\"regrowSeconds\":5", "")).Kinds[0].RegrowSeconds == ResourceTable.DefaultRegrowSeconds, "regrowSeconds has no default");
    }

    /// <summary>The 25 km map: a field made and asked without walking the world: long rays cost a few cell lookups each, in and out of forests.</summary>
    private static void GatherStreamed(ResourceTable table, Action<bool, string> want)
    {
        if (Maps.Find("Streamed Terrain") is not { } big)
        {
            want(false, "no Streamed Terrain map");
            return;
        }
        var sw = Stopwatch.StartNew();
        var f = new ResourceField(big, table, big.Sandbox?.Resources);
        var made = sw.Elapsed.TotalMilliseconds;
        var dir = Vector3.Normalize(new Vector3(1, 0, 0.37f));
        var hits = 0;
        const float span = 400; // (around one place: a far ray's ground is the streamed tile's to make, once)
        double ms = 0;
        long bytes = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            hits = 0;
            var before = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            for (var i = 0; i < 2000; i++)
            {
                var o = new Vector3(-span + i * span / 1000f, 1, (i * 37 % 2000 - 1000) * span / 1000f);
                if (f.RayTrunks(o, dir, 1000, out _, out _) < 1000)
                    hits++;
            }
            ms = sw.Elapsed.TotalMilliseconds;
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Line($"gathering: {big.Name} ({big.HalfX * 2:0} m): {f.DepositCount} deposits, field made in {made:0.0} ms, 2,000 km of rays in {ms:0.00} ms ({ms * 500:0} ns a km), {hits} hit a trunk, {bytes} B");
        want(bytes == 0, $"rays over the big world allocate ({bytes} B)");
        want(ms < 200, $"2,000 rays of 1 km took {ms:0} ms on the big world");
    }

    private static List<(uint Key, Trunk T)> Trunks(ResourceField f, Map map)
    {
        var all = new List<(uint, Trunk)>();
        var n = (int)(map.HalfX / ResourceField.TreeSpacing) + 1;
        for (var iz = -n; iz <= n; iz++)
            for (var ix = -n; ix <= n; ix++)
                if (f.TrunkAt(ix, iz, out var t))
                    all.Add((t.Key, t));
        return all;
    }

    /// <summary>
    /// Trunks are solid in the live world only and not as map data: rays (with their shape and normal), body, point, box and
    /// capsule tests all stop on a standing tree and pass a felled one, both player movers go round it, a buggy driven into it
    /// stops with a trunk between its wheels and off its centre line, a part thrown at 40 m/s doesn't pass it, and none of
    /// that allocates or touches the map's boxes, identity or nav.
    /// </summary>
    private static void GatherSolid(Map yard, Action<bool, string> want)
    {
        var sb = yard.Sandbox!;
        var keep = sb.Resources;
        var keepMode = World.PhysicsMode;
        sb.Resources = new SandboxResources { Generate = false, Deposits = [new SandboxDeposit("forest", 14, 4, 6, 1, 0)] };
        yard.DropResources();
        yard.RestoreProps();
        World.SetMap(yard);
        try
        {
            var f = yard.Resources!;
            var all = Trunks(f, yard).OrderBy(t => t.T.X).ToList();
            if (all.Count == 0)
            {
                want(false, "no tree in the test forest");
                return;
            }
            var t = all[0].T;
            f.Span(t, out var bottom, out var top);
            Line($"gathering: {all.Count} test trees, first at {t.X:0.00},{t.Z:0.00}, radius {t.Radius:0.00}, from {bottom:0.00} to {top:0.00} m");
            want(t.Radius >= 0.3f && top - bottom >= 4, $"a trunk of radius {t.Radius}, {top - bottom} m tall");

            var hash = yard.ContentHash;
            var solids = yard.Solid.Count;
            want(World.Physics.Forest == f, "the live collision world has no trees");
            want(PhysicsWorld.AsBuilt(yard).Forest == null && new PhysicsWorld(yard).Forest == null, "a nav or foliage copy of the world has trees");

            var y = bottom + 1.3f;
            var from = new Vector3(t.X - 3, y, t.Z);
            var q = World.Query;
            var hitOk = q.Raycast(from, Vector3.UnitX, 10, QueryFilter.Shots, out var hit);
            want(hitOk && hit.Kind == ShapeKind.Trunk && hit.Shape.Index == (int)t.Key, $"a shot hit {hit.Kind}, not the trunk");
            want(MathF.Abs(hit.Distance - (3 - t.Radius)) < 0.01f && Vector3.Dot(hit.Normal, -Vector3.UnitX) > 0.99f, $"a shot stopped at {hit.Distance} (normal {hit.Normal}), want {3 - t.Radius}");
            want(q.Raycast(from, Vector3.UnitX, 10, QueryFilter.Navigation) >= 10 || World.RaycastNav(from, Vector3.UnitX, 3.5f) >= 3.5f, "bot navigation sees a trunk");
            want(q.Raycast(new Vector3(t.X, top + 2, t.Z), -Vector3.UnitY, 3, QueryFilter.All, out var down) && down.Kind == ShapeKind.Trunk && MathF.Abs(down.Distance - 2) < 0.01f, "a ray down doesn't meet the trunk's top");
            want(q.Raycast(new Vector3(t.X, top + 0.5f, t.Z - 3), Vector3.UnitZ, 6, QueryFilter.All, out var above) == false || above.Kind != ShapeKind.Trunk, "the canopy is solid");
            want(q.Raycast(new Vector3(t.X, y, t.Z), Vector3.UnitX, 5, QueryFilter.All, out var inside) == false || inside.Kind != ShapeKind.Trunk, "a ray from inside a trunk hit it");
            var feet = new Vector3(t.X, bottom + 0.4f, t.Z);
            var capsule = new Capsule(feet + new Vector3(0, 0.5f, 0), feet + new Vector3(0, 1.4f, 0), 0.3f);
            var obb = new Obb(new Vector3(t.X, y, t.Z), new Vector3(0.2f), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
            var blocked = false;
            var clearNear = false;
            long bytes = 0;
            for (var pass = 0; pass < 2; pass++) // (the second counts: the first fills the shape lists)
            {
                var bytes0 = GC.GetAllocatedBytesForCurrentThread();
                blocked = World.Blocked(feet) && q.PointInside(new Vector3(t.X, y, t.Z), 0.05f, QueryFilter.All) && q.OverlapBox(obb, QueryFilter.All) && q.OverlapCapsule(capsule, QueryFilter.All);
                clearNear = !World.Blocked(feet + new Vector3(-t.Radius - 0.6f, 0, 0));
                for (var i = 0; i < 1000; i++)
                    q.Raycast(from + new Vector3(-20, 0, i * 0.003f), Vector3.UnitX, 40, QueryFilter.All);
                bytes = GC.GetAllocatedBytesForCurrentThread() - bytes0;
            }
            want(blocked, "a trunk doesn't block a body, point, box or capsule");
            want(clearNear, "a body beside a trunk is blocked");
            want(bytes == 0, $"tree queries allocate ({bytes} B)");
            foreach (var mode in new[] { PhysicsMode.Classic, PhysicsMode.Swept })
                GatherWalk(mode, t, bottom, true, want);
            World.PhysicsMode = keepMode;

            // Off the wheels' track and down the middle (the hull's corners straddle a lone trunk), against the same run with no tree.
            foreach (var off in new[] { 0f, 0.45f, -0.9f })
                GatherDrive(f, all, t, off, want);
            GatherThrown(f, t, bottom, want);
            GatherHinge(f, t, want);
            GatherForestCost(f, t, want);

            var shot = World.RaycastWorld(from, Vector3.UnitX, 10);
            foreach (var k in all)
                f.Fell(k.Key);
            want(World.RaycastWorld(from, Vector3.UnitX, 3.5f) >= 3.5f && shot < 3, "a felled tree still stops a shot");
            want(!World.Blocked(feet) && !q.OverlapBox(obb, QueryFilter.All) && !q.OverlapCapsule(capsule, QueryFilter.All) && !q.PointInside(new Vector3(t.X, y, t.Z), 0.05f, QueryFilter.All), "a felled tree still blocks");
            GatherWalk(PhysicsMode.Classic, t, bottom, false, want);
            GatherWalk(PhysicsMode.Swept, t, bottom, false, want);
            GatherDriveThrough(f, t, want);
            want(yard.ContentHash == hash && yard.Solid.Count == solids, "trees changed the map's identity or boxes");
        }
        finally
        {
            World.PhysicsMode = keepMode;
            sb.Resources = keep;
            yard.DropResources();
            yard.RestoreProps();
            World.SetMap(Maps.Find(GatherMap)!);
        }
    }

    /// <summary>A player walks straight at a trunk with a mover: blocked (never inside it), or through where a tree was.</summary>
    private static void GatherWalk(PhysicsMode mode, in Trunk t, float bottom, bool standing, Action<bool, string> want)
    {
        World.PhysicsMode = mode;
        var yaw = 0f;
        var forward = World.FlatForward(yaw);
        var side = World.FlatRight(yaw) * 0.1f; // (a little off centre: a mover slides round a tree, it doesn't hang on it)
        var s = new MoveState { Position = new Vector3(t.X, bottom + 0.3f, t.Z) - forward * 4 + side, Grounded = true };
        var closest = float.MaxValue;
        for (var i = 0; i < 120; i++)
        {
            World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward, Yaw = yaw, SpeedPct = 100 });
            closest = MathF.Min(closest, new Vector2(s.Position.X - t.X, s.Position.Z - t.Z).Length());
        }
        if (standing)
            want(closest >= t.Radius + (mode == PhysicsMode.Classic ? World.PlayerRadius - 0.01f : 0.1f), $"{mode}: a player got {closest:0.00} m from a trunk of radius {t.Radius:0.00}");
        else
            want(closest < 0.2f, $"{mode}: a player kept {closest:0.00} m from where a tree was");
    }

    private static ConstructWorld TreeWorld(ResourceField f) => new() { Resources = f };

    /// <summary>The buggy's run at a tree from standstill, off centre by <paramref name="off"/>: it stays out of the trunk, tick by tick.</summary>
    private static void GatherDrive(ResourceField f, List<(uint Key, Trunk T)> all, in Trunk t, float off, Action<bool, string> want)
    {
        var w = TreeWorld(f);
        if (ConstructBuild.FindBlueprint("Buggy") is not { } bp || DriveSpawn(w, bp, t.X - 26, t.Z + off, MathF.PI / 2, "the buggy") is not { } car)
            return;
        var q = World.Query;
        DriveSteps(w, q, 30);
        Seat(car);
        var deepest = 0f;
        var top = 0f;
        var ground = f.Ground(t.X, t.Z);
        var tree = t;
        var inner = t with { Radius = MathF.Max(t.Radius - 0.15f, 0.01f) };
        DriveSteps(w, q, 60 * 5, _ =>
        {
            DriveKeys(car, Buttons.Forward);
            top = MathF.Max(top, car.Vel.Length());
            foreach (var k in all)
                if (ConstructPhysics.TouchesTrunk(car, k.T with { Radius = MathF.Max(k.T.Radius - 0.15f, 0.01f) }, ground))
                    deepest++;
        });
        Line($"gathering: buggy {off:0.00} m off the tree: top speed {top:0.0} m/s, ends {car.WorldCom.X - tree.X:0.00} m past it, {car.WorldCom.Z - tree.Z:0.00} m aside, {deepest} ticks in a trunk");
        want(top > 6, $"the buggy only reached {top:0.0} m/s");
        want(deepest == 0, $"the buggy spent {deepest} ticks inside a trunk ({off} m off centre)");
        if (off == 0)
            want(car.WorldCom.X < tree.X, $"a buggy drove through a tree: {car.WorldCom.X - tree.X:0.00} m past it");
    }

    private static void GatherDriveThrough(ResourceField f, in Trunk t, Action<bool, string> want)
    {
        var w = TreeWorld(f);
        if (ConstructBuild.FindBlueprint("Buggy") is not { } bp || DriveSpawn(w, bp, t.X - 26, t.Z, MathF.PI / 2, "the buggy") is not { } car)
            return;
        var q = World.Query;
        DriveSteps(w, q, 30);
        Seat(car);
        var tx = t.X;
        DriveSteps(w, q, 60 * 5, _ => DriveKeys(car, Buttons.Forward));
        want(car.WorldCom.X > tx + 2, $"a buggy didn't pass where the tree was (ended {car.WorldCom.X - tx:0.00} m past it)");
    }

    /// <summary>A small part thrown hard at a trunk doesn't pass it between ticks.</summary>
    private static void GatherThrown(ResourceField f, in Trunk t, float bottom, Action<bool, string> want)
    {
        var w = TreeWorld(f);
        if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Frame Block"), new Vector3(t.X - 8, bottom + 2.2f, t.Z), 0) is not { } block)
        {
            want(false, "no block to throw");
            return;
        }
        block.Vel = new Vector3(40, 1, 0);
        block.Wake();
        var q = World.Query;
        for (var i = 0; i < 30; i++)
            w.Step(q, World.TickDt);
        want(block.WorldCom.X < t.X, $"a block at 40 m/s passed a trunk ({block.WorldCom.X - t.X:0.00} m past it)");
    }

    /// <summary>What trees add to a tick: ten buggies driving among trees, with and without the field, a fresh world each.</summary>
    private static void GatherForestCost(ResourceField f, in Trunk t, Action<bool, string> want)
    {
        var q = World.Query;
        var bp = ConstructBuild.FindBlueprint("Buggy")!;
        var (tx, tz) = (t.X, t.Z);
        (double Ms, long Bytes) Run(ResourceField? field)
        {
            var w = new ConstructWorld { Resources = field };
            var cars = new List<Construct>();
            for (var i = 0; i < 10; i++)
                if (DriveSpawn(w, bp, tx - 14 - (i % 2) * 7, tz - 20 + i * 4, MathF.PI / 2, "a buggy") is { } car)
                    cars.Add(car);
            DriveSteps(w, q, 30);
            foreach (var c in cars)
                Seat(c);
            void Keys(int _)
            {
                foreach (var c in cars)
                    DriveKeys(c, Buttons.Forward);
            }
            DriveSteps(w, q, 30, Keys);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            DriveSteps(w, q, 120, Keys);
            return (sw.Elapsed.TotalMilliseconds / 120, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Run(f); // (warm)
        var with = Run(f);
        var without = Run(null);
        Line($"gathering: ten buggies in a forest: {with.Ms:0.000} ms a tick with trunk contacts, {without.Ms:0.000} without; {with.Bytes} and {without.Bytes} B in 120 ticks");
        want(with.Bytes <= without.Bytes, $"trunk contacts allocate ({with.Bytes} B against {without.Bytes})");
    }

    /// <summary>An anchored Motor Gate whose door swings into a trunk (its parts beyond a joint, with no body motion) stalls at it.</summary>
    private static void GatherHinge(ResourceField f, in Trunk t, Action<bool, string> want)
    {
        var q = World.Query;
        if (ConstructBuild.FindBlueprint("Motor Gate") is not { } bp)
        {
            want(false, "no Motor Gate");
            return;
        }
        var tx = t.X;
        var tz = t.Z;
        var r = t.Radius;
        var stopped = false;
        foreach (var side in new[] { 1f, -1f })
        {
            // Where the door's middle would be at 50 degrees, from a gate built at the origin of the world beside the forest.
            var w = TreeWorld(f);
            var spot = new Vector3(-24, 0, -24);
            if (ConstructBuild.BuildOnGround(w, bp, spot, 0, out _) is not { } probe)
            {
                want(false, "the Motor Gate doesn't build");
                return;
            }
            ref var jt = ref probe.Joints[0];
            probe.WorldFrame(jt.ParentSeg, out var pq, out var pt);
            var axis = Vector3.Transform(jt.Axis, pq);
            var pivot = pt + Vector3.Transform(jt.Pivot, pq);
            var door = PartNamed(probe, "Door");
            var out0 = ConstructRender.PartCentre(probe, door) - pivot;
            out0 = Vector3.Normalize(out0 - axis * Vector3.Dot(out0, axis));
            var meet = pivot + Vector3.Transform(out0, Quaternion.CreateFromAxisAngle(axis, side * 50 * MathF.PI / 180)) * (r + 0.7f);
            w.Remove(probe);
            var gate = ConstructBuild.BuildOnGround(w, bp, new Vector3(tx, 0, tz) + spot - meet with { Y = 0 }, 0, out _);
            if (gate == null)
            {
                want(false, "the Motor Gate doesn't build by the tree");
                return;
            }
            var e = new ConstructEdits();
            var button = PartNamed(gate, "Button");
            for (var i = 0; i < 5; i++)
                w.Step(q, World.TickDt);
            e.Use(w, 1, ConstructRender.PartCentre(gate, button) + new Vector3(0, 0.3f, -1.4f), gate.Id, button); // (its toggle: open)
            var worst = 0f;
            var d = PartNamed(gate, "Door");
            var inner = r - 0.1f;
            for (var i = 0; i < 150; i++)
            {
                w.Step(q, World.TickDt);
                worst = MathF.Max(worst, BoxPen(gate, d, new Vector3(tx - inner, 0, tz - inner), new Vector3(tx + inner, 3, tz + inner)));
            }
            var angle = gate.Joints[0].Angle * 180 / MathF.PI;
            Line($"gathering: motor gate, side {side}: door at {angle:0.0} deg, {worst * 100:0.0} cm into the trunk");
            want(worst < 0.05f, $"a door swung {worst * 100:0} cm into a trunk");
            stopped |= MathF.Abs(angle) < 55;
        }
        want(stopped, "a door swung past a trunk on both sides");
    }

    /// <summary>
    /// Regrowth, by the table: not before its time, then solid and drawn again; waits while a construct or a player stands
    /// on the stump; "0" never; a new match leaves nothing due.
    /// </summary>
    private static void GatherRegrow(Map yard, Action<bool, string> want)
    {
        var sb = yard.Sandbox!;
        var keep = sb.Resources;
        ResourceTable.Use(ResourceTable.Parse(RegrowTable(2)));
        sb.Resources = new SandboxResources { Generate = false, Deposits = [new SandboxDeposit("forest", 14, 4, 6, 1, 0)] };
        yard.DropResources();
        yard.RestoreProps();
        World.SetMap(yard);
        try
        {
            var f = yard.Resources!;
            var w = TreeWorld(f);
            var q = World.Query;
            var all = Trunks(f, yard);
            if (all.Count < 2)
            {
                want(false, "too few trees for regrowth");
                return;
            }
            var t = all[0].T;
            var key = t.Key;
            void Run(float seconds)
            {
                for (var i = 0; i < seconds * World.TickRate; i++)
                    w.Step(q, World.TickDt);
            }
            var at = new Vector3(t.X, f.Ground(t.X, t.Z), t.Z);
            void Cut()
            {
                f.Cut(key, 12, out var felled);
                want(felled, "12 units didn't fell the test tree");
            }
            Cut();
            Run(1.8f);
            want(f.IsFelled(key) && w.GrownTrees.Count == 0, "a tree grew back early");
            Run(0.4f);
            want(!f.IsFelled(key) && w.GrownTrees.Contains(key), "a tree didn't grow back on time");
            want(World.Blocked(at), "a regrown tree isn't solid");
            f.DrawAround(new Vector3(float.MaxValue / 2));
            f.DrawAround(at);
            want(f.Drawn.All(d => d.Type != FoliageType.Stump), "a regrown tree's stump stayed");
            w.GrownTrees.Clear();

            // Something on the stump.
            Cut();
            var block = ConstructBuild.PlaceNew(w, ConstructParts.Need("Frame Block"), at + new Vector3(0, 0.3f, 0), 0)!;
            block.Anchored = true;
            Run(3);
            want(f.IsFelled(key), "a tree grew through a construct");
            w.Remove(block);
            w.PlayerSpots[0] = at;
            w.PlayerSpotCount = 1;
            Run(11);
            want(f.IsFelled(key), "a tree grew through a player");
            w.PlayerSpotCount = 0;
            Run(11);
            want(!f.IsFelled(key) && w.GrownTrees.Contains(key), "a tree didn't grow back once its stump was clear");

            // A new match: all back, nothing left due.
            Cut();
            yard.RestoreProps();
            want(f.FelledCount == 0, "a new match kept a felled tree");
            w.GrownTrees.Clear();
            Run(3);
            want(w.GrownTrees.Count == 0, "a tree of the last match grew back in this one");

            // Never.
            ResourceTable.Use(ResourceTable.Parse(RegrowTable(0)));
            yard.DropResources();
            var never = yard.Resources!;
            var w0 = TreeWorld(never);
            never.Cut(key, 12, out _);
            for (var i = 0; i < 300; i++)
                ConstructGatherer.Step(w0);
            want(never.IsFelled(key), "regrowSeconds 0 grew back");
        }
        finally
        {
            ResourceTable.Use(null);
            sb.Resources = keep;
            yard.DropResources();
            yard.RestoreProps();
            World.SetMap(Maps.Find(GatherMap)!);
        }
    }

    /// <summary>"Gathering network": real host, client and two late joiners: all hear what fell and what grew back, once each, and a joiner hears only what is felled now.</summary>
    private static void CheckGatheringNet()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail("gathering network: the yard is missing");
            return;
        }
        const ushort port = 26463;
        const float regrow = 3;
        var keep = sb.Resources;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        ResourceTable.Use(ResourceTable.Parse(RegrowTable((int)regrow)));
        sb.Resources = new SandboxResources { Deposits = [new SandboxDeposit("forest", 0, 0, 24, 1, 0), new SandboxDeposit("iron vein", 0, 0, 16, 1, 0)] };
        yard.DropResources();
        yard.RestoreProps();
        World.SetMap(yard);
        try
        {
            using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
            server.Mode = GameMode.Sandbox;
            using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Pump(server, [a], 1.0, ref now, clock);
            for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
                PumpFast(server, [a], 0.25, ref now);
            var f = yard.Resources!;
            // A tree with nothing beside it (the yard's constructs stand in this forest, and a tree waits for what stands on its stump).
            var free = Trunks(f, yard).Where(t => server.Constructs.Bodies.All(c => new Vector2(c.WorldCom.X - t.T.X, c.WorldCom.Z - t.T.Z).Length() > c.Radius + 2)).ToList();
            if (free.Count == 0)
            {
                Fail("gathering network: no clear tree in the yard's forest");
                return;
            }
            if (free.Count < 3)
            {
                Fail($"gathering network: only {free.Count} clear trees in the yard's forest");
                return;
            }
            var limit = ResourceTable.Current.Kinds.Find(k => k.Surface)!.Units / ConstructParts.Need("Saw Module").GatherRate + 3;
            List<Game> clients = [a];
            void Saw(in Trunk trunk)
            {
                var key = trunk.Key;
                var saw = GatherBody(server.Constructs, "Saw Module", new Vector3(trunk.X, f.Ground(trunk.X, trunk.Z), trunk.Z) + new Vector3(0, 1.5f, 0));
                for (var t = 0f; t < limit && !f.IsFelled(key); t += 0.1f)
                    Pump(server, clients, 0.1, ref now, clock);
                server.Constructs.Remove(saw); // (or it fells the regrown tree again)
                Pump(server, clients, 0.3, ref now, clock);
                if (!f.IsFelled(key))
                    Fail("gathering network: a tree didn't fall on the host");
            }
            void Regrown(uint key)
            {
                for (var t = 0f; t < regrow + 2 && f.IsFelled(key); t += 0.25f)
                    Pump(server, clients, 0.25, ref now, clock);
                Pump(server, clients, 0.3, ref now, clock);
                if (f.IsFelled(key))
                    Fail("gathering network: a tree didn't grow back on the host");
            }

            // Everyone hears a fall and a regrowth; someone joining after it hears of neither.
            Saw(free[0].T);
            Line($"gathering network: felled {f.FelledCount}, client read {a.FellRead}");
            if (a.FellRead != 1)
                Fail($"gathering network: the client read {a.FellRead} felled trees, want 1");
            Regrown(free[0].Key);
            Line($"gathering network: regrown, client read {a.GrewRead}");
            if (a.GrewRead != 1)
                Fail($"gathering network: the client read {a.GrewRead} regrown trees, want 1");
            using var late = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Late", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            clients.Add(late);
            Pump(server, clients, 1.0, ref now, clock);
            if (late.FellRead != 0)
                Fail($"gathering network: a joiner after the regrowth read {late.FellRead} felled trees, want 0");

            // The late joiner hears the next ones as they happen.
            Saw(free[1].T);
            Regrown(free[1].Key);
            Line($"gathering network: second tree: client {a.FellRead} fell, {a.GrewRead} grew; joiner {late.FellRead} fell, {late.GrewRead} grew");
            if (a.FellRead != 2 || a.GrewRead != 2 || late.FellRead != 1 || late.GrewRead != 1)
                Fail("gathering network: a second tree didn't reach both clients once each");

            // A joiner while a tree is down hears it. (Last: in this one process a joining client restores the shared map,
            // which forgets the host's timers; each real peer has its own field.)
            Saw(free[2].T);
            using var later = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Later", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            clients.Add(later);
            Pump(server, clients, 1.0, ref now, clock);
            if (later.FellRead != 1)
                Fail($"gathering network: a joiner read {later.FellRead} felled trees, want 1");
            GatherShaftNet(server, a, late, f, port, ref now, clock); // (SelfTestGatheringDrill.cs)
        }
        finally
        {
            ResourceTable.Use(null);
            sb.Resources = keep;
            yard.DropResources();
            yard.RestoreProps();
        }
    }
}
