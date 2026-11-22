using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a part is to a block machine ("machineBlock" in its file; MachineBlocks.cs).</summary>
public enum BlockRole : byte
{
    None,
    Core, // the machine's brain: its screen, ports, settings and store; an assembly is found from it
    Block, // one identical cell of one family's machine (an Extruder's Die Block, a Press Block)
    Shared, // a cell any family's machine takes in (the 2 m cube used as masts and walls)
    End, // a cell that closes a bed's far end (the Out Table): any family's machine takes it in, never as bed; nothing floods on past it
    Tier, // a tier piece bolted on a Core's tier slot (design page drawing 21): the Power Coupling (2) to the Plasma Conditioner (5); never a cell
    Carriage, // an Assembler's Gantry Carriage, welded on an Assembly Bed Block's rail edge: one more part built at once; never a cell
    Tool, // an Assembler's tool block (Thruster Jig Block, Sewing Table Block) docked in its Core's Tool Bay, a side the bed doesn't use; never a cell
}

/// <summary>A Core's machine, found at the rebuild (MachineBlocks.Build): the cells joined to it and the bed rectangle they make.</summary>
public struct CMachineAsm
{
    public ushort Core; // the Core part
    public byte Family; // MachineBlocks.Families index
    public short Cells; // blocks and shared cells taken in (the Core not counted)
    public short Idle; // of those, cells on the bed layer outside the rectangle
    public sbyte X0, Z0; // the rectangle's first cell, in the Core's cell frame (x across, z along the Core's +z)
    public byte W, L; // its size in cells (0: no rectangle)
    public bool Capped; // more cells were joined than MachineBlocks.MaxCells (the rest are idle)
    public short Blocker; // a machine's own check at the rebuild (a Press's shells' swing: ConstructPress.SwingCheck): the part in the way, or -1
    public int Grid; // the assembly's slot in the construct's column grid (MachineBlocks.GridSide squared bytes a slot)
    public ushort Out; // the first "end" cell (Out Table) just past the bed's far edge, the side away from the Core (ushort.MaxValue: none)
    public byte OutCells; // the bed's far edge cells that an end cell covers (W or L when the whole edge is closed)
    public byte OutSide; // the far edge's side in the Core's frame (a direction, ConstructParts.DirVector: 0 +x, 1 -x, 4 +z, 5 -z)
    public byte Tier; // the machine's tier, 1 to 5: the highest n with a tier piece of every tier from 2 to n bolted on its Core (drawing 21)
    public byte TierPieces; // bits by tier (2 to 5) of the tier pieces bolted on its Core, a gap included
    public byte Overdrive, Buffers; // an Assembler's Overdrive Units and Feed Buffers on its upgrade slots (a tier piece with a "module")
    public byte Carriages; // an Assembler's Gantry Carriages on its bed's blocks (inside the rectangle), at most MachineBlocks.MaxCarriages
    public byte Masts; // an Assembler's masts outside its bed: the shortest of its four corner columns, in cells (0: a corner has none)
    public byte Tool; // the tool block docked in an Assembler's Tool Bay: an index of AssemblingTable.Machines (1 thruster, 2 tailor), 0 none
    public ushort ToolPart; // that block's part (ushort.MaxValue: none)
}

public sealed partial class Construct
{
    public CMachineAsm[] MachineAsms = []; // the block machines' assemblies (MachineBlocks.Build)
    public int MachineAsmCount;
    public short[] BlockAsmOf = []; // each part's assembly, or -1 (sized to PartCount while any assembly exists)
    public byte[] BlockJoined = []; // each part's faces (bits by direction, part frame) welded to a cell of its own assembly
    public int[] BlockCellOf = []; // each part's cell in its Core's cell frame (MachineBlocks.CellOf: x, y, z packed a byte each, 128 the Core's)
    public byte[] BlockColumns = []; // per assembly, GridSide x GridSide column heights (bit 7: a bed cell; bits 0..6: cells stacked on it)
}

/// <summary>
/// The shared block system of the production machines (design page: "Direction", drawings 14 to 20): a machine is a Core
/// plus any number of identical cells welded to it and to each other. Every peer finds the same assembly from the parts at the
/// rebuild: from each Core, a flood through welds over cells of its family (and shared cells), each cell placed on the Core's
/// lattice. Cells on the Core's own layer make the bed; the machine's size is the largest full rectangle of them, at most
/// <see cref="MaxSide"/> cells a side and <see cref="MaxCells"/> in all, unless its Core's file gives its own (<c>"maxSide"</c>,
/// <c>"maxCells"</c>: the Building Station's bay, up to <see cref="GridHalf"/> and <see cref="MaxCellsAny"/>). Cells stacked on a bed cell are its column (a press's
/// wall, a mast). A cell's faces welded to its own assembly are kept (<see cref="Construct.BlockJoined"/>) so a box marked
/// "seam" on that face isn't drawn. Nothing here runs per tick, allocates in a steady state or goes on the wire.
/// </summary>
public static class MachineBlocks
{
    /// <summary>A bit of <see cref="Construct.BlockJoined"/> past the six faces: the part is a cell of a machine's assembly ("inMachine" boxes draw).</summary>
    public const byte InMachine = 0x40;
    public const int MaxSide = 16; // cells along either side of a bed rectangle (a Core's default: "maxSide" gives its own)
    public const int MaxCells = 128; // cells one machine takes in (a Core's default: "maxCells" gives its own)
    public const int GridHalf = 32; // the furthest a cell may lie from its Core's, either way, and the longest side any Core may ask for
    public const int MaxCellsAny = 512; // the most cells any Core may ask for (the flood's scratch)
    public const int GridSide = 2 * GridHalf + 1; // the column grid's side: the Core's cell in the middle, GridHalf either way
    private const int Mid = GridHalf;
    public const float DefaultCell = 2f; // m: the lattice step (the 2 m grid)
    public const int MaxTier = 5; // the tiers of drawing 21: 1 a bare Core, 2 to 5 by its tier pieces
    public const int MaxCarriages = 32; // an Assembler's Gantry Carriages (Sean 2026-11-21: one a bed block, at most 32 a machine)
    public const int MaxStack = 4; // Overdrive Units or Feed Buffers that count on one Assembler

    /// <summary>The families named by part files, in load order: an index is stable for one load (never saved or sent).</summary>
    public static readonly List<string> Families = [];

    public static byte FamilyOf(string name)
    {
        var at = Families.FindIndex(f => f.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (at >= 0)
            return (byte)at;
        if (Families.Count >= 255)
            throw new ConstructPartException($"more than 254 machine block families (\"{name}\")");
        Families.Add(name.ToLowerInvariant());
        return (byte)(Families.Count - 1);
    }

    /// <summary>The <c>"machineBlock"</c> key of a part file: <c>{"family": "press", "role": "core"|"block"|"shared", "cell": 2}</c>.</summary>
    public static void Parse(ConstructPartDef d, JsonElement root, string file)
    {
        d.SeamSame = ConstructParts.Opt(root, "seamSame") is { ValueKind: JsonValueKind.True };
        d.IsTurntable = ConstructParts.Opt(root, "turntable") is { ValueKind: JsonValueKind.True };
        if (ConstructParts.Opt(root, "machineBlock") is not { } mb)
            return;
        ConstructParts.Known(mb, ["family", "role", "cell", "plane", "origin", "seed", "coreSeams", "tier", "module", "maxSide", "maxCells", "tool"], $"{file}: \"machineBlock\"");
        var role = ConstructParts.Str(ConstructParts.Need(mb, "role", $"{file}: \"machineBlock\""), file, "machineBlock.role");
        d.BlockRole = role switch
        {
            "core" => BlockRole.Core,
            "block" => BlockRole.Block,
            "shared" => BlockRole.Shared,
            "end" => BlockRole.End,
            "tier" => BlockRole.Tier,
            "carriage" => BlockRole.Carriage,
            "tool" => BlockRole.Tool,
            _ => throw new ConstructPartException($"{file}: \"machineBlock.role\" is core, block, shared, end, tier, carriage or tool, not \"{role}\""),
        };
        if (d.BlockRole == BlockRole.Tool)
        {
            // An Assembler's tool block: docked on a side socket of its Core (the Tool Bay), it lets the Core build the recipes that name its tool.
            var tool = ConstructParts.Opt(mb, "tool") is { } tv0 ? ConstructParts.Str(tv0, file, "machineBlock.tool") : "";
            var k = Array.IndexOf(AssemblingTable.Machines, tool);
            if (k < 1)
                throw new ConstructPartException($"{file}: a tool block names its \"machineBlock.tool\": {string.Join(" or ", AssemblingTable.Machines.Skip(1))}");
            d.BlockTool = (byte)k;
            d.BlockFamily = 255;
            d.BlockCell = DefaultCell;
            return;
        }
        if (ConstructParts.Opt(mb, "tool") is not null)
            throw new ConstructPartException($"{file}: only a tool block (role tool) says \"machineBlock.tool\"");
        if (d.BlockRole == BlockRole.Carriage)
        {
            // An Assembler's Gantry Carriage: counted on the bed blocks it is welded to (Carriages), never a cell of the lattice.
            d.BlockFamily = 255;
            d.BlockCell = DefaultCell;
            return;
        }
        if (d.BlockRole == BlockRole.Tier && ConstructParts.Opt(mb, "module") is { } mod)
        {
            // An Assembler's upgrade module that is no tier (the Overdrive Unit, the Feed Buffer): on a Core's tier slot, it stacks.
            d.BlockModule = ConstructParts.Str(mod, file, "machineBlock.module") switch
            {
                "overdrive" => 1,
                "buffer" => 2,
                var v => throw new ConstructPartException($"{file}: \"machineBlock.module\" is overdrive or buffer, not \"{v}\""),
            };
            if (ConstructParts.Opt(mb, "tier") is not null)
                throw new ConstructPartException($"{file}: a module says \"machineBlock.module\" or \"machineBlock.tier\", not both");
            if (!d.Sockets.Any(s => s.Kind == SocketKind.Tier))
                throw new ConstructPartException($"{file}: a module has a socket of kind \"tier\" (its mount on a Core's upgrade slot)");
            d.BlockFamily = 255;
            d.BlockCell = DefaultCell;
            return;
        }
        if (d.BlockRole == BlockRole.Tier)
        {
            // A tier piece (drawing 21) goes with every family's Core, on its "tier" socket; its own mount is a "tier" socket too.
            var t = ConstructParts.Opt(mb, "tier") is { } tv ? ConstructParts.Num(tv, file, "machineBlock.tier") : 0;
            if (t is < 2 or > MaxTier || t != MathF.Round(t))
                throw new ConstructPartException($"{file}: a tier piece says its \"machineBlock.tier\", 2 to {MaxTier}");
            if (!d.Sockets.Any(s => s.Kind == SocketKind.Tier))
                throw new ConstructPartException($"{file}: a tier piece has a socket of kind \"tier\" (its mount on a Core's tier slot)");
            d.BlockTier = (byte)t;
            d.BlockFamily = 255;
            d.BlockCell = DefaultCell;
            return;
        }
        if (ConstructParts.Opt(mb, "tier") is not null)
            throw new ConstructPartException($"{file}: only a tier piece (role tier) says \"machineBlock.tier\"");
        if (ConstructParts.Opt(mb, "family") is { } fam)
            d.BlockFamily = FamilyOf(ConstructParts.Str(fam, file, "machineBlock.family"));
        else if (d.BlockRole is not (BlockRole.Shared or BlockRole.End))
            throw new ConstructPartException($"{file}: \"machineBlock\" names its \"family\" (only a shared or an end cell goes with every family)");
        else
            d.BlockFamily = 255;
        d.BlockCell = ConstructParts.Opt(mb, "cell") is { } cell ? ConstructParts.Num(cell, file, "machineBlock.cell") : DefaultCell;
        if (d.BlockCell is < 0.5f or > 8f)
            throw new ConstructPartException($"{file}: \"machineBlock.cell\" is 0.5 to 8 m, not {d.BlockCell}");
        if (ConstructParts.Opt(mb, "seed") is { ValueKind: JsonValueKind.True })
        {
            // A family with no Core (Pad Tiles, drawing 23): each weld of its blocks is one assembly seeded from its lowest-index block.
            if (d.BlockRole != BlockRole.Block)
                throw new ConstructPartException($"{file}: only a block is a \"machineBlock.seed\" (a family with no Core)");
            d.BlockSeed = true;
            d.IsRollerTile = Families[d.BlockFamily].Equals("roller", StringComparison.OrdinalIgnoreCase);
        }
        if (ConstructParts.Opt(mb, "origin") is { } origin)
        {
            if (d.BlockRole != BlockRole.Core && !d.BlockSeed)
                throw new ConstructPartException($"{file}: only a Core or a seed block gives \"machineBlock.origin\" (its own cell's middle)");
            d.BlockOrigin = ConstructParts.Vec(origin, file, "machineBlock.origin");
        }
        if (ConstructParts.Opt(mb, "maxSide") is { } ms)
        {
            var v = ConstructParts.Num(ms, file, "machineBlock.maxSide");
            if (d.BlockRole != BlockRole.Core || v != MathF.Round(v) || v < 1 || v > GridHalf)
                throw new ConstructPartException($"{file}: only a Core says \"machineBlock.maxSide\", a whole number of cells, 1 to {GridHalf}");
            d.BlockMaxSide = (byte)v;
        }
        if (ConstructParts.Opt(mb, "maxCells") is { } mc)
        {
            var v = ConstructParts.Num(mc, file, "machineBlock.maxCells");
            if (d.BlockRole != BlockRole.Core || v != MathF.Round(v) || v < 1 || v > MaxCellsAny)
                throw new ConstructPartException($"{file}: only a Core says \"machineBlock.maxCells\", a whole number of cells, 1 to {MaxCellsAny}");
            d.BlockMaxCells = (short)v;
        }
        if (ConstructParts.Opt(mb, "coreSeams") is { } cs)
        {
            if (d.BlockRole != BlockRole.Core || cs.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new ConstructPartException($"{file}: only a Core says \"machineBlock.coreSeams\" (true or false)");
            d.BlockCoreSeams = cs.ValueKind == JsonValueKind.True;
        }
        if (ConstructParts.Opt(mb, "plane") is { } plane)
            d.BlockFace = ConstructParts.Str(plane, file, "machineBlock.plane") switch
            {
                "bed" => false,
                "face" => true,
                var v => throw new ConstructPartException($"{file}: \"machineBlock.plane\" is bed or face, not \"{v}\""),
            };
        if (d.BlockRole is BlockRole.Block or BlockRole.Shared or BlockRole.End)
        {
            // A cell is one lattice step across and along (a shared cell may be any whole number of steps: an Out Table's width).
            var size = d.BoundsMax - d.BoundsMin;
            foreach (var (a, label) in d.BlockFace ? new[] { (size.X, "x"), (size.Y, "y") } : new[] { (size.X, "x"), (size.Z, "z") })
            {
                var steps = a / d.BlockCell;
                if (MathF.Abs(steps - MathF.Round(steps)) > 0.02f || MathF.Round(steps) < 1 || d.BlockRole == BlockRole.Block && MathF.Round(steps) != 1)
                    throw new ConstructPartException($"{file}: a machine {(d.BlockRole == BlockRole.Block ? "block is one" : "shared cell is a whole number of")} {d.BlockCell:0.##} m cell{(d.BlockRole == BlockRole.Block ? "" : "s")} along {label}, not {a:0.###} m");
            }
        }
    }

    private static int Pack(int x, int y, int z) => (x + 128) | (y + 128) << 8 | (z + 128) << 16;

    /// <summary>Part <paramref name="part"/>'s cell in its Core's cell frame (x across, y up, z along the Core's +z; the Core's own is 0, 0, 0).</summary>
    public static bool CellOf(Construct c, int part, out int x, out int y, out int z)
    {
        x = y = z = 0;
        if (AsmOf(c, part) < 0)
            return false;
        var v = c.BlockCellOf[part];
        (x, y, z) = ((v & 0xFF) - 128, (v >> 8 & 0xFF) - 128, (v >> 16 & 0xFF) - 128);
        return true;
    }

    /// <summary>The middle of the Core's own cell (body frame): cell (x, y, z) of its machine is this plus its rotation of (x, y, z) cells.</summary>
    public static Vector3 CoreMid(Construct c, int core)
    {
        ref readonly var p = ref c.Parts[core];
        return p.D.BlockOrigin is { } org ? p.Pos + Rot24.Apply(p.Rot, org) : Middle(c, core);
    }

    /// <summary>Does this part's def take part in a block machine?</summary>
    public static bool Is(ConstructPartDef d) => d.BlockRole != BlockRole.None;

    /// <summary>Part <paramref name="part"/>'s assembly, or -1.</summary>
    public static int AsmOf(Construct c, int part) => (uint)part < (uint)c.BlockAsmOf.Length && c.MachineAsmCount > 0 ? c.BlockAsmOf[part] : -1;

    /// <summary>The machine of the Core (or any cell) <paramref name="part"/>, when it has one.</summary>
    public static bool TryOf(Construct c, int part, out CMachineAsm asm)
    {
        var a = AsmOf(c, part);
        asm = a >= 0 ? c.MachineAsms[a] : default;
        return a >= 0;
    }

    /// <summary>The bed's size in metres (x across, z along; zero with no rectangle).</summary>
    public static Vector2 BedSize(Construct c, in CMachineAsm asm)
    {
        var cell = c.Parts[asm.Core].D.BlockCell;
        return new Vector2(asm.W * cell, asm.L * cell);
    }

    /// <summary>
    /// The cells stacked on bed cell (<paramref name="x"/>, <paramref name="z"/>) of the Core's cell frame, -1 when there is no bed cell there.
    /// A machine reads its walls or masts from this (the Press's daylight, an oven's or a pad's masts).
    /// </summary>
    public static int ColumnAt(Construct c, in CMachineAsm asm, int x, int z)
    {
        if (x < -Mid || x > Mid || z < -Mid || z > Mid)
            return -1;
        var v = c.BlockColumns[asm.Grid + (x + Mid) * GridSide + z + Mid];
        return (v & 0x80) == 0 ? -1 : v & 0x7F;
    }

    /// <summary>The cells stacked on the Core's own cell (a Grow Former's masts, drawing 22): 0 with none.</summary>
    public static int CoreColumn(Construct c, in CMachineAsm asm) => c.BlockColumns[asm.Grid + Mid * GridSide + Mid] & 0x7F;

    /// <summary>The lowest column among the bed rectangle's four corners (masts): 0 when any has none.</summary>
    public static int CornerColumns(Construct c, in CMachineAsm asm)
    {
        if (asm.W == 0)
            return 0;
        int x1 = asm.X0 + asm.W - 1, z1 = asm.Z0 + asm.L - 1;
        return Math.Max(0, Math.Min(Math.Min(ColumnAt(c, asm, asm.X0, asm.Z0), ColumnAt(c, asm, x1, asm.Z0)), Math.Min(ColumnAt(c, asm, asm.X0, z1), ColumnAt(c, asm, x1, z1))));
    }

    /// <summary>
    /// What a link from part <paramref name="part"/> counts as leaving: an end cell's (an Out Table's) Core, so a conveyor off a machine's
    /// Out Table is that machine's Piece out for the logistics and the orders; any other part itself.
    /// </summary>
    public static int Owner(Construct c, int part) =>
        (uint)part < (uint)c.PartCount && c.Parts[part].D.BlockRole == BlockRole.End && AsmOf(c, part) is var a and >= 0 ? c.MachineAsms[a].Core : part;

    /// <summary>
    /// The machine's Out Table (an "end" cell just past its bed's far edge, the side away from the Core), or -1: of several side by side,
    /// the first with a conveyor on its Piece out, else the first. <see cref="CMachineAsm.OutCells"/> says how much of the edge they cover.
    /// </summary>
    public static int OutTable(Construct c, in CMachineAsm asm)
    {
        if (asm.Out == ushort.MaxValue || asm.Out >= c.PartCount)
            return -1;
        if (asm.OutCells > 1)
        {
            var id = AsmOf(c, asm.Core);
            if (ConstructConveyor.Out(c, asm.Out) < 0)
                for (var k = 0; k < c.RouteCount; k++) // (the links, not the parts: bounded by what is laid, as Out is)
                {
                    ref readonly var r = ref c.Routes[k];
                    if (r.Type == RouteType.Conveyor && r.Parent == 255 && r.From < c.PartCount && r.From < c.BlockAsmOf.Length && c.BlockAsmOf[r.From] == id && c.Parts[r.From].D.BlockRole == BlockRole.End)
                        return r.From;
                }
        }
        return asm.Out;
    }

    /// <summary>The machine's far edge: the bed rectangle's side away from the Core, and the end cells (Out Tables) just past it.</summary>
    private static void FindOut(Construct c, ref CMachineAsm asm, ReadOnlySpan<ushort> cells, ReadOnlySpan<sbyte> cx, ReadOnlySpan<sbyte> cz, ReadOnlySpan<byte> steps)
    {
        asm.Out = ushort.MaxValue;
        asm.OutCells = 0;
        asm.OutSide = 4;
        if (asm.W == 0)
            return;
        int x0 = asm.X0, z0 = asm.Z0, x1 = asm.X0 + asm.W - 1, z1 = asm.Z0 + asm.L - 1;
        // The Core is cell (0, 0), beside the rectangle on one side: the far edge is the other.
        byte side = z0 > 0 ? (byte)4 : z1 < 0 ? (byte)5 : x0 > 0 ? (byte)0 : (byte)1;
        asm.OutSide = side;
        int lineAt = side switch { 4 => z1 + 1, 5 => z0 - 1, 0 => x1 + 1, _ => x0 - 1 }; // (the row or column of cells just past the edge)
        bool alongX = side is 4 or 5; // (the edge runs along x)
        var first = int.MaxValue;
        var covered = 0;
        for (var k = 1; k < cells.Length; k++)
        {
            if (c.Parts[cells[k]].D.BlockRole != BlockRole.End)
                continue;
            int across = steps[2 * k], along = steps[2 * k + 1];
            int a0 = alongX ? cx[k] : cz[k], n = alongX ? across : along; // (its cells along the edge)
            int b0 = alongX ? cz[k] : cx[k], depth = alongX ? along : across; // (and out from it)
            if (lineAt < b0 || lineAt >= b0 + depth)
                continue;
            int lo = alongX ? x0 : z0, hi = alongX ? x1 : z1;
            var on = Math.Min(hi, a0 + n - 1) - Math.Max(lo, a0) + 1;
            if (on <= 0)
                continue;
            covered += on;
            if (a0 < first)
                (first, asm.Out) = (a0, cells[k]);
        }
        asm.OutCells = (byte)Math.Min(covered, alongX ? asm.W : asm.L);
    }

    /// <summary>What the screen and the look line say of a machine's size: "bed 3 x 2 (6.0 x 4.0 m), 2 idle".</summary>
    public static string Describe(Construct c, in CMachineAsm asm)
    {
        if (asm.W == 0)
            return asm.Cells == 0 ? "no blocks: weld blocks to it" : $"no full bed: {asm.Cells} blocks, none in a rectangle";
        var m = BedSize(c, asm);
        var s = $"bed {asm.W} x {asm.L} ({m.X:0.0} x {m.Y:0.0} m)";
        if (asm.Idle > 0)
            s += $", {asm.Idle} idle";
        if (asm.Capped)
            s += $", over {c.Parts[asm.Core].D.BlockMaxCells} blocks";
        return s;
    }

    /// <summary>The tiers' names (drawing 21, as the machine lines' tiers: docs "Machine tiers") and the piece that brings each, by tier.</summary>
    public static readonly string[] TierNames = ["", "Scrap", "Workshop", "Industrial", "Military", "Plasma"];
    public static readonly string[] TierPieceNames = ["", "", "Power Coupling", "Coolant Loop", "Servo Rack", "Plasma Conditioner"];

    /// <summary>A machine's tier for its look line: "tier 3 Industrial", and the piece a gap waits for ("a Servo Rack waits for a Coolant Loop").</summary>
    public static string TierText(in CMachineAsm asm)
    {
        var t = Math.Clamp((int)asm.Tier, 1, MaxTier);
        var s = $"tier {t} {TierNames[t]}";
        if (asm.TierPieces >> (t + 1) != 0 && t < MaxTier)
        {
            var above = t + 2;
            while (above <= MaxTier && (asm.TierPieces & 1 << above) == 0)
                above++;
            if (above <= MaxTier)
                s += $" (its {TierPieceNames[above]} waits for a {TierPieceNames[t + 1]})";
        }
        else if (t < MaxTier)
            s += $" (bolt a {TierPieceNames[t + 1]} on for tier {t + 1})";
        return s;
    }

    // ---- The rebuild ---------------------------------------------------------------------------------------------------------

    /// <summary>Find every Core's machine (with the caches, after the welds are known). Nothing to do on a construct with no Core.</summary>
    public static void Build(Construct c)
    {
        var wasAny = c.MachineAsmCount > 0;
        c.MachineAsmCount = 0;
        var cores = 0;
        foreach (var i in c.Specials)
            if (c.Parts[i].D.BlockRole == BlockRole.Core || c.Parts[i].D.BlockSeed)
                cores++; // (a seed block may start an assembly too: at most one each)
        if (cores == 0)
        {
            if (wasAny || c.BlockJoined.Length > 0)
            {
                Array.Fill(c.BlockAsmOf, (short)-1);
                Array.Clear(c.BlockJoined); // (a Rack Side's last faces too: SeamSame sets them again)
            }
            ConstructPads.Build(c);
            RollerRuns.Note(c);
            SeamSame(c);
            return;
        }
        var n = c.PartCount;
        if (c.BlockAsmOf.Length < n)
        {
            c.BlockAsmOf = new short[Math.Max(n, c.BlockAsmOf.Length * 2)];
            c.BlockJoined = new byte[c.BlockAsmOf.Length];
            c.BlockCellOf = new int[c.BlockAsmOf.Length];
        }
        Array.Fill(c.BlockAsmOf, (short)-1);
        Array.Clear(c.BlockJoined);
        if (c.MachineAsms.Length < cores)
            Array.Resize(ref c.MachineAsms, Math.Max(cores, c.MachineAsms.Length * 2));
        var cells = GridSide * GridSide;
        if (c.BlockColumns.Length < cores * cells)
            Array.Resize(ref c.BlockColumns, Math.Max(cores * cells, c.BlockColumns.Length * 2));
        using var scratch = Scratch.Begin();
        var queue = scratch.Take<ushort>(MaxCellsAny + 1);
        var cx = scratch.Take<sbyte>(MaxCellsAny + 1);
        var cy = scratch.Take<sbyte>(MaxCellsAny + 1);
        var cz = scratch.Take<sbyte>(MaxCellsAny + 1);
        var steps = scratch.Take<byte>(2 * (MaxCellsAny + 1)); // (each cell's steps across and along: a shared cell may be several)
        var sum = scratch.Take<short>((GridSide + 1) * (GridSide + 1));
        foreach (var i in c.Specials)
            if (c.Parts[i].D.BlockRole == BlockRole.Core && c.BlockAsmOf[i] < 0)
                Assemble(c, i, queue, cx, cy, cz, steps, sum);
        // Seeded families after the Cores (a Mast Block a Core took stays the Core's), each from its lowest-index block: the specials
        // are in index order, so the first block of a weld met is its seed, and the rest join its flood.
        foreach (var i in c.Specials)
            if (c.Parts[i].D.BlockSeed && c.BlockAsmOf[i] < 0 && !c.Parts[i].Broken)
                Assemble(c, i, queue, cx, cy, cz, steps, sum);
        ConstructPads.Build(c);
        RollerRuns.Note(c);
        SeamSame(c);
    }

    /// <summary>Parts that seam with their own kind (<c>"seamSame"</c>: Rack Sides in a row show one end post): their faces welded to a part of the same def.</summary>
    private static void SeamSame(Construct c)
    {
        var n = c.PartCount;
        for (var i = 0; i < n; i++)
        {
            var d = c.Parts[i].D;
            if (!d.SeamSame || i < c.BlockAsmOf.Length && c.BlockAsmOf[i] >= 0)
                continue; // (a cell a machine took keeps its machine's faces: a Mast Block in an oven's corner)
            if (c.BlockAsmOf.Length < n)
            {
                var old = c.BlockAsmOf.Length;
                Array.Resize(ref c.BlockAsmOf, Math.Max(n, old * 2));
                Array.Resize(ref c.BlockJoined, c.BlockAsmOf.Length);
                Array.Fill(c.BlockAsmOf, (short)-1, old, c.BlockAsmOf.Length - old);
            }
            byte mask = 0;
            for (var e = c.FirstLink(i); e >= 0; e = c.NextLink(e))
            {
                var o = c.OtherEnd(e);
                if (c.Parts[o].Def != c.Parts[i].Def)
                    continue;
                ref readonly var l = ref c.Links[e >> 1];
                var s = (e & 1) == 0 ? l.SA : l.SB;
                if (s < d.Sockets.Length)
                    mask |= (byte)(1 << d.Sockets[s].Dir);
            }
            c.BlockJoined[i] = mask;
        }
    }

    private static void Assemble(Construct c, int core, Span<ushort> queue, Span<sbyte> cx, Span<sbyte> cy, Span<sbyte> cz, Span<byte> steps, Span<short> sum)
    {
        var id = (short)c.MachineAsmCount++;
        ref var asm = ref c.MachineAsms[id];
        var cd = c.Parts[core].D;
        asm = new CMachineAsm { Core = (ushort)core, Family = cd.BlockFamily, Grid = id * GridSide * GridSide, Blocker = -1, ToolPart = ushort.MaxValue };
        var grid = c.BlockColumns.AsSpan(asm.Grid, GridSide * GridSide);
        grid.Clear();
        var cell = cd.BlockCell;
        var coreRot = c.Parts[core].Rot;
        var assembler = Families[asm.Family] == "assembler"; // (its masts stand outside the bed, on its rail sides: Sean 2026-11-21)
        var seeded = cd.BlockSeed; // (no Core: the seed is a bed cell itself, and its blocks lie the same way round as it, so one hold spans them: ConstructPads)
        var unrot = Rot24Inverse(coreRot);
        var coreMid = CoreMid(c, core); // (a Core whose looks stand out of its cell names the cell's middle)
        c.BlockAsmOf[core] = id;
        c.BlockCellOf[core] = Pack(0, 0, 0);
        int count = 0, at = 0;
        queue[count++] = (ushort)core;
        cx[0] = cy[0] = cz[0] = 0;
        var cellCount = 0;
        int maxCells = cd.BlockMaxCells, maxSide = cd.BlockMaxSide; // (the Core's own limits: a seed block's are the defaults)
        while (at < count)
        {
            var i = queue[at++];
            if (c.Parts[i].D.BlockRole == BlockRole.End)
                continue; // (an Out Table closes the bed: what is welded past it is another machine's, a cutter's bed)
            for (var e = c.FirstLink(i); e >= 0; e = c.NextLink(e))
            {
                var o = c.OtherEnd(e);
                var d = c.Parts[o].D;
                if (d.BlockRole is not (BlockRole.Block or BlockRole.Shared or BlockRole.End) || d.BlockRole == BlockRole.Block && d.BlockFamily != asm.Family || seeded && d.BlockRole == BlockRole.Block && c.Parts[o].Rot != coreRot || MathF.Abs(d.BlockCell - cell) > 0.01f || d.BlockFace != cd.BlockFace || c.Parts[o].Broken)
                    continue;
                if (c.BlockAsmOf[o] >= 0)
                    continue; // (in this machine already, or another Core's; a broken cell idles until it is mended)
                if (d.BlockRole == BlockRole.End)
                {
                    // An Out Table belongs to the machine on its in side (its -z face, where its rollers come from), whatever was built
                    // first: in a line of former, table and cutter, the cutter's bed on the table's far side doesn't take it.
                    ref readonly var lk = ref c.Links[e >> 1];
                    var os = (e & 1) == 0 ? lk.SB : lk.SA; // (the table's own socket of the weld)
                    if (os >= d.Sockets.Length || d.Sockets[os].Dir != 5)
                        continue;
                }
                if (cellCount >= maxCells)
                {
                    asm.Capped = true;
                    Caps.Hit("cells in one block machine", maxCells, cellCount + 1);
                    continue;
                }
                // A block turned off the Core's lattice (its cell's square not in the Core's grid plane) isn't part of the machine.
                var span = Vector3.Abs(Rot24.Apply(unrot, Rot24.Apply(c.Parts[o].Rot, d.BoundsMax - d.BoundsMin))) / cell;
                var spanAlong = cd.BlockFace ? span.Y : span.Z;
                if (d.BlockRole == BlockRole.Block && (MathF.Abs(span.X - 1) > 0.02f || MathF.Abs(spanAlong - 1) > 0.02f))
                    continue;
                // Its place on the Core's lattice: the cell of its lowest corner's first step (a shared cell several steps wide marks
                // its first step only; the Core's own middle is cell 0, so a cell's lowest corner sits half a step under its cell's middle).
                var rel = Rot24.Apply(unrot, Corner(c, o, unrot) - coreMid) / cell;
                int x = (int)MathF.Floor(rel.X + 0.5f + 0.01f), y, z;
                if (d.BlockRole == BlockRole.End)
                {
                    y = 0; // (an end cell is a low table at the bed's work height: on the bed's layer whatever its height)
                    z = (int)MathF.Floor((cd.BlockFace ? rel.Y : rel.Z) + 0.5f + 0.01f);
                }
                else if (cd.BlockFace)
                {
                    // A face machine's cells stand in the Core's x-y face (an Extruder's Die Blocks beside its Core): the grid's "along" is up
                    // the face, and a cell's layer is how far its front stands out from the Core's (flush is the die's layer, 0).
                    z = (int)MathF.Floor(rel.Y + 0.5f + 0.01f);
                    y = (int)MathF.Round((Front(c, o, unrot) - Front(c, core, unrot)) / cell);
                }
                else
                {
                    y = (int)MathF.Round(Rot24.Apply(unrot, Middle(c, o) - coreMid).Y / cell);
                    z = (int)MathF.Floor(rel.Z + 0.5f + 0.01f);
                }
                if (Math.Abs(x) > maxSide || Math.Abs(z) > maxSide || Math.Abs(y) > 127)
                    continue; // (beyond the Core's reach, its "maxSide" either way: not part of this machine)
                if (y > 0 && d.BlockRole == BlockRole.Block && cd.IsPanelFormer && !cd.IsLaserCutter)
                    continue;
                if (y > 0 && d.BlockRole == BlockRole.Block && assembler)
                    continue; // (a bed block stacked on an Assembler's bed isn't part of it: its masts stand outside the bed, Masts) // (a Bed Block stacked on a Panel Former's bed isn't a mast: only shared cells deepen its stroke, ConstructPanelFormer.PinDepth)
                c.BlockAsmOf[o] = id;
                c.BlockCellOf[o] = Pack(x, y, z);
                cellCount++;
                cx[count] = (sbyte)x;
                cy[count] = (sbyte)y;
                cz[count] = (sbyte)z;
                steps[2 * count] = (byte)Math.Clamp((int)MathF.Round(span.X), 1, maxSide);
                steps[2 * count + 1] = (byte)Math.Clamp((int)MathF.Round(spanAlong), 1, maxSide);
                queue[count++] = (ushort)o;
            }
        }
        asm.Cells = (short)cellCount;
        Tiers(c, core, id, ref asm);
        // Columns: the bed layer is the Core's own (y 0); a cell above a bed cell adds to its column. A seed is a bed cell itself.
        if (seeded)
            grid[Mid * GridSide + Mid] |= 0x80;
        for (var k = 1; k < count; k++)
            if (cy[k] == 0 && c.Parts[queue[k]].D.BlockRole != BlockRole.End && !(assembler && c.Parts[queue[k]].D.BlockRole == BlockRole.Shared))
                for (var sx = 0; sx < steps[2 * k]; sx++)
                    for (var sz = 0; sz < steps[2 * k + 1]; sz++)
                        if (cx[k] + sx <= maxSide && cz[k] + sz <= maxSide) // (clipped at the Core's own reach, as before the grid grew)
                            grid[(cx[k] + sx + Mid) * GridSide + cz[k] + sz + Mid] |= 0x80;
        for (var k = 1; k < count; k++)
            if (assembler && c.Parts[queue[k]].D.BlockRole == BlockRole.Shared && cy[k] >= 0 && Math.Abs(cx[k]) <= maxSide && Math.Abs(cz[k]) <= maxSide)
            {
                // An Assembler's mast: a column of shared cells standing on the floor beside its bed (bit 7 clear), counted from the bed's layer.
                ref var g = ref grid[(cx[k] + Mid) * GridSide + cz[k] + Mid];
                if ((g & 0x80) == 0)
                    g = (byte)Math.Min(0x7F, Math.Max(g & 0x7F, cy[k] + 1));
            }
            else if (cy[k] > 0 && c.Parts[queue[k]].D.BlockRole != BlockRole.End)
            {
                ref var g = ref grid[(cx[k] + Mid) * GridSide + cz[k] + Mid];
                g = (byte)((g & 0x80) | Math.Min(0x7F, Math.Max(g & 0x7F, cy[k])));
            }
        LargestRectangle(grid, sum, ref asm, seeded, maxSide, maxCells);
        FindOut(c, ref asm, queue[..count], cx, cz, steps);
        if (assembler)
            AssemblerCounts(c, id, ref asm, queue[..count], cx, cz, grid);
        if (asm.W > 0 && Families[asm.Family] == "press")
            ConstructPress.SwingCheck(c, ref asm, id); // (its shells' swing: a fixed part in the way stops it, and the screen names it)
        // Bed cells outside the rectangle idle; each cell's welded faces to its own machine (for the seam boxes).
        var bed = 0;
        foreach (var g in grid)
            if ((g & 0x80) != 0)
                bed++;
        asm.Idle = (short)(bed - asm.W * asm.L);
        for (var k = 0; k < count; k++)
        {
            var i = queue[k];
            byte mask = InMachine;
            for (var e = c.FirstLink(i); e >= 0; e = c.NextLink(e))
            {
                var o = c.OtherEnd(e);
                if (c.BlockAsmOf[o] != id || !cd.BlockCoreSeams && c.Parts[i].D.BlockRole == BlockRole.Block && (o == core || c.Parts[o].D.BlockRole == BlockRole.End))
                    continue; // (a Core that says "coreSeams": false: only a weld to another bed cell hides a cell's seam, so a Laser Cutter's rail runs on past its Core and its Out Table)
                ref readonly var l = ref c.Links[e >> 1];
                var s = (e & 1) == 0 ? l.SA : l.SB; // (this part's own socket of the weld)
                var socks = c.Parts[i].D.Sockets;
                if (s < socks.Length)
                    mask |= (byte)(1 << socks[s].Dir);
            }
            c.BlockJoined[i] = mask;
        }
    }

    /// <summary>
    /// The tier pieces bolted on the Core's own tier slots (drawing 21): each whole one joins the Core's assembly (so it shows its Core's
    /// work), and the machine's tier is the highest n with a piece of every tier from 2 to n there. A piece with a gap below it does nothing yet.
    /// </summary>
    private static void Tiers(Construct c, int core, short id, ref CMachineAsm asm)
    {
        asm.Tier = 1;
        if (c.Parts[core].D.BlockSeed)
            return;
        for (var e = c.FirstLink(core); e >= 0; e = c.NextLink(e))
        {
            var o = c.OtherEnd(e);
            var d = c.Parts[o].D;
            if (d.BlockRole == BlockRole.Tool)
            {
                ToolBay(c, core, id, ref asm, e, o);
                continue;
            }
            if (d.BlockRole != BlockRole.Tier || c.Parts[o].Broken || c.BlockAsmOf[o] >= 0)
                continue;
            if (d.BlockModule != 0 && Families[asm.Family] != "assembler")
                continue; // (an Overdrive Unit or Feed Buffer works only an Assembler: on another Core it does nothing: its description says so)
            ref readonly var l = ref c.Links[e >> 1];
            var cs = (e & 1) == 0 ? l.SA : l.SB; // (the Core's own socket of the weld: only its tier slots count)
            var socks = c.Parts[core].D.Sockets;
            if (cs >= socks.Length || socks[cs].Kind != SocketKind.Tier)
                continue;
            c.BlockAsmOf[o] = id;
            c.BlockCellOf[o] = Pack(0, 0, 0);
            if (d.BlockModule == 1)
                asm.Overdrive = (byte)Math.Min(MaxStack, asm.Overdrive + 1);
            else if (d.BlockModule == 2)
                asm.Buffers = (byte)Math.Min(MaxStack, asm.Buffers + 1);
            else
                asm.TierPieces |= (byte)(1 << d.BlockTier);
        }
        while (asm.Tier < MaxTier && (asm.TierPieces & 1 << (asm.Tier + 1)) != 0)
            asm.Tier++;
    }

    /// <summary>The Core's side sockets that are its Tool Bay: a tool block docks on whichever of them the bed doesn't use.</summary>
    public static bool IsToolBay(ConstructPartDef core, int socket) =>
        (uint)socket < (uint)core.Sockets.Length && core.Sockets[socket].Kind == SocketKind.Struct && core.Sockets[socket].Dir is 0 or 1 or 4 or 5;

    /// <summary>A tool block docked on one of an Assembler Core's side sockets (its Tool Bay): the first one met joins the assembly and names the Core's tool.</summary>
    private static void ToolBay(Construct c, int core, short id, ref CMachineAsm asm, int e, int o)
    {
        if (Families[asm.Family] != "assembler" || asm.Tool != 0 || c.Parts[o].Broken || c.BlockAsmOf[o] >= 0)
            return;
        ref readonly var l = ref c.Links[e >> 1];
        if (!IsToolBay(c.Parts[core].D, (e & 1) == 0 ? l.SA : l.SB))
            return;
        c.BlockAsmOf[o] = id;
        c.BlockCellOf[o] = Pack(0, 0, 0);
        asm.Tool = c.Parts[o].D.BlockTool;
        asm.ToolPart = (ushort)o;
    }

    /// <summary>
    /// An Assembler's Gantry Carriages (each welded on a block of its bed rectangle; they join its assembly, at most <see cref="MaxCarriages"/>)
    /// and its masts: at each of the bed's four corners the tallest column of shared cells just outside it (beside the corner cell, or on its
    /// diagonal), and the machine's the shortest of the four.
    /// </summary>
    private static void AssemblerCounts(Construct c, short id, ref CMachineAsm asm, ReadOnlySpan<ushort> cells, ReadOnlySpan<sbyte> cx, ReadOnlySpan<sbyte> cz, ReadOnlySpan<byte> grid)
    {
        asm.Carriages = 0;
        asm.Masts = 0;
        if (asm.W == 0)
            return;
        int x1 = asm.X0 + asm.W - 1, z1 = asm.Z0 + asm.L - 1;
        for (var k = 1; k < cells.Length; k++)
        {
            var i = cells[k];
            if (c.Parts[i].D.BlockRole != BlockRole.Block || cx[k] < asm.X0 || cx[k] > x1 || cz[k] < asm.Z0 || cz[k] > z1)
                continue;
            for (var e = c.FirstLink(i); e >= 0; e = c.NextLink(e))
            {
                var o = c.OtherEnd(e);
                if (c.Parts[o].D.BlockRole != BlockRole.Carriage || c.Parts[o].Broken || c.BlockAsmOf[o] >= 0)
                    continue;
                if (asm.Carriages >= MaxCarriages)
                {
                    Caps.Hit("Gantry Carriages on one Assembler", MaxCarriages, asm.Carriages + 1);
                    continue;
                }
                c.BlockAsmOf[o] = id;
                c.BlockCellOf[o] = Pack(cx[k], 0, cz[k]);
                asm.Carriages++;
            }
        }
        asm.Masts = (byte)Math.Min(Math.Min(MastBeside(grid, asm.X0, asm.Z0, -1, -1), MastBeside(grid, x1, asm.Z0, 1, -1)), Math.Min(MastBeside(grid, asm.X0, z1, -1, 1), MastBeside(grid, x1, z1, 1, 1)));
    }

    /// <summary>The tallest column standing outside the bed at corner cell (x, z), beside it along x or z or on its diagonal (<paramref name="dx"/>, <paramref name="dz"/> outwards).</summary>
    private static int MastBeside(ReadOnlySpan<byte> grid, int x, int z, int dx, int dz) =>
        Math.Max(Outside(grid, x + dx, z), Math.Max(Outside(grid, x, z + dz), Outside(grid, x + dx, z + dz)));

    private static int Outside(ReadOnlySpan<byte> grid, int x, int z)
    {
        if (x < -Mid || x > Mid || z < -Mid || z > Mid)
            return 0;
        var g = grid[(x + Mid) * GridSide + z + Mid];
        return (g & 0x80) != 0 ? 0 : g & 0x7F;
    }

    /// <summary>The tier of the machine part <paramref name="part"/> is in (1 with no tier pieces, or not a block machine).</summary>
    public static int TierOf(Construct c, int part) => TryOf(c, part, out var asm) && asm.Tier > 0 ? asm.Tier : 1;

    /// <summary>The largest full rectangle of bed cells beside the Core (holding a cell next to its own), at most <paramref name="maxSide"/> a side and <paramref name="maxCells"/> in all; of equals, the one nearest the Core.</summary>
    private static void LargestRectangle(ReadOnlySpan<byte> grid, Span<short> sum, ref CMachineAsm asm, bool seeded, int maxSide, int maxCells)
    {
        const int S = GridSide + 1;
        for (var x = 0; x <= GridSide; x++)
            for (var z = 0; z <= GridSide; z++)
                sum[x * S + z] = (short)(x == 0 || z == 0 ? 0 : sum[(x - 1) * S + z] + sum[x * S + z - 1] - sum[(x - 1) * S + z - 1] + ((grid[(x - 1) * GridSide + z - 1] & 0x80) != 0 ? 1 : 0));
        int best = 0, bestNear = int.MaxValue, bx = 0, bz = 0, bw = 0, bl = 0;
        for (var x0 = 0; x0 < GridSide; x0++)
            for (var z0 = 0; z0 < GridSide; z0++)
            {
                if ((grid[x0 * GridSide + z0] & 0x80) == 0)
                    continue;
                for (var w = 1; w <= maxSide && x0 + w <= GridSide; w++)
                {
                    if ((grid[(x0 + w - 1) * GridSide + z0] & 0x80) == 0)
                        break;
                    for (var l = 1; l <= maxSide && z0 + l <= GridSide && w * l <= maxCells; l++)
                    {
                        var area = w * l;
                        int x1 = x0 + w, z1 = z0 + l;
                        if (sum[x1 * S + z1] - sum[x0 * S + z1] - sum[x1 * S + z0] + sum[x0 * S + z0] != area)
                            break; // (a gap: a longer one has it too)
                        if (area < best || !(seeded ? Holds(x0, z0, w, l) : Touches(x0, z0, w, l)))
                            continue; // (the machine is the rectangle that holds a cell welded to its Core: a stray patch elsewhere idles)
                        var near = Math.Abs(x0 + w / 2 - Mid) + Math.Abs(z0 + l / 2 - Mid);
                        if (area > best || near < bestNear)
                            (best, bestNear, bx, bz, bw, bl) = (area, near, x0, z0, w, l);
                    }
                }
            }
        asm.X0 = (sbyte)(bx - Mid);
        asm.Z0 = (sbyte)(bz - Mid);
        asm.W = (byte)bw;
        asm.L = (byte)bl;
    }

    /// <summary>Does the rectangle of grid cells from (x0, z0), w by l, hold one of the four cells beside the Core's (the grid's middle)?</summary>
    private static bool Touches(int x0, int z0, int w, int l)
    {
        bool In(int x, int z) => x >= x0 && x < x0 + w && z >= z0 && z < z0 + l;
        return In(Mid + 1, Mid) || In(Mid - 1, Mid) || In(Mid, Mid + 1) || In(Mid, Mid - 1);
    }

    /// <summary>Does the rectangle of grid cells from (x0, z0), w by l, hold the seed's own cell (the grid's middle)?</summary>
    private static bool Holds(int x0, int z0, int w, int l) => Mid >= x0 && Mid < x0 + w && Mid >= z0 && Mid < z0 + l;

    /// <summary>The corner of part <paramref name="part"/>'s bounds lowest in x and z of the Core's frame (<paramref name="unrot"/> turns into it), in the body frame.</summary>
    private static Vector3 Corner(Construct c, int part, byte unrot)
    {
        ref readonly var p = ref c.Parts[part];
        var d = p.D;
        Vector3 a = Rot24.Apply(unrot, p.Pos + Rot24.Apply(p.Rot, d.BoundsMin)), b = Rot24.Apply(unrot, p.Pos + Rot24.Apply(p.Rot, d.BoundsMax));
        var lo = Vector3.Min(a, b);
        return Rot24.Apply(Rot24Inverse(unrot), lo);
    }

    /// <summary>How far part <paramref name="part"/>'s bounds reach along the Core's +z (<paramref name="unrot"/> turns into its frame): a face machine's front.</summary>
    private static float Front(Construct c, int part, byte unrot)
    {
        ref readonly var p = ref c.Parts[part];
        var d = p.D;
        Vector3 a = Rot24.Apply(unrot, p.Pos + Rot24.Apply(p.Rot, d.BoundsMin)), b = Rot24.Apply(unrot, p.Pos + Rot24.Apply(p.Rot, d.BoundsMax));
        return MathF.Max(a.Z, b.Z);
    }

    private static Vector3 Middle(Construct c, int part)
    {
        ref readonly var p = ref c.Parts[part];
        var d = p.D;
        return p.Pos + Rot24.Apply(p.Rot, (d.BoundsMin + d.BoundsMax) * 0.5f);
    }

    private static readonly byte[] _inverse = MakeInverses();

    private static byte[] MakeInverses()
    {
        var inv = new byte[Rot24.Count];
        for (byte r = 0; r < Rot24.Count; r++)
            for (byte q = 0; q < Rot24.Count; q++)
                if (Vector3.Distance(Rot24.Apply(q, Rot24.Apply(r, Vector3.UnitX)), Vector3.UnitX) < 1e-3f && Vector3.Distance(Rot24.Apply(q, Rot24.Apply(r, Vector3.UnitY)), Vector3.UnitY) < 1e-3f)
                {
                    inv[r] = q;
                    break;
                }
        return inv;
    }

    internal static byte Rot24Inverse(byte r) => _inverse[r % Rot24.Count];

    // ---- A dev check: --machine-blocks BLUEPRINT -------------------------------------------------------------------------------

    /// <summary>--machine-blocks NAME|FILE[,...]: build each blueprint and print its block machines (a dev check; nothing is written).</summary>
    public static int Cli(string[] args)
    {
        var at = Array.FindIndex(args, a => a.Equals("--machine-blocks", StringComparison.OrdinalIgnoreCase));
        var names = at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--") ? args[at + 1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];
        if (names.Length == 0)
        {
            Console.WriteLine("--machine-blocks NAME|FILE[,...]: a blueprint by name, or a blueprint file");
            return 1;
        }
        var bad = 0;
        foreach (var name in names)
        {
            var bp = File.Exists(name) ? ConstructBuild.ParseBlueprint(File.ReadAllText(name), name) : ConstructBuild.FindBlueprint(name);
            if (bp == null)
            {
                Console.WriteLine($"[blocks] {name}: no such blueprint");
                bad++;
                continue;
            }
            var world = new ConstructWorld();
            if (ConstructBuild.Build(world, bp, Vector3.Zero, Quaternion.Identity, out var why) is not { } c)
            {
                Console.WriteLine($"[blocks] {bp.Name}: doesn't build: {why}");
                bad++;
                continue;
            }
            Console.WriteLine($"[blocks] {bp.Name}: {c.PartCount} parts, {c.MachineAsmCount} block machines");
            Console.Write(Report(c));
            // Seam parts outside any machine (a shed's pillars of Mast Blocks): how many, and how many of their look boxes are drawn.
            int lone = 0, drawn = 0, all = 0;
            for (var i = 0; i < c.PartCount; i++)
            {
                var d = c.Parts[i].D;
                if (!d.HasSeams || i < c.BlockAsmOf.Length && c.BlockAsmOf[i] >= 0)
                    continue;
                lone++;
                var joined = i < c.BlockJoined.Length ? c.BlockJoined[i] : (byte)0;
                foreach (var b in d.Boxes)
                    if (!b.Hidden)
                    {
                        all++;
                        if (!b.SeamHidden(joined))
                            drawn++;
                    }
            }
            if (lone > 0)
                Console.WriteLine($"[blocks]   {lone} seam parts outside a machine: {drawn} of their {all} look boxes drawn");
            for (var m = 0; m < c.MachineAsmCount; m++)
                if (ConstructAssembling.FitOf(c, c.MachineAsms[m].Core) is { Limited: true } fit)
                    foreach (var part in (string[])["Bulkhead Light", "Engine", "Ship Battery Bank", "Leg Pod Housing XL"])
                        if (ConstructParts.Find(part) is { } pd)
                            Console.WriteLine($"[blocks]   {part}: {fit.Places(pd)} places, {fit.AtOnce(pd)} at once{(fit.AtOnce(pd) == 0 ? ": " + fit.TooBigWhy(pd) : "")}");
        }
        return bad;
    }

    /// <summary>Every block machine's assembly in a built construct, one line each (the check command and the look line's detail).</summary>
    public static string Report(Construct c)
    {
        var sb = new StringBuilder();
        for (var a = 0; a < c.MachineAsmCount; a++)
        {
            ref readonly var asm = ref c.MachineAsms[a];
            sb.Append($"  {c.Parts[asm.Core].D.Name} (part {asm.Core}, family {Families[asm.Family]}): {asm.Cells} cells, {Describe(c, asm)}");
            if (asm.W > 0)
                sb.Append($", corner columns {CornerColumns(c, asm)}");
            if (asm.W > 0 && c.Parts[asm.Core].D is { IsPanelFormer: true, IsLaserCutter: false })
                sb.Append($", pins rise {ConstructPanelFormer.PinDepth(c, asm):0} m");
            var faces = 0;
            for (var i = 0; i < c.PartCount && i < c.BlockAsmOf.Length; i++)
                if (c.BlockAsmOf[i] == a)
                    faces += System.Numerics.BitOperations.PopCount((uint)(c.BlockJoined[i] & 0x3F));
            sb.Append($", {faces} welded faces");
            if (c.Parts[asm.Core].D.IsBuildStation)
            {
                Span<ushort> pads = stackalloc ushort[ConstructStation.MaxOwnPads];
                sb.Append($", {ConstructStation.Arms(c, asm.Core)} fitting arms, {ConstructStation.OwnPads(c, asm.Core, pads)} pick pads under its crane");
            }
            if (asm.Out != ushort.MaxValue)
                sb.Append($", out table {asm.OutCells} of {(asm.OutSide is 4 or 5 ? asm.W : asm.L)} cells");
            if (Families[asm.Family] == "assembler" && ConstructAssembling.FitOf(c, asm.Core) is { Limited: true } fit)
                sb.Append($", {fit.Carriages} arms ({asm.Carriages} Gantry Carriages), masts {asm.Masts} ({fit.Clear:0} m clear), modules {(asm.TierPieces == 0 ? "none" : AsmFit.ModuleWords(asm.TierPieces))}, {asm.Overdrive} Overdrive, {asm.Buffers} Feed Buffer");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
