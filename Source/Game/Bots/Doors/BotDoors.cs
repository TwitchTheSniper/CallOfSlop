using System.Numerics;

namespace CallOfSlop;

/// <summary>Per-bot door state (Source/Game/Bots/Doors/BotDoors.cs).</summary>
public sealed partial class BotBrain
{
    internal double NextDoorUse; // the interact key's rate limit (a press at most every BotDoorUseGap)
    internal int TrackDoor = -1; // the open doorway it is next to...
    internal float TrackSide; // ...and which side of it it was on (signed metres from the door's plane, + = swing side)
    internal int PassDoor = -1; // the door it just walked through (a candidate to shut behind it), -1: none
    internal double PassAt;
    internal int Passes; // doorways walked through this session (varies the close roll)
    internal int HoldDoor = -1; // the door a holding bot went to shut (or shut), and when
    internal double HoldDoorAt = -100;
    internal double HoldShutUntil; // keeps that door shut while the hold lasts: no wandering back out through it
    internal double NextHoldDoorCheck;
    private readonly int[] _doorLogDoor = [-1, -1, -1, -1, -1, -1, -1, -1];
    private readonly double[] _doorLogTime = new double[8];
    private int _doorLogNext;

    /// <summary>Self-test: the chance of shutting a door behind it, instead of the personality's (null: the personality's).</summary>
    public float? SelfTestDoorCloseChance;

    /// <summary>The steering wish after the door code (self-test traces).</summary>
    public Vector3 LastDoorWish;

    /// <summary>Ticks this bot waited at a doorway for someone else (whole session).</summary>
    public int DoorWaitTicks;

    /// <summary>Doors this bot opened or shut (whole session).</summary>
    public int DoorToggles;

    /// <summary>How many times this bot opened or shut door <paramref name="door"/> in the last <paramref name="window"/> seconds.</summary>
    public int DoorTogglesIn(int door, double now, double window)
    {
        var n = 0;
        for (var k = 0; k < _doorLogDoor.Length; k++)
            if (_doorLogDoor[k] == door && now - _doorLogTime[k] < window)
                n++;
        return n;
    }

    internal void LogDoorToggle(int door, double now)
    {
        _doorLogDoor[_doorLogNext] = door;
        _doorLogTime[_doorLogNext] = now;
        _doorLogNext = (_doorLogNext + 1) % _doorLogDoor.Length;
        DoorToggles++;
    }

    /// <summary>How likely this personality is to shut a door behind it when nobody follows.</summary>
    internal float DoorCloseChance => SelfTestDoorCloseChance ?? Personality switch
    {
        BotPersonality.Anchor => 0.85f,
        BotPersonality.Tactician => 0.75f,
        BotPersonality.Marksman => 0.6f,
        BotPersonality.Flanker => 0.5f,
        BotPersonality.Chaotic => 0.3f,
        BotPersonality.Rusher => 0.08f,
        _ => 0.5f,
    };
}

/// <summary>
/// Bots, co-op enemies and zombies at building doors (GameBuildings.cs has the doors themselves).
///
/// Opening: a bot looks <see cref="BotDoorLook"/> m ahead along its route; a shut door across it is opened as soon as the
/// leaf is within the interact key's reach (<see cref="DoorReach"/>, in plain view), through the same server call a player's
/// key press makes (<see cref="ServerUseDoor"/>), at most one press every <see cref="BotDoorUseGap"/> s. An open door's
/// collision box is gone at once (the leaf's swing is drawing only), so a bot never has to stop and wait: it opens the door
/// about 2 m out and walks on. On the side a door swings into, a bot inside the leaf's arc while it swings keeps to the
/// latch side. In a doorway one body passes at a time: a bot waits a moment for someone already in the doorway (or, head on,
/// the lower id goes first). Broken doors are always open. Walking into a shut door still opens it (the old fallback).
///
/// Closing: after walking through, with the door behind it and nobody following (no teammate on the far side coming this way
/// or routed through it, nobody in the doorway), a bot may shut it, by personality (anchors and tacticians mostly, rushers
/// rarely; a hash of its seed, so the shared random sequence is untouched). A bot falling back (Cover, an Insane squad's
/// Retreat) always shuts it, even with an enemy behind. A bot holding a spot inside a building shuts the room's door (walking
/// over to it first when holding a roaming spot) and doesn't wander back out through it while the hold lasts. Never: a door
/// its own route goes back through, a door someone else moved in the last moment, or a door this bot has already moved twice
/// in 10 s (so at most open + shut per door per 10 s, no fights and no loops). The server refuses to shut a door on anyone
/// in the doorway (players, co-op enemies, zombies), so nobody is ever shut in the leaf.
///
/// Co-op enemies open doors the same way (their next path node or direct goal across a shut door), and one in cover with
/// its quarry somewhere beyond an open door shuts it (at most once per 10 s). Zombies bash a shut building door they press
/// against (Zombies maps fence areas with their own purchasable doors, ZombiesMode.cs, which zombies never open; building
/// doors only appear if a Zombies map places buildings): each swipe takes <see cref="ZombieDoorDamage"/> off it until it breaks.
/// </summary>
public sealed partial class Game
{
    /// <summary>How far ahead along its route (m) a bot looks for a shut door to open.</summary>
    public const float BotDoorLook = 4.5f;

    /// <summary>The fastest a bot presses the interact key at doors (s).</summary>
    public const double BotDoorUseGap = 0.3;

    /// <summary>A zombie's swipe at a shut building door takes this much off it (a 150 hp wooden door: 4 swipes).</summary>
    public const int ZombieDoorDamage = 40;

    /// <summary>Doors opened and shut by bots, co-op enemies, and zombie swipes at doors, this session (self-test statistics).</summary>
    public int BotDoorOpens, BotDoorCloses, PveDoorOpens, PveDoorCloses, ZombieDoorSwipes;

    /// <summary>Self-test: every door event the server broadcasts (kind, door, health).</summary>
    internal Action<BuildingEventKind, int, int>? BuildingEventTap;

    private double[] _doorMovedAt = [];
    private byte[] _doorMovedBy = [];
    private Map? _doorStampMap;
    private byte _doorActor = byte.MaxValue; // who is moving a door right now (a bot's id; 255: a person or the game)
    private Box[] _buildingBounds = [];
    private Map? _buildingBoundsMap;

    /// <summary>Server: remembers when (and by whom) door <paramref name="i"/> last opened or shut.</summary>
    private void StampDoor(int i, double now)
    {
        var map = World.Map;
        if (_doorStampMap != map || _doorMovedAt.Length != map.BuildingDoors.Count)
        {
            _doorStampMap = map;
            _doorMovedAt = new double[map.BuildingDoors.Count];
            _doorMovedBy = new byte[map.BuildingDoors.Count];
            Array.Fill(_doorMovedAt, -100);
        }
        if ((uint)i < (uint)_doorMovedAt.Length)
        {
            _doorMovedAt[i] = now;
            _doorMovedBy[i] = _doorActor;
        }
    }

    /// <summary>Did someone other than <paramref name="id"/> move door <paramref name="i"/> in the last <paramref name="secs"/> seconds?</summary>
    private bool DoorMovedByOther(int i, byte id, double now, double secs) =>
        _doorStampMap == World.Map && (uint)i < (uint)_doorMovedAt.Length && now - _doorMovedAt[i] < secs && _doorMovedBy[i] != id;

    /// <summary>Did anyone move door <paramref name="i"/> in the last <paramref name="secs"/> seconds?</summary>
    private bool DoorMovedRecently(int i, double now, double secs) =>
        _doorStampMap == World.Map && (uint)i < (uint)_doorMovedAt.Length && now - _doorMovedAt[i] < secs;

    /// <summary>Is a co-op enemy or a zombie standing in <paramref name="leaf"/> (a door must not shut on it)?</summary>
    private bool NpcInLeaf(Box leaf)
    {
        var min = leaf.Min - new Vector3(World.PlayerRadius, World.PlayerHeight, World.PlayerRadius);
        var max = leaf.Max + new Vector3(World.PlayerRadius, 0, World.PlayerRadius);
        foreach (var e in Enemies)
            if (e.Alive && Inside(e.Position, min, max))
                return true;
        foreach (var z in Horde)
            if (z.State != ZState.Dead && Inside(z.Position, min, max))
                return true;
        return false;

        static bool Inside(Vector3 p, Vector3 lo, Vector3 hi) => p.X > lo.X && p.X < hi.X && p.Y > lo.Y && p.Y < hi.Y && p.Z > lo.Z && p.Z < hi.Z;
    }

    // ---- Door geometry ------------------------------------------------------------------------------------------

    /// <summary>The door's flat normal, pointing to the side it swings into.</summary>
    private static Vector3 DoorNormal(BuildingDoor d)
    {
        var n = d.Plan.SwingSide - d.Plan.OtherSide;
        n.Y = 0;
        var len = n.Length();
        return len > 1e-4f ? n / len : Vector3.UnitX;
    }

    /// <summary>Signed distance (m) of <paramref name="p"/> from the door's plane, + on the side it swings into.</summary>
    private static float DoorSide(BuildingDoor d, Vector3 p) => Vector3.Dot(p - d.Center, DoorNormal(d));

    private static float FlatDist(Vector3 a, Vector3 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    /// <summary>Does a walk from <paramref name="a"/> to <paramref name="b"/> (feet) pass through the door's leaf (grown by <paramref name="pad"/>)?</summary>
    internal static bool WalkCrossesDoor(BuildingDoor d, Vector3 a, Vector3 b, float pad = 0.3f)
    {
        var leaf = d.Plan.Closed;
        if (a.Y < leaf.Min.Y - 1.2f || a.Y > leaf.Max.Y - 0.3f)
            return false;
        // Through it, not alongside: the ends on either side of the door's plane (or one end right in it).
        var sa = DoorSide(d, a);
        var sb = DoorSide(d, b);
        if (sa * sb > 0 && MathF.Min(MathF.Abs(sa), MathF.Abs(sb)) > 0.05f)
            return false;
        float t0 = 0, t1 = 1;
        return Slab(a.X, b.X - a.X, leaf.Min.X - pad, leaf.Max.X + pad, ref t0, ref t1) && Slab(a.Z, b.Z - a.Z, leaf.Min.Z - pad, leaf.Max.Z + pad, ref t0, ref t1);

        static bool Slab(float o, float dir, float lo, float hi, ref float t0, ref float t1)
        {
            if (MathF.Abs(dir) < 1e-6f)
                return o >= lo && o <= hi;
            var u = (lo - o) / dir;
            var v = (hi - o) / dir;
            if (u > v)
                (u, v) = (v, u);
            t0 = MathF.Max(t0, u);
            t1 = MathF.Min(t1, v);
            return t0 <= t1;
        }
    }

    /// <summary>Is the door's leaf within the interact key's reach of <paramref name="eye"/> and in plain view (not through a wall)?</summary>
    private static bool DoorInReach(BuildingDoor d, Vector3 eye)
    {
        var to = d.Center - eye;
        var dist = to.Length();
        if (dist > DoorReach || dist < 1e-3f)
            return dist <= DoorReach;
        return World.Query.Raycast(eye, to / dist, dist, QueryFilter.All.Without(QueryLayers.Doors | QueryLayers.Clip)) >= dist - 0.25f;
    }

    /// <summary>The building whose shell's bounds hold <paramref name="p"/>, or -1.</summary>
    private int BuildingAt(Vector3 p)
    {
        var map = World.Map;
        if (_buildingBoundsMap != map || _buildingBounds.Length != map.PlacedBuildings.Count)
        {
            _buildingBoundsMap = map;
            _buildingBounds = new Box[map.PlacedBuildings.Count];
            for (var k = 0; k < map.PlacedBuildings.Count; k++)
            {
                var b = map.PlacedBuildings[k];
                var lo = new Vector3(float.MaxValue);
                var hi = new Vector3(float.MinValue);
                for (var s = b.ShellStart; s < b.ShellStart + b.ShellCount && s < map.Solid.Count; s++)
                {
                    lo = Vector3.Min(lo, map.Solid[s].Min);
                    hi = Vector3.Max(hi, map.Solid[s].Max);
                }
                _buildingBounds[k] = b.ShellCount > 0 ? new Box((lo + hi) / 2, (hi - lo) / 2) : new Box(new Vector3(0, -1e6f, 0), Vector3.Zero);
            }
        }
        for (var k = 0; k < _buildingBounds.Length; k++)
        {
            var bb = _buildingBounds[k];
            var lo = bb.Min;
            var hi = bb.Max;
            if (p.X > lo.X && p.X < hi.X && p.Z > lo.Z && p.Z < hi.Z && p.Y > lo.Y - 0.5f && p.Y < hi.Y)
                return k;
        }
        return -1;
    }

    private static float DoorHash01(int seed, int door, int n)
    {
        var h = (uint)seed ^ ((uint)door * 0x9E3779B1u) ^ ((uint)n * 0x85EBCA77u);
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    // ---- Bots -----------------------------------------------------------------------------------------------------

    /// <summary>A bot presses the interact key at a door (the player's own path: <see cref="ServerUseDoor"/>). True if the door moved.</summary>
    private bool BotUseDoor(Player p, BotBrain b, int i, bool open, double now)
    {
        var d = World.Map.BuildingDoors[i];
        if (d.Broken || d.Open == open || now < b.NextDoorUse)
            return false;
        b.NextDoorUse = now + BotDoorUseGap;
        _doorActor = p.Id;
        var ok = ServerUseDoor(p, i, now);
        _doorActor = byte.MaxValue;
        if (ok)
        {
            b.LogDoorToggle(i, now);
            if (open)
                BotDoorOpens++;
            else
                BotDoorCloses++;
        }
        return ok;
    }

    /// <summary>The bot's route for the next <see cref="BotDoorLook"/> m: its feet, then its path nodes (or where it's steering).</summary>
    private static int BotRoute(Player p, BotBrain b, in BotIntent steer, Span<Vector3> pts)
    {
        pts[0] = p.Position;
        var n = 1;
        var nav = b.PathGraph;
        var left = BotDoorLook;
        if (nav != null && b.PathIdx < b.PathLen)
        {
            for (var k = b.PathIdx; k < b.PathLen && n < pts.Length && left > 0; k++)
            {
                if ((uint)b.Path[k] >= (uint)nav.Nodes.Length)
                    break;
                var q = nav.Nodes[b.Path[k]];
                var seg = Vector3.Distance(pts[n - 1], q);
                if (seg > left && seg > 1e-3f)
                    q = pts[n - 1] + (q - pts[n - 1]) * (left / seg);
                left -= seg;
                pts[n++] = q;
            }
        }
        else if (steer.Wish.LengthSquared() > 0.04f)
            pts[n++] = p.Position + Vector3.Normalize(steer.Wish) * 2.5f;
        return n;
    }

    /// <summary>
    /// Does the straight line from <paramref name="a"/> to <paramref name="b"/> go through a building doorway (open, shut or
    /// broken)? Path shortcuts never do: the nav's doorway nodes either side line a bot up square with the opening.
    /// </summary>
    internal static bool ThroughDoorway(Vector3 a, Vector3 b)
    {
        var doors = World.Map.BuildingDoors;
        var mid = (a + b) * 0.5f;
        var r = Vector3.Distance(a, b) * 0.5f + 2f;
        for (var i = 0; i < doors.Count; i++)
        {
            var d = doors[i];
            if (Vector3.DistanceSquared(d.Center, mid) > r * r)
                continue;
            if (WalkCrossesDoor(d, a, b, 0.2f) && (MathF.Abs(DoorSide(d, a)) > 0.3f || MathF.Abs(DoorSide(d, b)) > 0.3f))
                return true;
        }
        return false;
    }

    private static bool RouteCrosses(BuildingDoor d, ReadOnlySpan<Vector3> pts)
    {
        for (var k = 1; k < pts.Length; k++)
            if (WalkCrossesDoor(d, pts[k - 1], pts[k]))
                return true;
        return false;
    }

    /// <summary>Does another bot's planned route (its next few nodes) go through door <paramref name="d"/>?</summary>
    private static bool BotRoutedThrough(Player o, BuildingDoor d)
    {
        if (o.Bot is not { } ob || ob.PathGraph is not { } nav)
            return false;
        var prev = o.Position;
        for (var k = ob.PathIdx; k < ob.PathLen && k < ob.PathIdx + 4; k++)
        {
            if ((uint)ob.Path[k] >= (uint)nav.Nodes.Length)
                break;
            var q = nav.Nodes[ob.Path[k]];
            if (WalkCrossesDoor(d, prev, q))
                return true;
            prev = q;
        }
        return false;
    }

    /// <summary>
    /// One tick of a bot at doors (after it has decided where to steer): open the shut door ahead, mind the doorway, shut
    /// the door behind it or the door of the room it holds. Server only; costs nothing on maps without buildings.
    /// </summary>
    private void BotDoors(Player p, BotBrain b, ref BotIntent steer, double now)
    {
        var map = World.Map;
        if (map.BuildingDoors.Count == 0 || !p.Alive)
            return;
        var pos = p.Position;
        Span<Vector3> route = stackalloc Vector3[5];
        var rn = BotRoute(p, b, steer, route);
        var pts = route[..rn];
        var eye = p.Eye;
        var fleeing = b.Mode == BotMode.Cover || b.Squad is { Tactic: SquadTactic.Retreat };
        var nearest = -1;
        var nearestDist = 2f;
        var reach2 = (BotDoorLook + 2f) * (BotDoorLook + 2f);
        for (var i = 0; i < map.BuildingDoors.Count; i++)
        {
            var d = map.BuildingDoors[i];
            if (Vector3.DistanceSquared(d.Center, pos) > reach2)
                continue;
            var fd = FlatDist(d.Center, pos);
            if (MathF.Abs(d.Center.Y - pos.Y - 1f) > 2.2f)
                continue; // another floor
            if (d.Open && fd < nearestDist)
            {
                nearest = i;
                nearestDist = fd;
            }
            if (!RouteCrosses(d, pts))
                continue;
            if (!d.Open) // (a broken door is open)
            {
                // A holding bot's shuffle doesn't take it back out through the door it shut for the hold.
                if (b.HoldDoor == i && now < b.HoldShutUntil && b.Mode == BotMode.Roam && b.HoldUntil > now)
                {
                    b.Waypoint = b.HoldAnchor;
                    b.ShuffleAt = now + 4;
                    continue;
                }
                if (DoorInReach(d, eye))
                    BotUseDoor(p, b, i, true, now);
            }
            else
                BotDoorway(p, i, d, ref steer, now);
        }
        // Walking into a shut door it didn't plan through (strafing in a fight, say) still opens it; never while standing still.
        if (!steer.Stationary && steer.Wish.LengthSquared() > 0.04f)
            BotOpenDoors(p, now, steer.Wish);
        BotTrackPass(p, b, nearest, fleeing, now);
        BotCloseBehind(p, b, pts, fleeing, now);
        BotHoldDoor(p, b, now);
        b.LastDoorWish = steer.Wish;
    }

    /// <summary>
    /// An open doorway on the route: one body at a time. Wait a moment for someone in the doorway coming the other way
    /// (head on, the lower id goes first); right after the door swung open, keep out of the leaf's arc on its swing side.
    /// </summary>
    private void BotDoorway(Player p, int i, BuildingDoor d, ref BotIntent steer, double now)
    {
        var pos = p.Position;
        var n = DoorNormal(d);
        var mySide = Vector3.Dot(pos - d.Center, n);
        var myFd = FlatDist(pos, d.Center);
        var inDoorway = myFd < 0.8f && MathF.Abs(mySide) < 0.6f;
        // Line up square with the opening: a doorway is barely wider than a body, and one coming in at an angle or off
        // to one side catches the jamb. Steer onto the door's axis while closing in; right at the wall, square up first.
        var perp = new Vector3(-n.Z, 0, n.X);
        var lat = Vector3.Dot(pos - d.Center, perp);
        var through = n * -MathF.Sign(mySide);
        if (MathF.Abs(mySide) is < 2.2f and > 0.05f && steer.Wish.LengthSquared() > 0.04f && Vector3.Dot(steer.Wish, through) > 0)
        {
            var slack = MathF.Max(0.03f, d.Plan.Width * 0.5f - World.PlayerRadius - 0.02f);
            var forward = MathF.Abs(lat) > slack && MathF.Abs(mySide) < 0.75f ? 0.25f : 1f;
            var len = steer.Wish.Length();
            var w = through * forward - perp * Math.Clamp(lat * 3f, -1f, 1f);
            steer.Wish = Vector3.Normalize(w) * len;
        }
        if (!inDoorway && myFd < 2.2f)
        {
            var heading = -MathF.Sign(mySide); // the way I cross: towards the other side
            foreach (var o in Players.Values)
            {
                if (o == p || !o.Alive)
                    continue;
                var ofd = FlatDist(o.Position, d.Center);
                if (ofd > 2.2f || MathF.Abs(o.Position.Y - pos.Y) > 1.5f)
                    continue;
                var oSide = Vector3.Dot(o.Position - d.Center, n);
                var oVel = Vector3.Dot(o.Sim.Velocity, n);
                var oInDoorway = ofd < 0.8f && MathF.Abs(oSide) < 0.6f;
                var comingAtMe = MathF.Sign(oVel) == -heading && MathF.Abs(oVel) > 0.5f;
                if (oInDoorway && comingAtMe)
                {
                    steer.Wish = Vector3.Zero; // let them out first
                    p.Bot!.DoorWaitTicks++;
                    break;
                }
                if (!oInDoorway && MathF.Sign(oSide) == -MathF.Sign(mySide) && comingAtMe && o.Id < p.Id && ofd < myFd + 0.6f)
                {
                    steer.Wish = Vector3.Zero; // head on: the lower id goes first
                    p.Bot!.DoorWaitTicks++;
                    break;
                }
            }
        }
        // Just swung open towards me: keep to the latch side, out of the leaf's arc.
        if (mySide > 0 && DoorMovedRecently(i, now, BuildingRenderer.SwingSeconds))
        {
            var hinge = new Vector2(d.Plan.Hinge.X, d.Plan.Hinge.Z);
            var rel = new Vector2(pos.X, pos.Z) - hinge;
            if (rel.Length() < d.Plan.Width + World.PlayerRadius && mySide < d.Plan.Width + World.PlayerRadius)
                steer.Wish += new Vector3(d.Plan.ClosedDir.X, 0, d.Plan.ClosedDir.Y) * 0.6f;
        }
    }

    /// <summary>Notices the bot walking through an open (unbroken) doorway: that door becomes the one it may shut behind it.</summary>
    private static void BotTrackPass(Player p, BotBrain b, int nearest, bool fleeing, double now)
    {
        if (nearest < 0)
            return;
        var d = World.Map.BuildingDoors[nearest];
        var s = DoorSide(d, p.Position);
        if (b.TrackDoor != nearest)
        {
            b.TrackDoor = nearest;
            b.TrackSide = s;
            return;
        }
        if (MathF.Abs(s) < 0.15f)
            return;
        if (MathF.Abs(b.TrackSide) >= 0.15f && MathF.Sign(s) != MathF.Sign(b.TrackSide) && !d.Broken)
        {
            b.PassDoor = nearest;
            b.PassAt = now;
            b.Passes++;
            var roll = DoorHash01(b.Seed, nearest, b.Passes);
            if (!fleeing && roll >= b.DoorCloseChance)
                b.PassDoor = -1; // not this one (this bot's personality leaves doors open)
        }
        b.TrackSide = s;
    }

    /// <summary>Shuts the door just walked through, once clear of it, if nobody is following and the route doesn't go back through it.</summary>
    private void BotCloseBehind(Player p, BotBrain b, ReadOnlySpan<Vector3> route, bool fleeing, double now)
    {
        if (b.PassDoor < 0)
            return;
        var i = b.PassDoor;
        var d = World.Map.BuildingDoors[i];
        if (now - b.PassAt > 3 || !d.Open || d.Broken || b.DoorTogglesIn(i, now, 10) >= 2)
        {
            b.PassDoor = -1;
            return;
        }
        var mySide = DoorSide(d, p.Position);
        if (MathF.Abs(mySide) < 0.6f)
            return; // still in the doorway
        if (RouteCrosses(d, route))
        {
            b.PassDoor = -1; // its way goes back through
            return;
        }
        if (Vector3.Distance(p.Eye, d.Center) > DoorReach)
        {
            b.PassDoor = -1; // walked on out of reach: leave it
            return;
        }
        if (DoorMovedByOther(i, p.Id, now, 1.5))
            return; // someone else just moved it: not a fight over it
        foreach (var o in Players.Values)
        {
            if (o == p || !o.Alive)
                continue;
            var ofd = FlatDist(o.Position, d.Center);
            if (ofd > 9f || MathF.Abs(o.Position.Y - p.Position.Y) > 2.5f)
                continue;
            if (ofd < 1.3f)
                return; // someone in the doorway (and the server wouldn't shut it on them anyway)
            if (MathF.Sign(DoorSide(d, o.Position)) == MathF.Sign(mySide))
                continue; // on my side already
            var towards = Vector3.Dot(o.Sim.Velocity, Vector3.Normalize(d.Center - o.Position));
            if (!IsEnemy(p, o))
            {
                if (ofd < 4f || towards > 0.3f || BotRoutedThrough(o, d))
                {
                    b.PassDoor = -1; // a teammate is following: leave it open
                    return;
                }
            }
            else if (!fleeing && ofd < 3f)
            {
                b.PassDoor = -1; // an enemy right there would only open it again
                return;
            }
        }
        if (!DoorInReach(d, p.Eye))
            return;
        if (BotUseDoor(p, b, i, false, now))
            b.PassDoor = -1;
    }

    /// <summary>
    /// A bot holding a spot inside a building shuts the room's door (an open door of that building within 7 m, on its floor):
    /// from where it stands if it's in reach, else (a roaming hold) it walks over first. Checked twice a second.
    /// </summary>
    private void BotHoldDoor(Player p, BotBrain b, double now)
    {
        if (now < b.NextHoldDoorCheck)
            return;
        b.NextHoldDoorCheck = now + 0.5;
        var roamHold = b.Mode == BotMode.Roam && b.HoldUntil > now;
        if (!roamHold && !b.IsHoldingPosition)
            return;
        if (roamHold && b.HoldUntil - now < 8)
            return; // about to move on anyway
        var map = World.Map;
        var anchor = roamHold ? b.HoldAnchor : p.Position;
        var building = BuildingAt(anchor);
        if (building < 0)
            return;
        var inst = map.PlacedBuildings[building];
        var best = -1;
        var bestD = 7f;
        var look = GravityFrames.Above(anchor, 1.2f);
        for (var i = inst.DoorStart; i < inst.DoorStart + inst.DoorCount; i++)
        {
            var d = map.BuildingDoors[i];
            if (!d.Open || d.Broken || MathF.Abs(d.Plan.Closed.Min.Y - anchor.Y) > 1.2f)
                continue;
            var fd = FlatDist(d.Center, anchor);
            if (fd >= bestD)
                continue;
            // The room's own door: in plain view from the spot (another room's door is behind a wall).
            var to = d.Center - look;
            var len = to.Length();
            if (len > 1e-3f && World.Query.Raycast(look, to / len, len, QueryFilter.All.Without(QueryLayers.Doors | QueryLayers.Clip)) < len - 0.25f)
                continue;
            bestD = fd;
            best = i;
        }
        if (best < 0)
            return;
        if (b.HoldDoor >= 0 && b.HoldDoor != best && now - b.HoldDoorAt < 30)
            return; // one door per hold: not every door in the building
        var door = map.BuildingDoors[best];
        if (b.DoorTogglesIn(best, now, 10) >= 2 || DoorMovedByOther(best, p.Id, now, 4))
            return;
        var anchorSide = DoorSide(door, anchor);
        foreach (var o in Players.Values)
        {
            if (o == p || !o.Alive)
                continue;
            var ofd = FlatDist(o.Position, door.Center);
            if (ofd < 1.3f)
                return; // someone in the doorway
            if (ofd < 8f && !IsEnemy(p, o) && MathF.Sign(DoorSide(door, o.Position)) != MathF.Sign(anchorSide))
                return; // a teammate out there: leave their way in open
        }
        if (MathF.Abs(DoorSide(door, p.Position)) >= 0.6f && MathF.Sign(DoorSide(door, p.Position)) == MathF.Sign(anchorSide) && DoorInReach(door, p.Eye))
        {
            if (BotUseDoor(p, b, best, false, now))
            {
                b.HoldDoor = best;
                b.HoldDoorAt = now;
                b.HoldShutUntil = roamHold ? b.HoldUntil : now + 10;
            }
            return;
        }
        if (roamHold && (b.HoldDoor != best || now - b.HoldDoorAt > 20))
        {
            // Walk over to it (the hold's shuffle goes back to the anchor afterwards).
            b.HoldDoor = best;
            b.HoldDoorAt = now;
            b.Waypoint = anchorSide > 0 ? door.Plan.SwingSide : door.Plan.OtherSide;
            b.ShuffleAt = now + 4;
        }
    }

    // ---- Co-op enemies ----------------------------------------------------------------------------------------------

    /// <summary>
    /// A co-op enemy at doors (near players, where it moves through the player physics): opens a shut door its next path
    /// leg (or its direct line to the goal) goes through once the leaf is in reach, or one it walks into; in cover with its
    /// quarry beyond an open door, shuts it. Checked every third tick.
    /// </summary>
    private void EnemyDoors(PveEnemy e, double now)
    {
        var map = World.Map;
        if (map.BuildingDoors.Count == 0 || !e.Alive || !e.NearPhysics || (ServerTick + e.Id) % 3 != 0)
            return;
        var pos = e.Position;
        Span<Vector3> route = stackalloc Vector3[4];
        route[0] = pos;
        var n = 1;
        var nav = NavGraph.Current;
        if (e.PathLen > 0)
        {
            for (var k = e.PathIdx; k < e.PathLen && n < route.Length; k++)
                if ((uint)e.Path[k] < (uint)nav.Nodes.Length)
                    route[n++] = nav.Nodes[e.Path[k]];
        }
        else
            route[n++] = e.Goal;
        // Only the first few metres matter.
        for (var k = 1; k < n; k++)
        {
            var seg = route[k] - route[k - 1];
            var len = seg.Length();
            if (len > BotDoorLook)
            {
                route[k] = route[k - 1] + seg * (BotDoorLook / len);
                n = k + 1;
                break;
            }
        }
        var pts = route[..n];
        var eye = EnemyEye(e);
        var forward = new Vector3(MathF.Sin(e.Yaw), 0, -MathF.Cos(e.Yaw));
        for (var i = 0; i < map.BuildingDoors.Count; i++)
        {
            var d = map.BuildingDoors[i];
            if (d.Broken || Vector3.DistanceSquared(d.Center, pos) > 49f)
                continue;
            if (!d.Open)
            {
                if (!e.Moving)
                    continue;
                var leaf = d.Plan.Closed;
                var dx = MathF.Max(0, MathF.Max(leaf.Min.X - pos.X, pos.X - leaf.Max.X));
                var dz = MathF.Max(0, MathF.Max(leaf.Min.Z - pos.Z, pos.Z - leaf.Max.Z));
                var pressed = dx * dx + dz * dz < 0.95f * 0.95f && pos.Y > leaf.Min.Y - 0.6f && pos.Y < leaf.Max.Y && Vector3.Dot(d.Center - pos, forward) > 0;
                if (pressed || (RouteCrosses(d, pts) && DoorInReach(d, eye)))
                {
                    _doorActor = byte.MaxValue;
                    if (ServerSetDoor(i, true, now))
                        PveDoorOpens++;
                }
            }
            else if (e.State == PveState.Cover && !e.Moving && !e.TargetVisible && now - e.DoorShutAt > 10 && now - e.LastSawTarget < 8
                     && MathF.Sign(DoorSide(d, e.LastSeen)) != MathF.Sign(DoorSide(d, pos)) && MathF.Abs(DoorSide(d, pos)) > 0.8f
                     && FlatDist(e.LastSeen, d.Center) < 30f && DoorInReach(d, eye) && !DoorMovedRecently(i, now, 3))
            {
                if (ServerSetDoor(i, false, now))
                {
                    e.DoorShutAt = now;
                    PveDoorCloses++;
                }
            }
        }
    }

    // ---- Zombies ------------------------------------------------------------------------------------------------------

    /// <summary>A zombie pressed against a shut building door swipes at it (<see cref="ZombieDoorDamage"/> a swipe) until it breaks.</summary>
    internal void ZombieBashDoors(Zombie z, double now)
    {
        var map = World.Map;
        if (map.BuildingDoors.Count == 0 || z.State is ZState.Dead or ZState.Rising || now < z.NextSwing)
            return;
        var pos = z.Position;
        for (var i = 0; i < map.BuildingDoors.Count; i++)
        {
            var d = map.BuildingDoors[i];
            if (d.Open || d.Broken)
                continue;
            var leaf = d.Plan.Closed;
            if (pos.Y < leaf.Min.Y - 0.6f || pos.Y > leaf.Max.Y)
                continue;
            var dx = MathF.Max(0, MathF.Max(leaf.Min.X - pos.X, pos.X - leaf.Max.X));
            var dz = MathF.Max(0, MathF.Max(leaf.Min.Z - pos.Z, pos.Z - leaf.Max.Z));
            if (dx * dx + dz * dz >= 0.8f * 0.8f)
                continue;
            z.NextSwing = now + ZRules.ZombieWindup + 0.6;
            ZombieDoorSwipes++;
            DamageDoor(i, ZombieDoorDamage, PropDamage.Melee, pos + new Vector3(0, 1.2f, 0));
            return;
        }
    }
}
