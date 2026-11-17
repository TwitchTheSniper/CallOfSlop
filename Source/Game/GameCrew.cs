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
        if (Crew.Events.Count > 0)
            CrewLog();
        CrewWorkTick();
        CrewSendSeats();
        CrewHireTick();
        CrewRosterSend();
    }

    /// <summary>
    /// A nobody's ground vehicle or walker just set down (a faction vehicle, a convoy's member) gets a crew in its seats (bot-brain.md phase 2:
    /// it moves only with a driver aboard). Aircraft and ships are crewed by their pilots and stabilizers; a player's vehicle never is.
    /// </summary>
    public void CrewFactionVehicle(Construct c)
    {
        if (!IsServer || c.Owner != 255 || c.Wreck || c.StabCount > 0 || c.DriveSeat < 0 || Crew.Has(c.Id))
            return;
        Crew.Crew(c, ShipAiMode.Strangers, faction: CrewFactionFor(c));
        CrewLog();
    }

    /// <summary>A nobody's generated craft's faction kind: the one its style belongs to, else Raiders.</summary>
    public static byte CrewFactionFor(Construct c) => c.Brief is { } b && CrewFactions.ForStyle(b.Style) is var f and > 0 ? f : CrewFactions.Find("Raiders");

    private void CrewLog()
    {
        foreach (var e in Crew.Events)
            Console.WriteLine($"[crew] {e}");
        Crew.Events.Clear();
    }

    // ---- Bodies ---------------------------------------------------------------------------------------------------------------------

    byte ICrewHost.Promote(CrewWorld crew, int m, Construct c, int part)
    {
        if (SandboxBotCount() + crew.Bodied >= Math.Clamp(SandboxBotCap, 0, SpawnBotCapMax))
            return 0; // (the spawned bots' cap is shared: bot-brain.md section 4)
        var id = FreeNpcId();
        if (id == 0)
            return 0;
        var ship = crew.ShipOf(c.Id);
        var bot = new Player
        {
            Id = id,
            Name = $"[{(CrewFactions.Of(ship?.Faction ?? 0)?.Name ?? "CREW")}] {CrewNames.Of(crew.Name[m])}",
            Bot = new BotBrain(BotDifficulty.Normal, BotPersonality.Tactician, (int)crew.Uid[m]),
            Team = ship?.Mode == ShipAiMode.Strangers && c.Owner != 255 && ship.Faction == 0 ? SbFriendlyTeam : SbHostileTeam,
            CrewSlot = m,
        };
        Players[id] = bot;
        Spawn(bot, ServerTick, SandboxNow);
        if (!CrewSit(bot, c, part))
        {
            Players.Remove(id);
            OnModePlayerLeave(bot);
            return 0;
        }
        bot.Health = Math.Max(1, (int)(World.MaxHealth * crew.Health[m] / CrewWorld.MemberHealth));
        _sbRosterDirty = true; // (one roster for a tick's bodies: SandboxSpawnTick sends it)
        return id;
    }

    void ICrewHost.Demote(byte player)
    {
        if (!Players.TryGetValue(player, out var p) || p.CrewSlot < 0)
            return;
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
            p.History[i] = new HistoryEntry { Position = p.Position, Yaw = p.Yaw };
        c.Wake();
        return true;
    }

    /// <summary>A crew body's tick in place of a bot's brain: it sits still and looks where its seat points (its gun's laying: ShipAi).</summary>
    private void CrewBodyTick(Player p)
    {
        if (!p.Alive)
            return;
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
        var def = CrewFactions.Of(s.Faction);
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
                        || (s.Own ? c.Owner == 255 || CrewStanding(o.Faction, c.Owner) >= 0 && !o.Foe(c.Owner) : def.ToFactions[o.Faction] != CrewStance.Hostile)
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
        return s.Foe(p.Id) || CrewStanding(s.Faction, p.Id) < 0;
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
            CrewSeatsOut(gone, 0, 0, 0, -1, 0);
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
            CrewSeatsOut(s.Body, s.Tier == CrewTier.Asleep && s.Near > CrewStandInFar ? 0 : Crew.StandIns(s, _crewParts), s.Faction, Crew.Alive(s), s.ScPart, s.ScStage);
            sent++;
        }
    }

    /// <summary>Stand-ins aren't sent for an asleep crew this far from everyone (m).</summary>
    public const float CrewStandInFar = 4000;

    private void CrewSeatsOut(ushort body, int n, byte faction, int alive, int scuttle, byte stage)
    {
        var w = _w;
        w.Begin(Msg.CrewSeats).U16(body).U8((byte)n);
        for (var k = 0; k < n; k++)
            w.U16(_crewParts[k]);
        w.U8(faction).U8((byte)alive).U16((ushort)(scuttle < 0 ? 0xFFFF : scuttle)).U8(stage);
        if (!Net.IsOffline)
            Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
        CrewSeatsSet(body, _crewParts.AsSpan(0, n), faction, alive, scuttle, stage); // (the host draws them too)
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
            _w.U8(info.Faction).U8(info.Alive).U16((ushort)(info.Scuttle < 0 ? 0xFFFF : info.Scuttle)).U8(info.Stage);
            Net.SendTo(to, _w.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>Client: Msg.CrewSeats (u16 body, u8 n, n x u16 seat part, u8 faction kind, u8 crew alive, u16 scuttle point (0xFFFF: none), u8 its stage: protocol 332).</summary>
    private void ReadCrewSeats(ref PacketReader r)
    {
        var body = r.U16();
        int n = r.U8();
        if (n > CrewWorld.MaxCrewPerBody || !r.Has(n * 2 + 5))
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
        CrewSeatsSet(body, parts, faction, alive, scuttle == 0xFFFF ? -1 : scuttle, stage);
    }

    /// <summary>Client (and the host): per crewed body, its faction kind and crew alive (the look line's). Never read by the simulation.</summary>
    public readonly Dictionary<ushort, (byte Faction, byte Alive, int Scuttle, byte Stage)> CrewInfo = new();

    /// <summary>The look line's crew note for a body ("Raiders, crew of 4"), or null.</summary>
    public string? CrewLook(ushort body) => CrewInfo.TryGetValue(body, out var i) && i.Alive > 0
        ? $"{CrewFactions.Of(i.Faction)?.Name ?? "crewed"}, crew of {i.Alive}" : null;

    private void CrewSeatsSet(ushort body, ReadOnlySpan<ushort> parts, byte faction, int alive, int scuttle, byte stage)
    {
        if (alive > 0 || !parts.IsEmpty || scuttle >= 0)
            CrewInfo[body] = (faction, (byte)Math.Min(alive, 255), scuttle, stage);
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
