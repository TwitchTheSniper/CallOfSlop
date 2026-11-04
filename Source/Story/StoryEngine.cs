using System.Diagnostics;

namespace CallOfSlop.Story;

/// <summary>
/// The story layer on the host (docs/story/STORY.md): the rules, the world state, the chronicle and the sifter, and the
/// story clock (seconds of play in this world, saved with it). <see cref="Record"/> is the one way in: an event's verb
/// changes the world state, the event goes into the chronicle, the sifter sees it, and any story it completes is recorded
/// as a Story event in turn. Nothing here runs per tick except <see cref="Advance"/> (a clock and, once a second, the
/// sifter's time limits).
/// </summary>
public sealed class StoryEngine
{
    public readonly StoryRules Rules;
    public readonly StoryWorld World;
    public readonly StoryChronicle Chronicle = new();
    public readonly StorySifter Sifter;

    /// <summary>The people (phase 2): none until a cast is added.</summary>
    public readonly StoryAgents Agents;

    /// <summary>Seconds of play in this world.</summary>
    public double Time;

    private double _sweepAt;
    public double LastSiftMs, WorstSiftMs, TotalSiftMs;
    public long Recorded;
    private int _depth;

    public StoryEngine(StoryRules rules)
    {
        Rules = rules;
        World = new StoryWorld(rules);
        Sifter = new StorySifter(rules, World, Chronicle);
        Agents = new StoryAgents(this);
    }

    public void Clear()
    {
        World.Clear();
        Chronicle.Clear();
        Sifter.Clear();
        Agents.Clear();
        Time = 0;
        _sweepAt = 0;
        LastSiftMs = WorstSiftMs = TotalSiftMs = 0;
        Recorded = 0;
    }

    /// <summary>
    /// The clock moves on (the host's tick): once a second the sifter's time limits are checked, and the people whose
    /// step ended think. With people, a long step (a headless run) goes a second at a time, so they live through it.
    /// </summary>
    public void Advance(double dt)
    {
        if (!(dt > 0))
            return;
        while (dt > 0)
        {
            var step = Agents.Count > 0 ? Math.Min(dt, 1.0) : dt;
            dt -= step;
            Time += step;
            if (Time >= _sweepAt)
            {
                _sweepAt = Time + 1;
                Sifter.Sweep(Time);
            }
            Agents.Tick(Time);
        }
    }

    /// <summary>
    /// An event happens now: the verb's effects, the chronicle, the sifter, then the stories it completed. Returns the
    /// event's id (-1: no such verb). Roles: an entity id, or -1 for none.
    /// </summary>
    public int Record(int verb, int actor, int target = -1, int owner = -1, int place = -1, int item = -1, int value = 0, bool scripted = false)
    {
        if ((uint)verb >= (uint)Rules.Verbs.Length)
            return -1;
        var clock = Stopwatch.GetTimestamp();
        Span<int> slots = [actor, target, owner, place, item];
        World.Apply(Rules.Verbs[verb].Effects, slots);
        ref var ev = ref Chronicle.Add(new StoryEvent
        {
            At = Time, Verb = (ushort)verb, Actor = actor, Target = target, Owner = owner, Place = place, Item = item, Value = value,
            Flags = scripted ? StoryEventFlags.Scripted : StoryEventFlags.None,
        });
        var copy = ev; // (the chronicle may move as stories are added below)
        Recorded++;
        _depth++;
        try
        {
            Sifter.Pending.Clear();
            Sifter.OnEvent(copy);
            if (Sifter.Pending.Count > 0 && _depth <= 4) // (stories about stories, four deep at most)
            {
                Span<int> done = stackalloc int[Math.Min(Sifter.Pending.Count, 32)];
                for (var i = 0; i < done.Length; i++)
                    done[i] = Sifter.Pending[i];
                Sifter.Pending.Clear();
                foreach (var d in done)
                {
                    var story = Sifter.Done[d];
                    var p = Rules.Patterns[story.Pattern];
                    var a = p.StoryActor >= 0 ? story.Binds[p.StoryActor] : -1;
                    var t = p.StoryTarget >= 0 ? story.Binds[p.StoryTarget] : -1;
                    var id = Record(Rules.StoryVerb, a, t, -1, copy.Place, -1, story.Pattern, scripted);
                    if (d < Sifter.Done.Count && Sifter.Done[d].Pattern == story.Pattern)
                    {
                        story.StoryEvent = id;
                        Sifter.Done[d] = story;
                    }
                    Chronicle.Keep(id);
                }
            }
            Agents.OnEvent(copy); // (the people where it happened see it)
        }
        finally
        {
            _depth--;
        }
        if (_depth == 0)
        {
            LastSiftMs = Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
            WorstSiftMs = Math.Max(WorstSiftMs, LastSiftMs);
            TotalSiftMs += LastSiftMs;
        }
        return copy.Id;
    }

    /// <summary>An event as a sentence ("Karl killed Rook on Planetoid").</summary>
    public string Sentence(in StoryEvent ev)
    {
        if (ev.Verb == Rules.StoryVerb && (uint)ev.Value < (uint)Rules.Patterns.Length)
        {
            foreach (var d in Sifter.Done)
                if (d.StoryEvent == ev.Id)
                    return $"Story: {Rules.Patterns[ev.Value].Name}: {Sifter.Tell(d.Pattern, d.Binds)}";
            return $"Story: {Rules.Patterns[ev.Value].Name} ({World.NameOf(ev.Actor)})";
        }
        var s = (uint)ev.Verb < (uint)Rules.Verbs.Length ? Rules.Verbs[ev.Verb].Sentence : "{actor} ?";
        if (s.Contains("{news}")) // (news told: its value is the event told of, always an earlier one)
            s = s.Replace("{news}", ev.Value > 0 && ev.Value < ev.Id && Chronicle.IndexOf(ev.Value) is var told and >= 0 ? Sentence(Chronicle[told]) : "old news");
        return s.Replace("{actor}", World.NameOf(ev.Actor)).Replace("{target}", World.NameOf(ev.Target)).Replace("{owner}", World.NameOf(ev.Owner))
            .Replace("{place}", World.NameOf(ev.Place)).Replace("{item}", World.NameOf(ev.Item));
    }

    /// <summary>What the sifter did, as a line ("+ Score settled 1/2 started: a = Rook, b = Karl"); times from <paramref name="began"/>.</summary>
    public string NoteLine(in SiftNote n, double began = 0)
    {
        var s = Sifter;
        var p = Rules.Patterns[n.Pattern];
        return n.Kind switch
        {
            SiftKind.Started => $"+ {p.Name} {n.Stage}/{p.Stages.Length} started: {s.BindText(n.Pattern, n.Binds)}",
            SiftKind.Advanced => $"> {p.Name} {n.Stage}/{p.Stages.Length}: {s.BindText(n.Pattern, n.Binds)}",
            SiftKind.Completed => $"* {p.Name}: {s.Tell(n.Pattern, n.Binds)}",
            SiftKind.RanOut => $"x {p.Name} ran out at {n.Stage}/{p.Stages.Length} ({StoryRules.Clock(n.At - began)}): {s.BindText(n.Pattern, n.Binds)}",
            SiftKind.Ended => $"x {p.Name} ended at {n.Stage}/{p.Stages.Length}: {s.BindText(n.Pattern, n.Binds)}",
            _ => $"x {p.Name} dropped (pool full): {s.BindText(n.Pattern, n.Binds)}",
        };
    }

    public string VerbName(int verb) => (uint)verb < (uint)Rules.Verbs.Length ? Rules.Verbs[verb].Name : $"verb {verb}";

    // ---- Saving -----------------------------------------------------------------------------------------------------

    public const byte SaveVersion = 2; // 2: the people (1 still reads, with none)

    /// <summary>
    /// The whole story state as bytes (a world save's Story section). Relations, qualities, verbs and patterns are written
    /// by name, so a save survives data files changing: what no longer exists is left out on load.
    /// </summary>
    public void Write(SaveWriter w)
    {
        var copy = new StorySave();
        Capture(copy);
        copy.Write(w);
    }

    /// <summary>
    /// A plain copy of the state into <paramref name="into"/> (reused between saves: no allocation once it has grown),
    /// for the host's tick; the bytes are made from it on the save's worker (<see cref="StorySave.Write"/>).
    /// </summary>
    public void Capture(StorySave into)
    {
        var s = into;
        s.Time = Time;
        if (s.Relations.Length != Rules.Relations.Length || s.Verbs.Length != Rules.Verbs.Length || s.Patterns.Length != Rules.Patterns.Length || s.Qualities.Length != Rules.Qualities.Length)
        {
            s.Relations = Rules.Relations.Select(r => r.Name).ToArray();
            s.Qualities = Rules.Qualities.Select(q => q.Name).ToArray();
            s.Verbs = Rules.Verbs.Select(v => v.Name).ToArray();
            s.Patterns = Rules.Patterns.Select(p => p.Name).ToArray();
        }
        var n = World.Count;
        StorySave.Grow(ref s.Entities, n);
        StorySave.Grow(ref s.PairStart, n + 1);
        StorySave.Grow(ref s.QualStart, n + 1);
        s.EntityCount = n;
        int pairs = 0, quals = 0;
        foreach (var e in World.All)
        {
            pairs += e.PairCount;
            quals += e.QualityCount;
        }
        StorySave.Grow(ref s.Pairs, pairs);
        StorySave.Grow(ref s.Quals, quals);
        pairs = quals = 0;
        for (var i = 0; i < n; i++)
        {
            var e = World[i];
            s.Entities[i] = new StorySave.Entity(e.Kind, e.Name, e.Key, (byte)((e.Gone ? 1 : 0) | (e.Scripted ? 2 : 0)), e.Born);
            s.PairStart[i] = pairs;
            e.Pairs.AsSpan(0, e.PairCount).CopyTo(s.Pairs.AsSpan(pairs));
            pairs += e.PairCount;
            s.QualStart[i] = quals;
            e.Qualities.AsSpan(0, e.QualityCount).CopyTo(s.Quals.AsSpan(quals));
            quals += e.QualityCount;
        }
        s.PairStart[n] = pairs;
        s.QualStart[n] = quals;
        var keys = World.KeyMapCount;
        StorySave.Grow(ref s.Keys, keys);
        s.KeyCount = 0;
        foreach (var (k, id) in World.KeyMap)
            if (s.KeyCount < s.Keys.Length)
                s.Keys[s.KeyCount++] = (k.Kind, k.Key, id);
        s.NextId = Chronicle.NextId;
        s.FoldedTotal = Chronicle.FoldedTotal;
        StorySave.Grow(ref s.Events, Chronicle.Count);
        Chronicle.All.CopyTo(s.Events);
        s.EventCount = Chronicle.Count;
        StorySave.Grow(ref s.Matches, StorySifter.PoolSize);
        s.MatchCount = 0;
        foreach (ref readonly var m in Sifter.Pool.AsSpan())
            if (m.Live)
                s.Matches[s.MatchCount++] = m;
        StorySave.Grow(ref s.Done, Sifter.Done.Count);
        Sifter.Done.CopyTo(s.Done);
        s.DoneCount = Sifter.Done.Count;
        var np = Rules.Patterns.Length;
        StorySave.Grow(ref s.Counts, np * 5);
        for (var p = 0; p < np; p++)
            (s.Counts[p * 5], s.Counts[p * 5 + 1], s.Counts[p * 5 + 2], s.Counts[p * 5 + 3], s.Counts[p * 5 + 4]) =
                (Sifter.Started[p], Sifter.Completed[p], Sifter.RanOut[p], Sifter.Ended[p], Sifter.Dropped[p]);
        Agents.Capture(s);
    }

    private static Ints8 ReadInts(SaveReader r)
    {
        var v = new Ints8();
        for (var i = 0; i < StoryRules.MaxVars; i++)
            v[i] = r.I32();
        return v;
    }

    /// <summary>Old ids to this build's (by name; -1 where the name is gone).</summary>
    private static int[] ReadNames(SaveReader r, Func<string, int> now)
    {
        var n = r.Count(4);
        var map = new int[n];
        for (var i = 0; i < n; i++)
            map[i] = now(r.Str());
        return map;
    }

    private static int Map(int[] map, int old) => (uint)old < (uint)map.Length ? map[old] : -1;

    /// <summary>Replaces the state with a saved one (a <see cref="WorldFileException"/> when the bytes don't read).</summary>
    public void Read(SaveReader r)
    {
        Clear();
        var version = r.U8();
        if (version is < 1 or > SaveVersion)
            throw new WorldFileException($"story format {version}, this build knows 1 to {SaveVersion}");
        Time = r.F64();
        if (!double.IsFinite(Time) || Time < 0)
            Time = 0;
        var rels = ReadNames(r, s => Rules.Relation(s)?.Id ?? -1);
        var quals = ReadNames(r, s => Rules.Quality(s)?.Id ?? -1);
        var verbs = ReadNames(r, Rules.VerbId);
        var pats = ReadNames(r, s => Rules.Pattern(s)?.Id ?? -1);
        var n = r.Count(16);
        var pairs = new List<(int S, int Rel, int T, int V)>();
        for (var i = 0; i < n; i++)
        {
            var kind = (StoryKind)r.U8();
            var name = r.Str();
            var key = r.U64();
            var flags = r.U8();
            var born = r.F64();
            var id = World.Create(kind, name, 0, born, (flags & 2) != 0);
            if (id >= 0)
            {
                World[id].Key = key;
                World[id].Gone = (flags & 1) != 0;
            }
            var pc = r.Count(10);
            for (var k = 0; k < pc; k++)
            {
                var rel = Map(rels, r.U16());
                var t = r.I32();
                var v = r.I32();
                if (rel >= 0 && id >= 0)
                    pairs.Add((id, rel, t, v));
            }
            var qc = r.Count(6);
            for (var k = 0; k < qc; k++)
            {
                var q = Map(quals, r.U16());
                var v = r.I32();
                if (q >= 0 && id >= 0)
                    World.SetQuality(id, q, v);
            }
        }
        foreach (var (s, rel, t, v) in pairs)
            if (World.Valid(t))
                World.Set(s, rel, t, v); // (symmetric ones are written both ways already: setting twice is the same)
        var kc = r.Count(13);
        for (var i = 0; i < kc; i++)
        {
            var kind = (StoryKind)r.U8();
            var key = r.U64();
            var id = r.I32();
            if (World.Valid(id))
                World.Bind(kind, key, id);
        }
        Chronicle.NextId = r.I32();
        Chronicle.FoldedTotal = r.I64();
        var ec = r.Count(39); // (an event's bytes, as Write puts them)
        for (var i = 0; i < ec; i++)
        {
            var ev = new StoryEvent { Id = r.I32(), At = r.F64() };
            var verb = Map(verbs, r.U16());
            ev.Actor = r.I32();
            ev.Target = r.I32();
            ev.Owner = r.I32();
            ev.Place = r.I32();
            ev.Item = r.I32();
            ev.Value = r.I32();
            ev.Flags = (StoryEventFlags)r.U8();
            if (verb < 0)
                continue;
            ev.Verb = (ushort)verb;
            if (verb == Rules.StoryVerb)
                ev.Value = Map(pats, ev.Value);
            Chronicle.Restore(ev);
        }
        var lc = r.Count(83);
        for (var i = 0; i < lc; i++)
        {
            var old = r.U16();
            var p = Map(pats, old);
            var m = new StoryMatch { Stage = r.U8(), Started = r.F64(), Last = r.F64() };
            m.Binds = ReadInts(r);
            m.Events = ReadInts(r);
            if (p < 0 || m.Stage == 0 || m.Stage >= Rules.Patterns[p].Stages.Length)
                continue;
            m.Pattern = (ushort)p;
            Sifter.Restore(m);
        }
        var dc = r.Count(78);
        for (var i = 0; i < dc; i++)
        {
            var p = Map(pats, r.U16());
            var d = new StoryDone { At = r.F64(), StoryEvent = r.I32() };
            d.Binds = ReadInts(r);
            d.Events = ReadInts(r);
            if (p < 0)
                continue;
            d.Pattern = (ushort)p;
            if (Sifter.Done.Count >= StorySifter.DoneKept)
                Sifter.Done.RemoveAt(0);
            Sifter.Done.Add(d);
        }
        var sc = r.Count(20);
        for (var i = 0; i < sc; i++)
        {
            var p = Map(pats, i);
            var (a, b, c, d, e) = (r.I32(), r.I32(), r.I32(), r.I32(), r.I32());
            if (p < 0)
                continue;
            (Sifter.Started[p], Sifter.Completed[p], Sifter.RanOut[p], Sifter.Ended[p], Sifter.Dropped[p]) = (a, b, c, d, e);
        }
        if (version >= 2)
            Agents.Read(r, verbs);
    }
}

/// <summary>
/// A story's state copied off the host's tick (<see cref="StoryEngine.Capture"/>), written to bytes on the world save's
/// worker. Its arrays are kept and reused between saves.
/// </summary>
public sealed class StorySave
{
    public readonly record struct Entity(StoryKind Kind, string Name, ulong Key, byte Flags, double Born);

    public double Time;
    public string[] Relations = [], Qualities = [], Verbs = [], Patterns = [];
    public Entity[] Entities = [];
    public int EntityCount;
    public int[] PairStart = [], QualStart = []; // per entity, and one past the last
    public StoryPair[] Pairs = [];
    public (ushort Quality, int Value)[] Quals = [];
    public (StoryKind Kind, ulong Key, int Id)[] Keys = [];
    public int KeyCount;
    public int NextId;
    public long FoldedTotal;
    public StoryEvent[] Events = [];
    public int EventCount;
    public StoryMatch[] Matches = [];
    public int MatchCount;
    public StoryDone[] Done = [];
    public int DoneCount;
    public int[] Counts = []; // per pattern: started, completed, ran out, ended, dropped

    /// <summary>A person: who, their goal, the step under way (its verb: actions are saved by verb).</summary>
    public readonly record struct AgentRec(int Entity, bool Scripted, double NeedAt, double WakeAt, int Goal, int GoalTarget, bool Busy,
        ushort Verb, int Target, int Owner, int Place, int Item, double From, double Until, int FromPlace);

    public ulong AgentSeed;
    public string[] NeedNames = [], GoalNames = [];
    public AgentRec[] Agents = [];
    public int AgentCount;
    public float[] AgentNeeds = []; // per person, per need
    public double[] AgentGoalLast = []; // per person, per goal
    public GoalRest[] AgentRests = []; // per person, StoryAgent.MaxRests
    public int[] KnownStart = []; // per person, and one past the last
    public KnownItem[] Known = [];

    /// <summary>Room for <paramref name="n"/> (half again more, so the next save needn't grow it).</summary>
    public static void Grow<T>(ref T[] a, int n)
    {
        if (n > a.Length)
            a = new T[n + n / 2];
    }

    /// <summary>The bytes (<see cref="StoryEngine.Read"/> reads them back).</summary>
    public void Write(SaveWriter w)
    {
        w.U8(StoryEngine.SaveVersion).F64(Time);
        foreach (var names in (ReadOnlySpan<string[]>)[Relations, Qualities, Verbs, Patterns])
        {
            w.I32(names.Length);
            foreach (var name in names)
                w.Str(name);
        }
        w.I32(EntityCount);
        for (var i = 0; i < EntityCount; i++)
        {
            ref readonly var e = ref Entities[i];
            w.U8((byte)e.Kind).Str(e.Name).U64(e.Key).U8(e.Flags).F64(e.Born);
            w.I32(PairStart[i + 1] - PairStart[i]);
            for (var k = PairStart[i]; k < PairStart[i + 1]; k++)
                w.U16(Pairs[k].Rel).I32(Pairs[k].Target).I32(Pairs[k].Value);
            w.I32(QualStart[i + 1] - QualStart[i]);
            for (var k = QualStart[i]; k < QualStart[i + 1]; k++)
                w.U16(Quals[k].Quality).I32(Quals[k].Value);
        }
        w.I32(KeyCount);
        for (var i = 0; i < KeyCount; i++)
            w.U8((byte)Keys[i].Kind).U64(Keys[i].Key).I32(Keys[i].Id);
        w.I32(NextId).I64(FoldedTotal).I32(EventCount);
        for (var i = 0; i < EventCount; i++)
        {
            ref readonly var ev = ref Events[i];
            w.I32(ev.Id).F64(ev.At).U16(ev.Verb).I32(ev.Actor).I32(ev.Target).I32(ev.Owner).I32(ev.Place).I32(ev.Item).I32(ev.Value).U8((byte)ev.Flags);
        }
        w.I32(MatchCount);
        for (var i = 0; i < MatchCount; i++)
        {
            ref readonly var m = ref Matches[i];
            w.U16(m.Pattern).U8(m.Stage).F64(m.Started).F64(m.Last);
            Ints(w, m.Binds);
            Ints(w, m.Events);
        }
        w.I32(DoneCount);
        for (var i = 0; i < DoneCount; i++)
        {
            ref readonly var d = ref Done[i];
            w.U16(d.Pattern).F64(d.At).I32(d.StoryEvent);
            Ints(w, d.Binds);
            Ints(w, d.Events);
        }
        w.I32(Patterns.Length);
        for (var p = 0; p < Patterns.Length; p++)
            w.I32(Counts[p * 5]).I32(Counts[p * 5 + 1]).I32(Counts[p * 5 + 2]).I32(Counts[p * 5 + 3]).I32(Counts[p * 5 + 4]);
        // Version 2: the people.
        w.U64(AgentSeed);
        foreach (var names in (ReadOnlySpan<string[]>)[NeedNames, GoalNames])
        {
            w.I32(names.Length);
            foreach (var name in names)
                w.Str(name);
        }
        w.I32(AgentCount);
        var needs = NeedNames.Length;
        for (var i = 0; i < AgentCount; i++)
        {
            ref readonly var a = ref Agents[i];
            w.I32(a.Entity).U8(a.Scripted ? (byte)1 : (byte)0).F64(a.NeedAt).F64(a.WakeAt).I32(a.Goal).I32(a.GoalTarget).U8(a.Busy ? (byte)1 : (byte)0);
            w.U16(a.Verb).I32(a.Target).I32(a.Owner).I32(a.Place).I32(a.Item).F64(a.From).F64(a.Until).I32(a.FromPlace);
            for (var k = 0; k < needs; k++)
                w.F32(AgentNeeds[i * needs + k]);
            for (var g = i * GoalNames.Length; g < (i + 1) * GoalNames.Length; g++)
                w.F64(AgentGoalLast[g]);
            for (var k = i * StoryAgent.MaxRests; k < (i + 1) * StoryAgent.MaxRests; k++)
                w.I32(AgentRests[k].Goal).I32(AgentRests[k].Target).F64(AgentRests[k].Until);
            w.I32(KnownStart[i + 1] - KnownStart[i]);
            for (var k = KnownStart[i]; k < KnownStart[i + 1]; k++)
            {
                ref readonly var it = ref Known[k];
                w.I32(it.Event).U16(it.Verb).I32(it.Actor).I32(it.Target).I32(it.Owner).I32(it.Place).U8((byte)it.Source).I32(it.From).F32(it.Interest).F64(it.At);
            }
        }
    }

    private static void Ints(SaveWriter w, in Ints8 v)
    {
        for (var i = 0; i < StoryRules.MaxVars; i++)
            w.I32(v[i]);
    }
}
