using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombie sounds" (Source/Audio/ZombieSounds.cs, SynthZombie.cs, Audio.Zombies.cs): the voices and
/// steps synthesise with real variety (the headshot screech brighter than a grunt, a death rattle longer, a groan
/// lower), sit on their mixer rows (on top of Zombies, 0 dB by default); each event plays its sound once (a footfall
/// per foot plant by speed class, the snarl on a new swipe, a grunt or a headshot screech on a flinch, a death rattle
/// or a cut-off screech on dying), nothing replays for a zombie first seen, nothing plays out of range; a 24-zombie
/// sprinting horde never has more than the capped voices busy and keeps to the groan budget, with no allocations; and
/// over the network a real head shot on the host gives the client the head bit (protocol 50), so its screech and the
/// headshot kill play there too. Headless (no audio device needed).
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombieSounds()
    {
        // ---- The sounds ----
        var kinds = (int)ZSound.COUNT;
        if ((int)Sfx.ZombieSprint - (int)Sfx.ZombieGroan + 1 != kinds || ZombieSounds.SfxOf(ZSound.Sprint) != Sfx.ZombieSprint || ZombieSounds.SfxOf(ZSound.HeadDeath) != Sfx.ZombieHeadDeath)
            Fail("zombie sounds: ZSound and the Sfx zombie block are out of step");
        var bright = new float[kinds];
        var length = new float[kinds];
        var variants = 0;
        for (var k = 0; k < kinds; k++)
        {
            var sfx = ZombieSounds.SfxOf((ZSound)k);
            var n = Synth.VariantCount(sfx);
            variants += n;
            if (n < 3)
                Fail($"zombie sounds: {sfx} has {n} variant(s) (want 3 or more, so a horde doesn't sound cloned)");
            for (var v = 0; v < n; v++)
            {
                var pcm = Synth.Effect(sfx, v);
                var f = Features(pcm);
                if (pcm.Length != (int)(Synth.ZombieSeconds(sfx) * Audio.SampleRate))
                    Fail($"zombie sounds: {sfx} #{v} is {pcm.Length} samples, not its ZombieSeconds buffer");
                bright[k] += f.Bright / n;
                length[k] += f.Length / n;
            }
            var bus = Audio.BusFor(sfx, true);
            if (bus != (Synth.IsZombieStep(sfx) ? MixBus.ZombieSteps : MixBus.ZombieVoices))
                Fail($"zombie sounds: {sfx} is on {bus}");
        }
        float B(ZSound k) => bright[(int)k];
        float L(ZSound k) => length[(int)k];
        Line($"sounds: {kinds} kinds, {variants} variants; brightness groan {B(ZSound.Groan):0.000}, growl {B(ZSound.Growl):0.000}, hurt {B(ZSound.Hurt):0.000}, headshot {B(ZSound.Headshot):0.000}; "
             + $"length hurt {L(ZSound.Hurt) * 1000:0} ms, attack {L(ZSound.Attack) * 1000:0} ms, death {L(ZSound.Death) * 1000:0} ms, groan {L(ZSound.Groan) * 1000:0} ms");
        if (!(B(ZSound.Headshot) > B(ZSound.Hurt) * 1.2f) || !(B(ZSound.Headshot) > B(ZSound.Groan) * 1.2f))
            Fail("zombie sounds: the headshot screech should be clearly brighter than a hurt grunt and a groan");
        if (!(L(ZSound.Death) > L(ZSound.Hurt) * 2f) || !(L(ZSound.Groan) > L(ZSound.Attack)) || !(L(ZSound.HeadDeath) < L(ZSound.Death)))
            Fail("zombie sounds: a death rattle should outlast a grunt, a groan a snarl, and a headshot kill be cut short");

        // ---- The mixer rows ----
        ref readonly var zv = ref AudioMixDefaults.Of(MixBus.ZombieVoices);
        ref readonly var zs = ref AudioMixDefaults.Of(MixBus.ZombieSteps);
        if (zv.Key != "zombies.voices" || zs.Key != "zombies.steps" || zv.Default != 0f || zs.Default != 0f || (int)MixBus.ZombieVoices != (int)MixBus.Zombies + 1
            || zv.Group != AudioMixDefaults.Of(MixBus.Zombies).Group)
            Fail($"zombie sounds: mixer rows {zv.Key} {zv.Default} dB, {zs.Key} {zs.Default} dB (want zombies.voices / zombies.steps at 0 dB beside Zombies)");
        var old = new AudioMix();
        var rep = old.LoadJson("""{"levels": {"zombies": {"db": -6, "mute": false, "default": 0}}}"""); // a save from before the rows
        if (!rep.Ok || old.Db(MixBus.Zombies) != -6f || old.Db(MixBus.ZombieVoices) != 0f || old.Db(MixBus.ZombieSteps) != 0f)
            Fail($"zombie sounds: an old audio_mix.json should keep its Zombies level and give the new rows 0 dB ({rep})");
        Line($"mixer: {zv.Key} and {zs.Key} at 0 dB on top of zombies; an old save keeps its zombies level: OK");

        // ---- Events, one zombie at a time ----
        var audio = new Audio(); // no device: the director's bookkeeping runs, nothing is heard
        var listener = new Vector3(0, 1.6f, 0);
        audio.SetListener(listener, -Vector3.UnitZ);
        const float dt = 1f / 60f;
        var horde = new List<Zombie>();
        Zombie Make(ushort id, Vector3 at, ZState s, ZSpeed sp) => new() { Id = id, State = s, Speed = sp, RenderPos = at, AnimMove = 1f, Health = 100, MaxHealth = 100 };
        float Speed(ZSpeed sp) => sp switch { ZSpeed.Sprint => 5.5f, ZSpeed.Run => 3.6f, _ => 1.3f };
        void Walk(Zombie z) => z.Gait += dt * Speed(z.Speed) * MathF.Tau / (4f * ZombieAnim.StrideHalf(z.Speed));
        var bad = new List<string>();
        var zs1 = new ZombieSounds();
        var z1 = Make(1, new Vector3(4, 0, 0), ZState.Chase, ZSpeed.Run);
        horde.Add(z1);
        zs1.Frame(audio, horde, dt); // first seen: nothing
        if (zs1.TotalPlayed() != 0)
            bad.Add($"a zombie first seen played {zs1.TotalPlayed()} sound(s)");
        // Footfalls: one per pi of the walk cycle, by speed class.
        foreach (var sp in new[] { ZSpeed.Walk, ZSpeed.Run, ZSpeed.Sprint })
        {
            z1.Speed = sp;
            zs1.ResetStats();
            var g0 = z1.Gait;
            for (var f = 0; f < 120; f++)
            {
                Walk(z1);
                zs1.Frame(audio, horde, dt);
            }
            var want = (int)(MathF.Floor((z1.Gait - MathF.PI / 2) / MathF.PI) - MathF.Floor((g0 - MathF.PI / 2) / MathF.PI));
            var k = ZombieSounds.StepOf(sp);
            if (zs1.Played[(int)k] != want || want < 3)
                bad.Add($"{sp}: {zs1.Played[(int)k]} {k} steps in 2 s (want {want}, one per foot plant)");
        }
        // Standing still (AnimMove 0) or tearing: no steps.
        zs1.ResetStats();
        z1.AnimMove = 0f;
        for (var f = 0; f < 60; f++)
        {
            Walk(z1);
            zs1.Frame(audio, horde, dt);
        }
        z1.AnimMove = 1f;
        if (zs1.Played[(int)ZSound.Sprint] != 0)
            bad.Add("a zombie standing still stepped");

        int Count(ZSound k) => zs1.Played[(int)k];
        void Settle(int frames)
        {
            for (var f = 0; f < frames; f++)
                zs1.Frame(audio, horde, dt);
        }
        z1.VoxNext = 1e9; // no idle groans in the event checks
        zs1.ResetStats();
        z1.AttackTick = 500;
        Settle(30);
        if (Count(ZSound.Attack) != 1)
            bad.Add($"a new swipe played {Count(ZSound.Attack)} snarl(s) (want 1)");
        z1.FlinchTick = 520;
        Settle(30);
        z1.HeadHitTick = 539;
        z1.FlinchTick = 540;
        Settle(30);
        if (Count(ZSound.Hurt) != 1 || Count(ZSound.Headshot) != 1)
            bad.Add($"a body hit and a head hit played {Count(ZSound.Hurt)} grunt(s) and {Count(ZSound.Headshot)} screech(es) (want 1 and 1)");
        // A burn ticking every tick: one grunt at a time.
        for (var f = 0; f < 60; f++)
        {
            z1.FlinchTick = 600 + f;
            zs1.Frame(audio, horde, dt);
        }
        if (Count(ZSound.Hurt) is < 3 or > 5)
            bad.Add($"a second of burn flinches gave {Count(ZSound.Hurt) - 1} grunts (want about 3)");
        z1.State = ZState.Dead;
        Settle(60);
        var z2 = Make(2, new Vector3(-4, 0, 2), ZState.Chase, ZSpeed.Walk);
        horde.Add(z2);
        Settle(1);
        z2.KilledByHead = true;
        z2.State = ZState.Dead;
        Settle(60);
        if (Count(ZSound.Death) != 1 || Count(ZSound.HeadDeath) != 1)
            bad.Add($"a body kill and a headshot kill played {Count(ZSound.Death)} death rattle(s) and {Count(ZSound.HeadDeath)} cut-off screech(es) (want 1 and 1)");
        // Out of range: nothing.
        horde.Clear();
        var far = Make(3, new Vector3(90, 0, 0), ZState.Chase, ZSpeed.Sprint);
        horde.Add(far);
        zs1.ResetStats();
        for (var f = 0; f < 600; f++)
        {
            Walk(far);
            if (f == 100)
                far.AttackTick = 900;
            zs1.Frame(audio, horde, dt);
        }
        if (zs1.TotalPlayed() != 0 || zs1.Culled[(int)ZSound.Sprint] == 0)
            bad.Add($"a zombie 90 m away played {zs1.TotalPlayed()} sound(s)");
        // Idle groans: a chasing zombie groans or growls every few seconds; one walking to its window less often.
        var idle = new int[2];
        for (var i = 0; i < 2; i++)
        {
            var solo = new ZombieSounds();
            var list = new List<Zombie> { Make((ushort)(4 + i), new Vector3(6, 0, 0), i == 0 ? ZState.Chase : ZState.ToWindow, ZSpeed.Run) };
            for (var f = 0; f < 60 * 60; f++) // a minute
                solo.Frame(audio, list, dt);
            idle[i] = solo.Played[(int)ZSound.Groan] + solo.Played[(int)ZSound.Growl];
        }
        Line($"events: steps per foot plant by speed, a snarl per swipe, grunt / screech / death / cut-off screech once each, burn flinches rate-limited, nothing first seen or 90 m away; "
             + $"idle in a minute: chasing {idle[0]}, walking to its window {idle[1]}");
        if (idle[0] is < 9 or > 22 || idle[1] is < 5 or > 13 || idle[0] <= idle[1])
            bad.Add($"in a minute a chasing zombie groaned {idle[0]} times (want 9-22) and a wandering one {idle[1]} (want 5-13, fewer)");
        if (bad.Count > 0)
            Fail("zombie sounds: " + string.Join("; ", bad));

        // ---- A 24-zombie sprinting horde: the caps hold, the groan budget holds, no allocations ----
        var zs2 = new ZombieSounds();
        horde.Clear();
        for (var i = 0; i < 24; i++)
        {
            var r = 1.5f + i * 1.2f;
            var a = i * 2.4f;
            horde.Add(Make((ushort)(100 + i), new Vector3(MathF.Cos(a) * r, 0, MathF.Sin(a) * r), ZState.Chase, i % 4 == 0 ? ZSpeed.Run : ZSpeed.Sprint));
        }
        var rng = new Random(7);
        int maxVox = 0, maxSteps = 0;
        var tick = 1000.0;
        void HordeFrame(int f)
        {
            tick += 0.5;
            for (var i = 0; i < horde.Count; i++)
            {
                var z = horde[i];
                Walk(z);
                if (z.State == ZState.Dead)
                    continue;
                var roll = rng.Next(1000);
                if (roll < 12)
                    z.AttackTick = tick;
                else if (roll < 20)
                {
                    if (roll < 15)
                        z.HeadHitTick = tick;
                    z.FlinchTick = tick;
                }
            }
            zs2.Frame(audio, horde, dt);
            maxVox = Math.Max(maxVox, zs2.Voices.Busy(zs2.Clock));
            maxSteps = Math.Max(maxSteps, zs2.Steps.Busy(zs2.Clock));
        }
        for (var f = 0; f < 120; f++)
            HordeFrame(f); // warm up (JIT)
        zs2.ResetStats();
        var stepsDropped0 = zs2.Steps.Dropped;
        var t0 = zs2.Clock;
        const int frames = 600;
        var sw = Stopwatch.StartNew();
        var alloc0 = GC.GetAllocatedBytesForCurrentThread();
        for (var f = 0; f < frames; f++)
            HordeFrame(f);
        var alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        sw.Stop();
        var seconds = zs2.Clock - t0;
        var us = sw.Elapsed.TotalMilliseconds * 1000 / frames;
        var groans = zs2.Played[(int)ZSound.Groan] + zs2.Played[(int)ZSound.Growl];
        var steps = zs2.Played[(int)ZSound.Run] + zs2.Played[(int)ZSound.Sprint];
        var events = zs2.Played[(int)ZSound.Attack] + zs2.Played[(int)ZSound.Hurt] + zs2.Played[(int)ZSound.Headshot];
        var dropped = 0;
        foreach (var d in zs2.Dropped)
            dropped += d;
        Line($"horde: 24 zombies sprinting 1.5-29 m away for {seconds:0} s: {steps} steps voiced ({zs2.Steps.Dropped - stepsDropped0} dropped by the {ZombieSounds.StepCap}-voice cap), {events} snarls / grunts / screeches, "
             + $"{groans} idle groans ({zs2.RateLimited} put off by the budget of {ZombieSounds.GroansPerSecond}/s), {dropped} sounds dropped in all; at most {maxVox}/{ZombieSounds.VoiceCap} vocal and {maxSteps}/{ZombieSounds.StepCap} step voices busy; "
             + $"{alloc} bytes allocated, {us:0.0} us a frame");
        if (maxVox > ZombieSounds.VoiceCap || maxSteps > ZombieSounds.StepCap)
            Fail($"zombie sounds: {maxVox} vocal / {maxSteps} step voices busy at once (caps {ZombieSounds.VoiceCap} / {ZombieSounds.StepCap})");
        if (zs2.Steps.Dropped == stepsDropped0 || steps < 200 || events < 20)
            Fail($"zombie sounds: the horde test isn't exercising the caps ({steps} steps, {events} events, none dropped)");
        if (groans > ZombieSounds.GroanBurst + ZombieSounds.GroansPerSecond * seconds + 1 || zs2.RateLimited == 0 || groans < 5)
            Fail($"zombie sounds: {groans} idle groans in {seconds:0} s (budget {ZombieSounds.GroansPerSecond}/s plus a burst of {ZombieSounds.GroanBurst}; {zs2.RateLimited} put off)");
        if (alloc > 0)
            Fail($"zombie sounds: the horde's sounds allocated {alloc} bytes in {frames} frames (want 0)");
        if (us > 300)
            Fail($"zombie sounds: {us:0} us a frame for 24 zombies (budget 300 us)");
        audio.Dispose();

        CheckZombieSoundsNetwork();
    }

    /// <summary>A real head shot and body shot on the host, a headshot kill: the client hears the screech, the grunt and the cut-off screech (protocol 50's head bit).</summary>
    private static void CheckZombieSoundsNetwork()
    {
        var now = 0.0;
        var zt = ZTestMap!;
        var zm = zt.Zombies!;
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var win = zm.Windows[0];
        var outward = Vector3.Normalize(new Vector3(win.Outside.X - win.Inside.X, 0, win.Outside.Z - win.Inside.Z));
        var lateral = new Vector3(-outward.Z, 0, outward.X);
        var z = g.SelfTestSpawnZombie(zm.Spawners.FindIndex(s => s.Window == 0), now);
        for (var i = 0; i < 60 * 20 && z.State != ZState.Tearing; i++)
        {
            now += World.TickDt;
            g.SelfTestZombiesTick(now);
        }
        ZTicks(g, ref now, 0.6);
        z.MaxHealth = z.Health = 2000; // survives the shots (and each one still moves the health byte)
        g.SelfTestAnimTick = g.ServerTick;
        z.RenderPos = z.Position;
        z.RenderYaw = z.Yaw;
        g.ZombieAnimInputFor(z, now, out var a);
        g.SelfTestAnimTick = null;
        ZombieAnim.Evaluate(a, out var pose);
        var head = Vector3.Transform(ZombieAnim.HeadCenter, pose.Head);
        var chest = Vector3.Transform(ZombieAnim.TorsoCenter, pose.Upper);

        var w = new PacketWriter();
        var w2 = new PacketWriter();
        var hostNow = now;
        using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
        World.SetMap(zt);
        void Sync()
        {
            g.WriteZEnts(w);
            g.WriteZWorld(w2);
            c.SelfTestReadZombies(w.Span.ToArray(), w2.Span.ToArray());
        }
        Sync();
        var cz = c.Horde.FirstOrDefault(x => x.Id == z.Id);
        if (cz == null)
        {
            Fail("zombie sounds network: the client never saw the zombie");
            return;
        }
        c.Audio.SetListener(cz.RenderPos + lateral * 4f + new Vector3(0, 1.6f, 0), -lateral);
        g.Audio.SetListener(z.RenderPos + lateral * 4f + new Vector3(0, 1.6f, 0), -lateral);
        c.ZSounds.Frame(c.Audio, c.Horde, 1f / 60f); // first seen
        g.ZSounds.Frame(g.Audio, g.Horde, 1f / 60f);
        cz.VoxNext = z.VoxNext = 1e9; // no idle groans in the way
        c.ZSounds.ResetStats();
        g.ZSounds.ResetStats();

        void Shoot(Vector3 from, Vector3 at)
        {
            me.Teleport(from - new Vector3(0, World.EyeHeight, 0));
            for (var i = 0; i < me.History.Length; i++)
                me.History[i] = new HistoryEntry { Position = me.Position, Yaw = me.Yaw, Tick = g.ServerTick };
            g.SelfTestServerShot(me, from, Vector3.Normalize(at - from), hostNow);
            g.ServerTick++;
            g.SelfTestZombieAnimTick(hostNow);
            Sync();
            for (var f = 0; f < 40; f++) // (past the one-grunt-at-a-time gap)
            {
                c.ZSounds.Frame(c.Audio, c.Horde, 1f / 60f);
                g.ZSounds.Frame(g.Audio, g.Horde, 1f / 60f);
            }
            g.ServerTick += 20; // the head bit is only fresh for 4 ticks
            g.SelfTestZombieAnimTick(hostNow);
            Sync();
        }
        var h0 = z.Health;
        Shoot(head + lateral * 3f, head);
        var headTick = z.HeadHitTick;
        var cHead = cz.HeadHitTick;
        var h1 = z.Health;
        Shoot(chest - lateral * 3f, chest);
        var h2 = z.Health;
        int C(Game x, ZSound k) => x.ZSounds.Played[(int)k];
        var line = $"host: screech {C(g, ZSound.Headshot)}, grunt {C(g, ZSound.Hurt)}; client: screech {C(c, ZSound.Headshot)}, grunt {C(c, ZSound.Hurt)} (head hit tick host {headTick}, client {cHead})";
        // A headshot kill.
        z.Health = 1;
        Shoot(head + lateral * 3f, head);
        line += $"; headshot kill: host {z.State} head {z.KilledByHead} cut-off screech {C(g, ZSound.HeadDeath)}, client {cz.State} head {cz.KilledByHead} cut-off screech {C(c, ZSound.HeadDeath)}, rattles {C(c, ZSound.Death)}";
        Line($"network (protocol {ServerInfo.ProtocolVersion}, {Game.ZEntBytes} bytes a zombie): head shot then body shot (health {h0} -> {h1} -> {h2}); " + line);
        if (h1 >= h0 || h2 >= h1)
            Fail("zombie sounds network: the test shots missed the zombie");
        else if (C(c, ZSound.Headshot) != 1 || C(c, ZSound.Hurt) != 1 || C(g, ZSound.Headshot) != 1 || C(g, ZSound.Hurt) != 1)
            Fail("zombie sounds network: a head shot should screech and a body shot grunt, on the host and on the client: " + line);
        if (z.State != ZState.Dead || !cz.KilledByHead || C(c, ZSound.HeadDeath) != 1 || C(g, ZSound.HeadDeath) != 1 || C(c, ZSound.Death) != 0)
            Fail("zombie sounds network: a headshot kill should play the cut-off screech on both (the client learns it from the head bit): " + line);
        if (ServerInfo.ProtocolVersion < 50 || Game.ZEntBytes != 12)
            Fail($"zombie sounds network: protocol {ServerInfo.ProtocolVersion} (want 50 or later for the head bit), {Game.ZEntBytes} bytes a zombie (want 12)");
    }
}
