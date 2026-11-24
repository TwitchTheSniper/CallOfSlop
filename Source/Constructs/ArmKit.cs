using System.Numerics;

namespace CallOfSlop;

/// <summary>What a piece of a modular loader arm is (its part file's "armPiece"; a pressed copy keeps its like part's).</summary>
public enum ArmPieceKind : byte
{
    None,
    Boom,
    Joint,
    Wrist,
    PadClamp,
    Counterweight,
    DrivePack,
    Pedestal,
}

/// <summary>
/// What an Arm Core's pieces make (docs/sandbox/constructs/production.md "Arm kit"): its chain on the Core's top (shoulder Joint,
/// upper Boom, elbow Joint, fore Boom, Wrist, Pad Clamp), the Counterweights chained on its -x face, the Drive Packs on its and its
/// Pedestals' sides, and the numbers they give: links, reach, the load chart, speed and power. Worked out on each peer when the
/// body's parts change; nothing saved or sent.
/// </summary>
public sealed class ArmSpec
{
    public bool Valid;
    public string Why = ""; // (invalid) what is missing, in words a player acts on
    public int Shoulder = -1, UpperBoom = -1, Elbow = -1, ForeBoom = -1, Wrist = -1, Head = -1;
    public int Pedestals, Weights, Packs;
    public float JointA, ElbowA, UpperLen, ForeLen, WristLen, HeadLen; // the pieces' sizes along the chain (m)
    public float Upper, Fore, Reach, Payload, Speed, Watts, ArmMass, WeightMass;
    public readonly float[] ChartR = new float[4], ChartP = new float[4]; // the load chart: kg at four radii
    public string Weak = ""; // the piece that sets the payload at full reach
}

public static class ArmKit
{
    public const int MaxPedestals = 4, MaxWeights = 4, MaxPacks = 8;
    public const float MaxReach = 32f, FoldSeconds = 6f, G = 9.81f;
    public const float JointMoment = 1.443e6f; // N m per m^3 of a joint's side (a solid steel hub's pin and cheeks)
    public const float BoomStress = 160e6f; // Pa: a boom's working stress
    public const float WeightLever = 3.25f; // m: a counterweight's lever behind the slewing bearing
    public const float CoreKw = 2f, PackKwPerM = 3f, KwPerTonne = 0.8f;
    public const float BearingMoment = 1.5e6f; // N m: the Core's slewing bearing alone

    /// <summary>The kit copy of an Arm Core's lifter def: its chain's numbers (made again when its body's parts change).</summary>
    public static LifterDef DefOf(Construct c, int part, LifterDef file)
    {
        Refresh(c);
        for (var k = 0; k < c.ArmN; k++)
            if (c.ArmDefs[k].Part == part)
                return c.ArmDefs[k].Def;
        return file;
    }

    /// <summary>Is part <paramref name="i"/> a chain piece of a whole arm (posed by LifterRender, no collider of its own)?</summary>
    public static bool Posed(Construct c, int i)
    {
        if (c.Parts[i].D.ArmPiece == ArmPieceKind.None)
            return false;
        Refresh(c);
        return i < c.ArmPosed.Length && c.ArmPosed[i];
    }

    public static void Refresh(Construct c)
    {
        if (c.ArmStamp == c.StructStamp && c.ArmPartCount == c.PartCount)
            return;
        c.ArmStamp = c.StructStamp;
        c.ArmPartCount = c.PartCount;
        c.ArmN = 0;
        if (c.ArmPosed.Length > 0)
            Array.Clear(c.ArmPosed);
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.Parts[i].D.Lifter is not { Kit: true } ld)
                continue;
            if (c.ArmPosed.Length < c.PartCount)
                c.ArmPosed = new bool[c.PartCount + 16];
            var spec = Build(c, i);
            var d = ld.CraneCopy();
            d.Of = ld;
            d.Spec = spec;
            if (spec.Valid)
            {
                Apply(d, ld, spec);
                foreach (var p in new[] { spec.Shoulder, spec.UpperBoom, spec.Elbow, spec.ForeBoom, spec.Wrist, spec.Head })
                    c.ArmPosed[p] = true;
            }
            if (c.ArmDefs.Length <= c.ArmN)
                Array.Resize(ref c.ArmDefs, c.ArmN + 4);
            c.ArmDefs[c.ArmN++] = ((ushort)i, d);
        }
    }

    /// <summary>The part linked on socket <paramref name="socket"/> of <paramref name="part"/>, and the socket of its it meets by; -1: none.</summary>
    private static int On(Construct c, int part, int socket, out int theirs)
    {
        theirs = -1;
        if (socket < 0)
            return -1;
        for (var e = c.FirstLink(part); e >= 0; e = c.NextLink(e))
        {
            ref var l = ref c.Links[e >> 1];
            if ((e & 1) == 0 ? l.SA == socket && l.B < c.PartCount : l.SB == socket && l.A < c.PartCount)
            {
                theirs = (e & 1) == 0 ? l.SB : l.SA;
                return c.OtherEnd(e);
            }
        }
        return -1;
    }

    private static int Sock(ConstructPartDef d, string name)
    {
        for (var s = 0; s < d.Sockets.Length; s++)
            if (d.Sockets[s].Name == name)
                return s;
        return -1;
    }

    /// <summary>The next piece of the chain: on <paramref name="part"/>'s socket, of kind <paramref name="kind"/>, met by its "base".</summary>
    private static int Next(Construct c, int part, string socket, ArmPieceKind kind)
    {
        var i = On(c, part, Sock(c.Parts[part].D, socket), out var theirs);
        return i >= 0 && c.Parts[i].D.ArmPiece == kind && !c.Parts[i].Broken && Sock(c.Parts[i].D, "base") == theirs ? i : -1;
    }

    private static float Along(ConstructPartDef d) => d.BoundsMax.Y - d.BoundsMin.Y;
    private static float Across(ConstructPartDef d) => MathF.Max(d.BoundsMax.X - d.BoundsMin.X, d.BoundsMax.Z - d.BoundsMin.Z);

    /// <summary>A square hollow boom's moment (N m): the section modulus of a w-wide tube with the kit's wall times the working stress.</summary>
    public static float BoomMoment(float w)
    {
        var t = 0.010f * MathF.Pow(w / 0.4f, 0.75f);
        var inner = MathF.Max(0, w - 2 * t);
        return BoomStress * (w * w * w * w - inner * inner * inner * inner) / (6 * w);
    }

    public static float JointRating(float a) => JointMoment * a * a * a;

    /// <summary>A Pad Clamp's own limit (kg), by its width: a power law, held to 120 t.</summary>
    public static float HeadRating(float w) => MathF.Min(120_000f, 2500f * MathF.Pow(MathF.Max(0.2f, w) / 0.6f, 2.5f));

    public static ArmSpec Build(Construct c, int core)
    {
        var s = new ArmSpec();
        var cd = c.Parts[core].D;
        // Pedestals under it, Counterweights on its -x face, Drive Packs on its and its pedestals' sides.
        var below = core;
        while (On(c, below, Sock(c.Parts[below].D, "bottom"), out var ts) is var p and >= 0 && c.Parts[p].D.ArmPiece == ArmPieceKind.Pedestal && Sock(c.Parts[p].D, "top") == ts)
        {
            s.Pedestals++;
            if (s.Pedestals > MaxPedestals)
                return Fail(s, $"at most {MaxPedestals} Arm Pedestals under it");
            CountPacks(c, p, s);
            below = p;
        }
        CountPacks(c, core, s);
        var w = Next(c, core, "weight", ArmPieceKind.Counterweight);
        while (w >= 0)
        {
            if (++s.Weights > MaxWeights)
                return Fail(s, $"at most {MaxWeights} Counterweights on its back");
            var wd = c.Parts[w].D;
            s.WeightMass += (wd.BoundsMax.X - wd.BoundsMin.X) * (wd.BoundsMax.Y - wd.BoundsMin.Y) * (wd.BoundsMax.Z - wd.BoundsMin.Z) * 7850f; // (solid steel plates: the bearing's ballast goes by its volume)
            w = Next(c, w, "back", ArmPieceKind.Counterweight);
        }
        if (s.Packs > MaxPacks)
            return Fail(s, $"at most {MaxPacks} Drive Packs on its sides");
        s.Shoulder = Next(c, core, "chain", ArmPieceKind.Joint);
        if (s.Shoulder < 0)
            return Fail(s, "fit a pressed Arm Joint on its top: the shoulder");
        s.UpperBoom = Next(c, s.Shoulder, "tip", ArmPieceKind.Boom);
        if (s.UpperBoom < 0)
            return Fail(s, "fit an Arm Boom on the shoulder Joint: the upper arm");
        s.Elbow = Next(c, s.UpperBoom, "tip", ArmPieceKind.Joint);
        if (s.Elbow < 0)
            return Fail(s, "fit an Arm Joint on the upper Boom: the elbow");
        s.ForeBoom = Next(c, s.Elbow, "tip", ArmPieceKind.Boom);
        if (s.ForeBoom < 0)
            return Fail(s, "fit an Arm Boom on the elbow Joint: the forearm");
        s.Wrist = Next(c, s.ForeBoom, "tip", ArmPieceKind.Wrist);
        if (s.Wrist < 0)
            return Fail(s, "fit an Arm Wrist on the forearm");
        s.Head = Next(c, s.Wrist, "tip", ArmPieceKind.PadClamp);
        if (s.Head < 0)
            return Fail(s, "fit a Pad Clamp on the Wrist");
        ConstructPartDef J1 = c.Parts[s.Shoulder].D, B1 = c.Parts[s.UpperBoom].D, J2 = c.Parts[s.Elbow].D, B2 = c.Parts[s.ForeBoom].D, Wd = c.Parts[s.Wrist].D, H = c.Parts[s.Head].D;
        s.JointA = Along(J1);
        s.ElbowA = Along(J2);
        s.UpperLen = Along(B1);
        s.ForeLen = Along(B2);
        s.WristLen = Along(Wd);
        s.HeadLen = Along(H);
        s.Upper = s.JointA * 0.5f + s.UpperLen + s.ElbowA * 0.5f;
        s.Fore = s.ElbowA * 0.5f + s.ForeLen;
        if (s.Upper < 0.6f || s.Fore < 0.6f || s.Upper > s.Fore * 3 || s.Fore > s.Upper * 3)
            return Fail(s, "its upper arm and forearm are too unlike to bend: keep the Booms within three times each other's length");
        s.Reach = (s.Upper + s.Fore) * LifterDef.Straight * 0.95f;
        if (s.Reach > MaxReach)
            return Fail(s, $"too long: an arm reaches at most {MaxReach:0} m");
        // ---- The load chart: P(r) = min((T_sh - M_arm) / (g r), (T_el - M_fore) / (g (r - x_el)), (T_bear - M_arm) / (g r), P_head).
        float mU = B1.Mass, mE = J2.Mass, mF = B2.Mass, mH = Wd.Mass + H.Mass;
        s.ArmMass = J1.Mass + mU + mE + mF + mH;
        float tSh = MathF.Min(JointRating(Across(J1)), BoomMoment(Across(B1)));
        float tEl = MathF.Min(JointRating(Across(J2)), MathF.Min(BoomMoment(Across(B2)), JointRating(Across(Wd))));
        float tBear = BearingMoment + s.WeightMass * G * WeightLever;
        float pHead = HeadRating(H.BoundsMax.X - H.BoundsMin.X);
        float R = s.Upper + s.Fore;
        var weak = "";
        for (var k = 0; k < 4; k++)
        {
            var r = s.Reach * (0.4f + 0.2f * k);
            var f = r / R; // (the arm's own weight's levers scale with how far out it is)
            var mArm = G * (mU * s.Upper * 0.5f + mE * s.Upper + mF * (s.Upper + s.Fore * 0.5f) + mH * R) * f;
            var xEl = r * s.Upper / R;
            var mFore = G * (mF * s.Fore * 0.5f + mH * s.Fore) * f;
            float a1 = (tSh - mArm) / (G * r), a2 = (tEl - mFore) / (G * MathF.Max(0.3f, r - xEl)), a3 = (tBear - mArm) / (G * r);
            var p = MathF.Max(0, MathF.Min(MathF.Min(a1, a2), MathF.Min(a3, pHead)));
            s.ChartR[k] = r;
            s.ChartP[k] = p;
            if (k == 3)
                weak = p == pHead ? H.Name : p == a3 ? "slewing bearing (add Counterweights)" : p == a1 ? (JointRating(Across(J1)) < BoomMoment(Across(B1)) ? "shoulder Joint" : "upper Boom") : (JointRating(Across(J2)) <= BoomMoment(Across(B2)) ? "elbow Joint" : "forearm Boom");
        }
        s.Weak = weak;
        s.Payload = s.ChartP[3];
        if (s.Payload < 1)
            return Fail(s, $"it can't hold itself up at full reach: its {weak} is too small for its Booms");
        // ---- Speed: v = v_cap min(1, kW / (0.8 kW/t (m_arm + m_load))), the cap from its biggest joint.
        var cap = 10f / (1 + MathF.Max(s.JointA, s.ElbowA));
        var kw = CoreKw + s.Watts;
        s.Speed = Math.Clamp(cap * MathF.Min(1, kw / (KwPerTonne * (s.ArmMass + s.Payload) / 1000f)), 0.3f, 12f);
        s.Watts = kw * 1000f;
        s.Valid = true;
        return s;
    }

    private static void CountPacks(Construct c, int part, ArmSpec s)
    {
        foreach (var name in new[] { "+z", "-z" })
        {
            var i = Next(c, part, name, ArmPieceKind.DrivePack);
            if (i < 0)
                continue;
            s.Packs++;
            s.Watts += PackKwPerM * Along(c.Parts[i].D); // (kW: summed here, made W in Build)
        }
    }

    private static ArmSpec Fail(ArmSpec s, string why)
    {
        s.Valid = false;
        s.Why = why;
        return s;
    }

    /// <summary>A kit copy's numbers from its chain, in the Core's frame (the chain stands on its "chain" socket).</summary>
    private static void Apply(LifterDef d, LifterDef file, ArmSpec s)
    {
        var top = file.Base; // (the file's base is the chain socket's point: the shoulder's middle is half a joint over it)
        d.Base = top + new Vector3(0, s.JointA * 0.5f, 0);
        d.Ring = top.Y;
        d.Upper = s.Upper;
        d.Fore = s.Fore;
        d.ReachMax = s.Reach;
        d.ReachMin = MathF.Min(s.Reach - 0.5f, MathF.Abs(s.Upper - s.Fore) + LifterDef.Fold + 0.3f);
        d.Up = (s.Upper + s.Fore) * 0.7f;
        d.Down = d.Base.Y + s.Pedestals * 2f + 0.3f;
        d.Payload = s.Payload;
        var hw = s.HeadLen; // (the clamp's width across is its own: read from the part when drawn; here its pads' spread by its length)
        d.PieceAcross = Math.Clamp(s.Reach * 0.25f, 0.5f, 8f);
        d.PieceLong = Math.Clamp(s.Reach, 1f, 32f);
        d.Speed = s.Speed;
        d.Handle = Math.Clamp(0.25f + 0.1f * s.JointA + hw * 0.1f, 0.2f, 2f);
        d.Housing = s.JointA * 2;
    }

    /// <summary>The Core's look-line words: its chart, or what its chain lacks.</summary>
    public static string LookText(LifterDef ld)
    {
        if (ld.Spec is not { } s)
            return "";
        if (!s.Valid)
            return $" - not an arm yet: {s.Why}";
        static string Kg(float p) => p >= 1000 ? $"{p / 1000f:0.#} t" : $"{p:0} kg";
        return $" - load chart {Kg(s.ChartP[0])} at {s.ChartR[0]:0.#} m, {Kg(s.ChartP[2])} at {s.ChartR[2]:0.#} m, {Kg(s.ChartP[3])} at {s.ChartR[3]:0.#} m (set by its {s.Weak}); {s.Speed:0.#} m/s, {s.Watts / 1000f:0.#} kW";
    }
}

/// <summary>A body's Arm Cores' kit defs (ArmKit.cs).</summary>
public sealed partial class Construct
{
    internal int ArmStamp = -1, ArmPartCount = -1;
    internal (ushort Part, LifterDef Def)[] ArmDefs = [];
    internal int ArmN;
    internal bool[] ArmPosed = [];
}

/// <summary><c>--arm-kit [NAME,...]</c>: each named blueprint (default: the four presets) built headless, and its Arm Cores' specs: the chain, the load chart, speed and power, or what it lacks.</summary>
public static class ArmKitCheck
{
    public static int Cli(string[] args)
    {
        var at = Array.FindIndex(args, a => a.Equals("--arm-kit", StringComparison.OrdinalIgnoreCase));
        var names = at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--") ? args[at + 1].Split(',', StringSplitOptions.TrimEntries) : ["Bench Arm", "Line Arm", "Heavy Arm", "Yard Arm"];
        var bad = 0;
        foreach (var name in names)
        {
            if (ConstructBuild.FindBlueprint(name) is not { } bp)
            {
                Console.WriteLine($"[arm] {name}: no such blueprint");
                bad++;
                continue;
            }
            var job = new ConstructBuild.GroundJob(new ConstructWorld(), bp);
            while (!job.Step(1000))
            {
            }
            if (job.Join(System.Numerics.Vector3.Zero, 0, out var why) is not { } c)
            {
                Console.WriteLine($"[arm] {name}: refused: {job.Why ?? why}");
                bad++;
                continue;
            }
            var cores = 0;
            for (var i = 0; i < c.PartCount; i++)
            {
                if (c.Parts[i].D.Lifter is not { Kit: true })
                    continue;
                cores++;
                var ld = ConstructLifters.DefOf(c, i)!;
                var s = ld.Spec!;
                if (!s.Valid)
                {
                    Console.WriteLine($"[arm] {name}: core {i}: not an arm: {s.Why}");
                    bad++;
                    continue;
                }
                var posed = 0;
                for (var k = 0; k < c.PartCount; k++)
                    posed += ArmKit.Posed(c, k) ? 1 : 0;
                Console.WriteLine($"[arm] {name}: {c.PartCount} parts, {posed} posed; upper {s.Upper:0.00} m, fore {s.Fore:0.00} m, reach {ld.ReachMin:0.0} to {ld.ReachMax:0.0} m, up {ld.Up:0.0}, down {ld.Down:0.0}; {s.Pedestals} pedestals, {s.Weights} weights ({s.WeightMass:0} kg), {s.Packs} packs; arm {s.ArmMass:0} kg");
                Console.WriteLine($"[arm]   chart: {string.Join(", ", Enumerable.Range(0, 4).Select(k => $"{s.ChartP[k]:0} kg at {s.ChartR[k]:0.0} m"))} (full reach set by its {s.Weak}); speed {s.Speed:0.00} m/s, {s.Watts / 1000:0.0} kW, handle {ld.Handle:0.00} s, piece {ld.PieceAcross:0.0} x {ld.PieceLong:0.0} m");
            }
            if (cores == 0)
            {
                Console.WriteLine($"[arm] {name}: no Arm Core");
                bad++;
            }
        }
        return bad;
    }
}
