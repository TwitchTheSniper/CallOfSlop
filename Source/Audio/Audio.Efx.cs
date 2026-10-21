using System.Numerics;
using Brutal.OpenAlApi;

namespace CallOfSlop;

/// <summary>The acoustic character of a map: picks the reverb (<see cref="Audio.SetEnvironment"/>).</summary>
public enum AudioSpace : byte
{
    /// <summary>Open terrain (the High Ground maps): a faint, wide reverb.</summary>
    Open,

    /// <summary>Streets and yards between buildings: short reflections.</summary>
    Urban,

    /// <summary>Mostly roofed over (warehouses, offices): a stony room.</summary>
    Indoor,
}

/// <summary>
/// OpenAL EFX: a low-pass on every positional sound by distance (far gunfire loses its crack) and one reverb send per
/// map. A canary at start-up (the device reports ALC_EXT_EFX, a low-pass filter takes and returns a value, a reverb
/// loads into an effect slot) decides whether it is used; without it everything plays dry, exactly as before.
/// </summary>
public sealed partial class Audio
{
    private LowpassFilter _lowpass;
    private ReverbEffect _reverb;
    private AlAuxiliaryEffectSlot _slot;

    /// <summary>EFX passed its canary and is in use.</summary>
    public bool EfxEnabled { get; private set; }

    /// <summary>One line on EFX for the log ("on: EFX 1.0, 2 sends", "not supported by the device", "failed canary: ...").</summary>
    public string EfxStatus { get; private set; } = "not checked";

    /// <summary>The reverb in use (the current map's).</summary>
    public AudioSpace Space { get; private set; } = AudioSpace.Urban;

    /// <summary>High-frequency gain of the distance low-pass: 1 within 8 m, about 0.4 at 60 m, never below 0.12.</summary>
    public static float DistanceHf(float distance) => Math.Clamp(MathF.Exp(-MathF.Max(0f, distance - 8f) / 55f), 0.12f, 1f);

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
            _reverb = efx.CreateReverb();
            var props = ReverbPreset.Generic;
            _reverb.Set(ref props);
            _slot = efx.CreateAuxiliaryEffectSlot();
            _slot.Load(_reverb);
            _slot.Gain = 0.3f;
            var sends = efx.MaxAuxiliarySends;
            foreach (var src in _sources.Concat(_stepSources))
            {
                if (sends > 0)
                    src.SetAuxiliarySend(_slot, 0, AlFilter.Null);
                src.DirectFilter = AlFilter.Null;
            }
            Al.ThrowIfError();
            EfxEnabled = true;
            EfxStatus = $"on (EFX {efx.EfxMajorVersion}.{efx.EfxMinorVersion}, {sends} sends: distance low-pass{(sends > 0 ? " + reverb" : "")})";
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
            foreach (var src in _sources.Concat(_stepSources))
                src.SetAuxiliarySend(AlAuxiliaryEffectSlot.Null, 0, AlFilter.Null);
            if (!_slot.IsNull())
                _slot.Delete();
            if (!_reverb.Effect.IsNull())
                _reverb.Delete();
            if (!_lowpass.Filter.IsNull())
                _lowpass.Delete();
        }
        catch (Exception)
        {
            // best effort on the way out
        }
        _slot = default;
        _reverb = default;
        _lowpass = default;
    }

    private void ApplyDistanceFilter(AlSource src, Vector3? position)
    {
        if (!EfxEnabled)
            return;
        try
        {
            if (position is { } p)
            {
                _lowpass.GainHf = DistanceHf(Vector3.Distance(p, _listener));
                src.DirectFilter = _lowpass; // copies the filter's settings into the source
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

    /// <summary>
    /// How a map sounds: terrain maps are open; box maps more than a quarter roofed over (thin slabs at least 2.5 m up)
    /// are indoor; the rest are urban.
    /// </summary>
    public static AudioSpace Classify(Map map)
    {
        if (map.HasTerrain)
            return AudioSpace.Open;
        var area = MathF.Max(1f, map.HalfX * map.HalfZ * 4);
        var roof = 0f;
        foreach (var b in map.Solid)
            if (b.Half.Y <= 0.4f && b.Min.Y >= 2.5f && b.Half.X * b.Half.Z * 4 >= 4f)
                roof += b.Half.X * b.Half.Z * 4;
        return roof / area > 0.25f ? AudioSpace.Indoor : AudioSpace.Urban;
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
            _reverb.Set(ref props);
            _slot.Load(_reverb); // a slot keeps a copy: reload after changing the effect
            _slot.Gain = gain;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[audio] reverb for {map.Name} failed: {ex.Message}");
        }
    }
}
