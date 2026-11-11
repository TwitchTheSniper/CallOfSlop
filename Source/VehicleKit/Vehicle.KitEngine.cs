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
        KitBurn = C.ModCount > 0 && C.ModEngines?[0] is { } me ? me.Burn : 0;
        KitNotes.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"kit engine {KitEngine.Name} {p.Kw:0} kW ({p.Cylinder}, {p.FuelPart}{(p.BoostPart != null ? ", " + p.BoostPart : "")}) for {w.Top:0} m/s at {w.DryMass + p.Mass:0} kg"));
        return fly;
    }

    /// <summary>The kit engine's burn at full throttle (litres a second; 0: no kit engine), as the game works it out.</summary>
    public float KitBurn { get; private set; }

    /// <summary>The tank part the kit adds for a kit engine's endurance (<see cref="KitTanks"/>): 150 l in a 0.5 x 0.5 x 1 m box.</summary>
    public const string KitTankPart = "Large Fuel Tank";

    /// <summary>The top-up when the last litres don't need a <see cref="KitTankPart"/>, or it won't go in: 40 l in a 0.5 m cube.</summary>
    public const string KitTopUpPart = "Fuel Tank";

    /// <summary>
    /// Tankage for a kit engine (ENGINEKIT.md "In a vehicle", "Fuel"): the shape keeps its own tank, piped to the engine, and when what the construct holds
    /// is short of <see cref="EngineWish.EnduranceMinutes"/> at full power, <see cref="KitTankPart"/>s go on as saddle tanks, hung on a rail's outer face at
    /// axle height. The same ordered places on every shape: between the axles, nearest the middle of the wheelbase first, left before right (the laid
    /// branch shafts of all-wheel drive and a tandem run under the right rail); then on the tail rails behind the last axle (under a bed or a boot). Each
    /// stands a wheel's radius and 10 cm clear of every axle, so no wheel's travel or steering reaches it, below the floor, so no door or view is blocked;
    /// a place the build rules refuse (a step, a sill's wing, a hull) is passed over. The construct's fuel is one pool, so only the shape's own tank is piped.
    /// </summary>
    public void KitTanks(EngineWish w)
    {
        if (KitEngine == null || KitBurn <= 0 || Engine == null || Diff == null)
            return;
        var need = KitBurn * w.EnduranceMinutes * 60f;
        var have = C.FuelCapacity();
        var added = 0;
        var behind = 0;
        if (have < need)
        {
            var r = WheelName.Length > 0 ? MathF.Max(Def(WheelName).WheelRadius, 0.3f) : 0.5f;
            var clear = r + 0.1f + 0.5f; // (a wheel's radius, 10 cm, and half the tank's length)
            var lastAxle = CentreOf(Diff2 ?? Diff).Z;
            var spots = new List<(float X, float Z, float Rank)>();
            var mid = (FrontAxleZ + RearAxleZ) * 0.5f;
            for (var z = RailGrid + MathF.Ceiling((FrontAxleZ + clear - RailGrid) / 0.25f - 0.001f) * 0.25f; z <= RearAxleZ - clear + 0.001f; z += 0.25f)
                foreach (var side in new[] { -1f, 1f })
                    spots.Add((side, z, MathF.Abs(z - mid) + (side > 0 ? 0.01f : 0)));
            for (var z = TailGrid + MathF.Ceiling((lastAxle + clear - TailGrid) / 0.25f - 0.001f) * 0.25f; z <= TailZ - 0.5f + 0.001f; z += 0.25f)
                foreach (var side in new[] { -1f, 1f })
                    spots.Add((side, z, 100f + (z - lastAxle) + (side > 0 ? 0.01f : 0)));
            spots.Sort((a, b) => a.Rank.CompareTo(b.Rank));
            foreach (var part in new[] { KitTankPart, KitTopUpPart }) // (the big tank first; the last few litres in the small one, where the big one won't go)
            foreach (var (x, z, _) in spots)
            {
                if (have >= need || added >= MaxKitTanks)
                    break;
                if (part == KitTankPart && need - have <= Def(KitTopUpPart).FuelCapacity)
                    break; // (the last litres: the small tank)
                try
                {
                    Tanks.Add(Put(part, new Vector3(x * (RailX + Half + 0.25f), 0, z), Orient.Forward, 0.03f));
                    have += Def(part).FuelCapacity;
                    added++;
                    behind += z > RearAxleZ ? 1 : 0;
                }
                catch (VehicleKitException)
                {
                    // (something is there, or the tank would run into it: the next place)
                }
            }
        }
        KitTankLitres = have;
        KitNotes.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"fuel {have:0} l ({added} tank(s) added, {added - behind} between the axles, {behind} behind) for {KitBurn:0.000} l/s: {have / KitBurn / 60f:0.0} min at full power"));
    }

    /// <summary>At most this many tanks are added (1050 l): past it the note says the endurance falls short.</summary>
    public const int MaxKitTanks = 7;

    /// <summary>The litres the construct holds after <see cref="KitTanks"/>.</summary>
    public float KitTankLitres { get; private set; }
}
