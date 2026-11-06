using System.Numerics;

namespace CallOfSlop;

/// <summary>A lift cell (a Fluid Tank whose box is an envelope), cached with the construct's parts.</summary>
public struct CCell
{
    public ushort Part;
    public byte Seg;
    public byte Mode; // what its lift does with height: 0 none, 1 a gas in a bag (it swells until the bag is full, then the lift falls), 2 by the air (hot air, vacuum)
    public Vector3 At; // the box's centre, its segment's frame: the lift acts here
    public float Volume, Face; // m3; the area it shows the wind, m2
    public float LiftKg; // what it lifts full, at sea level, with the build's soft scale
    public float Fill; // 0..1
    public float Sent; // the Fill clients were last told of
}

/// <summary>What a burner does, cached with the parts: which cell its flame reaches.</summary>
public struct CBurner
{
    public ushort Part;
    public short Cell; // an index into Cells, or -1: its flame reaches none
    public bool Fed; // a tank reaches it
    public float Level; // 0..1
}

public sealed partial class Construct
{
    public const int MaxCells = 32, MaxBurners = 16;
    public CCell[] Cells = [];
    public int CellCount;
    public CBurner[] Burners = [];
    public int BurnerCount;
    public ushort[] Compressors = []; // Gas Compressors
    public int CompressorCount;
    public float CellVolume, CellFront; // m3 in all; the area they show the wind, m2
    public float LiftNow; // N this tick (the hover hold takes it off what its thrusters must carry)
    public float LiftFarKg; // the summed path: kg lifted, as of the last gas step
    public Vector3 LiftFarAt; // ...and where, the construct's frame
    public float LiftSeen; // what it lifted when it last slept, kg: a change wakes it
    public Vector3 WindSeen; // the wind then
    public bool LiftNear; // a player is near: cells work one by one
    public float BurnTrim; // the driver's burner setting, 0..1 (Shift raises it, Ctrl lowers it; below zero Ctrl vents)
}

/// <summary>
/// Lift cells (docs/sandbox/CONSTRUCTS.md, "Air and lift"): a Tank Volume whose box is a solid envelope. Hydrogen and helium
/// come in by pipe, hot air from a burner, vacuum from the cell's own pump. A cell's lift acts at its own centre, so balance
/// and tilt are real. A hit makes a hole (<see cref="CfgKey.Hole"/>), gas leaves by the hole's size, an armoured cell's armour
/// stops small rounds, and the attach tool patches it. The gas moves four times a second; near a player every cell pushes
/// each tick, and further off one summed force does. A sleeping craft costs one comparison, and a change in what it lifts wakes it.
/// </summary>
public static partial class ConstructLift
{
    public const int Every = 15; // ticks between gas steps (4 Hz)
    private const float Least = 2e-4f; // a share of a cell that counts as nothing
    private const float SendStep = 0.002f; // a change of fill worth telling clients
    private const float CompressUnits = 8f; // bulk units a second a Gas Compressor takes

    /// <summary>The kg a cubic metre lifts at sea level, for a part holding this fluid; 0 when it doesn't lift.</summary>
    public static float LiftOf(ConstructPartDef d, Fluid? f, out byte mode)
    {
        var t = AirTable.Current;
        (var kg, mode) = f switch
        {
            Fluid.Hydrogen when !d.Envelope!.Vacuum => (t.LiftHydrogen, (byte)1),
            Fluid.Helium when !d.Envelope!.Vacuum => (t.LiftHelium, (byte)1),
            Fluid.HotAir when !d.Envelope!.Vacuum => (t.LiftHotAir, (byte)2),
            Fluid.Vacuum when d.Envelope!.Vacuum => (t.LiftVacuum, (byte)2),
            _ => (0f, (byte)0),
        };
        return kg;
    }

    /// <summary>How much of its full lift a cell gives in air this thick. A bag's gas swells as the air thins, so it holds its lift until the bag is full; past that, and for hot air and vacuum, the lift falls with the air.</summary>
    public static float Share(byte mode, float fill, float density) => mode == 1 ? MathF.Min(fill, density) : mode == 2 ? fill * density : 0;

    /// <summary>The kinder scale of big builds (Constructs/Air.json): the times over a construct of this many cubic metres counts.</summary>
    public static float SoftScale(float cubicMetres)
    {
        var t = AirTable.Current;
        return cubicMetres <= t.SoftFromM3 ? 1f : MathF.Min(t.SoftMost, MathF.Pow(cubicMetres / t.SoftFromM3, t.SoftPower));
    }

    /// <summary>The skin's weight (<see cref="Construct.LoadOf"/>), at the box's centre.</summary>
    public static bool Skin(Construct c, int part, out float kg, out Vector3 centre, out Vector3 half)
    {
        ref var p = ref c.Parts[part];
        ConstructCargo.Box(c, part, out centre, out half);
        var s = half * 2;
        kg = 2 * (s.X * s.Y + s.Y * s.Z + s.Z * s.X) * p.D.Envelope!.Skin;
        return kg > 0;
    }

    /// <summary>Rebuilt with the caches: the cells, the burners and what each burner's flame reaches.</summary>
    public static void Build(Construct c)
    {
        var cells = 0;
        var burners = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.Parts[i].D;
            if (d.IsEnvelope)
                cells++;
            else if (d.Role == PartRole.Burner)
                burners++;
        }
        Span<float> keepLevel = stackalloc float[Construct.MaxBurners];
        Span<ushort> keepPart = stackalloc ushort[Construct.MaxBurners];
        var had = Math.Min(c.BurnerCount, Construct.MaxBurners);
        for (var k = 0; k < had; k++)
        {
            keepLevel[k] = c.Burners[k].Level;
            keepPart[k] = c.Burners[k].Part;
        }
        cells = Math.Min(cells, Construct.MaxCells);
        burners = Math.Min(burners, Construct.MaxBurners);
        if (cells > c.Cells.Length)
            c.Cells = new CCell[cells];
        if (burners > c.Burners.Length)
            c.Burners = new CBurner[burners];
        c.CellCount = c.BurnerCount = 0;
        c.CellVolume = c.CellFront = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            var d = p.D;
            if (d.IsEnvelope && c.CellCount < cells)
            {
                p.SealedFaces = ConstructSeal.AllSealed; // (a cell is its own wall)
                if (p.Broken)
                    continue;
                ConstructCargo.Box(c, i, out var centre, out var half);
                var s = half * 2;
                c.Cells[c.CellCount++] = new CCell { Part = (ushort)i, Seg = p.Seg, At = centre, Volume = s.X * s.Y * s.Z, Face = (s.X * s.Y + s.Y * s.Z + s.Z * s.X) / 3f };
                c.CellVolume += s.X * s.Y * s.Z;
                c.CellFront += (s.X * s.Y + s.Y * s.Z + s.Z * s.X) / 3f;
            }
            else if (d.Role == PartRole.Burner && c.BurnerCount < burners)
            {
                ref var b = ref c.Burners[c.BurnerCount++];
                b = new CBurner { Part = (ushort)i, Cell = -1 };
                for (var k = 0; k < had; k++)
                    if (keepPart[k] == i)
                        b.Level = keepLevel[k];
            }
        }
        c.CompressorCount = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.GasLitres > 0)
            {
                if (c.CompressorCount == c.Compressors.Length)
                    Array.Resize(ref c.Compressors, c.CompressorCount + 4);
                c.Compressors[c.CompressorCount++] = (ushort)i;
            }
        for (var k = 0; k < c.BurnerCount; k++)
        {
            c.Burners[k].Cell = (short)FlameReaches(c, c.Burners[k].Part);
            c.Burners[k].Fed = c.ReachesFuel(c.Burners[k].Part);
        }
        Refresh(c);
    }

    /// <summary>The nearest cell up the burner's flame, or -1.</summary>
    private static int FlameReaches(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        var at = p.Pos + Rot24.Apply(p.Rot, p.D.FlameAt);
        var dir = Rot24.Apply(p.Rot, Vector3.UnitY);
        var best = -1;
        var near = p.D.FlameReach;
        for (var k = 0; k < c.CellCount; k++)
        {
            ConstructCargo.Box(c, c.Cells[k].Part, out var centre, out var half);
            if (Geometry.RayAabb(centre - half, centre + half, at, dir, near) is { } d && d < near)
            {
                near = d;
                best = k;
            }
        }
        return best;
    }

    /// <summary>Each cell's fill and lift as its part's settings stand. (Not the air: a blueprint is built before any world is loaded.)</summary>
    public static void Refresh(Construct c)
    {
        var soft = SoftScale(c.CellVolume);
        for (var k = 0; k < c.CellCount; k++)
        {
            ref var cell = ref c.Cells[k];
            if (cell.Part >= c.PartCount || !c.Parts[cell.Part].D.IsEnvelope)
                continue;
            ref var p = ref c.Parts[cell.Part];
            var d = p.D;
            var held = ConstructFluid.Held(p, d);
            cell.LiftKg = LiftOf(d, held, out cell.Mode) * cell.Volume * soft;
            cell.Fill = held == null ? 0 : Math.Clamp(ConstructConfig.Get(p, d, CfgKey.Fill, 0) / 100f, 0, 1);
        }
    }

    /// <summary>The sum for far or sleeping craft: what it lifts in the air it is in, and where.</summary>
    private static void Sum(Construct c)
    {
        var density = ConstructAir.Density(c.WorldCom);
        var sum = 0f;
        var at = Vector3.Zero;
        for (var k = 0; k < c.CellCount; k++)
        {
            ref var cell = ref c.Cells[k];
            var kg = cell.LiftKg * Share(cell.Mode, cell.Fill, density);
            sum += kg;
            at += cell.At * kg;
        }
        c.LiftFarKg = sum;
        c.LiftFarAt = sum > 0 ? at / sum : c.Com;
    }

    /// <summary>What the cells lift here and now (kg): the look line and the driver's HUD.</summary>
    public static float LiftKg(Construct c)
    {
        var density = ConstructAir.Density(c.WorldCom);
        var sum = 0f;
        for (var k = 0; k < c.CellCount; k++)
            sum += c.Cells[k].LiftKg * Share(c.Cells[k].Mode, c.Cells[k].Fill, density);
        return sum;
    }

    // ---- The gas, four times a second ---------------------------------------------------------------------------------

    /// <summary>Server: the gas in every cell, the burners, and whether a sleeping craft has to wake.</summary>
    public static void Step(ConstructWorld world, IWorldQuery query, ref int ticks)
    {
        if (++ticks < Every)
            return;
        ticks = 0;
        ConstructAir.ReadWeather();
        var dt = Every / (float)World.TickRate;
        var bodies = world.Bodies;
        var near = AirTable.Current.NearMetres;
        for (var n = 0; n < bodies.Count; n++)
        {
            var c = bodies[n];
            if (c.CellCount + c.CompressorCount == 0 || c.Dirty)
                continue;
            StepBody(world, query, c, near, dt);
        }
    }

    private static void StepBody(ConstructWorld world, IWorldQuery query, Construct c, float near, float dt)
    {
        var t = AirTable.Current;
        var air = ConstructAir.Density(c.WorldCom);
        var drive = c.Driver != 255 ? c.LiftInput : 0f;
        c.BurnTrim = Math.Clamp(c.BurnTrim + drive * 0.25f * dt, 0, 1);
        var wide = false;
        for (var k = 0; k < c.BurnerCount; k++)
            wide |= BurnerStep(c, query, ref c.Burners[k], air, dt);
        var venting = drive < 0 && c.BurnTrim <= 0;
        var changed = wide;
        for (var k = 0; k < c.CellCount; k++)
        {
            ref var cell = ref c.Cells[k];
            if (cell.Part >= c.PartCount || !c.Parts[cell.Part].D.IsEnvelope)
                continue;
            ref var p = ref c.Parts[cell.Part];
            var d = p.D;
            if (p.Broken)
                continue;
            var held = ConstructFluid.Held(p, d);
            var loss = 0f;
            var gain = 0f;
            var hole = ConstructConfig.Get(p, d, CfgKey.Hole, 0);
            if (held != null)
            {
                loss += hole * t.LeakSpeed / MathF.Max(1, cell.Volume);
                if (held == Fluid.HotAir)
                    loss += t.CoolShare * MathF.Max(air, 0.05f);
                var vent = p.InCount > 0 ? Math.Clamp(p.In, 0, 1) : venting ? 1f : 0f;
                loss += vent * (held == Fluid.HotAir ? t.VentHotShare : t.VentShare);
            }
            if (d.Envelope!.Vacuum && hole <= 0 && c.Electrical && ConstructPower.On(c, cell.Part))
                gain += t.PumpShare;
            if (p.Burn > 0 && held == Fluid.Hydrogen)
            {
                // It burns fast, and the fire goes through the skin in a few seconds: what is left goes up (ConstructWorld.CookOff).
                loss += t.BurnShare;
                p.Burn = (ushort)Math.Max((int)p.Burn, (int)(ConstructWeapons.BurnSeconds * World.TickRate * 0.6f));
                world.DamagePart(c, cell.Part, d.Health * t.BurnShare * dt, out _, raw: true);
                if (p.Broken || c.Dirty)
                    continue;
            }
            for (var b = 0; b < c.BurnerCount; b++)
                if (c.Burners[b].Cell == k && c.Burners[b].Level > 0 && held is null or Fluid.HotAir)
                    gain += c.Burners[b].Level * t.CoolShare * c.Parts[c.Burners[b].Part].D.BurnerHeat / MathF.Max(1, cell.Volume);
            if (gain > 0 && held == null)
                ConstructFluid.Fit(ref p, d, d.Envelope.Vacuum ? Fluid.Vacuum : Fluid.HotAir);
            if (gain > 0 || loss > 0)
                ConstructFluid.ChangeShare(ref p, d, (gain - loss * cell.Fill) * dt);
        }
        for (var k = 0; k < c.CompressorCount; k++)
            changed |= Compress(world, c, c.Compressors[k], dt);
        Refresh(c);
        Sum(c);
        for (var k = 0; k < c.CellCount; k++)
        {
            ref var cell = ref c.Cells[k];
            if (MathF.Abs(cell.Fill - cell.Sent) > SendStep || cell.Fill == 0 && cell.Sent != 0)
            {
                cell.Sent = cell.Fill;
                changed = true;
            }
        }
        if (changed)
            ConstructFluid.Note(c);
        // The cheap paths: is a player near enough for cell by cell, and has anything changed for a craft asleep?
        c.LiftNear = false;
        var reach = near + c.Radius;
        var com = c.WorldCom;
        for (var k = 0; k < world.PlayerSpotCount && !c.LiftNear; k++)
            c.LiftNear = Vector3.DistanceSquared(world.PlayerSpots[k], com) < reach * reach;
        var wind = ConstructAir.Wind(com);
        if (!c.Asleep)
        {
            c.LiftSeen = c.LiftFarKg;
            c.WindSeen = wind;
        }
        else if (MathF.Abs(c.LiftFarKg - c.LiftSeen) > 0.005f * MathF.Max(c.Mass, 1) || Vector3.DistanceSquared(wind, c.WindSeen) > 1)
            c.Wake();
    }

    /// <summary>A Gas Compressor: the hydrogen or helium in its bin goes down its pipes as gas, as fast as what they feed takes it.</summary>
    private static bool Compress(ConstructWorld world, Construct c, ushort part, float dt)
    {
        if (part >= c.PartCount || !c.Parts[part].Working || world.StoreOf(c, part) is not { Amount: > 0 } st)
            return false;
        var names = RecyclingTable.Current;
        Fluid gas;
        if (st.Material == names.IdOf("hydrogen"))
            gas = Fluid.Hydrogen;
        else if (st.Material == names.IdOf("helium"))
            gas = Fluid.Helium;
        else
            return false;
        var d = c.Parts[part].D;
        var moved = false;
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (r.Type != RouteType.Pipe || r.Source != part || !r.FedEnd || r.To >= c.PartCount)
                continue;
            var units = (int)MathF.Ceiling(MathF.Min(d.GasLitres * CompressUnits * dt, st.Amount * d.GasLitres) / d.GasLitres);
            var room = (int)(ConstructFluid.Room(c, r.To, gas) / d.GasLitres);
            units = Math.Min(units, room);
            if (units <= 0)
                continue;
            world.Withdraw(c, part, units);
            ConstructFluid.Give(c, r.To, gas, units * d.GasLitres);
            moved = true;
        }
        return moved;
    }

    /// <summary>One burner: what it is asked, a wire's throttle or height, else the driver's setting. True when it burned.</summary>
    private static bool BurnerStep(Construct c, IWorldQuery query, ref CBurner b, float air, float dt)
    {
        if (b.Part >= c.PartCount || c.Parts[b.Part].D.Role != PartRole.Burner)
            return false;
        ref var p = ref c.Parts[b.Part];
        var d = p.D;
        float want;
        if (p.InCount1 > 0)
        {
            // A height over the ground, wired: a slow trim, as the air in the bag takes a while to answer.
            var com = c.WorldCom;
            var ground = query.Raycast(com, -Vector3.UnitY, 4000f, QueryFilter.All, out var hit) ? hit.Point.Y : com.Y - 4000f;
            var miss = ground + p.In1 - com.Y;
            c.BurnTrim = Math.Clamp(c.BurnTrim + (0.02f * miss - 0.25f * c.Vel.Y) * dt, 0, 1);
            want = Math.Clamp(c.BurnTrim + 0.03f * miss - 0.4f * c.Vel.Y, 0, 1);
        }
        else
            want = p.InCount > 0 ? Math.Clamp(p.In, 0, 1) : c.BurnTrim;
        if (!p.Working || air <= 0 || b.Cell < 0 || !b.Fed || !(c.FuelLeft > 0))
            want = 0;
        b.Level += Math.Clamp(want - b.Level, -dt, dt);
        if (b.Level <= 0.001f)
        {
            b.Level = 0;
            return false;
        }
        c.UseFuel(d.BurnerRate * b.Level * dt);
        return true;
    }

    // ---- Damage ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// A hit got through a cell's armour: a hole by the damage. A vacuum cell collapses at the first one. Returns the share of
    /// the damage the cell's frame takes: a round goes through skin, it doesn't wreck the cell.
    /// </summary>
    public static float Holed(ConstructWorld world, Construct c, int part, float dealt)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var t = AirTable.Current;
        var hole = ConstructConfig.Get(p, d, CfgKey.Hole, 0) + MathF.Min(t.HoleMost, dealt * t.HolePerDamage);
        ConstructConfig.Put(ref p, d, CfgKey.Hole, MathF.Min(t.HoleCell, hole));
        ConstructFluid.Note(c);
        if (dealt >= t.IgniteDamage && ConstructFluid.Held(p, d) == Fluid.Hydrogen)
            world.Ignite(c, part); // (a shell or a blast, not a rifle round: burning rounds light it by their own route)
        return d.Envelope!.Vacuum ? 1e6f : 0.1f;
    }

    /// <summary>The attach tool's patch on a cell. Null when it worked, else why not.</summary>
    public static string? Patch(Construct c, int part)
    {
        if (part < 0 || part >= c.PartCount || !c.Parts[part].D.IsEnvelope)
            return "that isn't a lift cell";
        ref var p = ref c.Parts[part];
        var d = p.D;
        var hole = ConstructConfig.Get(p, d, CfgKey.Hole, 0);
        if (hole <= 0)
            return "it has no holes";
        ConstructConfig.Put(ref p, d, CfgKey.Hole, MathF.Max(0, hole - d.Envelope!.Patch));
        ConstructFluid.Note(c);
        return null;
    }

    /// <summary>What a burning hydrogen cell does when it gives way: its blast by the gas still in it.</summary>
    public static void Blast(in CPart p, ConstructPartDef d, float volume, out float radius, out float damage)
    {
        var gas = MathF.Cbrt(volume * Math.Clamp(ConstructConfig.Get(p, d, CfgKey.Fill, 0) / 100f, 0, 1));
        radius = MathF.Min(40f, 4f + 1.2f * gas);
        damage = MathF.Min(2500f, 200f + 60f * gas);
    }

    /// <summary>The look line's note on a cell, or null.</summary>
    public static string? Note(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        var hole = ConstructConfig.Get(p, p.D, CfgKey.Hole, 0);
        var lift = LiftKg(c);
        var text = !ConstructAir.HasAir ? "no air here: it lifts nothing"
            : $"lifts {Kg(lift)} of {Kg(c.Mass)}{(lift < c.Mass ? $", {Kg(FullKg(c))} when full" : "")}{(c.Anchored ? ", frozen: it can't rise" : "")}";
        return hole > 0 ? $"HOLED, {hole:0.##} m2 leaking - {text}" : text;
    }

    private static string Kg(float kg) => kg < 10_000 ? $"{kg:0} kg" : $"{kg / 1000f:0.#} t";

    /// <summary>What the construct's cells would lift full of what each holds, helium if empty, in the air here.</summary>
    private static float FullKg(Construct c)
    {
        var soft = SoftScale(c.CellVolume);
        var sum = 0f;
        var density = ConstructAir.Density(c.WorldCom);
        for (var k = 0; k < c.CellCount; k++)
        {
            ref var cell = ref c.Cells[k];
            sum += (cell.LiftKg > 0 ? cell.LiftKg : AirTable.Current.LiftHelium * cell.Volume * soft) * (cell.Mode == 2 ? density : MathF.Min(1, density));
        }
        return sum;
    }

    public static long LookKey(Construct c, int part) => (long)(ConstructConfig.Get(c.Parts[part], c.Parts[part].D, CfgKey.Hole, 0) * 100) ^ (long)(LiftKg(c) / 10f) << 12 ^ (long)(c.Mass / 10f) << 34 ^ (c.Anchored ? 1L << 11 : 0);
}

public static partial class ConstructPhysics
{
    /// <summary>
    /// One tick of a free, awake craft's cells: lift at each cell's centre near a player, the summed force further off; then
    /// the air's drag on the envelopes (with the wind) and its hold on their turning.
    /// </summary>
    private static void Lift(Construct c, in Matrix4x4 invI, Vector3 weight, float dt)
    {
        var g = weight.Length();
        var com = c.WorldCom;
        var density = ConstructAir.Density(com);
        c.LiftNow = 0;
        if (density <= 0 || g < 1e-3f || c.Mass <= 0)
            return;
        var up = -weight / g;
        var t = AirTable.Current;
        var wind = ConstructAir.Wind(com);
        var windSpeed = wind.Length();
        var force = 0f;
        Vector3 centre;
        if (c.LiftNear)
        {
            var mid = Vector3.Zero;
            for (var k = 0; k < c.CellCount; k++)
            {
                ref var cell = ref c.Cells[k];
                if (cell.LiftKg <= 0)
                    continue;
                c.WorldFrame(cell.Seg, out var q, out var o);
                var at = o + Vector3.Transform(cell.At, q);
                var f = cell.LiftKg * ConstructLift.Share(cell.Mode, cell.Fill, ConstructAir.Density(at)) * g;
                force += f;
                mid += at * f;
                var gust = windSpeed > 0 ? ConstructAir.Turbulence(at, _warmTick, windSpeed) * (f / g) : Vector3.Zero; // (a share of the craft by what the cell carries)
                ApplyImpulse(c, invI, at - com, (up * f + gust) * dt);
            }
            centre = force > 0 ? mid / force : com;
        }
        else
        {
            centre = c.Pos + Vector3.Transform(c.LiftFarAt, c.Rot);
            var kg = 0f;
            for (var k = 0; k < c.CellCount; k++)
                kg += c.Cells[k].LiftKg * ConstructLift.Share(c.Cells[k].Mode, c.Cells[k].Fill, density);
            force = kg * g;
            if (force > 0)
                ApplyImpulse(c, invI, centre - com, up * (force * dt));
        }
        c.LiftNow = force;
        // Drag: 1/2 rho Cd A v2, the wind's share with it. It can only bring the craft to the air's speed.
        var rel = c.Vel - wind;
        var speed = rel.Length();
        if (speed > 0.01f && c.CellFront > 0)
        {
            var dv = MathF.Min(speed, 0.6125f * density * t.Drag * c.CellFront * speed * speed / c.Mass * dt);
            ApplyImpulse(c, invI, (force > 0 ? centre - com : Vector3.Zero) * t.DragArm, -rel / speed * (dv * c.Mass)); // (the arm stands in for fins until flight surfaces come: all of it would tip any ship over)
        }
        c.AngVel *= 1 - MathF.Min(1, t.SpinDrag * density * dt);
    }
}
