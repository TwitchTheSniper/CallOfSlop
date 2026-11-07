using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Replacer (docs/sandbox/CONSTRUCTS.md "Build stations"): a part swapped in place for another that fits the same
/// sockets. It keeps its index, so every weld, wire and link stays; a link's socket index and port byte move to the new
/// part's. <see cref="Plan"/> says whether it can be done and how, without touching anything: the picker asks it for each
/// candidate. Joints, merged receivers and cargo volumes carry state tied to the part's shape, so they refuse with why.
/// </summary>
public static class ConstructReplace
{
    /// <summary>What a swap will do. Socket and port maps are scratch for the one plan being applied.</summary>
    public struct Swap
    {
        public Vector3 Pos;
        public byte Rot;
    }

    private const int MaxLinks = 64;
    private static readonly (int Link, bool EndA, int Socket, Vector3 At, byte Dir, SocketKind Kind, byte Class)[] Old = new (int, bool, int, Vector3, byte, SocketKind, byte)[MaxLinks];
    private static readonly int[] NewSocket = new int[MaxLinks];
    private static int _oldCount;

    private static string? Gather(Construct c, int part)
    {
        _oldCount = 0;
        for (var e = c.FirstLink(part); e >= 0; e = c.NextLink(e))
        {
            if (_oldCount >= MaxLinks)
                return "too many parts bolted to it: take some off first";
            ref var l = ref c.Links[e >> 1];
            var endA = (e & 1) == 0;
            int other = endA ? l.B : l.A, os = endA ? l.SB : l.SA, mine = endA ? l.SA : l.SB;
            c.SocketAt(other, os, out var at, out var dir);
            var od = c.DefOf(other).Sockets[os % c.DefOf(other).Sockets.Length];
            Old[_oldCount++] = (e >> 1, endA, mine, at, dir, od.Kind, od.Class);
        }
        return null;
    }

    private static bool Fits(in CSocket s, SocketKind kind, byte cls) => s.Kind == kind && s.Class == cls;

    /// <summary>Do all the part's links meet sockets of <paramref name="e"/> at this pose? Fills <see cref="NewSocket"/>.</summary>
    private static bool AllMeet(ConstructPartDef e, Vector3 pos, byte rot)
    {
        Span<bool> used = stackalloc bool[Math.Min(e.Sockets.Length, 512)];
        for (var k = 0; k < _oldCount; k++)
        {
            ref var o = ref Old[k];
            var found = -1;
            for (var s = 0; s < used.Length && found < 0; s++)
            {
                if (used[s] || !Fits(e.Sockets[s], o.Kind, o.Class) || e.HasJoint && s == e.LeafIndex || Rot24.ApplyDir(rot, e.Sockets[s].Dir) != (o.Dir ^ 1))
                    continue;
                if (Vector3.DistanceSquared(pos + Rot24.Apply(rot, e.Sockets[s].At), o.At) < 0.0004f)
                    found = s;
            }
            if (found < 0)
                return false;
            used[found] = true;
            NewSocket[k] = found;
        }
        return true;
    }

    /// <summary>
    /// Can part <paramref name="part"/> become an <paramref name="e"/>? Null and the pose, or why not. The same turn is tried
    /// first, so a beam stays a beam's way round; a part of another size goes where its sockets meet the old links.
    /// </summary>
    public static string? Plan(ConstructWorld world, Construct c, int part, ConstructPartDef e, out Swap swap, bool lugsFree = false)
    {
        swap = default;
        if (part < 0 || part >= c.PartCount)
            return "that part is gone";
        ref var p = ref c.Parts[part];
        var d = c.DefOf(part);
        if (d.Id == e.Id)
            return "that is the same part";
        if (p.Broken)
            return "that part is wrecked: take it off";
        if (c.FreeformCount > 0 && FreeformOn(c, part))
            return "it is freeform-attached: cut it free first";
        if (c.MergeOf[part] != 0 || c.PartScale[part] > 1 || d.IsReceiverGun || e.IsReceiverGun)
            return "receivers merge by what is next to them: take it off and fit the new one";
        if (d.HasJoint && c.JointOf(part) >= 0 || e.HasJoint)
            return "joints carry what hangs on them: take it off and fit the new one";
        if (ConstructCargo.IsVolume(d) || ConstructCargo.IsVolume(e) || d.IsSizedBox || e.IsSizedBox)
            return "volumes are sized by hand: take it off and fit the new one";
        if (p.SitterPlus1 != 0)
            return "someone is sitting in it";
        if (world.StoreOf(c, part) is { } st && (st.Items > 0 || st.Amount > 0 || st.ItemCount > 0))
            return "empty it first";
        if (RouteRefusal(world, c, part, d, e) is { } links)
            return links;
        if (Gather(c, part) is { } many)
            return many;
        var seg = p.Seg;
        var found = false;
        var bestScore = float.MinValue;
        if (_oldCount == 0)
        {
            // Nothing bolted to it (a body's only part): the same place.
            swap = new Swap { Pos = p.Pos, Rot = p.Rot };
            found = !ConstructBuild.Blocked(c, e, p.Pos, p.Rot, 1, seg, part);
            return found ? null : "something is in the way";
        }
        ref var first = ref Old[0];
        Span<byte> turns = stackalloc byte[4];
        var was = p.Pos + Rot24.Apply(p.Rot, (d.BoundsMin + d.BoundsMax) * 0.5f);
        for (var k = 0; k < e.Sockets.Length; k++)
        {
            if (!Fits(e.Sockets[k], first.Kind, first.Class) || e.HasJoint && k == e.LeafIndex)
                continue;
            if (lugsFree && e.Sockets[k].Name.StartsWith("lug", StringComparison.Ordinal))
                continue; // (an Ordnance Rack reshaped hangs by a mount, never by a hook: ModularRack.Reshape)
            var n = Rot24.Matching(e.Sockets[k].Dir, (byte)(first.Dir ^ 1), turns);
            for (var t = 0; t < n; t++)
            {
                var rot = turns[t];
                var pos = ConstructWorld.Quantise(first.At - Rot24.Apply(rot, e.Sockets[k].At));
                if (!AllMeet(e, pos, rot))
                    continue;
                var score = (rot == p.Rot ? 100f : 0f) - Vector3.Distance(was, pos + Rot24.Apply(rot, (e.BoundsMin + e.BoundsMax) * 0.5f));
                if (score <= bestScore || ConstructBuild.Blocked(c, e, pos, rot, 1, seg, part))
                    continue;
                bestScore = score;
                swap = new Swap { Pos = pos, Rot = rot };
                found = true;
            }
        }
        if (found)
        {
            AllMeet(e, swap.Pos, swap.Rot); // (the socket map of the pose chosen)
            return null;
        }
        return _oldCount == 1 ? $"the {e.Name} has no socket that fits there, or something is in the way" : $"the {e.Name} doesn't meet all {_oldCount} of the {d.Name}'s connections";
    }

    private static bool FreeformOn(Construct c, int part)
    {
        for (var j = 0; j < c.JointCount; j++)
            if (c.Joints[j].Freeform && (c.Joints[j].Hinge == part || c.Joints[j].Child == part))
                return true;
        return false;
    }

    /// <summary>The port on <paramref name="e"/> a link at this port of <paramref name="d"/> moves to: the same name, else the same type and way. -1: none.</summary>
    private static int PortFor(ConstructPartDef d, ConstructPartDef e, RouteType type, byte port, bool end)
    {
        NamedPort? old = null;
        foreach (var np in d.NamedPorts)
            if (np.Type == type && np.Byte == port)
                old = np;
        foreach (var np in e.NamedPorts)
            if (np.Type == type && (end ? np.In : np.Out) && old != null && string.Equals(np.Name, old.Name, StringComparison.OrdinalIgnoreCase))
                return np.Byte;
        foreach (var np in e.NamedPorts)
            if (np.Type == type && (end ? np.In : np.Out))
                return np.Byte;
        return -1;
    }

    private static bool CanLink(ConstructPartDef e, RouteType type, bool end) => type switch
    {
        RouteType.Data => end ? e.MaxInputs > 0 : e.HasOutput,
        RouteType.Chute => e.Role == PartRole.Storage,
        _ => Array.Exists(e.NamedPorts, np => np.Type == type && (end ? np.In : np.Out)),
    };

    private static string? RouteRefusal(ConstructWorld world, Construct c, int part, ConstructPartDef d, ConstructPartDef e)
    {
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (r.Parent == 255 && r.From == part && !CanLink(e, r.Type, false) || r.To == part && !CanLink(e, r.Type, true))
                return $"a {ConstructRoutes.TypeKey(r.Type)} link on it has nowhere to go on the {e.Name}";
        }
        for (var k = 0; k < world.WorldLinkCount; k++)
            if (world.WorldLinks[k].BodyFrom == c.Id && world.WorldLinks[k].From == part || world.WorldLinks[k].BodyTo == c.Id && world.WorldLinks[k].To == part)
                return "a link to another construct ends on it: cut it first";
        if (d.Role == PartRole.Hitch && world.CouplingOf(c, part) >= 0)
            return "it is coupled: uncouple it first";
        if (c.TrackHolding(part) >= 0 && (e.Role != PartRole.Wheel || e.TrackRays > 0))
            return "a track runs on it: take the track off first, or swap it for a wheel";
        if (d.Role == PartRole.Hose)
            return "a hose part stays: take it off and fit the new one";
        return null;
    }

    /// <summary>Applies a plan just made with <see cref="Plan"/> (Old and NewSocket are its). Settings carry over by key; paint and tag stay.</summary>
    internal static void Apply(ConstructWorld world, Construct c, int part, ConstructPartDef e, in Swap swap)
    {
        var d = c.DefOf(part);
        for (var k = 0; k < _oldCount; k++)
        {
            ref var l = ref c.Links[Old[k].Link];
            if (Old[k].EndA)
                l.SA = (byte)NewSocket[k];
            else
                l.SB = (byte)NewSocket[k];
        }
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (r.Parent == 255 && r.From == part && PortFor(d, e, r.Type, r.FromPort, false) is var f and >= 0)
                r.FromPort = (byte)f;
            if (r.To == part && PortFor(d, e, r.Type, r.ToPort, true) is var t and >= 0)
                r.ToPort = (byte)t;
        }
        ref var p = ref c.Parts[part];
        var old = p;
        world.SwapStore(ref p, e);
        p.Def = e.Id;
        p.Pos = swap.Pos;
        p.Rot = swap.Rot;
        p.Health = e.Health * MathF.Min(1, old.Health / MathF.Max(1, d.Health));
        p.Rounds = (ushort)Math.Min(e.BoxRounds, ushort.MaxValue);
        p.Setting = e.DefaultSetting;
        p.State = 0;
        p.Count = 0;
        p.Out = p.OutNext = 0;
        p.Burn = 0;
        ConstructConfig.Init(ref p, e);
        // A setting of the same key carries over (a lamp's colour on a bigger lamp; the panel clamps it to the new range).
        for (var s = 0; s < e.Config.Length; s++)
            for (var t = 0; t < d.Config.Length; t++)
                if (e.Config[s].Id == d.Config[t].Id)
                {
                    var v = ConstructConfig.At(old, d, t);
                    ConstructConfig.Set(ref p, e, s, v);
                }
        c.TreeStale();
        c.FuseMassStale(); // (a fused unit holds its members' share of the mass sums: the part's mass changed)
        c.Dirty = true;
        c.Wake();
        world.StructureDirty.Add(c.Id);
    }
}

public sealed partial class ConstructWorld
{
    /// <summary>A part being swapped: its store goes (it was empty) and the new part gets one if it holds things.</summary>
    internal void SwapStore(ref CPart p, ConstructPartDef e)
    {
        FreeStore(ref p);
        AllocStore(ref p, e);
    }
}

public sealed partial class ConstructEdits
{
    /// <summary>
    /// The Replacer: part <paramref name="part"/> becomes an <paramref name="e"/>, keeping its links; the new one comes from the
    /// hands, the inventory or the network, the old one goes back the way a part taken off does (a physics-only one is cut
    /// free as a body beside the construct). Null, or why not.
    /// </summary>
    public string? ReplacePart(ConstructWorld world, byte player, Vector3 eye, ushort body, int part, ushort newId)
    {
        ref var h = ref Hands[player];
        if (AidWhy(world, player, eye, BuildAid.Replace) is { } off)
            return off;
        if (h.Seated)
            return "get out of the seat first";
        if (ConstructParts.Get(newId) is not { } e)
            return "no such part";
        if (world.Get(body) is not { } c || part < 0 || part >= c.PartCount)
            return "that part is gone";
        if (c.Dirty)
            c.Rebuild();
        if (Contested(c, player) is { } defended)
            return defended;
        if (c.Driver != 255 && c.Driver != player)
            return "someone is driving it";
        if (SalvageRefusal(c, player) is { } theirs)
            return theirs;
        if (h.Tool is ConstructTool.None or ConstructTool.Hands)
            return "take out the attach tool (4)";
        var d = c.DefOf(part);
        if (Reachable(eye, ReachPoint(c, part, eye), ConstructBuild.Reach) is { } far)
            return far;
        if (ConstructReplace.Plan(world, c, part, e, out var swap) is { } why)
            return why;
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        var where = t + Vector3.Transform(c.Parts[part].Pos, q);
        var rot = q * Rot24.Quat(c.Parts[part].Rot);
        // Taken before the swap: having none changes nothing. (Plan's scratch stays: this takes no plan.)
        if (!TakeForAid(world, player, e, eye, c.Id))
            return $"no {e.Name} left";
        if (h.Held == ConstructParts.None && !e.PhysicsOnly)
            Refill(world, player, ref h, e, eye);
        ConstructReplace.Apply(world, c, part, e, swap);
        if (!InfiniteParts)
        {
            if (d.PhysicsOnly)
            {
                c.PartBounds(part, out var mn, out var mx);
                if (DropLoose(world, d, where + Vector3.Transform(Vector3.UnitY, q) * (mx.Y - mn.Y + 0.5f), rot) is { } freed)
                    freed.Owner = player;
            }
            else
                ReturnFromAid(world, player, d, eye, where);
        }
        Notices.Add((player, $"Replace: {d.Name} is now a {e.Name}"));
        Done.Add($"player {player}: replaced {d.Name} with {e.Name} on construct {c.Id}");
        return null;
    }
}
