using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// Body styles phase 3 (docs/vehiclekit/BODY-STYLES.md, section 9): one vehicle's roll of its cab and hood in its body's language. The section is the language's
/// own (a Bandit's slab or ledge, an IT pod, a British bus) drawn with the style's facets at a cab's size; its proportions, the windscreen's rake, the belt line,
/// the hood's width, crown and nose are the style's ranges (<see cref="BodyStyleRule"/>), each on a fork of its own, so nothing else of a seed moves.
/// </summary>
public sealed class CabLook
{
    public BodyLanguage Language { get; }
    public int Facets { get; }
    public float Width, Height, Belt, Rake, CapsuleRake, HoodHalf, HoodCrown, Reach, Back;
    public char Nose;

    /// <summary>The engine's top over a hood's section foot: the hood's sides come down 0.45 m past it (the fixed hoods' 0.56 m less the fenders' room).</summary>
    public const float HoodY0 = 0.45f;

    /// <summary>A cab's section has at most this many facets a side (its key keeps under a part name's 128 characters).</summary>
    public const int MaxFacets = 8;

    /// <summary><c>--cab KIND</c>: every lofted cab of that kind where its plan allows it (to look at one kind on many plans).</summary>
    public static string? Force;

    public CabLook(Vehicle v, BodyLook look)
    {
        var rule = v.Rule;
        var d = v.Dice.Fork("cab loft");
        Language = rule.Mix > 0 && d.Fork("language").Chance(rule.Mix) ? rule.Pick(d.Fork("which")) : look.At(0); // (a mixing style's cab may come off another wreck)
        if (rule.CabLanguage is { } own)
            Language = own; // (phase 8a: an archetype's own cab, a European cab-over ahead of a box body)
        if (BodyLook.Force is { } forced)
            Language = forced;
        Facets = look.Facets > 0 ? Math.Min(MaxFacets, Math.Max(6, look.Facets - 3)) : 0; // (a cab is smaller than a room: a few facets fewer reads as smooth)
        Width = (float)d.Fork("width").Between(rule.CabHalfMin, rule.CabHalfMax);
        Reach = rule.HoodReach;
        Height = (float)d.Fork("height").Between(rule.CabTallMin, rule.CabTallMax);
        Belt = (float)d.Fork("belt").Between(rule.BeltMin, rule.BeltMax);
        Rake = (float)d.Fork("rake").Between(rule.RakeMin, rule.RakeMax);
        CapsuleRake = (float)d.Fork("capsule rake").Between(rule.CapsuleRakeMin, rule.CapsuleRakeMax);
        HoodHalf = (float)d.Fork("hood width").Between(rule.HoodHalfMin, rule.HoodHalfMax);
        HoodCrown = (float)d.Fork("hood crown").Between(rule.HoodCrownMin, rule.HoodCrownMax);
        Nose = BodyStyleRule.Weighted(d.Fork("nose"), rule.Noses);
        Back = rule.CabBackMax > 0 ? (float)d.Fork("back").Between(rule.CabBackMin, rule.CabBackMax) : 0; // (phase 8a: a fastback's roof)
    }

    public string Label(char kind) => $"{Language.Name} {KindName(kind)}{(Facets > 0 ? $", {Facets} facets" : "")}, nose {NoseName(Nose)}";

    public static string KindName(char k) => k switch { 'w' => "crew cab", 'g' => "caged cab", 'a' => "armoured cab", 'o' => "capsule", 'm' => "merged cab", _ => "cab" };

    public static string NoseName(char n) => n switch { 'w' => "wedge", 'r' => "rounded", 'g' => "grille", _ => "flat" };

    public static char KindOf(string module) => module switch
    {
        "crew" => 'w',
        "caged" => 'g',
        "armoured" => 'a',
        "cab-over" => 'o',
        "merged" => 'm',
        _ => 'e',
    };

    /// <summary>
    /// The front profiles a style's cabs (or hoods) may show: each of its languages drawn with its facets at a cab's (a hood's) middle size, clamped as a cab's sides
    /// are, for identity's section part (cached).
    /// </summary>
    public static float[][] Refs(BodyStyleRule rule, bool hood)
    {
        lock (RefCache)
        {
            if (RefCache.TryGetValue((rule, hood), out var have))
                return have;
            var facets = rule.FacetsMax > 0 ? Math.Min(MaxFacets, Math.Max(6, (rule.FacetsMin + rule.FacetsMax) / 2 - 3)) : 0;
            var list = new List<float[]>();
            foreach (var (l, _) in rule.Languages)
            {
                var t = l.Drawn(facets);
                foreach (var h in hood ? new[] { HoodY0 + rule.HoodCrownMin, HoodY0 + rule.HoodCrownMax } : [rule.CabTallMin, rule.CabTallMax])
                {
                    var w = hood ? (rule.HoodHalfMin + rule.HoodHalfMax) / 2 : (rule.CabHalfMin + rule.CabHalfMax) / 2;
                    var s = t.Scaled(w / t.P[0].X, h / t.Top);
                    list.Add(FrontProfile.Of(hood ? Clamped(s, 0.4f, 0.64f, HoodY0 + 0.1f, 0.05f) : Clamped(s, 0.82f, 0.95f, 1.45f)));
                    if (!hood) // (a capsule: as CabFor draws one)
                        list.Add(FrontProfile.Of(Clamped(t.Scaled(0.95f / t.P[0].X, MathF.Max(1.72f, h) / t.Top), 0.85f, 0.97f, 1.05f)));
                }
            }
            return RefCache[(rule, hood)] = list.ToArray();
        }
    }

    private static readonly Dictionary<(BodyStyleRule, bool), float[][]> RefCache = new();

    /// <summary>The language's tall section at a cab's or a hood's size: half width <paramref name="w"/> at its foot, <paramref name="h"/> tall.</summary>
    public HullLoft.Section Scaled(float w, float h)
    {
        var t = Language.Drawn(Facets);
        return t.Scaled(w / t.P[0].X, h / t.Top);
    }

    /// <summary>Where the side turns into the roof: the lowest height above 1 m where the side comes in under 82 % of its widest (the windscreen's top stays under it).</summary>
    public static float RoofStart(HullLoft.Section s)
    {
        var most = s.WidestBetween(0, s.Top);
        for (var y = 1.0f; y < s.Top; y += 0.02f)
            if (s.XAt(y) < 0.82f * most)
                return y;
        return s.Top;
    }

    /// <summary>Points closer than 1.5 cm dropped and the half widths below <paramref name="below"/> held between <paramref name="lo"/> and <paramref name="hi"/> (the seats' and the wheels' room).</summary>
    public static HullLoft.Section Clamped(HullLoft.Section s, float lo, float hi, float below, float gap = 0.015f)
    {
        var pts = new List<Vector2>();
        foreach (var p in s.P)
        {
            var q = p.Y < below && pts.Count < s.P.Length - 1 ? p with { X = MathF.Round(Math.Clamp(p.X, lo, hi) * 100) / 100 } : p;
            if (pts.Count == 0 || Vector2.Distance(q, pts[^1]) >= gap)
                pts.Add(q);
            else if (q.X == 0)
                pts[^1] = q;
        }
        if (pts[^1].X != 0)
            pts.Add(new Vector2(0, pts[^1].Y));
        return new HullLoft.Section(pts.ToArray());
    }
}

public sealed partial class Vehicle
{
    /// <summary>Phase 3: this vehicle's cab and hood look (null: the fixed cab shells and hoods of kits 1 to 4, and of shapes not lofted).</summary>
    public CabLook? CabLook { get; set; }

    /// <summary>The lofted cab as built (null: a fixed shell, or none yet).</summary>
    public HullLoft.CabShape? CabShape { get; private set; }

    /// <summary>The lofted hood as built (null: a fixed hood, plates, a capsule's flat front).</summary>
    public HullLoft.HoodShape? HoodShape { get; private set; }

    /// <summary><c>--fixed-cabs</c>: kit 5 bodies with the fixed cab shells and hoods (phase 2's), to measure against.</summary>
    public static bool FixedCabs;

    private const float CabDeckEdge = 0.875f;

    /// <summary>
    /// The cab behind an engine at z -1.425, lofted in the body's language (phase 3): the seat sill, the driver on the left, a passenger sometimes (a crew cab: a
    /// second row on a sill of its own, two passengers), the cab shell made from its key, its window panes, the floor plates. A merged cab's shell waits for the
    /// room it merges into (<see cref="MergeCab"/>).
    /// </summary>
    public Vehicle LoftCab(string module)
    {
        const float sill = 0.375f;
        var kind = CabLook.KindOf(module);
        Sill(-0.175f, 0.5f);
        Driver(new Vector3(-0.5f, sill, -0.175f));
        if (kind == 'w' || Dice.Fork("crew").Chance(Style.Limit("thirdSeat")))
            Passenger(new Vector3(0.5f, sill, -0.175f));
        if (kind == 'w')
        {
            Sill(0.575f, 0.5f);
            Passenger(new Vector3(-0.5f, sill, 0.575f));
            Passenger(new Vector3(0.5f, sill, 0.575f));
        }
        Modules.Add("cab " + CabLook!.Label(kind));
        if (kind != 'm')
            PlaceCab(CabFor(kind));
        Deck("cab", Half, new Rect(-CabDeckEdge, CabDeckEdge, -0.8f, -0.3f), "deck");
        if (kind == 'w')
            Deck("cab2", Half, new Rect(-CabDeckEdge, CabDeckEdge, 0.2f, 0.95f), "deck", [new Rect(-CabDeckEdge, CabDeckEdge, 0.45f, 0.7f)]);
        return this;
    }

    /// <summary>A merged cab, after its room: the capsule whose back is the room's brow's own front section, where the brow stands (one mass, a bus front).</summary>
    public Vehicle MergeCab(Room room)
    {
        var brow = room.Bays[0];
        if (HullLoft.ShapeOf(brow.Def.Name) is not { Front: true } sh)
            throw new VehicleKitException("a merged cab needs a lofted brow in front of its room");
        var at = FromRest(C.Parts[brow.Index].Pos);
        var (zb, dy) = (MathF.Round((at.Z + 0.175f) * 100) / 100, MathF.Round((at.Y - Half) * 100) / 100);
        var pts = new List<Vector2> { new(MathF.Min(0.9f, sh.A.P[0].X), 0) };
        pts.AddRange(sh.A.P.Select(p => new Vector2(p.X, MathF.Round((p.Y + dy) * 100) / 100)));
        if (pts.Count > HullLoft.MaxPoints)
            throw new VehicleKitException($"a merged cab: the brow's section has {sh.A.P.Length} points, a cab's key takes {HullLoft.MaxPoints - 1}");
        var t = new HullLoft.Section(pts.ToArray());
        var yt = Math.Clamp(MathF.Min(CabLook.RoofStart(t), t.Top - 0.25f), 1.5f, 2.4f);
        PlaceCab(new HullLoft.CabShape('m', -2.15f, -1.0f, zb, 1.02f, yt, CabLook!.CapsuleRake, -0.42f, 0.2f, 0, 0, 0.85f, t, t));
        return this;
    }

    private void PlaceCab(HullLoft.CabShape shape)
    {
        if (Look is { Lining: not '\0' and var lining } && (shape with { Lining = lining }).Key().Length <= ConstructParts.MaxRuntimeName)
            shape = shape with { Lining = lining }; // (phase 7a: the faction's dash, where the key has room for its word; phase 7c: a capsule's and a merged cab's console, every one's steering wheel)
        var key = shape.Key();
        if (ConstructParts.Find(key) == null)
            throw new VehicleKitException($"the lofted cab won't make: {key}");
        var cab = Stand(key, new Vector3(-RailX, Half, 0.075f), Orient.Forward, null, "footL", 0.06f);
        CabShape = shape;
        PanelList("cabshell").Add(cab);
        foreach (var (socket, pane) in HullLoft.PanesOf(shape))
            PanelList("glass").Add(AsIs(cab, socket, pane.Key(), "frame"));
    }

    /// <summary>A part on <paramref name="on"/>'s socket turned as <paramref name="on"/> is (a pane cut in its frame: its corners are in that part's frame).</summary>
    private VPart AsIs(VPart on, string socket, string part, string by)
    {
        var def = Def(part);
        var target = C.DefOf(on.Index);
        var ts = Array.FindIndex(target.Sockets, s => s.Name == socket);
        var ds = Array.FindIndex(def.Sockets, s => s.Name == by);
        C.SocketAt(on.Index, ts, out _, out var dir);
        Span<byte> turns = stackalloc byte[4];
        var n = ConstructBuild.SpinTurns(def.Sockets[ds].Dir, dir, turns);
        for (var sp = 0; sp < n; sp++)
            if (turns[sp] == C.Parts[on.Index].Rot)
                return Onto(on, socket, part, by, sp);
        throw new VehicleKitException($"a {def.Name}: no turn keeps it in the {target.Name}'s frame");
    }

    /// <summary>A cab's shape for its kind, from the look: the conventional, crew, cage and armoured cabs behind a hood, a capsule over the engine.</summary>
    public HullLoft.CabShape CabFor(char kind)
    {
        var l = CabLook!;
        if (kind == 'o')
        {
            var cap = CabLook.Clamped(l.Scaled(0.95f, MathF.Max(1.72f, l.Height)), 0.85f, 0.97f, 1.05f);
            var ytc = Math.Clamp(CabLook.RoofStart(cap) - 0.02f, 1.5f, cap.Top - 0.12f);
            return new HullLoft.CabShape('o', -2.15f, -1.0f, 0.3f, 1.02f, ytc, l.CapsuleRake, -0.42f, 0.2f, 0, 0, 0.84f, cap, cap);
        }
        var hoodTop = CabLook.HoodY0 + l.HoodCrown - 0.45f + 0.875f - Half; // (the hood's crown in the cab's frame: the engine's top is 0.875 m up)
        var belt = MathF.Max(l.Belt, hoodTop + 0.04f);
        var t = CabLook.Clamped(l.Scaled(l.Width, kind == 'a' ? MathF.Min(l.Height, 1.68f) : l.Height), 0.82f, 0.95f, 1.45f);
        var yt = kind == 'a' ? 1.41f : Math.Clamp(CabLook.RoofStart(t) - 0.02f, 1.43f, t.Top - 0.12f);
        var (zb, d2, d3) = kind == 'w' ? (1.125f, 0.42f, 0.98f) : (0.325f, 0f, 0f);
        var rake = MathF.Min(l.Rake, 0.24f);
        // Phase 8a: a fastback's back section comes down toward the window band's top (it keeps 6 cm over it, the part's own limit being 4).
        var back = l.Back > 0 && kind is 'e' or 'g' ? CabLook.Clamped(t.Scaled(1f, (t.Top - l.Back * (t.Top - yt - 0.06f)) / t.Top), 0.82f, 0.95f, 1.45f) : t;
        if (back.Top <= yt + 0.05f)
            back = t;
        return new HullLoft.CabShape(kind, -0.6f, -0.6f, zb, MathF.Min(belt, yt - 0.3f), yt, rake, -0.3f, 0.22f, d2, d3, 1f, t, back);
    }

    /// <summary>A lofted hood for the fixed hood part a plan chose (its length class: a Long Hood or a Bull Hood is long, a Snub Hood or a Slab Nose short; a Slab Nose wide).</summary>
    private string LoftedHood(string part, Dice d)
    {
        var l = CabLook!;
        var longHood = part is "Long Hood" or "Bull Hood";
        var zf = MathF.Max(-1.05f, (longHood ? (float)d.Fork("hood length").Between(-1.0, -0.88) : (float)d.Fork("hood length").Between(-0.8, -0.72)) - l.Reach);
        var half = part == "Slab Nose" ? 0.62f : l.HoodHalf;
        var s = l.Scaled(half, CabLook.HoodY0 + l.HoodCrown);
        // The engine under it: its core and its details (a V8's rocker covers 12 cm over its top, 0.343 m out) clear of the skin by its thickness and a little.
        for (var grow = 0; grow < 12 && !(s.XAt(CabLook.HoodY0 + 0.13f) >= 0.4f && s.XAt(0.05f) >= 0.4f && s.YAt(0.38f) >= CabLook.HoodY0 + 0.155f); grow++)
            s = s.Scaled(s.XAt(CabLook.HoodY0 + 0.13f) < 0.4f || s.XAt(0.05f) < 0.4f ? 1.03f : 1f, s.YAt(0.38f) < CabLook.HoodY0 + 0.155f ? 1.03f : 1f);
        s = CabLook.Clamped(s, 0.4f, 0.64f, CabLook.HoodY0 + 0.1f, 0.05f); // (a squashed section's short rows, an eave's say, mitre into hairpins: rows under 5 cm go)
        var (kx, ky) = l.Nose switch { 'w' => (0.92f, 0.55f), 'r' => (0.82f, 0.78f), 'g' => (0.98f, 0.98f), _ => (0.96f, 0.96f) };
        var shape = new HullLoft.HoodShape(l.Nose, MathF.Round(zf * 100) / 100, -0.62f, 0.6f, CabLook.HoodY0, kx, ky, -0.25f, s);
        var key = shape.Key();
        if (ConstructParts.Find(key) == null)
            throw new VehicleKitException($"the lofted hood won't make: {key}");
        HoodShape = HullLoft.HoodOf(key);
        return key;
    }
}
