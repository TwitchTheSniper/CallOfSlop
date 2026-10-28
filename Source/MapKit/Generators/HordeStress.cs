namespace CallOfSlop.MapKit.Generators;

/// <summary>
/// Maps/Dev/HordeStress.json: "Horde Stress", the mass horde's stress map (developer request, 2026-10-28, board item
/// dev-horde-stress-map-hs01; docs/zombies/MASS-HORDE.md, "Horde Stress"). A 460 x 400 m valley on terrain, built to
/// watch a horde of up to 100,000 come: the players start on the Overlook, a raised bench on the west side; the far
/// (east) slope rises to a ridge with three staging pads along it, kept apart by low knolls; open ground between, so
/// the whole horde is in view. Each round the hordes wait on the pads for a countdown, pour down the slope to the rally
/// point on the valley floor, merge into one there and advance up the west slope on the players
/// (Source/Game/ZombiesCrowdStress.cs). The host picks the size in the lobby (1,000 to 100,000, or a custom number) and
/// can turn on endless waves.
///
/// Mirror-symmetric about z = 0 (every terrain feature, the pads, the knolls, the spawns and flags) for the multiplayer
/// modes' checks; the only noise is low and broad.
/// </summary>
public sealed class HordeStress : IMapGenerator
{
    public string Name => "Horde Stress";
    public string Pack => "Dev";
    public int Number => 0;

    const double HX = 230, HZ = 200; // half sizes
    const double OverlookX = -175, OverlookY = 17; // the players' raised spot
    const double PadX = 150; // the staging pads' x (on the far slope, under the ridge)
    static readonly double[] PadZ = [-125, 0, 125];
    const string Outer = "#5b5a52";

    public void Build(Kit k)
    {
        k.Describe("Horde stress dev map: a wide valley on terrain. You start on the Overlook; three hordes wait on staging pads along the far ridge, "
                   + "pour down into the valley, merge into one and come up for you. The lobby sets the horde's size (1,000 to 100,000) and endless waves; "
                   + "watch the F9 window, and the log has a summary per size.", order: 988, maxPlayers: 8);
        k.Size(HX, HZ);
        k.Skybox("overcast");
        k.Terrain(Terrain());
        k.Test(new JObject(("maxPlayers", 8), ("minHills", 1), ("minTeamSpawns", 4), ("symmetry", "mirrorZ"), ("walkFairness", 0.12), ("minMainNavPct", 90),
            ("probes", new JArray([JArray.Of("overlook", OverlookX, 0, OverlookY), JArray.Of("rally point", 0, 0, -12)])),
            ("botMatchSeconds", 8), ("spawnsStand", true)));

        k.Ground("#56703f");
        k.Perimeter(6, Outer);

        // The Overlook's lip: low stone kerbs along its valley side with gaps between (places to stand and shoot from).
        foreach (var s in new[] { -1, 1 })
            foreach (var (z0, z1) in new[] { (4.0, 18.0), (26.0, 40.0), (48.0, 62.0) })
                k.Block(OverlookX + 14.6, 0, s * z0, OverlookX + 15.4, 0.7, s * z1, "#8a8274", anchor: "ground");
        // Marker posts on the staging pads and at the rally point (to read distances by from the Overlook).
        foreach (var z in PadZ)
            k.Block(PadX - 0.4, 0, z - 0.4, PadX + 0.4, 4, z + 0.4, "#a33a2a", anchor: "ground");
        k.Block(-0.4, 0, -0.4, 0.4, 3, 0.4, "#c8b048", anchor: "ground");

        // Spawns on the Overlook (four a team, mirrored about z = 0), the hill on it, the flags down the west slope.
        foreach (var x in new[] { -184.0, -180.0, -176.0, -172.0 })
        {
            k.Spawn(0, x, -48);
            k.Spawn(1, x, 48);
        }
        k.Hill(OverlookX, 0, 5);
        k.Flag(-130, -90);
        k.Flag(-130, 90);
        k.AutoWaypoints(12);
        // Waypoints on the objectives (the open ground's grid is coarse) and at the rally point.
        k.Waypoint(OverlookX, 0);
        k.Waypoint(-130, -90);
        k.Waypoint(-130, 90);
        k.Waypoint(0, 0);

        k.Top.Set("zombies", Zombies());
    }

    static JObject Terrain() => new(
        ("cell", 2),
        ("margin", 40),
        ("surface", new JObject(("grass", "#5d7a3e"), ("dirt", "#7a6446"), ("rock", "#7b7770"), ("sand", "#a99a72"), ("sandBelow", -9.5), ("rockSlope", 40))),
        ("features", new JArray([
            new JObject(("type", "noise"), ("amplitude", 0.5), ("scale", 60), ("octaves", 2), ("seed", 17)),
            // The valley along z, its floor 12 m down at x = 0.
            new JObject(("type", "valley"), ("points", JArray.Of(JArray.Of(0, -280), JArray.Of(0, 280))), ("width", 230), ("depth", 12)),
            // The far ridge (east) and the near rise (west).
            new JObject(("type", "ridge"), ("points", JArray.Of(JArray.Of(185, -280), JArray.Of(185, 280))), ("width", 150), ("height", 24)),
            new JObject(("type", "ridge"), ("points", JArray.Of(JArray.Of(-190, -280), JArray.Of(-190, 280))), ("width", 130), ("height", 10)),
            // The Overlook: a raised flat spot on the west rise, gentle sides (the horde comes up them).
            new JObject(("type", "plateau"), ("x", OverlookX), ("z", 0), ("w", 30), ("d", 130), ("height", OverlookY), ("edge", 22), ("material", "dirt")),
            // Knolls between the staging pads, so the three hordes start apart.
            new JObject(("type", "hill"), ("x", PadX + 6), ("z", -62.5), ("radius", 20), ("height", 6)),
            new JObject(("type", "hill"), ("x", PadX + 6), ("z", 62.5), ("radius", 20), ("height", 6)),
            // The staging pads: level ground on the far slope.
            new JObject(("type", "flatten"), ("x", PadX), ("z", PadZ[0]), ("radius", 22), ("edge", 12), ("material", "dirt")),
            new JObject(("type", "flatten"), ("x", PadX), ("z", PadZ[1]), ("radius", 22), ("edge", 12), ("material", "dirt")),
            new JObject(("type", "flatten"), ("x", PadX), ("z", PadZ[2]), ("radius", 22), ("edge", 12), ("material", "dirt")),
            // The rally point: a level floor in the valley.
            new JObject(("type", "flatten"), ("x", 0), ("z", 0), ("radius", 40), ("edge", 20), ("height", -12)),
        ])));

    static JArray P(double x, double z) => JArray.Of(Kit.R(x), Kit.R(z));

    static JObject Zombies() => new(
        ("start", "Field"),
        ("crowd", 2500),
        ("stress", new JObject(
            ("staging", new JArray(PadZ.Select(z => (JNode)P(PadX, z)))),
            ("rally", P(0, 0)),
            ("countdown", 10),
            ("gather", 0.75),
            ("gatherSeconds", 300),
            ("waveSeconds", 60))),
        ("testKit", true),
        ("areas", new JArray([new JObject(("name", "Field"), ("rect", JArray.Of(-HX, -HZ, HX, HZ)))])),
        // (A ground spawner the crowd's promoted zombies are made from; nothing comes out of it on its own.)
        ("spawners", new JArray([new JObject(("area", "Field"), ("at", P(60, 0)))])),
        ("box", new JObject(("cost", 950), ("start", 0), ("spots", new JArray([new JObject(("at", P(OverlookX - 8, 0)), ("yaw", 90.0))])))));
}
