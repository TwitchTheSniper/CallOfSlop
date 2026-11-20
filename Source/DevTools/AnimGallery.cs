using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// <c>--anim-gallery DIR</c>: the Animation gallery (tools/preview/README.md "The Animation gallery"), the
/// Ship gallery's page with a timeline: every construct part that moves when worked (event doors and hatches, hinges and
/// joints, levers, knobs, rockers and buttons, machines' spinning and stroking boxes), and every assembly named by a timing
/// file in <c>tools/preview/anim/</c> (a blueprint whose event doors open in a given order, the leaves riding them). Each
/// model is the preview's JSON plus <c>"track"</c> (one byte a triangle: 0 still, k: the k-th track) and <c>"anim"</c>:
/// its length in seconds, whether it loops, labelled marks and each track's pose (position and quaternion in the model
/// frame) sampled evenly over the length, which the page plays, scrubs and cuts away. The poses come from the same part
/// data the game draws by (<see cref="ConstructDoors.DrawPose"/>, ConstructRender's ControlMove and WorkMove), not a copy
/// of their look: a design aid, so no lamps, no suspension travel, no walking legs.
/// </summary>
public static partial class ConstructPreview
{
    private static Func<int, int, byte>? _trackOf; // (part, box) -> its track (0: still), while Model runs for the Animation gallery
    private static List<byte>? _triTrack; // one track a triangle, filled by Model
    private static List<Matrix4x4>? _partFrames; // each part's frame into the model's, filled by Model

    /// <summary>A moving piece: the parts and boxes it holds, its motion at a time in seconds (in the frame of <see cref="Part"/>, or the body's when -1).</summary>
    private sealed record AnimTrack(string Name, int Part, Func<float, Matrix4x4> Move);

    private static int AnimGallery(string dir)
    {
        var template = Template();
        if (template == null)
        {
            Console.WriteLine($"--anim-gallery: no tools/preview/viewer.html above {AppContext.BaseDirectory}");
            return 1;
        }
        var models = Path.Combine(dir, "models");
        Directory.CreateDirectory(models);
        HashSet<string>? picked = null; // (always the whole gallery: its index lists every model, and a publish sends only the changed files)
        var changed = new List<string>();
        var written = new HashSet<string>(StringComparer.Ordinal);
        var groups = new Dictionary<string, List<object>>(StringComparer.Ordinal);
        var failed = 0;

        void Add(string group, string name, string json)
        {
            var file = Slug(name) + ".json";
            var path = Path.Combine(models, file);
            written.Add(file);
            if (!File.Exists(path) || File.ReadAllText(path) != json)
            {
                File.WriteAllText(path, json);
                changed.Add("models/" + file);
            }
            if (!groups.TryGetValue(group, out var list))
                groups[group] = list = [];
            list.Add(new { id = Slug(name), name, file = "models/" + file, tris = _tris, kb = json.Length / 1024 });
        }

        // Assemblies: one timing file each (tools/preview/anim/*.json).
        foreach (var timing in AnimFiles())
        {
            if (AssemblyModel(timing, picked, out var name, out var shelf) is { } json)
                Add(shelf, name, json);
            else if (name != "")
                failed++;
        }
        // Parts: every one with a box or a leaf that moves (not the style drafts: they show in their own assemblies).
        foreach (var d in ConstructParts.All.Where(d => d != null && !d.Retired && !d.Legacy && d.Group != "Style").OrderBy(d => d.Name, StringComparer.Ordinal))
        {
            if (picked != null && !picked.Contains(d.Name))
                continue;
            if (PartGroup(d) is not { } group)
                continue;
            if (PartModel(d) is { } json)
                Add(group, d.Name, json);
            else
                failed++;
        }

        string[] known = ["Ships", "Stations", "Assemblies", "Doors and hatches", "Hinges and joints", "Switches and levers", "Machines"];
        // (a timing file's own "shelf" goes after Ships and Stations, never dropped)
        string[] order = [.. known.Take(2), .. groups.Keys.Except(known).OrderBy(g => g, StringComparer.Ordinal), .. known.Skip(2)];
        var list = JsonSerializer.Serialize(new { groups = order.Where(groups.ContainsKey).Select(g => new { name = g, items = groups[g] }), noun = "animation" });
        var listPath = Path.Combine(models, "index.json");
        if (!File.Exists(listPath) || File.ReadAllText(listPath) != list)
        {
            File.WriteAllText(listPath, list);
            changed.Add("models/index.json");
        }
        var page = Page(template, "null", "Animation Gallery");
        var pagePath = Path.Combine(dir, "index.html");
        if (!File.Exists(pagePath) || File.ReadAllText(pagePath) != page)
        {
            File.WriteAllText(pagePath, page);
            changed.Add("index.html");
        }
        var stale = picked != null ? [] : Directory.GetFiles(models, "*.json").Select(Path.GetFileName).Where(f => f != "index.json" && !written.Contains(f!)).ToList();
        foreach (var f in stale)
            File.Delete(Path.Combine(models, f!));
        Console.WriteLine($"animation gallery {Path.GetFullPath(dir)}: {string.Join(", ", order.Where(groups.ContainsKey).Select(g => $"{groups[g].Count} {g.ToLowerInvariant()}"))}, {failed} refused; changed: {(changed.Count == 0 ? "nothing" : string.Join(" ", changed))}" +
                          (stale.Count > 0 ? $"; removed (publish them as null): {string.Join(" ", stale.Select(f => "models/" + f))}" : ""));
        return failed;
    }

    /// <summary>The timing files: tools/preview/anim/*.json above the DLL (a packaged build has none).</summary>
    private static IEnumerable<string> AnimFiles()
    {
        for (var up = new DirectoryInfo(AppContext.BaseDirectory); up != null; up = up.Parent)
        {
            var dir = Path.Combine(up.FullName, "tools", "preview", "anim");
            if (Directory.Exists(dir))
                return Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal);
        }
        return [];
    }

    /// <summary>Which shelf of the gallery a part goes on, or null: nothing on it moves.</summary>
    private static string? PartGroup(ConstructPartDef d)
    {
        if (d.Door != null)
            return "Doors and hatches";
        if (d.Role is PartRole.Hinge or PartRole.Joint && d.JointType is not (JointType.Fixed or JointType.Flex) && d.Boxes.Any(b => b.IsLeaf && !b.Hidden))
            return "Hinges and joints";
        if (d.Boxes.Any(b => !b.Hidden && b.Rig >= BoxRig.Lever))
            return "Switches and levers";
        if (d.Boxes.Any(b => !b.Hidden && b.Work is BoxWork.Spin or BoxWork.Stroke))
            return "Machines";
        return null;
    }

    // ---- One part -------------------------------------------------------------------------------------------------

    private static string? PartModel(ConstructPartDef d)
    {
        var tracks = new List<AnimTrack>();
        var boxTrack = new byte[d.Boxes.Length];
        var seconds = 0f;
        var loop = false;
        var marks = new List<(float, string)>();

        byte NewTrack(string name, Func<float, Matrix4x4> move)
        {
            tracks.Add(new AnimTrack(name, 0, move));
            return (byte)tracks.Count;
        }

        if (d.Door is { } dd)
        {
            seconds = dd.Seconds;
            var k = NewTrack(dd.Slide ? "Slides" : "Swings", tm => DoorMove(dd, 0, Vector3.Zero, Math.Clamp(tm / dd.Seconds, 0, 1)));
            Array.Fill(boxTrack, k);
            marks.Add((0, dd.Dock ? "Opens (docking hatch: opens as its body docks)" : dd.Launch ? "Opens (launch hatch: opens for a launch)" : "Opens (F or a wire)"));
        }
        else if (d.Role is PartRole.Hinge or PartRole.Joint && d.Boxes.Any(b => b.IsLeaf))
        {
            var axis = ConstructParts.DirVector(d.JointAxis);
            var piston = d.JointType is JointType.Piston or JointType.Slider;
            Func<float, Matrix4x4> move;
            if (!d.JointLimited)
            {
                var speed = d.MotorSpeed > 0 ? d.MotorSpeed : 1f;
                seconds = MathF.Tau / speed;
                loop = true;
                move = tm => Turn(axis, d.JointPivot, tm / seconds * MathF.Tau);
                marks.Add((0, "Turns round"));
            }
            else
            {
                var range = d.JointMax - d.JointMin;
                seconds = d.MotorSpeed > 0 ? Math.Clamp(range / d.MotorSpeed, 0.4f, 12f) : 1.5f;
                move = piston
                    ? tm => Matrix4x4.CreateTranslation(axis * (d.JointMin + range * Math.Clamp(tm / seconds, 0, 1)))
                    : tm => Turn(axis, d.JointPivot, d.JointMin + range * Math.Clamp(tm / seconds, 0, 1));
                marks.Add((0, piston ? $"From {d.JointMin:0.##} m" : $"From {d.JointMin * 180 / MathF.PI:0} deg"));
                marks.Add((seconds, piston ? $"To {d.JointMax:0.##} m" : $"To {d.JointMax * 180 / MathF.PI:0} deg"));
            }
            var k = NewTrack(d.JointType.ToString(), move);
            for (var b = 0; b < d.Boxes.Length; b++)
                if (d.Boxes[b].IsLeaf)
                    boxTrack[b] = k;
        }
        else
        {
            byte control = 0;
            for (var b = 0; b < d.Boxes.Length; b++)
            {
                var box = d.Boxes[b];
                if (box.Rig >= BoxRig.Lever)
                {
                    seconds = MathF.Max(seconds, 1.6f);
                    if (control == 0)
                    {
                        control = (byte)(tracks.Count + 1);
                        marks.Add((0, box.Rig is BoxRig.Flip or BoxRig.Press ? "Off" : "Low"));
                        marks.Add((1.6f, box.Rig is BoxRig.Flip or BoxRig.Press ? "On" : "High"));
                    }
                    var copy = box;
                    boxTrack[b] = NewTrack(box.Rig.ToString(), tm => ControlPose(in copy, Math.Clamp(tm / 1.6f, 0, 1)));
                }
                else if (box.Work is BoxWork.Spin or BoxWork.Stroke)
                {
                    loop = true;
                    var period = box.Work == BoxWork.Spin ? MathF.Tau / 2.4f : 1.25f;
                    seconds = MathF.Max(seconds, period);
                    var copy = box;
                    boxTrack[b] = NewTrack(box.Work.ToString(), tm => WorkPose(in copy, tm));
                    if (marks.Count == 0)
                        marks.Add((0, "While the machine runs"));
                }
            }
        }
        if (tracks.Count == 0)
            return null;
        if (Export(d.Name, out var bp, out var built, out _, boxTrack, tracks, seconds, loop, marks) is not { } json)
            return null;
        return json;
    }

    /// <summary>A hand control's box out of its rest pose at <paramref name="f"/> (0 low or off, 1 high or on), in the part's frame: ConstructRender.ControlMove's motions.</summary>
    private static Matrix4x4 ControlPose(in CPartBox box, float f)
    {
        var angle = (f - 0.5f) * 2 * AnalogLever.Arc;
        var pivot = box.RigPivot;
        return box.Rig switch
        {
            BoxRig.Knob => Turn(Vector3.UnitY, pivot, angle * AnalogLever.KnobTurn),
            BoxRig.Slide => Matrix4x4.CreateTranslation(Vector3.UnitZ * (box.RigTravel * angle / AnalogLever.Arc)),
            BoxRig.Flip => Turn(Vector3.UnitX, pivot, (f * 2 - 1) * AnalogLever.FlipAngle),
            BoxRig.Press => Matrix4x4.CreateTranslation(Vector3.UnitY * (-box.RigTravel * MathF.Sin(f * MathF.PI))),
            BoxRig.Stick => Matrix4x4.CreateTranslation(-pivot) * AnalogLever.StickTurn(0, 0.5f + 0.5f * MathF.Cos(f * MathF.Tau), 0.5f + 0.5f * MathF.Sin(f * MathF.Tau)) * Matrix4x4.CreateTranslation(pivot),
            _ => Turn(Vector3.UnitX, pivot, angle),
        };
    }

    /// <summary>A machine's working box at <paramref name="tm"/> seconds, in the part's frame: ConstructRender.WorkMove's motions.</summary>
    private static Matrix4x4 WorkPose(in CPartBox box, float tm)
    {
        var axis = ConstructParts.DirVector(box.WorkAxis);
        if (box.Work == BoxWork.Spin)
            return Turn(axis, box.Center, tm * 2.4f % MathF.Tau);
        var at = tm * 0.8f % 1f; // (down fast, up slowly: a press's stroke)
        return Matrix4x4.CreateTranslation(axis * (box.RigTravel * (at < 0.25f ? at * 4f : (1f - at) / 0.75f)));
    }

    private static Matrix4x4 Turn(Vector3 axis, Vector3 pivot, float angle) =>
        Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateFromAxisAngle(axis, angle) * Matrix4x4.CreateTranslation(pivot);

    /// <summary>An event door <paramref name="f"/> of the way from its settled pose (-1..1), in the frame its part sits in: <see cref="ConstructDoors.DrawPose"/>.</summary>
    private static Matrix4x4 DoorMove(CDoorDef dd, byte rot, Vector3 at, float f)
    {
        if (dd.Link != null || dd.Held)
            return ConstructDoors.LinkPose(dd, rot, at, f < 0 ? f + 1 : f, f < 0);
        var axis = Rot24.Apply(rot, ConstructParts.DirVector(dd.Axis));
        if (dd.Slide)
            return Matrix4x4.CreateTranslation(axis * (dd.Travel * f));
        var pivot = at + Rot24.Apply(rot, dd.Pivot);
        return Turn(axis, pivot, dd.Angle * f);
    }

    // ---- An assembly ----------------------------------------------------------------------------------------------

    /// <summary>
    /// A timing file: <c>{"blueprint": NAME|FILE, "title"?, "description"?, "seconds"?, "steps": [{"part": NAME, "at": S, "seconds"?: S, "label"?: TEXT}]}</c>.
    /// Every event door of the blueprint opens with the leaves riding it; a step's doors (every door part of that name) start at its "at"
    /// and take its "seconds" (or their own); unlisted doors start at 0. Null (and the reason printed, <paramref name="name"/> set) when it can't be made;
    /// null with an empty name when <paramref name="picked"/> leaves it out.
    /// </summary>
    private static string? AssemblyModel(string file, HashSet<string>? picked, out string name, out string shelf)
    {
        name = "";
        shelf = "Assemblies";
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }).RootElement;
        }
        catch (JsonException ex)
        {
            name = Path.GetFileName(file);
            Console.WriteLine($"REFUSED {name}: {ex.Message}");
            return null;
        }
        var what = root.TryGetProperty("blueprint", out var bpe) ? bpe.GetString() ?? "" : "";
        if (what != "" && !Path.IsPathRooted(what) && !ConstructBuild.Blueprints.Any(b => b.Name == what) && File.Exists(Path.Combine(Path.GetDirectoryName(file)!, what)))
            what = Path.Combine(Path.GetDirectoryName(file)!, what); // (a draft blueprint file beside the timing file)
        var title = root.TryGetProperty("title", out var te) ? te.GetString() ?? what : Path.GetFileNameWithoutExtension(what);
        if (picked != null && !picked.Contains(title) && !picked.Contains(what) && !picked.Contains(Path.GetFileNameWithoutExtension(file)))
            return null;
        name = title;
        var steps = new List<(string Part, float At, float Seconds, string Label)>();
        if (root.TryGetProperty("steps", out var se) && se.ValueKind == JsonValueKind.Array)
            foreach (var s in se.EnumerateArray())
                steps.Add((s.GetProperty("part").GetString() ?? "", s.TryGetProperty("at", out var a) ? a.GetSingle() : 0, s.TryGetProperty("seconds", out var sec) ? sec.GetSingle() : 0, s.TryGetProperty("label", out var l) ? l.GetString() ?? "" : ""));
        var length = root.TryGetProperty("seconds", out var le) ? le.GetSingle() : 0;
        var description = root.TryGetProperty("description", out var de) ? de.GetString() : null;
        // Its shelf: "shelf" in the file, else by what the blueprint is (a whole ship or station: its first part's group and at least 30 parts), else Assemblies.
        if (root.TryGetProperty("shelf", out var sh) && sh.GetString() is { Length: > 0 } named)
            shelf = named;
        else if (ConstructBuild.FindBlueprint(what) is { Steps.Count: >= 30 } known && ConstructParts.Find(known.Steps[0].Part)?.Group is { } g && g is "Ship" or "Station")
            shelf = g + "s";
        return Export(what, out _, out _, out _, null, null, length, false, null, steps, title, description, Path.GetFileName(file));
    }

    // ---- The model ------------------------------------------------------------------------------------------------

    /// <summary>
    /// <see cref="Export"/> with a timeline: a part's boxes by <paramref name="boxTrack"/> on <paramref name="tracks"/> (each in its part's frame), or a
    /// blueprint's event doors by <paramref name="steps"/>; the poses sampled into the model's "anim".
    /// </summary>
    private static string? Export(string what, out ConstructBuild.Blueprint bp, out int parts, out List<(string Name, int Count)> uses,
        byte[]? boxTrack, List<AnimTrack>? tracks, float seconds, bool loop, List<(float, string)>? marks,
        List<(string Part, float At, float Seconds, string Label)>? steps = null, string? title = null, string? description = null, string? timingFile = null)
    {
        bp = null!;
        parts = 0;
        uses = [];
        Construct? built = null;
        var assembly = steps != null;
        var partTrack = Array.Empty<byte>(); // (an assembly: part -> its door's track)
        tracks ??= [];
        marks ??= [];
        _partFrames = [];
        _triTrack = [];
        if (assembly)
        {
            // Build it first to find its doors, then once more through Export with the tracks known.
            if (!TryBuild(what, out bp, out built))
                return Done(null);
            built.EnsureDoors();
            partTrack = new byte[built.PartCount];
            if (built.DoorCount == 0)
            {
                Console.WriteLine($"REFUSED {timingFile}: {bp.Name} has no event doors to open");
                return Done(null);
            }
            if (built.DoorCount > 255)
                Console.WriteLine($"{timingFile}: {bp.Name} has {built.DoorCount} doors; the first 255 are animated");
            foreach (var st in steps!)
                if (!Enumerable.Range(0, built.DoorCount).Any(k => built.DefOf(built.Doors[k].Part).Name == st.Part))
                    Console.WriteLine($"{timingFile}: no event door part called \"{st.Part}\" in {bp.Name} (its doors: {string.Join(", ", Enumerable.Range(0, built.DoorCount).Select(k => built.DefOf(built.Doors[k].Part).Name).Distinct())})");
            var end = 0f;
            var body = built;
            for (var k = 0; k < Math.Min(255, built.DoorCount); k++)
            {
                var part = built.Doors[k].Part;
                var dd = built.DefOf(part).Door!;
                var pname = built.DefOf(part).Name;
                var step = steps!.FirstOrDefault(s => s.Part == pname);
                var start = step.Part != null ? step.At : 0;
                var dur = step.Part != null && step.Seconds > 0 ? step.Seconds : dd.Seconds;
                end = MathF.Max(end, start + dur);
                var p = built.Parts[part];
                var settled = ConstructDoors.SettledOpen(built, part) ? 1f : 0f;
                tracks.Add(new AnimTrack(pname, -1, tm => DoorMove(dd, p.Rot, p.Pos, Math.Clamp((tm - start) / dur, 0, 1) - settled)));
                var id = (byte)tracks.Count;
                partTrack[part] = id;
                for (var l = 0; l < built.Doors[k].LeafCount; l++)
                    partTrack[built.DoorLeaves[built.Doors[k].LeafFirst + l]] = id;
            }
            foreach (var st in steps!.Where(s => s.Part != "").OrderBy(s => s.At))
                if (st.Label != "" || !steps!.Any(o => o.At == st.At && o.Label != "") && !marks.Any(m => m.Item1 == st.At))
                    marks.Add((st.At, st.Label != "" ? st.Label : st.Part)); // (one mark a start: its label, or the first part's name)
            if (marks.Count == 0 || marks[0].Item1 > 0)
                marks.Insert(0, (0, "Shut"));
            seconds = seconds > 0 ? seconds : end;
            marks.Add((end, "Open"));
            _trackOf = (part, box) => part < partTrack.Length ? partTrack[part] : (byte)0;
        }
        else
            _trackOf = (part, box) => part == 0 && boxTrack != null && box < boxTrack.Length ? boxTrack[box] : (byte)0;
        var frames = Math.Clamp((int)MathF.Ceiling(seconds * 15) + 1, 2, 121);
        var anim = new StringBuilder();
        var tracksCopy = tracks;
        var marksCopy = marks;
        _animWrite = () =>
        {
            // (called by Model once the parts' frames are known: each track's pose in the model frame, sampled)
            anim.Append(",\"track\":\"").Append(Convert.ToBase64String(_triTrack!.ToArray())).Append('"');
            anim.Append(",\"anim\":{\"seconds\":").Append(F(seconds)).Append(",\"loop\":").Append(loop ? "true" : "false");
            anim.Append(",\"marks\":[").Append(string.Join(",", marksCopy.Select(m => $"[{F(m.Item1)},{JsonSerializer.Serialize(m.Item2)}]"))).Append(']');
            anim.Append(",\"tracks\":[");
            var body = _partFrames!.Count > 0 ? BodyFrame(built) : Matrix4x4.Identity;
            for (var k = 0; k < tracksCopy.Count; k++)
            {
                var tr = tracksCopy[k];
                var frame = tr.Part < 0 ? body : _partFrames[Math.Min(tr.Part, _partFrames.Count - 1)];
                Matrix4x4.Invert(frame, out var inv);
                anim.Append(k > 0 ? "," : "").Append("{\"name\":").Append(JsonSerializer.Serialize(tr.Name)).Append(",\"frames\":[");
                for (var f = 0; f < frames; f++)
                {
                    var m = inv * tr.Move(seconds * f / (frames - 1)) * frame;
                    Matrix4x4.Decompose(m, out _, out var q, out var tv);
                    anim.Append(f > 0 ? "," : "").Append('[').Append(F(tv.X)).Append(',').Append(F(tv.Y)).Append(',').Append(F(tv.Z)).Append(',')
                        .Append(F(q.X)).Append(',').Append(F(q.Y)).Append(',').Append(F(q.Z)).Append(',').Append(F(q.W)).Append(']');
                }
                anim.Append("]}");
            }
            anim.Append("]}");
            return anim.ToString();
        };
        string? json;
        if (assembly)
        {
            parts = built!.PartCount;
            var (oldName, oldDescription) = (bp.Name, bp.Description); // (shown under the timing file's title; the loaded blueprint is put back as it was)
            bp.Name = title ?? bp.Name;
            bp.Description = description ?? bp.Description;
            try
            {
                json = Model(built, bp);
            }
            finally
            {
                (bp.Name, bp.Description) = (oldName, oldDescription);
            }
        }
        else
            json = Export(what, out bp, out parts, out uses);
        return Done(json);

        string? Done(string? result)
        {
            _trackOf = null;
            _triTrack = null;
            _partFrames = null;
            _animWrite = null;
            return result;
        }

        static string F(float f) => f.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Func<string>? _animWrite;

    /// <summary>The body frame (segment 0's rest pose) into the model's.</summary>
    private static Matrix4x4 BodyFrame(Construct? c)
    {
        if (c == null)
            return Matrix4x4.Identity;
        c.RestPose(0, out var q, out var t);
        return Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t);
    }

    private static bool TryBuild(string what, out ConstructBuild.Blueprint bp, out Construct c)
    {
        bp = null!;
        c = null!;
        try
        {
            if (File.Exists(what))
                bp = ConstructBuild.ParseBlueprint(File.ReadAllText(what), Path.GetFileName(what));
            else if (ConstructBuild.FindBlueprint(what) is { } found)
                bp = found;
            else
            {
                Console.WriteLine($"--anim-gallery: no blueprint file or blueprint called \"{what}\"");
                return false;
            }
        }
        catch (Exception ex) when (ex is ConstructPartException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            Console.WriteLine($"REFUSED {what}: {ex.Message}");
            return false;
        }
        var built = ConstructBuild.BuildOnGround(new ConstructWorld(), bp, Vector3.Zero, 0, out var why);
        if (built == null)
        {
            Console.WriteLine($"REFUSED {bp.Name}: it doesn't build: {why}");
            return false;
        }
        c = built;
        return true;
    }
}
