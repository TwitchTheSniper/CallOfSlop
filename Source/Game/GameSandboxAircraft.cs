using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

/// <summary>
/// AI aircraft (docs/vehiclekit/AIRCRAFT.md, "Air spawn" and "Bot pilots"): a generated aircraft made through the dynamic spawn (<see cref="VehicleSpawner"/>), put in the air
/// over a point already flying, and flown by a server-side <see cref="AircraftPilot"/> through its stabilizer's autopilot. The server's alone; nothing new on the wire.
/// </summary>
public sealed partial class Game
{
    /// <summary>At most this many pilots at once (the list never grows past it).</summary>
    public const int MaxPilots = 32;

    private readonly List<AircraftPilot> _pilots = new(MaxPilots);

    /// <summary>The bot pilots flying now (server).</summary>
    public IReadOnlyList<AircraftPilot> Pilots => _pilots;

    /// <summary>
    /// Server: asks for a faction aircraft (<paramref name="style"/> and <paramref name="shape"/>: "Bandit", "Drone", "Plane" or "Helicopter") made at spawn time, put in the
    /// air <paramref name="height"/> m over the ground at <paramref name="over"/> heading <paramref name="yaw"/> (radians, as a player's), already flying, and handed to a bot
    /// pilot with <paramref name="order"/> about <paramref name="over"/> (a patrol's <paramref name="radius"/>). <paramref name="height"/> 0: it stands on the ground with no pilot.
    /// <paramref name="done"/> hears the craft and its pilot (both null when it wasn't made). Returns why it was refused, or null: queued.
    /// </summary>
    public string? SandboxSpawnAircraft(string style, string shape, int? seed, Vector3 over, float height, float yaw, PilotOrder order = PilotOrder.Patrol, float radius = 40,
        Action<Construct?, AircraftPilot?, string>? done = null)
    {
        if (!IsServer)
            return "only the host makes faction aircraft";
        if (VehicleSpawnKit.Find(style, shape) is not { } g)
            return $"no faction vehicle \"{style} {shape}\"";
        if (g is not IAircraftShape)
            return $"the {g.Name} doesn't fly";
        if (height > 0 && _pilots.Count >= MaxPilots)
            return $"{MaxPilots} bot pilots are flying already";
        var ground = SandboxGroundAt(over);
        return VehicleSpawn.Request(style, shape, seed, ground, yaw, done: (c, name, why, _) => AircraftDown(c, name, why, over, height, order, radius, done),
            context: SpawnContexts.At(ground));
    }

    /// <summary>The aircraft is on the ground where the spawner set it: lifted and launched, and a pilot takes it.</summary>
    private void AircraftDown(Construct? c, string name, string why, Vector3 over, float height, PilotOrder order, float radius, Action<Construct?, AircraftPilot?, string>? done)
    {
        if (c == null)
        {
            Console.WriteLine($"[aircraft] {name} wasn't made: {why}");
            done?.Invoke(null, null, why);
            return;
        }
        if (height <= 0)
        {
            Console.WriteLine($"[aircraft] {name} stands on the ground as construct {c.Id}");
            done?.Invoke(c, null, "");
            return;
        }
        if (_pilots.Count >= MaxPilots || AircraftAutopilot.Stab(c) < 0)
        {
            Console.WriteLine($"[aircraft] {name} as construct {c.Id}: {(_pilots.Count >= MaxPilots ? "no room for another pilot" : "no Flight Stabilizer")}: left on the ground");
            done?.Invoke(c, null, "left on the ground");
            return;
        }
        var index = Constructs.Bodies.IndexOf(c);
        if (index >= 0)
            SandboxSpawnMove(index, Vector3.UnitY * height); // (straight up from where it stood: the ground under it is the ground the spawner found)
        var plane = AircraftAutopilot.IsPlane(c);
        var speed = plane ? MathF.Max(25, 1.5f * ConstructAero.StallSpeed(c)) : 0;
        AircraftAutopilot.Launch(Constructs, c, speed);
        var pilot = new AircraftPilot(c, order, over, radius, height);
        _pilots.Add(pilot);
        Console.WriteLine($"[aircraft] {name} as construct {c.Id} launched {height:0} m up{(plane ? $" at {speed:0} m/s" : "")}; pilot: {order} about ({over.X:0}, {over.Z:0}), radius {pilot.Radius:0}");
        done?.Invoke(c, pilot, "");
    }

    /// <summary>Server, every Sandbox tick: each pilot's look (one in <see cref="AircraftPilot.Every"/> ticks each); a pilot done with its craft is dropped. No allocation.</summary>
    private void SandboxPilotsTick()
    {
        if (_pilots.Count == 0)
            return;
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var b0 = GC.GetAllocatedBytesForCurrentThread();
        for (var i = _pilots.Count - 1; i >= 0; i--)
            if (!_pilots[i].Step(Constructs))
            {
                Console.WriteLine($"[aircraft] pilot of construct {_pilots[i].Id} let go: {_pilots[i].Line(Constructs)}");
                _pilots.RemoveAt(i);
            }
        PilotsBytes += GC.GetAllocatedBytesForCurrentThread() - b0;
        PilotsCpuMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        PilotsTicks++;
    }

    /// <summary>What the pilots' ticks have cost (server): CPU (ms), bytes allocated (a pilot letting go logs a line), ticks with a pilot.</summary>
    public double PilotsCpuMs;
    public long PilotsBytes;
    public int PilotsTicks;
}
