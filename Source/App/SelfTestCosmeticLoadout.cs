using Brutal.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Self-test for the hat (now on the APPEARANCE screen): appearance.txt round trip and the migration from loadout.txt's old
/// hat line, the server's hat check (locked hats and bots get none), the hat replicating to a client in the roster, the
/// APPEARANCE screen layout at the user's resolutions, and determinism. (The look itself: SelfTestAppearance.cs.)
/// </summary>
public static partial class SelfTest
{
    private static readonly RakNetGUID HatGuid = new() { G = 0x4A7, SystemIndex = 0 };

    private static void CheckCosmeticLoadout()
    {
        CosmeticSaveCheck();
        CosmeticServerCheck();
        CosmeticLayoutCheck();
        CosmeticDeterminismCheck();
    }

    // ---- appearance.txt (the hat moved there from loadout.txt) ------------------------------------

    private static void CosmeticSaveCheck()
    {
        var backup = AppearanceStore.SaveLines().ToArray();
        var loadoutBackup = LoadoutStore.SaveLines().ToArray();
        try
        {
            AppearanceStore.LoadLines(null, ["selected=0", "loadout1.name=Old", "loadout1.primary=Hornet SMG"]); // loadout.txt from before hats
            var oldHat = AppearanceStore.Hat;
            AppearanceStore.LoadLines(null, ["primary=Hornet SMG", "grenade=Frag"]); // the single-loadout format
            var veryOldHat = AppearanceStore.Hat;
            AppearanceStore.LoadLines(null); // brand-new player
            var freshHat = AppearanceStore.Hat;
            if (oldHat != HatId.None || veryOldHat != HatId.None || freshHat != HatId.None)
                Fail($"cosmetic save: files without a hat line should load as no hat, got {oldHat} / {veryOldHat} / {freshHat}");

            var rounds = new List<string>();
            foreach (var h in UiHats())
            {
                AppearanceStore.Hat = h;
                var lines = AppearanceStore.SaveLines();
                var hatLines = lines.Where(x => x.StartsWith("hat=", StringComparison.Ordinal)).ToList();
                AppearanceStore.LoadLines(lines.ToArray());
                rounds.Add($"{Cosmetics.KeyOf(h)}->{AppearanceStore.Hat}");
                if (hatLines.Count != 1 || hatLines[0] != $"hat={Cosmetics.KeyOf(h)}" || AppearanceStore.Hat != h)
                    Fail($"cosmetic save: {h} saved as [{string.Join(", ", hatLines)}] and came back as {AppearanceStore.Hat}");
                if (lines.Any(x => x.Any(c => c is < ' ' or > '~')))
                    Fail("cosmetic save: appearance.txt has a non-ASCII line");
            }
            Line($"save: old loadout files -> {oldHat}, single-loadout file -> {veryOldHat}, new player -> {freshHat}; round trips {string.Join(", ", rounds)}");

            AppearanceStore.LoadLines(["hat=sombrero"]);
            var unknown = AppearanceStore.Hat;
            AppearanceStore.LoadLines(["hat=NONE"]);
            var none = AppearanceStore.Hat;
            AppearanceStore.LoadLines(["hat= Top_Hat "]);
            var loose = AppearanceStore.Hat;
            AppearanceStore.LoadLines(null, ["selected=0", "hat=fedora", "loadout1.name=X"]); // migration: the old loadout.txt line
            var migrated = AppearanceStore.Hat;
            AppearanceStore.LoadLines(["hat=beanie"], ["hat=fedora"]); // appearance.txt exists: it wins, loadout.txt's line is ignored
            var kept = AppearanceStore.Hat;
            if (unknown != HatId.None || none != HatId.None || loose != HatId.TopHat || migrated != HatId.Fedora || kept != HatId.Beanie)
                Fail($"cosmetic save: hat=sombrero -> {unknown}, hat=NONE -> {none}, ' Top_Hat ' -> {loose}, migrated fedora -> {migrated}, existing beanie -> {kept}");

            // An old loadout.txt with a hat line still loads its slots, and the line isn't written back (it moved).
            LoadoutStore.LoadLines(["hat=fedora", "loadout1.name=Kept"]);
            var slot1 = LoadoutStore.Slots[0].Name;
            var rewritten = LoadoutStore.SaveLines();
            Line($"loadout.txt: an old hat=fedora line leaves slot 1 \"{slot1}\" and isn't written back ({rewritten.Count(x => x.StartsWith("hat=", StringComparison.Ordinal))} hat lines)");
            if (slot1 != "Kept" || rewritten.Any(x => x.StartsWith("hat=", StringComparison.Ordinal)))
                Fail($"cosmetic save: the loadout still has a hat (slot 1 '{slot1}', save lines {string.Join("|", rewritten)})");

            // One choice for the profile: a locked hat is kept but worn as none.
            AppearanceStore.LoadLines(["hat=fedora"]);
            foreach (var prestige in new[] { 0, 2, 3, 5 })
            {
                var progress = new Progress { ReadOnly = true, Prestige = prestige };
                var worn = AppearanceStore.EffectiveHat(progress);
                var want = prestige >= 3 ? HatId.Fedora : HatId.None;
                Line($"profile hat Fedora at prestige {prestige}: worn as {worn}, kept in the file as {Cosmetics.KeyOf(AppearanceStore.Hat)}");
                if (worn != want || AppearanceStore.Hat != HatId.Fedora)
                    Fail($"cosmetic save: at prestige {prestige} the profile hat should be worn as {want} (kept as Fedora), got {worn}, kept {AppearanceStore.Hat}");
            }
        }
        finally
        {
            AppearanceStore.LoadLines(backup);
            LoadoutStore.LoadLines(loadoutBackup);
        }
    }

    private static HatId[] UiHats() => [HatId.None, .. Cosmetics.Hats.Select(h => h.Id)];

    // ---- Server check, bots, replication -------------------------------------------------------

    /// <summary>A Msg.Loadout packet as a client sends it, optionally with the hat byte.</summary>
    private static byte[] HatLoadoutPacket(HatId? hat)
    {
        var w = new PacketWriter();
        w.Begin(Msg.Loadout).U8(Weapons.DefaultPrimary.Id).U8(Weapons.DefaultSecondary.Id).U8(0).U8(0).U24(0).U24(0)
            .U8(0).U8(0).U8(0).U8((byte)KillStreaks.Starters[0]).U8((byte)KillStreaks.Starters[1]).U8((byte)KillStreaks.Starters[2]);
        if (hat is { } h)
            w.U8((byte)h);
        return w.Span.ToArray();
    }

    private static void CosmeticServerCheck()
    {
        // The rule itself, every hat at every prestige.
        var bad = 0;
        for (var prestige = 0; prestige <= Cosmetics.MaxPrestige; prestige++)
            foreach (var h in UiHats())
            {
                var want = prestige >= (Cosmetics.Get(h)?.Prestige ?? 0) ? h : HatId.None;
                if (Game.ValidHat(h, false, prestige) != want || Game.ValidHat(h, true, prestige) != HatId.None)
                    bad++;
            }
        if (Game.ValidHat((HatId)99, false, Cosmetics.MaxPrestige) != HatId.None)
            bad++;
        Line($"rule: {(Cosmetics.MaxPrestige + 1) * UiHats().Length} hat/prestige pairs checked for people and bots, {bad} wrong");
        if (bad > 0)
            Fail($"cosmetic server: Game.ValidHat got {bad} hat/prestige cases wrong (people: unlocked only; bots: never; unknown ids: never)");

        var savedMap = World.Map;
        var savedPhysics = World.PhysicsMode;
        try
        {
            using var server = new Game(NetPeer.Offline(), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1);
            server.Progress = new Progress { ReadOnly = true, Prestige = 1 };
            server.SelfTestPacketRaw(HatGuid, [(byte)DefaultMessageIDTypes.NewIncomingConnection], 0);
            var bot = server.Players.Values.First(p => p.Bot != null);
            var guest = server.Players.Values.First(p => p.Bot == null && p.Id != server.LocalId);
            var host = server.Local!;

            // The guest joins at prestige 3 (the Hello carries prestige and level after the name).
            var w = new PacketWriter();
            w.Begin(Msg.Hello).U8(5).Bytes("Guest"u8).U8(3).U8(20);
            server.SelfTestPacket(HatGuid, w.Span, 0);

            // Over the wire: a hat above their prestige is refused, one they've earned is worn.
            server.SelfTestPacket(HatGuid, HatLoadoutPacket(HatId.TallTopHat), 0);
            var locked = guest.Hat;
            server.SelfTestPacket(HatGuid, HatLoadoutPacket((HatId)200), 0);
            var junk = guest.Hat;
            server.SelfTestPacket(HatGuid, HatLoadoutPacket(null), 0); // an older client: no hat byte
            var older = guest.Hat;
            server.SelfTestPacket(HatGuid, HatLoadoutPacket(HatId.Fedora), 0);
            var earned = guest.Hat;
            Line($"server: guest at prestige {guest.Prestige} asked for Ridiculous Top Hat -> {locked}, hat 200 -> {junk}, older client -> {older}, Fedora -> {earned}");
            if (locked != HatId.None || junk != HatId.None || older != HatId.None || earned != HatId.Fedora)
                Fail($"cosmetic server: locked hat -> {locked}, junk -> {junk}, no hat byte -> {older} (want None), earned Fedora -> {earned}");

            // The host's own hat goes through the same check (its prestige comes from its profile).
            server.ChosenHat = HatId.Beanie;
            var hostBeanie = host.Hat;
            server.ChosenHat = HatId.TopHat;
            var hostTop = host.Hat;
            server.ChosenHat = HatId.Beanie;
            Line($"host at prestige 1: Beanie -> {hostBeanie}, Top Hat -> {hostTop}");
            if (hostBeanie != HatId.Beanie || hostTop != HatId.None)
                Fail($"cosmetic server: the host at prestige 1 got Beanie -> {hostBeanie}, Top Hat -> {hostTop} (want Beanie / None)");

            // Bots never wear one.
            var botAsked = server.ServerSetHat(bot, HatId.Beanie);
            Line($"bots: asked for Beanie -> {botAsked} (wears {bot.Hat})");
            if (botAsked != HatId.None || bot.Hat != HatId.None)
                Fail($"cosmetic server: a bot was given {bot.Hat}");

            // Replication: the roster carries everyone's hat to a client.
            var roster = server.SelfTestRosterPacket();
            using var client = new Game(NetPeer.OfflineClient(), new Audio(), "Guest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            client.SelfTestPacket(default, roster, 0);
            var seenGuest = client.HatOf(guest.Id);
            var seenBot = client.HatOf(bot.Id);
            var seenHost = client.HatOf(host.Id);
            Line($"replication: roster {roster.Length} bytes for {server.Players.Count} players; the client sees guest {seenGuest}, host {seenHost}, bot {seenBot}");
            if (seenGuest != HatId.Fedora || seenHost != HatId.Beanie || seenBot != HatId.None)
                Fail($"cosmetic replication: the client sees guest {seenGuest} (want Fedora), host {seenHost} (want Beanie), bot {seenBot} (want None)");

            // A made-up hat id from a hostile host draws nothing.
            if (RosterHatOffset(roster, guest.Id) is { } at)
            {
                var hostile = roster.ToArray();
                hostile[at] = 250;
                client.SelfTestPacket(default, hostile, 0);
                var afterHostile = client.HatOf(guest.Id);
                Line($"a made-up hat id in the roster -> {afterHostile}");
                if (afterHostile != HatId.None)
                    Fail($"cosmetic replication: a made-up hat id became {afterHostile}");
            }
            else
                Fail("cosmetic replication: the guest isn't in the roster");
        }
        finally
        {
            if (World.Map != savedMap)
                World.SetMap(savedMap);
            World.PhysicsMode = savedPhysics;
        }
    }

    /// <summary>Where a player's hat byte is in a roster packet (id, bot flag, name length, name, prestige, level, hat, 3 look bytes per player).</summary>
    private static int? RosterHatOffset(byte[] roster, byte id)
    {
        var at = 2;
        for (var i = 0; i < roster[1] && at + 3 <= roster.Length; i++)
        {
            var pid = roster[at];
            var end = at + 3 + roster[at + 2] + 3 + PlayerLook.WireBytes;
            if (pid == id && end <= roster.Length)
                return end - 1 - PlayerLook.WireBytes;
            at = end;
        }
        return null;
    }

    // ---- Layout --------------------------------------------------------------------------------

    /// <summary>
    /// The APPEARANCE screen at the smoke window and the user's monitors (the window as the screen sizes it, at the default
    /// UI scale): the model view and the picker panel inside the window, side by side without overlapping, the view big enough
    /// to see the model; and the loadout overview's Lethal / Tactical row (two cards now the hat has gone) fits.
    /// </summary>
    private static void CosmeticLayoutCheck()
    {
        foreach (var (w, h) in new[] { (2679f, 1786f), (5120f, 1440f), (3440f, 1440f), (1920f, 1080f) })
        {
            var (view, panel, half, cardsW, win) = CosmeticLayoutAt(w, h);
            var s = Math.Clamp(h / 900f, 0.8f, 3f);
            var problems = new List<string>();
            bool Inside((float2 Min, float2 Max) r) => r.Min.X >= win.Min.X - 0.5f && r.Min.Y >= win.Min.Y - 0.5f && r.Max.X <= win.Max.X + 0.5f && r.Max.Y <= win.Max.Y + 0.5f;
            if (!Inside(view) || !Inside(panel))
                problems.Add("the view or the panel leaves the window");
            if (view.Max.X > panel.Min.X + 0.01f)
                problems.Add("the view and the panel overlap");
            if (view.Max.X - view.Min.X < 300 * s || view.Max.Y - view.Min.Y < 400 * s)
                problems.Add($"the model view is only {view.Max.X - view.Min.X:0}x{view.Max.Y - view.Min.Y:0}");
            if (panel.Max.X - panel.Min.X < 560 * s)
                problems.Add($"the picker panel is only {panel.Max.X - panel.Min.X:0} px wide");
            var gap = 8 * s;
            if (2 * half + gap > cardsW + 0.5f || half < 180 * s)
                problems.Add($"overview row: two {half:0} px cards don't fit {cardsW:0} px or are too narrow");
            Line($"{w}x{h}: appearance view {view.Max.X - view.Min.X:0}x{view.Max.Y - view.Min.Y:0}, panel {panel.Max.X - panel.Min.X:0}x{panel.Max.Y - panel.Min.Y:0} "
                 + $"(content needs {Ui.AppearancePanelContentH() * s:0} px; it scrolls if short), overview cards {half:0} px"
                 + (problems.Count == 0 ? "" : $"; {problems.Count} problem(s)"));
            foreach (var p in problems)
                Fail($"appearance layout at {w}x{h}: {p}");
        }
    }

    /// <summary>The APPEARANCE screen's view and panel at a window size (the same sums as Ui.DrawAppearance), and the overview row.</summary>
    private static ((float2 Min, float2 Max) View, (float2 Min, float2 Max) Panel, float Half, float CardsW, (float2 Min, float2 Max) Win) CosmeticLayoutAt(float w, float h)
    {
        var s = Math.Clamp(h / 900f, 0.8f, 3f); // Ui.TargetScale at the default UI scale
        var size = new float2(MathF.Min(1150 * s, w - 40), MathF.Min(760 * s, h - 40));
        var win = new float2((w - size.X) / 2, (h - size.Y) / 2);
        var pad = 8 * s; // ImGui's window padding, scaled
        var title = 19 * s; // title bar (13 px font + frame padding)
        var contentMin = new float2(win.X + pad, win.Y + title + pad);
        var contentMax = new float2(win.X + size.X - pad, win.Y + size.Y - pad);
        var (vMin, vMax, pMin, pMax) = Ui.AppearanceRects(contentMin, contentMax, s);
        var cardsW = (size.X - 2 * pad) * 0.7f - 8 * s; // the loadout overview's cards column beside the 30% slot list
        return ((vMin, vMax), (pMin, pMax), Ui.OverviewHalfWidth(cardsW, 8 * s), cardsW, (contentMin, contentMax));
    }

    // ---- Determinism ---------------------------------------------------------------------------

    private static void CosmeticDeterminismCheck()
    {
        string Run()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var (w, h) in new[] { (2679f, 1786f), (5120f, 1440f), (3440f, 1440f) })
            {
                var (view, panel, _, _, _) = CosmeticLayoutAt(w, h);
                sb.Append($"{view.Min.X:R},{view.Min.Y:R},{view.Max.X:R},{view.Max.Y:R},{panel.Min.X:R},{panel.Max.Y:R};");
            }
            for (var p = 0; p <= Cosmetics.MaxPrestige; p++)
                foreach (var hat in UiHats())
                    sb.Append((int)Game.ValidHat(hat, false, p)).Append((int)Game.ValidHat(hat, true, p));
            var backup = AppearanceStore.SaveLines().ToArray();
            AppearanceStore.LoadLines(["hat=top_hat", "skin=tan", "headwear=helmet"]);
            sb.Append(string.Join("|", AppearanceStore.SaveLines()));
            AppearanceStore.LoadLines(backup);
            return sb.ToString();
        }
        var first = Run();
        var second = Run();
        Line($"determinism: two runs {(first == second ? "match" : "DIFFER")} ({first.Length} chars of layout, rule and save output)");
        if (first != second)
            Fail("cosmetic loadout: two identical runs gave different layouts, hat checks or save lines");
    }
}
