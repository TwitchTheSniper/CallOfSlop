using System.Numerics;

namespace CallOfSlop;

/// <summary>A Reload Hoist found at the rebuild: the launcher rack it serves (on its mount) and its works piece.</summary>
public struct CHoist
{
    public ushort Part; // the hoist
    public ushort Launcher; // the rack it loads: the nearest one whose mount is in the hoist's assembly
    public byte Idle; // looks left to skip after one found nothing to move (a rebuild makes a new entry, so a change looks at once)
}

public sealed partial class Construct
{
    public CHoist[] Hoists = []; // its Reload Hoists with a launcher (ConstructWorks.Build)
    public int HoistCount;
}

/// <summary>
/// Reload Hoists (docs/sandbox/design/ship-works.md 6): a hoist bolted behind a launcher's Hull Mount moves one weapon at a time
/// from its magazine (the Ordnance Racks bolted to the hoist) onto an empty station of the launcher, after a time that grows with
/// the square root of the weapon's mass. It needs power and must be working; it looks a few times a second, on the server, and a
/// move is a structure change (the part comes off the magazine and goes on the launcher, as a hand would hang it).
/// The hoist's own <see cref="CPart.Cool"/> holds the ticks left of the move under way (0: none), so it lives through the rebuild.
/// </summary>
public static class ConstructHoists
{
    public const int LookEvery = World.TickRate / 4; // ticks between a hoist's looks
    public const int IdleLooks = 8; // looks a hoist skips after one that found nothing to move (2 s)

    /// <summary>Server, each tick: every few ticks, each body's hoists look and move.</summary>
    public static void Step(ConstructWorld world)
    {
        if (++world.HoistTicks < LookEvery)
            return;
        world.HoistTicks = 0;
        var bodies = world.Bodies;
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
            if (c.HoistCount == 0)
                continue;
            if (c.Dirty)
                c.Rebuild();
            for (var k = 0; k < c.HoistCount; k++)
                if (StepHoist(world, c, ref c.Hoists[k]))
                    break; // (the parts were renumbered: the rest look next time)
        }
    }

    /// <summary>Is the hoist working and powered (as every electric part: a battery's links reach it and the power is on, or the construct isn't electrical)?</summary>
    public static bool Powered(Construct c, int part) => c.Parts[part].Working && ConstructPower.On(c, part);

    /// <summary>One hoist's look. True when it moved a weapon (the body's parts changed).</summary>
    private static bool StepHoist(ConstructWorld world, Construct c, ref CHoist h)
    {
        ref var hp = ref c.Parts[h.Part];
        if (h.Idle > 0)
        {
            h.Idle--; // (the magazine, the launcher and what's in its way change through a rebuild, which makes a new entry: a repair or a re-powered rack waits this long)
            return false;
        }
        if (!Powered(c, h.Part) || !c.Parts[h.Launcher].Working)
        {
            hp.Cool = 0; // (it can't: a move under way starts again)
            return false;
        }
        if (!Next(c, h, out var weapon, out var socket, out var at, out var rot))
        {
            hp.Cool = 0;
            h.Idle = IdleLooks; // (nothing to move: the search is a room check a weapon a station, so it waits for a change)
            return false;
        }
        if (hp.Cool == 0)
        {
            hp.Cool = (ushort)Math.Clamp((int)MathF.Round(Seconds(c.DefOf(weapon)) * World.TickRate), LookEvery + 1, ushort.MaxValue);
            return false;
        }
        if (hp.Cool > LookEvery)
        {
            hp.Cool -= LookEvery;
            return false;
        }
        hp.Cool = 0;
        return Move(world, c, h.Launcher, weapon, socket, at, rot);
    }

    /// <summary>A move's time (s): ShipWorks.json's hoistSeconds for a weapon of hoistMass, growing with the square root of the mass.</summary>
    public static float Seconds(ConstructPartDef weapon)
    {
        var t = ShipWorks.Current;
        return t.HoistSeconds * MathF.Sqrt(MathF.Max(1, weapon.Mass) / t.HoistMass);
    }

    /// <summary>The magazine's first weapon that the hoist can lift and that fits an empty station of the launcher, and where it goes.</summary>
    public static bool Next(Construct c, CHoist h, out int weapon, out int socket, out Vector3 at, out byte rot)
    {
        (weapon, socket, at, rot) = (-1, -1, default, 0);
        var serves = c.DefOf(h.Part).WorksServes;
        Span<ushort> held = stackalloc ushort[ConstructOrdnance.MaxHeld];
        for (var e = c.FirstLink(h.Part); e >= 0; e = c.NextLink(e))
        {
            var mag = c.OtherEnd(e);
            if (mag == h.Launcher || c.DefOf(mag).Logic != CLogic.BombRack || !c.Parts[mag].Working)
                continue;
            var n = ConstructOrdnance.Held(c, mag, held);
            for (var j = 0; j < n; j++)
            {
                var d = c.DefOf(held[j]);
                if (d.Mass > serves)
                    continue;
                if (ConstructOrdnance.StationFor(c, h.Launcher, d, out socket, out at, out rot))
                {
                    weapon = held[j];
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>The weapons in a hoist's magazine (its racks' held parts).</summary>
    public static int InMagazine(Construct c, CHoist h)
    {
        var count = 0;
        Span<ushort> held = stackalloc ushort[ConstructOrdnance.MaxHeld];
        for (var e = c.FirstLink(h.Part); e >= 0; e = c.NextLink(e))
        {
            var mag = c.OtherEnd(e);
            if (mag != h.Launcher && c.DefOf(mag).Logic == CLogic.BombRack && c.Parts[mag].Working)
                count += ConstructOrdnance.Held(c, mag, held);
        }
        return count;
    }

    /// <summary>The weapon goes on the launcher's station (a new part, its health and paint kept) and comes off the magazine.</summary>
    private static bool Move(ConstructWorld world, Construct c, int launcher, int weapon, int socket, Vector3 at, byte rot)
    {
        var def = c.DefOf(weapon);
        var lug = ConstructOrdnance.LugOf(def);
        var (health, paint) = (c.Parts[weapon].Health, c.Parts[weapon].Paint);
        var i = world.AddPart(c, def, at, rot, c.Parts[launcher].Seg);
        if (i < 0)
            return false;
        world.AddLink(c, launcher, socket, i, lug);
        c.Parts[i].Health = health;
        c.Parts[i].Paint = paint;
        world.RemovePart(c, weapon); // (the new part, last, moves down one)
        c.Wake();
        return true;
    }

    /// <summary>The hoist's look line: what it serves, its launcher and magazine, and why it waits.</summary>
    public static string Look(Construct c, int part, string wrecked, string? action)
    {
        var d = c.DefOf(part);
        var k = Of(c, part);
        if (k < 0)
            return SandboxKeys.Line(d.Name, SandboxKeys.Join($"lifts {d.WorksServes:0} kg", wrecked), "no launcher on its mount: bolt it behind a Hull Mount a launcher rack is on", action);
        var h = c.Hoists[k];
        var mag = InMagazine(c, h);
        var state = !c.Parts[part].Working ? "" : !Powered(c, part) ? "no power"
            : mag == 0 ? "its magazine is empty: hang weapons on the racks bolted to it"
            : Next(c, h, out var w, out _, out _, out _) ? $"loading the {c.DefOf(h.Launcher).Name} ({Seconds(c.DefOf(w)):0} s a load)"
            : $"the {c.DefOf(h.Launcher).Name} is full";
        return SandboxKeys.Line(d.Name, SandboxKeys.Join($"lifts {d.WorksServes:0} kg, {mag} in its magazine", wrecked), state, action);
    }

    /// <summary>The hoist entry of part <paramref name="part"/>, or -1.</summary>
    public static int Of(Construct c, int part)
    {
        for (var k = 0; k < c.HoistCount; k++)
            if (c.Hoists[k].Part == part)
                return k;
        return -1;
    }
}
