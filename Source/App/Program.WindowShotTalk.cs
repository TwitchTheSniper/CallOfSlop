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
    private static long _wsSecsUntil;

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

    private static bool WsTalkKnown(string action) => action.Split(' ', 2)[0].ToLowerInvariant() is "talk" or "say" or "heard" or "standing" or "secs";

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
                    WsFail("say: no talk panel open (talk NAME first)");
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
