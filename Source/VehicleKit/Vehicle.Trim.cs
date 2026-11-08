using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// Body styles phase 4 (docs/vehiclekit/BODY-STYLES.md, section 10): a faction's exterior. Each free waist socket of a lofted room's bays may take a trim cut for that
/// bay's side (<see cref="HullLoft.TrimShape"/>: plates and cage bars, slat armour and bins, a fairing, a signboard, clinker laps, a livery band), and the cab and
/// hood their faction's fittings (a sign, a destination board, a radome, a mascot and a chrome grille, a roof basket). The style's sets are data
/// (<see cref="BodyStyleRule.Trims"/>, <see cref="BodyStyleRule.Decos"/>); every roll is on a fork of its own, so nothing else of a seed moves.
/// </summary>
public sealed partial class Vehicle
{
    /// <summary><c>--no-trim</c>: kit 5 bodies with no faction trim or fittings (phase 3's), to measure against.</summary>
    public static bool NoTrim;

    private readonly List<(VPart Trim, VPart Bay, char Kind)> _trims = new();

    private static readonly byte Yaw180 = Rot24.Nearest(Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI), out _);

    public static string TrimName(char k) => k switch
    {
        'p' => "plates", 'c' => "cage", 's' => "slats", 'b' => "bins", 'f' => "fairing", 'g' => "signboards", 'r' => "clinker", 'l' => "livery", _ => k.ToString(),
    };

    public static string DecoName(char k) => k switch
    {
        'S' => "roof sign", 'D' => "destination board", 'R' => "radome", 'O' => "mascot", 'G' => "chrome grille", 'B' => "roof basket", 'J' => "jerry can", _ => k.ToString(),
    };

    /// <summary>A part on <paramref name="on"/>'s socket turned as <paramref name="on"/> is, then by <paramref name="extra"/> (a trim on a bay's -x side: half a turn about its up).</summary>
    private VPart Turned(VPart on, string socket, string part, string by, byte extra, bool clear, Func<ConstructPartDef, ConstructGhost, string?>? veto = null)
    {
        var def = Def(part);
        var target = C.DefOf(on.Index);
        var ts = Array.FindIndex(target.Sockets, s => s.Name == socket);
        var ds = Array.FindIndex(def.Sockets, s => s.Name == by);
        if (ts < 0 || ds < 0)
            throw new VehicleKitException($"a {def.Name}: no socket \"{socket}\" on the {target.Name}, or \"{by}\" on it");
        C.SocketAt(on.Index, ts, out _, out var dir);
        var want = Rot24.Compose(C.Parts[on.Index].Rot, extra);
        Span<byte> turns = stackalloc byte[4];
        var n = ConstructBuild.SpinTurns(def.Sockets[ds].Dir, dir, turns);
        for (var sp = 0; sp < n; sp++)
            if (turns[sp] == want)
                return Onto(on, socket, part, by, sp, clear, veto);
        throw new VehicleKitException($"a {def.Name}: no turn keeps it in the {target.Name}'s frame");
    }

    /// <summary>
    /// Does a part there (<paramref name="g"/>) draw into anything already on the vehicle but <paramref name="skip"/> (a trim stands off its own bay's skin by design)?
    /// Each of its boxes' bounds against every other part's, at rest; a shaped box by its bounds, so a near miss on a slanted face counts as a meet.
    /// </summary>
    private string? DrawnClash(ConstructPartDef def, in ConstructGhost g, HashSet<int> skip)
    {
        C.RestPose(g.Seg, out var q0, out var t0);
        var (lo, hi) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        var mine = new (Vector3 C, Vector3 H)[def.Boxes.Length];
        for (var b = 0; b < def.Boxes.Length; b++)
        {
            var bc = t0 + Vector3.Transform(g.Pos + Rot24.Apply(g.Rot, def.Boxes[b].Center), q0);
            var bh = Rot24.ApplyHalf(g.Rot, def.Boxes[b].Half) - new Vector3(0.002f);
            mine[b] = (bc, bh);
            (lo, hi) = (Vector3.Min(lo, bc - bh), Vector3.Max(hi, bc + bh));
        }
        for (var i = 0; i < C.PartCount; i++)
        {
            if (skip.Contains(i))
                continue;
            var d = C.DefOf(i);
            C.RestPose(C.Parts[i].Seg, out var q, out var t);
            C.PartBounds(i, out var pmn, out var pmx);
            var (pc, ph) = (t + Vector3.Transform((pmn + pmx) / 2, q), Spread(q, (pmx - pmn) / 2));
            if (pc.X + ph.X < lo.X || pc.Y + ph.Y < lo.Y || pc.Z + ph.Z < lo.Z || pc.X - ph.X > hi.X || pc.Y - ph.Y > hi.Y || pc.Z - ph.Z > hi.Z)
                continue;
            for (var k = 0; k < d.Boxes.Length; k++)
            {
                C.BoxOf(i, k, out var oc, out var oh);
                (oc, oh) = (t + Vector3.Transform(oc, q), Spread(q, oh));
                foreach (var (bc, bh) in mine)
                {
                    var gap = Vector3.Abs(bc - oc) - bh - oh;
                    if (gap.X < 0 && gap.Y < 0 && gap.Z < 0)
                        return $"it would draw into the {d.Name}";
                }
            }
        }
        return null;
    }

    /// <summary>A box's half size after a rotation, as axis-aligned bounds.</summary>
    private static Vector3 Spread(Quaternion q, Vector3 h)
    {
        if (q == Quaternion.Identity)
            return h;
        var m = Matrix4x4.CreateFromQuaternion(q);
        return new Vector3(
            MathF.Abs(m.M11) * h.X + MathF.Abs(m.M21) * h.Y + MathF.Abs(m.M31) * h.Z,
            MathF.Abs(m.M12) * h.X + MathF.Abs(m.M22) * h.Y + MathF.Abs(m.M32) * h.Z,
            MathF.Abs(m.M13) * h.X + MathF.Abs(m.M23) * h.Y + MathF.Abs(m.M33) * h.Z);
    }

    /// <summary>How low a trim may come on a bay's side over its floor: clear of any tyre that reaches out under that side (its top and 0.12 m of travel), else 5 cm.</summary>
    private float TrimLow(VPart bay, float side, float floorTop)
    {
        C.PartBounds(bay.Index, out var bmn, out var bmx);
        var (z0, z1) = (FromRest(bmn).Z, FromRest(bmx).Z);
        var low = 0.05f;
        for (var i = 0; i < C.PartCount; i++)
        {
            if (C.DefOf(i).Role is not PartRole.Wheel)
                continue;
            C.PartBounds(i, out var mn, out var mx);
            var (a, b) = (FromRest(mn), FromRest(mx));
            if (b.Z > z0 - 0.15f && a.Z < z1 + 0.15f && (side > 0 ? b.X > 0.85f : a.X < -0.85f))
                low = MathF.Max(low, b.Y - floorTop + 0.12f);
        }
        return MathF.Round(low * 100) / 100;
    }

    /// <summary>The faction's trim down its rooms' free bay sides (after the guns, panniers and dressing have taken theirs), each where it draws into nothing.</summary>
    private void FactionTrim()
    {
        var rule = BodyStyleRule.Of(Style);
        if (NoTrim || Look == null || Rooms.Count == 0 || rule.Trims.Length == 0)
            return;
        var d = Dice.Fork("faction trim");
        var kinds = BodyStyleRule.Weighted(d.Fork("set"), rule.Trims);
        var bays = Rooms.SelectMany(r => r.Bays.Concat(r.Upper?.Bays ?? []).Concat(r.Furniture).Concat(r.Containers).Concat(r.Seats)).Concat(Panels.GetValueOrDefault("furniture") ?? []).Select(b => b.Index).ToHashSet(); // (the skin is between: a shelf's bounds reach the wall)
        var placed = 0;
        foreach (var room in Rooms) // (not an upper storey's: measured, a storey's trim made a faction's rooms differ more among themselves than from the others', phase 5)
            for (var k = 0; k < room.Bays.Count; k++)
            {
                var bay = room.Bays[k];
                if (HullLoft.ShapeOf(bay.Def.Name) is not { } sh || sh.Cage != '\0')
                    continue; // (a caged bay's bars are its exterior)
                foreach (var (socket, side) in new[] { ("+x0", 1f), ("-x0", -1f) })
                {
                    var at = Array.FindIndex(bay.Def.Sockets, s => s.Name == socket);
                    if (at < 0 || C.SocketUsed(bay.Index, at))
                        continue;
                    var bd = d.Fork($"bay {room.Tag} {k} {socket}");
                    if (!bd.Chance(rule.TrimShare))
                        continue;
                    var kind = rule.TrimMix ? kinds[bd.Int(0, kinds.Length - 1)] : kinds[k % kinds.Length];
                    C.SocketAt(bay.Index, at, out _, out var face);
                    var outward = ConstructParts.DirVector(face).X > 0 ? 1f : -1f; // (a turned bay's "+x0" faces the vehicle's -x)
                    var low = TrimLow(bay, outward, room.FloorTop);
                    var shape = side > 0 ? new HullLoft.TrimShape(kind, bd.Int(0, 9), sh.Length, low, sh.A, sh.B) : new HullLoft.TrimShape(kind, bd.Int(0, 9), sh.Length, low, sh.B, sh.A);
                    var key = shape.Key();
                    if (key.Length > ConstructParts.MaxRuntimeName || ConstructParts.Find(key) == null)
                        continue; // (nothing of that kind fits this side: its wall is too short, or all wheel arch)
                    VehicleGenJob.Yield();
                    try
                    {
                        var skip = new HashSet<int>(bays);
                        var trim = Turned(bay, socket, key, "base", side > 0 ? (byte)0 : Yaw180, true, (def, g) => DrawnClash(def, g, skip));
                        PanelList("trim").Add(trim);
                        _trims.Add((trim, bay, kind));
                        placed++;
                    }
                    catch (VehicleKitException)
                    {
                        // (a ladder, a door's leaf, a patch or a gun's arm there: that side stays bare)
                    }
                }
            }
        if (placed > 0)
            Modules.Add($"trim {string.Join("+", kinds.Select(TrimName))} on {placed} bay sides");
    }

    /// <summary>The trims' paint, after the rooms': plates bare metal or old paint, a cage the frame's, a fairing, slats and bins the body colour, a sign the war paint, laps their bay's own, a livery band the second colour.</summary>
    private void PaintTrim()
    {
        var d = Dice.Fork("trim paint");
        var shared = new Dictionary<char, string>();
        foreach (var (trim, bay, kind) in _trims)
        {
            string colour;
            if (kind == 'r')
                colour = ConstructPaint.ToHex(C.Parts[bay.Index].Paint) is { Length: > 0 } own ? own : Style.Colour(d, "rust");
            else if (kind == 'p')
                colour = Style.Colour(d, d.Chance(0.5) ? "body" : "rust"); // (each plate off another wreck)
            else
            {
                if (!shared.TryGetValue(kind, out var c))
                    shared[kind] = c = Style.Colour(d, kind switch { 'c' => "frame", 'g' => Style.Palette.ContainsKey("war") ? "war" : "body", _ => "body" });
                colour = c; // (one colour a vehicle: a livery, a fleet's signs)
            }
            Paint(trim, colour);
        }
        foreach (var p in Panels.GetValueOrDefault("fittings") ?? [])
            if (p.Def.Name.StartsWith(HullLoft.Prefix + HullLoft.DecoWord + "S", StringComparison.Ordinal))
                Paint(p, Style.Colour(d, Style.Palette.ContainsKey("war") ? "war" : "body"));
            else if (p.Def.Name.StartsWith(HullLoft.Prefix + HullLoft.DecoWord + "J", StringComparison.Ordinal))
                Paint(p, Style.Colour(d, "body"));
    }

    /// <summary>The style's fittings on the cab and the hood (a chrome grille goes on with the hood: <see cref="ChromeGrille"/>).</summary>
    private void FactionDecos()
    {
        var rule = BodyStyleRule.Of(Style);
        if (NoTrim || CabLook == null)
            return;
        var cab = Panels.GetValueOrDefault("cabshell")?.FirstOrDefault();
        foreach (var (kind, chance) in rule.Decos)
        {
            var d = Dice.Fork("deco " + kind);
            if (kind == 'G' || !d.Chance(chance))
                continue;
            HullLoft.DecoShape? shape = null;
            VPart? on = null;
            var socket = "top";
            if (kind == 'O' && Panels.GetValueOrDefault("hood")?.FirstOrDefault() is { } hood && HoodShape is { } h)
            {
                // A mascot near the nose, on the hood's skin there (the hood's top socket is over its middle).
                var z = h.Zf + (float)d.Between(0.1, 0.16);
                var u = Math.Clamp((z - h.Zf) / MathF.Max(0.05f, h.Zm - h.Zf), 0, 1);
                var y = h.Front.Top + (h.S.Top - h.Front.Top) * u + 0.02f;
                (on, shape) = (hood, new HullLoft.DecoShape('O', [MathF.Round((z - (h.Zm + h.Zb) / 2) * 100) / 100, MathF.Round((y - (h.S.Top + 0.03f)) * 100) / 100]));
            }
            else if (cab != null && CabShape is { } c && c.Kind != 'g')
            {
                on = cab;
                shape = kind switch
                {
                    'S' => new HullLoft.DecoShape('S', [MathF.Round((CabLook.Width - 0.12f) * 100) / 100, 0.1f, MathF.Round((float)d.Between(0.34, 0.46) * 100) / 100, 0]),
                    'D' when c.Kind is 'o' or 'm' => new HullLoft.DecoShape('D', [0.5f, 0.22f, MathF.Round((c.Zf + c.Rake + 0.1f - (c.Zf + c.Rake + c.Zb) / 2) * 100) / 100, -0.04f]),
                    'R' => new HullLoft.DecoShape('R', [MathF.Round((float)d.Between(0.24, 0.32) * 100) / 100, MathF.Round((float)d.Between(0.18, 0.26) * 100) / 100]),
                    'B' => new HullLoft.DecoShape('B', [0.45f, MathF.Round((float)d.Between(0.3, 0.42) * 100) / 100]),
                    _ => null,
                };
            }
            if (on == null || shape == null || C.SocketUsed(on.Index, Array.FindIndex(on.Def.Sockets, s => s.Name == socket)))
                continue;
            var key = shape.Key();
            if (ConstructParts.Find(key) == null)
                continue;
            try
            {
                var skip = new HashSet<int> { on.Index };
                PanelList("fittings").Add(Turned(on, socket, key, "base", 0, true, (def, g) => DrawnClash(def, g, skip)));
                Modules.Add("fitting " + DecoName(kind));
            }
            catch (VehicleKitException)
            {
                // (a mast, a trophy or a gun already there)
            }
        }
    }

    /// <summary>A Highland chrome grille on a lofted hood's nose, where the plan's nose is a grille or none (a ram or a bull bar keeps the socket), on its own fork.</summary>
    private bool ChromeGrille(VPart hood)
    {
        var rule = BodyStyleRule.Of(Style);
        if (NoTrim || HoodShape is not { } h || !rule.Decos.Any(x => x.Kind == 'G'))
            return false;
        var d = Dice.Fork("deco G");
        if (!d.Chance(rule.Decos.First(x => x.Kind == 'G').Chance))
            return false;
        var front = h.Front;
        var sy = MathF.Max(0.1f, h.Y0 - 0.3f); // (the hood's "front" socket's height: HullLoft.HoodJson)
        var (y0, y1) = (0.08f, front.Top - 0.1f);
        var w = MathF.Min(front.XAt(y0), front.XAt(y1)) - 0.07f;
        if (w < 0.15f || y1 - y0 < 0.15f)
            return false;
        var shape = new HullLoft.DecoShape('G', [MathF.Round(w * 100) / 100, MathF.Round((y0 - sy) * 100) / 100, MathF.Round((y1 - sy) * 100) / 100]);
        if (ConstructParts.Find(shape.Key()) == null)
            return false;
        try
        {
            var skip = new HashSet<int> { hood.Index };
            PanelList("fittings").Add(Turned(hood, "front", shape.Key(), "base", 0, true, (def, g) => DrawnClash(def, g, skip)));
            Modules.Add("fitting chrome grille");
            return true;
        }
        catch (VehicleKitException)
        {
            return false;
        }
    }
}
