using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A map: a list of solid boxes (each with a colour), spawn points and bot waypoints.
/// Everything is axis-aligned boxes, so collision, shooting and rendering all share one representation.
/// </summary>
public sealed partial class Map
{
    [NotBaked] // (set by Maps.LoadFolder: a position in the list)
    public byte Id;
    public required string Name;
    public required string Description;
    public required float HalfX; // playable bounds (centred on the origin)
    public required float HalfZ;
    public Vector3 Sky = new(0.36f, 0.42f, 0.50f);

    public readonly List<Box> Solid = new();

    /// <summary>SHA-256 of the map file's text (empty for a map built in code): part of a baked nav graph's key (NavBake).</summary>
    public byte[] SourceTextHash { get; init; } = [];

    /// <summary>Wall-clock ms <see cref="MapFile.Load"/> took for this map in <see cref="Maps.LoadFolder(string, List{string}, bool)"/> (0 otherwise).</summary>
    [NotBaked]
    public double LoadMs;

    /// <summary>Wall-clock ms <see cref="Validate"/> took for this map in <see cref="Maps.LoadFolder(string, List{string}, bool)"/>.</summary>
    [NotBaked]
    public double ValidateMs;
    public readonly List<Vector3> Colors = new();
    public readonly List<Vector3> Spawns = new();
    public readonly List<Vector3> Waypoints = new();

    public string SizeLabel => $"{HalfX * 2:0} x {HalfZ * 2:0} m";
    public float ViewRadius => MathF.Max(HalfX, HalfZ) * 1.15f;

    /// <summary>Small / medium / large, by playable area (a long thin map counts as smaller).</summary>
    public MapSize Size
    {
        get
        {
            var side = MathF.Sqrt(PlayArea); // the rectangle's area, or the boundary outline's
            return side < 75 ? MapSize.Small : side < 135 ? MapSize.Medium : MapSize.Large;
        }
    }

    public float FarPlane => MathF.Max(300f, MathF.Sqrt(HalfX * HalfX + HalfZ * HalfZ) * 2.4f);
    /// <summary>
    /// Distance fog. Small rectangular maps have none (their floor slab reaches the far plane and stays lit like the map).
    /// A small map with a boundary gets fog that starts past the whole playable area, so nothing inside it is touched,
    /// and fades the darker far ground beyond the outline into the sky's horizon (without it, that ground ran unfogged to
    /// the far plane and read as a black void under the horizon on a dark sky such as "dusk").
    /// </summary>
    public float FogStart => PointGravity != null ? 400f : Size == MapSize.Large ? 70f : Size == MapSize.Medium ? 90f : Boundary != null ? SmallBoundaryFogStart : 400f;

    /// <inheritdoc cref="FogStart"/>
    public float FogEnd => PointGravity != null ? 0f : Size == MapSize.Large ? FarPlane * 0.9f : Size == MapSize.Medium ? 260f : Boundary != null ? FarPlane * 0.9f : 0f; // small rectangles: no fog

    /// <summary>A small shaped map's fog start: past the widest view across the playable area (at least the medium maps' 90 m).</summary>
    private float SmallBoundaryFogStart => MathF.Min(MathF.Max(90f, ViewRadius * 2f + 10f), FarPlane * 0.6f);

    // ---- Building blocks ------------------------------------------------------------

    /// <summary>A box from one corner to the other (any order).</summary>
    [MapOp]
    public void Block(float x0, float y0, float z0, float x1, float y1, float z1, Vector3 color)
    {
        using var __ = Op([x0, y0, z0, x1, y1, z1, color]);
        var min = new Vector3(MathF.Min(x0, x1), MathF.Min(y0, y1), MathF.Min(z0, z1));
        var max = new Vector3(MathF.Max(x0, x1), MathF.Max(y0, y1), MathF.Max(z0, z1));
        if (max.X - min.X < 0.01f || max.Y - min.Y < 0.01f || max.Z - min.Z < 0.01f)
            return;
        Solid.Add(new Box((min + max) / 2, (max - min) / 2));
        Colors.Add(color);
    }

    /// <summary>Box standing on the ground (or on <paramref name="y"/>) centred at x,z.</summary>
    [MapOp]
    public void Prop(float x, float z, float w, float h, float d, Vector3 color, float y = 0) 
    {
        using var __ = Op([x, z, w, h, d, color, y]);
        Block(x - w / 2, y, z - d / 2, x + w / 2, y + h, z + d / 2, color);
    }

    /// <summary>The floor slab under the whole map. On terrain maps the terrain is the floor, so this adds nothing.</summary>
    [MapOp]
    public void Ground(Vector3 color)
    {
        using var __ = Op([color]);
        if (HasTerrain)
            return;
        GroundIndex = Solid.Count;
        Block(-HalfX - 2, -1, -HalfZ - 2, HalfX + 2, 0, HalfZ + 2, color);
        if (Boundary != null) // the slab stays as the collider; the floor is drawn following the outline (MapEdges.cs)
            AddBoundaryGround(color, Textures.Pick(this, Solid[GroundIndex], color, true));
    }

    public int GroundIndex = -1;
    private Material[]? _materials;

    /// <summary>Surface texture for each solid box (picked from shape and colour, see <see cref="Textures.Pick"/>).</summary>
    public Material[] Materials =>
        _materials ??= Enumerable.Range(0, Solid.Count).Select(i => Textures.Pick(this, Solid[i], Colors[i], i == GroundIndex)).ToArray();

    [MapOp]
    public void Perimeter(float height, Vector3 color, float t = 1f)
    {
        using var __ = Op([height, color, t]);
        if (Boundary != null)
            throw new InvalidOperationException("this map has a \"boundary\": its edge replaces Perimeter (use \"edge\": { \"style\": \"wall\" })");
        if (HasTerrain)
        {
            TerrainPerimeter(height, color, t);
            return;
        }
        Block(-HalfX - t, 0, -HalfZ - t, HalfX + t, height, -HalfZ, color);
        Block(-HalfX - t, 0, HalfZ, HalfX + t, height, HalfZ + t, color);
        Block(-HalfX - t, 0, -HalfZ, -HalfX, height, HalfZ, color);
        Block(HalfX, 0, -HalfZ, HalfX + t, height, HalfZ, color);
    }

    public readonly record struct Hole(float Center, float Width, float Bottom, float Top);

    /// <summary>Wall running along X (from x0 to x1) occupying z..z+t, with rectangular openings.</summary>
    [MapOp]
    public void WallX(float x0, float x1, float z, float t, float y0, float y1, Vector3 color, params Hole[] holes)
    {
        using var __ = Op([x0, x1, z, t, y0, y1, color, holes]);
        var cursor = x0;
        foreach (var h in holes.OrderBy(h => h.Center))
        {
            var a = h.Center - h.Width / 2;
            var b = h.Center + h.Width / 2;
            Block(cursor, y0, z, a, y1, z + t, color); // solid part before the hole
            Block(a, y0, z, b, h.Bottom, z + t, color); // below (window sill)
            Block(a, h.Top, z, b, y1, z + t, color); // above (lintel)
            cursor = b;
        }
        Block(cursor, y0, z, x1, y1, z + t, color);
    }

    /// <summary>Wall running along Z (from z0 to z1) occupying x..x+t, with rectangular openings.</summary>
    [MapOp]
    public void WallZ(float z0, float z1, float x, float t, float y0, float y1, Vector3 color, params Hole[] holes)
    {
        using var __ = Op([z0, z1, x, t, y0, y1, color, holes]);
        var cursor = z0;
        foreach (var h in holes.OrderBy(h => h.Center))
        {
            var a = h.Center - h.Width / 2;
            var b = h.Center + h.Width / 2;
            Block(x, y0, cursor, x + t, y1, a, color);
            Block(x, y0, a, x + t, h.Bottom, b, color);
            Block(x, h.Top, a, x + t, y1, b, color);
            cursor = b;
        }
        Block(x, y0, cursor, x + t, y1, z1, color);
    }

    /// <summary>Horizontal slab with an optional rectangular hole (for stairwells).</summary>
    [MapOp]
    public void Slab(float x0, float z0, float x1, float z1, float yTop, float thick, Vector3 color,
        (float X0, float Z0, float X1, float Z1)? hole = null)
    {
        using var __ = Op([x0, z0, x1, z1, yTop, thick, color, hole]);
        var y0 = yTop - thick;
        if (hole is not { } h)
        {
            Block(x0, y0, z0, x1, yTop, z1, color);
            return;
        }
        Block(x0, y0, z0, x1, yTop, h.Z0, color);
        Block(x0, y0, h.Z1, x1, yTop, z1, color);
        Block(x0, y0, h.Z0, h.X0, yTop, h.Z1, color);
        Block(h.X1, y0, h.Z0, x1, yTop, h.Z1, color);
    }

    public const float StepRise = 0.4f;
    public const float StepRun = 0.45f;

    /// <summary>Solid staircase climbing <paramref name="height"/> from (x0..x1, starting at z) towards +Z (dir=1) or -Z (dir=-1).</summary>
    [MapOp]
    public float StairsZ(float x0, float x1, float z, int dir, float y0, float height, Vector3 color)
    {
        using var __ = Op([x0, x1, z, dir, y0, height, color]);
        var n = (int)MathF.Ceiling(height / StepRise);
        var rise = height / n;
        for (var k = 1; k <= n; k++)
        {
            var za = z + dir * (k - 1) * StepRun;
            var zb = z + dir * k * StepRun;
            Block(x0, y0, za, x1, y0 + k * rise, zb, color);
        }
        return z + dir * n * StepRun; // where the top step ends
    }

    /// <summary>Solid staircase along X.</summary>
    [MapOp]
    public float StairsX(float z0, float z1, float x, int dir, float y0, float height, Vector3 color)
    {
        using var __ = Op([z0, z1, x, dir, y0, height, color]);
        var n = (int)MathF.Ceiling(height / StepRise);
        var rise = height / n;
        for (var k = 1; k <= n; k++)
        {
            var xa = x + dir * (k - 1) * StepRun;
            var xb = x + dir * k * StepRun;
            Block(xa, y0, z0, xb, y0 + k * rise, z1, color);
        }
        return x + dir * n * StepRun;
    }

    public const float StoryHeight = 3.2f;

    /// <summary>
    /// A walk-in building: doors on the ground floor (sides given by "NSEW"), windows on every floor,
    /// internal stairs between floors, and optionally stairs onto a flat roof with a parapet.
    /// Needs depth (Z) of at least 10 m for the stairs.
    /// </summary>
    [MapOp]
    public void Building(float cx, float cz, float w, float d, int stories, Vector3 wall, Vector3 floor, string doors, bool roofAccess, float parapet = 1.0f)
    {
        using var __ = Op([cx, cz, w, d, stories, wall, floor, doors, roofAccess, parapet]);
        const float t = 0.3f, H = StoryHeight, slab = 0.25f, stairW = 1.4f;
        float x0 = cx - w / 2, x1 = cx + w / 2, z0 = cz - d / 2, z1 = cz + d / 2;

        for (var s = 0; s < stories; s++)
        {
            float y0 = s * H, y1 = y0 + H;
            Hole Door(float c) => new(c, 1.7f, y0, y0 + 2.5f);
            Hole Win(float c) => new(c, 1.5f, y0 + 1.0f, y0 + 2.3f);
            Hole[] SideX(char side) =>
                s == 0 && doors.Contains(side) ? [Door(cx)] : w >= 10 ? [Win(cx - w / 4), Win(cx + w / 4)] : [Win(cx)];
            Hole[] SideZ(char side) =>
                s == 0 && doors.Contains(side) ? [Door(cz + d * 0.2f)] : d >= 10 ? [Win(cz - d / 4 + 1), Win(cz + d / 4)] : [Win(cz)];

            WallX(x0, x1, z0, t, y0, y1, wall, SideX('N'));
            WallX(x0, x1, z1 - t, t, y0, y1, wall, SideX('S'));
            WallZ(z0 + t, z1 - t, x0, t, y0, y1, wall, SideZ('W'));
            WallZ(z0 + t, z1 - t, x1 - t, t, y0, y1, wall, SideZ('E'));

            // Stairs up from this floor (alternating ends so flights don't stack).
            var hasStairs = s < stories - 1 || roofAccess;
            (float, float, float, float)? hole = null;
            if (hasStairs)
            {
                float sx0 = x0 + t, sx1 = sx0 + stairW;
                if (s % 2 == 0)
                {
                    var zs = z0 + t + 0.6f;
                    var zEnd = StairsZ(sx0, sx1, zs, 1, y0, H, floor);
                    hole = (sx0, zs - 0.3f, sx1 + 0.2f, zEnd);
                }
                else
                {
                    var zs = z1 - t - 0.6f;
                    var zEnd = StairsZ(sx0, sx1, zs, -1, y0, H, floor);
                    hole = (sx0, zEnd, sx1 + 0.2f, zs + 0.3f);
                }
            }
            // Ceiling / next floor / roof
            Slab(x0, z0, x1, z1, y1, slab, floor, hole);
        }

        var roofY = stories * H;
        if (roofAccess)
        {
            // Low wall around the roof edge (at 0.5 m you can step over it).
            var p = parapet;
            Block(x0, roofY, z0, x1, roofY + p, z0 + t, wall);
            Block(x0, roofY, z1 - t, x1, roofY + p, z1, wall);
            Block(x0, roofY, z0 + t, x0 + t, roofY + p, z1 - t, wall);
            Block(x1 - t, roofY, z0 + t, x1, roofY + p, z1 - t, wall);
        }
    }

    /// <summary>Shipping container. Open ones are a tunnel you can run (and shoot) through.</summary>
    [MapOp]
    public void Container(float x, float z, bool alongX, Vector3 color, float y = 0, bool open = false)
    {
        using var __ = Op([x, z, alongX, color, y, open]);
        float len = 6f, wid = 2.5f, h = 2.6f, t = 0.12f;
        float hx = (alongX ? len : wid) / 2, hz = (alongX ? wid : len) / 2;
        if (!open)
        {
            Block(x - hx, y, z - hz, x + hx, y + h, z + hz, color);
            return;
        }
        Block(x - hx, y + h - t, z - hz, x + hx, y + h, z + hz, color); // roof
        if (alongX)
        {
            Block(x - hx, y, z - hz, x + hx, y + h, z - hz + t, color);
            Block(x - hx, y, z + hz - t, x + hx, y + h, z + hz, color);
        }
        else
        {
            Block(x - hx, y, z - hz, x - hx + t, y + h, z + hz, color);
            Block(x + hx - t, y, z - hz, x + hx, y + h, z + hz, color);
        }
    }

    [MapOp]
    public void Car(float x, float z, bool alongX, Vector3 color)
    {
        using var __ = Op([x, z, alongX, color]);
        float l = 4.2f, w = 1.9f;
        Prop(x, z, alongX ? l : w, 0.9f, alongX ? w : l, color, 0.3f);
        Prop(x, z, alongX ? l * 0.55f : w * 0.9f, 0.7f, alongX ? w * 0.9f : l * 0.55f, color * 0.75f, 1.2f);
        Prop(x, z, alongX ? l * 0.9f : w * 0.8f, 0.3f, alongX ? w * 0.8f : l * 0.9f, new Vector3(0.1f, 0.1f, 0.1f)); // wheels
    }

    [MapOp]
    public void Spawn(float x, float z, float y = 0)
    {
        using var __ = Op([x, z, y]);
        var g = PointGround(x, z); // terrain maps: y is measured from the ground (unless the op's anchor is "world")
        Spawns.Add(new Vector3(x, y + g, z));
        if (y == 0)
            Waypoints.Add(new Vector3(x, g, z));
    }

    /// <summary>A point bots roam between, on the ground at (x, z) or <paramref name="y"/> above it (a bridge, a deck).</summary>
    [MapOp]
    public void Waypoint(float x, float z, float y = 0) 
    {
        using var __ = Op([x, z, y]);
        Waypoints.Add(new Vector3(x, PointGround(x, z) + y, z));
    }

    /// <summary>Would a standing player at <paramref name="feet"/> overlap a box, be under the terrain, or be outside the map?</summary>
    public bool IsBlocked(Vector3 feet)
    {
        const float r = World.PlayerRadius + 0.05f;
        if (MathF.Abs(feet.X) > HalfX - r || MathF.Abs(feet.Z) > HalfZ - r)
            return true;
        if (Boundary != null && !Boundary.Inside(feet.X, feet.Z, r))
            return true; // outside the map's outline (MapEdges.cs)
        if (HasTerrain && GroundHeight(feet.X, feet.Z, feet.Y) > feet.Y + 0.05f)
            return true;
        if (HasTerrain && TerrainCollision!.CeilingAbove(feet.X, feet.Z, feet.Y) < feet.Y + World.PlayerHeight - 0.05f)
            return true; // a cave's ceiling too low to stand under
        if (Gravity is { } field && field.UpAt(feet) is var up && up != Vector3.UnitY)
            return BlockedAlongUp(feet, up, r); // in a gravity zone with its own up (a wall you stand on): the body stands along that up
        if (_validateGrid is { } grid)
        {
            // Validate: only the boxes filed near the feet (the same test, and any order gives the same answer).
            var near = grid.Near(feet.X - r, feet.Z - r, feet.X + r, feet.Z + r);
            foreach (var i in near)
            {
                var b = Solid[i];
                if (feet.Y < b.Max.Y - 0.001f && feet.Y + World.PlayerHeight > b.Min.Y + 0.001f
                    && feet.X + r > b.Min.X && feet.X - r < b.Max.X && feet.Z + r > b.Min.Z && feet.Z - r < b.Max.Z)
                    return true;
            }
            return false;
        }
        foreach (var b in Solid)
        {
            if (feet.Y < b.Max.Y - 0.001f && feet.Y + World.PlayerHeight > b.Min.Y + 0.001f
                && feet.X + r > b.Min.X && feet.X - r < b.Max.X && feet.Z + r > b.Min.Z && feet.Z - r < b.Max.Z)
                return true;
        }
        return false;
    }

    // Set only while Validate runs: Solid filed into 4 m cells, so moving spawns and waypoints out of geometry costs
    // points x nearby boxes instead of points x every box (London: 1.5 s). Solid doesn't change during Validate.
    private SolidGrid? _validateGrid;

    /// <summary>Would a body standing along <paramref name="up"/> at <paramref name="feet"/> overlap a box? Spheres of radius <paramref name="r"/> along its axis.</summary>
    private bool BlockedAlongUp(Vector3 feet, Vector3 up, float r)
    {
        for (var h = r + 0.02f; h <= World.PlayerHeight - r; h += 0.2f)
        {
            var c = feet + up * h;
            foreach (var b in Solid)
            {
                var q = Vector3.Clamp(c, b.Min, b.Max);
                if (Vector3.DistanceSquared(q, c) < r * r)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The closest spot to <paramref name="feet"/> (same height, searching outwards in rings) where a standing
    /// player fits, or null if there's none within <paramref name="maxRadius"/>. On terrain, a spot on the ground
    /// follows the ground (and skips slopes too steep to stand on).
    /// </summary>
    public Vector3? NearestFree(Vector3 feet, float maxRadius = 12f)
    {
        if (!BadSpot(feet))
            return feet;
        var onGround = HasTerrain && feet.Y - GroundHeight(feet.X, feet.Z) < 0.3f;
        for (var radius = 0.5f; radius < maxRadius; radius += 0.5f)
        {
            for (var k = 0; k < 16; k++)
            {
                var a = k * MathF.Tau / 16;
                var c = feet + new Vector3(MathF.Cos(a) * radius, 0, MathF.Sin(a) * radius);
                if (HasTerrain)
                    c.Y = onGround ? GroundHeight(c.X, c.Z) : MathF.Max(c.Y, GroundHeight(c.X, c.Z));
                if (!BadSpot(c))
                    return c;
            }
        }
        return null;
    }

    /// <summary>Like <see cref="IsBlocked"/>, but building doors don't count (the nav graph ignores them: bots open doors).</summary>
    private bool BlockedButForDoors(Vector3 feet)
    {
        const float r = World.PlayerRadius + 0.05f;
        if (MathF.Abs(feet.X) > HalfX - r || MathF.Abs(feet.Z) > HalfZ - r || OnSteepGround(feet))
            return true;
        var all = _validateGrid == null;
        var near = _validateGrid?.Near(feet.X - r, feet.Z - r, feet.X + r, feet.Z + r);
        for (var n = 0; n < (all ? Solid.Count : near!.Count); n++)
        {
            var i = all ? n : near![n];
            var b = Solid[i];
            if (!IsBuildingDoorBox(i) && feet.Y < b.Max.Y - 0.001f && feet.Y + World.PlayerHeight > b.Min.Y + 0.001f
                && feet.X + r > b.Min.X && feet.X - r < b.Max.X && feet.Z + r > b.Min.Z && feet.Z - r < b.Max.Z)
                return true;
        }
        return false;
    }

    /// <summary>Moves any spawn or waypoint that ended up inside geometry to the nearest free spot.</summary>
    public void Validate()
    {
        if (PointGravity != null)
        {
            // A planet: its Surface* ops put spawns and waypoints on the ground; the flat-floor fixes and generated spawns
            // below don't apply (place one spawn per player with SurfaceSpawn).
            AuthoredSpawns = Spawns.Count;
            return;
        }
        SnapToGround(Spawns);
        SnapToGround(Waypoints);
        _validateGrid = new SolidGrid(Solid);
        try
        {
            Fix(Spawns, "spawn");
            Fix(Waypoints, "waypoint");
            EnsureSpawns(); // one spawn per player: generated extras when the map has too few (MapPlayers.cs)
        }
        finally
        {
            _validateGrid = null;
        }

        void Fix(List<Vector3> points, string what)
        {
            for (var i = 0; i < points.Count; i++)
            {
                if (!BadSpot(points[i]) || (Gravity != null && !PlainUpAtPoint(points[i])))
                    continue; // (a gravity zone with its own up: the flat checks don't apply; its nav nodes are probed along that up)
                // A building's own nav points (doorways, stair ends) are where the nav graph's stair links start and end, and
                // the nav ignores doors (bots open them): a shut door next to one must not move it away (that left stairs with
                // no link, so bots reached the floor above only over railings, or not at all).
                if (ReferenceEquals(points, Waypoints) && BuildingNavPoints.Contains(points[i]) && !BlockedButForDoors(points[i]))
                    continue;
                var original = points[i];
                var fixedPoint = NearestFree(original);
                Console.WriteLine($"Map '{Name}': {what} at ({original.X:0.#}, {original.Z:0.#}) was inside geometry, moved to "
                    + (fixedPoint is { } f ? $"({f.X:0.#}, {f.Z:0.#})" : "nowhere (removed)"));
                if (fixedPoint is { } ok)
                    points[i] = ok;
                else
                    points.RemoveAt(i--);
            }
        }
    }
}

/// <summary>What a <see cref="Maps.Reload"/> found, compared with the list before it.</summary>
public sealed record MapReload(int Count, string[] Added, string[] Removed, string[] Changed, string[] Errors)
{
    public bool IsChanged(string name) => Changed.Contains(name, StringComparer.OrdinalIgnoreCase);

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Added.Length > 0) parts.Add($"new: {string.Join(", ", Added)}");
            if (Changed.Length > 0) parts.Add($"changed: {string.Join(", ", Changed)}");
            if (Removed.Length > 0) parts.Add($"gone: {string.Join(", ", Removed)}");
            if (Errors.Length > 0) parts.Add($"{Errors.Length} file(s) with errors");
            return $"{Count} maps" + (parts.Count > 0 ? " - " + string.Join("; ", parts) : " - no changes");
        }
    }
}

public enum MapSize : byte
{
    Small,
    Medium,
    Large,
}

public static partial class Maps
{
    public static Map Get(byte id) => id < All.Length ? All[id] : All[0];

    public static Map? Find(string name) => All.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // Colours for the built-in fallback map.
    private static readonly Vector3 Concrete = new(0.42f, 0.42f, 0.44f);
    private static readonly Vector3 LightGrey = new(0.62f, 0.62f, 0.64f);
    private static readonly Vector3 Orange = new(0.95f, 0.50f, 0.15f);

    // Built on first use (not as a field initialiser), so the palette colours above are guaranteed to be
    // set first. A field initialiser here once turned half the maps black.
    private static Map[]? _all;
    private static MapPack[]? _packs;
    public static Map[] All => _all ??= LoadAll();

    /// <summary>Every map pack (one per top-level folder in Maps, plus "Other" for loose files), sorted by order then name.</summary>
    public static MapPack[] Packs
    {
        get
        {
            _ = All; // loads the maps (and with them the packs) on first use
            return _packs!;
        }
    }

    /// <summary>The pack with this name, or null.</summary>
    public static MapPack? FindPack(string name) => Packs.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Re-reads the Maps folder without restarting ("Refresh maps" in the lobby, "Reload map files" in the pause menu).
    /// The list is swapped in one go. Map objects already in use (the map being played) stay valid; they're
    /// just no longer in the list. Ids are positions in the new list, which is why votes and the network go by name.
    /// Packs are rebuilt too.
    /// </summary>
    public static MapReload Reload()
    {
        var old = _all ?? [];
        LoadErrors.Clear();
        Props.Reload(); // maps place props from the library, so it's re-read first
        Decals.Reload(); // and decals (Source/Decals)
        Buildings.Reload(); // and buildings from the Buildings folder
        MaterialDestruction.Reload(); // and how building materials break (Materials/destruction.json)
        MapPack.ForgetDestructibleDefaults();
        var fresh = LoadAll(reload: true); // built live (MapBake: cached for the next start)
        _all = fresh;
        Generation++;
        Map? Was(Map m) => old.FirstOrDefault(o => o.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase));
        return new MapReload(
            fresh.Length,
            fresh.Where(m => Was(m) == null).Select(m => m.Name).ToArray(),
            old.Where(o => !fresh.Any(m => m.Name.Equals(o.Name, StringComparison.OrdinalIgnoreCase))).Select(o => o.Name).ToArray(),
            fresh.Where(m => Was(m) is { } o && (MapFile.ToJson(o) != MapFile.ToJson(m) || o.PackAssetHash != m.PackAssetHash)).Select(m => m.Name).ToArray(),
            LoadErrors.ToArray());
    }

    /// <summary>Goes up by one on every <see cref="Reload"/>, so screens can tell the list changed.</summary>
    public static int Generation { get; private set; }

    /// <summary>Problems found while loading map files (shown in the log; bad files are skipped).</summary>
    public static readonly List<string> LoadErrors = new();

    /// <summary>The folder map files are loaded from (next to the game).</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Maps");

    /// <summary>
    /// Every map in the Maps folder, sorted by "order" then name, and the packs they belong to.
    /// The shipped maps are ordinary map files too (in Maps/Legacy and the other pack folders).
    /// Ids are positions in this list (local only: the network refers to maps by name + fingerprint).
    /// </summary>
    private static Map[] LoadAll(bool reload = false)
    {
        var (maps, packs) = LoadFolder(Folder, LoadErrors, ParallelLoad, reload ? BakeUse.WriteOnly : BakeUse.ReadWrite);
        if (maps.Length == 0)
        {
            // No map files (or none valid): fall back to the one map built into the code so the game still starts.
            Console.WriteLine($"[maps] no map files in {Folder}; using the built-in fallback map");
            var pack = new MapPack { Name = "Built-in", Description = "The Maps folder is missing or empty, so only the built-in test map is available." };
            foreach (var m in Fallback())
            {
                m.Id = 0;
                m.Validate();
                m.Pack = pack;
                pack.Maps.Add(m);
            }
            maps = pack.Maps.ToArray();
            packs = [pack];
        }
        _packs = packs;
        Console.WriteLine($"[maps] {maps.Length} map(s) in {packs.Length} pack(s): "
            + string.Join("; ", packs.Select(p => $"{p.Name}: {string.Join(", ", p.Maps.Select(m => m.Name))}")));
        return maps;
    }

    /// <summary>
    /// Loads a maps folder: each top-level subfolder is a pack (all its *.json files, nested folders included,
    /// except pack.json, which describes the pack), and *.json files directly in the folder form the "Other" pack.
    /// Map names must be unique across all packs. Bad files are skipped and described in <paramref name="errors"/>.
    /// Returns the maps sorted by order then name (ids assigned, validated; at most 255) and the non-empty packs.
    /// </summary>
    public static (Map[] Maps, MapPack[] Packs) LoadFolder(string folder, List<string> errors) => LoadFolder(folder, errors, ParallelLoad);

    /// <summary>Build the map files of <see cref="LoadFolder(string, List{string})"/> on worker threads (off: one by one,
    /// as before; the "Big maps" self-test compares the two).</summary>
    public static bool ParallelLoad = true;

    /// <summary><see cref="LoadFolder(string, List{string})"/>, the files built on worker threads or one after another.</summary>
    public static (Map[] Maps, MapPack[] Packs) LoadFolder(string folder, List<string> errors, bool parallel) => LoadFolder(folder, errors, parallel, BakeUse.None);

    /// <summary>What a load does with baked maps (MapBake.cs): nothing (tests, tools), read and write (start-up), or only
    /// write the cache ("Refresh maps": the files just changed, so they're built live).</summary>
    public enum BakeUse : byte
    {
        None,
        ReadWrite,
        WriteOnly,
    }

    /// <summary>Is <paramref name="file"/> a map file of the maps folder <paramref name="folder"/> (what <see cref="LoadFolder(string, List{string})"/> loads)?</summary>
    public static bool IsMapFile(string folder, string file)
    {
        if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(file).Equals(MapPack.FileName, StringComparison.OrdinalIgnoreCase))
            return false;
        var rel = Path.GetRelativePath(folder, file).Replace('\\', '/');
        var slash = rel.IndexOf('/');
        if (slash < 0)
            return true; // a loose map
        return !rel.StartsWith('.') && !InPackAssetFolder(Path.Combine(folder, rel[..slash]), file);
    }

    /// <summary><see cref="LoadFolder(string, List{string}, bool)"/>, with baked maps used as <paramref name="bakes"/> says.</summary>
    public static (Map[] Maps, MapPack[] Packs) LoadFolder(string folder, List<string> errors, bool parallel, BakeUse bakes)
    {
        var read = bakes == BakeUse.ReadWrite && MapBake.Enabled;
        var cache = bakes != BakeUse.None && MapBake.CacheWrites;
        var bakeOut = bakes != BakeUse.None ? MapBake.BakeOutFolder : null;
        var keyed = read || cache || bakeOut != null;
        var tStart = System.Diagnostics.Stopwatch.GetTimestamp();
        if (keyed)
            lock (MapBake.Unbakeable)
                MapBake.Unbakeable.Clear();
        var maps = new List<Map>();
        var packs = new List<MapPack>();
        var byJob = new Dictionary<Map, LoadJob>(ReferenceEqualityComparer.Instance);
        if (!Directory.Exists(folder))
            return ([], []);
        static bool IsMapFile(string f) => !Path.GetFileName(f).Equals(MapPack.FileName, StringComparison.OrdinalIgnoreCase);
        var jobs = new List<LoadJob>();
        void LoadInto(MapPack pack, IEnumerable<string> files, bool loose, string packDir)
        {
            foreach (var file in files.Where(IsMapFile).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                jobs.Add(new LoadJob(pack, file, Path.GetRelativePath(folder, file), loose, packDir));
            packs.Add(pack);
        }

        // Pack folders first, so a stray loose duplicate never pushes a pack's own map out; retired pack folders last,
        // so an old copy of Maps/Classic never pushes Legacy's maps out either.
        foreach (var dir in Directory.GetDirectories(folder).OrderBy(d => MapPack.IsRetiredFolder(Path.GetFileName(d)) ? 1 : 0).ThenBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('.'))
                continue; // .git and friends
            var pack = new MapPack { Name = name, Folder = name };
            var meta = Path.Combine(dir, MapPack.FileName);
            if (File.Exists(meta))
            {
                try
                {
                    pack = MapPack.Load(File.ReadAllText(meta), name, Path.Combine(name, MapPack.FileName));
                }
                catch (Exception ex) when (ex is MapFileException or IOException)
                {
                    errors.Add(ex.Message);
                    Console.WriteLine($"[maps] pack info skipped: {ex.Message}");
                }
            }
            if (packs.Any(p => p.Name.Equals(pack.Name, StringComparison.OrdinalIgnoreCase)))
                pack.Name = $"{pack.Name} ({name})"; // two folders claiming the same pack name: keep them apart
            LoadInto(pack, Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories).Where(f => !InPackAssetFolder(dir, f)), false, dir);
        }
        LoadInto(new MapPack { Name = MapPack.LooseName, Description = "Map files placed directly in the Maps folder.", Order = int.MaxValue },
            Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly), true, "");

        var tFiles = System.Diagnostics.Stopwatch.GetTimestamp();
        // Build every file (in parallel: each map is built alone from its own text; the shared libraries are loaded
        // first and only read after that), then take the results in file order, exactly as one-by-one loading did.
        byte[]? inputs = null;
        if (keyed)
            inputs = MapBake.InputsHash(folder); // (everything else a map reads while it is built: one key part for every map)
        var inputsMs = System.Diagnostics.Stopwatch.GetElapsedTime(tStart).TotalMilliseconds;
        RunJobs(jobs, parallel, j =>
        {
            var t = System.Diagnostics.Stopwatch.GetTimestamp();
            var text = File.ReadAllText(j.File);
            var packDir = j.PackDir.Length > 0 ? j.PackDir : null;
            if (inputs != null)
            {
                j.Key = MapBake.KeyOf(j.Rel, System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)), inputs);
                if (read && MapBake.TryLoad(j.Rel, j.Key, packDir, out var loadLog, out var validateLog, out var how) is { } baked)
                {
                    Console.Write(loadLog); // (what building it wrote, as if it had been built)
                    j.Map = baked;
                    j.ValidateLog = validateLog;
                    j.From = how;
                    j.Map.LoadMs = System.Diagnostics.Stopwatch.GetElapsedTime(t).TotalMilliseconds;
                    return;
                }
            }
            j.Map = MapFile.Load(text, j.Rel, j.PackDir); // packDir: the pack's own props and models come first
            j.Map.LoadMs = System.Diagnostics.Stopwatch.GetElapsedTime(t).TotalMilliseconds;
        }, warm: true, route: keyed);
        var tBuilt = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (var j in jobs)
        {
            if (j.Log.Length > 0)
                Console.Write(j.Log);
            j.LoadLog = j.Log;
            j.Log = "";
            try
            {
                if (j.Error != null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(j.Error).Throw(); // as if it had happened here
                var m = j.Map!;
                if (maps.FirstOrDefault(x => x.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase)) is { } other)
                {
                    // A loose copy identical to a map in a pack, or the same map in a retired pack folder (Classic,
                    // CollisionPoint, HighGround: their maps moved to Legacy): most likely files left over from
                    // before the maps moved. Updates unzip into a new folder, so only a build unzipped over an older one or a reused dev
                    // bin folder (dotnet build never deletes outputs) has them; keep this. Skip it quietly.
                    if ((j.Loose || MapPack.IsRetiredFolder(j.Pack.Folder)) && MapFile.ToJson(other) == MapFile.ToJson(m))
                    {
                        Console.WriteLine($"[maps] ignored {j.Rel}: same map as {other.SourceFile}");
                        continue;
                    }
                    throw new MapFileException($"{j.Rel}: another map is already called '{m.Name}' ({other.SourceFile})");
                }
                m.Pack = j.Pack;
                maps.Add(m);
                byJob[m] = j;
            }
            catch (Exception ex) when (ex is MapFileException or IOException)
            {
                errors.Add(ex.Message);
                Console.WriteLine($"[maps] skipped: {ex.Message}");
            }
        }

        var sorted = maps.OrderBy(m => m.Order).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (sorted.Length > 255)
        {
            errors.Add($"{sorted.Length} maps found; only the first 255 are used");
            sorted = sorted[..255];
        }
        for (var i = 0; i < sorted.Length; i++)
            sorted[i].Id = (byte)i;
        // Validate each map (it only moves that map's own spawns and waypoints), in parallel too; the log in list order.
        var checks = sorted.Select(m => new LoadJob(null!, "", m.SourceFile ?? m.Name, false, "") { Map = m, Built = byJob.GetValueOrDefault(m) }).ToList();
        var tValidate = System.Diagnostics.Stopwatch.GetTimestamp();
        // Library indexes for writing bakes, one per pack folder, made once every map is built (flag variants and models
        // are added to the libraries while maps build).
        var indexes = new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<MapGraph.MapLibraryIndex>>(StringComparer.OrdinalIgnoreCase);
        RunJobs(checks, parallel, j =>
        {
            var t = System.Diagnostics.Stopwatch.GetTimestamp();
            if (j.Built is { From: not null } b)
            {
                Console.Write(b.ValidateLog); // a baked map was validated before it was baked
                j.Map!.ValidateMs = System.Diagnostics.Stopwatch.GetElapsedTime(t).TotalMilliseconds;
                return;
            }
            j.Map!.Validate();
            j.Map.ValidateMs = System.Diagnostics.Stopwatch.GetElapsedTime(t).TotalMilliseconds;
            if (j.Built is not { Key: { } key } built || !(cache || bakeOut != null))
                return;
            var toCache = cache && (read || !MapBake.CacheHas(built.Rel, key)); // ("Refresh maps": an unchanged map is cached already)
            if (!toCache && bakeOut == null)
                return;
            try
            {
                var index = indexes.GetOrAdd(built.PackDir, d => new Lazy<MapGraph.MapLibraryIndex>(() => MapBake.IndexFor(d.Length > 0 ? d : null))).Value;
                var file = MapBake.Write(j.Map, built.Rel, key, built.LoadLog, JobConsole.Current?.ToString() ?? "", index);
                if (bakeOut != null)
                {
                    Directory.CreateDirectory(bakeOut);
                    File.WriteAllBytes(Path.Combine(bakeOut, MapBake.FileName(built.Rel)), file);
                }
                if (toCache)
                    MapBake.WriteCache(built.Rel, file);
            }
            catch (Exception ex) // (whatever goes wrong baking a map, it has loaded: it is simply not cached)
            {
                lock (MapBake.Unbakeable)
                    MapBake.Unbakeable.Add($"{built.Rel}: {(ex is MapBakeException ? "" : ex.GetType().Name + ": ")}{ex.Message}");
            }
        }, warm: false, route: keyed);
        var tEnd = System.Diagnostics.Stopwatch.GetTimestamp();
        LastLoadTimes = (System.Diagnostics.Stopwatch.GetElapsedTime(tFiles, tBuilt).TotalMilliseconds - _warmMs, _warmMs,
            System.Diagnostics.Stopwatch.GetElapsedTime(tValidate, tEnd).TotalMilliseconds);
        foreach (var j in checks)
        {
            if (j.Log.Length > 0)
                Console.Write(j.Log);
            if (j.Error != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(j.Error).Throw();
        }
        if (keyed)
        {
            int baked = 0, cached = 0;
            foreach (var j in checks)
            {
                if (j.Built?.From == "baked")
                    baked++;
                else if (j.Built?.From == "cached")
                    cached++;
            }
            Interlocked.Add(ref MapBake.Baked, baked);
            Interlocked.Add(ref MapBake.Cached, cached);
            Interlocked.Add(ref MapBake.Built, sorted.Length - baked - cached);
            var summary = $"{sorted.Length} maps: {baked} baked, {cached} cached, {sorted.Length - baked - cached} built, in {System.Diagnostics.Stopwatch.GetElapsedTime(tStart).TotalMilliseconds:0} ms "
                + $"(inputs key {inputsMs:0} ms)";
            MapBake.NoteSummary(summary);
            Console.WriteLine("[maps] " + summary);
            lock (MapBake.Unbakeable)
                foreach (var u in MapBake.Unbakeable)
                    Console.WriteLine($"[maps] not baked: {u}");
        }
        foreach (var p in packs)
        {
            p.Maps.Clear();
            p.Maps.AddRange(sorted.Where(m => m.Pack == p));
        }
        var usedPacks = packs.Where(p => p.Maps.Count > 0).OrderBy(p => p.Order).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        return (sorted, usedPacks);
    }

    /// <summary>The last <see cref="LoadFolder(string, List{string}, bool)"/>: ms building the map files, loading the shared
    /// libraries first (parallel loads only; otherwise inside the build), validating.</summary>
    public static (double BuildMs, double LibrariesMs, double ValidateMs) LastLoadTimes { get; private set; }

    private static double _warmMs;

    private sealed class LoadJob(MapPack pack, string file, string rel, bool loose, string packDir)
    {
        public readonly MapPack Pack = pack;
        public readonly string File = file, Rel = rel, PackDir = packDir;
        public readonly bool Loose = loose;
        public Map? Map;
        public Exception? Error;
        public byte[]? Key; // the map's bake key (MapBake.cs; null: no bakes this load)
        public string? From; // "baked" or "cached": read from that file instead of being built
        public string LoadLog = "", ValidateLog = "";
        public LoadJob? Built; // (a validation job: the job that built the map)
        public string Log = ""; // what the job wrote to the console, printed in order afterwards
    }

    /// <summary>
    /// Runs <paramref name="work"/> for every job, on worker threads when <paramref name="parallel"/>, each job's console
    /// output kept in <see cref="LoadJob.Log"/> (so the log reads the same as a one-by-one load). With <paramref name="warm"/>
    /// the shared libraries every map reads (props, decals, buildings, materials, flags, the packs' own folders) are
    /// loaded first, on this thread.
    /// </summary>
    private static void RunJobs(List<LoadJob> jobs, bool parallel, Action<LoadJob> work, bool warm, bool route = false)
    {
        void Run(LoadJob j)
        {
            try
            {
                work(j);
            }
            catch (Exception ex)
            {
                j.Error = ex; // rethrown (or reported) by the caller, in order
            }
        }
        if (warm)
            _warmMs = 0;
        var serial = !parallel || jobs.Count < 2 || Environment.ProcessorCount < 2;
        if (serial && !route)
        {
            foreach (var j in jobs)
                Run(j);
            return;
        }
        if (warm)
        {
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            _ = Props.All;
            _ = Decals.All;
            _ = Buildings.All;
            _ = MaterialDestruction.Current;
            _ = FlagPool.Designs;
            foreach (var dir in jobs.Select(j => j.PackDir).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _ = Props.ForPack(dir.Length > 0 ? dir : null);
                _ = Decals.ForPack(dir.Length > 0 ? dir : null);
                _ = MapPack.DestructibleDefault(dir.Length > 0 ? dir : null);
            }
            _warmMs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        }
        var console = Console.Out;
        var router = new JobConsole(console);
        Console.SetOut(router);
        try
        {
            // Biggest files first, so one large map doesn't start last and leave the other cores idle.
            var order = Enumerable.Range(0, jobs.Count).OrderByDescending(i => jobs[i].File.Length > 0 ? new FileInfo(jobs[i].File).Length : jobs[i].Map?.Solid.Count ?? 0).ThenBy(i => i).ToArray();
            System.Threading.Tasks.Parallel.ForEach(order, new ParallelOptions { MaxDegreeOfParallelism = serial ? 1 : Math.Min(Environment.ProcessorCount, 8) }, i =>
            {
                var j = jobs[i];
                var sb = new StringWriter();
                JobConsole.Current = sb;
                try
                {
                    Run(j);
                }
                finally
                {
                    JobConsole.Current = null;
                    j.Log = sb.ToString();
                }
            });
        }
        finally
        {
            Console.SetOut(console);
        }
    }

    /// <summary>The console while maps load in parallel: each worker's lines go to its job's buffer, everyone else's straight through.</summary>
    private sealed class JobConsole(TextWriter inner) : TextWriter
    {
        [ThreadStatic] public static StringWriter? Current;

        public override System.Text.Encoding Encoding => inner.Encoding;

        private TextWriter Target => (TextWriter?)Current ?? inner;

        public override void Write(char value) => Target.Write(value);

        public override void Write(string? value) => Target.Write(value);

        public override void Write(char[] buffer, int index, int count) => Target.Write(buffer, index, count);

        public override void WriteLine(string? value) => Target.WriteLine(value);

        public override void WriteLine() => Target.WriteLine();

        public override void Flush() => Target.Flush();
    }

    /// <summary>
    /// Is <paramref name="file"/> inside the pack's own Props or Decals folder (Maps/&lt;Pack&gt;/Props, Maps/&lt;Pack&gt;/Decals)?
    /// Those are prop and decal files, not maps. (Models hold no .json files.)
    /// </summary>
    private static bool InPackAssetFolder(string packDir, string file)
    {
        var rel = Path.GetRelativePath(packDir, file).Replace('\\', '/');
        return rel.StartsWith(Props.PackFolderName + "/", StringComparison.OrdinalIgnoreCase)
               || rel.StartsWith(Decals.PackFolderName + "/", StringComparison.OrdinalIgnoreCase)
               || rel.StartsWith(Missions.PackFolderName + "/", StringComparison.OrdinalIgnoreCase); // co-op missions (Source/Game/Pve/Mission.cs)
    }

    /// <summary>The only map defined in code: used when the Maps folder is missing or empty.</summary>
    public static Map[] Fallback()
    {
        var m = ProtoBox();
        m.Order = 1;
        return [m];
    }

    /// <summary>
    /// "CallOfSlop.exe --export-maps DIR": writes every loaded map back out in the standard layout
    /// (handy for tidying a hand-edited file, or as a starting point for a new map).
    /// </summary>
    public static int Export(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var m in All)
        {
            var slug = new string(m.Name.Where(char.IsLetterOrDigit).ToArray());
            var packDir = Path.Combine(dir, m.Pack?.Folder ?? ""); // keep the pack folders
            Directory.CreateDirectory(packDir);
            File.WriteAllText(Path.Combine(packDir, $"{m.Order:00}-{slug}.json"), MapFile.ToJson(m));
        }
        Console.WriteLine($"[maps] wrote {All.Length} map file(s) to {Path.GetFullPath(dir)}");
        return 0;
    }

    /// <summary>
    /// "CallOfSlop.exe --check-map FILE": loads one map file, reports problems and runs the "Map contract" checks on it
    /// (SelfTestMapContract.cs: what main's full self-test would find, in seconds), for map makers. Prints to the
    /// terminal and to callofslop.log. Exit code 0 = the map loads and every check passes; otherwise the number of
    /// problems (the reasons are printed).
    /// </summary>
    public static int Check(string file)
    {
        using var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        var output = new TeeWriter(stdout, Console.Out);
        Brutal.GlfwApi.Glfw.Init(); // only for the clock (the bot match's game), as the self-test does; no window
        try
        {
            return CheckOne(file, output);
        }
        finally
        {
            Brutal.GlfwApi.Glfw.Terminate();
        }
    }

    /// <summary>"CallOfSlop.exe --check-maps DIR": --check-map for every map file in a folder (a pack), with a tally at the end.</summary>
    public static int CheckAll(string dir)
    {
        using var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        var output = new TeeWriter(stdout, Console.Out);
        if (!Directory.Exists(dir))
        {
            output.WriteLine($"ERROR: no folder {dir}");
            return 1;
        }
        var files = Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Equals(MapPack.FileName, StringComparison.OrdinalIgnoreCase) && !InPackAssetFolder(dir, f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        Brutal.GlfwApi.Glfw.Init();
        var problems = 0;
        var failedFiles = new List<string>();
        try
        {
            foreach (var f in files)
            {
                output.WriteLine($"== {Path.GetRelativePath(dir, f)}");
                var n = CheckOne(f, output);
                problems += n;
                if (n > 0)
                    failedFiles.Add(Path.GetFileName(f));
                output.WriteLine();
            }
        }
        finally
        {
            Brutal.GlfwApi.Glfw.Terminate();
        }
        output.WriteLine($"{files.Length} map files checked, {problems} problem(s){(failedFiles.Count > 0 ? " in " + string.Join(", ", failedFiles) : "")}");
        return problems;
    }

    private static int CheckOne(string file, TextWriter output)
    {
        Map m;
        try
        {
            // A file inside the Maps folder keeps its pack (its pack's own props, decals, models and skies); so does one in a
            // pack folder of another Maps folder (the source tree's Maps/<Pack>/x.json); anything else stands alone.
            var full = Path.GetFullPath(file);
            var rel = Path.GetRelativePath(Folder, full);
            var inMaps = !rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel);
            var packDir = inMaps ? null : "";
            var source = inMaps ? rel : Path.GetFileName(file);
            if (!inMaps && Path.GetDirectoryName(full) is { } dir && Path.GetDirectoryName(dir) is { } mapsDir
                && Path.GetFileName(mapsDir).Equals("Maps", StringComparison.OrdinalIgnoreCase))
            {
                packDir = dir;
                source = Path.GetFileName(dir) + "/" + Path.GetFileName(file);
            }
            m = MapFile.Load(File.ReadAllText(file), source, packDir);
            var spawnsBefore = m.Spawns.ToList();
            m.Validate();
            var moved = spawnsBefore.Where((p, i) => p != m.Spawns[i]).Count();
            output.WriteLine($"OK: '{m.Name}' ({m.Size}, {m.HalfX * 2:0} x {m.HalfZ * 2:0} m): {m.Solid.Count} boxes, {m.Spawns.Count} spawns, {m.Waypoints.Count} waypoints, fingerprint {m.ContentHash:x8}");
            if (m.Boundary is { } bd)
                output.WriteLine($"boundary: {bd.Spec.Shape.ToString().ToLowerInvariant()}, {bd.SegmentCount} segments, {bd.Area:0} square metres inside, "
                                 + $"{bd.Perimeter:0} m round, edge {bd.Spec.Edge.Style.ToString().ToLowerInvariant()}");
            if (m.Terrain is { } t)
                output.WriteLine($"terrain: {t.CountX} x {t.CountZ} samples every {t.Cell:0.##} m, heights {t.MinHeight:0.0} to {t.MaxHeight:0.0} m, {m.TerrainSpec!.Features.Count} features, "
                                 + $"{t.MaterialIds.Count(b => b == (byte)TerrainMaterial.Rock) * 100f / t.MaterialIds.Length:0}% rock");
            if (moved > 0)
                output.WriteLine($"note: {moved} spawn(s) were inside geometry and get moved to the nearest free spot");
            if (m.GeneratedSpawns > 0 || m.MaxPlayers < m.WantedMaxPlayers)
                output.WriteLine($"note: {m.AuthoredSpawns} spawns for up to {m.WantedMaxPlayers} players: {m.GeneratedSpawns} generated, limit {m.MaxPlayers} (add Spawn ops to place them yourself)");
            if (m.Waypoints.Count == 0)
                output.WriteLine("note: no waypoints; bots will wander. Add \"Waypoint\" ops or { \"op\": \"AutoWaypoints\", \"spacing\": 8 }");
        }
        catch (Exception ex) when (ex is MapFileException or IOException)
        {
            output.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
        // The contract checks need the map in the loaded list (the bot match starts a game by map id): take the slot of
        // the loaded map with the same name, or a new one.
        Adopt(m);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        output.WriteLine($"contract checks{(m.TestSpec == null ? " (universal only)" : "")}:");
        var failures = SelfTest.CheckMapContractTool(m, output);
        output.WriteLine(failures == 0 ? $"PASS: every check passed ({sw.Elapsed.TotalSeconds:0.0} s)" : $"FAILED: {failures} check(s) failed ({sw.Elapsed.TotalSeconds:0.0} s)");
        return failures;
    }

    /// <summary>Puts a map loaded by a tool into the list (replacing a loaded map of the same name), so a game can be started on it.</summary>
    private static void Adopt(Map m)
    {
        var all = All;
        var slot = Array.FindIndex(all, x => x.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase));
        if (slot >= 0)
        {
            m.Id = (byte)slot;
            m.Pack = all[slot].Pack;
            all[slot] = m;
        }
        else if (all.Length < 255)
        {
            m.Id = (byte)all.Length;
            _all = [.. all, m];
        }
    }

    /// <summary>Writes to two writers (the terminal and callofslop.log).</summary>
    private sealed class TeeWriter(TextWriter a, TextWriter b) : TextWriter
    {
        public override System.Text.Encoding Encoding => a.Encoding;
        public override void Write(char value) { a.Write(value); b.Write(value); }
        public override void Write(string? value) { a.Write(value); b.Write(value); }
        public override void WriteLine(string? value) { a.WriteLine(value); b.WriteLine(value); }
        public override void WriteLine() { a.WriteLine(); b.WriteLine(); }
    }

    /// <summary>"CallOfSlop.exe --map-ops": prints every op a map file can use, with its parameters.</summary>
    public static int PrintOps()
    {
        foreach (var sig in MapFile.BuilderSignatures())
            Console.WriteLine(sig);
        return 0;
    }

    // 1 ---------------------------------------------------------------------------------
    private static Map ProtoBox()
    {
        var m = new Map
        {
            Name = "Proto Box",
            PrototypeGrid = true,
            Description = "The original 40 m dev box. Nine crates, nowhere to hide.",
            HalfX = 20,
            HalfZ = 20,
        };
        m.Ground(Concrete);
        m.Perimeter(6, LightGrey);
        m.Prop(0, 0, 4, 2, 4, Orange);
        foreach (var (x, z) in new[] { (-9, -9), (9, -9), (-9, 9), (9, 9) })
            m.Prop(x, z, 2, 2.5f, 2, Orange);
        m.Prop(0, -12, 8, 1.2f, 1, Orange);
        m.Prop(0, 12, 8, 1.2f, 1, Orange);
        m.Prop(-12, 0, 1, 1.2f, 8, Orange);
        m.Prop(12, 0, 1, 1.2f, 8, Orange);
        foreach (var (x, z) in new[] { (-16, -16), (16, -16), (-16, 16), (16, 16), (0, -17), (0, 17), (-17, 0), (17, 0) })
            m.Spawn(x, z);
        m.Waypoint(0, -6);
        m.Waypoint(6, 0);
        return m;
    }
}

/// <summary>
/// The indices of a box list filed into 4 m columns (a snapshot: the list mustn't change while it's used). <see cref="Near"/>
/// gives every box whose footprint may touch a region (a superset, each once); callers apply their exact test.
/// </summary>
internal sealed class SolidGrid
{
    private const float Cell = 4f, Margin = 0.05f;
    private readonly Dictionary<long, List<int>> _cells = new();
    private readonly List<int> _huge = new();
    private readonly List<int> _near = new();
    private int[] _seen;
    private int _stamp;

    public SolidGrid(List<Box> boxes)
    {
        _seen = new int[boxes.Count];
        for (var i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            int x0 = C(b.Min.X - Margin), x1 = C(b.Max.X + Margin), z0 = C(b.Min.Z - Margin), z1 = C(b.Max.Z + Margin);
            if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > 400)
            {
                _huge.Add(i); // the ground slab and other giants: checked every time, there are few
                continue;
            }
            for (var z = z0; z <= z1; z++)
            for (var x = x0; x <= x1; x++)
            {
                if (!_cells.TryGetValue(Key(x, z), out var l))
                    _cells[Key(x, z)] = l = new List<int>(4);
                l.Add(i);
            }
        }
    }

    private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

    private static int C(float v) => (int)MathF.Floor(v / Cell);

    /// <summary>The boxes near (x0, z0)..(x1, z1); the list is reused by the next call.</summary>
    public List<int> Near(float x0, float z0, float x1, float z1)
    {
        _near.Clear();
        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_seen);
            _stamp = 1;
        }
        foreach (var i in _huge)
            Take(i);
        int cx0 = C(x0 - Margin), cx1 = C(x1 + Margin), cz0 = C(z0 - Margin), cz1 = C(z1 + Margin);
        for (var z = cz0; z <= cz1; z++)
        for (var x = cx0; x <= cx1; x++)
            if (_cells.TryGetValue(Key(x, z), out var l))
                foreach (var i in l)
                    Take(i);
        return _near;
    }

    private void Take(int i)
    {
        if (_seen[i] == _stamp)
            return;
        _seen[i] = _stamp;
        _near.Add(i);
    }
}
