using System.Numerics;

namespace CallOfSlop;

/// <summary>What scoops ask of the ground (ConstructScoops.cs): the same dig ops, log and rules as everything else that changes it.</summary>
public sealed partial class Game : IScoopGround
{
    bool IScoopGround.Live => TerrainDigLive;

    bool IScoopGround.LogRoom => DigGround is { } g && g.Ops.Count < DiggingTable.Current.ScoopLogCap;

    float IScoopGround.Measure(in DigOp op, Span<float> byKind)
    {
        var table = DiggingTable.Current;
        return DigGround is { } g ? g.Measure(in op, table.Ground, table.StoneDepth, byKind) : 0;
    }

    int IScoopGround.Dig(DigOp op) => Dig(op);

    bool IScoopGround.MayFill(in DigOp op) =>
        DigGround is { } g && g.AboveBuilt(op.At) + op.Radius <= DiggingTable.Current.MaxAboveSurface && FillBuries(g, in op) == null;
}
