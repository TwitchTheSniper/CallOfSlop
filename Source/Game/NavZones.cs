using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Bot navigation in gravity zones (GravityZones.cs) on an otherwise flat map. The flat grid build keeps only nodes where
/// up is +Y; every zone whose up is anything else (a wall or ceiling you fight on, a rock you walk round) gets nodes of its
/// own here: probes along the zone's local up over the zone's bounds (a grid across the up for a uniform zone, a Fibonacci
/// sphere round the centre for a point zone), plus the map's waypoints and spawns inside it. Zone nodes link to any node
/// within reach that a player could walk to along the local ground (<see cref="World.CanWalkField"/>), so a 45-degree
/// stair joins a deck to a wall. Low-gravity zones (up still +Y) need nothing: their nodes are the grid's.
/// </summary>
public sealed partial class NavGraph
{
    /// <summary>Zone node spacing: the grid's, but never more than 4 m (zones are small and their ground curves or ends).</summary>
    private static float ZoneSpacing(float s) => MathF.Min(s, 4f);

    private static IEnumerable<bool> AddZoneNodes(Map map, PhysicsWorld c, ZonedGravity zoned, List<Vector3> nodes, float s)
    {
        var sp = ZoneSpacing(s);
        var zoneStart = nodes.Count;
        for (var zi = 0; zi < zoned.Zones.Length; zi++)
        {
            var zone = zoned.Zones[zi];
            if (zone.StraightDown)
                continue;
            var k = 0;
            if (zone.Field is PointGravity pg)
            {
                // Round the centre, in from the zone's far edge (so every surface facing out gets a node: the rock's own, and
                // ledges on it).
                var reach = 0f;
                for (var i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) != 0 ? zone.Max.X : zone.Min.X, (i & 2) != 0 ? zone.Max.Y : zone.Min.Y, (i & 4) != 0 ? zone.Max.Z : zone.Min.Z);
                    reach = MathF.Max(reach, Vector3.Distance(corner, pg.Center));
                }
                foreach (var dir in Map.FibonacciSphere(reach, sp))
                {
                    ProbeZone(map, c, zoned, zi, nodes, zoneStart, pg.Center + dir * reach, dir, reach);
                    if (++k % 16 == 0)
                        yield return false;
                }
            }
            else if (zone.Field is UniformGravity u)
            {
                // A grid across the zone's up, probing from its top face down through it.
                GravityFrames.FrameOf(u, zone.Center, out var right, out var up, out var north);
                var half = (zone.Max - zone.Min) * 0.5f;
                var extR = MathF.Abs(right.X) * half.X + MathF.Abs(right.Y) * half.Y + MathF.Abs(right.Z) * half.Z;
                var extN = MathF.Abs(north.X) * half.X + MathF.Abs(north.Y) * half.Y + MathF.Abs(north.Z) * half.Z;
                var extU = MathF.Abs(up.X) * half.X + MathF.Abs(up.Y) * half.Y + MathF.Abs(up.Z) * half.Z;
                for (var a = -extR + sp / 2; a < extR; a += sp)
                for (var b = -extN + sp / 2; b < extN; b += sp)
                {
                    var start = zone.Center + up * (extU + 0.5f) + right * a + north * b;
                    ProbeZone(map, c, zoned, zi, nodes, zoneStart, start, up, extU * 2 + 1f);
                    if (++k % 16 == 0)
                        yield return false;
                }
            }
            // The map's own waypoints and spawns inside the zone.
            foreach (var w in map.Waypoints.Concat(map.Spawns))
            {
                if (zoned.ZoneAt(w) != zi)
                    continue;
                var up = zone.Field.UpAt(w);
                ProbeZone(map, c, zoned, zi, nodes, zoneStart, w + up * 1.0f, up, 3f, 0.3f);
            }
            yield return false;
        }
    }

    /// <summary>
    /// Nodes on every surface a ray from <paramref name="from"/> along -<paramref name="up"/> meets within <paramref name="depth"/>
    /// that lies in zone <paramref name="zi"/>, is walkable against the up there and has room for a standing body.
    /// </summary>
    private static void ProbeZone(Map map, PhysicsWorld c, ZonedGravity zoned, int zi, List<Vector3> nodes, int zoneStart, Vector3 from, Vector3 up, float depth, float apart = 0.8f)
    {
        var start = from;
        var left = depth;
        for (var hits = 0; hits < 6 && left > 0.1f; hits++)
        {
            if (!c.Raycast(start, -up, left, QueryFilter.Navigation, out var hit))
                return;
            var next = hit.Point - up * 0.25f; // carry on below this surface (through a slab to a floor under it)
            left -= hit.Distance + 0.25f;
            if (hit.Distance >= 0.001f)
            {
                var feet = hit.Point + up * 0.03f;
                if (zoned.ZoneAt(feet) == zi && MathF.Abs(feet.X) < map.HalfX - 0.5f && MathF.Abs(feet.Z) < map.HalfZ - 0.5f
                    && (map.Boundary == null || map.Boundary.Inside(feet.X, feet.Z, 0.6f)))
                {
                    var u = zoned.UpAt(feet);
                    if (Vector3.Dot(hit.Normal, u) >= World.MinWalkNormalY && World.BodyFits(feet, u) && !ZoneNodeNear(nodes, feet, zoneStart, apart))
                        nodes.Add(feet);
                }
            }
            start = next;
        }
    }

    private static bool ZoneNodeNear(List<Vector3> nodes, Vector3 p, int from, float within)
    {
        var w2 = within * within;
        for (var i = from; i < nodes.Count; i++)
            if (Vector3.DistanceSquared(nodes[i], p) < w2)
                return true;
        return false;
    }

    /// <summary>Links zone node <paramref name="i"/> to node <paramref name="j"/> both ways when a player can walk the ground between them. True when a walk was tried.</summary>
    private static bool TryLinkZone(NavGraph g, int i, int j, float linkR, List<(int From, int To, float Cost, byte Flags, Vector3 Ledge)> edges)
    {
        var a = g.Nodes[i];
        var b = g.Nodes[j];
        var len = Vector3.Distance(a, b);
        if (len > linkR || len < 0.05f)
            return false;
        if (!Clear(a, b))
            return false; // (the cheap straight look first: a wall between them, no walk)
        if (World.CanWalkField(a, b) && World.CanWalkField(b, a))
        {
            edges.Add((i, j, len, 0, Vector3.Zero));
            edges.Add((j, i, len, 0, Vector3.Zero));
        }
        return true;
    }
}
