using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class ZeroDayWorks
{
    private sealed partial class Script
    {
        // The steel shed (doc "Exterior"; parts: "The shed's shell parts" below the areas): three gabled spans over the crane bays, z -41 to
        // -197, carried wall to wall by one chain of half trusses on each mast column line (every 8 m, the last bay 4 m), seated on Eaves
        // Posts on the two outer rail rows only (a crane bridge overruns the inner rows' shared rails). Roof slopes between the trusses, a
        // Ridge Monitor along each ridge, a Valley Gutter in each valley, the side walls clad on the outer columns' masts to the eaves,
        // gable sheets over both end trusses. The ends below the eaves are open: the extruders' chutes come in from the materials row at
        // the north end, the transfer runs leave for the yard at the south end.
        public const string Cladding = "#5d6670", Roofing = "#7a3e2e", Trim = "#d8b02a";

        /// <summary>Each half's length and whether its low end is the eave (else a valley), west to east: bay 1, bay 2, bay 3.</summary>
        private static readonly (int Length, bool Eave)[] Halves = [(15, true), (15, false), (20, false), (20, false), (12, false), (12, true)];

        /// <summary>Each column line's truss chain, west to east (the pendants hang from them).</summary>
        private readonly Dictionary<int, VPart[]> _trusses = new();

        /// <summary>The shed's north end over the generator hall and the materials row (z of each extra column line, north to south).</summary>
        public static readonly float[] NorthLines = [-1f, -9f, -17f, -25f, -33f];

        private void LayShell()
        {
            // The column lines that carry a truss, north to south: the north end's own (Sean, 2026-11-22: the generator hall and the materials
            // row stood in the open; the shed now runs on over them to the site's north edge), then the crane bays' (every fourth rail block).
            // A north line is a column of 9 Mast Blocks on each outer row with a Crane Rail Block cap, as the bays' columns are, so its Eaves
            // Posts and trusses sit where theirs do; a cap stands alone, 8 m from the next, so no crane's run reaches it (a run is rail blocks
            // end to end: ConstructCraneRails.Run).
            var lines = new List<(float Z, VPart WestRail, VPart EastRail, List<VPart> WestMasts, List<VPart> EastMasts, int Key)>();
            foreach (var z in NorthLines)
            {
                var (wr, wm) = NorthColumn(RailX[0], z);
                var (er, em) = NorthColumn(RailX[^1], z);
                lines.Add((z, wr, er, wm, em, -1 - lines.Count));
            }
            foreach (var k in Enumerable.Range(0, RailBlocks).Where(k => _columns.ContainsKey((0, k))))
                lines.Add((RailNorth - 2 * k, _rails[0][k], _rails[RailX.Length - 1][k], _columns[(0, k)], _columns[(RailX.Length - 1, k)], k));

            var trusses = _trusses;
            var westPosts = new List<VPart>();
            var eastPosts = new List<VPart>();
            var chains = new List<VPart[]>();
            foreach (var line in lines)
            {
                var w = line.WestRail;
                var e = line.EastRail;
                var pw = Paint(Stand("Eaves Post", SocketAt(w, "-x"), Up, w, "rail"), Steel);
                var pe = Paint(Stand("Eaves Post", SocketAt(e, "+x"), South, e, "rail"), Steel);
                westPosts.Add(pw);
                eastPosts.Add(pe);
                // The chain: west halves (low end west, own +z east) and east halves (ridge west, own +z west), joined ridge to ridge and low to low.
                var h = new VPart[Halves.Length];
                h[0] = Paint(Stand(Truss(0), SocketAt(pw, "seat"), East, pw, "seat"), Steel);
                for (var i = 1; i < Halves.Length; i++)
                {
                    var join = i % 2 == 1 ? "ridge" : "low";
                    h[i] = Paint(Stand(Truss(i), SocketAt(h[i - 1], join), i % 2 == 1 ? West : East, h[i - 1], join), Steel);
                }
                trusses[line.Key] = h;
                chains.Add(h);
            }

            for (var b = 0; b + 1 < lines.Count; b++)
            {
                var wide = (int)MathF.Round(lines[b].Z - lines[b + 1].Z); // 8, or 4 for the last bay
                var tn = chains[b];
                var ts = chains[b + 1];
                for (var i = 0; i < Halves.Length; i++)
                {
                    var (len, eave) = Halves[i];
                    var slope = $"Roof Slope {wide}x{len} m {(eave ? "Eaves" : "Valley")}";
                    // A slope sheets along its own +x: a west half's (own +x south) from the north truss, an east half's (own +x north) from the south one.
                    if (i % 2 == 0)
                        Paint(Stand(slope, SocketAt(tn[i], "topLow"), East, tn[i], "rest"), Roofing);
                    else
                        Paint(Stand(slope, SocketAt(ts[i], "topLow"), West, ts[i], "rest"), Roofing);
                }
                // A Ridge Monitor over each ridge (on the west halves' tops), a Valley Gutter in each valley (welded on: both halves' topLow
                // sockets there carry slopes).
                foreach (var i in new[] { 0, 2, 4 })
                    Paint(Stand($"Ridge Monitor {wide} m", SocketAt(tn[i], "topRidge"), East, tn[i], "rest"), Rail);
                foreach (var i in new[] { 2, 4 })
                {
                    var gutter = $"Valley Gutter {wide} m";
                    var d = Def(gutter);
                    var mid = (d.BoundsMin + d.BoundsMax) * 0.5f;
                    var at = SocketAt(tn[i], "topLow") + new Vector3(0, mid.Y, -mid.X); // (East: own +x south, own +z east)
                    Paint(Weld(gutter, tn[i], at, East, 0, 0, 0), Rail);
                }
                // The side walls: 4 m sheets on every second mast block (the floor to 20.3 m), then the eaves sheet on the post to 23.3 m.
                // A sheet runs along its own +z: on the west wall north from the bay's south column, on the east wall south from its north one.
                // (The west wall's top sheet stops short in the bay bay 1's Crane Core stands in, outside its rail at 17.3 to 19.3 m.)
                var core = lines[b].Z > RailNorth - 2 && lines[b + 1].Z < RailNorth - 2;
                foreach (var (west, masts, o, face, post) in new[] { (true, lines[b + 1].WestMasts, Up, "-x", westPosts[b + 1]), (false, lines[b].EastMasts, South, "+x", eastPosts[b]) })
                {
                    for (var m = 0; m < masts.Count; m += 2)
                        if (!west || !core || m + 2 < masts.Count)
                            Paint(Stand($"Wall Sheet {wide}x4 m", SocketAt(masts[m], face), o, masts[m], "back"), Cladding);
                    Paint(Stand($"Wall Sheet {wide}x3 m Eaves", SocketAt(post, "wall"), o, post, "back"), Cladding);
                }
            }
            // The gables over both end trusses, outside them. A Gable Sheet stands on a truss's +x face and rises along the truss: on a west half
            // (own +x south) that face is outside only at the south end, on an east half (own +x north) only at the north end. The other three
            // can't be turned outward (that would be a mirror image), so each is welded on freeform 6 mm off the truss's -x face, outside.
            foreach (var (h0, north) in new[] { (chains[0], true), (chains[^1], false) })
                for (var i = 0; i < Halves.Length; i++)
                {
                    var h = h0[i];
                    var west = i % 2 == 0; // (a west half: own +z east, own +x south)
                    var gable = $"Gable Sheet {Halves[i].Length} m";
                    if (west != north)
                    {
                        Paint(Stand(gable, SocketAt(h, "+x"), west ? East : West, h, "rest"), Cladding);
                        continue;
                    }
                    var d = Def(gable);
                    var mid = (d.BoundsMin + d.BoundsMax) * 0.5f;
                    var face = SocketAt(h, "-x");
                    // Turned about, its own +x points at the truss: its origin goes out as far as puts the slab (own x 0.58 to 0.64) where a
                    // stood sheet's is, 0.58 to 0.64 m outside the face (the hidden cleat then outside it, the ribs inside).
                    var thick = 1.22f;
                    var origin = face + new Vector3(0, 0, north ? thick : -thick);
                    var centre = west ? origin + new Vector3(mid.Z, mid.Y, -mid.X) : origin + new Vector3(-mid.Z, mid.Y, mid.X);
                    Paint(Weld(gable, h, centre, west ? East : West, 0, 0, 0), Cladding);
                }
            LayNorthWall();
        }

        /// <summary>A north line's column: 9 Mast Blocks on the floor at (x, z) and a Crane Rail Block on them (the Eaves Post's seat).</summary>
        private (VPart Rail, List<VPart> Masts) NorthColumn(float x, float z)
        {
            var column = Paint(Stand("Mast Block", V(x, FloorY, z), Up, Tile(x, z), "bottom"), Steel);
            var masts = new List<VPart> { column };
            for (var m = 1; m < ColumnMasts; m++)
            {
                column = Paint(Stand("Mast Block", V(x, FloorY + 2 * m, z), Up, column, "bottom"), Steel);
                masts.Add(column);
            }
            return (Paint(Stand("Crane Rail Block", V(x, RailFoot, z), Up, column, "bottom"), Rail), masts);
        }

        /// <summary>
        /// The north end wall under the gable, the shed's back over the generator hall: Shed Columns (22.5 m) 2 m inside the end truss every
        /// 8 m from x 3, Wall Sheets east from each, the floor to 20.3 m (above that the gable's sheets come down to 22.2 m in their plane,
        /// so the band between is left open: the hall's vent), the last span a 4 m sheet.
        /// </summary>
        private void LayNorthWall()
        {
            var z = NorthLines[0] - 2; // (2 m inside the end truss's line: the outer columns' masts reach to z 0, and a sheet past them would cut through)
            for (var x = 3f; x < RailX[^1] - 1; x += 8)
            {
                var wide = x + 8 <= RailX[^1] - 1 ? 8 : 4;
                var column = Paint(Stand("Shed Column 22.5 m", V(x, FloorY, z), Up, Tile(x, z), "foot"), Steel);
                foreach (var y in new[] { 1, 5, 9, 13, 17 })
                    Paint(Stand($"Wall Sheet {wide}x4 m", SocketAt(column, $"+z{y}"), East, column, "back"), Cladding);
            }
        }

        private static string Truss(int i) => $"Roof Truss Half {Halves[i].Length} m";

        /// <summary>A part's socket's place (site coordinates).</summary>
        private Vector3 SocketAt(VPart p, string name)
        {
            if (!p.Def.Sockets.Any(x => x.Name == name))
                throw new VehicleKitException($"a {p.Name} has no socket \"{name}\"");
            var s = p.Def.Sockets.First(x => x.Name == name);
            ref var cp = ref Live.Parts[p.Index];
            return FromRest(cp.Pos + Rot24.Apply(cp.Rot, s.At));
        }
    }
}
