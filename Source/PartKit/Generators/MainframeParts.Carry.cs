using System.Numerics;
using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class MainframeParts
{
    /// <summary>A craft the Mainframe carries (or berths), as built on its own: its bounds and centre of mass in its own frame (from its
    /// blueprint's origin), and its mass.</summary>
    private readonly record struct Craft(Vector3 Min, Vector3 Max, Vector3 Com, float Mass);

    private static readonly Dictionary<string, Craft> Crafts = new(StringComparer.Ordinal);

    /// <summary>
    /// A shipped blueprint (a file in Constructs/Blueprints) built on a scratch world, as the game builds a carried craft: where its
    /// centre of mass is, so each dock's box is round it (ConstructCargo.BuildCarry finds the dock a craft's centre of mass is in). Its own
    /// carried craft are left off (the Zero Day's Shuttle and Limpet: a carried craft can't carry its own yet).
    /// </summary>
    private static Craft CraftOf(string file)
    {
        lock (Crafts)
        {
            if (Crafts.TryGetValue(file, out var known))
                return known;
            var path = Path.Combine(ConstructBuild.BlueprintFolder, file);
            var bp = ConstructBuild.ParseBlueprint(File.ReadAllText(path), path);
            bp.Carry.Clear();
            var c = ConstructBuild.Build(new ConstructWorld(), bp, Vector3.Zero, Quaternion.Identity, out var why) ?? throw new InvalidOperationException($"mainframe: {file} doesn't build: {why}");
            c.Rebuild();
            var craft = new Craft(c.LocalMin, c.LocalMax, c.Com, c.Mass);
            System.Console.WriteLine($"mainframe: {bp.Name}: {craft.Mass / 1000:0.0} t, bounds {craft.Min} to {craft.Max}, centre of mass {craft.Com}");
            return Crafts[file] = craft;
        }
    }

    /// <summary>The ship's "carry" list: the Shuttles on their pads and the Limpets in their pockets, each held by its clamp from the start.</summary>
    private static List<PyDict> CarryList(Writer w) =>
        [.. w.Carried.Select(c => Kw(("blueprint", c.Blueprint), ("at", Py.L(Py.Round(c.At.X, 2), Py.Round(c.At.Y, 2), Py.Round(c.At.Z, 2))), ("turn", (int)c.Turn)))];
}
