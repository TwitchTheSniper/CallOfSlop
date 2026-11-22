using System.Diagnostics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// <c>--economy-sim [--hours H] [--map NAME] [--start ID] [--warp W]</c>: a headless host plays a Survival world with nobody at the keys for H hours of
/// game time (default 4) as fast as the machine runs it: the stations' needs, their haulers flying their routes, the powers' controllers, the
/// territory's goods, consignments, camps and hardship, all the real code. Every hour of play it writes each station's stock, line by line,
/// to <c>economy-sim.txt</c> next to the DLL, and at the end the lines that ran dry or filled up. <c>--warp W</c> asks for space time W times
/// faster (as a player at the command view's time speed: legs between planets take months of space time; the rule slows it while ships thrust). A multi-minute run: ask the user first.
/// </summary>
public static class EconomySimCheck
{
    public static int Cli(string[] args)
    {
        string? Val(string flag) => args.SkipWhile(a => !a.Equals(flag, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var hours = double.TryParse(Val("--hours"), System.Globalization.CultureInfo.InvariantCulture, out var h) ? Math.Clamp(h, 0.1, 48) : 4;
        Space.StarSystems.Reload();
        if (Maps.Find(Val("--map") ?? "Caledon") is not { } map)
        {
            Console.WriteLine("[economy-sim] no such map");
            return 1;
        }
        Game.SeedNextGame = 7;
        using var g = new Game(NetPeer.Offline(), new Audio(), "Keeper", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id) { MapVoteEnabled = false };
        g.SetSandboxSurvival(true); // (before the mode: setting it starts the world, and its opening places the start's stations)
        g.SetStoryStart(Val("--start") ?? "Interactions");
        g.Mode = GameMode.Sandbox;
        double now = 0;
        void Step()
        {
            now += World.TickDt;
            g.Update(now, World.TickDt);
        }
        for (var i = 0; i < 3600 && g.Phase != MatchPhase.Playing; i++)
            Step();
        World.Enter(g.Place);
        var warp = int.TryParse(Val("--warp"), out var wv) ? Math.Clamp(wv, 1, Space.SpaceClock.MaxSpeed) : 1;
        var outPath = Path.Combine(AppContext.BaseDirectory, "economy-sim.txt");
        var sb = new StringBuilder();
        var all = StationMarkets.All;
        var low = new int[all.Length][];
        var high = new int[all.Length][];
        for (var d = 0; d < all.Length; d++)
            (low[d], high[d]) = (new int[all[d].Goods.Length + all[d].Parts.Length], new int[all[d].Goods.Length + all[d].Parts.Length]);
        var wall = Stopwatch.StartNew();
        var perHour = 3600 * World.TickRate;
        var total = (long)(hours * perHour);
        for (long t = 0; t < total; t++)
        {
            if (warp > 1 && t % World.TickRate == 0)
                g.RequestTimeSpeed(warp);
            Step();
            if ((t + 1) % perHour != 0 && t + 1 != total)
                continue;
            var hour = (t + 1) / (double)perHour;
            sb.Append($"== hour {hour:0.0} ({wall.Elapsed.TotalMinutes:0.0} min wall; space day {g.EconomySimSpaceDays():0.0})\n");
            for (var d = 0; d < all.Length; d++)
            {
                var m = g.EconomySimMarket(d);
                sb.Append($"  {all[d].Name} ({all[d].Power}):");
                for (var k = 0; k < all[d].Goods.Length && k < m.Length; k++)
                {
                    var gd = all[d].Goods[k];
                    sb.Append($" {RecyclingTable.Current.NameOf(gd.Material)} {m[k]}");
                    if (m[k] <= 0)
                        low[d][k]++;
                    else if (m[k] >= gd.Most)
                        high[d][k]++;
                }
                int parts = 0, none = 0;
                for (var j = 0; j < all[d].Parts.Length && all[d].Goods.Length + j < m.Length; j++)
                {
                    var k = all[d].Goods.Length + j;
                    parts += Math.Max(0, m[k]);
                    if (m[k] <= 0)
                    {
                        low[d][k]++;
                        none++;
                    }
                }
                if (all[d].Parts.Length > 0)
                    sb.Append($" | {parts} parts in stock, {none} of {all[d].Parts.Length} lines empty");
                sb.Append('\n');
            }
            sb.Append(g.EconomySimPowers()).Append('\n');
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine($"[economy-sim] hour {hour:0.0} of {hours:0.0} after {wall.Elapsed.TotalMinutes:0.0} min");
        }
        sb.Append("== lines empty or full at the hourly looks\n");
        for (var d = 0; d < all.Length; d++)
            for (var k = 0; k < low[d].Length; k++)
            {
                var name = k < all[d].Goods.Length ? RecyclingTable.Current.NameOf(all[d].Goods[k].Material) : ConstructParts.Def(all[d].Parts[k - all[d].Goods.Length].Part)?.Name ?? "?";
                if (low[d][k] > 0)
                    sb.Append($"  EMPTY {all[d].Name}: {name} at {low[d][k]} look(s)\n");
                if (high[d][k] > 0)
                    sb.Append($"  FULL  {all[d].Name}: {name} at {high[d][k]} look(s)\n");
            }
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"[economy-sim] done: {hours:0.0} h of play in {wall.Elapsed.TotalMinutes:0.0} min; {outPath}");
        return 0;
    }
}

public sealed partial class Game
{
    /// <summary>The economy sim: station <paramref name="def"/>'s stock by line (the host's market).</summary>
    internal int[] EconomySimMarket(int def) => MarketOfDef(def).Stock;

    /// <summary>The economy sim: space time since the epoch, in days (0 with no space here).</summary>
    internal double EconomySimSpaceDays() => Space?.Flight is { } f ? f.Clock.Now.Ns / 1e9 / 86400 : 0;

    /// <summary>The economy sim: each power's books, the camps and the hardship, in a line or two.</summary>
    internal string EconomySimPowers()
    {
        var sb = new StringBuilder("  powers:");
        for (byte p = 1; p <= PowerControllers.N; p++)
            if (Controllers.Powers[p] is { Plan: not null } pw && CrewPowers.Of(p) is { } def)
                sb.Append($" {def.Name} {pw.Stock:0} ({pw.ShipCount()} ships)");
        sb.Append("; hardship:");
        foreach (var (planet, hd) in _hardship)
            sb.Append($" {planet} {hd:0.00}");
        sb.Append($"; bands {_bands}; consignments {_consigns.Count}; hauls {_hauls.Count}");
        return sb.ToString();
    }
}
