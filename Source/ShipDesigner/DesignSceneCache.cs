using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// What the page's JSON is made of, kept between edits (a slider let go took 5-15 s on the page, 2026-11-24): each body copy's mesh, each
/// connection's, each room's shell and interior, each window and the checks, as text under a key of everything it was made from. An edit
/// that leaves something's key alone sends the same text without building it again. A key holds every input: the bodies' shapes (not
/// their cladding or paint) for what only follows the skin, every room's plan for a room, the doors the checks place, and so on.
/// </summary>
public static partial class DesignScene
{
    private static readonly Dictionary<string, (string Key, string Json)> Parts = [];
    private static (string Key, Check C)? _check;

    private static long _made;

    /// <summary>A key for a mesh just made in a slot, new every time one is made, so the page builds its geometry once per key.</summary>
    private static string Made(string slot) => slot + "." + ++_made;

    /// <summary>A mesh's text with its key, kept in a slot as <see cref="Part"/> keeps it.</summary>
    private static string MeshPart(string slot, string key, Func<DesignMesh> make) => Part(slot, key, () =>
    {
        var t = new StringBuilder();
        make().Write(t, Made(slot));
        return t.ToString();
    });

    /// <summary>The text under a slot when its key still matches, else made again and kept.</summary>
    private static string Part(string slot, string key, Func<string> make)
    {
        if (Parts.TryGetValue(slot, out var hit) && hit.Key == key)
            return hit.Json;
        var json = make();
        Parts[slot] = (key, json);
        return json;
    }

    /// <summary>The checks, run again only when something they read changed: the design less its cladding, paint and room interiors.</summary>
    private static Check Checks(ShipDesign d)
    {
        var o = JsonNode.Parse(d.ToJson())!.AsObject();
        foreach (var h in o["hulls"]!.AsArray())
        {
            h!.AsObject().Remove("clad");
            h.AsObject().Remove("cladFrom");
            h.AsObject().Remove("paint");
            h.AsObject().Remove("material");
            h.AsObject().Remove("damage");
        }
        foreach (var j in o["joins"]!.AsArray())
        {
            j!.AsObject().Remove("clad");
            j.AsObject().Remove("buttressClad");
            j.AsObject().Remove("matchClad");
            j.AsObject().Remove("matchButtress");
            j.AsObject().Remove("matchColour");
            j.AsObject().Remove("paint");
        }
        foreach (var r in o["rooms"]!.AsArray())
            foreach (var k in new[] { "interior", "ribSpacing", "panelColour", "trimColour", "lights", "floor", "wallStyle", "wallColour", "seed" })
                r!.AsObject().Remove(k);
        var key = o.ToJsonString();
        if (_check is { } hit && hit.Key == key)
            return hit.C;
        var c = Run(d);
        _check = (key, c);
        return c;
    }

    /// <summary>Every body's shape, for the keys of what follows the skin (rooms, windows, connections).</summary>
    private static string Shapes(ShipDesign d)
    {
        var k = new StringBuilder(d.Stacked ? "S" : "A");
        foreach (var h in d.Hulls)
            k.Append('|').Append(h.ShapeKey());
        foreach (var dk in d.Decks)
            k.Append('/').Append(ShipDesign.R(dk.Floor)).Append(',').Append(ShipDesign.R(dk.Height));
        return k.ToString();
    }

    /// <summary>Every room's plan (not its interior), for the keys of what another room's place changes (shells, edges, windows).</summary>
    private static string Plans(ShipDesign d)
    {
        var k = new StringBuilder();
        foreach (var r in d.Rooms)
        {
            var o = r.ToJson();
            foreach (var n in new[] { "interior", "ribSpacing", "panelColour", "trimColour", "lights", "floor", "wallStyle", "wallColour", "seed" })
                o.Remove(n);
            k.Append(r.Id).Append(':').Append(o.ToJsonString()).Append('|');
        }
        return k.ToString();
    }

    /// <summary>A body's look less its cladding (a connection takes its colour from its first body).</summary>
    private static string Paint(HullBody? h)
    {
        if (h is null)
            return "";
        var o = h.ToJson();
        o.Remove("clad");
        o.Remove("material");
        return o.ToJsonString();
    }

    /// <summary>A body's JSON for its mesh's key: all but its materials, which the page applies (the mesh is the same in any).</summary>
    private static string MeshJson(HullBody h)
    {
        var o = h.ToJson();
        o.Remove("material");
        (o["clad"] as JsonObject)?.Remove("material");
        return o.ToJsonString();
    }

    private static string Doors(List<double[]> doors)
    {
        var k = new StringBuilder();
        foreach (var q in doors)
            k.Append(q[0].ToString("R")).Append(',').Append(q[1].ToString("R")).Append(',').Append(q[2].ToString("R")).Append(';');
        return k.ToString();
    }

    private static string WindowKey(HullWindow w) =>
        $"{w.Id}:{w.Room}:{w.Side}:{w.T:R}:{w.W:R}:{w.H:R}:{w.Sill:R}:{w.Shape}:{w.Hole:R}:{w.Gap:R}:{w.Depth:R}:{w.Double}";

    // Each window's Lay answers (glass and boarding) under the key of all they come from: the window, every body's shape and every room's
    // plan. A body's cut and the window's own JSON both ask; a room's interior or a body's cladding changing leaves them be.
    private static readonly Dictionary<(int, double, double), (string Key, (double[] Pts, int Nu, int Nv, double Gap, bool Ok, double Len) L)> Laid = [];

    /// <summary>The key of what every window is laid from, worked out once a Json call (null outside one: then made on the spot).</summary>
    private static string? _frames;

    private static string FrameKey(ShipDesign d) => _frames ?? Shapes(d) + "#" + Plans(d);

    private static (double[] Pts, int Nu, int Nv, double Gap, bool Ok, double Len) LayOnce(ShipDesign d, HullWindow w, double grow = 0, double lift = 0.03)
    {
        var key = WindowKey(w) + FrameKey(d);
        if (Laid.TryGetValue((w.Id, grow, lift), out var hit) && hit.Key == key)
            return hit.L;
        var l = Lay(d, w, grow, lift);
        Laid[(w.Id, grow, lift)] = (key, l);
        return l;
    }

    /// <summary>The cut a window makes in the skin it lies on (null when it lies on none), and that cut as text for a body's key.</summary>
    /// <summary>How far a hull window's boarding reaches past the glass: 10 cm, plus what its hole scale adds.</summary>
    private static double WindowGrow(ShipDesign d, HullWindow w) =>
        WindowFrame(d, w) is { } q ? Math.Max(0.02, 0.1 + (w.Hole - 1) * Math.Min(q.Hx, q.Hz)) : 0.1;

    private static (Cutout? Cut, string Key) WindowCut(ShipDesign d, HullWindow hw)
    {
        // Steep skin (a hull turning under the window) is cut too: only facets almost along the rays are left whole.
        // Cut along the very rays the glass was laid by (straight out of the wall), so the hole is the glass's own outline on
        // the skin however the hull curves (Karl, 2026-11-24: a flat plane cut a box out of a curved side).
        if (WindowFrame(d, hw) is not { } q)
            return (null, "");
        // Only the skin the glass lies on: a band along the rays from just inside its nearest point to just past its
        // farthest, so a cut never runs on through a nose or a far wall (Karl, 2026-11-24: a window took a nose off).
        var (lp, _, _, _, _, _) = LayOnce(d, hw);
        double lo = double.MaxValue, hi = double.MinValue;
        for (var k = 0; k + 2 < lp.Length; k += 3)
        {
            var nn = (lp[k] - q.O[0]) * q.N[0] + (lp[k + 1] - q.O[1]) * q.N[1] + (lp[k + 2] - q.O[2]) * q.N[2];
            lo = Math.Min(lo, nn); hi = Math.Max(hi, nn);
        }
        if (lo > hi)
            return (null, "");
        var key = new StringBuilder(hw.Shape).Append(',').Append(hw.Gap.ToString("R")).Append(',').Append(hw.Hole.ToString("R"));
        var ex = (hw.Hole - 1) * Math.Min(q.Hx, q.Hz);
        foreach (var v in q.O.Concat(q.U).Concat(q.N).Concat(q.V).Append(q.Hx).Append(q.Hz).Append(lo).Append(hi))
            key.Append(',').Append(v.ToString("R"));
        return (new Cutout(q.O, q.U, q.N, q.V, hw.Shape == "Rect" ? Rect(q.Hx + ex - 0.01, q.Hz + ex - 0.01) : HullWindow.Rim(hw.Shape, q.Hx + ex - 0.01, q.Hz + ex - 0.01), 0, (lo - 0.12, hi + 0.08)) { GapSet = hw.Gap, Band = 0.1 }, key.ToString());
    }
}
