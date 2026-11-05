using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// A vehicle's running gear, as a recipe a style lists: what's sprung how at each end. A recipe is whole because its choices go together: how far out an arm throws a wheel decides how wide
/// the body may be between the wheels and how low its floor (<see cref="Floor"/>). Front and rear needn't match. "Tag" adds a free-rolling axle behind the driven one, "Dually" means the rear wheels are doubled.
/// </summary>
public sealed record Gear(string Name, string FrontSpring, string FrontWheel, string RearSpring, string RearWheel, string Differential = "Wide Differential", float FrontWing = 0.5f, float Floor = 0.625f, string? Tag = null, string? TagWheel = null);

public sealed partial class Vehicle
{
    /// <summary>The recipe this seed rolled from the style's light or heavy list (by <see cref="IsHeavy"/>).</summary>
    public Gear PickGear(params string[] only)
    {
        var list = IsHeavy ? Style.HeavyGear : Style.LightGear;
        var fits = only.Length == 0 ? list : list.Where(g => only.Contains(g.Name)).ToArray();
        return Dice.Fork("gear").Pick(fits.Length > 0 ? fits : throw new VehicleKitException($"style {Style.Name} has no gear named {string.Join(", ", only)}"));
    }

    /// <summary>Which of a spring's sockets bolts to a frame: a strut hangs from its upper side, everything else by its mount.</summary>
    private static string BoltBy(ConstructPartDef spring) => Array.Exists(spring.Sockets, s => s.Name == "side") ? "side" : "mount";

    /// <summary>An unpowered axle behind the differential at <paramref name="z"/>: a stub beam out from each tail rail, a spring and a wheel on it. Takes a third of the load off the driven axle and stretches the silhouette.</summary>
    public Vehicle TagAxle(float z, string spring, string wheel)
    {
        var at = RailZ(z);
        var wing = Def(Diff!.Def.Name).BoundsMax.X > 0.7f ? 0.5f : 0.25f;
        foreach (var side in new[] { -1f, 1f })
        {
            var beam = Put(BeamName(wing), new Vector3(side * (0.375f + wing * 0.5f), 0, at), Orient.Along(Vector3.UnitX), 0.03f);
            var tip = Array.FindIndex(beam.Def.Sockets, s => s.Name == "+z");
            C.SocketAt(beam.Index, tip, out var p, out _);
            Corner(beam, MathF.Abs(FromRest(p).X) > MathF.Abs(CentreOf(beam).X) ? "+z" : "-z", spring, wheel);
        }
        return this;
    }

    /// <summary>A stance's floor: rails to floor by one sill or two.</summary>
    public static int SillsFor(float floor) => floor < 0.5f ? 1 : 2;
}
