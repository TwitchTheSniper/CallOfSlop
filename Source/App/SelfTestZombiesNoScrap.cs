using System.Numerics;
using System.Reflection;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombies no scrap" (developer request, 2026-10-27: "remove the scrap feature ... the gambling
/// machines replace the need"): nothing in the Zombies code or its shipped tables knows scrap any more; older rules and
/// drop tables that still carry the scrap keys load (an old slot row's scrap weight counts as points, so its win chance
/// stays); a salvage pile gives a loose part once a round; and a short seeded run (two bots, the host pulling a slot
/// machine and searching a pile) never shows the word in a prompt or the feed, and the run message carries only parts.
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombiesNoScrap()
    {
        // ---- The code and the tables ----
        var types = new[] { typeof(ZPlayer), typeof(ZombiesRules), typeof(ZSlotMachine), typeof(PartDef), typeof(WeaponBuild), typeof(RunTables), typeof(BenchModel), typeof(ZSpot) };
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var members = types.SelectMany(t => t.GetMembers(All).Select(m => $"{t.Name}.{m.Name}")).Where(n => n.Contains("scrap", StringComparison.OrdinalIgnoreCase)).ToList();
        members.AddRange(Enum.GetNames<ZSlotResult>().Where(n => n.Contains("scrap", StringComparison.OrdinalIgnoreCase)));
        members.AddRange(typeof(Game).GetFields(All).Where(f => f.Name.StartsWith("SlotSym") && f.Name.Contains("Scrap")).Select(f => f.Name));
        var keys = RunTables.SlotKeys.Where(k => k.Contains("scrap")).ToList();
        var dataKeys = new List<string>();
        void Walk(JsonElement e, string at)
        {
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name.Contains("scrap", StringComparison.OrdinalIgnoreCase))
                        dataKeys.Add($"{at}.{p.Name}");
                    Walk(p.Value, $"{at}.{p.Name}");
                }
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var x in e.EnumerateArray())
                    Walk(x, at);
        }
        foreach (var file in new[] { ZombiesRules.PathName, RunTables.PathName })
            using (var doc = JsonDocument.Parse(File.ReadAllText(file)))
                Walk(doc.RootElement, Path.GetFileName(file));

        // ---- Older files still load ----
        var legacyRules = File.ReadAllText(ZombiesRules.PathName)
            .Replace("\"salvageParts\": 1", "\"scrapPerKill\": 1, \"salvageScrap\": 25")
            .Replace("\"dupePointsAt\": 0,", "\"scrapPayout\": 40, \"autoScrapAt\": 3,");
        string? legacyRulesWhy = null;
        try
        {
            ZombiesRules.Load(legacyRules, "old rules");
        }
        catch (WeaponFileException ex)
        {
            legacyRulesWhy = ex.Message;
        }
        var oldRow = RunTables.Load("""{"format": "call-of-slop-drops/1", "slotTiers": [{"fromRound": 1, "nothing": 30, "scrap": 22, "points": 13, "common": 35}]}""", "old drops");
        var oldPoints = oldRow.SlotTiers[0].Weights[RunTables.SlotPoints];
        var shipped = RunTables.Current.SlotTiers;
        var r1 = shipped.Count > 0 ? shipped[0].Weights : [];
        var winChance = r1.Length > 0 ? 1 - r1[RunTables.SlotNothing] / r1.Sum() : 0;
        ZLine($"no scrap: {members.Count} scrap member(s) in the code [{string.Join(", ", members)}], {keys.Count} slot key(s), {dataKeys.Count} key(s) in the shipped tables [{string.Join(", ", dataKeys)}]; "
              + $"old rules load '{legacyRulesWhy ?? "ok"}', an old slot row's scrap counts as points ({oldPoints}); round 1 slot win chance {winChance * 100:0.0}% (as before scrap went: 71.4%)");
        if (members.Count > 0 || keys.Count > 0 || dataKeys.Count > 0)
            Fail("Zombies should have no scrap left: no scrap fields, results, reel symbols, slot keys or table keys");
        if (legacyRulesWhy != null || oldPoints != 35)
            Fail("older rules files and drop tables with the scrap keys should still load (the scrap weight counting as points)");
        if (MathF.Abs(winChance - 75f / 105f) > 0.001f)
            Fail($"the slot machines' round 1 win chance should stay as it was before scrap went (71.4%), not {winChance * 100:0.0}%");

        // ---- A short seeded run: a salvage pile, slot pulls, two bots fighting; nothing says scrap ----
        var now = 0.0;
        if (ZTestMap?.Zombies is not { Salvage.Count: > 0, SlotMachines.Count: > 0 } zm)
        {
            Fail("Zombies Test should have a salvage pile and a slot machine");
            return;
        }
        using var g = ZGame(2, ref now, seed: 4242, tweak: r => r.FirstRoundDelay = 1);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var zp = g.ZP(me);
        var texts = new List<string>();
        var feed0 = g.Feed.Count;

        // The salvage pile: a loose part (rolled like a zombie drop), then picked clean for the round.
        Run(g, ref now, 1.5); // (round 1 starts: piles open once a round has begun)
        var pile = zm.Salvage[0];
        me.Teleport(pile.At + new Vector3(0, 0, 0.8f));
        var pilePrompt = g.FindZUse(me);
        var loose = zp.Parts.Values.Sum();
        g.SelfTestUse(me, now);
        var gotPart = zp.Parts.Values.Sum() == loose + g.ZRules.SalvageParts;
        var picked = g.FindZUse(me);
        texts.Add(pilePrompt?.Text ?? "");
        texts.Add(picked?.Text ?? "");

        // Slot pulls (the host), paying whatever comes up, while the bots hold the line.
        var spot = zm.SlotMachines[0];
        zp.Points = 1_000_000;
        var pulls = 0;
        for (var i = 0; i < 40; i++)
        {
            me.Teleport(spot.At + World.FlatForward(spot.Yaw) * 0.9f);
            if (g.FindZUse(me) is { Kind: ZUseKind.SlotMachine } su)
            {
                texts.Add(su.Text);
                if (!su.Blocked)
                {
                    g.SelfTestUse(me, now);
                    pulls++;
                }
            }
            Run(g, ref now, 0.5);
            texts.Add(g.SlotResultText(g.ZSlots[0]));
        }
        me.Teleport(zm.Benches[0].At + new Vector3(0, 0, -1.2f));
        texts.Add(g.FindZUse(me)?.Text ?? "");
        for (var i = feed0; i < g.Feed.Count; i++)
            texts.Add(g.Feed[i].Text);
        var said = texts.Where(t => t.Contains("scrap", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();

        // The run message: only the loose parts (protocol 56; a u16 count of kinds and a u32 count each since protocol 71), and a client reads them back.
        var w = new PacketWriter();
        g.SelfTestWriteRun(w, me);
        var msg = w.Span.ToArray();
        var expect = 3 + zp.Parts.Sum(kv => { var cw = new PacketWriter(); cw.Begin(Msg.ZRun).Str(kv.Key); return cw.Span.Length - 1 + 4; });
        using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
        c.SelfTestReadRun(msg);
        var cp = c.ZP(c.LocalId);
        var same = cp.Parts.Count == zp.Parts.Count && zp.Parts.All(kv => cp.Parts.GetValueOrDefault(kv.Key) == kv.Value);
        ZLine($"no scrap run (seed 4242, 2 bots, round {g.ZRound}): salvage pile '{pilePrompt?.Text}' -> a part {gotPart}, then '{picked?.Text}'; {pulls} slot pulls; {texts.Count} prompts and feed lines, "
              + $"{said.Count} saying scrap [{string.Join(" | ", said)}]; run message {msg.Length} bytes (parts only: {expect}), client copy {same}; protocol {ServerInfo.ProtocolVersion}");
        if (pilePrompt is not { Kind: ZUseKind.Salvage, Blocked: false } || !gotPart || picked is not { Kind: ZUseKind.Salvage, Blocked: true })
            Fail("a salvage pile should give a loose part once a round, then be picked clean");
        if (said.Count > 0 || pulls < 5)
            Fail("a Zombies run should never mention scrap (prompts, slot results, the feed)");
        if (msg.Length != expect || !same || ServerInfo.ProtocolVersion < 69)
            Fail("the run message should carry only the loose parts (protocol 56) and reach the client intact");
    }
}
