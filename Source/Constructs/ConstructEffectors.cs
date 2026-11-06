using System.Numerics;

namespace CallOfSlop;

/// <summary>What sort of thing an effector is: the panels' count and how its arrow is drawn.</summary>
public enum EffectorKind : byte
{
    Thruster,
    Gimbal,
    Repulsor,
    Rotor,
    Surface,
    Wing, // a surface that lifts: Auto picks plane keys
    Gyro,
}

/// <summary>One thing a stabilizer can command: a throttle, one axis of a nozzle, a flap. Its force and where it acts at a whole unit of command, body frame.</summary>
public struct CEffector
{
    public byte Source; // an index into ConstructEffectors.Sources
    public EffectorKind Kind;
    public ushort Part;
    public byte Channel; // a part with several commands: which
    public float Lo, Hi; // what a command may be
    public Vector3 Force, At, Torque; // N at a unit of command, where (the body's rest frame), and any turn of its own (N m, a gyro's)
    public float Auth; // how much of that it has now: 0 dead, out of fuel or out of air
    public float Cmd; // what it is told, in Lo..Hi
}

/// <summary>
/// One family of parts a stabilizer can use. To add a family: a class here and a line in <see cref="ConstructEffectors.Sources"/>. <see cref="List"/> runs when the structure changes, on
/// server and client, and calls <see cref="FlightEffectors.Add"/> for each command it has (leaving out what a player has wired by hand); the other two run each tick on the server and allocate nothing.
/// </summary>
public interface IEffectorSource
{
    void List(Construct c, FlightEffectors found, byte source);

    /// <summary>How much of its listed force it has now (a jet in thin air, a repulsor high up, a surface at low speed). 0: leave it out.</summary>
    float Authority(Construct c, in CEffector e);

    void Command(Construct c, in CEffector e, float value);
}

/// <summary>What a stabilizer found on its construct: the list, the rows of what each does, and the mix (ConstructFlight.Refit, Mix).</summary>
public sealed class FlightEffectors
{
    public const int Max = 96, Rows = 6;
    public const int Forward = 0, Up = 1, Right = 2, Pitch = 3, Roll = 4, Yaw = 5; // the rows of a demand: N along its axes, N m about them
    public readonly CEffector[] Items = new CEffector[Max];
    public int Count;
    public readonly float[] A = new float[Max * Rows], P = new float[Max * Rows]; // what a unit does to each row (scaled), and the share of each row each takes
    public readonly float[] Scale = new float[Rows]; // what a whole row is, N or N m (0: nothing answers it)
    public readonly float[] Low = new float[Max], High = new float[Max], U = new float[Max];
    public int Thrusters, Gimbals, Repulsors, Rotors, Surfaces, Burners, Vents;
    public bool Wings;
    public ulong AliveA, AliveB; // what the mix was fitted to
    public Vector3 ComFitted;
    public int Can; // bit r: row r answers

    public void Clear()
    {
        Count = 0;
        Thrusters = Gimbals = Repulsors = Rotors = Surfaces = Burners = Vents = 0;
        Wings = false;
        Can = 0;
        AliveA = AliveB = ulong.MaxValue;
    }

    public void Add(byte source, EffectorKind kind, int part, int channel, float lo, float hi, Vector3 force, Vector3 at, Vector3 torque = default)
    {
        if (Count >= Max)
            return;
        Items[Count++] = new CEffector { Source = source, Kind = kind, Part = (ushort)part, Channel = (byte)channel, Lo = lo, Hi = hi, Force = force, At = at, Torque = torque, Auth = 1 };
        switch (kind)
        {
            case EffectorKind.Thruster: Thrusters++; break;
            case EffectorKind.Gimbal: Gimbals++; break;
            case EffectorKind.Repulsor: Repulsors++; break;
            case EffectorKind.Rotor: Rotors++; break;
            case EffectorKind.Surface: Surfaces++; break;
            case EffectorKind.Wing: Surfaces++; Wings = true; break;
        }
    }

    public bool CanDo(int row) => (Can & 1 << row) != 0;
}

public static class ConstructEffectors
{
    /// <summary>Every family (add a line). A source's index goes in <see cref="CEffector.Source"/>.</summary>
    public static readonly IEffectorSource[] Sources = [new ThrusterEffectors(), new GyroEffector()];

    /// <summary>Looks over the construct: the structure changed.</summary>
    public static void Scan(Construct c, FlightEffectors found)
    {
        found.Clear();
        for (var k = 0; k < Sources.Length; k++)
            Sources[k].List(c, found, (byte)k);
        found.Burners = c.BurnerCount;
        found.Vents = c.CellCount;
    }

    /// <summary>The panels' line: "6 thrusters, 2 gimbals, 4 surfaces".</summary>
    public static string Found(FlightEffectors f)
    {
        var s = new System.Text.StringBuilder();
        void Item(int n, string one)
        {
            if (n > 0)
                s.Append(s.Length > 0 ? ", " : "").Append(n).Append(' ').Append(one).Append(n > 1 ? "s" : "");
        }
        Item(f.Thrusters, "thruster");
        Item(f.Gimbals, "gimbal");
        Item(f.Repulsors, "repulsor");
        Item(f.Rotors, "rotor");
        Item(f.Surfaces, "surface");
        Item(f.Burners, "burner");
        return s.Length > 0 ? s.ToString() : "nothing to fly with";
    }
}

/// <summary>Thrusters, the two axes of a nozzle, repulsors. One a wire from anything but its stabilizer sets is left to that wire. Solid boosters can't be commanded.</summary>
public sealed class ThrusterEffectors : IEffectorSource
{
    public void List(Construct c, FlightEffectors found, byte source)
    {
        var stab = c.StabCount > 0 ? c.Stabs[0].Part : -1;
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            ref var t = ref c.Thrusters[k];
            ref var p = ref c.Parts[t.Part];
            var d = p.D;
            if (d.ThrustKind == ThrustKind.Solid)
                continue;
            var byHand = WiredByHand(c, t.Part, stab, 0);
            t.Scanned = !byHand;
            t.GimbalScanned = false;
            if (!byHand)
                found.Add(source, d.ThrustKind == ThrustKind.Repulsor ? EffectorKind.Repulsor : EffectorKind.Thruster, t.Part, 0, 0, 1, t.Dir * d.ThrustForce, t.At);
            if (d.ThrustGimbal <= 0 || WiredByHand(c, t.Part, stab, 1) || WiredByHand(c, t.Part, stab, 2))
                continue;
            t.GimbalScanned = true;
            found.Add(source, EffectorKind.Gimbal, t.Part, 1, -1, 1, Vector3.Cross(t.GimbalX, t.Dir) * (d.ThrustForce * d.ThrustGimbal), t.At);
            found.Add(source, EffectorKind.Gimbal, t.Part, 2, -1, 1, Vector3.Cross(t.GimbalY, t.Dir) * (d.ThrustForce * d.ThrustGimbal), t.At);
        }
    }

    private static bool WiredByHand(Construct c, int part, int stab, int port)
    {
        for (var w = 0; w < c.WireCount; w++)
            if (c.Wires[w].To == part && c.Wires[w].ToPort == port && c.Wires[w].From != stab)
                return true;
        return false;
    }

    private static int Find(Construct c, int part)
    {
        for (var k = 0; k < c.ThrusterCount; k++)
            if (c.Thrusters[k].Part == part)
                return k;
        return -1;
    }

    public float Authority(Construct c, in CEffector e)
    {
        var k = Find(c, e.Part);
        if (k < 0)
            return 0;
        ref var t = ref c.Thrusters[k];
        ref var p = ref c.Parts[e.Part];
        if (!ConstructPhysics.ThrusterLive(c, ref t, p))
            return 0;
        var d = p.D;
        var air = d.ThrustKind == ThrustKind.Air ? ConstructBoost.AirShare(c, c.WorldCom) : 1f;
        if (e.Channel > 0)
            return air * t.Level; // (a nozzle turns what is coming out of it)
        if (d.ThrustKind == ThrustKind.Repulsor)
            return ConstructBoost.Falloff(t.Gap == float.MaxValue ? d.RepulsorRange : t.Gap, d.RepulsorRange);
        return air;
    }

    public void Command(Construct c, in CEffector e, float value)
    {
        var k = Find(c, e.Part);
        if (k < 0)
            return;
        ref var t = ref c.Thrusters[k];
        if (e.Channel == 0)
            t.Command = value;
        else if (e.Channel == 1)
            t.CommandX = value;
        else
            t.CommandY = value;
    }
}

/// <summary>The gyro in the stabilizer that flies the construct: a turn about its own up, nothing else.</summary>
public sealed class GyroEffector : IEffectorSource
{
    public void List(Construct c, FlightEffectors found, byte source)
    {
        if (c.StabCount == 0)
            return;
        ref var p = ref c.Parts[c.Stabs[0].Part];
        if (p.D.Gyro > 0)
            found.Add(source, EffectorKind.Gyro, c.Stabs[0].Part, 0, -1, 1, Vector3.Zero, p.Pos, Vector3.Transform(Vector3.UnitY, Rot24.Quat(p.Rot)) * -p.D.Gyro);
    }

    public float Authority(Construct c, in CEffector e) => c.Parts[e.Part].Working ? 1 : 0;

    public void Command(Construct c, in CEffector e, float value) => c.GyroNow += Vector3.Transform(e.Torque, c.Rot) * value;
}
