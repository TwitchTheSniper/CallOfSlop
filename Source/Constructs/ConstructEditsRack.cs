using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Pieces by hand (docs/sandbox/constructs/production.md, "Conveyors"): off a Piece Rack (F takes the one looked at: UseBin;
/// Shift+F drops a kind loose; the physics gun pulls one out) and off a conveyor (F takes a riding piece; the physics gun
/// grabs it). Server; the client names what it aimed at and the server checks it against the state it has now.
/// </summary>
public sealed partial class ConstructEdits
{
    /// <summary>Shift+F on a Piece Rack: the kind looked at, up to <see cref="ConstructConveyor.DropMost"/> of it, loose over the rack. Null, or why not.</summary>
    public string? RackDrop(ConstructWorld world, byte player, Vector3 eye, Vector3 look, ushort body, int part)
    {
        if (Storage(world, player, eye, body, part, out var c) is { } why)
            return why;
        var d = c!.DefOf(part);
        if (!d.IsPieceRack)
            return "that isn't a Piece Rack";
        var st = world.StoreOf(c, part)!;
        if (!world.RackPick(c, part, st, eye, look, out var slab, out _))
            return "the rack is empty";
        var def = st.Slabs![slab].Def;
        var name = ConstructParts.Get(def)?.Name ?? "piece";
        var n = world.RackDropKind(c, part, def, ConstructConveyor.DropMost);
        if (n == 0)
            return "the world is full";
        Done.Add($"player {player}: dropped {n} {name} off the {d.Name} (construct {c.Id})");
        return null;
    }

    /// <summary>
    /// F (<paramref name="grab"/> false: into the hands, by <see cref="Give"/>) or the physics gun (true: a loose body where it
    /// was, in the beam) on a piece riding conveyor <paramref name="route"/> of <paramref name="body"/>, its front about
    /// <paramref name="atCm"/> along when the player aimed. Null, or why not.
    /// </summary>
    public string? TakeRider(ConstructWorld world, byte player, Vector3 eye, float yaw, ushort body, int route, int atCm, bool grab)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (grab && h.Tool != ConstructTool.PhysicsGun)
            return "take out the physics gun to grab things";
        if (world.Get(body) is not { } c || (uint)route >= (uint)c.RouteCount || c.Routes[route].Type != RouteType.Conveyor)
            return "that is gone";
        if (Contested(c, player) is { } defended)
            return defended;
        var i = ConstructConveyor.RiderNear(c, route, atCm);
        if (i < 0)
            return "that piece has gone on";
        var r = c.Conveyed[i];
        if (ConstructParts.Get(r.Def) is not { } d)
            return "that is gone";
        ConstructConveyor.Size(d, out _, out _, out var along);
        ConstructConveyor.Pose(c, route, r.At - along * 0.5f, d, out var o, out _, out var seg, r.ViaPlus1 - 1);
        c.WorldFrame(seg, out var q, out var t);
        if (Reachable(eye, t + Vector3.Transform(o, q), ConstructBuild.UseReach + along * 0.5f + 0.5f) is { } far) // (it moves on as the request comes: half a metre's grace)
            return far;
        if (ConstructConveyor.TakeRider(world, c, i, out var centre, out var rot, out var health, out var paint) is not { } piece)
            return "that is gone";
        Done.Add($"player {player}: took {piece.Name} off a conveyor (construct {c.Id})");
        if (!grab)
        {
            Give(world, player, piece, centre, rot);
            return null;
        }
        if (DropLoose(world, piece, centre, rot) is not { } loose)
            return "the world is full";
        loose.Owner = c.Owner;
        loose.Parts[0].Health = MathF.Max(1, piece.Health * health / 255f);
        loose.Parts[0].Paint = paint;
        return Grab(world, player, eye, yaw, loose.Id, loose.Com);
    }

    /// <summary>The physics gun on a Piece Rack's stack (<paramref name="local"/>: where the beam met it, body frame): its top piece comes out loose where it lay, in the beam.</summary>
    private string? GrabFromRack(ConstructWorld world, byte player, Vector3 eye, float yaw, Construct c, int part, Vector3 local)
    {
        if (Contested(c, player) is { } defended)
            return defended;
        if (world.StoreOf(c, part) is not { ItemCount: > 0 } st)
            return "the rack is empty";
        var rd = c.DefOf(part);
        ConstructRackPack.Ensure(rd, st);
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out var t);
        var inSeg = Vector3.Transform(c.ToWorld(local) - t, Quaternion.Conjugate(q));
        var inRack = Vector3.Transform(inSeg - p.Pos, Quaternion.Conjugate(Rot24.Quat(p.Rot)));
        if (!ConstructRackPack.At(rd, st, inRack, out var slab, out var lane) && !ConstructRackPack.LastOf(st, st.KindDef![st.KindN - 1], out slab, out lane))
            return "the rack is empty";
        ConstructWorld.RackPieceAt(c, part, st, slab, lane, out var at, out var turn);
        if (world.RackTakeAt(c, part, slab, lane) is not { } piece)
            return "the rack is empty";
        if (DropLoose(world, piece, at, turn) is not { } loose)
        {
            world.RackPut(c, part, piece); // (back where it can go)
            return "the world is full";
        }
        loose.Owner = c.Owner;
        Done.Add($"player {player}: pulled {piece.Name} off the {rd.Name} (construct {c.Id})");
        return Grab(world, player, eye, yaw, loose.Id, loose.Com);
    }
}
