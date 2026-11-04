namespace CallOfSlop;

/// <summary>
/// Free travel on the traveller's side (docs/space/FREE-TRAVEL.md, Game/GameTravel.cs): when the host hands this PC a
/// planetoid, a game of its own runs it (<see cref="_session"/>, drawn and steered as ever) while the game connected to
/// the host stays open behind it (<see cref="_link"/>: space, chat, who is where). When the ship flies out of that
/// planetoid's sky, what it packed goes back through the link and the link is the session again.
/// </summary>
public static unsafe partial class Program
{
    /// <summary>The game connected to the host while a planetoid is kept on this PC (null: not travelling).</summary>
    private static Game? _link;

    /// <summary><c>--free-travel</c> (dev): hosting, a ship with one client aboard lands on that client's own game.</summary>
    private static bool _freeTravel;

    /// <summary>After the session's update: a planetoid to open, the link's own update, the way home.</summary>
    private static void TravelTick(double now)
    {
        if (_link == null && _session is { PendingTravel: { } open } host)
        {
            host.PendingTravel = null;
            if (Game.OpenKeeper(host, open, out var why) is { } keeper)
            {
                _link = host;
                _session = keeper;
                Ui.MenuOpen = false;
                _deathCam.Stop();
            }
            else
                Console.WriteLine($"[travel] couldn't open {open.Map}: {why}");
        }
        if (_link is not { } link || _session is not { } kept)
            return;
        if (link.Disconnected)
        {
            Console.WriteLine($"[travel] the host went ({link.Status}): {kept.Keep?.Map} closes with it");
            link.Dispose();
            _link = null;
            EndSession(link.Status);
            return;
        }
        link.Update(now, FrameDt);
        if (kept.PendingReturn is { } back)
        {
            link.TravelReturn(back);
            kept.Dispose();
            _session = link;
            _link = null;
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
