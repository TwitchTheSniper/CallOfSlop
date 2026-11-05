using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Ride frame", entries and drops (a player's video, 2026-11-05): frozen upside down the rider stays on; a body coming
    /// down over a deck's rim or a beam lands on it, on a tipped or inverted one too, however fast, not pushed off; and a
    /// construct that has just dropped its rider pushes them, it doesn't run them over.
    /// </summary>
    private static void RideEntries(Map yard)
    {
        foreach (var angle in new[] { MathF.PI, 2.97f })
        {
            var w = new ConstructWorld();
            if (RideDeck(w, -6, -6, 25f, 3) is not { } c)
                return;
            var s = RideBoard(w, c, DeckTop(0.2f, 1.4f));
            var r0 = c.Rot;
            var p0 = c.Pos;
            var pivot = c.WorldCom;
            for (var t = 0; t < 240; t++)
            {
                var a = MathF.Min(t * World.TickDt * 1.5f, angle);
                var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, a);
                RidePose(w, c, pivot + Vector3.Transform(p0 - pivot, q), q * r0, Vector3.Zero, a < angle ? new Vector3(0, 0, 1.5f) : Vector3.Zero);
                c.Anchored = a >= angle; // (frozen there, as the physics gun's freeze does)
                RideTick(w, ref s);
            }
            if (s.RideBody != c.Id)
                Fail($"ride entry: frozen at {angle * 180 / MathF.PI:0} deg the rider dropped off (at {s.Position})");
        }
        // Coming down with the centre past the edge, then a fall fast enough to pass a thin plate in one tick.
        foreach (var (part, turn, past, drop) in new[]
                 {
                     ("Deck Plate", 0f, 0.2f, 4f), ("Frame Beam", 0f, 0.2f, 4f), ("Frame Beam", MathF.PI, 0.15f, 4f),
                     ("Frame Beam", MathF.PI / 2, 0.15f, 4f), ("Deck Plate", 0f, 0f, 60f),
                 })
        {
            var w = new ConstructWorld();
            if (RideDeck(w, -6, -6, drop > 30 ? 40f : 8f, 1, part) is not { } c)
                return;
            RidePose(w, c, c.Pos, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, turn) * c.Rot, Vector3.Zero, Vector3.Zero);
            var com = c.WorldCom;
            var half = (c.LocalMax.X - c.LocalMin.X) * 0.5f;
            var s = new MoveState { Position = new Vector3(com.X + half + past, com.Y + 0.3f + drop, com.Z) };
            for (var t = 0; t < 180; t++)
            {
                RidePose(w, c, c.Pos, c.Rot, Vector3.Zero, Vector3.Zero);
                RideTick(w, ref s);
            }
            if (s.RideBody != c.Id)
                Fail($"ride entry: a {part} turned {turn:0.0} rad didn't take a body coming down {past} m past its edge from {drop} m (feet {s.Position}, its centre {com})");
        }
        RideNoRunOver(yard);
    }

    /// <summary>A heavy deck drives through someone who just rode it (pushed, unhurt), then through them again later (hurt).</summary>
    private static void RideNoRunOver(Map yard)
    {
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me)
        {
            Fail("ride entry: no local player");
            return;
        }
        int Pass(float wait, float z)
        {
            // On a deck, then off it beside it, then the deck drives into them after the wait.
            if (RideDeck(g.Constructs, 14, z, 0.3f, 3) is not { } c)
                return -1;
            var start = c.Pos;
            me.Teleport(c.ToWorld(DeckTop(0.2f, 1.4f)) + new Vector3(0, 0.03f, 0));
            me.ImmuneUntil = 0;
            me.Health = World.MaxHealth;
            for (var k = 0; k < 20; k++)
            {
                now += World.TickDt;
                g.Update(now, World.TickDt);
            }
            var rode = me.Sim.RideBody == c.Id;
            me.Teleport(ConstructWorld.Ground(World.Query, start.X + 6f, start.Z + 2f));
            for (var k = 0; k < wait * 60; k++)
            {
                now += World.TickDt;
                g.Update(now, World.TickDt);
            }
            var vel = new Vector3(14f, 0, 0);
            for (var k = 0; k < 40 && me.Health == World.MaxHealth && me.Alive; k++)
            {
                c.Pos = new Vector3(c.Pos.X + vel.X * World.TickDt, start.Y + 0.6f, c.Pos.Z); // (chest high: a settled plate would pass under the feet)
                c.Vel = vel;
                c.AngVel = Vector3.Zero;
                c.Asleep = false;
                now += World.TickDt;
                g.Update(now, World.TickDt);
            }
            if (!rode)
                Fail($"ride entry: the player didn't board the deck (wait {wait}, deck {c.Pos}, feet {me.Position})");
            return World.MaxHealth - me.Health;
        }
        var fresh = Pass(0f, -14);
        var later = Pass(2f, 6);
        Line($"ride entry: a deck at 14 m/s into its rider just off it: {fresh} damage; 2 s later: {later}");
        if (fresh > 0 || later <= 0)
            Fail($"ride entry: a rider just off a construct shouldn't be run over by it, and later should be (damage {fresh}, then {later})");
    }

    /// <summary>The fall onto a fast deck on the wire: the client's copy is a round trip behind, and its landing is eased onto the server's, never snapped.</summary>
    private static void RideLandOnWire(Game server, Game a, Game[] clients, Player sp, Stopwatch clock, ref double now, float z)
    {
        var w = server.Constructs;
        if (RideDeck(w, -8, z, 12f, 5) is not { } c)
            return;
        var pos0 = c.Pos;
        var mid = c.ToWorld(new Vector3(0, 0.05f, 4));
        var vel = new Vector3(0, 0, 14f);
        w.StructureDirty.Add(c.Id);
        Pump(server, clients, 0.4, ref now, clock);
        var start = clock.Elapsed.TotalSeconds;
        void Drive()
        {
            c.Pos = pos0 + vel * (float)(clock.Elapsed.TotalSeconds - start);
            c.Vel = vel;
            c.AngVel = Vector3.Zero;
            c.Asleep = false;
            c.Anchored = false;
        }
        PumpWith(server, clients, 0.5, ref now, clock, Drive);
        var meet = (float)(clock.Elapsed.TotalSeconds - start) + 1.03f; // (10 m of fall)
        sp.Teleport(new Vector3(mid.X, mid.Y + 10f, mid.Z + 14f * meet));
        var until = clock.Elapsed.TotalSeconds + 2.4;
        var worst = 0f;
        var last = Vector3.Zero;
        var has = false;
        while (clock.Elapsed.TotalSeconds < until)
        {
            PumpWith(server, clients, 0.004, ref now, clock, Drive);
            if (a.Local is not { } me || a.Constructs.Get(c.Id) is not { } cc)
                continue;
            if (sp.Sim.RideBody != c.Id && me.Sim.RideBody != c.Id)
                continue;
            var local = a.SelfTestDrawnLocal(cc, 0, me.RenderPosition);
            if (has)
                worst = MathF.Max(worst, Vector3.Distance(local, last));
            (last, has) = (local, true);
        }
        Line($"ride entry (wire): a 10 m fall onto a deck at 14 m/s: the biggest step in the deck's frame {worst:0.00} m; server {sp.Sim.RideBody == c.Id}, client {a.Local?.Sim.RideBody == c.Id}");
        if (sp.Sim.RideBody != c.Id || a.Local?.Sim.RideBody != c.Id)
            Fail($"ride entry (wire): the rider isn't on the deck (server {sp.Sim.RideBody}, client {a.Local?.Sim.RideBody}, deck {c.Id})");
        else if (a.Local is { } m && Vector3.Distance(m.Sim.RideLocal, sp.Sim.RideLocal) > 0.05f)
            Fail($"ride entry (wire): the client and the server disagree on the deck ({m.Sim.RideLocal}, {sp.Sim.RideLocal})");
        if (worst > 0.6f)
            Fail($"ride entry (wire): the landing snapped: {worst:0.00} m in a frame in the deck's frame");
        c.Anchored = true;
        c.Vel = Vector3.Zero;
        c.Pos = pos0;
        sp.Teleport(new Vector3(0, 1, 0));
    }
}
