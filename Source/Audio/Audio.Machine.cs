using System.Numerics;
using Brutal.Numerics;
using Brutal.OpenAlApi;

namespace CallOfSlop;

/// <summary>
/// Machines' looping hums (<see cref="MachineSounds"/> picks them; SynthMachine.cs makes them): a few looping sources of
/// their own, moved and faded every frame rather than restarted, like the engines'. Nothing without a device.
/// </summary>
public sealed partial class Audio
{
    private AlSource[] _machineSources = [];
    private readonly int[] _machineSfx = new int[MachineSounds.Voices];
    private double _machineFresh;

    /// <summary>A reactor's hum carries further the bigger it is (<paramref name="scale"/>: 1 for a 10 MW plant).</summary>
    internal static Falloff MachineFalloff(float scale) => new(3f * scale, 1.1f, 45f * scale);

    private void InitMachineSources()
    {
        _machineSources = Al.GenSources(MachineSounds.Voices);
        Array.Fill(_machineSfx, -1);
    }

    private void DisposeMachineSources()
    {
        if (_machineSources.Length == 0)
            return;
        foreach (var s in _machineSources)
            s.Stop();
        Al.DeleteSources(_machineSources);
        _machineSources = [];
    }

    private static int MachineSlot(int voice) => BuildLoopSlot(BuildSounds.LoopVoices) + voice;

    /// <summary>Machine voice <paramref name="voice"/>: started looping if it isn't sounding this loop, else moved, pitched and faded.</summary>
    internal void MachineLoopAt(int voice, Sfx sfx, Vector3 position, float gain, float pitch, float scale)
    {
        if (!Enabled || (uint)voice >= (uint)_machineSources.Length)
            return;
        _machineFresh = Now();
        var set = _sfx[(int)sfx];
        var mix = Mix.Gain(MixBus.Tactical);
        if (set.Length == 0 || mix <= 0f || gain <= 0.001f)
        {
            MachineLoopStop(voice);
            return;
        }
        var src = _machineSources[voice];
        gain *= EffectsVolume * DeafenGain;
        pitch = Math.Clamp(pitch, 0.25f, 4f);
        if (_machineSfx[voice] != (int)sfx || src.State != AlSourceState.Playing)
        {
            src.Looping = true;
            Start(MachineSlot(voice), src, set[0], position, gain, pitch, MachineFalloff(scale), mix, MixBus.Tactical);
            // (start each reactor somewhere along its loop, so two switched on together don't sound as one; a reactor's
            // second voice starts where its first is, so the two hums, both on 55 Hz, add up in phase and never cancel)
            var partner = voice ^ 1;
            src.SampleOffset = (voice & 1) == 1 && _machineSfx[partner] >= 0 && _machineSources[partner].State == AlSourceState.Playing
                ? _machineSources[partner].SampleOffset
                : _rng.Next(SampleRate * 39 / 10);
            _machineSfx[voice] = (int)sfx;
            _played++;
            return;
        }
        src.Position = new float3(position.X, position.Y, position.Z);
        src.Pitch = pitch;
        ApplyVoiceGain(MachineSlot(voice), src, gain, mix);
        ApplyDistanceFilter(src, position, false);
    }

    /// <summary>Machine voice <paramref name="voice"/> off.</summary>
    internal void MachineLoopStop(int voice)
    {
        if ((uint)voice >= (uint)_machineSources.Length)
            return;
        if (_machineSfx[voice] >= 0)
            _machineSources[voice].Stop();
        _machineSfx[voice] = -1;
    }

    /// <summary>With the audio's tick: hums nobody has refreshed for a moment (the mode changed) go quiet.</summary>
    private void MachineLoopsCheck(double now)
    {
        if (_machineSources.Length > 0 && now - _machineFresh > 0.3)
            for (var v = 0; v < _machineSources.Length; v++)
                MachineLoopStop(v);
    }
}
