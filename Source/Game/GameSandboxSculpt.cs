using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The ground drill on the server (docs/sandbox/TERRAIN-DIGGING.md, "Sculpting"): a client asks for one scoop or one fill at a
/// point (<see cref="CActKind.Sculpt"/>, at most one a repeat time) and the server checks the tool, the reach and the rules,
/// measures it (<see cref="TerrainEdits.Measure"/>), applies it through <see cref="Dig"/> (so it is logged, sent and wakes
/// what rests nearby) and adds or takes the carried material. A refusal goes back as a notice. Nothing allocates unless it refuses.
/// </summary>
public sealed partial class Game
{
    /// <summary>How many actions may arrive bunched up in a row (a client keeps the repeat time, the network may not).</summary>
    public const int SculptBurst = 3;

    /// <summary>The ground drill's HUD line (what is carried, what RMB places, the keys). The tool's frame sets it.</summary>
    public string SandboxSculptHint = "";

    private readonly float[] _sculptTokens = new float[256];
    private readonly double[] _sculptAt = new double[256];

    /// <summary>Client (or the host itself): dig or fill at a point. The server checks everything again.</summary>
    public void SandboxSculpt(DigKind kind, Vector3 at)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Sculpt).U8((byte)kind).Vec(at);
        SandboxSendBuilt(w);
    }

    /// <summary>Why the ground can't be dug on this map in this game (null: it can): the same line on the client's HUD and the server.</summary>
    private string? SculptRefusal() =>
        World.Map.Terrain == null ? "there is no ground to dig on this map"
        : !DestructionEnabled ? "the ground can't be changed: Destruction is off in the lobby"
        : null;

    private string? SandboxSculptAct(Player p, ref PacketReader r, Vector3 eye, double now)
    {
        if (!r.Has(1 + 12))
            return null;
        var kind = (DigKind)r.U8();
        var at = r.Vec();
        ref var h = ref SandboxHands.Hand(p.Id);
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.GroundDrill)
            return "take out the ground drill (the tool wheel) to dig";
        if (kind != DigKind.Scoop && kind is not (DigKind.FillDirt or DigKind.FillStone or DigKind.FillSand) || !float.IsFinite(at.X + at.Y + at.Z))
            return null;
        if (SculptRefusal() is { } off)
            return off;
        var table = DiggingTable.Current;
        if (Vector3.Distance(eye, at) > table.Reach + 0.75f)
            return "too far away";
        // The rate: a bucket, so a bunch of packets a client spaced out still get through, and one that asks faster gets nothing.
        var tokens = MathF.Min(_sculptTokens[p.Id] + (float)((now - _sculptAt[p.Id]) / table.RepeatSeconds), SculptBurst);
        _sculptAt[p.Id] = now;
        if (tokens < 1)
        {
            _sculptTokens[p.Id] = tokens;
            return null;
        }
        _sculptTokens[p.Id] = tokens - 1;
        var t = World.Map.Terrain!;
        var e = t.Edits;
        if (e.Log.Count >= table.LogCap)
            return $"the ground has taken all the changes it can this match ({e.Log.Count:N0}): nothing more can be dug or filled";
        var inv = SandboxInv.Of(p.Id);
        var op = new DigOp(kind, at, table.ScoopRadius).Quantised;
        Span<float> volume = stackalloc float[(int)GroundKind.COUNT];
        var total = e.Measure(in op, table.Ground, table.StoneDepth, volume);
        return kind == DigKind.Scoop ? SculptDig(table, inv, in op, total, volume) : SculptFill(table, t, inv, in op, total, volume);
    }

    private string? SculptDig(DiggingTable table, CallOfSlop.SandboxInventory inv, in DigOp op, float total, ReadOnlySpan<float> volume)
    {
        if (total <= 0)
            return "nothing to dig there";
        if (!inv.Infinite)
            for (var k = 0; k < volume.Length; k++)
                if (volume[k] > 0 && inv.Materials[MaterialOf((GroundKind)k)] >= CallOfSlop.SandboxInventory.MaxCount)
                    return $"you can't carry any more {DiggingTable.KindNames[k]} ({CallOfSlop.SandboxInventory.MaxCount})";
        if (Dig(op) == 0)
            return "the ground there can't be cut any finer";
        for (var k = 0; k < volume.Length; k++)
            inv.AddBulk(MaterialOf((GroundKind)k), volume[k] * table.Units[k]);
        return null;
    }

    private string? SculptFill(DiggingTable table, TerrainData t, CallOfSlop.SandboxInventory inv, in DigOp op, float total, ReadOnlySpan<float> volume)
    {
        var kind = (GroundKind)(op.Kind - DigKind.FillDirt);
        var name = DiggingTable.KindNames[(int)kind];
        if (total <= 0)
            return "nothing to fill there";
        var top = op.At.Y + op.Radius;
        if (top > DrillShaft.Surface(t, op.At.X, op.At.Z) + table.MaxAboveSurface)
            return $"too high: ground goes at most {table.MaxAboveSurface:0} m over the surface";
        var cost = volume[(int)kind] * table.Units[(int)kind];
        var id = MaterialOf(kind);
        if (!inv.Infinite && inv.BulkOf(id) < cost)
            return $"not enough {name}: that takes {MathF.Ceiling(cost):0}, you carry {inv.Materials[id]}";
        if (FillBuries(t.Edits, in op) is { } why)
            return why;
        if (Dig(op) == 0)
            return "the ground there can't take any more";
        if (!inv.Infinite)
            inv.PayBulk(id, cost);
        return null;
    }

    /// <summary>The carried material of a kind of ground.</summary>
    public static byte MaterialOf(GroundKind k) => RecyclingTable.Current.IdOf(DiggingTable.KindNames[(int)k]);

    /// <summary>Who a fill would bury, as a refusal (null: nobody). Players (bots too) and every body's parts, column by column.</summary>
    internal string? FillBuries(TerrainEdits e, in DigOp op)
    {
        var r = World.PlayerRadius;
        foreach (var p in Players.Values)
        {
            if (!p.Alive)
                continue;
            var min = p.Position - new Vector3(r, 0, r);
            var max = p.Position + new Vector3(r, World.HeightOf(p.Sim.Stance), r);
            if (e.FillTouches(in op, min, max))
                return p.Id == LocalId || Players.Count == 1 ? "that would bury a player" : $"that would bury {p.Name}";
        }
        foreach (var c in Constructs.Bodies)
        {
            c.WorldBounds(out var bmin, out var bmax);
            var reach = new Vector3(op.Radius);
            if (bmin.X > op.At.X + reach.X || bmax.X < op.At.X - reach.X || bmin.Z > op.At.Z + reach.Z || bmax.Z < op.At.Z - reach.Z || bmin.Y > op.At.Y + reach.Y || bmax.Y < op.At.Y - 2 * reach.Y)
                continue;
            for (var i = 0; i < c.PartCount; i++)
            {
                c.ShapeBounds(i, out var lo, out var hi);
                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);
                for (var k = 0; k < 8; k++)
                {
                    var w = c.ToWorld(new Vector3((k & 1) == 0 ? lo.X : hi.X, (k & 2) == 0 ? lo.Y : hi.Y, (k & 4) == 0 ? lo.Z : hi.Z));
                    min = Vector3.Min(min, w);
                    max = Vector3.Max(max, w);
                }
                if (e.FillTouches(in op, min, max))
                    return "a construct is in the way";
            }
        }
        return null;
    }
}
