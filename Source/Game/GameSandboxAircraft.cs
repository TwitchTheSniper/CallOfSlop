using System.Numerics;
using Brutal.RakNetApi;
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
        Action<Construct?, AircraftPilot?, string>? done = null, byte owner = 255)
    {
        if (!IsServer)
            return "only the host makes faction aircraft";
        if (VehicleSpawnKit.Find(style, shape) is not { } g)
            return $"no faction vehicle \"{style} {shape}\"";
        if (g is not IAircraftShape)
            return $"the {g.Name} doesn't fly";
        if (height > 0 && _pilots.Count + _airPending >= MaxPilots)
            return $"{MaxPilots} bot pilots are flying already";
        var ground = SandboxGroundAt(over);
        var why = VehicleSpawn.Request(style, shape, seed, ground, yaw, owner, done: (c, name, why, _) => AircraftDown(c, name, why, over, height, order, radius, done),
            context: SpawnContexts.At(ground));
        if (why == null && height > 0)
            _airPending++;
        return why;
    }

    /// <summary>Piloted aircraft asked for and not yet made (counted against <see cref="MaxPilots"/> and <see cref="MaxFactionAir"/>).</summary>
    private int _airPending;

    /// <summary>The aircraft is on the ground where the spawner set it: lifted and launched, and a pilot takes it.</summary>
    private void AircraftDown(Construct? c, string name, string why, Vector3 over, float height, PilotOrder order, float radius, Action<Construct?, AircraftPilot?, string>? done)
    {
        if (height > 0 && _airPending > 0)
            _airPending--;
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
    // ---- In the game: creative spawns and faction air (docs/vehiclekit/AIRCRAFT.md, "In the game") ----------------------------------------

    /// <summary>At most this many piloted aircraft asked for by <see cref="SandboxRequestAir"/> live at once (pilots flying plus requests on the way).</summary>
    public const int MaxFactionAir = 8;

    /// <summary>A convoy gets an air escort this often (one in three), and never sooner than <see cref="AirEscortEvery"/> s after the last.</summary>
    public const double AirEscortChance = 0.35, AirEscortEvery = 45;

    private double _airEscortAt;
    private ulong _airSeedState = (ulong)Environment.TickCount64 * 0x9E3779B97F4A7C15UL + 1;

    /// <summary>Faction aircraft flying or on the way (server).</summary>
    public int FactionAirLive => _pilots.Count + _airPending;

    /// <summary>
    /// Server: the hook the faction AI calls for something in the air (docs/vehiclekit/AIRCRAFT.md, "Faction aircraft"): an aircraft of <paramref name="style"/> for the air
    /// <paramref name="role"/> ("scout", "patrol", "escort"; null or empty: one by the roles' weights), made at spawn time, launched over <paramref name="over"/> at its shape's
    /// height and flown by a bot pilot on <paramref name="order"/> (a patrol's radius by its shape when <paramref name="radius"/> is 0). Refused past
    /// <see cref="MaxFactionAir"/>, or when the style fields no aircraft. <paramref name="done"/> as <see cref="SandboxSpawnAircraft"/>'s. Returns why it was refused, or null: queued.
    /// </summary>
    public string? SandboxRequestAir(string style, string? role, Vector3 over, int? seed = null, PilotOrder order = PilotOrder.Patrol, float radius = 0, float yaw = 0,
        Action<Construct?, AircraftPilot?, string>? done = null, byte owner = 255)
    {
        if (!IsServer)
            return "only the host makes faction aircraft";
        if (Styles.All.FirstOrDefault(s => s.Name.Equals(style, StringComparison.OrdinalIgnoreCase)) is not { } st)
            return $"no faction style \"{style}\"";
        if (!string.IsNullOrWhiteSpace(role) && AircraftRoles.Find(role) == null)
            return $"no air role \"{role}\": {string.Join(", ", AircraftRoles.Names)}";
        if (FactionAirLive >= MaxFactionAir)
            return $"{MaxFactionAir} faction aircraft are flying already";
        var s = seed ?? VehicleSpawnKit.Next(ref _airSeedState);
        var (r, shape) = AircraftRoles.Pick(st, role, s);
        if (shape == null)
            return $"the {st.Name} style fields no aircraft";
        Console.WriteLine($"[aircraft] {st.Name} {r}: a {shape}, seed {s}, over ({over.X:0}, {over.Z:0})");
        return SandboxSpawnAircraft(st.Name, shape, s, over, AircraftRoles.HeightOf(shape), yaw, order, radius > 0 ? radius : AircraftRoles.RadiusOf(shape), done, owner);
    }

    /// <summary>Server: a convoy was asked for: now and then (<see cref="AirEscortChance"/>, at most once in <see cref="AirEscortEvery"/> s) an escort flies over it.</summary>
    private void SandboxConvoyAir(string style, Vector3 anchor, float heading, int seed)
    {
        if (SandboxNow < _airEscortAt || FactionAirLive >= MaxFactionAir)
            return;
        if (Styles.All.FirstOrDefault(s => s.Name.Equals(style, StringComparison.OrdinalIgnoreCase)) is not { } st || !AircraftRoles.Fields(st))
            return;
        var s = seed > 0 ? seed : VehicleSpawnKit.Next(ref _airSeedState);
        if (VehicleKit.Dice.Of(s).Fork("convoy escort").Next() >= AirEscortChance)
            return;
        _airEscortAt = SandboxNow + AirEscortEvery;
        if (SandboxRequestAir(style, "escort", anchor, s, PilotOrder.Patrol, 0, heading) is { } why)
            Console.WriteLine($"[aircraft] no escort for the {style} convoy: {why}");
    }

    /// <summary>Client (or the host): ask for an aircraft already flying with a bot pilot over you: <paramref name="ask"/> Flying by <paramref name="shapeOrRole"/>'s shape, Faction by its air role (empty: any).</summary>
    public void SandboxSpawnAirborne(AirAsk ask, string style, string shapeOrRole, int seed)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Aircraft).U8((byte)ask).Str(style).Str(shapeOrRole).U32((uint)Math.Max(0, seed));
        if (IsServer)
        {
            if (Local is { } me)
            {
                var r = new PacketReader(w.Span[1..]);
                if (SandboxActGuarded(me, ref r) is { } why)
                    SandboxSay(why);
            }
            return;
        }
        Net.SendToServer(w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: CActKind.Aircraft (after the kind byte): the parts menu's aircraft, launched 20 m ahead of the player and patrolling about that point.</summary>
    private string? SandboxAircraftAct(Player p, ref PacketReader r)
    {
        var ask = (AirAsk)r.U8();
        var style = r.Text(64);
        var what = r.Text(64);
        var seed = (int)Math.Min(r.U32(), (uint)int.MaxValue);
        if (SandboxMenuSpawnRefusal(p) is { } no)
            return no;
        var id = p.Id;
        var over = p.Position + GravityFrames.FlatForward(p.Position, p.Yaw) * 20f;
        var label = $"{style} {(what.Length > 0 ? what : "aircraft")}";
        Action<Construct?, AircraftPilot?, string> told = (c, pilot, why) =>
            SandboxHands.Notices.Add((id, c == null ? $"no {label}: {why}" : pilot == null ? $"the {label} stands on the ground: {why}" : $"the {label} is flying ahead of you: take its seat to fly it"));
        string? refused;
        if (ask == AirAsk.Faction)
            refused = SandboxRequestAir(style, what, over, seed > 0 ? seed : null, PilotOrder.Patrol, 0, p.Yaw, told, id);
        else if (ask == AirAsk.Flying)
            refused = VehicleSpawnKit.Find(style, what) is { } g ? SandboxSpawnAircraft(style, what, seed > 0 ? seed : null, over, AircraftRoles.HeightOf(VehicleSpawnKit.Names(g).Shape),
                p.Yaw, PilotOrder.Patrol, AircraftRoles.RadiusOf(VehicleSpawnKit.Names(g).Shape), told, id) : $"no faction vehicle \"{style} {what}\"";
        else
            refused = "unknown aircraft request";
        if (refused != null)
            return refused;
        SandboxHands.Notices.Add((id, $"making a {style} {(what.Length > 0 ? what : "aircraft")} to fly over you"));
        return null;
    }
}

/// <summary>How the parts menu asks for an aircraft in the air (<see cref="CActKind.Aircraft"/>'s first byte).</summary>
public enum AirAsk : byte
{
    Flying = 1, // a shape of a style, flying with a bot pilot
    Faction = 2, // by air role, through the faction hook (Game.SandboxRequestAir)
}
