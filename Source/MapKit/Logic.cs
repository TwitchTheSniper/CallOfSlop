namespace CallOfSlop.MapKit;

/// <summary>
/// The map kit's side of map logic (Maps/README.md, "Map logic"): <c>kit.Logic</c> collects entities and wires and writes
/// the map's "logic" section. Each helper records one entity with the fields the game reads, checked against the real
/// reader when the map is loaded back (<c>--gen-map</c>): a wire to an unknown entity, event or action fails there with
/// the reason. <see cref="Entity"/> takes any type and fields, for what the helpers don't cover.
/// </summary>
public sealed class LogicKit
{
    private readonly JArray _entities = new();
    private readonly JArray _wires = new();
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase) { "zombies", "match" };

    public bool IsEmpty => _entities.Count == 0;

    /// <summary>Any entity: its type, its name, then its fields as the map file has them.</summary>
    public LogicKit Entity(string type, string name, params (string Key, object? Value)[] fields)
    {
        if (!_names.Add(name))
            throw new MapKitException($"logic: two entities called \"{name}\"");
        var e = new JObject(("type", type), ("name", name));
        foreach (var (k, v) in fields)
            if (v != null)
                e.Add(k, v is double d ? Kit.R(d) : v);
        _entities.Add(e);
        return this;
    }

    private static JArray P(double x, double y, double z) => JArray.Of(Kit.R(x), Kit.R(y), Kit.R(z));

    private static JArray B(double x0, double y0, double z0, double x1, double y1, double z1) => JArray.Of(Kit.R(x0), Kit.R(y0), Kit.R(z0), Kit.R(x1), Kit.R(y1), Kit.R(z1));

    /// <summary>A button at (x, y, z) facing <paramref name="yaw"/> (its face points along the yaw: 0 = north, -Z). Cost: Zombies points; cooldown: seconds.</summary>
    public LogicKit Button(string name, double x, double y, double z, double yaw = 0, string? prompt = null, int? cost = null, double? cooldown = null, bool? once = null, bool? power = null) =>
        Entity("button", name, ("at", P(x, y, z)), ("yaw", yaw == 0 ? null : Kit.Yaw(yaw)), ("prompt", prompt), ("cost", cost), ("cooldown", cooldown), ("once", once), ("power", power));

    public LogicKit Lever(string name, double x, double y, double z, double yaw = 0, string? prompt = null, bool? on = null) =>
        Entity("lever", name, ("at", P(x, y, z)), ("yaw", yaw == 0 ? null : Kit.Yaw(yaw)), ("prompt", prompt), ("on", on));

    public LogicKit Plate(string name, double x0, double z0, double x1, double z1, double y = 0, string? who = null) =>
        Entity("plate", name, ("box", B(x0, y, z0, x1, y + 0.3, z1)), ("who", who));

    public LogicKit Zone(string name, double x0, double y0, double z0, double x1, double y1, double z1, string? who = null) =>
        Entity("zone", name, ("box", B(x0, y0, z0, x1, y1, z1)), ("who", who));

    public LogicKit Timer(string name, double interval, bool? repeat = null, bool? running = null) =>
        Entity("timer", name, ("interval", interval), ("repeat", repeat), ("running", running));

    /// <summary>A door: a solid box that opens (gone) and shuts; <paramref name="nav"/> false: no nav link through it.</summary>
    public LogicKit Door(string name, double x0, double y0, double z0, double x1, double y1, double z1, bool? open = null, Color? color = null, bool? nav = null) =>
        Entity("door", name, ("box", B(x0, y0, z0, x1, y1, z1)), ("open", open), ("color", color?.Node), ("nav", nav));

    public LogicKit Lamp(string name, double x0, double y0, double z0, double x1, double y1, double z1, bool? on = null, Color? color = null) =>
        Entity("lamp", name, ("box", B(x0, y0, z0, x1, y1, z1)), ("on", on), ("color", color?.Node));

    /// <summary>A mover from the map's "movers" section, by its name.</summary>
    public LogicKit Mover(string name, string mover) => Entity("mover", name, ("mover", mover));

    public LogicKit Sound(string name, string sound, double x, double y, double z, double? volume = null, double? range = null) =>
        Entity("sound", name, ("at", P(x, y, z)), ("sound", sound), ("volume", volume), ("range", range));

    public LogicKit Text(string name, string text, bool toActivator = false) => Entity("text", name, ("text", text), ("to", toActivator ? "activator" : null));

    /// <summary>A gate: "and", "or" (2-4 inputs a..d) or "not" (input "in").</summary>
    public LogicKit Gate(string type, string name, int? inputs = null) => Entity(type, name, ("inputs", inputs));

    public LogicKit Counter(string name, int target, bool? repeat = null) => Entity("counter", name, ("target", target), ("repeat", repeat));

    public LogicKit Relay(string name) => Entity("relay", name);

    public LogicKit Random(string name, int choices) => Entity("random", name, ("choices", choices));

    /// <summary>A wire: "Entity.event" to "Entity.action", with a delay in seconds, once only, a value or number to send instead, or a number to wait for.</summary>
    public LogicKit Wire(string from, string to, double? delay = null, bool? once = null, bool? value = null, int? n = null, int? when = null)
    {
        var w = new JObject(("from", from), ("to", to));
        if (delay is > 0)
            w.Add("delay", Kit.R(delay.Value));
        if (once == true)
            w.Add("once", true);
        if (value != null)
            w.Add("value", value.Value);
        if (n != null)
            w.Add("n", n.Value);
        if (when != null)
            w.Add("when", when.Value);
        _wires.Add(w);
        return this;
    }

    public JObject ToJson() => new(("entities", _entities), ("wires", _wires));
}
