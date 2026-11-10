using System.Numerics;
using Brutal.RakNetApi;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The game's side of the storylets (docs/story/STORY.md "Storylets"; the engine is Source/Story/StoryStorylets.cs):
/// the induction's end recorded as an Induct, a player's visits to a storylet spot (near a person's body) counted in
/// Visited, the hooks a storylet starts ("lockdown": the colony's lockdown with the player wanted; "depot": the Orbital
/// Depot's report memo), a line nobody says sent as a notice, and the choices a storylet offers shown in the talk panel
/// of the person who asks (<see cref="Msg.TalkChoices"/>: the host sends them, the player's pick comes back as a
/// <see cref="TalkAsk"/> from <see cref="TalkAsk.Choose"/> up). Host only; nothing allocates in a steady tick.
/// </summary>
public sealed partial class Game
{
    public const int TalkChoicesMax = 4;
    public const double StoryletSpotEvery = 0.5; // s between looks at the spots

    private Action<int, string, Ints8>? _storyletHook;
    private Action<int, string>? _storyletNotice;
    private StoryEngine? _storyletWired;
    private int _vInduct = -2;
    private double _spotAt, _choicesAt;
    private readonly HashSet<long> _spotNear = new(); // (player entity, spot index): within reach now (a visit counts on coming in)
    private readonly int[] _choiceKey = new int[256]; // per player: what their talk panel's choices were last sent as
    private readonly (int Serial, int End)[] _choiceBuf = new (int, int)[TalkChoicesMax];
    private PacketWriter? _choiceW;

    /// <summary>Client (and host): the choices the talk panel with <see cref="TalkChoicesBody"/> offers now.</summary>
    public readonly string[] TalkChoices = new string[TalkChoicesMax];
    public readonly string[] TalkChoiceLabels = new string[TalkChoicesMax]; // the buttons' labels ("text##talkchoiceN")
    public int TalkChoiceCount;
    public byte TalkChoicesBody = 255;

    /// <summary>Does a storylet start the Orbital Depot ("hook": "depot")? Then the depot's memo waits for it, instead of following the induction.</summary>
    public static bool DepotByStorylet => _depotByStorylet ??= StoryData.Storylets.Storylets.Any(d => d.Hook == "depot" || d.Ends.Any(e => e.Hook == "depot"));
    private static bool? _depotByStorylet;

    private void StoryletsForget()
    {
        _spotNear.Clear();
        Array.Clear(_choiceKey);
    }

    /// <summary>Server, from <see cref="StoryTick"/>: the hooks wired to this story, the spots, and the open talk panels' choices.</summary>
    private void StoryletsTick(StoryEngine s, double now)
    {
        var sr = s.Rules.Storylets;
        if (sr.Storylets.Length == 0)
            return;
        if (!ReferenceEquals(_storyletWired, s))
        {
            _storyletWired = s;
            s.Storylets.Hook = _storyletHook ??= StoryletHook;
            s.Storylets.Notice = _storyletNotice ??= StoryletNotice;
            _vInduct = s.Rules.VerbId("Induct");
            StoryletsForget();
        }
        if (now >= _spotAt)
        {
            _spotAt = now + StoryletSpotEvery;
            StoryletSpots(s, sr);
            foreach (var p in Players.Values)
                if (p.Bot == null && p.Id < MaxPlayers && _tutState[p.Id] is TutorialState.Finished or TutorialState.Skipped)
                    StoryletInducted(p); // (once each: also a world saved before storylets, or a skip)
        }
        if (now >= _choicesAt)
        {
            _choicesAt = now + 0.5;
            StoryletChoicesSend(s);
        }
    }

    /// <summary>Server, from the induction (GameTutorial.cs): it finished or was skipped: an Induct, once per player.</summary>
    private void StoryletInducted(Player p)
    {
        if (Story is not { } s || _vInduct < 0 && (_vInduct = s.Rules.VerbId("Induct")) < 0 || StoryWho(p) is not (var who and >= 0))
            return;
        if (s.Rules.Quality("Inducted") is { } q && s.World.Quality(who, q.Id) > 0)
            return;
        s.Record(_vInduct, who, place: StoryPlace());
    }

    /// <summary>A player coming within a spot's reach of its person's body is a visit (Visited +1); they must walk out of twice the reach for the next.</summary>
    private void StoryletSpots(StoryEngine s, StoryletRules sr)
    {
        if (sr.Spots.Length == 0 || sr.Visited < 0)
            return;
        for (var k = 0; k < sr.Spots.Length; k++)
        {
            var spot = sr.Spots[k];
            var person = s.World.Find(spot.Near);
            if (person < 0 || StoryBodyOf(person) is not { Alive: true } body)
                continue;
            foreach (var p in Players.Values)
            {
                if (p.Bot != null || !p.Alive || StoryEntityOf(p) is not (var who and >= 0))
                    continue;
                var key = (long)who << 8 | (uint)k;
                var d2 = Vector3.DistanceSquared(p.Position, body.Position);
                if (d2 <= spot.Reach * spot.Reach)
                {
                    if (_spotNear.Add(key))
                    {
                        var thing = s.World.Find(spot.Name) is var t and >= 0 ? t : s.World.Get(StoryKind.Thing, StoryFactions.Key("fact:" + spot.Name), spot.Name, s.Time);
                        s.World.Add(who, sr.Visited, thing, 1);
                    }
                }
                else if (d2 > 4 * spot.Reach * spot.Reach)
                    _spotNear.Remove(key);
            }
        }
    }

    /// <summary>A storylet's hook, for a player (an entity).</summary>
    private void StoryletHook(int player, string hook, Ints8 cast)
    {
        var p = StoryPlayerOf(player) is { Bot: null } pp ? pp : null;
        if (p == null)
            return;
        var now = SandboxNow;
        switch (hook)
        {
            case "lockdown":
                LockdownStart(p, $"[MINISTRY MEMO] LOCKDOWN at the Penal Colony: contraband found on {p.Name}. The barracks is turning out.", now);
                break;
            case "depot":
                DepotOffer(p, now);
                break;
        }
        Log($"[storylet] hook {hook} for {p.Name}");
    }

    /// <summary>A storylet's line with nobody to say it: to that player alone, as a notice ("[MINISTRY MEMO] ..." keeps its tag).</summary>
    private void StoryletNotice(int player, string text)
    {
        if (StoryPlayerOf(player) is not { Bot: null } p)
            return;
        var close = text.StartsWith('[') ? text.IndexOf("] ", StringComparison.Ordinal) : -1;
        OpeningSay(p, null, close > 0 ? text[..(close + 1)] : "Note", close > 0 ? text[(close + 2)..] : text, SandboxNow);
    }

    // ---- Choices in the talk panel -----------------------------------------------------------------------------------

    /// <summary>The choices a player has from the person whose body they talk to: into <see cref="_choiceBuf"/>, how many.</summary>
    private int StoryletChoicesOf(StoryEngine s, Player p, byte bodyPlayer)
    {
        if (StoryEntityOf(p) is not (var who and >= 0) || StoryBodyIndex(bodyPlayer) is not (var i and >= 0))
            return 0;
        return s.Storylets.ChoicesFor(who, _bodies[i].Entity, _choiceBuf);
    }

    /// <summary>Twice a second: each player talking to someone gets the choices that person offers them, when they changed.</summary>
    private void StoryletChoicesSend(StoryEngine s)
    {
        foreach (var p in Players.Values)
        {
            if (p.Bot != null)
                continue;
            var body = (byte)255;
            for (var b = 0; b < _talkWith.Length; b++)
                if (_talkWith[b] == p.Id + 1)
                    body = (byte)b;
            var n = body == 255 ? 0 : StoryletChoicesOf(s, p, body);
            var key = body * 31 + n;
            for (var k = 0; k < n; k++)
                key = key * 131 + _choiceBuf[k].Serial * 8 + _choiceBuf[k].End;
            if (_choiceKey[p.Id] == key + 1)
                continue;
            _choiceKey[p.Id] = key + 1;
            var w = _choiceW ??= new PacketWriter(512);
            w.Begin(Msg.TalkChoices).U8(body).U8((byte)n);
            for (var k = 0; k < n; k++)
                w.Str(s.Storylets.ChoiceText(_choiceBuf[k].Serial, _choiceBuf[k].End));
            if (p.Id == LocalId)
            {
                var r = new PacketReader(w.Span[1..]);
                ReadTalkChoices(ref r);
            }
            else if (!Net.IsOffline && GuidOf(p) is { } g)
                Net.SendTo(g, w.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>Client (and host): a <see cref="Msg.TalkChoices"/> after its id: u8 the body, u8 n, n texts.</summary>
    private void ReadTalkChoices(ref PacketReader r)
    {
        if (!r.Has(2))
            return;
        TalkChoicesBody = r.U8();
        var n = Math.Min((int)r.U8(), TalkChoicesMax);
        for (var k = 0; k < n; k++)
        {
            TalkChoices[k] = ChatRules.Clean(r.Text(64));
            TalkChoiceLabels[k] = $"{TalkChoices[k]}##talkchoice{k}";
        }
        TalkChoiceCount = n;
    }

    /// <summary>Server: a player picked the choice at <paramref name="index"/> in their panel (as it was last sent).</summary>
    private string? StoryletChoose(StoryEngine s, Player p, byte bodyPlayer, int index)
    {
        var n = StoryletChoicesOf(s, p, bodyPlayer);
        if ((uint)index >= (uint)n || StoryEntityOf(p) is not (var who and >= 0))
            return "that answer isn't on offer any more";
        var (serial, end) = _choiceBuf[index];
        return s.Storylets.Choose(who, serial, end, s.Time) ? null : "that answer isn't on offer any more";
    }
}
