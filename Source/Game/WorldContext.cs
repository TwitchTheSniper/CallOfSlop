namespace CallOfSlop;

/// <summary>
/// One world's share of the process-wide simulation state: the map, its collision world, gravity (the map's field, force
/// hooks, gravity parts), the movement code, the gravity setting and the mover clock. Each <see cref="Game"/> holds one
/// (<see cref="Game.Place"/>) and enters it before it ticks or reads packets, so several Games can run in one process on
/// different maps (free travel between planetoids, docs/space/FREE-TRAVEL.md).
/// <para>
/// The roughly 500 call sites that read <see cref="World.Map"/>, <see cref="World.Physics"/> or
/// <see cref="GravityFrames"/> stay as they are: the statics always hold the entered world, and
/// <see cref="World.Enter"/> swaps them. With one Game in a process (every game until now) the swap never happens, so
/// nothing changes. While a context is not entered its fields hold its state; while it is entered they are stale and the
/// statics are the truth.
/// </para>
/// <para>
/// Not swapped, because they are keyed by map or are per-call scratch: posed mover slots and sweep pillars, prop and
/// penetration caches, terrain layer rings, the construct physics' scratch arrays (Games tick one after another, never at
/// once). Contact warm starts already live in each <see cref="ConstructWorld"/>. Client-side visuals (ragdolls, gibs) are
/// shared: <see cref="World.SetMap"/> still clears them, so only the Game being drawn should change map.
/// </para>
/// </summary>
public sealed class WorldContext
{
    /// <summary>The world the statics hold now. At start-up: the process's own (menus, map previews, tools).</summary>
    public static WorldContext Current { get; internal set; } = new() { Name = "process" };

    /// <summary>For logs and the dev check (<c>--two-planetoids</c>).</summary>
    public string Name = "";

    /// <summary>How many times a different world was entered (a dev counter: the swap's cost is a few dozen field copies).</summary>
    public static long Swaps { get; internal set; }

    internal Map Map = null!;
    internal PhysicsWorld? Physics;
    internal IMover Mover = null!;
    internal float GravityScale = 1f;
    internal int GravitySetting = 100;
    internal uint MoverClock;
    internal ConstructWorld? RideBodies, PhysicsBodies;
    internal GravityFrames.Saved Gravity = new();
    internal readonly ConstructGravity.State Parts = new();

    /// <summary>The map this world plays (the live one while entered).</summary>
    public Map MapNow => ReferenceEquals(Current, this) ? World.Map : Map;

    /// <summary>
    /// A new world that starts as a copy of the entered one (same map and collision world, same gravity setting), with its
    /// own gravity hooks and gravity parts. A server Game then loads its own map into it.
    /// </summary>
    public static WorldContext Fork(string name)
    {
        var c = new WorldContext { Name = name };
        World.SaveTo(c);
        c.Gravity = GravityFrames.Copy();
        c.RideBodies = c.PhysicsBodies = null;
        Open.Add(c);
        return c;
    }

    /// <summary>Every world a live Game holds (forked and not yet closed), besides the process's own.</summary>
    internal static readonly List<WorldContext> Open = new(4);

    /// <summary>The Game is gone: its world no longer needs to hear about broken props.</summary>
    public void Close() => Open.Remove(this);

    /// <summary>
    /// A breakable box changed on <paramref name="map"/>: the collision worlds of the worlds not entered that play it (a
    /// host and its own client in one process, as the self-tests run them) hear it too, unless one is <paramref name="skip"/>
    /// (shared with the entered world). <paramref name="index"/> below 0: every box grew back.
    /// </summary>
    internal static void SolidOnOthers(Map map, PhysicsWorld? skip, int index)
    {
        foreach (var c in Open)
            if (!ReferenceEquals(c, Current) && ReferenceEquals(c.Map, map) && c.Physics is { } p && !ReferenceEquals(p, skip) && p.IsFor(map))
            {
                if (index < 0)
                    p.RefreshBoxes();
                else
                    p.UpdateBox(index, map.Solid[index]);
            }
    }
}

public static partial class World
{
    /// <summary>
    /// Makes <paramref name="ctx"/> the world every static reads (<see cref="WorldContext"/>): the left world's state is
    /// kept in its context, the entered one's put back. Free when it is already entered. Main thread only.
    /// </summary>
    public static void Enter(WorldContext ctx)
    {
        var from = WorldContext.Current;
        if (ReferenceEquals(from, ctx))
            return;
        SaveTo(from);
        ConstructGravity.Swap(from.Parts); // (the left world's parts go into its State, the State's spare arrays come out)
        LoadFrom(ctx);
        ConstructGravity.Swap(ctx.Parts);
        WorldContext.Current = ctx;
        WorldContext.Swaps++;
    }

    /// <summary>Copies the live statics into <paramref name="c"/> (its gravity parts are swapped separately).</summary>
    internal static void SaveTo(WorldContext c)
    {
        c.Map = Map;
        c.Physics = _physics;
        c.Mover = Mover;
        c.GravityScale = GravityScale;
        c.GravitySetting = GravitySetting;
        c.MoverClock = MoverPhysics.Clock;
        c.RideBodies = ConstructRide.Bodies;
        c.PhysicsBodies = ConstructPhysics.LastWorld;
        GravityFrames.Save(c.Gravity);
    }

    private static void LoadFrom(WorldContext c)
    {
        Map = c.Map;
        _physics = c.Physics;
        Mover = c.Mover;
        GravityScale = c.GravityScale;
        GravitySetting = c.GravitySetting;
        MoverPhysics.Clock = c.MoverClock;
        ConstructRide.Bodies = c.RideBodies;
        GravityFrames.Load(c.Gravity);
        ConstructPhysics.LastWorld = c.PhysicsBodies;
    }
}

public sealed partial class Game
{
    /// <summary>
    /// Loads a map into this game's own world. Map changes can be started outside <see cref="Update"/> (the lobby room, a
    /// test driving the host between frames), so this enters <see cref="Place"/> first: with a host and its client in
    /// one process, the host's map change must not land in the client's world.
    /// </summary>
    private void SetMapHere(Map map)
    {
        World.Enter(Place);
        World.SetMap(map);
    }
}
