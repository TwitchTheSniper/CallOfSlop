using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Modular grenades" (docs/zombies/WEAPON-BUILDER.md, "Modular grenades"): every grenade part file
/// loads and broken ones are refused; every legal casing x fuse x payload (x each mod) builds, deterministically, and the
/// whole-grenade rules refuse the rest with a reason; on the Zombies Test map each fuse goes off when it should (timer,
/// cooked timer, impact, proximity, remote) and each payload does its thing to a seeded cluster of zombies (frag, fire,
/// cryo, shock, gas, lure, healing, ducks); clusters split into the right number of bomblets, aftershocks add their
/// bangs, and the live cap holds; the round refill adds a build's extra grenades; the lingering patches tick without
/// allocating; and a loopback client gets the build, throws it, flies it with the same numbers, sees the payload's
/// explosion and sets off a remote grenade.
/// </summary>
public static partial class SelfTest
{
    private static void CheckModularGrenades()
    {
        GrenadePartFiles();
        GrenadeCombos();
        var now = 0.0;
        GrenadeFusesAndPayloads(ref now);
        GrenadeLoopbackBuilt();
        int zg = (byte)Msg.ZGrenade, act = (byte)Msg.ZCasinoAct, protocol = ServerInfo.ProtocolVersion; // (locals: constants would fold at compile time)
        if (zg != act + 1 || protocol < 67)
            Fail("modular grenades: Msg.ZGrenade should come right after Msg.ZCasinoAct and the protocol be 67 or later");
    }

    /// <summary>A grenade build from part names (null when one is missing).</summary>
    private static WeaponBuild? GBuild(string name, string casing, string fuse, string payload, params string[] mods)
    {
        if (Parts.Find(casing) is not { Casing: not null } c || Parts.Find(fuse) is not { Fuse: not null } f || Parts.Find(payload) is not { Payload: not null } p)
            return null;
        var b = new WeaponBuild(name, c);
        b.Root!.Attach("fuse", f);
        b.Root.Attach("payload", p);
        var free = c.Sockets.Where(s => s.Type == "gmod").ToList();
        for (var i = 0; i < mods.Length && i < free.Count; i++)
            if (Parts.Find(mods[i]) is { GMod: not null } m)
                b.Root.Attach(free[i].Name, m);
        return b;
    }

    private static void GrenadePartFiles()
    {
        PartCategory[] cats = [PartCategory.Casing, PartCategory.Fuse, PartCategory.Payload, PartCategory.GrenadeMod];
        var counts = cats.Select(c => Parts.InCategory(c).Count()).ToArray();
        var files = cats.Sum(c => Directory.Exists(Path.Combine(Parts.Folder, c.ToString())) ? Directory.GetFiles(Path.Combine(Parts.Folder, c.ToString()), "*.json").Length : 0);
        Line($"modular grenades: parts {string.Join(", ", cats.Select((c, i) => $"{PartDef.CategoryName(c)} {counts[i]}"))} from {files} file(s); load errors {Parts.LoadErrors.Count}");
        if (counts.Sum() != files)
            Fail($"modular grenades: {files} grenade part files but {counts.Sum()} loaded ({string.Join("; ", Parts.LoadErrors.Take(3))})");
        foreach (var c in cats)
        {
            var tiers = Parts.InCategory(c).Select(p => p.Rarity).Distinct().Count();
            if (tiers != 6)
                Fail($"modular grenades: {PartDef.CategoryName(c)} parts should cover all six tiers (have {tiers})");
        }
        foreach (var k in Enum.GetValues<FuseKind>())
            if (!Parts.InCategory(PartCategory.Fuse).Any(p => p.Fuse!.Kind == k))
                Fail($"modular grenades: no {k} fuse part");
        foreach (var k in Enum.GetValues<PayloadKind>())
            if (!Parts.InCategory(PartCategory.Payload).Any(p => p.Payload!.Kind == k))
                Fail($"modular grenades: no {k} payload part");
        foreach (var p in Parts.All.Where(p => p.IsGrenadePart))
            if (p.Kind != BuildKind.Grenade || p.Effects.Length > 0 || (p.Category == PartCategory.Casing) != p.IsRoot)
                Fail($"modular grenades: {p.Name} isn't a clean grenade part");
        string[] bad =
        [
            """{"name": "Bad Casing", "category": "casing", "rarity": "common", "hand": [0, 0, 0], "casing": {}, "sockets": [{"name": "fuse", "type": "fuse", "pos": [0, 0, 0], "required": true}], "model": [{"size": [0.05, 0.05, 0.05]}]}""",
            """{"name": "Bad Fuse", "category": "fuse", "rarity": "common", "fuse": {"kind": "impact", "cook": true}, "model": [{"size": [0.02, 0.02, 0.02]}]}""",
            """{"name": "Bad Fuse Two", "category": "fuse", "rarity": "common", "fuse": {"seconds": 2}, "model": [{"size": [0.02, 0.02, 0.02]}]}""",
            """{"name": "Bad Payload", "category": "payload", "rarity": "common", "payload": {"kind": "gas", "damage": 100}, "model": [{"size": [0.02, 0.02, 0.02]}]}""",
            """{"name": "Bad Payload Two", "category": "payload", "rarity": "common", "payload": {"kind": "frag", "radius": 40}, "model": [{"size": [0.02, 0.02, 0.02]}]}""",
            """{"name": "Bad Mod", "category": "grenademod", "rarity": "common", "grenade": {"cluster": 40}, "model": [{"size": [0.02, 0.02, 0.02]}]}""",
            """{"name": "Bad Mod Two", "category": "grenademod", "rarity": "common", "model": [{"size": [0.02, 0.02, 0.02]}]}""",
            """{"name": "Bad Stock", "category": "stock", "rarity": "common", "fuse": {"kind": "timer"}, "model": [{"size": [0.02, 0.02, 0.02]}]}""",
            """{"name": "Bad Mod Three", "category": "grenademod", "rarity": "common", "grenade": {"wobble": 2}, "model": [{"size": [0.02, 0.02, 0.02]}]}""",
        ];
        var refused = 0;
        foreach (var json in bad)
            try
            {
                PartFile.Load(json, "test.json");
                Fail($"modular grenades: a broken grenade part loaded: {json[..Math.Min(60, json.Length)]}");
            }
            catch (WeaponFileException)
            {
                refused++;
            }
        Line($"modular grenades: {refused} of {bad.Length} broken grenade part files refused");
    }

    private static void GrenadeCombos()
    {
        var casings = Parts.InCategory(PartCategory.Casing).ToList();
        var fuses = Parts.InCategory(PartCategory.Fuse).ToList();
        var payloads = Parts.InCategory(PartCategory.Payload).ToList();
        var mods = Parts.InCategory(PartCategory.GrenadeMod).ToList();
        int built = 0, refused = 0, tried = 0;
        string? firstBad = null;
        var reasons = new SortedDictionary<string, int>();
        foreach (var c in casings)
            foreach (var f in fuses)
                foreach (var p in payloads)
                    for (var m = -1; m < mods.Count; m++)
                    {
                        var b = new WeaponBuild($"{c.Name} {m}", c);
                        b.Root!.Attach("fuse", f);
                        b.Root.Attach("payload", p);
                        if (m >= 0)
                        {
                            if (c.Sockets.FirstOrDefault(s => s.Type == "gmod") is not { } sock)
                                continue;
                            var why = BuildRules.WhyNot(b, "", sock.Name, mods[m]);
                            tried++;
                            if (why != null)
                            {
                                refused++;
                                var key = why.Contains("bounce") ? "sticky vs bouncy" : why.Contains("pulls zombies") ? "proximity vs lure" : why.Contains("heavy") ? "weight" : why;
                                reasons[key] = reasons.GetValueOrDefault(key) + 1;
                                continue;
                            }
                            b.Root.Attach(sock.Name, mods[m]);
                        }
                        else
                            tried++;
                        var a = WeaponBuilder.Assemble(b);
                        if (a.Grenade is not { } g)
                        {
                            var why = a.Problems.FirstOrDefault().Message ?? "no grenade";
                            if (why.Contains("pulls zombies") || why.Contains("stick") || why.Contains("heavy"))
                            {
                                refused++;
                                var key = why.Contains("bounce") ? "sticky vs bouncy" : why.Contains("pulls zombies") ? "proximity vs lure" : "weight";
                                reasons[key] = reasons.GetValueOrDefault(key) + 1;
                                continue;
                            }
                            firstBad ??= $"{b.ToText()}: {why}";
                            continue;
                        }
                        var again = WeaponBuilder.Assemble(WeaponBuild.Parse(b.ToText(), Parts.Find)).Grenade;
                        if (again == null || again.ToString() != g.ToString() || !float.IsFinite(g.Damage + g.Radius + g.ThrowSpeed + g.Duration)
                            || g.ThrowSpeed <= 0 || g.Radius < 1 || a.Def != null || a.PreviewDef?.BuiltMesh == null)
                            firstBad ??= $"{b.ToText()}: {g} vs {again}";
                        built++;
                    }
        Line($"modular grenades: {tried} combos tried, {built} built, {refused} refused ({string.Join(", ", reasons.Select(r => $"{r.Key} {r.Value}"))})");
        if (firstBad != null)
            Fail($"modular grenades: a legal combo didn't build the same twice: {firstBad}");
        if (built < 1000)
            Fail($"modular grenades: only {built} grenade combos built");

        // Every part touches the rest of the grenade it's on (each fuse, payload and mod on every casing, mods in every socket).
        var checkedModels = 0;
        foreach (var c in casings)
        {
            foreach (var f in fuses)
                foreach (var p in payloads.Take(1).Concat(f == fuses[0] ? payloads : []))
                {
                    var b = new WeaponBuild("Model", c);
                    b.Root!.Attach("fuse", f);
                    b.Root.Attach("payload", p);
                    CheckBuildConnected($"{c.Name} + {f.Name} + {p.Name}", b);
                    checkedModels++;
                }
            foreach (var m in mods)
            {
                var b = new WeaponBuild("Model", c);
                b.Root!.Attach("fuse", fuses[0]);
                b.Root.Attach("payload", payloads[0]);
                foreach (var sock in c.Sockets.Where(x => x.Type == "gmod"))
                    b.Root.Attach(sock.Name, m);
                CheckBuildConnected($"{c.Name} + {m.Name} in every mod socket", b);
                checkedModels++;
            }
        }
        Line($"modular grenades: {checkedModels} grenade models checked for floating parts");

        // The whole-grenade rules, one at a time.
        string? Why(WeaponBuild? b, string mod)
        {
            if (b == null)
                return "missing parts";
            var sock = b.Root!.Part.Sockets.First(s => s.Type == "gmod" && b.Root.Child(s.Name) == null);
            return BuildRules.WhyNot(b, "", sock.Name, Parts.Find(mod)!);
        }
        var two = Why(GBuild("Two", "Lunchbox Casing", "Pull Pin Fuse", "Black Powder", "Cluster Kit"), "Cluster Rack");
        var stick = Why(GBuild("Stick", "Sticky Sock Casing", "Pull Pin Fuse", "Black Powder"), "Spring Coil");
        var lure = BuildRules.Check(GBuild("Lure", "Tin Can Casing", "Tripwire Sensor", "Squeaky Toy Lure")!).FirstOrDefault().Message;
        var bare = BuildRules.Check(new WeaponBuild("Bare", Parts.Find("Tin Can Casing")!)).Select(p => p.Message).ToList();
        var onGun = BuildRules.WhyNot(new WeaponBuild("Gun", Parts.Find("Pistol Grip")!), "", "receiver_1", Parts.Find("Tin Can Casing")!);
        Line($"modular grenades: rules: two clusters \"{two}\"; sticky + spring \"{stick}\"; proximity + lure \"{lure}\"; bare casing: {string.Join(" / ", bare)}; a casing on a gun \"{onGun}\"");
        if (two == null || !two.Contains("splits once") || stick == null || !stick.Contains("stick") || lure == null || !lure.Contains("pulls zombies")
            || bare.Count != 2 || onGun == null || !onGun.Contains("casing"))
            Fail("modular grenades: the whole-grenade rules (one cluster mod, sticky vs bouncy, proximity vs lure, a fuse and a payload needed, a casing is a root) didn't all refuse");

        // Numbers: the mods do what they say.
        var plain = WeaponBuilder.Assemble(GBuild("Plain", "Lunchbox Casing", "Pull Pin Fuse", "Black Powder")!).Grenade!;
        var big = WeaponBuilder.Assemble(GBuild("Big", "Lunchbox Casing", "Pull Pin Fuse", "Black Powder", "Overpacker", "Wide Spreader", "Bandolier Loop")!).Grenade!;
        var fins = WeaponBuilder.Assemble(GBuild("Fins", "Lunchbox Casing", "Pull Pin Fuse", "Black Powder", "Tail Fins")!).Grenade!;
        Line($"modular grenades: {plain}\nmodular grenades: {big}\nmodular grenades: {fins}");
        if (big.Damage <= plain.Damage || big.Radius <= plain.Radius || big.Extra != 2 || fins.ThrowSpeed <= plain.ThrowSpeed || plain.Fuse != FuseKind.Timer || MathF.Abs(plain.FuseSeconds - 2.5f) > 0.01f)
            Fail("modular grenades: the mods' damage, radius, extra grenades and throw don't add up");
    }

    /// <summary>The host throws its built grenade; the newest one is returned, moved to <paramref name="at"/> and stopped.</summary>
    private static Projectile? GThrow(Game g, ref double now, Vector3 at, float cook = 0)
    {
        var me = g.Local!;
        Run(g, ref now, 0.85); // (the throw cooldown)
        var before = g.Projectiles.Keys.ToHashSet();
        me.GrenadesLeft = Math.Max(me.GrenadesLeft, 3);
        g.GrenadesLeft = me.GrenadesLeft;
        g.LookYaw = me.Yaw;
        g.LookPitch = 0.3f;
        g.TryThrow(false, now, cook);
        return GrenadeTo(g, me.Id, before, at);
    }

    /// <summary>The host's grenade slot holds <paramref name="b"/>.</summary>
    private static GrenadeDef? GEquip(Game g, WeaponBuild? b)
    {
        if (b == null || !g.GiveBuild(g.Local!, Game.GrenadeSlot, b, full: true))
            return null;
        return g.Local!.GrenadeBuild;
    }

    private static void GrenadeFusesAndPayloads(ref double nowRef)
    {
        var now = nowRef;
        GrenadeFusesAndPayloads(ref now, 0);
        nowRef = now;
    }

    private static void GrenadeFusesAndPayloads(ref double nowIn, int _)
    {
        var now = nowIn;
        var map = ZTestMap!;
        var zm = map.Zombies!;
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        me.Teleport(FreeSpot(map, zm.Areas[zm.StartArea]));
        Run(g, ref now, 0.1);
        var area = Math.Max(0, zm.Areas.FindIndex(a => a.Contains(me.Position)));

        // Equipping: the HUD and the server switch to the built grenade, keeping the count.
        var timer = GEquip(g, GBuild("Timer", "Tin Can Casing", "Pull Pin Fuse", "Black Powder"));
        Run(g, ref now, 0.1);
        Line($"modular grenades: equipped {timer?.Name}: server {me.Grenade} x{me.GrenadesLeft}, HUD {g.LocalGrenade} x{g.GrenadesLeft}");
        if (timer == null || me.Grenade != ThrowableKind.Built || g.LocalGrenade != ThrowableKind.Built)
        {
            Fail("modular grenades: a grenade build in the grenade slot isn't the grenade the player throws");
            return;
        }

        // Fuses.
        var home = me.Position;
        var (spot, cluster, _) = GrenadeTargets(g, map, zm, home, 6f, now);
        var t0 = now;
        var pr = GThrow(g, ref now, spot + new Vector3(0, 0.3f, 0));
        t0 = now;
        var early = false;
        while (pr != null && g.Projectiles.ContainsKey(pr.Id) && now - t0 < 4)
        {
            Run(g, ref now, World.TickDt);
            if (now - t0 < timer.FuseSeconds - 0.1 && !g.Projectiles.ContainsKey(pr.Id))
                early = true;
        }
        var timerAt = now - t0;
        var dead = cluster.Count(z => z.State == ZState.Dead);
        Line($"modular grenades: timer fuse {timer.FuseSeconds:0.0} s went off at {timerAt:0.00} s, frag killed {dead} of {cluster.Count}");
        if (pr == null || early || Math.Abs(timerAt - timer.FuseSeconds) > 0.1 || dead != cluster.Count)
            Fail($"modular grenades: a {timer.FuseSeconds} s timer frag went off at {timerAt:0.00} s and killed {dead} of {cluster.Count}");

        var cookDef = GEquip(g, GBuild("Cook", "Tin Can Casing", "Kitchen Timer Fuse", "Black Powder"))!;
        pr = GThrow(g, ref now, spot + new Vector3(0, 0.3f, 0), cook: 2f);
        t0 = now;
        while (pr != null && g.Projectiles.ContainsKey(pr.Id) && now - t0 < 5)
            Run(g, ref now, World.TickDt);
        var cooked = now - t0;
        Line($"modular grenades: a {cookDef.FuseSeconds:0.0} s cookable fuse cooked 2 s went off {cooked:0.00} s after the throw");
        if (Math.Abs(cooked - (cookDef.FuseSeconds - 2)) > 0.1)
            Fail($"modular grenades: a cooked timer should go off {cookDef.FuseSeconds - 2:0.0} s after the throw, not {cooked:0.00} s");

        GEquip(g, GBuild("Impact", "Tin Can Casing", "Contact Fuse", "Black Powder"));
        pr = GThrow(g, ref now, spot + new Vector3(0, 2.5f, 0));
        t0 = now;
        while (pr != null && g.Projectiles.ContainsKey(pr.Id) && now - t0 < 3)
            Run(g, ref now, World.TickDt);
        var impactAt = now - t0;
        Line($"modular grenades: an impact fuse dropped from 2.5 m went off after {impactAt:0.00} s");
        if (impactAt > 1.2)
            Fail($"modular grenades: an impact fuse should go off when it lands, not after {impactAt:0.00} s");

        var prox = GEquip(g, GBuild("Prox", "Tin Can Casing", "Tripwire Sensor", "Black Powder"))!;
        var quiet = spot;
        pr = GThrow(g, ref now, quiet + new Vector3(0, 0.2f, 0));
        Run(g, ref now, prox.FuseSeconds + 1.5);
        var waited = pr != null && g.Projectiles.ContainsKey(pr.Id);
        var walker = ZPlace(g, zm, quiet + new Vector3(0.6f, 0, 0), area, ZSpeed.Walk, now);
        walker.StunnedUntil = now + 1000;
        walker.Health = walker.MaxHealth = 5000;
        Run(g, ref now, 0.2);
        var proxGone = pr != null && !g.Projectiles.ContainsKey(pr.Id);
        Line($"modular grenades: proximity fuse waited with nobody near {waited}, went off with a zombie at 0.6 m {proxGone}");
        if (!waited || !proxGone)
            Fail("modular grenades: a proximity fuse should wait while nothing is near and go off when a zombie comes close");
        walker.Health = 0;
        walker.State = ZState.Dead;

        GEquip(g, GBuild("Remote", "Tin Can Casing", "Garage Remote", "Black Powder"));
        pr = GThrow(g, ref now, quiet + new Vector3(0, 0.2f, 0));
        Run(g, ref now, 2.0);
        var held = pr != null && g.Projectiles.ContainsKey(pr.Id);
        g.SelfTestRemoteDetonate(me);
        Run(g, ref now, 0.05);
        var remoteGone = pr != null && !g.Projectiles.ContainsKey(pr.Id);
        Line($"modular grenades: remote fuse waited 2 s {held}, went off on the detonator {remoteGone}");
        if (!held || !remoteGone)
            Fail("modular grenades: a remote fuse should wait for its thrower's key and then go off");

        // Payloads, each on a fresh tough cluster held still (their numbers are what change).
        List<Zombie> Tough()
        {
            foreach (var z in g.Horde)
                if (z.State != ZState.Dead)
                {
                    z.Health = 0;
                    z.State = ZState.Dead;
                }
            me.Teleport(home); // (blasts push the player about: the cluster goes back on the same spot)
            var (_, c, _) = GrenadeTargets(g, map, zm, home, 6f, now);
            foreach (var z in c)
                z.Health = z.MaxHealth = 20000;
            return c;
        }
        string Hp(List<Zombie> c) => string.Join("/", c.Select(z => z.Health));
        var results = new List<string>();
        void Payload(string payload, Func<List<Zombie>, double, string?> check, double wait, bool hold = false)
        {
            g.SelfTestClearGrenadeZones(); // (the last payload's fire or cloud doesn't count)
            if (GEquip(g, GBuild(payload, "Tin Can Casing", "Contact Fuse", payload)) is not { } def)
            {
                Fail($"modular grenades: no {payload} grenade");
                return;
            }
            var c = Tough();
            foreach (var z in c)
                z.StunnedUntil = 0; // (the stun payloads' own stun shows)
            var p = GThrow(g, ref now, spot + new Vector3(0, 0.25f, 0));
            foreach (var z in c)
                z.StunnedUntil = hold ? now + 1000 : 0; // held in the patch, or free so a stun shows
            var at = now;
            Run(g, ref now, wait);
            var why = p == null ? "nothing thrown" : check(c, at);
            results.Add($"{GrenadeDef.PayloadName(def.Payload)}: {Hp(c)}{(why == null ? "" : " FAILED " + why)}");
            if (why != null)
                Fail($"modular grenades: the {payload} payload: {why}");
            foreach (var z in c)
                z.StunnedUntil = now + 1000;
        }
        Payload("Nail Bomb", (c, _) => c.All(z => z.Health < z.MaxHealth) ? null : "the frag hurt nobody", 0.3);
        Payload("Lighter Fluid", (c, at) => g.ActiveZones(PayloadKind.Incendiary) == 0 ? "no fire patch" : c.Count(z => z.BurnUntil > now && z.Health < z.MaxHealth) < c.Count ? "not every zombie in the fire burns" : null, 1.5, hold: true);
        Payload("Ice Pack", (c, at) => c.All(z => z.StunnedUntil > now + 0.5 && z.SlowUntil > now + 2) ? null : "not every zombie froze and slowed", 0.3);
        Payload("Car Battery", (c, at) => c.All(z => z.StunnedUntil > now + 0.5 && z.ZapUntil > now && z.Health < z.MaxHealth) ? null : "not every zombie was zapped and stunned", 0.3);
        var hp0 = 0;
        Payload("Old Gym Socks", (c, at) =>
        {
            hp0 = c.Sum(z => z.MaxHealth - z.Health);
            return g.ActiveZones(PayloadKind.Gas) == 0 ? "no gas cloud" : c.All(z => z.Health <= z.MaxHealth - 100) ? null : "the cloud hurt too little";
        }, 2.5, hold: true);
        Payload("Squeaky Toy Lure", (c, at) =>
        {
            var lured = g.HordeAI.LureUntil >= 0 && g.ActiveZones(PayloadKind.Lure) == 1 && c.All(z => z.Health == z.MaxHealth);
            Run(g, ref now, 6.5);
            return !lured ? $"no lure going (lure until {g.HordeAI.LureUntil:0.0}, patches {g.ActiveZones(PayloadKind.Lure)}) or it hurt before its bang" : c.All(z => z.Health < z.MaxHealth) ? null : "the closing bang hurt nobody";
        }, 0.3, hold: true);
        me.Health = 30;
        Payload("First Aid Kit", (c, at) => me.Health > 30 && c.All(z => z.Health == z.MaxHealth) ? null : $"health {me.Health} (should be up from 30, zombies untouched)", 0.3);
        Payload("Rubber Duck Flock", (c, at) => c.All(z => z.Health < z.MaxHealth && z.StunnedUntil > now) ? null : "not every zombie was launched", 0.15);
        Line("modular grenades: payloads " + string.Join("; ", results));

        // Clusters and aftershocks: the bangs add up.
        foreach (var (mod, bomblets, stages) in new[] { ("Cluster Kit", 3, 0), ("Grape Bunch", 8, 0), ("Echo Charge", 0, 2), ("Nesting Doll Shell", 6, 1) })
        {
            GEquip(g, GBuild(mod, "Lunchbox Casing", "Contact Fuse", "Black Powder", mod));
            Tough();
            int b0 = g.BuiltBangs, k0 = g.BuiltBomblets, a0 = g.BuiltAftershocks;
            GThrow(g, ref now, spot + new Vector3(0, 0.25f, 0));
            Run(g, ref now, 4.0);
            int bangs = g.BuiltBangs - b0, kids = g.BuiltBomblets - k0, after = g.BuiltAftershocks - a0;
            Line($"modular grenades: {mod}: {bangs} bangs, {kids} bomblets, {after} aftershocks; {g.Projectiles.Values.Count(p => p.Kind == ThrowableKind.Built)} still flying");
            if (kids != bomblets || after != stages || bangs != 1 + bomblets + stages)
                Fail($"modular grenades: {mod} should make {bomblets} bomblets and {stages} aftershocks ({1 + bomblets + stages} bangs), not {kids}, {after} ({bangs})");
        }
        GEquip(g, GBuild("Party", "Lunchbox Casing", "Garage Remote", "Black Powder", "Party Popper"));
        var peak = 0;
        for (var i = 0; i < 9; i++)
            GThrow(g, ref now, spot + new Vector3(i * 0.2f, 0.25f, 0));
        g.SelfTestRemoteDetonate(me);
        for (var t = 0; t < 120; t++)
        {
            Run(g, ref now, World.TickDt);
            peak = Math.Max(peak, g.Projectiles.Values.Count(p => p.Kind == ThrowableKind.Built));
        }
        Line($"modular grenades: nine 12-bomblet grenades at once: at most {peak} built grenades in the air (cap {Game.MaxBuiltLive}), {g.Blasts.Count} flashes kept (cap {Game.MaxBlasts})");
        if (peak > Game.MaxBuiltLive || g.Blasts.Count > Game.MaxBlasts)
            Fail("modular grenades: the live grenade or flash cap didn't hold");
        Run(g, ref now, 3);

        // The refill: the round's count plus the build's extras; taking it apart keeps the count with the stock frag.
        GEquip(g, GBuild("Bandolier", "Tin Can Casing", "Pull Pin Fuse", "Black Powder", "Bandolier Loop"));
        me.GrenadesLeft = 0;
        g.SelfTestBeginRound(4, now);
        Run(g, ref now, 0.05);
        var want = g.ZRules.GrenadesForRound(4) + 2;
        var got = me.GrenadesLeft;
        g.ClearSlot(me, Game.GrenadeSlot);
        Line($"modular grenades: round 4 refill with a Bandolier Loop {got} (want {want}), HUD {g.GrenadesLeft}; taken apart: {me.Grenade} x{me.GrenadesLeft}");
        if (got != want || me.Grenade != g.ZRules.GrenadeKind || me.GrenadesLeft != want || me.GrenadeBuild != null)
            Fail($"modular grenades: the refill should add the build's extras ({want}, got {got}) and clearing the slot give the stock grenade back with the count kept");

        // The patches tick without allocating.
        GEquip(g, GBuild("Fire", "Tin Can Casing", "Contact Fuse", "Thermite Gel"));
        var fire = Tough();
        GThrow(g, ref now, spot + new Vector3(0, 0.25f, 0));
        Run(g, ref now, 0.3);
        GEquip(g, GBuild("Gas", "Tin Can Casing", "Contact Fuse", "Old Gym Socks"));
        GThrow(g, ref now, spot + new Vector3(0, 0.25f, 0));
        Run(g, ref now, 0.3);
        for (var i = 0; i < 240; i++)
            g.SelfTestTickGrenadeZones(now + i * World.TickDt);
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 240; i < 360; i++)
            g.SelfTestTickGrenadeZones(now + i * World.TickDt);
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Line($"modular grenades: fire + gas patches over {fire.Count} zombies: {bytes} bytes allocated in 120 ticks");
        if (bytes > 0)
            Fail($"modular grenades: ticking the fire and gas patches allocated {bytes} bytes");
    }

    /// <summary>A client gets a grenade build, throws it with the same numbers, sees its payload, and sets off a remote one.</summary>
    private static void GrenadeLoopbackBuilt()
    {
        var map = ZTestMap!;
        var zm = map.Zombies!;
        const ushort port = 60311;
        var now = 0.0;
        World.SetMap(map);
        using var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id)
        {
            MapVoteEnabled = false,
            LobbyEnabled = true,
            WarmupEnabled = true,
        };
        server.ZRules = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "test rules");
        server.ZRules.FirstRoundDelay = 9999;
        server.ZSeed = 13;
        server.Mode = GameMode.Zombies;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        PumpFast(server, [a], 1.0, ref now);
        server.LobbyStart(true, now);
        PumpFast(server, [a], Game.LobbyCountdownSeconds + 0.5, ref now);
        server.ForceStart();
        PumpFast(server, [a], Game.CountdownSeconds + 0.7, ref now);
        if (server.Phase != MatchPhase.Playing || !server.Players.TryGetValue(a.LocalId, out var ap))
        {
            Fail($"modular grenades (loopback): the match didn't start (server {server.Phase}, client {a.Phase})");
            return;
        }
        if (!ap.Alive)
        {
            ap.WantsRespawn = true;
            ap.RespawnAt = now;
            PumpFast(server, [a], 0.5, ref now);
        }
        ap.ImmuneUntil = 1e9;
        ap.Teleport(FreeSpot(map, zm.Areas[zm.StartArea]));
        var gas = GBuild("Stinker", "Rubber Ball Casing", "Pull Pin Fuse", "Old Gym Socks", "Long Burn Canister")!;
        server.GiveBuild(ap, Game.GrenadeSlot, gas, full: true);
        ap.GrenadesLeft = 3;
        PumpFast(server, [a], 0.5, ref now);
        var mine = a.Local!.GrenadeBuild;
        Line($"modular grenades (loopback): the client's grenade {mine?.Name ?? "none"}, HUD {a.LocalGrenade} x{a.GrenadesLeft}; its slot {a.ZP(a.LocalId).Slots[Game.GrenadeSlot]?.Name ?? "empty"}");
        if (mine == null || mine.ToString() != ap.GrenadeBuild?.ToString() || a.LocalGrenade != ThrowableKind.Built || a.GrenadesLeft != 3)
        {
            Fail("modular grenades (loopback): the client didn't get the grenade build and the server's count");
            return;
        }
        var before = server.Projectiles.Keys.ToHashSet();
        a.LookYaw = a.Local.Yaw;
        a.LookPitch = 0.3f;
        a.TryThrow(false, now);
        Projectile? sp = null;
        for (var k = 0; k < 20 && sp == null; k++)
        {
            PumpFast(server, [a], 0.03, ref now);
            sp = server.Projectiles.Values.FirstOrDefault(p => p.Owner == ap.Id && !before.Contains(p.Id));
        }
        for (var k = 0; k < 20 && sp != null && !a.Projectiles.ContainsKey(sp.Id); k++)
            PumpFast(server, [a], 0.03, ref now); // (the spawn message on its way)
        var cp = sp != null && a.Projectiles.TryGetValue(sp.Id, out var c) ? c : null;
        Line($"modular grenades (loopback): server projectile {sp?.Kind} with {sp?.Grenade?.Name}, bounce {sp?.Bounce:0.00}; client copy {cp?.Grenade?.Name ?? "none"}, bounce {cp?.Bounce:0.00}");
        if (sp is not { Kind: ThrowableKind.Built, Grenade: not null } || cp?.Grenade == null || MathF.Abs(cp.Bounce - sp.Bounce) > 1e-4f || cp.Grenade.ToString() != sp.Grenade.ToString())
            Fail("modular grenades (loopback): the client's throw should fly on both sides with the build's numbers");
        var bangs = a.BuiltBangs;
        PumpFast(server, [a], 3.0, ref now);
        Line($"modular grenades (loopback): client saw {a.BuiltBangs - bangs} bang(s), gas clouds on the client {a.ActiveZones(PayloadKind.Gas)}, on the server {server.ActiveZones(PayloadKind.Gas)}");
        if (a.BuiltBangs - bangs != 1 || a.ActiveZones(PayloadKind.Gas) != 1 || server.ActiveZones(PayloadKind.Gas) != 1)
            Fail("modular grenades (loopback): the client should see the payload go off and its cloud linger");

        var remote = GBuild("Clicker", "Tin Can Casing", "Garage Remote", "Black Powder")!;
        server.GiveBuild(ap, Game.GrenadeSlot, remote, full: true);
        PumpFast(server, [a], 1.0, ref now);
        before = server.Projectiles.Keys.ToHashSet();
        a.GrenadePress(now);
        PumpFast(server, [a], 1.0, ref now);
        var rp = server.Projectiles.Values.FirstOrDefault(p => p.Owner == ap.Id && !before.Contains(p.Id));
        a.GrenadePress(now); // with one out: the detonator
        PumpFast(server, [a], 0.3, ref now);
        var gone = rp != null && !server.Projectiles.ContainsKey(rp.Id);
        Line($"modular grenades (loopback): remote grenade thrown {rp != null}, set off by the client's second press {gone}; left {ap.GrenadesLeft}");
        if (rp == null || !gone)
            Fail("modular grenades (loopback): the client's second grenade key press should set off its remote grenade (Msg.ZGrenade)");
    }
}
