using System.Diagnostics;
using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Nudge tool" (docs/sandbox/CONSTRUCTS.md, "The Nudge tool"): the frame (Z down along gravity); through the real input a body
    /// taken, its X arrow dragged to a 0.1 m step, the mode's arc to 45 degrees (a 15 degree snap) about the pivot and free; R
    /// to local; RMB and Esc put a drag back; a move into a wall stops at it, one out of an overlap isn't refused; a coupled or
    /// selected partner doesn't block; frozen and anchored stay so, a loose one falls on letting go; survival's refusals;
    /// a real host and client with lag and loss: the holder's and a watcher's drawn poses stay smooth (second difference) and land exact.
    /// </summary>
    private static void CheckNudge()
    {
        Nudge.Axes(new Vector3(0, -9.8f, 0), false, Quaternion.Identity, out var x, out var y, out var z);
        NdWant(z == -Vector3.UnitY && MathF.Abs(Vector3.Dot(Vector3.Cross(x, y), z) - 1) < 1e-5f && x == Vector3.UnitX, $"world Z runs down along gravity, right-handed ({x}, {y}, {z})");
        Nudge.Axes(new Vector3(9.8f, 0, 0), false, Quaternion.Identity, out x, out y, out z);
        NdWant(z == Vector3.UnitX && MathF.Abs(Vector3.Dot(Vector3.Cross(x, y), z) - 1) < 1e-5f, $"sideways gravity turns the triad ({x}, {y}, {z})");
        var turned = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f);
        Nudge.Axes(default, true, turned, out x, out _, out z);
        NdWant(Vector3.Distance(x, Vector3.Transform(Vector3.UnitX, turned)) < 1e-5f && Vector3.Distance(z, -Vector3.UnitY) < 1e-5f, "the local triad is the body's");
        NdWant(MathF.Abs(Nudge.Snap(0.27f, 0.1f) - 0.3f) < 1e-5f && Nudge.Snap(0.27f, 0) == 0.27f, "the step snaps a value, 0 leaves it free");
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail($"nudge: the {ConstructYardMap} map is missing");
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
        var problems = new List<string>();
        var debris = sb.DebrisSeconds;
        try
        {
            NdServer(yard, sb);
            NdOnline(yard, sb, problems);
        }
        finally
        {
            Program.SandboxGuardRethrow = false;
            Game.SandboxActRethrow = false;
            sb.DebrisSeconds = debris;
            yard.RestoreProps();
            ImGui.DestroyContext(ctx);
        }
        foreach (var p in problems.Take(20))
            Fail($"nudge: {p}");
    }

    private static void NdServer(Map yard, SandboxMap sb)
    {
        yard.RestoreProps();
        World.SetMap(yard);
        sb.DebrisSeconds = 0;
        var w = new ConstructWorld();
        var e = new ConstructEdits { Query = World.Query, InfiniteParts = true };
        var q = World.Query;
        if (OpenSpot(new ConstructWorld()) is not { } spot)
        {
            Fail("nudge: no open level ground");
            return;
        }
        const byte me = 0;
        e.SetTool(me, ConstructTool.Nudge);
        var eye = spot + new Vector3(0, 3, 6);
        var now = 0.0;
        void Run(float s)
        {
            for (var t = 0; t < s * World.TickRate; t++)
            {
                now += World.TickDt;
                e.Tick(w, now, _ => eye);
                ConstructPhysics.Step(w, q, (float)World.TickDt);
            }
        }
        var block = FwLoose(w, "Frame Block", spot + new Vector3(0, 2, 0), Quaternion.Identity, frozen: true)!;
        var at0 = block.Pos;
        var why = e.NudgeBegin(w, me, eye, [block.Id], block.WorldCom);
        e.NudgeEnd(me, new Vector3(0.3f, 0, 0), Quaternion.Identity);
        Run(0.5f);
        NdWant(why == null && block.Pos == at0 + new Vector3(0.3f, 0, 0) && block.Frozen && block.NudgeBy == 255, $"a frozen block lands exactly on the step and stays frozen (\"{why}\", {block.Pos - at0})");
        // About a pivot, off to one side.
        var pivot = block.Pos + new Vector3(1, 0, 0);
        why = e.NudgeBegin(w, me, eye, [block.Id], pivot);
        var quarter = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        var p0 = block.Pos;
        e.NudgeEnd(me, Vector3.Zero, quarter);
        Run(0.5f);
        var wantAt = pivot + Vector3.Transform(p0 - pivot, quarter);
        NdWant(why == null && Vector3.Distance(block.Pos, wantAt) < 1e-3f && MathF.Abs(MathF.Abs(Quaternion.Dot(block.Rot, quarter)) - 1) < 1e-4f, $"a turn goes about the pivot ({block.Pos} for {wantAt})");
        // A wall stops it; out of it isn't refused.
        var wall = FwLoose(w, "Frame Block", block.Pos + new Vector3(1.0f, 0, 0), Quaternion.Identity, frozen: true)!;
        var start = block.Pos;
        e.NudgeBegin(w, me, eye, [block.Id], block.WorldCom);
        e.NudgeEnd(me, new Vector3(1.5f, 0, 0), Quaternion.Identity);
        Run(1.5f);
        var stopped = block.Pos.X - start.X;
        var hitNotice = e.Notices.Exists(n => n.Item2.Contains("in the way"));
        NdWant(stopped is > 0.2f and < 1.0f && hitNotice && block.NudgeBy == 255, $"a wall stops it short with the reason (went {stopped:0.00} m)");
        e.Notices.Clear();
        var inside = FwLoose(w, "Frame Block", wall.Pos + new Vector3(0.1f, 0, 0), Quaternion.Identity, frozen: true)!; // (overlapping the wall already)
        var i0 = inside.Pos;
        e.NudgeBegin(w, me, eye, [inside.Id], inside.WorldCom);
        e.NudgeEnd(me, new Vector3(3, 0, 0), Quaternion.Identity);
        Run(0.8f);
        NdWant(Vector3.Distance(inside.Pos, i0 + new Vector3(3, 0, 0)) < 1e-3f, $"a move out of an overlap isn't refused (went {inside.Pos.X - i0.X:0.00} m)");
        // Two together: each is in the other's way, and neither blocks.
        var a = FwLoose(w, "Frame Block", spot + new Vector3(3, 2, 5), Quaternion.Identity, frozen: true)!;
        var b = FwLoose(w, "Frame Block", a.Pos + new Vector3(0.6f, 0, 0), Quaternion.Identity, frozen: true)!;
        var (ba, bb) = (a.Pos, b.Pos);
        why = e.NudgeBegin(w, me, eye, [a.Id, b.Id], (a.WorldCom + b.WorldCom) * 0.5f);
        e.NudgeEnd(me, new Vector3(1, 0, 0), Quaternion.Identity);
        Run(0.6f);
        NdWant(why == null && Vector3.Distance(a.Pos, ba + Vector3.UnitX) < 1e-3f && Vector3.Distance(b.Pos, bb + Vector3.UnitX) < 1e-3f, $"a selection's bodies move as one (\"{why}\")");
        // A loose one goes back to the physics; an anchored one stays.
        var loose = FwLoose(w, "Frame Block", spot + new Vector3(4, 3, -3), Quaternion.Identity)!;
        why = e.NudgeBegin(w, me, eye, [loose.Id], loose.WorldCom);
        Run(0.3f);
        var heldAt = loose.Pos;
        var stayed = !loose.Asleep && loose.NudgeBy == me;
        Run(0.3f);
        stayed &= loose.Pos == heldAt;
        e.NudgeEnd(me, Vector3.Zero, Quaternion.Identity);
        Run(0.5f);
        NdWant(why == null && stayed && loose.Pos.Y < heldAt.Y - 0.1f, $"a loose body waits in a nudge and falls after it (\"{why}\", {heldAt.Y:0.00} to {loose.Pos.Y:0.00})");
        var pinned = FwLoose(w, "Frame Block", spot + new Vector3(-4, 1, -3), Quaternion.Identity)!;
        pinned.Pinned = true;
        pinned.Rebuild();
        why = e.NudgeBegin(w, me, eye, [pinned.Id], pinned.WorldCom);
        var pin0 = pinned.Pos;
        e.NudgeEnd(me, new Vector3(0, 0.5f, 0), Quaternion.Identity);
        Run(0.5f);
        NdWant(why == null && Vector3.Distance(pinned.Pos, pin0 + new Vector3(0, 0.5f, 0)) < 1e-3f && pinned.Anchored, $"creative: a pinned body moves and stays pinned (\"{why}\")");
        // The rules.
        e.InfiniteParts = false;
        e.Survival = true;
        NdWant(e.NudgeRefusal(pinned, me) is { } anchored && anchored.Contains("anchored"), "survival: an anchored body stays");
        var far = e.NudgeBegin(w, me, eye + new Vector3(0, 0, 80), [block.Id], block.WorldCom);
        NdWant(far != null && far.Contains("too far"), $"survival: out of range (\"{far}\")");
        e.InfiniteParts = true;
        e.Survival = false;
        var gun = FwLoose(w, "Frame Block", spot + new Vector3(6, 2, 3), Quaternion.Identity, frozen: true)!;
        e.NudgeBegin(w, me, eye, [gun.Id], gun.WorldCom);
        NdWant(e.GrabRefusal(gun, 1) != null && e.NudgeBegin(w, 1, eye, [gun.Id], gun.WorldCom) != null, "a nudged body is held: no other player, no physics gun");
        e.SetTool(me, ConstructTool.Welder);
        Run(0.1f);
        NdWant(gun.NudgeBy == 255, "the tool put away lets go");
        w.Remove(gun);
    }

    private static void NdOnline(Map yard, SandboxMap sb, List<string> problems)
    {
        const ushort port = 26521;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        sb.DebrisSeconds = 0;
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        using var v = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Watcher", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, v], 1.5, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            Pump(server, [a, v], 0.25, ref now, clock);
        Pump(server, [a, v], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp) || a.Local == null)
        {
            problems.Add("online: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        Pump(server, [a, v], 0.5, ref now, clock);
        sp.ImmuneUntil = 1e9;
        server.Net.SimulateBadNetwork(40, 5, 2);
        a.Net.SimulateBadNetwork(40, 5, 2);
        v.Net.SimulateBadNetwork(40, 5, 2);
        var w = server.Constructs;
        if (OpenSpot(w) is not { } spot)
        {
            problems.Add("online: no open level ground");
            return;
        }
        var block = FwLoose(w, "Frame Block", spot + new Vector3(0, 3f, 0), Quaternion.Identity, frozen: true)!;
        var start = block.Pos;
        var frameNow = now;
        void Step()
        {
            var t = clock.Elapsed.TotalSeconds;
            var dt = Math.Clamp(t - frameNow, 0.001, 0.1);
            frameNow = t;
            server.Update(t, dt);
            a.Update(t, dt);
            v.Update(t, dt);
        }
        bool WaitFor(Func<bool> cond, double seconds)
        {
            var until = clock.Elapsed.TotalSeconds + seconds;
            while (clock.Elapsed.TotalSeconds < until && !cond())
                Pump(server, [a, v], 0.05, ref now, clock);
            return cond();
        }
        if (!WaitFor(() => a.Constructs.Get(block.Id) != null && v.Constructs.Get(block.Id) != null, 3))
        {
            problems.Add("online: the clients never got the block");
            return;
        }
        var run = new SweepRunner { G = a, Problems = problems, Seen = new HashSet<string>(), Tick = Step, Where = "nudge online" };
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Nudge);
        if (!WaitFor(() => a.SandboxHands.Hand(a.LocalId).Tool == ConstructTool.Nudge && server.SandboxHands.Hand(a.LocalId).Tool == ConstructTool.Nudge, 3))
        {
            problems.Add("online: the Nudge tool never came out");
            return;
        }
        Program.SweepRelease(a);
        Program.SweepNudgeModes.Mode = SandboxNudgeModes.Option.Move;
        Program.SweepNudgeModes.Local = false;
        Program.SweepNudgeModes.Step = 0.1f;
        var me = a.Local!;
        me.Teleport(ConstructWorld.Ground(World.Query, spot.X - 4f, spot.Z) + new Vector3(0, 0.05f, 0));
        sp.Teleport(me.Position);
        WaitFor(() => Vector3.Distance(server.Players[a.LocalId].Position, me.Position) < 0.05f, 0.5);
        var cb = a.Constructs.Get(block.Id)!;
        var wb = v.Constructs.Get(block.Id)!;
        // Take it, then aim down the triad's Y arrow, which lies across the view, and drag along it on screen.
        PwLook(a, me, cb.WorldCom);
        run.Frame([]);
        run.Frame(PwFire);
        run.Frame([]);
        var size = Vector3.Distance(me.RenderEye, cb.WorldCom) * 0.16f;
        var axes = Program.NudgeTestAxes;
        var yAxis = axes[1];
        PwLook(a, me, cb.WorldCom + yAxis * size * 0.7f);
        run.Frame([]);
        run.Frame(PwFire);
        var dragging = Program.NudgeTestDrag == 1;
        var held = new List<Vector3>();
        var seen = new List<Vector3>();
        var sign = 0f;
        for (var f = 0; f < 90; f++)
        {
            run.Frame(PwFire, mouse: new double2(f < 60 ? 6 : 0, 0));
            held.Add(cb.Pos);
            seen.Add(wb.Pos);
            Thread.Sleep(10);
        }
        run.Frame([]);
        var landed = WaitFor(() => Vector3.Distance(block.Pos, start) > 0.05f && block.NudgeBy == 255, 3);
        sign = MathF.Sign(Vector3.Dot(block.Pos - start, yAxis));
        var moved = Vector3.Dot(block.Pos - start, yAxis);
        var onStep = MathF.Abs(moved / 0.1f - MathF.Round(moved / 0.1f)) < 0.01f && Vector3.Distance(block.Pos - start, yAxis * moved) < 1e-3f;
        Pump(server, [a, v], 0.6, ref now, clock);
        var agree = Vector3.Distance(cb.Pos, block.Pos) < 0.01f && Vector3.Distance(wb.Pos, block.Pos) < 0.01f;
        var (hs, hb) = NdRough(held);
        var (ws, wbk) = NdRough(seen);
        Line($"nudge online: dragging {dragging}, moved {moved:0.000} m along Y (sign {sign}), on a step {onStep}, landed {landed}, peers agree {agree}; holder: worst second difference {hs * 1000:0.0} mm, {hb} back steps; watcher {ws * 1000:0.0} mm, {wbk} back steps");
        if (!dragging || !landed || !onStep || !agree)
            problems.Add($"online: the drag didn't land on a step on all peers (dragging {dragging}, {moved:0.000} m, step {onStep}, landed {landed}, agree {agree})");
        if (hs > 0.08f || hb > 2 || ws > 0.12f || wbk > 4)
            problems.Add($"online: the drag isn't smooth: holder {hs * 1000:0.0} mm, {hb} back; watcher {ws * 1000:0.0} mm, {wbk} back");
        // Back to the rest: Esc and RMB put a drag back.
        var was = block.Pos;
        PwLook(a, me, cb.WorldCom + yAxis * size * 0.7f);
        run.Frame([]);
        run.Frame(PwFire);
        run.Frame(PwFire, mouse: new double2(40, 0));
        run.Frame(PwFire, mouse: new double2(40, 0));
        run.Frame(PwAimKey);
        run.Frame([]);
        var back = WaitFor(() => block.NudgeBy == 255 && Vector3.Distance(block.Pos, was) < 1e-3f, 3);
        if (!back || Program.NudgeTestDrag != -1)
            problems.Add($"online: RMB didn't put the drag back ({Vector3.Distance(block.Pos, was):0.000} m off, drag {Program.NudgeTestDrag})");
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
        run.Frame([]);
        Program.NudgeTestForget();
    }

    private static void NdWant(bool ok, string what)
    {
        Line($"nudge: {what}: {(ok ? "ok" : "NO")}");
        if (!ok)
            Fail($"nudge: {what}");
    }

    /// <summary>A drawn path's worst second difference between frames, and how many frames stepped back along its way.</summary>
    private static (float Worst, int Back) NdRough(List<Vector3> path)
    {
        var worst = 0f;
        var back = 0;
        var way = path[^1] - path[0];
        if (way.LengthSquared() > 1e-6f)
            way = Vector3.Normalize(way);
        for (var i = 2; i < path.Count; i++)
        {
            worst = MathF.Max(worst, (path[i] - path[i - 1] * 2 + path[i - 2]).Length());
            if (Vector3.Dot(path[i] - path[i - 1], way) < -0.002f)
                back++;
        }
        return (worst, back);
    }
}
