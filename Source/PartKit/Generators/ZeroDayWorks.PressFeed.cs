using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class ZeroDayWorks
{
    private sealed partial class Script
    {
        /// <summary>The presses' own Extruders, in the order of <see cref="_presses"/>.</summary>
        private readonly List<VPart> _pressExtruders = new();

        /// <summary>Each press's starting die, in the order of <see cref="_presses"/>.</summary>
        private readonly List<string> _pressDies = new();

        /// <summary>The dies whose kit (bay 2) is bulk copper: their presses get a chute from the copper bin.</summary>
        private static readonly string[] CopperKitDies = ["Board Die", "Button Die"];

        public const float PressFeedGap = 1.5f; // (an Extruder's die face to its press's bay mouth: the conveyor between, longer than any press blank)

        /// <summary>
        /// Each Press's own Extruder (Sean, 2026-11-23: a press works through a great many small blanks, so each takes them straight from
        /// its own Extruder, not over the shared belts or by crane). An Extruder Core stands south-west of each Press Core, its die facing north,
        /// and puts each blank it cuts on a short conveyor into the press's bay 1 (an order's chain sees
        /// the conveyor as its way to the press, so a press's blank orders go to its own Extruder). Its stock comes by chute from the steel
        /// and laminate bins (an Extruder on orders takes only its head order's material), and the Board and Button presses' bay 2 kit hoppers by chute
        /// from the copper bin (their dies' kit). The chutes come over the shed at 12 to 16.5 m, above the press belts
        /// (6.5 and 7.5 m) and the Assembler's chutes (9.5 to 11.5 m), and drop west of each press and Extruder, clear of the belts
        /// that rise over the Cores.
        /// </summary>
        private void FeedPresses()
        {
            var up = 12f;
            for (var k = 0; k < _presses.Count; k++)
            {
                var press = _presses[k];
                var (bay, bayFace) = PortOf(press, "Piece in");
                // The Extruder: on the floor's whole metres, its die PressFeedGap or a little more short of the mouth. It can't stand on
                // bay 1's line (0.31 m east of the Core's middle: it would stand in the bed), so it stands a metre west and its conveyor
                // jogs east to the bay. (Not welded on freeform to line it up: a conveyor measures its length with its end ports in their
                // own frames, so one from a freeform part's port ran 14 m and more and its blank never arrived.)
                var d = Def("Extruder");
                var ex = MathF.Round(bay.X) - 1;
                var ez = MathF.Floor(bay.Z + bayFace.Z * PressFeedGap - d.BoundsMax.Z); // (the bay faces south: the die face at least 1.5 m south of the mouth)
                var ext = On("Extruder", ex, ez, Up, paint: General);
                OrdersOnly(ext);
                _pressExtruders.Add(ext);
                Belt(ext, "Piece out", press, "Piece in", null, General2); // (out of the die, east along under the Core's front, into bay 1)

                // Its stock: steel and laminate, each down 1.5 m west of the Extruder to 3 m, then east and into its back.
                var (back, fb) = PortOf(ext, "Chute in");
                var b1 = back + fb;
                var west = b1.X - 1.5f;
                foreach (var m in new[] { "steel", "laminate" })
                {
                    var (a, fa) = PortOf(_bins[m], "Chute out");
                    var a1 = a + fa;
                    Link("chute", _bins[m], ext, "Chute out", "Chute in", Stock, false, null,
                        Cut(a, a1, V(a1.X, up, a1.Z), V(west, up, a1.Z), V(west, up, b1.Z), V(west, 3f, b1.Z), V(b1.X, 3f, b1.Z), b1));
                    up += 0.5f;
                }

                // The kit hopper (bay 2, the Core's west bay) of a Board or Button press: copper, down 1 m west of the Core and in at its
                // west side. (Not into the others: a press refuses a kit its die doesn't take, and says so on its look line.)
                if (!CopperKitDies.Contains(_pressDies[k]))
                    continue;
                var (lo, hi) = BoundsOf(press);
                var cz = (lo.Z + hi.Z) * 0.5f;
                var (c0, cf) = PortOf(_bins["copper"], "Chute out");
                var c1 = c0 + cf;
                var cx = lo.X - 1f;
                Link("chute", _bins["copper"], press, "Chute out", null, Stock, false, null,
                    Cut(c0, c1, V(c1.X, up, c1.Z), V(cx, up, c1.Z), V(cx, up, cz), V(cx, 1.4f, cz), V(lo.X - 0.2f, 1.4f, cz)));
                up += 0.5f;
            }
        }
    }
}
