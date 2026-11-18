using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The game's side of groups and admirals (docs/sandbox/design/faction-controllers.md phase 2; the fleet itself is Source/Crew/CrewFleet.cs):
/// a spawner's crewed convoy becomes a group led by an admiral, the fleet's news goes to everyone, officers taken prisoner are worth a ransom
/// and can be questioned for intelligence (an Interrogator of your own crew does it faster), and what they give up is shown to the asker and
/// their faction as marks in the world for a while (<see cref="Msg.Intel"/>). Server, except the marks.
/// </summary>
public sealed partial class Game
{
    /// <summary>An officer taken prisoner: what it can give up, and from where (its group's slot and serial: a group reused is another group).</summary>
    private sealed class Officer
    {
        public byte Power;
        public short Group;
        public int Serial;
        public ushort Ship;
        public bool Admiral;
        public float Resistance;
    }

    private readonly Dictionary<uint, Officer> _officers = new();
    private readonly Dictionary<uint, (byte Player, ushort Body, long Done)> _questioning = new();
    private readonly List<string> _intelLines = new(32);
    private readonly List<(Vector3 At, string Label)> _intelMarks = new(32);

    /// <summary>A crewed convoy's member set down by the spawner joins its convoy's group (its leader forms it and carries the admiral).</summary>
    private void CrewFleetVehicle(Construct c)
    {
        if (c.Brief is not { Convoy: not 0 } b || Crew.ShipOf(c.Id) is not { Faction: not 0 } s || s.Group >= 0)
            return;
        Span<ushort> one = [c.Id];
        if (b.ConvoyIndex == 0)
            Crew.Fleet.Form(Crew, Constructs, GroupTask.Convoy, one, $"the convoy led by construct {c.Id}");
        else if (Crew.ShipOf(b.Leader) is { Group: >= 0 } lead)
            Crew.Fleet.Join(Crew, Constructs, lead.Group, c.Id);
        else
            Console.WriteLine($"[fleet] construct {c.Id}: its convoy's leader {b.Leader} has no group: its captain is on its own");
        CrewLog();
    }

    /// <summary>A tick, after the crews': the fleet's news to everyone; questioning steps once a second.</summary>
    private void CrewFleetTick()
    {
        foreach (var line in Crew.Fleet.News)
            PowerNewsPush(line);
        Crew.Fleet.News.Clear();
        if (_questioning.Count > 0 && Crew.Tick % 30 == 0)
            CrewQuestionStep();
    }

    /// <summary>A ship taken: its admiral and its captain among the prisoners are officers (a relation event each, and a notice of what they are worth).</summary>
    private void CrewOfficersTaken(Construct c, CrewShip s, byte by)
    {
        var cap = s.CaptainUid;
        for (var i = 0; i < s.MemberCount; i++)
        {
            var m = s.Members[i];
            if (Crew.State[m] != CrewState.Surrendered)
                continue;
            var admiral = Crew.Job[m] == CrewJob.Admiral;
            if (!admiral && Crew.Uid[m] != cap)
                continue;
            short group = -1;
            var serial = 0;
            if (admiral)
                foreach (var g in Crew.Fleet.Groups)
                    if (g.Live && g.AdmiralUid == Crew.Uid[m])
                        (group, serial) = ((short)g.Slot, g.Serial);
            if (!admiral && Crew.Fleet.Of(s.Group) is { } sg)
                (group, serial) = (s.Group, sg.Serial);
            var power = Crew.Fac[m];
            var o = new Officer { Power = power, Group = group, Serial = serial, Ship = c.Id, Admiral = admiral, Resistance = CrewFleet.ResistanceOf(Crew.Uid[m], Crew.Fleet.Rules.DoctrineOf(power)) };
            _officers[Crew.Uid[m]] = o;
            if (by != 255)
                PowerDeed(RelEvent.CaptureOfficer, power, by, c.WorldCom);
            var rank = admiral ? "Admiral" : "Captain";
            Console.WriteLine($"[fleet] construct {c.Id}: {rank} {CrewNames.Of(Crew.Name[m])} ({CrewPowers.Of(power)?.Name}) taken prisoner by player {by}");
            CrewNotice(c, $"Among them {rank} {CrewNames.Of(Crew.Name[m])} of {CrewPowers.Of(power)?.Name}: worth a ransom of {(admiral ? Crew.Fleet.Rules.RansomAdmiral : Crew.Fleet.Rules.RansomCaptain)} credits, or questioning (Crew window).");
        }
    }

    /// <summary>The prisoner record of <paramref name="uid"/> on a ship <paramref name="p"/> holds, or why not.</summary>
    private string? CrewHeld(Player p, ushort body, uint uid, out Construct c, out CrewShip s, out int m)
    {
        c = null!;
        s = null!;
        m = -1;
        if (Constructs.Get(body) is not { } cc || Crew.ShipOf(body) is not { } ss)
            return "no crew aboard that";
        if (cc.Owner != p.Id && !Factions.Shares(cc.Owner, p.Id))
            return "not your ship";
        (c, s, m) = (cc, ss, CrewByUid(ss, uid));
        return m < 0 || Crew.State[m] != CrewState.Surrendered ? "no such prisoner" : null;
    }

    /// <summary>Server: start questioning a prisoner officer (CrewOrder.Interrogate). Null: started (or already under way).</summary>
    private string? CrewInterrogate(Player p, ushort body, uint uid)
    {
        if (CrewHeld(p, body, uid, out var c, out _, out var m) is { } why)
            return why;
        if (!_officers.TryGetValue(uid, out var o))
            return $"{CrewNames.Of(Crew.Name[m])} is no officer: only an admiral or a captain knows anything worth asking";
        if (_questioning.ContainsKey(uid))
            return null;
        // The best Interrogator of the asker's own crews within reach of the prisoner's ship.
        var skill = -1;
        var who = "";
        for (var i = 0; i < Crew.ShipHigh; i++)
            if (Crew.Ships[i] is { Own: true } own && Constructs.Get(own.Body) is { } oc && (oc.Owner == p.Id || Factions.Shares(oc.Owner, p.Id))
                && Vector3.DistanceSquared(oc.WorldCom, c.WorldCom) < InterrogateReach * InterrogateReach)
                for (var k = 0; k < own.MemberCount; k++)
                {
                    var im = own.Members[k];
                    if (Crew.Job[im] == CrewJob.Interrogator && Crew.State[im] is not (CrewState.Dead or CrewState.Surrendered) && Crew.Skill[im] > skill)
                        (skill, who) = (Crew.Skill[im], CrewNames.Of(Crew.Name[im]));
                }
        var secs = Crew.Fleet.InterrogationSeconds(o.Resistance, skill);
        _questioning[uid] = (p.Id, body, Crew.Tick + (long)(secs * World.TickRate));
        var rank = o.Admiral ? "Admiral" : "Captain";
        SandboxHands.Notices.Add((p.Id, $"{(skill >= 0 ? $"{who} questions" : "You question")} {rank} {CrewNames.Of(Crew.Name[m])}: {(o.Resistance > 0.7f ? "a hard one" : o.Resistance < 0.3f ? "they look ready to talk" : "they hold out")}, about {secs:0} s."));
        Console.WriteLine($"[fleet] player {p.Id} questions {rank} {CrewNames.Of(Crew.Name[m])} (resistance {o.Resistance:0.00}, {(skill >= 0 ? $"interrogator skill {skill}" : "no interrogator")}): {secs:0} s");
        return null;
    }

    /// <summary>A player's own crew within this far of a prisoner's ship (m) can question it.</summary>
    public const float InterrogateReach = 300;

    private void CrewQuestionStep()
    {
        foreach (var (uid, q) in _questioning)
        {
            var gone = !Players.TryGetValue(q.Player, out var p) || CrewHeld(p, q.Body, uid, out _, out _, out _) != null || !_officers.ContainsKey(uid);
            if (gone)
            {
                if (p != null)
                    SandboxHands.Notices.Add((p.Id, "The questioning stopped: the prisoner is no longer yours."));
                _questioning.Remove(uid);
                return; // (one a step: the dictionary changed)
            }
            if (Crew.Tick < q.Done)
                continue;
            _questioning.Remove(uid);
            CrewIntelGive(p!, uid, q.Body);
            return;
        }
    }

    /// <summary>A broken officer's intelligence: lines to the asker, marks to them and their faction for the rules' intel time.</summary>
    private void CrewIntelGive(Player p, uint uid, ushort body)
    {
        var o = _officers[uid];
        _officers.Remove(uid); // (questioned once: what they knew is told)
        var m = Crew.ShipOf(body) is { } s ? CrewByUid(s, uid) : -1;
        var name = m >= 0 ? CrewNames.Of(Crew.Name[m]) : "the prisoner";
        _intelLines.Clear();
        _intelMarks.Clear();
        Crew.Fleet.Intel(Crew, Constructs, this, uid, o.Power, o.Group, o.Serial, o.Ship, o.Admiral, _intelLines, _intelMarks);
        if (o.Resistance > 0.8f && _intelMarks.Count > 2)
            _intelMarks.RemoveRange(2, _intelMarks.Count - 2); // (a hard one gives up only part)
        SandboxHands.Notices.Add((p.Id, $"{(o.Admiral ? "Admiral" : "Captain")} {name} talks:"));
        foreach (var l in _intelLines)
            SandboxHands.Notices.Add((p.Id, l));
        if (o.Admiral && Crew.Fleet.Of(o.Group) is { } g && g.Serial == o.Serial)
        {
            g.RevealedUntil = Crew.Tick + (long)(Crew.Fleet.Rules.IntelSeconds * World.TickRate);
            g.RevealedTo[p.Id >> 6] |= 1UL << (p.Id & 63);
        }
        Console.WriteLine($"[fleet] player {p.Id} broke {(o.Admiral ? "Admiral" : "Captain")} {name}: {_intelLines.Count} lines, {_intelMarks.Count} positions");
        foreach (var q in Players.Values)
            if (q.Bot == null && q.CrewSlot < 0 && (q.Id == p.Id || Factions.Shares(p.Id, q.Id)))
                SendIntel(q, Crew.Fleet.Rules.IntelSeconds);
    }

    /// <summary>Server: an officer prisoner goes back to its power for credits (CrewOrder.Ransom): +15 with them, the ransom to the asker.</summary>
    private string? CrewRansom(Player p, ushort body, uint uid)
    {
        if (CrewHeld(p, body, uid, out var c, out var s, out var m) is { } why)
            return why;
        if (!_officers.TryGetValue(uid, out var o))
            return "only an admiral or a captain is worth a ransom";
        var amount = o.Admiral ? Crew.Fleet.Rules.RansomAdmiral : Crew.Fleet.Rules.RansomCaptain;
        Crew.Release(m, this);
        s.RosterDirty = true;
        _officers.Remove(uid);
        _questioning.Remove(uid);
        PowerDeed(RelEvent.ReleaseOfficer, o.Power, p.Id, c.WorldCom);
        CreditsGive(p.Id, amount);
        SandboxHands.Notices.Add((p.Id, $"{CrewPowers.Of(o.Power)?.Name} paid {amount} credits for {(o.Admiral ? "Admiral" : "Captain")} {CrewNames.Of(Crew.Name[m])}."));
        Console.WriteLine($"[fleet] player {p.Id} ransomed {(o.Admiral ? "admiral" : "captain")} {uid} to {CrewPowers.Of(o.Power)?.Name} for {amount}");
        return null;
    }

    /// <summary>An officer released (not ransomed): the bigger release event instead of a plain prisoner's.</summary>
    private bool CrewOfficerFreed(uint uid, byte player, Vector3 at)
    {
        if (!_officers.Remove(uid, out var o))
            return false;
        _questioning.Remove(uid);
        PowerDeed(RelEvent.ReleaseOfficer, o.Power, player, at);
        return true;
    }

    /// <summary>Server: one of your own hands becomes your Interrogator, or back to a spare (CrewOrder.Interrogator).</summary>
    private string? CrewMakeInterrogator(CrewShip s, uint uid)
    {
        var m = CrewByUid(s, uid);
        if (m < 0 || Crew.State[m] is CrewState.Dead or CrewState.Surrendered)
            return "no such hand";
        if (Crew.Job[m] == CrewJob.Interrogator)
            Crew.Job[m] = CrewJob.Spare;
        else
        {
            Crew.StandAside(m);
            Crew.Job[m] = CrewJob.Interrogator;
        }
        s.RosterDirty = true;
        return null;
    }

    // ---- Intelligence marks (client and host) -----------------------------------------------------------------------------------------

    /// <summary>What questioning gave up: world points with a label, shown until their time (client and host; never read by the simulation).</summary>
    public readonly List<(Vector3 At, string Label, long Until)> IntelMarks = new(IntelMax);

    public const int IntelMax = 64;

    private void SendIntel(Player to, float seconds)
    {
        var n = Math.Min(_intelMarks.Count, 32);
        if (to.Id == LocalId)
            IntelSet(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_intelMarks)[..n], seconds);
        if (Net.IsOffline || to.Id == LocalId || GuidOf(to) is not { } guid)
            return;
        _w.Begin(Msg.Intel).U16((ushort)Math.Min(seconds, 65535)).U8((byte)n);
        for (var k = 0; k < n; k++)
            _w.F32(_intelMarks[k].At.X).F32(_intelMarks[k].At.Y).F32(_intelMarks[k].At.Z).Str(_intelMarks[k].Label);
        Net.SendTo(guid, _w.Span, PacketReliability.ReliableOrdered);
    }

    private void IntelSet(ReadOnlySpan<(Vector3 At, string Label)> marks, float seconds)
    {
        var until = Environment.TickCount64 + (long)(seconds * 1000);
        foreach (var (at, label) in marks)
        {
            if (IntelMarks.Count >= IntelMax)
                IntelMarks.RemoveAt(0);
            IntelMarks.Add((at, label, until));
        }
    }

    /// <summary>Client: Msg.Intel (u16 seconds, u8 n, n x (3 x f32 where, text label): protocol 345).</summary>
    private void ReadIntel(ref PacketReader r)
    {
        if (!r.Has(3))
        {
            Console.WriteLine("[fleet] an intel message didn't read: dropped");
            return;
        }
        var secs = r.U16();
        int n = r.U8();
        Span<(Vector3, string)> marks = new (Vector3, string)[n];
        for (var k = 0; k < n; k++)
        {
            if (!r.Has(13))
            {
                Console.WriteLine($"[fleet] an intel message broke off at mark {k} of {n}: the rest dropped");
                n = k;
                break;
            }
            marks[k] = (new Vector3(r.F32(), r.F32(), r.F32()), r.Str());
        }
        IntelSet(marks[..n], secs);
        Console.WriteLine($"[fleet] intel: {n} positions for {secs} s");
    }

    /// <summary>The marks still shown (drops the old ones; the HUD draws these).</summary>
    public int IntelLive()
    {
        var now = Environment.TickCount64;
        for (var i = IntelMarks.Count - 1; i >= 0; i--)
            if (IntelMarks[i].Until < now)
                IntelMarks.RemoveAt(i); // (no lambda: this runs every frame)
        return IntelMarks.Count;
    }

    // ---- Lines for checks and window verbs ---------------------------------------------------------------------------------------------

    /// <summary>Every group (allocates: a verb's).</summary>
    public string CrewFleetLines()
    {
        var lines = new List<string>();
        foreach (var g in Crew.Fleet.Groups)
            if (g.Live)
                lines.Add($"group {g.Number} ({g.Task}, {CrewPowers.Of(g.Power)?.Name}): Admiral {(g.AdmiralUid != 0 ? CrewNames.Of(g.AdmiralName) : "none")} aboard {g.Flagship}, {g.Count} ships [{string.Join(", ", g.Ships.Take(g.Count).Select(b => $"{b} {Crew.ShipOf(b)?.Order.Kind}"))}], order {g.Order}, strength {g.Strength:0} vs {g.Enemy:0}{(g.Retreating ? ", pulling back" : "")}, on the net {g.OnNet}");
        return lines.Count > 0 ? string.Join(" | ", lines) : "no groups";
    }

    /// <summary>A verb's: the first officer prisoner held by <paramref name="me"/>, questioned or ransomed.</summary>
    public string CrewOfficerFirst(Player me, string op)
    {
        foreach (var (uid, o) in _officers)
            if (Crew.ShipOf(o.Ship) is { } s)
                return (op == "ransom" ? CrewRansom(me, s.Body, uid) : CrewInterrogate(me, s.Body, uid)) ?? $"{op}: under way for {(o.Admiral ? "an admiral" : "a captain")} ({uid})";
        return "no officer prisoner";
    }
}
