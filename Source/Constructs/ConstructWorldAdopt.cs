using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A body built on a world of its own joining this one whole (<see cref="ConstructBuild.GroundJob"/>: a big blueprint's steps
/// over several ticks, where nothing simulates, saves or sends a half-built body). What a body holds of its world is made
/// again here: its id, its parts' tags and stores, the caps.
/// </summary>
public sealed partial class ConstructWorld
{
    /// <summary><see cref="Adopt"/>'s answer for a body it doesn't take as it is (more than one body, or a store holding parts): build it here instead.</summary>
    public const string AdoptUnusual = "it came out of its steps holding more than a new body does";

    /// <summary>
    /// Body <paramref name="c"/> of <paramref name="from"/> (that world's only body, fresh from a blueprint's steps) moves into
    /// this world at the pose given, as if it had been built here: a new id, its parts' tags from this world, its containers'
    /// stores from this pool (with the bulk a creative Storage Volume was filled with), the world's caps. Null, with why, when
    /// this world can't take it (it stays in <paramref name="from"/>); <see cref="AdoptUnusual"/> for a body it doesn't take as it is.
    /// </summary>
    public Construct? Adopt(ConstructWorld from, Construct c, Vector3 pos, Quaternion rot, out string why)
    {
        why = "";
        if (from.Bodies.Count != 1 || !ReferenceEquals(from.Bodies[0], c))
        {
            why = AdoptUnusual; // (a merge that popped a part off, say: the steps' world has more than this body)
            return null;
        }
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Store != 0 && from.Stores[c.Parts[i].Store] is { } st && (st.Items > 0 || st.KindN > 0 || st.MixN > 0 || st.InAmount > 0))
            {
                why = AdoptUnusual;
                return null;
            }
        if (Bodies.Count >= BodyCap || Bodies.Count >= ushort.MaxValue - 1 || PartTally + c.PartCount > PartCap || TotalJoints + c.JointCount > JointCap)
            return null; // ("": the caller says the world is full, as for a body Create refuses)
        from.Bodies.Clear();
        from._byId.Clear();
        c.Id = FreeId();
        c.Pos = c.PrevPos = c.NetPos = c.NetFromPos = pos;
        c.Rot = c.PrevRot = c.NetRot = c.NetFromRot = rot;
        c.StorePool = Stores;
        c.QueryMark = c.GridMark = c.SimMark = 0; // (the other world's stamps: as a new body's)
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            p.Tag = NextTag(); // (in part order, as building here would have handed them out)
            if (p.Store == 0)
                continue;
            var had = from.Stores[p.Store];
            AllocStore(ref p, p.D);
            if (p.Store != 0 && had.Amount > 0)
                SetBulk(Stores[p.Store], had.Material, had.Amount);
        }
        Bodies.Add(c);
        _byId[c.Id] = c;
        StructureDirty.Add(c.Id);
        GridAdded(c);
        _blasting?.Add(c);
        if (_partTally >= 0)
            _partTally += c.PartCount;
        Events.AddRange(from.Events);
        c.Dirty = true;
        c.Wake();
        return c;
    }

    /// <summary>The next free body id (wrapping round past 65535 to the ids freed since).</summary>
    private ushort FreeId()
    {
        while (_byId.ContainsKey(_nextId) || _nextId == 0)
            _nextId++;
        return _nextId++;
    }
}
