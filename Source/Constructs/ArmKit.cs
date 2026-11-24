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
    Spreader, // (part 2: heads) a beam with two hooks: long pieces
    Vacuum, // ...suction cups: plates and sheets, light
    Magnet, // ...a lifting magnet: steel and iron parts
    Fork, // ...two tines under a piece: heavy, compact
    FitterHead, // ...a gripper, torch and cutter: what a Module Fitter carries
    HeadStand, // a rack of spare heads in an arm's reach: it swaps to the head the next piece needs
}

/// <summary>A head an arm can wear: its own on the Wrist (slot -1) or a spare on a Head Stand in reach.</summary>
public struct ArmHead
{
    public int Part; // the head part
    public ArmPieceKind Kind;
    public float Width, Rating; // its width across (m), and what it holds alone (kg)
    public Vector3 Slot; // a spare's: its stand slot's point in the Core's frame (the hand goes there to swap)
    public Vector3 SlotUp, SlotSide; // ...the slot's up and across in the Core's frame
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
    public readonly float[] ChainP = new float[4]; // (part 2) the chart without a head's own limit: what the chain holds
    public ArmHead[] Heads = []; // (part 2) [0] its own head on the Wrist, then the spares on Head Stands in reach (at most MaxHeads)
    public int Mounted;
    public LifterDef? Def; // the per-part def this spec is applied to (ArmKit.Refresh) // which of Heads is on the Wrist now (the Core's hidden ArmHead setting)
    public string ChainWeak = "";
    public Construct? Body; // (the body it was worked out for, and the Core: a carry's head swap sets the Core's setting)
    public int Core = -1;
}

public static class ArmKit
{
    public const int MaxPedestals = 4, MaxWeights = 4, MaxPacks = 8, MaxHeads = 8;
    public const int SwapTicks = 150; // (part 2) a head swap: 2.5 s, the hand to the stand and back
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
            {
                var d = c.ArmDefs[k].Def;
                if (d.Spec is { Valid: true, Heads.Length: > 1 } s)
                {
                    var want = ConstructConfig.Get(c.Parts[part], c.Parts[part].D, CfgKey.ArmHead, 0);
                    if ((int)MathF.Round(want) != s.Mounted) // (the Head setting changed by hand, or on a client by the body's settings)
                    {
                        Mount(s, want);
                        d.Payload = s.Payload;
                    }
                }
                return d;
            }
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

    /// <summary>Has a piece of a whole arm's chain broken since its spec was made (a body's rebuild after damage is put off)?</summary>
    public static bool ChainBroken(Construct c, ArmSpec s)
    {
        foreach (var i in (ReadOnlySpan<int>)[s.Shoulder, s.UpperBoom, s.Elbow, s.ForeBoom, s.Wrist, s.Head])
            if (i < 0 || i >= c.PartCount || c.Parts[i].Broken)
                return true;
        return false;
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
            spec.Def = d;
            if (spec.Valid)
            {
                Apply(d, ld, spec);
                foreach (var p in new[] { spec.Shoulder, spec.UpperBoom, spec.Elbow, spec.ForeBoom, spec.Wrist, spec.Head })
                    c.ArmPosed[p] = true;
                foreach (var h in spec.Heads)
                    c.ArmPosed[h.Part] = true; // (the spares on stands too: drawn by ArmKitRender, as they change places)
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
        s.Head = NextHead(c, s.Wrist);
        if (s.Head < 0)
            return Fail(s, "fit a head on the Wrist: a Pad Clamp, Spreader, Vacuum Head, Magnet Head, Fork or Fitter Head");
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
        float pHead = float.MaxValue; // (the chain alone; each head's own limit is set against it below)
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
                weak = p == a3 ? "slewing bearing (add Counterweights)" : p == a1 ? (JointRating(Across(J1)) < BoomMoment(Across(B1)) ? "shoulder Joint" : "upper Boom") : (JointRating(Across(J2)) <= BoomMoment(Across(B2)) ? "elbow Joint" : "forearm Boom");
        }
        s.ChainWeak = weak;
        Array.Copy(s.ChartP, s.ChainP, 4);
        s.Body = c;
        s.Core = core;
        Heads(c, core, s);
        Mount(s, ConstructConfig.Get(c.Parts[core], c.Parts[core].D, CfgKey.ArmHead, 0));
        if (s.ChainP[3] < 1)
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
        d.Fitter = file.Fitter || (s.Heads.Length > 0 && s.Heads[0].Kind == ArmPieceKind.FitterHead); // (a Fitter Head built on: it fits modules as a Module Fitter)
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

    // ---- Heads (part 2) ----------------------------------------------------------------------------------------------

    /// <summary>The server's bodies while its lifters step (ConstructLifters.Step), for a swap's dirty mark.</summary>
    internal static ConstructWorld? ServerWorld;

    public static bool IsHead(ArmPieceKind k) => k is ArmPieceKind.PadClamp or ArmPieceKind.Spreader or ArmPieceKind.Vacuum or ArmPieceKind.Magnet or ArmPieceKind.Fork or ArmPieceKind.FitterHead;

    /// <summary>What a head of a kind holds against a Pad Clamp of its width (the clamp's power law).</summary>
    public static float KindFactor(ArmPieceKind k) => k switch
    {
        ArmPieceKind.Spreader => 1.2f,
        ArmPieceKind.Vacuum => 0.4f,
        ArmPieceKind.Magnet => 0.8f,
        ArmPieceKind.Fork => 1.5f,
        _ => 1f,
    };

    public static string KindName(ArmPieceKind k) => k switch
    {
        ArmPieceKind.PadClamp => "Pad Clamp",
        ArmPieceKind.Spreader => "Spreader",
        ArmPieceKind.Vacuum => "Vacuum Head",
        ArmPieceKind.Magnet => "Magnet Head",
        ArmPieceKind.Fork => "Fork",
        ArmPieceKind.FitterHead => "Fitter Head",
        _ => "head",
    };

    /// <summary>
    /// Can a head of a kind and width take a part by its shape and stuff (mass apart)? A Pad Clamp's pads close on its middle side, up
    /// to one and a half times its width; a Spreader's hooks take a piece 2 m long or more, up to three widths across; a Vacuum Head's
    /// cups a plate or sheet (its thin side a tenth of its long side or less); a Magnet Head any steel or iron part; a Fork anything
    /// it can get its tines under (its long side within three widths); a Fitter Head what a clamp takes.
    /// </summary>
    public static bool Fits(ArmPieceKind k, float width, ConstructPartDef d)
    {
        var sz = d.BoundsMax - d.BoundsMin;
        var a = MathF.Max(sz.X, MathF.Max(sz.Y, sz.Z));
        var c = MathF.Min(sz.X, MathF.Min(sz.Y, sz.Z));
        var b = sz.X + sz.Y + sz.Z - a - c;
        return k switch
        {
            ArmPieceKind.Spreader => a >= 2f && b <= width * 3f,
            ArmPieceKind.Vacuum => c <= a * 0.1f + 0.005f && b <= width * 6f,
            ArmPieceKind.Magnet => d.Material is "steel" or "iron" or "metal" && b <= width * 3f,
            ArmPieceKind.Fork => a <= width * 3f,
            _ => b <= width * 1.5f,
        };
    }

    private static int NextHead(Construct c, int wrist)
    {
        var i = On(c, wrist, Sock(c.Parts[wrist].D, "tip"), out var theirs);
        return i >= 0 && IsHead(c.Parts[i].D.ArmPiece) && !c.Parts[i].Broken && Sock(c.Parts[i].D, "base") == theirs ? i : -1;
    }

    private static ArmHead HeadOf(Construct c, int part)
    {
        var d = c.Parts[part].D;
        var w = d.BoundsMax.X - d.BoundsMin.X;
        return new ArmHead { Part = part, Kind = d.ArmPiece, Width = w, Rating = HeadRating(w) * KindFactor(d.ArmPiece) };
    }

    /// <summary>A point and a direction of part <paramref name="i"/>'s frame, in the Core's (both on the body's grid).</summary>
    private static Vector3 InCore(Construct c, int core, int i, Vector3 at, bool dir = false)
    {
        var qi = Rot24.Quat(c.Parts[i].Rot);
        var inv = Quaternion.Conjugate(Rot24.Quat(c.Parts[core].Rot));
        var v = Vector3.Transform(at, qi);
        return dir ? Vector3.Transform(v, inv) : Vector3.Transform(c.Parts[i].Pos + v - c.Parts[core].Pos, inv);
    }

    /// <summary>Its own head, then the spare heads on every Head Stand of its body whose slot lies in its reach.</summary>
    private static void Heads(Construct c, int core, ArmSpec s)
    {
        var list = new List<ArmHead> { HeadOf(c, s.Head) };
        var shoulder = (c.Parts[core].D.Lifter?.Base ?? Vector3.Zero) + new Vector3(0, s.JointA * 0.5f, 0);
        for (var i = 0; i < c.PartCount && list.Count < MaxHeads; i++)
        {
            var sd = c.Parts[i].D;
            if (sd.ArmPiece != ArmPieceKind.HeadStand || c.Parts[i].Broken)
                continue;
            for (var k = 0; k < sd.Sockets.Length && list.Count < MaxHeads; k++)
            {
                if (!sd.Sockets[k].Name.StartsWith("slot", StringComparison.Ordinal))
                    continue;
                var h = On(c, i, k, out var theirs);
                if (h < 0 || !IsHead(c.Parts[h].D.ArmPiece) || c.Parts[h].Broken || Sock(c.Parts[h].D, "base") != theirs)
                    continue;
                var at = InCore(c, core, i, sd.Sockets[k].At);
                var r = new Vector2(at.X - shoulder.X, at.Z - shoulder.Z).Length();
                var wrist = MathF.Sqrt(r * r + (at.Y + 0.6f - shoulder.Y) * (at.Y + 0.6f - shoulder.Y));
                if (r > s.Reach || wrist > (s.Upper + s.Fore) * LifterDef.Straight)
                    continue; // (out of its reach: no spare of this arm's)
                var hd = HeadOf(c, h);
                hd.Slot = at;
                hd.SlotUp = Vector3.Normalize(InCore(c, core, i, SocketDirOf(sd.Sockets[k]), dir: true));
                hd.SlotSide = Vector3.Normalize(InCore(c, core, i, Vector3.UnitZ, dir: true));
                list.Add(hd);
            }
        }
        s.Heads = list.ToArray();
    }

    private static Vector3 SocketDirOf(in CSocket k) => ConstructParts.DirVector(k.Dir);

    /// <summary>The head on the Wrist: the chart and payload are the chain's held to its own limit.</summary>
    public static void Mount(ArmSpec s, float setting)
    {
        s.Mounted = Math.Clamp((int)MathF.Round(setting), 0, Math.Max(0, s.Heads.Length - 1));
        var h = s.Heads.Length > 0 ? s.Heads[s.Mounted] : default;
        for (var k = 0; k < 4; k++)
            s.ChartP[k] = MathF.Min(s.ChainP[k], h.Rating);
        s.Payload = s.ChartP[3];
        s.Weak = s.ChainP[3] > h.Rating ? KindName(h.Kind) : s.ChainWeak;
    }

    /// <summary>What a head holds on this arm: the chain's full-reach chart held to the head's own limit.</summary>
    public static float PayloadWith(ArmSpec s, int head) => MathF.Min(s.ChainP[3], s.Heads[head].Rating);

    /// <summary>The best head for a part, the mounted one first: -1 when none of its heads takes it.</summary>
    public static int HeadFor(ArmSpec s, ConstructPartDef d)
    {
        if (s.Heads.Length == 0)
            return -1;
        if (Takes(s, s.Mounted, d))
            return s.Mounted;
        var best = -1;
        for (var k = 0; k < s.Heads.Length; k++)
            if (k != s.Mounted && Takes(s, k, d) && (best < 0 || s.Heads[k].Rating > s.Heads[best].Rating))
                best = k;
        return best;
    }

    private static bool Takes(ArmSpec s, int k, ConstructPartDef d) => d.Mass <= PayloadWith(s, k) && Fits(s.Heads[k].Kind, s.Heads[k].Width, d);

    /// <summary>
    /// Server, at a loaded carry's start: if the mounted head can't take the piece, the head that can goes on (the Core's ArmHead
    /// setting, sent with its body) and the move waits <see cref="SwapTicks"/> while the hand goes to the stand and back.
    /// </summary>
    public static void SwapFor(LifterDef ld, CLift l, ConstructPartDef d)
    {
        if (ld.Spec is not { Valid: true, Body: { } c } s || s.Core < 0 || s.Core >= c.PartCount)
            return;
        var k = HeadFor(s, d);
        if (k < 0 || k == s.Mounted)
            return;
        SetHead(c, s, k);
        l.Held += SwapTicks; // (the path runs on Start + Held: the swap goes first; Held rides the lift word)
    }

    /// <summary>Puts head <paramref name="k"/> on (a carry's swap, or the Head setting changed by hand).</summary>
    public static void SetHead(Construct c, ArmSpec s, int k)
    {
        ref var p = ref c.Parts[s.Core];
        var slot = p.D.CfgSlot[(int)CfgKey.ArmHead];
        if (slot < 0)
            return;
        p.Cfg[slot] = k;
        Mount(s, k);
        if (s.Def != null)
            s.Def.Payload = s.Payload;
        ServerWorld?.StructureDirty.Add(c.Id); // (the setting goes to clients with the body)
    }

    /// <summary>Every Arm Core has its Head (which head is on: 0 its own, then the spares in reach; set by hand or by a carry's swap).</summary>
    internal static void AddSettings(ConstructPartDef d, List<CConfigDef> list)
    {
        if (d.Lifter is not { Kit: true } || list.Exists(e => e.Id == CfgKey.ArmHead))
            return;
        list.Add(new CConfigDef { Key = ConstructConfig.KeyName(CfgKey.ArmHead), Id = CfgKey.ArmHead, Type = CfgType.Number, Min = 0, Max = MaxHeads - 1, Step = 1, Default = 0, Label = "Head", Group = "Lifter",
            Tooltip = "Which head is on its Wrist: 0 its own, 1 and up the spares on Head Stands in its reach. It swaps by itself when the next piece needs another." });
    }

    /// <summary>The Core's look-line words: its chart, or what its chain lacks.</summary>
    public static string LookText(LifterDef ld)
    {
        if (ld.Spec is not { } s)
            return "";
        if (!s.Valid)
            return $" - not an arm yet: {s.Why}";
        static string Kg(float p) => p >= 1000 ? $"{p / 1000f:0.#} t" : $"{p:0} kg";
        var heads = s.Heads.Length > 1 ? $"; head {KindName(s.Heads[s.Mounted].Kind)} ({s.Mounted + 1} of {s.Heads.Length})" : "";
        return $"{heads} - load chart {Kg(s.ChartP[0])} at {s.ChartR[0]:0.#} m, {Kg(s.ChartP[2])} at {s.ChartR[2]:0.#} m, {Kg(s.ChartP[3])} at {s.ChartR[3]:0.#} m (set by its {s.Weak}); {s.Speed:0.#} m/s, {s.Watts / 1000f:0.#} kW";
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
        var names = at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--") ? args[at + 1].Split(',', StringSplitOptions.TrimEntries) : ["Bench Arm", "Line Arm", "Line Arm Heads", "Heavy Arm", "Yard Arm"];
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
                if (s.Heads.Length > 0)
                {
                    var takes = new int[s.Heads.Length];
                    var none = 0;
                    foreach (var pd in ConstructParts.All)
                    {
                        if (pd == null)
                            continue;
                        var k = ArmKit.HeadFor(s, pd);
                        if (k >= 0) takes[k]++; else none++;
                    }
                    Console.WriteLine($"[arm]   heads: {string.Join(", ", s.Heads.Select((h, k) => $"{ArmKit.KindName(h.Kind)} {h.Width:0.00} m, {h.Rating:0} kg, first pick for {takes[k]} parts"))}; {none} parts none takes");
                }
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
