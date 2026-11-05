using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// A contraption held with someone aboard, over the real transport (50 ms each way, jitter, 2% loss), a host, the holder, the
    /// passenger and a watcher, the holder working the tool through the game's own frame. Friendly (creative, then your own construct
    /// in survival): the grab takes, the passenger stays seated, unhurt and with a smooth view on every peer, the driver's keys
    /// don't fight the beam, and getting out leaves the hold. Not friendly (survival, a construct that isn't yours): the look line
    /// says who and the click does nothing; one who boards during a hold ends it, with the reason. The Nudge tool follows the same rule.
    /// </summary>
    private static void CheckSandboxPhysGunAboard()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail("physics gun aboard: no yard");
            return;
        }
        const ushort port = 26497;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        using var v = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Victor", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Game[] clients = [a, b, v];
        Pump(server, clients, 1.5, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            Pump(server, clients, 0.25, ref now, clock);
        Pump(server, clients, 0.5, ref now, clock);
        if (a.LocalId == byte.MaxValue || b.LocalId == byte.MaxValue || v.LocalId == byte.MaxValue)
        {
            Fail("physics gun aboard: the clients didn't join");
            return;
        }
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        bool WaitFor(Func<bool> ready, double seconds = 5.0)
        {
            for (var waited = 0.0; waited < seconds; waited += 0.05)
            {
                if (ready())
                    return true;
                Pump(server, clients, 0.05, ref now, clock);
            }
            return ready();
        }
        foreach (var g in new[] { a, b, v })
            if (!server.Players[g.LocalId].Alive)
                server.SelfTestRespawn(server.Players[g.LocalId], now);
        if (!WaitFor(() => a.Local is { Alive: true } && b.Local is { Alive: true } && v.Local is { Alive: true }))
        {
            Fail("physics gun aboard: the clients never saw themselves alive");
            return;
        }
        var w = server.Constructs;
        var hands = server.SandboxHands;
        foreach (var c in w.Bodies.ToList())
            w.Remove(c);
        var feet = ConstructWorld.Ground(World.Query, 0, 0);
        var bp = ConstructBuild.FindBlueprint("Buggy");
        var buggy = bp == null ? null : ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, 0, 8f), 0.3f, out _);
        if (buggy == null)
        {
            Fail("physics gun aboard: the Buggy doesn't build");
            return;
        }
        buggy.Owner = 255;
        var seat = buggy.DriveSeat;
        var pa = server.Players[a.LocalId];
        var pb = server.Players[b.LocalId];
        pa.Teleport(feet);
        var (keepCreative, keepMenu) = (yard.Sandbox!.InfiniteParts, yard.Sandbox.PartsMenu);
        void Sit(Game who)
        {
            server.Players[who.LocalId].Teleport(ConstructWorld.PartPoint(buggy, seat, Vector3.Zero) + new Vector3(1.2f, 0, 0));
            Pump(server, clients, 0.3, ref now, clock);
            who.SandboxSend(CActKind.Use, body: buggy.Id, part: (ushort)seat);
        }
        void Rules(bool creative)
        {
            yard.Sandbox!.InfiniteParts = yard.Sandbox.PartsMenu = creative;
        }
        void Own(byte owner)
        {
            buggy.Owner = owner;
            foreach (var g in clients)
                if (g.Constructs.Get(buggy.Id) is { } copy)
                    copy.Owner = owner; // (an owner is set when a body is made, on every peer; here it changes under a body already sent)
        }
        void StandUp(Game who) => who.SandboxSend(CActKind.Stand);
        // Aim the holder at the buggy, through their own look, and work the frame.
        void AimFrame(ReadOnlySpan<GameAction> keys)
        {
            var eye = a.Local!.RenderEye;
            var to = a.Constructs.Get(buggy.Id) is { } ac ? ac.WorldCom - eye : buggy.WorldCom - eye;
            a.LookYaw = MathF.Atan2(to.X, -to.Z);
            a.LookPitch = MathF.Asin(Math.Clamp(to.Y / to.Length(), -1f, 1f));
            Program.SweepFrame(a, now, keys);
        }
        var keys = new[] { GameAction.Fire };
        try
        {
            a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.PhysicsGun);
            Want(WaitFor(() => hands.Hand(a.LocalId).Tool == ConstructTool.PhysicsGun && a.Constructs.Get(buggy.Id) != null && v.Constructs.Get(buggy.Id) != null), "the tool or the buggy didn't arrive");
            var simOn = server.Net.SimulateBadNetwork(50, 10, 2) & a.Net.SimulateBadNetwork(50, 10, 2) & b.Net.SimulateBadNetwork(50, 10, 2) & v.Net.SimulateBadNetwork(50, 10, 2);
            Program.SweepRelease(a);

            // 1. Not friendly: survival, a construct that isn't the holder's.
            Rules(false);
            Pump(server, clients, 0.3, ref now, clock);
            Sit(b);
            Want(WaitFor(() => buggy.Driver == b.LocalId && a.Players[b.LocalId].SnapSeatBody == buggy.Id), "Bravo didn't get in");
            AimFrame([]);
            var look = a.SandboxLookText;
            AimFrame(keys);
            Pump(server, clients, 0.5, ref now, clock);
            Program.SweepFrame(a, now, []);
            Want(look.Contains("Bravo is aboard") && buggy.GrabbedBy == 255, $"not friendly: look \"{look}\", held by {buggy.GrabbedBy}");
            Want(hands.NudgeRefusal(buggy, a.LocalId)?.Contains("Bravo is aboard") == true && a.SandboxNudgeRefusal(buggy, a.LocalId)?.Contains("Bravo is aboard") == true, "not friendly: the Nudge tool doesn't refuse the same way");
            Line($"physics gun aboard: not friendly, look \"{look}\"; sim {simOn}");

            // 2. Your own construct: friendly. Then creative: grabbed through the frame, with the passenger.
            Own(a.LocalId);
            Pump(server, clients, 0.4, ref now, clock);
            Want(server.SandboxAboardRefusal(buggy, a.LocalId) == null && a.SandboxAboardRefusal(a.Constructs.Get(buggy.Id)!, a.LocalId) == null, "your own construct: the passenger still refuses");
            // (the Buggy is over survival's mass cap, so the carry is creative's: people have no contest there)
            Own(255);
            Rules(true);
            Pump(server, clients, 0.4, ref now, clock);
            AimFrame([]);
            look = a.SandboxLookText;
            AimFrame(keys);
            Want(WaitFor(() => buggy.GrabbedBy == a.LocalId, 3), $"friendly: the grab didn't take (look \"{look}\")");
            Want(a.SandboxNudgeRefusal(a.Constructs.Get(buggy.Id)!, a.LocalId) == null, $"friendly: the Nudge tool refuses ({a.SandboxNudgeRefusal(a.Constructs.Get(buggy.Id)!, a.LocalId)})");
            var health = pb.Health;
            var bHeld = new SmoothTrack();
            var vHeld = new SmoothTrack();
            var host = new SmoothTrack();
            var yaw0 = a.LookYaw;
            var pitch0 = a.LookPitch;
            var rel0 = hands.Hand(a.LocalId).GrabRel;
            var dist = hands.Hand(a.LocalId).GrabDist;
            var start = clock.Elapsed.TotalSeconds;
            var next = start;
            var hostTick = server.ServerTick;
            const double sweep = 3.0;
            float fight = 0;
            while (clock.Elapsed.TotalSeconds - start < sweep)
            {
                next = Math.Max(next + 0.01, clock.Elapsed.TotalSeconds + 0.002);
                while (clock.Elapsed.TotalSeconds < next)
                    Thread.Sleep(1);
                var t = clock.Elapsed.TotalSeconds;
                var dt = Math.Clamp(t - now, 0.001, 0.1);
                now = t;
                var phase = (float)((t - start) / sweep) * MathF.PI * 2;
                a.LookYaw = yaw0 + 0.4f * MathF.Sin(phase);
                a.LookPitch = pitch0 + 0.12f * (1 - MathF.Cos(phase));
                a.SandboxSteer(dist, ConstructGrab.Turn(rel0, 0.3f * MathF.Sin(phase * 0.5f), 0), now);
                b.HeldButtons = Buttons.Forward | Buttons.Left; // (the driver floors it and steers: the beam wins)
                server.Update(now, dt);
                a.Update(now, dt);
                b.Update(now, dt);
                v.Update(now, dt);
                fight = MathF.Max(fight, MathF.Abs(buggy.Throttle) + MathF.Abs(buggy.Steer));
                var aim = a.Local!.RenderEye + a.LookDir(a.LookYaw, a.LookPitch) * dist;
                if (b.Local is { } bl)
                    bHeld.Add(t, bl.RenderEye, aim);
                if (v.Players.TryGetValue(b.LocalId, out var seen))
                    vHeld.Add(t, seen.RenderEye, aim);
                if (server.ServerTick != hostTick)
                {
                    hostTick = server.ServerTick;
                    host.Add(hostTick * World.TickDt, pb.RenderEye, aim);
                }
            }
            b.HeldButtons = 0;
            var bs = bHeld.Measure(byAim: false);
            var vs = vHeld.Measure(byAim: false);
            var hs = host.Measure(byAim: false);
            Line($"physics gun aboard: friendly passenger carried 3 s at 50+50 ms, 2% loss: passenger's own view worst {bs.Max:0}, 98% {bs.P98:0} m/s2, back {bs.BackPerSec:0.0}/s; the watcher's sight of them {vs.Max:0} / {vs.P98:0}, back {vs.BackPerSec:0.0}/s; host {hs.Max:0} / {hs.P98:0}; driver's keys left {fight:0.00}");
            Want(buggy.GrabbedBy == a.LocalId, "friendly: the hold dropped during the carry");
            Want(buggy.Driver == b.LocalId && hands.Hand(b.LocalId).SeatBody == buggy.Id, "friendly: the passenger lost their seat");
            Want(pb.Health >= health && pb.Alive, $"friendly: the passenger was hurt ({health:0} to {pb.Health:0})");
            Want(fight < 0.01f, $"friendly: the driver's keys reached the body ({fight:0.00})");
            var bLimit = MathF.Max(SmoothP98, hs.P98 * 1.5f);
            Want(bs.P98 <= bLimit && vs.P98 <= bLimit, $"friendly: the seat jitters (98% {bs.P98:0}, watcher {vs.P98:0}, limit {bLimit:0})");
            Want(bs.Seconds > 2.5f && vs.Seconds > 2.5f, $"friendly: not drawn for the carry ({bs.Seconds:0.0} s, {vs.Seconds:0.0} s)");

            // 3. The passenger gets out during the hold: the hold stays, they are out.
            StandUp(b);
            Want(WaitFor(() => buggy.Driver == 255, 3), "the passenger couldn't get out of a held body");
            Pump(server, clients, 0.3, ref now, clock);
            Want(buggy.GrabbedBy == a.LocalId, "getting out ended the hold");
            Want(pb.Alive, "the passenger died getting out");

            // 4. Someone unfriendly boards a hold: a construct that isn't the holder's, in survival. It ends, with the reason.
            Rules(false);
            Pump(server, clients, 0.2, ref now, clock);
            Want(buggy.GrabbedBy == a.LocalId, "the hold didn't last until the boarder");
            Sit(b);
            Want(WaitFor(() => buggy.Driver == b.LocalId, 3), $"the boarder couldn't get in (eye {Vector3.Distance(pb.Eye, ConstructWorld.PartPoint(buggy, seat, Vector3.Zero)):0.0} m from the seat)");
            Want(WaitFor(() => buggy.GrabbedBy == 255, 3), "an unfriendly boarder didn't end the hold");
            Want(hands.Hand(a.LocalId).GrabBody == 0, "the hold's hand kept the body");
            Want(buggy.Driver == b.LocalId, "the boarder wasn't left in the seat");
        }
        finally
        {
            yard.Sandbox!.InfiniteParts = keepCreative;
            yard.Sandbox.PartsMenu = keepMenu;
            server.Net.SimulateBadNetwork(0, 0, 0);
            a.Net.SimulateBadNetwork(0, 0, 0);
            b.Net.SimulateBadNetwork(0, 0, 0);
            v.Net.SimulateBadNetwork(0, 0, 0);
            Program.SweepRelease(a);
        }
        foreach (var p in problems)
            Fail($"physics gun aboard: {p}");
    }
}
