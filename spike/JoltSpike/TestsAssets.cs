// The game project (CallOfSlop.csproj) compiles every .cs under the repo, so the spike's code only exists
// when JOLT_SPIKE is defined (JoltSpike.csproj defines it).
#if JOLT_SPIKE
using System.Runtime.InteropServices;
using System.Text;
using Brutal;
using Brutal.Collections;
using Brutal.JoltApi;
using Brutal.Numerics;
using Brutal.Render.Mesh;
using static JoltSpike.Report;
using G = Brutal.GltfApi;
using SN = System.Numerics;

namespace JoltSpike;

/// <summary>Section f: glTF load path, procedural meshes into Jolt colliders, and the scene graph.</summary>
public static class TestsAssets
{
    public static void Assets()
    {
        Phys.InitJolt();
        var path = Path.GetFullPath("spike_rifle.glb");
        WriteTestGlb(path);
        Metric("test .glb bytes", new FileInfo(path).Length.ToString());

        // ---- Load it with Brutal.Gltf ----
        var t0 = Clock.Now;
        using var loader = new G.GltfLoader(path);
        var gltf = loader.GltfJson;
        var meshes = gltf.Meshes?.Length ?? 0;
        var prims = gltf.Meshes?.Sum(m => m.Primitives?.Length ?? 0) ?? 0;
        var nodes = gltf.Nodes?.Length ?? 0;
        Check("glTF loads: meshes / primitives / nodes", meshes == 2 && prims == 2 && nodes == 5, $"{meshes} meshes, {prims} primitives, {nodes} nodes, {gltf.Accessors?.Length ?? 0} accessors");

        var prim = gltf.Meshes![0].Primitives[0];
        var positions = MemoryMarshal.Cast<byte, float3>(AccessorBytes(loader, prim.Attributes["POSITION"], 12)).ToArray();
        var indices = MemoryMarshal.Cast<byte, ushort>(AccessorBytes(loader, prim.Indices!.Value, 2)).ToArray();
        var loadUs = Clock.UsSince(t0);
        Check("glTF vertex data read", positions.Length == 24 && indices.Length == 36, $"{positions.Length} verts, {indices.Length} indices, load+read {F(loadUs, 0)} us");

        // ---- glTF nodes -> scene graph; find the muzzle attach point; attach a suppressor; turn the gun ----
        var root = new Node("scene");
        foreach (var n in gltf.Scenes![gltf.Scene ?? 0].Nodes)
            root.Add(FromGltf(gltf, n));
        root.UpdateWorld();
        var muzzle = root.Find("Muzzle");
        Check("attach point found by name", muzzle != null && Near(muzzle.WorldPosition, new SN.Vector3(0, 0.02f, -0.95f)), $"muzzle at {muzzle?.WorldPosition}");
        var suppressor = muzzle!.Add(new Node("Suppressor") { LocalPosition = new SN.Vector3(0, 0, -0.1f) });
        var rifle = root.Find("Rifle")!;
        rifle.LocalRotation = SN.Quaternion.CreateFromAxisAngle(SN.Vector3.UnitY, MathF.PI / 2); // turn 90 deg left
        rifle.LocalPosition = new SN.Vector3(10, 1.5f, 0);
        root.UpdateWorld();
        Check("child follows parent transform", Near(suppressor.WorldPosition, new SN.Vector3(10 - 1.05f, 1.52f, 0)), $"suppressor at {suppressor.WorldPosition} (expected (8.95, 1.52, 0))");

        // ---- glTF mesh -> Jolt colliders (convex hull and triangle mesh), placed by the node's world matrix ----
        var receiver = root.Find("Receiver")!;
        var worldVerts = positions.Select(v => ToF3(SN.Vector3.Transform(new SN.Vector3(v.X, v.Y, v.Z), receiver.World))).ToArray();
        var phys = new Phys();
        var tris = new IndexedTriangle[indices.Length / 3];
        for (var i = 0; i < tris.Length; i++)
            tris[i] = Phys.IT(indices[i * 3], indices[i * 3 + 1], indices[i * 3 + 2]);
        var hull = Phys.MakeHull(worldVerts, 0.01f, worldVerts, tris);
        var mesh = Phys.MakeMesh(worldVerts, tris);
        var hullBody = phys.AddStatic(hull, float3.Zero, floatQuat.Identity);
        var meshBody = phys.AddStatic(mesh, new float3(0, 0, 5), floatQuat.Identity);
        phys.System.OptimizeBroadPhase();
        var top = 10 - phys.Raycast(new float3(10, 10, 0), new float3(0, -1, 0), 20, out var hb);
        var top2 = 10 - phys.Raycast(new float3(10, 10, 5), new float3(0, -1, 0), 20, out var mb);
        if (!Phys.RaysAvailable)
            Metric("collider check", "glTF mesh turned into Jolt bodies, but no ray casts to verify them (no native export)");
        else
            Check("glTF mesh as convex hull and as MeshShape, ray hits both", hb == hullBody && mb == meshBody && MathF.Abs(top - 1.56f) < 0.02f && MathF.Abs(top2 - 1.56f) < 0.02f,
            $"hull top y={F(top)}, mesh top y={F(top2)} (receiver top 1.56)");

        // ---- Brutal.Render.Mesh procedural primitive -> convex hull ----
        try
        {
            var pos = new NativeStrideList(ByteSize.Of<float3>());
            var idx = new NativeStrideList(ByteSize.Of<uint>());
            var nrm = new NativeStrideList(ByteSize.Of<float3>());
            var pg = new ProcGenMesh(pos, idx, nrm);
            ProcGenMeshLibrary.GenerateCylinder(pg, float3.Zero, new float3(0, 1, 0), 0.5f, 2f, 16);
            var pts = pos.AsSpan<float3>().ToArray();
            var minY = pts.Min(p => p.Y);
            var maxY = pts.Max(p => p.Y);
            var cidx = idx.AsSpan<uint>().ToArray();
            var ctris = new IndexedTriangle[cidx.Length / 3];
            for (var i = 0; i < ctris.Length; i++)
                ctris[i] = Phys.IT(cidx[i * 3], cidx[i * 3 + 1], cidx[i * 3 + 2]);
            var cylHull = Phys.MakeHull(pts, 0.02f, pts, ctris);
            var cylBody = phys.AddStatic(cylHull, new float3(-10, 0, 0), floatQuat.Identity);
            var cylTop = 10 - phys.Raycast(new float3(-10, 10, 0), new float3(0, -1, 0), 20, out var cb);
            if (!Phys.RaysAvailable)
                Metric("procgen", $"ProcGenMesh cylinder: {pg.VertexCount} verts, {pg.IndexCount} indices -> Jolt body {cylBody}");
            else
            Check("ProcGenMesh cylinder -> convex hull collider", cb == cylBody && MathF.Abs(cylTop - maxY) < 0.03f,
                $"{pg.VertexCount} verts, {pg.IndexCount} indices, y {F(minY)}..{F(maxY)}, ray top {F(cylTop)}");
        }
        catch (Exception e)
        {
            Check("ProcGenMesh cylinder -> convex hull collider", false, e.GetType().Name + ": " + e.Message);
        }

        // ---- Scene graph cost: 10k nodes (100 props x 100 parts), move 1 prop vs everything ----
        var world = new Node("world");
        var props = new List<Node>();
        for (var i = 0; i < 100; i++)
        {
            var prop = world.Add(new Node($"prop{i}") { LocalPosition = new SN.Vector3(i, 0, 0) });
            props.Add(prop);
            for (var j = 0; j < 99; j++)
                prop.Add(new Node($"part{j}") { LocalPosition = new SN.Vector3(0, j * 0.1f, 0) });
        }
        world.UpdateWorld();
        var t1 = Clock.Now;
        for (var rep = 0; rep < 100; rep++)
            world.UpdateWorld();
        var cleanUs = Clock.UsSince(t1) / 100;
        t1 = Clock.Now;
        for (var rep = 0; rep < 100; rep++)
        {
            props[rep % 100].LocalRotation = SN.Quaternion.CreateFromAxisAngle(SN.Vector3.UnitY, rep * 0.1f);
            world.UpdateWorld();
        }
        var oneUs = Clock.UsSince(t1) / 100;
        t1 = Clock.Now;
        for (var rep = 0; rep < 20; rep++)
        {
            world.LocalPosition = new SN.Vector3(rep, 0, 0);
            world.UpdateWorld();
        }
        var allUs = Clock.UsSince(t1) / 20;
        Metric("scene graph 10k nodes: walk clean / 1 prop moved / root moved, us", $"{F(cleanUs, 1)} / {F(oneUs, 1)} / {F(allUs, 1)}");
    }

    /// <summary>Tightly packed accessor bytes (the published package has no accessor helper yet).</summary>
    static ReadOnlySpan<byte> AccessorBytes(G.GltfLoader loader, int accessor, int elementSize)
    {
        var acc = loader.GltfJson.Accessors[accessor];
        var view = loader.GltfJson.BufferViews[acc.BufferView!.Value];
        var buffer = loader.GetOrLoadBuffer(view.Buffer);
        return buffer.Slice(view.ByteOffset + acc.ByteOffset, acc.Count * elementSize);
    }

    static Node FromGltf(G.Gltf gltf, int index)
    {
        var g = gltf.Nodes[index];
        var n = new Node(g.Name ?? $"node{index}")
        {
            LocalPosition = new SN.Vector3(g.Translation.X, g.Translation.Y, g.Translation.Z),
            LocalRotation = new SN.Quaternion(g.Rotation.X, g.Rotation.Y, g.Rotation.Z, g.Rotation.W),
            LocalScale = new SN.Vector3(g.Scale.X, g.Scale.Y, g.Scale.Z),
        };
        if (g.Children != null)
            foreach (var c in g.Children)
                n.Add(FromGltf(gltf, c));
        return n;
    }

    static bool Near(SN.Vector3 a, SN.Vector3 b) => SN.Vector3.Distance(a, b) < 0.005f;
    static float3 ToF3(SN.Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>
    /// Writes a tiny rifle: Rifle (root) -> Receiver (unit cube scaled to 0.1 x 0.12 x 0.6) -> Barrel (cube mesh
    /// scaled thin and long); Muzzle is an empty node used as an attach point. Hand-rolled GLB so the spike
    /// needs no external file.
    /// </summary>
    static void WriteTestGlb(string path)
    {
        // Unit cube, 24 verts (4 per face) + normals, 36 uint16 indices.
        var pos = new List<float>();
        var nrm = new List<float>();
        var idx = new List<ushort>();
        int[][] faces = [[0, 4, 6, 2], [1, 3, 7, 5], [0, 1, 5, 4], [2, 6, 7, 3], [0, 2, 3, 1], [4, 5, 7, 6]];
        float[][] normals = [[-1, 0, 0], [1, 0, 0], [0, -1, 0], [0, 1, 0], [0, 0, -1], [0, 0, 1]];
        for (var f = 0; f < 6; f++)
        {
            var b = (ushort)(pos.Count / 3);
            foreach (var v in faces[f])
            {
                pos.AddRange([(v & 1) != 0 ? 0.5f : -0.5f, (v & 2) != 0 ? 0.5f : -0.5f, (v & 4) != 0 ? 0.5f : -0.5f]);
                nrm.AddRange(normals[f]);
            }
            idx.AddRange([b, (ushort)(b + 1), (ushort)(b + 2), b, (ushort)(b + 2), (ushort)(b + 3)]);
        }
        var bin = new List<byte>();
        foreach (var x in pos) bin.AddRange(BitConverter.GetBytes(x));
        foreach (var x in nrm) bin.AddRange(BitConverter.GetBytes(x));
        foreach (var x in idx) bin.AddRange(BitConverter.GetBytes(x));
        while (bin.Count % 4 != 0) bin.Add(0);
        int posLen = pos.Count * 4, nrmLen = nrm.Count * 4, idxLen = idx.Count * 2;

        var json = $$"""
        {
          "asset": { "version": "2.0", "generator": "JoltSpike" },
          "scene": 0,
          "scenes": [ { "nodes": [0] } ],
          "nodes": [
            { "name": "Rifle", "children": [1, 3, 4] },
            { "name": "Receiver", "mesh": 0, "scale": [0.1, 0.12, 0.6], "children": [2] },
            { "name": "Barrel", "mesh": 1, "translation": [0, 0.15, -0.8], "scale": [0.3, 0.2, 0.5] },
            { "name": "Muzzle", "translation": [0, 0.02, -0.95] },
            { "name": "Grip", "translation": [0, -0.12, 0.1] }
          ],
          "meshes": [
            { "name": "ReceiverMesh", "primitives": [ { "attributes": { "POSITION": 0, "NORMAL": 1 }, "indices": 2 } ] },
            { "name": "BarrelMesh", "primitives": [ { "attributes": { "POSITION": 0, "NORMAL": 1 }, "indices": 2 } ] }
          ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 24, "type": "VEC3", "min": [-0.5, -0.5, -0.5], "max": [0.5, 0.5, 0.5] },
            { "bufferView": 1, "componentType": 5126, "count": 24, "type": "VEC3" },
            { "bufferView": 2, "componentType": 5123, "count": 36, "type": "SCALAR" }
          ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0, "byteLength": {{posLen}} },
            { "buffer": 0, "byteOffset": {{posLen}}, "byteLength": {{nrmLen}} },
            { "buffer": 0, "byteOffset": {{posLen + nrmLen}}, "byteLength": {{idxLen}} }
          ],
          "buffers": [ { "byteLength": {{bin.Count}} } ]
        }
        """;
        var jsonBytes = Encoding.UTF8.GetBytes(json).ToList();
        while (jsonBytes.Count % 4 != 0) jsonBytes.Add((byte)' ');
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write(0x46546C67u); // "glTF"
        bw.Write(2u);
        bw.Write((uint)(12 + 8 + jsonBytes.Count + 8 + bin.Count));
        bw.Write((uint)jsonBytes.Count);
        bw.Write(0x4E4F534Au); // "JSON"
        bw.Write(jsonBytes.ToArray());
        bw.Write((uint)bin.Count);
        bw.Write(0x004E4942u); // "BIN\0"
        bw.Write(bin.ToArray());
    }
}
#endif
