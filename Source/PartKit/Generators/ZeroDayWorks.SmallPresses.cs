using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class ZeroDayWorks
{
    private sealed partial class Script
    {
        // The small presses (Sean, 2026-11-23: "for small pieces ... have 2 small presses for things that are used a lot in the construction").
        // The frigate's bill, counted through its Assembler recipes, wants 48 Circuit Boards and 29 Gear Sets, the most of any pressed part,
        // and both are pressed from blanks under 0.45 m across, which a Press Core takes alone: the big 3 x 4 presses were wasted on them.
        // So two small presses stand in the free band south of the block Assembler (z -141 to -146, between its masts and the stores'
        // pads), each a Core with a 2-block bed east of it (its swing lid hinges on the south side, away from the masts), fed by its own Extruder east
        // of the bed, turned to face it. Their parts go on a belt into a Large Conveyor Join east of the Assembler and on into the press
        // belts' first Join, so the Router sends them where the order wants them, as the big presses' are.

        /// <summary>The small presses' dies, west to east, and the one material each one's blanks are cut from.</summary>
        private static readonly (string Die, string Blank)[] SmallPressDies = [("Gear Die", "steel"), ("Board Die", "laminate")]; // (the Boards, the most wanted, on the east one: its belt runs straight into the Join)

        public const float SmallPressZ = -143f; // the Cores' row: their beds 1 m clear of the Assembler's south masts, their feed belts 1.4 m clear of the stores' pads
        private static readonly float[] SmallPressX = [75f, 86f];
        public const float SmallChuteUp = 17f; // (over the big presses' feed chutes, 12 to 16.5 m; under the rails' foot at 18.3 m)

        private readonly List<VPart> _smallPresses = new();

        /// <summary>The two small presses, their Extruders and their stock chutes.</summary>
        private void LaySmallPresses()
        {
            var up = SmallChuteUp;
            for (var k = 0; k < SmallPressDies.Length; k++)
            {
                var (die, blank) = SmallPressDies[k];
                var press = SmallPress(SmallPressX[k], SmallPressZ);
                Onto(press, "dieslot", die, "mount");
                _makers.Add(press);
                _smallPresses.Add(press);

                // Its Extruder: east of the bed, turned West so its die faces the press, on the floor's whole metres; its conveyor runs
                // half a metre south of the bed into bay 1.
                var (lo, hi) = BoundsOf(press);
                var bedEnd = BoundsOf(BedEnd(press)).Hi.X;
                var d = Def("Extruder");
                var ex = MathF.Ceiling(bedEnd + 1.3f + d.BoundsMax.Z); // (turned West: its die face, own +z, at x - BoundsMax.Z)
                var ext = On("Extruder", ex, SmallPressZ, West, paint: General);
                OrdersOnly(ext);
                Belt(ext, "Piece out", press, "Piece in", null, General2);

                // Its stock: one chute from its blank's bin, over the shed at 17 m, south along a line west of the Extruder's back (the
                // east one's would run in the column row at x 95), down to 3 m, then east over the Extruder and down into its back.
                var (back, fb) = PortOf(ext, "Chute in");
                var b1 = back + fb;
                var leg = MathF.Min(b1.X, 92f);
                var (a, fa) = PortOf(_bins[blank], "Chute out");
                var a1 = a + fa;
                Link("chute", _bins[blank], ext, "Chute out", "Chute in", Stock, false, null,
                    Cut(a, a1, V(a1.X, up, a1.Z), V(leg, up, a1.Z), V(leg, up, b1.Z), V(leg, 3f, b1.Z), V(b1.X, 3f, b1.Z), b1));
                up += 0.5f;

                // The Board press's kit hopper (bay 2): copper, down 0.5 m west of the Core and south of its row (the west press's
                // Extruder chute comes down 1.1 m west of the Core, on the row), north under it at 1.4 m and in at the Core's west side.
                if (!CopperKitDies.Contains(die))
                    continue;
                var cz = (lo.Z + hi.Z) * 0.5f;
                var (c0, cf) = PortOf(_bins["copper"], "Chute out");
                var c1 = c0 + cf;
                var cx = lo.X - 0.5f;
                var south = lo.Z - 1.5f;
                Link("chute", _bins["copper"], press, "Chute out", null, Stock, false, null,
                    Cut(c0, c1, V(c1.X, up, c1.Z), V(cx, up, c1.Z), V(cx, up, south), V(cx, 1.4f, south), V(cx, 1.4f, cz), V(lo.X - 0.2f, 1.4f, cz)));
                up += 0.5f;
            }
        }

        /// <summary>
        /// The small presses' belts (laid with the press belts, whose first Join they run into): a Large Conveyor Join in the free floor east
        /// of the block Assembler takes the east (Board) press's parts straight in from the south and the west press's over the Assembler's south
        /// masts at 5.5 m, and sends them north at 8 m over the south presses and west along the Joins' row into the first Join's free west side.
        /// </summary>
        private void LaySmallPressBelts(VPart firstJoin)
        {
            var join = On("Large Conveyor Join", 86, -134, East, "-y.4.4", General2); // (turned East as the belts' other Joins: its Piece in 2 faces south, Piece in west)
            Belt(_smallPresses[1], "Piece out", join, "Piece in 2", null, General2);
            Belt(_smallPresses[0], "Piece out", join, "Piece in 3", 5.5f, General2); // (east over the Assembler's south masts, north into the Join's north side: under the Line Console's wires at 6 m, over the Block Rack's chute at 5 m)
            Belt(join, "Piece out", firstJoin, "Piece in", 8f, General2, zFirst: true);
        }

        /// <summary>A small Press: its Core by its "-y.4.4" at (x, z) and a 2-block bed east of it, one row: no walls.</summary>
        private VPart SmallPress(float x, float z)
        {
            var core = On("Press", x, z, Up, "-y.4.4", General);
            OrdersOnly(core);
            var b0 = Paint(Onto(core, "+x", "Press Block", "-x"), General);
            _smallBedEnds[core] = Paint(Onto(b0, "+x", "Press Block", "-x"), General);
            return core;
        }

        private readonly Dictionary<VPart, VPart> _smallBedEnds = new();

        private VPart BedEnd(VPart press) => _smallBedEnds[press];
    }
}
