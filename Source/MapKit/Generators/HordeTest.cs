namespace CallOfSlop.MapKit.Generators;

/// <summary>
/// Maps/Dev/HordeTest.json: "Horde Test", a Zombies dev map for seeing and testing big hordes quickly (developer request,
/// 2026-10-27: "Getting a high round in zombies takes a lot of time"; docs/zombies/ZOMBIES-MODE.md, "Horde Test").
///
/// A 48 x 48 m walled arena (the start area) with fifteen boarded windows in its south, east and west walls and two
/// annexes on its north side (the Power Annex behind a door, the Armoury behind debris) with three windows each: 21
/// windows, two spawners behind each, so 400 zombies can come in from every side. Outside the walls a wide open strip
/// runs right round the map, so the horde controller's queues, splits and flanks are easy to watch from inside: the
/// raised dais in the middle (1.6 m, stairs east and west) and two railed lookout towers (3.2 m) either side of it see
/// over the 3 m walls. Every perk machine stands in a row along the arena's north wall; the box, a workbench, four wall
/// buys and a classic slot machine are in the arena, the power switch, the slot upgrade and a second workbench in the
/// Power Annex, five category slot machines and a box spot in the Armoury. Everything is a solid machine prop.
///
/// Mirror-symmetric about z = 0 for the multiplayer modes' checks (team spawns, flags; the hill is on the dais). The
/// section's "testKit" lets the host's Test kit setting hand out every perk and a strong late build (Lobby: Zombies).
/// </summary>
public sealed class HordeTest : IMapGenerator
{
    public string Name => "Horde Test";
    public string Pack => "Dev";
    public int Number => 0; // Dev maps carry no number: Maps/Dev/HordeTest.json

    const double A = 24.0; // the arena's inner half size
    const double T = 0.3; // wall thickness
    const double WallH = 3.0;
    const double AnnexZ = 29.5; // the annexes' north wall (inner face)
    const double DaisH = 1.6, TowerH = 3.2;
    static readonly double[] Windows = [-16, -8, 0, 8, 16]; // along the south, east and west walls
    static readonly double[] NorthWindows = [-19, -12, -5, 5, 12, 19]; // the annexes' north wall
    const double SillLo = 0.9, SillHi = 2.1, WinW = 1.6;

    const string Floor = "#4b4a45", Wall = "#76705f", WallB = "#6a6656", Annex = "#5f6b5a", Armoury = "#5a5f6b", Stone = "#8a8070", Deck = "#7d7466", Outer = "#3c3a36";

    public void Build(Kit k)
    {
        k.Describe("Zombies dev map for big hordes: a walled arena with 21 boarded windows (two spawners behind each) and an open strip right round it, "
                   + "a raised dais and two lookout towers to watch the horde queue, split and flank from, every perk in a row, the box, workbenches, wall buys, "
                   + "slot machines and the power. Pair it with the lobby's Start round and Test kit settings.", order: 986, maxPlayers: 8);
        k.Size(33, 35.5);
        k.Top.Set("sky", "#56606e");
        k.Test(new JObject(("maxPlayers", 8), ("minHills", 1), ("minTeamSpawns", 4), ("symmetry", "mirrorZ"), ("walkFairness", 0.12), ("minMainNavPct", 40),
            ("minSolidProps", 20), ("requireProps", JArray.Of("Mystery Box Crate", "Zombies Workbench", "Power Switch Box", "Weapon Locker", "Reel Cabinet")),
            ("probes", new JArray([JArray.Of("dais", 0, 0, DaisH), JArray.Of("west tower", -14, 0, TowerH), JArray.Of("east tower", 14, 0, TowerH)])),
            ("botMatchSeconds", 8), ("spawnsStand", true)));

        k.Ground(Floor);
        k.Perimeter(3.5, Outer);

        // The arena's walls: windows south, east and west; the north wall has the door and the debris into the annexes.
        k.WallX(-A - T, A + T, -A - T, T, 0, WallH, Wall, Windows.Select(x => Kit.Window(x, WinW, SillLo, SillHi)).ToArray());
        k.WallZ(-A - T, A + T, A, T, 0, WallH, Wall, Windows.Select(z => Kit.Window(z, WinW, SillLo, SillHi)).ToArray());
        k.WallZ(-A - T, A + T, -A - T, T, 0, WallH, Wall, Windows.Select(z => Kit.Window(z, WinW, SillLo, SillHi)).ToArray());
        k.WallX(-A - T, A + T, A, T, 0, WallH, WallB, Kit.Door(-12, 2.4, 2.6), Kit.Door(12, 2.4, 2.6));

        // The annexes: west (Power Annex) and east (Armoury), split at x = 0, three windows each in the north wall.
        k.WallX(-A - T, A + T, AnnexZ, T, 0, WallH, Annex, NorthWindows.Select(x => Kit.Window(x, WinW, SillLo, SillHi)).ToArray());
        k.WallZ(A + T, AnnexZ + T, -A - T, T, 0, WallH, Annex);
        k.WallZ(A + T, AnnexZ + T, A, T, 0, WallH, Armoury);
        k.WallZ(A + T, AnnexZ, -T, T, 0, WallH, WallB);

        // The dais in the middle: stairs east and west (the north and south lanes stay open).
        k.Block(-5, 0, -5, 5, DaisH, 5, Stone);
        k.StairsX(-1.2, 1.2, 5 + 4 * Map.StepRun, -1, 0, DaisH, Stone);
        k.StairsX(-1.2, 1.2, -5 - 4 * Map.StepRun, 1, 0, DaisH, Stone);

        // Two lookout towers either side of the dais, stairs facing it, railed round the top.
        foreach (var side in new[] { -1, 1 })
        {
            double x0 = side * 12, x1 = side * 16;
            k.Block(Math.Min(x0, x1), 0, -2, Math.Max(x0, x1), TowerH, 2, Deck);
            k.StairsX(-1.0, 1.0, x0 - side * 8 * Map.StepRun, side, 0, TowerH, Deck);
            Structures.Railing(k, x1, -2, TowerH, x1, 2);
            Structures.Railing(k, x0, -2, TowerH, x1, -2);
            Structures.Railing(k, x0, 2, TowerH, x1, 2);
            Structures.Railing(k, x0, -2, TowerH, x0, 2, gaps: [(2.0, 1.1)]);
        }

        // A few low crates in the outside strip to read distances by (clear of every window's lane).
        foreach (var (x, z) in new[] { (-29.0, -31.0), (29.0, -31.0), (-29.0, 32.0), (29.0, 32.0) })
            k.Block(x - 0.6, 0, z - 0.6, x + 0.6, 0.9, z + 0.6, "#5a4632");

        // Spawns: four a team, mirrored about z = 0 (Zombies puts everyone in the arena by id).
        foreach (var x in new[] { -6.0, -2.5, 2.5, 6.0 })
        {
            k.Spawn(0, x, -9);
            k.Spawn(1, x, 9);
        }
        k.Hill(19.5, 0, 2.5);
        k.Flag(0, -15);
        k.Flag(0, 15);
        k.AutoWaypoints(4);

        k.Top.Set("zombies", Zombies());
    }

    static JArray P(double x, double z) => JArray.Of(Kit.R(x), Kit.R(z));

    static JObject Spot(double x, double z, double yaw, params (string Key, object? Value)[] more)
    {
        var o = new JObject(("at", P(x, z)), ("yaw", yaw));
        foreach (var (key, v) in more)
            o.Add(key, v);
        return o;
    }

    static JObject Zombies()
    {
        var windows = new JArray();
        var spawners = new JArray();
        void Window(string area, double[] box, (double X, double Z) outside, (double X, double Z) inside, (double X, double Z) s1, (double X, double Z) s2)
        {
            var i = windows.Count;
            windows.Add(new JObject(("area", area), ("box", JArray.Of(box.Select(v => (object?)Kit.R(v)).ToArray())), ("outside", P(outside.X, outside.Z)),
                ("inside", P(inside.X, inside.Z)), ("boards", 6)));
            spawners.Add(new JObject(("window", i), ("at", P(s1.X, s1.Z))));
            spawners.Add(new JObject(("window", i), ("at", P(s2.X, s2.Z))));
        }
        const double h = WinW / 2;
        foreach (var x in Windows) // south
            Window("Arena", [x - h, SillLo, -A - T, x + h, SillHi, -A], (x, -A - 1.4), (x, -A + 0.9), (x - 1.5, -29), (x + 1.5, -32.5));
        foreach (var z in Windows) // east
            Window("Arena", [A, SillLo, z - h, A + T, SillHi, z + h], (A + 1.4, z), (A - 0.9, z), (28, z - 1.5), (31, z + 1.5));
        foreach (var z in Windows) // west
            Window("Arena", [-A - T, SillLo, z - h, -A, SillHi, z + h], (-A - 1.4, z), (-A + 0.9, z), (-28, z + 1.5), (-31, z - 1.5));
        foreach (var x in NorthWindows) // the annexes, north
            Window(x < 0 ? "Power Annex" : "Armoury", [x - h, SillLo, AnnexZ, x + h, SillHi, AnnexZ + T], (x, AnnexZ + 1.4), (x, AnnexZ - 0.9), (x - 1.2, 31.5), (x + 1.2, 33));
        spawners.Add(new JObject(("area", "Power Annex"), ("at", P(-16, 27))));
        spawners.Add(new JObject(("area", "Armoury"), ("at", P(16, 25.5))));

        string[] perks = ["revive", "secondwind", "hammerhand", "bloodroot", "salvage", "jugg", "speed", "doubletap", "fleetfoot", "marksman", "windfall", "bulwark"];
        string[] powered = ["jugg", "speed", "doubletap", "fleetfoot", "marksman", "windfall", "bulwark"];
        var perkRow = new JArray();
        for (var i = 0; i < perks.Length; i++)
        {
            var o = new JObject(("perk", perks[i]), ("at", P(-7.15 + 1.3 * i, A - 0.5)), ("yaw", 0));
            if (powered.Contains(perks[i]))
                o.Add("power", true);
            perkRow.Add(o);
        }

        var slots = new JArray([Spot(-A + 0.45, -4, 90)]);
        string[] cats = ["Receiver", "Barrel", "Ammo", "Optic", "Upgrade"];
        for (var i = 0; i < cats.Length; i++)
            slots.Add(Spot(A - 0.45, A + T + 0.7 + 0.95 * i, -90, ("category", cats[i])));

        return new JObject(
            ("start", "Arena"),
            ("machineProps", true),
            ("testKit", true),
            ("areas", new JArray([
                new JObject(("name", "Arena"), ("rect", JArray.Of(-A, -A, A, A))),
                new JObject(("name", "Power Annex"), ("rect", JArray.Of(-A, A + T, -T, AnnexZ))),
                new JObject(("name", "Armoury"), ("rect", JArray.Of(0, A + T, A, AnnexZ))),
            ])),
            ("doors", new JArray([
                new JObject(("name", "the Power Door"), ("kind", "door"), ("cost", 750), ("box", JArray.Of(-13.2, 0, A, -10.8, 2.6, Kit.R(A + T))), ("areas", JArray.Of("Arena", "Power Annex"))),
                new JObject(("name", "the Armoury Debris"), ("kind", "debris"), ("cost", 1000), ("box", JArray.Of(10.8, 0, Kit.R(A - 0.15), 13.2, 2.6, Kit.R(A + T + 0.15))), ("areas", JArray.Of("Arena", "Armoury"))),
            ])),
            ("windows", windows),
            ("spawners", spawners),
            ("wallBuys", new JArray([
                new JObject(("build", "Wall Rifle"), ("at", P(-A + 0.03, -12)), ("yaw", 90), ("cost", 500), ("ammoCost", 250)),
                new JObject(("build", "Wall SMG"), ("at", P(-12, -A + 0.03)), ("yaw", 180), ("cost", 1000)),
                new JObject(("build", "Wall Pump"), ("at", P(12, -A + 0.03)), ("yaw", 180), ("cost", 900)),
                new JObject(("build", "Burst Marksman"), ("at", P(A - 0.03, -12)), ("yaw", -90), ("cost", 1200)),
                new JObject(("build", "Coach Gun"), ("at", P(0.03, 27)), ("yaw", 90), ("cost", 1500)),
            ])),
            ("box", new JObject(("cost", 950), ("start", 0), ("spots", new JArray([Spot(-9, -11, 180), Spot(9, 11, 0), Spot(8, 27.5, 180)])))),
            ("workbenches", new JArray([Spot(9, -11, 180), Spot(-8, A + T + 0.9, 180)])),
            ("perks", perkRow),
            ("slotUpgrade", Spot(-T - 0.35, 27, -90, ("cost", 4000))),
            ("power", Spot(-A + 0.35, 27, 90)),
            ("salvage", new JArray([new JObject(("at", P(-2.5, 26.5))), new JObject(("at", P(15.5, 27.5)))])),
            ("slots", slots));
    }
}
