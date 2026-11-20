using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Where the linking tool lays a cable or pipe against a wall, a floor or a frame (Karl, 2026-11-18; power-and-links.md,
/// "Links in the walls"): inside it (hidden, only the devices at its ends show), on the side the player stands on (as links always
/// were), or on its far side. It is a setting of the tool only: a link is its nodes, and whether a stretch of it is in a wall is
/// what its nodes' places say (<see cref="ConstructRoutes.SegmentBuried"/>), so nothing new goes on the wire or into a save.
/// </summary>
public enum LinkLay : byte
{
    Near, // on this side of it (the old way, and every caller's that doesn't ask)
    Wall, // in the middle of the wall, floor or frame: hidden
    Far, // on its far side, through it at each end like a gland
    COUNT,
}

public static partial class ConstructRoutes
{
    /// <summary>The tool's lay for the next cable or pipe (this PC's setting; kept from link to link).</summary>
    public static LinkLay Lay = LinkLay.Wall;

    public static string LayName(LinkLay l) => l switch
    {
        LinkLay.Wall => "in the wall",
        LinkLay.Near => "on this side",
        _ => "on the far side",
    };

    /// <summary>A link that may run inside walls, floors and frames: a cable or a pipe. A chute, feed, shaft or conveyor is too big, and runs over them.</summary>
    public static bool Buryable(RouteType t) => t is RouteType.Data or RouteType.Power or RouteType.Pipe;

    /// <summary>A link the tool fixes to structure only (<see cref="IsFixing"/>): not a shaft (straight, port to port) or a conveyor (on trestles and machines).</summary>
    public static bool Tidies(RouteType t) => t is not (RouteType.Shaft or RouteType.Conveyor);

    /// <summary>
    /// A part a link's node may be fixed to with the tool: structure and machinery (a frame, a panel, an engine, a tank; a link may run
    /// along a machine with free routing). Not a fitting: a seat, a door, a switch, lamp or sensor, a crate, a gun or its ammo, a wheel
    /// (Karl, 2026-11-18: no cable over the seat).
    /// </summary>
    public static bool IsFixing(ConstructPartDef d) => d.TrayLanes > 0 || d.Role is not (PartRole.Seat or PartRole.Door or PartRole.Logic or PartRole.Storage
        or PartRole.Gun or PartRole.GunPart or PartRole.AmmoBox or PartRole.Hose or PartRole.Wheel or PartRole.Rotor or PartRole.Leg or PartRole.Hinge or PartRole.Joint);

    /// <summary>A box a cable or pipe may lie inside: a solid, opaque box of a frame or a panel (a glass pane shows what is in it).</summary>
    public static bool WallBox(ConstructPartDef d, int b) => d.Role is PartRole.Frame or PartRole.Panel && b < ConstructBuild.PlaceSolids(d) && !d.Boxes[b].IsGlass; // (a flush pod's body isn't a wall)

    /// <summary>A wall no thicker than this along its normal has a far side a link can lie on (as a conduit's gland: <see cref="RouteSearch.PierceMax"/>).</summary>
    public static float FarMax => RouteSearch.PierceMax;

    /// <summary>
    /// The surface point and normal a node clicked at <paramref name="p"/> (rest frame, normal <paramref name="n"/>) on
    /// <paramref name="part"/> snaps from for <paramref name="lay"/> (<see cref="Snap"/> puts the node one radius off them): for
    /// <see cref="LinkLay.Wall"/> the wall's middle, for <see cref="LinkLay.Far"/> its far face; on anything but a frame or a panel, its surface. Null, or why the lay can't go there.
    /// </summary>
    public static string? LaySurface(Construct c, RouteType type, int part, Vector3 p, Vector3 n, LinkLay lay, out Vector3 surface, out Vector3 normal)
    {
        surface = p;
        normal = n;
        if (lay == LinkLay.Near || !Buryable(type) || part < 0 || part >= c.PartCount)
            return null;
        var d = c.DefOf(part);
        if (d.Role is not (PartRole.Frame or PartRole.Panel))
            return null; // (a machine, a tank: on its surface, as near; whether a node may sit there at all is IsFixing's)
        var nn = n.LengthSquared() > 1e-6f ? Vector3.Normalize(n) : Vector3.UnitY;
        var inRest = c.Parts[part].Seg != 0;
        var depth = ExitAlongIn(c, part, p - nn * 0.002f, p - nn * (FarMax + 0.5f), inRest) + 0.002f;
        if (depth < 0.01f)
            return null; // (a sliver: as on the surface)
        var radius = Radius(type);
        if (lay == LinkLay.Wall)
        {
            surface = p - nn * (MathF.Min(depth * 0.5f, WallSink(radius)) + radius);
            return null;
        }
        if (depth > FarMax + 0.005f)
            return $"the {d.Name} is {depth * 100f:0} cm thick here: too thick to run behind (at most {FarMax * 100f:0} cm)";
        surface = p - nn * depth;
        normal = -nn;
        return null;
    }

    /// <summary>How deep a link in a wall lies under its face at most: the middle of a thin wall, just under the face of a thick one.</summary>
    public static float WallSink(float radius) => radius + 0.03f;

    /// <summary>Is segment <paramref name="seg"/> of link <paramref name="route"/> in a wall (its centreline inside frames and panels, and the devices at its ends)? Rebuilt with the links.</summary>
    public static bool SegmentBuried(Construct c, int route, int seg)
    {
        ref var r = ref c.Routes[route];
        return seg < r.Count ? c.RouteNodes[r.Start + seg].Buried : r.EndBuried;
    }

    private const float BuriedSlack = 0.003f; // a box grown this much holds a centreline (a cable laid against a wall, 0.25 radii off it, stays drawn)
    private const float BuriedGap = 0.03f; // a stretch out of every box this long in all is still in the wall (the seams between panels)

    /// <summary>Each cable's and pipe's segments: in a wall or not (<see cref="SegmentBuried"/>; <see cref="Rebuild"/>).</summary>
    private static void RebuildBuried(Construct c)
    {
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            var can = Buryable(r.Type);
            var a = Point(c, k, -1, out var sa);
            for (var i = 0; i <= r.Count; i++)
            {
                var b = Point(c, k, i, out var sb);
                var buried = can && sa == sb && Covered(c, a, b, sa, i == 0 && r.Parent == 255 ? r.From : -1, i == r.Count ? r.To : -1);
                if (i < r.Count)
                    c.RouteNodes[r.Start + i].Buried = buried;
                else
                    r.EndBuried = buried;
                a = b;
                sa = sb;
            }
        }
    }

    /// <summary>
    /// Is segment ab (in segment <paramref name="seg"/>'s frame) inside wall boxes (<see cref="WallBox"/>) and the end devices
    /// <paramref name="endA"/> and <paramref name="endB"/> all along, but for <see cref="BuriedGap"/>? Allocates nothing.
    /// </summary>
    public static bool Covered(Construct c, Vector3 a, Vector3 b, int seg, int endA, int endB)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 1e-4f)
            return false;
        Span<float> t0s = stackalloc float[48];
        Span<float> t1s = stackalloc float[48];
        var spans = 0;
        var wall = false;
        var grow = new Vector3(BuriedSlack);
        var hits = Construct.PartScratch;
        var tree = TreeFits(c, seg); // (as the path checks ask it)
        var count = tree ? c.PartsInBox(Vector3.Min(a, b) - grow, Vector3.Max(a, b) + grow, hits) : c.PartCount;
        for (var at = 0; at < count && spans < t0s.Length; at++)
        {
            var i = tree ? hits[at] : at;
            if (c.Parts[i].Seg != seg)
                continue;
            var def = c.DefOf(i);
            var end = i == endA || i == endB;
            if (!end && def.Role is not (PartRole.Frame or PartRole.Panel))
                continue;
            for (var k = 0; k < def.SolidBoxes && spans < t0s.Length; k++)
            {
                if (!end && !WallBox(def, k))
                    continue;
                c.BoxOf(i, k, out var centre, out var half);
                if (!SegmentBoxSpan(a - centre, d, half + grow, out var t0, out var t1))
                    continue;
                t0 = MathF.Max(t0, 0f);
                t1 = MathF.Min(t1, 1f);
                if (t1 <= t0)
                    continue;
                wall |= !end;
                t0s[spans] = t0;
                t1s[spans++] = t1;
            }
        }
        if (!wall)
            return false;
        // The length out of every box: sort the spans by their start (a few), then walk them.
        for (var i = 1; i < spans; i++)
            for (var j = i; j > 0 && t0s[j] < t0s[j - 1]; j--)
            {
                (t0s[j], t0s[j - 1]) = (t0s[j - 1], t0s[j]);
                (t1s[j], t1s[j - 1]) = (t1s[j - 1], t1s[j]);
            }
        var open = 0f;
        var reach = 0f;
        for (var i = 0; i < spans; i++)
        {
            if (t0s[i] > reach)
                open += t0s[i] - reach;
            reach = MathF.Max(reach, t1s[i]);
        }
        open += MathF.Max(0f, 1f - reach);
        return open * len <= BuriedGap;
    }

    /// <summary>The linking tool is out on this PC: a look ray finds a link in a wall just behind the surface it meets (the edit modes reach it), where a bullet doesn't.</summary>
    public static bool LookBuried;

    /// <summary>How far behind the surface a look ray meets a link in a wall (the thickest wall a link lies in the middle of, and more).</summary>
    public const float BuriedLookDepth = 0.35f;

    /// <summary>The first node from <paramref name="first"/> on fixed to a part that isn't structure (<see cref="IsFixing"/>), the ends' own parts excepted: why not, or null.</summary>
    public static string? Unfixed(Construct c, RouteType type, int from, int to, ReadOnlySpan<CRouteNode> nodes, int first)
    {
        if (!Tidies(type))
            return null;
        for (var i = first; i < nodes.Length; i++)
        {
            var h = nodes[i].Host;
            if (h < c.PartCount && h != from && h != to && !IsFixing(c.DefOf(h)))
                return FixingWhy(c, type, h);
        }
        return null;
    }

    public static string FixingWhy(Construct c, RouteType type, int host) =>
        $"a {Name(type)} is fixed to walls, floors and frames, not to the {c.DefOf(host).Name}";
}
