using CallOfSlop.PartKit.Generators;
using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.ShipGen;

/// <summary>
/// The procedural ship generator's shipped hulls (<c>--gen-parts shipgen_parts</c>): each brief below is made, its parts written under
/// Constructs/Parts/ShipGen/&lt;class&gt;, its blueprint (the hull with its reference fit) to Constructs/Blueprints, and its report (every
/// choice and why) to docs/sandbox/shipgen/reports. A hull that breaks a rule writes nothing: the run fails with the list.
/// docs/sandbox/shipgen/README.md is the guide.
/// </summary>
public sealed class ShipGenParts : IPartGenerator
{
    public string Name => "shipgen_parts";

    private const string Out = "Constructs/Parts/ShipGen";
    private const string BpOut = "Constructs/Blueprints";
    private const string ReportOut = "docs/sandbox/shipgen/reports";

    /// <summary>The hulls that ship: the Traders' haulers first (Karl, 2026-11-22: "give the hauler a try").</summary>
    public static readonly ShipBrief[] Shipped =
    [
        new("Traders", "trade between markets", 1),
        new("Traders", "trade between markets", 7),
        new("Traders", "trade between markets", 3, Cockpit: CockpitKind.Sealed), // (the sealed cockpit, Karl 2026-11-22: a room inside the hull)
    ];

    public void Make(PartOutput o)
    {
        o.Own(Out);
        o.Own(ReportOut);
        var shared = new Dictionary<string, PyDict>();
        foreach (var p in TraderHauler.SharedParts())
        {
            var d = p.Json();
            shared.Add((string)d["name"]!, d);
            o.File($"{Out}/Shared/{FileName((string)d["name"]!)}", PyJson.Dumps(d, 2) + "\n");
        }
        var fittings = new Dictionary<string, PyDict>(shared);
        BuildingParts.AddPartDefs(o, fittings, Out);
        var names = new HashSet<string>();
        var problems = new List<string>();
        foreach (var brief in Shipped)
        {
            var gen = new TraderHauler(brief);
            if (!names.Add(gen.Word))
                throw new InvalidOperationException($"shipgen_parts: two shipped hulls are called {gen.Word}: give one a name in its brief");
            var made = gen.Make(fittings);
            problems.AddRange(made.Problems.Select(p => $"{made.ShipName} (seed {brief.Seed}): {p}"));
            foreach (var d in made.Parts)
                o.File($"{Out}/{gen.Word}/{FileName((string)d["name"]!)}", PyJson.Dumps(d, 2) + "\n");
            o.File($"{BpOut}/{FileName(made.ShipName)}", PyJson.Dumps(made.Blueprint, 2) + "\n");
            o.File($"{ReportOut}/{gen.Word}.md", made.Report);
        }
        if (problems.Count > 0)
            throw new InvalidOperationException("shipgen_parts: the hulls break the rules:\n  " + string.Join("\n  ", problems));
    }
}
