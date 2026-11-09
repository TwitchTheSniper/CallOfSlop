namespace CallOfSlop;

/// <summary>
/// Tool upgrades on the game's side (docs/sandbox/TOOL-UPGRADES.md; the rules: Constructs/ConstructEditsUpgrades.cs): a
/// person's levels go into their record in a saved world and come back when they arrive, so each player keeps their own
/// upgrades from session to session and in co-op. The levels reach clients in the hands state (SandboxModule.WriteState).
/// </summary>
public sealed partial class Game
{
    /// <summary>Server: what goes in a person's record when the world is saved or they leave.</summary>
    private void UpgradesCatch(Player p, WorldPlayer rec)
    {
        var levels = SandboxHands.LevelsOf(p.Id)[..(int)ToolStat.COUNT];
        if (rec.Upgrades.Length != levels.Length)
            rec.Upgrades = new byte[levels.Length];
        levels.CopyTo(rec.Upgrades);
    }

    /// <summary>Server: a person arrived in a saved world: their levels from their record (a new person: the base).</summary>
    private void UpgradesArrive(Player p, WorldPlayer rec)
    {
        SandboxHands.ResetLevels(p.Id);
        var levels = SandboxHands.LevelsOf(p.Id);
        var table = ToolUpgrades.Current;
        for (var s = 0; s < rec.Upgrades.Length && s < (int)ToolStat.COUNT; s++)
            levels[s] = (byte)Math.Min(rec.Upgrades[s], table.Of((ToolStat)s).Levels.Length); // (a level the table no longer has: its top)
    }

    /// <summary>A saved world's tool upgrades (WorldSaveWriter.ToolsTag): each person's levels, onto their record.</summary>
    private static void WorldReadUpgrades(SaveReader r, WorldSession ws)
    {
        var n = r.Count(17);
        for (var k = 0; k < n; k++)
        {
            var key = r.U64();
            var steam = r.U64();
            var levels = r.Bytes(r.U8());
            foreach (var rec in ws.Players)
                if (rec.Key == key && rec.SteamId == steam)
                {
                    rec.Upgrades = levels;
                    break;
                }
        }
    }
}
