using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Destructible buildings on a map (Buildings/README.md, "Destruction"; <see cref="MapDestruction"/>). Opt-in: the map's
/// top-level "destructible" (or its pack's, Maps/World/pack.json) switches it on for every named Building op, and a
/// Building op's own "destructible" overrides that. Maps without it build exactly as before (same boxes, same fingerprint).
/// </summary>
public sealed partial class Map
{
    /// <summary>Does this map's Building ops make destructible buildings (unless an op says otherwise)?</summary>
    public bool Destructible;

    /// <summary>Was <see cref="Destructible"/> written in the map file itself (rather than the pack's default)?</summary>
    public bool? DestructibleSetting;

    /// <summary>The chunks and their state (null: nothing on this map can be destroyed).</summary>
    public MapDestruction? Destruction;

    /// <summary>Buildings that would have been destructible but went over the map's chunk budget (left whole).</summary>
    public int DestructionOverBudget;

    private List<(int Solid, byte Mat, byte Flags, int Structure)>? _pendingChunks;
    private List<DestructibleStructure>? _pendingStructures;
    private readonly List<Box> _splitScratch = new();

    /// <summary>Chunks recorded so far while loading (the map budget).</summary>
    private int PendingChunks => _pendingChunks?.Count ?? 0;

    /// <summary>A fingerprint of the destruction setup (the material table and the chunk count; 0 without destruction).</summary>
    public uint DestructionHash => Destruction is { } d ? (d.Table.Hash ^ (uint)d.Count * 2654435761u) | 1u : 0u;

    /// <summary>
    /// Adds a building's shell as destructible chunks (instead of whole boxes): every box is cut (<see cref="StructureChunker"/>),
    /// each piece keeps the box's colour and texture. False (nothing added) if it would go over the map's chunk budget.
    /// </summary>
    private bool AddDestructibleShell(BuildingInstance inst, BuildingPlan plan, IReadOnlyList<(BuildingBox Look, bool Foundation)> boxes)
    {
        var table = MaterialDestruction.Current;
        var r = table.Rules;
        var baseY = plan.BaseY;
        // The per-building budget: grow the cells until it fits.
        var scale = 1f;
        for (var tries = 0; tries < 6; tries++)
        {
            var total = 0;
            foreach (var (look, foundation) in boxes)
            {
                _splitScratch.Clear();
                total += foundation ? 1 : StructureChunker.Split(look.Box, r, baseY, scale, _splitScratch);
            }
            if (total <= r.MaxPerBuilding)
                break;
            scale *= 1.4f;
        }
        var count = 0;
        foreach (var (look, foundation) in boxes)
        {
            _splitScratch.Clear();
            count += foundation ? 1 : StructureChunker.Split(look.Box, r, baseY, scale, _splitScratch);
        }
        if (PendingChunks + count > r.MaxPerMap)
        {
            DestructionOverBudget++;
            return false;
        }
        _pendingChunks ??= new();
        _pendingStructures ??= new();
        var st = new DestructibleStructure
        {
            Index = _pendingStructures.Count, Building = inst.Index, ChunkStart = _pendingChunks.Count, SolidStart = Solid.Count, BaseY = baseY,
        };
        var level1 = baseY + BuildingGen.GroundSlab + plan.Def.StoreyHeight;
        foreach (var (look, foundation) in boxes)
        {
            _splitScratch.Clear();
            if (foundation)
                _splitScratch.Add(look.Box);
            else
                StructureChunker.Split(look.Box, r, baseY, scale, _splitScratch);
            var shape = StructureChunker.Classify(look.Box);
            foreach (var piece in _splitScratch)
            {
                var k = AddBuildingBox(piece, look with { Box = piece }, KindShell);
                byte flags = 0;
                var size = piece.Half * 2;
                if (MathF.Min(size.X, MathF.Min(size.Y, size.Z)) <= 0.05f)
                    flags |= MapDestruction.FlagSkin;
                if (shape == StructureChunker.Shape.Slab)
                    flags |= MapDestruction.FlagFloor;
                if (piece.Min.Y <= baseY + 0.12f)
                    flags |= MapDestruction.FlagAnchor;
                if (foundation || piece.Max.Y <= baseY + 0.15f)
                    flags |= MapDestruction.FlagFixed | MapDestruction.FlagAnchor; // the foundation and the ground-floor slab
                else if (r.CornerPillar > 0 && piece.Min.Y < level1 && (flags & (MapDestruction.FlagSkin | MapDestruction.FlagFloor)) == 0 && NearCorner(plan, piece, r.CornerPillar))
                    flags |= MapDestruction.FlagFixed | MapDestruction.FlagAnchor; // load-bearing corners: a building never loses all four
                var normal = shape == StructureChunker.Shape.Slab ? Vector3.UnitY : Vector3.UnitX;
                var surface = look.Layer >= 0 && look.Layer < (int)Material.Count
                    ? ImpactDecals.Refine((Material)look.Layer, look.Color, normal, false)
                    : shape == StructureChunker.Shape.Slab ? "Concrete" : "Plaster"; // untextured: plastered walls, concrete floors
                _pendingChunks.Add((k, table.IdOf(surface), flags, st.Index));
            }
        }
        st.ChunkCount = _pendingChunks.Count - st.ChunkStart;
        st.SolidCount = Solid.Count - st.SolidStart;
        st.Bounds = BoundsOf(st.SolidStart, st.SolidCount);
        _pendingStructures.Add(st);
        return true;
    }

    private static bool NearCorner(BuildingPlan plan, in Box piece, float within)
    {
        foreach (var rect in plan.Footprint)
        {
            ReadOnlySpan<Vector2> corners = [new(rect.X0, rect.Z0), new(rect.X1, rect.Z0), new(rect.X0, rect.Z1), new(rect.X1, rect.Z1)];
            foreach (var c in corners)
            {
                var dx = MathF.Max(0, MathF.Max(piece.Min.X - c.X, c.X - piece.Max.X));
                var dz = MathF.Max(0, MathF.Max(piece.Min.Z - c.Y, c.Y - piece.Max.Z));
                if (dx < within && dz < within)
                    return true;
            }
        }
        return false;
    }

    private Box BoundsOf(int start, int count)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = start; i < start + count; i++)
        {
            min = Vector3.Min(min, Solid[i].Min);
            max = Vector3.Max(max, Solid[i].Max);
        }
        return count == 0 ? default : new Box((min + max) / 2, (max - min) / 2);
    }

    /// <summary>
    /// A destructible block from one corner to the other, cut into chunks like a building's walls: <paramref name="material"/>
    /// is a key of Materials/destruction.json (Brick, Concrete, Wood, Plaster, Metal, Tiles...) and picks its texture too.
    /// It stands on whatever is under its lowest chunks. Drawn and broken like a building (Buildings/README.md, "Destruction").
    /// </summary>
    [MapOp]
    public void DestructibleBox(float x0, float y0, float z0, float x1, float y1, float z1, Vector3 color, string material = "Brick")
    {
        using var __ = Op([x0, y0, z0, x1, y1, z1, color, material]);
        var table = MaterialDestruction.Current;
        if (!table.Has(material))
            throw new InvalidOperationException($"unknown material \"{material}\" (Materials/destruction.json has: {string.Join(", ", table.Materials.Select(m => m.Key))})");
        var min = new Vector3(MathF.Min(x0, x1), MathF.Min(y0, y1), MathF.Min(z0, z1));
        var max = new Vector3(MathF.Max(x0, x1), MathF.Max(y0, y1), MathF.Max(z0, z1));
        if (max.X - min.X < 0.01f || max.Y - min.Y < 0.01f || max.Z - min.Z < 0.01f)
            return;
        var box = new Box((min + max) / 2, (max - min) / 2);
        var layer = material.ToLowerInvariant() switch
        {
            "brick" => (int)Material.Brick,
            "wood" => (int)Material.Wood,
            "metal" => (int)Material.Metal,
            "tiles" => (int)Material.Tiles,
            "grid" => (int)Material.Grid,
            "dirt" => (int)Material.Dirt,
            _ => (int)Material.Concrete,
        };
        var r = table.Rules;
        _splitScratch.Clear();
        StructureChunker.Split(box, r, min.Y, 1f, _splitScratch);
        if (PendingChunks + _splitScratch.Count > r.MaxPerMap)
        {
            DestructionOverBudget++;
            Block(x0, y0, z0, x1, y1, z1, color);
            return;
        }
        _pendingChunks ??= new();
        _pendingStructures ??= new();
        var st = new DestructibleStructure { Index = _pendingStructures.Count, Building = -1, ChunkStart = _pendingChunks.Count, SolidStart = Solid.Count, BaseY = min.Y };
        var id = table.IdOf(material);
        var slab = StructureChunker.Classify(box) == StructureChunker.Shape.Slab;
        foreach (var piece in _splitScratch)
        {
            var k = AddBuildingBox(piece, new BuildingBox(piece, color, layer), KindShell);
            var flags = (byte)(slab ? MapDestruction.FlagFloor : 0);
            if (piece.Min.Y <= min.Y + 0.05f)
                flags |= MapDestruction.FlagAnchor;
            _pendingChunks.Add((k, id, flags, st.Index));
        }
        st.ChunkCount = _pendingChunks.Count - st.ChunkStart;
        st.SolidCount = Solid.Count - st.SolidStart;
        st.Bounds = BoundsOf(st.SolidStart, st.SolidCount);
        _pendingStructures.Add(st);
        _materials = null;
    }

    /// <summary>Map load, after the ops: builds the chunks' support graph (<see cref="MapDestruction.Build"/>). No-op without chunks.</summary>
    internal void FinishDestruction()
    {
        if (_pendingChunks is not { Count: > 0 } chunks || _pendingStructures == null)
            return;
        var d = new MapDestruction();
        d.Structures.AddRange(_pendingStructures);
        d.Build(this, chunks);
        foreach (var st in d.Structures)
            if (st.Building >= 0)
                PlacedBuildings[st.Building].Structure = st.Index;
        Destruction = d;
        _pendingChunks = null;
        _pendingStructures = null;
    }

    /// <summary>The box the fingerprint uses for a chunk: as built (breaking never changes it).</summary>
    internal bool DestructionHashBox(int i, out Box b)
    {
        if (Destruction is { } d && d.IsChunk(i, out var c))
        {
            b = d.Original[c];
            return true;
        }
        b = default;
        return false;
    }

    // ---- Runtime (the server decides; GameDestruction.cs) -----------------------------------------------------

    /// <summary>Takes <paramref name="amount"/> off a chunk (a skin's damage goes to its host). True if that broke it (call <see cref="BreakChunk"/>).</summary>
    public bool DamageChunk(int c, float amount)
    {
        if (Destruction is not { } d || (uint)c >= (uint)d.Count || !(amount > 0))
            return false;
        c = d.Target(c);
        if (d.Broken[c] || (d.Flags[c] & MapDestruction.FlagFixed) != 0)
            return false;
        d.Hp[c] -= amount;
        return d.Hp[c] <= 0;
    }

    /// <summary>
    /// Breaks chunk <paramref name="c"/> (its skins too, and any door or window glass set in it): its box is parked under the
    /// world so nothing collides with it, its decals hide and the renderer drops it. False if it was already broken or can't
    /// break. Doors and windows it took with it are added to the lists if given.
    /// </summary>
    public bool BreakChunk(int c, List<int>? doors = null, List<int>? windows = null)
    {
        if (Destruction is not { } d || (uint)c >= (uint)d.Count)
            return false;
        c = d.Target(c);
        if (d.Broken[c] || (d.Flags[c] & MapDestruction.FlagFixed) != 0)
            return false;
        RemoveChunk(d, c);
        for (var k = d.SkinStart[c]; k < d.SkinStart[c + 1]; k++)
            if (!d.Broken[d.SkinList[k]])
                RemoveChunk(d, d.SkinList[k]);
        var st = d.Structures[d.Owner[c]];
        if (st.Building >= 0)
        {
            var b = PlacedBuildings[st.Building];
            var box = d.Original[c];
            var grown = box with { Half = box.Half + new Vector3(0.06f) };
            for (var i = b.DoorStart; i < b.DoorStart + b.DoorCount; i++)
                if (!BuildingDoors[i].Broken && Overlaps(grown, BuildingDoors[i].Plan.Closed) && BreakBuildingDoor(i))
                    doors?.Add(i);
            for (var i = b.WindowStart; i < b.WindowStart + b.WindowCount; i++)
                if (!BuildingWindows[i].Broken && Overlaps(grown, BuildingWindows[i].Plan.Glass) && BreakBuildingWindow(i))
                    windows?.Add(i);
        }
        return true;
    }

    private void RemoveChunk(MapDestruction d, int c)
    {
        d.Broken[c] = true;
        d.Hp[c] = 0;
        var st = d.Structures[d.Owner[c]];
        st.BrokenCount++;
        st.Version++;
        d.Version++;
        var k = d.Solid[c];
        Solid[k] = ParkedBox(d.Original[c]);
        World.SolidChanged(this, k);
    }

    private static bool Overlaps(in Box a, in Box b) =>
        a.Min.X < b.Max.X && a.Max.X > b.Min.X && a.Min.Y < b.Max.Y && a.Max.Y > b.Min.Y && a.Min.Z < b.Max.Z && a.Max.Z > b.Min.Z;

    /// <summary>
    /// Structure <paramref name="s"/> after something in it broke: every chunk that lost its support falls (broken, added to
    /// <paramref name="fallen"/>), with the props standing on fallen floor chunks (added to <paramref name="props"/>).
    /// Deterministic. Returns how many fell.
    /// </summary>
    public int CollapseStructure(int s, List<int> fallen, List<int>? props = null, List<int>? doors = null, List<int>? windows = null)
    {
        if (Destruction is not { } d || (uint)s >= (uint)d.Structures.Count)
            return 0;
        var first = fallen.Count;
        d.Unsupported(s, fallen);
        for (var k = first; k < fallen.Count; k++)
            BreakChunk(fallen[k], doors, windows);
        if (props != null && fallen.Count > first)
        {
            var bounds = d.Structures[s].Bounds;
            for (var i = 0; i < PropInstances.Count; i++)
            {
                if (IsPropBroken(i))
                    continue;
                var p = PropInstances[i].Position;
                if (p.X < bounds.Min.X || p.X > bounds.Max.X || p.Z < bounds.Min.Z || p.Z > bounds.Max.Z || p.Y < bounds.Min.Y - 0.2f || p.Y > bounds.Max.Y + 0.2f)
                    continue;
                for (var k = first; k < fallen.Count; k++)
                {
                    var b = d.Original[fallen[k]];
                    if ((d.Flags[fallen[k]] & MapDestruction.FlagFloor) != 0 && p.X >= b.Min.X && p.X <= b.Max.X && p.Z >= b.Min.Z && p.Z <= b.Max.Z && MathF.Abs(p.Y - b.Max.Y) < 0.2f)
                    {
                        if (BreakProp(i))
                            props.Add(i);
                        break;
                    }
                }
            }
        }
        return fallen.Count - first;
    }

    /// <summary>Every chunk whole again (a new match, a new session on this map). Part of <see cref="RestoreBuildings"/>.</summary>
    public void RestoreDestruction()
    {
        if (Destruction is not { } d)
            return;
        var any = false;
        for (var c = 0; c < d.Count; c++)
        {
            if (d.Broken[c])
            {
                d.Broken[c] = false;
                Solid[d.Solid[c]] = d.Original[c];
                any = true;
            }
            d.Hp[c] = d.MaxHp[c];
        }
        foreach (var st in d.Structures)
        {
            if (st.BrokenCount != 0)
                st.Version++;
            st.BrokenCount = 0;
        }
        if (any)
        {
            d.Version++;
            World.InvalidateGrid(this); // boxes growing back: the collision world reads them again
        }
    }
}
