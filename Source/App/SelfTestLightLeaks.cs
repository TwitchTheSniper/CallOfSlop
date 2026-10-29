using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// "Light leaks" (board items dev-light-leak-ll01 and dev-light-phantoms-lp01; docs/render/LIGHTING.md "Walls and what casts
/// shadows"): baked light stays on its side of thin walls (a wall with a cell centre inside it and one between two
/// centres, read the way the shader reads them: the outside face and the floor at its foot), a light inside a wall is
/// moved out to one side and lights only that side, a prop's light is baked where its flame is drawn, shadows come from
/// the props' drawn parts (not their colliders) and nothing thinner than half a cell casts one, a prop's own parts don't
/// shadow its light, and the Map contract's audit finds an invisible blocker and passes the shipped Light Test.
/// </summary>
public static partial class SelfTest
{
    /// <summary>
    /// A lit room between two thin walls (4 m tall, z -5..5): wall A at x -1.4..-1.1 holds the cell centres x = -1.25, wall B
    /// at x 2.4..2.6 sits between the centres 2.25 and 2.75. A light at (0.6, 2, 0) between them; a second light at (-6, 2,
    /// 8) inside a 0.4 m wall at z 7.8..8.2 (so it must be moved out, to the -z side: the nearer face); a Brazier, an Iron
    /// Torch and a 0.1 m pole for the prop and thin-occluder checks.
    /// </summary>
    private const string LeakScene = """
        {
          "format": "call-of-slop-map/1", "name": "Leak Scene", "halfX": 16, "halfZ": 16,
          "ops": [
            { "op": "Ground", "color": [0.5, 0.5, 0.5] },
            { "op": "BakedLighting", "cell": 0.5, "indoor": 0.2, "sunShadows": false },
            { "op": "Block", "x0": -1.4, "y0": 0, "z0": -5, "x1": -1.1, "y1": 4, "z1": 5, "color": [0.6, 0.6, 0.6] },
            { "op": "Block", "x0": 2.4, "y0": 0, "z0": -5, "x1": 2.6, "y1": 4, "z1": 5, "color": [0.6, 0.6, 0.6] },
            { "op": "LightSource", "x": 0.6, "y": 2, "z": 0, "color": [1, 0.8, 0.6], "intensity": 2, "range": 9 },
            { "op": "Block", "x0": -10, "y0": 0, "z0": 7.8, "x1": -2, "y1": 4, "z1": 8.2, "color": [0.6, 0.6, 0.6] },
            { "op": "LightSource", "x": -6, "y": 2, "z": 7.9, "color": [1, 1, 1], "intensity": 2, "range": 6 },
            { "op": "Prop", "name": "Brazier", "x": 9, "z": -9 },
            { "op": "LightSource", "x": 9, "y": 3, "z": -12, "color": [1, 1, 1], "intensity": 2, "range": 9 },
            { "op": "Block", "x0": 11.95, "y0": 0, "z0": -12.05, "x1": 12.05, "y1": 2.5, "z1": -11.95, "color": [0.2, 0.2, 0.2] },
            { "op": "Prop", "name": "Iron Torch", "x": -9, "z": -9, "y": 2, "yaw": 90, "scale": 1.5 },
            { "op": "Spawn", "x": 14, "z": 14 },
            { "op": "Spawn", "x": -14, "z": -14 }
          ]
        }
        """;

    /// <summary>The luminance a surface at <paramref name="p"/> facing <paramref name="n"/> gets from the baked lights, read as the shader reads it.</summary>
    private static float SurfaceLum(LightBake b, Vector3 p, Vector3 n)
    {
        b.SampleSurface(p, n, out var light, out var dir, out _);
        var s = LightBake.Shade(light, dir, n);
        return s.X * 0.2126f + s.Y * 0.7152f + s.Z * 0.0722f;
    }

    private static void CheckLightLeaks()
    {
        var m = MapFile.Load(LeakScene, "Dev/LeakScene.json", "");
        m.Validate();
        if (m.LightBake is not { } b)
        {
            Fail("Light leaks: the scene has no light bake");
            return;
        }

        // ---- Thin walls: the outside face and the floor at its foot get none of the room's light ----
        float inside = 0, worst = 0;
        var worstAt = "";
        foreach (var z in new[] { -1.5f, -0.6f, 0f, 0.7f, 1.3f })
        {
            inside = MathF.Max(inside, SurfaceLum(b, new Vector3(-1.1f, 1.5f, z), Vector3.UnitX));
            inside = MathF.Max(inside, SurfaceLum(b, new Vector3(2.4f, 1.5f, z), -Vector3.UnitX));
            foreach (var (p, n, what) in new[]
                     {
                         (new Vector3(-1.4f, 0.4f, z), -Vector3.UnitX, "wall A's outside face low"), (new Vector3(-1.4f, 2.0f, z), -Vector3.UnitX, "wall A's outside face"),
                         (new Vector3(-1.45f, 0f, z), Vector3.UnitY, "the floor at wall A's foot"), (new Vector3(-1.7f, 0f, z), Vector3.UnitY, "the floor 0.3 m out from wall A"),
                         (new Vector3(2.6f, 0.4f, z), Vector3.UnitX, "wall B's outside face low"), (new Vector3(2.6f, 2.0f, z), Vector3.UnitX, "wall B's outside face"),
                         (new Vector3(2.65f, 0f, z), Vector3.UnitY, "the floor at wall B's foot"), (new Vector3(2.9f, 0f, z), Vector3.UnitY, "the floor 0.3 m out from wall B"),
                     })
            {
                var v = SurfaceLum(b, p, n);
                if (v > worst)
                {
                    worst = v;
                    worstAt = $"{what} at z {z}";
                }
            }
        }
        if (!(inside > 0.2f) || !(worst < inside * 0.02f))
            Fail($"Light leaks: a thin wall lets the room's light through: {worst:0.000} on {worstAt}, {inside:0.000} on the inside faces");
        Line($"thin walls: inside faces {inside:0.000}, outside faces and the floor at their feet at most {worst:0.0000} ({worstAt})");

        // ---- The cell flags: the walls' blocks are mixed, open air is not ----
        bool Mixed(Vector3 p)
        {
            var g = (p - b.Origin) / b.CellSize - new Vector3(0.5f);
            var i = (((int)MathF.Floor(g.Z) * b.NY + (int)MathF.Floor(g.Y)) * b.NX + (int)MathF.Floor(g.X)) * 4;
            return (b.Sun[i] & LightBaker.FlagMixed) != 0;
        }
        if (!Mixed(new Vector3(-1.3f, 1.6f, 0.1f)) || !Mixed(new Vector3(2.5f, 1.6f, 0.1f)) || Mixed(new Vector3(6.1f, 1.6f, 3.1f)))
            Fail($"Light leaks: cell flags: wall A's block mixed {Mixed(new Vector3(-1.3f, 1.6f, 0.1f))}, wall B's {Mixed(new Vector3(2.5f, 1.6f, 0.1f))}, open air {Mixed(new Vector3(6.1f, 1.6f, 3.1f))} (want true, true, false)");

        // ---- A light inside a wall is moved out to the nearer face and lights only that side ----
        var inWall = b.Sources.FirstOrDefault(l => l.Range == 6f);
        var near = LightProbe(b, new Vector3(-6, 2, 6.5f));
        var far = LightProbe(b, new Vector3(-6, 2, 9.5f));
        if (!(inWall.Pos.Z < 7.8f && inWall.Pos.Z > 7.6f) || !(near > 0.1f) || !(far < near * 0.02f))
            Fail($"Light leaks: a light inside a wall: baked at {inWall.Pos} (want just past the face at z 7.8), {near:0.000} on its side, {far:0.000} behind the wall");
        Line($"a light inside a wall: moved to z {inWall.Pos.Z:0.00}; {near:0.000} on that side, {far:0.0000} behind the wall");

        // ---- A prop's light is baked where its flame is drawn (turned and scaled with the prop) ----
        var torchIndex = m.PropInstances.FindIndex(p => p.Def.Name == "Iron Torch");
        if (torchIndex < 0)
            Fail("Light leaks: no Iron Torch in the scene");
        else
        {
            var t = m.PropInstances[torchIndex];
            var flame = t.Def.Parts.First(pt => pt.Anim != null);
            float cs = MathF.Cos(t.Yaw), sn = MathF.Sin(t.Yaw); // (the prop renderer's turn: PropRenderer's vertex shader)
            var o = flame.Offset * t.Scale;
            var drawn = t.Position + new Vector3(o.X * cs + o.Z * sn, o.Y, -o.X * sn + o.Z * cs);
            var baked = b.Sources.Where(l => l.Seed == LightFlicker.PropLightSeed(PropAnim.Seed(t.Position), 0)).Select(l => l.Pos).FirstOrDefault();
            if (Vector3.Distance(drawn, baked) > 0.12f * t.Scale)
                Fail($"Light leaks: the torch's flame is drawn at {drawn}, its light baked at {baked}");
            Line($"prop light: the torch (turned 90, scale 1.5) drawn flame {drawn}, baked light {baked} ({Vector3.Distance(drawn, baked):0.00} m apart)");
        }

        // ---- Shadows come from what is drawn: no prop collider, nothing thinner than half a cell ----
        var occ = LightBaker.OccluderList(m, LightBaker.MinOccluder(b.Spec));
        for (var i = 0; i < m.Solid.Count; i++)
            if (m.IsPropCollider(i) && occ.Any(o2 => o2.Box.Min == m.Solid[i].Min && o2.Box.Max == m.Solid[i].Max))
                Fail($"Light leaks: a prop collider {m.Solid[i].Min} - {m.Solid[i].Max} casts shadows (the drawn parts should)");
        if (occ.Any(o2 => o2.Box.Max.X - o2.Box.Min.X < 0.2f && o2.Box.Max.Z - o2.Box.Min.Z < 0.2f && o2.Box.Max.Y > 1f))
            Fail("Light leaks: a pole thinner than half a cell is an occluder (one ray per cell draws it as a dark streak)");
        // The pole at (12, -12) stands 3 m from the light at (9, 3, -12): the floor behind it is as lit as the floor the same
        // distance off to the side.
        var behindPole = LightProbe(b, new Vector3(13.5f, 0.4f, -12));
        var besidePole = LightProbe(b, new Vector3(4.5f, 0.4f, -12)); // (as far from the light, the other way, nothing between)
        if (!(behindPole > besidePole * 0.8f))
            Fail($"Light leaks: the floor behind a 0.1 m pole is dark ({behindPole:0.000}, {besidePole:0.000} as far the other way): a thin pole casts a cell-sized shadow");
        Line($"occluders: {occ.Count} ({occ.Count(o2 => o2.Kind == LightBaker.OccluderKind.Prop)} prop parts, no colliders); behind the thin pole {behindPole:0.000}, open floor {besidePole:0.000}");

        // ---- The audit: an invisible blocker is found; the shipped Light Test matches ----
        var hidden = MapFile.Load(LeakScene.Replace("\"op\": \"Spawn\", \"x\": 14", "\"op\": \"Collider\", \"x0\": 4, \"y0\": 0, \"z0\": 4, \"x1\": 6, \"y1\": 2, \"z1\": 6 },\n            { \"op\": \"Spawn\", \"x\": 14"), "Dev/LeakScene.json", "");
        hidden.Validate();
        var found = LightBaker.Audit(hidden);
        if (!found.Any(f => f.Contains("hidden collider")))
            Fail($"Light leaks: the audit misses a hidden collider with no mesh over it ({string.Join("; ", found)})");
        var moves = new List<string>();
        if (LightBaker.Audit(m, moves) is { Count: > 0 } sceneProblems)
            Fail($"Light leaks: the scene's audit: {string.Join("; ", sceneProblems)}");
        if (moves.Count != 1)
            Fail($"Light leaks: the scene's audit lists {moves.Count} moved lights (want the one inside the wall): {string.Join("; ", moves)}");
        if (Maps.Find("Light Test") is { } lt && LightBaker.Audit(lt) is { Count: > 0 } ltProblems)
            Fail($"Light leaks: Light Test's audit: {string.Join("; ", ltProblems)}");
        Line($"audit: finds a hidden collider with no mesh; the scene's one light in a wall listed as moved; Light Test clean");
    }
}
