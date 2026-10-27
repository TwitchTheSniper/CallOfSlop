using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// What kind of weapon part a part file describes (Zombies weapon builder; see docs/zombies/WEAPON-BUILDER.md). The set
/// is fixed in code (stats, rules and the workbench treat each one differently); each one's default mount and display
/// name come from Parts/_Registry.json (<see cref="PartRegistry"/>).
/// </summary>
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

    /// <summary>Receiver upgrades: mount on a receiver's upgrade sockets (behind it or on its side) and raise its fire rate or damage.</summary>
    Upgrade,

    /// <summary>Damage multipliers: one per receiver, in its "amp" socket (an overpressure chamber, a hot-load kit...).</summary>
    Multiplier,

    /// <summary>Melee only: the handle a melee weapon is built on (the root of a melee build).</summary>
    Hilt,

    /// <summary>Melee only: what hits (a blade, a hammer head, a fish).</summary>
    Head,

    /// <summary>Melee only: modifiers on a hilt (nails, a wrap, a counterweight).</summary>
    MeleeMod,

    /// <summary>
    /// The fire-control group on a receiver's "action" socket: it decides the fire type (single shot, semi, burst, auto,
    /// pump, lever, bolt, rotary, chain, muzzle-loaded) and the fire rate. The receiver only picks the calibre. Last in
    /// the list so the older categories keep their order (network ids are positions in the sorted part list).
    /// </summary>
    Action,
}

/// <summary>How rare (and how strong) a part is. Later rounds drop rarer parts.</summary>
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

/// <summary>An action part's fire type: how pulling the trigger turns into shots (see <see cref="ActionInfo"/>).</summary>
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

    /// <summary>One shot per click, a lever stroke between shots (quicker than a pump).</summary>
    Lever,

    /// <summary>Single shot (a break action): one round in the chamber, then reload.</summary>
    Single,

    /// <summary>Muzzle-loaded: one round, rammed down the barrel; a very slow reload for every shot and a big hit.</summary>
    MuzzleLoad,
}

/// <summary>
/// Action-only data (the "action" block of an action part): the fire type and the fire rate the receiver it sits on
/// fires at. Receivers are calibre only (docs/zombies/WEAPON-BUILDER.md, "Actions: the fire type is a part").
/// </summary>
public sealed class ActionInfo
{
    public ActionKind Kind = ActionKind.Semi;

    public int Rpm = 600;
    public int BurstCount = 3;
    public float BurstDelay = 0.25f;

    /// <summary>Seconds of held trigger to reach full fire rate (rotary/chain), starting from <see cref="SpoolStart"/> of it.</summary>
    public float SpoolTime;

    public float SpoolStart = 1f;

    /// <summary>Rotary actions: fire rate per barrel of the receiver (rpm = this x barrels). 0 = use <see cref="Rpm"/>.</summary>
    public int RpmPerBarrel;

    /// <summary>Rounds the action holds before a reload whatever the feed holds (single shot, muzzle-loaded: 1). 0 = the feed's magazine.</summary>
    public int Chamber;

    /// <summary>The fire type in words ("semi-auto", "3-round burst", "rotary"...).</summary>
    public string FireType => Name(Kind, BurstCount);

    /// <summary>A fire type in words.</summary>
    public static string Name(ActionKind k, int burst = 3) => k switch
    {
        ActionKind.Semi => "semi-auto",
        ActionKind.Auto => "full auto",
        ActionKind.Burst => $"{burst}-round burst",
        ActionKind.Pump => "pump",
        ActionKind.Bolt => "bolt",
        ActionKind.Rotary => "rotary",
        ActionKind.Chain => "chain",
        ActionKind.Lever => "lever",
        ActionKind.Single => "single shot",
        _ => "muzzle-loaded",
    };
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

    /// <summary>
    /// Other named points for what plugs in here, chosen by the part's <see cref="PartDef.Placement"/> (purely visual):
    /// a receiver's action socket has "under" (the trigger group spot under the receiver, in front of the grip) and
    /// "front" (under the barrel at the receiver's front face) besides its own <see cref="Pos"/> on the right side.
    /// </summary>
    public Dictionary<string, Vector3> Points = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where <paramref name="part"/> sits in this socket: its placement's point, or <see cref="Pos"/> ("side", none, or a point this socket doesn't have).</summary>
    public Vector3 PosFor(PartDef? part) =>
        part?.Placement is { } p && Points.TryGetValue(p, out var at) ? at : Pos;

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

    /// <summary>
    /// The box's turn when it came from a model fragment (the fragment box's own turn, then the "use" entry's), which
    /// Euler angles can't always add up to; null = <see cref="RotDeg"/>. See <see cref="PartLibrary"/>.
    /// </summary>
    public Quaternion? Orient;

    /// <summary>The box's rotation: <see cref="Orient"/> if set, else <see cref="RotDeg"/> (applied Z, X, Y).</summary>
    public Quaternion Rotation => Orient ?? PartDef.Rot(RotDeg);

    public GunColor Color = new("gunmetal", default);
    public float Alpha = 1f;

    /// <summary>
    /// "irons" = a grip's iron sights: drawn only when a receiver is fitted (they sit on it) and no optic; "glow" = drawn
    /// unlit-ish (mode 2); "with:SOCKET" = drawn only while this part's socket SOCKET holds a part (a hopper chute, a stock
    /// mount: a piece that only holds something up).
    /// </summary>
    public string Tag = "";
}

/// <summary>
/// Receiver-only data: the calibre and the base numbers every other part modifies. The fire type and fire rate come
/// from the action part in the receiver's "action" socket (<see cref="ActionInfo"/>), not from the receiver.
/// </summary>
public sealed class ReceiverInfo
{
    /// <summary>The weapon class the build behaves as (recoil character, reload animation, arm poses).</summary>
    public WeaponClass Class = WeaponClass.AssaultRifle;

    /// <summary>The calibre class every bore-bound part must match (pistol, rifle, heavy, shell, launcher, energy).</summary>
    public string Bore = "rifle";

    /// <summary>A shipped weapon whose synthesised gunshot, reload and equip sounds the build borrows.</summary>
    public string SoundOf = "BR-1 Prototype";

    /// <summary>Barrel limits for this receiver (counted after splitters).</summary>
    public int MinBarrels = 1, MaxBarrels = 12;

    /// <summary>This receiver's own barrel size [width, height, length] in place of its bore class's (null = the class's; Parts/_Registry.json).</summary>
    public System.Numerics.Vector3? BarrelScale;

    /// <summary>
    /// The action part this receiver fired like before actions were parts ("stockAction" in the file): an old build with
    /// no actions gets it in this receiver's action socket when it loads (<see cref="ActionMigration"/>). Null = none.
    /// </summary>
    public string? StockAction;
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
    /// <summary>
    /// How the grip fires several receivers. Fixed by the grip (the user's call, docs/zombies/WEAPON-BUILDER.md): picking
    /// the grip picks the style; a build has no choice of its own.
    /// </summary>
    public FireStyle Style = FireStyle.Volley;

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

/// <summary>
/// A shared feed (hopper / manifold) on a grip's "hopper" socket: one pooled magazine that feeds up to <see cref="Feeds"/>
/// receivers that have no feed of their own (in receiver-socket order, matching bores and actions only). Its capacity
/// and reload are its "mag" and "reload" stats; ammo in its own "ammo" socket is shared by every receiver it feeds.
/// </summary>
public sealed class SharedFeedInfo
{
    /// <summary>How many receivers it can feed at most.</summary>
    public int Feeds = 3;
}

/// <summary>
/// A melee hilt (the root of a melee build): the base numbers of the melee weapon. Heads and modifiers change them with
/// their <see cref="MeleeEffects"/>.
/// </summary>
public sealed class HiltInfo
{
    public float Damage = 30;

    /// <summary>Seconds between swings.</summary>
    public float Swing = 0.7f;

    /// <summary>Reach from the eyes, metres.</summary>
    public float Reach = 2.0f;

    /// <summary>How many targets one swing can hit.</summary>
    public int Cleave = 1;

    /// <summary>Seconds a hit target is stunned.</summary>
    public float Stun;

    /// <summary>"one" or "two" handed (two-handed hilts swing slower but hit harder, set in the numbers).</summary>
    public string Hands = "one";
}

/// <summary>What a melee head or modifier does to the hilt's numbers.</summary>
public sealed class MeleeEffects
{
    /// <summary>Damage added, then multiplied.</summary>
    public float Damage, DamageMult = 1f;

    /// <summary>Swing time multiplier (below 1 = faster).</summary>
    public float SwingMult = 1f;

    /// <summary>Reach added (m), extra targets per swing, stun seconds added.</summary>
    public float Reach;

    public int Cleave;
    public float Stun;
}

/// <summary>
/// Ammo-only data: how the round is cut up. Ammo is universal (every ammo part fits every receiver; the receiver sets
/// the calibre), so an ammo part only changes the calibre's numbers relatively (its "stats": mul / add) and, with this
/// block, how many projectiles the round is split into.
/// </summary>
public sealed class RoundInfo
{
    /// <summary>
    /// Projectiles the round is split into, sharing the calibre's damage (8 = buckshot: a rifle round becomes eight
    /// pellets of an eighth each; 1 = a slug: a shell's eight pellets become one of eight times the damage). 0 = as the
    /// calibre fires it.
    /// </summary>
    public int Pellets;
}

/// <summary>Splitter-only data: the documented damage rule for each output.</summary>
public sealed class SplitInfo
{
    /// <summary>Outputs fan out by this many degrees each side of centre (0 = parallel barrels).</summary>
    public float SplayDeg;

    /// <summary>Spread multiplier on every output (rarer splitters are more accurate: below 1 = tighter).</summary>
    public float SpreadMult = 1f;

    /// <summary>
    /// Damage on every output: always 1 / outputs, so a splitter never changes the total damage of a shot (the user's
    /// rule: splitters trade focus for spread and coverage; rarity buys accuracy and more outputs, not damage).
    /// </summary>
    public static float OutputMult(PartDef splitter) => 1f / Math.Max(1, splitter.Sockets.Count(s => s.Type == "barrel"));
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

    /// <summary>
    /// Calibre classes it works with (empty = any). Feeds and ammo must match the receiver's bore; barrels, splitters and
    /// muzzle devices fit every receiver (on a barrel an old "bores" list only says what it was drawn for, see <see cref="SizedFor"/>).
    /// </summary>
    public string[] Bores = [];

    /// <summary>
    /// Barrels: the bore class its model is drawn for ("rifle" unless the file says "sizedFor"). Under a receiver of
    /// another bore the model is scaled by the ratio of the two classes' barrel scales (see <see cref="BuildRules.BarrelScale"/>).
    /// </summary>
    public string SizedFor = "rifle";

    /// <summary>Feeds can require particular fire types of their receiver's action part (a belt needs something automatic to pull it).</summary>
    public ActionKind[] RequiresAction = [];

    /// <summary>
    /// Which of its socket's points the part is drawn at (<see cref="PartSocket.Points"/>; null or "side" = the socket's
    /// own position). Purely visual: actions say "under" (trigger groups and sears), "front" (pump slide, ramrod) or
    /// nothing (bolts, levers, cranks and drives on the side). The part's model is drawn in that point's space.
    /// </summary>
    public string? Placement;

    public float Weight = 0.3f;

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

    /// <summary>Action parts only: the fire type and rate (see <see cref="ActionInfo"/>).</summary>
    public ActionInfo? Action;

    public SplitInfo? Split;

    /// <summary>Ammo only: how the round is split into projectiles (null = as the calibre fires it).</summary>
    public RoundInfo? Round;

    /// <summary>Root grips only (a grip with no mount): receiver capacity, fire style, handling.</summary>
    public FrameInfo? Frame;

    /// <summary>Shared feeds only (a feed on a grip's "hopper" socket).</summary>
    public SharedFeedInfo? Shared;

    /// <summary>Melee hilts only: the root of a melee build.</summary>
    public HiltInfo? Hilt;

    /// <summary>Melee heads and modifiers: what they do to the hilt's numbers.</summary>
    public MeleeEffects? Melee;

    /// <summary>Weight relief: kg added to the grip's carry weight while this part is on the build (slings, harnesses, anti-grav).</summary>
    public float CarryBonus;

    /// <summary>True for the parts a build starts from: gun grips (the root of a weapon) and melee hilts (the root of a melee weapon).</summary>
    public bool IsRoot => Frame != null || Hilt != null;

    /// <summary>A melee-only part (hilt, head or modifier).</summary>
    public bool IsMeleePart => Category is PartCategory.Hilt or PartCategory.Head or PartCategory.MeleeMod;

    /// <summary>How many receivers a root grip holds (its "receiver" sockets).</summary>
    public int ReceiverCapacity => Sockets.Count(s => s.Type == "receiver");

    /// <summary>The file it came from, relative to the Parts folder.</summary>
    public string? SourceFile;

    public bool FitsBore(string bore) => Bores.Length == 0 || Bores.Contains(bore, StringComparer.OrdinalIgnoreCase);

    public override string ToString() => $"{Name} ({Category}, {Rarity})";

    public static Quaternion Rot(Vector3 deg)
    {
        const float k = MathF.PI / 180f;
        return Quaternion.CreateFromAxisAngle(Vector3.UnitZ, deg.Z * k) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, deg.X * k)
               * Quaternion.CreateFromAxisAngle(Vector3.UnitY, deg.Y * k);
    }

    /// <summary>Display name of a category ("Receiver", "Barrel"...): the registry's "display" (Parts/_Registry.json).</summary>
    public static string CategoryName(PartCategory c) => Parts.Registry.DisplayOf(c);

    /// <summary>The built-in display names (used when there is no registry file).</summary>
    public static string BuiltInCategoryName(PartCategory c) => c switch
    {
        PartCategory.Multiplier => "Damage Multiplier",
        PartCategory.Upgrade => "Receiver Upgrade",
        PartCategory.MeleeMod => "Melee Mod",
        _ => c.ToString(),
    };

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
