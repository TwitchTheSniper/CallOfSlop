using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// A rig for a Storage Volume: two Frame Blocks at opposite corners of a <paramref name="size"/> hull, the volume between
    /// them, its box filling the hull. Anchored, so it stands still. The volume's part index comes back.
    /// </summary>
    private static Construct? CbHull(ConstructWorld w, Vector3 at, Vector3 size, out int vol, bool anchored = true)
    {
        vol = -1;
        if (w.Create(at, Quaternion.Identity) is not { } c)
            return null;
        var block = ConstructParts.Need("Frame Block");
        var volume = ConstructParts.Need("Storage Volume");
        w.AddPart(c, block, Vector3.Zero, 0);
        w.AddPart(c, block, size, 0);
        vol = w.AddPart(c, volume, new Vector3(0, 0, size.Z), 0);
        c.Pinned = anchored;
        c.Rebuild();
        CbBox(c, vol, size + new Vector3(0.5f), size * 0.5f - new Vector3(0, 0, size.Z));
        return c;
    }

    /// <summary>Sets a volume's box directly (a rig; the player's way goes through the configure tool).</summary>
    private static void CbBox(Construct c, int vol, Vector3 size, Vector3 at)
    {
        ref var p = ref c.Parts[vol];
        var d = p.D;
        Span<byte> slots = stackalloc byte[6];
        ConstructCargo.BoxSlots(d, slots);
        ConstructConfig.Set(ref p, d, slots[0], size.X);
        ConstructConfig.Set(ref p, d, slots[1], size.Y);
        ConstructConfig.Set(ref p, d, slots[2], size.Z);
        ConstructConfig.Set(ref p, d, slots[3], at.X);
        ConstructConfig.Set(ref p, d, slots[4], at.Y);
        ConstructConfig.Set(ref p, d, slots[5], at.Z);
        c.Dirty = true;
        c.Rebuild();
    }

    private static int CbChute(ConstructWorld w, Construct c, int from, byte fromPort, int to, byte toPort)
    {
        var k = ConstructRoutes.Add(w, c, RouteType.Chute, from, fromPort, 255, 0, default, default, to, toPort);
        c.Dirty = true;
        c.Rebuild();
        return k;
    }

    private static int CbFind(Construct c, int from, int to)
    {
        for (var k = 0; k < c.RouteCount; k++)
            if (c.Routes[k].Type == RouteType.Chute && c.Routes[k].Source == from && c.Routes[k].To == to)
                return k;
        return -1;
    }

    private static int CbFitOn(ConstructWorld w, Construct c, int on, ConstructPartDef def, string what)
    {
        var od = c.DefOf(on);
        for (var s = 0; s < od.Sockets.Length; s++)
            for (var o = 0; o < def.Sockets.Length; o++)
                if (!c.SocketUsed(on, s) && ConstructBuild.Place(c, on, s, def, o, 0, out var g) == null)
                {
                    var i = ConstructBuild.Attach(w, c, def, g);
                    c.Rebuild();
                    return i;
                }
        Fail($"{what}: a {def.Name} doesn't fit on part {on}");
        return -1;
    }

    /// <summary>
    /// "Custom bins" (docs/sandbox/CONSTRUCTS.md "Storage volumes"): the part, the table's rates and bad blocks; the box
    /// counted in full as sized, its limits, over-full and refused resizes; chutes in and out by their named ports, parts and
    /// bulk, from a real bin and shelf, in blueprints; the mass and centre of mass on a driven buggy; a huge load stays
    /// finite; a split, a removal, a spill; the wire at huge amounts and 256 kinds; the read API; the real input (the
    /// configure tool's handles, R's drawing, F, a click) through the game's own frame; no allocation.
    /// </summary>
    private static void CheckCustomBins()
    {
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        var table = RecyclingTable.Current;
        var ore = table.IdOf("iron ore");
        var metal = table.IdOf("metal");
        var gold = table.IdOf("gold ore");
        var lamp = ConstructParts.Need("Lamp");
        var engine = ConstructParts.Need("Engine");
        var def = ConstructParts.Need("Storage Volume");
        var binDef = ConstructParts.Need("Materials Bin");
        var shelfDef = ConstructParts.Need("Parts Shelf");

        // ---- The data.
        Want(table.UnitsPerCubicMetre == 400 && table.ItemPackShare == 0.7f, $"the shipped rates ({table.UnitsPerCubicMetre}, {table.ItemPackShare})");
        var badRate = RecyclingTable.Parse("{\"format\": \"call-of-slop-recycling/1\", \"materials\": [\"metal\"], \"classes\": {}, \"roles\": {}, \"binUnitsPerCubicMetre\": 0}");
        Want(badRate.Problem.Contains("binUnitsPerCubicMetre"), $"a bad rate is refused (\"{badRate.Problem}\")");
        Want(def.IsVolumeStore && def.IsBin && def.Role == PartRole.Storage && !def.PhysicsOnly && ConstructCargo.HasBox(def) && !ConstructCargo.IsVolume(def), "the part's flags");
        var text = File.ReadAllText(Path.Combine(ConstructParts.Folder, def.Folder, def.File));
        string? noBox = null, badBlock = null;
        try
        {
            var cut = text.IndexOf("\"config\"", StringComparison.Ordinal);
            ConstructParts.Parse(text[..text.LastIndexOf(',', cut)] + "\n}", "nobox.json");
        }
        catch (ConstructPartException ex)
        {
            noBox = ex.Message;
        }
        try
        {
            ConstructParts.Parse(text.Replace("\"volume\": true", "\"volume\": true, \"capacity\": 5"), "badblock.json");
        }
        catch (ConstructPartException ex)
        {
            badBlock = ex.Message;
        }
        Want(noBox != null && noBox.Contains("box settings") && badBlock != null && badBlock.Contains("volume"), $"a volume with no box settings, or a capacity of its own, is refused (\"{noBox}\", \"{badBlock}\")");

        // ---- The ports, by name, one way each.
        var w = new ConstructWorld();
        var q = new DriveWorld();
        void Steps(int n)
        {
            for (var t = 0; t < n; t++)
                w.Step(q, World.TickDt);
        }
        void Load() => Steps(ConstructWorld.LoadEvery + 1);
        var inPort = ConstructRoutes.PortNamed(def, RouteType.Chute, "Chute in");
        var outPort = ConstructRoutes.PortNamed(def, RouteType.Chute, "Chute out");
        Want(inPort is { In: true, Out: false, Byte: 255 } && outPort is { In: false, Out: true } && outPort.Byte != 255, "the two named chute ports");
        var outByte = outPort?.Byte ?? (byte)255;
        var c = CbHull(w, new Vector3(0, 20, 0), new Vector3(8, 3, 6), out var vol);
        if (c == null)
        {
            Fail("custom bins: no rig");
            return;
        }
        Span<ConstructRoutes.LinkPort> ports = new ConstructRoutes.LinkPort[ConstructRoutes.MaxLinkPorts];
        var pn = ConstructRoutes.AllPorts(c, vol, ports);
        var chuteNames = string.Join(", ", ports[..pn].ToArray().Where(p => p.Type == RouteType.Chute).Select(p => p.Name));
        Want(chuteNames == "Chute in, Chute out", $"the linking tool's picker lists both ports ({chuteNames})");

        // ---- The box: what the player sized, by the numbers, whatever the hull around it.
        var st = w.StoreOf(c, vol)!;
        var inner = new Vector3(4, 2, 3);
        CbBox(c, vol, inner, new Vector3(4, 1.5f, -3));
        Want(ConstructStorage.Capacity(c, vol) == 9600, $"a box holds 400 a cubic metre ({ConstructStorage.Capacity(c, vol)})");
        CbBox(c, vol, new Vector3(10, 10, 10), new Vector3(4, 1.5f, -3));
        Want(ConstructStorage.Capacity(c, vol) == 400000, $"a box past the hull counts in full ({ConstructStorage.Capacity(c, vol)})");
        CbBox(c, vol, new Vector3(2, 2, 2), new Vector3(30, 30, 30));
        Want(ConstructStorage.Capacity(c, vol) == 3200, $"a box wholly outside counts in full ({ConstructStorage.Capacity(c, vol)})");
        ConstructStoreVolume.Usable(c, vol, out var bmin, out var bmax);
        Want(bmax - bmin == new Vector3(2, 2, 2), $"the usable box is the sized box ({bmax - bmin})");
        CbBox(c, vol, new Vector3(40, 40, 40), new Vector3(4, 1.5f, -3));
        Want(ConstructStorage.Capacity(c, vol) == ConstructStoreVolume.MaxBulk, $"the biggest box stops at {ConstructStoreVolume.MaxBulk} ({ConstructStorage.Capacity(c, vol)})");
        Want(ConstructParts.Need("Storage Volume").Config.All(e => e.Id is not (CfgKey.SizeX or CfgKey.SizeY or CfgKey.SizeZ) || e.Max == ConstructStoreVolume.MaxSide), "the file's sides stop at the code's limit");
        ConstructCargo.DragFace(new Vector3(2), new Vector3(0, 1, 0), 0, new Vector3(100, 0, 5), Vector3.Normalize(new Vector3(0, 0, -1)), out var dragged, out _, ConstructCargo.SideLimit(def));
        Want(dragged.X == ConstructStoreVolume.MaxSide, $"a dragged face stops at {ConstructStoreVolume.MaxSide} m ({dragged.X})");
        CbBox(c, vol, new Vector3(9, 4, 7), new Vector3(4, 1.5f, -3));

        // ---- Bulk through a real chute, from a real bin and back out by the named ports.
        var binIn = w.AddPart(c, binDef, new Vector3(2, 0, 0), 0);
        var binOut = w.AddPart(c, binDef, new Vector3(4, 0, 0), 0);
        c.Rebuild();
        var inWrong = ConstructRoutes.Check(w, c, RouteType.Chute, vol, 255, 255, 0, default, default, binOut, 255, true);
        Want(inWrong != null && inWrong.Contains("no such chute output"), $"a chute can't start at Chute in (\"{inWrong}\")");
        var outWrong = ConstructRoutes.Check(w, c, RouteType.Chute, binIn, 255, 255, 0, default, default, vol, outByte, true);
        Want(outWrong != null && outWrong.Contains("no such chute input"), $"a chute can't end at Chute out (\"{outWrong}\")");
        var chIn = CbChute(w, c, binIn, 255, vol, 255);
        var chOut = CbChute(w, c, vol, outByte, binOut, 255);
        Want(chIn >= 0 && chOut >= 0, $"the chutes went on ({chIn}, {chOut})");
        var inPoint = ConstructRoutes.PortPoint(c, vol, 255, RouteType.Chute);
        var outPoint = ConstructRoutes.PortPoint(c, vol, outByte, RouteType.Chute);
        Want(Vector3.Distance(inPoint, outPoint) > 0.2f, $"each chute meets its own port ({inPoint}, {outPoint})");
        var bin1 = w.StoreOf(c, binIn)!;
        var bin2 = w.StoreOf(c, binOut)!;
        w.Deposit(c, binIn, 60, ore);
        Steps(600);
        Line($"custom bins: chute in and out: bin {bin1.Amount}, volume {st.Amount}, out bin {bin2.Amount}");
        Want(bin1.Amount == 0 && st.Amount + bin2.Amount == 60 && bin2.Amount > 0 && (st.Amount == 0 || st.Material == ore) && bin2.Material == ore, $"60 ore went through ({bin1.Amount}, {st.Amount}, {bin2.Amount})");
        // Refusals: one material; bulk or parts.
        w.Withdraw(c, binOut, 1000);
        w.Withdraw(c, vol, 1000);
        Want(w.Deposit(c, vol, 10, metal) == 10 && w.Deposit(c, vol, 10, ore) == 0, "one material at a time");
        var hb = ConstructStoreVolume.Refusal(c, vol, st, lamp);
        Want(hb != null && hb.Contains("bulk or parts"), $"a part refused while it holds bulk (\"{hb}\")");
        w.Withdraw(c, vol, 1000);

        // ---- A gathering module's bin feeds it, and it feeds a production machine: the same chutes, the bins' own rules.
        var mw = new ConstructWorld();
        var mc = CbHull(mw, new Vector3(0, 20, 0), new Vector3(8, 3, 6), out var mv)!;
        var drill = mw.AddPart(mc, ConstructParts.Need("Drill Module"), new Vector3(2, 0, 0), 0);
        var smelter = mw.AddPart(mc, ConstructParts.Need("Smelter"), new Vector3(4, 0, 0), 0);
        mc.Rebuild();
        CbChute(mw, mc, drill, 255, mv, 255);
        CbChute(mw, mc, mv, outByte, smelter, 255);
        mw.Deposit(mc, drill, 40, ore);
        var mq = new DriveWorld();
        for (var t = 0; t < 600; t++)
            mw.Step(mq, World.TickDt);
        var ms = mw.StoreOf(mc, smelter)!;
        Line($"custom bins: drill bin {mw.StoreOf(mc, drill)!.Amount}, volume {mw.StoreOf(mc, mv)!.Amount}, smelter in {ms.InAmount}");
        Want(mw.StoreOf(mc, drill)!.Amount == 0 && ms.InAmount > 0 && ms.InMaterial == ore, $"a drill's chute feeds it and it feeds a smelter ({mw.StoreOf(mc, drill)!.Amount}, {ms.InAmount})");

        // ---- Parts through chutes: a shelf into the volume, out to another shelf; refused ones stay.
        c.Rebuild();
        var shelfIn = w.AddPart(c, shelfDef, new Vector3(0, 0, -2), 0);
        var shelfOut = w.AddPart(c, shelfDef, new Vector3(2, 0, -2), 0);
        c.Rebuild();
        w.StoreOf(c, binIn)!.Reset(0);
        ConstructRoutes.Remove(w, c, chIn);
        ConstructRoutes.Remove(w, c, CbFind(c, vol, binOut));
        c.Dirty = true;
        c.Rebuild();
        var slot = -1;
        for (var k = 0; k < shelfDef.Slots.Length && slot < 0; k++)
            if (ConstructStorage.Check(shelfDef, w.StoreOf(c, shelfIn)!, k, lamp, out _) == SlotCheck.Ok && shelfDef.Slots[k].Stack > 1)
                slot = k;
        for (var n = 0; n < 4; n++)
            Want(w.SetIn(c, shelfIn, slot, lamp) == null, "a lamp on the shelf");
        CbChute(w, c, shelfIn, 255, vol, 255);
        Steps(300);
        Want(st.ItemCount == 4 && st.KindN == 1 && st.Amount == 0 && w.StoreOf(c, shelfIn)!.Items == 0, $"parts came in by a chute ({st.ItemCount} parts, shelf {w.StoreOf(c, shelfIn)!.Items})");
        var parts0 = st.ItemKg;
        CbChute(w, c, vol, outByte, shelfOut, 255);
        ConstructRoutes.Remove(w, c, CbFind(c, shelfIn, vol));
        c.Dirty = true;
        c.Rebuild();
        Steps(300);
        Want(st.ItemCount == 0 && w.StoreOf(c, shelfOut)!.Items == 4 && st.ItemKg == 0, $"parts went out by a chute ({st.ItemCount}, shelf {w.StoreOf(c, shelfOut)!.Items})");
        Want(parts0 > 0, "parts weigh");

        // ---- The room for parts, kinds, what never goes in.
        var e = new ConstructEdits { Inventory = new SandboxInventories(), InfiniteParts = false };
        e.Inventory.SetInfinite(false);
        e.ResetPacks();
        const byte me = 5;
        var room = ConstructStoreVolume.ItemRoom(c.LocalMin, c.LocalMax);
        var phys = w.PutPart(c, vol, engine);
        Want(phys != null && phys.Contains("physics-only"), $"a physics-only part is refused (\"{phys}\")");
        var box = w.PutPart(c, vol, ConstructParts.Need("Chest"));
        Want(box != null && box.Contains("physics-only") || box != null && box.Contains("storage"), $"storage doesn't go in storage (\"{box}\")");
        var items = ConstructParts.All.Where(d => !d.PhysicsOnly && d.Role != PartRole.Storage).ToList();
        var big = CbHull(w, new Vector3(50, 20, 0), new Vector3(30, 30, 30), out var bigVol)!;
        var bs = w.StoreOf(big, bigVol)!;
        var kinds = 0;
        foreach (var d in items)
            if (kinds < CStore.MaxKinds && w.PutPart(big, bigVol, d) == null)
                kinds++;
        Line($"custom bins: {items.Count} item kinds exist; {bs.KindN} went in ({bs.ItemSpace:0.0} of {ConstructStoreVolume.ItemRoom(big.LocalMin, big.LocalMax):0} m3, room {room:0})");
        Want(bs.KindN == Math.Min(items.Count, CStore.MaxKinds), $"more kinds than a chest's 12 ({bs.KindN})");
        Want(ConstructStoreVolume.SpaceOf(lamp) >= 0.001f, "a part takes room");
        // Hand-filled past the limit: a kind count and the room.
        if (items.Count >= CStore.MaxKinds + 1)
            Want(w.PutPart(big, bigVol, items[^1])?.Contains("kinds") == true, "kind 257 is refused");
        bs.Reset(0);
        for (var n = 0; n < 400; n++)
            Want(w.PutPart(big, bigVol, lamp) == null, "400 lamps in a big box");
        var tiny = CbHull(w, new Vector3(100, 20, 0), new Vector3(0.6f, 0.6f, 0.6f), out var tinyVol)!;
        var ts = w.StoreOf(tiny, tinyVol)!;
        var filled = 0;
        string? fullWhy = null;
        for (var n = 0; n < 20000 && fullWhy == null; n++)
        {
            fullWhy = w.PutPart(tiny, tinyVol, lamp);
            if (fullWhy == null)
                filled++;
        }
        Want(fullWhy != null && fullWhy.Contains("full") && MathF.Abs(ts.ItemSpace - ConstructStoreVolume.ItemRoom(tiny.LocalMin, tiny.LocalMax)) <= ConstructStoreVolume.SpaceOf(lamp) + 0.001f, $"the room for parts ends ({filled} lamps, {ts.ItemSpace}, \"{fullWhy}\")");
        ts.Reset(0);

        // ---- The read API over every kind of store.
        Span<CHolding> holds = new CHolding[CStore.MaxKinds + 1];
        bs.Reset(0);
        for (var n = 0; n < 3; n++)
            w.PutPart(big, bigVol, lamp);
        var hn = w.Holdings(big, bigVol, holds);
        Want(hn == 1 && holds[0] == new CHolding(true, lamp.Id, 3), $"Holdings reads a volume's parts ({hn})");
        Want(w.TakeHolding(big, bigVol, new CHolding(true, lamp.Id, 0), 2) == 2 && bs.ItemCount == 1, "TakeHolding takes parts");
        Want(w.TakeHolding(big, bigVol, new CHolding(true, lamp.Id, 0), 9) == 1 && bs.KindN == 0, "TakeHolding stops at what is there");
        w.Deposit(big, bigVol, 70000, ore);
        hn = w.Holdings(big, bigVol, holds);
        Want(hn == 1 && holds[0] == new CHolding(false, ore, 70000), $"Holdings reads bulk ({hn})");
        Want(w.TakeHolding(big, bigVol, new CHolding(false, metal, 0), 5) == 0 && w.TakeHolding(big, bigVol, new CHolding(false, ore, 0), 5) == 5, "TakeHolding takes the right material only");
        bs.Reset(0);
        var shelfStore = w.StoreOf(c, shelfOut)!;
        hn = w.Holdings(c, shelfOut, holds);
        Want(hn == 1 && holds[0] == new CHolding(true, lamp.Id, 4), $"Holdings adds a shelf's pile ({hn}, {(hn > 0 ? holds[0] : default)})");
        Want(w.TakeHolding(c, shelfOut, new CHolding(true, lamp.Id, 0), 3) == 3 && shelfStore.Items == 1, "TakeHolding takes from slots");
        w.TakeHolding(c, shelfOut, new CHolding(true, lamp.Id, 0), 3);

        // ---- Mass and centre of mass: the same cheap path, a driven buggy.
        if (DriveBlueprint("Buggy") is { } buggy && DriveSpawn(w, buggy, 200, 200, MathF.PI / 2, "custom bin buggy") is { } car && DriveSpawn(w, buggy, 200, 230, MathF.PI / 2, "custom bin twin") is { } twin)
        {
            car.Owner = twin.Owner = 255;
            var cv = ModFit(w, car, def.Name, "-y", "custom bins");
            var tv = ModFit(w, twin, def.Name, "-y", "custom bins");
            if (cv < 0 || tv < 0)
                return;
            CbBox(car, cv, new Vector3(1.2f, 0.4f, 1.2f), new Vector3(0, 0.2f, 0));
            CbBox(twin, tv, new Vector3(1.2f, 0.4f, 1.2f), new Vector3(0, 0.2f, 0));
            Steps(120);
            var (mass0, com0, inv0) = (car.Mass, car.Com, car.InvInertiaLocal);
            var cs = w.StoreOf(car, cv)!;
            var capCar = ConstructStorage.Capacity(car, cv);
            var put = w.Deposit(car, cv, capCar, ore);
            Load();
            var kg = put * table.WeightOf(ore);
            ConstructStorage.FillBox(car, cv, cs, out var fill, out _);
            var expect = (com0 * mass0 + fill * kg) / (mass0 + kg);
            Line($"custom bins: buggy {mass0:0} kg, {put} ore = {kg:0} kg at {fill}: {car.Mass:0} kg, com {car.Com} (want {expect})");
            Want(put > 0 && MathF.Abs(car.Mass - mass0 - kg) < 0.1f && car.LoadMass == kg && !car.Dirty && car.InvInertiaLocal != inv0, $"the load's mass ({car.Mass}, want {mass0 + kg})");
            Want(Vector3.Distance(car.Com, expect) < 0.003f, $"the centre of mass sits at the fill ({car.Com}, want {expect})");
            var (ma, ca) = (car.Mass, car.Com);
            car.Dirty = true;
            car.Rebuild();
            Want(ma == car.Mass && ca == car.Com, "RefreshMass and Rebuild differ");
            Steps(240);
            var start = car.WorldCom;
            var startT = twin.WorldCom;
            Seat(car);
            Seat(twin);
            for (var t = 0; t < 240; t++)
            {
                DriveKeys(car, Buttons.Forward);
                DriveKeys(twin, Buttons.Forward);
                w.Step(q, World.TickDt);
            }
            var d1 = Vector3.Distance(start with { Y = 0 }, car.WorldCom with { Y = 0 });
            var d0 = Vector3.Distance(startT with { Y = 0 }, twin.WorldCom with { Y = 0 });
            Line($"custom bins: 4 s at full throttle: empty {d0:0.0} m, loaded {d1:0.0} m");
            Want(d1 < d0 * 0.9f, $"a loaded buggy doesn't slow down ({d1:0.0} m against {d0:0.0} m)");
            car.Driver = twin.Driver = 255;
            car.DriverSeat = twin.DriverSeat = -1;

            // ---- Huge: the cap, a float's reach, a crushing load that stays finite and a unit's change that still shows.
            var hugeCap = CbHull(w, new Vector3(300, 40, 0), new Vector3(39, 39, 39), out var hv, anchored: false)!;
            hugeCap.Frozen = true;
            Want(ConstructStorage.Capacity(hugeCap, hv) == ConstructStoreVolume.MaxBulk, $"a 40 m box stops at the cap ({ConstructStorage.Capacity(hugeCap, hv)})");
            var hs = w.StoreOf(hugeCap, hv)!;
            Want(w.Deposit(hugeCap, hv, int.MaxValue, gold) == ConstructStoreVolume.MaxBulk && hs.Amount == ConstructStoreVolume.MaxBulk, $"a full huge box ({hs.Amount})");
            Want(w.Deposit(hugeCap, hv, 5, gold) == 0, "nothing more goes in");
            Load();
            var hkg = ConstructStoreVolume.MaxBulk * table.WeightOf(gold);
            Want(MathF.Abs(hugeCap.LoadMass - hkg) < 1 && float.IsFinite(hugeCap.InvInertiaLocal.M11) && hugeCap.InvInertiaLocal.M11 > 0 && hugeCap.InvMass > 0, $"the huge load's numbers ({hugeCap.LoadMass}, {hugeCap.InvInertiaLocal.M11})");
            var r0 = w.LoadRefreshes;
            w.Withdraw(hugeCap, hv, 1);
            Load();
            Want(w.LoadRefreshes > r0, "a unit out of a huge load doesn't show");
            hs.Reset(0);
            // On wheels: 80,000 t on a buggy, its box stretched along the hull. The wheels give, nothing goes to NaN.
            CbBox(car, cv, new Vector3(1.2f, 0.4f, 1.2f), new Vector3(0, 0.2f, 0));
            cs.Amount = 0;
            cs.Material = gold;
            cs.Amount = ConstructStoreVolume.MaxBulk;
            w.LoadChanged = true;
            Steps(240);
            var ok = float.IsFinite(car.Pos.X + car.Pos.Y + car.Pos.Z + car.Rot.X + car.Vel.Length() + car.AngVel.Length()) && car.Pos.Y > -10 && car.Vel.Length() < 40;
            Line($"custom bins: {cs.Amount} gold ore over-full on a buggy ({car.Mass:0} kg): at {car.Pos}, {car.Vel.Length():0.00} m/s");
            Want(ok, $"a crushing load blew it up (at {car.Pos}, {car.Vel.Length()} m/s, {car.AngVel.Length()} rad/s)");
            Want(ConstructStorage.FillBox(car, cv, cs, out _, out var overHalf) && overHalf.Length() > 0, "an over-full fill stays at the top");
            var over = w.Deposit(car, cv, 10, gold);
            var overLook = ConstructStoreVolume.Look(car, cv, cs, true, new SandboxInventory(), null);
            Want(over == 0 && overLook.Contains("OVER-FULL"), $"over-full refuses more and says so ({over}, \"{overLook}\")");
            cs.Reset(0);
            w.LoadChanged = true;
            Load();
        }

        // ---- A resize that would lose what it holds, or overlap, is refused; a split; taking it off; a spill.
        var sw = new ConstructWorld();
        var chain = sw.Create(new Vector3(0, 30, 0), Quaternion.Identity)!;
        var frame = ConstructParts.Need("Frame Block");
        var i0 = sw.AddPart(chain, frame, Vector3.Zero, 0);
        chain.Rebuild();
        var i1 = CbFitOn(sw, chain, i0, frame, "custom bins");
        var i2 = CbFitOn(sw, chain, i1, frame, "custom bins");
        var sv = CbFitOn(sw, chain, i2, def, "custom bins");
        if (i1 < 0 || i2 < 0 || sv < 0)
            return;
        var ss = sw.StoreOf(chain, sv)!;
        CbBox(chain, sv, new Vector3(6, 6, 6), Vector3.Zero);
        var capChain = ConstructStorage.Capacity(chain, sv);
        sw.Deposit(chain, sv, capChain, ore);
        chain.Rebuild();
        var chainTag = chain.Parts[sv].Tag;
        sw.RemovePart(chain, i1);
        var piece = sw.Bodies.First(b => Enumerable.Range(0, b.PartCount).Any(k => b.Parts[k].Tag == chainTag));
        var pv = Enumerable.Range(0, piece.PartCount).First(k => piece.Parts[k].Tag == chainTag);
        piece.Rebuild();
        var ps = sw.StoreOf(piece, pv);
        var capPiece = ps == null ? -1 : ConstructStorage.Capacity(piece, pv);
        Line($"custom bins: a split: {capChain} units on the chain, {capPiece} on the piece that kept it, {ps?.Amount} held, {piece.LoadMass:0} kg");
        Want(ps == ss && ss.Amount == capChain && piece.LoadMass == capChain * table.WeightOf(ore) && capPiece == capChain, $"a split keeps the load and the box ({ss.Amount}, {capPiece}, {piece.LoadMass})");
        var pSt = new ConstructEdits { Inventory = new SandboxInventories(), InfiniteParts = false };
        ref var ph = ref pSt.Hand(me);
        ph.Tool = ConstructTool.Wrench;
        piece.Owner = me;
        var pEye = ConstructRender.PartCentre(piece, pv) + new Vector3(0, 0.5f, 0.5f);
        var off = pSt.BeginRemove(sw, me, pEye, piece.Id, pv, 0);
        Want(off != null && off.Contains("empty the"), $"the wrench refuses a full volume (\"{off}\")");
        // A spill: a bundle's worth to the cap, the rest lost.
        var bodies0 = sw.Bodies.Count;
        sw.RemovePart(piece, pv);
        var bundles = sw.Bodies.Count(b => b.Wreck && b.PartCount == 1 && b.DefOf(0).Name == ConstructStorage.MaterialBundle && b.Parts[0].Setting == ore);
        Line($"custom bins: spilled {bundles} bundles of {capChain} units ({sw.Bodies.Count - bodies0} bodies more)");
        Want(bundles == Math.Min(ConstructStoreVolume.MaxSpill, (capChain + 99) / 100), $"the spill's bundles ({bundles})");
        Want(sw.LiveStores == 0 || sw.StoreOf(piece, 0) == null, "the store went with the part");
        var pw = new ConstructWorld();
        var pc = CbHull(pw, new Vector3(0, 40, 0), new Vector3(3, 3, 3), out var pcv)!;
        var pcs = pw.StoreOf(pc, pcv)!;
        for (var n = 0; n < 40; n++)
            pw.PutPart(pc, pcv, lamp);
        Want(pcs.ItemCount == 40, $"40 lamps in ({pcs.ItemCount})");
        pw.RemovePart(pc, pcv);
        var spilledLamps = pw.Bodies.Count(b => b.Loose && b.DefOf(0) == lamp);
        Want(spilledLamps == ConstructStoreVolume.MaxSpill, $"parts spill to the cap ({spilledLamps})");

        // ---- Refused resizes through the server's own Configure.
        var cw = new ConstructWorld();
        var ce = new ConstructEdits { Inventory = new SandboxInventories(), InfiniteParts = false };
        ref var ch = ref ce.Hand(me);
        ch.Tool = ConstructTool.Configure;
        var hull = CbHull(cw, new Vector3(0, 60, 0), new Vector3(8, 3, 6), out var hv2)!;
        hull.Owner = me;
        var hs2 = cw.StoreOf(hull, hv2)!;
        cw.Deposit(hull, hv2, 5000, ore);
        ReadOnlySpan<byte> sizeOnly = [(byte)def.CfgSlot[(int)CfgKey.SizeX]];
        ushort[] one = [(ushort)hv2];
        var eye2 = ConstructStoreVolume.NearestPoint(hull, hv2, hull.WorldCom + new Vector3(0, 4, 0)) + new Vector3(0, 1, 0);
        var shrink = ce.Configure(cw, me, eye2, hull.Id, one, sizeOnly, [0.2f], null);
        Want(shrink != null && shrink.Contains("Empty it first") && ConstructStorage.Capacity(hull, hv2) >= 5000, $"a shrink that loses the load is refused and undone (\"{shrink}\", {ConstructStorage.Capacity(hull, hv2)})");
        hs2.Reset(0);
        var farEye = ConstructStoreVolume.NearestPoint(hull, hv2, hull.ToWorld(new Vector3(8, 1, 0))) + new Vector3(0.1f, 0.1f, 0.1f);
        var grown = ce.Configure(cw, me, farEye, hull.Id, one, sizeOnly, [5f], null);
        Want(grown == null && hull.Dirty, $"a box is set from wherever its face is, far from its part (\"{grown}\", part {Vector3.Distance(farEye, ConstructRender.PartCentre(hull, hv2)):0.0} m off)");
        hull.Rebuild();
        var second = cw.AddPart(hull, def, new Vector3(4, 0, 0), 0);
        hull.Rebuild();
        CbBox(hull, second, new Vector3(1, 1, 1), new Vector3(0, 2, 0));
        CbBox(hull, hv2, new Vector3(2, 3, 2), new Vector3(0, 1, -6));
        ushort[] two = [(ushort)second];
        ReadOnlySpan<byte> atAll = [(byte)def.CfgSlot[(int)CfgKey.SizeX], (byte)def.CfgSlot[(int)CfgKey.SizeZ], (byte)def.CfgSlot[(int)CfgKey.AtX], (byte)def.CfgSlot[(int)CfgKey.AtZ]];
        var eye3 = ConstructRender.PartCentre(hull, second) + new Vector3(0, 1, 0);
        var into = ce.Configure(cw, me, eye3, hull.Id, two, atAll, [8f, 6f, -4f, 3f], null);
        Want(into != null && into.Contains("runs into"), $"a box can't grow into another (\"{into}\")");

        // A construct takes four.
        var fw = new ConstructWorld();
        var f = fw.Create(new Vector3(0, 80, 0), Quaternion.Identity)!;
        fw.AddPart(f, ConstructParts.Need("Foundation"), Vector3.Zero, 0);
        f.Rebuild();
        var fitted = 0;
        for (var n = 0; n < ConstructStoreVolume.MaxPerConstruct + 2; n++)
        {
            var ok = false;
            for (var s = 0; s < f.DefOf(0).Sockets.Length && !ok; s++)
                if (ConstructBuild.Place(f, 0, s, def, 2, 0, out var g) == null)
                {
                    ConstructBuild.Attach(fw, f, def, g);
                    f.Rebuild();
                    ok = true;
                }
            fitted += ok ? 1 : 0;
        }
        Want(fitted == ConstructStoreVolume.MaxPerConstruct, $"a construct takes {ConstructStoreVolume.MaxPerConstruct} ({fitted})");

        // ---- Blueprints: the box and the ports round trip.
        var bay = ConstructBuild.FindBlueprint("Storage Bay");
        var bw1 = new ConstructWorld();
        var built = bay == null ? null : ConstructBuild.BuildOnGround(bw1, bay, new Vector3(0, 0, 0), 0, out _);
        if (built == null)
            problems.Add("the Storage Bay blueprint doesn't build");
        else
        {
            var v = Enumerable.Range(0, built.PartCount).First(k => built.DefOf(k).IsVolumeStore);
            var links = ConstructRoutes.LinksJson(built);
            var cfg = ConstructConfig.ToJson(built.Parts[v], def);
            Line($"custom bins: blueprint config {cfg}, links {links}");
            Want(links.Contains("Chute out"), $"a chute's out port is written back ({links})");
            var copy = ConstructBuild.ParseBlueprint($$"""
                { "format": "call-of-slop-construct-blueprint/1", "name": "Copy", "steps": [
                    { "part": "Foundation" },
                    { "part": "Storage Volume", "on": 0, "socket": "top11", "by": "-y", "config": {{cfg}} },
                    { "part": "Materials Bin", "on": 0, "socket": "top03", "by": "bottom" },
                    { "part": "Materials Bin", "on": 0, "socket": "top30", "by": "bottom" } ],
                  "links": {{links}} }
                """, "copy.json");
            var bw2 = new ConstructWorld();
            var again = ConstructBuild.BuildOnGround(bw2, copy, new Vector3(40, 0, 0), 0, out var why2);
            ConstructCargo.Settings(built.Parts[v], def, out var s1, out var a1);
            if (again == null)
                problems.Add($"the copy doesn't build (\"{why2}\")");
            else
            {
                ConstructCargo.Settings(again.Parts[v], def, out var s2, out var a2);
                Want(s1 == s2 && a1 == a2 && ConstructRoutes.LinksJson(again) == links && ConstructStorage.Capacity(again, v) == ConstructStorage.Capacity(built, v), $"a blueprint round trip keeps the box and the ports ({s1}/{s2}, {a1}/{a2})");
            }
            // A chute named nowhere takes the unit's own port; a wrong name is refused by the existing path.
            bw1.Deposit(built, 2, 30, metal);
            var v2 = bw1.StoreOf(built, 3)!;
            var dq = new DriveWorld();
            for (var t = 0; t < 600; t++)
                bw1.Step(dq, World.TickDt);
            Want(v2.Amount > 0 && v2.Material == metal, $"the Storage Bay moves material through ({v2.Amount})");
        }

        // ---- The wire.
        var a0 = new CStore();
        a0.Reset(0);
        a0.Amount = 9_876_543;
        a0.Material = ore;
        var writer = new PacketWriter(16384);
        var b0 = new CStore();
        writer.Begin((Msg)0);
        ConstructStorage.Write(writer, a0);
        var r = new PacketReader(writer.Span);
        r.Take(1);
        ConstructStorage.Read(ref r, b0);
        Want(b0.Amount == a0.Amount && b0.Material == ore, $"a huge amount survives the wire ({b0.Amount})");
        var small = new PacketWriter();
        small.Begin((Msg)0);
        var c0 = new CStore();
        c0.Reset(0);
        c0.Amount = 600;
        c0.Material = metal;
        ConstructStorage.Write(small, c0);
        Want(small.Span.Length == 1 + 1 + 4, $"a small bin costs what it did ({small.Span.Length} bytes)");
        var d0k = new CStore();
        d0k.Reset(0);
        var ids = ConstructParts.All.Where(d => !d.PhysicsOnly).Take(CStore.MaxKinds).ToList();
        foreach (var d in ids)
            d0k.AddKind(d.Id, 7);
        d0k.AddKind(ids[0].Id, CStore.MaxPerKind - 7);
        var wk = new PacketWriter(16384);
        wk.Begin((Msg)0);
        ConstructStorage.Write(wk, d0k);
        var rk = new PacketReader(wk.Span);
        rk.Take(1);
        var e0k = new CStore();
        ConstructStorage.Read(ref rk, e0k);
        Line($"custom bins: {d0k.KindN} kinds: {wk.Span.Length} bytes");
        Want(e0k.KindN == d0k.KindN && e0k.ItemCount == d0k.ItemCount && e0k.KindCount![0] == CStore.MaxPerKind && e0k.KindDef![d0k.KindN - 1] == d0k.KindDef![d0k.KindN - 1] && MathF.Abs(e0k.ItemSpace - d0k.ItemSpace) < 0.001f, $"the kinds survive the wire ({e0k.KindN}, {e0k.ItemCount})");
        var bad = new PacketWriter();
        bad.Begin((Msg)0);
        bad.U8(0).U16(0).U8(0).U8(16).U16(1).U16((ushort)ConstructParts.All.Count).U16(3);
        var rb = new PacketReader(bad.Span);
        rb.Take(1);
        var refused = false;
        try
        {
            ConstructStorage.Read(ref rb, new CStore());
        }
        catch (MalformedPacketException)
        {
            refused = true;
        }
        Want(refused, "an unknown part kind on the wire is refused");

        // ---- No allocation: chutes moving parts and bulk, the load step, looks at a loaded volume.
        var aw = new ConstructWorld();
        var ah = CbHull(aw, new Vector3(0, 90, 0), new Vector3(8, 3, 6), out var av)!;
        var aBin = aw.AddPart(ah, binDef, new Vector3(2, 0, 0), 0);
        ah.Rebuild();
        CbChute(aw, ah, aBin, 255, av, 255);
        aw.Deposit(ah, aBin, 600, ore);
        var aq = new DriveWorld();
        for (var t = 0; t < 180; t++)
            aw.Step(aq, World.TickDt);
        var ast = aw.StoreOf(ah, av)!;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 0; t < 240; t++)
        {
            aw.Step(aq, World.TickDt);
            ConstructStorage.FillBox(ah, av, ast, out _, out _);
            ConstructStorage.Capacity(ah, av);
            ConstructStoreVolume.Check(ah, av, ast, lamp);
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Line($"custom bins: {bytes} B allocated over 240 ticks, {ast.Amount} units moved");
        Want(bytes < 2048 && ast.Amount > 30, $"the steady path allocates ({bytes} B, {ast.Amount} moved)");

        if (problems.Count > 0)
            Fail($"custom bins: {problems.Count} problem(s): {string.Join("; ", problems.Take(8))}");
        CustomBinInput();
    }

    /// <summary>The real input offline: the configure tool's handle drag, R's drawing, F at the control box and at a far fill, a click to put a part in, each through the game's own frame.</summary>
    private static void CustomBinInput()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail($"custom bins: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        var keep = sb.InfiniteParts;
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        if (SpawnLocalSafely(g, ref now) is { } notReady)
        {
            Fail($"custom bins: {notReady}");
            return;
        }
        var me = g.Local!;
        sb.InfiniteParts = false;
        Program.SweepRelease(g);
        var trips0 = Program.SandboxGuardTrips;
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        void Frame(ReadOnlySpan<GameAction> keys)
        {
            Program.SweepFrame(g, now, keys);
            now += 1.0 / 60;
            g.Update(now, 1.0 / 60);
        }
        ReadOnlySpan<GameAction> lmb = [GameAction.Fire];
        ReadOnlySpan<GameAction> rKey = [GameAction.Reload];
        ReadOnlySpan<GameAction> fKey = [GameAction.Interact];
        try
        {
            var w = g.Constructs;
            if (OpenSpot(w) is not { } spot)
            {
                Fail("custom bins: no open ground");
                return;
            }
            var size = new Vector3(8, 3, 6);
            var ground = ConstructWorld.Ground(World.Query, spot.X, spot.Z);
            if (CbHull(w, ground + new Vector3(-4, 0.4f, -3), size, out var vol) is not { } c)
            {
                Fail("custom bins: no rig");
                return;
            }
            c.Owner = me.Id;
            var def = c.DefOf(vol);
            CbBox(c, vol, new Vector3(2, 2, 2), new Vector3(2, 1, -3));
            var st = w.StoreOf(c, vol)!;
            var table = RecyclingTable.Current;
            var ore = table.IdOf("iron ore");
            me.ImmuneUntil = 1e9;

            // The configure tool: drag the +x face out by a metre (from where it is, 8 m reach).
            g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Configure);
            Frame([]);
            Frame([]);
            ConstructCargo.Settings(c.Parts[vol], def, out var size0, out var at0);
            var face = ConstructCargo.FromPart(c, vol, ConstructCargo.FaceAt(size0, at0, 0));
            StandBy(me, face, 3f, face - new Vector3(0, 0, 1));
            Frame([]);
            SculptLook(g, me, face);
            Frame([]);
            Want(g.SandboxLookText.Contains("drag its x+ face"), $"the handle's look line (\"{g.SandboxLookText}\")");
            Frame(lmb);
            Frame(lmb);
            SculptLook(g, me, face + Vector3.Transform(Vector3.UnitX, c.Rot) * 1f);
            Frame(lmb);
            Want(g.SandboxLookText.Contains("holds") && g.SandboxLookText.Contains("let go"), $"the drag's look line says what it would hold (\"{g.SandboxLookText}\")");
            Frame([]);
            Frame([]);
            ConstructCargo.Settings(c.Parts[vol], def, out var size1, out var at1);
            Line($"custom bins: dragged x+ {size0.X} to {size1.X} m (centre {at0.X} to {at1.X})");
            Want(MathF.Abs(size1.X - 3f) < 0.21f && MathF.Abs(at1.X - at0.X - (size1.X - size0.X) * 0.5f) < 0.02f, $"a face handle moved the box ({size1}, {at1})");

            // R on the part, then drag a new box on the construct: corner to corner.
            var ctrl = ConstructRender.PartCentre(c, vol);
            StandBy(me, ctrl, 2f, ctrl - new Vector3(0.3f, 0, 1));
            Frame([]);
            SculptLook(g, me, ctrl);
            Frame([]);
            Frame(rKey);
            Frame([]);
            var cornerA = ctrl;
            SculptLook(g, me, cornerA);
            Frame([]);
            Want(g.SandboxLookText.Contains("first corner"), $"drawing's look line (\"{g.SandboxLookText}\")");
            Frame(lmb);
            Frame(lmb);
            SculptLook(g, me, ctrl + new Vector3(1.5f, -0.1f, 1f));
            Frame(lmb);
            Frame([]);
            Frame([]);
            ConstructCargo.Settings(c.Parts[vol], def, out var size2, out _);
            Line($"custom bins: drawn box {size2}");
            Want(size2.X != size1.X || size2.Z != size1.Z, $"R and a drag drew a new box ({size1} then {size2})");
            CbBox(c, vol, new Vector3(9, 4, 7), new Vector3(4, 1.5f, -3));

            // F tips in what you carry, the look line counts it; at a far fill F takes a load out.
            g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
            Frame([]);
            var inv = g.SandboxInventory(me.Id);
            inv.AddMaterial(ore, 1200);
            StandBy(me, ctrl, 1.6f, ctrl - new Vector3(0.3f, 0, 1));
            Frame([]);
            SculptLook(g, me, ctrl);
            Frame([]);
            Want(g.SandboxLookText.Contains("tip in your 1200"), $"the empty look line (\"{g.SandboxLookText}\")");
            Frame(fKey);
            Frame([]);
            Frame([]);
            var cap = ConstructStorage.Capacity(c, vol);
            Want(st.Amount == 1200 && inv.Materials[ore] == 0, $"F tipped it in ({st.Amount}, {inv.Materials[ore]} carried)");
            SculptLook(g, me, ctrl);
            Frame([]);
            Want(g.SandboxLookText.Contains($"1200/{cap} iron ore"), $"the look line counts it (\"{g.SandboxLookText}\")");
            // Fill it most of the way, then stand over the far end, looking down at the level.
            w.Deposit(c, vol, cap / 2, ore);
            for (var k = 0; k < 20; k++)
                Frame([]);
            ConstructStorage.FillBox(c, vol, st, out var fc, out var fh);
            c.WorldFrame(0, out var bq, out var bt);
            var farTop = bt + Vector3.Transform(new Vector3(7, fc.Y + fh.Y, 1), bq);
            me.Teleport(farTop + new Vector3(0, 0.2f - World.EyeHeight, 0) + new Vector3(0, 1.2f, 0));
            Frame([]);
            SculptLook(g, me, farTop);
            Frame([]);
            var partFar = Vector3.Distance(me.RenderEye, ConstructRender.PartCentre(c, vol));
            Want(partFar > ConstructBuild.UseReach, $"the test's far end is out of reach of the part ({partFar:0.0} m)");
            Want(g.SandboxLookText.Contains("F: take 100"), $"the fill's look line, far from the part (\"{g.SandboxLookText}\", {partFar:0.0} m)");
            var before = st.Amount;
            Frame(fKey);
            Frame([]);
            Frame([]);
            Want(st.Amount == before - 100 && inv.Materials[ore] == 100, $"F took a load at the far fill ({st.Amount}, {inv.Materials[ore]} carried)");

            // A shot through the fill lands on nothing of it.
            var through = w.Raycast(farTop + new Vector3(0, 0.5f, 0), -Vector3.UnitY, 1f, out var shot);
            Want(!through || !shot.Body.DefOf(shot.Part).IsVolumeStore, "a shot's ray stops at the fill");

            // Parts: a click puts one in, F gives it back.
            st.Reset(0);
            st.Live = true;
            w.LoadChanged = true;
            var lamp = ConstructParts.Need("Lamp");
            ref var h = ref g.SandboxHands.Hand(me.Id);
            h.Held = lamp.Id;
            StandBy(me, ctrl, 1.6f, ctrl - new Vector3(0.3f, 0, 1));
            Frame([]);
            SculptLook(g, me, ctrl);
            Frame([]);
            Want(g.SandboxLookText.Contains("click to put the Lamp in"), $"the held part's look line (\"{g.SandboxLookText}\")");
            Frame(lmb);
            Frame([]);
            Frame([]);
            Want(st.ItemCount == 1 && h.Held == ConstructParts.None, $"a click put the lamp in ({st.ItemCount}, held {h.Held})");
            SculptLook(g, me, ctrl);
            Frame([]);
            Want(g.SandboxLookText.Contains("1 part") && g.SandboxLookText.Contains("F: take a Lamp"), $"parts on the look line (\"{g.SandboxLookText}\")");
            Frame(fKey);
            Frame([]);
            Frame([]);
            Want(st.ItemCount == 0 && h.Held == lamp.Id, $"F gave it back ({st.ItemCount}, held {h.Held})");
            h.Held = ConstructParts.None;

            // The index in hand is checked against the construct when used: the part goes mid-drag.
            g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Configure);
            Frame([]);
            Frame([]);
            ConstructCargo.Settings(c.Parts[vol], def, out var size3, out var at3);
            var f3 = ConstructCargo.FromPart(c, vol, ConstructCargo.FaceAt(size3, at3, 0));
            StandBy(me, f3, 3f, f3 - new Vector3(0, 0, 1));
            Frame([]);
            SculptLook(g, me, f3);
            Frame([]);
            Frame(lmb);
            w.RemovePart(c, vol);
            Frame(lmb);
            Frame([]);
            Frame([]);
            Want(Program.SandboxGuardTrips == trips0, $"the guard tripped {Program.SandboxGuardTrips - trips0} time(s)");
        }
        finally
        {
            sb.InfiniteParts = keep;
            Program.SweepRelease(g);
        }
        if (problems.Count > 0)
            Fail($"custom bins (input): {problems.Count} problem(s): {string.Join("; ", problems.Take(8))}");
    }

    /// <summary>"Custom bins network": a real host, a client and a late joiner: a box resized from the client's tool, a huge amount, parts, the mass, the look line, F over the wire, chutes moving parts.</summary>
    private static void CheckCustomBinsNet()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail("custom bins network: the yard is missing");
            return;
        }
        const ushort port = 26483;
        var trips0 = Program.SandboxGuardTrips;
        var keepInfinite = sb.InfiniteParts;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        sb.InfiniteParts = false;
        yard.RestoreProps();
        World.SetMap(yard);
        try
        {
            using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
            server.Mode = GameMode.Sandbox;
            using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Pump(server, [a], 1.0, ref now, clock);
            for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
                Pump(server, [a], 0.25, ref now, clock);
            var simHost = server.Net.SimulateBadNetwork(40, 8, 2);
            var simClient = a.Net.SimulateBadNetwork(40, 8, 2);
            Pump(server, [a], 0.5, ref now, clock);
            if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp) || a.Local is not { } mine)
            {
                Fail("custom bins network: the client didn't join");
                return;
            }
            if (!sp.Alive)
                server.SelfTestRespawn(sp, now);
            Pump(server, [a], 0.3, ref now, clock);
            Program.SweepRelease(a);
            var w = server.Constructs;
            var ground = OpenSpot(w) is { } s ? ConstructWorld.Ground(World.Query, s.X, s.Z) : new Vector3(10, 0, 10);
            if (CbHull(w, ground + new Vector3(-4, 0.4f, -3), new Vector3(8, 3, 6), out var vol) is not { } c)
            {
                Fail("custom bins network: no rig");
                return;
            }
            c.Owner = sp.Id;
            var table = RecyclingTable.Current;
            var ore = table.IdOf("iron ore");
            var def = c.DefOf(vol);
            CbBox(c, vol, new Vector3(9, 4, 7), new Vector3(4, 1.5f, -3));
            w.StructureDirty.Add(c.Id);
            var st = w.StoreOf(c, vol)!;
            bool Await(Func<bool> done, double seconds, Game[] peers)
            {
                for (var t = 0.0; t < seconds && !done(); t += 0.1)
                    Pump(server, peers, 0.1, ref now, clock);
                return done();
            }
            Construct? Copy(Game g) => g.Constructs.Get(c.Id);
            CStore? Store(Game g) => Copy(g) is { } cc ? g.Constructs.StoreOf(cc, vol) : null;
            if (!Await(() => Copy(a) is { } cc && Store(a) != null && ConstructStorage.Capacity(cc, vol) == ConstructStorage.Capacity(c, vol), 5, [a]))
            {
                Fail($"custom bins network: the client's capacity isn't the server's ({(Copy(a) is { } x ? ConstructStorage.Capacity(x, vol) : -1)}, {ConstructStorage.Capacity(c, vol)})");
                return;
            }
            // A huge amount (past a u16), its mass and fill on the client.
            var huge = 70000;
            w.Deposit(c, vol, huge, ore);
            bool Same(Game g) => Copy(g) is { } cc && Store(g)?.Amount == st.Amount && Store(g)?.Material == st.Material && MathF.Abs(cc.Mass - c.Mass) < 0.5f && Vector3.Distance(cc.Com, c.Com) < 0.002f && !cc.Dirty;
            if (!Await(() => Same(a), 5, [a]))
            {
                Fail($"custom bins network: a huge load didn't reach the client ({Store(a)?.Amount}, {Copy(a)?.Mass} kg; server {st.Amount}, {c.Mass} kg)");
                return;
            }
            Line($"custom bins network: {st.Amount} ore: server {c.Mass:0.0} kg, client {Copy(a)?.Mass:0.0} kg");
            // The client's own tool: a face dragged through its frame, over the transport.
            var ctrlWorld = ConstructRender.PartCentre(c, vol);
            sp.ImmuneUntil = 1e9;
            a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Configure);
            Await(() => server.SandboxHands.Hand(sp.Id).Tool == ConstructTool.Configure && a.SandboxHands.Hand(a.LocalId).Tool == ConstructTool.Configure, 3, [a]);
            ConstructCargo.Settings(c.Parts[vol], def, out var size0, out var at0);
            var face = ConstructCargo.FromPart(c, vol, ConstructCargo.FaceAt(size0, at0, 0));
            var stand = ConstructWorld.Ground(World.Query, face.X - 2f, face.Z + 2f) + new Vector3(0, 0.05f, 0);
            sp.Teleport(stand);
            mine.Teleport(stand);
            Pump(server, [a], 0.5, ref now, clock);
            var lmb = new[] { GameAction.Fire };
            void Hold(GameAction[] key, Func<Vector3> at, double seconds)
            {
                PumpEach(server, [a], seconds, ref now, clock, t2 =>
                {
                    SculptLook(a, a.Local!, at());
                    Program.SweepFrame(a, t2, key);
                });
            }
            Hold([], () => face, 0.2);
            Hold(lmb, () => face, 0.2);
            var pulled = face - Vector3.Transform(Vector3.UnitX, c.Rot) * 0.5f;
            Hold(lmb, () => pulled, 0.2);
            Hold([], () => pulled, 0.2);
            bool Resized() { ConstructCargo.Settings(c.Parts[vol], def, out var now1, out _); return now1.X < size0.X - 0.2f; }
            if (!Await(Resized, 3, [a]))
            {
                ConstructCargo.Settings(c.Parts[vol], def, out var was, out _);
                Fail($"custom bins network: the client's drag didn't reach the server (x {size0.X} then {was.X}; look \"{a.SandboxLookText}\", notice \"{a.SandboxNotice}\")");
                return;
            }
            if (!Await(() => Copy(a) is { } cc && ConstructStorage.Capacity(cc, vol) == ConstructStorage.Capacity(c, vol) && !cc.Dirty, 3, [a]))
                Fail("custom bins network: the resize didn't come back to the client");
            a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
            Pump(server, [a], 0.3, ref now, clock);
            // F over the wire: a load into the client's pockets.
            var stand2 = ConstructWorld.Ground(World.Query, ctrlWorld.X - 1f, ctrlWorld.Z + 1f) + new Vector3(0, 0.05f, 0);
            sp.Teleport(stand2);
            mine.Teleport(stand2);
            Pump(server, [a], 0.5, ref now, clock);
            var f = new[] { GameAction.Interact };
            var before = st.Amount;
            Hold([], () => ctrlWorld, 0.2);
            Hold(f, () => ctrlWorld, 0.1);
            Hold([], () => ctrlWorld, 0.1);
            if (!Await(() => st.Amount == before - ConstructStorage.TakeLoad, 3, [a]))
                Fail($"custom bins network: the client's F took nothing ({st.Amount}, before {before}; look \"{a.SandboxLookText}\", notice \"{a.SandboxNotice}\")");
            if (!Await(() => Same(a), 4, [a]))
                Fail($"custom bins network: the take didn't come back ({Store(a)?.Amount}, {st.Amount})");

            // Parts: many kinds by a real chute out of a shelf, into the volume; the client lists them as the server does.
            st.Reset(0);
            st.Live = true;
            w.StoreDirty.Add(st.Handle);
            w.LoadChanged = true;
            var lamp = ConstructParts.Need("Lamp");
            var shelfDef = ConstructParts.Need("Parts Shelf");
            var shelf = w.AddPart(c, shelfDef, new Vector3(0, 0, -2), 0);
            c.Rebuild();
            var pile = 0;
            for (var k = 0; k < shelfDef.Slots.Length; k++)
                if (shelfDef.Slots[k].Stack > 1)
                {
                    pile = k;
                    break;
                }
            for (var n = 0; n < 3; n++)
                w.SetIn(c, shelf, pile, lamp);
            CbChute(w, c, shelf, 255, vol, 255);
            var shelfOut = w.AddPart(c, shelfDef, new Vector3(2, 0, -2), 0);
            c.Rebuild();
            var outByte = ConstructRoutes.PortNamed(def, RouteType.Chute, "Chute out")!.Byte;
            CbChute(w, c, vol, outByte, shelfOut, 255);
            w.StructureDirty.Add(c.Id);
            bool OutOnWire(Game g) => Copy(g) is { } cc && Enumerable.Range(0, cc.RouteCount).Any(k => cc.Routes[k].Type == RouteType.Chute && cc.Routes[k].SourcePort == outByte);
            if (!Await(() => OutOnWire(a), 3, [a]))
                Fail("custom bins network: the named port didn't reach the client");
            ConstructRoutes.Remove(w, c, CbFind(c, vol, shelfOut));
            c.Dirty = true;
            c.Rebuild();
            w.StructureDirty.Add(c.Id);
            var kinds = 0;
            foreach (var d in ConstructParts.All)
                if (kinds < 30 && !d.PhysicsOnly && d.Role != PartRole.Storage && d != lamp && w.PutPart(c, vol, d) == null)
                    kinds++;
            int Sum(Game g) => Store(g) is { } cs ? cs.ItemCount : -1;
            var moved = Await(() => st.ItemCount >= kinds + 3 && Sum(a) == st.ItemCount && Store(a)!.KindN == st.KindN, 8, [a]);
            if (!moved)
                Fail($"custom bins network: the parts didn't reach the client ({Sum(a)} of {st.ItemCount}, kinds {Store(a)?.KindN}/{st.KindN})");
            Line($"custom bins network: {st.ItemCount} parts, {st.KindN} kinds, {st.ItemKg:0.0} kg");
            // The late joiner hears it all, and the mass.
            using var late = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Late", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            var massSame = Await(() => Copy(late) is { } cl && Store(late)?.ItemCount == st.ItemCount && Store(late)!.KindN == st.KindN && MathF.Abs(cl.Mass - c.Mass) < 0.5f && !cl.Dirty, 6, [a, late]);
            if (!massSame)
                Fail($"custom bins network: the joiner's copy isn't the server's ({Store(late)?.ItemCount} parts, {Copy(late)?.Mass} kg; server {st.ItemCount}, {c.Mass} kg)");
            Span<CHolding> hold = new CHolding[CStore.MaxKinds + 1];
            if (Copy(late) is { } cl2)
            {
                var hn = late.Constructs.Holdings(cl2, vol, hold);
                if (hn != st.KindN)
                    Fail($"custom bins network: Holdings on the joiner reads {hn} kinds, not {st.KindN}");
            }
            if (Program.SandboxGuardTrips != trips0)
                Fail($"custom bins network: the guard tripped {Program.SandboxGuardTrips - trips0} time(s)");
            _ = (simHost, simClient);
        }
        finally
        {
            sb.InfiniteParts = keepInfinite;
            yard.RestoreProps();
        }
    }
}
