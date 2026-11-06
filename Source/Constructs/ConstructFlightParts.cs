using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// A lifting surface's numbers: a part file's <c>"surface"</c> block, or an extruded airfoil's row (docs/sandbox/CONSTRUCTS.md,
/// "Wings"). Everything ConstructAero reads is here, in the part's frame, so a new surface is data.
/// </summary>
public sealed class SurfaceDef
{
    public float Area; // m2
    public Vector3 At; // where it acts: the part's frame
    public Vector3 Normal = Vector3.UnitY, Forward = -Vector3.UnitZ; // its lift side, its leading edge
    public float Cl0 = 0.25f; // lift coefficient at no angle of attack
    public float Slope = 4.6f; // lift coefficient a radian (2 pi, less by its aspect and sweep)
    public float Stall = 15 * MathF.PI / 180f; // angle from no lift to the stall, radians
    public float Cd0 = 0.02f; // drag with no lift
    public float Induced = 0.06f; // drag coefficient per lift coefficient squared
    public bool Tears; // cloth: it lifts less as it is shot up
    public float Control; // a control surface: the most it turns, radians (0: none)
    public float ControlRate = 2.5f; // ...how fast, radians a second
    public float Effect = 1; // ...how much of its turn the air sees
    public Vector3 Hinge; // ...where its leaf turns (the part's frame)

    /// <summary>The numbers that follow from an aspect ratio and a sweep (degrees): a lift slope by lifting-line theory, its induced drag.</summary>
    public void Shape(float aspect, float sweep)
    {
        var turn = MathF.Cos(sweep * MathF.PI / 180f);
        Slope = MathF.Tau * aspect / (aspect + 2) * turn;
        Induced = 1f / (MathF.PI * 0.8f * MathF.Max(1, aspect));
    }
}

/// <summary>
/// A propeller, rotor or ducted fan: a part file's <c>"rotor"</c> block, or what a Fan Former made (docs/sandbox/CONSTRUCTS.md,
/// "Propellers and rotors"). Its axis is the way it pushes, from its "at" (the middle of its disc, the part's frame).
/// </summary>
public sealed class RotorDef
{
    public float Radius; // m
    public float RatedKw; // what it takes at its rated speed, full pitch, sea level
    public float RatedOmega; // rad/s
    public float MotorKw; // an electric motor in its hub (0: only a shaft turns it)
    public bool Shaft = true; // it takes a drive shaft
    public int Blades = 2;
    public float BladeHealth = 60;
    public float Chord = 0.15f; // m: a blade's width
    public float Spool = 1.5f; // s from stopped to its rated speed
    public float TopSpeed = 100; // m/s up its axis at which it stops pushing (at its rated speed)
    public float Duct = 1; // what a duct adds to its push
    public bool Heli; // its first input is collective, and it takes cyclic
    public float Cyclic = 8 * MathF.PI / 180f; // radians its push tilts at full cyclic
    public float HubMoment = 0.5f; // the moment a full tilt adds at the hub, as a share of push x radius
    public Vector3 At, Axis = Vector3.UnitZ, Forward = -Vector3.UnitY; // the part's frame: the disc's middle, the push, and which way cyclic pitch tilts it
}

public static partial class ConstructParts
{
    private static Vector3 AirDir(JsonElement b, string key, Vector3 now, string file, string where) =>
        Opt(b, key) is { } v ? DirVector(DirOf(Str(v, file, where + "." + key), file, where + "." + key)) : now;

    /// <summary>The <c>"surface"</c> key (any part).</summary>
    private static void ParseSurface(ConstructPartDef d, JsonElement root, string file)
    {
        if (Opt(root, "surface") is not { } b)
            return;
        var w = $"{file}, surface";
        Known(b, ["area", "at", "lift", "forward", "aspect", "sweep", "cl0", "stall", "cd0", "tears", "control"], w);
        var s = new SurfaceDef { Area = Num(Need(b, "area", w), file, "surface.area") };
        Range(s.Area, 0.01f, 100_000, file, "surface.area");
        s.At = Opt(b, "at") is { } at ? Vec(at, file, "surface.at") : Vector3.Zero;
        s.Normal = AirDir(b, "lift", s.Normal, file, "surface");
        s.Forward = AirDir(b, "forward", s.Forward, file, "surface");
        if (MathF.Abs(Vector3.Dot(s.Normal, s.Forward)) > 0.1f)
            throw new ConstructPartException($"{file}: surface.lift and surface.forward lie across each other");
        var aspect = Opt(b, "aspect") is { } ar ? Num(ar, file, "surface.aspect") : 6f;
        Range(aspect, 0.2f, 40, file, "surface.aspect");
        var sweep = Opt(b, "sweep") is { } sw ? Num(sw, file, "surface.sweep") : 0f;
        Range(sweep, 0, 75, file, "surface.sweep");
        s.Shape(aspect, sweep);
        s.Cl0 = Opt(b, "cl0") is { } c0 ? Num(c0, file, "surface.cl0") : s.Cl0;
        Range(s.Cl0, -1, 2, file, "surface.cl0");
        if (Opt(b, "stall") is { } st)
        {
            var deg = Num(st, file, "surface.stall");
            Range(deg, 3, 45, file, "surface.stall");
            s.Stall = deg * MathF.PI / 180f;
        }
        s.Cd0 = Opt(b, "cd0") is { } cd ? Num(cd, file, "surface.cd0") : s.Cd0;
        Range(s.Cd0, 0.001f, 2, file, "surface.cd0");
        s.Tears = Opt(b, "tears") is { ValueKind: JsonValueKind.True };
        if (Opt(b, "control") is { } ct)
        {
            var cw = $"{file}, surface.control";
            Known(ct, ["max", "rate", "effect", "hinge"], cw);
            var max = Num(Need(ct, "max", cw), file, "surface.control.max");
            Range(max, 1, 80, file, "surface.control.max");
            s.Control = max * MathF.PI / 180f;
            if (Opt(ct, "rate") is { } ra)
            {
                var deg = Num(ra, file, "surface.control.rate");
                Range(deg, 1, 720, file, "surface.control.rate");
                s.ControlRate = deg * MathF.PI / 180f;
            }
            s.Effect = Opt(ct, "effect") is { } ef ? Num(ef, file, "surface.control.effect") : 1f;
            Range(s.Effect, 0, 1, file, "surface.control.effect");
            s.Hinge = Opt(ct, "hinge") is { } hg ? Vec(hg, file, "surface.control.hinge") : s.At;
            d.DataIn = ["angle"];
        }
        d.Surface = s;
    }

    /// <summary>A rotor's block (role <c>rotor</c>; ConstructRotor.cs).</summary>
    private static void ParseRotor(ConstructPartDef d, JsonElement b, string file, string bw)
    {
        Known(b, ["radius", "ratedKw", "ratedRpm", "motorKw", "shaft", "blades", "bladeHealth", "chord", "spool", "topSpeed", "duct", "heli", "cyclic", "hubMoment", "at", "axis", "forward"], bw);
        var r = new RotorDef
        {
            Radius = Num(Need(b, "radius", bw), file, "rotor.radius"),
            RatedKw = Num(Need(b, "ratedKw", bw), file, "rotor.ratedKw"),
            RatedOmega = Num(Need(b, "ratedRpm", bw), file, "rotor.ratedRpm") / ConstructDrive.RpmPerRadS,
        };
        Range(r.Radius, 0.05f, 100, file, "rotor.radius");
        Range(r.RatedKw, 0.01f, 10_000_000, file, "rotor.ratedKw");
        r.MotorKw = Opt(b, "motorKw") is { } mk ? Num(mk, file, "rotor.motorKw") : 0f;
        Range(r.MotorKw, 0, 10_000_000, file, "rotor.motorKw");
        r.Shaft = Opt(b, "shaft") is not { ValueKind: JsonValueKind.False };
        if (!r.Shaft && r.MotorKw <= 0)
            throw new ConstructPartException($"{file}: a rotor runs on a shaft or on its \"motorKw\"");
        r.Blades = Opt(b, "blades") is { } bl ? (int)Num(bl, file, "rotor.blades") : 2;
        Range(r.Blades, 2, ConstructRotor.MaxBlades, file, "rotor.blades");
        r.BladeHealth = Opt(b, "bladeHealth") is { } bh ? Num(bh, file, "rotor.bladeHealth") : 60f;
        Range(r.BladeHealth, 1, 1_000_000, file, "rotor.bladeHealth");
        r.Chord = Opt(b, "chord") is { } ch ? Num(ch, file, "rotor.chord") : r.Radius * 0.12f;
        r.Spool = Opt(b, "spool") is { } sp ? Num(sp, file, "rotor.spool") : 1.5f;
        Range(r.Spool, 0.05f, 60, file, "rotor.spool");
        r.TopSpeed = Opt(b, "topSpeed") is { } ts ? Num(ts, file, "rotor.topSpeed") : 100f;
        Range(r.TopSpeed, 1, 2000, file, "rotor.topSpeed");
        r.Duct = Opt(b, "duct") is { } du ? Num(du, file, "rotor.duct") : 1f;
        Range(r.Duct, 1, 3, file, "rotor.duct");
        r.Heli = Opt(b, "heli") is { ValueKind: JsonValueKind.True };
        if (Opt(b, "cyclic") is { } cy)
        {
            var deg = Num(cy, file, "rotor.cyclic");
            Range(deg, 0, 30, file, "rotor.cyclic");
            r.Cyclic = deg * MathF.PI / 180f;
        }
        r.HubMoment = Opt(b, "hubMoment") is { } hm ? Num(hm, file, "rotor.hubMoment") : r.HubMoment;
        r.At = Opt(b, "at") is { } at ? Vec(at, file, "rotor.at") : Vector3.Zero;
        r.Axis = AirDir(b, "axis", r.Axis, file, "rotor");
        r.Forward = AirDir(b, "forward", r.Forward, file, "rotor");
        if (MathF.Abs(Vector3.Dot(r.Axis, r.Forward)) > 0.1f)
            throw new ConstructPartException($"{file}: rotor.axis and rotor.forward lie across each other");
        d.Rotor = r;
        d.DataIn = r.Heli ? ["collective", "cyclic pitch", "cyclic roll", ConstructModules.Bus] : ["throttle", ConstructModules.Bus];
    }
}
