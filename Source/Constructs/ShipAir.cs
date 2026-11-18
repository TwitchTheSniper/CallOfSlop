namespace CallOfSlop;

/// <summary>What a part is to a ship's air ("air" in its file; Source/PartKit/Generators/EvaParts.cs).</summary>
public enum AirPart : byte
{
    None,
    Unit, // a Life Support Unit: scrubs and circulates the ship's air, with a small oxygen reserve of its own; without one working, a ship is vacuum
    Tank, // an Oxygen Tank: more oxygen for the unit to give out
}

/// <summary>
/// A ship's air (Karl, 2026-11-18; docs/sandbox/EVA.md "Ship air"). A body with a working Life Support Unit and oxygen left in
/// its unit and Oxygen Tanks is pressurised: whoever is aboard breathes it, suit or not, and a suit refills from it. Everyone
/// aboard who breathes uses <see cref="PerPersonMinute"/> a minute, crew and prisoners as well as players, so the more aboard the
/// sooner it is gone; once it is, the ship is vacuum again (suits on their own air, the rest hold their breath). The oxygen is
/// kept in each air part's <see cref="CPart.Rounds"/>, in person-minutes, so it is saved and synced as a magazine is. A craft
/// carried or gripped by another (a docked Shuttle, a Limpet on a hull) breathes its carrier's air when it has none of its own.
/// The unit refills the tanks from outside air (a planet's) at <see cref="RefillPerSecond"/>. Creative keeps it full.
/// </summary>
public static class ShipAir
{
    public const float PerPersonMinute = 1f; // oxygen a person aboard breathes a minute (the unit of everything here: a person-minute)
    public const ushort UnitHolds = 240, TankHolds = 720; // a Life Support Unit's own reserve (4 person-hours), an Oxygen Tank's (12)
    public const float SuitFillCost = 1f / 60f; // person-minutes for each second of suit air a suit takes from the ship (a full suit: 30)
    public const float RefillPerSecond = 12f; // what a working unit draws in from breathable air outside (an Oxygen Tank full in a minute)
    public const int SurveyMs = 250; // how stale a body's sums may be for the HUD and the breath rule

    /// <summary>With the caches: which parts hold the body's air.</summary>
    public static void Build(Construct c)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.Air != AirPart.None)
                n++;
        if (n == 0)
        {
            c.AirIdx = [];
            c.AirSurveyMs = 0;
            return;
        }
        if (c.AirIdx.Length != n)
            c.AirIdx = new ushort[n];
        n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.Air != AirPart.None)
                c.AirIdx[n++] = (ushort)i;
        c.AirSurveyMs = 0;
    }

    /// <summary>The body's oxygen, the most it holds, and whether a Life Support Unit works (summed again when stale or <paramref name="now"/>).</summary>
    public static void Survey(Construct c, bool now = false)
    {
        var ms = Environment.TickCount64;
        if (!now && c.AirSurveyMs != 0 && ms - c.AirSurveyMs < SurveyMs)
            return;
        c.AirSurveyMs = ms == 0 ? 1 : ms;
        int units = 0, max = 0;
        var unit = false;
        foreach (var i in c.AirIdx)
        {
            if (i >= c.PartCount)
                continue;
            ref readonly var p = ref c.Parts[i];
            if (p.Broken)
                continue;
            var d = p.D;
            units += Math.Min(p.Rounds, d.AirHolds);
            max += d.AirHolds;
            unit |= d.Air == AirPart.Unit;
        }
        (c.AirUnits, c.AirMax, c.AirWorks) = (units, max, unit);
    }

    /// <summary>Has this body a working Life Support Unit (oxygen or not)?</summary>
    public static bool HasLifeSupport(Construct c)
    {
        if (c.AirIdx.Length == 0)
            return false;
        Survey(c);
        return c.AirWorks;
    }

    /// <summary>The body whose air someone aboard <paramref name="c"/> breathes: its own when it has life support, else its carrier's (a docked or gripped craft), else null.</summary>
    public static Construct? AirBody(ConstructWorld world, Construct? c)
    {
        for (var hop = 0; c != null && hop < 3; hop++)
        {
            if (HasLifeSupport(c))
                return c;
            c = c.CarriedBy != 0 ? world.Get(c.CarriedBy) : null;
        }
        return null;
    }

    /// <summary>Is there air to breathe in this body: a working unit and oxygen left?</summary>
    public static bool Pressurised(Construct? c)
    {
        if (c == null || c.AirIdx.Length == 0)
            return false;
        Survey(c);
        return c.AirWorks && c.AirUnits > 0;
    }

    /// <summary>The body a player is aboard: the one they sit in, else the one they stand or float in (0: none).</summary>
    public static ushort Aboard(Player p, in SandboxHand h) => h.Seated && h.SeatBody != 0 ? h.SeatBody : p.Sim.RideBody;

    /// <summary>Takes up to <paramref name="units"/> of oxygen from the body: its tanks first, the unit's own reserve last. Returns what it took.</summary>
    public static int Take(ConstructWorld world, Construct c, int units)
    {
        var took = 0;
        for (var pass = 0; pass < 2 && took < units; pass++)
            foreach (var i in c.AirIdx)
            {
                if (took >= units)
                    break;
                if (i >= c.PartCount)
                    continue;
                ref var p = ref c.Parts[i];
                if (p.Broken || p.Rounds == 0 || (pass == 0) != (p.D.Air == AirPart.Tank))
                    continue;
                var t = Math.Min(units - took, (int)p.Rounds);
                p.Rounds -= (ushort)t;
                took += t;
                world.NoteAmmo(c, i);
            }
        if (took > 0)
            Survey(c, now: true);
        return took;
    }

    /// <summary>Puts up to <paramref name="units"/> of oxygen into the body: the unit's reserve first, then the tanks. Returns what went in.</summary>
    public static int Fill(ConstructWorld world, Construct c, int units)
    {
        var put = 0;
        for (var pass = 0; pass < 2 && put < units; pass++)
            foreach (var i in c.AirIdx)
            {
                if (put >= units)
                    break;
                if (i >= c.PartCount)
                    continue;
                ref var p = ref c.Parts[i];
                var room = p.D.AirHolds - p.Rounds;
                if (p.Broken || room <= 0 || (pass == 0) != (p.D.Air == AirPart.Unit))
                    continue;
                var t = Math.Min(units - put, room);
                p.Rounds += (ushort)t;
                put += t;
                world.NoteAmmo(c, i);
            }
        if (put > 0)
            Survey(c, now: true);
        return put;
    }

    /// <summary>How long the air lasts at <paramref name="aboard"/> people, as words ("3 h 20 min"), or null with nobody aboard.</summary>
    public static string? Lasts(int units, int aboard)
    {
        if (aboard <= 0)
            return null;
        var minutes = (int)(units / (aboard * PerPersonMinute));
        return minutes >= 60 ? $"{minutes / 60} h {minutes % 60:00} min" : $"{minutes} min";
    }

    /// <summary>The air's state in words: "Ship air 74%, 3 h 20 min for 9 aboard" (for the HUD and the unit's F).</summary>
    public static string Line(Construct c)
    {
        Survey(c);
        if (!c.AirWorks)
            return "Ship air: no working Life Support Unit (vacuum aboard)";
        if (c.AirUnits <= 0)
            return "SHIP AIR GONE: vacuum aboard (refill in a planet's air, or wear a suit)";
        var share = c.AirMax > 0 ? c.AirUnits * 100 / c.AirMax : 0;
        var aboard = Math.Max(1, (int)c.AirAboard);
        return $"Ship air {share}%, {Lasts(c.AirUnits, aboard)} for {aboard} aboard";
    }
}

public sealed partial class Construct
{
    public ushort[] AirIdx = []; // the parts that hold its air (ShipAir.Build, with the caches)
    public int AirUnits, AirMax; // its oxygen now and full (person-minutes), as last summed
    public bool AirWorks; // a Life Support Unit works
    public long AirSurveyMs; // when they were summed (Environment.TickCount64; 0: never)
    public byte AirAboard; // who breathed it at the last count (the server's; a client counts the people it sees)
    public float AirOwed; // server: oxygen breathed but not yet taken from the parts (under one person-minute)
}
