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
        Crew.Step(Constructs, World.Query, this, _crewPeople.AsSpan(0, n));
        if (Crew.Events.Count > 0)
            CrewLog();
        CrewSendSeats();
    }

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
            Name = $"[CREW] {CrewNames.Of(crew.Name[m])}",
            Bot = new BotBrain(BotDifficulty.Normal, BotPersonality.Tactician, (int)crew.Uid[m]),
            Team = ship?.Mode == ShipAiMode.Strangers && c.Owner != 255 ? SbFriendlyTeam : SbHostileTeam,
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
        var bodies = Players.Values.Count(p => p.Name.StartsWith("[CREW]", StringComparison.Ordinal));
        lines.Add($"{(IsServer ? "host" : "client")}: {bodies} crew bodies among the players, stand-ins on {CrewStandIns.Count} bodies ({CrewStandIns.Values.Sum(a => a.Length)} seats: {string.Join(", ", CrewStandIns.Select(kv => $"{kv.Key}: {string.Join("/", kv.Value)}"))})");
        for (var i = 0; i < Crew.ShipHigh; i++)
            if (Crew.Ships[i] is { } s && Constructs.Get(s.Body) is { } c)
                lines.Add($"construct {s.Body} at height {ConstructPhysics.HoldHeight(c.WorldCom):0} m, {c.Vel.Length():0.0} m/s, {(s.Ai != null ? $"{s.Ai.State}, shots {s.Ai.Shots} (gunners {s.Ai.GunnerShots})" : "no driver")}; {Crew.Describe(s)}");
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
    /// The crew's contact: the nearest living person or bot it fights within <see cref="ShipAi.EngageRange"/>, as what it shoots at (the body
    /// they sit or ride in, else themselves). Looked for again when <paramref name="look"/> (every half second, staggered); between looks the
    /// same contact is followed. Crew bodies are never contacts (crews fighting crews is phase 3's factions).
    /// </summary>
    bool ICrewHost.Target(Construct c, ShipAiMode mode, ref byte contact, bool look, out ShipAiTarget t)
    {
        t = default;
        var com = c.WorldCom;
        if (look)
        {
            contact = 255;
            var best = ShipAi.EngageRange * ShipAi.EngageRange;
            foreach (var p in Players.Values)
            {
                if (!Fights(c, mode, p) || !CrewAim(c, p, out var at, out _, out _))
                    continue;
                var d2 = Vector3.DistanceSquared(at, com);
                if (d2 >= best)
                    continue;
                best = d2;
                contact = p.Id;
            }
        }
        if (contact == 255 || !Players.TryGetValue(contact, out var q) || !Fights(c, mode, q) || !CrewAim(c, q, out var where, out var vel, out var body)
            || Vector3.DistanceSquared(where, com) > ShipAi.EngageRange * ShipAi.EngageRange * 1.21f)
        {
            contact = 255;
            return false;
        }
        t = new ShipAiTarget { At = where, Vel = vel, Body = body, Player = contact };
        return true;
    }

    private bool Fights(Construct c, ShipAiMode mode, Player p) =>
        p.Alive && p.CrewSlot < 0 && !(mode == ShipAiMode.Strangers && c.Owner != 255 && Factions.Shares(c.Owner, p.Id));

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
            CrewSeatsOut(gone, 0);
        Crew.GoneBodies.Clear();
        var sent = 0;
        var refresh = ++_crewSendAt % (5 * World.TickRate) == 0;
        for (var i = 0; i < Crew.ShipHigh && sent < 8; i++)
        {
            if (Crew.Ships[i] is not { } s || !s.SeatsDirty && !refresh)
                continue;
            s.SeatsDirty = false;
            CrewSeatsOut(s.Body, s.Tier == CrewTier.Asleep && s.Near > CrewStandInFar ? 0 : Crew.StandIns(s, _crewParts));
            sent++;
        }
    }

    /// <summary>Stand-ins aren't sent for an asleep crew this far from everyone (m).</summary>
    public const float CrewStandInFar = 4000;

    private void CrewSeatsOut(ushort body, int n)
    {
        var w = _w;
        w.Begin(Msg.CrewSeats).U16(body).U8((byte)n);
        for (var k = 0; k < n; k++)
            w.U16(_crewParts[k]);
        if (!Net.IsOffline)
            Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
        CrewSeatsSet(body, _crewParts.AsSpan(0, n)); // (the host draws them too)
    }

    /// <summary>A joiner hears every crewed body's stand-ins.</summary>
    private void SendCrewSeats(RakNetGUID to)
    {
        foreach (var (body, parts) in CrewStandIns)
        {
            _w.Begin(Msg.CrewSeats).U16(body).U8((byte)parts.Length);
            foreach (var p in parts)
                _w.U16(p);
            Net.SendTo(to, _w.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>Client: Msg.CrewSeats (u16 body, u8 n, n x u16 seat part).</summary>
    private void ReadCrewSeats(ref PacketReader r)
    {
        var body = r.U16();
        int n = r.U8();
        if (n > CrewWorld.MaxCrewPerBody || !r.Has(n * 2))
        {
            Console.WriteLine($"[crew] a stand-in list of {n} seats for construct {body} didn't read: dropped");
            return;
        }
        Span<ushort> parts = stackalloc ushort[n];
        for (var k = 0; k < n; k++)
            parts[k] = r.U16();
        CrewSeatsSet(body, parts);
    }

    private void CrewSeatsSet(ushort body, ReadOnlySpan<ushort> parts)
    {
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
