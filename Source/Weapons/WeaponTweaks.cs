using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace CallOfSlop;

/// <summary>Which part of the weapon tweak window a stat sits in.</summary>
public enum TweakGroup : byte
{
    Damage,
    Handling,
    Penetration,
    Recoil,
    Launcher,
}

/// <summary>The per-class multipliers of the weapon tweak window (wire bytes: never reorder).</summary>
public enum TweakStat : byte
{
    Damage,
    FireRate,
    Recoil,
    Spread,
    Range,
    Reload,
    Mobility,
    COUNT,
}

/// <summary>
/// One stat the weapon tweak window can change: a number of <see cref="WeaponDef"/> that a weapon file holds, with its
/// file key (and block), the range the file allows and a narrower range for the slider. Values are floats in the
/// window and on the wire; whole numbers and the fire mode are rounded when they're set.
/// </summary>
public sealed class TweakField
{
    public required int Index;
    public required FieldInfo Field;
    public required string Key; // the key in the file ("damage", or "vertical" inside the "recoil" block)
    public required string? Block; // null (top level), "recoil" or "launcher"
    public required string Label;
    public required TweakGroup Group;
    public required float Min, Max; // what a weapon file allows (WeaponFile.Ranges and the block attributes)
    public required float SoftMin, SoftMax; // the slider; Ctrl+click types any value inside Min..Max
    public bool IsInt => Field.FieldType == typeof(int);
    public bool IsEnum => Field.FieldType.IsEnum;
    public bool LauncherOnly => Group == TweakGroup.Launcher;
    public required Func<WeaponDef, float> Get;
    public required Action<WeaponDef, float> Set;

    /// <summary>ImGui id for the field's slider and reset button (made once).</summary>
    public required string SliderId, ResetId;

    /// <summary>The value as a weapon file writes it ("24", "0.6", "\"Auto\"").</summary>
    public string FileText(float v) =>
        IsEnum ? WeaponFile.Str(Enum.GetName(Field.FieldType, (int)MathF.Round(v)) ?? "")
        : IsInt ? ((int)MathF.Round(v)).ToString(CultureInfo.InvariantCulture)
        : WeaponFile.Num(v);

    /// <summary>Rounds and clamps a value the way <see cref="Set"/> stores it.</summary>
    public float Sanitize(float v)
    {
        if (!float.IsFinite(v))
            v = Min;
        v = Math.Clamp(v, Min, Max);
        return IsInt || IsEnum ? MathF.Round(v) : v;
    }
}

/// <summary>
/// The weapon tweak window's state (Source/UI/UiWeaponTweaks.cs): per-weapon stat overrides and per-class multipliers,
/// applied live to the loaded weapons (<see cref="Weapons.All"/>, and every kitted copy in place, so whoever holds a
/// weapon fires the new numbers at once). The weapons as their files have them are kept (<see cref="FileDef"/>), so the
/// weapon fingerprint, attachment fits and unlocks never see a tweak; only <see cref="Save"/> writes values back to the
/// files (changing the fingerprint from then on). In multiplayer only the host changes them; the host's Game sends
/// the whole set to everyone (Msg.WeaponTweaks, GameWeaponTweaks.cs). Nothing here is saved anywhere else.
/// </summary>
public static class WeaponTweaks
{
    /// <summary>Look and sound numbers in a weapon file: not stats, and baked into gun meshes and sound banks at start-up.</summary>
    private static readonly string[] LookFields = [nameof(WeaponDef.Length), nameof(WeaponDef.Bulk), nameof(WeaponDef.Caliber), nameof(WeaponDef.Bright), nameof(WeaponDef.Tail)];

    /// <summary>Slider ranges narrower than the file's (the rest slide over the whole file range).</summary>
    private static readonly Dictionary<string, (float Min, float Max)> Soft = new()
    {
        [nameof(WeaponDef.Damage)] = (1, 200),
        [nameof(WeaponDef.HeadMult)] = (1, 4),
        [nameof(WeaponDef.Pellets)] = (1, 20),
        [nameof(WeaponDef.RangeM)] = (1, 150),
        [nameof(WeaponDef.Rpm)] = (20, 1500),
        [nameof(WeaponDef.BurstCount)] = (1, 8),
        [nameof(WeaponDef.BurstDelay)] = (0, 1),
        [nameof(WeaponDef.Mag)] = (1, 200),
        [nameof(WeaponDef.Reload)] = (0.2f, 8),
        [nameof(WeaponDef.HipSpreadDeg)] = (0.05f, 10),
        [nameof(WeaponDef.AdsSpreadMult)] = (0, 1),
        [nameof(WeaponDef.RecoilDeg)] = (0, 5),
        [nameof(WeaponDef.AdsZoom)] = (1, 6),
        [nameof(WeaponDef.EquipTime)] = (0.05f, 2),
        [nameof(WeaponDef.AdsTime)] = (0.05f, 1),
        [nameof(WeaponDef.Penetration)] = (-1, 100),
        [nameof(WeaponDef.PenetrationMult)] = (0, 3),
        [nameof(WeaponDef.LauncherDirect)] = (0, 300),
        [nameof(WeaponDef.LauncherThrust)] = (0, 300),
        [nameof(WeaponDef.LauncherLockRange)] = (5, 300),
    };

    /// <summary>Every stat the window shows, in file order: top-level stats (with the fire mode), the recoil block, the launcher block.</summary>
    public static readonly TweakField[] Fields = BuildFields();

    /// <summary>The field each class multiplier scales.</summary>
    public static readonly TweakField[] StatField =
    [
        F(nameof(WeaponDef.Damage)), F(nameof(WeaponDef.Rpm)), F(nameof(WeaponDef.RecoilDeg)), F(nameof(WeaponDef.HipSpreadDeg)),
        F(nameof(WeaponDef.RangeM)), F(nameof(WeaponDef.Reload)), F(nameof(WeaponDef.Mobility)),
    ];

    public static readonly string[] StatNames = ["Damage", "Fire rate", "Recoil", "Spread", "Range", "Reload time", "Mobility"];

    /// <summary>A class multiplier's slider range.</summary>
    public const float MinMult = 0.25f, MaxMult = 4f;

    /// <summary>At most this many per-weapon overrides at once (they all fit in one packet: 6 bytes each).</summary>
    public const int MaxOverrides = 1200;

    public static readonly int ClassCount = Enum.GetValues<WeaponClass>().Length;

    private static TweakField F(string name) => Fields.First(f => f.Field.Name == name);

    private static TweakField[] BuildFields()
    {
        var list = new List<TweakField>();
        void Add(FieldInfo f, string key, string? block, TweakGroup group, float min, float max)
        {
            var (smin, smax) = Soft.TryGetValue(f.Name, out var s) ? s : (min, max);
            var w = Expression.Parameter(typeof(WeaponDef), "w");
            var v = Expression.Parameter(typeof(float), "v");
            var field = Expression.Field(w, f);
            Expression get = f.FieldType.IsEnum ? Expression.Convert(Expression.Convert(field, typeof(int)), typeof(float)) : Expression.Convert(field, typeof(float));
            var round = Expression.Call(typeof(MathF).GetMethod(nameof(MathF.Round), [typeof(float)])!, v);
            Expression val = f.FieldType == typeof(float) ? v
                : f.FieldType == typeof(int) ? Expression.Convert(round, typeof(int))
                : Expression.Convert(Expression.Convert(round, typeof(int)), f.FieldType);
            var label = block == null ? key : $"{block}.{key}";
            list.Add(new TweakField
            {
                Index = list.Count, Field = f, Key = key, Block = block, Label = label, Group = group, Min = min, Max = max, SoftMin = smin, SoftMax = smax,
                Get = Expression.Lambda<Func<WeaponDef, float>>(get, w).Compile(),
                Set = Expression.Lambda<Action<WeaponDef, float>>(Expression.Assign(field, val), w, v).Compile(),
                SliderId = "##tw_" + label, ResetId = "R##twr_" + label,
            });
        }
        foreach (var f in WeaponFile.Fields)
        {
            if (f.Name == nameof(WeaponDef.Mode))
                Add(f, WeaponFile.Key(f), null, TweakGroup.Handling, 0, Enum.GetValues<FireMode>().Length - 1);
            if (!WeaponFile.Ranges.TryGetValue(f.Name, out var r) || LookFields.Contains(f.Name))
                continue;
            var group = f.Name is nameof(WeaponDef.Damage) or nameof(WeaponDef.HeadMult) or nameof(WeaponDef.Pellets) or nameof(WeaponDef.RangeM) or nameof(WeaponDef.MinDamageMult)
                ? TweakGroup.Damage
                : f.Name is nameof(WeaponDef.Penetration) or nameof(WeaponDef.PenetrationMult) ? TweakGroup.Penetration : TweakGroup.Handling;
            Add(f, WeaponFile.Key(f), null, group, r.Min, r.Max);
        }
        foreach (var f in Recoil.Fields)
        {
            var a = f.GetCustomAttribute<RecoilKeyAttribute>()!;
            Add(f, a.Key, "recoil", TweakGroup.Recoil, a.Min, a.Max);
        }
        foreach (var f in LauncherStats.Fields)
        {
            if (f.FieldType.IsEnum)
                continue; // what it fires and how it's steered: not a number to slide
            var a = f.GetCustomAttribute<LauncherKeyAttribute>()!;
            Add(f, a.Key, "launcher", TweakGroup.Launcher, a.Min, a.Max);
        }
        if (list.Count > 255)
            throw new InvalidOperationException("weapon tweaks: more than 255 fields (field indices are bytes on the wire)");
        return list.ToArray();
    }

    // ---- State ---------------------------------------------------------------------------------

    private static WeaponDef[]? _file; // the weapons as their files have them, by id (made on the first tweak; a Save moves it)
    private static WeaponDef[]? _loaded; // the weapons as the files were when the game loaded them (what the network payload is against)
    private static bool[] _has = [];
    private static float[] _val = [];
    private static int _overrides;
    private static readonly float[] _mult = Enumerable.Repeat(1f, ClassCount * (int)TweakStat.COUNT).ToArray();
    private static int _mults; // multipliers that aren't 1

    /// <summary>Goes up on every change (the host's Game sends the set when it moves).</summary>
    public static int Version { get; private set; }

    /// <summary>Any override or class multiplier in effect: the weapons differ from their files.</summary>
    public static bool Active => _overrides > 0 || _mults > 0;

    /// <summary>Per-weapon overrides in effect.</summary>
    public static int OverrideCount => _overrides;

    /// <summary>Class multipliers that aren't 1.</summary>
    public static int MultCount => _mults;

    /// <summary>Copies every instance field (public and private) of one weapon into another: a MemberwiseClone into an existing object.</summary>
    internal static readonly Action<WeaponDef, WeaponDef> CopyInto = BuildCopier();

    private static Action<WeaponDef, WeaponDef> BuildCopier()
    {
        var from = Expression.Parameter(typeof(WeaponDef), "from");
        var to = Expression.Parameter(typeof(WeaponDef), "to");
        var body = typeof(WeaponDef).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(f => !f.IsInitOnly)
            .Select(f => (Expression)Expression.Assign(Expression.Field(to, f), Expression.Field(from, f)))
            .ToArray();
        return Expression.Lambda<Action<WeaponDef, WeaponDef>>(Expression.Block(body), from, to).Compile();
    }

    private static void EnsureState()
    {
        if (_file != null && _file.Length == Weapons.All.Length)
            return;
        var all = Weapons.All;
        _file = all.Select(w => w.Clone()).ToArray();
        _loaded = all.Select(w => w.Clone()).ToArray();
        _has = new bool[all.Length * Fields.Length];
        _val = new float[all.Length * Fields.Length];
        _overrides = 0;
    }

    /// <summary>
    /// The weapon as its file has it: the untweaked copy of a loaded weapon (<paramref name="w"/> itself when nothing was
    /// ever tweaked, or for anything that isn't one of <see cref="Weapons.All"/>). The fingerprint and attachment fits use it.
    /// </summary>
    public static WeaponDef FileDef(WeaponDef w)
    {
        var file = _file;
        return file != null && w.Id < file.Length && w.Kit == 0 && ReferenceEquals(Weapons.All[w.Id], w) ? file[w.Id] : w;
    }

    public static float FileValue(int weapon, TweakField f) => f.Get(FileDef(Weapons.Get((byte)weapon)));

    public static float LiveValue(int weapon, TweakField f) => f.Get(Weapons.Get((byte)weapon));

    public static bool HasOverride(int weapon, TweakField f) => _file != null && weapon < _file.Length && _has[weapon * Fields.Length + f.Index];

    /// <summary>The value the window set for this stat (or the file's when there is no override).</summary>
    public static float OverrideOr(int weapon, TweakField f) => HasOverride(weapon, f) ? _val[weapon * Fields.Length + f.Index] : FileValue(weapon, f);

    public static float ClassMult(WeaponClass c, TweakStat s) => _mult[(int)c * (int)TweakStat.COUNT + (int)s];

    /// <summary>Does this weapon differ from its file (an override, or a multiplier on its class)?</summary>
    public static bool IsTweaked(int weapon)
    {
        if (_file == null || weapon >= _file.Length)
            return false;
        for (var i = 0; i < Fields.Length; i++)
            if (_has[weapon * Fields.Length + i])
                return true;
        var c = (int)_file[weapon].Class;
        for (var s = 0; s < (int)TweakStat.COUNT; s++)
            if (_mult[c * (int)TweakStat.COUNT + s] != 1f)
                return true;
        return false;
    }

    /// <summary>Sets one stat of one weapon (rounded and clamped to what a weapon file allows). False when there are already <see cref="MaxOverrides"/>.</summary>
    public static bool SetOverride(int weapon, TweakField f, float value)
    {
        EnsureState();
        if (weapon >= _file!.Length)
            return false;
        var at = weapon * Fields.Length + f.Index;
        value = f.Sanitize(value);
        if (!_has[at])
        {
            if (_overrides >= MaxOverrides)
                return false;
            _has[at] = true;
            _overrides++;
        }
        else if (_val[at] == value)
            return true;
        _val[at] = value;
        Changed(weapon);
        return true;
    }

    /// <summary>Drops one stat's override (back to the file's value, times the class multiplier).</summary>
    public static void ClearOverride(int weapon, TweakField f)
    {
        if (!HasOverride(weapon, f))
            return;
        _has[weapon * Fields.Length + f.Index] = false;
        _overrides--;
        Changed(weapon);
    }

    /// <summary>"Revert": one weapon back to its file (its class multipliers stay).</summary>
    public static void RevertWeapon(int weapon)
    {
        if (_file == null || weapon >= _file.Length)
            return;
        var any = false;
        for (var i = 0; i < Fields.Length; i++)
        {
            ref var h = ref _has[weapon * Fields.Length + i];
            if (h)
            {
                h = false;
                _overrides--;
                any = true;
            }
        }
        if (any)
            Changed(weapon);
    }

    /// <summary>Sets a class multiplier (clamped to <see cref="MinMult"/>..<see cref="MaxMult"/>).</summary>
    public static void SetClassMult(WeaponClass c, TweakStat s, float m)
    {
        EnsureState();
        m = float.IsFinite(m) ? Math.Clamp(m, MinMult, MaxMult) : 1f;
        ref var cur = ref _mult[(int)c * (int)TweakStat.COUNT + (int)s];
        if (cur == m)
            return;
        _mults += (cur == 1f ? 1 : 0) - (m == 1f ? 1 : 0);
        cur = m;
        ChangedClass(c);
    }

    /// <summary>A class's multipliers back to 1.</summary>
    public static void RevertClass(WeaponClass c)
    {
        for (var s = 0; s < (int)TweakStat.COUNT; s++)
            SetClassMult(c, (TweakStat)s, 1f);
    }

    /// <summary>"Reset all to files": every override and multiplier gone, every weapon as its file has it.</summary>
    public static void ResetAll()
    {
        if (_file == null)
            return;
        var was = Active;
        Array.Clear(_has);
        _overrides = 0;
        Array.Fill(_mult, 1f);
        _mults = 0;
        for (var i = 0; i < _file.Length; i++)
            Rebuild(i);
        if (was)
            Version++;
    }

    private static void Changed(int weapon)
    {
        Rebuild(weapon);
        Version++;
    }

    private static void ChangedClass(WeaponClass c)
    {
        for (var i = 0; i < _file!.Length; i++)
            if (_file[i].Class == c)
                Rebuild(i);
        Version++;
    }

    /// <summary>
    /// Makes the live weapon its file values, then the overrides, then the class multipliers (whole numbers rounded, all
    /// clamped to the file ranges), and refreshes its kitted copies in place. No allocation.
    /// </summary>
    private static void Rebuild(int weapon)
    {
        var file = _file![weapon];
        var live = Weapons.All[weapon];
        CopyInto(file, live);
        var row = weapon * Fields.Length;
        for (var i = 0; i < Fields.Length; i++)
            if (_has[row + i])
                Fields[i].Set(live, _val[row + i]);
        var c = (int)file.Class * (int)TweakStat.COUNT;
        for (var s = 0; s < (int)TweakStat.COUNT; s++)
        {
            var m = _mult[c + s];
            if (m == 1f)
                continue;
            var f = StatField[s];
            var v = f.Get(live) * m;
            if (f.IsInt)
                v = MathF.Max(1, v);
            f.Set(live, f.Sanitize(v));
        }
        Attachments.RefreshKits(live);
    }

    /// <summary>Self-test: copies of the weapons as their files have them now (to put back with <see cref="RestoreFileDefs"/>).</summary>
    internal static WeaponDef[] SnapshotFileDefs() => Weapons.All.Select(w => FileDef(w).Clone()).ToArray();

    /// <summary>Self-test: makes <paramref name="defs"/> the file baseline again (after a Save into a temp folder), with no tweaks.</summary>
    internal static void RestoreFileDefs(WeaponDef[] defs)
    {
        EnsureState();
        for (var i = 0; i < defs.Length && i < _file!.Length; i++)
            CopyInto(defs[i], _file[i]);
        Array.Clear(_has);
        _overrides = 0;
        Array.Fill(_mult, 1f);
        _mults = 0;
        for (var i = 0; i < _file!.Length; i++)
            Rebuild(i);
        WeaponSet.ResetHashes();
        Version++;
    }

    // ---- Feel numbers ----------------------------------------------------------------------------

    /// <summary>Body shots to kill a full-health player at <paramref name="dist"/> metres (every pellet landing).</summary>
    public static int ShotsToKill(WeaponDef w, float dist, bool head = false)
    {
        var per = w.ShotDamage * w.DamageMultAt(dist) * (head ? w.HeadMult : 1f);
        return per <= 0 ? 999 : Math.Min(999, (int)MathF.Ceiling(World.MaxHealth / per));
    }

    /// <summary>
    /// Seconds from the first shot to the killing shot at <paramref name="dist"/> metres, body shots, every pellet landing:
    /// the fire interval (bursts with their pause), plus a reload for each magazine emptied on the way.
    /// </summary>
    public static float TimeToKill(WeaponDef w, float dist, bool head = false)
    {
        var n = ShotsToKill(w, dist, head);
        var k = n - 1; // shots after the first
        float t;
        if (w.Mode == FireMode.Burst && w.BurstCount > 1)
            t = k / w.BurstCount * (w.BurstCount * w.FireInterval + w.BurstDelay) + k % w.BurstCount * w.FireInterval;
        else
            t = k * w.FireInterval;
        if (w.Mag > 0)
            t += k / w.Mag * w.Reload;
        return t;
    }

    /// <summary>The ranges the window shows time to kill at (metres).</summary>
    public static readonly float[] TtkRanges = [5, 15, 30, 50];

    // ---- Network ----------------------------------------------------------------------------------

    /// <summary>
    /// The weapons as a Msg.WeaponTweaks payload (after the id): a class multiplier count (always 0: the multipliers are
    /// baked into the values), then a u16 count and, for every stat of every weapon whose live value differs from the file
    /// as the game loaded it, weapon id, field index and value. Absolute values against the files as loaded, so a client
    /// gets the host's numbers whether it has the host's files from before or after a Save. Ids are the same on every PC
    /// (the weapon fingerprint matched at join) and field indices too (same protocol, same build). At most
    /// <see cref="MaxOverrides"/> entries (one packet).
    /// </summary>
    public static void Write(PacketWriter w)
    {
        w.U8(0);
        var at = w.Length;
        w.U16(0);
        if (_loaded == null)
            return;
        var all = Weapons.All;
        var n = 0;
        for (var i = 0; i < _loaded.Length && i < all.Length; i++)
            for (var k = 0; k < Fields.Length && n < MaxOverrides; k++)
            {
                var f = Fields[k];
                var v = f.Get(all[i]);
                if (v != f.Get(_loaded[i]))
                {
                    w.U8((byte)i).U8((byte)k).F32(v);
                    n++;
                }
            }
        w.PatchU16(at, (ushort)n);
    }

    /// <summary>The live weapons differ from the files as the game loaded them (a tweak, or values saved this session).</summary>
    public static bool DiffersFromLoaded
    {
        get
        {
            if (_loaded == null)
                return false;
            var all = Weapons.All;
            for (var i = 0; i < _loaded.Length && i < all.Length; i++)
                foreach (var f in Fields)
                    if (f.Get(all[i]) != f.Get(_loaded[i]))
                        return true;
            return false;
        }
    }

    /// <summary>Client: replaces the whole set with the host's (bad entries are skipped). Returns the entries applied.</summary>
    public static int Read(ref PacketReader r)
    {
        EnsureState();
        Array.Clear(_has);
        _overrides = 0;
        Array.Fill(_mult, 1f);
        _mults = 0;
        var applied = 0;
        int mults = r.U8();
        for (var i = 0; i < mults; i++)
        {
            int c = r.U8(), s = r.U8();
            var m = r.F32();
            if (c >= ClassCount || s >= (int)TweakStat.COUNT || !float.IsFinite(m))
                continue;
            m = Math.Clamp(m, MinMult, MaxMult);
            ref var cur = ref _mult[c * (int)TweakStat.COUNT + s];
            if (cur == 1f && m != 1f)
                _mults++;
            cur = m;
            applied++;
        }
        int count = r.U16();
        for (var i = 0; i < count; i++)
        {
            int weapon = r.U8(), field = r.U8();
            var v = r.F32();
            if (weapon >= _file!.Length || field >= Fields.Length || !float.IsFinite(v) || _overrides >= MaxOverrides)
                continue;
            var at = weapon * Fields.Length + field;
            if (!_has[at])
                _overrides++;
            _has[at] = true;
            _val[at] = Fields[field].Sanitize(v);
            applied++;
        }
        for (var i = 0; i < _file!.Length; i++)
            Rebuild(i);
        return applied; // not a local change: Version stays (in a self-test the host shares these statics and would echo it)
    }

    // ---- Saving -----------------------------------------------------------------------------------

    /// <summary>The repository's Weapons folder in a dev build run from bin/ (null in a release or when there isn't one).</summary>
    public static string? RepoWeaponsFolder()
    {
        if (BuildInfo.IsRelease)
            return null;
        for (var d = new DirectoryInfo(AppFolders.Exe); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CallOfSlop.csproj")) && Directory.Exists(Path.Combine(d.FullName, "Weapons")))
                return Path.GetFullPath(Path.Combine(d.FullName, "Weapons")) is var repo && !SameFolder(repo, Weapons.Folder) ? repo : null;
        return null;
    }

    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// "Save to weapon files": every weapon that differs from its file gets its live stats written into the file, in each of
    /// <paramref name="folders"/> that has it (the game's Weapons folder, and in a dev build the repository's): only the
    /// changed values' text is replaced (<see cref="WeaponFilePatch"/>), so the file keeps its layout and the diff shows just
    /// those numbers. Each patched text is loaded back first and must read as exactly the weapon with the new values.
    /// After saving, the files are the new baseline: the overrides and multipliers are cleared (the live weapons don't
    /// change) and the weapon fingerprint is worked out again. Returns a line per file and the problems.
    /// </summary>
    public static (int Saved, List<string> Lines) Save(IReadOnlyList<string> folders)
    {
        var lines = new List<string>();
        if (_file == null || !Active)
            return (0, lines);
        var saved = 0;
        var failed = false;
        var all = Weapons.All;
        var done = new List<int>();
        for (var i = 0; i < all.Length; i++)
        {
            var live = all[i];
            var file = _file[i];
            var changes = Fields.Where(f => (!f.LauncherOnly || live.IsLauncher) && f.Get(live) != f.Get(file)).ToArray();
            if (changes.Length == 0)
                continue;
            if (file.SourceFile == null)
            {
                lines.Add($"{live.Name}: built-in weapon with no file; not saved");
                failed = true;
                continue;
            }
            try
            {
                WeaponFile.Validate(live, file.SourceFile); // a value pair the file can't hold (launcher maxSpeed below speed...)
            }
            catch (WeaponFileException ex)
            {
                lines.Add($"not saved: {ex.Message}");
                failed = true;
                continue;
            }
            var wrote = 0;
            foreach (var folder in folders)
            {
                var path = Path.Combine(folder, file.SourceFile);
                if (!File.Exists(path))
                    continue;
                try
                {
                    var text = File.ReadAllText(path);
                    var patched = PatchText(text, file.SourceFile, live, changes);
                    var tmp = path + ".tweak.tmp";
                    File.WriteAllText(tmp, patched);
                    File.Move(tmp, path, overwrite: true);
                    wrote++;
                    lines.Add($"saved {path} ({changes.Length} value(s))");
                }
                catch (Exception ex) when (ex is WeaponFileException or IOException or UnauthorizedAccessException)
                {
                    lines.Add($"{path}: {ex.Message}");
                    failed = true;
                }
            }
            if (wrote == 0)
            {
                lines.Add($"{live.Name}: {file.SourceFile} wasn't found in {string.Join(" or ", folders)}");
                failed = true;
                continue;
            }
            saved++;
            done.Add(i);
        }
        // The saved weapons' files now hold their live values: that is their new baseline.
        foreach (var i in done)
            foreach (var f in Fields)
                f.Set(_file[i], f.Get(all[i]));
        if (!failed)
        {
            Array.Clear(_has);
            _overrides = 0;
            Array.Fill(_mult, 1f);
            _mults = 0;
        }
        else
            foreach (var i in done)
                for (var k = 0; k < Fields.Length; k++)
                    if (_has[i * Fields.Length + k])
                    {
                        _has[i * Fields.Length + k] = false; // the value is in the file now (its class multiplier, if any, stays: it's on the others)
                        _overrides--;
                    }
        for (var i = 0; i < all.Length; i++)
            Rebuild(i);
        WeaponSet.ResetHashes();
        Version++;
        return (saved, lines);
    }

    /// <summary>
    /// A weapon file's text with <paramref name="changes"/> set to the values <paramref name="live"/> has, and the check that
    /// it loads as the weapon the text had with just those values changed. Throws <see cref="WeaponFileException"/> if not.
    /// </summary>
    public static string PatchText(string text, string source, WeaponDef live, IReadOnlyList<TweakField> changes)
    {
        var before = WeaponFile.Load(text, source);
        var patch = new WeaponFilePatch(text, source);
        foreach (var f in changes)
            patch.Set(f.Block, f.Key, f.FileText(f.Get(live)));
        var patched = patch.Result();
        var expected = before.Clone();
        foreach (var f in changes)
            f.Set(expected, f.Get(live));
        var after = WeaponFile.Load(patched, source);
        if (WeaponFile.ToJson(after) != WeaponFile.ToJson(expected))
            throw new WeaponFileException($"{source}: the saved file wouldn't read back as the tweaked weapon; not saved");
        return patched;
    }
}
