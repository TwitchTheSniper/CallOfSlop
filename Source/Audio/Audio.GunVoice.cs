namespace CallOfSlop;

/// <summary>
/// Per-shot colour from a kitted gun's attachments, on top of the weapon's baked identity (<see cref="GunVoice"/>):
/// a long or heavy barrel plays a touch lower, a short barrel or a muzzle brake a touch higher and louder. A small
/// pitch / gain pair per kit, cached after the first shot (no allocation per shot).
/// </summary>
public sealed partial class Audio
{
    private readonly Dictionary<uint, (float Pitch, float Gain)> _kitVoice = new();

    /// <summary>The pitch and gain multipliers <see cref="Shot"/> applies for the gun's attachments (1, 1 for a stock gun).</summary>
    private (float Pitch, float Gain) KitVoice(WeaponDef w)
    {
        if (w.Kit == 0)
            return (1f, 1f);
        lock (_kitVoice)
        {
            if (!_kitVoice.TryGetValue(w.Kit, out var kv))
                _kitVoice[w.Kit] = kv = KitVoiceFor(w.Kit);
            return kv;
        }
    }

    /// <summary>The self-test's way in to the cached per-shot lookup.</summary>
    internal (float Pitch, float Gain) KitVoiceForTest(WeaponDef w) => KitVoice(w);

    /// <summary>The attachments' sound colour for a kit (see <see cref="KitVoice"/>).</summary>
    public static (float Pitch, float Gain) KitVoiceFor(uint kit)
    {
        float pitch = 1f, gain = 1f;
        foreach (var a in Attachments.InKit(kit))
            (pitch, gain) = a.Name switch
            {
                "Long Barrel" => (pitch * 0.97f, gain),
                "Heavy Barrel" => (pitch * 0.98f, gain * 1.03f),
                "Short Barrel" => (pitch * 1.035f, gain * 1.04f),
                "Muzzle Brake" => (pitch * 1.02f, gain * 1.1f),
                "Compensator" => (pitch * 1.01f, gain * 1.05f),
                "Loudener" => (pitch * 0.9f, gain * 1.1f), // deeper and bigger (the carry comes from WeaponDef.Loudness: Audio.GunMix.cs)
                "Vent Collar" => (pitch * 1.015f, gain * 1.05f),
                _ => (pitch, gain),
            };
        return (pitch, gain);
    }
}
