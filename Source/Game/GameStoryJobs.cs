using System.Numerics;
using Brutal.RakNetApi;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>Either way, <see cref="Msg.Jobs"/>: server -> one client the jobs window's state; client -> server a take or a drop.</summary>
public enum JobOp : byte
{
    State, // server -> client: the whole view (WriteJobs)
    Take, // client -> server: u8 faction, u8 job
    Drop, // client -> server: u8 faction
}

/// <summary>The jobs window's view of one job, as the server sent it (<see cref="Game.JobsView"/>).</summary>
public sealed class JobRow
{
    public string Name = "", Ticket = "", About = "", What = "", Pay = "", Note = "";
    public bool Open, Held; // may be taken now; this party holds it
}

/// <summary>The jobs window's view of one story faction (its standing with you, its tier, its jobs).</summary>
public sealed class JobFaction
{
    public string Name = "", Tier = "";
    public int Standing;
    public JobRow[] Jobs = [];
}

/// <summary>
/// Faction jobs on the game's side (docs/story/STORY.md "Jobs"; the engine's side: Source/Story/StoryFactionJobs.cs, data
/// "jobs" in Story/Factions). No money: a job pays standing, and through it the faction's technologies. A party (a
/// player's faction, or the player) takes one job of each story faction at a time in the Faction window (F2).
/// <list type="bullet">
/// <item>Supply: fill a Requisition Crate the party owns with what the job asks for: parts in its box, bulk in its bin, cut
/// pieces (an Extruder's or a Wheel Former's) lying in its box. Once it holds the lot the crate is sealed for the courier,
/// who comes a little later, takes exactly what was asked and signs the job off. A "paperwork" job's first crate is sent
/// back over the forms.</item>
/// <item>Build: own a construct holding the parts it asks for; the faction inspects it and it stays yours.</item>
/// </list>
/// The faction speaks in its own voice ("[MINISTRY MEMO] ...") to the party's players. Host only: clients see the window
/// the server sends them (JobOp.State) and ask with JobOp.Take and JobOp.Drop.
/// </summary>
public sealed partial class Game
{
    public const float CourierSeconds = 20f; // from a crate holding the lot to the courier taking it
    public const float RefusedSeconds = 90f; // a crate sent back over the paperwork is looked at again after this
    private const int JobCheckEvery = 2; // seconds between looks at crates and builds

    /// <summary>A job a party holds, as the server is judging it (keyed by party and faction).</summary>
    private sealed class JobWatch
    {
        public int Party, Faction, Job, Actor; // Actor: a player of the party here (who it is signed off to)
        public readonly int[] Have = new int[FactionRules.MaxNeeds];
        public bool Full; // the crate holds the lot (or the build is there)
        public ushort Crate; // the crate's body (0: none) ...
        public int CratePart = -1; // ... and part
        public double CourierAt, LookAgainAt; // when the courier comes; a crate sent back is looked at again then
        public int Version; // up on every change to Have or Full (the window)
        public bool Seen; // still held, this look
    }

    private readonly Dictionary<(int Party, int Faction), JobWatch> _jobWatch = new();
    private readonly List<JobWatch> _jobGone = new(8);
    private readonly int[] _jobKey = new int[256]; // per player slot: a key of what their window shows (sent again when it changes)
    private PacketWriter? _jobW, _jobStateW;
    private readonly List<(Construct C, int Part)> _jobCrates = new(8);
    private readonly Player?[] _jobKeyFor = new Player?[256]; // ...and whom it was for (a slot taken by someone new is sent afresh)

    /// <summary>This PC's jobs window: the story factions with jobs, as the server last sent them (empty without a story).</summary>
    public JobFaction[] JobsView = [];
    public int JobsVersion;

    /// <summary>Forgets every job watch (a new story, a new world).</summary>
    private void StoryJobsForget()
    {
        _jobWatch.Clear();
        Array.Fill(_jobKey, 0);
        Array.Fill(_jobKeyFor, null);
        JobsView = [];
        JobsVersion++;
    }

    /// <summary>Once a second (from <see cref="StoryTick"/>): what the parties hold, crates and builds looked at, the couriers, and each player's window.</summary>
    private void StoryJobsTick(StoryEngine s, double now)
    {
        if (ServerTick % World.TickRate != 7)
            return;
        var fs = s.Rules.Factions.Factions;
        var any = false;
        foreach (var f in fs)
            any |= f.Jobs.Length > 0;
        if (!any)
            return;
        foreach (var w in _jobWatch.Values)
            w.Seen = false;
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || StoryWho(p) is not (var who and >= 0))
                continue;
            var party = s.Factions.Party(who);
            for (var f = 0; f < fs.Length; f++)
            {
                if (fs[f].Jobs.Length == 0 || s.Factions.JobOf(party, f) is not (var j and >= 0))
                    continue;
                if (!_jobWatch.TryGetValue((party, f), out var w) || w.Job != j)
                    _jobWatch[(party, f)] = w = new JobWatch { Party = party, Faction = f, Job = j };
                w.Actor = who;
                w.Seen = true;
            }
        }
        _jobGone.Clear();
        foreach (var w in _jobWatch.Values)
            if (!w.Seen)
                _jobGone.Add(w);
        foreach (var w in _jobGone)
            _jobWatch.Remove((w.Party, w.Faction)); // (dropped, done elsewhere, or nobody of the party here: looked at again when they come)
        if (ServerTick / World.TickRate % JobCheckEvery == 0)
            foreach (var w in _jobWatch.Values.ToArray())
                JobLook(s, w, now);
        JobsFinishedTick(s, now);
        foreach (var p in Players.Values)
            if (p.Bot == null)
                JobsSendIfChanged(s, p);
    }

    // ---- Judging ------------------------------------------------------------------------------------------------

    /// <summary>Is this owner byte one of the party's (a player in it, here or away)?</summary>
    private bool JobOwns(StoryEngine s, byte owner, int party) => StoryByte(owner) is var e and >= 0 && s.Factions.Party(e) == party;

    private void JobLook(StoryEngine s, JobWatch w, double now)
    {
        var def = s.Rules.Factions.Factions[w.Faction];
        var job = def.Jobs[w.Job];
        var was = w.Full;
        Span<int> had = stackalloc int[FactionRules.MaxNeeds];
        w.Have.AsSpan().CopyTo(had);
        if (job.Kind == JobKind.Build)
        {
            w.Full = JobBuildLook(s, w, job);
            if (w.Full)
            {
                JobSay(s, w.Party, def, $"Inspected and passed: {job.Name}.");
                s.Factions.FinishJob(w.Actor, w.Faction, w.Job);
                return;
            }
        }
        else
        {
            w.Full = JobCrateLook(s, w, job);
            if (w.Full && !was)
            {
                w.CourierAt = now + CourierSeconds;
                if (now >= w.LookAgainAt)
                    JobSay(s, w.Party, def, $"Your crate for {job.Name} is complete and sealed. The courier is on the way: leave it be.");
                if (Constructs.Get(w.Crate) is { } sealedCrate)
                    StandInCourierCalled(w.Party, w.Faction, sealedCrate, w.CratePart, Math.Max(w.CourierAt, w.LookAgainAt) - now); // STAND-IN: the courier's flyer (Source/StandIns)
            }
            else if (!w.Full && was && now < w.CourierAt)
                JobSay(s, w.Party, def, $"Your crate for {job.Name} is short again. The courier will wait until it isn't.");
            if (w.Full && now >= w.CourierAt && now >= w.LookAgainAt)
                JobCourier(s, w, def, job, now);
        }
        if (w.Full != was || !had.SequenceEqual(w.Have))
            w.Version++;
    }

    /// <summary>A build job: the best construct of the party's, by how much of the list it holds; true when one holds it all.</summary>
    private bool JobBuildLook(StoryEngine s, JobWatch w, FactionJob job)
    {
        Span<int> best = stackalloc int[FactionRules.MaxNeeds];
        best.Clear();
        var bestScore = -1;
        Span<int> have = stackalloc int[FactionRules.MaxNeeds];
        foreach (var c in Constructs.Bodies)
        {
            if (!JobOwns(s, c.Owner, w.Party))
                continue;
            have.Clear();
            for (var i = 0; i < c.PartCount; i++)
            {
                var name = c.Parts[i].D.Name;
                for (var k = 0; k < job.Needs.Length; k++)
                    if (job.Needs[k].Part.Equals(name, StringComparison.OrdinalIgnoreCase))
                        have[k]++;
            }
            var score = 0;
            for (var k = 0; k < job.Needs.Length; k++)
                score += Math.Min(have[k], job.Needs[k].Count);
            if (score > bestScore)
            {
                bestScore = score;
                have.CopyTo(best);
            }
        }
        var full = job.Needs.Length > 0;
        for (var k = 0; k < job.Needs.Length; k++)
        {
            w.Have[k] = Math.Min(best[k], job.Needs[k].Count);
            full &= w.Have[k] >= job.Needs[k].Count;
        }
        return full;
    }

    /// <summary>A supply job: the party's Requisition Crates, the fullest one counted; true when one holds the lot.</summary>
    private bool JobCrateLook(StoryEngine s, JobWatch w, FactionJob job)
    {
        _jobCrates.Clear();
        foreach (var c in Constructs.Bodies)
            if (JobOwns(s, c.Owner, w.Party))
                for (var i = 0; i < c.PartCount; i++)
                    if (c.Parts[i].D.IsRequisition && _jobCrates.Count < 32)
                        _jobCrates.Add((c, i));
        Span<int> best = stackalloc int[FactionRules.MaxNeeds];
        best.Clear();
        Span<int> have = stackalloc int[FactionRules.MaxNeeds];
        var bestScore = -1;
        (w.Crate, w.CratePart) = (0, -1);
        foreach (var (c, i) in _jobCrates)
        {
            CrateCount(c, i, job, have, take: false);
            var score = 0;
            for (var k = 0; k < job.Needs.Length; k++)
                score += Math.Min(1000, have[k] * 1000 / Math.Max(1, NeedAmount(job.Needs[k])));
            if (score > bestScore)
            {
                bestScore = score;
                have.CopyTo(best);
                (w.Crate, w.CratePart) = (c.Id, i);
            }
        }
        var full = _jobCrates.Count > 0;
        for (var k = 0; k < job.Needs.Length; k++)
        {
            w.Have[k] = Math.Min(best[k], NeedAmount(job.Needs[k]));
            full &= w.Have[k] >= NeedAmount(job.Needs[k]);
        }
        return full;
    }

    /// <summary>What a need's count is measured in: parts or pieces, or kilograms of bulk.</summary>
    private static int NeedAmount(JobNeed n) => n.Part.Length == 0 && n.Piece.Length == 0 ? n.Kg : n.Count;

    /// <summary>
    /// What a crate holds towards a job's needs (parts in its box, bulk in kilograms, pieces lying in its box). With
    /// <paramref name="take"/>, what was asked is taken out (the courier): parts and bulk from the store, pieces as bodies.
    /// </summary>
    private void CrateCount(Construct c, int part, FactionJob job, Span<int> have, bool take)
    {
        have.Clear();
        var st = Constructs.StoreOf(c, part);
        var rt = RecyclingTable.Current;
        for (var k = 0; k < job.Needs.Length; k++)
        {
            var n = job.Needs[k];
            if (st == null || n.Piece.Length > 0)
                continue;
            if (n.Part.Length > 0)
            {
                if (ConstructParts.Find(n.Part) is not { } d || st.KindIndex(d.Id) is not (var ki and >= 0))
                    continue;
                have[k] = st.KindCount![ki];
                if (take)
                    Constructs.TakeKind(st, d.Id, Math.Min(have[k], n.Count));
            }
            else if (st.Material != 0 && st.Material == rt.IdOf(n.Material))
            {
                var kgPer = rt.WeightOf(st.Material);
                have[k] = (int)Math.Min(int.MaxValue, st.Amount * (double)kgPer);
                if (take)
                    Constructs.TakeBulk(st, Math.Min(st.Amount, (int)Math.Ceiling(n.Kg / kgPer)));
            }
        }
        // Pieces: loose bodies of one cut piece lying in the crate's box (PieceInBox).
        var wantsPieces = false;
        foreach (var n in job.Needs)
            wantsPieces |= n.Piece.Length > 0;
        if (!wantsPieces)
            return;
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        ConstructStoreVolume.Usable(c, part, out var min, out var max);
        var iq = Quaternion.Conjugate(q);
        Construct? gone = null;
        for (var b = 0; b < Constructs.Bodies.Count; b++)
        {
            var body = Constructs.Bodies[b];
            if (ReferenceEquals(body, c) || body.PartCount != 1 || body.Anchored || body.Parts[0].D is not { IsExtrusion: true } pd || pd.ExKey.Growing)
                continue;
            if (!PieceInBox(body, t, iq, min, max))
                continue;
            for (var k = 0; k < job.Needs.Length; k++)
            {
                var n = job.Needs[k];
                if (n.Piece.Length == 0 || have[k] >= n.Count || !PieceFits(n, pd.ExKey, rt))
                    continue;
                have[k]++;
                if (take)
                    gone = body;
                break;
            }
            if (gone != null)
            {
                Constructs.Remove(gone); // (the courier takes it: one body less, so the same index is the next body)
                gone = null;
                b--;
            }
        }
    }

    /// <summary>
    /// Does a piece lie in a crate's box (its frame <paramref name="t"/>, <paramref name="iq"/>)? Its middle, or a point near
    /// either end of its longest side: a caber longer than the box counts laid in it with an end sticking out.
    /// </summary>
    private static bool PieceInBox(Construct body, Vector3 t, Quaternion iq, Vector3 min, Vector3 max)
    {
        body.PartBounds(0, out var bmin, out var bmax);
        var mid = (bmin + bmax) * 0.5f;
        var half = (bmax - bmin) * 0.5f;
        var along = half.X >= half.Y && half.X >= half.Z ? new Vector3(half.X, 0, 0) : half.Y >= half.Z ? new Vector3(0, half.Y, 0) : new Vector3(0, 0, half.Z);
        for (var k = -1; k <= 1; k++)
        {
            var local = Vector3.Transform(body.ToWorld(mid + along * (0.8f * k)) - t, iq);
            if (local.X >= min.X && local.Y >= min.Y && local.Z >= min.Z && local.X <= max.X && local.Y <= max.Y && local.Z <= max.Z)
                return true;
        }
        return false;
    }

    /// <summary>Is this piece what a need asks for: its section, material, length and size across?</summary>
    private static bool PieceFits(JobNeed n, ExtrusionKey k, RecyclingTable rt)
    {
        var shape = k.Shape switch
        {
            ExShape.Round => "round",
            ExShape.Pipe => "pipe",
            ExShape.Wheel => "wheel",
            ExShape.Section => Extrusion.PresetOf(k.Mask) is var p and >= 0 ? Extrusion.Presets[p].Id : "custom",
            _ => "other",
        };
        if (n.Piece != "any" && n.Piece != shape || shape == "other")
            return false;
        if (n.Material.Length > 0 && k.Material != rt.IdOf(n.Material))
            return false;
        var length = k.Shape == ExShape.Wheel ? k.W / 100f : k.L / 100f; // (a wheel: its width)
        var across = k.Shape == ExShape.Wheel ? k.H / 100f : Math.Min(k.W, k.H) / 100f; // (a wheel: its diameter)
        return length + 0.001f >= n.Length && across + 0.001f >= n.Across;
    }

    /// <summary>The courier at a full crate: sends it back over the paperwork once (a "paperwork" job), else takes what was asked and signs the job off.</summary>
    private void JobCourier(StoryEngine s, JobWatch w, FactionDef def, FactionJob job, double now)
    {
        if (Constructs.Get(w.Crate) is not { } c || w.CratePart < 0 || w.CratePart >= c.PartCount || !c.Parts[w.CratePart].D.IsRequisition)
            return;
        if (s.Factions.SendsBack(w.Party, w.Faction, w.Job))
        {
            JobSay(s, w.Party, def, job.Refused.Length > 0 ? job.Refused : $"The courier has sent your crate for {job.Name} back: the paperwork is wrong. Leave it as it is; they will look again.");
            w.LookAgainAt = now + RefusedSeconds;
            w.Full = false; // (so the next look, after the wait, seals it again)
            StandInCourierDone(w.Party, w.Faction, 3); // STAND-IN: the courier's flyer goes (Source/StandIns)
            return;
        }
        Span<int> took = stackalloc int[FactionRules.MaxNeeds];
        CrateCount(c, w.CratePart, job, took, take: true);
        Console.WriteLine($"[story] {def.Name}'s courier collected {job.Name} from {s.World.NameOf(w.Party)}");
        JobSay(s, w.Party, def, def.Courier.Length > 0 ? def.Courier : $"{def.Name}'s courier has collected your crate.");
        s.Factions.FinishJob(w.Actor, w.Faction, w.Job);
        _jobWatch.Remove((w.Party, w.Faction));
        StandInCourierDone(w.Party, w.Faction, 3); // STAND-IN: the courier's flyer goes (Source/StandIns)
    }

    /// <summary>Jobs the factions signed off: the job's line in the faction's voice and a HUD line to the party.</summary>
    private void JobsFinishedTick(StoryEngine s, double now)
    {
        var done = s.Factions.Finished;
        for (var k = 0; k < done.Count; k++)
        {
            var (f, j, party, _) = done[k];
            var def = s.Factions.Def(f);
            var job = def.Jobs[j];
            if (job.Done.Length > 0)
                JobSay(s, party, def, job.Done);
            var standing = s.Factions.Standing(f, party);
            JobHud(s, party, $"{def.Name}: job done, {job.Name} (+{job.Pay.Standing} standing; now {standing}, {def.TierName(standing)})");
            _jobWatch.Remove((party, f));
        }
        done.Clear();
    }

    /// <summary>A line in the faction's voice ("[MINISTRY MEMO] ...") to every player of the party here.</summary>
    private void JobSay(StoryEngine s, int party, FactionDef def, string text)
    {
        var prefix = $"[{(def.Voice.Length > 0 ? def.Voice : def.Name.ToUpperInvariant())}] ";
        var room = ChatRules.MaxLength - prefix.Length;
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || _storyWho[p.Id] is not (var who and >= 0) || s.Factions.Party(who) != party)
                continue;
            var rest = text.AsSpan().Trim();
            while (rest.Length > 0) // (a chat line holds 120 characters: a long one goes as several, broken between words)
            {
                var cut = rest.Length <= room ? rest.Length : rest[..room].LastIndexOf(' ') is var sp and > 0 ? sp : room;
                OfficeLine(p.Position, 1, prefix + rest[..cut].ToString(), Math.Max(0, _storyLast), p);
                rest = rest[cut..].TrimStart();
            }
        }
    }

    private void JobHud(StoryEngine s, int party, string text)
    {
        foreach (var p in Players.Values)
            if (p.Bot == null && _storyWho[p.Id] is var who and >= 0 && s.Factions.Party(who) == party)
                SandboxHands.Notices.Add((p.Id, text));
    }

    // ---- Taking and dropping --------------------------------------------------------------------------------------

    /// <summary>Server: a player asks to take or drop a job.</summary>
    private void JobHear(Player p, JobOp op, int f, int j)
    {
        if (Story is not { } s || p.Bot != null || StoryWho(p) is not (var who and >= 0) || (uint)f >= (uint)s.Factions.Count)
            return;
        var def = s.Factions.Def(f);
        var party = s.Factions.Party(who);
        if (op == JobOp.Take)
        {
            if (s.Factions.TakeJob(who, f, j) is { } why)
            {
                SandboxHands.Notices.Add((p.Id, $"Can't take that job: {why}"));
                return;
            }
            var job = def.Jobs[j];
            Console.WriteLine($"[story] {p.Name} took {def.Name}'s job {job.Name}");
            JobSay(s, party, def, (job.Ticket.Length > 0 ? $"Ticket {job.Ticket}: " : "") + (job.Taken.Length > 0 ? job.Taken : $"{job.Name}. {job.Text}."));
            JobHud(s, party, $"{p.Name} took {def.Name}'s job: {job.Name}");
        }
        else if (op == JobOp.Drop && s.Factions.DropJob(who, f) is var dropped and >= 0)
        {
            _jobWatch.Remove((party, f));
            JobHud(s, party, $"{p.Name} dropped {def.Name}'s job: {def.Jobs[dropped].Name}");
        }
        _jobKey[p.Id] = 0;
    }

    /// <summary>This PC's player takes (or drops, <paramref name="job"/> -1) a story faction's job.</summary>
    public void JobAsk(int faction, int job)
    {
        var op = job < 0 ? JobOp.Drop : JobOp.Take;
        if (IsServer)
        {
            if (Local is { } me)
                JobHear(me, op, faction, job);
            return;
        }
        var w = _jobW ??= new PacketWriter(16);
        w.Begin(Msg.Jobs).U8((byte)op).U8((byte)faction).U8((byte)Math.Max(0, job));
        Net.SendToServer(w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>A <see cref="Msg.Jobs"/> (after its id): the server hears a take or a drop, a client its window.</summary>
    private void OnJobsPacket(RakNetGUID sender, ref PacketReader r)
    {
        if (!r.Has(1))
            return;
        var op = (JobOp)r.U8();
        if (IsServer)
        {
            if (op != JobOp.State && r.Has(2) && _clients.TryGetValue(sender.G, out var cl) && Players.TryGetValue(cl.Id, out var p))
            {
                var f = r.U8();
                JobHear(p, op, f, r.U8());
            }
        }
        else if (op == JobOp.State)
            ReadJobs(ref r);
    }

    // ---- The window's state ----------------------------------------------------------------------------------------

    /// <summary>A key of what a player's window shows: their party, the factions' standing and jobs with them, the watches' versions, the minute.</summary>
    private int JobKeyOf(StoryEngine s, int party)
    {
        var h = new HashCode();
        h.Add(party);
        h.Add((int)(s.Time / 60));
        for (var f = 0; f < s.Factions.Count; f++)
        {
            if (s.Factions.Def(f).Jobs.Length == 0)
                continue;
            h.Add(s.Factions.Standing(f, party));
            var j = s.Factions.JobOf(party, f);
            h.Add(j);
            if (j >= 0 && _jobWatch.TryGetValue((party, f), out var w))
                h.Add(w.Version);
        }
        var k = h.ToHashCode();
        return k == 0 ? 1 : k;
    }

    private void JobsSendIfChanged(StoryEngine s, Player p)
    {
        if (StoryWho(p) is not (var who and >= 0))
            return;
        var party = s.Factions.Party(who);
        var key = JobKeyOf(s, party);
        if (_jobKey[p.Id] == key && ReferenceEquals(_jobKeyFor[p.Id], p))
            return;
        _jobKeyFor[p.Id] = p;
        _jobKey[p.Id] = key;
        var w = _jobStateW ??= new PacketWriter(32768);
        WriteJobs(s, party, w);
        if (p.Id == LocalId)
        {
            var r = new PacketReader(w.Span[1..]);
            r.U8();
            ReadJobs(ref r);
            return;
        }
        if (!Net.IsOffline && GuidOf(p) is { } g)
            Net.SendTo(g, w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>
    /// <see cref="Msg.Jobs"/> State: u8 n factions with jobs, each (text name, i8 standing, text tier, u8 jobs, each (text name,
    /// text ticket, text about, text what, text pay, u8 flags: 1 open, 2 held, text note: why not, or how far along)).
    /// </summary>
    private void WriteJobs(StoryEngine s, int party, PacketWriter w)
    {
        w.Begin(Msg.Jobs).U8((byte)JobOp.State);
        byte n = 0;
        for (var f = 0; f < s.Factions.Count && f < FactionTable.MaxStory; f++)
            n += (byte)(s.Factions.Def(f).Jobs.Length > 0 ? 1 : 0);
        w.U8(n);
        for (var f = 0; f < s.Factions.Count && f < FactionTable.MaxStory; f++)
        {
            var def = s.Factions.Def(f);
            if (def.Jobs.Length == 0)
                continue;
            var standing = s.Factions.Standing(f, party);
            var held = s.Factions.JobOf(party, f);
            w.Str(def.Name).U8((byte)(sbyte)Math.Clamp(standing, -100, 100)).Str(def.TierName(standing)).U8((byte)def.Jobs.Length);
            for (var j = 0; j < def.Jobs.Length; j++)
            {
                var job = def.Jobs[j];
                var why = held == j ? null : s.Factions.TakeRefusal(party, f, j);
                var note = held == j ? JobProgress(job, _jobWatch.GetValueOrDefault((party, f))) : why ?? "";
                var pay = $"+{job.Pay.Standing} standing" + (job.Tier >= 0 ? $", from {def.Tiers[job.Tier].Name}" : "");
                w.Str(job.Name).Str(job.Ticket).Str(job.About).Str(job.Text).Str(pay).U8((byte)((why == null && held != j ? 1 : 0) | (held == j ? 2 : 0))).Str(note);
            }
        }
    }

    /// <summary>How far along a held job is ("Gear Set 4/10, stone 0.5/2 t; the courier is on the way").</summary>
    private static string JobProgress(FactionJob job, JobWatch? w)
    {
        var parts = new string[job.Needs.Length];
        for (var k = 0; k < job.Needs.Length; k++)
        {
            var n = job.Needs[k];
            var have = w?.Have[k] ?? 0;
            parts[k] = n.Part.Length == 0 && n.Piece.Length == 0
                ? n.Kg >= 1000 ? $"{n.Material} {have / 1000.0:0.##}/{n.Kg / 1000.0:0.##} t" : $"{n.Material} {have}/{n.Kg} kg"
                : $"{(n.Part.Length > 0 ? n.Part : n.Piece)} {have}/{n.Count}";
        }
        var where = job.Kind == JobKind.Supply
            ? w is { Full: true } ? "; sealed, the courier is on the way" : w is { Crate: not 0 } ? " in your fullest Requisition Crate" : "; you need a Requisition Crate (build one, fill its box)"
            : " on your best construct";
        return string.Join(", ", parts) + where;
    }

    /// <summary>Client (and the host's own window): a <see cref="Msg.Jobs"/> State after the op.</summary>
    private void ReadJobs(ref PacketReader r)
    {
        int n = r.Has(1) ? r.U8() : 0;
        var view = new JobFaction[Math.Min(n, FactionTable.MaxStory)];
        for (var f = 0; f < view.Length; f++)
        {
            var jf = new JobFaction { Name = r.Text(64), Standing = r.Has(1) ? (sbyte)r.U8() : 0, Tier = r.Text(32) };
            int jn = r.Has(1) ? Math.Min((int)r.U8(), FactionRules.MaxJobs) : 0;
            jf.Jobs = new JobRow[jn];
            for (var j = 0; j < jn; j++)
            {
                var row = new JobRow { Name = r.Text(64), Ticket = r.Text(32), About = r.Text(255), What = r.Text(255), Pay = r.Text(96) };
                var flags = r.Has(1) ? r.U8() : 0;
                (row.Open, row.Held) = ((flags & 1) != 0, (flags & 2) != 0);
                row.Note = r.Text(255);
                jf.Jobs[j] = row;
            }
            view[f] = jf;
        }
        JobsView = view;
        JobsVersion++;
    }
}
