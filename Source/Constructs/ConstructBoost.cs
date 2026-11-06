using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What makes a thruster's push (<c>thruster.kind</c>; a saved part's data, so add at the end).</summary>
public enum ThrustKind : byte
{
    Plain, // a force for fuel or power, anywhere: the old jets and lift fans
    Air, // breathes: thrust falls with the air, and stops without it
    Rocket, // burns fuel and oxidiser from its tanks, anywhere
    Solid, // a booster: lit once, burns its grain, can't be throttled or stopped
    Repulsor, // pushes off what is under it, hard near it and fading fast
}

public sealed partial class Construct
{
    public bool HasBoost; // an air-breathing jet: the air is read each tick
    public int RepulsorCount;
    public float RepulsorGap, RepulsorShape; // the mean gap under its repulsors, m, and the share of full push it leaves them
    public int AntiCount; // Anti-Grav generators
    public float AntiShare, AntiNext; // the share of its weight cancelled this tick, and the sum the logic step is making
    public float OxLeft; // litres of oxidiser its rockets can reach

    /// <summary>Takes the stores a piece that came away with its tanks holds (a stage).</summary>
    public void SetStores(float fuel, float charge)
    {
        Fuel = fuel;
        Charge = charge;
        _fuelSet = _chargeSet = true;
    }
}

public sealed partial class ConstructWorld
{
    public readonly List<(ushort Body, ushort Part)> Decouples = new(16);

    /// <summary>
    /// A decoupler fires: it goes, and each side of it stays a working construct, with what its tanks hold, its links and wires, and any seat. The side with the drive seat
    /// keeps the body, so its pilot never notices the join going. Links across the join are cut with the part. The sides are parted at the decoupler's kick, by their masses.
    /// </summary>
    public void Decouple(Construct c, int index)
    {
        if (c.Dirty)
            c.Rebuild();
        if (index >= c.PartCount)
            return;
        var kick = c.Parts[index].D.DecoupleKick;
        c.WorldFrame(c.Parts[index].Seg, out var fq, out var ft);
        var at = ft + Vector3.Transform(c.Parts[index].Pos, fq);
        Span<int> comp = stackalloc int[c.PartCount];
        for (var i = 0; i < c.PartCount; i++)
            comp[i] = -1;
        comp[index] = -2;
        var root = c.DriveSeat >= 0 && c.DriveSeat < c.PartCount && c.DriveSeat != index ? c.DriveSeat : -1;
        for (var i = 0; i < c.PartCount && root < 0; i++)
            if (i != index && c.Parts[i].D.Role is PartRole.Seat or PartRole.Foundation)
                root = i;
        if (root < 0 && c.Root != index && c.Root < c.PartCount)
            root = c.Root;
        for (var i = 0; i < c.PartCount && root < 0; i++)
            if (i != index)
                root = i;
        if (root < 0)
            return;
        var comps = 0;
        Flood(c, root, comps++, comp);
        for (var i = 0; i < c.PartCount; i++)
            if (comp[i] == -1)
                Flood(c, i, comps++, comp);
        Span<ushort> made = stackalloc ushort[Math.Min(comps, 8)];
        var n = 0;
        for (var k = 1; k < comps; k++)
            if (SplitOff(c, comp, k, live: true) is { } piece && n < made.Length)
                made[n++] = piece.Id;
        Compact(c, comp, 0);
        c.Dirty = true;
        c.Rebuild();
        c.Wake();
        StructureDirty.Add(c.Id);
        for (var k = 0; k < n; k++)
        {
            if (Get(made[k]) is not { } piece)
                continue;
            StructureDirty.Add(piece.Id);
            var away = piece.WorldCom - at;
            away = away.LengthSquared() > 1e-4f ? Vector3.Normalize(away) : Vector3.UnitY;
            var all = piece.Mass + c.Mass;
            piece.Vel += away * (kick * c.Mass / all);
            c.Vel -= away * (kick * piece.Mass / all);
        }
        Events.Add($"construct {c.Id}: a decoupler fired, {n} stage(s) away");
    }
}

/// <summary>
/// Jets, rockets, boosters, gimbals, repulsors and Anti-Grav (docs/sandbox/CONSTRUCTS.md, "Jets, rockets and repulsors"). All but Anti-Grav and the decoupler are thrusters: a
/// <c>kind</c> and a few numbers in the part file, worked here beside <see cref="ConstructPhysics"/>'s thruster tick. Nothing here allocates in a steady tick; a decoupler firing does.
/// </summary>
public static class ConstructBoost
{
    private const float MinAir = 0.03f; // of sea level: below this a jet flames out
    private const float RamSpeed = 500f, RamMost = 0.3f; // m/s, and the share more it gets from going fast
    private const float AbSpool = 0.6f; // seconds an afterburner takes to light
    private const float MostAnti = 1f;
    public const int LookEvery = 3; // ticks between a repulsor's looks at the ground

    /// <summary>A thruster block's own keys.</summary>
    public static void Parse(ConstructPartDef d, JsonElement b, string file)
    {
        var bw = $"{file}, thruster";
        if (ConstructParts.Opt(b, "afterburner") is { } ab)
        {
            ConstructParts.Known(ab, ["force", "burn", "band"], bw + ".afterburner");
            d.AbForce = ConstructParts.Num(ConstructParts.Need(ab, "force", bw), file, "afterburner.force");
            d.AbBurn = ConstructParts.Num(ConstructParts.Need(ab, "burn", bw), file, "afterburner.burn");
            d.AbBand = ConstructParts.Opt(ab, "band") is { } band ? ConstructParts.Num(band, file, "afterburner.band") : 0.9f;
            if (d.ThrustKind != ThrustKind.Air)
                throw new ConstructPartException($"{file}: only an air-breathing jet has an afterburner");
            d.DataIn = ["throttle", "height", "afterburner"];
        }
        if (ConstructParts.Opt(b, "gimbal") is { } gim)
        {
            d.ThrustGimbal = ConstructParts.Num(gim, file, "thruster.gimbal") * MathF.PI / 180f;
            d.GimbalRate = (ConstructParts.Opt(b, "gimbalRate") is { } rate ? ConstructParts.Num(rate, file, "thruster.gimbalRate") : 60f) * MathF.PI / 180f;
            if (d.ThrustKind is not (ThrustKind.Air or ThrustKind.Rocket) || d.AbForce > 0)
                throw new ConstructPartException($"{file}: a gimbal is on a jet with no afterburner, or a rocket");
            d.DataIn = ["throttle", "gimbal pitch", "gimbal yaw"];
        }
        if (ConstructParts.Opt(b, "oxidiser") is { } ox)
        {
            d.ThrustOx = ConstructParts.Num(ox, file, "thruster.oxidiser");
            if (d.ThrustKind != ThrustKind.Rocket || d.ThrustOx <= 0)
                throw new ConstructPartException($"{file}: \"oxidiser\" is a rocket's l/s");
        }
        if (d.ThrustKind == ThrustKind.Rocket && d.ThrustOx <= 0)
            throw new ConstructPartException($"{file}: a rocket has an \"oxidiser\" rate");
        if (d.ThrustKind == ThrustKind.Solid)
            d.DataIn = ["ignite"];
        if (d.ThrustKind == ThrustKind.Repulsor)
        {
            d.RepulsorRange = ConstructParts.Num(ConstructParts.Need(b, "range", bw), file, "thruster.range");
            if (d.ThrustFuel != EngineFuel.Electric)
                throw new ConstructPartException($"{file}: a repulsor runs on power");
        }
    }

    /// <summary>With the caches, after the thrusters and tanks.</summary>
    public static void Build(Construct c)
    {
        c.HasBoost = false;
        c.RepulsorCount = 0;
        c.AntiCount = 0;
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            var kind = c.Parts[c.Thrusters[k].Part].D.ThrustKind;
            c.HasBoost |= kind == ThrustKind.Air;
            c.RepulsorCount += kind == ThrustKind.Repulsor ? 1 : 0;
        }
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.Logic == CLogic.AntiGrav)
                c.AntiCount++;
        if (c.AntiCount == 0)
        {
            c.AntiShare = c.AntiNext = 0;
            return;
        }
        // What each generator's field holds: a share of the mass, in its radius. Big ships get a kinder radius, as lift gets a kinder scale.
        var size = c.LocalMax - c.LocalMin;
        var soft = ConstructLift.SoftScale(MathF.Max(0, size.X * size.Y * size.Z));
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var g = ref c.Parts[i];
            if (g.D.Logic != CLogic.AntiGrav)
                continue;
            var reach = g.D.AntiRadius * soft;
            var held = 0f;
            var all = 0f;
            for (var k = 0; k < c.PartCount; k++)
            {
                var mass = c.Parts[k].D.Mass;
                all += mass;
                if (Vector3.DistanceSquared(c.Parts[k].Pos, g.Pos) <= reach * reach)
                    held += mass;
            }
            g.Mem = all > 0 ? held / all : 0;
        }
    }

    /// <summary>The logic step's sum is this tick's share. A change wakes it: power gone, it falls at once.</summary>
    public static void TakeAnti(Construct c)
    {
        var now = MathF.Min(MostAnti, c.AntiNext);
        if (now != c.AntiShare)
            c.Wake();
        c.AntiShare = now;
        c.AntiNext = 0;
    }

    /// <summary>What the air gives an air-breathing jet here: by its density, with a little for going fast; none in the thin and in vacuum.</summary>
    public static float AirShare(Construct c, Vector3 at)
    {
        var rho = ConstructAir.Density(at);
        return rho < MinAir ? 0 : rho * (1 + RamMost * MathF.Min(1, c.Vel.Length() / RamSpeed));
    }

    /// <summary>A thruster's force as of now, flat out, no afterburner (a stabilizer's mix).</summary>
    public static float ForceNow(Construct c, ConstructPartDef d) => d.ThrustKind == ThrustKind.Air ? d.ThrustForce * AirShare(c, c.WorldCom) : d.ThrustForce;

    /// <summary>How much of its push a repulsor has at this gap: most at the ground, a tenth by half its range, none at its range.</summary>
    public static float Falloff(float gap, float range) => gap >= range ? 0 : MathF.Exp(-4.6f * gap / range) * (1 - gap / range);

    /// <summary>A solid booster: lit by its input going high (unwired: the driver's Shift); a tick of its grain. 1 while it burns.</summary>
    public static float Grain(Construct c, ref CPart p, ConstructPartDef d, float dt)
    {
        if ((p.State & 1) == 0)
        {
            if (p.InCount > 0 ? p.In < 0.5f : c.Driver == 255 || c.LiftInput <= 0)
                return 0;
            p.State |= 1;
        }
        ConstructConfig.Put(ref p, d, CfgKey.Grain, ConstructConfig.Get(p, d, CfgKey.Grain, 0) + dt);
        return 1;
    }

    /// <summary>What a kind of thruster does to its push, its direction and its burn this tick.</summary>
    public static void Scale(Construct c, ref CThruster t, in CPart p, ConstructPartDef d, float air, float want, ref Vector3 dir, ref float push, ref float burn, Vector3 at, Quaternion q, float dt)
    {
        if (d.AbForce > 0)
        {
            var lit = p.InCount2 > 0 && p.In2 >= 0.5f && want >= (t.Ab > 0.5f ? 0.35f * d.AbBand : d.AbBand) && push > 0 && air > 0; // (once lit a stabilizer asks for less of it, and it stays lit)
            t.Ab = Math.Clamp(t.Ab + (lit ? dt : -dt) / AbSpool, 0, 1);
            push += d.AbForce * t.Ab;
            burn += d.AbBurn * t.Ab;
        }
        if (d.ThrustKind == ThrustKind.Air)
        {
            push *= air;
            burn *= air;
        }
        if (d.ThrustGimbal > 0)
        {
            var rate = d.GimbalRate * dt;
            var most = ConstructAero.ThrowOf(p, d, d.ThrustGimbal) * (ConstructConfig.Get(p, d, CfgKey.Reverse, 0) >= 0.5f ? -1 : 1);
            var expo = ConstructConfig.Get(p, d, CfgKey.Expo, 0) / 100f;
            var wantX = ConstructAero.Curve(t.GimbalScanned && c.StabOwns ? t.CommandX : p.InCount1 > 0 ? Math.Clamp(p.In1, -1, 1) : 0, expo) * most;
            var wantY = ConstructAero.Curve(t.GimbalScanned && c.StabOwns ? t.CommandY : p.InCount2 > 0 ? Math.Clamp(p.In2, -1, 1) : 0, expo) * most;
            t.AngleX += Math.Clamp(wantX - t.AngleX, -rate, rate);
            t.AngleY += Math.Clamp(wantY - t.AngleY, -rate, rate);
            dir = Vector3.Normalize(Vector3.Transform(t.Dir + Vector3.Cross(t.GimbalX, t.Dir) * t.AngleX + Vector3.Cross(t.GimbalY, t.Dir) * t.AngleY, q));
        }
        if (d.ThrustKind == ThrustKind.Repulsor)
        {
            var f = Falloff(t.Gap, d.RepulsorRange);
            push *= f;
            if (f > 0)
            {
                // A shock absorber in the field: each one damps its own corner, so a hover car doesn't bounce on the spring it rides.
                var damp = -c.Mass / c.RepulsorCount * 14f * Vector3.Dot(c.PointVel(at), dir) * MathF.Min(1, f * 4);
                push = Math.Clamp(push + damp, 0, d.ThrustForce * 2);
            }
        }
    }

    /// <summary>Server: a decoupler's tick. A rising input fires it, once; bodies change after the logic step.</summary>
    public static float StepDecoupler(ConstructWorld world, Construct c, int part, ref CPart p, bool on)
    {
        if (on && (p.State & 2) == 0)
            world.Decouples.Add((c.Id, (ushort)part));
        p.State = (byte)(on ? p.State | 2 : p.State & ~2);
        return 0;
    }

    public static void Separate(ConstructWorld world)
    {
        if (world.Decouples.Count == 0)
            return;
        // (highest part first: a body's lower indices stay good after a decoupler goes, and each body's other side is found in what is left)
        world.Decouples.Sort((a, b) => b.Part.CompareTo(a.Part));
        foreach (var (body, part) in world.Decouples)
            if (world.Get(body) is { } c && part < c.PartCount && c.Parts[part].D.Logic == CLogic.Decoupler)
                world.Decouple(c, part);
        world.Decouples.Clear();
    }

    /// <summary>The piece's share of the shared pools: by the fuel tanks and batteries that went with it.</summary>
    public static void ShareFuel(Construct c, Construct piece, Span<int> comp, int k)
    {
        var cap = c.FuelCapacity();
        var share = cap > 0 ? Math.Clamp(piece.FuelCapacity() / cap, 0, 1) : 0;
        var fuel = c.Fuel * share;
        c.Fuel -= fuel;
        var charge = c.ChargeCapacity > 0 ? c.Charge * Math.Clamp(piece.ChargeCapacity / c.ChargeCapacity, 0, 1) : 0;
        c.Charge -= charge;
        piece.SetStores(fuel, charge);
    }
}

public static partial class ConstructPhysics
{
    private const float RayBack = 0.15f;

    /// <summary>Each repulsor's gap: a look at the ground and at constructs under it every few ticks, in turn, and carried on by the speed between looks.</summary>
    public static void RepulsorLook(Construct c, IWorldQuery query, uint tick)
    {
        var sum = 0f;
        var shape = 0f;
        var n = 0;
        var lookNow = (int)(tick % ConstructBoost.LookEvery);
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            ref var t = ref c.Thrusters[k];
            ref var p = ref c.Parts[t.Part];
            var d = p.D;
            if (d.ThrustKind != ThrustKind.Repulsor || !p.Working)
                continue;
            c.WorldFrame(t.Seg, out var q, out var o);
            var at = o + Vector3.Transform(t.At, q);
            var down = -Vector3.Transform(t.Dir, q);
            if (k % ConstructBoost.LookEvery == lookNow || t.Gap == float.MaxValue)
            {
                var range = d.RepulsorRange;
                var gap = float.MaxValue;
                var from = at - down * RayBack; // (from inside the part: sat on the ground, its face is on the ground)
                if (query.Raycast(from, down, range + RayBack, QueryFilter.All, out var hit))
                    gap = MathF.Max(0, hit.Distance - RayBack);
                if (_world is { } w && w.Raycast(from, down, MathF.Min(range, gap) + RayBack, out var ch, ignore: c))
                    gap = MathF.Min(gap, MathF.Max(0, ch.Distance - RayBack));
                t.Gap = gap;
            }
            else if (t.Gap != float.MaxValue)
                t.Gap = MathF.Max(0, t.Gap + Vector3.Dot(c.PointVel(at), down) * World.TickDt);
            var seen = MathF.Min(t.Gap, d.RepulsorRange);
            sum += seen;
            shape += ConstructBoost.Falloff(seen, d.RepulsorRange);
            n++;
        }
        c.RepulsorGap = n > 0 ? sum / n : 0;
        c.RepulsorShape = n > 0 ? shape / n : 0;
    }
}
