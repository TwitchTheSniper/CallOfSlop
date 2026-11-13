using System.Diagnostics;
using System.Numerics;
using Brutal.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    private const string BeaconPart = "Inventory Beacon";

    /// <summary>The table and the part: the shipped range, every kind of bad file refused with a reason, a beacon that holds something refused.</summary>
    private static void BeaconTableChecks(Action<bool, string> want)
    {
        var t = BeaconTable.Current;
        want(t.Problem.Length == 0 && t.Has(BeaconTable.Inventory) && t.Range(BeaconTable.Inventory) == 60, $"the shipped table (\"{t.Problem}\", {t.Range(BeaconTable.Inventory)} m)");
        const string head = "{\"format\": \"call-of-slop-beacons/1\", \"beacons\": ";
        want(BeaconTable.Parse(head + "[{\"id\": \"big\", \"rangeMetres\": 200}]}") is { Problem.Length: 0 } own && own.Range("big") == 200, "a good file");
        (string Json, string Want)[] bad =
        [
            ("{", "depth"), ("{\"format\": \"nope\"}", "format"), (head + "[]}", "lists"), (head + "[{\"id\": \"a\"}]}", "rangeMetres"),
            (head + "[{\"id\": \"a\", \"rangeMetres\": 0}]}", "rangeMetres"), (head + "[{\"id\": \"A b\", \"rangeMetres\": 5}]}", "lower-case"),
            (head + "[{\"id\": \"a\", \"rangeMetres\": 5, \"x\": 1}]}", "unknown key"), (head + "[{\"id\": \"a\", \"rangeMetres\": 5}, {\"id\": \"a\", \"rangeMetres\": 6}]}", "twice"),
        ];
        foreach (var (json, why) in bad)
        {
            var b = BeaconTable.Parse(json);
            want(b.Problem.Contains(why, StringComparison.OrdinalIgnoreCase) && b.Has(BeaconTable.Inventory), $"a bad table should say \"{why}\" (\"{b.Problem}\")");
        }
        var d = ConstructParts.Need(BeaconPart);
        want(d.IsBeacon && d.Role == PartRole.Storage && d.Slots.Length == 0 && !d.IsBin && d.PowerDraw > 0 && d.Tool == ConstructTool.Welder, $"the part ({d.IsBeacon}, {d.Slots.Length} slots, {d.PowerDraw} W, {d.Tool})");
        want(d.MaxInputs == 1 && d.DataIn is [ConstructGatherer.RunPort], $"its Run input ({d.MaxInputs})");
        want(ZFight.Report(d.Name, ZFight.Boxes(d)).Count() == 0, $"it has coplanar faces: {string.Join("; ", ZFight.Report(d.Name, ZFight.Boxes(d)))}");
        var cat = SandboxCatalogue.Make();
        want(cat.Parts.Sections.Any(s => s.Items.Contains(d.Id)), "it isn't in the parts menu");
        want(ConstructBuild.FindBlueprint("Beacon Depot") != null, "the Beacon Depot blueprint is missing");
    }

    /// <summary>A world of the yard's ground with survival's rules, and a charged, owned Beacon Depot.</summary>
    private static Construct? BeaconDepot(ConstructWorld w, ConstructEdits hands, float x, float z, byte owner, Action<bool, string> want)
    {
        if (BuildBlueprint(w, "Beacon Depot", x, z) is not { } c)
        {
            want(false, $"no Beacon Depot at {x}, {z}");
            return null;
        }
        c.Owner = owner;
        c.Charge = c.ChargeCapacity;
        _ = hands;
        return c;
    }

    /// <summary>
    /// "Inventory beacon" (docs/sandbox/CONSTRUCTS.md "The Inventory Beacon"): the table and the part; the Beacon Depot on the yard in
    /// survival on the real step: every store found through chutes, a junction and a world link, counted once with two
    /// beacons, two networks merged; range, someone else's beacon, power, Run and a wreck, each with its reason; a take by
    /// the real action, the held part going back, a physics-only piece in front of the player, the contested last one;
    /// placing refills from the network; a part taken off, and materials, go to the network first, a full one falling
    /// back to the hands; a cut chute; no work per tick and no allocation; the message's bound.
    /// </summary>
    private static void CheckBeacon()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail($"inventory beacon: the {ConstructYardMap} map is missing");
            return;
        }
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        BeaconTableChecks(Want);
        var (keepMenu, keepInfinite) = (sb.PartsMenu, sb.InfiniteParts);
        yard.RestoreProps();
        World.SetMap(yard);
        sb.PartsMenu = false;
        sb.InfiniteParts = false;
        try
        {
            BeaconOffline(sb, Want);
        }
        finally
        {
            sb.PartsMenu = keepMenu;
            sb.InfiniteParts = keepInfinite;
            yard.RestoreProps();
        }
        foreach (var p in problems)
            Fail($"inventory beacon: {p}");
        BeaconWire();
    }

    private static void BeaconOffline(SandboxMap sb, Action<bool, string> Want)
    {
        var w = new ConstructWorld { CreativeRecycling = false };
        var q = World.Query;
        var hands = new ConstructEdits { Inventory = new SandboxInventories(), InfiniteParts = false, Survival = true, Query = q };
        hands.Inventory.SetInfinite(false);
        void Run(float seconds)
        {
            for (var t = 0; t < seconds * World.TickRate; t++)
                w.Step(q, World.TickDt);
        }
        if (OpenSpot(w) is not { } spot || BeaconDepot(w, hands, spot.X, spot.Z, 0, Want) is not { } c)
            return;
        int bi = PartNamed(c, BeaconPart), chest = PartNamed(c, "Chest"), shelf = PartNamed(c, "Parts Shelf"), bin = PartNamed(c, "Materials Bin");
        var beacon = w.StoreOf(c, bi)!;
        var def = c.DefOf(bi);
        var names = RecyclingTable.Current;
        byte metal = names.IdOf("metal"), wood = names.IdOf("wood");
        Run(0.3f);
        c.WorldFrame(c.Parts[bi].Seg, out var fq, out var ft);
        var at = ft + Vector3.Transform(c.Parts[bi].Pos, fq);
        var eye = at + new Vector3(0, 1.6f, 4f); // (near the beacon: in reach of the ground in front of it)
        var view = new BeaconView();
        void Look(float seconds = 0.2f)
        {
            Run(seconds);
            w.Beacons.Update(w, 0, eye, view);
        }
        var (lamp, button) = (ConstructParts.Need("Lamp"), ConstructParts.Need("Button"));

        // ---- What it reaches.
        Look();
        Want(view.State == BeaconState.On && view.Stores == 3 && view.Parts == 0 && view.Range == 60, $"the depot: {view.State}, {view.Stores} stores, {view.Parts} parts, {view.Range} m");
        Want(beacon.Busy && !beacon.Held && c.PowerUse >= def.PowerDraw, $"it draws its power while on (busy {beacon.Busy}, {c.PowerUse} W)");
        Want(w.SetIn(c, shelf, 2, lamp) == null && w.SetIn(c, shelf, 2, lamp) == null && w.SetIn(c, chest, 0, button) == null, "stocking the depot");
        w.Deposit(c, bin, 300, metal);
        Look();
        Want(view.Counts[lamp.Id] == 2 && view.Counts[button.Id] == 1 && view.Parts == 3 && view.Materials[metal] == 300 && view.Bulk == 300, $"the counts: {view.Counts[lamp.Id]} lamps, {view.Counts[button.Id]} buttons, {view.Parts} parts, {view.Materials[metal]} metal");
        Want(view.Line == "Beacon: 3 stores, 3 parts, 300 material, range 60 m", $"the HUD line \"{view.Line}\"");
        Want(ConstructBeacons.Look(def, beacon, false) == "Beacon: 3 stores, 3 parts, range 60 m", $"the look line \"{ConstructBeacons.Look(def, beacon, false)}\"");

        // A junction: a branch off a chute to a fourth store; a world link to a fifth, on another construct; a second beacon counts nothing twice.
        var chest2 = ModFit(w, c, "Chest", "bottom", "inventory beacon");
        var mid = ConstructWorld.Quantise((ConstructRoutes.Point(c, 0, -1, out _) + ConstructRoutes.Point(c, 0, 0, out _)) * 0.5f);
        var branch = ConstructRoutes.Add(w, c, RouteType.Chute, -1, 255, 0, 0, mid, default, chest2, 255);
        c.Rebuild();
        Want(chest2 >= 0 && branch >= 0 && c.Routes[branch].Parent == 0, $"a junction ({chest2}, link {branch})");
        Look();
        Want(view.Stores == 4, $"a chest on a junction should be the 4th store ({view.Stores})");
        var far = BuildBlueprint(w, "Storage Corner", spot.X + 8, spot.Z);
        Want(far != null, "a second construct");
        if (far == null)
            return;
        far.Owner = 0;
        Span<WNode> none = [];
        Want(w.AddWorldLink(q, RouteType.Chute, c, bin, 255, none, far, PartNamed(far, "Parts Shelf"), 255) >= 0, "a world link");
        Look();
        Want(view.Stores == 5, $"a shelf over a world link should be the 5th store ({view.Stores})");
        var graphs = w.Beacons.GraphRuns;
        var second = ModFit(w, c, BeaconPart, "bottom", "inventory beacon");
        Look();
        Want(second >= 0 && w.Beacons.Count == 2 && view.Stores == 5 && view.Parts == 3 && w.Beacons.GraphRuns > graphs, $"a second beacon on the network counts nothing twice ({w.Beacons.Count} beacons, {view.Stores} stores, {view.Parts} parts)");
        var other = BeaconDepot(w, hands, spot.X - 9, spot.Z, 0, Want);
        if (other == null)
            return;
        Want(w.SetIn(other, PartNamed(other, "Parts Shelf"), 2, lamp) == null, "stocking a second network");
        Look();
        Want(view.Stores == 8 && view.Counts[lamp.Id] == 3, $"two networks in range merge ({view.Stores} stores, {view.Counts[lamp.Id]} lamps)");

        // ---- No work per tick once it is steady, and no allocation.
        var (g0, c0) = (w.Beacons.GraphRuns, w.Beacons.CountRuns);
        Run(1);
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 0; t < 240; t++)
        {
            w.Step(q, World.TickDt);
            if (t % ConstructBeacons.ViewEvery == 0)
                w.Beacons.Update(w, 0, eye, view);
        }
        var alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        Want(w.Beacons.GraphRuns == g0 && w.Beacons.CountRuns == c0, $"a steady network was worked out again ({w.Beacons.GraphRuns - g0} graphs, {w.Beacons.CountRuns - c0} counts)");
        Want(alloc == 0, $"a steady tick allocates {alloc} B over 240 ticks");
        w.Deposit(c, bin, 1, metal);
        Look();
        Want(w.Beacons.GraphRuns == g0 && w.Beacons.CountRuns > c0 && view.Materials[metal] == 301, $"a change to a store counts again, no more ({w.Beacons.GraphRuns - g0} graphs, {view.Materials[metal]} metal)");
        w.Withdraw(c, bin, 1);

        // ---- Range, and whose it is.
        ulong Net(Vector3 from) => hands.NetworkOf(w, 0, from);
        Want(Net(at + new Vector3(0, 0, 59)) != 0 && Net(at + new Vector3(75, 0, 0)) == 0, "the edge of the range");
        var outside = at + new Vector3(100, 0, 0);
        Want(Net(outside) == 0, "100 m off nothing should be in range");
        var player = hands.Hand(0);
        Want(hands.TakePart(sb, 0, lamp.Id, w, outside, 0) is { } oor && oor.Contains("out of range"), $"a take out of range says why (\"{hands.TakePart(sb, 0, lamp.Id, w, outside, 0)}\")");
        Want(hands.TakePart(sb, 0, lamp.Id, w, at + new Vector3(500, 0, 0), 0) == "the parts menu is off on this map", "far from any beacon the map's rule stands");
        var view2 = new BeaconView();
        w.Beacons.Update(w, 0, outside, view2);
        Want(view2.State == BeaconState.OutOfRange && view2.Line.Contains("out of range") && !hands.Hand(0).Held.Equals(lamp.Id), $"out of range: {view2.State} \"{view2.Line}\"");
        c.Owner = other.Owner = 1;
        Want(Net(eye) == 0 && hands.TakePart(sb, 0, lamp.Id, w, eye, 0) is { } theirs && theirs.Contains("someone else's"), $"someone else's beacon (\"{hands.TakePart(sb, 0, lamp.Id, w, eye, 0)}\")");
        c.Owner = 255;
        Want(Net(eye) != 0, "a beacon nobody owns serves anyone");
        c.Owner = other.Owner = 0;
        other.Charge = 0;
        Run(0.3f);

        // ---- Power and Run, with their reasons; nothing is drawn held off.
        var charge = c.Charge;
        c.Charge = 0;
        Look(0.3f);
        Want(view.State == BeaconState.NoPower && Net(eye) == 0 && view.Line.Contains("no power") && ConstructBeacons.Look(def, beacon, false).Contains("no power"), $"a flat battery: {view.State} \"{view.Line}\"");
        Want(view.Counts[lamp.Id] == 0, "a network off shows nothing");
        Want(hands.TakePart(sb, 0, lamp.Id, w, eye, 0) is { } dead && dead.Contains("no power"), "a take with no power says why");
        c.Charge = charge;
        Look(0.4f);
        Want(view.State == BeaconState.On, $"power back: {view.State}");
        var sw = ModFit(w, c, "Switch", "-y", "inventory beacon");
        Wire(w, c, sw, bi);
        Wire(w, c, sw, second);
        c.Parts[sw].State = 0;
        Look(0.5f);
        Want(view.State == BeaconState.HeldOff && beacon.Held && !beacon.Busy && view.Line.Contains("Run wire") && c.PowerUse < def.PowerDraw, $"Run off: {view.State}, held {beacon.Held}, {c.PowerUse} W");
        c.Parts[sw].State = 1;
        Look(0.5f);
        Want(view.State == BeaconState.On && !beacon.Held && c.PowerUse >= def.PowerDraw, $"Run on: {view.State}, {c.PowerUse} W");
        other.Charge = other.ChargeCapacity;
        Run(0.3f);
        hands.InfiniteParts = true;
        Want(Net(eye) == 0, "creative: nothing goes through the network");
        hands.InfiniteParts = false;

        // ---- Taking: the real action.
        var eyeOf = new Func<byte, Vector3?>(_ => eye);
        CStore shelfStore = w.StoreOf(c, shelf)!, chestStore = w.StoreOf(c, chest)!;
        Want(hands.TakePart(sb, 0, lamp.Id, w, eye, 0) == null && hands.Hand(0).Held == lamp.Id && shelfStore.Count[2] == 1, $"a take: held {hands.Hand(0).Held}, {shelfStore.Count[2]} left");
        Want(hands.TakePart(sb, 0, button.Id, w, eye, 0) == null && hands.Hand(0).Held == button.Id && chestStore.Def[0] == ConstructParts.None, "a second take");
        Want(shelfStore.Count[2] == 2 && hands.Notices.Exists(n => n.Text == "Lamp into the network (Parts Shelf)"), $"what the hands held goes back to the network, onto its own pile ({shelfStore.Count[2]}; \"{string.Join("; ", hands.Notices)}\")");
        hands.Notices.Clear();
        Want(hands.TakePart(sb, 0, ConstructParts.Need("Scale").Id, w, eye, 0) is { } none2 && none2.Contains("left on the network"), "a part the network doesn't hold says so");
        Want(hands.Hand(0).Held == button.Id, "a refused take leaves the hands alone");
        hands.Hand(0).Held = ConstructParts.None;
        // The last one between two players: the first has it; the other is told.
        c.Owner = other.Owner = 255;
        w.TakeOut(c, shelf, 2);
        w.TakeOut(other, PartNamed(other, "Parts Shelf"), 2);
        Want(hands.TakePart(sb, 0, lamp.Id, w, eye, 0) == null, "the last lamp, first asked");
        Want(hands.TakePart(sb, 1, lamp.Id, w, eye, 0) == $"no Lamp left on the network" && hands.Hand(1).Held == ConstructParts.None, "the last lamp, second asked");
        hands.Hand(0).Held = ConstructParts.None;
        // A piece that became physics-only while stored: a body in front, as creative gives it.
        c.Owner = other.Owner = 0;
        var smelter = ConstructParts.Need("Drill Module"); // (a building wouldn't fit in front of the depot)
        shelfStore.Def[0] = smelter.Id;
        shelfStore.Count[0] = 1;
        w.Beacons.Stock++;
        var ids = w.Bodies.Select(b => b.Id).ToHashSet();
        var body = hands.TakePart(sb, 0, smelter.Id, w, eye, 0);
        Want(body == null && w.Bodies.Any(b => !ids.Contains(b.Id) && b.PartCount == 1 && b.DefOf(0) == smelter) && shelfStore.Def[0] == ConstructParts.None && hands.Hand(0).Held == ConstructParts.None, $"a physics-only piece: \"{body}\", slot {shelfStore.Def[0]}");
        foreach (var b in w.Bodies.Where(b => !ids.Contains(b.Id)).ToList())
            w.Remove(b);

        // ---- Bulk: fills what is carried up to a load; a refusal when there is nothing to fill.
        var inv = hands.Inventory.Of(0);
        Want(hands.TakeNetworkMaterial(w, 0, eye, metal) == null && inv.Materials[metal] == 100 && w.StoreOf(c, bin)!.Amount == 200, $"a load of metal ({inv.Materials[metal]}, bin {w.StoreOf(c, bin)!.Amount})");
        Want(hands.TakeNetworkMaterial(w, 0, eye, metal) is { } full2 && full2.Contains("already carry"), "a full load refuses");
        inv.TakeMaterial(metal, 60);
        Want(hands.TakeNetworkMaterial(w, 0, eye, metal) == null && inv.Materials[metal] == 100 && w.StoreOf(c, bin)!.Amount == 140, "it tops up to a load");
        Want(hands.TakeNetworkMaterial(w, 0, eye, wood) is { } nowood && nowood.Contains("no wood"), "a material it hasn't says so");
        Want(hands.TakeNetworkMaterial(w, 0, outside, metal) is { } farb && farb.Contains("out of range"), "out of range says why");
        Want(hands.TakeNetworkMaterial(w, 0, eye, 255) == "no such material", "a bad material id is refused");

        // Materials coming to the player: a bin holding it first, then an empty one; what doesn't fit stays carried.
        inv.TakeMaterial(metal, 100);
        inv.AddMaterial(metal, 50);
        var into = hands.MaterialToNetwork(w, 0, eye, metal, 50, 0);
        Want(into == 50 && inv.Materials[metal] == 0 && w.StoreOf(c, bin)!.Amount == 190, $"metal goes to the bin holding it ({into}, carried {inv.Materials[metal]}, bin {w.StoreOf(c, bin)!.Amount})");
        inv.AddMaterial(wood, 30);
        VolumeOnNetwork(Want);
        Want(hands.MaterialToNetwork(w, 0, eye, wood, 30, 5) == 30 && w.StoreOf(other, PartNamed(other, "Materials Bin"))!.Material == wood && inv.Materials[wood] == 0, "wood goes to an empty bin");
        hands.Notices.Clear();

        // ---- Building from it: placing uses the next, so a row needs no trips.
        var ground = ConstructWorld.Ground(q, at.X + 1, at.Z + 5);
        var chestSlot = w.SetIn(c, chest, 0, lamp);
        Want(chestSlot == null && w.SetIn(c, shelf, 2, lamp) == null, "restocking lamps");
        hands.SetTool(0, ConstructTool.Welder);
        Want(hands.TakePart(sb, 0, lamp.Id, w, eye, 0) == null, "a lamp for the row");
        var before = w.Bodies.Count;
        var now = 100.0;
        var placed = hands.BeginPlaceNew(w, 0, eye, ground, 0, now);
        now += 10;
        hands.Tick(w, now, eyeOf);
        Want(placed == null && w.Bodies.Count == before + 1 && hands.Hand(0).Held == lamp.Id, $"a lamp set down comes back from the network (\"{placed}\", {w.Bodies.Count - before} bodies, held {hands.Hand(0).Held}; {string.Join("; ", hands.Done)})");
        hands.Done.Clear();
        var lamps = w.Bodies.Last();
        var left = Net(eye) != 0 ? shelfStore.Count[2] + chestStore.Count[0] + chestStore.Count[1] : -1;
        Want(left == 0, $"the row used both from the network ({left} left)");

        // ---- Coming back: a part taken off goes to the network first; a full network falls back to the hands.
        hands.Hand(0).Held = ConstructParts.None;
        var taken = hands.BeginRemove(w, 0, eye, lamps.Id, 0, now);
        now += 10;
        hands.Tick(w, now, eyeOf);
        Want(taken == null && hands.Hand(0).Held == ConstructParts.None && hands.Notices.Exists(n => n.Text.Contains("into the network")) && CountOn(w, c, lamp.Id) + CountOn(w, other, lamp.Id) + CountOn(w, far, lamp.Id) == 1, $"a part taken off goes to the network (\"{taken}\", held {hands.Hand(0).Held}, {string.Join("; ", hands.Notices)})");
        hands.Notices.Clear();
        // Every slot taken: a lamp on every free slot on the network.
        foreach (var cc in (Construct[])[c, other, far])
            for (var i = 0; i < cc.PartCount; i++)
                if (w.StoreOf(cc, i) is { } s && !cc.DefOf(i).IsBeacon)
                    for (var k = 0; k < s.SlotCount; k++)
                        while (ConstructStorage.Check(cc.DefOf(i), s, k, lamp, out _) == SlotCheck.Ok)
                            w.SetIn(cc, i, k, lamp);
        var heldLamp = ConstructBuild.PlaceNew(w, lamp, ground + new Vector3(2, 0.5f, 0), 0);
        heldLamp!.Owner = 0;
        Run(0.5f);
        var fullBack = hands.BeginRemove(w, 0, eye, heldLamp.Id, 0, now);
        now += 10;
        hands.Tick(w, now, eyeOf);
        Want(fullBack == null && hands.Hand(0).Held == lamp.Id && hands.Notices.Exists(n => n.Text.Contains("no room on the network")), $"a full network: into the hands (\"{fullBack}\", held {hands.Hand(0).Held}, {string.Join("; ", hands.Notices)})");
        hands.Notices.Clear();
        hands.Hand(0).Held = ConstructParts.None;
        // A physics-only part taken off stays a body (decision 21).
        var welded = ConstructBuild.PlaceNew(w, ConstructParts.Need("Frame Beam"), ground + new Vector3(-2, 0.5f, 0), 0);
        if (welded != null && ModFit(w, welded, "Frame Beam", ConstructParts.Need("Frame Beam").Sockets[0].Name, "inventory beacon") is var beam and >= 0)
        {
            welded.Owner = 0;
            hands.SetTool(0, ConstructTool.Welder);
            hands.Done.Clear();
            var cut = hands.BeginRemove(w, 0, eye, welded.Id, beam, now);
            now += 10;
            hands.Tick(w, now, eyeOf);
            Want(cut == null && !hands.Done.Exists(d => d.Contains("into the network")) && hands.Done.Exists(d => d.Contains("cut free")), $"a welded piece stays a body (\"{cut}\", {string.Join("; ", hands.Done)})");
            hands.Done.Clear();
        }

        // ---- A cut chute, then a wreck: the store goes, then the beacon says why.
        Look(0.3f);
        var stores = view.Stores;
        Want(ConstructRoutes.Damage(w, c, 2, c.Routes[2].Count, 1e6f), "cutting the chute to the shelf");
        Look(0.3f);
        Want(view.Stores == stores - 2 && view.State == BeaconState.On, $"a cut chute drops the bin, and the shelf on its world link ({view.Stores} of {stores})");
        foreach (var b in (int[])[second, bi])
            w.DamagePart(c, b, 1e6f, out _, raw: true);
        other.Owner = 1;
        Look(0.3f);
        Want(view.State == BeaconState.Wrecked && view.Line.Contains("wrecked") && Net(eye) == 0, $"a wrecked beacon: {view.State} \"{view.Line}\"");
    }

    /// <summary>A Storage Volume speaks to a beacon network by its own kind: parts by kind, then a bulk box.</summary>
    private static void VolumeOnNetwork(Action<bool, string> want)
    {
        var w = new ConstructWorld();
        var vd = ConstructParts.Need("Storage Volume");
        var lamp = ConstructParts.Need("Lamp");
        var c = ConstructBuild.PlaceNew(w, vd, new Vector3(0, 2, 0), 0)!;
        var kind = VolumeStore.Instance;
        want(ConstructBeacons.Kinds[0] == kind && kind.Serves(vd) && !kind.Serves(lamp), "a Storage Volume isn't its own store kind on a network");
        want(!kind.PutPart(w, c, 0, lamp, true), "a volume piled a part it doesn't hold");
        if (w.StoreOf(c, 0) is not { } st)
        {
            want(false, "the volume has no store");
            return;
        }
        st.AddKind(lamp.Id, 2);
        want(kind.Parts(w, c, 0) == 2 && kind.Holds(w, c, 0, lamp.Id) == 2, $"a volume's parts on a network: {kind.Parts(w, c, 0)}");
        want(kind.TakePart(w, c, 0, lamp) && kind.Holds(w, c, 0, lamp.Id) == 1, "a part wasn't taken from a volume");
        var metal = RecyclingTable.Current.IdOf("metal");
        want(kind.PutBulk(w, c, 0, metal, 10, false) == 0, "bulk went into a volume holding parts");
    }

    private static int CountOn(ConstructWorld w, Construct c, ushort id)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            n += RackStore.Instance.Serves(c.DefOf(i)) ? RackStore.Instance.Holds(w, c, i, id) : SlotStore.Instance.Serves(c.DefOf(i)) ? SlotStore.Instance.Holds(w, c, i, id) : 0; // (a Piece Rack is read as a RackStore)
        return n;
    }

    /// <summary>The message: whatever the network holds, each packet stays inside the MTU and they add up to the same view; bad bytes are refused; a store's beacon numbers read back.</summary>
    private static void BeaconWire()
    {
        var kinds = ConstructParts.All.Count;
        var v = new BeaconView { State = BeaconState.On, Stores = 34, Parts = 512, Range = 60, Bulk = 700, Mask = 1, Unsent = true };
        for (var i = 0; i < kinds; i++)
            v.Counts[i] = (ushort)(i % 7 + 1);
        v.Materials[1] = 700;
        var w = new PacketWriter(16384);
        var heard = new BeaconView();
        var packets = 0;
        var most = 0;
        while (ConstructBeacons.Pending(v) && packets < 100)
        {
            ConstructBeacons.Write(w, v);
            most = Math.Max(most, w.Length);
            packets++;
            var r = new PacketReader(w.Span[2..]);
            ConstructBeacons.Read(ref r, heard);
        }
        var same = heard.State == BeaconState.On && heard.Stores == 34 && heard.Parts == 512 && heard.Range == 60 && heard.Materials[1] == 700 && Enumerable.Range(0, kinds).All(i => heard.Counts[i] == v.Counts[i]);
        Line($"inventory beacon: {kinds} kinds in {packets} packets, the largest {most} B");
        if (!same || most > PacketWriter.Mtu || packets < kinds / ConstructBeacons.MaxEntries)
            Fail($"inventory beacon: the counts' message (same {same}, {packets} packets, {most} B)");
        // A change to one kind costs one entry.
        v.Counts[3]++;
        v.Unsent = true;
        ConstructBeacons.Write(w, v);
        if (w.Length > 32)
            Fail($"inventory beacon: one change took {w.Length} B");
        // Leaving a network: the state alone, and the client's list goes.
        Array.Clear(v.Counts);
        v.Mask = 0;
        v.State = BeaconState.None;
        v.Stores = v.Parts = v.Bulk = 0;
        v.Reset = v.Unsent = true;
        ConstructBeacons.Write(w, v);
        var rr = new PacketReader(w.Span[2..]);
        ConstructBeacons.Read(ref rr, heard);
        if (w.Length > 32 || heard.Counts.Any(n => n != 0) || heard.State != BeaconState.None)
            Fail($"inventory beacon: leaving the network ({w.Length} B)");
        foreach (var (bad, what) in ((byte[], string)[])[([0, 99, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], "a state"), ([0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 255, 255, 0], "a list too long")])
            try
            {
                var br = new PacketReader(bad);
                ConstructBeacons.Read(ref br, null);
                Fail($"inventory beacon: {what} wasn't refused");
            }
            catch (MalformedPacketException)
            {
            }
        var store = new CStore { BeaconStores = 34, BeaconParts = 512, Busy = true };
        store.Reset(0);
        store.BeaconStores = 34;
        store.BeaconParts = 512;
        w.Begin(Msg.CBody);
        ConstructStorage.Write(w, store);
        var sr = new PacketReader(w.Span[1..]);
        var back = new CStore();
        ConstructStorage.Read(ref sr, back);
        if (back.BeaconStores != 34 || back.BeaconParts != 512 || !back.Empty)
            Fail($"inventory beacon: a store's numbers read back {back.BeaconStores}, {back.BeaconParts}");
    }

    /// <summary>
    /// "Inventory beacon network": a real host, two clients and a late joiner over the transport, a survival map. The clients
    /// hear the counts and the state; the Tab window lists what the network holds and a click takes it; both grab the last one
    /// and exactly one gets it, the other told why; a part set down refills from the network and comes back to it; a
    /// load of material; out of range and a flat battery with their reasons; a joiner hears it all.
    /// </summary>
    private static void CheckBeaconNet()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail($"inventory beacon network: the {ConstructYardMap} map is missing");
            return;
        }
        const ushort port = 26491;
        var (keepMenu, keepInfinite) = (sb.PartsMenu, sb.InfiniteParts);
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        BeaconTable.Use(BeaconTable.Parse("{\"format\": \"call-of-slop-beacons/1\", \"beacons\": [{\"id\": \"inventory\", \"rangeMetres\": 12}]}")); // (the yard is small)
        yard.RestoreProps();
        World.SetMap(yard);
        sb.InfiniteParts = false;
        sb.PartsMenu = false;
        try
        {
            using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
            server.Mode = GameMode.Sandbox;
            using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Beta", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Game[] both = [a, b];
            Pump(server, both, 1.0, ref now, clock);
            for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
                PumpFast(server, both, 0.25, ref now);
            Pump(server, both, 0.3, ref now, clock);
            if (a.LocalId == byte.MaxValue || b.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var pa) || !server.Players.TryGetValue(b.LocalId, out var pb))
            {
                Fail("inventory beacon network: the clients didn't join");
                return;
            }
            foreach (var p in (Player[])[pa, pb])
                if (!p.Alive)
                    server.SelfTestRespawn(p, now);
            Pump(server, both, 0.3, ref now, clock);
            var w = server.Constructs;
            if (OpenSpot(w) is not { } spot || BuildBlueprint(w, "Beacon Depot", spot.X, spot.Z) is not { } c)
            {
                Fail("inventory beacon network: no Beacon Depot on the yard");
                return;
            }
            c.Owner = 255; // (nobody's: both clients may use it)
            c.Charge = c.ChargeCapacity;
            int bi = PartNamed(c, BeaconPart), shelf = PartNamed(c, "Parts Shelf"), chest = PartNamed(c, "Chest"), bin = PartNamed(c, "Materials Bin");
            var lamp = ConstructParts.Need("Lamp");
            var button = ConstructParts.Need("Button");
            var metal = RecyclingTable.Current.IdOf("metal");
            w.SetIn(c, shelf, 2, lamp);
            w.SetIn(c, shelf, 2, lamp);
            w.SetIn(c, chest, 0, button);
            w.Deposit(c, bin, 250, metal);
            var near = spot + new Vector3(0, 0.05f, 5f);
            pa.Teleport(near);
            pb.Teleport(near + new Vector3(1.5f, 0, 0));
            bool Await(Func<bool> done, double seconds, Game[] peers)
            {
                for (var t = 0.0; t < seconds && !done(); t += 0.05)
                    Pump(server, peers, 0.05, ref now, clock);
                return done();
            }
            BeaconView Of(Game g) => g.SandboxBeacon(g.LocalId);
            if (!Await(() => Of(a) is { State: BeaconState.On, Stores: 3, Parts: 3 } v && v.Counts[lamp.Id] == 2 && v.Counts[button.Id] == 1 && v.Materials[metal] == 250 && Of(b).On, 3, both))
            {
                Fail($"inventory beacon network: the client's counts ({Of(a).State}, {Of(a).Stores} stores, {Of(a).Parts} parts, {Of(a).Counts[lamp.Id]} lamps; server {server.Constructs.Beacons.Count} beacons)");
                return;
            }
            if (!a.SandboxBeaconOn || !a.SandboxInventoryAllowed || a.SandboxPartsMenu || Of(a).Line != "Beacon: 3 stores, 3 parts, 250 material, range 12 m")
                Fail($"inventory beacon network: the client's Tab and HUD ({a.SandboxBeaconOn}, \"{Of(a).Line}\")");
            if (a.Constructs.Get(c.Id) is not { } ac || a.Constructs.StoreOf(ac, bi) is not { } st || ConstructBeacons.Look(ac.DefOf(bi), st, false) != "Beacon: 3 stores, 3 parts, range 12 m")
                Fail("inventory beacon network: the client's look line");

            BeaconWindow(server, a, b, lamp, button, ref now, clock);

            // The last lamp: both ask in the same pump; exactly one gets it, the other is told why.
            if (!Await(() => server.SandboxHands.Hand(pa.Id).Held == ConstructParts.None && Of(a).Counts[lamp.Id] > 0, 2, both))
                Fail("inventory beacon network: the window's lamp should have gone");
            var extra = Of(a).Counts[lamp.Id] - 1;
            for (var k = 0; k < extra; k++)
                w.TakeOut(c, shelf, 2);
            Await(() => Of(a).Counts[lamp.Id] == 1 && Of(b).Counts[lamp.Id] == 1, 2, both);
            a.SandboxTakePart(lamp.Id);
            b.SandboxTakePart(lamp.Id);
            Await(() => a.SandboxNotice.Length + b.SandboxNotice.Length > 0 && server.SandboxHands.Hand(pa.Id).Held + server.SandboxHands.Hand(pb.Id).Held != 2 * ConstructParts.None, 2, both);
            var got = (server.SandboxHands.Hand(pa.Id).Held == lamp.Id ? 1 : 0) + (server.SandboxHands.Hand(pb.Id).Held == lamp.Id ? 1 : 0);
            var loser = server.SandboxHands.Hand(pa.Id).Held == lamp.Id ? b : a;
            if (got != 1 || !loser.SandboxNotice.Contains("no Lamp left on the network"))
                Fail($"inventory beacon network: the last lamp: {got} got it; the other heard \"{loser.SandboxNotice}\"");
            var (winner, pw) = loser == a ? (b, pb) : (a, pa);

            // A row from the network: the winner's lamp is set down; the next comes from the store.
            w.SetIn(c, shelf, 2, lamp);
            Await(() => Of(winner).Counts[lamp.Id] == 1, 2, both);
            winner.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
            Pump(server, both, 0.2, ref now, clock);
            var ids = w.Bodies.Select(x => x.Id).ToHashSet();
            var ground = ConstructWorld.Ground(World.Query, pw.Position.X, pw.Position.Z + 2);
            winner.SandboxSend(CActKind.PlaceNew, at: ground, normal: Vector3.UnitY, yaw: 0);
            if (!Await(() => w.Bodies.Any(x => !ids.Contains(x.Id)) && w.StoreOf(c, shelf)!.Def[2] == ConstructParts.None && server.SandboxHands.Hand(pw.Id).Held == lamp.Id, 5, both))
                Fail($"inventory beacon network: placing from the network (held {server.SandboxHands.Hand(pw.Id).Held}, slot {w.StoreOf(c, shelf)!.Def[2]}, \"{winner.SandboxNotice}\")");
            var row = w.Bodies.FirstOrDefault(x => !ids.Contains(x.Id));

            // It comes back: to the network, and the HUD says where.
            if (row != null)
            {
                Pump(server, both, 0.2, ref now, clock);
                winner.SandboxSend(CActKind.Remove, body: row.Id, part: 0);
                if (!Await(() => w.Get(row.Id) == null && CountOn(w, c, lamp.Id) == 1 && winner.SandboxNotice.Contains("into the network"), 5, both))
                    Fail($"inventory beacon network: a part taken off ({CountOn(w, c, lamp.Id)} on the network, \"{winner.SandboxNotice}\")");
            }

            // A load of material by the message.
            a.SandboxTakeMaterial(metal);
            if (!Await(() => a.SandboxInventory(a.LocalId).Materials[metal] == 100 && Of(a).Materials[metal] == 150, 3, both))
                Fail($"inventory beacon network: a load of metal ({a.SandboxInventory(a.LocalId).Materials[metal]}, {Of(a).Materials[metal]} left)");

            // Out of range, then a flat battery: normal rules, with a reason.
            var awayAt = spot + new Vector3(0, 0.05f, 20f);
            pa.Teleport(awayAt);
            if (!Await(() => Of(a).State == BeaconState.OutOfRange, 3, both) || a.SandboxBeaconOn || a.SandboxInventoryAllowed || !a.SandboxNoMenuWhy.Contains("out of range") || Of(a).Counts.Any(n => n != 0))
                Fail($"inventory beacon network: out of range: {Of(a).State} \"{a.SandboxNoMenuWhy}\" at {Vector3.Distance(pa.Position, spot):0.0} m");
            a.SandboxTakePart(button.Id);
            if (!Await(() => a.SandboxNotice.Contains("out of range"), 2, both))
                Fail($"inventory beacon network: a take out of range: \"{a.SandboxNotice}\"");
            c.Charge = 0;
            if (!Await(() => Of(b).State == BeaconState.NoPower && Of(b).Line.Contains("no power"), 3, both))
                Fail($"inventory beacon network: a flat battery: {Of(b).State} \"{Of(b).Line}\"");
            c.Charge = c.ChargeCapacity;
            pa.Teleport(near);

            // A late joiner hears the network whole.
            using var late = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Late", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Game[] all = [a, b, late];
            Pump(server, all, 1.0, ref now, clock);
            for (var wait = 0; wait < 40 && (late.LocalId == byte.MaxValue || !server.Players.ContainsKey(late.LocalId)); wait++)
                Pump(server, all, 0.1, ref now, clock);
            if (!server.Players.TryGetValue(late.LocalId, out var pl))
            {
                Fail("inventory beacon network: the joiner didn't join");
                return;
            }
            if (!pl.Alive)
                server.SelfTestRespawn(pl, now);
            Pump(server, all, 0.3, ref now, clock);
            pl.Teleport(near + new Vector3(-1.5f, 0, 0));
            if (!Await(() => Of(late) is { State: BeaconState.On, Stores: 3 } v && v.Counts[button.Id] == Of(a).Counts[button.Id] && v.Materials[metal] == 150, 4, all))
                Fail($"inventory beacon network: the joiner ({Of(late).State}, {Of(late).Stores} stores, {Of(late).Materials[metal]} metal)");
            if (late.Constructs.Get(c.Id) is not { } lc || late.Constructs.StoreOf(lc, bi) is not { Busy: true, BeaconStores: 3 })
                Fail("inventory beacon network: the joiner's look line numbers");
        }
        finally
        {
            sb.InfiniteParts = keepInfinite;
            sb.PartsMenu = keepMenu;
            BeaconTable.Use(null);
            yard.RestoreProps();
        }
    }

    /// <summary>The Tab window on a client, headless: only what the network holds, the hidden tabs gone, greys with the box off, the Materials tab, a click takes one; no ImGui error.</summary>
    private static void BeaconWindow(Game server, Game a, Game b, ConstructPartDef lamp, ConstructPartDef button, ref double now, Stopwatch clock)
    {
        var ui = new Ui();
        ui.InitHeadless(new float2(1600, 900));
        ui.SandboxCounts = Game.InventoryCounts.Instance;
        var settings = new Settings();
        string? error = null;
        void Frames(int n)
        {
            for (var i = 0; i < n && error == null; i++)
                try
                {
                    ui.BeginFrame();
                    ui.DrawSandboxParts(a, settings);
                    ui.EndFrame();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
        }
        ui.OpenSandboxParts(focusSearch: false);
        Frames(3);
        var listed = ui.SandboxInventoryListedItems.ToArray();
        if (!ui.SandboxPartsOpen || !ui.SandboxInventoryOnBeacon || listed.Length != 2 || !listed.Contains(lamp.Id) || !listed.Contains(button.Id))
            Fail($"inventory beacon network: the window lists {listed.Length} parts (open {ui.SandboxPartsOpen}, on the network {ui.SandboxInventoryOnBeacon})");
        if (Game.InventoryCounts.Instance.PartCount(a, a.LocalId, lamp.Id) != 2)
            Fail($"inventory beacon network: a tile's count is {Game.InventoryCounts.Instance.PartCount(a, a.LocalId, lamp.Id)}");
        ui.SandboxNetHideMissing = false;
        ui.SetSandboxPartsSearch(" ");
        ui.ClearSandboxPartsSearch();
        ui.SetSandboxInventoryNode(-1, -1);
        Frames(3);
        if (ui.SandboxPartsListed <= 2)
            Fail($"inventory beacon network: with the box off the window should list the rest, greyed ({ui.SandboxPartsListed})");
        ui.SandboxNetHideMissing = true;
        ui.SetSandboxInventoryNode(-1, -1);
        ui.SetSandboxInventoryTab(Ui.SbTab.Materials);
        Frames(3);
        ui.SetSandboxInventoryTab(Ui.SbTab.Blueprints);
        Frames(2);
        if (ui.SandboxInventoryTab != Ui.SbTab.Parts)
            Fail($"inventory beacon network: creative's Blueprints tab on the network ({ui.SandboxInventoryTab})");
        ui.SetSandboxPartsSearch(lamp.Name);
        Frames(2);
        if (!ui.SandboxInventoryTake(a))
            Fail("inventory beacon network: the window had nothing to take");
        Frames(2);
        ui.CloseSandboxParts();
        Pump(server, [a, b], 0.3, ref now, clock);
        if (server.Players.TryGetValue(a.LocalId, out var p) && server.SandboxHands.Hand(p.Id).Held != lamp.Id)
            Fail($"inventory beacon network: a click in the window (held {server.SandboxHands.Hand(p.Id).Held}, \"{a.SandboxNotice}\")");
        if (error != null)
            Fail($"inventory beacon network: the window threw: {error}");
        if (server.Players.TryGetValue(a.LocalId, out var pp))
            server.SandboxHands.Hand(pp.Id).Held = ConstructParts.None;
        ui.Dispose();
    }
}
