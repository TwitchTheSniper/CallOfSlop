using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A tool dock (a part with <c>"toolDock": true</c>, the Magpie's Drill Dock; Karl, 2026-11-16): F on it lets go of what is
/// clipped to its "dock" socket, which falls away as a loose piece of its own (with whatever is fitted to it, such as the
/// drill's battery); F with such a piece close by clips it back on. A piece fits when one of its parts carries a free socket
/// that can meet the dock's face to face: the piece is moved square onto the dock and welded there, as the attach tool's
/// join does. Server; the client sends the plain Use.
/// </summary>
public sealed partial class ConstructEdits
{
    public const float DockReach = 2.5f; // how near a loose piece's socket must be to the dock to be clipped on (m)
    public const int DockMostParts = 8; // the biggest piece a dock takes (a drill with its battery and a lamp)

    /// <summary>F on tool dock <paramref name="dock"/> of <paramref name="c"/>: null when done, else why not.</summary>
    private string? DockUse(ConstructWorld world, byte player, Construct c, int dock)
    {
        ref var dp = ref c.Parts[dock];
        var d = dp.D;
        if (dp.Broken)
            return $"the {d.Name} is wrecked";
        if (Contested(c, player) is { } defended)
            return defended;
        var s = DockSocket(d);
        if (s < 0)
            return $"the {d.Name} has no dock socket";
        // Something on it: it lets go.
        for (var e = c.FirstLink(dock); e >= 0; e = c.NextLink(e))
        {
            ref var l = ref c.Links[e >> 1];
            var mine = (e & 1) == 0 ? l.SA : l.SB;
            if (mine != s)
                continue;
            var held = c.OtherEnd(e);
            var marks = new bool[c.PartCount];
            if (!MarkPiece(c, dock, held, marks))
                return "it is welded to more than the dock: take it off with the attach tool";
            var name = c.DefOf(held).Name;
            if (world.CutOut(c, marks, player) is not { } piece)
                return "the world is full";
            piece.Wake();
            Done.Add($"player {player}: let the {name} go from the {d.Name} (construct {c.Id} -> {piece.Id})");
            return null;
        }
        return DockClip(world, player, c, dock, null);
    }

    /// <summary>Why the dock on <paramref name="c"/> can't clamp a shell on now (defended, someone flying the ship), else null: asked before the wearer leaves it.</summary>
    public string? DockRefusal(Construct c, byte player) => Contested(c, player) ?? (c.Driver != 255 ? "someone is flying it: the dock clamps when nobody is" : null);

    /// <summary>A Robot Shell Dock clamps on the shell its wearer just left (Game/GameRobotShell.cs, ShellDockUse): null when done, else why not.</summary>
    public string? ClipOnDock(ConstructWorld world, byte player, Construct c, int dock, Construct piece) => DockClip(world, player, c, dock, piece);

    /// <summary>The empty dock clips on the nearest loose piece that fits (only <paramref name="only"/>, when given).</summary>
    private string? DockClip(ConstructWorld world, byte player, Construct c, int dock, Construct? only)
    {
        ref var dp = ref c.Parts[dock];
        var d = dp.D;
        var s = DockSocket(d);
        if (s < 0)
            return $"the {d.Name} has no dock socket";
        c.SocketAt(dock, s, out var at, out var dir);
        var wat = c.ToWorld(at);
        var wdir = Vector3.Transform(ConstructParts.DirVector(dir), c.Rot);
        var wup = Vector3.Transform(Vector3.Transform(Vector3.UnitY, Rot24.Quat(dp.Rot)), c.Rot);
        Construct? best = null;
        int bestPart = -1, bestSock = -1;
        var bestDist = DockReach;
        foreach (var o in world.Bodies)
        {
            if (o == c || only != null && o != only || o.Anchored || o.PartCount > DockMostParts || o.Driver != 255)
                continue;
            if (Vector3.Distance(o.Pos, wat) > o.Radius + DockReach)
                continue;
            for (var j = 0; j < o.PartCount; j++)
            {
                if (o.Parts[j].Seg != 0 || o.Parts[j].Broken || !Fits(d, o.DefOf(j)))
                    continue;
                var od = o.DefOf(j);
                for (var t = 0; t < od.Sockets.Length; t++)
                {
                    if (od.Sockets[t].Kind != SocketKind.Struct || od.Sockets[t].Class != d.Sockets[s].Class || o.SocketUsed(j, t) || od.Sockets[t].Name != "back")
                        continue;
                    o.SocketAt(j, t, out var oat, out _);
                    var dist = Vector3.Distance(o.ToWorld(oat), wat);
                    if (dist < bestDist)
                        (best, bestPart, bestSock, bestDist) = (o, j, t, dist);
                }
            }
        }
        if (best == null)
            return $"bring a {DockFitsText(d)} within {DockReach:0.#} m of the {d.Name} first (the physics gun carries it)";
        if (Contested(best, player) is { } theirs)
            return theirs;
        if (world.CanMerge(c, best) is { } why)
            return why;
        // Square it onto the dock: its socket against the dock's, facing it, its part's up along the dock's up.
        best.SocketAt(bestPart, bestSock, out var bat, out var bdir);
        var a1 = ConstructParts.DirVector(bdir);
        var b1 = Rot24.Apply(best.Parts[bestPart].Rot, Vector3.UnitY);
        if (MathF.Abs(Vector3.Dot(a1, b1)) > 0.5f)
            b1 = Rot24.Apply(best.Parts[bestPart].Rot, Vector3.UnitZ);
        var a2 = -wdir;
        var b2 = MathF.Abs(Vector3.Dot(a2, wup)) > 0.5f ? Vector3.Transform(Vector3.Transform(Vector3.UnitZ, Rot24.Quat(dp.Rot)), c.Rot) : wup;
        var rot = Turn(a1, b1, a2, b2);
        best.Rot = best.PrevRot = rot;
        best.Pos = best.PrevPos = wat - Vector3.Transform(bat, rot);
        best.Vel = best.AngVel = Vector3.Zero;
        if (!ConstructPlace.FindJoin(world, c, dock, out var plan, only: best) || plan.PartB != bestPart)
            return "it won't sit square on the dock";
        var from = best.Id;
        if (!world.Merge(c, plan))
            return "it can't be clipped on";
        Done.Add($"player {player}: clipped construct {from} onto the {d.Name} (construct {c.Id})");
        return null;
    }

    private static int DockSocket(ConstructPartDef d)
    {
        for (var i = 0; i < d.Sockets.Length; i++)
            if (d.Sockets[i].Name == "dock")
                return i;
        return -1;
    }

    /// <summary>Does part <paramref name="o"/> go on this dock: its name starts with the dock's <c>toolDock</c> kind ("Drill Module" takes every tier).</summary>
    private static bool Fits(ConstructPartDef dock, ConstructPartDef o) => dock.ToolDockFits.Length == 0 || o.Name.StartsWith(dock.ToolDockFits, StringComparison.Ordinal);

    private static string DockFitsText(ConstructPartDef d) => d.ToolDockFits.Length > 0 ? d.ToolDockFits : "part";

    /// <summary>Marks <paramref name="held"/> and everything reached from it without passing the dock. False when that reaches more than a small piece (it is part of the ship's structure).</summary>
    private static bool MarkPiece(Construct c, int dock, int held, bool[] marks)
    {
        var stack = new Stack<int>();
        stack.Push(held);
        marks[held] = true;
        var n = 1;
        while (stack.Count > 0)
        {
            var at = stack.Pop();
            for (var e = c.FirstLink(at); e >= 0; e = c.NextLink(e))
            {
                var other = c.OtherEnd(e);
                if (other == dock || marks[other])
                    continue;
                marks[other] = true;
                if (++n > DockMostParts)
                    return false;
                stack.Push(other);
            }
        }
        return true;
    }

    /// <summary>The turn taking the frame (a1, b1) onto (a2, b2) (unit vectors, each pair square).</summary>
    private static Quaternion Turn(Vector3 a1, Vector3 b1, Vector3 a2, Vector3 b2)
    {
        var c1 = Vector3.Cross(a1, b1);
        var c2 = Vector3.Cross(a2, b2);
        var from = new Matrix4x4(a1.X, a1.Y, a1.Z, 0, b1.X, b1.Y, b1.Z, 0, c1.X, c1.Y, c1.Z, 0, 0, 0, 0, 1);
        var to = new Matrix4x4(a2.X, a2.Y, a2.Z, 0, b2.X, b2.Y, b2.Z, 0, c2.X, c2.Y, c2.Z, 0, 0, 0, 0, 1);
        // Row vectors (System.Numerics): v * from^T takes v into the (a1, b1, c1) basis, * to takes it back out along (a2, b2, c2).
        var m = Matrix4x4.Transpose(from) * to;
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }
}
