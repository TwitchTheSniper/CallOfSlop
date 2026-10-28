using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test sections "Blood overlay" and "Body blood" (blood by health; Source/UI/BloodOverlay.cs, Source/Game/BodyBlood.cs,
/// Source/Render/BodyBloodRender.cs): the overlay's steps against health, the direction of new splatters, fading on regen
/// and Stim, clearing on death and respawn, the Subtle / Off settings, no allocations; the server recording hit parts and
/// directions (armour first), the blood word round trip and protocol, mark counts against health, two clients placing the
/// same marks from the same snapshot, Off drawing nothing, ragdolls keeping their marks, settings save, and the CPU cost
/// with 32 marked bodies.
/// </summary>
public static partial class SelfTest
{
    private static void CheckBloodOverlay()
    {
        var a = new Vector4[BloodOverlay.MaxInstances];
        var b = new Vector4[BloodOverlay.MaxInstances];
        const float w = 2560, h = 1080; // 21:9
        var (left, span) = HudLayout.Span(w, h, HudWidth.Wide16x9);
        double now = 0;
        const float dt = 1f / 120f;

        int Splats(BloodOverlay o, BloodMode mode = BloodMode.Full)
        {
            var n = o.Build(a, b, w, h, left, span, mode);
            var s = 0;
            for (var i = 0; i < n; i++)
                if (b[i].Y >= 0)
                    s++;
            return s;
        }
        void Run(BloodOverlay o, float frac, bool alive, int sector, double seconds)
        {
            for (var t = 0.0; t < seconds; t += dt)
            {
                now += dt;
                o.Update(frac, alive, sector, now, dt);
            }
        }

        // ---- Steps: none above 85%, more layers at 70 / 50 / 30 / 15% (health set directly, no hit splatters) ----
        var steps = new List<int>();
        float[] fracs = [1f, 0.9f, 0.8f, 0.65f, 0.45f, 0.25f, 0.1f];
        foreach (var f in fracs)
        {
            var o = new BloodOverlay();
            Run(o, 1f, true, 0, 0.05);
            o.Clear();
            Run(o, f, true, 0, 0.05); // a drop: counts as a hit below 85%, so count the base layers only
            steps.Add(o.Base.ToArray().Count(s => s.Alpha > 0.5f));
        }
        Line($"overlay layers at health {string.Join("/", fracs.Select(f => $"{f * 100:0}%"))}: {string.Join("/", steps)} (steps at {string.Join("/", BloodOverlay.Steps.Select(s => $"{s * 100:0}"))}%)");
        if (steps[0] != 0 || steps[1] != 0)
            Fail("blood overlay: splatters above 85% health");
        for (var i = 2; i < steps.Count; i++)
            if (steps[i] <= steps[i - 1])
                Fail($"blood overlay: no more splatters at {fracs[i] * 100:0}% than at {fracs[i - 1] * 100:0}%");

        // ---- Each hit adds a splatter on the side it came from; the centre stays clear ----
        var ov = new BloodOverlay();
        Run(ov, 1f, true, 0, 0.1);
        var sides = new List<string>();
        (int Sector, string Name, Func<Vector2, bool> Ok)[] dirs =
        [
            (4, "right", p => p.X > 0.85f), (12, "left", p => p.X < 0.15f), (0, "front/top", p => p.Y > 0.85f), (8, "behind/bottom", p => p.Y < 0.15f),
        ];
        var frac = 0.8f;
        foreach (var (sector, name, ok) in dirs)
        {
            frac -= 0.1f;
            var before = ov.HitsAdded;
            Run(ov, frac, true, sector, 0.05);
            var newest = ov.Hits.ToArray().Where(s => s.Live).OrderByDescending(s => s.Born).First();
            sides.Add($"{name} ({newest.Pos.X:0.00}, {newest.Pos.Y:0.00})");
            if (ov.HitsAdded != before + 1 || !ok(newest.Pos))
                Fail($"blood overlay: a hit from sector {sector} ({name}) put its splatter at ({newest.Pos.X:0.00}, {newest.Pos.Y:0.00})");
        }
        Line($"hit splatters: {string.Join(", ", sides)}");
        var n = ov.Build(a, b, w, h, left, span, BloodMode.Full);
        var inner = 0f; // how far any splatter reaches towards the centre (fraction of the half height / half span)
        for (var i = 0; i < n; i++)
        {
            if (b[i].Y < 0)
                continue;
            var cx = (a[i].X - (left + span / 2)) / (span / 2);
            var cy = (a[i].Y - h / 2) / (h / 2);
            var reach = MathF.Max(MathF.Abs(cx) - a[i].Z / (span / 2), MathF.Abs(cy) - a[i].Z / (h / 2));
            inner = MathF.Max(inner, 1f - reach);
            if (a[i].X < left - 1 || a[i].X > left + span + 1)
                Fail("blood overlay: a splatter centre outside the HUD span");
        }
        Line($"at {frac * 100:0}% health: {n} instances ({Splats(ov)} splatters + vignette bands), deepest reach {inner * 100:0}% of the way to the centre");
        if (inner > 0.45f) // quad bounds; the masks' blood itself stops well short of them
            Fail("blood overlay: splatters reach too far towards the centre of the screen");

        // ---- Fades as health regenerates; Stim fades it at once; armour (no health drop) adds nothing ----
        var regen = new BloodOverlay();
        Run(regen, 1f, true, 0, 0.05);
        Run(regen, 0.3f, true, 4, 0.2);
        var low = Splats(regen);
        var hitsBefore = regen.HitsAdded;
        Run(regen, 0.3f, true, 4, 1.0); // armour soaking hits: health doesn't change, no new splatters
        var armourHits = regen.HitsAdded - hitsBefore;
        var mid = 0;
        for (var f = 0.3f; f < 1f; f += Regen.Rate / World.MaxHealth * 0.1f) // regeneration at the real rate, in 0.1 s steps
        {
            Run(regen, f, true, 4, 0.1);
            if (MathF.Abs(f - 0.62f) < 0.01f)
                mid = Splats(regen);
        }
        Run(regen, 1f, true, 4, 0.6);
        var healed = Splats(regen);
        var stim = new BloodOverlay();
        Run(stim, 1f, true, 0, 0.05);
        Run(stim, 0.2f, true, 8, 0.2);
        var stimLow = Splats(stim);
        Run(stim, 1f, true, 8, 0.6); // a Stim Shot: straight back to full
        var stimAfter = Splats(stim);
        Line($"regeneration: {low} splatters at 30% -> {mid} at ~62% -> {healed} at full; armour-soaked hits added {armourHits}; Stim from 20%: {stimLow} -> {stimAfter} after 0.6 s");
        if (low == 0 || mid >= low || healed != 0)
            Fail("blood overlay: splatters don't fade as health regenerates");
        if (stimAfter != 0 || armourHits != 0)
            Fail("blood overlay: a Stim heal must clear it quickly; armour-soaked hits must add nothing");

        // ---- Clears at once on death; respawn starts clean; Subtle and Off ----
        var death = new BloodOverlay();
        Run(death, 1f, true, 0, 0.05);
        Run(death, 0.1f, true, 2, 0.3);
        var nearDeath = Splats(death);
        var pulseSeen = death.Pulse < 1f;
        var full = death.Build(a, b, w, h, left, span, BloodMode.Full);
        var subtle = death.Build(a, b, w, h, left, span, BloodMode.Subtle);
        var off = death.Build(a, b, w, h, left, span, BloodMode.Off);
        Run(death, 0f, false, 2, 1f / 120f);
        var dead = death.Build(a, b, w, h, left, span, BloodMode.Full);
        Run(death, 1f, true, 0, 1f / 120f);
        var respawned = death.Build(a, b, w, h, left, span, BloodMode.Full);
        Line($"near death (10%): {nearDeath} splatters, vignette {death.Vignette:0.00}, pulse {(pulseSeen ? "on" : "off")}; Full {full} / Subtle {subtle} / Off {off} instances; dead {dead}, respawned {respawned}");
        if (!pulseSeen || nearDeath <= low)
            Fail("blood overlay: no pulse or no extra splatters near death");
        if (off != 0 || subtle >= full || subtle == 0)
            Fail("blood overlay: Off must draw nothing and Subtle less than Full");
        if (dead != 0 || respawned != 0)
            Fail("blood overlay: must clear on death and start clean on respawn");

        // ---- Textures, allocations, settings ----
        var tex = BloodTextures.Generate();
        var cover = new double[BloodTextures.Layers];
        var borderMax = 0;
        const int sz = BloodTextures.Size;
        for (var l = 0; l < BloodTextures.Layers; l++)
            for (var y = 0; y < sz; y++)
                for (var x = 0; x < sz; x++)
                {
                    var v = tex[(l * sz + y) * sz + x];
                    cover[l] += v > 64 ? 1 : 0;
                    if (x == 0 || y == 0 || x == sz - 1 || y == sz - 1)
                        borderMax = Math.Max(borderMax, v);
                }
        var same = tex.AsSpan().SequenceEqual(BloodTextures.Generate());
        Line($"splatter masks: {BloodTextures.Layers} x {sz}px, coverage {string.Join("/", cover.Select(c => $"{c / (sz * sz) * 100:0}%"))}, border max {borderMax}, deterministic {same}");
        if (!same || borderMax > 8 || cover.Any(c => c / (sz * sz) < 0.08 || c / (sz * sz) > 0.6))
            Fail("blood overlay: splatter masks must be deterministic, fade before the border and cover a sensible area");

        var allocOv = new BloodOverlay();
        Run(allocOv, 1f, true, 0, 0.05);
        Run(allocOv, 0.3f, true, 4, 0.05);
        // The least of a few identical rounds (SelfTestCpuRatio.cs): a real per-call allocation shows in every round, a one-off runtime allocation in one at most.
        var alloc = SteadyAllocBytes(() =>
        {
            for (var i = 0; i < 2000; i++)
            {
                now += dt;
                allocOv.Update(0.3f - (i % 50) * 0.004f, true, i & 15, now, dt);
                allocOv.Build(a, b, w, h, left, span, BloodMode.Full);
            }
        }, warmups: 0);
        Line($"2000 frames of overlay update + build (a hit every frame): {alloc} bytes allocated");
        if (alloc > 0)
            Fail("blood overlay: allocates per frame");

        var st = new Settings { BloodOverlay = BloodMode.Subtle, BloodColour = BloodColour.Dark };
        var path = Path.Combine(Path.GetTempPath(), $"cos_blood_{Environment.ProcessId}.txt");
        try
        {
            st.Save(path);
            var back = Settings.Load(path);
            File.WriteAllText(path, "volume=0.5\n"); // an old save without the keys
            var old = Settings.Load(path);
            Line($"settings: saved Subtle/Dark -> loaded {back.BloodOverlay}/{back.BloodColour}; an old save loads {old.BloodOverlay}/{old.BloodColour}");
            if (back.BloodOverlay != BloodMode.Subtle || back.BloodColour != BloodColour.Dark || old.BloodOverlay != BloodMode.Full || old.BloodColour != BloodColour.Red)
                Fail("blood overlay: settings don't save, or an old save doesn't default to Full / Red");
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    private static void CheckBodyBlood()
    {
        World.SetMap(Maps.All[0]);
        var marks = new Vector4[BloodMarks.MaxMarks * 2];

        // ---- Mark count against health (any max health: fractions only) ----
        // A standing pose: thighs hanging from the hips (their bones point along -Z, turned to point down), right leg first.
        var pose = new LegPose();
        for (var i = 0; i < 2; i++)
        {
            pose.ThighLocal[i] = Matrix4x4.CreateRotationX(-MathF.PI / 2) * Matrix4x4.CreateTranslation(i == 0 ? 0.15f : -0.15f, LegAnimator.StandPelvis, 0);
            pose.ShinLocal[i] = pose.FootLocal[i] = Matrix4x4.Identity;
        }
        BodyBloodRender.SoldierFrames(Matrix4x4.CreateTranslation(3, 0, 4), pose, 0, 0, out var frames);
        var blood = BloodMarks.AddHit(BloodMarks.AddHit(0, BloodPart.Front, 0), BloodPart.Head, 0);
        var counts = new List<int>();
        var subtle = new List<int>();
        for (var hp = 1f; hp >= -0.001f; hp -= 0.1f)
        {
            counts.Add(BodyBloodRender.Place(7, blood, hp, frames, BloodMode.Full, marks));
            subtle.Add(BodyBloodRender.Place(7, blood, hp, frames, BloodMode.Subtle, marks));
        }
        var tiers = Enumerable.Range(0, 11).Select(i => BloodMarks.Tier(1f - i * 0.1f)).ToList();
        Line($"marks at health 100..0% (steps of 10): Full {string.Join("/", counts)}, Subtle {string.Join("/", subtle)}; tiers {string.Join("/", tiers)}");
        for (var i = 1; i < counts.Count; i++)
            if (counts[i] < counts[i - 1] || tiers[i] < tiers[i - 1])
                Fail("body blood: fewer marks at lower health");
        if (counts[0] != 0 || counts[1] != 0 || counts[^1] != BloodMarks.MaxMarks || subtle[^1] != BloodMarks.MaxMarks / 2 || tiers[^1] != BloodMarks.MaxMarks)
            Fail("body blood: marks at full health, or not all of them at 0");
        if (BodyBloodRender.Place(7, blood, 0.1f, frames, BloodMode.Off, marks) != 0)
            Fail("body blood: Off still places marks");

        // ---- Marks sit on their part, near the surface ----
        var onPart = 0;
        var near = BloodMarks.AddHit(BloodMarks.AddHit(BloodMarks.AddHit(BloodMarks.AddHit(0, BloodPart.Head, 0), BloodPart.LegL, 12), BloodPart.ArmR, 4), BloodPart.Back, 8);
        var nm = BodyBloodRender.Place(9, near, 0.35f, frames, BloodMode.Full, marks);
        for (var i = 0; i < Math.Min(nm, 4); i++)
        {
            var c = new Vector3(marks[i * 2].X, marks[i * 2].Y, marks[i * 2].Z) - new Vector3(3, 0, 4);
            var ok = i switch
            {
                0 => c.Y > World.HeadStart - 0.05f, // head
                1 => c.Y < LegAnimator.StandPelvis + 0.05f && c.X < 0.05f, // left thigh (-X)
                2 => c.X > 0.3f && c.Y > 1f, // right side of the chest
                _ => c.Z > 0.2f, // the back (+Z behind a body facing -Z)
            };
            if (ok)
                onPart++;
            else
                Fail($"body blood: mark {i} ({BloodMarks.Part(near, i)}) at local ({c.X:0.00}, {c.Y:0.00}, {c.Z:0.00}) isn't on its part");
        }
        Line($"{onPart}/4 marks on the part that was hit (head, left leg, right arm, back)");

        // ---- The server records parts and directions; armour first; full health clears ----
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1);
        var human = game.Players[0];
        var bot = game.Players.Values.First(p => p.Id != 0);
        game.SelfTestPerks(human, PerkSet.Empty);
        game.SelfTestPerks(bot, PerkSet.Empty);
        game.SelfTestPlace(human, new Vector3(5, 0, 5), MathF.PI); // facing +Z, at the bot
        game.SelfTestPlace(bot, new Vector3(5, 0, 8), 0); // facing -Z, at the human
        var hit = World.MaxHealth * 3 / 20; // 15% a hit
        game.SelfTestBloodShot(human, bot, hit, new Vector3(5, 1.1f, 7.75f), 1); // chest, front
        game.SelfTestBloodShot(human, bot, hit, new Vector3(5, 1.62f, 7.8f), 1.1); // head
        game.SelfTestBloodShot(human, bot, hit, new Vector3(5.36f, 1.2f, 8f), 1.2); // right arm (+X)
        game.SelfTestBloodShot(human, bot, hit, new Vector3(4.85f, 0.5f, 8f), 1.3); // left leg
        var parts = Enumerable.Range(0, BloodMarks.Count(bot.Blood)).Select(i => BloodMarks.Part(bot.Blood, i)).ToArray();
        var sector = BloodMarks.Sector(bot.Blood);
        game.SelfTestDamage(bot, human, hit, DamageKind.Explosive, 1.4); // a blast from the bot, in front of the human
        var swipeSector = BloodMarks.Sector(human.Blood);
        Line($"server: 4 bullet hits recorded {string.Join(", ", parts)}, last from sector {sector} (shooter in front); human hurt from sector {swipeSector}; bot at {bot.Health}/{World.MaxHealth}");
        if (string.Join(",", parts) != "Front,Head,ArmR,LegL" || sector != 0 || swipeSector != 0)
            Fail("body blood: the server recorded the wrong parts or directions");

        var vestVictim = human;
        game.SelfTestPlace(vestVictim, new Vector3(5, 0, 5), MathF.PI);
        vestVictim.Blood = 0;
        vestVictim.Streak.Armor = KillStreaks.VestArmor;
        vestVictim.Streak.ArmorHeavy = false;
        game.SelfTestDamage(bot, vestVictim, 20, DamageKind.Bullet, 2);
        var soaked = vestVictim.Health == World.MaxHealth && BloodMarks.Count(vestVictim.Blood) == 0;
        vestVictim.Streak.Armor = 0;
        Line($"Ballistic Vest soaking a 20-damage hit: health {vestVictim.Health}, blood marks {BloodMarks.Count(vestVictim.Blood)}");
        if (!soaked)
            Fail("body blood: an armour-soaked hit must leave no blood");

        // ---- Two clients place the same marks from the same snapshot ----
        var c1Marks = new Vector4[marks.Length];
        var c2Marks = new Vector4[marks.Length];
        var hostN = BodyBloodRender.Place(bot.Id, bot.Blood, bot.Health / (float)World.MaxHealth, frames, BloodMode.Full, marks);
        var snaps = game.SelfTestSnapshotFor(human);
        int n1, n2;
        uint b1, b2;
        using (var c1 = new Game(NetPeer.Join("127.0.0.1", 26181), new Audio(), "A", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id))
        using (var c2 = new Game(NetPeer.Join("127.0.0.1", 26182), new Audio(), "B", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id))
        {
            c1.LocalId = 200;
            c2.LocalId = 201;
            foreach (var s in snaps)
            {
                c1.SelfTestReceive(s, 5);
                c2.SelfTestReceive(s, 5);
            }
            var p1 = c1.Players.GetValueOrDefault(bot.Id);
            var p2 = c2.Players.GetValueOrDefault(bot.Id);
            b1 = p1?.Blood ?? 0;
            b2 = p2?.Blood ?? 0;
            n1 = p1 == null ? -1 : BodyBloodRender.Place(p1.Id, p1.Blood, p1.Health / (float)World.MaxHealth, frames, BloodMode.Full, c1Marks);
            n2 = p2 == null ? -1 : BodyBloodRender.Place(p2.Id, p2.Blood, p2.Health / (float)World.MaxHealth, frames, BloodMode.Full, c2Marks);
        }
        var identical = n1 == hostN && n2 == hostN && c1Marks.AsSpan(0, n1 * 2).SequenceEqual(c2Marks.AsSpan(0, n2 * 2)) && c1Marks.AsSpan(0, n1 * 2).SequenceEqual(marks.AsSpan(0, hostN * 2));
        Line($"two clients from the server's snapshot ({snaps.Count} packet(s)): blood 0x{b1:X8} / 0x{b2:X8} (server 0x{bot.Blood:X8}), {n1} / {n2} marks (server {hostN}), identical {identical}");
        if (!identical || b1 != bot.Blood || b2 != bot.Blood || hostN == 0)
            Fail("body blood: clients don't place the same marks as the server");

        // Full health clears (regeneration, Stim), and the next set is laid out afresh.
        var gen = BloodMarks.Gen(bot.Blood);
        bot.Health = World.MaxHealth;
        game.SelfTestHealthTick(50);
        Line($"back to full health: {BloodMarks.Count(bot.Blood)} marks left, generation {gen} -> {BloodMarks.Gen(bot.Blood)}");
        if (BloodMarks.Count(bot.Blood) != 0 || BloodMarks.Gen(bot.Blood) == gen)
            Fail("body blood: full health must clear the marks and bump the generation");

        // ---- Packet round trip, protocol ----
        var trips = 0;
        var w = new PacketWriter();
        foreach (var v in new uint[] { 0, 0xFFFFFFFF, 0x9ABCDEF1, BloodMarks.AddHit(BloodMarks.Cleared(0), BloodPart.LegR, 15) })
        {
            w.Begin(Msg.State);
            Snapshot.WriteFast(w, new SnapPlayer { Id = 3, Health = 77, Alive = true, Blood = v, Weapon = 2 });
            var r = new PacketReader(w.Span[1..]);
            var got = Snapshot.ReadFast(ref r);
            if (got.Blood == v && got.Health == 77 && got.Weapon == 2 && w.Length - 1 == Snapshot.FastBytes)
                trips++;
        }
        Line($"blood word round trips {trips}/4 in a {Snapshot.FastBytes}-byte fast entry, protocol {ServerInfo.ProtocolVersion}");
        if (trips != 4 || ServerInfo.ProtocolVersion < 33)
            Fail("body blood: the blood word doesn't survive the snapshot, or the protocol wasn't bumped");

        // ---- Ragdolls keep the final marks ----
        var rag = new float[8];
        BodyBloodRender.RagdollAmounts(bot.Id, near, 0f, BloodMode.Full, rag);
        var ragOff = new float[8];
        BodyBloodRender.RagdollAmounts(bot.Id, near, 0f, BloodMode.Off, ragOff);
        Line($"ragdoll blood per part (head..right leg): {string.Join("/", rag.Skip(1).Select(x => x.ToString("0.00")))}; Off: {ragOff.Sum():0.00}");
        if (rag[(int)BloodPart.Head] <= 0 || rag[(int)BloodPart.LegL] <= 0 || rag[(int)BloodPart.ArmR] <= 0 || rag[(int)BloodPart.Back] <= 0 || ragOff.Sum() != 0)
            Fail("body blood: a ragdoll must keep blood on the parts that were hit (and none when Off)");

        // ---- CPU and allocations: 32 marked bodies a frame ----
        var bodies = new Player[32];
        for (var i = 0; i < bodies.Length; i++)
        {
            bodies[i] = new Player { Id = (byte)i, Health = World.MaxHealth * (i % 8) / 10, Alive = true, Blood = near ^ ((uint)i << 24) };
            bodies[i].Teleport(new Vector3(i, 0, 3));
        }
        var body = Matrix4x4.CreateTranslation(1, 0, 2);
        for (var i = 0; i < 3000; i++) // warm up (and let the JIT settle: its one-off tier-up can allocate a few bytes)
            BodyBloodRender.PlacePlayer(bodies[i & 31], pose, body, i % 3);
        BodyBloodRender.Mode = BloodMode.Full;
        var placed = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        const int frameCount = 500;
        for (var f = 0; f < frameCount; f++)
            foreach (var p in bodies)
            {
                BodyBloodRender.PlacePlayer(p, pose, body, f % 3);
                placed += BodyBloodRender.Count;
            }
        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated > 0) // the least of three more identical rounds: a one-off JIT tier-up allocation shows in one at most
            allocated = Math.Min(allocated, SteadyAllocBytes(() =>
            {
                for (var f = 0; f < frameCount; f++)
                    foreach (var p in bodies)
                        BodyBloodRender.PlacePlayer(p, pose, body, f % 3);
            }, warmups: 0));
        BodyBloodRender.Reset();
        var usPerFrame = sw.Elapsed.TotalMilliseconds * 1000 / frameCount;
        Line($"32 bodies: {placed / (double)frameCount:0} marks placed per frame in {usPerFrame:0.0} us (budget 100 us), {allocated} bytes allocated over {frameCount} frames; 0 extra draw calls (marks paint in the bodies' own draws)");
        if (usPerFrame > 100)
            Fail($"body blood: {usPerFrame:0} us per frame for 32 bodies (budget 100 us)");
        if (allocated > 256) // a per-frame allocation would be at least 500 x 24 bytes; a one-off JIT tier-up is a few dozen
            Fail("body blood: allocates per frame");

        // Off: nothing placed, nothing bound.
        BodyBloodRender.Mode = BloodMode.Off;
        BodyBloodRender.PlacePlayer(bodies[1], pose, body, 0);
        var offCount = BodyBloodRender.Count;
        BodyBloodRender.Mode = BloodMode.Full;
        var zombie = new Zombie { Id = 12, Health = 10, MaxHealth = 100 };
        BodyBloodRender.PlaceZombie(zombie, Matrix4x4.Identity, Matrix4x4.CreateTranslation(0, 0.86f, 0), 0.3f);
        var zMarks = BodyBloodRender.Count;
        BodyBloodRender.Reset();
        Line($"Off places {offCount} marks; a zombie at 10% gets {zMarks} marks (zombie palette)");
        if (offCount != 0 || zMarks == 0)
            Fail("body blood: Off must place nothing; hurt zombies must bleed too");
    }
}
