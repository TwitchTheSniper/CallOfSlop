using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>One button on a panel: a part file's <c>panels[n].touch</c> entry.</summary>
public sealed class PanelTouchDef
{
    public string Action = "";
    public PanelAction? Run;
    public Vector4 At; // x0, y0, x1, y1: shares of the panel, from its bottom left
    public string Label = "";
}

/// <summary>What a button does, on the server. Null, or why not. It changes the part, and the caller sends the body on.</summary>
public delegate string? PanelAction(ConstructWorld world, Construct c, int part, byte player, uint tick);

/// <summary>
/// Touch regions on display panels (docs/sandbox/CONSTRUCTS.md, "Touch panels"): with Hands out and empty, the crosshair is a finger on a panel. A panel lists its buttons as data, each
/// a rectangle, a label and an action named in <see cref="Actions"/>; <see cref="PanelRender"/> draws them and lights the one aimed at, and a click goes to the server as
/// <see cref="CActKind.PanelTouch"/> under the configure tool's rule. A panel source can change a label as it fills its card (<see cref="PanelCard.TouchLabels"/>).
/// To add a button: one entry in the part's "touch" list, naming an action here (a new one is one line in this table).
/// </summary>
public static class PanelTouch
{
    public const int MaxRegions = 8;

    private static readonly Dictionary<string, PanelAction> Actions = new(StringComparer.Ordinal)
    {
        ["media.previous"] = (w, c, part, player, tick) => ConstructMedia.Touch(c, part, MediaOp.Previous, player, tick),
        ["media.next"] = (w, c, part, player, tick) => ConstructMedia.Touch(c, part, MediaOp.Next, player, tick),
        ["media.toggle"] = (w, c, part, player, tick) =>
            ConstructMedia.Touch(c, part, c.Parts[part].Media is { State: MediaState.Playing } ? MediaOp.Pause : MediaOp.Resume, player, tick),
        ["flight.off"] = (w, c, part, player, tick) => ConstructFlight.Touch(w, c, "off"),
        ["flight.on"] = (w, c, part, player, tick) => ConstructFlight.Touch(w, c, "on"),
        ["flight.mode"] = (w, c, part, player, tick) => ConstructFlight.Touch(w, c, "mode"),
        ["flight.home"] = (w, c, part, player, tick) => ConstructFlight.Touch(w, c, "home"),
        ["flight.level"] = (w, c, part, player, tick) => ConstructFlight.Touch(w, c, "level"),
        ["flight.strength"] = (w, c, part, player, tick) => ConstructFlight.Touch(w, c, "strength"),
        ["flight.altitude"] = (w, c, part, player, tick) => ConstructFlight.Touch(w, c, "altitude"),
        ["flight.heading"] = (w, c, part, player, tick) => ConstructFlight.Touch(w, c, "heading"),
        ["office.advert"] = (w, c, part, player, tick) => OfficeParts.ClickAdvert(c, part, 0),
        ["line.run"] = (w, c, part, player, tick) => ProductionLines.Run(c, part),
        ["line.next"] = (w, c, part, player, tick) => ProductionLines.Next(c, part),
        ["line.recipe"] = (w, c, part, player, tick) => ProductionLines.Recipe(c, part),
        ["office.advert2"] = (w, c, part, player, tick) => OfficeParts.ClickAdvert(c, part, 1),
    };

    /// <summary>A panel's "touch" list, and its "picture" rectangle.</summary>
    public static void Parse(PanelDef p, JsonElement e, string file, string where)
    {
        if (ConstructParts.Opt(e, "picture") is { } picture)
            p.Picture = Rect(picture, file, where + ".picture");
        if (ConstructParts.Opt(e, "touch") is not { } list)
            return;
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > MaxRegions)
            throw new ConstructPartException($"{file}: {where}.touch lists 1 to {MaxRegions} buttons ({{action, at, label}})");
        var made = new List<PanelTouchDef>();
        foreach (var b in list.EnumerateArray())
        {
            ConstructParts.Known(b, ["action", "at", "label"], $"{file}, {where}.touch");
            var action = ConstructParts.Str(ConstructParts.Need(b, "action", $"{file}, {where}.touch"), file, where + ".touch.action");
            made.Add(new PanelTouchDef
            {
                Action = action,
                Run = Actions.GetValueOrDefault(action) ?? throw new ConstructPartException($"{file}: {where}.touch: no action \"{action}\" ({string.Join(", ", Actions.Keys)})"),
                At = Rect(ConstructParts.Need(b, "at", $"{file}, {where}.touch"), file, where + ".touch.at"),
                Label = ConstructParts.Opt(b, "label") is { } l ? ConstructParts.Str(l, file, where + ".touch.label") : "",
            });
        }
        p.Touch = made.ToArray();
    }

    private static Vector4 Rect(JsonElement e, string file, string what)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 4)
            throw new ConstructPartException($"{file}: {what} is [x0, y0, x1, y1], shares of the panel from its bottom left");
        var r = new Vector4(ConstructParts.Num(e[0], file, what), ConstructParts.Num(e[1], file, what), ConstructParts.Num(e[2], file, what), ConstructParts.Num(e[3], file, what));
        if (r.X < 0 || r.Y < 0 || r.Z > 1 || r.W > 1 || r.X >= r.Z || r.Y >= r.W)
            throw new ConstructPartException($"{file}: {what} is a rectangle inside the panel");
        return r;
    }

    /// <summary>Where a panel is now, in the world.</summary>
    public static void Frame(Construct c, int part, PanelDef panel, out Vector3 centre, out Vector3 normal, out Vector3 up)
    {
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out var t);
        centre = t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, panel.At), q);
        normal = Vector3.Transform(ConstructParts.DirVector(Rot24.ApplyDir(p.Rot, panel.Face)), q);
        up = Vector3.Transform(ConstructParts.DirVector(Rot24.ApplyDir(p.Rot, panel.Up)), q);
    }

    /// <summary>A ray on a panel's plane: metres from its centre, x to the viewer's right and y up. False: it runs along or away from the panel.</summary>
    public static bool Project(Construct c, int part, PanelDef panel, Vector3 eye, Vector3 dir, out Vector2 at)
    {
        Frame(c, part, panel, out var centre, out var normal, out var up);
        at = default;
        var along = Vector3.Dot(dir, normal);
        if (along >= -1e-4f)
            return false;
        var hit = eye + dir * (Vector3.Dot(centre - eye, normal) / along);
        var right = Vector3.Cross(up, normal);
        at = new Vector2(Vector3.Dot(hit - centre, right), Vector3.Dot(hit - centre, up));
        return true;
    }

    /// <summary>The first panel on a part that a ray lands on.</summary>
    public static int Find(Construct c, int part, Vector3 eye, Vector3 dir, out Vector2 at)
    {
        var d = c.DefOf(part);
        at = default;
        for (var n = 0; n < d.Panels.Length; n++)
            if (Project(c, part, d.Panels[n], eye, dir, out at) && MathF.Abs(at.X) <= d.Panels[n].Size.X * 0.5f && MathF.Abs(at.Y) <= d.Panels[n].Size.Y * 0.5f)
                return n;
        return -1;
    }

    /// <summary>The button at a place on a panel, or -1.</summary>
    public static int Region(PanelDef panel, Vector2 at)
    {
        var x = at.X / panel.Size.X + 0.5f;
        var y = at.Y / panel.Size.Y + 0.5f;
        for (var i = 0; i < panel.Touch.Length; i++)
        {
            var r = panel.Touch[i].At;
            if (x >= r.X && x <= r.Z && y >= r.Y && y <= r.W)
                return i;
        }
        return -1;
    }
}

public sealed partial class ConstructEdits
{
    /// <summary>Server: a player's finger on a panel's button, under the configure tool's rule (<see cref="PartText"/>).</summary>
    public string? PanelTouched(ConstructWorld world, byte player, Vector3 eye, ushort body, ushort part, int panel, int region, uint tick, string? refusal)
    {
        if (world.Get(body) is not { } c || part >= c.PartCount)
            return "that part is gone";
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (panel >= d.Panels.Length || region >= d.Panels[panel].Touch.Length)
            return "there is no button there";
        if (Contested(c, player) is { } defended)
            return defended;
        if (refusal != null)
            return refusal;
        if (Vector3.Distance(eye, ReachPoint(c, part, eye)) > ConstructConfig.Reach)
            return "too far away";
        if (p.Broken || !ConstructPower.On(c, part))
            return $"the {d.Name} has no power";
        if (d.Panels[panel].Touch[region].Run!(world, c, part, player, tick) is { } why)
            return why;
        world.StructureDirty.Add(c.Id);
        return null;
    }
}
