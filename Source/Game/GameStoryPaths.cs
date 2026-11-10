using Brutal.RakNetApi;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>One storylet on a player's chart (the Logbook's Paths): what the host said, and its words worked out once.</summary>
public sealed class StoryPathStep
{
    public int Def; // StoryletRules.Storylets
    public byte State; // 0 had, 1 under way, 2 paused
    public byte Ends; // a bit per end it has had for them
    public int Times;
    public uint At; // the story second it began (under way) or last ended
    public string Lead = "", Title = "", Sub = "", Tip = "";
    public string[] EndLabel = [], EndTip = []; // per end; a locked end's label is "?"
}

/// <summary>
/// The Logbook's Paths (docs/story/STORY.md "Paths"): each player's own chart of the storylets they have had, the end
/// each came to (the others stay locked), the storylet qualities as tracks, and where they stand with the people who
/// have a feeling about them (Likes less Grudge, a line from hostile to trusted). Host: once a second per player, sent
/// when it changed (<see cref="Msg.StoryPaths"/>). Every peer has the storylets' texts (Story/Storylets), so the message
/// carries numbers and names only.
/// </summary>
public sealed partial class Game
{
    public const int StoryPathsMax = 64, StoryStandingsMax = 24;

    /// <summary>The local player's chart, oldest first.</summary>
    public readonly List<StoryPathStep> StoryPaths = new(StoryPathsMax + 1);

    /// <summary>The local player's storylet qualities (an index into StoryletRules.Qualities, and its value).</summary>
    public readonly List<(int Quality, int Value)> StoryTracks = new(33);

    /// <summary>Where the local player stands with people: -100 (hostile) to 100 (trusted).</summary>
    public readonly List<(string Name, int Standing)> StoryStandings = new(StoryStandingsMax + 1);

    private readonly Player?[] _spSentTo = new Player?[256];
    private readonly uint[] _spSentHash = new uint[256];
    private double _spNext;
    private int _spLikes = -2, _spGrudge = -2;
    private StoryEngine? _spFor;

    /// <summary>Both sides, a new match: nothing charted.</summary>
    private void StoryPathsReset()
    {
        Array.Clear(_spSentTo);
        StoryPaths.Clear();
        StoryTracks.Clear();
        StoryStandings.Clear();
        _spFor = null;
        _spNext = 0;
    }

    /// <summary>Server, from <see cref="StoryTick"/>: once a second, each player's chart if it changed.</summary>
    private void StoryPathsTick(StoryEngine s, double now)
    {
        if (now < _spNext && now > _spNext - 2)
            return;
        _spNext = now + 1;
        var sr = s.Rules.Storylets;
        if (sr.Storylets.Length == 0 || sr.Ended < 0)
            return;
        if (!ReferenceEquals(_spFor, s))
        {
            _spFor = s;
            _spLikes = s.Rules.Relation("Likes")?.Id ?? -1;
            _spGrudge = s.Rules.Relation("Grudge")?.Id ?? -1;
        }
        foreach (var p in Players.Values)
            if (p.Bot == null && p.Id < MaxPlayers && StoryWho(p) is var who and >= 0)
                StoryPathsSend(s, sr, p, who);
    }

    private void StoryPathsSend(StoryEngine s, StoryletRules sr, Player p, int who)
    {
        var world = s.World;
        var w = SbW;
        w.Begin(Msg.StoryPaths);
        var countAt = w.Length;
        w.U8(0);
        var n = 0;
        for (var k = 0; k < sr.Storylets.Length && n < StoryPathsMax; k++)
        {
            var t = s.Storylets.Thing(k);
            var on = world.Get(who, sr.OnStorylet, t);
            var times = world.Get(who, sr.Played, t);
            if (on <= 0 && times <= 0)
                continue;
            var led = on > 0 ? StoryPathLead(s, who, k) : sr.EndedWith >= 0 ? world.Get(who, sr.EndedWith, t) - 1 : -1;
            w.U16((ushort)k).U8((byte)(on <= 0 ? 0 : on >= 2 ? 2 : 1)).U8((byte)world.Get(who, sr.Ended, t)).U8((byte)Math.Min(times, 255))
                .U32((uint)Math.Max(0, world.Get(who, sr.StoryletAt, t))).Str(led >= 0 && world.Valid(led) ? world.NameOf(led) : "");
            n++;
        }
        w.PatchU8(countAt, (byte)n);
        w.U8((byte)Math.Min(sr.Qualities.Length, 32));
        for (var k = 0; k < sr.Qualities.Length && k < 32; k++)
            w.U8((byte)k).U16((ushort)(short)Math.Clamp(world.Quality(who, sr.Qualities[k].Id), short.MinValue, short.MaxValue));
        countAt = w.Length;
        w.U8(0);
        n = 0;
        if (_spLikes >= 0)
            for (var i = 0; i < world.Count && n < StoryStandingsMax; i++)
            {
                if (i == who || world[i].Kind != StoryKind.Person || world[i].Gone)
                    continue;
                var likes = world.Get(i, _spLikes, who);
                var grudge = _spGrudge >= 0 ? world.Get(i, _spGrudge, who) : 0;
                if (likes == 0 && grudge == 0)
                    continue;
                w.Str(world[i].Name).U8((byte)(sbyte)Math.Clamp(likes - grudge, -100, 100));
                n++;
            }
        w.PatchU8(countAt, (byte)n);
        var hash = 2166136261u;
        foreach (var x in w.Span)
            hash = (hash ^ x) * 16777619u;
        if (_spSentTo[p.Id] == p && _spSentHash[p.Id] == hash)
            return;
        _spSentTo[p.Id] = p;
        _spSentHash[p.Id] = hash;
        if (p.Id == LocalId)
        {
            var r = new PacketReader(w.Span);
            r.U8(); // (the message id)
            ReadStoryPaths(ref r);
            return;
        }
        if (GuidOf(p) is { } guid)
            Net.SendTo(guid, w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Who leads the storylet under way for a player (-1: it has no cast).</summary>
    private static int StoryPathLead(StoryEngine s, int who, int def)
    {
        foreach (var r in s.Storylets.Runs)
            if (r.Player == who && r.Def == def)
                return s.Rules.Storylets.Storylets[def].CastVars > 1 ? r.Binds[1] : -1;
        return -1;
    }

    /// <summary>Client (and the host's own): a <see cref="Msg.StoryPaths"/> (after its id).</summary>
    private void ReadStoryPaths(ref PacketReader r)
    {
        var sr = StoryData.Storylets;
        StoryPaths.Clear();
        StoryTracks.Clear();
        StoryStandings.Clear();
        var n = r.Has(1) ? r.U8() : 0;
        for (var k = 0; k < n && r.Has(10); k++)
        {
            var step = new StoryPathStep { Def = r.U16(), State = r.U8(), Ends = r.U8(), Times = r.U8(), At = r.U32(), Lead = r.Text(60) };
            if (step.Def < sr.Storylets.Length && StoryPaths.Count < StoryPathsMax)
            {
                StoryPathWords(sr, step);
                StoryPaths.Add(step);
            }
        }
        StoryPaths.Sort(static (a, b) => a.At != b.At ? a.At.CompareTo(b.At) : a.Def.CompareTo(b.Def));
        n = r.Has(1) ? r.U8() : 0;
        for (var k = 0; k < n && r.Has(3); k++)
        {
            var q = r.U8();
            var v = (short)r.U16();
            if (q < sr.Qualities.Length)
                StoryTracks.Add((q, v));
        }
        n = r.Has(1) ? r.U8() : 0;
        for (var k = 0; k < n && r.Has(2) && StoryStandings.Count < StoryStandingsMax; k++)
        {
            var name = r.Text(60);
            if (r.Has(1))
                StoryStandings.Add((name, Math.Clamp((int)(sbyte)r.U8(), -100, 100)));
        }
    }

    /// <summary>A step's words for the chart, once per message: its title, state, and each end's label and what it changed.</summary>
    private static void StoryPathWords(StoryletRules sr, StoryPathStep step)
    {
        var d = sr.Storylets[step.Def];
        step.Title = d.Title.Length > 0 ? d.Title : d.Id;
        step.Sub = step.State switch { 1 => "happening now", 2 => "on hold", _ => step.Times > 1 ? $"{step.Times} times" : step.Lead.Length > 0 ? $"with {step.Lead}" : "" };
        step.Tip = step.State != 0 ? step.Lead.Length > 0 ? $"With {step.Lead}. Not settled yet." : "Not settled yet." // (what it is about would give the end away)
            : d.About.Length > 0 ? char.ToUpperInvariant(d.About[0]) + d.About[1..] + "." : "";
        step.EndLabel = new string[d.Ends.Length];
        step.EndTip = new string[d.Ends.Length];
        for (var k = 0; k < d.Ends.Length; k++)
        {
            var e = d.Ends[k];
            var had = (step.Ends >> k & 1) != 0;
            step.EndLabel[k] = had ? e.Choice.Length > 0 ? e.Choice : StoryPathPretty(e.Id) : "?";
            step.EndTip[k] = had ? StoryPathChanges(d, e, step.Lead) : step.State != 0 ? "Not settled yet." : "A way this could have gone.";
        }
    }

    private static string StoryPathPretty(string id) => id.Length == 0 ? "Done" : char.ToUpperInvariant(id[0]) + id[1..].Replace('-', ' ').Replace('_', ' ');

    /// <summary>What an end changed, in a player's words: "Rank +1", "Officer Crumb thinks better of you", "Course: Trustee".</summary>
    private static string StoryPathChanges(StoryletDef d, StoryletEnd e, string lead)
    {
        var rules = StoryData;
        var lines = new List<string>();
        string Who(int v) => v == 0 ? "you" : v == 1 && lead.Length > 0 ? lead : v < d.CastVars ? "someone" : v - d.CastVars < d.Named.Length ? d.Named[v - d.CastVars] : "something";
        foreach (var fx in e.Then)
        {
            var amount = fx.Op == StoryOp.Add ? fx.Amount.ToString("+0;-0") : $"= {fx.Amount}";
            if (fx.Quality)
            {
                if (fx.What < rules.Qualities.Length)
                    lines.Add(fx.Subject == 0 ? $"{rules.Qualities[fx.What].Name} {amount}" : $"{Who(fx.Subject)}: {rules.Qualities[fx.What].Name} {amount}");
                continue;
            }
            if (fx.What >= rules.Relations.Length)
                continue;
            var rel = rules.Relations[fx.What].Name;
            if (fx.Object == 0 && rel is "Likes" or "Grudge")
            {
                var better = (rel == "Likes") == (fx.Amount > 0);
                lines.Add($"{char.ToUpperInvariant(Who(fx.Subject)[0])}{Who(fx.Subject)[1..]} thinks {(better ? "better" : "worse")} of you ({(better ? "+" : "-")}{Math.Abs(fx.Amount)})");
            }
            else if (fx.Subject == 0)
                lines.Add(fx.Op == StoryOp.Set && fx.Amount == 0 ? $"No longer {rel}: {Who(fx.Object)}" : $"{rel}: {Who(fx.Object)}");
        }
        if (e.Learn >= 0)
            lines.Add($"You learnt of {Who(e.Learn)}");
        return lines.Count > 0 ? string.Join("\n", lines) : "Nothing changed.";
    }
}
