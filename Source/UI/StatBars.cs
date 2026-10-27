namespace CallOfSlop;

/// <summary>
/// The 0..1 fill of each weapon stat bar, shared by the loadout menu and the gunsmith. Stats that span a wide range
/// (spread, recoil, range, magazine) use log or square-root scales, so an attachment's change is visible on every
/// class and the strongest kitted guns still fit without clamping.
/// </summary>
public static class StatBars
{
    /// <summary>Hip-fire accuracy: 0.25 deg (or less) fills the bar, 9 deg empties it (a fully choked Scattergun Lever is 0.29 deg).</summary>
    public static float Accuracy(WeaponDef w) => 1f - Math.Clamp(MathF.Log(MathF.Max(w.HipSpreadDeg, 0.01f) / 0.25f) / MathF.Log(9f / 0.25f), 0f, 1f);

    /// <summary>Recoil control (see <see cref="Recoil.ControlBar"/>).</summary>
    public static float Control(WeaponDef w) => Recoil.ControlBar(w);

    /// <summary>Full-damage range: 4 m is empty, 400 m is full.</summary>
    public static float Range(WeaponDef w) => Math.Clamp(MathF.Log(MathF.Max(w.RangeM, 1f) / 4f) / MathF.Log(100f), 0f, 1f);

    public static float FireRate(WeaponDef w) => Math.Clamp(w.Rpm / 1500f, 0f, 1f);

    public static float Mobility(WeaponDef w) => Math.Clamp((w.Mobility - 0.6f) / 0.65f, 0f, 1f);

    public static float AimSpeed(WeaponDef w) => 1f - Math.Clamp(w.AdsTime / 0.5f, 0f, 1f);

    /// <summary>Magazine size on a square-root scale: 400 rounds fill the bar.</summary>
    public static float Magazine(WeaponDef w) => Math.Clamp(MathF.Sqrt(w.Mag / 400f), 0f, 1f);

    public static float Reload(WeaponDef w) => 1f - Math.Clamp(w.Reload / 10f, 0f, 1f);

    /// <summary>"+25%" / "-30%" for a stat going from <paramref name="before"/> to <paramref name="after"/> ("" if it barely changed).</summary>
    public static string Change(float before, float after)
    {
        if (before <= 0 || MathF.Abs(after / before - 1f) < 0.005f)
            return "";
        var pct = (after / before - 1f) * 100f;
        return $"{(pct > 0 ? "+" : "")}{pct:0}%";
    }
}
