using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Pressed parts (the Press rework, phase 1; design in docs/sandbox/constructs/production.md "The Press" once phase 3 lands): what a
/// die makes of a whole blank, a part sized by the blank. Its key is its name, "Pressed Part (DIE.PATTERN PROFILE WxHxL MATERIAL)":
/// the die's family and pattern (Pressing.json /2, <see cref="PressDie"/>), then the blank as an Extruder names it less
/// "Extrusion ". It is a named run-time part (<see cref="ConstructPartDef.IsNamedRuntime"/>): made from its name the first time it is
/// asked for, its id this machine's own, its name on the wire (after <see cref="Extrusion.WireMark"/> and <see cref="HullLoft.WireKind"/>)
/// and in saves. A key that is a preset's is the catalogue part itself (<see cref="PressTable.PresetByKey"/>), so old data never changes.
/// <para>
/// The part: its like part's behaviour (a shallow copy), the blank's boxes (solid, as the extrusion meshes them, its origin in the middle),
/// the die's and pattern's features (drawn only, never solid: a part collides as its blank), the die's sockets and the blank's own face
/// sockets, and numbers from the material and the real volume, as an extrusion's; behaviour numbers scale by the law
/// <c>like x (measure / preset's)^power x (S / preset's S)</c>. No hull, no parse: a first make is a few dozen boxes (<c>--press-part</c> times it).
/// </para>
/// </summary>
public static class Pressed
{
    public const string Prefix = "Pressed Part (";
    public const int MinCm = 10; // the least side of a blank's section

    /// <summary>The most a press of each class takes across (cm): Small, Medium, Large, Capital.</summary>
    public static readonly int[] PressMaxCm = [45, 180, 720, 2880];

    private static readonly string[] ClassWords = ["Small", "Medium", "Large", "Capital"];
    private static readonly byte[] DieSocketClass = [ConstructParts.Small, ConstructParts.Medium, ConstructParts.Large, ConstructParts.Capital]; // (a die's mount socket class, by press class)
    private static readonly float[] PressSpeed = [1, 2, 4, 8];
    public static readonly string[] ScaledFields = ["maxTorque", "motorStrength", "motorSpeed", "sideLoad", "maxLoad", "fuelCapacity", "hoseLength"];

    private static readonly Dictionary<string, ConstructPartDef?> Interned = new(StringComparer.Ordinal);

    private static int PlateSection => 1; // Extrusion.Presets[1] is "plate"

    public static bool Scalable(string field) => Array.IndexOf(ScaledFields, field) >= 0;

    /// <summary>A blank's size across (cm): its smaller side, but a plate's width (its height only sets its thickness).</summary>
    public static int Across(in ExtrusionKey k) => k.Shape is ExShape.Round or ExShape.Pipe || k.Shape == ExShape.Section && Extrusion.PresetOf(k.Mask) == PlateSection ? k.W : Math.Min((int)k.W, k.H);

    /// <summary>A blank's section: an Extruder preset's index, or PressBaySpec.RoundSection or PipeSection; -1 for anything else (a custom grid, a wheel).</summary>
    public static int SectionOf(in ExtrusionKey k) => PressBaySpec.SectionOf(k);

    /// <summary>A section's word on a card or in a reason: "L-angle", "Round bar", "Pipe".</summary>
    public static string SectionLabel(int section) => section switch
    {
        PressBaySpec.RoundSection => "Round bar",
        PressBaySpec.PipeSection => "Pipe",
        >= 0 when section < Extrusion.Presets.Length => Extrusion.ProfileLabel(Extrusion.Presets[section].Mask),
        _ => "Custom",
    };

    /// <summary>The least press class (0 Small .. 3 Capital) whose bay takes a blank this many cm across; -1 when none does.</summary>
    public static int PressClass(int acrossCm) => Array.FindIndex(PressMaxCm, c => acrossCm <= c);

    public static string ClassWord(int c) => c >= 0 && c < ClassWords.Length ? ClassWords[c] : "no";

    /// <summary>A blank as an Extruder names it, less "Extrusion ": "solid 50x50x75 steel".</summary>
    public static string BlankText(in ExtrusionKey k) => Extrusion.Name(k)[Extrusion.Prefix.Length..];

    public static string KeyOf(PressDie die, PressPattern p, in ExtrusionKey blank) => $"{Prefix}{die.Id}.{p.Id} {BlankText(blank)})";

    /// <summary>A blank's text read back, only when spelt exactly as a machine writes it; a preset section, never growing.</summary>
    public static ExtrusionKey? ParseBlank(string text)
    {
        if (Extrusion.ParseKey(Extrusion.Prefix + text) is not { } k || k.Growing || SectionOf(k) < 0)
            return null;
        return BlankText(k) == text ? k : null;
    }

    /// <summary>Can this die and pattern press this blank? <see cref="Refusal"/> without its text: a press asks every step, and allocates nothing.</summary>
    public static bool Fits(PressDie die, PressPattern p, in ExtrusionKey k)
    {
        var section = SectionOf(k);
        if (section < 0 || !die.Takes(p, section) || Array.IndexOf(die.Materials, k.Material) < 0)
            return false;
        if (k.W % Extrusion.StepCm != 0 || k.H % Extrusion.StepCm != 0 || k.L % Extrusion.StepCm != 0 || k.W < MinCm || k.H < MinCm)
            return false;
        var across = Across(k);
        if (across < die.AcrossMin || across > die.AcrossMax || k.L < die.LengthMin || k.L > die.LengthMax)
            return false;
        var aspect = k.L / (float)across;
        return aspect >= die.AspectMin - 1e-4f && aspect <= die.AspectMax + 1e-4f && PressClass(across) >= 0;
    }

    /// <summary>Why a die and pattern can't press this blank (bay card text), or null.</summary>
    public static string? Refusal(PressDie die, PressPattern p, in ExtrusionKey k)
    {
        var section = SectionOf(k);
        if (section < 0 || !die.Takes(p, section))
            return $"the {p.Label} pattern doesn't take a {SectionLabel(section).ToLowerInvariant()} blank";
        if (Array.IndexOf(die.Materials, k.Material) < 0)
            return $"a {die.Family} die doesn't work {RecyclingTable.Current.NameOf(k.Material)}";
        if (k.W % Extrusion.StepCm != 0 || k.H % Extrusion.StepCm != 0 || k.L % Extrusion.StepCm != 0 || k.W < MinCm || k.H < MinCm)
            return $"a blank's sizes go in {Extrusion.StepCm} cm steps, at least {MinCm} cm across";
        var across = Across(k);
        if (across < die.AcrossMin || across > die.AcrossMax)
            return $"the blank is {across / 100f:0.00} m across: a {die.Family} die takes {die.AcrossMin / 100f:0.00} to {die.AcrossMax / 100f:0.00} m";
        if (k.L < die.LengthMin || k.L > die.LengthMax)
            return $"the blank is {k.L / 100f:0.00} m long: a {die.Family} die takes {die.LengthMin / 100f:0.00} to {die.LengthMax / 100f:0.00} m";
        var aspect = k.L / (float)across;
        if (aspect < die.AspectMin - 1e-4f || aspect > die.AspectMax + 1e-4f)
            return $"the blank is {aspect:0.##} times as long as it is across: a {die.Family} die takes {die.AspectMin:0.##} to {die.AspectMax:0.##}";
        if (PressClass(across) < 0)
            return "too large for any press";
        return null;
    }

    /// <summary>Reads a key (null die: not a pressed part; then <paramref name="why"/> says why).</summary>
    public static bool TryParse(string name, out PressDie? die, out PressPattern? pattern, out ExtrusionKey blank, out string why)
    {
        die = null;
        pattern = null;
        blank = default;
        why = "not a pressed part";
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(')') || name.Length > ConstructParts.MaxRuntimeName)
            return false;
        var inner = name[Prefix.Length..^1];
        var sp = inner.IndexOf(' ');
        var dot = sp > 0 ? inner.IndexOf('.', 0, sp) : -1;
        if (dot <= 0)
            return false;
        var t = PressTable.Current;
        if (t.Die(inner[..dot]) is not { } d || d.Id != inner[..dot])
        {
            why = $"no die \"{inner[..dot]}\"";
            return false;
        }
        if (d.Pattern(inner[(dot + 1)..sp]) is not { } p)
        {
            why = $"the {d.Family} die has no pattern \"{inner[(dot + 1)..sp]}\"";
            return false;
        }
        if (ParseBlank(inner[(sp + 1)..]) is not { } k)
        {
            why = $"\"{inner[(sp + 1)..]}\" isn't a blank as an Extruder names it";
            return false;
        }
        if (Refusal(d, p, k) is { } no)
        {
            why = no;
            return false;
        }
        (die, pattern, blank, why) = (d, p, k, "");
        return true;
    }

    /// <summary>The part a key names: a preset's catalogue part, or the pressed part made the first time it is asked for. Null: not a pressed part, or the run-time table is full.</summary>
    public static ConstructPartDef? Parse(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(')') || name.Length > ConstructParts.MaxRuntimeName)
            return null;
        var t = PressTable.Current; // (before the lock: the first ask loads the parts)
        if (t.PresetByKey.TryGetValue(name, out var preset))
            return preset;
        lock (Interned)
        {
            if (Interned.TryGetValue(name, out var have))
            {
                if (have != null)
                    have.Touched = Environment.TickCount64;
                return have;
            }
            ConstructPartDef? made = null;
            if (TryParse(name, out var die, out var pattern, out var blank, out _))
            {
                made = Build(die!, pattern!, blank, name);
                if (!Extrusion.Adopt(made))
                    made = null;
            }
            Interned[name] = made;
            return made;
        }
    }

    /// <summary>A key read for a work order: its die, pattern and blank (no def is made), or null when it isn't a pressed key.</summary>
    public sealed record OrderSpec(PressDie Die, PressPattern Pattern, ExtrusionKey Blank, ConstructPartDef? Preset);

    private static readonly Dictionary<string, OrderSpec?> OrderSpecs = new(StringComparer.Ordinal);
    private static PressTable? _orderTable;

    /// <summary>
    /// What a press needs to work an order for this key (Press phase 4): read once, then looked up, so a press asking every step allocates
    /// nothing. Null: not a pressed key. A preset's key gives its catalogue part as <see cref="OrderSpec.Preset"/>.
    /// </summary>
    public static OrderSpec? SpecOf(string key)
    {
        var t = PressTable.Current;
        lock (OrderSpecs)
        {
            if (!ReferenceEquals(_orderTable, t) || OrderSpecs.Count > 4096)
            {
                OrderSpecs.Clear(); // (a table read again: its dies are new objects)
                _orderTable = t;
            }
            if (OrderSpecs.TryGetValue(key, out var have))
                return have;
            OrderSpec? spec = null;
            if (TryParse(key, out var die, out var pattern, out var blank, out _))
                spec = new OrderSpec(die!, pattern!, blank, t.PresetByKey.GetValueOrDefault(key));
            OrderSpecs[key] = spec;
            return spec;
        }
    }

    /// <summary>A work order a press makes: a pressed key by name, or a catalogue part that is a die's preset. Null: neither.</summary>
    public static OrderSpec? SpecOf(in WorkOrder o) =>
        o.Name != null ? (o.Name.StartsWith(Prefix, StringComparison.Ordinal) ? SpecOf(o.Name) : null)
        : !o.IsPiece && ConstructParts.Get(o.Part) is { } d && PressTable.Current.PresetKeyOf(d) is { } key ? SpecOf(key) : null;

    /// <summary>Is this an order's name for a pressed part (its key's prefix)?</summary>
    public static bool IsName(string? name) => name != null && name.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>"CVT 0.50 x 0.50 x 0.75 m steel" for a key; the key itself when it doesn't read.</summary>
    public static string Label(string key) =>
        SpecOf(key) is { } s ? s.Preset?.Name ?? $"{s.Pattern.Label} {s.Blank.W / 100f:0.##} x {s.Blank.H / 100f:0.##} x {s.Blank.L / 100f:0.##} m {RecyclingTable.Current.NameOf(s.Blank.Material)}" : key;

    /// <summary>(RuntimeParts) Drops the doomed parts from the key table, and the keys that made nothing.</summary>
    internal static void Forget(Func<ConstructPartDef, bool> doomed)
    {
        lock (Interned)
            RuntimeParts.DropOrNull(Interned, d => d == null || doomed(d));
    }

    /// <summary>A name off the wire (after HullLoft.WireKind), as this machine's part id: a stand-in, never a dropped packet, when it can't be made.</summary>
    internal static ushort Read(string name) => Parse(name) is { } d ? d.Id : ConstructParts.Need("Frame Beam").Id;

    // ---- Numbers -------------------------------------------------------------------------------------------------------

    /// <summary>A blank's solid volume (m3) and mass (kg).</summary>
    public static (float Volume, float Mass) VolumeMass(in ExtrusionKey k)
    {
        var mat = ExtrusionTable.Current.Find(k.Material)!;
        var volume = Extrusion.AreaM2(k) * (k.L / 100f);
        return (volume, volume * mat.UnitsPerM3 * RecyclingTable.Current.WeightOf(k.Material));
    }

    /// <summary>A measure of a blank: length (m), across (m), its largest face (m2), volume (m3) or mass (kg).</summary>
    public static float Measure(PressMeasure m, in ExtrusionKey k)
    {
        float w = k.W / 100f, h = k.H / 100f, l = k.L / 100f;
        return m switch
        {
            PressMeasure.Length => l,
            PressMeasure.Across => Across(k) / 100f,
            PressMeasure.Area => MathF.Max(w * l, MathF.Max(h * l, w * h)),
            PressMeasure.Volume => VolumeMass(k).Volume,
            _ => VolumeMass(k).Mass,
        };
    }

    /// <summary>What bay 2 must hold for this blank (D4), or 0 when the pattern takes nothing there.</summary>
    public static int Bay2For(PressPattern p, in ExtrusionKey k) => p.Bay2 is { } a ? a.For(Measure(a.By, k)) : 0;

    private static float Bay2Mass(PressPattern p, in ExtrusionKey k)
    {
        if (p.Bay2 is not { } a)
            return 0;
        var n = a.For(Measure(a.By, k));
        return a.Part != null ? a.Part.Mass * n : RecyclingTable.Current.WeightOf(a.Material) * n;
    }

    /// <summary>The most a stroke takes at speed 1 (s): a blank's mass grows as its size cubed, and a 16 m housing isn't an afternoon's wait.</summary>
    public const float MaxSeconds = 60;

    /// <summary>A stroke's seconds on a press of class <paramref name="pressClass"/> (class 0: at speed 1, as a press's own speed then divides it).</summary>
    public static float Seconds(PressDie die, in ExtrusionKey k, int pressClass) =>
        MathF.Min(MaxSeconds, die.SecondsBase + die.SecondsPerKg * VolumeMass(k).Mass) / PressSpeed[Math.Clamp(pressClass, 0, PressSpeed.Length - 1)];

    private static float Strength(byte material) => (ExtrusionTable.Current.Find(material)?.HealthPerM3 ?? 0) / PressTable.Current.StrengthRef;

    /// <summary>One scaled number for this blank (the law of section 5), from the like value; three significant figures.</summary>
    public static float Scaled(PressScale s, float likeValue, PressPattern p, in ExtrusionKey k)
    {
        var like = likeValue == 0 ? s.Ref : likeValue;
        if (like == 0)
            return 0;
        var ratio = MathF.Pow(Measure(s.By, k) / MathF.Max(Measure(s.By, p.Preset), 1e-6f), s.Power);
        if (s.Strength)
            ratio *= Strength(k.Material) / MathF.Max(Strength(p.Preset.Material), 1e-6f);
        ratio = Math.Clamp(ratio, 1 / 64f, 64f); // (the die's bounds keep it near this; never past)
        return Sig3(like * ratio);
    }

    private static float Sig3(float v)
    {
        if (v == 0 || !float.IsFinite(v))
            return 0;
        var mag = Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(v))) - 2);
        return (float)(Math.Round(v / mag) * mag);
    }

    internal static float Field(ConstructPartDef d, string field) => field switch
    {
        "maxTorque" => d.MaxTorque,
        "motorStrength" => d.MotorStrength,
        "motorSpeed" => d.MotorSpeed,
        "sideLoad" => d.SideLoad,
        "maxLoad" => d.MaxLoad,
        "fuelCapacity" => d.FuelCapacity,
        "hoseLength" => d.HoseLength,
        _ => 0,
    };

    private static void SetField(ConstructPartDef d, string field, float v)
    {
        switch (field)
        {
            case "maxTorque": d.MaxTorque = v; break;
            case "motorStrength": d.MotorStrength = v; break;
            case "motorSpeed": d.MotorSpeed = v; break;
            case "sideLoad": d.SideLoad = v; break;
            case "maxLoad": d.MaxLoad = v; break;
            case "fuelCapacity": d.FuelCapacity = v; break;
            case "hoseLength": d.HoseLength = v; break;
        }
    }

    // ---- The def ---------------------------------------------------------------------------------------------------------

    private static ushort _collar;

    /// <summary>A collar's shape on a round blank: a closed cylinder along z.</summary>
    private static ushort Collar => _collar != 0 ? _collar : _collar = PartShapes.For(new PartBox { Shape = "cylinder", Size = Vector3.One, Segments = 16, Inner = -1, Ends = "closed" });

    /// <summary>The z-fighting gap for a patch this size (m): 3 mm up to 2 m, 10 mm up to 8 m, 1/800 above, at most 50 mm (CLAUDE.md "No coplanar faces").</summary>
    public static float Gap(float patch) => patch <= 2 ? 0.003f : patch <= 8 ? 0.010f : MathF.Min(0.05f, patch / 800);

    /// <summary>The pressed part, unadopted (no id yet). Made whole: a shallow copy and a few dozen boxes.</summary>
    internal static ConstructPartDef Build(PressDie die, PressPattern p, in ExtrusionKey k, string name)
    {
        var names = RecyclingTable.Current;
        var mat = ExtrusionTable.Current.Find(k.Material)!;
        var materialName = names.NameOf(k.Material);
        float w = k.W / 100f, h = k.H / 100f, len = k.L / 100f, cw = w / Extrusion.Grid, ch = h / Extrusion.Grid;
        var section = SectionOf(k);
        if (p.LikeBody)
            return BuildLike(die, p, k, name, section);
        // The blank's boxes: a section's rects as the Extruder meshes them, or a round bar's or a pipe's own (its hidden solid boxes and its
        // drawn lathe shape: Extrusion.BuildRound), moved so the blank's middle is the origin.
        var round = k.Shape is ExShape.Round or ExShape.Pipe;
        var bar = round ? Extrusion.Def(k with { Growing = false }) : null;
        Span<Extrusion.Rect> rects = stackalloc Extrusion.Rect[Extrusion.Grid * Extrusion.Grid];
        var nb = bar != null ? bar.Boxes.Length : Extrusion.Mesh(k.Mask, rects);
        var solid = bar != null ? bar.SolidBoxes : nb;
        var nf = 0;
        foreach (var f in die.Features)
            nf += f.On(section) ? 1 : 0;
        foreach (var f in p.Features)
            nf += f.On(section) ? 1 : 0;
        var n = nb + nf;
        Span<Vector3> lo = stackalloc Vector3[n];
        Span<Vector3> hi = stackalloc Vector3[n];
        Span<byte> proud = stackalloc byte[n];
        var looks = new byte[n];
        var thinnest = float.MaxValue;
        var half = new Vector3(w * 0.5f, h * 0.5f, len * 0.5f);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var barShift = new Vector3(0, 0, len * 0.5f);
        for (var i = 0; bar != null && i < nb; i++)
        {
            lo[i] = bar.Boxes[i].Center - bar.Boxes[i].Half - barShift;
            hi[i] = bar.Boxes[i].Center + bar.Boxes[i].Half - barShift;
            min = Vector3.Min(min, lo[i]);
            max = Vector3.Max(max, hi[i]);
        }
        if (bar != null)
            thinnest = k.Shape == ExShape.Pipe ? k.Wall / 100f : w;
        for (var i = 0; bar == null && i < nb; i++)
        {
            var r = rects[i];
            lo[i] = new Vector3(r.X * cw, r.Y * ch, 0) - half;
            hi[i] = new Vector3((r.X + r.W) * cw, (r.Y + r.H) * ch, len) - half;
            min = Vector3.Min(min, lo[i]);
            max = Vector3.Max(max, hi[i]);
            thinnest = MathF.Min(thinnest, MathF.Min(r.W * cw, r.H * ch));
        }
        // Features: unit boxes on the blank's whole size, thin sides held to their cm, then moved off any face plane they share.
        PlaceFeatures(die, p, section, new Vector3(w, h, len), lo, hi, proud, looks, nb, [0, 1, 2], Vector3.Zero);
        Settle(lo, hi, proud, nb, Gap(MathF.Max(w, MathF.Max(h, len))));
        // The boxes: the blank's solid ones in its material's colour, then the features (detail: drawn only).
        var colour = names.ColourOf(k.Material);
        var boxes = new CPartBox[n];
        for (var i = 0; i < n; i++)
        {
            if (bar != null && i < nb)
            {
                boxes[i] = bar.Boxes[i] with { Center = bar.Boxes[i].Center - barShift };
                continue;
            }
            var c = i < nb ? colour : Tone(colour, looks[i]);
            // On a round bar or a pipe a feature that rings the whole section is a collar: drawn round, as the bar is.
            var ring = bar != null && i >= nb && lo[i].X <= min.X + 1e-4f && lo[i].Y <= min.Y + 1e-4f && hi[i].X >= max.X - 1e-4f && hi[i].Y >= max.Y - 1e-4f;
            boxes[i] = new CPartBox((lo[i] + hi[i]) * 0.5f, (hi[i] - lo[i]) * 0.5f, c, Detail: i >= nb, Shape: ring ? Collar : (ushort)0);
        }
        // Sockets: the die's, then the blank's own faces (as an extrusion's, moved with its origin), less any the die put in the same place.
        var across = Across(k);
        var cls = across < 200 ? ConstructParts.Small : across < 700 ? ConstructParts.Medium : across < 2400 ? ConstructParts.Large : ConstructParts.Capital; // (as a wheel's axle: by its size across)
        var socks = new List<CSocket>(die.Sockets.Length + 40);
        foreach (var s in die.Sockets)
            socks.Add(new CSocket(s.Name, min + s.At * (max - min), s.Dir, SocketKind.Struct, cls));
        var shift = new Vector3(0, 0, len * 0.5f);
        foreach (var s in Extrusion.Sockets(k, w, h, len, min + shift, max + shift))
        {
            var pos = s.At - shift;
            if (!socks.Exists(x => x.Dir == s.Dir && Vector3.DistanceSquared(x.At, pos) < 1e-6f || x.Name == s.Name))
                socks.Add(s with { At = pos });
        }
        // The def: the like part's behaviour, the blank's body and numbers.
        var (volume, mass) = VolumeMass(k);
        var d = p.Like.ShallowCopy();
        d.Name = name;
        d.File = "";
        d.Description = $"Pressed on a {die.Family} die: a {p.Label} of a {Extrusion.ProfileLabel(k.Mask).ToLowerInvariant()} blank {w:0.##} by {h:0.##} by {len:0.##} m of {materialName}.";
        d.Boxes = boxes;
        d.SolidBoxes = solid;
        d.BoundsMin = min;
        d.BoundsMax = max;
        d.Sockets = socks.ToArray();
        d.Class = cls;
        d.Mass = Math.Clamp(mass + Bay2Mass(p, k), 0.1f, ConstructParts.MaxPartMass);
        d.Health = Math.Clamp(volume * mat.HealthPerM3, 10, ConstructParts.MaxPartHealth);
        d.Armour = MathF.Min(mat.MaxArmour, mat.ArmourPerCm * thinnest * 100);
        d.Material = mat.Class;
        d.AttachSeconds = Math.Clamp(2 + volume * 2, 2, 8);
        d.Friction = mat.Class == "rubber" ? 1f : 0.6f;
        d.PhysicsOnly = true;
        d.PhysicsWhy = "pressed to size";
        d.Stage = true; // (never offered: a pressed part comes out of a press)
        d.Legacy = false;
        d.OnBreak = PartBreak.Detach;
        d.CentreOptOut = true;
        d.ColliderBoxes = k.Shape == ExShape.Pipe; // (a pipe collides as its ring of boxes, as an extruded pipe does)
        d.Hulls = null;
        d.HullCovers = null;
        d.HasGlass = false;
        d.DamagedTo = -1;
        d.DamagedName = "";
        d.UseBoxes = [];
        d.StatusBox = -1;
        d.HasEngineBoxes = d.HasWork = d.HasRig = false;
        d.Lamps = [];
        d.Panels = [];
        d.MatBase = null;
        d.IsNamedRuntime = true;
        d.ExHash = Fnv(name);
        d.SurfaceOverride = SurfaceRules.OfBulk(materialName);
        SurfaceRules.Apply(d);
        // The like part's named ports (a tank's pipes, a gearbox's shafts) where its faces now are: its bounds stretched onto the blank's.
        var likeSize = p.Like.BoundsMax - p.Like.BoundsMin;
        d.NamedPorts = MapPorts(p.Like.NamedPorts, (p.Like.BoundsMin + p.Like.BoundsMax) * 0.5f, (max - min) / Vector3.Max(likeSize, new Vector3(1e-3f)), (min + max) * 0.5f);
        foreach (var s in p.Scale)
            SetField(d, s.Field, Scaled(s, Field(p.Like, s.Field), p, k));
        return d;
    }

    private static NamedPort[] MapPorts(NamedPort[] ports, Vector3 from, Vector3 scale, Vector3 to)
    {
        if (ports.Length == 0)
            return ports;
        var r = new NamedPort[ports.Length];
        for (var i = 0; i < r.Length; i++)
        {
            var q = ports[i];
            r[i] = new NamedPort
            {
                Name = q.Name, Type = q.Type, In = q.In, Out = q.Out, Socket = q.Socket, At = (q.At - from) * scale + to, Face = q.Face, Fluid = q.Fluid,
                NoEffect = q.NoEffect, Byte = q.Byte, Implicit = q.Implicit, Opening = q.Opening,
            };
        }
        return r;
    }

    /// <summary>
    /// The die's and pattern's features on a blank of <paramref name="size"/>, from index <paramref name="at"/>: unit boxes on the blank, thin
    /// sides held to their cm. <paramref name="axes"/> maps the blank's axes (width, height, length) to the part's, <paramref name="centre"/>
    /// is the blank's middle in the part's frame (a like body's frame differs from the blank's). Returns the next index.
    /// </summary>
    private static int PlaceFeatures(PressDie die, PressPattern p, int section, Vector3 size, Span<Vector3> lo, Span<Vector3> hi, Span<byte> proud, byte[] looks, int at, ReadOnlySpan<int> axes, Vector3 centre)
    {
        var half = size * 0.5f;
        foreach (var f in die.Features)
            at = Place(f, section, size, half, lo, hi, proud, looks, at, axes, centre);
        foreach (var f in p.Features)
            at = Place(f, section, size, half, lo, hi, proud, looks, at, axes, centre);
        return at;
    }

    private static int Place(PressFeature f, int section, Vector3 size, Vector3 half, Span<Vector3> lo, Span<Vector3> hi, Span<byte> proud, byte[] looks, int at, ReadOnlySpan<int> axes, Vector3 centre)
    {
        if (!f.On(section))
            return at;
        var a = f.Lo * size - half;
        var b = f.Hi * size - half;
        for (var ax = 0; ax < 3; ax++)
        {
            if ((f.Clamp >> ax & 1) == 0)
                continue;
            float x0 = Get(a, ax), x1 = Get(b, ax), e = x1 - x0;
            var want = Math.Clamp(e, f.MinCm / 100f, f.MaxCm / 100f);
            if (want == e)
                continue;
            float u0 = Get(f.Lo, ax), u1 = Get(f.Hi, ax);
            if (u0 <= 0) // (it stands on or past the blank's low face: that side stays)
                x1 = x0 + want;
            else if (u1 >= 1)
                x0 = x1 - want;
            else
            {
                var c = (x0 + x1) * 0.5f;
                (x0, x1) = (c - want * 0.5f, c + want * 0.5f);
            }
            a = With(a, ax, x0);
            b = With(b, ax, x1);
        }
        Vector3 pa = centre, pb = centre;
        byte bits = 0;
        for (var ax = 0; ax < 3; ax++)
        {
            pa = With(pa, axes[ax], Get(centre, axes[ax]) + Get(a, ax));
            pb = With(pb, axes[ax], Get(centre, axes[ax]) + Get(b, ax));
            bits |= (byte)((f.Proud >> ax & 1) << axes[ax]);
        }
        lo[at] = pa;
        hi[at] = pb;
        proud[at] = bits;
        looks[at] = f.Look;
        return at + 1;
    }

    /// <summary>
    /// A like-body pressed part (<see cref="PressPattern.LikeBody"/>): the like part's boxes, sockets and part-frame points stretched per axis
    /// so its bounds are the blank's size (the blank's length along <see cref="PressPattern.Along"/>), so a joint keeps its base and leaf
    /// sides, its pivot and leaf socket, a suspension its axle socket, a shaft its end ports; the die's features drawn on it; numbers from
    /// the blank as a blank body's. A piston's travel and a suspension's travel stretch with it.
    /// </summary>
    private static ConstructPartDef BuildLike(PressDie die, PressPattern p, in ExtrusionKey k, string name, int section)
    {
        var like = p.Like;
        var names = RecyclingTable.Current;
        var mat = ExtrusionTable.Current.Find(k.Material)!;
        var materialName = names.NameOf(k.Material);
        float w = k.W / 100f, h = k.H / 100f, len = k.L / 100f;
        var lsize = like.BoundsMax - like.BoundsMin;
        var along = p.Along >= 0 ? p.Along : lsize.X >= lsize.Y && lsize.X >= lsize.Z ? 0 : lsize.Y >= lsize.Z ? 1 : 2;
        Span<int> axes = stackalloc int[3]; // the blank's width, height and length: which of the like part's axes each goes along
        axes[2] = along;
        for (int ax = 0, j = 0; ax < 3; ax++)
            if (ax != along)
                axes[j++] = ax;
        var target = Vector3.Zero;
        target = With(target, axes[0], w);
        target = With(target, axes[1], h);
        target = With(target, axes[2], len);
        var s = target / Vector3.Max(lsize, new Vector3(1e-3f));
        Vector3 Map(Vector3 v) => v * s;
        var nb = like.Boxes.Length;
        var nf = 0;
        foreach (var f in die.Features)
            nf += f.On(section) ? 1 : 0;
        foreach (var f in p.Features)
            nf += f.On(section) ? 1 : 0;
        var n = nb + nf;
        Span<Vector3> lo = stackalloc Vector3[n];
        Span<Vector3> hi = stackalloc Vector3[n];
        Span<byte> proud = stackalloc byte[n];
        var looks = new byte[n];
        for (var i = 0; i < nb; i++)
        {
            var b = like.Boxes[i];
            var c = Map(b.Center);
            var hh = Vector3.Abs(b.Half * s);
            lo[i] = c - hh;
            hi[i] = c + hh;
        }
        var centre = Map((like.BoundsMin + like.BoundsMax) * 0.5f);
        PlaceFeatures(die, p, section, new Vector3(w, h, len), lo, hi, proud, looks, nb, axes, centre);
        Settle(lo, hi, proud, nb, Gap(MathF.Max(w, MathF.Max(h, len))));
        var colour = names.ColourOf(k.Material);
        var boxes = new CPartBox[n];
        var min = Map(like.BoundsMin);
        var max = Map(like.BoundsMax);
        for (var i = 0; i < n; i++)
        {
            if (i < nb)
            {
                var b = like.Boxes[i];
                boxes[i] = b with { Center = (lo[i] + hi[i]) * 0.5f, Half = (hi[i] - lo[i]) * 0.5f };
                continue;
            }
            boxes[i] = new CPartBox((lo[i] + hi[i]) * 0.5f, (hi[i] - lo[i]) * 0.5f, Tone(colour, looks[i]), Detail: true);
            min = Vector3.Min(min, lo[i]);
            max = Vector3.Max(max, hi[i]);
        }
        var across = Across(k);
        var cls = across < 200 ? ConstructParts.Small : across < 700 ? ConstructParts.Medium : across < 2400 ? ConstructParts.Large : ConstructParts.Capital;
        var socks = new CSocket[like.Sockets.Length];
        for (var i = 0; i < socks.Length; i++)
            socks[i] = like.Sockets[i] with { At = Map(like.Sockets[i].At), Class = cls };
        var (volume, mass) = VolumeMass(k);
        var thinnest = k.Shape == ExShape.Pipe ? k.Wall / 100f : MathF.Min(w, h);
        var d = like.ShallowCopy();
        d.Name = name;
        d.File = "";
        d.Description = $"Pressed on a {die.Family} die: a {p.Label} of a {Extrusion.ProfileLabel(k.Mask).ToLowerInvariant()} blank {w:0.##} by {h:0.##} by {len:0.##} m of {materialName}.";
        d.Boxes = boxes;
        d.BoundsMin = min;
        d.BoundsMax = max;
        d.Sockets = socks;
        d.Class = cls;
        d.Mass = Math.Clamp(mass + Bay2Mass(p, k), 0.1f, ConstructParts.MaxPartMass);
        d.Health = Math.Clamp(volume * mat.HealthPerM3, 10, ConstructParts.MaxPartHealth);
        d.Armour = MathF.Min(mat.MaxArmour, mat.ArmourPerCm * thinnest * 100);
        d.Material = mat.Class;
        d.AttachSeconds = Math.Clamp(2 + volume * 2, 2, 8);
        d.PhysicsOnly = true;
        d.PhysicsWhy = "pressed to size";
        d.Stage = true;
        d.Legacy = false;
        d.CentreOptOut = true;
        d.Hulls = null;
        d.HullCovers = null;
        d.DamagedTo = -1;
        d.DamagedName = "";
        d.Lamps = [];
        d.Panels = [];
        d.MatBase = null;
        d.IsNamedRuntime = true;
        d.ExHash = Fnv(name);
        d.SurfaceOverride = SurfaceRules.OfBulk(materialName);
        SurfaceRules.Apply(d);
        // Points in the part's frame, and lengths along an axis.
        d.SeatAt = Map(like.SeatAt);
        d.JointPivot = Map(like.JointPivot);
        d.PathPivot = Map(like.PathPivot);
        d.ThrustAt = Map(like.ThrustAt);
        d.SmokeAt = Map(like.SmokeAt);
        d.HitchAt = Map(like.HitchAt);
        d.HoseAt = Map(like.HoseAt);
        d.DialAt = Map(like.DialAt);
        d.FlameAt = Map(like.FlameAt);
        d.GunMuzzle = Map(like.GunMuzzle);
        d.DataSeatAt = null;
        d.NamedPorts = MapPorts(like.NamedPorts, Vector3.Zero, s, Vector3.Zero);
        if (like.Role == PartRole.Joint && like.JointType is JointType.Piston or JointType.Slider && float.IsFinite(like.JointMin) && float.IsFinite(like.JointMax))
        {
            var along2 = Get(s, like.JointAxis / 2);
            d.JointMin = Math.Clamp(like.JointMin * along2, -ConstructParts.MaxPistonTravel, 0);
            d.JointMax = Math.Clamp(like.JointMax * along2, 0, ConstructParts.MaxPistonTravel);
            d.JointGive = like.JointGive * along2;
        }
        if (like.Role == PartRole.Suspension)
            d.Travel = like.Travel * Get(s, along);
        foreach (var sc in p.Scale)
            SetField(d, sc.Field, Scaled(sc, Field(like, sc.Field), p, k));
        return d;
    }

    /// <summary>
    /// Moves feature faces (index <paramref name="fixedCount"/> and up) off any face plane an earlier box shares facing the same way with
    /// overlap: outward on a proud axis, else inward (outward when the box is too thin to give). The blank's boxes never move, and of two
    /// features the later one moves, so a pair never leapfrogs.
    /// </summary>
    private static void Settle(Span<Vector3> lo, Span<Vector3> hi, ReadOnlySpan<byte> proud, int fixedCount, float gap)
    {
        var n = lo.Length;
        Span<float> tries = stackalloc float[8];
        for (var pass = 0; pass < 3; pass++)
        {
            var moved = false;
            for (var i = fixedCount; i < n; i++)
                for (var ax = 0; ax < 3; ax++)
                    for (var s = -1; s <= 1; s += 2)
                    {
                        var at = s < 0 ? Get(lo[i], ax) : Get(hi[i], ax);
                        if (!Shares(lo, hi, i, ax, s, at))
                            continue;
                        // The nearest free plane: on its preferred side first (out on a proud axis, else in while it stays thick enough), then the other.
                        var thick = Get(hi[i], ax) - Get(lo[i], ax);
                        var outFirst = (proud[i] >> ax & 1) != 0;
                        var t = 0;
                        for (var k = 1; k <= 4; k++)
                        {
                            var o = at + s * gap * k;
                            var inw = at - s * gap * k;
                            var inOk = thick - gap * k >= gap;
                            if (outFirst)
                            {
                                tries[t++] = o;
                                if (inOk && k <= 2)
                                    tries[t++] = inw;
                            }
                            else
                            {
                                if (inOk)
                                    tries[t++] = inw;
                                if (k <= 2)
                                    tries[t++] = o;
                            }
                            if (t >= tries.Length - 1)
                                break;
                        }
                        foreach (var plane in tries[..t])
                            if (!Shares(lo, hi, i, ax, s, plane))
                            {
                                if (s < 0)
                                    lo[i] = With(lo[i], ax, plane);
                                else
                                    hi[i] = With(hi[i], ax, plane);
                                moved = true;
                                break;
                            }
                    }
            if (!moved)
                return;
        }
    }

    // Does box i's face on axis ax, side s, put on this plane, lie on an earlier box's face that faces the same way, with overlap?
    private static bool Shares(ReadOnlySpan<Vector3> lo, ReadOnlySpan<Vector3> hi, int i, int ax, int s, float plane)
    {
        int b = (ax + 1) % 3, c = (ax + 2) % 3;
        for (var j = 0; j < i; j++)
        {
            var pj = s < 0 ? Get(lo[j], ax) : Get(hi[j], ax);
            if (MathF.Abs(plane - pj) > 0.0006f)
                continue;
            var ob = MathF.Min(Get(hi[i], b), Get(hi[j], b)) - MathF.Max(Get(lo[i], b), Get(lo[j], b));
            var oc = MathF.Min(Get(hi[i], c), Get(hi[j], c)) - MathF.Max(Get(lo[i], c), Get(lo[j], c));
            if (ob > 0 && oc > 0 && ob * oc >= 1e-5f)
                return true;
        }
        return false;
    }

    private static float Get(Vector3 v, int a) => a == 0 ? v.X : a == 1 ? v.Y : v.Z;

    private static Vector3 With(Vector3 v, int a, float x) => a == 0 ? v with { X = x } : a == 1 ? v with { Y = x } : v with { Z = x };

    private static Vector3 Tone(Vector3 c, byte look) => look switch
    {
        1 => Vector3.Min(c * 1.2f + new Vector3(0.05f), Vector3.One),
        2 => c * 0.45f,
        3 => new Vector3(0.78f, 0.63f, 0.23f),
        _ => c,
    };

    private static uint Fnv(string s)
    {
        var h = 2166136261u;
        foreach (var ch in s)
            h = (h ^ ch) * 16777619u;
        return h;
    }

    /// <summary>A hash of a def's geometry and numbers (the same on every machine for one key).</summary>
    public static uint GeometryHash(ConstructPartDef d)
    {
        var h = 2166136261u;
        void Mix(float v) => h = (h ^ (uint)(int)MathF.Round(v * 10000)) * 16777619u;
        foreach (var b in d.Boxes)
        {
            Mix(b.Center.X); Mix(b.Center.Y); Mix(b.Center.Z);
            Mix(b.Half.X); Mix(b.Half.Y); Mix(b.Half.Z);
            Mix(b.Detail ? 1 : 0);
        }
        foreach (var s in d.Sockets)
        {
            Mix(s.At.X); Mix(s.At.Y); Mix(s.At.Z);
            Mix(s.Dir); Mix(s.Class);
        }
        Mix(d.Mass); Mix(d.Health); Mix(d.Armour);
        return h;
    }

    // ---- --press-part ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>--press-part list | presets [DIE] | sweep DIE [--samples N] | DIE[.PATTERN] BLANK [BAY2] | "Pressed Part (...)"</c>: prints to
    /// callofslop.log; the exit code is the problems found.
    /// </summary>
    public static int Cli(string[] args)
    {
        var t = PressTable.Current;
        if (t.Problem.Length > 0)
        {
            Console.WriteLine($"[press] the table is refused: {t.Problem}");
            return 1;
        }
        if (args.Length == 0 || args[0] == "list")
            return List(t);
        if (args[0] == "presets")
            return Presets(t, args.Length > 1 ? args[1] : null);
        if (args[0] == "sweep" && args.Length > 1)
        {
            var samples = 24;
            var k = Array.IndexOf(args, "--samples");
            if (k >= 0 && k + 1 < args.Length)
                samples = Math.Clamp(int.Parse(args[k + 1], CultureInfo.InvariantCulture), 1, 2000);
            return Sweep(t, args[1], samples);
        }
        string name;
        if (args[0].StartsWith(Prefix, StringComparison.Ordinal))
            name = args[0];
        else
        {
            var bits = args[0].Split('.', 2);
            if (t.Die(bits[0]) is not { } die)
            {
                Console.WriteLine($"[press] no die \"{bits[0]}\": {string.Join(", ", t.Dies.Select(d => d.Id))}");
                return 1;
            }
            var pat = bits.Length > 1 ? die.Pattern(bits[1]) : die.Patterns[0];
            if (pat == null || args.Length < 2)
            {
                Console.WriteLine($"[press] {die.Family}: give a pattern ({string.Join(", ", die.Patterns.Select(p => p.Id))}) and a blank (\"plate 100x10x200 steel\")");
                return 1;
            }
            name = $"{Prefix}{die.Id}.{pat.Id} {args[1]})";
        }
        return Show(t, name, args.Skip(args[0].StartsWith(Prefix, StringComparison.Ordinal) ? 1 : 2).FirstOrDefault());
    }

    private static int List(PressTable t)
    {
        var names = RecyclingTable.Current;
        Console.WriteLine($"[press] {t.Dies.Length} dies, {t.PresetByKey.Count} presets ({PressTable.Format}; a press with one of these dies presses a whole blank)");
        foreach (var d in t.Dies)
        {
            var classes = Enumerable.Range(0, 4).Where(c => (c == 0 ? Pressed.MinCm : PressMaxCm[c - 1] + 1) <= d.AcrossMax && d.AcrossMin <= PressMaxCm[c]).Select(ClassWord);
            Console.WriteLine($"[press] {d.Family} die: blanks {string.Join(", ", d.Blanks.Select(SectionLabel))}; {string.Join(", ", d.Materials.Select(names.NameOf))}; "
                + $"across {d.AcrossMin / 100f:0.00} to {d.AcrossMax / 100f:0.00} m, length {d.LengthMin / 100f:0.00} to {d.LengthMax / 100f:0.00} m, aspect {d.AspectMin:0.##} to {d.AspectMax:0.##}; "
                + $"presses {string.Join(", ", classes)}; {d.Features.Length} features, {d.Sockets.Length} sockets; presets {t.PresetByKey.Keys.Count(k => k.StartsWith(Prefix + d.Id + ".", StringComparison.Ordinal))}");
            foreach (var p in d.Patterns)
                Console.WriteLine($"[press]   {d.Id}.{p.Id,-10} {p.Label,-18} like {p.Like.Name}; preset blank {BlankText(p.Preset)}; bay 2 {(p.Bay2 is { } a ? a.What : "none")}; {p.Features.Length} features, {p.Scale.Length} scaled numbers");
        }
        return 0;
    }

    private static int Presets(PressTable t, string? die)
    {
        var bad = 0;
        foreach (var (key, part) in t.PresetByKey)
        {
            if (die != null && !key.StartsWith($"{Prefix}{die.ToLowerInvariant()}.", StringComparison.Ordinal))
                continue;
            var found = ConstructParts.Find(key);
            var ok = ReferenceEquals(found, part);
            var why = ok ? "" : $": resolves to {found?.Name ?? "nothing"}";
            // Where it is made: the least press class whose bay takes the blank and that has this family's die; and the press must pick this
            // part from the blank alone (PressDie.PatternFor), or the preset never comes out of a press.
            var press = "";
            if (ok && TryParse(key, out var d, out _, out var blank, out _) && d != null)
            {
                var cls = PressClass(Across(blank));
                while (cls >= 0 && cls < PressMaxCm.Length && !ConstructParts.All.Any(x => x.IsPressDie && x.DieFamily.Equals(d.Family, StringComparison.OrdinalIgnoreCase) && ConstructPress.DieClassOf(x) == DieSocketClass[cls]))
                    cls++;
                if (cls < 0 || cls >= PressMaxCm.Length)
                    why = $": no press with a {d.Family} Die takes a blank {Across(blank) / 100f:0.00} m across";
                else if (d.PatternFor(blank, out var picked) == null || !ReferenceEquals(picked, part))
                {
                    // Not Auto's: its pattern set on the press makes it (two presets of one blank, as the Plate die's plate and tread).
                    var place = Array.FindIndex(d.Patterns, x => x.Id == key[(Prefix.Length + d.Id.Length + 1)..key.IndexOf(' ', Prefix.Length)]);
                    if (place < 0)
                        why = $": a press makes {picked?.Name ?? "a sized part"} of this blank";
                    else
                        press = $" (a {ClassWord(cls)} Press set to Pattern {place + 1}; Auto makes {picked?.Name ?? "a sized part"})";
                }
                else
                    press = $" (a {ClassWord(cls)} Press)";
                ok = why.Length == 0;
            }
            bad += ok ? 0 : 1;
            Console.WriteLine($"[press] {(ok ? "ok " : "BAD")} {part.Name,-24} {key}{press}{why}");
        }
        Console.WriteLine($"[press] presets: {bad} problems");
        return bad;
    }

    private static int Show(PressTable t, string name, string? bay2)
    {
        if (!TryParse(name, out var die, out var p, out var k, out var why))
        {
            Console.WriteLine($"[press] {name}: refused, {why}");
            return 1;
        }
        Console.WriteLine($"[press] key {name} ({name.Length} characters)");
        if (t.PresetByKey.TryGetValue(name, out var preset))
            Console.WriteLine($"[press] a preset: it is the catalogue part {preset.Name}");
        var probs = Report(die!, p!, k, name, out var made, out _);
        var need = Bay2For(p!, k);
        if (p!.Bay2 is { } a)
        {
            var line = $"[press] bay 2: {need} {a.What} ({a.Base} + {a.Rate} x {a.By.ToString().ToLowerInvariant()}, at most {a.Max})";
            if (bay2 != null && bay2.Split('=') is [var what, var num] && int.TryParse(num, out var have))
                line += what.Equals(a.What, StringComparison.OrdinalIgnoreCase) ? have >= need ? $": {have} is enough" : $": needs {need}, holds {have}" : $": {what} isn't what bay 2 takes";
            Console.WriteLine(line);
        }
        else
            Console.WriteLine("[press] bay 2: nothing");
        foreach (var s in p.Scale)
            Console.WriteLine($"[press] {s.Field} {Field(made, s.Field):0.###} (preset {(Field(p.Like, s.Field) == 0 ? s.Ref : Field(p.Like, s.Field)):0.###} at {Measure(s.By, p.Preset):0.###} {s.By.ToString().ToLowerInvariant()})");
        var pc = PressClass(Across(k));
        Console.WriteLine($"[press] seconds: {string.Join(", ", Enumerable.Range(Math.Max(0, pc), 4 - Math.Max(0, pc)).Select(c => $"{ClassWord(c)} {Seconds(die!, k, c):0.##}"))}");
        Console.WriteLine($"[press] {probs} problems");
        return probs;
    }

    /// <summary>Makes the part twice from its name (as a server and a client would), prints its numbers and checks; the problems found.</summary>
    private static int Report(PressDie die, PressPattern p, in ExtrusionKey k, string name, out ConstructPartDef made, out double ms)
    {
        var probs = 0;
        var bytes0 = GC.GetAllocatedBytesForCurrentThread();
        var t0 = Stopwatch.GetTimestamp();
        made = Build(die, p, k, name);
        var first = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        var bytes = GC.GetAllocatedBytesForCurrentThread() - bytes0;
        ConstructPartDef again = made;
        ms = first;
        for (var rep = 0; rep < 3; rep++) // (a client's make from the name; the least of three, as two other agents' builds share the box)
        {
            var t1 = Stopwatch.GetTimestamp();
            again = Build(die, p, k, name);
            ms = Math.Min(ms, Stopwatch.GetElapsedTime(t1).TotalMilliseconds);
        }
        var hash = GeometryHash(made);
        var pc = PressClass(Across(k));
        var drawn = made.Boxes.Count(b => !b.Hidden);
        Console.WriteLine($"[press] {made.Name}: like {p.Like.Name}; {ClassWord(pc)} Press and up, a {ClassWord(pc)} {die.Family} Die; "
            + $"{drawn} boxes drawn, {made.SolidBoxes} solid, {made.Sockets.Length} sockets; {made.Mass:0.##} kg, {made.Health:0} hp, armour {made.Armour:0.#}; "
            + $"bounds {(made.BoundsMax - made.BoundsMin).X:0.###} x {(made.BoundsMax - made.BoundsMin).Y:0.###} x {(made.BoundsMax - made.BoundsMin).Z:0.###} m; hash {hash:x8}; a make {ms:0.000} ms (least of 4; the first {first:0.000} ms), {bytes / 1024.0:0.0} KB");
        if (GeometryHash(again) != hash)
        {
            Console.WriteLine("[press] PROBLEM: a second make from the name gives another part");
            probs++;
        }
        var hits = ZFight.Find(ZFight.Boxes(made));
        foreach (var h in hits.Take(4))
            Console.WriteLine($"[press] PROBLEM: z-fighting: {ZFight.Describe(ZFight.Boxes(made), h)}");
        probs += hits.Count;
        if (made.Sockets.Length > PressTable.MaxSockets + 40 || made.Sockets.Select(s => s.Name).Distinct().Count() != made.Sockets.Length)
        {
            Console.WriteLine("[press] PROBLEM: its sockets repeat a name or are too many");
            probs++;
        }
        if (name.Length >= 80)
        {
            Console.WriteLine($"[press] PROBLEM: the key is {name.Length} characters (under 80 by design)");
            probs++;
        }
        return probs;
    }

    private static int Sweep(PressTable t, string dieId, int samples)
    {
        if (t.Die(dieId) is not { } die)
        {
            Console.WriteLine($"[press] no die \"{dieId}\"");
            return 1;
        }
        var rng = new Random(1);
        var probs = 0;
        var made = 0;
        var worst = 0.0;
        var worstKey = "";
        // The die's corners first (its largest blanks: the first make's worst case), then seeded blanks across its bounds.
        var keys = new List<string>();
        foreach (var p in die.Patterns)
            foreach (var s in p.Blanks.Length > 0 ? p.Blanks : die.Blanks)
            {
                var m = die.Materials[0];
                int aMax = Step((int)Math.Min(die.AcrossMax, die.LengthMax / die.AspectMin)), aMin = Step(Math.Max(die.AcrossMin, MinCm));
                keys.Add(KeyFor(die, p, s, aMax, Step((int)Math.Min(die.LengthMax, aMax * die.AspectMax)), m));
                keys.Add(KeyFor(die, p, s, aMin, Step((int)Math.Max(die.LengthMin, aMin * die.AspectMin)), m));
            }
        var tries = 0;
        while (keys.Count < samples + die.Patterns.Length * 4 && tries++ < samples * 50)
        {
            var p = die.Patterns[rng.Next(die.Patterns.Length)];
            var secs = p.Blanks.Length > 0 ? p.Blanks : die.Blanks;
            var s = secs[rng.Next(secs.Length)];
            var across = Step((int)MathF.Exp(Lerp(MathF.Log(Math.Max(die.AcrossMin, MinCm)), MathF.Log(die.AcrossMax), rng.NextSingle())));
            var length = Step((int)(across * Lerp(die.AspectMin, die.AspectMax, rng.NextSingle())));
            var key = KeyFor(die, p, s, across, length, die.Materials[rng.Next(die.Materials.Length)]);
            if (TryParse(key, out _, out _, out _, out _))
                keys.Add(key);
        }
        foreach (var key in keys.Distinct())
        {
            if (!TryParse(key, out var d, out var p, out var k, out var why))
            {
                Console.WriteLine($"[press] skipped {key}: {why}");
                continue;
            }
            var bad = Report(d!, p!, k, key, out _, out var ms);
            if (ms > worst)
                (worst, worstKey) = (ms, key);
            probs += bad;
            made++;
        }
        // 200 new keys in one update: what a Full naming them all costs a client (made and adopted, then freed by the next sweep).
        var batch = new List<string>(200);
        for (var across = Step(Math.Max(die.AcrossMin, MinCm)); batch.Count < 200 && across <= die.AcrossMax; across += 4 * Extrusion.StepCm)
            for (var len = die.LengthMin; batch.Count < 200 && len <= die.LengthMax && len <= across * die.AspectMax; len += 4 * Extrusion.StepCm)
            {
                var p = die.Patterns[batch.Count % die.Patterns.Length];
                var s = (p.Blanks.Length > 0 ? p.Blanks : die.Blanks)[0];
                var key = KeyFor(die, p, s, across, len, die.Materials[0]);
                if (TryParse(key, out _, out _, out _, out _) && !t.PresetByKey.ContainsKey(key) && !batch.Contains(key))
                    batch.Add(key);
            }
        var b0 = Stopwatch.GetTimestamp();
        var ok = batch.Count(key => Parse(key) != null);
        var batchMs = Stopwatch.GetElapsedTime(b0).TotalMilliseconds;
        Console.WriteLine($"[press] sweep {die.Family}: {made} blanks, {probs} problems; slowest make {worst:0.000} ms ({worstKey}); {ok} of {batch.Count} new keys made in one go: {batchMs:0.00} ms");
        return probs;
    }

    private static string KeyFor(PressDie die, PressPattern p, int section, int across, int length, byte material)
    {
        var len = (ushort)Math.Clamp(length, Extrusion.StepCm, Extrusion.MaxLengthCm);
        if (section == PressBaySpec.RoundSection)
            return KeyOf(die, p, new ExtrusionKey(0, (ushort)across, (ushort)across, len, material, false, ExShape.Round));
        if (section == PressBaySpec.PipeSection)
            return KeyOf(die, p, new ExtrusionKey(0, (ushort)across, (ushort)across, len, material, false, ExShape.Pipe, (ushort)Math.Max(Extrusion.StepCm, across / 50 * Extrusion.StepCm)));
        var plate = section == PlateSection;
        int w = across, h = plate ? MinCm : across;
        if (section == PlateSection && across < MinCm)
            w = MinCm;
        return KeyOf(die, p, new ExtrusionKey(Extrusion.Presets[section].Mask, (ushort)w, (ushort)h, (ushort)Math.Clamp(length, Extrusion.StepCm, Extrusion.MaxLengthCm), material, false));
    }

    private static int Step(int cm) => Math.Max(MinCm, cm / Extrusion.StepCm * Extrusion.StepCm);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
