namespace CallOfSlop.VehicleKit;

/// <summary>A kit-3 chassis as rolled: the running gear (a <see cref="Gear"/> the frame builds from), where the front axle stands, and any second axle.</summary>
public sealed record ChassisPlan(Gear Gear, float FrontZ, float Inset, string Extra, float Spacing, string Label)
{
    /// <summary>A free-rolling axle behind the driven one ("tag") or ahead of it ("pusher"); "tandem" is in <see cref="Gear.Tandem"/>; "" for none.</summary>
    public bool Tag => Extra is "tag" or "pusher";

    /// <summary>The second axle's station from the driven one's: + behind, - ahead, 0 none.</summary>
    public float ExtraAt => Extra switch { "tag" or "tandem" => Spacing, "pusher" => -Spacing, _ => 0 };

    /// <summary>The frame ahead of the front axle: the bumper stays at z -2.3 (or a quarter on with the axle forward); a rail run is 0.5 m and up.</summary>
    public float Nose => FrontZ > -1.5f ? FrontZ + 2.3f : 0.625f;
}

/// <summary>
/// The chassis layer (kit 3; docs/vehiclekit/VEHICLEKIT.md, "Layered variety"). The style's own heavy gear recipes are the corners: each recipe gives a front corner (spring
/// and wheel on a Steering Pivot) and a rear one (spring, wheel, the differential and the floor's height it allows). A chassis takes a recipe, sometimes swaps its front or rear
/// for another recipe's, sometimes a wheel or a spring for another of its class (real parts only: a wheel no bigger than the one it replaces under a body, a spring of the same
/// mount), then an axle layout (one rear axle, a tag, a pusher or a driven tandem, at a spacing), where the front axle stands and how far in the rear one is, and the
/// front track. Everything rolls from the chassis layer's dice, so the rest of the vehicle keeps its seed.
/// </summary>
public static class VehicleChassis
{
    private static readonly string[] FrontWheels = ["Off-Road Wheel", "Large Wheel", "Spiked Wheel", "Solid Wheel"];
    private static readonly string[] UnderWheels = ["Off-Road Wheel", "Spiked Wheel", "Solid Wheel"];

    /// <summary>Springs that bolt the same way and carry the same load class: one may stand in for another.</summary>
    private static readonly string[][] SpringClasses =
    [
        ["Leaf Spring", "Heavy Suspension", "Hydraulic Suspension"],
        ["Double Wishbone", "Heavy Double Wishbone"],
        ["Swing Arm", "Heavy Swing Arm"],
    ];

    /// <summary>A spring part's kind, for the trait table and the kit line: leaf, coil, hydraulic, wishbone, swing arm, trailing arm, strut, long travel, rigid.</summary>
    public static string TypeOf(string spring) => spring switch
    {
        "Leaf Spring" => "leaf",
        "Rigid Mount" => "rigid",
        "Hydraulic Suspension" => "hydraulic",
        "Long-Travel Suspension" => "long travel",
        _ when spring.Contains("Wishbone", StringComparison.Ordinal) => "wishbone",
        _ when spring.Contains("Swing Arm", StringComparison.Ordinal) => "swing arm",
        _ when spring.Contains("Trailing Arm", StringComparison.Ordinal) => "trailing arm",
        _ when spring.Contains("Pogo", StringComparison.Ordinal) || spring.Contains("Strut", StringComparison.Ordinal) => "strut",
        _ => "coil",
    };

    private static float Radius(string wheel) => ConstructParts.Find(wheel) is { } d && d.WheelRadius > 0 ? d.WheelRadius : 0.45f;

    private static T Weighted<T>(Dice d, IReadOnlyList<(T Item, double W)> items)
    {
        var total = items.Sum(x => x.W);
        var roll = d.Next() * total;
        foreach (var (item, w) in items)
            if ((roll -= w) < 0)
                return item;
        return items[^1].Item;
    }

    /// <summary>
    /// Rolls a chassis for a body ending <paramref name="end"/> m back, with <paramref name="segments"/> segments, under the gear names the body's modules allow.
    /// <paramref name="cabOver"/>: a cab-over shell stands over the front axle, so it stays where kit 2 put it. <paramref name="fixedInset"/>: a hutch's gun stands
    /// where the Gun Wagon's frame puts it.
    /// </summary>
    public static ChassisPlan Roll(Dice c, FactionStyle st, string[] only, float end, int segments, bool cabOver, float? fixedInset, bool gunDeck, ChassisRange? range = null)
    {
        var r = range ?? LayerTables.ChassisOf(st); // (kit 4: a role's chassis, the style's table leaned by what the role needs: VehicleRoles.cs)
        var gears = st.HeavyGear.Where(g => only.Contains(g.Name)).ToArray();
        if (gears.Length == 0)
            throw new VehicleKitException($"style {st.Name} has no gear named {string.Join(", ", only)}");

        // The axle layout first: a second axle needs a high floor on a wide differential (a low one sits between wheels thrown out on arms), so when one is
        // rolled the rear corners come from the recipes that have one.
        static bool Wide(Gear g) => g.Floor >= 0.5f && g.Differential.StartsWith("Wide", StringComparison.Ordinal);
        var wides = gears.Where(Wide).ToArray();
        var ld = c.Fork("layout");
        var layouts = new List<(string, double)> { ("", r.Single) };
        var longer = Math.Clamp((end - 4.2) / 2.5, 0.3, 2); // (a second axle earns its place under a long body)
        if (wides.Length > 0 && only.Contains("tandem"))
            layouts.AddRange([("tag", r.Tag * longer), ("pusher", r.Pusher * longer)]);
        if (wides.Length > 0 && only.Contains("six"))
            layouts.Add(("tandem", r.Tandem * longer));
        var extra = Weighted(ld, layouts.Where(x => x.Item2 > 0).ToList());
        var spacing = extra.Length > 0 ? ld.Pick(r.Spacings) : 0;
        var rears = extra.Length > 0 ? wides : gears;

        var gear = c.Fork("recipe").Pick(rears);
        FactionStyle.Noted("gear", gear.Name);
        var mixed = new List<string>();

        // Front and rear from different recipes.
        var md = c.Fork("mix");
        if (gears.Length > 1 && md.Chance(r.Mix))
        {
            var swapFront = md.Chance(0.5);
            var other = md.Pick(swapFront ? gears : rears);
            if (other != gear)
            {
                if (swapFront)
                    gear = gear with { FrontSpring = other.FrontSpring, FrontWheel = other.FrontWheel };
                else // (the rear brings its differential and the floor it allows: a swing arm's throw is what lets a floor sit low)
                    gear = gear with { RearSpring = other.RearSpring, RearWheel = other.RearWheel, Differential = other.Differential, Floor = other.Floor };
                mixed.Add(other.Name);
            }
        }

        // Wheels and springs swapped within their class.
        var sw = c.Fork("swap");
        if (sw.Chance(r.WheelSwap))
        {
            var fronts = FrontWheels.Where(w => w != gear.FrontWheel && Radius(w) <= 0.56f).ToArray();
            if (fronts.Length > 0)
                gear = gear with { FrontWheel = sw.Pick(fronts) };
        }
        if (sw.Chance(r.WheelSwap) && gear.RearWheel != "Dually Wheel")
        {
            var under = UnderWheels.Where(w => w != gear.RearWheel && Radius(w) <= Radius(gear.RearWheel) + 0.001f).ToArray(); // (no bigger: a wheel's top under a floor meets the walls hung below it)
            if (under.Length > 0)
                gear = gear with { RearWheel = sw.Pick(under) };
        }
        string Swap(string spring) => SpringClasses.FirstOrDefault(k => k.Contains(spring)) is { } cls ? sw.Pick(cls) : spring;
        if (sw.Chance(r.SpringSwap))
            gear = gear with { FrontSpring = Swap(gear.FrontSpring) };
        if (sw.Chance(r.SpringSwap))
            gear = gear with { RearSpring = Swap(gear.RearSpring) };

        // The second axle on that rear.
        gear = gear with { Tag = null, TagWheel = null, Tandem = 0 };
        if (extra == "tandem")
            gear = gear with { Tandem = spacing };
        else if (extra.Length > 0)
            gear = gear with
            {
                Tag = ld.Chance(r.Rigid) ? "Rigid Mount" : gear.RearSpring is "Leaf Spring" or "Heavy Suspension" or "Hydraulic Suspension" ? gear.RearSpring : "Leaf Spring",
                TagWheel = Radius(gear.RearWheel) <= 0.451f && gear.RearWheel != "Dually Wheel" ? gear.RearWheel : "Off-Road Wheel",
            };

        // Where the axles stand: the front by the style's stations (a cab-over keeps kit 2's), the rear an inset from the body's end, the front track.
        var pd = c.Fork("stance");
        var front = cabOver ? -1.675f : Weighted(pd, r.FrontAxles);
        var most = segments == 1 && end <= 5.5f ? MathF.Min(r.InsetMax, 1.75f) : r.InsetMax; // (kit 2's: a short one-room body keeps its axle near the end)
        var least = r.InsetMin;
        if (extra == "tag" && gunDeck)
            least = MathF.Max(least, spacing); // (the stern gun deck stands a metre past the body: the tag stays under it)
        var steps = Math.Max(0, (int)MathF.Round((most - least) / 0.25f));
        var inset = fixedInset ?? MathF.Min(most, least + 0.25f * pd.Int(0, steps));
        if (extra == "pusher" && end - inset - spacing < 0.8f)
        {
            extra = !gunDeck || inset >= spacing ? "tag" : ""; // (a pusher would stand under the cab: it goes behind instead, or not at all)
            if (extra == "")
                gear = gear with { Tag = null, TagWheel = null };
        }
        var wing = Weighted(pd, r.FrontWings);
        if (front != -1.675f)
            wing = 0.5f; // (one change at a time at the front: a moved axle keeps kit 2's track)
        gear = gear with { FrontWing = wing, Name = mixed.Count > 0 ? gear.Name + "/" + string.Join("/", mixed) : gear.Name };

        var label = $"{(extra == "" ? "2 axles" : "3 axles, " + extra + $" {spacing:0.##} m")}, front {(front == -1.675f ? "under the hood" : front < -1.7f ? "forward" : "set back")}, inset {inset:0.##} m, "
            + $"{TypeOf(gear.FrontSpring)}/{TypeOf(gear.RearSpring)}{(gear.Tag != null ? "/" + TypeOf(gear.Tag) : "")}, wing {wing:0.##}";
        FactionStyle.Noted("chassis axles", extra == "" ? "single" : extra);
        FactionStyle.Noted("chassis front", front.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        return new ChassisPlan(gear, front, inset, extra, spacing, label);
    }
}
