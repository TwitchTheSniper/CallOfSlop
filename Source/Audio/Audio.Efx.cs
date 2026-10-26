using System.Numerics;
using Brutal.OpenAlApi;

namespace CallOfSlop;

/// <summary>The acoustic character of a map: picks the reverb (<see cref="Audio.SetEnvironment"/>).</summary>
public enum AudioSpace : byte
{
    /// <summary>Open terrain (the Legacy pack's High Ground maps): a faint, wide reverb.</summary>
    Open,

    /// <summary>Streets and yards between buildings: short reflections.</summary>
    Urban,

    /// <summary>Mostly roofed over (warehouses, offices): a stony room.</summary>
    Indoor,
}

/// <summary>
/// OpenAL EFX: a distance filter on every positional sound (a high shelf: far sounds lose their air; gunshots also get a
/// low shelf, so far gunfire loses its boom too, see <see cref="DistanceFilter"/>) and one reverb send per map. A canary at start-up (the device reports ALC_EXT_EFX, a low-pass filter takes and returns a value, a reverb
/// loads into an effect slot) decides whether it is used; without it everything plays dry, exactly as before.
/// </summary>
public sealed partial class Audio
{
    private LowpassFilter _lowpass;
    private BandpassFilter _bandpass; // both shelves (OpenAL Soft: low shelf at 250 Hz, high shelf at 5 kHz); null when the driver lacks it
    private ReverbEffect _reverb;
    private AlAuxiliaryEffectSlot _slot;

    /// <summary>EFX passed its canary and is in use.</summary>
    public bool EfxEnabled { get; private set; }

    /// <summary>One line on EFX for the log ("on: EFX 1.0, 2 sends", "not supported by the device", "failed canary: ...").</summary>
    public string EfxStatus { get; private set; } = "not checked";

    /// <summary>The reverb in use (the current map's).</summary>
    public AudioSpace Space { get; private set; } = AudioSpace.Urban;

    /// <summary>High-frequency gain of the distance low-pass: 1 within 8 m, about 0.4 at 60 m, never below 0.12.</summary>
    public static float DistanceHf(float distance) => AudioMix.Defaults.DistanceHf(distance); // the mixer's Distance rows (AudioMixDefaults.cs)

    private void InitEfx()
    {
        EfxEnabled = false;
        if (_device == null)
            return;
        try
        {
            if (!_device.IsExtensionPresent("ALC_EXT_EFX"))
            {
                EfxStatus = "off (not supported by the device)";
                return;
            }
            var efx = _device.Efx;
            _lowpass = efx.CreateLowpass();
            _lowpass.Gain = 1f;
            _lowpass.GainHf = 0.5f;
            var back = _lowpass.GainHf;
            if (MathF.Abs(back - 0.5f) > 1e-3f)
                throw new InvalidOperationException($"low-pass GainHf read back {back}");
            try
            {
                _bandpass = efx.CreateBandpass();
                _bandpass.Gain = 1f;
                _bandpass.GainLf = 0.5f;
                var lf = _bandpass.GainLf;
                if (MathF.Abs(lf - 0.5f) > 1e-3f)
                    throw new InvalidOperationException($"band-pass GainLf read back {lf}");
            }
            catch (Exception)
            {
                if (!_bandpass.Filter.IsNull())
                    _bandpass.Delete();
                _bandpass = default; // the high shelf alone (no low cut with distance)
            }
            _reverb = efx.CreateReverb();
            var props = ReverbPreset.Generic;
            _reverb.Set(in props);
            _slot = efx.CreateAuxiliaryEffectSlot();
            _slot.Load(_reverb);
            _slot.Gain = 0.3f;
            var sends = efx.MaxAuxiliarySends;
            foreach (var src in _sources.Concat(_stepSources).Concat(_zombieSources))
            {
                if (sends > 0)
                    src.SetAuxiliarySend(_slot, 0, AlFilter.Null);
                src.DirectFilter = AlFilter.Null;
            }
            Al.ThrowIfError();
            EfxEnabled = true;
            EfxStatus = $"on (EFX {efx.EfxMajorVersion}.{efx.EfxMinorVersion}, {sends} sends: distance {(_bandpass.Filter.IsNull() ? "low-pass" : "band-pass (gunshots thin out)")}{(sends > 0 ? " + reverb" : "")})";
        }
        catch (Exception ex)
        {
            EfxStatus = $"off (failed canary: {ex.Message})";
            DisposeEfx();
        }
    }

    private void DisposeEfx()
    {
        EfxEnabled = false;
        try
        {
            foreach (var src in _sources.Concat(_stepSources).Concat(_zombieSources))
                src.SetAuxiliarySend(AlAuxiliaryEffectSlot.Null, 0, AlFilter.Null);
            if (!_slot.IsNull())
                _slot.Delete();
            if (!_reverb.Effect.IsNull())
                _reverb.Delete();
            if (!_lowpass.Filter.IsNull())
                _lowpass.Delete();
            if (!_bandpass.Filter.IsNull())
                _bandpass.Delete();
        }
        catch (Exception)
        {
            // best effort on the way out
        }
        _slot = default;
        _reverb = default;
        _lowpass = default;
        _bandpass = default;
    }

    /// <summary>The distance filter for a voice: <paramref name="thin"/> = also the low shelf (<see cref="DistanceLf"/>; gunshots).</summary>
    private void ApplyDistanceFilter(AlSource src, Vector3? position, bool thin = false)
    {
        if (!EfxEnabled)
            return;
        try
        {
            if (position is { } p)
            {
                var d = Vector3.Distance(p, _listener);
                var hf = Mix.DistanceHf(d) * DeafenHf; // the player's mix (Audio.Mixer.cs)
                if (thin && !_bandpass.Filter.IsNull())
                {
                    _bandpass.GainHf = hf;
                    _bandpass.GainLf = Mix.DistanceLf(d);
                    src.DirectFilter = _bandpass; // copies the filter's settings into the source
                }
                else
                {
                    _lowpass.GainHf = hf;
                    src.DirectFilter = _lowpass;
                }
            }
            else if (DeafenHf < 0.99f)
            {
                _lowpass.GainHf = DeafenHf; // flashed: even your own sounds are dull (Audio.Tactical.cs)
                src.DirectFilter = _lowpass;
            }
            else
                src.DirectFilter = AlFilter.Null;
        }
        catch (Exception ex)
        {
            EfxEnabled = false;
            EfxStatus = $"off (failed while playing: {ex.Message})";
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Map, object> Spaces = new();

    /// <summary>
    /// How a map sounds: terrain maps are open; box maps more than 30% roofed over are indoor; the rest are urban. A
    /// roof is a thin slab (not earth or grass) at least 2.5 m up with open space under it, so terraced hills and
    /// stacked floors standing on something don't count.
    /// </summary>
    public static AudioSpace Classify(Map map)
    {
        if (Spaces.TryGetValue(map, out var known))
            return (AudioSpace)known;
        var space = AudioSpace.Urban;
        if (map.HasTerrain)
            space = AudioSpace.Open;
        else
        {
            var area = MathF.Max(1f, map.HalfX * map.HalfZ * 4);
            var roof = 0f;
            var mats = map.Materials;
            var solid = map.Solid;
            for (var i = 0; i < solid.Count; i++)
            {
                var b = solid[i];
                if (b.Half.Y > 0.4f || b.Min.Y < 2.5f || b.Half.X * b.Half.Z * 4 < 4f || mats[i] == Material.Dirt)
                    continue;
                var below = new Vector3(b.Center.X, b.Min.Y - 1.2f, b.Center.Z); // head height under the slab
                var open = true;
                foreach (var o in solid)
                    if (below.X > o.Min.X && below.X < o.Max.X && below.Y > o.Min.Y && below.Y < o.Max.Y && below.Z > o.Min.Z && below.Z < o.Max.Z)
                    {
                        open = false;
                        break;
                    }
                if (open)
                    roof += b.Half.X * b.Half.Z * 4;
            }
            space = roof / area > 0.3f ? AudioSpace.Indoor : AudioSpace.Urban;
        }
        Spaces.AddOrUpdate(map, space);
        return space;
    }

    /// <summary>Sets the reverb for a map (once per map change).</summary>
    public void SetEnvironment(Map map)
    {
        Space = Classify(map);
        if (!EfxEnabled)
            return;
        try
        {
            var (props, gain) = Space switch
            {
                AudioSpace.Open => (ReverbPreset.OutdoorsRollingplains, 0.35f),
                AudioSpace.Indoor => (ReverbPreset.Stoneroom, 0.4f),
                _ => (ReverbPreset.CityStreets, 0.35f),
            };
            _reverb.Set(in props);
            _slot.Load(_reverb); // a slot keeps a copy: reload after changing the effect
            _slot.Gain = gain;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[audio] reverb for {map.Name} failed: {ex.Message}");
        }
    }
}
