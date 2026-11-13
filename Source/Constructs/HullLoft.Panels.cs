using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop;

/// <summary>
/// Formed panels (docs/sandbox/constructs/production.md "The Panel Former"): bodywork a Panel Former makes from sheet. A panel is a run-time
/// part made from its name, as a lofted hull bay is, so blueprints, saves, the wire, racks and conveyors carry it with no second path:
/// <c>Formed Panel (hood 180x140 c12 r8 t20 steel)</c>: a kind, its length and width in cm, its crown (how far the middle stands proud
/// across the width, cm), its rake (the kind's second number, cm: a fender's arch, an S-bend's offset, a hood's nose drop), its thickness in
/// mm and its material. The kit's own lofts (<c>Hull Loft (...)</c>) are formed panels too: the Panel Former makes both from their names.
/// </summary>
public static partial class HullLoft
{
    public const string PanelPrefix = "Formed Panel (";
    public const int PanelMaxAlong = 8, PanelMaxAcross = 6; // facets: at most 48 a panel (each a hull shape: a part's first make is 0.1 ms a facet)
    public const int PanelMinCm = 20, PanelMaxCm = 600, PanelStepCm = 5, PanelMaxCrownCm = 80, PanelMaxRakeCm = 200, PanelMinMm = 12, PanelMaxMm = 40;

    /// <summary>A preset: its key word, its name, and its default length, width, crown and rake (cm).</summary>
    public readonly record struct PanelPreset(string Word, string Label, int L, int W, int Crown, int Rake, string What);

    public static readonly PanelPreset[] PanelPresets =
    [
        new("curve", "Curved panel", 150, 100, 10, 0, "a plain panel crowned across its width; rake arches it along its length"),
        new("sbend", "S-bend", 200, 100, 0, 30, "a panel that steps by its rake along its length in an S"),
        new("hood", "Hood", 160, 150, 14, 18, "a crowned hood, narrower at the nose, the nose dropped by its rake"),
        new("fender", "Fender", 140, 45, 6, 45, "an arch over a wheel, as high as its rake"),
        new("door", "Door skin", 110, 120, 6, 8, "a door's outer skin: a gentle crown and the top turned in by its rake"),
        new("roofbow", "Roof bow", 200, 180, 22, 6, "a roof: flat in the middle, rolled down at the sides, arched along by its rake"),
        new("cowl", "Cowl", 60, 150, 30, 12, "a round cowl between a hood and a screen, rising by its rake"),
        new("bend", "Bent panel", 200, 100, 0, 0, "a flat sheet folded along a line across it: its angle, where along the sheet, and how tight"),
        new("loft", "Lofted panel", 200, 120, 0, 0, "a sheet ruled between two edge profiles: eight heights across its back edge and eight across its nose, the nose its own width"),
    ];

    /// <summary>A panel's key, read: its preset, sizes in cm, thickness in mm and material.</summary>
    public readonly record struct PanelShape(int Kind, int L, int W, int Crown, int Rake, int Mm, string Material, int Angle = 0, int At = 0, int Radius = 0,
        int NoseW = 0, ulong Back = 0, ulong Nose = 0);

    /// <summary>The bent panel's preset (its key has three more words: <c>a</c> the fold's angle in degrees, up positive; <c>p</c> where its middle lies along the sheet, cm; <c>b</c> its inside radius, cm).</summary>
    public const int PanelBend = 7, PanelMaxAngle = 90, PanelMinRadiusCm = 2, PanelMaxRadiusCm = 100;

    /// <summary>
    /// The lofted panel's preset (phase 3): a sheet ruled between two edge profiles. Its key: <c>Formed Panel (loft LxWxN tT bH,H,.. fH,H,.. MATERIAL)</c>,
    /// <c>N</c> the nose's width (cm), <c>b</c> the back edge's eight heights across it and <c>f</c> the nose's (cm, -99 to 99, from the left
    /// side to the right). The heights ride in <see cref="PanelShape.Back"/> and <see cref="PanelShape.Nose"/>, a signed byte each (<see cref="ProfileAt"/>),
    /// so the shape stays a plain value. Crown and rake are 0: the profiles are the shape. At most 102 characters and the material's name.
    /// </summary>
    public const int PanelLoft = 8, PanelProfilePoints = 8, PanelMaxProfileCm = 99;

    /// <summary>A loft profile's height <paramref name="k"/> (0 the left side, 7 the right), cm.</summary>
    public static int ProfileAt(ulong profile, int k) => (sbyte)(byte)(profile >> (8 * k));

    /// <summary>A loft profile with height <paramref name="k"/> set to <paramref name="cm"/> (clamped to the range).</summary>
    public static ulong ProfileSet(ulong profile, int k, int cm) =>
        profile & ~(0xFFUL << (8 * k)) | (ulong)(byte)(sbyte)Math.Clamp(cm, -PanelMaxProfileCm, PanelMaxProfileCm) << (8 * k);

    /// <summary>A profile from its heights, cm (the left side first).</summary>
    public static ulong Profile(params ReadOnlySpan<int> cm)
    {
        var p = 0UL;
        for (var k = 0; k < PanelProfilePoints && k < cm.Length; k++)
            p = ProfileSet(p, k, cm[k]);
        return p;
    }

    public static string ProfileText(ulong p)
    {
        var sb = new StringBuilder(32);
        for (var k = 0; k < PanelProfilePoints; k++)
            sb.Append(k == 0 ? "" : ",").Append(ProfileAt(p, k).ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    public static bool ProfileRead(string s, out ulong p)
    {
        p = 0;
        var w = s.Split(',');
        if (w.Length != PanelProfilePoints)
            return false;
        for (var k = 0; k < PanelProfilePoints; k++)
        {
            if (!int.TryParse(w[k], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) || Math.Abs(v) > PanelMaxProfileCm)
                return false;
            p = ProfileSet(p, k, v);
        }
        return true;
    }

    /// <summary>A profile's height x across (-1 the left side, 1 the right), metres: a Catmull-Rom curve through its eight heights, evenly spaced.</summary>
    public static float ProfileHeight(ulong p, float x)
    {
        var t = Math.Clamp((x + 1) * 0.5f, 0, 1) * (PanelProfilePoints - 1);
        var i = Math.Min((int)t, PanelProfilePoints - 2);
        var f = t - i;
        float H(int k) => k < 0 ? 2 * ProfileAt(p, 0) - ProfileAt(p, 1) : k >= PanelProfilePoints ? 2 * ProfileAt(p, PanelProfilePoints - 1) - ProfileAt(p, PanelProfilePoints - 2) : ProfileAt(p, k);
        float p0 = H(i - 1), p1 = H(i), p2 = H(i + 1), p3 = H(i + 2);
        var v = 0.5f * (2 * p1 + (-p0 + p2) * f + (2 * p0 - 5 * p1 + 4 * p2 - p3) * f * f + (-p0 + 3 * p1 - 3 * p2 + p3) * f * f * f);
        return v / 100f;
    }

    public static bool IsPanelName(string name) => name.StartsWith(PanelPrefix, StringComparison.Ordinal);

    /// <summary>A named part a Panel Former makes: a formed panel or one of the vehicle kit's lofted parts.</summary>
    public static bool IsFormedName(string name) => IsPanelName(name) || name.StartsWith(Prefix, StringComparison.Ordinal);

    public static string PanelKey(in PanelShape s) => s.Kind == PanelLoft
        ? string.Create(CultureInfo.InvariantCulture, $"{PanelPrefix}loft {s.L}x{s.W}x{s.NoseW} t{s.Mm} b{ProfileText(s.Back)} f{ProfileText(s.Nose)} {s.Material})")
        : s.Kind == PanelBend
        ? string.Create(CultureInfo.InvariantCulture, $"{PanelPrefix}{PanelPresets[s.Kind].Word} {s.L}x{s.W} c{s.Crown} r{s.Rake} t{s.Mm} a{s.Angle} p{s.At} b{s.Radius} {s.Material})")
        : string.Create(CultureInfo.InvariantCulture, $"{PanelPrefix}{PanelPresets[s.Kind].Word} {s.L}x{s.W} c{s.Crown} r{s.Rake} t{s.Mm} {s.Material})");

    /// <summary>A preset at its default size, 20 mm thick.</summary>
    public static PanelShape PanelDefault(int kind, string material = "steel")
    {
        var p = PanelPresets[Math.Clamp(kind, 0, PanelPresets.Length - 1)];
        kind = Math.Clamp(kind, 0, PanelPresets.Length - 1);
        return kind == PanelBend ? new PanelShape(kind, p.L, p.W, p.Crown, p.Rake, 20, material, 45, p.L / 2, 10)
            : kind == PanelLoft ? new PanelShape(kind, p.L, p.W, 0, 0, 20, material, NoseW: 100, Back: Profile(0, 8, 13, 15, 15, 13, 8, 0), Nose: Profile(-6, 0, 4, 6, 6, 4, 0, -6))
            : new PanelShape(kind, p.L, p.W, p.Crown, p.Rake, 20, material);
    }

    /// <summary>Why a panel can't be made, or null. A key past a range is refused, never clamped, so every peer makes the same part or none.</summary>
    public static string? PanelRefusal(in PanelShape s)
    {
        if ((uint)s.Kind >= (uint)PanelPresets.Length)
            return "not a panel: no such preset";
        if (s.L < PanelMinCm || s.L > PanelMaxCm || s.W < PanelMinCm || s.W > PanelMaxCm || s.L % PanelStepCm != 0 || s.W % PanelStepCm != 0)
            return $"a panel is {PanelMinCm} to {PanelMaxCm} cm long and wide, in {PanelStepCm} cm steps";
        if (s.Crown < 0 || s.Crown > PanelMaxCrownCm || s.Crown > s.W / 2)
            return $"a panel's crown is 0 to {PanelMaxCrownCm} cm, at most half its width";
        if (Math.Abs(s.Rake) > PanelMaxRakeCm || Math.Abs(s.Rake) > s.L)
            return $"a panel's rake is within {PanelMaxRakeCm} cm and its own length";
        if (s.Mm < PanelMinMm || s.Mm > PanelMaxMm)
            return $"a panel is {PanelMinMm} to {PanelMaxMm} mm thick";
        if (s.Kind != PanelBend && (s.Angle != 0 || s.At != 0 || s.Radius != 0))
            return "only a bent panel has a fold";
        if (s.Kind != PanelLoft && (s.NoseW != 0 || s.Back != 0 || s.Nose != 0))
            return "only a lofted panel has edge profiles";
        if (s.Kind == PanelLoft)
        {
            if (s.Crown != 0 || s.Rake != 0)
                return "a lofted panel has no crown or rake: its profiles are its shape";
            if (s.NoseW < PanelMinCm || s.NoseW > PanelMaxCm || s.NoseW % PanelStepCm != 0)
                return $"a lofted panel's nose is {PanelMinCm} to {PanelMaxCm} cm wide, in {PanelStepCm} cm steps";
            for (var k = 0; k < PanelProfilePoints; k++)
                if (Math.Abs(ProfileAt(s.Back, k)) > PanelMaxProfileCm || Math.Abs(ProfileAt(s.Nose, k)) > PanelMaxProfileCm)
                    return $"a profile's heights are within {PanelMaxProfileCm} cm";
        }
        if (s.Kind == PanelBend)
        {
            if (s.Rake != 0)
                return "a bent panel has no rake: its fold is its angle";
            if (Math.Abs(s.Angle) > PanelMaxAngle)
                return $"a fold is within {PanelMaxAngle} degrees";
            if (s.Radius < PanelMinRadiusCm || s.Radius > PanelMaxRadiusCm)
                return $"a fold's radius is {PanelMinRadiusCm} to {PanelMaxRadiusCm} cm";
            if (s.At < 0 || s.At > s.L)
                return "a fold lies on the sheet: within its length";
            if (s.Radius * MathF.Abs(s.Angle) * MathF.PI / 180f > s.L)
                return "that fold is longer than the sheet: a smaller radius or angle";
        }
        if (RecyclingTable.Current.IdOf(s.Material) is var m && (m == 0 || ExtrusionTable.Current.Find(m) == null))
            return $"a panel can't be formed of {s.Material}: takes {ExtrusionTable.Current.Takes()}";
        if (s.Kind == PanelLoft && PanelKey(s).Length > ConstructParts.MaxRuntimeName)
            return "that key is too long for a part's name";
        return null;
    }

    /// <summary>A panel's key read back, or null: not a panel's name as a machine would write it.</summary>
    public static PanelShape? PanelOf(string name)
    {
        if (!IsPanelName(name) || !name.EndsWith(')'))
            return null;
        var w = name[PanelPrefix.Length..^1].Split(' ');
        if (w.Length < 6)
            return null;
        var kind = Array.FindIndex(PanelPresets, p => p.Word == w[0]);
        var size = w[1].Split('x');
        static bool Int(string s, out int v) => int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v);
        if (kind == PanelLoft)
        {
            if (size.Length != 3 || !Int(size[0], out var ll) || !Int(size[1], out var lw) || !Int(size[2], out var ln)
                || w[2].Length < 2 || w[2][0] != 't' || !Int(w[2][1..], out var lmm)
                || w[3].Length < 2 || w[3][0] != 'b' || !ProfileRead(w[3][1..], out var back)
                || w[4].Length < 2 || w[4][0] != 'f' || !ProfileRead(w[4][1..], out var nose))
                return null;
            var ls = new PanelShape(kind, ll, lw, 0, 0, lmm, string.Join(' ', w[5..]), NoseW: ln, Back: back, Nose: nose);
            return PanelRefusal(ls) == null && PanelKey(ls) == name ? ls : null;
        }
        if (kind < 0 || size.Length != 2 || !Int(size[0], out var l) || !Int(size[1], out var wd)
            || w[2].Length < 2 || w[2][0] != 'c' || !Int(w[2][1..], out var crown)
            || w[3].Length < 2 || w[3][0] != 'r' || !Int(w[3][1..], out var rake)
            || w[4].Length < 2 || w[4][0] != 't' || !Int(w[4][1..], out var mm))
            return null;
        var (ang, at, rad, first) = (0, 0, 0, 5);
        if (kind == PanelBend)
        {
            if (w.Length < 9 || w[5].Length < 2 || w[5][0] != 'a' || !Int(w[5][1..], out ang) || w[6].Length < 2 || w[6][0] != 'p' || !Int(w[6][1..], out at)
                || w[7].Length < 2 || w[7][0] != 'b' || !Int(w[7][1..], out rad))
                return null;
            first = 8;
        }
        var s = new PanelShape(kind, l, wd, crown, rake, mm, string.Join(' ', w[first..]), ang, at, rad);
        return PanelRefusal(s) == null && PanelKey(s) == name ? s : null; // (one spelling a key: no "+8", no leading zeros)
    }

    /// <summary>The material a formed part is made of: a panel's own, steel for the kit's lofts (0: this build has no such material).</summary>
    public static byte FormedMaterial(string name) =>
        IsCutName(name) ? CutMaterial(name) : RecyclingTable.Current.IdOf(PanelOf(name) is { } s ? s.Material : "steel");

    /// <summary>What a formed part costs in units of its material: its mass at the material's weight, so it weighs what went in.</summary>
    public static int FormedUnits(ConstructPartDef d, byte material) =>
        Math.Max(1, (int)MathF.Ceiling(d.Mass / MathF.Max(0.01f, RecyclingTable.Current.WeightOf(material))));

    /// <summary>The panel's surface: its height at s along (0 the back edge, 1 the nose) and x across (-1 to 1), metres; and its half width there.</summary>
    private static float PanelHeight(in PanelShape p, float s, float x)
    {
        float c = p.Crown / 100f, r = p.Rake / 100f;
        var bow = 1 - x * x;
        return p.Kind switch
        {
            0 => c * bow + r * 4 * s * (1 - s), // curve
            1 => c * bow + r * s * s * (3 - 2 * s), // sbend
            2 => c * bow * (1 - 0.35f * s) - r * s * s, // hood
            3 => c * bow + r * MathF.Sin(MathF.PI * s), // fender
            4 => c * bow + r * 0.25f * (x + 1) * (x + 1), // door: x = 1 is its top edge
            5 => c * (1 - x * x * x * x) + r * 4 * s * (1 - s), // roofbow
            PanelBend => c * bow, // (the fold is in PanelPoint: it moves the sheet along as well as up)
            PanelLoft => ProfileHeight(p.Back, x) * (1 - s) + ProfileHeight(p.Nose, x) * s, // ruled: a straight line from each back point to its nose point
            _ => c * MathF.Sqrt(MathF.Max(0, 1 - x * x * 0.96f)) + r * s, // cowl
        };
    }

    private static float PanelHalf(in PanelShape p, float s) => p.Kind == PanelLoft ? (p.W * (1 - s) + p.NoseW * s) / 200f : p.W / 200f * (p.Kind == 2 ? 1 - 0.18f * s * s : 1);

    /// <summary>The facets a key takes: (along, across).</summary>
    public static (int Along, int Across) PanelGrid(in PanelShape p)
    {
        if (p.Kind == PanelBend)
            return (BendRows(p, null), p.Crown > 0 ? Math.Clamp(p.W / 20, 4, PanelMaxAcross) : 1);
        if (p.Kind == PanelLoft)
        {
            // Across: a facet between each two of the eight heights (both profiles straight: one). Along: the rulings are straight, so one
            // facet when the two edges are alike or both straight, else rows enough for the twist between them (at most 6: 42 facets).
            var flat = true;
            for (var k = 0; k < PanelProfilePoints; k++)
                flat &= ProfileAt(p.Back, k) == ProfileAt(p.Back, 0) && ProfileAt(p.Nose, k) == ProfileAt(p.Nose, 0);
            var alike = p.Back == p.Nose && p.NoseW == p.W;
            return (alike || flat ? 1 : Math.Clamp(p.L / 30, 2, 6), flat ? 1 : PanelProfilePoints - 1);
        }
        var bent = p.Rake != 0 && p.Kind is not (4 or 6);
        var along = bent ? Math.Clamp(p.L / 20, 5, PanelMaxAlong) : p.Kind == 2 ? 4 : 1;
        var across = p.Crown > 0 || p.Kind == 4 && p.Rake != 0 ? Math.Clamp(p.W / 20, 4, PanelMaxAcross) : 1;
        return (along, across);
    }

    private static Vector3 PanelPoint(in PanelShape p, float s, float x)
    {
        if (p.Kind != PanelBend)
            return new(x * PanelHalf(p, s), PanelHeight(p, s, x), s * p.L / 100f);
        // A fold: flat, an arc of the fold's radius, flat again at its angle. s runs along the sheet itself, so the sheet's length is what was cut.
        BendSpan(p, out var u0, out var u1, out var rad, out var a);
        var u = s * p.L / 100f;
        float z, y;
        if (u <= u0 || a == 0)
            (z, y) = (u, 0);
        else
        {
            var th = (MathF.Min(u, u1) - u0) / rad;
            var sg = MathF.Sign(a);
            (z, y) = (u0 + rad * MathF.Sin(th), sg * rad * (1 - MathF.Cos(th)));
            if (u > u1)
                (z, y) = (z + (u - u1) * MathF.Cos(a), y + (u - u1) * MathF.Sin(a));
        }
        return new(x * PanelHalf(p, s), y + PanelHeight(p, s, x), z);
    }

    /// <summary>A fold's arc along the sheet (metres from its back edge), its radius and its signed angle (radians).</summary>
    private static void BendSpan(in PanelShape p, out float u0, out float u1, out float rad, out float a)
    {
        a = p.Angle * MathF.PI / 180f;
        rad = p.Radius / 100f;
        var len = rad * MathF.Abs(a);
        var total = p.L / 100f;
        u0 = Math.Clamp(p.At / 100f - len / 2, 0, MathF.Max(0, total - len));
        u1 = u0 + len;
    }

    /// <summary>
    /// A bent panel's rows along the sheet: its back edge, the fold's start, a row every 22.5 degrees of it (at most 5 facets), its end and
    /// the nose; a flat stretch shorter than 2 cm is left out. Returns the facets along; <paramref name="s"/> (if given) takes each row's place, 0 to 1.
    /// </summary>
    private static int BendRows(in PanelShape p, Span<float> s)
    {
        BendSpan(p, out var u0, out var u1, out _, out var a);
        var total = p.L / 100f;
        var arc = a == 0 ? 0 : Math.Clamp((int)MathF.Ceiling(MathF.Abs(p.Angle) / 22.5f), 1, 5);
        var n = 0;
        void Row(float u, Span<float> into)
        {
            if (!into.IsEmpty)
                into[n] = u / total;
            n++;
        }
        Row(0, s);
        if (arc == 0)
        {
            Row(total, s);
            return n - 1;
        }
        if (u0 > 0.02f)
            Row(u0, s);
        for (var k = 1; k < arc; k++)
            Row(u0 + (u1 - u0) * k / arc, s);
        if (total - u1 > 0.02f)
            Row(u1, s);
        Row(total, s);
        return n - 1;
    }

    /// <summary>Where row <paramref name="i"/> of <paramref name="nu"/> lies along the sheet, 0 to 1: even steps, but a bent panel's rows follow its fold.</summary>
    public static float PanelRowS(in PanelShape p, int i, int nu)
    {
        if (p.Kind != PanelBend)
            return i / (float)nu;
        Span<float> rows = stackalloc float[PanelMaxAlong + 1];
        BendRows(p, rows);
        return rows[Math.Clamp(i, 0, nu)];
    }

    private static Vector3 PanelNormal(in PanelShape p, float s, float x)
    {
        const float e = 0.002f;
        var du = PanelPoint(p, MathF.Min(1, s + e), x) - PanelPoint(p, MathF.Max(0, s - e), x);
        var dv = PanelPoint(p, s, MathF.Min(1, x + e)) - PanelPoint(p, s, MathF.Max(-1, x - e));
        var n = Vector3.Cross(du, dv);
        return n.LengthSquared() < 1e-12f ? Vector3.UnitY : Vector3.Normalize(n);
    }

    /// <summary>
    /// Force every panel to be made whole when it is first asked for (tools; a probe's "before"). Otherwise, once a game pumps the queue
    /// (<see cref="PumpPanels"/>), a new key gets a cheap part at once (its collision boxes, sockets, mass and a plain slab to draw) and its
    /// facets are made in slices over the following ticks and swapped in: a part's first make was 5 to 15 ms, over the 8 ms a tick may take.
    /// </summary>
    internal static bool PanelSync;
    private static bool _panelPumped; // a game loop pumps the queue: new keys are deferred
    private static long _panelSpent; // stopwatch ticks the placeholders made since the last pump took: they come off its budget
    private const int PanelSlice = 6; // facets a slice (one small parse: about 1 ms)

    private sealed class PanelJob
    {
        public required ConstructPartDef Def;
        public PanelShape Shape;
        public CutPlan? Cut; // (a cut part: HullLoft.Cuts.cs)
        public int Next, Total;
        public readonly List<CPartBox> Facets = new(PanelMaxAlong * PanelMaxAcross);
        public readonly List<ushort> Shapes = new(PanelMaxAlong * PanelMaxAcross);
    }

    private static readonly Queue<PanelJob> PanelJobs = new();

    /// <summary>Panels whose facets are still to make.</summary>
    public static int PanelsPending
    {
        get
        {
            lock (PanelJobs)
                return PanelJobs.Count;
        }
    }

    /// <summary>(HullLoft.Make's hook.) A formed panel from its name; null: not a panel's name at all.</summary>
    private static (bool Ok, ConstructPartDef? Def)? MakePanel(string name)
    {
        if (!IsPanelName(name))
            return null;
        if (PanelOf(name) is not { } sh)
            return (false, null);
        try
        {
            var (nu, nv) = PanelGrid(sh);
            var defer = _panelPumped && !PanelSync;
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var def = ConstructParts.Parse(PanelJson(name, sh, 0, defer ? 0 : nu * nv, defer), name);
            if (Adopt(def, name) is not { } made)
                return (false, null);
            if (defer)
                lock (PanelJobs)
                {
                    PanelJobs.Enqueue(new PanelJob { Def = made, Shape = sh, Total = nu * nv });
                    _panelSpent += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                }
            return (true, made);
        }
        catch (ConstructPartException e)
        {
            Console.WriteLine($"[panels] {name}: refused, {e.Message}");
            return (false, null);
        }
    }

    /// <summary>
    /// Once a tick or a frame (Game.Update): slices of the waiting panels' facets, oldest first, until <paramref name="budgetMs"/> is spent
    /// (every key that came in one packet shares it); a finished panel's facets are swapped in for its slabs and the bodies that draw it are
    /// told. Nothing to do: one lock and a count. <paramref name="budgetMs"/> negative: all of them, now (a tool).
    /// </summary>
    public static void PumpPanels(double budgetMs = 2.0)
    {
        _panelPumped = true;
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var swapped = false;
        if (budgetMs >= 0)
            lock (PanelJobs)
            {
                if (PanelJobs.Count == 0)
                {
                    _panelSpent = 0;
                    return;
                }
                // What the new keys' first parts cost since the last pump comes off this one's budget: a packet of many new keys isn't followed by slices too.
                budgetMs -= _panelSpent * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                _panelSpent = 0;
                if (budgetMs <= 0)
                    return;
            }
        while (true)
        {
            PanelJob? job;
            lock (PanelJobs)
                job = PanelJobs.Count > 0 ? PanelJobs.Peek() : null;
            if (job == null)
                break;
            if (job.Def.Reclaimed)
                job.Next = job.Total; // (freed before it was finished: nothing to make)
            else if (job.Next < job.Total)
            {
                var to = Math.Min(job.Total, job.Next + PanelSlice);
                var outer = PartShapes.BeginCollect();
                try
                {
                    var part = ConstructParts.ParseBody(PanelJson(job.Def.Name, job.Shape, job.Next, to, false, job.Cut), job.Def.Name);
                    for (var k = part.SolidBoxes; k < part.Boxes.Length; k++)
                        job.Facets.Add(part.Boxes[k]);
                }
                catch (ConstructPartException e)
                {
                    Console.WriteLine($"[panels] {job.Def.Name}: facets {job.Next} to {to} refused, {e.Message}");
                }
                finally
                {
                    job.Shapes.AddRange(PartShapes.EndCollect(outer));
                }
                job.Next = to;
            }
            if (job.Next >= job.Total)
            {
                lock (PanelJobs)
                    PanelJobs.Dequeue();
                if (!job.Def.Reclaimed && job.Facets.Count > 0)
                {
                    var d = job.Def;
                    var boxes = new CPartBox[d.SolidBoxes + job.Facets.Count];
                    Array.Copy(d.Boxes, boxes, d.SolidBoxes);
                    job.Facets.CopyTo(boxes, d.SolidBoxes);
                    d.Boxes = boxes; // (its solid boxes, bounds, sockets and mass are as they were: only what is drawn changes)
                    d.OwnShapes = [.. d.OwnShapes ?? [], .. job.Shapes];
                    swapped = true;
                }
                else
                    PartShapes.Release([.. job.Shapes]);
            }
            if (budgetMs >= 0 && System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds >= budgetMs)
                break;
        }
        if (swapped)
            RuntimeParts.Redrawn();
    }

    /// <summary>
    /// A panel's part file: its hidden boxes, sockets and numbers always, and of its facets (counted along, then across) those from
    /// <paramref name="from"/> up to <paramref name="to"/>. <paramref name="slabs"/>: a plain drawn slab over each hidden box, the look of a
    /// panel whose facets aren't made yet. The same floats in the same order whatever the range, so a part made whole and one made in slices are equal.
    /// </summary>
    private static string PanelJson(string name, in PanelShape sh, int from, int to, bool slabs, CutPlan? cut = null)
    {
        var p = new Builder();
        var (nu, nv) = PanelGrid(sh);
        var t = cut?.Thick ?? sh.Mm / 1000f;
        var area = 0f;
        var item = 0; // (a cut part: whole facets and pieces counted in one run, so its slices are ranges of it)
        var face = cut is { Plate: true } ? Hex(RecyclingTable.Current.ColourOf(cut.Stock.Material)) : null;
        var grid = new Vector3[nu + 1, nv + 1];
        var normal = new Vector3[nu + 1, nv + 1];
        for (var i = 0; i <= nu; i++)
            for (var j = 0; j <= nv; j++)
            {
                var (s, x) = (PanelRowS(sh, i, nu), j * 2f / nv - 1);
                grid[i, j] = PanelPoint(sh, s, x);
                normal[i, j] = PanelNormal(sh, s, x);
            }
        for (var i = 0; i < nu; i++)
            for (var j = 0; j < nv; j++)
            {
                Vector3[] q = [grid[i, j], grid[i, j + 1], grid[i + 1, j + 1], grid[i + 1, j]];
                Vector3[] off = [normal[i, j], normal[i, j + 1], normal[i + 1, j + 1], normal[i + 1, j]]; // (neighbours share each corner's offset: the seams close)
                var n = Vector3.Cross(q[2] - q[0], q[1] - q[3]);
                // Rows shade a little by height, as a loft's, so a paint's tone shows the shape.
                var tone = face ?? ((i + j) % 2 == 0 ? Steel : SteelHigh);
                if (cut?.Pieces[i * nv + j] is { } pieces)
                {
                    // A facet the cuts touch: the pieces it keeps, each on its own quad, and only their area.
                    double u0 = i == nu ? sh.L : PanelRowS(sh, i, nu) * (double)sh.L, u1 = i + 1 == nu ? sh.L : PanelRowS(sh, i + 1, nu) * (double)sh.L;
                    double v0 = j * (double)sh.W / nv, v1 = (j + 1) * (double)sh.W / nv;
                    foreach (var piece in pieces)
                    {
                        PieceOnQuad(piece, u0, u1, v0, v1, q, off, out var pts, out var offs);
                        area += PolyArea(pts);
                        if (item >= from && item < to)
                            p.Facet(pts, n.LengthSquared() < 1e-12f ? Vector3.UnitY : n, nv == 1 && nu == 1 ? face ?? Steel : tone, t, off: offs);
                        item++;
                    }
                    continue;
                }
                area += n.Length() / 2;
                if (cut != null ? item >= from && item < to : i * nv + j >= from && i * nv + j < to)
                    p.Facet(q, n.LengthSquared() < 1e-12f ? Vector3.UnitY : n, nv == 1 && nu == 1 ? face ?? Steel : tone, t, off: off);
                item++;
            }
        if (cut is { Plate: true })
            return PlateJson(name, cut, p, area, slabs);
        if (cut != null)
            CutPanelCollision(p, sh, cut, slabs, Steel); // (a cut panel: its boxes round the openings, its edge sockets less those cut away)
        else
            PanelCollision(p, sh, nu, grid, slabs);
        return PanelNumbers(name, sh, cut, p, area, t);
    }

    /// <summary>An uncut panel's collision and sockets: up to four hidden boxes along its length, and its edge sockets on them.</summary>
    private static void PanelCollision(Builder p, in PanelShape sh, int nu, Vector3[,] grid, bool slabs)
    {
        var nv = grid.GetLength(1) - 1;
        // Collision: up to four hidden boxes along its length, each over its strip's own height (a growing or riding panel collides by boxes, never a
        // hull). Their sides are whole centimetres just inside the drawn edges, so no hidden face shares a plane with a facet's rim and the edge
        // sockets, which stand on them, are whole centimetres too (docs/LESSONS.md, "Generated geometry").
        static float In(float v, bool up) => up ? MathF.Ceiling(v * 100 + 0.2f) / 100 : MathF.Floor(v * 100 - 0.2f) / 100;
        var strips = Math.Min(4, nu);
        var boxes = new (Vector3 Mn, Vector3 Mx)[strips];
        for (var k = 0; k < strips; k++)
        {
            int i0 = k * nu / strips, i1 = (k + 1) * nu / strips;
            var (mn, mx) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
            for (var i = i0; i <= i1; i++)
                for (var j = 0; j <= nv; j++)
                    (mn, mx) = (Vector3.Min(mn, grid[i, j]), Vector3.Max(mx, grid[i, j]));
            mn = new Vector3(In(mn.X, true), MathF.Round(mn.Y, 2), k == 0 ? In(mn.Z, true) : MathF.Round(mn.Z, 2));
            mx = new Vector3(In(mx.X, false), MathF.Round(mx.Y, 2), k == strips - 1 ? In(mx.Z, false) : MathF.Round(mx.Z, 2));
            if (mx.Y - mn.Y < 0.03f)
                (mn.Y, mx.Y) = (mn.Y - 0.01f, mn.Y + 0.02f);
            if (sh.Kind == PanelBend && mx.Z - mn.Z < 0.03f) // (a bent panel's leg standing straight up: its strip has no length of its own)
                (mn.Z, mx.Z) = (MathF.Round(MathF.Min(mn.Z, mx.Z), 2) - 0.01f, MathF.Round(MathF.Min(mn.Z, mx.Z), 2) + 0.02f);
            boxes[k] = (mn, mx);
            p.Hide(mn, mx);
            if (slabs)
                p.Boxes.Add(new JsonObject { ["min"] = V(mn.X, mn.Y, mn.Z), ["max"] = V(mx.X, mx.Y, mx.Z), ["color"] = Steel, ["detail"] = true });
        }
        // Edge sockets, about every half metre: e/w along its sides, s/n across its ends, and one under its middle. Each stands on a face of the
        // hidden box of its strip, at the surface's height there.
        static float Cm2(float v) => MathF.Round(v, 2);
        int StripAt(float z)
        {
            for (var k = 0; k < strips; k++)
                if (z <= boxes[k].Mx.Z + 0.0001f)
                    return k;
            return strips - 1;
        }
        // (a bent panel's strips follow its rows, not its length: a leg folded up shares its z with the fold)
        var shc = sh; // (a copy: a local function can't read an in parameter)
        int StripOf(float s, float z)
        {
            if (shc.Kind != PanelBend)
                return StripAt(z);
            var row = 0;
            while (row < nu - 1 && PanelRowS(shc, row + 1, nu) <= s)
                row++;
            for (var k = 0; k < strips; k++)
                if (row < (k + 1) * nu / strips)
                    return k;
            return strips - 1;
        }
        var along = Math.Clamp(sh.L / 50, 1, 6);
        var across = Math.Clamp(sh.W / 50, 1, 6);
        for (var k = 0; k < along; k++)
        {
            var s = (k + 0.5f) / along;
            var z = Cm2(PanelPoint(sh, s, 0).Z);
            var (mn, mx) = boxes[StripOf(s, z)];
            z = Math.Clamp(z, mn.Z, mx.Z);
            p.Sock($"e{k}", new Vector3(mx.X, Math.Clamp(Cm2(PanelPoint(sh, s, 1).Y), mn.Y, mx.Y), z), "+x");
            p.Sock($"w{k}", new Vector3(mn.X, Math.Clamp(Cm2(PanelPoint(sh, s, -1).Y), mn.Y, mx.Y), z), "-x");
        }
        for (var k = 0; k < across; k++)
        {
            var x = (k + 0.5f) / across * 2 - 1;
            var (a0, a1) = boxes[0];
            var (b0, b1) = boxes[strips - 1];
            var ps = PanelPoint(sh, 0, x);
            var pn = PanelPoint(sh, 1, x);
            p.Sock($"s{k}", new Vector3(Math.Clamp(Cm2(ps.X), a0.X, a1.X), Math.Clamp(Cm2(ps.Y), a0.Y, a1.Y), a0.Z), "-z");
            if (sh.Kind == PanelBend && Math.Abs(sh.Angle) > 45) // (a leg folded past 45 degrees ends upward or downward: its edge sockets with it)
                p.Sock($"n{k}", new Vector3(Math.Clamp(Cm2(pn.X), b0.X, b1.X), sh.Angle > 0 ? b1.Y : b0.Y, Math.Clamp(Cm2(pn.Z), b0.Z, b1.Z)), sh.Angle > 0 ? "+y" : "-y");
            else
                p.Sock($"n{k}", new Vector3(Math.Clamp(Cm2(pn.X), b0.X, b1.X), Math.Clamp(Cm2(pn.Y), b0.Y, b1.Y), b1.Z), "+z");
        }
        var backZ = sh.Kind == PanelBend ? PanelPoint(sh, PanelRowS(sh, 1, nu) / 2, 0).Z : sh.L / 200f; // (a bent panel: under the middle of its first flat)
        var (m0, m1) = boxes[sh.Kind == PanelBend ? 0 : StripAt(backZ)];
        p.Sock("back", new Vector3(0, m0.Y, Math.Clamp(Cm2(backZ), m0.Z, m1.Z)), "-y");
    }

    /// <summary>A panel's (or cut panel's) part file from its drawn boxes, collision and sockets: mass and health by the area drawn.</summary>
    private static string PanelNumbers(string name, in PanelShape sh, CutPlan? cut, Builder p, float area, float t)
    {
        var mat = RecyclingTable.Current.IdOf(sh.Material);
        var row = ExtrusionTable.Current.Find(mat)!;
        var volume = area * t;
        var mass = MathF.Round(Math.Clamp(volume * row.UnitsPerM3 * RecyclingTable.Current.WeightOf(mat), 1, 4000), 1);
        var preset = PanelPresets[sh.Kind];
        var part = new JsonObject
        {
            ["format"] = "call-of-slop-construct-part/1",
            ["name"] = name,
            ["description"] = cut != null
                ? $"{preset.Label}, {sh.L / 100f:0.##} by {sh.W / 100f:0.##} m of {sh.Mm} mm {sh.Material}, with {cut.Cuts.Length} cut-out{(cut.Cuts.Length == 1 ? "" : "s")}. Cut by a Laser Cutter; weld it by its edges."
                : $"{preset.Label}, {sh.L / 100f:0.##} by {sh.W / 100f:0.##} m of {sh.Mm} mm {sh.Material}: {preset.What}. Formed by a Panel Former; weld it by its edges.",
            ["group"] = "Vehicle Body",
            ["section"] = cut != null ? "Cut parts" : "Formed panels",
            ["role"] = "panel",
            ["tool"] = "welder",
            ["mass"] = mass,
            ["health"] = MathF.Round(Math.Clamp(volume * row.HealthPerM3, 40, 4000)),
            ["armour"] = MathF.Round(Math.Clamp(row.ArmourPerCm * sh.Mm / 10f, 1, row.MaxArmour)),
            ["material"] = row.Class,
            ["color"] = Steel,
            ["attachSeconds"] = 2.0,
            ["stage"] = true, // (never offered in the menus: a Panel Former makes it)
            ["boxes"] = p.Boxes,
            ["sockets"] = p.Sockets,
        };
        return part.ToJsonString();
    }

    /// <summary>
    /// <c>--form-panel PRESET [length=cm] [width=cm] [crown=cm] [rake=cm] [thick=mm] [material=NAME]</c>, or a whole key in quotes: prints the
    /// key, its facets, boxes and sockets, its mass and cost, and the former tier that takes it; checks it builds and that a second make from
    /// the name gives the same part. <c>--form-panel list</c> prints the presets. The exit code is the number of problems.
    /// </summary>
    public static int PanelCli(string[] args)
    {
        if (args.Length == 0 || args[0] == "list")
        {
            foreach (var pr in PanelPresets)
                Console.WriteLine($"{pr.Word,-8} {pr.Label,-13} {PanelKey(PanelDefault(Array.IndexOf(PanelPresets, pr)))}  ({pr.What})");
            return 0;
        }
        string name;
        if (IsFormedName(args[0]) || IsCutName(args[0]))
            name = args[0];
        else
        {
            var kind = Array.FindIndex(PanelPresets, p => p.Word.Equals(args[0], StringComparison.OrdinalIgnoreCase));
            if (kind < 0)
            {
                Console.WriteLine($"no preset \"{args[0]}\": {string.Join(", ", PanelPresets.Select(p => p.Word))}");
                return 1;
            }
            var s = PanelDefault(kind);
            foreach (var a in args.Skip(1))
            {
                var kv = a.Split('=', 2);
                if (kv.Length != 2)
                {
                    Console.WriteLine($"\"{a}\": key=value (length, width, crown, rake, thick, material)");
                    return 1;
                }
                var num = int.TryParse(kv[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v);
                switch (kv[0].ToLowerInvariant())
                {
                    case "length" when num: s = s with { L = v }; break;
                    case "width" when num: s = s with { W = v }; break;
                    case "crown" when num: s = s with { Crown = v }; break;
                    case "rake" when num: s = s with { Rake = v }; break;
                    case "thick" when num: s = s with { Mm = v }; break;
                    case "angle" when num: s = s with { Angle = v }; break;
                    case "at" when num: s = s with { At = v }; break;
                    case "radius" when num: s = s with { Radius = v }; break;
                    case "nose" when num: s = s with { NoseW = v }; break;
                    case "back" when ProfileRead(kv[1], out var bp): s = s with { Back = bp }; break;
                    case "front" when ProfileRead(kv[1], out var fp): s = s with { Nose = fp }; break;
                    case "material": s = s with { Material = kv[1] }; break;
                    default:
                        Console.WriteLine($"\"{a}\": key=value (length, width, crown, rake in cm; thick in mm; material)");
                        return 1;
                }
            }
            if (PanelRefusal(s) is { } why)
            {
                Console.WriteLine($"refused: {why}");
                return 1;
            }
            name = PanelKey(s);
        }
        var made0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (IsCutName(name) && CutRefusal(name) is { } cutWhy)
        {
            Console.WriteLine($"refused: {cutWhy}");
            return 1;
        }
        if (Parse(name) is not { } d)
        {
            Console.WriteLine($"refused: \"{name}\" is not a panel any machine makes");
            return 1;
        }
        PumpPanels(-1); // (a tool: whole at once)
        var madeMs = System.Diagnostics.Stopwatch.GetElapsedTime(made0).TotalMilliseconds;
        var bad = 0;
        var mat = FormedMaterial(name);
        int facets = 0, hidden = 0;
        foreach (var b in d.Boxes)
        {
            if (b.Detail)
                facets++;
            else
                hidden++;
        }
        var size = d.BoundsMax - d.BoundsMin;
        Console.WriteLine(name);
        Console.WriteLine($"  facets {facets}, collision boxes {hidden}, sockets {d.Sockets.Length} ({string.Join(' ', d.Sockets.Select(s => s.Name))})");
        Console.WriteLine($"  bounds {size.X:0.00} x {size.Y:0.00} x {size.Z:0.00} m, mass {d.Mass:0.#} kg, health {d.Health:0}, armour {d.Armour:0}");
        if (IsCutName(name) && ReadCut(name, out _) is { } plan)
        {
            // What the cut takes away comes back as offcut (phase 3's bin): the blank's mass less the cut part's, in units of its material, to the
            // nearest tenth (the user, 2026-11-13: the cutter counts its offcut in tenths and hands out whole units as they add up).
            var blank = ConstructParts.Find(plan.Blank);
            PumpPanels(-1);
            var off = blank == null ? 0 : MathF.Round(MathF.Max(0, blank.Mass - d.Mass) / MathF.Max(0.01f, RecyclingTable.Current.WeightOf(mat)) * 10) / 10;
            Console.WriteLine($"  cuts {plan.Cuts.Length} ({string.Join(' ', plan.Cuts.Select(c => c.Text))}), drawn pieces {plan.Items} of {CutMaxFacets}, key {name.Length} of {ConstructParts.MaxRuntimeName} characters");
            Console.WriteLine($"  blank {plan.Blank}: {blank?.Mass ?? 0:0.#} kg; offcut {off:0.0} {RecyclingTable.Current.NameOf(mat)}");
        }
        else
            Console.WriteLine($"  cost {FormedUnits(d, mat)} {RecyclingTable.Current.NameOf(mat)}; made by: {ConstructPanelFormer.TierFor(d) ?? "no Panel Former is big enough"}");
        Console.WriteLine($"  hash {PanelHash(d):x8} (boxes, sockets, mass and health: the same on every peer); first make {madeMs:0.0} ms (the first part a process makes also pays the JIT)");
        // A second make from the name alone (the table forgotten) gives the same part.
        var first = PanelHash(d);
        lock (Interned)
            Interned.Remove(name);
        made0 = System.Diagnostics.Stopwatch.GetTimestamp();
        // (the first was made whole; this one as a running game makes it: a first part, then its facets in slices. They must be equal.)
        var again = Parse(name);
        var firstPart = System.Diagnostics.Stopwatch.GetElapsedTime(made0).TotalMilliseconds;
        PumpPanels(-1);
        if (again == null || PanelHash(again) != first)
        {
            Console.WriteLine("  PROBLEM: a second make from the name, in slices, differs from the part made whole");
            bad++;
        }
        else
            Console.WriteLine($"  a second make from the name, in slices: its first part {firstPart:0.0} ms, {System.Diagnostics.Stopwatch.GetElapsedTime(made0).TotalMilliseconds:0.0} ms in all, the same part");
        if (IsCutName(name) && facets > CutMaxFacets)
        {
            Console.WriteLine($"  PROBLEM: {facets} facets, over {CutMaxFacets}");
            bad++;
        }
        if (IsPanelName(name) && facets > PanelMaxAlong * PanelMaxAcross)
        {
            Console.WriteLine($"  PROBLEM: {facets} facets, over {PanelMaxAlong * PanelMaxAcross}");
            bad++;
        }
        return bad;
    }

    /// <summary>A hash of a part's geometry and sockets as made: boxes, hull points, socket places and directions. Equal on two peers when they made the same part.</summary>
    public static uint PanelHash(ConstructPartDef d)
    {
        var h = 2166136261u;
        void Mix(float v) => h = (h ^ (uint)BitConverter.SingleToInt32Bits(MathF.Round(v, 4) + 0f)) * 16777619u;
        foreach (var b in d.Boxes)
        {
            Mix(b.Center.X); Mix(b.Center.Y); Mix(b.Center.Z);
            Mix(b.Half.X); Mix(b.Half.Y); Mix(b.Half.Z);
            h = (h ^ (b.Detail ? 1u : 0u)) * 16777619u;
        }
        foreach (var s in d.Sockets)
        {
            Mix(s.At.X); Mix(s.At.Y); Mix(s.At.Z);
            h = (h ^ s.Dir) * 16777619u;
            foreach (var ch in s.Name)
                h = (h ^ ch) * 16777619u;
        }
        Mix(d.Mass);
        Mix(d.Health);
        return h;
    }
}
