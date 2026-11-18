using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window tool's physics gun check (pg01): "swing RATE SECS [DIST] [pitch]" (DIST: the hold distance, as the wheel sets it; pitch: the look turns up, not round: a joint held across its axis) turns the look at RATE degrees a second for SECS seconds
/// (as the mouse would), then holds it still for 1.5 s, and prints every few frames how far the held piece's grab point is
/// drawn from the crosshair's point (m) and at what angle off the look (degrees), on this peer. A host and a joined client
/// running the same swing should print the same lag (the client's piece is simulated by the server's spring:
/// GameSandboxPhysGunSim.cs). It ends with a summary line: the most and the mean lag while swinging, and the lag at rest.
/// </summary>
public static partial class Program
{
    private static long _wsSwingStart, _wsSwingLast;
    private static bool _wsSwingPitch;
    private static float _wsSwingRate, _wsSwingSecs, _wsSwingMax, _wsSwingSum, _wsSwingRest;
    private static int _wsSwingFrames, _wsSwingN, _wsWaitPeerFrames;

    private static bool WsHeldKnown(string action) => action.Split(' ', 2)[0].ToLowerInvariant() is "swing" or "waitpeer" or "beside" or "pitch" or "aimat";

    private static bool WsHeldVerb(Game g, string verb, string arg)
    {
        if (verb == "waitpeer")
        {
            // "waitpeer": the host's script waits (up to 120 s) until another player has joined
            if (g.Players.Count < 2 && _wsWaitPeerFrames++ < 120 * 60)
            {
                _wsQueue.EnqueueFront("waitpeer");
                _wsWait = 30;
            }
            else
                _wsWaitPeerFrames = 0;
            return true;
        }
        if (verb == "aimat" && g.Local is { } eyeOf)
        {
            // "aimat N": the look turned to construct N's centre of mass
            if (!ushort.TryParse(arg, out var id) || g.Constructs.Get(id) is not { } body)
            {
                WsFail($"aimat: no construct {arg}");
                return true;
            }
            WsLook(g, eyeOf.Eye, body.WorldCom);
            WsSay(FormattableString.Invariant($"aimat: construct {id} at {body.WorldCom:0.0}, {Vector3.Distance(eyeOf.Eye, body.WorldCom):0.0} m"));
            _wsWait = 3;
            return true;
        }
        if (verb == "pitch")
        {
            // "pitch DEG": the look's pitch alone (the yaw stays where it is)
            g.LookPitch = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture) * MathF.PI / 180f;
            _wsWait = 3;
            return true;
        }
        if (verb == "beside" && g.Local is { } self)
        {
            // "beside M": host, every other player set down M m to the host's right, facing as the host faces; a client
            // waits (up to 60 s) until it stands there, then takes the host's look
            var right = Vector3.Normalize(Vector3.Cross(g.LookDir(self.Yaw, 0), GravityFrames.UpAt(self.Position)));
            var m = float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bm) ? bm : 3f;
            if (g.IsServer)
            {
                foreach (var other in g.Players.Values)
                    if (other != self)
                    {
                        other.Teleport(self.Position + right * m);
                        WsSay(FormattableString.Invariant($"beside: player {other.Id} to {self.Position + right * m:0.0}, yaw {self.Yaw * 180 / MathF.PI:0.0}"));
                    }
                _wsWait = 30;
                return true;
            }
            var host = g.Players.Values.FirstOrDefault(q => q != self && q.Alive);
            if (host == null || MathF.Abs(Vector2.Distance(new Vector2(host.Position.X, host.Position.Z), new Vector2(self.Position.X, self.Position.Z)) - m) > 1f)
            {
                if (_wsWaitPeerFrames++ < 60 * 2)
                {
                    _wsQueue.EnqueueFront($"beside {arg}");
                    _wsWait = 30;
                    return true;
                }
                WsFail("beside: the host never set this player beside it");
                return true;
            }
            _wsWaitPeerFrames = 0;
            g.LookYaw = host.Yaw;
            WsSay(FormattableString.Invariant($"beside: {Vector3.Distance(host.Position, self.Position):0.0} m from the host, yaw {host.Yaw * 180 / MathF.PI:0.0}"));
            _wsWait = 3;
            return true;
        }
        if (verb != "swing")
            return false;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var bits = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (bits.Length < 2 || !float.TryParse(bits[0], ci, out _wsSwingRate) || !float.TryParse(bits[1], ci, out _wsSwingSecs))
        {
            WsFail("swing RATE SECS [DIST] [pitch]");
            return true;
        }
        if (bits.Length > 2 && float.TryParse(bits[2], ci, out var dist))
            _pgDist = dist; // (as the wheel sets it)
        _wsSwingPitch = bits.Length > 3 && bits[3].Equals("pitch", StringComparison.OrdinalIgnoreCase);
        _wsSwingStart = _wsSwingLast = Stopwatch.GetTimestamp();
        _wsSwingMax = _wsSwingSum = _wsSwingRest = 0;
        _wsSwingFrames = _wsSwingN = 0;
        _wsWait = Math.Max(_wsWait, 2);
        return true;
    }

    /// <summary>Once a frame: a running swing turns the look and reads the held piece's lag.</summary>
    private static void WsHeldFrame()
    {
        if (_wsSwingStart == 0 || _session is not { } g || g.Local is not { } me)
            return;
        var now = Stopwatch.GetTimestamp();
        var t = (float)Stopwatch.GetElapsedTime(_wsSwingStart, now).TotalSeconds;
        var dt = (float)Stopwatch.GetElapsedTime(_wsSwingLast, now).TotalSeconds;
        _wsSwingLast = now;
        if (t < _wsSwingSecs && _wsSwingPitch)
            g.LookPitch = Math.Clamp(g.LookPitch + _wsSwingRate * MathF.PI / 180f * dt, -1.5f, 1.5f);
        else if (t < _wsSwingSecs)
            g.LookYaw += _wsSwingRate * MathF.PI / 180f * dt;
        _wsWait = Math.Max(_wsWait, 2);
        ref var h = ref g.SandboxHands.Hand(me.Id);
        if (h.GrabBody != 0 && g.Constructs.Get(h.GrabBody) is { } c)
        {
            var look = g.LookDir(g.LookYaw, g.LookPitch);
            var eye = me.RenderEye;
            var at = c.ToWorld(h.GrabLocal);
            if (h.GrabSeg != 0 && h.GrabSeg <= c.JointCount)
            {
                c.WorldFrame(h.GrabSeg, out var sq, out var st); // (a held joint: the point is in its segment's frame)
                at = st + Vector3.Transform(h.GrabLocal, sq);
            }
            var lag = Vector3.Distance(at, eye + look * _pgDist);
            var off = at - eye;
            var deg = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(off), look), -1f, 1f)) * 180f / MathF.PI;
            if (t < _wsSwingSecs)
            {
                _wsSwingMax = MathF.Max(_wsSwingMax, lag);
                _wsSwingSum += lag;
                _wsSwingN++;
            }
            else
                _wsSwingRest = lag;
            if (_wsSwingFrames++ % 6 == 0)
                WsSay(FormattableString.Invariant($"swing: t {t:0.00} s, lag {lag:0.000} m, {deg:0.0} deg off the look, sim {g.SandboxHeldSimulated || g.SandboxJointSimulated}, seg {h.GrabSeg}, dist {_pgDist:0.0}, mass {c.Mass:0} kg"));
        }
        if (t >= _wsSwingSecs + 1.5f)
        {
            WsSay(FormattableString.Invariant($"swing done: {(g.IsServer ? "host" : "client")}, swinging max {_wsSwingMax:0.000} m, mean {(_wsSwingN > 0 ? _wsSwingSum / _wsSwingN : 0):0.000} m over {_wsSwingN} frames; at rest {_wsSwingRest:0.000} m"));
            _wsSwingStart = 0;
            _wsWait = 0;
        }
    }
}
