using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>A colour in a weapon's "model" block: a named finish, "accent" (the weapon's accent colour) or an RGB value.</summary>
public readonly record struct GunColor(string? Name, Vector3 Rgb)
{
    public bool IsAccent => Name == "accent";

    /// <summary>The named finishes ("black", "gunmetal", "wood"...), shared by every gun.</summary>
    public static readonly Dictionary<string, Vector3> Named = new()
    {
        ["black"] = new(0.17f, 0.17f, 0.19f),
        ["gunmetal"] = new(0.29f, 0.3f, 0.33f),
        ["steel"] = new(0.5f, 0.52f, 0.55f),
        ["chrome"] = new(0.76f, 0.78f, 0.82f),
        ["brass"] = new(0.76f, 0.6f, 0.3f),
        ["copper"] = new(0.72f, 0.42f, 0.26f),
        ["polymer"] = new(0.31f, 0.31f, 0.33f),
        ["tan"] = new(0.62f, 0.54f, 0.4f),
        ["olive"] = new(0.33f, 0.36f, 0.24f),
        ["wood"] = new(0.55f, 0.36f, 0.2f),
        ["darkwood"] = new(0.36f, 0.22f, 0.12f),
        ["lightwood"] = new(0.7f, 0.5f, 0.3f),
        ["white"] = new(0.86f, 0.86f, 0.84f),
        ["rust"] = new(0.47f, 0.29f, 0.18f),
        ["orange"] = new(0.95f, 0.5f, 0.12f),
        ["yellow"] = new(0.92f, 0.74f, 0.15f),
    };

    public Vector3 Resolve(Vector3 accent) => Name == null ? Rgb : Name == "accent" ? accent : Named[Name];
}

/// <summary>
/// The "model" block of a weapon file, resolved: what the gun is built from (see <see cref="GunBuilder"/> and the
/// "Gun models" part of Weapons/README.md). A block starts from an archetype ("base": pump_shotgun, bolt_sniper...),
/// whose sizes are fractions of the weapon's length and multiples of its bulk; anything the file gives is used as is
/// (metres and degrees). Weapons without a block are drawn by the older per-class models in <see cref="GunModel"/>.
/// </summary>
public sealed class GunSpec
{
    public string Base = "rifle";
    public string Receiver = "box";
    public float ReceiverLength, ReceiverHeight, ReceiverWidth;
    public int Barrels = 1;
    public string BarrelLayout = "single", BarrelStyle = "round";
    public float BarrelLength, BarrelWidth;
    public string Shroud = "none";
    public float ShroudLength;
    public string Handguard = "rail";
    public float HandguardLength, HandguardHeight;
    public string Stock = "fixed";
    public float StockLength, StockDrop;
    public bool CheekRest;
    public string Grip = "pistol";
    public float GripAngle;
    public string Mag = "box";
    public float MagLength, MagOffset, MagAngle;
    public string Foregrip = "none", Bipod = "none", Bolt = "charging", Sights = "post", Muzzle = "none";
    public bool CarryHandle, TopRail, Lever, Hammer, GasBlock, Canister, SideSaddle, Ramrod;
    public int Coils;
    public GunColor Finish, Furniture, MagColor;
    public string AccentStyle = "mag";

    // Copied from the weapon when the block is read (attachments don't change the model's proportions).
    public int Cap;
    public float Caliber;
    public Vector3 Accent;

    /// <summary>The block as written in the file (key and raw JSON value, in order), for writing it back out.</summary>
    public (string Key, string Json)[] Given = [];

    private delegate void Setter(GunSpec s, object v);

    private sealed record Param(string Key, char Kind, string[]? Options, float Min, float Max, char Scale, Setter Set);

    private static Param E(string k, Setter s, params string[] o) => new(k, 'e', o, 0, 0, ' ', s);
    private static Param N(string k, float min, float max, char scale, Setter s) => new(k, 'n', null, min, max, scale, s);
    private static Param I(string k, int min, int max, Setter s) => new(k, 'i', null, min, max, ' ', s);
    private static Param B(string k, Setter s) => new(k, 'b', null, 0, 0, ' ', s);
    private static Param C(string k, Setter s) => new(k, 'c', null, 0, 0, ' ', s);

    /// <summary>Every key a "model" block may hold. Scale L = an archetype value is a fraction of the weapon's length, B = a multiple of its bulk.</summary>
    private static readonly Param[] Params =
    [
        E("receiver", (s, v) => s.Receiver = (string)v, "box", "slab", "round", "slide", "revolver", "bullpup", "lever", "break", "tool", "toy", "sci"),
        N("receiverLength", 0.03f, 1f, 'L', (s, v) => s.ReceiverLength = (float)v),
        N("receiverHeight", 0.02f, 0.4f, 'B', (s, v) => s.ReceiverHeight = (float)v),
        N("receiverWidth", 0.015f, 0.3f, 'B', (s, v) => s.ReceiverWidth = (float)v),
        I("barrels", 1, 8, (s, v) => s.Barrels = (int)v),
        E("barrelLayout", (s, v) => s.BarrelLayout = (string)v, "single", "side", "over", "rotary"),
        E("barrelStyle", (s, v) => s.BarrelStyle = (string)v, "round", "square", "fluted"),
        N("barrelLength", 0f, 1.5f, 'L', (s, v) => s.BarrelLength = (float)v),
        N("barrelWidth", 0.005f, 0.15f, 'B', (s, v) => s.BarrelWidth = (float)v),
        E("shroud", (s, v) => s.Shroud = (string)v, "none", "jacket", "perforated", "heatshield", "suppressor", "coils", "rails"),
        N("shroudLength", 0.05f, 1f, ' ', (s, v) => s.ShroudLength = (float)v),
        E("handguard", (s, v) => s.Handguard = (string)v, "none", "rail", "slim", "ribbed", "wood", "quad", "tube", "forend", "pump", "stock"),
        N("handguardLength", 0f, 1.2f, 'L', (s, v) => s.HandguardLength = (float)v),
        N("handguardHeight", 0.01f, 0.3f, 'B', (s, v) => s.HandguardHeight = (float)v),
        E("stock", (s, v) => s.Stock = (string)v, "none", "fixed", "crane", "skeleton", "folding", "wire", "wood", "straight", "thumbhole", "chassis", "bullpup", "toy", "spade"),
        N("stockLength", 0.02f, 0.6f, ' ', (s, v) => s.StockLength = (float)v),
        N("stockDrop", -15f, 30f, ' ', (s, v) => s.StockDrop = (float)v),
        B("cheekRest", (s, v) => s.CheekRest = (bool)v),
        E("grip", (s, v) => s.Grip = (string)v, "pistol", "wood", "straight", "birdshead", "none"),
        N("gripAngle", -10f, 50f, ' ', (s, v) => s.GripAngle = (float)v),
        E("mag", (s, v) => s.Mag = (string)v, "box", "curved", "stick", "drum", "tube", "pan", "topbox", "side", "internal", "beltbox", "helical", "grip", "cell", "strip", "striker", "none"),
        N("magLength", 0f, 0.8f, ' ', (s, v) => s.MagLength = (float)v),
        N("magOffset", -0.5f, 0.5f, ' ', (s, v) => s.MagOffset = (float)v),
        N("magAngle", -45f, 45f, ' ', (s, v) => s.MagAngle = (float)v),
        E("foregrip", (s, v) => s.Foregrip = (string)v, "none", "vertical", "stub", "folding", "strap"),
        E("bipod", (s, v) => s.Bipod = (string)v, "none", "folded", "deployed"),
        E("bolt", (s, v) => s.Bolt = (string)v, "none", "side", "rear", "charging", "top"),
        E("sights", (s, v) => s.Sights = (string)v, "post", "tall", "bead", "aperture", "ghost", "none"),
        E("muzzle", (s, v) => s.Muzzle = (string)v, "none", "crown", "flash", "brake", "bell", "comp", "tip", "breacher", "ported"),
        B("carryHandle", (s, v) => s.CarryHandle = (bool)v),
        B("topRail", (s, v) => s.TopRail = (bool)v),
        B("lever", (s, v) => s.Lever = (bool)v),
        B("hammer", (s, v) => s.Hammer = (bool)v),
        B("gasBlock", (s, v) => s.GasBlock = (bool)v),
        B("canister", (s, v) => s.Canister = (bool)v),
        B("sideSaddle", (s, v) => s.SideSaddle = (bool)v),
        B("ramrod", (s, v) => s.Ramrod = (bool)v),
        I("coils", 0, 10, (s, v) => s.Coils = (int)v),
        C("finish", (s, v) => s.Finish = (GunColor)v),
        C("furniture", (s, v) => s.Furniture = (GunColor)v),
        C("magColor", (s, v) => s.MagColor = (GunColor)v),
        E("accentStyle", (s, v) => s.AccentStyle = (string)v, "none", "stripe", "mag", "panel", "glow", "tips", "bands"),
    ];

    /// <summary>The keys a model block may hold (for the README check and error messages).</summary>
    public static IEnumerable<string> Keys => Params.Select(p => p.Key).Prepend("base");

    /// <summary>Every archetype a block can start from.</summary>
    public static IEnumerable<string> Archetypes => ArchetypeJson.Keys;

    private const string BaseJson = """
        {"receiver": "box", "receiverLength": 0.48, "receiverHeight": 0.1, "receiverWidth": 0.068, "barrels": 1, "barrelLayout": "single", "barrelStyle": "round",
         "barrelLength": 0.7, "barrelWidth": 0.03, "shroud": "none", "shroudLength": 0.6, "handguard": "rail", "handguardLength": 0.42, "handguardHeight": 0.085,
         "stock": "fixed", "stockLength": 0.22, "stockDrop": 0.0, "cheekRest": false, "grip": "pistol", "gripAngle": 17.0, "mag": "box", "magLength": 0.0,
         "magOffset": 0.0, "magAngle": 0.0, "foregrip": "none", "bipod": "none", "bolt": "charging", "sights": "post", "muzzle": "none", "carryHandle": false,
         "topRail": false, "lever": false, "hammer": false, "gasBlock": false, "canister": false, "sideSaddle": false, "ramrod": false, "coils": 0,
         "finish": "black", "furniture": "polymer", "magColor": "accent", "accentStyle": "mag"}
        """;

    private static readonly Dictionary<string, string> ArchetypeJson = new()
    {
        ["pistol"] = """{"receiver": "slide", "receiverLength": 0.95, "receiverHeight": 0.075, "receiverWidth": 0.05, "barrelLength": 0.06, "barrelWidth": 0.022, "handguard": "none", "handguardLength": 0.0, "stock": "none", "gripAngle": 14.0, "mag": "grip", "bolt": "none", "accentStyle": "stripe"}""",
        ["revolver"] = """{"receiver": "revolver", "receiverLength": 0.45, "receiverHeight": 0.08, "receiverWidth": 0.05, "barrelLength": 0.55, "barrelWidth": 0.024, "handguard": "none", "handguardLength": 0.0, "stock": "none", "grip": "wood", "gripAngle": 20.0, "mag": "none", "bolt": "none", "hammer": true, "finish": "steel", "furniture": "wood", "accentStyle": "none"}""",
        ["derringer"] = """{"receiver": "break", "receiverLength": 0.42, "receiverHeight": 0.07, "receiverWidth": 0.045, "barrels": 2, "barrelLayout": "over", "barrelLength": 0.62, "barrelWidth": 0.026, "handguard": "none", "handguardLength": 0.0, "stock": "none", "grip": "birdshead", "gripAngle": 28.0, "mag": "none", "bolt": "none", "hammer": true, "sights": "bead", "finish": "chrome", "furniture": "wood", "accentStyle": "none"}""",
        ["smg"] = """{"receiver": "box", "receiverLength": 0.55, "receiverHeight": 0.09, "receiverWidth": 0.062, "barrelLength": 0.55, "barrelWidth": 0.026, "handguard": "slim", "handguardLength": 0.22, "handguardHeight": 0.07, "stock": "folding", "mag": "stick", "accentStyle": "mag"}""",
        ["pdw"] = """{"receiver": "bullpup", "receiverLength": 0.95, "receiverHeight": 0.11, "receiverWidth": 0.07, "barrelLength": 0.25, "barrelWidth": 0.024, "handguard": "none", "handguardLength": 0.0, "stock": "bullpup", "mag": "topbox", "bolt": "charging", "sights": "post", "foregrip": "strap"}""",
        ["rifle"] = "{}",
        ["battle_rifle"] = """{"receiver": "slab", "receiverLength": 0.5, "receiverHeight": 0.11, "receiverWidth": 0.066, "barrelLength": 0.78, "handguard": "slim", "handguardLength": 0.4, "handguardHeight": 0.08, "mag": "box", "muzzle": "flash", "accentStyle": "mag"}""",
        ["bullpup"] = """{"receiver": "bullpup", "receiverLength": 0.8, "receiverHeight": 0.12, "receiverWidth": 0.07, "barrelLength": 0.42, "handguard": "slim", "handguardLength": 0.18, "handguardHeight": 0.08, "stock": "bullpup", "mag": "curved"}""",
        ["dmr"] = """{"receiver": "box", "receiverLength": 0.42, "receiverHeight": 0.095, "receiverWidth": 0.064, "barrelLength": 0.84, "handguard": "slim", "handguardLength": 0.42, "handguardHeight": 0.075, "stock": "fixed", "cheekRest": true, "mag": "box", "finish": "black", "furniture": "olive"}""",
        ["lever_rifle"] = """{"receiver": "lever", "receiverLength": 0.28, "receiverHeight": 0.085, "receiverWidth": 0.05, "barrelLength": 0.8, "barrelStyle": "square", "handguard": "forend", "handguardLength": 0.3, "handguardHeight": 0.045, "stock": "straight", "stockLength": 0.26, "stockDrop": 6.0, "grip": "straight", "mag": "tube", "lever": true, "hammer": true, "bolt": "none", "sights": "post", "finish": "steel", "furniture": "wood", "accentStyle": "none"}""",
        ["bolt_sniper"] = """{"receiver": "round", "receiverLength": 0.34, "receiverHeight": 0.085, "receiverWidth": 0.06, "barrelLength": 0.62, "barrelWidth": 0.024, "handguard": "stock", "handguardLength": 0.3, "handguardHeight": 0.055, "stock": "wood", "stockLength": 0.26, "cheekRest": true, "mag": "internal", "bolt": "side", "bipod": "folded", "sights": "none", "finish": "gunmetal", "furniture": "wood", "accentStyle": "none"}""",
        ["pump_shotgun"] = """{"receiver": "box", "receiverLength": 0.36, "receiverHeight": 0.11, "receiverWidth": 0.07, "barrelLength": 0.55, "barrelWidth": 0.04, "handguard": "pump", "handguardLength": 0.3, "handguardHeight": 0.05, "stock": "wood", "stockLength": 0.26, "mag": "tube", "bolt": "none", "sights": "bead", "furniture": "wood", "accentStyle": "none"}""",
        ["semi_shotgun"] = """{"receiver": "box", "receiverLength": 0.38, "receiverHeight": 0.105, "receiverWidth": 0.068, "barrelLength": 0.55, "barrelWidth": 0.038, "handguard": "forend", "handguardLength": 0.34, "handguardHeight": 0.05, "stock": "crane", "stockLength": 0.24, "mag": "tube", "bolt": "charging", "sights": "ghost", "topRail": true, "accentStyle": "none"}""",
        ["double_barrel"] = """{"receiver": "break", "receiverLength": 0.2, "receiverHeight": 0.09, "receiverWidth": 0.07, "barrels": 2, "barrelLayout": "side", "barrelLength": 0.64, "barrelWidth": 0.034, "handguard": "forend", "handguardLength": 0.28, "handguardHeight": 0.04, "stock": "straight", "stockLength": 0.28, "stockDrop": 7.0, "grip": "straight", "mag": "none", "bolt": "none", "sights": "bead", "hammer": true, "finish": "steel", "furniture": "wood", "accentStyle": "none"}""",
        ["drum_shotgun"] = """{"receiver": "box", "receiverLength": 0.42, "receiverHeight": 0.11, "receiverWidth": 0.07, "barrelLength": 0.5, "barrelWidth": 0.034, "shroud": "perforated", "shroudLength": 0.75, "handguard": "none", "handguardLength": 0.0, "stock": "folding", "stockLength": 0.24, "mag": "striker", "foregrip": "vertical", "bolt": "charging", "sights": "post", "accentStyle": "mag"}""",
        ["lever_shotgun"] = """{"receiver": "lever", "receiverLength": 0.3, "receiverHeight": 0.1, "receiverWidth": 0.06, "barrelLength": 0.6, "barrelWidth": 0.04, "handguard": "forend", "handguardLength": 0.24, "handguardHeight": 0.045, "stock": "straight", "stockLength": 0.26, "stockDrop": 8.0, "grip": "straight", "mag": "tube", "lever": true, "hammer": true, "bolt": "none", "sights": "bead", "finish": "gunmetal", "furniture": "wood", "accentStyle": "none"}""",
        ["lmg"] = """{"receiver": "box", "receiverLength": 0.46, "receiverHeight": 0.13, "receiverWidth": 0.09, "barrelLength": 0.66, "barrelWidth": 0.04, "shroud": "heatshield", "shroudLength": 0.4, "handguard": "quad", "handguardLength": 0.36, "handguardHeight": 0.1, "stock": "skeleton", "mag": "beltbox", "carryHandle": true, "bipod": "folded", "finish": "olive", "furniture": "polymer", "accentStyle": "mag"}""",
        ["rotary"] = """{"receiver": "box", "receiverLength": 0.42, "receiverHeight": 0.15, "receiverWidth": 0.1, "barrels": 6, "barrelLayout": "rotary", "barrelLength": 0.62, "barrelWidth": 0.017, "handguard": "none", "handguardLength": 0.0, "stock": "spade", "mag": "beltbox", "carryHandle": true, "bolt": "none", "sights": "post", "finish": "gunmetal", "accentStyle": "mag"}""",
        ["musket"] = """{"receiver": "lever", "receiverLength": 0.16, "receiverHeight": 0.07, "receiverWidth": 0.05, "barrelLength": 0.95, "barrelWidth": 0.024, "handguard": "stock", "handguardLength": 0.85, "handguardHeight": 0.045, "stock": "straight", "stockLength": 0.3, "stockDrop": 9.0, "grip": "straight", "mag": "none", "bolt": "none", "hammer": true, "ramrod": true, "sights": "bead", "muzzle": "crown", "finish": "gunmetal", "furniture": "wood", "accentStyle": "bands"}""",
        ["coil_rifle"] = """{"receiver": "sci", "receiverLength": 0.4, "receiverHeight": 0.11, "receiverWidth": 0.07, "barrelLength": 0.64, "barrelWidth": 0.03, "shroud": "rails", "shroudLength": 0.95, "handguard": "none", "handguardLength": 0.0, "stock": "chassis", "stockLength": 0.25, "mag": "cell", "coils": 4, "sights": "none", "finish": "white", "furniture": "gunmetal", "accentStyle": "glow"}""",
        ["energy"] = """{"receiver": "sci", "receiverLength": 0.5, "receiverHeight": 0.1, "receiverWidth": 0.07, "barrelLength": 0.6, "barrelWidth": 0.03, "shroud": "coils", "shroudLength": 0.8, "handguard": "none", "handguardLength": 0.0, "stock": "fixed", "mag": "cell", "coils": 4, "accentStyle": "glow", "finish": "gunmetal", "furniture": "polymer"}""",
        ["tool"] = """{"receiver": "tool", "receiverLength": 0.55, "receiverHeight": 0.14, "receiverWidth": 0.075, "barrelLength": 0.38, "barrelWidth": 0.03, "handguard": "none", "handguardLength": 0.0, "stock": "none", "mag": "strip", "magAngle": -20.0, "bolt": "none", "sights": "none", "muzzle": "tip", "canister": true, "finish": "accent", "furniture": "black", "magColor": "steel", "accentStyle": "none"}""",
        ["rocket_tube"] = """{"receiver": "round", "receiverLength": 0.42, "receiverHeight": 0.1, "receiverWidth": 0.1, "barrelLength": 0.62, "barrelWidth": 0.1, "shroud": "none", "shroudLength": 0.35, "handguard": "none", "handguardLength": 0.0, "stock": "none", "gripAngle": 12.0, "mag": "none", "foregrip": "vertical", "bolt": "none", "sights": "tall", "muzzle": "none", "finish": "olive", "furniture": "black", "accentStyle": "bands"}""",
        ["grenade_launcher"] = """{"receiver": "break", "receiverLength": 0.3, "receiverHeight": 0.09, "receiverWidth": 0.07, "barrelLength": 0.5, "barrelWidth": 0.05, "handguard": "forend", "handguardLength": 0.3, "handguardHeight": 0.05, "stock": "folding", "stockLength": 0.24, "mag": "none", "bolt": "none", "sights": "aperture", "finish": "olive", "furniture": "polymer", "accentStyle": "stripe"}""",
        ["toy"] = """{"receiver": "toy", "receiverLength": 0.6, "receiverHeight": 0.12, "receiverWidth": 0.075, "barrelLength": 0.45, "barrelWidth": 0.035, "barrelStyle": "square", "handguard": "slim", "handguardLength": 0.25, "handguardHeight": 0.08, "stock": "toy", "mag": "pan", "bolt": "top", "sights": "tall", "muzzle": "tip", "finish": "accent", "furniture": "white", "magColor": "white", "accentStyle": "panel"}""",
    };

    private static GunSpec? _base;
    private static readonly Dictionary<string, GunSpec> _archetypes = new();

    private static GunSpec Archetype(string name)
    {
        lock (_archetypes)
        {
            if (_archetypes.TryGetValue(name, out var a))
                return a;
            if (_base == null)
            {
                _base = new GunSpec();
                using var b = JsonDocument.Parse(BaseJson);
                foreach (var p in b.RootElement.EnumerateObject())
                    _base.SetValue(p.Name, p.Value, "base", validate: false);
            }
            a = _base.Copy();
            a.Base = name;
            using var doc = JsonDocument.Parse(ArchetypeJson[name]);
            foreach (var p in doc.RootElement.EnumerateObject())
                a.SetValue(p.Name, p.Value, name, validate: false);
            _archetypes[name] = a;
            return a;
        }
    }

    private GunSpec Copy() => (GunSpec)MemberwiseClone();

    /// <summary>Reads a weapon file's "model" block (after the rest of the file, since sizes scale with the weapon's length and bulk).</summary>
    public static GunSpec Read(JsonElement e, WeaponDef w, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{where}: expected {{ \"base\": \"{string.Join("/", ArchetypeJson.Keys.Take(3))}...\", ... }}");
        var name = "rifle";
        if (e.TryGetProperty("base"u8, out var b))
        {
            name = WeaponFile.Text(b, $"{where}.base");
            if (!ArchetypeJson.ContainsKey(name))
                throw new WeaponFileException($"{where}.base: \"{name}\" isn't one of {string.Join(", ", ArchetypeJson.Keys)}");
        }
        var s = Archetype(name).Copy();
        s.ReceiverLength *= w.Length;
        s.BarrelLength *= w.Length;
        s.HandguardLength *= w.Length;
        s.ReceiverHeight *= w.Bulk;
        s.ReceiverWidth *= w.Bulk;
        s.BarrelWidth *= w.Bulk;
        s.HandguardHeight *= w.Bulk;
        var given = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in e.EnumerateObject())
        {
            if (!seen.Add(p.Name))
                throw new WeaponFileException($"{where}.{p.Name}: given twice");
            given.Add((p.Name, p.Value.GetRawText()));
            if (p.Name != "base")
                s.SetValue(p.Name, p.Value, $"{where}.{p.Name}", validate: true);
        }
        s.Given = given.ToArray();
        s.Cap = w.Mag;
        s.Caliber = w.Caliber;
        s.Accent = w.Accent;
        return s;
    }

    private void SetValue(string key, JsonElement v, string where, bool validate)
    {
        var p = Params.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                ?? throw new WeaponFileException($"{where}: unknown model key \"{key}\". Known: {string.Join(", ", Keys)}");
        switch (p.Kind)
        {
            case 'e':
            {
                var t = WeaponFile.Text(v, where);
                var o = p.Options!.FirstOrDefault(x => x.Equals(t, StringComparison.OrdinalIgnoreCase))
                        ?? throw new WeaponFileException($"{where}: \"{t}\" isn't one of {string.Join(", ", p.Options!)}");
                p.Set(this, o);
                break;
            }
            case 'n':
            {
                var f = WeaponFile.Number(v, where);
                if (validate && !(f >= p.Min && f <= p.Max))
                    throw new WeaponFileException($"{where}: {WeaponFile.Num(f)} must be between {WeaponFile.Num(p.Min)} and {WeaponFile.Num(p.Max)}");
                p.Set(this, f);
                break;
            }
            case 'i':
            {
                var i = WeaponFile.Int(v, where);
                if (validate && !(i >= p.Min && i <= p.Max))
                    throw new WeaponFileException($"{where}: {i} must be between {p.Min} and {p.Max}");
                p.Set(this, i);
                break;
            }
            case 'b':
                p.Set(this, v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : throw new WeaponFileException($"{where}: expected true or false, got {v}"));
                break;
            default:
                p.Set(this, ReadColor(v, where));
                break;
        }
    }

    private static GunColor ReadColor(JsonElement v, string where)
    {
        if (v.ValueKind == JsonValueKind.String)
        {
            var t = v.GetString()!.ToLowerInvariant();
            if (t == "accent" || GunColor.Named.ContainsKey(t))
                return new GunColor(t, default);
            if (t.StartsWith('#'))
                return new GunColor(null, (Vector3)WeaponFile.FromJson(v, typeof(Vector3), where));
            throw new WeaponFileException($"{where}: \"{t}\" isn't a colour: use accent, {string.Join(", ", GunColor.Named.Keys)}, [r, g, b] or \"#rrggbb\"");
        }
        var c = (Vector3)WeaponFile.FromJson(v, typeof(Vector3), where);
        if (c.X is < 0 or > 1 || c.Y is < 0 or > 1 || c.Z is < 0 or > 1)
            throw new WeaponFileException($"{where}: colour values must be between 0 and 1");
        return new GunColor(null, c);
    }

    /// <summary>The block as JSON, the way the file gave it (for <see cref="WeaponFile.ToJson"/>).</summary>
    public string ToJson()
    {
        var sb = new StringBuilder("{ ");
        for (var i = 0; i < Given.Length; i++)
            sb.Append(i > 0 ? ", " : "").Append(WeaponFile.Str(Given[i].Key)).Append(": ").Append(Given[i].Json);
        return sb.Append(" }").ToString();
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Base} ({Receiver}, {Mag} mag, {Stock} stock)");
}
