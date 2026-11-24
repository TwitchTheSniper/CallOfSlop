using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The module socket standard's parts (docs/sandbox/design/module-sockets.md 2 and 14.1): Constructs/Parts/Modules.
///
/// A Module Socket W x L is a Medium-class plate of a whole-metre footprint (W along x, L along z, its origin the middle of its
/// first metre cell) whose back carries Medium sockets at each metre cell and Small ones on a 0.5 m grid, so it bolts onto
/// Small and Medium hulls; its face carries one "module" socket in the footprint's middle, which takes only a Module Plug of
/// the same footprint. The bolts round its rim (every 0.5 m) and the hazard rim are the look that says "standard socket".
/// A Module Plug W x L is the module's base: a plate 5 mm inside the footprint (no coplanar sides) with its "module" socket under
/// it and a grid of sockets on top to weld anything onto, and a lifting lug at each corner. Its underside sits 15 mm over the plate's
/// drawn face (z-fighting rule). A Module Stand W x L is a plate on a frame, 0.9 m off the floor: a module on it is stored or
/// built (a bench), and nothing couples.
/// </summary>
public sealed partial class ModuleSocketParts : IPartGenerator
{
    public string Name => "module_sockets";

    private const string Out = "Constructs/Parts/Modules";
    private const string Group = "Ship Modules";
    private const string PlateC = "#3d444c", RimC = "#d8a320", BoltC = "#8c939b", PlugC = "#c9a21c", PlugTopC = "#5a6168", LugC = "#2b2f34", LampC = "#39465a", FrameC = "#4a525b";

    private static readonly (int W, int L)[] Sizes = [(1, 1), (1, 2), (1, 3), (2, 2), (2, 3), (3, 3)];

    private const double Back = -0.5; // a plate's back (the bottom of its first cell)
    private const double PlateTop = -0.38; // the plate's face, where its "module" socket is
    private const double Drawn = 0.003; // the drawn face stands this far under the socket (the plug's 3 mm over it)
    private const double PlugT = 0.1; // the plug's thickness
    private const double Inset = 0.005; // the plug stands this far inside the footprint

    private static string Size(int w, int l) => $"{w}x{l}";

    private const double FrameW = 0.12; // a plate's or plug's frame round the hole it shows in an Opening socket (the hazard rim's width)

    /// <summary>A box inside the frame (<c>"middle"</c>): left undrawn while the plate is an Opening socket, or the plug is on one (ModuleSockets.FrameLook).</summary>
    private static PyDict? Mid(PyDict? b)
    {
        b!["middle"] = true;
        return b;
    }

    /// <summary>A slab from (x0, z0) to (x1, z1), y0 to y1, as a frame <see cref="FrameW"/> wide round its footprint's edge (ex0..ex1, ez0..ez1) and a middle.</summary>
    private static void FramedSlab(Part p, double x0, double x1, double z0, double z1, double y0, double y1, double ex0, double ex1, double ez0, double ez1, string colour)
    {
        double ix0 = ex0 + FrameW, ix1 = ex1 - FrameW, iz0 = ez0 + FrameW, iz1 = ez1 - FrameW;
        p.Add(Box([x0, y0, z0], [x1, y1, iz0], colour));
        p.Add(Box([x0, y0, iz1], [x1, y1, z1], colour));
        p.Add(Box([x0, y0, iz0], [ix0, y1, iz1], colour));
        p.Add(Box([ix1, y0, iz0], [x1, y1, iz1], colour));
        p.Add(Mid(Box([ix0, y0, iz0], [ix1, y1, iz1], colour, detail: true))); // (drawn only: the hole an Opening shows lets things through; what is behind carries the rest)
    }

    /// <summary>
    /// The pipe couplings (module-sockets.md 2.5; the table's "couplings"): where each stands in the footprint's 0..1 square, the fluid, and
    /// the smallest area that has it. The plate takes a pipe in at its back; the plug's port of the same name gives it out of its top
    /// (ModuleSockets.PassThrough): the pass-through. Coolant is carried and does nothing yet.
    /// </summary>
    private static readonly (string Name, string Fluid, double U, double V, int Area)[] Pipes =
        [("Fuel A", "fuel", 0.85, 0.15, 1), ("Fuel B", "fuel", 0.15, 0.85, 4), ("Coolant A", "coolant", 0.85, 0.5, 4), ("Coolant B", "coolant", 0.15, 0.5, 9)];

    private static List<object?> PipePorts(int w, int l, double y, string dir, string face)
    {
        var list = Py.L();
        foreach (var c in Pipes)
        {
            if (w * l < c.Area)
                continue;
            var at = Py.L(Py.Round(-0.5 + c.U * w, 3), y, Py.Round(-0.5 + c.V * l, 3));
            var port = c.Fluid == "coolant"
                ? Kw(("name", c.Name), ("type", "pipe"), ("dir", dir), ("fluid", c.Fluid), ("at", at), ("face", face), ("noEffect", true))
                : Kw(("name", c.Name), ("type", "pipe"), ("dir", dir), ("fluid", c.Fluid), ("at", at), ("face", face));
            list.Add(port);
        }
        // Ammo and chutes (phase 3): after the pipes; each type counts its own port bytes, so the pipes' stay as they were.
        foreach (var c in Others)
        {
            if (w * l < c.Area)
                continue;
            list.Add(Kw(("name", c.Name), ("type", c.Type), ("dir", dir), ("at", Py.L(Py.Round(-0.5 + c.U * w, 3), y, Py.Round(-0.5 + c.V * l, 3))), ("face", face)));
        }
        return list;
    }

    /// <summary>The ammo feed and the chutes (module-sockets.md 2.5): Ammo A at every size (a 1x1 turret module is fed too), Chute A from 2x2, Chute B at 3x3.</summary>
    private static readonly (string Name, string Type, double U, double V, int Area)[] Others =
        [("Ammo A", "feed", 0.5, 0.15, 1), ("Chute A", "chute", 0.5, 0.85, 4), ("Chute B", "chute", 0.85, 0.85, 9)];

    /// <summary>The back's sockets: Medium at each metre cell, Small on the 0.5 m grid inside each.</summary>
    private static void BackSockets(Part p, int w, int l, double y, string dir)
    {
        for (var i = 0; i < w; i++)
            for (var j = 0; j < l; j++)
            {
                p.Sock($"back.{i}.{j}", [i, y, j], dir);
                for (var a = 0; a < 2; a++)
                    for (var b = 0; b < 2; b++)
                        p.Sock($"b{i}{a}.{j}{b}", [i - 0.25 + a * 0.5, y, j - 0.25 + b * 0.5], dir, Small);
            }
    }

    /// <summary>The bolts round a plate's rim, every 0.5 m, standing on its drawn face (detail).</summary>
    private static void Bolts(Part p, int w, int l, double top)
    {
        double x0 = -0.5 + 0.07, x1 = w - 0.5 - 0.07, z0 = -0.5 + 0.07, z1 = l - 0.5 - 0.07;
        void Bolt(double x, double z) => p.Add(Box([x - 0.022, top - 0.01, z - 0.022], [x + 0.022, top + 0.006, z + 0.022], BoltC, detail: true)); // (6 mm over the face: under the plug, which stands 15 mm over it)
        for (var k = 0; k <= w * 2; k++)
        {
            var x = x0 + (x1 - x0) * k / (w * 2);
            Bolt(x, z0);
            Bolt(x, z1);
        }
        for (var k = 1; k < l * 2; k++)
        {
            var z = z0 + (z1 - z0) * k / (l * 2);
            Bolt(x0, z);
            Bolt(x1, z);
        }
    }

    /// <summary>The plate itself: its slab from <paramref name="y0"/> up to the drawn face, the hazard rim, the bolts, the pad under its socket and the lamp.</summary>
    private static void Plate(Part p, int w, int l, double y0, double top)
    {
        var face = top - Drawn;
        FramedSlab(p, -0.5, w - 0.5, -0.5, l - 0.5, y0, face - 0.016, -0.5, w - 0.5, -0.5, l - 0.5, PlateC); // (its middle left out in an Opening socket: a frame)
        // The face: a hazard rim round a plain middle, one slab high.
        p.Add(Mid(Box([-0.5 + 0.12, face - 0.016, -0.5 + 0.12], [w - 0.5 - 0.12, face, l - 0.5 - 0.12], PlateC, detail: true)));
        p.Add(Box([-0.5 + 0.004, face - 0.016, -0.5 + 0.004], [w - 0.5 - 0.004, face, -0.5 + 0.12], RimC));
        p.Add(Box([-0.5 + 0.004, face - 0.016, l - 0.5 - 0.12], [w - 0.5 - 0.004, face, l - 0.5 - 0.004], RimC));
        p.Add(Box([-0.5 + 0.004, face - 0.016, -0.5 + 0.12], [-0.5 + 0.12, face, l - 0.5 - 0.12], RimC));
        p.Add(Box([w - 0.5 - 0.12, face - 0.016, -0.5 + 0.12], [w - 0.5 - 0.004, face, l - 0.5 - 0.12], RimC));
        Bolts(p, w, l, face);
        // The keying notch: a dark corner block (a keyed socket takes its module with the notched corners together).
        p.Add(Mid(Box([-0.5 + 0.13, face - 0.01, -0.5 + 0.13], [-0.5 + 0.25, face + 0.006, -0.5 + 0.25], LugC, detail: true)));
        double cx = (w - 1) * 0.5, cz = (l - 1) * 0.5;
        p.Add(Box([cx - 0.03, top - 0.012, cz - 0.03], [cx + 0.03, top, cz + 0.03], hidden: true)); // (the socket's solid face; never drawn: small, as it stands in an Opening's hole)
        p.Sock("module", [cx, top, cz], "+y", kind: "module");
    }

    private static Part Socket(int w, int l)
    {
        var p = new Part($"Module Socket {Size(w, l)}", Medium, "Module sockets",
            $"A standard {Size(w, l)} m socket: a Module Plug of its size fits it. F with a module near fits it; F again takes it off.",
            role: "frame", color: PlateC, group: Group);
        Plate(p, w, l, Back, PlateTop);
        Controls(p, (z, out x) => x = w - 0.5);
        p.Add(Mid(Box([w - 0.5 - 0.2, PlateTop - Drawn - 0.01, -0.5 + 0.14], [w - 0.5 - 0.14, PlateTop + 0.003, -0.5 + 0.2], LampC, detail: true))); // the lamp: the last box
        BackSockets(p, w, l, Back, "-y");
        p.Sock("bc", [(w - 1) * 0.5, Back, (l - 1) * 0.5], "-y", Small); // (phase 6: the back's middle, so a plate goes centred on a Small mount: after every older socket)
        var area = w * l;
        p.Stats(area * 85, 120 + area * Medium.HealthM2, Medium.Armour, seconds: 3.0 + area);
        p.Set(("onBreak", "detach"), ("moduleSocket", Kw(("w", w), ("l", l))), ("ports", PipePorts(w, l, Back, "in", "-y")));
        return p;
    }

    private const string ButtonC = "#c8372d";

    private delegate void SideAt(double z, out double x);

    /// <summary>
    /// The frame's controls on the plate's +x side, its -z end (Sean 2026-11-21; ModuleSockets.ControlAt, where F finds them): the eject
    /// button, a red cap in a dark housing, and the lock slider's dark track (its knob is drawn by its state, ConstructRender.Sockets). They
    /// stand out of the side a little (detail: drawn only, nothing collides with them).
    /// </summary>
    private static void Controls(Part p, SideAt side)
    {
        double y = ModuleSockets.CtlY;
        side(-0.5 + ModuleSockets.EjectZ, out var bx);
        var bz = -0.5 + ModuleSockets.EjectZ;
        p.Add(Box([bx - 0.004, y - 0.034, bz - 0.034], [bx + 0.01, y + 0.034, bz + 0.034], LugC, detail: true));
        p.Add(Box([bx + 0.008, y - 0.022, bz - 0.022], [bx + 0.02, y + 0.022, bz + 0.022], ButtonC, detail: true));
        // The track: short pieces along the side (a round plate's side curves).
        const int pieces = 4;
        for (var k = 0; k < pieces; k++)
        {
            double z0 = -0.5 + ModuleSockets.LockZ0 - 0.02 + (ModuleSockets.LockZ1 - ModuleSockets.LockZ0 + 0.04) * k / pieces;
            double z1 = -0.5 + ModuleSockets.LockZ0 - 0.02 + (ModuleSockets.LockZ1 - ModuleSockets.LockZ0 + 0.04) * (k + 1) / pieces;
            side((z0 + z1) * 0.5, out var tx);
            p.Add(Box([tx - 0.006, y - 0.016 - 0.001 * k, z0], [tx + 0.007 + 0.001 * (k & 1), y + 0.016 + 0.001 * k, z1 - 0.002], LugC, detail: true));
        }
    }

    private static Part Plug(int w, int l)
    {
        var p = new Part($"Module Plug {Size(w, l)}", Medium, "Module sockets",
            $"The base of a {Size(w, l)} m module: weld anything onto it, then fit it to a Module Socket of its size. Its lugs are where arms grip it.",
            role: "frame", color: PlugC, group: Group);
        double y0 = Back + 0.012, y1 = Back + PlugT; // (its underside 12 mm over its socket: 15 mm over the plate's drawn face, clear of its bolts)
        double x0 = -0.5 + Inset, x1 = w - 0.5 - Inset, z0 = -0.5 + Inset, z1 = l - 0.5 - Inset;
        FramedSlab(p, x0, x1, z0, z1, y0, y1 - 0.02, -0.5, w - 0.5, -0.5, l - 0.5, PlugC); // (a frame on an Opening socket, so the module passes back through it)
        FramedSlab(p, x0 + 0.03, x1 - 0.03, z0 + 0.03, z1 - 0.03, y1 - 0.02, y1, -0.5, w - 0.5, -0.5, l - 0.5, PlugTopC);
        double cx = (w - 1) * 0.5, cz = (l - 1) * 0.5;
        p.Add(Box([cx - 0.03, Back, cz - 0.03], [cx + 0.03, Back + 0.012, cz + 0.03], hidden: true)); // (the socket's solid face: small, as it stands in an Opening's hole)
        // The lifting lugs: a block at each corner with an eye over it (inside the footprint, so never out of the room).
        // (each 7.5 cm square, 1 cm in from the plug's edge: a part 1.8 m across, as a Zero Day Reactor, stands between a 2x2 plug's lugs)
        foreach (var (lx, lz) in new[] { (x0 + 0.01, z0 + 0.01), (x1 - 0.085, z0 + 0.01), (x0 + 0.01, z1 - 0.085), (x1 - 0.085, z1 - 0.085) })
        {
            p.Add(Box([lx, y1, lz], [lx + 0.075, y1 + 0.06, lz + 0.075], LugC));
            p.Add(Box([lx + 0.02, y1 + 0.06, lz + 0.025], [lx + 0.055, y1 + 0.1, lz + 0.05], BoltC, detail: true));
        }
        // The keying notch, matching the plate's corner.
        p.Add(Mid(Box([-0.5 + 0.13, y1 - 0.02, -0.5 + 0.13], [-0.5 + 0.25, y1 + 0.004, -0.5 + 0.25], LugC, detail: true)));
        p.Sock("module", [cx, Back, cz], "-y", kind: "module");
        for (var i = 0; i < w; i++)
            for (var j = 0; j < l; j++)
            {
                p.Sock($"top.{i}.{j}", [i, y1, j], "+y");
                for (var a = 0; a < 2; a++)
                    for (var b = 0; b < 2; b++)
                        p.Sock($"t{i}{a}.{j}{b}", [i - 0.25 + a * 0.5, y1, j - 0.25 + b * 0.5], "+y", Small);
            }
        // A Small part in each cell's middle (one 0.5 m wide clears the corner lugs): after every older socket, since saves keep links by socket index.
        for (var i = 0; i < w; i++)
            for (var j = 0; j < l; j++)
                p.Sock($"s{i}.{j}", [i, y1, j], "+y", Small);
        if (w % 2 == 0 || l % 2 == 0)
            p.Sock("tc", [(w - 1) * 0.5, y1, (l - 1) * 0.5], "+y", Small); // (phase 6: the top's middle where no cell's is, so one part stands centred: last)
        RimSockets(p, w, l, y1);
        var area = w * l;
        p.Stats(area * 60, 100 + area * Medium.HealthM2, Medium.Armour, seconds: 3.0 + area);
        p.Set(("onBreak", "detach"), ("modulePlug", Kw(("w", w), ("l", l))), ("ports", PipePorts(w, l, y1, "out", "+y")));
        return p;
    }

    /// <summary>
    /// Small sockets on a plug's frame (Sean 2026-11-21, the Opening style): the middle of its rim on each side, so a part such as a piston's
    /// base stands on the frame while what it moves passes through the hole. After every older socket (saves keep links by index).
    /// </summary>
    private static void RimSockets(Part p, int w, int l, double y)
    {
        double e = FrameW * 0.5, cx = (w - 1) * 0.5, cz = (l - 1) * 0.5;
        p.Sock("rz0", [cx, y, -0.5 + e], "+y", Small);
        p.Sock("rz1", [cx, y, l - 0.5 - e], "+y", Small);
        p.Sock("rx0", [-0.5 + e, y, cz], "+y", Small);
        p.Sock("rx1", [w - 0.5 - e, y, cz], "+y", Small);
    }

    private static Part Stand(int w, int l)
    {
        var p = new Part($"Module Stand {Size(w, l)}", Medium, "Module stands",
            $"A {Size(w, l)} m plate on a frame, 0.9 m up: a module stands on it, stored (nothing couples), or is built on it as on a bench.",
            role: "frame", color: FrameC, group: Group);
        const double top = 0.42, slab = 0.3;
        Plate(p, w, l, slab, top);
        // The frame: a leg at each corner and a foot rail round the floor.
        foreach (var (lx, lz) in new[] { (-0.5, -0.5), (w - 0.5 - 0.1, -0.5), (-0.5, l - 0.5 - 0.1), (w - 0.5 - 0.1, l - 0.5 - 0.1) })
            p.Add(Box([lx + 0.01, Back, lz + 0.01], [lx + 0.09, slab, lz + 0.09], FrameC));
        p.Add(Box([-0.5 + 0.1, Back, -0.5 + 0.02], [w - 0.5 - 0.1, Back + 0.08, -0.5 + 0.08], FrameC));
        p.Add(Box([-0.5 + 0.1, Back, l - 0.5 - 0.08], [w - 0.5 - 0.1, Back + 0.08, l - 0.5 - 0.02], FrameC));
        BackSockets(p, w, l, Back, "-y"); // (its feet: on a Medium or a Small floor)
        var area = w * l;
        p.Stats(area * 110, 120 + area * Medium.HealthM2, Medium.Armour, seconds: 3.0 + area);
        p.Set(("moduleSocket", Kw(("w", w), ("l", l), ("stand", true))));
        return p;
    }

    public void Make(PartOutput o)
    {
        var parts = new List<(string, Part)>();
        foreach (var (w, l) in Sizes)
        {
            parts.Add(("", Socket(w, l)));
            parts.Add(("", Plug(w, l)));
            parts.Add(("", Stand(w, l)));
        }
        WriteParts(o, Out, parts);
        // (the Module Fitter S went, 2026-11-24: the Kit Module Fitter preset stands for it; its folder Fitters is empty)
        WriteParts(o, Out + "/Round", RoundParts()); // (round sockets, Sean 2026-11-21: their own folder, rows at the Assembling tail's end)
        foreach (var a in Adapters)
            o.File($"Constructs/Blueprints/ModuleAdapter{Size(a.W, a.L)}to{Size(a.SW, a.SL)}.json", AdapterText(a));
        foreach (var m in Shipped)
            o.File($"Constructs/Blueprints/Module{m.File}.json", ShippedText(m));
    }

    /// <summary>
    /// The shipped module designs (module-sockets.md 9 and 10, phase 5): a plug of a size with stock parts welded on, one or two of
    /// each kind, so faction choice (ModuleChoice) has candidates with trade-offs to weigh and players have modules to buy and fit.
    /// Each part is (name, the plug's socket, its own socket). Later: --gen-module's faction-styled designs.
    /// </summary>
    private static readonly (string File, string Name, int W, int L, string Primary, string[] Tags, string About, (string Part, string On, string By)[] Parts)[] Shipped =
    [
        ("Rcs1x1", "Module RCS 1x1", 1, 1, "engine", ["engine"], "one RCS Thruster: a small push, a small draw", [("RCS Thruster", "s0.0", "base")]),
        ("RcsQuad2x2", "Module RCS Quad 2x2", 2, 2, "engine", ["engine"], "four RCS Thrusters: four times the push and the mass", [("RCS Thruster", "s0.0", "base"), ("RCS Thruster", "s1.0", "base"), ("RCS Thruster", "s0.1", "base"), ("RCS Thruster", "s1.1", "base")]),
        ("Gun1x1", "Module Gun 1x1", 1, 1, "turret", ["turret"], "an AA Gun: feed it through the socket's Ammo A", [("AA Gun", "s0.0", "bottom")]),
        ("TwinGun2x2", "Module Twin Gun 2x2", 2, 2, "turret", ["turret"], "two AA Guns", [("AA Gun", "s0.0", "bottom"), ("AA Gun", "s1.1", "bottom")]),
        ("Tank1x1", "Module Tank 1x1", 1, 1, "cargo", ["cargo"], "a Fuel Tank", [("Fuel Tank", "s0.0", "-y")]),
        ("Battery1x1", "Module Battery 1x1", 1, 1, "power-store", ["power-store"], "a Battery", [("Battery", "s0.0", "-y")]),
        ("Reactor3x3", "Module Reactor 3x3", 3, 3, "reactor", ["reactor"], "a Magpie Power Plant: 400 kW", [("Magpie Power Plant", "s1.1", "bottom")]),
        ("BigReactor3x3", "Module Big Reactor 3x3", 3, 3, "reactor", ["reactor"], "a Zero Day Reactor: 3 MW, heavy", [("Zero Day Reactor", "s1.1", "bottom")]),
    ];

    /// <summary>A plug's Small socket in its top's middle: a cell's middle when both sides are odd, else "tc".</summary>
    public static string CentreTop(int w, int l) => w % 2 == 1 && l % 2 == 1 ? $"s{w / 2}.{l / 2}" : "tc";

    /// <summary>
    /// A module design's blueprint text for another generator (the ships' retrofits, module-sockets.md 11): a plug of W x L, then each
    /// part (name, the step it goes on, that step's socket, its own socket, spin), the plug being step 0.
    /// </summary>
    public static string DesignText(string name, int w, int l, string primary, string[] tags, string about, string by, (string Part, int On, string Socket, string By, int Spin)[] parts)
    {
        var steps = new List<string> { $"    {{ \"part\": \"Module Plug {Size(w, l)}\" }}" };
        foreach (var (part, on, sock, own, spin) in parts)
            steps.Add($"    {{ \"part\": \"{part}\", \"on\": {on}, \"socket\": \"{sock}\", \"by\": \"{own}\"{(spin != 0 ? $", \"spin\": {spin}" : "")} }}");
        return "{\n" +
            "  \"format\": \"call-of-slop-construct-blueprint/1\",\n" +
            $"  \"name\": \"{name}\",\n" +
            $"  \"description\": \"A {Size(w, l)} module: {about}. Written by the part kit ({by}).\",\n" +
            $"  \"module\": {{ \"w\": {w}, \"l\": {l}, \"primary\": \"{primary}\", \"tags\": [{string.Join(", ", tags.Select(t => $"\"{t}\""))}] }},\n" +
            "  \"steps\": [\n" + string.Join(",\n", steps) + "\n  ]\n" +
            "}\n";
    }

    private static string ShippedText((string File, string Name, int W, int L, string Primary, string[] Tags, string About, (string Part, string On, string By)[] Parts) m)
    {
        var steps = new List<string> { $"    {{ \"part\": \"Module Plug {Size(m.W, m.L)}\" }}" };
        foreach (var (part, on, by) in m.Parts)
            steps.Add($"    {{ \"part\": \"{part}\", \"on\": 0, \"socket\": \"{on}\", \"by\": \"{by}\" }}");
        return "{\n" +
            "  \"format\": \"call-of-slop-construct-blueprint/1\",\n" +
            $"  \"name\": \"{m.Name}\",\n" +
            $"  \"description\": \"A {Size(m.W, m.L)} module: {m.About}. Written by the part kit (module_sockets).\",\n" +
            $"  \"module\": {{ \"w\": {m.W}, \"l\": {m.L}, \"primary\": \"{m.Primary}\", \"tags\": [{string.Join(", ", m.Tags.Select(t => $"\"{t}\""))}] }},\n" +
            "  \"steps\": [\n" + string.Join(",\n", steps) + "\n  ]\n" +
            "}\n";
    }

    /// <summary>
    /// The shipped adapters (module-sockets.md 4.4): a plug of one size with a Module Socket of a smaller one on its top, so a small
    /// module goes in a bigger socket. Each is a module blueprint: the plug, then the plate on the plug's Small grid where its middle
    /// lands on the plug's (<c>Plug</c> the plug's top socket, <c>By</c> the plate's back one).
    /// </summary>
    private static readonly (int W, int L, int SW, int SL, string Plug, string By)[] Adapters =
        [(2, 2, 1, 1, "t01.01", "b00.00"), (2, 2, 1, 2, "t01.00", "b00.00"), (3, 3, 1, 1, "t10.10", "b00.00"), (3, 3, 2, 2, "t01.01", "b00.00")];

    private static string AdapterText((int W, int L, int SW, int SL, string Plug, string By) a) =>
        "{\n" +
        "  \"format\": \"call-of-slop-construct-blueprint/1\",\n" +
        $"  \"name\": \"Module Adapter {Size(a.W, a.L)} to {Size(a.SW, a.SL)}\",\n" +
        $"  \"description\": \"A {Size(a.W, a.L)} module whose top is a {Size(a.SW, a.SL)} Module Socket: fit it, then fit a {Size(a.SW, a.SL)} module on it. Written by the part kit (module_sockets).\",\n" +
        $"  \"module\": {{ \"w\": {a.W}, \"l\": {a.L}, \"primary\": \"utility\", \"tags\": [\"utility\"] }},\n" +
        "  \"steps\": [\n" +
        $"    {{ \"part\": \"Module Plug {Size(a.W, a.L)}\" }},\n" +
        $"    {{ \"part\": \"Module Socket {Size(a.SW, a.SL)}\", \"on\": 0, \"socket\": \"{a.Plug}\", \"by\": \"{a.By}\" }}\n" +
        "  ]\n" +
        "}\n";
}
