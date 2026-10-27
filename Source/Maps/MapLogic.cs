using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a map logic entity is (Maps/README.md, "Map logic"). The ids are not sent over the network (entities go by index).</summary>
public enum LogicKind : byte
{
    Button, Lever, Plate, Zone, Timer, Relay, And, Or, Not, Counter, Random, Door, Lamp, Mover, Sound, Text, Teleport, Breakable,
    ZDoor, ZMachine,

    /// <summary>A timed challenge: survive it (standing, in its zone) and it pays its reward.</summary>
    Challenge,

    /// <summary>Gives Zombies points or drops a power-up.</summary>
    Reward,

    /// <summary>Built in: the Zombies run (power, rounds, spawning). Only on maps with a "zombies" section.</summary>
    Zombies,

    /// <summary>Built in: the match (start, end, flags and hills).</summary>
    Match,
}

/// <summary>An input of an entity: what a wire asks it to do. The name in a map file is the camelCase of the value.</summary>
public enum LogicAction : byte
{
    Enable, Disable, Press, Toggle, TurnOn, TurnOff, Set, Start, Stop, Reset, Trigger, A, B, C, D, In, Add, Subtract,
    Open, Close, Advance, Play, Show, Send, Destroy, Power, Spawn, Pause, Resume, Win, Fail, Give, GiveAll, Drop,
}

/// <summary>An output of an entity: something that happened to it. The name in a map file is the camelCase of the value.</summary>
public enum LogicEvent : byte
{
    Pressed, On, Off, Changed, Released, Entered, Left, Occupied, Empty, Fired, Out, True, False, Reached,
    Out1, Out2, Out3, Out4, Out5, Out6, Out7, Out8,
    Opened, Closed, Started, Stopped, Arrived, Sent, Destroyed, Used, Power, RoundStarted, RoundEnded, Ended,
    FlagTaken, FlagCaptured, HillTaken, HillMoved, Survived, Failed, Given,
}

/// <summary>
/// One signal on a wire: a value (true for most events; false for "off", "released", "left", "closed"...; the new state
/// for "changed" and a gate's "out"), a number (a counter's count, a round, a team, a zone's head count...) and who set
/// it off (a player id, or 255 for nobody). Wires may override the value and the number, and filter on the number.
/// </summary>
public readonly record struct LogicSignal(bool Value, int N, byte Activator)
{
    public const byte Nobody = byte.MaxValue;
    public static LogicSignal Pulse(byte activator = Nobody, int n = 0) => new(true, n, activator);
}

/// <summary>Who a zone or pressure plate counts.</summary>
public enum LogicWho : byte
{
    Players, // everyone alive: people and bots
    Humans,
    Bots,
}

/// <summary>Which Zombies machine a "zmachine" entity stands for.</summary>
public enum LogicMachine : byte
{
    Power, Box, Perk, SlotUpgrade, SlotMachine, WallBuy, Bench, Salvage,
}

/// <summary>
/// One named thing in a map's "logic" section. Only the fields its kind uses are set. Everything here is as the map file
/// says (the live state is the game's, <see cref="LogicState"/>), so a baked map reads back field for field.
/// </summary>
public sealed class LogicEntity
{
    public required string Name;
    public LogicKind Kind;

    /// <summary>Starts enabled (a disabled entity ignores everything but "enable").</summary>
    public bool Enabled = true;

    /// <summary>Buttons, levers, sounds, teleports, breakables, Zombies machines: where it is (world). Buttons and levers face <see cref="Yaw"/>.</summary>
    public Vector3 At;
    public float Yaw;

    /// <summary>Zones, plates, doors and lamps: the box (world).</summary>
    public Box Box;

    /// <summary>Buttons and levers: how far from your eyes it can be used.</summary>
    public float Reach = 2.2f;

    /// <summary>Buttons and levers: the hint ("ring the bell"); text: what it shows.</summary>
    public string Text = "";

    /// <summary>Buttons and levers: points it costs in Zombies (free in every other mode).</summary>
    public int Cost;

    /// <summary>Buttons and levers: ticks before it can be used again; timers: ticks between fires.</summary>
    public int Ticks;

    /// <summary>Buttons and levers: works once a match. Timers: fire again and again (<see cref="Repeat"/>). Counters: start again at 0 on reaching the target.</summary>
    public bool Once, Repeat;

    /// <summary>Buttons and levers: needs the Zombies power on.</summary>
    public bool NeedsPower;

    /// <summary>Levers and lamps: starts on. Doors: starts open. Timers: starts running.</summary>
    public bool StartOn;

    /// <summary>Buttons, levers and plates: drawn (false: the map draws its own and the entity is only the hot spot).</summary>
    public bool Draw = true;

    /// <summary>Buttons, levers, plates, doors: the colour; lamps: lit. <see cref="ColorOff"/>: an unlit lamp.</summary>
    public Vector3 Color, ColorOff;

    /// <summary>Zones and plates: who they count.</summary>
    public LogicWho Who;

    /// <summary>
    /// And / Or: how many inputs (2-4); counters: the target; random: how many outputs (2-8); sounds: the sound (an
    /// <see cref="Sfx"/>); movers: the mover (index in <see cref="Map.Movers"/>); Zombies doors: the door; Zombies machines:
    /// the machine's index in its list (-1: any of them, the box spots); breakables: the prop (in <see cref="Map.PropInstances"/>).
    /// </summary>
    public int Target;

    public LogicMachine Machine;

    /// <summary>Sounds: volume (0-2) and how far it carries in metres (0: heard everywhere, not placed).</summary>
    public float Volume = 1f, Range;

    /// <summary>Text: true for only the player who set it off (their notice line), false for everyone (the kill feed).</summary>
    public bool ToActivator;

    /// <summary>Doors: a nav link through the doorway (its two ends), switched on while the door is open. Index into <see cref="MapLogic.NavGates"/>, or -1.</summary>
    public int NavGate = -1;

    /// <summary>Challenges: the zone its players must stay in (an entity index, -1: anywhere); rewards: the power-up it drops (a <see cref="ZPowerKind"/>, COUNT: a random one, -1: none).</summary>
    public int Zone = -1;

    /// <summary>Challenges: Zombies points each survivor gets; rewards: points each player given it gets.</summary>
    public int Points;

    /// <summary>Doors: the box in <see cref="Map.Solid"/>.</summary>
    public int SolidIndex = -1;
}

/// <summary>A wire: when entity <see cref="From"/> sends <see cref="Event"/>, entity <see cref="To"/> gets <see cref="Action"/>.</summary>
public readonly record struct LogicWire(int From, LogicEvent Event, int To, LogicAction Action, int DelayTicks, bool Once, sbyte Value, int N, int When)
{
    /// <summary><see cref="N"/> and <see cref="When"/> when the wire doesn't set them.</summary>
    public const int NoNumber = int.MinValue;
}

/// <summary>
/// A map's "logic" section (Maps/README.md, "Map logic"; the signal model is documented there): named entities with
/// outputs (events) wired to other entities' inputs (actions), with delays, once-only wires and simple logic. Read and
/// checked when the map is built: an unknown name, event or action is refused with the reason. The game runs it
/// (<see cref="Game"/>, GameLogic.cs), on the server only, event by event.
/// </summary>
public sealed class MapLogic
{
    public const int MaxEntities = 256, MaxWires = 1024, MaxZones = 64, MaxDoors = 64;

    public static readonly int EventCount = Enum.GetValues<LogicEvent>().Length;

    public readonly List<LogicEntity> Entities = new();

    /// <summary>Every wire, sorted by (From, Event): the wires out of one event are <see cref="WireFirst"/>..<see cref="WireEnd"/>.</summary>
    public LogicWire[] Wires = [];

    /// <summary>Per (entity * <see cref="EventCount"/> + event): its first wire and one past its last (equal: none).</summary>
    public int[] WireFirst = [], WireEnd = [];

    /// <summary>Nav links through logic doors: (one end, the other end). Also in <see cref="Map.NavLinks"/>, so the nav graph has them as built.</summary>
    public readonly List<(Vector3 A, Vector3 B)> NavGates = new();

    /// <summary>Entities that are zones or plates (sampled each tick against the players), doors, and the built-ins (-1: none).</summary>
    public int[] Zones = [], Doors = [], Challenges = [];
    public int ZombiesEntity = -1, MatchEntity = -1;

    /// <summary>The section as read, written back unchanged when a map is saved.</summary>
    public string Raw = "{}";

    /// <summary>The section's fingerprint (mixed into <see cref="Map.ContentHash"/>).</summary>
    public uint Hash;

    public int Find(string name) => Entities.FindIndex(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public ReadOnlySpan<LogicWire> WiresOut(int entity, LogicEvent ev)
    {
        var k = entity * EventCount + (int)ev;
        return (uint)k < (uint)WireFirst.Length ? Wires.AsSpan(WireFirst[k], WireEnd[k] - WireFirst[k]) : default;
    }

    // ---- Ports ------------------------------------------------------------------------------------------------

    /// <summary>The camelCase name of an action or event, as written in a map file.</summary>
    public static string PortName<T>(T value) where T : struct, Enum
    {
        var s = value.ToString();
        return char.ToLowerInvariant(s[0]) + s[1..];
    }

    public static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];

    private static readonly LogicAction[] Common = [LogicAction.Enable, LogicAction.Disable];

    /// <summary>The actions each kind takes (besides "enable" and "disable", which every entity takes).</summary>
    public static LogicAction[] Actions(LogicKind k) => k switch
    {
        LogicKind.Button => [LogicAction.Press],
        LogicKind.Lever => [LogicAction.Toggle, LogicAction.TurnOn, LogicAction.TurnOff, LogicAction.Set],
        LogicKind.Timer => [LogicAction.Start, LogicAction.Stop, LogicAction.Toggle, LogicAction.Reset],
        LogicKind.Relay => [LogicAction.Trigger],
        LogicKind.And or LogicKind.Or => [LogicAction.A, LogicAction.B, LogicAction.C, LogicAction.D, LogicAction.Reset],
        LogicKind.Not => [LogicAction.In],
        LogicKind.Counter => [LogicAction.Add, LogicAction.Subtract, LogicAction.Reset, LogicAction.Set],
        LogicKind.Random => [LogicAction.Trigger],
        LogicKind.Door => [LogicAction.Open, LogicAction.Close, LogicAction.Toggle, LogicAction.Set],
        LogicKind.Lamp => [LogicAction.TurnOn, LogicAction.TurnOff, LogicAction.Toggle, LogicAction.Set],
        LogicKind.Mover => [LogicAction.Start, LogicAction.Stop, LogicAction.Toggle, LogicAction.Advance, LogicAction.Reset],
        LogicKind.Sound => [LogicAction.Play],
        LogicKind.Text => [LogicAction.Show],
        LogicKind.Teleport => [LogicAction.Send],
        LogicKind.Breakable => [LogicAction.Destroy],
        LogicKind.ZDoor => [LogicAction.Open],
        LogicKind.Zombies => [LogicAction.Power, LogicAction.Spawn, LogicAction.Pause, LogicAction.Resume],
        LogicKind.Challenge => [LogicAction.Start, LogicAction.Stop, LogicAction.Win, LogicAction.Fail],
        LogicKind.Reward => [LogicAction.Give, LogicAction.GiveAll, LogicAction.Drop],
        _ => [],
    };

    /// <summary>The events each kind sends.</summary>
    public static LogicEvent[] Events(LogicKind k) => k switch
    {
        LogicKind.Button => [LogicEvent.Pressed],
        LogicKind.Lever => [LogicEvent.On, LogicEvent.Off, LogicEvent.Changed],
        LogicKind.Plate => [LogicEvent.Pressed, LogicEvent.Released, LogicEvent.Changed],
        LogicKind.Zone => [LogicEvent.Entered, LogicEvent.Left, LogicEvent.Occupied, LogicEvent.Empty, LogicEvent.Changed],
        LogicKind.Timer => [LogicEvent.Fired],
        LogicKind.Relay => [LogicEvent.Out],
        LogicKind.And or LogicKind.Or or LogicKind.Not => [LogicEvent.Out, LogicEvent.True, LogicEvent.False],
        LogicKind.Counter => [LogicEvent.Reached, LogicEvent.Changed],
        LogicKind.Random => [LogicEvent.Out1, LogicEvent.Out2, LogicEvent.Out3, LogicEvent.Out4, LogicEvent.Out5, LogicEvent.Out6, LogicEvent.Out7, LogicEvent.Out8],
        LogicKind.Door => [LogicEvent.Opened, LogicEvent.Closed],
        LogicKind.Lamp => [LogicEvent.On, LogicEvent.Off],
        LogicKind.Mover => [LogicEvent.Started, LogicEvent.Stopped, LogicEvent.Arrived],
        LogicKind.Teleport => [LogicEvent.Sent],
        LogicKind.Breakable => [LogicEvent.Destroyed],
        LogicKind.ZDoor => [LogicEvent.Opened],
        LogicKind.ZMachine => [LogicEvent.Used],
        LogicKind.Zombies => [LogicEvent.Power, LogicEvent.RoundStarted, LogicEvent.RoundEnded],
        LogicKind.Match => [LogicEvent.Started, LogicEvent.Ended, LogicEvent.FlagTaken, LogicEvent.FlagCaptured, LogicEvent.HillTaken, LogicEvent.HillMoved],
        LogicKind.Challenge => [LogicEvent.Started, LogicEvent.Survived, LogicEvent.Failed, LogicEvent.Ended],
        LogicKind.Reward => [LogicEvent.Given],
        _ => [],
    };

    public static bool Takes(LogicEntity e, LogicAction a) =>
        Common.Contains(a) || Actions(e.Kind) is var acts && Array.IndexOf(acts, a) >= 0 && (e.Kind is not (LogicKind.And or LogicKind.Or) || a == LogicAction.Reset || a - LogicAction.A < e.Target);

    public static bool Sends(LogicEntity e, LogicEvent ev) =>
        Array.IndexOf(Events(e.Kind), ev) >= 0 && (e.Kind != LogicKind.Random || ev - LogicEvent.Out1 < e.Target);

    // ---- Reading ----------------------------------------------------------------------------------------------

    private static readonly string[] Keys = ["entities", "wires"];

    private static readonly Dictionary<LogicKind, string[]> KindKeys = new()
    {
        [LogicKind.Button] = ["at", "yaw", "reach", "prompt", "cost", "cooldown", "once", "power", "draw", "color"],
        [LogicKind.Lever] = ["at", "yaw", "reach", "prompt", "cost", "cooldown", "once", "power", "draw", "color", "on"],
        [LogicKind.Plate] = ["box", "who", "draw", "color"],
        [LogicKind.Zone] = ["box", "who"],
        [LogicKind.Timer] = ["interval", "repeat", "running"],
        [LogicKind.Relay] = [],
        [LogicKind.And] = ["inputs"],
        [LogicKind.Or] = ["inputs"],
        [LogicKind.Not] = [],
        [LogicKind.Counter] = ["target", "repeat"],
        [LogicKind.Random] = ["choices"],
        [LogicKind.Door] = ["box", "open", "color", "nav"],
        [LogicKind.Lamp] = ["box", "on", "color", "offColor"],
        [LogicKind.Mover] = ["mover"],
        [LogicKind.Sound] = ["at", "sound", "volume", "range"],
        [LogicKind.Text] = ["text", "to"],
        [LogicKind.Teleport] = ["at", "yaw"],
        [LogicKind.Breakable] = ["at"],
        [LogicKind.ZDoor] = ["door"],
        [LogicKind.ZMachine] = ["machine", "at"],
        [LogicKind.Challenge] = ["seconds", "zone", "who", "reward"],
        [LogicKind.Reward] = ["points", "powerUp", "at"],
    };

    private static readonly string[] CommonKeys = ["type", "name", "enabled", "outputs"];
    private static readonly string[] WireKeys = ["from", "to", "delay", "once", "value", "n", "when"];

    /// <summary>The kinds a map file may declare (the built-ins "zombies" and "match" are always there).</summary>
    public static IEnumerable<string> TypeNames => KindKeys.Keys.Select(KindName);

    public static MapLogic Read(JsonElement e, Map map, string source)
    {
        var where = $"{source}: logic";
        Only(e, where, Keys);
        var logic = new MapLogic { Raw = e.GetRawText(), Hash = HashText(JsonSerializer.Serialize(e)) };
        var pending = new List<(int From, string Event, JsonElement Wire, string Where)>();
        var challengeZones = new List<(int Entity, string Zone)>();

        if (Get(e, "entities") is not { ValueKind: JsonValueKind.Array } ents)
            throw new MapFileException($"{where}: needs \"entities\": [ {{ \"type\": \"button\", \"name\": \"GateButton\", ... }} ]");
        var index = 0;
        foreach (var je in ents.EnumerateArray())
        {
            var w = $"{where}, entity #{++index}";
            if (je.ValueKind != JsonValueKind.Object)
                throw new MapFileException($"{w}: expected an object");
            var typeName = Get(je, "type") is { ValueKind: JsonValueKind.String } te ? te.GetString()! : throw new MapFileException($"{w}: needs \"type\" ({string.Join(", ", TypeNames)})");
            var kind = KindKeys.Keys.FirstOrDefault(k => KindName(k).Equals(typeName, StringComparison.OrdinalIgnoreCase), (LogicKind)255);
            if ((byte)kind == 255)
                throw new MapFileException($"{w}: unknown type \"{typeName}\" (known: {string.Join(", ", TypeNames)})");
            var name = Get(je, "name") is { ValueKind: JsonValueKind.String } ne ? ne.GetString()! : throw new MapFileException($"{w}: a {typeName} needs a \"name\"");
            w = $"{where}, {typeName} \"{name}\"";
            if (name.Length is 0 or > 32 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
                throw new MapFileException($"{w}: a name is 1 to 32 letters, digits, '_' or '-' (no spaces or dots: wires write \"Name.event\")");
            if (name.Equals("zombies", StringComparison.OrdinalIgnoreCase) || name.Equals("match", StringComparison.OrdinalIgnoreCase))
                throw new MapFileException($"{w}: \"{name}\" is a built-in entity's name");
            if (logic.Find(name) >= 0)
                throw new MapFileException($"{where}: two entities called \"{name}\"");
            Only(je, w, [.. CommonKeys, .. KindKeys[kind]]);
            var ent = new LogicEntity { Name = name, Kind = kind };
            ent.Enabled = Get(je, "enabled") is not { ValueKind: JsonValueKind.False };
            ReadFields(ent, je, map, w);
            logic.Entities.Add(ent);
            if (ent.Kind == LogicKind.Challenge && Get(je, "zone") is { } cz)
                challengeZones.Add((logic.Entities.Count - 1, Text(cz, $"{w} (zone)")));
            if (Get(je, "outputs") is { } outs)
            {
                if (outs.ValueKind != JsonValueKind.Object)
                    throw new MapFileException($"{w}: \"outputs\" is {{ \"event\": [\"Other.action\", ...] }}");
                foreach (var o in outs.EnumerateObject())
                {
                    var list = o.Value.ValueKind == JsonValueKind.Array ? o.Value.EnumerateArray().ToArray() : [o.Value];
                    foreach (var wire in list)
                        pending.Add((logic.Entities.Count - 1, o.Name, wire, $"{w}, outputs.{o.Name}"));
                }
            }
        }
        if (logic.Entities.Count > MaxEntities)
            throw new MapFileException($"{where}: at most {MaxEntities} entities");
        foreach (var (ci, zoneName) in challengeZones)
        {
            var zi = logic.Ref(zoneName, $"{where}, challenge \"{logic.Entities[ci].Name}\" (zone)");
            if (logic.Entities[zi].Kind is not (LogicKind.Zone or LogicKind.Plate))
                throw new MapFileException($"{where}, challenge \"{logic.Entities[ci].Name}\": \"zone\" names a zone, and \"{zoneName}\" is a {KindName(logic.Entities[zi].Kind)}");
            logic.Entities[ci].Zone = zi;
        }
        if (map.Zombies != null)
        {
            logic.ZombiesEntity = logic.Entities.Count;
            logic.Entities.Add(new LogicEntity { Name = "zombies", Kind = LogicKind.Zombies });
        }
        logic.MatchEntity = logic.Entities.Count;
        logic.Entities.Add(new LogicEntity { Name = "match", Kind = LogicKind.Match });

        if (Get(e, "wires") is { } wires)
        {
            if (wires.ValueKind != JsonValueKind.Array)
                throw new MapFileException($"{where}: \"wires\" is a list [ {{ \"from\": \"A.event\", \"to\": \"B.action\" }} ]");
            var wi = 0;
            foreach (var w in wires.EnumerateArray())
            {
                var ww = $"{where}, wire #{++wi}";
                Only(w, ww, WireKeys);
                var from = Get(w, "from") is { ValueKind: JsonValueKind.String } fe ? fe.GetString()! : throw new MapFileException($"{ww}: needs \"from\": \"Entity.event\"");
                var dot = from.LastIndexOf('.');
                if (dot <= 0)
                    throw new MapFileException($"{ww}: \"from\" is \"Entity.event\", got \"{from}\"");
                var src = logic.Ref(from[..dot], ww);
                pending.Add((src, from[(dot + 1)..], w, ww));
            }
        }

        var list2 = new List<LogicWire>();
        foreach (var (from, evName, w, ww) in pending)
            list2.Add(logic.ReadWire(from, evName, w, ww, map));
        if (list2.Count > MaxWires)
            throw new MapFileException($"{where}: at most {MaxWires} wires");
        logic.Wires = list2.OrderBy(x => x.From).ThenBy(x => (int)x.Event).ToArray(); // (a stable sort: wires out of one event keep the file's order)
        logic.WireFirst = new int[logic.Entities.Count * EventCount];
        logic.WireEnd = new int[logic.WireFirst.Length];
        for (var i = 0; i < logic.Wires.Length; i++)
        {
            var k = logic.Wires[i].From * EventCount + (int)logic.Wires[i].Event;
            if (logic.WireEnd[k] == 0)
                logic.WireFirst[k] = i;
            logic.WireEnd[k] = i + 1;
        }

        logic.Zones = Enumerable.Range(0, logic.Entities.Count).Where(i => logic.Entities[i].Kind is LogicKind.Zone or LogicKind.Plate).ToArray();
        logic.Doors = Enumerable.Range(0, logic.Entities.Count).Where(i => logic.Entities[i].Kind == LogicKind.Door).ToArray();
        logic.Challenges = Enumerable.Range(0, logic.Entities.Count).Where(i => logic.Entities[i].Kind == LogicKind.Challenge).ToArray();
        if (logic.Zones.Length > MaxZones)
            throw new MapFileException($"{where}: at most {MaxZones} zones and plates");
        if (logic.Doors.Length > MaxDoors)
            throw new MapFileException($"{where}: at most {MaxDoors} doors");
        return logic;
    }

    /// <summary>An entity by name, or a readable refusal (with the nearest name).</summary>
    private int Ref(string name, string where)
    {
        var i = Find(name);
        if (i >= 0)
            return i;
        if (name.Equals("zombies", StringComparison.OrdinalIgnoreCase))
            throw new MapFileException($"{where}: \"zombies\" is only there on a map with a \"zombies\" section");
        var near = Entities.Select(x => x.Name).OrderBy(n => Distance(n.ToLowerInvariant(), name.ToLowerInvariant())).FirstOrDefault();
        throw new MapFileException($"{where}: no entity called \"{name}\"{(near != null && Distance(near.ToLowerInvariant(), name.ToLowerInvariant()) <= 3 ? $" (did you mean \"{near}\"?)" : "")}");
    }

    private LogicWire ReadWire(int from, string evName, JsonElement w, string where, Map map)
    {
        var src = Entities[from];
        var events = Events(src.Kind).Where(ev => Sends(src, ev)).ToArray();
        var ev = events.FirstOrDefault(x => PortName(x).Equals(evName, StringComparison.OrdinalIgnoreCase), (LogicEvent)255);
        if ((byte)ev == 255)
        {
            var also = Actions(src.Kind).Concat(Common).Any(a => PortName(a).Equals(evName, StringComparison.OrdinalIgnoreCase)) ? $" (\"{evName}\" is one of its actions, not an event)" : "";
            throw new MapFileException(events.Length == 0
                ? $"{where}: {src.Name} ({KindName(src.Kind)}) sends no events{also}"
                : $"{where}: {src.Name} ({KindName(src.Kind)}) has no event \"{evName}\"{also}; its events: {string.Join(", ", events.Select(PortName))}");
        }
        string to;
        float delay = 0;
        var once = false;
        sbyte value = -1;
        int n = LogicWire.NoNumber, when = LogicWire.NoNumber;
        if (w.ValueKind == JsonValueKind.String)
            to = w.GetString()!;
        else
        {
            Only(w, where, WireKeys);
            to = Get(w, "to") is { ValueKind: JsonValueKind.String } te ? te.GetString()! : throw new MapFileException($"{where}: a wire needs \"to\": \"Entity.action\"");
            if (Get(w, "delay") is { } de)
                delay = Num(de, $"{where} (delay)");
            if (delay is < 0 or > 3600)
                throw new MapFileException($"{where}: \"delay\" is 0 to 3600 seconds");
            once = Get(w, "once") is { ValueKind: JsonValueKind.True };
            if (Get(w, "value") is { } ve)
                value = ve.ValueKind switch
                {
                    JsonValueKind.True => 1,
                    JsonValueKind.False => 0,
                    _ => throw new MapFileException($"{where}: \"value\" is true or false"),
                };
            if (Get(w, "n") is { } nn)
                n = Whole(nn, $"{where} (n)", -1000000, 1000000);
            if (Get(w, "when") is { } we)
                when = Whole(we, $"{where} (when)", -1000000, 1000000);
        }
        var dot = to.LastIndexOf('.');
        if (dot <= 0)
            throw new MapFileException($"{where}: \"to\" is \"Entity.action\", got \"{to}\"");
        var dst = Ref(to[..dot], where);
        var target = Entities[dst];
        var actName = to[(dot + 1)..];
        var acts = Common.Concat(Actions(target.Kind)).Where(a => Takes(target, a)).ToArray();
        var act = acts.FirstOrDefault(x => PortName(x).Equals(actName, StringComparison.OrdinalIgnoreCase), (LogicAction)255);
        if ((byte)act == 255)
        {
            var also = Events(target.Kind).Any(x => PortName(x).Equals(actName, StringComparison.OrdinalIgnoreCase)) ? $" (\"{actName}\" is one of its events, not an action)" : "";
            throw new MapFileException($"{where}: {target.Name} ({KindName(target.Kind)}) has no action \"{actName}\"{also}; its actions: {string.Join(", ", acts.Select(PortName))}");
        }
        if (target.Kind == LogicKind.Zombies && act == LogicAction.Spawn && n == LogicWire.NoNumber)
            throw new MapFileException($"{where}: zombies.spawn needs \"n\": how many zombies to add to the round");
        return new LogicWire(from, ev, dst, act, (int)MathF.Round(delay * World.TickRate), once, value, n, when);
    }

    public static string KindName(LogicKind k) => k switch
    {
        LogicKind.ZDoor => "zdoor",
        LogicKind.ZMachine => "zmachine",
        _ => k.ToString().ToLowerInvariant(),
    };

    private static void ReadFields(LogicEntity ent, JsonElement je, Map map, string w)
    {
        float Opt(string key, float def) => Get(je, key) is { } v ? Num(v, $"{w} ({key})") : def;
        bool Flag(string key, bool def) => Get(je, key) switch
        {
            null => def,
            { ValueKind: JsonValueKind.True } => true,
            { ValueKind: JsonValueKind.False } => false,
            var v => throw new MapFileException($"{w}: \"{key}\" is true or false, got {v}"),
        };
        Vector3 Col(string key, Vector3 def) => Get(je, key) is { } c ? MapFile.ToColour(c, $"{w} ({key})", "") : def;
        Box NeedBox()
        {
            var b = Nums(Need(je, "box", w), 6, $"{w} (box)");
            var lo = new Vector3(MathF.Min(b[0], b[3]), MathF.Min(b[1], b[4]), MathF.Min(b[2], b[5]));
            var hi = new Vector3(MathF.Max(b[0], b[3]), MathF.Max(b[1], b[4]), MathF.Max(b[2], b[5]));
            if (hi.X - lo.X < 0.05f || hi.Y - lo.Y < 0.05f || hi.Z - lo.Z < 0.05f || hi.X - lo.X > 200 || hi.Y - lo.Y > 200 || hi.Z - lo.Z > 200)
                throw new MapFileException($"{w}: each side of \"box\" must be 0.05 to 200 m");
            return new Box((lo + hi) / 2, (hi - lo) / 2);
        }
        Vector3 NeedAt() => Point(map, Need(je, "at", w), $"{w} (at)");
        string Prompt(string def)
        {
            var s = Get(je, "prompt") is { } p ? Text(p, $"{w} (prompt)") : def;
            return Ascii(s, $"{w} (prompt)", 60);
        }
        LogicWho WhoOf() => Get(je, "who") is { } wv ? Text(wv, $"{w} (who)").ToLowerInvariant() switch
        {
            "players" => LogicWho.Players,
            "humans" => LogicWho.Humans,
            "bots" => LogicWho.Bots,
            var s => throw new MapFileException($"{w}: \"who\" is \"players\", \"humans\" or \"bots\", got \"{s}\""),
        } : LogicWho.Players;

        switch (ent.Kind)
        {
            case LogicKind.Button:
            case LogicKind.Lever:
                ent.At = NeedAt();
                ent.Yaw = Opt("yaw", 0) * MathF.PI / 180f;
                ent.Reach = Opt("reach", 2.2f);
                if (ent.Reach is < 0.5f or > 6f)
                    throw new MapFileException($"{w}: \"reach\" is 0.5 to 6 m");
                ent.Text = Prompt(ent.Kind == LogicKind.Button ? "press the button" : "pull the lever");
                ent.Cost = Get(je, "cost") is { } ce ? Whole(ce, $"{w} (cost)", 0, 1000000) : 0;
                var cd = Opt("cooldown", ent.Kind == LogicKind.Button ? 1f : 0.5f);
                if (cd is < 0 or > 3600)
                    throw new MapFileException($"{w}: \"cooldown\" is 0 to 3600 seconds");
                ent.Ticks = (int)MathF.Round(cd * World.TickRate);
                ent.Once = Flag("once", false);
                ent.NeedsPower = Flag("power", false);
                if (ent.NeedsPower && map.Zombies == null)
                    throw new MapFileException($"{w}: \"power\": true needs a map with a \"zombies\" section");
                ent.Draw = Flag("draw", true);
                ent.Color = Col("color", ent.Kind == LogicKind.Button ? new Vector3(0.85f, 0.15f, 0.12f) : new Vector3(0.9f, 0.75f, 0.2f));
                ent.StartOn = Flag("on", false);
                break;
            case LogicKind.Plate:
            case LogicKind.Zone:
                ent.Box = NeedBox();
                ent.Who = WhoOf();
                ent.Draw = ent.Kind == LogicKind.Plate && Flag("draw", true);
                ent.Color = Col("color", new Vector3(0.55f, 0.55f, 0.6f));
                break;
            case LogicKind.Timer:
                var iv = Opt("interval", -1);
                if (iv is < 0.1f or > 3600)
                    throw new MapFileException($"{w}: a timer needs \"interval\": 0.1 to 3600 seconds");
                ent.Ticks = Math.Max(1, (int)MathF.Round(iv * World.TickRate));
                ent.Repeat = Flag("repeat", true);
                ent.StartOn = Flag("running", true);
                break;
            case LogicKind.And:
            case LogicKind.Or:
                ent.Target = Get(je, "inputs") is { } ie ? Whole(ie, $"{w} (inputs)", 2, 4) : 2;
                break;
            case LogicKind.Counter:
                ent.Target = Get(je, "target") is { } tg ? Whole(tg, $"{w} (target)", 1, 1000000) : throw new MapFileException($"{w}: a counter needs \"target\" (a whole number)");
                ent.Repeat = Flag("repeat", false);
                break;
            case LogicKind.Random:
                ent.Target = Get(je, "choices") is { } oe ? Whole(oe, $"{w} (choices)", 2, 8) : 2;
                break;
            case LogicKind.Door:
                ent.Box = NeedBox();
                ent.StartOn = Flag("open", false);
                ent.Color = Col("color", new Vector3(0.45f, 0.42f, 0.4f));
                ent.NavGate = -1;
                break;
            case LogicKind.Lamp:
                ent.Box = NeedBox();
                ent.StartOn = Flag("on", false);
                ent.Color = Col("color", new Vector3(1f, 0.85f, 0.45f));
                ent.ColorOff = Col("offColor", ent.Color * 0.22f);
                break;
            case LogicKind.Mover:
            {
                var mn = Text(Need(je, "mover", w), $"{w} (mover)");
                ent.Target = map.Movers.FindIndex(m => m.Name.Equals(mn, StringComparison.OrdinalIgnoreCase));
                if (ent.Target < 0)
                    throw new MapFileException($"{w}: no mover called \"{mn}\" in \"movers\" (named: {string.Join(", ", map.Movers.Where(m => m.Name.Length > 0).Select(m => m.Name))})");
                break;
            }
            case LogicKind.Sound:
            {
                ent.At = NeedAt();
                var sn = Text(Need(je, "sound", w), $"{w} (sound)");
                if (!Enum.TryParse<Sfx>(sn, true, out var sfx) || !Enum.IsDefined(sfx) || int.TryParse(sn, out _))
                    throw new MapFileException($"{w}: unknown sound \"{sn}\" (known: {string.Join(", ", Enum.GetNames<Sfx>().Select(Camel))})");
                ent.Target = (int)sfx;
                ent.Volume = Opt("volume", 1f);
                ent.Range = Opt("range", 40f);
                if (ent.Volume is < 0 or > 2 || ent.Range is < 0 or > 500)
                    throw new MapFileException($"{w}: \"volume\" is 0 to 2, \"range\" 0 to 500 m (0: heard everywhere)");
                break;
            }
            case LogicKind.Text:
                ent.Text = Ascii(Text(Need(je, "text", w), $"{w} (text)"), $"{w} (text)", 100);
                ent.ToActivator = Get(je, "to") is { } to && Text(to, $"{w} (to)").ToLowerInvariant() switch
                {
                    "all" => false,
                    "activator" => true,
                    var s => throw new MapFileException($"{w}: \"to\" is \"all\" (the kill feed) or \"activator\" (whoever set it off), got \"{s}\""),
                };
                break;
            case LogicKind.Teleport:
                ent.At = NeedAt();
                ent.Yaw = Opt("yaw", 0) * MathF.PI / 180f;
                break;
            case LogicKind.Breakable:
            {
                ent.At = NeedAt();
                var best = -1;
                var bestD = 1.5f;
                for (var i = 0; i < map.PropInstances.Count; i++)
                {
                    var pi = map.PropInstances[i];
                    var d = Vector2.Distance(new Vector2(pi.Position.X, pi.Position.Z), new Vector2(ent.At.X, ent.At.Z));
                    if (pi.Def.Breakable != null && d < bestD && MathF.Abs(pi.Position.Y - ent.At.Y) < 2f)
                    {
                        bestD = d;
                        best = i;
                    }
                }
                ent.Target = best >= 0 ? best : throw new MapFileException($"{w}: no breakable prop within 1.5 m of ({ent.At.X:0.##}, {ent.At.Y:0.##}, {ent.At.Z:0.##}) (place one with a Prop op first)");
                break;
            }
            case LogicKind.Challenge:
            {
                var secs = Opt("seconds", -1);
                if (secs is < 1 or > 3600)
                    throw new MapFileException($"{w}: a challenge needs \"seconds\": 1 to 3600 (how long to survive)");
                ent.Ticks = (int)MathF.Round(secs * World.TickRate);
                ent.Who = WhoOf();
                ent.Points = Get(je, "reward") is { } rw ? Whole(rw, $"{w} (reward)", 0, 1000000) : 0;
                break;
            }
            case LogicKind.Reward:
            {
                ent.Points = Get(je, "points") is { } pt ? Whole(pt, $"{w} (points)", 0, 1000000) : 0;
                ent.Zone = -1;
                if (Get(je, "powerUp") is { } pu)
                {
                    var pn = Text(pu, $"{w} (powerUp)");
                    if (pn.Equals("random", StringComparison.OrdinalIgnoreCase))
                        ent.Zone = (int)ZPowerKind.COUNT;
                    else if (Enum.TryParse<ZPowerKind>(pn, true, out var pk) && pk != ZPowerKind.COUNT && Enum.IsDefined(pk) && !int.TryParse(pn, out _))
                        ent.Zone = (int)pk;
                    else
                        throw new MapFileException($"{w}: \"powerUp\" is \"random\" or one of {string.Join(", ", Enum.GetNames<ZPowerKind>().Where(n => n != "COUNT").Select(Camel))}, got \"{pn}\"");
                    ent.At = Get(je, "at") is { } ra ? Point(map, ra, $"{w} (at)") : throw new MapFileException($"{w}: a power-up reward needs \"at\" (where it drops)");
                }
                if (ent.Points == 0 && ent.Zone < 0)
                    throw new MapFileException($"{w}: a reward needs \"points\" or a \"powerUp\"");
                break;
            }
            case LogicKind.ZDoor:
            {
                if (map.Zombies is not { } zm)
                    throw new MapFileException($"{w}: a zdoor needs a map with a \"zombies\" section");
                var dn = Text(Need(je, "door", w), $"{w} (door)");
                ent.Target = zm.Doors.FindIndex(d => d.Name.Equals(dn, StringComparison.OrdinalIgnoreCase));
                if (ent.Target < 0)
                    throw new MapFileException($"{w}: no Zombies door called \"{dn}\" (doors: {string.Join(", ", zm.Doors.Select(d => d.Name))})");
                break;
            }
            case LogicKind.ZMachine:
            {
                if (map.Zombies is not { } zm)
                    throw new MapFileException($"{w}: a zmachine needs a map with a \"zombies\" section");
                var mk = Text(Need(je, "machine", w), $"{w} (machine)");
                if (!Enum.TryParse<LogicMachine>(mk, true, out var machine) || !Enum.IsDefined(machine) || int.TryParse(mk, out _))
                    throw new MapFileException($"{w}: \"machine\" is one of {string.Join(", ", Enum.GetNames<LogicMachine>().Select(Camel))}, got \"{mk}\"");
                ent.Machine = machine;
                var spots = machine switch
                {
                    LogicMachine.Power => zm.Power is { } pw ? [pw.At] : Array.Empty<Vector3>(),
                    LogicMachine.SlotUpgrade => zm.SlotUpgrade is { } su ? [su.At] : [],
                    LogicMachine.Box => zm.BoxSpots.Select(s => s.At).ToArray(),
                    LogicMachine.Perk => zm.PerkMachines.Select(s => s.At).ToArray(),
                    LogicMachine.SlotMachine => zm.SlotMachines.Select(s => s.At).ToArray(),
                    LogicMachine.WallBuy => zm.WallBuys.Select(s => s.At).ToArray(),
                    LogicMachine.Bench => zm.Benches.Select(s => s.At).ToArray(),
                    _ => zm.Salvage.Select(s => s.At).ToArray(),
                };
                if (spots.Length == 0)
                    throw new MapFileException($"{w}: the map's \"zombies\" section has no {PortName(machine)}");
                if (Get(je, "at") is { } ae)
                {
                    ent.At = Point(map, ae, $"{w} (at)");
                    ent.Target = -1;
                    var bestD = 2.5f;
                    for (var i = 0; i < spots.Length; i++)
                    {
                        var d = Vector2.Distance(new Vector2(spots[i].X, spots[i].Z), new Vector2(ent.At.X, ent.At.Z));
                        if (d < bestD)
                        {
                            bestD = d;
                            ent.Target = i;
                        }
                    }
                    if (ent.Target < 0)
                        throw new MapFileException($"{w}: no {PortName(machine)} within 2.5 m of ({ent.At.X:0.##}, {ent.At.Z:0.##})");
                }
                else if (spots.Length == 1 || machine == LogicMachine.Box)
                    ent.Target = machine == LogicMachine.Box ? -1 : 0; // (the box moves between its spots: any of them)
                else
                    throw new MapFileException($"{w}: the map has {spots.Length} of {PortName(machine)}: say which with \"at\": [x, z]");
                break;
            }
        }
        if (ent.Kind == LogicKind.Door)
            ReadDoorNav(ent, je, map, w);
    }

    /// <summary>A door's nav link: "nav": true (default: across its thin side, 0.9 m out, on the floor), false, or [[x, y, z], [x, y, z]].</summary>
    private static void ReadDoorNav(LogicEntity ent, JsonElement je, Map map, string w)
    {
        var nav = Get(je, "nav");
        if (nav is { ValueKind: JsonValueKind.False })
            return;
        var b = ent.Box;
        Vector3 a, c;
        if (nav is { ValueKind: JsonValueKind.Array } arr)
        {
            if (arr.GetArrayLength() != 2)
                throw new MapFileException($"{w}: \"nav\" is true, false or the two ends [[x, y, z], [x, y, z]]");
            a = Point(map, arr[0], $"{w} (nav)");
            c = Point(map, arr[1], $"{w} (nav)");
        }
        else if (nav is null or { ValueKind: JsonValueKind.True })
        {
            var thinX = b.Half.X <= b.Half.Z;
            var off = (thinX ? b.Half.X : b.Half.Z) + 0.9f;
            var axis = thinX ? Vector3.UnitX : Vector3.UnitZ;
            var floor = b.Min.Y;
            a = new Vector3(b.Center.X, floor, b.Center.Z) + axis * off;
            c = new Vector3(b.Center.X, floor, b.Center.Z) - axis * off;
        }
        else
            throw new MapFileException($"{w}: \"nav\" is true, false or the two ends [[x, y, z], [x, y, z]]");
        if (Vector3.Distance(a, c) > 12f)
            throw new MapFileException($"{w}: the nav link's ends are {Vector3.Distance(a, c):0.#} m apart (at most 12)");
        ent.NavGate = map.Logic!.NavGates.Count;
        map.Logic.NavGates.Add((a, c));
    }

    // ---- Small readers ----------------------------------------------------------------------------------------

    private static string Ascii(string s, string w, int max)
    {
        if (s.Length > max)
            throw new MapFileException($"{w}: at most {max} characters");
        foreach (var ch in s)
            if (ch is < ' ' or > '~')
                throw new MapFileException($"{w}: plain ASCII only (the game's font has no '{ch}')");
        return s;
    }

    private static uint HashText(string s)
    {
        var h = 2166136261u;
        foreach (var ch in s)
        {
            h ^= ch;
            h *= 16777619u;
        }
        return h == 0 ? 1 : h;
    }

    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
            d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++)
            d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    private static Vector3 Point(Map map, JsonElement e, string w)
    {
        if (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 2)
            return new Vector3(Num(e[0], w), map.HasTerrain ? map.GroundHeight(Num(e[0], w), Num(e[1], w)) : 0, Num(e[1], w));
        var v = Nums(e, 3, w);
        return new Vector3(v[0], v[1], v[2]);
    }

    private static void Only(JsonElement e, string w, string[] keys)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{w}: expected an object");
        foreach (var p in e.EnumerateObject())
            if (!keys.Contains(p.Name))
                throw new MapFileException($"{w}: unknown key \"{p.Name}\" (it takes: {string.Join(", ", keys)})");
    }

    private static JsonElement? Get(JsonElement e, string name)
    {
        foreach (var p in e.EnumerateObject())
            if (p.Name == name)
                return p.Value;
        return null;
    }

    private static JsonElement Need(JsonElement e, string name, string w) =>
        Get(e, name) ?? throw new MapFileException($"{w}: missing \"{name}\"");

    private static float Num(JsonElement e, string w) =>
        e.ValueKind == JsonValueKind.Number ? e.GetSingle() : throw new MapFileException($"{w}: expected a number, got {e}");

    private static int Whole(JsonElement e, string w, int min, int max) =>
        e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var i) && i >= min && i <= max
            ? i : throw new MapFileException($"{w}: expected a whole number from {min} to {max}, got {e}");

    private static string Text(JsonElement e, string w) =>
        e.ValueKind == JsonValueKind.String && e.GetString() is { Length: > 0 } s ? s : throw new MapFileException($"{w}: expected text, got {e}");

    private static float[] Nums(JsonElement e, int n, string w)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != n)
            throw new MapFileException($"{w}: expected a list of {n} numbers");
        return e.EnumerateArray().Select(x => Num(x, w)).ToArray();
    }
}

/// <summary>The map logic's boxes in a map: doors (solid while shut) are boxes in <see cref="Solid"/>, drawn by the logic renderer.</summary>
public sealed partial class Map
{
    /// <summary>The map's "logic" section, or null (Maps/README.md, "Map logic").</summary>
    public MapLogic? Logic;

    private bool[] _logicBox = [];
    private readonly Dictionary<int, Box> _logicOriginal = new();

    /// <summary>Is <see cref="Solid"/>[i] a logic door (drawn by the logic renderer, not as a map box)?</summary>
    public bool IsLogicBox(int i) => (uint)i < (uint)_logicBox.Length && _logicBox[i];

    /// <summary>Reads the "logic" section: its entities, wires, door boxes and the doors' nav links (both ends become waypoints too).</summary>
    internal void ReadLogic(JsonElement e, string source)
    {
        Logic = new MapLogic();
        var logic = MapLogic.Read(e, this, source); // (doors' nav links go into Logic.NavGates while it reads)
        logic.NavGates.AddRange(Logic.NavGates);
        Logic = logic;
        foreach (var d in logic.Doors)
        {
            var ent = logic.Entities[d];
            ent.SolidIndex = Solid.Count;
            Solid.Add(ent.Box);
            Colors.Add(ent.Color);
            Array.Resize(ref _logicBox, Solid.Count);
            _logicBox[ent.SolidIndex] = true;
            _logicOriginal[ent.SolidIndex] = ent.Box;
        }
        foreach (var (a, b) in logic.NavGates)
        {
            NavLinks.Add((a, b)); // built into the graph as built (doors shut): switched on and off as the door opens and shuts
            Waypoints.Add(a);
            Waypoints.Add(b);
        }
    }

    /// <summary>Is logic door entity <paramref name="entity"/> open (its box parked out of the world)?</summary>
    public bool IsLogicDoorOpen(int entity) =>
        Logic is { } l && (uint)entity < (uint)l.Entities.Count && l.Entities[entity].SolidIndex is var k and >= 0 && Solid[k].Center.Y < -500f;

    /// <summary>Opens or shuts a logic door (both sides): its box is parked under the world or put back. False if it already was.</summary>
    public bool SetLogicDoor(int entity, bool open)
    {
        if (Logic is not { } l || (uint)entity >= (uint)l.Entities.Count || l.Entities[entity].SolidIndex is not (var k and >= 0) || IsLogicDoorOpen(entity) == open)
            return false;
        var b = _logicOriginal[k];
        Solid[k] = open ? new Box(new Vector3(b.Center.X, -1000f, b.Center.Z), new Vector3(0.001f)) : b;
        World.SolidChanged(this, k); // (the physics world switches a parked box off, and back on and refits when it returns)
        LogicVersion++;
        return true;
    }

    /// <summary>Goes up whenever a logic door changes (renderers and tests).</summary>
    [NotBaked]
    public int LogicVersion;

    /// <summary>Logic doors and controlled movers back as the map starts (a new match, a new session on this map). Part of <see cref="RestoreProps"/>.</summary>
    public void RestoreLogic()
    {
        foreach (var m in Movers)
            m.ResetControl();
        MoverPhysics.Invalidate();
        if (Logic is not { } l)
            return;
        foreach (var d in l.Doors)
            SetLogicDoor(d, l.Entities[d].StartOn);
    }

    /// <summary>Every logic door shut, as the nav graph is built (<see cref="AsBuiltSolid(Box[])"/>).</summary>
    private void LogicAsBuilt(Box[] boxes)
    {
        foreach (var (k, b) in _logicOriginal)
            if ((uint)k < (uint)boxes.Length)
                boxes[k] = b;
    }

    /// <summary>Are the logic doors as the nav graph is built (all shut)?</summary>
    private bool LogicIsAsBuilt()
    {
        foreach (var k in _logicOriginal.Keys)
            if (Solid[k].Center.Y < -500f)
                return false;
        return true;
    }

    /// <summary>
    /// Does a body walking a -> b go through a logic door's doorway? The nav graph's door links do (switched on only while
    /// the door is open, GameLogicNav.cs): the "Nav edge walks" audit, which walks the map as built (doors shut), leaves them out.
    /// </summary>
    public bool CrossesLogicDoor(Vector3 a, Vector3 b)
    {
        if (_logicOriginal.Count == 0)
            return false;
        const float r = World.PlayerRadius + 0.05f;
        foreach (var box in _logicOriginal.Values)
        {
            var grown = new Box(box.Center, box.Half + new Vector3(r, 0, r));
            var d = b - a;
            var len = d.Length();
            if (len < 1e-4f)
                continue;
            var lo = a with { Y = MathF.Min(a.Y, b.Y) + 0.3f };
            if (grown.Raycast(lo, new Vector3(d.X, 0, d.Z) / MathF.Max(1e-4f, new Vector2(d.X, d.Z).Length()), new Vector2(d.X, d.Z).Length()) != null)
                return true;
        }
        return false;
    }

    /// <summary>The box the fingerprint uses for a logic door: shut.</summary>
    internal bool LogicHashBox(int i, out Box b) => _logicOriginal.TryGetValue(i, out b);
}
