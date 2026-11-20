using System.Globalization;

namespace CallOfSlop.ProcShips;

/// <summary>
/// What a procedural ship spends its size on (generator version 3, docs/procships/PROCSHIPS.md "The fit"): a class has points
/// (a corvette 8, a frigate 12, a destroyer 18), its reactors and its drive cost them, and what is left is its payload, which caps
/// its guns, its cargo, its craft and its role's own fittings. A big reactor (or two) and a heavy drive leave little: a fast,
/// powerful ship that carries and fights less. The reactor's plate grows with it, up to 6 x 6 m (the biggest module socket).
/// </summary>
public sealed record ProcFit(int ReactorSize, int Reactors, ProcDrive Drive, int Points)
{
    /// <summary>The reactor sizes: the plate's side (m), its word and its cost in points (each).</summary>
    public static readonly (int Side, string Word, int Cost)[] ReactorSizes = [(2, "small", 1), (3, "medium", 2), (4, "large", 3), (6, "huge", 5)];

    public static int DriveCost(ProcDrive d) => d switch { ProcDrive.Light => 1, ProcDrive.Heavy => 4, _ => 2 };

    public static int PointsOf(ProcClass c) => c switch { ProcClass.Corvette => 8, ProcClass.Frigate => 12, _ => 18 };

    /// <summary>A medium reactor and a standard drive: the fit the class's counts (turrets, cargo plates) were set for.</summary>
    public static int StandardPayload(ProcClass c) => PointsOf(c) - 2 - DriveCost(ProcDrive.Standard);

    public int ReactorCost => ReactorSizes.First(r => r.Side == ReactorSize).Cost * Reactors;

    public int Payload => Points - ReactorCost - DriveCost(Drive);

    /// <summary>How the payload scales the class's counts: 1 for the standard fit, less for a ship that spent its points on power and thrust.</summary>
    public double Scale(ProcClass c) => Math.Clamp(Payload / (double)StandardPayload(c), 0.15, 1.75);

    public string ReactorWord => ReactorSizes.First(r => r.Side == ReactorSize).Word;

    public string Words => $"{(Reactors == 2 ? "twin " : "a ")}{ReactorWord} reactor{(Reactors == 2 ? "s" : "")} ({Reactors} x {ReactorSize} m), a {Drive.ToString().ToLowerInvariant()} drive";

    public string Budget => string.Create(CultureInfo.InvariantCulture, $"payload {Payload} of {Points} points (reactors {ReactorCost}, drive {DriveCost(Drive)})");

    /// <summary>
    /// The fit a spec rolls, by class and role: warships and carriers run big reactors (a lancer's railgun charges from them), haulers
    /// a heavy drive, a stealth ship a small reactor and a light drive (little heat to see); twin reactors now and then. Never a fit
    /// that leaves under one point of payload: the drive steps down, then the reactors.
    /// </summary>
    public static ProcFit Roll(ProcShipSpec spec, ProcRole role, ProcDice dice)
    {
        var f = dice.For("fit");
        var cls = spec.Class;
        // The reactor's size: the class sets the range, the role the lean.
        (int, double)[] sizes = cls switch
        {
            ProcClass.Corvette => [(2, 0.55), (3, 0.4), (4, 0.05)],
            ProcClass.Frigate => [(2, 0.2), (3, 0.5), (4, 0.3)],
            _ => [(3, 0.35), (4, 0.45), (6, 0.2)],
        };
        var lean = role switch
        {
            ProcRole.Lancer or ProcRole.Carrier => 1,
            ProcRole.Gunship or ProcRole.Comms => f.Chance(0.5) ? 1 : 0,
            ProcRole.Stealth => -1,
            ProcRole.Freighter or ProcRole.Liner => f.Chance(0.4) ? -1 : 0,
            _ => 0,
        };
        var size = f.Weighted(sizes);
        var at = Array.FindIndex(ReactorSizes, r => r.Side == size);
        var top = cls switch { ProcClass.Corvette => 2, ProcClass.Frigate => 2, _ => 3 }; // (a 6 m plate only in a destroyer's engineering)
        size = ReactorSizes[Math.Clamp(at + lean, 0, top)].Side;
        var twin = f.Chance(role switch
        {
            ProcRole.Lancer => 0.4, ProcRole.Carrier => 0.3, ProcRole.Gunship => 0.25, ProcRole.Comms => 0.2, ProcRole.Stealth => 0.05,
            _ => 0.12,
        } * (cls == ProcClass.Corvette ? 0.5 : 1));
        var drive = f.Weighted(role switch
        {
            ProcRole.Freighter or ProcRole.IceHauler or ProcRole.Salvager => [(ProcDrive.Light, 0.1), (ProcDrive.Standard, 0.45), (ProcDrive.Heavy, 0.45)],
            ProcRole.Gunship or ProcRole.Lancer => [(ProcDrive.Light, 0.1), (ProcDrive.Standard, 0.55), (ProcDrive.Heavy, 0.35)],
            ProcRole.Stealth => [(ProcDrive.Light, 0.7), (ProcDrive.Standard, 0.3)],
            ProcRole.Liner or ProcRole.Comms => [(ProcDrive.Light, 0.3), (ProcDrive.Standard, 0.6), (ProcDrive.Heavy, 0.1)],
            _ => new (ProcDrive, double)[] { (ProcDrive.Light, 0.2), (ProcDrive.Standard, 0.55), (ProcDrive.Heavy, 0.25) },
        });
        var fit = new ProcFit(size, twin ? 2 : 1, drive, PointsOf(cls));
        while (fit.Payload < 1)
            fit = fit.Drive != ProcDrive.Light ? fit with { Drive = fit.Drive - 1 }
                : fit.Reactors == 2 ? fit with { Reactors = 1 }
                : fit with { ReactorSize = ReactorSizes[Math.Max(0, Array.FindIndex(ReactorSizes, r => r.Side == fit.ReactorSize) - 1)].Side };
        return fit;
    }

    /// <summary>A count the class sets for the standard fit, scaled by the payload (at least <paramref name="least"/>).</summary>
    public int Cap(ProcClass c, int standard, int least = 0) => Math.Max(least, (int)Math.Round(standard * Scale(c)));
}

/// <summary>A ship's drive: light (fewer engine plates), standard, or heavy (each plate a size bigger, a nacelle's too).</summary>
public enum ProcDrive { Light, Standard, Heavy }
