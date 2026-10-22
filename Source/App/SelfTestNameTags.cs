using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "Name tags" self-test section: the HUD's world-to-screen projection (Source/UI/WorldLabels.cs), the name tag rules
/// (Source/UI/NameTags.cs: teammates through walls, enemies only when aimed and visible, linger, ranges, modes, death cam),
/// the layout math, the settings.txt values, the ray budget and no allocations per frame.
/// </summary>
public static partial class SelfTest
{
    private static void CheckNameTags()
    {
        NameTagProjection();
        NameTagRulesCheck();
        NameTagLayoutCheck();
        NameTagSettings();
        NameTagAllocations();
        NameTagZombiesColours();
    }

    private static readonly Vector3 TagEye = new(0, 1.6f, 0);

    private static TagCandidate TagCand(byte id, Vector3 feet, bool friendly, bool revealed = false, bool force = false) =>
        new() { Id = id, Hitbox = World.Hitbox(feet), Friendly = friendly, Revealed = revealed, Force = force, Health01 = 1f };

    private static bool Tagged(NameTags t, byte id, out float alpha)
    {
        for (var i = 0; i < t.Count; i++)
            if (t.Tags[i].Id == id)
            {
                alpha = t.Tags[i].Alpha;
                return true;
            }
        alpha = 0;
        return false;
    }

    private static bool Tagged(NameTags t, byte id) => Tagged(t, id, out _);

    // ---- Projection: centre, sides, above, behind the camera, off screen, on 32:9 and 21:9 ----
    private static void NameTagProjection()
    {
        var view = Matrix4x4.CreateLookAt(TagEye, TagEye + new Vector3(0, 0, -1), Vector3.UnitY);
        var bad = 0;
        foreach (var (w, h) in new[] { (5120f, 1440f), (3440f, 1440f), (1920f, 1080f) })
        {
            var fov = 75f * MathF.PI / 180f;
            var vp = view * Matrix4x4.CreatePerspectiveFieldOfView(fov, w / h, 0.05f, 500f);
            var ahead = Hud.Project(vp, TagEye + new Vector3(0, 0, -10), w, h, out var c, out var depth);
            var above = Hud.Project(vp, TagEye + new Vector3(0, 2, -10), w, h, out var up, out _);
            var right = Hud.Project(vp, TagEye + new Vector3(3, 0, -10), w, h, out var rt, out _);
            var behind = Hud.Project(vp, TagEye + new Vector3(0, 0, 10), w, h, out _, out var behindDepth);
            var side = Hud.Project(vp, TagEye + new Vector3(-100, 0, -10), w, h, out _, out _);
            var atEye = Hud.Project(vp, TagEye, w, h, out _, out _);
            var nan = Hud.Project(vp, new Vector3(float.NaN, 0, -10), w, h, out _, out _);
            // The vertical FOV maps 1:1 to the height: a point at the top edge of the frustum projects to y = 0.
            var edge = Hud.Project(vp, TagEye + new Vector3(0, MathF.Tan(fov / 2) * 10 * 0.999f, -10), w, h, out var top, out _);
            // Just outside the screen: hidden without a margin, shown with one.
            var past = TagEye + new Vector3(MathF.Tan(fov / 2) * (w / h) * 10 * 1.02f, 0, -10);
            var pastNo = Hud.Project(vp, past, w, h, out _, out _);
            var pastMargin = Hud.Project(vp, past, w, h, out _, out _, h * 0.05f);
            var ok = ahead && MathF.Abs(c.X - w / 2) < 0.5f && MathF.Abs(c.Y - h / 2) < 0.5f && MathF.Abs(depth - 10) < 0.01f
                     && above && up.Y < h / 2 && MathF.Abs(up.X - w / 2) < 0.5f && right && rt.X > w / 2
                     && !behind && behindDepth < 0 && !side && !atEye && !nan && edge && top.Y < 2f && !pastNo && pastMargin;
            if (!ok)
            {
                bad++;
                Fail($"projection {w}x{h}: ahead {ahead} at ({c.X:0.0},{c.Y:0.0}) depth {depth:0.00}, above {above} y {up.Y:0.0}, right {right} x {rt.X:0.0}, "
                     + $"behind {behind} (w {behindDepth:0.0}), far side {side}, at the eye {atEye}, NaN {nan}, top edge {edge} y {top.Y:0.0}, just off {pastNo} / with margin {pastMargin}");
            }
        }
        Line($"world-to-screen projection: centre, above, right, behind (hidden), off screen (hidden), top edge, margin on 32:9, 21:9 and 16:9: {(bad == 0 ? "ok" : $"{bad} bad")}");
    }

    // ---- The rules ----
    private static void NameTagRulesCheck()
    {
        var wall = false;
        var tags = new NameTags { Ray = (o, d, max) => wall ? MathF.Min(max, 3f) : max };
        var fwd = new Vector3(0, 0, -1);
        var away = new Vector3(1, 0, 0);
        const byte Mate = 1, Enemy = 2, Other = 3, FarMate = 4, FarEnemy = 5;
        TagCandidate[] cands =
        [
            TagCand(Mate, new Vector3(5, 0, -20), true),
            TagCand(Enemy, new Vector3(0, 0, -15), false),
            TagCand(Other, new Vector3(6, 0, -15), false),
            TagCand(FarMate, new Vector3(3, 0, -65), true),
        ];

        // Teammates through walls; the aimed enemy only with a clear view; an enemy you're not aiming at never.
        wall = true;
        tags.Update(cands, TagEye, fwd, true, NameTagMode.All, 10);
        var mateThroughWall = Tagged(tags, Mate);
        var enemyThroughWall = Tagged(tags, Enemy);
        var farMate = Tagged(tags, FarMate);
        wall = false;
        tags.Update(cands, TagEye, fwd, true, NameTagMode.All, 10.2);
        var enemyAimed = Tagged(tags, Enemy, out var aimedAlpha);
        var otherShown = Tagged(tags, Other);
        var aimedFlag = tags.FocusAimed;
        Line($"rules: teammate through a wall {mateThroughWall}, aimed enemy behind a wall {enemyThroughWall}, aimed enemy in view {enemyAimed} (alpha {aimedAlpha:0.00}), "
             + $"enemy not aimed at {otherShown}, teammate at 65 m {farMate}");
        if (!mateThroughWall || enemyThroughWall || !enemyAimed || aimedAlpha < 0.99f || !aimedFlag || otherShown || farMate)
            Fail("name tags: teammates must show through walls (within 60 m); enemies only when aimed at and visible");

        // Linger: 0.5 s after the crosshair leaves, fading; gone after; and dropped at once if they step behind a wall.
        tags.Update(cands, TagEye, away, true, NameTagMode.All, 10.4);
        var linger = Tagged(tags, Enemy, out var lingerAlpha);
        tags.Update(cands, TagEye, away, true, NameTagMode.All, 10.8);
        var after = Tagged(tags, Enemy);
        tags.Update(cands, TagEye, fwd, true, NameTagMode.All, 11);
        wall = true;
        tags.Update(cands, TagEye, away, true, NameTagMode.All, 11.05); // line of sight still cached: kept
        var cachedKeep = Tagged(tags, Enemy);
        tags.Update(cands, TagEye, away, true, NameTagMode.All, 11.15); // re-checked: behind the wall now
        var behindWall = Tagged(tags, Enemy);
        wall = false;
        Line($"linger: 0.2 s after {linger} (alpha {lingerAlpha:0.00}), 0.6 s after {after}; stepped behind a wall: kept while cached {cachedKeep}, then {behindWall}");
        if (!linger || MathF.Abs(lingerAlpha - 0.6f) > 0.05f || after || !cachedKeep || behindWall)
            Fail("name tags: an enemy's tag must linger 0.5 s (fading) and never show behind a wall");

        // Recon: an enemy revealed through walls can be tagged by aiming at their outline.
        var recon = new NameTags { Ray = (o, d, max) => MathF.Min(max, 3f) };
        recon.Update([TagCand(Enemy, new Vector3(0, 0, -15), false, revealed: true)], TagEye, fwd, true, NameTagMode.All, 1);
        var revealedTag = Tagged(recon, Enemy);

        // Ranges: enemies to 40 m, teammates to 60 m with a fade.
        var ranges = new NameTags { Ray = (o, d, max) => max };
        ranges.Update([TagCand(FarEnemy, new Vector3(0, 0, -45), false), TagCand(Mate, new Vector3(3, 0, -58), true), TagCand(Other, new Vector3(-2, 0, -4), true)],
            TagEye, fwd, true, NameTagMode.All, 1);
        var farEnemy = Tagged(ranges, FarEnemy);
        Tagged(ranges, Mate, out var farAlpha);
        Tagged(ranges, Other, out var nearAlpha);
        var sorted = ranges.Count == 2 && ranges.Tags[0].Id == Other && ranges.Tags[1].Id == Mate;
        Line($"recon-revealed enemy behind a wall, aimed: {revealedTag}; enemy aimed at 45 m {farEnemy}; teammate alpha at 4 m {nearAlpha:0.00}, at 58 m {farAlpha:0.00}; nearest first {sorted}");
        if (!revealedTag || farEnemy || nearAlpha < 0.99f || farAlpha > 0.5f || farAlpha < 0.3f || !sorted)
            Fail("name tags: recon reveal, ranges, distance fade or ordering is wrong");

        // Modes, and the death cam (can't aim; the killer is tagged).
        var modes = new NameTags { Ray = (o, d, max) => max };
        modes.Update(cands, TagEye, fwd, true, NameTagMode.TeamOnly, 1);
        var teamOnly = Tagged(modes, Mate) && !Tagged(modes, Enemy);
        modes.Update(cands, TagEye, fwd, true, NameTagMode.Off, 2);
        var off = modes.Count == 0;
        var dead = new NameTags { Ray = (o, d, max) => max };
        dead.Update([TagCand(Mate, new Vector3(5, 0, -20), true), TagCand(Enemy, new Vector3(0, 0, -15), false), TagCand(Other, new Vector3(6, 0, -15), false, force: true)],
            TagEye, fwd, false, NameTagMode.All, 1);
        var deadOk = Tagged(dead, Mate) && !Tagged(dead, Enemy) && Tagged(dead, Other);
        Line($"modes: team only {teamOnly}, off {off}; dead: teammate yes, aimed enemy no, killer yes: {deadOk}");
        if (!teamOnly || !off || !deadOk)
            Fail("name tags: Team only / Off modes or the death cam rules are wrong");

        // Ray budget: one line-of-sight ray per aimed enemy, cached for 0.1 s, however many enemies line up.
        var budget = new NameTags { Ray = (o, d, max) => max };
        TagCandidate[] line = [TagCand(2, new Vector3(0, 0, -10), false), TagCand(3, new Vector3(0, 0, -20), false), TagCand(6, new Vector3(0, 0, -30), false)];
        budget.Update(line, TagEye, fwd, true, NameTagMode.All, 20);
        var first = budget.RaysThisFrame;
        var nearest = budget.FocusId;
        budget.Update(line, TagEye, fwd, true, NameTagMode.All, 20.03);
        var cached = budget.RaysThisFrame;
        budget.Update(line, TagEye, fwd, true, NameTagMode.All, 20.06);
        cached += budget.RaysThisFrame;
        budget.Update(line, TagEye, fwd, true, NameTagMode.All, 20.15);
        var recheck = budget.RaysThisFrame;
        Line($"ray budget: 3 enemies in a line -> {first} ray (tags the nearest: {nearest == 2}), next 2 frames {cached}, after 0.1 s {recheck}");
        if (first != 1 || nearest != 2 || cached != 0 || recheck != 1)
            Fail("name tags: more than one cached line-of-sight ray per aimed enemy");
    }

    // ---- Layout: size by distance and resolution, scope shrink, clamping, de-cluttering ----
    private static void NameTagLayoutCheck()
    {
        var r = NameTagRules.Default;
        var near = NameTagLayout.FontPx(r, 1.2f, 5, false);
        var far = NameTagLayout.FontPx(r, 1.2f, 60, false);
        var scoped = NameTagLayout.FontPx(r, 1.2f, 5, true);
        var hi = NameTagLayout.FontPx(r, 1.6f, 5, false); // 1440p (5120x1440, 3440x1440) vs 1080p
        var (cx, cy) = NameTagLayout.Clamp(-50, -20, 100, 16, 5120, 1440, 4);
        var (rx, ry) = NameTagLayout.Clamp(5100, 1430, 100, 16, 5120, 1440, 4);
        TagRect[] placed = [new TagRect(100, 100, 80, 16)];
        var over = new TagRect(110, 104, 80, 16);
        var stackFade = NameTagLayout.Place(placed, ref over, r, 2);
        var stacked = over.Y1 <= 100 - 2 + 0.01f && stackFade == 1f;
        TagRect[] crowded = [new TagRect(100, 100, 80, 16), new TagRect(100, 82, 80, 16)];
        var squeezed = new TagRect(110, 104, 80, 16);
        var crowdFade = NameTagLayout.Place(crowded, ref squeezed, r, 2);
        Line($"layout: {near:0.0} px at 5 m, {far:0.0} px at 60 m, {scoped:0.0} px scoped, {hi:0.0} px at 1440p; clamped to ({cx},{cy}) and ({rx},{ry}); "
             + $"overlap stacks above {stacked}, crowded fades to {crowdFade:0.00}");
        if (!(far < near) || !(scoped < near) || !(hi > near) || cx != 4 || cy != 4 || rx != 5120 - 4 - 100 || ry != 1440 - 4 - 16 || !stacked || crowdFade != r.OverlapAlpha)
            Fail("name tags: layout (distance/scope/resolution size, clamping or de-cluttering) is wrong");
    }

    // ---- settings.txt ----
    private static void NameTagSettings()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "callofslop-nametags-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllLines(tmp, ["name=Old", "fov=90", "head_bob=50"]); // a settings file from before name tags
            var old = Settings.Load(tmp);
            new Settings { NameTags = NameTagMode.TeamOnly, NameTagHealth = true }.Save(tmp);
            var back = Settings.Load(tmp);
            new Settings { NameTags = NameTagMode.Off }.Save(tmp);
            var off = Settings.Load(tmp);
            File.WriteAllLines(tmp, ["name_tags=junk", "name_tag_health=7"]);
            var junk = Settings.Load(tmp);
            File.WriteAllLines(tmp, ["name_tags=9"]);
            var number = Settings.Load(tmp);
            var d = new Settings();
            if (d.NameTags != NameTagMode.All || d.NameTagHealth || old.NameTags != NameTagMode.All || old.NameTagHealth || old.Fov != 90
                || back.NameTags != NameTagMode.TeamOnly || !back.NameTagHealth || off.NameTags != NameTagMode.Off
                || junk.NameTags != NameTagMode.All || junk.NameTagHealth || number.NameTags != NameTagMode.All)
                Fail($"name tags: settings default {d.NameTags}/{d.NameTagHealth}, old file {old.NameTags}/{old.NameTagHealth}, saved Team only + bars -> {back.NameTags}/{back.NameTagHealth}, "
                     + $"Off -> {off.NameTags}, junk -> {junk.NameTags}/{junk.NameTagHealth}, 9 -> {number.NameTags}");
            var labels = string.Join(" / ", Enum.GetValues<NameTagMode>().Select(NameTagRules.Label));
            if (labels != "All / Team only / Off")
                Fail($"name tags: settings labels are \"{labels}\"");
        }
        finally
        {
            File.Delete(tmp);
        }
        Line("name tags: settings.txt name_tags (All / Team only / Off, default All) and name_tag_health (default off) save and load; older files get All");
    }

    // ---- No allocations per frame (the rules on candidates, and the match adapter over a real game) ----
    private static void NameTagAllocations()
    {
        var wall = false;
        var tags = new NameTags { Ray = (o, d, max) => wall ? MathF.Min(max, 3f) : max };
        TagCandidate[] cands = [TagCand(1, new Vector3(5, 0, -20), true), TagCand(2, new Vector3(0, 0, -15), false), TagCand(3, new Vector3(6, 0, -15), false)];
        var fwd = new Vector3(0, 0, -1);
        var away = new Vector3(1, 0, 0);
        void Frames(int n, double start)
        {
            for (var i = 0; i < n; i++)
            {
                wall = (i / 40) % 3 == 2;
                tags.Update(cands, TagEye, (i / 25) % 2 == 0 ? fwd : away, true, NameTagMode.All, start + i / 240.0);
            }
        }
        Frames(200, 0); // warm up (JIT)
        var before = GC.GetAllocatedBytesForCurrentThread();
        Frames(2400, 100);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        // The match adapter: candidates from a real offline game with bots (fake walls, so only the tag code is measured).
        World.SetMap(Maps.All[0]);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 3);
        game.SelfTestStreakTick(0);
        long gameBytes = -1;
        var gameTags = -1;
        if (game.Local is { } me)
        {
            game.SelfTestPlace(me, new Vector3(0, 0, 0), 0);
            var z = -8f;
            foreach (var p in game.Players.Values)
                if (p != me)
                {
                    game.SelfTestPlace(p, new Vector3(0, 0, z), 0);
                    z -= 6;
                }
            var adapter = new NameTags { Ray = (o, d, max) => max };
            var eye = me.RenderEye;
            for (var i = 0; i < 200; i++)
                adapter.Update(game, eye, fwd, NameTagMode.All, i / 240.0, NameTags.None);
            var b0 = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 2400; i++)
                adapter.Update(game, eye, i % 50 < 25 ? fwd : away, NameTagMode.All, 10 + i / 240.0, NameTags.None);
            gameBytes = GC.GetAllocatedBytesForCurrentThread() - b0;
            adapter.Update(game, eye, fwd, NameTagMode.All, 30, NameTags.None);
            gameTags = adapter.Count;
        }
        Line($"allocations: {bytes} bytes over 2400 frames of the rules; {gameBytes} bytes over 2400 frames from a real match ({game.Players.Count} players, "
             + $"mode {game.Mode}, aimed tags {gameTags})");
        if (bytes != 0)
            Fail($"name tags: the per-frame rules allocate ({bytes} bytes over 2400 frames)");
        if (gameBytes != 0)
            Fail(gameBytes < 0 ? "name tags: the self-test game has no local player" : $"name tags: the per-frame match adapter allocates ({gameBytes} bytes over 2400 frames)");

        // Colours: in free-for-all a bot is an enemy (enemy red); in Zombies every player is a teammate, so tags and the HUD
        // points list use the friendly team colour, never a player's own palette colour (bot 1's is red).
        if (game.Local is { } ffaMe && game.Players.Values.FirstOrDefault(p => p != ffaMe) is { } ffaBot
            && TagColour(game, ffaMe, ffaBot) != new NameTags().Rules.EnemyColour)
            Fail($"name tags: a free-for-all enemy's tag isn't the enemy colour ({TagColour(game, ffaMe, ffaBot)})");
        static Vector3 TagColour(Game g, Player me, Player p) => new NameTags().ColourOf(g, p, !g.AreEnemies(me, p));
    }

    /// <summary>Zombies: every player is on the co-op side, so tags and the HUD points list use the friendly colour (never red).</summary>
    private static void NameTagZombiesColours()
    {
        if (ZTestMap == null)
        {
            Fail("name tags: the Zombies Test map is missing (Zombies tag colours)");
            return;
        }
        var zNow = 0.0;
        using var zg = ZGame(3, ref zNow);
        var friendlyCol = GameModes.TeamColors[0];
        var zChecked = 0;
        if (zg.Local is { } zMe)
            foreach (var p in zg.Players.Values)
            {
                if (p == zMe)
                    continue;
                zChecked++;
                var tag = TagColour(zg, zMe, p);
                if (tag != friendlyCol || zg.SideColor(p) != friendlyCol)
                    Fail($"name tags: Zombies teammate {zg.NameOf(p.Id)} (id {p.Id}) is shown in {tag} / HUD {zg.SideColor(p)}, not the friendly colour {friendlyCol}");
            }
        Line($"colours: free-for-all enemy tag = enemy colour; Zombies: {zChecked} co-op bot(s) tagged and listed in the friendly colour");
        if (zChecked == 0)
            Fail("name tags: the Zombies colour check had no co-op bots");

        World.SetMap(Maps.All[0]);

        static Vector3 TagColour(Game g, Player me, Player p) => new NameTags().ColourOf(g, p, !g.AreEnemies(me, p));
    }
}
