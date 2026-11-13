using System.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The break-out's last part (docs/story/BREAKOUT.md "The ship"): an escapee who sits down in the Ministry Courier
/// (GameSpaceport.cs; stolen, or handed over) takes the convicts who are alive and near it aboard. Each says their line,
/// gains <c>Aboard</c> and their body goes (they ride inside: <see cref="BreakoutAboard"/> keeps it away). Once the
/// courier is far from the spaceport (<see cref="BreakoutAwayAt"/>, or gone from this world into space) the crew are
/// <c>Free</c>, and so is every escapee who carried them: the file's "away" lines are heard, a memo goes round, and the
/// break-out is over for them (<see cref="BreakoutAway"/>). The courier is a real ship: where it goes next is the
/// player's. Host only, with the break-out's ten looks a second. No wire change beyond the new stage's value.
/// </summary>
public sealed partial class Game
{
    public const byte BreakoutAway = 3;
    public const float BreakoutCrewBoardReach = 40f; // m from the courier: a convict this near goes aboard with the pilot
    public const float BreakoutAwayAt = 1500f; // m from the spaceport: the courier is away

    private int _bsQAboard = -1, _bsQFree = -1;
    private StoryEngine? _bsFor;
    private Player? _bsCarrier; // the escapee last seen seated in the courier with the crew aboard

    /// <summary>The ship's quality ids, looked up with the break-out's own (<see cref="BreakoutIds"/>).</summary>
    private void BreakoutShipIds(StoryEngine s)
    {
        _bsFor = s;
        _bsQAboard = s.Rules.Quality("Aboard")?.Id ?? -1;
        _bsQFree = s.Rules.Quality("Free")?.Id ?? -1;
        _bsCarrier = null;
    }

    /// <summary>A crew member riding in the courier: no body (GameStoryBodies.cs).</summary>
    private bool BreakoutAboard(int entity)
    {
        if (_bkCrewOf == null || _bkCrewOf != Story || _bsFor != _bkCrewOf || _bsQAboard < 0 || entity < 0)
            return false;
        for (var i = 0; i < _bkCrew.Length; i++)
            if (_bkCrew[i] == entity)
                return _bkCrewOf.World.Quality(entity, _bsQAboard) > 0;
        return false;
    }

    /// <summary>The courier (whoever owns it now), or null.</summary>
    private Construct? BreakoutCourier()
    {
        foreach (var (n, b) in _cbBuilt)
            if (n.Equals(SpaceportScript.Current.Courier, StringComparison.OrdinalIgnoreCase) && Constructs.Get(b) is { } c)
                return c;
        return null;
    }

    /// <summary>Server, a player at the spaceport, each look (from <see cref="BreakoutTick"/>): aboard, and away.</summary>
    private void BreakoutShipLook(StoryEngine s, Player p, ref BreakoutRun run, int who)
    {
        if (_bsFor != s || _bsQAboard < 0 || _bsQFree < 0)
            return;
        if (StoryWho(p) is var pw and >= 0 && s.World.Quality(pw, _bsQFree) > 0)
        {
            run.Stage = BreakoutAway; // (carried them off and came back: the break-out is done for them)
            BreakoutSend(p);
            return;
        }
        var courier = BreakoutCourier();
        ref var h = ref SandboxHands.Hand(p.Id);
        var seated = courier != null && h.Seated && h.SeatBody == courier.Id;
        var script = BreakoutScript.Current;
        var any = false;
        for (var i = 0; i < _bkCrew.Length; i++)
        {
            var e = _bkCrew[i];
            if (e < 0 || s.Agents.Of(e) is not { Dead: false } || s.World.Quality(e, _bkQFled) == 0 || s.World.Quality(e, _bsQFree) > 0)
                continue;
            if (s.World.Quality(e, _bsQAboard) > 0)
            {
                any = true;
                continue;
            }
            if (!seated || courier == null)
                continue;
            var body = StoryBodyOf(e);
            if (body != null && (!body.Alive || Vector3.Distance(body.Position, courier.WorldCom) > BreakoutCrewBoardReach))
                continue;
            s.World.SetQuality(e, _bsQAboard, 1);
            any = true;
            if (body != null && script.Ship.Aboard.Length > 0)
                StorySpeak(body, script.Ship.Aboard, SandboxNow);
            Log($"[breakout] {s.World.NameOf(e)} goes aboard the courier");
        }
        if (any && seated)
            _bsCarrier = p;
    }

    /// <summary>
    /// Server, each look: the crew aboard and the courier far from the spaceport, or gone from this world (into space,
    /// its pilot with it): they are away.
    /// </summary>
    private void BreakoutShipAwayTick(StoryEngine s)
    {
        if (_bsFor != s || _bsQAboard < 0 || _bsQFree < 0 || _bsCarrier is not { } carrier)
            return;
        var aboard = false;
        for (var i = 0; i < _bkCrew.Length && !aboard; i++)
            aboard = _bkCrew[i] >= 0 && s.World.Quality(_bkCrew[i], _bsQAboard) > 0 && s.World.Quality(_bkCrew[i], _bsQFree) == 0;
        if (!aboard)
            return;
        var courier = BreakoutCourier();
        if (courier != null && SpaceportFrame(out var origin, out _) && Flat2(courier.WorldCom - origin) <= BreakoutAwayAt * BreakoutAwayAt)
            return;
        BreakoutGoAway(s, carrier);
    }

    /// <summary>The courier is away with the crew aboard: everyone aboard is free, and so is whoever flew them.</summary>
    private void BreakoutGoAway(StoryEngine s, Player p)
    {
        var script = BreakoutScript.Current;
        for (var i = 0; i < _bkCrew.Length; i++)
        {
            var e = _bkCrew[i];
            if (e < 0 || s.World.Quality(e, _bsQAboard) == 0 || s.World.Quality(e, _bsQFree) > 0)
                continue;
            s.World.SetQuality(e, _bsQFree, 1);
            foreach (var (name, line) in script.Ship.Away)
                if (name.Equals(s.World.NameOf(e), StringComparison.OrdinalIgnoreCase))
                    ServerSystemChat($"{name}: {line}", SandboxNow); // (inside the cabin: everyone aboard hears it)
        }
        // The pilot is free wherever they are now (in space, another world: their stage follows when they're back here).
        if (StoryWho(p) is var pw and >= 0)
            s.World.SetQuality(pw, _bsQFree, 1);
        _bsCarrier = null;
        foreach (var q in Players.Values)
        {
            if (q != p && q.Bot == null && q.Id < MaxPlayers && _bkWho[q.Id] == q && _bk[q.Id].Stage == BreakoutSpaceport
                && SandboxHands.Hand(q.Id) is { Seated: true } sq && BreakoutCourier() is { } cq && sq.SeatBody == cq.Id && StoryWho(q) is var qw and >= 0)
                s.World.SetQuality(qw, _bsQFree, 1); // (a passenger)
            // Whoever is in the courier with them (the pilot, and anyone else seated in it) is away too.
            if (q.Bot != null || q.Id >= MaxPlayers || _bkWho[q.Id] != q || _bk[q.Id].Stage != BreakoutSpaceport)
                continue;
            ref var hq = ref SandboxHands.Hand(q.Id);
            if (q != p && !(hq.Seated && BreakoutCourier() is { } c && hq.SeatBody == c.Id))
                continue;
            _bk[q.Id].Stage = BreakoutAway;
            BreakoutSend(q);
            Log($"[breakout] {q.Name} is away in the courier with the crew");
        }
        if (script.Ship.Memo.Length > 0)
            ServerSystemChat(script.Ship.Memo.Replace("{name}", p.Name), SandboxNow);
    }

    /// <summary>A loaded world or a joiner: a player who is Free is away (from <see cref="BreakoutTick"/>).</summary>
    private bool BreakoutFreeOf(StoryEngine s, int who) =>
        (s.Rules.Quality("Free")?.Id ?? -1) is var q and >= 0 && s.World.Quality(who, q) > 0;
}
