using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;

namespace CallOfSlop;

public static partial class SelfTest
{
    private static int RecyclerPartOf(Construct c)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).IsRecycler)
                return i;
        return -1;
    }

    private static Vector3 IntakeCentre(Construct c, int part)
    {
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        var d = c.DefOf(part);
        return t + Vector3.Transform(c.Parts[part].Pos + Rot24.Apply(c.Parts[part].Rot, (d.IntakeMin + d.IntakeMax) * 0.5f), q);
    }

    /// <summary>
    /// "Salvage haul" (board item sh01, decision 31; docs/sandbox/CONSTRUCTS.md "Salvage haul"): there is no on-site scrap.
    /// A buggy's shot engine taken off with the wrench (the real edit path) is a wrecked-part body carrying its mass and
    /// material, which can't be fitted to anything and can't be salvaged again; an intact part taken off stays a usable
    /// part. A cargo volume hauls the wreck (not fed while carried); a Recycler in the Recycling Station takes in a wreck
    /// left in its intake, does nothing without power, and with power gives metal by mass into its bin and down the chute
    /// into the Materials Bin, and then no wreck is left; it works at once and unpowered in creative; an intact part in
    /// the intake is left alone; a wrecked bundle gives its material; a broken bin spills bundles of its material; old
    /// scrap counts become metal in bins; the table, the part files and the store's wire bytes are checked; the idle
    /// step allocates nothing.
    /// </summary>
    private static void CheckSalvageHaul()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox == null)
        {
            Fail($"salvage haul: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        var table = RecyclingTable.Current;
        var recDef = ConstructParts.Need("Recycler");
        var binDef = ConstructParts.Need("Materials Bin");
        var bundleDef = ConstructParts.Need(ConstructStorage.MaterialBundle);
        var metal = table.IdOf("metal");

        // ---- The data: the table, the parts, a bad recycler block.
        var (engM, engN) = table.YieldOf(new CPart { Def = ConstructParts.Need("Engine").Id });
        var (lampM, _) = table.YieldOf(new CPart { Def = ConstructParts.Need("Lamp").Id });
        var rubber = table.YieldOf(new CPart { Def = ConstructParts.Need("Wheel").Id }).Material;
        Line($"salvage haul data: table problem \"{table.Problem}\", materials {string.Join("/", table.Materials)}; Engine -> {engN} {table.NameOf(engM)}; Lamp -> {table.NameOf(lampM)}; Wheel -> {table.NameOf(rubber)}; "
             + $"Recycler: intake {recDef.IntakeMin}..{recDef.IntakeMax}, bin {recDef.BulkCapacity} {recDef.BulkMaterial}, welder {recDef.Tool == ConstructTool.Welder}, physics-only {recDef.PhysicsOnly}");
        Want(table.Problem.Length == 0 && metal == 1 && engM == metal && engN == (int)MathF.Round(ConstructParts.Need("Engine").Mass * 0.5f) && lampM == table.IdOf("circuits") && rubber == table.IdOf("rubber")
             && recDef.IsRecycler && recDef.Tool == ConstructTool.Welder && recDef.BulkMaterial == ConstructStorage.Any && binDef.BulkMaterial == ConstructStorage.Any && bundleDef.Yield > 0,
            "the table, the Recycler, the Materials Bin and the Material Bundle read right");
        var text = File.ReadAllText(Path.Combine(ConstructParts.Folder, recDef.Folder, recDef.File));
        string Refused(Action<JsonNode> edit)
        {
            var node = JsonNode.Parse(text)!;
            edit(node);
            try
            {
                ConstructParts.Parse(node.ToJsonString(), "bad.json");
                return "accepted";
            }
            catch (ConstructPartException ex)
            {
                return ex.Message;
            }
        }
        var tiny = Refused(n => n["storage"]!["recycler"]!["max"] = new JsonArray(-0.4f, 0.1f, 0.43f));
        var named = Refused(n => n["storage"]!["bulk"]!["material"] = "metal");
        var unknown = Refused(n => n["storage"]!["bulk"]!["material"] = "jam");
        Want(tiny.Contains("intake") && named.Contains("recycler") && unknown.Contains("bulk material"), $"bad recycler blocks are refused (\"{tiny}\" / \"{named}\" / \"{unknown}\")");
        var badTable = RecyclingTable.Parse("{\"format\": \"call-of-slop-recycling/1\", \"materials\": [\"metal\"], \"classes\": {\"steel\": {\"gives\": \"jam\", \"perKg\": 1}}}");
        Want(badTable.Problem.Contains("jam"), $"a table whose class gives an unknown material is refused (\"{badTable.Problem}\")");

        // ---- The scene: a Recycling Station, a buggy, a cargo rack.
        var w = new ConstructWorld();
        var e = new ConstructEdits { Inventory = new SandboxInventories(), InfiniteParts = false };
        e.Inventory.SetInfinite(false);
        const byte me = 5;
        var now = 0.0;
        var eye = Vector3.Zero;
        void Ticks(int n)
        {
            for (var k = 0; k < n; k++)
            {
                now += World.TickDt;
                e.Tick(w, now, _ => eye);
                w.Step(World.Query, World.TickDt);
            }
        }
        bool Until(Func<bool> done, double seconds)
        {
            for (var k = 0; k < (int)(seconds * World.TickRate) && !done(); k++)
                Ticks(1);
            return done();
        }
        if (OpenSpot(w) is not { } spot)
        {
            Fail("salvage haul: no open ground on the yard");
            return;
        }
        Construct? Build(string blueprint, float dx, float dz)
        {
            var c = ConstructBuild.BuildOnGround(w, ConstructBuild.FindBlueprint(blueprint)!, ConstructWorld.Ground(World.Query, spot.X + dx, spot.Z + dz), 0, out var why);
            if (c == null)
                problems.Add($"the {blueprint} didn't build ({why})");
            return c;
        }
        var station = Build("Recycling Station", 0, 0);
        var car = Build("Buggy", -6, 0);
        var rack = Build("Cargo Rack", 0, -7);
        if (station == null || car == null || rack == null || RecyclerPartOf(station) < 0)
        {
            Fail($"salvage haul: {string.Join("; ", problems)}");
            return;
        }
        car.Owner = 255;
        Ticks(60);
        var ri = RecyclerPartOf(station);
        var st = w.StoreOf(station, ri)!;
        var binIndex = Enumerable.Range(0, station.PartCount).First(i => station.DefOf(i) == binDef);
        var outBin = w.StoreOf(station, binIndex)!;
        var inv = e.Inventory.Of(me);
        Want(station.HasBattery && station.Parts[ri].Powered && station.RouteCount == 1, $"the station has a battery reaching the Recycler ({station.HasBattery}, {station.Parts[ri].Powered}) and its chute ({station.RouteCount})");

        // ---- Shoot the engine until wrecked, take it off with the wrench: a wrecked-part body, no scrap.
        var engDef = ConstructParts.Need("Engine");
        var eng = PartOf(car, PartRole.Engine);
        w.DamagePart(car, eng, 100000, out _);
        Ticks(30);
        eng = PartOf(car, PartRole.Engine);
        Want(eng >= 0 && car.Parts[eng].Broken, "the shot engine is wrecked in place");
        eye = PartCentre(car, eng) + new Vector3(0, 0, 1.6f);
        e.SetTool(me, engDef.Tool);
        var began = e.BeginRemove(w, me, eye, car.Id, eng, now);
        Ticks((int)((ConstructBuild.RemoveSeconds(engDef) + 0.3) * World.TickRate));
        var wreck = w.Bodies.FirstOrDefault(b => b.Wreck && b.PartCount == 1 && b.DefOf(0) == engDef && b.Parts[0].Broken);
        var said = e.Done.Count > 0 ? e.Done[^1] : "";
        Line($"salvage haul: the tool on the wrecked engine (\"{began ?? "ok"}\"): wrecked-part body {wreck?.Id.ToString() ?? "none"}, said \"{said}\", materials carried {inv.TotalMaterials}, engine parts in the inventory {inv.Count(engDef.Id)}");
        Want(began == null && wreck != null && ConstructEdits.WreckedAlone(wreck) && !wreck.Loose && inv.TotalMaterials == 0 && inv.Count(engDef.Id) == 0 && said.Contains("wrecked part"),
            "taking the wrecked engine off leaves a wrecked-part body (not scrap, not an item)");
        if (wreck == null)
        {
            Fail($"salvage haul: {string.Join("; ", problems)}");
            return;
        }
        Want(wreck.DefOf(0).Mass == engDef.Mass && wreck.Parts[0].Health == 0 && wreck.Damaged, "the wrecked part carries the engine's mass and is marked damaged");
        eye = PartCentre(wreck, 0) + new Vector3(0, 0, 1.6f);
        var again = e.BeginRemove(w, me, eye, wreck.Id, 0, now);
        var ghostRefused = ConstructBuild.Place(wreck, 0, 0, ConstructParts.Need("Frame Block"), 0, 0, out _);
        Want(again != null && again.Contains("Recycler") && ghostRefused != null, $"a wrecked part is not salvaged again (\"{again}\") and nothing fits to it (\"{ghostRefused}\")");

        // An intact part taken off stays a usable part.
        var wheelDef = ConstructParts.Need("Wheel");
        var wi = PartOf(car, PartRole.Wheel);
        eye = PartCentre(car, wi) + new Vector3(0, 0, 1.6f);
        e.SetTool(me, ConstructTool.Welder);
        var wBegan = e.BeginRemove(w, me, eye, car.Id, wi, now);
        Ticks((int)((ConstructBuild.RemoveSeconds(wheelDef) + 0.3) * World.TickRate));
        Want(wBegan == null && (inv.Count(wheelDef.Id) == 1 || e.Hand(me).Held == wheelDef.Id) && inv.TotalMaterials == 0, $"an intact wheel taken off stays a usable part (\"{wBegan}\", inventory {inv.Count(wheelDef.Id)}, hand {ConstructParts.Get(e.Hand(me).Held)?.Name})");

        // ---- Haul it in the cargo rack.
        var vi = Enumerable.Range(0, rack.PartCount).First(i => ConstructCargo.IsVolume(rack.DefOf(i)));
        Want(ConstructCargo.IsFrozen(rack.Parts[vi]), "the Cargo Rack's volume starts frozen");
        ConstructCargo.Box(rack, vi, out var boxCentre, out _);
        var into = rack.ToWorld(boxCentre);
        wreck.Pos += into - wreck.WorldCom;
        wreck.Vel = wreck.AngVel = Vector3.Zero;
        wreck.Wake();
        var caught = Until(() =>
        {
            w.CargoScanNow = true;
            return wreck.CarriedBy == rack.Id;
        }, 5);
        Want(caught, "the cargo volume catches the wrecked part");
        var off0 = rack.ToLocal(wreck.WorldCom);
        var rack0 = rack.Pos;
        rack.Pos += new Vector3(0, 0, 3f);
        Ticks(10);
        var off1 = rack.ToLocal(wreck.WorldCom);
        var fedWhileCarried = ConstructRecycler.Feedable(wreck);
        Want(Vector3.Distance(off1, off0) < 0.05f && Vector3.Distance(rack.Pos, rack0) > 1f && !fedWhileCarried, $"the wreck rides with the rack (at {off0} then {off1} in its frame) and a carried wreck is not feedable ({fedWhileCarried})");
        Want(e.Use(w, me, PartCentre(rack, vi) + new Vector3(0, 0.6f, 0.8f), rack.Id, vi) == null && Until(() => wreck.CarriedBy == 0, 2), "F on the volume lets the wreck go");

        // ---- Delivered to the Recycler, with the battery flat: nothing happens.
        station.Charge = 0;
        Ticks(3);
        void Deliver(Construct b)
        {
            b.Pos += IntakeCentre(station, ri) + new Vector3(0, 0.15f, 0) - b.WorldCom;
            b.Vel = b.AngVel = Vector3.Zero;
            b.Wake();
        }
        Deliver(wreck);
        Ticks(150);
        Want(!station.PowerOn && w.Get(wreck.Id) != null && st.Amount == 0 && !st.Busy && outBin.Amount == 0, $"without power nothing happens (power on {station.PowerOn}, wreck still there {w.Get(wreck.Id) != null}, {st.Amount} in the output)");

        // ---- Power back: it takes the wreck in, works, and the metal arrives by mass.
        station.Charge = station.ChargeCapacity;
        var took = Until(() => w.Get(wreck.Id) == null, 3);
        var busy = st.Busy;
        var drew = false;
        Until(() =>
        {
            drew |= station.PowerUse > 0;
            return !st.Busy;
        }, 20);
        var expected = (int)MathF.Round(engDef.Mass * 0.5f);
        Until(() => outBin.Amount >= expected, 12); // (the chute carries it on to the Materials Bin)
        var total = st.Amount + outBin.Amount;
        Line($"salvage haul: powered Recycler took the engine in {took} (busy {busy}, drew power {drew}); {expected} metal expected: output {st.Amount}, bin {outBin.Amount} {table.NameOf(outBin.Material)}; wreck left {w.Get(wreck.Id) != null}");
        Want(took && busy && drew && total == expected && outBin.Amount > 0 && outBin.Material == metal && w.Get(wreck.Id) == null && !st.Busy,
            "a powered Recycler turns the wreck into metal by its mass, draws power while it works, and the chute fills the Materials Bin");
        Want(!w.Bodies.Any(b => b.Wreck && b.PartCount == 1 && b.DefOf(0) == engDef), "no wrecked engine is left anywhere");

        // ---- An intact part in the intake is left alone; a wrecked wheel gives rubber into an output that holds metal: it waits.
        var lamp = ConstructBuild.PlaceNew(w, ConstructParts.Need("Lamp"), ConstructWorld.Ground(World.Query, spot.X + 3, spot.Z + 3), 0)!;
        Deliver(lamp);
        Ticks(90);
        Want(w.Get(lamp.Id) != null && !st.Busy, "an intact part left in the intake is not taken in");
        w.Remove(lamp);
        ClearBins(st, outBin);
        Ticks(30);

        // A wrecked bundle gives its material: a spilled bin's bundles feed the Recycler and the amounts add up.
        var spillBin = Build("Recycling Station", 8, 0);
        if (spillBin != null)
        {
            var bi = Enumerable.Range(0, spillBin.PartCount).First(i => spillBin.DefOf(i) == binDef);
            var rubberId = table.IdOf("rubber");
            w.Deposit(spillBin, bi, 250, rubberId);
            var ids = w.Bodies.Select(b => b.Id).ToHashSet();
            w.DamagePart(spillBin, bi, 100000, out _);
            Ticks(30);
            var bundles = w.Bodies.Where(b => !ids.Contains(b.Id) && b.PartCount == 1 && b.DefOf(0) == bundleDef && b.Parts[0].Broken).ToList();
            var sum = bundles.Sum(b => table.YieldOf(b.Parts[0]).Amount);
            var allRubber = bundles.All(b => table.YieldOf(b.Parts[0]).Material == rubberId);
            Want(bundles.Count >= 1 && sum >= 250 && allRubber, $"a broken bin of 250 rubber spills {bundles.Count} bundles worth {sum} rubber");
        }

        // ---- Creative: instant, no power.
        w.CreativeRecycling = true;
        station.Charge = 0;
        Ticks(3);
        var spare = ConstructBuild.PlaceNew(w, ConstructParts.Need("Lamp"), ConstructWorld.Ground(World.Query, spot.X + 3, spot.Z + 3), 0)!;
        spare.Parts[0].Broken = true;
        spare.Parts[0].Health = 0;
        spare.Wreck = true;
        spare.Dirty = true;
        spare.Rebuild();
        Deliver(spare);
        var inst = Until(() => w.Get(spare.Id) == null, 2);
        Want(inst && !st.Busy && st.Amount + outBin.Amount > 0, "in creative a Recycler works at once and needs no power");
        w.CreativeRecycling = false;

        // ---- Old scrap becomes metal in bins beside the player.
        var inv2 = new SandboxInventory();
        var ids2 = w.Bodies.Select(b => b.Id).ToHashSet();
        var report = ConstructLegacyScrap.Import(w, inv2, ConstructWorld.Ground(World.Query, spot.X - 12, spot.Z), 0f, 750);
        var legacyBins = w.Bodies.Where(b => !ids2.Contains(b.Id) && b.PartCount == 1 && b.DefOf(0) == binDef).ToList();
        var legacyMetal = legacyBins.Sum(b => w.StoreOf(b, 0)?.Amount ?? 0);
        Line($"salvage haul: 750 legacy scrap -> \"{report}\": {legacyBins.Count} bins hold {legacyMetal} metal, carried {inv2.Materials[metal]}");
        Want(legacyMetal + inv2.Materials[metal] == 750 && legacyBins.Count >= 1 && legacyBins.All(b => w.StoreOf(b, 0)?.Material == metal), "old scrap counts load as metal in bins (and the rest carried)");

        // ---- Nothing allocates while the Recycler idles; the store reads back as it was written.
        Ticks(60);
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 120; k++)
            w.Step(World.Query, World.TickDt);
        var alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        Want(alloc < 4096, $"the world step with an idle Recycler allocates {alloc} B over 120 ticks");
        var pw = new PacketWriter(2048);
        st.Material = metal;
        st.Amount = 77;
        st.Busy = true;
        ConstructStorage.Write(pw, st);
        var copy = new CStore();
        var rd = new PacketReader(pw.Span);
        ConstructStorage.Read(ref rd, copy);
        Want(copy.Amount == 77 && copy.Material == metal && copy.Busy, $"a store's bytes carry amount, material and busy ({copy.Amount}, {copy.Material}, {copy.Busy})");
        foreach (var p in problems)
            Fail($"salvage haul: {p}");
    }

    /// <summary>
    /// "Salvage haul network": a real host and a separate client over the transport, survival rules. The client spawns the
    /// Recycling Station; a wrecked engine delivered to its intake on the host is taken in (gone on the client too), and the
    /// client's copy of the stores hears the busy flag, then the metal in the bin the chute fills; the client's own
    /// inventory message carries carried materials (and no scrap) both ways.
    /// </summary>
    private static void CheckSalvageHaulNet()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail($"salvage haul network: the {ConstructYardMap} map is missing");
            return;
        }
        const ushort port = 26397;
        var keep = sb.InfiniteParts;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        sb.InfiniteParts = false;
        try
        {
            using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
            server.Mode = GameMode.Sandbox;
            using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Pump(server, [a], 1.0, ref now, clock);
            for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
                PumpFast(server, [a], 0.25, ref now);
            Pump(server, [a], 0.3, ref now, clock);
            if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
            {
                Fail("salvage haul network: the client didn't join");
                return;
            }
            if (!sp.Alive)
                server.SelfTestRespawn(sp, now);
            Pump(server, [a], 0.3, ref now, clock);
            var problems = new List<string>();
            void Want(bool ok, string what)
            {
                if (!ok)
                    problems.Add(what);
            }
            var w = server.Constructs;
            if (OpenSpot(w) is not { } spot)
            {
                Fail("salvage haul network: no open ground on the yard");
                return;
            }
            sp.Teleport(spot + new Vector3(0, 0.05f, 0));
            sp.Yaw = 0;
            Pump(server, [a], 0.3, ref now, clock);
            var before = w.Bodies.Select(b => b.Id).ToHashSet();
            a.SandboxSend(CActKind.Blueprint, part: (ushort)IndexOfBlueprint("Recycling Station"));
            Pump(server, [a], 1.0, ref now, clock);
            var station = w.Bodies.FirstOrDefault(b => !before.Contains(b.Id) && RecyclerPartOf(b) >= 0);
            if (station == null)
            {
                Fail($"salvage haul network: the Recycling Station didn't spawn (\"{a.SandboxNotice}\")");
                return;
            }
            var ri = RecyclerPartOf(station);
            var binDef = ConstructParts.Need("Materials Bin");
            var bi = Enumerable.Range(0, station.PartCount).First(i => station.DefOf(i) == binDef);
            var table = RecyclingTable.Current;
            var engDef = ConstructParts.Need("Engine");
            var wreck = ConstructBuild.PlaceNew(w, engDef, IntakeCentre(station, ri), 0)!;
            wreck.Parts[0].Broken = true;
            wreck.Parts[0].Health = 0;
            wreck.Wreck = true;
            wreck.Damaged = true;
            wreck.Dirty = true;
            wreck.Rebuild();
            wreck.Pos += IntakeCentre(station, ri) + new Vector3(0, 0.15f, 0) - wreck.WorldCom;
            wreck.Vel = Vector3.Zero;
            var wreckId = wreck.Id;
            Pump(server, [a], 0.4, ref now, clock);
            var hs = w.StoreOf(station, ri)!;
            var hostBusy = false;
            var clientBusy = false;
            for (var k = 0; k < 40 && !(hostBusy && clientBusy); k++)
            {
                Pump(server, [a], 0.1, ref now, clock);
                hostBusy |= hs.Busy;
                clientBusy |= a.Constructs.Get(station.Id) is { } cs && a.Constructs.StoreOf(cs, ri) is { Busy: true };
            }
            Want(hostBusy && clientBusy, $"the busy flag reaches the client (host {hostBusy}, client {clientBusy})");
            var expected = (int)MathF.Round(engDef.Mass * 0.5f);
            var arrived = false;
            var wreckGone = false;
            for (var k = 0; k < 160 && !(arrived && wreckGone); k++)
            {
                Pump(server, [a], 0.1, ref now, clock);
                var cs = a.Constructs.Get(station.Id);
                var cb = cs != null ? a.Constructs.StoreOf(cs, bi) : null;
                arrived = cb is { Amount: var amt, Material: var m } && amt == expected && m == table.IdOf("metal");
                wreckGone = a.Constructs.Get(wreckId) == null && w.Get(wreckId) == null;
            }
            Want(arrived && wreckGone, $"the client sees the wrecked part gone ({wreckGone}) and {expected} metal in the Materials Bin ({arrived}; host bin {w.StoreOf(station, bi)?.Amount})");
            // The client's own carried materials come over without any scrap.
            var inv = server.SandboxInventory(sp.Id);
            inv.AddMaterial(table.IdOf("wood"), 12);
            Pump(server, [a], 0.6, ref now, clock);
            var mine = a.SandboxInventory(a.LocalId);
            Want(mine.Materials[table.IdOf("wood")] == 12 && mine.TotalMaterials == 12, $"the client's inventory carries its 12 wood ({mine.Materials[table.IdOf("wood")]})");
            // Old scrap loads as metal in bins by the host.
            var line = server.SandboxImportLegacyScrap(sp.Id, 120);
            Pump(server, [a], 0.6, ref now, clock);
            Want(line.Contains("120 metal") || line.Contains("-> 120"), $"legacy scrap imported ({line})");
            foreach (var p in problems)
                Fail($"salvage haul network: {p}");
        }
        finally
        {
            sb.InfiniteParts = keep;
        }
    }

    private static void ClearBins(CStore a, CStore b)
    {
        a.Amount = b.Amount = 0;
        a.Material = b.Material = 0;
    }
}
