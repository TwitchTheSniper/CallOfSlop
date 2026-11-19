using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Players as admirals (docs/sandbox/design/faction-controllers.md phase 6, section 7): a player's own hired crews as a group, the player or one
/// of their hands as its admiral, through the same admiral code as the powers' groups (CrewFleet.cs). The game (Game/GameCrewFleetPlayers.cs)
/// checks who may command and writes where the commander is; this holds the group's side. Server only; allocates only on a player's act.
/// </summary>
public sealed partial class CrewFleet
{
    /// <summary>What players' groups have to tell their owners (by name: an admiral lost, command passed), drained by the game.</summary>
    public readonly List<(ulong Owner, string Text)> Notices = new(16);

    /// <summary>A player's escort engages a threat this close to what it guards (m: past the guns that hit a ward, a Zero Day's from 760 m); further off
    /// it keeps its station and its gunners fire from there. A power's escort fights whatever it sees, as in phase 2.</summary>
    public const float EscortBand = 1800;

    /// <summary>A group's news: a power's to everyone, a player's to its owner.</summary>
    private void Say(CrewGroup g, string text)
    {
        if (g.Players)
            Notices.Add((g.OwnerKey, text));
        else
            News.Add(text);
    }

    /// <summary>
    /// A group of <paramref name="owner"/>'s crewed bodies (their own hired crews; the game has checked they are theirs), the player its admiral. -1
    /// (with why): no slot, or no own crew among them.
    /// </summary>
    public int FormPlayer(CrewWorld crew, ConstructWorld w, string ownerName, ulong ownerKey, byte owner, ReadOnlySpan<ushort> bodies, out string? why)
    {
        why = null;
        var slot = -1;
        for (var i = 0; i < MaxGroups && slot < 0; i++)
            if (!Groups[i].Live)
                slot = i;
        if (slot < 0)
        {
            why = $"all {MaxGroups} fleet groups are in use";
            return -1;
        }
        var g = Groups[slot];
        g.Clear();
        foreach (var b in bodies)
        {
            if (g.Count >= CrewGroup.MaxShips)
            {
                why = $"a group takes {CrewGroup.MaxShips} ships at most";
                break;
            }
            if (crew.ShipOf(b) is not { Own: true, Prize: false } s || w.Get(b) is not { Wreck: false } c)
                continue;
            if (s.Group >= 0)
                Leave(crew, s);
            Look(crew, c, s);
            g.Ships[g.Count++] = b;
            s.Group = (short)slot;
            g.Power = s.Faction;
        }
        if (g.Count == 0)
        {
            g.Clear();
            why = "none of those ships has a hired crew of yours";
            return -1;
        }
        g.Live = true;
        g.Serial++;
        g.OwnerName = ownerName;
        g.OwnerKey = ownerKey;
        g.Owner = owner;
        g.PlayerAdmiral = true;
        g.Task = GroupTask.Patrol;
        g.Doctrine = Rules.DoctrineOf(g.Power).Copy();
        g.Number = (ushort)++_number;
        g.Order = OrderKind.Hold;
        g.ThinkAt = 0;
        // The flagship (the formation's anchor): a ship with a radio first, then the strongest.
        var best = 0;
        var bestScore = float.MinValue;
        for (var k = 0; k < g.Count; k++)
        {
            var s = crew.ShipOf(g.Ships[k])!;
            var score = s.Strength + (s.NetCount > 0 ? 1e6f : 0);
            if (score > bestScore)
                (bestScore, best) = (score, k);
        }
        g.Flagship = g.Ships[best];
        if (w.Get(g.Flagship) is { } f)
            g.Home = g.OrderAt = f.WorldCom;
        foreach (var b in g.Ships.AsSpan(0, g.Count))
        {
            g.Strength0 += crew.ShipOf(b)!.Strength;
            crew.ShipOf(b)!.SeatsDirty = true;
        }
        Live++;
        crew.Events.Add($"fleet: {ownerName}'s group {g.Number} formed: {g.Count} ships, {ownerName} the admiral, flagship construct {g.Flagship}");
        return slot;
    }

    /// <summary>A ship with a hired crew of the group's owner joins it. Null: done; else why not.</summary>
    public string? JoinPlayer(CrewWorld crew, ConstructWorld w, CrewGroup g, ushort body)
    {
        if (crew.ShipOf(body) is not { Own: true, Prize: false } s || w.Get(body) is not { Wreck: false } c)
            return "that ship has no hired crew of yours";
        if (s.Group == g.Slot)
            return "it is in that group already";
        if (g.Count >= CrewGroup.MaxShips)
            return $"a group takes {CrewGroup.MaxShips} ships at most";
        if (s.Group >= 0)
            Leave(crew, s);
        Look(crew, c, s);
        g.Ships[g.Count++] = body;
        s.Group = (short)g.Slot;
        s.SeatsDirty = true;
        g.Strength0 += s.Strength;
        g.ThinkAt = 0;
        crew.Events.Add($"fleet: construct {body} joins {g.OwnerName}'s group {g.Number} ({g.Count} ships)");
        return null;
    }

    /// <summary>A ship leaves a player's group (its hands stand down from the helm). Null: done.</summary>
    public string? LeavePlayer(CrewWorld crew, CrewGroup g, ushort body)
    {
        if (crew.ShipOf(body) is not { } s || s.Group != g.Slot)
            return "that ship isn't in the group";
        var admiralAboard = !g.PlayerAdmiral && g.AdmiralUid != 0 && Holds(crew, s, g.AdmiralUid) >= 0;
        if (admiralAboard)
            SetAdmiral(crew, g, 0); // (its admiral goes with the ship: the player takes command)
        Leave(crew, s);
        s.SeatsDirty = true;
        if (g.Live && g.Flagship == body)
            g.Flagship = g.Ships[0];
        return null;
    }

    /// <summary>The player's group goes: each captain is its own again, its hands off the helm.</summary>
    public void Disband(CrewWorld crew, CrewGroup g) => Dissolve(crew, g, $"{g.OwnerName} stood it down");

    private static int Holds(CrewWorld crew, CrewShip s, uint uid)
    {
        for (var i = 0; i < s.MemberCount; i++)
            if (crew.Uid[s.Members[i]] == uid)
                return s.Members[i];
        return -1;
    }

    /// <summary>
    /// The group's admiral: the player (uid 0), or one of their hands aboard a member (it keeps command while the player is away, and the player's
    /// orders reach it over the net). Null: done; else why not.
    /// </summary>
    public string? SetAdmiral(CrewWorld crew, CrewGroup g, uint uid)
    {
        int m = -1;
        CrewShip? on = null;
        if (uid != 0)
        {
            for (var k = 0; k < g.Count && m < 0; k++)
                if (crew.ShipOf(g.Ships[k]) is { } s && Holds(crew, s, uid) is var i and >= 0)
                    (m, on) = (i, s);
            if (m < 0 || on == null)
                return "no such hand aboard the group's ships";
            if (crew.State[m] is CrewState.Dead or CrewState.Surrendered)
                return "they can't lead";
        }
        // The old admiral back to a hand.
        if (g.AdmiralUid != 0)
            for (var k = 0; k < g.Count; k++)
                if (crew.ShipOf(g.Ships[k]) is { } s && Holds(crew, s, g.AdmiralUid) is var old and >= 0)
                {
                    if (crew.Job[old] == CrewJob.Admiral)
                        crew.Job[old] = CrewJob.Spare;
                    s.SeatsDirty = s.RosterDirty = true;
                }
        g.AdmiralUid = 0;
        g.AdmiralName = 0xFFFF;
        g.PlayerAdmiral = m < 0;
        g.DisarrayUntil = 0;
        if (m >= 0 && on != null)
        {
            crew.Job[m] = CrewJob.Admiral;
            g.AdmiralUid = crew.Uid[m];
            g.AdmiralName = crew.Name[m];
            g.Flagship = on.Body;
            on.SeatsDirty = on.RosterDirty = true;
            if (on.CaptainUid == uid)
            {
                on.CaptainUid = 0;
                CaptainOf(crew, on);
            }
        }
        g.ThinkAt = 0;
        crew.Events.Add($"fleet: {g.OwnerName}'s group {g.Number}: {(m >= 0 ? $"Admiral {CrewNames.Of(g.AdmiralName)} aboard construct {g.Flagship}" : $"{g.OwnerName} the admiral")}");
        return null;
    }

    /// <summary>The ship the formation forms on. Null: done.</summary>
    public string? SetFlagship(CrewWorld crew, CrewGroup g, ushort body)
    {
        if (crew.ShipOf(body) is not { } s || s.Group != g.Slot)
            return "that ship isn't in the group";
        if (!g.PlayerAdmiral && g.AdmiralUid != 0)
            return "the flagship is the ship its admiral is aboard: make the admiral another hand, or yourself";
        g.Flagship = body;
        g.ThinkAt = 0;
        return null;
    }

    /// <summary>
    /// Does the commander (the game's last word on where the player is and how far they reach) reach the group's net: any member, for the player as
    /// admiral, else the admiral's ship (through members in reach of each other)? Their orders go only over the net.
    /// </summary>
    public bool CommanderReaches(CrewWorld crew, ConstructWorld w, CrewGroup g)
    {
        if (!g.CommanderHere || g.Count == 0)
            return false;
        var n = 0;
        var root = -1;
        for (var k = 0; k < g.Count; k++)
        {
            if (crew.ShipOf(g.Ships[k]) is not { } s || w.Get(g.Ships[k]) is not { } c)
                continue;
            _pos[n] = c.WorldCom;
            _reach[n] = Reach(c, s);
            if (g.Ships[k] == g.Flagship)
                root = n;
            n++;
        }
        _pos[n] = g.CommanderAt;
        _reach[n] = g.CommanderReach;
        Spread(n + 1, n);
        return g.PlayerAdmiral ? AnyHeard(n) : root >= 0 && _heard[root];
    }

    private bool AnyHeard(int n)
    {
        for (var k = 0; k < n; k++)
            if (_heard[k])
                return true;
        return false;
    }

    /// <summary>Who hears node <paramref name="root"/> of the first <paramref name="n"/> (positions and reaches in _pos, _reach): through each other, a chain.</summary>
    private void Spread(int n, int root)
    {
        for (var k = 0; k < n; k++)
            _heard[k] = k == root;
        for (var changed = true; changed;)
        {
            changed = false;
            for (var a = 0; a < n; a++)
                if (_heard[a])
                    for (var b = 0; b < n; b++)
                    {
                        if (_heard[b])
                            continue;
                        var r = MathF.Max(_reach[a], _reach[b]);
                        if (Vector3.DistanceSquared(_pos[a], _pos[b]) <= r * r)
                            _heard[b] = changed = true;
                    }
        }
    }

    /// <summary>A player's group's order from the command view (the game has checked the player may give it and reaches the net).</summary>
    public void CommandPlayer(CrewGroup g, OrderKind kind, Vector3 at, ushort body)
    {
        g.Order = kind;
        g.OrderAt = at;
        g.OrderBody = body;
        g.Retreating = false;
        if (kind != OrderKind.Route)
            g.RouteCount = 0;
        g.ThinkAt = 0;
    }

    /// <summary>A stop added to a player's group's route (at most <see cref="CrewGroup.MaxRoute"/>); the group follows it, looped. Null: done.</summary>
    public string? AddStop(CrewGroup g, Vector3 at, OrderKind work, float dwell)
    {
        if (g.Order != OrderKind.Route)
        {
            g.RouteCount = 0;
            g.RouteAt = 0;
        }
        if (g.RouteCount >= CrewGroup.MaxRoute)
            return $"a route has {CrewGroup.MaxRoute} stops at most";
        g.Route[g.RouteCount] = at;
        g.RouteWork[g.RouteCount++] = work;
        g.Dwell = dwell;
        g.DwellUntil = 0;
        g.Loop = true;
        g.Order = OrderKind.Route;
        g.OrderBody = 0;
        g.ThinkAt = 0;
        return null;
    }
}
