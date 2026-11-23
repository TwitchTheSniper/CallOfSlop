namespace CallOfSlop.ShipDesigner;

/// <summary>
/// What can go on a hull: drives, guns and systems on the skin, and the sockets (airlock, dock, cargo hatch, boarding hatch) that join the
/// outside to a room inside. Each draws itself in its own frame: X across, Y out of the skin, Z towards the bow (on an end: towards the roof).
/// </summary>
public sealed record PieceKind(string Name, string Group, string Where, string? Socket, string Needs, string Desc, Action<DesignMesh> Draw)
{
    /// <summary>How far a socket may be from the room it opens into (the void between a room's wall and the skin, then a little).</summary>
    public double Reach => Socket switch { "dock" => 2.6, "cargo" => 2.6, _ => 2.2 };

    /// <summary>The hole a socket cuts through the skin, in its own frame (half sizes across and along; Round: a circle of radius Hx).</summary>
    public (double Hx, double Hz, bool Round)? Opening => Socket switch
    {
        "airlock" => (1.15, 0.75, false), "dock" => (1.05, 1.05, true), "cargo" => (1.5, 1.5, false), "shaft" => (0.5, 0.5, false), _ => null,
    };
}

public static class PieceKinds
{
    private const string Armour = "#2f3237", Gun = "#4b5058", Blue = "#3d8bd9", Amber = "#d9a23d", Glow = "#a9dcff", Pale = "#b8bdc4", Black = "#16181b";

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
    ];

    /// <summary>A socket's well: four walls from the skin inward (its frame's -Y), so the cut opening shows a frame, not the void.</summary>
    private static void Well(DesignMesh m, double hx, double hz, double depth)
    {
        m.Box(-hx - 0.06, -depth, -hz - 0.06, hx + 0.06, 0.02, -hz, Gun);
        m.Box(-hx - 0.06, -depth, hz, hx + 0.06, 0.02, hz + 0.06, Gun);
        m.Box(-hx - 0.06, -depth, -hz, -hx, 0.02, hz, Gun);
        m.Box(hx, -depth, -hz, hx + 0.06, 0.02, hz, Gun);
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
