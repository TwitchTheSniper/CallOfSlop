using System.Globalization;

namespace CallOfSlop;

/// <summary>
/// A construct's Fluid Tanks (docs/sandbox/CONSTRUCTS.md, "Fluid tanks"), rebuilt with the caches. Each tank holds its own
/// contents in its settings (<see cref="CfgKey.Fluid"/>, <see cref="CfgKey.Fill"/>), apart from <see cref="Fuel"/>, the pool
/// all of a construct's fuel tanks share.
/// </summary>
public sealed partial class Construct
{
    public ushort[] FluidTanks = [];
    public bool[] FluidFeeds = []; // a tank a fuel burner can draw on: it touches one, or a pipe runs to a fuel inlet
    public int FluidTankCount;
    public float FluidFuel, FluidFuelCap; // the litres of fuel in the tanks that feed, and what they hold at most
    public bool FluidStale; // server: a tank changed since the clients were last told

    /// <summary>The fuel its burners can reach: the pool, then the Fluid Tanks that feed them.</summary>
    public float FuelLeft => Fuel + FluidFuel;
}

/// <summary>
/// The Fluid Tank: one fluid at a time, kept on the part as two settings so it moves with the part and rides in blueprints,
/// world saves and joiners' CBody like any setting. The fill is a percentage of the tank; a tank with no fluid chosen or at
/// 0% is empty, and an empty tank takes whatever arrives first. A pipe runs from an output to an input, so it knows its
/// direction from how it was laid. Nothing here allocates per tick; a construct with no Fluid Tank costs nothing.
/// </summary>
public static class ConstructFluid
{
    /// <summary>The Fluid setting's options: none, then every <see cref="Fluid"/> in order.</summary>
    public static readonly string[] Options = MakeOptions();

    public const int SendEvery = 12; // ticks between the clients' updates of the tanks that changed (5 Hz)
    public const int MaxEntries = 150; // tanks a packet carries (it stays under the MTU)
    private const float Least = 1e-4f; // litres below which a tank is empty

    private static string[] MakeOptions()
    {
        var names = Enum.GetNames<Fluid>();
        var list = new string[names.Length + 1];
        list[0] = "none";
        for (var i = 0; i < names.Length; i++)
            list[i + 1] = names[i].ToLowerInvariant();
        return list;
    }

    public static string Name(Fluid f) => Options[(int)f + 1];

    // ---- One tank ---------------------------------------------------------------------------------------------------

    private static int FluidSlot(ConstructPartDef d) => d.CfgSlot[(int)CfgKey.Fluid];

    private static int FillSlot(ConstructPartDef d) => d.CfgSlot[(int)CfgKey.Fill];

    /// <summary>What a Fluid Tank holds, or null when it is empty.</summary>
    public static Fluid? Held(in CPart p, ConstructPartDef d)
    {
        var f = p.Cfg[FluidSlot(d)];
        return f >= 1 && p.Cfg[FillSlot(d)] > 0 ? (Fluid)((int)f - 1) : null;
    }

    /// <summary>The litres in a Fluid Tank.</summary>
    public static float Litres(in CPart p, ConstructPartDef d) => Held(p, d) == null ? 0 : p.Cfg[FillSlot(d)] / 100f * d.FuelCapacity;

    /// <summary>
    /// Adds <paramref name="litres"/> (taken off when negative). The percentage is a float, so what a tick's sip can't
    /// register on a big tank is kept and goes the next time it does (as <see cref="Construct.UseFuel"/>).
    /// </summary>
    private static void Change(ref CPart p, ConstructPartDef d, double litres)
    {
        var slot = FillSlot(d);
        var before = (double)p.Cfg[slot];
        var want = litres + p.FluidOwed;
        var now = Math.Clamp(before + want * 100.0 / d.FuelCapacity, 0, 100);
        p.Cfg[slot] = (float)now;
        p.FluidOwed = now is <= 0 or >= 100 ? 0 : (float)(want - (p.Cfg[slot] - before) / 100.0 * d.FuelCapacity);
        if (p.Cfg[slot] / 100f * d.FuelCapacity < Least)
            Clear(ref p, d);
    }

    private static void Clear(ref CPart p, ConstructPartDef d)
    {
        p.Cfg[FluidSlot(d)] = 0;
        p.Cfg[FillSlot(d)] = 0;
        p.FluidOwed = 0;
    }

    /// <summary>A tank wrecked: what it held is lost, as a fuel tank's share of the pool is.</summary>
    public static void Lost(Construct c, int part)
    {
        Clear(ref c.Parts[part], c.DefOf(part));
        Changed(c);
    }

    /// <summary>Every Fluid Tank on a construct made new (a survival paste): a contents setting comes only from creative.</summary>
    public static void EmptyAll(Construct c)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.Role == PartRole.FluidTank)
                Clear(ref c.Parts[i], c.Parts[i].D);
    }

    /// <summary>The fluid and litres a part can give down a pipe: a working Fluid Tank's, or a working fuel tank's pool.</summary>
    public static bool Holds(Construct c, int part, out Fluid fluid, out float litres)
    {
        fluid = default;
        litres = 0;
        if (part < 0 || part >= c.PartCount || !c.Parts[part].Working)
            return false;
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (d.Role == PartRole.FuelTank)
        {
            fluid = d.TankFluid;
            litres = c.Fuel;
        }
        else if (d.Role == PartRole.FluidTank && Held(p, d) is { } held)
        {
            fluid = held;
            litres = Litres(p, d);
        }
        return litres > Least;
    }

    /// <summary>The room for <paramref name="fluid"/> in a working tank: none for another fluid than it holds.</summary>
    private static float Room(Construct c, int part, Fluid fluid)
    {
        if (part < 0 || part >= c.PartCount || !c.Parts[part].Working)
            return 0;
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (d.Role == PartRole.FuelTank)
            return d.TankFluid == fluid ? c.FuelCapacity() - c.Fuel : 0;
        if (d.Role != PartRole.FluidTank)
            return 0;
        return Held(p, d) is not { } held ? d.FuelCapacity : held == fluid ? d.FuelCapacity - Litres(p, d) : 0;
    }

    /// <summary>Moves up to <paramref name="litres"/> from one tank to another, on one construct or two. Returns how much moved.</summary>
    public static float Move(Construct cf, int from, Construct ct, int to, float litres)
    {
        if (cf == ct && from == to || !Holds(cf, from, out var fluid, out var have))
            return 0;
        var moved = MathF.Min(litres, MathF.Min(have, Room(ct, to, fluid)));
        if (moved <= 0)
            return 0;
        Take(cf, from, moved);
        ref var q = ref ct.Parts[to];
        var d = q.D;
        if (d.Role == PartRole.FuelTank)
            ct.Fuel += moved;
        else
        {
            if (Held(q, d) == null)
            {
                q.Cfg[FillSlot(d)] = 0; // (a percentage set with no fluid chosen held nothing)
                q.FluidOwed = 0;
                q.Cfg[FluidSlot(d)] = (int)fluid + 1;
            }
            Change(ref q, d, moved);
        }
        Changed(cf);
        Changed(ct);
        return moved;
    }

    private static void Take(Construct c, int part, float litres)
    {
        ref var p = ref c.Parts[part];
        if (p.D.Role == PartRole.FuelTank)
            c.Fuel = MathF.Max(0, c.Fuel - litres);
        else
            Change(ref p, p.D, -(double)litres);
    }

    private static void Changed(Construct c)
    {
        if (c.FluidTankCount > 0)
        {
            c.FluidStale = true;
            Refresh(c);
        }
    }

    /// <summary>What goes through a pipe from one part kind to another, litres a second: the narrower tank's, or the one tank's.</summary>
    public static float Flow(ConstructPartDef from, ConstructPartDef to) =>
        from.TankFlow > 0 && to.TankFlow > 0 ? MathF.Min(from.TankFlow, to.TankFlow) : MathF.Max(MathF.Max(from.TankFlow, to.TankFlow), ConstructWorld.PipeFlow);

    /// <summary>
    /// One tick of a pipe with a Fluid Tank at either end: fluid from the output end to the input end, at the share of the
    /// flow a junction leaves it. A cut pipe pours a Fluid Tank's contents out, whatever they are. False: no Fluid Tank here.
    /// </summary>
    public static bool StepPipe(Construct c, int k, float dt)
    {
        ref var r = ref c.Routes[k];
        if (r.Source >= c.PartCount)
            return false;
        var sd = c.DefOf(r.Source);
        var td = r.To < c.PartCount ? c.DefOf(r.To) : null;
        var fromTank = sd.Role == PartRole.FluidTank;
        if (!fromTank && td?.Role != PartRole.FluidTank)
            return false;
        if (r.FirstCut <= r.Count)
        {
            if (!fromTank)
                return false; // (the pool's leak)
            if (r.Fed && Holds(c, r.Source, out _, out var have))
            {
                Take(c, r.Source, MathF.Min(have, ConstructRoutes.LeakLitres * dt));
                Changed(c);
            }
            return true;
        }
        if (td != null && r.FedEnd)
            Move(c, r.Source, c, r.To, Flow(sd, td) * r.Share * dt);
        return true;
    }

    /// <summary>A hose or anything else with no part at its ends: from the first tank that has something to the first that has room for it.</summary>
    public static float MoveAny(Construct cf, Construct ct, float litres)
    {
        if (cf == ct || cf.FluidTankCount == 0 && ct.FluidTankCount == 0)
            return 0;
        var poolFrom = PoolTank(cf);
        var poolTo = PoolTank(ct);
        for (var a = -1; a < cf.FluidTankCount; a++)
        {
            var from = a < 0 ? poolFrom : cf.FluidTanks[a];
            if (!Holds(cf, from, out var fluid, out _))
                continue;
            for (var b = -1; b < ct.FluidTankCount; b++)
            {
                var to = b < 0 ? poolTo : ct.FluidTanks[b];
                if (a < 0 && b < 0 || Room(ct, to, fluid) <= 0)
                    continue; // (pool to pool is the hose's own)
                return Move(cf, from, ct, to, litres);
            }
        }
        return 0;
    }

    private static int PoolTank(Construct c)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.Role == PartRole.FuelTank && c.Parts[i].Working)
                return i;
        return -1;
    }

    // ---- The construct ------------------------------------------------------------------------------------------------

    /// <summary>Rebuilt with the caches, after the links and the drive line: which parts are Fluid Tanks, and which of them feed a burner.</summary>
    public static void Build(Construct c)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.Role == PartRole.FluidTank)
                n++;
        if (n > c.FluidTanks.Length)
        {
            c.FluidTanks = new ushort[n + 4];
            c.FluidFeeds = new bool[n + 4];
        }
        c.FluidTankCount = 0;
        if (n > 0)
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].D.Role == PartRole.FluidTank)
                {
                    c.FluidTanks[c.FluidTankCount] = (ushort)i;
                    c.FluidFeeds[c.FluidTankCount++] = Feeds(c, i);
                }
        Refresh(c);
    }

    private static bool Feeds(Construct c, int tank)
    {
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (r.Type == RouteType.Pipe && r.Source == tank && r.FedEnd && r.To < c.PartCount && Burns(c, r.To)
                && ConstructRoutes.PortOf(c.DefOf(r.To), RouteType.Pipe, r.ToPort) is { NoEffect: false, Fluid: "" or "fuel" })
                return true;
        }
        // Touching one, as Construct.FuelReaches sees it: a socket link, a freeform seam's sockets, its bead.
        for (var k = 0; k < c.LinkCount + c.SeamCount + c.JointCount; k++)
        {
            int a, b;
            if (k < c.LinkCount)
                (a, b) = (c.Links[k].A, c.Links[k].B);
            else if (k < c.LinkCount + c.SeamCount)
                (a, b) = (c.SeamLinks[k - c.LinkCount].A, c.SeamLinks[k - c.LinkCount].B);
            else if (c.Joints[k - c.LinkCount - c.SeamCount] is { Freeform: true, Cut: false } fj)
                (a, b) = (fj.Hinge, fj.Child);
            else
                continue;
            var other = a == tank ? b : b == tank ? a : -1;
            if (other >= 0 && other < c.PartCount && (Burns(c, other) || c.DefOf(other).Role == PartRole.FuelLine))
                return true;
        }
        return false;
    }

    /// <summary>Does a part burn fuel: an engine or any piece of a modular one, a power plant, a thruster?</summary>
    private static bool Burns(Construct c, int part)
    {
        var d = c.DefOf(part);
        return d.Role == PartRole.Engine && d.FuelKind == EngineFuel.Fuel || d.Role == PartRole.Generator && d.PlantFuel == EngineFuel.Fuel
            || d.Role == PartRole.Thruster && d.ThrustFuel == EngineFuel.Fuel || part < c.EngineOf.Length && c.EngineOf[part] != 0;
    }

    /// <summary>Is the tank in this entry of the list one? Parts may have come off since the last rebuild, and the indices move with them.</summary>
    public static bool Current(Construct c, int k) => c.FluidTanks[k] < c.PartCount && c.DefOf(c.FluidTanks[k]).Role == PartRole.FluidTank;

    private static bool Feeding(Construct c, int k)
    {
        if (!Current(c, k) || !c.FluidFeeds[k])
            return false;
        ref var p = ref c.Parts[c.FluidTanks[k]];
        return p.Working && Held(p, p.D) == Fluid.Fuel;
    }

    /// <summary>The fuel the feeding tanks hold, again (a tank's contents changed).</summary>
    public static void Refresh(Construct c)
    {
        float fuel = 0, cap = 0;
        for (var k = 0; k < c.FluidTankCount; k++)
        {
            if (!Feeding(c, k))
                continue;
            ref var p = ref c.Parts[c.FluidTanks[k]];
            fuel += Litres(p, p.D);
            cap += p.D.FuelCapacity;
        }
        c.FluidFuel = fuel;
        c.FluidFuelCap = cap;
    }

    /// <summary>A burner's sip once the pool is dry: off the feeding tanks that hold fuel.</summary>
    public static void Burn(Construct c, float litres)
    {
        for (var k = 0; k < c.FluidTankCount && litres > 0; k++)
        {
            if (!Feeding(c, k))
                continue;
            ref var p = ref c.Parts[c.FluidTanks[k]];
            var sip = MathF.Min(litres, Litres(p, p.D));
            Change(ref p, p.D, -(double)sip);
            litres -= sip;
        }
        Changed(c);
    }

    /// <summary>Client: a tank's contents from the server.</summary>
    public static void Apply(Construct c, int part, int fluid, float fill)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        p.Cfg[FluidSlot(d)] = d.Config[FluidSlot(d)].Clamp(fluid);
        p.Cfg[FillSlot(d)] = d.Config[FillSlot(d)].Clamp(fill);
    }

    /// <summary>
    /// Does a pipe at this tank move nothing because the fluids don't match? <paramref name="other"/> is the fluid refused and
    /// <paramref name="mine"/> what is there instead; <paramref name="burner"/> is the part at the far end when the tank's own
    /// fluid is what is refused, else -1.
    /// </summary>
    private static bool Mismatch(Construct c, int part, out Fluid other, out Fluid mine, out int burner)
    {
        other = mine = default;
        burner = -1;
        ref var p = ref c.Parts[part];
        if (!p.Working)
            return false;
        var d = p.D;
        var held = Held(p, d);
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (r.Type != RouteType.Pipe || !r.FedEnd || r.To >= c.PartCount)
                continue;
            if (r.To == part && held is { } h && Holds(c, r.Source, out var comes, out _) && comes != h)
            {
                (other, mine) = (comes, h);
                return true;
            }
            if (r.Source != part || held is not { } goes)
                continue;
            ref var q = ref c.Parts[r.To];
            if (q.D.Role == PartRole.FluidTank && q.Working && Held(q, q.D) is { } there && there != goes)
            {
                (other, mine, burner) = (goes, there, r.To);
                return true;
            }
            if (q.D.Role == PartRole.FuelTank && q.D.TankFluid != goes)
            {
                (other, mine, burner) = (goes, q.D.TankFluid, r.To);
                return true;
            }
            if (goes != Fluid.Fuel && Burns(c, r.To) && ConstructRoutes.PortOf(q.D, RouteType.Pipe, r.ToPort) is { NoEffect: false, Fluid: "" or "fuel" })
            {
                (other, mine, burner) = (goes, Fluid.Fuel, r.To);
                return true;
            }
        }
        return false;
    }

    // ---- The look line ------------------------------------------------------------------------------------------------

    /// <summary>"60 of 150 l water" or "empty".</summary>
    public static string State(in CPart p, ConstructPartDef d)
    {
        if (p.Broken)
            return "wrecked";
        if (Held(p, d) is not { } f)
            return $"empty, holds {Amount(d.FuelCapacity)} l";
        return $"{Amount(Litres(p, d))} of {Amount(d.FuelCapacity)} l {Name(f)}";
    }

    private static string Amount(float litres) => litres.ToString(litres < 1000 ? "0.#" : "0", CultureInfo.InvariantCulture);

    /// <summary>The look line's problem: a pipe's fluid this tank refuses, or that what it holds is refused at the far end; else null.</summary>
    public static string? Problem(Construct c, int part)
    {
        if (!Mismatch(c, part, out var other, out var mine, out var far))
            return null;
        return far < 0 ? $"refused: a pipe brings {Name(other)}, this holds {Name(mine)}"
            : c.DefOf(far).Role is PartRole.FluidTank or PartRole.FuelTank ? $"refused: the {c.DefOf(far).Name} holds {Name(mine)}"
            : $"refused: the {c.DefOf(far).Name} burns {Name(mine)}";
    }

    /// <summary>What the look line depends on, so it is made again only when that changes.</summary>
    public static long LookKey(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var mixed = Mismatch(c, part, out var other, out _, out var far);
        return (long)(Litres(p, d) / d.FuelCapacity * 1000) | (long)(Held(p, d) is { } f ? (int)f + 1 : 0) << 11 | (mixed ? 1L + (int)other + (far + 1) * 16L : 0) << 15;
    }

    /// <summary>What a pipe leaving a Fluid Tank carries now ("" empty; any other part: <paramref name="port"/>).</summary>
    public static string Carried(Construct c, int part, string port)
    {
        ref var p = ref c.Parts[part];
        return p.D.Role != PartRole.FluidTank ? port : Held(p, p.D) is { } f ? Name(f) : "";
    }
}
