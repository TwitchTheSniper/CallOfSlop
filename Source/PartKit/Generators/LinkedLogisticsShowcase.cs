using System.Text;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The linked logistics showcase (docs/sandbox/design/linked-logistics.md, phase 7): Constructs/Blueprints/LinkedWorks.json, a small works
/// (a Logistics Receiver, a Cargo Dock fed by chute from a Storage Volume, a Docking Clamp beside the dock, a Logistics Dispatcher, a Building Station set to Sub-jobs Linked); LinkedYard.json, a
/// yard (a Logistics Transmitter, a Cargo Dock that chutes into a Crafting Terminal, a Docking Clamp, a Building Station: pick Submodule Skiff there and its pods are built at the works); and LinkedHauler.json, the
/// quad with four Light Lift Fans (electric jets, lots of spare thrust), a Cargo Port under it, a Route Computer and a Storage Volume; both
/// factories have four Battery Packs, so their Docking Clamps charge it. Spawn both on Glacier, pair the Receiver with the
/// Transmitter's code, fill the works' Storage Volume with parts, order one of them at the yard's terminal: it waits for a ship; land
/// the hauler in the works' clamp (or give it a route: the command view, ROUTE...).
/// </summary>
public sealed class LinkedLogisticsShowcase : IPartGenerator
{
    public string Name => "linked_logistics_showcase";

    private const string Format = "call-of-slop-construct-blueprint/1";

    public void Make(PartOutput o)
    {
        o.File("Constructs/Blueprints/LinkedWorks.json", Factory("Linked Works", "A small works for linked logistics: fill the Storage Volume with parts; the Cargo Dock sends what a linked network orders. Pair its Receiver with a Transmitter's code (one planetoid), or its Relay with a Relay's (the star system).", "Logistics Receiver", false));
        o.File("Constructs/Blueprints/LinkedYard.json", Factory("Linked Yard", "A yard for linked logistics: order at the Crafting Terminal what the linked works holds; a ship brings it to the Cargo Dock, which chutes it to the terminal. Its Transmitter's code pairs a Receiver on this planetoid; its Relay's, a Relay on another world.", "Logistics Transmitter", true));
        o.File("Constructs/Blueprints/LinkedHauler.json", Hauler());
    }

    /// <summary>A row of seven Foundations along +z: the link end on the second, the dock on the fourth, the clamp on the last; a Storage Volume (works) or a Crafting Terminal (yard) on the first.</summary>
    private static string Factory(string name, string description, string end, bool yard)
    {
        var steps = new List<string> { "{ \"part\": \"Foundation\", \"paint\": \"#8c8576\" }" };
        for (var k = 1; k <= 6; k++)
            steps.Add($"{{ \"part\": \"Foundation\", \"on\": {k - 1}, \"socket\": \"z+0\", \"by\": \"z-0\", \"paint\": \"#8c8576\" }}");
        steps.Add("{ \"part\": \"Cargo Dock\", \"on\": 3, \"socket\": \"top11\", \"by\": \"-y\" }"); // 7
        steps.Add("{ \"part\": \"Docking Clamp\", \"on\": 6, \"socket\": \"top11\", \"by\": \"-y\", \"config\": { \"startOn\": true } }"); // 8
        steps.Add($"{{ \"part\": \"{end}\", \"on\": 1, \"socket\": \"top00\", \"by\": \"-y\" }}"); // 9
        steps.Add("{ \"part\": \"Foundation\", \"on\": 1, \"socket\": \"x-0\", \"by\": \"x+0\", \"paint\": \"#8c8576\" }"); // 10
        steps.Add("{ \"part\": \"Foundation\", \"on\": 10, \"socket\": \"x-0\", \"by\": \"x+0\", \"paint\": \"#8c8576\" }"); // 11
        steps.Add("{ \"part\": \"Logistics Relay\", \"on\": 11, \"socket\": \"top11\", \"by\": \"-y\" }"); // 12: across the star system
        steps.Add(yard
            ? "{ \"part\": \"Crafting Terminal\", \"on\": 0, \"socket\": \"top30\", \"by\": \"-y.0.0\" }" // 13
            : "{ \"part\": \"Storage Volume\", \"on\": 0, \"socket\": \"top30\", \"by\": \"-y\" }");
        steps.Add(yard
            ? "{ \"part\": \"Foundation\", \"on\": 2, \"socket\": \"x+0\", \"by\": \"x-0\", \"paint\": \"#8c8576\" }" // 14 (a spacer: the yard has no Dispatcher)
            : "{ \"part\": \"Logistics Dispatcher\", \"on\": 1, \"socket\": \"top30\", \"by\": \"-y\" }"); // 14: sends registered route ships
        // A Building Station off to the +x side (its bay beside it): at the yard a main station whose sub-blueprint uses no local station
        // takes; at the works one set to Sub-jobs Linked, which takes them over the link.
        steps.Add("{ \"part\": \"Foundation\", \"on\": 3, \"socket\": \"x+0\", \"by\": \"x-0\", \"paint\": \"#8c8576\" }"); // 15
        steps.Add("{ \"part\": \"Foundation\", \"on\": 15, \"socket\": \"x+0\", \"by\": \"x-0\", \"paint\": \"#8c8576\" }"); // 16
        steps.Add($"{{ \"part\": \"Building Station\", \"on\": 16, \"socket\": \"top00\", \"by\": \"-y.0.0\", \"config\": {{ \"bayWidth\": 4, \"bayDepth\": 3, \"bayHeight\": 3, \"subJobs\": {(yard ? 1 : 2)} }} }}"); // 17
        // Four Battery Packs (80 MJ) on the outer of two Foundations off the row's -x side by the Cargo Dock (6 m from the clamp: a landing hauler's feet clear them): the clamp charges a held ship from it (20 kW),
        // so an electric hauler comes back full enough for its next leg.
        steps.Add("{ \"part\": \"Foundation\", \"on\": 3, \"socket\": \"x-0\", \"by\": \"x+0\", \"paint\": \"#8c8576\" }"); // 18
        steps.Add("{ \"part\": \"Foundation\", \"on\": 18, \"socket\": \"x-0\", \"by\": \"x+0\", \"paint\": \"#8c8576\" }"); // 19
        foreach (var (on, socket) in new[] { (19, "top10"), (19, "top30"), (19, "top13"), (19, "top33") })
            steps.Add($"{{ \"part\": \"Battery Pack\", \"on\": {on}, \"socket\": \"{socket}\", \"by\": \"-y\" }}"); // 20 to 23 (half a metre apart: their trims don't meet)
        var links = yard
            ? "{ \"type\": \"chute\", \"from\": 7, \"fromPort\": \"Chute out\", \"to\": 13, \"toPort\": \"Chute in\" }"
            : "{ \"type\": \"chute\", \"from\": 13, \"fromPort\": \"Chute out\", \"to\": 7, \"toPort\": \"Chute in\" }";
        var sb = new StringBuilder();
        sb.Append("{\n  \"format\": \"").Append(Format).Append("\",\n  \"name\": \"").Append(name).Append("\",\n  \"group\": \"Production\",\n");
        sb.Append("  \"description\": \"").Append(description).Append("\",\n  \"steps\": [\n");
        for (var k = 0; k < steps.Count; k++)
            sb.Append("    ").Append(steps[k]).Append(k + 1 < steps.Count ? ",\n" : "\n");
        sb.Append("  ],\n  \"links\": [\n    ").Append(links).Append("\n  ]\n}\n");
        return sb.ToString();
    }

    /// <summary>The Fan Quad's frame with four Light Lift Fans on legs under its crossbeams' ends (they are its feet, below the port; 8 kN each: four times its weight, where four ducted fans
    /// were marginal and tipped it as a clamp let go), seat, stabilizer and battery, with a Cargo Port under its spine, a Route Computer and a Storage Volume on it.</summary>
    private static string Hauler() => $$"""
        {
          "format": "{{Format}}",
          "name": "Linked Hauler",
          "group": "Production",
          "description": "A small cargo quad for linked logistics on four electric lift fans: a Cargo Port under it, a Route Computer and a Storage Volume. Land it in a Cargo Dock's Docking Clamp to load, or set it a route in the command view.",
          "steps": [
            { "part": "Square Beam 2 m" },
            { "part": "Square Beam 2 m", "on": 0, "socket": "+y0", "by": "-y3", "spin": 1 },
            { "part": "Square Beam 2 m", "on": 0, "socket": "+y7", "by": "-y3", "spin": 1 },
            { "part": "Square Beam 1 m", "on": 1, "socket": "-y0", "by": "-z" },
            { "part": "Square Beam 1 m", "on": 1, "socket": "-y7", "by": "-z" },
            { "part": "Square Beam 1 m", "on": 2, "socket": "-y0", "by": "-z" },
            { "part": "Square Beam 1 m", "on": 2, "socket": "-y7", "by": "-z" },
            { "part": "Seat", "on": 0, "socket": "+y5", "by": "bottom" },
            { "part": "Flight Stabilizer", "on": 0, "socket": "+y3", "by": "-y", "config": { "keys": "Rotorcraft", "maxTilt": 15 } },
            { "part": "Battery Pack", "on": 0, "socket": "-y3", "by": "+y" },
            { "part": "Cargo Port", "on": 9, "socket": "-y", "by": "+y" },
            { "part": "Route Computer", "on": 10, "socket": "-y", "by": "-y" },
            { "part": "Storage Volume", "on": 0, "socket": "-y0", "by": "-y", "config": { "sizeX": 1, "sizeY": 1, "sizeZ": 1, "atY": -0.4 } },
            { "part": "Light Lift Fan", "on": 3, "socket": "+z", "by": "front" },
            { "part": "Light Lift Fan", "on": 4, "socket": "+z", "by": "front" },
            { "part": "Light Lift Fan", "on": 5, "socket": "+z", "by": "front" },
            { "part": "Light Lift Fan", "on": 6, "socket": "+z", "by": "front" }
          ],
          "links": [
            { "type": "data", "from": 7, "to": 8 }
          ]
        }

        """.Replace("\r\n", "\n");
}
