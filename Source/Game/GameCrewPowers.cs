using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The game's side of the faction controllers (docs/sandbox/design/faction-controllers.md phase 3; the minds are Source/Crew/CrewControllers.cs):
/// each power's ships are set down at its home out of everyone's sight by the vehicle spawner (its blueprints, crewed by that power), dock there
/// when their mission is done, and the controllers' news joins the powers' news. On in survival worlds; <c>--powers on|off</c> overrides for a run.
/// The minds see the world through this (people, ground, crews' sight). Server only; nothing allocated in a tick but a request or a log line.
/// </summary>
public sealed partial class Game : IPowerHost
{
    /// <summary>The powers' controllers (server).</summary>
    public readonly PowerControllers Controllers = new();

    /// <summary>A run's override (<c>--powers on|off</c>; null: on in survival only).</summary>
    public static bool? PowersForce;

    /// <summary>Controllers field ships in this game: the server, powers read, Sandbox, survival (or forced).</summary>
    public bool PowersActive => IsServer && CrewStandingOn && SandboxOn && (PowersForce ?? !SandboxInfiniteParts);

    private void CrewPowersClear()
    {
        Controllers.Rules = ControllerRules.Current; // (Crew/Controllers.json and every plan's bills read here, at a world's start, never in a tick)
        Controllers.Reset();
        var rel = Relations;
        rel.Might = Controllers.Might;
        rel.Merge = (a, b) => Controllers.MergeInto(Crew, Constructs, Relations, a, b);
    }

    /// <summary>A tick, after the crews': the controllers think in turn; their news goes out with the powers'.</summary>
    private void CrewPowersTick()
    {
        if (!PowersActive)
            return;
        var rel = Relations;
        rel.Might ??= Controllers.Might;
        rel.Merge ??= (a, b) => Controllers.MergeInto(Crew, Constructs, Relations, a, b);
        Controllers.Step(Crew, Constructs, rel, this);
        if (Controllers.Log.Count > 0)
        {
            foreach (var line in Controllers.Log)
                Console.WriteLine($"[powers] {line}");
            Controllers.Log.Clear();
        }
        if (Controllers.News.Count > 0)
        {
            foreach (var line in Controllers.News)
                rel.Tell(line);
            Controllers.News.Clear();
        }
        var gone = (ushort)0;
        for (var a = 1; a <= CrewPowers.Count && a <= 16; a++)
            if (rel.Gone[a])
                gone |= (ushort)(1 << (a - 1));
        if (FactionTable.PowerGone != gone)
        {
            FactionTable.PowerGone = gone;
            FactionTable.Version++;
        }
    }

    int IPowerHost.People(Span<Vector3> into)
    {
        var n = 0;
        foreach (var p in Players.Values)
            if (p.Alive && p.Bot == null && n < into.Length)
                into[n++] = p.Position;
        return n;
    }

    string IPowerHost.WorldKey => World.Map.Name;

    Vector3 IPowerHost.Ground(Vector3 at) => ConstructWorld.Ground(World.Query, at);

    bool IPowerHost.Spawn(byte power, int ship, ConstructBuild.Blueprint bp, Vector3 at, float yaw)
    {
        if (VehicleSpawn.RequestBlueprint(bp, at, yaw, 255, 255, (c, _, why, _) => Controllers.Spawned(Crew, power, ship, c, why), power) is { } busy) // (a ship bought: rare)
        {
            Console.WriteLine($"[powers] {CrewPowers.Of(power)?.Name}'s {bp.Name} waits: {busy}");
            return false;
        }
        return true;
    }

    void IPowerHost.Dock(Construct c)
    {
        if (Crew.ShipOf(c.Id) is { } s)
            Crew.Disband(s, Constructs, this, "docked at home");
        Constructs.Remove(c);
    }

    bool IPowerHost.Sees(byte power, Vector3 at) => ((IPowerSight)this).Sees(power, at);

    /// <summary>The controllers for a save (null: none run here).</summary>
    private byte[]? ControllersBytes() => CrewStandingOn && IsServer ? Controllers.SaveBytes() : null;

    /// <summary>A saved world's Controllers section (WorldSaveWriter.ControllersTag), after its Powers section.</summary>
    private void WorldReadControllers(SaveReader r)
    {
        var bytes = r.Bytes(r.I32());
        if (!CrewStandingOn)
            return;
        Controllers.Load(Relations, bytes);
        foreach (var line in Controllers.Log)
            Console.WriteLine($"[powers] {line}");
        Controllers.Log.Clear();
        _relShown = -1;
    }
}
