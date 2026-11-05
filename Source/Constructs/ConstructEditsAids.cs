using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The server's half of the build stations (docs/sandbox/CONSTRUCTS.md "Build stations"): who has which aid, and parts for
/// what an aid places. The aids themselves: the mirror here, the line in ConstructLine.cs, the swap in ConstructReplace.cs.
/// </summary>
public sealed partial class ConstructEdits
{
    /// <summary>Every player's aids (server: the truth; a client: its own, as heard).</summary>
    public readonly BuildAidView?[] AidViews = new BuildAidView?[256];

    public BuildAidView AidView(byte player) => AidViews[player] ??= new BuildAidView();

    private bool _mirroring; // (the image of an edit is not mirrored again)
    private bool _lastMirrored; // (did the last MirrorAttach place an image: a line's count says so)

    /// <summary>Why this player has no such aid at <paramref name="eye"/>, or null. Live: an action calls it.</summary>
    public string? AidWhy(ConstructWorld world, byte player, Vector3 eye, BuildAid aid) =>
        BuildAids.Free(InfiniteParts) ? null : world.Beacons.AidWhy(world, player, eye, aid, AidView(player));

    /// <summary>
    /// One part for an aid to place: creative makes it; else the inventory, the network, and last the hands (so a held part
    /// stays held while there are others). A physics-only piece is never stored or carried (decision 21), so it comes from the
    /// loose pieces lying in the aid's range: a stock yard beside the build. False: they have none.
    /// </summary>
    public bool TakeForAid(ConstructWorld world, byte player, ConstructPartDef def, Vector3 eye, ushort notBody = 0)
    {
        if (InfiniteParts)
            return true;
        if (def.PhysicsOnly)
        {
            if (ConstructAid.LoosePiece(world, player, def, eye, notBody) is not { } piece)
                return false;
            world.Remove(piece);
            return true;
        }
        if (Inventory != null && Inventory.Of(player).Take(def.Id))
            return true;
        if (NetworkOf(world, player, eye) is var net and not 0 && world.Beacons.TakePart(world, this, player, net, def))
            return true;
        ref var h = ref Hands[player];
        if (h.Held != def.Id)
            return false;
        h.Held = ConstructParts.None;
        return true;
    }

    /// <summary>A part an aid took off comes back, as one taken off by hand does: the network, the inventory, or to the player.</summary>
    internal void ReturnFromAid(ConstructWorld world, byte player, ConstructPartDef def, Vector3 eye, Vector3 where)
    {
        if (InfiniteParts)
            return;
        if (def.PhysicsOnly)
        {
            Give(world, player, def, where, Quaternion.Identity); // (never an item: a body again)
            return;
        }
        if (ToNetwork(world, player, def, eye))
            return;
        if (Inventory != null && !Survival && Inventory.Of(player).Add(def.Id))
            return;
        Give(world, player, def, where, Quaternion.Identity);
    }

    /// <summary>The mirror plane this player builds with on this construct, if any (refreshed: an edit is never decided on a 4 Hz view).</summary>
    private bool MirrorOn(ConstructWorld world, byte player, Vector3 eye, Construct c, int seg, out int ax, out float w)
    {
        ax = 0;
        w = 0;
        if (_mirroring || AidViews[player] is not { } v)
            return false;
        world.Beacons.AidStates(world, player, eye, v);
        v.Commit(BuildAids.Free(InfiniteParts));
        if (v.Mirror == 0)
            return false;
        if (c.FreeformCount > 0 && c.FrameOf(seg) != 0)
            return false; // (a freeform piece has a frame of its own: the plane means nothing there)
        if (c.Dirty)
            c.Rebuild();
        ConstructMirror.Plane(c, v.Mirror, v.MirrorOffset, out ax, out w);
        return true;
    }

    /// <summary>After a fit: the part's image across the plane, by the same rules. What can't go says why on the HUD; it is never an error.</summary>
    private void MirrorAttach(ConstructWorld world, byte player, ref SandboxHand h, Construct c, ConstructPartDef def, int part, Vector3 eye)
    {
        _lastMirrored = false;
        if (part < 0 || part >= c.PartCount || !MirrorOn(world, player, eye, c, c.Parts[part].Seg, out var ax, out var w))
            return;
        var t = ConstructMirror.Of(def, c.Parts[part].Pos, c.Parts[part].Rot, ax, w);
        if (t.OnPlane)
            return;
        if (!ConstructAid.FindFit(c, t.Def, t.Pos, t.Rot, 0, out var g))
        {
            Notices.Add((player, $"Mirror: no free socket for the {t.Def.Name} on the other side"));
            return;
        }
        if (!TakeForAid(world, player, t.Def, eye, c.Id))
        {
            Notices.Add((player, $"Mirror: no {t.Def.Name} left for the other side"));
            return;
        }
        var j = ConstructBuild.Attach(world, c, t.Def, g);
        if (j < 0)
        {
            ReturnFromAid(world, player, t.Def, eye, c.SegToWorld(g.Seg, t.Pos));
            return;
        }
        if (!t.Exact)
            Notices.Add((player, $"Mirror: the {def.Name} has no mirrored form: turned to fit"));
        _lastMirrored = true;
        SnapRemember(player, t.Def, c, j);
        if (h.Held == ConstructParts.None)
            Refill(world, player, ref h, def, eye);
        Done.Add($"player {player}: mirrored {t.Def.Name} as part {j} of construct {c.Id}");
    }

    /// <summary>After taking a part off: where its image would be, found before the indices move (false: nothing to take).</summary>
    private bool MirrorRemoveFind(ConstructWorld world, byte player, Vector3 eye, Construct c, int part, out ConstructPartDef def, out Vector3 pos)
    {
        def = null!;
        pos = default;
        if (!MirrorOn(world, player, eye, c, c.Parts[part].Seg, out var ax, out var w))
            return false;
        var t = ConstructMirror.Of(c.DefOf(part), c.Parts[part].Pos, c.Parts[part].Rot, ax, w);
        def = t.Def;
        pos = t.Pos;
        return !t.OnPlane;
    }

    /// <summary>The image comes off too, the same way, if it is still there.</summary>
    private void MirrorRemove(ConstructWorld world, byte player, ref SandboxHand h, ushort body, ConstructPartDef def, Vector3 pos, Vector3 eye)
    {
        if (world.Get(body) is not { } c)
            return;
        var k = ConstructMirror.FindPart(c, def, pos);
        if (k < 0)
            return;
        _mirroring = true;
        try
        {
            h.Body = body;
            h.Part = (ushort)k;
            Done.Add($"player {player}: mirrored removal: {Complete(world, player, ref h, EditKind.Remove, eye)}");
        }
        finally
        {
            _mirroring = false;
        }
    }

    // ---- The gantry's reach ---------------------------------------------------------------------------------------

    private bool _gantryReach;
    private Vector3 _gantrySpot;
    private float _gantryRange;

    /// <summary>
    /// Set before each of a player's actions: while the gantry holds them, a point is in reach if it is in the gantry's range
    /// (<see cref="Reachable"/>), wherever their body stands. Off for everyone else.
    /// </summary>
    public void GantryReach(bool on, Vector3 spot, float range)
    {
        _gantryReach = on;
        _gantrySpot = spot;
        _gantryRange = range;
    }

    /// <summary>The gantry takes a player's camera (body nonzero), or lets go. Null, or why not.</summary>
    public string? Gantry(ConstructWorld world, byte player, Vector3 eye, ushort body)
    {
        var v = AidView(player);
        if (body == 0)
            v.GantryBody = 0;
        else
        {
            if (AidWhy(world, player, eye, BuildAid.Gantry) is { } why)
                return why;
            if (Hands[player].Seated)
                return "get out of the seat first";
            if (world.Get(body) is not { } c)
                return "that construct is gone";
            v.GantryBody = c.Id;
        }
        v.Commit(BuildAids.Free(InfiniteParts));
        return null;
    }

    /// <summary>Every look at a held gantry: it ends when the station does, the player leaves its range or the construct goes.</summary>
    public void GantryCheck(ConstructWorld world, byte player, Vector3 eye, bool alive)
    {
        if (AidViews[player] is not { GantryBody: not 0 } v)
            return;
        if (!alive || world.Get(v.GantryBody) == null || AidWhy(world, player, eye, BuildAid.Gantry) != null)
            v.GantryBody = 0;
    }
}
