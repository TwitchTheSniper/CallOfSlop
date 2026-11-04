using System.Diagnostics;
using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
namespace CallOfSlop;
public static partial class Program
{
    internal static SocketWeldFlow SwT2 => _swFlow;
}
public static partial class SelfTest
{
    private static void CheckAttachTmp()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb) return;
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
        try { TmpA(yard, sb, problems); }
        finally { Program.SandboxGuardRethrow = false; Game.SandboxActRethrow = false; Program.SweepWeldModes.Mode = SandboxWeldModes.Option.Socket; yard.RestoreProps(); ImGui.DestroyContext(ctx); }
        foreach (var p in problems) Fail("tmp: " + p);
    }
    private static readonly GameAction[] TShift = [GameAction.Sprint], TShiftFire = [GameAction.Sprint, GameAction.Fire], T3 = [GameAction.Weapon3];
    private static void TmpA(Map yard, SandboxMap sb, List<string> problems)
    {
        yard.RestoreProps(); World.SetMap(yard); sb.DebrisSeconds = 0;
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        if (SpawnLocalSafely(g, ref now) is { } why) { problems.Add(why); return; }
        var me = g.Local!; me.ImmuneUntil = 1e9;
        var w = g.Constructs;
        var spot = OpenSpot(w)!.Value;
        var run = new SweepRunner { G = g, Problems = problems, Seen = new HashSet<string>(), Now = now, Where = "tmp" };
        run.Tick = () => { run.Now += 1.0 / World.TickRate; g.Update(run.Now, 1.0 / World.TickRate); };
        void Settle() { for (var k = 0; k < 20; k++) run.Tick(); }
        // Ring: straight out is Socket; each option reachable.
        var wheel = new SandboxToolWheel();
        var modes = new SandboxWeldModes();
        wheel.SetOptions(SandboxToolWheel.Slot.Welder, modes);
        var m = wheel.Menu; const int W = (int)SandboxToolWheel.Slot.Welder; const int N = (int)SandboxToolWheel.Slot.COUNT;
        Vector2 At(float a, float r) => new(MathF.Sin(a) * r, -MathF.Cos(a) * r);
        for (var o = -1; o < 4; o++)
        {
            wheel.Step(g, true, Vector2.Zero, false);
            var c = RadialMenu.SegmentAngle(W, N);
            wheel.Step(g, true, At(c, 45) - m.Pointer, false);
            wheel.Step(g, true, At(c, m.RowDistance(0)) - m.Pointer, false);
            var straight = m.HoveredOption;
            if (o >= 0)
                wheel.Step(g, true, At(RadialMenu.OptionAngle(o, W, N, 4) + modes.OptionShift, m.RowDistance(0)) - m.Pointer, false);
            var hov = m.HoveredOption;
            var label = hov >= 0 ? modes.Options[hov].Label : "-";
            var sl = straight >= 0 ? modes.Options[straight].Label : "-";
            wheel.Step(g, false, Vector2.Zero, false);
            for (var k = 0; k < 3; k++) run.Tick();
            Line($"tmp ring: option {o}: straight out {sl}, hovered {label}, mode {modes.Mode}, hands {m.Items[(int)SandboxToolWheel.Slot.Hands].Label} at {RadialMenu.SegmentAngle((int)SandboxToolWheel.Slot.Hands, N) * 180 / MathF.PI:0} deg");
            if (straight != (int)SandboxWeldModes.Option.Socket) problems.Add("ring: straight out isn't Socket");
        }
        Program.SweepRelease(g);
        // key 3, wire byte
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None); for (var k = 0; k < 3; k++) run.Tick();
        run.Frame([]); run.Frame(T3); run.Frame([]); for (var k = 0; k < 3; k++) run.Tick();
        var tool3 = g.SandboxHands.Hand(me.Id).Tool;
        Line($"tmp key 3: tool {tool3}; From(1) {ConstructToolWire.From(1)}, Current(Wrench) {ConstructToolWire.Current(ConstructTool.Wrench)}");
        if (tool3 != ConstructTool.Welder) problems.Add("key 3");
        // old byte through the act
        g.SandboxSend(CActKind.Tool, part: 1); for (var k = 0; k < 3; k++) run.Tick();
        Line($"tmp byte 1 sent: hand {g.SandboxHands.Hand(me.Id).Tool}");
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder); for (var k = 0; k < 3; k++) run.Tick();
        Program.SweepRelease(g);
        Program.SweepWeldModes.Mode = SandboxWeldModes.Option.Socket;
        run.Frame([]);
        var block = ConstructParts.Need(SwFrame);
        var target = w.Create(spot + new Vector3(0, 0.9f, 0), Quaternion.Identity)!;
        w.AddPart(target, block, Vector3.Zero, 0);
        target.Pinned = true; target.Rebuild();
        var pf = Program.PointWeldTestFlow; var sf = Program.SwT2;
        Construct? P(Vector3 at) => FwLoose(w, "Panel", at, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f), frozen: true);
        // Shift: point attach
        var p1 = P(spot + new Vector3(-0.6f, 1.0f, 2.4f))!;
        var (sp, sn) = PwWorld(p1, 0, new Vector3(0.12f, 0.05f, 0.07f), Vector3.UnitY);
        PwStand(g, me, sp, sn, 1.4f, settle: Settle);
        run.Frame([]);
        Line($"tmp shift: no shift look \"{g.SandboxLookText}\" hint \"{g.SandboxWeldHint}\"");
        run.Frame(TShift);
        Line($"tmp shift: held look \"{g.SandboxLookText}\" hint \"{g.SandboxWeldHint}\"");
        run.Frame(TShiftFire); run.Frame([]);
        Line($"tmp shift: after click point {pf.Step} socket {sf.Step}, released: hint \"{g.SandboxWeldHint}\"");
        var (tp, tn) = PwWorld(target, 0, new Vector3(-0.25f, -0.06f, 0.08f), -Vector3.UnitX);
        PwStand(g, me, tp, tn, 1.5f, settle: Settle); run.Frame([]); run.Frame(PwFire); run.Frame([]);
        run.Frame(TShift, scroll: 1); run.Frame([]); 
        Line($"tmp shift: step {pf.Step} {pf.Angle} look \"{g.SandboxLookText}\"");
        var n0 = target.PartCount;
        run.Frame(PwFire); for (var k = 0; k < 4; k++) run.Frame([]);
        Line($"tmp shift: point attached parts {n0}->{target.PartCount}");
        if (target.PartCount != n0 + 1) problems.Add("point via shift");
        // Socket flow: Shift held past step 1 stays socket
        var p2 = P(spot + new Vector3(-0.6f, 1.0f, 3.4f))!;
        Settle();
        var at = SockW2(p2, "+x");
        PwStand(g, me, at, Vector3.UnitX, 1.6f, settle: Settle); PwLook(g, me, at); run.Frame([]); run.Frame(PwFire); run.Frame([]);
        Line($"tmp socket: {sf.Step} {pf.Step}");
        run.Frame(TShift); run.Frame(TShift);
        Line($"tmp socket: shift held, step2 still socket {sf.Step} point {pf.Step} hint \"{g.SandboxWeldHint}\"");
        run.Frame(PwAimKey); run.Frame([]);
        w.Remove(p2); Settle();
        // Freeform 3 m
        Program.SweepWeldModes.Mode = SandboxWeldModes.Option.Freeform; run.Frame([]);
        var a = P(spot + new Vector3(-6f, 0.8f, 0))!; var b = P(spot + new Vector3(-6f, 0.8f, 3.3f))!; var c2 = P(spot + new Vector3(-6f, 0.8f, 7.5f))!;
        a.Pinned = true; Settle();
        me.Teleport(ConstructWorld.Ground(World.Query, spot.X - 8f, spot.Z + 1.6f) + new Vector3(0, 0.05f, 0)); Settle();
        PwLook(g, me, a.Pos); run.Frame([]);
        Line($"tmp free: look \"{g.SandboxLookText}\" hint \"{g.SandboxWeldHint}\"");
        run.Frame(PwFire); run.Frame([]);
        PwLook(g, me, b.Pos); run.Frame([]);
        Line($"tmp free: second look \"{g.SandboxLookText}\" valid {ConstructRender.WeldValid}");
        run.Frame(PwFire); for (var k = 0; k < 4; k++) run.Frame([]);
        Line($"tmp free: joined freeform a {a.FreeformCount}, b gone {w.Get(b.Id) == null}, parts {a.PartCount}");
        if (a.FreeformCount != 1) problems.Add("freeform 3 m");
        // Refused beyond: gap
        var gap = Freeform.Gap(a, a.PartCount - 1, c2, 0, out _, out _);
        var pre = a.FreeformCount;
        PwLook(g, me, a.ToWorld(a.Parts[a.PartCount-1].Pos)); run.Frame([]); run.Frame(PwFire); run.Frame([]);
        me.Teleport(ConstructWorld.Ground(World.Query, spot.X - 8f, spot.Z + 5f) + new Vector3(0, 0.05f, 0)); Settle();
        PwLook(g, me, c2.Pos); run.Frame([]);
        Line($"tmp free: gap {gap:0.00}: look \"{g.SandboxLookText}\" valid {ConstructRender.WeldValid}");
        run.Frame(PwFire); run.Frame([]); run.Frame(PwAimKey); run.Frame([]);
        Line($"tmp free: refused count {a.FreeformCount}=={pre}, notice \"{g.SandboxNotice}\"");
        // Cut the 3 m seam: RMB on the far part near its bead end
        me.Teleport(ConstructWorld.Ground(World.Query, spot.X - 8f, spot.Z + 1.6f) + new Vector3(0, 0.05f, 0)); Settle();
        Freeform.Gap(a, 0, a, a.PartCount - 1, out var e1, out var e2); e1 += Vector3.Normalize(a.Pos - e1) * 0.3f;
        for (var k = 0; k < 4; k++) { PwLook(g, me, e1); run.Frame([]); }
        var ey = me.RenderEye; var dr = Vector3.Normalize(e1 - ey);
        var hitOk = w.Raycast(ey, dr, 6f, out var hh, look: true);
        Line($"tmp free: seam look \"{g.SandboxLookText}\" eye {ey} e1 {e1} e2 {e2} a {a.Pos} hit {hitOk} dist {hh.Distance:0.00} part {hh.Part} body {hh.Body?.Id}=={a.Id} seam {(hitOk ? Freeform.SeamAt(hh.Body!, hh.Part, hh.Point) : -9)} tool {g.SandboxHands.Hand(me.Id).Tool} mode {Program.SweepWeldModes.Mode}");
        PwLook(g, me, e1); run.Frame(PwAimKey); run.Frame([]); for (var k = 0; k < 4; k++) run.Frame([]);
        Line($"tmp free: after cut freeform {a.FreeformCount}");
        if (a.FreeformCount != 0) problems.Add("seam cut at 3 m");
    }
    private static Vector3 SockW2(Construct c, string name)
    {
        c.SocketAt(0, SwSocket(c.DefOf(0), name), out var at, out _);
        return c.ToWorld(at);
    }
}
