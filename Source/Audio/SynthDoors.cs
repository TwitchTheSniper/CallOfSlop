namespace CallOfSlop;

/// <summary>
/// Event doors' sounds (Karl, 2026-11-18; Game/GameDoorSounds.cs plays them): a sliding door's servo, a big door's geared
/// motor, a swing door's latch and hinge, a ramp's hydraulics, and the knock of each coming home. Made from tones and
/// noise like every other sound in the game. Deterministic.
/// </summary>
public static partial class Synth
{
    /// <summary>A door's sound, or null for an effect this file doesn't make.</summary>
    private static short[]? DoorFoley(Sfx sfx)
    {
        if (sfx is < Sfx.DoorSlide or > Sfx.DoorOpenStop)
            return null;
        var rng = new Random(61000 + (int)sfx * 613);
        Buf b;
        switch (sfx)
        {
            case Sfx.DoorSlide:
                // A ship's sliding door (about a second): a pneumatic kick as the latch lets go, a servo running steady
                // along the rail with a light rumble under it, winding down as it gets there.
                b = new Buf(1.25f);
                Click(b, rng, 0, 0.35f, 0.9f);
                Band(b, rng, 0.01f, 0.45f, 0.005f, 0.09f, 1500, 9000, length: 0.3f);
                Motor(b, rng, 0.03f, 1.0f, 230, 0.32f, rise: 0.08f, fall: 0.25f, droop: 0.18f);
                Band(b, rng, 0.05f, 0.25f, 0.2f, 0.6f, 60, 500, length: 1.0f); // (the leaf on its rollers)
                Echo(b, 0.15f, 0.6f);
                return Finish(b, 0.5f);
            case Sfx.DoorSlideHeavy:
                // A heavy door (two to four seconds): a warning chirp, a deep geared motor taking up the weight with a jolt,
                // grinding along the rail, a chain's rattle in it, winding down near the end.
                b = new Buf(3.2f);
                Tone(b, 0, 0.12f, 880, 880, 1, 0.005f, 0.06f, length: 0.1f);
                Clunk(b, rng, 0.08f, 0.6f, 0.45f);
                Motor(b, rng, 0.08f, 2.9f, 95, 0.45f, rise: 0.3f, fall: 0.6f, droop: 0.12f);
                Band(b, rng, 0.1f, 0.35f, 0.5f, 3f, 40, 380, length: 2.9f); // (the leaf's weight on the rail)
                Band(b, rng, 0.2f, 0.08f, 0.4f, 3f, 2000, 6000, tremolo: 17f, length: 2.7f); // (a chain over its sprocket)
                Echo(b, 0.3f, 1.2f);
                return Finish(b, 0.6f);
            case Sfx.DoorSwing:
                // A swing door or service hatch: the latch's two clicks, then a short hinge squeak.
                b = new Buf(0.7f);
                Click(b, rng, 0, 0.6f, 0.7f);
                Click(b, rng, 0.045f, 0.4f, 0.95f);
                Glide(b, 0.1f, 0.4f, 620, 880, 0.05f, 0.08f, 0.15f, vibrato: 7f, harmonics: 0.2f);
                Band(b, rng, 0.1f, 0.08f, 0.1f, 0.25f, 800, 4000, length: 0.45f);
                Echo(b, 0.12f, 0.5f);
                return Finish(b, 0.45f);
            case Sfx.DoorSwingHeavy:
                // A ramp, a hold door or a launch hatch on hydraulics: the valve's hiss, the pump whining up and holding,
                // and the hinge's deep groan as the weight swings.
                b = new Buf(3.2f);
                Clunk(b, rng, 0, 0.55f, 0.5f);
                Band(b, rng, 0.02f, 0.3f, 0.01f, 0.2f, 1200, 8000, length: 0.5f);
                Motor(b, rng, 0.05f, 2.9f, 140, 0.32f, rise: 0.35f, fall: 0.6f, droop: 0.1f);
                Glide(b, 0.15f, 2.6f, 70, 55, 0.22f, 0.5f, 0.8f, vibrato: 3f, harmonics: 0.35f); // (the hinge's groan)
                Echo(b, 0.3f, 1.2f);
                return Finish(b, 0.6f);
            case Sfx.DoorShut:
                // Home and shut: a firm thunk, the latch, and the seal's short hiss.
                b = new Buf(0.6f);
                Clunk(b, rng, 0, 0.9f, 0.55f);
                Tone(b, 0, 0.35f, 150, 80, 0.03f, 0.002f, 0.08f);
                Click(b, rng, 0.03f, 0.35f, 0.8f);
                Band(b, rng, 0.05f, 0.18f, 0.01f, 0.08f, 2000, 9000, length: 0.3f);
                Echo(b, 0.2f, 0.7f);
                return Finish(b, 0.5f);
            case Sfx.DoorShutHeavy:
                // A heavy door or hatch home: a deep clang ringing in the frame, then two locking bolts shooting home.
                b = new Buf(1.3f);
                Clunk(b, rng, 0, 1f, 0.35f);
                Tone(b, 0, 0.6f, 90, 45, 0.05f, 0.002f, 0.2f);
                Tone(b, 0, 0.12f, 410, 405, 0.3f, 0.001f, 0.3f); // (the frame ringing)
                Tone(b, 0, 0.07f, 1130, 1120, 0.3f, 0.001f, 0.2f);
                Click(b, rng, 0.28f, 0.55f, 0.7f);
                Click(b, rng, 0.36f, 0.5f, 0.65f);
                Echo(b, 0.35f, 1.1f);
                return Finish(b, 0.6f);
            case Sfx.DoorOpenStop:
                // At its open stop: a soft clack against the buffer.
                b = new Buf(0.35f);
                Clunk(b, rng, 0, 0.45f, 0.75f);
                Tone(b, 0, 0.2f, 200, 120, 0.02f, 0.002f, 0.04f);
                Echo(b, 0.15f, 0.6f);
                return Finish(b, 0.35f);
        }
        return null;
    }

    /// <summary>
    /// An electric motor running steady at <paramref name="f"/>: a buzzy tone with its gear teeth as noise, up to speed in
    /// <paramref name="rise"/>, held, then wound down over <paramref name="fall"/>, its pitch sagging by <paramref name="droop"/> as it does.
    /// </summary>
    private static void Motor(Buf b, Random rng, float start, float dur, float f, float gain, float rise, float fall, float droop)
    {
        var i0 = (int)(start * Sr);
        var n = (int)(dur * Sr);
        double phase = 0, teeth = 0;
        float lp = 0;
        for (var k = 0; k < n && i0 + k < b.Len; k++)
        {
            var t = k / (float)Sr;
            var up = MathF.Min(1f, t / rise);
            var down = MathF.Min(1f, (dur - t) / fall);
            var speed = (0.7f + 0.3f * up) * (1f - droop * (1f - down));
            var ff = f * speed;
            phase += 2 * Math.PI * ff / Sr;
            teeth += 2 * Math.PI * ff * 0.25 / Sr;
            var s = (float)Math.Sin(phase) * 0.6f + (float)Math.Sin(phase * 2) * 0.3f + (float)Math.Sin(phase * 3) * 0.15f;
            lp += 0.2f * ((float)(rng.NextDouble() * 2 - 1) - lp);
            var gear = lp * MathF.Pow(0.5f + 0.5f * (float)Math.Sin(teeth), 4f) * 0.7f;
            b.S[i0 + k] += (s + gear) * gain * up * down;
        }
    }
}
