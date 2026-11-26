using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// The designer's one door: every edit goes through the <see cref="DesignEditor"/>, and every answer carries the scene to draw
/// (<see cref="DesignScene.Json(ShipDesign)"/>). The web page (tools/ship-designer, through its JS exports) and the game's own
/// editor screen (UI/UiShipDesigner*.cs) both talk to it, so the two run the same commands and get the same answers.
/// </summary>
public sealed class DesignSession
{
    private DesignEditor _ed;

    public DesignSession(string start = "Zero Day") => _ed = new DesignEditor(DesignTemplates.Start(start));

    public ShipDesign Design => _ed.Design;

    private string Answer(string error, int id = 0) =>
        new StringBuilder("{\"error\":").Append(DesignScene.Q(error)).Append(",\"id\":").Append(id)
            .Append(",\"undo\":").Append(_ed.CanUndo ? "true" : "false").Append(",\"redo\":").Append(_ed.CanRedo ? "true" : "false")
            .Append(",\"scene\":").Append(DesignScene.Json(_ed.Design)).Append('}').ToString();

    public string Scene() => Answer("");

    /// <summary>
    /// Runs one command (its "op" and fields, as JSON). "preview" leaves the holes uncut, "light" leaves the hull meshes out of the
    /// answer (the last ones are drawn moved); "merge" folds it into the last undo step.
    /// </summary>
    public string Apply(string command)
    {
        var o = JsonNode.Parse(command)!.AsObject();
        if ((string?)o["op"] == "view")
        {
            // Room view in or out (no edit, no undo step): {op:"view", roomView:{room, connected}} or roomView null.
            DesignScene.RoomView = o["roomView"] is JsonObject rv ? ((int?)rv["room"] ?? 0, (bool?)rv["connected"] ?? false) : null;
            return Answer("");
        }
        var err = _ed.Apply(o.ToJsonString());
        DesignScene.Holes = !((bool?)o["preview"] ?? false);
        DesignScene.Light = (bool?)o["light"] ?? false;
        // A new piece or room's id, so the page can select it.
        var id = 0;
        var op = (string?)o["op"];
        if (err.Length == 0 && op == "connect")
            id = _ed.Design.Joins.Count == 0 ? 0 : _ed.Design.Joins[^1].Id;
        else if (err.Length == 0 && op == "addHullWindow")
            id = _ed.Design.Windows.Count == 0 ? 0 : _ed.Design.Windows[^1].Id;
        else if (err.Length == 0 && op == "addDoor")
            id = _ed.Design.Doors.Count == 0 ? 0 : _ed.Design.Doors.Max(k => k.Id);
        else if (err.Length == 0 && op == "addPassage")
            id = _ed.Design.Passages.Count == 0 ? 0 : _ed.Design.Passages[^1].Id;
        else if (err.Length == 0 && op == "addRiser")
            id = _ed.Design.Risers.Count == 0 ? 0 : _ed.Design.Risers[^1].Id;
        else if (err.Length == 0 && op is "addPiece" or "addRoom" or "pastePiece" or "pasteRoom" or "splitRoom" or "mount")
            id = Math.Max(_ed.Design.Pieces.Count == 0 ? 0 : _ed.Design.Pieces.Max(p => p.Id), _ed.Design.Rooms.Count == 0 ? 0 : _ed.Design.Rooms.Max(r => r.Id));
        try { return Answer(err, id); }
        finally { DesignScene.Holes = true; DesignScene.Light = false; }
    }

    public string Undo() { _ed.Undo(); return Answer(""); }

    public string Redo() { _ed.Redo(); return Answer(""); }

    /// <summary>The design as its file's JSON.</summary>
    public string Save() => _ed.Design.ToJson();

    /// <summary>What can be placed: pieces, room kinds, section shapes, bodies and starts.</summary>
    public static string Catalogue()
    {
        // The catalogue plus each kind's defaults, read off fresh objects, so the page's reset buttons follow the model's initialisers.
        var c = JsonNode.Parse(Basic())!.AsObject();
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
                ["cladSize"] = j.CladSize, ["cladDepth"] = j.CladDepth, ["buttressSize"] = j.ButtressSize, ["buttressDepth"] = j.ButtressDepth, ["matchClad"] = j.MatchClad, ["matchButtress"] = j.MatchButtress, ["matchColour"] = j.MatchColour,
            },
            ["hull"] = new JsonObject
            {
                ["cladSize"] = h.CladSize, ["cladDepth"] = h.CladDepth, ["roundFore"] = h.RoundFore, ["roundAft"] = h.RoundAft,
                ["accentAmount"] = h.AccentAmount, ["stripe"] = h.Stripe, ["wear"] = h.Wear,
                ["material"] = h.Material, ["cladMaterial"] = h.CladMaterial, ["damage"] = h.Damage, ["damageSeed"] = h.DamageSeed, ["cladFrom"] = null,
            },
            ["room"] = new JsonObject { ["wallThick"] = r.WallThick, ["ribSpacing"] = r.RibSpacing, ["wallStyle"] = r.WallStyle, ["wallColour"] = r.WallColour, ["seed"] = r.Seed, ["floor"] = r.Floor },
            ["piece"] = new JsonObject { ["gap"] = -1, ["hole"] = 1.0, ["depth"] = 0.2, ["double"] = true },
            ["hullWindow"] = new JsonObject { ["gap"] = -1, ["hole"] = 1.0, ["depth"] = 0.2, ["double"] = true },
            // A corridor's own defaults (Room.KindDefaults), over the room's.
            ["corridor"] = new JsonObject { ["wallStyle"] = "Corridor", ["floor"] = "Grating" },
        };
        return c.ToJsonString();
    }

    private static string Basic()
    {
        var sb = new StringBuilder("{\"pieces\":[");
        sb.AppendJoin(',', PieceKinds.All.Select(k => $"{{\"name\":{DesignScene.Q(k.Name)},\"group\":{DesignScene.Q(k.Group)},\"where\":{DesignScene.Q(k.Where)},\"socket\":{(k.Socket is null ? "null" : DesignScene.Q(k.Socket))},\"needs\":{DesignScene.Q(k.Needs)},\"desc\":{DesignScene.Q(k.Desc)}}}"));
        sb.Append("],\"rooms\":[");
        sb.AppendJoin(',', RoomKinds.All.Select(k => $"{{\"name\":{DesignScene.Q(k.Name)},\"colour\":{DesignScene.Q(k.Colour)},\"desc\":{DesignScene.Q(k.Desc)}}}"));
        sb.Append("],\"sections\":[");
        sb.AppendJoin(',', DesignTemplates.Sections.Select(s => $"{{\"name\":{DesignScene.Q(s.Name)},\"p\":[{string.Join(',', s.P.Select(q => $"[{ShipDesign.S(q.H)},{ShipDesign.S(q.Y)}]"))}]}}"));
        sb.Append("],\"bodies\":[").AppendJoin(',', DesignTemplates.Bodies.Select(DesignScene.Q));
        sb.Append("],\"starts\":[").AppendJoin(',', DesignTemplates.Starts.Select(DesignScene.Q));
        return sb.Append("]}").ToString();
    }
}
