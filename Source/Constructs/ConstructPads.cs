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

    private static readonly Dictionary<(ushort Def, sbyte Dx, sbyte Dz, byte W, byte L, byte Cols), ConstructPartDef> _defs = [];
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
        for (var a = 0; a < c.MachineAsmCount; a++)
        {
            ref readonly var asm = ref c.MachineAsms[a];
            var owner = asm.Core;
            var od = c.Parts[owner].D;
            if (!od.BlockSeed || asm.W == 0 || !od.IsPieceRack)
                continue;
            var cols = 0;
            for (var x = asm.X0; x < asm.X0 + asm.W; x++)
                for (var z = asm.Z0; z < asm.Z0 + asm.L; z++)
                    cols = Math.Max(cols, MachineBlocks.ColumnAt(c, asm, x, z));
            var unrot = MachineBlocks.Rot24Inverse(c.Parts[owner].Rot);
            for (var i = 0; i < n; i++)
            {
                if (c.BlockAsmOf[i] != a || c.Parts[i].Def != c.Parts[owner].Def)
                    continue;
                var rel = Rot24.Apply(unrot, c.Parts[i].Pos - c.Parts[owner].Pos) / od.BlockCell;
                int tx = (int)MathF.Round(rel.X), tz = (int)MathF.Round(rel.Z);
                if (tx < asm.X0 || tx >= asm.X0 + asm.W || tz < asm.Z0 || tz >= asm.Z0 + asm.L)
                    continue; // (an idle tile: a pad of its own)
                c.PadOwner[i] = (ushort)owner;
                c.PadDefs[i] = Derived(od, asm.X0 - tx, asm.Z0 - tz, asm.W, asm.L, cols);
            }
        }
    }

    /// <summary>The tile def <paramref name="tile"/> as the hold of a W by L rectangle whose first cell is (dx, dz) tiles from it, its stack raised by its masts.</summary>
    private static ConstructPartDef Derived(ConstructPartDef tile, int dx, int dz, int w, int l, int cols)
    {
        var key = ((ushort)tile.Id, (sbyte)dx, (sbyte)dz, (byte)w, (byte)l, (byte)Math.Min(cols, 255));
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
            _defs[key] = d;
            return d;
        }
    }
}
