using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A coupling between two constructs (milestone 3): hitch part <see cref="PartA"/> of construct <see cref="BodyA"/> (a tow
/// ball or a chain hook) to hitch part <see cref="PartB"/> of <see cref="BodyB"/> (an eye). A tow ball is a free pivot
/// (the two coupling points are held together, any turn allowed); a hook is a chain of <see cref="Length"/> that only pulls.
/// The parts are found by their tags, so a coupling survives other parts coming off either construct.
/// </summary>
public struct CCoupling
{
    public ushort TagA, TagB; // the hitch parts' tags (server)
    public ushort BodyA, BodyB; // where they are now (resolved each tick; what the wire carries)
    public byte PartA, PartB;
    public float Length; // 0: a rigid free pivot; > 0: a chain this long
}

/// <summary>A wire between two constructs (milestone 3): part <see cref="From"/>'s output of <see cref="BodyFrom"/> into part <see cref="To"/> of <see cref="BodyTo"/>.</summary>
public struct CXWire
{
    public ushort TagFrom, TagTo; // server
    public ushort BodyFrom, BodyTo;
    public byte From, To;
    public float Value; // the source's output last tick (every wire carries last tick's value)
}

/// <summary>
/// The links between constructs (docs/sandbox/CONSTRUCTS.md, "Milestone 3"): couplings (a trailer on a tow ball, a
/// wrecking ball on a chain) and wires from one construct into another (a wall button wired to a gate). Both name their
/// parts by <see cref="CPart.Tag"/>, resolved each tick (cheaply: where they were last tick first), and break when a part
/// is gone or wrecked, or (a wire) when its ends are further apart than <see cref="XWireReach"/>, or (a coupling) when the
/// pieces were torn apart. Server-authoritative: a client gets the resolved list (<see cref="CBodyKind.Links"/>).
/// Fixed arrays: nothing allocates.
/// </summary>
public sealed partial class ConstructWorld
{
    public const int MaxCouplings = 32;
    public const int MaxXWires = 64;
    public const float XWireReach = 12f; // a wire between constructs reaches this far (part centre to part centre)
    public const float CoupleReach = 0.6f; // a ball and an eye couple when their points are this close
    public const float TearSlack = 1.5f; // a coupling pulled this far past its length tears (a teleport, a blast)

    public readonly CCoupling[] Couplings = new CCoupling[MaxCouplings];
    public int CouplingCount;
    public readonly CXWire[] XWires = new CXWire[MaxXWires];
    public int XWireCount;

    /// <summary>The links changed (made, broken, or a part renumbered): the server sends them again.</summary>
    public bool LinksDirty;

    /// <summary>The map's rule (its "sandbox" section's "power"): every construct needs a battery for its electrical parts.</summary>
    public bool PowerRequired;

    private ushort _nextTag = 1;

    private ushort NextTag()
    {
        if (_nextTag == 0)
            _nextTag = 1;
        return _nextTag++;
    }

    private void ClearLinks()
    {
        if (CouplingCount + XWireCount > 0)
            LinksDirty = true;
        CouplingCount = 0;
        XWireCount = 0;
    }

    /// <summary>The construct and index of the part with <paramref name="tag"/> (where it was last first); null when it's gone.</summary>
    private Construct? Resolve(ushort tag, ref ushort body, ref byte part)
    {
        if (tag == 0)
            return null;
        if (Get(body) is { } c && part < c.PartCount && c.Parts[part].Tag == tag)
            return c;
        foreach (var b in Bodies)
            for (var i = 0; i < b.PartCount; i++)
                if (b.Parts[i].Tag == tag)
                {
                    body = b.Id;
                    part = (byte)i;
                    return b;
                }
        return null;
    }

    /// <summary>A hitch's coupling point in the world (its segment as it is now).</summary>
    public static Vector3 HitchPoint(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out var t);
        return t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, p.D.HitchAt), q);
    }

    /// <summary>The coupling on a hitch part, or -1.</summary>
    public int CouplingOf(Construct c, int part)
    {
        for (var k = 0; k < CouplingCount; k++)
        {
            ref var cp = ref Couplings[k];
            if (cp.BodyA == c.Id && cp.PartA == part || cp.BodyB == c.Id && cp.PartB == part)
                return k;
        }
        return -1;
    }

    /// <summary>Are these two constructs coupled (their parts near the hitch don't collide)?</summary>
    public bool Coupled(ushort a, ushort b)
    {
        for (var k = 0; k < CouplingCount; k++)
            if (Couplings[k].BodyA == a && Couplings[k].BodyB == b || Couplings[k].BodyA == b && Couplings[k].BodyB == a)
                return true;
        return false;
    }

    /// <summary>Can hitch <paramref name="a"/> (a ball or a hook) couple to <paramref name="b"/> (an eye), either way round? Null, or why not.</summary>
    public string? CanCouple(Construct ca, int a, Construct cb, int b)
    {
        if (ca == cb)
            return "a construct can't be coupled to itself";
        var da = ca.DefOf(a);
        var db = cb.DefOf(b);
        if (da.Role != PartRole.Hitch || db.Role != PartRole.Hitch)
            return "couple a hitch to a hitch";
        if (ca.Parts[a].Broken || cb.Parts[b].Broken)
            return "that hitch is wrecked";
        if ((da.Hitch == HitchKind.Eye) == (db.Hitch == HitchKind.Eye))
            return da.Hitch == HitchKind.Eye ? "two eyes don't couple: an eye takes a ball or a hook" : "a ball or a hook couples to an eye";
        if (CouplingOf(ca, a) >= 0 || CouplingOf(cb, b) >= 0)
            return "that hitch is already coupled";
        if (CouplingCount >= MaxCouplings)
            return $"at most {MaxCouplings} couplings in the world";
        var chain = MathF.Max(da.HitchChain, db.HitchChain);
        if (Vector3.Distance(HitchPoint(ca, a), HitchPoint(cb, b)) > MathF.Max(CoupleReach, chain + 0.3f))
            return chain > 0 ? "the eye is out of the chain's reach" : "bring the ball and the eye together first";
        return null;
    }

    /// <summary>Couples two hitches (call <see cref="CanCouple"/> first; the ball or hook side becomes A).</summary>
    public bool Couple(Construct ca, int a, Construct cb, int b)
    {
        if (CouplingCount >= MaxCouplings)
            return false;
        if (ca.DefOf(a).Hitch == HitchKind.Eye)
            (ca, a, cb, b) = (cb, b, ca, a);
        Couplings[CouplingCount++] = new CCoupling
        {
            TagA = ca.Parts[a].Tag, TagB = cb.Parts[b].Tag, BodyA = ca.Id, BodyB = cb.Id, PartA = (byte)a, PartB = (byte)b,
            Length = MathF.Max(ca.DefOf(a).HitchChain, cb.DefOf(b).HitchChain),
        };
        ca.Wake();
        cb.Wake();
        LinksDirty = true;
        Events.Add($"construct {ca.Id} coupled to construct {cb.Id}");
        return true;
    }

    public void Decouple(int k, string why)
    {
        if (k < 0 || k >= CouplingCount)
            return;
        var cp = Couplings[k];
        Couplings[k] = Couplings[--CouplingCount];
        Get(cp.BodyA)?.Wake();
        Get(cp.BodyB)?.Wake();
        LinksDirty = true;
        Events.Add($"construct {cp.BodyA} decoupled from construct {cp.BodyB}: {why}");
    }

    /// <summary>The nearest free hitch on another construct that <paramref name="part"/> of <paramref name="c"/> could couple to, or -1.</summary>
    public int NearestHitch(Construct c, int part, out Construct? other)
    {
        other = null;
        var best = -1;
        var bestD = float.MaxValue;
        var at = HitchPoint(c, part);
        foreach (var b in Bodies)
        {
            if (b == c || Vector3.Distance(b.WorldCom, at) > b.Radius + 9f)
                continue;
            for (var i = 0; i < b.PartCount; i++)
            {
                if (b.DefOf(i).Role != PartRole.Hitch || CanCouple(c, part, b, i) != null)
                    continue;
                var d = Vector3.Distance(HitchPoint(b, i), at);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                    other = b;
                }
            }
        }
        return best;
    }

    // ---- Wires between constructs -------------------------------------------------------------------------------------

    /// <summary>How many wires come into part <paramref name="part"/> of <paramref name="c"/> from other constructs.</summary>
    public int XWiresInto(Construct c, int part)
    {
        var n = 0;
        for (var k = 0; k < XWireCount; k++)
            if (XWires[k].BodyTo == c.Id && XWires[k].To == part)
                n++;
        return n;
    }

    /// <summary>The linking tool's rules for a wire from one construct into another. Null, or why not.</summary>
    public string? CanCrossWire(Construct cf, int from, Construct ct, int to)
    {
        if (from < 0 || from >= cf.PartCount || to < 0 || to >= ct.PartCount)
            return "no such part";
        var fd = cf.DefOf(from);
        var td = ct.DefOf(to);
        if (cf.Parts[from].Broken || ct.Parts[to].Broken)
            return "that part is wrecked";
        if (!fd.HasOutput)
            return $"a {fd.Name} has no output (wire from a seat, a control, a sensor or a gate)";
        if (td.MaxInputs == 0)
            return $"a {td.Name} takes no input";
        if (td.Logic == CLogic.Reader)
            return "an input reader reads a seat of its own construct";
        for (var k = 0; k < XWireCount; k++)
            if (XWires[k].BodyFrom == cf.Id && XWires[k].From == from && XWires[k].BodyTo == ct.Id && XWires[k].To == to)
                return "those are already wired";
        if (ct.WiresInto(to) + XWiresInto(ct, to) >= td.MaxInputs)
            return $"a {td.Name} takes at most {td.MaxInputs} input{(td.MaxInputs == 1 ? "" : "s")}";
        if (Vector3.Distance(ConstructRender.PartCentre(cf, from), ConstructRender.PartCentre(ct, to)) > XWireReach)
            return $"a wire between constructs reaches at most {XWireReach:0} m";
        if (XWireCount >= MaxXWires)
            return $"at most {MaxXWires} wires between constructs";
        return null;
    }

    public bool AddXWire(Construct cf, int from, Construct ct, int to)
    {
        if (XWireCount >= MaxXWires)
            return false;
        XWires[XWireCount++] = new CXWire
        {
            TagFrom = cf.Parts[from].Tag, TagTo = ct.Parts[to].Tag, BodyFrom = cf.Id, BodyTo = ct.Id, From = (byte)from, To = (byte)to,
            Value = cf.Parts[from].Out,
        };
        ct.Wake();
        LinksDirty = true;
        return true;
    }

    /// <summary>Cuts every wire between constructs into or out of a part. Returns how many went.</summary>
    public int RemoveXWires(Construct c, int part)
    {
        var cut = 0;
        for (var k = XWireCount - 1; k >= 0; k--)
        {
            ref var x = ref XWires[k];
            if (x.BodyFrom == c.Id && x.From == part || x.BodyTo == c.Id && x.To == part)
            {
                Get(x.BodyTo)?.Wake();
                XWires[k] = XWires[--XWireCount];
                cut++;
            }
        }
        if (cut > 0)
            LinksDirty = true;
        return cut;
    }

    // ---- Each tick ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Server, every tick before the logic: every coupling and cross wire finds its parts again (they may have been
    /// renumbered or moved to a wreck), breaks when a part is gone or wrecked or the ends are too far apart, and a wire
    /// takes its source's output as it was last tick. Coupled constructs wake each other, and one towed by a driven
    /// construct lets go of its parking brake.
    /// </summary>
    private void StepLinks()
    {
        foreach (var c in Bodies)
        {
            c.Towed = false;
            c.XWireIn = 0;
        }
        for (var k = CouplingCount - 1; k >= 0; k--)
        {
            ref var cp = ref Couplings[k];
            var (ba, pa, bb, pb) = (cp.BodyA, cp.PartA, cp.BodyB, cp.PartB);
            var ca = Resolve(cp.TagA, ref cp.BodyA, ref cp.PartA);
            var cb = Resolve(cp.TagB, ref cp.BodyB, ref cp.PartB);
            if (ba != cp.BodyA || pa != cp.PartA || bb != cp.BodyB || pb != cp.PartB)
                LinksDirty = true;
            if (ca == null || cb == null || ca.Parts[cp.PartA].Broken || cb.Parts[cp.PartB].Broken)
            {
                Decouple(k, "a hitch is gone");
                continue;
            }
            if (ca == cb)
            {
                Decouple(k, "both ends on one construct");
                continue;
            }
            if (Vector3.Distance(HitchPoint(ca, cp.PartA), HitchPoint(cb, cp.PartB)) > cp.Length + TearSlack + MathF.Max(CoupleReach, cp.Length > 0 ? 0.3f : 0f))
            {
                Decouple(k, "torn apart");
                continue;
            }
            // Wake each other (a driven buggy pulls its sleeping trailer), and a towed construct's parking brake lets go.
            if (Moving(ca) && cb.Asleep)
                cb.Wake();
            if (Moving(cb) && ca.Asleep)
                ca.Wake();
            if (Drives(ca))
                cb.Towed = true;
            if (Drives(cb))
                ca.Towed = true;
        }
        for (var k = XWireCount - 1; k >= 0; k--)
        {
            ref var x = ref XWires[k];
            var (bf, pf, bt, pt) = (x.BodyFrom, x.From, x.BodyTo, x.To);
            var cf = Resolve(x.TagFrom, ref x.BodyFrom, ref x.From);
            var ct = Resolve(x.TagTo, ref x.BodyTo, ref x.To);
            if (bf != x.BodyFrom || pf != x.From || bt != x.BodyTo || pt != x.To)
                LinksDirty = true;
            if (cf == null || ct == null || cf.Parts[x.From].Broken || ct.Parts[x.To].Broken)
            {
                Events.Add($"a wire from construct {x.BodyFrom} to construct {x.BodyTo} broke: a part is gone");
                Drop(k, ct);
                continue;
            }
            if (Vector3.Distance(ConstructRender.PartCentre(cf, x.From), ConstructRender.PartCentre(ct, x.To)) > XWireReach)
            {
                Events.Add($"a wire from construct {x.BodyFrom} to construct {x.BodyTo} broke: out of range");
                Drop(k, ct);
                continue;
            }
            var v = cf.Parts[x.From].Out;
            if (v != x.Value)
                ct.Wake();
            x.Value = v;
            ct.XWireIn++;
        }
    }

    private void Drop(int k, Construct? to)
    {
        XWires[k] = XWires[--XWireCount];
        to?.Wake();
        LinksDirty = true;
    }

    private static bool Moving(Construct c) =>
        !c.Asleep && !c.Anchored && (c.Vel.LengthSquared() >= ConstructPhysics.StillSpeed2 || c.AngVel.LengthSquared() >= ConstructPhysics.StillSpeed2 || c.Driver != 255 && c.Throttle != 0);

    private static bool Drives(Construct c) => c.CanDrive && (c.Driver != 255 || c.EngineWired) && (!c.Electrical || c.EngineRunning);

    /// <summary>
    /// The map's couplings and wires between the constructs it starts with (its "sandbox" section): construct index and
    /// blueprint step at each end. A coupled construct is moved so its eye meets the ball (a trailer behind its buggy).
    /// </summary>
    public void LinkSpawned(SandboxMap s, List<Construct?> spawned)
    {
        foreach (var (a, sa, b, sb) in s.Couplings)
        {
            if (Spawned(spawned, a, sa) is not { } ca || Spawned(spawned, b, sb) is not { } cb)
                continue;
            ca.Rebuild();
            cb.Rebuild();
            var move = HitchPoint(ca, sa) - HitchPoint(cb, sb);
            cb.Pos += move;
            cb.PrevPos = cb.NetPos = cb.NetFromPos = cb.Pos;
            if (CanCouple(ca, sa, cb, sb) is { } why)
                Events.Add($"the map's coupling {a}:{sa} - {b}:{sb} didn't couple: {why}");
            else
                Couple(ca, sa, cb, sb);
        }
        foreach (var (a, sa, b, sb) in s.Wires)
        {
            if (Spawned(spawned, a, sa) is not { } ca || Spawned(spawned, b, sb) is not { } cb)
                continue;
            if (CanCrossWire(ca, sa, cb, sb) is { } why)
                Events.Add($"the map's wire {a}:{sa} -> {b}:{sb} wasn't made: {why}");
            else
                AddXWire(ca, sa, cb, sb);
        }
    }

    private static Construct? Spawned(List<Construct?> spawned, int index, int part) =>
        index >= 0 && index < spawned.Count && spawned[index] is { } c && part >= 0 && part < c.PartCount ? c : null;
}
