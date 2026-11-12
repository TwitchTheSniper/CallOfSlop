using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace CallOfSlop.VehicleKit;

/// <summary>Thrown when a vehicle can't be built as asked: the message names the part, the place and the rule.</summary>
public sealed class VehicleKitException(string message) : Exception(message);

/// <summary>A part already on the vehicle: its blueprint step, its index on the construct being built and its file.</summary>
public sealed record VPart(int Step, int Index, ConstructPartDef Def, int Seg)
{
    public string Name => Def.Name;
}

/// <summary>Which way round a part has to end up. Each rule names a direction of the part and where it must point in the vehicle (x right, y up, -z forward).</summary>
public readonly record struct Orient(Vector3 OwnA, Vector3 WantA, Vector3 OwnB = default, Vector3 WantB = default)
{
    /// <summary>Any turn at all.</summary>
    public static readonly Orient Any = default;

    /// <summary>The part stands the right way up and faces forward: seats, guns, boxes with a front.</summary>
    public static readonly Orient Forward = new(Vector3.UnitY, Vector3.UnitY, -Vector3.UnitZ, -Vector3.UnitZ);

    /// <summary>Upright, any heading.</summary>
    public static readonly Orient Upright = new(Vector3.UnitY, Vector3.UnitY);

    /// <summary>A beam (its length is its own z) laid along <paramref name="axis"/>, its top up when that isn't its length.</summary>
    public static Orient Along(Vector3 axis) => new(Vector3.UnitZ, axis);

    /// <summary>A plate (its face is its own +y) with its face towards <paramref name="face"/>.</summary>
    public static Orient Facing(Vector3 face) => new(Vector3.UnitY, face);

    public bool Allows(byte rot)
    {
        if (OwnA != default && Vector3.Dot(Rot24.Apply(rot, OwnA), WantA) < 0.99f)
            return false;
        return OwnB == default || Vector3.Dot(Rot24.Apply(rot, OwnB), WantB) >= 0.99f;
    }
}

/// <summary>
/// The kit's bottom layer: a blueprint written step by step against a live construct, through the game's own rules. Every
/// step goes through <see cref="ConstructBuild.Place"/>, <see cref="ConstructBuild.CanWire"/> and
/// <see cref="ConstructRoutes.BuildLink"/> as it is added, so what can't be built throws at once with the game's reason,
/// and nothing here works sockets out by itself: <see cref="Put"/> searches the free sockets for the place asked for,
/// <see cref="Onto"/> takes a socket by name. <see cref="Text"/> writes the ordinary blueprint file.
/// Places are in vehicle coordinates: x right, y up, -z forward, the origin where <see cref="Root"/> says.
/// </summary>
public class BlueprintScript
{
    /// <summary>How far from the wanted centre a part may land before <see cref="Put"/> refuses (sockets are on a 0.25 m grid).</summary>
    public const float DefaultReach = 0.07f;

    protected readonly ConstructWorld World;
    protected readonly Construct C;
    private readonly List<Step> _steps = new();
    private readonly List<(int From, int To)> _wires = new();
    private readonly List<(int Reel, int Inlet)> _hoses = new();
    private readonly List<string> _links = new();
    private readonly List<string> _cargo = new();
    private readonly List<(int Step, string Table)> _loot = new();
    private readonly List<(int Step, int Slot, string Part, int Count)> _stored = new();
    private readonly List<(Vector3 Min, Vector3 Max)> _cargoBoxes = new();
    private Vector3 _origin;

    public string Name { get; internal set; } // (set again only for a family's mark, named for its model: VehicleFamilies.cs)
    public string Description { get; set; } = "";
    public string Group { get; set; } = "";

    /// <summary>
    /// Which kit made it (<see cref="VehicleKitVersion"/>) and, from the shape grammar on, the modules it chose ("nose long hood", "body room+bed"...).
    /// A file from kit 2 or later carries both in a comment line after its first brace (blueprints allow comments); kit 1's text is as it always was.
    /// </summary>
    public int Kit { get; set; } = VehicleKitVersion.Legacy;

    public List<string> Modules { get; } = new();

    /// <summary>Kit 4: the role, tier and shape and how the vehicle met the role (VehicleRoles.cs), written before <see cref="Modules"/> in the kit line; empty before kit 4.</summary>
    public List<string> KitNotes { get; } = new();

    /// <summary>The spawn context's line for the blueprint's comments (a <see cref="Vehicle"/>'s; "" for none).</summary>
    protected virtual string ContextLine() => "";

    public int Count => _steps.Count;

    /// <summary>The live construct the steps are built on (read it; change it only through the steps).</summary>
    public Construct Live => C;
    public IReadOnlyList<VPart> Parts => _parts;
    private readonly List<VPart> _parts = new();

    private sealed class Step
    {
        public string Part = "";
        public int On = -1, Spin;
        public string Socket = "", By = "";
        public string? Weld, Config;
        public uint Paint;
        public string? Finish; // (a material's name, Materials/Surfaces.json: the texture paint tool's; the interior builder's look, INTERIORS.md phase 12)
        public string[]? Text; // (a sign's messages, "text")
    }

    private readonly List<string> _bill = new();

    public BlueprintScript(string name)
    {
        Name = name;
        World = new ConstructWorld();
        C = World.Create(Vector3.Zero, Quaternion.Identity) ?? throw new VehicleKitException("no room for a construct");
    }

    protected static ConstructPartDef Def(string name) =>
        ConstructParts.Find(name) ?? throw new VehicleKitException($"no construct part named \"{name}\" (--check-construct-parts lists what loaded)");

    protected Vector3 ToRest(Vector3 at) => at - _origin;

    protected Vector3 FromRest(Vector3 rest) => rest + _origin;

    /// <summary>The first part: its bounds' centre at <paramref name="at"/>. Every later place is measured from the same origin.</summary>
    public VPart Root(string part, Vector3 at, Orient o = default)
    {
        if (_steps.Count > 0)
            throw new VehicleKitException("a vehicle has one root");
        var def = Def(part);
        var turn = FirstTurn(o);
        _origin = at - Rot24.Apply(turn, (def.BoundsMin + def.BoundsMax) * 0.5f);
        var i = World.AddPart(C, def, Vector3.Zero, turn);
        if (i < 0)
            throw new VehicleKitException($"the {part} won't start a construct");
        _steps.Add(new Step { Part = part, Spin = turn });
        return Added(i, def);
    }

    private VPart Added(int index, ConstructPartDef def)
    {
        var v = new VPart(_steps.Count - 1, index, def, C.Parts[index].Seg);
        _parts.Add(v);
        return v;
    }

    /// <summary>Fits <paramref name="part"/> by name: onto <paramref name="socket"/> of <paramref name="on"/>, by its own socket <paramref name="by"/> (null: the snap build's pick), turned by <paramref name="spin"/> (null: smart rotation's).
    /// <paramref name="clear"/>: also refused where a face of it would be coplanar with a part already there (<see cref="VehicleChecks.ZFightsWith"/>), for dressing that can go elsewhere.</summary>
    public VPart Onto(VPart on, string socket, string part, string? by = null, int? spin = null, bool clear = false, Func<ConstructPartDef, ConstructGhost, string?>? veto = null)
    {
        var def = Def(part);
        var onto = KitProfile.Time(KitProfile.S.Onto);
        var target = C.DefOf(on.Index);
        var ts = Array.FindIndex(target.Sockets, s => s.Name == socket);
        if (ts < 0)
            throw new VehicleKitException($"a {target.Name} has no socket \"{socket}\" ({string.Join(" ", target.Sockets.Select(s => s.Name))})");
        int ds;
        if (by != null)
        {
            ds = Array.FindIndex(def.Sockets, s => s.Name == by);
            if (ds < 0)
                throw new VehicleKitException($"a {def.Name} has no socket \"{by}\" ({string.Join(" ", def.Sockets.Select(s => s.Name))})");
        }
        else if (SnapBuild.Fit(C, def, on.Index, ts, -1, out var fit))
            ds = fit.DefSocket;
        else
            throw new VehicleKitException($"a {def.Name} doesn't fit on the {target.Name}'s \"{socket}\"");
        VehicleGenJob.Yield(); // (the fit and the turn above, the placing below: spans of their own)
        var sp = spin ?? Math.Max(0, SmartRotation.BestSpin(C, on.Index, ts, def, ds));
        VehicleGenJob.Yield();
        if (ConstructBuild.Place(C, on.Index, ts, def, ds, sp, out var g) is { } bad)
            throw new VehicleKitException($"a {def.Name} on the {target.Name}'s \"{socket}\": {(bad == ConstructBuild.InTheWay ? $"the {Blocker(def, g.Pos, g.Rot, g.Seg, on.Index)} is in the way" : bad)}");
        if (clear)
            VehicleGenJob.Yield();
        if (clear && VehicleChecks.ZFightsWith(C, def, g) is { } fight)
            throw new VehicleKitException($"a {def.Name} on the {target.Name}'s \"{socket}\": z-fighting: {fight}");
        if (veto?.Invoke(def, g) is { } no)
            throw new VehicleKitException($"a {def.Name} on the {target.Name}'s \"{socket}\": {no}");
        onto.Dispose();
        return Commit(on.Index, on.Step, ts, def, ds, sp, g);
    }

    /// <summary>
    /// A gun part on a merged receiver's centre socket <paramref name="socket"/> (the receiver's own socket of that name, N times out: ConstructMerge.cs), by its own
    /// <paramref name="by"/>. The blueprint names it "merged <paramref name="socket"/>".
    /// </summary>
    public VPart OntoCentre(VPart lead, string socket, string part, string by, int spin = 0)
    {
        var def = Def(part);
        var own = C.DefOf(lead.Index).Sockets;
        var k = Array.FindIndex(own, s => s.Name == socket);
        var ds = Array.FindIndex(def.Sockets, s => s.Name == by);
        if (!C.MergeLead(lead.Index) || k < 0 || ds < 0)
            throw new VehicleKitException($"a {def.Name} on the merged {lead.Name}'s \"{socket}\": {(C.MergeLead(lead.Index) ? "no such socket" : "it isn't a merged receiver's lead")}");
        var ts = own.Length + k;
        if (ConstructBuild.Place(C, lead.Index, ts, def, ds, spin, out var g) is { } bad)
            throw new VehicleKitException($"a {def.Name} on the merged {lead.Name}'s \"{socket}\": {(bad == ConstructBuild.InTheWay ? $"the {Blocker(def, g.Pos, g.Rot, g.Seg)} is in the way" : bad)}");
        return Commit(lead.Index, lead.Step, ts, def, ds, spin, g);
    }

    /// <summary>The turn <paramref name="part"/> would take on <paramref name="on"/>'s <paramref name="socket"/> by its <paramref name="by"/> at <paramref name="spin"/>, or null when the rules refuse it there: to choose a spin before fitting.</summary>
    public byte? TurnOf(VPart on, string socket, string part, string by, int spin)
    {
        var def = Def(part);
        var ts = Array.FindIndex(C.DefOf(on.Index).Sockets, s => s.Name == socket);
        var ds = Array.FindIndex(def.Sockets, s => s.Name == by);
        return ts < 0 || ds < 0 || ConstructBuild.Place(C, on.Index, ts, def, ds, spin, out var g) != null ? null : g.Rot;
    }

    /// <summary>The turn a part already on has.</summary>
    public byte TurnOf(VPart p) => C.Parts[p.Index].Rot;

    private VPart Commit(int onIndex, int onStep, int ts, ConstructPartDef def, int ds, int spin, in ConstructGhost g)
    {
        int i;
        using (KitProfile.Time(KitProfile.S.Attach))
            i = ConstructBuild.Attach(World, C, def, g);
        if (i < 0)
            throw new VehicleKitException($"a {def.Name}: too many parts");
        VehicleGenJob.Yield();
        using (KitProfile.Time(KitProfile.S.Rebuild))
            C.Rebuild();
        VehicleGenJob.Yield(); // (a spawn in the game hands back to the tick here: VehicleSpawn.cs)
        _steps.Add(new Step { Part = def.Name, On = onStep, Socket = ConstructMerge.SocketName(C, onIndex, ts), By = def.Sockets[ds].Name, Spin = spin });
        return Added(i, def);
    }

    /// <summary>
    /// Fits <paramref name="part"/> with the centre of its bounds at <paramref name="centre"/> and turned as <paramref name="o"/> says, on
    /// whichever free socket puts it nearest (within <paramref name="reach"/> metres): of <paramref name="on"/> when given, else of the vehicle's body.
    /// <paramref name="onTop"/>: only sockets facing up (furniture on a floor, never hung on a wall). <paramref name="fit"/>: half sizes it must take up (0 on an axis: any), which settles a long plate's way round. Refused with the nearest try and its reason.
    /// </summary>
    public VPart Put(string part, Vector3 centre, Orient o = default, float reach = DefaultReach, VPart? on = null, string? by = null, Vector3 fit = default, bool onTop = false)
    {
        var def = Def(part);
        var scan = KitProfile.Time(KitProfile.S.PutScan);
        var want = ToRest(centre);
        var mid = (def.BoundsMin + def.BoundsMax) * 0.5f;
        var halfSize = (def.BoundsMax - def.BoundsMin) * 0.5f;
        var bestD = float.MaxValue;
        (int I, int S, int Own, int Spin) best = (-1, 0, 0, 0);
        var nearD = float.MaxValue;
        (int I, int S, int Own, int Spin, Vector3 Pos, byte Turn, bool Tried, string? Why) nearest = (-1, 0, 0, 0, default, 0, false, null);
        Span<byte> turns = stackalloc byte[4];
        // Only a socket within this of the wanted centre can put the part's centre within reach + 0.5 (the farthest any try is
        // looked at): the centre is the socket plus the part's own arm from its socket to its centre, turned, then rounded to the
        // centimetre. So sockets and whole parts farther than that are passed over unread, in the same order, which
        // changes nothing a Put picks or says (VEHICLEKIT.md, "Generation time").
        var arm = 0f;
        for (var ds = 0; ds < def.Sockets.Length; ds++)
            if (by == null || def.Sockets[ds].Name == by)
                arm = MathF.Max(arm, Vector3.Distance(mid, def.Sockets[ds].At));
        var near = reach + 0.5f + arm + 0.05f;
        for (var i = on?.Index ?? 0; i < (on == null ? C.PartCount : on.Index + 1); i++)
        {
            if (on == null && C.Parts[i].Seg != 0)
                continue;
            var td = C.DefOf(i);
            if (Vector3.Distance(C.Parts[i].Pos, want) > near + SocketRadius(td) * C.ScaleOf(i) && !C.MergeLead(i))
                continue;
            VehicleGenJob.Yield();
            var used = UsedSockets(i);
            for (var s = 0; s < C.SocketCount(i); s++)
            {
                if (s >= td.Sockets.Length || used[s])
                    continue;
                C.SocketAt(i, s, out var at, out var dir);
                if (Vector3.Distance(at, want) > near)
                    continue;
                if (onTop && ConstructParts.DirVector(dir).Y < 0.9f)
                    continue;
                for (var ds = 0; ds < def.Sockets.Length; ds++)
                {
                    if (by != null && def.Sockets[ds].Name != by)
                        continue;
                    if (!ConstructBuild.KindsMate(td.Sockets[s].Kind, def.Sockets[ds].Kind, ConstructBuild.OnLeaf(C, i, s)))
                        continue;
                    var n = ConstructBuild.SpinTurns(def.Sockets[ds].Dir, dir, turns);
                    for (var sp = 0; sp < n; sp++)
                    {
                        if (!o.Allows(turns[sp]) || !Fits(Rot24.ApplyHalf(turns[sp], halfSize), fit))
                            continue;
                        var pos = ConstructWorld.Quantise(at - Rot24.Apply(turns[sp], def.Sockets[ds].At));
                        var d = Vector3.Distance(pos + Rot24.Apply(turns[sp], mid), want);
                        if (d >= bestD - 1e-4f || d > reach + 0.5f)
                            continue;
                        if (d > reach)
                        {
                            // (out of reach: it can only be the refusal's nearest try, so the rules are asked only if a refusal is written)
                            if (d < nearD)
                                (nearD, nearest) = (d, (i, s, ds, sp, pos, turns[sp], false, null));
                            continue;
                        }
                        VehicleGenJob.Yield();
                        string? why;
                        using (KitProfile.Time(KitProfile.S.PutTry))
                            why = ConstructBuild.Place(C, i, s, def, ds, sp, out _);
                        if (why == null)
                        {
                            bestD = d;
                            best = (i, s, ds, sp);
                        }
                        else if (d < nearD)
                            (nearD, nearest) = (d, (i, s, ds, sp, pos, turns[sp], true, why));
                    }
                }
            }
        }
        scan.Dispose();
        if (best.I < 0)
        {
            // The refusal, written only now (the nearest try's reason and blocker cost a pass over every part's boxes); the construct hasn't changed since the scan.
            var nearWhy = "no free socket on it";
            if (nearest.I >= 0)
            {
                var why = nearest.Tried ? nearest.Why : ConstructBuild.Place(C, nearest.I, nearest.S, def, nearest.Own, nearest.Spin, out _);
                nearWhy = why == ConstructBuild.InTheWay ? $"nearest try {nearD:0.00} m off: the {Blocker(def, nearest.Pos, nearest.Turn)} is in the way" : why ?? $"{nearD:0.00} m from where it was asked for, the nearest a socket puts it";
            }
            throw new VehicleKitException($"{Name}: no socket takes a {def.Name} at {Show(centre)}: {nearWhy}");
        }
        VehicleGenJob.Yield();
        ConstructBuild.Place(C, best.I, best.S, def, best.Own, best.Spin, out var g);
        return Commit(best.I, _parts.First(p => p.Index == best.I).Step, best.S, def, best.Own, best.Spin, g);
    }

    private bool[] _used = new bool[64];

    /// <summary>
    /// Which of part <paramref name="i"/>'s sockets are taken (as <see cref="Construct.SocketUsed"/> says, socket by socket), from one walk of its links: a lofted bay has
    /// hundreds of sockets and links, and asking socket by socket walked every link for every socket on every <see cref="Put"/>. Valid till the next call.
    /// </summary>
    private ReadOnlySpan<bool> UsedSockets(int i)
    {
        var n = C.SocketCount(i);
        if (_used.Length < n)
            _used = new bool[Math.Max(n, _used.Length * 2)];
        Array.Clear(_used, 0, n);
        for (var e = C.FirstLink(i); e >= 0; e = C.NextLink(e))
        {
            ref var l = ref C.Links[e >> 1];
            var (part, sock) = (e & 1) == 0 ? (l.A, l.SA) : (l.B, l.SB);
            if (part == i && (uint)sock < (uint)n)
                _used[sock] = true;
        }
        return _used.AsSpan(0, n);
    }

    private static readonly ConditionalWeakTable<ConstructPartDef, StrongBox<float>> SocketRadii = new();

    /// <summary>How far a part's farthest socket is from its origin (unscaled): <see cref="Put"/> passes over parts too far for any of theirs to matter.</summary>
    private static float SocketRadius(ConstructPartDef d) =>
        SocketRadii.GetValue(d, static d =>
        {
            var r = 0f;
            foreach (var k in d.Sockets)
                r = MathF.Max(r, k.At.Length());
            return new StrongBox<float>(r);
        }).Value;

    /// <summary>
    /// <see cref="Put"/> by a socket: the part goes where its own socket <paramref name="by"/> lands on <paramref name="at"/> (a seat's feet, an engine's mount), on <paramref name="on"/>.
    /// </summary>
    public VPart Stand(string part, Vector3 at, Orient o, VPart? on, string by, float reach = 0.03f)
    {
        var def = Def(part);
        var own = Array.FindIndex(def.Sockets, s => s.Name == by);
        if (own < 0)
            throw new VehicleKitException($"a {def.Name} has no socket \"{by}\" ({string.Join(" ", def.Sockets.Select(s => s.Name))})");
        var turn = FirstTurn(o);
        return Put(part, at + Rot24.Apply(turn, (def.BoundsMin + def.BoundsMax) * 0.5f - def.Sockets[own].At), o, reach, on, by);
    }

    /// <summary>What a part there would run into, for the refusal (<paramref name="seg"/>: the segment frame <paramref name="pos"/> is in; boxes are compared at rest, axis-aligned).</summary>
    private string Blocker(ConstructPartDef def, Vector3 pos, byte rot, byte seg = 0, int host = -1)
    {
        C.RestPose(seg, out var q0, out var t0);
        for (var b = 0; b < def.SolidBoxes; b++)
        {
            var bc = t0 + Vector3.Transform(pos + Rot24.Apply(rot, def.Boxes[b].Center), q0);
            var bh = Rot24.ApplyHalf(rot, def.Boxes[b].Half) - new Vector3(0.01f);
            for (var pass = 0; pass < 2; pass++) // (the part it goes on last: its own hooks touch what hangs on them)
            for (var i = 0; i < C.PartCount; i++)
            for (var k = 0; k < C.DefOf(i).SolidBoxes; k++)
            {
                if ((i == host) != (pass == 1))
                    continue;
                C.BoxOf(i, k, out var oc, out var oh);
                C.RestPose(C.Parts[i].Seg, out var q1, out var t1);
                oc = t1 + Vector3.Transform(oc, q1);
                var gap = Vector3.Abs(bc - oc) - bh - oh;
                if (gap.X < 0 && gap.Y < 0 && gap.Z < 0)
                    return $"{C.DefOf(i).Name} at {Show(FromRest(oc))} (its box {Show(oh * 2)}, against this part's {Show(bh * 2 + new Vector3(0.02f))} at {Show(FromRest(bc))})";
            }
        }
        return "something";
    }

    /// <summary>A half size against one asked for, where 0 is "any" on that axis.</summary>
    private static bool Fits(Vector3 half, Vector3 want) =>
        (want.X == 0 || MathF.Abs(half.X - want.X) < 0.04f) && (want.Y == 0 || MathF.Abs(half.Y - want.Y) < 0.04f) && (want.Z == 0 || MathF.Abs(half.Z - want.Z) < 0.04f);

    /// <summary>The first turn a rule allows.</summary>
    protected static byte FirstTurn(Orient o)
    {
        for (var r = 0; r < Rot24.Count; r++)
            if (o.Allows((byte)r))
                return (byte)r;
        return 0;
    }

    /// <summary>
    /// A part welded on off the grid (a blueprint's freeform step, board item fw01): its bounds' centre at <paramref name="centre"/>,
    /// turned <paramref name="o"/> and then tilted by <paramref name="pitch"/>, <paramref name="yaw"/>, <paramref name="roll"/> degrees. <paramref name="on"/> is on the body.
    /// </summary>
    public VPart Weld(string part, VPart on, Vector3 centre, Orient o, double pitch, double yaw, double roll)
    {
        var def = Def(part);
        if (on.Seg != 0)
            throw new VehicleKitException($"a {def.Name} is welded onto the body, and the {on.Name} is on a joint");
        var turn = FirstTurn(o);
        // (four decimals in the file: rounded here, so this copy and the file are the same pose)
        var rot = Quaternion.Normalize(RoundQ(Quaternion.CreateFromYawPitchRoll((float)(yaw * Math.PI / 180), (float)(pitch * Math.PI / 180), (float)(roll * Math.PI / 180)) * Rot24.Quat(turn)));
        var pos = RoundV(ToRest(centre) - Vector3.Transform((def.BoundsMin + def.BoundsMax) * 0.5f, rot));
        using var weld = KitProfile.Time(KitProfile.S.Weld);
        var i = World.AddFreeform(C, on.Index, def, pos, rot);
        if (i < 0)
            throw new VehicleKitException($"{Name}: a {def.Name} won't weld on: at most {Freeform.MaxPerConstruct} freeform welds a construct");
        VehicleGenJob.Yield();
        C.Rebuild();
        VehicleGenJob.Yield();
        _steps.Add(new Step
        {
            Part = def.Name,
            On = on.Step,
            Weld = string.Create(CultureInfo.InvariantCulture, $"{{\"on\": {on.Step}, \"pos\": [{N(pos.X)}, {N(pos.Y)}, {N(pos.Z)}], \"rot\": [{Q(rot.X)}, {Q(rot.Y)}, {Q(rot.Z)}, {Q(rot.W)}]}}"),
        });
        return Added(i, def);
    }

    private static Quaternion RoundQ(Quaternion q) => new(MathF.Round(q.X, 4), MathF.Round(q.Y, 4), MathF.Round(q.Z, 4), MathF.Round(q.W, 4));

    private static Vector3 RoundV(Vector3 v) => new(MathF.Round(v.X, 3), MathF.Round(v.Y, 3), MathF.Round(v.Z, 3));

    /// <summary>A wire (a data link), through the linking tool's rules.</summary>
    public void Wire(VPart from, VPart to)
    {
        using var wire = KitProfile.Time(KitProfile.S.Wire);
        if (ConstructBuild.CanWire(World, C, from.Index, to.Index) is { } bad)
            throw new VehicleKitException($"a wire {from.Name} -> {to.Name}: {bad}");
        VehicleGenJob.Yield();
        World.AddWire(C, from.Index, to.Index);
        C.Rebuild();
        VehicleGenJob.Yield();
        _wires.Add((from.Step, to.Step));
    }

    /// <summary>An ammo reel's belt plugged into its socket, as built.</summary>
    public void Hose(VPart reel, VPart inlet)
    {
        World.PlugHose(C, reel.Index, C, inlet.Index);
        C.Rebuild();
        VehicleGenJob.Yield();
        _hoses.Add((reel.Step, inlet.Step));
    }

    /// <summary>A routed link: "pipe", "shaft", "power" or "data", through the linking tool's rules. <paramref name="nodes"/>: the way it goes (vehicle coordinates); none: straight. <paramref name="laid"/>: only its ends and lengths are checked, so it may pass through plates.</summary>
    public void Link(string type, VPart from, VPart to, string? fromPort = null, string? toPort = null, string? paint = null, bool laid = false, params Vector3[] nodes) =>
        Link(type, from, to, fromPort, toPort, paint, laid, null, nodes);

    /// <summary>A routed link as above; <paramref name="size"/>: a conveyor's width and height in metres (null: as wide as both ends take).</summary>
    public void Link(string type, VPart from, VPart to, string? fromPort, string? toPort, string? paint, bool laid, Vector2? size, params Vector3[] nodes)
    {
        using var link = KitProfile.Time(KitProfile.S.Link);
        var text = new StringBuilder($"{{\"type\": \"{type}\", \"from\": {from.Step}, \"to\": {to.Step}");
        if (fromPort != null)
            text.Append($", \"fromPort\": \"{fromPort}\"");
        if (toPort != null)
            text.Append($", \"toPort\": \"{toPort}\"");
        if (nodes.Length > 0)
            text.Append(", \"nodes\": [").Append(string.Join(", ", nodes.Select(n => { var r = ToRest(n); return $"[{N(r.X)}, {N(r.Y)}, {N(r.Z)}]"; }))).Append(']');
        if (laid)
            text.Append(", \"laid\": true");
        if (size is { } sz)
            text.Append($", \"size\": [{N(sz.X)}, {N(sz.Y)}]");
        if (paint != null)
            text.Append($", \"paint\": \"{paint}\"");
        text.Append('}');
        using var doc = JsonDocument.Parse(text.ToString());
        var spec = ConstructRoutes.ParseLink(doc.RootElement, Name, _links.Count);
        var partOf = new int[_steps.Count];
        foreach (var p in _parts)
            partOf[p.Step] = p.Index;
        VehicleGenJob.Yield();
        if (ConstructRoutes.BuildLink(World, C, spec, partOf) is { } bad)
            throw new VehicleKitException($"a {type} link {from.Name} -> {to.Name}: {bad}");
        for (var k = 0; k < C.RouteCount; k++)
            C.Routes[k].Cursor = 0;
        VehicleGenJob.Yield();
        C.Rebuild();
        VehicleGenJob.Yield();
        _links.Add(text.ToString());
    }

    /// <summary>A step's paint; a part with nothing to paint just carries it.</summary>
    public void Paint(VPart p, string colour)
    {
        if (!ConstructPaint.TryParse(colour, out var paint))
            throw new VehicleKitException($"\"{colour}\" isn't a colour like \"#c04020\"");
        _steps[p.Step].Paint = paint;
        C.Parts[p.Index].Paint = paint;
    }

    /// <summary>A step's texture (the texture paint tool's finish): a material of Materials/Surfaces.json by name, which every box of the part but glass and glow takes.</summary>
    public void Finish(VPart p, string material)
    {
        var row = ConstructFinish.Of(material);
        if (row == 0)
            throw new VehicleKitException($"\"{material}\" isn't a material the texture paint tool offers (Materials/Surfaces.json)");
        _steps[p.Step].Finish = material;
        C.Parts[p.Index].Finish = row;
    }

    /// <summary>A sign's messages (a Text Sign's "text": ASCII, up to 60 characters each).</summary>
    public void Text(VPart p, params string[] messages)
    {
        if (messages.Any(m => m.Length > LogicText.MaxChars || m.Any(ch => ch < 32 || ch > 126)))
            throw new VehicleKitException($"a sign's message is plain ASCII, up to {LogicText.MaxChars} characters: \"{messages.First(m => m.Length > LogicText.MaxChars || m.Any(ch => ch < 32 || ch > 126))}\"");
        _steps[p.Step].Text = messages;
        for (var k = 0; k < messages.Length && k < p.Def.TextSlots; k++)
            LogicText.Set(ref C.Parts[p.Index], k, messages[k]);
    }

    /// <summary>A line on a Line Console's bill as it is built (the blueprint's "bill": an order, never material): a piece by its name and how many.</summary>
    public void Bill(VPart console, string piece, int count)
    {
        if (!ProductionLines.IsConsole(console.Def))
            throw new VehicleKitException($"a bill goes on a Line Console, and a {console.Name} isn't one");
        if (Extrusion.ParseKey(piece) is not { Growing: false } key)
            throw new VehicleKitException($"\"{piece}\" isn't a piece's name");
        if (WorkOrders.AddToBill(World, C, console.Index, new WorkOrder { Key = key, Part = ConstructParts.None, Count = (ushort)count }) is { } why)
            throw new VehicleKitException($"the bill: {why}");
        _bill.Add($"{{\"step\": {console.Step}, \"piece\": {Quote(piece)}, \"count\": {count}}}");
    }

    /// <summary>A catalogue part's line on a Line Console's bill (an Assembler's order: WorkOrders.cs) by its name and how many.</summary>
    public void BillPart(VPart console, string part, int count)
    {
        if (!ProductionLines.IsConsole(console.Def))
            throw new VehicleKitException($"a bill goes on a Line Console, and a {console.Name} isn't one");
        var d = ConstructParts.Find(part) ?? throw new VehicleKitException($"no part named \"{part}\"");
        if (WorkOrders.AddToBill(World, C, console.Index, new WorkOrder { Part = (ushort)d.Id, Count = (ushort)count }) is { } why)
            throw new VehicleKitException($"the bill: {why}");
        _bill.Add($"{{\"step\": {console.Step}, \"part\": {Quote(d.Name)}, \"count\": {count}}}");
    }

    /// <summary>A formed panel's line on a Line Console's bill (a Panel Former's order: ConstructPanelFormer.cs) by its name, which is its key, and how many.</summary>
    public void BillPanel(VPart console, string panel, int count)
    {
        if (!ProductionLines.IsConsole(console.Def))
            throw new VehicleKitException($"a bill goes on a Line Console, and a {console.Name} isn't one");
        if (!HullLoft.IsFormedName(panel) || ConstructParts.Find(panel) == null)
            throw new VehicleKitException($"\"{panel}\" isn't a panel a Panel Former makes");
        if (WorkOrders.AddToBill(World, C, console.Index, ConstructPanelFormer.Order(panel, count)) is { } why)
            throw new VehicleKitException($"the bill: {why}");
        _bill.Add($"{{\"step\": {console.Step}, \"panel\": {Quote(panel)}, \"count\": {count}}}");
    }

    /// <summary>A step's settings by key (the configure tool's): the values are written as JSON.</summary>
    public void Config(VPart p, params (string Key, object Value)[] values) =>
        _steps[p.Step].Config = "{" + string.Join(", ", values.Select(v => $"\"{v.Key}\": {JsonValue(v.Value)}")) + "}";

    /// <summary>A container that fills from a loot table (Constructs/Loot.json) each time the blueprint is spawned; nothing is fixed in the file.</summary>
    public void Loot(VPart container, string table)
    {
        if (C.DefOf(container.Index).Slots.Length == 0)
            throw new VehicleKitException($"loot goes in a container with slots, and a {container.Name} has none");
        if (!ConstructLoot.Tables.ContainsKey(table))
            throw new VehicleKitException($"no loot table \"{table}\" in Constructs/Loot.json");
        _loot.Add((container.Step, table));
    }

    /// <summary>How many containers fill from a loot table, and how many loose cargo pieces ride aboard (VehicleRoles.cs measures a role's loot by these).</summary>
    public int LootSpots => _loot.Count;

    public int CargoPieces => _cargo.Count;

    /// <summary>The loot tables its containers fill from, one a container.</summary>
    public IEnumerable<string> LootTables => _loot.Select(l => l.Table);

    /// <summary>Every loot container's table passed through <paramref name="map"/> (kit 4's loot tiers: "bandit" to "bandit 3"); a table the file doesn't have is refused.</summary>
    public void RetableLoot(Func<string, string> map)
    {
        for (var i = 0; i < _loot.Count; i++)
        {
            var t = map(_loot[i].Table);
            if (!ConstructLoot.Tables.ContainsKey(t))
                throw new VehicleKitException($"no loot table \"{t}\" in Constructs/Loot.json");
            _loot[i] = (_loot[i].Step, t);
        }
    }

    /// <summary>A loose part riding in a frozen Cargo Volume, its origin at <paramref name="at"/>. It stays a body of its own: shot off, stolen or salvaged like anything loose.</summary>
    public void Cargo(string part, Vector3 at, int turn = 0)
    {
        var def = Def(part);
        var r = ToRest(at);
        _cargo.Add($"{{\"part\": \"{part}\", \"at\": [{N(r.X)}, {N(r.Y)}, {N(r.Z)}], \"turn\": {turn}}}");
        var (lo, hi) = TurnedBounds(def, turn);
        _cargoBoxes.Add((at + lo, at + hi)); // (where it stands, in vehicle coordinates: VehicleCargo.cs lays more round it)
        CargoMass += def.Mass;
    }

    /// <summary>A part's bounds turned by Rot24 turn <paramref name="turn"/>, about its origin.</summary>
    public static (Vector3 Min, Vector3 Max) TurnedBounds(ConstructPartDef def, int turn)
    {
        var (lo, hi) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        for (var k = 0; k < 8; k++)
        {
            var corner = new Vector3((k & 1) != 0 ? def.BoundsMax.X : def.BoundsMin.X, (k & 2) != 0 ? def.BoundsMax.Y : def.BoundsMin.Y, (k & 4) != 0 ? def.BoundsMax.Z : def.BoundsMin.Z);
            var t = Rot24.Apply((byte)turn, corner);
            (lo, hi) = (Vector3.Min(lo, t), Vector3.Max(hi, t));
        }
        return (lo, hi);
    }

    /// <summary>The loose cargo's boxes so far, in vehicle coordinates, and what it weighs (kg).</summary>
    public IReadOnlyList<(Vector3 Min, Vector3 Max)> CargoBoxes => _cargoBoxes;

    public float CargoMass { get; private set; }

    /// <summary>
    /// Parts set in a container's slot when the vehicle is spawned, before its loot rolls into the rest (a blueprint's <c>"stored"</c>: what its crew stowed, the same
    /// every spawn). The caller has checked the slot takes them (<see cref="ConstructStorage.Fits"/>); an item only.
    /// </summary>
    public void Store(VPart container, int slot, string part, int count)
    {
        var def = Def(part);
        if (slot < 0 || slot >= C.DefOf(container.Index).Slots.Length)
            throw new VehicleKitException($"a {container.Name} has no slot {slot}");
        if (def.PhysicsOnly)
            throw new VehicleKitException($"the {part} is physics-only: it rides as cargo, not in a slot");
        _stored.Add((container.Step, slot, part, count));
    }

    /// <summary>What is stowed in slots: step, slot, part, count.</summary>
    public IReadOnlyList<(int Step, int Slot, string Part, int Count)> StoredParts => _stored;

    /// <summary>The steps of the containers that fill from a loot table.</summary>
    public IEnumerable<int> LootSteps => _loot.Select(l => l.Step);

    private static string JsonValue(object v) => v switch
    {
        bool b => b ? "true" : "false",
        string s => $"\"{s}\"",
        double d => N(d),
        float f => N(f),
        int i => i.ToString(CultureInfo.InvariantCulture),
        _ => throw new VehicleKitException($"a setting of type {v.GetType().Name}"),
    };

    protected static string N(double v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);

    private static string Q(double v) => Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture);

    protected static string Show(Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"[{v.X:0.00}, {v.Y:0.00}, {v.Z:0.00}]");

    /// <summary>The blueprint file. One step a line, so a change reads as a change; the same script always writes the same text.</summary>
    /// <summary>The blueprint spawned coupled behind this one: its first eye on this one's first ball.</summary>
    public string Tow { get; set; } = "";

    public string Text()
    {
        using var time = KitProfile.Time(KitProfile.S.Text);
        var sb = new StringBuilder();
        sb.Append('{');
        if (Kit > VehicleKitVersion.Legacy)
            sb.Append("\n  // kit ").Append(Kit).Append(KitNotes.Count + Modules.Count > 0 ? "; " + string.Join(", ", KitNotes.Concat(Modules)) : "");
        if (ContextLine() is { Length: > 0 } context)
            sb.Append("\n  // context ").Append(context); // (the spawn context it was made in: VehicleContext.cs; none in a neutral one)
        sb.Append("\n  \"format\": \"").Append(ConstructBuild.BlueprintFormat).Append("\",\n  \"name\": ").Append(Quote(Name)).Append(",\n");
        if (Group.Length > 0)
            sb.Append("  \"group\": ").Append(Quote(Group)).Append(",\n");
        sb.Append("  \"description\": ").Append(Quote(Description)).Append(",\n  \"steps\": [\n");
        for (var k = 0; k < _steps.Count; k++)
        {
            if (k % 64 == 63)
                VehicleGenJob.Yield(); // (a big vehicle's text is a millisecond or two)
            var s = _steps[k];
            sb.Append("    {\"part\": ").Append(Quote(s.Part));
            if (s.Weld != null)
                sb.Append(", \"weld\": ").Append(s.Weld);
            else if (k > 0)
                sb.Append($", \"on\": {s.On}, \"socket\": {Quote(s.Socket)}, \"by\": {Quote(s.By)}");
            if (s.Spin != 0)
                sb.Append($", \"spin\": {s.Spin}");
            if (s.Config != null)
                sb.Append(", \"config\": ").Append(s.Config);
            if (s.Paint != 0)
                sb.Append(", \"paint\": ").Append(Quote(ConstructPaint.ToHex(s.Paint)));
            if (s.Finish != null)
                sb.Append(", \"finish\": ").Append(Quote(s.Finish));
            if (s.Text != null)
                sb.Append(", \"text\": [").Append(string.Join(", ", s.Text.Select(Quote))).Append(']');
            sb.Append(k + 1 < _steps.Count ? "},\n" : "}\n");
        }
        sb.Append("  ]");
        if (_wires.Count > 0)
            sb.Append(",\n  \"wires\": [").Append(string.Join(", ", _wires.Select(w => $"[{w.From}, {w.To}]"))).Append(']');
        if (_hoses.Count > 0)
            sb.Append(",\n  \"hoses\": [").Append(string.Join(", ", _hoses.Select(h => $"[{h.Reel}, {h.Inlet}]"))).Append(']');
        if (_links.Count > 0)
            sb.Append(",\n  \"links\": [\n    ").Append(string.Join(",\n    ", _links)).Append("\n  ]");
        if (_cargo.Count > 0)
            sb.Append(",\n  \"cargo\": [\n    ").Append(string.Join(",\n    ", _cargo)).Append("\n  ]");
        if (Tow.Length > 0)
            sb.Append(",\n  \"tow\": ").Append(Quote(Tow));
        if (_loot.Count > 0)
            sb.Append(",\n  \"loot\": [").Append(string.Join(", ", _loot.Select(l => $"{{\"step\": {l.Step}, \"table\": {Quote(l.Table)}}}"))).Append(']');
        if (_stored.Count > 0)
            sb.Append(",\n  \"stored\": [\n    ").Append(string.Join(",\n    ", _stored.Select(x => $"{{\"step\": {x.Step}, \"slot\": {x.Slot}, \"part\": {Quote(x.Part)}, \"count\": {x.Count}}}"))).Append("\n  ]");
        if (_bill.Count > 0)
            sb.Append(",\n  \"bill\": [\n    ").Append(string.Join(",\n    ", _bill)).Append("\n  ]");
        sb.Append("\n}\n");
        return sb.ToString();
    }

    private static string Quote(string s) => $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
