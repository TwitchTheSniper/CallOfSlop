using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>Reading and writing a map file's optional "movers" section (see Maps/README.md, "Movers").</summary>
public static partial class MapFile
{
    private static readonly string[] MoverKeys = ["type", "name", "at", "points", "rise", "speed", "spin", "yaw", "dwell", "loop", "phase", "color", "boxes", "running"];
    private static readonly string[] MoverBoxKeys = ["size", "offset", "yaw", "color"];

    /// <summary>Most movers a map may have, and most boxes per mover (they're collided and drawn one by one).</summary>
    public const int MaxMovers = 32, MaxMoverBoxes = 16;

    private const float Deg = MathF.PI / 180f;

    // ---- Writing ----------------------------------------------------------------------------------

    private static string MoversToJson(List<MoverDef> movers)
    {
        var sb = new StringBuilder("[\n");
        for (var i = 0; i < movers.Count; i++)
        {
            var m = movers[i];
            var parts = new List<string> { $"\"type\": {Str(m.Kind.ToString().ToLowerInvariant())}" };
            if (m.Name.Length > 0)
                parts.Add($"\"name\": {Str(m.Name)}");
            switch (m.Kind)
            {
                case MoverKind.Rotate:
                    parts.Add($"\"at\": {Vec(m.Points[0])}");
                    parts.Add($"\"speed\": {Num(m.Spin / Deg)}");
                    break;
                case MoverKind.Elevator:
                    parts.Add($"\"at\": {Vec(m.Points[0])}");
                    parts.Add($"\"rise\": {Num(m.Rise)}");
                    parts.Add($"\"speed\": {Num(m.Speed)}");
                    break;
                default:
                    parts.Add("\"points\": [" + string.Join(", ", m.Points.Select(Vec)) + "]");
                    parts.Add($"\"speed\": {Num(m.Speed)}");
                    if (m.Loop)
                        parts.Add("\"loop\": true");
                    break;
            }
            if (m.Kind != MoverKind.Rotate && m.Spin != 0)
                parts.Add($"\"spin\": {Num(m.Spin / Deg)}");
            if (m.Yaw0 != 0)
                parts.Add($"\"yaw\": {Num(m.Yaw0 / Deg)}");
            if (m.Kind != MoverKind.Rotate && m.Dwell != 0)
                parts.Add($"\"dwell\": {Num(m.Dwell)}");
            if (m.Phase != 0)
                parts.Add($"\"phase\": {Num(m.Phase)}");
            if (!m.StartsRunning)
                parts.Add("\"running\": false");
            var boxes = m.Parts.Select(p =>
            {
                var bp = new List<string> { $"\"size\": {Vec(p.Half * 2)}" };
                if (p.Center != new Vector3(0, -p.Half.Y, 0))
                    bp.Add($"\"offset\": {Vec(p.Center)}");
                if (p.Yaw != 0)
                    bp.Add($"\"yaw\": {Num(p.Yaw / Deg)}");
                bp.Add($"\"color\": {Col(p.Color)}");
                return "{ " + string.Join(", ", bp) + " }";
            });
            parts.Add("\"boxes\": [\n        " + string.Join(",\n        ", boxes) + "\n      ]");
            sb.Append("    { ").Append(string.Join(", ", parts)).Append(" }").Append(i < movers.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("  ]");
        return sb.ToString();
    }

    private static string Vec(Vector3 v) => $"[{Num(v.X)}, {Num(v.Y)}, {Num(v.Z)}]";

    // ---- Reading ----------------------------------------------------------------------------------

    private static List<MoverDef> ReadMovers(JsonElement e, string source)
    {
        var list = new List<MoverDef>();
        var index = 0;
        foreach (var me in JsonList(e, $"{source}, movers"))
        {
            var where = $"{source}, mover #{++index}";
            if (index > MaxMovers)
                throw new MapFileException($"{source}: at most {MaxMovers} movers");
            CheckKeys(me, MoverKeys, where);
            var typeName = Prop(me, "type") is { ValueKind: JsonValueKind.String } te ? te.GetString()! : throw new MapFileException($"{where}: needs \"type\": \"rotate\", \"slide\" or \"elevator\"");
            if (!Enum.TryParse<MoverKind>(typeName, true, out var kind) || !Enum.IsDefined(kind))
                throw new MapFileException($"{where}: \"type\" is \"rotate\", \"slide\" or \"elevator\", got \"{typeName}\"");
            var m = new MoverDef { Kind = kind };
            if (Prop(me, "name") is { ValueKind: JsonValueKind.String } ne)
                m.Name = ne.GetString()!;
            Vector3 At() => Prop(me, "at") is { } a ? Vec3(a, $"{where} (at)") : throw new MapFileException($"{where}: a {typeName} mover needs \"at\": [x, y, z]");
            float Opt(string key, float def) => Prop(me, key) is { } v ? Num(v, $"{where} ({key})") : def;
            switch (kind)
            {
                case MoverKind.Rotate:
                    m.Points = [At()];
                    m.Spin = Opt("speed", 30f) * Deg;
                    if (Prop(me, "points") != null || Prop(me, "rise") != null)
                        throw new MapFileException($"{where}: a rotate mover turns in place (use \"at\", not \"points\" or \"rise\")");
                    break;
                case MoverKind.Elevator:
                {
                    var at = At();
                    m.Rise = Opt("rise", 0f);
                    if (MathF.Abs(m.Rise) < 0.1f)
                        throw new MapFileException($"{where}: an elevator needs \"rise\" (metres, at least 0.1)");
                    m.Points = [at, at + new Vector3(0, m.Rise, 0)];
                    m.Speed = Opt("speed", 2f);
                    m.Spin = Opt("spin", 0f) * Deg;
                    break;
                }
                default:
                {
                    if (Prop(me, "points") is not { } pe)
                        throw new MapFileException($"{where}: a slide mover needs \"points\": [[x, y, z], [x, y, z], ...]");
                    m.Points = JsonList(pe, $"{where} (points)").Select((p, i) => Vec3(p, $"{where} (point {i + 1})")).ToArray();
                    if (m.Points.Length < 2)
                        throw new MapFileException($"{where}: a slide mover needs at least two points");
                    m.Speed = Opt("speed", 2f);
                    m.Spin = Opt("spin", 0f) * Deg;
                    m.Loop = Prop(me, "loop") is { ValueKind: JsonValueKind.True };
                    break;
                }
            }
            if (m.Speed < 0.05f || m.Speed > 30f)
                throw new MapFileException($"{where}: speed {m.Speed} m/s is outside 0.05..30");
            if (MathF.Abs(m.Spin) > 720 * Deg)
                throw new MapFileException($"{where}: spin is at most 720 degrees per second");
            m.Yaw0 = Opt("yaw", 0f) * Deg;
            m.Dwell = MathF.Max(0f, Opt("dwell", kind == MoverKind.Elevator ? 2f : kind == MoverKind.Slide ? 1f : 0f));
            m.Phase = Opt("phase", 0f);
            m.StartsRunning = Prop(me, "running") is not { ValueKind: JsonValueKind.False }; // false: stands still until map logic starts it (MapLogic.cs)
            m.ResetControl();
            var color = Prop(me, "color") is { } ce ? ToColour(ce, $"{where} (color)", "") : new Vector3(0.85f, 0.6f, 0.15f);

            if (Prop(me, "boxes") is not { } be)
                throw new MapFileException($"{where}: needs \"boxes\": [{{ \"size\": [w, h, d] }}, ...]");
            var parts = new List<MoverPart>();
            var bi = 0;
            foreach (var b in JsonList(be, $"{where} (boxes)"))
            {
                var bw = $"{where}, box #{++bi}";
                CheckKeys(b, MoverBoxKeys, bw);
                var size = Prop(b, "size") is { } se ? Vec3(se, $"{bw} (size)") : throw new MapFileException($"{bw}: needs \"size\": [w, h, d]");
                if (size.X < 0.05f || size.Y < 0.05f || size.Z < 0.05f || size.X > 60 || size.Y > 60 || size.Z > 60)
                    throw new MapFileException($"{bw}: each side of \"size\" must be 0.05..60 m");
                var half = size / 2;
                var offset = Prop(b, "offset") is { } oe ? Vec3(oe, $"{bw} (offset)") : new Vector3(0, -half.Y, 0);
                var yaw = Prop(b, "yaw") is { } ye ? Num(ye, $"{bw} (yaw)") * Deg : 0f;
                var c = Prop(b, "color") is { } bc ? ToColour(bc, $"{bw} (color)", "") : color;
                parts.Add(new MoverPart(offset, half, yaw, c));
            }
            if (parts.Count == 0 || parts.Count > MaxMoverBoxes)
                throw new MapFileException($"{where}: needs 1..{MaxMoverBoxes} boxes");
            m.Parts = parts.ToArray();
            m.Prepare();
            list.Add(m);
        }
        return list;
    }

    /// <summary>
    /// After the movers are known: drop waypoints inside a mover's sweep (bots couldn't reach them) and refuse
    /// spawns there (someone would spawn inside a moving platform).
    /// </summary>
    private static void CheckMoverSweeps(Map map, string source)
    {
        map.Waypoints.RemoveAll(w => map.InMoverSweep(w));
        foreach (var s in map.Spawns)
            if (map.InMoverSweep(s, 0.3f))
                throw new MapFileException($"{source}: spawn ({s.X:0.##}, {s.Y:0.##}, {s.Z:0.##}) is inside a mover's path; move it clear of the movers");
    }

    private static Vector3 Vec3(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 3)
            throw new MapFileException($"{where}: expected [x, y, z]");
        return new Vector3(Num(e[0], where), Num(e[1], where), Num(e[2], where));
    }
}
