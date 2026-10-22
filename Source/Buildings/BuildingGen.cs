using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Turns a <see cref="BuildingDef"/> into a <see cref="BuildingPlan"/> for one placement: walls with their openings cut,
/// floors, ceilings, stairs and railings, the roof, porches and balconies, doors and window glass, waypoints for the bots,
/// and the furniture (<see cref="BuildingGenerator"/>'s furnishing half). Pure and deterministic: the same building,
/// variant, seed and placement give the same plan on every PC.
///
/// Conventions (building coordinates, metres): +X east, +Z south (the map's), y absolute. Room rects are measured to the
/// wall centre lines; walls are <see cref="BuildingDef.WallThickness"/> thick, centred on the rect edges, so the inside of
/// a room is its rect shrunk by half the thickness. The ground floor's floor is 0.1 m above the base, every floor slab and
/// the roof slab are 0.2 m thick. The building is built in its own frame, optionally mirrored (x to -x) and turned by
/// quarter turns about its origin, then moved to the op's "at".
/// </summary>
public static class BuildingGen
{
    public const float Slab = 0.2f;
    public const float GroundSlab = 0.1f;
    public const float LiningT = 0.02f;
    public const float StepRiseMax = 0.36f;
    public const float DoorClearDepth = 1.25f;

    /// <summary>Default door health by material: about 5 rifle hits for wood, a magazine for metal, two for glass.</summary>
    public static int DoorHealth(DoorMaterial m) => m switch { DoorMaterial.Metal => 400, DoorMaterial.Glass => 60, _ => 150 };

    /// <summary>
    /// Builds the plan. <paramref name="origin"/> is the building's origin in the world (its y is the ground the building
    /// stands on); <paramref name="quarter"/> is the turn in quarter turns (0-3, clockwise seen from above, like a prop's yaw).
    /// </summary>
    public static BuildingPlan Generate(BuildingDef def, BuildingVariant? variant, int seed, Vector3 origin, int quarter, PropScope scope) =>
        new BuildingGenerator(def, variant ?? BuildingVariant.Default, seed, origin, quarter, scope).Run();
}

/// <summary>The working state of one <see cref="BuildingGen.Generate"/> call (geometry half; furnishing is in BuildingFurnish.cs).</summary>
internal sealed partial class BuildingGenerator
{
    private const float Slab = BuildingGen.Slab;

    private readonly BuildingDef _def;
    private readonly BuildingVariant _variant;
    private readonly int _seed;
    private readonly Vector3 _origin;
    private readonly int _q;
    private readonly bool _mirror;
    private readonly PropScope _scope;
    private readonly Dictionary<string, Vector3> _palette;
    private readonly float _t;
    private readonly BuildingPlan _plan;
    private readonly List<BFloor> _floors = new();
    private readonly List<int> _floorOrig = new();
    private readonly List<(BStair Stair, int Floor)> _stairs = new();
    private float[] _level = [];
    private List<Seg>[] _segs = [];
    private readonly List<Attached> _openings = new();
    private List<BRect>[] _holesIn = []; // stair holes in the slab under floor f (index n: the roof slab)
    private List<BRect>[] _reserved = []; // stair areas furniture keeps off, per floor
    private RoofKind _roof;
    private uint _rng;

    /// <summary>A stretch of wall on one floor: along X (at z = C) or along Z (at x = C), from A to B, with the rooms on each side.</summary>
    internal sealed class Seg
    {
        public bool AlongX;
        public float C, A, B;
        public int Neg = -1, Pos = -1; // room index on the low-coordinate side and the high side (-1: outside)
        public readonly List<(float A, float B, float Y0, float Y1)> Holes = new(); // along, and heights above the floor
        public bool Exterior => Neg < 0 || Pos < 0;
    }

    /// <summary>An opening fixed to its wall.</summary>
    internal sealed record Attached(BOpening O, Seg S, int Floor, float Along, float Bottom, float Top);

    public BuildingGenerator(BuildingDef def, BuildingVariant variant, int seed, Vector3 origin, int quarter, PropScope scope)
    {
        _def = def;
        _variant = variant;
        _seed = seed != 0 ? seed : def.Seed;
        _origin = origin;
        _q = ((quarter % 4) + 4) % 4;
        _mirror = variant.Mirror;
        _scope = scope;
        _palette = def.Palette(variant.Palette);
        if (variant.Palette != null && !def.Palettes.Any(p => p.Name.Equals(variant.Palette, StringComparison.OrdinalIgnoreCase)))
            throw new BuildingFileException($"building \"{def.Name}\" has no palette \"{variant.Palette}\" (it has: {(def.Palettes.Count == 0 ? "none" : string.Join(", ", def.Palettes.Select(p => p.Name)))})");
        _t = def.WallThickness;
        _plan = new BuildingPlan { Def = def, BaseY = origin.Y };
        _rng = Foliage.Hash((uint)_seed * 2654435761u ^ 0xb11d1u);
    }

    private float Rand()
    {
        _rng = Foliage.Hash(_rng + 0x9e3779b9u);
        return (_rng >> 8) * (1f / 16777216f);
    }

    // ---- Frame ---------------------------------------------------------------------------------------------

    /// <summary>Building coordinates to world (y is already absolute).</summary>
    private Vector3 P(float x, float y, float z)
    {
        if (_mirror)
            x = -x;
        var (wx, wz) = _q switch { 0 => (x, z), 1 => (z, -x), 2 => (-x, -z), _ => (-z, x) };
        return new Vector3(_origin.X + wx, y, _origin.Z + wz);
    }

    /// <summary>A direction on the ground plan to world.</summary>
    private Vector2 V(float dx, float dz)
    {
        if (_mirror)
            dx = -dx;
        var (wx, wz) = _q switch { 0 => (dx, dz), 1 => (dz, -dx), 2 => (-dx, -dz), _ => (-dz, dx) };
        return new Vector2(wx, wz);
    }

    /// <summary>A yaw in the building's frame (degrees) to a world yaw (radians).</summary>
    private float YawW(float deg) => ((_mirror ? -deg : deg) + _q * 90f) * (MathF.PI / 180f);

    private Box BoxW(float x0, float y0, float z0, float x1, float y1, float z1)
    {
        var a = P(x0, y0, z0);
        var b = P(x1, y1, z1);
        var min = Vector3.Min(a, b);
        var max = Vector3.Max(a, b);
        return new Box((min + max) / 2, (max - min) / 2);
    }

    private void Add(float x0, float y0, float z0, float x1, float y1, float z1, BSurface s)
    {
        if (x1 - x0 < 0.005f || y1 - y0 < 0.005f || z1 - z0 < 0.005f)
            return;
        _plan.Shell.Add(new BuildingBox(BoxW(x0, y0, z0, x1, y1, z1), s.Color.Resolve(_palette), s.Layer));
    }

    /// <summary>A box spanning [a, b] along a wall's axis and [c0, c1] across it.</summary>
    private void AddAlong(bool alongX, float a, float b, float c0, float c1, float y0, float y1, BSurface s)
    {
        if (alongX)
            Add(a, y0, c0, b, y1, c1, s);
        else
            Add(c0, y0, a, c1, y1, b, s);
    }

    private Vector3 Col(ColorRef c) => c.Resolve(_palette);

    // ---- The whole build ----------------------------------------------------------------------------------

    public BuildingPlan Run()
    {
        ExpandFloors();
        _roof = _variant.Roof ?? _def.Roof.Kind;
        var n = _floors.Count;
        _level = new float[n + 1];
        _level[0] = _origin.Y + BuildingGen.GroundSlab;
        for (var f = 0; f < n; f++)
            _level[f + 1] = _level[f] + (_floors[f].Height > 0 ? _floors[f].Height : _def.StoreyHeight);
        _plan.Floors = n;
        _plan.RoofY = _level[n];
        _holesIn = Enumerable.Range(0, n + 1).Select(_ => new List<BRect>()).ToArray();
        _reserved = Enumerable.Range(0, n + 1).Select(_ => new List<BRect>()).ToArray();
        _segs = new List<Seg>[n];

        for (var f = 0; f < n; f++)
        {
            CheckRooms(f);
            _segs[f] = BuildSegs(RoomItems(f), (a, b) => Joined(f, a, b));
            var rooms = _floors[f].Rooms;
            for (var a = 0; a < rooms.Count; a++)
                for (var b = a + 1; b < rooms.Count; b++)
                    if (Joined(f, a, b) && rooms[a].Rects.Any(x => rooms[b].Rects.Any(y => x.Inset(-0.01f).Overlaps(y))))
                        _plan.Links.Add(($"{f}:{rooms[a].Id}", $"{f}:{rooms[b].Id}")); // open plan: no wall between them
        }
        for (var f = 0; f < n; f++)
            AttachOpenings(f);
        for (var f = 0; f < n; f++)
            AutoWindows(f);
        PlanStairs();
        for (var f = 0; f < n; f++)
        {
            foreach (var s in _segs[f])
                EmitWall(f, s);
            EmitFloor(f);
        }
        EmitRoof();
        foreach (var a in _openings)
            EmitOpening(a);
        EmitStairs();
        EmitExtras();
        EmitAnchors();
        if (_variant.Furnish)
            for (var f = 0; f < n; f++)
                for (var r = 0; r < _floors[f].Rooms.Count; r++)
                    Furnish(f, r);
        else
            for (var f = 0; f < n; f++)
                for (var r = 0; r < _floors[f].Rooms.Count; r++)
                    PlanRoomOnly(f, r);

        // Footprint (outer wall faces of the ground floor) and bounds.
        foreach (var room in _floors[0].Rooms)
            foreach (var rect in room.Rects)
            {
                var b = BoxW(rect.X0 - _t / 2, 0, rect.Z0 - _t / 2, rect.X1 + _t / 2, 1, rect.Z1 + _t / 2);
                _plan.Footprint.Add(new BRect(b.Min.X, b.Min.Z, b.Max.X, b.Max.Z));
            }
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var b in _plan.Shell)
        {
            min = Vector3.Min(min, b.Box.Min);
            max = Vector3.Max(max, b.Box.Max);
        }
        _plan.Bounds = new Box((min + max) / 2, (max - min) / 2);
        return _plan;
    }

    /// <summary>The floors this copy has: the "repeat" floor copied to reach the variant's (or file's) floor count.</summary>
    private void ExpandFloors()
    {
        var want = _variant.Floors > 0 ? _variant.Floors : _def.DefaultFloors > 0 ? _def.DefaultFloors : _def.Floors.Count;
        if (want < _def.MinFloors || want > _def.MaxFloors)
            throw new BuildingFileException($"building \"{_def.Name}\" has {_def.MinFloors}{(_def.MaxFloors > _def.MinFloors ? $" to {_def.MaxFloors}" : "")} floor(s), not {want}"
                                            + (_def.Floors.Any(f => f.Repeat) ? "" : " (mark a floor \"repeat\": true to allow more)"));
        var extra = want - _def.Floors.Count;
        var copyOf = new List<int>();
        for (var i = 0; i < _def.Floors.Count; i++)
        {
            var copies = _def.Floors[i].Repeat ? 1 + extra : 1;
            for (var k = 0; k < copies; k++)
            {
                _floors.Add(_def.Floors[i]);
                _floorOrig.Add(i);
                copyOf.Add(k);
            }
        }
        for (var e = 0; e < _floors.Count; e++)
            foreach (var s in _def.Stairs)
                if (s.Floor == _floorOrig[e])
                    _stairs.Add(copyOf[e] % 2 == 1 && s.HasAlt
                        ? (new BStair { Floor = s.Floor, Rect = s.AltRect, Dx = s.AltDx, Dz = s.AltDz, Surface = s.Surface, Railing = s.Railing }, e)
                        : (s, e));
    }

    /// <summary>The first expanded floor made from file floor <paramref name="orig"/>.</summary>
    private int FloorOf(int orig)
    {
        var i = _floorOrig.IndexOf(orig);
        return i < 0 ? Math.Min(orig, _floors.Count - 1) : i;
    }

    private string Where(int f) => $"building \"{_def.Name}\", floor {f}";

    private void CheckRooms(int f)
    {
        var items = RoomItems(f);
        for (var i = 0; i < items.Count; i++)
            for (var j = i + 1; j < items.Count; j++)
                if (items[i].R.Overlaps(items[j].R))
                    throw new BuildingFileException($"{Where(f)}: rooms \"{_floors[f].Rooms[items[i].Owner].Id}\" and \"{_floors[f].Rooms[items[j].Owner].Id}\" overlap");
        foreach (var room in _floors[f].Rooms)
            foreach (var o in room.OpenTo)
                if (!_floors[f].Rooms.Any(x => x.Id.Equals(o, StringComparison.OrdinalIgnoreCase)))
                    throw new BuildingFileException($"{Where(f)}: room \"{room.Id}\" is \"openTo\" \"{o}\", which isn't a room on this floor");
    }

    private List<(BRect R, int Owner)> RoomItems(int f)
    {
        var list = new List<(BRect, int)>();
        for (var i = 0; i < _floors[f].Rooms.Count; i++)
            foreach (var r in _floors[f].Rooms[i].Rects)
                list.Add((r, i));
        return list;
    }

    private bool Joined(int f, int a, int b)
    {
        var ra = _floors[f].Rooms[a];
        var rb = _floors[f].Rooms[b];
        return ra.OpenTo.Contains(rb.Id, StringComparer.OrdinalIgnoreCase) || rb.OpenTo.Contains(ra.Id, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The room on floor <paramref name="f"/> whose rects contain the point (strictly), or -1.</summary>
    private int RoomAt(int f, float x, float z)
    {
        var rooms = _floors[f].Rooms;
        for (var i = 0; i < rooms.Count; i++)
            foreach (var r in rooms[i].Rects)
                if (x > r.X0 && x < r.X1 && z > r.Z0 && z < r.Z1)
                    return i;
        return -1;
    }

    /// <summary>
    /// Wall segments from owned rectangles: wherever an edge of one owner's rect has a different owner (or nobody) on the
    /// other side, unless the two owners are <paramref name="joined"/>. Deterministic order.
    /// </summary>
    internal static List<Seg> BuildSegs(List<(BRect R, int Owner)> items, Func<int, int, bool> joined)
    {
        var groups = new SortedDictionary<(int AlongX, int Key), List<(float C, float A, float B, int Owner, bool Pos)>>();
        void Edge(bool alongX, float c, float a, float b, int owner, bool pos)
        {
            var key = (alongX ? 1 : 0, (int)MathF.Round(c * 1000f));
            if (!groups.TryGetValue(key, out var l))
                groups[key] = l = new List<(float, float, float, int, bool)>();
            l.Add((c, a, b, owner, pos));
        }
        foreach (var (r, o) in items)
        {
            Edge(true, r.Z0, r.X0, r.X1, o, true); // north edge: the room is on the +z side
            Edge(true, r.Z1, r.X0, r.X1, o, false);
            Edge(false, r.X0, r.Z0, r.Z1, o, true);
            Edge(false, r.X1, r.Z0, r.Z1, o, false);
        }
        var segs = new List<Seg>();
        foreach (var ((alongX, _), edges) in groups)
        {
            var pts = edges.SelectMany(e => new[] { e.A, e.B }).Distinct().OrderBy(p => p).ToList();
            Seg? cur = null;
            for (var k = 0; k + 1 < pts.Count; k++)
            {
                var mid = (pts[k] + pts[k + 1]) / 2;
                int neg = -1, pos = -1;
                foreach (var e in edges)
                    if (mid > e.A && mid < e.B)
                    {
                        if (e.Pos)
                            pos = e.Owner;
                        else
                            neg = e.Owner;
                    }
                var wall = neg != pos && !(neg >= 0 && pos >= 0 && joined(neg, pos));
                if (!wall)
                {
                    cur = null;
                    continue;
                }
                if (cur != null && cur.Neg == neg && cur.Pos == pos && MathF.Abs(cur.B - pts[k]) < 0.0001f)
                    cur.B = pts[k + 1];
                else
                {
                    cur = new Seg { AlongX = alongX == 1, C = edges[0].C, A = pts[k], B = pts[k + 1], Neg = neg, Pos = pos };
                    segs.Add(cur);
                }
            }
        }
        return segs;
    }

    // ---- Openings ------------------------------------------------------------------------------------------

    private float ClearHeight(int f) => _level[f + 1] - _level[f] - Slab;

    private void AttachOpenings(int f)
    {
        foreach (var o in _floors[f].Openings)
            Attach(f, o, false);
    }

    /// <summary>Fixes an opening to the wall its point lies on. With <paramref name="soft"/> a misfit returns false instead of throwing.</summary>
    private bool Attach(int f, BOpening o, bool soft)
    {
        var w = $"{Where(f)}: {o.Kind.ToString().ToLowerInvariant()} at ({o.X:0.##}, {o.Z:0.##})";
        Seg? seg = null;
        foreach (var s in _segs[f])
        {
            var onLine = s.AlongX ? MathF.Abs(s.C - o.Z) < 0.06f && o.X > s.A && o.X < s.B : MathF.Abs(s.C - o.X) < 0.06f && o.Z > s.A && o.Z < s.B;
            if (onLine)
            {
                seg = s;
                break;
            }
        }
        if (seg == null)
            return soft ? false : throw new BuildingFileException($"{w}: not on a wall (walls run along the room rect edges; put \"at\" on one)");
        var along = seg.AlongX ? o.X : o.Z;
        var half = o.Width / 2;
        var margin = _t / 2 + 0.05f;
        if (along - half < seg.A + margin - 0.001f || along + half > seg.B - margin + 0.001f) // (a millimetre's grace for rounding)
            return soft ? false : throw new BuildingFileException($"{w}: {o.Width:0.##} m wide doesn't fit its wall ({seg.A:0.##} to {seg.B:0.##}, minus {margin:0.##} m at each end)");
        var bottom = o.Kind is OpeningKind.Door or OpeningKind.Arch ? 0f : o.Bottom;
        var top = o.Top > 0 ? o.Top : _def.DoorHeight;
        var clear = ClearHeight(f);
        if (top > clear - 0.05f)
            return soft ? false : throw new BuildingFileException($"{w}: its top ({top:0.##} m) is above the ceiling ({clear:0.##} m clear)");
        if (top < bottom + 0.3f)
            return soft ? false : throw new BuildingFileException($"{w}: \"head\" must be at least 0.3 m above \"sill\"");
        foreach (var h in seg.Holes)
            if (along - half < h.B + 0.099f && along + half > h.A - 0.099f)
                return soft ? false : throw new BuildingFileException($"{w}: overlaps another opening in the same wall");
        seg.Holes.Add((along - half, along + half, bottom, top));
        _openings.Add(new Attached(o, seg, f, along, bottom, top));
        return true;
    }

    /// <summary>Windows spread along every exterior wall of rooms with an "autoWindows" rule (the room's, else the floor's).</summary>
    private void AutoWindows(int f)
    {
        var floor = _floors[f];
        foreach (var s in _segs[f])
        {
            if (!s.Exterior)
                continue;
            var ri = s.Neg >= 0 ? s.Neg : s.Pos;
            var room = floor.Rooms[ri];
            var spec = room.AutoWindows ?? floor.AutoWindows;
            if (spec == null || !room.Windows)
                continue;
            var lo = s.A + _t / 2 + 0.35f;
            var hi = s.B - _t / 2 - 0.35f;
            var usable = hi - lo;
            if (usable < spec.Width)
                continue;
            var count = 1 + (int)MathF.Floor((usable - spec.Width) / spec.Spacing);
            var span = (count - 1) * spec.Spacing;
            var start = (lo + hi) / 2 - span / 2;
            var head = MathF.Min(spec.Head, ClearHeight(f) - 0.1f);
            for (var k = 0; k < count; k++)
            {
                var u = start + k * spec.Spacing;
                var o = new BOpening
                {
                    Kind = OpeningKind.Window, Width = spec.Width, Bottom = spec.Sill, Top = head, Health = spec.Health, Auto = true,
                    X = s.AlongX ? u : s.C, Z = s.AlongX ? s.C : u,
                };
                Attach(f, o, true); // skipped where it would overlap a door or another window
            }
        }
    }

    // ---- Walls ---------------------------------------------------------------------------------------------

    private BSurface RoomWall(int f, int r) => r >= 0 ? _floors[f].Rooms[r].Wall ?? _def.InteriorWall : _def.InteriorWall;

    private BSurface RoomFloor(int f, int r) => r >= 0 ? _floors[f].Rooms[r].Floor ?? _def.FloorSurface : _def.FloorSurface;

    private BSurface RoomCeiling(int f, int r) => r >= 0 ? _floors[f].Rooms[r].Ceiling ?? _def.CeilingSurface : _def.CeilingSurface;

    private BSurface RoofSurface => _def.Roof.Surface ?? new BSurface(new ColorRef(new Vector3(0.36f, 0.33f, 0.31f), "roof"), (int)Material.Tiles);

    /// <summary>Wall core (full thickness, floor base to the next floor's level) and each room's lining on its own side.</summary>
    private void EmitWall(int f, Seg s)
    {
        var y0 = f == 0 ? _origin.Y : _level[f];
        var y1 = _level[f + 1];
        var lf = _level[f];
        var holes = s.Holes.OrderBy(h => h.A).Select(h => (h.A, h.B, lf + h.Y0, lf + h.Y1)).ToList();
        var open = f == _floors.Count - 1 && _roof == RoofKind.None; // nothing over the top floor
        // An interior wall stops under the slab above it (its top would fight with the floor or roof there); exterior walls
        // run up through the slab's edge (a wall or parapet above covers their top).
        CutWall(s.AlongX, s.A - _t / 2, s.B + _t / 2, s.C - _t / 2, s.C + _t / 2, y0, s.Exterior || open ? y1 : y1 - Slab, holes, s.Exterior ? _def.ExteriorWall : _def.InteriorWall);
        var liningTop = open ? y1 : y1 - Slab;
        for (var side = 0; side < 2; side++)
        {
            var r = side == 0 ? s.Neg : s.Pos;
            if (r < 0)
                continue;
            var dir = side == 0 ? -1f : 1f;
            var c0 = s.C + dir * _t / 2;
            var c1 = c0 + dir * BuildingGen.LiningT;
            var a = s.A;
            var b = s.B;
            if (!RoomContinues(f, r, s, a - 0.05f, dir))
                a += _t / 2;
            if (!RoomContinues(f, r, s, b + 0.05f, dir))
                b -= _t / 2;
            CutWall(s.AlongX, a, b, MathF.Min(c0, c1), MathF.Max(c0, c1), lf, liningTop, holes, RoomWall(f, r));
        }
    }

    /// <summary>Does room <paramref name="r"/> carry on past the end of a wall (no corner there, the lining keeps going)?</summary>
    private bool RoomContinues(int f, int r, Seg s, float along, float dir)
    {
        var across = s.C + dir * 0.05f;
        var (x, z) = s.AlongX ? (along, across) : (across, along);
        return RoomAt(f, x, z) == r;
    }

    /// <summary>A wall slab with rectangular holes: pieces between the holes, under them (sills) and over them (lintels).</summary>
    private void CutWall(bool alongX, float a0, float b0, float c0, float c1, float y0, float y1, List<(float A, float B, float Y0, float Y1)> holes, BSurface s)
    {
        var cursor = a0;
        foreach (var h in holes)
        {
            var ha = MathF.Max(h.A, a0);
            var hb = MathF.Min(h.B, b0);
            if (hb <= ha)
                continue;
            AddAlong(alongX, cursor, ha, c0, c1, y0, y1, s);
            AddAlong(alongX, ha, hb, c0, c1, y0, MathF.Max(y0, h.Y0), s);
            AddAlong(alongX, ha, hb, c0, c1, MathF.Min(y1, h.Y1), y1, s);
            cursor = hb;
        }
        AddAlong(alongX, cursor, b0, c0, c1, y0, y1, s);
    }

    // ---- Floors, ceilings, roofs ------------------------------------------------------------------------

    private void EmitFloor(int f)
    {
        var rooms = _floors[f].Rooms;
        var lf = _level[f];
        for (var r = 0; r < rooms.Count; r++)
        {
            foreach (var rect in rooms[r].Rects)
            {
                if (f == 0)
                    Add(rect.X0, _origin.Y, rect.Z0, rect.X1, lf, rect.Z1, RoomFloor(f, r));
                else
                    foreach (var p in BRect.Subtract([rect], _holesIn[f]))
                        Add(p.X0, lf - Slab, p.Z0, p.X1, lf, p.Z1, RoomFloor(f, r));
                var top = f == _floors.Count - 1;
                if (!top || _roof != RoofKind.None)
                {
                    var cy = _level[f + 1] - Slab;
                    foreach (var p in BRect.Subtract([rect.Inset(_t / 2)], _holesIn[f + 1]))
                        Add(p.X0, cy - BuildingGen.LiningT, p.Z0, p.X1, cy, p.Z1, RoomCeiling(f, r));
                }
            }
        }
        // Parts of this floor with no floor above are flat roofs (a terrace, a lower wing) with a parapet.
        if (f < _floors.Count - 1)
        {
            var above = _floors[f + 1].Rooms.SelectMany(x => x.Rects).ToList();
            var exposed = BRect.Subtract(rooms.SelectMany(x => x.Rects), above);
            var y = _level[f + 1];
            foreach (var p in exposed)
                Add(p.X0, y - Slab, p.Z0, p.X1, y, p.Z1, RoofSurface);
            if (exposed.Count > 0)
            {
                var items = exposed.Select(p => (p, 0)).Concat(above.Select(a => (a, 1))).ToList();
                foreach (var s in BuildSegs(items, (_, _) => false))
                    if (s.Neg == 0 && s.Pos < 0 || s.Pos == 0 && s.Neg < 0)
                        AddAlong(s.AlongX, s.A - _t / 2, s.B + _t / 2, s.C - _t / 2, s.C + _t / 2, y, y + 1.0f, _def.ExteriorWall);
            }
        }
    }

    private void EmitRoof()
    {
        var top = _floors.Count - 1;
        var y = _level[top + 1];
        var rects = _floors[top].Rooms.SelectMany(x => x.Rects).ToList();
        if (_roof == RoofKind.None)
            return;
        if (_roof != RoofKind.Flat && _holesIn[top + 1].Count > 0)
            throw new BuildingFileException($"building \"{_def.Name}\": stairs onto the roof need a flat roof");
        foreach (var p in BRect.Subtract(rects, _holesIn[top + 1]))
            Add(p.X0, y - Slab, p.Z0, p.X1, y, p.Z1, RoofSurface);
        if (_roof == RoofKind.Flat)
        {
            var parapet = MathF.Max(_def.Roof.Parapet, 0.1f); // at least a coping over the wall tops
            foreach (var s in _segs[top])
                if (s.Exterior)
                    AddAlong(s.AlongX, s.A - _t / 2, s.B + _t / 2, s.C - _t / 2, s.C + _t / 2, y, y + parapet, _def.ExteriorWall);
            return;
        }
        // Pitched roofs: stepped layers over the top floor's bounds (the game's chunky style; collision matches exactly).
        var b = rects.Aggregate((a, c) => a.Union(c)).Inset(-(_t / 2 + _def.Roof.Overhang));
        var ridgeX = _def.Roof.Ridge == 'x' || _def.Roof.Ridge == 0 && b.W >= b.D;
        var tan = MathF.Tan(_def.Roof.Pitch * MathF.PI / 180f);
        var surf = RoofSurface;
        if (_roof == RoofKind.Shed)
        {
            var dx = _def.Roof.ShedDx;
            var dz = _def.Roof.ShedDz;
            var span = dx != 0 ? b.W : b.D;
            var h = span * tan;
            var n = Math.Clamp((int)MathF.Round(h / 0.25f), 2, 24);
            for (var i = 0; i < n; i++)
            {
                var keep = span * (1f - i / (float)n);
                var r = dx > 0 ? b with { X0 = b.X1 - keep } : dx < 0 ? b with { X1 = b.X0 + keep } : dz > 0 ? b with { Z0 = b.Z1 - keep } : b with { Z1 = b.Z0 + keep };
                Add(r.X0, y + i * h / n, r.Z0, r.X1, y + (i + 1) * h / n, r.Z1, surf);
            }
            return;
        }
        var hs0 = (ridgeX ? b.D : b.W) / 2;
        var hl0 = (ridgeX ? b.W : b.D) / 2;
        var height = hs0 * tan;
        var layers = Math.Clamp((int)MathF.Round(height / 0.25f), 2, 24);
        var dh = height / layers;
        var c = b.Center;
        for (var i = 0; i < layers; i++)
        {
            var hs = hs0 * (1f - i / (float)layers);
            var hl = _roof == RoofKind.Hip ? MathF.Max(hl0 - (hs0 - hs), hs) : hl0;
            if (ridgeX)
                Add(c.X - hl, y + i * dh, c.Y - hs, c.X + hl, y + (i + 1) * dh, c.Y + hs, surf);
            else
                Add(c.X - hs, y + i * dh, c.Y - hl, c.X + hs, y + (i + 1) * dh, c.Y + hl, surf);
        }
    }

    // ---- Stairs ---------------------------------------------------------------------------------------------

    private readonly List<(BStair S, int F, BRect R, Vector2 Bottom, Vector2 Top)> _stairPlans = new();

    private void PlanStairs()
    {
        var n = _floors.Count;
        foreach (var (s, f) in _stairs)
        {
            var w = $"{Where(f)}: stairs at [{s.Rect.X0:0.##}, {s.Rect.Z0:0.##}, {s.Rect.X1:0.##}, {s.Rect.Z1:0.##}]";
            var r = s.Rect;
            var room = RoomAt(f, r.Center.X, r.Center.Y);
            if (room < 0 || !_floors[f].Rooms[room].Rects.Any(x => r.X0 >= x.X0 + _t / 2 - 0.01f && r.X1 <= x.X1 - _t / 2 + 0.01f && r.Z0 >= x.Z0 + _t / 2 - 0.01f && r.Z1 <= x.Z1 - _t / 2 + 0.01f))
                throw new BuildingFileException($"{w}: must lie inside one room, clear of its walls");
            var len = s.Dx != 0 ? r.W : r.D;
            var rise = _level[f + 1] - _level[f];
            var steps = (int)MathF.Ceiling(rise / BuildingGen.StepRiseMax);
            if (len / steps < 0.2f)
                throw new BuildingFileException($"{w}: too short for {steps} steps of {rise / steps:0.##} m (needs at least {steps * 0.2f:0.#} m along \"dir\")");
            var c = r.Center;
            var lowEdge = new Vector2(s.Dx > 0 ? r.X0 : s.Dx < 0 ? r.X1 : c.X, s.Dz > 0 ? r.Z0 : s.Dz < 0 ? r.Z1 : c.Y);
            var highEdge = new Vector2(s.Dx > 0 ? r.X1 : s.Dx < 0 ? r.X0 : c.X, s.Dz > 0 ? r.Z1 : s.Dz < 0 ? r.Z0 : c.Y);
            var d = new Vector2(s.Dx, s.Dz);
            var bottom = lowEdge - d * 0.7f;
            var top = highEdge + d * 0.7f;
            if (RoomAt(f, bottom.X, bottom.Y) < 0)
                throw new BuildingFileException($"{w}: the foot of the stairs ({bottom.X:0.##}, {bottom.Y:0.##}) isn't inside a room (leave 0.7 m in front of the first step)");
            if (f + 1 < n ? RoomAt(f + 1, top.X, top.Y) < 0 : RoomAt(f, top.X, top.Y) < 0)
                throw new BuildingFileException($"{w}: the top of the stairs ({top.X:0.##}, {top.Y:0.##}) doesn't land in a room on the floor above (leave 0.7 m beyond the last step)");
            if (f + 1 >= n && _roof != RoofKind.Flat)
                throw new BuildingFileException($"{w}: stairs onto the roof need a flat roof");
            _holesIn[f + 1].Add(r);
            var width = s.Dx != 0 ? r.D : r.W;
            // Furniture keeps off the stairs and the spaces at both ends.
            _reserved[f].Add(Grow(r, bottom, width));
            _reserved[f + 1].Add(Grow(r.Inset(-0.1f), top, width));
            _stairPlans.Add((s, f, r, bottom, top));
        }
    }

    /// <summary>The rect stretched to include a 1 m deep, stair-wide patch centred at <paramref name="p"/>.</summary>
    private static BRect Grow(BRect r, Vector2 p, float width)
    {
        var patch = new BRect(p.X - MathF.Max(0.5f, width / 2) - 0.1f, p.Y - 0.6f, p.X + MathF.Max(0.5f, width / 2) + 0.1f, p.Y + 0.6f);
        return r.Union(patch);
    }

    private void EmitStairs()
    {
        var n = _floors.Count;
        foreach (var (s, f, r, bottom, top) in _stairPlans)
        {
            var lf = _level[f];
            var rise = _level[f + 1] - lf;
            var steps = (int)MathF.Ceiling(rise / BuildingGen.StepRiseMax);
            var len = s.Dx != 0 ? r.W : r.D;
            var run = len / steps;
            var room = RoomAt(f, r.Center.X, r.Center.Y);
            var surf = s.Surface ?? RoomFloor(f, room);
            for (var k = 1; k <= steps; k++)
            {
                var y1 = k == steps ? _level[f + 1] : lf + k * rise / steps;
                float a, b;
                if (s.Dx > 0 || s.Dz > 0)
                {
                    var o = s.Dx != 0 ? r.X0 : r.Z0;
                    (a, b) = (o + (k - 1) * run, o + k * run);
                }
                else
                {
                    var o = s.Dx != 0 ? r.X1 : r.Z1;
                    (a, b) = (o - k * run, o - (k - 1) * run);
                }
                if (s.Dx != 0)
                    Add(a, lf, r.Z0, b, y1, r.Z1, surf);
                else
                    Add(r.X0, lf, a, r.X1, y1, b, surf);
            }
            var pb = P(bottom.X, lf, bottom.Y);
            var pt = P(top.X, _level[f + 1], top.Y);
            _plan.Waypoints.Add(pb);
            _plan.Waypoints.Add(pt);
            _plan.Stairs.Add((pb, pt));
            var upperRoom = f + 1 < n ? RoomAt(f + 1, top.X, top.Y) : -1;
            _plan.Links.Add(($"{f}:{_floors[f].Rooms[RoomAt(f, bottom.X, bottom.Y)].Id}", f + 1 < n ? $"{f + 1}:{_floors[f + 1].Rooms[upperRoom].Id}" : "roof"));
            if (!s.Railing)
                continue;
            // Railings round the hole on the floor above: the two long sides and the low end, unless a wall is already there.
            var upper = f + 1 < n ? f + 1 : f;
            var yr = _level[f + 1];
            var sides = new List<(bool AlongX, float C, float A, float B, float Out)>();
            if (s.Dx != 0)
            {
                sides.Add((true, r.Z0, r.X0, r.X1, -1));
                sides.Add((true, r.Z1, r.X0, r.X1, 1));
                sides.Add((false, s.Dx > 0 ? r.X0 : r.X1, r.Z0, r.Z1, s.Dx > 0 ? -1 : 1));
            }
            else
            {
                sides.Add((false, r.X0, r.Z0, r.Z1, -1));
                sides.Add((false, r.X1, r.Z0, r.Z1, 1));
                sides.Add((true, s.Dz > 0 ? r.Z0 : r.Z1, r.X0, r.X1, s.Dz > 0 ? -1 : 1));
            }
            foreach (var (ax, c, a0, b0, o) in sides)
            {
                if (NearWall(upper, ax, c, a0, b0))
                    continue;
                var c0 = o < 0 ? c - 0.05f : c;
                AddAlong(ax, a0, b0, c0, c0 + 0.05f, yr, yr + 1f, _def.TrimSurface);
            }
        }
    }

    /// <summary>Is there a wall (or, on the roof, a parapet line) on floor <paramref name="f"/> along this line?</summary>
    private bool NearWall(int f, bool alongX, float c, float a, float b)
    {
        foreach (var s in _segs[f])
            if (s.AlongX == alongX && MathF.Abs(s.C - c) < _t / 2 + 0.12f && MathF.Min(b, s.B) - MathF.Max(a, s.A) > (b - a) * 0.5f)
                return true;
        return false;
    }

    // ---- Doors, windows ------------------------------------------------------------------------------------

    private void EmitOpening(Attached at)
    {
        var (o, s, f) = (at.O, at.S, at.Floor);
        var lf = _level[f];
        // Which way it opens: an exterior door into the building, an interior one towards +x / +z; "swing": "out" flips it.
        var sign = s.Exterior ? (s.Pos >= 0 ? 1f : -1f) : 1f;
        if (o.SwingOut)
            sign = -sign;
        var n = s.AlongX ? new Vector2(0, sign) : new Vector2(sign, 0);
        var centre = s.AlongX ? new Vector2(at.Along, s.C) : new Vector2(s.C, at.Along);
        var half = o.Width / 2;
        if (o.Kind is OpeningKind.Door or OpeningKind.Arch || o.Kind == OpeningKind.Hole && at.Bottom <= 1.2f)
        {
            string Key(int r) => r < 0 ? "outside" : $"{f}:{_floors[f].Rooms[r].Id}";
            _plan.Links.Add((Key(s.Neg), Key(s.Pos)));
            if (s.Exterior && f == 0 && o.Kind != OpeningKind.Hole && (_plan.FrontRoom == null || o.Front))
                _plan.FrontRoom = Key(s.Neg >= 0 ? s.Neg : s.Pos);
        }
        if (o.Kind is OpeningKind.Door or OpeningKind.Arch)
        {
            var sw = centre + n * 0.9f;
            var ot = centre - n * 0.9f;
            _plan.Waypoints.Add(P(sw.X, lf, sw.Y));
            _plan.Waypoints.Add(P(ot.X, lf, ot.Y));
        }
        switch (o.Kind)
        {
            case OpeningKind.Door:
            {
                var right = new Vector2(-n.Y, n.X); // the right hand of someone looking along n
                var hs = o.HingeRight ? 1f : -1f;
                var hinge = centre + right * hs * (half - 0.02f);
                var lw = o.Width - 0.04f;
                var h = at.Top - at.Bottom - 0.02f;
                const float th = 0.06f;
                var a = at.Along - lw / 2;
                var b = at.Along + lw / 2;
                var box = s.AlongX ? BoxW(a, lf + at.Bottom, s.C - th / 2, b, lf + at.Bottom + h, s.C + th / 2)
                    : BoxW(s.C - th / 2, lf + at.Bottom, a, s.C + th / 2, lf + at.Bottom + h, b);
                var closed = -right * hs;
                var sw = centre + n * 0.9f;
                var ot = centre - n * 0.9f;
                _plan.Doors.Add(new PlannedDoor
                {
                    Closed = box,
                    Hinge = P(hinge.X, lf + at.Bottom, hinge.Y),
                    ClosedDir = V(closed.X, closed.Y),
                    OpenDir = V(n.X, n.Y),
                    Width = lw,
                    Height = h,
                    Thickness = th,
                    Material = o.Material,
                    Health = o.Health > 0 ? o.Health : BuildingGen.DoorHealth(o.Material),
                    Color = o.Color is { } dc ? Col(dc) : o.Material == DoorMaterial.Glass ? new Vector3(0.62f, 0.78f, 0.86f) : Col(_def.DoorColor),
                    StartOpen = _variant.Doors switch { "open" => true, "random" => Rand() < 0.5f, _ => o.StartOpen },
                    Front = o.Front,
                    Floor = f,
                    SwingSide = P(sw.X, lf, sw.Y),
                    OtherSide = P(ot.X, lf, ot.Y),
                });
                break;
            }
            case OpeningKind.Window:
            {
                var a = at.Along - half + 0.01f;
                var b = at.Along + half - 0.01f;
                var box = s.AlongX ? BoxW(a, lf + at.Bottom, s.C - 0.02f, b, lf + at.Top, s.C + 0.02f)
                    : BoxW(s.C - 0.02f, lf + at.Bottom, a, s.C + 0.02f, lf + at.Top, b);
                _plan.Windows.Add(new PlannedWindow { Glass = box, Health = o.Health > 0 ? o.Health : 1, Floor = f, Sill = at.Bottom, Exterior = s.Exterior });
                if (s.Exterior && at.Bottom > 0.2f)
                {
                    // A sill ledge on the outside.
                    var outSign = s.Pos >= 0 ? -1f : 1f;
                    var c0 = s.C + outSign * _t / 2;
                    var c1 = c0 + outSign * 0.08f;
                    AddAlong(s.AlongX, at.Along - half - 0.05f, at.Along + half + 0.05f, MathF.Min(c0, c1), MathF.Max(c0, c1), lf + at.Bottom - 0.06f, lf + at.Bottom, _def.TrimSurface);
                }
                break;
            }
        }
    }

    // ---- Extras and anchors ----------------------------------------------------------------------------------

    private void EmitExtras()
    {
        var baseY = _origin.Y;
        var l0 = _level[0];
        foreach (var x in _def.Extras)
        {
            switch (x.Kind)
            {
                case ExtraKind.Block:
                    Add(x.Min.X, baseY + x.Min.Y, x.Min.Z, x.Max.X, baseY + x.Max.Y, x.Max.Z, x.Surface ?? _def.TrimSurface);
                    break;
                case ExtraKind.Prop:
                {
                    var def = _def.FindProp(x.Prop!, _scope) ?? throw new BuildingFileException($"building \"{_def.Name}\": extras: unknown prop \"{x.Prop}\" ({_scope.Suggest(x.Prop!)})");
                    var py = x.OnRoof ? _level[_floors.Count] + x.At.Y : baseY + x.At.Y;
                    _plan.Props.Add(new PlannedProp(def, P(x.At.X, py, x.At.Z), YawW(x.Yaw), x.Scale, "exterior"));
                    break;
                }
                case ExtraKind.Porch:
                case ExtraKind.Awning:
                {
                    var r = x.Rect;
                    var surf = x.Surface ?? _def.TrimSurface;
                    if (x.Kind == ExtraKind.Porch)
                        Add(r.X0, baseY, r.Z0, r.X1, l0, r.Z1, x.Surface ?? _def.FloorSurface);
                    var top = baseY + x.Height;
                    if (x.Roof)
                        Add(r.X0 - 0.1f, top - 0.15f, r.Z0 - 0.1f, r.X1 + 0.1f, top, r.Z1 + 0.1f, surf);
                    if (x.Posts && x.Roof)
                        foreach (var (px, pz) in new[] { (r.X0, r.Z0), (r.X1, r.Z0), (r.X0, r.Z1), (r.X1, r.Z1) })
                        {
                            if (InsideFootprint(px, pz))
                                continue; // against the building: no post needed
                            var cx = Math.Clamp(px, r.X0 + 0.08f, r.X1 - 0.08f);
                            var cz = Math.Clamp(pz, r.Z0 + 0.08f, r.Z1 - 0.08f);
                            Add(cx - 0.08f, x.Kind == ExtraKind.Porch ? l0 : baseY, cz - 0.08f, cx + 0.08f, top - 0.15f, cz + 0.08f, surf);
                        }
                    break;
                }
                case ExtraKind.Platform:
                    EmitPlatform(x);
                    break;
                case ExtraKind.Balcony:
                    for (var f = 0; f < _floors.Count; f++)
                        if (_floorOrig[f] == x.Floor)
                            EmitBalcony(x, f); // every copy of a repeated floor gets one
                    break;
            }
        }
    }

    private void EmitBalcony(BExtra x, int f)
    {
        var y = _level[f];
        var r = x.Rect;
        Add(r.X0, y - Slab, r.Z0, r.X1, y, r.Z1, x.Surface ?? _def.FloorSurface);
        if (x.Railing <= 0.01f)
            return;
        var edges = new (bool AlongX, float C, float A, float B, float Out)[]
        {
            (true, r.Z0, r.X0, r.X1, -1), (true, r.Z1, r.X0, r.X1, 1), (false, r.X0, r.Z0, r.Z1, -1), (false, r.X1, r.Z0, r.Z1, 1),
        };
        foreach (var (ax, c, a, b, o) in edges)
        {
            // An edge against the building (the wall is just beyond it) gets no railing.
            var mid = (a + b) / 2;
            var (px, pz) = ax ? (mid, c + o * (_t / 2 + 0.1f)) : (c + o * (_t / 2 + 0.1f), mid);
            if (RoomAt(f, px, pz) >= 0 || NearWall(f, ax, c, a, b))
                continue;
            var c0 = o < 0 ? c : c - 0.05f;
            AddAlong(ax, a, b, c0, c0 + 0.05f, y, y + x.Railing, _def.TrimSurface);
        }
    }

    /// <summary>Low ceilings furniture must fit under: (area, clear height above the floor), per floor (platforms).</summary>
    private readonly Dictionary<int, List<(BRect R, float Clear)>> _lowCeil = new();

    /// <summary>The feet of platform stairs (furniture keeps a walkway to them).</summary>
    private readonly List<(int Floor, Vector2 Bottom)> _platformStairs = new();

    /// <summary>A platform (a mezzanine) inside a floor: a slab on posts, railings on its open sides, and its own stairs.</summary>
    private void EmitPlatform(BExtra x)
    {
        var f = FloorOf(x.Floor);
        var lf = _level[f];
        var top = lf + x.Height;
        var r = x.Rect;
        var w = $"{Where(f)}: platform at [{r.X0:0.##}, {r.Z0:0.##}, {r.X1:0.##}, {r.Z1:0.##}]";
        if (top > _level[f + 1] - Slab - 1.9f)
            throw new BuildingFileException($"{w}: {x.Height:0.##} m high leaves no headroom under the ceiling (needs 1.9 m)");
        var surf = x.Surface ?? _def.FloorSurface;
        Add(r.X0, top - 0.2f, r.Z0, r.X1, top, r.Z1, surf);
        if (!_lowCeil.TryGetValue(f, out var low))
            _lowCeil[f] = low = new List<(BRect, float)>();
        low.Add((r, x.Height - 0.25f));
        var hasStairs = x.StairRect.W > 0;
        var landing = 0; // which edge the stairs arrive at: 1 north, 2 south, 3 west, 4 east
        if (hasStairs)
        {
            var s = x.StairRect;
            var steps = (int)MathF.Ceiling(x.Height / BuildingGen.StepRiseMax);
            var len = x.StairDx != 0 ? s.W : s.D;
            if (len / steps < 0.2f)
                throw new BuildingFileException($"{w}: its stairs are too short for {steps} steps (need {steps * 0.2f:0.#} m)");
            var run = len / steps;
            for (var k = 1; k <= steps; k++)
            {
                var y1 = k == steps ? top : lf + k * x.Height / steps;
                float a, b;
                if (x.StairDx > 0 || x.StairDz > 0)
                {
                    var o = x.StairDx != 0 ? s.X0 : s.Z0;
                    (a, b) = (o + (k - 1) * run, o + k * run);
                }
                else
                {
                    var o = x.StairDx != 0 ? s.X1 : s.Z1;
                    (a, b) = (o - k * run, o - (k - 1) * run);
                }
                if (x.StairDx != 0)
                    Add(a, lf, s.Z0, b, y1, s.Z1, surf);
                else
                    Add(s.X0, lf, a, s.X1, y1, b, surf);
            }
            var c = s.Center;
            var d = new Vector2(x.StairDx, x.StairDz);
            var low0 = new Vector2(x.StairDx > 0 ? s.X0 : x.StairDx < 0 ? s.X1 : c.X, x.StairDz > 0 ? s.Z0 : x.StairDz < 0 ? s.Z1 : c.Y);
            var high = new Vector2(x.StairDx > 0 ? s.X1 : x.StairDx < 0 ? s.X0 : c.X, x.StairDz > 0 ? s.Z1 : x.StairDz < 0 ? s.Z0 : c.Y);
            var bottom = low0 - d * 0.7f;
            var up = high + d * 0.6f;
            if (!r.Contains(up.X, up.Y))
                throw new BuildingFileException($"{w}: its stairs must arrive on it (0.6 m past the top step is ({up.X:0.##}, {up.Y:0.##}))");
            if (RoomAt(f, bottom.X, bottom.Y) < 0)
                throw new BuildingFileException($"{w}: the foot of its stairs ({bottom.X:0.##}, {bottom.Y:0.##}) isn't inside a room");
            landing = x.StairDz > 0 ? 1 : x.StairDz < 0 ? 2 : x.StairDx > 0 ? 3 : 4;
            var pb = P(bottom.X, lf, bottom.Y);
            var pt = P(up.X, top, up.Y);
            _plan.Waypoints.Add(pb);
            _plan.Waypoints.Add(pt);
            _plan.Stairs.Add((pb, pt));
            _reserved[f].Add(Grow(s, bottom, x.StairDx != 0 ? s.D : s.W));
            _platformStairs.Add((f, bottom));
        }
        // Posts at corners that aren't against a wall; railings round the open sides (not where the stairs arrive).
        foreach (var (px, pz) in new[] { (r.X0, r.Z0), (r.X1, r.Z0), (r.X0, r.Z1), (r.X1, r.Z1) })
        {
            if (NearWall(f, true, pz, px - 0.2f, px + 0.2f) || NearWall(f, false, px, pz - 0.2f, pz + 0.2f))
                continue;
            var cx = Math.Clamp(px, r.X0 + 0.1f, r.X1 - 0.1f);
            var cz = Math.Clamp(pz, r.Z0 + 0.1f, r.Z1 - 0.1f);
            Add(cx - 0.1f, lf, cz - 0.1f, cx + 0.1f, top - 0.2f, cz + 0.1f, _def.TrimSurface);
            _reserved[f].Add(new BRect(cx - 0.3f, cz - 0.3f, cx + 0.3f, cz + 0.3f));
        }
        if (x.Railing <= 0.01f)
            return;
        var edges = new (int Id, bool AlongX, float C, float A, float B, float Out)[]
        {
            (1, true, r.Z0, r.X0, r.X1, -1), (2, true, r.Z1, r.X0, r.X1, 1), (3, false, r.X0, r.Z0, r.Z1, -1), (4, false, r.X1, r.Z0, r.Z1, 1),
        };
        foreach (var (id, ax, c0, a, b, o) in edges)
        {
            if (id == landing || NearWall(f, ax, c0, a, b))
                continue;
            var cc = o < 0 ? c0 : c0 - 0.05f;
            AddAlong(ax, a, b, cc, cc + 0.05f, top, top + x.Railing, _def.TrimSurface);
        }
    }

    private bool InsideFootprint(float x, float z)
    {
        foreach (var room in _floors[0].Rooms)
            foreach (var r in room.Rects)
                if (r.Inset(-_t / 2 - 0.05f).Contains(x, z))
                    return true;
        return false;
    }

    private void EmitAnchors()
    {
        foreach (var a in _def.Anchors)
        {
            var f = FloorOf(a.Floor);
            _plan.Anchors.Add((a.Type, a.Name, P(a.X, _level[f], a.Z)));
        }
    }
}
