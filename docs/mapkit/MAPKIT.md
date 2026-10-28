# The map kit (Source/MapKit)

A map generator is C# code inside the game assembly that fills a `Kit` with a map and writes the same JSON the map
loader reads. The point of doing it in C# rather than in a Python script is that the generated map is **checked by the
real game before anyone plays it**: `--gen-map` writes the file, loads it back through `MapFile.Load`, builds the
physics world and the nav graph and prints a report (objectives inside the outline and clear of geometry, spawns that
stand, the horizon fade, nav reach, fair walks for both teams, stairs the movers can climb, prop and decal counts,
nav build time). Those are the checks the Space maps' bespoke self-test sections and the all-maps sections make, so a
generator finds out at its desk what used to come back from CI on main.

Namespace `CallOfSlop.MapKit`. The committed map files stay the product: the game loads `Maps/<Pack>/*.json`, never a
generator, so a generator's output is committed and the "Map kit" self-test checks the committed file still matches.
Twin Shard (`Source/MapKit/Generators/TwinShard.cs`, the port of the old `tools/space/twinshard.py`) is the worked
example, and Horde Test (`Generators/HordeTest.cs`: a Zombies map, its "zombies" section built as a JSON tree in
`kit.Top`; `Number => 0` writes a Dev-style file with no number, `Maps/Dev/HordeTest.json`) a second one, and Ashgrave Cathedral (`Generators/AshgraveCathedral.cs`) a big Zombies map
on five levels (areas with heights, stairs with landings, map logic for the bell's climb, its own layout checks); `tools/mapkit/new_map.py <Pack> <NN> "<Name>"` scaffolds a new one from a template.

## Writing a generator

```csharp
namespace CallOfSlop.MapKit.Generators;

public sealed class OrbitalFoundry : IMapGenerator
{
    public string Name => "Orbital Foundry";   // the map's name in the lobby
    public string Pack => "Space";              // the pack folder under Maps
    public int Number => 13;                    // the file is Maps/Space/13-OrbitalFoundry.json

    public void Build(Kit kit)
    {
        kit.Describe("A zero-g steelworks: slag, sparks and two furnaces.", order: 613, maxPlayers: 16, fallKill: 6.0);
        kit.Skybox("night", ("stars", 1.8), ("ambient", 1.6));
        kit.Boundary(Geometry.FromHalf([(-60, 0), (-58, -30), (-30, -40), (0, -42)], Symmetry.Turn180), "smooth", "none", barrierHeight: 1.0);

        kit.Symmetric(Symmetry.Turn180, () =>           // Blue's half; the kit adds Red's twin of everything below
        {
            kit.Block(-50, 20, -10, -20, 20.5, 10, "#9a9590");           // a deck
            Structures.Catwalk(kit, -20, 0, -4, 0, 20.5, 3.0);          // a railed bridge to the middle
            Structures.Stairs(kit, -30, -9, 20.5, 3.0, 0, -1, width: 1.6); // a flight up (risers and width checked)
            Clusters.TankFarm(kit, -40, -6, 20.5, count: 3, seed: 4);
            kit.Prop("Station Console", -46, 4, 20.5, yaw: 90);
            kit.Decal("global:Hazard Stripes", -22, 0, 20.5, size: 2.5, rotation: 90);
            kit.Hill(-30, 0, 4.5, y: 20.5);
            kit.Flag(-46, 0, 20.5);
            kit.Spawn(0, -48, -6, 20.5);
        });
        kit.Hill(0, 0, 4.5, y: 20.5);                     // on the symmetry line: it stays single
        kit.AutoWaypoints(7);
    }
}
```

Register it in `GeneratorRegistry.All` (`Source/MapKit/Generators.cs`), then:

| Command | What it does |
|---|---|
| `CallOfSlop --gen-map "Orbital Foundry" [--out DIR] [--plan]` | Runs the generator, writes `Maps/Space/13-OrbitalFoundry.json` (in the repository's Maps folder when the game runs from a checkout, else DIR), loads it back and prints the `MapChecks` report; `--plan` also writes a top-down PNG next to the exe. Exit 0 when the map loads and passes (a map's `test` block may lower the main-area share with `minMainNavPct`). |
| `CallOfSlop --gen-maps [--out DIR]` | Runs every generator and says for each whether its output is identical to the committed file, the same map in different text (same fingerprint), or different. |
| `CallOfSlop --gen-plan <map.json> <out.png>` | A plan of any map file: outline, boxes shaded by height (hidden colliders bluer), solid meshes, props, waypoints, spawns by team, hills, flags. |
| `CallOfSlop --gen-plan <map.json> <out.png> --level y0,y1` | One level of a map with floors over each other (`Plan.DrawLevel`): the floors whose tops are between y0 and y1, the walls through them, props, waypoints, logic zones, and the Zombies section (areas, doors, windows, spawners, machines, wall buys) on that level. |

The console is written to `callofslop.log` next to the exe. The report looks like

```
Twin Shard: OK; 57 boxes (0 hidden), 137 meshes, 20 props, decals 110/160, 16 spawns, 69 waypoints; nav 1952 nodes (1845 main), 26521 edges, built in 763 ms; 0 stair flight(s); fingerprint 790c954b
  walks Blue/Red: hill 1 57/60 m, hill 2 83/82 m, hill 3 72/71 m, enemy flag 93/92 m
```

and every `FAIL` line names the place and the rule. The same checks run from code: `MapChecks.Check(map, new CheckOptions
{ WalkFairness = 0.1f, MinProps = 80 })`.

## What the kit gives you

**Ops.** `Block`, `Collider` (hidden), `Mesh` / `MeshAt` (placed by its centre whatever its turn), `WallX` / `WallZ` with
`Kit.Door` and `Kit.Window` holes, `Slab`, `StairsX` / `StairsZ` (the game's 0.4 m steps), `Ramp`, `Ground`, `Perimeter`,
`Prop`, `Decal`, `WallDecal` (by the wall's facing: `"+x"`, `"-z"`...), `WallSign` (tiles read left to right from the
side the wall faces), `DecalScatter`, `Clutter`, `Waypoint`, `WaypointGrid`, `Spawn(team, ...)`, `Building`, `Rocks`,
`AutoWaypoints`, and `Op(...)` / `Emit(JObject)` for anything else. Every op is checked when it is added against the real
op table (`MapFile.OpBuilder`): an unknown op or parameter, a missing one or a decimal where a whole number goes throws a
`MapKitException` naming the op, so a generator can't write what the loader would refuse. Numbers are rounded to
millimetres, yaws to a tenth of a degree, and `-0.0` stays negative (the fingerprint mixes float bits).

**Header.** `Describe`, `Size` (a rectangle) or `Boundary(points, shape, edge, ...)` with `Kit.Edge(...)`, `Skybox`,
`Terrain(JObject)`, and `kit.Top` for anything else (gravity zones, foliage, movers, flagPool). `Hill`, `Flag` and
`Spawn` fill `hills`, `flags` and `teamSpawns`.

**Symmetry.** `Symmetric(sym, half)` runs your half and appends the twin of every op it added, and the twins of its
spawns (into the other team), hills and flags. `Kit.Twin(op, sym)` knows each op's fields: positions and rectangles,
yaws (a turn adds 180; a mirror negates or takes 180 - yaw so fronts face the mirrored way), meshes' roll, decal
surfaces (`wall+x` becomes `wall-x`) and turns, scatter and clutter seeds (offset, so the two halves' litter isn't a
copy), wall thickness sides, stairs' and ramps' climbing direction, buildings' yaw and `mirror` variant. `TwinFeature`
turns a terrain feature (a cliff's side stays), `TwinZone` a gravity zone (renaming "west" to "east" if you ask), and
`Geometry.FromHalf` builds an outline from one half.

**Geometry.** `Circle`, `Ellipse`, `RegularPolygon`, `Bean` (a pinched ellipse), `Blob` (a seeded wobbly circle),
`Smooth` (the game's Catmull-Rom curve through control points, to test against), `Inside`, `EdgeDistance`, `Area`,
`Along` (points along a path at a spacing), `YawFacing` / `YawAlong`, `SurfaceDir` (a point on a sphere by latitude and
longitude), `Turn` (the game's mesh rotation order). `Rng(seed)` is a tiny deterministic generator: the same seed gives
the same map on every machine.

**Structures** (`Structures.*`, the movers' rules built in: Maps/README.md "Void maps", the "Stairs" section).
`Railing` is a hidden collider band 0.45 to 1.1 m above the deck (bullets and prone shots go under it) drawn as panels
and posts, with gaps where bridges meet. `Stairs` makes a flight of hidden colliders plus instanced mesh steps and
stringers, and throws for risers over 0.55 m or under 0.05, treads outside 0.15-1.6 m or a flight narrower than 1.0 m.
`RampColliders` and `Gangway` step up 4 cm at a time (a ramp, not a staircase, to the stairs audit) and `Gangway` refuses
slopes over 30 degrees. `Catwalk` is a level deck between any two points (diagonal decks are sliced) with railings and
waypoints every 4 m. `Lane(path, spacing, offset, place)` drops cover along a path, alternating sides.

**Clusters** (`Clusters.*`, from the Space generators): `Tank` and `TankFarm` (solid round cores), `CargoStack`,
`MarketStalls`, `LandingPad` (ring, glyph, number, floor lights, beacons, a skimmer), `CraneGantry` (a railed bridge, legs,
beam, trolley and hook), `DomeTower`. A group of props a map places again and again should become a **prefab prop**
instead: a prop file whose parts `use` other props (Props/README.md "Prefabs"; `Props/Space/Prefabs/LandingPadSet.json`).

**Map logic** (`kit.Logic`, `Source/MapKit/Logic.cs`; the format and the signal model are in Maps/README.md, "Map
logic"). `Door`, `Lamp`, `Button`, `Lever`, `Plate`, `Zone`, `Timer`, `Mover` (a named mover in `kit.Top["movers"]`),
`Sound`, `Text`, `Gate` ("and", "or", "not"), `Counter`, `Relay`, `Random`, `Challenge` (a timed survival with a
reward), `Reward` (points, a power-up), `Entity(type, name, fields...)` for anything
else (a `zdoor`, a `zmachine`, a `teleport`, a `breakable`), and `Wire("A.event", "B.action", delay, once, value, n, when)`;
every helper returns the builder, so wires chain. The kit refuses two entities with one name; the rest (unknown events
and actions, missing movers) is refused by the real reader when `--gen-map` loads the map back. Give the map's `test`
block an `"events"` list so the Map contract plays the logic through. Logic Test (`Generators/LogicTest.cs`,
`Maps/Dev/LogicTest.json`) is the worked example:

```csharp
var l = kit.Logic;
l.Door("Gate", -15, 0, -1.2, -14.7, 2.6, 1.2, color: "#b0763c");
l.Button("GateButton", -14.64, 1.3, 2.3, yaw: 90, prompt: "open the gate");
l.Wire("GateButton.pressed", "Gate.open").Wire("GateButton.pressed", "Gate.close", delay: 8);
l.Lever("LeverA", -4, 1.2, 14.98).Lever("LeverB", 4, 1.2, 14.98).Gate("and", "Both");
l.Wire("LeverA.changed", "Both.a").Wire("LeverB.changed", "Both.b").Wire("Both.true", "VaultDoor.open");
```

**Plan.** `Plan.Draw(map, "plan.png", lanes)` is the picture `--gen-plan` makes; `PngWriter` writes any RGB raster.

## Rules of the road

- Build Blue's half and let `Symmetric` make Red's, so both teams get the same cover and the same walks. Put hills on
  the symmetry line or in twinned pairs; `MapChecks` fails a hill or flag more than 12% farther from one team.
- Round things are cosmetic meshes over hidden colliders (`RoundCollider`, `Clusters.Tank`); a `Mesh` with
  `solid: true` collides with its exact triangles instead and counts towards the fingerprint.
- Void maps (`fallKill`): no `Ground`, `Boundary(..., edge: "none")`, waypoints with `y` along every bridge.
- Keep the horizon fade within 22 m of the floor on flat shaped maps: avoid outlines with long thin spikes (the report
  says how far past the floor the fade starts and in which direction).
- Run `--gen-map` after every change and commit the JSON. The "Map kit" self-test fails when a generator's output no
  longer matches its committed file, when a map file doesn't round-trip through the kit's JSON tree, and when
  `MapChecks.Check` fails on a Space map. Before pushing a map branch, run the all-maps sections too (`python3 -I
  tools/mapkit/check_branch.py` prints the `[tests: ...]` line).
- The old Python generators (`tools/space`, `tools/cluttered`, `tools/world`...) stay as they are: those maps are
  frozen and regenerate byte for byte. New maps use the kit.
