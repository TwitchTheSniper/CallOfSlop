using System.Numerics;

namespace CallOfSlop;

/// <summary>What a ray hit on a construct.</summary>
public struct ConstructHit
{
    public Construct Body;
    public int Part;
    public float Distance;
    public Vector3 Point, Normal;
    public byte SlotPlus1; // physical storage: the stored part hit is in this slot of the part (plus one; 0: the part itself)
    public byte RoutePlus1; // routed links: the link hit (plus one; 0: a part), its segment in RouteSeg (Part: the part at that segment's end)
    public byte RouteSeg;
    public bool Glass; // the box hit is a glass pane: rounds go on through it (and break it)
}

/// <summary>
/// Every construct in a match (server: simulated; client: a mirror of what the server sends). Owns the bodies, splitting
/// cut-off pieces into wrecks, per-part damage, ray tests, keeping players out of constructs. There is no design cap on
/// how many bodies a world holds (board item cl01): sleeping bodies cost nothing a tick, and the queries go through a
/// broadphase grid (ConstructBroadphase.cs). Only a safety cap stays, the host's <see cref="BodyCap"/> and
/// <see cref="PartCap"/>, against runaway spam.
/// Changes the network must carry are queued (<see cref="StructureDirty"/>, <see cref="Removed"/>, <see cref="HealthDirty"/>).
/// </summary>
public sealed partial class ConstructWorld
{
    // Safety caps (the host's "Max constructs"), not design caps: defaults, and the most a host may set (ids are ushort).
    public const int DefaultBodyCap = 10000;
    public const int DefaultPartCap = 250000;
    public const int MostBodies = 60000;
    public const int MostParts = 2000000;
    public const float FallOutY = -30f;

    /// <summary>The most bodies (constructs, wrecks and loose parts) the world holds: the host's safety cap.</summary>
    public int BodyCap = DefaultBodyCap;

    /// <summary>The most parts in all: the host's safety cap.</summary>
    public int PartCap = DefaultPartCap;

    /// <summary>Joints in all (each construct: Construct.MaxJoints): scales with the body cap, only awake ones are simulated.</summary>
    public int JointCap => Math.Max(128, BodyCap * 4);

    /// <summary>Routed links and world links in all (each construct: Construct.MaxRoutes): scales with the body cap.</summary>
    public int WireCap => Math.Max(512, BodyCap * 8);

    /// <summary>Why the host's caps refuse one more body (or <paramref name="parts"/> more parts), or null.</summary>
    public string? CapRefusal(int parts = 1)
    {
        if (Bodies.Count >= BodyCap)
            return $"the world is full: the host's limit is {BodyCap} constructs and loose parts";
        if (PartTally + parts > PartCap)
            return $"the world is full: the host's limit is {PartCap} parts";
        return null;
    }

    /// <summary>Is there room for one more body (and <paramref name="parts"/> parts) under the host's caps?</summary>
    public bool HasRoom(int parts = 1) => Bodies.Count < BodyCap && PartTally + parts <= PartCap;

    public readonly List<Construct> Bodies = new(256);
    private readonly Dictionary<ushort, Construct> _byId = new(256);
    private ushort _nextId = 1;

    // Network queues (server): bodies whose parts changed, bodies gone, part health changes.
    public readonly HashSet<ushort> StructureDirty = new(256);
    public readonly List<ushort> Removed = new(256);
    public readonly List<(ushort Body, byte Part)> HealthDirty = new(256);

    /// <summary>Where the living players are this tick (the server fills it; proximity sensors read it).</summary>
    public readonly Vector3[] PlayerSpots = new Vector3[64];
    public int PlayerSpotCount;

    /// <summary>
    /// Glass parts that broke since the game last sent them (server): where (the part's world bounds' centre and half
    /// size). The game sends each as a <see cref="CFireKind.Shatter"/> event and every PC bursts it into small shards.
    /// </summary>
    public readonly List<(Vector3 Centre, Vector3 Half)> Shattered = new(32);

    /// <summary>
    /// What the last <see cref="BlockPlayer"/> call found: the hardest push by a moving part (run-over damage), and who
    /// pushed. The speed is the construct's own: its contact point's velocity (the body's motion, linear and turning, and
    /// any joint's under it) along the contact normal towards the player. The player's own velocity plays no part, so
    /// running into a parked car is not an impact.
    /// </summary>
    public float LastHitSpeed, LastHitMass;
    public Vector3 LastHitVel, LastHitNormal;
    public Construct? LastHitBody;

    // ---- Weapons (milestone 3; ConstructWeapons.cs) ----------------------------------------------------------------

    /// <summary>Who the guns can hit besides parts and the map (the game sets it on the server; null: parts and the map only).</summary>
    public IConstructTargets? Targets;

    /// <summary>What the guns did this tick (shots, blasts, cook-offs): the server sends them, the host draws them. Cleared at the start of each step.</summary>
    public readonly CFireEvent[] Fires = new CFireEvent[256];
    public int FireCount;
    public int ShotsFired; // shots in all (the self-test)
    public ushort LastShotBody; // the last round's gun's construct and where it ended (the self-test)
    public Vector3 LastShotEnd;

    /// <summary>Ammo and fire changes the network must carry (a box's rounds, a gun's load, a part catching fire or going out).</summary>
    public readonly List<(ushort Body, byte Part)> AmmoDirty = new(256);

    /// <summary>Blasts waiting to go off (ammo boxes cooking off).</summary>
    public readonly CCookOff[] CookOffs = new CCookOff[32];
    public int CookOffCount;

    public void AddFire(in CFireEvent e)
    {
        if (e.Kind == CFireKind.Shot)
        {
            LastShotBody = e.Body;
            LastShotEnd = e.At;
        }
        if (FireCount < Fires.Length)
            Fires[FireCount++] = e;
    }

    public void NoteAmmo(Construct c, int part)
    {
        var key = (c.Id, (byte)part);
        var n = AmmoDirty.Count;
        if (n > 0 && AmmoDirty[n - 1] == key)
            return;
        if (n < 1024)
            AmmoDirty.Add(key);
    }

    /// <summary>Sets a flammable part alight (incendiary rounds, a neighbour burning): it burns for <see cref="ConstructWeapons.BurnSeconds"/>.</summary>
    public bool Ignite(Construct c, int part)
    {
        if (part < 0 || part >= c.PartCount || !c.Parts[part].D.Flammable)
            return false;
        ref var p = ref c.Parts[part];
        if (p.Burn == 0)
            c.Burning++;
        p.Burn = (ushort)(ConstructWeapons.BurnSeconds * World.TickRate);
        NoteAmmo(c, part);
        return true;
    }

    private void CookOff(Construct c, int part, Vector3 at)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        float radius, damage;
        if (d.Role == PartRole.AmmoBox && p.Rounds > 0 && d.CookRadius > 0)
        {
            var fill = Math.Clamp(p.Rounds / (float)Math.Max(1, d.BoxRounds), 0, 1);
            radius = d.CookRadius * (0.5f + 0.5f * fill);
            damage = d.CookDamage * (0.4f + 0.6f * fill);
            p.Rounds = 0;
        }
        else if (d.Role == PartRole.FuelTank && p.Burn > 0 && c.Fuel > 0.5f)
        {
            // A burning tank that gives way goes up with the fuel in it.
            var share = Math.Clamp(c.Fuel / MathF.Max(1, c.FuelCapacity()), 0, 1);
            radius = 3f + 1.5f * share;
            damage = 120f + 260f * share;
        }
        else
            return;
        if (CookOffCount < CookOffs.Length)
            CookOffs[CookOffCount++] = new CCookOff { At = at, Radius = radius, Damage = damage, Ticks = (int)(ConstructWeapons.CookOffDelay * World.TickRate), Body = c.Id };
        Events.Add($"construct {c.Id}: the {d.Name} cooks off");
    }

    /// <summary>A line per notable event (a part broke, a piece fell off): the game shows some, the self-test reads them.</summary>
    public readonly List<string> Events = new(32);

    public int TotalJoints
    {
        get
        {
            var n = 0;
            foreach (var c in Bodies)
                n += c.JointCount;
            return n;
        }
    }

    public int TotalWires
    {
        get
        {
            var n = 0;
            foreach (var c in Bodies)
                n += c.WireCount;
            return n;
        }
    }

    /// <summary>Routed links in all (the world's cap is the old wire cap).</summary>
    public int TotalRoutes
    {
        get
        {
            var n = 0;
            foreach (var c in Bodies)
                n += c.RouteCount;
            return n;
        }
    }

    public int TotalParts
    {
        get
        {
            var n = 0;
            foreach (var c in Bodies)
                n += c.PartCount;
            return n;
        }
    }

    // The parts in all for the safety cap: counted once a step (and on a body going), counted up as parts are added
    // (parts taken off lower it only at the next count: the cap errs on the safe side), so adding a part stays O(1).
    private int _partTally = -1;

    /// <summary>The parts in all as the safety cap counts them (exact after each step; see <see cref="TotalParts"/>).</summary>
    public int PartTally
    {
        get
        {
            if (_partTally < 0)
                _partTally = TotalParts;
            return _partTally;
        }
    }

    public Construct? Get(ushort id) => _byId.GetValueOrDefault(id);

    /// <summary>Server: where a player's hands are (the game sets it; null for a bare world): a held hose's receiver follows them.</summary>
    public Func<byte, Vector3?>? Hands;

    public void Clear()
    {
        foreach (var c in Bodies)
            Removed.Add(c.Id);
        Bodies.Clear();
        _partTally = -1;
        GridStale();
        _byId.Clear();
        StructureDirty.Clear();
        HealthDirty.Clear();
        ClearLinks();
        ClearWorldLinks();
        AmmoDirty.Clear();
        PaintDirty.Clear();
        Shattered.Clear();
        FireCount = 0;
        CookOffCount = 0;
        ClearStores();
    }

    /// <summary>A new empty body at a pose (null when the host's cap is reached or no id is free). Add parts, then it rebuilds on the next tick.</summary>
    public Construct? Create(Vector3 pos, Quaternion rot, ushort id = 0)
    {
        if (Bodies.Count >= BodyCap || Bodies.Count >= ushort.MaxValue - 1)
            return null;
        if (id == 0)
        {
            while (_byId.ContainsKey(_nextId) || _nextId == 0)
                _nextId++; // (wraps round past 65535 to the ids freed since)
            id = _nextId++;
        }
        else if (_byId.ContainsKey(id))
            return null;
        var c = new Construct { Id = id, Pos = pos, Rot = rot, PrevPos = pos, PrevRot = rot, NetPos = pos, NetFromPos = pos, NetRot = rot, NetFromRot = rot };
        Bodies.Add(c);
        _byId[id] = c;
        StructureDirty.Add(id);
        GridAdded(c);
        _blasting?.Add(c); // (a piece split off by a blast going off: it is in the blast too)
        return c;
    }

    public void Remove(Construct c)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Store != 0)
                FreeStore(ref c.Parts[i]); // (what a construct that goes holds goes with it; one being taken apart spills first)
        if (_byId.TryGetValue(c.Id, out var had) && ReferenceEquals(had, c) && _partTally >= 0)
            _partTally = Math.Max(0, _partTally - c.PartCount);
        Bodies.Remove(c);
        GridStale();
        _byId.Remove(c.Id);
        StructureDirty.Remove(c.Id);
        Removed.Add(c.Id);
    }

    /// <summary>Adds a part (no checks: the builder checks placements). Returns its index, or -1 when the caps are full.</summary>
    public int AddPart(Construct c, ConstructPartDef def, Vector3 pos, byte rot, byte seg = 0)
    {
        if (c.PartCount >= Construct.MaxParts || PartTally >= PartCap)
            return -1;
        _partTally++;
        GridGrew(c);
        var i = c.PartCount++;
        c.Parts[i] = new CPart { Def = def.Id, Pos = Quantise(pos), Rot = rot, Seg = seg, Health = def.Health, Setting = def.DefaultSetting, Tag = NextTag(), Rounds = (ushort)Math.Min(def.BoxRounds, ushort.MaxValue) };
        ConstructConfig.Init(ref c.Parts[i], def);
        AllocStore(ref c.Parts[i], def);
        c.Dirty = true;
        c.Wake();
        StructureDirty.Add(c.Id);
        if (def.Role == PartRole.FuelTank && c.PartCount > 1)
            c.AddFuel(def.FuelCapacity);
        if (def.Role == PartRole.Battery && c.PartCount > 1)
            c.Charge += def.BatteryCapacity; // (a new battery comes charged)
        return i;
    }

    /// <summary>Positions sit on a 1 cm grid, so what the server builds and what the wire carries are the same numbers.</summary>
    public static Vector3 Quantise(Vector3 v) => new(MathF.Round(v.X * 100) / 100, MathF.Round(v.Y * 100) / 100, MathF.Round(v.Z * 100) / 100);

    public bool AddLink(Construct c, int a, int sa, int b, int sb)
    {
        if (c.LinkCount >= Construct.MaxLinks)
            return false;
        c.Links[c.LinkCount++] = new CLink { A = (byte)a, B = (byte)b, SA = (byte)sa, SB = (byte)sb };
        c.Dirty = true;
        return true;
    }

    /// <summary>A joint on a joint part (a hinge, or a moving joint): the parts of a new segment turn (or slide) on it (returns the new segment, or 0 when full).</summary>
    public byte AddJoint(Construct c, int hinge)
    {
        if (c.JointCount >= Construct.MaxJoints || TotalJoints >= JointCap)
            return 0;
        ref var h = ref c.Parts[hinge];
        var d = h.D;
        c.Joints[c.JointCount] = new CJoint
        {
            Hinge = (byte)hinge,
            ParentSeg = h.Seg,
            Pivot = h.Pos + Rot24.Apply(h.Rot, d.JointPivot),
            Axis = ConstructParts.DirVector(Rot24.ApplyDir(h.Rot, d.JointAxis)),
            Min = d.JointMin,
            Max = d.JointMax,
            Type = d.JointType,
            Speed = d.MotorSpeed,
            Strength = d.MotorStrength,
        };
        c.JointCount++;
        return (byte)c.JointCount;
    }

    /// <summary>
    /// A wire from part <paramref name="from"/>'s output into part <paramref name="to"/> (call <see cref="ConstructBuild.CanWire"/>
    /// first): since the routed links, a data link routed straight between the two (the logic's wires are rebuilt from the
    /// data links). False when full.
    /// </summary>
    public bool AddWire(Construct c, int from, int to)
    {
        if (c.RouteCount >= Construct.MaxRoutes || TotalRoutes >= WireCap)
            return false;
        if (ConstructRoutes.Add(this, c, RouteType.Data, from, 255, 255, 0, default, default, to, 255) < 0)
            return false;
        c.Rebuild(); // (the wire is there at once, as it always was)
        return true;
    }

    /// <summary>Cuts every data link (wire) into or out of part <paramref name="part"/>. Returns how many went.</summary>
    public int RemoveWires(Construct c, int part)
    {
        var cut = ConstructRoutes.RemoveAt(this, c, part, RouteType.Data);
        if (cut > 0)
            c.Rebuild();
        return cut;
    }

    /// <summary>
    /// Takes part <paramref name="index"/> out of <paramref name="c"/> (broken off, or removed with a tool) and splits off
    /// every piece no longer joined to the root as a wreck of its own. Returns the removed part's type.
    /// </summary>
    public ConstructPartDef RemovePart(Construct c, int index)
    {
        if (c.DefOf(index).IsReceiverGun && c.Dirty)
            c.Rebuild(); // (the merges as they stand)
        if (c.DefOf(index).IsReceiverGun && c.MergeOf[index] != 0)
            index = ConstructMerge.Settle(this, c, index, index); // (a merged receiver's cell: the rest re-merge, its centre parts move or pop off)
        var def = c.DefOf(index);
        if (c.Parts[index].Store != 0)
            Spill(c, index); // (a container that goes spills what it holds: physical storage)
        var keepDriver = c.Driver;
        // Every part, by component: what still reaches the root (or part 0 when the root is the one going) stays.
        Span<int> comp = stackalloc int[Construct.MaxParts];
        for (var i = 0; i < c.PartCount; i++)
            comp[i] = -1;
        comp[index] = -2;
        var root = c.Root == index || c.Root >= c.PartCount ? -1 : c.Root;
        if (root < 0)
        {
            // The root is going: the new root is picked from what's left (a seat, a foundation, the first part).
            for (var i = 0; i < c.PartCount && root < 0; i++)
                if (i != index && c.Parts[i].D.Role is PartRole.Seat or PartRole.Foundation)
                    root = i;
            for (var i = 0; i < c.PartCount && root < 0; i++)
                if (i != index)
                    root = i;
        }
        var comps = 0;
        if (root >= 0)
            Flood(c, root, comps++, comp);
        for (var i = 0; i < c.PartCount; i++)
            if (comp[i] == -1)
                Flood(c, i, comps++, comp);
        // Pieces cut off become wrecks (a body each), in part order, so every run splits the same way.
        for (var k = 1; k < comps; k++)
            SplitOff(c, comp, k);
        // What's left: compact the arrays (parts of the removed part and the split pieces go).
        Compact(c, comp, 0);
        c.Dirty = true;
        c.Wake();
        StructureDirty.Add(c.Id);
        if (c.PartCount == 0)
            Remove(c);
        else if (keepDriver != 255 && c.Driver == 255)
            Events.Add($"driver {keepDriver} thrown out");
        return def;
    }

    private static void Flood(Construct c, int start, int id, Span<int> comp)
    {
        Span<byte> queue = stackalloc byte[Construct.MaxParts];
        int head = 0, tail = 0;
        comp[start] = id;
        queue[tail++] = (byte)start;
        while (head < tail)
        {
            int at = queue[head++];
            for (var k = 0; k < c.LinkCount; k++)
            {
                ref var l = ref c.Links[k];
                var other = l.A == at ? l.B : l.B == at ? l.A : -1;
                if (other < 0 || comp[other] != -1)
                    continue;
                comp[other] = id;
                queue[tail++] = (byte)other;
            }
            for (var j = 0; j < c.JointCount; j++)
            {
                // A freeform weld joins its anchor and its welded-on part (no socket link: board item fw01).
                ref var jt = ref c.Joints[j];
                if (!jt.Freeform || jt.Cut)
                    continue;
                var other = jt.Hinge == at ? jt.Child : jt.Child == at ? jt.Hinge : -1;
                if (other < 0 || other >= c.PartCount || comp[other] != -1)
                    continue;
                comp[other] = id;
                queue[tail++] = (byte)other;
            }
        }
    }

    /// <summary>Component <paramref name="k"/> of <paramref name="c"/> becomes a body of its own, moving as that part of the body moved.</summary>
    private Construct? SplitOff(Construct c, Span<int> comp, int k)
    {
        // Its frame: the segment none of its joints hang it from (its own hinge isn't in the piece), so its parts keep their turns.
        var baseSeg = -1;
        for (var i = 0; i < c.PartCount && baseSeg < 0; i++)
        {
            if (comp[i] != k)
                continue;
            baseSeg = BaseSeg(c, comp, k, c.Parts[i].Seg);
        }
        c.WorldFrame(baseSeg, out var q, out var t);
        var wreck = Create(t, q);
        if (wreck == null)
        {
            for (var i = 0; i < c.PartCount; i++)
                if (comp[i] == k && c.Parts[i].Store != 0)
                    FreeStore(ref c.Parts[i]);
            return null; // the caps are full: the piece is simply lost
        }
        // Copy the parts; segments other than the base keep their joints (when their hinge came along).
        Span<int> map = stackalloc int[Construct.MaxParts];
        Span<int> segMap = stackalloc int[Construct.MaxJoints + 1];
        for (var s = 0; s <= Construct.MaxJoints; s++)
            segMap[s] = -1;
        segMap[baseSeg] = 0;
        for (var j = 0; j < c.JointCount; j++)
            if (comp[c.Joints[j].Hinge] == k && wreck.JointCount < Construct.MaxJoints && !(c.Joints[j].Freeform && (c.Joints[j].Cut || comp[c.Joints[j].Child] != k)))
            {
                wreck.Joints[wreck.JointCount] = c.Joints[j];
                segMap[j + 1] = ++wreck.JointCount;
            }
        for (var i = 0; i < c.PartCount; i++)
        {
            map[i] = -1;
            if (comp[i] != k)
                continue;
            var seg = segMap[c.Parts[i].Seg];
            if (seg < 0)
                seg = 0;
            map[i] = wreck.PartCount;
            wreck.Parts[wreck.PartCount++] = c.Parts[i] with { Seg = (byte)seg };
        }
        for (var j = 0; j < wreck.JointCount; j++)
        {
            ref var jt = ref wreck.Joints[j];
            jt.Hinge = (byte)map[jt.Hinge];
            if (jt.Freeform)
                jt.Child = (byte)map[jt.Child];
            jt.ParentSeg = (byte)Math.Max(0, segMap[jt.ParentSeg]);
        }
        for (var l = 0; l < c.LinkCount; l++)
        {
            ref var link = ref c.Links[l];
            if (map[link.A] >= 0 && map[link.B] >= 0)
                AddLink(wreck, map[link.A], link.SA, map[link.B], link.SB);
        }
        ConstructRoutes.Remap(c, map[..c.PartCount], wreck); // (the routed links wholly on the piece go with it; the logic's wires follow from them)
        for (var i = 0; i < wreck.PartCount; i++)
            wreck.Parts[i].SitterPlus1 = 0; // (a seat that falls away throws its sitter out)
        wreck.Wreck = true;
        wreck.Owner = c.Owner;
        wreck.Damaged = true;
        wreck.Rebuild();
        // Moving as that bit of the old body moved.
        wreck.Vel = c.PointVel(wreck.WorldCom);
        wreck.AngVel = c.AngVel;
        wreck.Empty();
        Events.Add($"construct {c.Id}: a piece of {wreck.PartCount} part(s) fell away as wreck {wreck.Id}");
        return wreck;
    }

    /// <summary>A piece's frame: up from segment <paramref name="s"/> while the joint (or freeform weld) it hangs on came along with piece <paramref name="k"/>.</summary>
    private static int BaseSeg(Construct c, Span<int> comp, int k, int s)
    {
        for (var guard = 0; s > 0 && s <= c.JointCount && guard <= Construct.MaxJoints; guard++)
        {
            ref var jt = ref c.Joints[s - 1];
            if (comp[jt.Hinge] != k || jt.Freeform && (jt.Cut || jt.Child >= c.PartCount || comp[jt.Child] != k))
                break;
            s = jt.ParentSeg;
        }
        return s;
    }

    /// <summary>Keeps only the parts of component <paramref name="keep"/>, renumbering links, joints, the seat and the root.</summary>
    private static void Compact(Construct c, Span<int> comp, int keep)
    {
        // What stays may hang beyond a freeform weld that went (its anchor or the root came off that side): the body then
        // takes that segment's frame where it is, so its parts don't jump (board item fw01). Plain joints keep milestone 1's way.
        var rebase = false;
        var rebaseQ = Quaternion.Identity;
        var rebaseT = Vector3.Zero;
        for (var i = 0; i < c.PartCount; i++)
            if (comp[i] == keep)
            {
                var bs = BaseSeg(c, comp, keep, c.Parts[i].Seg);
                if (bs != 0 && c.FrameOf(bs) != 0)
                {
                    rebase = true;
                    c.WorldFrame(bs, out rebaseQ, out rebaseT);
                }
                break;
            }
        Span<int> map = stackalloc int[Construct.MaxParts];
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            map[i] = comp[i] == keep ? n : -1;
            if (comp[i] == keep)
                c.Parts[n++] = c.Parts[i];
        }
        var oldCount = c.PartCount;
        c.PartCount = n;
        var ln = 0;
        for (var l = 0; l < c.LinkCount; l++)
        {
            var link = c.Links[l];
            if (link.A >= oldCount || link.B >= oldCount || map[link.A] < 0 || map[link.B] < 0)
                continue;
            link.A = (byte)map[link.A];
            link.B = (byte)map[link.B];
            c.Links[ln++] = link;
        }
        c.LinkCount = ln;
        ConstructRoutes.Remap(c, map[..oldCount], c); // (routed links: renumbered, or gone with an end; the wires are rebuilt from them)
        // Joints whose hinge is gone: their segment's parts went with their own piece (or are gone too).
        Span<int> segMap = stackalloc int[Construct.MaxJoints + 1];
        segMap[0] = 0;
        var jn = 0;
        for (var j = 0; j < c.JointCount; j++)
        {
            var jt = c.Joints[j];
            if (jt.Freeform && !jt.Cut && (jt.Child >= oldCount || map[jt.Child] < 0))
            {
                // Its welded-on part went: another part still on that segment holds the weld (else it goes).
                for (var i = 0; i < oldCount; i++)
                    if (map[i] >= 0 && c.Parts[map[i]].Seg == j + 1)
                    {
                        jt.Child = (byte)i;
                        break;
                    }
            }
            if (jt.Freeform && jt.Cut)
                for (var i = 0; i < oldCount && jt.Cut; i++)
                    if (map[i] >= 0 && c.Parts[map[i]].Seg == j + 1 && map[jt.Hinge] >= 0)
                        jt.Cut = false; // (still joined another way: the weld stays)
            if (map[jt.Hinge] < 0 || jt.Freeform && (jt.Cut || jt.Child >= oldCount || map[jt.Child] < 0))
            {
                segMap[j + 1] = 0;
                continue;
            }
            jt.Hinge = (byte)map[jt.Hinge];
            if (jt.Freeform)
                jt.Child = (byte)map[jt.Child];
            jt.ParentSeg = (byte)segMap[jt.ParentSeg];
            c.Joints[jn] = jt;
            segMap[j + 1] = ++jn;
        }
        c.JointCount = jn;
        for (var i = 0; i < c.PartCount; i++)
            c.Parts[i].Seg = (byte)segMap[c.Parts[i].Seg];
        if (rebase)
        {
            c.Pos = c.PrevPos = c.NetPos = c.NetFromPos = rebaseT;
            c.Rot = c.PrevRot = c.NetRot = c.NetFromRot = rebaseQ;
        }
        if (c.DriverSeat >= 0)
        {
            c.DriverSeat = c.DriverSeat < oldCount ? map[c.DriverSeat] : -1;
            if (c.DriverSeat < 0)
                c.Driver = 255;
        }
    }

    // ---- Damage (decision 6: every part on its own) ------------------------------------------------------------

    /// <summary>
    /// Damage to one part: its armour takes a flat amount off first. At zero health it breaks off (and anything cut off
    /// from the root falls away as a wreck) or, for engines, tanks and seats, stops working. Returns true if it broke.
    /// </summary>
    public bool DamagePart(Construct c, int part, float damage, out float dealt, bool raw = false)
    {
        dealt = 0;
        if (part < 0 || part >= c.PartCount)
            return false;
        ref var p = ref c.Parts[part];
        var d = p.D;
        dealt = raw ? MathF.Max(0, damage) : MathF.Max(0, damage - d.Armour); // (raw: burning, which armour doesn't stop)
        if (dealt <= 0 || p.Broken)
            return false;
        p.Health -= dealt;
        c.Wake();
        if (p.Health > 0)
        {
            HealthDirty.Add((c.Id, (byte)part));
            if (d.DamagedTo >= 0 && p.Health < d.DamagedBelow * d.Health)
                LoseChunk(c, part);
            return false;
        }
        p.Health = 0;
        c.Damaged = true; // (disabled: anyone may salvage it now; ConstructEdits.BeginRemove)
        if (d.Role is PartRole.AmmoBox or PartRole.FuelTank)
            CookOff(c, part, ConstructRender.PartCentre(c, part)); // (a holed box, a burning tank: a beat later)
        if (p.Store != 0)
            Spill(c, part); // (a destroyed container spills what it holds)
        if (d.OnBreak == PartBreak.Disable)
        {
            p.Broken = true;
            c.Dirty = true;
            StructureDirty.Add(c.Id);
            Events.Add($"construct {c.Id}: {d.Name} wrecked (stops working)");
            if (d.Role == PartRole.FuelTank)
                c.Fuel = MathF.Min(c.Fuel, c.FuelCapacity() - 0); // (its fuel is lost)
            return true;
        }
        // The broken part itself falls away too, as wrecked debris (scrap to salvage later), moving as it was.
        c.WorldFrame(p.Seg, out var q, out var t);
        if (d.HasGlass)
        {
            // Glass shatters instead: nothing big falls, small shards burst out where it was (on every PC: Shattered).
            Events.Add($"construct {c.Id}: {d.Name} shattered");
            c.PartBounds(part, out var gmn, out var gmx);
            var gc = t + Vector3.Transform((gmn + gmx) * 0.5f, q);
            var gm = Matrix4x4.CreateFromQuaternion(q);
            var gh = (gmx - gmn) * 0.5f;
            var wh = new Vector3(
                MathF.Abs(gm.M11) * gh.X + MathF.Abs(gm.M21) * gh.Y + MathF.Abs(gm.M31) * gh.Z,
                MathF.Abs(gm.M12) * gh.X + MathF.Abs(gm.M22) * gh.Y + MathF.Abs(gm.M32) * gh.Z,
                MathF.Abs(gm.M13) * gh.X + MathF.Abs(gm.M23) * gh.Y + MathF.Abs(gm.M33) * gh.Z);
            if (Shattered.Count < 64)
                Shattered.Add((gc, wh));
            RemovePart(c, part);
            return true;
        }
        Events.Add($"construct {c.Id}: {d.Name} broke off");
        var debris = p with { Seg = 0, Broken = true, Health = 0, SitterPlus1 = 0, Store = 0 };
        c.PartBounds(part, out var mn, out var mx);
        var at = t + Vector3.Transform((mn + mx) * 0.5f, q);
        var vel = c.PointVel(at);
        var outward = at - c.WorldCom;
        outward.Y = 0;
        outward = outward.LengthSquared() > 1e-4f ? Vector3.Normalize(outward) * 2.5f : Vector3.Zero;
        var spin = c.AngVel;
        RemovePart(c, part);
        if (HasRoom() && Create(t, q) is { } piece)
        {
            piece.Parts[0] = debris;
            piece.PartCount = 1;
            piece.Wreck = true;
            piece.Owner = c.Owner;
            piece.Damaged = true;
            piece.Rebuild();
            piece.Vel = vel + outward + new Vector3(0, 2.5f, 0); // knocked off: out and up
            piece.AngVel = spin + new Vector3(1.3f, 0.4f, -0.9f);
        }
        return true;
    }

    /// <summary>A blast: every part within the radius takes damage falling off with distance (to its nearest point).</summary>
    public int Blast(Vector3 centre, float radius, float maxDamage)
    {
        var hits = 0;
        // Bodies can split while we go: walk by index, re-finding parts by distance each time. The pieces that split off
        // join the walk (Create adds them while a blast is going off), as they did when the walk was over every body.
        var near = Near(centre, radius + 1f, _blastNear);
        _blasting = near;
        for (var bi = 0; bi < near.Count; bi++)
        {
            var c = near[bi];
            if (Vector3.Distance(c.WorldCom, centre) > radius + c.Radius)
                continue;
            for (var guard = 0; guard < Construct.MaxParts; guard++)
            {
                // The next part not yet hit by this blast, nearest first (parts are removed as they break).
                var best = -1;
                var bestD = float.MaxValue;
                for (var i = 0; i < c.PartCount; i++)
                {
                    if (_blasted.Contains(((ulong)c.Id << 32) | (uint)Ident(c, i)))
                        continue;
                    var dist = PartDistance(c, i, centre);
                    if (dist < radius && dist < bestD)
                    {
                        bestD = dist;
                        best = i;
                    }
                }
                if (best < 0)
                    break;
                _blasted.Add(((ulong)c.Id << 32) | (uint)Ident(c, best));
                var k = 1f - bestD / radius;
                BlastStored(c, best, maxDamage * k * k); // (what is stored in it first: it may spill)
                DamagePart(c, best, maxDamage * k * k, out _);
                hits++;
                if (!IsLive(c))
                    break;
            }
        }
        _blasted.Clear();
        _blasting = null;
        // Routed links in range: each segment on its own (a blast through a fuel line lights it).
        for (var bi = 0; bi < near.Count; bi++)
        {
            var c = near[bi];
            if (IsLive(c) && c.RouteCount > 0 && Vector3.Distance(c.WorldCom, centre) <= radius + c.Radius)
                ConstructRoutes.Blast(this, c, centre, radius, maxDamage);
        }
        // The push: every body nearby gets an impulse away from the blast.
        for (var bi = 0; bi < near.Count; bi++)
        {
            var c = near[bi];
            if (c.Anchored || !IsLive(c))
                continue;
            var d = c.WorldCom - centre;
            var dist = d.Length();
            if (dist > radius + c.Radius * 0.5f || dist < 1e-3f)
                continue;
            var k = 1f - MathF.Min(1, dist / (radius + c.Radius * 0.5f));
            c.Wake();
            c.Vel += (d / dist + new Vector3(0, 0.5f, 0)) * k * maxDamage * 0.4f / MathF.Max(c.Mass, 20f) * 6f;
        }
        return hits;
    }

    private readonly HashSet<ulong> _blasted = new(256);

    // The broadphase's answers, one list per kind of query (they never nest), and the blast going off now (its walk).
    private readonly List<Construct> _rayNear = new(256), _blockNear = new(256), _blastNear = new(256);
    private List<Construct>? _blasting;

    /// <summary>Is the body still in the world (not removed since it was found)?</summary>
    public bool IsLive(Construct c) => _byId.TryGetValue(c.Id, out var live) && ReferenceEquals(live, c);

    // A part's identity during one blast (index plus its exact position: stable while other parts are removed).
    private static int Ident(Construct c, int i) => HashCode.Combine(c.Parts[i].Def, c.Parts[i].Pos, c.Parts[i].Rot);

    private static float PartDistance(Construct c, int i, Vector3 world)
    {
        c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
        var local = Vector3.Transform(world - t, Quaternion.Conjugate(q));
        c.PartBounds(i, out var mn, out var mx);
        var closest = Vector3.Clamp(local, mn, mx);
        return Vector3.Distance(closest, local);
    }

    // ---- Ray tests -------------------------------------------------------------------------------------------------

    /// <summary>The nearest construct part along a ray (before <paramref name="maxDist"/>). Uses the drawn pose on a client when <paramref name="drawn"/>.</summary>
    public bool Raycast(Vector3 origin, Vector3 dir, float maxDist, out ConstructHit hit, Construct? ignore = null)
    {
        hit = default;
        var best = maxDist;
        var found = false;
        var near = NearRay(origin, dir, maxDist, _rayNear); // (the broadphase: the bodies along the ray)
        for (var ni = 0; ni < near.Count; ni++)
        {
            var c = near[ni];
            if (c == ignore)
                continue;
            // Sphere round the body first.
            var oc = origin - c.WorldCom;
            var b = Vector3.Dot(oc, dir);
            var cc = oc.LengthSquared() - c.Radius * c.Radius;
            if (cc > 0 && b > 0)
                continue;
            if (b * b - cc < 0)
                continue;
            for (var i = 0; i < c.PartCount; i++)
            {
                ref var p = ref c.Parts[i];
                c.WorldFrame(p.Seg, out var q, out var t);
                var iq = Quaternion.Conjugate(q);
                var lo = Vector3.Transform(origin - t, iq);
                var ld = Vector3.Transform(dir, iq);
                var boxes = p.D.SolidBoxes;
                for (var bi = 0; bi < boxes; bi++)
                {
                    c.BoxOf(i, bi, out var centre, out var half);
                    if (Geometry.RayAabb(centre - half, centre + half, lo, ld, best) is { } d && d < best)
                    {
                        best = d;
                        found = true;
                        hit.Body = c;
                        hit.Part = i;
                        hit.Distance = d;
                        hit.Point = origin + dir * d;
                        hit.Normal = Vector3.Transform(Geometry.BoxEntryNormal(centre - half, centre + half, lo, ld, d), q);
                        hit.SlotPlus1 = 0;
                        hit.RoutePlus1 = 0;
                        hit.Glass = p.D.Boxes[bi].IsGlass; // (solid boxes come first: the same index)
                    }
                }
                if (p.Store != 0)
                {
                    // Physical storage: what is stored in it (and a bin's fill) is hit like a part of it.
                    var was = best;
                    var slot = RayStored(c, i, lo, ld, ref best);
                    if (slot >= -1 && best < was)
                    {
                        found = true;
                        hit.Body = c;
                        hit.Part = i;
                        hit.Distance = best;
                        hit.Point = origin + dir * best;
                        hit.Normal = -dir;
                        hit.SlotPlus1 = (byte)(slot + 1);
                        hit.RoutePlus1 = 0;
                        hit.Glass = false;
                    }
                }
            }
            // Routed links (cables, pipes, shafts, chutes on the outside): hit like parts, each segment on its own.
            if (c.RouteCount > 0 && ConstructRoutes.Ray(c, origin, dir, ref best, out var route, out var rseg, out var rp))
            {
                found = true;
                hit.Body = c;
                hit.Part = ConstructRoutes.HostOf(c, route, rseg);
                hit.Distance = best;
                hit.Point = rp;
                hit.Normal = -dir;
                hit.SlotPlus1 = 0;
                hit.RoutePlus1 = (byte)(route + 1);
                hit.RouteSeg = (byte)rseg;
            }
        }
        return found;
    }

    // ---- Players ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Keeps a player out of every construct: pushed out sideways, or stood on top when the feet are near a part's top
    /// (then carried along with it: the body's motion and any joint's under it, a lift, a turntable). Returns the body
    /// stood on, or null. Players never move constructs, except doors: a player walking into a door leaf swings it. On
    /// the server (<paramref name="server"/>) it also notes who stands where (pressure plates, the load on a joint) and
    /// the hardest push a moving part gave (<see cref="LastHitSpeed"/>: run-over damage, GameSandbox.cs).
    /// </summary>
    public Construct? BlockPlayer(ref MoveState s, float height, bool server, byte who = 255)
    {
        var pushDoors = server;
        Construct? standing = null;
        var standSeg = 0;
        LastHitSpeed = 0;
        LastHitBody = null;
        LastHitNormal = Vector3.Zero;
        var feet0 = s.Position.Y; // (a step up is measured from where the feet were: one step's rise a tick at most)
        var near = Near(s.Position, World.PlayerRadius + height + 1f, _blockNear); // (the broadphase: the bodies round the player)
        for (var bi = 0; bi < near.Count; bi++)
        {
            var c = near[bi];
            var reach = c.Radius + World.PlayerRadius + height;
            if (Vector3.DistanceSquared(s.Position, c.WorldCom) > reach * reach)
                continue;
            for (var i = 0; i < c.PartCount; i++)
            {
                ref var p = ref c.Parts[i];
                c.WorldFrame(p.Seg, out var q, out var t);
                var iq = Quaternion.Conjugate(q);
                var upLocal = Vector3.Transform(Vector3.UnitY, iq);
                var boxes = p.D.SolidBoxes;
                // Anchored builds and walkable parts (stairs, ramps, floors) are stepped up onto when the player's disc
                // reaches them, as the map's steps are; anything else only when the feet's centre is over it.
                var stepper = c.Anchored || p.D.Walk != WalkKind.None;
                var margin = stepper ? World.PlayerRadius + 0.02f : 0.05f; // (just past where the disc is pushed off: it steps up first)
                for (var bi2 = 0; bi2 < boxes; bi2++)
                {
                    c.BoxOf(i, bi2, out var centre, out var half);
                    // World bounds of the turned box.
                    var wc = t + Vector3.Transform(centre, q);
                    var m = Matrix4x4.CreateFromQuaternion(q);
                    var wh = new Vector3(
                        MathF.Abs(m.M11) * half.X + MathF.Abs(m.M21) * half.Y + MathF.Abs(m.M31) * half.Z,
                        MathF.Abs(m.M12) * half.X + MathF.Abs(m.M22) * half.Y + MathF.Abs(m.M32) * half.Z,
                        MathF.Abs(m.M13) * half.X + MathF.Abs(m.M23) * half.Y + MathF.Abs(m.M33) * half.Z);
                    var feet = s.Position;
                    if (feet.Y > wc.Y + wh.Y + 0.05f || feet.Y + height < wc.Y - wh.Y)
                        continue;
                    if (MathF.Abs(feet.X - wc.X) > wh.X + World.PlayerRadius || MathF.Abs(feet.Z - wc.Z) > wh.Z + World.PlayerRadius)
                        continue;
                    // In the box's frame: the closest point of the box to the player's axis, at feet and at a few heights.
                    var lf = Vector3.Transform(feet - t, iq);
                    var top = TopAt(lf, centre, half, upLocal, t, q, feet, margin);
                    if (top is { } topY && MathF.Min(feet.Y, stepper ? feet0 : feet.Y) >= topY - World.StepHeight && feet.Y <= topY + 0.06f && s.Velocity.Y <= 0.5f && upLocal.Y > 0.7f)
                    {
                        s.Position.Y = topY;
                        if (s.Velocity.Y < 0)
                            s.Velocity.Y = 0;
                        s.Grounded = true;
                        standing = c;
                        standSeg = p.Seg;
                        if (server)
                        {
                            if (p.D.Logic == CLogic.Plate)
                                p.Stood = true;
                            if (p.Seg > 0 && p.Seg <= Construct.MaxJoints)
                                c.RiderBits[p.Seg] |= 1UL << (who & 63);
                        }
                        continue;
                    }
                    if (wc.Y + wh.Y <= s.Position.Y + 0.06f)
                        continue; // (below the feet: walking over it, onto the next step or the next slab of a floor, never pushes)
                    var pushed = false;
                    for (var h = 0; h < 3 && !pushed; h++)
                    {
                        var probe = feet + new Vector3(0, 0.3f + h * (height - 0.5f) * 0.5f, 0);
                        var lp = Vector3.Transform(probe - t, iq);
                        var closest = Vector3.Clamp(lp, centre - half, centre + half);
                        var wclosest = t + Vector3.Transform(closest, q);
                        var dx = probe.X - wclosest.X;
                        var dz = probe.Z - wclosest.Z;
                        var d2 = dx * dx + dz * dz;
                        if (d2 >= World.PlayerRadius * World.PlayerRadius)
                            continue;
                        var inside = Vector3.DistanceSquared(closest, lp) < 1e-8f;
                        float nx, nz, depth;
                        if (inside || d2 < 1e-8f)
                        {
                            // Inside: out the nearest side, horizontally.
                            var rel = lp - centre;
                            var o = half - Vector3.Abs(rel);
                            var ln = o.X < o.Z ? new Vector3(MathF.Sign(rel.X) is var sx && sx == 0 ? 1 : sx, 0, 0) : new Vector3(0, 0, MathF.Sign(rel.Z) is var sz && sz == 0 ? 1 : sz);
                            var wn = Vector3.Transform(ln, q);
                            var len = MathF.Sqrt(wn.X * wn.X + wn.Z * wn.Z);
                            if (len < 1e-3f)
                                continue;
                            nx = wn.X / len;
                            nz = wn.Z / len;
                            depth = MathF.Min(o.X < o.Z ? o.X : o.Z, 0.5f) + World.PlayerRadius;
                        }
                        else
                        {
                            var d = MathF.Sqrt(d2);
                            nx = dx / d;
                            nz = dz / d;
                            depth = World.PlayerRadius - d;
                        }
                        s.Position.X += nx * depth;
                        s.Position.Z += nz * depth;
                        var vn = s.Velocity.X * nx + s.Velocity.Z * nz;
                        // A moving part (a car, a swinging arm) shoves the player along with it, and hits them as hard as it
                        // moves towards them: only the part's own speed at the contact counts, never the player's.
                        var pv = (c.Anchored || c.Asleep ? Vector3.Zero : c.PointVel(wclosest)) + c.SegPointVel(p.Seg, wclosest);
                        var pn = pv.X * nx + pv.Z * nz;
                        if (pn > LastHitSpeed)
                        {
                            LastHitSpeed = pn;
                            LastHitMass = c.Mass;
                            LastHitVel = pv;
                            LastHitNormal = new Vector3(nx, 0, nz);
                            LastHitBody = c;
                        }
                        if (vn < pn && pn > 0)
                        {
                            s.Velocity.X += (pn - vn) * nx;
                            s.Velocity.Z += (pn - vn) * nz;
                        }
                        else if (vn < 0)
                        {
                            s.Velocity.X -= vn * nx;
                            s.Velocity.Z -= vn * nz;
                        }
                        pushed = true;
                        if (pushDoors && p.Seg > 0)
                            PushJoint(c, p.Seg, wclosest, new Vector3(-nx, 0, -nz) * MathF.Max(1.5f, -vn));
                    }
                }
            }
        }
        if (standing != null)
        {
            // Carried along: the body's motion, and (across) any joint's under the feet; a rising lift lifts them by the stand above.
            var carry = standing.Anchored || standing.Asleep ? Vector3.Zero : standing.PointVel(s.Position);
            if (standSeg > 0)
                carry += standing.SegPointVel(standSeg, s.Position) * new Vector3(1, 0, 1);
            s.Position += carry * World.TickDt;
        }
        return standing;
    }

    /// <summary>The box's top surface height over the feet (world y), when the box is upright and the feet are over it.</summary>
    private static float? TopAt(Vector3 lf, Vector3 centre, Vector3 half, Vector3 upLocal, Vector3 t, Quaternion q, Vector3 feet, float margin = 0.05f)
    {
        // Upright enough boxes only: the box's local axis nearest world up.
        var ax = MathF.Abs(upLocal.X) > MathF.Abs(upLocal.Y) ? (MathF.Abs(upLocal.X) > MathF.Abs(upLocal.Z) ? 0 : 2) : (MathF.Abs(upLocal.Y) > MathF.Abs(upLocal.Z) ? 1 : 2);
        var sign = upLocal[ax] > 0 ? 1 : -1;
        if (MathF.Abs(upLocal[ax]) < 0.7f)
            return null;
        // Over the box (the feet's centre within the top face, a little inside the edge).
        for (var a = 0; a < 3; a++)
            if (a != ax && MathF.Abs(lf[a] - centre[a]) > half[a] + margin)
                return null;
        var topLocal = lf;
        topLocal[ax] = centre[ax] + sign * half[ax];
        return (t + Vector3.Transform(topLocal, q)).Y;
    }

    /// <summary>Wall chunks lost (the self-test reads it).</summary>
    public int ChunksLost;

    /// <summary>
    /// A part with damage stages ("damaged" in its file: a wall) dropped below a stage's share of its health: it turns
    /// into that stage in place (the same sockets, so every link stays; fewer boxes: a chunk is gone), and on through any
    /// later stage it is already below. The structure goes out again (the part's id is in it): no wire change.
    /// </summary>
    private void LoseChunk(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        for (var guard = 0; guard < 8; guard++)
        {
            var d = p.D;
            if (d.DamagedTo < 0 || p.Health >= d.DamagedBelow * d.Health)
                break;
            p.Def = (ushort)d.DamagedTo;
            ChunksLost++;
            Events.Add($"construct {c.Id}: {d.Name} lost a chunk");
        }
        c.Dirty = true;
        StructureDirty.Add(c.Id);
    }

    /// <summary>A push on a hinged segment at a world point: the joint picks up some swing.</summary>
    public static void PushJoint(Construct c, int seg, Vector3 at, Vector3 push)
    {
        if (seg <= 0 || seg > c.JointCount)
            return;
        ref var j = ref c.Joints[seg - 1];
        if (j.Motorised || j.Locked || j.Freeform)
            return; // a motor holds its joint, and a lock its hinge (a freeform weld has none)
        c.WorldFrame(j.ParentSeg, out var q, out var t);
        var axis = Vector3.Transform(j.Axis, q);
        var pivot = t + Vector3.Transform(j.Pivot, q);
        var r = at - pivot;
        var torque = Vector3.Dot(Vector3.Cross(r, push), axis);
        var lever = MathF.Max(0.3f, (r - axis * Vector3.Dot(r, axis)).Length());
        j.Vel = Math.Clamp(j.Vel + torque / (lever * lever) * 0.15f, -4f, 4f);
        j.Open = j.Angle + j.Vel * 0.3f is var guess && MathF.Abs(guess) > 0.4f; // a pushed door stays where it was pushed to
        c.Wake();
    }

    /// <summary>The ground under (x, z): the first thing a ray from high above hits.</summary>
    public static Vector3 Ground(IWorldQuery q, float x, float z)
    {
        const float top = 60f;
        var d = q.Raycast(new Vector3(x, top, z), -Vector3.UnitY, top + 20f, QueryFilter.All);
        return new Vector3(x, top - d, z);
    }

    /// <summary>A sandbox map's starting constructs and loose parts (server, at the start of a match).</summary>
    public void Populate(SandboxMap s, IWorldQuery q)
    {
        PowerRequired = s.PowerRequired;
        var spawned = new List<Construct?>(s.Constructs.Count);
        foreach (var sp in s.Constructs)
            spawned.Add(ConstructBuild.FindBlueprint(sp.Blueprint) is { } bp ? ConstructBuild.BuildOnGround(this, bp, Ground(q, sp.X, sp.Z), sp.Yaw, out _) : null);
        LinkSpawned(s, spawned); // (the map's couplings and wires between its constructs)
        foreach (var l in s.Loose)
            if (ConstructParts.Find(l.Part) is { } def)
                ConstructBuild.PlaceNew(this, def, Ground(q, l.X, l.Z), l.Yaw);
    }

    /// <summary>
    /// Debris expiry (server, every half second or so): a wreck (a piece cut off, or a part broken off) goes once no player
    /// has been within <paramref name="near"/> metres of it and nobody has been salvaging it for <paramref name="seconds"/>
    /// (0: never). Built constructs and loose parts set down by players never expire. Returns how many went.
    /// </summary>
    public int ExpireDebris(double now, float seconds, float near, ConstructEdits edits)
    {
        if (seconds <= 0)
            return 0;
        // Being salvaged (or anything else being taken off it): kept.
        var hands = edits.Hands;
        for (var pid = 0; pid < hands.Length; pid++)
            if (hands[pid].Edit == EditKind.Remove && Get(hands[pid].Body) is { } busy)
                busy.IdleSince = now;
        var gone = 0;
        var near2 = near * near;
        for (var i = Bodies.Count - 1; i >= 0; i--)
        {
            var c = Bodies[i];
            if (!c.Wreck)
                continue;
            if (c.IdleSince < 0)
                c.IdleSince = now;
            var com = c.WorldCom;
            var reach = near + c.Radius;
            for (var k = 0; k < PlayerSpotCount; k++)
                if (Vector3.DistanceSquared(PlayerSpots[k], com) <= MathF.Max(near2, reach * reach))
                {
                    c.IdleSince = now;
                    break;
                }
            if (now - c.IdleSince < seconds)
                continue;
            Events.Add($"wreck {c.Id} ({c.PartCount} part(s)) cleared away after {seconds:0} s alone");
            Remove(c);
            gone++;
        }
        return gone;
    }

    /// <summary>One tick: the links between constructs, power, the logic (every construct in list order, every part in index order), physics, then anything that fell out of the world goes.</summary>
    /// <summary>
    /// The strength model's optional overload damage (a joint part's "overloadDamage", 0 on every shipped part: the
    /// developer's open question): what a joint owes from being held past its rating is taken off its part, a whole hp at a
    /// time, raw (armour doesn't stop strain). One part a construct a tick, since a part that breaks changes its joints.
    /// </summary>
    private void OverloadDamage()
    {
        for (var i = Bodies.Count - 1; i >= 0; i--)
        {
            var c = Bodies[i];
            for (var j = 0; j < c.JointCount; j++)
            {
                ref var jt = ref c.Joints[j];
                if (jt.Strain < 1)
                    continue;
                var hp = MathF.Floor(jt.Strain);
                jt.Strain -= hp;
                DamagePart(c, jt.Hinge, hp, out _, raw: true);
                break;
            }
        }
    }

    public void Step(IWorldQuery query, float dt)
    {
        _partTally = -1; // (counted again when next asked)
        StepLinks(); // couplings and wires between constructs (ConstructLinks.cs): resolved, broken when apart, their values
        StepWorldLinks(query, dt, Hands); // world links and hoses (WorldLinks.cs, ConstructHoses.cs): resolved, snapped when a free construct moves, their flow
        foreach (var c in Bodies)
        {
            ConstructPower.Step(this, c, dt);
            ConstructRoutes.Step(this, c, dt); // (routed links: leaks, sparks, chutes)
            ConstructDrive.ShiftKeys(c); // (the driver's R and sprint, before the logic step clears the seats' keys)
        }
        ConstructWeapons.Step(this, query, dt); // (before the logic: a gun's wires carry last tick's values, its gunner's keys this tick's)
        FlushSnaps(); // (world links that tore off this tick: WorldLinks.cs)
        foreach (var c in Bodies)
            ConstructLogic.Step(this, c, dt);
        ConstructPhysics.Step(this, query, dt);
        OverloadDamage();
        ConstructCargo.Step(this); // (what frozen cargo volumes carry follows them; what entered is caught, what left is let go)
        for (var i = Bodies.Count - 1; i >= 0; i--)
            if (Bodies[i].Pos.Y < FallOutY)
            {
                Events.Add($"construct {Bodies[i].Id} fell out of the world");
                Remove(Bodies[i]);
            }
        GridMoved(); // (the awake bodies moved: the broadphase is rebuilt at its next query)
    }
}
