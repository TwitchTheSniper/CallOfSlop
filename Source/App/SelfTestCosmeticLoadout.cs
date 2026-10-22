using Brutal.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Self-test for the cosmetic loadout (the hat slot): loadout.txt round trip (old files have no hat), the server's hat
/// check (locked hats and bots get none), the hat replicating to a client in the roster, the cosmetics page layout at the
/// user's resolutions, and determinism.
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

    // ---- loadout.txt -------------------------------------------------------------------------

    private static void CosmeticSaveCheck()
    {
        var backup = LoadoutStore.SaveLines().ToArray();
        try
        {
            LoadoutStore.LoadLines(["selected=0", "loadout1.name=Old", "loadout1.primary=Hornet SMG"]); // from before hats
            var oldHat = LoadoutStore.Hat;
            LoadoutStore.LoadLines(["primary=Hornet SMG", "grenade=Frag"]); // the single-loadout format
            var veryOldHat = LoadoutStore.Hat;
            LoadoutStore.LoadLines(null); // brand-new player
            var freshHat = LoadoutStore.Hat;
            if (oldHat != HatId.None || veryOldHat != HatId.None || freshHat != HatId.None)
                Fail($"cosmetic save: files without a hat line should load as no hat, got {oldHat} / {veryOldHat} / {freshHat}");

            var rounds = new List<string>();
            foreach (var h in UiHats())
            {
                LoadoutStore.Hat = h;
                var lines = LoadoutStore.SaveLines();
                var hatLines = lines.Where(x => x.StartsWith("hat=", StringComparison.Ordinal)).ToList();
                LoadoutStore.LoadLines(lines.ToArray());
                rounds.Add($"{Cosmetics.KeyOf(h)}->{LoadoutStore.Hat}");
                if (hatLines.Count != 1 || hatLines[0] != $"hat={Cosmetics.KeyOf(h)}" || LoadoutStore.Hat != h)
                    Fail($"cosmetic save: {h} saved as [{string.Join(", ", hatLines)}] and came back as {LoadoutStore.Hat}");
                if (lines.Any(x => x.Any(c => c is < ' ' or > '~')))
                    Fail("cosmetic save: loadout.txt has a non-ASCII line");
            }
            Line($"save: old files -> {oldHat}, single-loadout file -> {veryOldHat}, new player -> {freshHat}; round trips {string.Join(", ", rounds)}");

            LoadoutStore.LoadLines(["hat=sombrero", "loadout1.name=X"]);
            var unknown = LoadoutStore.Hat;
            LoadoutStore.LoadLines(["hat=NONE"]);
            var none = LoadoutStore.Hat;
            LoadoutStore.LoadLines(["hat= Top_Hat "]);
            var loose = LoadoutStore.Hat;
            var slot1Name = LoadoutStore.Slots[0].Name;
            if (unknown != HatId.None || none != HatId.None || loose != HatId.TopHat || slot1Name != LoadoutStore.DefaultName(0))
                Fail($"cosmetic save: hat=sombrero -> {unknown}, hat=NONE -> {none}, ' Top_Hat ' -> {loose}, slot 1 name '{slot1Name}' (the hat line mustn't touch slot 1)");

            // One choice for the profile: every slot wears it, and a locked hat is kept but worn as none.
            LoadoutStore.LoadLines(["hat=fedora"]);
            foreach (var prestige in new[] { 0, 2, 3, 5 })
            {
                var progress = new Progress { ReadOnly = true, Prestige = prestige };
                var worn = LoadoutStore.Slots.Select(l => LoadoutStore.Effective(l, progress).Hat).Distinct().ToList();
                var want = prestige >= 3 ? HatId.Fedora : HatId.None;
                Line($"profile hat Fedora at prestige {prestige}: every slot wears {string.Join("/", worn)}, kept in the file as {Cosmetics.KeyOf(LoadoutStore.Hat)}");
                if (worn.Count != 1 || worn[0] != want || LoadoutStore.Hat != HatId.Fedora)
                    Fail($"cosmetic save: at prestige {prestige} the profile hat should apply to every slot as {want} (kept as Fedora), got {string.Join("/", worn)}, kept {LoadoutStore.Hat}");
            }
            if (LoadoutStore.Slots.Any(l => l.Hat != HatId.None))
                Fail("cosmetic save: a loadout slot stores a hat of its own (hats are one choice for the profile)");
        }
        finally
        {
            LoadoutStore.LoadLines(backup);
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

    /// <summary>Where a player's hat byte is in a roster packet (id, bot flag, name length, name, prestige, level, hat per player).</summary>
    private static int? RosterHatOffset(byte[] roster, byte id)
    {
        var at = 2;
        for (var i = 0; i < roster[1] && at + 3 <= roster.Length; i++)
        {
            var pid = roster[at];
            var end = at + 3 + roster[at + 2] + 3;
            if (pid == id && end <= roster.Length)
                return end - 1;
            at = end;
        }
        return null;
    }

    // ---- Layout --------------------------------------------------------------------------------

    /// <summary>
    /// The cosmetics page and the overview's hat card at the smoke window and the user's monitors: the window as the loadout
    /// screen sizes it (at the default UI scale), the body under the title bar, breadcrumb and footer, the page heading, then
    /// six cards that stay inside, don't overlap and are at least the minimum size.
    /// </summary>
    private static void CosmeticLayoutCheck()
    {
        foreach (var (w, h) in new[] { (2679f, 1786f), (5120f, 1440f), (3440f, 1440f), (1920f, 1080f) })
        {
            var (rects, area, origin, third, cardsW) = CosmeticLayoutAt(w, h);
            var s = Math.Clamp(h / 900f, 0.8f, 3f);
            var problems = new List<string>();
            for (var i = 0; i < rects.Length; i++)
            {
                var (a, b) = rects[i];
                if (a.X < origin.X - 0.5f || a.Y < origin.Y - 0.5f || b.X > origin.X + area.X + 0.5f || b.Y > origin.Y + area.Y + 0.5f)
                    problems.Add($"card {i} ({a.X:0},{a.Y:0})-({b.X:0},{b.Y:0}) leaves the area");
                if (b.X - a.X < Ui.HatCardMinW * s || b.Y - a.Y < Ui.HatCardMinH * s)
                    problems.Add($"card {i} is {b.X - a.X:0}x{b.Y - a.Y:0}, under {Ui.HatCardMinW * s:0}x{Ui.HatCardMinH * s:0}");
                for (var j = i + 1; j < rects.Length; j++)
                {
                    var (c, d) = rects[j];
                    if (MathF.Min(b.X, d.X) - MathF.Max(a.X, c.X) > 0.01f && MathF.Min(b.Y, d.Y) - MathF.Max(a.Y, c.Y) > 0.01f)
                        problems.Add($"cards {i} and {j} overlap");
                }
            }
            var gap = 8 * s;
            if (3 * third + 2 * gap > cardsW + 0.5f || third < 180 * s)
                problems.Add($"overview row: three {third:0} px cards don't fit {cardsW:0} px or are too narrow");
            if (rects.Length != UiHats().Length)
                problems.Add($"{rects.Length} card slots for {UiHats().Length} choices");
            Line($"{w}x{h}: cosmetics area {area.X:0}x{area.Y:0}, cards {rects[0].Max.X - rects[0].Min.X:0}x{rects[0].Max.Y - rects[0].Min.Y:0}, overview hat card {third:0} px wide"
                 + (problems.Count == 0 ? "" : $"; {problems.Count} problem(s)"));
            foreach (var p in problems)
                Fail($"cosmetics layout at {w}x{h}: {p}");
        }
    }

    /// <summary>The cosmetics page's card rectangles at a window size (the same sums as Ui.DrawLoadoutScreen / DrawCosmeticsPicker).</summary>
    private static ((float2 Min, float2 Max)[] Rects, float2 Area, float2 Origin, float Third, float CardsW) CosmeticLayoutAt(float w, float h)
    {
        var s = Math.Clamp(h / 900f, 0.8f, 3f); // Ui.TargetScale at the default UI scale
        var size = new float2(MathF.Min(1150 * s, w - 40), MathF.Min(760 * s, h - 40));
        var win = new float2((w - size.X) / 2, (h - size.Y) / 2);
        var pad = 8 * s; // ImGui's window padding, scaled
        var title = 19 * s; // title bar (13 px font + frame padding)
        var crumb = 25 * s; // breadcrumb line + spacing
        var footer = 46 * s;
        var bodyTop = win.Y + title + pad + crumb;
        var bodyH = win.Y + size.Y - pad - footer - 8 * s - bodyTop;
        var origin = new float2(win.X + pad, bodyTop + Ui.HatHeaderH * s);
        var area = new float2(size.X - 2 * pad, bodyH - Ui.HatHeaderH * s);
        var rects = Ui.HatCardRects(origin, area, s, 8 * s);
        // Overview: the cards column is what's left beside the 30% slot list.
        var cardsW = (size.X - 2 * pad) * 0.7f - 8 * s;
        return (rects, area, origin, Ui.OverviewThirdWidth(cardsW, 8 * s), cardsW);
    }

    // ---- Determinism ---------------------------------------------------------------------------

    private static void CosmeticDeterminismCheck()
    {
        string Run()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var (w, h) in new[] { (2679f, 1786f), (5120f, 1440f), (3440f, 1440f) })
                foreach (var (a, b) in CosmeticLayoutAt(w, h).Rects)
                    sb.Append($"{a.X:R},{a.Y:R},{b.X:R},{b.Y:R};");
            for (var p = 0; p <= Cosmetics.MaxPrestige; p++)
                foreach (var hat in UiHats())
                    sb.Append((int)Game.ValidHat(hat, false, p)).Append((int)Game.ValidHat(hat, true, p));
            var backup = LoadoutStore.SaveLines().ToArray();
            LoadoutStore.LoadLines(["hat=top_hat", "loadout2.name=Two"]);
            sb.Append(string.Join("|", LoadoutStore.SaveLines()));
            LoadoutStore.LoadLines(backup);
            return sb.ToString();
        }
        var first = Run();
        var second = Run();
        Line($"determinism: two runs {(first == second ? "match" : "DIFFER")} ({first.Length} chars of layout, rule and save output)");
        if (first != second)
            Fail("cosmetic loadout: two identical runs gave different layouts, hat checks or save lines");
    }
}
