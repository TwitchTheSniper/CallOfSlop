using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombie animation" (Source/Render/ZombieAnim.cs, Source/Game/ZombieAnimNet.cs): hands on the board
/// at every window of the Zombies maps while tearing, the yanked board leaving with the hands, hands on the sill while
/// climbing in, rising arms first, the swipe peaking as the damage lands, bones that keep their lengths, per-zombie
/// variety, the animation state over the network (still 12 bytes a zombie), and no allocations / the cost for 24 zombies.
/// Pure maths and one short scripted packet round trip: no matches.
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombieAnim()
    {
        var maps = new List<(Map Map, ZombiesMap Zm)>();
        foreach (var name in new[] { BlackmoorName, "Zombies Test" })
            if (Maps.Find(name) is { Zombies: { } zm } m)
                maps.Add((m, zm));
            else
                Fail($"zombie animation: the map '{name}' or its zombies section is missing");
        if (maps.Count == 0)
            return;
        var rules = ZombiesRules.Current;

        ZombieAnimInput Base(ZAnimMode mode, Vector3 pos, float yaw, ushort id)
        {
            var a = default(ZombieAnimInput);
            a.Pos = pos;
            a.Yaw = yaw;
            a.Mode = mode;
            a.Id = id;
            a.SinceState = 5f;
            a.Time = id * 0.37f;
            a.Attack = -1;
            a.Stagger = -1;
            a.Windup = rules.ZombieWindup;
            a.TearSeconds = rules.BoardTearSeconds;
            return a;
        }

        // ---- Tearing: both hands on the top remaining board, at every window, from where zombies stand ----
        float tearWorst = 0, yankWorst = 0, vaultWorst = 0, maxStep = 0;
        int tearCases = 0, vaultCases = 0;
        ushort[] ids = [1, 2, 3, 5, 8, 13, 21];
        foreach (var (map, zm) in maps)
            for (var w = 0; w < zm.Windows.Count; w++)
            {
                var win = zm.Windows[w];
                var outward = win.Outside - win.Inside;
                outward.Y = 0;
                outward = Vector3.Normalize(outward);
                var lateral = new Vector3(-outward.Z, 0, outward.X);
                // The AI keeps a tearing zombie within 0.3 m of the outside spot (ZombieAI.cs).
                Vector3[] spots = [win.Outside, win.Outside + outward * 0.3f, win.Outside + outward * 0.2f + lateral * 0.2f, win.Outside - lateral * 0.25f];
                foreach (var spot in spots)
                    foreach (var id in ids)
                        for (var boards = 1; boards <= win.Boards; boards++)
                        {
                            foreach (var phase in new[] { 0.55f, 0.75f, 0.9f, 0.98f })
                            {
                                var a = Base(ZAnimMode.Tearing, spot, win.Yaw, id);
                                a.SetWindow(win, boards);
                                a.Phase = phase;
                                ZombieAnim.Evaluate(a, out var p);
                                var err = MathF.Max(Vector3.Distance(p.HandR, ZombieAnim.BoardGrip(a, boards - 1, 1)), Vector3.Distance(p.HandL, ZombieAnim.BoardGrip(a, boards - 1, -1)));
                                if (err > tearWorst)
                                {
                                    tearWorst = err;
                                    if (err > 0.03f && tearCases < 100000)
                                        Line($"  tear miss: {map.Name} window {w} boards {boards} phase {phase} id {id}: {err * 100:0.0} cm");
                                }
                                maxStep = MathF.Max(maxStep, Vector3.Distance(new Vector3(p.FootR.X, 0, p.FootR.Z), new Vector3(spot.X, 0, spot.Z)));
                                tearCases++;
                            }
                            if (boards == win.Boards)
                                continue;
                            // The yank: the board that just came off (slot `boards`) goes with the hands (ZombieAnim.BoardFlight).
                            foreach (var u in new[] { 0.01f, 0.05f, 0.1f })
                            {
                                var a = Base(ZAnimMode.Tearing, spot, win.Yaw, id);
                                a.SetWindow(win, boards);
                                a.Phase = u;
                                ZombieAnim.Evaluate(a, out var p);
                                var board = ZombieAnim.BoardFlight(a, boards, u * a.TearSeconds, 0, win.Outside.Y, out _);
                                var mid = (p.HandR + p.HandL) * 0.5f;
                                yankWorst = MathF.Max(yankWorst, Vector3.Distance(mid, new Vector3(board.M41, board.M42, board.M43)));
                            }
                        }

                // ---- Vaulting: hands on the sill while the body goes over (the server's climb path) ----
                foreach (var from in new[] { win.Outside, win.Outside + outward * 0.3f, win.Outside + lateral * 0.2f })
                    foreach (var id in ids)
                        foreach (var t in new[] { 0.2f, 0.3f, 0.4f, 0.5f })
                        {
                            var pos = Vector3.Lerp(from, win.Inside, t);
                            pos.Y += MathF.Sin(t * MathF.PI) * 0.9f; // ZombieAI.cs: up over the sill and down
                            var a = Base(ZAnimMode.Vaulting, pos, win.Yaw, id);
                            a.SetWindow(win, 0);
                            a.Phase = t;
                            ZombieAnim.Evaluate(a, out var p);
                            var err = MathF.Max(Vector3.Distance(p.HandR, ZombieAnim.SillGrip(a, 1)), Vector3.Distance(p.HandL, ZombieAnim.SillGrip(a, -1)));
                            if (err > vaultWorst)
                            {
                                vaultWorst = err;
                                if (err > 0.03f)
                                    Line($"  vault miss: {map.Name} window {w} t {t} id {id}: {err * 100:0.0} cm");
                            }
                            vaultCases++;
                        }
            }
        Line($"tearing: {tearCases} poses at {maps.Sum(m => m.Zm.Windows.Count)} windows, hands worst {tearWorst * 100:0.0} cm off the board (step in up to {maxStep:0.00} m); "
             + $"yank: hands worst {yankWorst * 100:0.0} cm from the flying board; vaulting: {vaultCases} poses, hands worst {vaultWorst * 100:0.0} cm off the sill");
        if (tearWorst > 0.03f)
            Fail($"tearing zombies' hands miss the board by {tearWorst * 100:0.0} cm (at most 3)");
        if (yankWorst > 0.07f)
            Fail($"the torn board doesn't follow the hands ({yankWorst * 100:0.0} cm apart)");
        if (vaultWorst > 0.03f)
            Fail($"climbing zombies' hands miss the sill by {vaultWorst * 100:0.0} cm (at most 3)");

        // ---- Bones keep their lengths, everything finite, feet on the ground when walking ----
        var badBones = 0;
        var feetOff = 0f;
        var allModes = new[] { ZAnimMode.Locomotion, ZAnimMode.Tearing, ZAnimMode.WindowIdle, ZAnimMode.Vaulting, ZAnimMode.Rising, ZAnimMode.Dead };
        var w0 = maps[0].Zm.Windows[0];
        for (var mi = 0; mi < allModes.Length; mi++)
            for (var i = 0; i < 40; i++)
            {
                var a = Base(allModes[mi], new Vector3(3, 0.5f, -2), i * 0.4f, (ushort)(i + 1));
                a.SetWindow(w0, i % 7);
                a.Pos = w0.Outside;
                a.Yaw = w0.Yaw;
                a.Speed = (ZSpeed)(i % 3);
                a.Gait = i * 0.77f;
                a.Move = (i % 4) / 3f;
                a.Phase = i / 39f * (allModes[mi] == ZAnimMode.Dead ? 2f : 1f);
                a.Attack = i % 5 == 0 ? i * 0.03f : -1;
                a.Flinch = i % 6 == 0 ? 0.8f : 0;
                a.Stagger = i % 7 == 0 ? 0.2f : -1;
                if (allModes[mi] == ZAnimMode.Locomotion)
                    a.Pos = new Vector3(3, 0.5f, -2);
                ZombieAnim.Evaluate(a, out var p);
                bool Len(Vector3 x, Vector3 y, float l) => MathF.Abs(Vector3.Distance(x, y) - l) < 2e-3f;
                var fin = float.IsFinite(p.HandR.X + p.HandL.Y + p.FootR.Z + p.FootL.X + p.Head.M42 + p.Upper.M41 + p.ArmLoR.M43 + p.LegLoL.M42);
                if (!fin || !Len(p.ShoulderR, p.ElbowR, ZombieAnim.ArmUp) || !Len(p.ElbowL, p.HandL, ZombieAnim.ArmLo) || !Len(p.HipR, p.KneeR, ZombieAnim.LegUp) || !Len(p.KneeL, p.FootL, ZombieAnim.LegLo))
                    badBones++;
                if (allModes[mi] == ZAnimMode.Locomotion && a.Attack < 0 && a.Stagger < 0)
                    feetOff = MathF.Max(feetOff, MathF.Max(MathF.Max(a.Pos.Y - 0.02f - MathF.Min(p.FootR.Y, p.FootL.Y), 0), MathF.Min(p.FootR.Y, p.FootL.Y) - a.Pos.Y - 0.3f));
            }
        Line($"bones: {allModes.Length * 40} poses over every mode, {badBones} with a bone stretched or not finite; walking feet worst {feetOff * 100:0.0} cm off the ground");
        if (badBones > 0)
            Fail($"{badBones} zombie poses stretch a bone or aren't finite");
        if (feetOff > 0.03f)
            Fail($"walking zombies' feet leave the ground ({feetOff * 100:0.0} cm)");

        // ---- Rising: arms first, then hands planted on the ground, then standing ----
        {
            var ground = new Vector3(1, 0.25f, 1);
            ZombiePose At(float r)
            {
                var a = Base(ZAnimMode.Rising, ground, 0.5f, 9);
                a.Phase = r;
                ZombieAnim.Evaluate(a, out var p);
                return p;
            }
            var early = At(0.15f);
            var mid = At(0.5f);
            var done = At(1f);
            var earlyHead = Vector3.Transform(new Vector3(0, 0.87f, -0.04f), early.Head).Y; // the cranium
            var armsFirst = MathF.Min(early.HandR.Y, early.HandL.Y) > ground.Y + 0.1f && earlyHead < ground.Y;
            var planted = MathF.Abs(mid.HandR.Y - ground.Y) < 0.06f && MathF.Abs(mid.HandL.Y - ground.Y) < 0.06f;
            var standing = MathF.Abs(done.Upper.M42 - (ground.Y + ZombieAnim.PelvisY)) < 0.12f && MathF.Abs(done.FootR.Y - ground.Y) < 0.03f;
            Line($"rising: at 15% hands {early.HandR.Y - ground.Y:+0.00;-0.00} m with the head at {earlyHead - ground.Y:+0.00;-0.00} m; at 50% hands {mid.HandR.Y - ground.Y:+0.00;-0.00} m (planted); at the end hips {done.Upper.M42 - ground.Y:0.00} m; {rules.RiseSeconds:0.0} s");
            if (!armsFirst || !planted || !standing)
                Fail("rising zombies should come out arms first, push up on the ground, then stand");
        }

        // ---- The swipe: wound up behind as the windup ends, across the front just after (the damage lands at the windup) ----
        {
            var a = Base(ZAnimMode.Locomotion, Vector3.Zero, 0, 4);
            var look = new ZombieLook(4);
            a.Attack = rules.ZombieWindup - 0.01f;
            ZombieAnim.Evaluate(a, out var wound);
            a.Attack = rules.ZombieWindup + 0.13f;
            ZombieAnim.Evaluate(a, out var struck);
            a.Attack = rules.ZombieWindup + 1f;
            ZombieAnim.Evaluate(a, out var after);
            a.Attack = -1;
            ZombieAnim.Evaluate(a, out var none);
            var hw = look.SwipeSide > 0 ? wound.HandR : wound.HandL;
            var hs = look.SwipeSide > 0 ? struck.HandR : struck.HandL;
            var sh = look.SwipeSide > 0 ? wound.ShoulderR : wound.ShoulderL;
            var back = hw.Z > sh.Z - 0.05f && hw.Y > sh.Y;
            var front = hs.Z < sh.Z - 0.35f && MathF.Sign(hs.X - sh.X) == -look.SwipeSide;
            var recovered = Vector3.Distance(after.HandR, none.HandR) < 0.01f && Vector3.Distance(after.HandL, none.HandL) < 0.01f;
            Line($"swipe: at the end of the windup the hand is {hw.Z - sh.Z:+0.00;-0.00} m behind / {hw.Y - sh.Y:+0.00;-0.00} up from the shoulder; 0.13 s later {sh.Z - hs.Z:0.00} m in front, across; recovered {recovered}");
            if (!back || !front || !recovered)
                Fail("the zombie swipe should wind up behind and strike across the front as the damage lands");
        }

        // ---- Per-zombie variety (seeded by id) ----
        {
            var styles = new int[3];
            int limps = 0, limpR = 0;
            float rollMin = 1, rollMax = -1;
            for (ushort id = 1; id <= 200; id++)
            {
                var l = new ZombieLook(id);
                styles[l.ArmStyle]++;
                if (l.Limp > 0)
                {
                    limps++;
                    if (l.LimpSide > 0)
                        limpR++;
                }
                rollMin = MathF.Min(rollMin, l.HeadRoll);
                rollMax = MathF.Max(rollMax, l.HeadRoll);
            }
            var same = new ZombieLook(77).ArmStyle == new ZombieLook(77).ArmStyle && new ZombieLook(77).Limp == new ZombieLook(77).Limp;
            Line($"variety over 200 zombies: arm styles {styles[0]}/{styles[1]}/{styles[2]}, {limps} limp ({limpR} on the right), head roll {rollMin:0.00}..{rollMax:0.00} rad; the same id the same look {same}");
            if (styles.Any(s => s < 30) || limps is < 40 or > 170 || limpR < limps / 5 || limpR > limps * 4 / 5 || rollMax - rollMin < 0.4f || !same)
                Fail("zombies should vary (arm pose, limp, head tilt) by id, the same on every PC");
        }

        // ---- The network: a client sees the same animation state (protocol 43, still 12 bytes a zombie) ----
        var now = 0.0;
        var zt = ZTestMap!;
        var ztm = zt.Zombies!;
        using (var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999))
        {
            var winSpawner = ztm.Spawners.FindIndex(s => s.Window == 0);
            var groundSpawner = ztm.Spawners.FindIndex(s => s.Window < 0);
            var tear = g.SelfTestSpawnZombie(winSpawner, now);
            tear.State = ZState.Tearing;
            tear.NextTear = now + rules.BoardTearSeconds * 0.4; // 60% through a board
            var climb = g.SelfTestSpawnZombie(winSpawner, now);
            climb.State = ZState.Climbing;
            climb.StateAt = now - rules.ClimbSeconds * 0.35;
            var rise = groundSpawner >= 0 ? g.SelfTestSpawnZombie(groundSpawner, now) : g.SelfTestSpawnZombie(winSpawner, now);
            rise.State = ZState.Rising;
            rise.Window = -1;
            var swipe = g.SelfTestSpawnZombie(winSpawner, now);
            swipe.State = ZState.Attack;
            swipe.Window = -1;
            swipe.Yaw = 1.1f;
            var chase = g.SelfTestSpawnZombie(winSpawner, now);
            chase.State = ZState.Chase;
            chase.Window = -1;
            chase.Yaw = -2f;
            g.ServerTick++;
            g.SelfTestZombieAnimTick(now);

            var w = new PacketWriter();
            g.WriteZEnts(w);
            var ents = w.Span.ToArray();
            var w2 = new PacketWriter();
            g.WriteZWorld(w2);
            using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
            World.SetMap(zt);
            c.SelfTestReadZombies(ents, w2.Span.ToArray());
            // Then the tearing zombie gets shot: the client should flinch it on the same tick.
            g.ServerTick += 2;
            tear.Health -= tear.MaxHealth / 3;
            g.SelfTestZombieAnimTick(now + 2 * World.TickDt);
            g.WriteZEnts(w);
            var ents2 = w.Span.ToArray();
            g.WriteZWorld(w2);
            c.SelfTestReadZombies(ents2, w2.Span.ToArray());

            var bytesPer = (ents.Length - 6) / Math.Max(1, g.Horde.Count);
            int same = 0, total = 0;
            var worstPhase = 0f;
            g.SelfTestAnimTick = c.SelfTestAnimTick = g.ServerTick - 0.5;
            foreach (var z in g.Horde)
            {
                total++;
                if (c.Horde.FirstOrDefault(x => x.Id == z.Id) is not { } cz)
                    continue;
                cz.RenderPos = z.RenderPos = z.Position;
                cz.RenderYaw = z.RenderYaw = z.Yaw;
                if (z.State is ZState.Tearing or ZState.Climbing)
                    z.RenderYaw = cz.RenderYaw = ztm.Windows[z.Window].Yaw;
                g.ZombieAnimInputFor(z, 10, out var sa);
                c.ZombieAnimInputFor(cz, 10, out var ca);
                worstPhase = MathF.Max(worstPhase, MathF.Abs(sa.Phase - ca.Phase));
                var ok = sa.Mode == ca.Mode && MathF.Abs(sa.Phase - ca.Phase) < 0.01f && MathF.Abs(World.WrapAngle(sa.Yaw - ca.Yaw)) < 0.03f && MathF.Abs(sa.SinceState - ca.SinceState) < 0.001f
                         && MathF.Abs(sa.Attack - ca.Attack) < 0.001f && MathF.Abs(sa.Flinch - ca.Flinch) < 0.01f && cz.AnimByte == z.AnimByte;
                if (ok)
                    same++;
                else
                    Line($"  zombie {z.Id} {z.State}: host {sa.Mode} phase {sa.Phase:0.000} yaw {sa.Yaw:0.00} since {sa.SinceState:0.000} attack {sa.Attack:0.00} flinch {sa.Flinch:0.00}; client {ca.Mode} phase {ca.Phase:0.000} yaw {ca.Yaw:0.00} since {ca.SinceState:0.000} attack {ca.Attack:0.00} flinch {ca.Flinch:0.00}");
            }
            var ctear = c.Horde.FirstOrDefault(x => x.Id == tear.Id);
            g.ZombieAnimInputFor(tear, 10, out var tearIn);
            Line($"network: {same}/{total} zombies animate the same on a client (tearing at {tearIn.Phase:0.00} of a board, climbing, rising, swiping, chasing; phase worst {worstPhase:0.000}), "
                 + $"flinch tick {ctear?.FlinchTick ?? -1} vs {tear.FlinchTick}; {bytesPer} bytes a zombie, protocol {ServerInfo.ProtocolVersion}");
            g.SelfTestAnimTick = c.SelfTestAnimTick = null;
            if (same != total || total != 5 || ctear == null || ctear.FlinchTick != tear.FlinchTick || tear.FlinchTick < 0)
                Fail("a client doesn't see the zombies' animation state the host sees");
            if (bytesPer != Game.ZEntBytes || Game.ZEntBytes != 12 || ServerInfo.ProtocolVersion < 43)
                Fail($"ZEnts should stay at 12 bytes a zombie (got {bytesPer}) with protocol 43 or later");
            if (MathF.Abs(tearIn.Phase - 0.6f) > 0.03f)
                Fail($"the tearing zombie's board cycle should be 0.6 (got {tearIn.Phase:0.00})");

            // ---- No allocations, and the cost for 24 zombies posed and drawn (headless recorder) ----
            var rec = new DrawRecorder { Keep = false };
            var gfx = new Renderer { Recorder = rec };
            var inputs = new ZombieAnimInput[24];
            for (var i = 0; i < inputs.Length; i++)
            {
                var a = Base(allModes[i % 5], ztm.Windows[0].Outside + new Vector3(i * 0.1f, 0, 0), ztm.Windows[0].Yaw, (ushort)(i + 1));
                a.SetWindow(ztm.Windows[0], 1 + i % 6);
                a.Speed = (ZSpeed)(i % 3);
                a.Move = 1;
                a.Phase = (i * 0.13f) % 1f;
                inputs[i] = a;
            }
            var smooth = new ZombieTargets[24];
            void Frame(int f, bool lod)
            {
                for (var i = 0; i < inputs.Length; i++)
                {
                    inputs[i].Gait = f * 0.1f + i;
                    inputs[i].Phase = (inputs[i].Phase + 0.01f) % 1f;
                    ZombieAnim.Targets(inputs[i], out var tg);
                    ZombieAnim.Smooth(ref smooth[i], tg, 0.5f);
                    ZombieAnim.Solve(inputs[i], smooth[i], out var pose);
                    ZombieAnim.Draw(gfx, pose, new Vector3(0.3f), new Vector3(0.4f, 0.5f, 0.3f), new Vector3(0.2f), true, lod, true);
                }
                foreach (var z in g.Horde)
                    g.ZombieAnimInputFor(z, f * 0.016, out _);
            }
            for (var f = 0; f < 50; f++)
                Frame(f, f % 2 == 0); // warm up (JIT)
            rec.Reset();
            var sw = new Stopwatch();
            var before = GC.GetAllocatedBytesForCurrentThread();
            sw.Start();
            const int frames = 300;
            for (var f = 0; f < frames; f++)
                Frame(f, false);
            sw.Stop();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            var boxes = rec.Count / (double)(frames * inputs.Length);
            rec.Reset();
            Frame(0, true);
            var lodBoxes = rec.Count / (double)inputs.Length;
            var usPer = sw.Elapsed.TotalMilliseconds * 1000 / (frames * inputs.Length);
            Line($"cost: 24 zombies posed and drawn in {usPer * 24:0.0} us a frame ({usPer:0.00} us each, CPU side incl. the recorded draws), {boxes:0.0} boxes each up close, {lodBoxes:0.0} far; {allocated} bytes allocated over {frames} frames");
            if (allocated > 0)
                Fail($"posing zombies allocates ({allocated} bytes over {frames} frames)");
            if (usPer > 60)
                Fail($"posing a zombie costs {usPer:0.0} us (budget 60)");
        }
    }
}
