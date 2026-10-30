using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Joining two constructs into one (free placement, board item dev-sandbox-new-construct-nc01): a part welded so it
/// touches another construct lets the welder join them (<see cref="ConstructPlace.FindJoin"/> finds where). Every part of
/// the other construct moves into this one's frame (it is square to it, so each part only takes another Rot24 turn), with
/// its links, joints, wires and fuel and charge; a coupling between the two goes, a wire between them becomes one of its own.
/// </summary>
public sealed partial class ConstructWorld
{
    /// <summary>Why <paramref name="b"/> can't be merged into <paramref name="a"/>, or null.</summary>
    public string? CanMerge(Construct a, Construct b)
    {
        if (a == b)
            return "that's the same construct";
        if (a.PartCount + b.PartCount > Construct.MaxParts)
            return $"together they'd have more than {Construct.MaxParts} parts";
        if (a.JointCount + b.JointCount > Construct.MaxJoints)
            return $"together they'd have more than {Construct.MaxJoints} joints";
        if (a.WireCount + b.WireCount > Construct.MaxWires)
            return $"together they'd have more than {Construct.MaxWires} wires";
        if (a.LinkCount + b.LinkCount + 8 > Construct.MaxLinks)
            return "together they'd have too many welds";
        if (a.Driver != 255 || b.Driver != 255)
            return "someone is driving it";
        for (var i = 0; i < b.PartCount; i++)
            if (b.Parts[i].SitterPlus1 != 0)
                return "someone is sitting in it";
        return null;
    }

    /// <summary>Merges the construct of <paramref name="plan"/> into <paramref name="a"/> (call <see cref="CanMerge"/> first). The other construct goes.</summary>
    public bool Merge(Construct a, in JoinPlan plan)
    {
        var b = plan.Other;
        if (CanMerge(a, b) != null)
            return false;
        if (a.Dirty)
            a.Rebuild();
        if (b.Dirty)
            b.Rebuild();
        // Couplings between the two go (one body can't tow itself); wires between them become its own (below).
        for (var k = CouplingCount - 1; k >= 0; k--)
            if (Couplings[k].BodyA == a.Id && Couplings[k].BodyB == b.Id || Couplings[k].BodyA == b.Id && Couplings[k].BodyB == a.Id)
                Decouple(k, "the two were joined into one");
        var off = (byte)a.PartCount;
        var joints = (byte)a.JointCount;
        var turn = plan.Turn;
        var momentum = a.Vel * a.Mass + b.Vel * b.Mass;
        var mass = a.Mass + b.Mass;
        for (var j = 0; j < b.JointCount; j++)
        {
            var jt = b.Joints[j];
            jt.Hinge = (byte)(jt.Hinge + off);
            jt.ParentSeg = jt.ParentSeg == 0 ? (byte)0 : (byte)(jt.ParentSeg + joints);
            jt.Pivot = plan.Offset + Rot24.Apply(turn, jt.Pivot);
            jt.Axis = Rot24.Apply(turn, jt.Axis);
            a.Joints[a.JointCount++] = jt;
        }
        for (var i = 0; i < b.PartCount; i++)
        {
            var p = b.Parts[i];
            p.Pos = Quantise(plan.Offset + Rot24.Apply(turn, p.Pos));
            p.Rot = Rot24.Compose(turn, p.Rot);
            p.Seg = p.Seg == 0 ? (byte)0 : (byte)(p.Seg + joints);
            a.Parts[a.PartCount++] = p;
            b.Parts[i].Store = 0; // (physical storage: the store moves with its part; b's removal mustn't free it)
        }
        for (var l = 0; l < b.LinkCount; l++)
        {
            var link = b.Links[l];
            link.A = (byte)(link.A + off);
            link.B = (byte)(link.B + off);
            a.Links[a.LinkCount++] = link;
        }
        for (var w = 0; w < b.WireCount; w++)
            a.Wires[a.WireCount++] = new CWire { From = (byte)(b.Wires[w].From + off), To = (byte)(b.Wires[w].To + off) };
        for (var k = XWireCount - 1; k >= 0; k--)
        {
            ref var x = ref XWires[k];
            var ab = x.BodyFrom == a.Id && x.BodyTo == b.Id;
            var ba = x.BodyFrom == b.Id && x.BodyTo == a.Id;
            if (!ab && !ba)
                continue;
            var from = ab ? x.From : x.From + off;
            var to = ab ? x.To + off : x.To;
            if (a.WireCount < Construct.MaxWires && from < a.PartCount && to < a.PartCount)
                a.Wires[a.WireCount++] = new CWire { From = (byte)from, To = (byte)to };
            XWires[k] = XWires[--XWireCount];
            LinksDirty = true;
        }
        AddLink(a, plan.PartA, plan.SockA, plan.PartB + off, plan.SockB);
        for (var i = off; i < a.PartCount; i++)
            ConstructBuild.AutoLink(this, a, i); // (every other socket the two meet at welds too)
        a.AddFuel(b.Fuel);
        a.Charge += b.Charge;
        a.Pinned |= b.Pinned;
        a.Damaged |= b.Damaged;
        a.Wreck = false;
        Remove(b);
        a.Dirty = true;
        a.Rebuild();
        a.Vel = mass > 0 ? momentum / mass : a.Vel;
        a.Wake();
        StructureDirty.Add(a.Id);
        Events.Add($"construct {b.Id} joined into construct {a.Id} ({a.PartCount} parts)");
        return true;
    }
}
