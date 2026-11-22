using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class ZeroDayWorks
{
    private sealed partial class Script
    {
        // Dressing (doc "Dressing: steel-mill details"), from parts already on main: the rooms' furniture, and on the shed's columns
        // Hall Floodlights (on every second column, about 11 m up, under the crane rails, facing the lines; staggered so each bay is lit
        // from both sides), Deck Signs at head height and a Fire Point every 32 m. The doc's ten new dressing parts (High-Bay Pendant,
        // Amber Beacon, Lagged Main, ...) come with their own thread; the pipe banks, cable trays, floor markings and the yard's
        // lights are not laid yet.
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
        }

        /// <summary>Dressing on a socket where it fits (clear of what is there and of z-fighting), else left out.</summary>
        private void Dress(VPart on, string socket, string part, string paint)
        {
            try
            {
                Paint(Onto(on, socket, part, "back", clear: true), paint);
            }
            catch (VehicleKitException)
            {
                // (a bed's edge, a wall sheet or a chute there)
            }
        }
    }
}
