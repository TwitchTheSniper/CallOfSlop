namespace CallOfSlop.ShipDesigner;

/// <summary>
/// A ship's interior equipment (Karl, 2026-11-27: "add all of the different parts relevant to ships"): reactors, batteries, life support,
/// tanks, cargo, bunks, lockers, the galley, the heads, the med bay, consoles, benches and racks, each drawn fresh for the designer after
/// the game part it stands for (named in its description), never converted from it. Like the seat (<see cref="DesignSeats"/>, whose
/// socket kind they share) one is dropped on the skin by a room, or linked to one, and stands on that room's floor: a free-standing one at
/// the room's point nearest where it was dropped, a wall one backed onto the room's wall nearest that point, facing in. In the game each is
/// a solid design element (DesignParts.Pieces.cs makes every piece with a mesh one); what they do there comes later.
/// Frame: X across, Y up off the floor, Z the way it faces (a wall one's back is at -<see cref="Fur.Hz"/>, against the wall).
/// </summary>
public static class DesignFurnishings
{
    private const string A = PieceKinds.Armour, G = PieceKinds.Gun, P = PieceKinds.Pale, Am = PieceKinds.Amber, Gl = PieceKinds.Glow, Bk = PieceKinds.Black, Bl = PieceKinds.Blue;
    private const string Red = "#c0392b", Green = "#4f9a5a", White = "#dfe5e8", Steel = "#6c737b", Olive = "#5d6448", Wood = "#7a5a3a", Cloth = "#3f4c5a", Water = "#3a7fb0";

    /// <summary>One interior piece: its kind's name and gallery group, half its width (X) and depth (Z), its height, whether it backs onto a
    /// wall, its description and its drawing.</summary>
    public sealed record Fur(string Name, string Group, double Hx, double Hz, double H, bool Wall, string Desc, Action<DesignMesh> Draw);

    /// <summary>The gallery's inside groups, in the order they are shown.</summary>
    public static readonly string[] Groups = ["Crew", "Command", "Power", "Life support", "Tanks", "Cargo", "Galley", "Hygiene", "Medical", "Workshop", "Armoury", "Drive room"];

    public static readonly Fur[] All =
    [
        // ---- Power
        new("Reactor S", "Power", 1.1, 1.1, 2.4, false, "A small fusion reactor (after the Medium Reactor): a 1.8 m core on its plinth. Give it a Reactor room.", m => Reactor(m, 0.9, 2.4)),
        new("Reactor M", "Power", 1.6, 1.6, 3.2, false, "A fusion reactor (after the Large Reactor): a 2.8 m core with its coolant rings.", m => Reactor(m, 1.4, 3.2)),
        new("Reactor L", "Power", 2.5, 2.5, 4.4, false, "A capital ship's reactor (after the Capital Reactor): a 4.4 m core. Needs a tall room.", m => Reactor(m, 2.2, 4.4)),
        new("Battery bank", "Power", 0.8, 0.3, 1.8, true, "A rack of ship batteries against the wall (after the Ship Battery M): it stores the reactor's power.", BatteryBank),
        new("Power board", "Power", 0.6, 0.18, 2.0, true, "A wall power board (after the Heavy Power Terminal): breakers and bus bars for the ship's circuits.", PowerBoard),
        // ---- Life support
        new("Life support unit", "Life support", 0.7, 0.35, 2.0, true, "A life support unit (after the game's Life Support Unit): air, heat and pressure for the rooms round it.", LifeSupport),
        new("Oxygen tanks", "Life support", 0.75, 0.25, 1.7, true, "Four oxygen bottles strapped in a wall rack (after the Oxygen Tank).", OxygenTanks),
        new("Air scrubber", "Life support", 0.5, 0.3, 1.6, true, "A CO2 scrubber: filter beds behind grilles and a fan on top.", Scrubber),
        new("Gravity generator", "Life support", 1.3, 1.3, 2.2, false, "A deck gravity generator (after the Gravity Generator): a ring of coils over a lit core.", Gravity),
        // ---- Tanks
        new("Fuel tank S", "Tanks", 0.8, 0.8, 1.9, false, "A 1.4 m upright fuel tank in its frame (after the Small Fluid Tank).", m => UprightTank(m, 0.7, 1.9, Am)),
        new("Fuel tank L", "Tanks", 1.4, 1.4, 3.0, false, "A 2.6 m upright fuel bunker in its frame (after the Large Fluid Bunker).", m => UprightTank(m, 1.3, 3.0, Am)),
        new("Water tank", "Tanks", 0.7, 1.3, 1.5, false, "A 2.4 m water tank lying in its cradle (after the Fluid Tank).", WaterTank),
        // ---- Cargo
        new("Container 1 m", "Cargo", 0.5, 0.5, 1.0, false, "A 1 m standard container (after the Standard Container 1m).", m => Container(m, 0.5)),
        new("Container 2 m", "Cargo", 1.0, 1.0, 2.0, false, "A 2 m standard container (after the Standard Container 2m).", m => Container(m, 1.0)),
        new("Container 3 m", "Cargo", 1.5, 1.5, 3.0, false, "A 3 m standard container (after the Standard Container 3m).", m => Container(m, 1.5)),
        new("Container cradle", "Cargo", 1.3, 1.3, 0.35, false, "A deck cradle with corner clamps that a 2 m container locks into (after the Container Cradle 2x2).", Cradle),
        new("Cargo racks", "Cargo", 1.2, 0.4, 2.4, true, "Wall racking with crates on three shelves and a cargo net.", CargoRacks),
        new("Crate stack", "Cargo", 0.8, 0.6, 1.4, false, "Strapped crates stacked on a pallet.", CrateStack),
        // ---- Crew
        new("Bunk", "Crew", 1.05, 0.45, 1.9, true, "Two crew bunks, one over the other, against the wall (after the Bunk and the Magpie Bunk).", Bunk),
        new("Crash couch", "Crew", 0.45, 1.05, 1.0, false, "An acceleration couch, padded and harnessed (after the Crash Couch).", CrashCouch),
        new("Locker", "Crew", 0.3, 0.25, 1.9, true, "A crew locker (after the Locker).", Locker),
        new("Suit locker", "Crew", 0.5, 0.35, 2.1, true, "A space suit locker with a glass front (after the Space Suit Locker).", SuitLocker),
        new("MMU dock", "Crew", 0.6, 0.4, 1.8, true, "A wall dock for a manoeuvring pack (after the MMU Dock).", MmuDock),
        // ---- Command
        new("Chart table", "Command", 0.8, 0.5, 1.0, false, "A lit chart table for plotting (after the Chart Table).", ChartTable),
        new("Engineer's panel", "Command", 0.8, 0.2, 1.9, true, "A wall panel of gauges and switches for the ship's systems (after the Engineer's Panel).", EngineersPanel),
        new("Sensor console", "Command", 0.6, 0.4, 1.4, true, "A sensor operator's console: a sweep screen and a track board.", m => Console(m, Gl, 1)),
        new("Gunnery console", "Command", 0.6, 0.4, 1.4, true, "A gunnery console with a sight screen and arming switches (after the Gunnery Console).", m => Console(m, Red, 2)),
        new("Comms console", "Command", 0.6, 0.4, 1.4, true, "A comms desk: a handset, a set of dials and a screen.", m => Console(m, Green, 3)),
        // ---- Galley
        new("Mess table", "Galley", 1.2, 0.95, 0.9, false, "A mess table with a bench either side (after the Ship Mess Table).", MessTable),
        new("Galley range", "Galley", 0.9, 0.35, 1.9, true, "A galley counter with its hob and a hood (after the Ship Galley Range).", GalleyRange),
        new("Coffee station", "Galley", 0.4, 0.3, 1.2, true, "A coffee machine on its counter (after the Galley Coffee Station).", Coffee),
        // ---- Hygiene
        new("Toilet", "Hygiene", 0.25, 0.35, 0.9, true, "A vacuum toilet (after the Ship Toilet).", Toilet),
        new("Shower", "Hygiene", 0.45, 0.45, 2.1, true, "A shower stall with a screen (after the Shower Stall).", Shower),
        new("Basin", "Hygiene", 0.3, 0.25, 1.4, true, "A wash basin with a mirror over it (after the Ship Basin).", Basin),
        // ---- Medical
        new("Med bed", "Medical", 0.5, 1.1, 1.5, false, "A treatment bed on its lift with a monitor at its head (after the Medical Station).", MedBed),
        new("Medicine cabinet", "Medical", 0.4, 0.18, 1.8, true, "A wall cabinet of medicine (after the Medicine Cabinet).", MedCabinet),
        // ---- Workshop
        new("Repair bench", "Workshop", 1.0, 0.4, 1.8, true, "A work bench with a vice and a tool board over it (after the Repair Station).", RepairBench),
        new("Tool rack", "Workshop", 0.8, 0.1, 1.8, true, "A wall tool board (after the Tool Rack).", ToolRack),
        // ---- Armoury
        new("Gun rack", "Armoury", 0.7, 0.2, 1.8, true, "A locked rack of long guns (after the Gun Rack).", GunRack),
        new("Ammo crates", "Armoury", 0.7, 0.45, 1.0, false, "Ammunition boxes stacked on a pallet.", AmmoCrates),
        // ---- Drive room
        new("Jump drive", "Drive room", 1.8, 1.8, 3.2, false, "A jump drive (after the Jump Drive): field rings round a lit core on a heavy base.", JumpDrive),
    ];

    /// <summary>The interior kinds as piece kinds, appended to <see cref="PieceKinds.All"/> (the seat's socket: they stand in a room).</summary>
    public static PieceKind[] Kinds => [.. All.Select(f => new PieceKind(f.Name, f.Group, "skin", "seat", "any room",
        f.Desc + (f.Wall ? " Drop it by a room: it stands against the room's nearest wall." : " Drop it by a room: it stands on the room's floor."), f.Draw))];

    public static Fur? Of(string kind) => Array.Find(All, f => f.Name == kind);

    public static bool Is(string kind) => Of(kind) is not null;

    /// <summary>A room's interior pieces: too big for their room (warnings, with the room's size).</summary>
    public static void Check(ShipDesign d, DesignScene.Check c)
    {
        foreach (var p in d.Pieces)
        {
            if (Of(p.Kind) is not { } f || DesignSeats.RoomOf(d, p) is not { } r)
                continue;
            var b = d.Box(r);
            var up = d.Stacked ? b[5] - b[4] : b[3] - b[2];
            double w = b[1] - b[0], l = d.Stacked ? b[3] - b[2] : b[5] - b[4];
            var s = Math.Max(0.25, p.Size);
            if (f.H * s > up - 0.02)
                c.Issues.Add(new("warn", $"{p.Kind} is {f.H * s:0.0} m tall and {DesignScene.Name(r)} is {up:0.0} m: make it smaller or the room taller", $"piece:{p.Id}"));
            else if (2 * Math.Min(f.Hx, f.Hz) * s > Math.Min(w, l) || 2 * Math.Max(f.Hx, f.Hz) * s > Math.Max(w, l))
                c.Issues.Add(new("warn", $"{p.Kind} doesn't fit on {DesignScene.Name(r)}'s floor ({w:0.0} x {l:0.0} m): make it smaller or the room bigger", $"piece:{p.Id}"));
        }
    }

    // ================================================================ drawings
    // (Every box that meets another runs into it by 4 mm or more, so no two faces share a plane; feet stand 4 mm off the floor.)

    /// <summary>A plain body from the floor up, hx by hz, h tall.</summary>
    private static void Body(DesignMesh m, double hx, double hz, double h, string c) => m.Box(-hx, 0.004, -hz, hx, h, hz, c);

    /// <summary>A plate proud of a front face at z (6 mm out, its back 6 mm in).</summary>
    private static void Front(DesignMesh m, double x0, double y0, double x1, double y1, double z, string c) => m.Box(x0, y0, z - 0.006, x1, y1, z + 0.006, c);

    private static void Reactor(DesignMesh m, double r, double h)
    {
        var plinth = 0.3 * h / 2.4;
        m.Box(-r - 0.2, 0.004, -r - 0.2, r + 0.2, plinth, r + 0.2, A);
        m.Frustum(plinth - 0.01, r, h - 0.35 * r, r, 20, G);
        m.Frustum(h - 0.35 * r - 0.01, r, h, r * 0.55, 20, A);
        for (var y = plinth + 0.25 * h; y < h - 0.5 * r; y += 0.22 * h)
            m.Frustum(y, r + 0.08, y + 0.08 * h, r + 0.08, 20, Am);
        m.Frustum(plinth + 0.12 * h, r + 0.03, plinth + 0.18 * h, r + 0.03, 20, Gl, cap0: false, cap1: false); // its lit window band
        foreach (var sx in new[] { -1, 1 }) // coolant pipes down to the plinth
            m.Box(sx * (r + 0.02) - 0.08, plinth - 0.01, -0.08, sx * (r + 0.02) + 0.08, h - 0.6 * r, 0.08, Steel);
    }

    private static void BatteryBank(DesignMesh m)
    {
        Body(m, 0.8, 0.3, 1.8, A);
        for (var i = 0; i < 4; i++)
        {
            var x = -0.72 + i * 0.37;
            Front(m, x, 0.15, x + 0.33, 1.6, 0.3, G);
            Front(m, x + 0.05, 1.3, x + 0.28, 1.36, 0.312, Gl);
            Front(m, x + 0.05, 1.42, x + 0.28, 1.46, 0.312, Green);
        }
        m.Box(-0.82, 1.79, -0.32, 0.82, 1.86, 0.32, Am);
    }

    private static void PowerBoard(DesignMesh m)
    {
        Body(m, 0.6, 0.18, 2.0, G);
        Front(m, -0.55, 0.3, 0.55, 1.9, 0.18, A);
        for (var row = 0; row < 5; row++)
            for (var col = 0; col < 6; col++)
            {
                double x = -0.48 + col * 0.165, y = 0.5 + row * 0.27;
                m.Box(x, y, 0.18, x + 0.1, y + 0.16, 0.21, row == 0 ? Red : P);
            }
        m.Box(-0.6, 1.94, -0.2, 0.6, 2.0, 0.2, Am);
        m.Box(-0.4, 0.004, 0.1, 0.4, 0.04, 0.3, Am); // a hazard sill
    }

    private static void LifeSupport(DesignMesh m)
    {
        Body(m, 0.7, 0.35, 2.0, P);
        Front(m, -0.62, 1.0, 0.62, 1.9, 0.35, G);
        for (var y = 1.08; y < 1.85; y += 0.09)
            m.Box(-0.58, y, 0.35, 0.58, y + 0.035, 0.37, Bk); // its intake grille
        Front(m, -0.62, 0.1, -0.05, 0.9, 0.35, A);
        Front(m, 0.05, 0.1, 0.62, 0.9, 0.35, A);
        Front(m, 0.15, 0.6, 0.5, 0.8, 0.362, Gl); // its readout
        m.Box(-0.2, 2.0 - 0.01, -0.2, 0.2, 2.25, 0.2, Steel); // its duct up to the deckhead
        m.Box(-0.7, 0.004, 0.3, 0.7, 0.06, 0.37, Bl);
    }

    private static void OxygenTanks(DesignMesh m)
    {
        m.Box(-0.75, 0.004, -0.25, 0.75, 0.08, 0.25, A); // its foot
        m.Box(-0.75, 0.004, -0.25, 0.75, 1.7, -0.19, G); // its back plate
        for (var i = 0; i < 4; i++)
        {
            var cx = -0.54 + i * 0.36;
            m.Frustum(0.076, 0.16, 1.3, 0.16, 12, White, cx: cx, cz: 0.0);
            m.Frustum(1.296, 0.16, 1.46, 0.06, 12, White, cx: cx, cz: 0.0);
            m.Frustum(1.456, 0.04, 1.56, 0.04, 8, Steel, cx: cx, cz: 0.0);
            m.Frustum(1.0, 0.166, 1.08, 0.166, 12, Green, cap0: false, cap1: false, cx: cx, cz: 0.0);
        }
        m.Box(-0.76, 0.9, -0.2, 0.76, 0.95, 0.2, Am); // its strap
    }

    private static void Scrubber(DesignMesh m)
    {
        Body(m, 0.5, 0.3, 1.45, G);
        for (var y = 0.15; y < 1.35; y += 0.4)
        {
            Front(m, -0.44, y, 0.44, y + 0.34, 0.3, A);
            for (var x = -0.38; x < 0.4; x += 0.1)
                m.Box(x, y + 0.04, 0.3, x + 0.04, y + 0.3, 0.32, Bk);
        }
        m.Frustum(1.446, 0.3, 1.6, 0.3, 14, Steel); // its fan housing
        m.Frustum(1.596, 0.24, 1.61, 0.24, 14, Bk);
    }

    private static void Gravity(DesignMesh m)
    {
        m.Box(-1.3, 0.004, -1.3, 1.3, 0.3, 1.3, A);
        m.Frustum(0.296, 0.5, 1.9, 0.5, 16, G);
        m.Frustum(0.9, 0.53, 1.3, 0.53, 16, Gl, cap0: false, cap1: false);
        for (var y = 0.55; y < 1.9; y += 0.55)
            m.Frustum(y, 1.15, y + 0.18, 1.15, 24, Bl);
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) }) // its four posts
            m.Box(sx * 0.85 - 0.07, 0.296, sz * 0.85 - 0.07, sx * 0.85 + 0.07, 2.0, sz * 0.85 + 0.07, Steel);
        m.Frustum(1.896, 0.6, 2.2, 0.3, 16, A);
    }

    private static void UprightTank(DesignMesh m, double r, double h, string band)
    {
        var f = r + 0.1;
        m.Box(-f, 0.004, -f, f, 0.12, f, A);
        m.Frustum(0.116, r, h - 0.3 * r, r, 18, P);
        m.Frustum(h - 0.3 * r - 0.004, r, h - 0.08, r * 0.4, 18, P);
        m.Frustum(h - 0.084, 0.12, h, 0.12, 8, Steel); // its valve
        m.Frustum(0.4 * h, r + 0.02, 0.4 * h + 0.12, r + 0.02, 18, band, cap0: false, cap1: false);
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            m.Box(sx * (f - 0.05) - 0.04, 0.116, sz * (f - 0.05) - 0.04, sx * (f - 0.05) + 0.04, 0.6 * h, sz * (f - 0.05) + 0.04, G);
    }

    private static void WaterTank(DesignMesh m)
    {
        foreach (var z in new[] { -0.8, 0.8 }) // its two saddles
            m.Box(-0.6, 0.004, z - 0.12, 0.6, 0.5, z + 0.12, A);
        m.Barrel(0, 0.75, -1.25, 1.25, 0.65, 18, P); // (lying along its Z)
        m.Box(-0.08, 1.38, -0.08, 0.08, 1.5, 0.08, Steel);
        m.Box(-0.4, 0.5, 1.249, 0.4, 0.58, 1.262, Water);
    }

    private static void Container(DesignMesh m, double s)
    {
        var post = 0.06 * s + 0.03;
        Body(m, s - 0.01, s - 0.01, 2 * s - 0.01, Bl);
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            m.Box(sx * s - post, 0.004, sz * s - post, sx * s + post, 2 * s, sz * s + post, A);
        for (var x = -s + 0.25 * s; x < s - 0.1 * s; x += 0.25 * s)
        {
            m.Box(x - 0.02, 0.1, s - 0.02, x + 0.02, 2 * s - 0.1, s + 0.01, G); // corrugation ribs, front and back
            m.Box(x - 0.02, 0.1, -s - 0.01, x + 0.02, 2 * s - 0.1, -s + 0.02, G);
        }
        m.Box(-0.5 * s, 1.2 * s, s + 0.005, 0.5 * s, 1.5 * s, s + 0.02, Am); // its placard
    }

    private static void Cradle(DesignMesh m)
    {
        m.Box(-1.3, 0.004, -1.3, 1.3, 0.12, 1.3, G);
        m.Box(-1.1, 0.116, -1.1, 1.1, 0.14, 1.1, A);
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            m.Box(sx * 1.15 - 0.15, 0.116, sz * 1.15 - 0.15, sx * 1.15 + 0.15, 0.35, sz * 1.15 + 0.15, Am);
            m.Box(sx * 1.15 - 0.06, 0.346, sz * 1.15 - 0.06, sx * 1.15 + 0.06, 0.4, sz * 1.15 + 0.06, Steel);
        }
    }

    private static void CargoRacks(DesignMesh m)
    {
        foreach (var x in new[] { -1.18, 1.1 }) // uprights
            m.Box(x, 0.004, -0.4, x + 0.08, 2.4, 0.4, Am);
        for (var y = 0.1; y < 2.4; y += 0.78)
        {
            m.Box(-1.12, y, -0.38, 1.12, y + 0.06, 0.38, G);
            var i = (int)(y * 10);
            for (var x = -1.0; x < 0.95; x += 0.68)
                m.Box(x, y + 0.056, -0.3, x + 0.55 - 0.08 * (i % 3), y + 0.48 + 0.08 * (i % 2), 0.3, (i + (int)(x * 10)) % 2 == 0 ? Olive : Wood);
        }
    }

    private static void Crate(DesignMesh m, double x0, double y0, double z0, double x1, double y1, double z1, string c)
    {
        m.Box(x0, y0, z0, x1, y1, z1, c);
        m.Box(x0 - 0.01, y0 + (y1 - y0) * 0.45, z0 - 0.01, x1 + 0.01, y0 + (y1 - y0) * 0.55, z1 + 0.01, Am); // its strap
    }

    private static void CrateStack(DesignMesh m)
    {
        m.Box(-0.8, 0.004, -0.6, 0.8, 0.14, 0.6, Wood);
        Crate(m, -0.75, 0.136, -0.55, -0.02, 0.76, 0.55, Olive);
        Crate(m, 0.02, 0.136, -0.55, 0.75, 0.66, 0.4, Wood);
        Crate(m, -0.6, 0.756, -0.45, 0.3, 1.4, 0.45, Olive);
    }

    private static void Bunk(DesignMesh m)
    {
        foreach (var x in new[] { -1.05, 0.97 })
            m.Box(x, 0.004, -0.45, x + 0.08, 1.9, 0.45, G); // end frames
        foreach (var y in new[] { 0.3, 1.25 })
        {
            m.Box(-0.98, y, -0.43, 0.98, y + 0.1, 0.43, A);                    // its tray
            m.Box(-0.96, y + 0.096, -0.41, 0.96, y + 0.24, 0.41, Cloth);       // mattress
            m.Box(-0.94, y + 0.236, -0.36, -0.6, y + 0.32, 0.36, White);       // pillow
            m.Box(-0.98, y + 0.096, 0.41, 0.98, y + 0.3, 0.45, Am);            // the restraint rail
        }
        m.Box(0.6, 1.45, -0.45, 0.7, 1.55, -0.38, Gl); // a reading lamp
    }

    private static void CrashCouch(DesignMesh m)
    {
        m.Box(-0.2, 0.004, -0.2, 0.2, 0.36, 0.2, A); // pedestal
        m.Box(-0.42, 0.35, -0.55, 0.42, 0.5, 0.9, G); // seat and leg rest
        m.Box(-0.42, 0.496, -1.05, 0.42, 1.0, -0.5, G); // the raked back
        m.Box(-0.36, 0.496, -0.5, 0.36, 0.62, 0.86, Cloth);
        m.Box(-0.36, 0.6, -1.0, 0.36, 0.95, -0.55, Cloth);
        m.Box(-0.46, 0.5, -0.4, -0.416, 0.7, 0.3, Am); // harness rails
        m.Box(0.416, 0.5, -0.4, 0.46, 0.7, 0.3, Am);
    }

    private static void Locker(DesignMesh m)
    {
        Body(m, 0.3, 0.25, 1.9, Steel);
        Front(m, -0.27, 0.06, 0.27, 1.86, 0.25, G);
        for (var y = 1.55; y < 1.8; y += 0.07)
            m.Box(-0.18, y, 0.25, 0.18, y + 0.03, 0.262, Bk); // vents
        m.Box(0.18, 0.95, 0.25, 0.22, 1.15, 0.28, Am); // its handle
    }

    private static void SuitLocker(DesignMesh m)
    {
        Body(m, 0.5, 0.35, 2.1, A);
        Front(m, -0.44, 0.1, 0.44, 1.95, 0.32, Gl); // its glass front
        m.Box(-0.2, 0.2, -0.05, 0.2, 1.5, 0.15, White); // the suit inside
        m.Frustum(1.5, 0.17, 1.82, 0.17, 12, White, cz: 0.05);
        m.Box(-0.12, 1.58, 0.17, 0.12, 1.74, 0.23, Bk); // its visor
        m.Box(-0.5, 1.99, 0.3, 0.5, 2.06, 0.37, Am);
    }

    private static void MmuDock(DesignMesh m)
    {
        m.Box(-0.6, 0.004, -0.4, 0.6, 0.1, 0.4, A);
        m.Box(-0.6, 0.004, -0.4, 0.6, 1.8, -0.3, G);
        m.Box(-0.45, 0.6, -0.304, 0.45, 1.6, 0.15, P); // the pack
        m.Box(-0.55, 0.9, -0.2, -0.45, 1.3, 0.35, A); // its arms
        m.Box(0.45, 0.9, -0.2, 0.55, 1.3, 0.35, A);
        foreach (var x in new[] { -0.3, 0.3 })
            m.Barrel(x, 0.75, 0.15, 0.25, 0.07, 8, Bk); // nozzles
        m.Box(-0.2, 1.65, -0.304, 0.2, 1.72, -0.25, Gl);
    }

    private static void ChartTable(DesignMesh m)
    {
        m.Box(-0.65, 0.004, -0.35, 0.65, 0.84, 0.35, G);
        m.Box(-0.8, 0.836, -0.5, 0.8, 0.94, 0.5, A);
        m.Box(-0.72, 0.936, -0.42, 0.72, 0.95, 0.42, Gl); // its lit top
        m.Box(-0.8, 0.004, -0.5, 0.8, 0.06, -0.44, Am);
    }

    private static void EngineersPanel(DesignMesh m)
    {
        Body(m, 0.8, 0.2, 1.9, G);
        Front(m, -0.75, 0.9, 0.75, 1.82, 0.2, A);
        for (var col = 0; col < 4; col++)
            for (var row = 0; row < 2; row++)
            {
                double x = -0.56 + col * 0.37, y = 1.15 + row * 0.38;
                m.Box(x - 0.13, y - 0.13, 0.2, x + 0.13, y + 0.13, 0.226, row == 0 ? P : White);
                m.Box(x - 0.01, y - 0.02, 0.224, x + 0.1, y + 0.02, 0.232, Red);
            }
        Front(m, -0.75, 0.2, 0.75, 0.8, 0.2, A);
        for (var x = -0.6; x < 0.65; x += 0.15)
            m.Box(x, 0.45, 0.2, x + 0.05, 0.6, 0.25, Am); // switches
    }

    private static void Console(DesignMesh m, string screen, int style)
    {
        m.Box(-0.6, 0.004, -0.4, 0.6, 0.78, 0.1, G); // its desk body
        m.Box(-0.6, 0.776, -0.4, 0.6, 0.84, 0.4, A); // the desk top, over the knees
        m.Box(-0.6, 0.836, -0.4, 0.6, 1.4, -0.3, G); // its upright
        Front(m, -0.5, 0.92, 0.5, 1.33, -0.3, Bk);
        Front(m, -0.46, 0.95, style == 3 ? 0.1 : 0.46, 1.3, -0.29, screen);
        if (style == 1)
            m.Frustum(0.836, 0.12, 0.85, 0.12, 12, Gl, cx: 0.3, cz: 0.15); // a track ball
        if (style == 2)
            for (var x = -0.45; x < 0.45; x += 0.15)
                m.Box(x, 0.836, 0.1, x + 0.08, 0.87, 0.18, Red); // arming switches
        if (style == 3)
        {
            m.Box(0.2, 0.95, -0.29, 0.46, 1.3, -0.282, P); // dials
            m.Box(-0.4, 0.836, 0.05, -0.15, 0.9, 0.2, Bk); // the handset
        }
    }

    private static void MessTable(DesignMesh m)
    {
        m.Box(-0.15, 0.004, -0.15, 0.15, 0.72, 0.15, A); // its pedestal
        m.Box(-1.2, 0.716, -0.45, 1.2, 0.78, 0.45, P);
        foreach (var z in new[] { -0.8, 0.65 }) // benches
        {
            m.Box(-1.1, 0.004, z + 0.05, 1.1, 0.4, z + 0.1, A);
            m.Box(-1.1, 0.396, z, 1.1, 0.46, z + 0.15, Cloth);
        }
    }

    private static void GalleyRange(DesignMesh m)
    {
        Body(m, 0.9, 0.35, 0.9, Steel);
        m.Box(-0.92, 0.896, -0.36, 0.92, 0.94, 0.36, P);
        foreach (var x in new[] { -0.5, -0.1 })
            foreach (var z in new[] { -0.12, 0.15 })
                m.Frustum(0.936, 0.1, 0.95, 0.1, 12, Bk, cx: x, cz: z); // the hob
        Front(m, -0.85, 0.1, 0.0, 0.75, 0.35, G); // its oven door
        Front(m, -0.7, 0.62, -0.15, 0.68, 0.362, Gl);
        Front(m, 0.05, 0.1, 0.85, 0.82, 0.35, P);
        m.Box(-0.9, 1.5, -0.35, 0.9, 1.9, 0.05, Steel); // its hood
        m.Box(-0.85, 1.496, -0.3, 0.85, 1.51, 0.0, Gl);
        m.Box(-0.9, 0.94, -0.35, 0.9, 1.504, -0.3, P); // the splash back
    }

    private static void Coffee(DesignMesh m)
    {
        Body(m, 0.4, 0.3, 0.9, Wood);
        m.Box(-0.42, 0.896, -0.31, 0.42, 0.94, 0.31, P);
        m.Box(-0.25, 0.936, -0.25, 0.25, 1.2, 0.1, Bk); // the machine
        m.Box(-0.12, 0.98, 0.096, 0.12, 1.02, 0.18, Steel); // its drip tray
        m.Box(-0.15, 1.1, 0.096, 0.15, 1.16, 0.11, Am);
        m.Frustum(0.936, 0.05, 1.05, 0.05, 10, White, cx: 0.28, cz: 0.1); // a mug
    }

    private static void Toilet(DesignMesh m)
    {
        m.Box(-0.25, 0.004, -0.35, 0.25, 0.9, -0.15, P); // its cistern box on the wall
        m.Box(-0.16, 0.004, -0.16, 0.16, 0.36, 0.2, White);
        m.Box(-0.2, 0.356, -0.16, 0.2, 0.42, 0.3, White);
        m.Box(-0.18, 0.416, -0.16, 0.18, 0.44, 0.28, Steel); // its seat
        m.Box(0.06, 0.7, -0.152, 0.16, 0.78, -0.14, Am); // the vacuum flush button
    }

    private static void Shower(DesignMesh m)
    {
        m.Box(-0.45, 0.004, -0.45, 0.45, 0.1, 0.45, P); // its tray
        m.Box(-0.45, 0.004, -0.45, 0.45, 2.1, -0.4, White); // back wall
        m.Box(-0.45, 0.004, -0.4, -0.4, 2.1, 0.45, White); // side wall
        m.Box(-0.396, 0.096, 0.4, 0.45, 2.0, 0.43, Gl); // its screen
        m.Box(-0.45, 2.096, -0.45, 0.45, 2.14, 0.45, Steel);
        m.Frustum(1.8, 0.04, 1.85, 0.1, 10, Steel, cx: 0, cz: -0.3); // the rose
        m.Box(-0.03, 1.85, -0.404, 0.03, 1.88, -0.25, Steel);
    }

    private static void Basin(DesignMesh m)
    {
        m.Box(-0.06, 0.004, -0.25, 0.06, 0.78, -0.13, Steel); // its pedestal
        m.Box(-0.3, 0.776, -0.25, 0.3, 0.9, 0.2, White);
        m.Box(-0.22, 0.86, -0.15, 0.22, 0.904, 0.12, Water);
        m.Box(-0.03, 0.896, -0.24, 0.03, 1.0, -0.15, Steel); // the tap
        m.Box(-0.28, 1.0, -0.25, 0.28, 1.4, -0.22, Steel); // the mirror's frame
        m.Box(-0.25, 1.03, -0.224, 0.25, 1.37, -0.215, Gl);
    }

    private static void MedBed(DesignMesh m)
    {
        m.Box(-0.3, 0.004, -0.7, 0.3, 0.12, 0.7, A); // its base
        m.Box(-0.12, 0.116, -0.12, 0.12, 0.62, 0.12, Steel); // the lift ram
        m.Box(-0.5, 0.616, -1.05, 0.5, 0.7, 1.05, G);
        m.Box(-0.46, 0.696, -1.0, 0.46, 0.82, 1.0, White);
        m.Box(-0.4, 0.816, -0.98, 0.4, 0.88, -0.7, P); // pillow
        m.Box(0.42, 0.004, -1.1, 0.5, 1.5, -1.02, Steel); // its monitor's pole, at the head
        m.Box(0.2, 1.2, -1.1, 0.5, 1.45, -1.03, Bk);
        m.Box(0.23, 1.23, -1.034, 0.47, 1.42, -1.026, Green);
    }

    private static void MedCabinet(DesignMesh m)
    {
        m.Box(-0.4, 1.0, -0.18, 0.4, 1.8, 0.18, White);
        Front(m, -0.37, 1.03, -0.01, 1.77, 0.18, P);
        Front(m, 0.01, 1.03, 0.37, 1.77, 0.18, P);
        m.Box(-0.08, 1.5, 0.18, 0.08, 1.54, 0.2, Red); // its cross
        m.Box(-0.02, 1.44, 0.18, 0.02, 1.6, 0.2, Red);
        Body(m, 0.4, 0.18, 0.8, White); // the cupboard under it
        Front(m, -0.37, 0.05, 0.37, 0.76, 0.18, P);
    }

    private static void RepairBench(DesignMesh m)
    {
        foreach (var x in new[] { -0.95, 0.87 })
            m.Box(x, 0.004, -0.38, x + 0.08, 0.86, 0.38, G);
        m.Box(-1.0, 0.856, -0.4, 1.0, 0.92, 0.4, Wood);
        m.Box(-0.95, 0.25, -0.35, 0.95, 0.3, 0.35, G); // its lower shelf
        m.Box(-1.0, 0.916, -0.4, 1.0, 1.8, -0.34, Steel); // the tool board
        for (var x = -0.85; x < 0.85; x += 0.2)
            m.Box(x, 1.2, -0.344, x + 0.05, 1.55 - 0.1 * ((int)(x * 10 + 9) % 3), -0.31, Am);
        m.Box(0.5, 0.916, 0.1, 0.8, 1.05, 0.3, Bl); // the vice
    }

    private static void ToolRack(DesignMesh m)
    {
        m.Box(-0.8, 0.5, -0.1, 0.8, 1.8, -0.06, Steel);
        for (var x = -0.7; x < 0.7; x += 0.18)
        {
            var h = 0.2 + 0.08 * ((int)(x * 10 + 9) % 4);
            m.Box(x, 1.5 - h, -0.064, x + 0.06, 1.5, 0.0, (int)(x * 10 + 9) % 2 == 0 ? Am : Red);
        }
        m.Box(-0.8, 0.6, -0.064, 0.8, 0.66, 0.1, G); // a shelf
        m.Box(-0.06, 0.004, -0.1, 0.06, 0.504, -0.06, G); // its post
    }

    private static void GunRack(DesignMesh m)
    {
        Body(m, 0.7, 0.2, 1.8, A);
        Front(m, -0.65, 0.08, 0.65, 0.3, 0.2, G);
        for (var x = -0.5; x < 0.6; x += 0.2)
        {
            m.Box(x - 0.03, 0.3, 0.19, x + 0.03, 1.5, 0.26, Bk); // rifles stood in it
            m.Box(x - 0.05, 0.3, 0.2, x + 0.05, 0.7, 0.28, Wood);
        }
        m.Box(-0.7, 1.2, 0.27, 0.7, 1.25, 0.3, Am); // its locking bar
    }

    private static void AmmoCrates(DesignMesh m)
    {
        m.Box(-0.7, 0.004, -0.45, 0.7, 0.12, 0.45, Wood);
        Crate(m, -0.66, 0.116, -0.42, -0.02, 0.5, 0.42, Olive);
        Crate(m, 0.02, 0.116, -0.42, 0.66, 0.5, 0.42, Olive);
        Crate(m, -0.5, 0.496, -0.42, 0.3, 0.9, 0.42, Olive);
        m.Box(-0.3, 0.7, 0.416, 0.1, 0.8, 0.43, Am);
    }

    private static void JumpDrive(DesignMesh m)
    {
        m.Box(-1.8, 0.004, -1.8, 1.8, 0.4, 1.8, A);
        m.Frustum(0.396, 0.7, 2.6, 0.7, 18, G);
        m.Frustum(0.9, 0.73, 2.1, 0.73, 18, Gl, cap0: false, cap1: false);
        for (var y = 0.8; y < 2.6; y += 0.6)
            m.Frustum(y, 1.55, y + 0.22, 1.55, 28, Bl);
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            m.Box(sx * 1.3 - 0.1, 0.396, sz * 1.3 - 0.1, sx * 1.3 + 0.1, 3.0, sz * 1.3 + 0.1, Steel);
        m.Box(-1.45, 2.996, -1.45, 1.45, 3.2, 1.45, A);
        m.Box(-1.8, 0.396, 1.7, 1.8, 0.44, 1.8, Am);
    }
}
