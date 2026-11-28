using System.Numerics;

namespace CallOfSlop;

public sealed partial class Construct
{
    public ushort[] PadOwner = []; // each Pad Tile's owner (the tile whose store its rectangle uses), ushort.MaxValue: itself (ConstructPads)
    public ConstructPartDef?[] PadDefs = []; // ...and its rack def: the rectangle's hold seen from that tile (null: the part's own def)
}

/// <summary>
/// Pad Tiles (design page drawing 23; docs/sandbox/constructs/production.md "Pad Tiles"): 2 m tiles with no Core that weld into one
/// store. MachineBlocks finds each weld of tiles from its lowest-index tile (the seed, <c>machineBlock.seed</c>) and the largest full
/// rectangle holding it; that tile owns the store, and every tile in the rectangle is a view of it: <see cref="Owner"/> sends a store
/// lookup to the owner (ConstructWorld.StoreOf), and <see cref="RackDef"/> gives each tile the rectangle's hold in the tile's own
/// frame, so whatever reads a rack's hold through its own part (packing, a conveyor's end, F, a lifter's put, the drawing) sees the
/// same pieces in the same places from any tile. Masts stacked on the rectangle raise the stack: <see cref="BaseStack"/> with none,
/// <see cref="PerMast"/> more for each Mast Block of the tallest column. A tile outside the rectangle is a pad of its own. Built at
/// the rebuild on every peer from the parts alone: nothing here is saved or sent, and nothing allocates in a steady state (a derived
/// def is made once per shape and kept).
/// </summary>
public static class ConstructPads
{
    public const int PerTile = 500; // pieces a tile adds to its rectangle's count
    public const float BaseStack = 1f, PerMast = 2f; // m of stack with no mast, and for each Mast Block on the tallest column

    private static readonly Dictionary<(ConstructPartDef Def, sbyte Dx, sbyte Dz, byte W, byte L, byte Cols, byte Flip), ConstructPartDef> _defs = [];
    [ThreadStatic] private static byte[]? _flips; // (each assembly's FeedFlip during one Build: 0 not yet worked out, else 1 + its bits)
    private static readonly Lock _gate = new();

    /// <summary>The part whose store part <paramref name="part"/> uses: its pad's owner tile, else itself.</summary>
    public static int Owner(Construct c, int part) => (uint)part < (uint)c.PadOwner.Length && c.PadOwner[part] != ushort.MaxValue ? c.PadOwner[part] : part;

    /// <summary>A rack's def as its hold is now: a Pad Tile's rectangle seen from it, else the part's own.</summary>
    public static ConstructPartDef RackDef(Construct c, int part) => (uint)part < (uint)c.PadDefs.Length && c.PadDefs[part] is { } d ? d : c.DefOf(part);

    /// <summary>Is part <paramref name="part"/> a tile of a bigger pad that isn't its owner (its store is the owner's: lists of stores skip it)?</summary>
    public static bool IsMember(Construct c, int part) => Owner(c, part) != part;

    /// <summary>The pad's size in tiles and its stack's height (the look line): "pad 3 x 2 tiles, stacks 3 m".</summary>
    public static string Describe(Construct c, int part)
    {
        var o = Owner(c, part);
        if (!MachineBlocks.TryOf(c, o, out var asm) || asm.W == 0)
            return "";
        var rd = RackDef(c, o);
        return $"pad {asm.W} x {asm.L} tiles, stacks {rd.RackHoldMax.Y - rd.RackHoldMin.Y:0.#} m";
    }

    /// <summary>After MachineBlocks.Build: each seeded assembly's tiles in its rectangle point to their owner and the rectangle's def.</summary>
    internal static void Build(Construct c)
    {
        var any = false;
        for (var a = 0; a < c.MachineAsmCount && !any; a++)
            any = c.Parts[c.MachineAsms[a].Core].D.BlockSeed;
        if (!any)
        {
            if (c.PadOwner.Length > 0)
            {
                c.PadOwner = [];
                c.PadDefs = [];
            }
            return;
        }
        var n = c.PartCount;
        if (c.PadOwner.Length < n)
        {
            c.PadOwner = new ushort[Math.Max(n, c.PadOwner.Length * 2)];
            c.PadDefs = new ConstructPartDef?[c.PadOwner.Length];
        }
        Array.Fill(c.PadOwner, ushort.MaxValue);
        Array.Clear(c.PadDefs);
        if (_flips == null || _flips.Length < c.MachineAsmCount)
            _flips = new byte[Math.Max(c.MachineAsmCount, (_flips?.Length ?? 16) * 2)];
        Array.Clear(_flips, 0, c.MachineAsmCount);
        // One pass over the parts (a tile's assembly is BlockAsmOf): each tile in its owner's rectangle points to it and the rectangle's def.
        for (var i = 0; i < n; i++)
        {
            var a = c.BlockAsmOf[i];
            if (a < 0 || !c.Parts[i].D.BlockSeed)
                continue;
            ref readonly var asm = ref c.MachineAsms[a];
            var owner = asm.Core;
            var od = c.Parts[owner].D;
            if (asm.W == 0 || !od.IsPieceRack || c.Parts[i].Def != c.Parts[owner].Def)
                continue;
            var rel = Rot24.Apply(MachineBlocks.Rot24Inverse(c.Parts[owner].Rot), c.Parts[i].Pos - c.Parts[owner].Pos) / od.BlockCell;
            int tx = (int)MathF.Round(rel.X), tz = (int)MathF.Round(rel.Z);
            if (tx < asm.X0 || tx >= asm.X0 + asm.W || tz < asm.Z0 || tz >= asm.Z0 + asm.L)
                continue; // (an idle tile: a pad of its own)
            c.PadOwner[i] = (ushort)owner;
            if (_flips![a] == 0)
                _flips[a] = (byte)(1 + FeedFlip(c, a));
            c.PadDefs[i] = Derived(od, asm.X0 - tx, asm.Z0 - tz, asm.W, asm.L, Columns(c, asm), (byte)(_flips[a] - 1));
        }
    }

    /// <summary>The tallest column of masts stood on an assembly's rectangle.</summary>
    private static int Columns(Construct c, in CMachineAsm asm)
    {
        var cols = 0;
        for (var x = asm.X0; x < asm.X0 + asm.W; x++)
            for (var z = asm.Z0; z < asm.Z0 + asm.L; z++)
                cols = Math.Max(cols, MachineBlocks.ColumnAt(c, asm, x, z));
        return cols;
    }

    /// <summary>
    /// Which way pad <paramref name="a"/>'s places are mirrored (bit 1: along its x, bit 2: along its z), so that it fills from the side away
    /// from where its pieces come in: a conveyor ending on one of its tiles, a Roller Tile run whose out end butts it, or a machine's Out
    /// Table beside it; along that side, from the end away from where they come in. Packing puts the first column at the hold's -z end
    /// and the first stack at its -x side; a pad fed from that end filled its near corner first, and each piece after slid in through the
    /// ones parked there (Sean, 2026-11-27). Only the drawn places move (ConstructRackPack.Origin):
    /// packing, the store and the wire are as before, and both peers work it out from the parts and links alone. Fed from no side, or
    /// from both ends alike: no mirror.
    /// </summary>
    private static int FeedFlip(Construct c, int a)
    {
        ref readonly var asm = ref c.MachineAsms[a];
        ref readonly var core = ref c.Parts[asm.Core];
        var cell = core.D.BlockCell;
        if (asm.W == 0 || cell <= 0)
            return 0;
        var inv = MachineBlocks.Rot24Inverse(core.Rot);
        int sx = 0, sz = 0;
        float ox = 0, oz = 0;
        var mid = new Vector2(asm.X0 + (asm.W - 1) * 0.5f, asm.Z0 + (asm.L - 1) * 0.5f);
        var half = new Vector2(asm.W * 0.5f, asm.L * 0.5f);
        var o = core.Pos;
        // Conveyors ending on a tile of it: where each comes from, its last bend (or its start).
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref readonly var r = ref c.Routes[k];
            if (r.Type != RouteType.Conveyor || r.To >= c.PartCount || r.To >= c.BlockAsmOf.Length || c.BlockAsmOf[r.To] != a || RollerRuns.IsRun(c, k))
                continue; // (a Roller Tile run's own conveyor is counted by its tiles below, laid or not)
            if (r.Count > 0)
                Feed(c.RouteNodes[r.Start + r.Count - 1].Pos, o, inv, cell, mid, half, ref sx, ref sz, ref ox, ref oz);
            else if (r.Parent == CRoute.NoParent && r.From < c.PartCount && r.From != r.To)
                Feed(c.Parts[r.From].Pos, o, inv, cell, mid, half, ref sx, ref sz, ref ox, ref oz);
        }
        // Roller Tile runs whose out end (a run goes along its seed's +z) butts the pad: their conveyors are laid on the server's next
        // conveyor step after this rebuild (RollerRuns.Sync), so the tiles are read, not the links.
        for (var b = 0; b < c.MachineAsmCount; b++)
        {
            ref readonly var run = ref c.MachineAsms[b];
            ref readonly var seed = ref c.Parts[run.Core];
            if (b == a || run.W == 0 || !seed.D.IsRollerTile || seed.Seg != core.Seg)
                continue;
            var rc = RollerRuns.Cell;
            var lx = (run.X0 + (run.W - 1) * 0.5f) * rc;
            float lastZ = (run.Z0 + run.L - 1) * rc;
            var past = Rot24.Apply(inv, seed.Pos + Rot24.Apply(seed.Rot, new Vector3(lx, 0, lastZ + rc)) - o) / cell;
            if (MathF.Abs(past.Y) * cell < 1.5f && past.X > asm.X0 - 0.5f && past.X < asm.X0 + asm.W - 0.5f && past.Z > asm.Z0 - 0.5f && past.Z < asm.Z0 + asm.L - 0.5f)
                Feed(seed.Pos + Rot24.Apply(seed.Rot, new Vector3(lx, 0, lastZ)), o, inv, cell, mid, half, ref sx, ref sz, ref ox, ref oz);
        }
        // A machine's Out Table just past its edge (a bed a piece rolls across onto the pad: ConstructConveyor.OutPad).
        for (var b = 0; b < c.MachineAsmCount; b++)
        {
            if (b == a || c.MachineAsms[b].Out >= c.PartCount || MachineBlocks.OutTable(c, c.MachineAsms[b]) is not (>= 0 and var t) || c.Parts[t].Seg != core.Seg)
                continue;
            var rel = Rot24.Apply(inv, c.Parts[t].Pos - o) / cell;
            if (MathF.Abs(rel.Y) * cell < 1.5f && rel.X > asm.X0 - 1.6f && rel.X < asm.X0 + asm.W - 1 + 1.6f && rel.Z > asm.Z0 - 1.6f && rel.Z < asm.Z0 + asm.L - 1 + 1.6f)
                Feed(c.Parts[t].Pos, o, inv, cell, mid, half, ref sx, ref sz, ref ox, ref oz);
        }
        // Along the side it comes in by, the places start at the end away from where it comes in, so a piece slides past none of the
        // columns parked before it (they lie beyond its place).
        if (sz != 0 && Math.Abs(sz) >= Math.Abs(sx))
            return (sz < 0 ? 2 : 0) | (ox < -0.05f ? 1 : 0);
        if (sx != 0)
            return (sx < 0 ? 1 : 0) | (oz < -0.05f ? 2 : 0);
        return 0;
    }

    /// <summary>One way in at <paramref name="at"/> (body frame) counted on the pad's axis it comes in along, by side, and where it is along both (over the pad's half size).</summary>
    private static void Feed(Vector3 at, Vector3 o, byte inv, float cell, Vector2 mid, Vector2 half, ref int sx, ref int sz, ref float ox, ref float oz)
    {
        var rel = Rot24.Apply(inv, at - o) / cell;
        float u = (rel.X - mid.X) / half.X, v = (rel.Z - mid.Y) / half.Y;
        if (MathF.Abs(v) >= MathF.Abs(u))
            sz += MathF.Sign(v);
        else
            sx += MathF.Sign(u);
        ox += u; // (where along the side: the other axis's mirror)
        oz += v;
    }

    /// <summary>The tile def <paramref name="tile"/> as the hold of a W by L rectangle whose first cell is (dx, dz) tiles from it, its stack raised by its masts.</summary>
    private static ConstructPartDef Derived(ConstructPartDef tile, int dx, int dz, int w, int l, int cols, byte flip)
    {
        var key = (tile, (sbyte)dx, (sbyte)dz, (byte)w, (byte)l, (byte)Math.Min(cols, 255), flip);
        lock (_gate)
        {
            if (_defs.TryGetValue(key, out var d))
                return d;
            var cell = tile.BlockCell;
            ConstructRackPack.Hold(tile, out var lo, out var hi);
            d = tile.ShallowCopy();
            d.RackHoldMin = lo + new Vector3(dx * cell, 0, dz * cell);
            d.RackHoldMax = new Vector3(hi.X + (dx + w - 1) * cell, hi.Y + PerMast * cols, hi.Z + (dz + l - 1) * cell);
            d.RackSection = new Vector2(d.RackHoldMax.X - d.RackHoldMin.X, d.RackHoldMax.Y - d.RackHoldMin.Y);
            d.RackLength = d.RackHoldMax.Z - d.RackHoldMin.Z;
            d.RackCount = Math.Min(CStore.MaxPerKind, Math.Max(tile.RackCount, PerTile * w * l));
            d.RackFlipX = (flip & 1) != 0;
            d.RackFlipZ = (flip & 2) != 0;
            _defs[key] = d;
            return d;
        }
    }
}
