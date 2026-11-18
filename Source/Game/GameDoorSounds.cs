using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Event doors' sounds (Karl, 2026-11-18; SynthDoors.cs makes them): a door or hatch is heard as it starts to move and
/// again as it comes home, open or shut. A sliding door hums along on a servo, a swing door clicks its latch, and anything
/// slow (two seconds or more), a docking hatch or a launch hatch gets the heavy motor or hydraulics, pitched so the sound
/// lasts about as long as the door takes. Worked out on every peer from the door events it already has
/// (<see cref="ConstructWorld.DoorHeard"/>: the server's own, a client's from the server's word), so nothing new is sent.
/// A double door's two leaves are heard once. Nothing allocates.
/// </summary>
public sealed partial class Game
{
    /// <summary>Doors further than this aren't heard at all (m).</summary>
    private const float DoorHearRange = 70f;

    /// <summary>A door is heavy when it takes this long (s) or more.</summary>
    private const float HeavyDoorSeconds = 1.8f;

    /// <summary>How long each motion sound runs at pitch 1 (s), to match it to the door's own time.</summary>
    private const float SlideSoundSeconds = 1.0f, HeavySoundSeconds = 2.9f;

    private readonly Vector3[] _doorHeardAt = new Vector3[8];
    private readonly Sfx[] _doorHeardSfx = new Sfx[8];

    /// <summary>Every Sandbox frame, host and clients (from <see cref="MachineFrameSounds"/>): the doors that moved or settled since the last.</summary>
    private void DoorSounds()
    {
        var events = Constructs.DoorHeard;
        if (events.Count == 0)
            return;
        var heard = 0;
        var listener = Audio.ListenerPosition;
        for (var e = 0; e < events.Count; e++)
        {
            var (body, part, flags) = events[e];
            if (Constructs.Get(body) is not { } c || part >= c.PartCount || c.DefOf(part).Door is not { } d)
                continue;
            var at = ConstructRender.PartCentre(c, part);
            if (Vector3.DistanceSquared(at, listener) > DoorHearRange * DoorHearRange)
                continue;
            var heavy = d.Seconds >= HeavyDoorSeconds || d.Dock || d.Launch;
            var open = (flags & 1) != 0;
            Sfx sfx;
            float pitch, gain;
            if ((flags & 2) == 0)
            {
                // On its way: the motion, stretched or squeezed a little towards the door's own time.
                sfx = d.Slide ? heavy ? Sfx.DoorSlideHeavy : Sfx.DoorSlide : heavy ? Sfx.DoorSwingHeavy : Sfx.DoorSwing;
                pitch = sfx switch
                {
                    Sfx.DoorSlide => Math.Clamp(SlideSoundSeconds / d.Seconds, 0.85f, 1.15f),
                    Sfx.DoorSwing => 1f,
                    _ => Math.Clamp(HeavySoundSeconds / d.Seconds, 0.8f, 1.6f),
                };
                gain = heavy ? 0.9f : 0.7f;
            }
            else
            {
                // Home: shut with a thunk (heavy: a clang and bolts), open against its stop.
                sfx = open ? Sfx.DoorOpenStop : heavy ? Sfx.DoorShutHeavy : Sfx.DoorShut;
                pitch = open && heavy ? 0.7f : 1f;
                gain = open ? heavy ? 0.8f : 0.6f : heavy ? 0.9f : 0.75f;
            }
            var again = false; // (a double door's other leaf, or a door turned back twice in a frame)
            for (var h = 0; h < heard && !again; h++)
                again = _doorHeardSfx[h] == sfx && Vector3.DistanceSquared(_doorHeardAt[h], at) < 9f;
            if (again)
                continue;
            if (heard < _doorHeardAt.Length)
            {
                _doorHeardAt[heard] = at;
                _doorHeardSfx[heard] = sfx;
                heard++;
            }
            else
                break; // (enough doors for one frame)
            Audio.Effect3DPitched(sfx, at, gain, pitch * (0.97f + 0.06f * (float)Random.Shared.NextDouble()));
        }
        events.Clear();
    }
}
