using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>How a designed weapon finds what it steers for (a seeker section's <c>"kind"</c>).</summary>
public enum OrdSeek : byte
{
    None, // straight (a Seeker Head on its rack still locks for it)
    Heat, // the moving body nearest its axis at launch; flares draw it
    Radar, // the same, slow and stopped bodies too; flares don't
    Command, // what its launcher's gunner looks at when it goes: a body, or the point on the ground
    Laser, // the spot its launcher's gunner keeps looking at, or a Camera on its channel looks at (a drone's)
    Gps, // the waypoint of its launcher's Flight Stabilizer, read as it flies (wire the waypoint from logic)
    Radio, // the Wireless Sender on its channel
    Beacon, // the nearest beacon in its cone at launch
}

/// <summary>What sets a designed weapon off, besides hitting something (fuse sections, any together).</summary>
[Flags]
public enum OrdFuse : byte
{
    None = 0,
    Prox = 1, // something flying comes within its range
    Altitude = 2, // it comes down to its height above the ground
    Airburst = 4, // it comes within its distance of what it steers for
    Remote = 8, // a Wireless Sender on its channel turns on
    Timer = 16, // its time since launch
}

/// <summary>What a charge holds besides its own blast.</summary>
public enum OrdSub : byte
{
    None,
    Cluster, // bomblets that go off where they land
    Incendiary, // bomblets that set fire to what is near
    Mines, // bomblets that lie until something moves near, or their life runs out
    Mirv, // small guided warheads that fan out round its aim
    Rods, // steel rods: what they hit takes their speed
}

/// <summary>A stage: the sections between two separators, and what its motors do (OrdnanceFlight.cs). The tail's burns first.</summary>
public sealed class OrdStage
{
    public float Thrust, AirThrust; // N in all (the share that breathes air in AirThrust)
    public float Burn, Wait, MinSpeed, Gimbal; // s it burns, s after a drop before it lights, an air motor's lowest speed (m/s), degrees a second it turns while burning
    public float Mass, Fuel; // kg of its sections, and of fuel in them
    public string Fuels = ""; // its motors' fuels in words
    public OrdnanceDesign? Spent; // what it leaves when it drops (not the nose stage)
    public ConstructPartDef? SpentDef;
}

/// <summary>One charge of a series (payload sections between Tandem Spacers): its blast, and what it lets go.</summary>
public sealed class OrdCharge
{
    public float Payload, Blast, Damage, Crater;
    public OrdSub Sub;
    public int Count; // bomblets as designed
    public float SubLife, SubProx; // a mine's life (s) and range (m)
    public bool Kinetic; // a penetrator: its damage is its speed's when it hits
}

/// <summary>A design's flight: its stages, steering, seeker, fuses and charges, worked out with its numbers (<see cref="OrdnanceDesign.Stats"/>).</summary>
public sealed class OrdFlightStats
{
    public readonly List<OrdStage> Stages = []; // tail first
    public readonly List<OrdCharge> Charges = []; // nose first
    public readonly List<string> Limits = []; // engine limits it meets, and how they scale
    public float TurnAir, TurnBurn, TurnAny; // degrees a second
    public float Drag, Lift, Deploy; // a share of speed a second; of gravity held up; s before chutes and wings open
    public OrdSeek Seek;
    public float SeekReach, SeekCone, Channel;
    public OrdFuse Fuses;
    public float Prox, FuseHeight, FuseDistance, Timer, RemoteChannel;
    public float ThrustWeight, Range, TopSpeed, Seconds, Glide; // at launch from rest at sea level; Glide: metres along for a metre down
    public string Problem = "";
    public string Label = "";

    public const float BaseDrag = 0.02f; // a share of speed a second every designed weapon loses in thick air at DragSpeed (drag goes with the speed squared)
    public const float DragSpeed = 100f; // m/s at which a drag number is the share of speed lost a second
    public const float LiftSpeed = 45f; // m/s at which wings hold up all they can
    public const float TurnSpeed = 60f; // m/s at which fins turn all they can
    public const float GlideTurn = 0.8f; // a share of a winged weapon's sink a second its wings turn into speed along, at full hold
    public const float RodSpeed = 300f; // m/s at which a rod does its payload's damage

    /// <summary>kg a section's settings add: <c>"mass_KEY"</c> numbers, a kg for each unit of that setting over its least.</summary>
    public static float SettingMass(OrdPiece p)
    {
        var o = p.Section.Ord!;
        var kg = 0f;
        for (var k = 0; k < o.Settings.Length && k < p.Values.Length; k++)
            if (o.Nums.TryGetValue("mass_" + o.Settings[k].Key, out var per))
                kg += per * (p.Values[k] - o.Settings[k].Min);
        return kg;
    }

    private static float Set(OrdPiece p, string key, float or)
    {
        var s = p.Section.Ord!.Settings;
        for (var k = 0; k < s.Length; k++)
            if (s[k].Key == key)
                return k < p.Values.Length ? p.Values[k] : s[k].Default;
        return or;
    }

    /// <summary>Works out the flight of a design into its numbers (its mass and payload already there).</summary>
    public static void Fill(OrdnanceDesign d, OrdStats st)
    {
        var f = st.Flight;
        var s = d.Scale;
        var cube = s * s * s;
        var turnScale = 1f / MathF.Sqrt(MathF.Max(0.05f, s)); // (a bigger weapon turns slower)

        // Stages: the pieces from nose to tail, split at separators; the tail's goes first.
        var groups = new List<(int From, int To)>();
        var start = 0;
        for (var i = 0; i < d.Pieces.Count; i++)
            if (d.Pieces[i].Section.Ord!.Slot == "stage")
            {
                groups.Add((start, i));
                start = i + 1;
            }
        groups.Add((start, d.Pieces.Count));
        for (var g = groups.Count - 1; g >= 0; g--)
        {
            var (from, to) = groups[g];
            var lo = g > 0 ? from - 1 : from; // (a stage takes the separator ahead of it when it drops)
            var stage = new OrdStage();
            float use = 0, solid = 0;
            var fuels = new StringBuilder();
            for (var i = lo; i < to; i++)
            {
                var p = d.Pieces[i];
                var o = p.Section.Ord!;
                stage.Mass += (p.Section.Mass + SettingMass(p)) * p.Count * cube;
                if (o.Slot == "motor")
                {
                    var cls = Set(p, "class", 1);
                    var thrust = o.Num("thrust") * cls * p.Count * cube;
                    stage.Thrust += thrust;
                    if (o.Num("air") > 0)
                        stage.AirThrust += thrust;
                    stage.MinSpeed = MathF.Max(stage.MinSpeed, o.Num("minSpeed"));
                    use += o.Num("use") * cls * p.Count * cube;
                    stage.Fuel += o.Num("fuel") * p.Count * cube;
                    var burn = Set(p, "burn", o.Num("burn"));
                    solid = MathF.Max(solid, burn);
                    stage.Gimbal += o.Num("gimbal") * p.Count;
                    stage.Wait = MathF.Max(stage.Wait, o.Num("wait"));
                    if (o.Text.Length > 0 && fuels.ToString().IndexOf(o.Text, StringComparison.Ordinal) < 0)
                        fuels.Append(fuels.Length > 0 ? ", " : "").Append(o.Text);
                    if (o.Num("use") > 0 && o.Num("fuel") <= 0 && !HasTank(d, from, to))
                        f.Problem = $"the {p.Section.Name} needs a fuel tank in its stage";
                }
                else if (o.Slot == "fuel")
                    stage.Fuel += Set(p, "fuel", o.Num("fuel")) * p.Count * cube;
            }
            stage.Burn = MathF.Max(solid, use > 0 ? stage.Fuel / use : 0);
            stage.Fuels = fuels.ToString();
            if (g > 0)
            {
                stage.Spent = new OrdnanceDesign { Kind = OrdKind.Spent, DiameterCm = d.DiameterCm };
                for (var i = lo; i < to; i++)
                    stage.Spent.Pieces.Add(d.Pieces[i]);
            }
            f.Stages.Add(stage);
        }

        // Steering, drag and wings, seeker and fuses, over every section.
        OrdCharge? charge = null;
        float blastK = 0, damageK = 0, craterK = 0;
        void Close()
        {
            if (charge == null)
                return;
            (charge.Blast, charge.Damage, charge.Crater) = OrdnanceDesign.Charge(blastK, damageK, craterK, charge.Payload);
            f.Charges.Add(charge);
            charge = null;
            blastK = damageK = craterK = 0;
        }
        f.Drag = BaseDrag;
        foreach (var p in d.Pieces)
        {
            var o = p.Section.Ord!;
            f.TurnAir += o.Num("turnAir") * p.Count * turnScale;
            f.TurnBurn += o.Num("turnBurn") * p.Count * turnScale;
            f.TurnAny += o.Num("turnAny") * p.Count * turnScale;
            f.Drag += o.Num("drag") * p.Count;
            f.Lift += o.Num("lift") * p.Count;
            f.Deploy = MathF.Max(f.Deploy, o.Num("deploy"));
            switch (o.Slot)
            {
                case "seeker" when f.Seek == OrdSeek.None:
                    f.Seek = SeekOf(o.Text);
                    f.SeekReach = o.Num("reach", 1000);
                    f.SeekCone = o.Num("cone", 20);
                    f.Channel = Set(p, "channel", 1);
                    break;
                case "fuse":
                    switch (o.Text)
                    {
                        case "prox":
                            f.Fuses |= OrdFuse.Prox;
                            f.Prox = MathF.Max(f.Prox, Set(p, "range", 5));
                            break;
                        case "altitude":
                            f.Fuses |= OrdFuse.Altitude;
                            f.FuseHeight = MathF.Max(f.FuseHeight, Set(p, "height", 20));
                            break;
                        case "airburst":
                            f.Fuses |= OrdFuse.Airburst;
                            f.FuseDistance = MathF.Max(f.FuseDistance, Set(p, "distance", 15));
                            break;
                        case "remote":
                            f.Fuses |= OrdFuse.Remote;
                            f.RemoteChannel = Set(p, "channel", 1);
                            break;
                        case "timer":
                            f.Fuses |= OrdFuse.Timer;
                            f.Timer = f.Timer > 0 ? MathF.Min(f.Timer, Set(p, "time", 10)) : Set(p, "time", 10);
                            break;
                    }
                    break;
                case "spacer":
                    Close();
                    break;
                case "payload":
                    charge ??= new OrdCharge();
                    var kg = o.Payload * p.Count * cube;
                    charge.Payload += kg;
                    blastK += o.Blast * kg;
                    damageK += o.Damage * kg;
                    craterK += o.Crater * kg;
                    charge.Kinetic |= o.Text == "kinetic";
                    if (SubOf(o.Text) is var sub and not OrdSub.None && charge.Sub == OrdSub.None)
                    {
                        charge.Sub = sub;
                        charge.Count = (int)MathF.Round(Set(p, "count", o.Num("count", 10))) * p.Count;
                        charge.SubLife = Set(p, "life", o.Num("life", 60));
                        charge.SubProx = o.Num("prox", 4);
                    }
                    break;
            }
        }
        Close();

        // What it does from rest at sea level, by the same steps as in the air (no gravity: a level run).
        var mass = st.Mass;
        var first = f.Stages.Count > 0 ? f.Stages[0] : null;
        const float pull = World.Gravity; // (the flat worlds' pull: a design is the same wherever it is built)
        f.ThrustWeight = first != null && mass > 0 ? first.Thrust / (mass * pull) : 0;
        float v = d.Kind == OrdKind.Missile ? OrdnanceFlight.RailKick : 0, x = 0, t = 0, top = v;
        const float dt = 0.05f;
        foreach (var stage in f.Stages)
        {
            for (var w = 0f; w < stage.Burn && t < 600; w += dt, t += dt)
            {
                var push = (stage.Thrust - (v < stage.MinSpeed ? stage.AirThrust : 0)) / MathF.Max(0.5f, mass);
                v += (push - f.Drag * v * v / DragSpeed) * dt;
                x += v * dt;
                top = MathF.Max(top, v);
            }
            mass -= stage.Mass;
        }
        if (d.Kind == OrdKind.Missile)
            for (var w = 0f; w < ConstructOrdnance.MissileCoast; w += dt, t += dt)
            {
                v -= f.Drag * v * v / DragSpeed * dt;
                x += v * dt;
            }
        f.Range = x;
        f.TopSpeed = top;
        f.Seconds = t;
        f.Glide = f.Lift > 0 ? GlideRatio(f.Lift, f.Drag, pull) : 0;

        if (d.Kind == OrdKind.Missile && (first == null || f.Stages.TrueForAll(g => g.Thrust <= 0)) && f.Problem.Length == 0)
            f.Problem = "a missile needs a motor";

        // The engine's limits it meets, and what it does about them.
        foreach (var c in f.Charges)
            if (c.Sub != OrdSub.None && c.Count > OrdnanceFlight.MaxSubs)
                f.Limits.Add($"{c.Count} {SubWord(c.Sub)}: {OrdnanceFlight.MaxSubs} fly, each carrying {c.Count / (float)OrdnanceFlight.MaxSubs:0.#} of them");
        var life = OrdnanceFlight.LifeSeconds(f);
        if (life > ConstructWorld.ShellMaxAge / (float)World.TickRate)
            f.Limits.Add($"it flies up to {life:0} s, past the {ConstructWorld.ShellMaxAge / World.TickRate} s a shell is usually given");
        if (st.Blast > 30)
            f.Limits.Add($"its {st.Blast:0} m blast walks {ConstructWorld.ShellPartsPerTick} parts a tick over up to {ConstructWorld.ShellMaxBodies} bodies: on a big target it takes several ticks");
        if ((f.Fuses & OrdFuse.Remote) != 0)
            f.Limits.Add("its remote fuse hears its channel at any range");
        if (f.Charges.Count > 1)
            f.Limits.Add($"{f.Charges.Count} charges go off in series, {OrdnanceFlight.SeriesTicks} ticks apart");

        var label = new StringBuilder();
        if (f.Stages.Exists(g => g.Thrust > 0))
            label.Append($", thrust {f.ThrustWeight:0.#}x weight");
        if (f.Stages.Count > 1)
            label.Append($", {f.Stages.Count} stages");
        if (f.Seek != OrdSeek.None)
            label.Append($", {f.Seek.ToString().ToLowerInvariant()} seeker");
        f.Label = label.ToString();
    }

    /// <summary>Metres along for each metre down, let go level at 60 m/s in thick air: <see cref="OrdnanceFlight.Fly"/>'s wings in two dimensions.</summary>
    private static float GlideRatio(float lift, float drag, float g)
    {
        float vx = 60, vy = 0, x = 0, y = 0;
        const float dt = 0.05f;
        for (var i = 0; i < 2400 && y > -500; i++)
        {
            var speed = MathF.Sqrt(vx * vx + vy * vy);
            vx -= vx * MathF.Min(1f, drag * speed / DragSpeed * dt);
            vy -= vy * MathF.Min(1f, drag * speed / DragSpeed * dt);
            speed = MathF.Sqrt(vx * vx + vy * vy);
            var hold = MathF.Min(1f, lift * speed * speed / (LiftSpeed * LiftSpeed));
            vy -= g * (1 - hold) * dt;
            if (vy < 0)
            {
                var now = MathF.Sqrt(vx * vx + vy * vy);
                vy *= 1f - MathF.Min(1f, GlideTurn * hold * dt);
                vx = MathF.Sqrt(MathF.Max(0, now * now - vy * vy));
            }
            x += vx * dt;
            y += vy * dt;
        }
        return y < -1f ? x / -y : 99;
    }

    private static bool HasTank(OrdnanceDesign d, int from, int to)
    {
        for (var i = from; i < to; i++)
            if (d.Pieces[i].Section.Ord!.Slot == "fuel")
                return true;
        return false;
    }

    public static OrdSeek SeekOf(string kind) => kind switch
    {
        "heat" => OrdSeek.Heat,
        "radar" => OrdSeek.Radar,
        "command" => OrdSeek.Command,
        "laser" => OrdSeek.Laser,
        "gps" => OrdSeek.Gps,
        "radio" => OrdSeek.Radio,
        "beacon" => OrdSeek.Beacon,
        _ => OrdSeek.None,
    };

    public static OrdSub SubOf(string kind) => kind switch
    {
        "cluster" => OrdSub.Cluster,
        "incendiary" => OrdSub.Incendiary,
        "mines" => OrdSub.Mines,
        "mirv" => OrdSub.Mirv,
        "rods" => OrdSub.Rods,
        _ => OrdSub.None,
    };

    public static string SubWord(OrdSub s) => s switch
    {
        OrdSub.Cluster => "bomblets",
        OrdSub.Incendiary => "fire bomblets",
        OrdSub.Mines => "mines",
        OrdSub.Mirv => "warheads",
        OrdSub.Rods => "rods",
        _ => "",
    };
}

/// <summary>
/// Designed bombs and missiles in the air (docs/sandbox/CONSTRUCTS.md, "Modular ordnance"): a rack lets one go as a shell carrying its design, and it
/// flies on the shell pool like the Missile Rail's. Each PC flies its own copy from the launch event (<see cref="Fly"/>); the server drops its spent
/// stages (a loose part where it was), moves the point a guided one steers for and sends both as ShellSteer, sets off its fuses and, when it goes
/// off, its next charge, bomblets, mines, warheads or rods, as more shells. Steering is arcade: a turn-rate-limited pursuit of the seeker's point.
/// Nothing allocates in a tick but a stage dropping, which makes a part as a bomb's release does.
/// </summary>
public static class OrdnanceFlight
{
    public const float RailKick = 12f; // m/s a rail adds along itself as it lets go
    public const float DropWait = 0.4f; // s a missile let go from a hook falls before it lights (unless its motor says)
    public const int GuideEvery = 6; // ticks between a guided weapon's new looks at its point (10 Hz)
    public const float GuideMove = 0.75f; // m its point must move before every PC is told
    public const int MaxSubs = 48; // bomblets one charge lets go: more are carried by these
    public const int SeriesTicks = 3; // ticks between the charges of a series
    public const float SeriesDepth = 1.2f; // m on along its way each charge goes off (a tandem charge's later ones go through)
    public const float MineArm = 2f; // s before a mine wakes
    public const float MineStir = 0.5f; // m/s a body must move at to set a mine off
    public const float MirvFan = 8f; // m between a MIRV's warheads round their aim
    public const float RodDrop = 60f; // m/s down rods are thrown at

    /// <summary>A spent stage to drop after the shells' step (bodies change only there).</summary>
    public struct StageDrop
    {
        public OrdStage Stage;
        public Vector3 At, Vel;
        public byte By;
    }

    /// <summary>Seconds a design may fly before it is given up on: its burns and waits, a missile's coast, and half a minute.</summary>
    public static float LifeSeconds(OrdFlightStats f)
    {
        var t = 30f + ConstructOrdnance.MissileCoast;
        foreach (var g in f.Stages)
            t += g.Burn + g.Wait;
        return MathF.Max(t, f.Timer);
    }

    // ---- Launch -----------------------------------------------------------------------------------------------------

    /// <summary>Why a designed missile on a rail stays there (its first stage's push below its weight here), or null.</summary>
    public static string? Grounded(Construct c, int rack, int part)
    {
        if (!c.DefOf(rack).RackRail || c.DefOf(part).OrdStats is not { } st || c.DefOf(part).OrdDesign?.Kind != OrdKind.Missile || st.Flight.Stages.Count == 0)
            return null;
        var g = st.Flight.Stages[0];
        var at = ConstructRender.PartCentre(c, part);
        var push = g.Thrust - g.AirThrust + (g.MinSpeed <= RailKick ? g.AirThrust * MathF.Min(1f, ConstructAir.Density(at)) : 0);
        var weight = st.Mass * ConstructShells.GravityAt(at).Length();
        return push >= weight ? null : $"its thrust ({push / 1000f:0.#} kN) is below its weight ({weight / 1000f:0.#} kN): it won't leave the rail";
    }

    /// <summary>Server: a designed weapon leaves its rack. False when the pool is full (it waits on its hook).</summary>
    public static bool Launch(ConstructWorld world, Construct c, int rack, int part, IWorldQuery query)
    {
        var def = c.DefOf(part);
        var st = def.OrdStats!;
        var f = st.Flight;
        var design = def.OrdDesign!;
        var rail = c.DefOf(rack).RackRail;
        var at = ConstructOrdnance.ReleaseAt(c, rack, part); // (a drum turns it to the bottom first)
        c.WorldFrame(c.Parts[part].Seg, out var q, out _);
        var fwd = Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, q * Rot24.Quat(c.Parts[part].Rot)));
        var vel = c.PointVel(at) + c.SegPointVel(c.Parts[part].Seg, at) + (rail ? fwd * RailKick : Vector3.Zero);
        var first = f.Charges.Count > 0 ? f.Charges[0] : null;
        var s = new CShell
        {
            Kind = design.Kind == OrdKind.Missile ? ShellKind.Missile : ShellKind.Bomb,
            By = c.Owner, Gun = c.Id, GunPart = (ushort)rack, Size = def.BoundsMax - def.BoundsMin,
            Pos = at, Prev = at, Tested = at, Vel = vel, Clear = ConstructOrdnance.BombClear,
            Blast = first?.Blast ?? st.Blast, Damage = first?.Damage ?? st.Damage, Crater = first?.Crater ?? st.Crater,
            Delay = Math.Max(1, (int)MathF.Round(MathF.Max(0.02f, st.Fuse) * World.TickRate)),
            Flags = ShellFlags.Designed, Design = def, Mass = st.Mass,
            Turn = f.TurnAny * MathF.PI / 180f, TurnAir = f.TurnAir * MathF.PI / 180f,
            Drag = f.Drag, Lift = f.Lift, Deploy = (int)(f.Deploy * World.TickRate),
            Life = (int)(LifeSeconds(f) * World.TickRate),
            Seek = (byte)f.Seek, Channel = (int)f.Channel,
            Fuses = f.Fuses, Prox = (f.Fuses & OrdFuse.Prox) != 0 ? f.Prox : 0, FuseHeight = f.FuseHeight, FuseDistance = f.FuseDistance,
        };
        if (first is { Kinetic: true })
            s.Flags |= ShellFlags.Kinetic;
        SetStage(ref s, f.Stages, 0);
        if (f.Stages.Count > 0)
        {
            var wait = f.Stages[0].Wait;
            s.Wait = (int)((rail ? wait : MathF.Max(wait, design.Kind == OrdKind.Missile ? DropWait : 0)) * World.TickRate);
        }
        if (design.Kind == OrdKind.Missile)
        {
            var burns = s.Wait;
            foreach (var g in f.Stages)
                burns += (int)((g.Burn + g.Wait) * World.TickRate);
            s.Fuse = burns + (int)(ConstructOrdnance.MissileCoast * World.TickRate);
        }
        if ((f.Fuses & OrdFuse.Timer) != 0)
            s.Fuse = s.Fuse > 0 ? Math.Min(s.Fuse, (int)(f.Timer * World.TickRate)) : (int)(f.Timer * World.TickRate);
        Lock(world, c, rack, ref s, f, at, fwd, query);
        var id = world.AddShell(s);
        if (id == 0)
            return false;
        s.Id = id;
        world.AddFire(Out(s, c.Id, (ushort)rack).WithLook(c, part));
        return true;
    }

    /// <summary>Its numbers for stage <paramref name="k"/>: the push as m/s2 of what is still flying, how long, the gimbal's turn.</summary>
    private static void SetStage(ref CShell s, List<OrdStage> stages, int k)
    {
        s.Stage = (byte)Math.Min(k, 255);
        if (k >= stages.Count)
        {
            (s.Thrust, s.AirPush, s.Burn, s.TurnBurn, s.MinSpeed) = (0, 0, 0, 0, 0);
            return;
        }
        var g = stages[k];
        var mass = MathF.Max(0.5f, s.Mass);
        s.Thrust = (g.Thrust - g.AirThrust) / mass;
        s.AirPush = g.AirThrust / mass;
        s.Burn = (int)(g.Burn * World.TickRate);
        s.MinSpeed = g.MinSpeed;
        s.TurnBurn = (g.Gimbal + (s.Design?.OrdStats?.Flight.TurnBurn ?? 0)) * MathF.PI / 180f;
        s.Flags = g.AirThrust > 0 ? s.Flags | ShellFlags.AirMotor : s.Flags & ~ShellFlags.AirMotor;
    }

    /// <summary>The launch event of a shell: what every PC needs to fly it alike.</summary>
    public static CFireEvent Out(in CShell s, ushort body, ushort part) => new()
    {
        Kind = CFireKind.ShellOut, Body = body, Part = part, Shell = s.Id, At = s.Pos, To = s.Vel, Muzzle = (byte)s.Kind, Half = s.Size,
        Thrust = s.Thrust, Turn = s.Turn, Burn = (ushort)Math.Min(s.Burn, ushort.MaxValue), Target = s.Target,
        Ext = (byte)((s.Flags & ShellFlags.Designed) != 0 ? 1 : 0),
        AirPush = s.AirPush, TurnAir = s.TurnAir, TurnBurn = s.TurnBurn, Drag = s.Drag, Lift = s.Lift, MinSpeed = s.MinSpeed,
        Wait = (ushort)Math.Min(s.Wait, ushort.MaxValue), Deploy = (ushort)Math.Min(s.Deploy, ushort.MaxValue), Flags = s.Flags & ~ShellFlags.Mine, Aim = s.Aim, Life = s.Life,
    };

    // ---- Seekers -----------------------------------------------------------------------------------------------------

    /// <summary>What it steers for, as it leaves: a body (every PC steers for it), or a point (sent again as it moves).</summary>
    private static void Lock(ConstructWorld world, Construct c, int rack, ref CShell s, OrdFlightStats f, Vector3 at, Vector3 fwd, IWorldQuery query)
    {
        var cos = MathF.Cos(f.SeekCone * MathF.PI / 180f);
        switch (f.Seek)
        {
            case OrdSeek.None:
                // A Seeker Head bolted to the rack locks for a weapon with no seeker of its own.
                for (var e = c.FirstLink(rack); e >= 0; e = c.NextLink(e))
                    if (c.DefOf(c.OtherEnd(e)) is { Sight: SightKind.Seeker } head && c.Parts[c.OtherEnd(e)].Working)
                    {
                        s.Target = ConstructOrdnance.Seek(world, c, at, fwd, head.SightReach, MathF.Cos(head.SightCone * MathF.PI / 180f), query);
                        s.Flags |= ShellFlags.FlareProne;
                        break;
                    }
                break;
            case OrdSeek.Heat:
                s.Target = ConstructOrdnance.Seek(world, c, at, fwd, f.SeekReach, cos, query);
                s.Flags |= ShellFlags.FlareProne;
                break;
            case OrdSeek.Radar:
                s.Target = SeekAny(world, c, at, fwd, f.SeekReach, cos, query);
                break;
            case OrdSeek.Command:
                if (PilotSeat(c) is var seat and >= 0)
                {
                    var eye = ConstructWeapons.SeatEye(c, seat);
                    var look = GravityFrames.Forward(c.WorldCom, c.Parts[seat].AimYaw, c.Parts[seat].AimPitch);
                    var reach = query.Raycast(eye, look, f.SeekReach, QueryFilter.Shots);
                    if (world.Raycast(eye, look, reach, out var hit, c) && !hit.Body.Anchored)
                        s.Target = hit.Body.Id;
                    else
                        Aim(ref s, world.Raycast(eye, look, reach, out hit, c) ? hit.Point : eye + look * reach);
                }
                break;
            case OrdSeek.Laser:
                if (ConstructRemote.FindCamera(world, at, (int)f.Channel, c.Owner, out var cam) is { } camBody)
                {
                    s.Guide = camBody.Id;
                    s.GuidePart = -1 - cam; // (a camera's spot, not a gunner's)
                }
                else if (PilotSeat(c) is var gunner and >= 0)
                {
                    s.Guide = c.Id;
                    s.GuidePart = gunner;
                }
                if (Spot(world, ref s, query, f.SeekReach) is { } spot)
                    Aim(ref s, spot);
                break;
            case OrdSeek.Gps:
                for (var i = 0; i < c.PartCount; i++)
                    if (c.DefOf(i).Logic == CLogic.FlightStabilizer && c.Parts[i].Working)
                    {
                        s.Guide = c.Id;
                        s.GuidePart = i;
                        if (Spot(world, ref s, query, f.SeekReach) is { } way)
                            Aim(ref s, way);
                        break;
                    }
                break;
            case OrdSeek.Radio:
                if (world.RadioSenderNear(at, (int)f.Channel, f.SeekReach, out var tx))
                    Aim(ref s, tx);
                break;
            case OrdSeek.Beacon:
                if (BeaconNear(world, c, at, fwd, f.SeekReach, cos) is { } beacon)
                    Aim(ref s, beacon);
                break;
        }
    }

    private static void Aim(ref CShell s, Vector3 at)
    {
        s.Aim = at;
        s.Flags |= ShellFlags.HasAim;
    }

    /// <summary>The first seat on a construct with someone in it (who fires its racks), or -1.</summary>
    private static int PilotSeat(Construct c)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).Role == PartRole.Seat && (c.Parts[i].Keys & CPart.Occupied) != 0)
                return i;
        return -1;
    }

    /// <summary>A radar's look: the unanchored body nearest its axis in its cone, moving or not, that the map doesn't hide.</summary>
    private static ushort SeekAny(ConstructWorld world, Construct c, Vector3 at, Vector3 dir, float reach, float cos, IWorldQuery query)
    {
        var near = world.Near(at + dir * (reach * 0.5f), reach * 0.5f, world.OrdnanceNear);
        Construct? best = null;
        var most = cos;
        for (var i = 0; i < near.Count; i++)
        {
            var b = near[i];
            if (b == c || b.Anchored || b.CarriedBy != 0 || b.PartCount < 2)
                continue;
            var to = b.WorldCom - at;
            var len = to.Length();
            if (len > reach || len < 1f)
                continue;
            var dot = Vector3.Dot(to / len, dir);
            if (dot > most)
            {
                most = dot;
                best = b;
            }
        }
        if (best == null)
            return 0;
        var way = best.WorldCom - at;
        var dist = way.Length();
        return query.Raycast(at, way / dist, dist, QueryFilter.Shots) < dist - 1f ? (ushort)0 : best.Id;
    }

    /// <summary>The working beacon nearest its axis in its cone, or null. Walks the parts of the bodies near the cone: at a launch only.</summary>
    private static Vector3? BeaconNear(ConstructWorld world, Construct c, Vector3 at, Vector3 dir, float reach, float cos)
    {
        var near = world.Near(at + dir * (reach * 0.5f), reach * 0.5f, world.OrdnanceNear);
        Vector3? best = null;
        var most = cos;
        for (var i = 0; i < near.Count; i++)
        {
            var b = near[i];
            if (b == c)
                continue;
            for (var p = 0; p < b.PartCount; p++)
            {
                if (!b.DefOf(p).IsBeacon || !b.Parts[p].Working)
                    continue;
                var where = ConstructRender.PartCentre(b, p);
                var to = where - at;
                var len = to.Length();
                if (len > reach || len < 1f)
                    continue;
                var dot = Vector3.Dot(to / len, dir);
                if (dot > most)
                {
                    most = dot;
                    best = where;
                }
            }
        }
        return best;
    }

    /// <summary>Where a guided weapon's guide points now: a gunner's or a camera's spot, a Flight Stabilizer's waypoint, a radio sender. Null: the guide is gone.</summary>
    private static Vector3? Spot(ConstructWorld world, ref CShell s, IWorldQuery query, float reach)
    {
        switch ((OrdSeek)s.Seek)
        {
            case OrdSeek.Radio:
                return world.RadioSenderNear(s.Pos, s.Channel, reach, out var tx) ? tx : null;
            case OrdSeek.Laser or OrdSeek.Gps:
                if (world.Get(s.Guide) is not { } g)
                    return null;
                if (s.Seek == (byte)OrdSeek.Gps)
                {
                    if (s.GuidePart >= g.PartCount || g.DefOf(s.GuidePart).Logic != CLogic.FlightStabilizer)
                        return null;
                    ref var sp = ref g.Parts[s.GuidePart];
                    var d = sp.D;
                    var way = new Vector3(ConstructConfig.Get(sp, d, CfgKey.WaypointX, 0), ConstructConfig.Get(sp, d, CfgKey.WaypointY, 0), ConstructConfig.Get(sp, d, CfgKey.WaypointZ, 0));
                    if (GravityFrames.Curved)
                        return way;
                    var top = MathF.Max(s.Pos.Y, way.Y) + 2000f;
                    var down = query.Raycast(new Vector3(way.X, top, way.Z), -Vector3.UnitY, top + 4000f, QueryFilter.Shots);
                    return new Vector3(way.X, top - down, way.Z);
                }
                Vector3 eye, look;
                if (s.GuidePart < 0)
                {
                    var cam = -1 - s.GuidePart;
                    if (cam >= g.PartCount || !g.DefOf(cam).IsCamera || g.Parts[cam].Broken)
                        return null;
                    ConstructRemote.CameraPose(g, cam, 1f, false, out eye, out var rot);
                    look = Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, rot));
                }
                else
                {
                    if (s.GuidePart >= g.PartCount || (g.Parts[s.GuidePart].Keys & CPart.Occupied) == 0)
                        return null;
                    eye = ConstructWeapons.SeatEye(g, s.GuidePart);
                    look = GravityFrames.Forward(g.WorldCom, g.Parts[s.GuidePart].AimYaw, g.Parts[s.GuidePart].AimPitch);
                }
                var far = query.Raycast(eye, look, reach, QueryFilter.Shots);
                if (world.Raycast(eye, look, far, out var hit, g) && hit.Distance < far)
                    far = hit.Distance;
                return eye + look * far;
        }
        return null;
    }

    // ---- In flight ------------------------------------------------------------------------------------------------

    /// <summary>Every PC, each tick: its push (after its wait; an air motor's thinning with the air), drag, wings and turn towards what it steers for.</summary>
    public static void Fly(ConstructWorld world, ref CShell s, float dt)
    {
        var speed = s.Vel.Length();
        var air = MathF.Min(1f, ConstructAir.Density(s.Pos));
        var burning = false;
        if (s.Wait > 0)
            s.Wait--;
        else if (s.Burn > 0)
        {
            s.Burn--;
            burning = true;
            var push = s.Thrust + (speed >= s.MinSpeed ? s.AirPush * air : 0);
            var along = speed > 0.5f ? s.Vel / speed : Vector3.Normalize(-ConstructShells.GravityAt(s.Pos) + new Vector3(0, 1e-3f, 0));
            s.Vel += along * (push * dt);
            speed = s.Vel.Length();
        }
        if (s.Age >= s.Deploy)
        {
            if (s.Drag > 0 && air > 0)
                s.Vel -= s.Vel * MathF.Min(1f, s.Drag * air * speed / OrdFlightStats.DragSpeed * dt); // (with the square of its speed)
            if (s.Lift > 0 && air > 0)
            {
                var hold = MathF.Min(1f, s.Lift * air * speed * speed / (OrdFlightStats.LiftSpeed * OrdFlightStats.LiftSpeed));
                var pull = ConstructShells.GravityAt(s.Pos);
                s.Vel -= pull * (hold * dt); // (its wings hold up this share of its weight...)
                var g = pull.Length();
                if (g > 0.01f && speed > 1f)
                {
                    // ...and turn its sink into speed along: a glide, not a fall.
                    var up = -pull / g;
                    var sink = Vector3.Dot(s.Vel, up);
                    var along = s.Vel - up * sink;
                    var flat = along.Length();
                    if (sink < 0 && flat > 0.5f)
                    {
                        var now = s.Vel.Length();
                        sink *= 1f - MathF.Min(1f, OrdFlightStats.GlideTurn * hold * dt);
                        s.Vel = along / flat * MathF.Sqrt(MathF.Max(0, now * now - sink * sink)) + up * sink;
                    }
                }
            }
            speed = s.Vel.Length();
        }
        var rate = s.Turn + s.TurnAir * air * MathF.Min(1f, speed / OrdFlightStats.TurnSpeed) + (burning ? s.TurnBurn : 0);
        if (rate <= 0 || speed < 1f)
            return;
        Vector3 aim;
        if (s.Decoy != 0 && world.ShellAt(s.Decoy) is var fl and >= 0)
            aim = world.Shells[fl].Pos;
        else if (s.Decoy != 0)
        {
            s.Decoy = 0; // (the flare burnt out: nothing is left to steer for)
            s.Flags &= ~ShellFlags.HasAim;
            return;
        }
        else if (s.Target != 0 && world.Get(s.Target) is { } t)
        {
            var dist = Vector3.Distance(s.Pos, t.WorldCom);
            aim = t.WorldCom + t.Vel * MathF.Min(dist / speed, 3f);
        }
        else if ((s.Flags & ShellFlags.HasAim) != 0)
            aim = s.Aim;
        else
            return;
        var want = aim - s.Pos;
        if (want.LengthSquared() > 1e-2f)
            s.Vel = RocketFlight.Turn(s.Vel / speed, Vector3.Normalize(want), rate * dt) * speed;
    }

    /// <summary>Server, each tick for a designed weapon in the air: a burnt-out stage drops and the next lights; now and then a guided one's point moves.</summary>
    public static void Step(ConstructWorld world, ref CShell s, IWorldQuery query)
    {
        var f = s.Design!.OrdStats!.Flight;
        if (s.Burn == 0 && s.Wait == 0 && s.Stage + 1 < f.Stages.Count)
        {
            var spent = f.Stages[s.Stage];
            var dir = s.Vel.LengthSquared() > 1e-4f ? Vector3.Normalize(s.Vel) : Vector3.UnitY;
            if (spent.Spent != null && world.StageDrops.Count < world.StageDrops.Capacity)
            {
                // It falls away to one side of the line it flies, down where there is a down: its ray never meets its own stage.
                var down = ConstructShells.GravityAt(s.Pos);
                var side = down - dir * Vector3.Dot(down, dir);
                side = side.LengthSquared() > 1e-4f ? Vector3.Normalize(side) : Vector3.Normalize(Vector3.Cross(dir, MathF.Abs(dir.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY));
                world.StageDrops.Add(new StageDrop { Stage = spent, At = s.Pos - dir * (s.Size.Z * 0.5f) + side * (s.Size.X + 1.2f), Vel = s.Vel - dir * 3f + side * 3f, By = s.By });
            }
            s.Mass = MathF.Max(0.5f, s.Mass - spent.Mass);
            SetStage(ref s, f.Stages, s.Stage + 1); // (it lights at once: a motor's wait is only for a drop off a hook)
            world.AddFire(new CFireEvent
            {
                Kind = CFireKind.ShellSteer, Shell = s.Id, Target = s.Target, At = s.Pos, To = s.Vel, Ext = (byte)(6 | ((s.Flags & ShellFlags.HasAim) != 0 ? 1 : 0)), Aim = s.Aim,
                Thrust = s.Thrust, AirPush = s.AirPush, Burn = (ushort)Math.Min(s.Burn, ushort.MaxValue), TurnBurn = s.TurnBurn, MinSpeed = s.MinSpeed, Flags = s.Flags,
            });
        }
        if ((OrdSeek)s.Seek is OrdSeek.Laser or OrdSeek.Gps or OrdSeek.Radio && (s.Age + s.Id) % GuideEvery == 0 && s.Decoy == 0)
        {
            if (Spot(world, ref s, query, f.SeekReach * 2f) is not { } spot || Vector3.DistanceSquared(spot, s.Aim) < GuideMove * GuideMove && (s.Flags & ShellFlags.HasAim) != 0)
                return;
            Aim(ref s, spot);
            world.AddFire(new CFireEvent { Kind = CFireKind.ShellSteer, Shell = s.Id, Target = s.Target, At = s.Pos, To = s.Vel, Ext = 5, Aim = spot });
        }
    }

    /// <summary>Server, one tick in a few: does a fuse besides impact and proximity set it off?</summary>
    public static bool FuseCheck(ConstructWorld world, ref CShell s, IWorldQuery query)
    {
        if (s.Age <= ConstructOrdnance.ProxArm)
            return false;
        if ((s.Fuses & OrdFuse.Altitude) != 0)
        {
            var pull = ConstructShells.GravityAt(s.Pos);
            if (pull.LengthSquared() > 0.01f && Vector3.Dot(s.Vel, pull) > 0)
            {
                var down = Vector3.Normalize(pull);
                if (query.Raycast(s.Pos, down, s.FuseHeight, QueryFilter.Shots) < s.FuseHeight)
                    return true;
            }
        }
        if ((s.Fuses & OrdFuse.Airburst) != 0)
        {
            Vector3? aim = s.Target != 0 && world.Get(s.Target) is { } t ? t.WorldCom : (s.Flags & ShellFlags.HasAim) != 0 ? s.Aim : null;
            if (aim is { } a && Vector3.DistanceSquared(a, s.Pos) < s.FuseDistance * s.FuseDistance)
                return true;
        }
        if ((s.Fuses & OrdFuse.Remote) != 0 && s.Design?.OrdStats is { } st && world.RadioOn((int)st.Flight.RemoteChannel))
            return true;
        return false;
    }

    /// <summary>Server, now and then for a mine on the ground: has something moved near it since it woke?</summary>
    public static bool MineTripped(ConstructWorld world, ref CShell s)
    {
        if (s.Delay > (int)s.FuseDistance - (int)(MineArm * World.TickRate))
            return false; // (not awake yet: FuseDistance holds its life)
        var range = s.GuidePart / 100f; // (a mine's range, in centimetres)
        var near = world.Near(s.Pos, range + 4f, world.OrdnanceNear);
        for (var i = 0; i < near.Count; i++)
        {
            var c = near[i];
            if (c.Anchored || c.CarriedBy != 0 || c.Vel.LengthSquared() < MineStir * MineStir)
                continue;
            var local = c.ToLocal(s.Pos);
            if (Vector3.Distance(Vector3.Clamp(local, c.LocalMin, c.LocalMax), local) <= range)
                return true;
        }
        return false;
    }

    /// <summary>Server: a rod hits: its damage goes with its speed squared.</summary>
    public static void Strike(ref CShell s)
    {
        var v = s.Vel.Length() / OrdFlightStats.RodSpeed;
        s.Damage *= v * v;
        s.Crater *= MathF.Min(4f, MathF.Max(0.3f, v));
    }

    // ---- Going off ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Server, as a designed weapon's charge goes off: the next of a series is laid a little on along its way; a charge of bomblets, mines, warheads or rods
    /// lets them go (their blasts are the charge's, shared) and its own is only a pop.
    /// </summary>
    public static void GoOff(ConstructWorld world, ref CShell s)
    {
        var f = s.Design!.OrdStats!.Flight;
        var dir = s.Vel.LengthSquared() > 1e-4f ? Vector3.Normalize(s.Vel) : -Vector3.UnitY;
        if (s.Charge + 1 < f.Charges.Count && world.ShellRoom)
        {
            var next = f.Charges[s.Charge + 1];
            var at = s.Pos + dir * SeriesDepth * s.Design.OrdDesign!.Scale;
            var n = s;
            n.Pos = n.Prev = n.Tested = at;
            n.Charge++;
            n.Landed = true;
            n.Delay = SeriesTicks;
            (n.Blast, n.Damage, n.Crater) = (next.Blast, next.Damage, next.Crater);
            if (world.AddShell(n) != 0)
                world.ShellBlastsWaiting++;
        }
        if (s.Charge >= f.Charges.Count || f.Charges[s.Charge] is not { Sub: not OrdSub.None } c)
            return;
        var count = Math.Max(1, c.Count);
        var room = Math.Min(world.ShellRoomLeft, world.Fires.Length - world.FireCount - 16);
        var fly = Math.Min(Math.Min(count, MaxSubs), room);
        if (fly > 0)
        {
            var share = count / (float)fly; // (each carries this many of the designed ones: a share of the charge's filling)
            var kg = c.Payload / count * share;
            var (blast, damage, crater) = OrdnanceDesign.Charge(kg, kg, kg, kg);
            var spread = 2f + MathF.Sqrt(count) * 1.5f;
            Vector3? aimAt = s.Target != 0 && world.Get(s.Target) is { } tb ? tb.WorldCom : (s.Flags & ShellFlags.HasAim) != 0 ? s.Aim : null;
            var side = Vector3.Normalize(Vector3.Cross(dir, MathF.Abs(dir.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY));
            var up = Vector3.Cross(side, dir);
            for (var i = 0; i < fly; i++)
            {
                var h = (uint)(s.Id * 2654435761u ^ (uint)i * 2246822519u);
                var a = i * 2.39996f; // (the golden angle: an even fan)
                var r = MathF.Sqrt((i + 0.5f) / fly);
                var across = side * MathF.Cos(a) * r + up * MathF.Sin(a) * r;
                var b = new CShell
                {
                    Kind = c.Sub == OrdSub.Incendiary ? ShellKind.Incendiary : c.Sub == OrdSub.Mirv ? ShellKind.Missile : ShellKind.Bomb,
                    By = s.By, Gun = s.Gun, GunPart = s.GunPart, Pos = s.Pos, Prev = s.Pos, Tested = s.Pos,
                    Vel = s.Vel + across * (spread * (0.6f + (h & 255) / 640f)), Blast = blast, Damage = damage, Crater = crater, Delay = 1,
                    Size = new Vector3(0.12f, 0.12f, 0.3f) * MathF.Max(1f, MathF.Cbrt(share)), Flags = ShellFlags.Designed, Drag = OrdFlightStats.BaseDrag,
                };
                switch (c.Sub)
                {
                    case OrdSub.Mines:
                        b.Flags |= ShellFlags.Mine;
                        b.Delay = Math.Max(1, (int)(c.SubLife * World.TickRate));
                        b.Prox = 0; // (no fuse in the air: it lies, then MineTripped)
                        b.FuseDistance = b.Delay;
                        b.GuidePart = (int)(c.SubProx * 100);
                        break;
                    case OrdSub.Mirv:
                        b.Vel = s.Vel * 0.8f + across * spread;
                        b.Thrust = 60f;
                        b.Burn = 2 * World.TickRate;
                        b.Turn = 1.6f;
                        b.Fuse = b.Burn + (int)(ConstructOrdnance.MissileCoast * World.TickRate);
                        b.Prox = 3f;
                        b.Aim = (aimAt ?? s.Pos + dir * 300f) + (side * MathF.Cos(a) + up * MathF.Sin(a)) * (MirvFan * MathF.Sqrt(fly) * r);
                        b.Flags |= ShellFlags.HasAim;
                        break;
                    case OrdSub.Rods:
                        b.Flags |= ShellFlags.Kinetic;
                        b.Vel = s.Vel + across * spread + Vector3.Normalize(ConstructShells.GravityAt(s.Pos) + new Vector3(0, -1e-3f, 0)) * RodDrop;
                        b.Blast = MathF.Max(1.5f, blast * 0.25f);
                        b.Crater = crater * 1.5f;
                        break;
                }
                if (b.Kind == ShellKind.Incendiary)
                    b.Flags = ShellFlags.None; // (a plain shell to every PC: it falls)
                var id = world.AddShell(b);
                if (id == 0)
                    break;
                b.Id = id;
                world.AddFire(Out(b, s.Gun, s.GunPart));
            }
        }
        s.Blast = 1.5f; // (the case opening)
        s.Damage = 0;
        s.Crater = 0;
    }

    /// <summary>Server, after the shells' step: spent stages fall away as loose parts where they were.</summary>
    public static void DropStages(ConstructWorld world)
    {
        if (world.StageDrops.Count == 0)
            return;
        foreach (var drop in world.StageDrops)
        {
            var def = drop.Stage.SpentDef ??= Ordnance.Def(drop.Stage.Spent!);
            if (def == null)
                continue;
            var dir = drop.Vel.LengthSquared() > 1e-4f ? Vector3.Normalize(drop.Vel) : -Vector3.UnitZ;
            var body = world.Create(drop.At, Facing(dir));
            if (body == null)
                continue;
            world.AddPart(body, def, Vector3.Zero, 0);
            body.Owner = drop.By;
            body.Vel = drop.Vel;
            body.Rebuild();
        }
        world.StageDrops.Clear();
    }

    /// <summary>The turn that points -Z (a part's nose) along <paramref name="dir"/>.</summary>
    private static Quaternion Facing(Vector3 dir)
    {
        var from = -Vector3.UnitZ;
        var dot = Vector3.Dot(from, dir);
        if (dot > 0.9999f)
            return Quaternion.Identity;
        if (dot < -0.9999f)
            return Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(from, dir)), MathF.Acos(dot)));
    }
}

/// <summary>The world's side of designed weapons: what a radio-guided one or a remote fuse hears.</summary>
public sealed partial class ConstructWorld
{
    public int ShellRoomLeft => MaxShells - ShellCount;

    /// <summary>The nearest Wireless Sender on a channel within <paramref name="reach"/> of a point (last tick's), and where it is.</summary>
    public bool RadioSenderNear(Vector3 at, int channel, float reach, out Vector3 where)
    {
        where = default;
        var best = reach * reach;
        var found = false;
        for (var k = 0; k < _senderCount; k++)
        {
            ref var s = ref _senders[k];
            var d = Vector3.DistanceSquared(s.At, at);
            if (s.Channel == channel && d <= best)
            {
                best = d;
                where = s.At;
                found = true;
            }
        }
        return found;
    }

    /// <summary>Is any Wireless Sender on this channel on (sending more than a half), anywhere?</summary>
    public bool RadioOn(int channel)
    {
        for (var k = 0; k < _senderCount; k++)
            if (_senders[k].Channel == channel && _senders[k].Value > 0.5f)
                return true;
        return false;
    }
}
