using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Players outside the play area in Zombies (board item wr01, 2026-10-28). Window openings block bodies, so a player who
/// got outside (out of every area) had no way back. Now:
/// <list type="bullet">
/// <item>they can mantle in through any window whose boards are all gone (<see cref="WindowEntry"/>, in the movers);</item>
/// <item>the HUD tells them how (<see cref="OutsideReturnLeft"/>), and after <see cref="OutsideReturnSeconds"/> outside
/// the server puts them back on the nearest spawn of an open area (<see cref="ServerTickOutside"/>; no points lost);</item>
/// <item>the server logs each time someone gets out ("[zombies] player outside play area", with the map, where and how),
/// so the leaks can be found and fixed.</item>
/// </list>
/// "Outside" is more than <see cref="OutsideMargin"/> from every area, so the thin gaps the areas leave at walls and doors
/// (and stair landings between banded floors) never count. Bots follow the same rules (the movers are shared; the return
/// takes them back too). Nothing new on the wire: a client works out its own countdown from its predicted position.
/// </summary>
public sealed partial class Game
{
    /// <summary>Seconds outside before the server brings a player back.</summary>
    public const double OutsideReturnSeconds = 10;

    /// <summary>How far from every area a player's feet must be to count as outside (<see cref="WindowEntry.OutsideMargin"/>).</summary>
    public const float OutsideMargin = WindowEntry.OutsideMargin;

    /// <summary>A spell outside is logged once it has lasted this long (a jump that lands back inside isn't news).</summary>
    private const double OutsideLogAfter = 0.5;

    /// <summary>Players brought back from outside, and spells outside logged, since this game started (self-test).</summary>
    public int OutsideReturns { get; private set; }

    public int OutsideSpells { get; private set; }

    /// <summary>The last logged spell's cause (self-test).</summary>
    public string LastOutsideCause { get; private set; } = "";

    /// <summary>Client: when the local player's own spell outside began (-1: inside).</summary>
    private double _localOutsideSince = -1;

    /// <summary>Is <paramref name="feet"/> outside the play area (more than <see cref="OutsideMargin"/> from every area)?</summary>
    public static bool OutsidePlayArea(ZombiesMap zm, Vector3 feet) => WindowEntry.OutsidePlayArea(zm, feet);

    /// <summary>A zombie body or the crowd surface moved <paramref name="p"/> this tick (server: the outside log's "pushed").</summary>
    private void NotePushed(Player p)
    {
        if (IsServer && ZPlayers.TryGetValue(p.Id, out var zp))
            zp.PushedAt = _zNow;
    }

    /// <summary>Server, every Zombies tick for each player (after <see cref="TickZPlayer"/>): the outside watch, its log line and the return.</summary>
    private void ServerTickOutside(Player p, ZPlayer zp, ZombiesMap zm, double now)
    {
        var s = p.Sim;
        if (!p.Alive || zp.Downed || zp.Held || s.MantleTicks > 0)
        {
            // Dead, downed or held: the clock waits (a downed player outside is revived or bleeds out as anywhere).
            // Mid-mantle: wait for it to land (a mantle in through a window ends inside).
            if (!p.Alive)
                zp.OutsideSince = -1;
            zp.WasMantling = s.MantleTicks > 0;
            zp.LastPos = s.Position;
            return;
        }
        var feet = s.Position;
        if (!OutsidePlayArea(zm, feet))
        {
            zp.OutsideSince = -1;
            zp.LastInside = feet;
            zp.WasMantling = false;
            zp.LastPos = feet;
            return;
        }
        if (zp.OutsideSince < 0)
        {
            // Just got out: how?
            zp.OutsideSince = now;
            zp.OutsideLogged = false;
            zp.OutsideAt = feet;
            zp.OutsideCause = OutsideCause(p, zp, feet, now);
        }
        zp.WasMantling = false;
        zp.LastPos = feet;
        if (!zp.OutsideLogged && now - zp.OutsideSince >= OutsideLogAfter)
        {
            zp.OutsideLogged = true;
            OutsideSpells++;
            LastOutsideCause = zp.OutsideCause;
            var a = zp.OutsideAt;
            var l = zp.LastInside;
            Console.WriteLine($"[zombies] player outside play area: {p.Name}{(p.Bot != null ? " (bot)" : "")} on {World.Map.Name} at ({a.X:0.0}, {a.Y:0.0}, {a.Z:0.0}), last inside ({l.X:0.0}, {l.Y:0.0}, {l.Z:0.0}), how: {zp.OutsideCause}");
        }
        if (now - zp.OutsideSince >= OutsideReturnSeconds)
            ReturnFromOutside(p, zp, zm);
    }

    /// <summary>How a player just got out: the first cause that fits, from the clearest.</summary>
    private static string OutsideCause(Player p, ZPlayer zp, Vector3 feet, double now)
    {
        var s = p.Sim;
        var moved = feet - zp.LastPos;
        if (moved.LengthSquared() > 3f * 3f)
            return "teleport (moved over 3 m in a tick)";
        if (s.KnockTicks > 0)
            return "knockback (a blast push)";
        if (zp.WasMantling)
            return "mantle (climbed over a wall or ledge)";
        if (now - zp.PushedAt < 0.5)
            return "pushed (zombie bodies or the crowd surface)";
        if (feet.Y < zp.LastInside.Y - 1f)
            return "fell through (dropped out under the area)";
        if (!s.Grounded)
            return "jumped (over a wall or gap)";
        return "walked out (a gap in the walls, or the areas miss a spot)";
    }

    /// <summary>Server: puts <paramref name="p"/> on the nearest spawn inside an open area (the start area's if none is open), points and gear untouched.</summary>
    private void ReturnFromOutside(Player p, ZPlayer zp, ZombiesMap zm)
    {
        var from = p.Position;
        var best = from;
        var bestD = float.MaxValue;
        foreach (var sp in World.Map.Spawns)
        {
            if (!zm.Inside(sp) || !AreaOpen(zm.AreaAt(sp)))
                continue;
            var d = Vector3.DistanceSquared(sp, from);
            if (d < bestD)
                (best, bestD) = (sp, d);
        }
        if (bestD == float.MaxValue)
        {
            // No spawn in an open area (a map without spawns inside): the start area's middle, on its floor.
            var c = zm.Areas[zm.StartArea].Center;
            best = CartPhysics.Ground(World.Query, c with { Y = c.Y + 1.5f }, 0) is { } g ? c with { Y = g } : c;
        }
        p.Teleport(best);
        for (var i = 0; i < Player.HistorySize; i++)
            p.History[i] = new HistoryEntry { Position = best, Yaw = p.Yaw };
        zp.OutsideSince = -1;
        zp.LastInside = best;
        zp.LastPos = best;
        OutsideReturns++;
        Console.WriteLine($"[zombies] {p.Name} brought back from outside after {OutsideReturnSeconds:0} s: ({from.X:0.0}, {from.Y:0.0}, {from.Z:0.0}) to ({best.X:0.0}, {best.Y:0.0}, {best.Z:0.0})");
        if (p.Id == LocalId)
            _localOutsideSince = -1;
    }

    /// <summary>
    /// The HUD: seconds until <paramref name="me"/> is brought back from outside, or -1 when inside (or dead or downed).
    /// The server reads its own watch; a client keeps its own clock from its predicted position (the same rule).
    /// </summary>
    public double OutsideReturnLeft(Player me, double now)
    {
        if (ZMap is not { } zm || !me.Alive || ZP(me).Downed)
            return -1;
        if (IsServer)
        {
            var since = ZP(me).OutsideSince;
            return since < 0 ? -1 : Math.Max(0, OutsideReturnSeconds - (now - since));
        }
        if (me.Sim.MantleTicks > 0 || !OutsidePlayArea(zm, me.Position))
        {
            _localOutsideSince = -1;
            return -1;
        }
        if (_localOutsideSince < 0 || _localOutsideSince > now)
            _localOutsideSince = now;
        return Math.Max(0, OutsideReturnSeconds - (now - _localOutsideSince));
    }

    /// <summary>The HUD line for a player outside (ASCII), with the jump key's name.</summary>
    public static string OutsideHint(double left, string jumpKey) =>
        left > 0
            ? $"You are outside. Climb in at an open window (all boards gone): face it, hold forward and press {jumpKey}. Back inside in {Math.Ceiling(left):0} s."
            : "You are outside. Bringing you back inside...";

    /// <summary>Self-test: runs the outside watch once for every player (in id order).</summary>
    public void SelfTestTickOutside(double now)
    {
        if (ZMap is not { } zm)
            return;
        _zNow = now;
        for (var id = 0; id < 256; id++)
            if (Players.TryGetValue((byte)id, out var p))
                ServerTickOutside(p, ZP(p), zm, now);
    }
}
