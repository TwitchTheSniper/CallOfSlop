using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Creative Sandbox's noclip (docs/sandbox/CONSTRUCTS.md, "Noclip"; the flight is <see cref="NoclipMove"/>). The key sets
/// <see cref="Player.NoclipWanted"/>, which every input carries as a level, not an event: a lost packet costs nothing, the
/// client predicts going in on the very input the server goes in on, and the wish is checked against
/// <see cref="NoclipAllowed"/> wherever an input is simulated, so nothing a client sends turns it on where it isn't allowed.
/// </summary>
public sealed partial class Game
{
    /// <summary>
    /// Is this a creative Sandbox world (<see cref="SandboxInfiniteParts"/>: what the physics gun and the remover call creative)?
    /// The map and the host's lobby option, which every peer holds from the Welcome on, so a client's prediction agrees with the server from its first tick.
    /// </summary>
    public bool NoclipCreative => SandboxOn && SandboxInfiniteParts;

    /// <summary>May this player fly? Creative Sandbox, in play, alive and not in a seat (a seat ends it).</summary>
    /// <summary>The window-shot tool's camera may fly in a survival world too (Program.WindowShot.cs: <c>--at</c>).</summary>
    internal bool NoclipShot;

    public bool NoclipAllowed(Player p) => (NoclipCreative || NoclipShot) && Phase == MatchPhase.Playing && p.Alive && p.Bot == null && !SandboxSeated(p);

    /// <summary>The local player's noclip key. Refused keeps the wish off, so it can't start later by itself.</summary>
    public void ToggleNoclip()
    {
        if (Local is { } me)
            me.NoclipWanted = !me.NoclipWanted && NoclipAllowed(me);
    }

    // Server: each player's search for a floor inside the construct they are held in (MoveState.NoclipHold), by id.
    private readonly ConstructRide.InteriorSearch?[] _holdSearch = new ConstructRide.InteriorSearch?[256];

    /// <summary>
    /// Server, after everyone's moves: a player held in a construct's frame for a set-down inside (out of noclip in its hull,
    /// or the Board inside key) has the search for a floor stepped on, <see cref="ConstructRide.InteriorBudgetMs"/> a tick at
    /// most; found, they are set down there, standing in its frame (a snapshot says so and the client's prediction follows);
    /// none, out of it as anyone leaving noclip is (<see cref="NoclipMove.Leave"/>). One search per player at a time.
    /// </summary>
    private void NoclipHoldTick()
    {
        foreach (var p in Players.Values)
        {
            var search = _holdSearch[p.Id];
            ref var s = ref p.Sim;
            if (!s.NoclipHold || !s.Noclip || !p.Alive || Constructs.Get(s.RideBody) is not { PartCount: > 0 } c)
            {
                if (search is { Active: true })
                    search.Stop();
                if (s.NoclipHold && (!s.Noclip || !p.Alive))
                    s.NoclipHold = false;
                continue;
            }
            if (c.Dirty)
                c.Rebuild();
            search ??= _holdSearch[p.Id] = new ConstructRide.InteriorSearch();
            if (!search.Active || search.Body != c.Id || search.Parts != c.PartCount)
                search.Begin(Constructs, c, s.Position, World.HeightOf(Stance.Stand));
            if (!search.Step(ConstructRide.InteriorBudgetMs))
                continue;
            p.NoclipWanted = false;
            if (search.Found)
            {
                ConstructRide.NoclipSetDown(ref s, c, search.Spot);
                Constructs.Events.Add($"player {p.Id} set down inside construct {c.Id} at {search.Spot:0.0} ({search.Rays} rays, {search.Ticks} ticks, {search.WorstMs:0.00} ms at worst a tick)");
            }
            else
            {
                s.NoclipHold = s.Noclip = false;
                NoclipMove.Leave(ref s, MoverPhysics.Clock, Constructs); // (nowhere inside to stand: out of it, onto its deck)
                Constructs.Events.Add($"player {p.Id}: no floor inside construct {c.Id} ({search.Rays} rays), out of it instead");
            }
            p.PrevPosition = s.Position; // (drawn from the spot, not slid there from where they hung)
            (p.PrevRideBody, p.PrevRideSeg, p.PrevRideLocal) = (s.RideBody, s.RideSeg, s.RideLocal);
        }
    }

    // The Board inside key's ask, until the next input takes it (Program.Noclip.cs).
    private ushort _boardBody;
    private Vector3 _boardAt;

    /// <summary>
    /// The local player's Board inside key on construct <paramref name="body"/>, aimed at <paramref name="at"/> (its rest frame):
    /// noclip ends, and the next input carries the ask, so the server and this client's prediction set them down inside it
    /// in the same tick of the shared step (ConstructRideInterior.cs).
    /// </summary>
    public void NoclipBoardAsk(ushort body, Vector3 at)
    {
        if (Local is not { } me || !NoclipAllowed(me))
            return;
        me.NoclipWanted = false;
        (_boardBody, _boardAt) = (body, at);
    }

    /// <summary>The ask for the input being made, once.</summary>
    private ushort NoclipBoardTake(out Vector3 at)
    {
        at = _boardAt;
        var body = _boardBody;
        _boardBody = 0;
        return body;
    }

    /// <summary>The wish of one input, as the rules have it: the server's word on what a client sent, and the same rule for the client's prediction.</summary>
    private InputCmd NoclipFilter(Player p, InputCmd cmd)
    {
        if ((cmd.Noclip || cmd.NoclipBoard != 0) && !NoclipAllowed(p))
        {
            cmd.Noclip = false;
            cmd.NoclipBoard = 0; // (Board inside is creative's, as noclip is)
            p.NoclipWanted = false;
        }
        return cmd;
    }

    /// <summary>The tick someone left noclip (from <see cref="ZBlockPlayer"/>, the shared step): out of whatever they are in.</summary>
    private void NoclipLeft(ref MoveState s)
    {
        NoclipMove.Leave(ref s, _sepAtTick != 0 ? _sepAtTick : MoverPhysics.Clock, SandboxOn ? Constructs : null); // (in a construct's frame: on a floor inside it)
    }
}
