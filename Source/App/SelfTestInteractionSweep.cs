using System.Diagnostics;
using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Interaction sweep" (board item dev-crash-sweep-cs01: "fix all the crashes"; the developer's crash was F at a
    /// generated receiver: <c>ConstructRide.GrabOn</c>'s Math.Clamp with its bounds a hair inverted, on the host). Every
    /// construct part type, hand-made and generated, mounted on a frame block (a construct that isn't anchored: F there is
    /// the grab-on), and one of each kind (role, logic, joint, seat, storage, bench, bin, cargo volume, hose end, gun part
    /// category, config, tool) also loose, frozen by the physics gun and on a moving construct; every generated receiver
    /// (and the hand-made guns) also with parts fitted and merged 2 x 2. At each, every tool (bare hands with the gun, third
    /// person, the wrench, the part's own type in hand with its tool, the welder's socket, freeform and point modes, the physics
    /// gun, the linking tool, configure, paint, the remover) and every interaction (F, LMB, RMB, R, the wheel both ways,
    /// MMB with a flick) for a few frames: pressed, held, let go. Each frame runs like the game's: the input outside an
    /// ImGui frame (Program.SweepFrame, the same calls the player's loop makes), then the Sandbox ImGui pass inside a real
    /// headless frame with error recovery on (Program.SweepDraw), then a tick. Any exception (the guards rethrow here), any
    /// HUD text asked for outside a frame and any ImGui error fail the section. Then the same with a real host and a
    /// separate client (lag, loss) on one of each kind, both peers' input, and no packet may be dropped for throwing.
    /// </summary>
    private static void CheckInteractionSweep()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail($"interaction sweep: the {ConstructYardMap} map is missing");
            return;
        }
        var problems = new List<string>();
        var seen = new HashSet<string>();
        var ctx = ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.IniFilename = default;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset | ImGuiBackendFlags.RendererHasTextures;
        io.DisplaySize = new float2(1920, 1080);
        io.DisplayFramebufferScale = new float2(1, 1);
        io.DeltaTime = 1f / 60;
        Ui.SafeErrors(io);
        Program.SandboxGuardRethrow = true;
        Game.SandboxActRethrow = true;
        var trips0 = Program.SandboxGuardTrips;
        var actTrips0 = Game.SandboxActTrips;
        var dropped0 = NetSafety.Dropped;
        var debris = sb.DebrisSeconds;
        try
        {
            SweepOffline(yard, sb, problems, seen);
            SweepOnline(yard, sb, problems, seen);
        }
        finally
        {
            yard.RestoreProps(); // (the Ground Drill digs and fills the yard: later sections want it as built)
            Program.SandboxGuardRethrow = false;
            Game.SandboxActRethrow = false;
            Program.TestShift = Program.TestCtrl = Program.TestZ = false;
            sb.DebrisSeconds = debris;
            ImGui.DestroyContext(ctx);
        }
        if (Program.SandboxGuardTrips != trips0)
            problems.Add($"the Sandbox input guard recovered {Program.SandboxGuardTrips - trips0} frame(s)");
        if (Game.SandboxActTrips != actTrips0)
            problems.Add($"the host's action guard refused {Game.SandboxActTrips - actTrips0} action(s) that threw");
        if (NetSafety.Dropped != dropped0)
            problems.Add($"{NetSafety.Dropped - dropped0} packet(s) were dropped because their handler threw (last: {NetSafety.LastDrop})");
        if (problems.Count > 0)
            Fail($"interaction sweep: {problems.Count} problem(s): " + string.Join("; ", problems.Take(25)));
    }

    /// <summary>A part kind for the sweep's "one of each": what makes the input and use code take a different path.</summary>
    private static string SweepKind(ConstructPartDef d) =>
        $"{d.Role}|{d.Logic}|{d.Generated}|{(d.Role == PartRole.GunPart ? Parts.Find(d.GunPartName)?.Category.ToString() : "")}|{d.IsReceiverGun}|{d.HasJoint}|{d.Slots.Length > 0}|{d.IsBin}|{d.IsGunBench}|{d.Boarding}|{d.HoseEnd}|{d.Configurable}|{d.PhysicsOnly}|{d.Tool}|{ConstructCargo.IsVolume(d)}|{d.Settings.Length > 0}|{d.IsExtrusion}|{d.IsDieFrame}|{d.Piece}";

    private enum SweepState { Mounted, Loose, Frozen, Moving, WithParts, Merged }

    /// <summary>The tools of a sweep case: what is in the hands and which mode.</summary>
    private enum SweepTool { Hands, ThirdPerson, Wrench, Holding, WelderSocket, WelderFreeform, PhysicsGun, Linker, LinkerRope, Configure, Paint, Remover, WelderPoint, GroundDrill }

    private static readonly GameAction[] SweepKeys = [GameAction.Interact, GameAction.Fire, GameAction.Aim, GameAction.Reload];

    /// <summary>A whole small engine with a stack and a starter, on a pinned bed: the part of it that is <paramref name="d"/>.</summary>
    private static Construct? SweepEngine(ConstructWorld w, ConstructPartDef d, Vector3 ground, out int part)
    {
        part = -1;
        var block = ConstructParts.Need("Frame Block");
        var c = w.Create(ground + new Vector3(0, 0.02f - block.BoundsMin.Y, 0), Quaternion.Identity);
        if (c == null)
            return null;
        w.AddPart(c, block, Vector3.Zero, 0);
        c.Pinned = true;
        c.Rebuild();
        var crank = FitTurned(w, c, 0, "+y", "Small Crank Section", "-y", _ => true);
        var cyl = FitTurned(w, c, crank, "+y", "Small Cylinder", "base", _ => true);
        var fuel = FitTurned(w, c, cyl, "head", "Small Carburettor", "base", _ => true);
        var boost = FitTurned(w, c, fuel, "top", "Small Turbo", "base", _ => true);
        var outlet = FitTurned(w, c, cyl, "+x", "Small Exhaust Outlet", "base", _ => true);
        FitTurned(w, c, outlet, "top", "Small Smokestack", "base", _ => true);
        FitTurned(w, c, crank, "-x", "Small Starter Motor", "base", _ => true);
        var fly = FitTurned(w, c, crank, "front", "Small Flywheel", "crank", _ => true);
        FitTurned(w, c, fly, "+x", "Fuel Tank", "-x", _ => true);
        FitTurned(w, c, fly, "+z", "Transmission", "-z", _ => true);
        if (d.Name == "Small Injector Rail")
            FitTurned(w, c, cyl, "-x", d.Name, "base", _ => true);
        if (d.Name == "Small Supercharger")
            FitTurned(w, c, boost, "top", d.Name, "base", _ => true);
        c.Rebuild();
        for (var i = 0; i < c.PartCount && part < 0; i++)
            if (c.Parts[i].Def == d.Id)
                part = i;
        return part < 0 ? null : c;
    }

    /// <summary>Builds the case's construct in world <paramref name="w"/> at <paramref name="at"/>: the part in the state asked for. Returns it and the part's index (null when the state doesn't apply).</summary>
    private static Construct? SweepBuild(ConstructWorld w, ConstructPartDef d, SweepState st, Vector3 at, out int part)
    {
        part = 0;
        var ground = ConstructWorld.Ground(World.Query, at.X, at.Z);
        if (st is SweepState.Loose or SweepState.Frozen)
        {
            var c = w.Create(ground + new Vector3(0, 0.02f - d.BoundsMin.Y, 0), Quaternion.Identity);
            if (c == null)
                return null;
            w.AddPart(c, d, Vector3.Zero, 0);
            c.Frozen = st == SweepState.Frozen;
            c.Rebuild();
            return c;
        }
        if (st == SweepState.Merged)
        {
            if (!d.IsReceiverGun)
                return null;
            var c = w.Create(ground + new Vector3(0, 0.6f - d.BoundsMin.Y, 0), Quaternion.Identity);
            if (c == null)
                return null;
            var size = d.BoundsMax - d.BoundsMin;
            for (var y = 0; y < 2; y++)
                for (var x = 0; x < 2; x++)
                    ConstructBuild.AutoLink(w, c, w.AddPart(c, d, new Vector3(x * size.X, y * size.Y, 0), 0));
            c.Rebuild();
            part = c.MergeCount > 0 ? c.Merges[0].Lead : 0;
            return c;
        }
        // Mounted (and moving, and with parts): on a frame block, the first socket pairing that fits.
        var block = ConstructParts.Need("Frame Block");
        var m = w.Create(ground + new Vector3(0, 0.02f - block.BoundsMin.Y, 0), Quaternion.Identity);
        if (m == null)
            return null;
        w.AddPart(m, block, Vector3.Zero, 0);
        m.Rebuild();
        part = -1;
        for (var s = 0; s < block.Sockets.Length && part < 0; s++)
            for (var o = 0; o < d.Sockets.Length && part < 0; o++)
                for (var k = 0; k < 4 && part < 0; k++)
                    if (ConstructBuild.Place(m, 0, s, d, o, k, out var g) == null)
                    {
                        part = ConstructBuild.Attach(w, m, d, g);
                        m.Rebuild();
                    }
        if (part < 0)
        {
            // (it fits no socket of a frame block: a foundation, a big hull: then on its own, as set down)
            w.Remove(m);
            var c = w.Create(ground + new Vector3(0, 0.02f - d.BoundsMin.Y, 0), Quaternion.Identity);
            if (c == null)
                return null;
            w.AddPart(c, d, Vector3.Zero, 0);
            c.Rebuild();
            part = 0;
            return c;
        }
        if (st == SweepState.WithParts && d.IsExtruder)
        {
            // An Extruder with a piece on its die, as it is while it runs.
            ref var ep = ref m.Parts[part];
            var growing = Extrusion.Def(new ExtrusionKey(Extrusion.Presets[4].Mask, 25, 25, 100, RecyclingTable.Current.IdOf("metal"), true))!;
            var piece = w.AddPart(m, growing, ep.Pos + Rot24.Apply(ep.Rot, d.ExtDieAt), ep.Rot, ep.Seg);
            w.AddLink(m, part, d.ExtDieSocket, piece, 0);
            m.Rebuild();
            return m;
        }
        if (st == SweepState.WithParts && d.Piece != EnginePiece.None)
        {
            // Mounted on the block with the rest of a running engine round it (the crank, or what is on it, comes from the block).
            w.Remove(m);
            return SweepEngine(w, d, ground, out part);
        }
        if (st == SweepState.WithParts)
        {
            // A builder receiver (or a hand-made gun) with what fits it: a barrel, a magazine, an optic, a muzzle on the barrel.
            foreach (var cat in new[] { PartCategory.Barrel, PartCategory.Feed, PartCategory.Optic, PartCategory.Muzzle, PartCategory.Ammo })
                foreach (var cand in ConstructParts.All.Where(x => x.Role == PartRole.GunPart && Parts.Find(x.GunPartName) is { } pp && pp.Category == cat).Take(6))
                {
                    var fitted = false;
                    for (var on = m.PartCount - 1; on >= 1 && !fitted; on--)
                        for (var s = 0; s < m.DefOf(on).Sockets.Length && !fitted; s++)
                            if (SnapBuild.Fit(m, cand, on, s, -1, out var g))
                            {
                                ConstructBuild.Attach(w, m, cand, g);
                                m.Rebuild();
                                fitted = true;
                            }
                    if (fitted)
                        break;
                }
        }
        return m;
    }

    /// <summary>Stands the player 2 m off the part (on the side towards <paramref name="from"/>) and looks at its middle.</summary>
    private static void SweepAim(Game g, Player me, Construct c, int part, float side)
    {
        var at = part >= 0 && part < c.PartCount ? ConstructRender.PartCentre(c, part) : c.WorldCom;
        StandBy(me, at, 2.0f, at - new Vector3(side, 0, 0.6f));
        var dir = at - me.RenderEye;
        if (dir.LengthSquared() < 1e-4f)
            dir = -Vector3.UnitZ;
        dir = Vector3.Normalize(dir);
        g.LookPitch = MathF.Asin(Math.Clamp(dir.Y, -1f, 1f));
        g.LookYaw = MathF.Atan2(dir.X, -dir.Z);
    }

    /// <summary>The sweep's per-frame runner on one peer: the input outside ImGui, the ImGui pass inside a headless frame, problems recorded once per kind.</summary>
    private sealed class SweepRunner
    {
        public required Game G;
        public required List<string> Problems;
        public required HashSet<string> Seen;
        public Action Tick = () => { };
        public string Where = "";
        public int Frames, Looked, Climbs;
        public double Now;

        public void Note(string key, string text)
        {
            if (Seen.Add(key) && Problems.Count < 60)
                Problems.Add(text);
        }

        public void Frame(ReadOnlySpan<GameAction> down, double2 mouse = default, double scroll = 0)
        {
            Frames++;
            var oof = Hud.OutOfFrame;
            try
            {
                Program.SweepFrame(G, Now, down, mouse, scroll);
            }
            catch (Exception ex)
            {
                Note("input " + ex.GetType().Name + ex.StackTrace?.Split('\n').FirstOrDefault(), $"{Where}: input threw {ex.GetType().Name}: {ex.Message} at {Top(ex)}");
                Program.SweepRelease(G);
            }
            if (G.SandboxLookText.Length > 0)
                Looked++; // (the crosshair was on something the input names: the sweep reaches the parts)
            if (Hud.OutOfFrame != oof)
                Note("oof " + Where, $"{Where}: HUD text was asked for outside an ImGui frame (a native crash in the game)");
            var e0 = Ui.ImGuiErrors;
            ImGui.NewFrame();
            try
            {
                Program.SweepDraw(G, Now);
            }
            catch (Exception ex)
            {
                Note("draw " + ex.GetType().Name + ex.StackTrace?.Split('\n').FirstOrDefault(), $"{Where}: the ImGui pass threw {ex.GetType().Name}: {ex.Message} at {Top(ex)}");
                Program.SweepRelease(G);
            }
            ImGui.Render();
            FakeTextureUploads(ImGui.GetDrawData());
            Ui.CountImGuiErrors();
            ImString.SharedStorage.Reset();
            if (Ui.ImGuiErrors != e0)
                Note("imgui " + Where, $"{Where}: {Ui.ImGuiErrors - e0} ImGui error(s) in the Sandbox pass");
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                Note("tick " + ex.GetType().Name + ex.StackTrace?.Split('\n').FirstOrDefault(), $"{Where}: the tick after it threw {ex.GetType().Name}: {ex.Message} at {Top(ex)}");
            }
        }

        private static string Top(Exception ex) => string.Join(" < ", (ex.StackTrace ?? "").Split('\n').Take(4).Select(s => s.Trim().Replace("at CallOfSlop.", "")));

        /// <summary>One sweep case: the tool out (and its mode), then every interaction pressed, held two frames and let go; the wheel both ways; MMB with a flick.</summary>
        public void Case(SweepTool tool, ConstructPartDef target, Func<bool> stillThere, bool tail = true)
        {
            var me = G.Local!;
            G.SandboxSend(CActKind.Cancel);
            G.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
            if (G.SandboxHands.Hand(me.Id).Held != ConstructParts.None)
                G.SandboxSend(CActKind.Drop);
            Program.SweepRelease(G);
            switch (tool)
            {
                case SweepTool.ThirdPerson:
                    ThirdPerson.FootOn = true;
                    break;
                case SweepTool.Wrench:
                    G.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Wrench);
                    break;
                case SweepTool.Holding:
                    G.SandboxSend(CActKind.Tool, part: (byte)(target.Tool == ConstructTool.None ? ConstructTool.Wrench : target.Tool));
                    if (G.IsServer)
                        G.SandboxHands.Hand(me.Id).Held = target.Id; // (the parts menu's pick; a physics-only one too, as PlaceHold does)
                    else
                        G.SandboxTakePart(target.Id);
                    break;
                case SweepTool.WelderSocket:
                case SweepTool.WelderFreeform:
                case SweepTool.WelderPoint:
                    G.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
                    Program.SweepWeldModes.Mode = tool switch
                    {
                        SweepTool.WelderFreeform => SandboxWeldModes.Option.Freeform,
                        SweepTool.WelderPoint => SandboxWeldModes.Option.Point, // (board item pw01: LMB picks the point, the rest of the keys walk its steps)
                        _ => SandboxWeldModes.Option.Socket,
                    };
                    break;
                case SweepTool.PhysicsGun:
                    G.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.PhysicsGun);
                    break;
                case SweepTool.Linker:
                    G.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Linker);
                    break;
                case SweepTool.LinkerRope:
                    G.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Linker);
                    Program.LinkTestWheel.Choose((int)SandboxLinkWheel.Option.Rope);
                    break;
                case SweepTool.Configure:
                    G.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Configure);
                    break;
                case SweepTool.Paint:
                    G.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Paint);
                    break;
                case SweepTool.Remover:
                    G.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Remover);
                    break;
                case SweepTool.GroundDrill:
                    G.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.GroundDrill);
                    break;
            }
            Frame([]);
            Span<GameAction> one = stackalloc GameAction[1];
            foreach (var key in SweepKeys)
            {
                if (!stillThere())
                    return;
                one[0] = key;
                Frame(one);
                if (key == GameAction.Interact && G.IsServer && me.Sim.RideClimb > 0)
                    Climbs++; // (F at a construct's side: the grab-on, the developer's crash)
                Frame(one, new double2(3, -2));
                Frame([]);
                if (key == GameAction.Fire && tool is SweepTool.Configure or SweepTool.Remover)
                {
                    // Shift + click: configure selects more of a kind, the remover adds to its selection.
                    Program.TestShift = true;
                    Frame(one);
                    Frame([]);
                    Program.TestShift = false;
                }
            }
            if (!stillThere() || !tail)
            {
                G.SandboxSend(CActKind.Stand);
                Program.SweepRelease(G);
                return;
            }
            // The wheel (the physics gun's distance, the socket weld's spin, the link adjust, third person's zoom, the gun slot).
            one[0] = GameAction.Fire;
            Frame(one);
            Frame(one, scroll: 1);
            Frame(one, scroll: -1);
            Frame([], scroll: 1);
            Frame([]);
            // MMB: the tool wheel, flicked to a tool and let go.
            one[0] = GameAction.ToolWheel;
            Frame(one);
            Frame(one, new double2(60, -40));
            Frame([]);
            Frame([]);
            G.SandboxSend(CActKind.Stand); // (F on a seat sat down)
            Program.SweepRelease(G);
        }
    }

    private static readonly SweepTool[] SweepTools = Enum.GetValues<SweepTool>();

    private static void SweepOffline(Map yard, SandboxMap sb, List<string> problems, HashSet<string> seen)
    {
        yard.RestoreProps();
        World.SetMap(yard);
        sb.DebrisSeconds = 0;
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        if (SpawnLocalSafely(g, ref now) is { } why)
        {
            Fail($"interaction sweep: {why}");
            return;
        }
        var me = g.Local!;
        if (OpenSpot(g.Constructs) is not { } spot)
        {
            Fail("interaction sweep: no open level ground on the yard");
            return;
        }
        var w = g.Constructs;
        var clock = Stopwatch.StartNew();
        var run = new SweepRunner { G = g, Problems = problems, Seen = seen, Now = now };
        run.Tick = () =>
        {
            run.Now += 1.0 / World.TickRate;
            g.Update(run.Now, 1.0 / World.TickRate);
        };
        var kinds = new HashSet<string>();
        var cases = 0;
        var defs = ConstructParts.All.Where(d => d.Id != ConstructParts.None).ToList();
        defs.Add(ExPiece(4, 25, 25, 200)); // (a piece from an Extruder: no part file, the same paths: a preset and a custom section)
        defs.Add(Extrusion.Def(new ExtrusionKey(0x0000_00FF_FF00_0000ul | 0x1818_1818ul, 40, 40, 150, RecyclingTable.Current.IdOf("wood"), false))!);
        foreach (var d in defs)
        {
            var first = kinds.Add(SweepKind(d));
            var states = new List<SweepState> { SweepState.Mounted };
            if (first || d.IsReceiverGun)
                states.AddRange([SweepState.Loose, SweepState.Frozen, SweepState.Moving]);
            if (d.Role == PartRole.Gun)
                states.AddRange([SweepState.WithParts, SweepState.Merged]);
            if (d.IsExtruder)
                states.Add(SweepState.WithParts);
            if (d.Piece != EnginePiece.None && d.PieceSize == 0)
                states.Add(SweepState.WithParts); // (a modular engine piece in a whole running engine)
            foreach (var st in states)
            {
                // Every tool on every part's first state; one of each kind (and every gun) gets them in every state, the rest the hands, wrench and holding.
                var tools = first || d.Role == PartRole.Gun || st == SweepState.Mounted ? SweepTools : [SweepTool.Hands, SweepTool.Wrench, SweepTool.Holding];
                foreach (var tool in tools)
                {
                    while (w.Bodies.Count > 0)
                        w.Remove(w.Bodies[^1]);
                    if (SweepBuild(w, d, st, spot, out var part) is not { } c)
                        break;
                    var parts = c.PartCount;
                    var keepMoving = st == SweepState.Moving;
                    if (keepMoving)
                    {
                        c.Vel = new Vector3(1.5f, 0, 1f);
                        c.Wake();
                    }
                    me.Sim.RideClimb = 0;
                    SweepAim(g, me, c, part, (cases & 1) == 0 ? 1 : -1);
                    run.Where = $"offline {d.Name}{(d.Generated ? " (generated)" : "")} {st}, {tool}";
                    cases++;
                    run.Case(tool, d, () => w.Get(c.Id) == c && c.PartCount == parts, tail: first || st != SweepState.Mounted || d.Role == PartRole.Gun); // (the wheel and MMB, which depend on the tool, not the part: once per kind)
                    if (me.Alive == false)
                    {
                        g.SelfTestRespawn(me, run.Now);
                        me.ImmuneUntil = run.Now + 1e6;
                    }
                }
            }
        }
        while (w.Bodies.Count > 0)
            w.Remove(w.Bodies[^1]);
        Line($"interaction sweep offline: {defs.Count} part types ({kinds.Count} kinds), {cases} cases, {run.Frames} frames ({run.Looked} with a look line), {run.Climbs} grab-ons, in {clock.Elapsed.TotalSeconds:0.0} s");
        if (run.Looked < run.Frames / 3)
            problems.Add($"offline: only {run.Looked} of {run.Frames} frames had the crosshair on something (the sweep isn't reaching the parts)");
        if (run.Climbs == 0)
            problems.Add("offline: F at a mounted part never grabbed on (the developer's crash path wasn't reached)");
    }

    private static void SweepOnline(Map yard, SandboxMap sb, List<string> problems, HashSet<string> seen)
    {
        const ushort port = 26461;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        sb.DebrisSeconds = 0;
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.5, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            Pump(server, [a], 0.25, ref now, clock);
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp) || server.Local == null)
        {
            Fail("interaction sweep online: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        if (!server.Local.Alive)
            server.SelfTestRespawn(server.Local, now);
        Pump(server, [a], 0.5, ref now, clock);
        sp.ImmuneUntil = server.Local.ImmuneUntil = 1e9;
        var simHost = server.Net.SimulateBadNetwork(50, 10, 3);
        var simClient = a.Net.SimulateBadNetwork(50, 10, 3);
        var w = server.Constructs;
        if (OpenSpot(w) is not { } spot)
        {
            Fail("interaction sweep online: no open level ground on the yard");
            return;
        }
        // One of each kind, and every generated receiver's first, mounted; the guns also with parts and merged.
        var kinds = new HashSet<string>();
        var picks = new List<(ConstructPartDef D, SweepState St)>();
        foreach (var d in ConstructParts.All.Where(d => d.Id != ConstructParts.None).Append(ExPiece(4, 25, 25, 200)))
            if (kinds.Add($"{d.Role}|{d.Logic}|{d.IsReceiverGun}|{d.HasJoint}|{d.Slots.Length > 0}|{d.IsGunBench}|{d.HoseEnd}|{d.Boarding}|{ConstructCargo.IsVolume(d)}|{d.IsExtruder}|{d.IsExtrusion}"))
            {
                picks.Add((d, SweepState.Mounted));
                if (d.IsExtruder)
                    picks.Add((d, SweepState.WithParts));
                if (d.Role == PartRole.Gun)
                {
                    picks.Add((d, SweepState.WithParts));
                    picks.Add((d, SweepState.Merged));
                }
            }
        var frameNow = now;
        void Step()
        {
            var t = clock.Elapsed.TotalSeconds;
            var dt = Math.Clamp(t - frameNow, 0.001, 0.1);
            frameNow = t;
            server.Update(t, dt);
            a.Update(t, dt);
        }
        var host = new SweepRunner { G = server, Problems = problems, Seen = seen, Tick = Step };
        var client = new SweepRunner { G = a, Problems = problems, Seen = seen, Tick = Step };
        var cases = 0;
        var missing = 0;
        foreach (var (d, st) in picks)
        {
            while (w.Bodies.Count > 0)
                w.Remove(w.Bodies[^1]);
            if (SweepBuild(w, d, st, spot, out var part) is not { } c)
                continue;
            c.Pinned = d.Role == PartRole.Gun && st != SweepState.Mounted; // (a merged square or a fitted gun stays put while both peers work at it)
            var id = c.Id;
            var parts = c.PartCount;
            var until = clock.Elapsed.TotalSeconds + 3;
            while (clock.Elapsed.TotalSeconds < until && a.Constructs.Get(id) is not { } got)
                Pump(server, [a], 0.05, ref now, clock);
            if (a.Constructs.Get(id) == null)
            {
                missing++;
                continue;
            }
            foreach (var peer in new[] { client, host })
                foreach (var tool in SweepTools)
                {
                    if (w.Get(id) is not { } sc0 || sc0.PartCount != parts || a.Constructs.Get(id) == null)
                    {
                        // (taken apart or deleted by the last case: built again, and the client waited for)
                        while (w.Bodies.Count > 0)
                            w.Remove(w.Bodies[^1]);
                        if (SweepBuild(w, d, st, spot, out part) is not { } again)
                            break;
                        again.Pinned = c.Pinned;
                        id = again.Id;
                        parts = again.PartCount;
                        var by = clock.Elapsed.TotalSeconds + 3;
                        while (clock.Elapsed.TotalSeconds < by && a.Constructs.Get(id) == null)
                            Pump(server, [a], 0.05, ref now, clock);
                        if (a.Constructs.Get(id) == null)
                        {
                            missing++;
                            break;
                        }
                    }
                    var sc = w.Get(id)!;
                    var mine = peer.G.Constructs.Get(id)!;
                    var pl = peer.G.Local!;
                    var spl = server.Players[pl.Id];
                    spl.Sim.RideClimb = 0;
                    SweepAim(server, spl, sc, Math.Min(part, sc.PartCount - 1), (cases & 1) == 0 ? 1 : -1);
                    if (peer.G != server)
                    {
                        pl.Teleport(spl.Position);
                        SweepAim(peer.G, pl, mine, Math.Min(part, mine.PartCount - 1), (cases & 1) == 0 ? 1 : -1);
                        pl.Teleport(spl.Position);
                    }
                    peer.Where = $"online {(peer.G == server ? "host" : "client")} {d.Name}{(d.Generated ? " (generated)" : "")} {st}, {tool}";
                    cases++;
                    peer.Case(tool, d, () => w.Get(id) is { } x && x.PartCount == parts, tail: d.Role == PartRole.Gun || picks.IndexOf((d, st)) < 12);
                    if (!spl.Alive)
                        server.SelfTestRespawn(spl, now);
                }
        }
        if (!simHost || !simClient)
            problems.Add("online: simulated lag and loss weren't on");
        Pump(server, [a], 0.6, ref now, clock);
        while (w.Bodies.Count > 0)
            w.Remove(w.Bodies[^1]);
        Pump(server, [a], 0.3, ref now, clock);
        Line($"interaction sweep online: {picks.Count} picks ({kinds.Count} kinds), {cases} cases, {host.Frames} host and {client.Frames} client frames, {missing} never reached the client, in {clock.Elapsed.TotalSeconds:0.0} s");
        if (missing > picks.Count / 4)
            problems.Add($"online: {missing} of {picks.Count} test constructs never reached the client");
    }
}
