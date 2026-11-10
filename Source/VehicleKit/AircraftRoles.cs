namespace CallOfSlop.VehicleKit;

/// <summary>
/// What a faction asks for when it wants something in the air (docs/vehiclekit/AIRCRAFT.md, "Faction aircraft"): an air role ("scout", "patrol", "escort") becomes one of
/// the aircraft shapes its style names in <see cref="FactionStyle.Extra"/>, chosen by the seed, with the height and patrol radius a pilot flies it at. Ground roles
/// (<see cref="VehicleRoles"/>) never pick an aircraft: their shape lists name none.
/// </summary>
public static class AircraftRoles
{
    /// <summary>The aircraft shapes, in the order a style's list is read.</summary>
    public static readonly string[] Shapes = ["Drone", "Plane", "Helicopter"];

    /// <summary>The air roles a faction asks for.</summary>
    public static readonly string[] Names = ["scout", "patrol", "escort"];

    /// <summary>Each role's shapes and weights: a scout is mostly a drone, a patrol a plane or a helicopter, an escort a helicopter over the convoy.</summary>
    private static readonly Dictionary<string, (string Shape, double W)[]> ByRole = new()
    {
        ["scout"] = [("Drone", 3), ("Helicopter", 1)],
        ["patrol"] = [("Plane", 2), ("Helicopter", 2), ("Drone", 1)],
        ["escort"] = [("Helicopter", 3), ("Drone", 2)],
    };

    /// <summary>How often each role comes up when a request names none.</summary>
    private static readonly (string Role, double W)[] AnyRole = [("scout", 2), ("patrol", 2), ("escort", 1)];

    /// <summary>Whether a style fields any aircraft (names one in its extras).</summary>
    public static bool Fields(FactionStyle st) => Array.Exists(Shapes, s => Array.IndexOf(st.Extra, s) >= 0);

    /// <summary>A role by name (any case), or null.</summary>
    public static string? Find(string? role) =>
        string.IsNullOrWhiteSpace(role) ? null : Array.Find(Names, n => n.Equals(role.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The role (<paramref name="role"/> null or empty: one by <see cref="AnyRole"/>'s weights) and the shape a style fields it in, from the seed: the same seed always
    /// picks the same. Null shape: the style fields no aircraft for it (a shape a role names that the style doesn't is left out; when none is left, any it fields).
    /// </summary>
    public static (string Role, string? Shape) Pick(FactionStyle st, string? role, int seed)
    {
        var d = Dice.Of(seed);
        var r = Find(role) ?? Weighted(d.Fork("air role"), AnyRole);
        var own = ByRole[r].Where(x => Array.IndexOf(st.Extra, x.Shape) >= 0).ToArray();
        if (own.Length == 0)
            own = Shapes.Where(s => Array.IndexOf(st.Extra, s) >= 0).Select(s => (s, 1.0)).ToArray();
        return (r, own.Length == 0 ? null : Weighted(d.Fork("air shape " + r), own));
    }

    private static string Weighted(Dice d, (string Name, double W)[] table)
    {
        var roll = d.Next() * table.Sum(x => x.W);
        foreach (var (name, w) in table)
            if ((roll -= w) < 0)
                return name;
        return table[^1].Name;
    }

    /// <summary>The height a pilot flies a shape at over the ground (m): drones low, planes high enough to circle.</summary>
    public static float HeightOf(string shape) => shape switch { "Drone" => 25, "Helicopter" => 35, _ => 60 };

    /// <summary>A patrol's radius about its point (m): a plane's circle is wide (a tight one banks it 48 degrees and loses height).</summary>
    public static float RadiusOf(string shape) => shape switch { "Drone" => 30, "Helicopter" => 45, _ => 90 };
}
