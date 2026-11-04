using System.Numerics;

namespace CallOfSlop;

/// <summary>Where a part's mirror image goes (<see cref="ConstructMirror.Of"/>).</summary>
public struct MirrorTwin
{
    public ConstructPartDef Def; // the same part, or its Left / Right partner
    public Vector3 Pos;
    public byte Rot;
    public bool Exact; // its shape really is the mirror image (false: the nearest turn; a part with handedness and no partner)
    public bool OnPlane; // the part sits across the plane: it is its own mirror image
}

/// <summary>
/// The Mirror Table's geometry (docs/sandbox/CONSTRUCTS.md "Build stations"). The plane is a part setting of the table (or
/// in creative with no table the player's own choice): an axis and an offset from the built-on construct's centre, the
/// seat's middle (else its root part's), so one table serves every construct in reach and saves with the base. A plane is
/// square to the seat's own axes, in the rest frame: <paramref name="ax"/> 0, 1, 2 is X, Y, Z. A reflection isn't a turn, so a
/// part's image is found by shape: of the 24 turns of the part, and of its Left / Right partner if it has one, the one whose
/// boxes and sockets land on the reflected boxes and sockets. Found once per part, turn and axis and kept.
/// </summary>
public static class ConstructMirror
{
    public const byte Off = 0, Side = 1, Front = 2, Up = 3, Axes = 3;
    public static readonly string[] AxisNames = ["off", "side to side", "front to back", "up and down"];

    /// <summary>Reads a Mirror Table's plane (its "mirrorAxis" and "mirrorOffset" settings).</summary>
    public static void ReadTable(Construct c, int part, out byte axis, out float offset)
    {
        axis = 0;
        offset = 0;
        var d = c.DefOf(part);
        for (var s = 0; s < d.Config.Length; s++)
            if (d.Config[s].Id == CfgKey.MirrorAxis)
                axis = (byte)ConstructConfig.At(c.Parts[part], d, s);
            else if (d.Config[s].Id == CfgKey.MirrorOffset)
                offset = ConstructConfig.At(c.Parts[part], d, s);
    }

    /// <summary>Sets a Mirror Table's plane. True when it changed.</summary>
    public static bool WriteTable(Construct c, int part, int axis, float offset)
    {
        var d = c.DefOf(part);
        var changed = false;
        for (var s = 0; s < d.Config.Length; s++)
            if (d.Config[s].Id == CfgKey.MirrorAxis)
                changed |= ConstructConfig.Set(ref c.Parts[part], d, s, axis);
            else if (d.Config[s].Id == CfgKey.MirrorOffset)
                changed |= ConstructConfig.Set(ref c.Parts[part], d, s, offset);
        return changed;
    }

    /// <summary>The plane on a construct, in its rest frame: <paramref name="ax"/> is the axis it is square to, <paramref name="w"/> where along it.</summary>
    public static void Plane(Construct c, byte axis, float offset, out int ax, out float w)
    {
        var anchor = c.DriveSeat >= 0 && c.DriveSeat < c.PartCount ? c.DriveSeat : c.Root >= 0 && c.Root < c.PartCount ? c.Root : 0;
        ref var p = ref c.Parts[anchor];
        var v = Rot24.Apply(p.Rot, axis == Side ? Vector3.UnitX : axis == Front ? Vector3.UnitZ : Vector3.UnitY);
        var abs = Vector3.Abs(v);
        ax = abs.X >= abs.Y && abs.X >= abs.Z ? 0 : abs.Y >= abs.Z ? 1 : 2;
        c.PartBounds(anchor, out var mn, out var mx);
        w = MathF.Round(((mn + mx) * 0.5f)[ax] * 200 + offset * MathF.Sign(v[ax]) * 200) / 200; // (half-centimetres: an image lands on the 1 cm grid)
    }

    public static Vector3 Reflect(Vector3 v, int ax, float w)
    {
        v[ax] = 2 * w - v[ax];
        return v;
    }

    private static Vector3 Flip(Vector3 v, int ax)
    {
        v[ax] = -v[ax];
        return v;
    }

    private static byte FlipDir(byte dir, int ax) => dir >> 1 == ax ? (byte)(dir ^ 1) : dir;

    private readonly struct Turn(ushort def, byte rot, bool exact)
    {
        public readonly ushort Def = def;
        public readonly byte Rot = rot;
        public readonly bool Exact = exact;
    }

    private static readonly Dictionary<long, Turn> Turns = [];
    private static readonly Dictionary<ushort, ushort> Partners = [];

    /// <summary>A part's Left / Right partner by name, or itself.</summary>
    private static ConstructPartDef Partner(ConstructPartDef d)
    {
        if (Partners.TryGetValue(d.Id, out var id))
            return ConstructParts.Def(id);
        var name = d.Name.Contains("Left") ? d.Name.Replace("Left", "Right") : d.Name.Contains("Right") ? d.Name.Replace("Right", "Left") : d.Name;
        var other = name == d.Name ? d : ConstructParts.Find(name) ?? d;
        Partners[d.Id] = other.Id;
        return other;
    }

    private static Vector3 Middle(ConstructPartDef d) => (d.BoundsMin + d.BoundsMax) * 0.5f;

    /// <summary>Do <paramref name="e"/>'s boxes and sockets at turn <paramref name="r"/> lie on the reflection of <paramref name="d"/>'s at <paramref name="rot"/>?</summary>
    private static bool Same(ConstructPartDef d, byte rot, ConstructPartDef e, byte r, int ax)
    {
        const float tol = 0.004f;
        if (d.Boxes.Length != e.Boxes.Length || d.Sockets.Length != e.Sockets.Length)
            return false;
        Vector3 cd = Middle(d), ce = Middle(e);
        if (Vector3.Distance(Rot24.ApplyHalf(rot, (d.BoundsMax - d.BoundsMin) * 0.5f), Rot24.ApplyHalf(r, (e.BoundsMax - e.BoundsMin) * 0.5f)) > tol)
            return false;
        for (var a = 0; a < d.Boxes.Length; a++)
        {
            var want = Flip(Rot24.Apply(rot, d.Boxes[a].Center - cd), ax);
            var half = Rot24.ApplyHalf(rot, d.Boxes[a].Half);
            var found = false;
            for (var b = 0; b < e.Boxes.Length && !found; b++)
                found = Vector3.DistanceSquared(Rot24.Apply(r, e.Boxes[b].Center - ce), want) < tol * tol && Vector3.DistanceSquared(Rot24.ApplyHalf(r, e.Boxes[b].Half), half) < tol * tol;
            if (!found)
                return false;
        }
        for (var a = 0; a < d.Sockets.Length; a++)
        {
            ref readonly var s = ref d.Sockets[a];
            var want = Flip(Rot24.Apply(rot, s.At - cd), ax);
            var dir = FlipDir(Rot24.ApplyDir(rot, s.Dir), ax);
            var found = false;
            for (var b = 0; b < e.Sockets.Length && !found; b++)
            {
                ref readonly var o = ref e.Sockets[b];
                found = o.Kind == s.Kind && o.Class == s.Class && Rot24.ApplyDir(r, o.Dir) == dir && Vector3.DistanceSquared(Rot24.Apply(r, o.At - ce), want) < tol * tol;
            }
            if (!found)
                return false;
        }
        return true;
    }

    private static Turn TurnOf(ConstructPartDef d, byte rot, int ax)
    {
        var key = d.Id | (long)rot << 16 | (long)ax << 24;
        if (Turns.TryGetValue(key, out var t))
            return t;
        var best = float.MinValue;
        var front = Flip(Rot24.Apply(rot, -Vector3.UnitZ), ax);
        var up = Flip(Rot24.Apply(rot, Vector3.UnitY), ax);
        var partner = Partner(d);
        for (var pass = 0; pass < (partner == d ? 1 : 2); pass++)
        {
            var e = pass == 0 ? partner : d; // (a partner first: a left part's image is the right one)
            for (byte r = 0; r < Rot24.Count; r++)
            {
                var exact = Same(d, rot, e, r, ax);
                // Of the turns that fit, the one facing the reflected way, upright as the reflection is.
                var score = (exact ? 100f : 0f) + (e == partner && partner != d ? 0.5f : 0f) + 1.1f * Vector3.Dot(Rot24.Apply(r, -Vector3.UnitZ), front) + Vector3.Dot(Rot24.Apply(r, Vector3.UnitY), up);
                if (score > best)
                {
                    best = score;
                    t = new Turn(e.Id, r, exact);
                }
            }
        }
        Turns[key] = t;
        return t;
    }

    /// <summary>Where the image of a part at (<paramref name="pos"/>, <paramref name="rot"/>) goes across the plane.</summary>
    public static MirrorTwin Of(ConstructPartDef d, Vector3 pos, byte rot, int ax, float w)
    {
        var t = TurnOf(d, rot, ax);
        var e = ConstructParts.Def(t.Def);
        var middle = pos + Rot24.Apply(rot, Middle(d));
        var image = Reflect(middle, ax, w);
        return new MirrorTwin
        {
            Def = e,
            Rot = t.Rot,
            Exact = t.Exact,
            Pos = ConstructWorld.Quantise(image - Rot24.Apply(t.Rot, Middle(e))),
            OnPlane = MathF.Abs(middle[ax] - w) < 0.02f,
        };
    }

    /// <summary>The part of a kind at a position (a mirrored removal's other side), or -1. The first that sits there; turn doesn't matter.</summary>
    public static int FindPart(Construct c, ConstructPartDef def, Vector3 pos)
    {
        var hits = Construct.PartScratch;
        var n = c.PartsInBox(pos - new Vector3(0.05f), pos + new Vector3(0.05f), hits);
        for (var k = 0; k < n; k++)
            if (c.Parts[hits[k]].Def == def.Id && Vector3.DistanceSquared(c.Parts[hits[k]].Pos, pos) < 0.0009f)
                return hits[k];
        // (a part's bounds needn't hold its origin)
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Def == def.Id && Vector3.DistanceSquared(c.Parts[i].Pos, pos) < 0.0009f)
                return i;
        return -1;
    }
}

/// <summary>Placing a part at a pose (the build aids': a mirror image, a cell of a row): the socket that gives exactly that pose.</summary>
public static class ConstructAid
{
    private const float Near = 0.021f;
    private static readonly int[] Hits = new int[Construct.MaxParts];

    /// <summary>How far from the eye loose pieces count as stock for an aid (metres).</summary>
    public const float StockReach = 60;

    private static bool IsStock(Construct b, byte player, ConstructPartDef def, Vector3 eye) =>
        b is { Loose: true, PartCount: 1, Driver: 255, CarriedBy: 0, GrabbedBy: 255 } && b.Parts[0].Def == def.Id && !b.Parts[0].Broken && (b.Owner == player || b.Owner == 255) && Vector3.DistanceSquared(b.Pos, eye) <= StockReach * StockReach;

    /// <summary>The first loose piece of a kind a player may use, in reach of <paramref name="eye"/> (not <paramref name="notBody"/>: what is being built on).</summary>
    public static Construct? LoosePiece(ConstructWorld w, byte player, ConstructPartDef def, Vector3 eye, ushort notBody)
    {
        var bodies = w.Bodies;
        for (var i = 0; i < bodies.Count; i++)
            if (bodies[i].Id != notBody && IsStock(bodies[i], player, def, eye))
                return bodies[i];
        return null;
    }

    /// <summary>How many there are (the HUD's count, when what it shows changes).</summary>
    public static int LooseCount(ConstructWorld w, byte player, ConstructPartDef def, Vector3 eye)
    {
        var n = 0;
        var bodies = w.Bodies;
        for (var i = 0; i < bodies.Count; i++)
            if (IsStock(bodies[i], player, def, eye))
                n++;
        return n;
    }

    /// <summary>
    /// Finds how <paramref name="def"/> goes on at (<paramref name="pos"/>, <paramref name="rot"/>): a free socket of the
    /// construct that meets one of its own sockets there, facing it, by the same rules as a click (<see cref="ConstructBuild.Place"/>).
    /// False: nothing to bolt to there, or something in the way. Parts on another freeform frame are not considered.
    /// </summary>
    public static bool FindFit(Construct c, ConstructPartDef def, Vector3 pos, byte rot, int frame, out ConstructGhost g)
    {
        g = default;
        var pad = new Vector3(Near + 0.03f);
        for (var k = 0; k < def.Sockets.Length; k++)
        {
            ref readonly var ds = ref def.Sockets[k];
            var at = pos + Rot24.Apply(rot, ds.At);
            var dir = Rot24.ApplyDir(rot, ds.Dir);
            var n = c.PartsInBox(at - pad, at + pad, Hits);
            for (var h = 0; h < n; h++)
            {
                var i = Hits[h];
                if (c.Parts[i].Broken || c.FreeformCount > 0 && c.FrameOf(c.Parts[i].Seg) != frame)
                    continue;
                var count = c.SocketCount(i);
                for (var s = 0; s < count; s++)
                {
                    c.SocketAt(i, s, out var sa, out var sd);
                    if (sd != (dir ^ 1) || Vector3.DistanceSquared(sa, at) > Near * Near || c.SocketUsed(i, s))
                        continue;
                    for (var spin = 0; spin < 4; spin++)
                        if (ConstructBuild.Place(c, i, s, def, k, spin, out var gh) == null && gh.Rot == rot)
                        {
                            gh.Variant = k * 4 + spin;
                            g = gh;
                            return true;
                        }
                }
            }
        }
        return false;
    }
}
