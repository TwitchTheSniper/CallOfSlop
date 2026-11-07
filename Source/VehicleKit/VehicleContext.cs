using System.Globalization;
using System.Text;

namespace CallOfSlop.VehicleKit;

/// <summary>The ground a vehicle is spawned on, as the spawn context reads it (docs/vehiclekit/VEHICLEKIT.md, "Spawn context").</summary>
public enum Biome : byte
{
    Neutral, // nothing in particular, or nothing to read (a flat yard): today's vehicles
    Desert,
    Snow,
    Swamp,
    Forest,
    Road,
}

/// <summary>
/// Where a vehicle is spawned, as far as its build cares (roadmap phase 7; docs/vehiclekit/VEHICLEKIT.md, "Spawn context"): the ground's biome, gravity as a
/// multiple of the world's standard, the air's density, how rich the place is in wood and ore, and the weather's rain. The server builds one from the spawn
/// point (<c>SpawnContexts.At</c>, Source/Game) and passes it into <see cref="VehicleGenJob"/>; the kit leans the build by it while the faction's style still
/// decides how it looks (<see cref="VehicleContext"/>). <see cref="Neutral"/> (the default everywhere) makes exactly today's vehicles: nothing reads a context that
/// doesn't <see cref="Matters"/>.
/// </summary>
public sealed record SpawnContext
{
    public Biome Biome { get; init; }

    /// <summary>Gravity at the spawn, times the world's standard (1: an ordinary map at the host's 100 %).</summary>
    public float Gravity { get; init; } = 1;

    /// <summary>The air's density at the spawn: 1 at sea level, 0 off world (ConstructAir.Density).</summary>
    public float Air { get; init; } = 1;

    /// <summary>How much of the place is forest and how much ore lies under it, 0 to 1.</summary>
    public float Wood { get; init; }

    public float Ore { get; init; }

    /// <summary>The weather's rain at the spawn, 0 to 1 (0 with no weather).</summary>
    public float Rain { get; init; }

    /// <summary>How the server read it, for the log ("sand under 4 of 5 samples, 2.1 m over the sand line"); "" for a context given by hand.</summary>
    public string Read { get; init; } = "";

    public static SpawnContext Neutral { get; } = new();

    public const float LowGravityBelow = 0.75f, HighGravityAbove = 1.3f, NoAirBelow = 0.2f, RichFrom = 0.5f;

    public bool LowGravity => Gravity < LowGravityBelow;
    public bool HighGravity => Gravity > HighGravityAbove;
    public bool NoAir => Air < NoAirBelow;
    public bool WoodRich => Wood >= RichFrom;
    public bool OreRich => Ore >= RichFrom;

    /// <summary>Does anything in it change a build? A context that doesn't is neutral, whatever its numbers.</summary>
    public bool Matters => Biome != Biome.Neutral || LowGravity || HighGravity || NoAir || WoodRich || OreRich;

    /// <summary>What changes a build, as players read it: "desert, low gravity, ore"; "" when nothing does.</summary>
    public string Label
    {
        get
        {
            var bits = new List<string>(5);
            if (Biome != Biome.Neutral)
                bits.Add(Name(Biome));
            if (LowGravity)
                bits.Add("low gravity");
            else if (HighGravity)
                bits.Add("high gravity");
            if (NoAir)
                bits.Add("no air");
            if (WoodRich)
                bits.Add("wood");
            if (OreRich)
                bits.Add("ore");
            return string.Join(", ", bits);
        }
    }

    /// <summary>What a vehicle made in it is named with (so it never reads as a committed seed): " [desert, ore]"; "" for a context that doesn't matter.</summary>
    public string Suffix => Matters ? $" [{Label}]" : "";

    /// <summary>Every number, for the kit line and the log: "desert, gravity 1.00, air 1.00, wood 0.00, ore 0.62, rain 0.00".</summary>
    public string Detail => string.Create(CultureInfo.InvariantCulture, $"{Name(Biome)}, gravity {Gravity:0.00}, air {Air:0.00}, wood {Wood:0.00}, ore {Ore:0.00}, rain {Rain:0.00}");

    public static string Name(Biome b) => b.ToString().ToLowerInvariant();

    public static bool TryBiome(string text, out Biome b) => Enum.TryParse(text.Trim(), true, out b) && Enum.IsDefined(b);

    // ---- While a vehicle is made --------------------------------------------------------------------------------------

    [ThreadStatic] private static SpawnContext? _current;
    [ThreadStatic] private static bool _kitOnly;

    /// <summary>The context the vehicle being made on this thread is made in (null: none, as every vehicle before phase 7). Per thread, as <see cref="FactionStyle.Weigh"/>.</summary>
    public static SpawnContext? Current => _current;

    /// <summary>
    /// A family's model (VehicleFamilies.cs) made in a context keeps its family's body and chassis: only the context's kit changes (tyres of the same size, paint,
    /// plate materials, an electric drive off world). Null context or false for everything else.
    /// </summary>
    public static bool KitOnly => _kitOnly;

    /// <summary>Makes <paramref name="c"/> the current context for one generation; the one before comes back when it ends. A context that doesn't matter is none.</summary>
    public static Scope Use(SpawnContext? c, bool kitOnly) => new(c is { Matters: true } ? c : null, kitOnly);

    public readonly struct Scope : IDisposable
    {
        private readonly SpawnContext? _back;
        private readonly bool _backKit;

        internal Scope(SpawnContext? c, bool kitOnly)
        {
            (_back, _backKit) = (_current, _kitOnly);
            (_current, _kitOnly) = (c, c != null && kitOnly);
        }

        public void Dispose() => (_current, _kitOnly) = (_back, _backKit);
    }

    /// <summary>The command line's context: <c>--biome desert|snow|swamp|forest|road --gravity G --air 0|1 --wood W --ore O --rain R</c> (anything not given: neutral's).</summary>
    public static SpawnContext? FromArgs(string[] args, out string why)
    {
        why = "";
        string? Flag(string f) => args.SkipWhile(a => !a.Equals(f, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        float? Num(string f) => Flag(f) is { } t && float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        var c = new SpawnContext();
        var any = false;
        if (Flag("--biome") is { } bt)
        {
            if (!TryBiome(bt, out var b))
            {
                why = $"--biome {bt}: {string.Join(", ", Enum.GetValues<Biome>().Select(Name))}";
                return null;
            }
            c = c with { Biome = b };
            any = true;
        }
        foreach (var (flag, set) in new (string, Func<SpawnContext, float, SpawnContext>)[]
                 {
                     ("--gravity", (x, v) => x with { Gravity = Math.Clamp(v, 0.05f, 4f) }), ("--air", (x, v) => x with { Air = Math.Clamp(v, 0, 1) }),
                     ("--wood", (x, v) => x with { Wood = Math.Clamp(v, 0, 1) }), ("--ore", (x, v) => x with { Ore = Math.Clamp(v, 0, 1) }), ("--rain", (x, v) => x with { Rain = Math.Clamp(v, 0, 1) }),
                 })
            if (Num(flag) is { } v)
            {
                c = set(c, v);
                any = true;
            }
        return any ? c : null;
    }
}

/// <summary>
/// How a style leans in each context: what the kit may swap and how far, keyed by style name as <see cref="LayerTables"/> is, so <c>Styles.cs</c> is untouched. A
/// style not named takes <see cref="VehicleContext.Default"/>. Every choice is still among the style's own parts and colours (or the game's parts of the same class
/// for a spring, as kit 3's swaps are), so a desert Bandit truck is still a Bandit truck.
/// </summary>
public sealed record ContextStyle
{
    /// <summary>How strongly a lean weighs (1 the default; 0 off for that style): plate materials, recipes, the chassis table.</summary>
    public double Strength = 1;

    /// <summary>The colours the ground calls for, by biome ("#c8b48a"): the style's palette colours nearest it are kept, where it has any near enough.</summary>
    public Dictionary<Biome, string> Ground = new()
    {
        [Biome.Desert] = "#c3a874",
        [Biome.Snow] = "#dfe3e6",
        [Biome.Swamp] = "#4c4a30",
        [Biome.Forest] = "#3f5534",
    };

    /// <summary>The palette roles the ground's colours go on (the war paint stays the faction's shout).</summary>
    public string[] GroundRoles = ["body", "rust"];

    /// <summary>How far (RGB, 0 to 441) a colour may be from the ground's to count as near it.</summary>
    public double Near = 120;
}

/// <summary>
/// The spawn context's leans on a build (docs/vehiclekit/VEHICLEKIT.md, "Spawn context"), each a no-op unless <see cref="SpawnContext.Current"/> matters. None
/// rolls a die of its own: a weighted pick takes the same one roll the plain pick did, and a swap is a choice, so the rest of a seed rolls exactly as it does in a
/// neutral context and a context only changes what it is about. What each did is noted on the vehicle (<see cref="Vehicle.ContextNotes"/>).
/// </summary>
public static class VehicleContext
{
    public static readonly ContextStyle Default = new();

    /// <summary>By style name (additive: Karl's styles are untouched).</summary>
    public static readonly Dictionary<string, ContextStyle> ByStyle = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bandit"] = new() { Strength = 1.2 }, // (scavengers: they build from whatever is lying about)
        ["IT"] = new() { Strength = 0.7, Near = 100 }, // (procurement buys what the catalogue has)
        ["Scam"] = new() { Strength = 0.7, Near = 100 },
        ["Highland"] = new() { Strength = 1.0, GroundRoles = ["rust"] }, // (the tartan may go to the hills' colours; the Saltire stays)
        ["British"] = new() { Strength = 0.6, GroundRoles = ["rust"], Near = 90 }, // (the bus stays red unless something near the ground is listed)
    };

    public static ContextStyle Of(FactionStyle st) => ByStyle.GetValueOrDefault(st.Name) ?? Default;

    private static readonly HashSet<string> PlateSlots = new(StringComparer.Ordinal) { "sheet", "wall", "deck", "bed", "armour", "patch" };

    /// <summary>A plate material's mass per square metre against Plate Steel's (light sheet 0.4, wood 0.35, tread 0.8, armour 2): the low-gravity lean's measure.</summary>
    private static double Heft(string material) => material switch
    {
        _ when material.StartsWith("Sheet", StringComparison.Ordinal) => 0.4,
        _ when material.StartsWith("Wood", StringComparison.Ordinal) => 0.35,
        _ when material.StartsWith("Tread", StringComparison.Ordinal) => 0.8,
        _ when material.StartsWith("Armour", StringComparison.Ordinal) => 2,
        _ => 1,
    };

    private static bool Metal(string material) => material.StartsWith("Plate Steel", StringComparison.Ordinal) || material.StartsWith("Tread", StringComparison.Ordinal) || material.StartsWith("Armour", StringComparison.Ordinal);

    /// <summary>
    /// A slot's choices leaned by the context (FactionStyle.Part's hook, on top of a role's weights): plates lean to wood in a forest, to metal over ore, to the
    /// lightest in low gravity, always among the style's own; off world, an engine slot becomes the Electric Motor. Null: no change.
    /// </summary>
    public static (string[] Items, double[] Weights)? Weigh(SpawnContext ctx, FactionStyle st, string slot, string[] items, double[]? weights)
    {
        var cs = Of(st);
        if (ctx.NoAir && slot is "engine" or "heavyEngine")
        {
            Note($"{slot} Electric Motor (no air: nothing burns)");
            return (["Electric Motor"], [1]);
        }
        if (!PlateSlots.Contains(slot) || cs.Strength <= 0 || items.Distinct().Count() < 2)
            return null;
        var w = weights?.ToArray() ?? Enumerable.Repeat(1.0, items.Length).ToArray();
        var changed = false;
        for (var i = 0; i < items.Length; i++)
        {
            var f = 1.0;
            if (ctx.WoodRich && items[i].StartsWith("Wood", StringComparison.Ordinal))
                f *= 1 + 5 * ctx.Wood * cs.Strength;
            if (ctx.OreRich && Metal(items[i]))
                f *= 1 + 3 * ctx.Ore * cs.Strength;
            if (ctx.LowGravity)
                f *= Math.Pow(1 / Heft(items[i]), 1.5 * cs.Strength);
            if (f != 1)
                changed = true;
            w[i] *= f;
        }
        if (!changed)
            return null;
        Note($"{slot} plates leaned ({string.Join("/", new[] { ctx.WoodRich ? "wood" : "", ctx.OreRich ? "ore" : "", ctx.LowGravity ? "light" : "" }.Where(x => x.Length > 0))})");
        return (items, w);
    }

    /// <summary>A palette role's colours for the ground (FactionStyle.Colour's hook): the style's own nearest the biome's colour, where it has one near enough; else all of them.</summary>
    public static IReadOnlyList<string> Colours(FactionStyle st, string role, string[] colours)
    {
        if (SpawnContext.Current is not { Matters: true } ctx || colours.Length < 2)
            return colours;
        var cs = Of(st);
        if (!cs.Ground.TryGetValue(ctx.Biome, out var target) || !cs.GroundRoles.Contains(role) || Rgb(target) is not { } t)
            return colours;
        var near = colours.Select(c => (C: c, D: Rgb(c) is { } x ? Dist(x, t) : double.MaxValue)).ToList();
        var best = near.Min(x => x.D);
        if (best > cs.Near)
            return colours; // (the style has nothing near the ground's colour: it keeps its own look)
        var kept = near.Where(x => x.D <= best + 40).Select(x => x.C).ToArray();
        if (kept.Length == colours.Length)
            return colours;
        Note($"{role} paint {Name(ctx.Biome)} ({string.Join("/", kept.Distinct())})");
        return kept;
    }

    private static string Name(Biome b) => SpawnContext.Name(b);

    private static (double R, double G, double B)? Rgb(string hex) =>
        hex.Length == 7 && hex[0] == '#' && int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? ((v >> 16) & 255, (v >> 8) & 255, v & 255) : null;

    private static double Dist((double R, double G, double B) a, (double R, double G, double B) b) =>
        Math.Sqrt((a.R - b.R) * (a.R - b.R) + (a.G - b.G) * (a.G - b.G) + (a.B - b.B) * (a.B - b.B));

    // ---- The chassis ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Kit 3's chassis table leaned (not for a family's model: its chassis is its family's): more axles in snow, a swamp and low gravity, one axle on a road, a
    /// short wheelbase in a forest (the rear axle further in, the front set back), a wide stance in a desert and in low gravity, more axles to carry high gravity.
    /// </summary>
    public static ChassisRange Chassis(ChassisRange r, FactionStyle st)
    {
        if (SpawnContext.Current is not { Matters: true } ctx || SpawnContext.KitOnly)
            return r;
        var k = Of(st).Strength;
        if (k <= 0)
            return r;
        var o = r;
        var axles = ctx.Biome switch { Biome.Snow => 1.6, Biome.Swamp => 2.0, Biome.Road => 0.4, Biome.Forest => 0.6, _ => 1.0 } * (ctx.LowGravity ? 2.0 : ctx.HighGravity ? 1.8 : 1.0);
        axles = Math.Pow(axles, k);
        r = r with { Tag = r.Tag * axles, Pusher = r.Pusher * axles, Tandem = r.Tandem * axles };
        if (ctx.Biome == Biome.Road)
            r = r with { Single = r.Single * (1 + k) };
        if (ctx.Biome is Biome.Desert || ctx.LowGravity)
            r = r with { FrontWings = r.FrontWings.Select(x => (x.Wing, x.W * (x.Wing > 0.6f ? 1 + 3 * k : 1))).Append((0.75f, 0.5 * k)).GroupBy(x => x.Item1).Select(g => (g.Key, g.Sum(x => x.Item2))).ToArray() };
        if (ctx.Biome == Biome.Forest)
            r = r with
            {
                InsetMin = MathF.Min(r.InsetMax, r.InsetMin + 0.5f),
                FrontAxles = r.FrontAxles.Select(x => (x.Z, x.W * (x.Z > -1.2f ? 1 + 3 * k : 1))).ToArray(),
            };
        if (r != o)
            Note($"chassis table leaned ({Name(ctx.Biome)}{(ctx.LowGravity ? ", low gravity" : ctx.HighGravity ? ", high gravity" : "")})");
        return r;
    }

    /// <summary>A segment's length lean (kit 3's body range plus a role's): a forest wants short, a road a little long. Not for a model.</summary>
    public static double LengthBias(double bias)
    {
        if (SpawnContext.Current is not { Matters: true } ctx || SpawnContext.KitOnly)
            return bias;
        var add = ctx.Biome switch { Biome.Forest => -0.45, Biome.Road => 0.15, _ => 0 } + (ctx.HighGravity ? -0.2 : 0);
        return add == 0 ? bias : Math.Clamp(bias + add, -1, 1);
    }

    /// <summary>The room's section heights (kit 3's body range): high gravity keeps a low profile. Not for a model.</summary>
    public static BodyRange? Body(BodyRange? b)
    {
        if (b == null || SpawnContext.Current is not { HighGravity: true } || SpawnContext.KitOnly)
            return b;
        Note("low profile (high gravity)");
        return b with { Tall = b.Tall * 0.4, Low = b.Low * 2.5 };
    }

    private static readonly string[][] SpringClasses =
    [
        ["Leaf Spring", "Heavy Suspension", "Hydraulic Suspension"],
        ["Double Wishbone", "Heavy Double Wishbone"],
        ["Swing Arm", "Heavy Swing Arm"],
        ["Suspension", "Long-Travel Suspension", "Hydraulic Suspension", "Stiff Suspension", "Adjustable Suspension"],
    ];

    private static float Radius(string wheel) => ConstructParts.Find(wheel) is { } d && d.WheelRadius > 0 ? d.WheelRadius : 0.45f;

    /// <summary>A spring's travel and what it carries, from its part (the context's spring choices).</summary>
    private static (float Travel, float Load) Spring(string name)
    {
        if (ConstructParts.Find(name) is not { } d)
            return (0, 0);
        return (d.Travel, d.MaxLoad);
    }

    /// <summary>
    /// A chassis's running gear for the ground (after kit 3's roll, or a kit-1 recipe's pick): tyres from the style's own gear (the biggest that may go there in a
    /// desert, a swamp and a forest, spiked for snow, slicks on a road), springs within their class (the longest travel in a swamp and a forest, the stiffest on a
    /// road, the strongest in high gravity), a wide front wing in low gravity. A family's model only takes tyres within 5 cm of its own size, no bigger: the same
    /// chassis. <paramref name="heavy"/>: the rear wheel stands under a body and never grows (its top meets the walls hung under the floor).
    /// </summary>
    public static Gear Gear(Gear g, FactionStyle st, bool heavy)
    {
        if (SpawnContext.Current is not { Matters: true } ctx || Of(st).Strength <= 0)
            return g;
        var kitOnly = SpawnContext.KitOnly;
        var own = st.HeavyGear.Concat(st.LightGear).SelectMany(x => new[] { x.FrontWheel, x.RearWheel }).Distinct().Where(w => ConstructParts.Find(w) != null).ToArray();
        string Tyre(string now, bool front)
        {
            var r0 = Radius(now);
            bool Fits(string w) => w != now && (kitOnly ? Radius(w) <= r0 + 0.001f && Radius(w) >= r0 - 0.05f : front ? Radius(w) <= 0.56f : Radius(w) <= r0 + 0.001f || !heavy && Radius(w) <= 0.56f)
                                   && (front || w != "Dually Wheel" || heavy) && (w != "Dually Wheel" || !front);
            var pick = ctx.Biome switch
            {
                Biome.Desert or Biome.Swamp or Biome.Forest => own.Where(Fits).OrderByDescending(w => Radius(w) + (w == "Dually Wheel" ? 0.02f : 0)).ThenBy(w => w, StringComparer.Ordinal).FirstOrDefault(w => Radius(w) + (w == "Dually Wheel" ? 0.02f : 0) > r0),
                Biome.Snow => own.Contains("Spiked Wheel") && Fits("Spiked Wheel") ? "Spiked Wheel" : null,
                Biome.Road => own.Contains("Slick Wheel") && Fits("Slick Wheel") ? "Slick Wheel" : null,
                _ => null,
            };
            return pick ?? now;
        }
        string Sprung(string now)
        {
            if (kitOnly || SpringClasses.FirstOrDefault(k => k.Contains(now)) is not { } cls)
                return now;
            var options = cls.Where(s => ConstructParts.Find(s) != null && (heavy ? cls != SpringClasses[3] : true)).ToArray();
            Func<string, double>? score = ctx.HighGravity ? s => Spring(s).Load
                : ctx.Biome is Biome.Swamp or Biome.Forest ? s => Spring(s).Travel
                : ctx.Biome == Biome.Road ? s => -Spring(s).Travel
                : null;
            if (score == null)
                return now;
            var best = options.OrderByDescending(score).ThenBy(s => s, StringComparer.Ordinal).First();
            return score(best) > score(now) + 1e-4 ? best : now;
        }
        var o = g;
        g = g with { FrontWheel = Tyre(g.FrontWheel, true), RearWheel = Tyre(g.RearWheel, false), FrontSpring = Sprung(g.FrontSpring), RearSpring = Sprung(g.RearSpring) };
        if (g.TagWheel != null && !kitOnly)
            g = g with { TagWheel = Radius(g.RearWheel) <= 0.451f && g.RearWheel != "Dually Wheel" ? g.RearWheel : g.TagWheel };
        if (!VehicleChassis.Level(g.FrontSpring, g.RearSpring))
            g = g with { FrontSpring = o.FrontSpring, RearSpring = o.RearSpring }; // (never a chassis that stands on a slope: VehicleChassis.Level)
        if (g == o)
            return o;
        var bits = new List<string>(4);
        if (g.FrontWheel != o.FrontWheel || g.RearWheel != o.RearWheel)
            bits.Add($"tyres {g.FrontWheel}/{g.RearWheel}");
        if (g.FrontSpring != o.FrontSpring || g.RearSpring != o.RearSpring)
            bits.Add($"springs {g.FrontSpring}/{g.RearSpring}");
        Note(string.Join(", ", bits));
        return g;
    }

    /// <summary>A kit-1 recipe pick (Vehicle.PickGear) leaned to the ground, the same one roll: a recipe whose tyres or springs the context wants weighs more.</summary>
    public static Gear? Pick(Dice d, FactionStyle st, Gear[] fits)
    {
        if (SpawnContext.Current is not { Matters: true } ctx || SpawnContext.KitOnly || fits.Length < 2)
            return null;
        var k = Of(st).Strength;
        double W(Gear g) => Math.Pow(ctx.Biome switch
        {
            Biome.Desert => 1 + 2 * Math.Max(0, Radius(g.RearWheel) - 0.4f) * 10 * 0.2,
            Biome.Snow => g.FrontWheel == "Spiked Wheel" || g.RearWheel == "Spiked Wheel" ? 3 : 1,
            Biome.Swamp or Biome.Forest => 1 + Math.Max(Spring(g.FrontSpring).Travel, Spring(g.RearSpring).Travel) * 4,
            Biome.Road => g.FrontWheel == "Slick Wheel" || g.RearWheel == "Slick Wheel" ? 3 : 1,
            _ => 1,
        } * (ctx.HighGravity ? 1 + Spring(g.RearSpring).Load / 4000 : 1), k);
        var weights = fits.Select(W).ToArray();
        if (weights.All(w => Math.Abs(w - weights[0]) < 1e-9))
            return null;
        var roll = d.Next() * weights.Sum();
        for (var i = 0; i < fits.Length; i++)
            if ((roll -= weights[i]) < 0 || i == fits.Length - 1)
            {
                Note($"gear {fits[i].Name} (leaned)");
                return fits[i];
            }
        return fits[^1];
    }

    // ---- What it did ------------------------------------------------------------------------------------------------

    [ThreadStatic] private static List<string>? _notes;

    /// <summary>The vehicle being made on this thread takes the notes from here on (each plan its own: <see cref="Vehicle"/>'s constructor).</summary>
    internal static void NotesTo(List<string>? notes) => _notes = notes;

    /// <summary>Notes what a lean changed, once (the kit line's "context ...; changed ..." and <c>--context</c>).</summary>
    public static void Note(string what)
    {
        if (_notes != null && what.Length > 0 && !_notes.Contains(what))
            _notes.Add(what);
    }
}

public sealed partial class Vehicle
{
    /// <summary>The spawn context it was made in (null: none or neutral, as every vehicle before phase 7).</summary>
    public SpawnContext? Context { get; private set; }

    /// <summary>What the context changed on this plan ("tyres Large Wheel/Off-Road Wheel", "body paint desert (#a39a84)"...).</summary>
    public List<string> ContextNotes { get; } = new();

    /// <summary>The kit line's context, or "" (the blueprint's second comment: <c>// context desert, gravity ...; changed ...</c>).</summary>
    protected override string ContextLine() => Context is { Matters: true } c
        ? $"{c.Detail}{(SpawnContext.KitOnly || ModelContext ? " (kit only: a family's model)" : "")}{(ContextNotes.Count > 0 ? "; changed " + string.Join(", ", ContextNotes) : "; nothing to change")}"
        : "";

    /// <summary>A family's model was made in it (its kit only changed).</summary>
    public bool ModelContext { get; private set; }

    /// <summary>Takes this thread's context, and its notes from here on (the constructors).</summary>
    private void TakeContext()
    {
        if (SpawnContext.Current is not { } c)
            return;
        Context = c;
        ModelContext = SpawnContext.KitOnly;
        VehicleContext.NotesTo(ContextNotes);
    }

    /// <summary>
    /// Off world (no air), an Electric Motor stands where the engine went: a Battery Pack on its side (the hood stands on its top), and no fuel line (the tanks stay,
    /// and their fuel is still a weak point). True when it is electric.
    /// </summary>
    public bool Electric => Engine is { } e && e.Def.Role == PartRole.Engine && e.Def.FuelKind == EngineFuel.Electric;

    private void ElectricKit()
    {
        if (!Electric)
            return;
        foreach (var side in new[] { "-x", "+x", "-z" })
        {
            try
            {
                Onto(Engine!, side, "Battery Pack", "-y");
                VehicleContext.Note("electric drive: an Electric Motor and a Battery Pack");
                return;
            }
            catch (VehicleKitException)
            {
                // (that side is taken: the next)
            }
        }
        throw new VehicleKitException("no air here, and the Electric Motor has nowhere to carry its Battery Pack");
    }
}

/// <summary><c>--context "Style Shape|Style Role" [--tier N] [--seeds N] [--from S] --biome ... --gravity G --air 0|1 [--check] [--sweep]</c>: what a context changes against neutral.</summary>
public static class VehicleContextCli
{
    private static string? Flag(string[] args, string flag) => args.SkipWhile(a => !a.Equals(flag, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--"));

    /// <summary>The contexts a sweep tries: every biome, low and high gravity, no air, wood, ore.</summary>
    public static readonly SpawnContext[] Sweep =
    [
        new() { Biome = Biome.Desert }, new() { Biome = Biome.Snow }, new() { Biome = Biome.Swamp }, new() { Biome = Biome.Forest }, new() { Biome = Biome.Road },
        new() { Gravity = 0.4f }, new() { Gravity = 1.8f }, new() { Air = 0 }, new() { Wood = 1 }, new() { Ore = 1 },
    ];

    public static int Run(string what, string[] args)
    {
        var seeds = int.TryParse(Flag(args, "--seeds"), out var n) ? Math.Clamp(n, 1, 200) : 4;
        var from = int.TryParse(Flag(args, "--from"), out var f) ? f : 1;
        var tier = int.TryParse(Flag(args, "--tier"), out var t) ? Math.Clamp(t, 1, VehicleRoles.MaxTier) : 2;
        var check = args.Any(a => a.Equals("--check", StringComparison.OrdinalIgnoreCase));
        var sweep = args.Any(a => a.Equals("--sweep", StringComparison.OrdinalIgnoreCase));
        var given = SpawnContext.FromArgs(args, out var bad);
        if (bad.Length > 0)
        {
            Console.WriteLine(bad);
            return 1;
        }
        var contexts = sweep ? Sweep : given is { } g1 ? [g1] : [];
        if (contexts.Length == 0)
        {
            Console.WriteLine("--context: give --biome, --gravity, --air, --wood or --ore, or --sweep");
            return 1;
        }
        // What to make: "Style Shape" (a generator) or "Style role" (kit 4).
        IVehicleGenerator? gen = VehicleRegistry.Find(what);
        FactionStyle? st = null;
        VehicleRole? role = null;
        if (gen == null)
            foreach (var s in Styles.All)
                if (what.StartsWith(s.Name + " ", StringComparison.OrdinalIgnoreCase) && VehicleRoles.Find(what[(s.Name.Length + 1)..]) is { } r)
                    (st, role) = (s, r);
        if (gen == null && role == null)
        {
            Console.WriteLine($"--context: no generator or role \"{what}\"");
            return 1;
        }
        var failed = 0;
        var made = 0;
        var changedAny = 0;
        var lines = new List<string>();
        foreach (var ctx in contexts)
        {
            int ok = 0, fails = 0, ident = 0, checks = 0;
            double idSum = 0;
            for (var seed = from; seed < from + seeds; seed++)
            {
                VehicleGenJob.Yield();
                var (g, ask) = gen != null ? (gen, (RoleAsk?)null) : (VehicleRoles.ShapeFor(st!, role!, seed), new RoleAsk(role!, tier, st!) { Shape = VehicleSpawnKit.Names(VehicleRoles.ShapeFor(st!, role!, seed)).Shape });
                Vehicle? neutral = null, inCtx = null;
                string why = "";
                try
                {
                    neutral = VehicleRegistry.Make(g, seed, role: ask);
                }
                catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
                {
                    why = "neutral: " + ex.Message;
                }
                try
                {
                    inCtx = VehicleRegistry.Make(g, seed, role: ask, context: ctx);
                }
                catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
                {
                    why += (why.Length > 0 ? "; " : "") + ex.Message;
                }
                made++;
                if (inCtx == null)
                {
                    fails++;
                    failed++;
                    Console.WriteLine($"  {ctx.Label,-14} {g.Name} {seed}: REFUSED: {why}");
                    continue;
                }
                ok++;
                var id = inCtx.Identity ?? VehicleIdentity.Of(inCtx);
                idSum += id.Total;
                if (id.Missing.Count == 0 && id.Total >= VehicleIdentity.SignatureOf(inCtx.Style).Threshold)
                    ident++;
                var diff = neutral != null ? Diff(neutral, inCtx) : "(no neutral to compare)";
                if (diff.Length > 0)
                    changedAny++;
                Console.WriteLine($"  {ctx.Label,-14} {inCtx.Name}: {(inCtx.ContextNotes.Count > 0 ? string.Join("; ", inCtx.ContextNotes) : "nothing to change")}");
                Console.WriteLine($"    vs neutral: {(diff.Length > 0 ? diff : "the same parts")}; identity {id.Total:0.00}{(id.Missing.Count > 0 ? " (no " + string.Join(", no ", id.Missing) + ")" : "")}"
                                  + (inCtx.DriveCheck is { } dc ? $"; drive check tilt {dc.Tilt:0.0}, pulse {dc.Moved:0.0} m" : ""));
                if (check)
                {
                    var rep = VehicleChecks.Check(inCtx.Text(), inCtx.Name + ".json", g.Checks, inCtx.Rooms);
                    checks += rep.Passed ? 1 : 0;
                    Console.WriteLine($"    full check: {(rep.Passed ? "OK" : string.Join("; ", rep.Failures))}");
                }
            }
            lines.Add($"  {ctx.Label,-14} {ok}/{ok + fails} made, identity mean {(ok > 0 ? idSum / ok : 0):0.00}, {ident}/{ok} with every mark at the threshold{(check ? $", full check {checks}/{ok}" : "")}");
        }
        Console.WriteLine("context summary:");
        foreach (var l in lines)
            Console.WriteLine(l);
        Console.WriteLine($"  {made} made, {failed} refused, {changedAny} differ from neutral; drive check: {VehicleDriveCheck.Checked} checked, {VehicleDriveCheck.Caught} refused, {(VehicleDriveCheck.Checked > 0 ? VehicleDriveCheck.TotalMs / VehicleDriveCheck.Checked : 0):0} ms mean, {VehicleDriveCheck.WorstMs:0} worst");
        return failed;
    }

    /// <summary>What differs in parts between two vehicles: "+2 Large Wheel, -2 Off-Road Wheel, mass +120 kg, paint on 14 parts".</summary>
    public static string Diff(Vehicle a, Vehicle b)
    {
        var (ca, ma, pa) = a.Summary();
        var (cb, mb, pb) = b.Summary();
        var bits = new List<string>();
        foreach (var k in ca.Keys.Union(cb.Keys).OrderBy(x => x, StringComparer.Ordinal))
        {
            var d = cb.GetValueOrDefault(k) - ca.GetValueOrDefault(k);
            if (d != 0)
                bits.Add($"{(d > 0 ? "+" : "")}{d} {k}");
        }
        var paint = 0;
        for (var i = 0; i < Math.Min(pa.Length, pb.Length); i++)
            if (pa[i] != pb[i])
                paint++;
        var sb = new StringBuilder(string.Join(", ", bits.Take(12)));
        if (bits.Count > 12)
            sb.Append($", {bits.Count - 12} more kinds");
        var dm = mb - ma;
        if (MathF.Abs(dm) >= 1)
            sb.Append(sb.Length > 0 ? ", " : "").Append($"mass {(dm > 0 ? "+" : "")}{dm:0} kg");
        if (paint > 0)
            sb.Append(sb.Length > 0 ? ", " : "").Append($"paint on {paint} parts");
        return sb.ToString();
    }
}

public sealed partial class Vehicle
{
    /// <summary>Its parts by name, its mass and every part's paint (<c>--context</c>'s comparison).</summary>
    internal (Dictionary<string, int> Kinds, float Mass, uint[] Paints) Summary()
    {
        C.Rebuild();
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        var paints = new uint[C.PartCount];
        for (var i = 0; i < C.PartCount; i++)
        {
            var n = C.DefOf(i).Name;
            d[n] = d.GetValueOrDefault(n) + 1;
            paints[i] = C.Parts[i].Paint;
        }
        return (d, C.Mass, paints);
    }
}
