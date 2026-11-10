using System.Diagnostics;
using System.Numerics;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// "Terrain sculpting" and "Terrain sculpting network": the ground drill (docs/sandbox/TERRAIN-DIGGING.md, "Sculpting"). Fills on
/// undug, dug and streamed ground read back through every query, draw and collide; a dig and a fill balance; what each ground
/// gives; walking over it all with both movers; the real input path (the game's wheel, LMB, RMB, a stationary hold, a sweep,
/// every refusal, the server's rate, the log's cap); and a real host, a client and a late joiner on their own copies of the terrain ending
/// with identical ground.
/// </summary>
public static partial class SelfTest
{
    private const string SculptLabJson = """
        {
          "format": "call-of-slop-map/1",
          "name": "Sculpt Lab",
          "halfX": 30,
          "halfZ": 30,
          "terrain": {
            "cell": 1,
            "margin": 8,
            "features": [
              { "type": "hill", "x": -15, "z": -15, "radius": 12, "height": 0.01, "material": "sand" },
              { "type": "hill", "x": 15, "z": -15, "radius": 12, "height": 0.01, "material": "rock" },
              { "type": "hill", "x": -15, "z": 15, "radius": 12, "height": 0.01, "material": "dirt" },
              { "type": "hill", "x": 18, "z": 18, "radius": 8, "height": 3 }
            ]
          },
          "ops": [
            { "op": "Ground", "color": [0.36, 0.5, 0.25] },
            { "op": "RingSpawns", "count": 8, "frac": 0.75 }
          ]
        }
        """;

    private static Map SculptLab()
    {
        var m = MapFile.Load(SculptLabJson, "sculpt lab");
        m.Validate();
        return m;
    }

    private static void SculptLine(string s) => Line("  " + s);

    private static void CheckTerrainSculpt()
    {
        var previous = World.Map;
        var previousMover = World.Mover;
        try
        {
            SculptBasics();
            SculptGround();
            SculptOnMap("Proving Ground");
            SculptOnMap("Streamed Terrain");
            SculptLog();
            World.Mover = ClassicMover.Instance;
            SculptWalk();
            World.Mover = SweptMover.Instance;
            SculptWalk();
            World.Mover = ClassicMover.Instance;
            SculptInput();
        }
        finally
        {
            World.Mover = previousMover;
            World.SetMap(previous);
            DiggingTable.Use(null);
        }
    }

    private static void SculptBasics()
    {
        var slot = (int)SandboxToolWheel.Slot.GroundDrill;
        var wheel = new SandboxToolWheel();
        var model = ConstructTools.For(ConstructTool.GroundDrill);
        var byteBack = ConstructToolWire.From((byte)ConstructTool.GroundDrill);
        if (SandboxToolWheel.ToolOf(slot) != ConstructTool.GroundDrill || SandboxToolWheel.SlotOf(ConstructTool.GroundDrill) != slot || !wheel.Menu.Items[slot].Enabled
            || byteBack != ConstructTool.GroundDrill || model is not { Key: "drill" })
            Fail($"ground drill: the wheel slot, wire byte ({byteBack}) or held model is wrong");
        var t = DiggingTable.Load(DiggingTable.FilePath);
        if (t.Problem.Length > 0)
            Fail($"ground drill: Digging.json refused: {t.Problem}");
        foreach (var (json, want) in new[]
                 {
                     ("{\"format\": \"call-of-slop-digging/1\", \"reech\": 4}", "unknown key"),
                     ("{\"format\": \"call-of-slop-digging/1\", \"reach\": 400}", "\"reach\""),
                     ("{\"format\": \"call-of-slop-digging/1\", \"ground\": {\"rock\": \"cheese\"}}", "dirt, stone or sand"),
                     ("{\"format\": \"nope\"}", "format"),
                 })
            if (!DiggingTable.Parse(json).Problem.Contains(want))
                Fail($"ground drill: a bad table wasn't refused for \"{want}\": \"{DiggingTable.Parse(json).Problem}\"");
        foreach (var k in DiggingTable.KindNames)
            if (RecyclingTable.Current.IdOf(k) == 0)
                Fail($"ground drill: the Recycler has no material \"{k}\"");
        // The ring: Dig, the default mode, straight out from the tool (the 2026-11-02 lesson); dirt the default material.
        var ring = new SandboxSculptWheel();
        const int n = SandboxToolWheel.DefaultTools;
        var middle = (ring.Options.Length - 1) / 2;
        var straight = MathF.Abs(RadialMenu.OptionAngle(middle, slot, n, ring.Options.Length) - RadialMenu.SegmentAngle(slot, n)) < 1e-4f;
        if (ring.Options.Length != 7 || ring.CurrentOption != middle || ring.Kind != GroundKind.Dirt || ring.Mode != SandboxSculptWheel.DrillMode.Dig || ring.Options[middle].Label != "Dig" || !straight)
            Fail($"ground drill: the ring's default isn't the middle of its arc ({ring.Options.Length} options, current {ring.CurrentOption}, straight out {straight})");
        SculptLine($"ground drill: wheel slot {slot}, wire byte {(byte)ConstructTool.GroundDrill}, model {model?.Boxes.Length} boxes; table reach {t.Reach} m, scoop {t.ScoopRadius} m, repeat {t.RepeatSeconds} s, stone under {t.StoneDepth} m, log cap {t.LogCap} of {TerrainEdits.MaxOps}");
    }

    private static float Vol(TerrainEdits e, in DigOp op, Span<float> by)
    {
        var table = DiggingTable.Current;
        return e.Measure(in op, table.Ground, table.StoneDepth, by);
    }

    /// <summary>Every face of the dug cells near (x, z) against the collision and the drawn shape.</summary>
    private static void SculptFaces(TerrainData t, float x, float z, float reach, string what)
    {
        var plain = new DigFaceCheck { T = t };
        var drawn = new DrawnFaceCheck { E = t.Edits };
        t.Locate(x - reach, z - reach, out var i0, out var j0, out _, out _);
        t.Locate(x + reach, z + reach, out var i1, out var j1, out _, out _);
        for (var j = j0; j <= j1; j++)
        for (var i = i0; i <= i1; i++)
        {
            t.Edits.CellFaces(i, j, ref plain);
            t.Edits.CellFaces(i, j, ref drawn, smooth: true);
        }
        if (plain.Wrong > 0 || drawn.Wrong > 0 || plain.Faces == 0)
            Fail($"sculpting ({what}): {plain.Wrong} of {plain.Faces} faces disagree with the collision (first: {plain.First}), {drawn.Wrong} of {drawn.Faces} smoothed ones with the drawn shape (first: {drawn.First})");
    }

    private struct TopColour : IDigFaceSink
    {
        public Vector3 Want, Got;
        public bool Found;

        public void Smooth(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Vector4 ca, Vector4 cb, Vector4 cc, Vector4 cd) =>
            Quad(a, b, c, d, Vector3.Normalize(Vector3.Cross(c - a, d - b)), new Vector3(ca.X, ca.Y, ca.Z), ca.W);

        public bool Shaded => true;


        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector3 color, float rock)
        {
            if (n.Y > 0.5f && !Found && MathF.Abs((a.X + c.X) * 0.5f - Want.X) < 0.5f && MathF.Abs((a.Z + c.Z) * 0.5f - Want.Z) < 0.5f && a.Y > Want.Y)
            {
                Got = color;
                Found = true;
            }
        }
    }

    private static void SculptGround()
    {
        var m = SculptLab();
        World.SetMap(m);
        var t = m.Terrain!;
        var tc = m.TerrainCollision!;
        var e = t.Edits;
        Span<float> by = stackalloc float[(int)GroundKind.COUNT];
        var h0 = t.Sample(0, 0);

        // A fill on undug ground: every query reads it back, it stands over the map's highest sample.
        var fill = new DigOp(DigKind.FillDirt, new Vector3(0.1f, h0, 0.1f), 1f).Quantised;
        var measured = Vol(e, in fill, by);
        var sumBefore = 0f;
        for (var z = -2f; z < 2f; z += e.Size)
        for (var x = -2f; x < 2f; x += e.Size)
            sumBefore += t.Sample(x, z);
        if (e.Apply(fill) == 0)
            Fail("sculpting: a fill on undug ground changed nothing");
        var sumAfter = 0f;
        for (var z = -2f; z < 2f; z += e.Size)
        for (var x = -2f; x < 2f; x += e.Size)
            sumAfter += t.Sample(x, z);
        var built = (sumAfter - sumBefore) * e.Size * e.Size;
        var top = t.Sample(0.1f, 0.1f);
        var down = tc.Raycast(new Vector3(0.1f, h0 + 10, 0.1f), -Vector3.UnitY, 30);
        var across = tc.Raycast(new Vector3(-3, h0 + 0.3f, 0.1f), Vector3.UnitX, 8);
        SculptLine($"fill on undug ground: {measured:0.00} m3 measured, {built:0.00} m3 built (a half ball is {MathF.PI * 2 / 3:0.00}); top {top - h0:0.00} m up, highest ground {t.HighestGround - t.MaxHeight:0.00} m over the map's");
        if (MathF.Abs(measured - built) > 0.02f || MathF.Abs(measured - 2.09f) > 0.35f)
            Fail($"sculpting: a fill measured {measured:0.00} m3 and built {built:0.00} m3");
        if (top < h0 + 0.9f || !t.SolidAt(0.1f, 0.1f, h0 + 0.5f) || t.SolidAt(0.1f, 0.1f, top + 0.1f) || MathF.Abs(t.GroundBelow(0.1f, 0.1f, top + 2) - top) > 1e-4f)
            Fail("sculpting: the queries don't read a fill back");
        if (MathF.Abs(h0 + 10 - down - top) > 0.06f || t.HighestGround < top)
            Fail($"sculpting: a ray from above stopped {h0 + 10 - down - top:0.00} m off a fill over the map's highest ground");
        if (across is < 1.8f or > 2.5f)
            Fail($"sculpting: a ray across a fill met it {across:0.00} m out, want about 2");
        SculptFaces(t, 0, 0, 3, "undug");

        // Stacked on dug ground: no air inside, a second fill on the first one too.
        var pit = new Vector3(8, h0, 0);
        e.Apply(new DigOp(DigKind.Scoop, pit, 1.5f).Quantised);
        var floor = t.Sample(8.1f, 0.1f);
        var stack = new DigOp(DigKind.FillStone, new Vector3(8.1f, floor, 0.1f), 1f).Quantised;
        e.Apply(stack);
        e.Apply(new DigOp(DigKind.FillStone, new Vector3(8.1f, t.Sample(8.1f, 0.1f), 0.1f), 1f).Quantised);
        var stacked = t.Sample(8.1f, 0.1f);
        e.TryColumn(8.1f, 0.1f, out var sp);
        if (stacked < floor + 1.5f || sp.Length != 2)
            Fail($"sculpting: stacked fills reach {stacked - floor:0.00} m over the floor in {sp.Length / 2} spans, want over 1.5 m in one");
        SculptFaces(t, 8, 0, 4, "stacked");

        // A fill reads as what it was made of, drawn and dug.
        foreach (var (kind, spot, want) in new[] { (DigKind.FillDirt, -8f, t.Surface.Dirt), (DigKind.FillStone, -12f, t.Surface.Rock), (DigKind.FillSand, -16f, t.Surface.Sand) })
        {
            var at = new Vector3(spot, h0, 0.1f);
            e.Apply(new DigOp(kind, at, 1f).Quantised);
            var colour = new TopColour { Want = at };
            t.Locate(spot, 0.1f, out var i, out var j, out _, out _);
            e.CellFaces(i, j, ref colour);
            var dig = new DigOp(DigKind.Scoop, at + new Vector3(0, 1.1f, 0), 0.6f).Quantised;
            Vol(e, in dig, by);
            if (!colour.Found || Vector3.Distance(colour.Got, want) > 0.01f || by[kind - DigKind.FillDirt] <= 0 || by[(kind - DigKind.FillDirt + 1) % 3] > 0)
                Fail($"sculpting: a {kind} draws {colour.Got} (want {want}) and digs back as {by[0]:0.00} / {by[1]:0.00} / {by[2]:0.00}");
        }

        // A slope: the ball's downhill side reaches the ground, no air under it.
        var hill = new Vector3(13, 0, 18);
        hill.Y = t.Sample(hill.X, hill.Z);
        e.Apply(new DigOp(DigKind.FillDirt, hill, 1f).Quantised);
        var hollow = 0;
        for (var z = hill.Z - 1.2f; z <= hill.Z + 1.2f; z += e.Size)
        for (var x = hill.X - 1.2f; x <= hill.X + 1.2f; x += e.Size)
            if (e.TryColumn(x, z, out var s) && s.Length > 2)
                hollow++;
        if (hollow > 0)
            Fail($"sculpting: a fill on a slope left {hollow} columns with air under it");
        SculptFaces(t, hill.X, hill.Z, 3, "slope");

        // A pit roofed over: a ceiling, a span in the air, faces right. The column cap holds.
        var roofAt = new Vector3(-20, h0, 8);
        for (var k = 0; k < 5; k++)
            e.Apply(new DigOp(DigKind.Scoop, roofAt - new Vector3(0, k * 0.8f, 0), 1.2f).Quantised);
        e.Apply(new DigOp(DigKind.FillDirt, roofAt + new Vector3(0, -0.2f, 0), 1.1f).Quantised);
        var ceiling = t.CeilingAbove(roofAt.X, roofAt.Z, h0 - 3f);
        if (!(ceiling < h0) || !t.SolidAt(roofAt.X, roofAt.Z, h0) || t.SolidAt(roofAt.X, roofAt.Z, ceiling - 0.2f))
            Fail($"sculpting: a roof over a pit reads ceiling {ceiling - h0:0.00} m");
        SculptFaces(t, roofAt.X, roofAt.Z, 3, "roof");
        var column = new Vector3(20, h0, 8);
        for (var k = 0; k < TerrainEdits.MaxSpans * 2; k++)
            e.Apply(new DigOp(DigKind.FillSand, column + new Vector3(0, 3 + k * 1.2f, 0), 0.4f).Quantised);
        e.TryColumn(column.X, column.Z, out var tall);
        if (tall.Length / 2 > TerrainEdits.MaxSpans)
            Fail($"sculpting: a column has {tall.Length / 2} spans, more than {TerrainEdits.MaxSpans}");

        // Measuring and querying allocate nothing.
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        var sum = 0f;
        var probe = new DigOp(DigKind.FillStone, new Vector3(4, h0, 7), 0.8f).Quantised;
        var dig2 = new DigOp(DigKind.Scoop, new Vector3(4, h0, 7), 0.8f).Quantised;
        for (var k = 0; k < 200; k++)
            sum += Vol(e, in probe, by) + Vol(e, in dig2, by) + (e.FillTouches(in probe, new Vector3(3.5f, h0, 6.5f), new Vector3(4.5f, h0 + 2, 7.5f)) ? 1 : 0);
        var alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        if (alloc > 0)
            Fail($"sculpting: measuring allocated {alloc} B ({sum:0})");

        // Over the wire as a joiner gets it: the same ground.
        var copy = SculptLab();
        var w = new PacketWriter();
        Game.WriteTerrainDigState(w, e, pw =>
        {
            var r = new PacketReader(pw.Span[1..]);
            Game.ApplyTerrainDig(copy.Terrain!, ref r);
        });
        var differ = SculptDiffer(t, copy.Terrain!, -29, 29, h0 - 8, h0 + 12);
        if (differ > 0 || copy.Terrain!.Edits.Log.Count != e.Log.Count)
            Fail($"sculpting: a copy given the log over the wire differs at {differ} points ({copy.Terrain!.Edits.Log.Count} of {e.Log.Count} ops)");

        // The match restarts: all gone.
        m.RestoreProps();
        if (t.IsDug || t.HighestGround != t.MaxHeight || MathF.Abs(t.Sample(0.1f, 0.1f) - h0) > 1e-4f)
            Fail("sculpting: restoring the map didn't take the fills away");
        SculptLine($"stacked fill {stacked - floor:0.00} m over its floor; a fill reads back and digs as its own kind, on a slope no air under it, a roof over a pit, copies over the wire: {differ} points differ");
    }

    /// <summary>Points where two terrains differ (solid or not, then ground height), over a square.</summary>
    private static int SculptDiffer(TerrainData a, TerrainData b, float lo, float hi, float y0, float y1, float cx = 0, float cz = 0, float step = 0.37f)
    {
        var differ = 0;
        for (var z = lo; z <= hi; z += step)
        for (var x = lo; x <= hi; x += step)
        {
            if (a.Sample(cx + x, cz + z) != b.Sample(cx + x, cz + z))
                differ++;
            for (var y = y0; y <= y1; y += 0.5f)
                if (a.SolidAt(cx + x, cz + z, y) != b.SolidAt(cx + x, cz + z, y))
                    differ++;
        }
        return differ;
    }

    /// <summary>A dig and a fill on a real map (2 m cells; streamed tiles too): they read back and draw.</summary>
    private static void SculptOnMap(string name)
    {
        if (Maps.Find(name) is not { } m || m.Terrain is not { } t)
        {
            Fail($"sculpting: the map {name} is missing");
            return;
        }
        m.RestoreProps();
        World.SetMap(m);
        var tc = m.TerrainCollision!;
        var spawn = m.Spawns.Count > 0 ? m.Spawns[0] : Vector3.Zero;
        var at = new Vector3(spawn.X + 6, 0, spawn.Z + 6);
        at.Y = t.Sample(at.X, at.Z);
        var e = t.Edits;
        e.Apply(new DigOp(DigKind.FillStone, at, 1.5f).Quantised);
        var top = t.Sample(at.X + 0.1f, at.Z + 0.1f);
        var down = tc.Raycast(new Vector3(at.X + 0.1f, top + 5, at.Z + 0.1f), -Vector3.UnitY, 20);
        var dug = at + new Vector3(4, 0, 0);
        dug.Y = t.Sample(dug.X, dug.Z);
        e.Apply(new DigOp(DigKind.Scoop, dug, 1.5f).Quantised);
        var hole = t.Sample(dug.X + 0.1f, dug.Z + 0.1f);
        SculptLine($"{name}: fill {top - at.Y:0.00} m up, scoop {dug.Y - hole:0.00} m down, {e.TouchedCells} cells of {e.Sub} x {e.Sub} columns");
        if (top < at.Y + 1.2f || MathF.Abs(top + 5 - down - top) > 0.06f || dug.Y - hole < 1.2f)
            Fail($"sculpting on {name}: the fill stands {top - at.Y:0.00} m, a ray stopped {top + 5 - down - top:0.00} m off it, the scoop went {dug.Y - hole:0.00} m");
        SculptFaces(t, at.X + 2, at.Z, 5, name);
        m.RestoreProps();
    }

    /// <summary>A long session's log: its rate, what a joiner downloads and replays.</summary>
    private static void SculptLog()
    {
        var m = SculptLab();
        var t = m.Terrain!;
        var e = t.Edits;
        var h0 = t.Sample(0, 0);
        var rng = new Random(11);
        const int ops = 6000;
        var list = new DigOp[ops];
        for (var k = 0; k < ops; k++)
        {
            var at = new Vector3(rng.NextSingle() * 50 - 25, h0 + rng.NextSingle() * 3 - 2, rng.NextSingle() * 50 - 25);
            list[k] = new DigOp(k % 3 == 0 ? DigKind.FillDirt : DigKind.Scoop, at, 0.8f).Quantised;
        }
        var sw = Stopwatch.StartNew();
        foreach (var op in list)
            e.Apply(op);
        var us = sw.Elapsed.TotalMilliseconds * 1000 / ops;
        var table = DiggingTable.Current;
        var capMs = us * table.LogCap / 1000;
        var hours = table.LogCap * table.RepeatSeconds / 3600;
        SculptLine($"log: an op applies in {us:0.0} us, so {table.LogCap} ops replay in {capMs:0} ms; {table.LogCap * DigOp.WireBytes / 1024} KB to a joiner; {hours:0.0} hours of one player's held button, {hours * 60 / 4:0} minutes of four players'");
        if (us > 150)
            Fail($"sculpting: an op took {us:0.0} us (budget 150)");
    }

    /// <summary>Over a heap, across a trench and into a roofed gap: both movers, no head ever in the ground.</summary>
    private static void SculptWalk()
    {
        var m = SculptLab();
        World.SetMap(m);
        var t = m.Terrain!;
        var e = t.Edits;
        var h0 = t.Sample(0, 0);
        const float r = 0.8f;
        // A bank across the way (x = 0), a trench (x = 6), a fill stair down into the trench's far side, a roofed stretch (x = 12).
        for (var z = -6f; z <= 6f; z += 0.8f)
        {
            e.Apply(new DigOp(DigKind.FillDirt, new Vector3(0, h0, z), r).Quantised);
            e.Apply(new DigOp(DigKind.Scoop, new Vector3(6, h0, z), r).Quantised);
            e.Apply(new DigOp(DigKind.Scoop, new Vector3(12, h0 - 1.2f, z), 1.2f).Quantised);
            e.Apply(new DigOp(DigKind.Scoop, new Vector3(13.2f, h0 - 0.6f, z), 1.2f).Quantised);
            e.Apply(new DigOp(DigKind.Scoop, new Vector3(14.4f, h0, z), 1.2f).Quantised);
        }
        // Fills lower in the roofed cut make low lips a standing head must not go into.
        for (var z = -6f; z <= 6f; z += 1.6f)
            e.Apply(new DigOp(DigKind.FillDirt, new Vector3(12, h0 - 0.1f, z), 0.6f).Quantised);
        var wedged = 0;
        var first = "";
        var overBank = 0;
        var reached = 0;
        var runs = 0;
        void Wedge(in MoveState s)
        {
            var feet = s.Position;
            var head = feet.Y + World.HeightOf(s.Stance);
            var ceiling = t.CeilingAbove(feet.X, feet.Z, feet.Y + 0.05f);
            if (head <= ceiling + 0.05f)
                return;
            wedged++;
            if (first.Length == 0)
                first = $"head in the ground at ({feet.X:0.00}, {feet.Y:0.00}, {feet.Z:0.00}), {s.Stance}, ceiling {ceiling:0.00}";
        }
        for (var z = -4f; z <= 4f; z += 2f)
        {
            runs++;
            var st = new MoveState { Position = new Vector3(-6, t.Sample(-6, z), z), Grounded = true };
            var yaw = MathF.PI / 2; // (+X)
            var dir = World.FlatForward(yaw);
            if (dir.X < 0)
                yaw = -yaw;
            var high = float.MinValue;
            for (var k = 0; k < 8 * World.TickRate; k++)
            {
                World.Simulate(ref st, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint | (k % 20 < 2 ? Buttons.Jump : Buttons.None), Yaw = yaw, SpeedPct = 100 });
                Wedge(st);
                if (MathF.Abs(st.Position.X) < 0.4f)
                    high = MathF.Max(high, st.Position.Y);
                if (st.Position.X > 8)
                    break;
            }
            if (high > h0 + 0.6f)
                overBank++;
            if (st.Position.X > 8)
                reached++;
            // Into the roofed stretch and back, crouched or not.
            for (var k = 0; k < 3 * World.TickRate; k++)
            {
                World.Simulate(ref st, new InputCmd { Buttons = Buttons.Forward | (k % 30 < 2 ? Buttons.Jump : Buttons.None), Yaw = yaw, SpeedPct = 100 });
                Wedge(st);
            }
        }
        SculptLine($"{World.Mover.GetType().Name}: {overBank} of {runs} walked up over a bank of fill, {reached} crossed the trench, {wedged} ticks with a head in the ground");
        if (overBank < runs || reached < runs || wedged > 0)
            Fail($"sculpting ({World.Mover.GetType().Name}): {overBank} of {runs} went over the bank, {reached} crossed the trench, {wedged} ticks with a head in the ground ({first})");
    }

    /// <summary>The look at a point from the player's eye.</summary>
    private static void SculptLook(Game g, Player me, Vector3 at)
    {
        var dir = Vector3.Normalize(at - me.RenderEye);
        g.LookPitch = MathF.Asin(Math.Clamp(dir.Y, -1f, 1f));
        g.LookYaw = MathF.Atan2(dir.X, -dir.Z);
    }

    /// <summary>The real input offline: the wheel, LMB, RMB, holds and sweeps, every refusal. Survival rules (the map's infinite parts off), then creative.</summary>
    private static void SculptInput()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb || yard.Terrain is not { } t)
        {
            Fail($"sculpting: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        var wasInfinite = sb.InfiniteParts;
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        if (SpawnLocalSafely(g, ref now) is { } notReady)
        {
            Fail($"sculpting: {notReady}");
            return;
        }
        var me = g.Local!;
        sb.InfiniteParts = false;
        Program.SweepRelease(g);
        var table = DiggingTable.Current;
        void Frame(ReadOnlySpan<GameAction> keys, double2 mouse = default)
        {
            Program.SweepFrame(g, now, keys, mouse);
            now += 1.0 / 60;
            g.Update(now, 1.0 / 60);
        }
        var inv = g.SandboxInventory(me.Id);
        var dirtId = Game.MaterialOf(GroundKind.Dirt);
        var stoneId = Game.MaterialOf(GroundKind.Stone);
        var sandId = Game.MaterialOf(GroundKind.Sand);
        ReadOnlySpan<GameAction> lmb = [GameAction.Fire];
        ReadOnlySpan<GameAction> rmb = [GameAction.Aim];
        ReadOnlySpan<GameAction> both = [GameAction.Fire, GameAction.Aim];
        try
        {
            // The game's own wheel: a flick to the ground drill.
            var wheel = Program.ToolWheelTest;
            ReadOnlySpan<GameAction> mmb = [GameAction.ToolWheel];
            var slot = (int)SandboxToolWheel.Slot.GroundDrill;
            var angle = RadialMenu.SegmentAngle(slot, SandboxToolWheel.DefaultTools);
            Frame(mmb);
            for (var k = 0; k < 4; k++)
                Frame(mmb, new double2(MathF.Sin(angle) * 15, -MathF.Cos(angle) * 15));
            Frame([]);
            Run(g, ref now, 0.1);
            if (g.SandboxHands.Hand(me.Id).Tool != ConstructTool.GroundDrill)
                Fail($"sculpting: a flick on the tool wheel took out {g.SandboxHands.Hand(me.Id).Tool}, want the ground drill");
            if (wheel.Menu.Items[slot].Options == null || wheel.Menu.Items[slot].OptionCurrent != 3)
                Fail("sculpting: the wheel's ground drill has no ring, or it isn't on Dig");
            if (OpenSpot(g.Constructs) is not { } spot)
            {
                Fail("sculpting: no open spot on the yard");
                return;
            }
            var stand = spot + new Vector3(0, 0.02f, -6);
            me.Teleport(stand with { Y = t.Sample(stand.X, stand.Z) + 0.02f });
            Run(g, ref now, 0.3);
            var target = new Vector3(stand.X + 2.2f, 0, stand.Z);
            target.Y = t.Sample(target.X, target.Z);
            var ops0 = g.DigOps;

            // A click digs one scoop and gives what the ground lost.
            SculptLook(g, me, target);
            Frame([]);
            Span<float> by = stackalloc float[(int)GroundKind.COUNT];
            var dig = new DigOp(DigKind.Scoop, target, table.ScoopRadius).Quantised;
            var expect = Vol(t.Edits, in dig, by) * table.Units[0];
            Frame(lmb);
            Frame([]);
            var click = g.DigOps - ops0;
            var carried = inv.Materials[dirtId];
            SculptLine($"one click: {click} op, {carried} dirt (ground lost {expect:0.0} units), hint \"{g.SandboxSculptHint}\"");
            if (click != 1 || MathF.Abs(carried - expect) > 1.01f || !g.SandboxSculptHint.Contains($"dirt {carried}"))
                Fail($"sculpting: a click made {click} ops and gave {carried} dirt (want one and {expect:0.0}); HUD \"{g.SandboxSculptHint}\"");

            // Held still: it repeats at the rate, deeper, and no faster.
            Run(g, ref now, 0.4);
            var before = g.DigOps;
            var floorBefore = t.Sample(target.X, target.Z);
            for (var k = 0; k < 120; k++)
                Frame(lmb);
            Frame([]);
            var held = g.DigOps - before;
            var most = (int)MathF.Ceiling(2f / table.RepeatSeconds) + 1;
            SculptLine($"LMB held still 2 s: {held} ops (at most {most}), ground {floorBefore - t.Sample(target.X, target.Z):0.00} m lower");
            if (held < 3 || held > most || t.Sample(target.X, target.Z) >= floorBefore)
                Fail($"sculpting: a held LMB made {held} ops in 2 s, at most {most}");

            // RMB: a click fills once and costs what it built; held still it never stacks on itself.
            inv.Materials[dirtId] = 500;
            var fillAt = new Vector3(stand.X - 2.2f, 0, stand.Z);
            fillAt.Y = t.Sample(fillAt.X, fillAt.Z);
            SculptLook(g, me, fillAt);
            Frame([]);
            Run(g, ref now, 0.4);
            before = g.DigOps;
            var dirtBefore = inv.Materials[dirtId];
            var groundBefore = t.Sample(fillAt.X, fillAt.Z);
            for (var k = 0; k < 120; k++)
                Frame(rmb);
            Frame([]);
            var stationary = g.DigOps - before;
            var spent = dirtBefore - inv.Materials[dirtId];
            SculptLine($"RMB held still 2 s: {stationary} op, {spent} dirt spent, ground {t.Sample(fillAt.X, fillAt.Z) - groundBefore:0.00} m higher");
            if (stationary != 1 || spent <= 0 || t.Sample(fillAt.X, fillAt.Z) <= groundBefore + 0.3f)
                Fail($"sculpting: RMB held still made {stationary} ops and spent {spent} (want exactly one fill)");

            // A sweep lays a line: one fill a spot, the rate holding.
            before = g.DigOps;
            var spots = 0;
            for (var k = 0; k < 7; k++)
            {
                var turn = 1.9f + k * 0.4f; // (an arc round the player, 1.2 m a step, in reach)
                var p = new Vector3(stand.X + MathF.Cos(turn) * 3f, 0, stand.Z + MathF.Sin(turn) * 3f);
                p.Y = t.Sample(p.X, p.Z);
                SculptLook(g, me, p);
                for (var f = 0; f < 25; f++)
                    Frame(rmb);
                spots++;
            }
            Frame([]);
            var swept = g.DigOps - before;
            SculptLine($"RMB swept over {spots} spots: {swept} fills");
            if (swept < 3 || swept > spots)
                Fail($"sculpting: a sweep over {spots} spots made {swept} fills");

            // Both buttons: dig wins.
            Run(g, ref now, 0.4);
            var dirt0 = inv.Materials[dirtId];
            SculptLook(g, me, target);
            Frame(both);
            Frame([]);
            if (inv.Materials[dirtId] < dirt0)
                Fail("sculpting: with both buttons down a fill went first");

            // Refusals, each with its reason on the HUD, and nothing changed.
            void Refused(string what, string want, Action act)
            {
                Run(g, ref now, 0.4);
                g.SandboxSay("");
                var opsThen = g.DigOps;
                var dirtThen = inv.Materials[dirtId];
                act();
                Frame([]);
                var notice = g.SandboxNotice;
                SculptLine($"refused ({what}): \"{notice}\"");
                if (g.DigOps != opsThen || inv.Materials[dirtId] != dirtThen || !notice.Contains(want))
                    Fail($"sculpting: {what}: {g.DigOps - opsThen} ops, dirt {inv.Materials[dirtId] - dirtThen}, notice \"{notice}\" (want \"{want}\")");
            }
            void Click(ReadOnlySpan<GameAction> key)
            {
                Frame(key);
                Frame([]);
            }
            var away = new Vector3(stand.X, 0, stand.Z - 2.4f);
            away.Y = t.Sample(away.X, away.Z);
            var near = new Vector3(stand.X + 0.7f, 0, stand.Z);
            near.Y = t.Sample(near.X, near.Z);
            SculptLook(g, me, near);
            Refused("over your own feet", "bury", () => Click([GameAction.Aim]));
            var beam = ConstructBuild.PlaceNew(g.Constructs, ConstructParts.Need("Square Beam 0.5 m"), away + new Vector3(0.6f, 0, 0), 0);
            beam?.Rebuild();
            Run(g, ref now, 0.3);
            SculptLook(g, me, away);
            Refused("a construct in the way", "construct", () => Click([GameAction.Aim]));
            // A loose body resting on the ground wakes and falls when it is dug from under it.
            if (beam != null)
            {
                for (var k = 0; k < 300 && !beam.Asleep; k++)
                    Run(g, ref now, 0.02);
                var y0 = beam.WorldCom.Y;
                var wasAsleep = beam.Asleep;
                var under = beam.WorldCom with { Y = t.Sample(beam.WorldCom.X, beam.WorldCom.Z) };
                g.SandboxSculpt(DigKind.Scoop, under);
                var awake = !beam.Asleep;
                Run(g, ref now, 1.0);
                SculptLine($"a resting body (asleep {wasAsleep}) dug out from under: awake {awake}, fell {y0 - beam.WorldCom.Y:0.00} m");
                if (!awake || y0 - beam.WorldCom.Y < 0.2f)
                    Fail($"sculpting: a resting body dug out from under: awake {awake}, fell {y0 - beam.WorldCom.Y:0.00} m");
                g.Constructs.Remove(beam);
            }
            Run(g, ref now, 0.4);
            var far = away + new Vector3(0, 0, -30);
            Refused("too far", "too far", () => g.SandboxSculpt(DigKind.Scoop, far));
            g.DestructionEnabled = false;
            Refused("Destruction off", "Destruction", () => g.SandboxSculpt(DigKind.Scoop, away));
            g.DestructionEnabled = true;
            var sky = stand + new Vector3(1, 8, 0);
            SculptLook(g, me, away);
            var keep = inv.Materials[dirtId];
            inv.Materials[dirtId] = 0;
            inv.AddBulk(dirtId, 0.5f);
            Refused("nothing carried", "not enough dirt", () => Click([GameAction.Aim]));
            inv.Materials[dirtId] = keep;
            inv.Materials[dirtId] = SandboxInventory.MaxCount;
            Refused("carrying all it can", "can't carry", () => Click([GameAction.Fire]));
            inv.Materials[dirtId] = keep;
            var high = away + new Vector3(0, table.MaxAboveSurface + 1, 0);
            Refused("too high", "too high", () => { if (me.Sim.Grounded) { me.Sim.Position.Y = high.Y - 1.6f; } g.SandboxSculpt(DigKind.FillDirt, high); });
            me.Teleport(stand with { Y = t.Sample(stand.X, stand.Z) + 0.02f });
            Run(g, ref now, 0.3);
            g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
            Run(g, ref now, 0.1);
            Refused("no drill out", "ground drill", () => g.SandboxSculpt(DigKind.Scoop, away));
            g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.GroundDrill);
            Run(g, ref now, 0.1);

            // The crosshair off the ground says so, and nothing goes.
            SculptLook(g, me, sky);
            var quiet = g.DigOps;
            Frame(lmb);
            var skyText = g.SandboxLookText;
            Frame([]);
            if (g.DigOps != quiet || !skyText.Contains("ground"))
                Fail($"sculpting: LMB at the sky made {g.DigOps - quiet} ops and said \"{skyText}\"");

            // The server's rate: a client that asks faster gets the burst and no more.
            SculptLook(g, me, away);
            Run(g, ref now, 2.0);
            before = g.DigOps;
            for (var k = 0; k < 30; k++)
                g.SandboxSculpt(DigKind.Scoop, away + new Vector3(0, 0, k * 0.01f - 0.15f));
            var burst = g.DigOps - before;
            SculptLine($"30 asks in one tick: {burst} ops (burst {Game.SculptBurst})");
            if (burst > Game.SculptBurst)
                Fail($"sculpting: 30 asks in a tick made {burst} ops, the burst is {Game.SculptBurst}");

            // The picks: stone picked with none carried falls back to what is carried, and the HUD says so.
            inv.Materials[stoneId] = 0;
            inv.Materials[dirtId] = 200;
            inv.Materials[sandId] = 0;
            Program.SculptWheel.Choose(0);
            Frame([]);
            Frame([]);
            var hint = g.SandboxSculptHint;
            if (!hint.Contains("put back dirt") || Program.SculptWheel.Place(inv) != GroundKind.Dirt)
                Fail($"sculpting: stone picked, none carried: {Program.SculptWheel.Place(inv)}, HUD \"{hint}\"");
            inv.Materials[stoneId] = 50;
            Frame([]);
            Frame([]);
            if (Program.SculptWheel.Place(inv) != GroundKind.Stone || !g.SandboxSculptHint.Contains("put back stone") || !g.SandboxSculptHint.Contains("stone 50"))
                Fail($"sculpting: stone picked and carried: {Program.SculptWheel.Place(inv)}, HUD \"{g.SandboxSculptHint}\"");
            Program.SculptWheel.Choose(1);

            // A frame with the tool out and nothing pressed allocates nothing.
            SculptLook(g, me, away);
            Frame([]);
            Frame([]);
            var a0 = GC.GetAllocatedBytesForCurrentThread();
            for (var k = 0; k < 60; k++)
                Program.SweepFrame(g, now, []);
            var alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            if (alloc > 0)
                Fail($"sculpting: an idle frame with the drill out allocated {alloc / 60} B");

            // The log's cap: the tool stops with its reason, blasts still have room.
            var cap = table.LogCap;
            var pad = cap - t.Edits.Log.Count;
            t.Edits.Log.AddRange(Enumerable.Repeat(default(DigOp), pad));
            Refused("log full", "changes", () => Click([GameAction.Fire]));
            t.Edits.Log.RemoveRange(t.Edits.Log.Count - pad, pad);

            // Creative: placing costs nothing, with nothing carried.
            sb.InfiniteParts = true;
            Run(g, ref now, 0.4);
            inv.Materials[dirtId] = 0;
            before = g.DigOps;
            SculptLook(g, me, away);
            Frame([]);
            Frame(rmb);
            Frame([]);
            if (g.DigOps != before + 1 || inv.Materials[dirtId] != 0)
                Fail($"sculpting: creative: {g.DigOps - before} ops, {inv.Materials[dirtId]} dirt carried");

            // Each ground material through the server path, on a map that has them all.
            var lab = SculptLab();
            World.SetMap(lab);
            var lt = lab.Terrain!;
            inv.Clear();
            foreach (var (x, z, want, name) in new[] { (-15f, -15f, GroundKind.Sand, "sand"), (15f, -15f, GroundKind.Stone, "rock"), (-15f, 15f, GroundKind.Dirt, "dirt"), (0f, 15f, GroundKind.Dirt, "grass") })
            {
                Array.Clear(inv.Materials);
                var at = new Vector3(x, 0, z);
                at.Y = lt.Sample(x, z);
                me.Teleport(at + new Vector3(0, 0.02f, -2.2f));
                Run(g, ref now, 0.5);
                g.SandboxSculpt(DigKind.Scoop, at);
                var d = inv.Materials[dirtId];
                var s = inv.Materials[stoneId];
                var a = inv.Materials[sandId];
                var wantId = Game.MaterialOf(want);
                SculptLine($"{name} ground (material {(TerrainMaterial)lt.Material((int)MathF.Round((x - lt.OriginX) / lt.Cell), (int)MathF.Round((z - lt.OriginZ) / lt.Cell))}): {d} dirt, {s} stone, {a} sand");
                if (inv.Materials[wantId] < 5 || d + s + a != inv.Materials[wantId])
                    Fail($"sculpting: digging {name} ground gave {d} dirt, {s} stone, {a} sand");
            }
            // Under the stone depth everything is stone, grass over it or not.
            {
                Array.Clear(inv.Materials);
                var at = new Vector3(0, 0, 15);
                at.Y = lt.Sample(at.X, at.Z);
                var deep = at with { Y = at.Y - table.StoneDepth - 1.2f };
                me.Teleport(at + new Vector3(0, 0.02f, -2.2f));
                Run(g, ref now, 0.5);
                me.Sim.Position.Y = deep.Y + 1.5f;
                g.SandboxSculpt(DigKind.Scoop, deep);
                if (inv.Materials[stoneId] < 5 || inv.Materials[dirtId] != 0)
                    Fail($"sculpting: digging under the stone depth gave {inv.Materials[stoneId]} stone, {inv.Materials[dirtId]} dirt");
            }
            World.SetMap(yard);
        }
        finally
        {
            sb.InfiniteParts = wasInfinite;
            Program.SweepRelease(g);
            yard.RestoreProps();
        }
    }

    /// <summary>A real host, a client digging and filling through the real input, and a late joiner, each on its own terrain copy: identical ground at the end.</summary>
    private static void CheckTerrainSculptNet()
    {
        var previous = World.Map;
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Terrain is not { } t || yard.Sandbox is not { } sb)
        {
            Fail($"sculpting network: the {ConstructYardMap} map is missing");
            return;
        }
        const ushort port = 26487;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        var wasInfinite = sb.InfiniteParts;
        yard.RestoreProps();
        World.SetMap(yard);
        TerrainData Copy()
        {
            var m = MapFile.Load(File.ReadAllText(Path.Combine(AppFolders.Data, "Maps", "Sandbox", "ConstructYard.json")), "yard copy");
            m.Validate();
            return m.Terrain!;
        }
        try
        {
            sb.InfiniteParts = false;
            using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
            server.Mode = GameMode.Sandbox;
            using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            a.DigMirror = Copy();
            server.DigMirror = Copy();
            var serverLog = server.DigMirror.Edits.Log;
            var simHost = server.Net.SimulateBadNetwork(60, 12, 5);
            var simClient = a.Net.SimulateBadNetwork(60, 12, 5);
            for (var wait = 0; wait < 60 && (server.Phase != MatchPhase.Playing || a.Local == null || !server.Players.ContainsKey(a.LocalId)); wait++)
                Pump(server, [a], 0.25, ref now, clock); // (a join under this lag and loss takes about 2 s: wait for it, not for a fixed time)
            Pump(server, [a], 0.5, ref now, clock);
            if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp) || a.Local is not { } mine)
            {
                Fail("sculpting network: the client didn't join");
                return;
            }
            if (!sp.Alive)
                server.SelfTestRespawn(sp, now);
            Pump(server, [a], 0.3, ref now, clock);
            Program.SweepRelease(a);
            var spot = OpenSpot(server.Constructs) ?? new Vector3(10, 0, 10);
            var stand = spot with { Y = t.Sample(spot.X, spot.Z) + 0.02f };
            sp.ImmuneUntil = 1e9;
            sp.Teleport(stand);
            mine.Teleport(stand);
            Pump(server, [a], 0.6, ref now, clock);
            a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.GroundDrill);
            for (var wait = 0; wait < 40 && (server.SandboxHands.Hand(sp.Id).Tool != ConstructTool.GroundDrill || a.SandboxHands.Hand(a.LocalId).Tool != ConstructTool.GroundDrill); wait++)
                Pump(server, [a], 0.1, ref now, clock);
            SculptLine($"client's drill out: server {server.SandboxHands.Hand(sp.Id).Tool}, client {a.SandboxHands.Hand(a.LocalId).Tool}; client alive {a.Local?.Alive}, eye {a.Local?.RenderEye}, on the server {sp.Eye}");
            var lmb = new[] { GameAction.Fire };
            var rmb = new[] { GameAction.Aim };
            void Hold(GameAction[] key, Vector3 at, double seconds)
            {
                PumpEach(server, [a], seconds, ref now, clock, t2 =>
                {
                    SculptLook(a, a.Local!, at);
                    Program.SweepFrame(a, t2, key);
                });
                PumpEach(server, [a], 0.1, ref now, clock, t2 => Program.SweepFrame(a, t2, []));
            }
            Vector3 Ground(float dx, float dz)
            {
                var p = new Vector3(stand.X + dx, 0, stand.Z + dz);
                p.Y = t.Sample(p.X, p.Z);
                return p;
            }
            // RMB with nothing carried: the refusal reaches the client's HUD.
            var opsBefore = serverLog.Count;
            Hold(rmb, Ground(-2.2f, 0), 0.2);
            var notice = "";
            for (var wait = 0; wait < 30 && notice.Length == 0; wait++)
            {
                Pump(server, [a], 0.05, ref now, clock);
                notice = a.SandboxNotice;
            }
            SculptLine($"client refused: \"{notice}\"");
            if (!notice.Contains("not enough") || serverLog.Count != opsBefore)
                Fail($"sculpting network: RMB with nothing carried: {serverLog.Count - opsBefore} ops, the client's HUD says \"{notice}\"");
            // The client digs and fills through its input; the host blasts in the same place: ops from two peers in one order.
            Hold(lmb, Ground(2.2f, 0), 1.0);
            server.BlastCrater(Ground(0, 6), 4f);
            Hold(rmb, Ground(-2.2f, 0), 0.5);
            Hold(lmb, Ground(0, 2.2f), 0.7);
            Pump(server, [a], 0.6, ref now, clock);
            var dirtId = Game.MaterialOf(GroundKind.Dirt);
            var serverDirt = server.SandboxInventory(sp.Id).Materials[dirtId];
            var clientDirt = a.SandboxInventory(a.LocalId).Materials[dirtId];
            if (serverDirt == 0 || serverDirt != clientDirt)
                Fail($"sculpting network: the server says {serverDirt} dirt, the client {clientDirt}");
            // A late joiner, then more ops after it arrived.
            using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            b.DigMirror = Copy();
            Pump(server, [a, b], 1.5, ref now, clock);
            var late = b.DigOpsRead;
            // (b's map load wiped the map the peers share here, a thing of this test: the server's own ground comes back from its copy)
            if (t.Edits.Log.Count < serverLog.Count)
            {
                yard.RestoreProps();
                foreach (var op in serverLog)
                    t.Edits.Apply(op);
            }
            PumpEach(server, [a, b], 0.8, ref now, clock, t2 =>
            {
                SculptLook(a, a.Local!, Ground(-2.2f, 2.2f));
                Program.SweepFrame(a, t2, rmb);
            });
            PumpEach(server, [a, b], 0.1, ref now, clock, t2 => Program.SweepFrame(a, t2, []));
            Hold(lmb, Ground(2.2f, 2.2f), 0.5);
            var ops = serverLog.Count;
            for (var wait = 0; wait < 60 && (a.DigOpsRead < ops || b.DigOpsRead < ops); wait++)
                Pump(server, [a, b], 0.05, ref now, clock);
            var fills = 0;
            foreach (var op in serverLog)
                if (op.Kind >= DigKind.FillDirt)
                    fills++;
            var y = stand.Y;
            var differA = SculptDiffer(server.DigMirror, a.DigMirror!, -8, 8, y - 5, y + 4, stand.X, stand.Z, 0.3f);
            var differB = SculptDiffer(server.DigMirror, b.DigMirror!, -8, 8, y - 5, y + 4, stand.X, stand.Z, 0.3f);
            var logsMatch = a.DigMirror!.Edits.Log.Count == ops && b.DigMirror!.Edits.Log.Count == ops;
            SculptLine($"loopback ({clock.Elapsed.TotalSeconds:0.0} s, simulated lag and loss {(simHost && simClient ? "on" : "NOT on")}): {ops} ops ({fills} fills) on the server, client read {a.DigOpsRead}, late joiner {late} on arrival and {b.DigOpsRead} in all; {server.SandboxInventory(sp.Id).Materials[dirtId]} dirt carried; heights and solids: {differA} points differ on the client, {differB} on the joiner");
            if (ops < 5 || fills < 2)
                Fail($"sculpting network: only {ops} ops ({fills} fills) got through");
            if (differA > 0 || differB > 0 || !logsMatch || a.DigOpsRead != ops || b.DigOpsRead != ops)
                Fail($"sculpting network: {differA} points differ on the client, {differB} on the joiner; logs {a.DigMirror.Edits.Log.Count} and {b.DigMirror!.Edits.Log.Count} of {ops}");
            Program.SweepRelease(a);
        }
        finally
        {
            sb.InfiniteParts = wasInfinite;
            yard.RestoreProps();
            World.SetMap(previous);
        }
    }
}
