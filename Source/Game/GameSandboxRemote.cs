using System.Numerics;

namespace CallOfSlop;

/// <summary>The wireless seat link in the game (ConstructRemote.cs): each tick a seat that sends passes its sitter's keys and look to the drone. Server; a client shows it from who drives what.</summary>
public sealed partial class Game
{
    private SbText _sbRemote;

    /// <summary>A sitter's tick: finds their drone at 10 Hz, then its keys, look and drive, as the seat's own. Out of range it keeps the drone and gives it no keys.</summary>
    private void SandboxRemote(Player p, ref SandboxHand h, Construct c, int seat, Buttons b)
    {
        if (!ConstructRemote.Sends(c, seat))
        {
            if (h.RemoteBody != 0)
                SandboxRemoteRelease(ref h, p.Id);
            return;
        }
        if (h.RemoteScan++ % ConstructRemote.ScanEvery == 0)
        {
            var had = Constructs.Get(h.RemoteBody);
            if (had == null || had.Receiver < 0 || ConstructRemote.Signal(c, seat, had) <= 0)
            {
                var found = ConstructRemote.Find(Constructs, c, seat, p.Id, c.Owner, out _);
                if (found != null && found != had)
                {
                    SandboxRemoteRelease(ref h, p.Id);
                    h.RemoteBody = found.Id;
                }
            }
        }
        if (h.RemoteBody == 0)
            return;
        if (Constructs.Get(h.RemoteBody) is not { } d || d.Receiver < 0 || d.Receiver >= d.PartCount)
        {
            h.RemoteBody = 0;
            return;
        }
        if (d.Dirty)
            d.Rebuild();
        var rx = d.Receiver;
        var live = ConstructRemote.Signal(c, seat, d) > 0;
        ref var r = ref d.Parts[rx];
        var keys = live ? b : default;
        r.Keys = (ushort)((ushort)keys | (live ? h.SeatKeys & 0x7F00 : 0) | CPart.Occupied);
        if (live)
        {
            ConstructRemote.MapLook(c, seat, d, rx, p.Yaw, p.Pitch, out var yaw, out var pitch);
            r.AimYaw = yaw;
            r.AimPitch = pitch;
        }
        d.Driver = p.Id;
        d.DriverSeat = rx;
        if (rx == d.DriveSeat)
        {
            ConstructEdits.DriveInput(d, keys, World.TickDt);
            d.RollInput = live ? ConstructEdits.RollOf(h.SeatKeys) : 0;
        }
        if (live)
            d.Wake();
    }

    private void SandboxRemoteRelease(ref SandboxHand h, byte player)
    {
        if (Constructs.Get(h.RemoteBody) is { } d && d.Driver == player)
            SandboxHands.StopDriving(d);
        h.RemoteBody = 0;
    }

    /// <summary>Where a player's poses are centred (ConstructNet.WritePosesFor): a pilot's on their drone, so the bodies round it stay current however far off it is.</summary>
    private Vector3 SandboxInterestAt(Player p)
    {
        ref var h = ref SandboxHands.Hand(p.Id);
        return h.RemoteBody != 0 && Constructs.Get(h.RemoteBody) is { } d ? d.WorldCom : p.Position;
    }

    /// <summary>The drone this PC's player flies from their seat, or null: the view, the third-person orbit and the streamed ground go there. Looks at the bodies only in a seat that sends.</summary>
    public Construct? LocalDrone()
    {
        if (Local is not { Alive: true } me)
            return null;
        ref var h = ref SandboxHands.Hand(me.Id);
        if (!h.Seated || Constructs.Get(h.SeatBody) is not { } c || h.SeatPart >= c.PartCount || !ConstructRemote.Sends(c, h.SeatPart))
            return null;
        return ConstructRemote.Piloted(Constructs, me.Id);
    }

    /// <summary>A pilot's HUD lines, in place of the seat's. False: not piloting.</summary>
    private bool SandboxRemoteHud(Player me, in SandboxHand h, List<HudLine> lines, Vector4 white, Vector4 dim, Vector4 red)
    {
        if (Constructs.Get(h.SeatBody) is not { } c || h.SeatPart >= c.PartCount || ConstructRemote.Piloted(Constructs, me.Id) is not { } d)
            return false;
        var signal = (int)MathF.Ceiling(ConstructRemote.Signal(c, h.SeatPart, d) * 100);
        var kmh = (int)MathF.Round(d.Vel.Length() * 3.6f);
        if (_sbRemote.Stale(signal * 100000L + kmh + ((long)d.Id << 32) + ((long)SandboxKeys.Stamp << 50)))
            _sbRemote.Text = signal > 0 ? $"Drone: {kmh} km/h, signal {signal}%" : "Drone: signal lost - it holds where it is - get in range or check its power";
        lines.Add(new HudLine(_sbRemote.Text, signal > 0 ? white : red));
        lines.Add(new HudLine(d.ThrusterCount > 0 || d.StabCount > 0 ? ConstructFlight.KeyHint(d) : SandboxKeys.Drive, dim));
        return true;
    }
}
