using System.Runtime.CompilerServices;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>A robot shell's works that wear (Karl, 2026-11-17): the index into <see cref="ShellCond"/>.</summary>
public enum ShellWork : byte
{
    LegL, // left leg actuator
    LegR, // right leg actuator
    ArmL, // left arm actuator
    ArmR, // right arm actuator
    Power, // the internal power cell
    Cpu, // the processor
    Optics, // the visor's optics
    Plating, // the shell's plating
}

/// <summary>Each of a shell's works' wear, 0 (new) to <see cref="ShellWear.Failed"/> (it has failed). Inline: nothing allocates.</summary>
[InlineArray(ShellWear.Works)]
public struct ShellCond
{
    private byte _e;
}

/// <summary>
/// Robot shell wear as data and rules (Constructs/ShellWear.json; the game's side is Game/GameShellWear.cs; docs/sandbox/constructs/joints-and-logic.md "Robot shell wear";
/// Karl, 2026-11-17: "mild, but over time they wear down and need maintenance"). Wear is a byte a work: 0 new, 255 failed. A
/// standing shell keeps its wear in four hidden settings of its part (two works a setting, a byte each), so it rides saves, blueprints and
/// CBody like any setting; a worn shell's is the server's own while it is worn, and every peer hears it with <see cref="Msg.Shell"/>.
/// Below <see cref="WornPct"/> a work shows its fault; at 0 % it has failed. The spare parts that put a work back to new are the
/// part kit's (ShellParts.cs, "Shell Leg Actuator" and the rest).
/// </summary>
public sealed class ShellWear
{
    public const string Format = "call-of-slop-shell-wear/1";
    public const int Works = 8;
    public const byte Failed = 255;

    public float LegHours = 5, ArmHours = 7, PowerHours = 6, ProcessorHours = 9, OpticsHours = 8, PlatingHours = 12;
    public float WornPct = 40, ServiceToPct = 70, HitPlating = 0.25f, HitOther = 0.12f;
    public float HitLimb = 1.2f, LimbHealth = 0.25f, HeadChance = 0.5f, HitHead = 1.5f; // where a hit lands (Karl, 2026-11-18: GameShellWear.ShellHit)
    public string ServiceMaterial = "metal";
    public int ServiceCost = 1;
    public string Problem = "";

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "ShellWear.json");

    private static ShellWear? _current;

    /// <summary>The table in use (loaded once).</summary>
    public static ShellWear Current => _current ??= Load(FilePath);

    public static ShellWear Load(string path)
    {
        var t = new ShellWear();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            t.LegHours = Num(root, "legHours", 0.05f, 1000);
            t.ArmHours = Num(root, "armHours", 0.05f, 1000);
            t.PowerHours = Num(root, "powerHours", 0.05f, 1000);
            t.ProcessorHours = Num(root, "processorHours", 0.05f, 1000);
            t.OpticsHours = Num(root, "opticsHours", 0.05f, 1000);
            t.PlatingHours = Num(root, "platingHours", 0.05f, 1000);
            t.WornPct = Num(root, "worn", 1, 99);
            t.ServiceToPct = Num(root, "serviceTo", 1, 100);
            t.ServiceMaterial = root.GetProperty("serviceMaterial").GetString() ?? "";
            if (RecyclingTable.Current.IdOf(t.ServiceMaterial) == 0)
                throw new FormatException($"\"serviceMaterial\": no material named \"{t.ServiceMaterial}\" in Recycling.json");
            t.ServiceCost = (int)Num(root, "serviceCost", 0, 1000);
            t.HitPlating = Num(root, "hitPlating", 0, 100);
            t.HitOther = Num(root, "hitOther", 0, 100);
            t.HitLimb = Num(root, "hitLimb", 0, 100);
            t.LimbHealth = Num(root, "limbHealth", 0, 1);
            t.HeadChance = Num(root, "headChance", 0, 1);
            t.HitHead = Num(root, "hitHead", 0, 100);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            t.Problem = $"{Path.GetFileName(path)}: {ex.Message}";
            Console.WriteLine($"[shellwear] REFUSED {t.Problem}: robot shells don't wear");
        }
        return t;
    }

    private static float Num(JsonElement root, string key, float min, float max)
    {
        var v = root.GetProperty(key).GetSingle();
        if (!float.IsFinite(v) || v < min || v > max)
            throw new FormatException($"\"{key}\" is {v}, not between {min} and {max}");
        return v;
    }

    /// <summary>Hours worn (working) from new to failed for this work.</summary>
    public float HoursOf(ShellWork w) => w switch
    {
        ShellWork.LegL or ShellWork.LegR => LegHours,
        ShellWork.ArmL or ShellWork.ArmR => ArmHours,
        ShellWork.Power => PowerHours,
        ShellWork.Cpu => ProcessorHours,
        ShellWork.Optics => OpticsHours,
        _ => PlatingHours,
    };

    // ---- What a wear means -----------------------------------------------------------------------------------------

    /// <summary>Condition, % (100 new, 0 failed).</summary>
    public static int Pct(byte wear) => wear >= Failed ? 0 : Math.Max(1, (int)MathF.Round(100f * (1f - wear / 255f)));

    /// <summary>The wear of a condition, %.</summary>
    public static byte WearOf(float pct) => pct <= 0 ? Failed : (byte)Math.Clamp((int)MathF.Round(255f * (1f - pct / 100f)), 0, Failed - 1);

    /// <summary>Is it worn enough to show (and not failed)?</summary>
    public bool Worn(byte wear) => wear < Failed && Pct(wear) < WornPct;

    /// <summary>0 at <see cref="WornPct"/> and above, rising to 1 as it nears failure (how strongly a fault shows); 1 failed.</summary>
    public float Fault(byte wear) => wear >= Failed ? 1 : Math.Clamp((WornPct - Pct(wear)) / WornPct, 0f, 1f);

    /// <summary>
    /// How fast a shell in this condition moves, as a share of a body's: a worn leg limps (down to 80 %), a lost one hops (50 %); with both
    /// lost it crawls on its arms at a prone body's pace (<see cref="Game.ShellLegless"/>: the stance already slows it), slower with an arm
    /// hanging (70 %, both 40 %); a tired power cell is sluggish (down to 80 %, flat 65 %). 1: nothing wrong.
    /// </summary>
    public float MoveShare(in ShellCond c)
    {
        var lost = (c[(int)ShellWork.LegL] >= Failed ? 1 : 0) + (c[(int)ShellWork.LegR] >= Failed ? 1 : 0);
        var arms = (c[(int)ShellWork.ArmL] >= Failed ? 1 : 0) + (c[(int)ShellWork.ArmR] >= Failed ? 1 : 0);
        var k = lost switch { 2 => arms switch { 2 => 0.4f, 1 => 0.7f, _ => 1f }, 1 => 0.5f, _ => 1f };
        if (lost == 0)
            k *= 1f - 0.2f * MathF.Max(Fault(c[(int)ShellWork.LegL]), Fault(c[(int)ShellWork.LegR]));
        var power = c[(int)ShellWork.Power];
        k *= power >= Failed ? 0.65f : 1f - 0.2f * Fault(power);
        return Math.Clamp(k, 0.25f, 1f);
    }

    /// <summary>A hit's damage on a shell with this plating: worn plating lets more through (up to 1.4x, failed 1.5x).</summary>
    public float HitShare(in ShellCond c) => c[(int)ShellWork.Plating] >= Failed ? 1.5f : 1f + 0.4f * Fault(c[(int)ShellWork.Plating]);

    /// <summary>The spare part that puts this work back to new (the part kit's ShellParts.cs names).</summary>
    public static string SpareName(ShellWork w) => w switch
    {
        ShellWork.LegL or ShellWork.LegR => "Shell Leg Actuator",
        ShellWork.ArmL or ShellWork.ArmR => "Shell Arm Actuator",
        ShellWork.Power => "Shell Power Cell",
        ShellWork.Cpu => "Shell Processor",
        ShellWork.Optics => "Shell Optics",
        _ => "Shell Plating",
    };

    private static readonly string[] Names = ["left leg actuator", "right leg actuator", "left arm actuator", "right arm actuator", "power cell", "processor", "optics", "plating"];
    private static readonly string[] Shorts = ["L leg", "R leg", "L arm", "R arm", "power", "processor", "optics", "plating"];
    private static readonly string[] WornWords = ["limping", "limping", "shaky", "shaky", "sluggish", "glitching", "static", "thin"];
    private static readonly string[] FailedWords = ["leg lost", "leg lost", "arm hangs", "arm hangs", "flat", "failing", "near blind", "cracked"];

    /// <summary>For people: "left leg actuator".</summary>
    public static string NameOf(ShellWork w) => Names[(int)w];

    /// <summary>For the HUD: "L leg".</summary>
    public static string ShortOf(ShellWork w) => Shorts[(int)w];

    /// <summary>What the fault looks like: "limping", "leg lost" (empty: nothing shows).</summary>
    public string Symptom(ShellWork w, byte wear) => wear >= Failed ? FailedWords[(int)w] : Worn(wear) ? WornWords[(int)w] : "";

    /// <summary>The worst work (the most worn), and its wear.</summary>
    public static ShellWork Worst(in ShellCond c, out byte wear)
    {
        var at = 0;
        for (var k = 1; k < Works; k++)
            if (c[k] > c[at])
                at = k;
        wear = c[at];
        return (ShellWork)at;
    }

    // ---- A standing shell's settings ----------------------------------------------------------------------------------

    private static readonly CfgKey[] Keys = [CfgKey.ShellWear0, CfgKey.ShellWear1, CfgKey.ShellWear2, CfgKey.ShellWear3];

    /// <summary>Every Robot Shell has these (ConstructConfig.Parse): its works' wear, two a setting, hidden (they ride saves and CBody).</summary>
    public static void AddSettings(ConstructPartDef d, List<CConfigDef> list)
    {
        if (!d.ShellBody && d.ShellPod == null)
            return; // (a shell pod keeps its docked shell's wear, damage and kit: ConstructShellPod.cs)
        foreach (var k in Keys)
            if (!list.Exists(e => e.Id == k))
                list.Add(new CConfigDef { Key = ConstructConfig.KeyName(k), Id = k, Type = CfgType.Mask, Hidden = true, Label = "Shell wear", Group = "Shell", Tooltip = "Not on the panel." });
        if (!list.Exists(e => e.Id == CfgKey.ShellDamage))
            list.Add(new CConfigDef { Key = ConstructConfig.KeyName(CfgKey.ShellDamage), Id = CfgKey.ShellDamage, Type = CfgType.Number, Min = 0, Max = World.MaxHealth, Step = 1,
                Hidden = true, Label = "Shell damage", Group = "Shell", Tooltip = "Not on the panel." });
        ShellFrames.AddSettings(d, list); // (its modules: the Shell Gantry)
    }

    /// <summary>
    /// A standing shell's health (Karl, 2026-11-17: shells don't heal by themselves): what it had when it was last stood down,
    /// kept in a hidden setting as the health it has lost, so it rides saves, blueprints and CBody. A repair puts it back.
    /// </summary>
    public static int HealthOf(in CPart p, ConstructPartDef d) =>
        Math.Clamp(World.MaxHealth - (int)MathF.Round(ConstructConfig.Get(p, d, CfgKey.ShellDamage, 0)), 1, World.MaxHealth);

    /// <summary>...and written back.</summary>
    public static void PutHealth(ref CPart p, ConstructPartDef d, int health) =>
        ConstructConfig.Put(ref p, d, CfgKey.ShellDamage, World.MaxHealth - Math.Clamp(health, 0, World.MaxHealth));

    /// <summary>A standing shell's wear, read from its part's settings.</summary>
    public static void Read(in CPart p, ConstructPartDef d, ref ShellCond c)
    {
        for (var s = 0; s < Keys.Length; s++)
        {
            var v = (int)ConstructConfig.Get(p, d, Keys[s], 0);
            c[s * 2] = (byte)(v & 0xFF);
            c[s * 2 + 1] = (byte)(v >> 8 & 0xFF);
        }
    }

    /// <summary>...and written back.</summary>
    public static void Write(ref CPart p, ConstructPartDef d, in ShellCond c)
    {
        for (var s = 0; s < Keys.Length; s++)
            ConstructConfig.Put(ref p, d, Keys[s], c[s * 2] | c[s * 2 + 1] << 8);
    }

    /// <summary>Is anything on it worn past the service mark (or failed)?</summary>
    public bool NeedsService(in ShellCond c)
    {
        var mark = WearOf(ServiceToPct);
        for (var k = 0; k < Works; k++)
            if (c[k] > mark)
                return true;
        return false;
    }
}
