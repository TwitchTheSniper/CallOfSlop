using System.Numerics;

namespace CallOfSlop;

/// <summary>A player's firsts (GameMilestones.cs). Saved as bits: a new one goes at the end and none is ever renumbered.</summary>
public enum Milestone : byte
{
    Ship, // first flight in a craft of their own (or their faction's) that has thrusters: off the ground by MilestoneLift
    Space, // first time off-planet: seated in a craft as it leaves the sky (GameSpaceLaunch.cs)
    Landing, // first landing on another planetoid (GameSpaceLanding.cs)
    Shell, // first time awake in a robot shell (GameRobotShell.cs)
    COUNT,
}

/// <summary>
/// Milestones for firsts (Karl, 2026-11-13): the first flight in your own craft, the first time off-planet, the first landing on
/// another planetoid, the first robot shell. Each is reached once per player per world, celebrated in the middle of their
/// screen (<see cref="Celebrate"/>), kept in their record in the saved world (the Firsts section) and readable by other code
/// (<see cref="HasFirst"/>, <see cref="FirstReachedThen"/>: the objective tracker, anything a first should unlock). Server only;
/// the flight check runs once a second over seated players and allocates nothing.
/// </summary>
public sealed partial class Game
{
    /// <summary>How far a craft must climb from where its pilot first sat in it (m) to count as a first flight.</summary>
    public const float MilestoneLift = 15f;

    private readonly ushort[] _firsts = new ushort[256]; // per player slot: a bit per Milestone
    private readonly (ushort Body, float Low)[] _flight = new (ushort, float)[256]; // per player slot: the craft they sit in, its lowest height seen

    /// <summary>Server: called as a player reaches a first (after it is kept, before it is celebrated): the objective tracker, unlocks.</summary>
    public Action<Player, Milestone>? FirstReachedThen;

    /// <summary>Server: has this player reached this first in this world?</summary>
    public bool HasFirst(byte player, Milestone m) => (_firsts[player] & (1 << (int)m)) != 0;

    /// <summary>Server: a player reached a first: kept, said in the log, celebrated on their screen. Nothing the second time.</summary>
    private void FirstReached(Player p, Milestone m, string place = "")
    {
        if (!IsServer || p.Bot != null || HasFirst(p.Id, m))
            return;
        _firsts[p.Id] |= (ushort)(1 << (int)m);
        FirstReachedThen?.Invoke(p, m);
        Console.WriteLine($"[first] {p.Name}: {m}{(place.Length > 0 ? $" ({place})" : "")}");
        var (title, name, line) = m switch
        {
            Milestone.Ship => ("FIRST FLIGHT", "Your own craft flies", "Something you built has carried you off the ground."),
            Milestone.Space => ("FIRST TIME OFF-PLANET", place.Length > 0 ? $"Above {place}" : "Into space", "You left the sky behind."),
            Milestone.Landing => ("FIRST LANDING", place.Length > 0 ? place : "Another world", "You set down on another world."),
            _ => ("FIRST ROBOT SHELL", "Linked", "You woke in a robot shell. If it breaks, you wake in the chair."),
        };
        Celebrate(p.Id, title, name, line);
    }

    /// <summary>Server: everyone seated in this craft reaches a first.</summary>
    private void FirstForRiders(Construct c, Milestone m, string place)
    {
        foreach (var p in Players.Values)
            if (p.Bot == null && SeatedIn(p, c))
                FirstReached(p, m, place);
    }

    /// <summary>Server, once a second: a player seated in a craft of theirs that has thrusters, which has climbed <see cref="MilestoneLift"/> since they sat, has flown it.</summary>
    private void FirstsTick()
    {
        if (ServerTick % World.TickRate != 0)
            return;
        foreach (var p in Players.Values)
        {
            ref var fl = ref _flight[p.Id];
            if (p.Bot != null || HasFirst(p.Id, Milestone.Ship))
                continue;
            ref var h = ref SandboxHands.Hand(p.Id);
            if (!p.Alive || !h.Seated || Constructs.Get(h.SeatBody) is not { } c || c.ThrusterCount == 0 || !OwnsCraft(p, c)
                || _dpWho[p.Id] != null && _dp[p.Id].Lander == c.Id) // (not the lander the depot lent them: GameDepot.cs)
            {
                fl = default;
                continue;
            }
            var height = ConstructPhysics.HoldHeight(c.WorldCom);
            if (fl.Body != c.Id)
                fl = (c.Id, height);
            else if (height < fl.Low)
                fl.Low = height;
            else if (height - fl.Low >= MilestoneLift || c.SpaceShip != 0)
                FirstReached(p, Milestone.Ship);
        }
    }

    /// <summary>Is this craft the player's own, or their faction's?</summary>
    private bool OwnsCraft(Player p, Construct c) =>
        c.Owner == p.Id || (FactionTable.Of[p.Id] != 0 && FactionTable.Of[c.Owner] == FactionTable.Of[p.Id]);

    /// <summary>Both sides: one player's firsts forgotten (a new match, a new player in the slot; a saved world's record brings theirs back).</summary>
    private void FirstsResetPlayer(byte id)
    {
        _firsts[id] = 0;
        _flight[id] = default;
    }

    /// <summary>Server: every player's firsts forgotten (a new match; a saved world's record brings theirs back).</summary>
    private void FirstsReset()
    {
        Array.Clear(_firsts);
        Array.Clear(_flight);
    }

    /// <summary>Server: what goes in a person's record when the world is saved or they leave.</summary>
    private void FirstsCatch(Player p, WorldPlayer rec) => rec.Firsts = _firsts[p.Id];

    /// <summary>Server: a person arrived in a saved world: their firsts from their record.</summary>
    private void FirstsArrive(Player p, WorldPlayer rec)
    {
        _firsts[p.Id] = rec.Firsts;
        _flight[p.Id] = default;
    }

    /// <summary>A saved world's firsts (WorldSaveWriter.FirstsTag): each person's bits, onto their record.</summary>
    private static void WorldReadFirsts(SaveReader r, WorldSession ws)
    {
        var n = r.Count(18);
        for (var k = 0; k < n; k++)
        {
            var key = r.U64();
            var steam = r.U64();
            var bits = r.U16();
            foreach (var rec in ws.Players)
                if (rec.Key == key && rec.SteamId == steam)
                {
                    rec.Firsts = bits;
                    break;
                }
        }
    }
}
