using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The material painter on the server (PartMaterials.cs; docs/sandbox/CONSTRUCTS.md, "The material painter"). A part's type is
/// swapped in place for the same part in another material: the two have the same boxes and sockets, so its index, place, links,
/// wires, settings, paint, texture and fuse tag stay as they are and its health keeps its share; mass, health, armour, salvage and
/// look follow from the new type. The body is rebuilt once (a full rebuild: the journal can't say a type changed), and a fused
/// unit sums its mass again. Creative
/// is free; in survival the player pays the new part's units of its material out of what they carry and gets the old part's
/// salvage back, as the Recycler would give it.
/// </summary>
public sealed partial class ConstructEdits
{
    /// <summary>
    /// Server: part <paramref name="index"/> of construct <paramref name="body"/> (<paramref name="mode"/> Whole: every part that can be) made of
    /// offered material <paramref name="material"/> (<see cref="PartMaterials.Offered"/>; Reset: its own file's). The paint tools'
    /// rules (no seat, the tool out, in reach, decision 19's contested check) and the Replacer's (nobody else driving). Null, or why not.
    /// </summary>
    public string? Rematerial(ConstructWorld world, byte player, Vector3 eye, ushort body, PaintMode mode, int index, int material)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Material)
            return "take out the material painter (the tool wheel, or Tab > Tools) to change what parts are made of";
        if (world.Get(body) is not { } c)
            return "that construct is gone";
        if (mode > PaintMode.Reset)
            return "no such mode";
        if (mode != PaintMode.Reset && (uint)material >= (uint)PartMaterials.Offered.Length)
            return "no such material";
        if (c.Dirty)
            c.Rebuild();
        Vector3 at;
        if (mode == PaintMode.Whole)
            at = c.WorldCom;
        else
        {
            if (index < 0 || index >= c.PartCount)
                return "that part is gone";
            at = ReachPoint(c, index, eye);
        }
        if (Vector3.Distance(eye, at) > ConstructBuild.Reach + 1f + (mode == PaintMode.Whole ? c.Radius : 0f))
            return "too far away";
        if (Contested(c, player) is { } defended)
            return defended;
        if (c.Driver != 255 && c.Driver != player)
            return "someone is driving it";
        var inv = InfiniteParts || Inventory == null ? null : Inventory.Of(player);
        if (inv is { Infinite: true })
            inv = null;
        if (mode != PaintMode.Whole)
        {
            var d = c.DefOf(index);
            if (PartMaterials.Refusal(d) is { } cant)
                return cant;
            if (Target(d, mode, material) is not { } e)
                return "no room for another part of another material: restart the game";
            if (e.Id == d.Id)
                return null; // (already made of it: a stroke passes over it quietly)
            var one = Remake(inv, c, index, d, e);
            if (one == null)
                Remade(world, c);
            return one;
        }
        // The whole construct: every part that can be remade, the cost added up first (all or nothing).
        int need = 0, made = 0;
        string? first = null;
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            if (!PartMaterials.Paintable(d) || Target(d, mode, material) is not { } e || e.Id == d.Id)
                continue;
            need += PartMaterials.UnitsFor(e);
        }
        if (inv != null && mode != PaintMode.Reset && PartMaterials.Offered[material] is var m && RecyclingTable.Current.IdOf(m) is var id && inv.BulkOf(id) < need)
            return $"not enough {m}: the whole construct takes {need}, you carry {inv.Materials[id]}";
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            if (!PartMaterials.Paintable(d) || Target(d, mode, material) is not { } e || e.Id == d.Id)
                continue;
            if (Remake(inv, c, i, d, e) is { } why)
                first ??= why;
            else
                made++;
        }
        if (made > 0)
            Remade(world, c);
        if (made == 0)
            return first ?? "nothing on it can be made of that";
        Notices.Add((player, $"Material painter: {made} parts remade{(first != null ? $", some not ({first})" : "")}"));
        return null;
    }

    /// <summary>The part <paramref name="d"/> becomes: its file part for Reset, else the same part in the material.</summary>
    private static ConstructPartDef? Target(ConstructPartDef d, PaintMode mode, int material) =>
        mode == PaintMode.Reset ? PartMaterials.BaseOf(d) : PartMaterials.Variant(d, material);

    /// <summary>One part remade (its type swapped), paid for in survival. Null, or why not. The caller then calls <see cref="Remade"/> once.</summary>
    private static string? Remake(SandboxInventory? inv, Construct c, int part, ConstructPartDef d, ConstructPartDef e)
    {
        ref var p = ref c.Parts[part];
        if (p.Broken)
            return "that part is wrecked: take it off";
        if (e.Sockets.Length != d.Sockets.Length || e.Boxes.Length != d.Boxes.Length)
            return $"a {e.Name} isn't the same shape"; // (never: a remade part is its file part's shape)
        if (inv != null)
        {
            var names = RecyclingTable.Current;
            var id = names.IdOf(PartMaterials.MaterialOf(e));
            var cost = PartMaterials.UnitsFor(e);
            if (inv.BulkOf(id) < cost)
                return $"not enough {names.NameOf(id)}: a {e.Name} takes {cost}, you carry {inv.Materials[id]}";
            var (back, units) = RecyclingTable.Current.YieldOf(in p); // (what the old one would salvage for)
            inv.PayBulk(id, cost);
            if (back != 0 && units > 0)
                inv.AddMaterial(back, units);
        }
        var share = MathF.Min(1, p.Health / MathF.Max(1, d.Health));
        p.Def = e.Id;
        p.Health = e.Health * share;
        return null;
    }

    /// <summary>After parts of a body were remade: rebuilt once, a fused unit's mass summed again, and the clients sent the body.</summary>
    private static void Remade(ConstructWorld world, Construct c)
    {
        c.TreeStale();
        c.FuseMassStale();
        c.Dirty = true;
        c.Wake();
        world.StructureDirty.Add(c.Id);
    }
}
