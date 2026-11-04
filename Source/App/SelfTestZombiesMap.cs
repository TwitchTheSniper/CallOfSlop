using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>Helpers other sections still use; the section this file held was removed (user, 2026-11-03).</summary>
public static partial class SelfTest
{

    private static float ZFlat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    /// <summary>
    /// The parts of a full Zombies map's layout every such map must get right (Blackmoor Asylum, Ashgrave Cathedral): every
    /// area reachable through the doors, every door a real way through, the spawners and windows sound, every machine
    /// standing as its solid prop.
    /// </summary>
    private static void ZombiesMapSound(Map map, ZombiesMap zm)
    {
        // Every area is reachable through doors (the door graph), and every door is a real way through (shut: blocked).
        var reach = new HashSet<int> { zm.StartArea };
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var d in zm.Doors)
                if (reach.Contains(d.AreaA) != reach.Contains(d.AreaB))
                {
                    reach.Add(d.AreaA);
                    reach.Add(d.AreaB);
                    grew = true;
                }
        }
        var badDoors = new List<string>();
        for (var i = 0; i < zm.Doors.Count; i++)
        {
            var d = zm.Doors[i];
            var shut = World.Blocked(d.Box.Center with { Y = d.Box.Min.Y + 0.05f }) && !World.CanWalkStraight(d.SideA, d.SideB);
            map.SetDoorOpen(i, true);
            var open = World.CanWalkStraight(d.SideA, d.SideB) && !World.Blocked(d.SideA + new Vector3(0, 0.05f, 0)) && !World.Blocked(d.SideB + new Vector3(0, 0.05f, 0));
            map.SetDoorOpen(i, false);
            if (!shut || !open || !zm.Areas[d.AreaA].Contains(d.SideA) || !zm.Areas[d.AreaB].Contains(d.SideB))
                badDoors.Add($"{d.Name} (shut {shut}, open {open})");
        }
        ZLine($"  areas reachable through doors: {reach.Count} of {zm.Areas.Count}; doors that are a real way through: {zm.Doors.Count - badDoors.Count} of {zm.Doors.Count}");
        if (reach.Count != zm.Areas.Count)
            Fail($"{map.Name}: areas not reachable through doors: {string.Join(", ", Enumerable.Range(0, zm.Areas.Count).Where(a => !reach.Contains(a)).Select(a => zm.Areas[a].Name))}");
        foreach (var b in badDoors)
            Fail($"{map.Name}: door {b} isn't a clean way between its two areas");

        // Spawners and windows: behind a window = outside every area, inside the outline, room to stand, a straight walk
        // to the window; in the ground = in its area; window inside points in the area and clear, openings block bodies.
        var bad = new List<string>();
        foreach (var s in zm.Spawners)
        {
            var at = s.At + new Vector3(0, 0.05f, 0);
            if (World.Blocked(at) || !map.InsideBounds(s.At.X, s.At.Z, 0.5f))
                bad.Add($"spawner at {s.At} is blocked or outside the map");
            else if (s.Window >= 0 && (zm.Inside(s.At) || !World.CanWalkStraight(s.At, zm.Windows[s.Window].Outside)))
                bad.Add($"spawner at {s.At} for window {s.Window} is inside an area or can't walk to its window");
            else if (s.Window < 0 && !zm.Areas[s.Area].Contains(s.At))
                bad.Add($"ground spawner at {s.At} isn't in {zm.Areas[s.Area].Name}");
        }
        for (var i = 0; i < zm.Windows.Count; i++)
        {
            var w = zm.Windows[i];
            if (!zm.Spawners.Any(s => s.Window == i))
                bad.Add($"window {i} has no spawner");
            if (World.Blocked(w.Inside + new Vector3(0, 0.05f, 0)) || World.Blocked(w.Outside + new Vector3(0, 0.05f, 0)) || zm.Inside(w.Outside))
                bad.Add($"window {i}: its inside or outside spot is blocked (or outside is in an area)");
            if (!World.Blocked(w.Opening.Center with { Y = w.Opening.Min.Y + 0.05f }, 1.0f))
                bad.Add($"window {i}: the opening doesn't block bodies");
        }
        ZLine($"  spawners and windows: {zm.Spawners.Count} spawners, {zm.Windows.Count} windows, {bad.Count} problem(s)");
        foreach (var b in bad)
            Fail($"{map.Name}: {b}");

        // Machines: each one stands as its solid prop (players can't walk through it); Zombies Test keeps them collision-free.
        var machines = zm.Machines().ToList();
        var props = map.PropInstances.Where(p => p.Def.Category == "Zombies").ToList();
        var notSolid = machines.Count(m => !World.Blocked(m.Spot.At + new Vector3(0, 0.05f, 0), 0.5f));
        var matched = machines.Count(m => props.Any(p => p.Def.Name == m.Prop && Vector2.Distance(new Vector2(p.Position.X, p.Position.Z), new Vector2(m.Spot.At.X, m.Spot.At.Z)) < 0.01f));
        ZLine($"  machine props: {machines.Count} machines, {matched} with their prop, {machines.Count - notSolid} solid; Zombies Test machine props {ZTestMap?.Zombies?.MachineProps}");
        if (!zm.MachineProps || matched != machines.Count || notSolid > 0)
            Fail($"{map.Name}: every machine should stand as its solid prop ({matched} of {machines.Count} placed, {notSolid} not solid)");
    }

    // ---- Zombies on the map: through every window, through every door, the AI cost ---------------------------

    private static Zombie ZPlace(Game g, ZombiesMap zm, Vector3 at, int area, ZSpeed speed, double now)
    {
        var z = g.SelfTestSpawnZombie(zm.Spawners.FindIndex(s => s.Window >= 0), now);
        z.State = ZState.Chase;
        z.Window = -1;
        z.Area = area;
        z.Speed = speed;
        z.Sim.Position = z.PrevPos = at;
        for (var i = 0; i < z.History.Length; i++)
            z.History[i] = at;
        return z;
    }

    /// <summary>The floor under (x, y, z) within 3 m (y - 3 if none).</summary>
    private static float FloorBelow(float x, float y, float z)
    {
        var d = World.Query.Raycast(new Vector3(x, y, z), -Vector3.UnitY, 3f, QueryFilter.Navigation);
        return y - MathF.Min(d, 3f);
    }

    /// <summary>A free spot to stand near the middle of <paramref name="a"/> (props and machines stand in the way).</summary>
    private static Vector3 FreeSpot(Map map, ZArea a)
    {
        var c = a.Center;
        for (var r = 0f; r < 8f; r += 0.5f)
            for (var k = 0; k < 16; k++)
            {
                var p = new Vector3(c.X + MathF.Sin(k * MathF.Tau / 16) * r, 0, c.Z + MathF.Cos(k * MathF.Tau / 16) * r);
                p.Y = a.Banded ? FloorBelow(p.X, a.Y0 + 2.4f, p.Z) : map.GroundHeight(p.X, p.Z); // (an area with heights: its floor, found from just above it)
                if (a.Contains(p) && a.Distance(p) <= 0 && p.X > a.X0 + 0.6f && p.X < a.X1 - 0.6f && p.Z > a.Z0 + 0.6f && p.Z < a.Z1 - 0.6f && !World.Blocked(p + new Vector3(0, 0.05f, 0)))
                    return p;
            }
        return c;
    }

    /// <summary>Pulls <paramref name="g"/>'s trigger on <paramref name="slot"/> (a magazine and a few more) while Infinite Ammo runs.</summary>
    private static (int Pulls, int Shots, bool AmmoSame, bool Reloaded) InfinitePulls(Game g, int slot, ref double t)
    {
        g.SwitchTo(slot == 0 ? 1 : 0, t);
        g.SwitchTo(slot, t);
        var w = g.Slots[slot];
        // GiveBuild / LocalSetSlot stamp NextFireTime from the real GLFW clock, not the scripted `t`. On a long suite run
        // (real time past the scripted clock, 2026-10-31 on Windows) the gun then waits seconds to "cool down": the scripted
        // clock owns fire timing here, so put it back.
        g.NextFireTime = Math.Max(g.EquipUntil, t);
        t = Math.Max(t, g.EquipUntil) + 0.01;
        var step = Math.Max(w.Def.FireInterval * 1.3, 0.02);
        var pulls = w.Def.Mag + 5;
        var shots = 0;
        var same = true;
        var reloaded = false;
        for (var i = 0; i < pulls; i++)
        {
            t += step;
            g.SelfTestWeaponFrame(t, (float)step);
            g.UpdateTrigger(t, true, true);
            g.UpdateTrigger(t, false, false); // (let go: the next pull is a fresh press)
            if (g.LastShotTime >= t)
                shots++;
            same &= w.Ammo == w.Def.Mag;
            reloaded |= g.IsReloading(t) || w.ReloadUntil > 0;
        }
        return (pulls, shots, same, reloaded);
    }
}
