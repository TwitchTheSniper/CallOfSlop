using System.Numerics;

namespace CallOfSlop;

/// <summary>What a power's controller gives a group to do (faction-controllers.md 3.3, phase 3).</summary>
public enum MissionKind : byte
{
    None,
    Patrol,
    Defend, // a call answered: escort the caller's convoy or hold where it called from
    Convoy, // cargo from one of its places to another and back, with escorts
    Raid, // an enemy's convoy struck, its cargo looted
}

public enum PowerMissionStage : byte
{
    Gather, // its ships being fielded at home (from the hangar, else bought and built)
    Run,
    Return, // home, where each ship docks out of sight
}

/// <summary>A power's ship: being built, waiting to be set down, in its hangar (docked: a record), or out as a crewed body.</summary>
public enum PowerShipState : byte
{
    None,
    Building,
    Spawning,
    Hangar,
    Out,
    Far, // phase 4: a record in a far group (Crew/CrewTerritory.cs), no body
}

public struct PowerShip
{
    public PowerShipState State;
    public ShipRole Role;
    public short Bp; // into PowerControllers.Blueprints
    public ushort Body; // out: its construct (0 after a load until adopted)
    public float Hull; // its hull's share when it docked
    public uint Uid; // out: its captain's (else a member's) crew uid, kept fresh: after a load it takes up the body whose crew holds that uid
    public long ReadyAt; // building: the tick it is done
    public sbyte Mission; // -1: free
    public short Far; // phase 4: its far group + 1 (0: none; CrewTerritory.cs)
}

public struct PowerMission
{
    public MissionKind Kind;
    public PowerMissionStage Stage;
    public short Group; // its group's slot (-1: not formed yet)
    public ushort GroupNumber;
    public byte Other; // a raid's victim, a defence's enemy
    public byte Caller; // a defence: who called
    public byte CallerParty, CallerPerson; // phase 5: a defence for a player faction (its id) and the caller + 1, followed (not saved: a loaded one holds where it was called)
    public short OtherGroup; // a raid's target group, a defence's caller's group (slot), and its number
    public ushort OtherNumber;
    public Vector3 At;
    public byte From, To; // a convoy's places
    public byte NeedCargo, NeedArmed;
    public ShipRole Armed;
    public bool Paid, Called, Looted, Commanded;
    public long Started, StageAt, QuietSince, CalledAt;
    public long ReadTick; // the crews' tick (CrewWorld.Tick, its group's reports' stamps) it last read its group's reports at; -1: not yet
    public int CargoSeen; // a raid: the target's cargo ships when last looked
}

/// <summary>A call for help (faction-controllers.md 3.3 "Call allies"; answered by allies, and by neutral powers with a common enemy, section 5.2).</summary>
public struct PowerCall
{
    public byte Caller, Enemy;
    public byte Party, Person; // phase 5: a player faction's call (Caller 0): the faction, and the caller + 1 (0: none)
    public Vector3 At;
    public short Group;
    public ushort Number;
    public long Tick;
}

/// <summary>What the controllers need of the world they run in: the game's, or a tool's.</summary>
public interface IPowerHost
{
    /// <summary>Where people are (players, not bots).</summary>
    int People(Span<Vector3> into);

    /// <summary>The open world's key (a map or planet's name): places picked on one world are not used on another.</summary>
    string WorldKey { get; }

    /// <summary>The ground under or near <paramref name="at"/>; false: none there (off the map's edge).</summary>
    bool Ground(Vector3 at, out Vector3 ground);

    /// <summary>Set down blueprint <paramref name="bp"/> at <paramref name="at"/>, crewed by <paramref name="power"/>; the host calls <see cref="PowerControllers.Spawned"/>
    /// with <paramref name="ship"/> when it is down (or failed). False: not now (try again later).</summary>
    bool Spawn(byte power, int ship, ConstructBuild.Blueprint bp, Vector3 at, float yaw);

    /// <summary>A docked ship's body goes (its crew with it): out of everyone's sight.</summary>
    void Dock(Construct c);

    /// <summary>A crew of <paramref name="power"/> sees <paramref name="at"/> (its controller's fog of war: answer 7).</summary>
    bool Sees(byte power, Vector3 at);

    /// <summary>Phase 4: the space clock's seconds (far groups move between planets by it); -1: none (the controllers' own ticks).</summary>
    double SpaceSeconds => -1;

    /// <summary>Phase 4: metres between two planets now; -1: not known (the territory's transit time).</summary>
    double PlanetGap(string a, string b) => -1;

    /// <summary>Phase 4: a key site's place on the open world (<paramref name="at"/>: latitude and longitude on a planetoid, else x and z); false: that planet isn't open here.</summary>
    bool SiteAt(string planet, Vector2 at, out Vector3 world)
    {
        world = default;
        return false;
    }

    /// <summary>
    /// Ships use goods from stations (Karl, 2026-11-22): <paramref name="share"/> of <paramref name="goods"/> (a blueprint's materials) taken from
    /// the stock of the stations <paramref name="power"/> holds, all or nothing. A material none of its stations trades isn't taken (its stock pays
    /// for it as before). False: a station's material it trades is short (<paramref name="shortOf"/>), and nothing was taken.
    /// </summary>
    bool TakeGoods(byte power, (byte Material, float Units)[] goods, float share, out byte shortOf)
    {
        shortOf = 0;
        return true;
    }

    /// <summary>Phase 5: where person <paramref name="player"/> is (alive, here); false: not.</summary>
    bool PersonAt(byte player, out Vector3 at)
    {
        at = default;
        return false;
    }
}

/// <summary>
/// The faction controllers (docs/sandbox/design/faction-controllers.md section 3 and phase 3, "the controller in open worlds"): one mind per power,
/// the same code for all, each thinking every few seconds in turn. A controller holds its power's stock (start, income for each place it still
/// holds, trade, tribute, loot; it spends only what it has), its places on the open world, its ships (a hangar of records, ships being built from
/// their blueprints' real bills, and crewed bodies out), and its missions: Convoy (cargo from one place to another with escorts, led by an admiral),
/// Raid (an enemy's convoy it knows of), Defend (a call answered: allies, and neutral powers with a common enemy), Patrol. Groups are the fleet's
/// (Crew/CrewFleet.cs); it reads their reports. A power left with no ship, no place and no stock for a ship is gone for good (answer 5). Server only;
/// fixed arrays: nothing allocated in a tick but a news line or a log line.
/// </summary>
public sealed partial class PowerControllers
{
    public const int MaxShips = 64, MaxMissions = 8, MaxPlaces = 8, MaxCalls = 16, N = FactionTable.MaxStory;
    private static int T => World.TickRate;

    public ControllerRules Rules = ControllerRules.Current;
    public long Tick = 1;

    public sealed class Power
    {
        public byte Id;
        public PowerPlan Plan = new();
        public double Stock, Earned, Spent, Looted, Paid, Received, StartStock;
        public bool Fallen, Started;
        public string World = "";
        public readonly string[] PlaceName = new string[MaxPlaces];
        public readonly Vector3[] Place = new Vector3[MaxPlaces];
        public readonly bool[] Razed = new bool[MaxPlaces];
        public readonly long[] EnemySince = new long[MaxPlaces];
        public int PlaceCount;
        public readonly PowerShip[] Ships = new PowerShip[MaxShips];
        public readonly PowerMission[] Missions = new PowerMission[MaxMissions];
        public long NextConvoy, Thought, UnpaidSince, TributeTicks;
        public int CallRead, Bought, Docked, Lost;
        public int PlaceK = -1; // the generated place it lays next (-1: not laying)
        public bool Relink; // after a load: its missions' groups are looked up again once the Fleet section is read
        public byte Heir; // merged into (saved)
        public bool HadSites; // phase 4: it held a key site once (a power that never had a place or a site can't fall)
        public long NextTake; // phase 4: when it next weighs a Take

        public int Held()
        {
            var n = 0;
            for (var i = 0; i < PlaceCount; i++)
                if (!Razed[i])
                    n++;
            return n;
        }

        public int ShipCount()
        {
            var n = 0;
            foreach (var s in Ships)
                if (s.State != PowerShipState.None)
                    n++;
            return n;
        }

        /// <summary>The books: what it started with and took in, less what it spent and paid. A check compares it with <see cref="Stock"/>.</summary>
        public double Books => StartStock + Earned + Looted + Received - Spent - Paid;
    }

    public readonly Power[] Powers = new Power[N + 1];
    public readonly PowerCall[] Calls = new PowerCall[MaxCalls];
    public int CallTotal;

    /// <summary>Every blueprint any plan names (an index is a ship's <see cref="PowerShip.Bp"/>), with its bill. Made once at <see cref="Reset"/>.</summary>
    public readonly List<ConstructBuild.Blueprint> Blueprints = new();
    public readonly List<ControllerRules.Bill> Bills = new();
    private readonly short[][][] _fleet = new short[N + 1][][]; // [power][role] blueprint indices

    public readonly List<string> News = new(16);
    /// <summary>Log lines (the host prints them): kept apart so a think that logs is not counted as allocating in a steady tick.</summary>
    public readonly List<string> Log = new(16);
    public double CpuMs, WorstMs;
    /// <summary>Where a think's time goes (ms in all): books, places, ships, places lost, missions, calls, new missions, strays.</summary>
    public readonly double[] PartMs = new double[8];
    private long _partAt;

    private void Part(int k)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        PartMs[k] += (now - _partAt) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _partAt = now;
    }
    public int Ticks, Thinks;
    public long Bytes;
    private int _next;
    private long _thinkAt, _dockedAt;
    private int _adopts;
    private readonly Vector3[] _people = new Vector3[64];
    private int _peopleN;
    private readonly ushort[] _ids = new ushort[CrewGroup.MaxShips];
    public bool On = true;
    /// <summary>A test's: places laid this far out (and half again), not the rules' (0: the rules').</summary>
    public float PlacesNear;
    private float SpawnFrom => PlacesNear > 0 ? MathF.Min(Rules.SpawnFromPeople, PlacesNear * 0.6f) : Rules.SpawnFromPeople;
    private float DockFrom => PlacesNear > 0 ? MathF.Min(Rules.DockFromPeople, PlacesNear * 0.6f) : Rules.DockFromPeople;

    public PowerControllers()
    {
        for (var i = 0; i <= N; i++)
            Powers[i] = new Power { Id = (byte)i };
    }

    private static int Count => CrewPowers.Count;

    /// <summary>A new world: every power from its plan (stock at its start, no places, ships or missions).</summary>
    public void Reset()
    {
        Blueprints.Clear();
        Bills.Clear();
        for (var a = 1; a <= N; a++)
        {
            var p = Powers[a] = new Power { Id = (byte)a };
            _fleet[a] = [[], [], [], []];
            if (a > Count)
                continue;
            p.Plan = Rules.PlanOf((byte)a);
            p.Stock = p.StartStock = p.Plan.Start;
            for (var r = 0; r < (int)ShipRole.Count; r++)
            {
                var list = new List<short>();
                foreach (var name in p.Plan.Of((ShipRole)r))
                {
                    var bp = ConstructBuild.FindBlueprint(name);
                    if (bp == null)
                        continue;
                    var k = Blueprints.IndexOf(bp);
                    if (k < 0)
                    {
                        k = Blueprints.Count;
                        Blueprints.Add(bp);
                        Bills.Add(Rules.BillOf(bp));
                    }
                    list.Add((short)k);
                }
                _fleet[a][r] = list.ToArray();
            }
            for (var i = 0; i < MaxMissions; i++)
                p.Missions[i].Group = -1;
        }
        Array.Clear(Calls);
        CallTotal = 0;
        News.Clear();
        TerritoryReset(); // (phase 4: sites to their holders in the data, no far group: CrewTerritory.cs)
    }

    public float Cheapest(byte a)
    {
        var best = float.MaxValue;
        foreach (var roles in _fleet[a])
            foreach (var k in roles)
                best = MathF.Min(best, Bills[k].Units);
        return best;
    }

    /// <summary>A power's might (relations' tribute and merging): its ships and a thousandth of its stock.</summary>
    public float Might(byte a) => a > 0 && a <= N && !Powers[a].Fallen ? Powers[a].ShipCount() + (float)(Powers[a].Stock / 1000) : 0;

    /// <summary>Bodies the controllers have out or on their way (the world's cap).</summary>
    public int Bodies()
    {
        var n = 0;
        for (var a = 1; a <= Count; a++)
            foreach (var s in Powers[a].Ships)
                if (s.State is PowerShipState.Out or PowerShipState.Spawning)
                    n++;
        return n;
    }

    // ---- The tick --------------------------------------------------------------------------------------------------------------------

    /// <summary>A server tick: one power thinks when its turn comes (each every <see cref="ControllerRules.ThinkSeconds"/>).</summary>
    public void Step(CrewWorld crew, ConstructWorld w, PowerRelations rel, IPowerHost host)
    {
        _host = host;
        Tick++;
        Ticks++;
        if (!On || Count == 0)
            return;
        FarStep(crew, w, rel, host); // (a slice of the far groups each tick: CrewTerritory.cs)
        if (Tick < _thinkAt)
            return;
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var b0 = GC.GetAllocatedBytesForCurrentThread();
        var e0 = News.Count + crew.Events.Count + Log.Count;
        _thinkAt = Tick + Math.Max(1, (int)(Rules.ThinkSeconds * T) / Count);
        if (_next >= Count)
            _next = 0;
        var a = (byte)++_next;
        _peopleN = host.People(_people);
        Think(crew, w, rel, host, a);
        Thinks++;
        var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (News.Count + crew.Events.Count + Log.Count == e0)
            Bytes += GC.GetAllocatedBytesForCurrentThread() - b0; // (a think that said nothing allocates nothing)
        CpuMs += ms;
        if (Thinks > 3 * Count)
            WorstMs = Math.Max(WorstMs, ms); // (past each power's first thinks: their JIT)
    }

    private void Say(string line) => News.Add(line);

    private static string Name(byte p) => CrewPowers.Of(p)?.Name ?? $"power {p}";

    /// <summary>One power's think: its books, its places, its ships, its missions, the calls it hears, new missions, and whether it is gone.</summary>
    public void Think(CrewWorld crew, ConstructWorld w, PowerRelations rel, IPowerHost host, byte a)
    {
        var p = Powers[a];
        var def = CrewPowers.Of(a);
        if (def is not { Controller: true } || p.Fallen || rel.Gone[a] || !p.Plan.Fields)
            return;
        var dt = p.Thought == 0 ? 0 : Tick - p.Thought;
        p.Thought = Tick;
        var minutes = dt / (60.0 * T);
        _partAt = System.Diagnostics.Stopwatch.GetTimestamp();
        _adopts = 0;
        if (p.Relink)
            Relink(crew, p, a);
        Books(rel, p, a, minutes);
        Part(0);
        FarThink(crew, w, rel, host, p, a, def, minutes); // (territory: its sites' garrisons and income, Take and Defend groups nobody sees: CrewTerritory.cs)
        if (_peopleN == 0)
            return; // (nobody on this world: phase 4's far layer moves what nobody sees)
        if (p.Started && !string.Equals(p.World, host.WorldKey, StringComparison.Ordinal))
            (p.Started, p.PlaceK) = (false, -1); // (another open world: its places are laid again)
        if (!p.Started)
        {
            Places(host, p, a);
            if (!p.Started)
                return; // (laying its places, one a think)
        }
        Part(1);
        Upkeep(crew, w, host, p, a);
        Part(2);
        Raze(crew, w, rel, p, a);
        Part(3);
        for (var i = 0; i < MaxMissions; i++)
            if (p.Missions[i].Kind != MissionKind.None)
                StepMission(crew, w, rel, host, p, a, i, def);
        Part(4);
        Hear(crew, w, rel, p, a, def);
        Part(5);
        Plan(crew, w, rel, host, p, a, def);
        Part(6);
        Strays(crew, w, p, a);
        Part(7);
        if (p.Plan.CanFall && p.ShipCount() == 0 && (p.PlaceCount > 0 || p.HadSites) && p.Held() == 0 && SitesOf(a) == 0 && p.Stock < Cheapest(a))
        {
            p.Fallen = true;
            rel.Fall(a, $"{def.Name} is no more: no ship, no place and nothing left to build with");
            crew.Events.Add($"powers: {def.Name} is gone for good");
        }
    }

    // ---- Books: income, trade, tribute -------------------------------------------------------------------------------------------

    private void Books(PowerRelations rel, Power p, byte a, double minutes)
    {
        if (minutes <= 0)
            return;
        var income = p.Plan.Income * p.Held() * minutes;
        p.Stock += income;
        p.Earned += income;
        for (var i = 0; i < PowerRelations.MaxPacts; i++)
        {
            var k = rel.PactK[i];
            if (k == PactKind.None || rel.PactA[i] != a && rel.PactB[i] != a)
                continue;
            if (k == PactKind.Trade)
            {
                var t = rel.Rules.TradePerMinute * minutes;
                p.Stock += t;
                p.Earned += t;
            }
            else if (k is PactKind.Tribute or PactKind.Vassalage && rel.PactA[i] == a)
            {
                var to = Powers[rel.PactB[i]];
                var due = Math.Max(1, p.Plan.Income * Math.Max(1, p.Held())) * rel.Rules.TributeShare * minutes;
                if (p.Stock >= due)
                {
                    p.Stock -= due;
                    p.Paid += due;
                    to.Stock += due;
                    to.Received += due;
                    p.UnpaidSince = 0;
                    p.TributeTicks += (long)(minutes * 60 * T);
                    if (p.TributeTicks >= 3600L * T)
                    {
                        p.TributeTicks -= 3600L * T;
                        rel.Post(RelEvent.PayTribute, rel.PactB[i], a, 255, default);
                    }
                }
                else if (p.UnpaidSince == 0)
                    p.UnpaidSince = Tick;
                else if (Tick - p.UnpaidSince > 5L * 60 * T)
                {
                    p.UnpaidSince = 0;
                    rel.EndPact(i, "lapses: unpaid", -20);
                }
            }
        }
    }

    // ---- Places -------------------------------------------------------------------------------------------------------------------

    /// <summary>Its places on this open world: its plan's for this world, else as many as it wants picked round the first person (phase 3's
    /// waypoints: a depot and a yard for Traders, a camp for Raiders; phase 4 puts outposts at key sites). A convoy's two face each other across the person.</summary>
    private void Places(IPowerHost host, Power p, byte a)
    {
        if (p.PlaceK < 0)
        {
            // Begin: its plan's places for this world at once (a probe each), else one generated place a think (up to 16 probes each).
            p.World = host.WorldKey;
            p.PlaceCount = 0;
            p.PlaceK = 0;
            foreach (var spec in p.Plan.PlaceList)
                if ((spec.World.Length == 0 || spec.World == "*" || spec.World.Equals(p.World, StringComparison.OrdinalIgnoreCase)) && p.PlaceCount < MaxPlaces)
                    if (host.Ground(spec.At, out var g))
                        AddPlace(p, spec.Name, g);
                    else
                        Log.Add($"{Name(a)}: its place {spec.Name} at {spec.At} has no ground under it: left out");
            if (p.PlaceCount > 0)
                p.PlaceK = p.Plan.Places; // (its plan names its places here: none generated)
        }
        var k = p.PlaceK;
        if (k < p.Plan.Places && k < MaxPlaces)
        {
            var me = _people[0];
            var seed = a * 2654435761u ^ StableHash(p.World);
            var bearing = (seed % 3600) / 3600f * MathF.Tau;
            var b = bearing + (k == 1 ? MathF.PI * 0.85f : k * 2.1f);
            var (lo, hi) = PlacesNear > 0 ? (PlacesNear, PlacesNear * 1.5f) : (Rules.PlaceNear, Rules.PlaceFar);
            var r = lo + (hi - lo) * ((seed >> (8 + k * 4)) % 16) / 15f;
            var up = GravityFrames.Curved ? GravityFrames.UpAt(me) : Vector3.UnitY;
            var east = Vector3.Normalize(Vector3.Cross(up, MathF.Abs(up.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX));
            var north = Vector3.Cross(east, up);
            // Ground under it, else another bearing (and nearer, each four tries): a small map's edge.
            for (var tries = 0; tries < 16; tries++)
            {
                var bb = b + tries * 0.52f;
                var rr = r * MathF.Pow(0.8f, tries / 4);
                if (host.Ground(me + (east * MathF.Cos(bb) + north * MathF.Sin(bb)) * rr, out var g))
                {
                    AddPlace(p, k == 0 ? "home" : $"place {k + 1}", g);
                    break;
                }
            }
            p.PlaceK = k + 1;
            if (p.PlaceK < p.Plan.Places && p.PlaceK < MaxPlaces)
                return;
        }
        p.Started = true;
        p.PlaceK = -1;
        if (p.PlaceCount > 0)
            Log.Add($"{Name(a)}: {p.PlaceCount} places on {p.World}: {string.Join(", ", Enumerable.Range(0, p.PlaceCount).Select(i => $"{p.PlaceName[i]} ({p.Place[i].X:0}, {p.Place[i].Z:0})"))}");
    }

    private static uint StableHash(string text)
    {
        var h = 2166136261u;
        foreach (var ch in text)
            h = (h ^ ch) * 16777619u;
        return h;
    }

    private static void AddPlace(Power p, string name, Vector3 at)
    {
        var i = p.PlaceCount++;
        (p.PlaceName[i], p.Place[i], p.Razed[i], p.EnemySince[i]) = (name, at, false, 0);
    }

    /// <summary>A place an enemy group holds with none of its own ships near, for <see cref="ControllerRules.RazeSeconds"/>, is lost (its income with it).</summary>
    private void Raze(CrewWorld crew, ConstructWorld w, PowerRelations rel, Power p, byte a)
    {
        var r2 = Rules.RazeRange * Rules.RazeRange;
        for (var i = 0; i < p.PlaceCount; i++)
        {
            var enemy = false;
            foreach (var g in crew.Fleet.Groups)
                if (g.Live && g.Power != a && rel.Fights(g.Power, a) && w.Get(g.Flagship) is { } f && Vector3.DistanceSquared(f.WorldCom, p.Place[i]) < r2)
                {
                    enemy = true;
                    break;
                }
            var own = false;
            foreach (var s in p.Ships)
                if (s.State == PowerShipState.Out && w.Get(s.Body) is { } c && Vector3.DistanceSquared(c.WorldCom, p.Place[i]) < r2)
                {
                    own = true;
                    break;
                }
            if (p.Razed[i])
            {
                // Lost: its own ships standing there with no enemy near take it back in the same time.
                if (!own || enemy)
                    p.EnemySince[i] = 0;
                else if (p.EnemySince[i] == 0)
                    p.EnemySince[i] = Tick;
                else if (Tick - p.EnemySince[i] >= (long)(Rules.RazeSeconds * T))
                {
                    (p.Razed[i], p.EnemySince[i]) = (false, 0);
                    Say($"{Name(a)} takes back its {p.PlaceName[i]}");
                }
                continue;
            }
            enemy &= !own;
            if (!enemy)
                p.EnemySince[i] = 0;
            else if (p.EnemySince[i] == 0)
                p.EnemySince[i] = Tick;
            else if (Tick - p.EnemySince[i] >= (long)(Rules.RazeSeconds * T))
            {
                p.Razed[i] = true;
                Say($"{Name(a)} has lost its {p.PlaceName[i]}");
            }
        }
    }

    /// <summary>Where its ships go home to: its first place it holds, else its first place (to take it back); -1: none.</summary>
    private int Home(Power p)
    {
        for (var i = 0; i < p.PlaceCount; i++)
            if (!p.Razed[i])
                return i;
        return p.PlaceCount > 0 ? 0 : -1;
    }

    private float NearestPerson(Vector3 at)
    {
        var best = float.MaxValue;
        for (var k = 0; k < _peopleN; k++)
            best = MathF.Min(best, Vector3.Distance(_people[k], at));
        return best;
    }

    // ---- Ships ----------------------------------------------------------------------------------------------------------------------

    /// <summary>Ships built are set down at home out of sight; ships out that are gone count a loss; a free ship near home docks.</summary>
    private void Upkeep(CrewWorld crew, ConstructWorld w, IPowerHost host, Power p, byte a)
    {
        var home = Home(p);
        for (var i = 0; i < MaxShips; i++)
        {
            ref var s = ref p.Ships[i];
            switch (s.State)
            {
                case PowerShipState.Building when s.Far == 0 && Tick >= s.ReadyAt && home >= 0 && !p.Razed[home]:
                    if (Bodies() >= Rules.MaxBodies || NearestPerson(p.Place[home]) < SpawnFrom)
                        break; // (out of sight only, and within the world's cap)
                    if (!host.Ground(p.Place[home] + new Vector3((i % 4 - 1.5f) * 14, 0, (i / 4 % 4 - 1.5f) * 18), out var at))
                        at = p.Place[home];
                    s.State = PowerShipState.Spawning;
                    Log.Add($"{Name(a)} sets down its {Blueprints[s.Bp].Name} at its {p.PlaceName[home]}"); // (a request: rare, and it allocates)
                    if (!host.Spawn(a, i, Blueprints[s.Bp], at, (i * 0.7f) % MathF.Tau))
                        s.State = PowerShipState.Building;
                    break;
                case PowerShipState.Out:
                    if (s.Body == 0 && s.Far == 0 && _adopts < 2)
                    {
                        _adopts++; // (after a load: two a think, each a scan of the crews)
                        Adopt(crew, w, p, a, ref s);
                    }
                    if (s.Body == 0)
                        break;
                    if (w.Get(s.Body) is not { Wreck: false } c || crew.ShipOf(s.Body) is not { } cs || cs.Prize || cs.Own || cs.Faction != a)
                    {
                        crew.Events.Add($"powers: {Name(a)} lost its {Blueprints[s.Bp].Name} (construct {s.Body})");
                        p.Lost++;
                        s = default;
                        s.Mission = -1;
                        break;
                    }
                    s.Uid = UidOf(crew, cs);
                    if (_dockedAt != Tick && s.Mission < 0 && s.Far == 0 && cs.Group < 0 && home >= 0 && !p.Razed[home] && Vector3.Distance(c.WorldCom, p.Place[home]) < Rules.DockRange + c.Radius
                        && NearestPerson(c.WorldCom) > DockFrom)
                        Dock(crew, host, p, ref s, c, cs);
                    break;
            }
        }
    }

    /// <summary>After a load, once the Fleet section is read: each mission's group by its slot and number, else the same power's group of that number;
    /// none: the mission's ships go home (no news: nothing was lost).</summary>
    private void Relink(CrewWorld crew, Power p, byte a)
    {
        p.Relink = false;
        for (var i = 0; i < MaxMissions; i++)
        {
            ref var m = ref p.Missions[i];
            if (m.Kind == MissionKind.None || m.Group < 0)
                continue;
            if (crew.Fleet.Of(m.Group) is { } g && g.Number == m.GroupNumber && g.Power == a)
                continue;
            m.Group = -1;
            foreach (var o in crew.Fleet.Groups)
                if (o.Live && o.Power == a && o.Number == m.GroupNumber)
                    m.Group = (short)o.Slot;
            if (m.Group < 0 && m.Stage == PowerMissionStage.Run)
                (m.Stage, m.StageAt) = (PowerMissionStage.Return, Tick);
        }
    }

    /// <summary>Free ships out away from home (their group ended under them, or a load put them there) go home together as one group, then dock.</summary>
    private void Strays(CrewWorld crew, ConstructWorld w, Power p, byte a)
    {
        var home = Home(p);
        if (home < 0)
            return;
        var n = 0;
        foreach (var s in p.Ships)
            if (s.State == PowerShipState.Out && s.Mission < 0 && s.Far == 0 && s.Body != 0 && crew.ShipOf(s.Body) is { Group: < 0 } && w.Get(s.Body) is { } c
                && Vector3.Distance(c.WorldCom, p.Place[home]) > Rules.DockRange * 2 && n < _ids.Length)
                _ids[n++] = s.Body;
        if (n == 0)
            return;
        var i = FreeMission(p);
        if (i < 0)
            return;
        var slot = crew.Fleet.Form(crew, w, GroupTask.Patrol, _ids.AsSpan(0, n), $"{Name(a)}'s ships going home");
        if (slot < 0)
            return;
        for (var k = 0; k < MaxShips; k++)
            if (p.Ships[k].State == PowerShipState.Out && p.Ships[k].Mission < 0 && p.Ships[k].Far == 0 && Array.IndexOf(_ids, p.Ships[k].Body, 0, n) >= 0)
                p.Ships[k].Mission = (sbyte)i;
        p.Missions[i] = new PowerMission { Kind = MissionKind.Patrol, Stage = PowerMissionStage.Return, Group = (short)slot, GroupNumber = crew.Fleet.Groups[slot].Number, OtherGroup = -1, Started = Tick, StageAt = Tick, ReadTick = crew.Tick };
        crew.Fleet.Command(slot, OrderKind.GoTo, p.Place[home]);
    }

    private static uint UidOf(CrewWorld crew, CrewShip cs) => cs.CaptainUid != 0 ? cs.CaptainUid : cs.MemberCount > 0 ? crew.Uid[cs.Members[0]] : 0;

    /// <summary>After a load (bodies' ids change in a save): an out ship takes up the body of its power whose crew holds its saved uid, and that is in no
    /// group but its mission's; none: the record is dropped (it was lost while the world was away, or its crew changed past knowing).</summary>
    private void Adopt(CrewWorld crew, ConstructWorld w, Power p, byte a, ref PowerShip s)
    {
        var group = s.Mission >= 0 ? p.Missions[s.Mission].Group : (short)-1;
        for (var k = 0; k < crew.ShipHigh && s.Uid != 0; k++)
        {
            if (crew.Ships[k] is not { Own: false, Prize: false } cs || cs.Faction != a || cs.Group >= 0 && cs.Group != group || w.Get(cs.Body) is not { Wreck: false })
                continue;
            var has = false;
            for (var j = 0; j < cs.MemberCount && !has; j++)
                has = crew.Uid[cs.Members[j]] == s.Uid;
            if (!has)
                continue;
            s.Body = cs.Body;
            return;
        }
        Log.Add($"{Name(a)}'s saved {Blueprints[s.Bp].Name} has no body now: dropped");
        s.State = PowerShipState.None;
        s.Mission = -1;
    }

    private void Dock(CrewWorld crew, IPowerHost host, Power p, ref PowerShip s, Construct c, CrewShip cs)
    {
        _dockedAt = Tick; // (one body taken out of the world a think: a removal costs a few ms)
        if (cs.Group >= 0)
            crew.Fleet.Leave(crew, cs);
        s.Hull = Math.Clamp(cs.Hull, 0.05f, 1);
        crew.Events.Add($"powers: {Name(cs.Faction)}'s {Blueprints[s.Bp].Name} (construct {c.Id}) docks at home, hull {s.Hull * 100:0}%");
        host.Dock(c);
        s.State = PowerShipState.Hangar;
        s.Body = 0;
        s.Mission = -1;
        p.Docked++;
    }

    /// <summary>The host set ship <paramref name="ship"/> down as <paramref name="c"/> (null: it couldn't; tried again in half a minute).</summary>
    public void Spawned(CrewWorld crew, byte a, int ship, Construct? c, string why)
    {
        if (a == 0 || a > N || ship < 0 || ship >= MaxShips)
            return;
        ref var s = ref Powers[a].Ships[ship];
        if (s.State != PowerShipState.Spawning)
            return;
        if (c == null || crew.ShipOf(c.Id) is not { } cs || cs.Faction != a)
        {
            Log.Add($"{Name(a)}'s {Blueprints[s.Bp].Name} wasn't set down{(c != null ? " crewed" : "")}: {why}");
            if (s.Far != 0)
            {
                s.State = PowerShipState.Far; // (a far group's: it stays a record and is tried again: CrewTerritory.cs)
                return;
            }
            s.State = PowerShipState.Building;
            s.ReadyAt = Tick + 30L * T;
            return;
        }
        s.State = PowerShipState.Out;
        s.Body = c.Id;
        s.Uid = UidOf(crew, cs);
        crew.Events.Add($"powers: {Name(a)}'s {Blueprints[s.Bp].Name} is out as construct {c.Id}");
    }

    private IPowerHost? _host; // (the last step's: a ship's goods are taken through it, TakeGoods)
    private readonly long[] _goodsSaidAt = new long[256]; // (when each power last said it waits for goods: once a minute)

    /// <summary>
    /// A ship's goods (<see cref="ControllerRules.GoodsOf"/>, times <paramref name="share"/> of a whole build) taken from power <paramref name="a"/>'s
    /// stations. False: short of one, nothing taken, and the wait is logged at most once a minute a power.
    /// </summary>
    private bool TakeGoods(byte a, int bp, float share)
    {
        if (_host == null || Rules.GoodsShare <= 0 || share <= 0)
            return true;
        if (_host.TakeGoods(a, Rules.GoodsOf(Blueprints[bp]), share * Rules.GoodsShare, out var shortOf))
            return true;
        if (Tick >= _goodsSaidAt[a])
        {
            _goodsSaidAt[a] = Tick + 60L * T;
            Log.Add($"{Name(a)} can't build a {Blueprints[bp].Name}: its stations are short of {RecyclingTable.Current.NameOf(shortOf)}");
        }
        return false;
    }

    /// <summary>A ship of <paramref name="role"/> for mission <paramref name="m"/>: one out and free, else one from the hangar (its repair paid), else one bought
    /// and built (its bill paid) within the caps. False: none now (no ship, no stock: spending never runs past what it has).</summary>
    private bool Field(Power p, byte a, ShipRole role, int m)
    {
        for (var i = 0; i < MaxShips; i++)
            if (p.Ships[i].State == PowerShipState.Out && p.Ships[i].Role == role && p.Ships[i].Mission < 0 && p.Ships[i].Far == 0 && p.Ships[i].Body != 0)
            {
                p.Ships[i].Mission = (sbyte)m;
                return true;
            }
        for (var i = 0; i < MaxShips; i++)
            if (p.Ships[i].State == PowerShipState.Hangar && p.Ships[i].Role == role)
            {
                ref var s = ref p.Ships[i];
                var repair = (1 - s.Hull) * Bills[s.Bp].Units * Rules.RepairShare;
                if (p.Stock < repair || !TakeGoods(a, s.Bp, (1 - s.Hull) * Rules.RepairShare))
                    continue;
                p.Stock -= repair;
                p.Spent += repair;
                s.State = PowerShipState.Building;
                s.ReadyAt = Tick + (long)((1 - s.Hull) * Bills[s.Bp].Seconds * T);
                s.Mission = (sbyte)m;
                return true;
            }
        var list = _fleet[a][(int)role];
        if (list.Length == 0 || p.ShipCount() >= p.Plan.Ships)
            return false;
        var bp = list[(p.Bought + m) % list.Length];
        var bill = Bills[bp];
        if (p.Stock < bill.Units)
            return false;
        for (var i = 0; i < MaxShips; i++)
            if (p.Ships[i].State == PowerShipState.None)
            {
                if (!TakeGoods(a, bp, 1))
                    return false; // (its stations are short: it waits, so cutting a supply line starves its yards)
                p.Stock -= bill.Units;
                p.Spent += bill.Units;
                p.Bought++;
                p.Ships[i] = new PowerShip { State = PowerShipState.Building, Role = role, Bp = bp, Hull = 1, ReadyAt = Tick + (long)(bill.Seconds * T), Mission = (sbyte)m };
                Log.Add($"{Name(a)} buys a {Blueprints[bp].Name} ({role}) for {bill.Units:0}: {p.Stock:0} left; ready in {bill.Seconds:0} s");
                return true;
            }
        return false;
    }

    private int Assigned(Power p, int m, ShipRole? role, bool outOnly)
    {
        var n = 0;
        foreach (var s in p.Ships)
            if (s.State != PowerShipState.None && s.Mission == m && (role == null || s.Role == role) && (!outOnly || s.State == PowerShipState.Out && s.Body != 0))
                n++;
        return n;
    }

    // ---- Missions ------------------------------------------------------------------------------------------------------------------

    private int FreeMission(Power p)
    {
        var live = 0;
        var free = -1;
        for (var i = 0; i < MaxMissions; i++)
            if (p.Missions[i].Kind != MissionKind.None)
                live++;
            else if (free < 0)
                free = i;
        return live >= p.Plan.Groups ? -1 : free;
    }

    private bool Running(Power p, MissionKind k)
    {
        foreach (var m in p.Missions)
            if (m.Kind == k)
                return true;
        return false;
    }

    private int Start(Power p, byte a, MissionKind kind, Vector3 at, string why)
    {
        var i = FreeMission(p);
        if (i < 0)
            return -1;
        p.Missions[i] = new PowerMission { Kind = kind, Stage = PowerMissionStage.Gather, Group = -1, OtherGroup = -1, At = at, Started = Tick, StageAt = Tick, ReadTick = -1 };
        Log.Add($"{Name(a)}: a {kind} mission ({why})");
        return i;
    }

    /// <summary>New missions by its plan: a convoy when its last is long enough ago; a raid on an enemy's convoy it knows of; a patrol.</summary>
    private void Plan(CrewWorld crew, ConstructWorld w, PowerRelations rel, IPowerHost host, Power p, byte a, NpcPowerDef def)
    {
        if (p.Plan.Convoy && p.Held() >= 2 && Tick >= p.NextConvoy && !Running(p, MissionKind.Convoy))
        {
            int from = -1, to = -1;
            for (var i = 0; i < p.PlaceCount; i++)
                if (!p.Razed[i])
                {
                    if (from < 0)
                        from = i;
                    else if (to < 0)
                        to = i;
                }
            var m = Start(p, a, MissionKind.Convoy, p.Place[from], $"cargo from its {p.PlaceName[from]} to its {p.PlaceName[to]}");
            if (m >= 0)
            {
                ref var mi = ref p.Missions[m];
                (mi.From, mi.To, mi.NeedCargo, mi.NeedArmed, mi.Armed) = ((byte)from, (byte)to, (byte)p.Plan.ConvoyCargo, (byte)p.Plan.ConvoyEscorts, ShipRole.Escort);
                p.NextConvoy = Tick + (long)(Rules.ConvoyEveryMinutes * 60 * T);
            }
        }
        if (p.Plan.Raid && !Running(p, MissionKind.Raid) && Home(p) is var home and >= 0)
        {
            // A target it knows of (fog: near one of its places, or seen by one of its crews), scored by what it carries and how far it is.
            var best = 0f;
            byte victim = 0;
            short slot = -1;
            for (var b = (byte)1; b <= Count; b++)
            {
                if (b == a || rel.Gone[b] || rel.BandOf(a, b) != RelBand.War || !rel.Fights(a, b))
                    continue;
                foreach (var m in Powers[b].Missions)
                {
                    if (m.Kind != MissionKind.Convoy || m.Stage != PowerMissionStage.Run || crew.Fleet.Of(m.Group) is not { } g || g.Number != m.GroupNumber || w.Get(g.Flagship) is not { } f)
                        continue;
                    var at = f.WorldCom;
                    var known = host.Sees(a, at);
                    for (var i = 0; i < p.PlaceCount && !known; i++)
                        known = !p.Razed[i] && Vector3.Distance(p.Place[i], at) < Rules.RaidSight;
                    if (!known || g.Count > p.Plan.RaidShips * 2 / def.Caution)
                        continue;
                    var score = Rules.CargoValue * (0.5f + def.Greed) * (0.5f + def.Aggression) / (1 + Vector3.Distance(at, p.Place[home]) / 1000);
                    if (score > best)
                        (best, victim, slot) = (score, b, m.Group);
                }
            }
            if (victim != 0 && crew.Fleet.Of(slot) is { } tg)
            {
                var m = Start(p, a, MissionKind.Raid, p.Place[home], $"{Name(victim)}'s convoy, group {tg.Number}");
                if (m >= 0)
                {
                    ref var mi = ref p.Missions[m];
                    (mi.Other, mi.OtherGroup, mi.OtherNumber, mi.NeedArmed, mi.Armed) = (victim, slot, tg.Number, (byte)p.Plan.RaidShips, ShipRole.Raider);
                    mi.CargoSeen = CargoIn(crew, w, tg);
                }
            }
        }
        if (p.Plan.Patrol && !Running(p, MissionKind.Patrol) && Home(p) is var ph and >= 0)
        {
            var m = Start(p, a, MissionKind.Patrol, p.Place[ph], $"round its {p.PlaceName[ph]}");
            if (m >= 0)
                (p.Missions[m].NeedArmed, p.Missions[m].Armed) = ((byte)p.Plan.PatrolShips, ShipRole.Warship);
        }
    }

    /// <summary>
    /// A group's cargo ships still with it and their cargo still theirs (by its power's records: a cargo hauler may carry a gun). One shot up (its hull
    /// under the rules' raidTake share: holds breached, cargo shot out or handed over) has lost its cargo to whoever did it: a raid loots it without
    /// having to destroy the hauler (machine guns took minutes to wreck a 4x4 Truck), and it delivers nothing.
    /// </summary>
    /// <summary>Power <paramref name="b"/>'s convoy in that group has delivered its cargo (it goes home empty).</summary>
    private bool Delivered(byte b, short group, ushort number)
    {
        if (b == 0 || b > N)
            return false;
        foreach (var o in Powers[b].Missions)
            if (o.Kind == MissionKind.Convoy && o.Group == group && o.GroupNumber == number)
                return o.Paid;
        return false;
    }

    private int CargoIn(CrewWorld crew, ConstructWorld w, CrewGroup g)
    {
        var n = 0;
        if (g.Power == 0 || g.Power > N)
            return 0;
        foreach (var s in Powers[g.Power].Ships)
            if (s.State == PowerShipState.Out && s.Role == ShipRole.Cargo && s.Body != 0 && crew.ShipOf(s.Body) is { } cs && cs.Hull >= Rules.RaidTake && cs.Group == g.Slot && w.Get(s.Body) is { Wreck: false })
                n++;
        return n;
    }

    private void StepMission(CrewWorld crew, ConstructWorld w, PowerRelations rel, IPowerHost host, Power p, byte a, int i, NpcPowerDef def)
    {
        ref var m = ref p.Missions[i];
        var home = Home(p);
        switch (m.Stage)
        {
            case PowerMissionStage.Gather:
                Gather(crew, w, p, a, i, ref m);
                return;
            case PowerMissionStage.Return:
                ReturnHome(crew, w, host, p, a, i, ref m, home);
                return;
        }
        var g = crew.Fleet.Of(m.Group);
        if (g == null || g.Number != m.GroupNumber || g.Power != a)
        {
            Say(m.Kind == MissionKind.Convoy ? $"{Name(a)}'s convoy is lost" : $"{Name(a)}'s {m.Kind.ToString().ToLowerInvariant()} group is gone");
            End(p, i);
            return;
        }
        var flag = w.Get(g.Flagship);
        // Its ships that came late (a defence goes with what it has) join the group.
        foreach (var s in p.Ships)
            if (s.State == PowerShipState.Out && s.Mission == i && s.Body != 0 && crew.ShipOf(s.Body) is { Group: < 0 } && g.Count < CrewGroup.MaxShips)
                crew.Fleet.Join(crew, w, m.Group, s.Body);
        // Its group's reports since the last look.
        var contact = g.Enemy > 0;
        if (m.ReadTick < 0)
            m.ReadTick = crew.Tick; // (a mission loaded or not yet run: from now on)
        for (var k = 0; k < g.RingCount; k++)
        {
            ref var e = ref g.Ring[(g.RingHead - 1 - k + CrewGroup.RingSize * 2) % CrewGroup.RingSize];
            if (e.Tick <= m.ReadTick)
                break;
            if (e.Kind == FleetReport.Arrived && m.Kind == MissionKind.Convoy)
            {
                        if (e.Other == 1 && !m.Paid)
                {
                    m.Paid = true;
                    p.Stock += Rules.CargoValue * CargoIn(crew, w, g) / Math.Max(1, (int)m.NeedCargo);
                    p.Earned += Rules.CargoValue * CargoIn(crew, w, g) / Math.Max(1, (int)m.NeedCargo);
                    Say($"{Name(a)}'s convoy delivers its cargo to its {p.PlaceName[m.To]}");
                }
                else if (e.Other == 2 && m.Paid)
                    m.StageAt = -1; // (back home: done)
            }
        }
        m.ReadTick = crew.Tick; // (the reports are stamped with the crews' tick, not this clock: one tick apart, a convoy's arrival went unread and it never delivered)
        switch (m.Kind)
        {
            case MissionKind.Convoy:
                if (contact && Tick - m.CalledAt > 30L * T && flag != null)
                    Call(crew, w, rel, a, g, flag.WorldCom, ref m);
                if (m.StageAt < 0 || Tick - m.Started > (long)(Rules.RunMinutes * 60 * T))
                    Back(crew, p, a, i, ref m, home, "home");
                return;
            case MissionKind.Raid:
            {
                var tg = crew.Fleet.Of(m.OtherGroup);
                var alive = tg != null && tg.Number == m.OtherNumber && w.Get(tg.Flagship) is not null;
                var delivered = alive && Delivered(m.Other, m.OtherGroup, m.OtherNumber); // (on its way home, delivered, it carries nothing to take)
                var cargo = alive && !delivered ? CargoIn(crew, w, tg!) : 0;
                if (alive && !delivered && cargo < m.CargoSeen && !m.Looted) // (a convoy home and stood down has lost nothing)
                {
                    m.Looted = true;
                    var loot = Rules.CargoValue * Rules.RaidLoot * (m.CargoSeen - cargo);
                    p.Stock += loot;
                    p.Looted += loot;
                    rel.Post(RelEvent.RaidConvoy, m.Other, a, 255, flag?.WorldCom ?? m.At);
                    Say($"{Name(a)} raids {Name(m.Other)}'s convoy and takes its cargo");
                }
                m.CargoSeen = Math.Min(m.CargoSeen, cargo);
                if (!alive || m.Looted || cargo == 0 || Tick - m.Started > (long)(Rules.RunMinutes * 60 * T) || g.Retreating && Tick - m.QuietSince > 20L * T)
                {
                    Back(crew, p, a, i, ref m, home, m.Looted ? "home with the loot" : "home");
                    return;
                }
                if (!g.Retreating)
                    m.QuietSince = Tick;
                var at = w.Get(tg!.Flagship)!.WorldCom;
                if (flag != null && Vector3.Distance(flag.WorldCom, at) > 400)
                    crew.Fleet.Command(m.Group, OrderKind.GoTo, at, now: false); // (its captains fight on sight once near; the admiral focuses them)
                else if (g.Order != OrderKind.Hold)
                    crew.Fleet.Command(m.Group, OrderKind.Hold);
                return;
            }
            case MissionKind.Defend:
            {
                var cg = crew.Fleet.Of(m.OtherGroup);
                var callerAlive = cg != null && cg.Number == m.OtherNumber && w.Get(cg.Flagship) is { Wreck: false };
                if (callerAlive && cg!.Power == m.Caller)
                {
                    if (g.Order != OrderKind.Escort || g.OrderBody != cg.Flagship)
                        crew.Fleet.Command(m.Group, OrderKind.Escort, default, cg.Flagship);
                }
                else
                {
                    if (m.CallerPerson != 0 && host.PersonAt((byte)(m.CallerPerson - 1), out var person))
                        m.At = person; // (phase 5: a player who called is followed)
                    if (g.Order != OrderKind.GoTo || Vector3.Distance(g.OrderAt, m.At) > 50)
                        crew.Fleet.Command(m.Group, OrderKind.GoTo, m.At);
                }
                if (contact || cg is { Enemy: > 0 })
                    m.QuietSince = Tick;
                var near = flag != null && Vector3.Distance(flag.WorldCom, callerAlive ? w.Get(cg!.Flagship)!.WorldCom : m.At) < 600;
                if (near && Tick - m.QuietSince > (long)(Rules.QuietSeconds * T) || Tick - m.StageAt > (long)(Rules.DefendMinutes * 60 * T))
                {
                    if (callerAlive && m.Caller != a)
                        rel.Post(RelEvent.EscortConvoy, m.Caller, a, 255, m.At);
                    Back(crew, p, a, i, ref m, home, "home: the danger is past");
                }
                return;
            }
            case MissionKind.Patrol:
                if (Tick - m.StageAt > (long)(Rules.PatrolMinutes * 60 * T))
                    Back(crew, p, a, i, ref m, home, "home");
                return;
        }
    }

    /// <summary>Its ships fielded; all out and crewed: a group under an admiral, given its orders.</summary>
    private void Gather(CrewWorld crew, ConstructWorld w, Power p, byte a, int i, ref PowerMission m)
    {
        var needCargo = m.Kind == MissionKind.Convoy ? m.NeedCargo : 0;
        while (Assigned(p, i, ShipRole.Cargo, false) < needCargo && Field(p, a, ShipRole.Cargo, i)) { }
        while (Assigned(p, i, m.Armed, false) < m.NeedArmed && Field(p, a, m.Armed, i)) { }
        var haveCargo = Assigned(p, i, ShipRole.Cargo, true);
        var haveArmed = Assigned(p, i, m.Armed, true);
        var timeout = Tick - m.StageAt > (long)(Rules.GatherMinutes * 60 * T);
        var ready = haveCargo >= needCargo && haveArmed >= m.NeedArmed;
        // A defence goes with what it has when the call is urgent; a convoy needs its cargo; a raid at least one raider.
        if (!ready && !(timeout && haveCargo >= needCargo && haveCargo + haveArmed > 0))
        {
            if (timeout && Tick - m.StageAt > (long)(Rules.GatherMinutes * 120 * T))
            {
                Log.Add($"{Name(a)}'s {m.Kind} mission gives up: its ships didn't come (stock {p.Stock:0})");
                End(p, i);
            }
            return;
        }
        var n = 0;
        foreach (var role in (ReadOnlySpan<ShipRole>)[ShipRole.Cargo, m.Armed])
            foreach (var s in p.Ships)
                if (s.State == PowerShipState.Out && s.Mission == i && s.Role == role && s.Body != 0 && n < _ids.Length)
                    _ids[n++] = s.Body;
        var task = m.Kind switch { MissionKind.Convoy => GroupTask.Convoy, MissionKind.Raid => GroupTask.Strike, MissionKind.Defend => GroupTask.Defend, _ => GroupTask.Patrol };
        var slot = crew.Fleet.Form(crew, w, task, _ids.AsSpan(0, n), $"{Name(a)}'s {m.Kind.ToString().ToLowerInvariant()}");
        if (slot < 0)
            return;
        var g = crew.Fleet.Groups[slot];
        (m.Group, m.GroupNumber, m.Stage, m.StageAt, m.QuietSince, m.ReadTick) = ((short)slot, g.Number, PowerMissionStage.Run, Tick, Tick, crew.Tick);
        switch (m.Kind)
        {
            case MissionKind.Convoy:
            {
                Span<Vector3> pts = [p.Place[m.From], p.Place[m.To], p.Place[m.From]];
                Span<OrderKind> work = [OrderKind.Load, OrderKind.Deliver, OrderKind.Route];
                crew.Fleet.SetRoute(slot, pts, work, Rules.DwellSeconds, loop: false);
                Say($"{Name(a)} sends a convoy of {n} from its {p.PlaceName[m.From]} to its {p.PlaceName[m.To]} under Admiral {CrewNames.Of(g.AdmiralName)}");
                break;
            }
            case MissionKind.Patrol:
            {
                Span<Vector3> pts = stackalloc Vector3[4];
                for (var k = 0; k < 4; k++)
                    pts[k] = m.At + new Vector3(MathF.Cos(k * MathF.PI / 2), 0, MathF.Sin(k * MathF.PI / 2)) * 300;
                Span<OrderKind> work = [OrderKind.Route, OrderKind.Route, OrderKind.Route, OrderKind.Route];
                crew.Fleet.SetRoute(slot, pts, work, 0, loop: true);
                break;
            }
            case MissionKind.Raid:
                if (crew.Fleet.Of(m.OtherGroup) is { } tg && w.Get(tg.Flagship) is { } tf)
                    crew.Fleet.Command(slot, OrderKind.GoTo, tf.WorldCom);
                Say($"{Name(a)} sends {n} raiders after {Name(m.Other)}'s convoy");
                break;
            case MissionKind.Defend:
                crew.Fleet.Command(slot, OrderKind.GoTo, m.At);
                break;
        }
    }

    private void Back(CrewWorld crew, Power p, byte a, int i, ref PowerMission m, int home, string why)
    {
        m.Stage = PowerMissionStage.Return;
        m.StageAt = Tick;
        if (home >= 0)
            crew.Fleet.Command(m.Group, m.Kind == MissionKind.Raid || crew.Fleet.Of(m.Group) is { Retreating: true } ? OrderKind.Retreat : OrderKind.GoTo, p.Place[home]);
        crew.Events.Add($"powers: {Name(a)}'s {m.Kind} group goes {why}");
    }

    /// <summary>Home: each ship docks there out of everyone's sight (its hull share kept, its repair paid when it is fielded again).</summary>
    private void ReturnHome(CrewWorld crew, ConstructWorld w, IPowerHost host, Power p, byte a, int i, ref PowerMission m, int home)
    {
        var left = 0;
        for (var k = 0; k < MaxShips; k++)
        {
            ref var s = ref p.Ships[k];
            if (s.Mission != i || s.State == PowerShipState.None)
                continue;
            if (s.State != PowerShipState.Out || home < 0 || w.Get(s.Body) is not { } c || crew.ShipOf(s.Body) is not { } cs)
            {
                if (s.State is PowerShipState.Building or PowerShipState.Hangar)
                    s.Mission = -1;
                continue;
            }
            if (_dockedAt != Tick && !p.Razed[home] && Vector3.Distance(c.WorldCom, p.Place[home]) < Rules.DockRange + c.Radius && NearestPerson(c.WorldCom) > DockFrom)
                Dock(crew, host, p, ref s, c, cs);
            else
                left++;
        }
        if (left == 0 || Tick - m.StageAt > (long)(Rules.RunMinutes * 60 * T))
            End(p, i); // (a ship still out stays free: it docks when it is home and nobody is near: Upkeep)
    }

    private void End(Power p, int i)
    {
        for (var k = 0; k < MaxShips; k++)
            if (p.Ships[k].Mission == i)
                p.Ships[k].Mission = -1;
        p.Missions[i] = default;
        p.Missions[i].Group = -1;
    }

    // ---- Calls for help ------------------------------------------------------------------------------------------------------------

    /// <summary>A group under attack calls: its enemy is the power its members are fighting.</summary>
    private void Call(CrewWorld crew, ConstructWorld w, PowerRelations rel, byte a, CrewGroup g, Vector3 at, ref PowerMission m)
    {
        byte enemy = 0;
        for (var k = 0; k < g.Count && enemy == 0; k++)
            if (crew.ShipOf(g.Ships[k]) is { } s && crew.Tick - s.TargetTick < 5L * T && s.TargetBody != 0 && crew.ShipOf(s.TargetBody) is { Faction: not 0 } ts && ts.Faction != a)
                enemy = ts.Faction;
        if (enemy == 0)
            return;
        m.CalledAt = Tick;
        Calls[CallTotal % MaxCalls] = new PowerCall { Caller = a, Enemy = enemy, At = at, Group = (short)g.Slot, Number = g.Number, Tick = Tick };
        CallTotal++;
        if (!m.Called)
            Say($"{Name(a)}'s convoy is attacked by {Name(enemy)} and calls for help");
        m.Called = true;
    }

    /// <summary>Calls it hasn't heard: an ally answers (or is thought less of for not), and a power neutral or better to the caller that fights its enemy
    /// answers when its loyalty is high enough (section 5.2: "may answer a call for help against a common enemy").</summary>
    private void Hear(CrewWorld crew, ConstructWorld w, PowerRelations rel, Power p, byte a, NpcPowerDef def)
    {
        if (CallTotal - p.CallRead > MaxCalls)
            p.CallRead = CallTotal - MaxCalls;
        for (; p.CallRead < CallTotal; p.CallRead++)
        {
            var c = Calls[p.CallRead % MaxCalls];
            if (c.Party != 0)
            {
                HearParty(rel, p, a, def, c);
                continue;
            }
            if (c.Caller == a || Tick - c.Tick > 60L * T || rel.Gone[c.Caller] || !rel.Fights(a, c.Enemy))
                continue;
            var pact = rel.PactOf(a, c.Caller);
            var ally = pact is PactKind.Alliance or PactKind.Vassalage;
            var willing = ally || rel.BandOf(a, c.Caller) >= RelBand.Neutral && def.Loyalty >= 0.4f;
            if (!willing)
                continue;
            var already = false;
            foreach (var m in p.Missions)
                already |= m.Kind == MissionKind.Defend && m.Caller == c.Caller;
            if (already)
                continue;
            var home = Home(p);
            var near = home >= 0 && Vector3.Distance(p.Place[home], c.At) < Rules.CallRange;
            var i = p.Plan.Defend && near ? Start(p, a, MissionKind.Defend, c.At, $"{Name(c.Caller)}'s call against {Name(c.Enemy)}") : -1;
            if (i < 0)
            {
                if (ally)
                {
                    rel.Post(RelEvent.RefuseCall, c.Caller, a, 255, c.At);
                    Say($"{Name(a)} doesn't answer its ally {Name(c.Caller)}'s call");
                }
                continue;
            }
            ref var mi = ref p.Missions[i];
            (mi.Caller, mi.Other, mi.OtherGroup, mi.OtherNumber, mi.NeedArmed, mi.Armed) = (c.Caller, c.Enemy, c.Group, c.Number, (byte)p.Plan.DefendShips, ShipRole.Warship);
            mi.StageAt = Tick - (long)(Rules.GatherMinutes * 60 * T) + 20L * T; // (a call is urgent: it goes with what it has in 20 s)
            rel.Post(RelEvent.AnswerCall, c.Caller, a, 255, c.At);
            Say($"{Name(a)} answers {Name(c.Caller)}'s call against {Name(c.Enemy)}");
        }
    }

    /// <summary>Phase 5: a player faction calls its allies (the Faction window) against <paramref name="enemy"/> (0: whoever is fighting them) at <paramref name="at"/>.</summary>
    public void CallForParty(byte party, byte person, byte enemy, Vector3 at)
    {
        Calls[CallTotal % MaxCalls] = new PowerCall { Party = party, Person = (byte)(person + 1), Enemy = enemy, At = at, Group = -1, Tick = Tick };
        CallTotal++;
    }

    /// <summary>A faction's call: an ally answers (it must, as an ally's), a power neutral or better to the faction answers when loyal enough and it fights the foe.</summary>
    private void HearParty(PowerRelations rel, Power p, byte a, NpcPowerDef def, in PowerCall c)
    {
        if (Tick - c.Tick > 60L * T || c.Enemy == a)
            return;
        var ally = rel.PartyPact(a, c.Party) == PactKind.Alliance;
        var willing = ally || rel.PartyGet(a, c.Party) >= rel.Rules.NeutralLine && def.Loyalty >= 0.4f && c.Enemy != 0 && rel.Fights(a, c.Enemy);
        if (!willing)
            return;
        foreach (var m in p.Missions)
            if (m.Kind == MissionKind.Defend && m.CallerParty == c.Party)
                return; // (already on its way)
        var home = Home(p);
        var near = home >= 0 && Vector3.Distance(p.Place[home], c.At) < Rules.CallRange;
        var i = p.Plan.Defend && near ? Start(p, a, MissionKind.Defend, c.At, $"a player faction's call") : -1;
        var who = rel.Table.Names[c.Party] is { Length: > 0 } n ? n : "a faction";
        if (i < 0)
        {
            if (ally)
            {
                rel.NoteFor(c.Party, $"{Name(a)} can't answer your call: {(!p.Plan.Defend ? "it sends no help" : !near ? "you are too far from its places" : "it has no room for another mission")}.");
                Say($"{Name(a)} doesn't answer its ally {who}'s call");
            }
            return;
        }
        ref var mi = ref p.Missions[i];
        (mi.Caller, mi.CallerParty, mi.CallerPerson, mi.Other, mi.OtherGroup, mi.OtherNumber, mi.NeedArmed, mi.Armed) = (0, c.Party, c.Person, c.Enemy, -1, 0, (byte)p.Plan.DefendShips, ShipRole.Warship);
        mi.StageAt = Tick - (long)(Rules.GatherMinutes * 60 * T) + 20L * T; // (urgent: it goes with what it has in 20 s)
        rel.NoteFor(c.Party, $"{Name(a)} answers your call: its warships are on their way.");
        Say($"{Name(a)} answers {who}'s call{(c.Enemy != 0 ? $" against {Name(c.Enemy)}" : "")}");
    }

    // ---- Merging (Karl's answer 2) -----------------------------------------------------------------------------------------------

    /// <summary>Power <paramref name="a"/> joins <paramref name="b"/>: its ships out change sides, its hangar and stock go over as far as b's caps hold, then it is gone.</summary>
    public void MergeInto(CrewWorld crew, ConstructWorld w, PowerRelations rel, byte a, byte b)
    {
        var pa = Powers[a];
        var pb = Powers[b];
        for (var k = 0; k < MaxShips; k++)
        {
            var s = pa.Ships[k];
            if (s.State == PowerShipState.Out && w.Get(s.Body) is { } c && crew.ShipOf(s.Body) is { } cs)
            {
                if (cs.Group >= 0)
                    crew.Fleet.Leave(crew, cs);
                cs.Faction = b;
                c.NpcFaction = b;
                for (var j = 0; j < cs.MemberCount; j++)
                    crew.Fac[cs.Members[j]] = b;
                cs.SeatsDirty = true;
            }
            if (s.State == PowerShipState.Far)
                s = s with { State = PowerShipState.Hangar, Far = 0 };
            if (s.State is PowerShipState.Out or PowerShipState.Hangar && Array.FindIndex(pb.Ships, x => x.State == PowerShipState.None) is var free and >= 0)
                pb.Ships[free] = s with { Mission = -1, Far = 0 }; // (a merge: rare; its blueprint index is shared)
        }
        pb.Stock += pa.Stock;
        pb.Received += pa.Stock;
        pa.Paid += pa.Stock;
        pa.Stock = 0;
        pa.Fallen = true;
        pa.Heir = b;
        Array.Clear(pa.Ships);
        for (var i = 0; i < MaxMissions; i++)
            End(pa, i);
        FarMerged(a, b); // (its far groups end, its sites go to b: CrewTerritory.cs)
        rel.Fall(a, $"{Name(a)} joins {Name(b)}: they are one power now", into: b); // (its other crews follow: the host re-tags them, Game.CrewPowersGone)
    }

    // ---- A tool's and the window's look ------------------------------------------------------------------------------------------

    public string Text(CrewWorld crew)
    {
        var sb = new System.Text.StringBuilder();
        for (var a = 1; a <= Count; a++)
        {
            var p = Powers[a];
            if (!p.Plan.Fields)
                continue;
            int build = 0, hangar = 0, out_ = 0;
            foreach (var s in p.Ships)
                (build, hangar, out_) = s.State switch { PowerShipState.Building or PowerShipState.Spawning => (build + 1, hangar, out_), PowerShipState.Hangar => (build, hangar + 1, out_), PowerShipState.Out => (build, hangar, out_ + 1), _ => (build, hangar, out_) };
            sb.Append($"  {Name((byte)a),-12} stock {p.Stock,7:0} (start {p.StartStock:0} +earned {p.Earned:0} +loot {p.Looted:0} +got {p.Received:0} -spent {p.Spent:0} -paid {p.Paid:0}); ships out {out_}, building {build}, hangar {hangar}, lost {p.Lost}; places {p.Held()}/{p.PlaceCount}{(p.Fallen ? "; GONE" : "")}");
            foreach (var m in p.Missions)
                if (m.Kind != MissionKind.None)
                    sb.Append($"; {m.Kind} {m.Stage}{(crew.Fleet.Of(m.Group) is { } g && g.Number == m.GroupNumber ? $" (group {g.Number}, {g.Count} ships, order {g.Order}{(g.Retreating ? ", retreating" : "")})" : "")}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ---- Saves (the world's Controllers section) --------------------------------------------------------------------------------

    public const byte SaveVersion = 2; // 2: phase 4's sites, far groups, a ship's far group (version 1 still reads: its territory starts from the data)

    /// <summary>
    /// u8 version; u8 n; per power: text name, u8 flags (1 fallen, 2 started), 7 x f64 (stock, start, earned, spent, looted, paid, received), text world,
    /// u8 places (text name, 3 x f32, u8 razed), u8 ships (u8 state, u8 role, text blueprint, f32 hull, i32 seconds to ready, i8 mission), u8 missions
    /// (u8 kind, u8 stage, i16 group, u16 number, text other, text caller, i16 other group, u16 other number, 3 x f32 at, u8 from, u8 to, u8 cargo,
    /// u8 armed, u8 armed role, u8 flags (1 paid, 2 called, 4 looted), i32 seconds since started, i32 seconds in its stage), i32 seconds to the next convoy.
    /// A ship out is kept as a record whose body is taken up again by the first crewed body of its power (bodies' ids change).
    /// </summary>
    public byte[] SaveBytes()
    {
        var w = new SaveWriter(1024);
        w.U8(SaveVersion).U8((byte)Count);
        for (var a = 1; a <= Count; a++)
        {
            var p = Powers[a];
            w.Str(Name((byte)a)).U8((byte)((p.Fallen ? 1 : 0) | (p.Started ? 2 : 0) | (p.HadSites ? 4 : 0))).Str(p.Heir != 0 ? Name(p.Heir) : "");
            w.F64(p.Stock).F64(p.StartStock).F64(p.Earned).F64(p.Spent).F64(p.Looted).F64(p.Paid).F64(p.Received);
            w.Str(p.World).U8((byte)p.PlaceCount);
            for (var i = 0; i < p.PlaceCount; i++)
                w.Str(p.PlaceName[i]).F32(p.Place[i].X).F32(p.Place[i].Y).F32(p.Place[i].Z).U8((byte)(p.Razed[i] ? 1 : 0));
            w.U8((byte)p.ShipCount());
            foreach (var s in p.Ships)
                if (s.State != PowerShipState.None)
                    w.U8((byte)(s.State == PowerShipState.Spawning ? s.Far != 0 ? PowerShipState.Far : PowerShipState.Building : s.State)).U8((byte)s.Role).Str(Blueprints[s.Bp].Name).F32(s.Hull)
                        .I32((int)Math.Max(0, (s.ReadyAt - Tick) / T)).U8((byte)s.Mission).U32(s.State == PowerShipState.Out ? s.Uid : 0).U16((ushort)s.Far);
            var mn = 0;
            foreach (var m in p.Missions)
                if (m.Kind != MissionKind.None)
                    mn++;
            w.U8((byte)mn);
            for (var i = 0; i < MaxMissions; i++)
            {
                var m = p.Missions[i];
                if (m.Kind == MissionKind.None)
                    continue;
                w.U8((byte)m.Kind).U8((byte)m.Stage).U16((ushort)m.Group).U16(m.GroupNumber).Str(m.Other != 0 ? Name(m.Other) : "").Str(m.Caller != 0 ? Name(m.Caller) : "")
                    .U16((ushort)m.OtherGroup).U16(m.OtherNumber).F32(m.At.X).F32(m.At.Y).F32(m.At.Z).U8(m.From).U8(m.To).U8(m.NeedCargo).U8(m.NeedArmed).U8((byte)m.Armed)
                    .U8((byte)((m.Paid ? 1 : 0) | (m.Called ? 2 : 0) | (m.Looted ? 4 : 0))).I32((int)((Tick - m.Started) / T)).I32((int)((Tick - m.StageAt) / T)).U8((byte)i);
            }
            w.I32((int)Math.Max(0, (p.NextConvoy - Tick) / T)).I32((int)Math.Max(0, (p.NextTake - Tick) / T));
        }
        TerritorySave(w); // (phase 4: CrewTerritory.cs)
        return w.Span.ToArray();
    }

    /// <summary>A saved world's controllers over a fresh <see cref="Reset"/> (a power the save names that isn't read is left out, with a line; a section that doesn't read leaves every power at its plan's start, logged).</summary>
    public void Load(PowerRelations rel, byte[] bytes)
    {
        Reset();
        try
        {
            var r = new SaveReader(bytes);
            var v = r.U8();
            if (v is not (1 or SaveVersion))
            {
                Log.Add($"the world's controllers section is of version {v} (this build reads {SaveVersion}): every power starts from its plan");
                return;
            }
            int n = r.U8();
            for (var k = 0; k < n; k++)
            {
                var name = r.Str();
                var id = CrewPowers.Find(name);
                var p = id > 0 && Name(id).Equals(name, StringComparison.OrdinalIgnoreCase) ? Powers[id] : new Power();
                if (p.Id == 0)
                    Log.Add($"the world's controllers name power \"{name}\", which isn't read now: left out");
                var flags = r.U8();
                p.Fallen = (flags & 1) != 0;
                p.Started = (flags & 2) != 0;
                p.HadSites |= (flags & 4) != 0;
                p.Heir = CrewPowers.Find(r.Str());
                p.Relink = true;
                (p.Stock, p.StartStock, p.Earned, p.Spent, p.Looted, p.Paid, p.Received) = (r.F64(), r.F64(), r.F64(), r.F64(), r.F64(), r.F64(), r.F64());
                p.World = r.Str();
                p.PlaceCount = 0;
                int pc = r.U8();
                for (var i = 0; i < pc; i++)
                {
                    var pn = r.Str();
                    var at = new Vector3(r.F32(), r.F32(), r.F32());
                    var razed = r.U8() != 0;
                    if (p.PlaceCount < MaxPlaces)
                    {
                        AddPlace(p, pn, at);
                        p.Razed[p.PlaceCount - 1] = razed;
                    }
                }
                int sc = r.U8();
                for (var i = 0; i < sc; i++)
                {
                    var st = (PowerShipState)r.U8();
                    var role = (ShipRole)r.U8();
                    var bpName = r.Str();
                    var hull = r.F32();
                    var ready = r.I32();
                    var mission = (sbyte)r.U8();
                    var uid = r.U32();
                    var far = v >= 2 ? (short)r.U16() : (short)0;
                    var bp = Blueprints.FindIndex(b => b.Name.Equals(bpName, StringComparison.OrdinalIgnoreCase));
                    if (bp < 0 && ConstructBuild.FindBlueprint(bpName) is { } found)
                    {
                        bp = Blueprints.Count;
                        Blueprints.Add(found);
                        Bills.Add(Rules.BillOf(found));
                    }
                    if (bp < 0 || p.Id == 0 || i >= MaxShips || st is not (PowerShipState.Building or PowerShipState.Hangar or PowerShipState.Out or PowerShipState.Far) || role >= ShipRole.Count)
                    {
                        Log.Add($"{name}'s saved ship \"{bpName}\" can't be kept (no such blueprint now): left out");
                        continue;
                    }
                    p.Ships[i] = new PowerShip { State = st, Role = role, Bp = (short)bp, Hull = hull, ReadyAt = Tick + (long)ready * T, Mission = mission, Uid = uid, Far = (short)Math.Clamp((int)far, 0, MaxFar) };
                }
                int mc = r.U8();
                for (var i = 0; i < mc; i++)
                {
                    var m = new PowerMission { Kind = (MissionKind)r.U8(), Stage = (PowerMissionStage)r.U8(), Group = (short)r.U16(), GroupNumber = r.U16() };
                    var other = r.Str();
                    var caller = r.Str();
                    (m.Other, m.Caller) = (CrewPowers.Find(other), CrewPowers.Find(caller));
                    (m.OtherGroup, m.OtherNumber) = ((short)r.U16(), r.U16());
                    m.At = new Vector3(r.F32(), r.F32(), r.F32());
                    (m.From, m.To, m.NeedCargo, m.NeedArmed, m.Armed) = (r.U8(), r.U8(), r.U8(), r.U8(), (ShipRole)r.U8());
                    var mf = r.U8();
                    (m.Paid, m.Called, m.Looted) = ((mf & 1) != 0, (mf & 2) != 0, (mf & 4) != 0);
                    m.Started = Tick - (long)r.I32() * T;
                    m.StageAt = Tick - (long)r.I32() * T;
                    m.ReadTick = -1;
                    m.QuietSince = Tick;
                    int slot = r.U8();
                    if (p.Id != 0 && slot < MaxMissions && m.Kind is > MissionKind.None and <= MissionKind.Raid)
                        p.Missions[slot] = m;
                }
                p.NextConvoy = Tick + (long)r.I32() * T;
                if (v >= 2)
                    p.NextTake = Tick + (long)r.I32() * T;
                if (p.Fallen && p.Id != 0)
                    rel.Fall(p.Id, "", quiet: true, into: p.Heir);
            }
            if (v >= 2)
                TerritoryLoad(r);
            FarTidy(); // (a ship whose far group didn't read goes into its hangar)
        }
        catch (Exception ex) when (ex is WorldFileException or ArgumentException or IndexOutOfRangeException or InvalidDataException)
        {
            Log.Add($"the world's controllers section doesn't read ({ex.Message}): every power starts from its plan");
            Reset();
        }
    }
}
