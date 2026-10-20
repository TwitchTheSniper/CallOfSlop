using System.Numerics;
using System.Text;
using G = Brutal.GltfApi;

namespace CallOfSlop;

/// <summary>A model file the game can't use, with a message that says what's wrong and where.</summary>
public sealed class ModelException(string message) : Exception(message);

/// <summary>
/// Loads glTF 2.0 models (.gltf with embedded or external buffers, or binary .glb) into a <see cref="ModelAsset"/>.
/// Brutal.Gltf parses the JSON into its typed schema; this class resolves buffers, reads accessors by semantic, walks
/// the node tree and turns materials into vertex colours (base colour factor) and imported texture layers (base colour
/// texture). Supported: triangle primitives with POSITION (required), NORMAL, TEXCOORD_0 and COLOR_0, 8/16/32-bit
/// indices, interleaved buffer views, node TRS or matrix transforms. Skins, animations, morph targets, cameras, lights
/// and other texture slots are ignored with a warning; compressed extensions are refused. glTF is Y-up, metres and
/// counter-clockwise like the game, so nothing is converted.
/// </summary>
public static class GltfModelLoader
{
    private const uint GlbMagic = 0x46546C67, ChunkJson = 0x4E4F534A, ChunkBin = 0x004E4942;
    private const int MaxVertices = 1 << 21;

    private static readonly string[] UnsupportedRequired =
        ["KHR_draco_mesh_compression", "EXT_meshopt_compression", "KHR_mesh_quantization", "KHR_texture_basisu", "EXT_texture_webp"];

    /// <summary>Loads a model file. <paramref name="name"/> is how errors refer to it (defaults to the path).</summary>
    public static ModelAsset Load(string path, string? name = null)
    {
        name ??= path;
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            throw new ModelException($"{name}: can't read the file ({ex.Message})");
        }
        return Load(data, name, Path.GetDirectoryName(Path.GetFullPath(path)));
    }

    /// <summary>Loads a model from memory. External buffers and images are looked up in <paramref name="folder"/> (null: not allowed).</summary>
    public static ModelAsset Load(byte[] data, string name, string? folder)
    {
        try
        {
            return new Reader(data, name, folder).Read();
        }
        catch (ModelException)
        {
            throw;
        }
        catch (Exception ex) // anything unexpected from the parser still gets the file name and a plain message
        {
            throw new ModelException($"{name}: {ex.Message}");
        }
    }

    private sealed class Reader(byte[] data, string name, string? folder)
    {
        private G.Gltf _g = null!;
        private byte[]? _glbBin;
        private byte[]?[] _buffers = [];
        private readonly List<string> _warnings = new();
        private readonly Dictionary<int, int> _imageLayers = new();
        private ModelMaterial[] _materials = [];

        private ModelException Error(string what) => new($"{name}: {what}");

        public ModelAsset Read()
        {
            ParseContainer();
            if (_g.Asset?.Version is not { } version || !version.StartsWith("2.", StringComparison.Ordinal))
                throw Error($"glTF version \"{_g.Asset?.Version}\" isn't supported (only 2.x)");
            foreach (var ext in _g.ExtensionsRequired ?? [])
                if (UnsupportedRequired.Contains(ext))
                    throw Error($"needs the {ext} extension, which the game can't read (export without compression)");
            _buffers = new byte[]?[(_g.Buffers ?? []).Length];
            var nodes = _g.Nodes ?? [];
            if (nodes.Length == 0)
                throw Error("has no nodes, so nothing would be drawn");
            if ((_g.Skins ?? []).Length > 0)
                _warnings.Add("skins ignored (models are static)");
            if ((_g.Animations ?? []).Length > 0)
                _warnings.Add("animations ignored");
            _materials = ReadMaterials();

            // Walk the tree from the scene's roots: parents before children, every node at most once.
            var roots = SceneRoots(nodes);
            var order = new List<(int Gltf, int Parent)>();
            var seen = new bool[nodes.Length];
            var stack = new Stack<(int Node, int Parent, int Depth)>();
            for (var i = roots.Length - 1; i >= 0; i--)
                stack.Push((roots[i], -1, 0));
            while (stack.Count > 0)
            {
                var (n, parent, depth) = stack.Pop();
                if ((uint)n >= (uint)nodes.Length)
                    throw Error($"node index {n} is out of range (the file has {nodes.Length} nodes)");
                if (seen[n])
                    throw Error($"node {Describe(n)} appears twice in the hierarchy (glTF nodes must form a tree)");
                if (depth > 64)
                    throw Error("the node hierarchy is more than 64 levels deep");
                seen[n] = true;
                var me = order.Count;
                order.Add((n, parent));
                var children = nodes[n].Children ?? [];
                for (var c = children.Length - 1; c >= 0; c--)
                    stack.Push((children[c], me, depth + 1));
            }

            var meshIndex = new Dictionary<int, int>();
            var meshes = new List<MeshAsset>();
            var meshNames = new List<string>();
            var outNodes = new ModelNode[order.Count];
            var parts = new List<ModelPart>();
            var bounds = MeshBounds.Empty;
            for (var i = 0; i < order.Count; i++)
            {
                var (gi, parent) = order[i];
                var node = nodes[gi];
                var local = LocalMatrix(node, out var t, out var r, out var s);
                var world = parent >= 0 ? local * outNodes[parent].World : local;
                var mesh = -1;
                if (node.Mesh is { } m)
                {
                    if (!meshIndex.TryGetValue(m, out mesh))
                    {
                        var asset = ReadMesh(m);
                        mesh = asset == null ? -1 : meshes.Count;
                        if (asset != null)
                        {
                            meshes.Add(asset);
                            meshNames.Add(MeshName(m));
                        }
                        meshIndex[m] = mesh;
                    }
                }
                if (node.Skin != null && mesh >= 0)
                    _warnings.Add($"node {Describe(gi)}: skinned mesh drawn in its bind pose");
                outNodes[i] = new ModelNode(string.IsNullOrEmpty(node.Name) ? $"node{gi}" : node.Name, parent, mesh, t, r, s, local, world);
                if (mesh >= 0)
                {
                    parts.Add(new ModelPart(i, mesh, world));
                    bounds = bounds.Union(meshes[mesh].Bounds.Transform(world));
                }
            }
            if (parts.Count == 0)
                throw Error("no node has a drawable triangle mesh");
            return new ModelAsset
            {
                File = name, Meshes = meshes.ToArray(), MeshNames = meshNames.ToArray(), Materials = _materials, Nodes = outNodes,
                Parts = parts.ToArray(), Bounds = bounds, Warnings = _warnings.Distinct().ToArray(), Textures = _imageLayers.Count(kv => kv.Value >= 0),
            };
        }

        private string Describe(int node) => _g.Nodes is { } n && (uint)node < (uint)n.Length && !string.IsNullOrEmpty(n[node].Name) ? $"\"{n[node].Name}\"" : $"#{node}";

        private string MeshName(int m) => _g.Meshes![m].Name is { Length: > 0 } n ? n : $"mesh{m}";

        // ---- Container: .gltf JSON or .glb ------------------------------------------------------------------------

        private void ParseContainer()
        {
            string json;
            if (data.Length >= 4 && BitConverter.ToUInt32(data, 0) == GlbMagic)
            {
                if (data.Length < 20)
                    throw Error("the .glb file is truncated (no header)");
                var version = BitConverter.ToUInt32(data, 4);
                var length = BitConverter.ToUInt32(data, 8);
                if (version != 2)
                    throw Error($"the .glb container is version {version} (only 2 is supported)");
                if (length != data.Length)
                    throw Error($"the .glb header says {length} bytes but the file has {data.Length} (truncated or padded file)");
                json = "";
                var pos = 12;
                var first = true;
                while (pos + 8 <= data.Length)
                {
                    var len = BitConverter.ToUInt32(data, pos);
                    var type = BitConverter.ToUInt32(data, pos + 4);
                    if (len > data.Length - pos - 8)
                        throw Error($"a .glb chunk at byte {pos} runs past the end of the file");
                    if (first && type != ChunkJson)
                        throw Error("the first .glb chunk must be JSON");
                    if (type == ChunkJson)
                        json = Encoding.UTF8.GetString(data, pos + 8, (int)len);
                    else if (type == ChunkBin && _glbBin == null)
                        _glbBin = data.AsSpan(pos + 8, (int)len).ToArray();
                    first = false;
                    pos += 8 + (int)((len + 3) & ~3u);
                }
                if (json.Length == 0)
                    throw Error("the .glb file has no JSON chunk");
            }
            else
            {
                json = Encoding.UTF8.GetString(data).TrimStart('\uFEFF');
                if (!json.TrimStart().StartsWith('{'))
                    throw Error("is neither glTF JSON nor a binary .glb file");
            }
            try
            {
                _g = G.GltfUtility.DeserializeModelJson(json);
            }
            catch (Exception ex)
            {
                throw Error($"not valid glTF JSON ({ex.Message})");
            }
        }

        private byte[] Buffer(int index)
        {
            var list = _g.Buffers ?? [];
            if ((uint)index >= (uint)list.Length)
                throw Error($"buffer {index} is out of range (the file has {list.Length} buffers)");
            if (_buffers[index] is { } cached)
                return cached;
            var b = list[index];
            byte[] bytes;
            if (string.IsNullOrEmpty(b.Uri))
                bytes = index == 0 && _glbBin != null ? _glbBin : throw Error($"buffer {index} has no uri and there is no .glb binary chunk");
            else if (b.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                bytes = DataUri(b.Uri, $"buffer {index}");
            else
                bytes = External(b.Uri, $"buffer {index}");
            if (bytes.Length < b.ByteLength)
                throw Error($"buffer {index} is {bytes.Length} bytes but should be {b.ByteLength}");
            _buffers[index] = bytes;
            return bytes;
        }

        private byte[] DataUri(string uri, string what)
        {
            var comma = uri.IndexOf(',');
            if (comma < 0 || !uri.AsSpan(0, comma).EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                throw Error($"{what}: only base64 data: URIs are supported");
            try
            {
                return Convert.FromBase64String(uri[(comma + 1)..]);
            }
            catch (FormatException)
            {
                throw Error($"{what}: the base64 data is corrupt");
            }
        }

        private byte[] External(string uri, string what)
        {
            if (folder == null)
                throw Error($"{what} refers to the file \"{uri}\", but this model has no folder to look in");
            var rel = Uri.UnescapeDataString(uri).Replace('\\', '/');
            var path = Path.GetFullPath(Path.Combine(folder, rel));
            if (!File.Exists(path))
                throw Error($"{what} refers to \"{uri}\", which isn't next to the model");
            return File.ReadAllBytes(path);
        }

        private ReadOnlySpan<byte> View(int index, out int stride)
        {
            var views = _g.BufferViews ?? [];
            if ((uint)index >= (uint)views.Length)
                throw Error($"buffer view {index} is out of range (the file has {views.Length})");
            var v = views[index];
            var buf = Buffer(v.Buffer);
            if (v.ByteOffset < 0 || v.ByteLength < 0 || (long)v.ByteOffset + v.ByteLength > buf.Length)
                throw Error($"buffer view {index} (bytes {v.ByteOffset}-{(long)v.ByteOffset + v.ByteLength}) runs past the end of buffer {v.Buffer} ({buf.Length} bytes)");
            stride = v.ByteStride ?? 0;
            return buf.AsSpan(v.ByteOffset, v.ByteLength);
        }

        // ---- Accessors ----------------------------------------------------------------------------------------------

        private static int Components(G.Accessor.TypeEnum t) => t switch
        {
            G.Accessor.TypeEnum.Scalar => 1, G.Accessor.TypeEnum.Vec2 => 2, G.Accessor.TypeEnum.Vec3 => 3, G.Accessor.TypeEnum.Vec4 => 4, _ => 0,
        };

        private static int ComponentSize(G.Accessor.ComponentTypeEnum c) => c switch
        {
            G.Accessor.ComponentTypeEnum.Byte or G.Accessor.ComponentTypeEnum.UnsignedByte => 1,
            G.Accessor.ComponentTypeEnum.Short or G.Accessor.ComponentTypeEnum.UnsignedShort => 2,
            _ => 4,
        };

        private G.Accessor Accessor(int index, string what, out ReadOnlySpan<byte> bytes, out int stride, out int elementSize)
        {
            var list = _g.Accessors ?? [];
            if ((uint)index >= (uint)list.Length)
                throw Error($"{what}: accessor {index} is out of range (the file has {list.Length} accessors)");
            var a = list[index];
            if (a.Sparse != null)
                throw Error($"{what}: sparse accessors aren't supported");
            var comps = Components(a.Type);
            if (comps == 0)
                throw Error($"{what}: accessor {index} is a {a.Type}, expected a scalar or vector");
            elementSize = comps * ComponentSize(a.ComponentType);
            if (a.Count < 0 || a.Count > MaxVertices * 3)
                throw Error($"{what}: accessor {index} has {a.Count} elements (too many)");
            if (a.BufferView is not { } view)
            {
                bytes = default; // no buffer view: all zeros (allowed by the spec)
                stride = 0;
                return a;
            }
            var span = View(view, out stride);
            if (stride == 0)
                stride = elementSize;
            var need = a.Count == 0 ? 0 : (long)a.ByteOffset + (long)(a.Count - 1) * stride + elementSize;
            if (a.ByteOffset < 0 || need > span.Length)
                throw Error($"{what}: accessor {index} needs {need} bytes but its buffer view has {span.Length}");
            bytes = span[a.ByteOffset..];
            return a;
        }

        /// <summary>Reads a float vector attribute (float or normalised integer components) as count x <paramref name="want"/> floats.</summary>
        private float[] Floats(int index, string what, int[] allowedComponents, int want, out int count)
        {
            var a = Accessor(index, what, out var bytes, out var stride, out _);
            var comps = Components(a.Type);
            if (!allowedComponents.Contains(comps))
                throw Error($"{what}: accessor {index} is a {a.Type}, expected {string.Join(" or ", allowedComponents.Select(c => c == 1 ? "SCALAR" : $"VEC{c}"))}");
            if (a.ComponentType != G.Accessor.ComponentTypeEnum.Float && !a.Normalized)
                throw Error($"{what}: accessor {index} uses {a.ComponentType} without \"normalized\" (needs floats or normalised integers)");
            count = a.Count;
            var result = new float[count * want];
            if (bytes.IsEmpty)
                return result;
            var cs = ComponentSize(a.ComponentType);
            for (var i = 0; i < count; i++)
            {
                var e = bytes.Slice(i * stride);
                for (var c = 0; c < Math.Min(comps, want); c++)
                {
                    var b = e.Slice(c * cs);
                    float v = a.ComponentType switch
                    {
                        G.Accessor.ComponentTypeEnum.Float => BitConverter.ToSingle(b),
                        G.Accessor.ComponentTypeEnum.UnsignedByte => b[0] / 255f,
                        G.Accessor.ComponentTypeEnum.UnsignedShort => BitConverter.ToUInt16(b) / 65535f,
                        G.Accessor.ComponentTypeEnum.Byte => MathF.Max((sbyte)b[0] / 127f, -1f),
                        G.Accessor.ComponentTypeEnum.Short => MathF.Max(BitConverter.ToInt16(b) / 32767f, -1f),
                        _ => throw Error($"{what}: accessor {index} has unsupported component type {a.ComponentType}"),
                    };
                    if (!float.IsFinite(v))
                        throw Error($"{what}: accessor {index} contains NaN or infinity");
                    result[i * want + c] = v;
                }
                for (var c = comps; c < want; c++)
                    result[i * want + c] = 1f; // e.g. RGB colours get alpha 1
            }
            return result;
        }

        private uint[] Indices(int index, string what, int vertexCount)
        {
            var a = Accessor(index, what, out var bytes, out var stride, out _);
            if (a.Type != G.Accessor.TypeEnum.Scalar)
                throw Error($"{what}: index accessor {index} is a {a.Type}, expected SCALAR");
            var result = new uint[a.Count];
            for (var i = 0; i < a.Count; i++)
            {
                if (bytes.IsEmpty)
                    break;
                var e = bytes.Slice(i * stride);
                result[i] = a.ComponentType switch
                {
                    G.Accessor.ComponentTypeEnum.UnsignedByte => e[0],
                    G.Accessor.ComponentTypeEnum.UnsignedShort => BitConverter.ToUInt16(e),
                    G.Accessor.ComponentTypeEnum.UnsignedInt => BitConverter.ToUInt32(e),
                    _ => throw Error($"{what}: index accessor {index} uses {a.ComponentType} (indices are unsigned 8, 16 or 32-bit)"),
                };
                if (result[i] >= vertexCount)
                    throw Error($"{what}: index {result[i]} (at {i}) is past the last vertex ({vertexCount - 1})");
            }
            return result;
        }

        // ---- Materials and textures --------------------------------------------------------------------------------

        private static float ToSrgb(float linear) =>
            linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(Math.Clamp(linear, 0f, 1f), 1f / 2.4f) - 0.055f;

        private ModelMaterial[] ReadMaterials()
        {
            var list = _g.Materials ?? [];
            var result = new ModelMaterial[list.Length];
            for (var i = 0; i < list.Length; i++)
            {
                var m = list[i];
                var nm = string.IsNullOrEmpty(m.Name) ? $"material{i}" : m.Name;
                var pbr = m.PbrMetallicRoughness;
                var f = pbr != null ? pbr.BaseColorFactor : new Brutal.Numerics.float4(1, 1, 1, 1);
                var color = new Vector4(ToSrgb(f.X), ToSrgb(f.Y), ToSrgb(f.Z), f.W);
                var layer = -1;
                if (pbr?.BaseColorTexture is { } ti)
                {
                    if (ti.TexCoord != 0)
                        _warnings.Add($"material \"{nm}\": texture uses TEXCOORD_{ti.TexCoord}, read as TEXCOORD_0");
                    layer = TextureLayer(ti.Index, nm);
                }
                if (m.NormalTexture != null || m.OcclusionTexture != null || m.EmissiveTexture != null || pbr?.MetallicRoughnessTexture != null)
                    _warnings.Add($"material \"{nm}\": only the base colour is used (normal, occlusion, emissive and metal/roughness maps ignored)");
                if (m.AlphaMode != G.Material.AlphaModeEnum.Opaque)
                    _warnings.Add($"material \"{nm}\": alpha mode {m.AlphaMode} drawn opaque");
                result[i] = new ModelMaterial(nm, color, layer, m.DoubleSided);
            }
            return result;
        }

        private int TextureLayer(int texture, string material)
        {
            var textures = _g.Textures ?? [];
            if ((uint)texture >= (uint)textures.Length)
                throw Error($"material \"{material}\": texture {texture} is out of range (the file has {textures.Length})");
            if (textures[texture].Source is not { } image)
            {
                _warnings.Add($"texture {texture} has no image (needs an extension?), colour factor used");
                return -1;
            }
            if (_imageLayers.TryGetValue(image, out var cached))
                return cached;
            var images = _g.Images ?? [];
            if ((uint)image >= (uint)images.Length)
                throw Error($"texture {texture}: image {image} is out of range (the file has {images.Length})");
            var img = images[image];
            var what = $"image {image}{(string.IsNullOrEmpty(img.Name) ? "" : $" \"{img.Name}\"")}";
            int layer;
            try
            {
                byte[] bytes;
                if (img.BufferView is { } bv)
                    bytes = View(bv, out _).ToArray();
                else if (img.Uri?.StartsWith("data:", StringComparison.OrdinalIgnoreCase) == true)
                    bytes = DataUri(img.Uri, what);
                else if (!string.IsNullOrEmpty(img.Uri))
                    bytes = External(img.Uri, what);
                else
                    throw Error($"{what} has neither a buffer view nor a uri");
                var (w, h, rgba) = MeshTextures.Decode(bytes, img.MimeType ?? "");
                layer = MeshTextures.Add($"{name}#{image}", w, h, rgba);
                if (layer < 0)
                    _warnings.Add($"{what}: too many imported textures ({MeshTextures.MaxLayers}), colour factor used");
            }
            catch (InvalidDataException ex)
            {
                _warnings.Add($"{what} can't be decoded ({ex.Message}), colour factor used");
                layer = -1;
            }
            _imageLayers[image] = layer;
            return layer;
        }

        // ---- Meshes -------------------------------------------------------------------------------------------------

        private MeshAsset? ReadMesh(int m)
        {
            var list = _g.Meshes ?? [];
            if ((uint)m >= (uint)list.Length)
                throw Error($"mesh index {m} is out of range (the file has {list.Length} meshes)");
            var mesh = list[m];
            var meshName = MeshName(m);
            var parts = new List<MeshData>();
            var doubleSided = false;
            var prims = mesh.Primitives ?? [];
            for (var p = 0; p < prims.Length; p++)
            {
                var prim = prims[p];
                var what = $"mesh \"{meshName}\" primitive {p}";
                if (prim.Mode != G.MeshPrimitive.ModeEnum.Triangles)
                {
                    _warnings.Add($"{what}: mode {prim.Mode} skipped (only triangle lists are drawn)");
                    continue;
                }
                if (prim.Targets is { Length: > 0 })
                    _warnings.Add($"{what}: morph targets ignored");
                var attrs = prim.Attributes ?? new Dictionary<string, int>();
                if (!attrs.TryGetValue("POSITION", out var posAcc))
                    throw Error($"{what} has no POSITION attribute");
                var pos = Floats(posAcc, $"{what} POSITION", [3], 3, out var count);
                if (count > MaxVertices)
                    throw Error($"{what} has {count} vertices (the limit is {MaxVertices})");
                float[]? nrm = null, uv = null, col = null;
                if (attrs.TryGetValue("NORMAL", out var nAcc))
                    nrm = Floats(nAcc, $"{what} NORMAL", [3], 3, out var nc) is var n && nc == count ? n : throw Error($"{what}: NORMAL has {nc} entries, POSITION {count}");
                if (attrs.TryGetValue("TEXCOORD_0", out var tAcc))
                    uv = Floats(tAcc, $"{what} TEXCOORD_0", [2], 2, out var tc) is var t && tc == count ? t : throw Error($"{what}: TEXCOORD_0 has {tc} entries, POSITION {count}");
                if (attrs.TryGetValue("COLOR_0", out var cAcc))
                    col = Floats(cAcc, $"{what} COLOR_0", [3, 4], 4, out var cc) is var c && cc == count ? c : throw Error($"{what}: COLOR_0 has {cc} entries, POSITION {count}");
                var idx = prim.Indices is { } ia ? Indices(ia, $"{what} indices", count) : Enumerable.Range(0, count).Select(i => (uint)i).ToArray();
                if (idx.Length % 3 != 0)
                    throw Error($"{what}: {idx.Length} indices is not a whole number of triangles");
                ModelMaterial mat;
                if (prim.Material is { } mi)
                    mat = (uint)mi < (uint)_materials.Length ? _materials[mi] : throw Error($"{what}: material {mi} is out of range (the file has {_materials.Length})");
                else
                    mat = new ModelMaterial("default", Vector4.One, -1, false);
                doubleSided |= mat.DoubleSided;
                var layer = mat.TextureLayer >= 0 ? MeshVertex.ImportedLayerBase + mat.TextureLayer : MeshVertex.NoLayer;

                var verts = new MeshVertex[count];
                for (var i = 0; i < count; i++)
                {
                    var c = mat.BaseColor;
                    if (col != null)
                        c *= new Vector4(ToSrgb(col[i * 4]), ToSrgb(col[i * 4 + 1]), ToSrgb(col[i * 4 + 2]), col[i * 4 + 3]);
                    var n = nrm != null ? new Vector3(nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2]) : Vector3.Zero;
                    verts[i] = new MeshVertex(new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]),
                        n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY,
                        uv != null ? new Vector2(uv[i * 2], uv[i * 2 + 1]) : Vector2.Zero, MeshVertex.Pack(c), layer);
                }
                var data = new MeshData(verts, idx);
                parts.Add(nrm == null ? FlatNormals(data) : data);
            }
            if (parts.Count == 0 || parts.All(x => x.Indices.Length == 0))
            {
                _warnings.Add($"mesh \"{meshName}\" has no triangles, skipped");
                return null;
            }
            var lod0 = MeshOpt.Optimize(MeshData.Merge(parts));
            var lods = new List<MeshData> { lod0 };
            if (lod0.TriangleCount >= 64)
                foreach (var (ratio, error) in new[] { (0.5f, 0.01f), (0.2f, 0.03f) })
                    if (MeshOpt.Simplify(lod0, ratio, error) is { } simpler && simpler.Length < lods[^1].Indices.Length * 0.85f)
                        lods.Add(new MeshData(lod0.Vertices, simpler));
            return new MeshAsset($"{name}#{meshName}", lods.ToArray(), doubleSided, lods.Count > 1 ? "meshoptimizer" : "none");
        }

        /// <summary>glTF's rule for meshes without normals: flat shading (every triangle gets its own three vertices).</summary>
        private static MeshData FlatNormals(MeshData m)
        {
            var v = new MeshVertex[m.Indices.Length];
            var idx = new uint[m.Indices.Length];
            for (var i = 0; i + 2 < m.Indices.Length; i += 3)
            {
                var a = m.Vertices[m.Indices[i]];
                var b = m.Vertices[m.Indices[i + 1]];
                var c = m.Vertices[m.Indices[i + 2]];
                var n = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
                n = n.LengthSquared() > 1e-20f ? Vector3.Normalize(n) : Vector3.UnitY;
                a.Normal = b.Normal = c.Normal = n;
                (v[i], v[i + 1], v[i + 2]) = (a, b, c);
                (idx[i], idx[i + 1], idx[i + 2]) = ((uint)i, (uint)i + 1, (uint)i + 2);
            }
            return new MeshData(v, idx);
        }

        // ---- Nodes --------------------------------------------------------------------------------------------------

        private int[] SceneRoots(G.Node[] nodes)
        {
            var scenes = _g.Scenes ?? [];
            if (scenes.Length > 0)
            {
                var s = _g.Scene ?? 0;
                if ((uint)s >= (uint)scenes.Length)
                    throw Error($"default scene {s} is out of range (the file has {scenes.Length} scenes)");
                if (scenes.Length > 1)
                    _warnings.Add($"only scene {s} of {scenes.Length} is loaded");
                return scenes[s].Nodes ?? [];
            }
            // No scenes: every node that isn't somebody's child is a root.
            var isChild = new bool[nodes.Length];
            foreach (var n in nodes)
                foreach (var c in n.Children ?? [])
                    if ((uint)c < (uint)nodes.Length)
                        isChild[c] = true;
            return Enumerable.Range(0, nodes.Length).Where(i => !isChild[i]).ToArray();
        }

        private Matrix4x4 LocalMatrix(G.Node node, out Vector3 t, out Quaternion r, out Vector3 s)
        {
            var mm = node.Matrix.AsReadOnlySpan();
            var identity = true;
            for (var i = 0; i < 16; i++)
                identity &= mm[i] == (i % 5 == 0 ? 1f : 0f);
            if (!identity)
            {
                // glTF stores column-major for column vectors; that's exactly System.Numerics' row-vector layout.
                var m = new Matrix4x4(mm[0], mm[1], mm[2], mm[3], mm[4], mm[5], mm[6], mm[7], mm[8], mm[9], mm[10], mm[11], mm[12], mm[13], mm[14], mm[15]);
                for (var i = 0; i < 16; i++)
                    if (!float.IsFinite(mm[i]))
                        throw Error($"node \"{node.Name}\": matrix contains NaN or infinity");
                if (!Matrix4x4.Decompose(m, out s, out r, out t))
                {
                    _warnings.Add($"node \"{node.Name}\": matrix has shear, TRS in the node tree is approximate");
                    (s, r, t) = (Vector3.One, Quaternion.Identity, m.Translation);
                }
                return m;
            }
            t = new Vector3(node.Translation.X, node.Translation.Y, node.Translation.Z);
            r = new Quaternion(node.Rotation.X, node.Rotation.Y, node.Rotation.Z, node.Rotation.W);
            s = new Vector3(node.Scale.X, node.Scale.Y, node.Scale.Z);
            if (!float.IsFinite(t.X + t.Y + t.Z + r.X + r.Y + r.Z + r.W + s.X + s.Y + s.Z))
                throw Error($"node \"{node.Name}\": translation, rotation or scale contains NaN or infinity");
            r = r.LengthSquared() > 1e-12f ? Quaternion.Normalize(r) : Quaternion.Identity;
            return Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);
        }
    }
}
