using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombies window entry" (board item wr01; Source/Physics/WindowEntry.cs, Source/Game/ZombiesOutside.cs):
/// on Blackmoor Asylum and Ashgrave Cathedral, every window, with both movers:
/// <list type="bullet">
/// <item>a player outside, facing an open window (every board gone, its area open), mantles in with Jump + forward and
/// ends inside the window's area;</item>
/// <item>with one board left they stay outside; a player just inside, facing out, never leaves through it; a window of a
/// locked area (its doors shut) doesn't take anyone in;</item>
/// <item>the fallback: a player stood outside is logged once (with how they got out) and brought back to an open area's
/// spawn after the wait, points untouched; the HUD's countdown runs while outside and stops after;</item>
/// <item>the window search and the outside watch allocate nothing.</item>
/// </list>
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombiesWindowEntry()
    {
        var now = 0.0;
        foreach (var name in new[] { "Blackmoor Asylum", "Ashgrave Cathedral" })
        {
            if (Maps.Find(name) is not { Zombies: { } zm } map)
            {
                Fail($"window entry: the map '{name}' is missing or has no zombies section");
                continue;
            }
            WindowEntryMap(map, zm, ref now);
        }
    }

    private static void WindowEntryMap(Map map, ZombiesMap zm, ref double now)
    {
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        if (!ReferenceEquals(WindowEntry.Boards, g.ZBoards) || g.ZBoards.Length != zm.Windows.Count)
        {
            Fail($"{map.Name}: the movers don't see the game's boards (WindowEntry.Boards)");
            return;
        }
        // Locked areas first (every door shut): only the start area's windows take anyone in.
        Array.Clear(g.ZBoards);
        var lockedIn = 0;
        for (var i = 0; i < zm.Windows.Count; i++)
            if (!WindowEntry.AreaOpen(zm, zm.Windows[i].Area) && WindowTry(zm, i, ClassicMover.Instance, outside: true).In)
                lockedIn++;
        if (lockedIn > 0)
            Fail($"{map.Name}: {lockedIn} window(s) of locked areas let an outside player in");

        for (var d = 0; d < zm.Doors.Count; d++)
            g.OpenDoor(d, null);
        var movers = new IMover[] { ClassicMover.Instance, SweptMover.Instance };
        int entered = 0, boarded = 0, leftOut = 0, tries = 0;
        var bad = new List<string>();
        for (var i = 0; i < zm.Windows.Count; i++)
            foreach (var mover in movers)
            {
                tries++;
                var w = zm.Windows[i];
                Array.Clear(g.ZBoards);
                var r = WindowTry(zm, i, mover, outside: true);
                if (r.In)
                    entered++;
                else
                    bad.Add($"window {i} ({zm.Areas[w.Area].Name}, {mover.Mode}): an outside player didn't get in through it open (ended at {r.At.X:0.0},{r.At.Y:0.0},{r.At.Z:0.0})");
                g.ZBoards[i] = 1;
                r = WindowTry(zm, i, mover, outside: true);
                if (!r.In && !r.EverIn)
                    boarded++;
                else
                    bad.Add($"window {i} ({zm.Areas[w.Area].Name}, {mover.Mode}): got in with a board left");
                g.ZBoards[i] = 0;
                r = WindowTry(zm, i, mover, outside: false);
                if (!r.EverOut)
                    leftOut++;
                else
                    bad.Add($"window {i} ({zm.Areas[w.Area].Name}, {mover.Mode}): a player inside got out through it open (at {r.At.X:0.0},{r.At.Y:0.0},{r.At.Z:0.0})");
            }
        Line($"  {map.Name}: {zm.Windows.Count} windows x 2 movers: in through open {entered}/{tries}, kept out by a board {boarded}/{tries}, kept in {leftOut}/{tries}; locked areas let in {lockedIn}");
        foreach (var b in bad)
            Fail($"{map.Name}: {b}");

        // The fallback return, from outside the start area's first window (boards up: no way in).
        Array.Fill(g.ZBoards, (byte)6);
        var me = g.Local!;
        var zp = g.ZP(me);
        zp.Points = 1234;
        var w0 = zm.Windows.FindIndex(x => x.Area == zm.StartArea) is var k and >= 0 ? zm.Windows[k] : zm.Windows[0];
        var spells = g.OutsideSpells;
        var returns = g.OutsideReturns;
        me.Teleport(FreeSpot(map, zm.Areas[zm.StartArea]));
        g.SelfTestTickOutside(now);
        me.Teleport(w0.Outside);
        var hudOutside = -1.0;
        var t0 = now;
        while (now - t0 < Game.OutsideReturnSeconds + 0.5 && g.OutsideReturns == returns)
        {
            now += 0.05;
            g.SelfTestTickOutside(now);
            if (now - t0 > 1 && hudOutside < 0)
                hudOutside = g.OutsideReturnLeft(me, now);
        }
        var back = me.Position;
        var hudAfter = g.OutsideReturnLeft(me, now);
        var waited = now - t0;
        Line($"  {map.Name}: outside at window {zm.Windows.IndexOf(w0)}: logged {g.OutsideSpells - spells} ({g.LastOutsideCause}), brought back after {waited:0.0} s to ({back.X:0.0},{back.Y:0.0},{back.Z:0.0}), HUD {hudOutside:0.0} s left then {hudAfter:0}");
        if (g.OutsideSpells - spells != 1)
            Fail($"{map.Name}: a spell outside was logged {g.OutsideSpells - spells} times (want once)");
        if (!g.LastOutsideCause.StartsWith("teleport"))
            Fail($"{map.Name}: a player moved outside by a teleport was logged as '{g.LastOutsideCause}'");
        if (g.OutsideReturns - returns != 1 || waited < Game.OutsideReturnSeconds - 0.1)
            Fail($"{map.Name}: the return from outside came {g.OutsideReturns - returns} time(s) after {waited:0.0} s (want once after {Game.OutsideReturnSeconds} s)");
        if (!zm.Inside(back) || !g.AreaOpen(zm.AreaAt(back)) || World.Blocked(back + new Vector3(0, 0.05f, 0)))
            Fail($"{map.Name}: brought back from outside to ({back.X:0.0},{back.Y:0.0},{back.Z:0.0}), not a free spot in an open area");
        if (zp.Points != 1234)
            Fail($"{map.Name}: coming back from outside changed the points ({zp.Points}, want 1234)");
        if (hudOutside is <= 0 or > Game.OutsideReturnSeconds || hudAfter >= 0)
            Fail($"{map.Name}: the HUD countdown outside read {hudOutside:0.0} s, then {hudAfter:0.0} back inside");
        if (!Game.OutsideHint(5, "Space").Contains("Space") || Game.OutsideHint(5, "Space").Any(c => c > 126))
            Fail("the outside hint doesn't name the jump key, or isn't ASCII");

        // Nothing allocated: the window search outside an open window, and the watch inside.
        Array.Clear(g.ZBoards);
        g.ZBoards[0] = 1; // (one shut, so the search runs over every window)
        WindowTry(zm, 0, ClassicMover.Instance, outside: true);
        me.Teleport(FreeSpot(map, zm.Areas[zm.StartArea]));
        g.SelfTestTickOutside(now);
        var wa = zm.Windows[0];
        var s = new MoveState { Position = wa.Outside, Grounded = true };
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 0; t < 120; t++)
        {
            var probe = s;
            _ = WindowEntry.TryFind(probe, wa.Yaw, out _, out _, out _);
            now += World.TickDt;
            g.SelfTestTickOutside(now);
        }
        var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        if (alloc > 0)
            Fail($"{map.Name}: the window search and outside watch allocated {alloc} B in 120 ticks");
        Array.Fill(g.ZBoards, (byte)6);
    }

    /// <summary>
    /// A player at window <paramref name="i"/>'s outside (or inside) point facing through it, holding forward and pressing
    /// Jump for 2.5 s with <paramref name="mover"/>: where they end, whether they end in the window's area, and whether they
    /// were ever in it / ever outside the play area.
    /// </summary>
    private static (bool In, bool EverIn, bool EverOut, Vector3 At) WindowTry(ZombiesMap zm, int i, IMover mover, bool outside)
    {
        var w = zm.Windows[i];
        var start = outside ? w.Outside : w.Inside;
        var yaw = outside ? w.Yaw : w.Yaw + MathF.PI;
        var s = new MoveState { Position = start + new Vector3(0, 0.02f, 0), Grounded = false };
        var area = zm.Areas[w.Area];
        bool everIn = false, everOut = false;
        for (var t = 0; t < 150; t++)
        {
            // (once in, let go: holding forward would walk on across the room)
            var cmd = new InputCmd { Buttons = outside && everIn && s.MantleTicks == 0 ? Buttons.None : Buttons.Forward | Buttons.Jump, Yaw = yaw, SpeedPct = 100, Seq = (uint)t };
            mover.Simulate(ref s, cmd);
            everIn |= area.Contains(s.Position);
            everOut |= Game.OutsidePlayArea(zm, s.Position);
        }
        return (area.Contains(s.Position) && !Game.OutsidePlayArea(zm, s.Position), everIn, everOut, s.Position);
    }
}
