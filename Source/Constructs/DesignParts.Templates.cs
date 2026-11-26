namespace CallOfSlop;

/// <summary>
/// Design parts, the templates (DesignParts.cs has the map of the files): the part files a design element's def starts from, each parsed
/// once through <see cref="ConstructParts.Parse"/> (so its role, settings and config slots are the parser's) and then
/// <see cref="ConstructPartDef.ShallowCopy"/>'d by the kind that uses it (<see cref="KindTable.Fn"/>). A new working kind adds its JSON here.
/// </summary>
public static partial class DesignParts
{
    private static ConstructPartDef? _template, _thrustTemplate, _seatTemplate;

    /// <summary>Plain structure's (a band, a join, a piece that does nothing, a floor).</summary>
    private static ConstructPartDef Template => _template ??= ConstructParts.Parse(TemplateJson, "a design part's template");

    private static ConstructPartDef ThrustTemplate => _thrustTemplate ??= ConstructParts.Parse(ThrustJson, "a design thruster's template");

    private static ConstructPartDef SeatTemplate => _seatTemplate ??= ConstructParts.Parse(SeatJson, "a design seat's template");

    private const string ThrustJson = """
        {"format":"call-of-slop-construct-part/1","name":"Design thruster","description":"A drive or an RCS nozzle of a ship from the Ship Designer.","group":"Production","section":"Designs",
         "role":"thruster","tool":"welder","mass":10,"health":100,"material":"steel","color":"#8a8f96","attachSeconds":4,"stage":true,"centreSockets":false,
         "thruster":{"force":1000,"fuel":"electric","power":1,"exhaust":"-y"},
         "boxes":[{"min":[-0.5,-0.01,-0.5],"max":[0.5,0.01,0.5]}],
         "sockets":[{"name":"back","at":[0,-0.01,0],"dir":"-y"}]}
        """;

    private const string SeatJson = """
        {"format":"call-of-slop-construct-part/1","name":"Design seat","description":"A seat of a ship from the Ship Designer.","group":"Production","section":"Designs",
         "role":"seat","tool":"welder","mass":10,"health":100,"material":"steel","color":"#8a8f96","attachSeconds":4,"stage":true,"centreSockets":false,
         "seat":{"at":[0,0,0]},
         "boxes":[{"min":[-0.5,-0.01,-0.5],"max":[0.5,0.01,0.5]}],
         "sockets":[{"name":"back","at":[0,-0.01,0],"dir":"-y"}]}
        """;

    private const string TemplateJson = """
        {"format":"call-of-slop-construct-part/1","name":"Design part","description":"A part of a ship from the Ship Designer.","group":"Production","section":"Designs",
         "role":"frame","tool":"welder","mass":10,"health":100,"material":"steel","color":"#8a8f96","attachSeconds":4,"stage":true,"centreSockets":false,
         "boxes":[{"min":[-0.5,-0.01,-0.5],"max":[0.5,0.01,0.5]}],
         "sockets":[{"name":"back","at":[0,-0.01,0],"dir":"-y"}]}
        """;
}
