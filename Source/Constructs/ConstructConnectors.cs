using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a connector does (the attach tool's Rope and Spring modes; docs/sandbox/CONSTRUCTS.md, "Ropes and springs"). Wire and save byte: new kinds go at the end.</summary>
public enum ConnectorKind : byte
{
    Rope, // keeps its two points within a length: slack below it, no push
    Spring, // pushes and pulls towards a rest length; a stiffness of 0 makes it a plain damper
    COUNT,
}

/// <summary>
/// One end of a connector: a point on a part (<see cref="Body"/> not 0: <see cref="Local"/> is in the part's own frame, so it
/// survives the part being renumbered or split off) or on the world (<see cref="Body"/> 0: <see cref="Local"/> is a world position).
/// </summary>
public struct ConnectorEnd
{
    public ushort Tag; // server: the host part's tag (0: the world)
    public ushort Body; // where it is now (0: the world)
    public ushort Part;
    public bool OnWreck; // server: it was tied to a part already wrecked, so a wrecked part doesn't end it
    public Vector3 Local;
    public Vector3 Support; // server, a world end: the way to the surface it sits on (zero: none found; the connector goes when the surface does)
    public byte Player; // on a player (id + 1): a harpoon's launcher or head (ConstructHarpoon.cs). Body is 0 and each PC keeps Local on them (never saved)

    public readonly bool OnWorld => Body == 0;
}

/// <summary>
/// A free constraint between two points (docs/sandbox/CONSTRUCTS.md, "Ropes and springs"): one kind of record for ropes and
/// springs, in one list on the world (<see cref="ConstructWorld.Connectors"/>), whether its ends are on two constructs, one
/// construct across a joint, or a construct and the world. It is a row in the contact solve (ConstructPhysics), never a segment.
/// </summary>
public struct CConnector
{
    public ushort Id; // stable while it lives: what a patch and an edit name it by
    public ConnectorKind Kind;
    public byte Size; // 0..3: thickness, rating and default stiffness (ConstructWorld.ConnectorSizes)
    public ConnectorEnd A, B;
    public float Length; // a rope: the most its ends may be apart; a spring: its rest length
    public float Rating; // a rope: what it holds (N) before it snaps
    public float Stiffness; // a spring: N/m
    public float Damping; // a spring: N s/m
    // Server, not on the wire:
    public float Load; // the smoothed pull this tick (N)
    public int Row; // its row in this tick's solve + 1 (0: none)
    public Vector3 LastA, LastB; // where its ends were last tick (an end carried by a joint wakes a sleeping body)
    public bool Queued; // changed since the clients were last told
    public bool Line; // a harpoon's line: A the launcher, B the head (ConstructHarpoon.cs)
    public bool Reeling; // its trigger is reeling it in now
    public bool TriggerHeld; // the pull that fired it hasn't been let go
    public float ReelSpeed; // m/s
    public int ReelSync; // ticks until the clients next hear its length
}

/// <summary>A connector in a blueprint: both ends on steps of it (a part's own frame).</summary>
public readonly record struct ConnectorSpec(ConnectorKind Kind, byte Size, int StepA, Vector3 LocalA, int StepB, Vector3 LocalB, float Length, float Rating, float Stiffness, float Damping);

/// <summary>
/// Ropes and springs (docs/sandbox/CONSTRUCTS.md, "Ropes and springs"): the list, the rules the tool and the server share, and
/// the tick. Ends name their parts by <see cref="CPart.Tag"/>, like couplings, so a connector survives other parts coming off,
/// splits and joins; it goes when a part at its end goes (or is wrecked), when a rope is overloaded, when it is torn far past its
/// length, or when the ground under a world end goes. Server-authoritative: clients get the resolved list (CBodyKind.Connectors:
/// whole for a joiner or a big change, a patch otherwise). The list grows with use, so a clip's little world costs nothing, and
/// nothing allocates per tick.
/// </summary>
public sealed partial class ConstructWorld
{
    public const int MaxConnectors = 1024;
    public const float ConnectorMinLength = 0.1f, ConnectorMaxLength = 120f;
    public const float RopeSlack = 0.02f; // a new rope is this share longer than the gap ...
    public const float RopeSlackMin = 0.05f; // ... plus this (metres)
    public const float ConnectorTear = 6f; // a connector dragged this far past its reach goes (a teleport, a blast)
    public const float ConnectorEndLift = 0.03f; // a world end sits this far off its surface
    public const float SpringDampRatio = 0.3f; // a new spring's damping: this share of critical, for what it holds when it is made
    public const float ConnectorLoadSmooth = 0.25f; // the share of this tick's pull the Load takes: a one-tick spike is a quarter of itself

    /// <summary>The sizes: thickness (radius, m), what a rope holds (tonnes), a spring's stiffness (N/m), and the biggest side of a part it fits by default.</summary>
    public static readonly (float Radius, float Tonnes, float Stiffness, float PartSize, string Name)[] ConnectorSizes =
    [
        (0.010f, 1.5f, 15_000f, 0.6f, "S"),
        (0.018f, 6f, 60_000f, 1.5f, "M"),
        (0.030f, 25f, 250_000f, 4f, "L"),
        (0.050f, 120f, 1_200_000f, float.MaxValue, "XL"),
    ];

    public CConnector[] Connectors = new CConnector[4];
    public int ConnectorCount;
    public bool ConnectorsAllDirty; // server: send the whole list
    public int ConnectorsChanged; // server: how many are Queued
    public readonly List<ushort> ConnectorsGone = new(32); // server: ids that went since the clients were last told

    private ushort _nextConnector = 1;
    private int _cnSupportCursor;

    public static string ConnectorName(ConnectorKind k) => k == ConnectorKind.Rope ? "rope" : "spring";

    public int ConnectorIndex(ushort id)
    {
        for (var k = 0; k < ConnectorCount; k++)
            if (Connectors[k].Id == id)
                return k;
        return -1;
    }

    /// <summary>A point on a part (its own frame) in the world, its segment as it is now.</summary>
    public static Vector3 PartPoint(Construct c, int part, Vector3 local)
    {
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out var t);
        return t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, local), q);
    }

    /// <summary>A world point as a point in a part's own frame.</summary>
    public static Vector3 PartLocal(Construct c, int part, Vector3 world)
    {
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out var t);
        return Vector3.Transform(Vector3.Transform(world - t, Quaternion.Conjugate(q)) - p.Pos, Quaternion.Conjugate(Rot24.Quat(p.Rot)));
    }

    /// <summary>An end's point in the world. False when its construct or part isn't here (a client between messages).</summary>
    public bool EndPoint(in ConnectorEnd e, out Vector3 p)
    {
        if (e.Body == 0)
        {
            p = e.Local;
            return true;
        }
        if (Get(e.Body) is not { } c || e.Part >= c.PartCount)
        {
            p = default;
            return false;
        }
        p = PartPoint(c, e.Part, e.Local);
        return true;
    }

    /// <summary>The size a connector takes by the parts at its ends (the smaller of them).</summary>
    public byte AutoConnectorSize(in ConnectorEnd a, in ConnectorEnd b)
    {
        var side = MathF.Min(EndSide(a), EndSide(b));
        for (var s = 0; s < ConnectorSizes.Length; s++)
            if (side <= ConnectorSizes[s].PartSize)
                return (byte)s;
        return (byte)(ConnectorSizes.Length - 1);
    }

    private float EndSide(in ConnectorEnd e)
    {
        if (Get(e.Body) is not { } c || e.Part >= c.PartCount)
            return float.MaxValue;
        c.PartBounds(e.Part, out var mn, out var mx);
        var s = mx - mn;
        return MathF.Max(s.X, MathF.Max(s.Y, s.Z));
    }

    /// <summary>The tool's and the server's rule for a new connector. Null, or why not.</summary>
    public string? CheckConnector(in ConnectorEnd a, in ConnectorEnd b)
    {
        if (a.Body == 0 && b.Body == 0)
            return "tie at least one end to a construct";
        Construct? ca = null, cb = null;
        if (a.Body != 0 && ((ca = Get(a.Body)) == null || a.Part >= ca.PartCount))
            return "the first point's part is gone";
        if (b.Body != 0 && ((cb = Get(b.Body)) == null || b.Part >= cb.PartCount))
            return "that part is gone";
        if (!float.IsFinite(a.Local.X + a.Local.Y + a.Local.Z + b.Local.X + b.Local.Y + b.Local.Z))
            return "bad point";
        if (ca != null && ca == cb && ca.MoveSeg(ca.Parts[a.Part].Seg) == ca.MoveSeg(ca.Parts[b.Part].Seg))
            return "both points are on one rigid piece: a joint must lie between them";
        EndPoint(a, out var pa);
        EndPoint(b, out var pb);
        var d = Vector3.Distance(pa, pb);
        if (d < ConnectorMinLength)
            return "those points are too close";
        if (d > ConnectorMaxLength)
            return $"at most {ConnectorMaxLength:0} m long";
        if (ConnectorCount >= MaxConnectors)
            return $"at most {MaxConnectors} ropes and springs in the world";
        return null;
    }

    /// <summary>Adds a checked connector with the defaults for its kind and size. Its index, or -1 when full.</summary>
    public int AddConnector(IWorldQuery? q, ConnectorKind kind, byte size, ConnectorEnd a, ConnectorEnd b)
    {
        if (!EndPoint(a, out var pa) || !EndPoint(b, out var pb))
            return -1;
        var s = ConnectorSizes[Math.Min((int)size, ConnectorSizes.Length - 1)];
        var dist = Vector3.Distance(pa, pb);
        var k = kind == ConnectorKind.Spring ? s.Stiffness : 0f;
        var mass = HeldMass(a, b);
        return AddConnector(q, new CConnector
        {
            Kind = kind, Size = (byte)Math.Min((int)size, ConnectorSizes.Length - 1), A = a, B = b,
            Length = kind == ConnectorKind.Rope ? Math.Clamp(dist * (1 + RopeSlack) + RopeSlackMin, ConnectorMinLength, ConnectorMaxLength + 1f) : dist,
            Rating = s.Tonnes * 1000f * ConstructPhysics.GravityAccel,
            Stiffness = k,
            Damping = 2f * SpringDampRatio * MathF.Sqrt(k * mass),
        });
    }

    /// <summary>What a spring between two ends holds, for its default damping: the free side's mass, or both in series.</summary>
    private float HeldMass(in ConnectorEnd a, in ConnectorEnd b)
    {
        static float Mass(ConstructWorld w, in ConnectorEnd e) => w.Get(e.Body) is { Anchored: false } c ? c.Mass : 0f;
        float ma = Mass(this, a), mb = Mass(this, b);
        if (a.Body == b.Body)
            return MathF.Max(ma, 1f);
        return ma > 0 && mb > 0 ? ma * mb / (ma + mb) : MathF.Max(MathF.Max(ma, mb), 1f);
    }

    private int AddConnector(IWorldQuery? q, CConnector cn)
    {
        if (ConnectorCount >= MaxConnectors)
            return -1;
        if (ConnectorCount == Connectors.Length)
            Array.Resize(ref Connectors, Math.Min(Connectors.Length * 2, MaxConnectors));
        TieEnd(q, ref cn.A);
        TieEnd(q, ref cn.B);
        cn.Id = NextConnectorId();
        cn.Row = 0;
        cn.Load = 0;
        EndPoint(cn.A, out cn.LastA);
        EndPoint(cn.B, out cn.LastB);
        cn.Queued = true;
        ConnectorsChanged++;
        Connectors[ConnectorCount++] = cn;
        Get(cn.A.Body)?.Wake();
        Get(cn.B.Body)?.Wake();
        if (cn.A.Player != 0 || cn.B.Player != 0)
            PlayerEndCount++;
        return ConnectorCount - 1;
    }

    private void TieEnd(IWorldQuery? q, ref ConnectorEnd e)
    {
        if (Get(e.Body) is { } c && e.Part < c.PartCount)
        {
            EnsureTag(c, e.Part);
            e.Tag = c.Parts[e.Part].Tag;
            e.OnWreck = c.Parts[e.Part].Broken;
            e.Support = default;
            return;
        }
        e.Body = e.Part = e.Tag = 0;
        e.Support = q != null && e.Player == 0 ? FindSupport(q, e.Local, ConnectorEndLift) : Vector3.Zero;
    }

    private ushort NextConnectorId()
    {
        for (var guard = 0; guard <= MaxConnectors + 1; guard++)
        {
            if (_nextConnector == 0)
                _nextConnector = 1;
            var id = _nextConnector++;
            if (ConnectorIndex(id) < 0) // (ids only meet again after a wrap)
                return id;
        }
        return 1;
    }

    /// <summary>Connector <paramref name="k"/> changed: the clients are told.</summary>
    public void TouchConnector(int k) => TouchConnector(ref Connectors[k]);

    private void TouchConnector(ref CConnector cn)
    {
        if (cn.Queued)
            return;
        cn.Queued = true;
        ConnectorsChanged++;
    }

    /// <summary>Removes connector <paramref name="k"/>; what it held wakes.</summary>
    public void RemoveConnector(int k)
    {
        if (k < 0 || k >= ConnectorCount)
            return;
        ref var cn = ref Connectors[k];
        Get(cn.A.Body)?.Wake();
        Get(cn.B.Body)?.Wake();
        if (cn.Queued)
            ConnectorsChanged--;
        if (ConnectorsGone.Count < 256)
            ConnectorsGone.Add(cn.Id);
        else
            ConnectorsAllDirty = true;
        if (cn.A.Player != 0 || cn.B.Player != 0)
            PlayerEndCount--;
        Connectors[k] = Connectors[--ConnectorCount];
    }

    /// <summary>A connector goes with a clear look: the event every PC draws its whip from, and a word for the players near.</summary>
    private void SnapConnector(int k, string? hint)
    {
        ref var cn = ref Connectors[k];
        if (EndPoint(cn.A, out var a) && EndPoint(cn.B, out var b))
        {
            if (hint != null)
                Hints.Add(((a + b) * 0.5f, hint));
            if (_snapCount < _snaps.Length)
                _snaps[_snapCount++] = new CFireEvent { Kind = CFireKind.ConnectorSnap, Part = (ushort)((int)cn.Kind | cn.Size << 4), At = a, To = b };
        }
        Events.Add($"{ConnectorName(cn.Kind)} {cn.Id} went: {hint ?? "an end went"}");
        RemoveConnector(k);
    }

    /// <summary>Client: a connector as the server has it (new, or over the one with its id).</summary>
    public void ClientPutConnector(in CConnector c)
    {
        var k = ConnectorIndex(c.Id);
        if (k < 0)
        {
            if (ConnectorCount >= MaxConnectors)
                return;
            if (ConnectorCount == Connectors.Length)
                Array.Resize(ref Connectors, Math.Min(Connectors.Length * 2, MaxConnectors));
            k = ConnectorCount++;
        }
        var local = Connectors[k]; // (over one we had: a player's end stays where this PC keeps it)
        var had = local.Id == c.Id;
        Connectors[k] = c;
        if (had && c.A.Player != 0 && c.A.Player == local.A.Player)
            Connectors[k].A.Local = local.A.Local;
        if (had && c.B.Player != 0 && c.B.Player == local.B.Player)
            Connectors[k].B.Local = local.B.Local;
        RecountPlayerEnds();
    }

    public void ClientDropConnector(ushort id)
    {
        var k = ConnectorIndex(id);
        if (k >= 0)
            Connectors[k] = Connectors[--ConnectorCount];
        RecountPlayerEnds();
    }

    /// <summary>Client: the whole list is coming.</summary>
    public void ClientClearConnectors()
    {
        ConnectorCount = 0;
        PlayerEndCount = 0;
    }

    /// <summary>The list has been told to the clients.</summary>
    public void ConnectorsSent()
    {
        for (var k = 0; k < ConnectorCount; k++)
            Connectors[k].Queued = false;
        ConnectorsChanged = 0;
        ConnectorsGone.Clear();
        ConnectorsAllDirty = false;
    }

    private void ClearConnectors()
    {
        if (ConnectorCount > 0)
            ConnectorsAllDirty = true;
        ConnectorCount = 0;
        PlayerEndCount = 0;
        ConnectorsChanged = 0;
        ConnectorsGone.Clear();
    }

    /// <summary>An end finds its part again (it may have been renumbered or split off). False when the part is gone or wrecked.</summary>
    private bool ResolveEnd(ref CConnector cn, ref ConnectorEnd e, out Construct? c)
    {
        c = null;
        if (e.Tag == 0)
            return true;
        var (body, part) = (e.Body, e.Part);
        c = Resolve(e.Tag, ref e.Body, ref e.Part);
        if (c == null || c.Parts[e.Part].Broken && !e.OnWreck)
            return false;
        if (body != e.Body || part != e.Part)
            TouchConnector(ref cn);
        return true;
    }

    /// <summary>
    /// Server, every tick before the physics: every connector finds its parts; a rope over its rating snaps, one dragged far
    /// past its reach goes, one whose part or ground went goes; a moving end wakes what sleeps at the other end when it can pull.
    /// </summary>
    private void StepConnectors(IWorldQuery? query)
    {
        for (var k = ConnectorCount - 1; k >= 0; k--)
        {
            ref var cn = ref Connectors[k];
            if (!ResolveEnd(ref cn, ref cn.A, out var ca) || !ResolveEnd(ref cn, ref cn.B, out var cb))
            {
                SnapConnector(k, null);
                continue;
            }
            EndPoint(cn.A, out var pa);
            EndPoint(cn.B, out var pb);
            var dist = Vector3.Distance(pa, pb);
            if (cn.Kind == ConnectorKind.Rope && cn.Load > cn.Rating)
            {
                SnapConnector(k, $"the rope snapped: {cn.Load / (1000f * ConstructPhysics.GravityAccel):0.0} t on a {cn.Rating / (1000f * ConstructPhysics.GravityAccel):0.0} t rope");
                continue;
            }
            if (dist > cn.Length + ConnectorTear || dist > ConnectorMaxLength * 2)
            {
                SnapConnector(k, $"the {ConnectorName(cn.Kind)} tore: pulled too far");
                continue;
            }
            if (ca != cb)
            {
                var taut = cn.Kind != ConnectorKind.Rope || dist >= cn.Length - 0.03f;
                if (taut && (cb is { Asleep: true } && (ca != null && Moving(ca) || Vector3.DistanceSquared(pa, cn.LastA) > 1e-6f || dist > cn.Length + 0.02f && cn.Kind == ConnectorKind.Rope)))
                    cb.Wake();
                if (taut && (ca is { Asleep: true } && (cb != null && Moving(cb) || Vector3.DistanceSquared(pb, cn.LastB) > 1e-6f || dist > cn.Length + 0.02f && cn.Kind == ConnectorKind.Rope)))
                    ca.Wake();
            }
            cn.LastA = pa;
            cn.LastB = pb;
            if (cn.Line)
                ReelLine(ref cn, dist);
        }
        // One world end a tick: is its surface still there?
        if (query != null && ConnectorCount > 0)
        {
            _cnSupportCursor = (_cnSupportCursor + 1) % (ConnectorCount * 2);
            var k = _cnSupportCursor / 2;
            ref var e = ref (_cnSupportCursor & 1) == 0 ? ref Connectors[k].A : ref Connectors[k].B;
            if (e.Tag == 0 && e.Support != Vector3.Zero && !query.Raycast(e.Local, e.Support, ConnectorEndLift + SupportReach + 0.02f, QueryFilter.StaticOnly, out _))
                SnapConnector(k, $"the {ConnectorName(Connectors[k].Kind)} came loose: the ground under it is gone");
        }
    }

    /// <summary>The connector along a ray (world), within <paramref name="best"/>. A slack rope is tried along its sag.</summary>
    public bool RayConnectors(Vector3 origin, Vector3 dir, ref float best, out int connector)
    {
        connector = -1;
        Span<Vector3> pts = stackalloc Vector3[5];
        for (var k = 0; k < ConnectorCount; k++)
        {
            ref var cn = ref Connectors[k];
            if (!EndPoint(cn.A, out var a) || !EndPoint(cn.B, out var b))
                continue;
            var near = Vector3.Dot(a - origin, dir);
            var far = Vector3.Dot(b - origin, dir);
            if (near < 0 && far < 0 || near > best + 2f && far > best + 2f)
                continue;
            var radius = MathF.Max(0.05f, ConnectorSizes[cn.Size].Radius * (cn.Kind == ConnectorKind.Spring ? 5f : 1.5f));
            var n = pts.Length;
            for (var i = 0; i < n; i++)
                pts[i] = ConnectorRender.CurvePoint(a, b, cn.Kind == ConnectorKind.Rope ? cn.Length : 0, i / (float)(n - 1));
            for (var i = 1; i < n; i++)
            {
                var d2 = Geometry.ClosestSegmentSegment(origin, origin + dir * best, pts[i - 1], pts[i], out var onRay, out _);
                if (d2 > radius * radius)
                    continue;
                var t = Vector3.Dot(onRay - origin, dir);
                if (t >= 0 && t < best)
                {
                    best = t;
                    connector = k;
                }
            }
        }
        return connector >= 0;
    }

    /// <summary>The nearest either end of a connector is to a point, and the line between them: how far a player is from it.</summary>
    public float ConnectorReach(in CConnector cn, Vector3 from)
    {
        if (!EndPoint(cn.A, out var a) || !EndPoint(cn.B, out var b))
            return float.MaxValue;
        return MathF.Sqrt(Geometry.ClosestSegmentSegment(from, from, a, b, out _, out _));
    }

    /// <summary>What a stretch of this kind and size costs in survival (units of <see cref="ConstructEdits.ConnectorMaterial"/>): by the metre, rounded up. A spring is a coil of the same stuff: its rest length, twice over.</summary>
    public static int ConnectorCost(ConnectorKind kind, byte size, float metres) =>
        Math.Max(1, (int)MathF.Ceiling(metres * (kind == ConnectorKind.Spring ? 2f : 1f) * (1 << Math.Min((int)size, 3))));

    // ---- Selections and blueprints ------------------------------------------------------------------------------------

    /// <summary>Connectors with both ends among the parts copied come along (<paramref name="marked"/>: each source part's new index, or -1).</summary>
    private void CopyConnectors(ConstructWorld from, Construct src, int[] marked, Construct c)
    {
        for (var k = 0; k < from.ConnectorCount; k++)
        {
            ref var cn = ref from.Connectors[k];
            if (cn.A.Body != src.Id || cn.B.Body != src.Id || cn.A.Part >= marked.Length || cn.B.Part >= marked.Length || marked[cn.A.Part] < 0 || marked[cn.B.Part] < 0)
                continue;
            var copy = cn;
            copy.A = new ConnectorEnd { Body = c.Id, Part = (ushort)marked[cn.A.Part], Local = cn.A.Local };
            copy.B = new ConnectorEnd { Body = c.Id, Part = (ushort)marked[cn.B.Part], Local = cn.B.Local };
            AddConnector(null, copy);
        }
    }

    /// <summary>A blueprint's connectors onto what it built. Null, or why not.</summary>
    public string? BuildConnector(Construct c, in ConnectorSpec s, ReadOnlySpan<int> partOf)
    {
        if (s.StepA < 0 || s.StepB < 0 || s.StepA >= partOf.Length || s.StepB >= partOf.Length || partOf[s.StepA] < 0 || partOf[s.StepB] < 0)
            return "no such step";
        var a = new ConnectorEnd { Body = c.Id, Part = (ushort)partOf[s.StepA], Local = s.LocalA };
        var b = new ConnectorEnd { Body = c.Id, Part = (ushort)partOf[s.StepB], Local = s.LocalB };
        return AddConnector(null, new CConnector { Kind = s.Kind, Size = s.Size, A = a, B = b, Length = s.Length, Rating = s.Rating, Stiffness = s.Stiffness, Damping = s.Damping }) < 0 ? "no room" : null;
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The connectors with both ends on <paramref name="c"/> as a blueprint's "connectors" list ("" for none); a step is its part's index.</summary>
    public string ConnectorsJson(Construct c)
    {
        var sb = new StringBuilder();
        for (var k = 0; k < ConnectorCount; k++)
        {
            ref var cn = ref Connectors[k];
            if (cn.A.Body != c.Id || cn.B.Body != c.Id)
                continue;
            sb.Append(sb.Length == 0 ? "[\n" : ",\n");
            sb.Append($"    {{ \"kind\": \"{ConnectorName(cn.Kind)}\", \"size\": {cn.Size}, \"a\": {cn.A.Part}, \"at\": [{F(cn.A.Local.X)}, {F(cn.A.Local.Y)}, {F(cn.A.Local.Z)}], \"b\": {cn.B.Part}, \"bAt\": [{F(cn.B.Local.X)}, {F(cn.B.Local.Y)}, {F(cn.B.Local.Z)}]");
            sb.Append($", \"length\": {F(cn.Length)}, \"rating\": {F(cn.Rating)}, \"stiffness\": {F(cn.Stiffness)}, \"damping\": {F(cn.Damping)} }}");
        }
        return sb.Length == 0 ? "" : sb.Append("\n  ]").ToString();
    }

    /// <summary>One entry of a blueprint's "connectors" list.</summary>
    public static ConnectorSpec ParseConnector(JsonElement e, string file)
    {
        try
        {
            var kind = e.GetProperty("kind").GetString() switch { "rope" => ConnectorKind.Rope, "spring" => ConnectorKind.Spring, var o => throw new ConstructPartException($"{file}: connector kind \"{o}\" is rope or spring") };
            static Vector3 V(JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());
            var size = e.GetProperty("size").GetInt32();
            if (size < 0 || size >= ConnectorSizes.Length)
                throw new ConstructPartException($"{file}: connector size {size} is 0 to {ConnectorSizes.Length - 1}");
            var s = new ConnectorSpec(kind, (byte)size, e.GetProperty("a").GetInt32(), V(e.GetProperty("at")), e.GetProperty("b").GetInt32(), V(e.GetProperty("bAt")),
                e.GetProperty("length").GetSingle(), e.GetProperty("rating").GetSingle(), e.GetProperty("stiffness").GetSingle(), e.GetProperty("damping").GetSingle());
            if (!float.IsFinite(s.Length + s.Rating + s.Stiffness + s.Damping) || s.Length < ConnectorMinLength || s.Rating < 0 || s.Stiffness < 0 || s.Damping < 0)
                throw new ConstructPartException($"{file}: a connector's numbers are out of range");
            return s;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or IndexOutOfRangeException)
        {
            throw new ConstructPartException($"{file}: a connector needs kind, size, a, at, b, bAt, length, rating, stiffness, damping: {ex.Message}");
        }
    }
}

/// <summary>Connectors in the solve: a rope is the coupling chain's row, one-sided; a spring is an implicit soft row (ConstructFlex's sum on a two-point row), so no stiffness can blow it up.</summary>
public static partial class ConstructPhysics
{
    /// <summary>Both ends of a connector are simulated together, in a big world too.</summary>
    private static void MarkConnectors(ConstructWorld world, int stamp)
    {
        for (var k = 0; k < world.ConnectorCount; k++)
        {
            ref var cn = ref world.Connectors[k];
            if (cn.A.Body != cn.B.Body && world.Get(cn.A.Body) is { } a && world.Get(cn.B.Body) is { } b && (a.SimMark == stamp) != (b.SimMark == stamp))
                a.SimMark = b.SimMark = stamp;
        }
    }

    private static bool InSim(Construct c, int n) => (uint)c.SimIndex < (uint)n && ReferenceEquals(_bodies[c.SimIndex], c);

    private static ulong ConnectorKey(ushort id) => (0xC0FFEE0000000000UL ^ (id * 0x9E3779B97F4A7C15UL)) | 1;

    /// <summary>Each connector's row. A side that can't move (the world, a sleeping or anchored body) is a fixed point.</summary>
    private static void ConnectorRows(ConstructWorld world, int n)
    {
        for (var k = 0; k < world.ConnectorCount; k++)
        {
            ref var cn = ref world.Connectors[k];
            cn.Row = 0;
            Construct? a = cn.A.Body != 0 ? world.Get(cn.A.Body) : null, b = cn.B.Body != 0 ? world.Get(cn.B.Body) : null;
            if (a != null && cn.A.Part >= a.PartCount || b != null && cn.B.Part >= b.PartCount)
                continue;
            if (a != null && !InSim(a, n))
                a = null;
            if (b != null && !InSim(b, n))
                b = null;
            world.EndPoint(cn.A, out var pa);
            world.EndPoint(cn.B, out var pb);
            var sa = a != null ? a.Parts[cn.A.Part].Seg : 0;
            var sb = b != null ? b.Parts[cn.B.Part].Seg : 0;
            var movesA = a != null && (_dyn[a.SimIndex] || sa > 0 && _jact[a.SimIndex]);
            var movesB = b != null && (_dyn[b.SimIndex] || sb > 0 && _jact[b.SimIndex]);
            if (!movesA && !movesB)
                continue;
            var flip = !movesA;
            if (flip)
            {
                (a, b, pa, pb, sa, sb) = (b, a, pb, pa, sb, sa);
                movesB = false;
            }
            var d = pb - pa;
            var dist = d.Length();
            var onPlayer = cn.A.Player != 0 || cn.B.Player != 0; // (a player is a fixed point here, so the row is soft: a man's pull, not a wall's; Game.TetherPlayer moves them)
            if (dist < 1e-4f || cn.Kind == ConnectorKind.Rope && dist < cn.Length - (onPlayer ? 0f : 0.5f))
                continue; // (slack: nothing to hold)
            if (a == b && a!.MoveSeg(sa) == a.MoveSeg(sb))
                continue; // (joined since it was tied)
            if (!ConnectorRow(a!, sa, pa, movesB ? b : null, sb, pb, d / dist, cn.Length - dist, cn.Kind == ConnectorKind.Spring || onPlayer, onPlayer ? ConstructWorld.PlayerPullStiffness : cn.Stiffness, onPlayer ? ConstructWorld.PlayerPullDamping : cn.Damping))
                continue;
            if (cn.Kind == ConnectorKind.Rope && !onPlayer)
                _contacts[_contactCount - 1].Key = ConnectorKey(cn.Id); // (warm started: a heavy load on a light body holds from the first pass)
            cn.Row = _contactCount;
        }
    }

    private static bool ConnectorRow(Construct a, int sa, Vector3 pa, Construct? b, int sb, Vector3 pb, Vector3 normal, float gap, bool spring, float stiffness, float damping)
    {
        if (_contactCount >= MaxContacts)
            return false;
        short ia = (short)a.SimIndex, ib = b != null ? (short)b.SimIndex : (short)-1;
        var ua = _chainCount;
        var na = sa > 0 && _jact[ia] ? Chain(a, ia, sa, pa, out _) : 0;
        if (na < 0)
            return false;
        _chainCount += na;
        var ub = _chainCount;
        var nb = b != null && sb > 0 && _jact[ib] ? Chain(b, ib, sb, pb, out _) : 0;
        if (nb < 0)
            nb = 0;
        _chainCount += nb;
        var one = a == b;
        if (one)
        {
            // Across a joint on one body: what both chains share moves both ends alike, so it plays no part.
            while (na > 0 && nb > 0 && _chainSlot[ua + na - 1] == _chainSlot[ub + nb - 1])
            {
                na--;
                nb--;
            }
            if (na + nb == 0)
                return false;
        }
        if (!Add(ia, ib, pa, normal, gap, 0f, ua, na, ub, nb))
            return false;
        ref var ct = ref _contacts[_contactCount - 1];
        ct.TwoPoints = true;
        ct.PointB = pb;
        ct.Bilateral = spring;
        ct.Soft = spring;
        ct.SoftK = stiffness;
        ct.SoftD = damping;
        if (one)
            ct.KinA = ct.KinB = true; // (the row's push is inside the body: only the joints move)
        else
        {
            var heldA = a.GrabbedBy != 255 && _dyn[ia];
            var heldB = b != null && b.GrabbedBy != 255 && _dyn[ib];
            if (heldA != heldB && b != null)
            {
                ct.KinA = heldA;
                ct.KinB = heldB; // (held by a physics gun: it leads, the other follows, as on a coupling)
            }
        }
        if (na + nb > 0)
            LastJointContacts--;
        return true;
    }

    /// <summary>After the solve: what each connector carried this tick, smoothed (a rope's snap reads it next tick: the list must not change mid-step).</summary>
    private static void ConnectorLoads(ConstructWorld world, float dt)
    {
        for (var k = 0; k < world.ConnectorCount; k++)
        {
            ref var cn = ref world.Connectors[k];
            var now = cn.Row > 0 ? MathF.Abs(_contacts[cn.Row - 1].Jn) / dt : 0f;
            cn.Row = 0;
            cn.Load += (now - cn.Load) * ConstructWorld.ConnectorLoadSmooth;
        }
    }
}
