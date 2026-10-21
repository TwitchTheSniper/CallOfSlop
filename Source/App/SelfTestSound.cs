using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Sound": every sound synthesises (non-silent, no NaN, no clipping) within its time budget; the
/// headshot crunch differs from a body hit and the hit, kill and accent sounds from each other; footstep surfaces come
/// out right on test geometry (concrete and metal boxes, a wooden prop, a metal mover, terrain grass, rock and snow);
/// footstep cadence follows the gait (walk vs sprint), nothing steps in the air, jumps and landings fire once; the
/// footstep voice cap holds with 32 sprinting players and keeps the nearest; map reverb classes; the effects and
/// footstep volume settings; and the OpenAL EFX canary when an audio device exists (skipped on CI without one).
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> SoundNotes = new();

    private static void CheckSound()
    {
        SoundNotes.Clear();
        CheckSoundSynthesis();
        CheckSoundSurfaces();
        CheckFootstepCadence();
        CheckFootstepVoiceCap();
        CheckSoundSpaces();
        CheckSoundSettings();
        CheckSoundDevice();
        // The summary as a GitHub Actions notice (the workflow prints selftest.log; see SelfTestSlopes.cs).
        Line("::notice title=Sound::" + string.Join("%0A", SoundNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
    }

    /// <summary>A log line that also goes into the section's CI notice.</summary>
    private static void SoundLine(string s)
    {
        Line(s);
        SoundNotes.Add(s);
    }

    // ---- Synthesis ----------------------------------------------------------------------------------

    private static void CheckSoundSynthesis()
    {
        Synth.NonFinite = 0;
        _ = Synth.Footstep(FootSurface.Concrete, false, 0); // JIT warm-up
        var sw = Stopwatch.StartNew();
        var set = Audio.SynthesiseAll();
        var ms = sw.Elapsed.TotalMilliseconds;
        SoundLine($"[audio] synthesised {set.Count} buffers ({set.Bytes / 1024} KB) in {ms:0} ms on {Environment.ProcessorCount} threads; "
            + $"{(int)Sfx.COUNT} effects, {set.Steps.Length} footsteps, {set.Far.Count(f => f != null)} distant gunshot layers");
        if (ms > 20000)
            Fail($"synthesis took {ms:0} ms (budget 20 s)");
        if (Synth.NonFinite != 0)
            Fail($"{Synth.NonFinite} NaN/infinite samples during synthesis");

        void Check(string name, short[]? pcm)
        {
            if (pcm == null || pcm.Length < 200)
            {
                Fail($"{name}: missing or too short ({pcm?.Length ?? 0} samples)");
                return;
            }
            double sum = 0;
            var clipped = 0;
            foreach (var s in pcm)
            {
                sum += (double)s * s;
                if (Math.Abs((int)s) >= 32760)
                    clipped++;
            }
            var rms = Math.Sqrt(sum / pcm.Length) / 32768.0;
            if (rms < 0.004)
                Fail($"{name}: nearly silent (rms {rms:0.0000})");
            if (clipped > 0)
                Fail($"{name}: {clipped} clipped samples");
        }
        for (var e = 0; e < (int)Sfx.COUNT; e++)
            for (var v = 0; v < set.Effects[e].Length; v++)
                Check($"{(Sfx)e} #{v}", set.Effects[e][v]);
        for (var s = 0; s < (int)FootSurface.Count; s++)
            for (var r = 0; r < 2; r++)
                for (var v = 0; v < Synth.StepVariants; v++)
                    Check($"footstep {(FootSurface)s} {(r == 1 ? "run" : "walk")} #{v}", set.Steps[Audio.StepIndex((FootSurface)s, r == 1, v)]);
        foreach (var w in Weapons.All)
        {
            if (w.Sound is SoundStyle.Gun or SoundStyle.Boom)
                Check($"{w.Name} distant", set.Far[w.Id]);
            else if (set.Far[w.Id] != null)
                Fail($"{w.Name}: a {w.Sound} weapon has a distant layer");
        }

        // Variations really differ (so repeats don't sound copied).
        for (var e = 0; e < (int)Sfx.COUNT; e++)
            for (var v = 1; v < set.Effects[e].Length; v++)
                if (set.Effects[e][v].AsSpan().SequenceEqual(set.Effects[e][0]))
                    Fail($"{(Sfx)e}: variant {v} is identical to variant 0");
        for (var s = 0; s < (int)FootSurface.Count; s++)
            if (set.Steps[Audio.StepIndex((FootSurface)s, false, 1)].AsSpan().SequenceEqual(set.Steps[Audio.StepIndex((FootSurface)s, false, 0)]))
                Fail($"footstep {(FootSurface)s}: variants are identical");

        // ---- The headshot crunch vs a body hit: more crunch tail, a brighter crack, longer ----
        var head = Features(set.Effects[(int)Sfx.Headshot][0]);
        var body = Features(set.Effects[(int)Sfx.HitBody][0]);
        var head3D = Features(set.Effects[(int)Sfx.Headshot3D][0]);
        var flesh3D = Features(set.Effects[(int)Sfx.FleshHit3D][0]);
        SoundLine($"headshot: tail {head.Tail:0.000}, brightness {head.Bright:0.000}, length {head.Length * 1000:0} ms; body hit: tail {body.Tail:0.000}, brightness {body.Bright:0.000}, length {body.Length * 1000:0} ms");
        SoundLine($"headshot 3D: tail {head3D.Tail:0.000}, length {head3D.Length * 1000:0} ms; flesh 3D: tail {flesh3D.Tail:0.000}, length {flesh3D.Length * 1000:0} ms");
        if (head.Tail < body.Tail * 1.5f + 0.02f || head.Length < body.Length * 1.3f)
            Fail("the headshot crunch doesn't have a clearly longer gritty tail than a body hit");
        if (MathF.Abs(head.Bright - body.Bright) < 0.1f * MathF.Max(head.Bright, body.Bright))
            Fail("the headshot and body hit have the same brightness (spectral balance)");
        if (head3D.Tail < flesh3D.Tail * 1.5f + 0.02f)
            Fail("in the world, a headshot doesn't sound different from a body hit");

        // ---- Hit, kill and accent sounds are told apart by their envelopes ----
        Sfx[] distinct = [Sfx.HitBody, Sfx.Headshot, Sfx.Kill, Sfx.KillHeadshot, Sfx.HitMetal, Sfx.HitTick];
        for (var a = 0; a < distinct.Length; a++)
            for (var b = a + 1; b < distinct.Length; b++)
            {
                var (sa, sb) = (set.Effects[(int)distinct[a]][0], set.Effects[(int)distinct[b]][0]);
                var c = EnvelopeCorrelation(sa, sb);
                var (fa, fb) = (Features(sa), Features(sb));
                var brighter = MathF.Max(fa.Bright, fb.Bright) / MathF.Max(MathF.Min(fa.Bright, fb.Bright), 1e-4f);
                var longer = MathF.Max(fa.Length, fb.Length) / MathF.Max(MathF.Min(fa.Length, fb.Length), 1e-4f);
                if (c > 0.97 && brighter < 1.25f && longer < 1.3f)
                    Fail($"{distinct[a]} and {distinct[b]} sound alike (envelope correlation {c:0.00}, brightness x{brighter:0.00}, length x{longer:0.00})");
            }

        // ---- Distance helpers ----
        if (Audio.FarMix(10) != 0 || Audio.FarMix(200) != 1 || !(Audio.FarMix(50) > 0.1f && Audio.FarMix(50) < 0.9f))
            Fail($"gunshot near/far crossfade: {Audio.FarMix(10)} at 10 m, {Audio.FarMix(50)} at 50 m, {Audio.FarMix(200)} at 200 m");
        if (Audio.DistanceHf(5) != 1 || !(Audio.DistanceHf(60) < 0.6f) || Audio.DistanceHf(500) < 0.1f)
            Fail($"distance low-pass: {Audio.DistanceHf(5)} at 5 m, {Audio.DistanceHf(60)} at 60 m, {Audio.DistanceHf(500)} at 500 m");
    }

    /// <summary>Energy after 60 ms over the total, mean |first difference| over mean |x| (brightness), time to -40 dB.</summary>
    private static (float Tail, float Bright, float Length) Features(short[] pcm)
    {
        double total = 0, tail = 0, diff = 0, abs = 0;
        var cut = Audio.SampleRate * 60 / 1000;
        var peak = 1;
        foreach (var s in pcm)
            peak = Math.Max(peak, Math.Abs((int)s));
        var last = 0;
        for (var i = 0; i < pcm.Length; i++)
        {
            var x = (double)pcm[i];
            total += x * x;
            if (i >= cut)
                tail += x * x;
            abs += Math.Abs(x);
            if (i > 0)
                diff += Math.Abs(x - pcm[i - 1]);
            if (Math.Abs((int)pcm[i]) > peak / 100)
                last = i;
        }
        return ((float)(tail / Math.Max(total, 1)), (float)(diff / Math.Max(abs, 1)), last / (float)Audio.SampleRate);
    }

    /// <summary>Correlation of two sounds' 5 ms RMS envelopes over their first 400 ms.</summary>
    private static double EnvelopeCorrelation(short[] a, short[] b)
    {
        const int bins = 80;
        var win = Audio.SampleRate / 200;
        double[] Env(short[] x)
        {
            var e = new double[bins];
            for (var k = 0; k < bins; k++)
            {
                double s = 0;
                for (var i = k * win; i < (k + 1) * win && i < x.Length; i++)
                    s += (double)x[i] * x[i];
                e[k] = Math.Sqrt(s / win);
            }
            return e;
        }
        var ea = Env(a);
        var eb = Env(b);
        double ma = ea.Average(), mb = eb.Average(), num = 0, da = 0, db = 0;
        for (var k = 0; k < bins; k++)
        {
            num += (ea[k] - ma) * (eb[k] - mb);
            da += (ea[k] - ma) * (ea[k] - ma);
            db += (eb[k] - mb) * (eb[k] - mb);
        }
        return num / Math.Sqrt(Math.Max(da * db, 1e-9));
    }

    // ---- Surfaces -----------------------------------------------------------------------------------

    private const string SoundTestMapJson = """
        {
          "name": "Sound Test", "halfX": 30, "halfZ": 30,
          "ops": [
            { "op": "Ground", "color": [0.5, 0.5, 0.47] },
            { "op": "Block", "x0": -20, "y0": 0, "z0": -20, "x1": -14, "y1": 2, "z1": -14, "color": [0.5, 0.5, 0.47] },
            { "op": "Block", "x0": 14, "y0": 0, "z0": -20, "x1": 20, "y1": 3, "z1": -14, "color": [0.3, 0.35, 0.45] },
            { "op": "Block", "x0": -20, "y0": 0, "z0": 14, "x1": -19, "y1": 1, "z1": 15, "color": [0.55, 0.4, 0.25] },
            { "op": "PlaceProp", "name": "Wooden Crate", "x": 10, "z": 10 },
            { "op": "Spawn", "x": 0, "z": 0 },
            { "op": "Spawn", "x": 5, "z": 5 }
          ]
        }
        """;

    private static void CheckSoundSurfaces()
    {
        var previous = World.Map;
        try
        {
            var map = MapFile.Load(SoundTestMapJson, "sound test");
            World.SetMap(map);
            void Expect(string what, Vector3 foot, FootSurface want)
            {
                var got = SurfaceProbe.Under(foot);
                SoundLine($"surface: {what} at {foot.X:0.0},{foot.Y:0.0},{foot.Z:0.0} -> {got}");
                if (got != want)
                    Fail($"footstep surface on {what}: {got} (want {want})");
            }
            Expect("the concrete floor", new Vector3(0, 0, -5), FootSurface.Concrete);
            Expect("a concrete block", new Vector3(-17, 2, -17), FootSurface.Concrete);
            Expect("a metal container", new Vector3(17, 3, -17), FootSurface.Metal);
            Expect("a wooden box", new Vector3(-19.5f, 1, 14.5f), FootSurface.Wood);
            if (map.PropInstances.Count == 1)
            {
                var crate = map.PropInstances[0];
                Expect("a wooden crate prop", new Vector3(crate.Position.X, crate.Bounds.Max.Y, crate.Position.Z), FootSurface.Wood);
            }
            else
                Fail($"the sound test map has {map.PropInstances.Count} props (want the one crate)");
            if (Props.Find("Oil Drum") is { } drum && SurfaceProbe.OfProp(drum) != FootSurface.Metal)
                Fail($"Oil Drum prop reads as {SurfaceProbe.OfProp(drum)} (want metal)");
            Expect("the edge of a jump (nothing below)", new Vector3(0, 6, 0), FootSurface.Concrete);

            // A mover: always metal.
            if (Maps.Find(PhysicsTestMap) is { } phys && phys.Movers.Count > 0)
            {
                World.SetMap(phys);
                var boxes = MoverPhysics.At(phys, MoverPhysics.Clock);
                var best = 0;
                for (var i = 1; i < boxes.Length; i++)
                    if (boxes[i].Half.X * boxes[i].Half.Z > boxes[best].Half.X * boxes[best].Half.Z)
                        best = i;
                var b = boxes[best];
                Expect($"mover {phys.Movers[b.Mover].Name}", new Vector3(b.Center.X, b.Top, b.Center.Z), FootSurface.Metal);
            }
            else
                Fail("no Physics Test map with movers for the mover surface check");

            // Terrain: grass and rock on a High Ground map, snow on the alpine one.
            TerrainSurfaces("Ridgeline", TerrainMaterial.Grass, FootSurface.Grass, steep: false);
            TerrainSurfaces("Ridgeline", TerrainMaterial.Rock, FootSurface.Rock, steep: true);
            TerrainSurfaces("Alpine Pass", TerrainMaterial.Sand, FootSurface.Snow, steep: false);
        }
        finally
        {
            World.SetMap(previous);
        }
    }

    /// <summary>Probes up to 24 samples of one terrain material (flat or steep): at least 80% must come out as <paramref name="want"/>.</summary>
    private static void TerrainSurfaces(string mapName, TerrainMaterial mat, FootSurface want, bool steep)
    {
        var map = Maps.Find(mapName);
        if (map?.Terrain is not { } t)
        {
            Fail($"terrain map '{mapName}' not found for the footstep surface check");
            return;
        }
        World.SetMap(map);
        int tried = 0, right = 0, direct = 0;
        var rule = steep ? (float)Math.Cos((t.Surface.RockSlope + 10) * Math.PI / 180) : 0.985f;
        for (var j = 2; j < t.CountZ - 2 && tried < 24; j += 3)
            for (var i = 2; i < t.CountX - 2 && tried < 24; i += 5)
            {
                var n = t.VertexNormal(i, j);
                var ok = steep ? n.Y < rule : n.Y > rule && t.MaterialIds[j * t.CountX + i] == (byte)mat && t.VertexColor(i, j).Rock < 0.01f;
                if (!ok)
                    continue;
                var x = t.OriginX + i * t.Cell;
                var z = t.OriginZ + j * t.Cell;
                var foot = new Vector3(x, t.Height(i, j), z);
                tried++;
                if (SurfaceProbe.OfTerrain(t, x, z) == want)
                    direct++;
                if (SurfaceProbe.Under(foot) == want)
                    right++;
            }
        SoundSoundLine($"surface: {mapName} {(steep ? "steep" : "flat")} {mat}: {right}/{tried} probes and {direct}/{tried} samples read as {want}");
        if (tried < 5)
            Fail($"{mapName}: only {tried} {mat} samples to probe");
        else if (direct < tried || right < tried * 0.8f)
            Fail($"{mapName}: {mat} ground read as {want} by {direct}/{tried} samples and {right}/{tried} probes");
    }

    // ---- Cadence ------------------------------------------------------------------------------------

    private static List<StepEvent> RunSteps(Vector3 velocity, Stance stance, float seconds, bool grounded = true, Func<double, float?>? legPhase = null)
    {
        var p = new Player { Id = 201, Name = "Steps" };
        p.Teleport(new Vector3(-25, 0, 0));
        p.RenderStance = stance;
        p.RenderVelocity = velocity;
        p.RenderGrounded = grounded;
        var steps = new Footsteps();
        var events = new List<StepEvent>();
        const double dt = 1.0 / 60;
        for (var t = 0.0; t <= seconds + 1e-9; t += dt)
        {
            steps.Update(p, 100 + t, events.Add, legPhase?.Invoke(t));
            p.RenderPosition += new Vector3(velocity.X, 0, velocity.Z) * (float)dt;
        }
        return events;
    }

    private static void CheckFootstepCadence()
    {
        var previous = World.Map;
        try
        {
            World.SetMap(MapFile.Load(SoundTestMapJson, "sound test"));
            const float secs = 4f;
            foreach (var (name, speed, stance, kind, cadence) in new[]
                     {
                         ("walk", World.WalkSpeed, Stance.Stand, StepKind.Walk, LegAnimator.Cadence(World.WalkSpeed)),
                         ("sprint", World.SprintSpeed, Stance.Stand, StepKind.Run, LegAnimator.Cadence(World.SprintSpeed)),
                         ("crouch-walk", 3f, Stance.Crouch, StepKind.Crouch, LegAnimator.CrouchCadence(3f)),
                         ("prone crawl", 1.2f, Stance.Prone, StepKind.Crawl, 1.2f / 0.7f),
                     })
            {
                var ev = RunSteps(new Vector3(speed, 0, 0), stance, secs);
                var want = 2 * cadence * secs;
                var rate = ev.Count / secs;
                SoundLine($"footsteps {name} at {speed:0.0} m/s: {ev.Count} in {secs:0} s ({rate:0.0}/s, gait wants {2 * cadence:0.0}/s), kinds {string.Join(",", ev.Select(e => e.Kind).Distinct())}, surface {ev.FirstOrDefault().Surface}");
                if (MathF.Abs(ev.Count - want) > 2.5f)
                    Fail($"{name}: {ev.Count} steps in {secs} s, the gait wants {want:0.0}");
                if (ev.Any(e => e.Kind != kind))
                    Fail($"{name}: steps of the wrong kind ({string.Join(",", ev.Select(e => e.Kind).Distinct())}, want {kind})");
                if (ev.Count > 0 && ev.Any(e => e.Surface != FootSurface.Concrete))
                    Fail($"{name}: steps on the concrete floor read as {ev.First(e => e.Surface != FootSurface.Concrete).Surface}");
            }
            var walk = RunSteps(new Vector3(World.WalkSpeed, 0, 0), Stance.Stand, secs).Count;
            var run = RunSteps(new Vector3(World.SprintSpeed, 0, 0), Stance.Stand, secs).Count;
            if (run <= walk)
                Fail($"sprinting ({run} steps) isn't faster than walking ({walk})");
            var (wg, wr) = Audio.StepLoudness(StepKind.Walk, 1);
            var (rg, rr) = Audio.StepLoudness(StepKind.Run, 1.5f);
            var (cg, cr) = Audio.StepLoudness(StepKind.Crouch, 0.5f);
            if (!(rg > wg && wg > cg * 3 && rr > wr && wr > cr))
                Fail($"step loudness: run {rg}/{rr} m, walk {wg}/{wr} m, crouch {cg}/{cr} m should get quieter and shorter-ranged");

            // Phase-locked to the drawn legs: the same count, and the plants land on the legs' phase.
            var lockedPhase = 0.3f;
            var c6 = LegAnimator.Cadence(World.WalkSpeed);
            var locked = RunSteps(new Vector3(World.WalkSpeed, 0, 0), Stance.Stand, secs, legPhase: t => (float)((lockedPhase + c6 * t) % 1.0));
            if (MathF.Abs(locked.Count - 2 * c6 * secs) > 2.5f)
                Fail($"phase-locked walk: {locked.Count} steps (want {2 * c6 * secs:0.0})");

            // Nothing in the air; standing still is silent.
            var air = RunSteps(new Vector3(World.SprintSpeed, -2f, 0), Stance.Stand, 1f, grounded: false);
            var still = RunSteps(new Vector3(0.3f, 0, 0), Stance.Stand, 2f);
            if (air.Count != 0 || still.Count != 0)
                Fail($"{air.Count} steps while airborne and {still.Count} while (nearly) standing still");

            // A jump pushes off once; a fall lands once, harder from higher.
            var p = new Player { Id = 202, Name = "Jumper" };
            p.Teleport(new Vector3(0, 0, -5));
            var fs = new Footsteps();
            var evs = new List<StepEvent>();
            var now = 50.0;
            void Frame(Vector3 v, bool g)
            {
                p.RenderVelocity = v;
                p.RenderGrounded = g;
                fs.Update(p, now, evs.Add);
                now += 1.0 / 60;
            }
            for (var i = 0; i < 10; i++)
                Frame(Vector3.Zero, true);
            Frame(new Vector3(0, World.JumpSpeed, 0), false);
            for (var i = 0; i < 40; i++)
                Frame(new Vector3(0, World.JumpSpeed - World.Gravity * i / 60f, 0), false);
            Frame(Vector3.Zero, true);
            for (var i = 0; i < 10; i++)
                Frame(Vector3.Zero, true);
            var jumps = evs.Count(e => e.Kind == StepKind.Jump);
            var lands = evs.Where(e => e.Kind == StepKind.Land).ToList();
            SoundLine($"jump: {jumps} push-off, {lands.Count} landing (strength {lands.FirstOrDefault().Strength:0.00})");
            if (jumps != 1 || lands.Count != 1)
                Fail($"a jump made {jumps} push-offs and {lands.Count} landings (want 1 and 1)");
            evs.Clear();
            for (var i = 0; i < 70; i++)
                Frame(new Vector3(0, -World.Gravity * i / 60f, 0), false); // a long fall
            Frame(Vector3.Zero, true);
            var hard = evs.Where(e => e.Kind == StepKind.Land).ToList();
            if (hard.Count != 1 || lands.Count != 1 || hard[0].Strength <= lands[0].Strength)
                Fail($"a long fall landed {hard.Count} times, strength {hard.FirstOrDefault().Strength:0.00} (want 1, harder than the jump's {lands.FirstOrDefault().Strength:0.00})");
            if (evs.Any(e => e.Kind == StepKind.Jump))
                Fail("walking off a ledge counted as a jump");
        }
        finally
        {
            World.SetMap(previous);
        }
    }

    // ---- Voice cap ----------------------------------------------------------------------------------

    private static void CheckFootstepVoiceCap()
    {
        var previous = World.Map;
        try
        {
            World.SetMap(MapFile.Load(SoundTestMapJson, "sound test"));
            var listener = new Vector3(0, 1.6f, 0);
            var pool = new FootstepVoicePool(Audio.StepVoices);
            var fs = new Footsteps();
            var players = new List<Player>();
            for (var i = 0; i < 32; i++)
            {
                var r = 2f + i * 1.3f; // 2 m to 42 m away, all inside a sprint's range
                var a = i * 2.4f;
                var p = new Player { Id = (byte)(10 + i), Name = $"Runner {i}" };
                p.Teleport(new Vector3(MathF.Cos(a) * r, 0, MathF.Sin(a) * r));
                p.RenderVelocity = new Vector3(-MathF.Sin(a), 0, MathF.Cos(a)) * World.SprintSpeed; // round in circles
                players.Add(p);
            }
            int steps = 0, played = 0, maxBusy = 0, nearDropped = 0;
            var now = 10.0;
            var sw = Stopwatch.StartNew();
            for (var f = 0; f < 60 * 5; f++)
            {
                foreach (var p in players)
                {
                    fs.Update(p, now, e =>
                    {
                        steps++;
                        var d = Vector3.Distance(e.Position, listener);
                        var (_, range) = Audio.StepLoudness(e.Kind, e.Strength);
                        if (d > range)
                            return;
                        if (pool.Acquire(now, d, 0.45f) >= 0)
                            played++;
                        else if (d < 3.5f)
                            nearDropped++;
                    });
                    var r = new Vector2(p.RenderPosition.X, p.RenderPosition.Z).Length();
                    var a = MathF.Atan2(p.RenderPosition.Z, p.RenderPosition.X) + World.SprintSpeed / r / 60f;
                    p.RenderPosition = new Vector3(MathF.Cos(a) * r, 0, MathF.Sin(a) * r);
                    p.RenderVelocity = new Vector3(-MathF.Sin(a), 0, MathF.Cos(a)) * World.SprintSpeed;
                }
                maxBusy = Math.Max(maxBusy, pool.Busy(now));
                now += 1.0 / 60;
            }
            var us = sw.Elapsed.TotalMilliseconds * 1000 / (60 * 5);
            SoundLine($"[audio] 32 sprinting players for 5 s: {steps} steps, {played} voiced, {pool.Dropped} dropped by the cap, at most {maxBusy}/{Audio.StepVoices} voices busy; "
                + $"{nearDropped} steps within 3.5 m dropped; footstep logic {us:0.0} us per frame (with surface rays)");
            if (maxBusy > Audio.StepVoices)
                Fail($"{maxBusy} footstep voices busy at once (cap {Audio.StepVoices})");
            if (pool.Dropped == 0)
                Fail("32 sprinting players never hit the footstep voice cap (the test isn't testing it)");
            if (nearDropped > 0)
                Fail($"{nearDropped} steps within 3.5 m were dropped (the nearest should always win a voice)");
            if (us > 2000)
                Fail($"footsteps cost {us:0} us per frame for 32 players (budget 2 ms)");
        }
        finally
        {
            World.SetMap(previous);
        }
    }

    // ---- Spaces, settings, device -------------------------------------------------------------------

    private static void CheckSoundSpaces()
    {
        var counts = new Dictionary<AudioSpace, List<string>>();
        foreach (var m in Maps.All)
        {
            var s = Audio.Classify(m);
            (counts.TryGetValue(s, out var l) ? l : counts[s] = new List<string>()).Add(m.Name);
            if (m.HasTerrain && s != AudioSpace.Open)
                Fail($"{m.Name}: a terrain map has a {s} reverb (want Open)");
        }
        foreach (var (s, names) in counts.OrderBy(k => k.Key))
            SoundLine($"reverb {s}: {names.Count} maps ({string.Join(", ", names.Take(8))}{(names.Count > 8 ? ", ..." : "")})");
        if (!counts.ContainsKey(AudioSpace.Urban) && !counts.ContainsKey(AudioSpace.Indoor))
            Fail("every map got the same reverb");
    }

    private static void CheckSoundSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"callofslop-selftest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.txt");
            File.WriteAllLines(path, ["name=Old Timer", "volume=0.6", "music_volume=0.4"]); // from before the effects sliders
            var s = Settings.Load(path);
            if (s.EffectsVolume != 1f || s.FootstepVolume != 1f || Math.Abs(s.MusicVolume - 0.4f) > 1e-6f || Math.Abs(s.Volume - 0.6f) > 1e-6f)
                Fail($"old settings file: effects {s.EffectsVolume}, footsteps {s.FootstepVolume} (want 1 and 1), music {s.MusicVolume}, volume {s.Volume}");
            File.WriteAllLines(path, ["effects_volume=abc", "footstep_volume=7"]);
            s = Settings.Load(path);
            if (s.EffectsVolume != 1f || s.FootstepVolume != 1f)
                Fail($"bad volumes loaded as effects {s.EffectsVolume}, footsteps {s.FootstepVolume} (want 1 and clamped 1)");
            s.EffectsVolume = 0.7f;
            s.FootstepVolume = 0.35f;
            s.Save(path);
            var back = Settings.Load(path);
            if (Math.Abs(back.EffectsVolume - 0.7f) > 1e-6f || Math.Abs(back.FootstepVolume - 0.35f) > 1e-6f)
                Fail($"effects/footstep volumes didn't survive a save: {back.EffectsVolume}, {back.FootstepVolume}");
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // a leftover temp folder is harmless
            }
        }
    }

    /// <summary>With an audio device: the real start-up (synthesis, upload, EFX canary) and a few sounds. Skipped without one.</summary>
    private static void CheckSoundDevice()
    {
        var audio = new Audio();
        try
        {
            audio.Init(music: false);
            if (!audio.Enabled)
            {
                SoundLine("no audio device: OpenAL start-up and the EFX canary skipped");
                return;
            }
            SoundLine($"[audio] device up: {audio.BufferCount} buffers in {audio.SynthMs:0} ms; EFX {audio.EfxStatus}");
            audio.SetListener(Vector3.Zero, -Vector3.UnitZ);
            audio.SetEnvironment(Maps.All[0]);
            audio.Effect(Sfx.Headshot, 0.01f);
            audio.Shot(Weapons.DefaultPrimary, new Vector3(0, 0, -60));
            audio.Footstep(new StepEvent(1, StepKind.Run, FootSurface.Metal, new Vector3(3, 0, 0), 1f), self: false);
            if (audio.EfxStatus.StartsWith("off (failed while playing", StringComparison.Ordinal))
                Fail($"EFX broke while playing: {audio.EfxStatus}");
        }
        finally
        {
            audio.Dispose();
        }
    }
}
