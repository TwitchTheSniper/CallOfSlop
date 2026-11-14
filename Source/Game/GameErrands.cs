using System.Numerics;
using Brutal.RakNetApi;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// Errands (docs/story/DUTIES.md "Errands"; Karl, 2026-11-14: more side quests that teach the machines at the bases). Someone
/// at the colony asks the player for a small job on one of the works yard's machines (a storylet in Story/Storylets/Errands.json
/// asks, and its end starts the errand: "hook": "errand:ID"); the steps in Story/Errands/ID.json are judged here on the host
/// four times a second, from how things stand: the machine powered, what was handed over tipped in, the product carried.
/// The giver says each step's line, an orange marker shows where, and when it is done the story records an Errand (the
/// player is Taught that machine, and the giver thinks better of them). Optional: the Esc menu drops it, and the storylet
/// asks again later. One errand at a time; the one under way is the story's OnErrand relation, so it saves with the world
/// and starts again from its first step after a load. Per player, a fixed array: nothing allocates in a steady tick.
/// </summary>
public sealed partial class Game
{
    public const byte ErrandFinished = 255; // a run's step once it is done (the panel's last words, then none)
    public const double ErrandLookEvery = 0.25, ErrandDoneShows = 8;

    private struct ErrandRun
    {
        public byte Errand; // index + 1 into ErrandScript.All (0: none)
        public byte Step; // ErrandFinished: done
        public ushort Machine; // the construct, and the machine's part in it
        public int Part;
        public double StepAt;
        public int Base0, Base1, Base2; // what they had when the step began: a carry's or part's count, or each given material after the give
    }

    private readonly ErrandRun[] _er = new ErrandRun[256];
    private readonly Player?[] _erWho = new Player?[256];
    private double _erNextLook;
    private StoryEngine? _erWired;
    private int _erRelOn = -1, _erRelSupplied = -1, _erQShift = -1, _erVErrand = -1;

    // ---- Every peer: this PC's own errand, as the server tells it (Msg.Errand)
    public byte ErrandLocal { get; private set; } // index + 1 (0: none)
    public byte ErrandStepLocal { get; private set; }
    public Vector3 ErrandTarget { get; private set; }
    public bool ErrandHasTarget { get; private set; }
    public double ErrandChangedAt { get; private set; }

    /// <summary>This PC's errand and step, or nulls.</summary>
    public ErrandScript? ErrandNow => ErrandLocal > 0 && ErrandLocal <= ErrandScript.All.Length ? ErrandScript.All[ErrandLocal - 1] : null;
    public ErrandStep? ErrandStepNow => ErrandNow is { } e && ErrandStepLocal < e.Steps.Length ? e.Steps[ErrandStepLocal] : null;

    /// <summary>Both sides, a new match (from <see cref="OpeningReset"/>).</summary>
    private void ErrandReset()
    {
        Array.Clear(_er);
        Array.Clear(_erWho);
        _erWired = null;
        ErrandLocal = 0;
        ErrandHasTarget = false;
    }

    private void ErrandWire(StoryEngine s)
    {
        if (ReferenceEquals(_erWired, s))
            return;
        _erWired = s;
        _erRelOn = s.Rules.Relation("OnErrand")?.Id ?? -1;
        _erRelSupplied = s.Rules.Relation("Supplied")?.Id ?? -1;
        _erQShift = s.Rules.Quality("FirstShift")?.Id ?? -1;
        _erVErrand = s.Rules.VerbId("Errand");
    }

    // ---- The steps --------------------------------------------------------------------------------------------------

    /// <summary>Server, every Sandbox tick: who has done their first shift, where each errand under way stands.</summary>
    private void ErrandTick(double now)
    {
        if (now < _erNextLook || Phase != MatchPhase.Playing || Story is not { } s || ErrandScript.All.Length == 0)
            return;
        _erNextLook = now + ErrandLookEvery;
        ErrandWire(s);
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || p.Id >= MaxPlayers)
                continue;
            if (_erWho[p.Id] != p)
            {
                _erWho[p.Id] = p;
                _er[p.Id] = default;
            }
            var who = StoryWho(p);
            if (who < 0)
                continue;
            // The first shift: the quarry duty done or skipped (or none in this world), after the induction. The errands wait for it.
            if (_erQShift >= 0 && s.World.Quality(who, _erQShift) == 0 && _tutState[p.Id] is TutorialState.Finished or TutorialState.Skipped && QuarryLetsDepot(p.Id))
                s.World.SetQuality(who, _erQShift, 1);
            ref var run = ref _er[p.Id];
            if (run.Errand == 0)
            {
                if (_erRelOn >= 0 && ErrandUnderWay(s, who) is var k and >= 0)
                    ErrandBegin(p, k, now, again: true); // (a saved world, or a rejoin: from the first step)
                continue;
            }
            if (run.Step == ErrandFinished)
            {
                if (now - run.StepAt > ErrandDoneShows)
                {
                    run = default;
                    ErrandSend(p);
                }
                continue;
            }
            var e = ErrandScript.All[run.Errand - 1];
            if (ErrandMachineOf(ref run, e, p.Position) is not { } c)
            {
                ErrandEnd(p, s, who, e.Gone.Length > 0 ? e.Gone : null, now);
                Log($"[errand] {p.Name}: the {e.Machine} for {e.Id} is gone");
                continue;
            }
            if (!p.Alive || !ErrandJudge(p, ref run, c, e.Steps[run.Step], now))
                continue;
            if (run.Step + 1 < e.Steps.Length)
                ErrandSetStep(p, ref run, e, run.Step + 1, now);
            else
                ErrandFinish(p, s, who, ref run, e, now);
        }
    }

    /// <summary>Server: is this step done?</summary>
    private bool ErrandJudge(Player p, ref ErrandRun run, Construct c, ErrandStep st, double now)
    {
        var inv = SandboxInventory(p.Id);
        switch (st.Cue)
        {
            case ErrandCue.Wait:
                return now - run.StepAt >= st.Seconds;
            case ErrandCue.Reach:
                return ErrandSpotAt(c, run.Part, st.Spot) is { } at && Vector3.Distance(p.Position, at) <= st.Amount;
            case ErrandCue.Power:
                return ConstructRecycler.Powered(Constructs, c, run.Part);
            case ErrandCue.Feed:
                for (var g = 0; g < st.Give.Length; g++)
                    if (inv.Materials[RecyclingTable.Current.IdOf(st.Give[g].Material)] >= ErrandBase(ref run, g))
                        return false;
                return true;
            case ErrandCue.Recipe:
            {
                var d = c.DefOf(run.Part);
                if (!d.IsAssembler || ConstructAssembling.RecipeOf(AssemblingTable.Current, c.Parts[run.Part], d) is not { } r)
                    return false;
                return st.Recipe.Length == 0 || r.Output.Name.Equals(st.Recipe, StringComparison.OrdinalIgnoreCase);
            }
            case ErrandCue.Carry:
                return inv.Materials[RecyclingTable.Current.IdOf(st.Material)] > run.Base0;
            case ErrandCue.Part:
                return ErrandPartCount(p, st.Part) > run.Base0;
        }
        return false;
    }

    private static int ErrandBase(ref ErrandRun run, int g) => g == 0 ? run.Base0 : g == 1 ? run.Base1 : run.Base2;

    private int ErrandPartCount(Player p, string name)
    {
        if (ConstructParts.Find(name) is not { } def)
            return 0;
        return SandboxInventory(p.Id).Count(def.Id) + (SandboxHands.Hand(p.Id).Held == def.Id ? 1 : 0);
    }

    /// <summary>Server: on to a step: its line, what it hands over, what they have as it begins, and the client told.</summary>
    private void ErrandSetStep(Player p, ref ErrandRun run, ErrandScript e, int k, double now)
    {
        run.Step = (byte)k;
        run.StepAt = now;
        var st = e.Steps[k];
        var inv = SandboxInventory(p.Id);
        if (st.Say.Length > 0)
            OpeningSay(p, null, e.Giver, st.Say, now);
        // What a step hands over comes once per player, ever: a resume, a drop and a second asking don't hand it over again.
        var hand = st.Give.Length > 0 && ErrandSupply(p, e, k);
        for (var g = 0; g < st.Give.Length; g++)
        {
            var (name, units) = st.Give[g];
            var m = RecyclingTable.Current.IdOf(name);
            if (hand && inv.Materials[m] < units)
            {
                SandboxHands.Notices.Add((p.Id, $"{e.Giver} hands you {units - inv.Materials[m]} {name}"));
                inv.AddMaterial(m, units - inv.Materials[m]);
            }
            var b = inv.Materials[m];
            if (g == 0) run.Base0 = b;
            else if (g == 1) run.Base1 = b;
            else run.Base2 = b;
        }
        if (st.Cue == ErrandCue.Carry)
            run.Base0 = inv.Materials[RecyclingTable.Current.IdOf(st.Material)];
        else if (st.Cue == ErrandCue.Part)
        {
            run.Base0 = ErrandPartCount(p, st.Part);
            if (ConstructParts.Find(st.Part) == null)
                Log($"[errand] REFUSED {e.File} ({st.Id}): no part \"{st.Part}\", so the step can't be done");
        }
        Log($"[errand] {p.Name}: {e.Id} step {k + 1}, {st.Id}");
        ErrandSend(p);
    }

    /// <summary>Server: may this step hand its things over to this player (not yet, by the story's Supplied, a bit a step)? Marks it handed.</summary>
    private bool ErrandSupply(Player p, ErrandScript e, int step)
    {
        if (Story is not { } s || _erRelSupplied < 0 || StoryWho(p) is not (var who and >= 0))
            return true;
        var thing = ErrandThing(s, e);
        var had = s.World.Get(who, _erRelSupplied, thing);
        if ((had & 1 << step) != 0)
            return false;
        s.World.Set(who, _erRelSupplied, thing, had | 1 << step);
        return true;
    }

    /// <summary>Server: the errand starts (or starts again after a load) on the nearest of its machines to the colony.</summary>
    private void ErrandBegin(Player p, int k, double now, bool again)
    {
        if (Story is not { } s || StoryWho(p) is not (var who and >= 0))
            return;
        var e = ErrandScript.All[k];
        ref var run = ref _er[p.Id];
        run = default;
        if (ErrandFindMachine(e, p.Position) is not var (id, part))
        {
            Log($"[errand] {p.Name}: no {e.Machine} within {e.Reach:0} m of the colony for {e.Id}");
            if (!again)
                OpeningSay(p, null, e.Giver, e.Gone.Length > 0 ? e.Gone : $"Never mind. There's no {e.Machine} here after all.", now);
            ErrandEnd(p, s, who, null, now);
            return;
        }
        (run.Errand, run.Machine, run.Part) = ((byte)(k + 1), id, part);
        if (_erRelOn >= 0)
            s.World.Set(who, _erRelOn, ErrandThing(s, e), 1);
        Log($"[errand] {p.Name} {(again ? "goes back to" : "takes on")} {e.Id} at the {e.Machine} (construct {id})");
        ErrandSetStep(p, ref run, e, 0, now);
    }

    /// <summary>Server: a storylet's "errand:ID" hook (GameStoryStorylets.cs).</summary>
    private void ErrandOffer(Player p, string id, double now)
    {
        var k = ErrandScript.IndexOf(id);
        if (k < 0)
        {
            Log($"[errand] REFUSED hook errand:{id}: no such errand in Story/Errands");
            return;
        }
        ref var run = ref _er[p.Id];
        if (_erWho[p.Id] == p && run.Errand != 0 && run.Step != ErrandFinished)
        {
            SandboxHands.Notices.Add((p.Id, $"Finish {ErrandScript.All[run.Errand - 1].Title.ToLowerInvariant()} first"));
            return;
        }
        _erWho[p.Id] = p;
        ErrandBegin(p, k, now, again: false);
    }

    private void ErrandFinish(Player p, StoryEngine s, int who, ref ErrandRun run, ErrandScript e, double now)
    {
        if (e.Thanks.Length > 0)
            OpeningSay(p, null, e.Giver, e.Thanks, now);
        if (_erVErrand >= 0)
            s.Record(_erVErrand, who, s.World.Find(e.Giver), place: StoryPlace(), item: ErrandThing(s, e));
        if (_erRelOn >= 0)
            s.World.Set(who, _erRelOn, ErrandThing(s, e), 0);
        run.Step = ErrandFinished;
        run.StepAt = now;
        Log($"[errand] {p.Name}: {e.Id} done");
        ErrandSend(p);
    }

    /// <summary>Server: the errand ends undone (dropped, or its machine gone): a line, if any, and nothing under way.</summary>
    private void ErrandEnd(Player p, StoryEngine s, int who, string? line, double now)
    {
        ref var run = ref _er[p.Id];
        if (run.Errand > 0 && line != null)
            OpeningSay(p, null, ErrandScript.All[run.Errand - 1].Giver, line, now);
        if (_erRelOn >= 0 && ErrandUnderWay(s, who) is var k and >= 0)
            s.World.Set(who, _erRelOn, ErrandThing(s, ErrandScript.All[k]), 0);
        run = default;
        ErrandSend(p);
    }

    /// <summary>Server: the Esc menu drops the errand under way (the storylet asks again later).</summary>
    private void ErrandDrop(Player p)
    {
        ref var run = ref _er[p.Id];
        if (run.Errand == 0 || run.Step == ErrandFinished || Story is not { } s || StoryWho(p) is not (var who and >= 0))
            return;
        var e = ErrandScript.All[run.Errand - 1];
        SandboxHands.Notices.Add((p.Id, $"You leave {e.Title.ToLowerInvariant()} for now"));
        Log($"[errand] {p.Name} drops {e.Id}");
        ErrandEnd(p, s, who, null, SandboxNow);
    }

    /// <summary>This PC's player: drop the errand (the host's own goes straight in).</summary>
    public void ErrandAskDrop()
    {
        if (IsServer)
        {
            if (Local is { } me)
                ErrandDrop(me);
            return;
        }
        SandboxSend(CActKind.ErrandDrop);
    }

    /// <summary>Host, the window tool ("errand ID"): start an errand for this PC's player as its storylet's hook would. False: not the host, or no such errand.</summary>
    public bool ErrandDevStart(string id)
    {
        if (!IsServer || Local is not { } me || ErrandScript.IndexOf(id) < 0)
            return false;
        ErrandOffer(me, id, SandboxNow);
        return true;
    }

    /// <summary>The window tool ("errand"): where this PC's errand stands, and on the host what each step's judge sees.</summary>
    public string ErrandDevState()
    {
        var line = ErrandNow is { } e ? $"{e.Id} step {(ErrandStepLocal == ErrandFinished ? "done" : (ErrandStepLocal + 1).ToString())}{(ErrandHasTarget ? $", target {ErrandTarget.X:0.0},{ErrandTarget.Y:0.0},{ErrandTarget.Z:0.0}" : "")}" : "none";
        if (IsServer && Local is { } me && _er[me.Id].Errand > 0 && Constructs.Get(_er[me.Id].Machine) is { } c)
        {
            ref var run = ref _er[me.Id];
            line += $"; host: the {c.DefOf(run.Part).Name} on construct {c.Id}, powered {ConstructRecycler.Powered(Constructs, c, run.Part)}, bases {run.Base0}/{run.Base1}/{run.Base2}";
            var d = c.DefOf(run.Part);
            if (d.IsAssembler)
            {
                var list = AssemblingTable.Current.ByMachine[d.AsmMachine];
                var want = run.Step < ErrandScript.All[run.Errand - 1].Steps.Length ? ErrandScript.All[run.Errand - 1].Steps[run.Step].Recipe : "";
                line += $"; recipe now {ConstructAssembling.RecipeOf(AssemblingTable.Current, c.Parts[run.Part], d)?.Output.Name ?? "none"}{(want.Length > 0 ? $", {want} is setting {Array.FindIndex(list, r => r.Output.Name == want) + 1}" : "")}; Wall Clocks carried {ErrandPartCount(me, "Wall Clock")}";
            }
            if (d.IsRefiner && Constructs.StoreOf(c, run.Part) is { } st)
                line += $"; {ConstructRefining.Status(d, st)}";
            for (var k = c.PartCount - 1; k >= 0; k--)
                if (c.DefOf(k) is { IsBin: true, IsAssembler: false, IsRefiner: false } && Constructs.StoreOf(c, k) is { } bin)
                {
                    var nth = 0;
                    foreach (var o in Constructs.Bodies)
                    {
                        for (var j = 0; j < o.PartCount && !(o == c && j > k); j++)
                            if (o.DefOf(j).Name == c.DefOf(k).Name)
                                nth++;
                        if (o == c)
                            break;
                    }
                    line += $"; last bin {c.DefOf(k).Name}#{nth} holds {bin.Amount} {RecyclingTable.Current.NameOf(bin.Material)}; steel carried {SandboxInventory(me.Id).Materials[RecyclingTable.Current.IdOf("steel")]}";
                    break;
                }
        }
        if (Story is { } s && Local is { } p && StoryWho(p) is var who and >= 0)
            line += $"; FirstShift {(_erQShift >= 0 ? s.World.Quality(who, _erQShift) : -1)}, under way {ErrandUnderWay(s, who)}";
        return line;
    }

    // ---- The machine and the story's facts ----------------------------------------------------------------------------

    /// <summary>The errand the story says is under way for this player (its index), or -1.</summary>
    private int ErrandUnderWay(StoryEngine s, int who)
    {
        if (_erRelOn < 0)
            return -1;
        var (a, b) = s.World.Range(who, _erRelOn);
        var pairs = s.World[who].Pairs;
        for (var i = a; i < b; i++)
        {
            var name = s.World.NameOf(pairs[i].Target);
            var all = ErrandScript.All;
            for (var k = 0; k < all.Length; k++)
                if (all[k].Fact.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return k;
        }
        return -1;
    }

    /// <summary>The Thing an errand teaches ("the Foundry"), as a storylet's quoted name finds it (StoryletRunner.NamedThing).</summary>
    private static int ErrandThing(StoryEngine s, ErrandScript e)
    {
        var id = s.World.Find(e.Fact);
        return id >= 0 ? id : s.World.Get(StoryKind.Thing, StoryFactions.Key("fact:" + e.Fact), e.Fact, s.Time);
    }

    /// <summary>The run's machine, looked for again if it was taken apart or moved off (null: there is none now).</summary>
    private Construct? ErrandMachineOf(ref ErrandRun run, ErrandScript e, Vector3 player)
    {
        if (Constructs.Get(run.Machine) is { } c && run.Part < c.PartCount && !c.Parts[run.Part].Broken && c.DefOf(run.Part).Name == e.Machine)
            return c;
        if (ErrandFindMachine(e, player) is not var (id, part) || Constructs.Get(id) is not { } again)
            return null;
        (run.Machine, run.Part) = (id, part); // (rebuilt, or another of its kind: the marker follows)
        return again;
    }

    /// <summary>The nearest working machine of the errand's kind to the colony's middle (or the player, with no colony), within its reach.</summary>
    private (ushort Id, int Part)? ErrandFindMachine(ErrandScript e, Vector3 player)
    {
        var from = _opColony != 0 ? _opMiddle : player;
        (ushort, int)? best = null;
        var bestD = e.Reach * e.Reach;
        foreach (var c in Constructs.Bodies)
        {
            if (c.Id == _qrWorks)
                continue; // (the quarry's are the first duty's)
            for (var k = 0; k < c.PartCount; k++)
            {
                if (c.Parts[k].Broken || c.DefOf(k).Name != e.Machine)
                    continue;
                var d = Vector3.DistanceSquared(c.SegToWorld(c.Parts[k].Seg, c.Parts[k].Pos), from);
                if (d < bestD)
                    (best, bestD) = ((c.Id, k), d);
            }
        }
        return best;
    }

    /// <summary>A spot on the machine's construct now (null: there isn't one).</summary>
    private static Vector3? ErrandSpotAt(Construct c, int part, ErrandSpot spot)
    {
        Vector3 At(int k) => c.SegToWorld(c.Parts[k].Seg, c.Parts[k].Pos);
        switch (spot)
        {
            case ErrandSpot.Machine:
                return At(part);
            case ErrandSpot.Genset:
                for (var k = 0; k < c.PartCount; k++)
                    if (c.DefOf(k).Name.Contains("Genset", StringComparison.Ordinal))
                        return At(k);
                return At(part);
            case ErrandSpot.In:
                for (var k = 0; k < c.PartCount; k++)
                    if (c.DefOf(k) is { IsBin: true, IsAssembler: false, IsRefiner: false })
                        return At(k);
                return At(part);
            case ErrandSpot.Out:
                for (var k = c.PartCount - 1; k >= 0; k--)
                    if (c.DefOf(k) is { IsBin: true, IsAssembler: false, IsRefiner: false })
                        return At(k);
                return At(part);
        }
        return null;
    }

    // ---- The wire (Msg.Errand) ------------------------------------------------------------------------------------------

    /// <summary>Server: their errand, to them (<see cref="Msg.Errand"/>: u8 errand + 1, u8 step, u8 has a target, vec the target). The host's own goes straight in.</summary>
    private void ErrandSend(Player p)
    {
        ref var run = ref _er[p.Id];
        Vector3? target = null;
        if (run.Errand > 0 && run.Step != ErrandFinished && Constructs.Get(run.Machine) is { } c && run.Part < c.PartCount)
            target = ErrandSpotAt(c, run.Part, ErrandScript.All[run.Errand - 1].Steps[run.Step].Spot);
        if (p.Id == LocalId)
        {
            ErrandHear(run.Errand, run.Step, target);
            return;
        }
        if (GuidOf(p) is not { } guid)
            return;
        var w = SbW;
        w.Begin(Msg.Errand).U8(run.Errand).U8(run.Step).U8(target != null ? (byte)1 : (byte)0).Vec(target ?? default);
        Net.SendTo(guid, w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: a <see cref="Msg.Errand"/> (after its id).</summary>
    private void ReadErrand(ref PacketReader r)
    {
        if (!r.Has(3))
            return;
        var errand = r.U8();
        var step = r.U8();
        var has = r.U8() != 0;
        var at = r.Vec();
        ErrandHear(errand <= ErrandScript.All.Length ? errand : (byte)0, step, has && float.IsFinite(at.X + at.Y + at.Z) ? at : null);
    }

    private void ErrandHear(byte errand, byte step, Vector3? target)
    {
        if (errand != ErrandLocal || step != ErrandStepLocal)
            ErrandChangedAt = Brutal.GlfwApi.Glfw.Time;
        ErrandLocal = errand;
        ErrandStepLocal = step;
        ErrandHasTarget = target != null;
        ErrandTarget = target ?? default;
    }
}
