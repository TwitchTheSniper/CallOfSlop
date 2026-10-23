using System.Diagnostics;
using Brutal.Numerics;
using Brutal.OpenAlApi;

namespace CallOfSlop;

/// <summary>
/// The per-class frenzy gun sound on your own gun (<see cref="FrenzyGunSound"/>): the baked layers and driven copies
/// uploaded at start-up, a small voice pool of their own, and the extra voices a shot starts while you're in frenzy.
/// The game sets <see cref="FrenzyGunLevel"/>, <see cref="FrenzyGunClass"/> and <see cref="FrenzyGunMode"/> every frame.
/// </summary>
public sealed partial class Audio
{
    /// <summary>Voices only the frenzy layers use (so they never steal gunfire or effect voices).</summary>
    public const int FrenzyVoices = 10;

    /// <summary>How far left and right the spread layer voices sit (listener-relative, metres).</summary>
    private const float FrenzySpreadX = 0.9f;

    private AlSource[] _frenzySources = [];
    private readonly double[] _frenzyStartedAt = new double[FrenzyVoices];
    private AlBuffer[][][] _frenzyLayers = []; // [class][tier][variant]
    private AlBuffer[] _frenzyDrive = []; // [weapon]
    private FrenzyGunBank? _frenzyBank;
    private int _frenzyVariant;

    /// <summary>Your frenzy level (0-6) for your own gun's sound.</summary>
    public int FrenzyGunLevel;

    /// <summary>Your frenzy class (the weapon class of your most recent kill).</summary>
    public WeaponClass FrenzyGunClass = WeaponClass.AssaultRifle;

    /// <summary>Settings > Audio "Frenzy gun sound".</summary>
    public FrenzyGunSoundMode FrenzyGunMode = FrenzyGunSoundMode.Full;

    /// <summary>Frenzy layer voices started (for the smoke log).</summary>
    public int FrenzyVoicesStarted { get; private set; }

    /// <summary>Bakes and uploads the frenzy buffers and opens their voices. A failure only turns the feature off.</summary>
    private void InitFrenzyGun(SoundSet pcm)
    {
        try
        {
            var bank = FrenzyGunSound.Bake(pcm);
            _frenzyLayers = bank.Layers.Select(c => c.Select(t => t.Select(Upload).ToArray()).ToArray()).ToArray();
            _frenzyDrive = bank.Drive.Select(Upload).ToArray();
            _frenzySources = Al.GenSources(FrenzyVoices);
            _frenzyBank = bank;
            Console.WriteLine($"[frenzygun] {bank.Count} buffers ({bank.Bytes / 1024} KB) baked in {bank.BakeMs:0} ms: {FrenzyGunSound.Tiers} layer tiers x {FrenzyGunSound.Variants} per class, "
                + $"a driven copy per weapon; {FrenzyVoices} voices; signatures {string.Join(", ", Enum.GetValues<WeaponClass>().Select(c => $"{c} {FrenzyGunSound.Get(c).Signature}"))}");
        }
        catch (Exception ex)
        {
            _frenzyBank = null;
            Console.WriteLine($"[frenzygun] off: {ex.Message}");
        }
    }

    /// <summary>
    /// Your own shot while in frenzy: starts the class layer (one voice, or two spread left and right at levels 5-6)
    /// and the driven copy, shifts <paramref name="pitch"/>, and returns the clean shot's gain.
    /// </summary>
    private float FrenzyOwnShot(WeaponDef w, float gain, ref float pitch)
    {
        if (_frenzyBank is not { } bank || _frenzySources.Length == 0 || w.Id >= bank.ShotRms.Length)
            return gain;
        var t0 = Stopwatch.GetTimestamp();
        var plan = FrenzyGunSound.Plan(FrenzyGunLevel, FrenzyGunClass, FrenzyGunMode);
        if (!plan.Active)
            return gain;
        pitch *= plan.Pitch;
        var cls = Math.Min((int)FrenzyGunClass, _frenzyLayers.Length - 1);
        var layers = _frenzyLayers[cls][plan.Tier];
        var v = _frenzyVariant = (_frenzyVariant + 1) % layers.Length;
        var g = FrenzyGunSound.Gains(plan, gain, bank.ShotRms[w.Id], bank.DriveRms[w.Id], bank.LayerRms[cls][plan.Tier][v], w.Suppressor);
        if (g.Drive > 0f)
            FrenzyVoice(_frenzyDrive[w.Id], g.Drive, pitch, 0f);
        if (g.Layer > 0f)
        {
            var lp = FrenzyGunSound.LayerPitch(w) * Jitter(0.02f);
            if (plan.Spread > 0f)
            {
                FrenzyVoice(layers[v], g.Layer, lp, -FrenzySpreadX * plan.Spread);
                FrenzyVoice(layers[(v + 1) % layers.Length], g.Layer, lp * 1.004f, FrenzySpreadX * plan.Spread);
            }
            else
                FrenzyVoice(layers[v], g.Layer, lp, 0f);
        }
        _cpuTicks += Stopwatch.GetTimestamp() - t0;
        return g.Base;
    }

    /// <summary>Starts a listener-relative frenzy voice (a free one, else the oldest), <paramref name="x"/> metres to the side.</summary>
    private void FrenzyVoice(AlBuffer buffer, float gain, float pitch, float x)
    {
        var mix = Mix.Gain(MixBus.GunFrenzyLayer); // the mixer's "Frenzy gun-sound layer" (Audio.Mixer.cs)
        if (buffer.IsNull() || gain * EffectsVolume <= 0.002f || mix <= 0f)
            return;
        var now = Now();
        var pick = 0;
        var oldest = double.MaxValue;
        for (var i = 0; i < _frenzySources.Length; i++)
        {
            if (_frenzySources[i].State != AlSourceState.Playing)
            {
                pick = i;
                break;
            }
            if (_frenzyStartedAt[i] < oldest)
            {
                oldest = _frenzyStartedAt[i];
                pick = i;
            }
        }
        var src = _frenzySources[pick];
        Start(FrenzySlot(pick), src, buffer, null, MathF.Min(1f, gain * EffectsVolume), pitch, Falloff.Normal, mix, MixBus.GunFrenzyLayer);
        if (x != 0f)
            src.Position = new float3(x, 0f, -0.4f);
        _frenzyStartedAt[pick] = now;
        _played++;
        FrenzyVoicesStarted++;
    }

    private void DisposeFrenzyGun()
    {
        if (_frenzySources.Length > 0)
        {
            foreach (var s in _frenzySources)
                s.Stop();
            Al.DeleteSources(_frenzySources);
            _frenzySources = [];
        }
        var buffers = _frenzyLayers.SelectMany(c => c).SelectMany(t => t).Concat(_frenzyDrive).Where(b => !b.IsNull()).ToArray();
        if (buffers.Length > 0)
            Al.DeleteBuffers(buffers);
        _frenzyLayers = [];
        _frenzyDrive = [];
        _frenzyBank = null;
    }
}
