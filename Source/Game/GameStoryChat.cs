using System.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>What a player asks a person they talk to (<see cref="CActKind.Talk"/>'s byte).</summary>
public enum TalkAsk : byte
{
    Open, // the talk panel opened: they stop and turn to the player
    Greet, // hello: the first time a Greet (they meet), after that a Chat
    News, // any news? the best news they have that this player hasn't had from them
    Trade, // a trader takes the part in the player's hands and gives one of theirs for it
    Bye, // the panel closed: they go back to what they were doing
}

/// <summary>
/// Players talking to the story's people (docs/story/STORY.md "Talking to people"), and the bodies turning to whoever
/// they talk to. The use key on a person's body opens a small talk panel on that player's screen (Program.StoryTalk.cs);
/// each button is a <see cref="TalkAsk"/> sent to the host, which answers through the story: a greeting is the story's
/// Greet (or a Chat, at most every <see cref="StoryChatGap"/>), news is a Tell from the person to the player, a trade is
/// a Trade (the player buys from a trader) and a part handed over for one from the trader's stock (Loot.json's "trader"
/// table, worth no more than <see cref="StoryTradeCut"/> of what was given). What the person says comes from the
/// response rules (Story/Lines/Talk.json and the answers in People.json), said aloud like any of their lines.
/// A body stops and faces a player who is talking to it until the panel closes, they walk off or
/// <see cref="StoryTalkHold"/> passes with nothing asked; a body that says something to someone turns to them for
/// <see cref="StoryFaceFor"/> (and stops, when that someone is a player); a body with someone in mind for its step
/// faces them once it has reached them. Host only; nothing allocates in a tick, only when something is asked.
/// </summary>
public sealed partial class Game
{
    public const float StoryTalkReach = 3.5f, StoryTalkLeave = 6f; // m: a player talks to someone this close, and the talk ends past this
    public const double StoryTalkHold = 20; // s a body waits for a player who has asked nothing
    public const double StoryFaceFor = 4; // s a body keeps facing whoever it spoke to
    public const double StoryChatGap = 120; // s between two Chats recorded for the same player and person
    public const float StoryTradeCut = 0.8f; // a trader gives something worth at most this much of what they were given
    public const string StoryTradeTable = "trader"; // Constructs/Loot.json

    // Per body's player id: who it is talking to and facing (entities + 1, 0: nobody). A slot is checked against the person (Owner) before use.
    private readonly int[] _faceOwner = new int[256], _faceTo = new int[256];
    private readonly double[] _faceUntil = new double[256], _talkUntil = new double[256];
    private readonly byte[] _talkWith = new byte[256]; // the player's id + 1 (0: nobody)
    private readonly Dictionary<long, double> _storyChatAt = new(); // (person, player entity): when their last Chat was recorded
    private readonly HashSet<long> _storyTold = new(); // (player entity, event): news a player has had from somebody
    private int _vGreet = -2, _vChat, _vTell, _vTrade, _qTrader;
    private int _cGreeted, _cChatted, _cTraded, _cNoNews, _cNotTrader, _cEmptyHands, _cLowOffer, _cBye;

    /// <summary>Talks this session: asks answered, news told, trades made (the Story window).</summary>
    public int StoryTalkAsks, StoryNewsTold, StoryTrades;

    /// <summary>Client (or host): ask a person something from the talk panel.</summary>
    public void StoryTalkAsk(byte bodyPlayer, TalkAsk ask) => SandboxSend(CActKind.Talk, body: bodyPlayer, flags: (byte)ask);

    /// <summary>A loaded or fresh story: who was told what is forgotten (entity ids changed).</summary>
    private void StoryChatForget()
    {
        _storyChatAt.Clear();
        _storyTold.Clear();
        Array.Clear(_talkWith);
        Array.Clear(_faceOwner);
        _vGreet = -2;
    }

    private void StoryChatIds(StoryEngine s)
    {
        if (_vGreet != -2)
            return;
        var r = s.Rules;
        (_vGreet, _vChat, _vTell, _vTrade) = (r.VerbId("Greet"), r.VerbId("Chat"), r.VerbId("Tell"), r.VerbId("Trade"));
        _qTrader = r.Quality("Trader")?.Id ?? -1;
        var l = r.Lines;
        (_cGreeted, _cChatted, _cTraded, _cNoNews) = (l.Concept("Greeted"), l.Concept("Chatted"), l.Concept("Traded"), l.Concept("NoNews"));
        (_cNotTrader, _cEmptyHands, _cLowOffer, _cBye) = (l.Concept("NotTrader"), l.Concept("EmptyHands"), l.Concept("LowOffer"), l.Concept("Bye"));
    }

    // ---- What a player asks ---------------------------------------------------------------------------------------------

    /// <summary>Server: a player asks a person something (<see cref="CActKind.Talk"/>). Returns why not, or null.</summary>
    private string? StoryTalkAct(Player p, byte bodyPlayer, TalkAsk ask)
    {
        if (Story is not { } s || !Players.TryGetValue(bodyPlayer, out var body) || body.Team != SbPeopleTeam || !body.Alive
            || StoryBodyIndex(bodyPlayer) is not (var i and >= 0) || s.Agents.Of(_bodies[i].Entity) is not { Dead: false } a)
            return ask == TalkAsk.Bye ? null : "nobody to talk to";
        StoryChatIds(s);
        StoryFaceClaim(bodyPlayer, a.Entity);
        if (ask == TalkAsk.Bye)
        {
            if (_talkWith[bodyPlayer] == p.Id + 1)
            {
                _talkWith[bodyPlayer] = 0;
                if (StoryEntityOf(p) is var gone and >= 0)
                    StoryAnswer(s, a, gone, _cBye);
            }
            return null;
        }
        if (Flat2(body.Position - p.Position) > (StoryTalkReach + 1) * (StoryTalkReach + 1))
            return "too far away to talk";
        var who = StoryWho(p);
        if (who < 0)
            return "they don't know who you are yet";
        if (_talkWith[bodyPlayer] is var other and > 0 && other != p.Id + 1 && Players.TryGetValue((byte)(other - 1), out var busy) && busy.Alive
            && SbClock < _talkUntil[bodyPlayer] && Flat2(busy.Position - body.Position) <= StoryTalkLeave * StoryTalkLeave)
            return $"{body.Name} is talking to {busy.Name}";
        _talkWith[bodyPlayer] = (byte)(p.Id + 1);
        _talkUntil[bodyPlayer] = SbClock + StoryTalkHold;
        StoryTalkAsks++;
        switch (ask)
        {
            case TalkAsk.Greet:
                StoryGreet(s, a, who);
                break;
            case TalkAsk.News:
                StoryTellPlayer(s, a, who);
                break;
            case TalkAsk.Trade:
                return StoryTrade(s, a, p, who);
        }
        return null;
    }

    /// <summary>The game clock the bodies run on (<see cref="StoryBodyBot"/>'s now).</summary>
    private double SbClock => SandboxNow;

    /// <summary>A person answers a player (always: a player asked, so no gap between their words).</summary>
    private void StoryAnswer(StoryEngine s, StoryAgent a, int who, int concept, int verb = -1)
    {
        var f = LineFacts.Of(a.Entity);
        (f.Them, f.Actor, f.Target, f.Here, f.Verb) = (who, who, a.Entity, s.Agents.Where(a.Entity), verb);
        s.Talk.Say(concept, f, s.Time, prompted: true);
    }

    /// <summary>Hello: the first time the two meet (a Greet, answered as one), then a Chat now and then.</summary>
    private void StoryGreet(StoryEngine s, StoryAgent a, int who)
    {
        var knows = s.Rules.People.Knows;
        var met = knows >= 0 && s.World.Get(who, knows, a.Entity) > 0;
        if (!met)
        {
            s.Record(_vGreet, who, a.Entity, place: StoryPlace());
            StoryAnswer(s, a, who, _cGreeted, _vGreet);
            return;
        }
        var key = (long)a.Entity << 32 | (uint)who;
        if (!_storyChatAt.TryGetValue(key, out var at) || s.Time - at >= StoryChatGap)
        {
            _storyChatAt[key] = s.Time;
            s.Record(_vChat, who, a.Entity, place: StoryPlace()); // (it warms them to each other, like any Chat)
        }
        StoryAnswer(s, a, who, _cChatted, _vChat);
    }

    /// <summary>Any news? The most interesting thing they know that the player didn't do and hasn't had from anyone here.</summary>
    private void StoryTellPlayer(StoryEngine s, StoryAgent a, int who)
    {
        var p = s.Rules.People;
        int best = -1;
        var bestV = 0f;
        for (var k = 0; k < a.KnownCount; k++)
        {
            ref readonly var it = ref a.Known[k];
            if (it.Actor == who || _storyTold.Contains((long)who << 32 | (uint)it.Event))
                continue;
            if ((uint)it.Verb < (uint)p.ActionOfVerb.Length && p.ActionOfVerb[it.Verb] is var ai and >= 0 && p.Actions[ai].Sneaky && it.Source == KnowSource.Did)
                continue; // (nobody owns up to their own sneaky deeds)
            if (it.Verb == _vTell || it.Verb == s.Rules.StoryVerb)
                continue; // (who told whom, and the stories about stories: the news itself is told, not that)
            var v = StoryAgents.Faded(it, s.Time);
            if (v > bestV)
                (bestV, best) = (v, k);
        }
        if (best < 0)
        {
            StoryAnswer(s, a, who, _cNoNews);
            return;
        }
        var news = a.Known[best];
        _storyTold.Add((long)who << 32 | (uint)news.Event);
        var here = s.Agents.Where(a.Entity);
        var f = LineFacts.Of(a.Entity);
        (f.Them, f.Verb, f.News, f.About, f.Actor, f.Target, f.Owner, f.Place, f.Here) = (who, _vTell, news.Event, news.Verb, news.Actor, news.Target, news.Owner, news.Place, here);
        s.Talk.Say(LineRules.Step, f, s.Time, prompted: true); // (said as a Tell of theirs is: "Did you hear? ...")
        s.Record(_vTell, a.Entity, who, news.Target, here, news.Actor, news.Event);
        StoryNewsTold++;
    }

    /// <summary>A trade: a trader takes what the player holds and gives one of their stock for it, worth at most <see cref="StoryTradeCut"/> of it.</summary>
    private string? StoryTrade(StoryEngine s, StoryAgent a, Player p, int who)
    {
        if (_qTrader < 0 || s.World.Quality(a.Entity, _qTrader) <= 0)
        {
            StoryAnswer(s, a, who, _cNotTrader);
            return null;
        }
        ref var h = ref SandboxHands.Hand(p.Id);
        if (ConstructParts.Get(h.Held) is not { } given || h.Seated)
        {
            StoryAnswer(s, a, who, _cEmptyHands);
            return null;
        }
        if (h.Edit != EditKind.None)
            return "finish what you are doing with it first";
        if (!ConstructLoot.Tables.TryGetValue(StoryTradeTable, out var table) || table.Items.Length == 0)
            return $"{s.World.NameOf(a.Entity)} has nothing to sell (no \"{StoryTradeTable}\" table in Loot.json)";
        // What they give: one of the best they can afford to give for it (worth at least half of the best such), by the table's weights.
        var worth = StoryWorth(given) * StoryTradeCut;
        var top = 0f;
        foreach (var it in table.Items)
            if (it.Part != given && StoryWorth(it.Part) is var v && v <= worth)
                top = MathF.Max(top, v);
        if (top <= 0)
        {
            StoryAnswer(s, a, who, _cLowOffer);
            return null;
        }
        var total = 0;
        foreach (var it in table.Items)
            if (StoryTradeOffer(it, given, worth, top))
                total += it.Weight;
        var roll = _rng.Next(Math.Max(1, total));
        var pick = table.Items[0];
        foreach (var it in table.Items)
            if (StoryTradeOffer(it, given, worth, top) && (roll -= it.Weight) < 0)
            {
                pick = it;
                break;
            }
        h.Held = ConstructParts.None; // (the trader takes it)
        h.Edit = EditKind.None;
        var where = p.Eye + GravityFrames.FlatForward(p.Position, p.Yaw) * 0.6f;
        SandboxHands.Give(Constructs, p.Id, pick.Part, where);
        SandboxHands.Notices.Add((p.Id, $"Traded your {given.Name} for a {pick.Part.Name}"));
        s.Record(_vTrade, who, a.Entity, place: StoryPlace());
        StoryAnswer(s, a, who, _cTraded, _vTrade);
        StoryTrades++;
        Console.WriteLine($"[story] {p.Name} traded a {given.Name} (worth {StoryWorth(given):0}) to {s.World.NameOf(a.Entity)} for a {pick.Part.Name} (worth {StoryWorth(pick.Part):0})");
        return null;
    }

    private static bool StoryTradeOffer(in LootItem it, ConstructPartDef given, float worth, float top) =>
        it.Part != given && StoryWorth(it.Part) is var v && v <= worth && v >= top * 0.5f;

    private static readonly Dictionary<ConstructPartDef, float> _storyWorth = new();
    private static Dictionary<ConstructPartDef, AssembleRecipe>? _storyRecipes;

    /// <summary>
    /// What a part is worth to a trader: the material units of its Assembler recipe plus what its parts are worth, per
    /// part made; a part with no recipe (a weapon part, a runtime piece) by its mass, a unit a kilogram, at least 1.
    /// </summary>
    internal static float StoryWorth(ConstructPartDef d, int depth = 0)
    {
        if (_storyWorth.TryGetValue(d, out var w))
            return w;
        if (_storyRecipes == null)
        {
            _storyRecipes = new();
            foreach (var r in AssemblingTable.Current.Recipes)
                _storyRecipes.TryAdd(r.Output, r);
        }
        if (_storyRecipes.TryGetValue(d, out var recipe) && depth < 8)
        {
            w = 0;
            foreach (var (_, units) in recipe.Materials)
                w += units;
            foreach (var (part, count) in recipe.Parts)
                w += StoryWorth(part, depth + 1) * count;
            w /= Math.Max(1, recipe.Count);
        }
        else
            w = d.Mass;
        w = MathF.Max(1, w);
        if (depth == 0 || recipe == null)
            _storyWorth[d] = w;
        return w;
    }

    // ---- Facing whoever they talk to ------------------------------------------------------------------------------------

    /// <summary>A body's slots belong to its person from now on (a new body on a player id starts with nobody in mind).</summary>
    private void StoryFaceClaim(byte bodyPlayer, int entity)
    {
        if (_faceOwner[bodyPlayer] == entity + 1)
            return;
        _faceOwner[bodyPlayer] = entity + 1;
        _faceTo[bodyPlayer] = 0;
        _talkWith[bodyPlayer] = 0;
    }

    /// <summary>From the talk tick: a person with a body here said something to someone, so they turn to them for a while.</summary>
    private void StoryFaceSaid(int speaker, int to)
    {
        if (to < 0)
            return;
        for (var i = 0; i < _bodyCount; i++)
            if (_bodies[i].Entity == speaker)
            {
                var id = _bodies[i].Player;
                StoryFaceClaim(id, speaker);
                _faceTo[id] = to + 1;
                _faceUntil[id] = SbClock + StoryFaceFor;
                return;
            }
    }

    /// <summary>
    /// From <see cref="StoryBodyBot"/>, after its goal is set: a body talking to a player stands still and faces them; one
    /// that just spoke to someone faces them (and stands, for a player); one beside whoever its step is with faces them.
    /// The face is the bot's glance (BotMove.cs), held on them.
    /// </summary>
    private void StoryBodyFace(Player p, BotBrain b, double now)
    {
        var i = StoryBodyIndex(p.Id);
        if (i < 0 || Story is not { } s)
            return;
        ref var body = ref _bodies[i];
        StoryFaceClaim(p.Id, body.Entity);
        Vector3? face = null;
        var stop = false;
        // A player talking to them.
        if (_talkWith[p.Id] is var t and > 0)
        {
            if (Players.TryGetValue((byte)(t - 1), out var them) && them.Alive && now < _talkUntil[p.Id] && !body.Leaving
                && Flat2(them.Position - p.Position) <= StoryTalkLeave * StoryTalkLeave)
                (face, stop) = (them.Position, true);
            else
                _talkWith[p.Id] = 0;
        }
        // Someone they just spoke to.
        if (face == null && _faceTo[p.Id] > 0 && now < _faceUntil[p.Id] && StoryPosOf(_faceTo[p.Id] - 1) is { } to && Flat2(to - p.Position) <= 12f * 12f)
        {
            face = to;
            stop = !body.Leaving && StoryBodyOf(_faceTo[p.Id] - 1) == null && Flat2(to - p.Position) <= StoryTalkLeave * StoryTalkLeave; // (a player: wait for them)
        }
        // Whoever their step is with, once they are beside them.
        if (face == null && s.Agents.Of(body.Entity) is { } a)
        {
            var with = s.Plays.PartnerOf(a.Entity);
            if (with < 0 && a.Busy && a.Doing.Target >= 0 && !s.Rules.People.Actions[a.Doing.Action].Road)
                with = a.Doing.Target;
            if (with >= 0 && StoryPosOf(with) is { } them && Flat2(them - p.Position) <= StoryMeetReach * StoryMeetReach)
                face = them;
        }
        if (face is not { } at)
            return;
        if (stop)
        {
            b.ForcedGoal = p.Position;
            b.HoldAnchor = p.Position;
        }
        var dir = at - p.Position;
        if (Flat2(dir) < 0.04f)
            return;
        b.GlanceYaw = GravityFrames.YawOf(p.Position, dir);
        b.NextGlance = b.GlanceUntil = now + 0.5; // (renewed each tick while it lasts; the ordinary glances come back after)
    }
}

