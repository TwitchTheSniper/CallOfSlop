using System.Diagnostics;
using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Point weld" (board item dev-point-weld-pw01; docs/sandbox/CONSTRUCTS.md, "Point weld"). Through the real input
    /// (Program.SweepFrame outside an ImGui frame, Program.SweepDraw inside a real headless one with error recovery, then a
    /// tick) on the Construct Yard: the welder's ring has Point weld beside socket and freeform weld (socket weld still the
    /// default); a loose panel's clicked point is welded onto a clicked point on a frame block's face, face to face, spun 30
    /// degrees with the wheel (and R's half turns): the panel's point sits on the block's (1 cm), its normal opposite the
    /// block's and the spin is the one asked for (1 degree), as a fixed-pose weld; clicked points that line sockets up
    /// merge on the grid; a piece that would overlap the construct is refused with the reason; RMB, Esc, putting the welder
    /// away, another mode and the piece going away under it all clear the steps; holding still in steps 2 and 3 allocates
    /// nothing. Through the server's path (BeginPointWeld, with the points the input would pick): a panel on a motor hinge's
    /// leaf hangs on the joint and the lever drives it; a piece on the leaf that would weld to the fixed side too is refused;
    /// the buggy's wheels welded back by clicked points on their hubs and the suspensions roll, spring and steer; the engine
    /// welded back onto the gearbox by clicked points drives the buggy. Then a real host and a separate client: the client
    /// point-welds through its input and both copies agree on the pose.
    /// </summary>
    private static void CheckPointWeld()
    {
        var modes = new SandboxWeldModes();
        var ring = modes.Options.All(o => !o.Label.Contains("Point", StringComparison.Ordinal)) && modes.Mode == SandboxWeldModes.Option.Socket;
        Line($"point weld: the welder's ring: {string.Join(", ", modes.Options.Select(o => o.Label))}; socket attach the default, point attach is its Shift");
        if (!ring)
            Fail("point weld: the ring still lists it, or socket attach isn't the default");
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail($"point weld: the {ConstructYardMap} map is missing");
            return;
        }
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
        var debris = sb.DebrisSeconds;
        var problems = new List<string>();
        try
        {
            PwOffline(yard, sb, problems);
            PwOnline(yard, sb, problems);
        }
        finally
        {
            Program.SandboxGuardRethrow = false;
            Game.SandboxActRethrow = false;
            Program.SweepWeldModes.Mode = SandboxWeldModes.Option.Socket;
            Program.SweepPointHeld = false;
            sb.DebrisSeconds = debris;
            ImGui.DestroyContext(ctx);
        }
        foreach (var p in problems.Take(20))
            Fail($"point weld: {p}");
        PwJoint();
        PwWheels();
        PwGearbox();
    }

    /// <summary>Turns the look onto a world point from the player's eye.</summary>
    private static void PwLook(Game g, Player me, Vector3 at)
    {
        var dir = at - me.RenderEye;
        dir = dir.LengthSquared() < 1e-6f ? -Vector3.UnitZ : Vector3.Normalize(dir);
        g.LookPitch = MathF.Asin(Math.Clamp(dir.Y, -1f, 1f));
        g.LookYaw = MathF.Atan2(dir.X, -dir.Z);
    }

    /// <summary>A point on part <paramref name="part"/>'s box face (its segment rest frame: <paramref name="local"/>, outward <paramref name="n"/>) in the world.</summary>
    private static (Vector3 P, Vector3 N) PwWorld(Construct c, int part, Vector3 local, Vector3 n)
    {
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        return (t + Vector3.Transform(local, q), Vector3.Normalize(Vector3.Transform(n, q)));
    }

    /// <summary>Stands the player off a face point (along its normal, kept level) and looks at it.</summary>
    private static void PwStand(Game g, Player me, Vector3 p, Vector3 n, float dist = 1.6f, Action? settle = null)
    {
        var off = n with { Y = 0 };
        if (off.LengthSquared() < 0.05f)
            off = Vector3.UnitX; // (a top face: from the side, looking down onto it)
        off = Vector3.Normalize(off);
        var at = p + off * dist;
        me.Teleport(ConstructWorld.Ground(World.Query, at.X, at.Z) + new Vector3(0, 0.05f, 0));
        settle?.Invoke(); // (the player lands and the drawn eye catches up: the aim ray starts from it)
        PwLook(g, me, p);
    }

    private static readonly GameAction[] PwFire = [GameAction.Fire];
    private static readonly GameAction[] PwAimKey = [GameAction.Aim];
    private static readonly GameAction[] PwReload = [GameAction.Reload];

    private static void PwOffline(Map yard, SandboxMap sb, List<string> problems)
    {
        yard.RestoreProps();
        World.SetMap(yard);
        sb.DebrisSeconds = 0;
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        if (SpawnLocalSafely(g, ref now) is { } why)
        {
            problems.Add(why);
            return;
        }
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var w = g.Constructs;
        if (OpenSpot(w) is not { } spot)
        {
            problems.Add("no open level ground on the yard");
            return;
        }
        var seen = new HashSet<string>();
        var run = new SweepRunner { G = g, Problems = problems, Seen = seen, Now = now, Where = "point weld offline" };
        run.Tick = () =>
        {
            run.Now += 1.0 / World.TickRate;
            g.Update(run.Now, 1.0 / World.TickRate);
        };
        void Settle()
        {
            for (var k = 0; k < 20; k++)
                run.Tick();
        }
        var flow = Program.PointWeldTestFlow;
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
        Program.SweepRelease(g);
        Program.SweepWeldModes.Mode = SandboxWeldModes.Option.Socket;
        Program.SweepPointHeld = true;
        run.Frame([]);

        // The target: frame block A (pinned, 0.9 m up), block B on A's +x, block C on B's top (for the overlap refusal).
        var block = ConstructParts.Need(SwFrame);
        var target = w.Create(spot + new Vector3(0, 0.9f, 0), Quaternion.Identity)!;
        w.AddPart(target, block, Vector3.Zero, 0);
        target.Pinned = true;
        target.Rebuild();
        var b = FitTurned(w, target, 0, "+x", SwFrame, "-x", _ => true);
        var cTop = b < 0 ? -1 : FitTurned(w, target, b, "+y", SwFrame, "-y", _ => true);
        if (cTop < 0)
        {
            problems.Add("the target blocks don't build");
            return;
        }
        Construct? NewPanel(Vector3 at) => FwLoose(w, "Panel", at, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f), frozen: true);

        // 1. A panel's clicked point (on its top face, off its sockets) onto a point on A's -x face (off its socket), spun 30 degrees.
        if (NewPanel(spot + new Vector3(-0.6f, 1.0f, 2.4f)) is not { } panel)
            return;
        var (sp, sn) = PwWorld(panel, 0, new Vector3(0.12f, 0.05f, 0.07f), Vector3.UnitY);
        PwStand(g, me, sp, sn, 1.4f, settle: Settle);
        run.Frame([]);
        var marker = Program.PointWeldTestText.Contains("pick this point");
        run.Frame(PwFire);
        run.Frame([]);
        var step2 = flow.Step == SocketWeldFlow.Stage.Target && flow.Source == panel.Id && flow.SourceSocket == SocketWeld.FacePoint;
        var (tp, tn) = PwWorld(target, 0, new Vector3(-0.25f, -0.06f, 0.08f), -Vector3.UnitX);
        PwStand(g, me, tp, tn, 1.5f, settle: Settle);
        run.Frame([]);
        var ghost = SocketWeldRender.Ghost == panel && SocketWeldRender.Valid && SocketWeldRender.PointOn && SocketWeldRender.TargetOn;
        var why2 = Program.PointWeldTestWhy;
        run.Frame(PwFire);
        run.Frame([]);
        var step3 = flow.Step == SocketWeldFlow.Stage.Rotate;
        run.Frame([], scroll: 1);
        run.Frame([], scroll: 1);
        var a30 = flow.Angle;
        run.Frame(PwReload);
        run.Frame([]);
        var a210 = flow.Angle;
        run.Frame(PwReload);
        run.Frame([]);
        var back30 = flow.Angle;
        // Holding still in step 3 (and the look line worked out once): no allocation in the input.
        for (var k = 0; k < 4; k++)
            Program.SweepFrame(g, run.Now, []);
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 30; k++)
            Program.SweepFrame(g, run.Now, []);
        var alloc3 = GC.GetAllocatedBytesForCurrentThread() - a0;
        var src = flow.Src;
        var wt = flow.Target;
        SocketWeld.Pose(panel, src, target, wt, 30, out _, out _, out var wantPos, out var wantRot);
        SocketWeld.Pose(panel, src, target, wt, 0, out _, out _, out _, out var zeroRot);
        var srcWorld = wantPos + Vector3.Transform(src.At, wantRot);
        var srcNormal = Vector3.Transform(ConstructParts.DirVector(src.Dir), wantRot);
        var freeforms = target.FreeformCount;
        run.Frame(PwFire);
        for (var k = 0; k < 4; k++)
            run.Frame([]);
        var welded = w.Get(panel.Id) == null && target.FreeformCount == freeforms + 1 && PartNamed(target, "Panel") is >= 0;
        var pi = PartNamed(target, "Panel");
        var (gotPos, gotRot) = pi >= 0 ? FwWorld(target, pi) : (Vector3.Zero, Quaternion.Identity);
        var (move, turn) = FwDiff((wantPos, wantRot), (gotPos, gotRot));
        var onPoint = Vector3.Distance(srcWorld, tp);
        var opposed = Vector3.Dot(srcNormal, tn);
        var spin = 2 * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(gotRot), Quaternion.Normalize(zeroRot))), 0, 1)) * 180 / MathF.PI;
        Line($"point weld: a panel's point picked {step2} (look line {marker}), a point on the block's face: the ghost shown {ghost} ({why2 ?? "green"}); spun {a30:0} deg, R {a210:0}, R {back30:0}; welded {welded} as a fixed-pose weld: {move * 100:0.00} cm and {turn:0.00} deg off the ghost, its point {onPoint * 100:0.00} cm off the block's, normals {opposed:0.0000}, {spin:0.0} deg from angle 0; holding still allocated {alloc3} B over 30 frames");
        if (!marker || !step2 || !ghost || !step3)
            problems.Add($"the steps didn't go through (picked {step2}, ghost {ghost} {why2}, step 3 {step3})");
        if (MathF.Abs(a30 - 30) > 0.01f || MathF.Abs(a210 - 210) > 0.01f || MathF.Abs(back30 - 30) > 0.01f)
            problems.Add($"the wheel and R didn't spin it (30: {a30}, R: {a210}, R: {back30})");
        if (!welded || move > 0.01f || turn > 1f || onPoint > 0.01f || opposed > -MathF.Cos(MathF.PI / 180) || MathF.Abs(spin - 30) > 1f)
            problems.Add($"the panel wasn't welded at the clicked points (welded {welded}, {move * 100:0.00} cm / {turn:0.00} deg off, point {onPoint * 100:0.00} cm, normals {opposed:0.0000}, spin {spin:0.0})");
        if (alloc3 > 0)
            problems.Add($"holding still in step 3 allocated {alloc3} B");

        // 2. Clicked points that line sockets up (the panel's back socket, A's -z socket): merged on the grid, linked.
        // (stood on its edge, its back facing +z, so the player can see it)
        if (FwLoose(w, "Panel", spot + new Vector3(-0.6f, 1.2f, 2.4f), Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2), frozen: true) is not { } panel2)
            return;
        var parts0 = target.PartCount;
        var (sp2, sn2) = PwWorld(panel2, 0, new Vector3(0, -0.05f, 0), -Vector3.UnitY);
        PwStand(g, me, sp2, sn2, 1.4f, settle: Settle);
        run.Frame([]);
        var underneath = Program.PointWeldTestText.Contains("pick this point");
        run.Frame(PwFire);
        run.Frame([]);
        var picked2 = flow.Step == SocketWeldFlow.Stage.Target && flow.SourceDir == SwDirOf(panel2, "back");
        var (tp2, tn2) = PwWorld(target, b, target.Parts[b].Pos + new Vector3(0.0f, 0.0f, -0.25f), -Vector3.UnitZ);
        PwStand(g, me, tp2, tn2, 1.5f, settle: Settle);
        run.Frame([]);
        run.Frame(PwFire);
        run.Frame([]);
        var gridText = Program.PointWeldTestText;
        run.Frame(PwFire);
        for (var k = 0; k < 4; k++)
            run.Frame([]);
        var p2 = target.PartCount > parts0 ? target.PartCount - 1 : -1;
        var merged = w.Get(panel2.Id) == null && target.FreeformCount == freeforms + 1 && p2 >= 0 && target.DefOf(p2).Name == "Panel" && target.Parts[p2].Seg == 0
                     && target.SocketUsed(b, SwSocket(block, "-z"));
        Line($"point weld: points on the panel's back socket ({underneath}, {picked2}) and block B's -z socket: \"{gridText}\"; merged on the grid and linked {merged}");
        if (!merged)
            problems.Add($"clicked points on two sockets weren't merged on the grid ({gridText})");

        // 3. A refusal: the panel flat on A's top would overlap block C (on B): red with the reason, LMB goes on to the spin all the same, and step 3's LMB welds nothing.
        if (NewPanel(spot + new Vector3(-0.6f, 1.0f, 2.4f)) is not { } panel3)
            return;
        var (sp3, sn3) = PwWorld(panel3, 0, new Vector3(0, 0.05f, 0), Vector3.UnitY);
        PwStand(g, me, sp3, sn3, 1.4f, settle: Settle);
        run.Frame([]);
        run.Frame(PwFire);
        run.Frame([]);
        var (tp3, tn3) = PwWorld(target, 0, new Vector3(0.2f, 0.25f, 0), Vector3.UnitY);
        PwStand(g, me, tp3, Vector3.UnitZ, 1.3f, settle: Settle);
        run.Frame([]);
        var refused = Program.PointWeldTestWhy;
        var red = SocketWeldRender.Ghost == panel3 && !SocketWeldRender.Valid;
        var parts3 = target.PartCount;
        run.Frame(PwFire);
        for (var k = 0; k < 3; k++)
            run.Frame([]);
        var spinStep = flow.Step == SocketWeldFlow.Stage.Rotate;
        run.Frame(PwFire);
        for (var k = 0; k < 3; k++)
            run.Frame([]);
        var stayed = spinStep && w.Get(panel3.Id) != null && target.PartCount == parts3 && flow.Step == SocketWeldFlow.Stage.Rotate;
        run.Frame(PwAimKey); // (back to step 2, where the cancel paths below start)
        run.Frame([]);
        Line($"point weld: the panel flat on the block's top under the wall: {(red ? "red" : "not red")}: {refused ?? "allowed"}; LMB left it {stayed}");
        if (!red || refused == null || !refused.Contains("overlap") || !stayed)
            problems.Add($"an overlapping point weld wasn't refused with the reason (red {red}, {refused ?? "allowed"}, stayed {stayed})");

        // 4. Cancel paths: RMB steps back (3 to 2 to 1), Esc cancels, the welder put away, another mode, the piece gone.
        var paths = new List<string>();
        void Pick()
        {
            PwStand(g, me, sp3, sn3, 1.4f, settle: Settle);
            run.Frame([]);
            run.Frame(PwFire);
            run.Frame([]);
        }
        void Target3()
        {
            var (tpx, tnx) = PwWorld(target, cTop, target.Parts[cTop].Pos + new Vector3(0.06f, 0.05f, 0.25f), Vector3.UnitZ);
            PwStand(g, me, tpx, tnx, 1.5f, settle: Settle);
            run.Frame([]);
            run.Frame(PwFire);
            run.Frame([]);
        }
        Pick();
        Target3();
        var s3 = flow.Step;
        run.Frame(PwAimKey);
        var s2 = flow.Step;
        run.Frame([]);
        run.Frame(PwAimKey);
        var s1 = flow.Step;
        run.Frame([]);
        paths.Add($"RMB {s3} -> {s2} -> {s1}");
        var rmbOk = s3 == SocketWeldFlow.Stage.Rotate && s2 == SocketWeldFlow.Stage.Target && s1 == SocketWeldFlow.Stage.Source && flow.Source == 0;
        Pick();
        var escOk = flow.Step == SocketWeldFlow.Stage.Target && Program.PointWeldTestEsc() && flow.Step == SocketWeldFlow.Stage.Source && !Program.PointWeldTestEsc();
        paths.Add($"Esc {escOk}");
        Pick();
        Target3();
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
        run.Frame([]);
        var awayOk = flow.Step == SocketWeldFlow.Stage.Source && flow.Source == 0 && SocketWeldRender.Ghost == null;
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
        run.Frame([]);
        paths.Add($"put away {awayOk}");
        Pick();
        Program.SweepWeldModes.Mode = SandboxWeldModes.Option.Freeform;
        run.Frame([]);
        var modeOk = flow.Step == SocketWeldFlow.Stage.Source && flow.Source == 0;
        Program.SweepWeldModes.Mode = SandboxWeldModes.Option.Socket;
        Program.SweepPointHeld = true;
        run.Frame([]);
        paths.Add($"another mode {modeOk}");
        Pick();
        Target3();
        w.Remove(panel3);
        run.Frame([]);
        var goneOk = flow.Step == SocketWeldFlow.Stage.Source && flow.Source == 0;
        paths.Add($"piece gone {goneOk}");
        Line($"point weld: cancel paths: {string.Join("; ", paths)}");
        if (!rmbOk || !escOk || !awayOk || !modeOk || !goneOk)
            problems.Add($"a cancel path didn't clear the point weld ({string.Join("; ", paths)})");

        // 5. Holding still in step 2 allocates nothing either.
        if (NewPanel(spot + new Vector3(-0.6f, 1.0f, 2.4f)) is not { } panel4)
            return;
        var (sp4, sn4) = PwWorld(panel4, 0, new Vector3(0.1f, 0.05f, -0.1f), Vector3.UnitY);
        PwStand(g, me, sp4, sn4, 1.4f, settle: Settle);
        run.Frame([]);
        run.Frame(PwFire);
        run.Frame([]);
        var (tp4, tn4) = PwWorld(target, 0, new Vector3(-0.25f, 0.05f, 0.05f), -Vector3.UnitX);
        PwStand(g, me, tp4, tn4, 1.5f, settle: Settle);
        for (var k = 0; k < 4; k++)
            Program.SweepFrame(g, run.Now, []);
        var b0 = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 30; k++)
            Program.SweepFrame(g, run.Now, []);
        var alloc2 = GC.GetAllocatedBytesForCurrentThread() - b0;
        Line($"point weld: holding still in step 2 (the ghost live) allocated {alloc2} B over 30 frames (step 2: {flow.Step})");
        if (alloc2 > 0 || flow.Step != SocketWeldFlow.Stage.Target)
            problems.Add($"holding still in step 2 allocated {alloc2} B (or the step was lost: {flow.Step})");
        Program.SweepRelease(g);
    }

    private static byte SwDirOf(Construct c, string socket)
    {
        c.SocketAt(0, SwSocket(c.DefOf(0), socket), out _, out var d);
        return d;
    }

    /// <summary>The point weld through the server (BeginPointWeld) at a source and a target point given in their parts' frames, as the input picks them.</summary>
    private static string? PwServer(ConstructWorld w, ConstructEdits hands, Vector3 eye, Construct mover, int mPart, Vector3 mLocal, Vector3 mNormal, Construct target, int tPart, Vector3 tLocal, Vector3 tNormal, float degrees, out WeldTarget src, out WeldTarget wt)
    {
        wt = default;
        var (mp, mn) = PwWorld(mover, mPart, mLocal, mNormal);
        var (tp, tn) = PwWorld(target, tPart, tLocal, tNormal);
        if (!PointWeld.OriginAt(mover, mPart, mp, mn, out src))
            return "no point on the piece there";
        if (!SocketWeld.FaceAt(target, tPart, tp, tn, out wt))
            return "no point on the target there";
        return hands.BeginPointWeld(w, 0, eye, mover.Id, src, wt, degrees, 0);
    }

    /// <summary>A panel point-welded onto a motor hinge's leaf hangs on the joint and the lever drives it; a block that would also weld to the fixed side is refused.</summary>
    private static void PwJoint()
    {
        var q = new JointTestWorld();
        var joint = ConstructParts.Need("Motor Hinge");
        var w = new ConstructWorld();
        if (JwRig(w, q, joint, out var jp, out var lever, out _) is not { } c)
        {
            Fail("point weld: the motor hinge rig doesn't build");
            return;
        }
        JwLeaf(c, jp, out var leafAt, out var leafDir);
        c.SocketAt(jp, joint.LeafIndex, out var la, out var ld);
        var perp = ConstructParts.DirVector(SocketWeld.PerpDir(ld));
        if (FwLoose(w, "Panel", leafAt + new Vector3(-1.4f, 0.6f, 0.3f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f), frozen: true) is not { } panel)
            return;
        var eye = leafAt + leafDir * 1.0f + new Vector3(-0.8f, 0.4f, 0.4f);
        var why = PwServer(w, FwHands(), eye, panel, 0, new Vector3(0.05f, -0.05f, 0.02f), -Vector3.UnitY, c, jp, la + perp * 0.04f, ConstructParts.DirVector(ld), 0, out var src, out var wt);
        c.Rebuild();
        var pi = PartNamed(c, "Panel");
        var j = c.JointOf(jp);
        var hangs = why == null && pi >= 0 && j >= 0 && c.Under(pi, j + 1);
        var before = pi >= 0 ? FwWorld(c, pi) : default;
        JwDrive(w, q, c, jp, lever, 1, JwDriveTicks);
        var after = pi >= 0 ? FwWorld(c, pi) : default;
        var angle = j >= 0 ? c.Joints[j].Angle : 0;
        var (move, turn) = FwDiff(before, after);
        Line($"point weld: a panel's point onto a point on the motor hinge's leaf ({SocketWeld.OnLeaf(c, wt)}): {why ?? "welded"}, hangs on the joint {hangs}; the lever drove it {Deg(angle):0.0} deg: the panel moved {move * 100:0.0} cm and turned {turn:0.0} deg");
        if (!hangs)
            Fail($"point weld: a panel welded onto a joint's leaf by clicked points doesn't hang on the joint ({why ?? "not on the joint's segment"})");
        else if (MathF.Abs(Deg(angle)) < 5f || turn < 5f)
            Fail($"point weld: the joint didn't move the panel welded onto its leaf (angle {Deg(angle):0.0}, panel turned {turn:0.0} deg)");

        // The lock: a block on the leaf whose side would meet a block's on the fixed side (JwLock's wall), by clicked points.
        foreach (var wall in new[] { true, false })
        {
            var lw = new ConstructWorld();
            if (JwRig(lw, q, joint, out var ljp, out _, out _) is not { } lc)
                return;
            var piece = ConstructParts.Need(SwFrame);
            ConstructGhost g = default;
            var ok = false;
            for (var s = 0; s < 4 && !ok; s++)
                ok = ConstructBuild.Place(lc, ljp, joint.LeafIndex, piece, SwSocket(piece, "-y"), s, out g) == null;
            if (wall)
            {
                var b0 = FitTurned(lw, lc, 0, "top21", "Lever", "-y", _ => true);
                var b1 = b0 < 0 ? -1 : FitTurned(lw, lc, b0, "+y", SwFrame, "-y", _ => true);
                if (b1 < 0 || FitTurned(lw, lc, b1, "+y", SwFrame, "-y", _ => true) < 0)
                {
                    Fail("point weld: the lock test's wall doesn't build");
                    return;
                }
            }
            JwLeaf(lc, ljp, out var lAt, out _);
            if (FwLoose(lw, SwFrame, lAt + new Vector3(-1.5f, 0.8f, 0.4f), Quaternion.Identity, frozen: true) is not { } loose)
                return;
            SocketWeld.SourceFrame(loose, 0, SwSocket(piece, "-y"), out _, out var sDir);
            lc.SocketAt(ljp, joint.LeafIndex, out var lla, out var lld);
            var degrees = SocketWeld.AngleOfPartTurn(loose, 0, sDir, lld, g.Rot);
            var lwhy = PwServer(lw, FwHands(), lAt + new Vector3(-1, 0.6f, 1), loose, 0, new Vector3(0.01f, -0.25f, 0), -Vector3.UnitY, lc, ljp, lla, ConstructParts.DirVector(lld), degrees, out _, out _);
            Line($"point weld: a frame block by clicked points onto the motor hinge's leaf {(wall ? "beside a wall on the fixed side" : "with nothing beside it")}: {lwhy ?? "welded"}");
            if (wall && (lwhy == null || !lwhy.Contains("lock")))
                Fail($"point weld: a piece on a joint's leaf welding to the fixed side too wasn't refused ({lwhy ?? "welded"})");
            if (!wall && lwhy != null)
                Fail($"point weld: a piece on a joint's leaf with nothing beside it was refused: {lwhy}");
        }
    }

    /// <summary>The buggy's wheels welded back on by clicked points on each hub's inner face and on the suspension's axle face: they roll, spring and steer like the wrench's.</summary>
    private static void PwWheels()
    {
        if (ConstructBuild.FindBlueprint("Buggy") is not { } bp || ConstructParts.Find("Wheel") is not { } wheelDef)
        {
            Fail("point weld: no Buggy blueprint");
            return;
        }
        var q = new DriveWorld();
        var refWorld = new ConstructWorld();
        if (DriveSpawn(refWorld, bp, 0, 0, MathF.PI / 2, "the buggy") is not { } refCar || WwMeasure(refWorld, q, refCar, "point weld reference") is not { } reference)
            return;
        var w = new ConstructWorld();
        if (DriveSpawn(w, bp, 0, 0, MathF.PI / 2, "the buggy") is not { } car)
            return;
        WwSettle(w, q, car);
        for (var i = car.PartCount - 1; i >= 0; i--)
            if (car.DefOf(i).Role == PartRole.Wheel)
                w.RemovePart(car, i);
        car.Rebuild();
        var hands = FwHands();
        var wheelAxle = SwSocket(wheelDef, "axle");
        var welded = 0;
        var notes = new List<string>();
        for (var n = 0; n < 4; n++)
        {
            int susp = -1, axle = -1;
            for (var i = 0; i < car.PartCount && susp < 0; i++)
                if (car.DefOf(i).Role == PartRole.Suspension && (axle = SwSocket(car.DefOf(i), "axle")) >= 0 && !car.SocketUsed(i, axle))
                    susp = i;
            if (susp < 0)
                break;
            car.SocketAt(susp, axle, out var tAt, out var tDir);
            var (aw, an) = PwWorld(car, susp, tAt, ConstructParts.DirVector(tDir));
            if (FwLoose(w, "Wheel", aw + an * 1.1f + new Vector3(0, 0.4f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f + n), frozen: true) is not { } loose)
                return;
            loose.SocketAt(0, wheelAxle, out var wAt, out var wDir);
            var tPerp = ConstructParts.DirVector(SocketWeld.PerpDir(tDir));
            var wPerp = ConstructParts.DirVector(SocketWeld.PerpDir(wDir));
            // A click a few centimetres off each axle's centre (where a player hits the hub and the plate), spun a little.
            var why = PwServer(w, hands, aw + an * 0.8f + new Vector3(0, 1.1f, 0), loose, 0, wAt + wPerp * 0.03f, ConstructParts.DirVector(wDir), car, susp, tAt + tPerp * 0.02f, ConstructParts.DirVector(tDir), 20f * n, out _, out _);
            var ok = why == null && car.SocketUsed(susp, axle) && car.FreeformCount == 0;
            notes.Add(ok ? "on its axle" : $"not on its axle ({why ?? $"{car.FreeformCount} freeform"})");
            if (ok)
                welded++;
        }
        car.Rebuild();
        Line($"point weld: the buggy's wheels by clicked points: {string.Join(", ", notes)}; {car.WheelCount} raycast wheels (the wrench's {reference.Wheels})");
        if (welded != 4 || car.WheelCount != reference.Wheels)
        {
            Fail($"point weld: wheels welded by clicked points onto the suspensions' axles aren't rolling wheels ({string.Join(", ", notes)})");
            return;
        }
        if (WwMeasure(w, q, car, "point-welded") is not { } r)
            return;
        var rel = MathF.Abs(r.Dist - reference.Dist) / MathF.Max(reference.Dist, 0.1f);
        if (r.Steers != reference.Steers || r.Spin < 3f || r.Peak < r.Rest + 0.01f || rel > 0.1f || MathF.Sign(r.Yaw) != MathF.Sign(reference.Yaw))
            Fail($"point weld: the point-welded buggy doesn't roll, spring or steer like the wrench's (steer {r.Steers}/{reference.Steers}, spin {r.Spin:0.0}, {r.Dist:0.00} vs {reference.Dist:0.00} m)");
        if (r.Bytes > 0)
            Fail($"point weld: {r.Bytes} bytes allocated driving the point-welded buggy");
    }

    /// <summary>The buggy's engine taken off and point-welded back onto the gearbox by clicked points near their joining sockets: the drivetrain works.</summary>
    private static void PwGearbox()
    {
        if (ConstructBuild.FindBlueprint("Buggy") is not { } bp)
            return;
        var q = new DriveWorld();
        var refWorld = new ConstructWorld();
        if (DriveSpawn(refWorld, bp, 0, 0, MathF.PI / 2, "the buggy") is not { } refCar || FdRun(refWorld, q, refCar, "point weld reference") is not { } reference)
            return;
        var w = new ConstructWorld();
        if (DriveSpawn(w, bp, 0, 0, MathF.PI / 2, "the buggy") is not { } car)
            return;
        WwSettle(w, q, car);
        var trans = PartNamed(car, "Transmission");
        var engine = PartNamed(car, "Engine");
        if (trans < 0 || engine < 0)
        {
            Fail("point weld: the buggy has no transmission or engine");
            return;
        }
        var edef = car.DefOf(engine);
        var tdef = car.DefOf(trans);
        var eBy = SwSocket(edef, "+z");
        var tOn = SwSocket(tdef, "-z");
        w.RemovePart(car, engine);
        car.Rebuild();
        trans = PartNamed(car, "Transmission");
        if (FwLoose(w, "Engine", car.WorldCom + new Vector3(0, 1.5f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.9f), frozen: true) is not { } loose)
            return;
        loose.SocketAt(0, eBy, out var eAt, out var eDir);
        car.SocketAt(trans, tOn, out var tAt, out var tDir);
        var (tw, _) = PwWorld(car, trans, tAt, ConstructParts.DirVector(tDir));
        var why = PwServer(w, FwHands(), tw + new Vector3(0, 1.4f, 0.6f), loose, 0, eAt + ConstructParts.DirVector(SocketWeld.PerpDir(eDir)) * 0.01f, ConstructParts.DirVector(eDir),
            car, trans, tAt + ConstructParts.DirVector(SocketWeld.PerpDir(tDir)) * 0.01f, ConstructParts.DirVector(tDir), 0, out _, out _);
        car.Rebuild();
        var ne = PartNamed(car, "Engine");
        var tank = PartNamed(car, "Fuel Tank");
        if (why == null && ne >= 0 && tank >= 0)
        {
            var nodes = ReadOnlySpan<CRouteNode>.Empty;
            byte fuelIn = 255;
            for (byte p = 0; p < 8 && fuelIn == 255; p++)
                if (ConstructRoutes.PortOf(car.DefOf(ne), RouteType.Pipe, p) is { Fluid: "fuel" })
                    fuelIn = p;
            if (ConstructRoutes.Check(w, car, RouteType.Pipe, tank, 255, 255, 0, Vector3.Zero, nodes, ne, fuelIn) == null)
                ConstructRoutes.Add(w, car, RouteType.Pipe, tank, 255, 255, 0, Vector3.Zero, nodes, ne, fuelIn);
            car.Rebuild();
        }
        Line($"point weld: the engine by clicked points onto the gearbox: {why ?? "welded"} ({car.FreeformCount} freeform, engine linked {ne >= 0 && car.SocketUsed(trans, tOn)}), can drive {car.CanDrive} {car.DriveProblem}");
        if (why != null || ne < 0 || !car.CanDrive)
        {
            Fail($"point weld: the engine point-welded onto the gearbox doesn't make a drivetrain ({why ?? car.DriveProblem})");
            return;
        }
        if (FdRun(w, q, car, "engine point-welded onto the gearbox") is { } r)
            FdCompare(r, reference, "point-welded engine");
    }

    /// <summary>A real host and a separate client (lag, loss): the client point-welds a panel to a block through its input; both copies agree.</summary>
    private static void PwOnline(Map yard, SandboxMap sb, List<string> problems)
    {
        const ushort port = 26473;
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
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp) || a.Local == null)
        {
            problems.Add("online: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        Pump(server, [a], 0.5, ref now, clock);
        sp.ImmuneUntil = 1e9;
        server.Net.SimulateBadNetwork(40, 5, 2);
        a.Net.SimulateBadNetwork(40, 5, 2);
        var w = server.Constructs;
        if (OpenSpot(w) is not { } spot)
        {
            problems.Add("online: no open level ground");
            return;
        }
        var target = w.Create(spot + new Vector3(0, 0.9f, 0), Quaternion.Identity)!;
        w.AddPart(target, ConstructParts.Need(SwFrame), Vector3.Zero, 0);
        target.Pinned = true;
        target.Rebuild();
        if (FwLoose(w, "Panel", spot + new Vector3(-0.6f, 1.0f, 2.4f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f), frozen: true) is not { } panel)
            return;
        var frameNow = now;
        void Step()
        {
            var t = clock.Elapsed.TotalSeconds;
            var dt = Math.Clamp(t - frameNow, 0.001, 0.1);
            frameNow = t;
            server.Update(t, dt);
            a.Update(t, dt);
        }
        bool WaitFor(Func<bool> cond, double seconds)
        {
            var until = clock.Elapsed.TotalSeconds + seconds;
            while (clock.Elapsed.TotalSeconds < until && !cond())
                Pump(server, [a], 0.05, ref now, clock);
            return cond();
        }
        if (!WaitFor(() => a.Constructs.Get(target.Id) != null && a.Constructs.Get(panel.Id) != null, 3))
        {
            problems.Add("online: the client never got the block and the panel");
            return;
        }
        var seen = new HashSet<string>();
        var run = new SweepRunner { G = a, Problems = problems, Seen = seen, Tick = Step, Where = "point weld online client" };
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
        if (!WaitFor(() => a.SandboxHands.Hand(a.LocalId).Tool == ConstructTool.Welder && server.SandboxHands.Hand(a.LocalId).Tool == ConstructTool.Welder, 3))
        {
            problems.Add("online: the client's welder never came out");
            return;
        }
        Program.SweepRelease(a);
        Program.SweepWeldModes.Mode = SandboxWeldModes.Option.Socket;
        Program.SweepPointHeld = true;
        var me = a.Local!;
        var cp = a.Constructs.Get(panel.Id)!;
        var ct = a.Constructs.Get(target.Id)!;
        void StandBoth(Vector3 p, Vector3 n, float d)
        {
            PwStand(a, me, p, n, d);
            sp.Teleport(me.Position);
            WaitFor(() => Vector3.Distance(server.Players[a.LocalId].Position, me.Position) < 0.05f, 0.5);
            PwLook(a, me, p);
        }
        var (spt, snt) = PwWorld(cp, 0, new Vector3(0.12f, 0.05f, 0.07f), Vector3.UnitY);
        StandBoth(spt, snt, 1.4f);
        void Diag(string w0)
        {
            var eye = me.RenderEye; var dir = a.LookDir(a.LookYaw, a.LookPitch);
            var hitOk = a.Constructs.Raycast(eye, dir, 6f, out var hh, look: true);
            var ws = World.RaycastWorld(eye, dir, 6f);
            Line($"DIAG {w0}: flow {Program.PointWeldTestFlow.Step} src {Program.PointWeldTestFlow.Source} sock {Program.SwDiag} tool {a.SandboxHands.Hand(a.LocalId).Tool} held {a.SandboxHands.Hand(a.LocalId).Held} seated {a.SandboxHands.Hand(a.LocalId).Seated} mode {Program.SweepWeldModes.Mode} pointheld {Program.SweepPointHeld} eye {eye} pos {me.Position} srv {sp.Position} alive {me.Alive} hit {hitOk} d {hh.Distance:0.00} body {hh.Body?.Id} part {hh.Part} slot {hh.SlotPlus1} route {hh.RoutePlus1} wall {ws:0.00} panel {cp.Pos} look '{a.SandboxLookText}' notice '{a.SandboxNotice}' phase {a.Phase}");
        }
        Diag("pre");
        run.Frame([]);
        Diag("f0");
        run.Frame(PwFire);
        Diag("f1");
        run.Frame([]);
        Diag("f2");
        var (tpt, tnt) = PwWorld(ct, 0, new Vector3(-0.25f, -0.06f, 0.08f), -Vector3.UnitX);
        StandBoth(tpt, tnt, 1.5f);
        Diag("t0");
        run.Frame([]);
        Diag("t1");
        run.Frame(PwFire);
        Diag("t2");
        run.Frame([]);
        Diag("t3");
        run.Frame([], scroll: 1);
        run.Frame([], scroll: 1);
        run.Frame([], scroll: 1);
        var flow = Program.PointWeldTestFlow;
        var steps = $"{flow.Step} at {flow.Angle:0} deg ({Program.PointWeldTestWhy ?? "green"})";
        SocketWeld.Pose(cp, flow.Src, ct, flow.Target, 45, out _, out _, out var wantPos, out var wantRot);
        run.Frame(PwFire);
        run.Frame([]);
        var done = WaitFor(() => w.Get(panel.Id) == null && PartNamed(target, "Panel") >= 0 && a.Constructs.Get(panel.Id) == null
                                 && a.Constructs.Get(target.Id) is { } t2 && PartNamed(t2, "Panel") >= 0, 4);
        Pump(server, [a], 0.4, ref now, clock);
        var hp = PartNamed(target, "Panel");
        var cc = a.Constructs.Get(target.Id);
        var cpi = cc == null ? -1 : PartNamed(cc, "Panel");
        var (hPos, hRot) = hp >= 0 ? FwWorld(target, hp) : default;
        var (cPos, cRot) = cpi >= 0 ? FwWorld(cc!, cpi) : default;
        var (hm, ht) = FwDiff((wantPos, wantRot), (hPos, hRot));
        var (cm, ctn) = FwDiff((hPos, hRot), (cPos, cRot));
        Line($"point weld online: the client's steps: {steps}; welded on the host {done}: {hm * 100:0.00} cm / {ht:0.00} deg off the client's ghost; the client's copy {cm * 100:0.00} cm / {ctn:0.00} deg off the host's ({target.FreeformCount} freeform welds on the host, {cc?.FreeformCount ?? -1} on the client)");
        if (!done || hm > 0.01f || ht > 1f || cm > 0.01f || ctn > 1f || cc == null || cc.FreeformCount != target.FreeformCount)
            problems.Add($"online: the client's point weld didn't land where its ghost was on both peers ({steps}; done {done}, host {hm * 100:0.00} cm / {ht:0.00} deg, client {cm * 100:0.00} cm / {ctn:0.00} deg)");
        Program.SweepRelease(a);
        while (w.Bodies.Count > 0)
            w.Remove(w.Bodies[^1]);
        Pump(server, [a], 0.3, ref now, clock);
    }
}
