using System.Globalization;
using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// The wear layer (kit 3; docs/vehiclekit/VEHICLEKIT.md, "Layered variety"): a vehicle's age and damage, from its style's <see cref="WearProfile"/> and the wear layer's own
/// dice, so the same truck can come new or wrecked. Panels gone over to rust and road dirt, soot round the guns and the exhaust (all in the style's own colours, so a worn
/// vehicle still reads as its faction by its paint), patch repairs welded over panels
/// (on top of the style's crooked plates, whose fork is the wear layer's too), and an open deck's edge rusted out. It never touches a room's hull (the Gaps check), a
/// fuel tank, a tyre or a gun (the weak points), the frame (stacked beams of one colour don't fight) or anything with slots (loot).
/// </summary>
public sealed partial class Vehicle
{
    /// <summary>The style's wear, set before the body is built (kit 3); null: no wear (kit 1 and 2).</summary>
    public WearProfile? Worn { get; private set; }

    /// <summary>The age this seed's wear rolled: a factor on the profile.</summary>
    public double Age { get; private set; }

    /// <summary>Starts the wear layer: the age, from its own fork. Call before the decks are laid.</summary>
    public void StartWear(WearProfile w)
    {
        Worn = w;
        Age = Dice.Fork("wear age").Between(w.AgeMin, w.AgeMax);
    }

    private static readonly string[] OpenDecks = ["bed", "porch", "rack", "hood"];
    private static readonly string[] PatchOn = ["hoodsides", "sides", "skirts", "panniers"];

    /// <summary>Where every crooked plate was welded (<see cref="Crooked"/>), Trim's and the wear's.</summary>
    private readonly List<Vector3> _welded = new();

    /// <summary>How much this vehicle wore, for the kit line: rusty, dirty and scorched panels, patches, missing deck cells.</summary>
    private int _rusty, _dirty, _scorched, _patched, _missing;

    /// <summary>
    /// An open deck's rusted-out edge (<see cref="Deck"/> calls it): a share of its edge cells (by the profile and the age) left with no plate. Never a cell beside
    /// one already taken (a hole for a gun, a whole plate for the drum: it needs a plate beside it to fit), never a room's floor, the cab's or a hutch's.
    /// </summary>
    private void WornEdges(string tag, bool[,] used, int nx, int nz)
    {
        if (Worn is not { } w || Array.IndexOf(OpenDecks, tag) < 0 || nx < 3 || nz < 3 || w.Missing * Age <= 0)
            return;
        var d = Dice.Fork("wear deck " + tag);
        var edge = new List<(int I, int J)>();
        for (var i = 0; i < nx; i++)
        for (var j = 0; j < nz; j++)
        {
            if (used[i, j] || i > 0 && i < nx - 1 && j > 0 && j < nz - 1)
                continue;
            var beside = false;
            for (var a = Math.Max(0, i - 1); a <= Math.Min(nx - 1, i + 1) && !beside; a++)
            for (var b = Math.Max(0, j - 1); b <= Math.Min(nz - 1, j + 1) && !beside; b++)
                beside = used[a, b];
            if (!beside)
                edge.Add((i, j));
        }
        var n = (int)MathF.Round((float)(edge.Count * w.Missing * Age * d.Between(0.5, 1.5)));
        foreach (var (i, j) in d.Shuffled(edge).Take(n))
        {
            used[i, j] = true;
            _missing++;
        }
    }

    /// <summary>The rest of the wear, once the body and its kit are on: paint, soot, patches. Its own forks of the wear layer.</summary>
    public void Weather()
    {
        if (Worn is not { } w)
            return;
        var d = Dice.Fork("wear paint");
        var panels = Panels.Where(kv => kv.Key is not "furniture" and not "patches").SelectMany(kv => kv.Value).Where(p => p.Def.Slots.Length == 0).Distinct().ToList();
        var bays = Look != null ? Rooms.SelectMany(r => r.Bays).Select(b => b.Index).ToHashSet() : [];
        if (bays.Count > 0) // (phase 2: a room's bays rust by runs, as they were painted, each unlike its neighbour, so the sections read through the wear)
            PaintRooms(Dice.Fork("wear runs"), "rust", (rd, colour) =>
            {
                if (!rd.Chance(w.Rust * Age))
                    return colour;
                _rusty++;
                return Style.Colour(rd, "rust");
            });
        foreach (var p in panels)
        {
            if (bays.Contains(p.Index))
                continue;
            var colour = ConstructPaint.ToHex(C.Parts[p.Index].Paint);
            if (colour.Length == 0)
                continue;
            var was = colour;
            if (d.Chance(w.Rust * Age))
            {
                colour = Style.Colour(d, "rust"); // (a panel gone over to the style's rust; its own colours only, so the faction still reads by its paint)
                _rusty++;
            }
            if (CentreOf(p).Y < 1.2f && d.Chance(w.Dirt * Age))
            {
                colour = Style.Colour(d, "frame"); // (road dirt: the frame's dark)
                _dirty++;
            }
            if (colour != was)
                Paint(p, colour);
        }

        // Soot: round a gun that has fired a lot, and the hood over the exhaust.
        var sd = Dice.Fork("wear scorch");
        var soot = Style.Palette.Values.SelectMany(x => x).OrderBy(Luma).ThenBy(x => x, StringComparer.Ordinal).First(); // (the style's darkest colour)
        var near = new List<Vector3>();
        foreach (var (gun, _, _) in Turrets)
            if (sd.Chance(w.Scorch * Age))
                near.Add(CentreOf(gun));
        if (Engine != null && sd.Chance(w.Scorch * Age * 0.5))
            near.Add(CentreOf(Engine));
        foreach (var at in near)
            foreach (var p in panels.Where(p => Vector3.Distance(CentreOf(p), at) < 1.3f).OrderBy(p => Vector3.DistanceSquared(CentreOf(p), at)).Take(sd.Int(1, 3)))
            {
                var colour = ConstructPaint.ToHex(C.Parts[p.Index].Paint);
                if (colour.Length == 0)
                    continue;
                Paint(p, soot);
                _scorched++;
            }

        // Patch repairs: a plate welded over an outer panel, only where it stands clear of everything else (a freeform part isn't checked against the rest).
        var pd = Dice.Fork("wear patches");
        var want = (int)MathF.Round((float)(pd.Int(w.PatchesMin, w.PatchesMax) * Age));
        var outer = PatchOn.Where(Panels.ContainsKey).SelectMany(t => Panels[t]).ToList();
        foreach (var p in pd.Shuffled(outer))
        {
            if (_patched >= want)
                break;
            VehicleGenJob.Yield(); // (each try reads every part's bounds)
            var c = CentreOf(p);
            var face = MathF.Abs(c.X) > 0.6f ? new Vector3(MathF.Sign(c.X), 0, 0) : c.Z > 0 ? Vector3.UnitZ : -Vector3.UnitZ;
            var at = c + face * 0.06f + new Vector3(0, (float)pd.Between(-0.08, 0.08), 0);
            if (!ClearOf(p, at, face))
                continue;
            var patch = Crooked("patches", Style.Part(pd, "patch"), p, at, face, pd);
            Paint(patch, Style.Colour(pd, "rust"));
            _patched++;
        }
        Modules.Add($"wear age {Age:0.0#} ({_rusty} rusty, {_dirty} dirty, {_scorched} sooty, {_patched} patched, {_missing} rusted out)");
    }

    /// <summary>Whether a half-metre patch at <paramref name="at"/>, facing <paramref name="face"/> over <paramref name="on"/>, meets nothing but <paramref name="on"/>.</summary>
    private bool ClearOf(VPart on, Vector3 at, Vector3 face)
    {
        foreach (var w in _welded)
            if (Vector3.Distance(w, at) < 0.6f)
                return false; // (another patch there: a freeform part's bounds aren't the body's frame, so it is checked by where it was asked for)
        C.PartBounds(on.Index, out var omn, out var omx);
        var k = MathF.Abs(face.X) > 0.5f ? 0 : 2; // (patches face out sideways or along z)
        var s = k == 0 ? face.X : face.Z;
        var outer = s > 0 ? FromRest(omx)[k] : FromRest(omn)[k];
        var (mn, mx) = (at - new Vector3(0.3f), at + new Vector3(0.3f)); // (half a metre of plate, tilted a few degrees)
        var (near, far) = (outer + s * 0.005f, at[k] + s * 0.05f);
        mn[k] = MathF.Min(near, far);
        mx[k] = MathF.Max(near, far);
        for (var i = 0; i < C.PartCount; i++)
        {
            var seg = C.Parts[i].Seg;
            if (i == on.Index || seg > 0 && seg <= C.JointCount && C.Joints[seg - 1].Freeform)
                continue; // (a weld's numbers are its own segment's: it is judged by its spot above)
            C.PartBounds(i, out var bmn, out var bmx);
            var (qmn, qmx) = (FromRest(bmn), FromRest(bmx));
            if (qmn.X < mx.X && qmx.X > mn.X && qmn.Y < mx.Y && qmx.Y > mn.Y && qmn.Z < mx.Z && qmx.Z > mn.Z)
                return false;
        }
        return true;
    }

    /// <summary>A "#rrggbb" colour's brightness, for the darkest of a palette.</summary>
    private static int Luma(string h) =>
        2 * int.Parse(h.AsSpan(1, 2), NumberStyles.HexNumber) + 7 * int.Parse(h.AsSpan(3, 2), NumberStyles.HexNumber) + int.Parse(h.AsSpan(5, 2), NumberStyles.HexNumber);
}
