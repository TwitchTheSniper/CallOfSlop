namespace CallOfSlop;

/// <summary>
/// Flashbang deafness: while <see cref="Deafen"/> is up, every new sound starts quieter and duller (a low-pass on top of
/// the distance one when EFX is on), and the flashed player hears a soft ringing (<see cref="Sfx.Tinnitus"/>).
/// Sounds already playing are left alone; music is untouched.
/// </summary>
public sealed partial class Audio
{
    /// <summary>0 (normal) .. 1 (fully muffled). Set every frame by the game (GameTactical.cs).</summary>
    public float Deafen;

    /// <summary>Volume of new sounds while deafened: down to 35%.</summary>
    private float DeafenGain => 1f - 0.65f * Math.Clamp(Deafen, 0f, 1f);

    /// <summary>High-frequency gain while deafened: down to 8% (everything sounds like it's through a wall).</summary>
    private float DeafenHf => 1f - 0.92f * Math.Clamp(Deafen, 0f, 1f);

    /// <summary>The ring after a flash of strength <paramref name="s"/> (not muffled itself: it's the only clear thing you hear).</summary>
    public void Tinnitus(float s)
    {
        if (!Enabled || s <= 0.15f)
            return;
        var keep = Deafen;
        Deafen = 0;
        Play(Pick(Sfx.Tinnitus), null, 0.25f + 0.35f * Math.Clamp(s, 0f, 1f), 1f);
        Deafen = keep;
    }
}
