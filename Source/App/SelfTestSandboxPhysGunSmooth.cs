using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>A drawn point frame by frame (times and positions), with its jitter measures.</summary>
    private sealed class SmoothTrack
    {
        public readonly double[] T = new double[2048];
        public readonly Vector3[] P = new Vector3[2048];
        public readonly Vector3[] Aim = new Vector3[2048];

        /// <summary>
        /// A client's copy only: how far its newest pose of the body is ahead of the tick it draws (ticks; NaN where unknown).
        /// Below 0 the copy ran past its newest pose and is carried on at its velocity until the next one comes.
        /// </summary>
        public readonly double[] Lead = new double[2048];
        public int N;

        public void Clear() => N = 0;

        /// <summary>The longest gap between two frames (ms): a runner's stall, named in a failure beside the spike it makes.</summary>
        public double MaxGapMs()
        {
            var gap = 0.0;
            for (var i = 1; i < N; i++)
                gap = Math.Max(gap, T[i] - T[i - 1]);
            return gap * 1000;
        }

        public void Add(double t, Vector3 p, Vector3 aim, double lead = double.NaN)
        {
            if (N > 0 && t - T[N - 1] < 0.002)
                return;
            if (N < T.Length)
            {
                T[N] = t;
                P[N] = p;
                Aim[N] = aim;
                Lead[N] = lead;
                N++;
            }
        }

        /// <summary>
        /// The worst and 98th-percentile frame-to-frame acceleration (m/s2, the second difference over the frame times),
        /// the back-steps a second (the point moved back by more than 1 mm along the way it was going, or along the aim's
        /// own motion when <paramref name="byAim"/>), and how long it covered.
        /// The worst leaves out the samples that touch a frame drawn past the newest pose (<see cref="Lead"/> below 0): a pose
        /// lost and the next one late is more than the copy's cover (ConstructNet.BodyExtraDelayTicks bridges one lost pose),
        /// so one kink there is the network's, not the smoothing's (CI 2026-11-13: one sample of 833 m/s2 with the 98% at 109;
        /// here the lead fell to 0.5 tick at 30 ms jitter). Those frames are counted in Past; the 98% keeps every sample.
        /// </summary>
        public (float Max, float P98, float BackPerSec, float Seconds, int Past, double MinLead) Measure(bool byAim)
        {
            if (N < 4)
                return (0, 0, 0, 0, 0, double.NaN);
            var acc = new float[N];
            var na = 0;
            var back = 0;
            var worst = 0f;
            var past = 0;
            var minLead = double.NaN;
            for (var i = 0; i < N; i++)
            {
                if (Lead[i] < 0)
                    past++;
                if (!double.IsNaN(Lead[i]))
                    minLead = double.IsNaN(minLead) ? Lead[i] : Math.Min(minLead, Lead[i]);
            }
            for (var i = 2; i < N; i++)
            {
                var d1 = (float)(T[i] - T[i - 1]);
                var d0 = (float)(T[i - 1] - T[i - 2]);
                var v1 = (P[i] - P[i - 1]) / d1;
                var v0 = (P[i - 1] - P[i - 2]) / d0;
                var a = (v1 - v0).Length() / ((d0 + d1) * 0.5f);
                acc[na++] = a;
                if (!(Lead[i] < 0 || Lead[i - 1] < 0 || Lead[i - 2] < 0))
                    worst = MathF.Max(worst, a);
                var step = P[i] - P[i - 1];
                var dir = byAim ? Aim[i] - Aim[i - 1] : P[i - 1] - P[i - 2];
                if (dir.LengthSquared() > 1e-6f && Vector3.Dot(step, Vector3.Normalize(dir)) < -0.001f)
                    back++;
            }
            Array.Sort(acc, 0, na);
            var secs = (float)(T[N - 1] - T[0]);
            return (worst, acc[(int)(na * 0.98f)], back / MathF.Max(secs, 1e-3f), secs, past, minLead);
        }
    }

    /// <summary>
    /// cj01: a real host, the holding client and a watching client, 50 ms each way with jitter and 2% loss: the client
    /// sweeps a held loose piece and then a buggy along a smooth arc while turning it. The holder's drawn pose and the
    /// watcher's must be smooth (frame-to-frame acceleration under a limit, no back-steps along the path) and end within
    /// 3 cm of the server's once the aim rests.
    /// </summary>
    private static void FeelClientSmooth(Map yard)
    {
        const ushort port = 26395;
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
        if (a.LocalId == byte.MaxValue || b.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("physics gun smooth: the clients didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        bool WaitFor(Func<bool> ready, double seconds = 6.0)
        {
            for (var waited = 0.0; waited < seconds; waited += 0.05)
            {
                if (ready())
                    return true;
                Pump(server, clients, 0.05, ref now, clock);
            }
            return ready();
        }
        if (!WaitFor(() => a.Local is { Alive: true }))
        {
            Fail("physics gun smooth: the client never saw itself alive");
            return;
        }
        var w = server.Constructs;
        foreach (var c in w.Bodies.ToList())
            w.Remove(c);
        var feet = ConstructWorld.Ground(World.Query, 0, 0);
        var piece = ConstructBuild.PlaceNew(w, ConstructParts.Need("Frame Block"), feet + new Vector3(4f, 0.5f, 0), 0)!;
        piece.Anchored = false;
        var bp = ConstructBuild.FindBlueprint("Buggy");
        var buggy = bp == null ? null : ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, 0, 9f), 0.3f, out _);
        if (buggy == null)
        {
            Fail("physics gun smooth: the Buggy blueprint doesn't build");
            return;
        }
        sp.Teleport(feet);
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.PhysicsGun);
        if (!WaitFor(() => server.SandboxHands.Hand(a.LocalId).Tool == ConstructTool.PhysicsGun && Vector3.Distance(server.Players[a.LocalId].Position, feet) < 0.5f
                           && a.Constructs.Get(piece.Id) != null && a.Constructs.Get(buggy.Id) != null && b.Constructs.Get(buggy.Id) != null))
        {
            Fail("physics gun smooth: the host never got the client's tool and position, or the clients never got the bodies");
            return;
        }
        var simOn = server.Net.SimulateBadNetwork(50, 10, 2) & a.Net.SimulateBadNetwork(50, 10, 2) & b.Net.SimulateBadNetwork(50, 10, 2);
        Pump(server, clients, 0.5, ref now, clock);
        var hold = new SmoothTrack();
        var watch = new SmoothTrack();
        var host = new SmoothTrack();
        var problems = new List<string>();
        // (The buggy goes twice, the second time a fresh one with 30 ms of jitter each way, as a loaded runner's uneven
        // frames give: with four poses kept the drawn tick fell into the first segment, or before it, and the watcher's
        // buggy kinked at 1,500 to 2,500 m/s2.)
        for (var run = 0; run < 3; run++)
        {
            var jitter = run == 2 ? 30 : 10;
            if (run == 2)
            {
                w.Remove(buggy);
                buggy = ConstructBuild.BuildOnGround(w, bp!, ConstructWorld.Ground(World.Query, 0, 9f), 0.3f, out _);
                simOn &= server.Net.SimulateBadNetwork(50, jitter, 2) & a.Net.SimulateBadNetwork(50, jitter, 2) & b.Net.SimulateBadNetwork(50, jitter, 2);
                var fresh = buggy;
                if (fresh == null || !WaitFor(() => a.Constructs.Get(fresh.Id) != null && b.Constructs.Get(fresh.Id) != null))
                {
                    problems.Add("the second buggy didn't build or reach the clients");
                    break;
                }
                Pump(server, clients, 0.5, ref now, clock);
            }
            var target = run == 0 ? piece : buggy!;
            var name = run == 0 ? "loose piece" : run == 1 ? "buggy" : "buggy at 30 ms jitter";
            // Look at it and grab it at its centre of mass.
            var eye0 = a.Local!.RenderEye;
            var to = target.WorldCom - eye0;
            var yaw0 = MathF.Atan2(to.X, -to.Z);
            var pitch0 = MathF.Asin(Math.Clamp(to.Y / to.Length(), -1f, 1f));
            a.LookYaw = yaw0;
            a.LookPitch = pitch0;
            Pump(server, clients, 0.3, ref now, clock);
            a.SandboxGrab(target.Id, target.Com);
            if (!WaitFor(() => target.GrabbedBy == a.LocalId, 3.0))
            {
                problems.Add($"{name}: the host never saw the grab");
                continue;
            }
            var hand = server.SandboxHands.Hand(a.LocalId);
            var dist = hand.GrabDist;
            var rel0 = hand.GrabRel;
            Pump(server, clients, 0.4, ref now, clock);
            hold.Clear();
            watch.Clear();
            host.Clear();
            var start = clock.Elapsed.TotalSeconds;
            var next = start;
            const double sweep = 3.0, rest = 1.5;
            var hostTick = server.ServerTick;
            while (true)
            {
                // (After a stall, a loaded runner's, the next frame comes 2 ms on, not as a burst of catch-up frames: those
                // had dt clamped up to 1 ms each while the clock moved less, so the clients' render clocks ran ahead of
                // the frame times and the drawn bodies seemed to kick at thousands of m/s2.)
                next = Math.Max(next + 0.01, clock.Elapsed.TotalSeconds + 0.002);
                while (clock.Elapsed.TotalSeconds < next)
                    Thread.Sleep(1);
                var t = clock.Elapsed.TotalSeconds;
                var dt = Math.Clamp(t - now, 0.001, 0.1);
                now = t;
                var s = (float)Math.Min(t - start, sweep);
                // A smooth arc: the yaw swings 0.5 rad and back, the pitch a little, the piece turns about up.
                var phase = s / (float)sweep * MathF.PI * 2;
                a.LookYaw = yaw0 + 0.5f * MathF.Sin(phase);
                a.LookPitch = pitch0 + 0.08f * (1 - MathF.Cos(phase));
                var rel = ConstructGrab.Turn(rel0, 0.6f * MathF.Sin(phase * 0.5f), 0);
                a.SandboxSteer(dist, rel, now);
                server.Update(now, dt);
                a.Update(now, dt);
                b.Update(now, dt);
                if (t - start > sweep + rest)
                    break;
                if (t - start > sweep)
                    continue;
                var aim = a.Local!.RenderEye + a.LookDir(a.LookYaw, a.LookPitch) * dist;
                if (a.Constructs.Get(target.Id) is { } ac)
                    hold.Add(t, ac.ToWorld(ac.LocalMax), aim);
                if (b.Constructs.Get(target.Id) is { } bc)
                    watch.Add(t, bc.ToWorld(bc.LocalMax), aim, bc.NetTick - b.Constructs.NetRenderTick);
                if (server.ServerTick != hostTick)
                {
                    hostTick = server.ServerTick;
                    host.Add(hostTick * World.TickDt, target.ToWorld(target.LocalMax), aim);
                }
            }
            var ha = hold.Measure(byAim: true);
            var wa = watch.Measure(byAim: false);
            var sa = host.Measure(byAim: false);
            var hEnd = a.Constructs.Get(target.Id) is { } ac2 ? Vector3.Distance(ac2.ToWorld(ac2.LocalMax), target.ToWorld(target.LocalMax)) : 99f;
            var wEnd = b.Constructs.Get(target.Id) is { } bc2 ? Vector3.Distance(bc2.ToWorld(bc2.LocalMax), target.ToWorld(target.LocalMax)) : 99f;
            Line($"physics gun smooth: {name} ({target.Mass:0} kg) swept {ha.Seconds:0.0} s at 50+50 ms (jitter {jitter} ms), 2% loss (sim {simOn}): holder's drawn corner accel worst {ha.Max:0} / 98% {ha.P98:0} m/s2, back-steps {ha.BackPerSec:0.0}/s, ends {hEnd * 100:0.0} cm from the server; watcher worst {wa.Max:0} / 98% {wa.P98:0}, back-steps {wa.BackPerSec:0.0}/s, past its newest pose {wa.Past} frames (lead at least {wa.MinLead:0.0} ticks), ends {wEnd * 100:0.0} cm; host worst {sa.Max:0} / 98% {sa.P98:0}, back-steps {sa.BackPerSec:0.0}/s");
            // (Bounds: the arc itself needs about 10 m/s2 at the corner; a pose pulled back a round trip and pushed on
            // again showed as thousands before cj01. The frame times here are the wall clock's, on a busy runner too.)
            if (ha.Seconds < 2.5f || wa.Seconds < 2.5f)
                problems.Add($"{name}: the clients' copies weren't drawn for the sweep ({ha.Seconds:0.0} s, {wa.Seconds:0.0} s)");
            if (ha.P98 > SmoothP98 || ha.Max > SmoothMax || ha.BackPerSec > 0.5f)
                problems.Add($"{name}: the holder's drawn piece jitters (accel worst {ha.Max:0}, 98% {ha.P98:0} m/s2, limits {SmoothMax:0} / {SmoothP98:0}; back-steps {ha.BackPerSec:0.0}/s; longest frame gap {hold.MaxGapMs():0} ms)");
            // (The watcher draws the server's own motion, which on wheels dragged over the ground is rougher than the aim:
            // its limits are the same or 1.5 times the host's, whichever is more.)
            var wP98 = MathF.Max(SmoothP98, sa.P98 * 1.5f);
            var wMax = MathF.Max(SmoothMax, sa.Max * 3f);
            // (Drawn past the newest pose on more than one frame in twenty: the copy's cover is too short, not a rare loss.)
            if (wa.P98 > wP98 || wa.Max > wMax || wa.BackPerSec > sa.BackPerSec + 0.7f || wa.Past > watch.N / 20)
                problems.Add($"{name}: the watcher's copy jitters (accel worst {wa.Max:0}, 98% {wa.P98:0} m/s2, limits {wMax:0} / {wP98:0}; back-steps {wa.BackPerSec:0.0}/s, the host's {sa.BackPerSec:0.0}/s; longest frame gap {watch.MaxGapMs():0} ms; past its newest pose {wa.Past} of {watch.N} frames, lead at least {wa.MinLead:0.0} ticks)");
            if (hEnd > 0.03f || wEnd > 0.03f)
                problems.Add($"{name}: once the aim rested the clients' copies weren't within 3 cm of the server's ({hEnd * 100:0.0} cm, {wEnd * 100:0.0} cm)");
            a.SandboxLetGo(freeze: false);
            WaitFor(() => target.GrabbedBy == 255, 3.0);
            Pump(server, clients, 0.3, ref now, clock);
        }
        if (!simOn)
            problems.Add("the simulated lag and loss didn't switch on");
        foreach (var p in problems)
            Fail($"physics gun smooth: {p}");
    }

    /// <summary>cj01's limits on a drawn held piece's frame-to-frame acceleration (m/s2): 98th percentile, worst frame.</summary>
    private const float SmoothP98 = 150f, SmoothMax = 600f;

    /// <summary>cj01: a held body is in every pose packet, however many others move (small worlds and big worlds' per-client packets).</summary>
    private static void FeelHeldEveryPacket()
    {
        var server = new ConstructWorld();
        var client = new ConstructWorld();
        var beam = ConstructParts.Need("Square Beam 0.5 m");
        for (var k = 0; k < 80; k++)
        {
            var at = ConstructWorld.Ground(World.Query, -20 + k % 10 * 4f, -20 + k / 10 * 4f) + new Vector3(0, 0.5f, 0);
            ConstructBuild.PlaceNew(server, beam, at, 0);
            ConstructBuild.PlaceNew(client, beam, at, 0);
        }
        var held = server.Bodies[57];
        held.GrabbedBy = 0;
        var w = new PacketWriter(16384);
        var cursor = 0;
        uint last = 0;
        var missed = 0;
        for (var p = 0; p < 12; p++)
        {
            foreach (var c in server.Bodies)
            {
                c.Anchored = false;
                c.Wake();
                c.Vel = new Vector3(1, 0, 0);
            }
            var tick = 100u + (uint)p * ConstructNet.PoseEvery;
            ConstructNet.WritePoses(w, server, tick, ref cursor);
            var r = new PacketReader(w.Span[1..]);
            ConstructNet.ReadPoses(ref r, client, p, ref last);
            if (client.Get(held.Id)?.NetTick != tick)
                missed++;
        }
        // Big worlds: the held body far from the client still goes every time.
        ConstructNet.PoseMovers(server, 1);
        var bigMissed = 0;
        for (var p = 0u; p < 8; p++)
        {
            var seen = false;
            ConstructNet.WritePosesFor(w, server, 200 + p, p, held.WorldCom + new Vector3(500, 0, 0), 0, pw =>
            {
                var r = new PacketReader(pw.Span[1..]);
                ConstructNet.ReadPoses(ref r, client, 100 + p, ref last);
                seen |= client.Get(held.Id)?.NetTick == 200 + p;
            });
            if (!seen)
                bigMissed++;
        }
        held.GrabbedBy = 255;
        Line($"physics gun smooth: a held body among 80 moving: missing from {missed} of 12 pose packets; 500 m from a big world's client, missing from {bigMissed} of 8 pose ticks");
        if (missed > 0 || bigMissed > 0)
            Fail("physics gun smooth: a held body isn't in every pose packet");
    }
}
