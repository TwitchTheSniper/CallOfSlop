using System.Runtime.InteropServices.JavaScript;
using System.Text;

namespace CallOfSlop.ShipDesigner;

/// <summary>What the page calls: every edit goes through the game's DesignEditor, and every answer carries the scene to draw.</summary>
public static partial class Api
{
    private static DesignEditor _ed = new(DesignTemplates.Start("Zero Day"));

    public static void Main() { }

    private static string Answer(string error, int id = 0) =>
        new StringBuilder("{\"error\":").Append(DesignScene.Q(error)).Append(",\"id\":").Append(id)
            .Append(",\"undo\":").Append(_ed.CanUndo ? "true" : "false").Append(",\"redo\":").Append(_ed.CanRedo ? "true" : "false")
            .Append(",\"scene\":").Append(DesignScene.Json(_ed.Design)).Append('}').ToString();

    [JSExport]
    public static string Scene() => Answer("");

    [JSExport]
    public static string Apply(string command)
    {
        var o = System.Text.Json.Nodes.JsonNode.Parse(command)!.AsObject();
        var err = _ed.Apply(o.ToJsonString());
        DesignScene.Holes = !((bool?)o["preview"] ?? false);
        DesignScene.Light = (bool?)o["light"] ?? false;
        // A new piece or room's id, so the page can select it.
        var id = 0;
        if (err.Length == 0 && (string?)o["op"] == "connect")
            id = _ed.Design.Joins.Count == 0 ? 0 : _ed.Design.Joins[^1].Id;
        else if (err.Length == 0 && (string?)o["op"] == "addHullWindow")
            id = _ed.Design.Windows.Count == 0 ? 0 : _ed.Design.Windows[^1].Id;
        else if (err.Length == 0 && (string?)o["op"] == "addDoor")
            id = _ed.Design.Doors.Count == 0 ? 0 : _ed.Design.Doors.Max(k => k.Id);
        else if (err.Length == 0 && (string?)o["op"] == "addRiser")
            id = _ed.Design.Risers.Count == 0 ? 0 : _ed.Design.Risers[^1].Id;
        else if (err.Length == 0 && ((string?)o["op"] is "addPiece" or "addRoom" or "pastePiece" or "pasteRoom" or "splitRoom" or "mount"))
            id = Math.Max(_ed.Design.Pieces.Count == 0 ? 0 : _ed.Design.Pieces.Max(p => p.Id), _ed.Design.Rooms.Count == 0 ? 0 : _ed.Design.Rooms.Max(r => r.Id));
        try { return Answer(err, id); }
        finally { DesignScene.Holes = true; DesignScene.Light = false; }
    }

    [JSExport]
    public static string Undo() { _ed.Undo(); return Answer(""); }

    [JSExport]
    public static string Redo() { _ed.Redo(); return Answer(""); }

    [JSExport]
    public static string Save() => _ed.Design.ToJson();

    [JSExport]
    public static string Catalogue()
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
