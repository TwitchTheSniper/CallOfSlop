using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Terrain destruction (Sandbox survival design, decision 16): blasts leave craters in the ground (TerrainEdits.cs). The
/// server decides: a blast in Sandbox, with the lobby's Destruction setting on, on a map with terrain or a planetoid
/// (PlanetEdits.cs), takes a crater out
/// (a fraction of the blast's radius), logs the op and sends it to everyone (<see cref="Msg.TerrainDig"/>); joiners get
/// the whole log. Every peer applies the same ops in the same order with the same maths, so the ground matches. Bodies
/// near a crater are woken so they fall in. Other modes never dig.
/// </summary>
public sealed partial class Game
{
    /// <summary>A crater's radius for a blast's radius, and its smallest and largest.</summary>
    public const float CraterPerBlast = 0.4f, CraterMin = 0.75f, CraterMax = 6f;

    /// <summary>The biggest crater a prop's own "crater" may ask for (a small nuke).</summary>
    public const float BigCraterMax = 60f;

    /// <summary>Most ops in one <see cref="Msg.TerrainDig"/> packet (the joiner's state is sent in packets of this many).</summary>
    public const int DigOpsPerPacket = 64;

    /// <summary>Does ground get dug in this game on this map (the server's view)? A flat map's terrain or a planetoid's ground.</summary>
    public bool TerrainDigLive => SandboxOn && DestructionEnabled && (World.Map.Terrain != null || World.Map.Planetoid != null);

    /// <summary>Ops applied this match (the self-test and the perf window).</summary>
    public int DigOps => DigGroundIfMade?.Ops.Count ?? 0;

    /// <summary>The map's diggable ground (its terrain, else its planetoid; made on first use), or null when it has neither.</summary>
    internal static IDigGround? DigGround => World.Map.Terrain is { } t ? t.Edits : World.Map.Planetoid?.Edits;

    /// <summary><see cref="DigGround"/> if anything has dug it yet.</summary>
    internal static IDigGround? DigGroundIfMade => World.Map.Terrain is { } t ? t.EditsIfMade : World.Map.Planetoid?.EditsIfMade;

    /// <summary>Server: a blast at <paramref name="at"/> with radius <paramref name="blastRadius"/> leaves a crater (Sandbox only).</summary>
    internal void BlastCrater(Vector3 at, float blastRadius)
    {
        if (!IsServer || !TerrainDigLive || !(blastRadius > 0))
            return;
        var r = Math.Clamp(blastRadius * CraterPerBlast, CraterMin, CraterMax);
        Dig(new DigOp(DigKind.Crater, at, r));
    }

    /// <summary>Server: a crater of its own size (a bomb prop's "crater"; Sandbox only).</summary>
    internal void BlastCraterSized(Vector3 at, float craterRadius)
    {
        if (!IsServer || !TerrainDigLive || !(craterRadius > 0))
            return;
        Dig(new DigOp(DigKind.Crater, at, Math.Clamp(craterRadius, CraterMin, BigCraterMax)));
    }

    /// <summary>Server: a working drill's scoop (the hook of <see cref="ConstructWorld.DrillDig"/>; Sandbox only: off, the drill still taps).</summary>
    private int DrillScoop(DigOp op) => TerrainDigLive ? Dig(op) : 0;

    /// <summary>Server: applies one op, tells everyone and wakes what rests nearby. Returns the columns it changed.</summary>
    internal int Dig(DigOp op)
    {
        if (!IsServer || DigGround is not { } g)
            return 0;
        op = op.Quantised;
        var changed = g.Apply(op);
        if (changed == 0)
            return 0;
        DigMirror?.Edits.Apply(op);
        PlanetDigMirror?.Edits.Apply(op);
        AfterDig(op);
        _w.Begin(Msg.TerrainDig).U16(1);
        WriteDigOp(_w, op);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        return changed;
    }

    /// <summary>What dug ground changes beyond the ground itself: constructs resting on it wake (and fall in).</summary>
    private void AfterDig(DigOp op)
    {
        if (!IsServer || Constructs.Bodies.Count == 0)
            return;
        var reach = op.Radius + 1f;
        foreach (var c in Constructs.Bodies)
            if (Vector3.Distance(c.WorldCom, op.At) < reach + c.Radius)
                c.Wake();
    }

    public static void WriteDigOp(PacketWriter w, in DigOp op)
    {
        w.U8((byte)((byte)op.Kind | (op.Sliced ? DigOp.SlicedMark : 0))).Vec(op.At).U16((ushort)MathF.Round(op.Radius * 100f));
        if (op.Sliced)
            w.U8(op.Slice).U8(op.Slices); // (protocol 195: a band of a big crater)
        if (GroundShape.IsGrade(op.Kind))
            w.Vec(op.To); // (protocol 215: a grade's far end)
    }

    public static bool ReadDigOp(ref PacketReader r, out DigOp op)
    {
        op = default;
        if (!r.Has(DigOp.WireBytes))
            return false;
        var b = r.U8();
        var kind = (DigKind)(b & ~DigOp.SlicedMark);
        var at = r.Vec();
        var radius = r.U16() / 100f;
        if (kind > GroundShape.LastKind)
            return false;
        byte slice = 0, slices = 0;
        if ((b & DigOp.SlicedMark) != 0)
        {
            if (!r.Has(DigOp.SliceBytes))
                return false;
            (slice, slices) = (r.U8(), r.U8());
        }
        var to = default(Vector3);
        if (GroundShape.IsGrade(kind))
        {
            if (!r.Has(DigOp.ToBytes))
                return false;
            to = r.Vec();
        }
        op = new DigOp(kind, at, radius, slice, slices, to);
        return true;
    }

    /// <summary>Server: sends a joining player every op of this match, in order.</summary>
    private void SendTerrainDigState(RakNetGUID to)
    {
        if (DigGroundIfMade is { } e)
            WriteTerrainDigState(_w, e, w => Net.SendTo(to, w.Span, PacketReliability.ReliableOrdered));
    }

    /// <summary>Every op of the log, <see cref="DigOpsPerPacket"/> to a packet, in order. Returns the packets written.</summary>
    public static int WriteTerrainDigState(PacketWriter w, IDigGround e, Action<PacketWriter> send)
    {
        var packets = 0;
        var log = e.Ops;
        for (var at = 0; at < log.Count; at += DigOpsPerPacket)
        {
            var n = Math.Min(DigOpsPerPacket, log.Count - at);
            w.Begin(Msg.TerrainDig).U16((ushort)n);
            for (var k = 0; k < n; k++)
                WriteDigOp(w, log[at + k]);
            send(w);
            packets++;
        }
        return packets;
    }

    /// <summary>Ops a client has read from <see cref="Msg.TerrainDig"/> packets (the self-test's loopback).</summary>
    public int DigOpsRead { get; private set; }

    /// <summary>The self-test: this client applies ops to a terrain of its own (peers in one process share the map, and a joiner's map load restores it), and the server copies its ops to one, so the grounds can be compared.</summary>
    internal TerrainData? DigMirror;

    /// <summary>The same for a planetoid map (a copy of the planetoid of its own).</summary>
    internal Planetoid? PlanetDigMirror;

    /// <summary>Client: a <see cref="Msg.TerrainDig"/> (after the id): applies its ops in order.</summary>
    internal void ReadTerrainDig(ref PacketReader r)
    {
        if (IsServer)
            return;
        if ((DigMirror ?? World.Map.Terrain) is { } t)
            DigOpsRead += ApplyTerrainDig(t.Edits, ref r);
        else if ((PlanetDigMirror ?? World.Map.Planetoid) is { } pl)
            DigOpsRead += ApplyTerrainDig(pl.Edits, ref r);
    }

    /// <summary>Applies a <see cref="Msg.TerrainDig"/> packet's ops (after the id) to a terrain. Returns the ops read.</summary>
    public static int ApplyTerrainDig(TerrainData t, ref PacketReader r) => ApplyTerrainDig(t.Edits, ref r);

    /// <summary>Applies a <see cref="Msg.TerrainDig"/> packet's ops (after the id) to dug ground (a terrain's or a planetoid's). Returns the ops read.</summary>
    public static int ApplyTerrainDig(IDigGround g, ref PacketReader r)
    {
        if (!r.Has(2))
            return 0;
        int n = r.U16();
        var read = 0;
        for (; read < n && ReadDigOp(ref r, out var op); read++)
            g.Apply(op);
        return read;
    }
}
