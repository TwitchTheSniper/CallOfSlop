using System.Numerics;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class MainframeParts
{
    // ---- Planes for the inside: walls along the ship, walls across it, and levels

    /// <summary>A wall along the ship at x: V up, U aft (cm from the keel's height 0 and the bow).</summary>
    private static Plane WallAlongZ(double x, string name) => Flat(new D3(x, 0, 0), NX, PZ, name);

    /// <summary>A wall across the ship at z: V to starboard, U up.</summary>
    private static Plane WallAcross(double z, string name) => Flat(new D3(0, 0, z), NZ, PY, name);

    private static List<(double V, double U)> Rect(double v0, double u0, double v1, double u1) => [(v0, u0), (v1, u0), (v1, u1), (v0, u1)];

    private const string DoorPart = "Bulkhead Door 1.5x2.25 m";
    private static readonly Look WallLook = new(Wall, "painted steel"), DoorLook = new(Armour, "plated steel"), LowLook = new(WallLow, "painted steel");

    /// <summary>A wall along the ship at x from z0 to z1, y0 to y1, with a doorway (cut by the Laser Cutter) and a sliding door at each z given.</summary>
    private static void WallZ(Writer w, double x, double z0, double z1, double y0, double y1, IReadOnlyList<double> doors, Look look, int h = WallH, double wide = DoorW)
    {
        if (z1 - z0 < 0.3 || y1 - y0 < 0.3)
            return;
        var pl = WallAlongZ(x, $"wall at x {x:0.##}, z {z0:0.#}-{z1:0.#}, y {y0:0.#}");
        var holes = doors.Select(z => new Hole(z * 100, (y0 + DoorH / 2) * 100, wide * 100, DoorH * 100)).ToList();
        Plate(w, pl, Rect(y0 * 100, z0 * 100, y1 * 100, z1 * 100), holes, h, 0, look, maxL: doors.Count > 0 ? 2800 : 6400);
        foreach (var z in doors)
            w.Root(DoorPart, B(x, y0 + 1.125, z - 0.72), Turn(PZ, PY), DoorLook); // (1 cm clear of the opening's cut ends)
    }

    /// <summary>A wall across the ship at z from x0 to x1 (x0 below x1), y0 to y1, with a doorway and its door at each x given.</summary>
    private static void WallX(Writer w, double z, double x0, double x1, double y0, double y1, IReadOnlyList<double> doors, Look look, int h = WallH)
    {
        if (x1 - x0 < 0.3 || y1 - y0 < 0.3)
            return;
        var pl = WallAcross(z, $"wall at z {z:0.##}, x {x0:0.#}-{x1:0.#}, y {y0:0.#}");
        var holes = doors.Select(x => new Hole((y0 + DoorH / 2) * 100, x * 100, DoorH * 100, DoorW * 100)).ToList();
        Plate(w, pl, Rect(x0 * 100, y0 * 100, x1 * 100, y1 * 100), holes, h, 0, look, maxL: 6400);
        foreach (var x in doors)
            w.Root(DoorPart, B(x - 0.72, y0 + 1.125, z), 0, DoorLook);
    }

    /// <summary>A level plate at y (its top) over the rectangle x0..x1, z0..z1, with openings.</summary>
    private static void Slab(Writer w, double y, double x0, double x1, double z0, double z1, List<Hole> holes, Look look, int h = DeckH)
    {
        var pl = Flat(new D3(0, y, 0), PY, PZ, $"deck at {y:0.#} m, z {z0:0}-{z1:0}");
        Plate(w, pl, Rect(x0 * 100, z0 * 100, x1 * 100, z1 * 100), holes, h, -Thick(h) / 2 - 0.002, look);
    }

    /// <summary>An opening in a level (V x, U z) over a rectangle.</summary>
    private static Hole LevelHole(double x0, double x1, double z0, double z1, double corner = 0) =>
        new((z0 + z1) * 50, (x0 + x1) * 50, (z1 - z0) * 100, (x1 - x0) * 100, corner);

    // ---- Fittings

    /// <summary>The turn that stands a part upright with its back (the way its wall socket faces) toward <paramref name="back"/>.</summary>
    private static byte Backed(ConstructPartDef d, byte back)
    {
        var own = BackOf(d);
        for (byte r = 0; r < Rot24.Count; r++)
            if (Rot24.ApplyDir(r, PY) == PY && Rot24.ApplyDir(r, own) == back)
                return r;
        return 0;
    }

    /// <summary>A part's back: its wall socket's way, else its back socket's, else -z.</summary>
    private static byte BackOf(ConstructPartDef d)
    {
        foreach (var name in (ReadOnlySpan<string>)["wall", "back"])
            foreach (var s in d.Sockets)
                if (s.Name == name && s.Dir is not (PY or NY))
                    return s.Dir;
        return NZ;
    }

    /// <summary>
    /// A fitting standing on the floor at y, its back to a wall: <paramref name="back"/> the way to the wall, <paramref name="wall"/> the
    /// wall's face (x or z by the way), <paramref name="along"/> where it stands along the wall.
    /// </summary>
    private static int Fit(Writer w, string part, double y, byte back, double wall, double along, Look look = default, PyDict? config = null, IReadOnlyCollection<string>? keep = null)
    {
        var d = w.Def(part);
        var r = Backed(d, back);
        // (how deep it is from its origin to its back, in the way of the wall)
        var lo = d.BoundsMin;
        var hi = d.BoundsMax;
        var depth = 0.0;
        for (var i = 0; i < 8; i++)
        {
            var c = new Vector3((i & 1) == 0 ? lo.X : hi.X, (i & 2) == 0 ? lo.Y : hi.Y, (i & 4) == 0 ? lo.Z : hi.Z);
            var t = Rot24.Apply(r, c);
            var along2 = back switch { PX => t.X, NX => -t.X, PZ => t.Z, _ => -t.Z };
            depth = Math.Max(depth, along2);
        }
        var off = depth + 0.015;
        var sign = back is PX or PZ ? -1 : 1;
        var at = back is PX or NX ? new Vector3((float)(wall + sign * off), (float)(y + 0.003), (float)along) : new Vector3((float)along, (float)(y + 0.003), (float)(wall + sign * off));
        return w.Root(part, B(at.X, at.Y, at.Z), r, look, config, keep: keep);
    }

    /// <summary>A part free on the floor at x, z (its origin on the floor), turned by r.</summary>
    private static int Stand(Writer w, string part, double x, double y, double z, byte r = 0, Look look = default, PyDict? config = null) =>
        w.Root(part, B(x, y + 0.003, z), r, look, config);

    /// <summary>A lamp under the deck over a room (its back up against the plate's underside).</summary>
    private static int Lamp(Writer w, string part, double x, double ceil, double z, bool across = false)
    {
        var r = across ? Turn(PZ, NX) : Turn(PX, PZ); // (its back, -z, up: its length across or along the ship)
        return w.Root(part, B(x, ceil - 0.006, z), r);
    }

    // ---- Rooms

    /// <summary>A room a storey's row is cut into: what it is and how long (m along the ship; the last takes what is left).</summary>
    private readonly record struct Room(string Kind, double Len);

    private static Room R(string kind, double len) => new(kind, len);

    /// <summary>
    /// One storey of a citadel: a corridor down the middle (its walls with a door into every room), rooms either side to the citadel's
    /// side walls, the partitions between them, a lamp in each and the fittings its kind takes. Floor y, ceiling the deck a storey up.
    /// </summary>
    private static void Storey(Writer w, double z0, double z1, double y, Room[] starboard, Room[] port, double halfX = CitadelX)
    {
        var ceil = y + Storey4 - Thick(DeckH) - 0.012;
        foreach (var s in (ReadOnlySpan<int>)[1, -1])
        {
            var rooms = s > 0 ? starboard : port;
            var rows = new List<(double A, double B, string Kind)>();
            var at = z0 + 0.06;
            for (var i = 0; i < rooms.Length && at < z1 - 2.5; i++)
            {
                var b = i == rooms.Length - 1 ? z1 - 0.06 : Math.Min(z1 - 0.06, at + rooms[i].Len);
                rows.Add((at, b, rooms[i].Kind));
                at = b;
            }
            var doors = rows.Where(r => r.Kind != "void").Select(r => Math.Round(r.Kind == "lift" ? r.B - 1.2 : (r.A + r.B) / 2, 2)).ToList();
            WallZ(w, s * CorridorX, z0 + 0.06, z1 - 0.06, y, ceil, doors, s > 0 ? WallLook : WallLook);
            WallZ(w, s * halfX, z0 + 0.06, z1 - 0.06, y, ceil, [], WallLook);
            for (var i = 1; i < rows.Count; i++)
                WallX(w, rows[i].A, Math.Min(s * (CorridorX + 0.06), s * (halfX - 0.06)), Math.Max(s * (CorridorX + 0.06), s * (halfX - 0.06)), y, ceil, [], WallLook);
            foreach (var (a, b, kind) in rows)
            {
                if (kind is "void" or "lift")
                    continue;
                Lamp(w, "Strip Light", s * (CorridorX + halfX) / 2, ceil, (a + b) / 2);
                Furnish(w, kind, s, CorridorX + 0.06, halfX - 0.06, a + 0.06, b - 0.06, y); // (from the partitions' faces)
            }
        }
        // The corridor's own lamps, every 8 m.
        for (var z = z0 + 4; z < z1 - 2; z += 8)
            Lamp(w, "Strip Light", 0, ceil, z, across: true);
    }

    private const double Storey4 = 4.5;

    /// <summary>
    /// A room's fittings by its kind: side s (+1 starboard), from x = s*xa (the corridor's wall) out to s*xb, z a to b, floor y. Each kind
    /// is a few of the game's own fittings set against its walls, so a cutaway reads as a ship's rooms.
    /// </summary>
    private static void Furnish(Writer w, string kind, int s, double xa, double xb, double a, double b, double y)
    {
        var outer = s > 0 ? PX : NX; // (the way to the outer wall)
        var wallX = s * xb;
        double X(double t) => s * (xa + (xb - xa) * t); // (across the room: 0 the corridor's side, 1 the outer wall)
        var mid = (a + b) / 2;
        var len = b - a;
        switch (kind)
        {
            case "berth": // a berthing compartment: bunks along both partitions, lockers by the outer wall
                for (var i = 0; i < 4; i++)
                {
                    var x = X(0.28 + 0.16 * i);
                    Fit(w, "Bunk", y, NZ, a, x, new Look(Blue, "painted steel"));
                    if (len > 5.2)
                        Fit(w, "Bunk", y, PZ, b, x, new Look(Blue, "painted steel"));
                }
                Fit(w, "Locker", y, outer, wallX, mid - 0.7);
                Fit(w, "Locker", y, outer, wallX, mid + 0.7);
                break;
            case "officer": // an officer's cabin
                Fit(w, "Iron Bed", y, NZ, a, X(0.75));
                Fit(w, "Steel Desk", y, outer, wallX, mid + len * 0.2);
                Fit(w, "Filing Cabinet", y, PZ, b, X(0.8));
                Fit(w, "Locker", y, PZ, b, X(0.6));
                break;
            case "mess": // a mess hall: rows of tables
                for (var z = a + 2.2; z < b - 1.8; z += 3.0)
                    for (var t = 0.25; t < 0.9; t += 0.3)
                        Stand(w, "Ship Mess Table", X(t), y, z);
                Fit(w, "Galley Coffee Station", y, outer, wallX, a + 1.2);
                break;
            case "galley":
                Fit(w, "Ship Galley Range", y, outer, wallX, mid - 1.2);
                Fit(w, "Ship Galley Range", y, outer, wallX, mid + 1.2);
                Fit(w, "Service Counter 2 m", y, NZ, a, X(0.45));
                Fit(w, "Store Shelving 2 m", y, PZ, b, X(0.7));
                Fit(w, "Galley Stove", y, PZ, b, X(0.35));
                break;
            case "medbay":
                for (var t = 0.3; t < 0.95; t += 0.25)
                    Fit(w, "Medical Station", y, NZ, a, X(t));
                Fit(w, "Medicine Cabinet", y, outer, wallX, mid);
                Fit(w, "Medicine Cabinet", y, PZ, b, X(0.6));
                break;
            case "heads":
                for (var t = 0.35; t < 0.95; t += 0.2)
                    Fit(w, "Ship Toilet", y, NZ, a, X(t));
                Fit(w, "Ship Basin", y, PZ, b, X(0.4));
                Fit(w, "Ship Basin", y, PZ, b, X(0.55));
                Fit(w, "Shower Stall", y, outer, wallX, mid);
                break;
            case "armoury":
                for (var t = 0.3; t < 0.95; t += 0.15)
                    Fit(w, "Gun Rack", y, NZ, a, X(t));
                for (var t = 0.3; t < 0.95; t += 0.15)
                    Fit(w, "Locker", y, PZ, b, X(t));
                Stand(w, "Ammo Crate", X(0.55), y + 0.2, mid); // (its origin 0.2 m over its foot)
                break;
            case "suits": // a suit room by the boarding deck and the airlocks
                for (var t = 0.3; t < 0.95; t += 0.13)
                    Fit(w, "Space Suit Locker", y, NZ, a, X(t));
                var dock = Fit(w, "MMU Dock", y, PZ, b, X(0.5), keep: ["mmu"]);
                w.OnSocket(dock, "mmu", "Manned Manoeuvring Unit", "mount", w.Rot(dock));
                Fit(w, "MMU Dock", y, PZ, b, X(0.75));
                break;
            case "stores":
                for (var t = 0.22; t < 0.95; t += 0.2)
                {
                    Fit(w, "Store Shelving 2 m", y, NZ, a, X(t));
                    if (len > 4.5)
                        Fit(w, "Store Shelving 2 m", y, PZ, b, X(t));
                }
                break;
            case "servers":
                for (var z = a + 1.4; z < b - 1.0; z += 2.4)
                    for (var t = 0.25; t < 0.92; t += 0.1)
                        Stand(w, "Server Rack", X(t), y, z);
                break;
            case "office":
                for (var t = 0.3; t < 0.9; t += 0.3)
                {
                    Fit(w, "Steel Desk", y, NZ, a, X(t));
                    Fit(w, "Computer", y, PZ, b, X(t));
                }
                Fit(w, "Filing Cabinet", y, outer, wallX, mid);
                Stand(w, "Office Plant", X(0.15), y, b - 0.5);
                break;
            case "workshop":
                Fit(w, "Repair Station", y, NZ, a, X(0.4));
                Fit(w, "Tool Rack", y, NZ, a, X(0.7));
                Fit(w, "Tool Rack", y, PZ, b, X(0.55));
                Stand(w, "Recycler", X(0.8), y, mid);
                Stand(w, "Assembler", X(0.45), y, mid);
                break;
            case "brig": // cells: bed and bench to a cell, the cells' walls across the room
                for (var z = a + 2.6; z < b - 0.5; z += 2.6)
                    WallX(w, z, Math.Min(X(0.4), X(1.0)), Math.Max(X(0.4), X(1.0)), y, y + 2.6, [], LowLook);
                for (var z = a + 1.3; z < b - 0.5; z += 2.6)
                    Fit(w, "Iron Bed", y, outer, wallX, z);
                break;
            case "rec": // a rec room
                Stand(w, "Ship Mess Table", X(0.35), y, mid - 1.5);
                Stand(w, "Ship Mess Table", X(0.35), y, mid + 1.5);
                Fit(w, "Waiting Bench 2 m", y, outer, wallX, mid);
                Fit(w, "Waiting Bench 2 m", y, NZ, a, X(0.6));
                Stand(w, "Office Plant", X(0.85), y, a + 0.6);
                Stand(w, "Office Plant", X(0.85), y, b - 0.6);
                break;
            case "ready": // a ready room: couches for a boarding party, its lockers
                for (var z = a + 2.0; z < b - 1.5; z += 2.6)
                    for (var t = 0.3; t < 0.9; t += 0.2)
                        Stand(w, "Crash Couch", X(t), y, z);
                Fit(w, "Locker", y, outer, wallX, a + 1.0);
                Fit(w, "Locker", y, outer, wallX, a + 1.7);
                break;
            case "bridge": // the bridge: the helm forward, crew stations in rows, the captain's place behind; screens, no glass
                Stand(w, "Bridge Helm", X(0.5), y, a + 2.5);
                for (var t = 0.2; t < 0.9; t += 0.15)
                {
                    Stand(w, "Crew Station", X(t), y, a + 5.0);
                    Stand(w, "Crew Station", X(t), y, a + 7.5);
                }
                Stand(w, "Flight Couch", X(0.5), y, a + 10.5);
                Fit(w, "Gunnery Console", y, NZ, a, X(0.2));
                Fit(w, "Gunnery Console", y, NZ, a, X(0.8));
                Fit(w, "Engineer's Panel", y, outer, wallX, a + 6);
                Fit(w, "Computer", y, outer, wallX, a + 9);
                Fit(w, "Teleprinter", y, PZ, b, X(0.4));
                Stand(w, FlightComputerName, X(0.12), y, b - 1.2, 0, default, new PyDict { ["keys"] = "Ship", ["mouseAim"] = true }); // (it flies the ship from the helm)
                break;
            case "cic": // the combat information centre
                for (var z = a + 2.5; z < b - 2.0; z += 3.0)
                    for (var t = 0.2; t < 0.9; t += 0.15)
                        Stand(w, z - a < 4 ? "Gunnery Console" : "Crew Station", X(t), y, z);
                Fit(w, "Fire Director", y + 0.125, outer, wallX, mid);
                Fit(w, "Teleprinter", y, PZ, b, X(0.4));
                Fit(w, "Server Rack", y, PZ, b, X(0.7));
                break;
            case "briefing":
                for (var z = a + 2.4; z < b - 1.6; z += 1.6)
                    Stand(w, "Waiting Bench 2 m", X(0.45), y, z);
                Fit(w, "Steel Desk", y, NZ, a, X(0.45));
                break;
            case "lifesupport":
                for (var t = 0.25; t < 0.95; t += 0.18)
                    Fit(w, "Life Support Unit", y, NZ, a, X(t));
                for (var t = 0.25; t < 0.95; t += 0.12)
                    Fit(w, "Oxygen Tank", y, PZ, b, X(t));
                break;
            case "flight": // flight control for the hangar
                for (var t = 0.25; t < 0.9; t += 0.2)
                    Stand(w, "Crew Station", X(t), y, a + 2.5);
                Fit(w, "Computer", y, outer, wallX, mid);
                Fit(w, "Teleprinter", y, PZ, b, X(0.5));
                break;
            default:
                throw new InvalidOperationException($"mainframe: no room kind \"{kind}\"");
        }
    }

    // ---- Lifts

    /// <summary>A lift: its rail (a Linear Actuator Large 32 m) stood on end against the bulkhead at Z (Side +1: its aft face), at X,
    /// its carriage's middle at Mid (the car's floor 0.46 m under it), the car on the carriage and its shaft cut through every deck it
    /// passes.</summary>
    private readonly record struct Lift(double Z, double X, double Mid, int Side);

    private const string LiftRail = "Linear Actuator Large 32 m", LiftCar = "Freight Lift Car";
    private const double CarDrop = 0.46, RailHalf = 16;

    private static readonly Lift[] Lifts =
    [
        new(ZCrew, 4.5, UpperDeck + CarDrop, 1),
        new(ZMarines, 4.5, UpperDeck + CarDrop, 1),
        new(ZHangar, 4.5, MainDeck + CarDrop, 1), // (the boarding deck to the main deck)
        new(ZHangar, -4.5, 41 + CarDrop, 1), // (the main deck to the hangar's)
        new(ZCommand, -10, 41 + CarDrop, -1), // (a second into the hangar, from its aft end)
        new(ZCommand, 4.5, UpperDeck + CarDrop, 1),
        new(ZVault, 4.5, 17 + CarDrop, 1), // (the yard to the vault)
    ];

    /// <summary>The shaft a lift's car and rail pass through: x0..x1, z0..z1.</summary>
    private static (double X0, double X1, double Z0, double Z1) Shaft(Lift l)
    {
        double a = l.Z + l.Side * 0.35, b = l.Z + l.Side * 6.3;
        return (l.X - 2.2, l.X + 2.2, Math.Min(a, b), Math.Max(a, b));
    }

    /// <summary>The openings lifts need in a level at y over x0..x1 (a deck the rail passes through, not one under its foot).</summary>
    private static List<Hole> ShaftHoles(double y, double x0, double x1, double z0, double z1)
    {
        var holes = new List<Hole>();
        foreach (var l in Lifts)
        {
            if (y < l.Mid - RailHalf - CarDrop + 0.3 || y > l.Mid + RailHalf + 0.5)
                continue;
            var (a0, a1, b0, b1) = Shaft(l);
            if (a0 >= x0 && a1 <= x1 && b0 >= z0 && b1 <= z1)
                holes.Add(LevelHole(a0, a1, b0, b1, 20));
        }
        return holes;
    }

    private static void LayLifts(Writer w)
    {
        foreach (var l in Lifts)
        {
            var rail = w.Root(LiftRail, B(l.X, l.Mid, l.Z + l.Side * 1.055), l.Side > 0 ? TurnYZ(PZ, PY) : TurnYZ(NZ, PY), new Look(Frame, "dirty steel"), new PyDict { ["stopLow"] = 0, ["stopHigh"] = 100, ["speed"] = 1 }, keep: ["top"]);
            // (the rail's carriage is a large socket and the car small: an adapter between them)
            var adapter = w.OnSocket(rail, "top", "Adapter Large to Small", "back", l.Side > 0 ? TurnYZ(PZ, PY) : TurnYZ(NZ, PY), look: new Look(Frame, "dirty steel"));
            var car = w.OnSocket(adapter, "front2.2", LiftCar, "mount", l.Side > 0 ? Turn(PZ, PY) : Turn(NZ, PY), look: new Look(Hazard, "hazard paint"));
            w.OnSocket(car, "button", "Button", "-x", w.Rot(car));
        }
    }

    // ---- The flank passages and their airlocks (z 300 to 860, both sides, on the main deck)

    private const double PassageTop = 33.5, AirlockPitch = 20, HatchY = 31.9;

    /// <summary>The skin's half width at the main deck and at the passages' ceiling, midships (the hull is straight there).</summary>
    private static double SkinAt(double y) => HalfWidth(600, y);

    private static double PassageIn => SkinAt(MainDeck) - 8;
    private static double PassageOut => SkinAt(MainDeck) - 5;

    /// <summary>The airlocks' places along a passage: every 20 m, none where a bulkhead stands.</summary>
    private static IEnumerable<double> AirlockZs()
    {
        for (var z = ZHangar + 10; z < ZEngines - 5; z += AirlockPitch)
            if (Bulkheads.All(b => Math.Abs(b - z) > 4))
                yield return z;
    }

    /// <summary>The passages' runs between the bulkheads they pass.</summary>
    private static IEnumerable<(double Z0, double Z1)> PassageRuns()
    {
        var stops = Bulkheads.Where(b => b >= ZHangar && b <= ZEngines).ToList();
        for (var i = 0; i + 1 < stops.Count; i++)
            yield return (stops[i] + 0.06, stops[i + 1] - 0.06);
    }

    private static void Passages(Writer w, Dictionary<(int, int), Plane> planes)
    {
        foreach (var s in (ReadOnlySpan<int>)[1, -1])
        {
            foreach (var (z0, z1) in PassageRuns())
            {
                var locks = AirlockZs().Where(z => z > z0 + 3 && z < z1 - 3).ToList();
                // The inner wall, a door into the zone midway; the outer wall, a door into each airlock.
                var into = Math.Round((z0 + z1) / 2 + 4, 1);
                WallZ(w, s * PassageIn, z0, z1, MainDeck, PassageTop - 0.05, [into], WallLook);
                WallZ(w, s * PassageOut, z0, z1, MainDeck, PassageTop - 0.05, locks, WallLook);
                // Its ceiling, out to the skin.
                double x0 = s * (PassageIn - 0.05), x1 = s * (SkinAt(PassageTop) - 0.08);
                Slab(w, PassageTop, Math.Min(x0, x1), Math.Max(x0, x1), z0, z1, [], new Look(Ceil, "painted steel"));
                for (var z = z0 + 5; z < z1 - 3; z += 10)
                    Lamp(w, "Strip Light", s * (PassageIn + PassageOut) / 2, PassageTop - 0.04 - 0.012, z);
                foreach (var z in locks)
                    Airlock(w, s, z, planes);
            }
        }
    }

    /// <summary>
    /// An airlock off a flank passage: a chamber from the passage's outer wall to the skin (its side walls cut along the skin's slant),
    /// the door from the passage (laid with the wall), and the outer hatch over the opening the Laser Cutter cut in the belt's plate.
    /// </summary>
    private static void Airlock(Writer w, int s, double z, Dictionary<(int, int), Plane> planes)
    {
        var top = PassageTop - 0.05;
        foreach (var dz in (ReadOnlySpan<double>)[-1.6, 1.6])
        {
            var pl = WallAcross(z + dz, $"airlock at z {z:0}, side {s}");
            double a = PassageOut + 0.06, b0 = SkinAt(MainDeck) - 0.1, b1 = SkinAt(top) - 0.1;
            var poly = s > 0
                ? new List<(double V, double U)> { (a * 100, MainDeck * 100), (b0 * 100, MainDeck * 100), (b1 * 100, top * 100), (a * 100, top * 100) }
                : [(-b0 * 100, MainDeck * 100), (-a * 100, MainDeck * 100), (-a * 100, top * 100), (-b1 * 100, top * 100)];
            Plate(w, pl, poly, [], WallH, 0, new Look(Hazard, "hazard paint"));
        }
        Lamp(w, "Bulkhead Light", s * (PassageOut + 2.2), top - 0.012, z);
        // The outer hatch, on the belt's frame node over its opening.
        var (pl2, v, u) = AirlockSpot(s, z, planes);
        OnPlane(w, pl2, "Docking Hatch", v, Thick(SkinH) / 2 + 0.004, u, s > 0 ? (byte)0 : Turn(NX, PY), new Look(Armour, "plated steel"));
    }

    /// <summary>Where an airlock's outer hatch is: the belt's plane and its middle there (cm). The opening's middle is a hatch's lid's
    /// middle less its 0.69 m reach from the socket.</summary>
    private static (Plane Pl, double V, double U) AirlockSpot(int s, double z, Dictionary<(int, int), Plane> planes)
    {
        var band = BandOf(z);
        var f = s > 0 ? 1 : 7;
        var pl = planes[(band, f)];
        var x = s * HalfWidth(z, HatchY);
        var (v, u) = pl.Of(new D3(x, HatchY, z));
        return (pl, v, u);
    }

    /// <summary>The band of the hull a station z is in.</summary>
    private static int BandOf(double z)
    {
        for (var b = 0; b + 1 < Stations.Length; b++)
            if (z < Stations[b + 1].Z)
                return b;
        return Stations.Length - 2;
    }

    /// <summary>A part on a plane at (v, n, u) (cm, m, cm), turned in the plane's frame (x across, y out, z along): on its frame node by a
    /// socket of the node's that faces it, or on the body's frame for a plane on it.</summary>
    private static int OnPlane(Writer w, Plane pl, string part, double v, double n, double u, byte turn, Look look = default, PyDict? config = null, IReadOnlyCollection<string>? keep = null)
    {
        var local = new Vector3((float)(v / 100), (float)n, (float)(u / 100));
        if (pl.Node < 0)
            return w.Root(part, B(pl.O.X, pl.O.Y, pl.O.Z) + Rot24.Apply(pl.Rot, local), Rot24.Compose(pl.Rot, turn), look, config, keep: keep);
        var d = w.Def(part);
        foreach (var sk in d.Sockets)
        {
            if (keep?.Contains(sk.Name) == true)
                continue;
            var dir = Rot24.ApplyDir(turn, sk.Dir);
            var key = dir switch { NY => 'u', NX => 'e', PX => 'w', NZ => 'n', PZ => 's', _ => 'd' };
            if (w.Free(pl.Node, key))
                return w.OnSocket(pl.Node, w.Take(pl.Node, key), part, sk.Name, turn, local, look, config);
        }
        throw new InvalidOperationException($"mainframe: no socket of {part} can hang on {pl.Name}'s frame node");
    }
}
