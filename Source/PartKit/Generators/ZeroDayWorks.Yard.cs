using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class ZeroDayWorks
{
    private sealed partial class Script
    {
        // The generator hall (doc "Generator hall", z 0 to -24) and the open assembly yard (doc "Shipyard", z -199 to -239).
        //
        // Power: three Medium Gensets (1 MW plants each: a construct runs one engine only, the first that runs, so the doc's three
        // engine-kit sets with 1 MW heads became three plants, which all feed the one circuit), fuel from two Medium Fluid Bunkers by pipe,
        // and four battery banks. The frame is the circuit: every part the floor's links reach is on it.
        //
        // The yard: the one big Building Station (a 30 x 12 bay, 8-mast corners) under a yard crane on 22 m gantry columns, its rails
        // running east-west on the yard's north and south edges (a 32 m span); a staging pad and the fittings pick pad east of the
        // station's Core; Plasma Loader Arms along the bay's south side between the columns.
        public const float YardRailNorth = -199f, YardRailSouth = -231f; // the two runs' lines (z): a 32 m span
        public const float YardRailWest = 3f, YardRailEast = 93f; // the runs' first and last block centres (x)
        public const int YardMasts = 11; // 22 m: the bay's 16 m corner masts stand under the hook
        public static float YardRailFoot => FloorY + 2 * YardMasts;

        private VPart? _yardStation, _yardCrane;

        private void LayPower()
        {
            var gensets = new List<VPart>();
            foreach (var x in new[] { 20f, 30f, 40f })
                gensets.Add(Paint(Weld(Medium("Medium Genset", x, -12f)), Power));
            var bunkers = new List<VPart>();
            foreach (var x in new[] { 60f, 68f })
                bunkers.Add(Paint(Weld(Medium("Medium Fluid Bunker", x, -8f)), Stock));
            // Fuel: each bunker's out to every genset's Fuel in (a port takes any number of links), over the hall at 6 m.
            foreach (var g in gensets)
                Duct("pipe", bunkers[0], "Fluid out", g, "Fuel in", 6f, Power);
            Duct("pipe", bunkers[1], "Fluid out", bunkers[0], "Fluid in", 5f, Power);
            // The battery banks: the circuit's store (24 MJ each), along the hall's south wall.
            foreach (var x in new[] { 19f, 25f, 31f, 37f })
                On("Ship Battery Bank", x, -21f, Up, paint: Power);
        }

        /// <summary>A medium part (its sockets too big for the floor's) to weld on freeform, its bounds' foot 3 mm over the slab at (x, z).</summary>
        private (string Part, VPart On, Vector3 Centre) Medium(string part, float x, float z)
        {
            var d = Def(part);
            return (part, Tile(x, z), V(x, FloorY + 0.003f + (d.BoundsMax.Y - d.BoundsMin.Y) * 0.5f, z));
        }

        /// <summary>Welds a medium part on freeform, unturned, and keeps where its origin is (for its ports: <see cref="PortOf"/>).</summary>
        private VPart Weld((string Part, VPart On, Vector3 Centre) w)
        {
            var p = Weld(w.Part, w.On, w.Centre, Up, 0, 0, 0);
            _welded[p] = w.Centre - (p.Def.BoundsMin + p.Def.BoundsMax) * 0.5f;
            return p;
        }

        private void LayYard()
        {
            // ---- The yard crane: two runs of Crane Rail Blocks east-west on 22 m columns (one every 8 m), the Core at the north run's west end.
            var runs = new List<VPart>[2];
            var turned = East; // (a rail run along x: the blocks' own +z east, their own +x south)
            for (var r = 0; r < 2; r++)
            {
                var z = r == 0 ? YardRailNorth : YardRailSouth;
                runs[r] = new List<VPart>();
                VPart? prev = null;
                var blocks = (int)((YardRailEast - YardRailWest) / 2) + 1;
                for (var k = 0; k < blocks; k++)
                {
                    var x = YardRailWest + 2 * k;
                    VPart rail;
                    if (k % ColumnEvery == 0 || k == blocks - 1)
                    {
                        var column = Paint(Stand("Mast Block", V(x, FloorY, z), Up, Tile(x, z), "bottom"), Steel);
                        for (var m = 1; m < YardMasts; m++)
                            column = Paint(Stand("Mast Block", V(x, FloorY + 2 * m, z), Up, column, "bottom"), Steel);
                        rail = Stand("Crane Rail Block", V(x, YardRailFoot, z), turned, column, "bottom");
                    }
                    else
                        rail = Stand("Crane Rail Block", V(x - 1, YardRailFoot + 0.5f, z), turned, prev!, "-z");
                    runs[r].Add(Paint(rail, Rail));
                    prev = rail;
                }
            }
            _yardCrane = Paint(Stand("Crane Core", V(YardRailWest + 2, YardRailFoot + 0.5f, YardRailNorth + 1), turned, runs[0][1], "+x"), Crane);
            Config(_yardCrane, ("carryHeight", 1.5));

            // ---- The yard's Building Station: a 30 x 12 bay (x 17 to 75, z -203 to -225), its Core east of the bay, 8 Mast Blocks on each corner.
            _yardStation = Station(77, -205, 17, -203, 30, 12, 8, General);

            // ---- East of the Core: the staging pad (6 x 5) for the hull sections and the fittings pick pad (5 x 4), corner masts on each.
            var staging = Tiles("Pad Tile", 81, -203, 6, 5, South, Stock);
            foreach (var c in Corners(staging))
                Stack(c, "top", 1);
            _stores.Add(("staging", staging[0, 0]));
            var pick = Tiles("Pad Tile", 81, -217, 5, 4, South, Stock);
            foreach (var c in Corners(pick))
                Stack(c, "top", 1);
            _stores.Add(("fittings pick", pick[0, 0]));
            Arm("Plasma Loader Arm", 81, -229); // (at the pick pad's foot)

            // ---- Plasma Loader Arms along the bay's south side, one between each pair of the south run's columns.
            for (var x = YardRailWest + 2; x + 3.6f < 80f; x += 2 * ColumnEvery)
                Arm("Plasma Loader Arm", x, -231);
        }
    }
}
