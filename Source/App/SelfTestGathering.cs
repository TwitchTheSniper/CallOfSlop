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
    /// step allocates nothing while drilling.
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
        map.RestoreProps();
        Want(f.Felled.Count == 0, "a new match kept the felled trees");
        foreach (var p in problems)
            Fail($"gathering: {p}");
    }

    /// <summary>"Gathering network": real host, client and a late joiner: both clients hear what fell, one tree each.</summary>
    private static void CheckGatheringNet()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail("gathering network: the yard is missing");
            return;
        }
        const ushort port = 26463;
        var keep = sb.Resources;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        sb.Resources = new SandboxResources { Deposits = [new SandboxDeposit("forest", 0, 0, 24, 1, 0)] };
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
            if (FirstTree(f, yard.HalfX) is not { } tree)
            {
                Fail("gathering network: no tree in the yard's forest");
                return;
            }
            var saw = GatherBody(server.Constructs, "Saw Module", tree.At + new Vector3(0, 1.5f, 0));
            var limit = ResourceTable.Current.Kinds.Find(k => k.Surface)!.Units / ConstructParts.Need("Saw Module").GatherRate + 3;
            for (var t = 0f; t < limit && !f.Felled.Contains(tree.Key); t += 0.25f)
                Pump(server, [a], 0.25, ref now, clock);
            Pump(server, [a], 0.4, ref now, clock);
            Line($"gathering network: felled {f.Felled.Count}, client read {a.FellRead}");
            if (!f.Felled.Contains(tree.Key))
                Fail("gathering network: the tree didn't fall on the host");
            if (a.FellRead != 1)
                Fail($"gathering network: the client read {a.FellRead} felled trees, want 1");
            using var late = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Late", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Pump(server, [a, late], 1.5, ref now, clock);
            if (late.FellRead != 1)
                Fail($"gathering network: the joiner read {late.FellRead} felled trees, want 1");
        }
        finally
        {
            sb.Resources = keep;
            yard.DropResources();
            yard.RestoreProps();
        }
    }
}
