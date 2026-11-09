using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// Body styles phase 6 (docs/vehiclekit/BODY-STYLES.md, section 12): <b>balconies</b> and <b>lopsided</b> vehicles. A room's door may open onto a balcony
/// (<see cref="HullLoft.BalconyShape"/>): a platform at its floor outside the doorway, railed round its outer edge and one end, its ladder up to the open end. A
/// vehicle may be lopsided (<see cref="Lean"/>): its pods on one side only, the other side's trim thinned, its spikes on the one side. Both are the style's data
/// (<see cref="BodyStyleRule.Balcony"/>, <see cref="BodyStyleRule.Asym"/>), each from its own fork of the seed.
/// </summary>
public sealed partial class Vehicle
{
    /// <summary>The balcony a room's door would open onto (null: none), as the style rolls it; the room's look must be skinned (a caged room's bars are its rail).</summary>
    private HullLoft.BalconyShape? BalconyPlan(string tag, bool rear)
    {
        var rule = BodyStyleRule.Of(Style);
        if (BodyLook.NoBalconies || rule.Balcony <= 0 || Look == null || Look.Cage != '\0')
            return null;
        var d = Dice.Fork("balcony " + tag);
        if (!d.Chance(rule.Balcony))
            return null;
        static float Cm(double v) => MathF.Round((float)v * 100) / 100;
        var len = rear ? Cm(d.Between(1.3, 1.7)) : Cm(d.Between(rule.BalconyLenMin, rule.BalconyLenMax)); // (across a tail: inside its width)
        return new HullLoft.BalconyShape(rule.BalconyKind, len, Cm(d.Between(rule.BalconyDepthMin, rule.BalconyDepthMax)), Cm(d.Between(rule.BalconyRailMin, rule.BalconyRailMax)),
            d.Chance(0.5) ? 1 : -1, 0, 0);
    }

    /// <summary>
    /// The balcony outside <paramref name="room"/>'s door, welded to the door's bay: its inner edge a centimetre past whatever stands out of the wall there (the jambs,
    /// the hinge, the floor's edge), refused when that is more than 0.3 m out (a flare or a wheel there) or the balcony would draw into anything; then the ladder at its
    /// open end. False: none, and the caller hangs the ladder under the door as before.
    /// </summary>
    private bool Balcony(Room room, HullLoft.BalconyShape s0)
    {
        var s = s0;
        var rear = Look!.RearDoor;
        if (!rear)
        {
            // A side door's balcony keeps to its own bay, a middle one: past it a cab's side, a brow's rake, a neighbour's pods, panniers and sponsons come later
            // and would stand over its deck where a player's head is (measured: a long one under a merged cab pushed the player off).
            var k = room.Plan.IndexOf("Door");
            if (k < 1 || k > room.Bays.Count - 2 || HullLoft.ShapeOf(room.Bays[k].Def.Name) is not { } dsh)
                return false;
            s = s with { Length = MathF.Min(s.Length, MathF.Floor((dsh.Length - 0.06f) * 100) / 100) };
        }
        var side = Look.DoorSide;
        var orient = rear ? new Orient(Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY, Vector3.UnitY) : new Orient(Vector3.UnitX, new Vector3(side, 0, 0), Vector3.UnitY, Vector3.UnitY);
        var turn = FirstTurn(orient);
        var (outV, alongV) = (Rot24.Apply(turn, Vector3.UnitX), Rot24.Apply(turn, Vector3.UnitZ));
        var plane = Vector3.Dot(room.DoorMid, outV);
        var lc = Vector3.Dot(room.DoorMid, alongV);
        var (yLo, yHi) = (room.FloorTop - 0.15f, room.FloorTop + s.Rail + 0.1f);
        var (inner, head) = (plane, plane);
        var headTop = room.FloorTop + CallOfSlop.World.PlayerHeight + 0.15f;
        for (var i = 0; i < C.PartCount; i++)
        {
            var def = C.DefOf(i);
            C.RestPose(C.Parts[i].Seg, out var q, out var t);
            for (var k = 0; k < def.Boxes.Length; k++)
            {
                C.BoxOf(i, k, out var oc, out var oh);
                (oc, oh) = (FromRest(t + Vector3.Transform(oc, q)), Spread(q, oh));
                if (oc.Y + oh.Y < yLo || oc.Y - oh.Y > headTop)
                    continue;
                if (MathF.Abs(Vector3.Dot(oc, alongV) - lc) > s.Length / 2 + Vector3.Dot(oh, Vector3.Abs(alongV)) + 0.02f)
                    continue;
                var (mid, half) = (Vector3.Dot(oc, outV), Vector3.Dot(oh, Vector3.Abs(outV)));
                if (mid - half > plane + 0.4f)
                    continue;
                if (oc.Y - oh.Y <= yHi)
                    inner = MathF.Max(inner, mid + half); // (at the wall: what stands out of it there)
                else
                    head = MathF.Max(head, mid + half); // (over the rail: a flare or an eave a standing player's head must clear)
            }
        }
        if (inner - plane > 0.3f)
            return false;
        // A player standing on the deck's middle keeps their head clear of a flared wall over it: the deck made deeper, up to 1.2 m, or none.
        var need = MathF.Ceiling(2 * (head + CallOfSlop.World.PlayerRadius + 0.08f - inner - 0.012f) * 100) / 100;
        if (need > 1.2f)
            return false;
        if (need > s.Depth)
            s = s with { Depth = need };
        // The floor's free edge socket outward nearest the doorway's middle: the balcony stands on it.
        var want = room.DoorMid;
        var (sock, best) = (Vector3.Zero, float.MaxValue);
        foreach (var plate in Panels.GetValueOrDefault(room.Tag + "floor") ?? [])
        {
            var at = NearestFacing(plate, outV, want);
            if (at == Vector3.Zero || MathF.Abs(at.Y - room.FloorTop) > 0.2f || Vector3.Dot(at, outV) < plane - 0.2f)
                continue;
            var dist = MathF.Abs(Vector3.Dot(at - room.DoorMid, alongV));
            if (dist < best)
                (sock, best) = (at, dist);
        }
        if (best > 0.3f)
            return false;
        static float Cm(float v) => MathF.Round(v * 100) / 100;
        var inset = MathF.Max(0, MathF.Ceiling((inner + 0.012f - Vector3.Dot(sock, outV)) * 100) / 100);
        s = s with { Inset = inset, Rise = Cm(room.FloorTop - sock.Y) };
        var key = s.Key();
        if (ConstructParts.Find(key) is not { } bdef)
            return false;
        var ghost = new ConstructGhost { Pos = ToRest(sock), Rot = turn, Seg = 0 };
        if (DrawnClash(bdef, ghost, []) != null)
            return false;
        VPart part;
        try
        {
            part = Put(key, sock + Rot24.Apply(turn, (bdef.BoundsMin + bdef.BoundsMax) * 0.5f), orient, 0.05f, null, "base");
        }
        catch (VehicleKitException)
        {
            return false;
        }
        var origin = sock + outV * s.Inset + Vector3.UnitY * s.Rise;
        PanelList("balcony").Add(part);
        room.Balcony = (part, origin + outV * (s.Depth / 2), outV, -s.Gap * alongV, s.Depth, s.Length / 2);
        var si = Array.FindIndex(bdef.Sockets, x => x.Name == "ladder");
        if (si >= 0)
        {
            C.SocketAt(part.Index, si, out var at, out _);
            room.Ladder = Boarding(FromRest(at), room.Tag + "walls");
        }
        Modules.Add($"balcony {s.Kind}{(rear ? " at the back" : "")}");
        return true;
    }

    private int _lean = int.MinValue;

    /// <summary>
    /// Which side a lopsided vehicle leans to (+1 right, -1 left; 0: even), rolled once at the style's <see cref="BodyStyleRule.Asym"/>: toward its side balcony when it
    /// has one, else either way.
    /// </summary>
    public int Lean
    {
        get
        {
            if (_lean != int.MinValue)
                return _lean;
            var rule = BodyStyleRule.Of(Style);
            var d = Dice.Fork("lopsided");
            _lean = 0;
            if (!BodyLook.NoAsym && Look != null && rule.Asym > 0 && d.Chance(rule.Asym))
            {
                var b = Rooms.Select(r => r.Balcony).FirstOrDefault(x => x is { } v && MathF.Abs(v.Out.X) > 0.5f);
                _lean = b is { } bb ? MathF.Sign(bb.Out.X) : d.Chance(0.5) ? 1 : -1;
                Modules.Add($"lopsided {(_lean > 0 ? "right" : "left")}");
            }
            return _lean;
        }
    }
}
