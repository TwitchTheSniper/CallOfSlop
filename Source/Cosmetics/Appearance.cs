using System.Numerics;

namespace CallOfSlop;

/// <summary>The axes a player's look varies on: the same ones bots vary on (<see cref="BodyModel.VariantFor(string, byte)"/>), plus the outfit colour.</summary>
public enum LookAxis : byte
{
    Skin, Hair, Headwear, Beard, Gear, Outfit,
}

/// <summary>
/// The look a person picked on the APPEARANCE screen, one value per <see cref="LookAxis"/>. Every value is 0 for
/// <b>Auto</b> (the look the game picks from your name, exactly as before this screen existed and as bots get theirs),
/// or 1 + the index of an option bots already use: <see cref="BodyModel.SkinTones"/>, <see cref="BodyModel.HairColors"/>,
/// <see cref="CallOfSlop.Headwear"/>, beard (1 none, 2 beard), <see cref="BodyModel.GearColors"/> and the free-for-all
/// outfit colours (<see cref="Player.Palette"/>). <c>default</c> is all Auto. Cosmetic only: never used for gameplay.
/// On the network it's 3 bytes (<see cref="Pack"/>): a nibble per axis. A value this build doesn't know (a removed variant,
/// a newer build, a hostile packet) falls back to Auto for that axis (<see cref="Sanitized"/>).
/// </summary>
public readonly record struct PlayerLook(byte Skin = 0, byte Hair = 0, byte Headwear = 0, byte Beard = 0, byte Gear = 0, byte Outfit = 0)
{
    /// <summary>Bytes on the wire (<see cref="Pack"/>).</summary>
    public const int WireBytes = 3;

    /// <summary>All Auto: the name-based look (what bots and players who never opened the screen wear).</summary>
    public static PlayerLook Auto => default;

    /// <summary>How many options an axis has (not counting Auto).</summary>
    public static int Options(LookAxis axis) => axis switch
    {
        LookAxis.Skin => BodyModel.SkinTones.Length,
        LookAxis.Hair => BodyModel.HairColors.Length,
        LookAxis.Headwear => BodyModel.HeadwearParts.Length,
        LookAxis.Beard => 2,
        LookAxis.Gear => BodyModel.GearColors.Length,
        _ => Player.Palette.Length,
    };

    /// <summary>The value on an axis (0 Auto, 1.. an option).</summary>
    public byte Get(LookAxis axis) => axis switch
    {
        LookAxis.Skin => Skin, LookAxis.Hair => Hair, LookAxis.Headwear => Headwear, LookAxis.Beard => Beard, LookAxis.Gear => Gear, _ => Outfit,
    };

    /// <summary>A copy with one axis set (0 Auto, 1.. an option).</summary>
    public PlayerLook With(LookAxis axis, int value)
    {
        var v = (byte)Math.Clamp(value, 0, 15);
        return axis switch
        {
            LookAxis.Skin => this with { Skin = v },
            LookAxis.Hair => this with { Hair = v },
            LookAxis.Headwear => this with { Headwear = v },
            LookAxis.Beard => this with { Beard = v },
            LookAxis.Gear => this with { Gear = v },
            _ => this with { Outfit = v },
        };
    }

    /// <summary>Every axis Auto?</summary>
    public bool IsAuto => this == default;

    /// <summary>A copy where any value this build doesn't have becomes Auto (a removed variant falls back to the name's look).</summary>
    public PlayerLook Sanitized()
    {
        // No allocations: this runs when bodies are (re)built.
        static byte Ok(byte v, int options) => v <= options ? v : (byte)0;
        return new PlayerLook(Ok(Skin, Options(LookAxis.Skin)), Ok(Hair, Options(LookAxis.Hair)), Ok(Headwear, Options(LookAxis.Headwear)),
            Ok(Beard, Options(LookAxis.Beard)), Ok(Gear, Options(LookAxis.Gear)), Ok(Outfit, Options(LookAxis.Outfit)));
    }

    /// <summary>
    /// The body variant to draw: <paramref name="auto"/> (the name's look, <see cref="BodyModel.VariantFor(string, byte)"/>)
    /// with every axis that isn't Auto replaced by the chosen option. Out-of-range values keep the name's look.
    /// </summary>
    public BodyVariant Apply(in BodyVariant auto)
    {
        var s = Sanitized();
        return new BodyVariant(
            s.Skin > 0 ? (byte)(s.Skin - 1) : auto.Skin,
            s.Hair > 0 ? (byte)(s.Hair - 1) : auto.HairColor,
            s.Headwear > 0 ? (CallOfSlop.Headwear)(s.Headwear - 1) : auto.Headwear,
            s.Beard > 0 ? s.Beard == 2 : auto.Beard,
            s.Gear > 0 ? (byte)(s.Gear - 1) : auto.Gear);
    }

    /// <summary>The free-for-all outfit colour: the chosen one, or <paramref name="auto"/> (the player slot's colour) on Auto.</summary>
    public Vector3 OutfitColor(Vector3 auto)
    {
        var o = Outfit;
        return o > 0 && o <= Player.Palette.Length ? Player.Palette[o - 1] : auto;
    }

    /// <summary>The 3 wire bytes: skin | hair &lt;&lt; 4, headwear | gear &lt;&lt; 4, beard | outfit &lt;&lt; 4.</summary>
    public void Pack(PacketWriter w) =>
        w.U8((byte)((Skin & 15) | (Hair & 15) << 4)).U8((byte)((Headwear & 15) | (Gear & 15) << 4)).U8((byte)((Beard & 15) | (Outfit & 15) << 4));

    /// <summary>Reads <see cref="WireBytes"/> written by <see cref="Pack"/> (sanitised). Missing bytes: Auto.</summary>
    public static PlayerLook Unpack(ref PacketReader r)
    {
        if (!r.Has(WireBytes))
            return default;
        var a = r.U8();
        var b = r.U8();
        var c = r.U8();
        return new PlayerLook(Skin: (byte)(a & 15), Hair: (byte)(a >> 4), Headwear: (byte)(b & 15), Gear: (byte)(b >> 4), Beard: (byte)(c & 15),
            Outfit: (byte)(c >> 4)).Sanitized();
    }

    /// <summary>A random concrete option on every axis (the screen's Random button).</summary>
    public static PlayerLook Randomized(System.Random rng)
    {
        var l = default(PlayerLook);
        foreach (var axis in Enum.GetValues<LookAxis>())
            l = l.With(axis, 1 + rng.Next(Options(axis)));
        return l;
    }

    // ---- Names (the screen and the save file) ----

    private static readonly string[] SkinNames = ["Fair", "Light", "Medium", "Tan", "Brown", "Dark"];
    private static readonly string[] HairNames = ["Black", "Dark Brown", "Brown", "Blond", "Grey", "Auburn"];
    private static readonly string[] HeadwearNames = ["Hair", "Buzz Cut", "Helmet", "Patrol Cap", "Bandana"];
    private static readonly string[] BeardNames = ["Clean-Shaven", "Beard"];
    private static readonly string[] GearNames = ["Olive", "Tan", "Black", "Grey-Green"];
    private static readonly string[] OutfitNames = ["Blue", "Red", "Green", "Yellow", "Purple", "Cyan", "Orange", "White"];

    /// <summary>The heading of an axis on the screen.</summary>
    public static string AxisName(LookAxis axis) => axis switch
    {
        LookAxis.Skin => "Skin Tone",
        LookAxis.Hair => "Hair Colour",
        LookAxis.Headwear => "Headwear",
        LookAxis.Beard => "Beard",
        LookAxis.Gear => "Helmet / Cap Colour",
        _ => "Outfit Colour",
    };

    /// <summary>The save-file key of an axis.</summary>
    public static string AxisKey(LookAxis axis) => axis.ToString().ToLowerInvariant();

    /// <summary>The display name of option <paramref name="value"/> (1..) on an axis ("Auto" for 0, "Option N" past the named ones).</summary>
    public static string OptionName(LookAxis axis, int value)
    {
        if (value <= 0)
            return "Auto";
        var names = axis switch
        {
            LookAxis.Skin => SkinNames, LookAxis.Hair => HairNames, LookAxis.Headwear => HeadwearNames, LookAxis.Beard => BeardNames,
            LookAxis.Gear => GearNames, _ => OutfitNames,
        };
        return value - 1 < names.Length ? names[value - 1] : $"Option {value}";
    }

    /// <summary>The save-file word for a value ("auto", "dark_brown", "helmet").</summary>
    public static string OptionKey(LookAxis axis, int value) =>
        value <= 0 ? "auto" : OptionName(axis, value).ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

    /// <summary>Reads a save-file word (or a 1-based number) back; anything unknown is Auto.</summary>
    public static byte ParseOption(LookAxis axis, string? text)
    {
        var t = (text ?? "").Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        if (t.Length == 0 || t == "auto")
            return 0;
        if (int.TryParse(t, out var n))
            return n >= 1 && n <= Options(axis) ? (byte)n : (byte)0;
        for (var v = 1; v <= Options(axis); v++)
            if (OptionKey(axis, v) == t)
                return (byte)v;
        return 0;
    }

    /// <summary>The swatch colour of an option, or null for axes that aren't colours (headwear, beard).</summary>
    public static Vector3? Swatch(LookAxis axis, int value)
    {
        if (value <= 0 || value > Options(axis))
            return null;
        return axis switch
        {
            LookAxis.Skin => BodyModel.SkinTones[value - 1],
            LookAxis.Hair => BodyModel.HairColors[value - 1],
            LookAxis.Gear => BodyModel.GearColors[value - 1],
            LookAxis.Outfit => Player.Palette[value - 1],
            _ => null,
        };
    }

    /// <summary>The option an Auto axis resolves to for this name (1-based, for the screen's "Auto (Medium)").</summary>
    public static int AutoValue(LookAxis axis, in BodyVariant auto, byte id) => axis switch
    {
        LookAxis.Skin => auto.Skin % BodyModel.SkinTones.Length + 1,
        LookAxis.Hair => auto.HairColor % BodyModel.HairColors.Length + 1,
        LookAxis.Headwear => (int)auto.Headwear + 1,
        LookAxis.Beard => auto.Beard ? 2 : 1,
        LookAxis.Gear => auto.Gear % BodyModel.GearColors.Length + 1,
        _ => id % Player.Palette.Length + 1,
    };

    /// <summary>A short readable form for logs ("skin=tan hair=auto ...").</summary>
    public override string ToString()
    {
        var self = this; // (a lambda in a struct can't use this)
        return string.Join(" ", Enum.GetValues<LookAxis>().Select(a => $"{AxisKey(a)}={OptionKey(a, self.Get(a))}"));
    }
}

/// <summary>
/// Your appearance, saved to <c>appearance.txt</c> in the save folder (<see cref="SavePaths"/>, i.e. %APPDATA%\CallOfSlop):
/// the prestige hat (moved here from loadout.txt's <c>hat=</c> line) and the look on every <see cref="LookAxis"/>.
/// <code>
///   version=1
///   hat=fedora           (none, or a hat key from Cosmetics/hats.json)
///   skin=tan             (auto, or an option name, see PlayerLook.OptionKey)
///   hair=auto
///   headwear=helmet
///   beard=beard
///   gear=olive
///   outfit=green         (free-for-all only: team modes wear the team colour)
/// </code>
/// Migration: when there's no appearance.txt yet, the hat comes from loadout.txt's old <c>hat=</c> line (older builds kept
/// it there), and the look is all Auto (the name-based look players had before). loadout.txt is no longer written with a
/// hat. Unknown words load as Auto / no hat.
/// </summary>
public static class AppearanceStore
{
    /// <summary>The file name in the save folder.</summary>
    public const string FileName = "appearance.txt";

    /// <summary>The <c>version=</c> line written to appearance.txt.</summary>
    public const int SaveVersion = 1;

    private static string PathName => SavePaths.PathOf(FileName);

    /// <summary>The hat you wear (kept while it's locked at your prestige, but worn as None until it unlocks).</summary>
    public static HatId Hat;

    /// <summary>Your look (Auto on every axis for a new player).</summary>
    public static PlayerLook Look;

    /// <summary>The smoke test sets this so it never writes your appearance.txt.</summary>
    public static bool ReadOnly;

    /// <summary>The hat you'd actually wear: <see cref="Hat"/> if your prestige unlocks it, otherwise None.</summary>
    public static HatId EffectiveHat(Progress? progress) =>
        Cosmetics.IsUnlocked(Hat, CosmeticPrestige.Of(progress)) ? Hat : HatId.None;

    /// <summary>Reads appearance.txt (or migrates the hat from loadout.txt when there's none yet) and writes it back.</summary>
    public static void Load()
    {
        string[]? lines = null;
        string[]? legacy = null;
        try
        {
            lines = SavePaths.ReadLines(PathName, SaveVersion);
            if (lines == null && File.Exists(PathName))
                lines = []; // damaged and no backup: defaults
            if (lines == null)
                legacy = SavePaths.ReadLines(SavePaths.PathOf("loadout.txt"), LoadoutStore.SaveVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lines = [];
        }
        LoadLines(lines, legacy);
        if (lines == null)
        {
            Console.WriteLine($"[appearance] no {FileName} yet: hat {Cosmetics.KeyOf(Hat)} {(legacy != null ? "taken from loadout.txt" : "(new player)")}, look auto");
            Save();
        }
    }

    /// <summary>
    /// Reads appearance.txt lines (null: no file yet). With no file, <paramref name="legacyLoadout"/> (loadout.txt's lines,
    /// or null) supplies the hat from its old <c>hat=</c> line. The self-test calls this directly.
    /// </summary>
    public static void LoadLines(string[]? fileLines, string[]? legacyLoadout = null)
    {
        Hat = HatId.None;
        Look = default;
        if (fileLines == null)
        {
            Hat = LegacyHat(legacyLoadout);
            return;
        }
        foreach (var line in fileLines)
        {
            var parts = line.Split('=', 2);
            if (parts.Length != 2)
                continue;
            var key = parts[0].Trim().ToLowerInvariant();
            var value = parts[1].Trim();
            if (key == "hat")
            {
                Hat = Cosmetics.FromKey(value); // unknown or "none": no hat
                continue;
            }
            foreach (var axis in Enum.GetValues<LookAxis>())
                if (key == PlayerLook.AxisKey(axis))
                    Look = Look.With(axis, PlayerLook.ParseOption(axis, value));
        }
        Look = Look.Sanitized();
    }

    /// <summary>The hat in an old loadout.txt (its <c>hat=</c> line; None if there's no file or no line).</summary>
    public static HatId LegacyHat(string[]? loadoutLines)
    {
        if (loadoutLines == null)
            return HatId.None;
        var hat = HatId.None;
        foreach (var line in loadoutLines)
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2 && parts[0].Trim() == "hat")
                hat = Cosmetics.FromKey(parts[1]);
        }
        return hat;
    }

    /// <summary>The lines appearance.txt is saved as (without the version line).</summary>
    public static List<string> SaveLines()
    {
        var lines = new List<string> { $"hat={Cosmetics.KeyOf(Hat)}" };
        foreach (var axis in Enum.GetValues<LookAxis>())
            lines.Add($"{PlayerLook.AxisKey(axis)}={PlayerLook.OptionKey(axis, Look.Get(axis))}");
        return lines;
    }

    /// <summary>Writes appearance.txt (atomically, with a backup). Nothing in the smoke test.</summary>
    public static void Save()
    {
        if (ReadOnly)
            return;
        SavePaths.WriteLines(PathName, SaveLines(), SaveVersion);
    }
}
