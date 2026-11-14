using System.Globalization;
using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.EngineKit;

/// <summary>Which way the banks of cylinders point from the crank (its sockets are square: a V is 90 degrees).</summary>
public enum EngineLayout
{
    Auto,
    Inline, // one bank, up
    V, // up and to the right
    Flat, // left and right (a boxer)
    W, // left, up and right
    Radial, // all four sides
}

public enum EngineBoost
{
    Auto,
    None,
    Supercharger,
    Turbo,
}

/// <summary>
/// What an engine is wanted for (docs/vehiclekit/ENGINEKIT.md, "The request"). Zero means "any". Power is the engine's peak (kW at its size's peak rpm),
/// torque the crank's at that rpm; give either or both. <see cref="Fit"/> is the box it must go in: x length along the crank, y width, z height (metres).
/// </summary>
public sealed record EngineRequest
{
    public float PowerKw { get; init; }
    public float TorqueNm { get; init; }
    public float PeakRpm { get; init; } // the peak rpm wanted: picks the size (small engines rev high, big ones slow) and the stroke (a long one revs lower)
    public float MinLimiterRpm { get; init; } // the lowest limiter the engine may have (0: any): a vehicle's stock gearbox is geared for its size's limiter, so a long stroke would cap its top speed
    public string Fuel { get; init; } = "petrol"; // modular engines burn fuel: anything else is built as petrol, with a note
    public Vector3 Fit { get; init; } // length, width, height
    public float MaxMass { get; init; }
    public int Tier { get; init; } = 1; // 1 to 5: which pieces it may use (the tiers' cylinders, fuel systems and boosters)
    public EngineLayout Layout { get; init; }
    public EngineBoost Boost { get; init; }
    public int Size { get; init; } = -1; // -1 any, else 0 small to 3 capital
    public bool Underside { get; init; } = true; // false: no bank points down (an engine standing on a vehicle's sill: a radial's lower bank would be in the ground)
    public bool Exhausts { get; init; } = true; // an outlet on every head without a fuel system (looks only)
    public string Paint { get; init; } = ""; // a colour for the crank and flywheel ("" the parts' own): a faction's
    public string Name { get; init; } = "";
    public int Seed { get; init; }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{(PowerKw > 0 ? $"{PowerKw:0} kW" : "")}{(TorqueNm > 0 ? $" {TorqueNm:0} N m" : "")}{(PeakRpm > 0 ? $" peak {PeakRpm:0} rpm" : "")}, tier {Tier}, {Layout}, boost {Boost}" +
        $"{(Fit != Vector3.Zero ? $", fit {Fit.X:0.##}x{Fit.Y:0.##}x{Fit.Z:0.##} m" : "")}{(MaxMass > 0 ? $", under {MaxMass:0} kg" : "")}{(Size >= 0 ? ", " + EngineKit.SizeWords[Size] : "")}, seed {Seed}");
}

/// <summary>One way to make an engine: the plan the kit scores before it builds.</summary>
public sealed record EnginePlan(int Size, EngineLayout Layout, int Cranks, string Cylinder, string FuelPart, string? BoostPart, float Kw, float Mass, Vector3 Dims, float Score, bool Feasible, string Why)
{
    public int Banks => EngineKit.BankSockets(Layout).Length;
    public int Cylinders => Cranks * Banks;
    public string Crank { get; init; } = ""; // (the crank section's name: "" the size's catalogue one; a bored engine's is pressed longer)
    public string Grade { get; init; } = ""; // (a bored plan's word, "Racing": the grade whose power its thinner wall gives; "" plain)
    public float Stroke { get; init; } = 1f; // (its cylinders' stroke ratio, stroke power 2026-11-14: their kW times it, the revs over it)
    public float PeakRpm { get; init; } // (the revs at its peak power: the size's over its stroke)
    public float TorqueNm => PeakRpm > 0 ? Kw * 1000 / (PeakRpm / ConstructDrive.RpmPerRadS) : 0;
}

/// <summary>What the kit made: the blueprint, and the numbers the game itself works out for it (<see cref="ConstructEngine"/>).</summary>
public sealed class EngineResult
{
    public bool Built;
    public string Text = "", Name = "", Layout = "", Pieces = "", Note = "", Fault = "";
    public float Kw, TorqueNm, IdleRpm, PeakRpm, MaxRpm, Mass, BurnLps;
    public Vector3 Dims; // length, width, height as built
    public int Cylinders, Banks, Cranks, Parts;
    public EnginePlan? Plan;

    public override string ToString() => !Built ? $"no engine: {Note}" : string.Create(CultureInfo.InvariantCulture,
        $"{Name}: {Layout} {Kw:0} kW, {TorqueNm:0} N m at {PeakRpm:0} rpm (idle {IdleRpm:0}, limiter {MaxRpm:0}), {Mass:0} kg, {Dims.X:0.00} x {Dims.Y:0.00} x {Dims.Z:0.00} m, " +
        $"{Cylinders} cylinders in {Banks} bank(s) on {Cranks} crank(s), {Parts} parts, {BurnLps:0.000} l/s at full throttle; {Pieces}{(Fault.Length > 0 ? "; the game says: " + Fault : "")}{(Note.Length > 0 ? "; note: " + Note : "")}");
}

/// <summary>
/// The Engine Kit (docs/vehiclekit/ENGINEKIT.md): <see cref="Request"/> plans the modular engine nearest a request from every size, layout, crank count and
/// tier's pieces it allows (a few thousand sums, no building), then builds the best plans in turn through the game's own build rules (<see cref="BlueprintScript"/>)
/// until one builds and fits, and reads the numbers back from the game's own engine sums. Deterministic for a request and seed. Inside a vehicle generation it
/// runs in the generation's slices (every placing yields). Allocates: never on a tick.
/// </summary>
public static class EngineKit
{
    public static readonly string[] SizeWords = ["Small", "Medium", "Large", "Capital"];

    /// <summary>The most crank sections an engine gets (its length: 4 m at Small, 16 m at Medium).</summary>
    public const int MaxCranks = 8;

    /// <summary>How many plans are built before the kit gives up on building (each refused one says why).</summary>
    public const int MaxBuildTries = 6;

    /// <summary>How far past the best plan's score a seed may pick (seed 0 and every other seed pick among the same few).</summary>
    public const float SeedBand = 0.04f;

    public static string[] BankSockets(EngineLayout l) => l switch
    {
        EngineLayout.V => ["+y", "+x"],
        EngineLayout.Flat => ["+x", "-x"],
        EngineLayout.W => ["-x", "+y", "+x"],
        EngineLayout.Radial => ["+y", "+x", "-y", "-x"],
        _ => ["+y"],
    };

    private static readonly EngineLayout[] Layouts = [EngineLayout.Inline, EngineLayout.V, EngineLayout.Flat, EngineLayout.W, EngineLayout.Radial];

    /// <summary>A layout's usual name for <paramref name="cyl"/> cylinders: I4, V8, B4 (a flat), W12, R16.</summary>
    public static string LayoutName(EngineLayout l, int cyl) => (l switch { EngineLayout.V => "V", EngineLayout.Flat => "B", EngineLayout.W => "W", EngineLayout.Radial => "R", _ => "I" }) + cyl.ToString(CultureInfo.InvariantCulture);

    private static ConstructPartDef? Part(string name) => ConstructParts.Find(name);

    /// <summary>The tier a piece needs: the plain cylinder and the carburettor 1, the injector rail 2, the supercharger 3, the turbo 4; a tier cylinder its own.</summary>
    private static int TierOf(ConstructPartDef d) => d.Piece switch
    {
        EnginePiece.Fuel => d.PiecePower > 1 ? 2 : 1,
        EnginePiece.Boost => d.PieceSpool > 0 ? 4 : 3,
        _ => d.Tier,
    };

    /// <summary>
    /// A bore the kit plans with: the cylinder (its catalogue part, or the plain one's preset pressed with a thinner wall, so a wider bore in the
    /// same across: bored cylinders, 2026-11-14), the crank section it stands on (pressed longer only when the fuel system or booster the bank
    /// needs is wider than the catalogue section's pitch; its across, so its scale and revs, stay the size's), the length's stretch, the tier
    /// and grade word, and <see cref="Of"/> for the fuel system and booster pressed to the cylinder's scale (a small carburettor would starve a
    /// bored bank: sized-parts design 6.4).
    /// </summary>
    private readonly record struct Bore(float Ratio, int Tier, string Word, ConstructPartDef? Cylinder, ConstructPartDef? Crank, float Pitch, float Frail = 0, float Stroke = 1f, float Kw = 0, float BoostRatio = 1f, float BoostKw = 0)
    {
        // (Ratio: the scale a fuel system or booster needs to feed the cylinder's Kw whole, over the plain one's: by bore and stroke. Wider to
        // feed it, no taller: its across sets its scale, and a vehicle's bay is low.)
        public string? Of(string part) => Ratio == 1 ? part : FeedFor(part, Ratio, Kw);

        /// <summary>A booster for the bank: sized by the cylinder's bore alone (its kW at its bore: the engine scales a booster's share by that, whatever the stroke).</summary>
        public string? OfBoost(string part) => BoostRatio == 1 ? part : FeedFor(part, BoostRatio, BoostKw);
    }

    /// <summary>
    /// <paramref name="part"/> (a catalogue fuel system or booster) pressed wider, from <paramref name="ratio"/> times its preset up in 5 cm steps, until its kW
    /// reaches <paramref name="kw"/> (a scale's three figures can leave the first one a hair short, and a bank fed 99.5% is starved: design 6.4).
    /// </summary>
    private static string? FeedFor(string part, float ratio, float kw)
    {
        var rules = EngineRules.Current;
        string? made = null;
        for (var x = ratio; x < ratio * 1.25f; x *= 1.02f)
        {
            if (PressedAt(part, x, up: true, keepLength: true) is not { } pk || Part(pk) is not { } d)
                continue;
            made = pk;
            if (kw <= 0 || rules.KwAt(rules.ScaleOf(d)) >= kw * 0.9995f)
                return pk;
        }
        return made;
    }

    /// <summary>The strokes the kit plans with (stroke power, 2026-11-14): short, plain and two long, as ratios of the preset's length (5 cm steps; the die's range is 0.6 to 1.6).</summary>
    public static readonly float[] Strokes = [0.8f, 1f, 1.33f, 1.6f];

    /// <summary><paramref name="cyl"/>'s blank (a pressed key, or the catalogue part's preset) <paramref name="stroke"/> times as long, to the 5 cm lattice; the part itself at 1; null when no die presses it.</summary>
    private static ConstructPartDef? Stroked(ConstructPartDef cyl, float stroke)
    {
        if (stroke == 1f)
            return cyl;
        var key = PresetKey.IsMatch(cyl.Name) ? cyl.Name : PressTable.Current.PresetKeyOf(cyl);
        if (key == null || PresetKey.Match(key) is not { Success: true } m)
            return null;
        var cm = Math.Max(5, (int)MathF.Round(int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture) * stroke / 5, MidpointRounding.AwayFromZero) * 5);
        return Part(string.Create(CultureInfo.InvariantCulture, $"{m.Groups[1].Value}{m.Groups[2].Value}{m.Groups[3].Value} {m.Groups[4].Value}x{m.Groups[5].Value}x{cm} {m.Groups[7].Value})"));
    }

    private static readonly System.Text.RegularExpressions.Regex PresetKey = new(@"^(Pressed Part \(\S+ )(round|solid|hollow|pipe)(\d*) (\d+)x(\d+)x(\d+) (\S+)\)$");

    /// <summary>A catalogue part's preset blank times <paramref name="ratio"/> on each side (a pipe's wall too), to the 5 cm lattice, as a pressed key; null when no die presses it.</summary>
    private static string? PressedAt(string part, float ratio, bool up = false, bool keepLength = false)
    {
        if (Part(part) is not { } d || PressTable.Current.PresetKeyOf(d) is not { } key || PresetKey.Match(key) is not { Success: true } m)
            return null;
        int L5(float cm) => Math.Max(5, (up ? (int)MathF.Ceiling(cm / 5 - 1e-3f) : (int)MathF.Round(cm / 5, MidpointRounding.AwayFromZero)) * 5); // (up: a fuel system or booster never smaller than its bank, or it starves it)
        var wall = m.Groups[3].Value.Length > 0 ? L5(int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) * ratio).ToString(CultureInfo.InvariantCulture) : "";
        int w = L5(int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) * ratio), h = L5(int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture) * ratio), l = keepLength ? int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture) : L5(int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture) * ratio);
        var made = string.Create(CultureInfo.InvariantCulture, $"{m.Groups[1].Value}{m.Groups[2].Value}{wall} {w}x{h}x{l} {m.Groups[7].Value})");
        return Part(made) != null ? made : null;
    }

    /// <summary>
    /// The plain cylinder's preset with the thickest wall (whole cm) that gives at least <paramref name="power"/> times its kW, the same across
    /// and length; the thinnest wall that still adds power when none reaches it; null when no thinner wall adds power (or no die presses it).
    /// With it, the power it gives (times the plain one's) and the share of the plain wall's metal, so of its health, it gives up.
    /// </summary>
    private static (ConstructPartDef Cylinder, float Power, float Frail)? BoredAt(ConstructPartDef plain, float power)
    {
        if (PressTable.Current.PresetKeyOf(plain) is not { } key || PresetKey.Match(key) is not { Success: true } m || m.Groups[2].Value != "pipe" || m.Groups[3].Value.Length == 0)
            return null;
        var rules = EngineRules.Current;
        var plainKw = rules.KwAt(rules.ScaleOf(plain));
        var wall0 = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        var across = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
        float Metal(int wall) => across * (float)across - (across - 2f * wall) * (across - 2f * wall); // (a ring's area over pi/4: the same across and length)
        (ConstructPartDef, float, float)? best = null;
        var bestKw = plainKw * 1.001f;
        for (var wall = wall0 - 1; wall >= 1; wall--)
        {
            if (Part(string.Create(CultureInfo.InvariantCulture, $"{m.Groups[1].Value}pipe{wall} {m.Groups[4].Value}x{m.Groups[5].Value}x{m.Groups[6].Value} {m.Groups[7].Value})")) is not { } d)
                continue;
            var kw = rules.KwAt(rules.ScaleOf(d));
            var got = (d, kw / plainKw, 1 - Metal(wall) / Metal(wall0));
            if (kw >= plainKw * power * 0.97f)
                return got;
            if (kw > bestKw)
            {
                best = got;
                bestKw = kw;
            }
        }
        return best;
    }

    /// <summary>A crank section of the size at least <paramref name="pitchM"/> long: the catalogue one, or its preset pressed longer (5 cm steps, the same across).</summary>
    private static ConstructPartDef? Crank(string sw, float pitchM)
    {
        if (Part($"{sw} Crank Section") is not { } d)
            return null;
        var len = (d.BoundsMax - d.BoundsMin).Z;
        if (pitchM <= len + 1e-3f)
            return d;
        if (PressTable.Current.PresetKeyOf(d) is not { } key || PresetKey.Match(key) is not { Success: true } m)
            return null;
        var cm = (int)MathF.Ceiling(pitchM * 100 / 5) * 5;
        return Part(string.Create(CultureInfo.InvariantCulture, $"{m.Groups[1].Value}{m.Groups[2].Value}{m.Groups[3].Value} {m.Groups[4].Value}x{m.Groups[5].Value}x{cm} {m.Groups[7].Value})"));
    }

    /// <summary>
    /// The cylinders a tier allows: the plain bore, then each tier grade's (the plain cylinder with the thinner wall that gives its grade's power), each at every
    /// stroke in <see cref="Strokes"/> (stroke power, 2026-11-14: a longer stroke is more displacement, so more power at lower revs, for more metal).
    /// </summary>
    private static IEnumerable<Bore> Bores(string sw, int tier, bool shortToo)
    {
        var plain = Part($"{sw} Cylinder");
        if (plain == null || Part($"{sw} Crank Section") is not { } crank0)
        {
            yield return new Bore(1, 1, "", plain, Part($"{sw} Crank Section"), 1);
            yield break;
        }
        var rules = EngineRules.Current;
        var bores = new List<(ConstructPartDef Cyl, int Tier, string Word, float Frail)> { (plain, 1, "", 0) };
        ConstructPartDef? last = null;
        foreach (var g0 in EngineKitParts.Grades)
        {
            if (g0.Tier > tier || BoredAt(plain, g0.Power) is not { } bored || bored.Cylinder == last)
                continue;
            last = bored.Cylinder;
            // Named by what it reaches, not what was asked (a Medium or Large preset's wall is already thin: its best is about x1.37, a "Bored" one).
            var reached = Array.FindLastIndex(EngineKitParts.Grades, x => bored.Power >= x.Power * 0.97f);
            if (reached >= 0)
                bores.Add((bored.Cylinder, EngineKitParts.Grades[reached].Tier, EngineKitParts.Grades[reached].Word, Math.Clamp(bored.Frail, 0f, 1f)));
        }
        var plainKw = rules.CylinderKw(plain);
        var pitch = (crank0.BoundsMax - crank0.BoundsMin).Z;
        var plainBoreKw = rules.KwAt(rules.ScaleOf(plain));
        foreach (var b in bores)
            foreach (var st in Strokes)
            {
                if (st < 1f && !shortToo || Stroked(b.Cyl, st) is not { } cyl)
                    continue;
                VehicleGenJob.Yield(); // (each stroke presses up to a dozen pieces the first time: a slice of its own in a generation)
                // The fuel system pressed to feed the cylinder's kW whole (by bore and stroke: a small carburettor starves a long-stroke bank, design
                // 6.4), the booster to its kW at its bore (the engine scales a booster's share by that); the widest of them (and the cylinder) sets the
                // crank's pitch, so neighbours clear by the z-fighting gap.
                var kw = rules.CylinderKw(cyl);
                var boreKw = rules.KwAt(rules.ScaleOf(cyl));
                var ratio = MathF.Abs(kw / plainKw - 1f) < 1e-4f ? 1f : rules.ScaleForKw(kw) / rules.ScaleOf(plain);
                var boostRatio = MathF.Abs(boreKw / plainBoreKw - 1f) < 1e-4f ? 1f : rules.ScaleOf(cyl) / rules.ScaleOf(plain);
                var across = MathF.Max((cyl.BoundsMax - cyl.BoundsMin).X, (cyl.BoundsMax - cyl.BoundsMin).Z);
                foreach (var (piece, boost) in Feeders)
                    if ((boost ? boostRatio : ratio) != 1f && FeedFor($"{sw} {piece}", boost ? boostRatio : ratio, boost ? boreKw : kw) is { } pk && Part(pk) is { } pd)
                        across = MathF.Max(across, MathF.Max((pd.BoundsMax - pd.BoundsMin).X, (pd.BoundsMax - pd.BoundsMin).Z));
                var crank = across + 2 * CallOfSlop.Pressed.Gap(across) <= pitch + 1e-3f ? crank0 : Crank(sw, across + 2 * CallOfSlop.Pressed.Gap(across));
                yield return new Bore(ratio, b.Tier, b.Word, cyl, crank, crank == null ? 1 : (crank.BoundsMax - crank.BoundsMin).Z / pitch, b.Frail, EngineRules.StrokeOf(cyl), kw, boostRatio, boreKw);
            }
    }

    private static readonly (string Piece, bool Boost)[] Feeders = [("Carburettor", false), ("Injector Rail", false), ("Supercharger", true), ("Turbo", true)];

    /// <summary>Every plan the request allows, best first (feasible ones before the rest).</summary>
    public static List<EnginePlan> Plans(EngineRequest r)
    {
        var rules = EngineRules.Current;
        var tier = Math.Clamp(r.Tier, 1, ConstructParts.MaxTier);
        var plans = new List<EnginePlan>();
        var k = 0;
        for (var size = 0; size < SizeWords.Length; size++)
        {
            if (r.Size >= 0 && r.Size != size)
                continue;
            var sw = SizeWords[size];
            var s = rules.Scale[size];
            var peakW = rules.Peak[size] / ConstructDrive.RpmPerRadS; // rad/s
            var target = r.PowerKw > 0 ? r.PowerKw : r.TorqueNm > 0 ? r.TorqueNm * peakW / 1000f : 0;
            if (target <= 0)
                throw new ArgumentException("an engine request needs a power or a torque");
            var fly = Part($"{sw} Flywheel");
            var exhaust = Part($"{sw} Exhaust Outlet");
            var starter = Part($"{sw} Starter Motor");
            if (Part($"{sw} Crank Section") == null || fly == null)
                continue;
            // The plain bore (tiers 1 and 2) and each tier's bore (sized parts phase 6: the tier cylinders are retired, so a tier's extra
            // power is a plain cylinder pressed wider than its crank's scale, with its fuel system and booster pressed to match).
            foreach (var bore in Bores(sw, tier, r.PeakRpm > rules.Peak[size])) // (short strokes only when revs above the size's peak are asked)
            {
                // (A long stroke's limiter may be under what the request allows; a short stroke only spins faster for less power, so it is planned only when revs
                // above the size's peak are asked for.)
                if (r.MinLimiterRpm > 0 && rules.Max[size] / bore.Stroke < r.MinLimiterRpm * 0.999f || bore.Stroke < 1f && !(r.PeakRpm > rules.Peak[size]))
                    continue;
                var fuels = new[] { $"{sw} Carburettor", $"{sw} Injector Rail" }.Where(f => Part(f) is { } d && TierOf(d) <= tier).Select(f => bore.Of(f)).Where(f => f != null).Select(f => Part(f!)!).ToList();
                var boosts = new List<ConstructPartDef?>();
                if (r.Boost is EngineBoost.Auto or EngineBoost.None)
                    boosts.Add(null);
                if (r.Boost is EngineBoost.Auto or EngineBoost.Supercharger && Part($"{sw} Supercharger") is { } sc && TierOf(sc) <= tier && bore.OfBoost(sc.Name) is { } scb)
                    boosts.Add(Part(scb));
                if (r.Boost is EngineBoost.Auto or EngineBoost.Turbo && Part($"{sw} Turbo") is { } tu && TierOf(tu) <= tier && bore.OfBoost(tu.Name) is { } tub)
                    boosts.Add(Part(tub));
                if (r.Boost is EngineBoost.Supercharger or EngineBoost.Turbo && boosts.Count == 0)
                    boosts.Add(null); // (the booster asked for is past the tier: none, and the note says so)
                if (bore.Cylinder is not { } cyl || bore.Crank is not { } crank || fuels.Count == 0)
                    continue;
                var frail = bore.Frail; // (the share of the plain wall's metal, so of its health, the bore gives up)
                var cylKw = rules.CylinderKw(cyl); // (by its bore and stroke; a file cylinder's is its size's row exactly)
                var peakRpm = rules.Peak[size] / bore.Stroke; // (the revs fall with the stroke: ModEngine.Rpm)
                var needsStarter = rules.StarterAt(s, bore.Stroke);
                var tallerCyl = Taller(cyl, $"{sw} Cylinder"); // (a long stroke stands further out from the crank)
                foreach (var layout in Layouts)
                {
                    if (r.Layout != EngineLayout.Auto && r.Layout != layout)
                        continue;
                    var banks = BankSockets(layout);
                    if (!r.Underside && Array.IndexOf(banks, "-y") >= 0)
                        continue;
                    foreach (var fuel in fuels)
                        foreach (var boost in boosts)
                            for (var n = 1; n <= MaxCranks; n++)
                            {
                                if (++k % 1024 == 0)
                                    VehicleGenJob.Yield();
                                var cylinders = n * banks.Length;
                                var kw = cylinders * cylKw * cyl.PiecePower * fuel.PiecePower * (boost?.PiecePower ?? 1f);
                                var mass = n * crank.Mass + cylinders * cyl.Mass + banks.Length * (fuel.Mass + (boost?.Mass ?? 0)) + fly.Mass
                                           + (r.Exhausts && exhaust != null ? (cylinders - banks.Length) * exhaust.Mass : 0)
                                           + (needsStarter && starter != null ? starter.Mass : 0);
                                var dims = Estimate(banks, n, boost != null, needsStarter, s, (tallerCyl + Taller(fuel, fuel.PiecePower > 1 ? $"{sw} Injector Rail" : $"{sw} Carburettor") + (boost == null ? 0 : Taller(boost, boost.PieceSpool > 0 ? $"{sw} Turbo" : $"{sw} Supercharger"))) / s) * new Vector3(bore.Pitch, 1, 1); // (a bored cylinder keeps its across: only the crank, the fuel system and booster may grow; a long stroke stands taller)
                                plans.Add(Score(r, size, layout, n, cyl.Name, fuel.Name, boost?.Name, kw, mass, dims, target, peakW, peakRpm, tier, Math.Max(bore.Tier, Math.Max(TierOf(fuel), boost != null ? TierOf(boost) : 1)), frail) with { Crank = crank.Name, Grade = bore.Word, Stroke = bore.Stroke, PeakRpm = peakRpm });
                            }
                }
            }
        }
        plans.Sort((a, b) => a.Feasible != b.Feasible ? (a.Feasible ? -1 : 1) : a.Score.CompareTo(b.Score));
        return plans;
    }

    /// <summary>
    /// A plan's size as built, from the pieces' shapes (the part kit's EngineParts): the crank 0.5 m a section along z and 0.5 square, a cylinder 0.75 m out from its 0.25 m
    /// socket, a fuel system or outlet 0.25 m on the head and a booster 0.25 m on that, the flywheel 0.9 m across and 0.4 m past the crank's end, a starter
    /// 0.32 m out on a free side; all times the size's scale. The kit measures the built one again.
    /// </summary>
    private static Vector3 Estimate(string[] banks, int n, bool boost, bool starter, float s, float taller = 0)
    {
        var bank = 0.25f + 0.75f + 0.25f + (boost ? 0.25f : 0) + taller; // (taller: how much a pressed fuel system and booster stand above the catalogue ones, at scale 1: Taller)
        float Side(string d) => banks.Contains(d) ? bank : 0.45f;
        var w = Side("+x") + Side("-x");
        var h = Side("+y") + Side("-y");
        if (starter && banks.Length == 4)
            h = MathF.Max(h, bank + 0.57f);
        return new Vector3(n * 0.5f + 0.4f, w, h) * s;
    }

    /// <summary>How much further a bank's pressed piece stands out than the catalogue one it stands in for (m: its blank's length, which runs out from the head, over the catalogue preset's; 0 for the catalogue piece).</summary>
    private static float Taller(ConstructPartDef d, string catalogue)
    {
        if (Part(catalogue) is not { } c || c == d || PresetKey.Match(d.Name) is not { Success: true } m || PressTable.Current.PresetKeyOf(c) is not { } ck || PresetKey.Match(ck) is not { Success: true } cm)
            return 0f;
        return MathF.Max(0, (int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture) - int.Parse(cm.Groups[6].Value, CultureInfo.InvariantCulture)) / 100f);
    }

    /// <summary>Lower is better: the power's miss (short counts three times over), the torque's and rpm's, then mass and bulk, and a seeded hair to part ties.</summary>
    private static EnginePlan Score(EngineRequest r, int size, EngineLayout layout, int n, string cyl, string fuel, string? boost, float kw, float mass, Vector3 dims,
        float target, float peakW, float peakRpm, int tier, int usedTier, float frail = 0)
    {
        var why = "";
        var tq = kw * 1000 / (peakRpm / ConstructDrive.RpmPerRadS); // (at the plan's own peak: a long stroke's is lower, so its torque is more by its square; peakW, the size's, only scales the target)
        var miss = r.PowerKw > 0 ? (kw < target ? 3 * (target - kw) / target : (kw - target) / target)
            : tq < r.TorqueNm ? 3 * (r.TorqueNm - tq) / r.TorqueNm : (tq - r.TorqueNm) / r.TorqueNm; // (torque alone: its miss)
        var score = miss;
        if (r.TorqueNm > 0 && r.PowerKw > 0)
            score += tq < r.TorqueNm ? 3 * (r.TorqueNm - tq) / r.TorqueNm : (tq - r.TorqueNm) / r.TorqueNm;
        if (r.PeakRpm > 0)
            score += 0.5f * MathF.Abs(MathF.Log(peakRpm / r.PeakRpm));
        score += 0.15f * mass / MathF.Max(1, target * 2.5f); // (about 2.5 kg a kW is a plain small engine's)
        score += 0.02f * (dims.X * dims.Y * dims.Z) / MathF.Max(1e-3f, MathF.Pow(target / 100f, 1f)); // (bulk, lightly)
        score += 0.03f * (tier - usedTier); // (a tier's engine leans on its own pieces)
        score += 0.1f * frail; // (a bored cylinder's thinner wall is easier to damage: frail is the health it gives up against the plain one, 0 to 1)
        score += 0.01f * Hash01(r.Seed, size, (int)layout, n, cyl.Length * 31 + fuel.Length * 7 + (boost?.Length ?? 0));
        var feasible = true;
        if (r.Fit.X > 0 && dims.X > r.Fit.X || r.Fit.Y > 0 && dims.Y > r.Fit.Y || r.Fit.Z > 0 && dims.Z > r.Fit.Z)
        {
            feasible = false;
            var over = MathF.Max(r.Fit.X > 0 ? dims.X / r.Fit.X : 0, MathF.Max(r.Fit.Y > 0 ? dims.Y / r.Fit.Y : 0, r.Fit.Z > 0 ? dims.Z / r.Fit.Z : 0));
            score += 2 * over;
            why = string.Create(CultureInfo.InvariantCulture, $"it is {dims.X:0.00} x {dims.Y:0.00} x {dims.Z:0.00} m");
        }
        if (r.MaxMass > 0 && mass > r.MaxMass)
        {
            feasible = false;
            score += 2 * mass / r.MaxMass;
            why += (why.Length > 0 ? " and " : "") + string.Create(CultureInfo.InvariantCulture, $"it weighs {mass:0} kg");
        }
        return new EnginePlan(size, layout, n, cyl, fuel, boost, kw, mass, dims, score, feasible, why);
    }

    /// <summary>A stable hash in [0, 1) (HashCode is seeded per process: never for anything that must repeat).</summary>
    private static float Hash01(params int[] v)
    {
        var h = 2166136261u;
        foreach (var x in v)
        {
            h ^= (uint)x;
            h *= 16777619u;
            h ^= h >> 15;
        }
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    /// <summary>The engine nearest <paramref name="r"/>, built, or why not.</summary>
    public static EngineResult Request(EngineRequest r)
    {
        List<EnginePlan> plans;
        try
        {
            plans = Plans(r);
        }
        catch (ArgumentException ex)
        {
            return new EngineResult { Note = ex.Message };
        }
        if (plans.Count == 0)
            return new EngineResult { Note = "no modular engine pieces loaded for that size and tier" };
        // The seed's choice among the plans about as good as the best (within SeedBand of its score): a seed varies the layout and pieces, not the power.
        var near = plans.TakeWhile(p => p.Feasible == plans[0].Feasible && p.Score <= plans[0].Score + SeedBand).Take(8).ToList();
        if (near.Count > 1)
        {
            var pick = near[(int)(Hash01(r.Seed, 977) * near.Count)];
            plans.Remove(pick);
            plans.Insert(0, pick);
        }
        var refused = new List<string>();
        foreach (var p in plans.Take(MaxBuildTries))
        {
            var res = Build(r, p, out var why);
            if (res == null)
            {
                refused.Add($"{LayoutName(p.Layout, p.Cylinders)} {SizeWords[p.Size]}: {why}");
                continue;
            }
            var notes = new List<string>();
            if (!p.Feasible)
                notes.Add($"nothing the tier allows meets the limits; nearest: {p.Why}");
            if (r.Fit != Vector3.Zero && (r.Fit.X > 0 && res.Dims.X > r.Fit.X + 0.01f || r.Fit.Y > 0 && res.Dims.Y > r.Fit.Y + 0.01f || r.Fit.Z > 0 && res.Dims.Z > r.Fit.Z + 0.01f) && p.Feasible)
                notes.Add(string.Create(CultureInfo.InvariantCulture, $"built {res.Dims.X:0.00} x {res.Dims.Y:0.00} x {res.Dims.Z:0.00} m, over the box"));
            var target = r.PowerKw > 0 ? r.PowerKw : res.Kw * r.TorqueNm / MathF.Max(1, res.TorqueNm);
            if (res.Kw < target * 0.9f)
                notes.Add(string.Create(CultureInfo.InvariantCulture, $"{res.Kw:0} kW is short of {target:0} kW: the most within the limits{(r.Tier < ConstructParts.MaxTier ? " at this tier" : "")}"));
            if (!r.Fuel.Equals("petrol", StringComparison.OrdinalIgnoreCase) && !r.Fuel.Equals("fuel", StringComparison.OrdinalIgnoreCase))
                notes.Add($"modular engines burn fuel: built as petrol, not {r.Fuel}");
            if (r.Boost is EngineBoost.Turbo or EngineBoost.Supercharger && p.BoostPart == null)
                notes.Add($"no {r.Boost.ToString().ToLowerInvariant()} below tier {(r.Boost == EngineBoost.Turbo ? 4 : 3)}");
            if (refused.Count > 0)
                notes.Add("better plans wouldn't build: " + string.Join("; ", refused));
            res.Note = string.Join("; ", notes);
            res.Plan = p;
            return res;
        }
        return new EngineResult { Note = "no plan would build: " + string.Join("; ", refused), Plan = plans[0] };
    }

    /// <summary>Builds plan <paramref name="p"/> through the game's build rules; null with <paramref name="why"/> when a piece is refused.</summary>
    public static EngineResult? Build(EngineRequest r, EnginePlan p, out string why)
    {
        why = "";
        var sw = SizeWords[p.Size];
        var rules = EngineRules.Current;
        var layoutName = LayoutName(p.Layout, p.Cylinders);
        var name = r.Name.Length > 0 ? r.Name : $"{sw} {layoutName}{(p.Grade.Length > 0 ? " " + p.Grade : "")}{(p.Stroke > 1.01f ? " Long-stroke" : p.Stroke < 0.99f ? " Short-stroke" : "")}{(p.BoostPart != null ? Part(p.BoostPart)?.PieceSpool > 0 ? " Turbo" : " Supercharged" : "")}"; // (a turbo spools: a pressed one names its key, not "Turbo")
        var s = new BlueprintScript(name) { Group = "Engine Kit", Description = $"Made by the Engine Kit for {r}." };
        try
        {
            Assemble(s, r, p, null);
        }
        catch (VehicleKitException ex)
        {
            why = ex.Message;
            return null;
        }
        var live = s.Live;
        var res = new EngineResult { Name = name, Parts = live.PartCount, Cranks = p.Cranks, Text = s.Text() };
        if (live.ModCount < 1 || live.ModEngines?[0] is not { } e)
        {
            why = "the game doesn't see an engine in it";
            return null;
        }
        res.Built = true;
        res.Kw = e.Kw;
        res.Cylinders = e.Cylinders;
        res.Banks = e.Banks;
        res.BurnLps = e.Burn;
        e.Rpm(rules, out var idle, out var peak, out var max); // (read back from the live engine: its crank's scale and stroke, sized-parts design 6.1, 6.3)
        (res.IdleRpm, res.PeakRpm, res.MaxRpm) = (idle, peak, max);
        res.TorqueNm = e.Kw * 1000 / (res.PeakRpm / ConstructDrive.RpmPerRadS);
        res.Layout = LayoutName(p.Layout, e.Cylinders);
        res.Fault = e.Fault is EngineFault.None or EngineFault.NoTank ? e.Starved != 0 ? ConstructEngine.StarvedText(e) : "" : e.Fault.ToString(); // (a starved bank is no fault, but the plan meant to feed it whole: say so)
        res.Mass = 0;
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (var i = 0; i < live.PartCount; i++)
        {
            ref var part = ref live.Parts[i];
            res.Mass += part.D.Mass;
            foreach (var box in part.D.Boxes) // (every box, drawn ones too: the bounds are the solid ones only, and a flywheel's ring is drawn)
            {
                var c = part.Pos + Rot24.Apply(part.Rot, box.Center);
                var h = Rot24.ApplyHalf(part.Rot, box.Half);
                lo = Vector3.Min(lo, c - h);
                hi = Vector3.Max(hi, c + h);
            }
        }
        var ext = hi - lo;
        res.Dims = new Vector3(ext.Z, ext.X, ext.Y);
        res.Pieces = $"{p.Cylinder}, {p.FuelPart}{(p.BoostPart != null ? ", " + p.BoostPart : "")}";
        if (e.Banks != p.Banks)
        {
            why = $"the game counts {e.Banks} bank(s), the plan {p.Banks}";
            return null;
        }
        return res;
    }

    /// <summary>
    /// Puts plan <paramref name="p"/>'s pieces into <paramref name="s"/>: from a root crank at the origin with the flywheel on the last crank's front (<paramref name="fly"/>
    /// null), or out from a flywheel already placed (a vehicle's: <see cref="EngineKitVehicle"/>), the cranks back from its "crank" socket. Throws
    /// <see cref="VehicleKitException"/> when a piece is refused. Returns the flywheel.
    /// </summary>
    public static VPart Assemble(BlueprintScript s, EngineRequest r, EnginePlan p, VPart? fly)
    {
        var sw = SizeWords[p.Size];
        var banks = BankSockets(p.Layout);
        var crankName = p.Crank.Length > 0 ? p.Crank : $"{sw} Crank Section"; // (a bored engine's crank is pressed longer: Bores)
        var cranks = new VPart[p.Cranks];
        if (fly == null)
        {
            cranks[0] = s.Root(crankName, Vector3.Zero);
            for (var i = 1; i < p.Cranks; i++)
                cranks[i] = s.Onto(cranks[i - 1], "front", crankName, "back", 0);
        }
        else
        {
            cranks[^1] = s.Onto(fly, "crank", crankName, "front", 0);
            for (var i = p.Cranks - 2; i >= 0; i--)
                cranks[i] = s.Onto(cranks[i + 1], "back", crankName, "front", 0);
        }
        for (var b = 0; b < banks.Length; b++)
            for (var i = 0; i < p.Cranks; i++)
            {
                var cyl = s.Onto(cranks[i], banks[b], p.Cylinder, "base", 0);
                if (i == 0)
                {
                    var fuel = s.Onto(cyl, "head", p.FuelPart, "base", 0);
                    if (p.BoostPart != null)
                        s.Onto(fuel, "top", p.BoostPart, "base", 0);
                }
                else if (r.Exhausts)
                    s.Onto(cyl, "head", $"{sw} Exhaust Outlet", "base", 0);
            }
        fly ??= s.Onto(cranks[^1], "front", $"{sw} Flywheel", "crank", 0);
        if (EngineRules.Current.StarterAt(EngineRules.Current.Scale[p.Size], p.Stroke)) // (by its displacement: the crank's scale and the stroke, as the engine's own NeedsStarter)
            Starter(s, sw, cranks, fly, banks);
        if (r.Paint.Length > 0)
        {
            foreach (var c in cranks)
                s.Paint(c, r.Paint);
            s.Paint(fly, r.Paint);
        }
        return fly;
    }

    /// <summary>A starter on a crank side no bank uses, else on the flywheel's side.</summary>
    private static void Starter(BlueprintScript s, string sw, VPart[] cranks, VPart fly, string[] banks)
    {
        var name = $"{sw} Starter Motor";
        foreach (var side in new[] { "-y", "-x", "+x", "+y" })
            if (!banks.Contains(side))
                foreach (var c in cranks)
                    try
                    {
                        s.Onto(c, side, name, "base", 0);
                        return;
                    }
                    catch (VehicleKitException)
                    {
                    }
        foreach (var side in new[] { "-y", "-x", "+x", "+y" })
            try
            {
                s.Onto(fly, side, name, "base", 0);
                return;
            }
            catch (VehicleKitException)
            {
            }
        throw new VehicleKitException($"no room for the {name}");
    }
}
