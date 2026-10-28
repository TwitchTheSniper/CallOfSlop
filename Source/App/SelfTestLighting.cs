using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using CallOfSlop.MapKit;

namespace CallOfSlop;

/// <summary>
/// Baked lighting (docs/render/LIGHTING.md, Source/Maps/LightBaker.cs, Source/Render/LightGrid.cs): a small seeded scene
/// baked and probed. A wall shadows a light, the light falls off with distance and stops at its range, a roof shades the
/// sun and the sky, spot cones and area lights face the right way, prop lights turn with their prop (and only light lit
/// maps), the bake is the same twice and on one thread, it reads back from a map bake exactly, a changed light changes
/// the bake key and the grid, unlit maps stay unlit, and the per-frame light block costs no allocation.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The seeded scene: a light at (0, 2, 0), a wall at x -1.5..-1.2, a roof over (4..10, -10..-4), a spot, an area light and a torch prop.</summary>
    private static string LightScene(float intensity = 1f, bool propLights = true) => $$"""
        {
          "format": "call-of-slop-map/1", "name": "Light Scene", "halfX": 16, "halfZ": 16,
          "ops": [
            { "op": "Ground", "color": [0.5, 0.5, 0.5] },
            { "op": "BakedLighting", "cell": 0.5, "indoor": 0.2, "propLights": {{(propLights ? "true" : "false")}} },
            { "op": "Block", "x0": -1.5, "y0": 0, "z0": -4, "x1": -1.2, "y1": 4, "z1": 4, "color": [0.6, 0.6, 0.6] },
            { "op": "Block", "x0": 4, "y0": 3, "z0": -10, "x1": 10, "y1": 3.3, "z1": -4, "color": [0.6, 0.6, 0.6] },
            { "op": "LightSource", "x": 0, "y": 2, "z": 0, "color": [1, 1, 1], "intensity": {{intensity.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, "range": 8 },
            { "op": "LightSource", "x": 0, "y": 6, "z": 10, "color": [1, 0.9, 0.8], "type": "spot", "pitch": 90, "cone": 30, "range": 9, "intensity": 2 },
            { "op": "LightSource", "x": -10, "y": 2, "z": 10, "color": [0.6, 0.7, 1], "type": "area", "yaw": 90, "pitch": 0, "w": 1.5, "d": 1.5, "range": 6, "intensity": 2 },
            { "op": "Prop", "name": "Iron Torch", "x": 10, "z": 10, "y": 2, "yaw": 90 },
            { "op": "Spawn", "x": 12, "z": -12 },
            { "op": "Spawn", "x": -12, "z": 12 }
          ]
        }
        """;

    private static Map LightSceneMap(float intensity = 1f, bool propLights = true)
    {
        var m = MapFile.Load(LightScene(intensity, propLights), "Dev/LightScene.json", "");
        m.Validate();
        return m;
    }

    private static float Lum(LightBake b, Vector3 p) => LightProbe(b, p);

    private static void CheckBakedLighting()
    {
        var m = LightSceneMap();
        if (m.LightBake is not { } b)
        {
            Fail("Baked lighting: the scene with LightSource ops has no light bake");
            return;
        }
        Line($"scene: grid {b.NX} x {b.NY} x {b.NZ} at {b.Cell:0.##} m ({b.Cells} cells, {b.Bytes / 1024} KB), {b.Sources.Length} lights, baked in {b.BakeMs:0.0} ms, hash {b.Hash:x16}");

        // ---- Shadow and falloff ----
        var open = Lum(b, new Vector3(3, 2, 0));
        var shadowed = Lum(b, new Vector3(-3, 2, 0));
        if (!(open > 0.05f) || !(shadowed < open * 0.05f))
            Fail($"Baked lighting: the wall doesn't shadow the light: {open:0.000} in the open at 3 m, {shadowed:0.000} behind the wall at 3 m");
        var falloff = new float[6];
        for (var i = 0; i < falloff.Length; i++)
            falloff[i] = Lum(b, new Vector3(1 + i, 2, 0));
        for (var i = 1; i < falloff.Length; i++)
            if (!(falloff[i] < falloff[i - 1]))
                Fail($"Baked lighting: no falloff: {string.Join(", ", falloff.Select(f => f.ToString("0.000")))} at 1-6 m");
        var past = Lum(b, new Vector3(0, 2, -9.5f)); // past the range (8 m), nothing else near
        if (past > 0.002f)
            Fail($"Baked lighting: light reaches past its range: {past:0.0000} at 9.5 m (range 8)");
        if (LightBaker.Falloff(0, 8) != 1f || LightBaker.Falloff(8, 8) != 0f || !(LightBaker.Falloff(2, 8) > LightBaker.Falloff(4, 8)))
            Fail("Baked lighting: Falloff isn't 1 at the light, 0 at the range and falling between");
        Line($"light at 1-6 m: {string.Join(", ", falloff.Select(f => f.ToString("0.000")))}; behind the wall {shadowed:0.0000} (open {open:0.000}); past the range {past:0.0000}");

        // ---- Direction: a surface facing the light gets it, one facing away doesn't ----
        b.Sample(new Vector3(3, 2, 0), out var la, out var da, out _);
        var facing = LightBake.Shade(la, da, -Vector3.UnitX).Length();
        var away = LightBake.Shade(la, da, Vector3.UnitX).Length();
        if (!(away < facing * 0.15f))
            Fail($"Baked lighting: the light's direction is lost: facing {facing:0.000}, facing away {away:0.000}");

        // ---- Sun and sky under the roof ----
        b.Sample(new Vector3(7, 1, -7), out _, out var dRoof, out var sRoof);
        b.Sample(new Vector3(7, 1, 7), out _, out var dOpen, out var sOpen);
        if (!(sRoof.X < 0.1f) || !(sOpen.X > 0.9f))
            Fail($"Baked lighting: sun under the roof {sRoof.X:0.00}, in the open {sOpen.X:0.00} (want under 0.1 and over 0.9)");
        if (!(dRoof.W < dOpen.W - 0.1f))
            Fail($"Baked lighting: the roof doesn't cut the sky light: ambient {dRoof.W:0.00} under it, {dOpen.W:0.00} in the open");
        Line($"sun under the roof {sRoof.X:0.00}, open {sOpen.X:0.00}; ambient under it {dRoof.W:0.00}, open {dOpen.W:0.00}");

        // ---- Spot cone and area light ----
        var inCone = Lum(b, new Vector3(0, 1, 10));
        var outCone = Lum(b, new Vector3(0, 1, 14)); // 4 m off the axis 5 m down: 39 degrees, outside a 30 degree cone
        if (!(inCone > 0.1f) || !(outCone < inCone * 0.05f))
            Fail($"Baked lighting: spot cone: {inCone:0.000} on the axis, {outCone:0.000} outside the cone");
        var front = Lum(b, new Vector3(-7.5f, 2, 10));
        var behind = Lum(b, new Vector3(-12.5f, 2, 10));
        if (!(front > 0.05f) || !(behind < front * 0.05f))
            Fail($"Baked lighting: area light: {front:0.000} in front, {behind:0.000} behind");
        Line($"spot: {inCone:0.000} in the cone, {outCone:0.0000} outside; area: {front:0.000} in front, {behind:0.0000} behind");

        // ---- Prop lights: turned with the prop, only on lit maps, and the option turns them off ----
        var torch = Props.Find("Iron Torch");
        if (torch == null || torch.Lights.Length != 1)
            Fail("Baked lighting: the Iron Torch prop carries no light");
        else
        {
            var placed = m.BakeLights().LastOrDefault();
            var expect = torch.Lights[0].Placed(new Vector3(10, 2, 10), MathF.PI / 2, 1f).Pos;
            if (Vector3.Distance(placed.Pos, expect) > 1e-3f || !(placed.Pos.X > 10.1f))
                Fail($"Baked lighting: the torch's light is at {placed.Pos}, expected {expect} (turned 90 degrees: in front of it at +x)");
            if (LightSceneMap(propLights: false).BakeLights().Count != 3)
                Fail("Baked lighting: \"propLights\": false still bakes the props' lights");
        }
        var plain = MapFile.Load("""{ "name": "Unlit", "halfX": 8, "halfZ": 8, "ops": [ { "op": "Ground", "color": [0.5, 0.5, 0.5] }, { "op": "Prop", "name": "Iron Torch", "x": 0, "z": 0, "y": 2 }, { "op": "Spawn", "x": 3, "z": 3 } ] }""", "Dev/Unlit.json", "");
        plain.Validate();
        if (plain.IsLit || plain.LightBake != null)
            Fail("Baked lighting: a map with a lit prop but no LightSource or BakedLighting op got a light bake (it must look as before)");

        // ---- Determinism: again, and on one thread ----
        var again = LightBaker.Bake(m);
        LightBaker.Parallel = false;
        LightBake serial;
        try
        {
            serial = LightBaker.Bake(m);
        }
        finally
        {
            LightBaker.Parallel = true;
        }
        if (again.Hash != b.Hash || serial.Hash != b.Hash || !again.Light.AsSpan().SequenceEqual(b.Light) || !serial.Dir.AsSpan().SequenceEqual(b.Dir) || !serial.Sun.AsSpan().SequenceEqual(b.Sun))
            Fail($"Baked lighting: not deterministic: {b.Hash:x16}, again {again.Hash:x16}, one thread {serial.Hash:x16}");
        Line($"deterministic: the same grid again ({again.BakeMs:0.0} ms) and on one thread ({serial.BakeMs:0.0} ms)");

        // ---- A map bake reads it back exactly; a changed light changes the key and the grid ----
        var inputs = SHA256.HashData(Encoding.UTF8.GetBytes("inputs"));
        var key = MapBake.KeyOf("Dev/LightScene.json", SHA256.HashData(Encoding.UTF8.GetBytes(LightScene())), inputs);
        var file = MapBake.Write(m, "Dev/LightScene.json", key, "", "", MapBake.IndexFor(null));
        var back = MapBake.Read(file, "Dev/LightScene.json", key, null, out _, out _, out var why);
        if (back == null)
            Fail($"Baked lighting: the map bake didn't read back ({why})");
        else if (MapBake.Difference(m, back) is { } diff)
            Fail($"Baked lighting: the map read back from its bake differs: {diff}");
        else if (back.LightBake is not { } bb || bb.Hash != b.Hash || bb.BakeMs != 0)
            Fail("Baked lighting: the light grid didn't come back with the map bake");
        var brighter = LightSceneMap(2f);
        var key2 = MapBake.KeyOf("Dev/LightScene.json", SHA256.HashData(Encoding.UTF8.GetBytes(LightScene(2f))), inputs);
        if (key.AsSpan().SequenceEqual(key2) || brighter.LightBake?.Hash == b.Hash)
            Fail("Baked lighting: a brighter light gives the same bake key or the same grid");
        if (!(Lum(brighter.LightBake!, new Vector3(3, 2, 0)) > open * 1.9f))
            Fail("Baked lighting: twice the intensity isn't twice the light");
        Line($"map bake: {file.Length / 1024} KB, read back equal; a changed light: new key and grid {brighter.LightBake?.Hash:x16} (prop lights: the Props folder is in every map's key)");

        // ---- Per-frame: the light block and the probe allocate nothing; unlit maps draw in mode 0 ----
        var block = new LightGrid.FrameBlock();
        var dynPos = new Vector4[LightGrid.MaxDynamic];
        var dynCol = new Vector4[LightGrid.MaxDynamic];
        LightGrid.Fill(ref block, null, 1.0, 0, dynPos, dynCol);
        if (block.Mode.X != 0f)
            Fail("Baked lighting: an unlit map's light block isn't mode 0 (the classic light)");
        var view = Matrix4x4.CreateLookAt(new Vector3(2, 2, 2), Vector3.Zero, Vector3.UnitY);
        for (var i = 0; i < 50; i++) // (warm up)
        {
            LightGrid.Fill(ref block, b, i, 2, dynPos, dynCol);
            LightGrid.FillProbe(ref block, b, new Vector3(i * 0.1f, 1.7f, 0), view, i);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2000; i++)
        {
            LightGrid.Fill(ref block, b, i * 0.016, 4, dynPos, dynCol);
            LightGrid.FillProbe(ref block, b, new Vector3(i * 0.01f, 1.7f, -3), view, i * 0.016);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated != 0)
            Fail($"Baked lighting: the per-frame light block allocated {allocated} bytes over 2000 frames");
        LightGrid.Fill(ref block, b, 1.0, 9, dynPos, dynCol);
        if (block.Mode.X != 1f || block.Mode.W != LightGrid.MaxDynamic)
            Fail($"Baked lighting: the world block is mode {block.Mode.X} with {block.Mode.W} dynamic lights (want 1 and at most {LightGrid.MaxDynamic})");
        Line($"per frame: light block and eye probe, 0 bytes over 2000 frames; dynamic lights capped at {LightGrid.MaxDynamic}");

        // ---- The map kit: light ops are checked and twin under symmetry ----
        var spot = new JObject(("op", "LightSource"), ("x", 3), ("y", 4), ("z", 2), ("color", "#ffffff"), ("type", "spot"), ("yaw", 30), ("pitch", 45));
        Kit.Validate(spot);
        var twin = Kit.Twin(spot, Symmetry.MirrorX);
        if (twin == null || twin.Num("x") != -3 || twin.Num("yaw") != 330)
            Fail($"Baked lighting: a spot light's x mirror is {twin}, expected x -3 and yaw 330");

        // ---- A region: the grid covers only it (and the kit writes one the game reads) ----
        var regionText = LightScene().Replace("\"op\": \"BakedLighting\", \"cell\": 0.5,", "\"op\": \"BakedLighting\", \"cell\": 0.5, \"region\": { \"x0\": -4, \"z0\": -4, \"x1\": 6, \"z1\": 4 }, \"top\": 5,");
        var regional = MapFile.Load(regionText, "Dev/LightScene.json", "");
        regional.Validate();
        if (regional.LightBake is not { } rb || rb.Origin.X != -4 || rb.Origin.Z != -4 || MathF.Abs(rb.NX * rb.Cell - 10) > rb.Cell || rb.Max.Y > 5.01f)
            Fail($"Baked lighting: a 10 x 8 m region up to 5 m gave a grid from {regional.LightBake?.Origin} to {regional.LightBake?.Max}");
        else if (MathF.Abs(Lum(rb, new Vector3(3, 2, 0)) - open) > 0.01f)
            Fail($"Baked lighting: inside its region the grid differs from the whole map's ({Lum(rb, new Vector3(3, 2, 0)):0.000} vs {open:0.000})");
        var kit = new Kit("Region", "", 1000);
        kit.BakedLighting(region: (-4, -4, 6, 4), top: 5);
        if (kit.Ops[^1].Get("region") is not JObject)
            Fail("Baked lighting: the kit's BakedLighting region isn't written");

        // ---- The shipped light test map ----
        if (Maps.Find("Light Test") is { LightBake: { } lt } ltMap)
            Line($"Light Test: grid {lt.NX} x {lt.NY} x {lt.NZ} at {lt.Cell:0.##} m, {lt.Sources.Length} lights ({ltMap.Lights.Count} ops), {lt.Bytes / 1024} KB, baked in {lt.BakeMs:0} ms");
        else
            Fail("Baked lighting: the Dev map \"Light Test\" is missing or unlit");
    }
}
