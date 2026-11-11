using System.Numerics;
using CallOfSlop.EngineKit;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// A kit engine in the powertrain (docs/vehiclekit/ENGINEKIT.md, "In a vehicle"): only while an <see cref="EngineWish"/> is current, which no shipped seed's
/// generation has, so every committed blueprint is made as it always was.
/// </summary>
public sealed partial class Vehicle
{
    /// <summary>The Engine Kit's engine standing in for the style's, when one was asked for (null: the style's own engine).</summary>
    public EngineResult? KitEngine { get; private set; }

    /// <summary>
    /// The flywheel stands on the sill where the style's engine would (by its "-y" side, so its centre is as high as a V8's), the gearbox on its +z as on any
    /// engine; the cranks run forward from it and the cylinders stand open (no hood: <see cref="Hood"/> leaves it bare). The plan is the kit's for the wish's
    /// speed and the vehicle's mass without an engine, in <see cref="EngineKitVehicle.Bay"/>.
    /// </summary>
    private VPart KitFlywheel(EngineWish w, VPart sill, float z)
    {
        var (r, p) = EngineKitVehicle.Plan(w, w.DryMass, "", Seed);
        if (p == null)
            throw new VehicleKitException($"the Engine Kit has no plan for {r}");
        var fly = Stand($"{EngineKit.EngineKit.SizeWords[p.Size]} Flywheel", new Vector3(0, 0.375f, z), Orient.Forward, sill, "-y");
        EngineKit.EngineKit.Assemble(this, r, p, fly); // (a refused piece fails the generation: the spawner rerolls, the command line says why)
        KitEngine = new EngineResult { Built = true, Plan = p, Name = EngineKit.EngineKit.LayoutName(p.Layout, p.Cylinders), Kw = p.Kw, Mass = p.Mass, Note = r.ToString() };
        KitNotes.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"kit engine {KitEngine.Name} {p.Kw:0} kW ({p.Cylinder}, {p.FuelPart}{(p.BoostPart != null ? ", " + p.BoostPart : "")}) for {w.Top:0} m/s at {w.DryMass + p.Mass:0} kg"));
        return fly;
    }
}
