using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>Which way a part's face should point when smart rotation guesses its turn (a part file's "faces"; SmartRotation.cs).</summary>
public enum FaceHint : byte
{
    None, // no preference beyond upright
    Forward, // the vehicle's forward (seats, guns, headlights, windscreens)
    Back, // the vehicle's back (tail lights, exhausts)
    Up, // the vehicle's up (an exhaust stack)
    Outward, // away from the construct's centre (panels, armour, doors)
    Side, // away from the vehicle's centre plane, left or right (a wheel's outer face)
    ToSeat, // towards the nearest seat (gauges, dials, screens)
}

/// <summary>
/// Smart rotation's weights (Constructs/SmartRotation.json; docs/sandbox/CONSTRUCTS.md, "Smart rotation"): how much each
/// signal adds to a turn's score. A bad file is refused with <see cref="Problem"/> saying why, and the defaults are used.
/// </summary>
public sealed class SmartRotationWeights
{
    public const string Format = "call-of-slop-smart-rotation/1";

    public float Upright = 2f; // the part's top along the vehicle's up (times the cosine)
    public float Front = 1f; // the part's front along the vehicle's forward (times the cosine)
    public float Facing = 4f; // the part's face the way its hint says (times the cosine)
    public float Mirror = 6f; // the mirror of the same part across the centre plane, exactly
    public float Neighbour = 5f; // the same as the same parts next to it (times the share that match)
    public float LastChoice = 3.5f; // the turn the player last chose for this part (relative to the vehicle)
    public float Learned = 8f; // the player's correction for this part, socket kind and side
    public float CoveredSocket = 1.5f; // taken off for each free socket of another part it would cover (at most 4)
    public float FacingWall = 3f; // taken off when its face would be right against another part
    public string Problem = ""; // why the file was refused ("" when it loaded)

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "SmartRotation.json");

    private static SmartRotationWeights? _current;

    /// <summary>The weights in use (loaded once; the defaults when the file is missing or refused).</summary>
    public static SmartRotationWeights Current => _current ??= Load(FilePath);

    /// <summary>Sets the weights in use (the self-test; null: load the file again).</summary>
    public static void Use(SmartRotationWeights? w) => _current = w;

    public static SmartRotationWeights Load(string path)
    {
        try
        {
            var w = Parse(File.ReadAllText(path));
            if (w.Problem.Length > 0)
                Console.WriteLine($"[sandbox] {Path.GetFileName(path)} refused, smart rotation uses its defaults: {w.Problem}");
            return w;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SmartRotationWeights { Problem = $"{Path.GetFileName(path)}: {ex.Message}" };
        }
    }

    public static SmartRotationWeights Parse(string json)
    {
        var w = new SmartRotationWeights();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new FormatException("the file is not a JSON object");
            if (!root.TryGetProperty("format", out var f) || f.ValueKind != JsonValueKind.String || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            foreach (var p in root.EnumerateObject())
                switch (p.Name)
                {
                    case "format" or "about": break;
                    case "upright": w.Upright = Num(p.Value, p.Name); break;
                    case "front": w.Front = Num(p.Value, p.Name); break;
                    case "facing": w.Facing = Num(p.Value, p.Name); break;
                    case "mirror": w.Mirror = Num(p.Value, p.Name); break;
                    case "neighbour": w.Neighbour = Num(p.Value, p.Name); break;
                    case "lastChoice": w.LastChoice = Num(p.Value, p.Name); break;
                    case "learned": w.Learned = Num(p.Value, p.Name); break;
                    case "coveredSocket": w.CoveredSocket = Num(p.Value, p.Name); break;
                    case "facingWall": w.FacingWall = Num(p.Value, p.Name); break;
                    default:
                        throw new FormatException($"unknown key \"{p.Name}\" (format, about, upright, front, facing, mirror, neighbour, lastChoice, learned, coveredSocket, facingWall)");
                }
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return new SmartRotationWeights { Problem = ex.Message };
        }
        return w;
    }

    private static float Num(JsonElement v, string name) =>
        v.ValueKind == JsonValueKind.Number && v.GetSingle() is var f && float.IsFinite(f) && f >= 0 && f <= 100
            ? f
            : throw new FormatException($"\"{name}\" is a number from 0 to 100");
}

/// <summary>
/// What smart rotation remembers for one player (the client): the last turn chosen for each part, and the corrections
/// (a turn chosen other than the guess) for each part, socket kind and side of the vehicle, all relative to the vehicle's
/// frame. The corrections are saved in settings.txt (<see cref="Settings.SmartRotationLearned"/>); the last choices last
/// the session. Lookups don't allocate.
/// </summary>
public sealed class SmartRotationMemory
{
    public const int MaxLearned = 256; // the oldest correction goes first beyond this
    public const byte FreeKind = 255; // the socket kind of free placement (set down on a surface)

    private readonly Dictionary<int, byte> _last = new();
    private readonly Dictionary<int, byte> _learned = new();
    private readonly List<int> _order = new(); // the learned keys, oldest first

    public int LearnedCount => _learned.Count;

    public static int Key(ushort part, byte kind, byte side) => part << 16 | kind << 8 | side;

    public bool TryLast(ushort part, out byte rel) => _last.TryGetValue(part, out rel);

    public bool TryLearned(int key, out byte rel) => _learned.TryGetValue(key, out rel);

    public void Clear()
    {
        _last.Clear();
        _learned.Clear();
        _order.Clear();
    }

    /// <summary>The player set the part down at <paramref name="chosen"/> where the guess was <paramref name="guess"/> (both relative to the vehicle). True when a correction was learned.</summary>
    public bool Placed(ushort part, int key, byte chosen, byte guess)
    {
        _last[part] = chosen;
        if (chosen == guess)
            return false;
        if (_learned.TryGetValue(key, out var had) && had == chosen)
            return false;
        if (!_learned.ContainsKey(key))
        {
            _order.Add(key);
            if (_order.Count > MaxLearned)
            {
                _learned.Remove(_order[0]);
                _order.RemoveAt(0);
            }
        }
        _learned[key] = chosen;
        return true;
    }

    /// <summary>The corrections as settings lines' entries: "Part Name|kind|side|turn" (part names, so they outlive part ids).</summary>
    public List<string> Save()
    {
        var list = new List<string>(_order.Count);
        foreach (var key in _order)
            if (ConstructParts.Get((ushort)(key >> 16)) is { } d && d.Name.IndexOfAny(['|', ',']) < 0)
                list.Add(string.Create(CultureInfo.InvariantCulture, $"{d.Name}|{key >> 8 & 0xff}|{key & 0xff}|{_learned[key]}"));
        return list;
    }

    /// <summary>Loads corrections saved by <see cref="Save"/> (unknown parts and bad entries are skipped).</summary>
    public void Load(IEnumerable<string> entries)
    {
        foreach (var e in entries)
        {
            var f = e.Split('|');
            if (f.Length != 4 || ConstructParts.Find(f[0]) is not { } d
                || !byte.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out var kind) || (kind > (byte)SocketKind.Leaf && kind != FreeKind)
                || !byte.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out var side) || side > 5
                || !byte.TryParse(f[3], NumberStyles.None, CultureInfo.InvariantCulture, out var turn) || turn >= Rot24.Count)
                continue;
            var key = Key(d.Id, kind, side);
            if (!_learned.ContainsKey(key))
            {
                if (_order.Count >= MaxLearned)
                    continue;
                _order.Add(key);
            }
            _learned[key] = turn;
        }
    }
}

/// <summary>A construct's vehicle frame for smart rotation: its forward and up (rest frame), and the centre plane.</summary>
public struct SmartFrame
{
    public byte Turn, Inv; // the frame as a Rot24 turn (-Z forward, +Y up), and its inverse
    public byte FwdDir, UpDir, RightDir; // direction indices (+x 0, -x 1...)
    public Vector3 Fwd, Up, Right;
    public Vector3 Plane; // a point on the centre plane (its normal is Right): the frame part's centre
    public Vector3 Centre; // the construct's centre (the parts' centres averaged)
}

/// <summary>
/// Smart rotation (board item dev-sandbox-smart-rotation-sr01, decision 22; docs/sandbox/CONSTRUCTS.md "Smart rotation"):
/// the client's guess at the way round a held part should go. Every turn that fits a socket (or, for free placement, all
/// 24) is scored by context, with the weights in Constructs/SmartRotation.json: upright and facing forward in the
/// vehicle's frame (the driver seat, else the first seat, else the root part; an anchored build takes the world's up),
/// the part's facing hint (its file's "faces", else its role's default), the mirror of the same part across the centre
/// plane, the same parts next to it, the player's last choice and corrections, and clearance (a blocked turn is never a
/// candidate; covering another part's free sockets or facing into a part scores lower). Ties break by the old fixed
/// upright order, then the lowest variant, so the guess is deterministic. Only the guess is new: the server still checks
/// the variant the client sends, exactly as before (no wire change). Nothing allocates.
/// </summary>
public static class SmartRotation
{
    public const int MaxFreeSockets = 768; // the free sockets clearance looks at (more are ignored)

    /// <summary>This player's memory (the client's; Program loads the saved corrections into it).</summary>
    public static readonly SmartRotationMemory Memory = new();

    private static readonly byte[] Inverse = BuildInverse();

    private static byte[] BuildInverse()
    {
        var inv = new byte[Rot24.Count];
        for (byte r = 0; r < Rot24.Count; r++)
            for (byte k = 0; k < Rot24.Count; k++)
                if (Rot24.Compose(k, r) == 0)
                    inv[r] = k;
        return inv;
    }

    public static byte Invert(byte r) => Inverse[r % Rot24.Count];

    /// <summary>The turn taking -Z to <paramref name="fwd"/> and +Y to <paramref name="up"/> (direction indices; 0 when they are parallel).</summary>
    public static byte FromDirs(byte fwd, byte up)
    {
        for (byte r = 0; r < Rot24.Count; r++)
            if (Rot24.ApplyDir(r, 5) == fwd && Rot24.ApplyDir(r, 2) == up)
                return r;
        return 0;
    }

    private static byte MirrorDir(byte dir, byte rightDir) => (dir >> 1) == (rightDir >> 1) ? (byte)(dir ^ 1) : dir;

    /// <summary>The second axis the mirror and the free sockets compare (+Y, or -Z when the face is along Y).</summary>
    private static byte SecondAxis(byte face) => (face >> 1) == 1 ? (byte)5 : (byte)2;

    // ---- Part files -------------------------------------------------------------------------------------------------

    private static readonly string[] HintNames = ["none", "forward", "back", "up", "outward", "side", "toSeat"];

    /// <summary>A part file's "faces" and "faceAxis" (both optional; the role's default otherwise).</summary>
    public static void ParseFaces(ConstructPartDef d, JsonElement? faces, JsonElement? axis, string file)
    {
        RoleDefault(d, out d.Faces, out d.FaceAxis);
        if (faces is { } f)
        {
            var i = f.ValueKind == JsonValueKind.String ? Array.FindIndex(HintNames, n => n.Equals(f.GetString(), StringComparison.OrdinalIgnoreCase)) : -1;
            if (i < 0)
                throw new ConstructPartException($"{file}: \"faces\" is one of {string.Join(", ", HintNames)}");
            d.Faces = (FaceHint)i;
        }
        if (axis is { } a)
            d.FaceAxis = a.ValueKind == JsonValueKind.String ? ConstructParts.DirOf(a.GetString() ?? "", file, "faceAxis") : throw new ConstructPartException($"{file}: \"faceAxis\" is a direction (+x, -x, +y, -y, +z, -z)");
    }

    /// <summary>A role's facing hint and face: seats and guns face forward, wheels put their outer face (+x) to the side, panels their front (+y) outward, doors theirs (+z), gauges their dial towards the seat.</summary>
    public static void RoleDefault(ConstructPartDef d, out FaceHint hint, out byte axis)
    {
        (hint, axis) = d.Role switch
        {
            PartRole.Seat or PartRole.Gun => (FaceHint.Forward, (byte)5),
            PartRole.Wheel => (FaceHint.Side, (byte)0),
            PartRole.Panel => (FaceHint.Outward, (byte)2),
            PartRole.Door => (FaceHint.Outward, (byte)4),
            PartRole.Logic when d.HasDial => (FaceHint.ToSeat, d.DialDir),
            _ => (FaceHint.None, (byte)5),
        };
    }

    // ---- The vehicle frame ------------------------------------------------------------------------------------------

    /// <summary>The part the vehicle frame comes from: the driver's seat, else the first seat, else the root part.</summary>
    public static int FramePart(Construct c)
    {
        if (c.DriverSeat >= 0 && c.DriverSeat < c.PartCount && c.DefOf(c.DriverSeat).Role == PartRole.Seat)
            return c.DriverSeat;
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).Role == PartRole.Seat)
                return i;
        return c.Root >= 0 && c.Root < c.PartCount ? c.Root : 0;
    }

    public static SmartFrame FrameOf(Construct c)
    {
        var f = default(SmartFrame);
        if (c.PartCount == 0)
        {
            Fill(ref f, 0);
            return f;
        }
        var fp = FramePart(c);
        var t0 = c.Parts[fp].Rot;
        var fwd = Rot24.ApplyDir(t0, 5);
        var up = Rot24.ApplyDir(t0, 2);
        if (c.Anchored || c.Pinned)
        {
            // An anchored build: the world's up (its front from the frame part, squared to it).
            up = GhostTurn.Snap(Vector3.Transform(Vector3.UnitY, Quaternion.Conjugate(Quaternion.Normalize(c.Rot))));
            if ((fwd >> 1) == (up >> 1))
                fwd = (Rot24.ApplyDir(t0, 3) >> 1) != (up >> 1) ? Rot24.ApplyDir(t0, 3) : Rot24.ApplyDir(t0, 0);
        }
        Fill(ref f, FromDirs(fwd, up));
        c.PartBounds(fp, out var mn, out var mx);
        f.Plane = (mn + mx) * 0.5f;
        var sum = Vector3.Zero;
        for (var i = 0; i < c.PartCount; i++)
        {
            c.PartBounds(i, out var a, out var b);
            sum += (a + b) * 0.5f;
        }
        f.Centre = sum / c.PartCount;
        return f;
    }

    private static void Fill(ref SmartFrame f, byte turn)
    {
        f.Turn = turn;
        f.Inv = Invert(turn);
        f.FwdDir = Rot24.ApplyDir(turn, 5);
        f.UpDir = Rot24.ApplyDir(turn, 2);
        f.RightDir = Rot24.ApplyDir(turn, 0);
        f.Fwd = ConstructParts.DirVector(f.FwdDir);
        f.Up = ConstructParts.DirVector(f.UpDir);
        f.Right = ConstructParts.DirVector(f.RightDir);
    }

    // ---- Ranking a socket's turns ------------------------------------------------------------------------------------

    /// <summary>
    /// The ways round <paramref name="def"/> fits socket <paramref name="socket"/> of part <paramref name="part"/>, best
    /// first: one variant (own socket * 4 + spin, the lowest giving that turn: the one <see cref="GhostTurn.AttachVariant"/>
    /// returns) per distinct turn into <paramref name="variants"/>, and its turn into <paramref name="rots"/>. Only turns
    /// that fit are listed (a blocked one never is). <paramref name="onlyDefSocket"/> limits it to one of the part's own
    /// sockets (a blueprint step's "by"). <paramref name="memory"/> null: context only. Returns how many (0: none fits).
    /// </summary>
    public static int Rank(Construct c, ConstructPartDef def, int part, int socket, SmartRotationMemory? memory, Span<int> variants, Span<byte> rots, int onlyDefSocket = -1)
    {
        Span<int> variantOf = stackalloc int[Rot24.Count];
        Span<float> score = stackalloc float[Rot24.Count];
        variantOf.Fill(-1);
        var total = def.Sockets.Length * 4;
        var any = false;
        for (var v = 0; v < total; v++)
        {
            if (onlyDefSocket >= 0 && v / 4 != onlyDefSocket)
                continue;
            if (ConstructBuild.Place(c, part, socket, def, v / 4, v % 4, out var g) == null && variantOf[g.Rot] < 0)
            {
                variantOf[g.Rot] = v;
                any = true;
            }
        }
        if (!any)
            return 0;

        var w = SmartRotationWeights.Current;
        var f = FrameOf(c);
        c.SocketAt(part, socket, out var sockAt, out var sockDir);
        var ts = c.DefOf(part).Sockets[socket % c.DefOf(part).Sockets.Length];
        var side = Rot24.ApplyDir(f.Inv, sockDir);
        var learnedKey = SmartRotationMemory.Key(def.Id, (byte)ts.Kind, side);
        var hasLast = false;
        byte last = 0, learned = 0;
        var hasLearned = false;
        if (memory != null)
        {
            hasLast = memory.TryLast(def.Id, out last);
            hasLearned = memory.TryLearned(learnedKey, out learned);
        }

        // The free sockets of the parts already on (for clearance), once.
        Span<Vector3> freeAt = stackalloc Vector3[MaxFreeSockets];
        Span<byte> freeDir = stackalloc byte[MaxFreeSockets];
        var free = 0;
        for (var i = 0; i < c.PartCount && free < MaxFreeSockets; i++)
        {
            if (c.Parts[i].Broken)
                continue;
            for (var s = 0; s < c.SocketCount(i) && free < MaxFreeSockets; s++)
            {
                if (i == part && s == socket || c.SocketUsed(i, s))
                    continue;
                c.SocketAt(i, s, out freeAt[free], out freeDir[free]);
                if (Vector3.DistanceSquared(freeAt[free], sockAt) > 0.0001f)
                    free++;
            }
        }

        var scale = ConstructMerge.ScaleFor(c, part, socket, def);
        var k = scale > 1 ? scale : 1f;
        var localCentre = (def.BoundsMin + def.BoundsMax) * 0.5f * k;
        var localHalf = (def.BoundsMax - def.BoundsMin) * 0.5f * k;
        var faceAxis = def.FaceAxis;
        var second = SecondAxis(faceAxis);
        var reach = MathF.Max(localHalf.X, MathF.Max(localHalf.Y, localHalf.Z)) * 2 + 0.6f;
        var n = 0;
        for (byte rot = 0; rot < Rot24.Count; rot++)
        {
            if (variantOf[rot] < 0)
                continue;
            var v = variantOf[rot];
            ConstructBuild.Place(c, part, socket, def, v / 4, v % 4, out var g);
            var pc = g.Pos + Rot24.Apply(rot, localCentre);
            var faceDir = Rot24.ApplyDir(rot, faceAxis);
            var face = ConstructParts.DirVector(faceDir);
            var s = w.Upright * Vector3.Dot(Rot24.Apply(rot, Vector3.UnitY), f.Up) + w.Front * Vector3.Dot(Rot24.Apply(rot, -Vector3.UnitZ), f.Fwd);

            // The facing hint.
            if (def.Faces != FaceHint.None)
                s += w.Facing * Vector3.Dot(face, FaceTarget(c, def.Faces, in f, pc, sockDir));

            // The mirror across the centre plane, else the same parts next to it.
            var off = Vector3.Dot(pc - f.Plane, f.Right);
            var mirror = -1;
            if (MathF.Abs(off) > 0.05f)
            {
                var pm = pc - 2 * off * f.Right;
                for (var j = 0; j < c.PartCount && mirror < 0; j++)
                    if (c.Parts[j].Def == def.Id && c.Parts[j].Seg == g.Seg && Vector3.DistanceSquared(CentreOf(c, j), pm) < 0.0036f)
                        mirror = j;
                if (mirror >= 0)
                {
                    var rj = c.Parts[mirror].Rot;
                    if (Rot24.ApplyDir(rot, faceAxis) == MirrorDir(Rot24.ApplyDir(rj, faceAxis), f.RightDir) && Rot24.ApplyDir(rot, second) == MirrorDir(Rot24.ApplyDir(rj, second), f.RightDir))
                        s += w.Mirror;
                }
            }
            int near = 0, same = 0;
            for (var j = 0; j < c.PartCount; j++)
                if (j != mirror && c.Parts[j].Def == def.Id && c.Parts[j].Seg == g.Seg && Vector3.DistanceSquared(CentreOf(c, j), pc) < reach * reach)
                {
                    near++;
                    if (c.Parts[j].Rot == rot)
                        same++;
                }
            if (near > 0)
                s += w.Neighbour * same / near;

            // What the player chose before (relative to the vehicle).
            var rel = Rot24.Compose(f.Inv, rot);
            if (hasLast && rel == last)
                s += w.LastChoice;
            if (hasLearned && rel == learned)
                s += w.Learned;

            // Clearance: free sockets it would cover (unless it meets them with a socket of its own), and its face against a part.
            var covered = 0;
            for (var q = 0; q < free && covered < 4; q++)
                if (InsideSolid(def, g.Pos, rot, k, freeAt[q], 0.02f) && !Meets(def, g.Pos, rot, k, freeAt[q], freeDir[q]))
                    covered++;
            s -= w.CoveredSocket * covered;
            if (def.Faces != FaceHint.None && FacesWall(c, pc, face, Rot24.ApplyHalf(rot, localHalf)))
                s -= w.FacingWall;
            score[rot] = s;
            variants[n] = v;
            rots[n] = rot;
            n++;
            if (n >= variants.Length || n >= rots.Length)
                break;
        }

        // Best first; ties by the old fixed order (upright first, then facing forward), then the lowest variant.
        for (var a = 1; a < n; a++)
            for (var b = a; b > 0 && Before(rots[b], variants[b], rots[b - 1], variants[b - 1], score); b--)
            {
                (rots[b], rots[b - 1]) = (rots[b - 1], rots[b]);
                (variants[b], variants[b - 1]) = (variants[b - 1], variants[b]);
            }
        return n;
    }

    private static bool Before(byte ra, int va, byte rb, int vb, ReadOnlySpan<float> score)
    {
        var d = score[ra] - score[rb];
        if (MathF.Abs(d) > 1e-4f)
            return d > 0;
        var u = OldUpright(ra) - OldUpright(rb);
        if (MathF.Abs(u) > 1e-4f)
            return u > 0;
        return va < vb;
    }

    /// <summary>The old fixed order's score (ConstructBuild.Place sorts a socket's four spins by it): upright, then facing -Z, in the rest frame.</summary>
    public static float OldUpright(byte r) => Rot24.Apply(r, Vector3.UnitY).Y * 2 + Rot24.Apply(r, -Vector3.UnitZ).Z * -1 + Rot24.Apply(r, Vector3.UnitX).X * 0.1f;

    private static Vector3 CentreOf(Construct c, int i)
    {
        c.PartBounds(i, out var a, out var b);
        return (a + b) * 0.5f;
    }

    private static Vector3 FaceTarget(Construct c, FaceHint hint, in SmartFrame f, Vector3 pc, byte sockDir)
    {
        switch (hint)
        {
            case FaceHint.Forward: return f.Fwd;
            case FaceHint.Back: return -f.Fwd;
            case FaceHint.Up: return f.Up;
            case FaceHint.Outward:
            {
                var d = pc - f.Centre;
                return d.LengthSquared() > 0.0025f ? Vector3.Normalize(d) : ConstructParts.DirVector(sockDir);
            }
            case FaceHint.Side:
            {
                var off = Vector3.Dot(pc - f.Plane, f.Right);
                return MathF.Abs(off) > 0.05f ? f.Right * MathF.Sign(off) : Vector3.Zero;
            }
            case FaceHint.ToSeat:
            {
                var best = float.MaxValue;
                var to = -f.Fwd; // (no seat yet: towards where the driver will sit, behind it)
                for (var i = 0; i < c.PartCount; i++)
                    if (c.DefOf(i).Role == PartRole.Seat)
                    {
                        var head = CentreOf(c, i) + f.Up * 0.5f;
                        var d = head - pc;
                        if (d.LengthSquared() < best && d.LengthSquared() > 1e-4f)
                        {
                            best = d.LengthSquared();
                            to = Vector3.Normalize(d);
                        }
                    }
                return to;
            }
            default: return Vector3.Zero;
        }
    }

    private static bool InsideSolid(ConstructPartDef def, Vector3 pos, byte rot, float k, Vector3 p, float pad)
    {
        for (var b = 0; b < def.SolidBoxes; b++)
        {
            var bc = pos + Rot24.Apply(rot, def.Boxes[b].Center * k);
            var bh = Rot24.ApplyHalf(rot, def.Boxes[b].Half * k) + new Vector3(pad);
            if (MathF.Abs(p.X - bc.X) <= bh.X && MathF.Abs(p.Y - bc.Y) <= bh.Y && MathF.Abs(p.Z - bc.Z) <= bh.Z)
                return true;
        }
        return false;
    }

    /// <summary>Has the part, so placed, a socket of its own at <paramref name="p"/> facing <paramref name="dir"/> (they would weld, not be covered)?</summary>
    private static bool Meets(ConstructPartDef def, Vector3 pos, byte rot, float k, Vector3 p, byte dir)
    {
        for (var s = 0; s < def.Sockets.Length; s++)
            if (Rot24.ApplyDir(rot, def.Sockets[s].Dir) == (dir ^ 1) && Vector3.DistanceSquared(pos + Rot24.Apply(rot, def.Sockets[s].At * k), p) < 0.0004f)
                return true;
        return false;
    }

    /// <summary>Is there a part right in front of the face (within 0.4 m of it)?</summary>
    private static bool FacesWall(Construct c, Vector3 pc, Vector3 face, Vector3 half)
    {
        var start = pc + face * (MathF.Abs(Vector3.Dot(half, face)) + 0.02f);
        for (var step = 0; step < 3; step++)
        {
            var p = start + face * (0.05f + step * 0.17f);
            for (var i = 0; i < c.PartCount; i++)
            {
                var d = c.DefOf(i);
                for (var b = 0; b < d.SolidBoxes; b++)
                {
                    c.BoxOf(i, b, out var oc, out var oh);
                    if (MathF.Abs(p.X - oc.X) < oh.X && MathF.Abs(p.Y - oc.Y) < oh.Y && MathF.Abs(p.Z - oc.Z) < oh.Z)
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>The best spin (0..3) for a blueprint step whose "spin" is "auto": context only (no memory), so every machine builds it the same. -1: none fits.</summary>
    public static int BestSpin(Construct c, int part, int socket, ConstructPartDef def, int defSocket)
    {
        Span<int> v = stackalloc int[Rot24.Count];
        Span<byte> r = stackalloc byte[Rot24.Count];
        return Rank(c, def, part, socket, null, v, r, defSocket) > 0 ? v[0] % 4 : -1;
    }

    /// <summary>The player attached <paramref name="def"/> to that socket at <paramref name="chosen"/> where the guess was <paramref name="guess"/>: remembered (true when a correction was learned, to save).</summary>
    public static bool Placed(SmartRotationMemory memory, Construct c, ConstructPartDef def, int part, int socket, byte chosen, byte guess)
    {
        var f = FrameOf(c);
        c.SocketAt(part, socket, out _, out var dir);
        var ts = c.DefOf(part).Sockets[socket % c.DefOf(part).Sockets.Length];
        var key = SmartRotationMemory.Key(def.Id, (byte)ts.Kind, Rot24.ApplyDir(f.Inv, dir));
        return memory.Placed(def.Id, key, Rot24.Compose(f.Inv, chosen), Rot24.Compose(f.Inv, guess));
    }

    // ---- Free placement ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Free placement's 24 turns, best first (the new construct's frame is the vehicle frame: -Z the look, +Y up): upright,
    /// facing forward, the facing hint (forward, back or up; the others need a construct), the last choice and the
    /// correction for setting this part down. Ties keep the rotate key's old order (<see cref="ConstructPlace.Turns"/>).
    /// </summary>
    public static int RankFree(ConstructPartDef def, SmartRotationMemory? memory, Span<byte> into)
    {
        var w = SmartRotationWeights.Current;
        Span<float> score = stackalloc float[Rot24.Count];
        byte last = 0, learned = 0;
        var hasLast = memory != null && memory.TryLast(def.Id, out last);
        var hasLearned = memory != null && memory.TryLearned(SmartRotationMemory.Key(def.Id, SmartRotationMemory.FreeKind, 2), out learned);
        var target = def.Faces switch
        {
            FaceHint.Forward => -Vector3.UnitZ,
            FaceHint.Back => Vector3.UnitZ,
            FaceHint.Up => Vector3.UnitY,
            _ => Vector3.Zero,
        };
        var n = Math.Min(into.Length, Rot24.Count);
        for (var i = 0; i < n; i++)
        {
            var r = ConstructPlace.Turns[i];
            var s = w.Upright * Rot24.Apply(r, Vector3.UnitY).Y + w.Front * -Rot24.Apply(r, -Vector3.UnitZ).Z; // (-Z is the look)
            s += w.Facing * Vector3.Dot(ConstructParts.DirVector(Rot24.ApplyDir(r, def.FaceAxis)), target);
            if (hasLast && r == last)
                s += w.LastChoice;
            if (hasLearned && r == learned)
                s += w.Learned;
            score[r] = s;
            into[i] = r;
        }
        for (var a = 1; a < n; a++)
            for (var b = a; b > 0 && score[into[b]] > score[into[b - 1]] + 1e-4f; b--)
                (into[b], into[b - 1]) = (into[b - 1], into[b]);
        return n;
    }

    /// <summary>The player set <paramref name="def"/> down as a new construct at <paramref name="chosen"/> where the guess was <paramref name="guess"/>.</summary>
    public static bool PlacedFree(SmartRotationMemory memory, ConstructPartDef def, byte chosen, byte guess) =>
        memory.Placed(def.Id, SmartRotationMemory.Key(def.Id, SmartRotationMemory.FreeKind, 2), chosen, guess);
}
