using System.Numerics;

namespace CallOfSlop;

/// <summary>What kind of weapon part a part file describes (Zombies weapon builder; see docs/zombies/WEAPON-BUILDER.md).</summary>
public enum PartCategory : byte
{
    Receiver,
    Barrel,
    Splitter,
    Feed,
    Ammo,
    Stock,
    Grip,
    Optic,
    Muzzle,
    Utility,
}

/// <summary>How rare (and how strong) a part is. Later rounds drop rarer parts; scrap values follow the tier.</summary>
public enum Rarity : byte
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary,

    /// <summary>Ridiculous one-offs (mystery box and special drops only): fun first, balance second.</summary>
    Wonder,
}

/// <summary>A receiver's action: how pulling the trigger turns into shots.</summary>
public enum ActionKind : byte
{
    Semi,
    Auto,
    Burst,

    /// <summary>One shot per click, slow cycle (a pump stroke).</summary>
    Pump,

    /// <summary>One shot per click, very slow cycle (work the bolt).</summary>
    Bolt,

    /// <summary>Minigun: the barrels take turns (one barrel per shot) and the fire rate spools up while the trigger is held.</summary>
    Rotary,

    /// <summary>Chain gun: externally driven automatic with a short spool-up.</summary>
    Chain,
}

/// <summary>A socket a part offers: where another part plugs in, in the part's own space (metres, -Z forward, +Y up).</summary>
public sealed class PartSocket
{
    /// <summary>Unique within the part ("barrel", "barrel_2", "feed", ...). Builds and saves address children by it.</summary>
    public required string Name;

    /// <summary>The socket type ("receiver", "barrel", "feed", "ammo", "stock", "foregrip", "optic", "muzzle", "utility"): a part plugs in where its <see cref="PartDef.Mount"/> matches.</summary>
    public required string Type;

    /// <summary>Which part categories it takes (empty = any category whose mount is this type).</summary>
    public PartCategory[] Accepts = [];

    public Vector3 Pos;

    /// <summary>Rotation of what plugs in, in degrees about X, Y, Z (applied Z, X, Y like gun boxes).</summary>
    public Vector3 RotDeg;

    /// <summary>A weapon isn't complete (can't be fired) until this socket is filled.</summary>
    public bool Required;

    public Quaternion Rotation => PartDef.Rot(RotDeg);
}

/// <summary>One box of a part's model, in the part's space. Shapes: box, rod (octagonal, along Z) and a thin "plate".</summary>
public sealed class PartBox
{
    public string Shape = "box";
    public Vector3 Size = new(0.02f);
    public Vector3 Pos;
    public Vector3 RotDeg;
    public GunColor Color = new("gunmetal", default);
    public float Alpha = 1f;

    /// <summary>"irons" = iron sights, removed when an optic is fitted; "glow" = drawn unlit-ish (mode 2).</summary>
    public string Tag = "";
}

/// <summary>Receiver-only data: the action and the base numbers every other part modifies.</summary>
public sealed class ReceiverInfo
{
    public ActionKind Action = ActionKind.Auto;

    /// <summary>The weapon class the build behaves as (recoil character, reload animation, arm poses).</summary>
    public WeaponClass Class = WeaponClass.AssaultRifle;

    /// <summary>The calibre class every bore-bound part must match (pistol, rifle, heavy, shell, launcher, energy).</summary>
    public string Bore = "rifle";

    /// <summary>A shipped weapon whose synthesised gunshot, reload and equip sounds the build borrows.</summary>
    public string SoundOf = "BR-1 Prototype";

    public int Rpm = 600;
    public int BurstCount = 3;
    public float BurstDelay = 0.25f;

    /// <summary>Seconds of held trigger to reach full fire rate (rotary/chain), starting from <see cref="SpoolStart"/> of it.</summary>
    public float SpoolTime;

    public float SpoolStart = 1f;

    /// <summary>Barrel limits for this receiver (counted after splitters).</summary>
    public int MinBarrels = 1, MaxBarrels = 8;

    /// <summary>Rotary receivers: fire rate per fitted barrel (rpm = this x barrels). 0 = use <see cref="Rpm"/>.</summary>
    public int RpmPerBarrel;

}

/// <summary>How a grip with several receivers fires them (proposed, open for the user; see the design doc).</summary>
public enum FireStyle : byte
{
    /// <summary>One trigger pull fires every receiver at once (linked: the slowest receiver sets the rate).</summary>
    Volley,

    /// <summary>Receivers take turns, one per shot (their fire rates add up).</summary>
    Rotate,
}

/// <summary>
/// Root-grip data: the grip is the part the player holds and the root of the weapon tree. Its "receiver" sockets are its
/// receiver capacity (a pistol grip holds 3, a rifle grip 9, a push cart lots).
/// </summary>
public sealed class FrameInfo
{
    /// <summary>Fire styles the grip allows; the first is the default (the build picks one).</summary>
    public FireStyle[] FireStyles = [FireStyle.Volley];

    /// <summary>How it's held: "one" (pistol), "two" (rifle), "chainsaw" (top handle + trigger handle), "cart" (both hands on a push bar), "wheel", "case".</summary>
    public string Hands = "two";

    /// <summary>Weight the grip handles before the build gets sluggish (kg; the weight cap is twice this).</summary>
    public float Carry = 6f;

    /// <summary>Movement speed multiplier of the frame itself (a cart is slow however light it is).</summary>
    public float Mobility = 1f;

    /// <summary>False: no aiming down sights (hip fire only: carts, briefcases).</summary>
    public bool Ads = true;

    /// <summary>Height of the iron sight line above the grip's origin (used when no optic is fitted).</summary>
    public float IronHeight = 0.07f;

    /// <summary>Where the off hand goes when no foregrip is fitted (grip space); null = the lead receiver's barrel.</summary>
    public Vector3? Support;

    /// <summary>Reload time added per receiver after the first (all feeds are swapped together).</summary>
    public float ReloadPerReceiver = 0.25f;
}

/// <summary>Splitter-only data: the documented damage rule for each output.</summary>
public sealed class SplitInfo
{
    /// <summary>Damage multiplier on every output barrel. 1/N conserves the shot's damage; more than 1/N multiplies it (silly tier).</summary>
    public float OutputMult = 0.5f;

    /// <summary>Outputs fan out by this many degrees each side of centre (0 = parallel barrels).</summary>
    public float SplayDeg;
}

/// <summary>
/// One weapon part, loaded from a file in the Parts folder (format "call-of-slop-part/1"; see
/// docs/zombies/WEAPON-BUILDER.md). Parts connect through sockets into a tree with a grip at the root (holding one or
/// more receivers, each with its own barrels, feed and ammo); the
/// tree's stats become a <see cref="WeaponDef"/> (<see cref="BuildStats"/>) and its model a gun body (<see cref="BuildModel"/>).
/// </summary>
public sealed class PartDef
{
    public required string Name;
    public PartCategory Category;
    public Rarity Rarity;
    public string Flavor = "";
    public string Author = "";

    /// <summary>The socket type this part plugs into ("" for root grips: the weapon is built on them).</summary>
    public string Mount = "";

    /// <summary>Calibre classes it works with (empty = any). Barrels, splitters, feeds, ammo and muzzles must match the receiver's bore.</summary>
    public string[] Bores = [];

    /// <summary>Feeds and splitters can require particular actions of their receiver (a belt needs something automatic to pull it).</summary>
    public ActionKind[] RequiresAction = [];

    public float Weight = 0.3f;

    /// <summary>Scrap you get for breaking it down (0 in the file = the tier default, see <see cref="Scrap"/>).</summary>
    public int ScrapValue;

    public PartSocket[] Sockets = [];
    public PartBox[] Boxes = [];
    public AttachmentEffect[] Effects = [];

    /// <summary>Special-effect flags (incendiary, explosive, penetrating...): carried into the build, gameplay comes later.</summary>
    public string[] Flags = [];

    /// <summary>Where a hand holds this part (grips, foregrips, magazines), in part space.</summary>
    public Vector3? Hand;

    /// <summary>Grips: rake of the grip in degrees (positive = raked back), which the hand follows.</summary>
    public float GripAngleDeg;

    /// <summary>Barrels and muzzle devices: where shots leave, in part space. A barrel with several is a bundle (each counts as a barrel).</summary>
    public Vector3[] Muzzles = [];

    /// <summary>Optics: the existing optic look drawn on the rail (model, reticle and lens come from it).</summary>
    public OpticKind Optic = OpticKind.Iron;

    /// <summary>Optics: an optional "sight" block (see Attachments/README.md); null = the look's default.</summary>
    public SightSpec? Sight;

    /// <summary>Feeds: the reload style hint ("box", "drum", "belt", "tube", "cell", "single", "hopper").</summary>
    public string FeedStyle = "";

    public ReceiverInfo? Receiver;
    public SplitInfo? Split;

    /// <summary>Root grips only (a grip with no mount): receiver capacity, fire styles, handling.</summary>
    public FrameInfo? Frame;

    /// <summary>True for the grips a weapon is built on (the root of the tree).</summary>
    public bool IsRoot => Frame != null;

    /// <summary>How many receivers a root grip holds (its "receiver" sockets).</summary>
    public int ReceiverCapacity => Sockets.Count(s => s.Type == "receiver");

    /// <summary>The file it came from, relative to the Parts folder.</summary>
    public string? SourceFile;

    public int Scrap => ScrapValue > 0 ? ScrapValue : CallOfSlop.Scrap.TierValue(Rarity);

    public bool FitsBore(string bore) => Bores.Length == 0 || Bores.Contains(bore, StringComparer.OrdinalIgnoreCase);

    public override string ToString() => $"{Name} ({Category}, {Rarity})";

    public static Quaternion Rot(Vector3 deg)
    {
        const float k = MathF.PI / 180f;
        return Quaternion.CreateFromAxisAngle(Vector3.UnitZ, deg.Z * k) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, deg.X * k)
               * Quaternion.CreateFromAxisAngle(Vector3.UnitY, deg.Y * k);
    }

    /// <summary>Display name of a category ("Receiver", "Barrel"...).</summary>
    public static string CategoryName(PartCategory c) => c.ToString();

    /// <summary>Display name of a tier.</summary>
    public static string RarityName(Rarity r) => r.ToString();

    /// <summary>Tier colour for the UI (white, green, blue, purple, orange, red).</summary>
    public static Vector3 RarityColor(Rarity r) => r switch
    {
        Rarity.Common => new(0.85f, 0.85f, 0.85f),
        Rarity.Uncommon => new(0.35f, 0.9f, 0.4f),
        Rarity.Rare => new(0.35f, 0.65f, 1f),
        Rarity.Epic => new(0.75f, 0.45f, 1f),
        Rarity.Legendary => new(1f, 0.6f, 0.15f),
        _ => new(1f, 0.3f, 0.3f),
    };
}

/// <summary>
/// Scrap economy hooks (proposed numbers, open for the user; see docs/zombies/WEAPON-BUILDER.md): breaking a part down
/// gives its scrap value, crafting a part costs three times that, and upgrading a part to the next tier costs the
/// difference in craft cost minus half the old part's value.
/// </summary>
public static class Scrap
{
    /// <summary>Default scrap value per tier.</summary>
    public static int TierValue(Rarity r) => r switch
    {
        Rarity.Common => 5,
        Rarity.Uncommon => 12,
        Rarity.Rare => 25,
        Rarity.Epic => 50,
        Rarity.Legendary => 100,
        _ => 150,
    };

    /// <summary>Scrap gained by breaking the part down.</summary>
    public static int SalvageValue(PartDef p) => p.Scrap;

    /// <summary>Scrap needed to craft the part at a workbench (wonder parts can't be crafted: -1).</summary>
    public static int CraftCost(PartDef p) => p.Rarity == Rarity.Wonder ? -1 : p.Scrap * 3;

    /// <summary>Scrap to turn <paramref name="from"/> into <paramref name="to"/> (same category, higher tier), or -1 if not allowed.</summary>
    public static int UpgradeCost(PartDef from, PartDef to)
    {
        if (from.Category != to.Category || to.Rarity <= from.Rarity || to.Rarity == Rarity.Wonder)
            return -1;
        return Math.Max(1, CraftCost(to) - CraftCost(from) - from.Scrap / 2);
    }
}
