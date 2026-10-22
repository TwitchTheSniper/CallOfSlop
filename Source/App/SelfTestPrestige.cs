using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "Prestige" self-test section: the hat catalog and its unlock rule (Cosmetics.cs), prestiging (Progression.cs:
/// only at max level, what resets, the cap, progress.txt round trips including old saves), bots never having a prestige
/// or a hat (GameCosmetics.cs, HatRender.cs), the hat models (build, sit on the head, the ridiculous top hat's height,
/// scene graph = immediate drawing) and no gameplay effect (hitboxes ignore hats).
/// </summary>
public static partial class SelfTest
{
    private static void CheckPrestige()
    {
        PrestigeCatalog();
        PrestigeProgress();
        PrestigeSaves();
        PrestigeBots();
        PrestigeModels();
        PrestigeNoGameplay();
    }

    private static Progress MaxedProgress(int prestige = 0)
    {
        var p = new Progress { ReadOnly = true, Prestige = prestige };
        p.Xp = Progress.TotalFor(Progress.MaxLevel);
        return p;
    }

    // ---- The catalog and IsUnlocked ----
    private static void PrestigeCatalog()
    {
        Cosmetics.Reload();
        var hats = Cosmetics.Hats;
        if (Cosmetics.LoadError is { } err)
            Fail($"prestige: Cosmetics/hats.json didn't load: {err}");
        (HatId, string, string, int)[] want =
        [
            (HatId.Beanie, "beanie", "Beanie", 1), (HatId.BaseballCap, "baseball_cap", "Baseball Cap", 2), (HatId.Fedora, "fedora", "Fedora", 3),
            (HatId.TopHat, "top_hat", "Top Hat", 4), (HatId.TallTopHat, "tall_top_hat", "Ridiculous Top Hat", 5),
        ];
        if (hats.Count != want.Length)
            Fail($"prestige: {hats.Count} hats in the catalog, want {want.Length}");
        foreach (var (id, key, name, prestige) in want)
        {
            var h = Cosmetics.Get(id);
            if (h == null || h.Key != key || h.Name != name || h.Prestige != prestige)
                Fail($"prestige: hat {id} is {(h == null ? "missing" : $"{h.Key}/{h.Name}/P{h.Prestige}")}, want {key}/{name}/P{prestige}");
            if (Cosmetics.FromKey(key) != id || Cosmetics.FromKey(key.ToUpperInvariant()) != id || Cosmetics.KeyOf(id) != key || Cosmetics.HatForPrestige(prestige) != id)
                Fail($"prestige: key round trip for {key} is wrong");
        }
        if (Cosmetics.Get(HatId.None) != null || Cosmetics.FromKey("none") != HatId.None || Cosmetics.FromKey("sombrero") != HatId.None || Cosmetics.KeyOf(HatId.None) != "none")
            Fail("prestige: None / unknown keys must map to no hat");

        var bad = 0;
        for (var pr = 0; pr <= Cosmetics.MaxPrestige; pr++)
        {
            if (!Cosmetics.IsUnlocked(HatId.None, pr))
                bad++;
            foreach (var (id, _, _, need) in want)
                if (Cosmetics.IsUnlocked(id, pr) != (pr >= need))
                    bad++;
            if (Cosmetics.IsUnlocked((HatId)99, pr))
                bad++;
        }
        if (bad > 0)
            Fail($"prestige: IsUnlocked broke the rule (None always, a hat at its prestige or above, unknown ids never) {bad} time(s)");

        // A broken catalog falls back to the built-in hats with the same ids and prestige.
        var builtIn = Cosmetics.BuiltInCatalog();
        if (builtIn.Count != 5 || builtIn.Any(h => h.Prestige != (int)h.Id || h.Boxes.Count == 0))
            Fail("prestige: the built-in fallback catalog is wrong");
        try
        {
            Cosmetics.Parse("{\"format\":\"call-of-slop-cosmetics/1\",\"hats\":[{\"id\":\"crown\",\"name\":\"Crown\",\"prestige\":1,\"parts\":[]}]}", "test");
            Fail("prestige: an unknown hat id in a cosmetics file must be an error");
        }
        catch (FormatException)
        {
        }
        Line($"  catalog: {hats.Count} hats ({string.Join(", ", hats.Select(h => $"P{h.Prestige} {h.Name} {h.Boxes.Count} boxes"))}), unlock rule checked for prestige 0-{Cosmetics.MaxPrestige}");
    }

    // ---- CanPrestige, DoPrestige, the cap ----
    private static void PrestigeProgress()
    {
        var p = new Progress { ReadOnly = true };
        if (p.CanPrestige || p.DoPrestige())
            Fail("prestige: possible at level 1");
        p.Xp = Progress.TotalFor(Progress.MaxLevel) - 1;
        if (p.Level != Progress.MaxLevel - 1 || p.CanPrestige || p.DoPrestige())
            Fail($"prestige: possible one XP short of max level (level {p.Level})");

        var locked = Progress.All.Where(u => u.Level > 1 && u.Weapon != null).Select(u => u.Weapon!).First();
        var perk = Progress.All.First(u => u.Level > 1 && u.Perk != null).Perk!.Value;
        var streak = Progress.All.First(u => u.Level > 1 && u.Streak != null).Streak!.Value;
        var starter = Weapons.DefaultPrimary;
        var gun = Weapons.All.First(w => Attachments.For(w).Any());
        for (var n = 1; n <= Cosmetics.MaxPrestige; n++)
        {
            p = Progress.Parse([.. MaxedProgress(n - 1).ToLines(), $"wx.{gun.Name}=99999", "kills=1234", "wins=77"]);
            p.ReadOnly = true;
            if (!p.CanPrestige || !p.IsUnlocked(locked) || p.WeaponXp(gun.Name) != 99999)
                Fail($"prestige {n}: setup wrong (can {p.CanPrestige}, level {p.Level})");
            if (!p.DoPrestige(10))
            {
                Fail($"prestige {n}: DoPrestige refused at max level");
                continue;
            }
            if (p.Prestige != n || p.Level != 1 || p.Xp != 0 || p.WeaponXp(gun.Name) != 0)
                Fail($"prestige {n}: after DoPrestige prestige {p.Prestige}, level {p.Level}, XP {p.Xp}, weapon XP {p.WeaponXp(gun.Name)} (want {n}, 1, 0, 0)");
            if (p.IsUnlocked(locked) || p.IsUnlocked(perk) || p.IsUnlocked(streak) || p.LoadoutSlots != 1 || !p.IsUnlocked(starter))
                Fail($"prestige {n}: level-gated unlocks didn't lock again ({locked.Name} {p.IsUnlocked(locked)}, {perk} {p.IsUnlocked(perk)}, {streak} {p.IsUnlocked(streak)}, slots {p.LoadoutSlots})");
            if (Attachments.For(gun).Any(a => Attachments.UnlockXp(gun, a) > 0 && Attachments.Sanitize(gun, Attachments.With(0, a.Slot, a.Index), p) != 0))
                Fail($"prestige {n}: attachments on {gun.Name} are still unlocked");
            if (p.Kills != 1234 || p.Wins != 77)
                Fail($"prestige {n}: lifetime stats must be kept (kills {p.Kills}, wins {p.Wins})");
            if (p.CanPrestige || p.DoPrestige())
                Fail($"prestige {n}: could prestige again straight away at level 1");
            var hat = Cosmetics.Get(Cosmetics.HatForPrestige(n))!;
            if (!p.LevelUpBanner.Contains(hat.Name) || !p.PrestigeSummary().Contains("Prestige " + (n + 1)) && n < 5)
                Fail($"prestige {n}: banner \"{p.LevelUpBanner}\" doesn't name the {hat.Name}");
            if (p.PrestigeSummary().Any(c => c > 126) || p.LevelUpBanner.Any(c => c > 126))
                Fail($"prestige {n}: texts must be plain ASCII");
        }
        var top = MaxedProgress(Cosmetics.MaxPrestige);
        if (top.CanPrestige || top.DoPrestige() || top.Prestige != Cosmetics.MaxPrestige || top.Level != Progress.MaxLevel)
            Fail($"prestige: past the cap of {Cosmetics.MaxPrestige} (can {top.CanPrestige}, prestige {top.Prestige}, level {top.Level})");
        var first = MaxedProgress();
        var summary = first.PrestigeSummary();
        if (!summary.StartsWith("Prestige 1: unlocks the Beanie", StringComparison.Ordinal))
            Fail($"prestige: the dialog should start \"Prestige 1: unlocks the Beanie\", got \"{summary[..Math.Min(40, summary.Length)]}\"");
        if (Progress.RankLabel(0, 13) != "L13" || Progress.RankLabel(2, 13) != "P2 L13")
            Fail($"prestige: rank labels \"{Progress.RankLabel(0, 13)}\" / \"{Progress.RankLabel(2, 13)}\"");
        // Settings > Reset level keeps the prestige.
        var reset = MaxedProgress(3);
        reset.Reset();
        if (reset.Prestige != 3 || reset.Level != 1)
            Fail($"prestige: Reset level changed the prestige ({reset.Prestige})");
        Line($"  prestige: only at level {Progress.MaxLevel}, resets XP + weapon XP (locks {locked.Name}, {perk}, {streak}, attachments, slots), keeps stats, caps at {Cosmetics.MaxPrestige}");
    }

    // ---- progress.txt ----
    private static void PrestigeSaves()
    {
        var p = MaxedProgress(4);
        var back = Progress.Parse(p.ToLines());
        if (back.Prestige != 4 || back.Xp != p.Xp)
            Fail($"prestige: save round trip gave prestige {back.Prestige}, XP {back.Xp}");
        var old = Progress.Parse(["xp=5000", "kills=10", "headshots=2", "matches=3", "wins=1", "wx.Vanguard AR=700"]);
        if (old.Prestige != 0 || old.Xp != 5000 || old.WeaponXp("Vanguard AR") != 700)
            Fail($"prestige: an old progress.txt (no prestige line) loaded as prestige {old.Prestige}, XP {old.Xp}");
        if (Progress.Parse(["prestige=99"]).Prestige != Cosmetics.MaxPrestige || Progress.Parse(["prestige=-4"]).Prestige != 0 || Progress.Parse(["prestige=abc"]).Prestige != 0)
            Fail("prestige: out-of-range prestige values in progress.txt must be clamped");
        Line($"  saves: \"{string.Join(" ", p.ToLines().Take(2))}\" round trips; old saves load as prestige 0");
    }

    // ---- Bots: no prestige, no hat, no rank tag; the server checks hats ----
    private static void PrestigeBots()
    {
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 2)
            { Progress = MaxedProgress(3) };
        var host = game.Local!;
        var bots = game.Players.Values.Where(p => p.Bot != null).ToList();
        if (bots.Count == 0)
            Fail("prestige: no bots in the test game");
        foreach (var b in bots)
        {
            if (game.ServerSetHat(b, HatId.Beanie) != HatId.None || b.Hat != HatId.None || b.Prestige != 0)
                Fail($"prestige: bot {b.Name} got a hat or a prestige ({b.Hat}, P{b.Prestige})");
            b.Prestige = 5;
            b.Hat = HatId.TallTopHat; // even if something set it, bots are never drawn with one
            if (HatRender.HatFor(b) != null)
                Fail($"prestige: bot {b.Name} is drawn with a hat");
            if (game.RankTag(b) != "")
                Fail($"prestige: bot {b.Name} has a rank tag \"{game.RankTag(b)}\"");
            b.Prestige = 0;
            b.Hat = HatId.None;
        }
        if (game.ServerSetHat(host, HatId.Fedora) != HatId.Fedora || host.Prestige != 3 || HatRender.HatFor(host)?.Id != HatId.Fedora)
            Fail($"prestige: a prestige 3 player can't wear the Fedora ({host.Hat}, P{host.Prestige})");
        if (game.ServerSetHat(host, HatId.TopHat) != HatId.None || host.Hat != HatId.None)
            Fail("prestige: a prestige 3 player got the Top Hat (prestige 4)");
        if (game.ServerSetHat(host, (HatId)77) != HatId.None)
            Fail("prestige: an unknown hat id was accepted");
        if (game.RankTag(host) != "  [P3 L" + Progress.MaxLevel + "]")
            Fail($"prestige: host rank tag \"{game.RankTag(host)}\"");
        // A client claiming more than it has: the drawn hat is checked against the prestige it reported.
        host.Prestige = 1;
        host.Hat = HatId.TopHat;
        if (HatRender.HatFor(host) != null)
            Fail("prestige: a hat above the player's prestige is drawn");
        host.Hat = HatId.None;
        Line($"  bots: {bots.Count} bots refused hats, no prestige or rank tag; host P3 wears the Fedora, not the Top Hat; tag \"{game.RankTag(host).Trim()}\"");
    }

    // ---- Models: on the head, heights, scene graph = immediate, never in first person ----
    private static void PrestigeModels()
    {
        var oldRec = new DrawRecorder();
        var newRec = new DrawRecorder();
        var oldG = new Renderer { Recorder = oldRec };
        var newG = new Renderer { Recorder = newRec };
        var list = new DrawList();
        var scene = new Scene();
        var parity = new Parity();
        var p = new Player { Id = 9, Prestige = 5 };
        p.Teleport(new Vector3(3.5f, 0.25f, -2f));
        p.RenderPosition = p.Position;
        var rig = new PlayerRig(p.Id);
        scene.AddRoot(rig.Root);
        var now = 321.0;
        var topHat = Cosmetics.Get(HatId.TopHat)!.Height;
        var tall = Cosmetics.Get(HatId.TallTopHat)!.Height;
        if (topHat < 0.3f || tall < 3 * topHat || tall > 1.95f)
            Fail($"prestige: top hat {topHat:0.00} m, ridiculous top hat {tall:0.00} m (want 3-4x and under 2 m)");
        foreach (var h in Cosmetics.Hats)
        {
            p.Hat = h.Id;
            p.Alive = true;
            if (h.Boxes.Count == 0 || h.Boxes.Count > 48 || h.Height <= 0.05f)
                Fail($"prestige: {h.Name} model has {h.Boxes.Count} boxes, height {h.Height:0.00}");
            foreach (var stance in (ReadOnlySpan<Stance>)[Stance.Stand, Stance.Crouch, Stance.Prone])
            {
                now += 0.37;
                p.RenderStance = stance;
                p.RenderYaw = (float)(now * 0.9 % MathF.Tau);
                p.RenderVelocity = new Vector3(MathF.Sin((float)now) * 5, 0, MathF.Cos((float)now) * 5);
                p.RenderPosition += new Vector3(0.04f, 0, 0.02f);
                oldRec.Reset();
                PlayerRender.DrawImmediate(oldG, p, now);
                newRec.Reset();
                rig.Pose(p, now);
                scene.UpdateTransforms();
                list.Clear();
                list.Flatten(rig.Root);
                list.Submit(newG);
                CompareDraws($"prestige {h.Name} {stance}", oldRec, newRec, parity);

                // Sits on the head: centred over it, bottom near the top of the head.
                var node = rig.HatNode(stance switch { Stance.Crouch => 1, Stance.Prone => 2, _ => 0 });
                if (!node.Visible || node.ChildCount != h.Boxes.Count)
                    Fail($"prestige: {h.Name} {stance}: hat node visible {node.Visible}, {node.ChildCount} boxes");
                var (min, max) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
                for (var i = 0; i < node.ChildCount; i++)
                    for (var c = 0; c < 8; c++)
                    {
                        var v = Vector3.Transform(new Vector3((c & 1) - 0.5f, ((c >> 1) & 1) - 0.5f, ((c >> 2) & 1) - 0.5f), node.Child(i).World);
                        (min, max) = (Vector3.Min(min, v), Vector3.Max(max, v));
                    }
                var headTop = stance switch { Stance.Crouch => 1.2f, Stance.Prone => 0.49f, _ => World.PlayerHeight } + p.RenderPosition.Y;
                if (stance == Stance.Stand)
                {
                    var center = (min + max) / 2 - p.RenderPosition;
                    if (min.Y < headTop - 0.26f || min.Y > headTop + 0.05f || MathF.Sqrt(center.X * center.X + center.Z * center.Z) > 0.25f + h.Height * 0.2f)
                        Fail($"prestige: {h.Name} isn't on the head (bottom {min.Y - p.RenderPosition.Y:0.00} m, head top {World.PlayerHeight}, offset {center.X:0.00},{center.Z:0.00})");
                    if (h.Id == HatId.TallTopHat && max.Y - headTop < 3 * topHat * 0.9f)
                        Fail($"prestige: the ridiculous top hat only reaches {max.Y - headTop:0.00} m above the head");
                }
                else if (max.Y < headTop)
                {
                    Fail($"prestige: {h.Name} {stance} is below the head top ({max.Y - p.RenderPosition.Y:0.00} vs {headTop - p.RenderPosition.Y:0.00})");
                }
            }
            p.Alive = false;
            oldRec.Reset();
            PlayerRender.DrawImmediate(oldG, p, now);
            newRec.Reset();
            rig.Pose(p, now);
            scene.UpdateTransforms();
            list.Clear();
            list.Flatten(rig.Root);
            list.Submit(newG);
            CompareDraws($"prestige {h.Name} corpse", oldRec, newRec, parity);
            if (newRec.Count != 1 + h.Boxes.Count)
                Fail($"prestige: {h.Name} corpse draws {newRec.Count}, want the corpse + {h.Boxes.Count} hat boxes");
            // Your own first-person legs: never the hat.
            p.Alive = true;
            rig.Pose(p, now, legsOnly: true);
            scene.UpdateTransforms();
            if (rig.HatNode(0).Visible || rig.HatNode(1).Visible || rig.HatNode(2).Visible)
                Fail($"prestige: {h.Name} is visible in first person");
        }
        // No hat: the same draws as before hats existed (no extra nodes drawn).
        p.Hat = HatId.None;
        p.Alive = true;
        p.RenderStance = Stance.Stand;
        newRec.Reset();
        rig.Pose(p, now);
        scene.UpdateTransforms();
        list.Clear();
        list.Flatten(rig.Root);
        list.Submit(newG);
        var bare = newRec.Count;
        p.Hat = HatId.Beanie;
        newRec.Reset();
        rig.Pose(p, now);
        scene.UpdateTransforms();
        list.Clear();
        list.Flatten(rig.Root);
        list.Submit(newG);
        if (newRec.Count != bare + Cosmetics.Get(HatId.Beanie)!.Boxes.Count)
            Fail($"prestige: the beanie added {newRec.Count - bare} draws, want {Cosmetics.Get(HatId.Beanie)!.Boxes.Count}");
        Line($"  models: hat parity {parity.Cases} poses, {parity.Draws} draws, worst {parity.Worst:0.0e0}, {parity.Bad} mismatching; top hat {topHat:0.00} m, ridiculous {tall:0.00} m ({tall / topHat:0.0}x); hidden in first person");
    }

    // ---- No gameplay effect ----
    private static void PrestigeNoGameplay()
    {
        var p = new Player { Id = 3 };
        p.Teleport(new Vector3(1, 0, 1));
        var bad = 0;
        foreach (var stance in (ReadOnlySpan<Stance>)[Stance.Stand, Stance.Crouch, Stance.Prone])
        {
            p.Sim.Stance = stance;
            p.Hat = HatId.None;
            p.Prestige = 0;
            var before = World.Hitbox(p.Position, p.Sim.Stance, p.Yaw);
            p.Hat = HatId.TallTopHat;
            p.Prestige = 5;
            var after = World.Hitbox(p.Position, p.Sim.Stance, p.Yaw);
            if (before != after)
                bad++;
        }
        // A shot through the ridiculous top hat (well above the head) misses the player.
        p.Sim.Stance = Stance.Stand;
        var box = World.Hitbox(p.Position, Stance.Stand, 0);
        var through = box.Raycast(p.Position + new Vector3(-5, World.PlayerHeight + 0.6f, 0), Vector3.UnitX, 10f);
        if (bad > 0 || through != null)
            Fail($"prestige: hats changed the hitbox ({bad} stance(s)) or a shot through the hat hit ({through})");
        // A hat costs nothing in the snapshot: only the roster carries it (3 bytes a player with prestige and level).
        Line($"  no gameplay effect: hitboxes identical with and without hats in 3 stances, a shot through the tall hat misses");
        p.Hat = HatId.None;
    }
}
