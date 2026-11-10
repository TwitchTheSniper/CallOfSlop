using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// Body styles phase 8a (docs/vehiclekit/BODY-STYLES.md, section 16): the archetype library. An archetype is a body language with its proportions, taken from a
/// class of real vehicles as inspiration only (no maker's design, name or badge): its section profile, roofline, scale class, cab kind and height, belt, windscreen
/// rake, nose and hood length, the fastback of its cab's roof, its brows and tails and the ride it leans to. It is laid over the faction's own rule
/// (<see cref="BodyStyleRule"/>) for one vehicle, so the faction keeps its paint, trim, fittings, lining and props and the archetype gives the silhouette.
/// Which factions build an archetype, and how often, is data here (<see cref="Factions"/>); which shapes it may take is <see cref="Shapes"/>.
/// </summary>
public sealed record BodyArchetype(string Name, string Family, string Words, string[] Shapes, (string Style, double W)[] Factions, Func<BodyStyleRule, BodyStyleRule> Over)
{
    private static HullLoft.Section S(params float[] xy) => new(Enumerable.Range(0, xy.Length / 2).Select(i => new Vector2(xy[2 * i], xy[2 * i + 1])).ToArray());

    // ---- The languages: each keeps a walk-in room's limits (x 1.0 m or more below 1.6 m, over 2.1 m at its top), so any of them can loft a room as well as a cab.

    /// <summary>An early long-hood fastback: a soft hip, a long tumblehome, the roof running down into the tail.</summary>
    public static readonly BodyLanguage Fastback = new("fastback", "an early fastback: a soft hip, a long tumblehome, the roof running down into the tail",
        S(1.02f, 0, 1.1f, 0.55f, 1.1f, 1.0f, 1.0f, 1.6f, 0.7f, 2.05f, 0, 2.16f), 2.1f, 2.24f, 0.62f, 0.75f, 0.55f, 0.7f, 0.92f, 0.86f)
        { Lines = [(Roofline.Fastback, 2), (Roofline.Sloped, 1)], FlareBlend = 0.45f };

    /// <summary>A late-sixties coke-bottle: a hip over the rear wheel, pinched at the belt, a pillared roof set in.</summary>
    public static readonly BodyLanguage Coke = new("coke", "a coke-bottle: a hip flare low, pinched at the belt, a narrow roof set in",
        S(1.0f, 0, 1.14f, 0.45f, 1.04f, 0.95f, 1.1f, 1.3f, 1.0f, 1.6f, 0.66f, 2.06f, 0, 2.14f), 2.1f, 2.22f, 0.62f, 0.75f, 0.6f, 0.75f, 0.92f, 0.88f)
        { Lines = [(Roofline.Fastback, 1), (Roofline.Crowned, 1)], FlareBlend = 0.4f };

    /// <summary>A seventies wedge: slab sides, a hard crease at the belt, a flat formal roof.</summary>
    public static readonly BodyLanguage Wedge = new("wedge", "a seventies wedge: slab sides, a crease at the belt, a flat formal roof",
        S(1.05f, 0, 1.08f, 0.9f, 1.06f, 1.65f, 0.86f, 2.12f, 0, 2.16f), 2.12f, 2.24f, 0.7f, 0.85f, 0.75f, 0.9f, 0.96f, 0.94f)
        { Lines = [(Roofline.Wedged, 2), (Roofline.Plain, 1)], FlareBlend = 0.05f };

    /// <summary>A mid-engined supercar: very wide and low, a strong tumblehome to a small canopy.</summary>
    public static readonly BodyLanguage SuperWedge = new("superwedge", "a supercar: wide at the sills, a strong tumblehome to a small canopy",
        S(1.0f, 0, 1.18f, 0.35f, 1.14f, 0.8f, 1.02f, 1.4f, 1.0f, 1.6f, 0.62f, 1.98f, 0, 2.12f), 2.1f, 2.2f, 0.55f, 0.66f, 0.6f, 0.72f, 0.88f, 0.86f)
        { Lines = [(Roofline.Fastback, 1.5), (Roofline.Tapered, 1)], FlareBlend = 0.6f };

    /// <summary>A European compact hatch: near-upright sides, a tall glasshouse, a short upright tail.</summary>
    public static readonly BodyLanguage Hatch = new("hatch", "a compact hatch: near-upright sides, a tall glasshouse, a short upright tail",
        S(1.02f, 0, 1.08f, 0.7f, 1.05f, 1.55f, 0.9f, 2.0f, 0.5f, 2.14f, 0, 2.17f), 2.14f, 2.26f, 0.62f, 0.78f, 0.82f, 0.94f, 0.94f, 0.95f)
        { Lines = [(Roofline.Plain, 1.5), (Roofline.Crowned, 1)], FlareBlend = 0.3f };

    /// <summary>A European estate: a long level roof, square shoulders, upright at the back.</summary>
    public static readonly BodyLanguage Estate = new("estate", "an estate: square shoulders under a long level roof, upright at the back",
        S(1.03f, 0, 1.07f, 0.8f, 1.05f, 1.62f, 0.94f, 2.08f, 0.55f, 2.18f, 0, 2.2f), 2.16f, 2.28f, 0.65f, 0.8f, 0.85f, 0.95f, 0.95f, 0.96f)
        { Lines = [(Roofline.Level, 2), (Roofline.Plain, 1)], FlareBlend = 0.2f };

    /// <summary>A European minibus: upright sides, a tall glazed band, a gently domed roof.</summary>
    public static readonly BodyLanguage Minibus = new("minibus", "a minibus: upright sides, a tall glazed band, a gently domed roof",
        S(1.02f, 0, 1.06f, 0.8f, 1.04f, 1.9f, 0.92f, 2.22f, 0.5f, 2.32f, 0, 2.34f), 2.28f, 2.4f, 0.62f, 0.78f, 0.8f, 0.92f, 0.95f, 0.95f)
        { Lines = [(Roofline.Level, 1.5), (Roofline.Crowned, 1)], FlareBlend = 0.3f };

    /// <summary>A European rigid truck's box body: flat panels, crisp corners, one height end to end.</summary>
    public static readonly BodyLanguage BoxBody = new("box body", "a rigid truck's box body: flat panels, crisp corners, one height end to end",
        S(1.07f, 0, 1.08f, 2.3f, 1.02f, 2.38f, 0, 2.4f), 2.34f, 2.5f, 0.85f, 0.95f, 0.88f, 0.97f, 0.99f, 0.99f)
        { Lines = [(Roofline.Level, 3), (Roofline.Plain, 0.5)], FlareBlend = 0.05f };

    /// <summary>A curtain-sider: the curtain bowed a little between a sole rail and a cant rail, a flat roof.</summary>
    public static readonly BodyLanguage Curtain = new("curtain", "a curtain-sider: the curtain bowed between a sole rail and a cant rail, a flat roof",
        S(1.06f, 0, 1.1f, 0.4f, 1.1f, 1.9f, 1.07f, 2.26f, 1.0f, 2.34f, 0, 2.36f), 2.3f, 2.46f, 0.85f, 0.95f, 0.88f, 0.97f, 0.99f, 0.99f)
        { Lines = [(Roofline.Level, 3), (Roofline.Plain, 0.5)], FlareBlend = 0.2f };

    /// <summary>A European cab-over: a flat upright face, square shoulders rounding into the roof (drawn on the cab; its rooms are the box's).</summary>
    public static readonly BodyLanguage CabOver = new("cab-over", "a European cab-over: a flat upright face, square shoulders rounding into the roof",
        S(1.04f, 0, 1.06f, 1.5f, 1.0f, 2.0f, 0.84f, 2.26f, 0, 2.3f), 2.24f, 2.36f, 0.8f, 0.92f, 0.8f, 0.92f, 0.97f, 0.97f)
        { Lines = [(Roofline.Level, 1)], FlareBlend = 0.1f };

    // ---- Phase 8b (section 18): buggies, monster trucks, fantasy wagons, concept cars.

    /// <summary>A sci-fi buggy's tub: a wide flat sill, the sides tucking hard in to a low canopy line (drawn on a caged cab).</summary>
    public static readonly BodyLanguage Tub = new("tub", "a buggy's tub: a wide flat sill, the sides tucked hard in to a low canopy line",
        S(1.12f, 0, 1.14f, 0.5f, 1.02f, 1.2f, 0.84f, 1.8f, 0.5f, 2.08f, 0, 2.12f), 2.1f, 2.2f, 0.55f, 0.7f, 0.6f, 0.75f, 0.9f, 0.88f)
        { Lines = [(Roofline.Wedged, 1.5), (Roofline.Tapered, 1)], FlareBlend = 0.1f };

    /// <summary>A pickup's cab, tall and upright: square shoulders, a flat glasshouse (the monster truck's cab, high over its wheels).</summary>
    public static readonly BodyLanguage Pickup = new("pickup", "a pickup's cab: square shoulders, a tall upright glasshouse, a flat roof",
        S(1.04f, 0, 1.07f, 1.0f, 1.03f, 1.55f, 0.84f, 2.06f, 0, 2.14f), 2.12f, 2.24f, 0.68f, 0.82f, 0.75f, 0.9f, 0.95f, 0.95f)
        { Lines = [(Roofline.Plain, 1.5), (Roofline.Level, 1)], FlareBlend = 0.15f };

    /// <summary>A war wagon's hooped top: low upright sides, a tall round canvas arch over them.</summary>
    public static readonly BodyLanguage Hoop = new("hoop", "a war wagon: low upright sides under a tall round arch of hoops",
        S(1.04f, 0, 1.06f, 1.15f, 1.02f, 1.6f, 0.84f, 2.0f, 0.48f, 2.3f, 0, 2.36f), 2.3f, 2.44f, 0.55f, 0.7f, 0.55f, 0.7f, 0.86f, 0.86f)
        { Lines = [(Roofline.Crowned, 2), (Roofline.Sagged, 1)], FlareBlend = 0.4f };

    /// <summary>An ornate carriage: a body bowed out at the waist, tucked in at the sill and the roof, a crowned top.</summary>
    public static readonly BodyLanguage Carriage = new("carriage", "a carriage: a body bowed out at the waist, tucked under and in, a crowned top",
        S(1.0f, 0, 1.12f, 0.55f, 1.16f, 1.1f, 1.08f, 1.7f, 0.92f, 2.1f, 0.6f, 2.28f, 0, 2.32f), 2.26f, 2.38f, 0.65f, 0.8f, 0.65f, 0.8f, 0.9f, 0.9f)
        { Lines = [(Roofline.Crowned, 2), (Roofline.Sheer, 1)], FlareBlend = 0.6f };

    /// <summary>A concept car's bubble: round from the sill to a dome canopy.</summary>
    public static readonly BodyLanguage Bubble = new("bubble", "a bubble car: round from the sill to a dome canopy",
        S(1.0f, 0, 1.12f, 0.4f, 1.14f, 0.9f, 1.04f, 1.4f, 0.8f, 1.85f, 0.42f, 2.1f, 0, 2.16f), 2.12f, 2.22f, 0.55f, 0.66f, 0.55f, 0.66f, 0.86f, 0.84f)
        { Lines = [(Roofline.Crowned, 1.5), (Roofline.Fastback, 1)], FlareBlend = 0.6f };

    /// <summary>A streamliner: a low teardrop, wide at the sills, a narrow single-seat canopy.</summary>
    public static readonly BodyLanguage Streamliner = new("streamliner", "a streamliner: a low teardrop, wide at the sills, a narrow canopy",
        S(1.0f, 0, 1.16f, 0.35f, 1.12f, 0.8f, 0.94f, 1.3f, 0.62f, 1.8f, 0.3f, 2.08f, 0, 2.12f), 2.1f, 2.18f, 0.5f, 0.6f, 0.5f, 0.6f, 0.86f, 0.84f)
        { Lines = [(Roofline.Fastback, 2), (Roofline.Tapered, 1)], FlareBlend = 0.6f };

    public static readonly BodyLanguage[] Languages = [Fastback, Coke, Wedge, SuperWedge, Hatch, Estate, Minibus, BoxBody, Curtain, CabOver, Tub, Pickup, Hoop, Carriage, Bubble, Streamliner];

    // ---- The archetypes (the user, 2026-11-07: muscle cars by era, supercars, European car lines, European trucks; real vehicles as inspiration only).

    private static readonly string[] Car = ["Car"], Rigid = ["Box Truck"], Bus = ["War Bus"];
    private static readonly string[] Buggy = ["Buggy"], Monster = ["Monster Truck"], Wagon = ["Wagon"], GiantTruck = ["Giant Truck"], Concept = ["Concept"];

    public static readonly BodyArchetype[] All =
    [
        // Muscle cars by era: long hoods, low wide cabs, a fastback roof.
        new("early fastback", "muscle", "an early long-hood fastback: a long flat bonnet, a low wide cab, the roof running down to the tail", Car, [("Highland", 2), ("Bandit", 0.5)],
            r => r with
            {
                Languages = [(Fastback, 1)], Mix = 0, Cabs = [("enclosed", 1)], Noses = [('g', 2), ('r', 1)], Ride = (2.5, 1, 0.2),
                CabTallMin = 1.56f, CabTallMax = 1.62f, BeltMin = 0.92f, BeltMax = 0.98f, RakeMin = 0.2f, RakeMax = 0.24f, CabHalfMin = 0.92f, CabHalfMax = 0.95f,
                HoodReach = 0.15f, HoodHalfMin = 0.6f, HoodHalfMax = 0.64f, HoodCrownMin = 0.17f, HoodCrownMax = 0.19f, CabBackMin = 0.6f, CabBackMax = 0.85f,
            }),
        new("coke bottle", "muscle", "a late-sixties coke-bottle: a hip over the wheels, a pinched waist, a semi-fastback roof", Car, [("Bandit", 2)],
            r => r with
            {
                Languages = [(Coke, 1)], Mix = 0, Cabs = [("enclosed", 2), ("caged", 0.5)], Noses = [('g', 1.5), ('f', 1)], Ride = (2, 1, 0.5),
                CabTallMin = 1.58f, CabTallMax = 1.66f, BeltMin = 0.96f, BeltMax = 1.02f, RakeMin = 0.16f, RakeMax = 0.22f, CabHalfMin = 0.9f, CabHalfMax = 0.94f,
                HoodReach = 0.1f, HoodHalfMin = 0.58f, HoodHalfMax = 0.62f, HoodCrownMin = 0.18f, HoodCrownMax = 0.22f, CabBackMin = 0.3f, CabBackMax = 0.55f,
            }),
        new("seventies wedge", "muscle", "a seventies boxy wedge: slab sides, a flat formal roof, an upright grille", Car, [("Bandit", 1.5), ("Scam", 0.5)],
            r => r with
            {
                Languages = [(Wedge, 1)], Mix = 0, FacetsMin = 0, FacetsMax = 0, Cabs = [("enclosed", 2), ("armoured", 0.5)], Noses = [('f', 2), ('w', 1)], Ride = (1.5, 1, 0.5),
                CabTallMin = 1.6f, CabTallMax = 1.68f, BeltMin = 1.0f, BeltMax = 1.04f, RakeMin = 0.12f, RakeMax = 0.18f, CabHalfMin = 0.93f, CabHalfMax = 0.95f,
                HoodReach = 0.12f, HoodHalfMin = 0.6f, HoodHalfMax = 0.64f, HoodCrownMin = 0.16f, HoodCrownMax = 0.18f, CabBackMin = 0.1f, CabBackMax = 0.25f,
            }),
        // Supercars: low, wide, cab-forward wedges.
        new("supercar", "supercar", "a mid-engined supercar: a cab-forward wedge, very low and wide, a raked screen", Car, [("IT", 2)],
            r => r with
            {
                Languages = [(SuperWedge, 1)], Mix = 0, Cabs = [("cab-over", 2), ("enclosed", 1)], Noses = [('w', 3)], Ride = (4, 1, 0),
                CabTallMin = 1.55f, CabTallMax = 1.6f, BeltMin = 0.9f, BeltMax = 0.94f, RakeMin = 0.24f, RakeMax = 0.24f, CapsuleRakeMin = 0.75f, CapsuleRakeMax = 0.8f,
                CabHalfMin = 0.94f, CabHalfMax = 0.95f, HoodHalfMin = 0.62f, HoodHalfMax = 0.64f, HoodCrownMin = 0.16f, HoodCrownMax = 0.17f, CabBackMin = 0.7f, CabBackMax = 0.9f,
            }),
        // European car lines: a compact hatch and an estate.
        new("compact hatch", "european car", "a European compact hatch: a short bonnet, a tall glasshouse, an upright tail", Car, [("British", 2), ("Scam", 1)],
            r => r with
            {
                Languages = [(Hatch, 1)], Mix = 0, Cabs = [("enclosed", 1)], Noses = [('r', 1.5), ('f', 1)], Ride = (1, 1.5, 0.3),
                CabTallMin = 1.72f, CabTallMax = 1.8f, BeltMin = 0.9f, BeltMax = 0.94f, RakeMin = 0.18f, RakeMax = 0.24f, CabHalfMin = 0.88f, CabHalfMax = 0.91f,
                HoodReach = -0.08f, HoodHalfMin = 0.52f, HoodHalfMax = 0.56f, HoodCrownMin = 0.2f, HoodCrownMax = 0.24f, CabBackMin = 0, CabBackMax = 0,
            }),
        new("estate", "european car", "a European estate: square shoulders, a long level roof, upright at the back", Car, [("Scam", 1.5), ("British", 1)],
            r => r with
            {
                Languages = [(Estate, 1)], Mix = 0, Cabs = [("enclosed", 1)], Noses = [('f', 1.5), ('g', 1)], Ride = (1, 1.5, 0.3),
                CabTallMin = 1.7f, CabTallMax = 1.78f, BeltMin = 0.94f, BeltMax = 0.98f, RakeMin = 0.14f, RakeMax = 0.2f, CabHalfMin = 0.9f, CabHalfMax = 0.93f,
                HoodReach = 0, HoodHalfMin = 0.54f, HoodHalfMax = 0.58f, CabBackMin = 0, CabBackMax = 0,
            }),
        // A European minibus on the War Bus: a merged cab, upright and glazed.
        new("minibus", "european van", "a European minibus: a merged cab under one domed roof, upright and glazed", Bus, [("Scam", 1.5)],
            r => r with
            {
                Languages = [(Minibus, 1)], Mix = 0, Scales = [(BodyScale.Standard, 1)], TallMin = 0.95f, TallMax = 1.05f, Cabs = [("merged", 2), ("cab-over", 1)], Noses = [('r', 1)],
                Brows = [(1.25f, 1), (1f, 1)], Tails = [(1f, 1), (0.75f, 1)], CapsuleRakeMin = 0.3f, CapsuleRakeMax = 0.45f, Uppers = [("none", 1)], Cage = 0, Ride = (1, 1.5, 0.2),
            }),
        // European trucks: a flat-fronted cab-over ahead of a box body or a curtain-sider, one height end to end.
        new("rigid box", "european truck", "a European rigid truck: a flat-fronted cab-over ahead of a crisp box body", Rigid, [("IT", 1.5), ("Scam", 0.5)],
            r => r with
            {
                Languages = [(BoxBody, 1)], Mix = 0, FacetsMin = 0, FacetsMax = 0, Scales = [(BodyScale.Heavy, 1.5), (BodyScale.Standard, 1)], TallMin = 1.0f, TallMax = 1.1f, FlareShare = 0.15f,
                Brows = [(1f, 1)], Tails = [(0.75f, 1), (1f, 1)], Cabs = [("cab-over", 1)], CapsuleRakeMin = 0.12f, CapsuleRakeMax = 0.22f, Uppers = [("none", 1)], Cage = 0, Ride = (0.5, 2, 0.2),
                CabTallMin = 1.8f, CabTallMax = 1.9f, DoorRear = 0.6, CabLanguage = CabOver,
            }),
        new("curtain-sider", "european truck", "a European curtain-sider: a flat-fronted cab-over ahead of a bowed curtain between rails", Rigid, [("British", 1.5), ("Highland", 0.4)],
            r => r with
            {
                Languages = [(Curtain, 1)], Mix = 0, Scales = [(BodyScale.Heavy, 1.5), (BodyScale.Standard, 1)], TallMin = 1.0f, TallMax = 1.1f, FlareShare = 0.2f,
                Brows = [(1f, 1)], Tails = [(0.75f, 1), (1f, 1)], Cabs = [("cab-over", 1)], CapsuleRakeMin = 0.12f, CapsuleRakeMax = 0.22f, Uppers = [("none", 1)], Cage = 0, Ride = (0.5, 2, 0.2),
                CabTallMin = 1.8f, CabTallMax = 1.9f, DoorRear = 0.6, CabLanguage = CabOver,
            }),
        // Phase 8b (section 18). Sci-fi open-top 4x4 gun buggies: a caged tub low between wheels on arms, the gun on a ring behind the cage.
        new("gun buggy", "buggy", "a sci-fi open-top 4x4 gun buggy: a caged tub, chunky wheels thrown out on arms, a gun on a ring behind the cage", Buggy, [("IT", 2), ("Scam", 1)],
            r => r with
            {
                Languages = [(Tub, 1)], Mix = 0, Cabs = [("caged", 1)], Noses = [('w', 2), ('r', 1)], Ride = (3, 1, 0),
                CabTallMin = 1.58f, CabTallMax = 1.64f, BeltMin = 0.86f, BeltMax = 0.92f, RakeMin = 0.22f, RakeMax = 0.24f, CabHalfMin = 0.92f, CabHalfMax = 0.95f,
                HoodReach = -0.05f, HoodHalfMin = 0.56f, HoodHalfMax = 0.6f, HoodCrownMin = 0.14f, HoodCrownMax = 0.16f,
            }),
        new("scrap buggy", "buggy", "a scrap-built dune buggy: a slab tub under a roll cage, a short flat bonnet, high on long travel", Buggy, [("Bandit", 2)],
            r => r with
            {
                Languages = [(Tub, 1)], Mix = 0, FacetsMin = 0, FacetsMax = 0, Cabs = [("caged", 1)], Noses = [('f', 2), ('g', 1)], Ride = (1, 2, 0),
                CabTallMin = 1.58f, CabTallMax = 1.66f, BeltMin = 0.9f, BeltMax = 0.96f, RakeMin = 0.14f, RakeMax = 0.2f, CabHalfMin = 0.9f, CabHalfMax = 0.94f,
                HoodReach = -0.08f, HoodHalfMin = 0.54f, HoodHalfMax = 0.6f,
            }),
        // Monster trucks: a short tall pickup on stilts over huge wheels (the gear is the recipe's: ShapeGrammar's MonsterGears).
        new("monster truck", "monster truck", "a monster truck: a short tall pickup cab and bed, high on stilts over four huge wheels", Monster, [("Bandit", 2), ("Highland", 1.5)],
            r => r with
            {
                Languages = [(Pickup, 1)], Mix = 0, Cabs = [("enclosed", 2), ("caged", 0.5)], Noses = [('g', 1.5), ('f', 1)], Ride = (0, 0, 1),
                CabTallMin = 1.66f, CabTallMax = 1.76f, BeltMin = 0.98f, BeltMax = 1.04f, RakeMin = 0.12f, RakeMax = 0.18f, CabHalfMin = 0.9f, CabHalfMax = 0.94f,
                HoodReach = 0.05f, HoodHalfMin = 0.58f, HoodHalfMax = 0.64f,
            }),
        // Fantasy: a war wagon under a hooped top (its faction's plates and spikes on it), an ornate carriage with a bowed body.
        new("war wagon", "fantasy", "a war wagon: a room under a tall hooped top on low sides, a hard short cab ahead", Wagon, [("Bandit", 2)],
            r => r with
            {
                Languages = [(Hoop, 1)], Mix = 0, Scales = [(BodyScale.Standard, 1)], TallMin = 1.0f, TallMax = 1.08f, FlareShare = 0.3f,
                Brows = [(1.25f, 1), (1f, 1)], Tails = [(1.25f, 1), (1f, 1)], Cabs = [("caged", 1), ("armoured", 1)], Uppers = [("none", 1)], Ride = (0.5, 2, 0.5),
            }),
        new("carriage", "fantasy", "an ornate carriage: a room bowed out at the waist under a crowned top, a coachman's cab ahead", Wagon, [("British", 2), ("Highland", 1)],
            r => r with
            {
                Languages = [(Carriage, 1)], Mix = 0, Scales = [(BodyScale.Standard, 1)], TallMin = 1.0f, TallMax = 1.1f, FlareShare = 0.3f,
                Brows = [(1.5f, 1), (1.25f, 1)], Tails = [(1.5f, 1), (1.25f, 1)], Cabs = [("enclosed", 1), ("caged", 0.5)], Uppers = [("none", 1)], Ride = (0.5, 2, 0.3), DoorRear = 0.5,
            }),
        // One-off concept vehicles: a bubble-canopy car, a single-seat streamliner.
        new("bubble car", "concept", "a bubble-canopy concept car: round from sill to dome, a cab-forward capsule, a short tail", Concept, [("IT", 2), ("Scam", 1)],
            r => r with
            {
                Languages = [(Bubble, 1)], Mix = 0, Cabs = [("cab-over", 2), ("enclosed", 1)], Noses = [('r', 2), ('w', 1)], Ride = (2, 1, 0),
                CabTallMin = 1.58f, CabTallMax = 1.66f, BeltMin = 0.84f, BeltMax = 0.9f, RakeMin = 0.24f, RakeMax = 0.24f, CapsuleRakeMin = 0.6f, CapsuleRakeMax = 0.7f,
                CabHalfMin = 0.9f, CabHalfMax = 0.94f, HoodHalfMin = 0.56f, HoodHalfMax = 0.6f, HoodCrownMin = 0.2f, HoodCrownMax = 0.24f, CabBackMin = 0.5f, CabBackMax = 0.7f,
            }),
        new("streamliner", "concept", "a single-seat streamliner: a low teardrop, a long bonnet, the canopy running down into the tail", Concept, [("Highland", 1.5), ("British", 1.5)],
            r => r with
            {
                Languages = [(Streamliner, 1)], Mix = 0, Cabs = [("enclosed", 1)], Noses = [('r', 2), ('w', 1)], Ride = (3, 1, 0),
                CabTallMin = 1.56f, CabTallMax = 1.6f, BeltMin = 0.86f, BeltMax = 0.9f, RakeMin = 0.24f, RakeMax = 0.24f, CabHalfMin = 0.88f, CabHalfMax = 0.92f,
                HoodReach = 0.15f, HoodHalfMin = 0.56f, HoodHalfMax = 0.6f, HoodCrownMin = 0.16f, HoodCrownMax = 0.18f, CabBackMin = 0.8f, CabBackMax = 0.95f,
            }),
        // A giant truck: the giant scale class over the faction's own languages (the class does the work: wider, taller, longer).
        new("giant hauler", "giant truck", "a giant truck: a land train's room at the giant scale class, a tall cab ahead", GiantTruck, [("Highland", 1), ("IT", 1), ("Bandit", 1)],
            r => r with { Scales = [(BodyScale.Giant, 1)], Uppers = [("none", 1)], Balcony = 0, Ride = (0.2, 2, 0) }),
    ];

    /// <summary>The chance a vehicle of a shape takes one of its style's archetypes instead of the style's own body (a Car always does when its style has one).</summary>
    public static readonly Dictionary<string, double> Share = new(StringComparer.Ordinal) { ["Car"] = 1, ["Box Truck"] = 0.4, ["War Bus"] = 0.3,
        ["Buggy"] = 1, ["Monster Truck"] = 1, ["Wagon"] = 1, ["Giant Truck"] = 1, ["Concept"] = 1 }; // (phase 8b: its new shapes always take one)

    /// <summary>The archetypes a style builds for a shape, with their weights.</summary>
    public static (BodyArchetype A, double W)[] For(FactionStyle st, string shape) =>
        All.Where(a => a.Shapes.Contains(shape)).Select(a => (A: a, W: a.Factions.FirstOrDefault(f => f.Style == st.Name).W)).Where(x => x.W > 0).ToArray();

    /// <summary><c>--archetype NAME</c>: every vehicle of a shape it takes in that archetype, whatever its style (to look at one).</summary>
    public static BodyArchetype? Force;

    /// <summary><c>--no-archetypes</c>: none (the styles' own bodies, as before phase 8a), to measure against.</summary>
    public static bool None;

    /// <summary>The archetype laid over a style's rule, made once (the identity's and the cab's reference caches are keyed by the rule).</summary>
    public static BodyStyleRule RuleOf(BodyArchetype a, FactionStyle st)
    {
        lock (Rules)
        {
            if (!Rules.TryGetValue((a.Name, st.Name), out var r))
                Rules[(a.Name, st.Name)] = r = a.Over(BodyStyleRule.Of(st));
            return r;
        }
    }

    private static readonly Dictionary<(string, string), BodyStyleRule> Rules = new();

    public static BodyArchetype? Find(string name) => All.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A vehicle's archetype, on a fork of its own ("archetype"), so nothing else of a seed moves; null: the style's own body.</summary>
    public static BodyArchetype? Roll(Vehicle v, string shape)
    {
        if (None)
            return null;
        if (Force is { } f)
            return f.Shapes.Contains(shape) ? f : null;
        var list = For(v.Style, shape);
        if (list.Length == 0 || !Share.TryGetValue(shape, out var share))
            return null;
        var d = v.Dice.Fork("archetype");
        return d.Fork("take").Chance(share) ? BodyStyleRule.Weighted(d.Fork("which"), list) : null;
    }
}

public sealed partial class Vehicle
{
    /// <summary>Phase 8a: this vehicle's archetype (null: its style's own body).</summary>
    public BodyArchetype? Archetype { get; private set; }

    private BodyStyleRule? _rule;

    /// <summary>The body rule this vehicle is made by: its style's (<see cref="BodyStyleRule.Of"/>), with its archetype laid over it.</summary>
    public BodyStyleRule Rule => _rule ??= BodyStyleRule.Of(Style);

    /// <summary>Lays an archetype over the style's rule (before the look is rolled).</summary>
    public void UseArchetype(BodyArchetype? a)
    {
        Archetype = a;
        _rule = a == null ? BodyStyleRule.Of(Style) : BodyArchetype.RuleOf(a, Style);
        if (a != null)
            Modules.Add("archetype " + a.Name);
    }
}
