using System.Globalization;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// Wheel Former wheels in the kit (drivetrain-and-engines.md "All-wheel drive and round wheels", "Formed wheels"): a wheel asked for by its size and tyre
/// instead of a catalogue part. The kit names it by its key ("Extrusion wheel 35x90x0 rubber", <see cref="Extrusion.Name"/>), which is how a blueprint
/// records a run-time piece: the step holds the key, never the run-time id, and <see cref="ConstructParts.Find"/> makes the piece from it. Off by default.
/// </summary>
public sealed partial class Vehicle
{
    /// <summary>Fit Wheel Former wheels in place of the style's catalogue wheels (before <see cref="SteeredAxle"/>), sized by <see cref="FormedWheelMass"/>. Off by default.</summary>
    public bool FormedWheels;

    /// <summary>The vehicle's mass the formed wheels carry (kg; 0: <see cref="FormedWheelWish"/>'s, else 1500).</summary>
    public float FormedWheelMass;

    /// <summary>Formed wheels for every vehicle made on this thread (<c>--formed-wheels</c>): the load on each wheel to size them for (kg; 0: weigh the vehicle first). Null: off.</summary>
    public static float? FormedWheelWish => _formedWish;

    [ThreadStatic] private static float? _formedWish;

    public static FormedScope UseFormedWheels(float? mass) => new(mass);

    public readonly struct FormedScope : IDisposable
    {
        private readonly float? _back;

        internal FormedScope(float? mass)
        {
            _back = _formedWish;
            _formedWish = mass;
        }

        public void Dispose() => _formedWish = _back;
    }

    private bool Formed => FormedWheels || _formedWish != null;

    /// <summary>
    /// Proposed (2026-11-11): a formed tyre carries this many kilograms a metre of width a metre of diameter. 1600: a 1.8 t car on 0.9 m wheels gets
    /// 0.3 m tyres, a 4 t six-wheeler 0.45 m.
    /// </summary>
    public const float FormedLoadPerM2 = 1600f;

    /// <summary>
    /// The formed wheel standing in for <paramref name="catalogue"/>: its diameter (the arch the body is built round; to 5 cm), its width from the load on
    /// each wheel (<see cref="FormedLoadPerM2"/>, 0.2 to 1 m), its tyre rubber, or steel for a wheel that can't go flat (Solid, Spiked, road wheels).
    /// </summary>
    public string FormedWheelFor(string catalogue, int wheels)
    {
        var d = Def(catalogue);
        if (d.Role != PartRole.Wheel || d.WheelRadius <= 0 || d.IsExtrusion)
            return catalogue;
        var across = Cm(d.WheelRadius * 2);
        var load = FormedWheelMass > 0 ? FormedWheelMass / Math.Max(wheels, 1) : _formedWish is > 0 and var w ? w : 1500f / Math.Max(wheels, 1);
        var wide = Cm(Math.Clamp(load / (FormedLoadPerM2 * across / 100f), 0.2f, 1f));
        return string.Create(CultureInfo.InvariantCulture, $"{Extrusion.Prefix}wheel {wide}x{across}x0 {(d.FlatTyre ? "rubber" : "steel")}");
    }

    /// <summary>The wheel's width the formed one had before <see cref="CornerWheel"/> fell back to the catalogue's (how often the arch was too narrow).</summary>
    public int FormedNarrowed { get; private set; }

    private static int Cm(float m) => Math.Max(Extrusion.StepCm, (int)MathF.Round(m * 100f / Extrusion.StepCm) * Extrusion.StepCm);

    /// <summary>
    /// A corner's wheel: the formed one when asked, and when its width won't go in (a fender, the body), the same diameter at the catalogue wheel's width.
    /// </summary>
    private VPart CornerWheel(VPart spring, string catalogue, int wheels)
    {
        if (!Formed)
            return Onto(spring, "axle", catalogue, "axle");
        var formed = FormedWheelFor(catalogue, wheels);
        try
        {
            return Onto(spring, "axle", formed, "axle");
        }
        catch (VehicleKitException) when (formed != catalogue)
        {
            var d = Def(catalogue);
            var fit = string.Create(CultureInfo.InvariantCulture, $"{Extrusion.Prefix}wheel {Cm(d.BoundsMax.X - d.BoundsMin.X)}x{Cm(d.WheelRadius * 2)}x0 {(d.FlatTyre ? "rubber" : "steel")}");
            FormedNarrowed++;
            return Onto(spring, "axle", fit, "axle");
        }
    }
}
