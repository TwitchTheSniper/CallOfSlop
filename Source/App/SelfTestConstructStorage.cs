using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Construct storage" (decision 18, physical storage; docs/sandbox/CONSTRUCTS.md "Physical storage"): the storage
    /// parts load (the chest, the shelf, the wheel rack, the bin) and bad storage blocks are refused for the right
    /// reason; what fits where (a size class, a shape, a pile and its limit); in an offline Sandbox game on the yard a
    /// carried part set into a fitting chest slot is stored and sits in it (a ray finds it there, and not through the
    /// shut lid), a non-fitting part is refused and stays in the hands, taking it out puts it back in the hands, two
    /// players taking the same part: one wins, a stored part is shot to pieces, a shelf piles small parts, a bin takes
    /// and gives scrap with its level rising, a destroyed chest spills its contents as loose parts and a broken bin its
    /// scrap as bundles, and survival salvage comes into the hands; the state survives a round trip through its bytes
    /// (a truncated one refused); the work allocates nothing; and over a loopback both clients see the contents, two
    /// clients grabbing one part: one gets it, and a late joiner gets the contents.
    /// </summary>
    private static void CheckConstructStorage()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail($"construct storage: the {ConstructYardMap} map (with its sandbox section) is missing");
            return;
        }
        StorageData();
        StorageRoundTrip();
        var keepInfinite = sb.InfiniteParts;
        var keepMenu = sb.PartsMenu;
        var keepSeconds = sb.DebrisSeconds;
        try
        {
            StorageOffline(yard, sb);
            sb.InfiniteParts = keepInfinite;
            sb.PartsMenu = keepMenu;
            sb.DebrisSeconds = keepSeconds;
            StorageOverWire(yard, sb);
        }
        finally
        {
            sb.InfiniteParts = keepInfinite;
            sb.PartsMenu = keepMenu;
            sb.DebrisSeconds = keepSeconds;
        }
    }

    private static void StorageData()
    {
        var chest = ConstructParts.Find("Chest");
        var shelf = ConstructParts.Find("Parts Shelf");
        var rack = ConstructParts.Find("Wheel Rack");
        var bin = ConstructParts.Find("Scrap Bin");
        if (chest == null || shelf == null || rack == null || bin == null || ConstructBuild.FindBlueprint("Chest") == null || ConstructParts.Find(ConstructStorage.ScrapBundle) == null)
        {
            Fail("construct storage: the Chest, Parts Shelf, Wheel Rack, Scrap Bin or Scrap Bundle part (or the Chest blueprint) didn't load");
            return;
        }
        Line($"storage parts: Chest {string.Join(", ", chest.Slots.Select(s => ConstructStorage.SizeClass(s)))}; Parts Shelf {string.Join(", ", shelf.Slots.Select(s => ConstructStorage.SizeClass(s) + (s.Stack > 1 ? $" x{s.Stack}" : "")))}; "
             + $"Wheel Rack {string.Join(", ", rack.Slots.Select(s => ConstructStorage.SizeClass(s)))}; Scrap Bin {bin.BulkCapacity} {bin.BulkMaterial}");
        if (chest.Slots.Length != 2 || ConstructStorage.SizeClass(chest.Slots[0]) != "medium" || shelf.Slots.Count(s => s.Stack > 1) != 3 || rack.Slots.Any(s => s.Fits.Length != 1 || s.Fits[0] != PartRole.Wheel) || !bin.IsBin)
            Fail("construct storage: the storage parts' slots aren't what their files say");

        // Bad storage blocks, each refused for its reason.
        const string head = "{\"name\": \"Box\", \"role\": \"storage\", \"tool\": \"wrench\", \"mass\": 5, \"health\": 50, \"boxes\": [{\"min\": [-0.5, 0, -0.5], \"max\": [0.5, 0.05, 0.5]}, {\"min\": [-0.5, 0.05, -0.5], \"max\": [-0.45, 0.6, 0.5]}], \"sockets\": [{\"at\": [0, 0, 0], \"dir\": \"-y\"}], ";
        string Bad(string storage)
        {
            try
            {
                ConstructParts.Parse(head + "\"storage\": " + storage + "}", "box.json");
                return "accepted";
            }
            catch (ConstructPartException ex)
            {
                return ex.Message;
            }
        }
        var good = Bad("{\"slots\": [{\"min\": [-0.4, 0.06, -0.4], \"max\": [0.4, 0.5, 0.4]}]}");
        (string Json, string Want)[] bad =
        [
            ("{}", "needs \"slots\" or \"bulk\""),
            ("{\"slots\": [{\"min\": [-0.4, 0.06, -0.4], \"max\": [0.4, 0.9, 0.4]}]}", "outside the part"),
            ("{\"slots\": [{\"min\": [-0.5, 0.06, -0.4], \"max\": [0.4, 0.5, 0.4]}]}", "runs into the part's box"),
            ("{\"slots\": [{\"min\": [-0.4, 0.06, -0.4], \"max\": [0.1, 0.5, 0.4]}, {\"min\": [0, 0.06, -0.4], \"max\": [0.4, 0.5, 0.4]}]}", "overlaps slots[0]"),
            ("{\"slots\": [{\"min\": [-0.4, 0.06, -0.4], \"max\": [0.4, 0.5, 0.4], \"fits\": [\"teapot\"]}]}", "isn't a part role"),
            ("{\"slots\": [{\"min\": [-0.4, 0.06, -0.4], \"max\": [0.4, 0.5, 0.4], \"stack\": 40}]}", "stack"),
            ("{\"slots\": [{\"min\": [-0.4, 0.06, -0.4], \"max\": [-0.38, 0.5, 0.4]}]}", "smaller than 5 cm"),
            ("{\"bulk\": {\"material\": \"jam\", \"capacity\": 10, \"min\": [-0.4, 0.06, -0.4], \"max\": [0.4, 0.5, 0.4]}}", "isn't a bulk material"),
            ("{\"bulk\": {\"material\": \"scrap\", \"capacity\": 10, \"min\": [-0.4, 0.06, -0.4], \"max\": [0.4, 0.5, 0.4]}, \"slots\": [{\"min\": [-0.4, 0.06, -0.4], \"max\": [0.4, 0.5, 0.4]}]}", "not both"),
            ("{\"shelves\": 2}", "shelves"),
        ];
        var wrong = bad.Where(b => !Bad(b.Json).Contains(b.Want)).Select(b => $"{b.Want}: \"{Bad(b.Json)}\"").ToList();
        Line($"storage files: a good block {good}; {bad.Length - wrong.Count}/{bad.Length} bad ones refused for their reason{(wrong.Count > 0 ? ": " + string.Join("; ", wrong) : "")}");
        if (good != "accepted" || wrong.Count > 0)
            Fail("construct storage: a storage block isn't checked right");

        // What fits where.
        var susp = ConstructParts.Need("Suspension");
        var engine = ConstructParts.Need("Engine");
        var wheel = ConstructParts.Need("Wheel");
        var lamp = ConstructParts.Need("Lamp");
        var st = new CStore();
        st.Reset(chest.Slots.Length);
        var suspOk = ConstructStorage.CanSet(chest, st, 0, susp, out _);
        var engineNo = ConstructStorage.CanSet(chest, st, 0, engine, out _);
        var engineShelf = ConstructStorage.CanSet(shelf, Fresh(shelf), 0, engine, out _);
        var wheelRack = ConstructStorage.CanSet(rack, Fresh(rack), 0, wheel, out var wheelRot);
        var suspRack = ConstructStorage.CanSet(rack, Fresh(rack), 0, susp, out _);
        var pile = Fresh(shelf);
        var piled = 0;
        while (ConstructStorage.CanSet(shelf, pile, 2, lamp, out var lr) == null && piled < 20)
        {
            pile.Def[2] = lamp.Id;
            pile.Rot[2] = lr;
            pile.Count[2]++;
            piled++;
        }
        var seventh = ConstructStorage.CanSet(shelf, pile, 2, lamp, out _);
        var other = ConstructStorage.CanSet(shelf, pile, 2, ConstructParts.Need("Button"), out _);
        Line($"storage fits: Suspension in a Chest slot {suspOk ?? "yes"}; Engine: \"{engineNo}\"; Engine on the shelf's big slot {engineShelf ?? "yes"}; Wheel in the rack {wheelRack ?? "yes"} (turn {wheelRot}); "
             + $"Suspension in the rack: \"{suspRack}\"; {piled} Lamps pile in a parts bin, then \"{seventh}\", a Button there: \"{other}\"");
        // (decision 21: the 120 kg engine is physics-only, so no slot takes it, the shelf's big one included)
        if (suspOk != null || engineNo == null || !engineNo.Contains("physics-only") || engineShelf == null || !engineShelf.Contains("physics-only") || wheelRack != null || suspRack == null || !suspRack.Contains("wheel")
            || piled != 6 || seventh == null || !seventh.Contains("full") || other == null)
            Fail("construct storage: what fits which slot is wrong");
    }

    private static CStore Fresh(ConstructPartDef d)
    {
        var s = new CStore { Live = true };
        s.Reset(d.Slots.Length);
        return s;
    }

    private static void StorageRoundTrip()
    {
        var shelf = ConstructParts.Need("Parts Shelf");
        var s = Fresh(shelf);
        s.Def[0] = ConstructParts.Need("Engine").Id;
        s.Count[0] = 1;
        s.Rot[0] = 5;
        s.Health[0] = 0.5f;
        s.Def[3] = ConstructParts.Need("Lamp").Id;
        s.Count[3] = 4;
        s.Health[3] = 1f;
        var w = new PacketWriter(256);
        w.Begin(Msg.CBody);
        ConstructStorage.Write(w, s);
        var bytes = w.Span[1..].ToArray();
        var back = new CStore();
        var r = new PacketReader(bytes);
        ConstructStorage.Read(ref r, back);
        var same = back.SlotCount == s.SlotCount && back.Live;
        for (var k = 0; k < s.SlotCount; k++)
            same &= back.Def[k] == s.Def[k] && back.Count[k] == s.Count[k] && back.Rot[k] == s.Rot[k] && MathF.Abs(back.Health[k] - s.Health[k]) < 0.01f;
        var bin = Fresh(ConstructParts.Need("Scrap Bin"));
        bin.Amount = 321;
        w.Begin(Msg.CBody);
        ConstructStorage.Write(w, bin);
        var binBytes = w.Span[1..].ToArray();
        var binBack = new CStore();
        var rb = new PacketReader(binBytes);
        ConstructStorage.Read(ref rb, binBack);
        string truncated;
        try
        {
            var rt = new PacketReader(bytes.AsSpan(0, bytes.Length - 3));
            ConstructStorage.Read(ref rt, new CStore());
            truncated = "accepted";
        }
        catch (MalformedPacketException)
        {
            truncated = "refused";
        }
        var badPart = (byte[])bytes.Clone();
        badPart[1] = 0xFE; // (slot 0's part: no such type; two bytes since the part ids widened)
        badPart[2] = 0xFE;
        string unknown;
        try
        {
            var ru = new PacketReader(badPart);
            ConstructStorage.Read(ref ru, new CStore());
            unknown = "accepted";
        }
        catch (MalformedPacketException)
        {
            unknown = "refused";
        }
        Line($"storage state: a shelf ({s.Items} parts) in {bytes.Length} bytes reads back the same {same}; a bin of {bin.Amount} scrap reads back {binBack.Amount}; truncated {truncated}; an unknown part {unknown}");
        if (!same || binBack.Amount != 321 || truncated != "refused" || unknown != "refused")
            Fail("construct storage: the state doesn't survive a round trip through its bytes (or bad bytes are trusted)");
    }

    /// <summary>The construct's storage part (the first of its kind), or -1.</summary>
    private static int StorePart(Construct c, string name)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).Name == name)
                return i;
        return -1;
    }

    /// <summary>A ray straight down into a slot from above it (in the world): what it meets.</summary>
    private static bool RayIntoSlot(ConstructWorld w, Construct c, int part, int slot, out ConstructHit hit)
    {
        ref var cp = ref c.Parts[part];
        ConstructStorage.SlotBox(cp, cp.D.Slots[slot], out var sc, out _);
        c.WorldFrame(cp.Seg, out var q, out var t);
        var at = t + Vector3.Transform(sc, q);
        return w.Raycast(at + new Vector3(0, 1.5f, 0), -Vector3.UnitY, 3f, out hit);
    }

    private static void StorageOffline(Map yard, SandboxMap sb)
    {
        yard.RestoreProps();
        World.SetMap(yard);
        sb.DebrisSeconds = 0;
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me)
        {
            Fail("construct storage: no local player in the Sandbox game");
            return;
        }
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        var w = g.Constructs;
        ref var hand = ref g.SandboxHands.Hand(me.Id);
        var susp = ConstructParts.Need("Suspension");
        var engine = ConstructParts.Need("Engine");
        var lamp = ConstructParts.Need("Lamp");
        var battery = ConstructParts.Need("Battery");

        // A chest a few metres from the player.
        var spot = new Vector3(me.Position.X + 4f, 0, me.Position.Z);
        var chest = ConstructBuild.BuildOnGround(w, ConstructBuild.FindBlueprint("Chest")!, ConstructWorld.Ground(World.Query, spot.X, spot.Z), 0, out var why);
        if (chest == null)
        {
            Fail($"construct storage: the Chest blueprint didn't build ({why})");
            return;
        }
        Run(g, ref now, 1.0);
        var box = StorePart(chest, "Chest");
        var lid = StorePart(chest, "Chest Lid");
        StandBy(me, PartCentre(chest, box), 1.6f, chest.WorldCom + new Vector3(0, 0, -1));
        Run(g, ref now, 0.3);
        Want(w.StoreOf(chest, box) is { Empty: true } && lid >= 0, "the chest has an empty store and a lid");
        var shutHidden = !RayIntoSlot(w, chest, box, 0, out var shutHit) || shutHit.Part == lid;

        // Shut: the lid is in the way; opened with F (Use) it swings up.
        g.SandboxSend(CActKind.Use, body: chest.Id, part: (byte)lid);
        Run(g, ref now, 1.2);
        var open = RayIntoSlot(w, chest, box, 0, out var openHit) && openHit.Part == box;
        Want(open, "the lid opens with Use and the slot can be seen into");

        // A suspension carried to the chest goes into a fitting slot; the engine doesn't fit; taking out returns it to the hands.
        hand.Held = susp.Id;
        g.SandboxSend(CActKind.StoreIn, body: chest.Id, part: (byte)box, socket: 0);
        Run(g, ref now, 0.1);
        var st = w.StoreOf(chest, box)!;
        var stored = st.Def[0] == susp.Id && hand.Held == ConstructParts.None;
        var seen = RayIntoSlot(w, chest, box, 0, out var itemHit) && itemHit.Part == box && itemHit.SlotPlus1 == 1;
        ConstructStorage.SlotBox(chest.Parts[box], chest.DefOf(box).Slots[0], out var slotC, out var slotH);
        var inside = true;
        for (var b = 0; b < susp.SolidBoxes; b++)
        {
            ConstructStorage.ItemBox(chest.Parts[box], chest.DefOf(box).Slots[0], susp, st.Rot[0], 0, b, out var ic, out var ih);
            inside &= Vector3.Max(Vector3.Abs(ic - slotC) + ih - slotH, Vector3.Zero).Length() < 0.006f;
        }
        Want(stored && seen && inside, "the suspension goes into slot 0, sits inside it and a ray finds it there");
        hand.Held = engine.Id;
        g.SandboxSend(CActKind.StoreIn, body: chest.Id, part: (byte)box, socket: 1);
        Run(g, ref now, 0.1);
        var refused = st.Def[1] == ConstructParts.None && hand.Held == engine.Id && g.SandboxNotice.Contains("physics-only");
        Want(refused, $"the engine (physics-only) is refused by the slot (notice \"{g.SandboxNotice}\")");
        g.SandboxSend(CActKind.StoreIn, body: chest.Id, part: (byte)box, socket: 0);
        Run(g, ref now, 0.1);
        var taken = st.Def[0] == susp.Id && hand.Held == engine.Id; // (slot taken)
        hand.Held = ConstructParts.None;
        g.SandboxSend(CActKind.TakeStored, body: chest.Id, part: (byte)box, socket: 0);
        Run(g, ref now, 0.1);
        var back = hand.Held == susp.Id && st.Def[0] == ConstructParts.None;
        Want(taken && back, "a taken slot is refused, and taking the suspension out puts it in the hands");

        // Two players take the same stored part in one tick: one wins (the server's order), the other is told.
        hand.Held = ConstructParts.None;
        w.SetIn(chest, box, 1, battery);
        const byte other = 77;
        ref var otherHand = ref g.SandboxHands.Hand(other);
        otherHand = SandboxHand.Empty;
        var eye = me.Eye;
        var first = g.SandboxHands.TakeStored(w, me.Id, eye, chest.Id, box, 1);
        var second = g.SandboxHands.TakeStored(w, other, eye, chest.Id, box, 1);
        var oneWins = first == null && hand.Held == battery.Id && second != null && otherHand.Held == ConstructParts.None;
        Want(oneWins, $"two takers: the first gets it, the second is refused (\"{second}\")");
        otherHand = SandboxHand.Empty;

        // A stored part shot through goes; the chest is still whole.
        hand.Held = ConstructParts.None;
        w.SetIn(chest, box, 0, lamp);
        var shotAt = RayIntoSlot(w, chest, box, 0, out var shot) && shot.SlotPlus1 == 1;
        if (shotAt)
            for (var k = 0; k < 10 && st.Def[0] != ConstructParts.None; k++)
                w.DamageStored(chest, shot.Part, shot.SlotPlus1 - 1, 30);
        Want(shotAt && st.Def[0] == ConstructParts.None && chest.Parts[box].Health >= chest.DefOf(box).Health, "a stored lamp hit by rounds is destroyed, the chest untouched");

        // A shelf piles small parts in its bins; the count shows; a different part is refused there.
        var shelfDef = ConstructParts.Need("Parts Shelf");
        var shelf = ConstructBuild.PlaceNew(w, shelfDef, ConstructWorld.Ground(World.Query, spot.X, spot.Z + 3f), 0)!;
        Run(g, ref now, 0.5);
        var piles = 0;
        for (var k = 0; k < 8; k++)
            if (w.SetIn(shelf, 0, 2, lamp) == null)
                piles++;
        var shelfStore = w.StoreOf(shelf, 0)!;
        var mixed = w.SetIn(shelf, 0, 2, ConstructParts.Need("Button"));
        var copies = ConstructStorage.DrawnCopies(shelfDef.Slots[2], lamp, shelfStore.Rot[2], shelfStore.Count[2]);
        Want(piles == 6 && shelfStore.Count[2] == 6 && mixed != null && copies >= 2, $"the parts bin piles 6 lamps ({piles}, drawn {copies} high), a button refused (\"{mixed}\")");
        var pickRefused = g.SandboxHands.PickUp(w, me.Id, me.Eye, shelf.Id);

        // A bin: scrap in, scrap out, the level rises.
        var binDef = ConstructParts.Need("Scrap Bin");
        var bin = ConstructBuild.PlaceNew(w, binDef, ConstructWorld.Ground(World.Query, spot.X - 2.5f, spot.Z), 0)!;
        Run(g, ref now, 0.5);
        StandBy(me, PartCentre(bin, 0), 1.6f, bin.WorldCom + new Vector3(1, 0, 0));
        Run(g, ref now, 0.2);
        var inv = g.SandboxInventory(me.Id);
        inv.AddScrap(120);
        g.SandboxSend(CActKind.Bin, body: bin.Id, part: 0);
        Run(g, ref now, 0.1);
        var binStore = w.StoreOf(bin, 0)!;
        var afterIn = (binStore.Amount, inv.Scrap);
        ConstructStorage.FillBox(bin.Parts[0], binDef, binStore.Amount, out _, out var fillLow);
        g.SandboxSend(CActKind.Bin, body: bin.Id, part: 0);
        Run(g, ref now, 0.1);
        var afterOut = (binStore.Amount, inv.Scrap);
        w.Deposit(bin, 0, 400);
        ConstructStorage.FillBox(bin.Parts[0], binDef, binStore.Amount, out _, out var fillHigh);
        Want(afterIn == (120, 0) && afterOut == (20, 100) && fillHigh.Y > fillLow.Y * 2, $"the bin takes 120 scrap ({afterIn}), gives a load of 100 back ({afterOut}), its level rising ({fillLow.Y * 2:0.00} m to {fillHigh.Y * 2:0.00} m)");

        // A destroyed chest spills its contents as loose parts; a broken bin its scrap as bundles to salvage.
        StandAt(me, spot.X - 10f, spot.Z - 6f);
        w.SetIn(chest, box, 0, susp);
        w.SetIn(chest, box, 1, battery);
        var storesBefore = w.LiveStores;
        var bodiesBefore = w.Bodies.Select(b => b.Id).ToHashSet();
        w.DamagePart(chest, box, 100000, out _);
        Run(g, ref now, 1.0);
        var spilled = w.Bodies.Where(b => !bodiesBefore.Contains(b.Id) && b.Loose).Select(b => b.DefOf(0).Name).OrderBy(n => n).ToList();
        Want(spilled.Contains(susp.Name) && spilled.Contains(battery.Name) && w.LiveStores == storesBefore - 1, $"the destroyed chest spills its suspension and battery as loose parts ({string.Join(", ", spilled)}) and frees its store");
        var bundleDef = ConstructParts.Need(ConstructStorage.ScrapBundle);
        bodiesBefore = w.Bodies.Select(b => b.Id).ToHashSet();
        w.Blast(bin.WorldCom, 3f, 100000);
        Run(g, ref now, 1.0);
        var bundles = w.Bodies.Count(b => !bodiesBefore.Contains(b.Id) && b.PartCount == 1 && b.DefOf(0) == bundleDef && b.Parts[0].Broken);
        Want(bundles == ConstructStorage.MaxSpillBundles, $"the broken bin's 420 scrap spills as {bundles} bundles (wrecks to salvage)");

        // Survival: a part salvaged comes into the hands, not the inventory.
        sb.InfiniteParts = false;
        sb.PartsMenu = false;
        Run(g, ref now, 0.1);
        var gate = w.Bodies.FirstOrDefault(b => b.Owner == 255 && b.Anchored && PartOf(b, PartRole.Door) >= 0 && b.JointCount > 0 && b.Joints[0].Type == JointType.Passive);
        var survivalHand = "no gate";
        if (gate != null)
        {
            var door = PartOf(gate, PartRole.Door);
            hand.Held = ConstructParts.None;
            StandBy(me, PartCentre(gate, door), 1.8f, gate.WorldCom);
            Run(g, ref now, 0.3);
            g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Wrench);
            g.SandboxSend(CActKind.Remove, body: gate.Id, part: (byte)door);
            Run(g, ref now, ConstructBuild.RemoveSeconds(gate.DefOf(door)) + 0.5);
            survivalHand = $"{ConstructParts.Get(hand.Held)?.Name ?? "nothing"} in hand, {inv.TotalParts} in the inventory";
            Want(hand.Held == ConstructParts.Need("Door").Id && inv.TotalParts == 0, $"survival: the gate's door salvaged comes into the hands ({survivalHand})");
        }
        else
            problems.Add("no gate on the yard to salvage");
        sb.InfiniteParts = true;
        sb.PartsMenu = true;
        hand.Held = ConstructParts.None;
        Run(g, ref now, 0.1);

        StorageNoAlloc(g, ref now, shelf, problems);
        Line($"storage offline: shut lid hides the slot {shutHidden}, opened {open}; stored {stored}, seen there {seen}, inside the slot {inside}; engine refused {refused}; taken back {back}; two takers one wins {oneWins}; "
             + $"a full shelf can't be picked up: \"{pickRefused}\"; bin {afterIn} -> {afterOut}; spilled [{string.Join(", ", spilled)}], {bundles} scrap bundles; survival: {survivalHand}");
        if (!shutHidden)
            problems.Add("the shut lid doesn't hide the slot");
        if (pickRefused == null || !pickRefused.Contains("empty"))
            problems.Add("a loose shelf holding parts can be picked up");
        if (problems.Count > 0)
            Fail($"construct storage: offline: {string.Join("; ", problems)}");
    }

    /// <summary>No allocation: the Sandbox tick with stocked storage, the rays into it, the fit checks and drawing's geometry, the state's message.</summary>
    private static void StorageNoAlloc(Game g, ref double now, Construct shelf, List<string> problems)
    {
        Run(g, ref now, 0.3);
        var w = g.Constructs;
        var lamp = ConstructParts.Need("Lamp");
        var susp = ConstructParts.Need("Suspension");
        w.SetIn(shelf, 0, 0, susp);
        var st = w.StoreOf(shelf, 0)!;
        var pw = new PacketWriter(4096);
        var t = now;
        var sum = 0f;
        void Work(int n)
        {
            for (var k = 0; k < n; k++)
            {
                t += World.TickDt;
                g.ServerTickSandbox(t);
                for (var s = 0; s < 5; s++)
                {
                    if (RayIntoSlot(w, shelf, 0, s, out var hit))
                        sum += hit.Distance + hit.SlotPlus1;
                    sum += ConstructStorage.Check(shelf.DefOf(0), st, s, lamp, out var rot) == SlotCheck.Ok ? rot : 0;
                    ConstructStorage.ItemBox(shelf.Parts[0], shelf.DefOf(0).Slots[s], lamp, 0, 0, 0, out var c, out _);
                    sum += c.Y;
                }
                w.TakeOut(shelf, 0, 2);
                w.SetIn(shelf, 0, 2, lamp);
                ConstructNet.WriteStore(pw, st);
            }
        }
        Work(60);
        var least = long.MaxValue;
        for (var round = 0; round < 3; round++)
        {
            var b0 = GC.GetAllocatedBytesForCurrentThread();
            Work(120);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - b0);
        }
        now = t;
        Line($"storage: the Sandbox tick, rays into the slots, fit checks, a take and a set, and the store's message allocate {least} bytes per 120 ticks (least of three){(sum < 0 ? "" : "")}");
        if (least > 0)
            problems.Add($"{least} bytes allocated per 120 ticks (should be none)");
    }

    private static void StorageOverWire(Map yard, SandboxMap sb)
    {
        const ushort port = 26349;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        sb.DebrisSeconds = 0;
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, b], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a, b], 0.25, ref now);
        Pump(server, [a, b], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || b.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var pa) || !server.Players.TryGetValue(b.LocalId, out var pb))
        {
            Fail("construct storage over the wire: the clients didn't join");
            return;
        }
        if (!pa.Alive)
            server.SelfTestRespawn(pa, now);
        if (!pb.Alive)
            server.SelfTestRespawn(pb, now);
        Pump(server, [a, b], 0.3, ref now, clock);
        var w = server.Constructs;
        var shelfDef = ConstructParts.Need("Parts Shelf");
        var lamp = ConstructParts.Need("Lamp");
        var susp = ConstructParts.Need("Suspension");
        var shelf = ConstructBuild.PlaceNew(w, shelfDef, ConstructWorld.Ground(World.Query, pa.Position.X + 3f, pa.Position.Z), 0)!;
        Pump(server, [a, b], 0.5, ref now, clock);
        // Both clients stand by it; client A sets a carried lamp in through its own request.
        StandBy(pa, PartCentre(shelf, 0), 1.6f, shelf.WorldCom + new Vector3(0, 0, -1));
        StandBy(pb, PartCentre(shelf, 0), 1.6f, shelf.WorldCom + new Vector3(0.4f, 0, -1));
        server.SandboxHands.Hand(pa.Id).Held = lamp.Id;
        w.SetIn(shelf, 0, 0, susp);
        Pump(server, [a, b], 0.3, ref now, clock);
        a.SandboxSend(CActKind.StoreIn, body: shelf.Id, part: 0, socket: 3);
        Pump(server, [a, b], 0.4, ref now, clock);
        var ss = w.StoreOf(shelf, 0);
        var ca = a.Constructs.Get(shelf.Id) is { } sa ? a.Constructs.StoreOf(sa, 0) : null;
        var cb = b.Constructs.Get(shelf.Id) is { } sbb ? b.Constructs.StoreOf(sbb, 0) : null;
        bool Same(CStore? x) => x != null && ss != null && x.Def[0] == ss.Def[0] && x.Def[3] == ss.Def[3] && x.Count[3] == ss.Count[3];
        var mirrored = ss != null && ss.Def[3] == lamp.Id && ss.Def[0] == susp.Id && Same(ca) && Same(cb);
        // Both clients grab the suspension at once: exactly one gets it.
        a.SandboxSend(CActKind.TakeStored, body: shelf.Id, part: 0, socket: 0);
        b.SandboxSend(CActKind.TakeStored, body: shelf.Id, part: 0, socket: 0);
        Pump(server, [a, b], 0.5, ref now, clock);
        var heldA = server.SandboxHands.Hand(pa.Id).Held == susp.Id;
        var heldB = server.SandboxHands.Hand(pb.Id).Held == susp.Id;
        var clientEmpty = ca != null && ca.Def[0] == ConstructParts.None && cb != null && cb.Def[0] == ConstructParts.None;
        var told = (heldA ? b.SandboxNotice : a.SandboxNotice).Contains("empty");
        // A late joiner gets the contents.
        using var c = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Charlie", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, b, c], 1.0, ref now, clock);
        var cc = c.Constructs.Get(shelf.Id) is { } sc ? c.Constructs.StoreOf(sc, 0) : null;
        var late = cc != null && cc.Def[3] == lamp.Id && cc.Count[3] == 1 && cc.Def[0] == ConstructParts.None;
        Line($"storage over the wire: a client's lamp set in on the server {ss?.Def[3] == lamp.Id}, both clients mirror the shelf {mirrored}; two clients grab the suspension: A {heldA}, B {heldB}, the loser told {told}, gone on the clients {clientEmpty}; a late joiner sees the lamp {late}");
        if (!mirrored)
            Fail("construct storage over the wire: a client's part didn't go in, or the clients don't see the contents");
        if (heldA == heldB || !clientEmpty || !told)
            Fail("construct storage over the wire: two clients taking one part didn't end with exactly one holding it");
        if (!late)
            Fail("construct storage over the wire: a late joiner doesn't get the contents");
    }
}
