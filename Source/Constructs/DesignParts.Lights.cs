using System.Numerics;
using System.Runtime.CompilerServices;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// Design parts, the rooms' lamps and the use polish (DesignParts.cs has the map of the files; Karl's walkable designs, package L, 2026-11-27).
/// <list type="bullet">
/// <item><b>Lamps.</b> Every room with <see cref="Room.Lights"/> was pitch black in the game: the designer's interior draws its light strips
/// unlit-bright, but nothing lit the room round them. Each lit room gets one hidden element, <c>room{n}.lights</c>, whose part holds the room's
/// lamps (<see cref="ConstructPartDef.Lamps"/>, lit by <see cref="ConstructLamps"/> as a lined bay's are: always on for now, no switch). The
/// lamps stand where the designer's own ceiling light strips are (<see cref="DesignScene.RoomInterior"/>'s glow mesh), one every
/// <see cref="ConstructLamps.StripSpacing"/> along each strip and <see cref="ConstructLamps.StripDrop"/> under it, as
/// <see cref="ConstructLamps.StripLamps"/> lights a catalogue ship's strips. A room whose interior is Off has no strips drawn: its lamps
/// stand where the Clean interior would put them (the designer's same rule, asked for that room with its interior on).</item>
/// <item><b>Names.</b> A design part's key (<c>Design (#hash) room10</c>) is no name for a player: <see cref="ShownName"/> gives "Zero Day -
/// Bridge", a door's own name ("Airlock inner door"), and so on, for the look line.</item>
/// <item><b>Board at the helm.</b> F on a design's plain hull from outside sits the player at its helm (<see cref="BoardFrom"/>): never inside a
/// room, never on a part that does something, and never when something usable (a door, ladder, lift, seat, hitch, lamp switch) is in reach.</item>
/// </list>
/// </summary>
public static partial class DesignParts
{
    public const int MaxRoomLamps = 48; // one room's lamps at most (a 40 m corridor's two strips)
    public const float RoomLampKg = 5f;

    private static readonly Vector3 CleanLamp = new Vector3(0.957f, 0.984f, 1f) * 0.9f, IndustrialLamp = new Vector3(1f, 0.847f, 0.565f) * 0.9f;

    /// <summary>Each lights element's lamps, in the ship's frame (its part's frame once <see cref="IntoFrames"/> has set its origin).</summary>
    private static readonly ConditionalWeakTable<Element, RoomLamps> LampsOf = new();

    private sealed record RoomLamps(Vector3[] At, (Vector3 Half, Vector3 Face)[] Lines, Vector3 Colour);

    /// <summary>The lit rooms' lamps: an element a room (a step each), welded to the room's first shell element.</summary>
    private static IEnumerable<int> AddLights(Steps st)
    {
        var (d, els) = (st.D, st.Els);
        if (!d.Rooms.Any(r => r.Lights))
            yield break;
        var check = st.Check ??= DesignScene.Run(d);
        var shellOf = new Dictionary<int, int>(); // room index -> its first shell element
        for (var i = 0; i < els.Count; i++)
            if (els[i].Kind == "floor" && els[i].Id.StartsWith("room", StringComparison.Ordinal))
            {
                var id = els[i].Id.AsSpan(4);
                var dot = id.IndexOf('.');
                if (int.TryParse(dot < 0 ? id : id[..dot], out var ri))
                    shellOf.TryAdd(ri, i);
            }
        var up = d.Stacked ? -Vector3.UnitZ : Vector3.UnitY; // (a stacked plan has up as -Z, as its interior has)
        var made = 0;
        for (var ri = 0; ri < d.Rooms.Count; ri++)
        {
            var r = d.Rooms[ri];
            if (!r.Lights)
                continue;
            var was = r.Interior;
            DesignMesh glow;
            try
            {
                if (r.Interior == "Off")
                    r.Interior = "Clean"; // (where its strips would be: the designer's own rule; nothing of it is drawn)
                glow = DesignScene.RoomInterior(d, r, check.Doors).Glow;
            }
            finally
            {
                r.Interior = was;
            }
            var lamps = CeilingLamps(d, r, glow, up);
            if (lamps.Count == 0)
            {
                Console.WriteLine($"[design] {DesignScene.Name(r)} on deck {r.Deck + 1} is lit but has no ceiling strip to hang a lamp on: dark");
                yield return els.Count;
                continue;
            }
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            foreach (var l in lamps)
                (lo, hi) = (Vector3.Min(lo, l.At), Vector3.Max(hi, l.At));
            var mid = (lo + hi) * 0.5f;
            var e = new Element
            {
                Id = $"room{ri}.lights", Kind = "lights", Name = $"{DesignScene.Name(r)} lights", Fn = "roomlights", Kg = RoomLampKg,
                BoxMin = mid - new Vector3(0.005f), BoxMax = mid + new Vector3(0.005f), NoCollider = true, // (hidden: the room's strips are its look)
            };
            LampsOf.AddOrUpdate(e, new RoomLamps([.. lamps.Select(l => l.At)], [.. lamps.Select(l => (l.Half, -up))], was == "Industrial" ? IndustrialLamp : CleanLamp));
            els.Add(e);
            if (shellOf.TryGetValue(ri, out var shell))
                Weld(els, shell, els.Count - 1);
            made += lamps.Count;
            yield return els.Count;
        }
        Console.WriteLine($"[design] \"{d.Name}\"'s rooms: {made} lamps in {d.Rooms.Count(r => r.Lights)} lit rooms");
    }

    /// <summary>
    /// The lamps under a room's ceiling strips: the glow mesh's boxes (36 corners each, as DesignMesh.Box lays them) over the room's middle
    /// height, a lamp every <see cref="ConstructLamps.StripSpacing"/> along each, <see cref="ConstructLamps.StripDrop"/> under it.
    /// </summary>
    private static List<(Vector3 At, Vector3 Half)> CeilingLamps(ShipDesign d, Room r, DesignMesh glow, Vector3 up)
    {
        var lamps = new List<(Vector3 At, Vector3 Half)>();
        var b = d.Box(r);
        var midUp = Vector3.Dot(new Vector3((float)(b[0] + b[1]) * 0.5f, (float)(b[2] + b[3]) * 0.5f, (float)(b[4] + b[5]) * 0.5f), up);
        const int Corners = 36;
        for (var v = 0; v + Corners * 3 <= glow.P.Count && lamps.Count < MaxRoomLamps; v += Corners * 3)
        {
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            for (var k = 0; k < Corners; k++)
            {
                var p = new Vector3((float)glow.P[v + k * 3], (float)glow.P[v + k * 3 + 1], (float)glow.P[v + k * 3 + 2]);
                (lo, hi) = (Vector3.Min(lo, p), Vector3.Max(hi, p));
            }
            var centre = (lo + hi) * 0.5f;
            if (Vector3.Dot(centre, up) <= midUp)
                continue; // (a floor strip: it lights nothing round it)
            var size = hi - lo;
            var flat = size - up * Vector3.Dot(size, up) * Vector3.Dot(up, up);
            var along = MathF.Abs(flat.X) >= MathF.Max(MathF.Abs(flat.Y), MathF.Abs(flat.Z)) ? Vector3.UnitX : MathF.Abs(flat.Z) >= MathF.Abs(flat.Y) ? Vector3.UnitZ : Vector3.UnitY;
            var len = MathF.Abs(Vector3.Dot(size, along));
            var under = centre - up * (MathF.Abs(Vector3.Dot(size, up)) * 0.5f + ConstructLamps.StripDrop);
            var n = Math.Max(1, (int)MathF.Round(len / ConstructLamps.StripSpacing));
            for (var k = 0; k < n && lamps.Count < MaxRoomLamps; k++)
                lamps.Add((under + along * (((k + 0.5f) / n - 0.5f) * len), along * (len / n * 0.5f))); // (each a line over its share of the strip)
        }
        return lamps;
    }

    /// <summary>The most lamps one light bar holds (a 24 m bar's, one every <see cref="ConstructLamps.StripSpacing"/>).</summary>
    public const int MaxBarLamps = 14;

    /// <summary>
    /// A light bar's element (DesignLightBars, Karl 2026-11-27): its part holds lamps along its strip in its colour at its brightness, always on
    /// as a room's are (the roomlights function), and it has no collider (a strip on a wall: nothing a person walks into). Its lamps are worked out
    /// at the piece's own place <paramref name="w0"/> and moved to this copy's <paramref name="w"/> (a mirrored twin's, a pair's).
    /// </summary>
    private static void BarLamps(ShipDesign d, Piece p, DesignScene.Place w0, DesignScene.Place w, Element e)
    {
        e.Hulls = [];
        e.NoCollider = true;
        var lamps = DesignLightBars.Lamps(d, p, ConstructLamps.StripSpacing, MaxBarLamps);
        if (lamps.Count == 0)
            return;
        var at = new Vector3[lamps.Count];
        var lines = new (Vector3 Half, Vector3 Face)[lamps.Count];
        // (a direction moved from the piece's own place to this copy's: no origin)
        Vector3 Turn(double[] v)
        {
            double lx = v[0] * w0.X[0] + v[1] * w0.X[1] + v[2] * w0.X[2], ly = v[0] * w0.Y[0] + v[1] * w0.Y[1] + v[2] * w0.Y[2], lz = v[0] * w0.Z[0] + v[1] * w0.Z[1] + v[2] * w0.Z[2];
            return new Vector3((float)(w.X[0] * lx + w.Y[0] * ly + w.Z[0] * lz), (float)(w.X[1] * lx + w.Y[1] * ly + w.Z[1] * lz), (float)(w.X[2] * lx + w.Y[2] * ly + w.Z[2] * lz));
        }
        for (var i = 0; i < lamps.Count; i++)
        {
            var (q, half, face) = lamps[i];
            lines[i] = (Turn(half), Vector3.Normalize(Turn(face)));
            double[] r = [q[0] - w0.O[0], q[1] - w0.O[1], q[2] - w0.O[2]];
            double lx = r[0] * w0.X[0] + r[1] * w0.X[1] + r[2] * w0.X[2], ly = r[0] * w0.Y[0] + r[1] * w0.Y[1] + r[2] * w0.Y[2], lz = r[0] * w0.Z[0] + r[1] * w0.Z[1] + r[2] * w0.Z[2];
            at[i] = new Vector3((float)(w.O[0] + w.X[0] * lx + w.Y[0] * ly + w.Z[0] * lz), (float)(w.O[1] + w.X[1] * lx + w.Y[1] * ly + w.Z[1] * lz), (float)(w.O[2] + w.X[2] * lx + w.Y[2] * ly + w.Z[2] * lz));
        }
        var c = DesignMesh.Hex(p.Beam);
        e.Fn = "roomlights";
        LampsOf.AddOrUpdate(e, new RoomLamps(at, lines, new Vector3(c.R, c.G, c.B) / 255f * (float)p.Bright));
    }

    private static void RegisterLights(KindTable k) => k.Fn("roomlights", _ => Template.ShallowCopy(), LightsFinish);

    /// <summary>A lights element's part: its lamps, in the part's frame (moved by its origin: a structure element stands unturned).</summary>
    private static void LightsFinish(Element e, ConstructPartDef d)
    {
        if (LampsOf.TryGetValue(e, out var l))
        {
            d.Lamps = [.. l.At.Select(p => (p - e.Origin, l.Colour))];
            d.LampLines = l.Lines; // (each a strip: lit along its share of the strip, on the side it faces: ConstructLamps)
        }
    }

    // ---- What a player is told and given on aiming at a design -------------------------------------------------------------

    /// <summary>
    /// A design part's name as a player reads it (the look line): a room "Zero Day - Bridge", a working part its own name ("Airlock inner
    /// door", "Helm seat"), the hull and its pieces "Zero Day - hull" or "Zero Day - Railgun". Null: not a design part.
    /// </summary>
    public static string? ShownName(ConstructPartDef d)
    {
        if (!IsDesign(d))
            return null;
        Entry? en;
        Element? el;
        lock (Interned)
        {
            if (!Info.TryGetValue(d, out var i))
                return null;
            (en, el) = (i.E, i.El);
        }
        var ship = en.Name.Length > 0 ? en.Name : "Ship";
        return el.Kind switch
        {
            "floor" => $"{ship} - {RoomOf(el.Name)}",
            "lights" => $"{ship} - {el.Name}",
            "band" or "join" => $"{ship} - hull",
            "piece" when el.Fn.Length == 0 => $"{ship} - {el.Name}",
            _ => el.Name.Length > 0 ? char.ToUpperInvariant(el.Name[0]) + el.Name[1..] : ship,
        };
    }

    private static string RoomOf(string shellName)
    {
        var at = shellName.IndexOf(" shell", StringComparison.Ordinal);
        return at > 0 ? shellName[..at] : shellName;
    }

    /// <summary>
    /// The seat F on a design part sits the player in (ConstructEdits.Use and the look line), or -1: F works the part itself. Only plain hull
    /// (a band, a join, a piece that does nothing) aimed at from outside: never from inside a room (its shells' bounds), and never when a usable
    /// part (a door, ladder, lift, seat, hitch, dock, lamp or switch) is within <see cref="ConstructBuild.UseReach"/> of the eye (a door's
    /// leaf the ray passed, a hatch beside the aim). Not per tick: on F, and when the look line changes.
    /// </summary>
    public static int BoardFrom(Construct c, int part, Vector3 eye)
    {
        if (part < 0 || part >= c.PartCount)
            return -1;
        var d = c.DefOf(part);
        if (d.Role == PartRole.Seat || KeepsUse(d) || ElementOf(d) is not { } el || el.Fn.Length > 0 || el.Kind is not ("band" or "join" or "piece"))
            return -1;
        var local = c.ToLocal(eye);
        var reach = ConstructBuild.UseReach;
        for (var i = 0; i < c.PartCount; i++)
        {
            var di = c.DefOf(i);
            if (!IsDesign(di))
                continue;
            ref var p = ref c.Parts[i];
            var usable = di.Role == PartRole.Seat || di.Logic is CLogic.Button or CLogic.Switch or CLogic.Lever or CLogic.KeySwitch || KeepsUse(di) && ElementOf(di) is not ({ Kind: "lights" } or { Fn: "roomlights" });
            var room = !usable && ElementOf(di) is { Kind: "floor" };
            if (!usable && !room)
                continue;
            var lo = p.Pos + Rot24.Apply(p.Rot, di.BoundsMin);
            var hi = p.Pos + Rot24.Apply(p.Rot, di.BoundsMax);
            (lo, hi) = (Vector3.Min(lo, hi), Vector3.Max(lo, hi));
            if (room)
            {
                if (local.X > lo.X && local.X < hi.X && local.Y > lo.Y && local.Y < hi.Y && local.Z > lo.Z && local.Z < hi.Z)
                    return -1; // (inside a room: F there is never the helm's)
                continue;
            }
            if (Vector3.DistanceSquared(local, Vector3.Clamp(local, lo, hi)) < reach * reach)
                return -1; // (something usable in reach: F is for it)
        }
        return BoardSeat(c, part);
    }
}
