using Brutal.Numerics;
using Brutal.OpenAlApi;

namespace CallOfSlop;

/// <summary>
/// The cold storm's wind (<see cref="ColdStorm"/>, <see cref="Synth.Wind"/>): one looping stereo source of its own,
/// following the listener, on the Weather row of the mixer under the Effects volume. Louder as the storm and its gusts
/// grow; under a roof it drops and goes dull (the EFX low-pass), as through walls. Made the first time a storm is
/// heard (the loop is made off the main thread), set every frame, stopped when the storm is gone. Nothing without a device.
/// </summary>
public sealed partial class Audio
{
    private AlSource _stormSource;
    private AlBuffer _stormBuffer;
    private Task<short[]>? _stormPcm;
    private bool _stormPlaying, _stormBroken;

    /// <summary>
    /// The wind for this frame: <paramref name="level"/> how hard it blows (0 stops it), <paramref name="outside"/> how much
    /// you are out in it (0 under a roof), <paramref name="gust"/> the gusts on top.
    /// </summary>
    public void StormWind(float level, float outside, float gust)
    {
        if (!Enabled || _stormBroken)
            return;
        var mix = Mix.Gain(MixBus.Weather);
        var gain = level * (0.18f + 0.82f * outside) * (0.65f + 0.5f * gust) * 0.85f * EffectsVolume * DeafenGain;
        if (gain < 0.002f || mix <= 0f)
        {
            StormWindStop();
            return;
        }
        try
        {
            if (_stormBuffer.IsNull())
            {
                _stormPcm ??= Task.Run(Synth.Wind);
                if (!_stormPcm.IsCompleted)
                    return;
                var pcm = _stormPcm.Result;
                _stormBuffer = Al.GenBuffer();
                _stormBuffer.SetData(AlFormat.Stereo16, System.Runtime.InteropServices.MemoryMarshal.AsBytes(pcm.AsSpan()), SampleRate);
                _stormSource = Al.GenSource();
                _stormSource.SourceRelative = true; // (stereo: around you, not from anywhere)
                _stormSource.Position = new float3(0, 0, 0);
                _stormSource.RolloffFactor = 0f;
                _stormSource.Looping = true;
                _stormSource.Buffer = _stormBuffer;
            }
            var (g, cap) = VoiceGain(gain, mix);
            _stormSource.MaxGain = cap;
            _stormSource.Gain = g;
            _stormSource.Pitch = 0.94f + 0.12f * gust;
            if (EfxEnabled)
            {
                _lowpass.GainHf = (0.1f + 0.9f * outside * outside) * DeafenHf;
                _stormSource.DirectFilter = _lowpass; // (copies the filter's settings in)
            }
            if (!_stormPlaying || _stormSource.State != AlSourceState.Playing)
            {
                _stormSource.Play();
                _stormPlaying = true;
            }
        }
        catch (Exception ex)
        {
            _stormBroken = true;
            Console.WriteLine($"[storm] no wind sound: {ex.Message}");
        }
    }

    /// <summary>The wind off (no storm here, or the menus).</summary>
    public void StormWindStop()
    {
        if (!_stormPlaying)
            return;
        _stormSource.Stop();
        _stormPlaying = false;
    }

    private void DisposeStorm()
    {
        if (!_stormSource.IsNull())
        {
            _stormSource.Stop();
            Al.DeleteSources([_stormSource]);
            _stormSource = default;
        }
        if (!_stormBuffer.IsNull())
        {
            Al.DeleteBuffers([_stormBuffer]);
            _stormBuffer = default;
        }
        _stormPlaying = false;
    }
}
