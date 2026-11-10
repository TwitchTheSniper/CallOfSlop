using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Talking to the story's people (docs/story/STORY.md "Talking to people"; the host's side is Game/GameStoryChat.cs):
/// the crosshair on a person within <see cref="Game.StoryTalkReach"/> shows their name and "F: talk", and the use key
/// opens the talk panel (UiStoryTalk.cs) and frees the mouse for it, like the Zombies trader's shelf. Opening it asks
/// them to stop and turn to you; closing it (Esc, Goodbye, walking off, their death) says goodbye. A construct part
/// nearer than the person keeps the use key.
/// </summary>
public static partial class Program
{
    private static byte _talkTo = 255; // the person's body (its player id), 255: no panel
    private static byte _talkLookFor = 255;
    private static string _talkLookText = "";
    private static bool _talkRemote; // the panel is another player's talk, joined from afar (no reach to keep)
    private static int _inviteSeen, _joinOverSeen;
    private static double _inviteUntil;
    private static string _inviteText = "", _talkWithText = "";
    public const double TalkInviteShown = 12; // s the notice stays

    /// <summary>The join key, while another player's talk is on offer: opens the same panel from wherever the player is. True when it used the key.</summary>
    private static bool TalkJoinKey(Brutal.GlfwApi.GlfwKey key)
    {
        if (_inviteUntil <= 0 || _talkTo != 255 || key == Brutal.GlfwApi.GlfwKey.Unknown || !_settings.HasBind(GameAction.JoinTalk, Keybinds.Key(key)) || Ui.ChatOpen || Ui.WantsTextInput
            || _session is not { SandboxOn: true } g || MenusOpen || !g.Players.TryGetValue(g.TalkInviteBody, out var them) || !them.Alive || them.Team != Game.SbPeopleTeam)
            return false;
        _inviteUntil = 0;
        (_talkTo, _talkRemote) = (them.Id, true);
        _talkWithText = g.Players.TryGetValue(g.TalkInviteFrom, out var with) ? $"With {with.Name}, from afar." : "From afar.";
        g.StoryTalkAsk(them.Id, TalkAsk.Join);
        SetMouseCaptured(false);
        return true;
    }

    /// <summary>One frame of the Sandbox input: a person under the crosshair. True when the use key's press went to them.</summary>
    private static bool TalkFrame(Game g, Player me, Vector3 eye, Vector3 dir, bool aimed, float aimedAt)
    {
        if (_talkTo != 255)
            return false;
        Player? best = null;
        var bestT = Game.StoryTalkReach;
        foreach (var p in g.Players.Values)
        {
            if (p.Team != Game.SbPeopleTeam || !p.Alive || p == me)
                continue;
            if (RayPerson(eye, dir, p.RenderPosition) is var t and >= 0 && t < bestT)
                (best, bestT) = (p, t);
        }
        if (best == null || aimed && aimedAt < bestT)
            return false;
        if (_talkLookFor != best.Id || !_talkLookText.StartsWith(best.Name, StringComparison.Ordinal))
            (_talkLookFor, _talkLookText) = (best.Id, SandboxKeys.Line(best.Name, null, null, SandboxKeys.Do(SandboxKeys.Use, "talk")));
        g.SandboxLookText = _talkLookText;
        if (!Pressed(GameAction.Interact))
            return true; // (the look is theirs: nothing behind them takes the use key this frame)
        _talkTo = best.Id;
        g.StoryTalkAsk(best.Id, TalkAsk.Open);
        SetMouseCaptured(false);
        return true;
    }

    /// <summary>How far along the ray a standing person at <paramref name="feet"/> is hit (a 0.45 m upright cylinder, 1.9 m tall), or -1.</summary>
    private static float RayPerson(Vector3 eye, Vector3 dir, Vector3 feet)
    {
        var flat = new Vector2(dir.X, dir.Z);
        var f2 = flat.LengthSquared();
        var to = new Vector2(feet.X - eye.X, feet.Z - eye.Z);
        float t;
        if (f2 < 0.0025f)
            t = MathF.Abs(dir.Y) > 0 ? (feet.Y + 0.95f - eye.Y) / dir.Y : -1; // (looking straight up or down: at the middle of them)
        else
            t = Vector2.Dot(to, flat) / f2;
        if (t <= 0)
            return -1;
        var at = eye + dir * t;
        if (Vector2.DistanceSquared(new Vector2(at.X, at.Z), new Vector2(feet.X, feet.Z)) > 0.45f * 0.45f)
            return -1;
        return at.Y >= feet.Y - 0.1f && at.Y <= feet.Y + 1.95f ? t : -1;
    }

    /// <summary>Esc: closes the talk panel (not the match).</summary>
    private static bool TalkEsc()
    {
        if (_talkTo == 255)
            return false;
        TalkClose();
        return true;
    }

    private static void TalkClose()
    {
        if (_talkTo == 255)
            return;
        _game.StoryTalkAsk(_talkTo, TalkAsk.Bye);
        _talkTo = 255;
        _talkRemote = false;
        if (!Ui.PauseOpen && !MenusOpen)
            SetMouseCaptured(true);
        _swallowClick = true;
    }

    /// <summary>The frame's ImGui pass: the panel while its person is still there, alive and near.</summary>
    private static void DrawTalkInMatch(double now)
    {
        var g = _game;
        // Another player began a talk: a notice for a while, to join or ignore.
        if (g.TalkInviteSerial != _inviteSeen)
        {
            _inviteSeen = g.TalkInviteSerial;
            _inviteUntil = 0;
            if (_session != null && g.Players.TryGetValue(g.TalkInviteFrom, out var from) && g.Players.TryGetValue(g.TalkInviteBody, out var to))
            {
                _inviteUntil = now + TalkInviteShown;
                _inviteText = $"{from.Name} is talking to {to.Name}.";
            }
        }
        if (_inviteUntil > 0 && (now >= _inviteUntil || _session == null || !g.SandboxOn))
            _inviteUntil = 0;
        if (_inviteUntil > 0 && _talkTo == 255 && !MenusOpen)
            Ui.DrawTalkInvite(_inviteText, _settings.Bind(GameAction.JoinTalk));
        if (g.TalkJoinOverSerial != _joinOverSeen)
        {
            _joinOverSeen = g.TalkJoinOverSerial;
            if (_talkRemote)
                TalkClose(); // (the talk they joined is over)
        }
        if (_talkTo == 255)
            return;
        if (_session == null || !g.SandboxOn || g.Local is not { Alive: true } me || !g.Players.TryGetValue(_talkTo, out var them) || !them.Alive
            || them.Team != Game.SbPeopleTeam || !_talkRemote && Vector3.DistanceSquared(me.Position, them.Position) > Game.StoryTalkLeave * Game.StoryTalkLeave)
        {
            TalkClose();
            return;
        }
        if (Ui.DrawStoryTalk(g, them, g.SandboxHands.Hand(g.LocalId).Held, now, _talkRemote ? _talkWithText : null) is { } ask)
        {
            if (ask == TalkAsk.Bye)
                TalkClose();
            else
                g.StoryTalkAsk(_talkTo, ask);
        }
    }
}
