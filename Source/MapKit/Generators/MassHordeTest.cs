namespace CallOfSlop.MapKit.Generators;

/// <summary>
/// Maps/Dev/MassHordeTest.json: "Mass Horde Test", the mass horde spike's dev map (developer request, 2026-10-27;
/// docs/zombies/MASS-HORDE.md). A 160 x 160 m open field with "crowd": 10000 in its Zombies section: each round is ten
/// thousand zombies, seeded as crowd particles out in the field (Source/Game/Horde/HordeCrowd.cs), that become full
/// zombies within 22 m of a player and go back to the crowd beyond 34 m. On this map the host draws every particle
/// (yellow; orange while held back by the individual cap) and a red marker over every individual zombie.
///
/// A low square plaza in the middle (the hill and the box; the start area is the whole field, the spawns just inside the long walls), and obstacles over
/// the field for the flow field to route round: four long walls with gaps, an L of crates in each quarter and a ring of
/// pillars. Mirror-symmetric about z = 0 for the multiplayer modes' checks.
/// </summary>
public sealed class MassHordeTest : IMapGenerator
{
    public string Name => "Mass Horde Test";
    public string Pack => "Dev";
    public int Number => 0;

    const double H = 80; // half size
    const string Floor = "#4d4c43", Wall = "#6f6a5c", Crate = "#5a4632", Stone = "#857c6c", Outer = "#3c3a36";

    public void Build(Kit k)
    {
        k.Describe("Mass horde dev map: a 160 x 160 m field where each Zombies round is ten thousand zombies, a crowd that turns into "
                   + "full zombies near the players (the host draws the crowd as yellow particles, individual zombies with a red marker). "
                   + "Long walls, crates and pillars to route round.", order: 987, maxPlayers: 8);
        k.Size(H, H);
        k.Top.Set("sky", "#5a6470");
        k.Test(new JObject(("maxPlayers", 8), ("minHills", 1), ("minTeamSpawns", 4), ("symmetry", "mirrorZ"), ("walkFairness", 0.12), ("minMainNavPct", 60),
            ("botMatchSeconds", 8), ("spawnsStand", true)));

        k.Ground(Floor);
        k.Perimeter(3.5, Outer);

        // The plaza: a low kerb round a 16 x 16 m square (gaps in the middle of each side), the box on it.
        foreach (var s in new[] { -1, 1 })
        {
            k.Block(-8, 0, s * 8 - 0.2, -2, 0.5, s * 8 + 0.2, Stone);
            k.Block(2, 0, s * 8 - 0.2, 8, 0.5, s * 8 + 0.2, Stone);
            k.Block(s * 8 - 0.2, 0, -8, s * 8 + 0.2, 0.5, -2, Stone);
            k.Block(s * 8 - 0.2, 0, 2, s * 8 + 0.2, 0.5, 8, Stone);
        }

        // Four long walls, each with two gaps, 30 m out: the crowd funnels through the gaps or goes round the ends.
        k.WallX(-26, 26, -30, 0.4, 0, 2.6, Wall, Kit.Door(-10, 4, 2.6), Kit.Door(10, 4, 2.6));
        k.WallX(-26, 26, 30, 0.4, 0, 2.6, Wall, Kit.Door(-10, 4, 2.6), Kit.Door(10, 4, 2.6));
        k.WallZ(-26, 26, -30, 0.4, 0, 2.6, Wall, Kit.Door(-8, 4, 2.6), Kit.Door(8, 4, 2.6));
        k.WallZ(-26, 26, 30, 0.4, 0, 2.6, Wall, Kit.Door(-8, 4, 2.6), Kit.Door(8, 4, 2.6));

        // An L of crates in each quarter, 50 m out.
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                for (var i = 0; i < 6; i++)
                {
                    var x = sx * (44 + 2 * i);
                    k.Block(x - 0.9, 0, sz * 50 - 0.9, x + 0.9, 1.6, sz * 50 + 0.9, Crate);
                }
                for (var i = 1; i < 6; i++)
                {
                    var z = sz * (50 + 2 * i);
                    k.Block(sx * 44 - 0.9, 0, z - 0.9, sx * 44 + 0.9, 1.6, z + 0.9, Crate);
                }
            }

        // A ring of pillars at 62 m.
        for (var i = 0; i < 16; i++)
        {
            var a = (i + 0.5) * Math.PI * 2 / 16;
            double x = Math.Round(Math.Cos(a) * 62, 2), z = Math.Round(Math.Sin(a) * 62, 2);
            k.Block(x - 1, 0, z - 1, x + 1, 3, z + 1, Stone);
        }

        // Spawns: four a team inside the long walls either side of the plaza, mirrored about z = 0; the hill on the plaza.
        foreach (var x in new[] { -5.0, -2.5, 2.5, 5.0 })
        {
            k.Spawn(0, x, -20);
            k.Spawn(1, x, 20);
        }
        k.Hill(0, 0, 4);
        k.Flag(0, -40);
        k.Flag(0, 40);
        k.AutoWaypoints(6);

        k.Top.Set("zombies", Zombies());
    }

    static JArray P(double x, double z) => JArray.Of(Kit.R(x), Kit.R(z));

    static JObject Zombies() => new(
        ("start", "Field"),
        ("crowd", 10000),
        ("areas", new JArray([new JObject(("name", "Field"), ("rect", JArray.Of(-H, -H, H, H)))])),
        // (A ground spawner the crowd's promoted zombies are made from; nothing comes out of it on its own.)
        ("spawners", new JArray([new JObject(("area", "Field"), ("at", P(0, -70)))])),
        ("box", new JObject(("cost", 950), ("start", 0), ("spots", new JArray([new JObject(("at", P(4, 0)), ("yaw", 90.0))])))));
}
