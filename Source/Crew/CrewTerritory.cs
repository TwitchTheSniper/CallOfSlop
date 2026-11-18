using System.Numerics;

namespace CallOfSlop;

/// <summary>What a far group is for (faction-controllers.md phase 4).</summary>
public enum FarTask : byte
{
    None,
    Take, // a key site held by a power at war with it: beat its garrison and defenders, then hold it
    Defend, // its own site under attack
    Hold, // a site it took: it stays a while as its defence
    Home, // back to its home planet, where its ships go into the hangar
}

public enum FarState : byte
{
    None,
    Gather, // its ships being fielded at home (from the hangar, or bought and built)
    Transit, // between planets, on the space clock
    Land, // on its planet, on its way to its site
    AtSite,
}

/// <summary>A group nobody sees: one record, its ships records of its power's (<see cref="PowerShip.Far"/>). Real (bodies) near a person.</summary>
public struct FarGroup
{
    public byte Power;
    public FarTask Task;
    public FarState State;
    public byte Body; // 0 a record; 1 its ships being set down; 2 real: a crew group of bodies
    public short Planet, ToPlanet, Site; // Planet -1: in transit
    public Vector3 Pos; // on the open world (PosKnown), else nothing
    public bool PosKnown;
    public double Depart, Arrive; // transit: the clock's seconds (the space clock where there is one); landing: the same
    public long StateAt, QuietSince, FarSince, LastStep, ReportedAt, SpawnAt;
    public short RealSlot;
    public ushort RealNumber, Number;
    public float Strength;
    public byte Ships;
    public Vector3 ReportPos; // where it was when news last named it (stale for a client: section 7's fog)
}

/// <summary>
/// Territory and the far layer (docs/sandbox/design/faction-controllers.md phase 4; Karl's answer 1, the user's answers 3, 4 and 8). Planets and their
/// key sites (Crew/Territory.json): a power holds a site, and a planet when its sites give it the majority of the planet's control, each site weighted by
/// its kind shared among the sites of that kind there. Garrisons (abstract: outposts as buildings are later) grow back while nobody attacks; held sites
/// earn. Each controller sends Take groups against sites of powers it is at war with, and Defend groups to its own sites under attack. Those groups are
/// records nobody sees (<see cref="FarGroup"/>): they move between planets on the space clock, land, and fight by strength at the site, a bounded share
/// lost each step (minutes, never one roll), told as news; a closed world is never opened for them. On the open world, within the materialise range of
/// a person, a group's ships are set down as crewed bodies out of sight (a crew group under an admiral), and when nobody has been within the collapse
/// range for a while they go back to records (hulls kept). Fixed arrays, a slice of groups a tick: nothing allocated in a steady tick but news and logs.
/// </summary>
public sealed partial class PowerControllers
{
    public const int MaxPlanets = 16, MaxSites = 64, MaxFar = CrewFleet.MaxGroups;

    public TerritoryRules Territory = TerritoryRules.Current;

    // Sites (the rules' order), and each planet's holder.
    public readonly byte[] SiteHolder = new byte[MaxSites];
    public readonly float[] SiteGarrison = new float[MaxSites];
    public readonly float[] SiteWeight = new float[MaxSites];
    public readonly long[] SiteEmptySince = new long[MaxSites]; // its garrison gone and an attacker at it: since then (0: not)
    public readonly long[] SiteNewsAt = new long[MaxSites];
    private readonly Vector3[] _siteWorld = new Vector3[MaxSites];
    private readonly bool[] _siteWorldOk = new bool[MaxSites];
    private string _siteWorldFor = "\0";
    public readonly byte[] PlanetHolder = new byte[MaxPlanets];
    public readonly float[] PlanetShare = new float[MaxPlanets]; // the holder's share (the leader's, when contested)
    public readonly byte[] PlanetLeader = new byte[MaxPlanets];
    public int SiteCount => Math.Min(Territory.Sites.Count, MaxSites);
    public int PlanetCount => Math.Min(Territory.Planets.Count, MaxPlanets);

    public readonly FarGroup[] Far = new FarGroup[MaxFar];
    public int FarLive;
    private int _farNext;
    private ushort _farNumber;
    public double FarMs, FarWorstMs;
    public long FarBytes;
    public int FarSteps, PlanetChanges, SitesTaken, FarFights, Materialised, Collapsed;

    /// <summary>The clock far groups move by: the host's space clock (s), else the controllers' own ticks.</summary>
    private double Now(IPowerHost host) => host.SpaceSeconds is var s and >= 0 ? s : Tick / (double)T;

    /// <summary>A new world: every site to its holder in the data, garrisons full, no far group.</summary>
    private void TerritoryReset()
    {
        Array.Clear(Far);
        FarLive = 0;
        _farNumber = 0;
        _siteWorldFor = "\0";
        for (var k = 0; k < SiteCount; k++)
        {
            var s = Territory.Sites[k];
            SiteHolder[k] = s.Holder.Length > 0 ? CrewPowers.Find(s.Holder) : (byte)0;
            SiteGarrison[k] = SiteHolder[k] != 0 ? Territory.Kinds[s.Kind].Garrison : 0;
            SiteEmptySince[k] = SiteNewsAt[k] = 0;
            _sitePlanet[k] = (short)Territory.PlanetOf(s.Planet);
            var same = 0;
            foreach (var o in Territory.Sites)
                if (o.Kind == s.Kind && o.Planet.Equals(s.Planet, StringComparison.OrdinalIgnoreCase))
                    same++;
            SiteWeight[k] = Territory.Kinds[s.Kind].Weight / Math.Max(1, same);
        }
        Holders(null, quiet: true);
        for (var a = 1; a <= N; a++)
            Powers[a].HadSites = SitesOf((byte)a) > 0;
    }

    public int SitesOf(byte a)
    {
        var n = 0;
        for (var k = 0; k < SiteCount; k++)
            if (SiteHolder[k] == a)
                n++;
        return n;
    }

    private readonly short[] _sitePlanet = new short[MaxSites];

    private int PlanetIndex(int site) => _sitePlanet[site]; // (made at TerritoryReset: a lookup by name allocates its lambda)

    private string PlanetName(int planet) => planet >= 0 && planet < PlanetCount ? Territory.Planets[planet] : "space";

    /// <summary>Each planet's holder from its sites (Karl's answer 1); a change is news and a relation event (the old holder's view of the new).</summary>
    private void Holders(PowerRelations? rel, bool quiet = false)
    {
        Span<float> share = stackalloc float[N + 1];
        for (var pl = 0; pl < PlanetCount; pl++)
        {
            share.Clear();
            float total = 0;
            for (var k = 0; k < SiteCount; k++)
            {
                if (!Territory.Sites[k].Planet.Equals(Territory.Planets[pl], StringComparison.OrdinalIgnoreCase))
                    continue;
                total += SiteWeight[k];
                share[SiteHolder[k]] += SiteWeight[k];
            }
            byte lead = 0;
            float best = 0;
            for (var a = 1; a <= N; a++)
                if (share[a] > best)
                    (best, lead) = (share[a], (byte)a);
            var s = total > 0 ? best / total : 0;
            var holder = s > Territory.Majority ? lead : (byte)0;
            var was = PlanetHolder[pl];
            (PlanetHolder[pl], PlanetShare[pl], PlanetLeader[pl]) = (holder, s, lead);
            if (quiet || holder == was)
                continue;
            PlanetChanges++;
            var planet = Territory.Planets[pl];
            if (holder == 0)
                Say($"{planet} is contested: {Name(was)} holds it no longer");
            else if (was == 0)
                Say($"{Name(holder)} holds {planet} now ({s * 100:0}% of its control)");
            else
                Say($"{planet} falls to {Name(holder)}: {Name(was)} has lost it");
            if (was != 0 && holder != 0 && rel != null)
                rel.Post(RelEvent.TakePlanet, was, holder, 255, default);
        }
    }

    /// <summary>A site's place on the open world (null: not this world, or no ground there); found once a world.</summary>
    private bool SiteWorld(IPowerHost host, int k, out Vector3 at)
    {
        if (!string.Equals(_siteWorldFor, host.WorldKey, StringComparison.Ordinal))
        {
            _siteWorldFor = host.WorldKey;
            for (var i = 0; i < SiteCount; i++)
            {
                var s = Territory.Sites[i];
                _siteWorldOk[i] = s.Planet.Equals(_siteWorldFor, StringComparison.OrdinalIgnoreCase) && host.SiteAt(s.Planet, s.At, out _siteWorld[i]);
            }
        }
        at = _siteWorld[k];
        return k >= 0 && k < SiteCount && _siteWorldOk[k];
    }

    private bool OpenWorld(IPowerHost host, int planet) => planet >= 0 && planet < PlanetCount && Territory.Planets[planet].Equals(host.WorldKey, StringComparison.OrdinalIgnoreCase);

    // ---- A controller's think: its sites, then Take and Defend -------------------------------------------------------------------

    private void FarThink(CrewWorld crew, ConstructWorld w, PowerRelations rel, IPowerHost host, Power p, byte a, NpcPowerDef def, double minutes)
    {
        if (SiteCount == 0)
            return;
        var held = 0;
        for (var k = 0; k < SiteCount; k++)
        {
            if (SiteHolder[k] != a)
                continue;
            held++;
            var kind = Territory.Kinds[Territory.Sites[k].Kind];
            if (minutes > 0)
            {
                p.Stock += kind.Income * minutes;
                p.Earned += kind.Income * minutes;
                if (SiteEmptySince[k] == 0 && !Besieged(rel, k, a))
                    SiteGarrison[k] = MathF.Min(kind.Garrison, SiteGarrison[k] + (float)(kind.Garrison * Territory.RegenPerMinute * minutes));
            }
        }
        p.HadSites |= held > 0;
        if (!p.Plan.Fields || FarArmed(a) is not { } armed)
            return;
        // Defend: its sites under attack, one group each, with what it can field.
        for (var k = 0; k < SiteCount; k++)
            if (SiteHolder[k] == a && Besieged(rel, k, a) && !FarGoing(a, k) && FarGroupsOf(a) < Territory.FarGroups)
            {
                var need = Math.Max(1, (int)MathF.Ceiling(Attackers(rel, k, a) * 0.8f / MathF.Max(1, ShipStrength(a, armed))));
                if (FarForm(rel, p, a, FarTask.Defend, k, Math.Min(need, Territory.TakeShips * 2), armed) >= 0)
                    Say($"{Name(a)} sends ships to defend its {Territory.Sites[k].Name} on {Territory.Sites[k].Planet}");
            }
        // Take: a site of a power it is at war with, worth most for what it would take to beat.
        if (Tick < p.NextTake || FarTaking(a) || FarGroupsOf(a) >= Territory.FarGroups)
            return;
        p.NextTake = Tick + (long)(Territory.TakeEveryMinutes * 60 * T);
        var best = 0f;
        var target = -1;
        var mine = ShipStrength(a, armed) * Territory.TakeShips;
        for (var k = 0; k < SiteCount; k++)
        {
            var h = SiteHolder[k];
            if (h == 0 || h == a || rel.Gone[h] || rel.BandOf(a, h) != RelBand.War || !rel.Fights(a, h))
                continue;
            var defence = SiteGarrison[k] + Defenders(rel, k, a);
            if (mine < defence * def.Caution)
                continue;
            var score = SiteWeight[k] * (0.5f + def.ValuePlanets) * (0.5f + def.Aggression) / (1 + defence);
            if (score > best)
                (best, target) = (score, k);
        }
        if (target >= 0 && FarForm(rel, p, a, FarTask.Take, target, Territory.TakeShips, armed) is var gi and >= 0)
            Log.Add($"{Name(a)}: a far Take group ({Far[gi].Ships} ships) for {Name(SiteHolder[target])}'s {Territory.Sites[target].Name} on {Territory.Sites[target].Planet}");
    }

    /// <summary>The role its far groups are made of: warships, else raiders, else escorts (null: it has none).</summary>
    private ShipRole? FarArmed(byte a)
    {
        foreach (var r in (ReadOnlySpan<ShipRole>)[ShipRole.Warship, ShipRole.Raider, ShipRole.Escort])
            if (_fleet[a][(int)r].Length > 0)
                return r;
        return null;
    }

    private float ShipStrength(byte a, ShipRole role) => _fleet[a][(int)role] is { Length: > 0 } list ? Bills[list[0]].Strength : 1;

    private int FarGroupsOf(byte a)
    {
        var n = 0;
        for (var i = 0; i < MaxFar; i++)
            if (Far[i].Power == a && Far[i].Task != FarTask.None)
                n++;
        return n;
    }

    private bool FarTaking(byte a)
    {
        for (var i = 0; i < MaxFar; i++)
            if (Far[i].Power == a && Far[i].Task == FarTask.Take)
                return true;
        return false;
    }

    private bool FarGoing(byte a, int site)
    {
        for (var i = 0; i < MaxFar; i++)
            if (Far[i].Power == a && Far[i].Site == site && Far[i].Task is FarTask.Defend or FarTask.Hold)
                return true;
        return false;
    }

    private bool AtSite(in FarGroup g, int site) => g.Task != FarTask.None && g.Site == site && g.State == FarState.AtSite;

    /// <summary>Strength at site <paramref name="k"/> of groups whose powers fight <paramref name="a"/> (attackers of a's site, or defenders against a).</summary>
    private float Against(PowerRelations rel, int k, byte a)
    {
        float s = 0;
        for (var i = 0; i < MaxFar; i++)
            if (AtSite(Far[i], k) && Far[i].Power != a && rel.Fights(Far[i].Power, a))
                s += Far[i].Strength;
        return s;
    }

    private float Attackers(PowerRelations rel, int k, byte holder) => Against(rel, k, holder);

    private float Defenders(PowerRelations rel, int k, byte attacker)
    {
        float s = 0;
        var h = SiteHolder[k];
        for (var i = 0; i < MaxFar; i++)
            if (AtSite(Far[i], k) && (Far[i].Power == h || rel.Fights(Far[i].Power, attacker) && Far[i].Power != attacker))
                s += Far[i].Strength;
        return s;
    }

    private bool Besieged(PowerRelations rel, int k, byte holder) => Attackers(rel, k, holder) > 0;

    /// <summary>A far group for <paramref name="task"/> at site <paramref name="site"/>: its ships from the hangar (repairs paid) or bought (bills paid),
    /// within its caps and stock; it gathers at home, then goes. -1: none (no free slot, nothing to field).</summary>
    public int FarForm(PowerRelations rel, Power p, byte a, FarTask task, int site, int ships, ShipRole role)
    {
        var gi = -1;
        for (var i = 0; i < MaxFar; i++)
            if (Far[i].Task == FarTask.None)
            {
                gi = i;
                break;
            }
        if (gi < 0)
            return -1;
        var n = 0;
        for (var i = 0; i < MaxShips && n < ships; i++)
        {
            ref var s = ref p.Ships[i];
            if (s.State != PowerShipState.Hangar || s.Role != role)
                continue;
            var repair = (1 - s.Hull) * Bills[s.Bp].Units * Rules.RepairShare;
            if (p.Stock < repair)
                continue;
            p.Stock -= repair;
            p.Spent += repair;
            (s.State, s.ReadyAt, s.Far, s.Mission) = (PowerShipState.Building, Tick + (long)((1 - s.Hull) * Bills[s.Bp].Seconds * T), (short)(gi + 1), -1);
            n++;
        }
        var list = _fleet[a][(int)role];
        for (var i = 0; i < MaxShips && n < ships && list.Length > 0 && p.ShipCount() < p.Plan.Ships; i++)
        {
            if (p.Ships[i].State != PowerShipState.None)
                continue;
            var bp = list[(p.Bought + n) % list.Length];
            var bill = Bills[bp];
            if (p.Stock < bill.Units)
                break;
            p.Stock -= bill.Units;
            p.Spent += bill.Units;
            p.Bought++;
            p.Ships[i] = new PowerShip { State = PowerShipState.Building, Role = role, Bp = bp, Hull = 1, ReadyAt = Tick + (long)(bill.Seconds * T), Mission = -1, Far = (short)(gi + 1) };
            n++;
        }
        if (n == 0)
            return -1;
        var home = HomePlanet(a);
        Far[gi] = new FarGroup
        {
            Power = a, Task = task, State = FarState.Gather, Planet = (short)home, ToPlanet = (short)PlanetIndex(site), Site = (short)site, StateAt = Tick, QuietSince = Tick,
            LastStep = Tick, RealSlot = -1, Number = ++_farNumber, Ships = (byte)n,
        };
        FarLive++;
        return gi;
    }

    /// <summary>Its home planet: where its first held site is (-1: none: its groups gather in space and dock where they end).</summary>
    private int HomePlanet(byte a)
    {
        for (var k = 0; k < SiteCount; k++)
            if (SiteHolder[k] == a)
                return PlanetIndex(k);
        return -1;
    }

    // ---- The far step: a slice of groups a tick ---------------------------------------------------------------------------------------

    private void FarStep(CrewWorld crew, ConstructWorld w, PowerRelations rel, IPowerHost host)
    {
        if (FarLive == 0 || SiteCount == 0)
            return;
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var b0 = GC.GetAllocatedBytesForCurrentThread();
        var e0 = News.Count + crew.Events.Count + Log.Count + rel.NewsTotal;
        _peopleN = host.People(_people);
        var now = _lastNow = Now(host);
        for (var n = 0; n < Territory.GroupsPerTick; n++)
        {
            // (the next live group; at most one pass round the pool a tick)
            var tries = 0;
            while (Far[_farNext].Task == FarTask.None && tries++ < MaxFar)
                _farNext = (_farNext + 1) % MaxFar;
            if (Far[_farNext].Task == FarTask.None)
                break;
            var gi = _farNext;
            _farNext = (_farNext + 1) % MaxFar;
            ref var g = ref Far[gi];
            var dt = (Tick - g.LastStep) / (float)T;
            g.LastStep = Tick;
            if (g.Arrive < 0)
                g.Arrive = now + (-1 - g.Arrive); // (loaded: its seconds left, on this clock)
            StepFar(crew, w, rel, host, gi, ref g, dt, now);
            FarSteps++;
            if (n + 1 >= FarLive)
                break; // (fewer groups than the slice: each once)
        }
        var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        FarMs += ms;
        if (Ticks > 600)
            FarWorstMs = Math.Max(FarWorstMs, ms);
        if (News.Count + crew.Events.Count + Log.Count + rel.NewsTotal == e0)
            FarBytes += GC.GetAllocatedBytesForCurrentThread() - b0;
    }

    private void StepFar(CrewWorld crew, ConstructWorld w, PowerRelations rel, IPowerHost host, int gi, ref FarGroup g, float dt, double now)
    {
        var p = Powers[g.Power];
        if (rel.Gone[g.Power] || p.Fallen)
        {
            FarEnd(gi, ref g, "");
            return;
        }
        // Its ships: those built are records now; its strength from what is left of each.
        var ships = 0;
        var building = 0;
        float strength = 0;
        for (var i = 0; i < MaxShips; i++)
        {
            ref var s = ref p.Ships[i];
            if (s.Far != gi + 1)
                continue;
            if (s.State == PowerShipState.Building && Tick >= s.ReadyAt)
                s.State = PowerShipState.Far;
            if (s.State == PowerShipState.Building)
                building++;
            else if (s.State is PowerShipState.Far or PowerShipState.Spawning or PowerShipState.Out)
            {
                ships++;
                strength += Bills[s.Bp].Strength * Math.Clamp(s.State == PowerShipState.Out && s.Body != 0 && crew.ShipOf(s.Body) is { } cs ? cs.Hull : s.Hull, 0, 1);
            }
        }
        (g.Ships, g.Strength) = ((byte)ships, strength);
        if (g.Body != 0)
        {
            StepReal(crew, w, rel, host, gi, ref g, dt, ships);
            return;
        }
        if (ships == 0 && building == 0)
        {
            if (g.State != FarState.Gather)
                Say($"{Name(g.Power)}'s group {g.Number} is wiped out{(g.Site >= 0 ? $" at {Territory.Sites[g.Site].Name} on {Territory.Sites[g.Site].Planet}" : "")}");
            FarEnd(gi, ref g, "");
            return;
        }
        switch (g.State)
        {
            case FarState.Gather:
                if (building > 0 && Tick - g.StateAt < (long)(Territory.GatherMinutes * 60 * T))
                    return;
                if (ships == 0)
                {
                    FarEnd(gi, ref g, "");
                    return;
                }
                Go(host, ref g, g.ToPlanet, now);
                return;
            case FarState.Transit:
                if (now < g.Arrive)
                    return;
                g.Planet = g.ToPlanet;
                Land(host, ref g, now);
                return;
            case FarState.Land:
                if (g.Task == FarTask.Home && g.Site < 0)
                {
                    FarDock(gi, ref g);
                    return;
                }
                if (OpenWorld(host, g.Planet) && g.Site >= 0 && SiteWorld(host, g.Site, out var to))
                {
                    if (!g.PosKnown)
                        (g.Pos, g.PosKnown) = (to, true);
                    var d = to - g.Pos;
                    var len = d.Length();
                    var step = Territory.GroundSpeed * dt;
                    g.Pos = len <= step ? to : g.Pos + d / len * step;
                    if (len <= Territory.SiteReach)
                        Arrive(ref g);
                }
                else if (now >= g.Arrive)
                    Arrive(ref g);
                Materialise(crew, w, host, gi, ref g);
                return;
            case FarState.AtSite:
                AtSiteStep(crew, w, rel, host, gi, ref g, p, dt, now);
                if (g.Task != FarTask.None && g.Body == 0)
                    Materialise(crew, w, host, gi, ref g);
                return;
        }
    }

    /// <summary>Off to a planet: transit on the space clock when it is another, else straight to its site.</summary>
    private void Go(IPowerHost host, ref FarGroup g, int planet, double now)
    {
        g.ToPlanet = (short)planet;
        if (planet < 0 || g.Planet == planet)
        {
            g.Planet = (short)planet;
            Land(host, ref g, now);
            return;
        }
        var gap = g.Planet >= 0 ? host.PlanetGap(Territory.Planets[g.Planet], Territory.Planets[planet]) : -1;
        g.Depart = now;
        g.Arrive = now + (gap > 0 ? gap / Territory.SpaceSpeed : Territory.TransitSeconds);
        g.Planet = -1;
        g.PosKnown = false;
        (g.State, g.StateAt) = (FarState.Transit, Tick);
    }

    private void Land(IPowerHost host, ref FarGroup g, double now)
    {
        (g.State, g.StateAt) = (FarState.Land, Tick);
        g.Depart = now;
        g.Arrive = now + Territory.LandSeconds;
        if (OpenWorld(host, g.Planet) && g.Site >= 0 && SiteWorld(host, g.Site, out var site) && !g.PosKnown)
        {
            // (landing on the open world: it comes down a way off its site, out of the way of anyone there, and goes on by ground)
            var east = Vector3.Normalize(Vector3.Cross(GravityFrames.Curved ? GravityFrames.UpAt(site) : Vector3.UnitY, Vector3.UnitZ) + new Vector3(1e-4f, 0, 0));
            (g.Pos, g.PosKnown) = (site + east * (Territory.SiteReach + Territory.GroundSpeed * Territory.LandSeconds), true);
            if (host.Ground(g.Pos, out var ground))
                g.Pos = ground;
        }
    }

    private void Arrive(ref FarGroup g)
    {
        (g.State, g.StateAt, g.QuietSince) = (FarState.AtSite, Tick, Tick);
    }

    /// <summary>At its site: fights by strength with what fights it there (the garrison too, when its holder fights the group), takes the site when
    /// nothing is left to hold it, holds or goes home when its work is done or it is beaten.</summary>
    private void AtSiteStep(CrewWorld crew, ConstructWorld w, PowerRelations rel, IPowerHost host, int gi, ref FarGroup g, Power p, float dt, double now)
    {
        var k = g.Site;
        var a = g.Power;
        var def = CrewPowers.Of(a)!;
        var holder = SiteHolder[k];
        var enemy = Against(rel, k, a);
        var garrison = holder != 0 && holder != a && rel.Fights(holder, a) ? SiteGarrison[k] : 0;
        if (enemy + garrison > 0)
        {
            g.QuietSince = Tick;
            if (g.Body == 0)
            {
                // Abstract: a bounded share lost a step to what it faces (real ships take real hits instead).
                var loss = MathF.Min(Territory.FightRate * dt * (enemy + garrison), Territory.FightCap * dt * MathF.Max(0.1f, g.Strength));
                FarDamage(rel, gi, ref g, loss, Strongest(rel, k, a, holder));
                FarFights++;
            }
            if (garrison > 0)
            {
                SiteGarrison[k] = MathF.Max(0, SiteGarrison[k] - Territory.FightRate * dt * g.Strength);
                if (SiteNewsAt[k] == 0 || Tick - SiteNewsAt[k] > 10L * 60 * T)
                {
                    SiteNewsAt[k] = Tick;
                    (g.ReportedAt, g.ReportPos) = (Tick, g.Pos);
                    Say($"{Name(a)} attacks {Name(holder)}'s {Territory.Sites[k].Name} on {Territory.Sites[k].Planet}");
                }
            }
            // Beaten: what it faces is past what its caution allows it to stand.
            if (g.Strength < (enemy + garrison) * def.Caution * 0.5f && g.Task is FarTask.Take or FarTask.Defend)
            {
                Say($"{Name(a)}'s group {g.Number} pulls back from {Territory.Sites[k].Name} on {Territory.Sites[k].Planet}");
                Home(host, ref g, now);
                return;
            }
        }
        // Taken: no garrison and no defender left, an attacker standing there for the take time (a holder it no longer fights: it goes home).
        if (g.Task == FarTask.Take && holder != 0 && holder != a && !rel.Fights(holder, a))
        {
            Home(host, ref g, now);
            return;
        }
        if (g.Task == FarTask.Take && holder != a && garrison <= 0 && enemy <= 0)
        {
            if (SiteEmptySince[k] == 0)
                SiteEmptySince[k] = Tick;
            else if (Tick - SiteEmptySince[k] >= (long)(Territory.TakeSeconds * T))
                TakeSite(rel, k, a, ref g);
            return;
        }
        if (g.Task == FarTask.Take && holder == a)
            (g.Task, g.StateAt) = (FarTask.Hold, Tick);
        var quiet = (Tick - g.QuietSince) / (float)T;
        if (g.Task == FarTask.Defend && quiet > Rules.QuietSeconds * 3)
            Home(host, ref g, now);
        else if (g.Task == FarTask.Hold && (holder != a || Tick - g.StateAt > (long)(Territory.HoldMinutes * 60 * T) && SiteGarrison[k] >= Territory.Kinds[Territory.Sites[k].Kind].Garrison * 0.5f && quiet > 60))
            Home(host, ref g, now);
    }

    private byte Strongest(PowerRelations rel, int k, byte a, byte holder)
    {
        byte best = holder != 0 && rel.Fights(holder, a) ? holder : (byte)0;
        float most = best != 0 ? SiteGarrison[k] : 0;
        for (var i = 0; i < MaxFar; i++)
            if (AtSite(Far[i], k) && Far[i].Power != a && rel.Fights(Far[i].Power, a) && Far[i].Strength > most)
                (most, best) = (Far[i].Strength, Far[i].Power);
        return best;
    }

    private void TakeSite(PowerRelations rel, int k, byte a, ref FarGroup g)
    {
        var was = SiteHolder[k];
        SiteHolder[k] = a;
        SiteGarrison[k] = Territory.Kinds[Territory.Sites[k].Kind].Garrison * 0.25f;
        SiteEmptySince[k] = 0;
        SitesTaken++;
        Powers[a].HadSites = true;
        (g.Task, g.StateAt, g.ReportedAt, g.ReportPos) = (FarTask.Hold, Tick, Tick, g.Pos);
        Say($"{Name(a)} takes {Territory.Sites[k].Name} on {Territory.Sites[k].Planet}{(was != 0 ? $" from {Name(was)}" : "")}");
        if (was != 0)
            rel.Post(RelEvent.TakeSite, was, a, 255, default);
        Holders(rel);
    }

    /// <summary>A loss in strength spread over its ships in turn: a ship whose hull runs out is lost (a relation event against <paramref name="doer"/>).</summary>
    private void FarDamage(PowerRelations rel, int gi, ref FarGroup g, float loss, byte doer)
    {
        var p = Powers[g.Power];
        for (var i = 0; i < MaxShips && loss > 0; i++)
        {
            ref var s = ref p.Ships[i];
            if (s.Far != gi + 1 || s.State != PowerShipState.Far)
                continue;
            var full = Bills[s.Bp].Strength;
            var take = MathF.Min(loss, s.Hull * full);
            s.Hull -= take / full;
            loss -= take;
            if (s.Hull <= 0.02f)
            {
                s = default;
                s.Mission = -1;
                p.Lost++;
                if (doer != 0)
                    rel.Post(RelEvent.DestroyShip, g.Power, doer, 255, default);
            }
        }
    }

    /// <summary>Home: to its home planet; there its ships go into the hangar.</summary>
    private void Home(IPowerHost host, ref FarGroup g, double now)
    {
        g.Task = FarTask.Home;
        g.Site = -1;
        var home = HomePlanet(g.Power);
        if (g.Body != 0)
            return; // (bodies: they collapse first, when nobody is near; then they go)
        if (home < 0 || home == g.Planet)
        {
            g.Planet = (short)Math.Max(home, g.Planet);
            (g.State, g.StateAt) = (FarState.Land, Tick);
            return;
        }
        Go(host, ref g, home, now);
    }

    private void FarDock(int gi, ref FarGroup g)
    {
        var p = Powers[g.Power];
        for (var i = 0; i < MaxShips; i++)
        {
            ref var s = ref p.Ships[i];
            if (s.Far != gi + 1)
                continue;
            if (s.State == PowerShipState.Far)
                (s.State, s.Far) = (PowerShipState.Hangar, 0);
            else if (s.State == PowerShipState.Building)
                (s.State, s.Far) = (PowerShipState.Hangar, 0); // (a repair or a build never finished: it waits in the hangar)
        }
        FarEnd(gi, ref g, "");
    }

    private void FarEnd(int gi, ref FarGroup g, string why)
    {
        var p = Powers[g.Power];
        for (var i = 0; i < MaxShips; i++)
            if (p.Ships[i].Far == gi + 1)
            {
                if (p.Ships[i].State is PowerShipState.Far or PowerShipState.Building)
                    p.Ships[i].State = PowerShipState.Hangar;
                p.Ships[i].Far = 0;
            }
        if (why.Length > 0)
            Log.Add(why);
        g = default;
        g.RealSlot = -1;
        FarLive = Math.Max(0, FarLive - 1);
    }

    /// <summary>A power merged into another: its far groups end (their ships go to the heir with its hangar), its sites go over.</summary>
    private void FarMerged(byte a, byte heir)
    {
        for (var i = 0; i < MaxFar; i++)
            if (Far[i].Power == a && Far[i].Task != FarTask.None)
            {
                Far[i] = default;
                Far[i].RealSlot = -1;
                FarLive = Math.Max(0, FarLive - 1);
            }
        var any = false;
        for (var k = 0; k < SiteCount; k++)
            if (SiteHolder[k] == a)
                (SiteHolder[k], any) = (heir, true);
        if (any)
            Holders(null);
    }

    // ---- Bodies near a person: materialising and collapsing ----------------------------------------------------------------------------

    private float NearestPersonTo(Vector3 at)
    {
        var best = float.MaxValue;
        for (var k = 0; k < _peopleN; k++)
            best = MathF.Min(best, Vector3.Distance(_people[k], at));
        return best;
    }

    /// <summary>On the open world within the materialise range of a person: its ships are set down as crewed bodies out of sight (two a step),
    /// within the world's cap on the controllers' bodies; over the cap it stays a record (and nobody sees it).</summary>
    private void Materialise(CrewWorld crew, ConstructWorld w, IPowerHost host, int gi, ref FarGroup g)
    {
        if (_peopleN == 0 || !g.PosKnown || !OpenWorld(host, g.Planet) || NearestPersonTo(g.Pos) > Territory.MaterialiseRange)
            return;
        var p = Powers[g.Power];
        var records = 0;
        for (var i = 0; i < MaxShips; i++)
            if (p.Ships[i].Far == gi + 1 && p.Ships[i].State == PowerShipState.Far)
                records++;
        if (records == 0 || Bodies() + records > Rules.MaxBodies)
            return; // (nothing built yet, or over the world's cap: it stays a record, unseen)
        g.Body = 1;
        g.SpawnAt = Tick;
        g.FarSince = 0;
        Materialised++;
        crew.Events.Add($"powers: {Name(g.Power)}'s group {g.Number} materialises: a person is {NearestPersonTo(g.Pos):0} m from it");
        SpawnSome(host, gi, ref g, p);
    }

    private void SpawnSome(IPowerHost host, int gi, ref FarGroup g, Power p)
    {
        var put = 0;
        for (var i = 0; i < MaxShips && put < 2; i++)
        {
            ref var s = ref p.Ships[i];
            if (s.Far != gi + 1 || s.State != PowerShipState.Far)
                continue;
            var at = g.Pos + new Vector3((i % 4 - 1.5f) * 16, 0, (i / 4 % 4 - 1.5f) * 20);
            // (out of everyone's sight: pushed away from the nearest person to the spawn distance)
            for (var k = 0; k < _peopleN; k++)
            {
                var d = at - _people[k];
                var len = d.Length();
                if (len < Territory.SpawnFromPeople)
                    at = _people[k] + (len > 1 ? d / len : Vector3.UnitX) * Territory.SpawnFromPeople;
            }
            if (host.Ground(at, out var ground))
                at = ground;
            s.State = PowerShipState.Spawning;
            put++;
            if (!host.Spawn(g.Power, i, Blueprints[s.Bp], at, i * 0.7f % MathF.Tau))
            {
                s.State = PowerShipState.Far;
                break;
            }
        }
    }

    /// <summary>Real: set down (its crew group formed when all are out), its site's work by its flagship's place, and its collapse when nobody is near.</summary>
    private void StepReal(CrewWorld crew, ConstructWorld w, PowerRelations rel, IPowerHost host, int gi, ref FarGroup g, float dt, int ships)
    {
        var p = Powers[g.Power];
        int far = 0, spawning = 0, out_ = 0;
        float nearest = float.MaxValue;
        for (var i = 0; i < MaxShips; i++)
        {
            ref var s = ref p.Ships[i];
            if (s.Far != gi + 1)
                continue;
            if (s.State == PowerShipState.Out)
            {
                if (s.Body == 0 && FarAdopt(crew, w, g.Power, ref s))
                    continue; // (after a load: its body taken up again, or dropped)
                if (s.Body == 0)
                    continue;
                if (w.Get(s.Body) is not { Wreck: false } c || crew.ShipOf(s.Body) is not { } cs || cs.Prize || cs.Own || cs.Faction != g.Power)
                {
                    crew.Events.Add($"powers: {Name(g.Power)}'s group {g.Number} lost its {Blueprints[s.Bp].Name}");
                    p.Lost++;
                    s = default;
                    s.Mission = -1;
                    continue;
                }
                s.Uid = UidOf(crew, cs);
                out_++;
                nearest = MathF.Min(nearest, NearestPersonTo(c.WorldCom));
            }
            else if (s.State == PowerShipState.Spawning)
                spawning++;
            else if (s.State == PowerShipState.Far)
                far++;
        }
        if (g.Body == 1)
        {
            if (far > 0 && Tick - g.SpawnAt < 120L * T)
            {
                SpawnSome(host, gi, ref g, p);
                return;
            }
            if (spawning > 0 && Tick - g.SpawnAt < 120L * T)
                return;
            if (out_ == 0)
            {
                g.Body = 0; // (nothing could be set down: a record again)
                return;
            }
            var n = 0;
            for (var i = 0; i < MaxShips && n < _ids.Length; i++)
                if (p.Ships[i].Far == gi + 1 && p.Ships[i].State == PowerShipState.Out && p.Ships[i].Body != 0)
                    _ids[n++] = p.Ships[i].Body;
            var slot = crew.Fleet.Form(crew, w, g.Task == FarTask.Take ? GroupTask.Strike : GroupTask.Defend, _ids.AsSpan(0, n), $"{Name(g.Power)}'s group {g.Number}");
            if (slot < 0)
                return;
            (g.Body, g.RealSlot, g.RealNumber) = (2, (short)slot, crew.Fleet.Groups[slot].Number);
            if (g.Site >= 0 && SiteWorld(host, g.Site, out var to))
                crew.Fleet.Command(slot, OrderKind.GoTo, to);
            else
                crew.Fleet.Command(slot, OrderKind.Hold);
            return;
        }
        // Real: its flagship is where the group is; ships that came late (set down after it formed) join it.
        if (g.RealSlot < 0 || crew.Fleet.Of(g.RealSlot) is not { } grp || grp.Number != g.RealNumber)
        {
            if (out_ > 0 && FarRelink(crew, gi, ref g, p))
                return;
            if (out_ == 0 && far == 0 && spawning == 0)
            {
                Say($"{Name(g.Power)}'s group {g.Number} is destroyed{(g.Site >= 0 ? $" at {Territory.Sites[g.Site].Name}" : "")}");
                FarEnd(gi, ref g, "");
                return;
            }
            if (out_ == 0)
            {
                g.Body = 0;
                return;
            }
            grp = null;
        }
        if (grp != null && w.Get(grp.Flagship) is { } flag)
            g.Pos = flag.WorldCom;
        if (g.Site >= 0 && SiteWorld(host, g.Site, out var site))
        {
            if (Vector3.Distance(g.Pos, site) <= Territory.SiteReach)
            {
                if (g.State != FarState.AtSite)
                    Arrive(ref g);
                AtSiteStep(crew, w, rel, host, gi, ref g, p, dt, Now(host));
            }
            else if (g.State == FarState.AtSite)
                g.State = FarState.Land;
        }
        // Collapse: nobody within the collapse range for long enough: one ship back to a record a step.
        if (nearest < Territory.CollapseRange && _peopleN > 0)
        {
            g.FarSince = 0;
            return;
        }
        if (g.FarSince == 0)
        {
            g.FarSince = Tick;
            return;
        }
        if (Tick - g.FarSince < (long)(Territory.CollapseSeconds * T) || _dockedAt == Tick)
            return;
        for (var i = 0; i < MaxShips; i++)
        {
            ref var s = ref p.Ships[i];
            if (s.Far != gi + 1 || s.State != PowerShipState.Out || s.Body == 0 || w.Get(s.Body) is not { } c || crew.ShipOf(s.Body) is not { } cs)
                continue;
            _dockedAt = Tick;
            if (cs.Group >= 0)
                crew.Fleet.Leave(crew, cs);
            s.Hull = Math.Clamp(cs.Hull, 0.05f, 1);
            host.Dock(c);
            (s.State, s.Body) = (PowerShipState.Far, 0);
            out_--;
            break;
        }
        if (out_ <= 0 && spawning == 0)
        {
            (g.Body, g.RealSlot) = (0, -1);
            Collapsed++;
            crew.Events.Add($"powers: {Name(g.Power)}'s group {g.Number} collapses to a record: nobody within {Territory.CollapseRange:0} m");
            if (g.Task == FarTask.Home)
                Home(host, ref g, Now(host));
        }
    }

    /// <summary>After a load: a real group's crew group found again by the group its first body's crew is in.</summary>
    private bool FarRelink(CrewWorld crew, int gi, ref FarGroup g, Power p)
    {
        for (var i = 0; i < MaxShips; i++)
            if (p.Ships[i].Far == gi + 1 && p.Ships[i].State == PowerShipState.Out && p.Ships[i].Body != 0 && crew.ShipOf(p.Ships[i].Body) is { Group: >= 0 } cs
                && crew.Fleet.Of(cs.Group) is { } grp && grp.Power == g.Power)
            {
                (g.RealSlot, g.RealNumber) = ((short)grp.Slot, grp.Number);
                return true;
            }
        return false;
    }

    /// <summary>After a load: an out ship of a far group takes up the body whose crew holds its saved uid (any group); none: it is dropped.</summary>
    private bool FarAdopt(CrewWorld crew, ConstructWorld w, byte a, ref PowerShip s)
    {
        for (var k = 0; k < crew.ShipHigh && s.Uid != 0; k++)
        {
            if (crew.Ships[k] is not { Own: false, Prize: false } cs || cs.Faction != a || w.Get(cs.Body) is not { Wreck: false })
                continue;
            for (var j = 0; j < cs.MemberCount; j++)
                if (crew.Uid[cs.Members[j]] == s.Uid)
                {
                    s.Body = cs.Body;
                    return false;
                }
        }
        Log.Add($"{Name(a)}'s saved {Blueprints[s.Bp].Name} of a far group has no body now: dropped");
        s = default;
        s.Mission = -1;
        return true;
    }

    /// <summary>A test's (the window tool's <c>crewterritory near</c>): a Take group of the first power that fields armed ships, set on the open world
    /// <paramref name="from"/> m from <paramref name="at"/>, for a site on that world (any, else none). -1: none could be made.</summary>
    public int FarNear(PowerRelations rel, IPowerHost host, Vector3 at, float from)
    {
        var planet = Territory.PlanetOf(host.WorldKey);
        if (planet < 0)
            return -1;
        for (var a = (byte)1; a <= Count; a++)
        {
            if (FarArmed(a) is not { } role || !Powers[a].Plan.Fields || rel.Gone[a])
                continue;
            var site = -1;
            for (var k = 0; k < SiteCount && site < 0; k++)
                if (_sitePlanet[k] == planet && SiteHolder[k] != a)
                    site = k;
            if (site < 0)
                continue;
            Powers[a].Stock = Math.Max(Powers[a].Stock, 3000); // (a test's: it can field them)
            var gi = FarForm(rel, Powers[a], a, FarTask.Take, site, 2, role);
            if (gi < 0)
                continue;
            ref var g = ref Far[gi];
            var p = at + new Vector3(from, 0, 0);
            if (host.Ground(p, out var ground))
                p = ground;
            (g.Planet, g.ToPlanet, g.State, g.StateAt, g.Pos, g.PosKnown, g.Arrive) = ((short)planet, (short)planet, FarState.Land, Tick, p, true, double.MaxValue);
            return gi;
        }
        return -1;
    }

    // ---- Saves (in the controllers' section, version 2) -----------------------------------------------------------------------------

    /// <summary>u8 sites (text planet, text name, text holder, f32 garrison); u16 far groups (u16 slot, text power, u8 task, u8 state, u8 body, text planet,
    /// text to-planet, text site, f64 seconds to its arrival, 3 x f32 place, u8 place known, u16 number, f32 strength). Names, so the data may change under it.</summary>
    private void TerritorySave(SaveWriter w)
    {
        w.U8((byte)SiteCount);
        for (var k = 0; k < SiteCount; k++)
            w.Str(Territory.Sites[k].Planet).Str(Territory.Sites[k].Name).Str(SiteHolder[k] != 0 ? Name(SiteHolder[k]) : "").F32(SiteGarrison[k]);
        var n = 0;
        for (var i = 0; i < MaxFar; i++)
            if (Far[i].Task != FarTask.None)
                n++;
        w.U16((ushort)n);
        var now = _lastNow;
        for (var i = 0; i < MaxFar; i++)
        {
            ref var g = ref Far[i];
            if (g.Task == FarTask.None)
                continue;
            w.U16((ushort)i).Str(Name(g.Power)).U8((byte)g.Task).U8((byte)g.State).U8((byte)(g.Body == 2 ? 2 : 0)).Str(g.Planet >= 0 ? PlanetName(g.Planet) : "")
                .Str(g.ToPlanet >= 0 ? PlanetName(g.ToPlanet) : "").Str(g.Site >= 0 && g.Site < SiteCount ? Territory.Sites[g.Site].Name : "").F64(g.Arrive < 0 ? -1 - g.Arrive : Math.Max(0, g.Arrive - now))
                .F32(g.Pos.X).F32(g.Pos.Y).F32(g.Pos.Z).U8((byte)(g.PosKnown ? 1 : 0)).U16(g.Number).F32(g.Strength);
        }
    }

    private double _lastNow;

    private void TerritoryLoad(SaveReader r)
    {
        int sites = r.U8();
        for (var i = 0; i < sites; i++)
        {
            var planet = r.Str();
            var name = r.Str();
            var holder = r.Str();
            var garrison = r.F32();
            var k = Territory.Sites.FindIndex(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && x.Planet.Equals(planet, StringComparison.OrdinalIgnoreCase));
            if (k < 0 || k >= SiteCount)
            {
                Log.Add($"the world's territory names the site {name} on {planet}, which isn't in the data now: left out");
                continue;
            }
            SiteHolder[k] = holder.Length > 0 ? CrewPowers.Find(holder) : (byte)0;
            SiteGarrison[k] = garrison;
        }
        int n = r.U16();
        for (var i = 0; i < n; i++)
        {
            int slot = r.U16();
            var power = CrewPowers.Find(r.Str());
            var g = new FarGroup { Task = (FarTask)r.U8(), State = (FarState)r.U8(), Body = r.U8(), RealSlot = -1, LastStep = Tick, StateAt = Tick, QuietSince = Tick };
            g.Planet = (short)Territory.PlanetOf(r.Str());
            g.ToPlanet = (short)Territory.PlanetOf(r.Str());
            var site = r.Str();
            g.Site = (short)Territory.Sites.FindIndex(x => x.Name.Equals(site, StringComparison.OrdinalIgnoreCase));
            g.Arrive = r.F64(); // (seconds left: made a time on the clock at its first step: FarTidy keeps it relative)
            g.Pos = new Vector3(r.F32(), r.F32(), r.F32());
            g.PosKnown = r.U8() != 0;
            g.Number = r.U16();
            g.Strength = r.F32();
            g.Power = power;
            _farNumber = Math.Max(_farNumber, g.Number);
            if (slot >= MaxFar || power == 0 || g.Task is FarTask.None or > FarTask.Home || g.State > FarState.AtSite || g.Site >= SiteCount)
            {
                Log.Add($"a saved far group can't be kept (its power, task or site isn't read now): left out");
                continue;
            }
            g.Arrive = -1 - g.Arrive; // (negative: seconds left, to be put on the clock at its first step)
            Far[slot] = g;
            FarLive++;
        }
        Holders(null, quiet: true);
    }

    /// <summary>After a load: a ship of a far group that wasn't read goes into its hangar.</summary>
    private void FarTidy()
    {
        for (var a = 1; a <= N; a++)
            for (var i = 0; i < MaxShips; i++)
            {
                ref var s = ref Powers[a].Ships[i];
                if (s.Far == 0 || s.Far - 1 < MaxFar && Far[s.Far - 1].Task != FarTask.None && Far[s.Far - 1].Power == a)
                    continue;
                if (s.State is PowerShipState.Far)
                    s.State = PowerShipState.Hangar;
                s.Far = 0;
            }
    }

    // ---- What the window, the news and the command view read ---------------------------------------------------------------------

    public string TerritoryText()
    {
        var sb = new System.Text.StringBuilder();
        for (var pl = 0; pl < PlanetCount; pl++)
        {
            sb.Append($"  {Territory.Planets[pl],-9} {(PlanetHolder[pl] != 0 ? $"held by {Name(PlanetHolder[pl])}" : "contested")} ({(PlanetLeader[pl] != 0 ? Name(PlanetLeader[pl]) : "nobody")} {PlanetShare[pl] * 100:0}%):");
            for (var k = 0; k < SiteCount; k++)
                if (PlanetIndex(k) == pl)
                    sb.Append($" {Territory.Kinds[Territory.Sites[k].Kind].Name} {(SiteHolder[k] != 0 ? CrewPowers.Of(SiteHolder[k])?.Tag is { Length: > 0 } t ? t : Name(SiteHolder[k]) : "-")} g{SiteGarrison[k]:0.0};");
            sb.AppendLine();
        }
        var shown = 0;
        for (var i = 0; i < MaxFar && shown < 24; i++)
            if (Far[i].Task != FarTask.None)
            {
                ref var g = ref Far[i];
                shown++;
                sb.AppendLine($"  far group {g.Number} of {Name(g.Power)}: {g.Task} {g.State}{(g.Body == 2 ? " REAL" : g.Body == 1 ? " setting down" : "")}, {g.Ships} ships, strength {g.Strength:0.0}, {(g.Planet >= 0 ? Territory.Planets[g.Planet] : $"in transit to {PlanetName(g.ToPlanet)}")}{(g.Site >= 0 ? $", for {Territory.Sites[g.Site].Name}" : "")}");
            }
        return sb.ToString();
    }
}
