using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// One event door of a construct (docs/sandbox/CONSTRUCTS.md, "Event doors"): a door part on the body's own segment with a
/// <c>"door"</c> block, and the small parts hung on it alone (a handle, a sign). Not a joint and not a segment: its leaf sits
/// in the body frame at its settled pose (shut or open), and only a door that is moving does anything a tick. Its parts are
/// never in the merged meshes (they are drawn on their own), and settling moves their boxes in the collider and the part tree
/// alone (<see cref="Construct.DoorPartMoved"/>): no rebuild, no refresh, the body's mass as built.
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
    public bool DockSeen, Mated, MateNow, ShutDue; // a docking hatch ("dock": true): looked at once, mated at the last look, at this one, and to shut once nobody is in it (ConstructDoors.StepDocks)
    public float StartF; // how far open it was drawn when it last started moving (0 shut, 1 open)
    public double StartAt; // and when, on ConstructRender.Now's clock
    public float Squash, PrevSquash; // a sprung landing foot's (CDoorDef.SpringTravel): how far its spring is in, metres (server physics: ConstructPhysics.GearFeet)
    public float Shown; // ...and as drawn on this peer (ConstructPhysics.FitGear)
    public bool Touch; // its spring is on the ground
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
    public int DoorDockCount; // docking hatches among them (ConstructDoors.StepDocks)
    public int DoorMovingCount, DoorWiredCount; // (the server's step looks at a body only while one is above 0)
    public int GearTicks; // server: ticks until a gear key's second half (ConstructDoors.ToggleGear); not saved
    public bool GearOut; // ...and which way it goes: true lowers the legs (after the canopy), false shuts the canopy (after the legs)
    public int GearFootCount; // sprung landing feet among them (CDoorDef.SpringTravel: ConstructPhysics.GearFeet)
    public int DoorHeldCount; // "held" doors (ConstructDoors.cs: drawn at their open pose while open, so the drawing looks them up)
    public int LaunchHatchHold; // server: ticks until the launch hatches opened for its launches shut (0: none is held open; ConstructDoors.OpenForLaunch)
    public int DoorEpoch; // goes up whenever the door list is built again (the merged meshes plan again, leaving door parts out: ConstructRenderBatch.cs)
    private int _doorStamp = -1;
    private int[] _doorAt = [], _doorLinks = [];
    private int[] _doorOfPart = []; // part -> its door (the door part or a leaf part), -1: none (for PartCount parts as of the build)
    private int _doorOfCount;
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
        DoorCount = DoorLeafCount = DoorMovingCount = DoorWiredCount = DoorDockCount = DoorHeldCount = GearFootCount = 0;
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
        _doorOfCount = 0;
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
            _doorOfPart = new int[Parts.Length];
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
                dr.DockSeen = _doorOld[o].DockSeen;
                dr.Mated = _doorOld[o].Mated;
                dr.ShutDue = _doorOld[o].ShutDue;
                dr.Squash = _doorOld[o].Squash;
                dr.PrevSquash = _doorOld[o].PrevSquash;
                dr.Shown = _doorOld[o].Shown;
                dr.Touch = _doorOld[o].Touch;
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
        Array.Fill(_doorOfPart, -1, 0, PartCount);
        for (var k = 0; k < DoorCount; k++)
        {
            ref var dr = ref Doors[k];
            _doorOfPart[dr.Part] = k;
            for (var m = 0; m < dr.LeafCount; m++)
                _doorOfPart[DoorLeaves[dr.LeafFirst + m]] = k;
        }
        _doorOfCount = PartCount;
        for (var w = 0; w < WireCount; w++)
            if (Wires[w].To < PartCount && _doorAt[Wires[w].To] is var k and >= 0)
                Doors[k].Wired = true;
        for (var k = 0; k < DoorCount; k++)
        {
            if (Doors[k].Wired)
                DoorWiredCount++;
            if (Parts[Doors[k].Part].D.Door!.Dock)
                DoorDockCount++;
            if ((Parts[Doors[k].Part].State & ConstructDoors.Moving) != 0)
                DoorMovingCount++;
            if (Parts[Doors[k].Part].D.Door!.Held)
                DoorHeldCount++;
            if (Parts[Doors[k].Part].D.Door!.SpringTravel > 0)
                GearFootCount++;
        }
        DoorEpoch++;
    }

    /// <summary>The event door part <paramref name="part"/> belongs to (its door part or one of its leaf's parts), or -1.</summary>
    public int DoorOf(int part)
    {
        if (!EnsureDoors() || (uint)part >= (uint)PartCount)
            return -1;
        return DoorOfListed(part);
    }

    /// <summary>As <see cref="DoorOf"/>, from the list as last built (never builds it: drawing). -1 while the list waits for a rebuild.</summary>
    public int DoorOfListed(int part) => DoorCount > 0 && _doorStamp == RebuildStamp && !Dirty && (uint)part < (uint)_doorOfCount ? _doorOfPart[part] : -1;

    /// <summary>
    /// Drawing: the moving door part <paramref name="part"/> rides, or -1 (no door moves: one compare). A door's parts are
    /// always drawn on their own (ConstructRender.DrawPart), a moving one at <see cref="ConstructDoors.DrawPose"/>.
    /// </summary>
    public int MovingDoorOf(int part)
    {
        if ((DoorMovingCount | DoorHeldCount) == 0 || DoorOfListed(part) is not (var k and >= 0))
            return -1;
        ref var dp = ref Parts[Doors[k].Part];
        if ((dp.State & ConstructDoors.Moving) != 0)
            return k;
        return dp.D.Door!.Held && ConstructDoors.SettledOpen(this, Doors[k].Part) ? k : -1; // (a held door, open: drawn at its open pose)
    }

    // ---- Settling: only what the leaf touches (ConstructDoors.Settle) --------------------------------------------

    /// <summary>Door part <paramref name="part"/> is about to move: its share of a big body's mass sums is taken off (put back at its new place by <see cref="DoorPartMoved"/>).</summary>
    internal void DoorPartMoving(int part)
    {
        if (DoorSums(part))
            AddPartSums(part, -1);
    }

    private bool DoorSums(int part) => _sumsValid && !Dirty && !FuseMassHeld(part);

    /// <summary>
    /// Door part <paramref name="part"/> (on segment 0) was just put at its new pose, turned by <paramref name="r"/> from
    /// <paramref name="oldPos"/>, its bounds before being <paramref name="oldMin"/>..<paramref name="oldMax"/>. Its boxes are moved
    /// in the collider and its bounds in the part tree, the nodes over them refitted; the body's bounds grow to hold it. The
    /// rest stays as built: the mass and inertia (a leaf moving a metre on a big body changes nothing that matters; a big body's
    /// sums follow it, so an edit's journal takes off what is there, and the next full refresh of the mass sees it), the merged
    /// meshes (door parts aren't in them), the tree's shape. Costs the depth of the trees, not the part count; allocates nothing.
    /// A part the collider has in another form (a hull, fused, merged) has it built again when next asked for, as before.
    /// </summary>
    /// <summary>How many collider leaves part <paramref name="part"/> has (as ConstructCollider.Build makes them): its solid boxes
    /// no hull stands for, and its hulls (a ship door's skin plates), or one for its bounds.</summary>
    private int ColliderLeaves(int part)
    {
        var d = Parts[part].D;
        var hulls = ConstructHulls.Of(d);
        var n = hulls.Length;
        var boxes = SolidCount(part);
        for (var b = 0; b < boxes; b++)
            if (hulls.Length == 0 || !ConstructHulls.Covered(d, b))
                n++;
        return Math.Max(1, n);
    }

    internal void DoorPartMoved(int part, Vector3 oldMin, Vector3 oldMax, Vector3 oldPos, byte r)
    {
        if (DoorSums(part))
        {
            AddPartSums(part, 1);
            StructBounds(part, out var smn, out var smx);
            GrowBounds(smn, smx); // (the face it left stays out: loose, never short)
        }
        ReachBounds(part, out var mn, out var mx); // (as the part tree holds it: ConstructDoors.Place passes the old one so)
        ref var p = ref Parts[part];
        if (_collider != null && !_colliderStale)
        {
            if (FuseRough(part) || MergeOf[part] != 0 || p.D.IsEnvelope || Rides(part)
                || !_collider.MovePart(part, ColliderLeaves(part), oldMin, oldMax, oldPos, p.Pos, r))
                ColliderChanged();
        }
        if (_treeParts >= 0 && !TreeMove(part, oldMin, oldMax, mn, mx))
            TreeStale();
        if (mn.X < LocalMin.X || mn.Y < LocalMin.Y || mn.Z < LocalMin.Z || mx.X > LocalMax.X || mx.Y > LocalMax.Y || mx.Z > LocalMax.Z)
        {
            LocalMin = Vector3.Min(LocalMin, mn);
            LocalMax = Vector3.Max(LocalMax, mx);
            Radius = MathF.Max(Radius, MathF.Max(Vector3.Distance(Com, LocalMin), Vector3.Distance(Com, LocalMax)) + 0.5f);
        }
    }

}
