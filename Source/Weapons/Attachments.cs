namespace CallOfSlop;

public enum AttachSlot : byte
{
    Optic,
    Muzzle,
    Barrel,
    Underbarrel,
    Magazine,
    Stock,
}

/// <summary>
/// What you look through when aiming. Iron = the gun's own sights (or the built-in scope of a long gun without an optic).
/// New kinds go at the end: optic files name them, and the order here is never relied on elsewhere.
/// </summary>
public enum OpticKind : byte
{
    Iron,
    RedDot,
    Holo,
    Acog,
    Scope4,
    Scope8,

    /// <summary>Tiny open reflex sight (fastest to aim).</summary>
    MiniDot,

    /// <summary>Compact 2x prism with a horseshoe reticle.</summary>
    Prism2,

    /// <summary>Reflex sight with a 3x magnifier behind it.</summary>
    Hybrid3,

    /// <summary>Low-power variable optic set to 6x.</summary>
    Lpvo6,

    /// <summary>Long-range 12x sniper scope with a mil-dot reticle.</summary>
    Scope12,

    /// <summary>Slim long-eye-relief scout scope mounted forward on the rail (about 2x).</summary>
    Scout,

    /// <summary>Fat night-vision tube with a glowing objective and a green picture (about 2x).</summary>
    NightTube,

    /// <summary>Over-the-wall sight: a tall riser with a mirror head, so the gun sits low under the sight line (1x reflex).</summary>
    Periscope,

    /// <summary>A shotgun's bead and rear ghost ring on a short rail: no glass at all (1x).</summary>
    Bead,
}

/// <summary>One attachment: where it goes, what it fits, and how it changes the gun. Loaded from the Attachments folder.</summary>
public sealed class AttachmentDef
{
    public required AttachSlot Slot;
    public byte Index; // 1..31 inside its slot (0 = empty slot); assigned from "order" when the files load
    public required string Name;
    public string Blurb = "";
    public string[] Pros = [];
    public string[] Cons = [];
    public Func<WeaponDef, bool> Fits = _ => true;
    public Action<WeaponDef> Apply = _ => { };

    /// <summary>Sort order inside its slot (decides <see cref="Index"/>); files without one get 1000.</summary>
    public int Order = 1000;

    /// <summary>Where it comes in each weapon's unlock sequence (lower unlocks earlier); files without one get 1000.</summary>
    public int UnlockOrder = 1000;

    public string Author = "";

    /// <summary>The "fits" rules and stat changes from its file (null/empty for code-built attachments).</summary>
    public AttachmentFits? Rule;

    public AttachmentEffect[] Effects = [];

    /// <summary>An optic's "sight" block: how it looks when aiming (null = the default for its optic kind).</summary>
    public SightSpec? Sight;

    /// <summary>
    /// How it's drawn on the gun: one of <see cref="GunBuilder.Looks"/> (the file's "model"), e.g. "Drum Mag" or "bipod".
    /// Null = by its own name, which is how the shipped attachments from before "model" existed are drawn.
    /// </summary>
    public string? Look;

    /// <summary>Metres added to melee reach while the gun carrying it is in hand (the file's "meleeReach"; a bayonet). 0 for almost everything.</summary>
    public float MeleeReach;

    /// <summary>The model the gun builder draws for it: <see cref="Look"/>, or its name.</summary>
    public string Model => Look ?? Name;

    /// <summary>The file it was loaded from, relative to the Attachments folder.</summary>
    public string? SourceFile;
}

/// <summary>
/// Call of Duty style gunsmith. Every weapon has 6 slots; each slot holds one attachment or nothing.
/// A weapon's attachments are a "kit": 5 bits per slot packed into a uint (sent as 4 bytes; protocol 46, 4 bits and 3 bytes before).
/// Attachments unlock per weapon with weapon XP (kills with that gun, plus a match bonus; see <see cref="UnlockXp"/>).
/// The server applies the same kit to the same base stats, so attachments are fully authoritative.
/// </summary>
public static class Attachments
{
    public const int SlotCount = 6;

    /// <summary>Bits per slot in a kit (<see cref="KitBits"/> in all): 0 means empty, so a slot holds at most <see cref="MaxPerSlot"/> attachments.</summary>
    public const int SlotBits = 5;

    /// <summary>Bits a kit takes on the wire (<see cref="SlotCount"/> x <see cref="SlotBits"/> = 30: one U32).</summary>
    public const int KitBits = SlotCount * SlotBits;

    /// <summary>Kits pack 5 bits per slot and 0 means empty, so a slot holds at most 31 attachments (15 until protocol 46).</summary>
    public const int MaxPerSlot = (1 << SlotBits) - 1;

    /// <summary>A kit with every slot set to its highest index (the self-test's junk kit).</summary>
    public const uint FullKit = (1u << KitBits) - 1;

    private static readonly object LoadLock = new();
    private static AttachmentDef[][]? _bySlot;
    private static AttachmentDef[]? _unlockSequence;

    /// <summary>All attachments per slot, in slot-index order (index 1 first). Loaded from the Attachments folder on first use.</summary>
    public static AttachmentDef[][] BySlot
    {
        get
        {
            var b = _bySlot;
            if (b != null)
                return b;
            lock (LoadLock)
                return _bySlot ??= LoadAll();
        }
    }

    /// <summary>Every attachment in unlock order ("unlockOrder", then name).</summary>
    private static AttachmentDef[] UnlockSequence =>
        _unlockSequence ??= BySlot.SelectMany(s => s).OrderBy(a => a.UnlockOrder).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Problems found while loading attachment files (bad files are skipped). Shown in the lobby and the log.</summary>
    public static readonly List<string> LoadErrors = new();

    /// <summary>The folder attachment files are loaded from (next to the game).</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Attachments");

    /// <summary>
    /// Every *.json in the Attachments folder and its subfolders. Indices inside a slot follow "order" then name.
    /// No files means no attachments (the game still works).
    /// </summary>
    private static AttachmentDef[][] LoadAll()
    {
        var loaded = new List<AttachmentDef>();
        if (Directory.Exists(Folder))
        {
            foreach (var file in Directory.GetFiles(Folder, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var rel = Path.GetRelativePath(Folder, file);
                try
                {
                    var a = AttachmentFile.Load(File.ReadAllText(file), rel);
                    if (loaded.FirstOrDefault(x => x.Name.Equals(a.Name, StringComparison.OrdinalIgnoreCase)) is { } dup)
                        throw new WeaponFileException($"{rel}: another attachment is already called '{a.Name}' ({dup.SourceFile})");
                    loaded.Add(a);
                }
                catch (Exception ex) when (ex is WeaponFileException or IOException or UnauthorizedAccessException)
                {
                    LoadErrors.Add(ex.Message);
                    Console.WriteLine($"[attachments] skipped: {ex.Message}");
                }
            }
        }
        var bySlot = new AttachmentDef[SlotCount][];
        for (var s = 0; s < SlotCount; s++)
        {
            var inSlot = loaded.Where(a => (int)a.Slot == s).OrderBy(a => a.Order).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var extra in inSlot.Skip(MaxPerSlot))
            {
                LoadErrors.Add($"{extra.SourceFile}: the {SlotName((AttachSlot)s)} slot is full (at most {MaxPerSlot} attachments per slot); skipped");
                Console.WriteLine($"[attachments] skipped {extra.SourceFile}: slot full");
            }
            bySlot[s] = inSlot.Take(MaxPerSlot).ToArray();
            for (var i = 0; i < bySlot[s].Length; i++)
                bySlot[s][i].Index = (byte)(i + 1);
        }
        // Run every attachment on every weapon it fits once now, so the stat code is compiled at startup
        // instead of in the middle of a match the first time someone spawns with a new kit.
        foreach (var a in bySlot.SelectMany(x => x))
            foreach (var w in Weapons.All)
                if (a.Fits(w))
                    a.Apply(w.Clone());
        var count = bySlot.Sum(x => x.Length);
        Console.WriteLine(count == 0 ? $"[attachments] no attachment files in {Folder}; guns have no attachments" : $"[attachments] {count} attachment(s) from {Folder}");
        return bySlot;
    }

    public static AttachmentDef? Get(AttachSlot slot, int index) =>
        index <= 0 || index > BySlot[(int)slot].Length ? null : BySlot[(int)slot][index - 1];

    /// <summary>The model ("look") drawn for the attachment in a kit's slot (see <see cref="AttachmentDef.Model"/>), or null when the slot is empty.</summary>
    public static string? LookIn(uint kit, AttachSlot slot) => Get(slot, Slot(kit, slot))?.Model;

    /// <summary>Extra melee reach from a kitted gun's attachments (a bayonet), in metres; 0 for a stock gun.</summary>
    public static float MeleeReachBonus(WeaponDef d)
    {
        if (d.Kit == 0)
            return 0f;
        var baseDef = Weapons.Get(d.Id);
        var sum = 0f;
        foreach (var a in InKit(d.Kit))
            if (a.MeleeReach != 0 && a.Fits(baseDef))
                sum += a.MeleeReach;
        return sum;
    }

    public static AttachmentDef? Find(string name) => BySlot.SelectMany(s => s).FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Attachments that fit this weapon, in unlock order.</summary>
    public static IReadOnlyList<AttachmentDef> For(WeaponDef w)
    {
        if (_forCache.TryGetValue(w.Id, out var list))
            return list;
        var baseDef = Weapons.Get(w.Id);
        list = UnlockSequence.Where(a => a.Fits(baseDef)).ToArray();
        _forCache[w.Id] = list;
        return list;
    }

    private static readonly Dictionary<byte, AttachmentDef[]> _forCache = new();

    /// <summary>
    /// Weapon XP to unlock everything on this gun. A typical match earns ~1,000 weapon XP for the gun you use, so the
    /// fewest attachments (17, a derringer) take ~3.5 matches and the most (about 45, an assault rifle) ~7.4 matches.
    /// (1000 + 180 per attachment until the 2026-10-23 attachments pass; the older attachments' XP is capped anyway.)
    /// </summary>
    public static int MaxXp(WeaponDef w) => For(w).Count == 0 ? 0 : (1100 + 140 * For(w).Count) / 50 * 50; // multiple of 50

    /// <summary>
    /// Weapon XP for the k-th attachment (in unlock order) on a gentle curve: the first few come fast,
    /// the last ones take a while. 60% of a gun's attachments needs about half its max XP.
    /// An attachment that shipped before the bigger attachment set (<see cref="LegacyNames"/>) never needs more XP than it
    /// used to, so nothing a player already unlocked gets locked again.
    /// </summary>
    public static int UnlockXp(WeaponDef w, AttachmentDef a)
    {
        if (_unlockCache.TryGetValue((w.Id, a), out var cached))
            return cached;
        var xp = int.MaxValue;
        var list = For(w);
        for (var i = 0; i < list.Count; i++)
            if (list[i] == a)
            {
                var f = MathF.Pow((i + 1f) / list.Count, 1.3f);
                xp = Math.Min(MaxXp(w), (int)(MathF.Round(MaxXp(w) * f / 50f) * 50f)); // round to 50 XP; the last one = max
                xp = Math.Min(xp, LegacyUnlockXp(w, a));
                if (a == DefaultOptic(w))
                    xp = 0; // the scope a sniper starts with comes with the gun
                break;
            }
        _unlockCache[(w.Id, a)] = xp;
        return xp;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(byte, AttachmentDef), int> _unlockCache = new();

    /// <summary>
    /// The 27 attachments that shipped before the class-specific ones (2026-10). Their unlock XP is capped at what it was
    /// then (<see cref="LegacyUnlockXp"/>), since adding attachments to a gun moves everything along its unlock curve.
    /// </summary>
    public static readonly string[] LegacyNames =
    [
        "Red Dot", "Holographic", "2.5x ACOG", "4x Scope", "8x Sniper Scope", "Pinhole Reflex", "Brick 2x Prism", "Tandem 3x Hybrid", "Variant 6x LPVO",
        "Longshot 12x", "Suppressor", "Compensator", "Muzzle Brake", "Choke", "Long Barrel", "Short Barrel", "Heavy Barrel", "Vertical Grip", "Angled Grip",
        "Laser", "Extended Mag", "Fast Mag", "Drum Mag", "Hollow Point Mag", "Light Stock", "Heavy Stock", "No Stock",
    ];

    /// <summary>
    /// What a shipped-before attachment cost under the old rules: the old curve over only the legacy attachments that fit
    /// the gun, with the old total (2000 + 220 per attachment). int.MaxValue for anything newer.
    /// </summary>
    public static int LegacyUnlockXp(WeaponDef w, AttachmentDef a)
    {
        if (!LegacyNames.Contains(a.Name, StringComparer.OrdinalIgnoreCase))
            return int.MaxValue;
        var old = For(w).Where(x => LegacyNames.Contains(x.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        var i = old.IndexOf(a);
        if (i < 0)
            return int.MaxValue;
        var max = (2000 + 220 * old.Count) / 50 * 50;
        var f = MathF.Pow((i + 1f) / old.Count, 1.3f);
        return Math.Min(max, (int)(MathF.Round(max * f / 50f) * 50f));
    }

    // ---- Default optic and iron sights ---------------------------------------------------
    //
    // Snipers (and any weapon whose file names a "defaultOptic") start with a scope: it is in their default kit, it needs
    // no weapon XP, and bots always run it. Their iron sights are the empty optic slot, which unlocks part-way through the
    // gun's attachment XP (IronSightsShare). Iron sights are not an attachment of their own (they were designed when the
    // optic slot was full at 15):
    // on such a gun an empty optic slot simply means "iron sights" and stays locked until then (see Sanitize).

    /// <summary>Iron sights on a gun that starts scoped unlock at this share of its max weapon XP (half-way).</summary>
    public const float IronSightsShare = 0.5f;

    /// <summary>
    /// The name iron sights are saved as in loadout.txt kits (on a gun that starts scoped, where an empty optic slot is a
    /// choice; older files without it get the default scope, which is what their "no optic" looked through then).
    /// </summary>
    public const string IronSightsName = "Iron Sights";

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<byte, AttachmentDef?> _defaultOptic = new();

    /// <summary>
    /// The optic a weapon starts with (null: iron sights, the empty slot). The file's "defaultOptic" if it names an optic
    /// that fits; otherwise, for a sniper, its first full scope (4x and up) in unlock order. Cached per weapon id.
    /// </summary>
    public static AttachmentDef? DefaultOptic(WeaponDef w) => _defaultOptic.GetOrAdd(w.Id, id => ResolveDefaultOptic(Weapons.Get(id)));

    /// <summary><see cref="DefaultOptic"/> worked out for any weapon definition (uncached; --check-weapon uses it on a file).</summary>
    public static AttachmentDef? ResolveDefaultOptic(WeaponDef baseDef)
    {
        var optics = BySlot[(int)AttachSlot.Optic].Where(a => a.Fits(baseDef)).ToArray();
        if (baseDef.DefaultOptic is { } name && optics.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } named)
            return named;
        if (baseDef.Class != WeaponClass.Sniper || optics.Length == 0)
            return null;
        float Zoom(AttachmentDef a)
        {
            var d = baseDef.Clone();
            a.Apply(d);
            return d.AdsZoom;
        }
        return optics.Where(a => Zoom(a) >= GunModel.FullScopeZoom).OrderBy(a => a.UnlockOrder).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
               ?? optics.OrderByDescending(Zoom).First();
    }

    /// <summary>Does the weapon start with a scope (so its iron sights are an unlock)?</summary>
    public static bool StartsScoped(WeaponDef w) => DefaultOptic(w) != null;

    /// <summary>The kit a weapon starts with: its default optic, or nothing.</summary>
    public static uint DefaultKit(WeaponDef w) => DefaultOptic(w) is { } o ? With(0, AttachSlot.Optic, o.Index) : 0;

    /// <summary>Weapon XP that unlocks iron sights (the empty optic slot) on a gun that starts scoped; 0 on any other gun.</summary>
    public static int IronSightsXp(WeaponDef w) =>
        StartsScoped(w) ? Math.Clamp((int)(MathF.Round(MaxXp(w) * IronSightsShare / 50f) * 50f), 50, Math.Max(50, MaxXp(w))) : 0;

    /// <summary>Can the gun be run without an optic at this much weapon XP?</summary>
    public static bool IronSightsUnlocked(WeaponDef w, int weaponXp) => weaponXp >= IronSightsXp(w);

    /// <summary>What an empty optic slot is called on this gun: its iron sights, or the scope built into it.</summary>
    public static string NoOpticName(WeaponDef w) => GunModel.HasBuiltInScope(Weapons.Get(w.Id)) ? "Built-in scope" : "Iron sights";

    // ---- Kits ---------------------------------------------------------------------------

    public static int Slot(uint kit, AttachSlot slot) => (int)((kit >> ((int)slot * SlotBits)) & MaxPerSlot);

    public static uint With(uint kit, AttachSlot slot, int index) =>
        (kit & ~((uint)MaxPerSlot << ((int)slot * SlotBits))) | ((uint)(index & MaxPerSlot) << ((int)slot * SlotBits));

    public static IEnumerable<AttachmentDef> InKit(uint kit)
    {
        for (var s = 0; s < SlotCount; s++)
            if (Get((AttachSlot)s, Slot(kit, (AttachSlot)s)) is { } a)
                yield return a;
    }

    /// <summary>
    /// Drops anything that doesn't fit the weapon (and, if progress is given, anything still locked: on a gun that starts
    /// scoped, an empty optic slot is its iron sights, so while those are locked the default scope goes back on).
    /// </summary>
    public static uint Sanitize(WeaponDef w, uint kit, Progress? progress = null)
    {
        var baseDef = Weapons.Get(w.Id);
        uint clean = 0;
        for (var s = 0; s < SlotCount; s++)
        {
            var a = Get((AttachSlot)s, Slot(kit, (AttachSlot)s));
            if (a == null || !a.Fits(baseDef))
                continue;
            if (progress != null && progress.WeaponXp(w.Id) < UnlockXp(baseDef, a))
                continue;
            clean = With(clean, (AttachSlot)s, a.Index);
        }
        if (progress != null && Slot(clean, AttachSlot.Optic) == 0 && DefaultOptic(baseDef) is { } scope && !IronSightsUnlocked(baseDef, progress.WeaponXp(w.Id)))
            clean = With(clean, AttachSlot.Optic, scope.Index);
        return clean;
    }

    private static readonly Dictionary<(byte, uint), WeaponDef> _applied = new();

    /// <summary>
    /// The weapon's stats with a kit applied (cached, so the same kit always returns the same object).
    /// Kit 0 is the plain weapon from <see cref="Weapons.All"/>.
    /// </summary>
    public static WeaponDef Apply(WeaponDef baseDef, uint kit)
    {
        if (kit == 0)
            return baseDef;
        lock (_applied)
        {
            if (_applied.TryGetValue((baseDef.Id, kit), out var def))
                return def;
            def = baseDef.Clone();
            def.Kit = kit;
            foreach (var a in InKit(kit))
                if (a.Fits(baseDef))
                    a.Apply(def);
            def.Mobility = Math.Clamp(def.Mobility, 0.6f, 1.25f);
            def.AdsTime = Math.Clamp(def.AdsTime, 0.08f, 0.5f);
            def.Loudness = Math.Clamp(def.Loudness, WeaponDef.MinLoudness, WeaponDef.MaxLoudness);
            // However much is stacked, a kitted gun keeps at least half its hip spread and 40% of its recoil.
            def.HipSpreadDeg = MathF.Max(def.HipSpreadDeg, baseDef.HipSpreadDeg * 0.5f);
            Recoil.ClampKit(baseDef, def);
            LauncherStats.ClampKit(baseDef, def); // launchers keep sane lock times, speeds and reserves (LauncherStats.cs)
            _applied[(baseDef.Id, kit)] = def;
            return def;
        }
    }

    public static string Describe(uint kit)
    {
        var names = InKit(kit).Select(a => a.Name).ToArray();
        return names.Length == 0 ? "no attachments" : string.Join(", ", names);
    }

    public static string SlotName(AttachSlot s) => s == AttachSlot.Underbarrel ? "Underbarrel" : s.ToString();
}
