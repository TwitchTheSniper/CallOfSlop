using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window tool's talk actions (a quest played through, docs/story/BREAKOUT.md): "talk NAME" walks up to a story person
/// and opens the talk panel as the use key would; "say hello|news|bye|choice N|ask TEXT" presses one of its buttons ("ask"
/// takes the first topic whose label starts with TEXT, any case); "heard" prints what the panel shows: their last lines,
/// the storylet's choices and the topics on offer. Everything goes through the panel's own calls.
/// </summary>
public static partial class Program
{
    private static int _wsHeardFrom;
    private static string _wsTalkName = "";
    private static long _wsSecsUntil, _wsKeysUntil;
    private static readonly List<GameAction> _wsKeys = [];

    /// <summary>PollActions: the "hold" keys, down until their time is up.</summary>
    private static void WsTalkHold(bool[] down)
    {
        if (_wsKeys.Count == 0)
            return;
        if (System.Diagnostics.Stopwatch.GetTimestamp() >= _wsKeysUntil)
        {
            _wsKeys.Clear();
            return;
        }
        foreach (var k in _wsKeys)
            down[(int)k] = true;
    }

    /// <summary>Once a frame: a running "secs" holds the script until its time is up (real seconds, not frames).</summary>
    private static void WsTalkFrame()
    {
        if (_wsSecsUntil == 0)
            return;
        if (System.Diagnostics.Stopwatch.GetTimestamp() >= _wsSecsUntil)
        {
            _wsSecsUntil = 0;
            _wsWait = 0;
        }
        else
            _wsWait = Math.Max(_wsWait, 2);
    }

    private static bool WsTalkKnown(string action) => action.Split(' ', 2)[0].ToLowerInvariant() is "talk" or "say" or "heard" or "standing" or "secs" or "hold" or "where" or "seat";

    private static bool WsTalkVerb(Game g, Player me, string verb, string arg)
    {
        switch (verb)
        {
            case "talk":
            {
                var who = g.Players.Values.FirstOrDefault(q => q.Alive && q.Team == Game.SbPeopleTeam && q.Name.Equals(arg, StringComparison.OrdinalIgnoreCase));
                if (who == null)
                {
                    WsFail($"talk NAME: nobody alive called \"{arg}\"");
                    return true;
                }
                var off = who.Position - me.Position;
                off.Y = 0;
                var step = off.LengthSquared() > 1e-4f ? Vector3.Normalize(off) * 1.6f : Vector3.UnitX * 1.6f;
                me.Teleport(who.Position - step);
                WsLook(g, me.Eye, who.RenderPosition + Vector3.UnitY * 1.5f);
                g.LookFrame = me.Position;
                _talkTo = who.Id;
                _wsTalkName = who.Name;
                _talkRemote = false;
                SetMouseCaptured(false);
                g.StoryTalkAsk(who.Id, TalkAsk.Open);
                _wsHeardFrom = g.Chat.Count;
                WsSay($"[talk] {who.Name} at {Vector3.Distance(who.Position, me.Position):0.0} m");
                _wsWait = 20;
                return true;
            }
            case "say":
            {
                if (_talkTo == 255)
                {
                    if (_wsTalkName.Length == 0)
                    {
                        WsFail("say: no talk panel open (talk NAME first)");
                        return true;
                    }
                    WsSay($"[say] the panel closed (they walked off?): to {_wsTalkName} again");
                    _wsQueue.EnqueueFront($"say {arg}");
                    _wsQueue.EnqueueFront($"talk {_wsTalkName}");
                    return true;
                }
                var sp = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
                var rest = sp.Length > 1 ? sp[1] : "";
                switch (sp[0].ToLowerInvariant())
                {
                    case "hello": g.StoryTalkAsk(_talkTo, TalkAsk.Greet); break;
                    case "news": g.StoryTalkAsk(_talkTo, TalkAsk.News); break;
                    case "bye": TalkClose(); break;
                    case "choice" when int.TryParse(rest, out var c) && c >= 1 && c <= g.TalkChoiceCount && g.TalkChoicesBody == _talkTo:
                        WsSay($"[say] choice {c}: {g.TalkChoiceLabels[c - 1].Split("##")[0]}");
                        g.StoryTalkAsk(_talkTo, (TalkAsk)((int)TalkAsk.Choose + c - 1));
                        break;
                    case "ask":
                    {
                        var k = -1;
                        if (g.TalkTopicsBody == _talkTo)
                            for (var i = 0; i < g.TalkTopicCount && k < 0; i++)
                                if (g.TalkTopicLabels[i].StartsWith(rest, StringComparison.OrdinalIgnoreCase))
                                    k = i;
                        if (k < 0)
                        {
                            WsSay($"[say] no topic \"{rest}\" on offer");
                            return true;
                        }
                        WsSay($"[say] ask: {g.TalkTopicLabels[k].Split("##")[0]}");
                        g.StoryTalkAsk(_talkTo, TalkAsk.Topic, g.TalkTopics[k]);
                        break;
                    }
                    default:
                        WsSay($"[say] can't \"{arg}\" (hello, news, bye, choice N of {(g.TalkChoicesBody == _talkTo ? g.TalkChoiceCount : 0)}, ask TEXT)");
                        return true;
                }
                _wsWait = 30;
                return true;
            }
            case "secs": // "secs S": the script waits S real seconds (the game runs on: a story's minutes)
                _wsSecsUntil = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture) * System.Diagnostics.Stopwatch.Frequency);
                _wsWait = 2;
                return true;
            case "hold": // "hold MoveForward+Jump 5": those keys down for 5 real seconds (the script goes on meanwhile: add "secs 5" to wait)
            {
                if (!MenusOpen)
                    SetMouseCaptured(true); // (movement keys are built only while the mouse is held: a window that lost focus to the other one had let it go, so a joined client's held keys never reached its seat)
                var sp = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
                _wsKeys.Clear();
                foreach (var name in sp[0].Split('+'))
                    if (Enum.TryParse<GameAction>(name, true, out var ga))
                        _wsKeys.Add(ga);
                    else
                    {
                        WsFail($"hold: no action \"{name}\"");
                        return true;
                    }
                _wsKeysUntil = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(double.Parse(sp.Length > 1 ? sp[1] : "1", System.Globalization.CultureInfo.InvariantCulture) * System.Diagnostics.Stopwatch.Frequency);
                return true;
            }
            case "seat": // "seat NAME": stand by the first Seat of that building or vehicle (by its blueprint's name) and press the use key on it
            {
                if (g.ColonyBuilding(arg) is not { } c)
                {
                    WsFail($"seat: no body called \"{arg}\"");
                    return true;
                }
                var part = -1;
                for (var i = 0; i < c.PartCount && part < 0; i++)
                    if (c.DefOf(i).Name == "Seat")
                        part = i;
                if (part < 0)
                {
                    WsFail($"seat: {arg} has no Seat");
                    return true;
                }
                var spot = WsPartSpot(c, part, out _);
                var up = GravityFrames.UpAt(spot);
                me.Teleport(spot + up * 0.2f - Vector3.Normalize(spot - c.WorldCom with { }) * 0f);
                WsEyeTo(g, me, spot + up * 1.4f);
                g.LookFrame = me.Position;
                (g.LookYaw, g.LookPitch) = GravityFrames.Curved ? GravityFrames.Angles(me.Position, spot - me.Eye) : (g.LookYaw, -1.2f);
                WsGrabMouse();
                _wsPress = GameAction.Interact;
                _wsWait = 30;
                WsSay($"[seat] {arg} part {part}, {Vector3.Distance(me.Eye, spot):0.0} m");
                return true;
            }
            case "where": // where this player is: from the colony's middle and from the spaceport (m), and what they sit in
                WsSay($"[where] at {me.Position:0}, {Vector3.Distance(me.Position, g.ColonyBuilding("Penal Colony")?.WorldCom ?? me.Position):0} m from the colony, {Vector3.Distance(me.Position, g.ColonyBuilding("Spaceport Terminal")?.WorldCom ?? me.Position):0} m from the spaceport terminal, seated {g.SandboxHands.Hand(me.Id).Seated}");
                return true;
            case "standing": // host: the four convicts' feelings for this player and the break-out's qualities
                WsSay("[standing] " + g.StoryStanding(me, ["Dot Pruett", "Wren Haskett", "Ambrose Thale", "Hester Mossop", "Sergeant Bellweather"], ["Crew", "Suspicion", "Grassed", "Inducted", "Parcel", "Contraband", "Escaped"]));
                return true;
            case "heard":
            {
                for (var i = Math.Max(0, Math.Min(_wsHeardFrom, g.Chat.Count)); i < g.Chat.Count; i++)
                    if ((g.Chat[i].Flags & ChatFlags.Say) != 0 || g.Chat[i].From == 255)
                        WsSay($"[heard] {(g.Players.TryGetValue(g.Chat[i].From, out var q) ? q.Name : "-")}: {g.Chat[i].Text}");
                _wsHeardFrom = g.Chat.Count;
                if (_talkTo != 255)
                {
                    if (g.TalkChoicesBody == _talkTo)
                        for (var k = 0; k < g.TalkChoiceCount; k++)
                            WsSay($"[heard] choice {k + 1}: {g.TalkChoiceLabels[k].Split("##")[0]}");
                    if (g.TalkTopicsBody == _talkTo)
                        for (var k = 0; k < g.TalkTopicCount; k++)
                            WsSay($"[heard] topic: {g.TalkTopicLabels[k].Split("##")[0]}{(g.TalkTopicAsked[k] ? " (asked)" : "")}");
                }
                return true;
            }
        }
        return false;
    }
}

public sealed partial class Game
{
    /// <summary>Host, the window tool's "standing": how each named person feels about this player, and the player's qualities named.</summary>
    internal string StoryStanding(Player me, string[] people, string[] qualities)
    {
        if (Story is not { } s || StoryWho(me) is var who && who < 0)
            return "no story, or not known to it yet";
        var w = s.World;
        var sb = new System.Text.StringBuilder();
        foreach (var name in people)
        {
            var id = w.Find(name);
            if (id < 0)
            {
                sb.Append($"{name}: unknown; ");
                continue;
            }
            sb.Append($"{name}:");
            foreach (var rel in new[] { "Likes", "Grudge" })
                if (s.Rules.Relation(rel) is { } r)
                    sb.Append($" {rel} {w.Get(id, r.Id, who)}");
            sb.Append("; ");
        }
        foreach (var q in qualities)
            if (s.Rules.Quality(q) is { } qd)
                sb.Append($"{q} {w.Quality(who, qd.Id)}; ");
        return sb.ToString();
    }
}
