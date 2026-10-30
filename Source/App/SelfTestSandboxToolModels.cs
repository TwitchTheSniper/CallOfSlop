using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Sandbox tool models": the held models of the Sandbox tools (Constructs/Tools; Source/Render/ToolRender.cs;
    /// docs/sandbox/CONSTRUCTS.md, "Tools in hand"). Every tool file loads (one piece, its hands within reach), each tool in
    /// the game has a model and the tools still to come have theirs; bad files are refused for the right reason; in first
    /// person each model is drawn with its boxes (sparks and a beam while in use) and both hands reach their grips, and the
    /// equip drops the old tool before the new one comes up; in an offline Sandbox game a bot and the local player show
    /// the tool (and a carried part) in third person instead of the gun, the scene graph drawing exactly what the old
    /// immediate code draws, and nobody holds a tool outside Sandbox; a real client sees the host's tool and the host the
    /// client's; and drawing allocates nothing per frame.
    /// </summary>
    private static void CheckSandboxToolModels()
    {
        try
        {
            ToolModelFiles();
            ToolModelsFirstPerson();
            if (Maps.Find(ConstructYardMap) is not { } yard)
            {
                Fail($"tool models: the {ConstructYardMap} map is missing");
                return;
            }
            ToolModelsThirdPerson(yard);
            ToolModelsOverWire(yard);
        }
        finally
        {
            ToolRender.Hands = null;
            ToolRender.ShowModel = null;
            ToolRender.Reset();
        }
    }

    private static void ToolModelFiles()
    {
        var all = ConstructTools.All;
        foreach (var r in ConstructTools.Refused)
            Fail($"tool models: refused {r}");
        var missing = ConstructTools.Keys.Where(k => ConstructTools.Find(k) == null).ToList();
        var wrongTool = new[] { ConstructTool.Wrench, ConstructTool.Welder, ConstructTool.Linker }.Where(t => ConstructTools.For(t)?.Tool != t).ToList();
        var bad = new List<string>();
        foreach (var m in all)
        {
            if (ConstructTools.Floating(m.Boxes) is { } fl)
                bad.Add($"{m.Name}: box {fl} floats");
            if (m.Grip.At.Length() > 0.15f || m.Tip.Length() > 0.6f || m.Boxes.Length == 0 || m.Description.Length == 0)
                bad.Add($"{m.Name}: grip, tip, boxes or description");
            if (m.Tool == null && m.Description.IndexOf("to come", StringComparison.OrdinalIgnoreCase) < 0)
                bad.Add($"{m.Name}: a tool still to come should say so");
        }
        Line($"tool models: {all.Count} loaded ({string.Join(", ", all.Select(m => $"{m.Name} {m.Boxes.Length} boxes{(m.Tool == null ? " (to come)" : "")}"))}); "
             + $"{ConstructTools.Refused.Count} refused, {missing.Count} tool keys without a model, {bad.Count} problems");
        if (missing.Count > 0 || wrongTool.Count > 0 || bad.Count > 0)
            Fail($"tool models: missing {string.Join(", ", missing)}; wrong tool {string.Join(", ", wrongTool)}; {string.Join("; ", bad)}");

        // Bad files are refused for the right reason.
        const string ok = "\"format\": \"call-of-slop-construct-tool/1\", \"name\": \"T\", \"grip\": { \"at\": [0, 0, 0] }, \"tip\": [0, 0, -0.2]";
        const string box = "{ \"size\": [0.04, 0.1, 0.04] }";
        var cases = new (string Json, string Want)[]
        {
            ($"{{ {ok}, \"tool\": \"wrench\", \"boxes\": [{box}], \"colour\": 1 }}", "unknown key \"colour\""),
            ($"{{ {ok}, \"tool\": \"spanner\", \"boxes\": [{box}] }}", "\"tool\" is one of"),
            ($"{{ {ok}, \"tool\": \"wrench\", \"boxes\": [{box}, {{ \"size\": [0.02, 0.02, 0.02], \"at\": [0, 0.3, 0] }}] }}", "boxes[1] doesn't touch"),
            ($"{{ {ok}, \"tool\": \"wrench\", \"boxes\": [] }}", "at least one box"),
            ($"{{ {ok}, \"tool\": \"wrench\", \"boxes\": [{string.Join(", ", Enumerable.Repeat(box, ConstructTools.MaxBoxes + 1))}] }}", $"at most {ConstructTools.MaxBoxes} boxes"),
            ($"{{ \"name\": \"T\", \"tool\": \"wrench\", \"tip\": [0, 0, 0], \"boxes\": [{box}] }}", "missing \"grip\""),
            ($"{{ {ok}, \"tool\": \"wrench\", \"boxes\": [{{ \"size\": [0.04, 0.1, 0.04], \"spin\": true }}] }}", "need a \"spin\" section"),
            ($"{{ {ok}, \"tool\": \"wrench\", \"boxes\": [{{ \"size\": [0.04, 0.1, 0.04], \"glow\": true }}] }}", "must not glow"),
            ($"{{ {ok}, \"tool\": \"wrench\", \"boxes\": [{{ \"size\": [0.9, 0.1, 0.04] }}] }}", "2 mm to 60 cm"),
            ($"{{ {ok}, \"tool\": \"wrench\", \"boxes\": [{box}], \"use\": {{ \"motion\": \"wobble\" }} }}", "\"use.motion\" is one of"),
            ($"{{ {ok}, \"tool\": \"wrench\", \"boxes\": [{box}], \"use\": {{ \"spin\": 5 }} }}", "no box has \"spin\""),
            ($"{{ {ok}, \"tool\": \"wrench\", \"boxes\": [{box}], \"support\": {{ \"at\": [0, 0, -0.1], \"shape\": \"claw\" }} }}", "\"support.shape\" is one of"),
        };
        var refusedRight = 0;
        foreach (var (json, want) in cases)
        {
            try
            {
                ConstructTools.Parse(json, "bad.json");
                Fail($"tool models: a bad file loaded (wanted \"{want}\")");
            }
            catch (ToolModelException ex)
            {
                if (ex.Message.Contains(want, StringComparison.Ordinal))
                    refusedRight++;
                else
                    Fail($"tool models: refused for \"{ex.Message}\", wanted \"{want}\"");
            }
        }
        // Two models for one tool: the second is refused, the first kept.
        var dir = Directory.CreateTempSubdirectory("cos_tools_");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "A.json"), $"{{ {ok.Replace("\"T\"", "\"A\"")}, \"tool\": \"drill\", \"boxes\": [{box}] }}");
            File.WriteAllText(Path.Combine(dir.FullName, "B.json"), $"{{ {ok.Replace("\"T\"", "\"B\"")}, \"tool\": \"drill\", \"boxes\": [{box}] }}");
            var refused = new List<string>();
            var loaded = ConstructTools.LoadFolder(dir.FullName, refused);
            if (loaded.Length != 1 || refused.Count != 1 || !refused[0].Contains("already the drill's model"))
                Fail($"tool models: two models for the drill: {loaded.Length} loaded, refused {string.Join("; ", refused)}");
            else
                refusedRight++;
        }
        finally
        {
            dir.Delete(true);
        }
        Line($"tool models: {refusedRight} of {cases.Length + 1} bad files refused for the right reason");
    }

    private static void ToolModelsFirstPerson()
    {
        ToolRender.Reset();
        var rec = new DrawRecorder();
        var g = new Renderer { Recorder = rec };
        var anim = new ViewModelAnimator();
        var t = 50.0;
        var problems = new List<string>();
        var drawn = 0;
        foreach (var m in ConstructTools.All)
        {
            ToolRender.ShowModel = m;
            foreach (var use in new[] { false, true })
            {
                var f = new ToolFrame();
                var input = new ToolRender.ViewInput { Tool = ConstructTool.Wrench, Now = t, Using = use, Grounded = true, Velocity = new Vector3(3, 0, 0) };
                ToolRender.FirstPerson(input, ref f, out _);
                input.Now = t += 1.0; // (raised by now)
                if (!ToolRender.FirstPerson(input, ref f, out var shown) || f.Model != m || shown != ConstructTool.Wrench)
                {
                    problems.Add($"{m.Name}: not shown");
                    continue;
                }
                rec.Reset();
                var o = new ToolGfxOut(g);
                ToolRender.Emit(ref o, f);
                var want = m.Boxes.Length + (use && m.Sparks != null ? 10 : 0) + (use && m.Beam != null ? 1 : 0);
                if (rec.Count != want || ToolRender.BoxCount(f) != want)
                    problems.Add($"{m.Name}{(use ? " in use" : "")}: {rec.Count} boxes drawn, expected {want}");
                for (var i = 0; i < rec.Count; i++)
                    if (!Finite(rec.Items[i].M))
                    {
                        problems.Add($"{m.Name}: a box isn't a number");
                        break;
                    }
                // Both hands on their grips (the view model's arms; a one-handed tool's off hand rests out of sight).
                anim.Right.Solve(f.Right, Matrix4x4.Identity, Vector3.Zero);
                anim.Left.Solve(f.Left, Matrix4x4.Identity, Vector3.Zero);
                var miss = (anim.Right.HandWorld.Translation - f.Right.Pos).Length();
                var missL = (anim.Left.HandWorld.Translation - f.Left.Pos).Length();
                if (!anim.Right.Reached || miss > 0.005f || (m.Support != null && (!anim.Left.Reached || missL > 0.005f)))
                    problems.Add($"{m.Name}: a hand misses its grip by {miss * 1000:0} / {missL * 1000:0} mm");
                // The tool sits in the lower right of the view, in front of the camera.
                var grip = f.Pose.Translation;
                if (grip.Z > -0.25f || grip.X < 0.05f || grip.Y > 0f)
                    problems.Add($"{m.Name}: the grip is at {grip}");
                drawn++;
            }
        }
        ToolRender.ShowModel = null;

        // Equipping: the welder out; then the wrench (the welder drops, the wrench comes up); then back to the gun.
        ToolRender.Reset();
        var w0 = ToolRender.Shown(ToolRender.ViewSlot, ConstructTool.Welder, 10.0, out var d0);
        var w1 = ToolRender.Shown(ToolRender.ViewSlot, ConstructTool.Welder, 10.0 + ToolRender.RaiseSeconds + 0.01, out var d1);
        var s0 = ToolRender.Shown(ToolRender.ViewSlot, ConstructTool.Wrench, 11.0, out var e0);
        var s1 = ToolRender.Shown(ToolRender.ViewSlot, ConstructTool.Wrench, 11.0 + ToolRender.LowerSeconds * 0.5, out var e1);
        var s2 = ToolRender.Shown(ToolRender.ViewSlot, ConstructTool.Wrench, 11.0 + ToolRender.LowerSeconds + ToolRender.RaiseSeconds * 0.5, out var e2);
        var s3 = ToolRender.Shown(ToolRender.ViewSlot, ConstructTool.Wrench, 12.0, out var e3);
        var g0 = ToolRender.Shown(ToolRender.ViewSlot, ConstructTool.None, 13.0, out _);
        var g1 = ToolRender.Shown(ToolRender.ViewSlot, ConstructTool.None, 13.0 + ToolRender.LowerSeconds + 0.01, out _);
        var equip = w0 == ConstructTool.Welder && d0 > 0.99f && w1 == ConstructTool.Welder && d1 < 0.01f // (from the gun: straight up)
                    && s0 == ConstructTool.Welder && e0 < 0.01f && s1 == ConstructTool.Welder && e1 is > 0.4f and < 0.6f
                    && s2 == ConstructTool.Wrench && e2 is > 0.4f and < 0.6f && s3 == ConstructTool.Wrench && e3 < 0.01f
                    && g0 == ConstructTool.Wrench && g1 == ConstructTool.None;
        Line($"tool models, first person: {drawn} poses drawn (idle and in use), {problems.Count} problems; equip: welder up from the gun, "
             + $"swap to the wrench: the welder {e1:0.00} down half-way through the drop, the wrench {e2:0.00} down half-way up; back to the gun after the drop {g1 == ConstructTool.None}");
        if (problems.Count > 0)
            Fail($"tool models, first person: {string.Join("; ", problems.Take(6))}");
        if (!equip)
            Fail($"tool models: the equip went wrong ({w0} {d0:0.00}, {w1} {d1:0.00}; {s0} {e0:0.00}, {s1} {e1:0.00}, {s2} {e2:0.00}, {s3} {e3:0.00}; {g0}, {g1})");
    }

    /// <summary>Draws of a box of exactly this colour in a recording (a tool's or part's distinctive colour).</summary>
    private static int DrawsOf(DrawRecorder rec, Vector3 colour)
    {
        var n = 0;
        for (var i = 0; i < rec.Count; i++)
            if ((rec.Items[i].Color - colour).Length() < 1e-4f)
                n++;
        return n;
    }

    private static void ToolModelsThirdPerson(Map yard)
    {
        yard.RestoreProps();
        World.SetMap(yard);
        ToolRender.Reset();
        var now = 0.0;
        using var g = ModeGame(1, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        var bot = g.Players.Values.FirstOrDefault(p => p.Bot != null && p.Alive);
        if (g.Local is not { Alive: true } me || bot == null)
        {
            Fail("tool models: no live local player and bot in the Sandbox game");
            return;
        }
        var welder = ToolRender.ModelFor(ConstructTool.Welder);
        var body = welder.Boxes[1].Color; // (the welder's orange body)
        var wheel = ConstructParts.Need("Wheel");
        var oldRec = new DrawRecorder();
        var newRec = new DrawRecorder();
        var oldG = new Renderer { Recorder = oldRec };
        var newG = new Renderer { Recorder = newRec };
        var parity = new Parity();
        var t = 200.0;
        (int Old, int New, int Tool) Both(Player p, string what)
        {
            oldRec.Reset();
            newRec.Reset();
            PlayerRender.DrawImmediate(oldG, p, t);
            ScenePresentation.DrawPlayer(newG, p, t);
            CompareDraws(what, oldRec, newRec, parity);
            return (oldRec.Count, newRec.Count, DrawsOf(newRec, body));
        }
        ToolRender.Hands = g.SandboxHands;
        var gun = Both(bot, "a bot with its gun");
        ref var bh = ref g.SandboxHands.Hand(bot.Id);
        bh.Tool = ConstructTool.Welder;
        Both(bot, "a bot taking out the welder"); // (the swap starts)
        t += 1;
        var tool = Both(bot, "a bot with the welder");
        bh.Edit = EditKind.Attach;
        t += 0.05;
        var use = Both(bot, "a bot welding");
        bh.Edit = EditKind.None;
        bh.Held = wheel.Id;
        var held = Both(bot, "a bot with the welder and a wheel");
        var wheelDraws = DrawsOf(newRec, wheel.Boxes[0].Color);
        ref var mh = ref g.SandboxHands.Hand(me.Id);
        mh.Tool = ConstructTool.Wrench;
        Both(me, "the local player taking out the wrench");
        t += 1;
        var wrench = ToolRender.ModelFor(ConstructTool.Wrench);
        var mine = Both(me, "the local player with the wrench");
        var wrenchDraws = DrawsOf(newRec, wrench.Boxes[0].Color);
        ToolRender.Hands = null; // (outside Sandbox: guns)
        var outside = Both(bot, "a bot outside Sandbox");
        Line($"tool models, third person: a bot's gun {gun.New} draws; the welder {tool.New} draws ({tool.Tool} of its orange body), welding {use.New} (sparks), "
             + $"with a wheel {held.New} ({wheelDraws} wheel boxes); the local player's wrench {mine.New} ({wrenchDraws} of its grip); outside Sandbox {outside.New} (the gun); "
             + $"scene graph vs immediate: {parity.Cases} cases, {parity.Bad} differ");
        if (tool.Tool != 1 || use.New != tool.New + 10 || wheelDraws < 1 || wrenchDraws != 1 || outside.Tool != 0 || outside.New != gun.New || gun.Tool != 0)
            Fail("tool models: a third-person tool, its sparks or a carried part isn't drawn (or a gun is drawn outside Sandbox with a tool)");

        // No allocation: 240 frames of the first-person view and two third-person players with tools in use.
        ToolRender.Hands = g.SandboxHands;
        bh.Edit = EditKind.Attach;
        oldRec.Keep = newRec.Keep = false;
        var f = new ToolFrame();
        var anim = new ViewModelAnimator();
        long Frames(int n)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < n; i++)
            {
                t += 1 / 60.0;
                var input = new ToolRender.ViewInput { Tool = i % 120 < 60 ? ConstructTool.Welder : ConstructTool.Linker, Held = wheel, Now = t, Using = true, Grounded = true };
                ToolRender.FirstPerson(input, ref f, out _);
                var o = new ToolGfxOut(oldG);
                ToolRender.Emit(ref o, f);
                anim.Right.Solve(f.Right, Matrix4x4.Identity, Vector3.Zero);
                anim.Left.Solve(f.Left, Matrix4x4.Identity, Vector3.Zero);
                ToolRender.AddLights(t);
                oldRec.Reset();
                newRec.Reset();
                PlayerRender.DrawImmediate(oldG, bot, t);
                ScenePresentation.DrawPlayer(newG, bot, t);
                ScenePresentation.DrawPlayer(newG, me, t);
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Frames(30);
        var bytes = Frames(240);
        Line($"tool models: 240 frames of tools in use (first person, a bot and the local player in third person) allocated {bytes} bytes");
        if (bytes > 0)
            Fail($"tool models: drawing tools allocated {bytes} bytes over 240 frames");
        ToolRender.Hands = null;
    }

    /// <summary>A real host and client: each sees the other's tool in their hands.</summary>
    private static void ToolModelsOverWire(Map yard)
    {
        const ushort port = 26351;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        ToolRender.Reset();
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp) || server.Local is not { } host)
        {
            Fail("tool models over the wire: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        if (!host.Alive)
            server.SelfTestRespawn(host, now);
        Pump(server, [a], 0.4, ref now, clock);
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Linker);
        server.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
        Pump(server, [a], 0.6, ref now, clock);
        var rec = new DrawRecorder();
        var g = new Renderer { Recorder = rec };
        // The host draws the client's player; the client draws the host's.
        int Drawn(Game game, byte id, ConstructTool tool)
        {
            if (!game.Players.TryGetValue(id, out var p) || !p.Alive)
                return -1;
            ToolRender.Hands = game.SandboxHands;
            var t = 300.0;
            ScenePresentation.DrawPlayer(g, p, t); // (the swap starts)
            rec.Reset();
            ScenePresentation.DrawPlayer(g, p, t + 1);
            ToolRender.Hands = null;
            return DrawsOf(rec, ToolRender.ModelFor(tool).Boxes[1].Color);
        }
        var clientOnHost = Drawn(server, a.LocalId, ConstructTool.Linker);
        var hostOnClient = Drawn(a, host.Id, ConstructTool.Welder);
        Line($"tool models over the wire: the client's hands on the host {server.SandboxHands.Hand(a.LocalId).Tool}, drawn there with {clientOnHost} linking tool body box; "
             + $"the host's hands on the client {a.SandboxHands.Hand(host.Id).Tool}, drawn there with {hostOnClient} welder body box");
        if (clientOnHost != 1 || hostOnClient != 1)
            Fail("tool models: a remote player's tool isn't drawn in their hands");
    }
}
