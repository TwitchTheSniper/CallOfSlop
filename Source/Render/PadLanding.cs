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
    private const int MaxNotes = 4; // pieces on their way in a pad keeps notes of
    private const int MaxMoves = 4; // pieces sliding at once on one pad
    private const double NoteFresh = 1.0; // s a note stands after it was last made
    private const float SlideSpeed = 1.5f; // m/s (the rollers' 1 m/s and a little)
    private const float MaxSlide = 4f; // s

    /// <summary>A piece on its way onto the pad: its kind and last pose (the pad's segment frame), and the pad's columns of its kind then.</summary>
    private sealed class Note
    {
        public ushort Def;
        public int Route; // the conveyor whose lead rider it is (-1: a loose body taken in)
        public Vector3 Pos;
        public Quaternion Rot;
        public int Seg;
        public double Seen;
        public int SnapN;
        public readonly uint[] SnapZ = new uint[MaxSnap]; // (each column's place: ConstructRackPack.PlaceKey)
        public readonly ushort[] SnapDef = new ushort[MaxSnap];
        public readonly byte[] SnapLanes = new byte[MaxSnap * ConstructRackPack.MaxLanes];
    }

    private sealed class Pad
    {
        public int NoteN;
        public readonly Note[] Notes = [new(), new(), new(), new()];
        // Pieces sliding in: their column (kind, near end), stack and layer, and the slide.
        public int MoveN;
        public readonly Move[] Moves = new Move[MaxMoves];
    }

    private struct Move
    {
        public ushort Def;
        public uint At; // its column's place (ConstructRackPack.PlaceKey)
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

    /// <summary>
    /// The lead piece riding into pad <paramref name="pad"/> on conveyor <paramref name="route"/> (-1: a loose body the pad takes in), at
    /// (<paramref name="origin"/>, <paramref name="rot"/>) in segment <paramref name="seg"/>'s frame. A conveyor's note is made again each
    /// frame it is drawn; a body's is added (several may go in one tick: each slides from its own pose).
    /// </summary>
    public static void NoteRider(ConstructWorld world, Construct c, int pad, int route, ushort def, Vector3 origin, Quaternion rot, int seg, double now)
    {
        if (pad >= c.PartCount || world.StoreOf(c, pad) is not { } st)
            return;
        if (!Pads.TryGetValue((c.Id, pad), out var p))
            Pads[(c.Id, pad)] = p = new Pad();
        var at = -1;
        for (var k = 0; k < p.NoteN && route >= 0 && at < 0; k++)
            if (p.Notes[k].Route == route)
                at = k;
        if (at < 0)
        {
            if (p.NoteN == MaxNotes)
            {
                var first = p.Notes[0]; // (full: the oldest goes)
                Array.Copy(p.Notes, 1, p.Notes, 0, MaxNotes - 1);
                p.Notes[MaxNotes - 1] = first;
                p.NoteN--;
            }
            at = p.NoteN++;
        }
        var n = p.Notes[at];
        (n.Def, n.Route, n.Pos, n.Rot, n.Seg, n.Seen) = (def, route, origin, rot, seg, now);
        ConstructRackPack.Ensure(ConstructPads.RackDef(c, pad), st);
        n.SnapN = 0;
        for (var s = 0; s < st.SlabN && n.SnapN < MaxSnap; s++)
        {
            if (def != AnyKind && st.Slabs![s].Def != def)
                continue;
            (n.SnapZ[n.SnapN], n.SnapDef[n.SnapN]) = (ConstructRackPack.PlaceKey(st.Slabs![s]), st.Slabs[s].Def);
            Array.Copy(st.SlabLanes!, s * ConstructRackPack.MaxLanes, n.SnapLanes, n.SnapN * ConstructRackPack.MaxLanes, ConstructRackPack.MaxLanes);
            n.SnapN++;
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
                NoteRider(world, c, pad, -1, def, Vector3.Transform(wp - t, iq), Quaternion.Normalize(iq * wr), seg, now);
                return;
            }
        }
    }

    /// <summary>
    /// Before pad <paramref name="pad"/> is drawn: each note (oldest first) whose kind has a piece on the pad its snapshot didn't have
    /// starts that piece's slide from the note's pose, and is used (one note, one piece). A column is matched to the snapshot by its place,
    /// or by its order among its kind's columns when the columns closed up (<see cref="ConstructRackPack.Place"/>). False when nothing on
    /// this pad slides (it draws as before).
    /// </summary>
    public static bool Arrivals(Construct c, int pad, CStore st, double now)
    {
        if (Pads.Count == 0 || !Pads.TryGetValue((c.Id, pad), out var p))
            return false;
        // (slides done and notes gone stale are dropped)
        for (var m = p.MoveN - 1; m >= 0; m--)
            if (now - p.Moves[m].Start >= p.Moves[m].Time)
                p.Moves[m] = p.Moves[--p.MoveN];
        for (var k = 0; k < p.NoteN; k++)
            if (now - p.Notes[k].Seen >= NoteFresh)
                Drop(p, k--);
        ref readonly var part = ref c.Parts[pad];
        for (var k = 0; k < p.NoteN; k++)
        {
            var n = p.Notes[k];
            if (now <= n.Seen || part.Seg != n.Seg || !NewPiece(c, pad, st, p, n, out var s, out var l, out var layer, out var d))
                continue;
            var rd = ConstructPads.RackDef(c, pad);
            var rot = Rot24.Quat(part.Rot);
            var to = part.Pos + Vector3.Transform(ConstructRackPack.Origin(rd, st.Slabs![s], d, l, layer), rot);
            var mid = (d.BoundsMin + d.BoundsMax) * 0.5f;
            var dist = Vector3.Distance(to + Vector3.Transform(mid, rot * ConstructRackPack.Turn(rd, d)), n.Pos + Vector3.Transform(mid, n.Rot));
            if (p.MoveN < MaxMoves && dist > 0.05f)
                p.Moves[p.MoveN++] = new Move
                {
                    Def = st.Slabs[s].Def, At = ConstructRackPack.PlaceKey(st.Slabs[s]), Lane = (byte)l, Layer = (byte)layer,
                    From = n.Pos, FromRot = n.Rot, Start = now, Time = Math.Clamp(dist / SlideSpeed, 0.2f, MaxSlide),
                };
            Drop(p, k--); // (its piece is in: the note is used)
        }
        return p.MoveN > 0;
    }

    private static void Drop(Pad p, int k)
    {
        var n = p.Notes[k];
        Array.Copy(p.Notes, k + 1, p.Notes, k, p.NoteN - k - 1);
        p.Notes[--p.NoteN] = n;
    }

    /// <summary>A piece of note <paramref name="n"/>'s kind on the pad that its snapshot hadn't and no slide has yet: its column, stack, layer and kind.</summary>
    private static bool NewPiece(Construct c, int pad, CStore st, Pad p, Note n, out int slab, out int lane, out int layer, out ConstructPartDef d)
    {
        (slab, lane, layer, d) = (-1, -1, -1, null!);
        for (var s = 0; s < st.SlabN; s++)
        {
            var def = st.Slabs![s].Def;
            if (n.Def != AnyKind && def != n.Def || ConstructParts.Get(def) is not { } sd)
                continue;
            // (its column then: by place, else by its order among the kind's columns when as many are there as then)
            int was = -1, ord = 0, cols = 0, snapCols = 0;
            for (var q = 0; q < st.SlabN; q++)
                if (st.Slabs[q].Def == def)
                    (ord, cols) = (q < s ? ord + 1 : ord, cols + 1);
            for (var k = 0; k < n.SnapN; k++)
                if (n.SnapDef[k] == def)
                {
                    if (n.SnapZ[k] == ConstructRackPack.PlaceKey(st.Slabs[s]))
                        was = k;
                    snapCols++;
                }
            if (was < 0 && snapCols == cols)
                for (int k = 0, o = 0; k < n.SnapN && was < 0; k++)
                    if (n.SnapDef[k] == def && o++ == ord)
                        was = k;
            for (var l = 0; l < st.Slabs[s].Lanes; l++)
            {
                int now = st.SlabLanes![s * ConstructRackPack.MaxLanes + l], before = was < 0 ? 0 : n.SnapLanes[was * ConstructRackPack.MaxLanes + l];
                for (var y = now - 1; y >= before; y--)
                {
                    var taken = false;
                    for (var m = 0; m < p.MoveN && !taken; m++)
                        taken = p.Moves[m].Def == def && p.Moves[m].At == ConstructRackPack.PlaceKey(st.Slabs[s]) && p.Moves[m].Lane == l && p.Moves[m].Layer == y;
                    if (taken)
                        continue;
                    (slab, lane, layer, d) = (s, l, y, sd);
                    return true;
                }
            }
        }
        return false;
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
            if (mv.Def != slab.Def || mv.At != ConstructRackPack.PlaceKey(slab) || mv.Lane != lane || mv.Layer != layer)
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
