using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>What a chain was on the host at each tick (ring by tick).</summary>
    private sealed class ChainLog
    {
        public const int Size = 2048;
        public readonly Vector3[] Tip = new Vector3[Size], Shape = new Vector3[Size];
        public readonly uint[] At = new uint[Size];

        public void Add(uint tick, Vector3 tip, Vector3 shape)
        {
            var k = tick % Size;
            At[k] = tick;
            Tip[k] = tip;
            Shape[k] = shape;
        }

        public bool Get(double tick, out Vector3 tip, out Vector3 shape)
        {
            var t0 = (uint)Math.Floor(tick);
            var (a, b) = (t0 % Size, (t0 + 1) % Size);
            tip = shape = default;
            if (At[a] != t0 || At[b] != t0 + 1)
                return false;
            var f = (float)(tick - t0);
            tip = Vector3.Lerp(Tip[a], Tip[b], f);
            shape = Vector3.Lerp(Shape[a], Shape[b], f);
            return true;
        }
    }

    /// <summary>
    /// Board report (the user's video): a chain of joints looked wiggly in multiplayer, the links out of step with the bodies. A host, a
    /// client and a third peer at 50 ms each way with jitter and 2% loss: free pivots swinging, a driven hinge, piston and turntable,
    /// a loose chain thrown and one held with the gun. Each peer's chain is compared with the host's by server tick: the tip, and
    /// its shape in the body's frame, so the leaf must be right, not only the first link; and the tip is checked for smoothness.
    /// </summary>
    private static void CheckJointChainNet()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail("joint chain: no yard");
            return;
        }
        const ushort port = 26499;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Game[] clients = [a, b];
        Pump(server, clients, 1.5, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            Pump(server, clients, 0.25, ref now, clock);
        Pump(server, clients, 0.5, ref now, clock);
        var pa = server.Players[a.LocalId];
        pa.Alive = true;
        var w = server.Constructs;
        foreach (var c in w.Bodies.ToList())
            w.Remove(c);
        var feet = ConstructWorld.Ground(World.Query, 0, 0);
        pa.Teleport(feet);
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.PhysicsGun);
        Pump(server, clients, 0.5, ref now, clock);
        server.Net.SimulateBadNetwork(50, 10, 2);
        a.Net.SimulateBadNetwork(50, 10, 2);
        b.Net.SimulateBadNetwork(50, 10, 2);

        Construct Base(Vector3 at, bool anchored)
        {
            var c = ConstructBuild.PlaceNew(w, ConstructParts.Need("Frame Block"), at, 0)!;
            c.Frozen = c.Anchored = anchored;
            return c;
        }
        int Link(Construct c, int on, string socket, string joint, string by, string off)
        {
            var d = ConstructParts.Need(joint);
            var j = FitTurned(w, c, on, socket, joint, by, r => MathF.Abs(ConstructParts.DirVector(Rot24.ApplyDir(r, d.JointAxis)).Y) < 0.1f);
            return j < 0 ? -1 : FitTurned(w, c, j, off, "Frame Beam", "end-", _ => true);
        }
        Construct? Free(Vector3 at, bool anchored, out int tip)
        {
            var c = Base(at, anchored);
            tip = Link(c, 0, "+x", "Free Pivot 2x2", "base", "top");
            for (var k = 0; k < 2 && tip >= 0; k++)
                tip = Link(c, tip, "end+", "Free Pivot 2x2", "base", "top");
            c.Rebuild();
            return tip < 0 ? null : c;
        }
        Construct? Driven(Vector3 at, out int tip)
        {
            var c = Base(at, true);
            tip = Link(c, 0, "+x", "Motor Hinge", "base", "leaf");
            if (tip >= 0)
                tip = FitTurned(w, c, tip, "end+", "Piston", "base", _ => true) is var p and >= 0 ? FitTurned(w, c, p, "top", "Frame Beam", "end-", _ => true) : -1;
            if (tip >= 0)
                tip = FitTurned(w, c, tip, "end+", "Turntable", "base", _ => true) is var t and >= 0 ? FitTurned(w, c, t, "top", "Frame Beam", "end-", _ => true) : -1;
            c.Rebuild();
            return tip < 0 ? null : c;
        }
        var problems = new List<string>();
        void Run(string name, Construct c, int tip, double seconds, Action<double>? each, bool holder = false, float tipLimit = 0.1f)
        {
            var hitches = 0;
            var lastFrame = clock.Elapsed.TotalSeconds;
            var settled = 0.0;
            var log = new ChainLog();
            var host = new SmoothTrack();
            var ta = new SmoothTrack();
            var tb = new SmoothTrack();
            var errTip = new List<float>[] { new(), new() };
            var errShape = new List<float>[] { new(), new() };
            var start = clock.Elapsed.TotalSeconds;
            var next = start;
            var hostTick = server.ServerTick;
            while (clock.Elapsed.TotalSeconds - start < seconds)
            {
                next = Math.Max(next + 0.01, clock.Elapsed.TotalSeconds + 0.002);
                while (clock.Elapsed.TotalSeconds < next)
                    Thread.Sleep(1);
                var t = clock.Elapsed.TotalSeconds;
                var dt = Math.Clamp(t - now, 0.001, 0.1);
                now = t;
                // A stall of this whole process (all three peers, a loaded runner) is not what's measured: the poses come in a burst and
                // the curve's change is eased, 30 to 60 cm on a piece swung at 12 m/s, gone in a third of a second.
                if (t - lastFrame > 0.04)
                {
                    hitches++;
                    settled = t + 0.5;
                }
                lastFrame = t;
                each?.Invoke(t - start);
                server.Update(now, dt);
                a.Update(now, dt);
                b.Update(now, dt);
                if (server.ServerTick != hostTick)
                {
                    var p = ConstructRender.PartCentre(c, tip);
                    var shapeNow = c.ToLocal(p);
                    if (server.ServerTick == hostTick + 2 && log.Get(hostTick - 1 + 0.0, out var was, out var wasShape))
                        log.Add(hostTick + 1, (was + p) * 0.5f, (wasShape + shapeNow) * 0.5f); // (two ticks in one update)
                    hostTick = server.ServerTick;
                    log.Add(hostTick, p, shapeNow);
                    host.Add(hostTick * World.TickDt, p, default);
                }
                if (t - start < 1 || t < settled)
                    continue; // (the first poses in flight, or just after a stall)
                var peer = 0;
                foreach (var g in clients)
                {
                    if (g.Constructs.Get(c.Id) is { } copy && tip < copy.PartCount)
                    {
                        var p = ConstructRender.PartCentre(copy, tip);
                        (peer == 0 ? ta : tb).Add(t, p, default);
                        if (log.Get(g.Constructs.NetRenderTick, out var want, out var shape))
                        {
                            errTip[peer].Add(Vector3.Distance(p, want));
                            errShape[peer].Add(Vector3.Distance(copy.ToLocal(p), shape));
                        }
                    }
                    peer++;
                }
            }
            static string P(List<float> l, out float p98, out float max)
            {
                l.Sort();
                p98 = l.Count > 0 ? l[l.Count * 98 / 100] : 99;
                max = l.Count > 0 ? l[^1] : 99;
                return $"{p98 * 100:0.0} / {max * 100:0.0} cm";
            }
            var hostAcc = new List<float>();
            for (var k = hostTick - (uint)(seconds * 50); k + 2 <= hostTick; k++)
                if (log.Get(k, out var p0, out _) && log.Get(k + 1, out var p1, out _) && log.Get(k + 2, out var p2, out _))
                    hostAcc.Add((p2 - p1 * 2 + p0).Length() / (World.TickDt * World.TickDt));
            P(hostAcc, out var hostP98, out var hostMax);
            var (sa, sb) = (ta.Measure(false), tb.Measure(false));
            var hs = (P98: hostP98, Max: hostMax);
            Line($"joint chain: {name}{(hitches > 0 ? $" ({hitches} stalls left out)" : "")}: tip against the host by tick (98% / worst): client {P(errTip[0], out var ta98, out _)}, peer {P(errTip[1], out var tb98, out _)}; shape in the body's frame: client {P(errShape[0], out var sa98, out _)}, peer {P(errShape[1], out var sb98, out _)}; tip accel 98% / worst: host {hs.P98:0} / {hs.Max:0}, client {sa.P98:0} / {sa.Max:0} (back {sa.BackPerSec:0.0}/s), peer {sb.P98:0} / {sb.Max:0} (back {sb.BackPerSec:0.0}/s)");
            // (Bounds: a chain drawn as it arrives was 25 to 160 cm off in shape; three free pivots rang at 200 to 16,000 m/s2 on the host.)
            if (sa98 > 0.08f || sb98 > 0.08f)
                problems.Add($"{name}: the chain's shape is off by {sa98 * 100:0.0} / {sb98 * 100:0.0} cm in the body's frame");
            if (tb98 > tipLimit || !holder && ta98 > tipLimit)
                problems.Add($"{name}: the tip is {ta98 * 100:0.0} / {tb98 * 100:0.0} cm from the host's at the same tick"); // (a holder's own copy leads on purpose)
            if (hs.P98 > 1000)
                problems.Add($"{name}: the host's chain itself rings ({hs.P98:0} m/s2)");
            if (sa.P98 > MathF.Max(500, hs.P98 * 1.5f) || sb.P98 > MathF.Max(500, hs.P98 * 1.5f))
                problems.Add($"{name}: the drawn tip jitters ({sa.P98:0} / {sb.P98:0} m/s2)");
        }

        var rig = Free(feet + new Vector3(0, 7, 8), true, out var tipA);
        if (rig == null)
        {
            Fail("joint chain: the free chain doesn't build");
            return;
        }
        Pump(server, clients, 0.5, ref now, clock);
        Run("three free pivots swinging", rig, tipA, 5, null);
        w.Remove(rig);
        var driven = Driven(feet + new Vector3(0, 7, -8), out var tipB);
        if (driven == null)
        {
            Fail("joint chain: the driven chain doesn't build");
            return;
        }
        Pump(server, clients, 0.5, ref now, clock);
        Run("hinge, piston, turntable driven", driven, tipB, 5, t =>
        {
            driven.Wake();
            for (var j = 0; j < driven.JointCount; j++)
            {
                driven.Joints[j].Wired = false;
                driven.Joints[j].Open = MathF.Sin((float)t * (2 + j)) > 0;
            }
        });
        w.Remove(driven);
        var thrown = Free(feet + new Vector3(0, 30, 8), false, out var tipC);
        if (thrown == null)
        {
            Fail("joint chain: the thrown chain doesn't build");
            return;
        }
        Pump(server, clients, 0.3, ref now, clock);
        thrown.Vel = new Vector3(3, 12, 0);
        thrown.AngVel = new Vector3(0.5f, 1, 2);
        Run("thrown", thrown, tipC, 2.5, null);
        w.Remove(thrown);
        var held = Free(feet + new Vector3(4, 2, 0), false, out var tipD);
        if (held == null)
        {
            Fail("joint chain: the held chain doesn't build");
            return;
        }
        Pump(server, clients, 0.8, ref now, clock);
        var to = held.WorldCom - a.Local!.RenderEye;
        var yaw0 = MathF.Atan2(to.X, -to.Z);
        var pitch0 = MathF.Asin(Math.Clamp(to.Y / to.Length(), -1f, 1f));
        a.LookYaw = yaw0;
        a.LookPitch = pitch0;
        a.SandboxGrab(held.Id, held.Com);
        Pump(server, clients, 0.5, ref now, clock);
        if (held.GrabbedBy != a.LocalId)
            problems.Add("the chain wasn't grabbed");
        var dist = server.SandboxHands.Hand(a.LocalId).GrabDist;
        var rel = server.SandboxHands.Hand(a.LocalId).GrabRel;
        Run("held and swung with the gun", held, tipD, 4, t =>
        {
            a.LookYaw = yaw0 + 0.6f * MathF.Sin((float)t * 2.5f);
            a.LookPitch = pitch0 + 0.15f * MathF.Sin((float)t * 4f);
            a.SandboxSteer(dist, rel, now);
        }, holder: true, tipLimit: 0.2f); // (the tip goes at 12 m/s: a tick of it is 20 cm)
        a.SandboxLetGo(freeze: false);
        w.Remove(held);
        // The user's first report: springs on a client didn't act as on the host. A spring and a rope, each between two bodies, a spring
        // on a frozen contraption's free slider, and a held body pulling another by a spring: compared with the host's by tick, as above.
        void Soft(int k)
        {
            if (k < 0)
                return;
            w.Connectors[k].Stiffness = 300; // (slow enough to see: a new spring's own is stiff, and settles in a tenth of a second)
            w.Connectors[k].Damping = 12;
            w.TouchConnector(k);
        }
        ConnectorEnd End(Construct c, int part, Vector3 local) => new() { Tag = c.Parts[part].Tag, Body = c.Id, Part = (ushort)part, Local = local };
        var hook = Base(feet + new Vector3(4, 16, 8), true);
        var bob = Base(feet + new Vector3(4, 11, 8), false);
        var swing = Base(feet + new Vector3(10, 16, 8), false);
        var spring = w.AddConnector(World.Query, ConnectorKind.Spring, 1, End(hook, 0, Vector3.Zero), End(bob, 0, Vector3.Zero));
        var rope = w.AddConnector(World.Query, ConnectorKind.Rope, 1, End(hook, 0, Vector3.Zero), End(swing, 0, Vector3.Zero));
        if (spring < 0 || rope < 0)
            problems.Add("the spring or the rope wouldn't tie");
        Soft(spring);
        // (sent reliably: at 2% loss a resend takes its own time, so a bounded wait for them, not a fixed one)
        Pump(server, clients, 0.3, ref now, clock);
        for (var wait = 0; wait < 60 && (a.Constructs.ConnectorCount != 2 || b.Constructs.ConnectorCount != 2); wait++)
            Pump(server, clients, 0.05, ref now, clock);
        if (a.Constructs.ConnectorCount != 2 || b.Constructs.ConnectorCount != 2)
            problems.Add($"the clients have {a.Constructs.ConnectorCount} and {b.Constructs.ConnectorCount} connectors, not 2");
        bob.Vel = new Vector3(0, -8, 0);
        bob.Wake();
        swing.Wake();
        Run("a block bouncing on a spring", bob, 0, 3, null);
        if (bob.Asleep)
            problems.Add("the block on the spring stopped too soon to compare");
        Run("a block swinging on a rope", swing, 0, 3, null);
        w.Remove(hook);
        w.Remove(bob);
        w.Remove(swing);
        var rail = Base(feet + new Vector3(0, 7, 8), true);
        var slider = FitTurned(w, rail, 0, "+x", "Free Slider 2x2 4 m", "base", _ => true);
        var carriage = slider < 0 ? -1 : FitTurned(w, rail, slider, "top", "Frame Beam", "end-", _ => true);
        var post = Base(feet + new Vector3(5, 7, 8), true);
        if (carriage < 0)
            problems.Add("the slider doesn't build");
        else
        {
            rail.Rebuild();
            Soft(w.AddConnector(World.Query, ConnectorKind.Spring, 1, End(rail, carriage, Vector3.Zero), End(post, 0, Vector3.Zero)));
            Pump(server, clients, 0.3, ref now, clock);
            rail.Wake();
            rail.Joints[0].Vel = 3;
            Run("a free slider's carriage on a spring, frozen base", rail, carriage, 3, null);
        }
        w.Remove(rail);
        w.Remove(post);
        var lead = Base(feet + new Vector3(4, 2, 0), false);
        var towed = Base(feet + new Vector3(4, 2, 3), false);
        Soft(w.AddConnector(World.Query, ConnectorKind.Spring, 1, End(lead, 0, Vector3.Zero), End(towed, 0, Vector3.Zero)));
        Pump(server, clients, 0.8, ref now, clock);
        to = lead.WorldCom - a.Local!.RenderEye;
        yaw0 = MathF.Atan2(to.X, -to.Z);
        pitch0 = MathF.Asin(Math.Clamp(to.Y / to.Length(), -1f, 1f));
        a.LookYaw = yaw0;
        a.LookPitch = pitch0;
        a.SandboxGrab(lead.Id, lead.Com);
        Pump(server, clients, 0.5, ref now, clock);
        dist = server.SandboxHands.Hand(a.LocalId).GrabDist;
        rel = server.SandboxHands.Hand(a.LocalId).GrabRel;
        Run("a block towed by a spring from a held block", towed, 0, 4, t =>
        {
            a.LookYaw = yaw0 + 0.6f * MathF.Sin((float)t * 2.5f);
            a.LookPitch = pitch0 + 0.15f * MathF.Sin((float)t * 4f);
            a.SandboxSteer(dist, rel, now);
        });
        a.SandboxLetGo(freeze: false);
        w.Remove(lead);
        w.Remove(towed);
        // The user's report ("all further physics seems to now be desynced for the client"): a joint added to a body whose
        // poses had filled the client's history threw in every pose after it (the joint rows were shifted at the old size),
        // and each dropped packet took every body after that one with it, for good.
        var grown = Free(feet + new Vector3(0, 7, 8), true, out var tipE);
        var later = Base(feet + new Vector3(8, 60, 0), false);
        if (grown == null)
        {
            Fail("joint chain: the chain to grow doesn't build");
            return;
        }
        Pump(server, clients, 0.8, ref now, clock);
        var droppedWere = NetSafety.Dropped;
        var fellFrom = a.Constructs.Get(later.Id)?.Pos.Y ?? 0;
        if (Link(grown, tipE, "end+", "Free Pivot 2x2", "base", "top") < 0)
            problems.Add("a joint wouldn't fit on the chain");
        grown.Rebuild();
        grown.Wake();
        Pump(server, clients, 1.5, ref now, clock);
        if (NetSafety.Dropped != droppedWere)
            problems.Add($"{NetSafety.Dropped - droppedWere} packets dropped after a joint was added to a moving body");
        if (a.Constructs.Get(later.Id) is not { } laterCopy || fellFrom - laterCopy.Pos.Y < 5 || Vector3.Distance(laterCopy.Pos, later.Pos) > 12)
            problems.Add($"after a joint was added, the body behind it stopped on the client (fell {fellFrom - a.Constructs.Get(later.Id)?.Pos.Y:0.0} m, {Vector3.Distance(a.Constructs.Get(later.Id)?.Pos ?? default, later.Pos):0.0} m from the host's)");
        foreach (var p in problems)
            Fail($"joint chain: {p}");
        server.Net.SimulateBadNetwork(0, 0, 0);
        a.Net.SimulateBadNetwork(0, 0, 0);
        b.Net.SimulateBadNetwork(0, 0, 0);
    }
}
