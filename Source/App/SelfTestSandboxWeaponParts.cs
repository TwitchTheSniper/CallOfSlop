using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Weapon builder parts in Sandbox (board item dev-sandbox-weapon-parts-wp01; docs/sandbox/CONSTRUCTS.md, "Weapon
    /// builder parts in Sandbox"): every builder part has a construct def (generated, or the hand-made file that wraps it)
    /// with a drawn model, in the inventory's "Weapon parts" group; the hand-made parts keep their ids and generating
    /// again gives the same ids; the Zombies-economy parts are creative-only by the rules file; a builder Long Barrel and a
    /// feed wrenched onto a generated receiver make a gun with the Zombies builder's own numbers; a grip on it goes where
    /// the builder's rules say; a 2x2 of a generated receiver merges with exactly one front barrel socket at its front
    /// centre; and over a real host and a separate client, the client takes a generated part by id from the parts menu and
    /// both sides see the same part.
    /// </summary>
    private static void CheckSandboxWeaponParts()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"sandbox weapon parts: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        ReceiverMergeRules.Use(null);
        WpCoverage();
        WpGun();
        WpMerge();

        if (yard.Sandbox is { } sbm)
            WpAttachFlow(yard, sbm);
        WpOverWire(yard);
    }

    /// <summary>The construct def standing for a builder part (generated or hand-made), or null.</summary>
    private static ConstructPartDef? WpDefOf(PartDef p) =>
        ConstructParts.All.FirstOrDefault(d => d.GunPartName.Equals(p.Name, StringComparison.OrdinalIgnoreCase) || d.GunReceiver.Equals(p.Name, StringComparison.OrdinalIgnoreCase) && d.IsReceiverGun);

    private static void WpCoverage()
    {
        var all = ConstructParts.All;
        var gen = all.Where(d => d.Generated).ToList();
        var missing = Parts.All.Where(p => WpDefOf(p) == null).Select(p => p.Name).ToList();
        var noModel = gen.Where(d => d.Boxes.Length - d.SolidBoxes < 1 || d.Mass > ConstructParts.HandCarryLimit || d.PhysicsOnly || !d.UsesTool(ConstructTool.Wrench)).Select(d => d.Name).ToList();
        var handMade = all.Count(d => !d.Generated);
        var order = gen.Count == 0 || gen.Min(d => d.Id) == handMade && gen.Max(d => d.Id) == all.Count - 1;
        var refused = ConstructParts.Refused.Where(r => r.StartsWith("(weapon part)") || r.StartsWith("WeaponParts.json")).ToList();
        // Generating again (as a client does at its own start) gives the same names in the same ids.
        var again = ConstructParts.WithWeaponParts(all.Where(d => !d.Generated).ToArray(), new List<string>());
        var same = again.Length == all.Count && again.Select(d => (d.Id, d.Name)).SequenceEqual(all.Select(d => (d.Id, d.Name)));
        var cat = SandboxCatalogue.Make();
        var grouped = gen.Count(d => cat.PartGroup[d.Id] == "Weapon parts");
        var sections = cat.Parts.Sections.Count(s => cat.Parts.Groups[s.Group].Name == "Weapon parts");
        var creative = gen.Where(d => d.CreativeOnly).Select(d => Parts.Find(d.GunPartName)!.Category).Distinct().OrderBy(c => c).ToList();
        var rules = ConstructWeaponParts.Current;
        var stdOk = StandardBuilds.All.Count(b => b.Build().Nodes.All(n => WpDefOf(n.Part) != null));
        Line($"coverage: {Parts.All.Length} builder parts, {gen.Count} generated defs (ids {handMade}..{all.Count - 1} of {ConstructParts.MaxTypes}), {missing.Count} without a def{(missing.Count > 0 ? $" ({string.Join(", ", missing.Take(6))})" : "")}, {noModel.Count} bad models or not items, {refused.Count} refused{(refused.Count > 0 ? $" ({refused[0]})" : "")}; again the same ids {same}; {grouped} in \"Weapon parts\" ({sections} sections); creative-only: {string.Join(", ", creative)} (rules: {string.Join(", ", rules.CreativeOnly)}); standard builds whose every part has a def: {stdOk}/{StandardBuilds.All.Length}");
        if (missing.Count > 0 || noModel.Count > 0 || refused.Count > 0 || !order || !same || all.Count > ConstructParts.MaxTypes)
            Fail("sandbox weapon parts: not every builder part has a deterministic wrench item def with a model");
        if (grouped != gen.Count || sections < 10)
            Fail("sandbox weapon parts: the generated parts aren't grouped like the builder in the inventory's Weapon parts group");
        if (!creative.SequenceEqual(rules.CreativeOnly.OrderBy(c => c)) || !rules.CreativeOnly.Contains(PartCategory.Upgrade) || !rules.CreativeOnly.Contains(PartCategory.Multiplier))
            Fail("sandbox weapon parts: the Upgrade and Multiplier parts aren't creative-only by the rules file");
        if (stdOk != StandardBuilds.All.Length)
            Fail("sandbox weapon parts: a standard build uses a part with no Sandbox def");
        // A bad rules file is refused with the reason (the defaults stay).
        var bad = new List<string>();
        ConstructWeaponParts.Load("""{ "format": "call-of-slop-weapon-parts/1", "creativeOnly": ["laser"] }""", "bad.json", bad);
        ConstructWeaponParts.Load("""{ "format": "call-of-slop-weapon-parts/1", "scale": 99 }""", "bad2.json", bad);
        if (bad.Count != 2 || !bad[0].Contains("laser") || !bad[1].Contains("scale"))
            Fail($"sandbox weapon parts: bad rules files weren't refused for their reasons ({string.Join(" / ", bad)})");
    }

    /// <summary>A generated receiver that takes the builder's Long Barrel, and a feed (its construct def) it takes.</summary>
    private static (ConstructPartDef Rec, ConstructPartDef Feed)? WpRifleReceiver()
    {
        var lb = Parts.Find("Long Barrel")!;
        foreach (var d in ConstructParts.All.Where(x => x.Generated && x.IsReceiverGun))
        {
            var rec = Parts.Find(d.GunReceiver)!;
            var root = new BuildNode(rec);
            root.Attach(Array.Find(rec.Sockets, s => s.Type == "action")!.Name, Parts.Find(d.GunAction)!);
            var bs = Array.Find(rec.Sockets, s => s.Type == "barrel");
            var fs = Array.Find(rec.Sockets, s => s.Type == "feed");
            if (bs == null || fs == null || BuildRules.SocketWhy(root, bs, lb) != null)
                continue;
            foreach (var f in Parts.All.Where(p => p.Category == PartCategory.Feed))
                if (BuildRules.SocketWhy(root, fs, f) == null && WpDefOf(f) is { } fd)
                    return (d, fd);
        }
        return null;
    }

    private static void WpGun()
    {
        if (WpRifleReceiver() is not var (recDef, feedDef))
        {
            Fail("sandbox weapon parts: no generated receiver takes a Long Barrel");
            return;
        }
        var w = new ConstructWorld();
        var c = w.Create(ConstructWorld.Ground(World.Query, -8, 6) + new Vector3(0, 0.5f, 0), Quaternion.Identity)!;
        var rec = w.AddPart(c, recDef, Vector3.Zero, 0);
        c.Rebuild();
        var lastWhy = "";
        int Fit(int on, string socket, ConstructPartDef def, string by)
        {
            var why = ConstructBuild.Place(c, on, Array.FindIndex(c.DefOf(on).Sockets, s => s.Name == socket), def, Array.FindIndex(def.Sockets, s => s.Name == by), 0, out var g);
            var i = why == null ? ConstructBuild.Attach(w, c, def, g) : -1;
            lastWhy = why ?? "";
            c.Rebuild();
            return i;
        }
        var barrelDef = WpDefOf(Parts.Find("Long Barrel")!)!;
        var barrel = Fit(rec, "front", barrelDef, "back");
        var feed = Fit(rec, "left", feedDef, "right");
        var gun = c.GunCount > 0 ? c.Guns[0] : default;
        // The Zombies builder's numbers for the same parts.
        var rp = Parts.Find(recDef.GunReceiver)!;
        var root = new BuildNode(rp);
        root.Attach(Array.Find(rp.Sockets, s => s.Type == "action")!.Name, Parts.Find(recDef.GunAction)!);
        root.Attach(Array.Find(rp.Sockets, s => s.Type == "feed")!.Name, Parts.Find(feedDef.GunPartName)!);
        root.Attach(Array.Find(rp.Sockets, s => s.Type == "ammo")!.Name, Parts.Find("FMJ")!);
        root.Attach(Array.Find(rp.Sockets, s => s.Type == "barrel")!.Name, Parts.Find("Long Barrel")!);
        var zw = BuildStats.ReceiverStats(recDef.Name, root).Def;
        // A builder grip bolted on: the builder's rules decide (on, or the gun says why).
        var gripDef = ConstructParts.All.First(d => d.Generated && Parts.Find(d.GunPartName)?.Category == PartCategory.Grip);
        var grip = -1;
        var gripOn = "";
        foreach (var (on, by) in new[] { ("bottom", "top"), ("back", "front"), ("right", "left"), ("top", "bottom") })
            if ((grip = Fit(rec, on, gripDef, by)) >= 0)
            {
                gripOn = on;
                break;
            }
        var gripProblem = c.GunCount > 0 ? c.Guns[0].Problem : "no gun";
        Line($"gun: a {recDef.Name} (generated) with a builder Long Barrel ({barrelDef.Name}, part {barrel}) and a {feedDef.Name} (part {feed}): problem \"{gun.Problem}\", {gun.Def?.Damage} damage at {gun.Def?.Rpm} rpm, mag {gun.Def?.Mag}, range {gun.Def?.RangeM:0.#} m; the Zombies builder's: {zw.Damage} at {zw.Rpm} rpm, mag {zw.Mag}, range {zw.RangeM:0.#} m; a {gripDef.Name} on its {gripOn} (part {grip}{(grip < 0 ? $": {lastWhy}" : "")}): \"{gripProblem}\"");
        if (barrel < 0 || feed < 0 || gun.Def == null || gun.Problem.Length > 0)
            Fail("sandbox weapon parts: a builder Long Barrel on a generated construct receiver doesn't make a working gun");
        else if (gun.Def.Damage != zw.Damage || gun.Def.Rpm != zw.Rpm || gun.Def.Mag != zw.Mag || MathF.Abs(gun.Def.RangeM - zw.RangeM) > 0.01f)
            Fail("sandbox weapon parts: the construct gun's numbers aren't the Zombies builder's for the same parts");
        if (grip < 0)
            Fail("sandbox weapon parts: a builder grip can't be wrenched onto a construct receiver");
    }

    /// <summary>
    /// The player's flow (board item dev-sandbox-weapon-attach-wa01): a receiver taken from the parts menu and set down
    /// (loose, frozen, or bolted on a frame), then a barrel in hand with the wrench out, the ghost the way the client works
    /// it out (<see cref="SnapBuild.Ghosts"/> near the aimed socket, the magnet's <see cref="SnapBuild.Fit"/>), the attach
    /// sent as the click sends it. A hand-made and a generated barrel, then a muzzle on that barrel.
    /// </summary>
    private static void WpAttachFlow(Map yard, SandboxMap sb)
    {
        yard.RestoreProps();
        World.SetMap(yard);
        sb.DebrisSeconds = 0;
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me)
        {
            Fail("sandbox weapon attach: no local player in the Sandbox game");
            return;
        }
        if (OpenSpot(g.Constructs) is not { } spot)
        {
            Fail("sandbox weapon attach: no open level ground on the yard");
            return;
        }
        var w = g.Constructs;
        var problems = new List<string>();
        if (WpRifleReceiver() is not var (recDef, _))
        {
            Fail("sandbox weapon attach: no generated receiver takes a barrel");
            return;
        }
        var rp = Parts.Find(recDef.GunReceiver)!;
        var rroot = new BuildNode(rp);
        rroot.Attach(Array.Find(rp.Sockets, s => s.Type == "action")!.Name, Parts.Find(recDef.GunAction)!);
        var barrelSock = Array.Find(rp.Sockets, s => s.Type == "barrel")!;
        // Barrels the builder lets onto this receiver: the hand-made one and a generated one.
        var barrels = ConstructParts.All.Where(d => d.Role == PartRole.GunPart && Parts.Find(d.GunPartName) is { Category: PartCategory.Barrel } bp && BuildRules.SocketWhy(rroot, barrelSock, bp) == null).ToList();
        var hand = barrels.FirstOrDefault(d => !d.Generated);
        var gen = barrels.Where(d => d.Generated).OrderByDescending(d => d.BoundsMax.Z - d.BoundsMin.Z).FirstOrDefault();
        if (hand == null || gen == null)
        {
            Fail($"sandbox weapon attach: no hand-made ({hand?.Name}) and generated ({gen?.Name}) barrel fits {recDef.Name}");
            return;
        }
        var muzzles = ConstructParts.All.Where(d => d.Role == PartRole.GunPart && Parts.Find(d.GunPartName) is { Category: PartCategory.Muzzle }).ToList();
        var splitter = ConstructParts.All.FirstOrDefault(d => d.Generated && d.Role == PartRole.GunPart && Parts.Find(d.GunPartName) is { Category: PartCategory.Splitter } sp && BuildRules.SocketWhy(rroot, barrelSock, sp) == null);
        var sbarrel = gen;
        var block = ConstructParts.Need("Frame Block");
        var cache = new SnapCache();
        var ghosts = new ConstructGhost[ConstructBuild.MaxGhosts];
        void Tool(ConstructTool t) => g.SandboxSend(CActKind.Tool, part: (byte)t);

        // What the client does with a held part aimed at part socket (rest frame point <near>): ghosts, the one on that socket, the click.
        string Click(Construct c, ConstructPartDef held, int part, string socketName, out int made)
        {
            made = -1;
            ConstructRender.GhostDef = held;
            var si = Array.FindIndex(c.DefOf(part).Sockets, s => s.Name == socketName);
            c.SocketAt(part, si, out var at, out _);
            var n = SnapBuild.Ghosts(c, held, cache, ghosts, at, SnapBuild.SinglePurpose(held) ? 4f : 2.5f);
            // The screen magnet, the crosshair a hand's breadth off the socket from where the player stands.
            c.SegPose(c.Parts[part].Seg, out var mq, out var mt);
            var sworld = c.ToWorld(mt + Vector3.Transform(at, mq));
            var eyeAt = me.RenderEye;
            var vp = Matrix4x4.CreateLookAt(eyeAt, sworld + new Vector3(0.05f, 0.03f, 0), Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(1.2f, 16f / 9f, 0.1f, 100f);
            var toward = Vector3.Normalize(c.ToWorld(mt + Vector3.Transform(c.Parts[part].Pos, mq)) - eyeAt); // (the part's middle: a barrel is thin)
            var wallD = World.RaycastWorld(eyeAt, toward, ConstructBuild.Reach + 1);
            var rayHit = g.Constructs.Raycast(eyeAt, toward, MathF.Min(wallD, ConstructBuild.Reach + 1), out var ch);
            if (!rayHit || ch.Body != c)
                return $"the crosshair on the {socketName} socket doesn't hit the construct (hit {rayHit}, world wall {wallD:0.00} m, {Vector3.Distance(eyeAt, sworld):0.00} m away)";
            var mk = SnapBuild.Magnet(c, cache, vp, new Vector2(1920, 1080), SnapBuild.SnapPixels);
            if (mk < 0 || cache.Part[mk] != part || cache.Socket[mk] != si)
                return $"the screen magnet doesn't pick the {socketName} socket (picked {mk} of {cache.Count} cached)";
            var k = -1;
            for (var i = 0; i < n; i++)
                if (ghosts[i].Part == part && ghosts[i].Socket == si)
                    k = i;
            if (k < 0 && !SnapBuild.Fit(c, held, part, si, -1, out _))
                return $"no ghost on {c.DefOf(part).Name}'s {socketName} ({n} ghosts)";
            if (k < 0)
                return $"{n} ghosts, none on {socketName}, though the magnet fits";
            var before = c.PartCount;
            var gh = ghosts[k];
            // Smart rotation's guess, then snap building's own-socket filter (Program.SandboxSmartGuess, SandboxSnapRank).
            var rank = new int[Rot24.Count];
            var rots = new byte[Rot24.Count];
            var rn = SmartRotation.Rank(c, held, gh.Part, gh.Socket, null, rank, rots);
            var variant = rn > 0 ? rank[0] : gh.Variant;
            if (rn > 0)
            {
                var fn = SnapBuild.FilterRank(held, rank, rots, rn, -1);
                if (fn > 0)
                    variant = rank[0];
                else
                    problems.Add($"filter left no variant (rank {rn})");
            }
            var ownName = held.Sockets[variant / 4].Name;
            if (SnapBuild.OwnOrder(held).IndexOf((byte)(variant / 4)) < 0)
                return $"the guess is by the {ownName} socket, which snap building doesn't allow";
            if (ConstructBuild.Place(c, gh.Part, gh.Socket, held, variant / 4, variant % 4, out _) is { } why)
                return $"ghost shows but is invalid: {why}";
            gh.Variant = variant;
            g.SandboxSay("");
            g.SandboxSend(CActKind.Attach, body: c.Id, part: (byte)gh.Part, socket: (byte)gh.Socket, variant: (byte)gh.Variant);
            var notice = g.SandboxNotice;
            Run(g, ref now, held.AttachSeconds + 0.3);
            made = c.PartCount - before;
            return made == 1 ? "" : $"click did nothing (\"{notice}\")";
        }

        foreach (var mode in new[] { "loose", "frozen", "mounted" })
        {
            var at = spot + new Vector3(mode == "loose" ? 0 : mode == "frozen" ? 3 : -3, 0, 0);
            me.Teleport(at + new Vector3(0, 0.05f, 2.0f));
            me.Yaw = 0;
            Run(g, ref now, 0.3);
            Tool(ConstructTool.Wrench);
            PlaceHold(g, me.Id, recDef);
            var n0 = w.Bodies.Count;
            g.SandboxSend(CActKind.PlaceNew, at: at, yaw: 0, normal: Vector3.UnitY, variant: 0, flags: ConstructPlace.FlagGrid);
            Run(g, ref now, recDef.AttachSeconds + 0.4);
            if (w.Bodies.Count <= n0)
            {
                problems.Add($"{mode}: the receiver wasn't set down (\"{g.SandboxNotice}\")");
                continue;
            }
            var c = w.Bodies[^1];
            if (mode == "frozen")
            {
                c.Frozen = true;
                c.Wake();
            }
            else if (mode == "mounted")
            {
                Tool(ConstructTool.Welder);
                PlaceHold(g, me.Id, block);
                var bottom = Array.FindIndex(recDef.Sockets, s => s.Name == "bottom");
                if (!SnapBuild.Fit(c, block, 0, bottom, -1, out var bg))
                    problems.Add("mounted: a frame block won't mount under the receiver");
                else
                {
                    g.SandboxSend(CActKind.Attach, body: c.Id, part: 0, socket: (byte)bottom, variant: (byte)bg.Variant);
                    Run(g, ref now, block.AttachSeconds + 0.4);
                }
                Tool(ConstructTool.Wrench);
            }
            Run(g, ref now, 0.5);
            StandBy(me, c.WorldCom, 3.0f);
            Run(g, ref now, 0.3);
            foreach (var barrel in new[] { hand, gen })
            {
                PlaceHold(g, me.Id, barrel);
                var fail = Click(c, barrel, 0, "front", out var made);
                Line($"attach flow: {mode} {recDef.Name} + {(barrel.Generated ? "generated" : "hand-made")} {barrel.Name}: {(fail.Length == 0 ? "attached" : fail)}; parts {c.PartCount}, gun problem \"{(c.GunCount > 0 ? c.Guns[0].Problem : "no gun")}\"");
                if (fail.Length > 0)
                    problems.Add($"{mode} + {barrel.Name}: {fail}");
                else
                {
                    // Parts onto parts: a muzzle on this barrel's front, then (barrel off) a barrel on a splitter.
                    var bi = c.PartCount - 1;
                    foreach (var mz in muzzles.Where(m => m.Generated == barrel.Generated).Take(1))
                    {
                        PlaceHold(g, me.Id, mz);
                        var mf = Click(c, mz, bi, "front", out _);
                        Line($"attach flow: {mode} {mz.Name} on the {barrel.Name}'s front: {(mf.Length == 0 ? "attached" : mf)}");
                        if (mf.Length > 0)
                            problems.Add($"{mode} muzzle {mz.Name}: {mf}");
                        else
                            g.Constructs.RemovePart(c, c.PartCount - 1);
                    }
                    g.Constructs.RemovePart(c, bi);
                }
            }
            if (splitter != null && sbarrel != null)
            {
                PlaceHold(g, me.Id, splitter);
                var sf = Click(c, splitter, 0, "front", out _);
                if (sf.Length == 0)
                {
                    var si = c.PartCount - 1;
                    var outName = splitter.Sockets.Select(x => x.Name).FirstOrDefault(x => x.StartsWith("front", StringComparison.Ordinal)) ?? "front";
                    PlaceHold(g, me.Id, sbarrel);
                    sf = Click(c, sbarrel, si, outName, out _);
                    if (sf.Length == 0)
                        g.Constructs.RemovePart(c, c.PartCount - 1);
                    g.Constructs.RemovePart(c, si);
                }
                Line($"attach flow: {mode} {splitter.Name} on the receiver, then {sbarrel.Name} on it: {(sf.Length == 0 ? "attached" : sf)}");
                if (sf.Length > 0)
                    problems.Add($"{mode} splitter: {sf}");
            }
        }
        if (problems.Count > 0)
            Fail("sandbox weapon attach: " + string.Join("; ", problems));
    }

    private static void WpProbe()
    {
        var bad = 0; var tot = 0;
        foreach (var rd in ConstructParts.All.Where(x => x.IsReceiverGun))
        {
            var w = new ConstructWorld();
            var rc = ConstructBuild.PlaceNew(w, rd, new Vector3(20, 0, 20), 0f)!;
            var rp = Parts.Find(rd.GunReceiver)!;
            var root = new BuildNode(rp);
            if (Array.Find(rp.Sockets, s => s.Type == "action") is { } asock && Parts.Find(rd.GunAction) is { } ap) root.Attach(asock.Name, ap);
            var bs = Array.Find(rp.Sockets, s => s.Type == "barrel");
            if (bs == null) continue;
            foreach (var bd in ConstructParts.All.Where(d => d.Role == PartRole.GunPart && Parts.Find(d.GunPartName) is { Category: PartCategory.Barrel } bp && BuildRules.SocketWhy(root, bs, bp) == null))
            {
                tot++;
                var fi = Array.FindIndex(rd.Sockets, s => s.Name == "front");
                var ok = SnapBuild.Fit(rc, bd, 0, fi, -1, out _);
                if (!ok) { bad++; Line($"probe: {rd.Name} gen={rd.Generated} {bd.Name} gen={bd.Generated} NOFIT place={ConstructBuild.Place(rc, 0, fi, bd, Array.FindIndex(bd.Sockets, s => s.Name == "back"), 0, out _)} own={string.Join(",", Enumerable.Range(0, bd.Sockets.Length).Select(i => bd.Sockets[i].Name + ":" + SnapBuild.OwnType(bd, i)))}"); }
            }
        }
        Line($"probe: {bad}/{tot} nofit");
    }

    private static void WpMerge()
    {
        if (WpRifleReceiver() is not var (d, _))
            return;
        var w = new ConstructWorld();
        var c = w.Create(ConstructWorld.Ground(World.Query, -8, -6) + new Vector3(0, 0.5f, 0), Quaternion.Identity)!;
        var size = d.BoundsMax - d.BoundsMin;
        foreach (var (x, y) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
            ConstructBuild.AutoLink(w, c, w.AddPart(c, d, new Vector3(x * size.X, y * size.Y, 0), 0));
        c.Rebuild();
        if (c.MergeCount != 1)
        {
            Line($"merge: four {d.Name}s in a 2x2: {c.MergeCount} merges");
            Fail("sandbox weapon parts: a 2x2 of a generated receiver didn't merge");
            return;
        }
        ref var m = ref c.Merges[0];
        var own = d.Sockets.Length;
        var fronts = 0;
        var ok = true;
        ConstructMerge.MergedBounds(c, m, out var mc, out var mh);
        for (var s = own; s < c.SocketCount(m.Lead); s++)
        {
            c.SocketAt(m.Lead, s, out var at, out var dir);
            if (dir != 5)
                continue;
            fronts++;
            ok &= MathF.Abs(at.X - mc.X) < 0.01f && MathF.Abs(at.Y - mc.Y) < 0.01f && MathF.Abs(at.Z - (mc.Z - mh.Z)) < 0.01f;
        }
        var barrelDef = WpDefOf(Parts.Find("Long Barrel")!)!;
        var why = ConstructBuild.Place(c, m.Lead, own + Array.FindIndex(d.Sockets, s => s.Name == "front"), barrelDef, Array.FindIndex(barrelDef.Sockets, s => s.Name == "back"), 0, out _);
        Line($"merge: four {d.Name}s in a 2x2: N {m.N}, {fronts} front centre socket(s) on the merged receiver (at its front centre {ok}); a Long Barrel there: {why ?? "fits"}");
        if (m.N != 2 || fronts != 1 || !ok || why != null)
            Fail("sandbox weapon parts: a merged generated receiver doesn't have exactly one front centre barrel socket");
    }

    private static void WpOverWire(Map yard)
    {
        const ushort port = 26371;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var pa))
        {
            Fail("sandbox weapon parts over the wire: the client didn't join");
            return;
        }
        if (!pa.Alive)
            server.SelfTestRespawn(pa, now);
        Pump(server, [a], 0.3, ref now, clock);
        var item = ConstructParts.All.Last(d => d.Generated && !d.CreativeOnly && d.Mass <= 40);
        a.SandboxTakePart(item.Id);
        Pump(server, [a], 0.4, ref now, clock);
        var held = server.SandboxHands.Hand(pa.Id).Held;
        a.SandboxSend(CActKind.Stow);
        Pump(server, [a], 0.4, ref now, clock);
        var pack = a.SandboxHands.Pack(pa.Id);
        var packed = pack.Def.Take(pack.SlotCount).Contains(item.Id);
        // Creative-only parts: refused with infinite parts off (survival).
        var upgrade = ConstructParts.All.First(d => d.CreativeOnly);
        var hands = server.SandboxHands;
        var inf = hands.InfiniteParts;
        hands.InfiniteParts = false;
        var refused = hands.TakePart(yard.Sandbox, pa.Id, upgrade.Id);
        hands.InfiniteParts = inf;
        Line($"over the wire: the client asked for part {item.Id} ({item.Name}): the server's hands hold {held} ({ConstructParts.Get(held)?.Name}); stowed, the client's copy of its backpack has it {packed}; a {upgrade.Name} with infinite parts off: \"{refused}\"");
        if (held != item.Id || !packed)
            Fail("sandbox weapon parts over the wire: a client and the host don't agree on a generated part's id");
        if (refused == null || !refused.Contains("creative-only"))
            Fail("sandbox weapon parts: a creative-only part was handed out with infinite parts off");
    }
}
