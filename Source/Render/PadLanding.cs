using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A piece rolling off a conveyor onto a Lay-down Pad (a Pad Tile pick pad) slides from where its run left it into its own place on the
/// pad, instead of vanishing at the run's end and showing at once where the pad packed it (Sean, 2026-11-27: "pieces should move into
/// their position on the pad instead of teleporting"). Render only, on every peer, from what each already draws: the lead piece riding
/// into a pad is noted each frame with the pad's columns of its kind (<see cref="NoteRider"/>); when the pad then holds one more of that
/// kind, that new piece is drawn on its way from the rider's last pose to its place (<see cref="Arrivals"/>, <see cref="Moving"/>).
/// Nothing allocates once a pad has been seen; a pad nothing rides into costs a dictionary miss.
/// </summary>
public static class PadLanding
{
    private const int MaxSnap = 16; // columns of one kind a note keeps
    private const int MaxMoves = 4; // pieces sliding at once on one pad
    private const double NoteFresh = 1.0; // s a rider's note stands after it was last drawn
    private const float SlideSpeed = 1.5f; // m/s (the rollers' 1 m/s and a little)
    private const float MaxSlide = 4f; // s

    private sealed class Pad
    {
        // The lead rider into the pad as last drawn (pad's segment frame), and the pad's columns of its kind then.
        public ushort Def = ushort.MaxValue;
        public Vector3 Pos;
        public Quaternion Rot;
        public int Seg;
        public double Seen;
        public int SnapN;
        public readonly ushort[] SnapZ = new ushort[MaxSnap], SnapDef = new ushort[MaxSnap];
        public readonly byte[] SnapLanes = new byte[MaxSnap * ConstructRackPack.MaxLanes];
        // Pieces sliding in: their column (kind, near end), stack and layer, and the slide.
        public int MoveN;
        public readonly Move[] Moves = new Move[MaxMoves];
    }

    private struct Move
    {
        public ushort Def, Z0Mm;
        public byte Lane, Layer;
        public Vector3 From;
        public Quaternion FromRot;
        public double Start;
        public float Time;
    }

    private static readonly Dictionary<(ushort Body, int Part), Pad> Pads = new();
    private const ushort AnyKind = ushort.MaxValue - 1; // (a welded group taken in: a client may not know what it packs into)

    /// <summary>
    /// The pad a conveyor's pieces land on: the pad it ends at, or the pick pad its end machine's Out Table butts (a bed it rolls across
    /// onto a pad, Zero Day Works II's big lines); -1: none.
    /// </summary>
    public static int PadOf(Construct c, int route)
    {
        int to = c.Routes[route].To;
        if (to >= c.PartCount)
            return -1;
        var owner = ConstructPads.Owner(c, to);
        if (owner < c.PartCount && c.DefOf(owner).IsLayDownPad)
            return owner;
        var td = c.DefOf(to);
        if (td.IsPanelFormer && ConstructConveyor.Out(c, to) < 0 && ConstructConveyor.OutPad(c, to) is >= 0 and var pad)
            return ConstructPads.Owner(c, pad);
        return -1;
    }

    /// <summary>The lead piece riding into pad <paramref name="pad"/>, drawn at (<paramref name="origin"/>, <paramref name="rot"/>) in segment <paramref name="seg"/>'s frame.</summary>
    public static void NoteRider(ConstructWorld world, Construct c, int pad, ushort def, Vector3 origin, Quaternion rot, int seg, double now)
    {
        if (pad >= c.PartCount || world.StoreOf(c, pad) is not { } st)
            return;
        if (!Pads.TryGetValue((c.Id, pad), out var p))
            Pads[(c.Id, pad)] = p = new Pad();
        (p.Def, p.Pos, p.Rot, p.Seg, p.Seen) = (def, origin, rot, seg, now);
        ConstructRackPack.Ensure(ConstructPads.RackDef(c, pad), st);
        p.SnapN = 0;
        for (var s = 0; s < st.SlabN && p.SnapN < MaxSnap; s++)
        {
            if (def != AnyKind && st.Slabs![s].Def != def)
                continue;
            (p.SnapZ[p.SnapN], p.SnapDef[p.SnapN]) = (st.Slabs![s].Z0Mm, st.Slabs[s].Def);
            Array.Copy(st.SlabLanes!, s * ConstructRackPack.MaxLanes, p.SnapLanes, p.SnapN * ConstructRackPack.MaxLanes, ConstructRackPack.MaxLanes);
            p.SnapN++;
        }
    }

    /// <summary>Hooks <see cref="ConstructWorld.BodyGone"/> (once): a loose piece a pad takes in slides into its place too.</summary>
    public static void Hook() => ConstructWorld.BodyGone ??= Gone;

    /// <summary>
    /// A body going (either peer): a loose piece or welded group resting over a Lay-down Pad is noted there as a rider is, as it was last
    /// posed, since a pad takes in what rests on it (the Bake Oven's roll-out onto its pad, ConstructLifterPads.PadTakeIn). Bodies go
    /// seldom; this looks over the pads of the bodies near it.
    /// </summary>
    private static void Gone(ConstructWorld world, Construct lo)
    {
        if (lo.PartCount == 0 || lo.PartCount > 256 || lo.Anchored || lo.PieceRackCount > 0)
            return;
        Freeform.PartWorld(lo, 0, out var wp, out var wr);
        var def = lo.PartCount > 1 ? lo.PackDef is { } pd ? (ushort)pd.Id : AnyKind : (ushort)lo.DefOf(0).Id;
        var now = ConstructConveyor.Clock;
        foreach (var c in world.Bodies)
        {
            if (c == lo || c.PieceRackCount == 0 || Vector3.DistanceSquared(c.Pos, wp) > (c.Radius + 20f) * (c.Radius + 20f))
                continue;
            for (var k = 0; k < c.PieceRackCount; k++)
            {
                int pad = c.PieceRacks[k];
                if (pad >= c.PartCount || ConstructPads.Owner(c, pad) != pad || ConstructPads.RackDef(c, pad) is not { IsLayDownPad: true } rd)
                    continue;
                Freeform.PartWorld(c, pad, out var pp, out var pr);
                ConstructRackPack.Hold(rd, out var hlo, out var hhi);
                var at = Vector3.Transform(wp - pp, Quaternion.Conjugate(pr));
                if (at.X < hlo.X - 1f || at.X > hhi.X + 1f || at.Z < hlo.Z - 1f || at.Z > hhi.Z + 1f || at.Y < hlo.Y - 2f || at.Y > hhi.Y + 6f)
                    continue;
                var seg = c.Parts[pad].Seg;
                c.WorldFrame(seg, out var q, out var t);
                var iq = Quaternion.Conjugate(q);
                NoteRider(world, c, pad, def, Vector3.Transform(wp - t, iq), Quaternion.Normalize(iq * wr), seg, now);
                return;
            }
        }
    }

    /// <summary>
    /// Before pad <paramref name="pad"/> is drawn: a piece of the noted rider's kind new on it since the note starts its slide from the
    /// rider's last pose (one rider, one piece). False when nothing on this pad slides (it draws as before).
    /// </summary>
    public static bool Arrivals(Construct c, int pad, CStore st, double now)
    {
        if (Pads.Count == 0 || !Pads.TryGetValue((c.Id, pad), out var p))
            return false;
        // (slides done are dropped)
        for (var m = p.MoveN - 1; m >= 0; m--)
            if (now - p.Moves[m].Start >= p.Moves[m].Time)
                p.Moves[m] = p.Moves[--p.MoveN];
        if (p.Def != ushort.MaxValue && now - p.Seen < NoteFresh && now > p.Seen)
        {
            ref readonly var part = ref c.Parts[pad];
            var rd = ConstructPads.RackDef(c, pad);
            if (part.Seg == p.Seg)
                for (var s = 0; s < st.SlabN && p.Def != ushort.MaxValue; s++)
                {
                    if (p.Def != AnyKind && st.Slabs![s].Def != p.Def || ConstructParts.Get(st.Slabs![s].Def) is not { } d)
                        continue;
                    var was = -1;
                    for (var k = 0; k < p.SnapN; k++)
                        if (p.SnapZ[k] == st.Slabs[s].Z0Mm && p.SnapDef[k] == st.Slabs[s].Def)
                            was = k;
                    for (var l = 0; l < st.Slabs[s].Lanes; l++)
                    {
                        int now2 = st.SlabLanes![s * ConstructRackPack.MaxLanes + l], before = was < 0 ? 0 : p.SnapLanes[was * ConstructRackPack.MaxLanes + l];
                        if (now2 <= before)
                            continue;
                        // The new piece: its place on the pad (segment frame) against where the rider was last drawn.
                        var rot = Rot24.Quat(part.Rot);
                        var to = part.Pos + Vector3.Transform(ConstructRackPack.Origin(rd, st.Slabs[s], d, l, now2 - 1), rot);
                        var mid = (d.BoundsMin + d.BoundsMax) * 0.5f;
                        var dist = Vector3.Distance(to + Vector3.Transform(mid, rot), p.Pos + Vector3.Transform(mid, p.Rot));
                        if (p.MoveN < MaxMoves && dist > 0.05f)
                            p.Moves[p.MoveN++] = new Move
                            {
                                Def = st.Slabs[s].Def, Z0Mm = st.Slabs[s].Z0Mm, Lane = (byte)l, Layer = (byte)(now2 - 1),
                                From = p.Pos, FromRot = p.Rot, Start = now, Time = Math.Clamp(dist / SlideSpeed, 0.2f, MaxSlide),
                            };
                        p.Def = ushort.MaxValue; // (its rider is in: the note is used)
                        break;
                    }
                }
        }
        return p.MoveN > 0;
    }

    /// <summary>
    /// Is the piece at column <paramref name="slab"/>, stack <paramref name="lane"/>, layer <paramref name="layer"/> of pad
    /// <paramref name="pad"/> on its slide in? Then its pose (segment frame) now.
    /// </summary>
    public static bool Moving(Construct c, int pad, in RackSlab slab, int lane, int layer, Vector3 at, Quaternion rot, double now, out Matrix4x4 pose)
    {
        pose = default;
        if (!Pads.TryGetValue((c.Id, pad), out var p))
            return false;
        for (var m = 0; m < p.MoveN; m++)
        {
            ref readonly var mv = ref p.Moves[m];
            if (mv.Def != slab.Def || mv.Z0Mm != slab.Z0Mm || mv.Lane != lane || mv.Layer != layer)
                continue;
            var u = (float)Math.Clamp((now - mv.Start) / mv.Time, 0, 1);
            u = u * u * (3 - 2 * u); // (eased: it starts and stops gently)
            var q = Quaternion.Slerp(mv.FromRot, rot, u);
            pose = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(Vector3.Lerp(mv.From, at, u));
            return true;
        }
        return false;
    }
}
