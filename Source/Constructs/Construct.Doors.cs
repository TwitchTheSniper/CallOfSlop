using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// One event door of a construct (docs/sandbox/CONSTRUCTS.md, "Event doors"): a door part on the body's own segment with a
/// <c>"door"</c> block, and the small parts hung on it alone (a handle, a sign). Not a joint and not a segment: its leaf sits
/// in the body frame at its settled pose (shut or open), and only a door that is moving does anything a tick.
/// Derived from the parts at each rebuild (<see cref="Construct.EnsureDoors"/>); what lasts is in the door part itself:
/// where it settled in its hidden <see cref="CfgKey.DoorOpen"/> setting (saves, blueprints, CBody), and while it moves
/// <see cref="CPart.State"/> (<see cref="ConstructDoors.Moving"/>, <see cref="ConstructDoors.Target"/>) and on the server
/// <see cref="CPart.Count"/> (ticks left).
/// </summary>
public struct CDoor
{
    public ushort Part; // the door part
    public ushort LeafFirst; // its other leaf parts: Construct.DoorLeaves[LeafFirst .. LeafFirst + LeafCount]
    public byte LeafCount;
    public bool Wired; // a wire runs into one of its ports: the server reads them each tick (edges only)
    public bool Seen; // the ports below hold a reading (no edge on the first)
    public bool LastOpen, LastToggle;
    public float StartF; // how far open it was drawn when it last started moving (0 shut, 1 open)
    public double StartAt; // and when, on ConstructRender.Now's clock
}

public sealed partial class Construct
{
    /// <summary>Event doors a construct (their own array, not joints: a segment is a byte, a door isn't one).</summary>
    public const int MaxDoors = 1024;

    /// <summary>Parts that may ride one door's leaf besides the door part itself (each linked to that part alone).</summary>
    public const int MaxDoorLeaf = 8;

    public CDoor[] Doors = [];
    public int DoorCount;
    public ushort[] DoorLeaves = [];
    public int DoorLeafCount;
    public int DoorMovingCount, DoorWiredCount; // (the server's step looks at a body only while one is above 0)
    public int DoorEpoch; // goes up whenever a door starts or stops moving (the merged meshes plan again: ConstructRenderBatch.cs)
    private int _doorStamp = -1;
    private int[] _doorAt = [], _doorLinks = [];
    private CDoor[] _doorOld = [];

    /// <summary>The door list as the parts are now (built again after a rebuild). False while the body waits for a rebuild.</summary>
    public bool EnsureDoors()
    {
        if (Dirty)
            return false;
        if (_doorStamp != RebuildStamp)
            BuildDoors();
        return true;
    }

    private void BuildDoors()
    {
        _doorStamp = RebuildStamp;
        var oldCount = DoorCount;
        if (oldCount > 0)
        {
            if (_doorOld.Length < oldCount)
                _doorOld = new CDoor[Doors.Length];
            Array.Copy(Doors, _doorOld, oldCount);
        }
        DoorCount = DoorLeafCount = DoorMovingCount = DoorWiredCount = 0;
        foreach (var i in Specials)
        {
            ref var p = ref Parts[i];
            if (p.D.Door == null || p.Seg != 0 || p.Broken)
                continue;
            if (DoorCount == MaxDoors)
                break;
            if (DoorCount == Doors.Length)
                Array.Resize(ref Doors, Math.Min(MaxDoors, Math.Max(8, DoorCount * 2)));
            Doors[DoorCount++] = new CDoor { Part = (ushort)i };
        }
        if (DoorCount == 0)
        {
            if (oldCount > 0)
                DoorEpoch++;
            return;
        }
        if (_doorAt.Length < PartCount)
        {
            _doorAt = new int[Parts.Length];
            _doorLinks = new int[Parts.Length];
        }
        Array.Fill(_doorAt, -1, 0, PartCount);
        for (var k = 0; k < oldCount; k++)
            if (_doorOld[k].Part < PartCount)
                _doorAt[_doorOld[k].Part] = k;
        // What it was doing before the rebuild (the parts kept their indices when only their shape changed: a settle).
        for (var k = 0; k < DoorCount; k++)
        {
            ref var dr = ref Doors[k];
            if (_doorAt[dr.Part] is var o and >= 0)
            {
                dr.StartF = _doorOld[o].StartF;
                dr.StartAt = _doorOld[o].StartAt;
                dr.Seen = _doorOld[o].Seen;
                dr.LastOpen = _doorOld[o].LastOpen;
                dr.LastToggle = _doorOld[o].LastToggle;
            }
            else
            {
                dr.StartF = ConstructDoors.SettledOpen(this, dr.Part) ? 1 : 0;
                dr.StartAt = ConstructRender.Now;
            }
        }
        Array.Fill(_doorAt, -1, 0, PartCount);
        for (var k = 0; k < DoorCount; k++)
            _doorAt[Doors[k].Part] = k;
        // A leaf's other parts: linked to its door part alone, on any socket but the one it hangs by.
        Array.Clear(_doorLinks, 0, PartCount);
        for (var l = 0; l < LinkCount; l++)
        {
            _doorLinks[Links[l].A]++;
            _doorLinks[Links[l].B]++;
        }
        for (var k = 0; k < DoorCount; k++)
            Doors[k].LeafFirst = (ushort)Math.Min(DoorLeafCount, ushort.MaxValue);
        for (var l = 0; l < LinkCount; l++)
        {
            ref var ln = ref Links[l];
            for (var side = 0; side < 2; side++)
            {
                int door = side == 0 ? ln.A : ln.B, other = side == 0 ? ln.B : ln.A, sock = side == 0 ? ln.SA : ln.SB;
                if (_doorAt[door] is not (var k and >= 0) || _doorAt[other] >= 0 || _doorLinks[other] != 1 || Parts[other].Seg != 0)
                    continue;
                if (sock == Parts[door].D.Door!.Hang)
                    continue;
                ref var dr = ref Doors[k];
                if (dr.LeafCount >= MaxDoorLeaf)
                    continue;
                // (a door's leaf parts sit together: the doors are in part order, the links aren't, so a later door's are moved up)
                var at = dr.LeafFirst + dr.LeafCount;
                if (DoorLeaves.Length <= DoorLeafCount)
                    Array.Resize(ref DoorLeaves, Math.Max(16, DoorLeaves.Length * 2));
                Array.Copy(DoorLeaves, at, DoorLeaves, at + 1, DoorLeafCount - at);
                DoorLeaves[at] = (ushort)other;
                DoorLeafCount++;
                dr.LeafCount++;
                for (var m = k + 1; m < DoorCount; m++)
                    Doors[m].LeafFirst++;
            }
        }
        for (var w = 0; w < WireCount; w++)
            if (Wires[w].To < PartCount && _doorAt[Wires[w].To] is var k and >= 0)
                Doors[k].Wired = true;
        for (var k = 0; k < DoorCount; k++)
        {
            if (Doors[k].Wired)
                DoorWiredCount++;
            if ((Parts[Doors[k].Part].State & ConstructDoors.Moving) != 0)
                DoorMovingCount++;
        }
        DoorEpoch++;
    }

    /// <summary>The event door part <paramref name="part"/> belongs to (its door part or one of its leaf's parts), or -1.</summary>
    public int DoorOf(int part)
    {
        if (!EnsureDoors() || (uint)part >= (uint)PartCount)
            return -1;
        for (var k = 0; k < DoorCount; k++)
        {
            ref var dr = ref Doors[k];
            if (dr.Part == part)
                return k;
            for (var m = 0; m < dr.LeafCount; m++)
                if (DoorLeaves[dr.LeafFirst + m] == part)
                    return k;
        }
        return -1;
    }

    /// <summary>
    /// Drawing: the moving door part <paramref name="part"/> rides, or -1 (no door moves: one compare). A moving leaf is drawn
    /// on its own (ConstructRender.DrawPart) at <see cref="ConstructDoors.DrawPose"/>, out of the merged meshes.
    /// </summary>
    public int MovingDoorOf(int part)
    {
        if (DoorMovingCount == 0 || _doorStamp != RebuildStamp || Dirty)
            return -1;
        for (var k = 0; k < DoorCount; k++)
        {
            ref var dr = ref Doors[k];
            if ((Parts[dr.Part].State & ConstructDoors.Moving) == 0)
                continue;
            if (dr.Part == part)
                return k;
            for (var m = 0; m < dr.LeafCount; m++)
                if (DoorLeaves[dr.LeafFirst + m] == part)
                    return k;
        }
        return -1;
    }
}
