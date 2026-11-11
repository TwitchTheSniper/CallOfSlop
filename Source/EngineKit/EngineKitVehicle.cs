using System.Globalization;
using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.EngineKit;

/// <summary>
/// What a vehicle generation asks of the Engine Kit (docs/vehiclekit/ENGINEKIT.md, "In a vehicle"): a tech tier, a top speed on the flat (m/s; 0: the tier's), an
/// acceleration (m/s^2 at half the top speed; 0: none asked) and the vehicle's mass without its engine (kg; 0: the kit measures it by making the vehicle once with its
/// stock engine). While one is current (<see cref="Use"/>), <see cref="Vehicle.Powertrain"/> stands a kit engine on the sill in place of the style's engine, and the
/// vehicle is named with <see cref="Suffix"/>, so it never reads as a committed seed. None is current anywhere else: every shipped seed is made as it always was.
/// </summary>
public sealed record EngineWish
{
    public int Tier { get; init; } = 3;
    public float TopSpeed { get; init; }
    public float Accel { get; init; }
    public float DryMass { get; init; }
    public int Seed { get; init; }

    /// <summary>Drive every axle (<see cref="Vehicle.AllWheelDrive"/>): past about 1.7 times a V8 two driven tyres limit the speed, not the engine.</summary>
    public bool AllWheels { get; init; }

    /// <summary>The top speeds a tier's vehicles aim at when none is given (m/s; the slow-cars table's suggestion for a 1.5 t car).</summary>
    public static readonly float[] TierTop = [0, 25, 27, 30, 33, 36];

    public float Top => TopSpeed > 0 ? TopSpeed : TierTop[Math.Clamp(Tier, 1, 5)];

    public string Suffix => string.Create(CultureInfo.InvariantCulture, $" [engine tier {Tier}, {Top:0} m/s{(Accel > 0 ? $", {Accel:0.#} m/s2" : "")}{(AllWheels ? ", all-wheel drive" : "")}]");

    /// <summary>
    /// The engine power (kW) a vehicle of <paramref name="mass"/> kg needs: the arcade drag at the top speed (0.006 v^3 W a kilogram, <see cref="ConstructPhysics.AirDrag"/>)
    /// and half again for rolling and the drive line's losses, or what accelerating at <paramref name="accel"/> at half the top speed takes (at 85 % through the
    /// drive line), whichever is more.
    /// </summary>
    public static float PowerFor(float mass, float top, float accel)
    {
        var drag = mass * ConstructPhysics.AirDrag * top * top * top * 1.5f;
        var acc = accel > 0 ? mass * accel * top * 0.5f / 0.85f : 0;
        return MathF.Max(drag, acc) / 1000f;
    }

    [ThreadStatic] private static EngineWish? _current;

    /// <summary>The wish of the vehicle being made on this thread (null: none, the stock engines).</summary>
    public static EngineWish? Current => _current;

    public static Scope Use(EngineWish? w) => new(w);

    public readonly struct Scope : IDisposable
    {
        private readonly EngineWish? _back;

        internal Scope(EngineWish? w)
        {
            _back = _current;
            _current = w;
        }

        public void Dispose() => _current = _back;
    }
}

/// <summary>The Engine Kit inside a vehicle generation (<see cref="Vehicle.Powertrain"/>): the request for a vehicle, and the plan built from its flywheel.</summary>
public static class EngineKitVehicle
{
    /// <summary>The box a vehicle's engine may take ahead of its flywheel: crank length, width, height (m). Wider than the rails would hit the front wheels.</summary>
    public static readonly Vector3 Bay = new(3.0f, 1.9f, 1.75f);

    /// <summary>
    /// The request for a vehicle of <paramref name="dryMass"/> kg without its engine: Small pieces (they turn at a car's rpm), nothing pointing down, in the bay; the
    /// power for the wish's speed with the engine's own mass counted (two rounds of plans: an engine's mass changes the power it needs).
    /// </summary>
    public static (EngineRequest Request, EnginePlan? Plan) Plan(EngineWish w, float dryMass, string paint, int seed)
    {
        var r = new EngineRequest
        {
            Tier = Math.Clamp(w.Tier, 1, ConstructParts.MaxTier),
            Size = 0,
            Underside = false,
            Fit = Bay,
            Paint = paint,
            Seed = seed,
            PowerKw = EngineWish.PowerFor(dryMass + 250, w.Top, w.Accel),
        };
        EnginePlan? p = null;
        for (var round = 0; round < 2; round++)
        {
            var plans = EngineKit.Plans(r);
            if (plans.Count == 0)
                return (r, null);
            p = plans[0];
            r = r with { PowerKw = EngineWish.PowerFor(dryMass + p.Mass, w.Top, w.Accel) };
        }
        return (r, p);
    }
}
