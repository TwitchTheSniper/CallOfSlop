using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Big drive (docs/sandbox/CONSTRUCTS.md, "Big drive"): power plants, stacks and the pools at scale. A power plant
/// (role <c>generator</c>: a boiler, a furnace, a genset, a reactor) feeds the circuit it is on with no engine and no
/// seat: it needs its own links (fuel reaching it, a working part) and, wired, its run input. A <c>"smoke"</c> part puts
/// out what a burning plant makes while one reaches it: the plant itself, a stack touching it (through other stacks), or
/// a pipe from its exhaust port.
/// </summary>
public sealed partial class Construct
{
    public const int MaxPlants = 32;
    public readonly ushort[] Plants = new ushort[MaxPlants];
    public readonly float[] PlantLevel = new float[MaxPlants]; // 0..1: how much of its output it makes (it comes up over its spool)
    public readonly bool[] PlantFed = new bool[MaxPlants]; // on fuel: a tank reaches it
    public int PlantCount;
    public float PlantOut; // kW this tick
    public float SmokeLevel; // 0..1: how hard its stacks smoke (server; clients get it in the pose)
    public bool[] SmokeFed = []; // a part with a "smoke" block that a burning plant reaches (made when there is one)
    public bool HasSmoke;
    private double _fuelDebt, _chargeDebt;

    /// <summary>
    /// Burns <paramref name="litres"/>. A capital tank holds tens of millions of litres, where a float can't take a
    /// tick's sip off: what doesn't register is kept and goes the next time it does.
    /// </summary>
    public void UseFuel(float litres)
    {
        if (Fuel <= 0 && FluidFuel > 0)
        {
            ConstructFluid.Burn(this, litres); // (the pool is dry: the Fluid Tanks that feed)
            return;
        }
        _fuelDebt += litres;
        var now = Fuel - _fuelDebt;
        if (now <= 0)
        {
            Fuel = 0;
            _fuelDebt = 0;
            return;
        }
        Fuel = (float)now;
        _fuelDebt = Fuel - now;
    }

    /// <summary>Takes <paramref name="kj"/> off the batteries, or puts it in when negative (as <see cref="UseFuel"/>: a capital bank holds 10 TJ).</summary>
    public void UseCharge(float kj)
    {
        _chargeDebt += kj;
        var now = Charge - _chargeDebt;
        if (now <= 0 || now >= ChargeCapacity)
        {
            Charge = now <= 0 ? 0 : ChargeCapacity;
            _chargeDebt = 0;
            return;
        }
        Charge = (float)now;
        _chargeDebt = Charge - now;
    }

    /// <summary>With the caches: the plants and what each stack is fed by.</summary>
    private void BuildPlants()
    {
        Span<float> keep = stackalloc float[MaxPlants];
        Span<ushort> keepPart = stackalloc ushort[MaxPlants];
        var old = PlantCount;
        for (var k = 0; k < old; k++)
        {
            keep[k] = PlantLevel[k];
            keepPart[k] = Plants[k];
        }
        PlantCount = 0;
        HasSmoke = false;
        for (var i = 0; i < PartCount; i++)
        {
            var d = Parts[i].D;
            HasSmoke |= d.SmokeSize > 0;
            if (d.Role != PartRole.Generator || PlantCount >= MaxPlants)
                continue;
            var n = PlantCount++;
            Plants[n] = (ushort)i;
            PlantFed[n] = d.PlantFuel != EngineFuel.Fuel || Parts[i].Working && FuelReaches(i);
            PlantLevel[n] = 0;
            for (var k = 0; k < old; k++)
                if (keepPart[k] == i)
                    PlantLevel[n] = keep[k];
        }
        if (!HasSmoke)
            return;
        if (SmokeFed.Length < PartCount)
            SmokeFed = new bool[PartCount * 2];
        Array.Clear(SmokeFed);
        for (var i = 0; i < PartCount; i++)
            if (Parts[i].D.SmokeSize > 0 && Burner(i))
                SmokeFed[i] = true;
        for (var k = 0; k < RouteCount; k++)
        {
            ref var r = ref Routes[k];
            if (r.Type == RouteType.Pipe && r.Source < PartCount && r.To < PartCount && Parts[r.To].D.SmokeSize > 0 && Burner(r.Source))
                SmokeFed[r.To] = true;
        }
        // A stack touching a plant smokes, and a stack on that stack (a chimney in sections).
        for (var more = true; more;)
        {
            more = false;
            for (var k = 0; k < LinkCount; k++)
            {
                ref var l = ref Links[k];
                more |= Flue(l.A, l.B) | Flue(l.B, l.A);
            }
        }
    }

    private bool Burner(int i)
    {
        var d = Parts[i].D;
        return d.Role == PartRole.Engine && d.FuelKind == EngineFuel.Fuel || d.Role == PartRole.Generator && d.PlantFuel == EngineFuel.Fuel;
    }

    private bool Flue(int from, int to)
    {
        if (from >= PartCount || to >= PartCount || SmokeFed[to] || Parts[to].D.SmokeSize <= 0 || !Parts[to].Working)
            return false;
        if (!SmokeFed[from] && !Burner(from))
            return false;
        SmokeFed[to] = true;
        return true;
    }
}

/// <summary>The power plants each tick (server, in the power step; nothing allocates).</summary>
public static class ConstructPlant
{
    public const float BankedFire = 0.1f; // a plant a wire keeps on, with nothing to feed, holds this much of its output

    /// <summary>One tick: each plant follows what the circuit wants (a wire may hold it off, or keep its fire banked), burns for it, and what the stacks show.</summary>
    public static void Step(Construct c, float drawKw, float dt)
    {
        c.PlantOut = 0;
        var smoke = 0f;
        var need = drawKw + (c.ChargeCapacity - c.Charge) / dt;
        for (var k = 0; k < c.PlantCount; k++)
        {
            int part = c.Plants[k];
            if (part >= c.PartCount)
                continue;
            ref var p = ref c.Parts[part];
            var d = p.D;
            var fed = p.Working && c.PlantFed[k] && (d.PlantFuel != EngineFuel.Fuel || c.FuelLeft > 0);
            var wired = p.InCount > 0;
            var on = fed && (!wired || p.In >= 0.5f);
            var want = on ? Math.Clamp(need / d.PlantRate, 0, 1) : 0f;
            if (on && wired)
                want = MathF.Max(want, BankedFire);
            ref var level = ref c.PlantLevel[k];
            var rate = d.PlantSpool > 0 ? dt / d.PlantSpool : 1f;
            level = Math.Clamp(level + Math.Clamp(want - level, -2 * rate, rate), 0, 1);
            if (level <= 0)
                continue;
            var output = level * d.PlantRate;
            c.PlantOut += output;
            need -= output;
            if (d.PlantFuel == EngineFuel.Fuel)
            {
                c.UseFuel(d.PlantBurn * level * dt);
                smoke = MathF.Max(smoke, level);
            }
        }
        if (c.EngineRunning && c.Engine >= 0 && c.Engine < c.PartCount && c.DefOf(c.Engine) is { FuelKind: EngineFuel.Fuel } e)
            smoke = MathF.Max(smoke, 0.2f + 0.8f * Math.Clamp(c.DriveTorque / e.EngineTorque, 0, 1));
        c.SmokeLevel = smoke;
    }
}
