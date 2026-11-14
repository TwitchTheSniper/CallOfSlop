using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;

namespace CallOfSlop;

/// <summary>
/// The Pane (docs/sandbox/design/laser-cutter.md section 4.5, phase 6): glass a Laser Cutter cuts to an opening's own outline, its key the
/// outline and its thickness, <c>Cut Pane (rA,B,C tT glass)</c>: <c>A</c> cm along (the panel's z), <c>B</c> across (its x), each corner cut back
/// by <c>C</c>, <c>T</c> mm thick. It is flat, one convex piece, drawn 7 mm short of the outline all round (PaneGap) so its rim never meets the cut's; its
/// blank is the smallest formed glass sheet that holds it (<see cref="PaneBlank"/>), the rest coming back as offcut. It welds by one socket,
/// <c>back</c>, in the middle of its back face, to an opening's <c>o</c> socket. A cut part like the others (<see cref="IsCutName"/>): an
/// order, a bill line and a rack's kind by its key; made at once (one box or two), never sliced and never a stand-in off the wire.
/// </summary>
public static partial class HullLoft
{
    public const string CutPanePrefix = "Cut Pane (", PaneMaterial = "glass";
    /// <summary>
    /// How far short of the opening's outline a pane is drawn and collides, all round (m): an opening's socket is on the whole centimetre
    /// (<see cref="AddOpeningSockets"/>), up to 5 mm from the opening's true middle (a 105 cm sheet's middle is on a half centimetre, a crowned
    /// sheet's surface anywhere), so 5 mm of that and 2 mm clear of the rim (the merge review, 2026-11-13: 2 mm let a pane's rim cut into the edge).
    /// </summary>
    public const float PaneGap = 0.007f;
    private const string PaneTint = "#9fc6d6"; // (the shipped Glass Panes' and the cab panes' tint)

    /// <summary>A pane's outline (cm) and thickness (mm).</summary>
    public readonly record struct PaneSize(int A, int B, int C, int Mm);

    public static bool IsPaneName(string name) => name.StartsWith(CutPanePrefix, StringComparison.Ordinal);

    public static string PaneKey(in PaneSize p) => string.Create(CultureInfo.InvariantCulture, $"{CutPanePrefix}r{p.A},{p.B},{p.C} t{p.Mm} {PaneMaterial})");

    /// <summary>
    /// The pane for an opening of a cut panel <paramref name="mm"/> thick (its own thickness, kept within a pane's) on a sheet <paramref name="l"/>
    /// by <paramref name="w"/> cm, or null when <see cref="PaneWhy"/> says no: never a pane for an opening with no socket, or one it would overhang.
    /// </summary>
    public static string? PaneFor(in Cut c, int mm, int l, int w) =>
        PaneWhy(c, l, w) != null ? null : PaneKey(new PaneSize(c.A, c.B, c.C, Math.Clamp(mm, PanelMinMm, PanelMaxMm)));

    /// <summary>Why an opening takes no pane, or null: a pane fits only an opening of 10 cm or more both ways lying wholly inside the sheet (an opening through an edge is a notch: its socket may be gone, and a pane would overhang).</summary>
    public static string? PaneWhy(in Cut c, int l, int w) =>
        c.Corner ? "a cut corner takes no pane"
        : c.A < CutOpenCm || c.B < CutOpenCm ? $"an opening under {CutOpenCm} cm a side has no socket for a pane"
        : c.U * 2 - c.A <= 0 || c.U * 2 + c.A >= l * 2 || c.V * 2 - c.B <= 0 || c.V * 2 + c.B >= w * 2 ? "this opening runs through the sheet's edge: a pane fits only an opening wholly inside the sheet"
        : null;

    /// <summary>A pane key read and checked; null with <paramref name="why"/> (never clamped: every peer makes the same pane or none).</summary>
    public static PaneSize? PaneOf(string name, out string why)
    {
        why = "not a pane";
        if (!IsPaneName(name) || !name.EndsWith(')') || name.Length > 64)
            return null;
        var w = name[CutPanePrefix.Length..^1].Split(' ');
        if (w.Length != 3 || w[0].Length < 2 || w[0][0] != 'r' || w[1].Length < 2 || w[1][0] != 't')
            return null;
        if (w[2] != PaneMaterial)
        {
            why = "a pane is cut from glass";
            return null;
        }
        var nums = w[0][1..].Split(',');
        if (nums.Length != 3 || !int.TryParse(nums[0], NumberStyles.None, CultureInfo.InvariantCulture, out var a) || !int.TryParse(nums[1], NumberStyles.None, CultureInfo.InvariantCulture, out var b)
            || !int.TryParse(nums[2], NumberStyles.None, CultureInfo.InvariantCulture, out var c) || !int.TryParse(w[1][1..], NumberStyles.None, CultureInfo.InvariantCulture, out var mm))
            return null;
        if (a < CutOpenCm || b < CutOpenCm || a > PanelMaxCm || b > PanelMaxCm)
        {
            why = $"a pane is {CutOpenCm} to {PanelMaxCm} cm a side";
            return null;
        }
        if (c * 2 > Math.Min(a, b))
        {
            why = "a pane's corners are cut back by at most half its smaller side";
            return null;
        }
        if (mm < PanelMinMm || mm > PanelMaxMm)
        {
            why = $"a pane is {PanelMinMm} to {PanelMaxMm} mm thick";
            return null;
        }
        var p = new PaneSize(a, b, c, mm);
        if (PaneKey(p) != name)
            return null; // (one spelling a key)
        why = "";
        return p;
    }

    /// <summary>The glass sheet a pane is cut from: a flat formed panel, its sides the outline's rounded up to 5 cm and at least 20.</summary>
    public static string PaneBlank(in PaneSize p)
    {
        static int Up(int cm) => Math.Max(PanelMinCm, (cm + PanelStepCm - 1) / PanelStepCm * PanelStepCm);
        return PanelKey(new PanelShape(0, Up(p.A), Up(p.B), 0, 0, p.Mm, PaneMaterial));
    }

    /// <summary>A pane's outline in its own frame (x across, z along, metres), 2 mm short of the opening's all round: four to eight corners, in order.</summary>
    private static Vector2[] PaneOutline(in PaneSize p)
    {
        float hx = p.B / 200f - PaneGap, hz = p.A / 200f - PaneGap, k = p.C / 100f;
        if (k <= 0)
            return [new(-hx, -hz), new(hx, -hz), new(hx, hz), new(-hx, hz)];
        var pts = new List<Vector2>(8) { new(-hx + k, -hz), new(hx - k, -hz), new(hx, -hz + k), new(hx, hz - k), new(hx - k, hz), new(-hx + k, hz), new(-hx, hz - k), new(-hx, -hz + k) };
        for (var i = pts.Count - 1; i >= 0 && pts.Count > 3; i--)
            if (Vector2.DistanceSquared(pts[i], pts[(i + 1) % pts.Count]) < 1e-10f)
                pts.RemoveAt(i); // (corners cut back by half the side meet)
        return [.. pts];
    }

    /// <summary>The length the head cuts round a pane (m): its whole outline.</summary>
    private static float PanePerimeter(in PaneSize p)
    {
        var o = PaneOutline(p);
        var s = 0f;
        for (var i = 0; i < o.Length; i++)
            s += Vector2.Distance(o[i], o[(i + 1) % o.Length]);
        return s;
    }

    private static float PaneArea(Vector2[] o)
    {
        var s = 0f;
        for (var i = 0; i < o.Length; i++)
        {
            var (a, b) = (o[i], o[(i + 1) % o.Length]);
            s += a.X * b.Y - b.X * a.Y;
        }
        return MathF.Abs(s) / 2;
    }

    /// <summary>(MakeCut's hook) A pane from its key.</summary>
    private static (bool Ok, ConstructPartDef? Def) MakePane(string name)
    {
        if (PaneOf(name, out var why) is not { } p)
        {
            Console.WriteLine($"[cuts] {name}: refused, {why}");
            return (false, null);
        }
        try
        {
            var def = ConstructParts.Parse(PaneJson(name, p), name);
            return Adopt(def, name) is { } made ? (true, made) : (false, null);
        }
        catch (ConstructPartException e)
        {
            Console.WriteLine($"[cuts] {name}: refused, {e.Message}");
            return (false, null);
        }
    }

    /// <summary>
    /// A pane's part file. Its back face lies on y = 0 (the opening's middle plane once welded to its <c>o</c> socket), its front <c>T</c> mm out.
    /// A rectangle is one solid glass box (rounds pass and break it, as the shipped Glass Panes); a pane with cut corners is drawn as a glass hull
    /// over a hidden box of its outline's bounds (a round stops on it and breaks it).
    /// </summary>
    private static string PaneJson(string name, in PaneSize p)
    {
        var b = new Builder();
        var o = PaneOutline(p);
        var t = p.Mm / 1000f;
        float hx = p.B / 200f - PaneGap, hz = p.A / 200f - PaneGap;
        if (p.C <= 0)
            b.Boxes.Add(new JsonObject { ["min"] = V(-hx, 0, -hz), ["max"] = V(hx, t, hz), ["color"] = PaneTint, ["glass"] = true });
        else
        {
            b.Hide(new Vector3(-hx, 0, -hz), new Vector3(hx, t, hz));
            var pts = new JsonArray();
            foreach (var y in (float[])[0, t])
                foreach (var q in o)
                    pts.Add(V(q.X, y, q.Y));
            b.Boxes.Add(new JsonObject { ["min"] = V(-hx, 0, -hz), ["max"] = V(hx, t, hz), ["color"] = PaneTint, ["shape"] = "hull", ["points"] = pts, ["glass"] = true, ["detail"] = true });
        }
        b.Sock("back", Vector3.Zero, "-y");
        var names = RecyclingTable.Current;
        var mat = names.IdOf(PaneMaterial);
        var row = ExtrusionTable.Current.Find(mat)!;
        var volume = PaneArea(o) * t;
        var part = new JsonObject
        {
            ["format"] = "call-of-slop-construct-part/1",
            ["name"] = name,
            ["description"] = $"A glass pane, {p.A / 100f:0.##} by {p.B / 100f:0.##} m and {p.Mm} mm thick{(p.C > 0 ? $", its corners cut back {p.C} cm" : "")}, cut by a Laser Cutter to fit an opening of that size. Weld it into the opening by its back. Glass: any bullet breaks it.",
            ["group"] = "Panels",
            ["section"] = "Cut parts",
            ["role"] = "panel",
            ["tool"] = "welder",
            ["mass"] = MathF.Round(Math.Clamp(volume * row.UnitsPerM3 * names.WeightOf(mat), 0.5f, ConstructParts.MaxPartMass), 1),
            ["health"] = MathF.Round(Math.Clamp(volume * row.HealthPerM3, 10, ConstructParts.MaxPartHealth)),
            ["armour"] = 0,
            ["material"] = row.Class,
            ["color"] = PaneTint,
            ["attachSeconds"] = 1.2,
            ["stage"] = true, // (never offered in the menus: a Laser Cutter makes it)
            ["centreSockets"] = false,
            ["boxes"] = b.Boxes,
            ["sockets"] = b.Sockets,
        };
        return part.ToJsonString();
    }

    /// <summary>"Pane 40 x 50 cm, 12 mm glass" for a pane key, or null.</summary>
    private static string? PaneLabel(string name) =>
        PaneOf(name, out _) is { } p ? $"Pane {p.A} x {p.B} cm{(p.C > 0 ? $" (corners {p.C})" : "")}, {p.Mm} mm glass" : null;
}
