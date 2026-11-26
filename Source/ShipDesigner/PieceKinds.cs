namespace CallOfSlop.ShipDesigner;

/// <summary>
/// What can go on a hull: drives, guns and systems on the skin, and the sockets (airlock, dock, cargo hatch, boarding hatch) that join the
/// outside to a room inside. Each draws itself in its own frame: X across, Y out of the skin, Z towards the bow (on an end: towards the roof).
/// </summary>
public sealed record PieceKind(string Name, string Group, string Where, string? Socket, string Needs, string Desc, Action<DesignMesh> Draw)
{
    /// <summary>How far a socket may be from the room it opens into (the void between a room's wall and the skin, then a little).</summary>
    /// <summary>A drive's service socket (Karl, 2026-11-24): placed on the skin, it gets a socket on the inside, opposite it, in the
    /// room behind (its feed and access panel). It cuts no hole and is not a way aboard.</summary>
    /// RCS quads need none (Karl, 2026-11-25): they are fed by their pads, with no way in from inside.
    public bool Service => Group == "Drives" && Name != "RCS quad";

    public double Reach => Service ? 3.0 : Socket switch { "dock" => 2.6, "cargo" => 2.6, "ramp" => RampReach, _ => 2.2 };

    /// <summary>How far in from the skin a boarding ramp's room floor may be (Karl, 2026-11-24): its well and stair make up the rest.</summary>
    public const double RampReach = 3.0;

    /// <summary>The hole a socket cuts through the skin, in its own frame (half sizes across and along; Round: a circle of radius Hx).</summary>
    public (double Hx, double Hz, bool Round)? Opening => Socket switch
    {
        "airlock" => (1.15, 0.75, false), "window" => (0.55, 0.9, false), "dock" => (1.05, 1.05, true), "cargo" => (1.5, 1.5, false), "shaft" => (0.5, 0.5, false),
        "ramp" => (1.25, 1.6, false), "gangway" => (1.1, 0.6, false), "ladder" => (1.05, 0.55, false), _ => PieceDocking.Opening(Socket),
    };

    /// <summary>What keeps hull cladding clear: the opening, or a drive's base on the skin (it cuts no hole). Pieces with one get the
    /// clearance and hole size sliders (Karl, 2026-11-25).</summary>
    public (double Hx, double Hz, bool Round)? Footprint => Opening ?? Name switch
    {
        "Main drive" => (1.35, 1.35, true), "Small drive" => (0.7, 0.7, true), "RCS quad" => (0.35, 0.35, false), _ => PieceDocking.Footprint(Name),
    };
}

public static class PieceKinds
{
    internal const string Armour = "#2f3237", Gun = "#4b5058", Blue = "#3d8bd9", Amber = "#d9a23d", Glow = "#a9dcff", Pale = "#b8bdc4", Black = "#16181b";

    public static readonly PieceKind[] All =
    [
        new("Main drive", "Drives", "aft", null, "", "A drive bell for the stern, 4 m across.", m =>
        {
            m.Frustum(-0.3, 1.35, 0.5, 1.35, 16, Armour);
            m.Frustum(0.5, 1.2, 3.4, 2.0, 16, Gun, cap0: false, cap1: false);
            m.Frustum(0.6, 1.0, 3.3, 1.85, 16, Black, cap0: true, cap1: false);
            m.Frustum(3.3, 2.05, 3.45, 2.05, 16, Amber, cap0: false, cap1: false);
        }),
        new("Small drive", "Drives", "aft", null, "", "A smaller bell, 2 m across.", m =>
        {
            m.Frustum(-0.2, 0.7, 0.3, 0.7, 12, Armour);
            m.Frustum(0.3, 0.6, 1.8, 1.0, 12, Gun, cap0: false, cap1: false);
            m.Frustum(0.35, 0.5, 1.75, 0.92, 12, Black, cap1: false);
        }),
        new("RCS quad", "Drives", "skin", null, "", "Four small thrusters for turning.", m =>
        {
            m.Box(-0.35, 0, -0.35, 0.35, 0.5, 0.35, Armour);
            m.Barrel(0, 0.3, 0.35, 0.6, 0.1, 8, Gun);
            m.Barrel(0, 0.3, -0.35, -0.6, 0.1, 8, Gun);
            m.Box(0.35, 0.22, -0.08, 0.55, 0.38, 0.08, Gun);
            m.Box(-0.55, 0.22, -0.08, -0.35, 0.38, 0.08, Gun);
        }),
        new("PDC turret", "Weapons", "skin", null, "", "A point defence cannon on a ring, its magazine inside.", m =>
        {
            m.Frustum(0, 0.95, 0.35, 0.85, 12, Armour);
            m.Box(-0.6, 0.35, -0.7, 0.6, 0.95, 0.6, Gun);
            m.Box(-0.45, 0.95, -0.55, 0.45, 1.1, 0.45, Armour);
            m.Barrel(-0.18, 0.65, 0.6, 2.4, 0.07, 8, Black);
            m.Barrel(0.18, 0.65, 0.6, 2.4, 0.07, 8, Black);
        }),
        new("Railgun", "Weapons", "skin", null, "", "A spinal railgun: its rails run 14 m towards the bow.", m =>
        {
            m.Box(-1.0, 0, -3.0, 1.0, 0.9, 1.5, Armour);
            m.Box(-0.55, 0.9, -2.0, -0.25, 1.4, 14.0, Gun);
            m.Box(0.25, 0.9, -2.0, 0.55, 1.4, 14.0, Gun);
            for (var z = 0.0; z < 13; z += 2.5)
                m.Box(-0.7, 0.85, z, 0.7, 1.55, z + 0.4, Armour);
            m.Box(-0.2, 1.0, 13.6, 0.2, 1.3, 14.05, Blue);
        }),
        new("Torpedo sponson", "Weapons", "skin", null, "", "Two torpedo tubes in an armoured sponson, 8 m long.", m =>
        {
            m.Box(-1.2, 0, -4.0, 1.2, 1.5, 3.2, Armour);
            m.Box(-1.0, 0, 3.2, 1.0, 1.2, 4.0, Gun);
            m.Box(-0.8, 0.25, 3.95, -0.1, 0.95, 4.05, Black);
            m.Box(0.1, 0.25, 3.95, 0.8, 0.95, 4.05, Black);
            m.Box(-1.25, 1.5, -3.0, 1.25, 1.56, 2.0, Blue);
        }),
        new("Sensor mast", "Systems", "skin", null, "", "A mast with a dish and a lamp.", m =>
        {
            m.Frustum(0, 0.3, 0.3, 0.2, 8, Armour);
            m.Frustum(0.3, 0.09, 3.0, 0.07, 8, Gun);
            m.Frustum(2.6, 0.1, 3.0, 0.9, 12, Pale, cap0: true, cap1: true);
            m.Box(-0.08, 3.0, -0.08, 0.08, 3.2, 0.08, Amber);
        }),
        new("Radiator", "Systems", "skin", null, "", "A heat radiator fin, 4 m out from the skin.", m =>
        {
            m.Box(-0.3, 0, -2.2, 0.3, 0.3, 2.2, Armour);
            m.Box(-0.05, 0.3, -2.0, 0.05, 4.2, 2.0, Pale);
            for (var z = -1.6; z < 2; z += 0.8)
                m.Box(-0.09, 0.3, z, 0.09, 4.2, z + 0.08, Gun);
        }),
        new("Landing leg", "Systems", "skin", null, "", "A landing leg pod for the belly, foot down.", m =>
        {
            m.Box(-0.8, 0, -1.3, 0.8, 0.9, 1.3, Armour);
            m.Frustum(0.9, 0.22, 3.0, 0.18, 8, Gun);
            m.Frustum(3.0, 0.85, 3.25, 0.85, 12, Armour);
            m.Box(-0.82, 0.3, -1.32, 0.82, 0.42, 1.32, Amber);
        }),
        new("Airlock", "Sockets", "skin", "airlock", "any room", "A crew airlock. Opens into the room behind it.", m =>
        {
            m.Box(-1.35, 0, -0.95, 1.35, 0.22, -0.75, Armour);
            m.Box(-1.35, 0, 0.75, 1.35, 0.22, 0.95, Armour);
            m.Box(-1.35, 0, -0.75, -1.15, 0.22, 0.75, Armour);
            m.Box(1.15, 0, -0.75, 1.35, 0.22, 0.75, Armour);
            m.Box(-1.15, -0.7, -0.75, 1.15, -0.62, 0.75, Gun); // the outer door, set in the well
            m.Box(-1.2, -0.62, -0.05, 1.2, -0.58, 0.05, Amber);
            m.Box(0.9, 0.22, -0.9, 1.05, 0.3, -0.8, Glow);
            Well(m, 1.15, 0.75, 1.2);
        }),
        new("Window", "Sockets", "skin", "window", "any room", "A viewport: armoured glass in a frame, cut through the skin. Opens onto the room behind it.", m =>
        {
            WindowFrame(m);
            Glazing(m, 0.55, 0.9, 0.2, true);
        }),
        new("Docking collar", "Sockets", "skin", "dock", "any room", "A docking collar for another ship or a station's clamp.", m =>
        {
            m.Frustum(0, 1.45, 0.7, 1.35, 16, Armour, cap1: false);
            m.Frustum(0.7, 1.55, 0.85, 1.55, 16, Amber);
            m.Frustum(0.8, 1.05, 0.88, 1.05, 16, Gun);
            m.Frustum(-1.4, 1.05, 0, 1.05, 16, Gun, cap0: false, cap1: false);
        }),
        new("Cargo hatch", "Sockets", "skin", "cargo", "a cargo hold", "A 3 m cargo door. Opens into a cargo hold.", m =>
        {
            m.Box(-1.8, 0, -1.8, 1.8, 0.15, 1.8, Armour);
            m.Box(-1.5, -0.55, -1.5, -0.03, -0.48, 1.5, Gun);
            m.Box(0.03, -0.55, -1.5, 1.5, -0.48, 1.5, Gun);
            m.Box(-1.8, 0.15, 1.6, 1.8, 0.2, 1.8, Amber);
            m.Box(-1.8, 0.15, -1.8, 1.8, 0.2, -1.6, Amber);
            Well(m, 1.5, 1.5, 1.4);
        }),
        new("Boarding hatch", "Sockets", "skin", "shaft", "a room on the lowest deck", "A belly hatch over the boarding shaft's ladder (the boarding shaft kit).", m =>
        {
            m.Box(-0.65, 0, -0.65, 0.65, 0.1, -0.5, Armour); m.Box(-0.65, 0, 0.5, 0.65, 0.1, 0.65, Armour); m.Box(-0.65, 0, -0.5, -0.5, 0.1, 0.5, Armour); m.Box(0.5, 0, -0.5, 0.65, 0.1, 0.5, Armour);
            
            m.Box(-0.65, 0.1, 0.5, 0.65, 0.16, 0.65, Amber);
            Well(m, 0.5, 0.5, 2.2);
            // The boarding shaft's ladder: two rails and rungs up the shaft (its own frame's -Y is into the ship).
            m.Box(-0.3, -2.2, 0.38, -0.25, 0, 0.43, Amber);
            m.Box(0.25, -2.2, 0.38, 0.3, 0, 0.43, Amber);
            for (var y = -2.0; y < 0; y += 0.3)
                m.Box(-0.25, y, 0.39, 0.25, y + 0.04, 0.42, Gun);
        }),
        // Ways aboard from the ground or a pier (Karl, 2026-11-24). Each opens into any room.
        new("Boarding ramp", "Sockets", "skin", "ramp", "any room", "A ramp door hinged at its aft edge, shown lowered: walk aboard from the ground.", m =>
        {
            RampFrame(m, 1.0);
            RampStair(m, 0);
        }),
        // On a side wall a piece's X is up (Where turns these two the right way up on the port side too), Z along the ship.
        new("Gangway", "Sockets", "skin", "gangway", "any room", "A side door with a gangway run out 3 m: board from a pier or a station's platform.", m =>
        {
            m.Box(-1.25, 0, -0.8, -1.1, 0.12, 0.8, Armour); m.Box(1.1, 0, -0.8, 1.25, 0.12, 0.8, Armour);
            m.Box(-1.1, 0, -0.8, 1.1, 0.12, -0.6, Armour); m.Box(-1.1, 0, 0.6, 1.1, 0.12, 0.8, Armour);
            m.Box(-1.1, 0.05, -0.6, -1.0, 3.1, 0.6, Gun);         // the walkway, level with the sill
            m.Box(-1.0, 0.1, -0.62, -0.05, 3.1, -0.56, Amber);   // its rails
            m.Box(-1.0, 0.1, 0.56, -0.05, 3.1, 0.62, Amber);
            m.Box(-1.0, 2.95, -0.62, -0.9, 3.1, 0.62, Armour);
            Well(m, 1.1, 0.6, 0.8);
        }),
        new("Boarding ladder", "Sockets", "skin", "ladder", "any room", "A side door with a ladder folded down outside it, to climb aboard from the ground.", m =>
        {
            m.Box(-1.2, 0, -0.75, -1.05, 0.12, 0.75, Armour); m.Box(1.05, 0, -0.75, 1.2, 0.12, 0.75, Armour);
            m.Box(-1.05, 0, -0.75, 1.05, 0.12, -0.55, Armour); m.Box(-1.05, 0, 0.55, 1.05, 0.12, 0.75, Armour);
            // From the sill down 3 m, standing 15 cm off the skin.
            m.Box(-4.2, 0.12, -0.3, -1.05, 0.2, -0.25, Amber);
            m.Box(-4.2, 0.12, 0.25, -1.05, 0.2, 0.3, Amber);
            for (var x = -4.0; x < -1.1; x += 0.3)
                m.Box(x, 0.14, -0.25, x + 0.04, 0.18, 0.25, Gun);
            Well(m, 1.05, 0.55, 0.8);
        }),
        // The ammo socket, docking and boarding pieces (Karl, 2026-11-26): DesignDocking.cs.
        .. PieceDocking.Kinds,
        // Weapon mounts, the shell magazine and the ammo hoist (Karl, 2026-11-26): DesignWeaponMounts.cs.
        .. PieceWeapons.Kinds,
    ];

    /// <summary>The boarding ramp's frame on the skin and its well in to the room's floor (depth metres).</summary>
    internal static void RampFrame(DesignMesh m, double depth)
    {
            m.Box(-1.45, 0, -1.8, 1.45, 0.12, -1.6, Armour); m.Box(-1.45, 0, 1.6, 1.45, 0.12, 1.8, Armour);
            m.Box(-1.45, 0, -1.6, -1.25, 0.12, 1.6, Armour); m.Box(1.25, 0, -1.6, 1.45, 0.12, 1.6, Armour);
        Well(m, 1.25, 1.6, depth);
    }

    /// <summary>The ramp as a stair hanging from its frame's origin (Karl, 2026-11-24): placed with its origin at the room's floor, it
    /// starts flush with the floor and goes down out of the hatch to the ground.</summary>
    internal static void RampStair(DesignMesh m, double drop)
    {
            // The ramp as a stair hanging under the hull (Karl, 2026-11-24): it starts in the opening and goes down to the ground,
            // 14 level treads of 0.2 m rise and 0.25 m going (about 39 degrees). Its frame's +Y is out of the hull (down on the belly).
            // From the floor: as many more treads as the floor is in from the skin, so it still reaches the ground.
            var n = 14 + (int)Math.Ceiling(drop / 0.2);
            const double rise = 0.2, going = 0.25, top = 1.45;
            for (var i = 0; i < n; i++)
            {
                double y = (i + 1) * rise, z0 = top - i * going, z1 = z0 - going - 0.03;
                m.Box(-1.1, y - 0.05, z1, 1.1, y, z0, i % 2 == 0 ? Gun : Armour);            // the tread, its walking face toward the ship
                m.Box(-1.2, y - 0.05, z1, -1.1, y + 0.3, z0, Armour);                          // stringers under the treads' ends
                m.Box(1.1, y - 0.05, z1, 1.2, y + 0.3, z0, Armour);
                if (y > 1.0)
                {
                    m.Box(-1.2, y - 0.95, z1, -1.14, y - 0.88, z0, Amber);                     // hand rails 0.9 m over the treads, outside the hull
                    m.Box(1.14, y - 0.95, z1, 1.2, y - 0.88, z0, Amber);
                    if (i % 3 == 0) { m.Box(-1.2, y - 0.9, z0 - 0.06, -1.14, y - 0.05, z0, Amber); m.Box(1.14, y - 0.9, z0 - 0.06, 1.2, y - 0.05, z0, Amber); }
                }
            }
    }

    /// <summary>A socket's well: four walls from the skin inward (its frame's -Y), so the cut opening shows a frame, not the void.</summary>
    private static void Well(DesignMesh m, double hx, double hz, double depth)
    {
        m.Box(-hx - 0.06, -depth, -hz - 0.06, hx + 0.06, 0.02, -hz, Gun);
        m.Box(-hx - 0.06, -depth, hz, hx + 0.06, 0.02, hz + 0.06, Gun);
        m.Box(-hx - 0.06, -depth, -hz, -hx, 0.02, hz, Gun);
        m.Box(hx, -depth, -hz, hx + 0.06, 0.02, hz, Gun);
    }

    /// <summary>A window socket's armoured frame round its 1.1 x 1.8 m opening.</summary>
    public static void WindowFrame(DesignMesh m)
    {
        m.Box(-0.7, 0, -1.05, 0.7, 0.12, -0.9, Armour);
        m.Box(-0.7, 0, 0.9, 0.7, 0.12, 1.05, Armour);
        m.Box(-0.7, 0, -0.9, -0.55, 0.12, 0.9, Armour);
        m.Box(0.55, 0, -0.9, 0.7, 0.12, 0.9, Armour);
    }

    /// <summary>
    /// A window's glass (Karl, 2026-11-25): double glazed, an outer pane just under the frame (1-3 cm in) and an inner one
    /// <paramref name="depth"/> in (at least 5 cm, so the panes never share a plane), with a reveal lining the tunnel from the skin to just
    /// past the inner pane; single glazed, one pane at that depth in the same reveal. Panes stop 4 mm short of the reveal's faces.
    /// </summary>
    public static void Glazing(DesignMesh m, double hx, double hz, double depth, bool dbl)
    {
        depth = Math.Max(depth, 0.05);
        Well(m, hx, hz, depth + 0.06);
        double gx = hx - 0.004, gz = hz - 0.004;
        if (dbl)
            m.Box(-gx, -0.03, -gz, gx, -0.012, gz, Glow);
        m.Box(-gx, -depth - 0.018, -gz, gx, -depth, gz, Glow);
    }

    public static PieceKind? Find(string name) => Array.Find(All, k => k.Name == name);
}

/// <summary>What a room can be. A Hold is a cargo hold; a Shaft is a stair or ladder well that joins the decks it spans.</summary>
public sealed record RoomKind(string Name, string Colour, string Desc);

public static class RoomKinds
{
    public static readonly RoomKind[] All =
    [
        new("Bridge", "#3d8bd9", "Command: the captain and the helm."),
        new("Corridor", "#8a9097", "A passage. Joins rooms."),
        new("Cabin", "#7aa36b", "Crew bunks and lockers."),
        new("Mess", "#c4a35a", "Galley and mess table."),
        new("Med bay", "#e8f0f2", "Medical stations."),
        new("Engineering", "#d9733d", "Drive works, power and pumps."),
        new("Reactor", "#d94a3d", "The power plant."),
        new("Armoury", "#8d5a3d", "Weapons lockers and the magazines' feeds."),
        new("Workshop", "#a0805a", "Repair benches and spares."),
        new("Airlock room", "#d9a23d", "Suits and the inner door."),
        new("Hold", "#5a6f8d", "A cargo hold: crates, bulk and containers."),
        new("Shaft", "#6d6aa8", "A stair or ladder well between decks."),
    ];

    public static RoomKind? Find(string name) => Array.Find(All, k => k.Name == name);
}
