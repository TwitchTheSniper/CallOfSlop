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
            // However much is stacked, a kitted gun keeps at least half its hip spread and 40% of its recoil.
            def.HipSpreadDeg = MathF.Max(def.HipSpreadDeg, baseDef.HipSpreadDeg * 0.5f);
            Recoil.ClampKit(baseDef, def);
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
