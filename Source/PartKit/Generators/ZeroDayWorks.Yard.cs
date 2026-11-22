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
        // The yard: the one big Building Station (a 30 x 9 bay, 8-mast corners, its two pick pads south of it) under a yard crane on 22 m gantry columns, its rails
        // running east-west on the yard's north and south edges (a 32 m span); a staging pad and the fittings pick pad east of the
        // station's Core.
        public const float YardRailNorth = -199f, YardRailSouth = -231f; // the two runs' lines (z): a 32 m span
        public const float YardRailWest = 3f, YardRailEast = 93f; // the runs' first and last block centres (x)
        public const int YardMasts = 10; // 20 m: the bay's 16 m corner masts stand under the hook, and the Core clears the shed's last truss (22.8 m)
        public const int BayAcross = 9, YardPadRows = 6; // the yard bay's cells across (18 m: the frigate's 16.4 m beam) and its pick pads' rows south of it
        public static float YardRailFoot => FloorY + 2 * YardMasts;

        private VPart? _yardStation, _yardCrane;
        private readonly List<VPart> _makers = new(); // (the general line's Presses and the block Assembler: the yard's orders console runs them)

        private void LayPower()
        {
            var gensets = new List<VPart>();
            foreach (var x in new[] { 20f, 30f, 40f })
                gensets.Add(Paint(Weld(Medium("Medium Genset", x, -12f)), Power));
            var bunkers = new List<VPart>();
            foreach (var x in new[] { 60f, 68f })
                bunkers.Add(Paint(Weld(Medium("Medium Fluid Bunker", x, -8f)), Stock));
            foreach (var b in bunkers)
                Config(b, ("fluid", "fuel"), ("fill", 100)); // (full of fuel when spawned in creative, Sean 2026-11-22)
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

            // ---- The yard's Building Station: a 30 x 9 bay (x 16 to 76, z -200 to -218), 8 Mast Blocks on each corner, its Core on the bay's
            // north side on the north run's line between two of the crane's columns (x 51 and 67: the bay and its pick pads south of it
            // then fill the crane's 32 m span), turned so the bay runs along it: a station builds a blueprint's
            // length along its Core (the bay's "along"), and the frigate's 51 m must lie east-west in the 60 m (with the Core east of the bay,
            // facing south, the bay was 60 m across and 24 m along, and the frigate didn't fit).
            _yardStation = Station(55, YardRailNorth, 17, YardRailNorth - 2, 30, BayAcross, 8, General, East);
            // ---- The yard Assembler, west of the bay (x 0 to 14, z -202 to -224): the frigate's big pieces (decks 5 x 9 cells, the spine 3 x
            // 11, bow and stern 8 m high) need a 6 x 11 bed with 10 m masts; the bay-3 Assembler's 4 x 4 makes the rest. Under the yard
            // crane, which lifts what it makes to the station.
            var big = Assembler(1, -205, 6, 11, 5, jig: false, carriagesEast: true);
            var up = 9.5f;
            foreach (var m in new[] { "steel", "copper", "glass", "concrete", "rubber" })
            {
                var (from, ff) = PortOf(_bins[m], "Chute out");
                var (to, tf) = PortOf(big, "Chute in");
                var a1 = from + ff;
                var b1 = to + tf;
                // (south down the bin's own x over the bays, off a column row, to z -195, between the shed's last columns (z -197) and
                // the bays' (z -189); west there to x 7, south between the yard crane's first two north columns, then in)
                var lane = RailX.FirstOrDefault(r => MathF.Abs(r - a1.X) < 1.5f, float.NaN) is var rx && float.IsNaN(rx) ? a1.X : rx - 2f; // (off a column row)
                Link("chute", _bins[m], big, "Chute out", "Chute in", Stock, false, null,
                    Cut(from, a1, V(a1.X, up, a1.Z), V(lane, up, a1.Z), V(lane, up, -195f), V(7f, up, -195f), V(7f, up, -201.2f), V(b1.X, up, -201.2f), V(b1.X, up, b1.Z), b1));
                up += 0.5f;
            }
            YardOrders(_yardStation, big);

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

            // ---- The station's own pick pads, south of the bay under both cranes (a station takes a pad wholly past its bay's side,
            // within the bay's length: ConstructStationPads): two pads of 10 x 6 tiles (20 x 12 m: a deck's 18 x 10 lies on one), Mast
            // Blocks on their corners for the bow's and stern's 8 m. The yard crane lays the yard Assembler's big pieces on them and
            // the station's own arms fit them from there (the arms the doc had along the bay's south side are the station's own).
            const float PadZ = YardRailNorth - 2 * (BayAcross + 1); // (the first row's middle: the cell after the bay's last)
            foreach (var x0 in new[] { 21f, 49f })
            {
                var pad = Tiles("Pad Tile", x0, PadZ, 10, YardPadRows, South, Stock);
                foreach (var c in Corners(pad))
                    Stack(c, "top", 4);
                _stores.Add(($"yard pick {x0:0}", pad[0, 0]));
            }
        }

        /// <summary>A link's nodes through <paramref name="pts"/> from <paramref name="from"/>, each straight cut into spans of at most <see cref="SegmentMax"/>.</summary>
        private static Vector3[] Cut(Vector3 from, params Vector3[] pts)
        {
            var nodes = new List<Vector3>();
            var last = from;
            foreach (var q in pts)
            {
                if (Vector3.Distance(q, last) < 0.05f)
                    continue;
                var n = (int)MathF.Ceiling(Vector3.Distance(q, last) / SegmentMax);
                for (var k = 1; k <= n; k++)
                    nodes.Add(Vector3.Lerp(last, q, k / (float)n));
                last = q;
            }
            return nodes.ToArray();
        }

        /// <summary>
        /// What the yard's station orders through: a Line Console (started) in bay 3 by the block Assembler, its Run wired to the Assembler,
        /// the four Presses and the yard Assembler, its wire into the station's Orders port carried south over the fittings run at 6 m; and the link stock (copper
        /// for wires, steel for pipes) in two Materials Bins east of the Core, chuted into it (a bin takes any material: a player fills them).
        /// </summary>
        private void YardOrders(VPart station, VPart big)
        {
            const float up = 6f;
            var console = Paint(On("Line Console", 93, -127, West, paint: Rail), Rail);
            Config(console, ("startOn", 1));
            var at = SocketAt(console, "bottom");
            foreach (var m in _makers)
            {
                var to = SocketAt(m, m.Def.Sockets[0].Name);
                Link("data", console, m, null, "Run", Rail, false, null, Cut(at, V(at.X, up, at.Z), V(to.X, up, at.Z), V(to.X, up, to.Z)));
            }
            var s = SocketAt(station, "-y.0.1");
            Link("data", console, station, null, "Orders", Rail, false, null,
                Cut(at, V(at.X, up, at.Z), V(88.5f, up, at.Z), V(88.5f, up, -199f), V(s.X, up, -199f), V(s.X, up, s.Z)));
            // ...and the yard Assembler, the same way south and on west along the yard's north edge (a station takes one console).
            var bs = SocketAt(big, big.Def.Sockets[0].Name);
            Link("data", console, big, null, "Run", Rail, false, null,
                Cut(at, V(at.X, up, at.Z), V(88.5f, up, at.Z), V(88.5f, up, -199f), V(bs.X, up, -199f), V(bs.X, up, bs.Z)));
            // (bins, not open Storage Volumes: an open face's lowest corner under the level pours: production.md lessons, 2026-11-17)
            var (ci, cf) = PortOf(station, "Chute in");
            var c1 = ci + cf;
            foreach (var x in new[] { 61f, 63f })
            {
                var bin = On("Materials Bin", x, YardRailNorth, Up, paint: Stock);
                var top = SocketAt(bin, "bottom") + new Vector3(0, bin.Def.BoundsMax.Y + 0.2f, 0);
                Link("chute", bin, station, null, "Chute in", Stock, false, null, Cut(top, V(top.X, 3f, top.Z), V(top.X, 3f, top.Z + 2f), V(c1.X, 3f, top.Z + 2f), V(c1.X, 3f, c1.Z), c1)); // (north round the crane's column at x 59)
            }
        }
    }
}
