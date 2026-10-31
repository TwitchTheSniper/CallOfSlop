using System.Numerics;

namespace CallOfSlop;

/// <summary>Where a held part goes when it is set down on a surface as a new construct (free placement): the construct's pose and the part's turn in it.</summary>
public struct FreePlacement
{
    public Vector3 Pos; // the new construct's origin (the part's origin) in the world
    public Quaternion Rot; // the construct's frame: up along the surface (or straight up on near-level ground), turned to the look
    public byte Turn; // the part's Rot24 turn in that frame (the rotate keys turn it: GhostTurn.cs)
    public bool Anchor; // pinned to the ground (creative's "anchor to ground"); a foundation anchors itself
}

/// <summary>Two constructs that a part touches (free sockets meeting, face to face, the two bodies square to each other): what welding them into one needs.</summary>
public struct JoinPlan
{
    public Construct Other;
    public int PartA, SockA, PartB, SockB;
    public byte Turn; // the other's frame in this one's: a Rot24 turn
    public Vector3 Offset; // ...and its origin in this one's frame
}

/// <summary>
/// Free placement (board item dev-sandbox-new-construct-nc01; docs/sandbox/CONSTRUCTS.md "New constructs"): a held part,
/// aimed at a surface rather than a construct's socket, is set down there as a new construct rooted at that part. The
/// rules are shared by the client (the ghost, green or red with the reason) and the server (which checks again when the
/// placement starts and when it is done): the pose follows the surface's normal (level on near-level ground, resting on
/// the highest ground under its footprint), the optional world grid snaps the spot and the yaw, the rotate keys turn
/// the part through the 24 turns relative to the camera (GhostTurn.cs), and the part's boxes must be clear of the map, players and other constructs. Nothing allocates.
/// </summary>
public static class ConstructPlace
{
    public const float GridStep = 0.5f; // the world grid (m); the yaw snaps to 90 degrees with it
    public const float LevelCos = 0.978f; // a surface within 12 degrees of level counts as level (the construct stands upright)
    public const int MaxPerPlayer = 12; // constructs a player may own by default (wrecks and loose parts don't count; the host's ConstructWorld.PerPlayerCap)
    public const string MaxPerPlayerText = "12";
    public const byte FlagGrid = 1, FlagAnchor = 2; // CActKind.PlaceNew's flags

    /// <summary>The 24 turns in the rotate key's order: upright ones first (the four yaws), then on their sides, then upside down.</summary>
    public static readonly byte[] Turns = BuildTurns();

    private static byte[] BuildTurns()
    {
        var t = new byte[Rot24.Count];
        for (var i = 0; i < t.Length; i++)
            t[i] = (byte)i;
        return t.OrderByDescending(r => MathF.Round(Rot24.Apply(r, Vector3.UnitY).Y)).ThenBy(r => r).ToArray();
    }

    /// <summary>The turn after <paramref name="turn"/> in the rotate key's order.</summary>
    public static byte NextTurn(byte turn) => Turns[(Array.IndexOf(Turns, turn) + 1) % Turns.Length];

    /// <summary>The yaw a new construct's frame takes: the look's, squared to 90 degrees on the grid.</summary>
    public static float FrameYaw(float yaw, bool grid) => grid ? MathF.Round(yaw / (MathF.PI / 2)) * (MathF.PI / 2) : yaw;

    /// <summary>A construct's frame on a surface: its up along <paramref name="normal"/> (straight up on near-level ground), turned to <paramref name="yaw"/>.</summary>
    public static Quaternion Frame(Vector3 normal, float yaw, out bool level)
    {
        var n = normal.LengthSquared() > 1e-6f ? Vector3.Normalize(normal) : Vector3.UnitY;
        level = n.Y >= LevelCos;
        var spin = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw); // (our yaw turns -Z towards +X)
        if (level)
            return spin;
        var axis = Vector3.Cross(Vector3.UnitY, n);
        var align = axis.LengthSquared() < 1e-8f
            ? n.Y > 0 ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI)
            : Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(Math.Clamp(n.Y, -1f, 1f)));
        return Quaternion.Normalize(align * spin);
    }

    /// <summary>The part's bounds in its construct's frame, turned by <paramref name="turn"/>.</summary>
    public static void Bounds(ConstructPartDef def, byte turn, out Vector3 min, out Vector3 max)
    {
        var a = Rot24.Apply(turn, def.BoundsMin);
        var b = Rot24.Apply(turn, def.BoundsMax);
        min = Vector3.Min(a, b);
        max = Vector3.Max(a, b);
    }

    private static float Snap(float v) => MathF.Round(v / GridStep) * GridStep;

    /// <summary>
    /// The placement for <paramref name="def"/> on the surface at <paramref name="point"/> (facing <paramref name="normal"/>),
    /// turned <paramref name="turn"/>, the frame turned to <paramref name="yaw"/>; with <paramref name="grid"/> the spot snaps
    /// to the world grid and the yaw to 90 degrees. With a world query the surface is found again under the spot (the
    /// server's own check, and the snapped spot's height); without one (a bare construct world in a test) the point is
    /// taken as it is. Null, or why not.
    /// </summary>
    public static string? Pose(IWorldQuery? q, ConstructPartDef def, Vector3 point, Vector3 normal, float yaw, byte turn, bool grid, out FreePlacement fp)
    {
        fp = default;
        if (!float.IsFinite(point.X + point.Y + point.Z + normal.X + normal.Y + normal.Z + yaw))
            return "no surface there";
        turn = (byte)(turn % Rot24.Count);
        yaw = FrameYaw(yaw, grid);
        var rot = Frame(normal, yaw, out var level);
        var n = level ? Vector3.UnitY : Vector3.Normalize(normal);
        if (grid)
        {
            // The two world axes across the surface snap (on level ground: x and z).
            var a = MathF.Abs(n.X) >= MathF.Abs(n.Y) && MathF.Abs(n.X) >= MathF.Abs(n.Z) ? 0 : MathF.Abs(n.Y) >= MathF.Abs(n.Z) ? 1 : 2;
            point = new Vector3(a == 0 ? point.X : Snap(point.X), a == 1 ? point.Y : Snap(point.Y), a == 2 ? point.Z : Snap(point.Z));
        }
        if (q != null)
        {
            // The surface really is there, under the (snapped) spot.
            if (!q.Raycast(point + n * 0.35f, -n, 0.9f, QueryFilter.All, out var hit))
                return "no surface there";
            point = hit.Point;
            if (!level)
            {
                if (Vector3.Dot(hit.Normal, n) < 0.9f)
                    return "the surface is too uneven there";
            }
        }
        Bounds(def, turn, out var mn, out var mx);
        var foot = new Vector3((mn.X + mx.X) * 0.5f, mn.Y - 0.01f, (mn.Z + mx.Z) * 0.5f); // (the footprint's middle, a centimetre up)
        if (level && q != null)
        {
            // It rests on the highest ground under its footprint's corners (a bump under one corner lifts it).
            var top = point.Y;
            for (var k = 0; k < 4; k++)
            {
                var corner = point + Vector3.Transform(new Vector3((k & 1) == 0 ? mn.X : mx.X, 0, (k & 2) == 0 ? mn.Z : mx.Z) - (foot with { Y = 0 }), rot);
                var d = q.Raycast(corner + new Vector3(0, 0.5f, 0), -Vector3.UnitY, 1.0f, QueryFilter.All);
                if (d < 1.0f)
                    top = MathF.Max(top, corner.Y + 0.5f - d);
            }
            point.Y = top;
        }
        fp = new FreePlacement { Pos = point - Vector3.Transform(foot, rot), Rot = rot, Turn = turn };
        return null;
    }

    /// <summary>Solid box <paramref name="b"/> of the part as placed, in the world (shrunk by a centimetre: touching is fine).</summary>
    public static Obb BoxAt(ConstructPartDef def, in FreePlacement fp, int b)
    {
        var box = def.Boxes[b];
        var half = Vector3.Max(Rot24.ApplyHalf(fp.Turn, box.Half) - new Vector3(0.01f), new Vector3(0.004f));
        return new Obb(fp.Pos + Vector3.Transform(Rot24.Apply(fp.Turn, box.Center), fp.Rot), half, fp.Rot);
    }

    /// <summary>
    /// Is the placement clear: of the map (<paramref name="q"/>, when given), of the players standing at
    /// <paramref name="feet"/> and of every construct's parts? Null, or why not (the ghost shows it in red).
    /// </summary>
    public static string? Fits(ConstructWorld world, IWorldQuery? q, ConstructPartDef def, in FreePlacement fp, ReadOnlySpan<Vector3> feet)
    {
        for (var b = 0; b < def.SolidBoxes; b++)
        {
            var obb = BoxAt(def, fp, b);
            if (q != null && q.OverlapBox(obb, QueryFilter.All))
                return "it would be inside the ground or a wall";
            for (var k = 0; k < feet.Length; k++)
                if (Geometry.ObbOverlap(obb, new Obb(feet[k] + new Vector3(0, World.PlayerHeight * 0.5f, 0), new Vector3(World.PlayerRadius, World.PlayerHeight * 0.5f, World.PlayerRadius), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ)))
                    return "someone is standing there";
            var reach = obb.Half.Length();
            foreach (var c in world.Bodies)
            {
                if (c.Dirty)
                    c.Rebuild();
                if (Vector3.Distance(c.WorldCom, obb.Center) > c.Radius + reach)
                    continue;
                for (var i = 0; i < c.PartCount; i++)
                {
                    c.WorldFrame(c.Parts[i].Seg, out var cq, out var ct);
                    for (var k = 0; k < c.SolidCount(i); k++)
                    {
                        c.SolidBox(i, k, out var oc, out var oh);
                        if (Geometry.ObbOverlap(obb, new Obb(ct + Vector3.Transform(oc, cq), oh, cq)))
                            return "another construct is in the way";
                    }
                }
            }
        }
        return null;
    }

    /// <summary>The constructs a player owns that count towards <see cref="MaxPerPlayer"/> (not wrecks, not loose parts).</summary>
    public static int Owned(ConstructWorld world, byte player)
    {
        var n = 0;
        foreach (var c in world.Bodies)
            if (c.Owner == player && !c.Wreck && !c.Loose)
                n++;
        return n;
    }

    /// <summary>Why the player can't have another construct, or null.</summary>
    public static string? LimitRefusal(ConstructWorld world, byte player)
    {
        if (world.CapRefusal() is { } full)
            return full;
        if (world.PerPlayerCap > 0 && Owned(world, player) >= world.PerPlayerCap)
            return world.PerPlayerCap == MaxPerPlayer
                ? "you have " + MaxPerPlayerText + " constructs, the most a player may have (take one apart or join two)"
                : $"you have {world.PerPlayerCap} constructs, the most the host lets a player have (take one apart or join two)";
        return null;
    }

    // ---- Joining two constructs ----------------------------------------------------------------------------------

    /// <summary>
    /// Does part <paramref name="part"/> of <paramref name="c"/> touch another construct so they can be welded into one: one
    /// of its free structural sockets meeting a free structural socket of the other, face to face, both on their main
    /// bodies (not on a joint), the two bodies square to each other (a 90 degree turn apart)? Returns the first found.
    /// </summary>
    public static bool FindJoin(ConstructWorld world, Construct c, int part, out JoinPlan plan, Construct? only = null)
    {
        plan = default;
        if (part < 0 || part >= c.PartCount || c.Parts[part].Seg != 0 || c.Parts[part].Broken)
            return false;
        var d = c.DefOf(part);
        var inv = Quaternion.Conjugate(c.Rot);
        foreach (var o in world.Bodies)
        {
            if (o == c || only != null && o != only)
                continue;
            if (o.Dirty)
                o.Rebuild();
            if (Vector3.Distance(o.WorldCom, c.ToWorld((c.DefOf(part).BoundsMin + c.DefOf(part).BoundsMax) * 0.5f + c.Parts[part].Pos)) > o.Radius + 2f)
                continue;
            var turn = Rot24.Nearest(inv * o.Rot, out var match);
            if (match < 0.9995f)
                continue; // (not square to it: a weld would bend one of them)
            for (var s = 0; s < d.Sockets.Length; s++)
            {
                if (d.Sockets[s].Kind != SocketKind.Struct || c.SocketUsed(part, s) || d.HasJoint && s == d.LeafIndex)
                    continue;
                c.SocketAt(part, s, out var at, out var dir);
                var wat = c.ToWorld(at);
                for (var j = 0; j < o.PartCount; j++)
                {
                    if (o.Parts[j].Seg != 0 || o.Parts[j].Broken)
                        continue;
                    var od = o.DefOf(j);
                    for (var t = 0; t < od.Sockets.Length; t++)
                    {
                        if (od.Sockets[t].Kind != SocketKind.Struct || o.SocketUsed(j, t) || od.HasJoint && t == od.LeafIndex)
                            continue;
                        o.SocketAt(j, t, out var oat, out var odir);
                        if (Vector3.DistanceSquared(o.ToWorld(oat), wat) > 0.03f * 0.03f || Rot24.ApplyDir(turn, odir) != (dir ^ 1))
                            continue;
                        plan = new JoinPlan { Other = o, PartA = part, SockA = s, PartB = j, SockB = t, Turn = turn, Offset = Vector3.Transform(o.Pos - c.Pos, inv) };
                        return true;
                    }
                }
            }
        }
        return false;
    }
}
