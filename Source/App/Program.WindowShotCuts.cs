using System.Globalization;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window tool's verbs for cut parts (docs/sandbox/design/laser-cutter.md, phase 2), each placed by a spot on the part's flat sheet
/// (U cm along it, V cm across, as a cut key places its cuts):
/// <c>ray PART[#n] U,V [M]</c> a bullet's construct ray at that spot, square to the sheet from M metres out (default 2), printing what it hits;
/// <c>walk PART[#n] U,V M TICKS</c> the player, noclip off, on the ground M metres out from that spot, holding Forward towards the sheet
/// for that many simulation ticks of real time, whatever the frame rate (a client waits until the host has put it there: <c>put PART[#n] U,V M</c> on the host), then <c>side PART[#n] U,V</c>,
/// which prints which side of the sheet the player stands on.
/// </summary>
public static unsafe partial class Program
{
    private static long _wsHoldUntil; // (walk: the Stopwatch stamp the held key comes up at)
    private static GameAction _wsHold;
    private static int _wsPutWait; // (a client's walk waiting for the host's put)

    private static bool WsCutVerb(Game g, Player me, string verb, string arg)
    {
        if (WsCutterVerb(g, verb, arg))
            return true; // (the Laser Cutter: Program.WindowShotCutter.cs)
        if (verb is not ("ray" or "walk" or "side" or "put"))
            return false;
        var words = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // The part is every word before the last one of numbers with a comma (a name may have spaces in it, and a cut key has commas of its own).
        var at = Array.FindLastIndex(words, w => w.Contains(',') && w.Split(',').All(x => float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out _)));
        if (at < 1)
        {
            WsFail($"{verb} PART[#n] U,V ...");
            return true;
        }
        var (c, part) = WsFind(g, string.Join(' ', words[..at]));
        var uv = words[at].Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        var rest = words[(at + 1)..].Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        if (c == null || uv.Length != 2 || !HullLoft.CutSpot(c.DefOf(part).Name, uv[0], uv[1], out var local, out var localN))
        {
            WsFail($"{verb}: no cut part \"{string.Join(' ', words[..at])}\" on the spawned blueprint, or that spot is off its sheet");
            return true;
        }
        Freeform.PartWorld(c, part, out var pos, out var rot);
        var spot = pos + Vector3.Transform(local, rot);
        var n = Vector3.Normalize(Vector3.Transform(localN, rot));
        switch (verb)
        {
            case "ray":
            {
                var m = rest.Length > 0 ? rest[0] : 2f;
                var o = spot + n * m;
                if (g.Constructs.Raycast(o, -n, m * 2, out var hit))
                    WsSay($"ray at {uv[0]:0},{uv[1]:0}: hits {hit.Body.DefOf(hit.Part).Name} (part {hit.Part}) at {hit.Distance:0.00} m of {m * 2:0.#} (the sheet is at {m:0.00})");
                else
                    WsSay($"ray at {uv[0]:0},{uv[1]:0}: passes, no part within {m * 2:0.#} m");
                _wsWait = 2;
                return true;
            }
            case "walk":
            {
                if (rest.Length < 2)
                {
                    WsFail("walk PART[#n] U,V M TICKS");
                    return true;
                }
                var flat = Vector3.Normalize(new Vector3(n.X, 0, n.Z) + new Vector3(1e-5f, 0, 0));
                var stand = spot + flat * rest[0];
                me.NoclipWanted = false;
                g.NoclipShot = false;
                if (g.IsServer)
                    me.Teleport(new Vector3(stand.X, World.Map.GroundHeight(stand.X, stand.Z) + 0.05f, stand.Z));
                else if (Vector2.Distance(new Vector2(me.Position.X, me.Position.Z), new Vector2(stand.X, stand.Z)) > 1f)
                {
                    // A client can't put its own player anywhere: it waits (up to 30 s) for the host's "put" to the same spot.
                    if (++_wsPutWait > 180)
                    {
                        WsFail("walk: the host never put this player there (the host's script: put PART U,V M)");
                        return true;
                    }
                    _wsQueue.EnqueueFront($"{verb} {arg}");
                    WsWaitTicks(10);
                    return true;
                }
                _wsPutWait = 0;
                g.LookYaw = MathF.Atan2(-flat.X, flat.Z);
                g.LookPitch = 0;
                WsSay($"walk: from {stand:0.00}, {rest[0]:0.0} m out, towards the sheet for {rest[1]:0} ticks");
                _wsHold = GameAction.MoveForward;
                _wsHoldUntil = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(rest[1] / World.TickRate * System.Diagnostics.Stopwatch.Frequency);
                WsWaitTicks((int)rest[1] + 10);
                _wsQueue.EnqueueFront("side " + string.Join(' ', words[..(at + 1)]));
                return true;
            }
            case "put": // host: every other player on the ground M metres out from the spot (for a client's walk)
            {
                var flat = Vector3.Normalize(new Vector3(n.X, 0, n.Z) + new Vector3(1e-5f, 0, 0));
                var stand = spot + flat * (rest.Length > 0 ? rest[0] : 3f);
                var count = 0;
                foreach (var other in g.Players.Values)
                    if (other != me)
                    {
                        other.Teleport(new Vector3(stand.X, World.Map.GroundHeight(stand.X, stand.Z) + 0.05f, stand.Z));
                        count++;
                    }
                if (count == 0 && ++_wsPutWait < 360) // (no one has joined yet: try again, up to a minute)
                {
                    _wsQueue.EnqueueFront($"{verb} {arg}");
                    WsWaitTicks(10);
                    return true;
                }
                _wsPutWait = 0;
                WsSay($"put: {count} player(s) to {stand:0.00}");
                _wsWait = 5;
                return true;
            }
            default: // side
            {
                var flat = Vector3.Normalize(new Vector3(n.X, 0, n.Z) + new Vector3(1e-5f, 0, 0));
                var d = Vector3.Dot(me.Position - spot, flat);
                WsSay($"side: the player is {MathF.Abs(d):0.00} m {(d >= 0 ? "in front of" : "behind")} the sheet at {uv[0]:0},{uv[1]:0} (feet {me.Position:0.00})");
                return true;
            }
        }
    }

    /// <summary>PollActions: the walk verb's held key.</summary>
    private static void WindowShotHold(bool[] down)
    {
        if (_wsHoldUntil == 0)
            return;
        if (System.Diagnostics.Stopwatch.GetTimestamp() >= _wsHoldUntil)
        {
            _wsHoldUntil = 0;
            return;
        }
        down[(int)_wsHold] = true;
    }
}
