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
