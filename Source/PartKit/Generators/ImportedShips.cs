using System.Numerics;
using System.Text;
using System.Text.Json;
using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Ships designed elsewhere, brought in from a glTF binary (Karl, 2026-11-21: "inputting ship designs from another AI"; the first
/// is the Valkyrie-IV dropship, a three.js page's export). Each folder under tools/imports holds the model (ship.glb) and an
/// import.json that says what its objects are. The model's named objects (a node with meshes of its own: a wall, a door, a seat,
/// a crate) become one part each, in the ship's frame; big ones ("pieces") are split into pieces that break off one at a time.
/// Every mesh of the model is one simple shape (a box, a cylinder, a lofted slab), so each becomes one part box: an axis-aligned
/// box as itself, anything else as a "hull" shape over its corners, with hidden boxes under it for collision where it is
/// structure. Colours come from the model's materials, glow from its emissive ones, glass from its see-through ones.
///
/// What the model can't say comes from import.json: which way is forward, a rise that gives its rooms a person's headroom, and the
/// roles (sliding doors with their leaves, swinging doors such as a ramp or a VTOL pod, seats, a bunk, a med bed, the flight computer,
/// reactors and thrusters), lift jets under the pods, and a switch for the pods. The parts are joined by small hidden tabs, each
/// weld one facing socket pair, in a tree from the root piece out to the nearest part. Notes: docs/sandbox/constructs/imported-ships.md.
/// </summary>
public sealed class ImportedShips : IPartGenerator
{
    public string Name => "imported_ships";

    private const string In = "tools/imports";
    private const string BpOut = "Constructs/Blueprints";

    public void Make(PartOutput o)
    {
        var root = Path.Combine(o.Root, In);
        if (!Directory.Exists(root))
            return;
        foreach (var dir in Directory.GetDirectories(root).Order(StringComparer.Ordinal))
            if (File.Exists(Path.Combine(dir, "import.json")))
                new ShipImport(o, Path.GetRelativePath(o.Root, dir).Replace('\\', '/')).Run();
    }

    // ---- the model

    private sealed class GNode
    {
        public string? Name;
        public int Mesh = -1;
        public Matrix4x4 Local = Matrix4x4.Identity;
        public List<int> Kids = [];
    }

    private sealed class GMat
    {
        public Vector4 Base = Vector4.One;
        public Vector3 Emissive;
        public float Metal, Rough = 1;
        public bool Clear;
    }

    private sealed class Glb
    {
        public readonly List<GNode> Nodes = [];
        public readonly List<GMat> Mats = [];
        public readonly List<List<(Vector3[] P, int Mat)>> Meshes = [];
        public int Root;

        public Glb(byte[] data, string file)
        {
            if (data.Length < 20 || BitConverter.ToUInt32(data, 0) != 0x46546C67)
                throw new InvalidOperationException($"imported_ships: {file} is not a glTF binary");
            var jlen = (int)BitConverter.ToUInt32(data, 12);
            using var doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(data, 20, jlen));
            var j = doc.RootElement;
            var binAt = 20 + jlen;
            var bin = binAt + 8 <= data.Length ? new ReadOnlyMemory<byte>(data, binAt + 8, (int)BitConverter.ToUInt32(data, binAt)) : ReadOnlyMemory<byte>.Empty;
            var views = j.GetProperty("bufferViews").EnumerateArray().Select(v => (Off: v.TryGetProperty("byteOffset", out var bo) ? bo.GetInt32() : 0, Len: v.GetProperty("byteLength").GetInt32(), Stride: v.TryGetProperty("byteStride", out var bs) ? bs.GetInt32() : 0)).ToList();
            var accs = j.GetProperty("accessors").EnumerateArray().ToList();
            float[] Floats(int a, int comps)
            {
                var acc = accs[a];
                var v = views[acc.GetProperty("bufferView").GetInt32()];
                var off = v.Off + (acc.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);
                var n = acc.GetProperty("count").GetInt32();
                var stride = v.Stride > 0 ? v.Stride : comps * 4;
                var span = bin.Span;
                var o = new float[n * comps];
                for (var i = 0; i < n; i++)
                    for (var c = 0; c < comps; c++)
                        o[i * comps + c] = BitConverter.ToSingle(span.Slice(off + i * stride + c * 4, 4));
                return o;
            }
            if (j.TryGetProperty("materials", out var mats))
                foreach (var m in mats.EnumerateArray())
                {
                    var g = new GMat();
                    if (m.TryGetProperty("pbrMetallicRoughness", out var pbr))
                    {
                        if (pbr.TryGetProperty("baseColorFactor", out var bc))
                        {
                            var f = bc.EnumerateArray().Select(x => x.GetSingle()).ToArray();
                            g.Base = new Vector4(f[0], f[1], f[2], f[3]);
                        }
                        g.Metal = pbr.TryGetProperty("metallicFactor", out var mf) ? mf.GetSingle() : 1;
                        g.Rough = pbr.TryGetProperty("roughnessFactor", out var rf) ? rf.GetSingle() : 1;
                    }
                    if (m.TryGetProperty("emissiveFactor", out var ef))
                    {
                        var f = ef.EnumerateArray().Select(x => x.GetSingle()).ToArray();
                        g.Emissive = new Vector3(f[0], f[1], f[2]);
                    }
                    g.Clear = m.TryGetProperty("alphaMode", out var am) && am.GetString() == "BLEND" || g.Base.W < 0.9f;
                    Mats.Add(g);
                }
            foreach (var mesh in j.GetProperty("meshes").EnumerateArray())
            {
                var prims = new List<(Vector3[], int)>();
                foreach (var pr in mesh.GetProperty("primitives").EnumerateArray())
                {
                    var pos = Floats(pr.GetProperty("attributes").GetProperty("POSITION").GetInt32(), 3);
                    var pts = new Vector3[pos.Length / 3];
                    for (var i = 0; i < pts.Length; i++)
                        pts[i] = new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
                    prims.Add((pts, pr.TryGetProperty("material", out var mi) ? mi.GetInt32() : -1));
                }
                Meshes.Add(prims);
            }
            foreach (var n in j.GetProperty("nodes").EnumerateArray())
            {
                var g = new GNode
                {
                    Name = n.TryGetProperty("name", out var nm) && nm.GetString() is { Length: > 0 } s ? s : null,
                    Mesh = n.TryGetProperty("mesh", out var me) ? me.GetInt32() : -1,
                };
                if (n.TryGetProperty("children", out var ch))
                    g.Kids = [.. ch.EnumerateArray().Select(c => c.GetInt32())];
                if (n.TryGetProperty("matrix", out var mx))
                {
                    var f = mx.EnumerateArray().Select(x => x.GetSingle()).ToArray(); // (column-major: glTF; System.Numerics is row-vector, so it reads straight across)
                    g.Local = new Matrix4x4(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15]);
                }
                else
                {
                    var t = n.TryGetProperty("translation", out var tr) ? tr.EnumerateArray().Select(x => x.GetSingle()).ToArray() : [0f, 0, 0];
                    var r = n.TryGetProperty("rotation", out var ro) ? ro.EnumerateArray().Select(x => x.GetSingle()).ToArray() : [0f, 0, 0, 1];
                    var sc = n.TryGetProperty("scale", out var sca) ? sca.EnumerateArray().Select(x => x.GetSingle()).ToArray() : [1f, 1, 1];
                    g.Local = Matrix4x4.CreateScale(sc[0], sc[1], sc[2]) * Matrix4x4.CreateFromQuaternion(new Quaternion(r[0], r[1], r[2], r[3])) * Matrix4x4.CreateTranslation(t[0], t[1], t[2]);
                }
                Nodes.Add(g);
            }
            var scene = j.GetProperty("scenes")[j.TryGetProperty("scene", out var sn) ? sn.GetInt32() : 0];
            var roots = scene.GetProperty("nodes").EnumerateArray().Select(x => x.GetInt32()).ToList();
            if (roots.Count != 1)
                throw new InvalidOperationException($"imported_ships: {file} has {roots.Count} root nodes (one, the ship, is wanted)");
            Root = roots[0];
        }
    }

    // ---- one import

    /// <summary>A mesh of an object, posed in the ship's frame (turned and raised).</summary>
    private sealed record MeshIn(Vector3[] P, GMat Mat, int Kid, Vector3 Centre);

    private sealed class Obj
    {
        public string Name = ""; // the model's
        public List<string> Path = []; // its named ancestors, outermost first
        public Matrix4x4 World; // its node's frame in the ship's
        public List<MeshIn> Meshes = [];
    }

    /// <summary>A part being made: its boxes in the ship's frame, and how it is turned (k quarter turns left about the vertical).</summary>
    private sealed class Piece
    {
        public string Name = "";
        public string Source = ""; // the model's object name
        public string Group = ""; // the model's group (Hull_Bulkheads, Part_Crew_Seats)
        public string Role = "frame";
        public JsonElement? Spec;
        public int K;
        public bool Door;
        public readonly List<Bx> Boxes = [];
        public readonly List<(string Name, Vector3 At, int Dir)> Socks = [];
        public Vector3 Lo => new(Boxes.Min(b => b.Lo.X), Boxes.Min(b => b.Lo.Y), Boxes.Min(b => b.Lo.Z));
        public Vector3 Hi => new(Boxes.Max(b => b.Hi.X), Boxes.Max(b => b.Hi.Y), Boxes.Max(b => b.Hi.Z));
        public Vector3 Mid => (Lo + Hi) / 2;
        public int Tabs;
        public Obj? From;
    }

    /// <summary>A box in the ship's frame: solid, drawn only (detail) or hidden; points for a hull shape.</summary>
    private sealed class Bx
    {
        public Vector3 Lo, Hi;
        public Vector3[]? Points;
        public string Color = "#808080";
        public string? Finish;
        public bool Detail, Hidden, Glow, Glass, Tab;
        public bool Solid => !Detail;
    }

    private sealed class ShipImport(PartOutput o, string dir)
    {
        private JsonElement _spec;
        private string _prefix = "", _section = "", _out = "";
        private float _above = float.MaxValue, _by;
        private Matrix4x4 _turn = Matrix4x4.Identity;
        private readonly StringBuilder _log = new();

        private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        private string S(string key, string fallback = "") => _spec.TryGetProperty(key, out var v) && v.GetString() is { } s ? s : fallback;

        private static bool Glob(string pattern, string name)
        {
            if (!pattern.Contains('*'))
                return pattern == name;
            var parts = pattern.Split('*');
            if (!name.StartsWith(parts[0], StringComparison.Ordinal) || !name.EndsWith(parts[^1], StringComparison.Ordinal))
                return false;
            var at = parts[0].Length;
            for (var i = 1; i < parts.Length - 1; i++)
            {
                var k = name.IndexOf(parts[i], at, StringComparison.Ordinal);
                if (k < 0)
                    return false;
                at = k + parts[i].Length;
            }
            return at <= name.Length - parts[^1].Length;
        }

        private bool Listed(string key, Obj ob) =>
            _spec.TryGetProperty(key, out var arr) && arr.EnumerateArray().Any(p => ob.Path.Append(ob.Name).Any(n => Glob(p.GetString()!, n)));

        private JsonElement? RoleOf(string name)
        {
            if (_spec.TryGetProperty("roles", out var roles))
                foreach (var r in roles.EnumerateArray())
                    if (Glob(r.GetProperty("match").GetString()!, name))
                        return r;
            return null;
        }

        public void Run()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(o.Root, dir, "import.json")), Lenient);
            _spec = doc.RootElement;
            var name = S("name");
            _prefix = S("prefix", name);
            _section = S("section", name);
            _out = "Constructs/Parts/Imported/" + new string([.. _prefix.Where(char.IsAsciiLetterOrDigit)]);
            if (_spec.TryGetProperty("turn", out var tn))
                _turn = Matrix4x4.CreateRotationY(tn.GetSingle() * MathF.PI / 180f);
            if (_spec.TryGetProperty("raise", out var ra))
            {
                _above = ra.GetProperty("above").GetSingle();
                _by = ra.GetProperty("by").GetSingle();
            }
            var model = Path.Combine(o.Root, dir, S("model", "ship.glb"));
            var glb = new Glb(File.ReadAllBytes(model), model);
            var objs = new List<Obj>();
            Collect(glb, glb.Root, Matrix4x4.Identity, [], objs);
            var pieces = new List<Piece>();
            foreach (var ob in objs)
                pieces.AddRange(Make(ob));
            Names(pieces);
            AddLiftJets(pieces, objs);
            Weld(pieces, out var order);
            FittingSockets(pieces);
            Trim(pieces, order.Select(t => t.Part).ToList());
            Write(pieces, order, name);
        }

        // -- objects

        /// <summary>The objects under node n: a named node with meshes of its own is one (everything under it is its); a named node with none is a group to look into.</summary>
        private void Collect(Glb g, int n, Matrix4x4 parent, List<string> path, List<Obj> objs)
        {
            var node = g.Nodes[n];
            var world = node.Local * parent;
            var hasMesh = node.Kids.Any(k => g.Nodes[k].Mesh >= 0);
            if (node.Name != null && (hasMesh || node.Mesh >= 0) && path.Count > 0)
            {
                var ob = new Obj { Name = node.Name, Path = [.. path], World = world * _turn };
                if (node.Mesh >= 0)
                    AddMeshes(g, node, world, -1, ob);
                for (var i = 0; i < node.Kids.Count; i++)
                    Gather(g, node.Kids[i], world, i, ob);
                Raise(ob);
                objs.Add(ob);
                return;
            }
            var next = node.Name != null ? [.. path, node.Name] : path;
            foreach (var k in node.Kids)
                Collect(g, k, world, next, objs);
        }

        private void Gather(Glb g, int n, Matrix4x4 parent, int kid, Obj ob)
        {
            var node = g.Nodes[n];
            var world = node.Local * parent;
            if (node.Mesh >= 0)
                AddMeshes(g, node, world, kid, ob);
            foreach (var k in node.Kids)
                Gather(g, k, world, kid, ob);
        }

        private void AddMeshes(Glb g, GNode node, Matrix4x4 world, int kid, Obj ob)
        {
            foreach (var (p, mat) in g.Meshes[node.Mesh])
            {
                var pts = Unique(p.Select(v => Vector3.Transform(v, world)));
                ob.Meshes.Add(new MeshIn(pts, mat >= 0 ? g.Mats[mat] : new GMat(), kid, Vector3.Zero));
            }
        }

        private static Vector3[] Unique(IEnumerable<Vector3> pts)
        {
            var o = new List<Vector3>();
            foreach (var v in pts)
                if (!o.Any(u => Vector3.DistanceSquared(u, v) < 1e-8f))
                    o.Add(v);
            return [.. o];
        }

        /// <summary>The rise (a rigid object by its middle, anything else point by point) and the turn, into the ship's frame.</summary>
        private void Raise(Obj ob)
        {
            var rigid = Listed("rigid", ob) && !Listed("stretch", ob);
            var shift = 0f;
            if (rigid && ob.Meshes.Count > 0)
            {
                float lo = ob.Meshes.Min(m => m.P.Min(v => v.Y)), hi = ob.Meshes.Max(m => m.P.Max(v => v.Y));
                shift = (lo + hi) / 2 > _above ? _by : 0;
            }
            Vector3 Pose(Vector3 v) => Vector3.Transform(new Vector3(v.X, v.Y + (rigid ? shift : v.Y > _above ? _by : 0), v.Z), _turn);
            for (var i = 0; i < ob.Meshes.Count; i++)
            {
                var m = ob.Meshes[i];
                var pts = m.P.Select(Pose).ToArray();
                ob.Meshes[i] = m with { P = pts, Centre = pts.Aggregate(Vector3.Zero, (a, b) => a + b) / pts.Length };
            }
            var t = ob.World.Translation;
            var tr = new Vector3(t.X, t.Y, t.Z); // (already turned; raise its height as its own geometry was)
            var orig = Vector3.Transform(tr, Matrix4x4.Transpose(_turn));
            tr.Y += rigid ? shift : orig.Y > _above ? _by : 0;
            ob.World.Translation = tr;
        }

        // -- shapes

        private static string Hex(Vector4 c)
        {
            static int S(float l) => (int)Math.Round(Math.Clamp(l <= 0.0031308f ? l * 12.92f : 1.055f * MathF.Pow(l, 1 / 2.4f) - 0.055f, 0, 1) * 255);
            return $"#{S(c.X):x2}{S(c.Y):x2}{S(c.Z):x2}";
        }

        private static string FinishOf(GMat m)
        {
            var c = m.Base;
            float mx = MathF.Max(c.X, MathF.Max(c.Y, c.Z)), mn = MathF.Min(c.X, MathF.Min(c.Y, c.Z));
            if (m.Metal >= 0.45f)
                return "steel";
            if (mx > 0.25f && mx - mn > 0.5f * mx && c.X >= c.Z)
                return c.Y > 0.6f * c.X ? "hazard paint" : "gloss paint";
            if (mx < 0.03f)
                return "dirty steel";
            if (m.Rough >= 0.8f)
                return "matte paint";
            return "painted plating";
        }

        /// <summary>One mesh as a box: itself when it is an axis-aligned box, else a hull over its corners (thickened when flat).</summary>
        private Bx Shape(MeshIn m, bool structure, string where)
        {
            var b = new Bx { Color = Hex(m.Mat.Base) };
            var p = m.P;
            b.Lo = new Vector3(p.Min(v => v.X), p.Min(v => v.Y), p.Min(v => v.Z));
            b.Hi = new Vector3(p.Max(v => v.X), p.Max(v => v.Y), p.Max(v => v.Z));
            if (m.Mat.Emissive.LengthSquared() > 0.01f)
            {
                b.Glow = true;
                b.Color = Hex(new Vector4(Vector3.Max(m.Mat.Emissive, new Vector3(m.Mat.Base.X, m.Mat.Base.Y, m.Mat.Base.Z)), 1));
            }
            else if (m.Mat.Clear)
                b.Glass = true;
            else
                b.Finish = FinishOf(m.Mat);
            var e = b.Hi - b.Lo;
            var aligned = p.Length == 8 && Distinct(p, v => v.X) == 2 && Distinct(p, v => v.Y) == 2 && Distinct(p, v => v.Z) == 2;
            if (aligned)
            {
                var thin = MathF.Min(e.X, MathF.Min(e.Y, e.Z));
                b.Detail = b.Glow || b.Glass || !(thin >= 0.04f && e.X * e.Y * e.Z >= 0.002f) || !structure && thin < 0.08f;
                return b;
            }
            b.Detail = true;
            var pts = p;
            if (ConvexHull.Of(pts) == null)
            {
                pts = Thicken(pts);
                if (ConvexHull.Of(pts) == null)
                {
                    _log.AppendLine($"imported_ships: {where}: a mesh of {p.Length} corners makes no hull of {ConvexHull.MaxCorners} corners or fewer: drawn as its bounds");
                    pts = null;
                }
            }
            b.Points = pts;
            if (pts != null)
            {
                b.Lo = new Vector3(pts.Min(v => v.X), pts.Min(v => v.Y), pts.Min(v => v.Z));
                b.Hi = new Vector3(pts.Max(v => v.X), pts.Max(v => v.Y), pts.Max(v => v.Z));
            }
            return b;
        }

        private static int Distinct(Vector3[] p, Func<Vector3, float> f)
        {
            var vals = new List<float>();
            foreach (var v in p)
                if (!vals.Any(x => MathF.Abs(x - f(v)) < 1e-3f))
                    vals.Add(f(v));
            return vals.Count;
        }

        /// <summary>A flat set of points (a quad, a plane, a ring) given 16 mm of depth behind its face.</summary>
        private static Vector3[] Thicken(Vector3[] p)
        {
            var c = p.Aggregate(Vector3.Zero, (a, b) => a + b) / p.Length;
            var n = Vector3.Zero;
            for (var i = 0; i < p.Length && n.LengthSquared() < 1e-6f; i++)
                for (var j = i + 1; j < p.Length && n.LengthSquared() < 1e-6f; j++)
                    n = Vector3.Cross(p[i] - c, p[j] - c);
            if (n.LengthSquared() < 1e-9f)
                return p;
            n = Vector3.Normalize(n) * (float)DetailMin;
            return [.. p, .. p.Select(v => v - n)];
        }

        /// <summary>Hidden boxes that fill a drawn hull for collision: the cells (0.25 m) whose middle lies within half a cell of it, merged into runs.</summary>
        private static List<Bx> Fill(Bx b)
        {
            var hull = ConvexHull.Of(b.Points!);
            if (hull == null)
                return [];
            var planes = new List<(Vector3 N, float D)>();
            for (var f = 0; f < hull.N.Length; f++)
                planes.Add((hull.N[f], Vector3.Dot(hull.N[f], hull.V[hull.FaceIdx[hull.FaceStart[f]]])));
            for (var cell = 0.25f; ; cell *= 1.6f)
            {
                var e = b.Hi - b.Lo;
                int nx = Math.Max(1, (int)MathF.Ceiling(e.X / cell)), ny = Math.Max(1, (int)MathF.Ceiling(e.Y / cell)), nz = Math.Max(1, (int)MathF.Ceiling(e.Z / cell));
                var sz = new Vector3(e.X / nx, e.Y / ny, e.Z / nz);
                var margin = 0.5f * MathF.Max(sz.X, MathF.Max(sz.Y, sz.Z));
                var inside = new bool[nx, ny, nz];
                for (var x = 0; x < nx; x++)
                    for (var y = 0; y < ny; y++)
                        for (var z = 0; z < nz; z++)
                        {
                            var c = b.Lo + new Vector3((x + 0.5f) * sz.X, (y + 0.5f) * sz.Y, (z + 0.5f) * sz.Z);
                            inside[x, y, z] = planes.All(pl => Vector3.Dot(pl.N, c) - pl.D <= margin * 0.5f);
                        }
                var o = new List<Bx>();
                var used = new bool[nx, ny, nz];
                for (var y = 0; y < ny; y++)
                    for (var z = 0; z < nz; z++)
                        for (var x = 0; x < nx; x++)
                        {
                            if (!inside[x, y, z] || used[x, y, z])
                                continue;
                            var x1 = x;
                            while (x1 + 1 < nx && inside[x1 + 1, y, z] && !used[x1 + 1, y, z])
                                x1++;
                            var z1 = z;
                            while (z1 + 1 < nz && Enumerable.Range(x, x1 - x + 1).All(i => inside[i, y, z1 + 1] && !used[i, y, z1 + 1]))
                                z1++;
                            var y1 = y;
                            while (y1 + 1 < ny && Enumerable.Range(x, x1 - x + 1).All(i => Enumerable.Range(z, z1 - z + 1).All(k => inside[i, y1 + 1, k] && !used[i, y1 + 1, k])))
                                y1++;
                            for (var i = x; i <= x1; i++)
                                for (var j = y; j <= y1; j++)
                                    for (var k = z; k <= z1; k++)
                                        used[i, j, k] = true;
                            o.Add(new Bx
                            {
                                Lo = b.Lo + new Vector3(x * sz.X, y * sz.Y, z * sz.Z),
                                Hi = b.Lo + new Vector3((x1 + 1) * sz.X, (y1 + 1) * sz.Y, (z1 + 1) * sz.Z),
                                Hidden = true,
                            });
                        }
                if (o.Count <= 12 || cell > 2)
                    return o;
            }
        }

        // -- parts

        private static int Yaw(Vector3 front)
        {
            // The part faces its -z; turned k quarter turns its -z goes to (-sin, 0, -cos)(90k).
            Vector3[] f = [new(0, 0, -1), new(-1, 0, 0), new(0, 0, 1), new(1, 0, 0)];
            var best = 0;
            for (var k = 1; k < 4; k++)
                if (Vector3.Dot(f[k], front) > Vector3.Dot(f[best], front))
                    best = k;
            return best;
        }

        private IEnumerable<Piece> Make(Obj ob)
        {
            var group = ob.Path.Count > 0 ? ob.Path[^1] : ob.Name;
            var role = RoleOf(ob.Name);
            var kind = role is { } r ? r.GetProperty("role").GetString()! : "frame";
            var structure = ob.Path.Any(p => p.StartsWith("Ship_Hull", StringComparison.Ordinal)) || kind == "slideDoor";
            if (_spec.TryGetProperty("pieces", out var pc) && pc.TryGetProperty(ob.Name, out var cellEl))
            {
                var cell = cellEl.GetSingle();
                var cells = ob.Meshes.GroupBy(m => (MathF.Floor(m.Centre.X / cell), MathF.Floor(m.Centre.Z / cell))).OrderBy(c => c.Key.Item2).ThenBy(c => c.Key.Item1).ToList();
                var n = 0;
                foreach (var c in cells)
                    foreach (var chunk in Chunks([.. c], structure, ob.Name))
                    {
                        var p = new Piece { Source = ob.Name, Group = group, Name = $"{ob.Name} {++n}", From = ob };
                        p.Boxes.AddRange(chunk);
                        yield return p;
                    }
                yield break;
            }
            if (kind == "slideDoor")
            {
                var leaf = role!.Value.GetProperty("leaf").EnumerateArray().Select(x => x.GetInt32()).ToHashSet();
                var frame = new Piece { Source = ob.Name, Group = group, Name = ob.Name + " Frame", From = ob };
                var door = new Piece { Source = ob.Name, Group = group, Name = ob.Name, Role = "door", Spec = role, Door = true, From = ob };
                foreach (var m in ob.Meshes)
                    (leaf.Contains(m.Kid) ? door : frame).Boxes.AddRange(Boxes(m, true, ob.Name));
                yield return frame;
                yield return door;
                yield break;
            }
            var one = new Piece { Source = ob.Name, Group = group, Name = ob.Name, Spec = role, From = ob };
            foreach (var m in ob.Meshes)
                one.Boxes.AddRange(Boxes(m, structure, ob.Name));
            switch (kind)
            {
                case "swingDoor":
                    one.Role = "door";
                    one.Door = true;
                    break;
                case "seat" or "bunk" or "medBed":
                    one.Role = "seat";
                    one.K = Yaw(Vector3.TransformNormal(Vector3.UnitZ, ob.World));
                    break;
                case "flightComputer":
                    one.Role = "logic";
                    break;
                case "generator" or "thruster":
                    one.Role = kind;
                    break;
            }
            if (one.Boxes.Count > 250)
                throw new InvalidOperationException($"imported_ships: {ob.Name} has {one.Boxes.Count} boxes (250 at most): list it under \"pieces\"");
            yield return one;
        }

        private List<Bx> Boxes(MeshIn m, bool structure, string where)
        {
            var b = Shape(m, structure, where);
            var o = new List<Bx> { b };
            if (b.Solid)
            {
                // Drawn as modelled; what collides is a hidden twin, which the weld pass trims where it overlaps a part placed before it.
                b.Detail = true;
                o.Add(new Bx { Lo = b.Lo, Hi = b.Hi, Hidden = true });
            }
            if (structure && b.Points != null && !b.Glow && (b.Hi - b.Lo).Length() > 0.5f)
                o.AddRange(Fill(b));
            return o;
        }

        /// <summary>A cell's meshes as one or more pieces of at most 200 boxes each, halving along the longest side.</summary>
        private List<List<Bx>> Chunks(List<MeshIn> ms, bool structure, string where)
        {
            var boxes = ms.Select(m => (m, Boxes(m, structure, where))).ToList();
            if (boxes.Sum(b => b.Item2.Count) <= 150 || ms.Count < 2)
                return [[.. boxes.SelectMany(b => b.Item2)]];
            var lo = new Vector3(ms.Min(m => m.Centre.X), ms.Min(m => m.Centre.Y), ms.Min(m => m.Centre.Z));
            var hi = new Vector3(ms.Max(m => m.Centre.X), ms.Max(m => m.Centre.Y), ms.Max(m => m.Centre.Z));
            var e = hi - lo;
            Func<MeshIn, float> f = e.X >= e.Z && e.X >= e.Y ? m => m.Centre.X : e.Z >= e.Y ? m => m.Centre.Z : m => m.Centre.Y;
            var sorted = ms.OrderBy(f).ToList();
            var half = sorted.Count / 2;
            return [.. Chunks(sorted[..half], structure, where), .. Chunks(sorted[half..], structure, where)];
        }

        /// <summary>Part names: the prefix and the object's name shortened by the import's list, unique, at most 32 characters.</summary>
        private void Names(List<Piece> pieces)
        {
            var taken = new HashSet<string>();
            foreach (var p in pieces)
            {
                var n = p.Name;
                if (_spec.TryGetProperty("shorten", out var sh))
                    foreach (var pair in sh.EnumerateArray())
                        n = n.Replace(pair[0].GetString()!, pair[1].GetString()!);
                n = string.Join(' ', n.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                n = $"{_prefix} {n}";
                if (n.Length > 32)
                    n = n[..32].TrimEnd();
                var b = n;
                for (var i = 2; !taken.Add(n); i++)
                    n = b.Length + 1 + i.ToString().Length > 32 ? $"{b[..(32 - 1 - i.ToString().Length)]} {i}" : $"{b} {i}";
                p.Name = n;
            }
        }

        // -- lift jets

        private void AddLiftJets(List<Piece> pieces, List<Obj> objs)
        {
            if (!_spec.TryGetProperty("lift", out var lift))
                return;
            var under = lift.GetProperty("under").GetString()!;
            // As many jets under each pod as keep each one at or under "most" (a Ship thruster over 500 kN needs works behind it:
            // Constructs/ShipWorks.json jetMost), spread along the pod's belly. Their force is set in Write, once the weight is known.
            var pods = pieces.Where(p => Glob(under, p.Source) && p.Door).ToList();
            var share = lift.GetProperty("share").GetDouble();
            var most = lift.TryGetProperty("most", out var mo) ? mo.GetDouble() : 490000;
            var weight = pieces.Sum(MassOf) * 9.81;
            var comZ = (float)(pieces.Sum(p => MassOf(p) * p.Mid.Z) / pieces.Sum(MassOf)); // (the jets' line sits on it, or the stabilizer holds them back to keep level)
            var each = Math.Max(1, (int)Math.Ceiling(weight * share / Math.Max(1, pods.Count) / most));
            foreach (var pod in pods)
            {
                // Under its pivot line, in the pod's belly as it rests (cruise): the nozzles show under the pod.
                var pivot = pod.From!.World.Translation;
                var pts = pod.From.Meshes.SelectMany(m => m.P).ToList();
                float z0 = pts.Min(v => v.Z), z1 = pts.Max(v => v.Z);
                for (var k = 0; k < each; k++)
                {
                    var span = MathF.Min(1.2f * (each - 1), MathF.Min(comZ - z0, z1 - comZ) * 2 - 0.6f);
                    var z = each == 1 ? comZ : comZ - span / 2 + span * k / (each - 1);
                    var low = pts.Where(v => MathF.Abs(v.Z - z) < 0.6f).DefaultIfEmpty(pivot).Min(v => v.Y);
                    var jet = new Piece { Source = pod.Source + "_Lift", Group = pod.Group, Name = $"{_prefix} Lift Jet {(pivot.X < 0 ? "Port" : "Stbd")} {k + 1}", Role = "thruster" };
                    LiftJetLook(jet, new Vector3(pivot.X, low + 0.3f, z));
                    pieces.Add(jet);
                }
            }
        }

        private static void LiftJetLook(Piece p, Vector3 at)
        {
            Vector3 R(float r, float y, int i)
            {
                var a = MathF.PI / 8 + i * MathF.PI / 4;
                return at + new Vector3(r * MathF.Cos(a), y, r * MathF.Sin(a));
            }
            Bx Ring(float r0, float y0, float r1, float y1, string col, string? fin, bool glow = false)
            {
                var pts = Enumerable.Range(0, 8).Select(i => R(r0, y0, i)).Concat(Enumerable.Range(0, 8).Select(i => R(r1, y1, i))).ToArray();
                return new Bx
                {
                    Points = pts, Detail = true, Color = col, Finish = fin, Glow = glow,
                    Lo = new Vector3(pts.Min(v => v.X), pts.Min(v => v.Y), pts.Min(v => v.Z)), Hi = new Vector3(pts.Max(v => v.X), pts.Max(v => v.Y), pts.Max(v => v.Z)),
                };
            }
            p.Boxes.Add(Ring(0.34f, 0, 0.3f, -0.3f, "#3a3f46", "dirty steel"));
            p.Boxes.Add(Ring(0.24f, -0.3f, 0.32f, -0.5f, "#2a2c2e", "soot"));
            p.Boxes.Add(Ring(0.16f, -0.36f, 0.16f, -0.38f, "#ffb060", null, glow: true));
            p.Boxes.Add(new Bx { Lo = at + new Vector3(-0.22f, -0.3f, -0.22f), Hi = at + new Vector3(0.22f, 0, 0.22f), Hidden = true });
        }

        private static Vector3 DirVec(string d)
        {
            var v = d[1] switch { 'x' => Vector3.UnitX, 'y' => Vector3.UnitY, _ => Vector3.UnitZ };
            return d[0] == '-' ? -v : v;
        }

        private static readonly string[] DirNames = ["+x", "-x", "+y", "-y", "+z", "-z"];

        private static int DirIndex(Vector3 v)
        {
            var best = 0;
            for (var i = 1; i < 6; i++)
                if (Vector3.Dot(DirVec(DirNames[i]), v) > Vector3.Dot(DirVec(DirNames[best]), v))
                    best = i;
            return best;
        }

        // -- welding

        private static float Gap(Bx a, Bx b)
        {
            var d = Vector3.Max(Vector3.Zero, Vector3.Max(a.Lo - b.Hi, b.Lo - a.Hi));
            return d.Length();
        }

        private static List<Bx> Weldable(Piece p)
        {
            var s = p.Boxes.Where(b => b.Solid).ToList();
            return s.Count > 0 ? s : p.Boxes;
        }

        /// <summary>A tree from the root piece out: each part goes on the nearest part already placed (never on a door), by a pair of small hidden tabs.</summary>
        private void Weld(List<Piece> pieces, out List<(int Part, int On, string Theirs, string Mine)> order)
        {
            var rootName = S("root", "Hull_Lower_Chassis");
            var root = pieces.Select((p, i) => (p, i)).Where(t => t.p.Source == rootName).OrderBy(t => t.p.Mid.Length()).Select(t => t.i).DefaultIfEmpty(0).First();
            var placed = new List<int> { root };
            order = [(root, -1, "", "")];
            var best = pieces.Select(_ => (D: float.MaxValue, On: -1, A: (Bx?)null, B: (Bx?)null)).ToArray();
            var solid = pieces.Select(Weldable).ToList();
            void Update(int on)
            {
                if (pieces[on].Door)
                    return;
                for (var i = 0; i < pieces.Count; i++)
                {
                    if (placed.Contains(i))
                        continue;
                    foreach (var a in solid[on])
                        foreach (var b in solid[i])
                        {
                            var g = Gap(a, b);
                            if (g < best[i].D)
                                best[i] = (g, on, a, b);
                        }
                }
            }
            Update(root);
            while (placed.Count < pieces.Count)
            {
                var next = -1;
                for (var i = 0; i < pieces.Count; i++)
                    if (!placed.Contains(i) && best[i].On >= 0 && (next < 0 || best[i].D < best[next].D))
                        next = i;
                if (next < 0)
                    throw new InvalidOperationException("imported_ships: parts joined to nothing: " + string.Join(", ", pieces.Where((_, i) => !placed.Contains(i)).Select(p => p.Name)));
                var (d, on, a, b) = best[next];
                if (d > 0.5f)
                    _log.AppendLine($"imported_ships: {pieces[next].Name} is {d:0.00} m from the nearest part ({pieces[on].Name}): welded across the gap");
                var (theirs, mine) = Tab(pieces[on], pieces[next], a!, b!, _tabs);
                placed.Add(next);
                order.Add((next, on, theirs, mine));
                Update(next);
            }
        }

        private readonly List<Bx> _tabs = [];

        private static (string Theirs, string Mine) Tab(Piece q, Piece p, Bx a, Bx b, List<Bx> tabs)
        {
            var cb = (b.Lo + b.Hi) / 2;
            int axis = -1;
            var plus = false;
            var excess = 0f;
            for (var k = 0; k < 3; k++)
            {
                float c = cb.Get(k), lo = a.Lo.Get(k), hi = a.Hi.Get(k);
                if (c > hi && c - hi > excess)
                    (axis, plus, excess) = (k, true, c - hi);
                else if (c < lo && lo - c > excess)
                    (axis, plus, excess) = (k, false, lo - c);
            }
            if (axis < 0)
            {
                var near = float.MaxValue;
                for (var k = 0; k < 3; k++)
                {
                    float c = cb.Get(k), lo = a.Lo.Get(k), hi = a.Hi.Get(k);
                    if (hi - c < near)
                        (axis, plus, near) = (k, true, hi - c);
                    if (c - lo < near)
                        (axis, plus, near) = (k, false, c - lo);
                }
            }
            var s = new float[3];
            for (var k = 0; k < 3; k++)
            {
                float lo = a.Lo.Get(k), hi = a.Hi.Get(k);
                s[k] = k == axis ? (plus ? hi : lo) : hi - lo > 0.06f ? Math.Clamp(cb.Get(k), lo + 0.03f, hi - 0.03f) : (lo + hi) / 2;
                s[k] = MathF.Round(s[k] * 100) / 100;
            }
            var at = new Vector3(s[0], s[1], s[2]);
            var dir = Vector3.Zero.With(axis, plus ? 1 : -1);
            var side = new Vector3(0.02f).With(axis, 0);
            Bx Tab(Vector3 from, Vector3 to) => new() { Lo = Vector3.Min(from, to) - side, Hi = Vector3.Max(from, to) + side, Hidden = true, Tab = true };
            // Clear of every other tab (two on one spot would block each other): step along the face, 6 cm at a time.
            bool Clear(Vector3 c) => !tabs.Any(t => t.Lo.X < c.X + 0.07f && c.X - 0.07f < t.Hi.X && t.Lo.Y < c.Y + 0.07f && c.Y - 0.07f < t.Hi.Y && t.Lo.Z < c.Z + 0.07f && c.Z - 0.07f < t.Hi.Z);
            int u = (axis + 1) % 3, w = (axis + 2) % 3;
            var found = Clear(at);
            for (var r = 1; r < 12 && !found; r++)
                for (var du = -r; du <= r && !found; du++)
                    for (var dw = -r; dw <= r && !found; dw++)
                    {
                        if (Math.Max(Math.Abs(du), Math.Abs(dw)) != r)
                            continue;
                        var c = at.With(u, at.Get(u) + du * 0.06f).With(w, at.Get(w) + dw * 0.06f);
                        if (c.Get(u) < a.Lo.Get(u) || c.Get(u) > a.Hi.Get(u) || c.Get(w) < a.Lo.Get(w) || c.Get(w) > a.Hi.Get(w) || !Clear(c))
                            continue;
                        at = new Vector3(MathF.Round(c.X * 100) / 100, MathF.Round(c.Y * 100) / 100, MathF.Round(c.Z * 100) / 100);
                        found = true;
                    }
            var tq = Tab(at - dir * 0.04f, at);
            var tp = Tab(at, at + dir * 0.04f);
            tabs.Add(tq);
            tabs.Add(tp);
            q.Boxes.Add(tq);
            p.Boxes.Add(tp);
            var name = $"t{q.Tabs++}";
            q.Socks.Add((name, at, DirIndex(dir)));
            var mine = p.Door && !p.Socks.Any(x => x.Name == "hang") ? "hang" : $"t{p.Tabs++}";
            p.Socks.Add((mine, at, DirIndex(-dir)));
            return (name, mine);
        }

        /// <summary>
        /// The game refuses a part whose solid boxes overlap a placed part's by more than a centimetre: each part's hidden boxes, in
        /// placing order, lose what overlaps the parts placed before it (box minus box, slivers under 2 cm dropped). Tabs stay as
        /// they are; one that still overlaps is logged.
        /// </summary>
        private void Trim(List<Piece> pieces, List<int> order)
        {
            const float Slack = 0.009f;
            var placed = new List<(Piece P, Bx B)>();
            static bool Over(Bx a, Bx b, float slack) =>
                a.Lo.X < b.Hi.X - slack && b.Lo.X < a.Hi.X - slack && a.Lo.Y < b.Hi.Y - slack && b.Lo.Y < a.Hi.Y - slack && a.Lo.Z < b.Hi.Z - slack && b.Lo.Z < a.Hi.Z - slack;
            static bool Kept(Bx a)
            {
                var e = a.Hi - a.Lo;
                return MathF.Min(e.X, MathF.Min(e.Y, e.Z)) >= 0.02f;
            }
            foreach (var i in order)
            {
                var p = pieces[i];
                // This part's tabs stay: what they overlap in the parts before it is cut out of those (less collision never blocks anything).
                foreach (var t in p.Boxes.Where(b => b.Tab))
                    for (var k = 0; k < placed.Count; k++)
                    {
                        var (q, h) = placed[k];
                        if (!Over(t, h, Slack))
                            continue;
                        if (h.Tab)
                        {
                            _log.AppendLine($"imported_ships: {p.Name}: a weld tab at {t.Lo} overlaps one of {q.Name}'s");
                            continue;
                        }
                        var rest = Minus(h, t).Where(Kept).ToList();
                        var at = q.Boxes.IndexOf(h);
                        q.Boxes.RemoveAt(at);
                        q.Boxes.InsertRange(at, rest);
                        placed.RemoveAt(k);
                        placed.InsertRange(k, rest.Select(r => (q, r)));
                        k += rest.Count - 1;
                    }
                var keep = new List<Bx>();
                foreach (var b in p.Boxes)
                {
                    if (!b.Hidden || b.Tab)
                    {
                        keep.Add(b);
                        continue;
                    }
                    var parts = new List<Bx> { b };
                    foreach (var (_, q) in placed)
                    {
                        var next = new List<Bx>();
                        foreach (var a in parts)
                            if (Over(a, q, Slack))
                                next.AddRange(Minus(a, q));
                            else
                                next.Add(a);
                        parts = next;
                        if (parts.Count == 0)
                            break;
                    }
                    keep.AddRange(parts.Where(Kept));
                }
                if (keep.Count > 250)
                    keep = Merge(keep);
                p.Boxes.Clear();
                p.Boxes.AddRange(keep);
                placed.AddRange(p.Boxes.Where(b => b.Hidden).Select(b => (p, b)));
            }
            foreach (var p in pieces.Where(p => p.Boxes.Count > 256))
                throw new InvalidOperationException($"imported_ships: {p.Name} has {p.Boxes.Count} boxes after trimming (256 at most)");
        }

        /// <summary>a minus b: up to six boxes (b's slabs out of a, x first).</summary>
        private static List<Bx> Minus(Bx a, Bx b)
        {
            var o = new List<Bx>();
            Vector3 lo = a.Lo, hi = a.Hi;
            for (var k = 0; k < 3; k++)
            {
                if (b.Lo.Get(k) > lo.Get(k))
                {
                    o.Add(new Bx { Lo = lo, Hi = hi.With(k, b.Lo.Get(k)), Hidden = true });
                    lo = lo.With(k, b.Lo.Get(k));
                }
                if (b.Hi.Get(k) < hi.Get(k))
                {
                    o.Add(new Bx { Lo = lo.With(k, b.Hi.Get(k)), Hi = hi, Hidden = true });
                    hi = hi.With(k, b.Hi.Get(k));
                }
            }
            return o;
        }

        /// <summary>Too many hidden boxes after trimming: the smallest ones go (they are drawn anyway).</summary>
        private static List<Bx> Merge(List<Bx> boxes)
        {
            var hidden = boxes.Where(b => b.Hidden && !b.Tab).OrderByDescending(b => { var e = b.Hi - b.Lo; return e.X * e.Y * e.Z; }).ToList();
            var drop = hidden.Skip(250 - (boxes.Count - hidden.Count)).ToHashSet();
            return [.. boxes.Where(b => !drop.Contains(b))];
        }

        /// <summary>
        /// Stock fittings the import hangs on its parts ("fittings": a switch, a gate): a socket for each on the face it names of its
        /// host's biggest drawn box, at a hand's height over the host's foot (or "y" over it), at "x" or "z" across the face when given.
        /// </summary>
        private void FittingSockets(List<Piece> pieces)
        {
            if (!_spec.TryGetProperty("fittings", out var list))
                return;
            foreach (var f in list.EnumerateArray())
            {
                var id = f.GetProperty("id").GetString()!;
                var p = pieces.FirstOrDefault(q => q.Source == f.GetProperty("on").GetString()) ?? throw new InvalidOperationException($"imported_ships: fitting {id} goes on {f.GetProperty("on").GetString()}, which isn't an object of the model");
                var face = DirVec(f.TryGetProperty("face", out var fc) ? fc.GetString()! : "+y");
                var top = p.Boxes.Where(b => !b.Hidden && b.Points == null).OrderByDescending(b => { var e = b.Hi - b.Lo; return e.X * e.Y * e.Z; }).First();
                var mid = (top.Lo + top.Hi) / 2;
                var y = face.Y != 0 ? (face.Y > 0 ? top.Hi.Y : top.Lo.Y) : MathF.Min(top.Hi.Y - 0.1f, p.Lo.Y + (f.TryGetProperty("y", out var fy) ? fy.GetSingle() : 1.35f));
                var x = face.X != 0 ? (face.X > 0 ? top.Hi.X : top.Lo.X) : f.TryGetProperty("x", out var fx) ? fx.GetSingle() : mid.X;
                var z = face.Z != 0 ? (face.Z > 0 ? top.Hi.Z : top.Lo.Z) : f.TryGetProperty("z", out var fz) ? fz.GetSingle() : mid.Z;
                var at = new Vector3(MathF.Round(x * 100) / 100, MathF.Round(y * 100) / 100, MathF.Round(z * 100) / 100);
                var side = new Vector3(0.03f).With(DirIndex(face) / 2, 0);
                p.Boxes.Add(new Bx { Lo = Vector3.Min(at, at - face * 0.04f) - side, Hi = Vector3.Max(at, at - face * 0.04f) + side, Hidden = true, Tab = true });
                p.Socks.Add(("fit " + id, at, DirIndex(face)));
            }
        }

        // -- files

        private static Vector3 ToPart(Vector3 v, int k)
        {
            // body = R_y(90k) part: part = R_y(-90k) body. R_y(t): x' = x cos t + z sin t, z' = -x sin t + z cos t.
            var t = -k * MathF.PI / 2;
            float c = MathF.Round(MathF.Cos(t)), s = MathF.Round(MathF.Sin(t));
            return new Vector3(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
        }

        private static double[] D(Vector3 v) => [Math.Round(v.X, 4), Math.Round(v.Y, 4), Math.Round(v.Z, 4)];

        private static string DirOf(int dir, int k) => DirNames[DirIndex(ToPart(DirVec(DirNames[dir]), k))];

        private PyDict Json(Piece p, double mass)
        {
            var desc = $"Part of the {S("name")}, from its model's {p.Source.Replace('_', ' ')} ({p.Group.Replace('_', ' ')}).";
            var part = new Part(p.Name, Small, _section, desc, role: p.Role, tool: p.Role is "seat" or "logic" ? "wrench" : "welder", material: "steel", color: p.Boxes.FirstOrDefault(b => !b.Hidden)?.Color ?? "#808080", group: "Ship");
            foreach (var b in p.Boxes)
            {
                Vector3 lo = ToPart(b.Lo, p.K), hi = ToPart(b.Hi, p.K);
                var mn = Vector3.Min(lo, hi);
                var mx = Vector3.Max(lo, hi);
                if (b.Hidden)
                {
                    part.Add(Box(D(mn), D(mx), hidden: true));
                    continue;
                }
                var shape = new PyDict();
                if (b.Points != null)
                {
                    shape["shape"] = "hull";
                    shape["points"] = b.Points.Select(v => (object?)Vec(D(ToPart(v, p.K)))).ToList();
                }
                if (b.Glow)
                    shape["glow"] = true;
                else if (b.Finish != null)
                    shape["finish"] = b.Finish;
                if (b.Glass)
                    shape["glass"] = true;
                shape["paintable"] = false;
                var box = Box(D(mn), D(mx), b.Color, detail: b.Detail, shape: shape);
                if (box == null)
                    _log.AppendLine($"imported_ships: {p.Name}: a solid box under 1 cm thick dropped ({mn} to {mx})");
                part.Add(box);
            }
            foreach (var (name, at, dir) in p.Socks)
                part.Sock(name, D(ToPart(at, p.K)), DirOf(dir, p.K));
            Roles(p, part, mass);
            var hull = p.Group.StartsWith("Hull", StringComparison.Ordinal);
            return part.Stats(mass, Math.Max(120, mass * 4), hull ? 20 : 8, hull ? 6.0 : 2.5).Json();
        }

        private double _weight; // N: the whole ship's, for the thrusters

        private void Roles(Piece p, Part part, double mass)
        {
            var spec = p.Spec;
            Vector3 lo = p.Boxes.Where(b => !b.Hidden).Select(b => b.Lo).Aggregate(Vector3.Min), hi = p.Boxes.Where(b => !b.Hidden).Select(b => b.Hi).Aggregate(Vector3.Max);
            var sp = spec ?? default;
            var kind = spec != null ? sp.GetProperty("role").GetString() : null;
            switch (kind)
            {
                case "slideDoor":
                {
                    // Slides along its width, towards its wall's middle (the object's origin), into the wall.
                    var w = p.From!.World;
                    var across = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, w));
                    var mid = (lo + hi) / 2;
                    var toWall = Vector3.Dot(w.Translation - mid, across);
                    var axis = DirIndex(MathF.Abs(toWall) > 0.05f && toWall < 0 ? -across : across);
                    part.Set(("door", Kw(("kind", "slide"), ("axis", DirOf(axis, p.K)), ("travel", sp.GetProperty("travel").GetDouble()), ("seconds", sp.GetProperty("seconds").GetDouble()), ("hang", "hang"))));
                    break;
                }
                case "swingDoor":
                {
                    var pivot = ToPart(p.From!.World.Translation, p.K);
                    var angle = sp.GetProperty("angle").GetDouble();
                    var door = Kw(("kind", "swing"), ("axis", sp.GetProperty("axis").GetString()), ("pivot", Vec(D(pivot))), ("angle", angle), ("seconds", sp.GetProperty("seconds").GetDouble()), ("hang", "hang"));
                    if (Math.Abs(angle % 90) > 0.01)
                        door["held"] = true; // (an open pose off the quarter turns is drawn there; its collider stays shut: ConstructDoors "held")
                    part.Set(("door", door));
                    break;
                }
                case "seat" or "bunk" or "medBed":
                {
                    // The sitter's feet under the middle; a bunk's on its lower mattress.
                    var foot = new Vector3((lo.X + hi.X) / 2, lo.Y, (lo.Z + hi.Z) / 2);
                    var seat = Kw(("at", Vec(D(ToPart(foot, p.K)))));
                    if (kind == "bunk")
                        seat["bunk"] = true;
                    if (kind == "medBed")
                        seat["heal"] = 2;
                    if (sp.TryGetProperty("helm", out var helm) && helm.GetBoolean())
                        seat["helm"] = true;
                    part.Set(("seat", seat));
                    break;
                }
                case "flightComputer":
                    part.Set(("logic", Kw(("kind", "flightstabilizer"), ("gyro", Math.Round(_weight * 60)))), ("config", FlightParts.Config), ("faces", "forward"), ("faceAxis", "-z"));
                    break;
                case "generator":
                {
                    var rate = sp.GetProperty("rate").GetDouble();
                    var port = ToPart(new Vector3((lo.X + hi.X) / 2, lo.Y + 0.05f, hi.Z), p.K);
                    part.Set(("generator", Kw(("rate", rate), ("spool", 5), ("fuel", "none"))),
                        ("ports", Py.L(Kw(("name", "Power out"), ("type", "power"), ("dir", "out"), ("at", Vec(D(port))), ("face", DirNames[DirIndex(ToPart(Vector3.UnitZ, p.K))])))));
                    break;
                }
                case "thruster":
                {
                    var force = Math.Round(_weight * sp.GetProperty("share").GetDouble());
                    var ex = sp.GetProperty("exhaust").GetString()!;
                    var dv = DirVec(ex);
                    var mid = (lo + hi) / 2;
                    var at = new Vector3(dv.X != 0 ? (dv.X > 0 ? hi.X : lo.X) : mid.X, dv.Y != 0 ? (dv.Y > 0 ? hi.Y : lo.Y) : mid.Y, dv.Z != 0 ? (dv.Z > 0 ? hi.Z : lo.Z) : mid.Z);
                    Thrust(part, force, ex, at, MathF.Min(hi.X - lo.X, hi.Y - lo.Y) * 0.4f, p.K);
                    break;
                }
                default:
                    if (p.Role == "thruster") // a lift jet
                    {
                        var share = _spec.GetProperty("lift").GetProperty("share").GetDouble();
                        var jets = Math.Max(1, _liftJets);
                        Thrust(part, Math.Round(_weight * share / jets), "-y", new Vector3((lo.X + hi.X) / 2, lo.Y, (lo.Z + hi.Z) / 2), 0.3f, p.K);
                    }
                    break;
            }
        }

        private int _liftJets;

        private static void Thrust(Part part, double force, string exhaust, Vector3 at, float radius, int k)
        {
            var pa = Vec(D(ToPart(at, k)));
            var ex = DirNames[DirIndex(ToPart(DirVec(exhaust), k))];
            part.Set(("thruster", Kw(("force", force), ("fuel", "electric"), ("power", Math.Round(force / 1000)), ("spool", 0.4), ("exhaust", ex), ("at", pa))),
                ("flame", Py.L(Kw(("at", pa), ("dir", ex), ("radius", Math.Round(radius, 3)), ("length", Math.Round(radius * 6, 3)), ("kind", "Rocket")))));
        }

        private static double MassOf(Piece p)
        {
            double m = 0;
            foreach (var b in p.Boxes)
            {
                if (b.Hidden)
                    continue;
                var e = b.Hi - b.Lo;
                var v = (double)e.X * e.Y * e.Z;
                m += b.Points != null ? v * 60 : v * (b.Glass ? 400 : 180);
            }
            return Math.Max(5, Math.Round(m, 1));
        }

        private void Write(List<Piece> pieces, List<(int Part, int On, string Theirs, string Mine)> order, string shipName)
        {
            var masses = pieces.Select(MassOf).ToArray();
            _weight = masses.Sum() * 9.81;
            _liftJets = pieces.Count(p => p.Role == "thruster" && p.Spec == null);
            var defs = new Dictionary<string, PyDict>();
            o.Own(_out);
            for (var i = 0; i < pieces.Count; i++)
            {
                var d = Json(pieces[i], masses[i]);
                defs.Add(pieces[i].Name, d);
                o.File($"{_out}/{FileName(pieces[i].Name)}", PyJson.Dumps(d, 2) + "\n");
            }
            BuildingParts.AddPartDefs(o, defs, _out);
            var bp = new BuildingParts.Blueprint(shipName, S("description"), defs, maxSteps: pieces.Count + 16);
            bp.Conduit = true;
            var step = new int[pieces.Count];
            foreach (var (i, on, theirs, mine) in order)
            {
                var p = pieces[i];
                if (on < 0)
                {
                    step[i] = bp.First(p.Name);
                    continue;
                }
                int[] rots = [BuildingParts.R0, BuildingParts.RY90, BuildingParts.RY180, BuildingParts.RY270];
                step[i] = bp.HangTurned(p.Name, step[on], theirs, mine, rots[p.K]); // (turned as asked: a socket facing up would take any of four)
            }
            // The pilot's seat flies the ship through the flight computer.
            int Find(Func<Piece, bool> f) => pieces.FindIndex(p => f(p));
            var pilot = Find(p => p.Spec is { } s && s.TryGetProperty("pilot", out var pl) && pl.GetBoolean());
            var fc = Find(p => p.Role == "logic");
            if (fc >= 0)
            {
                var flight = ShipParts.ShipFlight();
                flight["mouseAim"] = false; // (a VTOL craft flies level over its lift jets, as the Dibber: the mouse doesn't swing its nose)
                bp.Steps[step[fc]]["config"] = flight;
                if (pilot >= 0)
                    bp.Wires.Add((step[pilot], step[fc]));
            }
            // Stock fittings, then the wires the import lists (by a fitting's id or an object's name).
            var fit = new Dictionary<string, int>();
            if (_spec.TryGetProperty("fittings", out var fl))
                foreach (var f in fl.EnumerateArray())
                {
                    var id = f.GetProperty("id").GetString()!;
                    var host = Find(p => p.Source == f.GetProperty("on").GetString());
                    var st = bp.Hang(f.GetProperty("part").GetString()!, step[host], "fit " + id, f.GetProperty("by").GetString()!);
                    if (f.TryGetProperty("config", out var cfg))
                        bp.Steps[st]["config"] = PyJson.Loads(cfg.GetRawText());
                    fit[id] = st;
                }
            int StepOf(string n) => fit.TryGetValue(n, out var st) ? st : (Find(p => p.Source == n && p.Door) is var d and >= 0 ? d : Find(p => p.Source == n)) is var i and >= 0 ? step[i] : throw new InvalidOperationException($"imported_ships: a wire names {n}, which is neither a fitting nor an object");
            if (_spec.TryGetProperty("wires", out var wl))
                foreach (var w in wl.EnumerateArray())
                    bp.Wires.Add((StepOf(w[0].GetString()!), StepOf(w[1].GetString()!)));
            o.File($"{BpOut}/{S("blueprint", FileName(shipName))}", PyJson.Dumps(bp.Json(), 2) + "\n");
            if (_log.Length > 0)
                Console.Write(_log.ToString());
        }
    }
}

internal static class ImportVec
{
    public static float Get(this Vector3 v, int k) => k == 0 ? v.X : k == 1 ? v.Y : v.Z;

    public static Vector3 With(this Vector3 v, int k, float x) => k == 0 ? new Vector3(x, v.Y, v.Z) : k == 1 ? new Vector3(v.X, x, v.Z) : new Vector3(v.X, v.Y, x);
}
