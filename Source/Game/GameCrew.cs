using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The game's side of the crews (docs/sandbox/design/bot-brain.md; the crew itself is Source/Crew/CrewWorld.cs): bot bodies for crew
/// near people, what a crew fights, and the stand-ins clients draw for crew with no body (<see cref="Msg.CrewSeats"/>). Replaces the
/// bodiless ship AI crew (Karl's ShipAi drivers now work only manned seats). Server, except the stand-ins' table.
/// </summary>
public sealed partial class Game : ICrewHost
{
    /// <summary>The world's crews (server).</summary>
    public readonly CrewWorld Crew = new();

    private readonly Vector3[] _crewPeople = new Vector3[MaxPlayers];
    private readonly ushort[] _crewParts = new ushort[CrewWorld.MaxCrewPerBody];
    private int _crewSendAt;

    /// <summary>A tick, after the people's seats and before the constructs step: the crews think, man their seats and drive.</summary>
    private void SandboxCrewTick()
    {
        var n = 0;
        foreach (var p in Players.Values)
            if (p.Alive && p.Bot == null)
                _crewPeople[n++] = p.Position;
        CrewFactionsTick();
        Crew.Step(Constructs, World.Query, this, _crewPeople.AsSpan(0, n));
        CrewFleetTick(); // (groups' news, questioning: GameCrewFleet.cs)
        FleetPlayersTick(); // (players' groups: their commanders on the net, what each peer hears: GameCrewFleetPlayers.cs)
        CrewPowersTick(); // (the faction controllers: GameCrewPowers.cs)
        if (Crew.Events.Count > 0)
            CrewLog();
        CrewWorkTick();
        CrewMineTick(); // (miners off their posts: GameCrewMine.cs)
        CrewSendSeats();
        CrewHireTick();
        CrewRosterSend();
    }

    /// <summary>
    /// A nobody's ground vehicle or walker just set down (a faction vehicle, a convoy's member) gets a crew in its seats (bot-brain.md phase 2:
    /// it moves only with a driver aboard). Aircraft and ships are crewed by their pilots and stabilizers; a player's vehicle never is.
    /// </summary>
    public void CrewFactionVehicle(Construct c, byte power = 0)
    {
        if (!IsServer || c.Owner != 255 || c.Wreck || c.StabCount > 0 || c.DriveSeat < 0 && !CrewWorks(c) || Crew.Has(c.Id))
            return;
        Crew.Crew(c, ShipAiMode.Strangers, faction: power != 0 ? power : CrewFactionFor(c)); // (a power's own ship: its power, GameCrewPowers.cs)
        CrewFleetVehicle(c); // (a convoy's member: its group under an admiral, GameCrewFleet.cs)
        CrewLog();
    }

    /// <summary>A body with a machine a work crew works (a lifter, a cargo dock, a Building Station: phase 7).</summary>
    private static bool CrewWorks(Construct c)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (CrewWorld.WorkKind(c.Parts[i].D) != CrewStationKind.Rest)
                return true;
        return false;
    }

    /// <summary>A nobody's generated craft's power: the first made from the kind its style belongs to, else from Raiders.</summary>
    public static byte CrewFactionFor(Construct c) => c.Brief is { } b && CrewPowers.ForStyle(b.Style) is var f and > 0 ? f : CrewPowers.ForKindName("Raiders");

    private void CrewLog()
    {
        foreach (var e in Crew.Events)
            Console.WriteLine($"[crew] {e}");
        Crew.Events.Clear();
    }

    // ---- Bodies ---------------------------------------------------------------------------------------------------------------------

    byte ICrewHost.Promote(CrewWorld crew, int m, Construct c, int part, Vector3? stand)
    {
        if (SandboxBotCount() + crew.Bodied >= Math.Clamp(SandboxBotCap, 0, SpawnBotCapMax))
            return 0; // (the spawned bots' cap is shared: bot-brain.md section 4)
        if (!CrewGroundUnder(c))
            return 0; // (a parked body with no ground the server knows under it: its crew stay records)
        var id = FreeNpcId();
        if (id == 0)
            return 0;
        var ship = crew.ShipOf(c.Id);
        var bot = new Player
        {
            Id = id,
            Name = $"[{(CrewPowers.Of(ship?.Faction ?? 0)?.Name ?? "CREW")}] {CrewNames.Of(crew.Name[m])}",
            Bot = new BotBrain(BotDifficulty.Normal, BotPersonality.Tactician, (int)crew.Uid[m]),
            Team = CrewTeam(c, ship),
            CrewSlot = m,
        };
        Players[id] = bot;
        Spawn(bot, ServerTick, SandboxNow);
        if (stand is { } at)
            CrewStandAt(bot, c, at); // (an engineer or a loader stands at its place)
        else if (!CrewSit(bot, c, part))
        {
            Players.Remove(id);
            OnModePlayerLeave(bot);
            return 0;
        }
        bot.Health = Math.Max(1, (int)(World.MaxHealth * crew.Health[m] / CrewWorld.MemberHealth));
        _sbRosterDirty = true; // (one roster for a tick's bodies: SandboxSpawnTick sends it)
        return id;
    }

    /// <summary>
    /// Whether crew may be bodied on <paramref name="c"/>: a body in motion carries its riders in its frame wherever it is; a parked one (asleep,
    /// anchored, still) only where the server's world has ground or a static under it within 60 m (a body never comes to life over nothing).
    /// </summary>
    private bool CrewGroundUnder(Construct c)
    {
        if (!c.Asleep && !c.Anchored && c.Vel.LengthSquared() > 0.25f)
            return true;
        var com = c.WorldCom;
        if (ConstructWorld.FellOut(com, World.Query))
            return false;
        var down = -GravityFrames.UpAt(com);
        return World.Query.Raycast(com, down, c.Radius + 60f, new QueryFilter(QueryLayers.Static), out _);
    }

    /// <summary>A crew body's side: a hired crew (<see cref="CrewShip.Own"/>) and an owned ship's Fights strangers crew are on the owner's (friendly bots), the rest enemy bots.</summary>
    private static byte CrewTeam(Construct c, CrewShip? s) =>
        s != null && (s.Own || s.Mode == ShipAiMode.Strangers && c.Owner != 255 && s.Faction == 0) ? SbFriendlyTeam : SbHostileTeam;

    /// <summary>After a crew's side changes (a prize's roster hired), its bodies take the new side.</summary>
    private void CrewTeamsFollow(CrewShip s)
    {
        if (Constructs.Get(s.Body) is not { } c)
            return;
        var team = CrewTeam(c, s);
        for (var i = 0; i < s.MemberCount; i++)
            if (Crew.Player[s.Members[i]] is var b and not 0 && Players.TryGetValue((byte)(b - 1), out var p))
                p.Team = team;
    }

    void ICrewHost.Demote(byte player)
    {
        if (!Players.TryGetValue(player, out var p) || p.CrewSlot < 0)
            return;
        if (p.InShell)
            ShellReturn(p, lost: false, ""); // (a miner in a Mining Shell: back to the chair first, the shell left standing: GameCrewMine.cs)
        if (SandboxHands.Hand(p.Id).Seated)
            SandboxHands.Stand(Constructs, p.Id);
        SandboxHands.Hand(p.Id).Edit = EditKind.None;
        Players.Remove(p.Id);
        OnModePlayerLeave(p);
        _sbRosterDirty = true;
    }

    bool ICrewHost.Alive(byte player) => Players.TryGetValue(player, out var p) && p.Alive;

    float ICrewHost.HealthOf(byte player) => Players.TryGetValue(player, out var p) && p.Alive ? (float)p.Health / World.MaxHealth : -1;

    /// <summary>A player uses a seat a crew body sits in: the member stands up and finds another place (a person always gets the seat). True: it is free.</summary>
    private bool CrewYield(Construct c, int part)
    {
        var who = c.Parts[part].SitterPlus1 != 0 ? c.Parts[part].SitterPlus1 - 1 : part == c.DriveSeat ? c.Driver : 255;
        if (who == 255 || !Crew.IsBody[who] || !Players.TryGetValue((byte)who, out var p))
            return false;
        Crew.StandAside(p.CrewSlot);
        SandboxStand(p, teleport: true);
        if (c.Driver == who)
            SandboxHands.StopDriving(c);
        Console.WriteLine($"[crew] construct {c.Id}: {p.Name} stood up from seat {part} for a player");
        return c.Parts[part].SitterPlus1 == 0 && (part != c.DriveSeat || c.Driver == 255);
    }

    bool ICrewHost.Sit(byte player, Construct c, int part) => Players.TryGetValue(player, out var p) && CrewSit(p, c, part);

    void ICrewHost.StandAt(byte player, Construct c, Vector3 rest)
    {
        if (Players.TryGetValue(player, out var p))
            CrewStandAt(p, c, rest);
    }

    bool ICrewHost.Feet(byte player, out Vector3 feet)
    {
        feet = default;
        if (!Players.TryGetValue(player, out var p) || !p.Alive)
            return false;
        feet = p.Position;
        return true;
    }

    /// <summary>A crew body up out of any seat and on its feet at <paramref name="rest"/> (the body's rest frame), riding the body (CrewNav's walks start here).</summary>
    private void CrewStandAt(Player p, Construct c, Vector3 rest)
    {
        ref var h = ref SandboxHands.Hand(p.Id);
        if (h.Seated)
        {
            var was = Constructs.Get(h.SeatBody);
            SandboxHands.Stand(Constructs, p.Id);
            if (was != null && was.Driver == p.Id)
                SandboxHands.StopDriving(was);
        }
        h.Edit = EditKind.None;
        var at = c.ToWorld(rest + new Vector3(0, 0.03f, 0));
        p.Teleport(at);
        p.Sim.Velocity = c.Asleep || c.Anchored ? Vector3.Zero : c.PointVel(at);
        ConstructRide.End(ref p.Sim, null, c, 0, Vector3.Zero, frame: true); // (the ship's own frame, as a person rides it: CrewNav's walks need it)
        for (var i = 0; i < Player.HistorySize; i++)
            p.History[i] = new HistoryEntry { Position = p.Position, Yaw = p.Yaw }; // (a sleeping body isn't woken to place its crew)
    }

    /// <summary>A crew body into a seat, out of any it was in (no reach: the crew's timed walk got it there).</summary>
    private bool CrewSit(Player p, Construct c, int part)
    {
        if (part < 0 || part >= c.PartCount || c.DefOf(part).Role != PartRole.Seat || c.Parts[part].Broken || c.Parts[part].SitterPlus1 != 0
            || part == c.DriveSeat && c.Driver != 255)
            return false;
        ref var h = ref SandboxHands.Hand(p.Id);
        if (h.Seated)
            SandboxHands.Stand(Constructs, p.Id);
        h.Edit = EditKind.None;
        h.HasSeatLook = false;
        h.SeatBody = c.Id;
        h.SeatPart = (ushort)part;
        c.Parts[part].SitterPlus1 = (byte)(p.Id + 1);
        if (part == c.DriveSeat)
        {
            c.Driver = p.Id;
            c.DriverSeat = part;
        }
        p.Teleport(ConstructEdits.SeatFeet(c, part));
        for (var i = 0; i < Player.HistorySize; i++)
            p.History[i] = new HistoryEntry { Position = p.Position, Yaw = p.Yaw }; // (no wake: a parked body stays parked; its drive wakes it on its keys)
        return true;
    }

    /// <summary>A crew body's tick in place of a bot's brain: it sits still and looks where its seat points (its gun's laying: ShipAi).</summary>
    private void CrewBodyTick(Player p)
    {
        if (!p.Alive)
            return;
        if (CrewWalkTick(p))
            return;
        if (CrewMineBody(p))
            return; // (a miner on the ground: GameCrewMine.cs)
        ref var h = ref SandboxHands.Hand(p.Id);
        var yaw = p.Yaw;
        var pitch = p.Pitch;
        if (h.Seated && Constructs.Get(h.SeatBody) is { } c && h.SeatPart < c.PartCount)
        {
            yaw = c.Parts[h.SeatPart].AimYaw;
            pitch = c.Parts[h.SeatPart].AimPitch;
        }
        ApplyCmd(p, new InputCmd { Seq = p.LastInput.Seq + 1, Yaw = yaw, Pitch = Math.Clamp(pitch, -1.5f, 1.5f), Weapon = p.Weapon, SpeedPct = 100 });
    }

    /// <summary>A crew body walking CrewNav's path presses the keys that follow it (true), and says when it is there or gives up.</summary>
    private bool CrewWalkTick(Player p)
    {
        var w = Crew.Nav.WalkerOf(p.CrewSlot);
        if (w < 0 || Constructs.Get(Crew.Nav.WBody[w]) is not { Wreck: false } c)
            return false;
        if (SandboxHands.Hand(p.Id).Seated)
            return false; // (sat down meanwhile)
        var m = p.CrewSlot;
        var leave = Crew.State[m] == CrewState.Fighting || Crew.ShipOf(c.Id) is { Boarded: true }; // (a fight aboard: doors are left as they are)
        var phase = Crew.Nav.Steer(w, c, Constructs, ref p.Sim, out var cmd, leave);
        if (phase >= 2)
        {
            Crew.Nav.End(w);
            Crew.Arrived(p.CrewSlot, phase == 2);
            cmd = new InputCmd { SpeedPct = 100, RideBody = p.Sim.RideBody, Yaw = p.Yaw };
        }
        else if (phase == 0)
            cmd = new InputCmd { SpeedPct = 100, RideBody = p.Sim.RideBody, Yaw = p.Yaw }; // (the way is being searched: it waits)
        cmd.Seq = p.LastInput.Seq + 1;
        cmd.Weapon = p.Weapon;
        if (cmd.Buttons == Buttons.None)
            cmd.RideYaw = p.Sim.RideBody != 0 ? cmd.RideYaw : 0;
        ApplyCmd(p, cmd);
        return true;
    }

    /// <summary>Lines for a tool (allocates): each crew's roster, its driver's shots, its body's height and speed.</summary>
    public List<string> CrewLines()
    {
        var lines = new List<string>();
        var bodies = Players.Values.Count(p => p.CrewSlot >= 0 || !IsServer && p.Name.StartsWith('['));
        lines.Add($"{(IsServer ? "host" : "client")}: {CreditsLine}; rosters known: {string.Join("; ", CrewRosters.Values.Select(v => $"{v.Body} owner {v.Owner}{(v.Own ? " own" : "")}{(v.Prize ? " prize" : "")}: {string.Join(", ", v.Members.Select(m => $"{m.Name} {m.Job} {m.State}"))}"))}");
        lines.Add($"{(IsServer ? "host" : "client")}: {bodies} crew bodies among the players, stand-ins on {CrewStandIns.Count} bodies ({CrewStandIns.Values.Sum(a => a.Length)} seats: {string.Join(", ", CrewStandIns.Select(kv => $"{kv.Key}: {string.Join("/", kv.Value)}"))})");
        for (var i = 0; i < Crew.ShipHigh; i++)
            if (Crew.Ships[i] is { } s && Constructs.Get(s.Body) is { } c)
                lines.Add($"construct {s.Body}{(c.CrewAims ? " (crew aims)" : "")}{(c.Driver != 255 ? $" driver {c.Driver}" : "")} at height {ConstructPhysics.HoldHeight(c.WorldCom):0} m, {c.Vel.Length():0.0} m/s, {(s.Ai != null ? $"{s.Ai.State}, shots {s.Ai.Shots} (gunners {s.Ai.GunnerShots})" : "no driver")}; {Crew.Describe(s)}");
        return lines;
    }

    /// <summary>A tool: <paramref name="by"/> shoots the first crew body whose job is <paramref name="job"/> through the players' damage. Null: none.</summary>
    public string? CrewShoot(Player by, string job)
    {
        for (var m = 0; m < Crew.High; m++)
            if (Crew.Ship[m] >= 0 && Crew.Player[m] != 0 && Crew.State[m] == CrewState.Station && Crew.Job[m].ToString().Equals(job, StringComparison.OrdinalIgnoreCase)
                && Players.TryGetValue((byte)(Crew.Player[m] - 1), out var p) && p.Alive)
            {
                DamagePlayer(by, p, 1000, false, 0, SandboxNow);
                return $"crew member {Crew.Uid[m]} ({p.Name}) shot: {(p.Alive ? "alive" : "dead")}";
            }
        return null;
    }

    // ---- Targets ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The crew's contact: the nearest living person or bot it fights within its faction's sight (<see cref="ShipAi.EngageRange"/> with none), as
    /// what it shoots at (the body they sit or ride in, else themselves), or the nearest awake crewed body of a faction its own is hostile to.
    /// Looked for again when <paramref name="look"/> (every half second, staggered); between looks the same contact is followed.
    /// </summary>
    bool ICrewHost.Target(Construct c, CrewShip s, bool look, out ShipAiTarget t)
    {
        t = default;
        var com = c.WorldCom;
        var def = CrewPowers.KindOf(s.Faction);
        var sight = def?.Sight ?? ShipAi.EngageRange;
        if (look)
        {
            s.Contact = 255;
            s.ContactBody = 0;
            var best = sight * sight;
            foreach (var p in Players.Values)
            {
                if (!Fights(c, s, p) || !CrewAim(c, p, out var at, out _, out _))
                    continue;
                var d2 = Vector3.DistanceSquared(at, com);
                if (d2 >= best)
                    continue;
                best = d2;
                s.Contact = p.Id;
            }
            if (def != null)
                for (var i = 0; i < Crew.ShipHigh; i++)
                {
                    if (Crew.Ships[i] is not { Tier: not CrewTier.Asleep, Faction: not 0, Own: false, Prize: false } o || o == s || o.MemberCount == 0
                        || (s.Own ? c.Owner == 255 || (CrewStanding(o.Faction, c.Owner) >= 0 || CrewStandingOn && Relations.PartyHoldsFire(o.Faction, c.Owner)) && !o.Foe(c.Owner) : !CrewShipFights(s, o))
                        || Constructs.Get(o.Body) is not { Wreck: false } oc)
                        continue;
                    var d2 = Vector3.DistanceSquared(oc.WorldCom, com);
                    if (d2 >= best)
                        continue;
                    best = d2;
                    s.Contact = 255;
                    s.ContactBody = o.Body;
                }
        }
        if (s.ContactBody != 0)
        {
            if (Constructs.Get(s.ContactBody) is { Wreck: false } oc && Crew.ShipOf(s.ContactBody) is { MemberCount: > 0 } && Vector3.DistanceSquared(oc.WorldCom, com) < sight * sight * 1.21f)
            {
                t = new ShipAiTarget { At = oc.WorldCom, Vel = oc.Vel, Body = oc.Id, Player = 255 };
                return true;
            }
            s.ContactBody = 0;
            return false;
        }
        if (s.Contact == 255 || !Players.TryGetValue(s.Contact, out var q) || !Fights(c, s, q) || !CrewAim(c, q, out var where, out var vel, out var body)
            || Vector3.DistanceSquared(where, com) > sight * sight * 1.21f)
        {
            s.Contact = 255;
            return false;
        }
        t = new ShipAiTarget { At = where, Vel = vel, Body = body, Player = s.Contact };
        return true;
    }

    /// <summary>
    /// Whether crew <paramref name="s"/> fights <paramref name="p"/>: with a faction kind, anyone who has hit its body or whose standing with the
    /// kind is below 0 (a hostile kind starts there, a wary or neutral one above: bot-brain.md section 7); with none, the Flight Stabilizer's
    /// settings (Fights strangers spares its owner's faction).
    /// </summary>
    private bool Fights(Construct c, CrewShip s, Player p)
    {
        if (!p.Alive || p.CrewSlot >= 0)
            return false;
        if (s.Faction == 0)
            return !(s.Mode == ShipAiMode.Strangers && c.Owner != 255 && Factions.Shares(c.Owner, p.Id));
        if (s.Own)
            return p.Id != c.Owner && !Factions.Shares(c.Owner, p.Id) && (s.Foe(p.Id) || FactionTable.RelationOf(c.Owner, p.Id) == FactionTable.Relations.War); // (a player's own crew: who hit it, or a faction at war with its owner's)
        if (s.Foe(p.Id))
            return true;
        if (CrewStandingOn && Relations.PartyHoldsFire(s.Faction, p.Id))
            return false; // (phase 5: a pact with their faction holds fire; one who shoots is a foe, and breaks it)
        return CrewStanding(s.Faction, p.Id) < 0 || CrewStandingOn && CrewHelpsAgainst(c, s, p);
    }

    /// <summary>
    /// Whether crew <paramref name="s"/> fights crew <paramref name="o"/> (faction-controllers.md section 5): it answers a crew that shot it lately;
    /// else no pact may hold between their powers, and it fights when its power sees theirs below 0, or (not friends with them) when it sees them
    /// fighting a power or a person its power counts as a friend or an ally (help in a fight it sees).
    /// </summary>
    private bool CrewShipFights(CrewShip s, CrewShip o)
    {
        var a = s.Faction;
        var b = o.Faction;
        if (a == b)
            return false;
        if (s.FoeBody == o.Body && Crew.Tick - s.FoeBodyAt < 2 * CrewWorld.DoerTicks)
            return true;
        if (!CrewStandingOn)
            return CrewPowers.KindOf(a) is { } k && CrewPowers.KindOf(b) is { } kb && k.ToFactions[kb.Id] == CrewStance.Hostile; // (the story owns the rows: the kinds' fixed stances)
        var friend = o.ContactBody != 0 && Crew.ShipOf(o.ContactBody) is { Faction: not 0, Own: false } f ? f.Faction : (byte)0;
        var person = o.Contact != 255 && Players.TryGetValue(o.Contact, out var p) && p.CrewSlot < 0 && p.Bot == null ? o.Contact : (byte)255;
        return Relations.ShipFights(a, b, friend, person);
    }

    /// <summary>A person crew <paramref name="s"/> doesn't fight for itself, seen fighting a crew of a power its own counts as a friend or ally, within its sight (help in a fight it sees).</summary>
    private bool CrewHelpsAgainst(Construct c, CrewShip s, Player p)
    {
        var rel = Relations;
        if (rel.Peek(s.Faction, p.Id) >= rel.Rules.FriendlyLine || rel.PartyHoldsFire(s.Faction, p.Id))
            return false;
        var sight = CrewPowers.KindOf(s.Faction)?.Sight ?? ShipAi.EngageRange;
        for (var i = 0; i < Crew.ShipHigh; i++)
            if (Crew.Ships[i] is { Tier: not CrewTier.Asleep, Own: false, Prize: false } o && o != s && o.Contact == p.Id && o.Faction != s.Faction
                && rel.Helps(s.Faction, o.Faction) && Constructs.Get(o.Body) is { } oc && Vector3.DistanceSquared(oc.WorldCom, c.WorldCom) < sight * sight)
                return true;
        return false;
    }

    /// <summary>Where a crew shoots at <paramref name="p"/>: the body they sit or ride in, else themselves. False: aboard this one (a boarder is the crew's business, not its guns').</summary>
    private bool CrewAim(Construct c, Player p, out Vector3 at, out Vector3 vel, out ushort body)
    {
        ref var h = ref SandboxHands.Hand(p.Id);
        body = h.SeatBody != 0 ? h.SeatBody : p.Sim.RideBody;
        at = p.Position;
        vel = p.Sim.Velocity;
        if (body == c.Id)
            return false;
        if (body != 0 && Constructs.Get(body) is { } b)
        {
            at = b.WorldCom;
            vel = b.Vel;
            return true;
        }
        body = 0;
        at = GravityFrames.Above(at, 1.2f);
        return true;
    }

    // ---- Stand-ins: crew with no body, drawn at their seats ---------------------------------------------------------------------------

    /// <summary>Client (and the host): per crewed body, the seats with a crew member and no body (Msg.CrewSeats). Never read by the simulation.</summary>
    public readonly Dictionary<ushort, ushort[]> CrewStandIns = new();

    /// <summary>Host: a crewed body's stand-ins when they change (a few a tick), and every body's again in turn for anyone who missed one.</summary>
    private void CrewSendSeats()
    {
        foreach (var gone in Crew.GoneBodies)
        {
            CrewSeatsOut(gone, 0, 0, 0, -1, 0, 0xFFFF, 0xFFFF, 0);
            CrewRosterGone(gone);
        }
        Crew.GoneBodies.Clear();
        var sent = 0;
        var refresh = ++_crewSendAt % (5 * World.TickRate) == 0;
        for (var i = 0; i < Crew.ShipHigh && sent < 8; i++)
        {
            if (Crew.Ships[i] is not { } s || !s.SeatsDirty && !refresh)
                continue;
            s.SeatsDirty = false;
            s.RosterDirty = true; // (the Crew window's roster too, for an own crew or a prize)
            var cap = s.Faction != 0 && !s.Own ? CrewFleet.CaptainOf(Crew, s) : -1;
            var grp = Crew.Fleet.Of(s.Group);
            var adm = grp != null && grp.Flagship == s.Body && grp.AdmiralUid != 0 ? grp.AdmiralName : (ushort)0xFFFF;
            CrewSeatsOut(s.Body, s.Tier == CrewTier.Asleep && s.Near > CrewStandInFar ? 0 : Crew.StandIns(s, _crewParts), s.Faction, Crew.Alive(s), s.ScPart, s.ScStage,
                cap >= 0 ? Crew.Name[cap] : (ushort)0xFFFF, adm, grp?.Number ?? 0);
            sent++;
        }
    }

    /// <summary>Stand-ins aren't sent for an asleep crew this far from everyone (m).</summary>
    public const float CrewStandInFar = 4000;

    private void CrewSeatsOut(ushort body, int n, byte faction, int alive, int scuttle, byte stage, ushort captain, ushort admiral, ushort group)
    {
        var w = _w;
        w.Begin(Msg.CrewSeats).U16(body).U8((byte)n);
        for (var k = 0; k < n; k++)
            w.U16(_crewParts[k]);
        w.U8(faction).U8((byte)alive).U16((ushort)(scuttle < 0 ? 0xFFFF : scuttle)).U8(stage).U16(captain).U16(admiral).U16(group);
        if (!Net.IsOffline)
            Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
        CrewSeatsSet(body, _crewParts.AsSpan(0, n), faction, alive, scuttle, stage, captain, admiral, group); // (the host draws them too)
    }

    /// <summary>A joiner hears every crewed body's stand-ins.</summary>
    private void SendCrewSeats(RakNetGUID to)
    {
        foreach (var (body, info) in CrewInfo)
        {
            var parts = CrewStandIns.TryGetValue(body, out var ps) ? ps : [];
            _w.Begin(Msg.CrewSeats).U16(body).U8((byte)parts.Length);
            foreach (var p in parts)
                _w.U16(p);
            _w.U8(info.Faction).U8(info.Alive).U16((ushort)(info.Scuttle < 0 ? 0xFFFF : info.Scuttle)).U8(info.Stage).U16(info.Captain).U16(info.Admiral).U16(info.Group);
            Net.SendTo(to, _w.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>Client: Msg.CrewSeats (u16 body, u8 n, n x u16 seat part, u8 faction kind, u8 crew alive, u16 scuttle point (0xFFFF: none), u8 its stage: protocol 332; u16 captain's name, u16 admiral's name aboard (0xFFFF: none), u16 group number: 346).</summary>
    private void ReadCrewSeats(ref PacketReader r)
    {
        var body = r.U16();
        int n = r.U8();
        if (n > CrewWorld.MaxCrewPerBody || !r.Has(n * 2 + 11))
        {
            Console.WriteLine($"[crew] a stand-in list of {n} seats for construct {body} didn't read: dropped");
            return;
        }
        Span<ushort> parts = stackalloc ushort[n];
        for (var k = 0; k < n; k++)
            parts[k] = r.U16();
        var faction = r.U8();
        var alive = r.U8();
        var scuttle = r.U16();
        var stage = r.U8();
        var captain = r.U16();
        var admiral = r.U16();
        var group = r.U16();
        CrewSeatsSet(body, parts, faction, alive, scuttle == 0xFFFF ? -1 : scuttle, stage, captain, admiral, group);
    }

    /// <summary>Client (and the host): per crewed body, its faction kind and crew alive (the look line's). Never read by the simulation.</summary>
    public readonly Dictionary<ushort, (byte Faction, byte Alive, int Scuttle, byte Stage, ushort Captain, ushort Admiral, ushort Group)> CrewInfo = new();

    /// <summary>The look line's crew note for a body ("Raiders, crew of 4"), or null.</summary>
    public string? CrewLook(ushort body) => CrewInfo.TryGetValue(body, out var i) && i.Alive > 0
        ? $"{(i.Admiral != 0xFFFF ? $"Admiral {CrewNames.Of(i.Admiral)}'s flagship, " : "")}{CrewPowers.Of(i.Faction)?.Name ?? "crewed"}{(i.Group != 0 ? $" group {i.Group}" : "")}, {(i.Captain != 0xFFFF ? $"Captain {CrewNames.Of(i.Captain)}, " : "")}crew of {i.Alive}" : null;

    private void CrewSeatsSet(ushort body, ReadOnlySpan<ushort> parts, byte faction, int alive, int scuttle, byte stage, ushort captain, ushort admiral, ushort group)
    {
        if (alive > 0 || !parts.IsEmpty || scuttle >= 0)
            CrewInfo[body] = (faction, (byte)Math.Min(alive, 255), scuttle, stage, captain, admiral, group);
        else
            CrewInfo.Remove(body);
        if (parts.IsEmpty)
        {
            CrewStandIns.Remove(body);
            return;
        }
        if (!CrewStandIns.TryGetValue(body, out var arr) || arr.Length != parts.Length)
            CrewStandIns[body] = arr = new ushort[parts.Length]; // (a change of crew, not a tick)
        parts.CopyTo(arr);
    }
}
