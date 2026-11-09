using System.Text.Json;

namespace CallOfSlop.Story;

/// <summary>
/// A mood (docs/story/STORY.md "Moods"): a buff or a debuff a person's body takes on at a moment of the fighting (a friend
/// killed nearby, badly hurt, coming to help...), if its query holds. While it lasts it changes how hard they are to hurt,
/// how hard they hit, how well they aim and how quickly they react, and it may make them fight (or run from) whoever caused
/// it. One mood at a time: a new one takes over only when its <see cref="Rank"/> is at least the one they are in.
/// </summary>
public sealed class MoodDef
{
    public const int Me = 0, Them = 1, Target = 2, Here = 3; // its query's variables
    public static readonly List<string> Vars = ["me", "them", "target", "here"];

    public const byte ActNone = 0, ActFight = 1, ActFlee = 2;

    public int Id;
    public string Name = "", Label = "", About = "", File = "", Text = "";
    public int On = -1; // the moment (MoodRules.Moments), or -1: reached only from another mood's "then"
    public StoryQuery? If;
    public int Needs; // the moment's names its query reads, a bit each (MoodDef.Vars): it doesn't take hold when one is missing
    public int Weight; // the query's clauses: of the moods for a moment whose queries hold, the most specific wins
    public float Chance = 1;
    public int Rank = 5; // 0-9: a mood takes over from one of a rank no higher
    public double Seconds = 30;
    public float Taken = 1, Dealt = 1, Aim = 1, React = 1, Turn = 1; // multipliers: damage taken and dealt, aim error, reaction time, turn rate
    public byte Act; // fight or run from ?them while it lasts
    public int Verb = -1; // a verb recorded as it takes hold: actor ?me, target ?them, owner ?target
    public int Then = -1; // the mood that follows when this one ends (its "then"), or -1
    public string ThenName = "";
}

/// <summary>The moods from Story/Moods/*.json. A bad file is refused whole with a reason (in <see cref="StoryRules.Errors"/>).</summary>
public sealed class MoodRules
{
    public const int MaxMoods = 64;
    public const float MinMult = 0.1f, MaxMult = 5f;
    public const double MaxSeconds = 600;

    /// <summary>
    /// The moments the game asks about: someone killed near them (?them the killer, ?target the dead), they killed someone
    /// (?target the dead), hurt (?them who did it), hurt below a third of their health for the first time in a fight
    /// (Wounded), and coming to help someone (?target the one helped, ?them their attacker).
    /// </summary>
    public static readonly string[] Moments = ["Killed", "Kill", "Hurt", "Wounded", "Defend"];

    public const int Killed = 0, Kill = 1, Hurt = 2, Wounded = 3, Defend = 4;

    public MoodDef[] Moods = [];
    public int[][] OfMoment = [.. Moments.Select(_ => Array.Empty<int>())]; // per moment, its moods, the most specific first

    public MoodDef? Mood(string name) => Array.Find(Moods, m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] FileKeys = ["about", "moods"];
    private static readonly string[] MoodKeys = ["name", "label", "about", "on", "if", "chance", "rank", "seconds", "taken", "dealt", "aim", "react", "turn", "act", "verb", "then"];

    /// <summary>Story/Moods/*.json under <paramref name="folder"/>. Errors go to the rules' list.</summary>
    public static MoodRules Load(StoryRules rules, string folder)
    {
        var mr = new MoodRules();
        var all = new List<MoodDef>();
        foreach (var f in StoryRules.Files(Path.Combine(folder, "Moods")))
        {
            var name = $"Moods/{Path.GetFileName(f)}";
            try
            {
                using var doc = StoryRules.Parse(StoryRules.ReadFile(f), name);
                var read = Read(rules, doc.RootElement, name, all);
                all.AddRange(read);
            }
            catch (StoryException ex)
            {
                rules.Errors.Add(ex.Message);
                Console.WriteLine($"[story] moods skipped: {ex.Message}");
            }
        }
        // "then" names another mood, from any file; one that isn't there is dropped with a reason (the mood still works).
        foreach (var m in all)
            if (m.ThenName.Length > 0)
            {
                var then = all.FindIndex(o => o.Name.Equals(m.ThenName, StringComparison.OrdinalIgnoreCase));
                if (then < 0 || then == m.Id)
                    rules.Errors.Add($"{m.File}: mood {m.Name}: \"then\": no other mood {m.ThenName}");
                else
                    m.Then = then;
            }
        mr.Moods = all.ToArray();
        mr.OfMoment = Enumerable.Range(0, Moments.Length)
            .Select(k => mr.Moods.Where(m => m.On == k).OrderByDescending(m => m.Weight).Select(m => m.Id).ToArray()).ToArray();
        return mr;
    }

    private static List<MoodDef> Read(StoryRules rules, JsonElement root, string file, List<MoodDef> before)
    {
        StoryRules.Keys(root, FileKeys, file);
        var list = new List<MoodDef>();
        foreach (var m in StoryRules.List(root, "moods", file, true))
        {
            var where = $"{file}: mood {list.Count + 1}";
            if (m.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{where}: a mood is like {{\"name\": \"Rage\", \"on\": \"Killed\", \"if\": \"?me Likes ?target >= 20\", \"taken\": 0.6}}");
            StoryRules.Keys(m, MoodKeys, where);
            var def = new MoodDef { Id = before.Count + list.Count, File = file };
            def.Name = StoryRules.Str(m, "name", where, true);
            if (!StoryRules.Word(def.Name))
                throw new StoryException($"{where}: \"name\" is one word");
            where = $"{file}: mood {def.Name}";
            if (before.Concat(list).Any(o => o.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase)))
                throw new StoryException($"{where}: there is already a mood of that name");
            def.Label = StoryRules.Str(m, "label", where);
            if (def.Label.Length == 0)
                def.Label = def.Name.ToLowerInvariant();
            if (def.Label.Length > 40 || def.Label.Any(c => c < ' ' || c > '~'))
                throw new StoryException($"{where}: \"label\" is printable ASCII, 40 characters at most");
            def.About = StoryRules.Str(m, "about", where);
            var on = StoryRules.Str(m, "on", where);
            if (on.Length > 0)
            {
                def.On = Array.FindIndex(Moments, k => k.Equals(on, StringComparison.OrdinalIgnoreCase));
                if (def.On < 0)
                    throw new StoryException($"{where}: \"on\" is a moment ({string.Join(", ", Moments)}); leave it out for a mood only another's \"then\" reaches");
            }
            var text = StoryRules.Str(m, "if", where);
            if (text.Length > 0)
            {
                if (def.On < 0)
                    throw new StoryException($"{where}: \"if\" needs an \"on\"");
                try
                {
                    def.If = StoryQuery.Compile(text, rules, [.. MoodDef.Vars]); // (one more variable of its own, like ?x)
                }
                catch (StoryException ex)
                {
                    throw new StoryException($"{where}: \"{text}\": {ex.Message}");
                }
            }
            def.Weight = def.If?.Clauses.Length ?? 0;
            foreach (var cl in def.If?.Clauses ?? []) // (an unbound ?them would match anyone)
            {
                if (cl.A.Var is >= 0 and < 4)
                    def.Needs |= 1 << cl.A.Var;
                if (cl.Kind is StoryQuery.ClauseKind.Relation or StoryQuery.ClauseKind.Same or StoryQuery.ClauseKind.Different && cl.B.Var is >= 0 and < 4)
                    def.Needs |= 1 << cl.B.Var;
            }
            def.Chance = Num(m, "chance", where, 1, 0.01f, 1);
            var rank = Num(m, "rank", where, 5, 0, 9);
            def.Rank = (int)rank;
            if (def.Rank != rank)
                throw new StoryException($"{where}: \"rank\" is a whole number, 0 to 9");
            def.Seconds = Num(m, "seconds", where, 30, 1, (float)MaxSeconds);
            def.Taken = Num(m, "taken", where, 1, MinMult, MaxMult);
            def.Dealt = Num(m, "dealt", where, 1, MinMult, MaxMult);
            def.Aim = Num(m, "aim", where, 1, MinMult, MaxMult);
            def.React = Num(m, "react", where, 1, MinMult, MaxMult);
            def.Turn = Num(m, "turn", where, 1, MinMult, MaxMult);
            var act = StoryRules.Str(m, "act", where);
            def.Act = act.ToLowerInvariant() switch
            {
                "" => MoodDef.ActNone,
                "fight" => MoodDef.ActFight,
                "flee" => MoodDef.ActFlee,
                _ => throw new StoryException($"{where}: \"act\" is \"fight\" or \"flee\" (whoever caused it)"),
            };
            if (def.Act != MoodDef.ActNone && def.On is Kill)
                throw new StoryException($"{where}: on Kill there is nobody to fight or run from (the one they killed is dead)");
            var verb = StoryRules.Str(m, "verb", where);
            if (verb.Length > 0)
                def.Verb = rules.Verb(verb)?.Id ?? throw new StoryException($"{where}: no verb {verb}");
            def.ThenName = StoryRules.Str(m, "then", where);
            def.Text = $"{(def.On >= 0 ? $"on {Moments[def.On]}" : "after another")}{(text.Length > 0 ? $" if {text}" : "")}";
            list.Add(def);
            if (before.Count + list.Count > MaxMoods)
                throw new StoryException($"{file}: too many moods ({MaxMoods} at most in all)");
        }
        return list;
    }

    private static float Num(JsonElement e, string key, string where, float def, float min, float max)
    {
        if (!e.TryGetProperty(key, out var v))
            return def;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetSingle(out var n) || !float.IsFinite(n) || n < min || n > max)
            throw new StoryException($"{where}: \"{key}\" is a number from {min} to {max}");
        return n;
    }

    /// <summary>What a mood does, in words (the window, --check-story).</summary>
    public static string Effects(MoodDef m)
    {
        var parts = new List<string>();
        void Add(float v, string what)
        {
            if (v != 1)
                parts.Add($"{what} x{v:0.##}");
        }
        Add(m.Taken, "damage taken");
        Add(m.Dealt, "damage dealt");
        Add(m.Aim, "aim error");
        Add(m.React, "reaction time");
        Add(m.Turn, "turn speed");
        if (m.Act == MoodDef.ActFight)
            parts.Add("fights whoever caused it");
        else if (m.Act == MoodDef.ActFlee)
            parts.Add("runs from whoever caused it");
        return parts.Count > 0 ? string.Join(", ", parts) : "nothing";
    }
}
