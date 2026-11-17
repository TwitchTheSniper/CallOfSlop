namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The helpers the concrete buildings share (the outposts use them): pads, lids, doors, porches, lamps, switches and signs,
/// in the Brutalist style (docs/sandbox/constructs/buildings.md). The penal colony's own buildings that were made here were
/// removed with the colony (2026-11-17).
/// </summary>
public sealed partial class BuildingParts
{
    // The Ministry's palette (buildings.md "The Ministry of Slop"): concrete, weathered concrete, and one accent a building.
    private const string MinConcrete = "#a29e94", MinWeathered = "#858179", MinSlate = "#4b5157", MinGreen = "#2f5a3c", MinNavy = "#1f2b45",
        MinWhite = "#d6dad3", MinSoot = "#3a3836";

    private const string Sodium = "#ffb347"; // the lamps outside: warm against the snow
    private const string Tube = "#e4f2dc"; // the strip lights inside: cold and a little green

    private const int G = 2; // the ground floor's first row of cells (a pad is 2 cells thick)
    private const int Slab1 = G + WallH; // the slab over the ground floor

    /// <summary>A step's paint and finish (look.md "The authoring fields").</summary>
    private static int Look(Blueprint bp, int step, string paint, string? finish = null)
    {
        bp.Steps[step]["paint"] = paint;
        if (finish != null)
            bp.Steps[step]["finish"] = finish;
        return step;
    }

    private static void Concrete(Blueprint bp, IEnumerable<int> steps, string paint = MinConcrete)
    {
        foreach (var s in steps)
            Look(bp, s, paint, "concrete");
    }

    /// <summary>A notice in the colony's style (cream board, black letters, three lines), or a one-line cream strip where a
    /// notice board's height doesn't fit (<paramref name="size"/> Narrow), by its back on a face socket.</summary>
    private static int Sign(Blueprint bp, int on, string sock, string text, BoardSize size = BoardSize.Notice) => Board(size, bp, on, sock, text, BoardCream);

    /// <summary>A lit Text Sign by its back on a face socket (the other factions' signs: screens, not boards), saying <paramref name="text"/>.</summary>
    private static int TextSign(Blueprint bp, int on, string sock, string text)
    {
        if (text.Length > 60 || text.Any(c => c < ' ' || c > '~'))
            throw new InvalidOperationException($"{bp.Name}: sign text \"{text}\" (60 ASCII characters at most)");
        var s = bp.Hang("Text Sign", on, sock, "back");
        bp.Steps[s]["text"] = new List<object?> { text };
        return Look(bp, s, MinNavy);
    }

    // The penal colony's signs (buildings.md "Signs"): signwritten boards, not screens. A block's name in cream on Ministry
    // green; the rules in black on cream; a warning in cream on red. Every block has a number.
    private const string BoardGreen = "#2f5a3c", BoardCream = "#d8d1b4", BoardRed = "#8e2a22";

    private static int Board(BoardSize bl, Blueprint bp, int on, string sock, string text, string paint)
    {
        if (text.Length > 60 || text.Any(c => c < ' ' || c > '~'))
            throw new InvalidOperationException($"{bp.Name}: sign text \"{text}\" (60 ASCII characters at most)");
        var s = bp.Hang(bl switch { BoardSize.Wide => "Sign Board 3 m", BoardSize.Narrow => "Sign Board 2 m", _ => "Notice Board" }, on, sock, "back");
        bp.Steps[s]["text"] = new List<object?> { text };
        return Look(bp, s, paint);
    }

    private enum BoardSize { Wide, Narrow, Notice }

    /// <summary>A block's name board, 3 m (or 2 m where it hangs on a canopy): "BLOCK 4 - REQUISITIONS".</summary>
    private static int NameBoard(Blueprint bp, int on, string sock, string text, bool narrow = false) => Board(narrow ? BoardSize.Narrow : BoardSize.Wide, bp, on, sock, text, BoardGreen);

    /// <summary>A notice of rules, black on cream, three lines.</summary>
    private static int Notice(Blueprint bp, int on, string sock, string text) => Board(BoardSize.Notice, bp, on, sock, text, BoardCream);

    /// <summary>A warning, cream on red, one line on a 2 m board.</summary>
    private static int Warning(Blueprint bp, int on, string sock, string text) => Board(BoardSize.Narrow, bp, on, sock, text, BoardRed);

    /// <summary>A slogan over the street: a Roof Sign Frame by its middle foot on a lid's top socket, a 3 m board on it in cream on
    /// Ministry red, and two sodium lamps under its hood to light it at night.</summary>
    private static int Slogan(Blueprint bp, int lid, string sock, string text)
    {
        var f = bp.Hang("Roof Sign Frame 3 m", lid, sock, "b");
        Board(BoardSize.Wide, bp, f, "sign", text, BoardRed);
        Outside(bp, f, "lamp0");
        Outside(bp, f, "lamp1");
        return f;
    }

    /// <summary>A mains lamp by its back on a socket, glowing <paramref name="colour"/> (lit while the building's battery reaches it).</summary>
    private static int Lamp(Blueprint bp, string part, int on, string sock, string colour)
    {
        var s = bp.Hang(part, on, sock, "back");
        bp.Steps[s]["config"] = new PyDict { ["colour"] = colour };
        return s;
    }

    private static int Outside(Blueprint bp, int on, string sock) => Lamp(bp, "Bulkhead Light", on, sock, Sodium);

    private static int Strip(Blueprint bp, int on, string sock) => Lamp(bp, "Strip Light", on, sock, Tube);

    /// <summary>The room's light switch, on (a wire to each of its lamps: a player can turn the lights off, with a clunk).</summary>
    private static int Switch(Blueprint bp, int on, string sock, params int[] lamps)
    {
        var s = bp.Hang("Bakelite Switch", on, sock, "back");
        bp.Steps[s]["config"] = new PyDict { ["startOn"] = true };
        foreach (var l in lamps)
            bp.Wires.Add((s, l));
        return s;
    }

    /// <summary>A part standing on a pad's top socket: pad <paramref name="i"/>, <paramref name="j"/> (4 m squares) at its local x, z.</summary>
    private static double[] OnPad(int i, int j, double x, double z, double up = 0.375) => [4.0 * i + x, up, 4.0 * j + z];

    /// <summary>The pads, the first at (0, 0) then the rest in order, each next to one already down. <paramref name="floor"/>: the
    /// finish of the pads that are indoors (terrazzo or lino on top, plain concrete at their edges: Materials/Surfaces.json), those
    /// <paramref name="indoors"/> picks (all when null); the rest keep the foundation's concrete.</summary>
    private static void Pads(Blueprint bp, IEnumerable<(int I, int J)> pads, string? floor = null, Func<int, int, bool>? indoors = null)
    {
        const string Pad = "Concrete Foundation 4x4 m";
        var first = bp.First(Pad);
        if (floor != null && (indoors?.Invoke(0, 0) ?? true))
            bp.Steps[first]["finish"] = floor;
        foreach (var (i, j) in pads)
        {
            var s = bp.Put(Pad, Cell(16 * i, 0, 16 * j), R0);
            if (floor != null && (indoors?.Invoke(i, j) ?? true))
                bp.Steps[s]["finish"] = floor;
        }
    }

    /// <summary>A flat roof of 4 m slabs over cells x0.., z0.. (nx by nz), its slabs' row <paramref name="y"/>.</summary>
    private static List<int> Roof(Blueprint bp, int x0, int z0, int nx, int nz, int y)
    {
        var o = new List<int>();
        for (var z = z0; z < z0 + nz; z += 16)
            for (var x = x0; x < x0 + nx; x += 16)
                o.Add(Look(bp, bp.Put("Concrete Floor 4x4 m", Cell(x, y, z), R0), MinWeathered, "concrete"));
        return o;
    }

    /// <summary>The lid: fascias round a roof of 4 m slabs (cells x0.., z0.., nx by nz, the slabs' row y), and the corners where two
    /// sides meet. <paramref name="front"/> false leaves the -z side off (where a taller part of the building stands),
    /// <paramref name="back"/> false the +z side.</summary>
    private static List<int> Lid(Blueprint bp, int x0, int z0, int nx, int nz, int y, bool front = true, bool back = true)
    {
        const string Band = "Concrete Fascia 4 m", Corner = "Concrete Fascia Corner";
        var o = new List<int>();
        for (var x = x0; x < x0 + nx; x += 16)
        {
            if (front)
                o.Add(bp.Put(Band, Cell(x, y, z0 - 2), R0));
            if (back)
                o.Add(bp.Put(Band, Cell(x + 15, y, z0 + nz + 1), RY180));
        }
        for (var z = z0; z < z0 + nz; z += 16)
        {
            o.Add(bp.Put(Band, Cell(x0 - 2, y, z + 15), RY90));
            o.Add(bp.Put(Band, Cell(x0 + nx + 1, y, z), RY270));
        }
        if (front)
        {
            o.Add(bp.Put(Corner, Cell(x0 - 2, y, z0 - 2), R0));
            o.Add(bp.Put(Corner, Cell(x0 + nx + 1, y, z0 - 2), RY270));
        }
        if (back)
        {
            o.Add(bp.Put(Corner, Cell(x0 + nx + 1, y, z0 + nz + 1), RY180));
            o.Add(bp.Put(Corner, Cell(x0 - 2, y, z0 + nz + 1), RY90));
        }
        Concrete(bp, o, MinWeathered);
        return o;
    }

    /// <summary>A door on a doorway's hinge socket.</summary>
    private static void Door(Blueprint bp, int jamb, string leaf = "Wood Flush Door 1.5x2.25 m", string paint = MinGreen)
    {
        var h = bp.Hang("Door Hinge", jamb, "hinge", "base");
        Look(bp, bp.Hang(leaf, h, "leaf", "hang"), paint, leaf.StartsWith("Wood", StringComparison.Ordinal) ? "painted wood" : "worn paint");
    }

    /// <summary>The way in: a canopy over a door corner's doorway with a lamp under it and the building's name on its front.</summary>
    private static void Porch(Blueprint bp, int doorCorner, string name)
    {
        var c = Look(bp, bp.Hang("Concrete Canopy 2 m", doorCorner, "fa7_10", "back7"), MinWeathered, "concrete");
        Outside(bp, c, "lamp");
        NameBoard(bp, c, "sign", name, narrow: true);
    }

    private static int Steps(Blueprint bp, int x, int z) => Look(bp, bp.Put("Concrete Entrance Steps 2 m", Cell(x, 0, z - 4), R0), MinWeathered, "concrete");

    private static void Piers(Blueprint bp, string part, params (int On, string Sock)[] at)
    {
        foreach (var (on, sock) in at)
            Look(bp, bp.Hang(part, on, sock, "back5"), MinWeathered, "concrete");
    }
}
