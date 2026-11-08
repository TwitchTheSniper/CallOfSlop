using System.Text.Json;

namespace CallOfSlop.Story;

/// <summary>What a job is done by (docs/story/STORY.md "Jobs"): a crate the faction's courier collects, or something built that it inspects.</summary>
public enum JobKind : byte
{
    /// <summary>Fill a Requisition Crate with what it asks for (parts, bulk, cut pieces); the courier collects it.</summary>
    Supply,

    /// <summary>Own a construct that holds the parts it asks for; the faction inspects it and it stays yours.</summary>
    Build,
}

/// <summary>
/// One thing a job asks for: <see cref="Part"/> parts by name, <see cref="Material"/> bulk by the kilogram, or, with
/// <see cref="Piece"/>, pieces an Extruder or a Wheel Former cut (a section's id such as "i-beam", "round", "pipe", "wheel"
/// or "any"), of <see cref="Material"/> (any when empty), at least <see cref="Length"/> long and <see cref="Across"/> across.
/// </summary>
public sealed class JobNeed
{
    public string Part = "", Material = "", Piece = "";
    public int Count = 1; // parts or pieces
    public int Kg; // bulk
    public float Length, Across; // a piece's least length and size across (metres)
    public string Text = ""; // in words ("10 x Gear Set", "2 t stone", "4 x steel i-beam, 8 m+")
}

/// <summary>
/// A job a faction offers (docs/story/STORY.md "Jobs"): no money, the pay is standing (and through it the faction's
/// technologies). A party (a player's group, or the player) holds at most one job of each faction at a time.
/// </summary>
public sealed class FactionJob
{
    public string Name = "", About = "", Ticket = "";
    public int Id; // its place in the faction's list
    public int Tier = -1; // offered only from this tier of the faction's standing up (-1: to anyone from -50 up, so a party can work its way back)
    public JobKind Kind;
    public JobNeed[] Needs = [];
    public FactionDeed Pay = new(); // what doing it does: standing, wants, and the faction remembers it (JobsDone)
    public double Again = 1800; // s before the same party may take it again
    public bool Paperwork; // the courier sends the first crate back over the forms, and takes it the next time
    public string Taken = "", Done = "", Refused = ""; // lines in the faction's voice
    public string Text = ""; // what it asks, in words, for the window
}

public sealed partial class FactionRules
{
    public const int MaxJobs = 16, MaxNeeds = 6;
    public int OnJob = -1, JobAgain = -1, JobRefused = -1, JobsDone = -1; // relations: a party and a job (a Thing); the faction and a party
    public int Job = -1; // the verb: a faction signed a job off

    private static readonly string[] JobKeys = ["name", "about", "ticket", "tier", "supply", "build", "standing", "wants", "again", "paperwork", "taken", "done", "refused"];
    private static readonly string[] NeedKeys = ["part", "material", "piece", "count", "tonnes", "kg", "length", "across"];

    /// <summary>The job ids (after the rest of <see cref="Load"/>'s).</summary>
    private void JobIds(StoryRules rules)
    {
        OnJob = rules.Relation("OnJob")?.Id ?? -1;
        JobAgain = rules.Relation("JobAgain")?.Id ?? -1;
        JobRefused = rules.Relation("JobRefused")?.Id ?? -1;
        JobsDone = rules.Relation("JobsDone")?.Id ?? -1;
        Job = rules.VerbId("Job");
    }

    private static string Voice(JsonElement root, string where)
    {
        var v = StoryRules.Str(root, "voice", where);
        if (v.Length > 24 || v.Any(c => c < ' ' || c > '~' || c == '[' || c == ']'))
            throw new StoryException($"{where}: \"voice\" is a short tag its lines go out under, up to 24 printable characters (\"MINISTRY MEMO\")");
        return v;
    }

    private static FactionJob[] ReadJobs(FactionRules fr, StoryRules rules, JsonElement root, FactionDef def, string where)
    {
        var jobs = new List<FactionJob>();
        foreach (var e in StoryRules.List(root, "jobs", where))
        {
            var at = $"{where}: job {jobs.Count + 1}";
            if (e.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{at}: like {{\"name\": \"Breaking Rocks\", \"supply\": [{{\"material\": \"stone\", \"tonnes\": 2}}], \"standing\": 6}}");
            StoryRules.Keys(e, JobKeys, at);
            var job = new FactionJob { Name = StoryRules.Str(e, "name", at, true), About = StoryRules.Str(e, "about", at), Id = jobs.Count };
            at = $"{where}: {job.Name}";
            Plain(job.Name, at, 40);
            if (jobs.Any(o => o.Name.Equals(job.Name, StringComparison.OrdinalIgnoreCase)) || def.Deeds.Any(o => o.Name.Equals(job.Name, StringComparison.OrdinalIgnoreCase))
                || def.Tiers.Any(o => o.Name.Equals(job.Name, StringComparison.OrdinalIgnoreCase)) || def.Teaches.Any(o => o.Name.Equals(job.Name, StringComparison.OrdinalIgnoreCase))
                || job.Name.Equals("Notorious", StringComparison.OrdinalIgnoreCase))
                throw new StoryException($"{at}: jobs, technologies, deeds and tiers share names (a fax's notices name them)");
            job.Ticket = StoryRules.Str(e, "ticket", at);
            if (job.Ticket.Length > 24)
                throw new StoryException($"{at}: \"ticket\" is up to 24 characters (\"#4471, priority 3\")");
            var tier = StoryRules.Str(e, "tier", at);
            if (tier.Length > 0)
                job.Tier = def.Tiers.ToList().FindIndex(t => t.Name.Equals(tier, StringComparison.OrdinalIgnoreCase)) is var ti and >= 0 ? ti
                    : throw new StoryException($"{at}: \"tier\" is one of the faction's tiers (no {tier})");
            var supply = e.TryGetProperty("supply", out _);
            var build = e.TryGetProperty("build", out _);
            if (supply == build)
                throw new StoryException($"{at}: a job has \"supply\" (a crate the courier collects) or \"build\" (something the faction inspects), not both");
            job.Kind = supply ? JobKind.Supply : JobKind.Build;
            job.Needs = ReadNeeds(e, supply ? "supply" : "build", job.Kind, at);
            job.Pay = new FactionDeed { Name = job.Name, Id = -1, Remember = fr.JobsDone, Standing = Int(e, "standing", at, 1, MaxStanding, null) };
            if (e.TryGetProperty("wants", out var ws))
            {
                if (ws.ValueKind != JsonValueKind.Object)
                    throw new StoryException($"{at}: \"wants\" is like {{\"Hardware\": 10}}");
                job.Pay.Wants = ws.EnumerateObject().Select(p => def.Wants.FirstOrDefault(w => w.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)) is { } w
                    && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var n) && n is >= -100 and <= 100 ? (w.Quality, n)
                    : throw new StoryException($"{at}: \"wants\": {p.Name} is one of the faction's wants, by a whole number from -100 to 100")).ToArray();
            }
            var again = StoryRules.Str(e, "again", at);
            if (again.Length > 0)
                job.Again = StoryRules.Duration(again) is { } d and >= 60 and <= 7 * 86400 ? d : throw new StoryException($"{at}: \"again\" is a time like \"30m\", 1 minute to 7 days");
            job.Paperwork = e.TryGetProperty("paperwork", out var pw) && (pw.ValueKind == JsonValueKind.True ? true
                : pw.ValueKind == JsonValueKind.False ? false : throw new StoryException($"{at}: \"paperwork\" is true or false"));
            if (job.Paperwork && job.Kind != JobKind.Supply)
                throw new StoryException($"{at}: \"paperwork\" is for a supply job (the courier sends the first crate back)");
            job.Taken = Line(e, "taken", at);
            job.Done = Line(e, "done", at);
            job.Refused = Line(e, "refused", at);
            job.Text = (job.Kind == JobKind.Supply ? "Supply: " : "Build: ") + string.Join(", ", job.Needs.Select(n => n.Text));
            jobs.Add(job);
        }
        if (jobs.Count > MaxJobs)
            throw new StoryException($"{where}: {MaxJobs} jobs at most");
        if (jobs.Count > 0 && (fr.OnJob < 0 || fr.JobAgain < 0 || fr.JobRefused < 0 || fr.Job < 0))
            throw new StoryException($"{where}: \"jobs\" need the relations OnJob, JobAgain and JobRefused and the verb Job (Story/Relations, Story/Verbs)");
        return jobs.ToArray();
    }

    private static string Line(JsonElement e, string key, string at)
    {
        var s = StoryRules.Str(e, key, at);
        if (s.Length > 240 || s.Any(c => c < ' ' || c > '~'))
            throw new StoryException($"{at}: \"{key}\" is a line of printable ASCII, up to 240 characters");
        return s;
    }

    private static JobNeed[] ReadNeeds(JsonElement e, string key, JobKind kind, string at)
    {
        var needs = new List<JobNeed>();
        foreach (var n in StoryRules.List(e, key, at))
        {
            var where = $"{at}: \"{key}\" {needs.Count + 1}";
            if (n.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{where}: like {{\"part\": \"Gear Set\", \"count\": 10}}, {{\"material\": \"stone\", \"tonnes\": 2}} or {{\"piece\": \"i-beam\", \"material\": \"steel\", \"length\": 8}}");
            StoryRules.Keys(n, NeedKeys, where);
            var need = new JobNeed { Part = StoryRules.Str(n, "part", where), Material = StoryRules.Str(n, "material", where), Piece = StoryRules.Str(n, "piece", where).ToLowerInvariant() };
            var what = (need.Part.Length > 0 ? 1 : 0) + (need.Piece.Length > 0 ? 1 : 0) + (need.Part.Length == 0 && need.Piece.Length == 0 && need.Material.Length > 0 ? 1 : 0);
            if (what != 1 || need.Part.Length > 0 && need.Material.Length > 0)
                throw new StoryException($"{where}: one of \"part\", \"piece\" (with or without a \"material\") or \"material\" alone (bulk)");
            if (kind == JobKind.Build && need.Part.Length == 0)
                throw new StoryException($"{where}: a build job names parts (\"part\")");
            if (need.Piece.Length > 0 && !PieceNames.Contains(need.Piece))
                throw new StoryException($"{where}: \"piece\" is one of {string.Join(", ", PieceNames)}");
            var bulk = need.Part.Length == 0 && need.Piece.Length == 0;
            if (bulk)
            {
                var tonnes = n.TryGetProperty("tonnes", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetDouble(out var tv) && tv is > 0 and <= 1000 ? tv : -1;
                var kg = n.TryGetProperty("kg", out _) ? Int(n, "kg", where, 1, 1_000_000, null) : -1;
                if ((tonnes > 0) == (kg > 0))
                    throw new StoryException($"{where}: bulk is asked for in \"tonnes\" (up to 1000) or \"kg\", one of them");
                need.Kg = kg > 0 ? kg : (int)Math.Round(tonnes * 1000);
                need.Text = need.Kg >= 1000 ? $"{need.Kg / 1000.0:0.#} t {need.Material}" : $"{need.Kg} kg {need.Material}";
            }
            else
            {
                if (n.TryGetProperty("tonnes", out _) || n.TryGetProperty("kg", out _))
                    throw new StoryException($"{where}: \"tonnes\" and \"kg\" are for bulk (a \"material\" alone)");
                need.Count = Int(n, "count", where, 1, 1000, 1);
                need.Length = Metres(n, "length", where);
                need.Across = Metres(n, "across", where);
                if (need.Part.Length > 0 && (need.Length > 0 || need.Across > 0))
                    throw new StoryException($"{where}: \"length\" and \"across\" are for a piece");
                need.Text = need.Part.Length > 0 ? $"{need.Count} x {need.Part}"
                    : $"{need.Count} x {(need.Material.Length > 0 ? need.Material + " " : "")}{(need.Piece == "any" ? "piece" : need.Piece)}"
                      + (need.Length > 0 ? $", {need.Length:0.##} m+" : "") + (need.Across > 0 ? $", {need.Across:0.##} m+ across" : "");
            }
            needs.Add(need);
        }
        if (needs.Count is 0 or > MaxNeeds)
            throw new StoryException($"{at}: \"{key}\" asks for 1 to {MaxNeeds} things");
        return needs.ToArray();
    }

    /// <summary>What a piece may be: the Extruder's section ids, a round, a pipe, a wheel, or any of them.</summary>
    public static readonly string[] PieceNames = ["any", "solid", "plate", "wall", "hollow", "i-beam", "c-channel", "l-angle", "t-bar", "cross", "round", "pipe", "wheel"];

    private static float Metres(JsonElement n, string key, string where)
    {
        if (!n.TryGetProperty(key, out var v))
            return 0;
        return v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && d is > 0 and <= 200 ? (float)d : throw new StoryException($"{where}: \"{key}\" is metres, above 0 and up to 200");
    }
}

/// <summary>
/// Faction jobs in the story world (docs/story/STORY.md "Jobs"). A party holds a job as <c>OnJob</c> towards the job's Thing
/// (<c>"job:FACTION:NAME"</c>); <c>JobAgain</c> is the story minute (plus one) before which it may not take that job again,
/// and <c>JobRefused</c> marks a crate the courier has sent back once over the paperwork. All world state: saved with the story.
/// The game judges whether a job is done (GameStoryJobs.cs) and calls <see cref="FinishJob"/>.
/// </summary>
public sealed partial class StoryFactions
{
    private int[][] _jobIds = []; // per faction and job, its Thing
    private (int Faction, int Job, int Party) _finishing = (-1, -1, -1); // the job whose Job event is being recorded

    /// <summary>Jobs finished since the game last looked (faction, job, the party, who did it), oldest first; the game empties it.</summary>
    public readonly List<(int Faction, int Job, int Party, int Actor)> Finished = new(8);

    private void EnsureJobs()
    {
        var r = R;
        _jobIds = new int[r.Factions.Length][];
        for (var f = 0; f < r.Factions.Length; f++)
        {
            var def = r.Factions[f];
            _jobIds[f] = new int[def.Jobs.Length];
            for (var j = 0; j < def.Jobs.Length; j++)
                _jobIds[f][j] = W.Get(StoryKind.Thing, Key($"job:{def.Name}:{def.Jobs[j].Name}"), def.Jobs[j].Name, _e.Time);
        }
    }

    private int JobThing(int f, int j) => (uint)f < (uint)_jobIds.Length && (uint)j < (uint)_jobIds[f].Length ? _jobIds[f][j] : -1;

    private int Minute => (int)(_e.Time / 60) + 1;

    /// <summary>The job of this faction the party holds, or -1.</summary>
    public int JobOf(int party, int f)
    {
        Ensure();
        if (R.OnJob < 0 || !W.Valid(party) || (uint)f >= (uint)R.Factions.Length)
            return -1;
        for (var j = 0; j < R.Factions[f].Jobs.Length; j++)
            if (_jobIds[f][j] >= 0 && W.Get(party, R.OnJob, _jobIds[f][j]) > 0)
                return j;
        return -1;
    }

    /// <summary>Why this party may not take the job now, or null.</summary>
    public string? TakeRefusal(int party, int f, int j)
    {
        Ensure();
        if (R.OnJob < 0 || !W.Valid(party) || (uint)f >= (uint)R.Factions.Length || (uint)j >= (uint)R.Factions[f].Jobs.Length || JobThing(f, j) < 0)
            return "no such job";
        var def = R.Factions[f];
        var job = def.Jobs[j];
        if (Notorious(party))
            return $"{def.Name} won't work with you: you are Notorious";
        if (JobOf(party, f) is var held and >= 0)
            return held == j ? "you already hold it" : $"you already hold {def.Name}'s job {def.Jobs[held].Name}: finish or drop it first";
        var standing = Standing(f, party);
        if (job.Tier >= 0 && def.TierOf(standing) < job.Tier)
            return $"{def.Name} offers it from {def.Tiers[job.Tier].Name} up";
        if (job.Tier < 0 && standing < -FactionRules.MaxStanding / 2)
            return $"{def.Name} won't give you work at {def.TierName(standing)}";
        var again = W.Get(party, R.JobAgain, JobThing(f, j));
        if (again > Minute)
            return $"{def.Name} has no more of that for {StoryRules.Span((again - Minute) * 60.0)}";
        return null;
    }

    /// <summary>A player takes a job for their party. Null, or why not.</summary>
    public string? TakeJob(int actor, int f, int j)
    {
        var party = Party(actor);
        if (TakeRefusal(party, f, j) is { } why)
            return why;
        W.Set(party, R.OnJob, JobThing(f, j), 1);
        W.Set(party, R.JobRefused, JobThing(f, j), 0);
        return null;
    }

    /// <summary>A player drops their party's job of this faction: it may be taken again after its "again" time. Returns the job dropped, or -1.</summary>
    public int DropJob(int actor, int f)
    {
        var party = Party(actor);
        var j = JobOf(party, f);
        if (j < 0)
            return -1;
        var thing = JobThing(f, j);
        W.Set(party, R.OnJob, thing, 0);
        W.Set(party, R.JobRefused, thing, 0);
        W.Set(party, R.JobAgain, thing, Minute + (int)Math.Ceiling(R.Factions[f].Jobs[j].Again / 60));
        return j;
    }

    /// <summary>
    /// A supply job's crate is full: true when the faction sends it back over the paperwork this time (a "paperwork" job's
    /// first crate), false when the courier may take it.
    /// </summary>
    public bool SendsBack(int party, int f, int j)
    {
        var job = R.Factions[f].Jobs[j];
        if (!job.Paperwork || R.JobRefused < 0 || W.Get(party, R.JobRefused, JobThing(f, j)) > 0)
            return false;
        W.Set(party, R.JobRefused, JobThing(f, j), 1);
        return true;
    }

    /// <summary>
    /// The game judged a party's job done (the crate collected, the build inspected): a Job event from the doer to the faction
    /// (the radio can tell of it); the faction remembers it (JobsDone), its standing with the doer and their group rises by the
    /// job's pay (rivals cool, tiers rate and teach as with any deed), its wants move, and a notice goes out.
    /// </summary>
    public void FinishJob(int actor, int f, int j)
    {
        var party = Party(actor);
        if (JobOf(party, f) != j || R.Job < 0)
            return;
        var thing = JobThing(f, j);
        W.Set(party, R.OnJob, thing, 0);
        W.Set(party, R.JobRefused, thing, 0);
        W.Set(party, R.JobAgain, thing, Minute + (int)Math.Ceiling(R.Factions[f].Jobs[j].Again / 60));
        _finishing = (f, j, party);
        try
        {
            _e.Record(R.Job, actor, _ids[f], item: thing, value: R.Factions[f].Jobs[j].Pay.Standing);
        }
        finally
        {
            _finishing = (-1, -1, -1);
        }
    }

    /// <summary>From <see cref="OnEvent"/>: the Job event being recorded pays the job.</summary>
    private void PayJob(in StoryEvent ev)
    {
        var (f, j, party) = _finishing;
        if (f < 0 || ev.Verb != R.Job)
            return;
        _finishing = (-1, -1, -1);
        var job = R.Factions[f].Jobs[j];
        var id = _ids[f];
        Deeds++;
        Touch(f, job.Pay, ev.Actor, ev.Actor == party, ev.Actor);
        if (party != ev.Actor)
            Touch(f, job.Pay, party, true, ev.Actor);
        foreach (var (q, delta) in job.Pay.Wants)
            W.SetQuality(id, q, Math.Clamp(W.Quality(id, q) + delta, 0, 100));
        if (Notices.Count < 64)
            Notices.Add(new FactionNotice(f, -1, -1, ev.Actor, party, Job: j));
        if (Finished.Count < 64)
            Finished.Add((f, j, party, ev.Actor));
    }
}
