using System.Runtime.InteropServices.JavaScript;
using System.Text.Json.Nodes;

namespace CallOfSlop.ShipDesigner;

/// <summary>What the page calls: the game's own <see cref="DesignSession"/>, which the in-game editor uses too.</summary>
public static partial class Api
{
    private static readonly DesignSession _s = new();

    public static void Main() { }

    [JSExport]
    public static string Scene() => _s.Scene();

    [JSExport]
    public static string Apply(string command) => _s.Apply(command);

    [JSExport]
    public static string Undo() => _s.Undo();

    [JSExport]
    public static string Redo() => _s.Redo();

    [JSExport]
    public static string Save() => _s.Save();

    [JSExport]
    public static string Catalogue()
    {
        // The catalogue plus each kind's defaults, read off fresh objects, so the page's reset buttons follow the model's initialisers.
        var c = JsonNode.Parse(DesignSession.Catalogue())!.AsObject();
        var j = new HullJoin();
        var h = new HullBody();
        var r = new Room();
        c["defaults"] = new JsonObject
        {
            ["join"] = new JsonObject
            {
                ["w"] = j.WA, ["h"] = j.HA, ["wb"] = j.WB, ["hb"] = j.HB, ["twist"] = j.Twist, ["rotA"] = j.RotA, ["rotB"] = j.RotB, ["sweepUp"] = j.SweepUp, ["sweepFore"] = j.SweepFore,
                ["bias"] = j.Bias, ["segments"] = j.Segments, ["flare"] = j.Flare, ["buttress"] = j.Buttress, ["openA"] = j.OpenA, ["openB"] = j.OpenB,
                ["leaveA"] = j.LeaveA, ["leaveB"] = j.LeaveB, ["leaveForeA"] = j.LeaveForeA, ["leaveForeB"] = j.LeaveForeB, ["tanA"] = j.TanA, ["tanB"] = j.TanB,
                ["collar"] = j.Collar, ["collarHeight"] = j.CollarHeight, ["collarMargin"] = j.CollarMargin, ["collarBevel"] = j.CollarBevel,
                ["cladSize"] = j.CladSize, ["cladDepth"] = j.CladDepth, ["buttressSize"] = j.ButtressSize, ["buttressDepth"] = j.ButtressDepth, ["matchClad"] = j.MatchClad, ["matchButtress"] = j.MatchButtress,
            },
            ["hull"] = new JsonObject
            {
                ["cladSize"] = h.CladSize, ["cladDepth"] = h.CladDepth, ["roundFore"] = h.RoundFore, ["roundAft"] = h.RoundAft,
                ["accentAmount"] = h.AccentAmount, ["stripe"] = h.Stripe, ["wear"] = h.Wear,
                ["material"] = h.Material, ["cladMaterial"] = h.CladMaterial, ["damage"] = h.Damage, ["damageSeed"] = h.DamageSeed, ["cladFrom"] = null,
            },
            ["room"] = new JsonObject { ["wallThick"] = r.WallThick, ["ribSpacing"] = r.RibSpacing, ["wallStyle"] = r.WallStyle, ["wallColour"] = r.WallColour, ["seed"] = r.Seed, ["floor"] = r.Floor },
        };
        return c.ToJsonString();
    }
}
