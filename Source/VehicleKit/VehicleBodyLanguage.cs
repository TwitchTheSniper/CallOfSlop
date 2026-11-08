using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// A style's body language (docs/vehiclekit/BODY-STYLES.md): the cross-sections its rooms are lofted from, and how a seed varies them. The grammar still plans a room's
/// bays as tall, low and narrow sections (<see cref="ShapeGrammar.Bays(Dice, int, bool, BodyRange)"/>); a language turns each into this style's own section, at this
/// seed's height and width, and its brow and tail into this seed's rake. Kit 5 (<see cref="VehicleKitVersion.Lofted"/>). A language is data: a new archetype (a van, a
/// coach, a pod, later a car's cabin) is a row here and a row in <see cref="BodyStyleRule.ByStyle"/>, never a special case in the builder.
/// </summary>
public sealed record BodyLanguage(string Name, string Words, HullLoft.Section Tall, float HeightMin, float HeightMax, float BrowRakeMin, float BrowRakeMax, float TailRakeMin, float TailRakeMax,
    float BrowNarrow, float TailNarrow, float LowShare = 0.87f, float NarrowShare = 0.8f)
{
    /// <summary>
    /// Phase 2: the rooflines this language draws along a room, and how often (<see cref="Roofline"/>); null: <see cref="Roofline.Plain"/>, the plan's steps only.
    /// Data, so a new archetype's line (a muscle car's fastback, a bus's dome) is a row.
    /// </summary>
    public (Roofline Line, double W)[]? Lines { get; init; }

    /// <summary>How an overhang meets the wall under it: the height over which the extra width comes in (0.05 m a hard welded ledge, 0.5 m a soft flare).</summary>
    public float FlareBlend { get; init; } = 0.3f;

    /// <summary>The language's own walk-in section is a room's; a later non-room archetype (an engine cover, a cargo box, a car's cabin) says false and keeps its own scale.</summary>
    public bool WalkIn { get; init; } = true;

    private static HullLoft.Section S(params float[] xy) => new(Enumerable.Range(0, xy.Length / 2).Select(i => new Vector2(xy[2 * i], xy[2 * i + 1])).ToArray());

    // The languages. Every tall section keeps x >= 1.0 below 1.6 m (furniture reaches a metre out: Vehicle.BayHalf), stands on the floor at 1.0 m or more and is over
    // 2.1 m at its top, so a door bay can be any of them; a low one keeps 2 m at the centre. The rounded ones are control polygons: a smooth style draws them as
    // curves (BodyStyleRule.Facets), a faceted one as they are.
    // Phase 2: each language's rooflines along the room (Roofline) and how its overhang meets the wall under it (FlareBlend): data, a row each.
    public static readonly BodyLanguage Slab = new("slab", "welded slabs: upright walls, one hard chamfer, a flat roof", S(1.04f, 0, 1.07f, 1.86f, 0.8f, 2.22f, 0, 2.22f), 2.16f, 2.32f, 0.62f, 0.8f, 0.7f, 0.9f, 0.96f, 0.94f)
        { Lines = [(Roofline.Stepped, 2), (Roofline.Plain, 1), (Roofline.Kicked, 1), (Roofline.Raked, 1), (Roofline.Sagged, 0.5)], FlareBlend = 0.05f };
    public static readonly BodyLanguage Ledge = new("ledge", "a box welded over a box: the upper walls stand proud of the lower on a ledge", S(1.02f, 0, 1.03f, 1.08f, 1.12f, 1.16f, 1.12f, 1.9f, 0.84f, 2.2f, 0, 2.2f), 2.14f, 2.3f, 0.7f, 0.85f, 0.75f, 0.9f, 0.95f, 0.93f)
        { Lines = [(Roofline.Stepped, 1.5), (Roofline.Plain, 1), (Roofline.Kicked, 1), (Roofline.Sloped, 1)], FlareBlend = 0.05f };
    public static readonly BodyLanguage Pod = new("pod", "clean rounded panels: tumblehome and a crowned roof", S(1.0f, 0, 1.1f, 0.45f, 1.1f, 1.3f, 0.98f, 1.8f, 0.72f, 2.15f, 0.36f, 2.3f, 0, 2.33f), 2.26f, 2.4f, 0.64f, 0.76f, 0.65f, 0.82f, 0.9f, 0.9f)
        { Lines = [(Roofline.Crowned, 2), (Roofline.Tapered, 2), (Roofline.Sloped, 1), (Roofline.Plain, 0.5)], FlareBlend = 0.6f };
    public static readonly BodyLanguage Van = new("van", "a panel van: near-upright sides rounding into a gently crowned roof", S(1.02f, 0, 1.08f, 0.6f, 1.08f, 1.75f, 0.96f, 2.08f, 0.6f, 2.24f, 0, 2.28f), 2.22f, 2.36f, 0.6f, 0.78f, 0.78f, 0.92f, 0.94f, 0.95f)
        { Lines = [(Roofline.Plain, 1.5), (Roofline.Raked, 1), (Roofline.Crowned, 1), (Roofline.Tapered, 0.5)], FlareBlend = 0.4f };
    public static readonly BodyLanguage Bus = new("bus", "a bus: tall upright sides, a domed roof with rounded corners", S(1.04f, 0, 1.07f, 0.95f, 1.06f, 1.85f, 0.98f, 2.12f, 0.78f, 2.3f, 0.4f, 2.38f, 0, 2.4f), 2.34f, 2.48f, 0.72f, 0.9f, 0.72f, 0.88f, 0.96f, 0.95f)
        { Lines = [(Roofline.Plain, 1.5), (Roofline.Crowned, 1), (Roofline.Kicked, 0.5), (Roofline.Tapered, 0.5)], FlareBlend = 0.5f };
    public static readonly BodyLanguage Coach = new("coach", "a coach: a lower body, a long tumblehome and a raked front", S(1.02f, 0, 1.08f, 0.7f, 1.06f, 1.5f, 0.92f, 1.98f, 0.62f, 2.18f, 0, 2.24f), 2.2f, 2.32f, 0.6f, 0.72f, 0.72f, 0.88f, 0.9f, 0.93f)
        { Lines = [(Roofline.Raked, 1.5), (Roofline.Tapered, 1), (Roofline.Crowned, 1), (Roofline.Plain, 0.5)], FlareBlend = 0.5f };
    public static readonly BodyLanguage Boat = new("boat", "boat-like: a flare to a strake, a curved tumblehome, a boat-tail stern", S(1.02f, 0, 1.13f, 0.7f, 1.09f, 1.45f, 0.92f, 1.95f, 0.55f, 2.22f, 0, 2.26f), 2.2f, 2.36f, 0.65f, 0.82f, 0.6f, 0.78f, 0.92f, 0.8f)
        { Lines = [(Roofline.Sheer, 2), (Roofline.Sagged, 1), (Roofline.Tapered, 1), (Roofline.Plain, 0.5)], FlareBlend = 0.35f };
    public static readonly BodyLanguage Cabin = new("cabin", "a site cabin: plain walls, eaves and a pitched roof", S(1.04f, 0, 1.06f, 1.92f, 1.12f, 1.98f, 0, 2.42f), 2.36f, 2.5f, 0.75f, 0.9f, 0.75f, 0.9f, 0.97f, 0.97f, 0.88f)
        { Lines = [(Roofline.Plain, 2), (Roofline.Stepped, 1), (Roofline.Sloped, 0.5)], FlareBlend = 0.05f };
    public static readonly BodyLanguage Armour = new("armour", "faceted armour: a flared lower hull, a steep glacis-like upper, a narrow roof", S(1.0f, 0, 1.13f, 0.75f, 0.88f, 2.0f, 0.56f, 2.2f, 0, 2.2f), 2.16f, 2.28f, 0.62f, 0.75f, 0.65f, 0.8f, 0.9f, 0.88f)
        { Lines = [(Roofline.Sloped, 1.5), (Roofline.Plain, 1), (Roofline.Raked, 1), (Roofline.Kicked, 0.5)], FlareBlend = 0.08f };

    public static readonly BodyLanguage[] All = [Slab, Ledge, Pod, Van, Bus, Coach, Boat, Cabin, Armour];

    /// <summary>Which languages a style speaks, and how often (a style not named speaks armour). Kept for the tools; the rule is <see cref="BodyStyleRule"/>.</summary>
    public static (BodyLanguage L, double W)[] Of(FactionStyle st) => BodyStyleRule.Of(st).Languages;

    /// <summary>
    /// The language's tall section drawn with <paramref name="facets"/> facets a side (0: as written, its own corners). A smooth one is the control polygon cut at its
    /// corners three times (Chaikin), then walked at even steps along it; its last row (onto the centreline) stays level, so the roof is one panel across.
    /// </summary>
    public HullLoft.Section Drawn(int facets)
    {
        if (facets <= 0 || facets + 1 == Tall.P.Length)
            return Tall;
        var pts = Tall.P.ToList();
        for (var pass = 0; pass < 3; pass++)
        {
            var next = new List<Vector2> { pts[0] };
            for (var k = 0; k + 1 < pts.Count; k++)
            {
                var (a, b) = (pts[k], pts[k + 1]);
                if (k > 0)
                    next.Add(Vector2.Lerp(a, b, 0.25f));
                if (k + 2 < pts.Count)
                    next.Add(Vector2.Lerp(a, b, 0.75f));
            }
            next.Add(pts[^1]);
            pts = next;
        }
        var len = new float[pts.Count];
        for (var k = 1; k < pts.Count; k++)
            len[k] = len[k - 1] + Vector2.Distance(pts[k - 1], pts[k]);
        var outPts = new Vector2[facets + 1];
        var j = 0;
        for (var i = 0; i <= facets; i++)
        {
            var at = len[^1] * i / facets;
            while (j + 2 < pts.Count && len[j + 1] < at)
                j++;
            var t = len[j + 1] > len[j] ? Math.Clamp((at - len[j]) / (len[j + 1] - len[j]), 0, 1) : 0;
            outPts[i] = Vector2.Lerp(pts[j], pts[j + 1], t);
        }
        outPts[0] = Tall.P[0];
        outPts[^1] = Tall.P[^1];
        outPts[^2].Y = outPts[^1].Y; // (level onto the centreline: one roof panel, no seam)
        for (var i = 1; i < outPts.Length; i++)
            outPts[i].Y = MathF.Max(outPts[i].Y, outPts[i - 1].Y); // (a profile only goes up)
        return new HullLoft.Section(outPts);
    }
}

/// <summary>
/// A line along a room (docs/vehiclekit/BODY-STYLES.md, "Phase 2"): how a body's height and its width above the furniture change from the front of the room to its
/// back, as factors of the language's section at each station between two bays (lofted bay to bay). Crown (a hog's back, or below 0 a sway back), rise (a raked roof
/// rising to the tail, or below 0 sloping down to it), a kick-up over the tail, a taper in plan toward both ends, and how hard the plan's tall, low and narrow steps
/// stay (1 as planned, 0 none). Each language lists the lines it draws (<see cref="BodyLanguage.Lines"/>) and the seed rolls one and its amount: data, a row each.
/// </summary>
public sealed record Roofline(string Name, string Words, float Crown = 0, float Rise = 0, float Kick = 0, float Taper = 0, float Steps = 1)
{
    public static readonly Roofline Plain = new("plain", "the plan's steps between tall, low and narrow bays");
    public static readonly Roofline Stepped = new("stepped", "the plan's steps, cut deeper", Steps: 1.6f);
    public static readonly Roofline Crowned = new("crowned", "a hog's back: highest in the middle, easing down to both ends", Crown: 0.16f, Steps: 0.35f);
    public static readonly Roofline Sagged = new("sagged", "a sway back: lowest in the middle, rising to both ends", Crown: -0.12f, Steps: 0.5f);
    public static readonly Roofline Raked = new("raked", "low at the front, rising toward the tail", Rise: 0.11f, Steps: 0.6f);
    public static readonly Roofline Sloped = new("sloped", "tall at the front, sloping down toward the tail", Rise: -0.11f, Steps: 0.6f);
    public static readonly Roofline Kicked = new("kick-up", "level, then kicked up over the tail", Kick: 0.2f, Steps: 0.7f);
    public static readonly Roofline Tapered = new("tapered", "narrowing toward both ends in plan, a little crown", Crown: 0.06f, Taper: 0.2f, Steps: 0.5f);
    public static readonly Roofline Sheer = new("sheer", "a boat's sheer: high at the bow, dipping amidships, a little rise at the stern", Crown: -0.08f, Rise: -0.08f, Taper: 0.1f, Steps: 0.4f);

    public static readonly Roofline[] All = [Plain, Stepped, Crowned, Sagged, Raked, Sloped, Kicked, Tapered, Sheer];

    /// <summary>The height and width factors at <paramref name="u"/> along the room (0 its front, 1 its back), the line drawn at <paramref name="amount"/> of its size.</summary>
    public (float H, float W) At(float u, float amount)
    {
        var h = 1 + amount * (Crown * MathF.Sin(MathF.PI * u) + Rise * (2 * u - 1) + Kick * MathF.Max(0, (u - 0.6f) / 0.4f));
        var w = 1 - amount * Taper * (2 * u - 1) * (2 * u - 1);
        return (h, w);
    }
}

/// <summary>
/// A scale class (phase 2): the sizes a body of that class may take, so the limits are data, not constants. A walk-in class keeps what the room check needs (the aisle,
/// a metre either side of the middle below 1.6 m, and 2.02 m of headroom at the centre); a class that isn't walked in (an engine cover, a tank, an armoured shell, a
/// cargo box, a car's cabin) may go much smaller, and the big classes much bigger, up to what the chassis carries (the stability and drive checks plan again past
/// that). <see cref="HullLoft.Section"/>'s key reaches 2 m of half width and 4 m of height for any of them.
/// </summary>
public sealed record BodyScale(string Name, string Words, bool WalkIn, float HalfMin, float HalfMax, float HeightMin, float HeightMax, float BayMin, float BayMax, float FloorHalf,
    float FlareMin, float FlareMax)
{
    /// <summary>The ride height bands the class leans to (factors on the style's: a heavy room high on stilts rolls, so it seldom goes there).</summary>
    public (double Low, double Mid, double High) Ride { get; init; } = (1, 1, 1);

    /// <summary>A car's or a buggy's body: nothing walks in it; the half width 0.6 to 1.0 m, 0.8 to 1.9 m tall, bays of half a metre to a metre.</summary>
    public static readonly BodyScale Compact = new("compact", "a car's or a buggy's: engine covers, cargo boxes, cabins nobody walks in", false, 0.3f, 1.0f, 0.5f, 1.9f, 0.5f, 1.0f, 0.875f, 0, 0.1f);

    /// <summary>A truck's room: as phase 1's, the upper body a little proud of the wheels.</summary>
    public static readonly BodyScale Standard = new("standard", "a truck's walk-in room", true, 1.0f, 1.28f, 2.02f, 2.75f, 0.75f, 1.25f, 1.125f, 0, 0.2f);

    /// <summary>A heavy truck's or a bus's: taller, and wider over the wheels.</summary>
    public static readonly BodyScale Heavy = new("heavy", "a heavy truck's or a bus's: taller, wider over the wheels", true, 1.0f, 1.45f, 2.2f, 3.05f, 0.75f, 1.5f, 1.125f, 0.1f, 0.38f) { Ride = (0.7, 1.5, 0.25) };

    /// <summary>A land train's or a mobile base's: the biggest the kit has a name for (no style draws it yet: the chassis it needs is a later phase's).</summary>
    public static readonly BodyScale Giant = new("giant", "a land train's or a mobile base's", true, 1.0f, 1.9f, 2.4f, 3.8f, 1.0f, 2.0f, 1.125f, 0.2f, 0.7f) { Ride = (0.3, 1, 0) };

    public static readonly BodyScale[] All = [Compact, Standard, Heavy, Giant];

    /// <summary>Where a bay's feet stand from its middle, from its floor's half width (the part's own rule: <see cref="HullLoft.FootX(float)"/>).</summary>
    public static float Foot(float floorHalf) => HullLoft.FootX(floorHalf);
}

/// <summary>
/// How a style's rooms are lofted (the user's decisions, 2026-11-07): its languages and their weights; how many facets a side it draws them with (Bandit and
/// military flat-faceted, 3 to 6; IT, British and any future high-tech smooth, 8 to 12; 0: each language's own corners); and whether its bays mix languages one
/// station to the next (Bandit: cobbled from wrecks), or keep one language a vehicle. Phase 2 adds its proportions: the scale classes it builds at and how often,
/// how tall against the language (<see cref="TallMin"/>, <see cref="TallMax"/>), how far its upper body overhangs (the class's flare, times <see cref="FlareShare"/>),
/// how long its brows and tails may run (a wedge), and which side its door is on. Data, so a new style or archetype is a row.
/// </summary>
public sealed record BodyStyleRule((BodyLanguage L, double W)[] Languages, int FacetsMin = 0, int FacetsMax = 0, double Mix = 0)
{
    /// <summary>The scale classes the style's rooms come in, and how often.</summary>
    public (BodyScale S, double W)[] Scales { get; init; } = [(BodyScale.Standard, 2), (BodyScale.Heavy, 1)];

    /// <summary>The height against the language's own, as a factor range (then the scale's limits).</summary>
    public float TallMin { get; init; } = 0.97f;
    public float TallMax { get; init; } = 1.1f;

    /// <summary>How much of the scale's overhang range the style uses (0 none: its walls go straight up from the floor).</summary>
    public float FlareShare { get; init; } = 1f;

    /// <summary>How hard the style draws its rooflines (a factor on the line's amount, rolled 0.5 to 1 of it).</summary>
    public float LineAmount { get; init; } = 1f;

    /// <summary>Brow and tail lengths a seed may roll (metres, on the quarter grid), with weights: a long brow is a wedge nose on the room.</summary>
    public (float L, double W)[] Brows { get; init; } = [(1f, 2), (1.25f, 1.5), (1.5f, 1)];
    public (float L, double W)[] Tails { get; init; } = [(1f, 2), (0.75f, 1), (1.25f, 1)];

    /// <summary>How the style's chassis ride, as weights of the bands (low between arms, mid, high on stilts), before the scale class's own (<see cref="BodyScale.Ride"/>).</summary>
    public (double Low, double Mid, double High) Ride { get; init; } = (1, 1, 1);

    /// <summary>The chance a room's door is in its left (-x) side; the rest in its right (+x).</summary>
    public double DoorLeft { get; init; } = 0.5;

    /// <summary>The chance a room that is the vehicle's last, with nothing behind it but a hitch or a bare tail, has its door in its back instead (a van's, an APC's).</summary>
    public double DoorRear { get; init; } = 0.2;

    /// <summary>
    /// Paint by sections (phase 2, the user's: "so the shapes read through the wear"): a room's bays are painted in runs of <see cref="RunMin"/> to <see cref="RunMax"/>
    /// bays, a run ending where the roofline steps or the language changes, each run one colour unlike its neighbour's; <see cref="Contrast"/> is the palette role a run
    /// takes when its neighbour already has the colour it rolled (null: another of the panels' own role). The rows inside a bay keep their tones (HullLoft).
    /// </summary>
    public int RunMin { get; init; } = 2;
    public int RunMax { get; init; } = 3;
    public string? Contrast { get; init; }

    /// <summary>
    /// Phase 3 (docs/vehiclekit/BODY-STYLES.md, section 9): the cab kinds a lofted body's style builds, by the grammar's cab modules ("enclosed", "crew", "caged",
    /// "armoured", "cab-over", "merged"), and how often; a kind whose plan doesn't allow it (a crew cab needs a metre of bare rails behind the cab, a merged cab a
    /// room right behind it) drops out of the roll. Data, so an archetype's cab is a row.
    /// </summary>
    public (string Kind, double W)[] Cabs { get; init; } = [("enclosed", 2), ("crew", 1), ("cab-over", 0.5)];

    /// <summary>The noses a lofted hood takes (<c>f</c> flat, <c>w</c> wedge, <c>r</c> rounded, <c>g</c> a grille box), and how often.</summary>
    public (char Nose, double W)[] Noses { get; init; } = [('f', 1), ('w', 1), ('r', 1), ('g', 1)];

    /// <summary>A cab's height over its floor (the seated eye is 1.3 m up), the windscreen's rake on a cab behind a hood and on a capsule (how far its top is set back).</summary>
    public float CabTallMin { get; init; } = 1.62f;
    public float CabTallMax { get; init; } = 1.82f;
    public float RakeMin { get; init; } = 0.1f;
    public float RakeMax { get; init; } = 0.22f;
    public float CapsuleRakeMin { get; init; } = 0.45f;

    /// <summary>The belt (the window line) and the cab's half width: a high belt and a narrow screen read as a slit and a box, a low one as a glasshouse.</summary>
    public float BeltMin { get; init; } = 0.96f;
    public float BeltMax { get; init; } = 1.04f;
    public float CabHalfMin { get; init; } = 0.88f;
    public float CabHalfMax { get; init; } = 0.93f;

    /// <summary>How much further forward than the plan's hood part a hood reaches (a Highland's long bonnet), metres (the nose rails end 0.875 m ahead of the engine; a hood may run to 1.05 m).</summary>
    public float HoodReach { get; init; } = 0;
    public float CapsuleRakeMax { get; init; } = 0.7f;

    /// <summary>A hood's half width and its crown over the engine's top (the engines' rocker covers stand 12 cm up: it clears them by 4 cm or more).</summary>
    public float HoodHalfMin { get; init; } = 0.5f;
    public float HoodHalfMax { get; init; } = 0.6f;
    public float HoodCrownMin { get; init; } = 0.17f;
    public float HoodCrownMax { get; init; } = 0.24f;

    /// <summary>
    /// Phase 4 (docs/vehiclekit/BODY-STYLES.md, section 10): the faction's exterior on its rooms. Each entry is a trim set (<see cref="HullLoft.TrimKinds"/>'s letters)
    /// and its weight; a vehicle rolls one set, so a faction has two or three exterior sub-styles. With <see cref="TrimMix"/> each bay side takes any of the set's kinds
    /// (a Bandit's cobbled sides); otherwise the kinds take turns down the room. <see cref="TrimShare"/>: the chance a free bay side is dressed at all.
    /// </summary>
    public (string Kinds, double W)[] Trims { get; init; } = [];
    public bool TrimMix { get; init; }
    public double TrimShare { get; init; } = 1;

    /// <summary>The fittings a style's vehicles carry (<see cref="HullLoft.DecoKinds"/>), each with the chance a vehicle has it where its cab or hood can take it.</summary>
    public (char Kind, double Chance)[] Decos { get; init; } = [];

    /// <summary>
    /// Phase 5 (docs/vehiclekit/BODY-STYLES.md, section 11): the new spaces. <see cref="Uppers"/>: an upper storey over a run of a long room's middle bays, by kind
    /// ("none", "saloon" an enclosed storey reached by a stair through a hatch, "deck" an open deck with a bulwark round it), how much of the middle it spans
    /// (<see cref="UpperShareMin"/>), its height, and how much narrower than the wall under it it stands (<see cref="UpperNarrow"/>: a bus's flush, a pod's set in).
    /// <see cref="Cage"/>: the chance a room is a frame of bars instead (<see cref="CageKind"/>, a <see cref="HullLoft.CageKinds"/> letter). <see cref="Pods"/>: side
    /// pods (a <see cref="HullLoft.PodKinds"/> letter) and the chance each free middle bay side takes one, up to <see cref="PodMax"/> a vehicle.
    /// </summary>
    public (string Kind, double W)[] Uppers { get; init; } = [("none", 1)];
    public float UpperShareMin { get; init; } = 0.5f;
    public float UpperShareMax { get; init; } = 1f;
    public float UpperTallMin { get; init; } = 2.06f;
    public float UpperTallMax { get; init; } = 2.2f;
    public float UpperNarrow { get; init; } = 1f;
    public float BulwarkMin { get; init; } = 1.0f;
    public float BulwarkMax { get; init; } = 1.15f;
    public double Cage { get; init; }
    public char CageKind { get; init; } = 'w';
    public (char Kind, double Chance)[] Pods { get; init; } = [];
    public int PodMax { get; init; } = 3;

    /// <summary>The weight of a cab kind (0: the style never builds it).</summary>
    public double CabWeight(string kind)
    {
        foreach (var (k, w) in Cabs)
            if (k == kind)
                return w;
        return 0;
    }

    public static readonly Dictionary<string, BodyStyleRule> ByStyle = new(StringComparer.Ordinal)
    {
        // (each station after the first: a new language half the time; tall and short, hard ledges over the wheels)
        ["Bandit"] = new([(BodyLanguage.Slab, 2), (BodyLanguage.Ledge, 1.5), (BodyLanguage.Armour, 0.5)], Mix: 0.5)
            {
                Scales = [(BodyScale.Standard, 2), (BodyScale.Heavy, 1.5)], TallMin = 0.92f, TallMax = 1.25f, FlareShare = 1f, LineAmount = 1.2f, RunMin = 1, RunMax = 2, Contrast = "body", Ride = (0.8, 1, 1.4), DoorRear = 0.25,
                // (phase 3: open cabs in roll cages first, slabs of armour, upright screens, flat and grilled noses)
                Cabs = [("caged", 2.5), ("enclosed", 1), ("armoured", 1), ("crew", 0.7), ("cab-over", 0.4)], Noses = [('f', 1.5), ('w', 1), ('g', 2)],
                // (phase 4: welded plates and cage bars, cobbled bay by bay; a bare side now and then)
                Trims = [("p", 2), ("pc", 2), ("c", 1)], TrimMix = true, TrimShare = 0.7,
                // (phase 5: welded cages for rooms, a crow's-nest deck now and then, welded boxes hung on the flanks)
                Cage = 0.4, CageKind = 'w', Uppers = [("none", 3), ("deck", 1)], UpperShareMin = 0.3f, UpperShareMax = 0.6f, BulwarkMin = 1.05f, BulwarkMax = 1.2f, Pods = [('w', 0.35)],
                CabTallMin = 1.56f, CabTallMax = 1.7f, RakeMin = 0.02f, RakeMax = 0.1f, HoodCrownMin = 0.16f, HoodCrownMax = 0.2f, HoodHalfMin = 0.56f, HoodHalfMax = 0.62f, BeltMin = 1.04f, BeltMax = 1.12f, CabHalfMin = 0.86f, CabHalfMax = 0.9f,
            },
        // (low and wide, flat-faceted: little height, a glacis brow)
        ["Militia"] = new([(BodyLanguage.Armour, 1)])
            {
                Scales = [(BodyScale.Standard, 1), (BodyScale.Heavy, 1)], TallMin = 0.9f, TallMax = 1.1f, FlareShare = 0.9f, Brows = [(1.25f, 1), (1.5f, 1.5), (1f, 0.5)], Ride = (1.5, 1, 0.5), DoorRear = 0.35,
                // (armoured slit cabs and crew cabs, the driver inside an armoured capsule now and then; wedge noses like a glacis)
                Cabs = [("armoured", 2.5), ("crew", 1.2), ("merged", 1), ("caged", 0.4)], Noses = [('w', 2.5), ('f', 1)],
                // (phase 4: slat armour and stowage bins down the sides, a basket on the cab)
                Trims = [("s", 2), ("b", 1.5), ("sb", 1.5)], TrimShare = 0.9, Decos = [('B', 0.7)],
                // (phase 5: slatted frame rooms, an armoured fighting deck over the middle, armoured stowage pods)
                Cage = 0.3, CageKind = 's', Uppers = [("none", 2), ("deck", 1.3)], UpperShareMin = 0.5f, UpperShareMax = 0.9f, BulwarkMin = 1.1f, BulwarkMax = 1.25f, Pods = [('a', 0.3)], PodMax = 2,
                CabTallMin = 1.55f, CabTallMax = 1.65f, RakeMin = 0.18f, RakeMax = 0.24f, CapsuleRakeMin = 0.65f, CapsuleRakeMax = 0.8f, HoodCrownMin = 0.16f, HoodCrownMax = 0.19f, HoodHalfMin = 0.58f, HoodHalfMax = 0.62f, BeltMin = 1.08f, BeltMax = 1.12f, CabHalfMin = 0.9f, CabHalfMax = 0.94f,
            },
        // (long, low, even: tapered pods, smooth flares, the door kerbside)
        ["IT"] = new([(BodyLanguage.Pod, 2), (BodyLanguage.Van, 1.5)], 8, 12)
            {
                Scales = [(BodyScale.Standard, 2), (BodyScale.Heavy, 1)], TallMin = 0.92f, TallMax = 1.16f, FlareShare = 0.8f, Brows = [(1.25f, 1.5), (1.5f, 1.5), (1f, 1)], Tails = [(1f, 1), (1.25f, 1.5), (0.75f, 0.5)], DoorLeft = 0.3, Ride = (1.6, 1, 0.3), DoorRear = 0.35,
                // (capsules in the body's panels, raked screens, rounded and wedge noses)
                Cabs = [("enclosed", 1.5), ("cab-over", 1.2), ("crew", 0.9), ("merged", 0.8)], Noses = [('r', 2), ('w', 1.5)],
                // (phase 4: a smooth skirt fairing, a stripe and vents; a radome on the cab)
                Trims = [("f", 1)], Decos = [('R', 0.75)],
                // (phase 5: a short pod storey set in on the roof, smooth blisters on the flanks)
                Uppers = [("none", 2), ("saloon", 1.2)], UpperShareMin = 0.3f, UpperShareMax = 0.55f, UpperTallMin = 2.06f, UpperTallMax = 2.14f, UpperNarrow = 0.84f, Pods = [('b', 0.45)],
                CabTallMin = 1.66f, CabTallMax = 1.8f, RakeMin = 0.2f, RakeMax = 0.24f, CapsuleRakeMin = 0.6f, CapsuleRakeMax = 0.78f, HoodHalfMin = 0.5f, HoodHalfMax = 0.55f, BeltMin = 0.92f, BeltMax = 0.97f,
            },
        // (a portacabin on a chassis: boxy, square ends, tall)
        ["Scam"] = new([(BodyLanguage.Cabin, 2), (BodyLanguage.Van, 1)])
            {
                Scales = [(BodyScale.Standard, 2.5), (BodyScale.Heavy, 0.7)], TallMin = 0.95f, TallMax = 1.2f, FlareShare = 0.5f, Brows = [(1f, 3), (1.25f, 1)], Tails = [(1f, 3), (0.75f, 1)],
                // (boxy enclosed and crew cabs, upright screens, flat noses)
                Cabs = [("enclosed", 2), ("crew", 1.2), ("cab-over", 0.8)], Noses = [('f', 2), ('g', 1)],
                // (phase 4: signboards down the sides, one over the cab)
                Trims = [("g", 1)], TrimShare = 0.8, Decos = [('S', 0.9)],
                // (phase 5: a portacabin stacked on the portacabin, kiosks hung on the flanks)
                Uppers = [("none", 1.5), ("saloon", 1.3)], UpperShareMin = 0.5f, UpperShareMax = 1f, UpperTallMin = 2.1f, UpperTallMax = 2.25f, UpperNarrow = 0.97f, Pods = [('k', 0.35)], PodMax = 2,
                CabTallMin = 1.8f, CabTallMax = 1.95f, RakeMin = 0.03f, RakeMax = 0.08f, CapsuleRakeMin = 0.3f, CapsuleRakeMax = 0.42f, HoodCrownMin = 0.2f, HoodCrownMax = 0.24f, BeltMin = 1.0f, BeltMax = 1.04f, CabHalfMin = 0.92f, CabHalfMax = 0.95f,
            },
        // (long and low with a sheer line: boat-tail sterns, flared strakes)
        ["Highland"] = new([(BodyLanguage.Boat, 3), (BodyLanguage.Slab, 0.5)])
            {
                Scales = [(BodyScale.Standard, 2), (BodyScale.Heavy, 1)], TallMin = 0.9f, TallMax = 1.14f, FlareShare = 1f, Tails = [(1.25f, 1.5), (1f, 1), (0.75f, 0.5)],
                // (long hoods with grilles, enclosed and caged cabs)
                Cabs = [("enclosed", 2), ("crew", 1), ("caged", 0.5)], Noses = [('g', 2.5), ('r', 1)],
                // (phase 4: clinker laps and a wooden strake, a chrome grille and a bonnet mascot)
                Trims = [("r", 1)], Decos = [('O', 0.8), ('G', 0.75)],
                // (phase 5: a stock wagon of timber stakes, an open after deck behind a bulwark, clinker lockers)
                Cage = 0.3, CageKind = 't', Uppers = [("none", 2), ("deck", 1)], UpperShareMin = 0.4f, UpperShareMax = 0.7f, BulwarkMin = 0.95f, BulwarkMax = 1.1f, Pods = [('l', 0.35)],
                CabTallMin = 1.62f, CabTallMax = 1.74f, RakeMin = 0.08f, RakeMax = 0.16f, HoodCrownMin = 0.22f, HoodCrownMax = 0.26f, HoodHalfMin = 0.48f, HoodHalfMax = 0.54f, CabHalfMin = 0.86f, CabHalfMax = 0.9f, HoodReach = 0.1f,
            },
        // (tall, upright buses: the heavy class first, the door kerbside)
        ["British"] = new([(BodyLanguage.Bus, 2.5), (BodyLanguage.Coach, 1.5)], 8, 12)
            {
                Scales = [(BodyScale.Heavy, 2), (BodyScale.Standard, 1)], TallMin = 1f, TallMax = 1.28f, FlareShare = 0.7f, DoorLeft = 0.2, Ride = (1, 1.5, 0.3), DoorRear = 0,
                // (the bus front: the cab inside the body; capsules; rounded and grilled noses on the rest)
                Cabs = [("merged", 2), ("cab-over", 1.2), ("enclosed", 0.8), ("crew", 0.5)], Noses = [('r', 2), ('g', 1)],
                // (phase 4: the two-tone livery band and its chrome strips, a lit destination board over a bus front)
                Trims = [("l", 1)], Decos = [('D', 0.9)],
                // (phase 5: the double deck: an upper saloon flush with the walls, over most of the room)
                Uppers = [("saloon", 2.5), ("none", 1)], UpperShareMin = 0.7f, UpperShareMax = 1f, UpperTallMin = 2.06f, UpperTallMax = 2.16f, UpperNarrow = 1f,
                CabTallMin = 1.72f, CabTallMax = 1.86f, RakeMin = 0.1f, RakeMax = 0.18f, CapsuleRakeMin = 0.2f, CapsuleRakeMax = 0.38f, BeltMin = 0.94f, BeltMax = 1.0f,
            },
    };

    /// <summary>A style not named: military armour, faceted, one language.</summary>
    public static readonly BodyStyleRule Default = new([(BodyLanguage.Armour, 1)]);

    public static BodyStyleRule Of(FactionStyle st) => ByStyle.GetValueOrDefault(st.Name) ?? Default;

    /// <summary>A weighted pick from the languages.</summary>
    public BodyLanguage Pick(Dice d) => Weighted(d, Languages);

    public static T Weighted<T>(Dice d, IReadOnlyList<(T Item, double W)> items)
    {
        var roll = d.Next() * items.Sum(x => x.W);
        foreach (var (l, w) in items)
            if ((roll -= w) < 0)
                return l;
        return items[^1].Item;
    }
}

/// <summary>
/// One body's roll of its language: its scale class, height and width, how far its upper body overhangs, its roofline along the room, how far its brow and tail rake
/// and how long they run, which side its door is on, how many facets it draws with, and the sections that come of it. Where its style mixes, each station between two
/// bays (a room's front, each joint, its back) takes a language of its own, and a bay lofts from one to the next.
/// </summary>
public sealed class BodyLook
{
    public BodyLanguage Language { get; }
    public BodyScale Scale { get; }

    /// <summary>The ride height bands the chassis rolls by: the style's times the scale class's (<see cref="VehicleChassis.RideOf"/>).</summary>
    public (double Low, double Mid, double High) Ride => (_rule.Ride.Low * Scale.Ride.Low, _rule.Ride.Mid * Scale.Ride.Mid, _rule.Ride.High * Scale.Ride.High);
    public Roofline Line { get; }
    public float Height, Width, BrowRake, TailRake, BrowNarrow, TailNarrow, Flare, LineAmount, BrowLength, TailLength;
    public int Facets;

    /// <summary>+1: the door in the room's right side (+x), -1 its left.</summary>
    public float DoorSide = 1;

    /// <summary>Whether this look would put its door in the room's back (<see cref="BodyStyleRule.DoorRear"/>); the plan says whether it may (<see cref="RearDoor"/>).</summary>
    public bool WantsRearDoor;

    /// <summary>The room's door is in its tail's back (a doorway in its cap), set by the plan when the room is last and nothing stands behind it.</summary>
    public bool RearDoor;

    /// <summary>
    /// Phase 4: a merged cab stands in front of this room, its back the brow's front section, so the brow comes down little at its front (0.86 of the room's height
    /// at least; its width as rolled, or the plan reads as a box from above): the cab and the room read as one mass, not a cab a step below a tall room. Set by the
    /// plan with its cab.
    /// </summary>
    public bool MergedFront;

    /// <summary>
    /// Phase 5 (docs/vehiclekit/BODY-STYLES.md, section 11): an upper storey over bays <see cref="UpperPlan.From"/> to <see cref="UpperPlan.To"/> of the room, set by the
    /// plan (null: none). Those bays' roofs are cut level at <see cref="UpperPlan.Cut"/> and are its floor, with a hatch over the stair (<see cref="UpperPlan.Hatch"/>).
    /// </summary>
    public UpperPlan? Upper;

    /// <summary>Phase 5: the room is a frame of bars in this pattern (a <see cref="HullLoft.CageKinds"/> letter; 0: a skinned room), set by the plan.</summary>
    public char Cage;

    /// <summary>An upper storey as planned: its kind ("saloon", "deck"), the room's bays it stands over, the lower roof's cut, its own height or its bulwark's, and the stair.</summary>
    public sealed class UpperPlan(string kind, int from, int to, float cut, float height)
    {
        public string Kind { get; } = kind;
        public int From { get; } = from;
        public int To { get; } = to;
        public float Cut { get; } = cut;
        public float Height { get; } = height;
        public bool Open => Kind == "deck";

        /// <summary>The hatch in the floor (x across, z from the room's front; metres), set with the stair; null until then.</summary>
        public (float X0, float X1, float Z0, float Z1)? Hatch;

        /// <summary>The stair: its middle across the room, where its first tread starts (from the room's front), which way it climbs (+1 to the back), its key.</summary>
        public float StairX, StairZ, StairWay = 1;
        public HullLoft.StairShape? Stair;
    }

    /// <summary>A section cut level at height <paramref name="h"/> (an upper floor's roof, a bulwark's top): to the centimetre, with a level last row to the centreline.</summary>
    public static HullLoft.Section Cut(HullLoft.Section s, float h)
    {
        var pts = new List<Vector2>();
        for (var k = 0; k < s.P.Length; k++)
        {
            if (s.P[k].Y < h - 0.015f)
            {
                pts.Add(s.P[k]);
                continue;
            }
            var prev = pts[^1];
            var t = Math.Clamp((h - prev.Y) / MathF.Max(1e-4f, s.P[k].Y - prev.Y), 0, 1);
            var x = MathF.Round((prev.X + (s.P[k].X - prev.X) * t) * 100) / 100;
            if (x < 0.05f)
                break;
            pts.Add(new Vector2(x, MathF.Round(h * 100) / 100));
            break;
        }
        pts.Add(new Vector2(0, MathF.Round(h * 100) / 100));
        return Tidy(new HullLoft.Section(pts.ToArray()));
    }

    /// <summary>
    /// A section under an upper storey: cut level at <paramref name="cut"/>, its walls kept out to <see cref="UnderHalf"/> above a metre (a tumblehome, a gable or a dome
    /// would leave a floor too narrow for the hatch and the storey's feet: the lower storey's walls stand straight, the storey above takes the language's shape).
    /// </summary>
    public static HullLoft.Section UnderUpper(HullLoft.Section s, float cut)
    {
        var c = Cut(s, cut);
        return Tidy(new HullLoft.Section(c.P.Select((p, i) => i > 0 && i < c.P.Length - 1 && p.Y >= 1.0f ? p with { X = MathF.Max(p.X, UnderHalf) } : p).ToArray()));
    }

    public const float UnderHalf = 0.96f;

    /// <summary>An upper storey's section at a station over a lower wall whose top is <paramref name="cutX"/> out: the language's own at the storey's height and width (or its bulwark).</summary>
    public HullLoft.Section UpperSection(BodyLanguage l, float cutX, float narrow)
    {
        var t = l.Drawn(Facets);
        var s = Tidy(t.Scaled(cutX * narrow / t.P[0].X, (Upper!.Open ? 2.1f : Upper.Height) / t.Top));
        return Upper.Open ? Cut(s, Upper.Height) : s; // (a bulwark: the lower part of a storey's walls, upright as the language's are, cut at its height)
    }

    /// <summary>The upper storey's bays' keys, one over each of the run's bays (<see cref="UpperPlan.From"/> to <see cref="UpperPlan.To"/>), its ends closed.</summary>
    public string[] UpperNames(string[] bays, float narrow)
    {
        if (Upper is not { } up)
            return [];
        var lengths = Lengths(bays);
        var lower = Names(bays);
        var names = new List<string>();
        for (var k = up.From; k <= up.To; k++)
        {
            var turned = bays[k] == "Door" && DoorSide < 0 && !RearDoor;
            var sh = Built(lower[k], turned)!.Value;
            var (ua, ub) = Match(UpperSection(At(k), sh.A.P[^2].X, narrow), UpperSection(At(k + 1), sh.B.P[^2].X, narrow));
            // (over a left door bay, which stands turned round, the storey's bay is turned with it: its key back to front, so the two frames' sockets meet)
            names.Add(turned ? HullLoft.Key(lengths[k], false, k == up.To, k == up.From, ub, ua, false, up.Open ? "o" : "") : HullLoft.Key(lengths[k], false, k == up.From, k == up.To, ua, ub, false, up.Open ? "o" : ""));
        }
        return names.ToArray();
    }

    /// <summary>
    /// Where the overhang starts, above the room's floor: over the wheels' tops with room for their travel (measured over every chassis recipe on 2026-11-07:
    /// tyres stand at most 0.36 m over the floor, inside 1.1 m of half width). The hull sets it again from the real wheels (<see cref="Vehicle.Hull"/>).
    /// </summary>
    public float FlareFrom = FlareFloor;
    public const float FlareFloor = 0.6f;

    private readonly BodyStyleRule _rule;
    private readonly Dice _mix, _lengths;

    public BodyLook(BodyLanguage l, Dice d, BodyStyleRule? rule = null)
    {
        Language = l;
        _rule = rule ?? new BodyStyleRule([(l, 1)]);
        Height = (float)d.Between(l.HeightMin, l.HeightMax);
        Width = (float)d.Between(0.98, 1.02);
        BrowRake = (float)d.Between(l.BrowRakeMin, l.BrowRakeMax);
        TailRake = (float)d.Between(l.TailRakeMin, l.TailRakeMax);
        BrowNarrow = l.BrowNarrow * (float)d.Between(0.97, 1.0);
        TailNarrow = l.TailNarrow * (float)d.Between(0.97, 1.0);
        Facets = _rule.FacetsMax > 0 ? d.Fork("facets").Int(_rule.FacetsMin, _rule.FacetsMax) : 0;
        _mix = d.Fork("stations");
        // Phase 2's proportions, each on a fork of its own.
        var p = d.Fork("phase 2");
        Scale = BodyStyleRule.Weighted(p.Fork("scale"), _rule.Scales);
        Height = Math.Clamp(Height * (float)p.Fork("tall").Between(_rule.TallMin, _rule.TallMax), Scale.HeightMin, Scale.HeightMax);
        Flare = (float)p.Fork("flare").Between(Scale.FlareMin, Scale.FlareMax) * _rule.FlareShare;
        Line = l.Lines is { Length: > 0 } lines ? BodyStyleRule.Weighted(p.Fork("line"), lines) : Roofline.Plain;
        LineAmount = _rule.LineAmount * (float)p.Fork("line amount").Between(0.6, 1.0);
        BrowLength = BodyStyleRule.Weighted(p.Fork("brow"), _rule.Brows);
        TailLength = BodyStyleRule.Weighted(p.Fork("tail"), _rule.Tails);
        DoorSide = p.Fork("door").Chance(_rule.DoorLeft) ? -1 : 1;
        WantsRearDoor = p.Fork("rear door").Chance(_rule.DoorRear);
        _lengths = p.Fork("lengths");
    }

    /// <summary><c>--language NAME</c>: every kit-5 body in that language, whatever its style (to compare languages on one vehicle).</summary>
    public static BodyLanguage? Force;

    /// <summary><c>--roofline NAME</c> and <c>--scale NAME</c>: every kit-5 body with that line or at that scale (to compare them on one plan).</summary>
    public static Roofline? ForceLine;

    /// <summary>Phase 5: <c>--no-spaces</c> (no upper storeys, cages or pods), <c>--upper KIND</c>, <c>--cage</c>.</summary>
    public static bool NoSpaces, ForceCage;
    public static string? ForceUpper;
    public static BodyScale? ForceScale;

    /// <summary>A body's look from the style's languages, on the body layer's "language" fork.</summary>
    public static BodyLook Roll(Vehicle v)
    {
        var d = v.Dice.Fork("language");
        var rule = BodyStyleRule.Of(v.Style);
        if (Force is { } forced)
            rule = rule with { Languages = [(forced, 1.0)], Mix = 0 };
        if (ForceScale is { } scale)
            rule = rule with { Scales = [(scale, 1.0)] };
        var look = new BodyLook(rule.Pick(d), d.Fork("proportions"), rule);
        return ForceLine is { } line ? look.WithLine(line) : look;
    }

    private BodyLook(BodyLook o, Roofline line)
    {
        (Language, Scale, Line, _rule, _mix, _lengths) = (o.Language, o.Scale, line, o._rule, o._mix, o._lengths);
        (Height, Width, BrowRake, TailRake, BrowNarrow, TailNarrow, Flare, LineAmount, BrowLength, TailLength, Facets, DoorSide, FlareFrom) =
            (o.Height, o.Width, o.BrowRake, o.TailRake, o.BrowNarrow, o.TailNarrow, o.Flare, o.LineAmount, o.BrowLength, o.TailLength, o.Facets, o.DoorSide, o.FlareFrom);
        (WantsRearDoor, RearDoor, MergedFront, Upper, Cage) = (o.WantsRearDoor, o.RearDoor, o.MergedFront, o.Upper, o.Cage);
    }

    private BodyLook WithLine(Roofline line) => new(this, line);

    /// <summary>
    /// The sections a style's rooms may show from the front, for <see cref="VehicleIdentity"/>'s section part: each language's tall section at the style's height
    /// range, with and without its overhang and its taper, as <see cref="Section(BodyLanguage, string, int, float, float)"/> draws them (cached).
    /// </summary>
    public static IReadOnlyList<HullLoft.Section> References(BodyStyleRule rule)
    {
        lock (Refs)
        {
            if (Refs.TryGetValue(rule, out var have))
                return have;
            var list = new List<HullLoft.Section>();
            var facets = rule.FacetsMax > 0 ? (rule.FacetsMin + rule.FacetsMax) / 2 : 0;
            foreach (var (l, _) in rule.Languages)
                foreach (var (scale, _) in rule.Scales)
                    foreach (var tall in new[] { rule.TallMin, rule.TallMax })
                        foreach (var flare in new[] { scale.FlareMin, (scale.FlareMin + scale.FlareMax) / 2, scale.FlareMax })
                            foreach (var w in new[] { 1f, 0.9f })
                            {
                                var look = new BodyLook(l, new Dice(1), rule with { Scales = [(scale, 1)] });
                                look.Height = Math.Clamp((l.HeightMin + l.HeightMax) / 2 * tall, scale.HeightMin, scale.HeightMax);
                                (look.Flare, look.Facets) = (flare * rule.FlareShare, facets);
                                list.Add(look.Section(l, "T", 0, 1, w));
                            }
            return Refs[rule] = list;
        }
    }

    private static readonly Dictionary<BodyStyleRule, List<HullLoft.Section>> Refs = new(ReferenceEqualityComparer.Instance);

    /// <summary>Station <paramref name="i"/>'s language (0: the room's front): the look's own unless the style mixes, then, after the first, a new one now and then. No dice state: the same station always answers the same.</summary>
    public BodyLanguage At(int i)
    {
        if (_rule.Mix <= 0 || i <= 0)
            return Language;
        var prev = At(i - 1);
        var d = _mix.Fork("station " + i);
        return d.Chance(_rule.Mix) ? _rule.Pick(d) : prev;
    }

    /// <summary>The languages a room of <paramref name="stations"/> stations speaks, front to back, without repeats ("slab", "slab+ledge").</summary>
    public string Spoken(int stations) => string.Join("+", Enumerable.Range(0, stations).Select(At).Select(l => l.Name).Distinct());

    /// <summary>The section a plan's token stands for in this look's language: T tall, L low, N narrow; <paramref name="end"/> shrinks it for a brow (-1) or a tail (+1).</summary>
    public HullLoft.Section Section(string token, int end = 0) => Section(Language, token, end);

    /// <summary>
    /// The section a plan's token stands for in language <paramref name="l"/> at this look's proportions, at a station whose roofline factors are <paramref name="h"/>
    /// (height) and <paramref name="w"/> (the width above the furniture and the overhang).
    /// </summary>
    public HullLoft.Section Section(BodyLanguage l, string token, int end = 0, float h = 1, float w = 1, float minTop = 0)
    {
        var t = l.Drawn(Facets);
        var ky = Height * h / t.Top;
        var kx = Width;
        var steps = Line.Steps;
        if (token == "L")
            ky *= 1 - (1 - l.LowShare) * steps;
        else if (token == "N")
        {
            kx *= 1 - (1 - l.NarrowShare) * MathF.Min(1, steps);
            ky *= 1 - (1 - l.LowShare) * steps / 2;
        }
        var walkIn = Scale.WalkIn && l.WalkIn;
        if (end == 0 && walkIn)
            ky = MathF.Max(ky, 2.02f / t.Top); // (headroom at the centre: a player is 1.8 m)
        if (end < 0)
            (kx, ky) = (kx * BrowNarrow, ky * (MergedFront ? MathF.Max(BrowRake, 0.86f) : BrowRake));
        else if (end > 0)
            (kx, ky) = (kx * TailNarrow, ky * TailRake);
        if (walkIn)
            kx = MathF.Max(kx, 0.76f / t.P[0].X); // (a seat or a crate stands half a metre out by a brow or a tail: the fixed bays' narrowest end is 0.74 m)
        ky = MathF.Max(ky, minTop / t.Top); // (a door bay's doorway; a long brow's or tail's room where the check's rays start: Names)
        var s = t.Scaled(kx, ky);
        // The taper (the upper body only: the furniture's metre stays) and the overhang over the wheels, coming in over the language's blend.
        var flare = Flare * w * (token == "N" ? 0.5f : 1) * (end != 0 ? 0.6f : 1);
        if (flare > 0.005f || w < 0.999f)
            s = Overhang(s, flare, w, l.FlareBlend);
        if (end == 0 && token != "N" && walkIn)
            s = new HullLoft.Section(s.P.Select((p, i) => i == 0 || p.Y < 1.6f ? p with { X = MathF.Max(p.X, i == 0 ? 1.0f : 1.02f) } : p).ToArray()); // (furniture's metre)
        if (s.P.Any(p => p.X > Scale.HalfMax))
            s = new HullLoft.Section(s.P.Select(p => p with { X = MathF.Min(p.X, Scale.HalfMax) }).ToArray());
        return Tidy(s);
    }

    /// <summary>
    /// Moves the side out by <paramref name="flare"/> above <see cref="FlareFrom"/> (eased in over <paramref name="blend"/>) and narrows what is above 1.6 m by
    /// <paramref name="w"/> (a taper toward the room's ends), with a point at the overhang's foot so the wall under it stays upright; to the centimetre.
    /// </summary>
    private HullLoft.Section Overhang(HullLoft.Section s, float flare, float w, float blend)
    {
        var pts = s.P.ToList();
        void Split(float y)
        {
            for (var k = 0; k + 1 < pts.Count; k++)
                if (pts[k].Y < y - 0.03f && pts[k + 1].Y > y + 0.03f && pts.Count < HullLoft.MaxPoints)
                {
                    var t = (y - pts[k].Y) / (pts[k + 1].Y - pts[k].Y);
                    pts.Insert(k + 1, new Vector2(MathF.Round((pts[k].X + (pts[k + 1].X - pts[k].X) * t) * 100) / 100, MathF.Round(y * 100) / 100));
                    return;
                }
        }
        if (flare > 0.005f)
        {
            Split(FlareFrom);
            if (blend > 0.1f)
                Split(FlareFrom + blend);
        }
        var top = pts[^1].Y;
        for (var k = 1; k < pts.Count; k++)
        {
            var p = pts[k];
            if (p.X <= 0.001f)
                continue; // (the centreline stays)
            var u = blend <= 0.001f ? (p.Y > FlareFrom + 0.001f ? 1 : 0) : Math.Clamp((p.Y - FlareFrom) / blend, 0, 1);
            var ease = u * u * (3 - 2 * u);
            var x = p.X + flare * ease;
            if (p.Y > 1.6f)
                x *= w;
            pts[k] = new Vector2(MathF.Round(x * 100) / 100, p.Y);
        }
        _ = top;
        return new HullLoft.Section(pts.ToArray());
    }

    /// <summary>Drops points the centimetre grid made repeats of (a smooth curve's close points), keeping the floor edge and the centreline.</summary>
    private static HullLoft.Section Tidy(HullLoft.Section s)
    {
        var list = new List<Vector2> { s.P[0] };
        for (var i = 1; i < s.P.Length; i++)
            if (Vector2.Distance(s.P[i], list[^1]) >= 0.015f || i == s.P.Length - 1)
            {
                if (i == s.P.Length - 1 && Vector2.Distance(s.P[i], list[^1]) < 0.015f && list.Count > 1)
                    list[^1] = s.P[i];
                else
                    list.Add(s.P[i]);
            }
        return list.Count == s.P.Length ? s : new HullLoft.Section(list.ToArray());
    }

    /// <summary>
    /// Two sections with as many points each, for a bay that lofts from one to the other: the one with fewer gets its longest side rows split in half (collinear points,
    /// so its shape doesn't change). The roof's last row is never split, so a flat roof stays one panel.
    /// </summary>
    public static (HullLoft.Section A, HullLoft.Section B) Match(HullLoft.Section a, HullLoft.Section b)
    {
        static HullLoft.Section Grow(HullLoft.Section s, int n)
        {
            var pts = s.P.ToList();
            while (pts.Count < n)
            {
                var (best, len) = (0, -1f);
                for (var k = 0; k + 2 < pts.Count; k++) // (not the last row)
                    if (Vector2.Distance(pts[k], pts[k + 1]) > len)
                        (best, len) = (k, Vector2.Distance(pts[k], pts[k + 1]));
                var mid = (pts[best] + pts[best + 1]) / 2;
                pts.Insert(best + 1, new Vector2(MathF.Round(mid.X * 100) / 100, MathF.Round(mid.Y * 100) / 100));
            }
            return new HullLoft.Section(pts.ToArray());
        }
        var m = Math.Max(a.P.Length, b.P.Length);
        return (a.P.Length < m ? Grow(a, m) : a, b.P.Length < m ? Grow(b, m) : b);
    }

    /// <summary>
    /// The bays' lengths, front to back, for a plan of <paramref name="bays"/>: the brow and the tail as the seed rolled them (a long brow is a wedge), the door bay 1.5 m,
    /// the metre bays taking up the difference in quarters within the scale's range, so the room keeps its planned length (3.5 m and whole metres). No dice state.
    /// </summary>
    public float[] Lengths(string[] bays)
    {
        var n = bays.Length;
        var len = bays.Select(b => b == "Door" ? 1.5f : 1f).ToArray();
        var ones = Enumerable.Range(1, Math.Max(0, n - 2)).Where(k => bays[k] != "Door").ToList();
        float Lo() => MathF.Max(0.75f, Scale.BayMin);
        float Hi() => MathF.Min(1.5f, Scale.BayMax);
        var (brow, tail) = ones.Count > 0 ? (BrowLength, TailLength) : (1f, 1f); // (a short room's walls are its brow and tail: a wedge there leaves no wall for its loot)
        for (var tries = 0; tries < 6; tries++)
        {
            var extra = (brow - 1) + (tail - 1); // (what the metre bays give back, in quarters)
            var room = ones.Sum(k => extra > 0 ? 1 - Lo() : Hi() - 1);
            if (MathF.Abs(extra) <= room + 1e-4f)
                break;
            (brow, tail) = (brow > 1 ? brow - 0.25f : brow < 1 ? brow + 0.25f : brow, tail > 1 ? tail - 0.25f : tail < 1 ? tail + 0.25f : tail);
        }
        len[0] = brow;
        len[^1] = tail;
        var give = (brow - 1) + (tail - 1);
        var d = _lengths.Fork("bays " + n);
        foreach (var k in d.Shuffled(ones).Concat(ones).Concat(ones)) // (a quarter at a time, round them)
        {
            if (MathF.Abs(give) < 1e-4f)
                break;
            var step = give > 0 ? -0.25f : 0.25f;
            if (len[k] + step < Lo() - 1e-4f || len[k] + step > Hi() + 1e-4f)
                continue;
            len[k] += step;
            give += step;
        }
        if (MathF.Abs(give) > 1e-4f)
            (len[0], len[^1]) = (1, 1); // (no room for them: the plan's own lengths)
        if (MathF.Abs(give) > 1e-4f)
            for (var k = 1; k < n - 1; k++)
                len[k] = bays[k] == "Door" ? 1.5f : 1f;
        // A rhythm along a long room: one bay a quarter longer, the next a quarter shorter, now and then (the heavy classes' longer bays).
        if (ones.Count >= 2 && Hi() > 1.01f && d.Chance(0.5))
        {
            var (a, b) = (d.Pick(ones), d.Pick(ones));
            if (a != b && len[a] + 0.25f <= Hi() + 1e-4f && len[b] - 0.25f >= Lo() - 1e-4f)
                (len[a], len[b]) = (len[a] + 0.25f, len[b] - 0.25f);
        }
        return len;
    }

    /// <summary>Each station's roofline factors (height, width), front to back, for bays of <paramref name="lengths"/>; the door bay's two stations level (its frame and a turret's roof).</summary>
    private (float H, float W)[] Stations(string[] bays, float[] lengths)
    {
        var total = lengths.Sum();
        var at = new (float H, float W)[bays.Length + 1];
        var z = 0f;
        for (var k = 0; k <= bays.Length; k++)
        {
            at[k] = Line.At(z / total, LineAmount);
            if (k < bays.Length)
                z += lengths[k];
        }
        var door = Array.IndexOf(bays, "Door");
        if (door >= 0)
            at[door] = at[door + 1] = (MathF.Max(at[door].H, at[door + 1].H), MathF.Max(at[door].W, at[door + 1].W));
        return at;
    }

    /// <summary>A tail's back section widened where a back doorway needs it: out to the jamb and its frame from the floor to the lintel (HullLoft.RearJamb).</summary>
    private static HullLoft.Section RearFrame(HullLoft.Section s)
    {
        const float least = HullLoft.RearJamb + 0.15f, lintel = 2.06f;
        var pts = s.P.ToList();
        var k = pts.FindIndex(p => p.Y > lintel);
        if (k > 0 && pts[k].Y - lintel > 0.02f && lintel - pts[k - 1].Y > 0.02f && pts.Count < HullLoft.MaxPoints)
            pts.Insert(k, new Vector2(MathF.Max(least, s.XAt(lintel)), lintel)); // (a point at the lintel, so the side reaches the frame up to it)
        return new HullLoft.Section(pts.Select((p, i) => i < pts.Count - 1 && p.Y <= lintel + 0.001f ? p with { X = MathF.Round(MathF.Max(p.X, least) * 100) / 100 } : p).ToArray());
    }

    /// <summary>A door bay's least height (its doorway is 2 m: HullLoft wants 2.1 m), and a brow's or a tail's where the room check's rays start.</summary>
    private const float DoorTop = 2.14f, EndRays = 1.68f;

    /// <summary>A plan's bay names ("Brow T", "Door", "Bay T-L", "Tail L") as lofted parts' keys, front to back; bay k lofts from station k to station k + 1.</summary>
    public string[] Names(string[] bays)
    {
        var names = NamesUncut(bays);
        if (Upper is not { } up && Cage == '\0')
            return names;
        var lengths = Lengths(bays);
        var z = 0f;
        for (var k = 0; k < names.Length; k++)
        {
            var sh = HullLoft.ShapeOf(names[k])!.Value;
            if (Cage != '\0')
                names[k] = HullLoft.Key(sh.Length, sh.Door, sh.Front, sh.Back, sh.A, sh.B, sh.BackDoor, "c" + Cage);
            else if (Upper is { } u && k >= u.From && k <= u.To)
            {
                // (phase 5: under an upper storey the roof is cut level and is its floor; the hatch where it crosses this bay, in the bay's own frame: a left door bay is turned round)
                var (ca, cb) = Match(UnderUpper(sh.A, u.Cut), UnderUpper(sh.B, u.Cut));
                (float, float, float, float)? hatch = null;
                if (u.Hatch is { } h && h.Z1 > z + 0.02f && h.Z0 < z + lengths[k] - 0.02f)
                {
                    var (hz0, hz1) = (MathF.Max(0, h.Z0 - z), MathF.Min(lengths[k], h.Z1 - z));
                    var turned = bays[k] == "Door" && DoorSide < 0 && !RearDoor;
                    hatch = turned ? (-h.X1, -h.X0, lengths[k] - hz1, lengths[k] - hz0) : (h.X0, h.X1, hz0, hz1);
                    if (hatch.Value.Item4 - hatch.Value.Item3 < 0.3f)
                        hatch = null; // (a sliver: the stair's top is in the next bay)
                }
                names[k] = HullLoft.Key(sh.Length, sh.Door, sh.Front, sh.Back, ca, cb, sh.BackDoor, HullLoft.HatchWord(hatch));
            }
            z += lengths[k];
        }
        return names;
    }

    /// <summary>The bays' keys before phase 5's cuts and cages.</summary>
    private string[] NamesUncut(string[] bays)
    {
        var lengths = Lengths(bays);
        var st = Stations(bays, lengths);
        var names = new string[bays.Length];
        var up = Upper;
        float M(int station, float least = 0) => up != null && station >= up.From && station <= up.To + 1 ? MathF.Max(least, up.Cut + 0.08f) : least; // (phase 5: tall enough to be cut level under an upper storey)
        for (var k = 0; k < bays.Length; k++)
        {
            var b = bays[k];
            if (b == "Door" && RearDoor)
            {
                // (the door is in the back: this bay is a plain tall one, level, 1.5 m, as the plan laid it out)
                var (ra, rb) = Match(Section(At(k), "T", 0, st[k].H, st[k].W, M(k, DoorTop)), Section(At(k + 1), "T", 0, st[k + 1].H, st[k + 1].W, M(k + 1, DoorTop)));
                names[k] = HullLoft.Key(lengths[k], false, false, false, ra, rb);
                continue;
            }
            if (b == "Door")
            {
                var (ta, tb) = Match(Section(At(k), "T", 0, st[k].H, st[k].W, M(k, DoorTop)), Section(At(k + 1), "T", 0, st[k + 1].H, st[k + 1].W, M(k + 1, DoorTop)));
                // (a door on the left: the right-hand door bay turned round, so its front section is the room's back one: Vehicle.Hull stands it by its back right foot)
                names[k] = DoorSide > 0 ? HullLoft.Key(1.5f, true, false, false, ta, tb) : HullLoft.Key(1.5f, true, false, false, tb, ta);
                continue;
            }
            var parts = b.Split(' ');
            var tokens = parts[1].Split('-');
            var (front, back) = (tokens[0], tokens[^1]);
            // A brow or a tail is as low at its end as its rake says, but no lower than keeps a room's height where the check's rays start (0.35 m in from the
            // front, 0.3 m from the back, 1.6 m up: VehicleChecks.Gaps): a long wedge would put its roof under them (Militia War Bus 2, phase 2).
            static float Least(float far, float at, float len) => (EndRays - far * (at / len)) / (1 - at / len);
            var (fa, fb) = parts[0] switch
            {
                "Brow" => Section(At(1), front, 0, st[1].H, st[1].W, M(1)) is var bb ? (Section(At(1), front, -1, st[0].H, st[0].W, Least(bb.Top, 0.35f, lengths[k])), bb) : default,
                "Tail" when RearDoor => Section(At(k), "T", 0, st[k].H, st[k].W, M(k, DoorTop)) is var td ? (td, RearFrame(Section(At(k), "T", 1, st[k + 1].H, st[k + 1].W, DoorTop + 0.02f))) : default, // (a doorway in its back: tall and wide enough at its end)
                "Tail" => Section(At(k), back, 0, st[k].H, st[k].W, M(k)) is var tf ? (tf, Section(At(k), back, 1, st[k + 1].H, st[k + 1].W, Least(tf.Top, 0.3f, lengths[k]))) : default,
                _ => (Section(At(k), front, 0, st[k].H, st[k].W, M(k)), Section(At(k + 1), back, 0, st[k + 1].H, st[k + 1].W, M(k + 1))),
            };
            var (ma, mb) = Match(fa, fb);
            names[k] = HullLoft.Key(lengths[k], false, parts[0] == "Brow", parts[0] == "Tail", ma, mb, parts[0] == "Tail" && RearDoor);
        }
        return names;
    }

    /// <summary>A plan's bay k as built: its key's shape with the room's own front first (a left door bay's key is turned round: <see cref="Names"/>).</summary>
    public static HullLoft.Shape? Built(string name, bool turned) =>
        HullLoft.ShapeOf(name) is { } s ? turned ? s with { A = s.B, B = s.A } : s : null;

    public string Label => string.Create(CultureInfo.InvariantCulture,
        $"{Language.Name} {Scale.Name} {Height:0.00} m, flare {Flare:0.00}, {Line.Name} {LineAmount:0.0}, brow {BrowRake:0.00} {BrowLength:0.##} m, tail {TailRake:0.00} {TailLength:0.##} m, door {(RearDoor ? "rear" : DoorSide > 0 ? "right" : "left")}{(Facets > 0 ? $", {Facets} facets" : "")}");
}

public sealed partial class Vehicle
{
    /// <summary>
    /// A room's bays in paint runs (<see cref="BodyStyleRule.RunMin"/>): a run ends where the roofline steps (a "Bay T-L"), where a mixing style's language changes,
    /// or when it has run its rolled length. Kit 5 only; a fixed-bay room is one run a bay.
    /// </summary>
    public List<List<VPart>> PaintRuns(Room room, Dice d)
    {
        var runs = new List<List<VPart>>();
        var rule = BodyStyleRule.Of(Style);
        var want = d.Int(rule.RunMin, rule.RunMax);
        for (var k = 0; k < room.Bays.Count; k++)
        {
            var step = k > 0 && (room.Plan[k - 1].Contains('-') || Look != null && Look.At(k) != Look.At(k - 1) || room.Plan[k - 1].StartsWith("Brow", StringComparison.Ordinal) != room.Plan[k].StartsWith("Brow", StringComparison.Ordinal) && d.Chance(0.5));
            if (runs.Count == 0 || step || runs[^1].Count >= want || Look == null)
            {
                runs.Add(new List<VPart>());
                want = d.Int(rule.RunMin, rule.RunMax);
            }
            runs[^1].Add(room.Bays[k]);
        }
        return runs;
    }

    /// <summary>
    /// Paints each room by its runs (<see cref="PaintRuns"/>), each a colour of <paramref name="role"/> unlike the run before it (else the style's contrast role), so a
    /// room's sections read from a distance; <paramref name="pick"/> may turn a run's colour (the wear's rust), and is asked again when it gives the neighbour's.
    /// </summary>
    public void PaintRooms(Dice d, string role, Func<Dice, string, string>? pick = null)
    {
        var rule = BodyStyleRule.Of(Style);
        foreach (var room in Rooms)
        {
            if (room.Bays.Count == 0 || Look == null)
                continue;
            var prev = "";
            var i = 0;
            foreach (var run in PaintRuns(room, d.Fork("runs " + room.Tag)))
            {
                var colour = "";
                var own = rule.Contrast != null && i++ % 2 == 1 && d.Chance(0.7) ? rule.Contrast : role; // (a contrasting style: every other run in its contrast role, mostly)
                for (var tries = 0; tries < 6 && (colour.Length == 0 || Near(colour, prev)); tries++)
                {
                    colour = Style.Colour(d, tries >= 2 && rule.Contrast != null ? rule.Contrast : own);
                    if (pick != null && (tries < 3 || rule.Contrast == null))
                        colour = pick(d, colour); // (the wear's rust; after a few tries a contrasting style keeps its contrast instead)
                }
                foreach (var b in run)
                    Paint(b, colour);
                prev = colour;
            }
            PaintUpper(room); // (phase 5: an upper storey as the bays it stands on)
        }
    }

    /// <summary>Two "#rrggbb" colours that read as one from a distance: no channel 40 apart (two of a style's rust browns).</summary>
    private static bool Near(string a, string b)
    {
        if (a.Length != 7 || b.Length != 7)
            return a == b;
        for (var k = 1; k < 7; k += 2)
            if (Math.Abs(Convert.ToInt32(a.Substring(k, 2), 16) - Convert.ToInt32(b.Substring(k, 2), 16)) >= 40)
                return false;
        return true;
    }

    /// <summary>Kit 5: the room's body language and this seed's roll of it (<see cref="BodyLook"/>); null: the fixed hull bays of kits 1 to 4.</summary>
    public BodyLook? Look { get; set; }

    /// <summary>The construct as built so far, for a measure (the report below).</summary>
    internal Construct Built => C;
}

/// <summary>
/// <c>--body-report Style [--seeds N] [--from S]</c>: what the rooms are made of and how their cross-sections differ (docs/vehiclekit/BODY-STYLES.md, "Measuring"):
/// distinct sections and bays, and a front-view distance the silhouette term can't see, within the style and against the other styles' rooms in the same run.
/// </summary>
public static class BodyReportCli
{
    private const int Bins = FrontProfile.Bins;

    /// <summary>A room's cross-section from the front (<see cref="FrontProfile"/>).</summary>
    private static float[] Front(Vehicle v)
    {
        var (tris, owner) = VehiclePlan.PartTriangles(v.Built);
        return FrontProfile.OfVehicle(v, tris, owner).Room;
    }

    private static float Dist(float[] a, float[] b) => FrontProfile.Dist(a, b);

    /// <summary>Coplanar faces inside one lofted bay (its own facets against each other), as --check-parts would find in a part file.</summary>
    private static int OwnFights(ConstructPartDef d, out string first)
    {
        var boxes = ZFight.Boxes(d);
        var hits = ZFight.Find(boxes);
        first = hits.Count > 0 ? ZFight.Describe(boxes, hits[0]) : "";
        return hits.Count;
    }

    public static int Run(string styleName, string[] args)
    {
        string? Arg(string k) => args.SkipWhile(a => !a.Equals(k, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var seeds = int.TryParse(Arg("--seeds"), out var n) ? n : 6;
        var from = int.TryParse(Arg("--from"), out var f) ? f : 1;
        var styles = styleName.Equals("all", StringComparison.OrdinalIgnoreCase) ? Styles.All.ToArray() : Styles.All.Where(s => s.Name.Equals(styleName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (styles.Length == 0)
        {
            Console.WriteLine($"--body-report: no style {styleName}");
            return 1;
        }
        var fronts = new List<(string Style, float[] F)>();
        var cabs = new List<(string Style, float[] F, float[] S)>(); // (phase 3: the cab and hood, front and side)
        var exteriors = new List<(string Style, float[] Room, float[] Relief, float[] Body, float[] Side)>(); // (phase 4: the rooms with what is on them, their relief, the whole body)
        var trims = new Dictionary<string, (int Vehicles, int Trims, int Fittings)>(StringComparer.Ordinal);
        var failures = 0;
        var fights = 0;
        foreach (var st in styles)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var sections = new HashSet<string>(StringComparer.Ordinal);
            var langs = new Dictionary<string, int>(StringComparer.Ordinal);
            var mine = new List<float[]>();
            var ms = new List<double>();
            var parts = new List<int>();
            var facets = new List<int>();
            var traits = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal); // (phase 2: scale, roofline, door, bay lengths)
            var heights = new List<float>();
            var widths = new List<float>();
            void Tally(string what, string value)
            {
                if (!traits.TryGetValue(what, out var t))
                    traits[what] = t = new Dictionary<string, int>(StringComparer.Ordinal);
                t[value] = t.GetValueOrDefault(value) + 1;
            }
            foreach (var g in VehicleRegistry.For(st, false).Where(g => ShapeGrammar.RecipeOf(g) != null))
                for (var seed = from; seed < from + seeds; seed++)
                {
                    try
                    {
                        var (_, v, _, t) = VehicleDiversity.Generate(g, seed);
                        ms.Add(t);
                        parts.Add(v.Built.PartCount);
                        foreach (var b in v.Rooms.SelectMany(r => r.Bays))
                        {
                            if (keys.Add(b.Def.Name) && OwnFights(b.Def, out var first) is var own and > 0)
                            {
                                fights += own;
                                Console.WriteLine($"  {g.Name} {seed}: {own} coplanar pair(s) inside {b.Def.Name}: {first}");
                            }
                            if (HullLoft.ShapeOf(b.Def.Name) is { } sh)
                            {
                                sections.Add(sh.A.Key());
                                sections.Add(sh.B.Key());
                                facets.Add(sh.A.P.Length - 1);
                            }
                            else
                                sections.Add(b.Def.Name);
                        }
                        foreach (var cp in new[] { "cabshell", "hood", "glass" }.SelectMany(t => v.Panels.GetValueOrDefault(t) ?? []))
                            if (keys.Add(cp.Def.Name) && OwnFights(cp.Def, out var cf) is var cown and > 0)
                            {
                                fights += cown;
                                Console.WriteLine($"  {g.Name} {seed}: {cown} coplanar pair(s) inside {cp.Def.Name}: {cf}");
                            }
                        {
                            var (ct, co) = VehiclePlan.PartTriangles(v.Built);
                            var (cfr, csd) = FrontProfile.OfCab(v, ct, co);
                            cabs.Add((st.Name, cfr, csd));
                            var (er, erl, es) = FrontProfile.Exterior(v, ct, co);
                            exteriors.Add((st.Name, er, erl, FrontProfile.OfVehicle(v, ct, co).Body, es));
                            var tr = trims.GetValueOrDefault(st.Name);
                            trims[st.Name] = (tr.Vehicles + 1, tr.Trims + (v.Panels.GetValueOrDefault("trim")?.Count ?? 0), tr.Fittings + (v.Panels.GetValueOrDefault("fittings")?.Count ?? 0));
                            foreach (var tp in (v.Panels.GetValueOrDefault("trim") ?? []).Concat(v.Panels.GetValueOrDefault("fittings") ?? []))
                                if (keys.Add(tp.Def.Name) && OwnFights(tp.Def, out var tf) is var town and > 0)
                                {
                                    fights += town;
                                    Console.WriteLine($"  {g.Name} {seed}: {town} coplanar pair(s) inside {tp.Def.Name}: {tf}");
                                }
                            foreach (var m in v.Modules.Where(m => m.StartsWith("trim ", StringComparison.Ordinal) || m.StartsWith("fitting ", StringComparison.Ordinal)))
                                Tally(m.StartsWith("trim ", StringComparison.Ordinal) ? "trim" : "fitting", m.StartsWith("trim ", StringComparison.Ordinal) ? m[5..m.IndexOf(" on ", StringComparison.Ordinal)] : m[8..]);
                            Tally("cab", v.CabShape is { } cs ? CabLook.KindName(cs.Kind) + (v.HoodShape is { } hs ? " + " + CabLook.NoseName(hs.Nose) + " hood" : "") : (v.Panels.GetValueOrDefault("cabshell")?.FirstOrDefault()?.Def.Name ?? "none"));
                        }
                        var lang = v.Look?.Language.Name ?? "fixed bays";
                        langs[lang] = langs.GetValueOrDefault(lang) + 1;
                        if (v.Look is { } look && v.Rooms.Count > 0)
                        {
                            Tally("scale", look.Scale.Name);
                            Tally("line", look.Line.Name);
                            Tally("door", look.RearDoor ? "rear" : look.DoorSide > 0 ? "right" : "left");
                            Tally("bays", string.Join(" ", look.Lengths(v.Rooms[0].Plan.ToArray()).Select(x => x.ToString("0.##", CultureInfo.InvariantCulture))));
                            var shapes = v.Rooms[0].Bays.Select(b => HullLoft.ShapeOf(b.Def.Name)).Where(x => x != null).Select(x => x!.Value).ToList();
                            if (shapes.Count > 0)
                            {
                                heights.Add(shapes.Max(x => MathF.Max(x.A.Top, x.B.Top)));
                                widths.Add(shapes.Max(x => x.A.P.Concat(x.B.P).Max(p => p.X)));
                            }
                        }
                        if (v.Rooms.Count > 0)
                        {
                            var fr = Front(v);
                            mine.Add(fr);
                            fronts.Add((st.Name, fr));
                        }
                    }
                    catch (Exception e) when (e is VehicleKitException or ConstructPartException)
                    {
                        failures++;
                        Console.WriteLine($"  {g.Name} {seed}: {e.Message}");
                    }
                }
            var pairs = new List<float>();
            for (var i = 0; i < mine.Count; i++)
                for (var j = i + 1; j < mine.Count; j++)
                    pairs.Add(Dist(mine[i], mine[j]));
            Console.WriteLine($"{st.Name}: {ms.Count} vehicles, {mine.Count} with rooms; bay parts {keys.Count} distinct, sections {sections.Count} distinct; languages {string.Join(", ", langs.Select(x => $"{x.Key} {x.Value}"))}");
            Console.WriteLine($"  facets a side {(facets.Count > 0 ? $"{facets.Min()} to {facets.Max()}, mean {facets.Average():0.0}" : "-")}");
            if (heights.Count > 0)
                Console.WriteLine($"  room height {heights.Min():0.00} to {heights.Max():0.00} m (mean {heights.Average():0.00}), half width at its widest {widths.Min():0.00} to {widths.Max():0.00} m (mean {widths.Average():0.00})");
            foreach (var (what, t) in traits.Where(x => x.Key != "bays"))
                Console.WriteLine($"  {what}: {string.Join(", ", t.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}"))}");
            if (traits.TryGetValue("bays", out var bl))
                Console.WriteLine($"  bay lengths: {bl.Count} different runs over {bl.Values.Sum()} rooms; commonest {bl.OrderByDescending(x => x.Value).First().Key} ({bl.Values.Max()})");
            Console.WriteLine($"  front profile: mean pair {(pairs.Count > 0 ? pairs.Average() : 0):0.000}; generation mean {(ms.Count > 0 ? ms.Average() : 0):0} ms, worst {(ms.Count > 0 ? ms.Max() : 0):0} ms; parts mean {(parts.Count > 0 ? parts.Average() : 0):0}");
            if (mine.Count > 0)
                Console.WriteLine("  mean front: " + string.Join(" ", Enumerable.Range(0, Bins).Select(b => mine.Average(x => x[b]).ToString("0.00", CultureInfo.InvariantCulture))));
        }
        if (styles.Length > 1)
        {
            Console.WriteLine("front profile between styles (mean pair; the diagonal is within the style):");
            var names = styles.Select(s => s.Name).ToArray();
            var sb = new StringBuilder("            ");
            foreach (var b in names)
                sb.Append($"{b,10}");
            Console.WriteLine(sb.ToString());
            foreach (var a in names)
            {
                sb.Clear().Append($"{a,-12}");
                foreach (var b in names)
                {
                    var d = (from x in fronts where x.Style == a from y in fronts where y.Style == b && !ReferenceEquals(x.F, y.F) select Dist(x.F, y.F)).DefaultIfEmpty(0).Average();
                    sb.Append($"{d,10:0.000}");
                }
                Console.WriteLine(sb.ToString());
            }
        }
        if (styles.Length > 1 && cabs.Count > 0)
        {
            // Phase 3: how far apart the styles' cabs and hoods read (front and side outline, mean of the two), the diagonal within a style.
            Console.WriteLine("cab and hood (front and side outline) between styles (mean pair; the diagonal is within the style):");
            var names = styles.Select(s => s.Name).ToArray();
            var sb = new StringBuilder("            ");
            foreach (var b in names)
                sb.Append($"{b,10}");
            Console.WriteLine(sb.ToString());
            var (within, between) = (new List<float>(), new List<float>());
            foreach (var a in names)
            {
                sb.Clear().Append($"{a,-12}");
                foreach (var b in names)
                {
                    var ds = (from x in cabs where x.Style == a from y in cabs where y.Style == b && !ReferenceEquals(x.F, y.F) select (Dist(x.F, y.F) + Dist(x.S, y.S)) / 2).ToList();
                    (a == b ? within : between).AddRange(ds);
                    sb.Append($"{(ds.Count > 0 ? ds.Average() : 0),10:0.000}");
                }
                Console.WriteLine(sb.ToString());
            }
            Console.WriteLine($"cab separation: within a style {(within.Count > 0 ? within.Average() : 0):0.000}, between styles {(between.Count > 0 ? between.Average() : 0):0.000}, ratio {(within.Count > 0 && within.Average() > 0 ? between.Average() / within.Average() : 0):0.00}");
        }
        if (styles.Length > 1 && exteriors.Count > 0)
        {
            // Phase 4: how far apart the styles' exteriors read, whatever their paint: the rooms with what is on them (front profile and relief, the mean of the
            // two) and the whole body (front profile and side outline), each a matrix with the style's own on the diagonal, then between over within.
            var names = styles.Select(s => s.Name).ToArray();
            void Matrix(string title, Func<int, int, float> dist)
            {
                Console.WriteLine(title + " between styles (mean pair; the diagonal is within the style):");
                var sb = new StringBuilder("            ");
                foreach (var b in names)
                    sb.Append($"{b,10}");
                Console.WriteLine(sb.ToString());
                var (within, between) = (new List<float>(), new List<float>());
                var nearest = new List<string>();
                foreach (var a in names)
                {
                    sb.Clear().Append($"{a,-12}");
                    var row = new Dictionary<string, float>();
                    foreach (var b in names)
                    {
                        var ds = new List<float>();
                        for (var i = 0; i < exteriors.Count; i++)
                            for (var j = 0; j < exteriors.Count; j++)
                                if (i != j && exteriors[i].Style == a && exteriors[j].Style == b && dist(i, j) is var dd && !float.IsNaN(dd))
                                    ds.Add(dd);
                        (a == b ? within : between).AddRange(ds);
                        row[b] = ds.Count > 0 ? ds.Average() : 0;
                        sb.Append($"{row[b],10:0.000}");
                    }
                    Console.WriteLine(sb.ToString());
                    var own = row[a];
                    var other = row.Where(x => x.Key != a).OrderBy(x => x.Value).First();
                    nearest.Add($"{a} {other.Key} {(own > 0 ? other.Value / own : 0):0.00}");
                }
                Console.WriteLine($"  within a style {(within.Count > 0 ? within.Average() : 0):0.000}, between styles {(between.Count > 0 ? between.Average() : 0):0.000}, ratio {(within.Count > 0 && within.Average() > 0 ? between.Average() / within.Average() : 0):0.00}; nearest other style (its distance over the style's own): {string.Join(", ", nearest)}");
            }
            static float Rooms((string, float[] Room, float[] Relief, float[], float[]) x, (string, float[] Room, float[] Relief, float[], float[]) y) =>
                x.Room.Sum() == 0 || y.Room.Sum() == 0 ? float.NaN : (FrontProfile.Dist(x.Room, y.Room) + FrontProfile.Dist(x.Relief, y.Relief)) / 2;
            var withRooms = exteriors.Select((x, i) => (x, i)).Where(t => t.x.Room.Sum() > 0).Select(t => t.i).ToHashSet();
            Matrix("room exteriors (front with trim, and relief)", (i, j) => withRooms.Contains(i) && withRooms.Contains(j) ? Rooms(exteriors[i], exteriors[j]) : float.NaN);
            Matrix("whole body (front and side outline)", (i, j) => (FrontProfile.Dist(exteriors[i].Body, exteriors[j].Body) + FrontProfile.Dist(exteriors[i].Side, exteriors[j].Side)) / 2);
            foreach (var st in names)
                if (exteriors.Where(x => x.Style == st && x.Room.Sum() > 0).ToList() is { Count: > 0 } mine)
                    Console.WriteLine($"  {st} relief by band (floor up): " + string.Join(" ", Enumerable.Range(0, Bins).Select(b => mine.Average(x => x.Relief[b]).ToString("0.00", CultureInfo.InvariantCulture))));
            foreach (var (st, t) in trims)
                Console.WriteLine($"  {st}: {(double)t.Trims / Math.Max(1, t.Vehicles):0.0} trims and {(double)t.Fittings / Math.Max(1, t.Vehicles):0.0} fittings a vehicle");
        }
        Console.WriteLine($"failures: {failures}; coplanar pairs inside lofted bays, cabs, hoods, panes, trims and fittings: {fights}");
        return failures + fights;
    }
}


/// <summary>
/// Where a room's guns and gear may go, judged by its real bays (kit 5's lofted sections, read back from their keys) rather than by the plan's names: a roof turret on
/// a roof that is level under its turntable with nothing beside it higher, a side gun on a waist with nothing above it standing out over its turret (an eave, a
/// ledge, a neighbour's roof edge), a pannier on a waist with room above it. A bay that isn't lofted (kits 1 to 4, the Double Decker) keeps the plan's rule.
/// </summary>
public static class BaySpots
{
    /// <summary>A roof turret's bay: how level along its length, and how much higher than its roof a bay within the turret's reach may stand (its own roof may fall away either side: a gable, a crown).</summary>
    private const float Level = 0.03f, Rise = 0.02f;

    /// <summary>A side gun's turret on a sponson: from just above the sponson's top to over the gunner's head, nothing out past the waist by more than this.</summary>
    private const float SideFrom = 0.15f, SideTo = 1.3f, SideProud = 0.03f, PannierTo = 0.35f;

    /// <summary>
    /// The turret a spot is judged for (phase 2: from the parts, not constants): the turntable's half width (a bay shorter than the table can't hold it) and the arm's
    /// reach either way from the bay's middle (the arm, its seat and the gun's hinge: the Beam 1 m the kit lays, a quarter over each end), so a long bay's neighbours
    /// are judged by how far they really are and a short one's next-but-one too.
    /// </summary>
    public readonly record struct Turret(float Table, float Reach)
    {
        public static Turret Kit => _kit ??= Measure();
        private static Turret? _kit;

        private static Turret Measure()
        {
            {
                var table = ConstructParts.Find("Turntable") is { } t ? MathF.Max(t.BoundsMax.X - t.BoundsMin.X, t.BoundsMax.Z - t.BoundsMin.Z) / 2 : 0.33f;
                var arm = ConstructParts.Find(Vehicle.Beam + " 1 m") is { } b ? (b.BoundsMax.Z - b.BoundsMin.Z) / 2 + 0.25f : 0.75f;
                return new Turret(table, arm);
            }
        }
    }

    private static HullLoft.Shape? Of(string? name) => name == null ? null : HullLoft.ShapeOf(name);

    private static float Top(HullLoft.Shape s) => MathF.Max(s.A.Top, s.B.Top);

    /// <summary>Each lofted bay's start and end along the room (its key's lengths), front to back.</summary>
    private static (float Z0, float Z1)[] Spans(string[] loft)
    {
        var at = new (float, float)[loft.Length];
        var z = 0f;
        for (var k = 0; k < loft.Length; k++)
        {
            var len = Of(loft[k])?.Length ?? 1f;
            at[k] = (z, z + len);
            z += len;
        }
        return at;
    }

    /// <summary>The bays any part of which lies within <paramref name="reach"/> of bay <paramref name="k"/>'s middle (itself left out).</summary>
    private static IEnumerable<int> Near(string[] loft, int k, float reach)
    {
        var spans = Spans(loft);
        var mid = (spans[k].Z0 + spans[k].Z1) / 2;
        for (var j = 0; j < loft.Length; j++)
            if (j != k && spans[j].Z1 > mid - reach + 1e-3f && spans[j].Z0 < mid + reach - 1e-3f)
                yield return j;
    }

    /// <summary>Is bay <paramref name="k"/>'s roof a turret's: level along it, long enough for the turntable, no bay within the turret's reach higher than its roof (the turntable stands on its top).</summary>
    public static bool Roof(string[] plan, string[]? loft, int k) => Roof(plan, loft, k, Turret.Kit);

    public static bool Roof(string[] plan, string[]? loft, int k, Turret t)
    {
        if (Of(loft?[k]) is not { } s)
            return plan[k] is "Door" or "Bay T" || plan[k] is "Tail T" && loft == null;
        var top = s.A.Top;
        if (MathF.Abs(s.A.Top - s.B.Top) > Level || s.Front || s.Back || s.Length < 2 * t.Table)
            return false; // (a sloped roof, a brow or a tail, a bay shorter than the table: the arm runs into the next bay's wall)
        foreach (var j in Near(loft!, k, t.Reach + 0.25f))
            if (Of(loft![j]) is { } n && Top(n) > top + Rise)
                return false;
        return true;
    }

    /// <summary>Is bay <paramref name="k"/>'s roof flat enough for a rack's deck plates (out to <paramref name="half"/> either side, level along it)?</summary>
    public static bool Flat(string[] plan, string[]? loft, int k, float half = 0.55f)
    {
        if (Of(loft?[k]) is not { } s)
            return plan[k] is "Door" or "Bay T";
        return !s.Front && !s.Back && MathF.Abs(s.A.Top - s.B.Top) <= 0.005f && s.A.YAt(half) >= s.A.Top - 0.005f && s.B.YAt(half) >= s.B.Top - 0.005f;
    }

    /// <summary>The waist a bay's side mounts hang on (height, and the side's half width there), as <see cref="HullLoft"/> puts its "+x0"/"-x0" sockets.</summary>
    private static (float Y, float X) Waist(HullLoft.Shape s)
    {
        var y = (s.A.WaistY + s.B.WaistY) / 2;
        return (y, (MathF.Max(s.A.XAt(y), 0) + MathF.Max(s.B.XAt(y), 0)) / 2);
    }

    /// <summary>Does anything stand out over a mount on bay <paramref name="k"/>'s waist, from <paramref name="from"/> to <paramref name="to"/> above it, on it or the bays within <paramref name="reach"/>?</summary>
    private static bool ClearAbove(string[] loft, int k, float from, float to, float reach, float proud = SideProud)
    {
        if (Of(loft[k]) is not { } s)
            return true;
        var (wy, wx) = Waist(s);
        foreach (var j in Near(loft, k, reach).Append(k))
            if (Of(loft[j]) is { } n)
                foreach (var sec in new[] { n.A, n.B })
                    if (sec.WidestBetween(MathF.Max(0, wy + from), wy + to) > wx + proud)
                        return false;
        return true;
    }

    /// <summary>
    /// Can bay <paramref name="k"/> take a side gun (a sponson, its turntable, an arm a metre long either way, the hinge and the gun)? Not on the door bay, and not
    /// where an eave, a ledge or a neighbour's roof edge stands out over the turret (Bandit War Bus 5's ledge, a cabin's eaves: 2026-11-07), within the turret's reach.
    /// </summary>
    public static bool Side(string[] plan, string[]? loft, int k) => plan[k] != "Door" && (loft == null ||
        ClearAbove(loft, k, SideFrom, SideTo, Turret.Kit.Reach + 0.25f, 0.005f) && ClearAbove(loft, k, -SponsonHalf, SponsonHalf, 0, 0.005f)); // (phase 2: a flared wall meets a sponson's back or its turntable face to face: nothing out past the waist at all)

    private const float SponsonHalf = 0.28f; // (a Gun Sponson is 0.52 m tall about its back socket)

    /// <summary>
    /// Can a pannier hang on bay <paramref name="k"/>'s waist: nothing of the bay's side out past the waist over the pannier's height (its flat back stands in the skin,
    /// so a wall above or below that stands out meets it face to face: IT Box Truck 3's flare, phase 2), and 0.35 m above it clear?
    /// </summary>
    public static bool Pannier(string[]? loft, int k) => loft == null || ClearAbove(loft, k, -PannierHalf, PannierHalf, 0, 0.005f) && ClearAbove(loft, k, 0.05f, PannierTo, 0);

    private const float PannierHalf = 0.32f; // (a Hull Pannier 1 m is 0.6 m tall about its back socket)
}

/// <summary>
/// A cross-section seen from the front (docs/vehiclekit/BODY-STYLES.md, "Measuring"): the half width in <see cref="Bins"/> bands of the thing's own height, each over
/// the widest, so a box reads 1 all the way up and a triangle falls to 0. <see cref="VehicleDiversity"/> compares two vehicles' rooms and bodies by it, and
/// <see cref="VehicleIdentity"/> each room bay against its style's languages; <c>--body-report</c> prints it. Works on any drawn triangles or any section, so a
/// later archetype (a car's cabin, a buggy's cage) is measured the same way.
/// </summary>
public static class FrontProfile
{
    public const int Bins = 12;

    /// <summary>The mean difference per band: 0 the same section, about 0.3 a box against a triangle.</summary>
    public static float Dist(float[] a, float[] b)
    {
        var s = 0f;
        for (var i = 0; i < Bins; i++)
            s += MathF.Abs(a[i] - b[i]);
        return s / Bins;
    }

    private static void Add(float[] bins, Vector2 a, Vector2 b, float lo, float hi)
    {
        var n = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) / 0.04f));
        for (var k = 0; k <= n; k++)
        {
            var p = Vector2.Lerp(a, b, k / (float)n);
            var i = Math.Clamp((int)((p.Y - lo) / (hi - lo) * Bins), 0, Bins - 1);
            bins[i] = MathF.Max(bins[i], MathF.Abs(p.X));
        }
    }

    private static float[] Finish(float[] bins)
    {
        var most = bins.Max();
        for (var b = 0; b < Bins; b++)
            bins[b] = most > 0 ? bins[b] / most : 0;
        for (var b = 1; b < Bins; b++)
            if (bins[b] == 0)
                bins[b] = bins[b - 1]; // (a band nothing fell in takes the one below)
        return bins;
    }

    /// <summary>Of the triangles <paramref name="ranges"/> pick out of <paramref name="tris"/> (three points each; ranges in triangles, end exclusive), about their middle in x.</summary>
    public static float[] Of(Vector3[] tris, IEnumerable<(int From, int To)> ranges)
    {
        var bins = new float[Bins];
        float lo = float.MaxValue, hi = float.MinValue, xl = float.MaxValue, xh = float.MinValue;
        var list = ranges.Where(r => r.From >= 0 && r.To > r.From).ToList();
        foreach (var (f, t) in list)
            for (var i = f * 3; i < t * 3; i++)
                (lo, hi, xl, xh) = (MathF.Min(lo, tris[i].Y), MathF.Max(hi, tris[i].Y), MathF.Min(xl, tris[i].X), MathF.Max(xh, tris[i].X));
        var mid = (xl + xh) / 2;
        if (hi <= lo + 0.05f)
            return bins;
        foreach (var (f, t) in list)
            for (var i = f; i < t; i++)
                for (var e = 0; e < 3; e++)
                {
                    var (p, q) = (tris[i * 3 + e], tris[i * 3 + (e + 1) % 3]);
                    Add(bins, new Vector2(p.X - mid, p.Y), new Vector2(q.X - mid, q.Y), lo, hi);
                }
        return Finish(bins);
    }

    /// <summary>Of a half section (floor edge to the centreline).</summary>
    public static float[] Of(HullLoft.Section s)
    {
        var bins = new float[Bins];
        for (var k = 0; k + 1 < s.P.Length; k++)
            Add(bins, s.P[k], s.P[k + 1], 0, s.Top);
        return Finish(bins);
    }

    private static readonly Dictionary<(string, int), float[]> Langs = new();

    /// <summary>A language's tall section as a style draws it (cached).</summary>
    public static float[] Of(BodyLanguage l, int facets)
    {
        lock (Langs)
        {
            if (!Langs.TryGetValue((l.Name, facets), out var p))
                Langs[(l.Name, facets)] = p = Of(l.Drawn(facets));
            return p;
        }
    }

    private static readonly Dictionary<BodyStyleRule, float[][]> Styles = new(ReferenceEqualityComparer.Instance);

    /// <summary>Every section a style's rooms may show (<see cref="BodyLook.References"/>), from the front (cached).</summary>
    public static float[][] OfStyle(BodyStyleRule rule)
    {
        var refs = BodyLook.References(rule);
        lock (Styles)
        {
            if (!Styles.TryGetValue(rule, out var p))
                Styles[rule] = p = refs.Select(Of).ToArray();
            return p;
        }
    }

    /// <summary>The facets a style draws with, for its languages' profiles: the middle of its range (0: as written).</summary>
    public static int FacetsOf(BodyStyleRule r) => r.FacetsMax > 0 ? (r.FacetsMin + r.FacetsMax) / 2 : 0;

    /// <summary>
    /// A built vehicle's cab and hood (the cab shell, its panes, the hood and what hangs on it), from the front, and their side outline: the top's height in
    /// <see cref="Bins"/> slices along their length, over their own height (body styles phase 3). No cab: zeros.
    /// </summary>
    public static (float[] Front, float[] Side) OfCab(Vehicle v, Vector3[] tris, int[] owner)
    {
        var c = v.Built;
        var mine = new HashSet<int>(new[] { "cabshell", "hood", "glass" }.SelectMany(t => v.Panels.GetValueOrDefault(t) ?? []).Select(p => p.Index));
        var ranges = new List<(int, int)>();
        var (start, prev) = (0, owner.Length > 0 ? owner[0] : -1);
        for (var t = 0; t <= owner.Length; t++)
            if (t == owner.Length || owner[t] != prev)
            {
                if (prev >= 0 && prev < c.PartCount && mine.Contains(prev))
                    ranges.Add((start, t));
                if (t < owner.Length)
                    (start, prev) = (t, owner[t]);
            }
        if (ranges.Count == 0)
            return (new float[Bins], new float[Bins]);
        var side = new float[Bins];
        float lo = float.MaxValue, hi = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        foreach (var (f, t) in ranges)
            for (var i = f * 3; i < t * 3; i++)
                (lo, hi, z0, z1) = (MathF.Min(lo, tris[i].Y), MathF.Max(hi, tris[i].Y), MathF.Min(z0, tris[i].Z), MathF.Max(z1, tris[i].Z));
        foreach (var (f, t) in ranges)
            for (var i = f; i < t; i++)
                for (var e = 0; e < 3; e++)
                {
                    var (p, q) = (tris[i * 3 + e], tris[i * 3 + (e + 1) % 3]);
                    var n = Math.Max(1, (int)MathF.Ceiling(Vector3.Distance(p, q) / 0.04f));
                    for (var k = 0; k <= n; k++)
                    {
                        var at = Vector3.Lerp(p, q, k / (float)n);
                        var b = Math.Clamp((int)((at.Z - z0) / MathF.Max(0.05f, z1 - z0) * Bins), 0, Bins - 1);
                        side[b] = MathF.Max(side[b], (at.Y - lo) / MathF.Max(0.05f, hi - lo));
                    }
                }
        return (Of(tris, ranges), side);
    }

    /// <summary>
    /// Body styles phase 4: what a vehicle's exterior reads as, whatever its paint. <c>Room</c>: the rooms' cross-section from the front with everything on their
    /// outside (trim, panniers, sponsons, ladders), as <see cref="Of(Vector3[], IEnumerable{ValueTuple{int, int}})"/>; <c>Relief</c>: in <see cref="Bins"/> bands of the
    /// room's height, the share of its length where something stands proud of the skin by a centimetre or more (a belt, slats, a board, plates); <c>Side</c>: the
    /// whole body's side outline (its top's height in <see cref="Bins"/> slices along its length, over its own height). Zeros where there is no room.
    /// </summary>
    public static (float[] Room, float[] Relief, float[] Side) Exterior(Vehicle v, Vector3[] tris, int[] owner)
    {
        var c = v.Built;
        var bays = new HashSet<int>(v.Rooms.SelectMany(r => r.Bays).Select(b => b.Index));
        var inside = new HashSet<int>(v.Rooms.SelectMany(r => r.Furniture.Concat(r.Containers).Concat(r.Seats).Concat(r.Door is { } d ? [d] : []).Concat(r.Hinge is { } h ? [h] : [])).Select(p => p.Index));
        foreach (var p in v.Panels.GetValueOrDefault("furniture") ?? [])
            inside.Add(p.Index);
        var side = new float[Bins];
        float lo = float.MaxValue, hi = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        for (var t = 0; t < owner.Length; t++)
            if (owner[t] >= 0 && owner[t] < c.PartCount && c.DefOf(owner[t]).Role is not (PartRole.Wheel or PartRole.Suspension))
                for (var e = 0; e < 3; e++)
                {
                    var q = tris[3 * t + e];
                    (lo, hi, z0, z1) = (MathF.Min(lo, q.Y), MathF.Max(hi, q.Y), MathF.Min(z0, q.Z), MathF.Max(z1, q.Z));
                }
        for (var t = 0; t < owner.Length; t++)
            if (owner[t] >= 0 && owner[t] < c.PartCount && c.DefOf(owner[t]).Role is not (PartRole.Wheel or PartRole.Suspension))
                for (var e = 0; e < 3; e++)
                {
                    var (p, q) = (tris[3 * t + e], tris[3 * t + (e + 1) % 3]);
                    var n = Math.Max(1, (int)MathF.Ceiling(Vector3.Distance(p, q) / 0.05f));
                    for (var k = 0; k <= n; k++)
                    {
                        var at = Vector3.Lerp(p, q, k / (float)n);
                        var b = Math.Clamp((int)((at.Z - z0) / MathF.Max(0.05f, z1 - z0) * Bins), 0, Bins - 1);
                        side[b] = MathF.Max(side[b], (at.Y - lo) / MathF.Max(0.05f, hi - lo));
                    }
                }
        if (bays.Count == 0)
            return (new float[Bins], new float[Bins], side);
        // The rooms' extent, and the skin's half width in cells of 10 cm along the room by a twelfth of its height (a room's middle in x: its bays').
        float rlo = float.MaxValue, rhi = float.MinValue, rz0 = float.MaxValue, rz1 = float.MinValue, xl = float.MaxValue, xh = float.MinValue;
        for (var t = 0; t < owner.Length; t++)
            if (bays.Contains(owner[t]))
                for (var e = 0; e < 3; e++)
                {
                    var q = tris[3 * t + e];
                    (rlo, rhi, rz0, rz1, xl, xh) = (MathF.Min(rlo, q.Y), MathF.Max(rhi, q.Y), MathF.Min(rz0, q.Z), MathF.Max(rz1, q.Z), MathF.Min(xl, q.X), MathF.Max(xh, q.X));
                }
        var mid = (xl + xh) / 2;
        var slices = Math.Max(1, (int)MathF.Ceiling((rz1 - rz0) / 0.1f));
        var skin = new float[slices, Bins];
        var proud = new bool[slices, Bins];
        bool Cell(Vector3 at, out int zi, out int yi)
        {
            (zi, yi) = ((int)((at.Z - rz0) / 0.1f), (int)((at.Y - rlo) / MathF.Max(0.05f, rhi - rlo) * Bins));
            return zi >= 0 && zi < slices && yi >= 0 && yi < Bins;
        }
        void Samples(int t, Action<Vector3> take)
        {
            var (a, b, d) = (tris[3 * t], tris[3 * t + 1], tris[3 * t + 2]);
            var n = Math.Clamp((int)MathF.Ceiling(MathF.Max(Vector3.Distance(a, b), MathF.Max(Vector3.Distance(b, d), Vector3.Distance(d, a))) / 0.05f), 1, 60);
            for (var i = 0; i <= n; i++)
                for (var j = 0; i + j <= n; j++)
                    take(a + (b - a) * (i / (float)n) + (d - a) * (j / (float)n));
        }
        for (var t = 0; t < owner.Length; t++)
            if (bays.Contains(owner[t]))
                Samples(t, at =>
                {
                    if (Cell(at, out var zi, out var yi))
                        skin[zi, yi] = MathF.Max(skin[zi, yi], MathF.Abs(at.X - mid));
                });
        var room = new List<(int, int)>();
        var (start, prev) = (0, owner.Length > 0 ? owner[0] : -1);
        for (var t = 0; t <= owner.Length; t++)
            if (t == owner.Length || owner[t] != prev)
            {
                if (prev >= 0 && prev < c.PartCount && !inside.Contains(prev) && c.DefOf(prev).Role is not (PartRole.Wheel or PartRole.Suspension))
                {
                    var near = bays.Contains(prev);
                    if (!near)
                        for (var k = start; k < t && !near; k++)
                            for (var e = 0; e < 3 && !near; e++)
                            {
                                var q = tris[3 * k + e];
                                near = Cell(q, out var zi, out var yi) && MathF.Abs(q.X - mid) > skin[zi, yi] - 0.1f && skin[zi, yi] > 0;
                            }
                    if (near)
                        room.Add((start, t));
                }
                if (t < owner.Length)
                    (start, prev) = (t, owner[t]);
            }
        foreach (var (f, t) in room)
            for (var k = f; k < t; k++)
                if (!bays.Contains(owner[k]))
                    Samples(k, at =>
                    {
                        if (Cell(at, out var zi, out var yi) && skin[zi, yi] > 0 && MathF.Abs(at.X - mid) > skin[zi, yi] + 0.01f)
                            proud[zi, yi] = true;
                    });
        var relief = new float[Bins];
        for (var yi = 0; yi < Bins; yi++)
        {
            var n = 0;
            for (var zi = 0; zi < slices; zi++)
                n += proud[zi, yi] ? 1 : 0;
            relief[yi] = n / (float)slices;
        }
        return (Of(tris, room), relief, side);
    }

    /// <summary>A built vehicle's rooms (every bay of every room) and its whole body (every part but its wheels), from the front. Empty rooms: zeros.</summary>
    public static (float[] Room, float[] Body) OfVehicle(Vehicle v, Vector3[] tris, int[] owner)
    {
        var c = v.Built;
        var bays = new HashSet<int>(v.Rooms.SelectMany(r => r.Bays).Select(b => b.Index));
        var rooms = new List<(int, int)>();
        var body = new List<(int, int)>();
        var (start, prev) = (0, owner.Length > 0 ? owner[0] : -1);
        for (var t = 0; t <= owner.Length; t++)
            if (t == owner.Length || owner[t] != prev)
            {
                if (prev >= 0 && prev < c.PartCount)
                {
                    if (bays.Contains(prev))
                        rooms.Add((start, t));
                    if (c.DefOf(prev).Role is not (PartRole.Wheel or PartRole.Suspension))
                        body.Add((start, t));
                }
                if (t < owner.Length)
                    (start, prev) = (t, owner[t]);
            }
        return (rooms.Count > 0 ? Of(tris, rooms) : new float[Bins], Of(tris, body));
    }
}
