using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>The way a leg bends (docs/sandbox/GIANTS.md, "The leg part").</summary>
public enum LegKind : byte
{
    Mech, // knee forward, a straight shin, the foot under it
    Digitigrade, // knee forward, the ankle swept back, walking on its toes
    Insect, // a coxa out from the hip, the knee high above, the foot straight down
}

/// <summary>
/// A walker leg's numbers: a part file's <c>"leg"</c> block (docs/sandbox/GIANTS.md). The part's own boxes are its hip
/// housing; the segments, knees, ankle and foot are posed by the gait brain (ConstructWalk.cs) and drawn by
/// ConstructRenderLegs.cs. Everything here is data, so a new leg is a new file from tools/constructs/leg_parts.py.
/// </summary>
public sealed class LegDef
{
    public const int MaxSegments = 4;
    public LegKind Kind;
    public string Tier = "";
    public float[] Segments = []; // metres, hip first (2 to 4)
    public float Width; // the thickest segment's side, metres
    public float Foot; // the foot pad's half width, metres
    public Vector3 Hip; // the hip's pivot, the part's frame
    public float Load; // kg the leg carries at full pace
    public float StepTime; // seconds a step takes in the air, unloaded
    public float Stride = 0.5f; // how far the body goes while a foot is planted, a share of the reach (the stroke it sweeps, centred on its rest point)
    public float StepHeight = 0.15f; // how high a foot lifts, a share of the reach
    public float Stand = 0.6f; // the hip's height over the ground, a share of the reach
    public float Spread = 0.45f; // the foot's resting distance out from the hip, a share of the reach
    public float Hydraulic, Electric, Shaft; // strength on each power source, a share of the leg's own (0: it doesn't take it)
    public Vector3 JointColour = new(0.18f, 0.18f, 0.19f);
    public Vector3 Colour = new(0.45f, 0.45f, 0.47f); // the segments' (the hip housing's first box)
    public int SegSocket = -1; // the first segment weld socket ("seg0"; one a segment, added at load: ConstructParts.WithLegSockets)
    public Vector3[] RestCentre = []; // each segment's middle on the canonical rest pose (straight down from the hip housing), the part's frame

    /// <summary>Every segment's frame on the canonical rest pose (ConstructPhysics.BoneFrame of a bone straight down, outer face +x).</summary>
    public static readonly Quaternion RestFrame = ConstructPhysics.BoneFrame(Vector3.Zero, -Vector3.UnitY, Vector3.UnitX);

    /// <summary>Hip to foot, every segment straight.</summary>
    public float Reach
    {
        get
        {
            var r = 0f;
            foreach (var s in Segments)
                r += s;
            return r;
        }
    }
}

public static partial class ConstructParts
{
    /// <summary>
    /// A weld socket on each segment of a leg (docs/sandbox/GIANTS.md, phase 3), "seg0" hip first, appended after the file's
    /// own sockets (their indices never shift). They sit on a canonical rest pose, the leg hanging straight down from under
    /// its hip housing with its outer face +x, at each segment's middle on that face; a plain part welded there rides the
    /// segment as it is posed (ConstructWalkRiders.cs). The leg's own size class.
    /// </summary>
    public static CSocket[] WithLegSockets(ConstructPartDef d)
    {
        if (d.Leg is not { } leg || leg.SegSocket >= 0)
            return d.Sockets;
        var n = leg.Segments.Length;
        var add = new CSocket[n];
        leg.RestCentre = new Vector3[n];
        var top = MathF.Min(leg.Hip.Y, d.BoundsMin.Y) - 0.01f;
        for (var s = 0; s < n; s++)
        {
            var mid = MathF.Round((top - leg.Segments[s] * 0.5f) * 100) / 100;
            leg.RestCentre[s] = new Vector3(leg.Hip.X, mid, leg.Hip.Z);
            var face = MathF.Ceiling(ConstructPhysics.LegWidth(leg, s) * 50) / 100; // (half its side, up to the centimetre)
            add[s] = new CSocket("seg" + s, new Vector3(MathF.Round((leg.Hip.X + face) * 100) / 100, mid, MathF.Round(leg.Hip.Z * 100) / 100), 0, SocketKind.Struct, d.Class);
            top -= leg.Segments[s];
        }
        leg.SegSocket = d.Sockets.Length;
        return [.. d.Sockets, .. add];
    }

    private static void ParseLeg(ConstructPartDef d, JsonElement b, string file, string bw)
    {
        Known(b, ["type", "tier", "segments", "width", "foot", "hip", "load", "stepTime", "stride", "stepHeight", "stand", "spread", "power", "jointColor"], bw);
        var leg = new LegDef
        {
            Kind = EnumOf<LegKind>(Str(Need(b, "type", bw), file, "leg.type"), file, "leg.type"),
            Tier = Opt(b, "tier") is { } t ? Str(t, file, "leg.tier") : "",
        };
        var segs = Need(b, "segments", bw);
        if (segs.ValueKind != JsonValueKind.Array || segs.GetArrayLength() is < 2 or > LegDef.MaxSegments)
            throw new ConstructPartException($"{file}: \"leg.segments\" is a list of 2 to {LegDef.MaxSegments} lengths");
        leg.Segments = new float[segs.GetArrayLength()];
        for (var k = 0; k < leg.Segments.Length; k++)
        {
            leg.Segments[k] = Num(segs[k], file, "leg.segments");
            Range(leg.Segments[k], 0.1f, MaxPartReach, file, "leg.segments");
        }
        Range(leg.Reach, 0.2f, MaxPartReach, file, "leg reach (the segments added up)");
        leg.Width = Num(Need(b, "width", bw), file, "leg.width");
        Range(leg.Width, 0.02f, leg.Reach * 0.5f, file, "leg.width");
        leg.Foot = Opt(b, "foot") is { } f ? Num(f, file, "leg.foot") : leg.Width;
        Range(leg.Foot, 0.02f, leg.Reach * 0.5f, file, "leg.foot");
        leg.Hip = Opt(b, "hip") is { } h ? Vec(h, file, "leg.hip") : Vector3.Zero;
        leg.Load = Num(Need(b, "load", bw), file, "leg.load");
        Range(leg.Load, 1, 1e9f, file, "leg.load");
        leg.StepTime = Num(Need(b, "stepTime", bw), file, "leg.stepTime");
        Range(leg.StepTime, 0.1f, 20, file, "leg.stepTime");
        if (Opt(b, "stride") is { } sd)
            leg.Stride = Num(sd, file, "leg.stride");
        Range(leg.Stride, 0.1f, 1.2f, file, "leg.stride");
        if (Opt(b, "stepHeight") is { } sh)
            leg.StepHeight = Num(sh, file, "leg.stepHeight");
        Range(leg.StepHeight, 0.02f, 0.6f, file, "leg.stepHeight");
        if (Opt(b, "stand") is { } st)
            leg.Stand = Num(st, file, "leg.stand");
        Range(leg.Stand, 0.2f, 0.95f, file, "leg.stand");
        if (Opt(b, "spread") is { } sp)
            leg.Spread = Num(sp, file, "leg.spread");
        Range(leg.Spread, 0, 0.8f, file, "leg.spread");
        if (leg.Stand * leg.Stand + leg.Spread * leg.Spread > 0.9f * 0.9f)
            throw new ConstructPartException($"{file}: the leg's stand and spread put the foot past 0.9 of its reach");
        if (Opt(b, "power") is { } pw)
        {
            Known(pw, ["hydraulic", "electric", "shaft"], $"{bw}.power");
            leg.Hydraulic = Opt(pw, "hydraulic") is { } hy ? Num(hy, file, "leg.power.hydraulic") : 0;
            leg.Electric = Opt(pw, "electric") is { } el ? Num(el, file, "leg.power.electric") : 0;
            leg.Shaft = Opt(pw, "shaft") is { } sf ? Num(sf, file, "leg.power.shaft") : 0;
            Range(leg.Hydraulic, 0, 4, file, "leg.power.hydraulic");
            Range(leg.Electric, 0, 4, file, "leg.power.electric");
            Range(leg.Shaft, 0, 4, file, "leg.power.shaft");
        }
        if (Opt(b, "jointColor") is { } jc)
            leg.JointColour = Colour(jc, file, "leg.jointColor");
        if (d.Boxes.Length > 0)
            leg.Colour = d.Boxes[0].Color;
        d.Leg = leg;
    }
}
