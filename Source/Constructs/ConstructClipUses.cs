using System.Globalization;
using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>What the save window does with a use whose parts changed since its stamp (the user's answer 4: parts only).</summary>
public enum UseChoice : byte
{
    Plain, // saved as plain parts (the default)
    NewVersion, // its parts saved as the next version of its blueprint: every live use everywhere gets it
    NewBlueprint, // its parts saved as a blueprint of their own, under a new name
}

/// <summary>
/// A clip saved with its sub-blueprint uses (docs/sandbox/design/sub-blueprints.md, section 1): which uses stay uses (the highest
/// intact use wholly in the selection; a changed one dissolves into its intact inner ones), the build order, and each use's
/// "uses" entry. <see cref="ConstructClip.ToBlueprint"/> writes the plain parts as steps and these as uses.
/// </summary>
public sealed class ClipUses
{
    /// <summary>One row of the save window's Uses list: a use as the selection has it.</summary>
    public sealed class Row
    {
        public int Tag; // its tag on the clip's body
        public string Name = "", Id = "";
        public int Version;
        public bool Lock;
        public bool Kept; // saved as a use
        public bool Changed; // its parts changed: the row offers the three ways
        public UseChoice Choice;
        public string NewName = ""; // NewBlueprint: the name asked
        public string Why = ""; // why it is plain parts
        public int Parts;
    }

    private readonly ConstructClip _clip;
    public readonly List<Row> Rows = new();
    public readonly List<int> Chosen = new(); // tags saved as uses, in build order
    private readonly Dictionary<int, SubBlueprints.SubEntry> _subs = new();
    public int[] Steps = [], Key = [], UseOf = [], SubStep = [], StepIndex = [];
    private int[] _after = [];

    public int Count => Chosen.Count;

    private ClipUses(ConstructClip clip) => _clip = clip;

    /// <summary>
    /// The uses of a clip, as rows and a plan. <paramref name="keep"/>: the rows of an earlier plan (their lock toggles and choices).
    /// Null when the clip has no tags.
    /// </summary>
    public static ClipUses? Plan(ConstructClip clip, List<Row>? keep = null)
    {
        var c = clip.Body;
        if (c.Uses == null || c.Uses.Count == 0)
            return null;
        var plan = new ClipUses(clip);
        var dissolved = new HashSet<int>();
        var whyOf = new Dictionary<int, string>();
        // Ropes, springs, no-collide pairs and tracks name parts as steps: a clip with any keeps every use as plain parts (for now).
        var lists = clip.World.ConnectorsJson(c).Length > 0 || clip.World.NoCollidesJson(c).Length > 0 || c.TrackCount > 0;
        for (var guard = 0; guard < 8; guard++)
        {
            plan.Rows.Clear();
            plan.Chosen.Clear();
            plan.Choose(-1, dissolved, whyOf, keep, lists);
            if (plan.Chosen.Count == 0)
                break;
            if (!plan.Order())
            {
                // (nothing but uses, no parent part to hold them: the outermost go plain, their inner uses stay uses)
                foreach (var t in plan.Chosen)
                {
                    dissolved.Add(t);
                    whyOf[t] = "the selection is nothing but this use: its inner uses are kept";
                }
                continue;
            }
            // A routed link wholly in one use is that use's own; a branch on one, or one branching off it from outside, can't be told
            // apart: those uses go plain, and the choice is made again.
            var more = false;
            for (var k = 0; k < c.RouteCount; k++)
            {
                ref var r = ref c.Routes[k];
                if (r.Parent == CRoute.NoParent)
                    continue;
                if (plan.InsideOne(k) != plan.InsideOne(r.Parent))
                    foreach (var part in new[] { (int)r.To, (int)r.Source })
                        if (part < c.PartCount && plan.UseOf[part] >= 0 && dissolved.Add(plan.Chosen[plan.UseOf[part]]))
                        {
                            whyOf[plan.Chosen[plan.UseOf[part]]] = "a branched link runs into it: saved as plain parts";
                            more = true;
                        }
            }
            if (!more)
                break;
        }
        if (plan.Chosen.Count == 0)
        {
            plan.Steps = Enumerable.Range(0, c.PartCount).ToArray();
            plan.Key = plan.Steps;
        }
        return plan;
    }

    /// <summary>The highest intact uses under <paramref name="parent"/>: each a row; one that isn't intact dissolves into its inner ones.</summary>
    private void Choose(int parent, HashSet<int> dissolved, Dictionary<int, string> whyOf, List<Row>? keep, bool lists)
    {
        var c = _clip.Body;
        for (var t = 0; t < c.Uses!.Count; t++)
        {
            var tag = c.Uses[t];
            if (tag.Parent != parent)
                continue;
            var parts = 0;
            var square = true;
            for (var i = 0; i < c.PartCount; i++)
                if (SubBlueprints.In(c, i, t))
                {
                    parts++;
                    square &= c.FrameOf(c.Parts[i].Seg) == 0;
                }
            if (parts == 0)
                continue; // (a tag no part of the selection has)
            var row = new Row { Tag = t, Name = tag.Name, Id = tag.Id, Version = tag.Version, Lock = tag.Lock, Parts = parts };
            if (keep?.Find(r => r.Tag == t && r.Id == tag.Id) is { } was)
            {
                row.Lock = was.Lock;
                row.Choice = was.Choice;
                row.NewName = was.NewName;
            }
            Rows.Add(row);
            var why = whyOf.GetValueOrDefault(t, "");
            SubBlueprints.SubEntry? sub = null;
            if (why.Length == 0 && lists)
                why = "the selection has ropes, springs, no-collide pairs or tracks: saved as plain parts";
            if (why.Length == 0 && !square)
                why = "freeform-attached: a use is saved only standing square to the rest";
            if (why.Length == 0 && Vector3.DistanceSquared(tag.Origin, ConstructWorld.Quantise(tag.Origin)) > 1e-6f) // (1 mm: a float's sums aren't exact)
                why = "off the 1 cm grid: saved as plain parts";
            if (why.Length == 0)
            {
                sub = SubBlueprints.Resolve(tag.Id, tag.Version, tag.Name);
                if (sub.Why.Length > 0)
                    why = sub.Why;
                else if (SubBlueprints.HashOf(c, t) != tag.Hash || sub.Flat.Steps.Count != parts)
                {
                    row.Changed = true;
                    why = parts < sub.Flat.Steps.Count ? $"{sub.Flat.Steps.Count - parts} of its parts are missing (cut by the selection, or taken off)"
                        : parts > sub.Flat.Steps.Count ? $"{parts - sub.Flat.Steps.Count} parts were added to it" : "its parts were moved or changed";
                }
                else if (!Map(t, sub))
                    why = "its parts don't match its blueprint's: saved as plain parts";
            }
            if (why.Length > 0 || dissolved.Contains(t))
            {
                row.Why = why.Length > 0 ? why : "saved as plain parts";
                Choose(t, dissolved, whyOf, keep, lists); // (its intact inner uses stay uses)
                continue;
            }
            row.Kept = true;
            _subs[t] = sub!;
            Chosen.Add(t);
        }
    }

    /// <summary>Each part of use <paramref name="t"/> to its step of the sub-blueprint (part and place). False when one has none.</summary>
    private bool Map(int t, SubBlueprints.SubEntry sub)
    {
        var c = _clip.Body;
        var tag = c.Uses![t];
        if (SubStep.Length != c.PartCount)
        {
            SubStep = new int[c.PartCount];
            Array.Fill(SubStep, -1);
        }
        var at = new Dictionary<(string, int, int, int), int>();
        for (var s = 0; s < sub.PartOf.Length; s++)
        {
            ref var sp = ref sub.Body.Parts[sub.PartOf[s]];
            var p = ConstructWorld.Quantise(tag.Origin + Rot24.Apply(tag.Turn, sp.Pos));
            at[(sub.Body.DefOf(sub.PartOf[s]).Name, Cm(p.X), Cm(p.Y), Cm(p.Z))] = s;
        }
        for (var i = 0; i < c.PartCount; i++)
            if (SubBlueprints.In(c, i, t))
            {
                var p = c.Parts[i].Pos;
                if (!at.TryGetValue((c.DefOf(i).Name, Cm(p.X), Cm(p.Y), Cm(p.Z)), out var s))
                    return false;
                SubStep[i] = s;
            }
        return true;
    }

    private static int Cm(float v) => (int)MathF.Round(v * 100);

    /// <summary>The build order: plain parts as steps in the clip's order, each use where its first part came (after a step).</summary>
    private bool Order()
    {
        var c = _clip.Body;
        var n = c.PartCount;
        UseOf = new int[n];
        Array.Fill(UseOf, -1);
        for (var u = 0; u < Chosen.Count; u++)
            for (var i = 0; i < n; i++)
                if (SubBlueprints.In(c, i, Chosen[u]))
                    UseOf[i] = u;
        Key = new int[n];
        StepIndex = new int[n];
        Array.Fill(StepIndex, -1);
        _after = new int[Chosen.Count];
        var useKey = new int[Chosen.Count];
        var emitted = new bool[Chosen.Count];
        var steps = new List<int>();
        var pending = new List<int>();
        var seq = 0;
        for (var i = 0; i < n; i++)
        {
            var u = UseOf[i];
            if (u < 0)
            {
                StepIndex[i] = steps.Count;
                steps.Add(i);
                Key[i] = seq++;
                foreach (var pu in pending)
                {
                    _after[pu] = StepIndex[i];
                    useKey[pu] = seq++;
                }
                pending.Clear();
            }
            else if (!emitted[u])
            {
                emitted[u] = true;
                if (steps.Count == 0)
                    pending.Add(u);
                else
                {
                    _after[u] = steps.Count - 1;
                    useKey[u] = seq++;
                }
            }
        }
        if (steps.Count == 0)
            return false;
        for (var i = 0; i < n; i++)
            if (UseOf[i] >= 0)
                Key[i] = useKey[UseOf[i]];
        // A use's order among the uses after one step: the order they were met (useKey), which is the order Chosen is written in.
        var order = Enumerable.Range(0, Chosen.Count).OrderBy(u => useKey[u]).ToArray();
        if (!order.SequenceEqual(Enumerable.Range(0, Chosen.Count)))
        {
            var remap = new int[Chosen.Count];
            for (var k = 0; k < order.Length; k++)
                remap[order[k]] = k;
            var chosen = order.Select(u => Chosen[u]).ToList();
            var after = order.Select(u => _after[u]).ToArray();
            Chosen.Clear();
            Chosen.AddRange(chosen);
            _after = after;
            for (var i = 0; i < n; i++)
                if (UseOf[i] >= 0)
                    UseOf[i] = remap[UseOf[i]];
        }
        Steps = steps.ToArray();
        return true;
    }

    public bool InUse(int part) => part >= 0 && part < UseOf.Length && UseOf[part] >= 0;

    /// <summary>A part as a step in the file: its step, or its use's step ["uN", k].</summary>
    public string RefJson(int part) => UseOf[part] >= 0 ? SubBlueprints.RefJson(SubBlueprints.UseRef(UseOf[part], SubStep[part])) : StepIndex[part].ToString(CultureInfo.InvariantCulture);

    /// <summary>Is routed link <paramref name="k"/> wholly in one use (that use's own: left out)?</summary>
    public bool InsideOne(int k)
    {
        var c = _clip.Body;
        ref var r = ref c.Routes[k];
        var u = r.To < UseOf.Length ? UseOf[r.To] : -1;
        if (u < 0 || r.Source < UseOf.Length && UseOf[r.Source] != u)
            return false;
        for (var i = 0; i < r.Count; i++)
            if (c.Node(k, i).Host is var h && h < UseOf.Length && UseOf[h] != u)
                return false;
        return true;
    }

    /// <summary>The "uses" list: per use its id, name, version, lock, turn, place, the step it follows, its joining links and its overrides.</summary>
    public void UsesJson(StringBuilder sb, Construct c, Vector3 origin, HashSet<(int, int)> taken)
    {
        sb.Append(",\n  \"uses\": [\n");
        for (var u = 0; u < Chosen.Count; u++)
        {
            var t = Chosen[u];
            var tag = c.Uses![t];
            var row = Rows.Find(r => r.Tag == t)!;
            var at = tag.Origin - origin;
            sb.Append("    { \"id\": \"").Append(tag.Id).Append("\", \"name\": ").Append(ConstructClip.Quote(tag.Name)).Append(", \"version\": ").Append(tag.Version);
            if (row.Lock)
                sb.Append(", \"lock\": true");
            sb.Append(", \"turn\": ").Append(tag.Turn).Append(FormattableString.Invariant($", \"at\": [{at.X:0.##}, {at.Y:0.##}, {at.Z:0.##}]")).Append(", \"after\": ").Append(_after[u]);
            // Its links to what comes before it: the parent's steps and earlier uses.
            var on = new StringBuilder();
            for (var i = 0; i < c.PartCount; i++)
            {
                if (UseOf[i] != u)
                    continue;
                var d = c.DefOf(i);
                for (var e = c.FirstLink(i); e >= 0; e = c.NextLink(e))
                {
                    int o = c.OtherEnd(e);
                    if (UseOf[o] == u || Key[o] > Key[i])
                        continue;
                    ref var l = ref c.Links[e >> 1];
                    var (st, sd) = (e & 1) != 0 ? ((int)l.SA, (int)l.SB) : (l.SB, l.SA);
                    if (st >= c.SocketCount(o) || sd >= d.Sockets.Length)
                        continue; // (a merged receiver's centre socket: not a blueprint's)
                    taken.Add((o, st));
                    on.Append(on.Length == 0 ? "[" : ", ").Append('[').Append(RefJson(o)).Append(", ").Append(ConstructClip.Quote(ConstructMerge.SocketName(c, o, st)))
                        .Append(", ").Append(SubStep[i]).Append(", ").Append(ConstructClip.Quote(d.Sockets[sd].Name)).Append(']');
                }
            }
            if (on.Length > 0)
                sb.Append(", \"on\": ").Append(on).Append(']');
            // Its overrides: paint and settings changed on this use (the user's answer 4).
            var sub = _subs[t];
            var over = new StringBuilder();
            for (var i = 0; i < c.PartCount; i++)
            {
                if (UseOf[i] != u)
                    continue;
                var s = SubStep[i];
                ref var p = ref c.Parts[i];
                ref var sp = ref sub.Body.Parts[sub.PartOf[s]];
                var d = c.DefOf(i);
                var one = new StringBuilder();
                var wantPaint = s < sub.Flat.StepPaint.Count ? sub.Flat.StepPaint[s] : 0;
                if (p.Paint != wantPaint)
                    one.Append(", \"paint\": \"").Append(ConstructPaint.IsSet(p.Paint) ? ConstructPaint.ToHex(p.Paint) : "none").Append('"');
                var wantFinish = s < sub.Flat.StepFinish.Count ? sub.Flat.StepFinish[s] : (byte)0;
                if (p.Finish != wantFinish)
                    one.Append(", \"finish\": \"").Append(ConstructFinish.Name(p.Finish) is { Length: > 0 } fn ? fn : "none").Append('"');
                if (p.Setting != sp.Setting)
                    one.Append(", \"setting\": ").Append(p.Setting);
                var cfg = ConstructConfig.ToJson(in p, d);
                if (cfg != ConstructConfig.ToJson(in sp, sub.Body.DefOf(sub.PartOf[s])))
                    one.Append(", \"config\": ").Append(cfg.Length > 0 ? cfg : "{}");
                if (one.Length > 0)
                    over.Append(over.Length == 0 ? "[" : ", ").Append("{ \"step\": ").Append(s).Append(one).Append(" }");
            }
            if (over.Length > 0)
                sb.Append(", \"over\": ").Append(over).Append(']');
            sb.Append(u + 1 < Chosen.Count ? " },\n" : " }\n");
        }
        sb.Append("  ]");
    }

    /// <summary>
    /// A changed use's parts saved as a blueprint of their own (the save window's New version or New blueprint), in the use's own frame
    /// so every other live use of it stands where it stood: the tag then names it, intact. Null, or why not.
    /// </summary>
    public static string? SaveUse(ConstructClip clip, Row row)
    {
        var c = clip.Body;
        var tag = c.Uses![row.Tag];
        var items = new List<ushort>();
        for (var i = 0; i < c.PartCount; i++)
            if (SubBlueprints.In(c, i, row.Tag))
                items.Add((ushort)i);
        if (items.Count == 0)
            return "it has no parts";
        if (ConstructClip.Capture(c, items.ToArray(), out var why, clip.World) is not { } part)
            return why;
        // Into the use's own axes: merged into an empty body by the inverse of its turn, then the first part's place is the frame's.
        var world = new ConstructWorld();
        var a = world.Create(Vector3.Zero, Quaternion.Identity)!;
        var b = world.CopyPiece(part.Body, Enumerable.Range(0, part.Body.PartCount).ToArray(), Vector3.Zero, Quaternion.Identity, out why);
        if (b == null)
            return why;
        var inv = SubBlueprints.Inverse(tag.Turn);
        if (!world.MergeAt(a, new JoinPlan { Other = b, Turn = inv, Offset = -Rot24.Apply(inv, tag.Origin) }))
            return "it couldn't be turned into its own frame";
        a.Rebuild();
        a.Uses = null;
        for (var i = 0; i < a.PartCount; i++)
            a.Parts[i].Use = 0; // (saved as plain parts: its inner uses become plain in the new version)
        var own = ConstructClip.Of(a, row.Name, world);
        var first = own.Body.Parts[0].Pos; // (ToBlueprint's frame: its first part's place, these axes)
        string name, id;
        int version;
        if (row.Choice == UseChoice.NewVersion)
        {
            var lib = SubBlueprints.Index();
            if (!lib.TryGetValue(tag.Id, out var e))
                return $"{tag.Name} isn't in your blueprints: save it as a new blueprint";
            name = e.Name;
            var text = own.ToBlueprint(name, "", out why);
            if (text == null)
                return why;
            text = SubBlueprints.NextVersion(name, SubBlueprints.WithId(text, tag.Id, e.Version));
            if (PlayerBlueprints.Write(name, text) is { } bad)
                return bad;
            (id, version) = SubBlueprints.IdOf(text);
        }
        else
        {
            name = row.NewName.Trim();
            if (PlayerBlueprints.NameRefusal(name) is { } badName)
                return badName;
            if (PlayerBlueprints.Exists(name))
                return $"{name} exists: give another name";
            var text = own.ToBlueprint(name, "", out why);
            if (text == null)
                return why;
            id = SubBlueprints.NewId();
            version = 1;
            if (PlayerBlueprints.Write(name, SubBlueprints.WithId(text, id, version)) is { } bad)
                return bad;
        }
        // The tag names it now, in its new frame (its first part's place), intact.
        tag.Id = id;
        tag.Name = name;
        tag.Version = version;
        tag.Origin = ConstructWorld.Quantise(tag.Origin + Rot24.Apply(tag.Turn, first));
        for (var i = 0; i < c.PartCount; i++) // (its inner uses are plain parts of the new version: no part names them now)
            if (SubBlueprints.In(c, i, row.Tag))
                c.Parts[i].Use = (ushort)(row.Tag + 1);
        tag.Hash = SubBlueprints.HashOf(c, row.Tag);
        row.Choice = UseChoice.Plain;
        return null;
    }
}
