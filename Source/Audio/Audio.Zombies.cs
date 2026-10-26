using System.Diagnostics;
using System.Numerics;
using Brutal.OpenAlApi;

namespace CallOfSlop;

/// <summary>
/// The zombies' voices and footsteps (<see cref="ZombieSounds"/> decides what plays; SynthZombie.cs makes the sounds):
/// their own OpenAL sources, so a horde can never take the voices gunfire needs, on the Zombies row times the
/// zombie voices or zombie steps row (AudioMixDefaults.cs).
/// </summary>
public sealed partial class Audio
{
    private AlSource[] _zombieSources = []; // [0, ZombieVoiceCap): vocals; then ZombieStepCap steps

    /// <summary>How zombie vocals fade: heard from further than a footstep, gone by 70 m.</summary>
    internal static readonly Falloff ZombieVoiceFalloff = new(4f, 1f, 70f);

    /// <summary>Zombie sounds started (self-test and the [audio] line).</summary>
    public int ZombieSoundsStarted { get; private set; }

    private void InitZombieSources() => _zombieSources = Al.GenSources(ZombieSounds.VoiceCap + ZombieSounds.StepCap);

    private void DisposeZombieSources()
    {
        if (_zombieSources.Length == 0)
            return;
        foreach (var s in _zombieSources)
            s.Stop();
        Al.DeleteSources(_zombieSources);
        _zombieSources = [];
    }

    /// <summary>The meter / cap slot of zombie voice <paramref name="voice"/> (after the effect, footstep and frenzy-layer slots).</summary>
    private static int ZombieSlot(int voice, bool step) => Voices + StepVoices + FrenzyVoices + (step ? ZombieSounds.VoiceCap : 0) + voice;

    /// <summary>
    /// Plays a zombie sound on its pool voice <paramref name="voice"/> (picked by <see cref="ZombieSounds"/>): variant
    /// <paramref name="variant"/> of <paramref name="sfx"/> at <paramref name="position"/>. Nothing without a device.
    /// </summary>
    public void ZombieSound(int voice, Sfx sfx, int variant, Vector3 position, float gain, float pitch)
    {
        if (!Enabled || _zombieSources.Length == 0)
            return;
        var step = Synth.IsZombieStep(sfx);
        var bus = step ? MixBus.ZombieSteps : MixBus.ZombieVoices;
        var mix = Mix.Gain(MixBus.Zombies) * Mix.Gain(bus);
        var set = _sfx[(int)sfx];
        if (mix <= 0f || set.Length == 0 || (uint)voice >= (uint)(step ? ZombieSounds.StepCap : ZombieSounds.VoiceCap))
            return;
        var t0 = Stopwatch.GetTimestamp();
        var src = _zombieSources[(step ? ZombieSounds.VoiceCap : 0) + voice];
        gain *= EffectsVolume * DeafenGain * (step ? FootstepVolume : 1f);
        Start(ZombieSlot(voice, step), src, set[(int)((uint)variant % (uint)set.Length)], position, gain, pitch, step ? Falloff.Step : ZombieVoiceFalloff, mix, bus);
        ZombieSoundsStarted++;
        _played++;
        _cpuTicks += Stopwatch.GetTimestamp() - t0;
    }

    /// <summary>Test (the mixer's buttons): a zombie groan or a sprinter's steps that far away.</summary>
    public void TestZombie(bool steps, float distance, float angleDeg)
    {
        var at = AroundListener(distance, angleDeg);
        if (steps)
            ZombieSound(0, Sfx.ZombieSprint, _rng.Next(4), at, ZombieSounds.StepGain(ZSpeed.Sprint), Jitter(0.05f));
        else
            ZombieSound(0, Sfx.ZombieGroan, _rng.Next(6), at, ZombieSounds.Gain(ZSound.Groan), Jitter(0.08f));
    }
}
