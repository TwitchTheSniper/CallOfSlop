using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Recycler: Constructs/Parts/Storage/Recycler.json (docs/sandbox/constructs/production.md, "Recycling"). It was a hand-made file
/// with its status lamp added by hand; this writes it, so it follows "generators, not outputs". Its name, its keys in their order, its
/// nine solid boxes (a floor, four walls, a wider rim), sockets, bin, throat, screen, lamp and setting are the old file's. The look is
/// the machine look's (look.md "Machine look"): an open hopper with a white rim (the way in), two toothed shredder shafts in the throat
/// and a pulley on its side that turn while it works, a steel discharge door under an orange band on the control face, and the button
/// shelf under the screen. The folder holds other hand-made parts, so it is not owned: one file by <c>o.File</c>.
/// </summary>
public sealed class RecyclerParts : IPartGenerator
{
    public string Name => "recycler_parts";

    private const string Path = "Constructs/Parts/Storage/Recycler.json";
    private const string Body = "#5f7a34", Rim = "#7d9a4a", Screen = "#14181c";

    private static PyDict Recycler()
    {
        var boxes = new List<PyDict>();
        void Add(PyDict? b)
        {
            if (b is not null)
                boxes.Add(b);
        }
        // ---- The solid shape, as the hand-made file had it: a floor, four walls to 0.7 m and a rim a little wider up to 1.1 m.
        Add(Box([-0.5, 0, -0.5], [0.5, 0.05, 0.5]));
        Add(Box([-0.5, 0.05, 0.44], [0.5, 0.7, 0.5]));
        Add(Box([-0.5, 0.05, -0.5], [0.5, 0.7, -0.44]));
        Add(Box([-0.5, 0.05, -0.44], [-0.44, 0.7, 0.44]));
        Add(Box([0.44, 0.05, -0.44], [0.5, 0.7, 0.44]));
        Add(Box([-0.52, 0.66, 0.38], [0.52, 1.1, 0.46], Rim));
        Add(Box([-0.52, 0.66, -0.46], [0.52, 1.1, -0.38], Rim));
        Add(Box([-0.53, 0.67, -0.38], [-0.45, 1.08, 0.38], Rim));
        Add(Box([0.45, 0.67, -0.38], [0.53, 1.08, 0.38], Rim));
        // ---- The control face (+z): the screen's plate, the lamp over it, the button shelf under it.
        var plate = Box([-0.37, 0.72, 0.455], [0.37, 1.04, 0.48], Screen, detail: true)!;
        Add(plate);
        Add(new PyDict { ["min"] = Py.L(-0.37, 1.048, 0.462), ["max"] = Py.L(0.37, 1.094, 0.483), ["color"] = "#2b3036", ["detail"] = true, ["paintable"] = false, ["status"] = true });
        MachineLook.Buttons(boxes, plate, "+z");
        // ---- A dark skirt round the foot, and dark corner posts up the body (12 mm proud: parts stand against its sides).
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            Add(L([sx > 0 ? 0.45 : -0.512, 0.09, sz > 0 ? 0.45 : -0.512], [sx > 0 ? 0.512 : -0.45, 0.655, sz > 0 ? 0.512 : -0.45], MachineLook.Base));
        Add(L([-0.514, 0, -0.514], [0.514, 0.1, -0.5 + 0.004], MachineLook.Base));
        Add(L([-0.514, 0, 0.5 - 0.004], [0.514, 0.1, 0.514], MachineLook.Base));
        Add(L([-0.514, 0, -0.496], [-0.5 + 0.004, 0.1, 0.496], MachineLook.Base));
        Add(L([0.5 - 0.004, 0, -0.496], [0.514, 0.1, 0.496], MachineLook.Base));
        // ---- The way in: a white rim on the hopper's back and sides, hazard paint on the edge a hand reaches over.
        Add(L([-0.53, 1.086, -0.47], [0.53, 1.114, -0.37], MachineLook.In));
        Add(L([-0.54, 1.066, -0.368], [-0.44, 1.094, 0.37], MachineLook.In));
        Add(L([0.44, 1.066, -0.368], [0.54, 1.094, 0.37], MachineLook.In));
        Add(L([-0.53, 1.096, 0.372], [0.53, 1.114, 0.47], MachineLook.Accent, "hazard paint"));
        // A white band on the hopper's back with an arrow down into the throat.
        Add(L([-0.3, 0.8, -0.474], [0.3, 1.0, -0.458], MachineLook.In));
        // ---- The shredder: two steel shafts across the throat with dark teeth that mesh, turning while it works; the bin shows between them.
        foreach (var (z, odd) in new[] { (-0.12, false), (0.12, true) })
        {
            Add(MachineLook.Work(L([-0.44, 0.82, z - 0.045], [0.44, 0.91, z + 0.045], MachineLook.Steel, "steel"), "spin", "+x"));
            for (var k = 0; k < (odd ? 3 : 4); k++)
            {
                var x = odd ? -0.2 + 0.2 * k : -0.3 + 0.2 * k;
                Add(MachineLook.Work(L([x - 0.03, 0.787, z - 0.078], [x + 0.03, 0.943, z + 0.078], k % 2 == 0 ? MachineLook.Base : MachineLook.Accent), "spin", "+x"));
            }
        }
        // ---- The drive, on the +x side of the hopper (under 2 cm proud): a dark guard, an accent pulley and its steel hub that turn.
        Add(L([0.532, 0.7, -0.3], [0.544, 1.05, 0.3], MachineLook.Base));
        Add(MachineLook.Work(L([0.546, 0.77, -0.11], [0.56, 0.99, 0.11], MachineLook.Accent), "spin", "+x"));
        Add(MachineLook.Work(L([0.562, 0.845, -0.035], [0.576, 0.915, 0.035], MachineLook.Steel, "steel"), "spin", "+x"));
        Add(L([-0.544, 0.7, -0.3], [-0.532, 1.05, 0.3], MachineLook.Base)); // (the far side's bearing cover)
        Add(L([-0.558, 0.82, -0.06], [-0.546, 0.94, 0.06], MachineLook.Steel, "steel"));
        // ---- The way out, on the control face: a steel discharge door in a dark frame under an orange band.
        Add(L([-0.34, 0.12, 0.502], [0.34, 0.5, 0.514], MachineLook.Base));
        Add(L([-0.3, 0.15, 0.516], [0.3, 0.47, 0.528], MachineLook.Steel, "steel"));
        Add(L([-0.06, 0.38, 0.53], [0.06, 0.42, 0.55], MachineLook.Accent)); // its handle
        Add(L([-0.42, 0.53, 0.502], [0.42, 0.62, 0.516], MachineLook.Out));
        Add(L([-0.42, 0.102, 0.516], [0.42, 0.118, 0.53], MachineLook.Accent, "hazard paint"));
        var sockets = new List<PyDict>
        {
            Kw(("name", "bottom"), ("at", Py.L(0, 0, 0)), ("dir", "-y")),
            Kw(("name", "+x"), ("at", Py.L(0.5, 0.3, 0)), ("dir", "+x")),
            Kw(("name", "-x"), ("at", Py.L(-0.5, 0.3, 0)), ("dir", "-x")),
            Kw(("name", "back"), ("at", Py.L(0, 0.3, -0.5)), ("dir", "-z")),
        };
        PartZFight.Resolve(boxes, sockets); // no two drawn faces on one plane (CLAUDE.md, "No coplanar faces")
        return new PyDict
        {
            ["format"] = "call-of-slop-construct-part/1",
            ["name"] = "Recycler",
            ["description"] = "Breaks wrecked parts into material. Needs power.",
            ["role"] = "storage",
            ["tool"] = "welder",
            ["attachSeconds"] = 4.0,
            ["mass"] = 220,
            ["health"] = 600,
            ["armour"] = 8,
            ["material"] = "steel",
            ["power"] = 600,
            ["color"] = Body,
            ["boxes"] = boxes.Cast<object?>().ToList(),
            ["sockets"] = sockets.Cast<object?>().ToList(),
            ["storage"] = Kw(
                ("bulk", Kw(("material", "any"), ("capacity", 600), ("min", Py.L(-0.435, 0.055, -0.435)), ("max", Py.L(0.435, 0.64, 0.435)))),
                ("recycler", Kw(("min", Py.L(-0.43, 0.06, -0.43)), ("max", Py.L(0.43, 1.2, 0.43))))),
            ["panels"] = Py.L(Kw(("source", "machine"), ("at", Py.L(0, 0.88, 0.484)), ("size", Py.L(0.71, 0.25)), ("face", "+z"), ("up", "+y"), ("rows", 4), ("range", 30))),
            ["config"] = Py.L(Kw(("key", "ink"), ("type", "colour"), ("label", "Screen colour"), ("group", "Display"), ("default", "#000000"),
                ("tooltip", "The colour of what its screen shows. Black: its own colours."))),
        };
    }

    public void Make(PartOutput o) => o.File(Path, PyJson.Dumps(Recycler(), 2) + "\n");
}
