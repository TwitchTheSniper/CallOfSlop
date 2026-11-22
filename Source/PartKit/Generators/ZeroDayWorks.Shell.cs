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

        private void LayShell()
        {
            var ks = Enumerable.Range(0, RailBlocks).Where(k => _columns.ContainsKey((0, k))).ToList();
            var trusses = new Dictionary<int, VPart[]>();
            var westPosts = new Dictionary<int, VPart>();
            var eastPosts = new Dictionary<int, VPart>();
            foreach (var k in ks)
            {
                var w = _rails[0][k];
                var e = _rails[RailX.Length - 1][k];
                var pw = Paint(Stand("Eaves Post", SocketAt(w, "-x"), Up, w, "rail"), Steel);
                var pe = Paint(Stand("Eaves Post", SocketAt(e, "+x"), South, e, "rail"), Steel);
                westPosts[k] = pw;
                eastPosts[k] = pe;
                // The chain: west halves (low end west, own +z east) and east halves (ridge west, own +z west), joined ridge to ridge and low to low.
                var h = new VPart[Halves.Length];
                h[0] = Paint(Stand(Truss(0), SocketAt(pw, "seat"), East, pw, "seat"), Steel);
                for (var i = 1; i < Halves.Length; i++)
                {
                    var join = i % 2 == 1 ? "ridge" : "low";
                    h[i] = Paint(Stand(Truss(i), SocketAt(h[i - 1], join), i % 2 == 1 ? West : East, h[i - 1], join), Steel);
                }
                trusses[k] = h;
            }

            for (var b = 0; b + 1 < ks.Count; b++)
            {
                int north = ks[b], south = ks[b + 1];
                var wide = 2 * (south - north); // 8, or 4 for the last bay
                var tn = trusses[north];
                var ts = trusses[south];
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
                foreach (var (run, k, o, face, posts) in new[] { (0, south, Up, "-x", westPosts), (RailX.Length - 1, north, South, "+x", eastPosts) })
                {
                    var masts = _columns[(run, k)];
                    for (var m = 0; m < masts.Count; m += 2)
                        if (run != 0 || m + 2 < masts.Count) // (the west wall stops at 16.3 m: bay 1's Crane Core runs outside its rail there, 17.3 to 19.3 m)
                            Paint(Stand($"Wall Sheet {wide}x4 m", SocketAt(masts[m], face), o, masts[m], "back"), Cladding);
                    Paint(Stand($"Wall Sheet {wide}x3 m Eaves", SocketAt(posts[k], "wall"), o, posts[k], "back"), Cladding);
                }
            }
            // The gables over both end trusses, outside them. A Gable Sheet stands on a truss's +x face and rises along the truss: on a west half
            // (own +x south) that face is outside only at the south end, on an east half (own +x north) only at the north end. The other three
            // can't be turned outward (that would be a mirror image), so each is welded on freeform 6 mm off the truss's -x face, outside.
            foreach (var (k, north) in new[] { (ks[0], true), (ks[^1], false) })
                for (var i = 0; i < Halves.Length; i++)
                {
                    var h = trusses[k][i];
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
                    var thick = d.BoundsMax.X + 0.006f;
                    var origin = face + new Vector3(0, 0, north ? thick : -thick);
                    var centre = west ? origin + new Vector3(mid.Z, mid.Y, -mid.X) : origin + new Vector3(-mid.Z, mid.Y, mid.X);
                    Paint(Weld(gable, h, centre, west ? East : West, 0, 0, 0), Cladding);
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
