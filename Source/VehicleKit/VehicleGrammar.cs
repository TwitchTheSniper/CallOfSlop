using System.Numerics;
using System.Text;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// Which kit made a vehicle (docs/vehiclekit/VEHICLEKIT.md, "Shape grammar"). Kit 1 is the shapes as whole generators; kit 2 builds the shapes that have a recipe
/// (<see cref="ShapeGrammar.Recipes"/>) from modules. A committed seed stays on the kit it was committed with (<see cref="Frozen"/>), so its file never changes;
/// every other seed, the dynamic spawn's included, is made by <see cref="Current"/>. The kit is in the blueprint's text (a comment line, kit 2 on) and in the spawn log.
/// Kit 3 (<see cref="Layered"/>) is kit 2's grammar with its variety in layers (<see cref="VehicleLayers"/>): a kit-2 seed means another vehicle under kit 3, which is
/// fine because no kit-2 seed was ever committed and a saved world holds built constructs, not seeds; <c>--kit 2</c> still makes kit 2's.
/// </summary>
public static class VehicleKitVersion
{
    /// <remarks>
    /// Kit 4 (<see cref="Roles"/>) is kit 3 with a role and a tier (VehicleRoles.cs). Kit 5 (<see cref="Lofted"/>, current since 2026-11-07, the user's decision) is kit 3,
    /// or kit 4 for a role, with its rooms lofted in the style's body language (VehicleBodyLanguage.cs): every seed asked for by shape is kit 5, and so is every role's,
    /// family's, convoy's and context's vehicle; <c>--kit 3</c> and <c>--kit 4</c> still make the older kits, to measure against.
    /// </remarks>
    public const int Legacy = 1, Grammar = 2, Layered = 3, Roles = 4, Lofted = 5, Current = Lofted;

    /// <summary>The command line's <c>--kit N</c>: every seed by that kit (to measure one against the other). Null: each seed's own.</summary>
    public static int? Force;

    /// <summary>
    /// Seeds made by kit 1 for good, by generator: none since 2026-11-07, when every committed faction vehicle was made again by kit 5 (the user: continuity isn't
    /// required). A kit that changes a committed seed lists the seeds committed before it here with their kit's number, or the shapes are committed again.
    /// Only shapes with a recipe need listing: a shape without one is kit 1 whatever the seed (the Technical, the Fuel Trailer, the Tanker Rig; the Double Decker has had a recipe since body styles phase 5).
    /// </summary>
    public static readonly Dictionary<string, int[]> Frozen = new(StringComparer.Ordinal);

    /// <summary>The kit a generator's seed is made by: <see cref="Force"/>, else kit 1 for a frozen seed or a shape with no recipe, else <see cref="Current"/>.</summary>
    public static int For(IVehicleGenerator g, int seed) =>
        Force ?? (ShapeGrammar.RecipeOf(g) == null || Frozen.TryGetValue(g.Name, out var s) && Array.IndexOf(s, seed) >= 0 ? Legacy : Current);
}

/// <summary>
/// One module a grammar vehicle is assembled from: its family ("nose", "cab", "body", "rear", "under", "flank", "sides"), its name, the style parts it needs (any one
/// the style lists in <see cref="Slot"/>; none: always possible), and its connection rules as tags: what it <see cref="Needs"/> from the rest of the vehicle, what it
/// <see cref="Provides"/>, what it can't stand beside (<see cref="Excludes"/>), and the gear recipes it allows (null: any).
/// </summary>
public sealed record VehicleModule(string Family, string Name, string Label)
{
    public string Slot { get; init; } = "";
    public string[] Parts { get; init; } = [];
    public string[] Needs { get; init; } = [];
    public string[] Provides { get; init; } = [];
    public string[] Excludes { get; init; } = [];
    public string[]? Gears { get; init; }

    /// <summary>The default weight before the style's: a part-backed module weighs as many entries as the style's slot has of its parts.</summary>
    public double Weight { get; init; } = 1;

    /// <summary>Whether the style can make it (one of its parts is in the slot) and the tags so far allow it.</summary>
    public bool Fits(FactionStyle st, ISet<string> tags) =>
        (Parts.Length == 0 || st.Parts.TryGetValue(Slot, out var list) && list.Any(Parts.Contains)) && Needs.All(tags.Contains) && !Excludes.Any(tags.Contains);

    /// <summary>The weight in this style: the part entries it has (or <see cref="Weight"/>), times the style's factor.</summary>
    public double WeightIn(FactionStyle st) =>
        st.ModuleWeight(Family, Name, Parts.Length == 0 ? Weight : Weight * (st.Parts.TryGetValue(Slot, out var list) ? list.Count(Parts.Contains) : 0));

    /// <summary>The style's parts for this module, as often as the style lists them.</summary>
    public string[] PartsIn(FactionStyle st) => st.Parts.TryGetValue(Slot, out var list) ? list.Where(Parts.Contains).ToArray() : [];
}

/// <summary>
/// A body segment of a recipe: a kind ("room", "bed", "gun bed", "hutch", "gun bay", "tank") and its length range in metres. Rooms are 3.5 m and up in whole
/// metres (a door bay is 1.5 m), beds go in half metres, a tank is its barrel (2 or 3 m), a hutch bed 4 m and a gun bay 2 m.
/// </summary>
public sealed record BodySegment(string Kind, float Min, float Max);

/// <summary>One way a shape's body may run, front to back, and how likely it is.</summary>
public sealed record BodyPattern(double Weight, params BodySegment[] Segments);

/// <summary>
/// A shape as a recipe over module families (kit 2): its body patterns, its rears, the gear recipes it allows, how many guns it carries, and the names its parts go by.
/// "Box Truck" is a walk-in hull first (sometimes with an open bed behind it, or a gun bed in front), so a Box Truck stays a box truck, in any of many outlines.
/// </summary>
public sealed record ShapeRecipe(string Shape, string RoomTag, string Noun, BodyPattern[] Bodies, (string Rear, double Weight)[] Rears, int MinGuns, int MaxGuns, string[]? Gears = null)
{
    /// <summary>Phase 5: an upper storey every seed of the shape has where its room allows ("saloon": the Double Decker); null: the style's roll.</summary>
    public string? Upper { get; init; }

    /// <summary>Phase 5: the share of the room's middle the recipe's own upper storey spans (null: the style's): a deck over all of it reads as a box from the side.</summary>
    public (float Min, float Max)? UpperShare { get; init; }
}

/// <summary>
/// The shape grammar (kit 2; docs/vehiclekit/VEHICLEKIT.md, "Shape grammar"): a heavy vehicle assembled from tagged modules (nose, cab, body segments, rear, underside,
/// flanks, bed sides, guns), chosen by the seed under the style's weights, with connection rules that let through only what builds and keeps to the Silhouette rules:
/// separate masses (a cab, then a room, a bed or a tank, each its own height), a roofline that steps (every room changes height at least once), wheels in the open
/// where nothing hangs over them, and a room only where there is a crew or loot to hide. The module bodies are the kit-1 generators' own code, put where the plan says.
/// </summary>
public static class ShapeGrammar
{
    private const float Under = 0.625f, Edge = 1.125f, Front = 0.2f; // (as HeavyShape: a floor on two-layer sills, the odd eighth nearest 2.25 m wide; a body starts behind the cab)
    private const int Attempts = 4;
    private const float MinStability = 0.63f; // (HeavyShape's checks want 0.6; a little over, as the built body settles on its springs)

    private static readonly string[] FortGears = ["leaf", "hydraulic", "tandem", "six", "stilt"];
    private static readonly string[] BusGears = ["leaf", "hydraulic", "swing", "wishbone", "tandem", "six", "dually", "crawler"]; // (no stilts: a long tall hull on them rolls)
    private static readonly string[] CarGears = ["swing", "wishbone", "leaf", "hydraulic"]; // (phase 8a: a car rides low, on two axles)
    private static readonly string[] BuggyGears = ["wishbone", "swing", "hydraulic"]; // (phase 8b: a buggy's wheels thrown out on arms, or a long hydraulic travel)
    private static readonly string[] MonsterGears = ["monster"]; // (phase 8b: huge wheels on heavy pogo struts; no other recipe takes them)
    private static readonly string[] GiantGears = ["leaf", "hydraulic", "tandem", "six", "dually"]; // (phase 8b: a giant body on a wide differential, often two rear axles)

    /// <summary>Phase 8b: gear recipes only a recipe naming them takes (a recipe with no gear list takes every other one of its style's).</summary>
    public static readonly string[] SpecialGears = ["monster"];
    private static readonly string[] TechGears = ["leaf", "swing", "wishbone", "hydraulic", "dually"]; // (phase 9b: a technical rides low on two axles, as kit 1's did)
    private static readonly string[] TankGears = ["leaf", "hydraulic", "tandem", "dually", "six", "stilt"];

    private static BodySegment Room(int minBays, int maxBays) => new("room", 3.5f + minBays, 3.5f + maxBays);
    internal static BodySegment Bed(float min, float max) => new("bed", min, max);
    private static BodySegment GunBed(float min, float max) => new("gun bed", min, max);
    private static readonly BodySegment Hutch = new("hutch", 4f, 4f), Gap = new("gap", 0.5f, 1f);
    internal static readonly BodySegment GunBay = new("gun bay", 2f, 2f);
    private static BodySegment Tank(float min, float max) => new("tank", min, max);
    internal static BodySegment Boot(float min, float max) => new("boot", min, max);
    internal static readonly BodySegment Row = new("gap", 1f, 1f); // (phase 8a part 2: a car's second row of seats, under its crew cabin)

    /// <summary>The recipes, by shape name: kit 1's shapes as module families. A shape not here (the Technical, the trailers) is made by its generator.</summary>
    public static readonly Dictionary<string, ShapeRecipe> Recipes = new(StringComparer.Ordinal)
    {
        ["Box Truck"] = new("Box Truck", "box", "box truck",
            [new(3, Room(0, 1)), new(1.5, Gap, Room(0, 1)), new(2, Room(0, 1), Bed(1.5f, 2f)), new(1, GunBed(2f, 2f), Room(0, 0)), new(1.5, Room(0, 0), GunBed(2f, 2.5f))],
            [("porch", 3), ("porch hitch", 1), ("gun deck", 1.5), ("hitch", 1), ("bare", 1)], 1, 3),
        ["War Bus"] = new("War Bus", "bus", "war bus",
            [new(4, Room(2, 4)), new(1.5, Gap, Room(2, 3)), new(1, Room(2, 3), Bed(1.5f, 2f)), new(1, Room(2, 3), GunBed(2f, 2.5f))],
            [("porch", 3), ("porch hitch", 1), ("gun deck", 2), ("hitch", 1)], 2, 4, BusGears),
        ["Flatbed Fort"] = new("Flatbed Fort", "bunker", "flatbed fort",
            [new(3, GunBed(2f, 3f), Room(0, 0)), new(2, Room(0, 0), GunBed(2f, 3f)), new(1, GunBed(2f, 2.5f), Room(0, 1))],
            [("bare", 2), ("porch", 2), ("porch hitch", 1), ("hitch", 1)], 2, 3, FortGears),
        ["Gun Wagon"] = new("Gun Wagon", "crew", "gun wagon",
            [new(3, Hutch), new(1, GunBay, GunBed(2f, 3f)), new(1, Room(0, 0), GunBed(2f, 3f))],
            [("bare", 2), ("porch", 2), ("porch hitch", 1), ("hitch", 1)], 1, 2),
        // (phase 5: the British double decker, made by the grammar since its upper deck can be lofted: an open-topped deck over a long room, as kit 1's was, behind a
        // bulwark in the bus's own lines; a stern gun deck more often than not, since the deck takes the roof a turret would stand on)
        ["Double Decker"] = new("Double Decker", "bus", "double-decker bus",
            [new(1, Room(3, 4))],
            [("gun deck", 3), ("porch", 1.5), ("porch hitch", 0.5)], 2, 3, BusGears) { Upper = "deck", UpperShare = (0.45f, 0.75f) },
        // (phase 8a: a car, the archetypes' muscle cars, supercars and European cars: a lofted cabin in the archetype's language, two seats or four (a crew cabin
        // over a second row), and a boot behind it: a lofted tail deck over its fuel tank, the gun on a pedestal between the cabin and the boot, its seat facing it)
        ["Car"] = new("Car", "car", "car", [new(1, Boot(2.25f, 2.75f)), new(1.5, Row, Boot(2.5f, 3f))], [("bare", 2), ("hitch", 1)], 1, 1, CarGears),
        // (phase 8b, section 18: the archetype library's part 2. A buggy: a caged tub, a gun bay behind it, the drum on a porch. A monster truck: a short cab, a gun
        // behind it and a bed, high over huge wheels. A wagon: a room (a war wagon's canvas top, a carriage's bowed body). A giant truck: a long room at the giant scale class.
        // A concept car: a cabin, a hood and a boot, never a second row.)
        ["Buggy"] = new("Buggy", "tub", "gun buggy", [new(1, GunBay)], [("porch", 2), ("porch hitch", 1)], 1, 1, BuggyGears),
        ["Monster Truck"] = new("Monster Truck", "truck", "monster truck", [new(2, GunBay, Bed(1.5f, 2f)), new(1, GunBay)], [("bare", 2), ("hitch", 1), ("porch", 1)], 1, 1, MonsterGears),
        ["Wagon"] = new("Wagon", "wagon", "wagon", [new(2, Room(0, 1)), new(1, Room(0, 0), Bed(1.5f, 2f))], [("porch", 2), ("porch hitch", 1), ("bare", 1), ("hitch", 0.5)], 1, 2),
        ["Giant Truck"] = new("Giant Truck", "hauler", "giant truck", [new(2, Room(2, 4)), new(1, Room(2, 3), GunBed(2f, 2.5f)), new(1, Gap, Room(2, 3))],
            [("porch", 2), ("gun deck", 2), ("porch hitch", 1), ("hitch", 1)], 2, 4, GiantGears),
        ["Concept"] = new("Concept", "car", "concept car", [new(1, Boot(2.25f, 3f))], [("bare", 2), ("hitch", 0.5)], 1, 1, CarGears),
        // (phase 9b: kit 1's Technical as a recipe: a lofted cab and hood in the style's language, a gun behind the cab on its own sill and a short open bed (the drum
        // in it) or the drum on a porch, as the kit-1 stepside, ute and sand rail were; low gear, never stilts. Its generator keeps its own checks)
        ["Technical"] = new("Technical", "truck", "technical", [new(2, GunBay, Bed(1.5f, 2f)), new(1, GunBay)], [("porch", 1), ("bare", 2), ("hitch", 0.5)], 1, 1, TechGears),
        ["Scrap Tanker"] = new("Scrap Tanker", "crew", "scrap tanker",
            [new(3, GunBay, Tank(2f, 3f)), new(1, GunBay, Tank(2f, 2f), Bed(1.5f, 2f)), new(1, Room(0, 0), Tank(2f, 3f))],
            [("bare", 3), ("hitch", 1)], 1, 2, TankGears),
    };

    /// <summary>Every module, by family. A nose or a cab is made only of the style's own parts; the rest are shapes of the frame and the body.</summary>
    public static readonly VehicleModule[] Catalogue =
    [
        new("cab", "caged", "a caged cab") { Slot = "heavyCab", Parts = ["Roll Cage", "Mesh Guard Cage", "Roll Bar"] },
        new("cab", "enclosed", "an enclosed cab") { Slot = "heavyCab", Parts = ["Tin Roof Cab", "Chop Top Cab", "Windscreen Frame"] },
        new("cab", "armoured", "an armoured slit cab") { Slot = "heavyCab", Parts = ["Slit Cab"] },
        new("cab", "cab-over", "a cab-over") { Slot = "heavyCab", Parts = ["Cab Over Shell"], Provides = ["cabover"] },
        new("cab", "crew", "a crew cab") { Needs = ["lofted", "gap first"] }, // (phase 3: lofted cabs only; a second row of seats over a metre of bare rails)
        new("cab", "merged", "a cab merged into the room") { Needs = ["lofted", "room first"], Provides = ["cabover"] }, // (a capsule whose back is the room's front: a bus front)
        new("nose", "long hood", "a long hood") { Slot = "hood", Parts = ["Long Hood", "Bull Hood"], Excludes = ["cabover"] },
        new("nose", "short hood", "a short hood") { Slot = "hood", Parts = ["Snub Hood", "Slab Nose"], Excludes = ["cabover"] },
        new("nose", "plated hood", "a plated hood and ram bars") { Slot = "hood", Parts = ["plates"], Excludes = ["cabover"] },
        new("nose", "ram", "a ram on the hood") { Slot = "nose", Parts = ["Ram Plow", "Bull Bar"], Excludes = ["cabover"] },
        new("nose", "flat", "a flat nose") { Needs = ["cabover"] },
        new("body", "room", "a walk-in hull with loot"),
        new("body", "bed", "an open bed"),
        new("body", "gun bed", "a gun bed"),
        new("body", "hutch", "an ammo hutch and a stern gun on a flat deck"),
        new("body", "gun bay", "a gun behind the cab"),
        new("body", "gap", "bare rails behind the cab"),
        new("body", "boot", "a boot behind the cabin, the gun on a pedestal ahead of it"),
        new("body", "tank", "a big round tank of fuel") { Gears = TankGears },
        new("rear", "porch", "a drum on the rear porch") { Excludes = ["fuel"], Provides = ["fuel"] },
        new("rear", "porch hitch", "a drum on the rear porch over a tow hitch") { Excludes = ["fuel"], Provides = ["fuel"] },
        new("rear", "gun deck", "a stern gun deck") { Needs = ["room last"], Excludes = ["hutch", "bed last"] }, // (its drum beside the gun when nothing else carries one)
        new("rear", "hitch", "a tow hitch") { Needs = ["fuel or drum"] },
        new("rear", "bare", "a bare tail") { Needs = ["fuel or drum"] },
        new("under", "stilts", "on stilts") { Weight = 0.5 },
        new("under", "tub", "on a closed tub") { Needs = ["high floor"], Excludes = ["six", "tag axle"] }, // (a tub runs along the rails: a second axle's beams break them)
        new("under", "skirted", "skirted") { Needs = ["high floor", "room"] },
        new("flank", "bare", "flat flanks") { Needs = ["room"] },
        new("flank", "panniers", "panniers") { Needs = ["room"], Weight = 2 },
        new("flank", "loaded", "panniers all along") { Needs = ["room"] },
        new("roof", "none", "") { Needs = ["room"], Weight = 2 },
        new("roof", "rack", "a railed rack on the roof") { Needs = ["long room"] }, // (two flat bays side by side need a room with a metre bay)
        new("sides", "walls", "walled sides") { Needs = ["bed"], Gears = FortGears, Weight = 2 },
        new("sides", "rails", "tube rails") { Needs = ["bed"] },
        new("sides", "open", "open sides") { Needs = ["bed"] },
    ];

    /// <summary>A generator's recipe, by its shape's name; null for a shape the grammar doesn't make.</summary>
    public static ShapeRecipe? RecipeOf(IVehicleGenerator g) => g is VehicleShape s && Recipes.TryGetValue(s.Shape, out var r) ? r : null;

    /// <summary>
    /// A seed by the grammar. A plan that the game refuses as it is built (a part with nowhere to go) is planned again from the next fork of the seed, up to
    /// <see cref="Attempts"/> times, so a seed fails only when every plan does; the same seed always takes the same attempts.
    /// </summary>
    public static Vehicle Make(IVehicleGenerator g, int seed, ShapeRecipe recipe, int kit = VehicleKitVersion.Current, VehicleLayers layers = default, RoleAsk? role = null)
    {
        if (role != null && kit >= VehicleKitVersion.Roles)
            return MakeRole(g, seed, recipe, kit, layers, role);
        Vehicle? kept = null; // (the best plan the identity check turned down: VehicleIdentity.Gate)
        for (var attempt = 0; ; attempt++)
        {
            VehicleGenJob.Yield(); // (a plan again starts a world of its own: a span of its own)
            var v = New(g, seed, kit, layers);
            VehicleGenJob.Yield();
            try
            {
                var plan = Plan(v, recipe, attempt);
                VehicleGenJob.Yield();
                Build(v, plan);
                v.DriveGate(VehicleRegistry.Towed(g)); // (built from its text, settled, level and moving on a throttle pulse, else the next plan: VehicleDriveCheck.cs)
                if (VehicleIdentity.Gate(v, attempt, Attempts, ref kept) is { } done)
                    return done;
            }
            catch (Exception ex) when (ex is VehicleKitException or ConstructPartException && attempt + 1 < Attempts)
            {
                // (the next plan)
            }
            catch (Exception ex) when (ex is VehicleKitException or ConstructPartException && kept != null)
            {
                return kept; // (every later plan failed: the best that built)
            }
        }
    }

    /// <summary>
    /// Kit 4: a seed for a role (VehicleRoles.cs). The plan leans to the role (<see cref="Plan"/> reads <see cref="Vehicle.Role"/>), the style's parts weigh by the role's
    /// armour and the tier (<see cref="FactionStyle.Weigh"/>), and each plan that builds is measured against the role and scored for its faction; one that misses
    /// either is planned again, up to <see cref="VehicleRoleMake.Plans"/> built plans (and <see cref="Attempts"/> more for plans the game refuses), and the best is kept:
    /// fewest role misses, then fewest marks missing, then the identity score.
    /// </summary>
    private static Vehicle MakeRole(IVehicleGenerator g, int seed, ShapeRecipe recipe, int kit, VehicleLayers layers, RoleAsk role)
    {
        Vehicle? best = null;
        (int, int, int, double) bestRank = default;
        Exception? last = null;
        var built = 0;
        var (weigh, scheme) = (FactionStyle.Weigh, FactionStyle.Scheme);
        (FactionStyle.Weigh, FactionStyle.Scheme) = (role.Weigh, role.Scheme); // (a convoy's shared paint, VehicleConvoys.cs; null for every other vehicle)
        try
        {
            for (var attempt = 0; attempt < Attempts + VehicleRoleMake.Plans - 1 && built < VehicleRoleMake.Plans; attempt++)
            {
                VehicleGenJob.Yield();
                var v = New(g, seed, kit, layers, role);
                VehicleGenJob.Yield();
                try
                {
                    var plan = Plan(v, recipe, attempt);
                    VehicleGenJob.Yield();
                    Build(v, plan);
                    if (role.Family && v.Stability() is var stable && stable < (g.Checks ?? new VehicleCheckOptions()).MinStability + 0.02f)
                        throw new VehicleKitException($"stability {stable:0.00}: under the check's"); // (a family's mark isn't planned again for its role, so it is held to the check's stability as it builds: VehicleFamilies.cs)
                    v.DriveGate(VehicleRegistry.Towed(g));
                }
                catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
                {
                    last = ex; // (the next plan)
                    continue;
                }
                built++;
                v.PlanAttempt = attempt;
                VehicleGenJob.Yield();
                var id = VehicleIdentity.Of(v);
                v.Identity = id;
                VehicleGenJob.Yield();
                v.Fit = VehicleRoles.Measure(v, role, id);
                v.RolePlans = built - 1;
                var rank = VehicleRoleMake.Rank(v, id);
                if (best == null || rank.CompareTo(bestRank) < 0)
                    (best, bestRank) = (v, rank);
                if (rank.Item1 == 0 && rank.Item2 == 0 && rank.Item3 == 0 || role.Family) // (a family's mark keeps its first plan that builds: its marks share one body)
                    break;
                if (built < VehicleRoleMake.Plans)
                {
                    Interlocked.Increment(ref VehicleRoleMake.Replanned);
                    Console.WriteLine($"[vehicles] {v.Name}: plan {attempt + 1} {(v.Fit.Met ? $"reads {id.Total:0.00} as {id.Style}{(id.Missing.Count > 0 ? " (no " + string.Join(", no ", id.Missing) + ")" : "")}" : "misses the " + role.Role.Name + ": " + string.Join("; ", v.Fit.Misses))}; planning again");
                }
            }
        }
        finally
        {
            (FactionStyle.Weigh, FactionStyle.Scheme) = (weigh, scheme);
        }
        if (best == null)
            throw last ?? new VehicleKitException($"no plan of the {g.Name} built for a {role.Role.Name}");
        best.RolePlans = built - 1; // (how many it took in all, whichever was kept)
        VehicleRoleMake.Note(best, role, recipe.Shape);
        return best;
    }

    /// <summary>The first plan of a seed, nothing built: <see cref="VehicleGenJob.Warm"/> compiles the planner this way. False when it doesn't plan.</summary>
    internal static bool PlanOnly(IVehicleGenerator g, int seed)
    {
        if (RecipeOf(g) is not { } recipe)
            return false;
        try
        {
            Plan(New(g, seed, VehicleKitVersion.Current, default), recipe, 0);
            return true;
        }
        catch (VehicleKitException)
        {
            return false;
        }
    }

    /// <summary>A plan's vehicle: kit 2's plain, kit 3's with its layers and its wear started.</summary>
    private static Vehicle New(IVehicleGenerator g, int seed, int kit, VehicleLayers layers, RoleAsk? role = null)
    {
        if (kit < VehicleKitVersion.Layered)
            return new Vehicle(VehicleRegistry.BlueprintName(g, seed), g.Style, seed) { Kit = kit };
        var v = new Vehicle(VehicleRegistry.BlueprintName(g, seed) + layers.Suffix + (role?.Suffix ?? ""), g.Name, g.Style, seed) { Kit = kit, Role = role };
        v.UseLayers(layers);
        v.StartWear(LayerTables.WearOf(g.Style));
        if (kit >= VehicleKitVersion.Lofted)
        {
            if (g is VehicleShape shape)
                v.UseArchetype(BodyArchetype.Roll(v, shape.Shape), shape.Shape); // (phase 8a: an archetype laid over the style's body rule, on a fork of its own)
            v.Look = BodyLook.Roll(v); // (kit 5: the body layer's "language" fork, so nothing else of a seed moves)
            v.Modules.Add("language " + v.Look.Label);
            if (!Vehicle.FixedCabs)
                v.CabLook = new CabLook(v, v.Look); // (phase 3: the cab and hood in the body's language, on forks of their own)
        }
        return v;
    }

    // ---- The plan ------------------------------------------------------------------------------------------------

    /// <summary>A segment as planned: where it runs, and what stands on it.</summary>
    private sealed class Seg
    {
        public string Kind = "";
        public float Z0, Z1;
        public string[] Bays = [];
        public bool Gun, Aft, Drum;
        public float GunZ, TankZ;
        public WeaponOption? Weapon;
        public int RoofGuns, SeatCount;
        public int SideBay = -1;
        public float SideSide = -1;
        public List<int> RoofBays = new();
        public float Len => Z1 - Z0;
    }

    private sealed class PlanData
    {
        public ShapeRecipe Recipe = null!;
        public List<Seg> Segs = new();
        public Gear Gear = null!;
        public float Rails, Tail, RearAxle, End;
        public VehicleModule Cab = null!, Nose = null!, Rear = null!, UnderKind = null!;
        public VehicleModule? Flank, Sides, Roof;
        public string CabPart = "";
        public string? HoodPart;
        public string[]? NoseParts;
        public WeaponOption? RearWeapon;
        public float RearGunZ;
        public bool RearDrum;
        public int Attempt;
        public readonly HashSet<string> Tags = new(StringComparer.Ordinal);

        /// <summary>Kit 3: the chassis layer's roll, the body's ranges and its dice for the room's bays; null in kit 2.</summary>
        public ChassisPlan? Chassis;
        public BodyRange? Body;
        public Dice? BayDice;

        /// <summary>Phase 5: the upper storey the plan asks for over its room ("saloon", "deck"; null none), and its dice.</summary>
        public string? UpperKind;
        public Dice? UpperDice;

        /// <summary>Where the axles stand behind the front one: a sill or a gun can't go there (the rails break for a differential).</summary>
        public readonly List<float> Stations = new();
    }

    private static VehicleModule Module(string family, string name) => Catalogue.First(m => m.Family == family && m.Name == name);

    /// <summary>One of a family's modules that fit, by weight; null when none does.</summary>
    private static VehicleModule? Choose(Dice d, FactionStyle st, string family, ISet<string> tags, Func<VehicleModule, bool>? also = null, Func<VehicleModule, double>? weight = null)
    {
        var fits = Catalogue.Where(m => m.Family == family && m.Fits(st, tags) && (also?.Invoke(m) ?? true)).Select(m => (M: m, W: (weight?.Invoke(m) ?? 1) * m.WeightIn(st))).Where(x => x.W > 0).ToList();
        if (fits.Count == 0)
            return null;
        var roll = d.Next() * fits.Sum(x => x.W);
        foreach (var (m, w) in fits)
            if ((roll -= w) < 0)
                return m;
        return fits[^1].M;
    }

    private static T Weighted<T>(Dice d, IReadOnlyList<(T Item, double W)> items)
    {
        var total = items.Sum(x => x.W);
        var roll = d.Next() * total;
        foreach (var (item, w) in items)
            if ((roll -= w) < 0)
                return item;
        return items[^1].Item;
    }

    private static float Steps(Dice d, float min, float max, float step) => min + step * d.Int(0, (int)MathF.Round((max - min) / step));

    /// <summary>A length from <paramref name="min"/> to <paramref name="max"/> in steps, leaning long (<paramref name="bias"/> over 0) or short (under): kit 3's body ranges.</summary>
    private static float Steps(Dice d, float min, float max, float step, double bias)
    {
        var n = (int)MathF.Round((max - min) / step) + 1;
        var u = d.Next();
        u = bias >= 0 ? 1 - Math.Pow(1 - u, 1 + 2 * bias) : Math.Pow(u, 1 - 2 * bias);
        return min + step * Math.Min(n - 1, (int)(u * n));
    }

    private static PlanData Plan(Vehicle v, ShapeRecipe r, int attempt)
    {
        var st = v.Style;
        var layered = v.Kit >= VehicleKitVersion.Layered;
        var again = attempt == 0 ? "" : " " + attempt;
        var d = v.Dice.Fork("grammar" + again);
        // (kit 3: each decision forks from its layer's root, so a layer's seed moves only what that layer decides; kit 2: all of it from one fork, as it always did)
        Dice D(VehicleLayer l, string topic) => !layered ? d.Fork(topic) : l switch
        {
            VehicleLayer.Chassis => v.LayerDice(l).Fork("chassis" + again).Fork(topic),
            VehicleLayer.Kit => v.LayerDice(l).Fork("grammar kit" + again).Fork(topic),
            _ => d.Fork(topic),
        };
        var p = new PlanData { Recipe = r, Attempt = attempt, Body = layered ? VehicleContext.Body(LayerTables.BodyOf(st)) : null };
        var tags = p.Tags;
        var role = v.Role; // (kit 4: the role leans each choice; null: kit 3's choices exactly)
        VehicleModule? Ch(Dice dd, string family, Func<VehicleModule, bool>? also = null, Func<VehicleModule, double>? weight = null) =>
            role == null && !VehicleContext.LeansModules ? Choose(dd, st, family, tags, also, weight)
                : Choose(dd, st, family, tags, also, m => (weight?.Invoke(m) ?? 1) * (role?.Module(family, m.Name) ?? 1) * VehicleContext.Module(family, m.Name)); // (a role's and a spawn context's leans: the same one roll)

        // The body: a pattern, weighted by the style's liking for each kind of segment, then lengths.
        var bodies = v.Archetype?.Bodies is { Length: > 0 } own && v.Archetype.Shapes.Contains(r.Shape) ? own : r.Bodies; // (phase 9a: an archetype's own body plans, a faction's car outline)
        var pattern = Weighted(D(VehicleLayer.Body, "body"), bodies.Select(b => (b, b.Weight * b.Segments.Aggregate(1.0, (w, s) => w * st.ModuleWeight("body", s.Kind, 1)) * (role?.Body(b) ?? 1) * CarRows(v, r, b))).ToList());
        var z = Front;
        var ld = D(VehicleLayer.Body, "lengths");
        foreach (var s in pattern.Segments)
        {
            var step = s.Kind is "room" or "tank" ? 1f : 0.5f;
            var len = p.Body is { } body ? Steps(ld, s.Min, s.Max, step, VehicleContext.LengthBias(role?.LengthBias(body.LengthBias) ?? body.LengthBias)) : Steps(ld, s.Min, s.Max, step);
            var seg = new Seg { Kind = s.Kind, Z0 = z, Z1 = z + len };
            p.Segs.Add(seg);
            tags.Add(s.Kind is "gun bed" ? "bed" : s.Kind);
            z += len;
        }
        if (p.Segs.Count(s => s.Kind == "room") > 1 || p.Segs.Count(s => s.Kind is "bed" or "gun bed") > 1)
            throw new VehicleKitException("a recipe has two rooms or two beds"); // (a recipe's mistake: panels are tagged by kind)
        p.End = z;
        if (p.Segs.Any(s => s.Kind == "room" && s.Len >= 4.5f))
            tags.Add("long room");
        tags.Add(p.Segs[^1].Kind is "room" ? "room last" : p.Segs[^1].Kind is "bed" or "gun bed" ? "bed last" : p.Segs[^1].Kind + " last");
        if (tags.Contains("tank") || tags.Contains("boot"))
            tags.Add("fuel"); // (a boot carries its own tank under its lid)
        if (tags.Contains("fuel") || p.Segs.Any(s => s.Kind is "hutch" || s.Kind is "bed" or "gun bed" && s.Len >= 1.5f))
            tags.Add("fuel or drum"); // (a bare tail needs fuel carried ahead of it)

        // Bed sides before the gear: walls hang under the floor's edge, so they rule out some recipes.
        if (tags.Contains("bed"))
            p.Sides = Ch(D(VehicleLayer.Body, "sides"), "sides", m => m.Gears == null || r.Gears == null || m.Gears.Intersect(r.Gears).Any()); // (phase 8b: no sides the recipe's gear can't carry)

        // The gear: what every chosen module allows.
        var allowed = (r.Gears ?? (st.HeavyGear.Select(x => x.Name).Where(x => !SpecialGears.Contains(x)).ToArray())).AsEnumerable();
        foreach (var gears in Catalogue.Where(m => m.Family == "body" && tags.Contains(m.Name) || m == p.Sides).Select(m => m.Gears).Where(x => x != null))
            allowed = allowed.Intersect(gears!);
        var only = allowed.ToArray();
        if (only.Length == 0)
            throw new VehicleKitException("no gear recipe fits every module");
        v.Heavy();
        if (!layered)
        {
            if (attempt == 0)
                p.Gear = v.PickGear(only);
            else // (a plan again rolls its gear again: one recipe may be what failed, a stilt's height say)
            {
                var fits = st.HeavyGear.Where(x => only.Contains(x.Name)).ToArray();
                p.Gear = d.Fork("gear").Pick(fits.Length > 0 ? fits : throw new VehicleKitException($"style {st.Name} has no gear named {string.Join(", ", only)}"));
                FactionStyle.Noted("gear", p.Gear.Name);
            }
            GearTags(p);
        }

        // Cab and nose: the style's own parts.
        if (v.CabLook != null)
        {
            // Phase 3: a lofted cab, its kind weighed by the style's body rule (BodyStyleRule.Cabs), whatever cab parts the style lists.
            tags.Add("lofted");
            if (p.Segs[0].Kind == "gap" && p.Segs[0].Len >= 1f)
                tags.Add("gap first");
            if (p.Segs[0].Kind == "room")
                tags.Add("room first");
            var rule = v.Rule;
            var fits = Catalogue.Where(m => m.Family == "cab" && m.Needs.All(tags.Contains) && !m.Excludes.Any(tags.Contains))
                .Select(m => (M: m, W: (CabLook.Force is { } only ? (m.Name == only ? 1 : 0) : rule.CabWeight(m.Name)) * (role?.Module("cab", m.Name) ?? 1) * VehicleContext.Module("cab", m.Name))).Where(x => x.W > 0).ToList();
            p.Cab = fits.Count > 0 ? Weighted(D(VehicleLayer.Body, "cab"), fits) : Module("cab", "enclosed");
            if (r.Shape == "Car" && tags.Contains("gap first") && p.Cab.Name != "cab-over")
                p.Cab = Module("cab", "crew"); // (phase 8a part 2: a four-seat car's cabin runs over its second row, one roof from screen to boot)
            p.CabPart = "";
        }
        else
            p.Cab = Ch(D(VehicleLayer.Body, "cab"), "cab") ?? throw new VehicleKitException($"style {st.Name} lists no heavy cab the grammar knows");
        foreach (var t in p.Cab.Provides)
            tags.Add(t);
        if (v.Look != null)
            v.Look.MergedFront = p.Cab.Name == "merged"; // (phase 4: a merged cab's room keeps its brow's front up, so the two read as one mass)
        if (v.CabLook == null)
            p.CabPart = FactionStyle.Noted("heavyCab", D(VehicleLayer.Body, "cab part").Pick(p.Cab.PartsIn(st)));
        p.Nose = Ch(D(VehicleLayer.Body, "nose"), "nose") ?? throw new VehicleKitException($"style {st.Name} lists no hood the grammar knows");
        var nd = D(VehicleLayer.Body, "nose part");
        if (p.Nose.Name == "ram")
        {
            p.HoodPart = FactionStyle.Noted("hood", nd.Pick(st.Choices("hood").Where(h => h != "plates").DefaultIfEmpty("Long Hood").ToArray()));
            p.NoseParts = p.Nose.PartsIn(st);
        }
        else if (p.Nose.Name != "flat")
            p.HoodPart = FactionStyle.Noted("hood", nd.Pick(p.Nose.PartsIn(st)));

        // The rear, then fuel: one tank feeds the engine (an engine has one fuel inlet).
        var rearWeights = r.Rears.ToDictionary(x => x.Rear, x => x.Weight);
        var backDoor = v.Look?.WantsRearDoor == true && tags.Contains("room last") && tags.Contains("long room") ? 2.5 : 1.0; // (phase 2: a body whose door would be in its back leans to a tail it can open onto)
        p.Rear = Ch(D(VehicleLayer.Body, "rear"), "rear", m => rearWeights.ContainsKey(m.Name), m => rearWeights[m.Name] * (m.Name is "bare" or "hitch" ? backDoor : 1)) ?? Module("rear", "bare");
        foreach (var t in p.Rear.Provides)
            tags.Add(t);
        if (!tags.Contains("fuel"))
        {
            var bed = p.Segs.FirstOrDefault(s => s.Kind is "bed" or "gun bed" && s.Len >= 1.5f);
            if (bed != null)
                bed.Drum = true;
            else if (p.Segs.Any(s => s.Kind == "hutch"))
                p.Segs.First(s => s.Kind == "hutch").Drum = true;
            else if (p.Rear.Name == "gun deck")
                p.RearDrum = true;
            else
                p.Rear = Module("rear", "porch"); // (nothing else to stand a drum on)
            tags.Add("fuel");
        }

        // Phase 2: a room that is last, with only a hitch or a bare tail behind it, may have its door in its back (the look's roll; BodyStyleRule.DoorRear).
        if (v.Look is { } look)
        {
            look.RearDoor = look.WantsRearDoor && tags.Contains("room last") && p.Rear.Name is "bare" or "hitch" && tags.Contains("long room"); // (a 3.5 m room's end walls are its loot's and its magazine's)
            if (look.RearDoor)
                v.Modules.Add("door rear");
        }

        // The frame under it.
        var rearLen = p.Rear.Name is "porch" or "porch hitch" ? 1.25f : p.Rear.Name == "gun deck" ? 1.75f : p.Rear.Name == "hitch" ? 0.25f : 0f;
        if (!layered)
        {
            // The rear axle sits 1.25 m to 2.25 m in from the body's end, the tail rails reach the rear's end.
            var shift = p.Segs[0].Kind == "hutch" ? 0.5f : 0.5f * d.Fork("wheelbase").Int(0, p.Segs.Count > 1 || p.End > 5.5f ? 2 : 1); // (a hutch's gun stands where Gun Wagon's frame puts it)
            p.Rails = p.End - Front + 0.25f - shift;
            p.RearAxle = -1.3f + p.Rails;
            p.Tail = p.End - p.RearAxle + rearLen;
            var least = p.Gear.Tandem > 0 ? p.Gear.Tandem + 0.75f : p.Gear.Tag != null ? 1.5f : 0.75f;
            if (p.Tail < least)
                p.Tail = least; // (the rails run on past the body)
            p.Stations.Add(p.RearAxle);
            if (p.Gear.Tandem > 0 || p.Gear.Tag != null)
                p.Stations.Add(p.RearAxle + 1.25f);
        }
        else
        {
            // Kit 3: the chassis layer, fitted under the body: corners, axles, where they stand, the track.
            var c = VehicleChassis.Roll(D(VehicleLayer.Chassis, "roll"), st, only, p.End, p.Segs.Count, tags.Contains("cabover"), p.Segs[0].Kind == "hutch" ? 1.75f : null, p.Rear.Name == "gun deck",
                role?.Chassis(LayerTables.ChassisOf(st)), v.Look?.Ride);
            p.Chassis = c;
            p.Gear = c.Gear;
            GearTags(p);
            p.RearAxle = p.End - c.Inset;
            p.Rails = p.RearAxle - c.FrontZ - 0.375f;
            p.Tail = p.End - p.RearAxle + rearLen;
            var least = c.Extra == "tandem" ? c.Spacing + 0.75f : c.Extra == "tag" ? c.Spacing + 0.25f : 0.75f;
            if (p.Tail < least)
                p.Tail = least;
            p.Stations.Add(p.RearAxle);
            if (c.Extra.Length > 0)
                p.Stations.Add(p.RearAxle + c.ExtraAt);
            if (c.Extra == "pusher" && p.RearAxle - c.Spacing < Front + 0.6f)
                throw new VehicleKitException("a pusher axle under the cab"); // (the next plan)
        }
        if (p.Rails < 1.5f)
            throw new VehicleKitException("too short a body for a frame");
        if (p.Cab.Name == "crew" && tags.Contains("boot") && p.RearAxle < 1.9f)
            throw new VehicleKitException("a car's rear axle under its crew cabin"); // (the differential's face met the cabin's floor: the next plan)

        p.UnderKind = p.Gear.Floor < 0.5f ? Module("under", "stilts") : Ch(D(VehicleLayer.Chassis, "under"), "under") ?? Module("under", "stilts");
        p.Flank = Ch(D(VehicleLayer.Kit, "flank"), "flank");
        p.Roof = Ch(D(VehicleLayer.Body, "roof"), "roof");
        if (v.Look is { } spacesLook && !BodyLook.NoSpaces)
            Spaces(v, p, spacesLook, D(VehicleLayer.Body, "spaces")); // (phase 5: a caged room, or an upper storey over a long one; forks of their own)

        if (layered)
            p.BayDice = D(VehicleLayer.Body, "bays");
        Guns(v, p, D(VehicleLayer.Kit, "guns"));
        return p;
    }

    /// <summary>
    /// Phase 5 (docs/vehiclekit/BODY-STYLES.md, section 11): a caged room in the style's pattern (its chance, <see cref="BodyStyleRule.Cage"/>), else an upper storey over a
    /// long room (<see cref="BodyStyleRule.Uppers"/>, or the recipe's own); either takes the roof, so no rack. The room's bays are planned later: the storey's run is
    /// chosen with them (<see cref="PlanUpper"/>).
    /// </summary>
    private static void Spaces(Vehicle v, PlanData p, BodyLook lk, Dice d)
    {
        var rule = v.Rule;
        var room = p.Segs.FirstOrDefault(s => s.Kind == "room");
        if (room == null)
            return;
        var cage = BodyLook.ForceCage ? (rule.Cage > 0 ? rule.CageKind : 'w') : rule.Cage > 0 && p.Recipe.Upper == null && d.Fork("cage").Chance(rule.Cage) && !BodyLook.NoCages ? rule.CageKind : '\0';
        if (cage != '\0' && BodyLook.ForceUpper is null or "none")
        {
            lk.Cage = cage;
            p.Roof = Module("roof", "none");
            v.Modules.Add("space cage " + cage);
            return;
        }
        var kind = BodyLook.ForceUpper ?? p.Recipe.Upper ?? BodyStyleRule.Weighted(d.Fork("upper storey kind"), rule.Uppers);
        if (kind is "saloon" or "deck" && room.Len >= 4.5f && !BodyLook.NoUppers)
        {
            p.UpperKind = kind;
            p.UpperDice = d.Fork("upper run");
            p.Roof = Module("roof", "none");
        }
    }

    /// <summary>
    /// Which middle bays an upper storey stands over (never the brow or the tail, which rake down): a run of the style's share of them, two at least, long enough for
    /// its stair; the lower roof's cut (a door bay's 2.16 m at least, near the look's own height) and the storey's height or its bulwark's.
    /// </summary>
    private static void PlanUpper(Vehicle v, PlanData p, Seg room, BodyLook lk)
    {
        var rule = v.Rule;
        var d = p.UpperDice!;
        var n = room.Bays.Length;
        if (n < 4)
            return;
        var lengths = lk.Lengths(room.Bays);
        var middle = n - 2;
        var (shareMin, shareMax) = p.Recipe.UpperShare ?? (rule.UpperShareMin, rule.UpperShareMax);
        var want = Math.Clamp((int)MathF.Round((float)d.Between(shareMin, shareMax) * middle), 2, middle);
        var plain = lk.Names(room.Bays);
        var roofs = Enumerable.Range(1, n - 1).Where(k => BaySpots.Roof(room.Bays, plain, k)).ToList();
        // The style's share, else the nearest share that leaves a roof for a turret beside it, else the nearest that is long enough at all.
        var order = Enumerable.Range(2, middle - 1).OrderBy(w => Math.Abs(w - want)).ThenByDescending(w => w).ToList();
        List<int> Starts(int w) => Enumerable.Range(1, middle - w + 1).Where(k => Enumerable.Range(k, w).Sum(i => lengths[i]) >= 3.0f).ToList(); // (the hatch over the stair and room to step off it)
        var reach = BaySpots.Turret.Kit.Reach + BaySpots.Turret.Kit.Table + 0.1f;
        float Mid(int k) => lengths.Take(k).Sum() + lengths[k] / 2;
        bool Leaves(int k, int w) => roofs.Any(r => Mid(r) < lengths.Take(k).Sum() - reach || Mid(r) > lengths.Take(k + w).Sum() + reach); // (a roof whose turret's arm swings clear of the storey)
        var pick = order.Select(w => (W: w, S: Starts(w).Where(k => Leaves(k, w)).ToList())).FirstOrDefault(x => x.S.Count > 0);
        if (pick.S == null)
            pick = order.Select(w => (W: w, S: Starts(w))).FirstOrDefault(x => x.S.Count > 0);
        if (pick.S == null)
            return;
        var from = d.Pick(pick.S);
        var cut = MathF.Round(Math.Clamp(lk.Height * (float)d.Between(0.86, 0.94), 2.18f, 2.36f) * 100) / 100;
        var high = p.UpperKind == "deck" ? (float)d.Between(rule.BulwarkMin, rule.BulwarkMax) : (float)d.Between(rule.UpperTallMin, rule.UpperTallMax);
        lk.Upper = new BodyLook.UpperPlan(p.UpperKind!, from, from + pick.W - 1, cut, MathF.Round(high * 100) / 100);
    }

    /// <summary>
    /// The stair up to an upper storey: against a side wall inside the storey's run, its treads climbing to the back, clear of the seats, the doorway, the magazine at
    /// the front and a back door; the hatch over it, wide enough for a player. False when no stretch of wall takes it.
    /// </summary>
    private static bool PlaceStair(Vehicle v, Seg room, BodyLook lk)
    {
        var up = lk.Upper!;
        var lengths = lk.Lengths(room.Bays);
        var starts = new float[lengths.Length + 1];
        for (var k = 0; k < lengths.Length; k++)
            starts[k + 1] = starts[k] + lengths[k];
        var (run0, run1) = (starts[up.From], starts[up.To + 1]);
        var len = starts[^1];
        var rise = up.Cut + 0.02f; // (to the upper floor's panel top)
        var steps = (int)MathF.Ceiling(rise / 0.4f); // (a ship ladder's rise: a player steps up 0.55)
        var stair = new HullLoft.StairShape(MathF.Round(rise * 100) / 100, steps, 0.55f);
        var door = Array.IndexOf(room.Bays, "Door");
        var (door0, door1) = door >= 0 && !lk.RearDoor ? (starts[door], starts[door + 1]) : (-9f, -9f);
        var seats = SeatSpots(room, lk.DoorSide, lk.RearDoor).Select(t => t.Z - room.Z0).ToArray();
        var names = lk.Names(room.Bays);
        var flat = Enumerable.Range(up.From, up.To - up.From + 1).Select(k => HullLoft.ShapeOf(names[k])!.Value).Min(sh => MathF.Min(sh.A.P[^2].X, sh.B.P[^2].X));
        if (flat < 0.9f)
            return false; // (the level roof too narrow for a hatch a player fits through beside the middle: the hatch runs to the wall's top where it is narrower)
        var back = len - (lk.RearDoor ? 1.5f : 0.3f);
        foreach (var way in new[] { 1f, -1f })
            foreach (var side in new[] { -lk.DoorSide, lk.DoorSide })
            {
                var x = side * (BodyLook.UnderHalf * v.Rule.UpperNarrow >= 0.92f ? 0.5f : 0.25f); // (on the floor's quarter grid, by the wall; nearer the middle under a storey set in)
                for (var foot = 0.5f; foot <= len - 0.25f; foot += 0.25f)
                {
                    // The stair's foot (where its first tread starts) and its top, along the room; the hatch over it, from just before the first tread to the top.
                    var top = foot + way * stair.Run;
                    var (lo, hi) = (MathF.Min(foot, top), MathF.Max(foot, top));
                    // (the hatch from a body's width before the first tread, where the head starts to rise over the ceiling, to a body's width past the last tread a
                    // chest still meets the floor's edge from; then room to step off and stand clear of the storey's end)
                    var chest = MathF.Max(stair.Run, MathF.Floor((stair.Rise - 0.6f) / (stair.Rise / stair.Steps)) * HullLoft.StairShape.Going + 0.45f);
                    var far = foot + way * chest;
                    var (h0, h1) = way > 0 ? (foot - 0.45f, far) : (far, foot + 0.45f);
                    var stepOff = way > 0 ? (far, far + 0.6f) : (far - 0.6f, far);
                    if (h0 < run0 + 0.05f || h1 > run1 - 0.05f || stepOff.Item1 < run0 + 0.02f || stepOff.Item2 > run1 - 0.02f)
                        continue; // (the hatch inside the storey's floor, with room to step off at the top)
                    var approach = way > 0 ? (foot - 0.9f, foot) : (foot, foot + 0.9f); // (where a player stands to go up)
                    if (MathF.Min(lo, approach.Item1) < MathF.Max(1.0f, starts[1]) || MathF.Max(hi, approach.Item2) > MathF.Min(back, starts[^2]))
                        continue; // (clear of the brow's and the tail's rake, the magazine and a back door)
                    if (side == -lk.DoorSide && seats.Any(sz => sz > MathF.Min(lo, approach.Item1) - 0.35f && sz < MathF.Max(hi, approach.Item2) + 0.35f))
                        continue; // (the gunners' seats on that wall)
                    if (side == lk.DoorSide && (door0 < hi + 0.25f && door1 > lo - 0.25f || MathF.Min(lo, approach.Item1) < 1.1f))
                        continue; // (the doorway, and the magazine's crates at the front)
                    (up.StairX, up.StairZ, up.StairWay, up.Stair) = (x, foot, way, stair);
                    up.Hatch = side > 0 ? (x - 0.46f, x + 0.42f, h0, h1) : (x - 0.42f, x + 0.46f, h0, h1); // (a player's width and a little: from 4 cm off the middle by the wall)
                    v.Modules.Add($"upper {up.Kind} over bays {up.From} to {up.To}, cut {up.Cut:0.00} m, {(up.Open ? "bulwark" : "storey")} {up.Height:0.00} m, stair {(side > 0 ? "right" : "left")} at {foot:0.00} m {(way > 0 ? "back" : "forward")}");
                    return true;
                }
            }
        return false;
    }

    /// <summary>Phase 8a part 2: where a boot's tank sill goes, under a lid from <paramref name="z0"/> to <paramref name="z1"/>: nearest the lid's middle, clear of the axles and of the tail's cap; null: nowhere.</summary>
    private static float? TankSpot(PlanData p, float z0, float z1, bool wedge)
    {
        float? best = null;
        for (var z = RailZ(p, z0 + 0.3f); z <= z1 - (wedge ? 0.55f : 0.4f); z += 0.25f) // (a wedge tail's cap comes down further in)
            if (z >= z0 + 0.29f && Clear(p, z) && (best == null || MathF.Abs(z - (z0 + z1) / 2) < MathF.Abs(best.Value - (z0 + z1) / 2)))
                best = z;
        return best;
    }

    /// <summary>Phase 8a part 2: a car's second row by its archetype (a supercar and a compact hatch seat two); 1 for every other recipe and pattern.</summary>
    private static double CarRows(Vehicle v, ShapeRecipe r, BodyPattern b) =>
        r.Shape == "Car" && b.Segments[0].Kind == "gap" && v.Archetype?.Name is "supercar" or "compact hatch" ? 0 : 1;

    /// <summary>The tags a gear recipe gives the rest of the plan.</summary>
    private static void GearTags(PlanData p)
    {
        if (p.Gear.Floor >= 0.5f)
            p.Tags.Add("high floor");
        if (p.Gear.Tandem > 0)
            p.Tags.Add("six");
        if (p.Gear.Tag != null)
            p.Tags.Add("tag axle");
    }

    /// <summary>Axle and tag stations a sill can't go on: rails break for a differential.</summary>
    private static bool Clear(PlanData p, float z) => p.Stations.TrueForAll(a => MathF.Abs(z - a) >= 0.45f);

    private static float RailZ(PlanData p, float z)
    {
        var grid = z > p.RearAxle ? p.RearAxle + 0.375f : -1.675f + 0.25f; // (as Vehicle.RailZ: the tail rails' rows start an eighth behind the differential's back socket; a tandem's are 1.25 m on, the same rows)
        return grid + MathF.Round((z - grid) / 0.25f) * 0.25f;
    }

    /// <summary>Where each gun goes, and which: deck guns where the bed has room clear of the axles, the rest on the room's roof and flank.</summary>
    private static void Guns(Vehicle v, PlanData p, Dice d)
    {
        var st = v.Style;
        var r = p.Recipe;
        var role = v.Role;
        var (gunsLo, gunsHi) = role?.GunRange(r.MinGuns, r.MaxGuns) ?? (r.MinGuns, r.MaxGuns);
        var want = d.Int(gunsLo, gunsHi);
        var count = 0;
        WeaponOption Pick(int k, bool aft)
        {
            var arsenal = v.Arsenal; // (the style's guns and the gunsmith's for this vehicle as it is now: VehicleGunsmith.cs)
            var noWhaler = arsenal.Where(w => w.Name != "Whaler" && !w.Build.StartsWith("Whaler", StringComparison.Ordinal)).ToArray();
            var list = aft && noWhaler.Length > 0 ? noWhaler : arsenal;
            var w = role != null ? role.Pick(d.Fork("weapon " + k), list) : VehicleArmoury.Pick(d.Fork("weapon " + k), list);
            FactionStyle.Noted("weapon", w.Name);
            return w;
        }
        var room = p.Segs.FirstOrDefault(s => s.Kind == "room");
        foreach (var s in p.Segs)
        {
            var ahead = p.Segs.TakeWhile(x => x != s).LastOrDefault();
            switch (s.Kind)
            {
                case "gun bay":
                    s.Gun = true;
                    s.GunZ = RailZ(p, 0.575f);
                    s.Weapon = Pick(count++, false);
                    break;
                case "hutch":
                    s.Gun = true;
                    s.Weapon = Pick(count++, false);
                    break;
                case "boot": // (phase 8a part 2: on a pedestal just behind the cabin, clear of the axles, over the cabin's roof; the tank's sill under the lid behind it)
                    for (var at = s.Z0 + 0.5f; at <= s.Z1 - 0.75f && !s.Gun; at += 0.25f)
                        if (RailZ(p, at) is var bz && bz - 0.125f >= s.Z0 - 0.01f && Clear(p, bz) && TankSpot(p, bz + 0.15f, s.Z1, v.CabLook?.Nose == 'w') is { } tz)
                            (s.Gun, s.GunZ, s.TankZ, s.Weapon) = (true, bz, tz, Pick(count++, false));
                    if (!s.Gun)
                        throw new VehicleKitException("a boot with no room for its gun and its tank clear of the axles"); // (the next plan)
                    break;
                case "gun bed":
                case "bed":
                    if (s.Kind == "bed" && (count >= want || !d.Chance(role?.BedGun ?? 0.5)))
                        break;
                    var behindRoom = ahead is { Kind: "room" };
                    var w = Pick(count, behindRoom);
                    var aft = behindRoom && w.Name != "Whaler";
                    var reach = v.GunReach(w);
                    var spots = new List<float>();
                    for (var at = s.Z0 + 0.625f; at + 0.375f <= s.Z1 + 0.01f; at += 0.5f) // (half a metre from the stilts, or in place of one)
                        spots.Add(at);
                    var next = p.Segs.SkipWhile(x => x != s).Skip(1).FirstOrDefault();
                    foreach (var at in behindRoom ? spots.Where(a => a >= s.Z0 + 1.6f) /* (an aft gun's seat rides the arm's front end, towards the room) */ : spots)
                    {
                        var gz = RailZ(p, at);
                        if (gz - 0.375f < s.Z0 - 0.01f || gz + 0.375f > s.Z1 + 0.01f || !Clear(p, gz))
                            continue;
                        if (next is { Kind: "room" } && gz > next.Z0 - 1.2f) // (a gun's back needs a metre before a room's brow)
                            continue;
                        if (behindRoom && !aft && gz - reach < s.Z0 + 0.1f) // (a forward gun behind a room clears its tail)
                            continue;
                        if (behindRoom && aft && p.Rear.Name == "gun deck")
                            continue;
                        (s.Gun, s.GunZ, s.Aft, s.Weapon) = (true, gz, aft, w);
                        count++;
                        break;
                    }
                    break;
            }
        }
        if (p.Rear.Name == "gun deck") // (a metre and more behind the tag axle, which is at most at the body's end)
        {
            p.RearGunZ = RailZ(p, p.End + 1.125f); // (an aft gun's arm and seat reach forward from its turntable: clear of the room's tail)
            p.RearWeapon = Pick(count, true);
            count++;
        }
        if (p.Tags.Contains("hutch"))
            return; // (a hutch feeds one gun: its own)
        if (room == null)
        {
            if (count == 0)
                throw new VehicleKitException("nowhere for a gun");
            return;
        }

        // The room: the guns it works from inside (roof turrets, a sponson), up to the four belts a magazine feeds.
        var inRoom = Math.Clamp(want - count, count == 0 ? 1 : 0, room.Len >= 5.5f ? 3 : room.Len >= 4.5f ? 2 : 1); // (each is a seat on a wall that loot would take)
        room.Bays = p.Body is { } body ? Bays(p.BayDice!, (int)MathF.Round(room.Len - 3.5f), p.Roof?.Name == "rack", body) : Bays(d.Fork("bays"), (int)MathF.Round(room.Len - 3.5f), p.Roof?.Name == "rack");
        if (p.UpperKind != null && v.Look is { } upLook)
            PlanUpper(v, p, room, upLook); // (phase 5: which bays it stands over; their roofs are cut level for its floor)
        var loft = v.Look?.Names(room.Bays); // (kit 5: the bays as they will be built, so the spots come from their real roofs and flanks: BaySpots)
        // (a turret's arm lies along a flat tall roof: on a sloped or low one it runs into the next bay's wall; never under an upper storey, nor where its arm swings
        // over one: by distance along the room, the table's half and the arm's reach from the bay's middle)
        var bayAt = v.Look?.Lengths(room.Bays) is { } lens ? Enumerable.Range(0, lens.Length).Select(k => lens.Take(k).Sum() + lens[k] / 2).ToArray() : null;
        var clearOf = v.Look?.Upper is { } upper && bayAt != null && v.Look.Lengths(room.Bays) is { } ul
            ? (ul.Take(upper.From).Sum() - BaySpots.Turret.Kit.Reach - BaySpots.Turret.Kit.Table - 0.1f, ul.Take(upper.To + 1).Sum() + BaySpots.Turret.Kit.Reach + BaySpots.Turret.Kit.Table + 0.1f)
            : (float.MaxValue, float.MinValue);
        var roofCandidates = Enumerable.Range(1, room.Bays.Length - 1).Where(k => BaySpots.Roof(room.Bays, loft, k) && (bayAt == null || bayAt[k] < clearOf.Item1 || bayAt[k] > clearOf.Item2)).ToList();
        var middle = Enumerable.Range(1, room.Bays.Length - 2).Where(k => BaySpots.Side(room.Bays, loft, k)).ToList();
        var side = inRoom >= 2 || inRoom == 1 && d.Chance(0.3) ? middle.Count > 0 : false;
        if (side)
        {
            room.SideBay = d.Pick(middle);
            var door = Array.IndexOf(room.Bays, "Door");
            var doorSide = v.Look?.DoorSide ?? 1;
            room.SideSide = v.Look?.RearDoor == true ? (d.Chance(0.5) ? 1 : -1) : Math.Abs(room.SideBay - door) > 1 && d.Chance(0.4) ? doorSide : -doorSide; // (the door side only away from the door and its ladder)
            inRoom--;
        }
        foreach (var k in d.Shuffled(roofCandidates))
        {
            if (room.RoofBays.Count >= inRoom)
                break;
            if (room.RoofBays.All(o => Math.Abs(o - k) >= 2))
                room.RoofBays.Add(k);
        }
        room.RoofBays.Sort();
        room.RoofGuns = room.RoofBays.Count;
        room.SeatCount = room.RoofGuns + (room.SideBay >= 0 ? 1 : 0);
        if (count + room.SeatCount == 0 && v.Look is { Upper: not null } crowded)
        {
            crowded.Upper = null; // (the storey took every roof a gun could stand on: a room without it)
            v.Modules.Add("upper dropped: no roof left for a gun");
            room.RoofBays.Clear();
            foreach (var k in d.Shuffled(Enumerable.Range(1, room.Bays.Length - 1).Where(k => BaySpots.Roof(room.Bays, v.Look.Names(room.Bays), k)).ToList()))
                if (room.RoofBays.Count < Math.Max(1, inRoom) && room.RoofBays.All(o => Math.Abs(o - k) >= 2))
                    room.RoofBays.Add(k);
            room.RoofBays.Sort();
            room.RoofGuns = room.RoofBays.Count;
            room.SeatCount = room.RoofGuns + (room.SideBay >= 0 ? 1 : 0);
        }
        if (count + room.SeatCount == 0)
            throw new VehicleKitException("nowhere for a gun");
        if (v.Look is { Upper: not null } withUpper && !PlaceStair(v, room, withUpper))
        {
            withUpper.Upper = null; // (no stretch of wall for its stair: a room without it)
            v.Modules.Add("upper dropped: no wall for its stair");
        }
    }

    /// <summary>
    /// A room's bays, front to back: a brow, <paramref name="ones"/> metre bays and the door bay in a seeded order, a tail. Heights walk between tall, low and narrow
    /// sections (a "Bay A-B" changes from one to the next), the door bay is tall, and the roofline changes at least once.
    /// </summary>
    public static string[] Bays(Dice d, int ones, bool flatPair = false)
    {
        static string H(Dice d) => d.Pick((string[])["T", "T", "T", "L", "L", "N"]);
        var slots = ones + 1;
        var door = d.Int(0, slots - 1);
        var bounds = new string[slots + 1]; // (bounds[i]: the height between slot i-1 and slot i; bounds[0] is the brow, bounds[slots] the tail)
        bounds[0] = H(d);
        for (var i = 1; i <= slots; i++)
            bounds[i] = d.Chance(0.45) ? H(d) : bounds[i - 1];
        bounds[door] = bounds[door + 1] = "T";
        var keep = new List<int> { door, door + 1 };
        if (flatPair && slots >= 2) // (a roof rack wants two flat tall bays together: the door's and a "Bay T" beside it)
        {
            var n = door + 1 < slots ? door + 1 : door - 1;
            bounds[n] = bounds[n + 1] = "T";
            keep.AddRange([n, n + 1]);
        }
        if (bounds.All(b => b == "T"))
        {
            var ends = new List<int>();
            if (!keep.Contains(0))
                ends.Add(0);
            if (!keep.Contains(slots))
                ends.Add(slots);
            if (ends.Count > 0) // (a brow, a door and a tail stay flat: the brow and the tail rake its ends)
                bounds[d.Pick(ends)] = d.Chance(0.6) ? "L" : "N";
        }
        // A long room steps twice: one change in six metres of hull still reads as a box from the side.
        var changes = Enumerable.Range(1, slots).Count(i => bounds[i] != bounds[i - 1]);
        var free = Enumerable.Range(0, slots + 1).Where(i => !keep.Contains(i)).ToList();
        for (var tries = 0; changes < (slots >= 3 ? 2 : 1) && free.Count > 0 && tries < 8; tries++)
        {
            var i = d.Pick(free);
            bounds[i] = bounds[i] == "T" ? (d.Chance(0.6) ? "L" : "N") : "T";
            changes = Enumerable.Range(1, slots).Count(k => bounds[k] != bounds[k - 1]);
        }
        var names = new List<string> { "Brow " + bounds[0] };
        for (var i = 0; i < slots; i++)
            names.Add(i == door ? "Door" : bounds[i] == bounds[i + 1] ? "Bay " + bounds[i] : $"Bay {bounds[i]}-{bounds[i + 1]}");
        names.Add("Tail " + bounds[slots]);
        return names.ToArray();
    }

    /// <summary>
    /// Kit 3's bays: <see cref="Bays(Dice, int, bool)"/> under a style's <see cref="BodyRange"/>: its section heights' weights, how often the roofline steps, and how often a
    /// brow or a tail is raked down. The same rules hold: the door bay is tall, a rack has its flat pair, the roofline changes at least once (twice from three metre bays).
    /// </summary>
    public static string[] Bays(Dice d, int ones, bool flatPair, BodyRange b)
    {
        string H()
        {
            var x = d.Next() * (b.Tall + b.Low + b.Narrow);
            return x < b.Tall ? "T" : x < b.Tall + b.Low ? "L" : "N";
        }
        var slots = ones + 1;
        var door = d.Int(0, slots - 1);
        var bounds = new string[slots + 1];
        bounds[0] = H();
        for (var i = 1; i <= slots; i++)
            bounds[i] = d.Chance(b.StepChance) ? H() : bounds[i - 1];
        if (d.Chance(b.Rake))
            bounds[0] = d.Chance(0.6) ? "L" : "N"; // (a raked brow)
        if (d.Chance(b.Rake))
            bounds[slots] = d.Chance(0.6) ? "L" : "N"; // (and a raked tail)
        bounds[door] = bounds[door + 1] = "T";
        var keep = new List<int> { door, door + 1 };
        if (flatPair && slots >= 2)
        {
            var n = door + 1 < slots ? door + 1 : door - 1;
            bounds[n] = bounds[n + 1] = "T";
            keep.AddRange([n, n + 1]);
        }
        if (bounds.All(x => x == "T"))
        {
            var ends = new List<int>();
            if (!keep.Contains(0))
                ends.Add(0);
            if (!keep.Contains(slots))
                ends.Add(slots);
            if (ends.Count > 0)
                bounds[d.Pick(ends)] = d.Chance(0.6) ? "L" : "N";
        }
        var changes = Enumerable.Range(1, slots).Count(i => bounds[i] != bounds[i - 1]);
        var free = Enumerable.Range(0, slots + 1).Where(i => !keep.Contains(i)).ToList();
        for (var tries = 0; changes < (slots >= 3 ? 2 : 1) && free.Count > 0 && tries < 8; tries++)
        {
            var i = d.Pick(free);
            bounds[i] = bounds[i] == "T" ? (d.Chance(0.6) ? "L" : "N") : "T";
            changes = Enumerable.Range(1, slots).Count(k => bounds[k] != bounds[k - 1]);
        }
        var names = new List<string> { "Brow " + bounds[0] };
        for (var i = 0; i < slots; i++)
            names.Add(i == door ? "Door" : bounds[i] == bounds[i + 1] ? "Bay " + bounds[i] : $"Bay {bounds[i]}-{bounds[i + 1]}");
        names.Add("Tail " + bounds[slots]);
        return names.ToArray();
    }

    // ---- The build -----------------------------------------------------------------------------------------------

    private static void Build(Vehicle v, PlanData p)
    {
        var g = p.Gear;
        var layered = p.Chassis != null;
        v.FloorY = g.Floor;
        var (frontZ, nose) = p.Chassis is { } ch ? (ch.FrontZ, ch.Nose) : (-1.675f, 0.625f);
        v.NoFenders = frontZ != -1.675f; // (the hood's fenders stand where kit 2's front wheels are)
        v.Ladder(frontZ, p.Rails, nose, p.Tail, g.Differential, frontWing: g.FrontWing, tandem: g.Tandem).SteeredAxle(g.FrontWheel, g.FrontSpring).DrivenAxle(g.RearWheel, g.RearSpring)
            .Powertrain(-1.425f);
        if (v.CabLook != null)
            v.LoftCab(p.Cab.Name); // (phase 3: lofted in the body's language; a merged cab's shell goes on after its room)
        else
            v.OpenCab(shell: p.CabPart);
        if (g.Tag != null)
            v.TagAxle(v.RearAxleZ + (p.Chassis?.ExtraAt ?? 1.25f), g.Tag, g.TagWheel ?? g.RearWheel);
        if (MathF.Abs(v.RearAxleZ - p.RearAxle) > 0.01f)
            throw new VehicleKitException($"the plan put the rear axle at {p.RearAxle}, the frame at {v.RearAxleZ}"); // (the plan's sums are the frame's)

        var dice = v.Dice.Fork(p.Attempt == 0 ? "grammar build" : "grammar build " + p.Attempt);
        var kit = layered ? v.LayerDice(VehicleLayer.Kit).Fork(p.Attempt == 0 ? "grammar kit build" : "grammar kit build " + p.Attempt) : dice; // (kit 3: the guns' and stores' rolls are the kit layer's)
        Room? room = null;
        Seg? roomSeg = null;
        VPart? hutch = null;
        var bedSills = new Dictionary<Seg, VPart>();
        var walls = new List<string> { "hoodsides" };
        var under = p.UnderKind.Name;

        foreach (var s in p.Segs)
            switch (s.Kind)
            {
                case "room":
                    v.Under(under, s.Z0, s.Z1);
                    var spots = SeatSpots(s, v.Look?.DoorSide ?? 1, v.Look?.RearDoor == true);
                    room = v.Hull(p.Recipe.RoomTag, s.Z0, v.FloorY, s.Bays, spots);
                    roomSeg = s;
                    if (v.Look?.Upper is { Stair: not null })
                        v.UpperStorey(room); // (phase 5: the stair up through the hatch and the storey on the cut roofs: Vehicle.Spaces.cs)
                    if (p.Cab.Name == "merged" && p.Segs.IndexOf(s) == 0)
                        v.MergeCab(room);
                    // (not its walls in Trim's crooked patches: a patch on a hull bay goes on the bay's middle, which is inside the room, where a player stands)
                    break;
                case "bed":
                case "gun bed":
                {
                    v.Under(under == "skirted" ? "stilts" : under, s.Z0, s.Z1, s.Gun ? [s.GunZ] : []);
                    if (s.Gun)
                        bedSills[s] = v.Sill(s.GunZ, layers: Vehicle.SillsFor(v.FloorY));
                    var holes = s.Gun ? new[] { new Vehicle.Rect(-0.375f, 0.375f, s.GunZ - 0.375f, s.GunZ + 0.375f) } : [];
                    var spot = DrumSpot(s);
                    v.Deck("bed", v.FloorY, new Vehicle.Rect(-Edge, Edge, s.Z0, s.Z1), "bed", holes, whole: s.Drum ? [spot] : null);
                    if (s.Drum)
                        v.FuelUnderFloor(v.TankOn(v.Wholes[("bed", spot)], new Vector3(1, 0, spot.Z0 + 0.05f)), 1);
                    break;
                }
                case "hutch": // (Generators/GunWagon.cs, at the front of the body)
                {
                    v.Under(under == "skirted" ? "tub" : under, Front, Front + 1f);
                    var deckY = v.FloorY;
                    s.GunZ = v.RailZ(Front + 2.625f);
                    bedSills[s] = v.Sill(s.GunZ, layers: Vehicle.SillsFor(deckY));
                    v.Sill(v.RailZ(Front + 1.125f), layers: Vehicle.SillsFor(deckY));
                    var spot = new Vehicle.Rect(0.375f, 1.375f, Front + 3.25f, Front + 4.25f);
                    v.Deck("bed", deckY, new Vehicle.Rect(-Edge, Edge, Front, s.Z1), "floor", [new Vehicle.Rect(-0.375f, 0.375f, s.GunZ - 0.375f, s.GunZ + 0.375f)], whole: s.Drum ? [spot] : null);
                    if (s.Drum)
                        v.FuelUnderFloor(v.TankOn(v.Wholes[("bed", spot)], new Vector3(1, 0, spot.Z0 + 0.05f)), 1);
                    s.Aft = kit.Fork("aft").Chance(0.5);
                    break;
                }
                case "gap": // (bare rails: air between the cab and the body)
                    break;
                case "boot": // (phase 8a part 2: the gun's sill, then the tank and the lofted lid behind it: Vehicle.Boot)
                    if (s.Gun)
                        bedSills[s] = v.Sill(s.GunZ);
                    v.Boot(s.GunZ + 0.15f, s.Z1, s.TankZ); // (the lid runs under the turret's table, which stands over the cabin's roof line)
                    break;
                case "gun bay": // (Generators/ScrapTanker.cs: a gun on its own sill behind the cab)
                    bedSills[s] = v.Sill(s.GunZ, layers: 2);
                    break;
                case "tank": // (Generators/ScrapTanker.cs: the barrel on cradles of its own, piped to the engine)
                {
                    var front = v.RailZ(s.Z0 - 0.125f);
                    var open = p.Segs.IndexOf(s) > 0 && p.Segs[p.Segs.IndexOf(s) - 1].Kind == "gun bay"; // (a gun bay has nothing to hit: the barrel's end may reach into it, as on a Scrap Tanker)
                    while (front < s.Z0 + (open ? -0.2f : 0.1f))
                        front += 0.25f;
                    var barrel = MathF.Round(s.Len);
                    // (phase 9b: the barrel stood on its front foot's sill, and a front over the rear axle or a station took that sill away and left it nowhere to
                    // stand (Bandit and Highland Scrap Tanker 3): the front steps back a row at a time, up to 0.75 m, until every cradle can go in, and failing
                    // that the barrel stands on the first cradle there is)
                    bool Fits(float f)
                    {
                        for (var k = 0; k < barrel; k++)
                            if (MathF.Abs(f + k - v.RearAxleZ) <= 0.3f || !Clear(p, f + k))
                                return false;
                        return true;
                    }
                    for (var step = 0; step < 3 && !Fits(front); step++)
                        if (Fits(front + 0.25f * (step + 1)))
                        {
                            front += 0.25f * (step + 1);
                            break;
                        }
                    var foot = -1; // (the first cradle that goes in carries the barrel: its front one taken by an axle, it stands on the next foot)
                    for (var k = 0; k < barrel; k++)
                        if (MathF.Abs(front + k - v.RearAxleZ) > 0.3f && Clear(p, front + k))
                        {
                            v.Sill(v.RailZ(front + k), layers: 2);
                            if (foot < 0)
                                foot = k;
                        }
                    foot = Math.Max(foot, 0);
                    var tank = v.Stand(barrel > 2.5f ? "Tanker Barrel 3 m" : "Tanker Barrel 2 m", new Vector3(-0.25f, Vehicle.SillTop(2), v.RailZ(front + foot)), Orient.Forward, null, "footL" + foot, 0.03f);
                    if (!v.Electric)
                        v.Link("pipe", tank, v.Engine!, laid: true);
                    v.Tanks.Add(tank);
                    break;
                }
            }

        // The stern gun deck, before the guns: a deck behind the room, its gun facing aft.
        VPart? rearSill = null;
        if (p.Rear.Name == "gun deck" && p.RearWeapon != null)
        {
            rearSill = v.Sill(p.RearGunZ, layers: Vehicle.SillsFor(v.FloorY));
            var spot = new Vehicle.Rect(0.375f, 1.375f, p.End + 0.25f, p.End + 1.25f);
            v.Deck("porch", v.FloorY, new Vehicle.Rect(-Edge, Edge, p.End, p.End + 1.5f), "deck", [new Vehicle.Rect(-0.375f, 0.375f, p.RearGunZ - 0.375f, p.RearGunZ + 0.375f)], whole: p.RearDrum ? [spot] : null);
            if (p.RearDrum)
                v.FuelUnderFloor(v.TankOn(v.Wholes[("porch", spot)], new Vector3(1, 0, spot.Z0 + 0.05f)), 1);
        }

        // Guns: the room's from its seats, fed by its magazine; the beds' and bays' with a seat on the arm and a box of their own (a belt from the room would
        // cross it where a player walks), except a hutch's, which it feeds.
        if (room != null && roomSeg != null)
        {
            v.FedTurrets = true;
            var seats = room.Seats.ToList();
            VPart Seat(float z)
            {
                var best = seats.OrderBy(x => MathF.Abs(v.CentreOf(x).Z - z)).First();
                seats.Remove(best);
                return best;
            }
            foreach (var k in roomSeg.RoofBays)
                v.Turret(room.Bays[k], WeaponFor(v, kit, room.Bays[k], false), seat: Seat(v.CentreOf(room.Bays[k]).Z));
            if (roomSeg.SideBay >= 0)
            {
                var bay = room.Bays[roomSeg.SideBay];
                v.SideGun(bay, roomSeg.SideSide, Seat(v.CentreOf(bay).Z), WeaponFor(v, kit, bay, true));
            }
        }
        var fedGuns = v.Turrets.Select(t => t.Gun).ToArray();
        foreach (var s in p.Segs.Where(s => s.Gun))
        {
            v.FedTurrets = s.Kind == "hutch";
            v.Turret(bedSills[s], s.Weapon, aft: s.Aft, lift: s.Kind == "boot");
        }
        v.FedTurrets = false;
        if (rearSill != null)
            v.Turret(rearSill, p.RearWeapon, aft: true);

        // What feeds them: the room's magazine, else the hutch.
        if (room != null && fedGuns.Length > 0)
            Magazine(v, room, roomSeg!.Z0, fedGuns);
        if (p.Segs.FirstOrDefault(s => s.Kind == "hutch") is { } hs)
        {
            var deckY = v.FloorY;
            hutch = v.Stand("Ammo Hutch", new Vector3(-0.5f, deckY + 0.05f, Front + 0.875f), Orient.Forward, null, "foot00", 0.2f);
            v.Feeds(new Vector3(-0.25f, deckY + 0.25f, Front + 1.125f), new Vector3(0.25f, deckY + 0.25f, Front + 1.125f), "FMJ",
                v.Dice.Fork("rounds").Pick((string[])["Armour Piercing", "Explosive Tip", "Dragon's Breath"]), hutch, "top", new Vector3(0, deckY + 0.25f, Front + 1.65f), v.Gun!);
        }
        if (room != null)
        {
            if (roomSeg!.Len < 4.5f)
                v.EndChest(room); // (a short room's walls go to seats, the door and the magazine: its loot takes the end wall first)
            v.Furnish(room, clearMiddle: true);
            v.FurnishUpper(room); // (phase 7c: the storey over it)
            if (room.Containers.Count == 0 && !v.EndChest(room))
                throw new VehicleKitException($"the {room.Tag}: no wall took a container"); // (seats and the door took the walls: plan again)
            var chance = p.Flank?.Name switch { "panniers" => 0.4, "loaded" => 0.85, _ => 0.0 };
            if (chance > 0)
                v.Panniers(room.Bays, kit.Fork("panniers"), chance);
        }

        if (room != null && p.Roof?.Name == "rack" && !Rack(v, room, roomSeg!.RoofBays))
            p.Roof = Module("roof", "none"); // (no two flat tall bays side by side to lay it on)

        // The rear.
        switch (p.Rear.Name)
        {
            case "porch":
            case "porch hitch":
                Porch(v, p.End, dice.Fork("porch").Chance(0.5) ? 1 : -1);
                if (p.Rear.Name == "porch hitch")
                    v.TowHitch();
                break;
            case "hitch":
                v.TowHitch();
                break;
        }

        // Bed sides.
        if (p.Segs.FirstOrDefault(s => s.Kind is "bed" or "gun bed") is { } bed && p.Sides != null)
        {
            switch (p.Sides.Name)
            {
                case "walls":
                    foreach (var s in new[] { -1f, 1f })
                        v.Wall("sides", new Vector3(s, 0, 0), s * Edge, bed.Z0, bed.Z1, v.FloorY - 0.12f, 0.5f, "sheet", 0.2);
                    v.Boarding(new Vector3(-Edge - 0.1f, v.FloorY, (bed.Z0 + bed.Z1) * 0.5f), "sides");
                    walls.Add("sides");
                    break;
                case "rails":
                    foreach (var s in new[] { -1f, 1f })
                        v.TubeRails(s * Edge, bed.Z0 + 0.125f, bed.Z1 - 0.1f, v.FloorY, s);
                    break;
            }
        }

        v.Hood(p.HoodPart, p.NoseParts);
        VehicleGenJob.Yield();
        v.Trim(walls.ToArray());
        v.GuaranteeMarks(); // (every mark the style's signature asks for: VehicleMarks.cs)
        v.RoleKit(room); // (kit 4: what the role is for that the shape didn't bring, and its loot tier: VehicleRoles.cs; nothing without a role)
        if (layered)
        {
            VehicleGenJob.Yield();
            v.Weather(); // (kit 3's wear layer: paint, soot, patches; the decks' rusted-out edges went in as they were laid)
        }

        // The check holds a heavy vehicle to a stability of 0.6 (half its track over its centre of mass's height): a plan that stacks too much up high is planned again.
        VehicleGenJob.Yield(); // (each measure rebuilds the construct whole: a slice of its own)
        var stable = v.Stability();
        if (stable < MinStability)
            throw new VehicleKitException($"stability {stable:0.00}: too much up high for its track");
        VehicleGenJob.Yield();
        var (side, top) = v.Fill(); // (and the Silhouette rule's limits, with a margin: a plan that came out a box is planned again)
        var limits = new VehicleCheckOptions();
        var margin = layered ? 0.01f : 0.005f; // (kit 3: a plan that measured 61.6 % built at 62 %; a point keeps a seed off the check's own limit)
        if (side > limits.MaxSideFill - margin || top > limits.MaxTopFill - margin)
            throw new VehicleKitException($"it fills {side:P0} of its side box and {top:P0} of its plan box: a box");
        VehicleGenJob.Yield();

        Describe(v, p);
    }

    /// <summary>
    /// A railed rack on the room's roof (Generators/DoubleDecker.cs's top deck, smaller): deck plates over the longest run of flat tall bays (a door bay, "Bay T"), their
    /// "top" sockets on the deck cells' centres, holes round the turrets standing there, tube rails down both edges. False when no two such bays stand together.
    /// </summary>
    private static bool Rack(Vehicle v, Room room, IReadOnlyList<int> turretBays)
    {
        var plan = room.Plan.ToArray();
        var loft = room.Bays.Select(b => b.Def.Name).ToArray();
        var flat = Enumerable.Range(0, plan.Length).Select(k => BaySpots.Flat(plan, v.Look != null ? loft : null, k)).ToArray(); // (by the built bays' roofs: BaySpots)
        var (bestFrom, bestLen) = (0, 0);
        for (var i = 0; i < flat.Length; i++)
        {
            var n = 0;
            while (i + n < flat.Length && flat[i + n])
                n++;
            if (n > bestLen)
                (bestFrom, bestLen) = (i, n);
        }
        if (bestLen < 2)
            return false;
        var (front, back) = (room.Bays[bestFrom], room.Bays[bestFrom + bestLen - 1]);
        var roof = room.FloorTop + front.Def.BoundsMax.Y;
        var z0 = v.CentreOf(front).Z - (front.Def.BoundsMax.Z - front.Def.BoundsMin.Z) / 2 + 0.125f;
        var z1 = z0 + 0.25f * MathF.Floor((v.CentreOf(back).Z + (back.Def.BoundsMax.Z - back.Def.BoundsMin.Z) / 2 - z0) / 0.25f);
        var holes = turretBays.Where(k => k >= bestFrom && k < bestFrom + bestLen).Select(k => v.CentreOf(room.Bays[k]).Z).Select(z => new Vehicle.Rect(-0.375f, 0.375f, z - 0.375f, z + 0.375f)).ToArray();
        v.Deck("rack", roof, new Vehicle.Rect(-Edge, Edge, z0, z1), "deck", holes);
        foreach (var s in new[] { -1f, 1f })
            v.TubeRails(s * Edge, z0 + 0.125f, z1, roof, s);
        return v.Panels.TryGetValue("rack", out var laid) && laid.Count > 0;
    }

    /// <summary>A room's seats half a metre to the side away from the door (+x on the right, phase 2's left doors -x): the front, the back, and a third a metre ahead of the back one.</summary>
    private static (float X, float Z)[] SeatSpots(Seg s, float doorSide, bool rear = false)
    {
        var x = -0.5f * doorSide;
        var back = rear ? 0.75f : 0; // (a door in the back: the back seats a little forward, out of its way in)
        var list = new List<(float, float)>();
        if (s.SeatCount >= 1)
            list.Add((x, s.Z0 + 0.375f));
        if (s.SeatCount >= 2)
            list.Add((x, s.Z1 - 0.875f - back));
        if (s.SeatCount >= 3)
            list.Add((x, s.Z1 - 1.875f - back)); // (a metre ahead of the back one: clear of the magazine's reels at the front and of the room's middle, where a player stands)
        return list.ToArray();
    }

    /// <summary>A bed's drum plate: a whole 1 m plate at its back, beside any gun.</summary>
    private static Vehicle.Rect DrumSpot(Seg s) => new(0.375f, 1.375f, s.Z1 - 1.25f + (s.Len >= 2f ? 0f : 0.25f), s.Z1 - 0.25f + (s.Len >= 2f ? 0f : 0.25f));

    /// <summary>The weapon for a roof or flank gun, from the style's list by the order the guns go on.</summary>
    private static WeaponOption WeaponFor(Vehicle v, Dice d, VPart mount, bool side)
    {
        // A flame gun on a sponson doesn't follow its gunner's look round (it swings 20 degrees and more off), and a long gun on a roof low and far forward would
        // want a pedestal over the cab, which a roof has no socket for: the next of the style's guns, in a seeded order.
        var at = v.CentreOf(mount);
        var arsenal = v.Arsenal; // (the style's guns and the gunsmith's for this vehicle: VehicleGunsmith.cs)
        var order = v.Role is { } role ? role.Order(d.Fork("weapon " + v.Turrets.Count), arsenal) : VehicleArmoury.Order(d.Fork("weapon " + v.Turrets.Count), arsenal);
        var w = order.FirstOrDefault(x => !(side && x.Box == null) && !((side || at.Y < 1.5f) && at.Z - v.GunReach(x) < 0.25f)) ?? order[0]; // (a sponson hangs low on the waist)
        FactionStyle.Noted("weapon", w.Name);
        return w;
    }

    /// <summary>The room's magazine (Generators/BoxTruck.cs, HeavyShape.Magazine): two crates of different rounds by the front bay, a switch and a lever, feeding every gun.</summary>
    private static void Magazine(Vehicle v, Room room, float z0, VPart[] guns)
    {
        var y = room.FloorTop + 0.2f;
        var side = v.Look?.DoorSide ?? 1; // (the crates on the door's side, the seats on the other)
        v.Feeds(new Vector3(0.5f * side, y, z0 + 0.625f), new Vector3(0f, y, z0 + 0.625f), "FMJ", v.Dice.Fork("rounds").Pick((string[])["Armour Piercing", "Explosive Tip", "Dragon's Breath"]), room.Bays[0], "lever", guns[0], guns[1..]);
    }

    /// <summary>A plated porch behind the body with the fuel drum on it (HeavyShape.Porch).</summary>
    private static void Porch(Vehicle v, float end, float side)
    {
        v.Sill(v.RailZ(end + 0.75f), layers: Vehicle.SillsFor(v.FloorY));
        var spot = side > 0 ? new Vehicle.Rect(0.125f, Edge, end + 0.25f, end + 1.25f) : new Vehicle.Rect(-Edge, -0.125f, end + 0.25f, end + 1.25f);
        var gap = side > 0 ? new Vehicle.Rect(0.125f, Edge, end, end + 0.25f) : new Vehicle.Rect(-Edge, -0.125f, end, end + 0.25f);
        v.Deck("porch", v.FloorY, new Vehicle.Rect(-Edge, Edge, end, end + 1f), "deck", [gap], whole: [spot]);
        v.FuelUnderFloor(v.TankOn(v.Wholes[("porch", spot)], new Vector3(side, 0, end + 0.3f)), side);
    }

    /// <summary>The description players read, and the modules in the file's kit line and the trace.</summary>
    private static void Describe(Vehicle v, PlanData p)
    {
        var body = string.Join("+", p.Segs.Select(s => s.Kind == "room" ? $"room({string.Join(",", s.Bays)})" : $"{s.Kind} {s.Len:0.#} m"));
        void Note(string family, string value)
        {
            v.Modules.Add($"{family} {value}");
            FactionStyle.Noted("module " + family, value);
        }
        Note("body", string.Join("+", p.Segs.Select(s => s.Kind)));
        Note("cab", p.Cab.Name);
        Note("nose", p.Nose.Name);
        Note("rear", p.Rear.Name);
        Note("under", p.UnderKind.Name);
        if (p.Flank != null)
            Note("flank", p.Flank.Name);
        if (p.Sides != null)
            Note("sides", p.Sides.Name);
        if (p.Roof != null)
            Note("roof", p.Roof.Name);
        v.Modules.Add("layout " + body);
        if (p.Chassis is { } c && v.Layers is { } layers)
        {
            v.Modules.Insert(0, "chassis " + c.Label);
            v.Modules.Insert(0, "seeds " + layers.Label(v.Seed));
        }
        if (p.Attempt > 0)
            v.Modules.Add("plan " + (p.Attempt + 1));
        var sb = new StringBuilder();
        var article = "aeiou".Contains(p.Recipe.Noun[0]) ? "An" : "A";
        sb.Append($"{article} {p.Recipe.Noun}: {p.Nose.Label}, {p.Cab.Label}, ");
        sb.Append(string.Join(", ", p.Segs.Select(s => s.Kind == "tank" ? Module("body", "tank").Label : s.Gun && s.Kind is "bed" ? "a gun bed" : Module("body", s.Kind).Label)));
        if (p.Roof?.Name == "rack")
            sb.Append(", ").Append(p.Roof.Label);
        sb.Append(p.Rear.Name == "bare" ? "." : $", {p.Rear.Label}.");
        sb.Append($" {v.Turrets.Count} gun{(v.Turrets.Count == 1 ? "" : "s")}.");
        v.Description = sb.ToString();
    }
}
