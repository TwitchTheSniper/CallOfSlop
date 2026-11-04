using System.Numerics;

namespace CallOfSlop;

/// <summary>Ropes and springs on the server (docs/sandbox/CONSTRUCTS.md, "Ropes and springs"): tying one, taking one off and setting one, each checked again here.</summary>
public sealed partial class ConstructEdits
{
    public const float ConnectorTake = ConstructBuild.Reach + 2f; // how far off an end or the line may be for the tool to act on it

    /// <summary>The material a rope or spring is made of in survival (bulk, from what the player carries, then a beacon's network).</summary>
    public const string ConnectorMaterial = "metal";

    /// <summary>The attach tool ties a new one. <paramref name="size"/> 255: by the parts. Null, or why not.</summary>
    public string? ConnectorAdd(ConstructWorld world, IWorldQuery? q, byte player, Vector3 eye, ConnectorKind kind, byte size, in ConnectorEnd a, in ConnectorEnd b)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Welder)
            return "take out the attach tool";
        if (kind >= ConnectorKind.COUNT)
            return "no such kind";
        if (world.CheckConnector(a, b) is { } why)
            return why;
        if ((EndContested(world, a, player) ?? EndContested(world, b, player)) is { } defended)
            return defended;
        world.EndPoint(a, out var pa);
        world.EndPoint(b, out var pb);
        if (MathF.Min(Vector3.Distance(eye, pa), Vector3.Distance(eye, pb)) > ConnectorTake)
            return "too far away";
        var s = size == 255 ? world.AutoConnectorSize(a, b) : Math.Min(size, (byte)(ConstructWorld.ConnectorSizes.Length - 1));
        if (ConnectorPay(world, player, eye, ConstructWorld.ConnectorCost(kind, s, Vector3.Distance(pa, pb))) is { } poor)
            return poor;
        if (world.AddConnector(q, kind, s, a, b) < 0)
            return "no room for another";
        Done.Add($"player {player}: tied a {ConstructWorld.ConnectorName(kind)} between construct {a.Body} and {b.Body}");
        return null;
    }

    private string? EndContested(ConstructWorld world, in ConnectorEnd e, byte player) => world.Get(e.Body) is { } c ? Contested(c, player) : null;

    /// <summary>Survival: takes <paramref name="cost"/> units of metal from what the player carries, then their network; all or nothing.</summary>
    private string? ConnectorPay(ConstructWorld world, byte player, Vector3 eye, int cost)
    {
        if (InfiniteParts || Inventory == null || cost <= 0)
            return null;
        var m = RecyclingTable.Current.IdOf(ConnectorMaterial);
        if (m == 0)
            return null;
        var inv = Inventory.Of(player);
        int carried = Math.Min(inv.Materials[m], cost);
        var rest = cost - carried;
        var net = rest > 0 ? NetworkOf(world, player, eye) : 0;
        var got = net != 0 ? world.Beacons.TakeBulk(world, this, player, net, m, rest) : 0;
        if (got < rest)
        {
            if (got > 0)
                world.Beacons.PutBulk(world, this, player, net, m, got);
            return $"that takes {cost} {ConnectorMaterial}: you reach {carried + got}";
        }
        inv.TakeMaterial(m, carried);
        return null;
    }

    /// <summary>RMB with the attach tool at the line, or the remover on it. Nothing comes back: a rope is used up. Null, or why not.</summary>
    public string? ConnectorDelete(ConstructWorld world, byte player, Vector3 eye, ushort id)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool is not (ConstructTool.Welder or ConstructTool.Remover))
            return "take out the attach tool or the remover";
        var k = world.ConnectorIndex(id);
        if (k < 0)
            return "that is gone";
        ref var cn = ref world.Connectors[k];
        if ((EndContested(world, cn.A, player) ?? EndContested(world, cn.B, player)) is { } defended)
            return defended;
        if (world.ConnectorReach(cn, eye) > ConnectorTake)
            return "too far away";
        Done.Add($"player {player}: took off {ConstructWorld.ConnectorName(cn.Kind)} {id}");
        world.RemoveConnector(k);
        return null;
    }

    /// <summary>
    /// The configure tool sets a connector. Survival: a rope can't be set stronger than its size, and lengthening one pays for the
    /// new metres (creative: up to 20 times its size). Null, or why not.
    /// </summary>
    public string? ConnectorSet(ConstructWorld world, byte player, Vector3 eye, ushort id, float length, float rating, float stiffness, float damping)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Configure)
            return "take out the configure tool";
        if (!float.IsFinite(length + rating + stiffness + damping))
            return "bad value";
        var k = world.ConnectorIndex(id);
        if (k < 0)
            return "that is gone";
        ref var cn = ref world.Connectors[k];
        if ((EndContested(world, cn.A, player) ?? EndContested(world, cn.B, player)) is { } defended)
            return defended;
        if (world.ConnectorReach(cn, eye) > ConstructConfig.Reach + 2f)
            return "too far away";
        var s = ConstructWorld.ConnectorSizes[cn.Size];
        var full = s.Tonnes * 1000f * ConstructPhysics.GravityAccel;
        var most = InfiniteParts ? 20f : 1f;
        length = Math.Clamp(length, ConstructWorld.ConnectorMinLength, ConstructWorld.ConnectorMaxLength + 1f);
        if (cn.Kind == ConnectorKind.Rope && length > cn.Length && ConnectorPay(world, player, eye, ConstructWorld.ConnectorCost(cn.Kind, cn.Size, length - cn.Length)) is { } poor)
            return poor;
        cn.Length = length;
        cn.Rating = Math.Clamp(rating, full * 0.02f, full * most);
        cn.Stiffness = Math.Clamp(stiffness, 0, s.Stiffness * 20f);
        cn.Damping = Math.Clamp(damping, 0, s.Stiffness * 20f);
        world.TouchConnector(k);
        world.Get(cn.A.Body)?.Wake();
        world.Get(cn.B.Body)?.Wake();
        return null;
    }
}
