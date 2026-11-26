using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// The Ship Designer's yard: what is drawn and picked, rebuilt from the scene after each command (the page's rebuild() and its
/// draw functions, index.html). The model's meshes come keyed and are kept on the GPU while the scene keeps them; boxes, frames,
/// rings and lines are made here, as the page makes them with three.js.
/// </summary>
public sealed partial class Ui
{
    private readonly List<DesignObj> _sdObjs = [], _sdPickables = [], _sdPreview = [];
    private readonly List<DesignGeo> _sdOwned = [];
    private readonly Dictionary<string, DesignGeo> _sdKeyed = [];
    private readonly HashSet<string> _sdKeyedUsed = [];
    private readonly Dictionary<string, (DesignGeo G, Vector3 At)> _hullGeo = [];
    private readonly Dictionary<int, List<(int Copy, double CladFit)>> _hullCopies = [];
    private float _sdGridY;

    /// <summary>A material as the page gives its meshes: a colour (hex, sRGB), see-through or not, a glow, lit or not.</summary>
    private readonly record struct Mat(uint Hex = 0xffffff, float Alpha = 1, bool Transparent = false, bool DepthWrite = true, uint Emissive = 0,
        bool Unlit = false, float Metal = 0.15f, bool DepthTest = true, int Order = 0, uint Tint = 0xffffff, float Rough = 0.7f, float Coat = 0, float Env = 0);

    /// <summary>The page's body materials (MATS): a tint over the vertex colours, metalness, roughness and clearcoat; Painted is none.</summary>
    private static readonly (string Name, uint Tint, float Metal, float Rough, float Coat)[] SdMats =
    [
        ("Painted", 0xffffff, 0.15f, 0.7f, 0), ("Steel", 0xc8ccd0, 0.85f, 0.38f, 0), ("Brushed aluminium", 0xe4e7ea, 0.9f, 0.5f, 0),
        ("Titanium", 0xb6b2a8, 0.8f, 0.3f, 0), ("Copper", 0xf0a070, 0.95f, 0.28f, 0), ("Gloss plastic", 0xffffff, 0, 0.18f, 1),
        ("Matte plastic", 0xffffff, 0, 0.92f, 0), ("Carbon", 0x3a3d42, 0.2f, 0.42f, 0.8f),
    ];

    /// <summary>A material by name, worn by damage as the page's wornOpts does (rougher, less metal, less clearcoat and reflection).</summary>
    private static Mat MatNamed(string name, double dmg = 0)
    {
        var i = Array.FindIndex(SdMats, m => m.Name == name);
        if (i <= 0)
            return new Mat(0xffffff);
        var m = SdMats[i];
        var d = (float)Math.Clamp(dmg, 0, 1);
        return new Mat(0xffffff, Tint: m.Tint, Metal: m.Metal * (1 - 0.55f * d), Rough: MathF.Min(1, m.Rough + 0.45f * d), Coat: m.Coat * (1 - d), Env: 0.8f * (1 - 0.45f * d));
    }

    private static Vector3 Lin(uint hex) => new(SrgbToLinear((hex >> 16) & 255), SrgbToLinear((hex >> 8) & 255), SrgbToLinear(hex & 255));

    private static float SrgbToLinear(uint c)
    {
        var v = c / 255f;
        return v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
    }

    private static uint HexOf(string s)
    {
        var c = ShipDesigner.DesignMesh.Hex(s);
        return ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
    }

    /// <summary>Triangles or line segments made here, through a matrix (a three.js group's).</summary>
    private sealed class GB
    {
        public readonly List<float> P = [];
        public readonly List<uint> C = [];
        public Matrix4x4 M = Matrix4x4.Identity;
        public bool Lines;
        public uint Col = 0xffffffff;

        public void V(Vector3 p)
        {
            var w = Vector3.Transform(p, M);
            P.Add(w.X);
            P.Add(w.Y);
            P.Add(w.Z);
            C.Add(Col);
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            V(a);
            V(b);
            V(c);
        }

        public void Seg(Vector3 a, Vector3 b)
        {
            V(a);
            V(b);
        }

        /// <summary>A box of size w, h, d at (x, y, z), turned <paramref name="rz"/> about z first (three's BoxGeometry in a mesh).</summary>
        public void Box(float w, float h, float d, float x, float y, float z, float rz = 0)
        {
            var m0 = M;
            M = Matrix4x4.CreateRotationZ(rz) * Matrix4x4.CreateTranslation(x, y, z) * m0;
            float a = w / 2, b = h / 2, c = d / 2;
            Vector3 P0 = new(-a, -b, -c), P1 = new(a, -b, -c), P2 = new(a, b, -c), P3 = new(-a, b, -c);
            Vector3 P4 = new(-a, -b, c), P5 = new(a, -b, c), P6 = new(a, b, c), P7 = new(-a, b, c);
            Quad(P4, P5, P6, P7); // +z
            Quad(P1, P0, P3, P2); // -z
            Quad(P5, P1, P2, P6); // +x
            Quad(P0, P4, P7, P3); // -x
            Quad(P7, P6, P2, P3); // +y
            Quad(P0, P1, P5, P4); // -y
            M = m0;
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Tri(a, b, c);
            Tri(a, c, d);
        }

        /// <summary>A sphere of radius r at p.</summary>
        public void Sphere(Vector3 p, float r, int seg = 10, int rings = 8)
        {
            Vector3 At(int i, int j)
            {
                float th = MathF.PI * j / rings, ph = MathF.Tau * i / seg;
                return p + r * new Vector3(MathF.Sin(th) * MathF.Cos(ph), MathF.Cos(th), MathF.Sin(th) * MathF.Sin(ph));
            }
            for (var j = 0; j < rings; j++)
                for (var i = 0; i < seg; i++)
                    Quad(At(i, j), At(i, j + 1), At(i + 1, j + 1), At(i + 1, j));
        }

        /// <summary>A cylinder along y, radius r, length len, about the middle.</summary>
        public void Cylinder(float r, float len, int seg = 8)
        {
            for (var i = 0; i < seg; i++)
            {
                float a0 = MathF.Tau * i / seg, a1 = MathF.Tau * (i + 1) / seg;
                Vector3 b0 = new(r * MathF.Sin(a0), -len / 2, r * MathF.Cos(a0)), b1 = new(r * MathF.Sin(a1), -len / 2, r * MathF.Cos(a1));
                var t0 = b0 with { Y = len / 2 };
                var t1 = b1 with { Y = len / 2 };
                Quad(b0, b1, t1, t0);
                Tri(new Vector3(0, len / 2, 0), t0, t1);
                Tri(new Vector3(0, -len / 2, 0), b1, b0);
            }
        }

        /// <summary>A box's twelve edges (three's EdgesGeometry of a box).</summary>
        public void BoxEdges(float w, float h, float d)
        {
            float a = w / 2, b = h / 2, c = d / 2;
            var k = new Vector3[] { new(-a, -b, -c), new(a, -b, -c), new(a, b, -c), new(-a, b, -c), new(-a, -b, c), new(a, -b, c), new(a, b, c), new(-a, b, c) };
            foreach (var (i, j) in new[] { (0, 1), (1, 2), (2, 3), (3, 0), (4, 5), (5, 6), (6, 7), (7, 4), (0, 4), (1, 5), (2, 6), (3, 7) })
                Seg(k[i], k[j]);
        }

        public DesignGeo Geo() => new DesignGeo { P = P.ToArray(), C = C.ToArray(), Lines = Lines }.Bound();
    }

    private DesignObj Add(DesignGeo g, Mat m, DesignPick? pick = null, bool pickable = false, Vector3 pos = default, bool vertexColours = false)
    {
        var o = new DesignObj
        {
            Geo = g, Pos = pos, Tint = vertexColours ? Lin(m.Tint) : Lin(m.Hex) * Lin(m.Tint), Rough = m.Rough, Coat = m.Coat, Env = m.Env, Alpha = m.Alpha, Transparent = m.Transparent || m.Alpha < 1,
            DepthWrite = m.DepthWrite && !(m.Transparent && m.Alpha < 1 && !m.DepthWrite), Emissive = Lin(m.Emissive), Unlit = m.Unlit, Metal = m.Metal,
            DepthTest = m.DepthTest, Order = m.Order, Pick = pick, VertexColours = vertexColours,
        };
        o.DepthWrite = m.DepthWrite;
        _sdObjs.Add(o);
        if (pickable)
            _sdPickables.Add(o);
        return o;
    }

    private DesignObj Add(GB b, Mat m, DesignPick? pick = null, bool pickable = false)
    {
        var g = b.Geo();
        _sdOwned.Add(g);
        return Add(g, m, pick, pickable);
    }

    /// <summary>A mesh from the scene ({k, p, c}): by its key, the geometry it had last time; else made from its numbers.</summary>
    private DesignGeo GeoOf(JsonElement m)
    {
        var key = Str(m, "k");
        if (key.Length > 0 && _sdKeyed.TryGetValue(key, out var hit))
        {
            _sdKeyedUsed.Add(key);
            return hit;
        }
        var p = Get(m, "p");
        var c = Get(m, "c");
        var n = p.ValueKind == JsonValueKind.Array ? p.GetArrayLength() : 0;
        var g = new DesignGeo { P = new float[n], C = new uint[n / 3] };
        var i = 0;
        if (n > 0)
            foreach (var v in p.EnumerateArray())
                g.P[i++] = (float)v.GetDouble();
        if (c.ValueKind == JsonValueKind.Array)
        {
            var bytes = new byte[c.GetArrayLength()];
            i = 0;
            foreach (var v in c.EnumerateArray())
                bytes[i++] = (byte)v.GetInt32();
            for (var k = 0; k < g.C.Length && k * 3 + 2 < bytes.Length; k++)
                g.C[k] = bytes[k * 3] | ((uint)bytes[k * 3 + 1] << 8) | ((uint)bytes[k * 3 + 2] << 16) | 0xff000000u;
        }
        g.Bound();
        if (key.Length > 0)
        {
            g.Key = key;
            _sdKeyed[key] = g;
            _sdKeyedUsed.Add(key);
        }
        else
            _sdOwned.Add(g);
        return g;
    }

    private void ClearWorld()
    {
        foreach (var g in _sdOwned)
            ShipDesignerRenderer.Free(g);
        _sdOwned.Clear();
        _sdObjs.Clear();
        _sdPickables.Clear();
        _sdKeyedUsed.Clear();
    }

    private void DropUnused()
    {
        foreach (var hg in _hullGeo.Values)
            if (hg.G.Key != null)
                _sdKeyedUsed.Add(hg.G.Key);
        foreach (var k in _sdKeyed.Keys.Where(k => !_sdKeyedUsed.Contains(k)).ToList())
        {
            ShipDesignerRenderer.Free(_sdKeyed[k]);
            _sdKeyed.Remove(k);
        }
    }

    /// <summary>The page's rebuild(): everything in the yard drawn again from the scene, for the mode and the view toggles.</summary>
    private void Rebuild()
    {
        if (!_sdLoaded)
            return;
        ClearWorld();
        ShipDesignerRenderer.Cut = null;
        if (Has(S, "roomView"))
        {
            // Room view: nothing of the ship is drawn (the model sent no hull, connection or piece mesh), only the rooms it lists.
            var shownRooms = Arr(S, "roomView").Select(x => x.GetInt32()).ToHashSet();
            DrawRooms(shownRooms);
            DrawPassages(shownRooms);
            if (_vPerson)
                DrawPerson();
            ApplyCut();
            DrawGrid();
            DropUnused();
            _gizmoHull = 0;
            return;
        }
        var ghost = _vXray || Inside || _pickRoomFor != 0;
        var fresh = Arr(S, "hulls").All(h => Has(h, "copies"));
        if (fresh)
            _hullGeo.Clear();
        var hideHull = !_vHull && _sdMode != "hull";
        var ghostMat = new Mat(Alpha: Inside ? 0.12f : 0.28f, Transparent: true, DepthWrite: false);
        foreach (var h in Arr(S, "hulls"))
        {
            if (hideHull)
                continue;
            var hid = I(h, "id");
            var hpos = new Vector3((float)D(h, "x"), (float)D(h, "y"), (float)D(h, "z"));
            var hasCopies = Has(h, "copies");
            var copies = hasCopies ? Arr(h, "copies").Select(c => (I(c, "copy"), (JsonElement?)c)).ToList()
                : (_hullCopies.TryGetValue(hid, out var kept) ? kept.Select(k => (k.Copy, (JsonElement?)null)).ToList() : []);
            foreach (var (copy, c) in copies)
            {
                foreach (var surf in new[] { "skin", "fore", "aft", "skinClad", "foreClad", "aftClad" })
                {
                    var isClad = surf.EndsWith("Clad");
                    var clad = Get(h, "clad");
                    var cm = Str(clad, "material");
                    var hm = Str(h, "material", "Painted");
                    var look = isClad ? MatNamed(cm.Length > 0 && cm != "Hull" ? cm : hm, Found(clad) && Has(clad, "damage") ? D(clad, "damage") : D(h, "damage")) : MatNamed(hm);
                    var key = $"{hid}/{copy}/{surf}";
                    if (c is { } ce)
                    {
                        var m = Get(ce, surf);
                        if (!Found(m) || Get(m, "p").GetArrayLength() == 0)
                            continue;
                        _hullGeo[key] = (GeoOf(m), hpos);
                    }
                    if (!_hullGeo.TryGetValue(key, out var hg))
                        continue;
                    var selHull = _selKind == "hull" && _selId == hid && _sdMode == "hull";
                    var mat = ghost ? look with { Alpha = ghostMat.Alpha, Transparent = true, DepthWrite = false } : look;
                    if (selHull)
                        mat = mat with { Emissive = 0x0d2a48 };
                    var off = new Vector3((copy == 1 ? -1 : 1) * (hpos.X - hg.At.X), hpos.Y - hg.At.Y, hpos.Z - hg.At.Z);
                    Add(hg.G, mat, new DesignPick("hull", Hull: hid, Copy: copy, Surface: isClad ? surf[..^4] : surf), true, off, true);
                }
            }
            if (_sdMode == "hull" && _selKind == "hull" && _selId == hid)
                DrawRings(h);
        }
        foreach (var j in Arr(S, "joins"))
        {
            var mesh = Get(j, "mesh");
            if (Get(mesh, "p").GetArrayLength() == 0 || hideHull)
                continue;
            var jl = MatNamed(Str(j, "material", "Painted"));
            var mat = ghost ? jl with { Alpha = ghostMat.Alpha, Transparent = true, DepthWrite = false } : jl;
            if (_selKind == "join" && _selId == I(j, "id"))
                mat = mat with { Emissive = 0x1d4f86 };
            Add(GeoOf(mesh), mat, new DesignPick("join", I(j, "id")), true, default, true);
        }
        foreach (var p in Arr(S, "pieces"))
        {
            if (!_vParts && _sdMode != "pieces")
                continue;
            var mat = Inside ? new Mat(Alpha: 0.35f, Transparent: true, DepthWrite: false) : new Mat(0xffffff);
            if (_selKind == "piece" && _selId == I(p, "id"))
                mat = mat with { Emissive = 0x1d4f86 };
            if (!B(p, "ok", true))
                mat = mat with { Emissive = 0x5a1408 };
            Add(GeoOf(Get(p, "mesh")), mat, new DesignPick("piece", I(p, "id")), true, default, true);
        }
        if (_vRooms && (Inside || _vXray || _pickRoomFor != 0))
            DrawRooms();
        if (_vParts)
            DrawConnectors();
        if (Inside || _vXray)
            DrawRisers();
        if (Inside || _vXray)
            DrawPassages();
        if (_vHull || Inside)
            DrawHullWindows();
        if (_vLinks && _sdMode != "hull")
            DrawLinks();
        if (Inside)
            DrawDeckPlane();
        if (_vPerson)
            DrawPerson();
        DrawGrid();
        DropUnused();
        PlaceGizmo();
    }

    private void DrawGrid()
    {
        // three's GridHelper(200, 100): lines every 2 m, the middle ones lighter.
        var b = new GB { Lines = true };
        var c = new GB { Lines = true };
        for (var i = 0; i <= 100; i++)
        {
            var t = -100 + i * 2f;
            var g = i == 50 ? c : b;
            g.Seg(new Vector3(-100, _sdGridY, t), new Vector3(100, _sdGridY, t));
            g.Seg(new Vector3(t, _sdGridY, -100), new Vector3(t, _sdGridY, 100));
        }
        Add(b, new Mat(0x26323a, Unlit: true));
        Add(c, new Mat(0x3a4852, Unlit: true));
    }

    // A person for scale (1.8 m), standing on the floor of the bridge (else the first room), else beside the ship.
    private void DrawPerson()
    {
        var rooms = Arr(S, "rooms").ToList();
        var r = rooms.FirstOrDefault(x => Str(x, "kind") == "Bridge");
        if (!Found(r) && rooms.Count > 0)
            r = rooms[0];
        var g = new GB();
        Vector3 at;
        var bx = Found(r) ? Nums(Get(r, "box")) : [];
        if (bx.Length == 6)
            at = new Vector3((float)(bx[0] + bx[1]) / 2, (float)bx[2], (float)(bx[4] + bx[5]) / 2);
        else
            at = new Vector3(Arr(S, "hulls").Select(h => (float)Math.Abs(D(h, "x"))).DefaultIfEmpty(0).Max() + 12, 0, 0);
        if (Stacked && bx.Length == 6)
            g.M = Matrix4x4.CreateRotationX(-MathF.PI / 2) * Matrix4x4.CreateTranslation((float)(bx[0] + bx[1]) / 2, (float)(bx[2] + bx[3]) / 2, (float)bx[5]);
        else
            g.M = Matrix4x4.CreateTranslation(at);
        g.Box(0.16f, 0.85f, 0.2f, -0.11f, 0.425f, 0);
        g.Box(0.16f, 0.85f, 0.2f, 0.11f, 0.425f, 0); // legs
        g.Box(0.46f, 0.62f, 0.26f, 0, 1.17f, 0); // body
        g.Box(0.11f, 0.6f, 0.13f, -0.3f, 1.16f, 0);
        g.Box(0.11f, 0.6f, 0.13f, 0.3f, 1.16f, 0); // arms
        g.Box(0.22f, 0.26f, 0.24f, 0, 1.64f, 0); // head (top at 1.77 m)
        Add(g, new Mat(0xe8792b, Metal: 0));
    }

    private void DrawRings(JsonElement h)
    {
        var hid = I(h, "id");
        float hx = (float)D(h, "x"), hy = (float)D(h, "y"), hz = (float)D(h, "z");
        var i = 0;
        foreach (var s in Arr(h, "stations"))
        {
            var p = Arr(s, "p").Select(q => (X: (float)q[0].GetDouble(), Y: (float)q[1].GetDouble())).ToList();
            var sz = (float)D(s, "z");
            for (var c = 0; c < (B(h, "mirror") && Math.Abs(hx) > 0.01 ? 2 : 1); c++)
            {
                var cx = c == 1 ? -hx : hx;
                var ring = new List<(float X, float Y)> { (0, p[0].Y) };
                ring.AddRange(p);
                ring.Add((0, p[^1].Y));
                var pts = new List<Vector3>();
                foreach (var (x, y) in ring)
                    pts.Add(new Vector3(cx + x, hy + y, hz + sz));
                for (var k = ring.Count - 1; k >= 0; k--)
                    pts.Add(new Vector3(cx - ring[k].X, hy + ring[k].Y, hz + sz));
                var g = new GB { Lines = true };
                for (var k = 0; k + 1 < pts.Count; k++)
                    g.Seg(pts[k], pts[k + 1]);
                Add(g, new Mat(_selStation == i ? 0xffb02eu : 0x6fb6ffu, Unlit: true, DepthTest: false, Transparent: true, Order: 5), new DesignPick("station", Hull: hid, I: i), true);
            }
            i++;
        }
    }

    // A room's extensions: two rings of the same count, sides between them and a fan cap on each, in its colour.
    private void DrawExts(JsonElement r, bool dim)
    {
        var col = HexOf(Str(r, "colour"));
        // The model's mesh of them all, bent inside the skin; the rings (pulled in too) give the outline.
        var em = Get(r, "extMesh");
        var hasMesh = Found(em) && Get(em, "p").GetArrayLength() > 0;
        if (hasMesh)
            Add(GeoOf(em), new Mat(Alpha: dim ? 0.06f : 0.3f, Transparent: true, DepthWrite: false, Emissive: _selKind == "room" && _selId == I(r, "id") ? 0x2a4f80u : 0),
                new DesignPick("room", I(r, "id")), !dim || _mergeFrom != 0 || _linking != null, default, true);
        foreach (var e in Arr(r, "exts"))
        {
            var A = Nums(Get(e, "a"));
            var Bv = Nums(Get(e, "b"));
            var n = A.Length / 3;
            if (n < 3 || Bv.Length != A.Length)
                continue;
            Vector3 Pa(int k) => new((float)A[3 * k], (float)A[3 * k + 1], (float)A[3 * k + 2]);
            Vector3 Pb(int k) => new((float)Bv[3 * k], (float)Bv[3 * k + 1], (float)Bv[3 * k + 2]);
            var g = new GB();
            var l = new GB { Lines = true };
            Vector3 ca = Vector3.Zero, cb = Vector3.Zero;
            for (var i = 0; i < n; i++)
            {
                var j = (i + 1) % n;
                g.Tri(Pa(i), Pa(j), Pb(j));
                g.Tri(Pa(i), Pb(j), Pb(i));
                ca += Pa(i);
                cb += Pb(i);
                l.Seg(Pa(i), Pa(j));
                l.Seg(Pb(i), Pb(j));
                l.Seg(Pa(i), Pb(i));
            }
            ca /= n;
            cb /= n;
            for (var i = 0; i < n; i++)
            {
                g.Tri(ca, Pa(i), Pa((i + 1) % n));
                g.Tri(cb, Pb(i), Pb((i + 1) % n));
            }
            var picked = _selKind == "room" && _selId == I(r, "id");
            if (!hasMesh)
                Add(g, new Mat(col, Alpha: dim ? 0.06f : 0.3f, Transparent: true, DepthWrite: false, Emissive: picked ? 0x2a4f80u : 0), new DesignPick("room", I(r, "id")), !dim || _mergeFrom != 0 || _linking != null);
            Add(l, new Mat(col, Alpha: dim ? 0.2f : 1, Transparent: dim, Unlit: true));
        }
    }

    private void DrawRooms(HashSet<int>? only = null)
    {
        var decks = Arr(S, "decks").ToList();
        var rooms = Arr(S, "rooms").ToList();
        var stacked = Stacked;
        double? fl = _sdDeck < decks.Count ? D(decks[_sdDeck], "floor") : null;
        foreach (var r in rooms)
        {
            var rd = I(r, "deck");
            if (rd >= decks.Count)
                continue;
            var id = I(r, "id");
            if (only != null && !only.Contains(id))
                continue;
            var b = Nums(Get(r, "box"));
            if (b.Length < 6)
                continue;
            // Dimmed off its deck, unless it runs through the deck shown (a room merged across decks, or one rising through them).
            var through = fl is { } f && (stacked ? b[4] < f - 0.1 && b[5] >= f - 0.01 : b[2] <= f + 0.01 && b[3] > f + 0.1);
            var dim = only == null && Inside && rd != _sdDeck && !through;
            var pickRoom = !dim || _mergeFrom != 0 || _linking != null;
            var picked = _selKind == "room" && _selId == id;
            var col = HexOf(Str(r, "colour"));
            var ok = B(r, "ok", true);
            DrawExts(r, dim);
            var pick = new DesignPick("room", id);
            if (Has(r, "wallMesh"))
            {
                // In Room view see-through, so its interior shows from outside.
                var see = only != null && !_roomOpaque;
                Add(GeoOf(Get(r, "wallMesh")), new Mat(Alpha: dim ? 0.15f : see ? 0.3f : 1, Transparent: dim || see, DepthWrite: !see, Emissive: picked ? 0x1a3050u : 0), pick, pickRoom, default, true);
            }
            var im = Get(r, "intMesh");
            if (Found(im) && Get(im, "p").GetArrayLength() > 0)
            {
                // Its interior cladding; the glow mesh unlit, so its strips read as lamps.
                Add(GeoOf(im), new Mat(Alpha: dim ? 0.12f : 1, Transparent: dim, Metal: 0.25f), pick, false, default, true);
                var ig = Get(r, "intGlow");
                if (Found(ig) && Get(ig, "p").GetArrayLength() > 0)
                    Add(GeoOf(ig), new Mat(Alpha: dim ? 0.12f : 1, Transparent: dim, Unlit: true), null, false, default, true);
            }
            if (Has(r, "mesh"))
            {
                // A room out to the hull follows the skin: drawn from its own shape.
                var group = I(r, "group");
                var selGroup = _selKind == "room" && (_selId == id || (group != 0 && I(ById(rooms, _selId), "group") == group));
                var solid = only != null && _roomOpaque && !dim;
                Add(GeoOf(Get(r, "mesh")), new Mat(Alpha: solid ? 1 : dim ? 0.08f : 0.38f, Transparent: !solid, DepthWrite: solid, Emissive: selGroup ? 0x2a4f80u : (!ok ? 0x6a1a0au : 0)), pick, pickRoom, default, true);
                continue;
            }
            float bx0 = (float)b[0], bx1 = (float)b[1], by0 = (float)b[2], by1 = (float)b[3], bz0 = (float)b[4], bz1 = (float)b[5];
            var grp = I(r, "group");
            var gap = grp != 0 && rooms.Any(o => I(o, "id") != id && I(o, "group") == grp) ? 0 : 0.06f; // a room in parts: one floor, no seams
            var floor = new GB();
            if (stacked)
                floor.Box(bx1 - bx0 - gap, by1 - by0 - gap, 0.08f, (bx0 + bx1) / 2, (by0 + by1) / 2, bz1 - 0.043f);
            else
                floor.Box(bx1 - bx0 - gap, 0.08f, bz1 - bz0 - gap, (bx0 + bx1) / 2, by0 + 0.043f, (bz0 + bz1) / 2);
            Add(floor, new Mat(col, Alpha: dim ? 0.25f : 1, Transparent: dim, Metal: 0, Emissive: picked ? 0x2a4f80u : (!ok ? 0x6a1a0au : 0)), new DesignPick("room", id, Floor: true), pickRoom);
            // Its box bent inside the skin where the hull curves over it (the model's mesh); a plain box only if none came.
            var boxMat = new Mat(col, Alpha: dim ? 0.03f : 0.1f, Transparent: true, DepthWrite: false, Unlit: true);
            if (Has(r, "boxMesh"))
                Add(GeoOf(Get(r, "boxMesh")), boxMat, pick, pickRoom);
            else
            {
                var bg = new GB();
                bg.Box(bx1 - bx0 - 0.06f, by1 - by0 - 0.06f, bz1 - bz0 - 0.06f, (bx0 + bx1) / 2, (by0 + by1) / 2, (bz0 + bz1) / 2);
                Add(bg, boxMat, pick, pickRoom);
            }
            // Its outline; a room in parts leaves out the edges on the walls its parts share, so it reads as one.
            var mates = grp != 0 ? rooms.Where(o => I(o, "id") != id && I(o, "group") == grp && I(o, "deck") == rd && !Has(o, "mesh")).Select(o => Nums(Get(o, "box"))).ToList() : [];
            bool OnMate(Vector3 q) => mates.Any(o => o.Length == 6 && q.X >= o[0] - 0.05 && q.X <= o[1] + 0.05 && q.Y >= o[2] - 0.05 && q.Y <= o[3] + 0.05 && q.Z >= o[4] - 0.05 && q.Z <= o[5] + 0.05);
            var C = new Vector3[] { new(bx0, by0, bz0), new(bx1, by0, bz0), new(bx1, by1, bz0), new(bx0, by1, bz0), new(bx0, by0, bz1), new(bx1, by0, bz1), new(bx1, by1, bz1), new(bx0, by1, bz1) };
            var edges = new GB { Lines = true };
            foreach (var (i, j) in new[] { (0, 1), (1, 2), (2, 3), (3, 0), (4, 5), (5, 6), (6, 7), (7, 4), (0, 4), (1, 5), (2, 6), (3, 7) })
                if (!OnMate((C[i] + C[j]) / 2))
                    edges.Seg(C[i], C[j]);
            var ec = !ok ? 0xff6a50u : !B(r, "reached") ? 0xffb02eu : col;
            Add(edges, new Mat(ec, Alpha: dim ? 0.2f : 0.85f, Transparent: true, Unlit: true));
        }
        // Placed doors: a frame you can pick and drag along the wall the two rooms share.
        if (_sdMode == "connectors")
            foreach (var p in Arr(S, "placedDoors"))
                DrawDoor(p);
        bool NearShown(Vector3 q) => rooms.Any(r => only!.Contains(I(r, "id")) && Nums(Get(r, "box")) is { Length: 6 } o
            && q.X >= o[0] - 0.3 && q.X <= o[1] + 0.3 && q.Y >= o[2] - 0.3 && q.Y <= o[3] + 0.3 && q.Z >= o[4] - 0.3 && q.Z <= o[5] + 0.3);
        foreach (var d in Arr(S, "doors"))
        {
            var v = V3(d);
            if (only != null && !NearShown(v))
                continue;
            var g = new GB();
            if (stacked)
                g.Box(0.7f, 0.7f, 0.04f, v.X, v.Y, v.Z - 0.1f);
            else
                g.Box(0.7f, 0.04f, 0.7f, v.X, v.Y + 0.1f, v.Z);
            Add(g, new Mat(0xffc35a, Unlit: true));
        }
    }

    private void DrawDoor(JsonElement p)
    {
        var id = I(p, "id");
        var s0 = V3(Get(p, "s0"));
        var s1 = V3(Get(p, "s1"));
        var along = s1 - s0;
        var acrossX = !(Math.Abs(along.X) > 1e-3 || along.Length() < 1e-3);
        var picked = _selKind == "door" && _selId == id;
        var m = Matrix4x4.Identity;
        if (acrossX)
            m *= Matrix4x4.CreateRotationY(MathF.PI / 2); // in a wall along z: turn it to face across x
        if (Stacked)
            m *= Matrix4x4.CreateRotationX(-MathF.PI / 2);
        m *= Matrix4x4.CreateTranslation(V3(Get(p, "at")));
        var frame = new GB { M = m };
        var pane = new GB { M = m };
        var kind = Str(p, "kind");
        float w = (float)D(p, "w"), h = (float)D(p, "h");
        if (kind != "Window")
        {
            frame.Box(0.1f, h, 0.2f, -w / 2 - 0.05f, h / 2, 0);
            frame.Box(0.1f, h, 0.2f, w / 2 + 0.05f, h / 2, 0);
            frame.Box(w + 0.2f, 0.1f, 0.2f, 0, h + 0.05f, 0);
            if (w > 1.3f)
            {
                pane.Box(w / 2 - 0.02f, h - 0.03f, 0.08f, -w / 4, h / 2, 0);
                pane.Box(w / 2 - 0.02f, h - 0.03f, 0.08f, w / 4, h / 2, 0); // two leaves
            }
            else
                pane.Box(w, h - 0.03f, 0.08f, 0, h / 2, 0);
        }
        else
        {
            // A window's outline as points round its middle (sill + h/2 up): a rectangle, a circle, an oval or an arch.
            var shape = Str(p, "shape");
            if (shape == "Round")
                h = w;
            var cy = (float)D(p, "sill") + h / 2;
            var pts = new List<Vector2>();
            if (shape == "Rect")
                pts.AddRange([new(-w / 2, -h / 2), new(w / 2, -h / 2), new(w / 2, h / 2), new(-w / 2, h / 2)]);
            else if (shape == "Arch")
            {
                float rr = w / 2, sh = MathF.Max(0, h - rr);
                pts.AddRange([new(-rr, -h / 2), new(rr, -h / 2), new(rr, -h / 2 + sh)]);
                for (var i = 1; i < 12; i++)
                {
                    var t = MathF.PI * i / 12;
                    pts.Add(new Vector2(rr * MathF.Cos(t), -h / 2 + sh + rr * MathF.Sin(t)));
                }
                pts.Add(new Vector2(-rr, -h / 2 + sh));
            }
            else
                for (var i = 0; i < 24; i++)
                {
                    var t = MathF.Tau * i / 24;
                    pts.Add(new Vector2(w / 2 * MathF.Cos(t), h / 2 * MathF.Sin(t)));
                }
            for (var i = 1; i + 1 < pts.Count; i++)
                pane.Tri(new Vector3(pts[0].X, cy + pts[0].Y, 0), new Vector3(pts[i].X, cy + pts[i].Y, 0), new Vector3(pts[i + 1].X, cy + pts[i + 1].Y, 0));
            for (var i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                frame.Box(Vector2.Distance(a, b) + 0.06f, 0.08f, 0.18f, (a.X + b.X) / 2, cy + (a.Y + b.Y) / 2, 0, MathF.Atan2(b.Y - a.Y, b.X - a.X));
            }
        }
        var em = picked ? 0x1d4f86u : 0;
        Add(frame, new Mat(0xd8b04a, Metal: 0.4f, Emissive: em, Order: 6), new DesignPick("door", id), true);
        Add(pane, new Mat(kind == "Window" ? 0x9fd3ffu : 0x5c6b7au, Alpha: kind == "Window" ? 0.35f : 0.55f, Transparent: true, Emissive: em, Order: 6), new DesignPick("door", id), true);
    }

    // Ladders and lifts between decks: a ladder is two rails and rungs up a well, a lift a glass shaft with corner posts and its car
    // at the lowest stop; amber frames mark where it passes each deck. Pickable in the interior.
    private void DrawRisers()
    {
        foreach (var k in Arr(S, "risers"))
        {
            var a = V3(Get(k, "a"));
            var b = V3(Get(k, "b"));
            var len = Vector3.Distance(a, b);
            if (len < 0.1f)
                continue;
            var m = (Stacked ? Matrix4x4.CreateRotationX(-MathF.PI / 2) : Matrix4x4.Identity) * Matrix4x4.CreateTranslation(a);
            var picked = _selKind == "riser" && _selId == I(k, "id");
            var amber = new GB { M = m };
            var steel = new GB { M = m };
            float hx = (float)D(k, "hx"), hz = (float)D(k, "hz");
            var stops = Nums(Get(k, "stops"));
            var lo = stops.Length > 0 ? stops.Min() : 0;
            foreach (var f in stops)
            {
                float y = (float)Math.Abs(f - lo) + 0.02f, w = 2 * hx + 0.1f, d = 2 * hz + 0.1f;
                amber.Box(w, 0.05f, 0.08f, 0, y, d / 2);
                amber.Box(w, 0.05f, 0.08f, 0, y, -d / 2);
                amber.Box(0.08f, 0.05f, d, w / 2, y, 0);
                amber.Box(0.08f, 0.05f, d, -w / 2, y, 0);
            }
            var lift = Str(k, "kind") == "Lift";
            if (lift)
            {
                foreach (var (x, z) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                    steel.Box(0.08f, len, 0.08f, x * hx, len / 2, z * hz);
                steel.Box(2 * hx - 0.1f, 0.12f, 2 * hz - 0.1f, 0, 0.06f, 0); // the car's floor at the lowest stop
                steel.Box(2 * hx - 0.1f, 0.05f, 2 * hz - 0.1f, 0, 2.3f, 0); // and its roof
            }
            else
            {
                var z = -hz + 0.08f;
                foreach (var x in new[] { -0.22f, 0.22f })
                    steel.Box(0.05f, len, 0.05f, x, len / 2, z);
                for (var y = 0.3f; y < len; y += 0.3f)
                    steel.Box(0.44f, 0.035f, 0.035f, 0, y, z);
            }
            var em = picked ? 0x1d4f86u : 0;
            Add(amber, new Mat(B(k, "ok") ? 0xd8b04au : 0xff5a44u, Metal: 0.5f, Emissive: em, Order: 5));
            Add(steel, new Mat(0x9aa3ad, Metal: 0.6f, Emissive: em, Order: 5));
            var hit = new GB { M = m };
            hit.Box(2 * hx, len, 2 * hz, 0, len / 2, 0);
            Add(hit, new Mat(lift ? 0x9fd3ffu : 0xd8b04au, Alpha: lift ? 0.14f : 0.06f, Transparent: true, DepthWrite: false, Unlit: true, Order: 5), new DesignPick("riser", I(k, "id")), true);
        }
    }

    // Windows in the hull: glass laid on the skin from a grid of points, so it follows the contour, with a frame round its edge.
    private void DrawHullWindows()
    {
        foreach (var w in Arr(S, "hullWindows"))
        {
            int nu = I(w, "nu"), nv = I(w, "nv");
            if (nu < 2 || nv < 2)
                continue;
            var pts = Nums(Get(w, "pts"));
            var id = I(w, "id");
            var picked = _selKind == "hullwin" && _selId == id;
            Vector3 P(double[] q, int k) => new((float)q[3 * k], (float)q[3 * k + 1], (float)q[3 * k + 2]);
            // A shaped window is a fan of rings from its middle: row nv - 1 is its rim, and it wraps round.
            var shaped = Str(w, "shape") is { Length: > 0 } sh && sh != "Rect";
            var g = new GB();
            for (var j = 0; j < nv - 1; j++)
                for (var i = 0; i < (shaped ? nu : nu - 1); i++)
                {
                    int a = j * nu + i, b = j * nu + (i + 1) % nu, c = a + nu, d = b + nu;
                    if (3 * Math.Max(c, d) + 2 >= pts.Length)
                        continue;
                    g.Tri(P(pts, a), P(pts, b), P(pts, d));
                    g.Tri(P(pts, a), P(pts, d), P(pts, c));
                }
            var glassMat = new Mat(B(w, "ok") ? 0x9fd3ffu : 0xff8a70u, Alpha: 0.55f, Transparent: true, DepthWrite: false, Metal: 0.3f, Emissive: picked ? 0x1d4f86u : 0x0b2236u, Order: 5);
            var wn = Nums(Get(w, "n"));
            var glazed = wn.Length == 3 && D(w, "depth") > 0;
            if (!glazed || B(w, "double", true))
                Add(g, glassMat, new DesignPick("hullwin", id), true);
            var ring = new List<Vector3>();
            if (shaped)
                for (var i = 0; i <= nu; i++)
                    ring.Add(P(pts, (nv - 1) * nu + i % nu));
            else
            {
                for (var i = 0; i < nu; i++)
                    ring.Add(P(pts, i));
                for (var j = 1; j < nv; j++)
                    ring.Add(P(pts, j * nu + nu - 1));
                for (var i = nu - 2; i >= 0; i--)
                    ring.Add(P(pts, (nv - 1) * nu + i));
                for (var j = nv - 2; j >= 0; j--)
                    ring.Add(P(pts, j * nu));
            }
            if (glazed)
            {
                // Double glazing: the glass above is the outer pane, on the skin; an inner pane sits depth in along the window's ray,
                // and a reveal lines the tunnel between their rims. Single glazed: the one pane at that depth instead.
                var dn = new Vector3((float)wn[0], (float)wn[1], (float)wn[2]) * -(float)Math.Max(0.05, D(w, "depth"));
                var ig = new GB { M = Matrix4x4.CreateTranslation(dn) };
                for (var j = 0; j < nv - 1; j++)
                    for (var i = 0; i < (shaped ? nu : nu - 1); i++)
                    {
                        int a = j * nu + i, b = j * nu + (i + 1) % nu, c = a + nu, d = b + nu;
                        if (3 * Math.Max(c, d) + 2 >= pts.Length)
                            continue;
                        ig.Tri(P(pts, a), P(pts, b), P(pts, d));
                        ig.Tri(P(pts, a), P(pts, d), P(pts, c));
                    }
                Add(ig, glassMat, new DesignPick("hullwin", id), true);
                var rv = new GB();
                for (var k = 0; k + 1 < ring.Count; k++)
                    rv.Quad(ring[k], ring[k + 1], ring[k + 1] + dn, ring[k] + dn);
                Add(rv, new Mat(0x4b5058, Metal: 0.3f), new DesignPick("hullwin", id), true);
            }
            var fl = new GB { Lines = true };
            for (var k = 0; k + 1 < ring.Count; k++)
                fl.Seg(ring[k], ring[k + 1]);
            Add(fl, new Mat(picked ? 0x6fb0ffu : 0x2f3237u, Unlit: true, Order: 6));
            // Boarding round the glass: a band 10 cm wide laid on the skin by the same rays as the glass (fpts).
            int fnu = I(w, "fnu"), fnv = I(w, "fnv");
            if (fnu < 2 || fnv < 2)
                continue;
            var fp = Nums(Get(w, "fpts"));
            List<Vector3>[] Sides(double[] q, int a, int b)
            {
                var s4 = new[] { new List<Vector3>(), new List<Vector3>(), new List<Vector3>(), new List<Vector3>() };
                for (var i = 0; i < a; i++) s4[0].Add(P(q, i));
                for (var j = 0; j < b; j++) s4[1].Add(P(q, j * a + a - 1));
                for (var i = a - 1; i >= 0; i--) s4[2].Add(P(q, (b - 1) * a + i));
                for (var j = b - 1; j >= 0; j--) s4[3].Add(P(q, j * a));
                return s4;
            }
            static List<Vector3> Resample(List<Vector3> line, int K)
            {
                var L = new List<float> { 0 };
                for (var i = 1; i < line.Count; i++)
                    L.Add(L[i - 1] + Vector3.Distance(line[i], line[i - 1]));
                var T = L[^1] > 0 ? L[^1] : 1;
                var o = new List<Vector3>();
                for (var k = 0; k < K; k++)
                {
                    var t = T * k / (K - 1);
                    var i = 1;
                    while (i < line.Count - 1 && L[i] < t)
                        i++;
                    var den = L[i] - L[i - 1];
                    var f = (t - L[i - 1]) / (den != 0 ? den : 1);
                    o.Add(Vector3.Lerp(line[i - 1], line[i], Math.Clamp(f, 0, 1)));
                }
                return o;
            }
            var inner = new List<Vector3>();
            var outer = new List<Vector3>();
            if (shaped)
                for (var i = 0; i < nu; i++)
                {
                    inner.Add(P(pts, (nv - 1) * nu + i));
                    outer.Add(P(fp, (fnv - 1) * fnu + i % fnu));
                }
            else
            {
                var si = Sides(pts, nu, nv);
                var so = Sides(fp, fnu, fnv);
                for (var e = 0; e < 4; e++)
                {
                    var K = Math.Max(si[e].Count, so[e].Count) * 2;
                    var a = Resample(si[e], K);
                    var b = Resample(so[e], K);
                    a.RemoveAt(a.Count - 1);
                    b.RemoveAt(b.Count - 1);
                    inner.AddRange(a);
                    outer.AddRange(b);
                }
            }
            var bd = new GB();
            var N = inner.Count;
            for (var k = 0; k < N; k++)
            {
                var k1 = (k + 1) % N;
                bd.Tri(inner[k], inner[k1], outer[k1]);
                bd.Tri(inner[k], outer[k1], outer[k]);
            }
            Add(bd, new Mat(picked ? 0x4a6f9au : 0x3a3e44u, Metal: 0.4f), new DesignPick("hullwin", id), true);
        }
    }

    private static Matrix4x4 FromTo(Vector3 from, Vector3 to)
    {
        var q = Quaternion.Identity;
        var d = Vector3.Dot(from, to);
        if (d < -0.999999f)
        {
            var axis = Vector3.Cross(Vector3.UnitX, from);
            if (axis.LengthSquared() < 1e-6f)
                axis = Vector3.Cross(Vector3.UnitY, from);
            q = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        else if (d < 0.999999f)
        {
            var c = Vector3.Cross(from, to);
            q = Quaternion.Normalize(new Quaternion(c.X, c.Y, c.Z, 1 + d));
        }
        return Matrix4x4.CreateFromQuaternion(q);
    }

    // A socket's connector to a room farther than its reach: a lined shaft, with rungs when it climbs and a hatch frame at the
    // room's end; a drive's service socket is a panel with its feed. Drawn with the parts, whether or not the links are shown.
    private void DrawConnectors()
    {
        foreach (var l in Arr(S, "links"))
        {
            var a = V3(Get(l, "a"));
            var b = V3(Get(l, "b"));
            var ok = B(l, "ok");
            if (B(l, "service"))
            {
                var d = a - b;
                var nn = V3(Get(l, "n"));
                var dir = Vector3.Normalize(nn.LengthSquared() > 1e-6f ? nn : d.LengthSquared() > 1e-9f ? d : Vector3.UnitZ);
                var m = FromTo(Vector3.UnitZ, dir) * Matrix4x4.CreateTranslation(b);
                var steel = new GB { M = m };
                var amber = new GB { M = m };
                steel.Box(0.7f, 0.7f, 0.06f, 0, 0, 0.03f);
                foreach (var (x, y, w, h) in new[] { (0f, 0.37f, 0.8f, 0.05f), (0f, -0.37f, 0.8f, 0.05f), (0.37f, 0f, 0.05f, 0.8f), (-0.37f, 0f, 0.05f, 0.8f) })
                    amber.Box(w, h, 0.08f, x, y, 0.04f);
                var len = MathF.Max(0.05f, d.Length());
                steel.M = Matrix4x4.CreateRotationX(MathF.PI / 2) * Matrix4x4.CreateTranslation(0, 0, len / 2) * m;
                steel.Cylinder(0.06f, len);
                Add(steel, new Mat(0x6d7682, Metal: 0.5f, Order: 4));
                Add(amber, new Mat(ok ? 0xd8b04au : 0xff5a44u, Metal: 0.4f, Order: 4));
                continue;
            }
            var shaft = (float)D(l, "shaft");
            if (!(shaft > 0))
                continue;
            var dd = b - a;
            var ln = dd.Length();
            if (ln < 0.1f)
                continue;
            var wv = shaft + 0.2f;
            var dn = dd / ln;
            var gm = FromTo(Vector3.UnitY, dn) * Matrix4x4.CreateTranslation(a + dd * 0.5f);
            var tube = new GB { M = gm };
            tube.Box(wv, ln, wv, 0, 0, 0);
            Add(tube, new Mat(0x6d7682, Alpha: 0.55f, Transparent: true, DepthWrite: false, Metal: 0.4f, Order: 4));
            var edge = new GB { M = gm, Lines = true };
            edge.BoxEdges(wv, ln, wv);
            Add(edge, new Mat(ok ? 0xd8b04au : 0xff5a44u, Unlit: true, Order: 4));
            var am = new GB { M = gm };
            foreach (var y in new[] { -ln / 2, ln / 2 })
                foreach (var (sx, sz, bx, bz) in new[] { (0f, wv / 2, wv + 0.1f, 0.08f), (0f, -wv / 2, wv + 0.1f, 0.08f), (wv / 2, 0f, 0.08f, wv + 0.1f), (-wv / 2, 0f, 0.08f, wv + 0.1f) })
                    am.Box(bx, 0.1f, bz, sx, y, sz);
            Add(am, new Mat(0xd8b04a, Metal: 0.5f, Order: 4));
            var st = new GB { M = gm };
            if (Str(l, "sock") == "ramp")
            {
                // A boarding ramp's passage is a stair from the room's floor down to the ramp at the hatch: treads every 0.2 m of rise.
                var n = Math.Max(2, (int)Math.Round(ln * Math.Abs(dn.Y) / 0.2f));
                for (var k = 0; k < n; k++)
                    st.Box(wv - 0.1f, 0.05f, MathF.Max(0.22f, ln / n * 1.1f), 0, ln / 2 - (k + 0.5f) * ln / n, 0);
            }
            else if (Math.Abs(dn.Y) > 0.6f)
            {
                // A ladder up its side when it climbs more than it runs: two rails and rungs every 0.3 m.
                var z = -wv / 2 + 0.12f;
                foreach (var x in new[] { -0.22f, 0.22f })
                    st.Box(0.04f, ln, 0.04f, x, 0, z);
                for (var y = -ln / 2 + 0.15f; y < ln / 2; y += 0.3f)
                    st.Box(0.44f, 0.03f, 0.03f, 0, y, z);
            }
            if (st.P.Count > 0)
                Add(st, new Mat(0x9aa3ad, Metal: 0.6f, Order: 4));
        }
    }

    private void DrawLinks()
    {
        foreach (var l in Arr(S, "links"))
        {
            var a = V3(Get(l, "a"));
            var b = V3(Get(l, "b"));
            var col = B(l, "ok") ? 0x5cd07au : 0xff5a44u;
            // Dashed, as the page draws it: 0.3 m on, 0.2 m off.
            var g = new GB { Lines = true };
            var len = Vector3.Distance(a, b);
            if (len > 1e-4f)
            {
                var d = (b - a) / len;
                for (var t = 0f; t < len; t += 0.5f)
                    g.Seg(a + d * t, a + d * MathF.Min(len, t + 0.3f));
            }
            Add(g, new Mat(col, Unlit: true, DepthTest: false, Transparent: true, Order: 6));
            var dot = new GB();
            dot.Sphere(b, 0.22f);
            Add(dot, new Mat(col, Unlit: true, DepthTest: false, Transparent: true, Order: 6));
        }
    }

    private void DrawDeckPlane()
    {
        var decks = Arr(S, "decks").ToList();
        if (_sdDeck >= decks.Count)
            return;
        var f = (float)D(decks[_sdDeck], "floor");
        // three's GridHelper(80, 160): lines every half metre.
        var g = new GB { Lines = true };
        var c = new GB { Lines = true };
        for (var i = 0; i <= 160; i++)
        {
            var t = -40 + i * 0.5f;
            var gg = i == 80 ? c : g;
            if (Stacked)
            {
                gg.Seg(new Vector3(-40, 3 + t, f - 0.01f), new Vector3(40, 3 + t, f - 0.01f));
                gg.Seg(new Vector3(t, 3 - 40, f - 0.01f), new Vector3(t, 3 + 40, f - 0.01f));
            }
            else
            {
                gg.Seg(new Vector3(-40, f + 0.01f, 22 + t), new Vector3(40, f + 0.01f, 22 + t));
                gg.Seg(new Vector3(t, f + 0.01f, 22 - 40), new Vector3(t, f + 0.01f, 22 + 40));
            }
        }
        Add(g, new Mat(0x2e3a42, Alpha: 0.55f, Transparent: true, Unlit: true));
        Add(c, new Mat(0x4c6070, Alpha: 0.55f, Transparent: true, Unlit: true));
    }

    /// <summary>The page's frame(): the camera onto the whole ship, the grid under it.</summary>
    private void FrameShip()
    {
        var any = false;
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        foreach (var o in _sdObjs)
            if (o.Pick?.Type == "hull" && o.Geo.Count > 0)
            {
                lo = Vector3.Min(lo, o.Geo.Min + o.Pos);
                hi = Vector3.Max(hi, o.Geo.Max + o.Pos);
                any = true;
            }
        if (!any)
            return;
        var c = (lo + hi) / 2;
        var r = (hi - lo).Length() / 2;
        _camTarget = c;
        _camPos = c + new Vector3(r * 1.7f, r * 0.9f, -r * 0.8f);
        _sdGridY = lo.Y - 3.4f;
        Rebuild();
    }

    // ---------- picking (three's Raycaster over the pickables)

    private readonly record struct Hit(DesignObj O, float T, Vector3 Point, Vector3 N);

    private List<Hit> Cast(Vector3 ro, Vector3 rd, IEnumerable<DesignObj>? list = null)
    {
        var hits = new List<Hit>();
        foreach (var o in list ?? _sdPickables)
        {
            var g = o.Geo;
            if (g.Count == 0)
                continue;
            var lo = g.Min + o.Pos - new Vector3(0.4f);
            var hi = g.Max + o.Pos + new Vector3(0.4f);
            if (!RayBox(ro, rd, lo, hi))
                continue;
            var p = g.P;
            if (g.Lines)
            {
                // A line within 0.35 m of the ray (three's Line threshold).
                float best = float.MaxValue, bt = 0;
                for (var i = 0; i + 5 < p.Length; i += 6)
                {
                    var a = new Vector3(p[i], p[i + 1], p[i + 2]) + o.Pos;
                    var b = new Vector3(p[i + 3], p[i + 4], p[i + 5]) + o.Pos;
                    var (dist, t) = RaySeg(ro, rd, a, b);
                    if (dist < 0.35f && t < best)
                        (best, bt) = (t, t);
                }
                if (best < float.MaxValue)
                    hits.Add(new Hit(o, bt, ro + rd * bt, Vector3.UnitY));
                continue;
            }
            float tMin = float.MaxValue;
            Vector3 n = default;
            for (var i = 0; i + 8 < p.Length; i += 9)
            {
                var a = new Vector3(p[i], p[i + 1], p[i + 2]) + o.Pos;
                var b = new Vector3(p[i + 3], p[i + 4], p[i + 5]) + o.Pos;
                var c = new Vector3(p[i + 6], p[i + 7], p[i + 8]) + o.Pos;
                if (RayTri(ro, rd, a, b, c, out var t) && t < tMin)
                {
                    tMin = t;
                    n = Vector3.Cross(b - a, c - a);
                }
            }
            if (tMin < float.MaxValue)
                hits.Add(new Hit(o, tMin, ro + rd * tMin, n.LengthSquared() > 0 ? Vector3.Normalize(n) : Vector3.UnitY));
        }
        hits.Sort((x, y) => x.T.CompareTo(y.T));
        return hits;
    }

    private static bool RayBox(Vector3 ro, Vector3 rd, Vector3 lo, Vector3 hi)
    {
        float t0 = 0, t1 = float.MaxValue;
        for (var k = 0; k < 3; k++)
        {
            float o = k == 0 ? ro.X : k == 1 ? ro.Y : ro.Z, d = k == 0 ? rd.X : k == 1 ? rd.Y : rd.Z;
            float l = k == 0 ? lo.X : k == 1 ? lo.Y : lo.Z, h = k == 0 ? hi.X : k == 1 ? hi.Y : hi.Z;
            if (MathF.Abs(d) < 1e-9f)
            {
                if (o < l || o > h)
                    return false;
                continue;
            }
            float a = (l - o) / d, b = (h - o) / d;
            if (a > b)
                (a, b) = (b, a);
            t0 = MathF.Max(t0, a);
            t1 = MathF.Min(t1, b);
            if (t0 > t1)
                return false;
        }
        return true;
    }

    private static bool RayTri(Vector3 ro, Vector3 rd, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0;
        var e1 = b - a;
        var e2 = c - a;
        var pv = Vector3.Cross(rd, e2);
        var det = Vector3.Dot(e1, pv);
        if (MathF.Abs(det) < 1e-9f)
            return false;
        var inv = 1 / det;
        var tv = ro - a;
        var u = Vector3.Dot(tv, pv) * inv;
        if (u < 0 || u > 1)
            return false;
        var q = Vector3.Cross(tv, e1);
        var v = Vector3.Dot(rd, q) * inv;
        if (v < 0 || u + v > 1)
            return false;
        t = Vector3.Dot(e2, q) * inv;
        return t > 0.1f; // (the camera's near plane)
    }

    /// <summary>The distance between a ray and a segment, and how far along the ray the closest point is.</summary>
    private static (float Dist, float T) RaySeg(Vector3 ro, Vector3 rd, Vector3 a, Vector3 b)
    {
        var u = b - a;
        var w = ro - a;
        float A = Vector3.Dot(rd, rd), Bq = Vector3.Dot(rd, u), C = Vector3.Dot(u, u), Dq = Vector3.Dot(rd, w), E = Vector3.Dot(u, w);
        var den = A * C - Bq * Bq;
        float s, t;
        if (den < 1e-9f)
        {
            s = 0;
            t = C > 0 ? E / C : 0;
        }
        else
        {
            s = (Bq * E - C * Dq) / den;
            t = (A * E - Bq * Dq) / den;
        }
        t = Math.Clamp(t, 0, 1);
        s = MathF.Max(0, Vector3.Dot(a + u * t - ro, rd) / A);
        return (Vector3.Distance(ro + rd * s, a + u * t), s);
    }

    /// <summary>
    /// Room view's cutaway (the page's applyCut): a plane from the room's floor (0%) to a little over its ceiling (100%: nothing cut),
    /// on everything drawn.
    /// </summary>
    private void ApplyCut()
    {
        ShipDesignerRenderer.Cut = null;
        if (_roomView is not { } rv || _roomCut >= 100)
            return;
        var r = ById(Arr(S, "rooms").ToList(), rv.Room);
        if (!Found(r))
            return;
        var b = Nums(Get(r, "box"));
        if (b.Length < 6)
            return;
        if (Stacked)
        {
            var top = b[4] - 0.3;
            var h = b[5] + (top - b[5]) * _roomCut / 100;
            ShipDesignerRenderer.Cut = new Vector4(0, 0, 1, (float)-h); // keeps z >= h
        }
        else
        {
            var top = b[3] + 0.3;
            var h = b[2] + (top - b[2]) * _roomCut / 100;
            ShipDesignerRenderer.Cut = new Vector4(0, -1, 0, (float)h); // keeps y <= h
        }
    }

    // ---------- passages through an arm between bodies (Karl, 2026-11-26)

    /// <summary>A centripetal Catmull-Rom curve through the points (three's CatmullRomCurve3), sampled evenly by length.</summary>
    private static List<Vector3> SplineAt(List<Vector3> p, int n)
    {
        var dense = new List<Vector3>();
        Vector3 Pt(int i) => p[Math.Clamp(i, 0, p.Count - 1)];
        for (var i = 0; i < p.Count - 1; i++)
        {
            Vector3 p0 = i == 0 ? 2 * p[0] - p[1] : Pt(i - 1), p1 = Pt(i), p2 = Pt(i + 1), p3 = i + 2 >= p.Count ? 2 * p[^1] - p[^2] : Pt(i + 2);
            float Kn(Vector3 a, Vector3 b) => MathF.Max(1e-4f, MathF.Sqrt(Vector3.Distance(a, b)));
            float t0 = 0, t1 = Kn(p0, p1), t2 = t1 + Kn(p1, p2), t3 = t2 + Kn(p2, p3);
            for (var k = 0; k < 16; k++)
            {
                var t = t1 + (t2 - t1) * k / 16f;
                var a1 = (t1 - t) / (t1 - t0) * p0 + (t - t0) / (t1 - t0) * p1;
                var a2 = (t2 - t) / (t2 - t1) * p1 + (t - t1) / (t2 - t1) * p2;
                var a3 = (t3 - t) / (t3 - t2) * p2 + (t - t2) / (t3 - t2) * p3;
                var b1 = (t2 - t) / (t2 - t0) * a1 + (t - t0) / (t2 - t0) * a2;
                var b2 = (t3 - t) / (t3 - t1) * a2 + (t - t1) / (t3 - t1) * a3;
                dense.Add((t2 - t) / (t2 - t1) * b1 + (t - t1) / (t2 - t1) * b2);
            }
        }
        dense.Add(p[^1]);
        var L = new float[dense.Count];
        for (var i = 1; i < dense.Count; i++)
            L[i] = L[i - 1] + Vector3.Distance(dense[i], dense[i - 1]);
        var o = new List<Vector3>(n + 1);
        for (int k = 0, i = 1; k <= n; k++)
        {
            var t = L[^1] * k / n;
            while (i < dense.Count - 1 && L[i] < t)
                i++;
            var den = L[i] - L[i - 1];
            o.Add(Vector3.Lerp(dense[i - 1], dense[i], den > 0 ? Math.Clamp((t - L[i - 1]) / den, 0, 1) : 0));
        }
        return o;
    }

    /// <summary>
    /// A glass tube along the arm's curved spine from a door in one room to a door in the other, a track along its floor and, for a
    /// lift, its car halfway (a walkway: a grating). Red when the arm is too thin. Pickable.
    /// </summary>
    private void DrawPassages(HashSet<int>? only = null)
    {
        foreach (var k in Arr(S, "passages"))
        {
            var pts = Nums(Get(k, "pts"));
            var ups = Nums(Get(k, "ups"));
            int ka = I(k, "a"), kb = I(k, "b"), id = I(k, "id");
            if (pts.Length < 6 || (only != null && !only.Contains(ka) && !only.Contains(kb)))
                continue;
            var P = new List<Vector3>();
            var U = new List<Vector3>();
            for (var i = 0; i + 2 < pts.Length; i += 3)
            {
                P.Add(new((float)pts[i], (float)pts[i + 1], (float)pts[i + 2]));
                U.Add(i + 2 < ups.Length ? Vector3.Normalize(new((float)ups[i], (float)ups[i + 1], (float)ups[i + 2])) : Vector3.UnitY);
            }
            var picked = _selKind == "passage" && _selId == id;
            var ok = B(k, "ok", true);
            var rad = (float)Math.Max(0.35, Math.Min(0.75, D(k, "clear", 1.2) / 2));
            var N = Math.Max(24, P.Count * 8);
            var C = SplineAt(P, N);
            var pick = new DesignPick("passage", id);
            Vector3 Tan(int i) => Vector3.Normalize(C[Math.Min(N, i + 1)] - C[Math.Max(0, i - 1)]);
            Vector3 Up(int i)
            {
                var f = Math.Min(U.Count - 1.001f, (float)i / N * (U.Count - 1));
                return Vector3.Normalize(Vector3.Lerp(U[(int)MathF.Floor(f)], U[(int)MathF.Ceiling(f)], f - MathF.Floor(f)));
            }
            // Each sample's frame: side and up square to the tangent.
            var fr = new (Vector3 S, Vector3 U)[N + 1];
            for (var i = 0; i <= N; i++)
            {
                var sd = Vector3.Normalize(Vector3.Cross(Up(i), Tan(i)));
                fr[i] = (sd, Vector3.Normalize(Vector3.Cross(Tan(i), sd)));
            }
            const int Seg = 14;
            var tube = new GB();
            for (var i = 0; i < N; i++)
                for (var j = 0; j < Seg; j++)
                {
                    float a0 = MathF.Tau * j / Seg, a1 = MathF.Tau * (j + 1) / Seg;
                    Vector3 R(int s, float a) => C[s] + rad * (MathF.Cos(a) * fr[s].S + MathF.Sin(a) * fr[s].U);
                    tube.Quad(R(i, a0), R(i, a1), R(i + 1, a1), R(i + 1, a0));
                }
            Add(tube, new Mat(ok ? 0x9fd3ffu : 0xff6a50u, Alpha: only != null ? 0.18f : 0.32f, Transparent: true, DepthWrite: false, Emissive: picked ? 0x1d4f86u : 0, Order: 5), pick, true);
            var steel = new Mat(ok ? 0xd8b04au : 0xff5a44u, Metal: 0.5f, Emissive: picked ? 0x1d4f86u : 0, Order: 5);
            // Its track: two rails on the tube's floor.
            var rails = new GB();
            foreach (var sgn in new[] { -1f, 1f })
                for (var i = 0; i < N; i++)
                {
                    var a = C[i] - fr[i].U * rad * 0.82f + fr[i].S * sgn * rad * 0.42f;
                    var b = C[i + 1] - fr[i + 1].U * rad * 0.82f + fr[i + 1].S * sgn * rad * 0.42f;
                    for (var j = 0; j < 6; j++)
                    {
                        float a0 = MathF.Tau * j / 6, a1 = MathF.Tau * (j + 1) / 6;
                        Vector3 Rr(Vector3 c, int s, float an) => c + 0.035f * (MathF.Cos(an) * fr[s].S + MathF.Sin(an) * fr[s].U);
                        rails.Quad(Rr(a, i, a0), Rr(a, i, a1), Rr(b, i + 1, a1), Rr(b, i + 1, a0));
                    }
                }
            Add(rails, steel, pick, true);
            Matrix4x4 Basis(Vector3 sd, Vector3 up, Vector3 fw, Vector3 at) => new(sd.X, sd.Y, sd.Z, 0, up.X, up.Y, up.Z, 0, fw.X, fw.Y, fw.Z, 0, at.X, at.Y, at.Z, 1);
            if (Str(k, "kind") == "Lift")
            {
                var m = N / 2;
                var ch = MathF.Min(1.9f, 2 * rad * 0.85f);
                var car = new GB { M = Basis(fr[m].S, fr[m].U, Tan(m), C[m] + fr[m].U * (-rad * 0.82f + ch / 2 + 0.05f)) };
                car.Box(rad * 1.2f, ch, MathF.Min(1.4f, rad * 1.8f), 0, 0, 0);
                Add(car, new Mat(0x9aa3ad, Metal: 0.6f, Emissive: picked ? 0x1d4f86u : 0, Order: 5), pick, true);
            }
            else
            {
                var len = 0f;
                for (var i = 0; i < N; i++)
                    len += Vector3.Distance(C[i], C[i + 1]);
                var planks = new GB();
                for (var i = 0; i < N; i += 2)
                {
                    planks.M = Basis(fr[i].S, fr[i].U, Tan(i), (C[i] + C[i + 1]) / 2 - fr[i].U * rad * 0.8f);
                    planks.Box(rad * 1.1f, 0.03f, len / N * 1.6f, 0, 0, 0);
                }
                Add(planks, steel);
            }
            // A door frame at each end, in the room's wall, facing along the tube.
            var frames = new GB();
            foreach (var (at, tg) in new[] { (P[0], Tan(0)), (P[^1], Tan(N)) })
            {
                var up = Stacked ? new Vector3(0, 0, -1) : Vector3.UnitY;
                var flat = tg - up * Vector3.Dot(tg, up);
                if (flat.LengthSquared() < 1e-6f)
                    continue;
                flat = Vector3.Normalize(flat);
                frames.M = Basis(Vector3.Normalize(Vector3.Cross(up, flat)), up, flat, at);
                frames.Box(0.1f, 2.1f, 0.2f, -0.5f, 0, 0);
                frames.Box(0.1f, 2.1f, 0.2f, 0.5f, 0, 0);
                frames.Box(1.1f, 0.1f, 0.2f, 0, 1.05f, 0);
            }
            Add(frames, steel);
        }
    }
}
