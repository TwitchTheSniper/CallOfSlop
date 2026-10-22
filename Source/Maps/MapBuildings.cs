using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>Raw JSON kept as text: the "variant" object of a Building op (written back out exactly as read).</summary>
public readonly record struct MapJson(string Text);

/// <summary>One building placed on a map (a Building op with a "name").</summary>
public sealed class BuildingInstance
{
    public required BuildingDef Def;
    public required BuildingPlan Plan;
    public int Index;

    /// <summary>Its shell boxes are <see cref="Map.Solid"/>[ShellStart .. ShellStart + ShellCount).</summary>
    public int ShellStart, ShellCount;

    public int DoorStart, DoorCount, WindowStart, WindowCount;

    /// <summary>Height of the building's highest shell box (its roof or parapet top).</summary>
    public float Top;
}

/// <summary>A door in a building: open or shut (the interact key), and it breaks after enough damage. Runtime state lives here.</summary>
public sealed class BuildingDoor
{
    public required PlannedDoor Plan;
    public int SolidIndex, Building;
    public bool Open, Broken;
    public int Health;

    /// <summary>How far the leaf has swung, 0 shut to 1 open (drawing only, eases towards <see cref="Open"/>).</summary>
    public float Swing;

    public Vector3 Center => Plan.Closed.Center;
}

/// <summary>A window's glass: blocks bodies, lets bullets through, and shatters when shot.</summary>
public sealed class BuildingWindow
{
    public required PlannedWindow Plan;
    public int SolidIndex, Building;
    public bool Broken;
    public int Health;
}

/// <summary>
/// Buildings on a map (see Buildings/README.md): the Building op with a "name" (<see cref="PlaceBuilding"/>), its boxes,
/// doors and windows, and their state. Shell boxes are ordinary collision boxes (bullets, bodies, bots and foliage treat
/// them like any wall) but are drawn by the building renderer, batched, not one draw call each. A shut door is a box on
/// its own physics layer (<see cref="QueryLayers.Doors"/>: bodies and bullets stop, bot navigation plans through it); an
/// open or broken door's box is parked under the world like a broken prop's. Window glass is a clip box (bodies stop,
/// bullets pass and break it). Maps without buildings are unchanged (fingerprint included).
/// </summary>
public sealed partial class Map
{
    public readonly List<BuildingInstance> PlacedBuildings = new();
    public readonly List<BuildingDoor> BuildingDoors = new();
    public readonly List<BuildingWindow> BuildingWindows = new();

    /// <summary>Anchor points the buildings offer (spawns, hills, flags, points), world space.</summary>
    public readonly List<(string Type, string Name, Vector3 At, int Building)> BuildingAnchors = new();

    /// <summary>
    /// The buildings' waypoints (also in <see cref="Waypoints"/>): either side of every doorway, stair ends, room centres. The nav
    /// build places these exactly and never merges them into a nearby grid node (one off the door's axis can't see through it).
    /// </summary>
    public readonly HashSet<Vector3> BuildingNavPoints = new();

    /// <summary>
    /// Ways the nav build links without walking them (both ways): the buildings' stairs, foot to top. A body on a flight of
    /// short steps always overlaps the next ones, which the nav's sampled walk reads as a wall; players and bots step up them.
    /// </summary>
    public readonly List<(Vector3 A, Vector3 B)> NavLinks = new();

    /// <summary>A fingerprint of the buildings placed (their files, seeds, turns and variants); 0 without any.</summary>
    public uint BuildingHash;

    /// <summary>Goes up whenever a door or window changes (renderers refresh).</summary>
    public int BuildingStateVersion;

    private byte[] _buildingKind = []; // per Solid box: 0 none, 1 shell, 2 door, 3 glass
    private readonly Dictionary<int, Box> _buildingOriginal = new();
    private readonly HashSet<PropDef> _buildingLocalProps = new();

    private const byte KindShell = 1, KindDoor = 2, KindGlass = 3;

    /// <summary>Is Solid[i] part of a building (drawn by the building renderer, not as a map box)?</summary>
    public bool IsBuildingBox(int i) => (uint)i < (uint)_buildingKind.Length && _buildingKind[i] != 0;

    /// <summary>Is Solid[i] a building door (the <see cref="QueryLayers.Doors"/> layer)?</summary>
    public bool IsBuildingDoorBox(int i) => (uint)i < (uint)_buildingKind.Length && _buildingKind[i] == KindDoor;

    /// <summary>Is Solid[i] window glass (a clip box: bodies stop, bullets pass)?</summary>
    public bool IsBuildingGlass(int i) => (uint)i < (uint)_buildingKind.Length && _buildingKind[i] == KindGlass;

    /// <summary>The box the fingerprint uses for a door or window: as built (opening or breaking never changes it).</summary>
    internal bool BuildingHashBox(int i, out Box b) => _buildingOriginal.TryGetValue(i, out b);

    /// <summary>The prop renderer's library: the pack's (or global) props, plus building-local props this map uses.</summary>
    public PropDef[] PropLibrary => _buildingLocalProps.Count == 0
        ? Props.ForPack(PackDir).Library
        : CallOfSlop.Buildings.CombinedLibrary(Props.ForPack(PackDir).Library, _buildingLocalProps);

    /// <summary>
    /// A building from the Buildings folder, its origin at <paramref name="at"/> ([x, z] or [x, y, z]; y is above the
    /// ground), turned by <paramref name="yaw"/> degrees (a multiple of 90). <paramref name="seed"/> 0 uses the building's
    /// own seed. <paramref name="variant"/>: { "palette", "floors", "mirror", "roof", "furnish", "doors" }. With
    /// <paramref name="spawns"/> its spawn anchors become spawn points.
    /// </summary>
    [MapOp]
    public void PlaceBuilding(string name, float[] at, float yaw = 0, int seed = 0, MapJson? variant = null, bool spawns = false)
    {
        using var __ = Op([name, at, yaw, seed, variant, spawns]);
        var def = CallOfSlop.Buildings.Find(name) ?? throw new InvalidOperationException($"unknown building \"{name}\": {CallOfSlop.Buildings.Suggest(name)}");
        if (at.Length is not (2 or 3))
            throw new InvalidOperationException("\"at\" is [x, z] or [x, y, z]");
        var quarter = yaw / 90f;
        if (MathF.Abs(quarter - MathF.Round(quarter)) > 0.001f)
            throw new InvalidOperationException("\"yaw\" is a multiple of 90 (buildings stay square to the map)");
        var q = (((int)MathF.Round(quarter)) % 4 + 4) % 4;
        var v = ParseVariant(variant);
        var x = at[0];
        var z = at[^1];
        var up = at.Length == 3 ? at[1] : 0f;

        // On terrain: stand on the highest ground under the footprint, with a foundation down to the lowest.
        var baseY = up;
        float lowest = 0;
        BRect? world = null;
        if (HasTerrain)
        {
            var probe = BuildingGen.Generate(def, new BuildingVariant { Furnish = false, Floors = v.Floors, Mirror = v.Mirror, Palette = v.Palette, Roof = v.Roof },
                seed, new Vector3(x, 0, z), q, PropScope);
            var fp = probe.Footprint.Aggregate((a, b) => a.Union(b));
            world = fp;
            if (_topAnchor == OpAnchor.World)
                baseY = up;
            else
            {
                float hi = float.MinValue, lo = float.MaxValue;
                for (var sx = fp.X0; sx <= fp.X1 + 0.01f; sx += MathF.Max(0.5f, fp.W / 16))
                    for (var sz = fp.Z0; sz <= fp.Z1 + 0.01f; sz += MathF.Max(0.5f, fp.D / 16))
                    {
                        var g = GroundHeight(sx, sz);
                        hi = MathF.Max(hi, g);
                        lo = MathF.Min(lo, g);
                    }
                baseY = hi + up;
                lowest = lo;
            }
        }
        BuildingPlan plan;
        try
        {
            plan = BuildingGen.Generate(def, v, seed, new Vector3(x, baseY, z), q, PropScope);
        }
        catch (BuildingFileException ex)
        {
            throw new InvalidOperationException(ex.Message);
        }
        var inst = new BuildingInstance { Def = def, Plan = plan, Index = PlacedBuildings.Count, ShellStart = Solid.Count };
        var foundation = def.FloorSurface.Color.Resolve(def.Palette(v.Palette)) * 0.8f;
        if (world != null && baseY > lowest + 0.05f)
            foreach (var r in plan.Footprint)
                AddBuildingBox(new Box(new Vector3((r.X0 + r.X1) / 2, (lowest - 0.3f + baseY) / 2, (r.Z0 + r.Z1) / 2),
                    new Vector3(r.W / 2, (baseY - lowest + 0.3f) / 2, r.D / 2)), new BuildingBox(default, foundation, (int)Material.Concrete), KindShell);
        foreach (var b in plan.Shell)
            AddBuildingBox(b.Box, b, KindShell);
        inst.ShellCount = Solid.Count - inst.ShellStart;
        inst.Top = baseY;
        for (var i = inst.ShellStart; i < Solid.Count; i++)
            inst.Top = MathF.Max(inst.Top, Solid[i].Max.Y);
        inst.DoorStart = BuildingDoors.Count;
        foreach (var d in plan.Doors)
        {
            var k = AddBuildingBox(d.Closed, new BuildingBox(d.Closed, d.Color, -1), KindDoor);
            var door = new BuildingDoor { Plan = d, SolidIndex = k, Building = inst.Index, Open = d.StartOpen, Health = d.Health, Swing = d.StartOpen ? 1 : 0 };
            if (door.Open)
                Solid[k] = ParkedBox(d.Closed);
            BuildingDoors.Add(door);
        }
        inst.DoorCount = BuildingDoors.Count - inst.DoorStart;
        inst.WindowStart = BuildingWindows.Count;
        foreach (var w in plan.Windows)
        {
            var k = AddBuildingBox(w.Glass, new BuildingBox(w.Glass, GlassColor, -1), KindGlass);
            BuildingWindows.Add(new BuildingWindow { Plan = w, SolidIndex = k, Building = inst.Index, Health = w.Health });
        }
        inst.WindowCount = BuildingWindows.Count - inst.WindowStart;
        foreach (var p in plan.Props)
        {
            AddProp(p.Def, p.Position, p.Yaw, p.Scale, null);
            if (p.Def.Pack != null && p.Def.Pack.StartsWith("Buildings/", StringComparison.Ordinal))
                _buildingLocalProps.Add(p.Def);
        }
        Waypoints.AddRange(plan.Waypoints);
        BuildingNavPoints.UnionWith(plan.Waypoints);
        NavLinks.AddRange(plan.Stairs);
        foreach (var (type, aname, pos) in plan.Anchors)
        {
            BuildingAnchors.Add((type, aname, pos, inst.Index));
            if (spawns && type == "spawn")
                Spawns.Add(pos);
        }
        PlacedBuildings.Add(inst);
        _materials = null;

        // Fingerprint: the building's files, the seed, turn and variant (the boxes, props and waypoints count anyway).
        var h = BuildingHash == 0 ? 2166136261u : BuildingHash;
        void Mix(uint u)
        {
            for (var i = 0; i < 4; i++)
            {
                h ^= (u >> (i * 8)) & 0xFF;
                h *= 16777619u;
            }
        }
        Mix(def.FileHash);
        Mix((uint)seed);
        Mix((uint)q);
        foreach (var ch in variant?.Text ?? "")
            Mix(ch);
        foreach (var r in plan.Rooms)
            if (def.Rule(r.Type) is { } rule)
                Mix(rule.FileHash);
        BuildingHash = h == 0 ? 1 : h;
    }

    /// <summary>The colour window glass is drawn in (see-through).</summary>
    public static readonly Vector3 GlassColor = new(0.62f, 0.8f, 0.9f);

    /// <summary>Each building box's colour and texture layer (index: Solid index), for the building renderer.</summary>
    public readonly Dictionary<int, BuildingBox> BuildingLooks = new();

    private int AddBuildingBox(Box box, BuildingBox look, byte kind)
    {
        var k = Solid.Count;
        Solid.Add(box);
        Colors.Add(look.Color);
        if (_buildingKind.Length < Solid.Count)
            Array.Resize(ref _buildingKind, Math.Max(Solid.Count, _buildingKind.Length * 2 + 256));
        _buildingKind[k] = kind;
        BuildingLooks[k] = look with { Box = box };
        if (kind != KindShell)
            _buildingOriginal[k] = box;
        return k;
    }

    private static Box ParkedBox(Box b) => new(new Vector3(b.Center.X, -1000f, b.Center.Z), new Vector3(0.001f));

    private static BuildingVariant ParseVariant(MapJson? json)
    {
        if (json is not { } j)
            return BuildingVariant.Default;
        using var doc = JsonDocument.Parse(j.Text);
        var e = doc.RootElement;
        if (e.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("\"variant\" is an object: { \"palette\": ..., \"floors\": ..., \"mirror\": ..., \"roof\": ..., \"furnish\": ..., \"doors\": ... }");
        var v = new BuildingVariant();
        foreach (var p in e.EnumerateObject())
        {
            var val = p.Value;
            switch (p.Name)
            {
                case "palette" when val.ValueKind == JsonValueKind.String:
                    v.Palette = val.GetString();
                    break;
                case "floors" when val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out var fl) && fl is >= 1 and <= 12:
                    v.Floors = fl;
                    break;
                case "mirror" when val.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    v.Mirror = val.GetBoolean();
                    break;
                case "furnish" when val.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    v.Furnish = val.GetBoolean();
                    break;
                case "roof" when val.ValueKind == JsonValueKind.String:
                    v.Roof = val.GetString()!.ToLowerInvariant() switch
                    {
                        "flat" => RoofKind.Flat,
                        "gable" => RoofKind.Gable,
                        "hip" => RoofKind.Hip,
                        "shed" => RoofKind.Shed,
                        "none" => RoofKind.None,
                        var r => throw new InvalidOperationException($"variant \"roof\" is flat, gable, hip, shed or none (got \"{r}\")"),
                    };
                    break;
                case "doors" when val.ValueKind == JsonValueKind.String && val.GetString() is "closed" or "open" or "random":
                    v.Doors = val.GetString()!;
                    break;
                default:
                    throw new InvalidOperationException($"variant: bad \"{p.Name}\": {val} (it takes palette (name), floors (1-12), mirror, roof (flat/gable/hip/shed/none), furnish (true/false), doors (closed/open/random))");
            }
        }
        return v;
    }

    // ---- Door and window state (the server decides; see GameBuildings.cs) -------------------------------------

    /// <summary>Opens or shuts a door. False if it's broken or already that way.</summary>
    public bool SetBuildingDoor(int i, bool open)
    {
        if ((uint)i >= (uint)BuildingDoors.Count)
            return false;
        var d = BuildingDoors[i];
        if (d.Broken || d.Open == open)
            return false;
        d.Open = open;
        Solid[d.SolidIndex] = open ? ParkedBox(d.Plan.Closed) : d.Plan.Closed;
        if (open)
            World.SolidChanged(this, d.SolidIndex);
        else
            World.InvalidateGrid(this); // a box growing back: the collision world reads the boxes again
        BuildingStateVersion++;
        return true;
    }

    /// <summary>Takes damage off a door. True if that broke it (call <see cref="BreakBuildingDoor"/>).</summary>
    public bool DamageBuildingDoor(int i, int amount)
    {
        if ((uint)i >= (uint)BuildingDoors.Count || BuildingDoors[i].Broken || amount <= 0)
            return false;
        var d = BuildingDoors[i];
        d.Health -= amount;
        return d.Health <= 0;
    }

    /// <summary>Breaks a door: it's gone for the rest of the match and the doorway stays open. False if already broken.</summary>
    public bool BreakBuildingDoor(int i)
    {
        if ((uint)i >= (uint)BuildingDoors.Count || BuildingDoors[i].Broken)
            return false;
        var d = BuildingDoors[i];
        d.Broken = true;
        d.Health = 0;
        if (!d.Open)
        {
            Solid[d.SolidIndex] = ParkedBox(d.Plan.Closed);
            World.SolidChanged(this, d.SolidIndex);
        }
        d.Open = true;
        BuildingStateVersion++;
        return true;
    }

    /// <summary>Takes damage off a window's glass. True if that broke it.</summary>
    public bool DamageBuildingWindow(int i, int amount)
    {
        if ((uint)i >= (uint)BuildingWindows.Count || BuildingWindows[i].Broken || amount <= 0)
            return false;
        var w = BuildingWindows[i];
        w.Health -= amount;
        return w.Health <= 0;
    }

    /// <summary>Shatters a window: the glass is gone and the opening can be climbed through. False if already broken.</summary>
    public bool BreakBuildingWindow(int i)
    {
        if ((uint)i >= (uint)BuildingWindows.Count || BuildingWindows[i].Broken)
            return false;
        var w = BuildingWindows[i];
        w.Broken = true;
        w.Health = 0;
        Solid[w.SolidIndex] = ParkedBox(w.Plan.Glass);
        World.SolidChanged(this, w.SolidIndex);
        BuildingStateVersion++;
        return true;
    }

    /// <summary>Every door back to its starting state and every window whole (a new match, a new session on this map).</summary>
    public void RestoreBuildings()
    {
        if (BuildingDoors.Count == 0 && BuildingWindows.Count == 0)
            return;
        var changed = false;
        foreach (var d in BuildingDoors)
        {
            var open = d.Plan.StartOpen;
            if (d.Broken || d.Open != open || d.Health != d.Plan.Health)
            {
                changed |= d.Broken || d.Open != open;
                d.Broken = false;
                d.Open = open;
                d.Health = d.Plan.Health;
                d.Swing = open ? 1 : 0;
                Solid[d.SolidIndex] = open ? ParkedBox(d.Plan.Closed) : d.Plan.Closed;
            }
        }
        foreach (var w in BuildingWindows)
        {
            if (!w.Broken && w.Health == w.Plan.Health)
                continue;
            changed |= w.Broken;
            w.Broken = false;
            w.Health = w.Plan.Health;
            Solid[w.SolidIndex] = w.Plan.Glass;
        }
        if (changed)
            World.InvalidateGrid(this);
        BuildingStateVersion++;
    }

    /// <summary>Is (x, z) inside a building's footprint? (Foliage keeps out, roof or not.)</summary>
    public bool InsideBuilding(float x, float z)
    {
        foreach (var b in PlacedBuildings)
        {
            if (HasTerrain && b.Top < GroundHeight(x, z) - 0.5f)
                continue; // a building buried in a terrain cave (World Tour "Silo"): plants grow on the ground above it
            foreach (var r in b.Plan.Footprint)
                if (r.Contains(x, z, 0.3f))
                    return true;
        }
        return false;
    }
}
