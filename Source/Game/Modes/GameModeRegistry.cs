namespace CallOfSlop;

/// <summary>
/// Every game mode the build knows, in menu order. This is the ONE place a mode is registered: add a factory to
/// <see cref="Factories"/> and the lobby, the server browser, the map vote, the HUD, the score limits and the self-test
/// all pick it up. The <see cref="GameMode"/> ids are the wire format; the order here is only the menu order.
/// </summary>
public static class GameModeRegistry
{
    /// <summary>The registered modes: a factory per mode (a <see cref="Game"/> gets its own instance of each).</summary>
    private static readonly Func<GameModeModule>[] Factories =
    [
        () => new FreeForAllModule(),
        () => new TeamDeathmatchModule(),
        () => new KingOfTheHillModule(),
        () => new CaptureTheFlagModule(),
        () => new GunGameModule(),
        () => new ZombiesModule(),
        () => new CoopModule(), // co-op PvE missions (docs/pve/DESIGN.md)
        () => new TestRangeModule(), // free-for-all with no win condition: the match never ends
        () => new LastManStandingModule(), // the template mode (Dev-only): Source/Game/Modes/README.md
        () => new SandboxModule(), // build and drive constructs (Dev-only, milestone 1): docs/sandbox/CONSTRUCTS.md
    ];

    /// <summary>A reference instance per mode, for everything that needs a mode's identity or rules without a game (menus, the server browser, score limits).</summary>
    private static readonly GameModeModule[] _prototypes = Build();

    private static readonly Dictionary<GameMode, GameModeModule> _byId = _prototypes.ToDictionary(m => m.Id);

    private static GameModeModule[] Build()
    {
        var list = Factories.Select(f => f()).ToArray();
        var ids = new HashSet<GameMode>();
        foreach (var m in list)
            if (!ids.Add(m.Id))
                throw new InvalidOperationException($"game mode id {m.Id} is registered twice ({m.Name})");
        return list;
    }

    /// <summary>Every registered mode (prototype instances: identity and rules only), in menu order.</summary>
    public static IReadOnlyList<GameModeModule> All => _prototypes;

    /// <summary>The modes players see in menus (not the Dev-only ones), in menu order.</summary>
    public static IEnumerable<GameModeModule> Shipped => _prototypes.Where(m => !m.DevOnly);

    /// <summary>Is this byte a registered mode? (Packets and settings files carry modes as bytes.)</summary>
    public static bool IsKnown(GameMode id) => _byId.ContainsKey(id);

    /// <summary>The reference instance of a mode (free-for-all for an unknown id).</summary>
    public static GameModeModule Get(GameMode id) => _byId.TryGetValue(id, out var m) ? m : _byId[GameMode.FreeForAll];

    /// <summary>A fresh instance of every mode for one game, by id.</summary>
    public static Dictionary<GameMode, GameModeModule> CreateAll()
    {
        var d = new Dictionary<GameMode, GameModeModule>();
        foreach (var f in Factories)
        {
            var m = f();
            d[m.Id] = m;
        }
        return d;
    }

    /// <summary>
    /// The modes a map can be played in, in menu order: the ones that fit it (<see cref="GameModeModule.Fits"/>), plus the
    /// Dev-only ones when the map comes from a dev pack (one kept out of True Random). <paramref name="map"/> null: a
    /// random map, so only modes every map plays.
    /// </summary>
    public static IEnumerable<GameModeModule> ForMap(Map? map)
    {
        var dev = map?.Pack is { InTrueRandom: false };
        return _prototypes.Where(m => m.Fits(map) && (!m.DevOnly || dev));
    }
}
