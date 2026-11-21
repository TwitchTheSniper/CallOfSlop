using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// A store's layout for the stock filler (Constructs/Stores/*.json, written by the station's generator: SpaceStationHexkey.cs): the blueprint it
/// is for, an anchor piece and where that piece's origin is in the station frame the layout's points are in, and the showroom's bays in the
/// route's order (each an inside box and the way its open front faces).
/// </summary>
public sealed class StoreLayout
{
    public const string Format = "call-of-slop-store/1";

    public sealed class Bay
    {
        public string Piece = "";
        public Vector3 Min, Max;
        public bool OpenPlusZ;
    }

    public string Blueprint = "", Anchor = "";
    public Vector3 Origin;
    public Bay[] Bays = [];

    private static Dictionary<string, StoreLayout>? _all;

    /// <summary>The layouts by blueprint name (empty when none loaded; a bad file is refused with a reason in the log).</summary>
    public static Dictionary<string, StoreLayout> All => _all ??= Load(Path.Combine(AppFolders.Data, "Constructs", "Stores"));

    private static Vector3 V(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());

    private static Dictionary<string, StoreLayout> Load(string dir)
    {
        var all = new Dictionary<string, StoreLayout>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(dir))
            return all;
        foreach (var path in Directory.GetFiles(dir, "*.json").Order())
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var r = doc.RootElement;
                if (!r.TryGetProperty("format", out var f) || f.GetString() != Format)
                    throw new FormatException($"\"format\" is not \"{Format}\"");
                var s = new StoreLayout
                {
                    Blueprint = r.GetProperty("blueprint").GetString() ?? "",
                    Anchor = r.GetProperty("anchor").GetString() ?? "",
                    Origin = V(r.GetProperty("origin")),
                    Bays = [.. r.GetProperty("bays").EnumerateArray().Select(b => new Bay
                    {
                        Piece = b.GetProperty("piece").GetString() ?? "",
                        Min = V(b.GetProperty("min")),
                        Max = V(b.GetProperty("max")),
                        OpenPlusZ = b.GetProperty("open").GetString() == "+z",
                    })],
                };
                all[s.Blueprint] = s;
            }
            catch (Exception ex) when (ex is IOException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            {
                Console.WriteLine($"[store] REFUSED {path}: {ex.Message}");
            }
        return all;
    }
}

/// <summary>The showroom's room sets (Crew/StoreRooms.json): each part's job, the departments in the route's order and the rooms as slots by job.</summary>
public sealed class StoreRooms
{
    public const string Format = "call-of-slop-store-rooms/1";

    public readonly record struct Slot(string Job, int Count, char Place, bool Turn, float Cx, float Cz);

    public sealed class Room
    {
        public string Name = "";
        public float W, D;
        public Slot[] Slots = [];
    }

    public sealed class Department
    {
        public string Name = "", Wall = "";
        public Room[] Rooms = [];
        public string[] WallJobs = [];
    }

    public Dictionary<ushort, string> JobOf = [];
    public Department[] Departments = [];

    private static StoreRooms? _current;

    public static StoreRooms Current => _current ??= Load(Path.Combine(AppFolders.Data, "Crew", "StoreRooms.json"));

    private static StoreRooms Load(string path)
    {
        var s = new StoreRooms();
        if (!File.Exists(path))
            return s;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var r = doc.RootElement;
            if (!r.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" is not \"{Format}\"");
            foreach (var j in r.GetProperty("jobs").EnumerateObject())
                if (ConstructParts.Find(j.Name) is { } pd)
                    s.JobOf[(ushort)pd.Id] = j.Value.GetString() ?? "";
                else
                    Console.WriteLine($"[store] {path}: no construct part named \"{j.Name}\" (its job is left out)");
            var rooms = new Dictionary<string, Room>();
            foreach (var rm in r.GetProperty("rooms").EnumerateObject())
            {
                var slots = new List<Slot>();
                foreach (var e in rm.Value.GetProperty("slots").EnumerateArray())
                {
                    var t = (e.GetString() ?? "").Split(' ', 2)[0];
                    var at = t.IndexOf('@');
                    if (at <= 0 || at + 1 >= t.Length)
                        throw new FormatException($"{rm.Name}: bad slot \"{t}\"");
                    var head = t[..at];
                    var n = 1;
                    if (head.IndexOf('*') is var star and > 0)
                    {
                        n = Math.Clamp(int.Parse(head[(star + 1)..]), 1, 12);
                        head = head[..star];
                    }
                    var place = t[at + 1];
                    if ("BLRWC".IndexOf(place) < 0)
                        throw new FormatException($"{rm.Name}: bad place in \"{t}\"");
                    var rest = t[(at + 2)..];
                    var turn = rest.StartsWith('r');
                    if (turn)
                        rest = rest[1..];
                    float cx = 0, cz = 0;
                    if (rest.StartsWith(':'))
                    {
                        var xy = rest[1..].Split(',');
                        cx = float.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture);
                        cz = float.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture);
                    }
                    slots.Add(new Slot(head, n, place, turn, cx, cz));
                }
                rooms[rm.Name] = new Room { Name = rm.Name, W = rm.Value.GetProperty("w").GetSingle(), D = rm.Value.GetProperty("d").GetSingle(), Slots = [.. slots] };
            }
            s.Departments = [.. r.GetProperty("departments").EnumerateArray().Select(d => new Department
            {
                Name = d.GetProperty("name").GetString() ?? "",
                Rooms = [.. d.GetProperty("rooms").EnumerateArray().Select(x => rooms.TryGetValue(x.GetString() ?? "", out var rm) ? rm : throw new FormatException($"no room \"{x.GetString()}\""))],
                Wall = d.TryGetProperty("wall", out var w) ? w.GetString() ?? "" : "",
                WallJobs = d.TryGetProperty("jobs", out var js) ? [.. js.EnumerateArray().Select(x => x.GetString() ?? "")] : [],
            })];
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            Console.WriteLine($"[store] REFUSED {path}: {ex.Message}");
            return new StoreRooms();
        }
        return s;
    }
}

/// <summary>
/// The stock filler (Karl, 2026-11-21: "Don't stock it with parts, make it match the stock in"; "Move it and add!"): a store station's showroom
/// bays are furnished, as real parts on the station, from what its market has in stock. The departments run along the route in order: each of
/// its rooms whose key job is in stock (Crew/StoreRooms.json), each slot taking the most-stocked part of its job that fits, then a product wall
/// of every in-stock part of its jobs; bays left over go round again with the next-most-stocked picks, so more stock means more rooms. When the
/// stock changes (a delivery, shoppers), the plan is made again and only the bays whose contents changed are rebuilt, a few a second.
/// The display pieces are the store's, not its stock: a shopper takes stock from the warehouse (a later phase).
/// </summary>
public sealed partial class Game
{
    private sealed class StoreFill
    {
        public int StockHash = int.MinValue;
        public string[] Planned = [], Built = [];
        public List<(ushort Def, Vector3 Pos, byte Rot)>[] Plan = [];
        public long NextAt;
    }

    private readonly Dictionary<ushort, StoreFill> _storeFills = new(4);

    /// <summary>How many bays a store rebuilds a pass, and the passes' spacing (ticks).</summary>
    private const int StoreBaysPerPass = 6, StorePassEvery = 2 * World.TickRate;

    /// <summary>Host, once a second from the market step: each store station here furnished from its stock.</summary>
    private void StoreFillTick()
    {
        foreach (var m in StationMarketsHere.Values)
        {
            if (m.Def < 0 || m.Def >= StationMarkets.All.Length || Constructs.Get(m.Body) is not { Wreck: false, IsStation: true } st)
                continue;
            var def = StationMarkets.All[m.Def];
            if (def.Blueprint.Length == 0 || !StoreLayout.All.TryGetValue(def.Blueprint, out var lay))
                continue;
            if (!_storeFills.TryGetValue(st.Id, out var f))
                _storeFills[st.Id] = f = new StoreFill();
            if (ServerTick < f.NextAt)
                continue;
            f.NextAt = ServerTick + StorePassEvery;
            StoreFillStep(st, m, def, lay, f);
        }
    }

    private void StoreFillStep(Construct st, StationMarket m, StationDef def, StoreLayout lay, StoreFill f)
    {
        var hash = 17;
        foreach (var s in m.Stock)
            hash = hash * 31 + (s > 0 ? 1 + Math.Min(s, 40) : 0);
        if (hash != f.StockHash)
        {
            f.StockHash = hash;
            if (StorePlan(st, m, def, lay, f) is { } why)
            {
                Log($"[store] {def.Name}: {why}");
                return;
            }
        }
        if (f.Built.Length != lay.Bays.Length)
            f.Built = new string[lay.Bays.Length];
        var done = 0;
        for (var b = 0; b < lay.Bays.Length && done < StoreBaysPerPass; b++)
        {
            if (f.Built[b] == f.Planned[b])
                continue;
            StoreBuildBay(st, lay, b, f.Plan[b]);
            f.Built[b] = f.Planned[b];
            done++;
        }
        if (done > 0)
        {
            st.Rebuild();
            Log($"[store] {def.Name}: furnished {done} bay(s) from its stock ({st.PartCount} parts)");
        }
    }

    /// <summary>Server: a bay's old display pieces out and its plan's in, each tied to the bay's piece so it stays with the station.</summary>
    private void StoreBuildBay(Construct st, StoreLayout lay, int b, List<(ushort Def, Vector3 Pos, byte Rot)> plan)
    {
        var bay = lay.Bays[b];
        if (!StoreFrame(st, lay, out var anchorPos, out var anchorRot))
            return;
        var lo = StoreLocal(anchorPos, anchorRot, lay, bay.Min);
        var hi = StoreLocal(anchorPos, anchorRot, lay, bay.Max);
        var mn = Vector3.Min(lo, hi) - new Vector3(0.01f);
        var mx = Vector3.Max(lo, hi) + new Vector3(0.01f);
        for (var i = st.PartCount - 1; i >= 0; i--)
        {
            if (st.DefOf(i).IsStationPiece)
                continue;
            var p = st.Parts[i].Pos;
            if (p.X >= mn.X && p.X <= mx.X && p.Y >= mn.Y && p.Y <= mx.Y && p.Z >= mn.Z && p.Z <= mx.Z)
                Constructs.RemovePart(st, i);
        }
        var host = -1;
        for (var i = 0; i < st.PartCount && host < 0; i++)
            if (st.DefOf(i).Name == bay.Piece)
                host = i;
        if (host < 0)
            return;
        foreach (var (d, pos, rot) in plan)
        {
            if (ConstructParts.Get(d) is not { } pd)
                continue;
            var i = Constructs.AddPart(st, pd, StoreLocal(anchorPos, anchorRot, lay, pos), Rot24.Compose(anchorRot, rot)); // (the plan's turn is the station frame's)
            if (i < 0)
                break;
            Constructs.AddLink(st, i, 0, host, 0);
        }
    }

    /// <summary>The anchor piece's place on the station (its origin and turn), from which the layout's station-frame points are found.</summary>
    private static bool StoreFrame(Construct st, StoreLayout lay, out Vector3 pos, out byte rot)
    {
        for (var i = 0; i < st.PartCount; i++)
            if (st.DefOf(i).Name == lay.Anchor)
            {
                (pos, rot) = (st.Parts[i].Pos, st.Parts[i].Rot);
                return true;
            }
        (pos, rot) = (default, 0);
        return false;
    }

    private static Vector3 StoreLocal(Vector3 anchorPos, byte anchorRot, StoreLayout lay, Vector3 p) => anchorPos + Rot24.Apply(anchorRot, p - lay.Origin);

    /// <summary>
    /// Host: what each bay holds for the stock as it is (station-frame points, turns into the station's frame by the anchor's turn), and a
    /// signature a bay is rebuilt by. Null, or why there is no plan.
    /// </summary>
    private string? StorePlan(Construct st, StationMarket m, StationDef def, StoreLayout lay, StoreFill f)
    {
        var rooms = StoreRooms.Current;
        if (rooms.Departments.Length == 0)
            return "no room sets (Crew/StoreRooms.json)";
        if (!StoreFrame(st, lay, out _, out _))
            return $"no {lay.Anchor} on the station";
        // the stock by job, most first
        var byJob = new Dictionary<string, List<(ushort Part, int Stock)>>();
        var ng = def.Goods.Length;
        for (var j = 0; j < def.Parts.Length && ng + j < m.Stock.Length; j++)
        {
            var n = m.Stock[ng + j];
            if (n <= 0 || !rooms.JobOf.TryGetValue(def.Parts[j].Part, out var job))
                continue;
            if (!byJob.TryGetValue(job, out var l))
                byJob[job] = l = [];
            l.Add((def.Parts[j].Part, n));
        }
        foreach (var l in byJob.Values)
            l.Sort((a, b) => b.Stock != a.Stock ? b.Stock.CompareTo(a.Stock) : a.Part.CompareTo(b.Part));
        // the bays' order: rooms whose key is in, then the department's wall; round again with the next picks while bays are left
        var entries = new List<(StoreRooms.Department Dept, StoreRooms.Room? Room, int Pass)>();
        for (var pass = 0; pass < 6 && entries.Count < lay.Bays.Length; pass++)
        {
            var before = entries.Count;
            foreach (var d in rooms.Departments)
            {
                foreach (var r in d.Rooms)
                    if (r.Slots.Length > 0 && byJob.TryGetValue(r.Slots[0].Job, out var key) && key.Count > 0 && (pass == 0 || key.Sum(x => x.Stock) > pass * 4))
                        entries.Add((d, r, pass));
                if (pass == 0 && d.Wall.Length > 0 && d.WallJobs.Any(byJob.ContainsKey))
                    entries.Add((d, null, 0));
            }
            if (entries.Count == before)
                break;
        }
        if (f.Plan.Length != lay.Bays.Length)
            f.Plan = new List<(ushort, Vector3, byte)>[lay.Bays.Length];
        if (f.Planned.Length != lay.Bays.Length)
            f.Planned = new string[lay.Bays.Length];
        var sb = new System.Text.StringBuilder();
        for (var b = 0; b < lay.Bays.Length; b++)
        {
            var plan = f.Plan[b] ??= [];
            plan.Clear();
            if (b < entries.Count)
            {
                var (d, r, pass) = entries[b];
                if (r != null)
                    StoreLayRoom(lay.Bays[b], r, byJob, pass, plan);
                else
                    StoreLayWall(lay.Bays[b], d, byJob, plan);
            }
            sb.Clear();
            foreach (var (d0, p, ro) in plan)
                sb.Append(d0).Append(':').Append(p.X.ToString("0.00")).Append(',').Append(p.Z.ToString("0.00")).Append(',').Append(ro).Append(';');
            f.Planned[b] = sb.ToString();
        }
        var rooms0 = entries.Count(e => e.Room != null);
        Log($"[store] {def.Name}: planned {Math.Min(entries.Count, lay.Bays.Length)} of {lay.Bays.Length} bays ({rooms0} rooms, {entries.Count - rooms0} product walls) from {byJob.Sum(x => x.Value.Count)} parts in stock");
        return null;
    }

    /// <summary>A room's frame in a bay: its back-left corner, its across (left to right seen from the route) and its into-the-room directions (station frame).</summary>
    private static void StoreRoomFrame(StoreLayout.Bay bay, float w, out Vector3 corner, out Vector3 ex, out Vector3 ez)
    {
        var cx = (bay.Min.X + bay.Max.X) / 2;
        if (bay.OpenPlusZ)
        {
            (ex, ez) = (Vector3.UnitX, Vector3.UnitZ);
            corner = new Vector3(cx - w / 2, bay.Min.Y, bay.Min.Z);
        }
        else
        {
            (ex, ez) = (-Vector3.UnitX, -Vector3.UnitZ);
            corner = new Vector3(cx + w / 2, bay.Min.Y, bay.Max.Z);
        }
    }

    /// <summary>A part's back in its own frame: its wall or hang socket's way when that is level, else -z (the furniture's convention).</summary>
    private static Vector3 StoreBack(ConstructPartDef d)
    {
        foreach (var s in d.Sockets)
            if (s.Name is "wall" or "hang" or "back")
            {
                var v = ConstructParts.DirVector(s.Dir);
                if (MathF.Abs(v.Y) < 0.5f)
                    return v;
            }
        return -Vector3.UnitZ;
    }

    /// <summary>The turn (station frame) that puts part <paramref name="d"/>'s back towards <paramref name="toward"/> (a level way), a quarter more with <paramref name="quarter"/>.</summary>
    private static byte StoreTurnWorld(ConstructPartDef d, Vector3 toward, bool quarter)
    {
        var back = StoreBack(d);
        var a = MathF.Atan2(toward.X, toward.Z) - MathF.Atan2(back.X, back.Z) + (quarter ? MathF.PI / 2 : 0);
        return Rot24.Nearest(Quaternion.CreateFromAxisAngle(Vector3.UnitY, a), out _);
    }

    /// <summary>A part's extent turned by <paramref name="r"/> (in the frame the turn is in): its lowest and highest corner.</summary>
    private static (Vector3 Lo, Vector3 Hi) StoreBox(ConstructPartDef d, byte r)
    {
        var a = Rot24.Apply(r, d.BoundsMin);
        var b = Rot24.Apply(r, d.BoundsMax);
        return (Vector3.Min(a, b), Vector3.Max(a, b));
    }

    private static float Along(Vector3 lo, Vector3 hi, Vector3 axis) => MathF.Abs(Vector3.Dot(hi - lo, axis));

    /// <summary>
    /// The origin that stands part <paramref name="d"/> turned <paramref name="r"/> with the near corner of its box at room point (u, v), its feet
    /// <paramref name="lift"/> over the bay's floor (station frame).
    /// </summary>
    private static Vector3 StoreSpot(ConstructPartDef d, byte rWorld, Vector3 corner, Vector3 ex, Vector3 ez, float u, float v, float lift)
    {
        var (lo, hi) = StoreBox(d, rWorld);
        // the box's least u and v from its origin
        float lu = MathF.Min(Vector3.Dot(lo, ex), Vector3.Dot(hi, ex)), lv = MathF.Min(Vector3.Dot(lo, ez), Vector3.Dot(hi, ez));
        var p = corner + ex * (u - lu) + ez * (v - lv);
        p.Y = corner.Y + lift - lo.Y;
        return p;
    }

    /// <summary>Lays room <paramref name="r"/> in a bay: its back run, its hung run, its side runs and its free pieces, each slot's part the most stocked that fits.</summary>
    private static void StoreLayRoom(StoreLayout.Bay bay, StoreRooms.Room r, Dictionary<string, List<(ushort Part, int Stock)>> byJob, int pass,
        List<(ushort Def, Vector3 Pos, byte Rot)> plan)
    {
        var bw = MathF.Abs(bay.Max.X - bay.Min.X) - 0.2f;
        var bd = MathF.Abs(bay.Max.Z - bay.Min.Z) - 1.2f;
        var w = MathF.Min(bw, r.W * 1.25f);
        var dep = MathF.Min(bd, r.D * 1.25f);
        StoreRoomFrame(bay, w, out var corner, out var ex, out var ez);
        const float Gap = 0.12f, Feet = 0.004f;
        var used = new Dictionary<string, int>();
        ConstructPartDef? Pick(string job, float most)
        {
            if (!byJob.TryGetValue(job, out var l) || l.Count == 0)
                return null;
            used.TryGetValue(job, out var k);
            for (var t = 0; t < l.Count; t++)
            {
                var c = l[(t + k + pass) % l.Count];
                if (ConstructParts.Get(c.Part) is not { } d)
                    continue;
                var (lo, hi) = StoreBox(d, 0);
                var size = hi - lo;
                if (MathF.Min(size.X, size.Z) <= most && size.Y < 3.1f)
                {
                    used[job] = k + 1;
                    return d;
                }
            }
            return null;
        }
        // the back run (B), along ex from the left wall
        float u = Gap, backDepth = 0;
        foreach (var s in r.Slots.Where(s => s.Place == 'B'))
            for (var n = 0; n < s.Count; n++)
            {
                if (Pick(s.Job, dep * 0.6f) is not { } d)
                    break;
                var rw = StoreTurnWorld(d, -ez, s.Turn);
                var (lo, hi) = StoreBox(d, rw);
                var across = Along(lo, hi, ex);
                if (u + across > w - Gap)
                    break;
                plan.Add(((ushort)d.Id, StoreSpot(d, rw, corner, ex, ez, u, 0.03f, Feet), rw));
                backDepth = MathF.Max(backDepth, Along(lo, hi, ez));
                u += across + Gap;
            }
        // the hung run (W), over the back run, its foot 2 m up
        u = Gap;
        foreach (var s in r.Slots.Where(s => s.Place == 'W'))
            for (var n = 0; n < s.Count; n++)
            {
                if (Pick(s.Job, 0.5f) is not { } d)
                    break;
                var rw = StoreTurnWorld(d, -ez, false);
                var (lo, hi) = StoreBox(d, rw);
                var across = Along(lo, hi, ex);
                if (u + across > w - Gap || hi.Y - lo.Y > 1.0f)
                    break;
                plan.Add(((ushort)d.Id, StoreSpot(d, rw, corner, ex, ez, u, 0.03f, 2.0f), rw));
                u += across + Gap;
            }
        // the side runs (L, R), from in front of the back run to the front
        foreach (var side in "LR")
        {
            var v = backDepth + 0.15f;
            var toward = side == 'L' ? -ex : ex;
            foreach (var s in r.Slots.Where(s => s.Place == side))
                for (var n = 0; n < s.Count; n++)
                {
                    if (Pick(s.Job, w * 0.3f) is not { } d)
                        break;
                    var rw = StoreTurnWorld(d, toward, s.Turn);
                    var (lo, hi) = StoreBox(d, rw);
                    var along = Along(lo, hi, ez);
                    var into = Along(lo, hi, ex);
                    if (v + along > dep - 0.3f)
                        break;
                    var uu = side == 'L' ? 0.03f : w - 0.03f - into;
                    plan.Add(((ushort)d.Id, StoreSpot(d, rw, corner, ex, ez, uu, v, Feet), rw));
                    v += along + Gap;
                }
        }
        // free pieces (C), centred where the room's plan put them (scaled with the room)
        foreach (var s in r.Slots.Where(s => s.Place == 'C'))
        {
            if (Pick(s.Job, MathF.Min(w, dep) * 0.5f) is not { } d)
                continue;
            var rw = StoreTurnWorld(d, -ez, s.Turn);
            var (lo, hi) = StoreBox(d, rw);
            float au = Along(lo, hi, ex), av = Along(lo, hi, ez);
            float u0 = au / 2 + 0.3f, u1 = w - au / 2 - 0.3f, v0 = backDepth + 0.3f + av / 2, v1 = dep - 0.2f - av / 2;
            if (u0 > u1 || v0 > v1)
                continue; // (no room for it in front of the back run)
            var cu = Math.Clamp(s.Cx * w / r.W, u0, u1);
            var cv = Math.Clamp(s.Cz * dep / r.D, v0, v1);
            plan.Add(((ushort)d.Id, StoreSpot(d, rw, corner, ex, ez, cu - au / 2, cv - av / 2, Feet), rw));
        }
        StoreDropOverlaps(plan);
    }

    /// <summary>A department's product wall: every in-stock part of its jobs side by side, back wall first, then the left and right walls.</summary>
    private static void StoreLayWall(StoreLayout.Bay bay, StoreRooms.Department dept, Dictionary<string, List<(ushort Part, int Stock)>> byJob,
        List<(ushort Def, Vector3 Pos, byte Rot)> plan)
    {
        var w = MathF.Abs(bay.Max.X - bay.Min.X) - 0.2f;
        var dep = MathF.Abs(bay.Max.Z - bay.Min.Z) - 1.2f;
        StoreRoomFrame(bay, w, out var corner, out var ex, out var ez);
        var all = dept.WallJobs.Where(byJob.ContainsKey).SelectMany(j => byJob[j]).OrderByDescending(x => x.Stock).ThenBy(x => x.Part).ToList();
        const float Gap = 0.35f;
        float u = Gap, backDepth = 0, vl = 0, vr = 0;
        var phase = 0;
        foreach (var (part, _) in all)
        {
            if (ConstructParts.Get(part) is not { } d)
                continue;
            for (; phase < 3; phase++)
            {
                if (phase == 0)
                {
                    var rw = StoreTurnWorld(d, -ez, false);
                    var (lo, hi) = StoreBox(d, rw);
                    var across = Along(lo, hi, ex);
                    if (u + across <= w - Gap && hi.Y - lo.Y < 3.1f && Along(lo, hi, ez) < dep * 0.5f)
                    {
                        plan.Add(((ushort)d.Id, StoreSpot(d, rw, corner, ex, ez, u, 0.03f, 0.004f), rw));
                        backDepth = MathF.Max(backDepth, Along(lo, hi, ez));
                        u += across + Gap;
                        break;
                    }
                    if (u > Gap)
                    {
                        vl = vr = backDepth + 0.4f;
                        continue; // (the back wall is full: on to the sides)
                    }
                    break; // (too big for the back wall even alone)
                }
                var left = phase == 1;
                var toward = left ? -ex : ex;
                var rs = StoreTurnWorld(d, toward, false);
                var (slo, shi) = StoreBox(d, rs);
                var along = Along(slo, shi, ez);
                var into = Along(slo, shi, ex);
                ref var v = ref left ? ref vl : ref vr;
                if (v + along <= dep - 0.3f && into < w * 0.3f && shi.Y - slo.Y < 3.1f)
                {
                    var uu = left ? 0.03f : w - 0.03f - into;
                    plan.Add(((ushort)d.Id, StoreSpot(d, rs, corner, ex, ez, uu, v, 0.004f), rs));
                    v += along + Gap;
                    break;
                }
            }
            if (phase >= 3)
                break;
        }
        StoreDropOverlaps(plan);
    }

    /// <summary>Drops any planned piece whose box overlaps one planned before it (a run that met a free piece, a side that met the back).</summary>
    private static void StoreDropOverlaps(List<(ushort Def, Vector3 Pos, byte Rot)> plan)
    {
        for (var i = plan.Count - 1; i > 0; i--)
        {
            if (ConstructParts.Get(plan[i].Def) is not { } di)
                continue;
            var (a0, a1) = StoreBox(di, plan[i].Rot);
            a0 += plan[i].Pos;
            a1 += plan[i].Pos;
            for (var j = 0; j < i; j++)
            {
                if (ConstructParts.Get(plan[j].Def) is not { } dj)
                    continue;
                var (b0, b1) = StoreBox(dj, plan[j].Rot);
                b0 += plan[j].Pos;
                b1 += plan[j].Pos;
                if (a0.X < b1.X - 0.01f && a1.X > b0.X + 0.01f && a0.Y < b1.Y - 0.01f && a1.Y > b0.Y + 0.01f && a0.Z < b1.Z - 0.01f && a1.Z > b0.Z + 0.01f)
                {
                    plan.RemoveAt(i);
                    break;
                }
            }
        }
    }
}
