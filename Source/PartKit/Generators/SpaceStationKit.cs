using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.Generators.StationLook;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The space station kit's modules (docs/sandbox/constructs/station.md): each call draws one module of a station as welded pieces,
/// in the station's frame, from a few numbers. A station is a list of these calls (<see cref="SpaceStationParts"/>'s plans), so a new
/// station is a new list, not new geometry. Every module keeps the station rules: rooms inside a separate hull with a service gap,
/// square rooms, decks 4 m apart on a lift core, solid boxes that meet face to face so the pieces weld.
/// </summary>
public sealed partial class SpaceStationParts
{
    /// <summary>A room on one deck, in plan metres (x east, n north), with the side its door is on (n, s, e, w).</summary>
    public readonly record struct Room(double X0, double N0, double X1, double N1, string Name, char Door);

    /// <summary>A part hung on a piece once the structure is placed: a docking clamp, a docking hatch.</summary>
    private readonly record struct HangSpec(string Piece, string Sock, string Part, string By, PyDict? Config, string? Paint, string? Finish);

    private sealed class Kit(string prefix, string section)
    {
        public readonly string Prefix = prefix, Section = section;
        public readonly List<Part> Pieces = [];
        public readonly List<HangSpec> Hangs = [];

        public Part Piece(string name, string desc, string color = Plate)
        {
            var full = Prefix + " " + name;
            if (full.Length > ConstructParts.MaxName)
                throw new InvalidOperationException($"space_station: {full} is longer than {ConstructParts.MaxName} characters");
            var p = new Part(full, Small, Section, desc, role: "frame", tool: "welder", material: "steel", color: color, group: Group);
            p.Set(("collider", "boxes"));
            Pieces.Add(p);
            return p;
        }
    }

    // ---- shared numbers

    private const double Slab = 0.6, DeckSlab = 0.3, Pitch = 4.0, Clear = 3.7, HullT = 0.5, Gap = 2.5, CoreH = 5.0;

    /// <summary>The floor tops of a block's levels: its floor, then a deck every 4 m while a room's height fits under the roof.</summary>
    private static List<double> Levels(double y0, double y1)
    {
        var l = new List<double>();
        for (var f = y0 + Slab; f + Clear <= y1 - Slab + 1e-6; f += Pitch)
            l.Add(f);
        return l;
    }

    /// <summary>Mass and health from the solid boxes (150 kg a cubic metre of solid: a slab is plate on girders, not a solid block).</summary>
    private static Part Finish(Part p, double density = 150)
    {
        double vol = 0;
        foreach (var b in p.Boxes)
        {
            if (b.Get("detail") is true)
                continue;
            var mn = (List<object?>)b["min"]!;
            var mx = (List<object?>)b["max"]!;
            vol += Enumerable.Range(0, 3).Aggregate(1.0, (acc, k) => acc * (Convert.ToDouble(mx[k]) - Convert.ToDouble(mn[k])));
        }
        var mass = Math.Max(500, vol * density);
        return p.Stats(mass, Math.Min(2_000_000, 4000 + mass * 1.2), 40, 20.0);
    }

    // ---- a block: a square hull of decks on a lift core

    private sealed record BlockOpts(bool Windows = true, bool Decks = true, Dictionary<int, Room[]>? Rooms = null, Action<Part, double>? Roof = null,
        double Keep = 6, Action<Part, double>? FloorExtra = null, int Seed = 1, string Job = "a block");

    /// <summary>
    /// A square block, half-size h, from y0 to y1: a floor slab, a roof slab, four hull walls (the north and south ones run the whole
    /// width, east and west between them), a deck every 4 m on the lift core, and on each level a pressure wall 2.5 m inside the hull
    /// (the service gap between them carries conduit and pipes) with square rooms inside it. Rooms by level index (0: the floor).
    /// </summary>
    private static void Block(Kit k, string mod, double h, double y0, double y1, BlockOpts o)
    {
        var levels = o.Decks ? Levels(y0, y1) : [y0 + Slab];
        var hi = h - HullT - Gap; // the pressure wall's inner face
        double Top(int i) => i + 1 < levels.Count ? levels[i + 1] - DeckSlab : y1 - Slab;
        Room[] RoomsAt(int i) => o.Rooms != null && o.Rooms.TryGetValue(i, out var r) ? r : [];

        // the floor
        var floor = k.Piece($"{mod} Floor", $"{Cap(o.Job)}'s floor slab: girders under it, the first level's rooms on it.");
        Solid(floor, -h, y0, -h, h, y0 + Slab, h, Plate2, Plated);
        for (var x = -h + 3; x < h - 1; x += 6)
            Detail(floor, x - 0.3, y0 - 0.45, -h + 0.6, x + 0.3, y0, h - 0.6, Frame, Dirty);
        foreach (var (sx, sz) in Corners)
            Lamp(floor, sx * (h - 0.6) - 0.2, y0 - 0.12, sz * (h - 0.6) - 0.2, sx * (h - 0.6) + 0.2, y0, sz * (h - 0.6) + 0.2);
        if (o.Decks)
            Interior(floor, levels[0], Top(0), h, hi, RoomsAt(0), o.Seed);
        o.FloorExtra?.Invoke(floor, levels[0]);
        Finish(floor);

        // the roof
        var roof = k.Piece($"{mod} Roof", $"{Cap(o.Job)}'s roof slab, with its plant, vents and lamps.");
        Solid(roof, -h, y1 - Slab, -h, h, y1, h, Plate2, Plated);
        Detail(roof, -h, y1, -h, h, y1 + 0.35, -h + 0.4, Frame, Dirty);
        Detail(roof, -h, y1, h - 0.4, h, y1 + 0.35, h, Frame, Dirty);
        Detail(roof, -h, y1, -h + 0.4, -h + 0.4, y1 + 0.35, h - 0.4, Frame, Dirty);
        Detail(roof, h - 0.4, y1, -h + 0.4, h, y1 + 0.35, h - 0.4, Frame, Dirty);
        Greebles(roof, -h + 0.6, h - 0.6, -h + 0.6, h - 0.6, y1, o.Keep, (int)Math.Clamp(h * 0.7, 6, 22), o.Seed + 7);
        foreach (var (sx, sz) in Corners)
            Lamp(roof, sx * (h - 0.25) - 0.15, y1 + 0.35, sz * (h - 0.25) - 0.15, sx * (h - 0.25) + 0.15, y1 + 0.6, sz * (h - 0.25) + 0.15, sx < 0 ? NavRed : NavGreen);
        o.Roof?.Invoke(roof, y1);
        Finish(roof);

        // the walls
        double wy0 = y0 + Slab, wy1 = y1 - Slab;
        foreach (var s in new[] { -1, 1 })
        {
            var ns = k.Piece($"{mod} Wall {(s < 0 ? "N" : "S")}", $"{Cap(o.Job)}'s {(s < 0 ? "north" : "south")} hull wall: plate on ribs, the livery band, a window strip a deck.");
            Solid(ns, -h, wy0, s < 0 ? -h : h - HullT, h, wy1, s < 0 ? -h + HullT : h, Plate, Painted);
            WallLook(ns, 'z', s, s * h, -h, h, wy0, wy1, levels, o.Windows, o.Seed * 10 + (s + 1));
            foreach (var ex in new[] { -1, 1 })
            {
                Detail(ns, ex < 0 ? -h - 0.35 : h - 1.0, wy0, s < 0 ? -h - 0.35 : h, ex < 0 ? -h + 1.0 : h + 0.35, wy1, s < 0 ? -h : h + 0.35, Frame, Dirty);
                Detail(ns, ex < 0 ? -h - 0.35 : h, wy0, s < 0 ? -h : h - 1.0, ex < 0 ? -h : h + 0.35, wy1, s < 0 ? -h + 1.0 : h, Frame, Dirty);
            }
            Finish(ns);
            var ew = k.Piece($"{mod} Wall {(s < 0 ? "W" : "E")}", $"{Cap(o.Job)}'s {(s < 0 ? "west" : "east")} hull wall: plate on ribs, the livery band, a window strip a deck.");
            Solid(ew, s < 0 ? -h : h - HullT, wy0, -h + HullT, s < 0 ? -h + HullT : h, wy1, h - HullT, Plate, Painted);
            WallLook(ew, 'x', s, s * h, -h + HullT + 1.0, h - HullT - 1.0, wy0, wy1, levels, o.Windows, o.Seed * 10 + (s + 5));
            Finish(ew);
        }

        // the decks
        for (var i = 1; i < levels.Count; i++)
        {
            var f = levels[i];
            var deck = k.Piece($"{mod} Deck {i + 1}", $"{Cap(o.Job)}'s deck {i + 1}: a slab round the lift core, the pressure wall and its rooms.");
            var a = h - HullT;
            Solid(deck, -a, f - DeckSlab, -a, a, f, -CoreH, Plate2, Plated);
            Solid(deck, -a, f - DeckSlab, CoreH, a, f, a, Plate2, Plated);
            Solid(deck, -a, f - DeckSlab, -CoreH, -CoreH, f, CoreH, Plate2, Plated);
            Solid(deck, CoreH, f - DeckSlab, -CoreH, a, f, CoreH, Plate2, Plated);
            Interior(deck, f, Top(i), h, hi, RoomsAt(i), o.Seed + i);
            Finish(deck);
        }

        // the lift core
        var core = k.Piece($"{mod} Core", $"{Cap(o.Job)}'s lift core: two lifts and a freight lift, a door on every level.", Core);
        Solid(core, -CoreH, wy0, -CoreH, CoreH, wy1, CoreH, Core, Painted);
        foreach (var f in levels)
            foreach (var s in new[] { -1, 1 })
            {
                var z = s * CoreH;
                Detail(core, -1.4, f, z, 1.4, f + 2.6, z + s * 0.05, Hazard, HazardFin);
                Detail(core, -1.1, f, z, 1.1, f + 2.3, z + s * 0.08, Plate3, Painted);
                Lamp(core, -0.8, f + 2.75, z, 0.8, f + 2.9, z + s * 0.06);
            }
        Finish(core);
    }

    private static readonly (int, int)[] Corners = [(-1, -1), (1, -1), (-1, 1), (1, 1)];

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>One level's inside: floor finish, the pressure wall with a dado, the rooms' walls and doors, lamps.</summary>
    private static void Interior(Part p, double f, double top, double h, double hi, Room[] rooms, int seed, bool grating = true)
    {
        var a = h - HullT;
        // floor finish: deck plate inside the pressure wall, grating in the service gap
        Detail(p, -hi, f, -hi, hi, f + 0.02, -CoreH, DeckCol, DeckFin);
        Detail(p, -hi, f, CoreH, hi, f + 0.02, hi, DeckCol, DeckFin);
        Detail(p, -hi, f, -CoreH, -CoreH, f + 0.02, CoreH, DeckCol, DeckFin);
        Detail(p, CoreH, f, -CoreH, hi, f + 0.02, CoreH, DeckCol, DeckFin);
        if (grating)
        {
            Detail(p, -a, f, -a, a, f + 0.015, -hi - 0.2, Truss, Grating);
            Detail(p, -a, f, hi + 0.2, a, f + 0.015, a, Truss, Grating);
        }
        // the pressure wall
        var t = 0.2;
        Solid(p, -hi - t, f, -hi - t, hi + t, top, -hi, Wall, Painted);
        Solid(p, -hi - t, f, hi, hi + t, top, hi + t, Wall, Painted);
        Solid(p, -hi - t, f, -hi, -hi, top, hi, Wall, Painted);
        Solid(p, hi, f, -hi, hi + t, top, hi, Wall, Painted);
        Detail(p, -hi, f, -hi, hi, f + 1.1, -hi + 0.025, Dado, Painted);
        Detail(p, -hi, f, hi - 0.025, hi, f + 1.1, hi, Dado, Painted);
        Detail(p, -hi, f, -hi + 0.03, -hi + 0.025, f + 1.1, hi - 0.03, Dado, Painted);
        Detail(p, hi - 0.025, f, -hi + 0.03, hi, f + 1.1, hi - 0.03, Dado, Painted);
        // rooms
        var drawn = new HashSet<string>();
        foreach (var r in rooms)
        {
            double x0 = r.X0, x1 = r.X1, z0 = -r.N1, z1 = -r.N0;
            Side(p, 'z', z0, x0, x1, r.Door == 'n', f, top, hi, drawn);
            Side(p, 'z', z1, x0, x1, r.Door == 's', f, top, hi, drawn);
            Side(p, 'x', x0, z0, z1, r.Door == 'w', f, top, hi, drawn);
            Side(p, 'x', x1, z0, z1, r.Door == 'e', f, top, hi, drawn);
            double cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
            Lamp(p, cx - 0.6, top - 0.06, cz - 0.6, cx + 0.6, top, cz + 0.6);
        }
        // corridor lamps
        foreach (var (sx, sz) in Corners)
            Lamp(p, sx * (CoreH + 1.5) - 0.4, top - 0.06, sz * (CoreH + 1.5) - 0.4, sx * (CoreH + 1.5) + 0.4, top, sz * (CoreH + 1.5) + 0.4);
    }

    /// <summary>One side of a room: a partition 15 cm thick on the line, with a door gap and lintel in its middle (door), unless the line is
    /// the pressure wall or another room drew it.</summary>
    private static void Side(Part p, char axis, double at, double u0, double u1, bool door, double f, double top, double hi, HashSet<string> drawn)
    {
        if (Math.Abs(Math.Abs(at) - hi) < 0.01)
            return;
        var key = $"{axis}{Math.Round(at, 2)}:{Math.Round(u0, 2)}:{Math.Round(u1, 2)}";
        if (!drawn.Add(key))
            return;
        u0 = Math.Max(u0, -hi);
        u1 = Math.Min(u1, hi);
        const double t = 0.075, dw = 0.8, dh = 2.4;
        void W(double a0, double a1, double ya, double yb, string col)
        {
            if (a1 - a0 < 0.05)
                return;
            if (axis == 'z')
                Solid(p, a0, ya, at - t, a1, yb, at + t, col, Painted);
            else
                Solid(p, at - t, ya, a0, at + t, yb, a1, col, Painted);
        }
        if (!door)
        {
            W(u0, u1, f, top, Wall);
            return;
        }
        var m = (u0 + u1) / 2;
        W(u0, m - dw, f, top, Wall);
        W(m + dw, u1, f, top, Wall);
        W(m - dw, m + dw, f + dh, top, Wall);
        if (axis == 'z')
            Detail(p, m - dw - 0.12, f, at - t - 0.03, m + dw + 0.12, f + dh + 0.12, at + t + 0.03, Hazard, HazardFin);
        else
            Detail(p, at - t - 0.03, f, m - dw - 0.12, at + t + 0.03, f + dh + 0.12, m + dw + 0.12, Hazard, HazardFin);
    }

    // ---- the spine: a square truss between blocks

    /// <summary>A length of the spine from ya to yb: four corner columns, the lift tube in the middle, frames and diagonals, pipes.</summary>
    private static void Spine(Kit k, string name, double ya, double yb, int seed, double half = 5)
    {
        var p = k.Piece(name, "A length of the station's spine: a square truss of four columns round the lift tube, with the main pipes and cables.", Truss);
        var c = half - 1;
        foreach (var (sx, sz) in Corners)
            Solid(p, sx < 0 ? -half : c, ya, sz < 0 ? -half : c, sx < 0 ? -c : half, yb, sz < 0 ? -c : half, Truss, Dirty);
        Solid(p, -2.5, ya, -2.5, 2.5, yb, 2.5, Core, Painted);
        var bays = Math.Max(1, (int)Math.Round((yb - ya) / 6.0));
        var bh = (yb - ya) / bays;
        for (var i = 0; i <= bays; i++)
        {
            var y = ya + i * bh;
            double y0 = Math.Max(ya, y - 0.25), y1 = Math.Min(yb, y + 0.25);
            Detail(p, -c, y0, -half + 0.05, c, y1, -half + 0.45, Frame, Dirty);
            Detail(p, -c, y0, half - 0.45, c, y1, half - 0.05, Frame, Dirty);
            Detail(p, -half + 0.05, y0, -c, -half + 0.45, y1, c, Frame, Dirty);
            Detail(p, half - 0.45, y0, -c, half - 0.05, y1, c, Frame, Dirty);
        }
        for (var i = 0; i < bays; i++)
        {
            double y0 = ya + i * bh + 0.3, y1 = ya + (i + 1) * bh - 0.3;
            var flip = i % 2 == 0 ? 1 : -1;
            Bar(p, [-c * flip, y0, -half + 0.25], [c * flip, y1, -half + 0.25], 0.3, Truss, Dirty);
            Bar(p, [c * flip, y0, half - 0.25], [-c * flip, y1, half - 0.25], 0.3, Truss, Dirty);
            Bar(p, [-half + 0.25, y0, c * flip], [-half + 0.25, y1, -c * flip], 0.3, Truss, Dirty);
            Bar(p, [half - 0.25, y0, -c * flip], [half - 0.25, y1, c * flip], 0.3, Truss, Dirty);
        }
        Cyl(p, 'y', 3.1, 0, -1.2, 0.45, ya + 0.05, yb - 0.05, Tank, Rusty);
        Cyl(p, 'y', 3.1, 0, 1.0, 0.35, ya + 0.05, yb - 0.05, Water, Painted);
        Cyl(p, 'y', -3.1, 0, 0, 0.4, ya + 0.05, yb - 0.05, Oxygen, Painted);
        Detail(p, -1.0, ya + 0.05, -3.4, 1.0, yb - 0.05, -2.9, Power, Painted);
        for (var y = ya + 3; y < yb - 1; y += 9)
            foreach (var (sx, sz) in Corners)
                Lamp(p, sx * (half + 0.01) - (sx < 0 ? 0 : 0.12), y, sz * c - 0.12, sx * (half + 0.01) + (sx < 0 ? 0.12 : 0), y + 0.35, sz * c + 0.12);
        Finish(p);
    }

    // ---- arms: docking arms that reach out from a collar's wall

    /// <summary>An arm's frame: along a (from the station's axis), across c, both on the ground plane; d the way out.</summary>
    private readonly record struct ArmFrame(int Dx, int Dz)
    {
        public int Ex => -Dz;
        public int Ez => Dx;
        public (double X, double Z) At(double a, double c) => (a * Dx + c * Ex, a * Dz + c * Ez);

        public (double X0, double Z0, double X1, double Z1) Rect(double a0, double a1, double c0, double c1)
        {
            var (xa, za) = At(a0, c0);
            var (xb, zb) = At(a1, c1);
            return (Math.Min(xa, xb), Math.Min(za, zb), Math.Max(xa, xb), Math.Max(za, zb));
        }

        public void Solid(Part p, double a0, double a1, double c0, double c1, double y0, double y1, string col, string fin)
        {
            var r = Rect(a0, a1, c0, c1);
            StationLook.Solid(p, r.X0, y0, r.Z0, r.X1, y1, r.Z1, col, fin);
        }

        public void Detail(Part p, double a0, double a1, double c0, double c1, double y0, double y1, string col, string? fin = null, bool glow = false)
        {
            var r = Rect(a0, a1, c0, c1);
            StationLook.Detail(p, r.X0, y0, r.Z0, r.X1, y1, r.Z1, col, fin, glow);
        }

        public void Hidden(Part p, double a0, double a1, double c0, double c1, double y0, double y1)
        {
            var r = Rect(a0, a1, c0, c1);
            StationLook.Hidden(p, r.X0, y0, r.Z0, r.X1, y1, r.Z1);
        }

        public double[] Pt(double a, double c, double y)
        {
            var (x, z) = At(a, c);
            return [x, y, z];
        }

        /// <summary>A pipe along the arm at across c and height y.</summary>
        public void Pipe(Part p, double a0, double a1, double c, double y, double r, string col, string fin)
        {
            var (x, z) = At(0, c);
            if (Dx != 0)
                Cyl(p, 'x', 0, y, z, r, Math.Min(a0 * Dx, a1 * Dx), Math.Max(a0 * Dx, a1 * Dx), col, fin);
            else
                Cyl(p, 'z', x, y, 0, r, Math.Min(a0 * Dz, a1 * Dz), Math.Max(a0 * Dz, a1 * Dz), col, fin);
        }

    }

    private const double ArmHalf = 4, ArmH = 7;

    /// <summary>
    /// A docking arm from r0 (a collar's wall) to r1, its underside at yb, in pieces of about 20 m: chords top and bottom, a walkway
    /// tube down the middle with a freight duct and a feed duct beside it, the station's feeds (fuel, water, air, power) along it, gantry
    /// rails on top, side trusses, hazard paint along its berths, lamps; at its tip nav lamps and a turret. ore: a conveyor on top.
    /// </summary>
    private static void Arm(Kit k, string mod, ArmFrame fr, double r0, double r1, double yb, (double A0, double A1)[] berths, bool ore, int seed)
    {
        var n = Math.Max(1, (int)Math.Round((r1 - r0) / 20.0));
        var len = (r1 - r0) / n;
        for (var i = 0; i < n; i++)
        {
            double s0 = r0 + i * len, s1 = s0 + len;
            var p = k.Piece($"{mod} {i + 1}", $"A length of a docking arm: chords, side trusses, a walkway tube between a freight duct and a feed duct, the feeds along it and gantry rails on top{(ore ? ", an ore conveyor between them" : "")}.", Truss);
            fr.Solid(p, s0, s1, -ArmHalf, ArmHalf, yb, yb + 1, Plate2, Plated);
            fr.Solid(p, s0, s1, -ArmHalf, ArmHalf, yb + ArmH - 1, yb + ArmH, Plate2, Plated);
            fr.Solid(p, s0, s1, -1.6, 1.6, yb + 1, yb + 4.4, Plate, Painted); // the walkway
            fr.Solid(p, s0, s1, 2.2, 3.4, yb + 1, yb + 4.0, Truss, Dirty); // the freight duct
            fr.Solid(p, s0, s1, -3.4, -2.2, yb + 1, yb + 4.0, Truss, Dirty); // the feed duct
            fr.Solid(p, s0, s1, -3.6, -2.8, yb + ArmH, yb + ArmH + 0.4, Frame, Steel); // gantry rails
            fr.Solid(p, s0, s1, 2.8, 3.6, yb + ArmH, yb + ArmH + 0.4, Frame, Steel);
            fr.Detail(p, s0 + 0.05, s1 - 0.05, -1.65, 1.65, yb + 2.2, yb + 2.7, Window, glow: true);
            fr.Pipe(p, s0 + 0.05, s1 - 0.05, -2.8, yb + 4.7, 0.3, Tank, Rusty);
            fr.Pipe(p, s0 + 0.05, s1 - 0.05, -2.0, yb + 4.7, 0.25, Water, Painted);
            fr.Pipe(p, s0 + 0.05, s1 - 0.05, 2.0, yb + 4.7, 0.25, Oxygen, Painted);
            fr.Detail(p, s0 + 0.05, s1 - 0.05, 2.6, 3.4, yb + 4.5, yb + 4.9, Power, Painted);
            if (ore)
            {
                fr.Detail(p, s0, s1, -1.3, 1.3, yb + ArmH, yb + ArmH + 0.55, Frame, Soot);
                fr.Detail(p, s0, s1, -1.1, 1.1, yb + ArmH + 0.55, yb + ArmH + 0.75, Tank, Rusty);
            }
            // side trusses: verticals and diagonals, both sides
            var bays = Math.Max(2, (int)Math.Round(len / 5.0));
            var bw = len / bays;
            foreach (var c in new[] { -1, 1 })
            {
                double ci = c * (ArmHalf - 0.4), co = c * ArmHalf;
                for (var b = 0; b < bays; b++)
                {
                    var a = s0 + (b + 0.5) * bw;
                    fr.Solid(p, a - 0.2, a + 0.2, Math.Min(ci, co), Math.Max(ci, co), yb + 1, yb + ArmH - 1, Truss, Dirty);
                    var flip = b % 2 == 0;
                    Bar(p, fr.Pt(a - bw / 2 + 0.25, c * (ArmHalf - 0.2), flip ? yb + 1.1 : yb + ArmH - 1.1), fr.Pt(a - 0.25, c * (ArmHalf - 0.2), flip ? yb + ArmH - 1.1 : yb + 1.1), 0.25, Truss, Dirty);
                }
                // lamps
                for (var a = s0 + 4; a < s1 - 1; a += 10)
                    fr.Detail(p, a - 0.25, a + 0.25, c * ArmHalf, c * (ArmHalf + 0.06), yb + 0.3, yb + 0.7, LampCol, glow: true);
                // hazard paint along the berths, on the bottom chord's side
                foreach (var (b0, b1) in berths)
                    for (var a = Math.Max(s0, b0); a + 1.6 <= Math.Min(s1, b1); a += 3.2)
                        fr.Detail(p, a, a + 1.6, c * ArmHalf, c * (ArmHalf + 0.03), yb + 0.1, yb + 0.9, Hazard, HazardFin);
            }
            if (i == n - 1)
            {
                // the tip: an end plate, nav lamps, a turret
                fr.Detail(p, s1, s1 + 0.25, -ArmHalf, ArmHalf, yb, yb + ArmH, Frame, Dirty);
                fr.Detail(p, s1 + 0.25, s1 + 0.4, -ArmHalf, -ArmHalf + 0.6, yb + ArmH - 1, yb + ArmH - 0.4, NavRed, glow: true);
                fr.Detail(p, s1 + 0.25, s1 + 0.4, ArmHalf - 0.6, ArmHalf, yb + ArmH - 1, yb + ArmH - 0.4, NavGreen, glow: true);
                var t = fr.Pt(s1 - 2.0, 0, yb + ArmH);
                Pdc(p, t[0], t[1], t[2]);
            }
            Finish(p);
        }
    }

    /// <summary>
    /// A berth's clamp bracket hung off an arm's side (side +1 or -1) at along a: a post down the arm's side and a short bracket under
    /// its edge carrying a Docking Clamp Large, its box set out beside the arm where the ship lies (Karl, 2026-11-18: nothing under the
    /// ship, only the docking crane holds it; the cradle beams it rested on went through its hull), and the boarding tube to the
    /// ship's side. Only the berth's clamp has one (<paramref name="clamp"/> false makes nothing).
    /// </summary>
    private static void Cradle(Kit k, string name, ArmFrame fr, int side, double a, double yb, double top, double shipW, double shipL, double shipH, bool clamp)
    {
        if (!clamp)
            return;
        var p = k.Piece(name, "A berth's clamp bracket: a post down the arm's side, a short bracket under its edge with the berth's docking clamp, and the boarding tube to the ship's side.", Truss);
        // (the bracket hangs 2.4 m lower than the old cradle beam, so the clamp's 2.4 m foot on it stays under the ship's keel, and the
        // clamp stands just past the post)
        const double drop = 2.4;
        double c0 = side * ArmHalf, low = top - drop, outer = ArmHalf + 5.2;
        double Cs(double v) => side * v;
        fr.Solid(p, a - 0.8, a + 0.8, Math.Min(c0, Cs(ArmHalf + 1)), Math.Max(c0, Cs(ArmHalf + 1)), low - 2.2, yb + ArmH, Truss, Dirty);
        fr.Solid(p, a - 2.2, a + 2.2, Math.Min(Cs(ArmHalf + 1), Cs(outer)), Math.Max(Cs(ArmHalf + 1), Cs(outer)), low - 2.2, low, Truss, Dirty);
        for (var v = ArmHalf + 1.2; v + 0.6 < outer; v += 1.2)
            fr.Detail(p, a - 2.22, a + 2.22, Math.Min(Cs(v), Cs(v + 0.6)), Math.Max(Cs(v), Cs(v + 0.6)), low - 0.02, low + 0.012, Hazard, HazardFin);
        var clampV = ArmHalf + 3.05; // (the clamp's 4 m foot just past the post)
        var mid = ArmHalf + 3.5 + shipW / 2;
        var (x, z) = fr.At(a, Cs(clampV));
        var (bx, bz) = fr.At(a, Cs(mid));
        Sock(p, "clamp", x, low, z, "+y", Large);
        // the boarding tube from the arm's walkway to the ship's side, at walkway height
        fr.Solid(p, a - 1.4, a + 1.4, Math.Min(Cs(ArmHalf + 1), Cs(ArmHalf + 3.4)), Math.Max(Cs(ArmHalf + 1), Cs(ArmHalf + 3.4)), yb + 1, yb + 4.0, Plate, Painted);
        fr.Solid(p, a - 0.5, a + 0.5, Math.Min(Cs(ArmHalf + 1), Cs(ArmHalf + 3.4)), Math.Max(Cs(ArmHalf + 1), Cs(ArmHalf + 3.4)), top + 0.05, yb + 1, Truss, Dirty);
        fr.Detail(p, a - 1.5, a + 1.5, Math.Min(Cs(ArmHalf + 3.4), Cs(ArmHalf + 3.45)), Math.Max(Cs(ArmHalf + 3.4), Cs(ArmHalf + 3.45)), yb + 0.9, yb + 4.1, Hazard, HazardFin);
        fr.Detail(p, a - 1.1, a + 1.1, Math.Min(Cs(ArmHalf + 3.45), Cs(ArmHalf + 3.49)), Math.Max(Cs(ArmHalf + 3.45), Cs(ArmHalf + 3.49)), yb + 1.2, yb + 3.8, LampCol, glow: true);
        var cfg = new PyDict
        {
            ["sizeX"] = Math.Min(96, fr.Dx != 0 ? shipL + 6 : shipW + 4),
            ["sizeY"] = Math.Min(96, shipH + 4),
            ["sizeZ"] = Math.Min(96, fr.Dx != 0 ? shipW + 4 : shipL + 6),
            ["atX"] = R(bx - x), // (the box out beside the arm, where the ship lies, not over the clamp)
            ["atY"] = R(drop + 2 + shipH / 2), // (the box where it was over the old cradle beam)
            ["atZ"] = R(bz - z),
            ["startOn"] = true, // (armed from the start: a station's berths hold what docks: the command view's Dock order and by hand)
        };
        k.Hangs.Add(new HangSpec((string)p.D["name"]!, "clamp", "Docking Clamp Large", "-y", cfg, Hazard, HazardFin));
        Finish(p);
    }

    /// <summary>A repair gantry riding an arm's rails at along a: two legs and a yellow beam over both berths, a trolley and a jointed
    /// manipulator over each.</summary>
    private static void Gantry(Kit k, string name, ArmFrame fr, double a, double yb, double span, double beamY)
    {
        var p = k.Piece(name, "A repair gantry on an arm's rails: a beam over both berths, a trolley and a jointed manipulator over each ship (still: the arms don't move yet).", Hazard);
        var rail = yb + ArmH + 0.4;
        fr.Solid(p, a, a + 2, -3.6, -2.8, rail, beamY, Hazard, HazardFin);
        fr.Solid(p, a, a + 2, 2.8, 3.6, rail, beamY, Hazard, HazardFin);
        fr.Solid(p, a - 0.3, a + 2.3, -span, span, beamY, beamY + 2.4, Hazard, HazardFin);
        fr.Detail(p, a - 0.32, a + 2.32, -span, span, beamY + 1.0, beamY + 1.4, HazardDark, HazardFin);
        foreach (var c in new[] { -1, 1 })
        {
            var tc = c * (span - 7);
            fr.Detail(p, a - 0.6, a + 2.6, tc - 1.4, tc + 1.4, beamY - 1.2, beamY, Frame, Dirty);
            var j0 = fr.Pt(a + 1, tc, beamY - 1.2);
            var j1 = fr.Pt(a + 1, tc - c * 2.5, beamY - 4.2);
            var j2 = fr.Pt(a + 1, tc - c * 1.0, beamY - 6.4);
            Bar(p, j0, j1, 0.5, Plate3, Painted);
            Bar(p, j1, j2, 0.4, Plate3, Painted);
            Sphere(p, j1[0], j1[1], j1[2], 0.45, Frame, Dirty);
            Sphere(p, j2[0], j2[1], j2[2], 0.35, Hazard, HazardFin);
            StationLook.Lamp(p, j2[0] - 0.12, j2[1] - 0.5, j2[2] - 0.12, j2[0] + 0.12, j2[1] - 0.35, j2[2] + 0.12, Glow);
            for (var v = 3.0; v < span - 1; v += 6)
                fr.Detail(p, a + 0.4, a + 1.6, c * v - 0.25, c * v + 0.25, beamY - 0.06, beamY, LampCol, glow: true);
        }
        Finish(p);
    }

    // ---- freight aprons

    /// <summary>
    /// A freight apron off a block's wall at r0 to r1, its top at yTop, 24 m wide: a deck in pieces of about 23 m with side rails, a lift
    /// table under where the ship's belly well sits (aLift), a Docking Clamp Large beside it, and a container tunnel under it back into
    /// the block.
    /// </summary>
    private static void Apron(Kit k, string mod, ArmFrame fr, double r0, double r1, double yTop, (double A0, double A1) lift, double clampA, double centreA, double shipW, double shipL, double shipH, int seed)
    {
        const double half = 12;
        var n = Math.Max(1, (int)Math.Round((r1 - r0) / 23.0));
        var len = (r1 - r0) / n;
        for (var i = 0; i < n; i++)
        {
            double s0 = r0 + i * len, s1 = s0 + len;
            var p = k.Piece($"{mod} {i + 1}", "A length of a freight apron: a plated deck on girders, side rails a hauler's clamps run on, lamps along its edges.", Plate2);
            fr.Solid(p, s0, s1, -half, half, yTop - 4, yTop, Plate2, Plated);
            fr.Solid(p, s0, s1, -11.8, -10.6, yTop, yTop + 3, Hazard, HazardFin);
            fr.Solid(p, s0, s1, 10.6, 11.8, yTop, yTop + 3, Hazard, HazardFin);
            fr.Detail(p, s0 + 0.05, s1 - 0.05, -10.5, 10.5, yTop, yTop + 0.02, Truss, Tread);
            for (var a = s0 + 2; a < s1 - 1; a += 5)
            {
                fr.Detail(p, a - 0.3, a + 0.3, -half + 0.5, half - 0.5, yTop - 4.6, yTop - 4, Frame, Dirty);
                fr.Detail(p, a - 0.2, a + 0.2, -half, -half - 0.06, yTop - 1.2, yTop - 0.8, LampCol, glow: true);
                fr.Detail(p, a - 0.2, a + 0.2, half, half + 0.06, yTop - 1.2, yTop - 0.8, LampCol, glow: true);
            }
            // the lift table and its hazard frame where it falls in this piece
            double l0 = Math.Max(s0, lift.A0), l1 = Math.Min(s1, lift.A1);
            if (l1 > l0 + 0.5)
            {
                fr.Detail(p, l0, l1, -5.6, 5.6, yTop + 0.02, yTop + 0.05, Hazard, HazardFin);
                fr.Detail(p, Math.Max(l0, lift.A0 + 0.6), Math.Min(l1, lift.A1 - 0.6), -5.0, 5.0, yTop + 0.05, yTop + 0.08, HazardDark, Tread);
            }
            if (clampA >= s0 && clampA < s1)
            {
                var (x, z) = fr.At(clampA, 0);
                Sock(p, "clamp", x, yTop, z, "+y", Large);
                var cfg = new PyDict
                {
                    ["sizeX"] = Math.Min(96, fr.Dx != 0 ? shipL + 6 : shipW + 4),
                    ["sizeY"] = Math.Min(96, shipH + 4),
                    ["sizeZ"] = Math.Min(96, fr.Dx != 0 ? shipW + 4 : shipL + 6),
                    ["atY"] = R(2 + shipH / 2),
                    ["startOn"] = true, // (armed from the start: a station's berths hold what docks: the command view's Dock order and by hand)
                };
                var (cx2, cz2) = fr.At(centreA, 0);
                cfg["atX"] = R(cx2 - x);
                cfg["atZ"] = R(cz2 - z);
                k.Hangs.Add(new HangSpec((string)p.D["name"]!, "clamp", "Docking Clamp Large", "-y", cfg, Hazard, HazardFin));
            }
            if (i == n - 1)
            {
                fr.Detail(p, s1, s1 + 0.2, -half, -half + 0.8, yTop - 1, yTop, NavRed, glow: true);
                fr.Detail(p, s1, s1 + 0.2, half - 0.8, half, yTop - 1, yTop, NavGreen, glow: true);
            }
            Finish(p);
        }
        // the container tunnel under the apron, from the block's wall to the far end of the lift
        var t = k.Piece($"{mod} Tunnel", "The container tunnel under a freight apron: containers ride down through the lift table and along it into the warehouse.", Plate);
        fr.Solid(t, r0, lift.A1, -4, 4, yTop - 12, yTop - 4, Plate, Painted);
        foreach (var c in new[] { -1, 1 })
        {
            fr.Detail(t, r0 + 1, lift.A1 - 1, c * 4, c * 4.03, yTop - 9.2, yTop - 8.4, Window, glow: true);
            for (var a = r0 + 4; a < lift.A1 - 1; a += 6)
                fr.Detail(t, a - 0.26, a + 0.26, c * 4, c * 4.4, yTop - 12, yTop - 4, Plate2, Dirty);
        }
        fr.Detail(t, lift.A1, lift.A1 + 0.1, -4, 4, yTop - 12, yTop - 4, Frame, Dirty);
        Finish(t);
    }

    // ---- the crown's bridge band and mast

    private static void Band(Kit k, string mod, double h, double out_, double y0, double y1)
    {
        var p = k.Piece(mod, "The bridge band round the ops crown: armoured window slits all round, lamps under its lip.", Plate);
        double o = h + out_;
        Solid(p, -o, y0, -o, o, y1, -h, Plate, Painted);
        Solid(p, -o, y0, h, o, y1, o, Plate, Painted);
        Solid(p, -o, y0, -h, -h, y1, h, Plate, Painted);
        Solid(p, h, y0, -h, o, y1, h, Plate, Painted);
        Detail(p, -o + 0.4, y0 + 1.4, -o - 0.03, o - 0.4, y0 + 2.4, -o, Window, glow: true);
        Detail(p, -o + 0.4, y0 + 1.4, o, o - 0.4, y0 + 2.4, o + 0.03, Window, glow: true);
        Detail(p, -o - 0.03, y0 + 1.4, -o + 0.4, -o, y0 + 2.4, o - 0.4, Window, glow: true);
        Detail(p, o, y0 + 1.4, -o + 0.4, o + 0.03, y0 + 2.4, o - 0.4, Window, glow: true);
        for (var u = -o + 1; u < o - 0.5; u += 2.0)
        {
            Detail(p, u, y0 + 1.3, -o - 0.06, u + 0.2, y0 + 2.5, -o, Frame, Dirty);
            Detail(p, u, y0 + 1.3, o, u + 0.2, y0 + 2.5, o + 0.06, Frame, Dirty);
        }
        foreach (var (sx, sz) in Corners)
            Lamp(p, sx * (o - 0.5) - 0.2, y0 - 0.1, sz * (o - 0.5) - 0.2, sx * (o - 0.5) + 0.2, y0, sz * (o - 0.5) + 0.2);
        Finish(p);
    }

    private static void Mast(Kit k, string name, double y0, double y1)
    {
        var p = k.Piece(name, "The mast on the ops crown: a comms dish, the radar, aerials and a beacon at the top.", Truss);
        Solid(p, -1.5, y0, -1.5, 1.5, y1, 1.5, Truss, Dirty);
        Solid(p, -6, y0 + 8, -0.6, -1.5, y0 + 9.5, 0.6, Truss, Dirty);
        Shaped(p, -9.5, y0 + 4.5, -4.5, -8.5, y0 + 13.5, 4.5, Plate3, Painted, "cylinder", "+x");
        Shaped(p, -8.5, y0 + 7.5, -1.5, -6, y0 + 10.5, 1.5, Frame, Dirty, "cylinder", "+x");
        Solid(p, 1.5, y1 - 6, -0.4, 4.5, y1 - 5, 0.4, Truss, Dirty);
        Detail(p, 4.5, y1 - 7, -3, 5.2, y1 - 4, 3, Plate3, Painted);
        for (var y = y0 + 3; y < y1 - 2; y += 4)
            Detail(p, -1.7, y, -1.7, 1.7, y + 0.3, 1.7, Frame, Dirty);
        Cyl(p, 'y', 0.9, 0, 0.9, 0.08, y1, y1 + 6, Frame, Steel);
        Cyl(p, 'y', -0.9, 0, -0.9, 0.06, y1, y1 + 4, Frame, Steel);
        Lamp(p, -0.5, y1, -0.5, 0.5, y1 + 0.8, 0.5, NavRed);
        Lamp(p, 0.7, y1 + 6, 0.7, 1.1, y1 + 6.4, 1.1, NavRed);
        Finish(p);
    }

    // ---- the tank farm

    private static void TankFarm(Kit k, string mod, double h, double y0, double y1, double ring, double r, string[] tanks)
    {
        var f = k.Piece($"{mod} Frame", "The tank farm's frame: deck slabs top and bottom on corner columns, the pump core in the middle, the ring main round it.", Truss);
        Solid(f, -h, y0, -h, h, y0 + 1, h, Plate2, Plated);
        Solid(f, -h, y1 - 1, -h, h, y1, h, Plate2, Plated);
        foreach (var (sx, sz) in Corners)
            Solid(f, sx < 0 ? -h : h - 1.2, y0 + 1, sz < 0 ? -h : h - 1.2, sx < 0 ? -h + 1.2 : h, y1 - 1, sz < 0 ? -h + 1.2 : h, Truss, Dirty);
        Solid(f, -CoreH, y0 + 1, -CoreH, CoreH, y1 - 1, CoreH, Core, Painted);
        var ym = (y0 + y1) / 2; // the ring beam the tanks' struts bolt to, round the frame at their height
        Solid(f, -h + 1.2, ym - 1, -h, h - 1.2, ym + 1, -h + 0.8, Truss, Dirty);
        Solid(f, -h + 1.2, ym - 1, h - 0.8, h - 1.2, ym + 1, h, Truss, Dirty);
        Solid(f, -h, ym - 1, -h + 1.2, -h + 0.8, ym + 1, h - 1.2, Truss, Dirty);
        Solid(f, h - 0.8, ym - 1, -h + 1.2, h, ym + 1, h - 1.2, Truss, Dirty);
        foreach (var yy in new[] { y0 + 4, y1 - 4 })
        {
            Detail(f, -h + 1.2, yy, -h + 0.3, h - 1.2, yy + 0.6, -h + 0.9, Tank, Rusty);
            Detail(f, -h + 1.2, yy, h - 0.9, h - 1.2, yy + 0.6, h - 0.3, Tank, Rusty);
            Detail(f, -h + 0.3, yy, -h + 1.2, -h + 0.9, yy + 0.6, h - 1.2, Tank, Rusty);
            Detail(f, h - 0.9, yy, -h + 1.2, h - 0.3, yy + 0.6, h - 1.2, Tank, Rusty);
        }
        for (var i = 0; i < 4; i++)
        {
            var yy = y0 + 1 + (i + 0.5) * (y1 - y0 - 2) / 4;
            Detail(f, -CoreH - 0.4, yy, -CoreH - 0.4, CoreH + 0.4, yy + 0.8, CoreH + 0.4, Frame, Dirty);
        }
        foreach (var (sx, sz) in Corners)
            Lamp(f, sx * (h + 0.01) - (sx < 0 ? 0 : 0.15), y0 + 1.4, sz * (h - 0.6) - 0.15, sx * (h + 0.01) + (sx < 0 ? 0.15 : 0), y0 + 1.8, sz * (h - 0.6) + 0.15);
        Finish(f);

        var cy = (y0 + y1) / 2;
        for (var i = 0; i < tanks.Length; i++)
        {
            var ang = Math.PI / tanks.Length + i * 2 * Math.PI / tanks.Length;
            double cx = Math.Round(ring * Math.Cos(ang), 2), cz = Math.Round(-ring * Math.Sin(ang), 2);
            var kind = tanks[i];
            var col = kind switch { "Water" => Water, "Oxygen" => Plate3, "Nitrogen" => Oxygen, _ => Tank };
            var p = k.Piece($"{mod} {i + 1}", $"A {kind.ToLowerInvariant()} tank: a sphere {2 * r:0} m across on a strut from the frame, banded and stencilled.", col);
            var cube = Math.Round(r * 0.7, 2);
            Hidden(p, cx - cube, cy - cube, cz - cube, cx + cube, cy + cube, cz + cube);
            Sphere(p, cx, cy, cz, r, col, kind == "Oxygen" ? Painted : Rusty);
            Cyl(p, 'y', cx, 0, cz, r + 0.06, cy - 0.3, cy + 0.3, Frame, Dirty);
            Cyl(p, 'y', cx, 0, cz, r * 0.55, cy + r * 0.82, cy + r * 0.82 + 0.6, Frame, Dirty);
            Cyl(p, 'y', cx, 0, cz, r * 0.55, cy - r * 0.82 - 0.6, cy - r * 0.82, Frame, Dirty);
            // the strut from the nearest face of the frame to the hidden core
            if (Math.Abs(cx) >= Math.Abs(cz))
            {
                var s = Math.Sign(cx);
                double a = s * h, b = cx - s * cube;
                Solid(p, Math.Min(a, b), cy - 0.8, cz - 0.8, Math.Max(a, b), cy + 0.8, cz + 0.8, Truss, Dirty);
                Cyl(p, 'x', 0, cy + 1.6, cz, 0.35, Math.Min(a, b), Math.Max(a, b), Pipe, Dirty);
            }
            else
            {
                var s = Math.Sign(cz);
                double a = s * h, b = cz - s * cube;
                Solid(p, cx - 0.8, cy - 0.8, Math.Min(a, b), cx + 0.8, cy + 0.8, Math.Max(a, b), Truss, Dirty);
                Cyl(p, 'z', cx, cy + 1.6, 0, 0.35, Math.Min(a, b), Math.Max(a, b), Pipe, Dirty);
            }
            Finish(p, 120);
        }
    }

    // ---- radiators

    /// <summary>
    /// A radiator boom from a block's wall at r0 out to r1, its axis at ym: a square truss with the coolant loop inside it, and on it
    /// tapered radiator panels above and below in a row, hinged to the boom with gaps between them, tallest at the root (hRoot) and
    /// shortest at the tip (hTip). The heat pipes on the panels glow at the root and cool to copper and steel outwards.
    /// </summary>
    private static void Radiator(Kit k, string mod, ArmFrame fr, double r0, double r1, double ym, double hRoot, double hTip, int segs)
    {
        var len = (r1 - r0) / segs;
        double H(double a) => hRoot + (hTip - hRoot) * (a - r0) / (r1 - r0);
        string[] pipeCol = [Glow, "#b86a3c", "#8d9398", "#8d9398"];
        for (var i = 0; i < segs; i++)
        {
            double s0 = r0 + i * len, s1 = s0 + len;
            var p = k.Piece($"{mod} {i + 1}", "A length of a radiator boom: a square truss carrying the coolant loop, and tapered radiator panels hinged above and below it, edge-on to the sun.", RadiatorCol);
            // the boom: four chords, frames, diagonals, the coolant pipes
            foreach (var (sc, sy) in Corners)
                fr.Solid(p, s0, s1, sc < 0 ? -1.3 : 0.9, sc < 0 ? -0.9 : 1.3, sy < 0 ? ym - 1.3 : ym + 0.9, sy < 0 ? ym - 0.9 : ym + 1.3, Truss, Dirty);
            var bays = Math.Max(2, (int)Math.Round(len / 3.5));
            var bw = len / bays;
            for (var b = 0; b <= bays; b++)
            {
                var a = s0 + b * bw;
                double a0 = Math.Max(s0, a - 0.15), a1 = Math.Min(s1, a + 0.15);
                fr.Detail(p, a0, a1, -1.25, 1.25, ym + 0.95, ym + 1.25, Frame, Dirty);
                fr.Detail(p, a0, a1, -1.25, 1.25, ym - 1.25, ym - 0.95, Frame, Dirty);
                fr.Detail(p, a0, a1, -1.25, -0.95, ym - 0.9, ym + 0.9, Frame, Dirty);
                fr.Detail(p, a0, a1, 0.95, 1.25, ym - 0.9, ym + 0.9, Frame, Dirty);
            }
            for (var b = 0; b < bays; b++)
            {
                double a0 = s0 + b * bw + 0.2, a1 = s0 + (b + 1) * bw - 0.2;
                var fl = b % 2 == 0 ? 1 : -1;
                Bar(p, fr.Pt(a0, -1.1, ym - fl * 0.9), fr.Pt(a1, -1.1, ym + fl * 0.9), 0.18, Truss, Dirty);
                Bar(p, fr.Pt(a0, 1.1, ym + fl * 0.9), fr.Pt(a1, 1.1, ym - fl * 0.9), 0.18, Truss, Dirty);
            }
            fr.Pipe(p, s0 + 0.02, s1 - 0.02, -0.4, ym, 0.3, i == 0 ? Tank : Pipe, Rusty);
            fr.Pipe(p, s0 + 0.02, s1 - 0.02, 0.4, ym, 0.3, Water, Painted);
            if (i == 0)
            {
                fr.Solid(p, s0, s0 + 2.5, -1.9, 1.9, ym - 2.4, ym + 2.4, Plate2, Plated);
                fr.Detail(p, s0, s0 + 2.52, -1.95, 1.95, ym - 0.4, ym + 0.4, Hazard, HazardFin);
                fr.Detail(p, s0 + 0.6, s0 + 1.9, 1.9, 1.96, ym + 1.0, ym + 1.4, LampCol, glow: true);
            }
            // the panels, three a length, above and below
            const int per = 3;
            const double gap = 0.7;
            var pw = (len - gap * per) / per;
            for (var j = 0; j < per; j++)
            {
                var a0 = s0 + gap / 2 + j * (pw + gap);
                if (i == 0 && j == 0)
                    a0 += 2.2; // clear of the root mount
                var a1 = s0 + gap / 2 + j * (pw + gap) + pw;
                foreach (var up in new[] { 1, -1 })
                {
                    double yb = ym + up * 1.6, yt0 = yb + up * H(a0), yt1 = yb + up * H(a1);
                    Hull(p, [fr.Pt(a0, -0.06, yb), fr.Pt(a0, 0.06, yb), fr.Pt(a1, -0.06, yb), fr.Pt(a1, 0.06, yb),
                        fr.Pt(a0, -0.06, yt0), fr.Pt(a0, 0.06, yt0), fr.Pt(a1, -0.06, yt1), fr.Pt(a1, 0.06, yt1)], RadiatorCol, Steel);
                    var hmin = Math.Min(H(a0), H(a1));
                    var hy0 = up > 0 ? yb : yb - hmin;
                    fr.Hidden(p, a0 + 0.02, a1 - 0.02, -0.05, 0.05, hy0, hy0 + hmin);
                    // the heat pipes
                    for (var a = a0 + 0.45; a < a1 - 0.3; a += 0.8)
                    {
                        var ht = H(a) - 0.2;
                        double ry0 = up > 0 ? yb + 0.06 : yb - ht, ry1 = up > 0 ? yb + ht : yb - 0.06;
                        if (i == 0)
                            fr.Detail(p, a - 0.05, a + 0.05, -0.1, 0.1, ry0, ry1, pipeCol[i], glow: true);
                        else
                            fr.Detail(p, a - 0.05, a + 0.05, -0.1, 0.1, ry0, ry1, pipeCol[Math.Min(i, 3)], Steel);
                    }
                    // the frame: the outer edge, the two sides, the hinge
                    Bar(p, fr.Pt(a0, 0, yt0), fr.Pt(a1, 0, yt1), 0.22, Frame, Dirty);
                    double sy0 = up > 0 ? yb + 0.04 : yt0, sy1 = up > 0 ? yt0 : yb - 0.04;
                    fr.Detail(p, a0 - 0.09, a0 + 0.09, -0.12, 0.12, sy0, sy1, Frame, Dirty);
                    sy0 = up > 0 ? yb + 0.04 : yt1;
                    sy1 = up > 0 ? yt1 : yb - 0.04;
                    fr.Detail(p, a1 - 0.09, a1 + 0.09, -0.12, 0.12, sy0, sy1, Frame, Dirty);
                    fr.Pipe(p, a0 + 0.3, a1 - 0.3, 0, ym + up * 1.45, 0.2, Frame, Dirty);
                }
            }
            if (i == segs - 1)
            {
                fr.Detail(p, s1, s1 + 0.3, -1.3, 1.3, ym - 1.3, ym + 1.3, Frame, Dirty);
                fr.Detail(p, s1 + 0.3, s1 + 0.45, -0.25, 0.25, ym - 0.25, ym + 0.25, NavRed, glow: true);
            }
            Finish(p, 200);
        }
    }

    // ---- a space refinery: an open frame of process plant against a block

    /// <summary>
    /// A refinery against a block's wall face (wall: its distance from the axis, out: +1 east, -1 west), from y0 to y1, depth out from
    /// the wall, hz either side: an open truss cage holding the ore hopper (the chute from the miners' arm lands on it), a crusher drum,
    /// a smelter with glowing vent rings and its stack, two fractionating columns, gas spheres, radiator fins on its outer face and the
    /// product duct back into the block.
    /// </summary>
    private static void Refinery(Kit k, string name, string desc, int outX, double wall, double y0, double y1, double depth, double hz)
    {
        var p = k.Piece(name, desc, Truss);
        double X(double u) => outX * (wall + u);
        void S(double u0, double u1, double ya, double yb, double z0, double z1, string col, string fin) => Solid(p, X(u0), ya, z0, X(u1), yb, z1, col, fin);
        void D(double u0, double u1, double ya, double yb, double z0, double z1, string col, string? fin = null, bool glow = false) => Detail(p, X(u0), ya, z0, X(u1), yb, z1, col, fin, glow);
        double[] Pt(double u, double y, double z) => [X(u), y, z];

        // the cage: a deck, corner columns, top beams, a mid ring, braces
        S(0, depth, y0, y0 + 1, -hz, hz, Plate2, Plated);
        D(0.3, depth - 0.3, y0 + 1, y0 + 1.02, -hz + 0.3, hz - 0.3, Truss, Grating);
        foreach (var u in new[] { (0.0, 1.2), (depth - 1.2, depth) })
            foreach (var z in new[] { (-hz, -hz + 1.2), (hz - 1.2, hz) })
                S(u.Item1, u.Item2, y0 + 1, y1 - 1, z.Item1, z.Item2, Truss, Dirty);
        S(0, depth, y1 - 1, y1, -hz, -hz + 1.2, Truss, Dirty);
        S(0, depth, y1 - 1, y1, hz - 1.2, hz, Truss, Dirty);
        S(depth - 1.2, depth, y1 - 1, y1, -hz + 1.2, hz - 1.2, Truss, Dirty);
        S(0, 1.2, y1 - 1, y1, -hz + 1.2, hz - 1.2, Truss, Dirty);
        var ymid = (y0 + y1) / 2;
        D(1.2, depth - 1.2, ymid - 0.35, ymid + 0.35, -hz + 0.1, -hz + 0.9, Truss, Dirty);
        D(1.2, depth - 1.2, ymid - 0.35, ymid + 0.35, hz - 0.9, hz - 0.1, Truss, Dirty);
        D(depth - 0.9, depth - 0.1, ymid - 0.35, ymid + 0.35, -hz + 1.2, hz - 1.2, Truss, Dirty);
        foreach (var z in new[] { -hz + 0.5, hz - 0.5 })
            foreach (var (ya, yb) in new[] { (y0 + 1.2, ymid - 0.5), (ymid + 0.5, y1 - 1.2) })
            {
                Bar(p, Pt(1.4, ya, z), Pt(depth / 2 - 0.2, yb, z), 0.3, Truss, Dirty);
                Bar(p, Pt(depth - 1.4, ya, z), Pt(depth / 2 + 0.2, yb, z), 0.3, Truss, Dirty);
            }
        foreach (var (ya, yb) in new[] { (y0 + 1.2, ymid - 0.5), (ymid + 0.5, y1 - 1.2) })
        {
            Bar(p, Pt(depth - 0.5, ya, -hz + 1.4), Pt(depth - 0.5, yb, -0.2), 0.3, Truss, Dirty);
            Bar(p, Pt(depth - 0.5, ya, hz - 1.4), Pt(depth - 0.5, yb, 0.2), 0.3, Truss, Dirty);
        }
        D(0, depth, y0 + 0.2, y0 + 0.8, -hz - 0.03, -hz, Hazard, HazardFin);
        D(0, depth, y0 + 0.2, y0 + 0.8, hz, hz + 0.03, Hazard, HazardFin);

        // the ore hopper under the chute, and the crusher drum it feeds
        S(1.5, 6.5, y1 - 6, y1, -3, 3, Tank, Rusty);
        D(1.4, 6.6, y1 - 1.2, y1 - 0.6, -3.1, 3.1, Frame, Dirty);
        Shaped(p, Math.Min(X(1.7), X(6.3)), y1 - 8.5, -2.8, Math.Max(X(1.7), X(6.3)), y1 - 6, 2.8, Tank, Rusty, "wedge", "-y", ("taper", new List<object?> { 0.45, 0.45 }));
        var drumY = y1 - 11.5;
        Cyl(p, 'z', X(4), drumY, 0, 3, -6, 6, Plate3, Dirty);
        foreach (var z in new[] { -4.5, 0.0, 4.5 })
            Cyl(p, 'z', X(4), drumY, 0, 3.15, z - 0.25, z + 0.25, Frame, Dirty);
        Cyl(p, 'z', X(4), drumY, 0, 1.1, -6.8, -6, Frame, Steel);
        Cyl(p, 'z', X(4), drumY, 0, 1.1, 6, 6.8, Frame, Steel);
        D(2.5, 5.5, drumY - 4.2, drumY - 3.4, -7.4, -5.6, Hazard, HazardFin); // the drive motor's mount
        D(2.9, 5.1, drumY - 3.4, drumY - 1.2, -7.2, -5.8, Plate2, Painted);
        Hidden(p, X(2), drumY - 2, -5.5, X(6), drumY + 2, 5.5);

        // the conveyor from the crusher to the smelter
        D(5.5, 9.0, y0 + 4.6, y0 + 5.2, -4.4, -2.6, Frame, Soot);
        D(5.5, 9.0, y0 + 5.2, y0 + 5.45, -4.2, -2.8, Tank, Rusty);
        for (var u = 6.0; u < 9; u += 1.4)
            D(u, u + 0.2, y0 + 1.02, y0 + 4.6, -4.3, -4.1, Truss, Dirty);

        // the smelter: a tall drum with glowing vent rings, a hood and a stack
        double su = 11.5, sz = -3.5, sr = 3.2;
        Cyl(p, 'y', X(su), 0, sz, sr, y0 + 1, y1 - 5, Plate3, Soot);
        for (var y = y0 + 4; y < y1 - 7; y += 3)
            GlowCyl(p, X(su), sz, sr + 0.06, y, y + 0.35, Glow);
        Shaped(p, Math.Min(X(su - sr - 0.2), X(su + sr + 0.2)), y1 - 5, sz - sr - 0.2, Math.Max(X(su - sr - 0.2), X(su + sr + 0.2)), y1 - 2.5, sz + sr + 0.2, Plate2, Soot, "wedge", "+y", ("taper", new List<object?> { 0.35, 0.35 }));
        Cyl(p, 'y', X(su), 0, sz, 0.8, y1 - 2.5, y1 + 6, Plate2, Soot);
        Cyl(p, 'y', X(su), 0, sz, 0.95, y1 + 5.4, y1 + 6, Frame, Dirty);
        Lamp(p, X(su) - 0.15, y1 + 6, sz - 0.15, X(su) + 0.15, y1 + 6.3, sz + 0.15, NavRed);
        Hidden(p, X(su - 2.2), y0 + 1, sz - 2.2, X(su + 2.2), y1 - 5, sz + 2.2);
        D(su - 1.2, su + 1.2, y0 + 1.5, y0 + 3.6, sz - sr - 0.1, sz - sr + 0.4, Frame, Dirty); // the tap hatch
        Lamp(p, X(su - 0.9), y0 + 1.7, sz - sr - 0.16, X(su + 0.9), y0 + 2.0, sz - sr - 0.1, Glow);

        // the product duct back into the block, ingots glowing in it
        D(0, su - sr + 0.3, y0 + 1.2, y0 + 2.8, sz - 1.0, sz + 1.0, Plate2, Plated);
        D(0.2, su - sr + 0.1, y0 + 2.8, y0 + 2.95, sz - 0.5, sz + 0.5, Glow, glow: true);

        // two fractionating columns with ring platforms
        foreach (var (cu, cz, cr, ct) in new[] { (15.0, 2.8, 1.3, y1 - 1.4), (15.0, 6.0, 1.0, y1 - 4.0) })
        {
            Cyl(p, 'y', X(cu), 0, cz, cr, y0 + 1, ct, Plate3, Painted);
            for (var y = y0 + 4; y < ct - 1; y += 4)
                Cyl(p, 'y', X(cu), 0, cz, cr + 0.6, y, y + 0.2, Frame, Grating);
            Lamp(p, X(cu) - 0.15, ct, cz - 0.15, X(cu) + 0.15, ct + 0.2, cz + 0.15);
        }

        // gas spheres on saddles
        foreach (var (gu, gz, gr, col) in new[] { (6.5, 5.0, 2.6, Water), (10.8, 5.6, 2.0, Oxygen) })
        {
            Sphere(p, X(gu), y0 + 1.6 + gr, gz, gr, col, Painted);
            Cyl(p, 'y', X(gu), 0, gz, gr * 0.5, y0 + 1, y0 + 1.6 + gr * 0.4, Frame, Dirty);
        }

        // pipes: the feed main along the wall side and up to the columns
        Cyl(p, 'x', 0, y1 - 2.2, 7.2, 0.35, Math.Min(X(1.2), X(15)), Math.Max(X(1.2), X(15)), Pipe, Dirty);
        Cyl(p, 'x', 0, y0 + 7.5, -7.4, 0.3, Math.Min(X(1.2), X(depth - 1.2)), Math.Max(X(1.2), X(depth - 1.2)), Tank, Rusty);
        Cyl(p, 'y', X(15), 0, 7.2, 0.3, y1 - 4.0, y1 - 2.2, Pipe, Dirty);

        // radiator fins on the outer face, the hot end glowing
        foreach (var z in new[] { -6.0, -2.4, 1.2, 4.8 })
        {
            D(depth + 0.02, depth + 4.5, y0 + 3, y1 - 3, z - 0.08, z + 0.08, RadiatorCol, Steel);
            D(depth + 0.02, depth + 0.5, y0 + 3.2, y1 - 3.2, z - 0.12, z + 0.12, Glow, glow: true);
            D(depth + 4.5, depth + 4.7, y0 + 2.9, y1 - 2.9, z - 0.15, z + 0.15, Frame, Dirty);
        }
        foreach (var z in new[] { -hz + 0.6, hz - 0.6 })
            Lamp(p, X(depth) - 0.2, y1 - 0.4, z - 0.2, X(depth) + 0.2, y1 + 0.05, z + 0.2, z < 0 ? NavRed : NavGreen);
        Finish(p);
    }
}
