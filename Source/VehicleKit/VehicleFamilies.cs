using System.Globalization;
using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// A faction model line's member (docs/vehiclekit/VEHICLEKIT.md, "Families and convoys"): a base (style, role, the family's base tier, seed and layer seeds) and a mark
/// (1 to 3) or a variant. The same model always makes the same vehicle, so this small record is all a world, a log or a convoy needs to keep to spawn it again:
/// <see cref="Key"/> writes it ("Bandit/raider/2/733786/mk2/gun truck/scheme 55/wear 9"), <see cref="TryParse"/> reads it back.
/// </summary>
/// <param name="Tier">The family's base tier: Mk1's. A later mark is asked at a tier more each (to 4), on Mk1's body and chassis.</param>
/// <param name="Scheme">A convoy's shared paint (<see cref="VehicleConvoys.SchemeOf"/>); null: the style's whole palette.</param>
/// <param name="Layers">Kit 3's layer overrides (a convoy varies the wear); kept only on the grammar's shapes (the rest have no layers).</param>
/// <param name="Named">A rare named unique: null rolls it from the seed (<see cref="VehicleFamilies.RollNamed"/>), true or false forces it.</param>
public sealed record VehicleModel(string Style, string Role, int Tier, int Seed, int Mark = 1, string Variant = "", int? Scheme = null, VehicleLayers Layers = default, bool? Named = null)
{
    /// <summary>A rare named unique: its own name, the best loot, one more chest and one more mark, a tier up.</summary>
    public bool IsNamed => Named ?? VehicleFamilies.RollNamed(this);

    /// <summary>The model's key: everything that makes it, as ASCII text.</summary>
    public string Key
    {
        get
        {
            var bits = new List<string> { Style, Role, Tier.ToString(CultureInfo.InvariantCulture), Seed.ToString(CultureInfo.InvariantCulture), $"mk{Mark}" };
            if (Variant.Length > 0)
                bits.Add(Variant);
            if (Scheme is { } s)
                bits.Add($"scheme {s}");
            if (Named is { } n)
                bits.Add(n ? "named" : "plain");
            if (Layers.Any)
            {
                var me = Layers;
                bits.Add(string.Join(",", VehicleLayers.All.Where(l => me[l] != null).Select(l => $"{VehicleLayers.Name(l)} {me[l]}")));
            }
            return string.Join("/", bits);
        }
    }

    public override string ToString() => Key;

    /// <summary>A key back to its model, or why not.</summary>
    public static bool TryParse(string key, out VehicleModel? model, out string why)
    {
        model = null;
        why = "";
        var bits = key.Split('/', StringSplitOptions.TrimEntries);
        if (bits.Length < 5 || !int.TryParse(bits[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tier) || !int.TryParse(bits[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)
            || !bits[4].StartsWith("mk", StringComparison.OrdinalIgnoreCase) || !int.TryParse(bits[4][2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mark))
        {
            why = $"\"{key}\" isn't a model key: style/role/tier/seed/mkN[/variant][/scheme S][/named][/wear W]";
            return false;
        }
        var m = new VehicleModel(bits[0], bits[1], tier, seed, mark);
        foreach (var b in bits.Skip(5))
        {
            if (VehicleFamilies.Variants.Contains(b))
                m = m with { Variant = b };
            else if (b.StartsWith("scheme ", StringComparison.Ordinal) && int.TryParse(b[7..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                m = m with { Scheme = s };
            else if (b is "named" or "plain")
                m = m with { Named = b == "named" };
            else if (VehicleLayers.TryParse(b, out var layers, out _))
                m = m with { Layers = layers };
            else
            {
                why = $"\"{b}\" in \"{key}\": a variant ({string.Join(", ", VehicleFamilies.Variants)}), scheme N, named, plain or layer seeds";
                return false;
            }
        }
        model = m;
        return true;
    }
}

/// <summary>
/// Faction model lines (docs/vehiclekit/VEHICLEKIT.md, "Families and convoys"). A family is a style, a role, a base tier and a seed: its Mk1, Mk2 and Mk3 share the
/// seed's body and chassis layers (the frame leans by the family's base tier: <see cref="RoleAsk.FrameAt"/>) and change the kit, the appliqué and the tier, so a
/// Mk2 is the same truck a tier up, plated; a variant changes one layer on purpose (the kit: up-armoured, a gun truck, a tanker conversion). Each mark is made from
/// its first plan that builds, never planned again for its role, so the marks keep one body (a miss is reported, as any role vehicle's is). A low-chance named unique
/// (its own name, a tier up, the best loot, one more chest and one more mark) is rolled from the seed. Names: <see cref="VehicleFamilyNames"/>, by style.
/// </summary>
public static class VehicleFamilies
{
    public const string UpArmoured = "up-armoured", GunTruck = "gun truck", TankerConversion = "tanker conversion";

    /// <summary>Every variant: each changes the kit layer on purpose, on the mark's own body and chassis.</summary>
    public static readonly string[] Variants = [UpArmoured, GunTruck, TankerConversion];

    public const int Marks = 3;

    /// <summary>How often a model is a named unique: a leader's role (command, boss) and every other.</summary>
    public const double NamedLeader = 0.08, NamedOther = 0.02;

    /// <summary>Whether a model is a named unique, from its seed: the same model always rolls the same.</summary>
    public static bool RollNamed(VehicleModel m) =>
        Dice.Of(m.Seed).Fork($"family named {m.Style} {m.Role} {m.Mark} {m.Variant}").Chance(m.Role is "command" or "boss" ? NamedLeader : NamedOther);

    /// <summary>What a model is made from: its style, role, generator (the shape its seed picks, the same for every mark) and the role ask. Throws what can't be.</summary>
    public static (FactionStyle St, VehicleRole Role, IVehicleGenerator G, RoleAsk Ask, VehicleLayers Layers) Resolve(VehicleModel m)
    {
        var st = Styles.All.FirstOrDefault(s => s.Name.Equals(m.Style, StringComparison.OrdinalIgnoreCase)) ?? throw new VehicleKitException($"no faction style \"{m.Style}\"");
        var role = VehicleRoles.Find(m.Role) ?? throw new VehicleKitException($"no role \"{m.Role}\"");
        if (m.Mark is < 1 or > Marks)
            throw new VehicleKitException($"a mark is 1 to {Marks}");
        if (m.Variant.Length > 0 && !Variants.Contains(m.Variant))
            throw new VehicleKitException($"no variant \"{m.Variant}\": {string.Join(", ", Variants)}");
        var g = VehicleRoles.ShapeFor(st, role, m.Seed);
        var frame = Math.Clamp(m.Tier, 1, VehicleRoles.MaxTier);
        var tier = Math.Clamp(frame + m.Mark - 1 + (m.IsNamed ? 1 : 0), 1, VehicleRoles.MaxTier);
        var ask = new RoleAsk(role, tier, st, m, frame)
        {
            Shape = VehicleSpawnKit.Names(g).Shape,
            Scheme = m.Scheme is { } s ? VehicleConvoys.SchemeOf(st, s) : null,
        };
        var layers = ShapeGrammar.RecipeOf(g) != null ? m.Layers : default; // (a shape the grammar doesn't make has no layers: its marks are the seed's)
        if (m.Variant is UpArmoured or GunTruck && ShapeGrammar.RecipeOf(g) != null && layers.Kit == null)
            layers = layers with { Kit = VehicleSpawnKit.Reroll(m.Seed, 300 + Array.IndexOf(Variants, m.Variant)) }; // (the layer it changes on purpose: its own kit, on the family's body and chassis)
        return (st, role, g, ask, layers);
    }

    /// <summary>A model's vehicle, made as the dynamic spawn makes it (<see cref="VehicleGenJob"/> runs the same).</summary>
    public static Vehicle Make(VehicleModel m)
    {
        var (_, _, g, ask, layers) = Resolve(m);
        return VehicleRegistry.Make(g, m.Seed, layers: layers.Any ? layers : null, role: ask);
    }

    /// <summary>The model's name, as players read it: "Bandit Rustbucket Mk2 Gun Truck"; a named unique's own ("Bandit Old Mother Rust").</summary>
    public static string DisplayName(VehicleModel m)
    {
        var names = VehicleFamilyNames.For(m.Style);
        var style = Styles.All.FirstOrDefault(s => s.Name.Equals(m.Style, StringComparison.OrdinalIgnoreCase))?.Name ?? m.Style;
        if (m.IsNamed)
            return $"{style} {Dice.Of(m.Seed).Fork("family named name " + m.Role).Pick(names.Named)}";
        var model = ModelName(m);
        var mark = names.Marks[Math.Clamp(m.Mark, 1, Marks) - 1];
        var variant = m.Variant.Length > 0 ? names.VariantLabel(m.Variant) : "";
        return string.Join(" ", new[] { style, model, mark, variant }.Where(x => x.Length > 0));
    }

    /// <summary>The family's model name from the style's list, by its seed and role ("Rustbucket"): every mark and variant of the family shares it.</summary>
    public static string ModelName(VehicleModel m) => Dice.Of(m.Seed).Fork("family name " + m.Role).Pick(VehicleFamilyNames.For(m.Style).Models);

    /// <summary>A mark's vehicle once made: named for its model, the key in the kit line, the description says what it is.</summary>
    internal static void Note(Vehicle v, RoleAsk ask, VehicleModel m, string shape)
    {
        var display = DisplayName(m);
        v.Name = $"{display} [{ask.Label}]";
        v.KitNotes.Insert(0, $"model {m.Key}");
        if (v.PlanAttempt > 0)
            v.KitNotes.Add($"plan {v.PlanAttempt + 1}"); // (its first plans didn't build: a mark on another plan has another body)
        var what = m.IsNamed ? $"{display}, a named {m.Style} {shape}, one of a kind" : $"The {display[(display.IndexOf(' ') + 1)..]}, a {m.Style} {shape}{(m.Mark > 1 ? $", mark {m.Mark}" : "")}{(m.Variant.Length > 0 ? ", " + m.Variant : "")}";
        v.Description = $"{what}. {v.Description}";
    }
}

public sealed partial class Vehicle
{
    /// <summary>Which plan of the shape grammar was kept (kit 4): a family's marks share a body when they share it.</summary>
    public int PlanAttempt { get; set; }

    /// <summary>
    /// How far the vehicle reaches ahead of its origin and behind it (m, along -z and +z), over its fixed parts: a convoy spaces its members by these (VehicleSpawn.cs).
    /// </summary>
    public (float Front, float Rear) ExtentZ()
    {
        float lo = float.MaxValue, hi = float.MinValue;
        for (var i = 0; i < C.PartCount; i++)
        {
            var seg = C.Parts[i].Seg;
            if (seg > 0 && seg <= C.JointCount && C.Joints[seg - 1].Freeform)
                continue;
            C.PartBounds(i, out var a, out var b);
            lo = MathF.Min(lo, a.Z);
            hi = MathF.Max(hi, b.Z);
        }
        return lo > hi ? (2, 2) : (-lo, hi);
    }

    /// <summary>
    /// Appliqué for a vehicle with no room (a family's Mk2 and Mk3, its up-armoured variant): the style's heaviest armour plate, a metre square, welded upright a few
    /// centimetres proud of the outermost plate or beam of the body at a station along each side, 0.35 m and more over the ground, only where it meets nothing
    /// else (wheels, guns, ladders, drums), a metre and more from any other weld (the hull's appliqué included). As many as the lean asks, from a third of the stations
    /// at 0.3 up, and at least <paramref name="least"/> (a Mk2 two, a Mk3 and an up-armoured variant four) where there is room.
    /// </summary>
    private void SideArmour(double lean, int least, Dice d)
    {
        var material = Style.Parts.GetValueOrDefault("armour")?.OrderByDescending(VehicleRoles.PlateArmour).ThenBy(x => x, StringComparer.Ordinal).FirstOrDefault() ?? "Plate Steel";
        if (ConstructParts.Find(material + " 1 m") is not { } def)
            return;
        var e = def.BoundsMax - def.BoundsMin;
        var thick = MathF.Min(e.X, MathF.Min(e.Y, e.Z));
        // The fixed parts' boxes in vehicle coordinates (not on joints, not welded, not wheels), and the ground under the wheels.
        var boxes = new List<(int Index, Vector3 Lo, Vector3 Hi, bool Host)>();
        var ground = float.MaxValue;
        for (var i = 0; i < C.PartCount; i++)
        {
            C.PartBounds(i, out var mn, out var mx);
            var (lo, hi) = (FromRest(mn), FromRest(mx));
            var role = C.DefOf(i).Role;
            if (role == PartRole.Wheel)
            {
                ground = MathF.Min(ground, lo.Y);
                continue;
            }
            var seg = C.Parts[i].Seg;
            if (seg != 0 && (seg > C.JointCount || C.Joints[seg - 1].Freeform || !C.Rigid(seg)))
                continue;
            boxes.Add((i, lo, hi, role is PartRole.Panel or PartRole.Frame));
        }
        if (boxes.Count == 0 || ground == float.MaxValue)
            return;
        var z0 = boxes.Min(b => b.Lo.Z) + 0.6f;
        var z1 = boxes.Max(b => b.Hi.Z) - 0.6f;
        var spots = new List<(VPart Host, Vector3 At, float Side)>();
        for (var z = z0; z <= z1; z += 1.1f)
            foreach (var side in new[] { -1f, 1f })
            {
                // The outermost thing on this side at this station, between knee and head height over the ground.
                (int Index, Vector3 Lo, Vector3 Hi, bool Host)? out_ = null;
                foreach (var b in boxes)
                {
                    if (b.Lo.Z > z + 0.3f || b.Hi.Z < z - 0.3f || b.Hi.Y < ground + 0.5f || b.Lo.Y > ground + 2.2f)
                        continue;
                    var edge = side > 0 ? b.Hi.X : -b.Lo.X;
                    if (out_ is not { } o || edge > (side > 0 ? o.Hi.X : -o.Lo.X))
                        out_ = b;
                }
                if (out_ is not { Host: true } host)
                    continue;
                var y = MathF.Max(ground + 0.9f, MathF.Min((host.Lo.Y + host.Hi.Y) * 0.5f, host.Hi.Y - 0.1f));
                var x = (side > 0 ? host.Hi.X : host.Lo.X) + side * (thick * 0.5f + 0.04f);
                if (Parts.FirstOrDefault(p => p.Index == host.Index) is { } vp)
                    spots.Add((vp, new Vector3(x, y, Math.Clamp(z, host.Lo.Z - 0.3f, host.Hi.Z + 0.3f)), side));
            }
        var want = Math.Max(Math.Max(2, least), (int)MathF.Round((float)(lean * spots.Count * 0.35)));
        var placed = new List<Vector3>();
        foreach (var (host, at, side) in d.Shuffled(spots))
        {
            if (placed.Count >= want)
                break;
            VehicleGenJob.Yield(); // (each try reads every part's bounds)
            if (placed.Any(p => Vector3.Distance(p, at) < 1.05f) || _welded.Any(w => Vector3.Distance(w, at) < 0.9f) || !Clear(host, at, new Vector3(thick * 0.5f + 0.02f, 0.5f, 0.5f)))
                continue;
            var p = Weld(def.Name, host, at, Orient.Facing(new Vector3(side, 0, 0)), 0, 0, 0);
            PanelList("armour").Add(p);
            _welded.Add(at);
            placed.Add(at);
            if (Style.Palette.ContainsKey("body"))
                Paint(p, Style.Colour(d, "body"));
        }
        if (placed.Count > 0)
            MarksPlaced.Add($"armour: {placed.Count} {def.Name} on the sides");
    }

    /// <summary>
    /// A family's own pass, inside the role pass and on forks of the kit layer: a tanker conversion's drums on its open deck (up to three, clear of the guns, under
    /// open sky, never coplanar with what is there), and a named unique's one more mark (the style's masts, else its trophies) on the marks pass's spots.
    /// </summary>
    internal void FamilyKit(RoleAsk ask)
    {
        var least = ask.Variant == VehicleFamilies.UpArmoured ? 4 : ask.Model!.Mark >= 3 ? 4 : ask.Model.Mark >= 2 ? 2 : 0;
        if (ask.KitArmourLean >= 0.3 && (Rooms.Count == 0 || least > 0))
            SideArmour(ask.KitArmourLean, least, Dice.Fork("kit family armour")); // (a later mark is plated where it shows: the sides, past what the hull took)
        if (ask.Variant == VehicleFamilies.TankerConversion)
        {
            var d = Dice.Fork("kit family drums");
            var drums = 0;
            for (var k = 0; k < 3; k++)
            {
                VehicleGenJob.Yield();
                if (OnDeck("Fuel Drum", null, d, clear: true) is null)
                    break;
                drums++;
            }
            if (drums > 0)
                MarksPlaced.Add($"tanker conversion: {drums} Fuel Drum on the deck");
        }
        if (ask.Named)
        {
            var slot = Style.Parts.ContainsKey("mast") ? "mast" : "trophy";
            var d = Dice.Fork("kit family named");
            var parts = d.Shuffled((Style.Parts.GetValueOrDefault(slot) ?? []).Distinct()).Where(x => ConstructParts.Find(x) is { } def && Array.Exists(def.Sockets, s => s.Name == "base")).ToList();
            VehicleGenJob.Yield();
            if (parts.Count > 0 && PlaceMark(parts, d) is { } placed)
                MarksPlaced.Add($"named: a {placed.Name} on the {_markHost}");
        }
    }
}
