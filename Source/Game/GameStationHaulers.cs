using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Station haulers (docs/sandbox/design/faction-controllers.md "Stations", phase S2; Karl, 2026-11-18: "Haulers from other neutral factions
/// can then send ships to buy goods and take them back to their station", then "we will then start testing logistics runs with ships").
/// Each route in Crew/Stations.json's "haulers" has real ships, crewed by its power, built beside its first station with a ship start's
/// stations. A hauler docks there with the command view's own Dock order, sells what it carries that the station buys, buys the route's
/// goods (what the station asks, paid from its power's stock), waits, and docks at the other end to do the same with the goods for the run
/// home. The stock moves between the two markets, and the powers' books carry it. Not saved: a loaded world's haulers are plain crewed ships.
/// </summary>
public sealed partial class Game
{
    private enum HaulStage : byte { Going, Moored }

    private sealed class HaulRun
    {
        public ushort Ship;
        public int Route;
        public bool AtTo; // the leg it is on: false, to the route's first station ("from"); true, to its second ("to")
        public HaulStage Stage;
        public long Since, RetryAt;
        public string Last = ""; // the last refusal logged (so a refusal repeated every try is logged once)
    }

    private readonly List<HaulRun> _hauls = new(8);

    /// <summary>Host: the body of the station whose market is <paramref name="def"/> (0: none here).</summary>
    private ushort StationBodyOf(int def)
    {
        foreach (var m in StationMarketsHere.Values)
            if (m.Def == def && Constructs.Get(m.Body) is { Wreck: false })
                return m.Body;
        return 0;
    }

    /// <summary>
    /// Server, after a ship start's stations are built: each hauler route's ships, a kilometre and a half apart behind its first station on its
    /// orbit, going round with it, nobody's and crewed by the route's power. A route whose station, ship or power is missing is logged and left out.
    /// </summary>
    private void OpeningHaulers(Vector3 centre)
    {
        var routes = StationMarkets.Haulers;
        for (var ri = 0; ri < routes.Length; ri++)
        {
            var h = routes[ri];
            var power = CrewPowers.Find(h.Power);
            var from = StationBodyOf(h.From);
            if (power == 0 || from == 0 || StationBodyOf(h.To) == 0 || Constructs.Get(from) is not { } st || ConstructBuild.FindBlueprint(h.Ship) is not { } bp)
            {
                Log($"[haul] route {ri} ({h.Power}, {StationMarkets.All[h.From].Name} to {StationMarkets.All[h.To].Name}) left out: {(power == 0 ? "no such power" : from == 0 || StationBodyOf(h.To) == 0 ? "a station isn't in this start" : "no blueprint " + h.Ship)}");
                continue;
            }
            var up = Vector3.Normalize(st.Pos - centre);
            var fwd = st.Vel.LengthSquared() > 1 ? Vector3.Normalize(st.Vel - up * Vector3.Dot(st.Vel, up)) : Vector3.Normalize(Vector3.Cross(up, Vector3.UnitX));
            var z = -fwd;
            var x = Vector3.Cross(up, z);
            var rot = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, up.X, up.Y, up.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
            for (var k = 0; k < h.Count; k++)
            {
                var c = ConstructBuild.Build(Constructs, bp, st.Pos - fwd * (1500f + 900f * k), rot, out var why);
                if (c == null)
                {
                    Log($"[haul] a {h.Ship} for {h.Power} didn't build in orbit: {why}");
                    continue;
                }
                c.Rebuild();
                c.Owner = 255; // (nobody's: the power's)
                c.Vel = st.Vel;
                c.PrevPos = c.Pos;
                c.Wake();
                var crew = Crew.Crew(c, ShipAiMode.Strangers, faction: power);
                if (Crew.Events.Count > 0)
                    CrewLog();
                _hauls.Add(new HaulRun { Ship = c.Id, Route = ri, RetryAt = ServerTick + 5 * World.TickRate });
                Log($"[haul] construct {c.Id}, a {h.Ship} crewed by {h.Power} ({(crew != null ? Crew.Alive(crew) : 0)} aboard), hauls between {StationMarkets.All[h.From].Name} and {StationMarkets.All[h.To].Name}");
            }
        }
    }

    /// <summary>The route a hauler runs and the station it is bound for, for the command view's card (null: it is no hauler).</summary>
    public string? HaulLine(ushort ship)
    {
        foreach (var run in _hauls)
            if (run.Ship == ship)
            {
                var h = StationMarkets.Haulers[run.Route];
                return run.Stage == HaulStage.Moored ? "TRADING AT " + StationMarkets.All[run.AtTo ? h.To : h.From].Name.ToUpperInvariant()
                    : "HAULING TO " + StationMarkets.All[run.AtTo ? h.To : h.From].Name.ToUpperInvariant();
            }
        return null;
    }

    /// <summary>Host, once a second while space is up: each hauler's leg (a Dock order to the station it is bound for), its trade once moored, and the next leg.</summary>
    private void StationHaulTick(SpaceSession s)
    {
        for (var i = _hauls.Count - 1; i >= 0; i--)
        {
            var run = _hauls[i];
            if (Constructs.Get(run.Ship) is not { Wreck: false, PartCount: > 0 } c)
            {
                Log($"[haul] construct {run.Ship} is gone: its route ends");
                _hauls.RemoveAt(i);
                continue;
            }
            var h = StationMarkets.Haulers[run.Route];
            var def = run.AtTo ? h.To : h.From;
            var st = StationBodyOf(def);
            if (st == 0)
                continue; // (its station is gone: it waits where it is)
            if (c.MooredTo == st)
            {
                if (run.Stage != HaulStage.Moored)
                {
                    run.Stage = HaulStage.Moored;
                    run.Since = ServerTick;
                    HaulTrade(run, c, h, def);
                }
                else if (ServerTick - run.Since >= (long)(h.Wait * World.TickRate))
                {
                    run.AtTo = !run.AtTo; // (the next leg: its order casts it off, NavApply)
                    run.Stage = HaulStage.Going;
                    run.RetryAt = 0;
                }
                if (run.Stage == HaulStage.Moored)
                    continue;
                def = run.AtTo ? h.To : h.From;
                st = StationBodyOf(def);
                if (st == 0)
                    continue;
            }
            run.Stage = HaulStage.Going;
            if (NavOf(c.Id) != null || ServerTick < run.RetryAt)
                continue;
            if (c.Dirty)
                c.Rebuild();
            var why = NavApply(s, c, NavKind.Dock, 0, Vec3d.Zero, 0, 255, h.Power + " hauler", null, out _, st);
            run.RetryAt = ServerTick + 30 * World.TickRate; // (an order that ends without docking is given again after this)
            var line = why ?? "";
            if (line != run.Last)
                Log(why == null ? $"[haul] construct {c.Id} docks at {StationMarkets.All[def].Name}" : $"[haul] construct {c.Id} can't dock at {StationMarkets.All[def].Name}: {why}");
            run.Last = line;
        }
    }

    /// <summary>Host: a hauler moored at its station: it sells what it carries that the station buys, then loads the leg's goods home, paying from its power.</summary>
    private void HaulTrade(HaulRun run, Construct c, StationHaulerDef h, int def)
    {
        if (StationMarketsHere.GetValueOrDefault(StationBodyOf(def)) is not { } m)
            return;
        var name = StationMarkets.All[def].Name;
        var pw = Controllers.Powers[CrewPowers.Find(h.Power)];
        Span<int> sold = stackalloc int[StationMarkets.All[def].Goods.Length];
        var value = StationTakeCargo(m, c, sold);
        if (value > 0)
        {
            if (pw is { Fallen: false })
            {
                pw.Stock += value;
                pw.Earned += value;
            }
            StationsVersion++;
            Log($"[haul] construct {c.Id} ({h.Power}) sold {StationGoodsText(m, sold)} to {name} for {value:0} credits");
        }
        var goods = StationMarkets.All[def].Goods;
        Span<int> bought = stackalloc int[goods.Length];
        double cost = 0;
        foreach (var mat in run.AtTo ? h.Back : h.Goods)
            for (var k = 0; k < goods.Length; k++)
                if (goods[k].Material == mat && goods[k].Sell > 0)
                {
                    var afford = pw is { Fallen: false } ? (int)Math.Min(int.MaxValue, Math.Max(0, Math.Floor((pw.Stock - cost) / goods[k].Sell))) : 0;
                    var n = StationLoadCargo(m, c, k, afford);
                    bought[k] += n;
                    cost += n * (double)goods[k].Sell;
                    break;
                }
        if (cost > 0 && pw != null)
        {
            pw.Stock -= cost;
            pw.Spent += cost;
            Log($"[haul] construct {c.Id} ({h.Power}) bought {StationGoodsText(m, bought)} at {name} for {cost:0} credits");
        }
        else if (value <= 0)
            Log($"[haul] construct {c.Id} ({h.Power}) traded nothing at {name}");
    }
}
