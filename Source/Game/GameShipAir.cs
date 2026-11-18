namespace CallOfSlop;

/// <summary>
/// A ship's air, the game's side (docs/sandbox/EVA.md "Ship air"; the rules are Source/Constructs/ShipAir.cs): once a second the
/// server counts who breathes each body's air (players aboard, its crew and prisoners) and takes their oxygen from its tanks;
/// a suit filling aboard is billed to the ship; a unit in a planet's air refills its tanks; creative keeps them full.
/// </summary>
public sealed partial class Game
{
    /// <summary>The body whose air <paramref name="p"/> breathes (its Life Support Unit works), or null: their own, or the carrier of the craft they're in.</summary>
    public Construct? ShipAirOf(Player p) =>
        ShipAir.Aboard(p, SandboxHands.Hand(p.Id)) is var b and not 0 ? ShipAir.AirBody(Constructs, Constructs.Get(b)) : null;

    /// <summary>Is <paramref name="p"/> breathing a ship's air this tick?</summary>
    public bool OnShipAir(Player p) => ShipAir.Pressurised(ShipAirOf(p));

    /// <summary>Server, each tick: once a second, who breathes what, and the oxygen it costs.</summary>
    private void ShipAirTick()
    {
        if (ServerTick % World.TickRate != 0)
            return;
        var bodies = Constructs.Bodies;
        for (var k = 0; k < bodies.Count; k++)
            if (bodies[k].AirIdx.Length > 0)
                bodies[k].AirCount = 0;
        foreach (var p in Players.Values)
            if (p.Bot == null && Eva.Breathes(p) && ShipAirOf(p) is { } b)
                b.AirCount++;
        for (var k = 0; k < Crew.ShipHigh; k++)
        {
            if (Crew.Ships[k] is not { } s || ShipAir.AirBody(Constructs, Constructs.Get(s.Body)) is not { } b)
                continue;
            for (var i = 0; i < s.MemberCount; i++)
                if (Crew.State[s.Members[i]] != CrewState.Dead)
                    b.AirCount++; // (its crew, and its prisoners: surrendered members held aboard)
        }
        var creative = SandboxInfiniteParts;
        for (var k = 0; k < bodies.Count; k++)
        {
            var c = bodies[k];
            if (c.AirIdx.Length == 0)
                continue;
            ShipAir.Survey(c, now: true);
            c.AirAboard = (byte)Math.Min(255, c.AirCount);
            if (creative)
            {
                c.AirOwed = 0;
                if (c.AirUnits < c.AirMax)
                    ShipAir.Fill(Constructs, c, c.AirMax);
                continue;
            }
            if (!c.AirWorks)
            {
                c.AirOwed = 0;
                continue;
            }
            if (!Eva.Vacuum(c.WorldCom))
            {
                c.AirOwed = 0; // (in a planet's air the unit breathes it in: nothing used, the tanks refill)
                if (c.AirUnits < c.AirMax)
                    ShipAir.Fill(Constructs, c, (int)ShipAir.RefillPerSecond);
                continue;
            }
            c.AirOwed += c.AirCount * ShipAir.PerPersonMinute / 60f;
            if (c.AirOwed < 1)
                continue;
            var take = (int)c.AirOwed;
            c.AirOwed -= take;
            var had = c.AirUnits;
            ShipAir.Take(Constructs, c, take);
            if (had > 0 && c.AirUnits <= 0)
            {
                Constructs.Events.Add($"construct {c.Id}: its air ran out ({c.AirCount} aboard)");
                foreach (var p in Players.Values)
                    if (p.Bot == null && ShipAirOf(p) == c)
                        SandboxHands.Notices.Add((p.Id, "The ship's air has run out: suit up or get to air"));
            }
            else if (had * 10 > c.AirMax && c.AirUnits * 10 <= c.AirMax)
                foreach (var p in Players.Values)
                    if (p.Bot == null && ShipAirOf(p) == c)
                        SandboxHands.Notices.Add((p.Id, $"Ship air low: {ShipAir.Line(c)}"));
        }
    }

    /// <summary>Server: suit air taken aboard is billed to the ship that gave it.</summary>
    private void ShipAirBillSuit(Player p, float suitSeconds)
    {
        if (suitSeconds > 0 && ShipAirOf(p) is { } c && !SandboxInfiniteParts)
            c.AirOwed += suitSeconds * ShipAir.SuitFillCost;
    }

    /// <summary>Server, F on a Life Support Unit or an Oxygen Tank: what the ship's air is doing.</summary>
    private string? ShipAirUse(Player p, Construct c, int part)
    {
        var d = c.DefOf(part);
        if (c.Parts[part].Broken)
            return $"the {d.Name} is wrecked";
        ShipAir.Survey(c, now: true);
        var own = $"{d.Name}: {Math.Min(c.Parts[part].Rounds, d.AirHolds) * 100 / Math.Max(1, (int)d.AirHolds)}% ({c.Parts[part].Rounds / 60} of {d.AirHolds / 60} person-hours)";
        SandboxHands.Notices.Add((p.Id, $"{own}. {ShipAir.Line(c)}"));
        return null;
    }
}

public sealed partial class Construct
{
    public int AirCount; // server: who breathes its air, counted once a second (ShipAir.cs; then AirAboard)
}
