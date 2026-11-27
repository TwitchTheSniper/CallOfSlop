using System.Text.Json.Nodes;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// A recessed piece's lining (Karl, 2026-11-27): the walls of the well a piece sits back in (its Recess) can be clad, in the colour of
/// its hull's cladding (a tickbox, on by default) or in plain armour plate; and a window's reveal on in to its room (its sleeve) can be
/// lined with a functional wall panel, in its room's panel colour (or plain grey). Every piece kind takes it.
/// </summary>
public static class DesignRecess
{
    private const double Wall = 0.04; // a lining plate's thickness (m, in the piece's frame before its size)
    public const string Plain = "#8a9098"; // a reveal's wall panel when its room gives no colour

    public static JsonObject Write(Piece p, JsonObject o)
    {
        if (p.RecessClad) o["recessClad"] = true;
        if (!p.CladHull) o["cladHull"] = false;
        if (p.RevealClad) o["revealClad"] = true;
        return o;
    }

    public static Piece Read(JsonObject o, Piece p)
    {
        p.RecessClad = (bool?)o["recessClad"] ?? false;
        p.CladHull = (bool?)o["cladHull"] ?? true;
        p.RevealClad = (bool?)o["revealClad"] ?? false;
        return p;
    }

    /// <summary>The colour the recess's lining takes: its hull's cladding (the skin's paint) or armour plate.</summary>
    public static string RecessColour(ShipDesign d, Piece p)
    {
        if (!p.CladHull || d.Hull(p.Hull) is not { } h)
            return PieceKinds.Armour;
        var s = d.CladSource(h);
        return s.Skin;
    }

    /// <summary>The colour a window's reveal is lined in: its room's wall panels, or plain grey.</summary>
    public static string RevealColour(Room? r) => r is { Interior: not "Off" } ? r.PanelColour : Plain;

    /// <summary>What the lining's colours depend on, for the piece mesh's cache key.</summary>
    public static string Key(ShipDesign d, Piece p) =>
        !p.RecessClad && !p.RevealClad ? "" : "/rc" + RecessColour(d, p) + string.Join(',', p.RevealClad ? d.Rooms.Select(r => r.Id + RevealColour(r)) : []);

    /// <summary>
    /// The recess's four walls (into <paramref name="m"/>, whose frame is the piece's, set back by its Recess): from the piece back out to the
    /// skin, just outside its opening of half sizes <paramref name="hx"/>, <paramref name="hz"/> (the piece's frame units).
    /// </summary>
    public static void Draw(ShipDesign d, Piece p, DesignMesh m, double hx, double hz)
    {
        if (!p.RecessClad || p.Recess <= 0)
            return;
        var c = RecessColour(d, p);
        double t = Wall / p.Size, y1 = p.Recess / p.Size - 0.003; // (3 mm short of the skin: no face shared with the skin's cladding)
        m.Box(-hx - t, 0, -hz - t, hx + t, y1, -hz, c);
        m.Box(-hx - t, 0, hz, hx + t, y1, hz + t, c);
        m.Box(-hx - t, 0.002, -hz, -hx, y1, hz, c); // (2 mm offsets where the side walls meet the ends' plates)
        m.Box(hx, 0.002, -hz, hx + t, y1, hz, c);
    }
}
