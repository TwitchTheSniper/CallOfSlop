namespace CallOfSlop;

/// <summary>
/// Pack-local assets (see Maps/README.md "Where assets go"). A map in a pack folder looks for the props, models and sky
/// images it names in its pack first and in the global folders second:
/// <list type="bullet">
/// <item>props: Maps/&lt;Pack&gt;/Props, then Props (<see cref="PropScope"/>);</item>
/// <item>models: Maps/&lt;Pack&gt;/&lt;path&gt;, then &lt;path&gt; from the game folder, so "Models/crate.glb" finds Maps/&lt;Pack&gt;/Models/crate.glb before Models/crate.glb;</item>
/// <item>sky images: next to the map file, then the pack folder, then the Maps folder (<see cref="SkyImages.Resolve"/>).</item>
/// </list>
/// A "global:" prefix skips the pack. Pack-local props and models a map uses are mixed into its fingerprint
/// (<see cref="PackAssetHash"/>), so a player with different pack files gets the usual map mismatch message; global
/// assets and sky images are not, so every map that uses none keeps the fingerprint it always had.
/// </summary>
public sealed partial class Map
{
    /// <summary>
    /// The pack folder (absolute) this map's assets are looked up in first, or null for a map outside any pack (or built
    /// in code). Set from the map file's location when it loads, before any op runs.
    /// </summary>
    public string? PackDir;

    /// <summary>The props this map can name (its pack's own props, then the global ones).</summary>
    public PropScope PropScope => Props.ForPack(PackDir);

    /// <summary>Pack-local models used by Model ops: path in the pack (lower case) to the model's content hash.</summary>
    private readonly SortedDictionary<string, uint> _packModels = new(StringComparer.Ordinal);

    /// <summary>The pack-local models this map uses (their paths relative to the pack folder).</summary>
    public IReadOnlyCollection<string> PackModels => _packModels.Keys;

    /// <summary>
    /// The pack folder for a map file at <paramref name="source"/> (its path relative to the Maps folder): the first
    /// folder in the path, or null for a file directly in Maps.
    /// </summary>
    public static string? PackDirFor(string source, string? mapsFolder = null)
    {
        var norm = source.Replace('\\', '/');
        var slash = norm.IndexOf('/');
        if (slash <= 0 || Path.IsPathRooted(source))
            return null;
        return Path.Combine(mapsFolder ?? Maps.Folder, norm[..slash]);
    }

    /// <summary>
    /// A fingerprint of the pack-local props and models the map uses (0 when it uses none): each one's key and file
    /// fingerprint, in name order. Mixed into <see cref="ContentHash"/> only when non-zero. The pack folder's name doesn't
    /// count (a map sees one pack only), so a renamed copy of a pack still matches.
    /// </summary>
    public uint PackAssetHash
    {
        get
        {
            var props = new SortedDictionary<string, uint>(StringComparer.Ordinal);
            foreach (var p in PropInstances)
                if (p.Def.Pack != null)
                    props[p.Def.Name.ToLowerInvariant()] = p.Def.FileHash;
            if (props.Count == 0 && _packModels.Count == 0)
                return 0;
            var h = 2166136261u;
            void Mix(uint v)
            {
                for (var i = 0; i < 4; i++)
                {
                    h ^= (v >> (i * 8)) & 0xFF;
                    h *= 16777619u;
                }
            }
            void MixText(string s)
            {
                foreach (var ch in s)
                    Mix(ch);
                Mix(0);
            }
            foreach (var (k, v) in props)
            {
                MixText("prop:" + k);
                Mix(v);
            }
            foreach (var (k, v) in _packModels)
            {
                MixText("model:" + k);
                Mix(v);
            }
            return h == 0 ? 1 : h;
        }
    }

    /// <summary>
    /// Where a Model op's file is: in the pack folder if it's there (pack-local), else from the game folder. "global:"
    /// skips the pack. Throws with the models that exist when it's in neither.
    /// </summary>
    private (string Path, string Display, string? PackRel) ResolveModelFile(string file)
    {
        if (string.IsNullOrWhiteSpace(file))
            throw new InvalidOperationException("no model file given");
        var forceGlobal = file.StartsWith(PropScope.GlobalPrefix, StringComparison.OrdinalIgnoreCase);
        var rel = (forceGlobal ? file[PropScope.GlobalPrefix.Length..] : file).Trim().Replace('\\', '/');
        if (Path.IsPathRooted(rel))
            return (rel, rel, null);
        if (rel.Split('/').Any(p => p == ".."))
            throw new InvalidOperationException($"model file \"{file}\" must be a path inside the game folder (no \"..\")");
        if (!forceGlobal && PackDir != null)
        {
            var local = Path.Combine(PackDir, rel);
            if (File.Exists(local))
                return (local, $"Maps/{Path.GetFileName(PackDir)}/{rel}", rel);
        }
        var global = Path.Combine(AppContext.BaseDirectory, rel);
        if (File.Exists(global))
            return (global, rel, null);
        static IEnumerable<string> ModelsIn(string dir) => Directory.Exists(dir)
            ? Directory.GetFiles(dir).Where(f => f.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
                .Select(f => "Models/" + Path.GetFileName(f))
            : [];
        var near = ModelsIn(MeshLibrary.ModelsFolder).ToList();
        var pack = PackDir != null && !forceGlobal ? ModelsIn(Path.Combine(PackDir, "Models")).ToList() : [];
        var where = PackDir != null && !forceGlobal ? $"looked in Maps/{Path.GetFileName(PackDir)} and the game folder" : "looked in the game folder";
        var list = pack.Select(p => p + " (pack)").Concat(near.Select(n => forceGlobal ? PropScope.GlobalPrefix + n : n)).Order(StringComparer.Ordinal).ToArray();
        throw new InvalidOperationException($"model file \"{file}\" not found ({where}){(list.Length > 0 ? $"; models: {string.Join(", ", list)}" : "")}");
    }

    /// <summary>Remembers a pack-local model for the fingerprint.</summary>
    private void NotePackModel(string rel, ModelAsset model) => _packModels[rel.Replace('\\', '/').ToLowerInvariant()] = ModelHash(model);

    /// <summary>
    /// A fingerprint of a model's content: its meshes' full-detail vertices and triangles, materials and parts. Taken
    /// from the loaded data, not the file bytes, so line endings in a .gltf don't matter.
    /// </summary>
    public static uint ModelHash(ModelAsset model)
    {
        var h = 2166136261u;
        void Mix(uint v)
        {
            for (var i = 0; i < 4; i++)
            {
                h ^= (v >> (i * 8)) & 0xFF;
                h *= 16777619u;
            }
        }
        void F(float f) => Mix(BitConverter.SingleToUInt32Bits(MathF.Round(f * 10000f) / 10000f));
        Mix((uint)model.Meshes.Length);
        foreach (var m in model.Meshes)
        {
            var d = m.Lods[0];
            Mix((uint)d.Vertices.Length);
            Mix((uint)d.Indices.Length);
            foreach (var v in d.Vertices)
            {
                F(v.Position.X); F(v.Position.Y); F(v.Position.Z);
            }
            foreach (var i in d.Indices)
                Mix(i);
        }
        Mix((uint)model.Materials.Length);
        foreach (var mat in model.Materials)
        {
            F(mat.BaseColor.X); F(mat.BaseColor.Y); F(mat.BaseColor.Z); F(mat.BaseColor.W);
        }
        Mix((uint)model.Textures);
        foreach (var p in model.Parts)
        {
            Mix((uint)p.Mesh);
            var t = p.Transform;
            F(t.M11); F(t.M12); F(t.M13); F(t.M21); F(t.M22); F(t.M23); F(t.M31); F(t.M32); F(t.M33); F(t.M41); F(t.M42); F(t.M43);
        }
        return h == 0 ? 1 : h;
    }
}
