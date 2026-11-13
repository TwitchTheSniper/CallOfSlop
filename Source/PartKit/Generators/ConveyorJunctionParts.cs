using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Conveyor junctions: Constructs/Parts/ConveyorJunctions (docs/sandbox/constructs/production.md, "Conveyors"). A low steel table
/// with a roller deck that pieces cross whole from one conveyor to the next. A Splitter takes one conveyor in at -z and sends
/// its pieces out at +z, +x and -x in turn (round robin, past a cut, too small or full one); a Join takes three in (-z, +x,
/// -x) and sends them out at +z fairly. The ports sit at its edges' middles on the deck's line, so a piece runs on through
/// its middle (ConstructConveyor.LeadIn). Amber posts at the corners; a pale mark under each way out, an amber one under each
/// way in.
/// </summary>
public sealed class ConveyorJunctionParts : IPartGenerator
{
    public string Name => "conveyor_junction_parts";

    private const string Out = "Constructs/Parts/ConveyorJunctions";
    private const string Frame = "#35556b"; // (the body: it takes paint; the rest is MachineLook's tones)
    private const string Logistics = "#3f8f5a"; // (a Router's body: logistics green, no machine family's colour)
    private const string Unlit = "#3a3328"; // (a Router's way lamp while that way isn't chosen: drawn lit in the choice's colour, ConstructLogistics)

    private static readonly (string Face, double Dx, double Dz)[] Sides = [("-z", 0, -1), ("+z", 0, 1), ("+x", 1, 0), ("-x", -1, 0)];

    /// <summary>A junction n by n small cells (0.25 m), two high, passing pieces up to section by section; <paramref name="split"/>: one in, three out (else three in, one out).</summary>
    /// <para>
    /// <paramref name="router"/>: a Conveyor Router (docs/sandbox/constructs/production.md "Smart logistics"): the Splitter's table, its ports and
    /// bytes, in logistics green; its out arrows are lamps (<c>"way": 1..3</c>) lit in the colour of the way its last piece took, and a low steel
    /// fence on the deck (<c>"fence": true</c>) swings toward that way. No post (user, 2026-11-12: deck lamps only).
    /// </para>
    private static Part Junction(string name, int n, double section, bool split, string desc, bool router = false)
    {
        var c = Small;
        var p = new Part(name, c, "Extrusion", desc, tool: "welder", color: router ? Logistics : Frame, group: "Production");
        double x0 = c.Lo(), x1 = c.Hi(n), y0 = c.Lo(), y1 = c.Hi(2), z0 = c.Lo(), z1 = c.Hi(n);
        var top = y1 - 0.12; // the frame's top; the conveyors' line is 5 mm over it (their rollers' tops 6 cm over the line, as the deck's are)
        double cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
        p.Add(Box([x0, y0, z0], [x1, top, z1]));
        // The deck: a dark plate, and light rollers across it every 0.25 m (their tops 6 cm over the line, a piece's underside 5 mm over them).
        p.Add(MachineLook.D([x0 + 0.06, top - 0.005, z0 + 0.06], [x1 - 0.06, top + 0.02, z1 - 0.06], MachineLook.Base));
        for (var z = z0 + 0.15; z + 0.05 < z1 - 0.1; z += 0.25)
            p.Add(MachineLook.D([x0 + 0.13, top + 0.015, z], [x1 - 0.13, top + 0.06, z + 0.05], MachineLook.Steel, "steel"));
        // Amber posts at the corners (clear of the widest conveyor at each edge's middle).
        // (3 mm in from the frame's sides: where they overlap its top their faces would meet the frame's)
        foreach (var (px, pz) in new[] { (x0 + 0.003, z0 + 0.003), (x1 - 0.123, z0 + 0.003), (x0 + 0.003, z1 - 0.123), (x1 - 0.123, z1 - 0.123) })
            p.Add(MachineLook.D([px, top - 0.005, pz], [px + 0.12, y1, pz + 0.12], MachineLook.Accent, "hazard paint")); // (the guard posts: what a shin finds)
        // The ways in and out: a mark on each side face under its port (pale out, amber in), 4 mm proud.
        var ports = new List<object?>();
        var outs = 0;
        var ins = 0;
        foreach (var (face, dx, dz) in Sides)
        {
            var isIn = split ? face == "-z" : face != "+z";
            double ax = cx + dx * (x1 - x0) / 2, az = cz + dz * (z1 - z0) / 2;
            double[] mn = dz != 0 ? [cx - 0.2, top - 0.09, az - (dz > 0 ? 0.01 : 0.004)] : [ax - (dx > 0 ? 0.01 : 0.004), top - 0.09, cz - 0.2];
            double[] mx = dz != 0 ? [cx + 0.2, top - 0.03, az + (dz > 0 ? 0.004 : 0.01)] : [ax + (dx > 0 ? 0.004 : 0.01), top - 0.03, cz + 0.2];
            p.Add(MachineLook.D(mn, mx, isIn ? MachineLook.In : MachineLook.Out));
            // An arrow on the deck at each way, between the rollers' lines, pointing the way a piece goes: a shaft and a stepped head (white in, orange out).
            var way = isIn ? -1 : 1;
            var lamp = router && !isIn ? outs + 1 : 0; // (a Router's out arrow is the lamp of its way: 1 ahead, 2 and 3 the sides, in port order)
            var colour = isIn ? MachineLook.In : lamp > 0 ? Unlit : MachineLook.Out;
            double ay0 = top + 0.021, ay1 = top + 0.034, reach = (x1 - x0) / 2;
            void Mark(double a0, double a1, double w)
            {
                double lo = Math.Min(a0, a1), hi = Math.Max(a0, a1);
                var b = dz != 0 ? MachineLook.D([cx - w, ay0, cz + dz * lo - (dz < 0 ? hi - lo : 0)], [cx + w, ay1, cz + dz * lo + (dz > 0 ? hi - lo : 0)], colour)
                    : MachineLook.D([cx + dx * lo - (dx < 0 ? hi - lo : 0), ay0, cz + 0.05 - w * 0.55], [cx + dx * lo + (dx > 0 ? hi - lo : 0), ay1, cz + 0.05 + w * 0.55], colour);
                if (b != null && lamp > 0)
                    b["way"] = lamp;
                p.Add(b);
            }
            double tail = reach * (way > 0 ? 0.42 : 0.86), tip = reach * (way > 0 ? 0.86 : 0.42), neck = tail + (tip - tail) * 0.6;
            Mark(tail, neck, 0.045);
            Mark(neck, (neck + tip) / 2, 0.1);
            Mark((neck + tip) / 2, tip, 0.05);
            var portName = isIn ? (++ins == 1 ? "Piece in" : $"Piece in {ins}") : (++outs == 1 ? "Piece out" : $"Piece out {outs}");
            ports.Add(Kw(("name", portName), ("type", "conveyor"), ("dir", isIn ? "in" : "out"), ("at", Py.L(Py.Round(ax, 3), Py.Round(top + 0.005, 3), Py.Round(az, 3))), ("face", face),
                ("opening", Py.L(section, section))));
        }
        if (router)
        {
            // The diverter: a low steel fence along the deck's middle, just over the rollers (a riding piece hides it as it crosses), its
            // ends in hazard paint; drawn turned toward the way the last piece took (ConstructRender: "fence").
            double fl = (x1 - x0) * 0.3, fy0 = top + 0.064, fy1 = top + 0.078;
            var fence = MachineLook.D([cx - 0.02, fy0, cz - fl], [cx + 0.02, fy1, cz + fl], MachineLook.Steel, "steel");
            if (fence != null)
                fence["fence"] = true;
            p.Add(fence);
            foreach (var e in new[] { -1.0, 1.0 })
            {
                double za = cz + e * (fl - 0.06), zb = cz + e * (fl + 0.003); // (each end's last 6 cm, 3 to 4 mm proud of the fence all round)
                var cap = MachineLook.D([cx - 0.024, fy0 - 0.003, Math.Min(za, zb)], [cx + 0.024, fy1 + 0.003, Math.Max(za, zb)], MachineLook.Accent, "hazard paint");
                if (cap != null)
                    cap["fence"] = true;
                p.Add(cap);
            }
        }
        var panels = new List<object?>();
        if (router)
        {
            // Its screen (smart logistics phase 4): no post (the user's Q6), so a low dark plate on each side face's frame, under the side
            // ports' conveyor beds (their under-plates come down to 9 cm under the line), 12 mm proud of the frame; panel source "router",
            // with the Spill and Hold buttons along its bottom (RouterScreen.cs). Two, so a conveyor on one side leaves the other to read.
            double sw = Math.Round((x1 - x0) * 0.72, 3), sh = 0.24, sy0 = y0 + 0.02, sy1 = sy0 + sh + 0.02;
            foreach (var (face, sign) in new[] { ("-x", -1.0), ("+x", 1.0) })
            {
                var edge = sign < 0 ? x0 : x1;
                double px0 = sign < 0 ? edge - 0.012 : edge - 0.02, px1 = sign < 0 ? edge + 0.02 : edge + 0.012;
                p.Add(MachineLook.D([px0, sy0, cz - sw / 2 - 0.02], [px1, sy1, cz + sw / 2 + 0.02], "#14181c"));
                panels.Add(Kw(("source", "router"), ("at", Py.L(Py.Round(edge + sign * 0.016, 4), Py.Round((sy0 + sy1) / 2, 4), Py.Round(cz, 4))),
                    ("size", Py.L(sw, sh)), ("face", face), ("up", "+y"), ("rows", 5), ("range", 16),
                    ("touch", Py.L(Kw(("action", "router.spill"), ("at", Py.L(0.02, 0.03, 0.49, 0.27)), ("label", "Spill")),
                        Kw(("action", "router.hold"), ("at", Py.L(0.51, 0.03, 0.98, 0.27)), ("label", "Hold"))))));
            }
        }
        BlockSockets(p, n, 2, n, faces: "y", skip: ["+y"], limit: new() { ["-y"] = (3, 6) });
        var wide = n * c.Cell;
        p.Stats(wide * wide * 40, 300 + 40 * wide * wide, 4);
        p.Set(("ports", ports));
        if (router)
            p.Set(("router", true), ("power", n > 6 ? 40 : 20), ("panels", panels));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out,
    [
        ("", Junction("Conveyor Splitter", 6, 1.0, split: true, "Takes pieces off one conveyor and sends them on to up to three, in turn, past any that is cut, too small or full. Pieces up to 1 x 1 m.")),
        ("", Junction("Conveyor Join", 6, 1.0, split: false, "Takes pieces off up to three conveyors, each in its turn, and sends them on along one. Pieces up to 1 x 1 m.")),
        ("", Junction("Large Conveyor Splitter", 10, 2.0, split: true, "Takes pieces off one conveyor and sends them on to up to three, in turn, past any that is cut, too small or full. Pieces up to 2 x 2 m.")),
        ("", Junction("Large Conveyor Join", 10, 2.0, split: false, "Takes pieces off up to three conveyors, each in its turn, and sends them on along one. Pieces up to 2 x 2 m.")),
        ("", Junction("Conveyor Router", 6, 1.0, split: true, "Sends each piece the way to where it is wanted (an order's tray), else to a Piece Rack on its network with room; holds it, red, when there is none. Unpowered, it deals in turn. Pieces up to 1 x 1 m.", router: true)),
        ("", Junction("Large Conveyor Router", 10, 2.0, split: true, "Sends each piece the way to where it is wanted (an order's tray), else to a Piece Rack on its network with room; holds it, red, when there is none. Unpowered, it deals in turn. Pieces up to 2 x 2 m.", router: true)),
    ]);
}
