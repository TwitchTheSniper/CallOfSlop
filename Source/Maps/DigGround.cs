using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Ground that can be dug (docs/sandbox/TERRAIN-DIGGING.md): a flat map's terrain (<see cref="TerrainEdits"/>) or a
/// planetoid (<see cref="PlanetEdits"/>, docs/planets/PLANETS.md "Digging"). The game's digging (blasts, the ground drill,
/// the network log) works through this, so it doesn't care which.
/// </summary>
public interface IDigGround
{
    /// <summary>Applies one op (logged when it changed something). Returns the columns it changed.</summary>
    int Apply(DigOp op);

    /// <summary>Every op applied since the map was last restored, in order (what joiners replay).</summary>
    List<DigOp> Ops { get; }

    /// <summary>What an op would take out or put in, by kind of ground, without changing anything. Returns the total (cubic metres).</summary>
    float Measure(in DigOp op, ReadOnlySpan<GroundKind> surfaceGives, float stoneDepth, Span<float> byKind);

    /// <summary>Would a fill put new ground inside the world box <paramref name="min"/>..<paramref name="max"/>?</summary>
    bool FillTouches(in DigOp op, Vector3 min, Vector3 max);

    /// <summary>How far a point is above the ground's surface as built (straight up there), for the fill's height limit.</summary>
    float AboveBuilt(Vector3 p);

    /// <summary>Every change undone and the log emptied.</summary>
    void Clear();

    /// <summary>
    /// Room made ahead for <paramref name="columns"/> more dug columns past those already made room for, at most <paramref name="most"/> of their span
    /// lists a call (a big crater's, over the ticks before its first band: Game.MegaDigTick), so the band that first digs them makes nothing. Returns
    /// how many are still to make: pass that back next time.
    /// </summary>
    int Reserve(int columns, int most);
}
