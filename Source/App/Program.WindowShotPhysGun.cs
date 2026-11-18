using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window tool's physics gun check (pg01): "swing RATE SECS [DIST]" (DIST: the hold distance, as the wheel sets it) turns the look at RATE degrees a second for SECS seconds
/// (as the mouse would), then holds it still for 1.5 s, and prints every few frames how far the held piece's grab point is
/// drawn from the crosshair's point (m) and at what angle off the look (degrees), on this peer. A host and a joined client
/// running the same swing should print the same lag (the client's piece is simulated by the server's spring:
/// GameSandboxPhysGunSim.cs). It ends with a summary line: the most and the mean lag while swinging, and the lag at rest.
/// </summary>
public static partial class Program
{
    private static long _wsSwingStart, _wsSwingLast;
    private static float _wsSwingRate, _wsSwingSecs, _wsSwingMax, _wsSwingSum, _wsSwingRest;
    private static int _wsSwingFrames, _wsSwingN, _wsWaitPeerFrames;

    private static bool WsHeldKnown(string action) => action.Split(' ', 2)[0].ToLowerInvariant() is "swing" or "waitpeer";

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
        if (verb != "swing")
            return false;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var bits = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (bits.Length < 2 || !float.TryParse(bits[0], ci, out _wsSwingRate) || !float.TryParse(bits[1], ci, out _wsSwingSecs))
        {
            WsFail("swing RATE SECS [DIST]");
            return true;
        }
        if (bits.Length > 2 && float.TryParse(bits[2], ci, out var dist))
            _pgDist = dist; // (as the wheel sets it)
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
        if (t < _wsSwingSecs)
            g.LookYaw += _wsSwingRate * MathF.PI / 180f * dt;
        _wsWait = Math.Max(_wsWait, 2);
        ref var h = ref g.SandboxHands.Hand(me.Id);
        if (h.GrabBody != 0 && g.Constructs.Get(h.GrabBody) is { } c)
        {
            var look = g.LookDir(g.LookYaw, g.LookPitch);
            var eye = me.RenderEye;
            var at = c.ToWorld(h.GrabLocal);
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
                WsSay(FormattableString.Invariant($"swing: t {t:0.00} s, lag {lag:0.000} m, {deg:0.0} deg off the look, sim {g.SandboxHeldSimulated}, dist {_pgDist:0.0}, mass {c.Mass:0} kg"));
        }
        if (t >= _wsSwingSecs + 1.5f)
        {
            WsSay(FormattableString.Invariant($"swing done: {(g.IsServer ? "host" : "client")}, swinging max {_wsSwingMax:0.000} m, mean {(_wsSwingN > 0 ? _wsSwingSum / _wsSwingN : 0):0.000} m over {_wsSwingN} frames; at rest {_wsSwingRest:0.000} m"));
            _wsSwingStart = 0;
            _wsWait = 0;
        }
    }
}
