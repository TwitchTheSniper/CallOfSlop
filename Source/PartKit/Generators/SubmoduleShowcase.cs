using System.Globalization;
using System.Text;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The sub-blueprints showcase (docs/sandbox/design/sub-blueprints.md, phase 6): Constructs/Blueprints/SubmodulePod.json, a small
/// gun pod; SubmoduleSkiff.json, a skiff whose two pods are uses of it (<c>ship:Submodule Pod</c>, one turned about); and
/// SubmoduleYard.json, two Building Stations on one foundation whose bays don't touch, a Loader Arm between them that reaches both.
/// Pick Submodule Skiff at the yard's first station: a pod goes to the second as a sub-job, the arm brings it back, the big fit welds it.
/// The yard's numbers come from the parts' files: the station's bay sits beside its +x side (ConstructStation.Bay), the arm's
/// shoulder over its base (LoaderArm.json "lifter.base"), its reach 1 to 4 m round it.
/// </summary>
public sealed class SubmoduleShowcase : IPartGenerator
{
    public string Name => "submodule_showcase";

    private const string Format = "call-of-slop-construct-blueprint/1";
    private const string Beam = "Square Beam 0.25 m";

    public void Make(PartOutput o)
    {
        o.File("Constructs/Blueprints/SubmodulePod.json", Pod());
        o.File("Constructs/Blueprints/SubmoduleSkiff.json", Skiff());
        o.File("Constructs/Blueprints/SubmoduleYard.json", Yard());
    }

    private static string Pod() => $$"""
        {
          "format": "{{Format}}",
          "name": "Submodule Pod",
          "group": "Submodules",
          "description": "A small gun pod: the Submodule Skiff uses it twice (sub-blueprints).",
          "steps": [
            { "part": "{{Beam}}" },
            { "part": "{{Beam}}", "on": 0, "socket": "+y", "by": "-y", "paint": "#5a6470" },
            { "part": "{{Beam}}", "on": 1, "socket": "+x", "by": "-x", "paint": "#2f343a" }
          ]
        }

        """.Replace("\r\n", "\n");

    /// <summary>Four beams in a row, a pod on each end; the far one turned about (Rot24 turn 2: half a turn round +y).</summary>
    private static string Skiff() => $$"""
        {
          "format": "{{Format}}",
          "name": "Submodule Skiff",
          "group": "Submodules",
          "description": "A skiff with two Submodule Pods as sub-blueprint uses: at a Building Station each pod is a sub-job for another station on the same construct.",
          "steps": [
            { "part": "{{Beam}}", "paint": "#8a6a3a" },
            { "part": "{{Beam}}", "on": 0, "socket": "+x", "by": "-x", "paint": "#8a6a3a" },
            { "part": "{{Beam}}", "on": 1, "socket": "+x", "by": "-x", "paint": "#8a6a3a" },
            { "part": "{{Beam}}", "on": 2, "socket": "+x", "by": "-x", "paint": "#8a6a3a" }
          ],
          "uses": [
            { "id": "ship:Submodule Pod", "name": "Submodule Pod", "version": 0, "turn": 0, "at": [0, 0.25, 0], "after": 3, "on": [[0, "+y", 0, "-y"]] },
            { "id": "ship:Submodule Pod", "name": "Submodule Pod", "version": 0, "turn": 2, "at": [0.75, 0.25, 0], "after": 3, "on": [[3, "+y", 0, "-y"]] }
          ]
        }

        """.Replace("\r\n", "\n");

    /// <summary>
    /// Foundations along +z (2 m each), the first station on the first, the second three foundations on (its bay 3 m past the first's:
    /// they don't touch), and a Loader Arm on a foundation in the gap between the bays, two foundations out along +x, its shoulder
    /// about 3 m from each bay's middle.
    /// </summary>
    private static string Yard()
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"format\": \"").Append(Format).Append("\",\n  \"name\": \"Submodule Yard\",\n  \"group\": \"Submodules\",\n");
        sb.Append("  \"description\": \"Two Building Stations whose bays don't touch and a Loader Arm that reaches both: pick Submodule Skiff at the first station; a pod is built at the second and the arm brings it.\",\n");
        sb.Append("  \"steps\": [\n");
        var steps = new List<string> { "{ \"part\": \"Foundation\" }" };
        for (var k = 1; k <= 3; k++)
            steps.Add($"{{ \"part\": \"Foundation\", \"on\": {k - 1}, \"socket\": \"z+0\", \"by\": \"z-0\" }}");
        // Out along +x from the third foundation (at z 4 m): two more, the arm on the last.
        steps.Add("{ \"part\": \"Foundation\", \"on\": 2, \"socket\": \"x+0\", \"by\": \"x-0\" }"); // 4: (2, 0, 4)
        steps.Add("{ \"part\": \"Foundation\", \"on\": 4, \"socket\": \"x+0\", \"by\": \"x-0\" }"); // 5: (4, 0, 4)
        steps.Add("{ \"part\": \"Building Station\", \"on\": 0, \"socket\": \"top01\", \"by\": \"-y.0.0\" }"); // 6: the main station
        steps.Add("{ \"part\": \"Building Station\", \"on\": 3, \"socket\": \"top01\", \"by\": \"-y.0.0\" }"); // 7: the sub-station
        // Each station's bay: two Bay Blocks out of its dock pad's mouth, 2 x 4 m (its rover parks on the Core's pad).
        steps.Add("{ \"part\": \"Bay Block\", \"on\": 6, \"socket\": \"+x.0\", \"by\": \"-x\" }"); // 8
        steps.Add("{ \"part\": \"Bay Block\", \"on\": 7, \"socket\": \"+x.0\", \"by\": \"-x\" }"); // 9
        steps.Add("{ \"part\": \"Bay Block\", \"on\": 8, \"socket\": \"+x\", \"by\": \"-x\" }"); // 10
        steps.Add("{ \"part\": \"Bay Block\", \"on\": 9, \"socket\": \"+z\", \"by\": \"-z\" }"); // 11 (beside, clear of the arm)
        // The arm: a Kit Loader Arm (4 m, 500 kg: what the fixed Loader Arm was, ArmKitMigrate), its Core's middle on the foundation's
        // top23, 0.18 m from where the fixed arm's shoulder stood.
        var first = steps.Count;
        foreach (var (part, on, socket, by) in ArmKitParts.PresetSteps("Kit Loader Arm"))
            steps.Add(on < 0
                ? $"{{ \"part\": \"{part}\", \"on\": 5, \"socket\": \"top23\", \"by\": \"bottom\" }}"
                : $"{{ \"part\": \"{part}\", \"on\": {first + on}, \"socket\": \"{socket}\", \"by\": \"{by}\" }}");
        for (var k = 0; k < steps.Count; k++)
            sb.Append("    ").Append(steps[k]).Append(k + 1 < steps.Count ? ",\n" : "\n");
        sb.Append("  ]\n}\n");
        return sb.ToString();
    }
}
