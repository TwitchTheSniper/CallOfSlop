using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The penal colony's lore items: Constructs/Parts/Lore (docs/story/STORY.md "Lore"; the words are Story/Lore). Each is a
/// thing of its own a player can look at closely in the colony's buildings: a tally scratched into a bed board, the photo
/// board with a gap in year three, the Warden's handbook on its lectern, the Ministry's in-tray, the barracks noticeboard
/// with Crumb's postcard, the Sergeant's key board with its eighth hook, a brass storm lantern from the Brine Flats,
/// Nettles' clipboard on a crate, a box of old schoolbooks and the old mine's shift board. Every one stands on its own
/// feet (the host stands it on a floor: Game/GameStoryLore.cs) and faces its own +z. None has a recipe (the assembling
/// skips the Lore folder): they are the colony's, not a player's to make. Details stand 4 mm proud of what they are on, and
/// at least 1 cm thick (a thinner box is dropped); faces that meet are kept apart (CLAUDE.md, "No coplanar faces").
/// </summary>
public sealed class LoreParts : IPartGenerator
{
    public string Name => "lore_parts";

    private const string Out = "Constructs/Parts/Lore";
    private const string Section = "Furniture", Group = "Vehicle Body";
    private const string Wood = "#6b4a2e", WoodDark = "#4a3220", WoodPale = "#9a7650", Cork = "#b08350", CorkPale = "#c9a271";
    private const string Paper = "#f2efe6", PaperOld = "#e2d6b8", Ink = "#2a2c30", Brass = "#c8a04a", BrassBright = "#efd27a";
    private const string Grey = "#7d8287", GreyDark = "#55595e", Iron = "#3a3c3f", Chalk = "#2f3a33", ChalkLine = "#d9ddd2";
    private const string RedCover = "#7a2622", Concrete = "#8a867e", ConcreteDark = "#6e6a63", Enamel = "#3d4a42", RedEnamel = "#8a1f1a", BlueCover = "#25467a", GreenCover = "#2f5a36", Light = "#f4e27a";

    private static PyDict NoPaint => Kw(("paintable", false));

    /// <summary>A detail on a front face at z: 16 mm thick (the corrector may take a few off), 4 mm proud of it.</summary>
    private static PyDict? Face(double x0, double y0, double x1, double y1, double z, string colour) =>
        Box([x0, y0, z - 0.012], [x1, y1, z + 0.004], colour, detail: true, shape: NoPaint);

    private static void Feet(Part p, params double[] xs)
    {
        for (var i = 0; i < xs.Length; i++)
            p.Sock($"foot{i}", [xs[i], 0, 0], "-y");
    }

    /// <summary>A board standing on two legs, its face at +z: the bottom of the board at lift, the board w wide, h high, 3 cm thick.</summary>
    private static double Standing(Part p, double w, double h, double lift, string board, string legs = WoodDark)
    {
        p.Add(Box([-w / 2, lift, -0.015], [w / 2, lift + h, 0.015], board));
        foreach (var x in new[] { -w / 2 + 0.06, w / 2 - 0.1 })
        {
            p.Add(Box([x, 0.05, -0.05], [x + 0.04, lift + 0.12, -0.016], legs)); // (behind the board, up past its bottom)
            p.Add(Box([x - 0.03, 0, -0.18], [x + 0.07, 0.05, 0.12], legs)); // (a foot, wide enough to stand)
        }
        Feet(p, -w / 2 + 0.08, w / 2 - 0.08);
        return 0.015; // (the face)
    }

    private static Part Tally()
    {
        var p = new Part("Scratched Tally Board", Small, Section, "An old bed board from the cell block, scratched with days in fives. The last row stops at three.", color: Wood, group: Group);
        var z = Standing(p, 0.7, 0.9, 0.05, WoodPale);
        // Rows of fives (four strokes and one across), neat at the top, worse lower down; then three, and an arrow up (north).
        for (var row = 0; row < 6; row++)
            for (var g = 0; g < 3; g++)
            {
                var x0 = -0.3 + g * 0.2 + row * 0.006;
                var y1 = 0.88 - row * 0.12;
                for (var s = 0; s < 4; s++)
                    p.Add(Face(x0 + s * 0.03, y1 - 0.08 - (row > 3 ? s % 2 * 0.012 : 0), x0 + s * 0.03 + 0.016, y1, z, WoodDark));
                p.Add(Face(x0 - 0.015, y1 - 0.048, x0 + 0.115, y1 - 0.032, z, WoodDark));
            }
        for (var s = 0; s < 3; s++)
            p.Add(Face(-0.3 + s * 0.03, 0.08, -0.284 + s * 0.03, 0.16, z, WoodDark));
        p.Add(Face(0.12, 0.08, 0.14, 0.2, z, Iron)); // (the arrow, cut deep: its shaft and head)
        p.Add(Face(0.1, 0.18, 0.16, 0.2, z, Iron));
        p.Add(Face(0.115, 0.2, 0.145, 0.22, z, Iron));
        p.Stats(6, 30, 0, 0.5);
        return p;
    }

    private static Part PhotoBoard()
    {
        var p = new Part("Inmate Photo Board", Small, Section, "A cork board of inmates' photographs, one row a year. Year three has a gap.", color: Cork, group: Group);
        var z = Standing(p, 1.0, 0.7, 0.8, Cork);
        for (var row = 0; row < 3; row++)
            for (var col = 0; col < 5; col++)
            {
                double x0 = -0.44 + col * 0.18, y0 = 1.32 - row * 0.22;
                if (row == 2 && col == 2)
                {
                    p.Add(Face(x0, y0, x0 + 0.13, y0 + 0.16, z, CorkPale)); // (the paler square where it hung)
                    p.Add(Box([x0 + 0.1, y0 + 0.13, z - 0.002], [x0 + 0.125, y0 + 0.155, z + 0.014], Paper, detail: true, shape: NoPaint)); // (a torn corner under the pin)
                    p.Add(Box([x0 + 0.105, y0 + 0.135, z], [x0 + 0.12, y0 + 0.15, z + 0.016], "#c03030", detail: true, shape: NoPaint));
                    continue;
                }
                p.Add(Face(x0, y0, x0 + 0.13, y0 + 0.16, z, Paper));
                p.Add(Box([x0 + 0.035, y0 + 0.05, z], [x0 + 0.095, y0 + 0.13, z + 0.016], row == 0 ? "#6e6252" : "#5a5e64", detail: true, shape: NoPaint)); // (a face)
            }
        p.Add(Box([-0.045, 0.9, z], [-0.029, 0.96, z + 0.016], Ink, detail: true, shape: NoPaint)); // (a biro arrow beside the gap)
        p.Stats(5, 25, 0, 0.5);
        return p;
    }

    private static Part Handbook()
    {
        var p = new Part("Warden's Handbook", Small, Section, "The colony handbook, four hundred pages, open on a lectern of its own.", color: WoodDark, group: Group);
        p.Add(Box([-0.22, 0, -0.18], [0.22, 0.04, 0.18], WoodDark)); // (the foot)
        p.Add(Box([-0.05, 0.04, -0.05], [0.05, 1.0, 0.05], Wood)); // (the post)
        p.Add(Box([-0.26, 1.0, -0.2], [0.26, 1.04, 0.2], Wood)); // (the desk)
        p.Add(Box([-0.26, 1.04, 0.16], [0.26, 1.07, 0.2], WoodDark)); // (the lip the book rests on)
        p.Add(Box([-0.23, 1.042, -0.16], [0.23, 1.06, 0.15], RedCover)); // (the covers, open)
        p.Add(Box([-0.22, 1.06, -0.15], [-0.008, 1.1, 0.14], Paper, shape: NoPaint)); // (the pages, thick on both sides)
        p.Add(Box([0.008, 1.06, -0.15], [0.22, 1.095, 0.14], Paper, shape: NoPaint));
        p.Add(Box([-0.012, 1.055, -0.15], [0.012, 1.09, 0.14], RedCover)); // (the spine's fold)
        for (var k = 0; k < 6; k++)
            p.Add(Box([0.04, 1.088, 0.1 - k * 0.04], [0.19 - k % 2 * 0.04, 1.104, 0.116 - k * 0.04], Ink, detail: true, shape: NoPaint)); // (rule 212 and its margin)
        Feet(p, 0);
        p.Stats(8, 30, 0, 0.5);
        return p;
    }

    private static Part InTray()
    {
        var p = new Part("Ministry In-Tray", Small, Section, "A filing cabinet with a wire in-tray of Ministry memos on top, every one dated a Thursday.", color: Grey, group: Group);
        double w = 0.24, d = 0.3, h = 0.8;
        p.Add(Box([-w, 0, -d], [w, h, d], Grey)); // (the cabinet)
        for (var k = 0; k < 3; k++)
        {
            p.Add(Box([-w + 0.02, 0.04 + k * 0.25, d - 0.006], [w - 0.02, 0.25 + k * 0.25, d + 0.008], GreyDark)); // (a drawer front)
            p.Add(Box([-0.06, 0.2 + k * 0.25, d + 0.004], [0.06, 0.22 + k * 0.25, d + 0.03], Iron)); // (its handle)
            p.Add(Box([-0.04, 0.12 + k * 0.25, d + 0.002], [0.04, 0.16 + k * 0.25, d + 0.018], Paper, detail: true, shape: NoPaint)); // (its label)
        }
        p.Add(Box([-0.18, h + 0.002, -0.22], [0.18, h + 0.014, 0.2], Iron)); // (the tray's floor)
        foreach (var x in new[] { -0.18, 0.168 })
            p.Add(Box([x, h + 0.014, -0.22], [x + 0.012, h + 0.08, 0.2], Iron)); // (its wire sides)
        p.Add(Box([-0.168, h + 0.014, -0.2], [0.168, h + 0.06, 0.18], PaperOld, shape: NoPaint)); // (the memos)
        p.Add(Box([-0.15, h + 0.06, -0.17], [0.155, h + 0.072, 0.19], Paper, shape: NoPaint)); // (the top one, askew)
        p.Add(Box([-0.12, h + 0.066, 0.1], [0.12, h + 0.082, 0.13], Ink, detail: true, shape: NoPaint)); // (MEMO 1,144)
        p.Add(Box([-0.12, h + 0.066, -0.05], [0.05, h + 0.082, -0.03], Ink, detail: true, shape: NoPaint));
        p.Add(Box([-0.14, h + 0.066, -0.15], [-0.11, h + 0.082, -0.12], "#9a9ea2", detail: true, shape: NoPaint)); // (staple holes, nothing in them)
        Feet(p, -0.12, 0.12);
        p.Stats(25, 60, 1, 1.0);
        return p;
    }

    private static Part Noticeboard()
    {
        var p = new Part("Barracks Noticeboard", Small, Section, "Rotas, a lost-glove notice, and a postcard from the centrifuge pinned in the corner.", color: Cork, group: Group);
        var z = Standing(p, 1.1, 0.75, 0.75, Cork);
        (double X0, double Y0, double X1, double Y1, string C)[] sheets =
        [
            (-0.5, 1.12, -0.22, 1.44, Paper), (-0.18, 1.1, 0.1, 1.44, Paper), (-0.48, 0.8, -0.24, 1.05, PaperOld), (-0.1, 0.82, 0.2, 1.04, Paper),
        ];
        foreach (var (x0, y0, x1, y1, c) in sheets)
        {
            p.Add(Face(x0, y0, x1, y1, z, c));
            for (var k = 0; k < 4; k++) // (the rota's lines)
                p.Add(Box([x0 + 0.03, y1 - 0.06 - k * 0.05, z], [x1 - 0.05, y1 - 0.044 - k * 0.05, z + 0.016], Ink, detail: true, shape: NoPaint));
        }
        p.Add(Face(0.26, 1.18, 0.5, 1.4, z, "#3a86b8")); // (the postcard: sky,)
        p.Add(Box([0.26, 1.18, z], [0.5, 1.24, z + 0.016], "#d8c27a", detail: true, shape: NoPaint)); // (sand,)
        p.Add(Box([0.36, 1.24, z], [0.4, 1.36, z + 0.016], "#e0e4e8", detail: true, shape: NoPaint)); // (and the centrifuge's arm)
        p.Add(Box([0.27, 1.32, z], [0.49, 1.34, z + 0.016], "#e0e4e8", detail: true, shape: NoPaint));
        foreach (var (x, y) in new[] { (0.27, 1.38), (0.48, 1.38), (-0.36, 1.42), (-0.04, 1.42) })
            p.Add(Box([x, y, z], [x + 0.016, y + 0.016, z + 0.024], "#c03030", detail: true, shape: NoPaint)); // (pins)
        p.Stats(5, 25, 0, 0.5);
        return p;
    }

    private static Part KeyBoard()
    {
        var p = new Part("Colony Key Board", Small, Section, "Seven brass hooks with keys and labels, and an eighth with neither, worn bright.", color: Wood, group: Group);
        var z = Standing(p, 0.95, 0.3, 1.25, Wood);
        for (var k = 0; k < 8; k++)
        {
            var x = -0.42 + k * 0.12;
            var last = k == 7;
            p.Add(Box([x, 1.42, z - 0.008], [x + 0.03, 1.45, z + 0.04], last ? BrassBright : Brass, detail: true, shape: NoPaint)); // (the hook)
            if (last)
                continue;
            p.Add(Box([x + 0.005, 1.33, z + 0.01], [x + 0.025, 1.43, z + 0.03], Brass, detail: true, shape: NoPaint)); // (the key, hung on it)
            p.Add(Face(x - 0.02, 1.27, x + 0.05, 1.31, z, Paper)); // (its label)
        }
        p.Stats(6, 25, 0, 0.5);
        return p;
    }

    private static Part Lantern()
    {
        var p = new Part("Brass Storm Lantern", Small, Section, "A brass storm lantern on a stool, polished, its wick trimmed and never lit. BRINE FLATS LIGHT.", color: Brass, group: Group);
        p.Add(Box([-0.18, 0.42, -0.18], [0.18, 0.46, 0.18], Wood)); // (the stool's seat)
        foreach (var x in new[] { -0.16, 0.12 })
            foreach (var zz in new[] { -0.16, 0.12 })
                p.Add(Box([x, 0, zz], [x + 0.04, 0.42, zz + 0.04], WoodDark)); // (its legs)
        double y = 0.46;
        p.Add(Box([-0.08, y + 0.002, -0.08], [0.08, y + 0.04, 0.08], Brass)); // (the lantern's base, the fount)
        p.Add(Box([-0.065, y + 0.04, -0.065], [0.065, y + 0.2, 0.065], hidden: true));
        p.Add(Box([-0.065, y + 0.04, -0.065], [0.065, y + 0.2, 0.065], "#d8e4e8", detail: true, glass: true, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+y")))); // (the glass)
        p.Add(Box([-0.012, y + 0.04, -0.012], [0.012, y + 0.08, 0.012], "#f0ead8", detail: true, shape: NoPaint)); // (the wick, trimmed square)
        foreach (var x in new[] { -0.075, 0.063 })
            p.Add(Box([x, y + 0.04, -0.006], [x + 0.012, y + 0.2, 0.006], Brass)); // (the guard wires)
        p.Add(Box([-0.075, y + 0.2, -0.075], [0.075, y + 0.24, 0.075], Brass)); // (the cap)
        p.Add(Box([-0.03, y + 0.24, -0.03], [0.03, y + 0.27, 0.03], Brass));
        p.Add(Box([-0.07, y + 0.27, -0.006], [0.07, y + 0.29, 0.006], Brass)); // (the handle)
        p.Add(Box([0.1, 0.46, 0.02], [0.15, 0.49, 0.07], Grey)); // (the tin of saved trimmings)
        Feet(p, -0.14, 0.14);
        p.Stats(5, 25, 0, 0.5);
        return p;
    }

    /// <summary>A slatted crate, w by d, h high, its top open: what is in it stands on its floor at 0.05.</summary>
    private static void Crate(Part p, double w, double d, double h)
    {
        p.Add(Box([-w, 0, -d], [w, 0.05, d], WoodDark)); // (the floor)
        foreach (var (x0, x1, z0, z1) in new[] { (-w, w, d - 0.03, d), (-w, w, -d, -d + 0.03), (-w, -w + 0.03, -d + 0.03, d - 0.03), (w - 0.03, w, -d + 0.03, d - 0.03) })
            for (var k = 0; k < 3; k++)
                p.Add(Box([x0, 0.05 + k * h / 3, z0], [x1, 0.05 + (k + 1) * h / 3 - 0.03, z1], Wood)); // (slats, a gap between)
        foreach (var x in new[] { -w, w - 0.05 })
            p.Add(Box([x, 0.05, d], [x + 0.05, 0.05 + h, d + 0.02], WoodDark)); // (the corner battens, in front)
        Feet(p, -w / 2, w / 2);
    }

    private static Part Clipboard()
    {
        var p = new Part("Officer Nettles' Clipboard", Small, Section, "A clipboard left on a crate: a lunch log, a margin, and a list that could be longer.", color: Wood, group: Group);
        double w = 0.3, d = 0.22, h = 0.42;
        p.Add(Box([-w, 0, -d], [w, 0.05, d], WoodDark)); // (a shut crate: its body and lid)
        p.Add(Box([-w + 0.01, 0.05, -d + 0.01], [w - 0.01, h, d - 0.01], Wood));
        p.Add(Box([-w, h, -d], [w, h + 0.03, d], WoodPale));
        p.Add(Face(-0.25, 0.1, 0.25, 0.14, d - 0.01, WoodDark)); // (MINISTRY PROPERTY, stencilled)
        var y = h + 0.03;
        p.Add(Box([-0.13, y + 0.002, -0.17], [0.13, y + 0.014, 0.17], "#8a6a44")); // (the clipboard)
        p.Add(Box([-0.12, y + 0.014, -0.16], [0.12, y + 0.026, 0.15], Paper, shape: NoPaint)); // (the lunch log)
        for (var k = 0; k < 6; k++)
            p.Add(Box([-0.1, y + 0.022, 0.1 - k * 0.04], [0.04, y + 0.038, 0.116 - k * 0.04], Ink, detail: true, shape: NoPaint));
        foreach (var k in new[] { 1, 3 })
            p.Add(Box([0.06, y + 0.022, 0.095 - k * 0.04], [0.08, y + 0.038, 0.115 - k * 0.04], Light, detail: true, shape: NoPaint)); // (stars for the good lunches)
        p.Add(Box([-0.05, y + 0.014, 0.13], [0.05, y + 0.04, 0.17], Iron)); // (the clip)
        Feet(p, -0.15, 0.15);
        p.Stats(9, 30, 0, 0.5);
        return p;
    }

    private static Part Schoolbooks()
    {
        var p = new Part("Box of Schoolbooks", Small, Section, "A crate of books nobody signs out. On top: THE NIGHT SKY FOR YOUNG READERS.", color: Wood, group: Group);
        double w = 0.3, d = 0.2, h = 0.3;
        Crate(p, w, d, h);
        string[] covers = [RedCover, GreenCover, BlueCover, "#6a5a3a", RedCover, GreenCover, "#4a4a6a"];
        for (var k = 0; k < covers.Length; k++)
            p.Add(Box([-w + 0.04 + k * 0.074, 0.05, -0.14], [-w + 0.104 + k * 0.074, 0.27 - k % 3 * 0.02, 0.14], covers[k])); // (books standing)
        p.Add(Box([-0.16, h + 0.06, -0.12], [0.16, h + 0.08, 0.12], BlueCover)); // (the night sky book, lying on top)
        p.Add(Box([-0.03, h + 0.072, -0.02], [0.03, h + 0.088, 0.04], Light, detail: true, shape: Kw(("paintable", false), ("glow", true)))); // (its cover: the Light, and nothing else)
        p.Stats(10, 30, 0, 0.5);
        return p;
    }

    private static Part ShiftBoard()
    {
        var p = new Part("Mine Shift Board", Small, Section, "The old mine's chalk shift board. The last shift's names were never wiped. HOLD AT MARKER 9.", color: Chalk, group: Group);
        p.Add(Box([-0.5, 0.6, -0.015], [0.5, 1.3, 0.015], Chalk)); // (the board)
        p.Add(Box([-0.53, 0.57, -0.02], [0.53, 0.6, 0.03], Wood)); // (its frame: the chalk ledge,)
        p.Add(Box([-0.53, 1.3, -0.02], [0.53, 1.33, 0.02], Wood)); // (the top,)
        foreach (var x in new[] { -0.53, 0.5 })
            p.Add(Box([x, 0.6, -0.02], [x + 0.03, 1.3, 0.02], Wood)); // (the sides)
        foreach (var x in new[] { -0.44, 0.38 })
        {
            p.Add(Box([x, 0.05, -0.06], [x + 0.06, 0.6, -0.021], WoodDark)); // (the easel's legs, behind)
            p.Add(Box([x - 0.03, 0, -0.25], [x + 0.09, 0.05, 0.12], WoodDark)); // (its feet)
        }
        const double z = 0.015;
        for (var col = 0; col < 3; col++)
        {
            var x0 = -0.44 + col * 0.31;
            p.Add(Face(x0, 1.21, x0 + 0.16, 1.24, z, ChalkLine)); // (DAYS, NIGHTS, SICK)
            for (var k = 0; k < (col == 2 ? 2 : 5); k++)
                p.Add(Face(x0, 1.14 - k * 0.07, x0 + 0.12 + (k * 7 + col * 3) % 5 * 0.025, 1.155 - k * 0.07, z, ChalkLine)); // (names)
        }
        p.Add(Face(-0.44, 0.68, 0.3, 0.71, z, ChalkLine)); // (HOLD AT MARKER 9 ...)
        p.Add(Face(0.34, 0.66, 0.4, 0.74, z, "#f4f6ee")); // (... the 9, chalked again and again)
        p.Add(Box([0.1, 0.6, 0.0], [0.16, 0.62, 0.02], "#f4f6ee", detail: true, shape: NoPaint)); // (a stub of chalk on the ledge)
        Feet(p, -0.41, 0.41);
        p.Stats(8, 30, 0, 0.5);
        return p;
    }

    /// <summary>
    /// The Ministry's work board in the colony yard (the job board: Source/UI/UiJobBoard.cs): a cast concrete hood on two
    /// board-marked posts over a low plinth, an enamelled steel case between them under a red header plate, and four columns
    /// of work sheets, one per faction that posts work (the Ministry's buff triplicate, the IT Department's tickets, the Call
    /// Centre's pink flyers, Clan MacSlop's notices with a tartan tab). One sheet in a column is stamped TAKEN. Its front is +z.
    /// </summary>
    private static Part WorkBoard()
    {
        var p = new Part("Ministry Work Board", Small, Section, "WORK ALLOCATION. Every faction's jobs, posted in the yard, in triplicate.", color: Concrete, group: Group);
        p.Add(Box([-1.08, 0, -0.22], [1.08, 0.14, 0.22], ConcreteDark)); // (the plinth)
        foreach (var x in new[] { -1.0, 0.78 })
        {
            p.Add(Box([x, 0.08, -0.11], [x + 0.22, 2.2, 0.11], Concrete)); // (a post, cast in the plinth)
            for (var k = 0; k < 5; k++) // (the shuttering's boards, 4 mm proud of its front)
                p.Add(Box([x + 0.02, 0.3 + k * 0.38, 0.105], [x + 0.2, 0.32 + k * 0.38, 0.114], ConcreteDark, detail: true, shape: NoPaint));
        }
        p.Add(Box([-1.12, 2.16, -0.2], [1.12, 2.34, 0.34], Concrete)); // (the hood, out over the sheets against the snow)
        p.Add(Box([-1.1, 2.12, 0.22], [1.1, 2.18, 0.3], ConcreteDark)); // (its drip under the front)
        p.Add(Box([-0.79, 0.62, -0.04], [0.79, 2.02, 0.03], Enamel)); // (the steel case)
        p.Add(Box([-0.79, 0.62, 0.025], [0.79, 0.66, 0.07], Iron)); // (its sill,)
        p.Add(Box([-0.79, 1.98, 0.025], [0.79, 2.02, 0.07], Iron)); // (its head)
        const double z = 0.03;
        p.Add(Face(-0.72, 1.86, 0.72, 1.96, z, RedEnamel)); // (the header plate: WORK ALLOCATION)
        for (var k = 0; k < 14; k++)
            p.Add(Box([-0.62 + k * 0.09, 1.885, z + 0.002], [-0.57 + k * 0.09, 1.935, z + 0.014], Paper, detail: true, shape: NoPaint));
        (string Sheet, string Tab)[] cols = [(PaperOld, RedEnamel), (Paper, "#2a5db0"), ("#f0b8c8", "#e8c430"), (Paper, "#2f6a3a")];
        for (var c = 0; c < cols.Length; c++)
        {
            var x0 = -0.72 + c * 0.37;
            p.Add(Face(x0, 1.76, x0 + 0.33, 1.81, z, cols[c].Tab)); // (the column's tab, the faction's colour)
            if (c == 3)
                for (var k = 0; k < 3; k++)
                    p.Add(Box([x0 + 0.04 + k * 0.1, 1.76, z + 0.002], [x0 + 0.07 + k * 0.1, 1.81, z + 0.014], "#a02828", detail: true, shape: NoPaint)); // (tartan)
            for (var s = 0; s < 3; s++)
            {
                var y1 = 1.7 - s * 0.35;
                var w = s == 1 ? 0.28 : 0.31;
                p.Add(Face(x0 + 0.01, y1 - 0.3, x0 + w, y1, z, cols[c].Sheet)); // (a sheet)
                for (var k = 0; k < 4; k++) // (its lines)
                    p.Add(Box([x0 + 0.04, y1 - 0.06 - k * 0.055, z + 0.002], [x0 + w - 0.04 - (k * 5 + c) % 3 * 0.03, y1 - 0.045 - k * 0.055, z + 0.014], Ink, detail: true, shape: NoPaint));
                p.Add(Box([x0 + 0.15, y1 - 0.02, z + 0.002], [x0 + 0.17, y1, z + 0.02], Brass, detail: true, shape: NoPaint)); // (its drawing pin)
                if ((s + c) % 3 == 1) // (one stamped TAKEN, in purple)
                    p.Add(Box([x0 + 0.06, y1 - 0.26, z + 0.002], [x0 + 0.22, y1 - 0.22, z + 0.016], "#6a3a8a", detail: true, shape: NoPaint));
            }
        }
        p.Add(Box([0.66, 0.68, 0.036], [0.69, 0.9, 0.056], Iron, detail: true, shape: NoPaint)); // (a pencil on a string, for signing)
        Feet(p, -0.9, 0.9);
        p.Stats(120, 400, 0, 2);
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out,
    [
        ("", Tally()), ("", PhotoBoard()), ("", Handbook()), ("", InTray()), ("", Noticeboard()),
        ("", KeyBoard()), ("", Lantern()), ("", Clipboard()), ("", Schoolbooks()), ("", ShiftBoard()), ("", WorkBoard()),
    ]);
}
