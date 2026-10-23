using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Self-test for the APPEARANCE screen's look (Source/Cosmetics/Appearance.cs): appearance.txt round trips on every axis
/// and option, the migration of the old hat line from loadout.txt (through the real files in the self-test save folder),
/// unknown or removed variants falling back to Auto, the look on the body (every axis bots vary on), team colour winning
/// over the outfit colour, the network (loadout packet to the server, roster to a client and to a joiner, bots never
/// customised, hostile bytes), and the loadout no longer having a hat slot.
/// </summary>
public static partial class SelfTest
{
    private static readonly RakNetGUID LookGuid = new() { G = 0x10CC, SystemIndex = 0 };

    private static void CheckAppearance()
    {
        AppearanceSaveCheck();
        AppearanceMigrationCheck();
        AppearanceFallbackCheck();
        AppearanceBodyCheck();
        AppearanceNetworkCheck();
        AppearanceLoadoutCheck();
    }

    // ---- appearance.txt ----------------------------------------------------------------------

    private static void AppearanceSaveCheck()
    {
        var backup = AppearanceStore.SaveLines().ToArray();
        try
        {
            AppearanceStore.LoadLines(["hat=beanie"]); // a file with only a hat: the look is Auto
            var hatOnly = AppearanceStore.Look;
            var cases = 0;
            var bad = new List<string>();
            foreach (var axis in Ui.LookAxes)
                for (var v = 0; v <= PlayerLook.Options(axis); v++)
                {
                    AppearanceStore.Look = PlayerLook.Auto.With(axis, v);
                    var lines = AppearanceStore.SaveLines();
                    AppearanceStore.LoadLines(lines.ToArray());
                    cases++;
                    if (AppearanceStore.Look.Get(axis) != v || AppearanceStore.Look != PlayerLook.Auto.With(axis, v))
                        bad.Add($"{axis}={v} came back as {AppearanceStore.Look}");
                    if (lines.Any(x => x.Any(c => c is < ' ' or > '~')))
                        bad.Add($"{axis}={v}: a non-ASCII line");
                }
            var rng = new Random(45);
            for (var i = 0; i < 40; i++)
            {
                var look = PlayerLook.Randomized(rng);
                AppearanceStore.Look = look;
                AppearanceStore.LoadLines(AppearanceStore.SaveLines().ToArray());
                cases++;
                if (AppearanceStore.Look != look)
                    bad.Add($"random {look} came back as {AppearanceStore.Look}");
            }
            // Words are forgiving: case, spaces, numbers.
            AppearanceStore.LoadLines(["Skin = TAN", "hair=Dark Brown", "headwear=3", "beard=beard", "gear=grey-green", "outfit=green"]);
            var loose = AppearanceStore.Look;
            var want = new PlayerLook(4, 2, 3, 2, 4, 3);
            Line($"save: {cases} round trips, {bad.Count} wrong; hat-only file -> {hatOnly}; loose words -> {loose}");
            foreach (var b in bad.Take(5))
                Fail($"appearance save: {b}");
            if (!hatOnly.IsAuto || loose != want)
                Fail($"appearance save: a hat-only file gave {hatOnly} (want all auto); loose words gave {loose} (want {want})");
        }
        finally
        {
            AppearanceStore.LoadLines(backup);
        }
    }

    /// <summary>The real files: an old loadout.txt with a hat line and no appearance.txt yet, through Load() and back.</summary>
    private static void AppearanceMigrationCheck()
    {
        var appearancePath = SavePaths.PathOf(AppearanceStore.FileName);
        var loadoutPath = SavePaths.PathOf("loadout.txt");
        string[]? Keep(string path) => File.Exists(path) ? File.ReadAllLines(path) : null;
        var keptAppearance = Keep(appearancePath);
        var keptAppearanceBak = Keep(SavePaths.BackupOf(appearancePath));
        var keptLoadout = Keep(loadoutPath);
        var memory = AppearanceStore.SaveLines().ToArray();
        var loadoutMemory = LoadoutStore.SaveLines().ToArray();
        var (wasRo, wasLoadoutRo) = (AppearanceStore.ReadOnly, LoadoutStore.ReadOnly);
        try
        {
            AppearanceStore.ReadOnly = LoadoutStore.ReadOnly = false;
            File.Delete(appearancePath);
            File.Delete(SavePaths.BackupOf(appearancePath));
            File.WriteAllLines(loadoutPath, ["version=1", "selected=0", "hat=top_hat", "loadout1.name=Migrated"]); // an older build's save

            AppearanceStore.Load(); // first run of this build: no appearance.txt, the hat comes from loadout.txt
            var migrated = AppearanceStore.Hat;
            var written = File.Exists(appearancePath) ? File.ReadAllLines(appearancePath) : [];
            LoadoutStore.Load(); // rewrites loadout.txt without the hat line
            var loadoutLines = File.ReadAllLines(loadoutPath);
            var slot = LoadoutStore.Slots[0].Name;
            AppearanceStore.Look = new PlayerLook(Skin: 5, Headwear: 3);
            AppearanceStore.Save();
            AppearanceStore.Hat = HatId.None;
            AppearanceStore.Look = default;
            AppearanceStore.Load(); // next start: appearance.txt is there
            var reloaded = (AppearanceStore.Hat, AppearanceStore.Look);
            Line($"migration: loadout.txt hat=top_hat -> appearance.txt [{string.Join(", ", written.Where(l => !l.StartsWith("version", StringComparison.Ordinal)).Take(3))}...], "
                 + $"loadout.txt rewritten with {loadoutLines.Count(l => l.StartsWith("hat=", StringComparison.Ordinal))} hat lines (slot 1 \"{slot}\"), next start {reloaded.Hat} / {reloaded.Look}");
            if (migrated != HatId.TopHat || !written.Contains("hat=top_hat"))
                Fail($"appearance migration: the old loadout.txt hat became {migrated}; appearance.txt has [{string.Join(", ", written)}]");
            if (loadoutLines.Any(l => l.StartsWith("hat=", StringComparison.Ordinal)) || slot != "Migrated")
                Fail($"appearance migration: loadout.txt still has a hat line or lost its slot (slot 1 '{slot}')");
            if (reloaded.Hat != HatId.TopHat || reloaded.Look != new PlayerLook(Skin: 5, Headwear: 3))
                Fail($"appearance migration: after a restart the hat is {reloaded.Hat} and the look {reloaded.Look}");
            if (!SavePaths.SaveFiles.Contains(AppearanceStore.FileName))
                Fail("appearance migration: appearance.txt isn't one of the save files copied between builds");
        }
        finally
        {
            AppearanceStore.ReadOnly = wasRo;
            LoadoutStore.ReadOnly = wasLoadoutRo;
            void Restore(string path, string[]? lines)
            {
                if (lines != null)
                    File.WriteAllLines(path, lines);
                else if (File.Exists(path))
                    File.Delete(path);
            }
            Restore(appearancePath, keptAppearance);
            Restore(SavePaths.BackupOf(appearancePath), keptAppearanceBak);
            Restore(loadoutPath, keptLoadout);
            AppearanceStore.LoadLines(memory);
            LoadoutStore.LoadLines(loadoutMemory);
        }
    }

    // ---- Unknown / removed variants ------------------------------------------------------------

    private static void AppearanceFallbackCheck()
    {
        var problems = new List<string>();
        var backup = AppearanceStore.SaveLines().ToArray();
        try
        {
            // Words this build doesn't know (a removed variant, a newer build, a typo) load as Auto, axis by axis.
            AppearanceStore.LoadLines(["skin=purple", "hair=99", "headwear=jetpack", "beard=7", "gear=0", "outfit=tan", "hat=sombrero"]);
            if (!AppearanceStore.Look.IsAuto || AppearanceStore.Hat != HatId.None)
                problems.Add($"unknown words gave {AppearanceStore.Look}, hat {AppearanceStore.Hat}");
            AppearanceStore.LoadLines(["skin=dark", "headwear=jetpack"]);
            if (AppearanceStore.Look != new PlayerLook(Skin: 6))
                problems.Add($"a known skin and an unknown headwear gave {AppearanceStore.Look}");
        }
        finally
        {
            AppearanceStore.LoadLines(backup);
        }

        // Out-of-range values (a removed variant over the wire) draw as the name's look on that axis only.
        var auto = BodyModel.VariantFor("Fallback", 7);
        var broken = new PlayerLook(Skin: 15, Hair: 2, Headwear: 9, Beard: 3, Gear: 12, Outfit: 14);
        var v = broken.Apply(auto);
        if (v.Skin != auto.Skin || v.HairColor != 1 || v.Headwear != auto.Headwear || v.Beard != auto.Beard || v.Gear != auto.Gear)
            problems.Add($"out-of-range values drew as {v} (want the name's {auto} except hair 1)");
        if (broken.Sanitized() != new PlayerLook(Hair: 2))
            problems.Add($"sanitising {broken} gave {broken.Sanitized()}");
        if (broken.OutfitColor(Vector3.One) != Vector3.One)
            problems.Add("an out-of-range outfit colour didn't fall back to the slot colour");
        // Hostile bytes: every nibble 15.
        var w = new PacketWriter();
        w.Begin(Msg.Roster).U8(0xFF).U8(0xFF).U8(0xFF);
        var bytes = w.Span.ToArray();
        var r = new PacketReader(bytes.AsSpan(1));
        var hostile = PlayerLook.Unpack(ref r);
        var shortR = new PacketReader(bytes.AsSpan(1, 2));
        var truncated = PlayerLook.Unpack(ref shortR);
        if (!hostile.IsAuto || !truncated.IsAuto)
            problems.Add($"hostile bytes gave {hostile}, a truncated look {truncated}");
        Line($"fallback: unknown words and out-of-range values -> Auto per axis ({broken} drew as {v}); hostile / truncated wire -> {hostile} / {truncated}");
        foreach (var p in problems)
            Fail($"appearance fallback: {p}");
    }

    // ---- On the body -----------------------------------------------------------------------------

    private static void AppearanceBodyCheck()
    {
        var problems = new List<string>();
        var p = new Player { Id = 4, Name = "Looker" };
        p.Teleport(new Vector3(2, 0, 2));
        var auto = BodyModel.VariantFor(p);
        if (auto != BodyModel.VariantFor(p.Name, p.Id))
            problems.Add("an all-Auto look doesn't draw as the name's look (people who never opened the screen must look the same)");

        // Every option of every axis reaches the body variant / colours.
        var options = 0;
        foreach (var axis in Ui.LookAxes)
            for (var v = 1; v <= PlayerLook.Options(axis); v++)
            {
                options++;
                p.Look = PlayerLook.Auto.With(axis, v);
                var b = BodyModel.VariantFor(p);
                var c = BodyColors.For(p.Color, b);
                var ok = axis switch
                {
                    LookAxis.Skin => c.Skin == BodyModel.SkinTones[v - 1],
                    LookAxis.Hair => c.Hair == BodyModel.HairColors[v - 1],
                    LookAxis.Headwear => b.Headwear == (Headwear)(v - 1),
                    LookAxis.Beard => b.Beard == (v == 2),
                    LookAxis.Gear => c.Gear == BodyModel.GearColors[v - 1],
                    _ => p.Color == Player.Palette[v - 1],
                };
                if (!ok)
                    problems.Add($"{axis} option {v} ({PlayerLook.OptionName(axis, v)}) doesn't show on the body");
            }

        // Team modes: the team colour wins over the outfit colour (readability); free-for-all wears the pick.
        p.Look = new PlayerLook(Outfit: 3);
        p.Team = 1;
        var team = p.Color;
        p.Team = Game.NoTeam;
        var ffa = p.Color;
        if (team != GameModes.TeamColors[1] || ffa != Player.Palette[2])
            problems.Add($"outfit colour: team mode {team} (want the team colour), free-for-all {ffa} (want green)");

        // The scene graph rebuilds when the look changes, and draws what the look says (helmet + beard).
        var rig = new PlayerRig(p.Id);
        p.Look = new PlayerLook(Headwear: (byte)Headwear.Hair + 1, Beard: 1);
        rig.Pose(p, 10);
        var before = rig.Body(0).Head.Count;
        p.Look = new PlayerLook(Headwear: (byte)Headwear.Helmet + 1, Beard: 2, Skin: 6);
        rig.Pose(p, 10.1);
        var (_, head) = rig.Body(0);
        var wantHead = BodyModel.HeadParts.Length + BodyModel.HeadwearParts[(int)Headwear.Helmet].Length + 1;
        var skinOk = false;
        for (var i = 0; i < head.Count; i++)
            if (head.Paint[i] == BodyPaint.Skin && head.Draws[i].Color == BodyModel.SkinTones[5])
                skinOk = true;
        if (head.Count != wantHead || !skinOk)
            problems.Add($"the rig's head has {head.Count} parts (want {wantHead}: helmet + beard) and the dark skin {(skinOk ? "shows" : "is missing")} (hair, no beard: {before})");

        // Ragdolls and gibs build the same look.
        var body = new RagdollBody();
        Ragdolls.BuildSoldierLook(body, p.Name, p.Id, p.Color, p.Look);
        if (body.Head.Count != wantHead)
            problems.Add($"a ragdoll's head has {body.Head.Count} parts (want {wantHead})");

        Line($"body: {options} options on {Ui.LookAxes.Length} axes reach the model; Auto = the name's look; team colour wins in team modes; rig and ragdoll follow the look ({before} -> {head.Count} head parts)");
        foreach (var x in problems)
            Fail($"appearance body: {x}");
    }

    // ---- Network ----------------------------------------------------------------------------------

    private static void AppearanceNetworkCheck()
    {
        var savedMap = World.Map;
        var savedPhysics = World.PhysicsMode;
        try
        {
            using var server = new Game(NetPeer.Offline(), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1);
            server.Progress = new Progress { ReadOnly = true };
            server.SelfTestPacketRaw(LookGuid, [(byte)DefaultMessageIDTypes.NewIncomingConnection], 0);
            var bot = server.Players.Values.First(p => p.Bot != null);
            var guest = server.Players.Values.First(p => p.Bot == null && p.Id != server.LocalId);
            var host = server.Local!;
            var w = new PacketWriter();
            w.Begin(Msg.Hello).U8(5).Bytes("Guest"u8).U8(0).U8(4);
            server.SelfTestPacket(LookGuid, w.Span, 0);

            // The guest's loadout packet carries its look after the hat and the profile.
            var guestLook = new PlayerLook(Skin: 3, Hair: 6, Headwear: 5, Beard: 2, Gear: 2, Outfit: 6);
            server.SelfTestPacket(LookGuid, LookLoadoutPacket(guestLook), 0);
            var got = guest.Look;
            server.SelfTestPacket(LookGuid, LookLoadoutPacket(null), 0); // a packet without the look: the look stays
            var kept = guest.Look;
            server.ChosenLook = new PlayerLook(Skin: 1, Headwear: 3); // the host's own
            var hostLook = host.Look;
            var botLook = server.ServerSetLook(bot, guestLook); // bots are never customised
            Line($"server: guest sent {guestLook} -> {got}; no look bytes -> {kept}; host {hostLook}; bot asked -> {botLook}");
            if (got != guestLook || kept != guestLook || hostLook != new PlayerLook(Skin: 1, Headwear: 3) || botLook.IsAuto == false || !bot.Look.IsAuto)
                Fail($"appearance server: guest {got} / {kept} (want {guestLook}), host {hostLook}, bot {bot.Look}");

            // The roster carries everyone's look to a client, and to a joiner that arrives later (the same roster on join).
            var roster = server.SelfTestRosterPacket();
            using var client = new Game(NetPeer.OfflineClient(), new Audio(), "Guest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            client.SelfTestPacket(default, roster, 0);
            server.SelfTestPacketRaw(new RakNetGUID { G = 0x10CD, SystemIndex = 0 }, [(byte)DefaultMessageIDTypes.NewIncomingConnection], 0); // someone else joins
            var joinerRoster = server.SelfTestRosterPacket();
            using var joiner = new Game(NetPeer.OfflineClient(), new Audio(), "Late", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            joiner.SelfTestPacket(default, joinerRoster, 0);
            var (cg, ch, cb) = (client.LookOf(guest.Id), client.LookOf(host.Id), client.LookOf(bot.Id));
            var (jg, jh) = (joiner.LookOf(guest.Id), joiner.LookOf(host.Id));
            Line($"replication: roster {roster.Length} bytes for {server.Players.Count} players ({PlayerLook.WireBytes} look bytes each); client sees guest {cg}, host {ch}, bot {cb}; joiner sees guest {jg}, host {jh}");
            if (cg != guestLook || ch != hostLook || !cb.IsAuto || jg != guestLook || jh != hostLook)
                Fail($"appearance replication: client guest {cg}, host {ch}, bot {cb}; joiner guest {jg}, host {jh}");

            // A hostile host's look bytes draw as Auto.
            if (RosterHatOffset(roster, guest.Id) is { } at)
            {
                var hostile = roster.ToArray();
                hostile[at + 1] = 0xEE;
                hostile[at + 2] = 0xEE;
                hostile[at + 3] = 0xEE;
                client.SelfTestPacket(default, hostile, 0);
                var seen = client.LookOf(guest.Id);
                if (!seen.IsAuto || client.NameOf(guest.Id) != "Guest")
                    Fail($"appearance replication: hostile look bytes became {seen} (name '{client.NameOf(guest.Id)}')");
            }
            else
                Fail("appearance replication: the guest isn't in the roster");

            // Round trip of the 3 wire bytes for every option of every axis.
            var bad = 0;
            foreach (var axis in Ui.LookAxes)
                for (var v = 0; v <= PlayerLook.Options(axis); v++)
                {
                    var look = guestLook.With(axis, v);
                    var pw = new PacketWriter();
                    pw.Begin(Msg.Roster);
                    look.Pack(pw);
                    var data = pw.Span.ToArray();
                    var pr = new PacketReader(data.AsSpan(1));
                    if (data.Length != 1 + PlayerLook.WireBytes || PlayerLook.Unpack(ref pr) != look)
                        bad++;
                }
            if (bad > 0)
                Fail($"appearance wire: {bad} looks didn't survive Pack / Unpack");
        }
        finally
        {
            if (World.Map != savedMap)
                World.SetMap(savedMap);
            World.PhysicsMode = savedPhysics;
        }
    }

    /// <summary>A Msg.Loadout packet as a client sends it: guns, perks, streaks, hat, prestige and level, then the look (or none).</summary>
    private static byte[] LookLoadoutPacket(PlayerLook? look)
    {
        var w = new PacketWriter();
        w.Begin(Msg.Loadout).U8(Weapons.DefaultPrimary.Id).U8(Weapons.DefaultSecondary.Id).U8(0).U8(0).U24(0).U24(0)
            .U8(0).U8(0).U8(0).U8((byte)KillStreaks.Starters[0]).U8((byte)KillStreaks.Starters[1]).U8((byte)KillStreaks.Starters[2])
            .U8(0).U8(0).U8(4);
        look?.Pack(w);
        return w.Span.ToArray();
    }

    // ---- The loadout has no hat slot --------------------------------------------------------------

    private static void AppearanceLoadoutCheck()
    {
        var problems = new List<string>();
        if (typeof(Loadout).GetField("Hat") != null || typeof(LoadoutStore).GetField("Hat") != null)
            problems.Add("Loadout / LoadoutStore still have a Hat field");
        if (Enum.GetNames<LoadoutPage>().Any(n => n.Contains("Cosmetic", StringComparison.OrdinalIgnoreCase) || n.Contains("Hat", StringComparison.Ordinal)))
            problems.Add($"the loadout screen still has a hat page ({string.Join(", ", Enum.GetNames<LoadoutPage>())})");
        if (LoadoutStore.SaveLines().Any(l => l.StartsWith("hat=", StringComparison.Ordinal)))
            problems.Add("loadout.txt is still written with a hat line");
        if (typeof(Ui).GetMethod("HatCard", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) != null)
            problems.Add("the loadout overview still has a hat card");
        Line($"loadout: no hat field, page, card or save line ({problems.Count} problem(s)); the hat is on the APPEARANCE screen");
        foreach (var p in problems)
            Fail($"appearance: {p}");
    }
}
