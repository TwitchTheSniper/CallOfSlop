namespace CallOfSlop;

/// <summary>
/// Self-test section "Voice boost" (VoiceBoost in Source/Audio/Voice/VoiceDsp.cs, Audio.Voice.cs, Settings.cs): voice
/// volume up to 300%. How a volume splits between the samples and the OpenAL source at 0 / 50 / 100 / 300%, the boost on
/// a quiet voice (the full 3x at once), the limiter on a loud one (no clipping, peaks under the ceiling, recovery after),
/// 100% leaving the samples bit for bit alone, the soft knee, the settings round trip (global and per-player volumes and
/// mutes, old files, out-of-range values) and no allocations on the boost path. Synthetic frames only; no device.
/// </summary>
public static partial class SelfTest
{
    private static void CheckVoiceBoost()
    {
        const int n = VoiceCodec.FrameSamples;
        var pcm = new short[n];

        // How a volume splits: the product is always the volume asked for, the source never above 1.
        var splitOk = true;
        foreach (var v in (ReadOnlySpan<float>)[0f, 0.5f, 1f, 2f, 3f])
        {
            var (samples, source) = VoiceBoost.Split(v);
            splitOk &= MathF.Abs(samples * source - v) < 1e-5f && source <= 1f && samples >= 1f;
        }
        var (s0, src0) = VoiceBoost.Split(0f);
        var (s3, src3) = VoiceBoost.Split(3f);
        Line($"split: 0% -> samples x{s0}, source {src0}; 300% -> samples x{s3}, source {src3}; products right: {splitOk}");
        if (!splitOk || src0 != 0f || s3 != 3f || src3 != 1f)
            Fail("voice volume doesn't split into a sample boost (above 1) and a source gain (1 or less)");

        // 100%: the samples are untouched.
        Tone(pcm, 0.95f, 0);
        var copy = (short[])pcm.Clone();
        var state = 0.5f;
        VoiceBoost.Apply(pcm, 1f, ref state);
        var same = pcm.AsSpan().SequenceEqual(copy) && state == 1f;
        VoiceBoost.Apply(pcm, 0f, ref state);
        same &= pcm.AsSpan().SequenceEqual(copy);
        Line($"100% and 0%: samples unchanged {same}");
        if (!same)
            Fail("a voice volume of 100% or less changed the samples (it belongs on the OpenAL gain)");

        // 300% on a quiet voice: three times as loud straight away, no limiting.
        state = 1f;
        Tone(pcm, 0.1f, 0);
        VoiceBoost.Level(pcm, out var quietIn, out _);
        VoiceBoost.Apply(pcm, 3f, ref state);
        VoiceBoost.Level(pcm, out var quietOut, out _);
        var ratio = quietOut / quietIn;
        Line($"300% on a quiet voice (peak {quietIn:0.000}): x{ratio:0.000} in the first frame");
        if (MathF.Abs(ratio - 3f) > 0.02f)
            Fail($"a 300% boost on a quiet voice gave x{ratio:0.000}, expected x3");

        // 300% on a loud voice: limited, never clipped; then quiet again, the boost comes back.
        var worst = 0f;
        var clipped = 0;
        for (var f = 0; f < 25; f++)
        {
            Tone(pcm, 0.6f, f * n);
            VoiceBoost.Apply(pcm, 3f, ref state);
            VoiceBoost.Level(pcm, out var pk, out _);
            worst = MathF.Max(worst, pk);
            foreach (var x in pcm)
                clipped += x > VoiceBoost.MaxSample || x < -VoiceBoost.MaxSample ? 1 : 0;
        }
        var limited = state;
        var recovered = 0;
        for (var f = 0; f < 60 && recovered == 0; f++)
        {
            Tone(pcm, 0.1f, f * n);
            VoiceBoost.Apply(pcm, 3f, ref state);
            if (state > 0.95f)
                recovered = f + 1;
        }
        Line($"300% on a loud voice (peak 0.6): output peak {worst:0.000}, clipped samples {clipped}, limiter at {limited:0.00} of the boost; back to 95% after {recovered} quiet frames");
        if (worst > VoiceBoost.Ceiling + 0.01f || clipped > 0)
            Fail($"the voice limiter let a loud voice through at peak {worst:0.000} ({clipped} clipped samples)");
        if (recovered == 0 || recovered > 40)
            Fail($"the voice limiter didn't let the boost back after the loud part ({recovered} frames)");

        // A sudden shout after quiet talk: caught in its first frame.
        state = 1f;
        Tone(pcm, 0.05f, 0);
        VoiceBoost.Apply(pcm, 3f, ref state);
        Tone(pcm, 0.7f, n);
        VoiceBoost.Apply(pcm, 3f, ref state);
        VoiceBoost.Level(pcm, out var shout, out _);
        Tone(pcm, 1f, 2 * n);
        copy = (short[])pcm.Clone();
        VoiceBoost.Apply(pcm, 3f, ref state);
        var fullAsIs = pcm.AsSpan().SequenceEqual(copy);
        Line($"a sudden shout (peak 0.7) at 300%: first frame peak {shout:0.000}; a full-scale one plays as at 100%: {fullAsIs}");
        if (shout > VoiceBoost.Ceiling + 0.01f)
            Fail($"a sudden shout passed the limiter (peak {shout:0.000})");
        if (!fullAsIs)
            Fail("a voice already at full scale changed under a boost (it should play as at 100%, never quieter)");

        // The soft knee: straight below it, rising and under full scale above it.
        var kneeOk = VoiceBoost.SoftClip(1000f) == 1000 && VoiceBoost.SoftClip(-1000f) == -1000;
        short last = 0;
        for (var x = 0f; x <= 32768f * 9f; x += 97f)
        {
            var y = VoiceBoost.SoftClip(x);
            kneeOk &= y >= last && y <= VoiceBoost.MaxSample && VoiceBoost.SoftClip(-x) == -y;
            last = y;
        }
        Line($"soft knee: linear below {VoiceBoost.Knee:0.0}, monotonic and under full scale up to 9x: {kneeOk}");
        if (!kneeOk)
            Fail("the voice soft knee isn't linear below the knee, monotonic, symmetric and under full scale");

        // No allocations on the boost path.
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var f = 0; f < 1000; f++)
        {
            Tone(pcm, (f & 1) == 0 ? 0.9f : 0.1f, f * n);
            VoiceBoost.Apply(pcm, 2.5f, ref state);
            VoiceBoost.Level(pcm, out _, out _);
        }
        var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        Line($"boost path: {alloc} bytes allocated over 1000 frames");
        if (alloc > 0)
            Fail($"the voice boost allocates ({alloc} bytes over 1000 frames)");

        CheckVoiceBoostSettings();
    }

    /// <summary>A 440 Hz tone at <paramref name="amp"/> of full scale, from sample <paramref name="start"/>.</summary>
    private static void Tone(short[] pcm, float amp, int start)
    {
        for (var i = 0; i < pcm.Length; i++)
            pcm[i] = (short)(amp * 32767f * MathF.Sin(2f * MathF.PI * 440f * (start + i) / VoiceCodec.SampleRate));
    }

    private static void CheckVoiceBoostSettings()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "callofslop-voiceboost-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var fresh = new Settings();
            File.WriteAllLines(tmp, ["name=Old", "voice_volume=0.7"]);
            var old = Settings.Load(tmp);
            File.WriteAllLines(tmp, ["voice_volume=7", "voice_player=12:0:Loud", "voice_player=nope:1:Muted Only", "voice_player=1:0:"]);
            var wild = Settings.Load(tmp);
            var s = new Settings { VoiceVolume = 2.5f };
            s.SetVoicePlayerVolume("Quiet Guy", 2.75f);
            s.SetVoicePlayerVolume("Rude:One", 0.3f);
            s.SetVoicePlayerVolume("Normal", 1f); // 100%: not stored
            s.VoicePlayerMuted.Add("Rude:One");
            s.VoicePlayerMuted.Add("Spammer");
            s.Save(tmp);
            var back = Settings.Load(tmp);
            var same = MathF.Abs(back.VoiceVolume - 2.5f) < 1e-4f && MathF.Abs(back.VoicePlayerGain("Quiet Guy") - 2.75f) < 1e-4f
                       && MathF.Abs(back.VoicePlayerGain("Rude:One") - 0.3f) < 1e-4f && back.VoicePlayerGain("Normal") == 1f
                       && back.VoicePlayerVolume.Count == 2 && back.VoicePlayerMuted.SetEquals(["Rude:One", "Spammer"]);
            Line($"settings: default {fresh.VoiceVolume * 100:0}%, an old file's 0.7 -> {old.VoiceVolume * 100:0}%, 7 -> {wild.VoiceVolume * 100:0}%, a player at 12 -> {wild.VoicePlayerGain("Loud") * 100:0}%; "
                 + $"300% range, per-player volumes and mutes saved and loaded back the same: {same}");
            if (fresh.VoiceVolume != 1f || MathF.Abs(old.VoiceVolume - 0.7f) > 1e-4f)
                Fail("voice volume: the default must stay 100% and an old file must load as it was");
            if (wild.VoiceVolume != VoiceBoost.Max || wild.VoicePlayerGain("Loud") != VoiceBoost.Max || !wild.VoicePlayerMuted.Contains("Muted Only")
                || wild.VoicePlayerVolume.Count != 1)
                Fail("voice volumes past 300% must load clamped, and a bad volume or empty name skipped");
            if (!same)
                Fail("voice volumes (global, per player) or voice mutes don't survive a save and load");
        }
        finally
        {
            try
            {
                File.Delete(tmp);
                File.Delete(tmp + ".bak");
            }
            catch (Exception)
            {
                // a temp file
            }
        }
    }
}
