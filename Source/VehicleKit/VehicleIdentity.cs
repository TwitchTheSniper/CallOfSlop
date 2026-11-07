using System.Numerics;
using System.Text;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// Which parts of a vehicle a mark or a weak point means: parts by name, and/or a role keyword: "fuel" (any fuel tank: a drum, a barrel), "tyres" (wheels),
/// "driver" (the driver's seat), "seat" (any seat), "engine", "ammo" (ammo boxes and crates).
/// </summary>
public sealed record PartPick(string[] Names, string Role = "")
{
    public static PartPick Of(params string[] names) => new(names);

    public static PartPick ByRole(string role, params string[] names) => new(names, role);
}

/// <summary>
/// A feature every vehicle of a style carries: at least <paramref name="Min"/> of the picked parts, on every vehicle that meets <paramref name="When"/>
/// (comma-separated, all must hold: "seat", "room", "heavy", "light"; empty: every vehicle). <paramref name="Open"/> &gt; 0: one of them must also be seen from that many of
/// the six directions (a Bandit drum in the open).
/// </summary>
public sealed record StyleMark(string Label, PartPick Pick, int Min = 1, string When = "seat", int Open = 0);

/// <summary>
/// A designed weak point: a cheap exposed part a player shoots to disable the vehicle rather than wreck what's worth salvaging. At least <paramref name="Min"/> of the
/// picked parts must be there, each seen from <paramref name="MinSides"/> of the six directions and hit by a ray from a standing player's eye (1.6 m over the ground) within
/// <paramref name="Range"/> metres from <paramref name="MinBearings"/> of 16 bearings round it. <paramref name="Optional"/>: none there doesn't count against it (if there, it must be hittable).
/// </summary>
public sealed record WeakPointRule(string Label, PartPick Pick, int MinSides = 2, float Range = 20, int MinBearings = 2, int Min = 1, string When = "seat", bool Optional = false);

/// <summary>
/// The outline a style's vehicles sit in, over the fixed parts (as <c>MaxSideFill</c> sees them): side fill and plan fill of the bounding box, height over length, how much the
/// roofline wanders (<see cref="VehiclePlan.Outline"/>'s third value), and rake (how far the ends drop below the highest point, 0 to 1).
/// </summary>
/// <remarks>
/// The defaults are every style's 5th to 95th percentile over 6 seeds of each shape (November 2026), widened a little: outlines come from the shapes, which every style shares,
/// so today the range keeps a style's vehicles off a box and off a sliver rather than telling one style from another. A style that wants its own outline narrows it.
/// </remarks>
public sealed record SilhouetteRange(
    float SideMin = 0.26f, float SideMax = 0.66f, float TopMin = 0.38f, float TopMax = 0.86f, float HeightMin = 0.2f, float HeightMax = 0.56f,
    float RoofMin = 0.1f, float RoofMax = 0.4f, float RakeMin = 0.15f, float RakeMax = 0.85f);

/// <summary>
/// A style's signature (docs/vehiclekit/VEHICLEKIT.md, "Identity and weak points"): what every vehicle of it must read as. Additive to <see cref="FactionStyle"/>: a style that
/// names none gets one derived from its own slots (<see cref="VehicleIdentity.SignatureOf"/>).
/// </summary>
public sealed class StyleSignature
{
    /// <summary>Features every vehicle carries.</summary>
    public StyleMark[] Marks { get; init; } = [];

    /// <summary>Characteristic parts by name and how much each says "this style" (each name counted at most three times).</summary>
    public Dictionary<string, double> Parts { get; init; } = [];

    /// <summary>How much characteristic weight reads as fully this style.</summary>
    public double PartTarget { get; init; } = 4;

    /// <summary>Characteristic grammar modules by family and name (kit 2's <c>Vehicle.Modules</c>), each family judged against its best; a module not named counts 0.3 of it.</summary>
    public Dictionary<string, Dictionary<string, double>> Modules { get; init; } = [];

    public SilhouetteRange Silhouette { get; init; } = new();

    public WeakPointRule[] WeakPoints { get; init; } = [];

    /// <summary>Under this, or with a mark missing, a kit-2 plan is planned again (<see cref="ShapeGrammar.Make"/>); kit 1 is only reported.</summary>
    public double Threshold { get; init; } = 0.75;
}

/// <summary>
/// How much a built vehicle reads as its style, 0 to 1 (docs/vehiclekit/VEHICLEKIT.md, "Identity and weak points"), from five parts weighted into one number:
/// marks (0.30, the style's required features), palette (0.20, painted parts in the style's colours and how many of its roles show), traits (0.20, characteristic parts
/// and modules), silhouette (0.15, the outline in the style's range) and weak points (0.15, the style's designed weak points there and hittable by rays through what's drawn).
/// A part with nothing to judge (no paint, no marks that apply) is left out and the rest weighed up. Uses no dice: the same vehicle scores the same everywhere.
/// </summary>
public static class VehicleIdentity
{
    public const double WMarks = 0.30, WPalette = 0.15, WTraits = 0.20, WSilhouette = 0.10, WWeak = 0.15, WSection = 0.10; // (2026-11-07: section 0.10 from palette and silhouette, 0.20 and 0.15 before)
    private const double OldPalette = 0.20, OldSilhouette = 0.15;

    /// <summary>
    /// A room bay's front profile within <see cref="SectionFloor"/> (mean difference per band) of its style's nearest language reads 1 (what the facets' thickness and
    /// the centimetre grid leave), and <see cref="SectionScale"/> further reads 0. Measured on IT War Bus seeds (2026-11-07): its own pods and vans 0.0096, a slab on it
    /// 0.012, kit 3's fixed bays 0.025, a cabin 0.041.
    /// </summary>
    public const float SectionFloor = 0.008f, SectionScale = 0.03f;
    private const float Eye = 1.6f;
    private const int Bearings = 16;

    /// <summary>The kit-2 re-plan on a low score (<see cref="Gate"/>); <c>--no-identity-gate</c> turns it off to measure without it.</summary>
    public static bool GateOn = true;

    /// <summary>Plans turned down for identity since start-up (any thread).</summary>
    public static int Replanned;

    // ---- The styles' signatures ------------------------------------------------------------------------------------------

    private static readonly string[] BanditDress = ["Iron Spike", "Long Iron Spike", "Skull Trophy", "Horned Skull", "Hubcap Plate"];
    private static readonly string[] ItMasts = ["Satellite Dish", "Wi-Fi Mast", "Router Antenna"];
    private static readonly string[] ItGear = ["CRT Monitor", "Network Switch", "Cable Reel", "Office Plant", "Desk Fan", "Employee of the Month", "Server Rack", "Computer", "Fax Machine"];
    private static readonly string[] ScamMasts = ["Phone Mast", "Loudhailer"];
    private static readonly string[] ScamGear = ["Office Phone", "Call Headset", "Gift Card Sign", "Prize Wheel", "Cubicle Desk", "Water Cooler"];
    private static readonly string[] HighlandDress = ["Saltire Flag", "Thistle", "Coo Heid", "Haggis on a Post", "Wee Caber", "Wee Keg", "Whisky Cask"];
    private static readonly string[] BritishFlags = ["Union Flag", "Belisha Beacon", "Furled Brolly", "Rain Gauge"];
    private static readonly string[] BritishTea = ["Teapot", "Traffic Cone", "Garden Gnome", "Queue Post", "Pillar Box", "Phone Box", "Tea Urn"];

    private static readonly WeakPointRule Tyres = new("tyres", PartPick.ByRole("tyres"), MinSides: 2, MinBearings: 4, Min: 2, When: "");
    private static readonly WeakPointRule Fuel = new("fuel", PartPick.ByRole("fuel"), MinSides: 2, MinBearings: 2, When: ""); // (a trailer's barrel too)

    private static Dictionary<string, double> W(double w, params string[] names) => names.ToDictionary(n => n, _ => w);

    private static Dictionary<string, double> Merge(params Dictionary<string, double>[] parts)
    {
        var all = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var p in parts)
            foreach (var (k, v) in p)
                all[k] = v;
        return all;
    }

    /// <summary>
    /// The shipped styles' signatures, by style name, from what their docs and slots say (Karl's styles stay as they are; a style may also carry its own
    /// <see cref="FactionStyle.Signature"/>, which wins). A style not here gets <see cref="Derived"/>.
    /// </summary>
    public static readonly Dictionary<string, StyleSignature> Signatures = new(StringComparer.Ordinal)
    {
        // Rust and bare metal, spikes and skulls, the drum stands in the open, nothing covers the tyres, the driver sits in the open (VEHICLEKIT.md, "The Bandit style").
        ["Bandit"] = new()
        {
            Marks =
            [
                new("fuel in the open", PartPick.ByRole("fuel"), Open: 2),
                new("spikes or trophies", PartPick.Of(BanditDress)),
            ],
            Parts = Merge(W(1, BanditDress), W(1, "Fuel Drum", "Ram Plow", "Mesh Guard Cage", "Slit Cab"), W(0.5, "Bull Bar", "Roll Cage", "Chop Top Cab", "Wood Planks", "Mesh Grate", "Spiked Wheel")),
            Modules = new()
            {
                ["cab"] = new() { ["caged"] = 1, ["armoured"] = 1, ["enclosed"] = 0.5, ["cab-over"] = 0.3 },
                ["nose"] = new() { ["ram"] = 1, ["plated hood"] = 1, ["long hood"] = 0.6, ["short hood"] = 0.6 },
                ["flank"] = new() { ["panniers"] = 1, ["loaded"] = 1, ["bare"] = 0.5 },
            },
            WeakPoints =
            [
                new("fuel drum", PartPick.ByRole("fuel"), When: ""),
                Tyres,
                new("open driver", PartPick.ByRole("driver"), MinSides: 2, MinBearings: 2, When: "light"),
                new("ammo crate", PartPick.ByRole("ammo"), MinSides: 1, MinBearings: 1, When: "light", Optional: true), // (a technical's loose crate blows; a room's magazine is meant to be safe)
            ],
        },
        // Masts and dishes on the roofs and the cab, monitors, switches and reels on the beds, racks, computers and fax machines in the rooms (VEHICLEKIT.md, "The IT style").
        ["IT"] = new()
        {
            Marks =
            [
                new("mast or dish", PartPick.Of(ItMasts)),
                new("office gear", PartPick.Of(ItGear)),
            ],
            Parts = Merge(W(1, ItMasts), W(1, ItGear), W(0.5, "Slat Grille", "Cab Over Shell", "Tin Roof Cab")),
            Modules = new()
            {
                ["cab"] = new() { ["enclosed"] = 1, ["cab-over"] = 1, ["caged"] = 0.3 },
                ["nose"] = new() { ["short hood"] = 1, ["flat"] = 1, ["long hood"] = 0.6, ["ram"] = 0.2 },
                ["flank"] = new() { ["bare"] = 1, ["panniers"] = 0.5, ["loaded"] = 0.3 },
            },
            WeakPoints = [new("antenna", PartPick.Of(ItMasts), MinSides: 2, MinBearings: 2), Tyres, Fuel],
        },
        // Phone masts and loudhailers, phones, headsets, GIFT CARDS signs, a prize wheel, cubicles round the water cooler (VEHICLEKIT.md, "The Call Centre style").
        ["Scam"] = new()
        {
            Marks =
            [
                new("phone mast or loudhailer", PartPick.Of(ScamMasts)),
                new("call centre gear", PartPick.Of(ScamGear)),
            ],
            Parts = Merge(W(1, ScamMasts), W(1, ScamGear), W(0.5, "Server Rack", "Computer", "Slat Grille")),
            Modules = new()
            {
                ["cab"] = new() { ["enclosed"] = 1, ["cab-over"] = 1, ["caged"] = 0.3 },
                ["nose"] = new() { ["short hood"] = 1, ["long hood"] = 0.8, ["flat"] = 1, ["ram"] = 0.2 },
                ["flank"] = new() { ["bare"] = 1, ["panniers"] = 0.6, ["loaded"] = 0.4 },
            },
            WeakPoints = [new("antenna", PartPick.Of(ScamMasts), MinSides: 2, MinBearings: 2), Tyres, Fuel],
        },
        // A Pipe Box on every vehicle with a seat, Saltires on the roofs, thistles, haggis and a coo's heid, wee cabers, kegs and casks (VEHICLEKIT.md, "The Highland style").
        ["Highland"] = new()
        {
            Marks =
            [
                new("Pipe Box", PartPick.Of("Pipe Box")),
                new("clan dressing", PartPick.Of(HighlandDress)),
            ],
            Parts = Merge(W(1, HighlandDress), W(1.5, "Pipe Box"), W(0.5, "Bull Bar", "Ram Plow", "Wood Planks")),
            Modules = new()
            {
                ["nose"] = new() { ["long hood"] = 1, ["ram"] = 1, ["short hood"] = 0.5 },
                ["cab"] = new() { ["caged"] = 1, ["enclosed"] = 0.8, ["armoured"] = 0.5 },
                ["flank"] = new() { ["panniers"] = 1, ["loaded"] = 0.8, ["bare"] = 0.6 },
            },
            WeakPoints = [new("pipe box", PartPick.Of("Pipe Box"), MinSides: 2, MinBearings: 2), Tyres, Fuel],
        },
        // Union Flags, Belisha beacons and brollies, teapots, cones and gnomes, queue posts, pillar boxes and phone boxes round a tea urn (VEHICLEKIT.md, "The British style").
        ["British"] = new()
        {
            Marks =
            [
                new("flags, beacons or brollies", PartPick.Of(BritishFlags)),
                new("tea, cones or a queue", PartPick.Of(BritishTea)),
            ],
            Parts = Merge(W(1, BritishFlags), W(1, BritishTea), W(0.5, "Slat Grille", "Tin Roof Cab", "Windscreen Frame")),
            Modules = new()
            {
                ["cab"] = new() { ["enclosed"] = 1, ["cab-over"] = 0.8, ["caged"] = 0.2 },
                ["nose"] = new() { ["long hood"] = 1, ["short hood"] = 0.8, ["flat"] = 0.8, ["ram"] = 0.2 },
                ["flank"] = new() { ["bare"] = 1, ["panniers"] = 0.6, ["loaded"] = 0.3 },
            },
            WeakPoints = [new("flag or beacon", PartPick.Of(BritishFlags), MinSides: 2, MinBearings: 2, Optional: true), Tyres, Fuel],
        },
    };

    /// <summary>A style's signature: its own, else <see cref="Signatures"/>' entry, else <see cref="Derived"/>.</summary>
    public static StyleSignature SignatureOf(FactionStyle st) => st.Signature ?? (Signatures.TryGetValue(st.Name, out var s) ? s : Derived(st));

    private static readonly Dictionary<string, StyleSignature> _derived = new(StringComparer.Ordinal);

    /// <summary>
    /// A signature for a style that names none (Militia, a new faction): its dressing slots' parts ("spike", "trophy", "mast", "gear", "piper") are its characteristic parts,
    /// and a vehicle with a seat carries one of them; weak points are the tyres and the fuel. A style with no dressing slots has no marks.
    /// </summary>
    public static StyleSignature Derived(FactionStyle st)
    {
        lock (_derived)
        {
            if (_derived.TryGetValue(st.Name, out var have))
                return have;
            var dress = new[] { "spike", "trophy", "mast", "gear", "piper" }.Where(st.Parts.ContainsKey).SelectMany(k => st.Parts[k]).Distinct().ToArray();
            var sig = new StyleSignature
            {
                Marks = dress.Length > 0 ? [new StyleMark("its dressing", PartPick.Of(dress))] : [],
                Parts = dress.ToDictionary(n => n, _ => 1.0),
                WeakPoints = [Tyres, Fuel],
            };
            _derived[st.Name] = sig;
            return sig;
        }
    }

    // ---- The score ---------------------------------------------------------------------------------------------------

    /// <summary>One weak point rule's result.</summary>
    public sealed record WeakResult(string Label, int Found, int Hittable, bool Applies, bool Passed, string Why);

    /// <summary>A vehicle's identity against one style, with its parts (NaN: nothing to judge) and a short breakdown.</summary>
    public sealed record Score(string Style, double Total, double Marks, double Palette, double Traits, double Silhouette, double Weak, IReadOnlyList<WeakResult> WeakPoints, string Breakdown)
    {
        /// <summary>The marks that apply and aren't there (or are covered).</summary>
        public IReadOnlyList<string> Missing { get; init; } = [];

        /// <summary>The outline as measured: side fill, plan fill, height over length, roofline wander, rake.</summary>
        public float[] Outline { get; init; } = [];

        /// <summary>Every weak point that applies is there and hittable.</summary>
        public bool WeakPassed => WeakPoints.All(w => !w.Applies || w.Passed);

        /// <summary>How much the rooms' cross-sections read as the style's body languages, 0 to 1 (NaN: no room, or <c>--no-sections</c>).</summary>
        public double Section { get; init; } = double.NaN;

        public override string ToString() => $"{Total:0.00} as {Style}: {Breakdown}";
    }

    /// <summary>The geometry and part sets of one built vehicle, shared by every style it is scored against.</summary>
    public sealed class Body
    {
        public readonly Vehicle V;
        public readonly Construct C;
        internal Vector3[] Tris = [];
        internal int[] Start = [], End = [];
        internal Vector3[] Min = [], Max = [];
        internal float Ground;
        internal Vector3 BoundsMin, BoundsMax;
        internal float Side, Top, Roof, Rake, Height;
        internal readonly Dictionary<(int Part, float Range), (int Sides, int Bearings)> Seen = new();
        internal readonly bool HasSeat;

        public Body(Vehicle v)
        {
            V = v;
            C = v.BuiltBody();
            for (var i = 0; i < C.PartCount; i++)
                HasSeat |= C.DefOf(i).Role == PartRole.Seat;
        }

        internal bool GeometryReady;
    }

    /// <summary>Scores a vehicle against its own style.</summary>
    public static Score Of(Vehicle v) => Of(new Body(v), v.Style);

    /// <summary>Scores a vehicle against every style in <see cref="Styles.All"/>, its own first.</summary>
    public static List<Score> Against(Vehicle v)
    {
        var b = new Body(v);
        return new[] { v.Style }.Concat(Styles.All.Where(s => s != v.Style)).Select(s => Of(b, s)).ToList();
    }

    public static Score Of(Body b, FactionStyle st)
    {
        var sig = SignatureOf(st);
        var c = b.C;
        var notes = new List<string>();

        // Marks.
        double marks = double.NaN;
        var missing = new List<string>();
        var applying = sig.Marks.Where(m => When(b, m.When)).ToList();
        if (applying.Count > 0)
        {
            var ok = 0;
            foreach (var m in applying)
            {
                var found = Select(b, m.Pick);
                var pass = found.Count >= m.Min && (m.Open <= 0 || found.Any(i => Exposure(b, i, 0).Sides >= m.Open));
                if (pass)
                    ok++;
                else
                    missing.Add(found.Count >= m.Min ? m.Label + " (covered)" : m.Label);
            }
            marks = ok / (double)applying.Count;
            notes.Add($"marks {ok}/{applying.Count}{(missing.Count > 0 ? " (no " + string.Join(", no ", missing) + ")" : "")}");
        }

        // Palette.
        double palette = double.NaN;
        var all = new HashSet<string>(st.Palette.Values.SelectMany(x => x), StringComparer.OrdinalIgnoreCase);
        var seenRoles = new HashSet<string>(StringComparer.Ordinal);
        int painted = 0, ours = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            var paint = c.Parts[i].Paint;
            if (!ConstructPaint.IsSet(paint))
                continue;
            painted++;
            var hex = ConstructPaint.ToHex(paint);
            if (all.Contains(hex))
            {
                ours++;
                foreach (var (role, list) in st.Palette)
                    if (list.Contains(hex, StringComparer.OrdinalIgnoreCase))
                        seenRoles.Add(role);
            }
        }
        if (painted > 0)
        {
            var share = ours / (double)painted;
            var roles = st.Palette.Count == 0 ? 1 : seenRoles.Count / (double)st.Palette.Count;
            palette = 0.6 * share + 0.4 * roles;
            notes.Add($"palette {share:P0} ({seenRoles.Count}/{st.Palette.Count} roles)");
        }

        // Traits: characteristic parts, and modules for a grammar vehicle.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var sum = 0.0;
        for (var i = 0; i < c.PartCount; i++)
        {
            var n = c.DefOf(i).Name;
            if (!sig.Parts.TryGetValue(n, out var w))
                continue;
            var k = counts.GetValueOrDefault(n);
            if (k >= 3)
                continue;
            counts[n] = k + 1;
            sum += w;
        }
        var partsScore = sig.Parts.Count == 0 || !b.HasSeat ? double.NaN : Math.Min(1, sum / Math.Max(0.1, sig.PartTarget));
        double modulesScore = double.NaN;
        if (sig.Modules.Count > 0 && b.V.Modules.Count > 0 && b.HasSeat)
        {
            var fam = new List<double>();
            foreach (var (family, weights) in sig.Modules)
            {
                var entry = b.V.Modules.FirstOrDefault(m => m.StartsWith(family + " ", StringComparison.Ordinal));
                if (entry == null || weights.Count == 0)
                    continue;
                var best = weights.Values.Max();
                var chosen = entry[(family.Length + 1)..].Split('+');
                fam.Add(chosen.Average(x => weights.TryGetValue(x, out var w) ? w : 0.3 * best) / Math.Max(1e-6, best));
            }
            if (fam.Count > 0)
                modulesScore = fam.Average();
        }
        var traits = Mean(partsScore, modulesScore);
        if (!double.IsNaN(traits))
            notes.Add($"traits {traits:0.00} (parts {Fmt(partsScore)}, modules {Fmt(modulesScore)})");

        // Silhouette.
        Geometry(b);
        var r = sig.Silhouette;
        var outside = new List<string>();
        double In(string name, float x, float lo, float hi)
        {
            if (x >= lo && x <= hi)
                return 1;
            outside.Add($"{name} {x:0.00} {(x < lo ? $"under {lo:0.00}" : $"over {hi:0.00}")}");
            var d = x < lo ? lo - x : x - hi;
            return Math.Max(0, 1 - d / (0.25 * (hi - lo) + 0.05));
        }
        var silhouette = (In("side", b.Side, r.SideMin, r.SideMax) + In("plan", b.Top, r.TopMin, r.TopMax) + In("height/length", b.Height, r.HeightMin, r.HeightMax)
            + In("roofline", b.Roof, r.RoofMin, r.RoofMax) + In("rake", b.Rake, r.RakeMin, r.RakeMax)) / 5;
        notes.Add($"silhouette {5 - outside.Count}/5{(outside.Count > 0 ? " (" + string.Join(", ", outside) + ")" : "")}");

        // Weak points.
        var weak = new List<WeakResult>();
        foreach (var w in sig.WeakPoints)
        {
            if (!When(b, w.When))
            {
                weak.Add(new WeakResult(w.Label, 0, 0, false, true, "n/a"));
                continue;
            }
            var found = Select(b, w.Pick);
            if (found.Count == 0 && w.Optional)
            {
                weak.Add(new WeakResult(w.Label, 0, 0, false, true, "none"));
                continue;
            }
            var hit = 0;
            foreach (var i in found)
            {
                var (sides, bearings) = Exposure(b, i, w.Range);
                if (sides >= w.MinSides && bearings >= w.MinBearings)
                    hit++;
                VehicleGenJob.Yield();
            }
            var need = Math.Min(w.Min, Math.Max(1, found.Count));
            var pass = found.Count >= (w.Optional ? 1 : w.Min) && hit >= need;
            var why = found.Count == 0 ? "missing" : found.Count < w.Min && !w.Optional ? $"only {found.Count}" : hit < need ? $"covered ({hit}/{found.Count} hittable)" : $"{hit}/{found.Count} hittable";
            weak.Add(new WeakResult(w.Label, found.Count, hit, true, pass, why));
        }
        var applies = weak.Where(x => x.Applies).ToList();
        var weakScore = applies.Count == 0 ? double.NaN : applies.Average(x => x.Passed ? 1 : x.Found > 0 ? 0.25 : 0);
        if (applies.Count > 0)
            notes.Add($"weak points {applies.Count(x => x.Passed)}/{applies.Count} ({string.Join(", ", applies.Select(x => $"{x.Label} {x.Why}"))})");

        // Section: each room bay from the front against the style's languages (the nearest one), so a pod on a Bandit truck reads less Bandit.
        var section = VehicleDiversity.SectionsOn ? SectionScore(b, st) : double.NaN;
        if (!double.IsNaN(section))
            notes.Add($"section {section:0.00}");

        var on = VehicleDiversity.SectionsOn;
        var parts = new[] { (marks, WMarks), (palette, on ? WPalette : OldPalette), (traits, WTraits), ((double)silhouette, on ? WSilhouette : OldSilhouette), (weakScore, WWeak), (section, WSection) };
        var weight = parts.Where(p => !double.IsNaN(p.Item1)).Sum(p => p.Item2);
        var total = weight <= 0 ? 0 : parts.Where(p => !double.IsNaN(p.Item1)).Sum(p => p.Item1 * p.Item2) / weight;
        return new Score(st.Name, total, marks, palette, traits, silhouette, weakScore, weak, string.Join("; ", notes)) { Outline = [b.Side, b.Top, b.Height, b.Roof, b.Rake], Missing = missing, Section = section };
    }

    /// <summary>The mean over the vehicle's room bays of how near each bay's front profile is to the nearest of the style's languages (as the style draws them); NaN without a room.</summary>
    private static double SectionScore(Body b, FactionStyle st)
    {
        if (b.V.Rooms.Count == 0)
            return double.NaN;
        Geometry(b);
        var rule = BodyStyleRule.Of(st);
        var facets = FrontProfile.FacetsOf(rule);
        var langs = rule.Languages.Select(x => FrontProfile.Of(x.L, facets)).ToArray();
        var sum = 0.0;
        var n = 0;
        foreach (var bay in b.V.Rooms.SelectMany(r => r.Bays))
        {
            var i = bay.Index;
            if (i < 0 || i >= b.Start.Length || b.Start[i] < 0)
                continue;
            var prof = FrontProfile.Of(b.Tris, [(b.Start[i], b.End[i])]);
            var near = langs.Min(l => FrontProfile.Dist(prof, l));
            sum += Math.Clamp(1 - (near - SectionFloor) / SectionScale, 0, 1);
            n++;
        }
        return n == 0 ? double.NaN : sum / n;
    }

    private static string Fmt(double x) => double.IsNaN(x) ? "-" : x.ToString("0.00");

    private static double Mean(double a, double b) => double.IsNaN(a) ? b : double.IsNaN(b) ? a : (a + b) / 2;

    private static bool When(Body b, string when)
    {
        foreach (var w in when.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ok = w switch
            {
                "seat" => b.HasSeat,
                "room" => b.V.Rooms.Count > 0,
                "heavy" => b.V.IsHeavy,
                "light" => !b.V.IsHeavy && b.HasSeat,
                _ => true,
            };
            if (!ok)
                return false;
        }
        return true;
    }

    /// <summary>The parts a pick means, by index in the body.</summary>
    private static List<int> Select(Body b, PartPick p)
    {
        var c = b.C;
        var list = new List<int>();
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            var hit = p.Names.Length > 0 && Array.IndexOf(p.Names, d.Name) >= 0 || p.Role switch
            {
                "fuel" => d.Role == PartRole.FuelTank,
                "tyres" => d.Role == PartRole.Wheel,
                "engine" => d.Role == PartRole.Engine,
                "ammo" => d.Role == PartRole.AmmoBox,
                "seat" => d.Role == PartRole.Seat,
                "driver" => b.V.DriverSeat is { } ds ? ds.Index == i : d.Role == PartRole.Seat,
                _ => false,
            };
            if (hit)
                list.Add(i);
        }
        return list;
    }

    // ---- Rays through what's drawn ------------------------------------------------------------------------------------

    /// <summary>The body's triangles by part with each part's box, its outline numbers and where the ground is.</summary>
    private static void Geometry(Body b)
    {
        if (b.GeometryReady)
            return;
        b.GeometryReady = true;
        var c = b.C;
        VehicleGenJob.Yield();
        var (tris, owner) = VehiclePlan.PartTriangles(c);
        b.Tris = tris;
        var n = c.PartCount;
        (b.Start, b.End, b.Min, b.Max) = (new int[n], new int[n], new Vector3[n], new Vector3[n]);
        Array.Fill(b.Start, -1);
        for (var t = 0; t < owner.Length; t++)
        {
            var i = owner[t];
            if (i < 0 || i >= n)
                continue;
            if (b.Start[i] < 0)
            {
                b.Start[i] = t;
                b.Min[i] = new Vector3(float.MaxValue);
                b.Max[i] = new Vector3(float.MinValue);
            }
            b.End[i] = t + 1;
            for (var k = 0; k < 3; k++)
            {
                b.Min[i] = Vector3.Min(b.Min[i], tris[t * 3 + k]);
                b.Max[i] = Vector3.Max(b.Max[i], tris[t * 3 + k]);
            }
        }
        b.Ground = float.MaxValue;
        for (var k = 0; k < c.WheelCount; k++)
            b.Ground = MathF.Min(b.Ground, c.Wheels[k].Centre.Y - c.Wheels[k].Travel * 0.5f - c.Wheels[k].Radius);
        (b.BoundsMin, b.BoundsMax) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        foreach (var p in tris)
        {
            b.BoundsMin = Vector3.Min(b.BoundsMin, p);
            b.BoundsMax = Vector3.Max(b.BoundsMax, p);
        }
        if (b.Ground == float.MaxValue)
            b.Ground = b.BoundsMin.Y;
        VehicleGenJob.Yield();
        (b.Side, b.Top, b.Roof) = VehiclePlan.Outline(c);
        VehicleGenJob.Yield();
        var (_, _, height) = VehiclePlan.Profiles(c, VehicleDiversity.Bins);
        var highest = height.Max();
        b.Rake = highest <= 0 ? 0 : 1 - (height[0] + height[^1]) / (2 * highest);
        var fixedTris = VehiclePlan.Triangles(c, false);
        var (fmin, fmax) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        foreach (var p in fixedTris)
        {
            fmin = Vector3.Min(fmin, p);
            fmax = Vector3.Max(fmax, p);
        }
        b.Height = fmax.Z > fmin.Z ? (fmax.Y - fmin.Y) / (fmax.Z - fmin.Z) : 0;
        VehicleGenJob.Yield();
    }

    /// <summary>The part the ray meets first within <paramref name="reach"/>, or -1.</summary>
    private static int FirstHit(Body b, Vector3 from, Vector3 way, float reach)
    {
        var best = reach;
        var who = -1;
        var inv = new Vector3(1 / (MathF.Abs(way.X) < 1e-9f ? 1e-9f : way.X), 1 / (MathF.Abs(way.Y) < 1e-9f ? 1e-9f : way.Y), 1 / (MathF.Abs(way.Z) < 1e-9f ? 1e-9f : way.Z));
        var tris = b.Tris;
        for (var i = 0; i < b.Start.Length; i++)
        {
            if (b.Start[i] < 0)
                continue;
            var t0 = (b.Min[i] - from) * inv;
            var t1 = (b.Max[i] - from) * inv;
            var lo = Vector3.Min(t0, t1);
            var hi = Vector3.Max(t0, t1);
            var enter = MathF.Max(MathF.Max(lo.X, lo.Y), lo.Z);
            var exit = MathF.Min(MathF.Min(hi.X, hi.Y), hi.Z);
            if (exit < 0 || enter > exit + 1e-4f || enter > best)
                continue;
            for (var t = b.Start[i]; t < b.End[i]; t++)
            {
                Vector3 a = tris[t * 3], e1 = tris[t * 3 + 1] - a, e2 = tris[t * 3 + 2] - a;
                var p = Vector3.Cross(way, e2);
                var det = Vector3.Dot(e1, p);
                if (MathF.Abs(det) < 1e-9f)
                    continue;
                var s = from - a;
                var u = Vector3.Dot(s, p) / det;
                if (u < 0 || u > 1)
                    continue;
                var q = Vector3.Cross(s, e1);
                var v = Vector3.Dot(way, q) / det;
                if (v < 0 || u + v > 1)
                    continue;
                var d = Vector3.Dot(e2, q) / det;
                if (d > 1e-4f && d < best)
                    (best, who) = (d, i);
            }
        }
        return who;
    }

    private static readonly Vector3[] Ways = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];

    /// <summary>
    /// How many of the six directions a part is seen from (a ray from far off along that direction meets it first, at its middle or one of four points round it), and from
    /// how many of 16 bearings a standing player's eye <paramref name="range"/> metres off hits it first (0: not asked). Kept per part and range.
    /// </summary>
    private static (int Sides, int Bearings) Exposure(Body b, int part, float range)
    {
        Geometry(b);
        if (b.Seen.TryGetValue((part, range), out var known))
            return known;
        if (b.Start[part] < 0)
            return b.Seen[(part, range)] = (0, 0);
        var (min, max) = (b.Min[part], b.Max[part]);
        var mid = (min + max) * 0.5f;
        var half = (max - min) * 0.5f;
        var span = (b.BoundsMax - b.BoundsMin).Length() + 2;
        var sides = 0;
        foreach (var w in Ways)
        {
            var u = MathF.Abs(w.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
            var v = Vector3.Cross(w, u);
            var hu = MathF.Abs(Vector3.Dot(half, u)) * 0.4f;
            var hv = MathF.Abs(Vector3.Dot(half, v)) * 0.4f;
            foreach (var off in (Vector3[])[Vector3.Zero, u * hu, -u * hu, v * hv, -v * hv])
            {
                var at = mid + off;
                if (FirstHit(b, at + w * span, -w, span * 2) == part)
                {
                    sides++;
                    break;
                }
            }
            VehicleGenJob.Yield(); // (a spawn's slice may end between directions)
        }
        var bearings = 0;
        if (range > 0)
        {
            var centre = (b.BoundsMin + b.BoundsMax) * 0.5f;
            var reachOut = MathF.Max(range, new Vector2(b.BoundsMax.X - b.BoundsMin.X, b.BoundsMax.Z - b.BoundsMin.Z).Length() * 0.5f + 2);
            for (var k = 0; k < Bearings; k++)
            {
                var a = k * MathF.Tau / Bearings;
                var eye = new Vector3(centre.X + MathF.Sin(a) * reachOut, b.Ground + Eye, centre.Z + MathF.Cos(a) * reachOut);
                foreach (var off in (Vector3[])[Vector3.Zero, new(half.X * 0.4f, 0, 0), new(-half.X * 0.4f, 0, 0), new(0, half.Y * 0.4f, 0), new(0, 0, half.Z * 0.4f), new(0, 0, -half.Z * 0.4f)])
                {
                    var to = mid + off - eye;
                    var len = to.Length();
                    if (FirstHit(b, eye, to / len, len + 1) == part)
                    {
                        bearings++;
                        break;
                    }
                }
                VehicleGenJob.Yield();
            }
        }
        return b.Seen[(part, range)] = (sides, bearings);
    }

    // ---- The grammar's hook ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The shape grammar's identity check (<see cref="ShapeGrammar.Make"/>): a built plan scoring under its style's <see cref="StyleSignature.Threshold"/> is turned down
    /// (logged, kept in <paramref name="kept"/> when it is the best turned down so far) and the seed plans again; the last attempt returns the best of what was built.
    /// Returns the vehicle to use, or null to plan again.
    /// </summary>
    public static Vehicle? Gate(Vehicle v, int attempt, int attempts, ref Vehicle? kept)
    {
        if (!GateOn)
            return v;
        var s = Of(v);
        v.Identity = s;
        var sig = SignatureOf(v.Style);
        if (s.Total >= sig.Threshold && s.Missing.Count == 0)
            return v;
        var second = kept != null; // (identity re-plans once: a seed whose shape can't carry a mark would otherwise pay for every plan, 4 times the time at spawn)
        if (kept?.Identity is not { } k || (s.Missing.Count, -s.Total).CompareTo((k.Missing.Count, -k.Total)) < 0)
            kept = v; // (fewest marks missing, then the highest score)
        if (attempt + 1 < attempts && !second)
        {
            Interlocked.Increment(ref Replanned);
            Console.WriteLine($"[vehicles] {v.Name}: plan {attempt + 1} reads {s.Total:0.00} as {s.Style} ({(s.Missing.Count > 0 ? "no " + string.Join(", no ", s.Missing) : $"under {sig.Threshold:0.00}")}): {s.Breakdown}; planning again");
            return null;
        }
        Console.WriteLine($"[vehicles] {v.Name}: plan {attempt + 1} reads {s.Total:0.00} as {s.Style} too; keeping the best plan checked, {kept.Identity!.Total:0.00}{(kept.Identity.Missing.Count > 0 ? ", no " + string.Join(", no ", kept.Identity.Missing) : "")}");
        return kept;
    }

    // ---- The marks pass's questions (Vehicle.GuaranteeMarks) ----------------------------------------------------------------

    /// <summary>
    /// The marks of the vehicle's signature that apply to it and that it has too few parts of, by name: what <see cref="Vehicle.GuaranteeMarks"/> places. A mark picked by
    /// role (a Bandit's fuel in the open) is the shape's to carry, not a part to add, and isn't listed. Cheap: names only, no geometry.
    /// </summary>
    internal static List<StyleMark> Lacking(Vehicle v)
    {
        var list = new List<StyleMark>();
        var seat = v.Parts.Any(p => p.Def.Role == PartRole.Seat);
        foreach (var m in SignatureOf(v.Style).Marks)
            if (m.Pick.Role.Length == 0 && m.Pick.Names.Length > 0 && Applies(v, seat, m.When) && Count(v, m.Pick) < m.Min)
                list.Add(m);
        return list;
    }

    /// <summary>How many of the vehicle's parts a pick names.</summary>
    internal static int Count(Vehicle v, PartPick p) => v.Parts.Count(x => Array.IndexOf(p.Names, x.Name) >= 0);

    private static bool Applies(Vehicle v, bool seat, string when) =>
        when.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(w => w switch
        {
            "seat" => seat,
            "room" => v.Rooms.Count > 0,
            "heavy" => v.IsHeavy,
            "light" => !v.IsHeavy && seat,
            _ => true,
        });

    // ---- Reports -----------------------------------------------------------------------------------------------------

    /// <summary>One vehicle's scores against every style: its own, the best other, and whether it reads more as that one.</summary>
    public sealed record Reading(int Seed, Score Own, Score BestOther)
    {
        public bool Confused => BestOther.Total > Own.Total;
    }

    public static Reading Read(Vehicle v)
    {
        var all = Against(v);
        return new Reading(v.Seed, all[0], all.Skip(1).OrderByDescending(s => s.Total).First());
    }

    /// <summary>
    /// <c>--identity "Style Shape" [--seeds N] [--from S] [--seed-list A,B] [--shipped]</c> (or a style's name for all its shapes): each seed's identity and breakdown, the best other style,
    /// then the mean, the weak-point pass rate and every vehicle that reads more as another style. Default 6 seeds. Exit code: 0, 1 for no such generator or style.
    /// </summary>
    public static int Cli(string name, string[] args)
    {
        List<IVehicleGenerator> gens;
        if (VehicleRegistry.Find(name) is { } one)
            gens = [one];
        else if (Styles.All.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || s.Group.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } st)
            gens = VehicleQualityCli.ShapesOf(st, args.Any(a => a.Equals("--extras", StringComparison.OrdinalIgnoreCase))).ToList();
        else
        {
            Console.WriteLine($"no vehicle generator or style named \"{name}\"");
            return 1;
        }
        var shipped = args.Any(a => a.Equals("--shipped", StringComparison.OrdinalIgnoreCase));
        var raw = args.Any(a => a.Equals("--raw", StringComparison.OrdinalIgnoreCase));
        var readings = new List<(string Name, Reading R)>();
        foreach (var g in gens)
        {
            var seeds = shipped ? g.Seeds.ToList() : VehicleQualityCli.SeedsFrom(args, 6);
            Console.WriteLine($"{g.Name}:");
            foreach (var seed in seeds)
                try
                {
                    var before = Replanned;
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    var v = VehicleRegistry.Make(g, seed);
                    var made = clock.Elapsed.TotalMilliseconds;
                    clock.Restart();
                    var own = Of(v);
                    var scored = clock.Elapsed.TotalMilliseconds;
                    var r = Read(v);
                    readings.Add((g.Name, r));
                    Console.WriteLine($"  seed {seed} (kit {v.Kit}{(v.Modules.FirstOrDefault(m => m.StartsWith("plan ", StringComparison.Ordinal)) is { } p ? ", " + p : "")}{(Replanned > before ? $", {Replanned - before} identity re-plan{(Replanned - before == 1 ? "" : "s")}" : "")}): {r.Own}");
                    Console.WriteLine($"    best other: {r.BestOther.Style} {r.BestOther.Total:0.00}{(r.Confused ? "  READS AS " + r.BestOther.Style.ToUpperInvariant() : "")}");
                    if (raw)
                        Console.WriteLine($"    made in {made:0} ms (scoring included where kit 2 checks it), scored against its own style in {scored:0} ms ({own.Total:0.00})");
                    if (raw && v.MarksPlaced.Count > 0)
                        Console.WriteLine($"    marks placed: {string.Join("; ", v.MarksPlaced)}");
                    if (raw)
                        Console.WriteLine($"    outline: side {r.Own.Outline[0]:0.000} plan {r.Own.Outline[1]:0.000} height/length {r.Own.Outline[2]:0.000} roofline {r.Own.Outline[3]:0.000} rake {r.Own.Outline[4]:0.000}");
                }
                catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
                {
                    Console.WriteLine($"  seed {seed}: doesn't generate: {ex.Message}");
                }
        }
        Summarise(readings);
        return 0;
    }

    /// <summary>The mean identity, the weak-point pass rate, per weak point, and the confusions.</summary>
    public static void Summarise(IReadOnlyList<(string Name, Reading R)> readings)
    {
        if (readings.Count == 0)
            return;
        Console.WriteLine($"identity: mean {readings.Average(x => x.R.Own.Total):0.00}, least {readings.Min(x => x.R.Own.Total):0.00}; weak points pass on {readings.Count(x => x.R.Own.WeakPassed)}/{readings.Count}");
        foreach (var g in readings.SelectMany(x => x.R.Own.WeakPoints).Where(w => w.Applies).GroupBy(w => w.Label))
            Console.WriteLine($"  {g.Key}: {g.Count(w => w.Passed)}/{g.Count()} pass, {g.Count(w => w.Found == 0)} missing, {g.Count(w => w.Found > 0 && !w.Passed)} covered");
        var mk = readings.Where(x => !double.IsNaN(x.R.Own.Marks)).ToList();
        if (mk.Count > 0)
            Console.WriteLine($"  marks {mk.Average(x => x.R.Own.Marks):0.00}, palette {readings.Select(x => x.R.Own.Palette).Where(x => !double.IsNaN(x)).DefaultIfEmpty().Average():0.00}, traits {readings.Select(x => x.R.Own.Traits).Where(x => !double.IsNaN(x)).DefaultIfEmpty().Average():0.00}, silhouette {readings.Average(x => x.R.Own.Silhouette):0.00}, section {readings.Select(x => x.R.Own.Section).Where(x => !double.IsNaN(x)).DefaultIfEmpty(double.NaN).Average():0.00}");
        var confused = readings.Where(x => x.R.Confused).ToList();
        Console.WriteLine(confused.Count == 0 ? "  none reads more as another style" : $"  {confused.Count} read more as another style:");
        foreach (var (n, r) in confused)
            Console.WriteLine($"    {n} {r.Seed}: {r.Own.Total:0.00} as its own, {r.BestOther.Total:0.00} as {r.BestOther.Style}");
    }
}

public sealed partial class Vehicle
{
    /// <summary>Its identity score from the grammar's check (<see cref="VehicleIdentity.Gate"/>), when that ran.</summary>
    public VehicleIdentity.Score? Identity { get; set; }

    /// <summary>The live construct as it stands, rebuilt, for measuring (<see cref="VehicleIdentity"/>).</summary>
    internal Construct BuiltBody()
    {
        C.Rebuild();
        return C;
    }
}
