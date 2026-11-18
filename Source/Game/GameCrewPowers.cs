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
        Controllers.Territory = TerritoryRules.Current; // (phase 4: Crew/Territory.json, the planets' key sites)
        Controllers.Reset();
        var rel = Relations;
        (rel.Might, rel.Merge) = (null, null); // (trade, tribute, blood feuds and merging only where the controllers run: CrewPowersTick)
        Crew.LivePower ??= PowerLive;
        _goneSeen = 0;
    }

    private ushort _goneSeen;

    /// <summary>The power a crew of <paramref name="p"/> serves now: itself while it stands; gone, its heir (merged into), else another of its kind standing, else none (0).</summary>
    public byte PowerLive(byte p)
    {
        var rel = _rel;
        for (var hops = 0; hops < PowerRelations.N && p != 0 && p <= PowerRelations.N && rel.Gone[p]; hops++)
        {
            var heir = rel.MergedInto[p];
            if (heir == 0)
            {
                var kind = CrewPowers.KindOf(p);
                for (var q = (byte)1; q <= CrewPowers.Count; q++)
                    if (!rel.Gone[q] && CrewPowers.KindOf(q) == kind)
                        return q;
                return 0;
            }
            p = heir;
        }
        return p;
    }

    /// <summary>A power newly gone (fallen or merged, or read so from a save): every crew and group still of it serves its heir now (or nobody).</summary>
    private void CrewPowersGone(byte a)
    {
        var to = PowerLive(a);
        for (var i = 0; i < Crew.ShipHigh; i++)
            if (Crew.Ships[i] is { } s && s.Faction == a && Constructs.Get(s.Body) is { } c)
            {
                s.Faction = to;
                c.NpcFaction = s.Own ? (byte)0 : to;
                for (var j = 0; j < s.MemberCount; j++)
                    if (Crew.Fac[s.Members[j]] == a)
                        Crew.Fac[s.Members[j]] = to;
                s.SeatsDirty = true;
                if (to == 0 && s.Group >= 0)
                    Crew.Fleet.Leave(Crew, s);
            }
        foreach (var g in Crew.Fleet.Groups)
            if (g.Live && g.Power == a)
                g.Power = to;
        Console.WriteLine($"[powers] {CrewPowers.Of(a)?.Name} is gone: its crews serve {(to != 0 ? CrewPowers.Of(to)?.Name : "nobody")} now");
    }

    /// <summary>A tick, after the crews': the controllers think in turn; their news goes out with the powers'.</summary>
    private void CrewPowersTick()
    {
        var rel = Relations;
        var gone = (ushort)0;
        for (var a = 1; a <= CrewPowers.Count && a <= 16; a++)
            if (rel.Gone[a])
                gone |= (ushort)(1 << (a - 1));
        if (gone != _goneSeen)
        {
            for (var a = 1; a <= 16; a++)
                if ((gone >> (a - 1) & 1) != 0 && (_goneSeen >> (a - 1) & 1) == 0)
                    CrewPowersGone((byte)a); // (rare)
            _goneSeen = gone;
        }
        if (FactionTable.PowerGone != gone)
        {
            FactionTable.PowerGone = gone;
            FactionTable.Version++;
        }
        if (!PowersActive)
        {
            (rel.Might, rel.Merge) = (null, null); // (the controllers' politics stay off in creative)
            return;
        }
        rel.Might ??= Controllers.Might;
        rel.Merge ??= (a, b) => Controllers.MergeInto(Crew, Constructs, Relations, a, b);
        Controllers.Step(Crew, Constructs, rel, this);
        CrewTerritoryTick(); // (each player's view of the map, once a second: GameCrewTerritory.cs)
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
    }

    int IPowerHost.People(Span<Vector3> into) => InterestPoints(into);

    /// <summary>Where people see from: each living player, and the drone a player flies from their seat (the same point their poses centre on,
    /// <see cref="SandboxInterestAt"/>). The far bodies' rate, the controllers' sight and the far layer's materialising and collapsing all use these.</summary>
    public int InterestPoints(Span<Vector3> into)
    {
        var n = 0;
        foreach (var p in Players.Values)
        {
            if (!p.Alive || p.Bot != null || n >= into.Length)
                continue;
            into[n++] = p.Position;
            var at = SandboxInterestAt(p);
            if (n < into.Length && Vector3.DistanceSquared(at, p.Position) > 1)
                into[n++] = at;
        }
        return n;
    }

    string IPowerHost.WorldKey => World.Map.Name;

    bool IPowerHost.Ground(Vector3 at, out Vector3 ground)
    {
        if (World.Map.Planetoid != null)
        {
            ground = ConstructWorld.Ground(World.Query, at);
            return !ConstructWorld.FellOut(ground, World.Query);
        }
        ground = at with { Y = 0 };
        if (!World.Query.Raycast(new Vector3(at.X, 200, at.Z), -Vector3.UnitY, 400, new QueryFilter(QueryLayers.Static), out var hit))
            return false; // (off the map)
        ground = hit.Point;
        return !ConstructWorld.FellOut(ground, World.Query);
    }

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

    bool IPowerHost.PersonAt(byte player, out Vector3 at)
    {
        at = default;
        if (!Players.TryGetValue(player, out var p) || !p.Alive)
            return false;
        at = p.Position;
        return true;
    }

    double IPowerHost.SpaceSeconds => Space is { } s ? s.Now.Seconds : -1;

    double IPowerHost.PlanetGap(string a, string b)
    {
        if (Space is not { } s)
            return -1;
        var sys = s.System;
        int i = sys.IndexOf(a), j = sys.IndexOf(b);
        if (i < 0 || j < 0)
            return -1;
        var now = s.Now;
        return (sys.PositionOf(i, now) - sys.PositionOf(j, now)).Length;
    }

    bool IPowerHost.SiteAt(string planet, Vector2 at, out Vector3 world)
    {
        world = default;
        if (!planet.Equals(World.Map.Name, StringComparison.OrdinalIgnoreCase))
            return false;
        if (World.Map.Planetoid is { } pl)
        {
            var (lat, lon) = (at.X * MathF.PI / 180, at.Y * MathF.PI / 180);
            var dir = new Vector3(MathF.Cos(lat) * MathF.Cos(lon), MathF.Sin(lat), MathF.Cos(lat) * MathF.Sin(lon));
            world = pl.Center + dir * pl.SurfaceRadius(dir, out _);
            if (((IPowerHost)this).Ground(world + dir * 2, out var ground))
                world = ground;
            return true;
        }
        return ((IPowerHost)this).Ground(new Vector3(at.X, 0, at.Y), out world);
    }

    /// <summary>A window run's look (<c>crewpowers [near]</c>): near lays every power's places again close round the first person and sends its convoy now; then the books,
    /// ships and missions, and whether the section's bytes read back the same.</summary>
    public string CrewPowersLine(string arg)
    {
        if (arg.Trim().Equals("near", StringComparison.OrdinalIgnoreCase))
        {
            Controllers.PlacesNear = 300; // (ships set down and docked 180 m from people then) // (a test's: places laid again at each one's next think, 300-450 m out)
            foreach (var p in Controllers.Powers)
                (p.Started, p.NextConvoy) = (false, 0);
        }
        var back = new PowerControllers { Rules = Controllers.Rules };
        var bytes = Controllers.SaveBytes();
        back.Load(new PowerRelations(), bytes);
        var same = back.SaveBytes().AsSpan().SequenceEqual(bytes);
        return $"active {PowersActive}, bodies {Controllers.Bodies()}, {Controllers.Thinks} thinks, {Controllers.CpuMs / Math.Max(1, Controllers.Ticks) * 1000:0.0} us a tick, {Controllers.Bytes} bytes (parts in ms: {string.Join(" ", Controllers.PartMs.Select(x => x.ToString("0.0")))}; worst {Controllers.WorstMs:0.00} ms); save {bytes.Length} bytes, reads back {(same ? "the same" : "DIFFERENT")}\n{Controllers.Text(Crew)}";
    }

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
