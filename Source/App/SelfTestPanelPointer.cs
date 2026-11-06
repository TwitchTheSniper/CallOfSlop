using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// A seated rider holding a fixed aim on a panel of their own craft at 30 m/s, host and client over the real transport: the spot under the crosshair stays put, frame after frame.
    /// The bug: the eye was last frame's drawn pose and the panel the tick's, up to 0.5 m apart here (<see cref="PanelRender.DrawnSpot"/>).
    /// </summary>
    private static void CheckPanelPointer()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
            return;
        const ushort port = 26501;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        try
        {
            using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
            server.Mode = GameMode.Sandbox;
            using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Pump(server, [a], 1.2, ref now, clock);
            for (var i = 0; i < 40 && server.Phase != MatchPhase.Playing; i++)
                Pump(server, [a], 0.25, ref now, clock);
            foreach (var who in new[] { server, a })
            {
                var id = who == server ? server.LocalId : a.LocalId;
                if (!server.Players.TryGetValue(id, out var pl))
                {
                    Fail($"panel pointer: no player {id}");
                    return;
                }
                if (!pl.Alive)
                    server.SelfTestRespawn(pl, now);
            }
            Pump(server, [a], 0.3, ref now, clock);
            var w = server.Constructs;
            var seatDef = ConstructParts.Need("Seat");
            foreach (var (peer, label) in new[] { (server, "host"), (a, "client") })
            {
                var spot = OpenSpot(w)!.Value;
                var c = ConstructBuild.PlaceNew(w, seatDef, new FreePlacement { Pos = spot + new Vector3(0, 300, 0), Rot = Quaternion.Identity })!;
                w.AddPart(c, ConstructParts.Need("Video Screen"), new Vector3(0, 1.2f, 1.6f), 0, 0);
                c.Rebuild();
                var screen = c.PartCount - 1;
                ref var hand = ref server.SandboxHands.Hand(peer.LocalId);
                hand.SeatBody = c.Id;
                hand.SeatPart = 0;
                c.Parts[0].SitterPlus1 = (byte)(peer.LocalId + 1);
                c.Driver = peer.LocalId;
                c.DriverSeat = 0;
                c.Wake();
                var key = PanelRender.Key(c.Id, screen, 0);
                var panel = c.DefOf(screen).Panels[0];
                var low = new Vector2(float.MaxValue);
                var high = new Vector2(float.MinValue);
                Vector3? aim = null, from = null;
                var to = Vector3.Zero;
                var oldLow = new Vector2(float.MaxValue);
                var oldHigh = new Vector2(float.MinValue);
                var frames = 0;
                var end = clock.Elapsed.TotalSeconds + 3;
                while (clock.Elapsed.TotalSeconds < end)
                {
                    c.Vel = new Vector3(30, 0, 0);
                    c.PrevPos.Y = c.Pos.Y = spot.Y + 300;
                    var t = clock.Elapsed.TotalSeconds;
                    var dt = Math.Clamp(t - now, 0.001, 0.05);
                    now = t;
                    server.Update(now, dt);
                    a.Update(now, dt);
                    Thread.Sleep(frames % 3 == 0 ? 2 : 11); // (uneven frames: the blend between ticks is where it showed)
                    if (peer.Constructs.Get(c.Id) is not { PartCount: 2 } copy || peer.Local is not { } me || !peer.SandboxHands.Hand(me.Id).Seated)
                        continue;
                    // What this peer's picture holds: the eye and the panel as drawn, and an aim that doesn't change.
                    var centre = ConstructRender.DrawnPoint(copy, screen, panel.At, peer.TickAlpha, peer.IsServer);
                    PanelRender.BeginWorld(me.RenderEye, aim ??= Vector3.Normalize(centre - me.RenderEye));
                    PanelRender.NoteDrawn(key, centre, -Vector3.UnitZ, Vector3.UnitY);
                    if (!PanelRender.DrawnSpot(key, out var at))
                        continue;
                    if (++frames < 30)
                        continue; // (settling into the seat)
                    low = Vector2.Min(low, at);
                    high = Vector2.Max(high, at);
                    from ??= centre;
                    to = centre;
                    if (PanelTouch.Project(copy, screen, panel, me.RenderEye, aim.Value, out var old))
                    {
                        oldLow = Vector2.Min(oldLow, old);
                        oldHigh = Vector2.Max(oldHigh, old);
                    }
                }
                var spread = high - low;
                var travel = Vector3.Distance(from ?? to, to);
                Console.WriteLine($"panel pointer, {label}: {frames} frames, {travel:0} m travelled, the spot moved {spread.X * 1000:0.0} x {spread.Y * 1000:0.0} mm; from the tick pose it moved {(oldHigh.X - oldLow.X) * 1000:0} x {(oldHigh.Y - oldLow.Y) * 1000:0} mm");
                if (travel < 30)
                    Fail($"panel pointer: {label}: the craft only went {travel:0.0} m: this proves nothing");
                if (frames < 60 || spread.X > 0.03f || spread.Y > 0.03f)
                    Fail($"panel pointer: {label}: {frames} frames, the spot moved {spread.X:0.000} x {spread.Y:0.000} m at 30 m/s");
                hand.SeatBody = 0;
                c.Parts[0].SitterPlus1 = 0;
                c.Driver = 255;
                c.Vel = default;
            }
        }
        finally
        {
            yard.RestoreProps();
        }
    }
}
