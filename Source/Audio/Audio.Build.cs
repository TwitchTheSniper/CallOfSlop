using System.Numerics;
using Brutal.Numerics;
using Brutal.OpenAlApi;

namespace CallOfSlop;

/// <summary>
/// The Sandbox's building sounds (<see cref="BuildSounds"/> decides what plays; SynthBuild.cs makes it): a small pool of
/// voices of their own, so a burst of building never takes a voice from the gunfire, and a few looping voices for the
/// welder's arc and the wrench's ratchet. Nothing without a device.
/// </summary>
public sealed partial class Audio
{
    private AlSource[] _buildSources = [], _buildLoopSources = [];
    private readonly double[] _buildStarted = new double[BuildSounds.ShotVoices];
    private readonly int[] _buildLoopSfx = new int[BuildSounds.LoopVoices];
    private double _buildLoopFresh;

    /// <summary>Building sounds started (the hand check reads it).</summary>
    public int BuildShotsStarted { get; private set; }

    /// <summary>Where the building sounds' clock comes from.</summary>
    internal static double BuildClock => Now();

    private void InitBuildSources()
    {
        _buildSources = Al.GenSources(BuildSounds.ShotVoices);
        _buildLoopSources = Al.GenSources(BuildSounds.LoopVoices);
        Array.Fill(_buildLoopSfx, -1);
    }

    private void DisposeBuildSources()
    {
        foreach (var set in new[] { _buildSources, _buildLoopSources })
        {
            if (set.Length == 0)
                continue;
            foreach (var s in set)
                s.Stop();
            Al.DeleteSources(set);
        }
        _buildSources = _buildLoopSources = [];
    }

    private static int BuildSlot(int voice) => EngineSlot(EngineSounds.Voices) + voice;

    private static int BuildLoopSlot(int voice) => BuildSlot(BuildSounds.ShotVoices) + voice;

    /// <summary>One building sound: a free voice of the pool, else the oldest. Null position: at your ear.</summary>
    internal void BuildShot(Sfx sfx, int variant, Vector3? position, float gain, float pitch, Falloff falloff)
    {
        var mix = Mix.Gain(MixBus.Building);
        if (!Enabled || mix <= 0f || _buildSources.Length == 0)
            return;
        var set = _sfx[(int)sfx];
        if (set.Length == 0)
            return;
        var pick = 0;
        var oldest = double.MaxValue;
        for (var i = 0; i < _buildSources.Length; i++)
        {
            if (_buildSources[i].State != AlSourceState.Playing)
            {
                pick = i;
                break;
            }
            if (_buildStarted[i] < oldest)
            {
                oldest = _buildStarted[i];
                pick = i;
            }
        }
        Start(BuildSlot(pick), _buildSources[pick], set[(int)((uint)variant % (uint)set.Length)], position, gain * EffectsVolume * DeafenGain, pitch, falloff, mix, MixBus.Building);
        _buildStarted[pick] = Now();
        BuildShotsStarted++;
        _played++;
    }

    /// <summary>Building loop voice <paramref name="voice"/>: started if it isn't sounding this loop, else moved and faded.</summary>
    internal void BuildLoopAt(int voice, Sfx sfx, Vector3 position, float gain, Falloff falloff)
    {
        if (!Enabled || (uint)voice >= (uint)_buildLoopSources.Length)
            return;
        _buildLoopFresh = Now();
        var mix = Mix.Gain(MixBus.Building);
        var set = _sfx[(int)sfx];
        if (set.Length == 0 || mix <= 0f)
        {
            BuildLoopStop(voice);
            return;
        }
        var src = _buildLoopSources[voice];
        gain *= EffectsVolume * DeafenGain;
        if (_buildLoopSfx[voice] != (int)sfx || src.State != AlSourceState.Playing)
        {
            src.Looping = true;
            Start(BuildLoopSlot(voice), src, set[0], position, gain, 1f, falloff, mix, MixBus.Building);
            _buildLoopSfx[voice] = (int)sfx;
            _played++;
            return;
        }
        src.Position = new float3(position.X, position.Y, position.Z);
        ApplyVoiceGain(BuildLoopSlot(voice), src, gain, mix);
        ApplyDistanceFilter(src, position, false);
    }

    /// <summary>Building loop voice <paramref name="voice"/> off.</summary>
    internal void BuildLoopStop(int voice)
    {
        if ((uint)voice >= (uint)_buildLoopSources.Length)
            return;
        if (_buildLoopSfx[voice] >= 0)
            _buildLoopSources[voice].Stop();
        _buildLoopSfx[voice] = -1;
    }

    /// <summary>With the audio's tick: loops nobody has refreshed for a moment (the mode changed) go quiet.</summary>
    private void BuildLoopsCheck(double now)
    {
        if (_buildLoopSources.Length > 0 && now - _buildLoopFresh > 0.3)
            for (var v = 0; v < _buildLoopSources.Length; v++)
                BuildLoopStop(v);
    }
}
