using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// Kit 2's guaranteed marks (docs/vehiclekit/VEHICLEKIT.md, "Identity and weak points"): after the dressing, every mark the style's signature asks of this vehicle
/// (<see cref="StyleSignature.Marks"/>) that isn't there gets one of its parts, as the Pipe Box pass stands its box. Each mark rolls on its own fork of the seed, so
/// nothing else of the seed moves, and it tries a ranked list of spots: up-facing sockets on a room's roof, the cab, the hood, a roof rack, the bed, the porch; then a
/// short post of frame beam on the vehicle's rearmost free top socket, with the mark on its tip; then sideways on the headboard and the flanks. Nothing it stands up
/// rises into a turret's sweep (a gun turning would pass through it, and its rounds hit it), and a post goes up only where the room over it was tested first, as
/// nothing placed is taken off again. A mark with nowhere to go is left missing, and the identity gate re-plans as before.
/// </summary>
/// <remarks>
/// No bracket on a turret ring: the ring's fixed side sockets sit inside the sweep of the gunner's arm and seat, so a mast there would be in the way of both.
/// </remarks>
public sealed partial class Vehicle
{
    /// <summary>What the marks pass placed, "label: part", for the logs and the reports.</summary>
    public List<string> MarksPlaced { get; } = new();

    private string _markHost = "";

    /// <summary>The slots a mark's part is looked for in, in this order (weighted by how often each slot lists it); a part in none is used as named.</summary>
    private static readonly string[] MarkSlots = ["mast", "spike", "gear", "trophy"];

    /// <summary>Every missing mark of the style's signature, placed where it can be (kit 2 only: the grammar and the dynamic seeds of kit-1 shapes call it).</summary>
    public Vehicle GuaranteeMarks()
    {
        foreach (var m in VehicleIdentity.Lacking(this))
        {
            var d = Dice.Fork("mark " + m.Label);
            var parts = MarkParts(m, d);
            if (parts.Count == 0)
                continue;
            for (var have = VehicleIdentity.Count(this, m.Pick); have < m.Min; have++)
            {
                VehicleGenJob.Yield();
                if (PlaceMark(parts, d) is not { } placed)
                    break; // (nowhere: the identity gate plans again)
                MarksPlaced.Add($"{m.Label}: a {placed.Name} on the {_markHost}");
            }
        }
        return this;
    }

    /// <summary>The parts a mark may be met with, in the seed's order: the style's own from its dressing slots first (as often as it lists them), then the rest named.</summary>
    private List<string> MarkParts(StyleMark m, Dice d)
    {
        var weighted = new List<string>();
        foreach (var slot in MarkSlots)
            if (Style.Parts.TryGetValue(slot, out var list))
                weighted.AddRange(list.Where(m.Pick.Names.Contains));
        var order = new List<string>();
        foreach (var n in d.Shuffled(weighted).Concat(m.Pick.Names))
            if (!order.Contains(n) && ConstructParts.Find(n) is { } def && Array.Exists(def.Sockets, s => s.Name == "base"))
                order.Add(n);
        return order;
    }

    /// <summary>One of <paramref name="parts"/> on the best spot that takes one; null when none does.</summary>
    private VPart? PlaceMark(List<string> parts, Dice d)
    {
        IEnumerable<VPart> Tag(string t) => Panels.GetValueOrDefault(t) ?? [];
        var up = new[]
        {
            Rooms.SelectMany(r => r.Roof), Tag("cabshell"), Tag("hood"), Tag("rack"), Tag("bed"), Tag("porch"),
        };
        foreach (var on in up)
            if (MarkOn(parts, on, Vector3.UnitY, d) is { } p)
                return p;
        if (MarkOnPost(parts) is { } post)
            return post;
        var sides = Tag("sides").Concat(Panels.Where(kv => kv.Key.EndsWith("walls", StringComparison.Ordinal)).SelectMany(kv => kv.Value)).Concat(Tag("cab")).Concat(Tag("cabshell")).Concat(Tag("hoodsides")).ToList();
        foreach (var (on, dir) in new (IEnumerable<VPart>, Vector3)[] { (Tag("headboard"), -Vector3.UnitZ), (sides.Where(p => CentreOf(p).X < -0.3f), -Vector3.UnitX), (sides.Where(p => CentreOf(p).X > 0.3f), Vector3.UnitX) })
            if (MarkOn(parts, on, dir, d) is { } p)
                return p;
        return null;
    }

    /// <summary>A mark on a free socket of one of <paramref name="on"/> facing <paramref name="dir"/>, trying each part on each such socket.</summary>
    private VPart? MarkOn(List<string> parts, IEnumerable<VPart> on, Vector3 dir, Dice d)
    {
        foreach (var p in d.Shuffled(on))
        {
            if (p.Seg != 0 && !C.Rigid(p.Seg))
                continue; // (not on a turret's leaf or a joint)
            foreach (var socket in d.Shuffled(FreeSockets(p, dir)))
            {
                C.SocketAt(p.Index, Array.FindIndex(p.Def.Sockets, k => k.Name == socket), out var at, out _);
                var up = dir.Y > 0.9f;
                if (TryMark(parts.Where(n => ClearOfGuns(at, 0, Def(n), !up) && (!up || OpenSky(at, 0, Def(n)))).ToList(), p, socket) is { } placed)
                    return placed;
            }
        }
        return null;
    }

    /// <summary>Every free socket of <paramref name="p"/> facing <paramref name="dir"/>.</summary>
    private List<string> FreeSockets(VPart p, Vector3 dir)
    {
        var list = new List<string>();
        for (var s = 0; s < p.Def.Sockets.Length; s++)
        {
            if (C.SocketUsed(p.Index, s))
                continue;
            C.SocketAt(p.Index, s, out _, out var face);
            if (Vector3.Dot(ConstructParts.DirVector(face), dir) > 0.9f)
                list.Add(p.Def.Sockets[s].Name);
        }
        return list;
    }

    private VPart? TryMark(List<string> parts, VPart on, string socket)
    {
        foreach (var part in parts)
            try
            {
                var placed = Onto(on, socket, part, "base", 0, clear: true);
                _markHost = on.Name;
                return placed;
            }
            catch (VehicleKitException)
            {
                // (no room for that one there, or a face on one already there: the next part, then the next socket)
            }
        return null;
    }

    /// <summary>
    /// A short post of the frame's beam standing on the rearmost free top socket of the body (a tail rail, a porch, a bed's edge), the mark on its tip: where a
    /// War Bus whose roof went to turrets still has room. The post takes the frame's paint.
    /// </summary>
    private VPart? MarkOnPost(List<string> parts)
    {
        var hosts = new List<(VPart P, string S, float Z)>();
        foreach (var p in Parts)
        {
            if (p.Seg != 0 || p.Def.Role is not (PartRole.Frame or PartRole.Panel) || p.Def.Slots.Length > 0)
                continue;
            for (var s = 0; s < p.Def.Sockets.Length; s++)
            {
                if (C.SocketUsed(p.Index, s) || p.Def.Sockets[s].Name.StartsWith("up", StringComparison.Ordinal) && HullLoft.ShapeOf(p.Def.Name) is { Floor: true })
                    continue; // (not an upper floor's: a post there stands where players walk, phase 5)
                C.SocketAt(p.Index, s, out var at, out var face);
                if (ConstructParts.DirVector(face).Y > 0.9f && !Rooms.Any(r => r.Kept(FromRest(at).X, FromRest(at).Z, 0.3f, 0.3f)))
                    hosts.Add((p, p.Def.Sockets[s].Name, FromRest(at).Z)); // (never on floor kept clear: the way onto a stair)
            }
        }
        var paint = FramePaint();
        var tries = 0;
        foreach (var (p, s, _) in hosts.OrderByDescending(h => h.Z))
        {
            VehicleGenJob.Yield();
            C.SocketAt(p.Index, Array.FindIndex(p.Def.Sockets, k => k.Name == s), out var at, out _);
            var fits = parts.Where(n => OpenAbove(at, Def(n)) && OpenSky(at, PostLength, Def(n)) && ClearOfGuns(at, PostLength, Def(n))).ToList();
            if (fits.Count == 0)
                continue; // (something over it: a post would stand there bare)
            if (++tries > 8)
                break;
            VPart post;
            try
            {
                post = Onto(p, s, $"{Beam} 0.5 m", "-z"); // (PostLength)
            }
            catch (VehicleKitException)
            {
                continue; // (no room for a post there)
            }
            if (paint != null)
                Paint(post, paint);
            if (TryMark(fits, post, "+z") is { } tip)
            {
                _markHost = $"post on the {p.Name}";
                return tip;
            }
            Console.WriteLine($"[vehicles] {Name}: a mark's post at {Show(FromRest(at))} took no mark"); // (the room test above said it would: a bare post, which a check would show)
            return null;
        }
        return null;
    }

    private const float PostLength = 0.5f;

    /// <summary>
    /// Whether a post standing on the socket at <paramref name="at"/> (rest coordinates, facing up) and <paramref name="mark"/> on its tip would meet nothing: tested
    /// against every part's solid boxes before anything is placed, as nothing placed is taken off again.
    /// </summary>
    private bool OpenAbove(Vector3 at, ConstructPartDef mark)
    {
        var baseAt = Array.Find(mark.Sockets, k => k.Name == "base")!.At;
        return Empty(at + new Vector3(-0.06f, 0.01f, -0.06f), at + new Vector3(0.06f, PostLength, 0.06f))
            && Empty(at + new Vector3(0, PostLength, 0) + mark.BoundsMin - baseAt + new Vector3(0.01f), at + new Vector3(0, PostLength, 0) + mark.BoundsMax - baseAt - new Vector3(0.01f));
    }

    /// <summary>
    /// Whether nothing of the vehicle stands over <paramref name="mark"/> standing <paramref name="lift"/> over the up-facing socket at <paramref name="at"/>: open sky
    /// over its footprint, so it is seen from above and over the roofline round it, not hidden under a rack, a deck or a room's overhang (the identity score's weak point
    /// wants a mast seen from two sides and hit from two bearings).
    /// </summary>
    private bool OpenSky(Vector3 at, float lift, ConstructPartDef mark)
    {
        var baseAt = Array.Find(mark.Sockets, k => k.Name == "base")!.At;
        var lo = at + new Vector3(0, lift, 0) + mark.BoundsMin - baseAt + new Vector3(0.01f);
        var hi = at + new Vector3(0, lift, 0) + mark.BoundsMax - baseAt - new Vector3(0.01f);
        return Empty(new Vector3(lo.X, hi.Y + 0.02f, lo.Z), new Vector3(hi.X, hi.Y + 30, hi.Z));
    }

    /// <summary>Whether no part's solid box meets the box from <paramref name="lo"/> to <paramref name="hi"/> (rest coordinates).</summary>
    private bool Empty(Vector3 lo, Vector3 hi)
    {
        var (c, h) = ((lo + hi) * 0.5f, (hi - lo) * 0.5f);
        for (var i = 0; i < C.PartCount; i++)
            for (var k = 0; k < C.DefOf(i).SolidBoxes; k++)
            {
                C.BoxOf(i, k, out var oc, out var oh);
                var gap = Vector3.Abs(c - oc) - h - oh;
                if (gap.X < 0 && gap.Y < 0 && gap.Z < 0)
                    return false;
            }
        return true;
    }

    /// <summary>
    /// Whether <paramref name="mark"/> standing <paramref name="lift"/> over the up-facing socket at <paramref name="at"/> (rest coordinates; <paramref name="sideways"/>:
    /// lying out from a side socket) stays out of every turret's sweep: lower than the ring's top, or further out than the longest gun reaches (or the gunner's arm, 1.2 m), with a margin.
    /// </summary>
    private bool ClearOfGuns(Vector3 at, float lift, ConstructPartDef mark, bool sideways = false)
    {
        if (Turrets.Count == 0)
            return true;
        var tall = mark.BoundsMax.Y - Array.Find(mark.Sockets, k => k.Name == "base")!.At.Y;
        var top = at.Y + lift + (sideways ? MathF.Max(mark.BoundsMax.X - mark.BoundsMin.X, mark.BoundsMax.Z - mark.BoundsMin.Z) * 0.5f : tall);
        var reach = MathF.Max(1.2f, Turrets.Max(t => GunReach(t.Weapon))) + 0.4f + MathF.Max(mark.BoundsMax.X - mark.BoundsMin.X, mark.BoundsMax.Z - mark.BoundsMin.Z) * 0.5f;
        foreach (var t in Parts)
        {
            if (t.Name != "Turntable")
                continue;
            C.PartBounds(t.Index, out var lo, out var hi);
            var ring = (lo + hi) * 0.5f;
            if (top > hi.Y - 0.05f && new Vector2(ring.X - at.X, ring.Z - at.Z).Length() < reach)
                return false;
        }
        return true;
    }

    /// <summary>The frame's colour as Trim painted it (its beams share one), or null.</summary>
    private string? FramePaint()
    {
        foreach (var p in Parts)
            if (p.Name.StartsWith(Beam, StringComparison.Ordinal) && ConstructPaint.IsSet(C.Parts[p.Index].Paint))
                return ConstructPaint.ToHex(C.Parts[p.Index].Paint);
        return null;
    }
}
