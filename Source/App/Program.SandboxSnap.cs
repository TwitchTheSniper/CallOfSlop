using System.Numerics;
using Brutal.GlfwApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Snappy building on this PC (board item dev-sandbox-snappy-build-sb01, decision 24; docs/sandbox/CONSTRUCTS.md, "Snappy
/// building"): the connection-point markers and the screen magnet (Constructs/SnapBuild.cs, Render/SnapRender.cs), K to
/// switch which of the held part's points mates, the snap feel (an ease, a flash and a click on a new socket, a thunk when
/// the part lands), hold-to-paint in creative, a middle-button tap to pick the aimed part's kind, Ctrl+Z to undo and the
/// recent-parts wheel (L). The server still decides everything (ConstructEditsSnap.cs). Settings.SnapBuild turns it off.
/// </summary>
public static unsafe partial class Program
{
    private static int _snapOwn = -1; // the own socket the player switched to (-1: the structural mount first)
    private static long _snapKey = long.MinValue; // the selected ghost's identity (construct, part, socket): a change is a snap
    private static readonly SnapStroke _snapStroke = new(); // the held press's paint rules (SnapStroke.cs)
    private static long _snapLineKey = long.MinValue;
    private static Vector3 _snapPrevPos;
    private static int _snapPrevBody = -1, _snapPrevSeg = -1;
    private static double _snapLast;
    private static int _snapExpectBody = -1, _snapExpectCount;
    private static double _snapExpectUntil;
    private static Vector3 _snapExpectAt;
    private static bool _snapZPrev;
    private static bool _snapTap; // the tool wheel key was tapped (middle button): pick the aimed part
    private static readonly SandboxRecentWheel _snapWheel = new();

    /// <summary>The tool wheel saw a quick tap of its key (SandboxToolWheel.Tapped): the aimed part's kind is picked.</summary>
    private static void SnapTapped() => _snapTap = true;

    /// <summary>Every frame at the start of the Sandbox keys: last frame's markers off, the ease running out, the thunk when a part landed.</summary>
    private static void SandboxSnapFrame(Game g, double now)
    {
        SnapRender.Clear();
        var dt = (float)Math.Clamp(now - _snapLast, 0, 0.1);
        _snapLast = now;
        if (!Down(GameAction.Fire))
            _snapStroke.Reset();
        ConstructRender.GhostEase *= MathF.Exp(-dt * 26f);
        if (ConstructRender.GhostEase.LengthSquared() < 1e-6f)
            ConstructRender.GhostEase = Vector3.Zero;
        if (_snapExpectBody >= 0)
        {
            if (g.Constructs.Get((ushort)_snapExpectBody) is { } c && c.PartCount >= _snapExpectCount)
            {
                g.Audio.Effect3DPitched(Sfx.LandThud, _snapExpectAt, 0.5f, 1.7f); // the heavier thunk: the part is on
                _snapExpectBody = -1;
            }
            else if (now > _snapExpectUntil)
                _snapExpectBody = -1;
        }
    }

    /// <summary>The ghosts for the held part, by snap building's rules (setting on), else the old way.</summary>
    private static int SandboxSnapGhosts(Construct c, ConstructPartDef held, Vector3 near)
    {
        if (!_settings.SnapBuild)
            return ConstructBuild.Ghosts(c, held, ConstructRender.Ghosts, near, 2.5f);
        SnapRender.Active = true;
        SnapRender.Body = c;
        SnapRender.Held = held;
        return SnapBuild.Ghosts(c, held, SnapRender.Cache, ConstructRender.Ghosts, near, SnapBuild.SinglePurpose(held) ? 4f : 2.5f);
    }

    /// <summary>A single-purpose part magnet-snaps to the functional socket nearest the crosshair on screen; any other part keeps the crosshair's own pick (<paramref name="best"/>).</summary>
    private static int SandboxSnapSelect(Construct c, ConstructPartDef held, ref int n, int best)
    {
        if (!_settings.SnapBuild || !SnapBuild.SinglePurpose(held))
            return best;
        var fb = _window.FramebufferSize;
        var size = new Vector2(fb.X, fb.Y);
        var k = SnapBuild.Magnet(c, SnapRender.Cache, Gfx.ViewProj, size, SnapBuild.SnapPixels * size.Y / 1080f);
        if (k < 0)
            return -1;
        var part = SnapRender.Cache.Part[k];
        var socket = SnapRender.Cache.Socket[k];
        for (var i = 0; i < n; i++)
            if (ConstructRender.Ghosts[i].Part == part && ConstructRender.Ghosts[i].Socket == socket)
                return i;
        if (n >= ConstructRender.Ghosts.Length || !SnapBuild.Fit(c, held, part, socket, _snapOwn, out var g))
            return -1;
        ConstructRender.Ghosts[n++] = g;
        ConstructRender.SetGhosts(c, n);
        return n - 1;
    }

    /// <summary>After smart rotation's guess: only the own sockets snap building allows, the preferred one first.</summary>
    private static void SandboxSnapRank(ConstructPartDef held, in ConstructGhost gh)
    {
        if (!_settings.SnapBuild)
            return;
        if (_sbRankN > 0)
        {
            _sbRankN = SnapBuild.FilterRank(held, _sbRank, _sbRankRot, _sbRankN, _snapOwn);
            if (_sbRankN > 0 && _sbVariant >= 0 && _sbRank.AsSpan(0, _sbRankN).IndexOf(_sbVariant) < 0)
            {
                _sbRankPos = 0;
                _sbVariant = _sbRank[0];
            }
        }
        else if (_sbVariant < 0)
            _sbVariant = gh.Variant;
    }

    /// <summary>The switch key: the next own connection point that fits mates (K).</summary>
    private static bool SandboxSnapSwitch(Game g, Construct c, ConstructPartDef held, in ConstructGhost gh)
    {
        if (!_settings.SnapBuild || !Pressed(GameAction.SnapSwitch))
            return false;
        var nv = SnapBuild.NextOwn(c, held, gh.Part, gh.Socket, _sbVariant >= 0 ? _sbVariant : gh.Variant);
        if (nv < 0)
        {
            g.SandboxSay($"the {held.Name} has no other way on there");
            return true;
        }
        _snapOwn = nv / 4;
        _sbVariant = nv;
        _sbRankN = SnapBuild.FilterRank(held, _sbRank, _sbRankRot, _sbRankN, _snapOwn);
        _sbRankPos = _sbRank.AsSpan(0, _sbRankN).IndexOf(nv);
        g.SandboxSay($"{held.Name}: its {held.Sockets[_snapOwn].Name} ({SnapBuild.Label(SnapBuild.OwnType(held, _snapOwn))}) mates");
        return true;
    }

    /// <summary>The snap feel and the connections to mark, once the ghost is final: a click, a flash and an ease on a new socket; the lineups of the shown part.</summary>
    private static void SandboxSnapShown(Game g, Construct c, ConstructPartDef held, in ConstructGhost gh, double now)
    {
        if (!_settings.SnapBuild)
            return;
        var key = ((long)c.Id << 40) | ((long)gh.Part << 24) | (long)gh.Socket << 8 | (uint)held.Id >> 8;
        if (key != _snapKey)
        {
            _snapKey = key;
            _snapOwn = -1;
            SnapRender.FlashAt = now;
            g.Audio.Effect(Sfx.HitTick, 0.16f); // the soft click
            if (_snapPrevBody == c.Id && _snapPrevSeg == gh.Seg)
            {
                var d = _snapPrevPos - gh.Pos;
                if (d.LengthSquared() < 2.25f)
                    ConstructRender.GhostEase = d;
            }
        }
        _snapPrevPos = gh.Pos;
        _snapPrevBody = c.Id;
        _snapPrevSeg = gh.Seg;
        var lkey = key ^ ((long)gh.Variant << 56) ^ (long)gh.Rot << 52 ^ c.PartCount;
        if (lkey != _snapLineKey)
        {
            _snapLineKey = lkey;
            SnapRender.LineCount = ConstructRender.GhostValid ? SnapBuild.Lineups(c, held, gh, SnapRender.Lines) : 0;
        }
    }

    /// <summary>Paint: a held press places the part on further sockets (creative; quick building) by SnapStroke's rules. A press only starts the stroke.</summary>
    private static bool SandboxSnapPaint(Game g, in SandboxHand h, Construct c, in ConstructGhost gh, bool pressed)
    {
        if (!_settings.SnapBuild || !g.SandboxInfiniteParts || h.Held == ConstructParts.None || _swallowClick)
            return false;
        if (pressed)
            _snapStroke.Press(c.Id, c.PartCount, _snapLast);
        if (pressed || !_settings.HoldToPaint || !ConstructRender.GhostValid || !Down(GameAction.Fire))
            return false;
        return _snapStroke.Want(_snapLast, c.Id, c.PartCount, gh.Part, SnapPaintKey(c, in gh), SnapWorldAt(c, in gh));
    }

    private static long SnapPaintKey(Construct c, in ConstructGhost gh) => ((long)c.Id << 24) | ((long)gh.Part << 8) | (uint)gh.Socket;

    private static Vector3 SnapWorldAt(Construct c, in ConstructGhost gh)
    {
        c.SegPose(c.Parts[gh.Part].Seg, out var wq, out var wt);
        return c.ToWorld(wt + Vector3.Transform(gh.Pos, wq));
    }

    /// <summary>The part was sent: remembered for the paint, the recent wheel and the thunk.</summary>
    private static void SandboxSnapSent(Construct? c, ConstructPartDef held, in ConstructGhost gh, Vector3 worldAt)
    {
        _snapWheel.Placed(held.Id);
        if (c == null)
            return;
        _snapStroke.Sent(c.Id, c.PartCount + 1, _snapLast, SnapPaintKey(c, in gh), worldAt);
        _snapExpectBody = c.Id;
        _snapExpectCount = c.PartCount + 1;
        _snapExpectUntil = _snapLast + 1.0;
        _snapExpectAt = worldAt;
    }

    /// <summary>Quick building on or off with the setting (once per change), Ctrl+Z to undo, and the tap that picks the aimed part.</summary>
    private static void SandboxSnapGlobal(Game g, bool aimed, in ConstructHit hit)
    {
        var want = _settings.SnapBuild ? Game.SnapQuickOn : Game.SnapQuickOff;
        if (g.SnapQuickSent != want)
        {
            g.SnapQuickSent = want;
            g.SandboxSendSnap(want);
        }
        var ctrl = _window.GetKey(GlfwKey.LeftControl) == GlfwKeyAction.Press || _window.GetKey(GlfwKey.RightControl) == GlfwKeyAction.Press;
        var z = ctrl && _window.GetKey(GlfwKey.Z) == GlfwKeyAction.Press;
        if (z)
        {
            _down[(int)GameAction.Prone] = false; // (Ctrl+Z is not crouch and prone)
            _down[(int)GameAction.CrouchHold] = false;
        }
        if (z && !_snapZPrev && _settings.SnapBuild)
            g.SandboxSendSnap(Game.SnapUndo);
        _snapZPrev = z;
        if (_snapTap)
        {
            _snapTap = false;
            if (_settings.SnapBuild && aimed)
                g.SandboxSendSnap(Game.SnapPick, hit.Body.Id, (byte)hit.Part);
        }
    }

    /// <summary>The recent-parts wheel while its key is held (the view is frozen, as the tool wheel's). True while open.</summary>
    private static bool SnapWheelStep(Game g, double2 delta, bool blocked, float dt)
    {
        if (!_settings.SnapBuild)
        {
            if (_snapWheel.Menu.IsOpen)
                _snapWheel.Menu.Close();
            return false;
        }
        return _snapWheel.Step(g, Down(GameAction.RecentParts), new Vector2((float)delta.X, (float)delta.Y), blocked, dt);
    }
}
