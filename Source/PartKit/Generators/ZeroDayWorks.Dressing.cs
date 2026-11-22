using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class ZeroDayWorks
{
    private sealed partial class Script
    {
        // Dressing (doc "Dressing: steel-mill details"): the rooms' furniture, a High-Bay Pendant under each truss half, and on the shed's columns
        // Hall Floodlights (on every second column, about 11 m up, under the crane rails, facing the lines; staggered so each bay is lit
        // from both sides), Deck Signs at head height and a Fire Point every 32 m; the Lagged Main along the west wall, the ovens' heat shields, gauge clusters and bottle cages, the Foundry's ladle stand and slag pots, and floor markings
        // down bays 1 and 3. Not laid yet: pipe banks, cable trays and the yard's lights.
        //
        // Furniture stands against a wall by its "wall" socket (buildings.md "Laying out a building"), on the wall's inside face: a
        // south or east wall's "k" side, a north or west wall's "f" side, clear of the posts where walls meet (so a 2 m wall takes
        // nothing: the doc's service counter by the spare parts door has no room). What has no "wall" socket stands free on the floor.

        /// <summary>A piece of furniture on a wall's inside face at the wall's cell <paramref name="cell"/> (0, 7, 8 or 15), row 0.</summary>
        private VPart AtWall(VPart wall, char side, int cell, string part, string paint)
        {
            var face = side is 's' or 'e' ? "k" : "f";
            return Paint(Onto(wall, $"{face}{cell}_0", part, "wall"), paint);
        }

        /// <summary>A piece standing free on a floor cell, its back (own -z) toward <paramref name="back"/> ('n', 'e', 's' or 'w').</summary>
        private VPart OnFloor(VPart[,] g, int cx, int cz, char back, string part, string paint)
        {
            var (p, s) = Cell(g, cx, cz);
            var o = back switch { 'n' => South, 'e' => West, 's' => Up, _ => East };
            return Paint(Stand(part, SocketAt(p, s), o, p, "bottom"), paint);
        }

        private const string Furniture = "#59616b", Wood = "#7a5a3a", Green = "#4f7a45";

        /// <summary>The control room (6 x 12 m, the stair's opening in its south-west corner): desks with computers under the east
        /// windows, a teleprinter under the north ones, the chart table, the power board and the engineer's panel.</summary>
        private void FurnishControl(VPart[,] g, Dictionary<char, List<VPart>> w)
        {
            var desk1 = AtWall(w['e'][0], 'e', 7, "Steel Desk", Furniture);
            var desk2 = AtWall(w['e'][1], 'e', 7, "Steel Desk", Furniture);
            Paint(Onto(desk1, "top", "Computer", "foot00"), Furniture);
            Paint(Onto(desk2, "top", "Computer", "foot00"), Furniture);
            AtWall(w['n'][0], 'n', 0, "Teleprinter", Furniture);
            OnFloor(g, 8, 24, 'w', "Chart Table", Wood);
            OnFloor(g, 16, 32, 'e', "Engineer's Panel", Furniture);
            OnFloor(g, 16, 40, 'e', "Power Board", Furniture);
        }

        /// <summary>The service rooms at the foot of bay 3 (10 m wide; walls: the repair shop's west side three 4 m pieces, the other
        /// rooms' one; their north and south sides a 4 m and a 2 m piece; the east side's first or second piece the door).</summary>
        private void Furnish(string room, VPart[,] g, Dictionary<char, List<VPart>> w)
        {
            switch (room)
            {
                case "repair":
                    OnFloor(g, 8, 16, 'w', "Repair Station", Furniture);
                    OnFloor(g, 8, 40, 'w', "Repair Station", Furniture);
                    OnFloor(g, 16, 7, 's', "Tool Rack", Furniture);
                    AtWall(w['s'][0], 's', 7, "Store Shelving 2 m", Furniture);
                    AtWall(w['w'][2], 'w', 7, "Store Shelving 2 m", Furniture);
                    Paint(Onto(w['w'][0], "f7_5", "Tool Board", "back"), Wood);
                    Paint(Onto(w['w'][1], "f15_5", "Tool Board", "back"), Wood);
                    Paint(Onto(w['w'][0], "f0_10", "Wall Clock", "back"), Furniture);
                    // The Jib Crane on bay 3's west column by the shop (x 71, z -169), over its west side.
                    Paint(Onto(_columns[(3, 64)][2], "+x", "Jib Crane", "back"), Crane);
                    break;
                case "spares":
                    AtWall(w['w'][0], 'w', 7, "Store Shelving 2 m", Furniture);
                    AtWall(w['n'][0], 'n', 7, "Store Shelving 2 m", Furniture);
                    AtWall(w['n'][1], 'n', 7, "Store Shelving 2 m", Furniture);
                    AtWall(w['s'][0], 's', 7, "Store Shelving 2 m", Furniture);
                    AtWall(w['s'][1], 's', 7, "Store Shelving 2 m", Furniture);
                    AtWall(w['n'][0], 'n', 15, "Filing Cabinet", Furniture);
                    AtWall(w['s'][0], 's', 15, "Filing Cabinet", Furniture);
                    break;
                case "maintenance":
                    foreach (var c in new[] { 0, 7, 15 })
                        AtWall(w['w'][0], 'w', c, "Locker", Green);
                    OnFloor(g, 16, 7, 's', "Tool Rack", Furniture);
                    OnFloor(g, 24, 7, 's', "Galley Stove", Furniture);
                    var table = OnFloor(g, 16, 16, 's', "Mess Table", Wood);
                    Paint(Onto(table, "top", "Office Plant", "base"), Green);
                    AtWall(w['n'][0], 'n', 7, "Waiting Bench 2 m", Wood);
                    Paint(Onto(w['n'][0], "f7_5", "Cork Notice Board", "back"), Wood); // (over the bench)
                    break;
            }
        }

        private void LayDressing()
        {
            // Floodlights, signs and fire points on the shed's columns: the faces that look into a bay (the outer rows' inner side,
            // both sides of the inner rows).
            foreach (var ((run, k), masts) in _columns)
            {
                var faces = run == 0 ? new[] { "+x" } : run == RailX.Length - 1 ? new[] { "-x" } : new[] { "+x", "-x" };
                foreach (var f in faces)
                {
                    var east = f == "+x";
                    if (k % 16 == (east ? 0 : 8))
                        Dress(masts[5], f, "Hall Floodlight", Rail);
                    if (k % 8 == 4)
                        Dress(masts[1], f, "Deck Sign", Lane);
                    if (k % 16 == 12)
                        Dress(masts[0], f, "Fire Point", Power);
                }
            }
            // The Lagged Main: a steam main along the west wall, on the west columns' inner faces about 7 m up (mast 3): a 6 m piece
            // on each column and a 2 m piece on its south end, so the run is unbroken from column to column.
            foreach (var ((run, k), masts) in _columns.Where(x => x.Key.Run == 0).OrderBy(x => x.Key.K))
                if (DressAlong(masts[3], "+x", "Lagged Main 6 m", Power, Vector3.UnitZ) is { } main)
                    Dress(main, SocketAt(main, "-x").Z < SocketAt(main, "+x").Z ? "-x" : "+x", "Lagged Main 2 m", Power, "-x");
            // (No Amber Beacons on the cranes' Cores: a beacon on a Core's top stands up into its own bridge's way, and the crane stops.)
            // The ovens: a Heat Shield and a Gauge Cluster on each bed corner's foot mast (the faces a shield fits: along the bed's
            // sides, clear of its neighbours), and a Gas Bottle Cage on the floor beside each oven's Core.
            foreach (var m in _ovenMasts)
            {
                var shield = false;
                foreach (var f in new[] { "+z", "-z", "+x", "-x" })
                    if (!shield)
                        shield = Dress(m, f, "Heat Shield", Steel);
                    else if (Dress(m, f, "Gauge Cluster", Furniture))
                        break;
            }
            foreach (var oven in _ovens)
            {
                var at = SocketAt(oven, oven.Def.Sockets[0].Name);
                FloorTry("Gas Bottle Cage", Power, (at.X + 3, at.Z), (at.X - 3, at.Z), (at.X + 3, at.Z + 2), (at.X - 3, at.Z + 2));
            }
            // The Foundry's ladle stand and two slag pots, on the floor between the raw row and the bins (z -33), beside the Foundries.
            const float Between = (RawRow + BinRow) * 0.5f;
            FloorTry("Ladle Stand", Steel, (60, Between), (54, Between), (66, Between), (60, Between + 0.5f));
            FloorTry("Slag Pot", Steel, (54, Between), (66, Between), (52, Between), (68, Between));
            FloorTry("Slag Pot", Steel, (66, Between), (68, Between), (52, Between), (70, Between));
            // Floor markings: lane markings down bay 1's and bay 3's aisles every 8 m, an arrow south every 32 m (the flow).
            foreach (var x in new[] { 14f, 85f })
                for (var z = -50f; z > -194f; z -= 8f)
                    FloorTry(z % 32 == -18 ? "Floor Arrow" : "Lane Marking", Lane, (x, z));
            // A High-Bay Pendant under the middle of every truss half (two rows a span, one every 8 m along the shed), 0.7 m under
            // the chord, clear over the crane bridges.
            foreach (var h in _trusses.Values.SelectMany(x => x))
                Dress(h, $"hang{(h.Def.Sockets.Count(x => x.Name.StartsWith("hang")) + 1) / 2 * 2}", "High-Bay Pendant", Rail, "top");
        }

        /// <summary>A placing refused for want of room (something in the way, or a face on another's), which a leg or dressing may
        /// skip; anything else (a part or socket renamed, a cap reached) still throws.</summary>
        private static bool Refused(VehicleKitException e) => e.Message.Contains(" is in the way") || e.Message.Contains(": z-fighting: ") || e.Message.EndsWith("something is already attached there");

        /// <summary>Dressing on a socket where it fits (clear of what is there and of z-fighting), else left out. Whether it went on.</summary>
        private bool Dress(VPart on, string socket, string part, string paint, string by = "back")
        {
            try
            {
                Paint(Onto(on, socket, part, by, clear: true), paint);
                return true;
            }
            catch (VehicleKitException e) when (Refused(e))
            {
                return false; // (a bed's edge, a wall sheet or a chute there)
            }
        }

        /// <summary>Dressing whose own x runs along <paramref name="axis"/> (a pipe along a wall): the first quarter turn that does and fits, or null.</summary>
        private VPart? DressAlong(VPart on, string socket, string part, string paint, Vector3 axis, string by = "back")
        {
            for (var spin = 0; spin < 4; spin++)
                try
                {
                    return Paint(Onto(on, socket, part, by, spin, clear: true,
                        veto: (_, g) => MathF.Abs(Vector3.Dot(Rot24.Apply(g.Rot, Vector3.UnitX), axis)) > 0.9f ? null : "turned across"), paint);
                }
                catch (VehicleKitException e) when (Refused(e) || e.Message.EndsWith("turned across"))
                {
                }
            return null;
        }

        /// <summary>A free-standing piece on the floor (turned Up) at the first of <paramref name="spots"/> where it fits, else left out.</summary>
        private void FloorTry(string part, string paint, params (float X, float Z)[] spots)
        {
            foreach (var (x, z) in spots)
                try
                {
                    Paint(Stand(part, V(x, FloorY, z), Up, Tile(x, z), "bottom"), paint);
                    return;
                }
                catch (VehicleKitException e) when (Refused(e) || e.Message.Contains(": no socket takes a ")) // (a machine stands on that floor socket)
                {
                }
        }
    }
}
