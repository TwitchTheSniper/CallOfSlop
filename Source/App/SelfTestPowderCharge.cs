using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>"Powder charge": a real host and client with lag and loss: a hand gun set through its build text (what the workbench sends), a merged gun and a howitzer through the configure tool's message, each fired at low, standard and high charge and its real flight measured; then a blueprint and a world save.</summary>
public static partial class SelfTest
{
    private static void CheckPowderCharge()
    {
        var problems = new List<string>();
        var g2 = World.Gravity * World.GravityScale;
        // The text: the default adds nothing, so every other build's text stays as it was.
        var build = StandardBuilds.All.Select(b => b.Build()).First(b => b.Kind == BuildKind.Gun && b.Receivers.Count == 1 && b.Receivers[0].Part.Receiver!.Bore == "rifle" && PowderCharge.Takes(b.Receivers[0].Part));
        var plain = build.ToText();
        build.Charge = 150;
        var back = WeaponBuild.Parse(build.ToText(), Parts.Find, null, migrate: false);
        if (plain.Contains('@') || back.Charge != 150 || WeaponBuild.Parse(plain, Parts.Find, null, migrate: false).Charge != PowderCharge.Default)
            problems.Add("the charge didn't go through the build text, or the default changed it");
        var w100 = BuildStats.Compute(WeaponBuild.Parse(plain, Parts.Find, null, migrate: false), out _)!;
        build.Charge = 300;
        var w300 = BuildStats.Compute(build, out _)!;
        Line($"numbers: 100%: {w100.Damage} damage, {w100.HipSpreadDeg:0.00} deg, x{w100.ChargeV}; 300%: {w300.Damage} damage, {w300.HipSpreadDeg:0.00} deg, x{w300.ChargeV:0.00}");
        if (w100.ChargeV != 1 || w300.ChargeV <= 1.5f || w300.Damage <= w100.Damage || w300.RangeM <= w100.RangeM)
            problems.Add("a charge's numbers don't follow it");

        var yard = Maps.Find("Proving Ground")!;
        const ushort port = 26399;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using (var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false })
        {
            server.Mode = GameMode.Sandbox;
            using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Pump(server, [a], 1.0, ref now, clock);
            for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
                PumpFast(server, [a], 0.25, ref now);
            Pump(server, [a], 0.5, ref now, clock);
            if (!server.Players.TryGetValue(a.LocalId, out var sp))
            {
                Fail("powder charge: the client never joined");
                return;
            }
            var w = server.Constructs;
            var hands = server.SandboxHands;
            while (w.Bodies.Count > 0)
                w.Remove(w.Bodies[^1]);
            server.Net.SimulateBadNetwork(80, 15, 4);
            a.Net.SimulateBadNetwork(80, 15, 4);
            bool Until(Func<bool> done, double seconds, string what)
            {
                var end = now + seconds;
                while (now < end)
                {
                    if (done())
                        return true;
                    Pump(server, [a], 0.05, ref now, clock);
                }
                if (done())
                    return true;
                problems.Add($"timed out: {what}");
                return false;
            }
            sp.WantsRespawn = true;
            Until(() => { sp.WantsRespawn = !sp.Alive; return sp.Alive && a.Local is { Alive: true }; }, 8, "the player spawned");

            // ---- A hand gun: level, from a height the ground sets ----
            var stand = ConstructWorld.Ground(World.Query, -120, 40) + new Vector3(0, 0.05f, 0);
            float? handK = null;
            foreach (var charge in new[] { 25, 100, 300 })
            {
                build.Charge = charge;
                var why = server.SandboxBench(sp, Game.BenchSpawn, 0, build.ToText());
                var wantV = PowderCharge.Speed(charge, PowderCharge.BarrelLength(build.Nodes.First(n => n.Part.Category == PartCategory.Barrel).Part, 0));
                if (why != null || !Until(() => a.Slots[0].Def.BuildText == build.ToText(), 3, $"the client got the {charge}% gun"))
                {
                    problems.Add($"the {charge}% gun: {why}");
                    continue;
                }
                sp.Sim.Position = stand;
                sp.Sim.Velocity = default;
                Pump(server, [a], 0.4, ref now, clock);
                a.SwitchTo(0, now);
                var marks = new List<float>();
                var v = PowderCharge.HandSpeed(a.Slots[0].Def);
                var seen = server.Tracers.Count == 0 ? 0.0 : server.Tracers[^1].Born;
                float reach = 0, bow = 0;
                for (var shot = 0; shot < 7; shot++)
                {
                    a.LookYaw = MathF.PI / 2;
                    a.LookPitch = 0;
                    a.Slots[0].Ammo = a.Slots[0].Def.Mag;
                    a.NextFireTime = 0;
                    a.EquipUntil = 0;
                    a.SelfTestWeaponFrame(now, 0.02f);
                    a.UpdateTrigger(now, true, true);
                    a.UpdateTrigger(now, false, false);
                    Pump(server, [a], 0.3, ref now, clock);
                    foreach (var t in server.Tracers)
                        if (t.Born > seen)
                        {
                            seen = t.Born;
                            var eye = sp.Eye;
                            var x = MathF.Sqrt((t.To.X - eye.X) * (t.To.X - eye.X) + (t.To.Z - eye.Z) * (t.To.Z - eye.Z));
                            var drop = eye.Y - t.To.Y;
                            marks.Add(drop - g2 / (2 * v * v) * x * x);
                            reach = MathF.Max(reach, x);
                            bow = MathF.Max(bow, t.Bow);
                        }
                }
                marks.Sort();
                var off = marks.Count > 0 ? marks[marks.Count / 2] : float.NaN;
                Line($"hand {build.Name} at {charge}%: {v:0} m/s (x{a.Slots[0].Def.ChargeV:0.00}, wanted x{wantV:0.00}), reached {reach:0} m, {marks.Count} rounds, the arc's fit off by {off:0.00} m, tracer bow {bow:0.00} m");
                if (marks.Count < 3 || MathF.Abs(a.Slots[0].Def.ChargeV - wantV) > 0.02f || !(MathF.Abs(off) <= MathF.Max(1.2f, reach * a.Slots[0].Def.HipSpreadRad * 0.75f)))
                    problems.Add($"a hand gun at {charge}% didn't fly as its speed says");
                if (charge == 25 && (reach > 80 || bow <= 0))
                    problems.Add("a low charge's round didn't come down soon, on a visible arc");
                if (charge == 300 && reach < 200)
                    problems.Add("a high charge's round didn't carry");
                handK = reach;
            }

            // ---- A merged gun, by the configure tool's message on a member that isn't the lead ----
            var rec = ConstructParts.Need("Rifle Receiver");
            var barrel = ConstructParts.Need("Gun Barrel");
            var mag = ConstructParts.Need("Box Magazine");
            var swDef = ConstructParts.Need("Switch");
            var at = ConstructWorld.Ground(World.Query, 40, 120) + new Vector3(0, 2f, 0);
            var c = ConstructBuild.PlaceNew(w, rec, new FreePlacement { Pos = at, Rot = Quaternion.Identity, Anchor = true })!; // (pinned in the air: a level shot from a known height)
            c.Owner = sp.Id;
            var size = rec.BoundsMax - rec.BoundsMin;
            for (var y = 0; y < 2; y++)
                for (var x = y == 0 ? 1 : 0; x < 2; x++)
                    ConstructBuild.AutoLink(w, c, w.AddPart(c, rec, new Vector3(x * size.X, y * size.Y, 0), 0));
            c.Rebuild();
            int FitTo(Construct on, int part, ConstructPartDef d, Func<Vector3, bool> where)
            {
                for (var s = 0; s < on.SocketCount(part); s++)
                {
                    on.SocketAt(part, s, out _, out var dir);
                    if (where(ConstructParts.DirVector(dir)) && SnapBuild.Fit(on, d, part, s, -1, out var ghost))
                    {
                        var i = ConstructBuild.Attach(w, on, d, ghost);
                        on.Rebuild();
                        return i;
                    }
                }
                return -1;
            }
            var lead = c.MergeCount == 1 ? c.Merges[0].Lead : 0;
            FitTo(c, lead, barrel, n => n.Z < -0.5f);
            var magPart = FitTo(c, lead, mag, n => n.X < -0.5f || n.Y > 0.5f);
            var trigger = FitTo(c, lead == 0 ? 1 : 0, swDef, n => n.X > 0.5f || n.Y < -0.5f);
            var member = -1;
            for (var i = 0; i < c.PartCount; i++)
                if (c.DefOf(i) == rec && i != lead)
                    member = i;
            if (c.GunCount == 0 || magPart < 0 || trigger < 0 || member < 0)
                problems.Add($"the merged gun wasn't built: {c.GunCount} guns, magazine {magPart}, switch {trigger}, problem {(c.GunCount > 0 ? c.Guns[0].Problem : "")}");
            else
            {
                w.StructureDirty.Add(c.Id);
                sp.Sim.Position = ConstructWorld.Ground(World.Query, at.X + 1.5f, at.Z);
                sp.Sim.Velocity = default;
                Pump(server, [a], 0.5, ref now, clock);
                hands.SetTool(sp.Id, ConstructTool.Linker);
                var wired = hands.Wire(w, sp.Id, sp.Eye, c.Id, trigger, c.Merges[0].Lead);
                if (wired != null)
                    problems.Add($"merged gun: the wire: {wired}");
                a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Configure);
                Pump(server, [a], 0.4, ref now, clock);
                var slot = (byte)rec.CfgSlot[(int)CfgKey.Charge];
                var kick100 = 0f;
                foreach (var charge in new[] { 100, 25, 300 })
                {
                    if (charge != 100)
                    {
                        Span<ushort> parts = [(ushort)member];
                        Span<byte> slots = [slot];
                        Span<float> values = [charge];
                        a.SandboxConfigure(c.Id, parts, slots, values);
                    }
                    var wantV = PowderCharge.Speed(charge, PowderCharge.BarrelLength(Parts.Find(barrel.GunPartName)!, 0));
                    Until(() => MathF.Abs(c.Guns[0].Def!.ChargeV - wantV) < 0.01f && a.Constructs.Get(c.Id) is { GunCount: > 0 } cc && cc.Guns[0].Def != null && MathF.Abs(cc.Guns[0].RoundV - c.Guns[0].RoundV) < 0.5f, 3, $"the merged gun took {charge}% on both peers");
                    if (charge == 100)
                        kick100 = c.Guns[0].Kick;
                    var v = c.Guns[0].RoundV;
                    c.Parts[magPart].Rounds = ConstructAmmo.Pack(800, 0);
                    c.Parts[c.Guns[0].Part].Rounds = c.Guns[0].Mag;
                    c.Parts[c.Guns[0].Part].Reload = 0;
                    var seen = a.Tracers.Count == 0 ? 0.0 : a.Tracers[^1].Born;
                    var marks = new List<float>();
                    float reach = 0;
                    c.Parts[trigger].State = 1;
                    PumpEach(server, [a], 1.6, ref now, clock, _ =>
                    {
                        foreach (var t in a.Tracers)
                            if (t.Born > seen)
                            {
                                seen = t.Born;
                                var x = MathF.Sqrt((t.To.X - t.From.X) * (t.To.X - t.From.X) + (t.To.Z - t.From.Z) * (t.To.Z - t.From.Z));
                                marks.Add(t.From.Y - t.To.Y - g2 / (2 * v * v) * x * x);
                                reach = MathF.Max(reach, x);
                            }
                    });
                    c.Parts[trigger].State = 0;
                    marks.Sort();
                    var off = marks.Count > 0 ? marks[marks.Count / 2] : float.NaN;
                    Line($"merged 2x2 at {charge}%: {v:0} m/s, kick {c.Guns[0].Kick:0} (100%: {kick100:0}), reached {reach:0} m, {marks.Count} rounds seen by the client, off the arc by {off:0.00} m");
                    if (marks.Count < 2 || !(MathF.Abs(off) <= MathF.Max(1.5f, reach * 0.03f)) || MathF.Abs(c.Guns[0].Kick - kick100 * wantV) > 1f)
                        problems.Add($"a merged gun at {charge}% didn't fly as its speed says");
                    if (charge == 25 && reach > 80)
                        problems.Add("a merged gun's low charge carried too far");
                }
                // A blueprint: the charge goes with it (a merged receiver's gun parts can't go in one yet: a single receiver).
                var one = ConstructBuild.PlaceNew(w, rec, new FreePlacement { Pos = at + new Vector3(4, 0, 0), Rot = Quaternion.Identity, Anchor = true })!;
                FitTo(one, 0, barrel, n => n.Z < -0.5f);
                FitTo(one, 0, mag, n => n.X < -0.5f);
                one.Parts[0].Cfg[slot] = 300;
                one.Rebuild();
                var text = ConstructClip.Of(one, "powder test", w).ToBlueprint("Powder test", "", out var bpWhy);
                if (text == null)
                    problems.Add($"the blueprint: {bpWhy}");
                else if (ConstructBuild.ParseBlueprint(text, "powder test") is var bp && ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, 80, 120), 0, out var built) is { } copy)
                {
                    Line($"blueprint: {copy.GunCount} guns, {copy.Guns[0].RoundV:0} m/s against {one.Guns[0].RoundV:0}");
                    if (copy.GunCount != 1 || one.Guns[0].RoundV < 600 || MathF.Abs(copy.Guns[0].RoundV - one.Guns[0].RoundV) > 0.5f)
                        problems.Add("the charge didn't go through a blueprint");
                    w.Remove(copy);
                }
                else
                    problems.Add("the blueprint didn't build");
            }

            // ---- A howitzer: a real shell ----
            var hRec = ConstructParts.All.First(d => d.Name == "Howitzer Receiver");
            var tube = ConstructParts.All.First(d => d.GunPartName == "Howitzer Tube");
            var hMag = ConstructParts.All.First(d => ConstructAmmo.IsMagazine(d) && d.Generated && ConstructAmmo.GunTakes(hRec, d) == null);
            var hAt = ConstructWorld.Ground(World.Query, -40, 120) + new Vector3(0, 2.4f, 0);
            var h = ConstructBuild.PlaceNew(w, hRec, new FreePlacement { Pos = hAt, Rot = Quaternion.Identity, Anchor = true })!;
            h.Owner = sp.Id;
            FitTo(h, 0, tube, n => n.Z < -0.5f);
            var hm = FitTo(h, 0, hMag, n => n.X < -0.5f);
            var hs = FitTo(h, 0, swDef, n => n.X > 0.5f || n.Y < -0.5f);
            if (h.GunCount == 0 || hm < 0 || hs < 0 || h.Guns[0].Problem.Length > 0)
                problems.Add($"the howitzer wasn't built: {h.GunCount} guns, {hm}, {hs}, problem {(h.GunCount > 0 ? h.Guns[0].Problem : "")}");
            else
            {
                w.StructureDirty.Add(h.Id);
                sp.Sim.Position = ConstructWorld.Ground(World.Query, hAt.X + 2f, hAt.Z);
                sp.Sim.Velocity = default;
                Pump(server, [a], 0.5, ref now, clock);
                hands.SetTool(sp.Id, ConstructTool.Linker);
                var wired = hands.Wire(w, sp.Id, sp.Eye, h.Id, hs, 0);
                if (wired != null)
                    problems.Add($"howitzer: the wire: {wired}");
                a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Configure);
                Pump(server, [a], 0.4, ref now, clock);
                var slot = (byte)hRec.CfgSlot[(int)CfgKey.Charge];
                var baseSpeed = 0f;
                var baseBlast = 0f;
                float baseFlight = 0;
                foreach (var charge in new[] { 100, 40, 200 })
                {
                    if (charge != 100)
                    {
                        Span<ushort> parts = [0];
                        Span<byte> slots = [slot];
                        Span<float> values = [charge];
                        a.SandboxConfigure(h.Id, parts, slots, values);
                    }
                    var wantV = PowderCharge.Speed(charge, PowderCharge.BarrelLength(Parts.Find("Howitzer Tube")!, 0));
                    Until(() => h.GunCount > 0 && h.Guns[0].Def is { } hd && MathF.Abs(hd.ChargeV - wantV) < 0.01f, 3, $"the howitzer took {charge}%");
                    if (charge == 100)
                    {
                        baseSpeed = h.Guns[0].Speed;
                        baseBlast = h.Guns[0].Blast;
                    }
                    h.Parts[hm].Rounds = ConstructAmmo.Pack(4, 0);
                    h.Parts[0].Rounds = h.Guns[0].Mag;
                    h.Parts[0].Reload = 0;
                    h.Parts[0].Cool = 0;
                    var last = default(Vector3);
                    var first = default(Vector3);
                    var flying = false;
                    var landed = false;
                    h.Parts[hs].State = 1;
                    PumpEach(server, [a], 7, ref now, clock, _ =>
                    {
                        if (landed)
                            return;
                        var any = false;
                        foreach (ref var s in w.Shells.AsSpan())
                            if (s.Id != 0 && !s.Landed && s.Gun == h.Id)
                            {
                                any = true;
                                if (!flying)
                                    first = s.Pos;
                                flying = true;
                                last = s.Pos;
                                h.Parts[hs].State = 0;
                            }
                        landed = flying && !any;
                    });
                    h.Parts[hs].State = 0;
                    var flew = new Vector2(last.X - first.X, last.Z - first.Z).Length();
                    Line($"howitzer at {charge}%: {h.Guns[0].Speed:0} m/s (x{h.Guns[0].Speed / MathF.Max(1f, baseSpeed):0.00}, wanted x{wantV:0.00}), reach {h.Guns[0].MaxRange:0} m, blast {h.Guns[0].Blast:0.0} m, flew {flew:0} m level from {first.Y - ConstructWorld.Ground(World.Query, last.X, last.Z).Y:0.0} m up");
                    if (charge == 100)
                        baseFlight = flew;
                    if (!flying || !landed || MathF.Abs(h.Guns[0].Speed - baseSpeed * wantV) > 1f || MathF.Abs(h.Guns[0].Blast - baseBlast) > 0.3f || !(MathF.Abs(flew - baseFlight * wantV) <= MathF.Max(6f, baseFlight * wantV * 0.25f)))
                        problems.Add($"a howitzer at {charge}% didn't fly as its speed says");
                }
            }
        }

        // ---- A world save ----
        if (Maps.Find(ConstructYardMap) is { } home)
        {
            home.RestoreProps();
            World.SetMap(home);
            const string saveName = "Powder charge test";
            var folder = WorldStore.FolderFor(saveName);
            WorldStore.Delete(folder);
            var session = Game.WorldNew(saveName, home, false);
            var rifle = ConstructParts.Need("Rifle Receiver");
            using (var g = ModeGame(0, GameMode.Sandbox, home.Id))
            {
                g.WorldAttach(session);
                Run(g, ref now, 0.5);
                if (OpenSpot(g.Constructs) is { } spot && ConstructBuild.PlaceNew(g.Constructs, rifle, new FreePlacement { Pos = spot + new Vector3(0, 1, 0), Rot = Quaternion.Identity }) is { } piece)
                {
                    piece.Parts[0].Cfg[rifle.CfgSlot[(int)CfgKey.Charge]] = 250;
                    if (!g.WorldSaveNow(true))
                        problems.Add("the world didn't save");
                }
                else
                    problems.Add("no room for the save's gun");
            }
            if (WorldStore.List().FirstOrDefault(e => e.Folder == folder) is { } entry && Game.WorldOpen(entry, out _) is { } loaded)
                using (var g = ModeGame(0, GameMode.Sandbox, home.Id))
                {
                    g.WorldAttach(loaded);
                    Run(g, ref now, 0.5);
                    var found = g.Constructs.Bodies.FirstOrDefault(b => b.PartCount == 1 && b.DefOf(0) == rifle);
                    if (found == null || (int)ConstructConfig.Get(found.Parts[0], rifle, CfgKey.Charge, 0) != 250)
                        problems.Add("the charge didn't go through a world save");
                }
            else
                problems.Add("the saved world didn't open");
            WorldStore.Delete(folder);
        }
        foreach (var p in problems)
            Fail($"powder charge: {p}");
    }
}
