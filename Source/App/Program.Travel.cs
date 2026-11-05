namespace CallOfSlop;

/// <summary>
/// Free travel on the traveller's side (docs/space/FREE-TRAVEL.md, Game/GameTravel.cs, GameTravelMeet.cs): when the host
/// hands this PC a planetoid, or sends it to visit one kept on someone else's PC, a game of its own plays there
/// (<see cref="_session"/>, drawn and steered as ever) while the game connected to the host stays open behind it
/// (<see cref="_link"/>: space, chat, who is where, and the relay a keeper and its visitors talk through). When the ship
/// flies out of that planetoid's sky, the link is the session again.
/// </summary>
public static unsafe partial class Program
{
    /// <summary>The game connected to the host while this PC keeps or visits a planetoid (null: not travelling).</summary>
    private static Game? _link;

    /// <summary><c>--free-travel</c> (dev): hosting, a ship with one client aboard lands on that client's own game.</summary>
    private static bool _freeTravel;

    /// <summary>After the session's update: the link's own update, then a planetoid to open or visit, or the way home.</summary>
    private static void TravelTick(double now)
    {
        if (_session is not { } session)
            return;
        var link = _link ?? session;
        if (link.IsServer)
            return; // (the host's own game is the world: it never travels alone)
        var here = _link != null ? session : null;
        if (here != null)
        {
            if (link.Disconnected)
            {
                Console.WriteLine($"[travel] the host went ({link.Status}): {here.Keep?.Map ?? here.Visit?.Map} closes with it");
                var why = link.Status;
                _session = link;
                _link = null;
                here.Dispose();
                EndSession(why);
                return;
            }
            link.Update(now, FrameDt);
        }
        var next = Game.TravelStep(link, here, out var said);
        if (said != null)
            Console.WriteLine($"[travel] {said}");
        if (!ReferenceEquals(next, session))
        {
            _session = next;
            _link = ReferenceEquals(next, link) ? null : link;
            Ui.MenuOpen = false;
            _deathCam.Stop();
        }
        World.Enter(_session.Place); // (drawing and input read the session's world)
    }

    /// <summary>The session ends: the link goes with it.</summary>
    private static void TravelEnd()
    {
        if (_link == null)
            return;
        _link.Dispose();
        _link = null;
    }
}
