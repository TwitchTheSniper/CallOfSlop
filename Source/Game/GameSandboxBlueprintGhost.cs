using System.Diagnostics;
using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What this PC's blueprint ghost is doing.</summary>
public enum BlueprintGhostState : byte
{
    None,
    Preparing, // asked for: the host is making it ready
    Carrying, // the ghost is out: the player places it
}

/// <summary>
/// The blueprint ghost (docs/sandbox/constructs/building-and-tools.md, "Spawning with a ghost"): the parts menu's Spawn (a shipped blueprint,
/// a faction vehicle by shape or role, the creative "Make an engine") asks the host to make it ready (CActKind.SpawnPrepare wraps the
/// spawn act). The host makes it and builds it in a scratch world a slice a tick (VehicleSpawner, ConstructBuild.GroundJob), then holds it
/// as the player's pending spawn and sends them its ghost: the scratch body's boxes (CActKind.SpawnGhost), so a generated vehicle, which
/// only the host makes, has a true ghost as a shipped one does. The client carries it under the crosshair, turns it and places it
/// (CActKind.SpawnPlace); the host checks the place with the same <see cref="BlueprintGhostRefusal"/> the ghost shows and joins it there.
/// One pending spawn a player: a new one replaces it; it goes with a cancel, death, leaving, a new world or after <see cref="HeldSeconds"/>.
/// </summary>
public sealed partial class Game
{
    /// <summary>A pending spawn not placed by then is dropped (seconds).</summary>
    public const double HeldSeconds = 300;

    /// <summary>The most boxes a ghost is sent (the biggest first); past it the bounds' frame shows the whole.</summary>
    public const int GhostMaxBoxes = ConstructRender.MaxGroupBoxes;

    /// <summary>Steepest ground under a ghost's footprint (degrees).</summary>
    public const float GhostMaxSlope = 35f;

    // ---- Shared: the pose and the check (the client's ghost and the host's place use the same two functions) --------------------

    /// <summary>A ghost's reach radius from its bounds: the half diagonal of its footprint.</summary>
    public static float GhostRadius(Vector3 min, Vector3 max) => MathF.Max(0.5f, new Vector2(max.X - min.X, max.Z - min.Z).Length() * 0.5f);

    /// <summary>How far out the ghost is carried when the crosshair is on nothing nearer (m): out past its own size, 5 to 120.</summary>
    public static float GhostCarry(float radius) => Math.Clamp(radius + 3f, 5f, 120f);

    /// <summary>How far from the eye a ghost may be placed (m): a 100 m walker (a radius of about 60) still reaches about 110 m.</summary>
    public static float GhostReach(float radius) => radius * 1.5f + 20f;

    /// <summary>
    /// A ghost's pose for the point <paramref name="at"/> on a surface and a yaw: its footprint's middle over the point, turned about the up
    /// there (ConstructBuild.GroundRot: the direct spawn's frame), its lowest point 2 cm into the surface as SetDown stands it.
    /// <paramref name="ground"/> is the point the set-down is asked for (GroundJob.Join), <paramref name="bodyPos"/> its body's origin.
    /// </summary>
    public static void BlueprintGhostPose(Vector3 at, float yaw, Vector3 min, Vector3 max, float low, out Vector3 ground, out Quaternion rot, out Vector3 bodyPos)
    {
        rot = ConstructBuild.GroundRot(at, yaw);
        var mid = new Vector3((min.X + max.X) * 0.5f, 0, (min.Z + max.Z) * 0.5f);
        ground = at - Vector3.Transform(mid, rot); // (it turns about its own middle, not its first part)
        bodyPos = ground - Vector3.Transform(new Vector3(0, low - 0.02f, 0), rot);
    }

    /// <summary>
    /// Why a ghost can't go down there, or null: out of the world, too far from <paramref name="eye"/>, too steep, or its bounds (3 cm in,
    /// 5 cm off its base so a top it rests on doesn't count) overlap a construct's or a player. No allocation.
    /// </summary>
    public string? BlueprintGhostRefusal(byte me, Vector3 eye, Vector3 at, Quaternion rot, Vector3 bodyPos, Vector3 min, Vector3 max)
    {
        if (!float.IsFinite(at.X + at.Y + at.Z + bodyPos.X + bodyPos.Y + bodyPos.Z))
            return "aim at the ground";
        if (ConstructWorld.FellOut(at, World.Query))
            return "out of the world";
        var radius = GhostRadius(min, max);
        if (Vector3.Distance(eye, at) > GhostReach(radius))
            return "too far away";
        var up = Vector3.Transform(Vector3.UnitY, rot);
        var right = Vector3.Transform(Vector3.UnitX, rot);
        var fwd = Vector3.Transform(Vector3.UnitZ, rot);
        // Too steep: the ground's heights at the footprint's edges (at most 3 m out), against the up there.
        var s = Math.Clamp(MathF.Min(max.X - min.X, max.Z - min.Z) * 0.5f, 0.5f, 3f);
        var slope = MathF.Tan(GhostMaxSlope * MathF.PI / 180f) * 2 * s;
        var hx = Vector3.Dot(ConstructWorld.Ground(World.Query, at + right * s) - ConstructWorld.Ground(World.Query, at - right * s), up);
        var hz = Vector3.Dot(ConstructWorld.Ground(World.Query, at + fwd * s) - ConstructWorld.Ground(World.Query, at - fwd * s), up);
        if (MathF.Abs(hx) > slope || MathF.Abs(hz) > slope)
            return "too steep here";
        var lo = min + new Vector3(0.03f, 0.05f, 0.03f);
        var hi = max - new Vector3(0.03f);
        var centre = bodyPos + Vector3.Transform((lo + hi) * 0.5f, rot);
        var half = Vector3.Max((hi - lo) * 0.5f, new Vector3(0.01f));
        foreach (var c in Constructs.Bodies)
        {
            if (c.PartCount == 0)
                continue;
            var cr = c.Rot;
            var ch = (c.LocalMax - c.LocalMin) * 0.5f;
            var cc = c.Pos + Vector3.Transform((c.LocalMin + c.LocalMax) * 0.5f, cr);
            var r2 = half.Length() + ch.Length();
            if (Vector3.DistanceSquared(centre, cc) > r2 * r2)
                continue;
            if (BoxesOverlap(centre, half, rot, cc, ch, cr))
                return c.Loose ? "a loose part is in the way" : "overlaps a construct";
        }
        var inv = Quaternion.Conjugate(rot);
        foreach (var p in Players.Values)
        {
            if (!p.Alive)
                continue;
            var pu = GravityFrames.UpAt(p.Position);
            for (var k = 0; k < 3; k++)
            {
                var l = Vector3.Transform(p.Position + pu * (0.2f + 0.7f * k) - centre, inv);
                if (MathF.Abs(l.X) < half.X + 0.35f && MathF.Abs(l.Y) < half.Y + 0.35f && MathF.Abs(l.Z) < half.Z + 0.35f)
                    return p.Id == me ? "you are in the way" : "a player is in the way";
            }
        }
        return null;
    }

    /// <summary>Two oriented boxes overlap (the separating axis test, 15 axes).</summary>
    private static bool BoxesOverlap(Vector3 ca, Vector3 ha, Quaternion ra, Vector3 cb, Vector3 hb, Quaternion rb)
    {
        Span<Vector3> a = [Vector3.Transform(Vector3.UnitX, ra), Vector3.Transform(Vector3.UnitY, ra), Vector3.Transform(Vector3.UnitZ, ra)];
        Span<Vector3> b = [Vector3.Transform(Vector3.UnitX, rb), Vector3.Transform(Vector3.UnitY, rb), Vector3.Transform(Vector3.UnitZ, rb)];
        var d = cb - ca;
        for (var i = 0; i < 3; i++)
        {
            if (Separates(a[i], d, ha, hb, a, b))
                return false;
            if (Separates(b[i], d, ha, hb, a, b))
                return false;
        }
        for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
            {
                var axis = Vector3.Cross(a[i], b[j]);
                if (axis.LengthSquared() > 1e-8f && Separates(axis, d, ha, hb, a, b))
                    return false;
            }
        return true;
    }

    private static bool Separates(Vector3 axis, Vector3 d, Vector3 ha, Vector3 hb, ReadOnlySpan<Vector3> a, ReadOnlySpan<Vector3> b)
    {
        var ra = ha.X * MathF.Abs(Vector3.Dot(a[0], axis)) + ha.Y * MathF.Abs(Vector3.Dot(a[1], axis)) + ha.Z * MathF.Abs(Vector3.Dot(a[2], axis));
        var rb = hb.X * MathF.Abs(Vector3.Dot(b[0], axis)) + hb.Y * MathF.Abs(Vector3.Dot(b[1], axis)) + hb.Z * MathF.Abs(Vector3.Dot(b[2], axis));
        return MathF.Abs(Vector3.Dot(d, axis)) > ra + rb;
    }

    // ---- Server: the pending spawns ---------------------------------------------------------------------------------------------

    private readonly VehicleSpawner.Held?[] _sbHeld = new VehicleSpawner.Held?[256];
    private readonly ushort[] _sbHeldToken = new ushort[256];
    private int _sbHeldCount;
    private PacketWriter? _sbGhostW;

    /// <summary>Server: pending spawns held now (the window-shot tool and the log count them).</summary>
    public int SpawnPendingCount => _sbHeldCount;

    /// <summary>Server: CActKind.SpawnPrepare (after its kind byte): the spawn act inside is run with its ask held for a ghost.</summary>
    private string? SandboxSpawnPrepareAct(Player p, ref PacketReader r, double now)
    {
        var peek = r;
        var inner = (CActKind)peek.U8();
        if (inner is not (CActKind.Blueprint or CActKind.Vehicle or CActKind.FactionVehicle or CActKind.MakeEngine))
            return "that can't be placed with a ghost";
        if (inner == CActKind.FactionVehicle && peek.U8() == 1)
            return "a convoy is set down as it is";
        if (SandboxMenuSpawnRefusal(p) is { } no)
            return no;
        SpawnDrop(p.Id, ""); // (one at a time: a new one replaces it)
        var token = ++_sbHeldToken[p.Id];
        if (token == 0)
            token = _sbHeldToken[p.Id] = 1;
        var spawner = VehicleSpawn;
        spawner.HoldFor = p.Id;
        spawner.HoldToken = token;
        try
        {
            var why = SandboxActDo(p, ref r, now);
            if (why != null)
                SpawnGhostSend(p.Id, 2, token, "", null);
            return why;
        }
        finally
        {
            spawner.HoldFor = 255;
        }
    }

    /// <summary>Server: CActKind.SpawnPlace. The same check as the ghost's, then the set-down at that pose; a refusal leaves the ghost out.</summary>
    private string? SandboxSpawnPlaceAct(Player p, ushort token, Vector3 at, float yaw)
    {
        if (_sbHeld[p.Id] is not { } h || h.Token != token)
            return "nothing to place: it was dropped";
        if (SandboxMenuSpawnRefusal(p) is { } no)
            return no;
        if (!float.IsFinite(yaw))
            return "bad place";
        BlueprintGhostPose(at, yaw, h.Min, h.Max, h.Low, out var ground, out var rot, out var bodyPos);
        if (BlueprintGhostRefusal(p.Id, p.Eye, at, rot, bodyPos, h.Min, h.Max) is { } why)
            return why;
        _sbHeld[p.Id] = null;
        _sbHeldCount--;
        var c = VehicleSpawn.PlaceHeld(h, ground, yaw, out var refused);
        SpawnGhostSend(p.Id, c != null ? (byte)1 : (byte)2, token, h.Name, null);
        if (c == null)
            return refused.Length > 0 ? refused : $"the {h.Name} couldn't be set down";
        return null;
    }

    /// <summary>Server: CActKind.SpawnCancel: the pending spawn (or the one being made) goes.</summary>
    private string? SandboxSpawnCancelAct(Player p, ushort token)
    {
        if (token != 0 && token != _sbHeldToken[p.Id])
            return null; // (an older one: already gone; 0: whatever is current)
        _sbHeldToken[p.Id]++; // (one still being made is dropped when it is ready)
        SpawnDrop(p.Id, "");
        return null;
    }

    /// <summary>Server: the spawner made a held spawn ready (VehicleSpawner.StepPlace). Its shape is measured and its ghost sent; a stale one is dropped.</summary>
    internal void SpawnHeld(VehicleSpawner.Held h)
    {
        var who = h.Owner;
        if (h.Token != _sbHeldToken[who] || !Players.TryGetValue(who, out var p) || !p.Alive)
        {
            Console.WriteLine($"[sandbox] the {h.Name} made ready for player {who} was dropped (cancelled, replaced or they left)");
            return;
        }
        if (h.Job.Body is not { } body)
        {
            SpawnHeldFailed(who, h.Token, h.Name, h.Job.Why);
            return;
        }
        var start = Stopwatch.GetTimestamp();
        h.Low = ConstructBuild.LowOf(h.Job.Scratch, body); // (rebuilt here: its bounds, wheels and legs)
        h.Min = body.LocalMin;
        h.Max = body.LocalMax;
        h.Since = SandboxNow;
        SpawnDrop(who, "");
        _sbHeld[who] = h;
        _sbHeldCount++;
        var boxes = SpawnGhostSend(who, 0, h.Token, h.Name, body, h.Low);
        Console.WriteLine($"[sandbox] pending spawn: the {h.Name} for player {who} ({body.PartCount} parts, {boxes} ghost boxes, {h.Max - h.Min} m), measured and sent in {(Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency:0.0} ms; {_sbHeldCount} pending");
    }

    /// <summary>Server: a held spawn couldn't be made: the asker is told and their preparing ends.</summary>
    internal void SpawnHeldFailed(byte who, ushort token, string name, string why)
    {
        if (token != _sbHeldToken[who])
            return;
        SandboxHands.Notices.Add((who, why.Length > 0 ? why : $"the {name} couldn't be made"));
        SpawnGhostSend(who, 2, token, name, null);
    }

    /// <summary>Server: a player's pending spawn goes (its scratch world with it); <paramref name="why"/> non-empty: they are told.</summary>
    private void SpawnDrop(byte who, string why)
    {
        if (_sbHeld[who] is not { } h)
            return;
        _sbHeld[who] = null;
        _sbHeldCount--;
        Console.WriteLine($"[sandbox] pending spawn dropped: the {h.Name} of player {who}{(why.Length > 0 ? $" ({why})" : "")}; {_sbHeldCount} pending");
        if (why.Length > 0)
        {
            SandboxHands.Notices.Add((who, $"the {h.Name} was dropped: {why}"));
            SpawnGhostSend(who, 2, h.Token, h.Name, null);
        }
    }

    /// <summary>Server, every Sandbox tick: pending spawns of players who left or died, or held too long, go. Nothing to do: one comparison.</summary>
    private void SpawnHeldTick()
    {
        if (_sbHeldCount <= 0)
            return;
        for (var i = 0; i < _sbHeld.Length; i++)
        {
            if (_sbHeld[i] is not { } h)
                continue;
            var who = (byte)i;
            if (!Players.TryGetValue(who, out var p))
                SpawnDrop(who, "");
            else if (!p.Alive)
                SpawnDrop(who, "you died");
            else if (SandboxNow - h.Since > HeldSeconds)
                SpawnDrop(who, "not placed in 5 minutes");
        }
    }

    /// <summary>Server: a new world or match: no pending spawns.</summary>
    private void SpawnHeldReset()
    {
        Array.Clear(_sbHeld);
        _sbHeldCount = 0;
        GhostState = BlueprintGhostState.None; // (this PC's ghost goes with the world too)
        GhostCount = 0;
    }

    /// <summary>Server: CActKind.SpawnGhost to one player (the host's own read at once). Status 0 sends <paramref name="body"/>'s boxes: returns how many.</summary>
    private int SpawnGhostSend(byte who, byte status, ushort token, string name, Construct? body, float low = 0)
    {
        var w = (_sbGhostW ??= new PacketWriter(16 + 512 + GhostMaxBoxes * 31)).Begin(Msg.CAct).U8((byte)CActKind.SpawnGhost).U8(status).U16(token).Str(name);
        var n = 0;
        if (status == 0 && body != null)
            n = WriteGhostBoxes(w, body, low);
        if (who == LocalId)
        {
            var r = new PacketReader(w.Span[2..]);
            SpawnGhostRead(ref r);
        }
        else if (!Net.IsOffline && Players.TryGetValue(who, out var p) && GuidOf(p) is { } g)
            Net.SendTo(g, w.Span, PacketReliability.ReliableOrdered);
        return n;
    }

    /// <summary>The ghost's shape (bounds, lowest point) and its boxes in its body's frame: the biggest <see cref="GhostMaxBoxes"/> when there are more.</summary>
    private static int WriteGhostBoxes(PacketWriter w, Construct body, float low)
    {
        var total = 0;
        for (var i = 0; i < body.PartCount; i++)
            total += body.DefOf(i).Boxes.Length;
        var list = new List<(float Size, int Part, int Box)>(total);
        for (var i = 0; i < body.PartCount; i++)
        {
            var d = body.DefOf(i);
            for (var b = 0; b < d.Boxes.Length; b++)
                if (!d.Boxes[b].Hidden)
                    list.Add((d.Boxes[b].Half.X * d.Boxes[b].Half.Y * d.Boxes[b].Half.Z, i, b));
        }
        if (list.Count > GhostMaxBoxes)
            list.Sort((x, y) => y.Size.CompareTo(x.Size)); // (the biggest show the shape: the bounds' frame the rest)
        var n = Math.Min(list.Count, GhostMaxBoxes);
        w.Vec(body.LocalMin).Vec(body.LocalMax).F32(low);
        return WriteGhostBoxList(w, body, list, n);
    }

    private static int WriteGhostBoxList(PacketWriter w, Construct body, List<(float Size, int Part, int Box)> list, int n)
    {
        w.U16((ushort)n);
        for (var k = 0; k < n; k++)
        {
            var (_, i, b) = list[k];
            ref var p = ref body.Parts[i];
            ref readonly var box = ref body.DefOf(i).Boxes[b];
            var m = box.Model(p.Rot, p.Pos + Rot24.Apply(p.Rot, box.Center));
            Matrix4x4.Decompose(m, out var scale, out var q, out var t);
            var colour = ConstructRender.GhostBoxColour(in box, true);
            w.F32(t.X).F32(t.Y).F32(t.Z);
            w.U16(BitConverter.HalfToUInt16Bits((Half)scale.X)).U16(BitConverter.HalfToUInt16Bits((Half)scale.Y)).U16(BitConverter.HalfToUInt16Bits((Half)scale.Z));
            w.U16(Snorm(q.X)).U16(Snorm(q.Y)).U16(Snorm(q.Z)).U16(Snorm(q.W));
            w.U8(Unorm(colour.X)).U8(Unorm(colour.Y)).U8(Unorm(colour.Z)).U8(Unorm(0.35f * ConstructRender.GhostGlass(in box))).U8((byte)box.Shape);
        }
        return n;
    }

    private static ushort Snorm(float v) => unchecked((ushort)(short)Math.Clamp(MathF.Round(v * 32767f), -32767f, 32767f));

    private static byte Unorm(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0f, 255f);

    // ---- Client: this PC's ghost ------------------------------------------------------------------------------------------------

    public BlueprintGhostState GhostState;
    public string GhostName = "";
    public ushort GhostToken;
    public Vector3 GhostMin, GhostMax;
    public float GhostLow;
    public double GhostSince;

    /// <summary>The ghost's boxes in its body's frame (allocated when a bigger ghost comes; drawn by ConstructRender.DrawBlueprintGhost).</summary>
    public Matrix4x4[] GhostBoxes = [];
    public Vector3[] GhostColours = [];
    public float[] GhostAlpha = [];
    public int[] GhostShapes = [];
    public int GhostCount;

    /// <summary>Client: the parts menu's Spawn with a ghost: <paramref name="act"/> is the spawn act's own message (kind and fields), sent wrapped.</summary>
    private void SpawnPrepareSend(PacketWriter act, string name)
    {
        var w = _sbPrepW ??= new PacketWriter(1024);
        w.Begin(Msg.CAct).U8((byte)CActKind.SpawnPrepare).Bytes(act.Span[1..]); // (past Msg.CAct; copied first: the cancel below writes SbW)
        if (GhostState != BlueprintGhostState.None)
            SpawnCancelSend(); // (one at a time)
        GhostState = BlueprintGhostState.Preparing;
        GhostName = name;
        GhostSince = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        SpawnActSend(w);
    }

    private PacketWriter? _sbPrepW;

    /// <summary>Client: a shipped blueprint from the parts menu, placed with a ghost.</summary>
    public void SandboxPrepareBlueprint(int index)
    {
        var all = ConstructBuild.Blueprints;
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Blueprint).U16((ushort)index);
        SpawnPrepareSend(w, index >= 0 && index < all.Count ? all[index].Name : "blueprint");
    }

    /// <summary>Client: place the ghost at a surface point, turned by a yaw (the host checks it all again).</summary>
    public void SpawnPlaceSend(Vector3 at, float yaw)
    {
        if (GhostState != BlueprintGhostState.Carrying)
            return;
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.SpawnPlace).U16(GhostToken).Vec(at).F32(yaw);
        SpawnActSend(w);
    }

    /// <summary>Client: the ghost (or the spawn being made) is dropped, here and on the host.</summary>
    public void SpawnCancelSend()
    {
        if (GhostState == BlueprintGhostState.None)
            return;
        var token = GhostState == BlueprintGhostState.Carrying ? GhostToken : (ushort)0; // (0: whatever is being made)
        GhostState = BlueprintGhostState.None;
        GhostCount = 0;
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.SpawnCancel).U16(token);
        SpawnActSend(w);
    }

    private void SpawnActSend(PacketWriter w)
    {
        if (IsServer)
        {
            if (Local is { } me)
            {
                var r = new PacketReader(w.Span[1..]);
                if (SandboxActGuarded(me, ref r) is { } why)
                    SandboxSay(why);
            }
            return;
        }
        Net.SendToServer(w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client (and the host's own): CActKind.SpawnGhost after its kind byte.</summary>
    private void SpawnGhostRead(ref PacketReader r)
    {
        var status = r.U8();
        var token = r.U16();
        var name = r.Text(120);
        if (status != 0)
        {
            if (GhostState == BlueprintGhostState.Preparing || GhostToken == token)
            {
                GhostState = BlueprintGhostState.None;
                GhostCount = 0;
            }
            return;
        }
        if (GhostState != BlueprintGhostState.Preparing)
        {
            // (cancelled here while it was made: the host drops it too)
            var w = SbW;
            w.Begin(Msg.CAct).U8((byte)CActKind.SpawnCancel).U16(token);
            SpawnActSend(w);
            return;
        }
        GhostMin = r.Vec();
        GhostMax = r.Vec();
        GhostLow = r.F32();
        var n = r.U16();
        if (GhostBoxes.Length < n)
        {
            GhostBoxes = new Matrix4x4[n];
            GhostColours = new Vector3[n];
            GhostAlpha = new float[n];
            GhostShapes = new int[n];
        }
        for (var k = 0; k < n; k++)
        {
            var t = new Vector3(r.F32(), r.F32(), r.F32());
            var s = new Vector3((float)BitConverter.UInt16BitsToHalf(r.U16()), (float)BitConverter.UInt16BitsToHalf(r.U16()), (float)BitConverter.UInt16BitsToHalf(r.U16()));
            var q = new Quaternion(Snorm(r.U16()), Snorm(r.U16()), Snorm(r.U16()), Snorm(r.U16()));
            q = q.LengthSquared() > 1e-6f ? Quaternion.Normalize(q) : Quaternion.Identity;
            GhostBoxes[k] = Matrix4x4.CreateScale(s * 1.005f) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t);
            GhostColours[k] = new Vector3(r.U8(), r.U8(), r.U8()) / 255f;
            GhostAlpha[k] = r.U8() / 255f;
            GhostShapes[k] = r.U8();
        }
        GhostCount = n;
        GhostToken = token;
        GhostName = name;
        GhostState = BlueprintGhostState.Carrying;
        GhostSince = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    }

    private static float Snorm(ushort v) => unchecked((short)v) / 32767f;
}
