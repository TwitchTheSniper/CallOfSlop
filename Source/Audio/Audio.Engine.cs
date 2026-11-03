using System.Numerics;
using Brutal.OpenAlApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Running modular engines (<see cref="EngineSounds"/> picks them; SynthEngine.cs makes the loops): a few looping OpenAL
/// sources of their own, moved, pitched and faded every frame rather than restarted. Nothing without a device.
/// </summary>
public sealed partial class Audio
{
    private AlSource[] _engineSources = [];
    private readonly int[] _engineVariant = new int[EngineSounds.Voices];
    private double _engineFresh;

    /// <summary>Engine loops started (the self-test).</summary>
    public int EngineLoopsStarted { get; private set; }

    /// <summary>An engine carries: a small one across a yard; <paramref name="scale"/> grows it with the engine.</summary>
    internal static Falloff EngineFalloff(float scale) => new(6f * MathF.Sqrt(scale), 1f, 120f * MathF.Sqrt(scale));

    private void InitEngineSources()
    {
        _engineSources = Al.GenSources(EngineSounds.Voices);
        Array.Fill(_engineVariant, -1);
    }

    private void DisposeEngineSources()
    {
        if (_engineSources.Length == 0)
            return;
        foreach (var s in _engineSources)
            s.Stop();
        Al.DeleteSources(_engineSources);
        _engineSources = [];
    }

    private static int EngineSlot(int voice) => Voices + StepVoices + FrenzyVoices + ZombieSounds.VoiceCap + ZombieSounds.StepCap + CrowdRoar.Voices + voice;

    /// <summary>Engine voice <paramref name="voice"/>: started looping if it isn't sounding or its variant changed, else moved and faded.</summary>
    public void EngineLoopAt(int voice, int variant, Vector3 position, float gain, float pitch, float scale)
    {
        if (!Enabled || (uint)voice >= (uint)_engineSources.Length)
            return;
        _engineFresh = Now();
        var set = _sfx[(int)Sfx.EngineLoop];
        var mix = Mix.Gain(MixBus.Tactical);
        if (set.Length == 0 || mix <= 0f)
        {
            EngineLoopStop(voice);
            return;
        }
        var src = _engineSources[voice];
        gain *= EffectsVolume * DeafenGain;
        pitch = Math.Clamp(pitch, 0.05f, 8f);
        variant = (int)((uint)variant % (uint)set.Length);
        if (_engineVariant[voice] != variant || src.State != AlSourceState.Playing)
        {
            src.Looping = true;
            Start(EngineSlot(voice), src, set[variant], position, gain, pitch, EngineFalloff(scale), mix, MixBus.Tactical);
            _engineVariant[voice] = variant;
            EngineLoopsStarted++;
            _played++;
            return;
        }
        src.Position = new float3(position.X, position.Y, position.Z);
        src.Pitch = pitch;
        ApplyVoiceGain(EngineSlot(voice), src, gain, mix);
        ApplyDistanceFilter(src, position, false);
    }

    /// <summary>With the audio's tick: engines nobody has refreshed for a moment (the mode changed) go quiet.</summary>
    private void EngineLoopsCheck(double now)
    {
        if (_engineSources.Length > 0 && now - _engineFresh > 0.3)
            for (var v = 0; v < _engineSources.Length; v++)
                EngineLoopStop(v);
    }

    /// <summary>Engine voice <paramref name="voice"/> off.</summary>
    public void EngineLoopStop(int voice)
    {
        if ((uint)voice >= (uint)_engineSources.Length)
            return;
        if (_engineVariant[voice] >= 0)
            _engineSources[voice].Stop();
        _engineVariant[voice] = -1;
    }
}
