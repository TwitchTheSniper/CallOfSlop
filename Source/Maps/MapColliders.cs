using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Hidden colliders: boxes that collide like any <see cref="Block"/> (players, bullets, grenades, bot sight and the nav
/// graph) but are never drawn. A map gives a round or curved cosmetic <c>Mesh</c> (a disc deck, a dome, a tank) its
/// collision with a few of these just inside the mesh, so the look comes from the mesh and the boxes cost no draw calls
/// (map boxes are drawn one by one; see Program.DrawArena).
/// </summary>
public sealed partial class Map
{
    private readonly HashSet<int> _hiddenBoxes = new();

    /// <summary>Is <see cref="Solid"/>[i] a hidden collider (collision only, not drawn)?</summary>
    public bool IsHiddenBox(int i) => _hiddenBoxes.Count > 0 && _hiddenBoxes.Contains(i);

    /// <summary>How many hidden colliders the map has.</summary>
    public int HiddenBoxCount => _hiddenBoxes.Count;

    /// <summary>A box from one corner to the other that collides but is never drawn (put a cosmetic mesh over it).</summary>
    [MapOp]
    public void Collider(float x0, float y0, float z0, float x1, float y1, float z1)
    {
        using var __ = Op([x0, y0, z0, x1, y1, z1]);
        var before = Solid.Count;
        Block(x0, y0, z0, x1, y1, z1, new Vector3(0.5f, 0.5f, 0.5f));
        for (var i = before; i < Solid.Count; i++)
            _hiddenBoxes.Add(i);
    }
}
