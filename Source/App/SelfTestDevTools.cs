using System.Numerics;
using System.Text.Json.Nodes;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>The scripted scene: a block and an Anemometer turned 90 degrees, so a part hit proves the prop's turn is used.</summary>
    private const string DevToolsMapJson = """
        {
          "format": "call-of-slop-map/1",
          "name": "Dev Tools Test",
          "halfX": 20,
          "halfZ": 20,
          "ops": [
            {"op": "Ground"},
            {"op": "Block", "x0": 5, "y0": 0, "z0": -1, "x1": 6, "y1": 2, "z1": 1},
            {"op": "Prop", "name": "Anemometer", "x": 0, "z": 0, "yaw": 90},
            {"op": "Spawn", "x": -10, "z": 0}
          ]
        }
        """;

    /// <summary>
    /// The dev tools (Source/DevTools, UiDevTools.cs): the look-at ray names the right box, prop and part (with the prop's
    /// turn), its file op and a stable reference line; the prop editor's edits (anim axis on every spinning part, part size,
    /// duplicate / delete, undo / redo, a bad size refused with the loader's reason, the placed prop moved) go live, round-trip
    /// through a change package (the package's prop file loads to the same prop, its map op patch loads, a broken package is
    /// caught) and revert exactly; the keys are unshared; nothing allocates with the windows closed or per look.
    /// Works on temporary folders only (never the game's Props or Maps).
    /// </summary>
    private static void CheckDevTools()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "callofslop-devtools-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(tmp, "data");
        Directory.CreateDirectory(Path.Combine(data, "Maps"));
        Directory.CreateDirectory(Path.Combine(data, "Props", "Animated"));
        File.WriteAllText(Path.Combine(data, "Maps", "DevToolsTest.json"), DevToolsMapJson);
        File.Copy(Path.Combine(Props.Folder, "Animated", "Anemometer.json"), Path.Combine(data, "Props", "Animated", "Anemometer.json"));
        var def = Props.Find("Anemometer");
        if (def == null)
        {
            Fail("dev tools: no Anemometer prop");
            return;
        }
        var original = def.Parts.ToArray();
        PropEditSession? s = null;
        try
        {
            var map = MapFile.Load(DevToolsMapJson, "DevToolsTest.json", "");

            // ---- Look-at ---------------------------------------------------------------------------
            if (map.FileOpMarks.Count != 5 || map.FileOpNames[2] != nameof(Map.PlaceProp) || map.FileOpNames[1] != nameof(Map.Block))
                Fail($"dev tools: file op marks {map.FileOpMarks.Count} ({string.Join(", ", map.FileOpNames)}), want 4 ops and the closing mark");
            var box = LookAt.Probe(map, new Vector3(10, 1, 0), -Vector3.UnitX, 100);
            if (box.Kind != LookAtKind.Box || MathF.Abs(box.Point.X - 6) > 1e-3f || box.Normal != Vector3.UnitX || LookAt.OpText(map, box.FileOp) != "op #2 Block")
                Fail($"dev tools: the block probe found {box.Kind} at {box.Point}, normal {box.Normal}, '{LookAt.OpText(map, box.FileOp)}'");
            var pole = LookAt.Probe(map, new Vector3(-5, 0.75f, 0), Vector3.UnitX, 100);
            if (pole.Kind != LookAtKind.Prop || pole.Part != 1 || LookAt.OpText(map, pole.FileOp) != "op #3 PlaceProp")
                Fail($"dev tools: the pole probe found {pole.Kind} part {pole.Part + 1}, '{LookAt.OpText(map, pole.FileOp)}' (want part #2, op #3)");
            // The first cup sits at +x in the prop's frame: turned 90 degrees it is at -z in the world.
            var cup = LookAt.Probe(map, new Vector3(0, 1.6f, -5), Vector3.UnitZ, 100);
            if (cup.Kind != LookAtKind.Prop || cup.Part != 7 || MathF.Abs(cup.Point.Z + 0.36f) > 0.01f || cup.Normal != -Vector3.UnitZ)
                Fail($"dev tools: the cup probe found {cup.Kind} part {cup.Part + 1} at {cup.Point}, normal {cup.Normal} (want part #8 at z -0.36)");
            var miss = LookAt.Probe(map, new Vector3(0, 30, 0), Vector3.UnitY, 100);
            if (miss.Found)
                Fail($"dev tools: looking at the sky found {miss.Kind}");
            var reference = LookAt.Reference(map, cup);
            const string want = "Dev Tools Test / Props/Animated/Anemometer.json \"Anemometer\" part #8 of 12 (anim spin y 90 rpm) / Maps/DevToolsTest.json op #3 PlaceProp / at (0.0, 1.6, -0.4)";
            if (reference != want)
                Fail($"dev tools: reference line\n  got  {reference}\n  want {want}");
            Line($"look-at: block op #2, pole part #2, turned cup part #8; \"{reference}\"; {LookAt.Details(map, cup).Count} detail lines");

            // ---- Editor ------------------------------------------------------------------------------------
            s = PropEditSession.Open(map, 0, out var why, data);
            if (s == null)
            {
                Fail("dev tools: can't open the editor: " + why);
                return;
            }
            // The tester's example: the wind sensor's rotor on another axis (every part with the same anim block at once).
            if (!s.SetAnim(2, "axis", JsonValue.Create("z"), sameAnim: true))
                Fail("dev tools: setting the anim axis was refused: " + s.Error);
            var spinning = def.Parts.Count(p => p.Anim != null);
            if (def.Parts.Any(p => p.Anim != null && p.Anim.Axis != Vector3.UnitZ) || spinning != 11)
                Fail($"dev tools: after the axis edit {def.Parts.Count(p => p.Anim?.Axis == Vector3.UnitZ)} of {spinning} spinning parts turn about z (want 11 of 11)");
            if (LookAt.AnimText(def.Parts[7]) != "anim spin z 90 rpm")
                Fail($"dev tools: the live prop says '{LookAt.AnimText(def.Parts[7])}'");
            s.SetPartVector(1, "size", new Vector3(0.06f, 1.6f, 0.06f));
            if (def.Parts[1].Size != new Vector3(0.06f, 1.6f, 0.06f) || MathF.Abs(map.PropInstances[0].Bounds.Max.Y - 1.64f) > 0.02f)
                Fail($"dev tools: the pole is {def.Parts[1].Size} (bounds top {map.PropInstances[0].Bounds.Max.Y})");
            var copy = s.DuplicatePart(0);
            if (copy != 1 || def.Parts.Count != 13)
                Fail($"dev tools: duplicate gave part {copy}, {def.Parts.Count} parts");
            s.SetPartColor(1, new Vector3(1, 0, 0));
            s.DeletePart(1);
            if (def.Parts.Count != 12 || def.Parts[1].Size != new Vector3(0.06f, 1.6f, 0.06f))
                Fail($"dev tools: after delete {def.Parts.Count} parts, part #2 {def.Parts[1].Size}");
            s.Undo();
            if (def.Parts.Count != 13 || def.Parts[1].Color != new Vector3(1, 0, 0))
                Fail($"dev tools: undo gave {def.Parts.Count} parts, part #2 colour {def.Parts[1].Color}");
            s.Redo();
            if (def.Parts.Count != 12)
                Fail($"dev tools: redo gave {def.Parts.Count} parts");
            var before = def.Parts[0].Size;
            if (s.SetPartVector(0, "size", new Vector3(0, 1, 1)) || !s.Error.Contains("size", StringComparison.Ordinal) || def.Parts[0].Size != before || def.Parts.Count != 12)
                Fail($"dev tools: a zero size wasn't refused ('{s.Error}', size {def.Parts[0].Size})");
            var refusal = s.Error;
            var added = s.AddPart();
            if (added != 12 || def.Parts.Count != 13 || s.Error.Length > 0)
                Fail($"dev tools: add box gave {added} ({s.Error})");
            var moved = map.PropInstances[0].Position + new Vector3(1.5f, 0, -2);
            s.SetPlacement(moved, 45, 1.2f);
            if (map.PropInstances[0].Position != moved || MathF.Abs(map.PropInstances[0].Yaw - MathF.PI / 4) > 1e-5f || map.PropInstances[0].Scale != 1.2f)
                Fail("dev tools: the placed prop didn't move");
            var lookMoved = LookAt.Probe(map, moved + new Vector3(-5, 0.75f, 0), Vector3.UnitX, 100);
            if (lookMoved.Kind != LookAtKind.Prop || lookMoved.Part != 1)
                Fail($"dev tools: after the move the pole probe found {lookMoved.Kind} part {lookMoved.Part + 1}");

            // ---- The package ------------------------------------------------------------------------------
            var dir = DevEditPackage.Write(s, new DateTime(2026, 10, 28, 12, 0, 0), "rotor on the wrong axis", Path.Combine(tmp, "DevEdits"), data);
            var problems = DevEditPackage.Validate(dir, data);
            if (problems.Count > 0)
                Fail("dev tools: the package doesn't validate: " + string.Join("; ", problems));
            var edit = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, DevEditPackage.FileName)))!;
            var op = edit["mapOps"]![0];
            if (op?["opNumber"]?.GetValue<int>() != 3 || op["after"]?["x"]?.GetValue<double>() is not { } nx || Math.Abs(nx - moved.X) > 1e-3
                || op["after"]?["yaw"]?.GetValue<double>() != 45 || op["after"]?["scale"]?.GetValue<double>() is not { } sc || Math.Abs(sc - 1.2) > 1e-6)
                Fail($"dev tools: map op patch {op?.ToJsonString()}");
            var changes = edit["propChanges"]!.AsArray();
            if (!changes.Any(c => c?["path"]?.GetValue<string>() == "parts[2].anim.axis" && c["to"]?.GetValue<string>() == "z"))
                Fail("dev tools: propChanges has no parts[2].anim.axis -> z: " + changes.ToJsonString());
            var packaged = PropFile.Load(File.ReadAllText(Path.Combine(dir, "Props", "Animated", "Anemometer.json")), "Animated/Anemometer.json", "Animated");
            var same = packaged.Parts.Count == def.Parts.Count && packaged.Bounds == def.Bounds
                       && packaged.Parts.Zip(def.Parts).All(t => t.First.Size == t.Second.Size && t.First.Offset == t.Second.Offset && t.First.RotationDeg == t.Second.RotationDeg
                                                                && t.First.Color == t.Second.Color && t.First.Layer == t.Second.Layer && t.First.Anim?.Axis == t.Second.Anim?.Axis
                                                                && t.First.Anim?.Pivot == t.Second.Anim?.Pivot);
            if (!same)
                Fail("dev tools: the package's prop file doesn't load to the edited prop");
            if (!File.ReadAllText(Path.Combine(dir, "README.txt")).Contains("parts[2].anim.axis", StringComparison.Ordinal))
                Fail("dev tools: the readme doesn't list the changes");
            // A package that wouldn't load is caught.
            var bad = Path.Combine(dir, "Props", "Animated", "Anemometer.json");
            File.WriteAllText(bad, File.ReadAllText(bad).Replace("\"size\": [0.25, 0.08, 0.25]", "\"size\": [0, 0.08, 0.25]"));
            var badProblems = DevEditPackage.Validate(dir, data);
            if (badProblems.Count == 0)
                Fail("dev tools: a package with a zero-size part validated");
            Line($"editor: axis z on 11 spinning parts, size, duplicate, delete, undo / redo, add box, refused '{refusal[..Math.Min(70, refusal.Length)]}'; package {Path.GetFileName(dir)}: "
                 + $"{changes.Count} prop change(s), map op #3 patch, validates; a broken copy is caught ({badProblems[0][..Math.Min(70, badProblems[0].Length)]})");

            // ---- Revert --------------------------------------------------------------------------------------
            s.Revert();
            if (!def.Parts.SequenceEqual(original) || map.PropInstances[0].Position != s.OriginalPlacement.Position || s.Changed)
                Fail("dev tools: revert didn't put the prop back exactly");
            s = null;

            // ---- Keys and allocations ----------------------------------------------------------------------------
            var kd = Keybinds.Defaults;
            if (kd[(int)GameAction.LookAtInspector] != Keybinds.Key(Brutal.GlfwApi.GlfwKey.F6) || kd[(int)GameAction.CopyLookAt] != Keybinds.Key(Brutal.GlfwApi.GlfwKey.F5)
                || kd[(int)GameAction.PropEditor] != Keybinds.Key(Brutal.GlfwApi.GlfwKey.F10) || Duplicates(kd).Length > 0)
                Fail("dev tools: the default keys aren't unshared F6 / F5 / F10");
            var ui = new Ui();
            var vp = Matrix4x4.Identity;
            var details = new List<string>();
            var closed = SteadyAllocBytes(() =>
            {
                for (var i = 0; i < 20000; i++)
                {
                    ui.DrawLookAt(map, cup, reference, details, vp, 0, 0, 0);
                    ui.DrawPropEditor(null, vp, i * 0.016, 0);
                }
            });
            var probe = SteadyAllocBytes(() =>
            {
                for (var i = 0; i < 2000; i++)
                    LookAt.Probe(map, new Vector3(0, 1.6f, -5), Vector3.UnitZ, 100);
            });
            Line($"keys F6 / F5 / F10; closed windows: {closed} bytes over 20000 frames; 2000 looks: {probe} bytes");
            if (closed > 0 || probe > 0)
                Fail($"dev tools: {closed} bytes allocated with the windows closed, {probe} by looking (want 0)");
        }
        finally
        {
            s?.Revert();
            try
            {
                Directory.Delete(tmp, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
