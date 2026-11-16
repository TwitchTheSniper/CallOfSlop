using System.Numerics;

namespace CallOfSlop;

/// <summary>Ships with an AI crew (ConstructShipAi.cs; torpedo-frigate.md "The crew"): which bodies have one, who each is after, and their seats' keys each tick. Server only.</summary>
public sealed partial class Game
{
    private readonly Dictionary<ushort, ShipAi> _shipAi = new();
    private readonly List<ushort> _shipAiGone = new();
    private int _shipAiTick;

    /// <summary>A tick, after the players' seats and before the constructs step: once a second it looks for crewed ships, then each works its seats.</summary>
    private void SandboxShipAiTick()
    {
        if (++_shipAiTick % World.TickRate == 0)
            ShipAiFind();
        if (_shipAi.Count == 0)
            return;
        _shipAiGone.Clear();
        foreach (var (id, ai) in _shipAi)
        {
            if (Constructs.Get(id) is not { } c || c.Wreck || c.SpaceShip != 0)
            {
                if (Constructs.Get(id) is { } left)
                    ai.Release(left);
                _shipAiGone.Add(id);
                continue;
            }
            if (ai.Boarded(c))
                continue; // (a player has the helm; the next look stands the crew down)
            var has = ShipAiTarget(c, ai.Mode, out var t);
            ai.Step(Constructs, c, World.Query, has, t);
        }
        foreach (var id in _shipAiGone)
            _shipAi.Remove(id);
    }

    /// <summary>Which bodies have a crew now: a Flight Stabilizer set to one, with nobody at its helm.</summary>
    private void ShipAiFind()
    {
        foreach (var c in Constructs.Bodies)
        {
            if (c.SpaceShip != 0 || c.StabCount == 0 && !_shipAi.ContainsKey(c.Id))
                continue; // (a ship out in space has no crew at work: the tick lets it go)
            var mode = c.Wreck ? ShipAiMode.Off : ShipAi.ModeOf(c);
            _shipAi.TryGetValue(c.Id, out var ai);
            if (mode != ShipAiMode.Off && ai == null)
            {
                _shipAi[c.Id] = ai = new ShipAi(c.Id, mode);
                Constructs.Events.Add($"construct {c.Id}: an AI crew took it ({mode})");
            }
            if (ai == null)
                continue;
            ai.Mode = mode;
            if (mode == ShipAiMode.Off || ai.Boarded(c))
            {
                ai.Release(c);
                if (mode == ShipAiMode.Off)
                {
                    _shipAi.Remove(c.Id);
                    Constructs.Events.Add($"construct {c.Id}: its AI crew stood down");
                }
            }
        }
    }

    /// <summary>The nearest living player a crew fights within its reach, as what it shoots at: the body they sit or ride in, else themselves. False: none.</summary>
    private bool ShipAiTarget(Construct c, ShipAiMode mode, out ShipAiTarget t)
    {
        t = default;
        var com = c.WorldCom;
        var best = ShipAi.EngageRange * ShipAi.EngageRange;
        var found = false;
        foreach (var p in Players.Values)
        {
            if (!p.Alive || mode == ShipAiMode.Strangers && Factions.Shares(c.Owner, p.Id))
                continue;
            ref var h = ref SandboxHands.Hand(p.Id);
            var body = h.SeatBody != 0 ? h.SeatBody : p.Sim.RideBody;
            if (body == c.Id)
                continue; // (aboard it: a boarder is the crew's business, not its guns')
            var at = p.Position;
            var vel = p.Sim.Velocity;
            if (body != 0 && Constructs.Get(body) is { } b)
            {
                at = b.WorldCom;
                vel = b.Vel;
            }
            else
            {
                body = 0;
                at = GravityFrames.Above(at, 1.2f);
            }
            var d2 = Vector3.DistanceSquared(at, com);
            if (d2 >= best)
                continue;
            best = d2;
            found = true;
            t = new ShipAiTarget { At = at, Vel = vel, Body = body, Player = p.Id };
        }
        return found;
    }
}
