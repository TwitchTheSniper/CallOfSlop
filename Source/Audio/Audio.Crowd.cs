using System.Numerics;
using Brutal.OpenAlApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The mass horde's crowd roar (<see cref="CrowdRoar"/> decides where and how loud; SynthZombie.cs makes the loop):
/// <see cref="CrowdRoar.Voices"/> looping OpenAL sources of its own, one per dense cluster, on the Zombies row times the
/// zombie voices row, moved and faded every frame rather than restarted. Nothing without a device.
/// </summary>
public sealed partial class Audio
{
    private AlSource[] _crowdSources = [];
    private readonly int[] _crowdVariant = new int[CrowdRoar.Voices];

    /// <summary>Crowd roars started (self-test and the [audio] line).</summary>
    public int CrowdRoarsStarted { get; private set; }

    /// <summary>A roar is heard from far across the field, gone by 160 m.</summary>
    internal static readonly Falloff CrowdRoarFalloff = new(10f, 1f, 160f);

    private void InitCrowdSources()
    {
        _crowdSources = Al.GenSources(CrowdRoar.Voices);
        Array.Fill(_crowdVariant, -1);
    }

    private void DisposeCrowdSources()
    {
        if (_crowdSources.Length == 0)
            return;
        foreach (var s in _crowdSources)
            s.Stop();
        Al.DeleteSources(_crowdSources);
        _crowdSources = [];
    }

    /// <summary>The meter / cap slot of crowd roar voice <paramref name="voice"/> (after the zombie voices and steps).</summary>
    private static int CrowdSlot(int voice) => Voices + StepVoices + FrenzyVoices + ZombieSounds.VoiceCap + ZombieSounds.StepCap + voice;

    /// <summary>
    /// Roar voice <paramref name="voice"/> at <paramref name="position"/> with <paramref name="gain"/> (before distance
    /// and the mix): started looping (variant <paramref name="variant"/>) if it isn't sounding, else moved and faded.
    /// </summary>
    public void CrowdRoarAt(int voice, int variant, Vector3 position, float gain)
    {
        if (!Enabled || (uint)voice >= (uint)_crowdSources.Length)
            return;
        var set = _sfx[(int)Sfx.CrowdRoar];
        var mix = Mix.Gain(MixBus.Zombies) * Mix.Gain(MixBus.ZombieVoices);
        if (set.Length == 0 || mix <= 0f)
        {
            CrowdRoarStop(voice);
            return;
        }
        var src = _crowdSources[voice];
        gain *= EffectsVolume * DeafenGain;
        variant = (int)((uint)variant % (uint)set.Length);
        if (_crowdVariant[voice] != variant || src.State != AlSourceState.Playing)
        {
            src.Looping = true;
            Start(CrowdSlot(voice), src, set[variant], position, gain, 1f, CrowdRoarFalloff, mix, MixBus.ZombieVoices);
            _crowdVariant[voice] = variant;
            CrowdRoarsStarted++;
            _played++;
            return;
        }
        src.Position = new float3(position.X, position.Y, position.Z);
        ApplyVoiceGain(CrowdSlot(voice), src, gain, mix);
        ApplyDistanceFilter(src, position, CrowdRoarFalloff.Thin);
    }

    /// <summary>Roar voice <paramref name="voice"/> off.</summary>
    public void CrowdRoarStop(int voice)
    {
        if ((uint)voice >= (uint)_crowdSources.Length)
            return;
        if (_crowdVariant[voice] >= 0)
            _crowdSources[voice].Stop();
        _crowdVariant[voice] = -1;
    }

    /// <summary>Test (the mixer's buttons): the crowd roar that far away, for a few seconds.</summary>
    public void TestCrowdRoar(float distance, float angleDeg) => CrowdRoarAt(0, 0, AroundListener(distance, angleDeg), 1f);
}
