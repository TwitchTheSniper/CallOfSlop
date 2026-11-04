using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The track tool on this PC (ConstructTracks.cs; docs/sandbox/CONSTRUCTS.md, "Tracks"): pick wheels one after another round a
/// loop, and a green or red band shows the track they would make, with the reason when it can't be. The server checks it again
/// by the same rule (<see cref="ConstructTracks.Check"/>). Picked wheels are checked against the construct every frame: a part
/// taken off under the tool shifts the indices, so a pick that no longer names the same part ends the stroke.
/// </summary>
public static partial class Program
{
    private static ushort _tkBody;
    private static readonly ushort[] _tkParts = new ushort[Construct.MaxTrackWheels + 1];
    private static readonly ushort[] _tkDefs = new ushort[Construct.MaxTrackWheels + 1];
    private static readonly Vector3[] _tkPos = new Vector3[Construct.MaxTrackWheels + 1];
    private static int _tkCount;
    private static long _tkKey = -1;
    private static string _tkLine = "";

    /// <summary>Every frame in Sandbox: the tool put away, the stroke and its ghost go.</summary>
    private static void SandboxTrackTick(bool toolOut)
    {
        if (!toolOut && (_tkCount > 0 || ConstructRender.TrackGhostCount > 0))
            TrackReset();
    }

    private static void TrackReset()
    {
        _tkCount = 0;
        _tkBody = 0;
        _tkKey = -1;
        ConstructRender.TrackGhostCount = 0;
        ConstructRender.TrackGhostBody = null;
    }

    private static bool TrackStrokeLive(Construct? c)
    {
        if (c == null)
            return false;
        for (var k = 0; k < _tkCount; k++)
            if (_tkParts[k] >= c.PartCount || c.Parts[_tkParts[k]].Def != _tkDefs[k] || c.Parts[_tkParts[k]].Pos != _tkPos[k])
                return false;
        return true;
    }

    /// <summary>The tool's frame with the mouse captured (from SandboxInput, with the crosshair's hit).</summary>
    private static void SandboxTrackInput(Game g, Player me, bool aimed, in ConstructHit hit)
    {
        if (_tkCount > 0 && !TrackStrokeLive(g.Constructs.Get(_tkBody)))
        {
            TrackReset();
            g.SandboxSay("the wheels picked changed: start the track again");
        }
        var inReach = aimed && hit.Distance <= ConstructBuild.Reach + 2f && hit.Body is { } && hit.Part >= 0 && hit.Part < hit.Body.PartCount;
        var c = inReach ? hit.Body : null;
        var part = -1;
        var wheel = false;
        var name = "";
        if (c != null)
        {
            part = hit.Part;
            var d = c.DefOf(part);
            wheel = d.Role == PartRole.Wheel;
            name = d.Name;
        }
        var body = _tkCount > 0 ? g.Constructs.Get(_tkBody) : c;
        var existing = c == null ? -1 : hit.TrackPlus1 > 0 && hit.TrackPlus1 <= c.TrackCount ? hit.TrackPlus1 - 1 : wheel ? c.TrackHolding(part) : -1;
        var fire = Pressed(GameAction.Fire) && !_swallowClick;
        var aim = Pressed(GameAction.Aim);
        var finish = Pressed(GameAction.Reload);
        var drop = Pressed(GameAction.SandboxDrop);

        // What the picks would make with the wheel under the crosshair.
        var picked = -1;
        for (var k = 0; k < _tkCount; k++)
            if (c == body && _tkParts[k] == part)
                picked = k;
        var n = _tkCount;
        var onOther = _tkCount > 0 && c != null && c != body;
        if (wheel && !onOther && picked < 0 && n < _tkParts.Length)
            _tkParts[n++] = (ushort)part;
        string? why = null;
        if (onOther)
            why = "all of a track's wheels are on one construct";
        else if (body != null && n > 0)
        {
            why = ConstructTracks.Check(body, _tkParts.AsSpan(0, n));
            if (n == 1 && why == "a track needs two wheels or more")
                why = null;
        }
        else if (c != null && !wheel && _tkCount > 0)
            why = $"a {name} isn't a wheel: a track runs on wheels";
        ConstructRender.TrackGhostBody = n > 0 ? body : null;
        ConstructRender.TrackGhostCount = n;
        ConstructRender.TrackGhostOk = why == null;
        if (n > 0)
            Array.Copy(_tkParts, ConstructRender.TrackGhostParts, n);

        // The line.
        var key = ((long)n << 40) ^ ((long)(part + 1) << 20) ^ (long)(existing + 1) << 10 ^ ((long)(why?.GetHashCode() ?? 0) << 44) ^ ((long)SandboxKeys.Stamp << 8) ^ (long)(c?.Id ?? 0) ^ (existing >= 0 && c != null ? ((long)MathF.Ceiling(c.Tracks[existing].Health) << 25) + (c.Tracks[existing].Live ? 1L << 24 : 0) : 0);
        if (key != _tkKey)
        {
            _tkKey = key;
            _tkLine = TrackLine(body, c, part, wheel, name, existing, n, picked, why);
        }
        g.SandboxLookText = _tkLine;

        // The keys.
        if (fire && _tkCount == 0 && existing >= 0 && !c!.Tracks[existing].Live)
        {
            g.SandboxSendTrackEdit(c.Id, TrackOp.Repair, existing);
            return;
        }
        if (fire && _tkCount == 0 && existing >= 0)
        {
            g.SandboxSay("that wheel is on a track already");
            return;
        }
        if (fire && picked == 0 && _tkCount >= 2)
        {
            TrackFinish(g);
            return;
        }
        if (fire && picked >= 0)
        {
            for (var k = picked; k + 1 < _tkCount; k++)
            {
                _tkParts[k] = _tkParts[k + 1];
                _tkDefs[k] = _tkDefs[k + 1];
                _tkPos[k] = _tkPos[k + 1];
            }
            _tkCount--;
            if (_tkCount == 0)
                TrackReset();
            return;
        }
        if (fire && why != null)
        {
            g.SandboxSay(why);
            return;
        }
        if (fire && wheel && !onOther && _tkCount < Construct.MaxTrackWheels)
        {
            _tkBody = body!.Id;
            _tkDefs[_tkCount] = body.Parts[part].Def;
            _tkPos[_tkCount] = body.Parts[part].Pos;
            _tkParts[_tkCount++] = (ushort)part;
            return;
        }
        if (finish && _tkCount >= 2)
        {
            if (ConstructTracks.Check(body!, _tkParts.AsSpan(0, _tkCount)) is { } bad)
                g.SandboxSay(bad);
            else
                TrackFinish(g);
            return;
        }
        if (finish && _tkCount == 0 && existing >= 0)
        {
            Ui.OpenSandboxTrack(c!, existing);
            SetMouseCaptured(false);
            return;
        }
        if (aim && _tkCount > 0)
        {
            _tkCount--;
            if (_tkCount == 0)
                TrackReset();
            return;
        }
        if (drop && _tkCount > 0)
        {
            TrackReset();
            return;
        }
        if (drop && existing >= 0)
            g.SandboxSendTrackEdit(c!.Id, TrackOp.Remove, existing);
    }

    private static void TrackFinish(Game g)
    {
        g.SandboxSendTrackMake(_tkBody, _tkParts.AsSpan(0, _tkCount));
        TrackReset();
    }

    private static string TrackLine(Construct? body, Construct? c, int part, bool wheel, string name, int existing, int n, int picked, string? why)
    {
        if (_tkCount == 0 && existing >= 0 && c != null)
        {
            ref var t = ref c.Tracks[existing];
            return SandboxKeys.Line("Track", SandboxKeys.Join($"{t.Count} wheels", $"{t.Width:0.##} m wide", t.Live ? $"health {100 * t.Health / MathF.Max(1, t.MaxHealth):0}%" : t.Health <= 0 ? "shot off" : "off its wheels",
                    t.Live || _game.SandboxHands.InfiniteParts || ConstructTracks.Whole(c, existing) ? null : $"{ConstructTracks.MendCost(c, existing)} {ConstructTracks.Material} to mend"), null,
                t.Live ? $"{SandboxKeys.Do(SandboxKeys.Reload, "settings")}, {SandboxKeys.Do(SandboxKeys.Drop, "take it off")}" : $"{SandboxKeys.Do(SandboxKeys.Fire, "put it back on")}, {SandboxKeys.Do(SandboxKeys.Drop, "take it off")}");
        }
        if (_tkCount == 0)
            return wheel ? SandboxKeys.Line(name, null, why, why == null ? SandboxKeys.Do(SandboxKeys.Fire, "start a track here") : null)
                : SandboxKeys.Line("Track tool", null, null, $"{SandboxKeys.Fire} wheels in turn round the loop");
        var length = 0f;
        if (body != null && n >= 2)
            length = ConstructRender.TrackGhostLength(body);
        string? cost = null;
        if (body != null && _tkCount >= 2 && !_game.SandboxHands.InfiniteParts)
        {
            var parts = _tkParts.AsSpan(0, _tkCount);
            var links = ConstructTracks.LinkCount(body, parts, ConstructTracks.DefaultLink(body, parts));
            cost = $"{links} links, {ConstructTracks.Cost(links)} {ConstructTracks.Material}";
        }
        var state = SandboxKeys.Join($"{_tkCount} wheel{(_tkCount == 1 ? "" : "s")}", length > 0 ? $"{length:0.0} m round" : null, cost);
        var keys = picked == 0 && _tkCount >= 2 ? SandboxKeys.Do(SandboxKeys.Fire, "close the loop")
            : picked > 0 ? SandboxKeys.Do(SandboxKeys.Fire, "leave this wheel out")
            : why == null && wheel ? SandboxKeys.Do(SandboxKeys.Fire, "add this wheel") : null;
        var tail = _tkCount >= 2 ? $"{SandboxKeys.Do(SandboxKeys.Reload, "finish")}, {SandboxKeys.Do(SandboxKeys.Aim, "back")}" : SandboxKeys.Do(SandboxKeys.Aim, "back");
        return SandboxKeys.Line("Track", state, why, keys == null ? tail : $"{keys}, {tail}");
    }

    /// <summary>The track settings panel over the match; it closes (and the mouse goes back to aiming) when it should.</summary>
    private static void DrawSandboxTrackInMatch()
    {
        if (!Ui.SandboxTrackOpen)
            return;
        var me = _game.Local;
        var gone = _session == null || _mouseCaptured || !_game.SandboxOn || me is not { Alive: true } || _game.Phase != MatchPhase.Playing;
        if (!gone)
        {
            ref var h = ref _game.SandboxHands.Hand(me!.Id);
            gone = h.Seated || h.Tool is not (ConstructTool.Tracks or ConstructTool.Configure);
        }
        if (!gone && Ui.DrawSandboxTrack(_game))
            return;
        Ui.CloseSandboxTrack();
        if (!MenusOpen)
            SetMouseCaptured(true);
        _swallowClick = true;
    }
}
