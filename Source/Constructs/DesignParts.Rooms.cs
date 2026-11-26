using System.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// Design parts, the rooms (DesignParts.cs has the map of the files): each room's floor, a slab for now (solid inside the bands). The
/// walkable rooms (step 4) replace it with the room's own shell as <see cref="Element.Boxes"/>.
/// </summary>
public static partial class DesignParts
{
    /// <summary>Rooms' floors: a slab each, welded to the band of its deck's body there.</summary>
    private static IEnumerable<int> AddRooms(Steps st)
    {
        var (d, els) = (st.D, st.Els);
        for (var ri = 0; ri < d.Rooms.Count; ri++)
        {
            var r = d.Rooms[ri];
            var bx = d.Box(r);
            var (x0, x1, y0, y1, z0, z1) = ((float)bx[0], (float)bx[1], (float)bx[2], (float)bx[3], (float)bx[4], (float)bx[5]);
            var area = d.Stacked ? (x1 - x0) * (y1 - y0) : (x1 - x0) * (z1 - z0);
            if (area <= 0.01f)
                continue;
            var e = new Element { Id = $"r{ri}", Kind = "floor", Name = $"{DesignScene.Name(r)} floor", Area = area, Kg = area * DeckKgPerM2 };
            (e.BoxMin, e.BoxMax) = d.Stacked ? (new Vector3(x0, y0, z1 - 0.1f), new Vector3(x1, y1, z1)) : (new Vector3(x0, y0 - 0.1f, z0), new Vector3(x1, y0, z1));
            var at = els.Count;
            els.Add(e);
            var hull = d.Decks[r.Deck].Hull != 0 ? d.Decks[r.Deck].Hull : d.Hulls.Count > 0 ? d.Hulls[0].Id : 0;
            if (st.BandAt(hull, (x0 + x1) < 0 ? 1 : 0, (e.BoxMin.Z + e.BoxMax.Z) * 0.5f) is var b and >= 0)
                Weld(els, b, at);
        }
        yield break;
    }

    /// <summary>This file's kinds (none yet).</summary>
    private static void RegisterRooms(KindTable k)
    {
    }
}
