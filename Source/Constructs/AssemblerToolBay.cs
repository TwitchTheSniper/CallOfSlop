using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// An Assembler Core's Tool Bay (design doc "Assembler on the block system", "Families fold in, with swappable tool blocks"; Assembler
/// PR 3): a tool block (the Thruster Jig Block, the Sewing Table Block) docked on a side socket of the Core that its bed doesn't use lets
/// it build the recipes that name that tool (<see cref="AssembleRecipe.Tool"/>). <see cref="MachineBlocks"/> finds the docked block at the
/// rebuild (<see cref="CMachineAsm.Tool"/>). A player docks one with the attach tool. A chute from a store holding tool blocks (a Block
/// Rack) into the Core swaps them: when the head recipe needs a tool the Core hasn't docked, the chute takes that block out of the rack,
/// and at the start of the next step (a chute moves inside the routes' own loop, where parts can't come and go) the old block goes
/// back on the rack and the new one docks in its place. Server only; nothing allocates.
/// </summary>
public static class AssemblerToolBay
{
    /// <summary>A swap the chute started: the block taken from the source, delivered at the start of the next step.</summary>
    public readonly record struct Swap(ushort FromBody, ushort FromPart, ushort ToBody, ushort ToPart, ushort Tool);

    /// <summary>The tool a Core wants docked now: its head recipe's (0: none, or it has it).</summary>
    public static byte Wanted(Construct c, int core)
    {
        var d = c.Parts[core].D;
        if (d.BlockRole != BlockRole.Core || ConstructAssembling.RecipeOf(AssemblingTable.Current, c.Parts[core], d) is not { ToolId: > 0 } r)
            return 0;
        return ConstructAssembling.FitOf(c, core).LacksTool(r) ? r.ToolId : (byte)0;
    }

    /// <summary>A chute's move from a store into an Assembler Core: the tool block its head recipe needs, if the store holds one. True when one is on its way.</summary>
    public static bool Chute(ConstructWorld world, Construct cf, int from, CStore src, Construct ct, int to)
    {
        var want = Wanted(ct, to);
        if (want == 0)
            return false;
        foreach (var s in world.ToolSwaps)
            if (s.ToBody == ct.Id && s.ToPart == to)
                return false; // (one is on its way already)
        for (var k = 0; k < src.SlotCount; k++)
            if (ConstructParts.Get(src.Def[k]) is { BlockRole: BlockRole.Tool } t && t.BlockTool == want && world.ToolSwaps.Count < world.ToolSwaps.Capacity && CanSwap(world, cf, from, src, k, ct, to, t) && world.TakeOut(cf, from, k) is { } moved)
            {
                world.ToolSwaps.Add(new Swap(cf.Id, (ushort)from, ct.Id, (ushort)to, moved.Id));
                return true;
            }
        return false;
    }

    /// <summary>
    /// Would a swap of the block in slot <paramref name="slot"/> go through: the old block has a slot to go back to (that one, once
    /// emptied, or another) and the new one a side to dock on? Asked before the block leaves the rack, so a swap that can't happen is
    /// never started, undone and started again at every chute move.
    /// </summary>
    private static bool CanSwap(ConstructWorld world, Construct cf, int from, CStore src, int slot, Construct c, int core, ConstructPartDef tool)
    {
        if (c.Dirty)
            return false; // (the next move asks again, after the rebuild)
        if (MachineBlocks.TryOf(c, core, out var asm) && asm.ToolPart < c.PartCount)
        {
            var old = c.Parts[asm.ToolPart].D;
            var cd = cf.DefOf(from);
            var room = src.Count[slot] == 1 && ConstructStorage.Fits(cd.Slots[slot], old, out _) && (old.PhysicsOnly ? ConstructStorage.RackHolds(cd.Slots[slot], old) : true);
            for (var t = 0; t < src.SlotCount && !room; t++)
                room = ConstructStorage.Check(cd, src, t, old, out _) == SlotCheck.Ok;
            return room; // (its side frees as it goes: the new one docks there)
        }
        return Dock(world, c, core, tool, place: false);
    }

    /// <summary>The swaps the chutes started, done now (the start of the Assemblers' step, every tick).</summary>
    public static void Deliver(ConstructWorld world)
    {
        for (var k = 0; k < world.ToolSwaps.Count; k++)
            Deliver(world, world.ToolSwaps[k]);
        world.ToolSwaps.Clear();
    }

    private static void Deliver(ConstructWorld world, in Swap s)
    {
        if (ConstructParts.Get(s.Tool) is not { } tool)
            return;
        var from = world.Get(s.FromBody);
        if (from is { Dirty: true })
            from.Rebuild();
        var fromPart = from != null && s.FromPart < from.PartCount && world.StoreOf(from, s.FromPart) != null ? s.FromPart : -1;
        if (world.Get(s.ToBody) is not { } c || s.ToPart >= c.PartCount || c.Parts[s.ToPart].D.BlockRole != BlockRole.Core)
        {
            Back(world, from, fromPart, null, -1, tool);
            return;
        }
        if (c.Dirty)
            c.Rebuild();
        int core = s.ToPart;
        var hint = -1;
        if (MachineBlocks.TryOf(c, core, out var asm) && asm.ToolPart < c.PartCount)
        {
            // The old block goes back on the rack the new one came from, into a slot that takes it (the new one's slot is free now).
            int old = asm.ToolPart;
            var oldDef = c.Parts[old].D;
            if (from == null || fromPart < 0 || FreeSlot(world, from, fromPart, oldDef) is not (>= 0 and var slot))
            {
                Back(world, from, fromPart, c, core, tool);
                Console.WriteLine($"[assembler] the {c.Parts[core].D.Name} keeps its {oldDef.Name}: no room for it on the rack the {tool.Name} came from");
                return;
            }
            for (var e = c.FirstLink(core); e >= 0; e = c.NextLink(e))
                if (c.OtherEnd(e) == old)
                {
                    ref readonly var l = ref c.Links[e >> 1];
                    hint = (e & 1) == 0 ? l.SA : l.SB;
                    break;
                }
            world.SetIn(from, fromPart, slot, oldDef);
            var sameBody = ReferenceEquals(from, c);
            world.RemovePart(c, old);
            if (old < core)
                core--;
            if (sameBody && old < fromPart)
                fromPart--;
            if (c.Dirty)
                c.Rebuild();
            if (core >= c.PartCount || c.Parts[core].D.BlockRole != BlockRole.Core)
            {
                Back(world, from, fromPart, null, -1, tool); // (the Core came off with it: a part that held them together went)
                return;
            }
        }
        if (!Dock(world, c, core, tool, hint))
        {
            Back(world, from, fromPart, c, core, tool);
            Console.WriteLine($"[assembler] the {tool.Name} can't dock on the {c.Parts[core].D.Name}: every side of its Core is taken or blocked");
        }
    }

    /// <summary>
    /// Docks a tool block on a free side socket of an Assembler Core (the one <paramref name="hint"/> names first): its "dock" socket
    /// against the Core's side, upright with the Core. False when no side is free and clear.
    /// </summary>
    public static bool Dock(ConstructWorld world, Construct c, int core, ConstructPartDef tool, int hint = -1, bool place = true)
    {
        var dock = -1;
        for (var k = 0; k < tool.Sockets.Length && dock < 0; k++)
            if (tool.Sockets[k].Name == "dock")
                dock = k;
        if (dock < 0)
            return false;
        ref var p = ref c.Parts[core];
        var cd = p.D;
        for (var pass = 0; pass < 2; pass++)
            for (var s = 0; s < cd.Sockets.Length; s++)
            {
                if (pass == 0 ? s != hint : s == hint)
                    continue;
                if (!MachineBlocks.IsToolBay(cd, s) || ConstructPress.OnSocket(c, core, s) >= 0)
                    continue;
                var outward = Rot24.ApplyDir(p.Rot, cd.Sockets[s].Dir);
                var up = Rot24.ApplyDir(p.Rot, 2);
                var rot = -1;
                for (var r = 0; r < Rot24.Count && rot < 0; r++)
                    if (Rot24.ApplyDir((byte)r, tool.Sockets[dock].Dir) == (outward ^ 1) && Rot24.ApplyDir((byte)r, 2) == up)
                        rot = r;
                if (rot < 0)
                    continue;
                var at = ConstructWorld.Quantise(p.Pos + Rot24.Apply(p.Rot, cd.Sockets[s].At) - Rot24.Apply((byte)rot, tool.Sockets[dock].At));
                if (ConstructBuild.Blocked(c, tool, at, (byte)rot, seg: p.Seg))
                    continue;
                if (!place)
                    return true;
                var i = world.AddPart(c, tool, at, (byte)rot, p.Seg);
                if (i < 0)
                    return false;
                world.AddLink(c, core, s, i, dock);
                c.Wake();
                return true;
            }
        return false;
    }

    /// <summary>A slot of a store that takes <paramref name="d"/> now, or -1.</summary>
    private static int FreeSlot(ConstructWorld world, Construct c, int part, ConstructPartDef d)
    {
        if (world.StoreOf(c, part) is not { } st)
            return -1;
        var cd = c.DefOf(part);
        for (var t = 0; t < st.SlotCount; t++)
            if (ConstructStorage.Check(cd, st, t, d, out _) == SlotCheck.Ok)
                return t;
        return -1;
    }

    /// <summary>A block that couldn't dock goes back where it came from, else onto the Core's tray; never lost silently.</summary>
    private static void Back(ConstructWorld world, Construct? from, int fromPart, Construct? c, int core, ConstructPartDef tool)
    {
        if (from != null && fromPart >= 0 && FreeSlot(world, from, fromPart, tool) is >= 0 and var slot && world.SetIn(from, fromPart, slot, tool) == null)
            return;
        if (c != null && core >= 0 && world.StoreOf(c, core) is { } st)
        {
            world.TrayAdd(st, c.Parts[core].D, tool);
            return;
        }
        Console.WriteLine($"[assembler] a {tool.Name} on its way to a Tool Bay was lost: neither its rack nor its Assembler is there");
    }
}

/// <summary>The Tool Bays' side of the world.</summary>
public sealed partial class ConstructWorld
{
    public readonly List<AssemblerToolBay.Swap> ToolSwaps = new(16);
}
