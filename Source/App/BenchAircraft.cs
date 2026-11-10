using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

/// <summary>
/// <c>--bench-aircraft [STYLE] [--seed N] [--height H] [--seconds S] [--map NAME]</c>: the AI aircraft by hand (docs/vehiclekit/AIRCRAFT.md, "Air spawn" and "Bot pilots").
/// A command-line probe, not a self-test section: it fails nothing. An offline Sandbox host; a Drone, a Plane and a Helicopter of the style (Bandit by default) made through
/// the real dynamic spawn: first one of each on the ground (it must stand), then each launched H m up (40 by default) on a patrol, flown for S seconds (20) with a line every
/// 2 s, then the drone and the helicopter told to return and land (up to 30 s). Prints what the pilots' ticks cost and allocated.
/// </summary>
public static partial class SelfTest
{
    public static int BenchAircraft(string[] args)
    {
        string? Arg(string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var style = args.SkipWhile(a => !a.Equals("--bench-aircraft", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } first && !first.StartsWith("--", StringComparison.Ordinal) ? first : "Bandit";
        var seed = int.TryParse(Arg("--seed"), out var sd) ? sd : 7;
        var height = float.TryParse(Arg("--height"), out var h) ? h : 40;
        var seconds = float.TryParse(Arg("--seconds"), out var s) ? Math.Clamp(s, 2, 60) : 20;
        var map = Maps.Find(Arg("--map") ?? "Proving Ground") ?? Maps.All.First(m => m.Terrain != null);
        map.RestoreProps();
        World.SetMap(map);
        using var g = new Game(NetPeer.Offline(), new Audio(), "Bench", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: (byte)Array.IndexOf(Maps.All, map)) { MapVoteEnabled = false };
        g.Mode = GameMode.Sandbox;
        var now = 0.0;
        Run(g, ref now, Game.CountdownSeconds + 0.5);
        Console.WriteLine($"[air bench] {map.Name}, phase {g.Phase}, server {g.IsServer}");
        var at = Vector3.Zero; // (the middle of the map: the patrols stay over its ground)
        string[] shapes = ["Drone", "Plane", "Helicopter"];

        // 1. On the ground, through the real spawn path.
        var made = new Construct?[shapes.Length];
        for (var k = 0; k < shapes.Length; k++)
        {
            var i = k;
            var why = g.SandboxSpawnAircraft(style, shapes[k], seed, at + new Vector3(-30 + 30 * k, 0, 80), 0, 0, done: (c, _, w) => { made[i] = c; if (c == null) Console.WriteLine($"[air bench] {shapes[i]} on the ground: not made: {w}"); });
            if (why != null)
                Console.WriteLine($"[air bench] {shapes[k]} refused: {why}");
        }
        AbWaitSpawns(g, ref now);
        Run(g, ref now, 3);
        for (var k = 0; k < shapes.Length; k++)
            if (made[k] is { } c)
                Console.WriteLine($"[air bench] ground {shapes[k]}: construct {c.Id}, {c.PartCount} parts, {c.Mass:0} kg, tilt {AbTilt(c):0.0} deg, speed {c.Vel.Length():0.00} m/s, stabilizer {(AircraftAutopilot.Stab(c) >= 0 ? "yes" : "NO")}");
        foreach (var c in made)
            if (c != null)
                g.Constructs.Remove(c);

        // 2. In the air, on a patrol.
        var air = new (Construct? C, AircraftPilot? P)[shapes.Length];
        for (var k = 0; k < shapes.Length; k++)
        {
            var i = k;
            var over = at + new Vector3(-110 + 110 * k, 0, -60);
            var why = g.SandboxSpawnAircraft(style, shapes[k], seed, over, height, 0, PilotOrder.Patrol, 40, (c, p, w) => { air[i] = (c, p); if (c == null) Console.WriteLine($"[air bench] {shapes[i]} in the air: not made: {w}"); });
            if (why != null)
                Console.WriteLine($"[air bench] {shapes[k]} refused: {why}");
        }
        AbWaitSpawns(g, ref now);
        var start = new float[shapes.Length];
        for (var k = 0; k < shapes.Length; k++)
            start[k] = air[k].C is { } c ? c.WorldCom.Y : 0;
        var low = new float[shapes.Length];
        Array.Fill(low, float.MaxValue);
        var worst = new float[shapes.Length];
        var (cpu0, bytes0, ticks0) = (g.PilotsCpuMs, g.PilotsBytes, g.PilotsTicks);
        for (var t = 0f; t < seconds; t += 2)
        {
            for (var n = 0; n < 2 * World.TickRate; n++)
            {
                Run(g, ref now, 1.0 / World.TickRate);
                if (t == 0 && n is 1 or 15 or 30 or 60 or 90)
                    for (var k = 0; k < shapes.Length; k++)
                        if (air[k].C is { } c)
                            Console.WriteLine($"[air bench] launch {shapes[k]} tick {n}: {c.WorldCom.Y - start[k]:0.00} m, climbing {c.Vel.Y:0.0} m/s, {c.Vel.Length():0.0} m/s, tilt {AbTilt(c):0}, engine {(c.EngineRunning ? "on" : "off")} {c.Rpm:0} rpm, rotors {AbRotors(c)}");
                for (var k = 0; k < shapes.Length; k++)
                    if (air[k].C is { } c)
                    {
                        low[k] = MathF.Min(low[k], c.WorldCom.Y - start[k]);
                        worst[k] = MathF.Max(worst[k], AbTilt(c));
                    }
            }
            for (var k = 0; k < shapes.Length; k++)
                if (air[k] is { C: { } c, P: { } p })
                    Console.WriteLine($"[air bench] {t + 2:0} s {shapes[k]}: {p.Line(g.Constructs)}, tilt {AbTilt(c):0}, at ({c.WorldCom.X:0}, {c.WorldCom.Y:0}, {c.WorldCom.Z:0}), {(g.Constructs.Get(c.Id) == null ? "REMOVED" : "")}{(g.Pilots.Contains(p) ? "" : " (pilot gone)")}");
        }
        for (var k = 0; k < shapes.Length; k++)
            if (air[k].C != null)
                Console.WriteLine($"[air bench] {shapes[k]}: lowest {low[k]:0.0} m against its launch height, worst tilt {worst[k]:0}");
        var ticks = g.PilotsTicks - ticks0;
        Console.WriteLine($"[air bench] pilots' ticks: {(g.PilotsCpuMs - cpu0) / Math.Max(1, ticks) * 1000:0.0} us a tick for {g.Pilots.Count} pilots, {g.PilotsBytes - bytes0} bytes allocated over {ticks} ticks");

        // 3. Home and down (rotorcraft).
        for (var k = 0; k < shapes.Length; k++)
            if (air[k] is { C: { } c, P: { Plane: false } p })
                p.Give(c, g.Constructs, PilotOrder.Return, p.Home);
        for (var t = 0f; t < 30 && air.Any(a => a.P is { Plane: false, Landed: false } p && g.Pilots.Contains(p)); t += 2)
        {
            Run(g, ref now, 2);
            for (var k = 0; k < shapes.Length; k++)
                if (air[k] is { C: { } c, P: { Plane: false } p })
                    Console.WriteLine($"[air bench] {t + 2:0} s {shapes[k]} home: {p.Line(g.Constructs)}, {AbFlat(c.WorldCom - p.Home):0} m from home");
        }
        return 0;
    }

    private static string AbRotors(Construct c)
    {
        var most = 0f;
        var least = float.MaxValue;
        for (var k = 0; k < c.RotorCount; k++)
            if (c.Parts[c.Rotors[k].Part].D.Rotor is { } d)
            {
                most = MathF.Max(most, c.Rotors[k].Omega / d.RatedOmega);
                least = MathF.Min(least, c.Rotors[k].Omega / d.RatedOmega);
            }
        return c.RotorCount == 0 ? "none" : $"{c.RotorCount} at {least:P0} to {most:P0} of rated";
    }

    private static float AbFlat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    private static float AbTilt(Construct c) => MathF.Acos(Math.Clamp(Vector3.Transform(Vector3.UnitY, c.Rot).Y, -1, 1)) * 180 / MathF.PI;

    private static void AbWaitSpawns(Game g, ref double now)
    {
        for (var t = 0; t < 60 * 120 && g.VehicleSpawn.Pending > 0; t++)
            Run(g, ref now, 1.0 / World.TickRate);
        Console.WriteLine($"[air bench] spawns done ({g.VehicleSpawn.Generated} generated, {g.VehicleSpawn.Failed} failed, {g.VehicleSpawn.FellBack} fell back)");
    }
}
