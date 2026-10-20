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

/// <summary>What you look through when aiming. Iron = the gun's own sights (or built-in scope on snipers).</summary>
public enum OpticKind : byte
{
    Iron,
    RedDot,
    Holo,
    Acog,
    Scope4,
    Scope8,
}

/// <summary>One attachment: where it goes, what it fits, and how it changes the gun. Loaded from the Attachments folder.</summary>
public sealed class AttachmentDef
{
    public required AttachSlot Slot;
    public byte Index; // 1..15 inside its slot (0 = empty slot); assigned from "order" when the files load
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

    /// <summary>The file it was loaded from, relative to the Attachments folder.</summary>
    public string? SourceFile;
}

/// <summary>
/// Call of Duty style gunsmith. Every weapon has 6 slots; each slot holds one attachment or nothing.
/// A weapon's attachments are a "kit": 4 bits per slot packed into a uint (sent as 3 bytes).
/// Attachments unlock per weapon with weapon XP (kills with that gun, plus a match bonus; see <see cref="UnlockXp"/>).
/// The server applies the same kit to the same base stats, so attachments are fully authoritative.
/// </summary>
public static class Attachments
{
    public const int SlotCount = 6;

    /// <summary>Kits pack 4 bits per slot and 0 means empty, so a slot holds at most 15 attachments.</summary>
    public const int MaxPerSlot = 15;

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
        var count = bySlot.Sum(x => x.Length);
        Console.WriteLine(count == 0 ? $"[attachments] no attachment files in {Folder}; guns have no attachments" : $"[attachments] {count} attachment(s) from {Folder}");
        return bySlot;
    }

    // ---- Who fits what ------------------------------------------------------------------
    private static bool IsLong(WeaponDef w) => w.Class is WeaponClass.AssaultRifle or WeaponClass.LMG or WeaponClass.DMR or WeaponClass.Sniper;
    private static bool HasStock(WeaponDef w) => w.Class is not (WeaponClass.Pistol or WeaponClass.Special);
    private static bool Normal(WeaponDef w) => w.Sound is SoundStyle.Gun or SoundStyle.Boom or SoundStyle.Suppressed;
    private static bool UsesMag(WeaponDef w) => w.Mag >= 3;
    private static bool Precision(WeaponDef w) => w.Class is WeaponClass.DMR or WeaponClass.Sniper;

    private static int Grow(int mag, float mult) => Math.Max(mag + 1, (int)MathF.Round(mag * mult));

    /// <summary>
    /// TEMPORARY: the attachments as they were in code before they became files (with Index and UnlockOrder set
    /// the old way). Only the self-test uses it, to prove the files behave identically. Deleted once confirmed.
    /// </summary>
    internal static AttachmentDef[] BuiltInCatalogue()
    {
        AttachmentDef[] all =
        [
            // ---- Optics ---------------------------------------------------------------
            new()
            {
                Slot = AttachSlot.Optic, Name = "Red Dot", Blurb = "Small reflex sight. A clean dot instead of iron sights.",
                Pros = ["Clear sight picture", "Slightly tighter aimed spread"], Cons = ["Low 1.5x zoom"],
                Fits = w => true,
                Apply = w => { w.Optic = OpticKind.RedDot; w.AdsZoom = 1.5f; w.AdsSpreadMult *= 0.9f; },
            },
            new()
            {
                Slot = AttachSlot.Optic, Name = "Holographic", Blurb = "Wide window with a ring reticle. Great for tracking moving targets.",
                Pros = ["Ring + dot reticle", "1.6x zoom", "Tighter aimed spread"], Cons = ["Slightly slower to aim"],
                Fits = w => w.Class != WeaponClass.Pistol,
                Apply = w => { w.Optic = OpticKind.Holo; w.AdsZoom = 1.6f; w.AdsSpreadMult *= 0.85f; w.AdsTime += 0.02f; },
            },
            new()
            {
                Slot = AttachSlot.Optic, Name = "2.5x ACOG", Blurb = "Mid-range prism scope with a chevron reticle.",
                Pros = ["2.5x zoom", "Much tighter aimed spread"], Cons = ["Slower to aim", "Narrow view"],
                Fits = w => w.Class is not (WeaponClass.Pistol or WeaponClass.Shotgun or WeaponClass.Special),
                Apply = w => { w.Optic = OpticKind.Acog; w.AdsZoom = 2.5f; w.AdsSpreadMult *= 0.75f; w.AdsTime += 0.05f; },
            },
            new()
            {
                Slot = AttachSlot.Optic, Name = "4x Scope", Blurb = "Full scope for long sightlines.",
                Pros = ["4x zoom", "Very tight aimed spread"], Cons = ["Much slower to aim", "Tunnel vision", "-3% speed"],
                Fits = w => IsLong(w),
                Apply = w => { w.Optic = OpticKind.Scope4; w.AdsZoom = 4f; w.AdsSpreadMult *= 0.6f; w.AdsTime += 0.1f; w.Mobility -= 0.03f; },
            },
            new()
            {
                Slot = AttachSlot.Optic, Name = "8x Sniper Scope", Blurb = "High-power glass for map-length shots.",
                Pros = ["8x zoom", "Pinpoint aimed spread"], Cons = ["Slowest to aim", "-5% speed"],
                Fits = w => Precision(w),
                Apply = w => { w.Optic = OpticKind.Scope8; w.AdsZoom = 8f; w.AdsSpreadMult *= 0.5f; w.AdsTime += 0.15f; w.Mobility -= 0.05f; },
            },

            // ---- Muzzle ---------------------------------------------------------------
            new()
            {
                Slot = AttachSlot.Muzzle, Name = "Suppressor", Blurb = "Quiet shots and a dim muzzle flash. Bullets lose a bit of punch at range.",
                Pros = ["Quiet, muffled gunshot", "Dim flash and tracers"], Cons = ["-15% range"],
                Fits = w => Normal(w) && w.Sound != SoundStyle.Suppressed && w.Class != WeaponClass.Special,
                Apply = w => { w.Suppressor = true; w.Sound = SoundStyle.Suppressed; w.RangeM *= 0.85f; },
            },
            new()
            {
                Slot = AttachSlot.Muzzle, Name = "Compensator", Blurb = "Vents gas upward to fight muzzle climb.",
                Pros = ["-20% recoil"],
                Fits = w => w.Class is not (WeaponClass.Shotgun or WeaponClass.Special),
                Apply = w => w.RecoilDeg *= 0.8f,
            },
            new()
            {
                Slot = AttachSlot.Muzzle, Name = "Muzzle Brake", Blurb = "Balances recoil and keeps hip fire steady.",
                Pros = ["-10% recoil", "-10% hip spread"],
                Fits = w => w.Class is not (WeaponClass.Shotgun or WeaponClass.Special or WeaponClass.Pistol),
                Apply = w => { w.RecoilDeg *= 0.9f; w.HipSpreadDeg *= 0.9f; },
            },
            new()
            {
                Slot = AttachSlot.Muzzle, Name = "Choke", Blurb = "Tightens the pellet pattern.",
                Pros = ["-30% pellet spread", "+15% range"],
                Fits = w => w.Class == WeaponClass.Shotgun,
                Apply = w => { w.HipSpreadDeg *= 0.7f; w.RangeM *= 1.15f; },
            },

            // ---- Barrel ---------------------------------------------------------------
            new()
            {
                Slot = AttachSlot.Barrel, Name = "Long Barrel", Blurb = "More velocity, more reach.",
                Pros = ["+30% range"], Cons = ["-4% speed", "Slower to aim"],
                Fits = w => w.Class is not (WeaponClass.Pistol or WeaponClass.Special),
                Apply = w => { w.RangeM *= 1.3f; w.Mobility -= 0.04f; w.AdsTime += 0.03f; w.Length += 0.08f; },
            },
            new()
            {
                Slot = AttachSlot.Barrel, Name = "Short Barrel", Blurb = "Chopped down for close quarters.",
                Pros = ["+4% speed", "-15% hip spread", "Faster to aim"], Cons = ["-20% range"],
                Fits = w => w.Class is not (WeaponClass.Pistol or WeaponClass.Special),
                Apply = w => { w.RangeM *= 0.8f; w.Mobility += 0.04f; w.HipSpreadDeg *= 0.85f; w.AdsTime = MathF.Max(0.08f, w.AdsTime - 0.02f); w.Length -= 0.06f; },
            },
            new()
            {
                Slot = AttachSlot.Barrel, Name = "Heavy Barrel", Blurb = "Thick, stable barrel.",
                Pros = ["+15% range", "-10% recoil"], Cons = ["-6% speed", "Slower draw"],
                Fits = w => IsLong(w) || w.Class == WeaponClass.SMG,
                Apply = w => { w.RangeM *= 1.15f; w.RecoilDeg *= 0.9f; w.Mobility -= 0.06f; w.EquipTime *= 1.15f; },
            },

            // ---- Underbarrel ----------------------------------------------------------
            new()
            {
                Slot = AttachSlot.Underbarrel, Name = "Vertical Grip", Blurb = "Something solid to hold on to.",
                Pros = ["-20% recoil"], Cons = ["Slightly slower to aim"],
                Fits = w => HasStock(w),
                Apply = w => { w.RecoilDeg *= 0.8f; w.AdsTime += 0.01f; },
            },
            new()
            {
                Slot = AttachSlot.Underbarrel, Name = "Angled Grip", Blurb = "Snappier shouldering.",
                Pros = ["25% faster to aim", "-5% recoil"],
                Fits = w => HasStock(w),
                Apply = w => { w.AdsTime *= 0.75f; w.RecoilDeg *= 0.95f; },
            },
            new()
            {
                Slot = AttachSlot.Underbarrel, Name = "Laser", Blurb = "Points where the hip-fire cone is centred.",
                Pros = ["-30% hip spread"],
                Fits = w => true,
                Apply = w => w.HipSpreadDeg *= 0.7f,
            },

            // ---- Magazine -------------------------------------------------------------
            new()
            {
                Slot = AttachSlot.Magazine, Name = "Extended Mag", Blurb = "More rounds before you reload.",
                Pros = ["+50% magazine"], Cons = ["12% slower reload"],
                Fits = w => UsesMag(w),
                Apply = w => { w.Mag = Grow(w.Mag, 1.5f); w.Reload *= 1.12f; },
            },
            new()
            {
                Slot = AttachSlot.Magazine, Name = "Fast Mag", Blurb = "Clipped together for quick swaps.",
                Pros = ["30% faster reload"],
                Fits = w => UsesMag(w),
                Apply = w => w.Reload *= 0.7f,
            },
            new()
            {
                Slot = AttachSlot.Magazine, Name = "Drum Mag", Blurb = "Huge capacity, huge weight.",
                Pros = ["Double magazine"], Cons = ["40% slower reload", "-5% speed"],
                Fits = w => w.Class is WeaponClass.AssaultRifle or WeaponClass.SMG or WeaponClass.LMG,
                Apply = w => { w.Mag = Grow(w.Mag, 2f); w.Reload *= 1.4f; w.Mobility -= 0.05f; },
            },

            // ---- Stock ----------------------------------------------------------------
            new()
            {
                Slot = AttachSlot.Stock, Name = "Light Stock", Blurb = "Skeletonised for speed.",
                Pros = ["+6% speed"], Cons = ["+10% recoil"],
                Fits = w => HasStock(w),
                Apply = w => { w.Mobility += 0.06f; w.RecoilDeg *= 1.1f; },
            },
            new()
            {
                Slot = AttachSlot.Stock, Name = "Heavy Stock", Blurb = "Padded and weighted.",
                Pros = ["-15% recoil"], Cons = ["-4% speed", "Slightly slower to aim"],
                Fits = w => HasStock(w),
                Apply = w => { w.RecoilDeg *= 0.85f; w.Mobility -= 0.04f; w.AdsTime += 0.02f; },
            },
            new()
            {
                Slot = AttachSlot.Stock, Name = "No Stock", Blurb = "Run and gun.",
                Pros = ["+10% speed", "15% faster to aim"], Cons = ["+25% recoil", "+10% hip spread"],
                Fits = w => HasStock(w),
                Apply = w => { w.Mobility += 0.1f; w.AdsTime *= 0.85f; w.RecoilDeg *= 1.25f; w.HipSpreadDeg *= 1.1f; },
            },
        ];

        for (var s = 0; s < SlotCount; s++)
        {
            var inSlot = all.Where(a => (int)a.Slot == s).ToArray();
            for (var i = 0; i < inSlot.Length; i++)
                inSlot[i].Index = (byte)(i + 1);
        }
        foreach (var a in all)
            a.UnlockOrder = Array.IndexOf(UnlockOrder, a.Name) + 1;
        return all;
    }

    /// <summary>TEMPORARY (see <see cref="BuiltInCatalogue"/>): the order attachments unlocked in.</summary>
    private static readonly string[] UnlockOrder =
    [
        "Red Dot", "Laser", "Compensator", "Choke", "Fast Mag", "Holographic", "Vertical Grip", "Extended Mag", "Short Barrel",
        "Light Stock", "Suppressor", "2.5x ACOG", "Angled Grip", "Long Barrel", "Muzzle Brake", "Heavy Stock", "4x Scope",
        "Drum Mag", "Heavy Barrel", "No Stock", "8x Sniper Scope",
    ];

    public static AttachmentDef? Get(AttachSlot slot, int index) =>
        index <= 0 || index > BySlot[(int)slot].Length ? null : BySlot[(int)slot][index - 1];

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
    /// Weapon XP to unlock everything on this gun. A typical match earns ~1,000 weapon XP for the gun
    /// you use, so 3 attachments = ~3 matches and 19 attachments = ~7 matches.
    /// </summary>
    public static int MaxXp(WeaponDef w) => For(w).Count == 0 ? 0 : (2000 + 260 * For(w).Count) / 50 * 50; // multiple of 50

    /// <summary>
    /// Weapon XP for the k-th attachment (in unlock order) on a gentle curve: the first few come fast,
    /// the last ones take a while. 60% of a gun's attachments needs about half its max XP.
    /// </summary>
    public static int UnlockXp(WeaponDef w, AttachmentDef a)
    {
        var list = For(w);
        for (var i = 0; i < list.Count; i++)
            if (list[i] == a)
            {
                var f = MathF.Pow((i + 1f) / list.Count, 1.3f);
                return Math.Min(MaxXp(w), (int)(MathF.Round(MaxXp(w) * f / 50f) * 50f)); // round to 50 XP; the last one = max
            }
        return int.MaxValue;
    }

    // ---- Kits ---------------------------------------------------------------------------

    public static int Slot(uint kit, AttachSlot slot) => (int)((kit >> ((int)slot * 4)) & 15);

    public static uint With(uint kit, AttachSlot slot, int index) =>
        (kit & ~(15u << ((int)slot * 4))) | ((uint)(index & 15) << ((int)slot * 4));

    public static IEnumerable<AttachmentDef> InKit(uint kit)
    {
        for (var s = 0; s < SlotCount; s++)
            if (Get((AttachSlot)s, Slot(kit, (AttachSlot)s)) is { } a)
                yield return a;
    }

    /// <summary>Drops anything that doesn't fit the weapon (and, if progress is given, anything still locked).</summary>
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
