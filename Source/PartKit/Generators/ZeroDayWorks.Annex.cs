using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class ZeroDayWorks
{
    private sealed partial class Script
    {
        // The west annex refinery (Sean, 2026-11-24: "the current shipbuilding process is always starved for metal and steel"; he chose the
        // west annex beside the materials row). Outside the shed's west wall, x -32 to 0 and z -24 to -72, on four more columns of floor
        // slabs welded to the site's west edge: the block refiners in their big form (production.md "Refiners as block machines").
        //
        // - A Smelter Core with a 5 x 5 square of Hearth Blocks (x57.25: about 114 metal a second from 229 iron ore) and a Casting Bed.
        // - A Foundry Core on steel with a 5 x 5 square of Crucible Blocks (x57.25: about 114 steel a second from 114 metal and 57 stone)
        //   and a Mould Line. The four armour lines draw about 29 steel a second; the whole frigate's bill was put at about 100.
        // - Stock north of them: two Ore Bunker 8 m of iron ore and one of stone (60,000 each); buffers south: a metal silo between the
        //   Smelter and the Foundry, a steel silo after the Foundry (Ore Silo 4 m, 57,960 each).
        // - The steel goes to the materials row's steel bin and the hall's steel silo on high chutes along z -37, the open band under the
        //   materials row's roof north of the bays' walls, so nothing crosses the crane bays.
        //
        // To grow it (Sean will set the rate once he has tried it): a Core takes up to 50 blocks, so a second 5 x 5 fits north of each
        // square, in the 10 m left free between the squares and the stock row (z -36 to -46). Weld it on the first square's +z side; the
        // best split makes it a second 5 x 5 furnace (x113.5 in all).
        public const float AnnexWest = -32f, AnnexNorth = -24f, AnnexSouth = -72f;
        public const int AnnexSlabsX = 4;
        public const float AnnexCoreZ = -55f; // the Cores' row: the squares' front (south) row; the squares run north to z -46
        public const float AnnexSmelterX = -31f, AnnexFoundryX = -15f;
        public const int AnnexSquare = 5;
        public const float SteelRunZ = -37f; // the band the steel chutes cross to the materials row along: between the roof's last column line (z -33) and the bays' (z -41)

        /// <summary>The annex's floor slabs (i west from the site's edge: 0 is x -8 to 0; j as the site's): null where none is laid.</summary>
        private readonly VPart?[,] _west = new VPart?[AnnexSlabsX, SlabsZ];

        /// <summary>The annex's floor: Works Floor Slabs west of the site's edge, each welded to its east neighbour.</summary>
        private void LayAnnexFloor()
        {
            var j0 = (int)(-AnnexNorth / Slab);
            var j1 = (int)(-AnnexSouth / Slab);
            for (var j = j0; j < j1; j++)
                for (var i = 0; i < AnnexSlabsX; i++)
                {
                    var at = V(-Slab * (i + 0.5f), 0.15f, -Slab * (j + 0.5f));
                    var t = Put("Works Floor Slab 8 m", at, Up, on: i == 0 ? _tiles[0, j] : _west[i - 1, j]);
                    _west[i, j] = t;
                    Paint(t, Floor);
                    Finish(t, "concrete");
                }
        }

        private void LayAnnex()
        {
            // Stock: iron ore at x -28 and -18, stone at x -9 (a slab's sockets are on the metres 1 to 7 past its edge).
            var ore1 = AnnexStock(-28f, "iron ore");
            var ore2 = AnnexStock(-18f, "iron ore");
            var stone = AnnexStock(-9f, "stone");

            var smelter = Paint(On("Smelter", AnnexSmelterX, AnnexCoreZ, Up), Refining);
            var hearths = Square(smelter, "Hearth Block");
            Paint(Onto(hearths[AnnexSquare - 1, 0], "+x", "Casting Bed", "-x"), Refining); // (the front row's bed: the runner ends in it)

            var foundry = Paint(On("Foundry", AnnexFoundryX, AnnexCoreZ, Up), Refining);
            Config(foundry, ("recipe", 2)); // (steel only: metal and stone)
            var crucibles = Square(foundry, "Crucible Block");
            Paint(Onto(crucibles[AnnexSquare - 1, 0], "+x", "Mould Line", "-x"), Refining);

            var metalSilo = Full(On("Ore Silo 4 m", -27f, -65f, Up, paint: Stock, finish: "concrete"), "metal");
            var steelSilo = Full(On("Ore Silo 4 m", -9f, -65f, Up, paint: Stock, finish: "concrete"), "steel");

            // Ore in over the reserved band (11 to 12 m: over the Smelter's stacks, 9 m), the metal straight into the Foundry and into its
            // silo, which tops the Foundry up; stone in; the steel into its silo and on to the materials row.
            Chute(ore1, smelter, 11f);
            Chute(ore2, smelter, 11.5f);
            Chute(smelter, foundry, 12.5f);
            Chute(smelter, metalSilo, 13f);
            Chute(metalSilo, foundry, 13.5f);
            Chute(stone, foundry, 12f);
            Chute(foundry, steelSilo, 14f);
            // (each refiner chute moves the Core's load, ceil(10 r) a second: 573 for a 5 x 5, so one chute carries all it makes)
            SteelRun(foundry, _bins["steel"], 16.5f);
            SteelRun(steelSilo, _bins["steel"], 17f);
            SteelRun(foundry, _steelSilo!, 17.5f);
        }

        /// <summary>An 8 m stock bunker on the annex's north row at x, full of <paramref name="stock"/> when spawned in creative.</summary>
        private VPart AnnexStock(float x, string stock)
        {
            var b = On("Ore Bunker 8 m", x, -30f, Up, paint: Stock, finish: "concrete");
            Config(b, ("material", stock), ("fill", 100));
            return b;
        }

        /// <summary>
        /// An n x n square of blocks on a Core's +x side (its own +x east, +z north): the first column along the Core's row, each column north
        /// from its first block. [i, k]: i east from the Core (0 is the cell next to it), k north.
        /// </summary>
        private VPart[,] Square(VPart core, string block)
        {
            var b = new VPart[AnnexSquare, AnnexSquare];
            for (var i = 0; i < AnnexSquare; i++)
            {
                b[i, 0] = Paint(Onto(i == 0 ? core : b[i - 1, 0], "+x", block, "-x"), Refining);
                for (var k = 1; k < AnnexSquare; k++)
                    b[i, k] = Paint(Onto(b[i, k - 1], "+z", block, "-z"), Refining);
            }
            return b;
        }

        /// <summary>
        /// A chute from the annex to the materials row: up to <paramref name="height"/>, north to <see cref="SteelRunZ"/> (between column lines), east along it, then
        /// along z and down into the far port (the shed's side walls start at the bays, z -41).
        /// </summary>
        private void SteelRun(VPart from, VPart to, float height)
        {
            var (a, fa) = PortOf(from, "Chute out");
            var (b, fb) = PortOf(to, "Chute in");
            var a1 = a + fa * 1.0f;
            var b1 = b + fb * 1.0f;
            var pts = new List<Vector3> { a1, V(a1.X, height, a1.Z), V(a1.X, height, SteelRunZ), V(b1.X, height, SteelRunZ), V(b1.X, height, b1.Z), b1 };
            var nodes = new List<Vector3>();
            var last = a;
            foreach (var q in pts)
            {
                if (Vector3.Distance(q, last) < 0.05f)
                    continue;
                var n = (int)MathF.Ceiling(Vector3.Distance(q, last) / SegmentMax);
                for (var k = 1; k <= n; k++)
                    nodes.Add(Vector3.Lerp(last, q, k / (float)n));
                last = q;
            }
            Link("chute", from, to, "Chute out", "Chute in", Stock, false, null, nodes.ToArray());
        }
    }
}
