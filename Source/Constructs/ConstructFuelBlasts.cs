using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// How a tank with fuel in it goes up when it is destroyed (Constructs/FuelBlasts.json; docs/sandbox/CONSTRUCTS.md, "Fuel blasts"), loaded once;
/// the defaults when the file is missing or refused. A tank's blast comes from the litres it held at that moment times its fluid's strength
/// (its "energy"): the radius grows with the cube root, the damage with the square root, both held to the caps. Empty: no blast.
/// </summary>
public sealed class FuelBlastTable
{
    public const string Format = "call-of-slop-fuel-blasts/1";
    public float MinLitres = 0.5f; // less than this (in energy-litres) is no blast
    public float RadiusBase = 0.5f, RadiusPerCubeRoot = 1f, MaxRadius = 160f;
    public float DamageBase = 60f, DamagePerRoot = 30f, MaxDamage = 3000f;
    public float CraterPerRadius = 0.2f, MaxCrater = 25f;
    public int DelayTicks = 18, StaggerTicks = 6; // ticks after it breaks; a chain's tanks a few ticks apart
    public float PropellantPerKg = 1.5f; // a designed weapon's motor and tank fuel (kg) with no warhead: energy-litres a kg
    public readonly float[] Energy = new float[Enum.GetValues<Fluid>().Length]; // energy-litres a litre, by fluid (0: doesn't burn)
    public string Problem = "";

    public FuelBlastTable()
    {
        Energy[(int)Fluid.Fuel] = 1f;
        Energy[(int)Fluid.Hydrogen] = 1.5f;
        Energy[(int)Fluid.Oxidiser] = 0.6f;
        Energy[(int)Fluid.Oil] = 0.35f;
    }

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "FuelBlasts.json");

    private static FuelBlastTable? _current;

    public static FuelBlastTable Current => _current ??= Load(FilePath);

    /// <summary>Sets the table in use (a test; null: load the file again).</summary>
    public static void Use(FuelBlastTable? t) => _current = t;

    public static FuelBlastTable Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ConstructPartException or InvalidOperationException or FormatException)
        {
            Console.WriteLine($"[constructs] {path}: {ex.Message}: the defaults stand");
            return new FuelBlastTable { Problem = ex.Message };
        }
    }

    public static FuelBlastTable Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (ConstructParts.Opt(root, "format")?.GetString() != Format)
            throw new ConstructPartException($"\"format\" must be \"{Format}\"");
        ConstructParts.Known(root, ["format", "about", "minLitres", "radiusBase", "radiusPerCubeRoot", "maxRadius", "damageBase", "damagePerRoot", "maxDamage", "craterPerRadius", "maxCrater", "delayTicks", "staggerTicks", "propellantPerKg", "energy"], "FuelBlasts.json");
        var t = new FuelBlastTable();
        static float Num(JsonElement e, string key, float now, float lo, float hi)
        {
            if (ConstructParts.Opt(e, key) is not { } v)
                return now;
            var n = v.GetSingle();
            if (!(n >= lo && n <= hi))
                throw new ConstructPartException($"\"{key}\" is {lo} to {hi}");
            return n;
        }
        t.MinLitres = Num(root, "minLitres", t.MinLitres, 0, 1e6f);
        t.RadiusBase = Num(root, "radiusBase", t.RadiusBase, 0, 100);
        t.RadiusPerCubeRoot = Num(root, "radiusPerCubeRoot", t.RadiusPerCubeRoot, 0, 100);
        t.MaxRadius = Num(root, "maxRadius", t.MaxRadius, 0.5f, ConstructWorld.MegaMaxRadius);
        t.DamageBase = Num(root, "damageBase", t.DamageBase, 0, 1e6f);
        t.DamagePerRoot = Num(root, "damagePerRoot", t.DamagePerRoot, 0, 1e6f);
        t.MaxDamage = Num(root, "maxDamage", t.MaxDamage, 1, 1e7f);
        t.CraterPerRadius = Num(root, "craterPerRadius", t.CraterPerRadius, 0, 2);
        t.MaxCrater = Num(root, "maxCrater", t.MaxCrater, 0, ConstructWorld.MegaCraterMax);
        t.DelayTicks = (int)Num(root, "delayTicks", t.DelayTicks, 1, 600);
        t.StaggerTicks = (int)Num(root, "staggerTicks", t.StaggerTicks, 1, 600);
        t.PropellantPerKg = Num(root, "propellantPerKg", t.PropellantPerKg, 0, 1000);
        if (ConstructParts.Opt(root, "energy") is { } energy)
        {
            Array.Clear(t.Energy);
            foreach (var e in energy.EnumerateObject())
            {
                if (!Enum.TryParse<Fluid>(e.Name, true, out var f))
                    throw new ConstructPartException($"\"energy\": no fluid \"{e.Name}\"");
                t.Energy[(int)f] = Num(energy, e.Name, 0, 0, 100);
            }
        }
        return t;
    }

    /// <summary>The blast of <paramref name="litres"/> of <paramref name="fluid"/>. False: too little (or it doesn't burn) for one.</summary>
    public bool Blast(Fluid fluid, float litres, out float radius, out float damage, out float crater) =>
        BlastOf(litres * Energy[(int)fluid], out radius, out damage, out crater);

    /// <summary>The blast of <paramref name="energyLitres"/> (litres of plain fuel, or what is as strong).</summary>
    public bool BlastOf(float energyLitres, out float radius, out float damage, out float crater)
    {
        radius = damage = crater = 0;
        if (!(energyLitres >= MinLitres) || energyLitres <= 0)
            return false;
        radius = MathF.Min(MaxRadius, RadiusBase + RadiusPerCubeRoot * MathF.Cbrt(energyLitres));
        damage = MathF.Min(MaxDamage, DamageBase + DamagePerRoot * MathF.Sqrt(energyLitres));
        crater = MathF.Min(MaxCrater, radius * CraterPerRadius);
        return radius > 0 && damage > 0;
    }
}

public sealed partial class ConstructWorld
{
    /// <summary>Fuel blasts set off (the perf window, a hand check).</summary>
    public int FuelBlastsSet;

    /// <summary>
    /// The litres of something that burns a part holds now, as energy-litres (its fluid's strength in): a fuel tank's share of the body's pool (by
    /// its capacity among the tanks working, itself counted), a Fluid Tank's or Tank Volume's own contents, a designed weapon's fuel with no warhead.
    /// </summary>
    internal static float FuelEnergyIn(Construct c, int part, FuelBlastTable t)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (d.Role == PartRole.FuelTank)
        {
            if (c.Fuel <= 0 || d.FuelCapacity <= 0)
                return 0;
            var cap = 0f;
            foreach (var i in c.Specials) // (tanks are special parts)
                if (i != part && c.Parts[i].D.Role == PartRole.FuelTank && c.Parts[i].Working)
                    cap += c.Parts[i].D.FuelCapacity;
            cap += d.FuelCapacity;
            return MathF.Min(d.FuelCapacity, c.Fuel * d.FuelCapacity / cap) * t.Energy[(int)d.TankFluid];
        }
        if (d.Role == PartRole.FluidTank && !d.IsEnvelope && ConstructFluid.Held(p, d) is { } f)
            return ConstructFluid.Litres(p, d) * t.Energy[(int)f];
        if (d.Burns && !d.Warhead && d.OrdStats is { } st)
        {
            var kg = 0f;
            foreach (var s in st.Flight.Stages)
                kg += s.Fuel;
            return kg * t.PropellantPerKg;
        }
        return 0;
    }

    /// <summary>
    /// Server: a part holding fuel broke (<see cref="CookOff"/>): it goes off, scaled by what it held, as a landed incendiary charge a beat later
    /// (<see cref="StepShells"/> walks the blasts a few a tick within the parts budget, so a chain of tanks spreads over ticks; a big one is a
    /// shock front). Its fuel leaves the pool. False: it held too little.
    /// </summary>
    private bool FuelBlast(Construct c, int part, Vector3 at)
    {
        var t = FuelBlastTable.Current;
        var energy = FuelEnergyIn(c, part, t);
        if (!t.BlastOf(energy, out var radius, out var damage, out var crater))
            return false;
        var d = c.Parts[part].D;
        if (d.Role == PartRole.FuelTank && t.Energy[(int)d.TankFluid] > 0)
            c.Fuel = MathF.Max(0, c.Fuel - energy / t.Energy[(int)d.TankFluid]); // (its share goes with it)
        var ticks = t.DelayTicks + part * 7 % t.StaggerTicks;
        var s = new CShell
        {
            Kind = ShellKind.Incendiary, By = DamageBy != 255 ? DamageBy : c.Owner, Gun = c.Id, GunPart = ushort.MaxValue, Pos = at, Prev = at, Tested = at,
            Landed = true, Delay = Math.Max(1, ticks), Size = d.BoundsMax - d.BoundsMin, Blast = radius, Damage = damage, Crater = crater,
        };
        if (IsLive(c))
        {
            s.Stuck = c.Id;
            s.StuckAt = c.ToLocal(at);
        }
        FuelBlastsSet++;
        if (AddShell(s) != 0)
        {
            ShellBlastsWaiting++;
            return true;
        }
        if (CookOffCount < CookOffs.Length)
            CookOffs[CookOffCount++] = new CCookOff { At = at, Radius = radius, Damage = damage, Ticks = Math.Max(1, ticks), Body = c.Id };
        else
            Caps.Hit("cook-offs waiting", CookOffs.Length);
        return true;
    }
}
