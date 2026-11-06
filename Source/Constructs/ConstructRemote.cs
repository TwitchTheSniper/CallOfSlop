using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// The wireless seat link (docs/sandbox/CONSTRUCTS.md, "Drones"). A <b>sender</b> is a seat that transmits (a Remote Console), or a
/// Remote Transmitter wired from any seat. A <b>receiver</b> (a Drone Receiver) has the seat role with nobody in it: linked into
/// a Driving or Turret Module on its own construct it is the seat there, so the drive line, guns, wires and the
/// module's outputs need nothing new and a drone needs no seat. The server copies the sitter's keys and look to it every
/// tick, through the same fields a seat has. Channels are the logic parts' (1 to 99), and the range is the sender's. A sitter
/// reaches a receiver that is theirs or nobody's. With the signal lost the drone keeps its driver and has no keys: it
/// stops, and a flyer's hover hold stays on, until the signal is back or the pilot gets up.
/// </summary>
public static class ConstructRemote
{
    public const int ScanEvery = 6; // ticks between looks for the receiver (10 Hz)

    /// <summary>The range and channel a seat sends on: its own, or a Remote Transmitter's wired from it. 0: it sends nothing.</summary>
    public static float SenderOf(Construct c, int seat, out int channel)
    {
        ref var p = ref c.Parts[seat];
        channel = 1;
        if (p.D.RemoteRange > 0)
        {
            channel = (int)ConstructConfig.Get(p, p.D, CfgKey.Channel, 1);
            return p.Broken || !ConstructPower.On(c, seat) ? 0 : p.D.RemoteRange;
        }
        for (var k = 0; k < c.WireCount; k++)
            if (c.Wires[k].From == seat && c.Wires[k].To < c.PartCount && c.Parts[c.Wires[k].To] is { Broken: false } t && t.D.Logic == CLogic.RemoteLink)
            {
                channel = (int)ConstructConfig.Get(t, t.D, CfgKey.Channel, 1);
                return ConstructPower.On(c, c.Wires[k].To) ? t.D.RemoteRange : 0;
            }
        return 0;
    }

    /// <summary>Does this seat send at all, working or not?</summary>
    public static bool Sends(Construct c, int seat)
    {
        if (c.Parts[seat].D.RemoteRange > 0)
            return true;
        for (var k = 0; k < c.WireCount; k++)
            if (c.Wires[k].From == seat && c.Wires[k].To < c.PartCount && c.Parts[c.Wires[k].To].D.Logic == CLogic.RemoteLink)
                return true;
        return false;
    }

    private static Vector3 PartAt(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out var t);
        return t + Vector3.Transform(p.Pos, q);
    }

    /// <summary>Is this working receiver on this channel, and within <paramref name="range"/> of <paramref name="from"/>? Its distance squared, or -1.</summary>
    public static float Hears(Construct d, int rx, int channel, float range, Vector3 from)
    {
        ref var p = ref d.Parts[rx];
        if (p.Broken || !p.D.RemoteReceiver || (int)ConstructConfig.Get(p, p.D, CfgKey.Channel, 1) != channel || !ConstructPower.On(d, rx))
            return -1;
        var dist = Vector3.DistanceSquared(PartAt(d, rx), from);
        return dist <= range * range ? dist : -1;
    }

    /// <summary>The nearest receiver a sitter of this seat can reach: its body, and its part; null: none. Bodies driven by someone else are passed over.</summary>
    public static Construct? Find(ConstructWorld world, Construct c, int seat, byte player, byte owner, out int part)
    {
        part = -1;
        var range = SenderOf(c, seat, out var channel);
        if (range <= 0)
            return null;
        var at = PartAt(c, seat);
        Construct? best = null;
        var near = float.MaxValue;
        foreach (var d in world.Bodies)
        {
            if (d.Receiver < 0 || d == c || d.Dirty || d.Receiver >= d.PartCount || d.Driver != 255 && d.Driver != player || !Factions.Shares(d.Owner, owner) && d.Owner != 255)
                continue;
            var f = Hears(d, d.Receiver, channel, range, at);
            if (f >= 0 && f < near)
            {
                near = f;
                best = d;
                part = d.Receiver;
            }
        }
        return best;
    }

    /// <summary>The drone this player flies, if any: from who drives it, so a client works it out from what it heard.</summary>
    public static Construct? Piloted(ConstructWorld world, byte player)
    {
        foreach (var d in world.Bodies)
            if (d.Receiver >= 0 && d.Driver == player && d.Receiver < d.PartCount)
                return d;
        return null;
    }

    /// <summary>A share of the range left, 0 to 1 (0: out of range, or the link or the receiver is down). Either side.</summary>
    public static float Signal(Construct seatBody, int seat, Construct d)
    {
        if (d.Receiver < 0 || d.Receiver >= d.PartCount)
            return 0;
        var range = SenderOf(seatBody, seat, out var channel);
        if (range <= 0)
            return 0;
        var f = Hears(d, d.Receiver, channel, range, PartAt(seatBody, seat));
        return f < 0 ? 0 : MathF.Max(0.01f, 1 - MathF.Sqrt(f) / range);
    }

    /// <summary>The look a sitter has in their seat, put on the receiver: forward from the seat is forward from it.</summary>
    public static void MapLook(Construct c, int seat, Construct d, int rx, float yaw, float pitch, out float toYaw, out float toPitch)
    {
        ref var s = ref c.Parts[seat];
        c.WorldFrame(s.Seg, out var sq, out _);
        ref var r = ref d.Parts[rx];
        d.WorldFrame(r.Seg, out var rq, out _);
        var look = Vector3.Transform(World.Forward(yaw, pitch), Quaternion.Conjugate(sq * Rot24.Quat(s.Rot)));
        var w = Vector3.Transform(look, rq * Rot24.Quat(r.Rot));
        toYaw = MathF.Atan2(w.X, -w.Z);
        toPitch = MathF.Asin(Math.Clamp(w.Y, -1, 1));
    }

    /// <summary>The look line of a Drone Receiver, a Remote Transmitter or a Camera's channel.</summary>
    public static string ChannelText(in CPart p, ConstructPartDef d) => $"channel {(int)ConstructConfig.Get(p, d, CfgKey.Channel, 1)}";

    /// <summary>What a sitter's seat says about its link, for the look line, or null.</summary>
    public static string? SeatProblem(ConstructWorld world, Construct c, int seat)
    {
        if (!Sends(c, seat))
            return null;
        return SenderOf(c, seat, out var channel) <= 0 ? "no power to the transmitter"
            : Find(world, c, seat, 255, c.Owner, out _) == null && Piloting(world, c, seat) == null ? $"no receiver free on channel {channel} in range" : null;
    }

    private static Construct? Piloting(ConstructWorld world, Construct c, int seat) =>
        c.Parts[seat].SitterPlus1 is var s and > 0 ? Piloted(world, (byte)(s - 1)) : null;

    // ---- Cameras ------------------------------------------------------------------------------------------------

    /// <summary>The nearest camera on a channel to a point that this owner may watch: its body and part, or null.</summary>
    public static Construct? FindCamera(ConstructWorld world, Vector3 at, int channel, byte owner, out int part)
    {
        part = -1;
        Construct? best = null;
        var near = float.MaxValue;
        foreach (var d in world.Bodies)
        {
            if (d.CameraParts.Length == 0 || !Factions.Shares(d.Owner, owner) && d.Owner != 255 && owner != 255)
                continue;
            for (var k = 0; k < d.CameraParts.Length; k++)
            {
                int i = d.CameraParts[k];
                if (i >= d.PartCount)
                    continue;
                ref var p = ref d.Parts[i];
                if (p.Broken || !p.D.IsCamera || (int)ConstructConfig.Get(p, p.D, CfgKey.Channel, 1) != channel)
                    continue;
                var f = Vector3.DistanceSquared(PartAt(d, i), at);
                if (f < near)
                {
                    near = f;
                    best = d;
                    part = i;
                }
            }
        }
        return best;
    }

    /// <summary>Where a camera is drawn this frame: its lens and its turn (looking along -Z, +Y up).</summary>
    public static void CameraPose(Construct c, int part, float alpha, bool blendTicks, out Vector3 at, out Quaternion rot)
    {
        ref var p = ref c.Parts[part];
        at = ConstructRender.DrawnPoint(c, part, p.D.CameraAt, alpha, blendTicks);
        var body = blendTicks ? Quaternion.Slerp(c.PrevRot, c.Rot, alpha) : c.Rot;
        var sq = Quaternion.Identity;
        if (p.Seg > 0 && p.Seg <= c.JointCount)
            ConstructRender.SegPoseBlended(c, p.Seg, blendTicks ? alpha : 1f, out sq, out _);
        rot = body * sq * Rot24.Quat(p.Rot);
    }

    /// <summary>A screen's card: the camera's picture, or why not. Being filled at all means someone is near and looking, and that is what asks the camera to work.</summary>
    public static void FillScreen(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var channel = (int)ConstructConfig.Get(p, d, CfgKey.Channel, 1);
        if (!ConstructPower.On(c, part))
        {
            card.State = PanelState.Fault;
            card.Add(0, "No power");
            return;
        }
        if (FindCamera(world, PartAt(c, part), channel, c.Owner, out var cam) is not { } body)
        {
            card.State = PanelState.Wants;
            card.Add(0, "No signal");
            card.Add(1, "Channel ").Add(1, channel);
            return;
        }
        card.State = PanelState.Ok;
        card.Picture = CameraFeeds.Request(body, cam);
        if (card.Picture == 0)
            card.Add(0, "Waiting");
    }

    // ---- Part files -----------------------------------------------------------------------------------------------

    /// <summary>A part file's <c>"remote"</c> and <c>"camera"</c> keys.</summary>
    public static void Parse(ConstructPartDef d, JsonElement root, string file)
    {
        if (ConstructParts.Opt(root, "remote") is { } r)
        {
            ConstructParts.Known(r, ["range", "receiver"], $"{file}, remote");
            d.RemoteReceiver = ConstructParts.Opt(r, "receiver") is { ValueKind: JsonValueKind.True };
            d.RemoteRange = ConstructParts.Opt(r, "range") is { } g ? ConstructParts.Num(g, file, "remote.range") : 0;
            ConstructParts.Range(d.RemoteRange, 0, 100000, file, "remote.range");
            if (d.RemoteReceiver == d.RemoteRange > 0)
                throw new ConstructPartException($"{file}: \"remote\" is a sender (\"range\") or a \"receiver\"");
            if (d.Role != PartRole.Seat && d.Logic != CLogic.RemoteLink)
                throw new ConstructPartException($"{file}: \"remote\" goes on a seat or a Remote Transmitter");
        }
        if (ConstructParts.Opt(root, "camera") is { } cam)
        {
            ConstructParts.Known(cam, ["at", "fov"], $"{file}, camera");
            d.CameraAt = ConstructParts.Vec(ConstructParts.Need(cam, "at", $"{file}, camera"), file, "camera.at");
            d.CameraFov = ConstructParts.Opt(cam, "fov") is { } f ? ConstructParts.Num(f, file, "camera.fov") : 70;
            ConstructParts.Range(d.CameraFov, 20, 120, file, "camera.fov");
            d.IsCamera = true;
        }
    }
}
