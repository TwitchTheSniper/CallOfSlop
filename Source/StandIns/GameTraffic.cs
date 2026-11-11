using Brutal.RakNetApi;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// STAND-IN (placeholder, Source/StandIns/README.md "Traffic"): the story world's ambient traffic, so the colony's world
/// looks lived in while the real vehicles, ships and places are still to come. Everything is a stand-in craft (a picture on
/// a scripted path): the host decides each leg and sends it as any other craft's script.
/// <list type="bullet">
/// <item><b>Stations</b>: the colony's gate and the quarry are real; the rest are placeholder pads (a plinth, a mast, a
/// hut) set out round the colony by bearing and distance, each standing for a place that isn't built yet.</item>
/// <item><b>Rounds</b>: a craft that goes round its stops for ever, standing a while at each (the ore run, the ration
/// round, the fence patrol): haulers along the ground, flyers in hops.</item>
/// <item><b>Visits</b>: a faction's ship from its home world lands at a station, stands while it trades, and jumps away.</item>
/// <item><b>Flyovers</b>: a freighter crosses the sky high over a player, bound somewhere else.</item>
/// </list>
/// Every one has a reason to travel (Story/Traffic/Traffic.json: "why"), which the Story window lists and the radio now
/// and then says. Nothing is traded yet (no money, no stock): the factions' goods are words until the places are real.
/// </summary>
public sealed partial class Game
{
    public const double TrafficPadSeconds = 900; // a pad's script lasts this long; the host renews it a minute before
    public const float TrafficClear = 45f; // m: a placeholder station keeps this far from anything built

    /// <summary>The host's switch (the Story window's People tab).</summary>
    public bool TrafficOn = true;

    /// <summary>Legs, visits and flyovers begun this session, and radio lines said (the window).</summary>
    public int TrafficTrips, TrafficRadioLines;

    private struct TrafficRun
    {
        public ushort Craft; // its stand-in's id (0: none yet)
        public int Stop; // a round: the stop it is at or bound for
        public double At; // when it next sets off or comes (the game's seconds)
    }

    private Vector3[] _tfSpot = [];
    private ushort[] _tfPad = [];
    private TrafficRun[] _tfRound = [], _tfVisit = [], _tfOver = [];
    private Map? _tfMap;
    private ushort _tfColony;
    private bool _tfSet;
    private double _tfRadioAt;
    private ulong _tfRng = 0x9E3779B97F4A7C15UL;
    private PacketWriter? _tfW;

    /// <summary>Both sides: no traffic (a new story, a new world).</summary>
    private void TrafficForget()
    {
        _tfSet = false;
        _tfMap = null;
    }

    private float TrafficRand()
    {
        _tfRng ^= _tfRng << 13;
        _tfRng ^= _tfRng >> 7;
        _tfRng ^= _tfRng << 17;
        return (_tfRng >> 40) / 16777216f;
    }

    /// <summary>Server, four times a second (from <see cref="StandInsTick"/>): the stations' pads, the rounds' next legs, the visits and the flyovers.</summary>
    private void TrafficTick(double now)
    {
        var on = StandInsOn && TrafficOn && Phase == MatchPhase.Playing && _opColony != 0;
        if (!on)
        {
            if (_tfSet)
                TrafficStop();
            return;
        }
        var data = TrafficData.Current;
        if (data.Problem.Length > 0)
            return;
        if (!_tfSet || _tfMap != World.Map || _tfColony != _opColony)
            TrafficSetUp(data, now);
        TrafficPads(data);
        for (var r = 0; r < _tfRound.Length; r++)
            TrafficRound(data, r, now);
        for (var v = 0; v < _tfVisit.Length; v++)
            TrafficVisit(data, v, now);
        for (var f = 0; f < _tfOver.Length; f++)
            TrafficFlyover(data, f, now);
    }

    /// <summary>Server: the switch went off (or the colony is gone): the traffic's craft and pads go at once.</summary>
    private void TrafficStop()
    {
        _tfSet = false;
        for (var i = 0; i < StandInCount; i++)
        {
            if (_standInWhy[i].For is not (StandInFor.Traffic or StandInFor.Pad) || ServerTick >= StandIns[i].EndsAt)
                continue;
            ref var c = ref StandIns[i];
            c.Start = ServerTick > 3 ? ServerTick - 3 : 0;
            (c.In, c.Wait, c.Out) = (1, 1, 1);
            StandInSend(i, resend: false);
        }
    }

    /// <summary>Server, once a world: where the stations are, and when everything first moves (spread out, so it doesn't all come at once).</summary>
    private void TrafficSetUp(TrafficData data, double now)
    {
        _tfSet = true;
        _tfMap = World.Map;
        _tfColony = _opColony;
        _tfRng = (ulong)ServerTick * 0x9E3779B97F4A7C15UL + 0x1234567UL | 1;
        var n = data.Stations.Length;
        if (_tfSpot.Length != n)
        {
            _tfSpot = new Vector3[n];
            _tfPad = new ushort[n];
        }
        Array.Clear(_tfPad);
        var middle = _opGate;
        var up = StandInPath.Up(middle);
        var east = StandInPath.Along(Vector3.Cross(Vector3.UnitY, up), middle);
        var north = Vector3.Cross(up, east);
        for (var i = 0; i < n; i++)
        {
            ref readonly var st = ref data.Stations[i];
            if (st.Real == TrafficReal.Colony)
            {
                _tfSpot[i] = QuarryColonyStop();
                continue;
            }
            if (st.Real == TrafficReal.Quarry && _qrWorks != 0 && Constructs.Get(_qrWorks) != null)
            {
                _tfSpot[i] = _qrStop;
                continue;
            }
            // A placeholder: out by its bearing and distance, turned a little at a time off anything built there.
            var spot = middle;
            for (var turn = 0; turn < 8; turn++)
            {
                var a = (st.Bearing + turn * 23f) * MathF.PI / 180f;
                spot = StandInPath.Ground(middle + (north * MathF.Cos(a) + east * MathF.Sin(a)) * st.Distance + up * 60f);
                if (TrafficSpotClear(spot))
                    break;
            }
            _tfSpot[i] = spot;
        }
        if (_tfRound.Length != data.Rounds.Length)
            _tfRound = new TrafficRun[data.Rounds.Length];
        if (_tfVisit.Length != data.Visits.Length)
            _tfVisit = new TrafficRun[data.Visits.Length];
        if (_tfOver.Length != data.Flyovers.Length)
            _tfOver = new TrafficRun[data.Flyovers.Length];
        for (var r = 0; r < _tfRound.Length; r++)
            _tfRound[r] = new TrafficRun { Stop = (int)(TrafficRand() * data.Rounds[r].Stops.Length), At = now + 4 + r * 9 + TrafficRand() * 6 };
        for (var v = 0; v < _tfVisit.Length; v++)
            _tfVisit[v] = new TrafficRun { At = now + 30 + data.Visits[v].Every * (0.1 + 0.9 * (v + TrafficRand()) / Math.Max(1, _tfVisit.Length)) };
        for (var f = 0; f < _tfOver.Length; f++)
            _tfOver[f] = new TrafficRun { At = now + 45 + data.Flyovers[f].Every * (f + TrafficRand()) / Math.Max(1, _tfOver.Length) };
        _tfRadioAt = now + data.RadioEvery * 0.5;
        Log($"[traffic] {n} stations round the colony, {_tfRound.Length} rounds, {_tfVisit.Length} visits, {_tfOver.Length} flyovers");
    }

    private bool TrafficSpotClear(Vector3 spot)
    {
        foreach (var c in Constructs.Bodies)
            if ((c.Anchored || c.Frozen) && Vector3.DistanceSquared(c.WorldCom, spot) < TrafficClear * TrafficClear)
                return false;
        return true;
    }

    /// <summary>Server: every placeholder station's pad stands, its script renewed before it runs out.</summary>
    private void TrafficPads(TrafficData data)
    {
        var renewAt = StandInTicks(60);
        for (var i = 0; i < _tfPad.Length; i++)
        {
            if (TrafficStationIsReal(data, i))
                continue;
            var slot = StandInSlotOf(_tfPad[i]);
            if (slot >= 0 && StandIns[slot].LeavesAt > ServerTick + renewAt)
                continue;
            if (slot >= 0)
            {
                StandIns[slot].Start = ServerTick;
                StandInSend(slot, resend: false);
                continue;
            }
            var toMiddle = StandInPath.Along(_opGate - _tfSpot[i], _tfSpot[i]);
            slot = StandInCall(StandInKind.Pad, _tfSpot[i], toMiddle, 1.0 / World.TickRate, TrafficPadSeconds, StandInFor.Pad, i, 0, jump: false);
            if (slot < 0)
                continue;
            StandIns[slot].Tint = data.Stations[i].Tint;
            StandInSend(slot, resend: false);
            _tfPad[i] = StandIns[slot].Id;
        }
    }

    private bool TrafficStationIsReal(TrafficData data, int i) =>
        data.Stations[i].Real == TrafficReal.Colony || (data.Stations[i].Real == TrafficReal.Quarry && _qrWorks != 0 && Constructs.Get(_qrWorks) != null);

    /// <summary>Server: a round's craft, once it has stood its time at a stop, sets off for the next.</summary>
    private void TrafficRound(TrafficData data, int r, double now)
    {
        ref var run = ref _tfRound[r];
        ref readonly var def = ref data.Rounds[r];
        var slot = StandInSlotOf(run.Craft);
        if (slot >= 0 && ServerTick < StandIns[slot].LeavesAt)
            return; // (on its way, or standing; it stands on a few seconds past its time, so a late look still finds it)
        if (slot < 0 && now < run.At)
            return;
        var from = slot >= 0 ? StandIns[slot].Land : _tfSpot[def.Stops[run.Stop]];
        var fromStop = run.Stop;
        run.Stop = (run.Stop + 1) % def.Stops.Length;
        var to = _tfSpot[def.Stops[run.Stop]];
        var dist = Vector3.Distance(from, to);
        if (dist < 5)
            return; // (two stops in one place: on to the next, next time)
        var seconds = Math.Clamp(dist / def.Speed, 4, 900);
        slot = TrafficLeg(slot, def.Kind, from, to, seconds, def.Stand, r, def.Tint, over: false);
        if (slot < 0)
        {
            run.Stop = fromStop;
            run.At = now + 10; // (no room among the craft just now)
            return;
        }
        run.Craft = StandIns[slot].Id;
        TrafficTrips++;
        TrafficSay(data, def.Radio, def.Name, data.Stations[def.Stops[fromStop]].Name, data.Stations[def.Stops[run.Stop]].Name, now);
    }

    /// <summary>
    /// Server: a leg from one spot to another, in the craft's own slot when it has one (the same id, so every PC sees one
    /// craft going on) or a new one. Returns the slot, or -1 with no room.
    /// </summary>
    private int TrafficLeg(int slot, StandInKind kind, Vector3 from, Vector3 to, double seconds, double stand, int who, Vector3 tint, bool over)
    {
        var way = StandInPath.Along(to - from, to);
        if (slot < 0)
        {
            slot = StandInCall(kind, to, way, seconds, stand, StandInFor.Traffic, who, 0, jump: false);
            if (slot < 0)
                return -1;
        }
        ref var c = ref StandIns[slot];
        c.Flags = (byte)(StandInCraft.FlagRoute | (over ? StandInCraft.FlagOver | StandInCraft.FlagJump : 0)); // (overhead: it warps in and jumps away)
        c.Boomed = 0;
        c.Start = ServerTick;
        c.In = (ushort)Math.Min(ushort.MaxValue, StandInTicks(seconds));
        c.Wait = (ushort)Math.Min(ushort.MaxValue, over ? 1 : StandInTicks(stand));
        c.Out = (ushort)StandInTicks(over ? 8 : 4);
        c.From = from;
        c.Land = to;
        c.Way = way;
        c.Tint = tint;
        c.Track = null;
        StandInSend(slot, resend: false);
        return slot;
    }

    /// <summary>Server: a faction's ship comes down at its station when its time is up, stands while it trades and jumps away.</summary>
    private void TrafficVisit(TrafficData data, int v, double now)
    {
        ref var run = ref _tfVisit[v];
        if (now < run.At || StandInSlotOf(run.Craft) >= 0)
            return;
        ref readonly var def = ref data.Visits[v];
        var spot = _tfSpot[def.Station];
        var a = TrafficRand() * MathF.Tau;
        var way = StandInPath.Along(new Vector3(MathF.Sin(a), 0, MathF.Cos(a)), spot);
        var (ins, outs) = StandInCraft.Times(def.Kind);
        var slot = StandInCall(def.Kind, spot, way, ins, def.Stand, StandInFor.Traffic, 1000 + v, 0, jump: true);
        run.At = now + ins + def.Stand + outs + def.Every * (0.7 + 0.6 * TrafficRand());
        if (slot < 0)
            return;
        StandIns[slot].Tint = def.Tint;
        StandInSend(slot, resend: false);
        run.Craft = StandIns[slot].Id;
        TrafficTrips++;
        TrafficSay(data, def.Radio, def.Name, def.From, data.Stations[def.Station].Name, now);
    }

    /// <summary>Server: a ship bound somewhere else crosses the sky over a player (or the colony), high up.</summary>
    private void TrafficFlyover(TrafficData data, int f, double now)
    {
        ref var run = ref _tfOver[f];
        if (now < run.At || StandInSlotOf(run.Craft) >= 0)
            return;
        ref readonly var def = ref data.Flyovers[f];
        run.At = now + def.Every * (0.7 + 0.6 * TrafficRand());
        var under = _opGate;
        var seen = 0;
        foreach (var p in Players.Values)
            if (p.Bot == null && p.Alive && TrafficRand() * ++seen < 1)
                under = p.Position;
        var up = StandInPath.Up(under);
        var a = TrafficRand() * MathF.Tau;
        var dir = StandInPath.Along(new Vector3(MathF.Sin(a), 0, MathF.Cos(a)), under);
        var side = Vector3.Cross(up, dir) * ((TrafficRand() - 0.5f) * 2f * def.Aside);
        var half = def.Length * 0.5f;
        var height = def.Height * (0.8f + 0.4f * TrafficRand());
        var p0 = under + side - dir * half;
        var p1 = under + side + dir * half;
        var from = StandInPath.Ground(p0 + StandInPath.Up(p0) * 60f) + StandInPath.Up(p0) * height;
        var to = StandInPath.Ground(p1 + StandInPath.Up(p1) * 60f) + StandInPath.Up(p1) * height;
        var slot = TrafficLeg(-1, def.Kind, from, to, def.Length / def.Speed, 0, 2000 + f, def.Tint, over: true);
        if (slot < 0)
            return;
        run.Craft = StandIns[slot].Id;
        TrafficTrips++;
        TrafficSay(data, def.Radio, def.Name, "", "", now);
    }

    /// <summary>Server: now and then the colony's traffic channel says what is moving and why (a [RADIO] line for every player).</summary>
    private void TrafficSay(TrafficData data, string[] lines, string name, string from, string to, double now)
    {
        if (now < _tfRadioAt || lines.Length == 0 || data.RadioEvery <= 0)
            return;
        _tfRadioAt = now + data.RadioEvery * (0.75 + 0.5 * TrafficRand());
        TrafficSayNow(lines[(int)(TrafficRand() * lines.Length) % lines.Length], from, to, now);
    }

    private void TrafficSayNow(string text, string from, string to, double now)
    {
        var line = ChatRules.Clean("Traffic: " + text.Replace("{from}", from).Replace("{to}", to));
        if (line.Length == 0)
            return;
        TrafficRadioLines++;
        AddChat(ChatRules.SystemId, "", line, now, ChatFlags.System | ChatFlags.Radio); // (a [RADIO] line, as the story's news: GameStoryTalk.cs)
        if (Net.IsOffline)
            return;
        var w = _tfW ??= new PacketWriter(256);
        w.Begin(Msg.ChatLine).U8(ChatRules.SystemId).Str(line).U8((byte)(ChatFlags.System | ChatFlags.Radio)).U8(NoTeam);
        Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Host, the Story window: what is moving now and why, a line each.</summary>
    public void TrafficDescribe(List<string> into)
    {
        into.Clear();
        var data = TrafficData.Current;
        if (data.Problem.Length > 0)
        {
            into.Add(data.Problem);
            return;
        }
        if (!_tfSet)
        {
            into.Add(_opColony == 0 ? "No colony in this world: the traffic runs round the story's colony." : "Not running.");
            return;
        }
        for (var r = 0; r < _tfRound.Length; r++)
        {
            ref readonly var def = ref data.Rounds[r];
            var slot = StandInSlotOf(_tfRound[r].Craft);
            var at = data.Stations[def.Stops[_tfRound[r].Stop]].Name;
            var state = slot < 0 ? "not out yet" : StandIns[slot].Landed(ServerTick) ? $"standing at {at}" : $"on its way to {at}";
            into.Add($"{def.Name} ({def.Faction}, {def.Kind}): {state}. {def.Why}");
        }
        for (var v = 0; v < _tfVisit.Length; v++)
        {
            ref readonly var def = ref data.Visits[v];
            var here = StandInSlotOf(_tfVisit[v].Craft) >= 0;
            into.Add($"{def.Name} ({def.Faction}, {def.Kind} from {def.From}): {(here ? "here now" : "due later")} at {data.Stations[def.Station].Name}. {def.Why}");
        }
        for (var f = 0; f < _tfOver.Length; f++)
        {
            ref readonly var def = ref data.Flyovers[f];
            into.Add($"{def.Name} ({def.Faction}, overhead): {(StandInSlotOf(_tfOver[f].Craft) >= 0 ? "passing over now" : "due later")}. {def.Why}");
        }
    }

    /// <summary>The check (TrafficCheck.cs): the stations' spots, and everything due at once.</summary>
    internal int TrafficStationCount => _tfSet ? _tfSpot.Length : 0;
    internal Vector3 TrafficStationSpot(int i) => _tfSpot[i];

    internal void TrafficHurry(double now)
    {
        for (var v = 0; v < _tfVisit.Length; v++)
            _tfVisit[v].At = Math.Min(_tfVisit[v].At, now + 5 + v * 20);
        for (var f = 0; f < _tfOver.Length; f++)
            _tfOver[f].At = Math.Min(_tfOver[f].At, now + 10 + f * 15);
        _tfRadioAt = Math.Min(_tfRadioAt, now + 20);
    }

    internal void TrafficTickForCheck(double now) => TrafficTick(now);
}

/// <summary>A station that is a real place of the story's colony (the others are placeholder pads).</summary>
public enum TrafficReal : byte { None, Colony, Quarry }

public struct TrafficStation
{
    public string Name, About;
    public float Bearing, Distance; // degrees from north round the colony's gate, and m out
    public TrafficReal Real;
    public Vector3 Tint;
}

public struct TrafficRound
{
    public string Name, Faction, Why;
    public StandInKind Kind;
    public int[] Stops;
    public float Speed; // m/s
    public double Stand; // s at each stop
    public string[] Radio;
    public Vector3 Tint;
}

public struct TrafficVisitDef
{
    public string Name, Faction, Why, From;
    public StandInKind Kind;
    public int Station;
    public double Every, Stand;
    public string[] Radio;
    public Vector3 Tint;
}

public struct TrafficFlyoverDef
{
    public string Name, Faction, Why;
    public StandInKind Kind;
    public double Every;
    public float Height, Length, Speed, Aside;
    public string[] Radio;
    public Vector3 Tint;
}

/// <summary>
/// Story/Traffic/Traffic.json (format call-of-slop-traffic/1): the stations, rounds, visits and flyovers of the story
/// world's ambient traffic. A bad file is refused with a reason (Problem) and there is no traffic.
/// </summary>
public sealed class TrafficData
{
    public const string Format = "call-of-slop-traffic/1";
    public const int MaxStations = 12, MaxRounds = 8, MaxVisits = 8, MaxFlyovers = 6, MaxStops = 6;

    public string Problem = "";
    public double RadioEvery = 480;
    public TrafficStation[] Stations = [];
    public TrafficRound[] Rounds = [];
    public TrafficVisitDef[] Visits = [];
    public TrafficFlyoverDef[] Flyovers = [];

    private static TrafficData? _current;
    public static TrafficData Current => _current ??= Load(Path.Combine(AppFolders.Data, "Story", "Traffic", "Traffic.json"));

    public static TrafficData Load(string path)
    {
        try
        {
            var d = Parse(File.ReadAllText(path));
            Console.WriteLine($"[traffic] {d.Stations.Length} stations, {d.Rounds.Length} rounds, {d.Visits.Length} visits, {d.Flyovers.Length} flyovers");
            return d;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            Console.WriteLine($"[traffic] refused: {ex.Message}");
            return new TrafficData { Problem = $"Traffic.json: {ex.Message}" };
        }
    }

    public static TrafficData Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
            throw new FormatException($"format must be \"{Format}\"");
        var d = new TrafficData();
        d.RadioEvery = Num(root, "radioEvery", "file", 480, 0, 36000);

        var liveries = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("liveries", out var lv) && lv.ValueKind == JsonValueKind.Object)
            foreach (var p in lv.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Array || p.Value.GetArrayLength() != 3)
                    throw new FormatException($"liveries.{p.Name}: three numbers from 0 to 1 (red, green, blue)");
                var c = new Vector3(p.Value[0].GetSingle(), p.Value[1].GetSingle(), p.Value[2].GetSingle());
                if (!(c.X is >= 0 and <= 1 && c.Y is >= 0 and <= 1 && c.Z is >= 0 and <= 1))
                    throw new FormatException($"liveries.{p.Name}: three numbers from 0 to 1 (red, green, blue)");
                liveries[p.Name] = c;
            }
        Vector3 Tint(string faction) => liveries.TryGetValue(faction, out var c) ? c : Vector3.Zero;

        var stations = List(root, "stations", MaxStations);
        d.Stations = new TrafficStation[stations.Count];
        for (var i = 0; i < stations.Count; i++)
        {
            var e = stations[i];
            var where = $"stations[{i}]";
            var st = new TrafficStation
            {
                Name = Str(e, "name", where, need: true),
                About = Str(e, "about", where),
                Bearing = (float)Num(e, "bearing", where, 0, -360, 360),
                Distance = (float)Num(e, "distance", where, 200, 60, 2000),
                Tint = Tint(Str(e, "faction", where)),
            };
            var real = Str(e, "real", where);
            st.Real = real.Length == 0 ? TrafficReal.None
                : Enum.TryParse<TrafficReal>(real, true, out var k) && !int.TryParse(real, out _) && k != TrafficReal.None ? k
                : throw new FormatException($"{where}: no such real place \"{real}\" (colony, quarry)");
            for (var j = 0; j < i; j++)
                if (d.Stations[j].Name.Equals(st.Name, StringComparison.OrdinalIgnoreCase))
                    throw new FormatException($"{where}: two stations named \"{st.Name}\"");
            d.Stations[i] = st;
        }
        int Station(string name, string where)
        {
            for (var i = 0; i < d.Stations.Length; i++)
                if (d.Stations[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return i;
            throw new FormatException($"{where}: no station named \"{name}\"");
        }

        var rounds = List(root, "rounds", MaxRounds);
        d.Rounds = new TrafficRound[rounds.Count];
        for (var i = 0; i < rounds.Count; i++)
        {
            var e = rounds[i];
            var where = $"rounds[{i}]";
            var r = new TrafficRound
            {
                Name = Str(e, "name", where, need: true),
                Faction = Str(e, "faction", where),
                Why = Str(e, "why", where, need: true),
                Kind = Craft(Str(e, "craft", where, need: true), where, StandInKind.Hauler, StandInKind.Courier, StandInKind.Shuttle),
                Speed = (float)Num(e, "speed", where, 11, 2, 80),
                Stand = Num(e, "stand", where, 25, 3, 600),
                Radio = Strs(e, "radio", where),
            };
            r.Tint = Tint(r.Faction);
            var stops = Strs(e, "stops", where);
            if (stops.Length is < 2 or > MaxStops)
                throw new FormatException($"{where}: 2 to {MaxStops} stops");
            r.Stops = new int[stops.Length];
            for (var k = 0; k < stops.Length; k++)
                r.Stops[k] = Station(stops[k], where);
            for (var k = 0; k < stops.Length; k++)
                if (r.Stops[k] == r.Stops[(k + 1) % stops.Length])
                    throw new FormatException($"{where}: \"{stops[k]}\" twice running");
            d.Rounds[i] = r;
        }

        var visits = List(root, "visits", MaxVisits);
        d.Visits = new TrafficVisitDef[visits.Count];
        for (var i = 0; i < visits.Count; i++)
        {
            var e = visits[i];
            var where = $"visits[{i}]";
            var v = new TrafficVisitDef
            {
                Name = Str(e, "name", where, need: true),
                Faction = Str(e, "faction", where),
                Why = Str(e, "why", where, need: true),
                From = Str(e, "from", where, need: true),
                Kind = Craft(Str(e, "craft", where, need: true), where, StandInKind.Shuttle, StandInKind.Courier, StandInKind.Freighter),
                Station = Station(Str(e, "at", where, need: true), where),
                Every = Num(e, "every", where, 300, 30, 36000),
                Stand = Num(e, "stand", where, 40, 5, 600),
                Radio = Strs(e, "radio", where),
            };
            v.Tint = Tint(v.Faction);
            d.Visits[i] = v;
        }

        var overs = List(root, "flyovers", MaxFlyovers);
        d.Flyovers = new TrafficFlyoverDef[overs.Count];
        for (var i = 0; i < overs.Count; i++)
        {
            var e = overs[i];
            var where = $"flyovers[{i}]";
            var o = new TrafficFlyoverDef
            {
                Name = Str(e, "name", where, need: true),
                Faction = Str(e, "faction", where),
                Why = Str(e, "why", where, need: true),
                Kind = Craft(Str(e, "craft", where, need: true), where, StandInKind.Freighter, StandInKind.Shuttle, StandInKind.Courier),
                Every = Num(e, "every", where, 240, 20, 36000),
                Height = (float)Num(e, "height", where, 380, 120, 1500),
                Length = (float)Num(e, "length", where, 2600, 600, 5000),
                Speed = (float)Num(e, "speed", where, 55, 10, 300),
                Aside = (float)Num(e, "aside", where, 250, 0, 1500),
                Radio = Strs(e, "radio", where),
            };
            if (o.Length / o.Speed > 1000)
                throw new FormatException($"{where}: length over speed is at most 1000 s");
            o.Tint = Tint(o.Faction);
            d.Flyovers[i] = o;
        }
        return d;
    }

    private static StandInKind Craft(string name, string where, params StandInKind[] allowed)
    {
        foreach (var k in allowed)
            if (k.ToString().Equals(name, StringComparison.OrdinalIgnoreCase))
                return k;
        throw new FormatException($"{where}: craft \"{name}\" isn't one of {string.Join(", ", allowed).ToLowerInvariant()}");
    }

    private static List<JsonElement> List(JsonElement root, string key, int max)
    {
        var list = new List<JsonElement>();
        if (!root.TryGetProperty(key, out var a))
            return list;
        if (a.ValueKind != JsonValueKind.Array)
            throw new FormatException($"\"{key}\" must be a list");
        foreach (var e in a.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object)
                throw new FormatException($"{key}[{list.Count}] must be an object");
            list.Add(e);
        }
        return list.Count <= max ? list : throw new FormatException($"\"{key}\": at most {max}");
    }

    private static double Num(JsonElement e, string key, string where, double fallback, double min, double max)
    {
        if (!e.TryGetProperty(key, out var v))
            return fallback;
        if (v.ValueKind != JsonValueKind.Number || v.GetDouble() is var n && !(n >= min && n <= max))
            throw new FormatException($"{where}.{key}: a number from {min:0.#} to {max:0.#}");
        return v.GetDouble();
    }

    private static string Str(JsonElement e, string key, string where, bool need = false)
    {
        if (e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            return Ascii(v.GetString() ?? "", $"{where}.{key}") is { Length: > 0 } s || !need ? Ascii(v.GetString() ?? "", $"{where}.{key}") : throw new FormatException($"{where}.{key} is empty");
        return need ? throw new FormatException($"{where} has no \"{key}\"") : "";
    }

    private static string[] Strs(JsonElement e, string key, string where)
    {
        if (!e.TryGetProperty(key, out var a))
            return [];
        if (a.ValueKind != JsonValueKind.Array)
            throw new FormatException($"{where}.{key} must be a list");
        var list = new List<string>();
        foreach (var v in a.EnumerateArray())
            list.Add(v.ValueKind == JsonValueKind.String ? Ascii(v.GetString() ?? "", $"{where}.{key}") : throw new FormatException($"{where}.{key}: text only"));
        return list.ToArray();
    }

    private static string Ascii(string s, string where)
    {
        foreach (var c in s)
            if (c is < ' ' or > '~')
                throw new FormatException($"{where}: player text is ASCII only");
        if (s.Length > 200)
            throw new FormatException($"{where}: at most 200 characters");
        return s;
    }
}
