using System.Numerics;
using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Mainframe, the first capital ship (Karl, 2026-11-20: "a capital ship, taking aspects from Day Zero but a new, capital-ship
/// style hull ... multiple reactors, thrusters, multiple levels ... an internal berth for Day Zero, an internal shuttle bay with a
/// large hangar, four Limpet boarding craft, a huge storage of robot shells, missile bays ... around 1 km in length. Use the Laser
/// Cutter system to make holes and other cutting equipment in order to make walls, floors and hull panels"; "dozens if not hundreds
/// of airlocks, hatches"; "an elevator system within"; "add any features, rooms etc. which you think would make sense").
///
/// Where the Zero Day is drawn as big ship-named parts, the Mainframe is built the way a player's yard would build it: its hull, decks,
/// walls and bulkheads are steel plate off an Extruder ("Extrusion plate ...") and plate a Laser Cutter has cut ("Cut Plate (...)":
/// corners cut to follow the hull's lines, openings for doors, hatches, lift shafts and bays), so every sheet is a piece the production
/// line makes, up to the Capital Laser Cutter's 64 by 32 m bed (HullLoft.CutPlateMaxCm). Its own parts are few: the frame nodes the
/// slanting hull plates hang on, and the machinery a yard can't cut (reactors, drives, bay doors). Everything else is the game's own
/// parts: doors, hatches, clamps, lifts, turrets, lockers, shell docks, consoles.
///
/// The hull follows the Zero Day's lines at capital scale (docs/sandbox/constructs/mainframe.md): a flat keel and spine, the sides
/// flaring out to a sharp beam line and leaning in above it, a faceted chisel bow, dark armour slabs proud of the pale skin in steps,
/// link-blue light along the beam. Every station of the hull is the midship section scaled about one line, so each facet of each band
/// is a flat trapezoid: one plane, one frame node, its plates laid flat on it.
///
/// Ship metres: x across (0 the centreline, + starboard), y up from the keel, z from the bow tip (0) aft to the transom (1000); forward
/// is -z. The blueprint's body origin (step 0) is at <see cref="Origin"/>, mid-ship on the main deck, so the craft it carries sit within
/// the 200 m a carried craft may lie from it.
/// </summary>
public sealed partial class MainframeParts : IPartGenerator
{
    public string Name => "mainframe_parts";

    private const string Out = "Constructs/Parts/Mainframe";
    public const string BlueprintFile = "Constructs/Blueprints/Mainframe.json";
    public const string ShipName = "Mainframe";
    private const string Group = "Ship", Section = "Mainframe";

    // The paint: the Zero Day's (the IT faction's look). Pale skin, dark armour, link blue; inside pale walls over a blue dado.
    private const string Skin = "#bdb9ad", Skin2 = "#a9a59a", Armour = "#3b4148", Armour2 = "#30353b", Blue = "#2c5aa0", Frame = "#1f2226",
        Hazard = "#e0b020", Glow = "#5ac8ff", Wall = "#c4c8ca", WallLow = "#2c5aa0", Deck = "#4a4f55", Ceil = "#9aa1a6", Console = "#22262b",
        Screen = "#58c8e8", Bay = "#5d636a", BayFloor = "#3e4247", Grate = "#34383c", Copper = "#8a5a3a", Soot = "#1a1c1e";

    /// <summary>Step 0, the body's origin, in ship metres: mid-ship, on the main deck's top.</summary>
    private static readonly Vector3 Origin = new(0, 30, 500);

    /// <summary>Ship metres to the body's frame (step 0 at the origin), to the centimetre.</summary>
    private static Vector3 B(double x, double y, double z) => new((float)(Math.Round(x * 100) / 100 - Origin.X), (float)(Math.Round(y * 100) / 100 - Origin.Y), (float)(Math.Round(z * 100) / 100 - Origin.Z));

    public void Make(PartOutput o)
    {
        o.Own(Out);
        var own = new Dictionary<string, ConstructPartDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in Parts())
        {
            var name = (string)d["name"]!;
            var text = PyJson.Dumps(d, 2) + "\n";
            o.File($"{Out}/{FileName(name)}", text);
            own[name] = ConstructParts.Parse(text, name); // (the game's own reading: its sockets and bounds, and a refusal here rather than at start-up)
        }
        var w = new Writer(own);
        Lay(w);
        o.File(BlueprintFile, w.Text(ShipName, Description, CarryList(w)));
        var plates = w.Steps.Count(s => s.Part.StartsWith(Extrusion.Prefix, StringComparison.Ordinal) || HullLoft.IsCutName(s.Part));
        var cut = w.Steps.Count(s => HullLoft.IsCutName(s.Part));
        System.Console.WriteLine($"mainframe: {w.Steps.Count} steps ({plates} plates, {cut} of them laser-cut), {w.Welds} welds, {w.Joints} joints, {w.Keys} different pieces, {w.Carried.Count} craft carried");
    }

    private const string Description = "The Mainframe, a capital ship a kilometre long: a faceted armoured hull of laser-cut plate, a berth in its keel for a Zero Day, a dorsal hangar for three Shuttles, four Limpet pockets, a vault of robot shells, two missile fields, four fusion reactors and six drive bells, decks of rooms joined by lifts and a corridor the length of the ship, and airlocks all along both flanks.";

    /// <summary>The ship's own parts, as their files' dicts.</summary>
    private static IEnumerable<PyDict> Parts()
    {
        yield return Node();
        foreach (var d in OwnParts())
            yield return d;
    }

    // ---- The frame node

    public const string NodeName = "Mainframe Frame Node";

    /// <summary>Sockets of a node: up to the hull (+y) the most, as every plate of a facet hangs on its node; a few each other way.</summary>
    private static readonly (char Key, string Dir, int Count)[] NodeSocks = [('u', "+y", 200), ('e', "+x", 10), ('w', "-x", 10), ('n', "+z", 10), ('s', "-z", 10), ('d', "-y", 10)];

    /// <summary>
    /// A frame node: a steel block a slanting facet of the hull is built on. A yard welds it inside the facet's plane, turned to lie in
    /// it, and hangs the facet's plates on it by their backs, so a hull of flat plates follows any line. Its origin is on the facet's
    /// plane, the block half a metre inside it. Step 0 of the blueprint is one too.
    /// </summary>
    private static PyDict Node()
    {
        var p = new Part(NodeName, Small, Section, "A frame node: a steel block welded inside one face of a capital hull, turned to lie in that face. The face's plates hang on it, so a hull of flat plates can follow any line.", role: "frame", tool: "welder", material: "steel", color: Frame, group: Group);
        p.Add(Box([-0.25, -0.75, -0.25], [0.25, -0.25, 0.25], Frame, shape: Kw(("finish", "dirty steel"))));
        p.Sock("u0", [0, -0.25, 0], "+y");
        p.Stats(120, 4000, 30, 4.0);
        var d = p.Json();
        var socks = new List<object?>();
        foreach (var (key, dir, count) in NodeSocks)
            for (var i = 0; i < count; i++)
                socks.Add(new PyDict { ["name"] = $"{key}{i}", ["at"] = Vec(NodeSockAt(dir)), ["dir"] = dir });
        d["sockets"] = socks; // (more than the part kit's 64: the game takes 255, ConstructParts.MaxSockets)
        return d;
    }

    private static double[] NodeSockAt(string dir) => dir switch
    {
        "+y" => [0, -0.25, 0],
        "-y" => [0, -0.75, 0],
        "+x" => [0.25, -0.5, 0],
        "-x" => [-0.25, -0.5, 0],
        "+z" => [0, -0.5, 0.25],
        _ => [0, -0.5, -0.25],
    };
}
