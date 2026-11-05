using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    private const int RasterW = 96, RasterH = 48;

    /// <summary>A card as pixels: its quads painted in layer order (a glyph as its box), so a colour or a shape that changes shows here with no GL.</summary>
    private static Vector3[] PanelPixels(PanelCard card, Vector2 size, int rows)
    {
        var quads = PanelRender.LaidOut(card, size, rows);
        var px = new Vector3[RasterW * RasterH];
        for (var layer = 0; layer <= 2; layer++)
            for (var q = 0; q + PanelRender.QuadFloats <= quads.Length; q += PanelRender.QuadFloats)
            {
                if ((int)quads[q + 9] != layer)
                    continue;
                var x0 = (int)MathF.Floor((quads[q] / size.X + 0.5f) * RasterW);
                var x1 = (int)MathF.Ceiling((quads[q + 10] / size.X + 0.5f) * RasterW);
                var y0 = (int)MathF.Floor((quads[q + 1] / size.Y + 0.5f) * RasterH);
                var y1 = (int)MathF.Ceiling((quads[q + 11] / size.Y + 0.5f) * RasterH);
                var colour = new Vector3(quads[q + 6], quads[q + 7], quads[q + 8]);
                for (var y = Math.Max(0, y0); y < Math.Min(RasterH, y1); y++)
                    for (var x = Math.Max(0, x0); x < Math.Min(RasterW, x1); x++)
                        px[y * RasterW + x] = colour;
            }
        return px;
    }

    /// <summary>The share of the panel's inside that isn't its plain background.</summary>
    private static float PanelInk(Vector3[] px)
    {
        var plain = new Vector3(0.03f, 0.045f, 0.055f);
        var n = 0;
        var all = 0;
        for (var y = 4; y < RasterH - 4; y++)
            for (var x = 4; x < RasterW - 4; x++)
            {
                all++;
                if (Vector3.DistanceSquared(px[y * RasterW + x], plain) > 0.001f)
                    n++;
            }
        return n / (float)all;
    }

    private static bool Differ(Vector3[] a, Vector3[] b)
    {
        for (var k = 0; k < a.Length; k++)
            if (Vector3.DistanceSquared(a[k], b[k]) > 0.002f)
                return true;
        return false;
    }

    private static string CardText(PanelCard card)
    {
        var s = card.State.ToString();
        for (var l = 0; l < PanelCard.MaxLines; l++)
            if (card.Line(l).Length > 0)
                s += " | " + new string(card.Line(l));
        return s;
    }

    private static PanelCard FillCard(ConstructWorld w, Construct c, int part, int panel = 0)
    {
        var card = new PanelCard();
        var d = c.DefOf(part);
        d.Panels[panel].Fill!(w, c, part, d.Panels[panel], card);
        return card;
    }

    /// <summary>
    /// The display parts and the machines' screens: a preview for every display, drawn from the real panel; the Bar Gauges and Graph Screen
    /// with no number and the whole screen theirs; the colour modes and the colour setting changing the pixels; the settings through a
    /// blueprint, a world save and a real host and client; every machine's states, on the screen's own lines, with no allocation.
    /// </summary>
    private static void CheckDisplayScreens()
    {
        void Want(bool ok, string what)
        {
            if (!ok)
                Fail("display screens: " + what);
        }
        Previews(Want);
        Plots(Want);
        Colours(Want);
        MachineStates(Want);
        DisplaysSaved(Want);
        DisplaysOnline(Want);
    }

    private static void Previews(Action<bool, string> want)
    {
        var card = new PanelCard();
        var sources = new HashSet<string>();
        var seen = 0;
        foreach (var d in ConstructParts.All.Where(d => d.Panels.Length > 0 && d.Panels[0].Source.StartsWith("logic.")))
        {
            seen++;
            sources.Add(d.Panels[0].Source);
            want(LogicDisplays.Preview(d, card, out var panel), $"{d.Name} has no preview");
            var ink = PanelInk(PanelPixels(card, panel.Size, panel.Rows));
            want(ink > 0.02f, $"{d.Name}'s preview is blank ({ink:0.000}): {CardText(card)}");
            want(!CardText(card).Contains("no power"), $"{d.Name}'s preview has no power");
        }
        Line($"display screens: {seen} displays previewed, {sources.Count} kinds");
        want(seen >= 30 && sources.Count == 11, $"the previews reach every display ({seen}, kinds {string.Join(", ", sources)})");
        want(!LogicDisplays.Preview(ConstructParts.Need("Smelter"), card, out _), "a machine isn't a display's preview");
        // Not one card shown on all: each shape has its own look.
        LogicDisplays.Preview(ConstructParts.Need("Bar Gauge"), card, out var bar);
        var a = PanelPixels(card, bar.Size, bar.Rows);
        LogicDisplays.Preview(ConstructParts.Need("Number Readout"), card, out var readout);
        want(Differ(a, PanelPixels(card, readout.Size, readout.Rows)), "a Bar Gauge's preview looks like a Number Readout's");
    }

    private static void Plots(Action<bool, string> want)
    {
        var w = new ConstructWorld();
        foreach (var name in (string[])["Bar Gauge", "Tall Bar Gauge", "Graph Screen", "Medium Bar Gauge", "Large Graph Screen"])
        {
            var d = ConstructParts.Need(name);
            var c = ConstructEdits.DropLoose(w, d, new Vector3(0, 10, 0), Quaternion.Identity)!;
            c.Parts[0].Reading = 80;
            c.Parts[0].Trace = new float[LogicDisplays.TraceLength];
            var card = FillCard(w, c, 0);
            var panel = d.Panels[0];
            want(CardText(card) == "Working", $"the {name} draws text: {CardText(card)}");
            want(!ConstructConfig.Has(d, CfgKey.Decimals) && !ConstructConfig.Has(d, CfgKey.Unit), $"the {name} still has number settings");
            // The plot's ground fills the panel inside its edge.
            var quads = PanelRender.LaidOut(card, panel.Size, panel.Rows);
            float wide = 0, tall = 0;
            for (var q = 5 * PanelRender.QuadFloats; q + PanelRender.QuadFloats <= quads.Length; q += PanelRender.QuadFloats)
            {
                wide = MathF.Max(wide, quads[q + 10] - quads[q]);
                tall = MathF.Max(tall, quads[q + 11] - quads[q + 1]);
            }
            want(wide > panel.Size.X - panel.Size.Y * 0.15f && tall > panel.Size.Y * 0.85f, $"the {name}'s plot isn't the whole screen ({wide:0.00} x {tall:0.00} of {panel.Size})");
            w.Remove(c);
        }
    }

    private static void Colours(Action<bool, string> want)
    {
        var w = new ConstructWorld();
        foreach (var name in (string[])["Bar Gauge", "Tall Bar Gauge", "Graph Screen"])
        {
            var d = ConstructParts.Need(name);
            var c = ConstructEdits.DropLoose(w, d, new Vector3(0, 10, 0), Quaternion.Identity)!;
            ref var p = ref c.Parts[0];
            p.Reading = 95;
            p.Trace = new float[LogicDisplays.TraceLength];
            for (var k = 0; k < p.Trace.Length; k++)
                p.Trace[k] = k * 100f / p.Trace.Length;
            var slot = d.CfgSlot[(int)CfgKey.Ramp];
            want(slot >= 0 && d.Config[slot].Options.Length == 4, $"the {name} has no colour mode");
            var shots = new Vector3[4][];
            for (var m = 0; m < 4; m++)
            {
                p.Cfg[slot] = m;
                var card = FillCard(w, c, 0);
                want(card.Ramp == (PanelRamp)m, $"the {name}'s mode {m} doesn't reach its card");
                shots[m] = PanelPixels(card, d.Panels[0].Size, d.Panels[0].Rows);
            }
            for (var a = 0; a < 4; a++)
                for (var b = a + 1; b < 4; b++)
                    want(Differ(shots[a], shots[b]), $"the {name}'s colour modes {a} and {b} draw the same");
            want(shots[1].Any(x => x.X > 0.9f && x.Y < 0.4f) && shots[1].Any(x => x.Y > 0.8f && x.X < 0.5f), $"the {name}'s green yellow red has no red or no green");
            w.Remove(c);
        }
        // The colour setting on every display: the pixels change, and to that colour.
        var red = 0xFF2000;
        foreach (var d in ConstructParts.All.Where(d => d.Panels.Length > 0 && d.Panels[0].Source.StartsWith("logic.")))
        {
            var slot = d.CfgSlot[(int)CfgKey.Ink];
            want(slot >= 0, $"the {d.Name} has no colour setting");
            if (slot < 0)
                continue;
            var c = ConstructEdits.DropLoose(w, d, new Vector3(0, 10, 0), Quaternion.Identity)!;
            c.Parts[0].Reading = d.Logic is CLogic.Numpad ? 12 : 60;
            c.Parts[0].Trace = new float[LogicDisplays.TraceLength];
            Array.Fill(c.Parts[0].Trace!, 40f);
            if (d.Panels[0].Source == "logic.sign")
                LogicText.Set(ref c.Parts[0], 0, "HELLO");
            var panel = d.Panels[0];
            var plain = PanelPixels(FillCard(w, c, 0), panel.Size, panel.Rows);
            c.Parts[0].Cfg[slot] = red;
            var inked = FillCard(w, c, 0);
            var px = PanelPixels(inked, panel.Size, panel.Rows);
            want(inked.HasInk && inked.Ink.X > 0.99f && Differ(plain, px), $"the {d.Name}'s colour setting changes nothing");
            w.Remove(c);
        }
    }

    private static void MachineStates(Action<bool, string> want)
    {
        var w = new ConstructWorld();
        var names = RecyclingTable.Current;
        var checks = 0;
        void Says(Construct c, string state, string first, string where)
        {
            var d = c.DefOf(0);
            var card = FillCard(w, c, 0);
            checks++;
            want(card.State.ToString() == state && new string(card.Line(0)) == first, $"{where}: wants {state} \"{first}\", says {CardText(card)}");
            var panel = d.Panels[0];
            var room = PanelRender.Columns(panel.Size, panel.Rows);
            for (var l = 0; l < panel.Rows; l++)
            {
                want(card.Line(l).Length <= room, $"{where}: \"{new string(card.Line(l))}\" runs off the {d.Name}'s screen ({room} characters)");
                foreach (var ch in card.Line(l))
                    want(ch is >= ' ' and <= '~', $"{where}: not ASCII");
            }
        }
        // Every tier and size has its screen.
        foreach (var d in ConstructParts.All.Where(d => d.IsRefiner || d.IsAssembler || d.IsExtruder || d.IsRecycler))
        {
            want(d.Panels.Length == 1 && d.Panels[0].Source == "machine" && d.Panels[0].Range <= 40, $"the {d.Name} has no screen, or one seen too far off");
            want(ConstructConfig.Has(d, CfgKey.Ink), $"the {d.Name}'s screen has no colour setting");
        }
        var smelter = ConstructEdits.DropLoose(w, ConstructParts.Need("Smelter"), new Vector3(0, 10, 0), Quaternion.Identity)!;
        var st = w.StoreOf(smelter, 0)!;
        var iron = names.IdOf("iron ore");
        var metal = names.IdOf("metal");
        Says(smelter, "Idle", "Needs input", "an empty Smelter");
        st.InMaterial = iron;
        st.InAmount = 1;
        Says(smelter, "Wants", "Needs more", "one ore");
        st.InAmount = 100;
        Says(smelter, "Fault", "No power", "ready, no power");
        st.Busy = true;
        Says(smelter, "Working", "Refining", "at work");
        st.Busy = false;
        st.Held = true;
        Says(smelter, "Idle", "Held off", "a Run wire");
        st.Held = false;
        st.Amount = smelter.DefOf(0).BulkCapacity;
        st.Material = metal;
        Says(smelter, "Wants", "Output full", "its output full");
        st.Amount = 5;
        st.Material = names.IdOf("glass");
        Says(smelter, "Wants", "Empty output", "another material out");
        st.Amount = 0;
        st.Material = 0;
        st.InMaterial = names.IdOf("copper ore");
        Says(smelter, "Refused", "Needs tier 2", "copper in a tier 1");
        st.InMaterial = names.IdOf("wood");
        Says(smelter, "Refused", "Wrong input", "wood in a Smelter");
        var foundry = ConstructEdits.DropLoose(w, ConstructParts.Need("Foundry"), new Vector3(9, 10, 0), Quaternion.Identity)!;
        var fs = w.StoreOf(foundry, 0)!;
        w.AddInput(fs, metal, 40);
        w.AddInput(fs, names.IdOf("stone"), 40);
        var fc = FillCard(w, foundry, 0);
        want(new string(fc.Line(2)).Contains("+1"), $"a Foundry's mixed input: {CardText(fc)}");
        var asm = ConstructEdits.DropLoose(w, ConstructParts.Need("Assembler"), new Vector3(-9, 10, 0), Quaternion.Identity)!;
        var ast = w.StoreOf(asm, 0)!;
        ref var ap = ref asm.Parts[0];
        var ad = asm.DefOf(0);
        Says(asm, "Idle", "No recipe", "no recipe");
        var gears = Array.FindIndex(AssemblingTable.Current.Recipes, r => r.Name == "Gear Set");
        ap.Cfg[ad.CfgSlot[(int)CfgKey.Recipe]] = gears + 1;
        Says(asm, "Wants", "Needs input", "nothing in it");
        want(new string(FillCard(w, asm, 0).Line(2)).Contains("steel"), $"what it lacks: {CardText(FillCard(w, asm, 0))}");
        w.AddInput(ast, names.IdOf("steel"), 50);
        Says(asm, "Fault", "No power", "ready, no power");
        ast.Busy = true;
        ast.Progress = 40;
        Says(asm, "Working", "Building", "at work");
        want(MathF.Abs(FillCard(w, asm, 0).Bar - 0.4f) < 0.01f, "the bar is the job's progress");
        var ex = ConstructEdits.DropLoose(w, ConstructParts.Need("Extruder"), new Vector3(0, 10, 12), Quaternion.Identity)!;
        var est = w.StoreOf(ex, 0)!;
        est.Refused = (byte)ExtrudeStall.NoMaterial;
        foreach (var (stall, state, text) in (ValueTuple<ExtrudeStall, string, string>[])[
            (ExtrudeStall.NoPower, "Fault", "No power"), (ExtrudeStall.NoMaterial, "Wants", "Needs material"), (ExtrudeStall.BadMaterial, "Refused", "Wrong material"),
            (ExtrudeStall.Mixed, "Wants", "Other material"), (ExtrudeStall.BadShape, "Refused", "Bad section"), (ExtrudeStall.AtCap, "Wants", "At full size"),
            (ExtrudeStall.Blocked, "Wants", "Blocked"), (ExtrudeStall.TooHeavy, "Wants", "Path too heavy"), (ExtrudeStall.ChuteWaits, "Wants", "Chute waits")])
        {
            est.Refused = (byte)stall;
            Says(ex, state, text, stall.ToString());
        }
        est.Refused = 0;
        est.Busy = true;
        Says(ex, "Working", "Extruding", "at work");
        est.Busy = false;
        Says(ex, "Idle", "Waiting", "stopped");
        var former = ConstructEdits.DropLoose(w, ConstructParts.Need("Wheel Former"), new Vector3(9, 10, 12), Quaternion.Identity)!;
        w.StoreOf(former, 0)!.Busy = true;
        Says(former, "Working", "Forming", "a Wheel Former");
        var rec = ConstructEdits.DropLoose(w, ConstructParts.Need("Recycler"), new Vector3(-9, 10, 12), Quaternion.Identity)!;
        var rst = w.StoreOf(rec, 0)!;
        Says(rec, "Fault", "No power", "no battery");
        w.CreativeRecycling = true;
        Says(rec, "Idle", "Waiting", "nothing in it");
        rst.Busy = true;
        rst.Progress = 25;
        Says(rec, "Working", "Recycling", "at work");
        want(MathF.Abs(FillCard(w, rec, 0).Bar - 0.25f) < 0.01f, "a Recycler's bar is its job");
        rst.Busy = false;
        rst.Amount = rec.DefOf(0).BulkCapacity;
        rst.Material = metal;
        Says(rec, "Wants", "Output full", "a full bin");
        // The screen allocates nothing in a steady state.
        var card = new PanelCard();
        var bodies = new[] { smelter, foundry, asm, ex, former, rec };
        foreach (var b in bodies)
        {
            card.Clear();
            b.DefOf(0).Panels[0].Fill!(w, b, 0, b.DefOf(0).Panels[0], card);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 200; k++)
            foreach (var b in bodies)
            {
                card.Clear();
                b.DefOf(0).Panels[0].Fill!(w, b, 0, b.DefOf(0).Panels[0], card);
            }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        want(bytes == 0, $"machine screens allocate ({bytes} B in 1200 fills)");
        // Progress goes out at once when a job ends, and about once a second while it runs.
        var dirty = w.StoreDirty.Count;
        for (var k = 0; k < 25; k++)
            MachineScreens.SetProgress(w, ast, 41 + k);
        want(w.StoreDirty.Count - dirty <= 3, $"progress is sent too often ({w.StoreDirty.Count - dirty} in 25 steps)");
        MachineScreens.SetProgress(w, ast, 0);
        want(ast.Progress == 0, "a stopped job's progress doesn't clear at once");
        Line($"display screens: {checks} machine states");
    }

    private static void DisplaysSaved(Action<bool, string> want)
    {
        // A blueprint: the step's settings, written from the part and read back.
        var d = ConstructParts.Need("Graph Screen");
        var w = new ConstructWorld();
        var c = ConstructEdits.DropLoose(w, d, new Vector3(0, 10, 0), Quaternion.Identity)!;
        c.Parts[0].Cfg[d.CfgSlot[(int)CfgKey.Ink]] = 0x20FF40;
        c.Parts[0].Cfg[d.CfgSlot[(int)CfgKey.Ramp]] = 3;
        var json = ConstructConfig.ToJson(c.Parts[0], d);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var values = ConstructConfig.ParseValues(d, doc.RootElement, "the test");
        want(values.Any(v => v.Slot == d.CfgSlot[(int)CfgKey.Ink] && (int)v.Value == 0x20FF40) && values.Any(v => v.Slot == d.CfgSlot[(int)CfgKey.Ramp] && (int)v.Value == 3), $"a blueprint's settings: {json}");
        var machine = ConstructParts.Need("Smelter");
        var m = ConstructEdits.DropLoose(w, machine, new Vector3(0, 10, 9), Quaternion.Identity)!;
        m.Parts[0].Cfg[machine.CfgSlot[(int)CfgKey.Ink]] = 0xFF8000;
        using var mdoc = System.Text.Json.JsonDocument.Parse(ConstructConfig.ToJson(m.Parts[0], machine));
        want(ConstructConfig.ParseValues(machine, mdoc.RootElement, "the test").Count == 1, "a machine's screen colour in a blueprint");
        using (var oldDoc = System.Text.Json.JsonDocument.Parse("""{"decimals": 2, "unit": "m"}"""))
            want(ConstructConfig.ParseValues(ConstructParts.Find("Bar Gauge")!, oldDoc.RootElement, "the test").Count == 0, "an older blueprint's decimals and unit on a bar gauge still load");
        // A world save: a real file, read back into another game.
        if (Maps.Find(ConstructYardMap) is not { } yard)
            return;
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        var folder = WorldStore.FolderFor("Display screens test");
        WorldStore.Delete(folder);
        var session = Game.WorldNew("Display screens test", yard, false);
        using (var g = ModeGame(0, GameMode.Sandbox, yard.Id))
        {
            g.WorldAttach(session);
            Run(g, ref now, 0.5);
            if (OpenSpot(g.Constructs) is not { } spot || ConstructBuild.PlaceNew(g.Constructs, d, new FreePlacement { Pos = spot + new Vector3(0, 1, 0), Rot = Quaternion.Identity, Anchor = true }) is not { } s)
            {
                want(false, "no room to place a screen for the save");
                return;
            }
            s.Parts[0].Cfg[d.CfgSlot[(int)CfgKey.Ink]] = 0x20FF40;
            s.Parts[0].Cfg[d.CfgSlot[(int)CfgKey.Ramp]] = 2;
            want(g.WorldSaveNow(true), "the world didn't save");
        }
        var entry = WorldStore.List().FirstOrDefault(e => e.Folder == folder);
        want(entry != null, "the saved world isn't listed");
        if (entry == null || Game.WorldOpen(entry, out var why) is not { } back)
            return;
        using (var g2 = ModeGame(0, GameMode.Sandbox, yard.Id))
        {
            g2.WorldAttach(back);
            Run(g2, ref now, 0.5);
            var found = g2.Constructs.Bodies.FirstOrDefault(b => b.PartCount == 1 && b.DefOf(0) == d);
            want(found != null && (int)ConstructConfig.Get(found.Parts[0], d, CfgKey.Ink, 0) == 0x20FF40 && (int)ConstructConfig.Get(found.Parts[0], d, CfgKey.Ramp, 0) == 2,
                $"a display's colour and colour mode through a world save ({found?.Parts[0].Cfg[d.CfgSlot[(int)CfgKey.Ink]]})");
        }
        WorldStore.Delete(folder);
    }

    private static void DisplaysOnline(Action<bool, string> want)
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
            return;
        const ushort port = 26479;
        var keep = sb.InfiniteParts;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        sb.InfiniteParts = false;
        var dropped = NetSafety.Dropped;
        try
        {
            using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
            server.Mode = GameMode.Sandbox;
            using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Pump(server, [a], 1.2, ref now, clock);
            for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
                Pump(server, [a], 0.25, ref now, clock);
            if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
            {
                want(false, "the client didn't join");
                return;
            }
            if (!sp.Alive)
                server.SelfTestRespawn(sp, now);
            Pump(server, [a], 0.3, ref now, clock);
            server.Net.SimulateBadNetwork(30, 8, 2);
            a.Net.SimulateBadNetwork(30, 8, 2);
            var w = server.Constructs;
            bool Await(Func<bool> done, double seconds = 4)
            {
                var until = clock.Elapsed.TotalSeconds + seconds;
                while (!done() && clock.Elapsed.TotalSeconds < until)
                    Pump(server, [a], 0.05, ref now, clock);
                return done();
            }
            if (OpenSpot(w) is not { } spot)
            {
                want(false, "no open ground");
                return;
            }
            var bp = ConstructBuild.ParseBlueprint("""
                { "format": "call-of-slop-construct-blueprint/1", "name": "Screens", "steps": [
                  { "part": "Concrete Foundation 4x4 m" },
                  { "part": "Assembler", "on": 0, "socket": "t7_7", "by": "bottom" },
                  { "part": "Battery", "on": 0, "socket": "t15_8", "by": "-y" },
                  { "part": "Graph Screen", "on": 0, "socket": "t7_0", "by": "back" }
                ] }
                """, "screens.json");
            if (ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, spot.X, spot.Z), 0, out var why) is not { } c)
            {
                want(false, $"the stand: {why}");
                return;
            }
            c.Owner = sp.Id;
            c.Rebuild();
            if (!Await(() => a.Constructs.Get(c.Id) != null))
            {
                want(false, "the client has no copy");
                return;
            }
            var copy = a.Constructs.Get(c.Id)!;
            var screen = PartNamed(c, "Graph Screen");
            var stand = ConstructRender.PartCentre(c, screen) + new Vector3(0, 0, -1.2f);
            sp.Teleport(ConstructWorld.Ground(World.Query, stand.X, stand.Z) + new Vector3(0, 0.05f, 0));
            a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Configure);
            Pump(server, [a], 0.4, ref now, clock);
            var asm = PartNamed(c, "Assembler");
            var sd = c.DefOf(screen);
            // The client sets a display's colour and colour mode; the host and the client draw the same.
            a.SandboxConfigure(c.Id, [(ushort)screen], [(byte)sd.CfgSlot[(int)CfgKey.Ink], (byte)sd.CfgSlot[(int)CfgKey.Ramp]], [0x20FF40, 1]);
            var set = Await(() => (int)ConstructConfig.Get(c.Parts[screen], sd, CfgKey.Ink, 0) == 0x20FF40 && (int)ConstructConfig.Get(copy.Parts[screen], sd, CfgKey.Ink, 0) == 0x20FF40 && (int)ConstructConfig.Get(copy.Parts[screen], sd, CfgKey.Ramp, 0) == 1);
            want(set, $"a display's colour through the client's Configure (host {c.Parts[screen].Cfg[sd.CfgSlot[(int)CfgKey.Ink]]}, client {copy.Parts[screen].Cfg[sd.CfgSlot[(int)CfgKey.Ink]]}; \"{a.SandboxNotice}\")");
            want(FillCard(a.Constructs, copy, screen).Ramp == PanelRamp.Level && FillCard(a.Constructs, copy, screen).Ink.Y > 0.9f, "the client's screen doesn't draw them");
            // A machine: every state the same on both peers, and the job's progress on the wire.
            var ad = c.DefOf(asm);
            string Host() => CardText(FillCard(w, c, asm));
            string Client() => CardText(FillCard(a.Constructs, copy, asm));
            want(Await(() => Host() == Client()), $"an idle machine's screen: host {Host()}, client {Client()}");
            var gears = Array.FindIndex(AssemblingTable.Current.Recipes, r => r.Name == "Gear Set");
            a.SandboxConfigure(c.Id, [(ushort)asm], [(byte)ad.CfgSlot[(int)CfgKey.Recipe]], [gears + 1]);
            want(Await(() => Host().Contains("Needs input") && Host() == Client()), $"no input: host {Host()}, client {Client()}");
            var st = w.StoreOf(c, asm)!;
            w.AddInput(st, RecyclingTable.Current.IdOf("steel"), 50);
            st.Version++;
            w.StoreDirty.Add(st.Handle);
            var going = Await(() => Host().Contains("Building") && Client().Contains("Building") && a.Constructs.StoreOf(copy, asm)!.Progress > 0, 6);
            Line($"display screens: online: host {Host()} ({st.Progress} %), client {Client()} ({a.Constructs.StoreOf(copy, asm)?.Progress} %)");
            want(going, $"a job's progress doesn't reach the client (host {Host()} {st.Progress}, client {Client()} {a.Constructs.StoreOf(copy, asm)?.Progress})");
            want(FillCard(a.Constructs, copy, asm).Bar > 0, "the client's bar stands still");
            if (NetSafety.Dropped != dropped)
                want(false, $"{NetSafety.Dropped - dropped} packets dropped");
        }
        finally
        {
            sb.InfiniteParts = keep;
            yard.RestoreProps();
        }
    }
}
