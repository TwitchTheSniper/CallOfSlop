using System.Globalization;
using System.Numerics;
using System.Reflection;

namespace CallOfSlop.MapKit;

public sealed class MapKitException(string message) : Exception(message);

/// <summary>A map colour: "#rrggbb" text or an [r, g, b] triple (0-1), written the way it was given.</summary>
public readonly struct Color
{
    public readonly JNode Node;

    private Color(JNode node) => Node = node;

    public static implicit operator Color(string hex) => hex.Length == 7 && hex[0] == '#' ? new Color(new JString(hex)) : throw new MapKitException($"colour \"{hex}\" isn't #rrggbb");

    public static implicit operator Color((double R, double G, double B) rgb) => new(JArray.Of(rgb.R, rgb.G, rgb.B));

    public static implicit operator Color(Vector3 v) => new(JArray.Of(Kit.R(v.X), Kit.R(v.Y), Kit.R(v.Z)));

    public static Color Rgb(double r, double g, double b) => (r, g, b);

    /// <summary>The colour as the game reads it.</summary>
    public Vector3 Value => Node is JString s && s.Value.Length == 7
        ? new Vector3(((Hex(s.Value) >> 16) & 255) / 255f, ((Hex(s.Value) >> 8) & 255) / 255f, (Hex(s.Value) & 255) / 255f)
        : Node is JArray a ? new Vector3((float)((JNumber)a[0]).Value, (float)((JNumber)a[1]).Value, (float)((JNumber)a[2]).Value) : Vector3.Zero;

    private static int Hex(string s) => int.Parse(s.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    public override string ToString() => Node.ToString() ?? "";
}

/// <summary>How a map is made fair from one half (see <see cref="Kit.Twin"/>).</summary>
public enum Symmetry
{
    /// <summary>No twin.</summary>
    None,
    /// <summary>Turned 180 degrees about the centre: (x, z) to (-x, -z), yaws + 180. Exact for every shape.</summary>
    Turn180,
    /// <summary>Mirrored about x = 0: (x, z) to (-x, z), yaws negated (a prop's front faces the mirrored way).</summary>
    MirrorX,
    /// <summary>Mirrored about z = 0: (x, z) to (x, -z), yaws to 180 - yaw.</summary>
    MirrorZ,
}

/// <summary>
/// The map kit's builder: a generator calls its methods to record map ops (as the ordered JSON the game's map loader
/// reads: every op is checked against the real op definitions in <see cref="MapFile"/> when it is added), the team
/// spawns, hills and flags, then <see cref="ToDocument"/> gives the whole map file and <see cref="Write"/> its text.
/// Numbers are rounded to millimetres (yaws to a tenth of a degree) like the Python generators did. The pieces that were
/// duplicated across those generators live beside it: <see cref="Geometry"/> (outlines), <see cref="Structures"/>
/// (railings, stairs, ramps, catwalks), <see cref="Clusters"/> (prop groups), <see cref="Plan"/> (a top-down PNG),
/// <see cref="Rng"/> (a seeded generator) and <see cref="MapChecks"/> (the real Map, nav graph and movers checking it).
/// </summary>
public sealed class Kit
{
    public readonly List<JObject> Ops = new();

    /// <summary>Counts by kind (blocks, colliders, meshes, props, decals, waypoints, spawns, buildings, skipped).</summary>
    public readonly SortedDictionary<string, int> Stats = new(StringComparer.Ordinal);

    /// <summary>Spawns recorded per team ([x, y, z]); written as "teamSpawns".</summary>
    public readonly List<double[]>[] TeamSpawns = [new List<double[]>(), new List<double[]>()];

    public readonly List<JObject> Hills = new();
    public readonly List<double[]> Flags = new();

    /// <summary>The top-level fields (name, description, skybox, boundary, terrain...). <see cref="ToDocument"/> puts them in the usual order.</summary>
    public readonly JObject Top = new();

    /// <summary>Everything an op may hold besides its parameters.</summary>
    private static readonly HashSet<string> OpExtras = new(StringComparer.OrdinalIgnoreCase) { "op", "anchor" };

    public Kit(string name, string description, int order, int maxPlayers = 16, string author = "Call of Slop")
    {
        Top.Add("format", MapFile.Format);
        Top.Add("name", name);
        Top.Add("description", description);
        Top.Add("author", author);
        Top.Add("order", order);
        Top.Add("maxPlayers", maxPlayers);
    }

    public string Name => Top.Str("name") ?? "";

    private LogicKit? _logic;

    /// <summary>The map's "logic" section (Maps/README.md, "Map logic"): entities and the wires between them. Written by <see cref="ToDocument"/>.</summary>
    public LogicKit Logic => _logic ??= new LogicKit();

    // ---- the header -------------------------------------------------------------------------------------------------

    /// <summary>The map's description, order (the pack's convention: Space 600 + NN), player limit and, for a void map, the fall-kill height.</summary>
    public Kit Describe(string description, int order, int maxPlayers = 16, double? fallKill = null, bool? destructible = null)
    {
        Top.Set("description", description);
        Top.Set("order", order);
        Top.Set("maxPlayers", maxPlayers);
        if (fallKill != null)
            Top.Set("fallKill", fallKill.Value);
        if (destructible != null)
            Top.Set("destructible", destructible.Value);
        return this;
    }

    /// <summary>A rectangular map's half size (metres): the boundary is the rectangle. Shaped maps use <see cref="Boundary"/> instead.</summary>
    public Kit Size(double halfX, double halfZ)
    {
        Top.Set("halfX", R(halfX));
        Top.Set("halfZ", R(halfZ));
        return this;
    }

    /// <summary>
    /// A shaped outline (Maps/README.md "Boundaries"): <paramref name="shape"/> is "polygon" (straight sides) or "smooth"
    /// (a curve through the points); <paramref name="edge"/> is "none" or an edge object such as
    /// Edge("wall", height: 6, thickness: 0.8, color: "#9fb0bd"); <paramref name="noise"/> roughens the outline.
    /// </summary>
    public Kit Boundary(IReadOnlyList<double[]> points, string shape = "polygon", object? edge = null, double? groundMargin = null, double? barrierHeight = null, JObject? noise = null)
    {
        var b = new JObject(("shape", shape), ("points", new JArray(points.Select(p => (JNode)JArray.Of(p.Cast<object>().ToArray())))));
        if (noise != null) b.Add("noise", noise);
        if (edge != null) b.Add("edge", edge);
        if (groundMargin != null) b.Add("groundMargin", groundMargin.Value);
        if (barrierHeight != null) b.Add("barrierHeight", barrierHeight.Value);
        Top.Set("boundary", b);
        return this;
    }

    /// <summary>An outline edge: style "wall", "fence", "hedge" or "cliff" with its sizes and colour.</summary>
    public static JObject Edge(string style, double? height = null, double? thickness = null, double? width = null, Color? color = null, string? material = null, double? spacing = null)
    {
        var e = new JObject(("style", style));
        if (height != null) e.Add("height", height.Value);
        if (thickness != null) e.Add("thickness", thickness.Value);
        if (width != null) e.Add("width", width.Value);
        if (spacing != null) e.Add("spacing", spacing.Value);
        if (color != null) e.Add("color", color.Value.Node);
        if (material != null) e.Add("material", material);
        return e;
    }

    /// <summary>The sky (Maps/README.md "Skies"): a preset name with overrides, e.g. Skybox("night", ("stars", 1.8), ("ambient", 2.0)).</summary>
    public Kit Skybox(string preset, params (string Key, object Value)[] settings)
    {
        var sky = new JObject(("preset", preset));
        foreach (var (k, v) in settings)
            sky.Add(k, v);
        Top.Set("skybox", sky);
        return this;
    }

    /// <summary>The terrain section (features in order; see Maps/README.md "Terrain").</summary>
    public Kit Terrain(JObject terrain)
    {
        Top.Set("terrain", terrain);
        return this;
    }

    // ---- numbers ---------------------------------------------------------------------------------------------------

    /// <summary>Rounded to millimetres (the Python generators' r()). A negative zero stays negative: the map fingerprint mixes float bits, so -0.0 and 0.0 are different maps.</summary>
    public static double R(double v, int digits = 3) => Math.Round(v, digits, MidpointRounding.ToEven);

    /// <summary>A yaw in 0..360, rounded to a tenth of a degree.</summary>
    public static double Yaw(double yaw) => R(((yaw % 360) + 360) % 360, 1);

    // ---- raw ops ---------------------------------------------------------------------------------------------------

    /// <summary>Adds an op as given (checked against the op definitions) and returns it.</summary>
    public JObject Emit(JObject op)
    {
        Validate(op, Ops.Count + 1);
        Ops.Add(op);
        return op;
    }

    /// <summary>Adds an op from key/value pairs, e.g. Op(("op", "Ground"), ("color", "#333333")).</summary>
    public JObject Op(params (string Key, object? Value)[] items)
    {
        var o = new JObject();
        foreach (var (k, v) in items)
            if (v != null)
                o.Add(k, v is Color c ? c.Node : JNode.From(v));
        return Emit(o);
    }

    private void Count(string key, int n = 1) => Stats[key] = Stats.GetValueOrDefault(key) + n;

    /// <summary>Checks an op against the game's op table: known op, every key a parameter, right types, nothing missing.</summary>
    public static void Validate(JObject op, int index = 0)
    {
        var where = index > 0 ? $"op #{index}" : "op";
        var name = op.Str("op") ?? throw new MapKitException($"{where}: no \"op\" key");
        var method = MapFile.OpBuilder(name, op.Has("name")) ?? throw new MapKitException($"{where}: unknown op \"{name}\" (known: {string.Join(", ", MapFile.OpNames)})");
        var ps = method.GetParameters();
        foreach (var kv in op.Items)
        {
            if (OpExtras.Contains(kv.Key))
            {
                if (kv.Key.Equals("anchor", StringComparison.OrdinalIgnoreCase) && !(kv.Value is JString a && Enum.TryParse<OpAnchor>(a.Value, true, out _)))
                    throw new MapKitException($"{where} ({name}): \"anchor\" is ground, drape, world or auto");
                continue;
            }
            var p = ps.FirstOrDefault(q => string.Equals(q.Name, kv.Key, StringComparison.OrdinalIgnoreCase))
                    ?? throw new MapKitException($"{where} ({method.Name}): no parameter \"{kv.Key}\" (it takes: {string.Join(", ", ps.Select(q => q.Name))})");
            if (!Fits(kv.Value, p.ParameterType))
                throw new MapKitException($"{where} ({method.Name}.{p.Name}): {Describe(kv.Value)} isn't a {MapFile.TypeNameOf(p.ParameterType)}");
        }
        foreach (var p in ps)
            if (!p.HasDefaultValue && p.ParameterType != typeof(Map.Hole[]) && !op.Items.Any(kv => string.Equals(kv.Key, p.Name, StringComparison.OrdinalIgnoreCase)))
                throw new MapKitException($"{where} ({method.Name}): needs \"{p.Name}\"");
    }

    private static bool Fits(JNode v, Type t)
    {
        if (t == typeof(float))
            return v is JNumber;
        if (t == typeof(int))
            return v is JNumber { IsInt: true };
        if (t == typeof(bool))
            return v is JBool;
        if (t == typeof(bool?))
            return v is JBool or JNull;
        if (t == typeof(string))
            return v is JString;
        if (t == typeof(string[]))
            return v is JString || v is JArray a && a.Items.All(i => i is JString);
        if (t == typeof(Vector3))
            return v is JString { Value: { Length: 7 } s } && s[0] == '#' || v is JArray { Count: 3 } c && c.Items.All(i => i is JNumber);
        if (t == typeof(Map.Hole[]))
            return v is JArray h && h.Items.All(i => i is JObject or JArray { Count: 4 });
        if (t == typeof(float[]))
            return v is JArray f && f.Items.All(i => i is JNumber);
        if (t == typeof(MapJson?) || t == typeof(MapJson))
            return v is JObject or JNull;
        if (Nullable.GetUnderlyingType(t) == typeof(ValueTuple<float, float, float, float>))
            return v is JNull or JObject or JArray { Count: 4 };
        return false;
    }

    private static string Describe(JNode v) => v switch
    {
        JNumber { IsInt: true } n => $"the whole number {n.Value}",
        JNumber n => $"the number {JNode.PyFloat(n.Value)}",
        JString s => $"the text \"{s.Value}\"",
        JBool b => b.Value ? "true" : "false",
        JArray => "a list",
        JObject => "an object",
        _ => "null",
    };

    // ---- solids ----------------------------------------------------------------------------------------------------

    /// <summary>A drawn solid box between two corners (any order). Boxes thinner than a centimetre are skipped (counted in Stats["skipped"]).</summary>
    public JObject? Block(double x0, double y0, double z0, double x1, double y1, double z1, Color color, string? anchor = null)
    {
        if (Math.Abs(x1 - x0) < 0.01 || Math.Abs(y1 - y0) < 0.01 || Math.Abs(z1 - z0) < 0.01)
        {
            Count("skipped");
            return null;
        }
        Count("blocks");
        var o = new JObject(("op", "Block"), ("x0", R(Math.Min(x0, x1))), ("y0", R(Math.Min(y0, y1))), ("z0", R(Math.Min(z0, z1))),
            ("x1", R(Math.Max(x0, x1))), ("y1", R(Math.Max(y0, y1))), ("z1", R(Math.Max(z0, z1))));
        o.Add("color", color.Node);
        if (anchor != null)
            o.Add("anchor", anchor);
        return Emit(o);
    }

    /// <summary>A hidden solid box: collision, bullets and bot sight, never drawn (put a cosmetic Mesh over it).</summary>
    public JObject? Collider(double x0, double y0, double z0, double x1, double y1, double z1, string? anchor = null)
    {
        if (Math.Abs(x1 - x0) < 0.02 || Math.Abs(y1 - y0) < 0.02 || Math.Abs(z1 - z0) < 0.02)
        {
            Count("skipped");
            return null;
        }
        Count("colliders");
        var o = new JObject(("op", "Collider"), ("x0", R(Math.Min(x0, x1))), ("y0", R(Math.Min(y0, y1))), ("z0", R(Math.Min(z0, z1))),
            ("x1", R(Math.Max(x0, x1))), ("y1", R(Math.Max(y0, y1))), ("z1", R(Math.Max(z0, z1))));
        if (anchor != null)
            o.Add("anchor", anchor);
        return Emit(o);
    }

    /// <summary>Three hidden boxes approximating an upright cylinder of radius <paramref name="rad"/>.</summary>
    public void RoundCollider(double x, double z, double rad, double y0, double y1)
    {
        double a = rad * 0.96, b = rad * 0.41, c = rad * 0.70;
        Collider(x - a, y0, z - b, x + a, y1, z + b);
        Collider(x - b, y0, z - a, x + b, y1, z + a);
        Collider(x - c, y0, z - c, x + c, y1, z + c);
    }

    /// <summary>A cosmetic mesh standing on its lowest point at (x, y, z) (bullets and players pass through unless solid).</summary>
    public JObject Mesh(string kind, double x, double z, double y, Color color, double? width = null, double? height = null, double? depth = null,
        double? radius = null, double? inner = null, double? yaw = null, double? pitch = null, double? roll = null, int? segments = null, int? detail = null,
        double? scale = null, string? material = null, bool solid = false, string? anchor = null)
    {
        Count("meshes");
        var o = new JObject(("op", "Mesh"), ("mesh", kind), ("x", R(x)), ("z", R(z)), ("y", R(y)));
        o.Add("color", color.Node);
        if (width != null) o.Add("width", R(width.Value));
        if (height != null) o.Add("height", R(height.Value));
        if (depth != null) o.Add("depth", R(depth.Value));
        if (radius != null) o.Add("radius", R(radius.Value));
        if (inner != null) o.Add("inner", R(inner.Value));
        if (yaw != null && Math.Abs(yaw.Value) > 1e-9) o.Add("yaw", R(yaw.Value));
        if (pitch != null && Math.Abs(pitch.Value) > 1e-9) o.Add("pitch", R(pitch.Value));
        if (roll != null && Math.Abs(roll.Value) > 1e-9) o.Add("roll", R(roll.Value));
        if (segments != null) o.Add("segments", segments.Value);
        if (detail != null) o.Add("detail", detail.Value);
        if (scale != null) o.Add("scale", R(scale.Value));
        if (material != null) o.Add("material", material);
        if (solid) o.Add("solid", true);
        if (anchor != null) o.Add("anchor", anchor);
        return Emit(o);
    }

    /// <summary>
    /// A mesh placed by its CENTRE whatever its turn: the game turns a shape about its base point, so the base is put
    /// where the turned centre offset lands on the wanted centre.
    /// </summary>
    public JObject MeshAt(string kind, double cx, double cy, double cz, Color color, double yaw = 0, double pitch = 0, double roll = 0, double? width = null,
        double? height = null, double? depth = null, double? radius = null, double? inner = null, int? segments = null, int? detail = null, string? material = null, bool solid = false)
    {
        var h = height ?? 1.0;
        var inn = inner ?? 0.0;
        if (kind is "torus" or "ring" && inn == 0)
            inn = 0.1;
        var rest = kind is "torus" or "ring" ? new Vector3(0, (float)inn, 0) : new Vector3(0, (float)(h / 2), 0);
        var off = Geometry.Turn(rest, yaw, pitch, roll);
        return Mesh(kind, cx - off.X, cz - off.Z, cy - off.Y, color, width, height, depth, radius, inner, yaw, pitch, roll, segments, detail, null, material, solid);
    }

    // ---- walls, slabs, stairs, ramps (the game's own ops) --------------------------------------------------------------

    /// <summary>A hole in a wall: centre along the wall, width, bottom and top heights.</summary>
    public static JObject Hole(double center, double width, double bottom, double top) =>
        new(("center", R(center)), ("width", R(width)), ("bottom", R(bottom)), ("top", R(top)));

    public static JObject Door(double center, double width = 2.2, double height = 2.4, double y0 = 0) => Hole(center, width, y0, y0 + height);

    public static JObject Window(double center, double width = 1.6, double y0 = 1.0, double y1 = 2.2) => Hole(center, width, y0, y1);

    /// <summary>A wall along X from x0 to x1 filling z..z+t, with holes (centres are absolute x).</summary>
    public JObject WallX(double x0, double x1, double z, double t, double y0, double y1, Color color, params JObject[] holes)
    {
        Count("blocks", 1 + 2 * holes.Length);
        var o = new JObject(("op", "WallX"), ("x0", R(x0)), ("x1", R(x1)), ("z", R(z)), ("t", R(t)), ("y0", R(y0)), ("y1", R(y1)));
        o.Add("color", color.Node);
        if (holes.Length > 0)
            o.Add("holes", new JArray(holes));
        return Emit(o);
    }

    public JObject WallZ(double z0, double z1, double x, double t, double y0, double y1, Color color, params JObject[] holes)
    {
        Count("blocks", 1 + 2 * holes.Length);
        var o = new JObject(("op", "WallZ"), ("z0", R(z0)), ("z1", R(z1)), ("x", R(x)), ("t", R(t)), ("y0", R(y0)), ("y1", R(y1)));
        o.Add("color", color.Node);
        if (holes.Length > 0)
            o.Add("holes", new JArray(holes));
        return Emit(o);
    }

    /// <summary>A floor or roof slab whose top is at yTop, optionally with one rectangular hole (x0, z0, x1, z1).</summary>
    public JObject Slab(double x0, double z0, double x1, double z1, double yTop, double thick, Color color, (double X0, double Z0, double X1, double Z1)? hole = null)
    {
        Count("blocks", hole == null ? 1 : 4);
        var o = new JObject(("op", "Slab"), ("x0", R(Math.Min(x0, x1))), ("z0", R(Math.Min(z0, z1))), ("x1", R(Math.Max(x0, x1))), ("z1", R(Math.Max(z0, z1))),
            ("yTop", R(yTop)), ("thick", R(thick)));
        o.Add("color", color.Node);
        if (hole is { } h)
            o.Add("hole", new JObject(("x0", R(Math.Min(h.X0, h.X1))), ("z0", R(Math.Min(h.Z0, h.Z1))), ("x1", R(Math.Max(h.X0, h.X1))), ("z1", R(Math.Max(h.Z0, h.Z1)))));
        return Emit(o);
    }

    /// <summary>The game's solid staircase along Z (0.4 m risers, 0.45 m treads) from z climbing towards dir. Returns where the top step ends.</summary>
    public double StairsZ(double x0, double x1, double z, int dir, double y0, double height, Color color, string? anchor = null)
    {
        Structures.CheckFlightWidth(Math.Abs(x1 - x0), "StairsZ");
        var n = (int)Math.Ceiling(height / Map.StepRise - 1e-6);
        Count("blocks", n);
        var o = new JObject(("op", "StairsZ"), ("x0", R(Math.Min(x0, x1))), ("x1", R(Math.Max(x0, x1))), ("z", R(z)), ("dir", dir), ("y0", R(y0)), ("height", R(height)));
        o.Add("color", color.Node);
        if (anchor != null) o.Add("anchor", anchor);
        Emit(o);
        return z + dir * n * Map.StepRun;
    }

    public double StairsX(double z0, double z1, double x, int dir, double y0, double height, Color color, string? anchor = null)
    {
        Structures.CheckFlightWidth(Math.Abs(z1 - z0), "StairsX");
        var n = (int)Math.Ceiling(height / Map.StepRise - 1e-6);
        Count("blocks", n);
        var o = new JObject(("op", "StairsX"), ("z0", R(Math.Min(z0, z1))), ("z1", R(Math.Max(z0, z1))), ("x", R(x)), ("dir", dir), ("y0", R(y0)), ("height", R(height)));
        o.Add("color", color.Node);
        if (anchor != null) o.Add("anchor", anchor);
        Emit(o);
        return x + dir * n * Map.StepRun;
    }

    /// <summary>The game's Ramp op: a long ramp of small steps from (x, z) in direction dir, up to a platform.</summary>
    public JObject Ramp(double x, double z, double width, double height, bool alongX, int dir, Color color, double y0 = 0, string? anchor = null)
    {
        Count("blocks", (int)Math.Ceiling(height / 0.5));
        var o = new JObject(("op", "Ramp"), ("x", R(x)), ("z", R(z)), ("width", R(width)), ("height", R(height)), ("alongX", alongX), ("dir", dir));
        o.Add("color", color.Node);
        if (y0 != 0) o.Add("y0", R(y0));
        if (anchor != null) o.Add("anchor", anchor);
        return Emit(o);
    }

    /// <summary>Boundary walls round a rectangular map's edge.</summary>
    public JObject Perimeter(double height, Color color, double t = 1)
    {
        var o = new JObject(("op", "Perimeter"), ("height", R(height)));
        o.Add("color", color.Node);
        if (t != 1) o.Add("t", R(t));
        return Emit(o);
    }

    public JObject Ground(Color color)
    {
        var o = new JObject(("op", "Ground"));
        o.Add("color", color.Node);
        return Emit(o);
    }

    public JObject AutoWaypoints(double spacing) => Emit(new JObject(("op", "AutoWaypoints"), ("spacing", spacing == Math.Floor(spacing) ? (object)(int)spacing : (object)R(spacing))));

    // ---- props, decals, waypoints, spawns, buildings ------------------------------------------------------------------

    /// <summary>A prop from the Props folder standing at (x, z) (y above the ground), turned yaw degrees.</summary>
    public JObject Prop(string name, double x, double z, double y = 0, double yaw = 0, string? anchor = null, params (string Key, object Value)[] more)
    {
        Count("props");
        var o = new JObject(("op", "Prop"), ("name", name), ("x", R(x)), ("z", R(z)));
        if (y != 0) o.Add("y", R(y));
        if (Yaw(yaw) != 0) o.Add("yaw", Yaw(yaw));
        if (anchor != null) o.Add("anchor", anchor);
        foreach (var (k, v) in more)
            o.Add(k, v);
        return Emit(o);
    }

    /// <summary>Props from (name, x, z, yaw) tuples, all at height y.</summary>
    public void Props(IEnumerable<(string Name, double X, double Z, double Yaw)> items, double y = 0)
    {
        foreach (var (n, x, z, yaw) in items)
            Prop(n, x, z, y, yaw);
    }

    /// <summary>
    /// A decal at (x, z, y) on a surface: "floor", "ceiling", "wall" (the nearest), or a wall by the way it faces
    /// ("wall+x" faces +X and is hit from a point east of it, "wall-z" faces north...). Rotation is degrees on the surface.
    /// </summary>
    public JObject Decal(string name, double x, double z, double y = 0, string onto = "floor", double? size = null, double? rotation = null, double? reach = null,
        string? tint = null, int? seed = null, string? anchor = null)
    {
        Count("decals");
        var o = new JObject(("op", "Decal"), ("decal", name), ("x", R(x)), ("z", R(z)), ("y", R(y)), ("onto", onto));
        if (size != null) o.Add("size", R(size.Value));
        if (rotation != null) o.Add("rotation", R(rotation.Value, 1));
        if (reach != null) o.Add("reach", R(reach.Value));
        if (tint != null) o.Add("tint", tint);
        if (seed != null) o.Add("seed", seed.Value);
        if (anchor != null) o.Add("anchor", anchor);
        return Emit(o);
    }

    /// <summary>A decal on the wall that faces <paramref name="facing"/> (+x, -x, +z or -z), <paramref name="standOff"/> metres out from the wall face at (x, z).</summary>
    public JObject WallDecal(string name, double x, double z, double y, string facing, double size, double standOff = 0.6, double? rotation = 0, double reach = 1.5)
    {
        var (dx, dz) = facing switch { "+x" => (1.0, 0.0), "-x" => (-1.0, 0.0), "+z" => (0.0, 1.0), "-z" => (0.0, -1.0), _ => throw new MapKitException($"facing is +x, -x, +z or -z, not \"{facing}\"") };
        return Decal(name, x + dx * standOff, z + dz * standOff, y, "wall" + facing, size, rotation, reach);
    }

    /// <summary>
    /// A sign cut into square tiles laid side by side on a wall facing <paramref name="facing"/>, centred on (cx, cz) at
    /// height y, reading left to right as seen from the side the wall faces.
    /// </summary>
    public void WallSign(IReadOnlyList<string> tiles, double tileSize, double cx, double cz, double y, string facing, double standOff = 0.6, double reach = 1.5)
    {
        var n = tiles.Count;
        for (var i = 0; i < n; i++)
        {
            var off = (i - (n - 1) / 2.0) * tileSize;
            // A viewer facing the wall: their right is the wall's facing turned 90 degrees anticlockwise seen from above.
            var (rx, rz) = facing switch { "+z" => (-1.0, 0.0), "-z" => (1.0, 0.0), "+x" => (0.0, 1.0), "-x" => (0.0, -1.0), _ => throw new MapKitException($"facing is +x, -x, +z or -z, not \"{facing}\"") };
            WallDecal(tiles[i], cx + rx * off, cz + rz * off, y, facing, tileSize, standOff, 0, reach);
        }
    }

    /// <summary>Decals scattered over a rectangle (seeded).</summary>
    public JObject DecalScatter(IEnumerable<string> decals, double x0, double z0, double x1, double z1, int count, int seed, double y = 0, string? onto = null, double? gap = null)
    {
        Count("decals", count);
        var o = new JObject(("op", "DecalScatter"), ("decals", decals.ToArray()), ("x0", R(Math.Min(x0, x1))), ("z0", R(Math.Min(z0, z1))), ("x1", R(Math.Max(x0, x1))), ("z1", R(Math.Max(z0, z1))),
            ("count", count), ("seed", seed));
        if (onto != null) o.Add("onto", onto);
        if (y != 0) o.Add("y", R(y));
        if (gap != null) o.Add("gap", R(gap.Value));
        return Emit(o);
    }

    /// <summary>Props scattered over a rectangle, keeping out of everything (runs last in the game); mirror: the same again at (-x, -z).</summary>
    public JObject Clutter(IEnumerable<string> props, double x0, double z0, double x1, double z1, int count, int seed, bool mirror = false, double? gap = null)
    {
        var o = new JObject(("op", "Clutter"), ("props", props.ToArray()), ("x0", R(Math.Min(x0, x1))), ("z0", R(Math.Min(z0, z1))), ("x1", R(Math.Max(x0, x1))), ("z1", R(Math.Max(z0, z1))),
            ("count", count), ("seed", seed));
        if (mirror) o.Add("mirror", true);
        if (gap != null) o.Add("gap", R(gap.Value));
        return Emit(o);
    }

    public JObject Waypoint(double x, double z, double y = 0, string? anchor = null)
    {
        Count("waypoints");
        var o = new JObject(("op", "Waypoint"), ("x", R(x)), ("z", R(z)));
        if (y != 0) o.Add("y", R(y));
        if (anchor != null) o.Add("anchor", anchor);
        return Emit(o);
    }

    /// <summary>Waypoints over a rectangle at height y, `spacing` apart, starting half a step in; keep(x, z) filters.</summary>
    public void WaypointGrid(double x0, double z0, double x1, double z1, double y, double spacing = 6.0, Func<double, double, bool>? keep = null)
    {
        var nx = Math.Max(1, (int)Math.Round((x1 - x0) / spacing));
        var nz = Math.Max(1, (int)Math.Round((z1 - z0) / spacing));
        for (var i = 0; i < nx; i++)
            for (var j = 0; j < nz; j++)
            {
                var x = x0 + (x1 - x0) * (i + 0.5) / nx;
                var z = z0 + (z1 - z0) * (j + 0.5) / nz;
                if (keep == null || keep(x, z))
                    Waypoint(x, z, y);
            }
    }

    /// <summary>A spawn point for team 0 (Blue) or 1 (Red), recorded for "teamSpawns" too.</summary>
    public JObject Spawn(int team, double x, double z, double y = 0)
    {
        if (team is not (0 or 1))
            throw new MapKitException("team is 0 (Blue) or 1 (Red)");
        Count("spawns");
        TeamSpawns[team].Add([R(x), R(y), R(z)]);
        var o = new JObject(("op", "Spawn"), ("x", R(x)), ("z", R(z)));
        if (y != 0) o.Add("y", R(y));
        return Emit(o);
    }

    /// <summary>A furnished building from the Buildings folder, its centre at (x, z) (y above the ground).</summary>
    public JObject Building(string name, double x, double z, double y = 0, double yaw = 0, int seed = 1, bool spawns = false, JObject? variant = null)
    {
        Count("buildings");
        var o = new JObject(("op", "Building"), ("name", name), ("at", y != 0 ? JArray.Of(R(x), R(y), R(z)) : JArray.Of(R(x), R(z))), ("yaw", (int)Yaw(yaw)), ("seed", seed));
        if (spawns) o.Add("spawns", true);
        if (variant != null) o.Add("variant", variant);
        return Emit(o);
    }

    public JObject Rocks(double x, double z, double size, Color color, int seed)
    {
        var o = new JObject(("op", "Rocks"), ("x", R(x)), ("z", R(z)), ("size", R(size)));
        o.Add("color", color.Node);
        o.Add("seed", seed);
        return Emit(o);
    }

    // ---- objectives --------------------------------------------------------------------------------------------------

    public void Hill(double x, double z, double radius, double? y = null)
    {
        var h = new JObject(("x", R(x)), ("z", R(z)));
        if (y != null) h.Add("y", R(y.Value));
        h.Add("radius", R(radius));
        Hills.Add(h);
    }

    /// <summary>The two flags, Blue's first ([x, z] or [x, y, z]).</summary>
    public void Flag(double x, double z, double? y = null) => Flags.Add(y == null ? [R(x), R(z)] : [R(x), R(y.Value), R(z)]);

    // ---- symmetry ----------------------------------------------------------------------------------------------------

    /// <summary>A point's twin.</summary>
    public static (double X, double Z) TwinPoint(double x, double z, Symmetry sym) => sym switch
    {
        Symmetry.Turn180 => (-x, -z),
        Symmetry.MirrorX => (-x, z),
        Symmetry.MirrorZ => (x, -z),
        _ => (x, z),
    };

    /// <summary>A yaw's twin (props and meshes alike: the front faces the twinned way).</summary>
    public static double TwinYaw(double yaw, Symmetry sym) => sym switch
    {
        Symmetry.Turn180 => yaw + 180,
        Symmetry.MirrorX => -yaw,
        Symmetry.MirrorZ => 180 - yaw,
        _ => yaw,
    };

    private static readonly Dictionary<string, string> FlipX = new(StringComparer.OrdinalIgnoreCase) { ["wall+x"] = "wall-x", ["wall-x"] = "wall+x" };
    private static readonly Dictionary<string, string> FlipZ = new(StringComparer.OrdinalIgnoreCase) { ["wall+z"] = "wall-z", ["wall-z"] = "wall+z" };

    /// <summary>The surface a decal lands on, twinned (a wall facing +x faces -x after a turn or an x mirror).</summary>
    public static string TwinOnto(string onto, Symmetry sym)
    {
        if (sym is Symmetry.Turn180 or Symmetry.MirrorX && FlipX.TryGetValue(onto, out var fx))
            return fx;
        if (sym is Symmetry.Turn180 or Symmetry.MirrorZ && FlipZ.TryGetValue(onto, out var fz))
            return fz;
        return onto;
    }

    /// <summary>
    /// An op's twin under a symmetry: positions, rectangles, yaws, decal surfaces and turns, stair and ramp directions,
    /// wall thickness sides, building yaws (mirrors add a "mirror" variant). Scatters, clutter and rocks get their seed
    /// offset by <paramref name="reseed"/> so the two halves' litter isn't a copy. Null for ops that have no twin (Ground,
    /// Perimeter, AutoWaypoints, Clutter with mirror, planet ops); throws for an op it doesn't know how to turn.
    /// </summary>
    public static JObject? Twin(JObject op, Symmetry sym, int reseed = 0)
    {
        if (sym == Symmetry.None)
            return null;
        var name = op.Str("op") ?? throw new MapKitException("an op without \"op\"");
        var d = op.Clone();
        bool fx = sym is Symmetry.Turn180 or Symmetry.MirrorX, fz = sym is Symmetry.Turn180 or Symmetry.MirrorZ;

        void Rect(string ax0, string ax1, string az0, string az1)
        {
            if (fx) (d[ax0], d[ax1]) = (Neg(op[ax1]), Neg(op[ax0]));
            if (fz) (d[az0], d[az1]) = (Neg(op[az1]), Neg(op[az0]));
        }

        void Point(string kx, string kz)
        {
            if (fx && op.Has(kx)) d[kx] = Neg(op[kx]);
            if (fz && op.Has(kz)) d[kz] = Neg(op[kz]);
        }

        void YawKey(string key, bool required)
        {
            var had = op.Get(key) is JNumber;
            if (!had && !required)
                return;
            var yaw = had ? op.Num(key) : 0;
            var t = Yaw(TwinYaw(yaw, sym));
            if (t == 0 && !had)
                return;
            d.Set(key, t);
        }

        void Seed()
        {
            if (reseed != 0 && op.Get("seed") is JNumber s)
                d.Set("seed", (long)s.Value + reseed);
        }

        switch (name.ToLowerInvariant())
        {
            case "ground":
            case "perimeter":
            case "autowaypoints":
            case "planet":
            case "planetwaypoints":
            case "surfacescatter":
                return null;
            case "block":
            case "collider":
                Rect("x0", "x1", "z0", "z1");
                break;
            case "mesh":
            case "prop":
            case "waypoint":
            case "spawn":
            case "tree":
            case "treeifclear":
            case "watchtower":
                Point("x", "z");
                if (name.Equals("mesh", StringComparison.OrdinalIgnoreCase) || name.Equals("prop", StringComparison.OrdinalIgnoreCase))
                    YawKey("yaw", required: false);
                if (name.Equals("mesh", StringComparison.OrdinalIgnoreCase) && sym != Symmetry.Turn180 && op.Get("roll") is JNumber rl)
                    d.Set("roll", R(-rl.Value));
                if (name.Equals("watchtower", StringComparison.OrdinalIgnoreCase) && fz && op.Get("stairDir") is JNumber sd)
                    d.Set("stairDir", -(long)sd.Value);
                break;
            case "decal":
                Point("x", "z");
                if (op.Get("onto") is JString onto)
                    d.Set("onto", TwinOnto(onto.Value, sym));
                if (op.Get("rotation") is JNumber rot && rot.Value >= 0)
                {
                    var flat = (op.Str("onto") ?? "floor") is "floor" or "ceiling";
                    var t = sym == Symmetry.Turn180 ? rot.Value + 180 : !flat ? rot.Value : sym == Symmetry.MirrorX ? -rot.Value : 180 - rot.Value;
                    d.Set("rotation", R(((t % 360) + 360) % 360, 1));
                }
                break;
            case "decalscatter":
                Rect("x0", "x1", "z0", "z1");
                Seed();
                break;
            case "clutter":
                if (op.Get("mirror") is JBool { Value: true })
                    return null; // already covers both halves
                Rect("x0", "x1", "z0", "z1");
                Seed();
                break;
            case "rocks":
                Point("x", "z");
                Seed();
                break;
            case "building":
                if (op.Get("at") is JArray at)
                {
                    var p = at.Items.Select(n => ((JNumber)n).Value).ToArray();
                    var (tx, tz) = TwinPoint(p[0], p[^1], sym);
                    d.Set("at", p.Length == 3 ? JArray.Of(R(tx), p[1], R(tz)) : JArray.Of(R(tx), R(tz)));
                }
                else
                    Point("cx", "cz");
                if (op.Get("yaw") is JNumber by)
                    d.Set("yaw", (long)Yaw(TwinYaw(by.Value, sym)));
                else if (sym != Symmetry.MirrorX)
                    d.Set("yaw", (long)Yaw(TwinYaw(0, sym)));
                if (sym != Symmetry.Turn180 && op.Has("name"))
                {
                    var v = op.Get("variant") is JObject vo ? vo.Clone() : new JObject();
                    if (v.Get("mirror") is JBool { Value: true })
                        v.Remove("mirror"); // mirrored twice is the original
                    else
                        v.Set("mirror", true);
                    if (v.Count == 0)
                        d.Remove("variant");
                    else
                        d.Set("variant", v);
                }
                if (op.Get("doors") is JString doors && !op.Has("name"))
                    d.Set("doors", new string(doors.Value.Select(c => c switch { 'N' when fz => 'S', 'S' when fz => 'N', 'E' when fx => 'W', 'W' when fx => 'E', _ => c }).ToArray()));
                break;
            case "hangar":
                Point("cx", "cz");
                break;
            case "bridge":
                Point("x0", "z0");
                Point("x1", "z1");
                break;
            case "wallx":
                if (fx) (d["x0"], d["x1"]) = (Neg(op["x1"]), Neg(op["x0"]));
                if (fz) d["z"] = new JNumber(R(-op.Num("z") - op.Num("t")));
                if (fx && op.Get("holes") is JArray hx)
                    d.Set("holes", new JArray(hx.Items.Select(h => FlipHole(h))));
                break;
            case "wallz":
                if (fz) (d["z0"], d["z1"]) = (Neg(op["z1"]), Neg(op["z0"]));
                if (fx) d["x"] = new JNumber(R(-op.Num("x") - op.Num("t")));
                if (fz && op.Get("holes") is JArray hz)
                    d.Set("holes", new JArray(hz.Items.Select(h => FlipHole(h))));
                break;
            case "slab":
                Rect("x0", "x1", "z0", "z1");
                if (op.Get("hole") is JObject hole)
                {
                    var h2 = hole.Clone();
                    if (fx) (h2["x0"], h2["x1"]) = (Neg(hole["x1"]), Neg(hole["x0"]));
                    if (fz) (h2["z0"], h2["z1"]) = (Neg(hole["z1"]), Neg(hole["z0"]));
                    d.Set("hole", h2);
                }
                break;
            case "stairsz":
                if (fx) (d["x0"], d["x1"]) = (Neg(op["x1"]), Neg(op["x0"]));
                if (fz)
                {
                    d["z"] = Neg(op["z"]);
                    d.Set("dir", -(long)op.Num("dir"));
                }
                break;
            case "stairsx":
                if (fz) (d["z0"], d["z1"]) = (Neg(op["z1"]), Neg(op["z0"]));
                if (fx)
                {
                    d["x"] = Neg(op["x"]);
                    d.Set("dir", -(long)op.Num("dir"));
                }
                break;
            case "ramp":
                Point("x", "z");
                if (op.Get("alongX") is JBool ax && (ax.Value ? fx : fz))
                    d.Set("dir", -(long)op.Num("dir"));
                break;
            case "hill":
            case "container":
            case "ringspawns":
                Point("cx", "cz");
                Point("x", "z");
                break;
            default:
                throw new MapKitException($"Twin: don't know how to turn a {name} op");
        }
        return d;
    }

    private static JNode Neg(JNode n) => n is JNumber { IsInt: true } i ? new JNumber(-(long)i.Value) : new JNumber(R(-((JNumber)n).Value));

    private static JNode FlipHole(JNode h) => h switch
    {
        JObject o => Flipped(o),
        JArray a => new JArray([Neg(a[0]), a[1], a[2], a[3]]),
        _ => h,
    };

    private static JObject Flipped(JObject hole)
    {
        var c = hole.Clone();
        if (hole.Has("center"))
            c["center"] = Neg(hole["center"]);
        return c;
    }

    /// <summary>The twins of a list of ops, in order (ops without a twin are left out).</summary>
    public static List<JObject> Twins(IEnumerable<JObject> ops, Symmetry sym, int reseed = 0) =>
        ops.Select(o => Twin(o, sym, reseed)).Where(o => o != null).Select(o => o!).ToList();

    /// <summary>
    /// Builds Red's half from Blue's: runs <paramref name="half"/>, then appends the twin of every op it added, mirrors the
    /// spawns it recorded for team 0 into team 1, and the hills and flags added into their twins (a hill on the symmetry
    /// line stays single). The twins come after the whole half, so the file reads as "Blue's side, then Red's".
    /// </summary>
    public void Symmetric(Symmetry sym, Action half, int reseed = 100)
    {
        int ops0 = Ops.Count, spawns0 = TeamSpawns[0].Count, hills0 = Hills.Count, flags0 = Flags.Count;
        half();
        var twins = Twins(Ops.Skip(ops0).ToList(), sym, reseed);
        foreach (var t in twins)
            Emit(t);
        for (var i = spawns0; i < TeamSpawns[0].Count; i++)
        {
            var s = TeamSpawns[0][i];
            var (x, z) = TwinPoint(s[0], s[2], sym);
            TeamSpawns[1].Add([R(x), s[1], R(z)]);
        }
        var newHills = Hills.Skip(hills0).ToList();
        foreach (var h in newHills)
        {
            var (x, z) = TwinPoint(h.Num("x"), h.Num("z"), sym);
            if (Math.Abs(x - h.Num("x")) < 1e-6 && Math.Abs(z - h.Num("z")) < 1e-6)
                continue;
            var t = h.Clone();
            t.Set("x", R(x));
            t.Set("z", R(z));
            Hills.Add(t);
        }
        var newFlags = Flags.Skip(flags0).ToList();
        foreach (var f in newFlags)
        {
            var (x, z) = TwinPoint(f[0], f[^1], sym);
            Flags.Add(f.Length == 3 ? [R(x), f[1], R(z)] : [R(x), R(z)]);
        }
        Count("spawns", TeamSpawns[0].Count - spawns0);
    }

    /// <summary>A terrain feature's twin: x, z and every point turned; a cliff's side stays (Maps/README.md, "Fair terrain").</summary>
    public static JObject TwinFeature(JObject feature, Symmetry sym)
    {
        var g = feature.Clone();
        if (feature.Has("x") && feature.Has("z"))
        {
            var (x, z) = TwinPoint(feature.Num("x"), feature.Num("z"), sym);
            g["x"] = feature["x"] is JNumber { IsInt: true } ? new JNumber((long)x) : new JNumber(R(x));
            g["z"] = feature["z"] is JNumber { IsInt: true } ? new JNumber((long)z) : new JNumber(R(z));
        }
        if (feature.Get("points") is JArray pts)
            g["points"] = new JArray(pts.Items.Select(p =>
            {
                var a = (JArray)p;
                var (x, z) = TwinPoint(((JNumber)a[0]).Value, ((JNumber)a[1]).Value, sym);
                var q = new JArray([a[0] is JNumber { IsInt: true } ? new JNumber((long)x) : new JNumber(R(x)), a[1] is JNumber { IsInt: true } ? new JNumber((long)z) : new JNumber(R(z))]);
                for (var i = 2; i < a.Count; i++)
                    q.Add(a[i]);
                return (JNode)q;
            }));
        return g;
    }

    /// <summary>A gravity zone's twin (box min/max or sphere centre, the field's centre and direction), renamed <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static JObject TwinZone(JObject zone, Symmetry sym, string? from = null, string? to = null)
    {
        var g = zone.Clone();
        JArray P3(JArray a)
        {
            var (x, z) = TwinPoint(((JNumber)a[0]).Value, ((JNumber)a[2]).Value, sym);
            return new JArray([a[0] is JNumber { IsInt: true } ? new JNumber((long)x) : new JNumber(R(x)), a[1], a[2] is JNumber { IsInt: true } ? new JNumber((long)z) : new JNumber(R(z))]);
        }
        if (zone.Get("min") is JArray mn && zone.Get("max") is JArray mx)
        {
            var a = P3(mn);
            var b = P3(mx);
            g["min"] = new JArray([Min(a[0], b[0]), a[1], Min(a[2], b[2])]);
            g["max"] = new JArray([Max(a[0], b[0]), b[1], Max(a[2], b[2])]);
        }
        if (zone.Get("center") is JArray c)
            g["center"] = P3(c);
        if (zone.Get("field") is JObject f)
        {
            var f2 = f.Clone();
            if (f.Get("center") is JArray fc)
                f2["center"] = P3(fc);
            if (f.Get("direction") is JArray dir)
                f2["direction"] = P3(dir);
            g["field"] = f2;
        }
        if (from != null && to != null && zone.Str("name") is { } nm)
            g["name"] = new JString(nm.Replace(from, to));
        return g;
    }

    private static JNode Min(JNode a, JNode b) => ((JNumber)a).Value <= ((JNumber)b).Value ? a : b;
    private static JNode Max(JNode a, JNode b) => ((JNumber)a).Value >= ((JNumber)b).Value ? a : b;

    // ---- the file ----------------------------------------------------------------------------------------------------

    private static readonly string[] TopOrder =
    [
        "format", "name", "description", "author", "order", "prototypeGrid", "penetration", "destructible", "fallKill", "maxPlayers", "test", "flagPool",
        "halfX", "halfZ", "sky", "skybox", "boundary", "gravity", "terrain", "teamSpawns", "hills", "flags", "foliage", "movers", "zombies", "logic",
    ];

    /// <summary>
    /// The map's "test" block for the "Map contract" self-test and --check-map (shape, dressing floors, symmetry, fair
    /// walks, a short bot match...; Maps/README.md, "Testing a map"). Not part of the map's fingerprint.
    /// </summary>
    public Kit Test(JObject spec)
    {
        Top.Set("test", spec);
        return this;
    }

    /// <summary>The whole map file as a JSON tree: the top-level fields in the usual order, the objectives, then the ops.</summary>
    public JObject ToDocument()
    {
        if (_logic is { } logic && !logic.IsEmpty)
            Top.Set("logic", logic.ToJson());
        var doc = new JObject();
        var objectives = new JObject();
        if (TeamSpawns[0].Count + TeamSpawns[1].Count > 0 && !Top.Has("teamSpawns"))
            objectives.Add("teamSpawns", new JArray(TeamSpawns.Select(t => (JNode)new JArray(t.Select(s => (JNode)JArray.Of(s.Cast<object>().ToArray()))))));
        if (Hills.Count > 0 && !Top.Has("hills"))
            objectives.Add("hills", new JArray(Hills));
        if (Flags.Count > 0 && !Top.Has("flags"))
            objectives.Add("flags", new JArray(Flags.Select(f => (JNode)JArray.Of(f.Cast<object>().ToArray()))));
        foreach (var key in TopOrder)
        {
            if (Top.Get(key) is { } v)
                doc.Add(key, v);
            else if (objectives.Get(key) is { } o)
                doc.Add(key, o);
        }
        foreach (var kv in Top.Items)
            if (!TopOrder.Contains(kv.Key) && kv.Key != "ops")
                doc.Add(kv.Key, kv.Value);
        doc.Add("ops", new JArray(Ops));
        return doc;
    }

    /// <summary>The map file's text (the Python generators' json.dump indent=1 style).</summary>
    public string Write() => ToDocument().Write();

    /// <summary>One line of counts for the console.</summary>
    public string Summary() => $"{Ops.Count} ops, " + string.Join(", ", Stats.Select(kv => $"{kv.Key} {kv.Value}"));
}
