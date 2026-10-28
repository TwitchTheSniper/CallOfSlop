namespace CallOfSlop.MapKit.Generators;

/// <summary>
/// Maps/Dev/LightTest.json: "Light Test", the baked lighting dev map (developer request, 2026-10-27; docs/render/LIGHTING.md,
/// Maps/README.md "Lighting"). A dusk-lit stone hall built with the kit's light helpers (docs/mapkit/MAPKIT.md "Lights"):
/// <list type="bullet">
/// <item>The hall: pillars, an iron chandelier, wall torches down both long walls, candle stands and altar candles round a
/// spot-lit altar, and blue and red "stained glass" area lights in the west windows, where the low sun also shines in.</item>
/// <item>The side room through the east door: a fireplace and a hanging lantern, nothing else (falloff and shadows).</item>
/// <item>Outside: a brazier by the south door, a street lamp and a heritage lamp post (props that carry their own lights).</item>
/// </list>
/// The map's "test" block checks the light at a few spots (lightProbes), so the Map contract notices if the bake goes dark.
/// </summary>
public sealed class LightTest : IMapGenerator
{
    public string Name => "Light Test";
    public string Pack => "Dev";
    public int Number => 0; // Dev maps carry no number: Maps/Dev/LightTest.json

    const string Ground = "#4a4740", Stone = "#8c867a", Pillar = "#9a9384", Roof = "#5e5850", Altar = "#b8b0a0", Room = "#7d7468";

    public void Build(Kit k)
    {
        k.Describe("Baked lighting dev map: a dusk-lit stone hall with torches, candles, a chandelier, a spot-lit altar and stained "
                   + "glass, a side room by firelight, and lamps outside.", order: 989, maxPlayers: 8);
        k.Size(22, 16);
        k.Skybox("dusk");
        k.BakedLighting(cell: 0.5, indoor: 0.22);

        k.Ground(Ground);
        k.Perimeter(3, Stone);

        // ---- The hall: x -14..6, z -8..4, walls 7 m, a roof ----
        k.WallX(-14, 6, -8, 0.4, 0, 7, Stone, Kit.Window(-10, 1.6, 3.5, 5.5), Kit.Window(-4, 1.6, 3.5, 5.5), Kit.Window(2, 1.6, 3.5, 5.5));
        k.WallX(-14, 6, 3.6, 0.4, 0, 7, Stone, Kit.Window(-10, 1.6, 3.5, 5.5), Kit.Door(-4, 3.0, 3.2), Kit.Window(2, 1.6, 3.5, 5.5));
        k.WallZ(-8, 4, -14.4, 0.4, 0, 7, Stone, Kit.Window(-5, 1.6, 3.0, 5.5), Kit.Window(1, 1.6, 3.0, 5.5));
        k.WallZ(-8, 4, 6, 0.4, 0, 7, Stone, Kit.Door(-2, 2.0, 2.6));
        k.Slab(-14.4, -8, 6.4, 4, 7.4, 0.4, Roof);
        foreach (var z in new[] { -4.5, 0.5 })
            foreach (var x in new[] { -11.0, -7.0, -3.0, 1.0 })
                k.Block(x - 0.4, 0, z - 0.4, x + 0.4, 7, z + 0.4, Pillar);

        // The altar at the east end: candles on it, two candle stands, a spotlight from the roof.
        k.Block(2.2, 0, -3.6, 4.4, 1.0, -0.4, Altar);
        k.Prop("Altar Candles", 3.3, -2.0, y: 1.0);
        k.Prop("Candle Stand", 2.4, -5.0);
        k.Prop("Candle Stand", 2.4, 1.0);
        k.SpotLight(3.3, 6.9, -2.0, "#ffe9c0", pitch: 90, cone: 40, intensity: 2.5, range: 10);

        // The chandelier over the nave, torches down both long walls (backs to the wall).
        k.Prop("Chandelier", -5, -2, y: 7 - 1.2);
        Lights.Fittings(k, "Iron Torch", -12, -7.6, 4, -7.6, 4, y: 2.2, yaw: 0);
        Lights.Fittings(k, "Iron Torch", -12, 3.6, -6, 3.6, 6, y: 2.2, yaw: 180);
        k.Prop("Iron Torch", 4, 3.6, y: 2.2, yaw: 180);

        // Stained glass: coloured light falling in through the west windows (they face west, the light shines east).
        Lights.Window(k, -13.9, 4.25, -5, 1.6, 2.5, 90, "#6f8cff", intensity: 1.2);
        Lights.Window(k, -13.9, 4.25, 1, 1.6, 2.5, 90, "#ff7a6a", intensity: 1.2);

        // ---- The side room: x 6.4..14, z -8..4, 4 m high, a fireplace and a lantern ----
        k.WallX(6.4, 14, -8, 0.4, 0, 4, Room);
        k.WallX(6.4, 14, 3.6, 0.4, 0, 4, Room);
        k.WallZ(-8, 4, 14, 0.4, 0, 4, Room);
        k.Slab(6.4, -8, 14.4, 4, 4.4, 0.4, Roof);
        k.Prop("Fireplace", 10, -7.35);
        k.Prop("Hanging Lantern", 12.6, 2.4, yaw: 180);

        // ---- Outside: a brazier by the south door, street lamps ----
        k.Prop("Brazier", -1.5, 7.5);
        k.Prop("Street Lamp", 9, 9, yaw: 180);
        k.Prop("Heritage Lamp Post", -18, 8);

        // ---- Spawns, objectives, waypoints ----
        foreach (var (x, z) in new[] { (-18.0, -12.0), (-18.0, 0.0), (-18.0, 12.0), (-10.0, 12.0) })
            k.Spawn(0, x, z);
        foreach (var (x, z) in new[] { (18.0, 12.0), (18.0, 0.0), (18.0, -12.0), (10.0, 12.0) })
            k.Spawn(1, x, z);
        k.Hill(0, 9, 3.0);
        k.Hill(0, -12, 3.0);
        k.Flag(-18, -12);
        k.Flag(18, 12);
        k.AutoWaypoints(4);

        k.Test(new JObject(("maxPlayers", 8), ("minHills", 2), ("minTeamSpawns", 4), ("walkFairness", 0.0), ("minMainNavPct", 60),
            ("lightProbes", new JArray([
                Probe(-5, 1.0, -2, 0.25, null),      // under the chandelier
                Probe(3.3, 1.2, -2.0, 0.6, null),    // the altar top, in its spotlight
                Probe(10, 0.6, -6.4, 0.25, null),    // in front of the fireplace
                Probe(10, 1.0, 1.0, null, 0.8),      // the side room, between the fire and the lantern: not bright
                Probe(-5, 6.6, -2, null, 2.0)]))));  // under the roof above the chandelier (still lit, not blown out)
    }

    private static JObject Probe(double x, double y, double z, double? min, double? max)
    {
        var o = new JObject(("at", JArray.Of(x, y, z)));
        if (min != null) o.Add("min", min.Value);
        if (max != null) o.Add("max", max.Value);
        return o;
    }
}
