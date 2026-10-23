using System.Numerics;

namespace CallOfSlop;

/// <summary>Settings > Audio "Voice chat": how you talk (and whether you hear voices at all).</summary>
public enum VoiceMode : byte
{
    /// <summary>Everyone near you hears you, from where you stand (teammates and enemies alike).</summary>
    Proximity,

    /// <summary>Only your teammates hear you, at any distance, through a radio.</summary>
    TeamRadio,

    /// <summary>No voice: you don't send, and you hear nobody.</summary>
    Off,
}

/// <summary>How a voice frame reaches one listener (the server decides, <see cref="VoiceRules.Route"/>).</summary>
public enum VoiceRoute : byte
{
    /// <summary>Not relayed to this listener.</summary>
    None,

    /// <summary>Spatial: at the talker's head, fading with distance, muffled through walls.</summary>
    Proximity,

    /// <summary>Team radio: any distance, not positioned, with the radio's band-pass.</summary>
    Radio,

    /// <summary>Flat, everyone: the lobby room and the results screen, and dead players talking to the dead.</summary>
    Open,
}

/// <summary>One side of a voice route: what the rules need to know about a talker or a listener.</summary>
public readonly record struct VoiceParty(Vector3 Head, byte Team, bool Alive);

/// <summary>
/// The voice chat's rules (pure functions and constants: the server, the client and the self-test share them).
/// Routing (<see cref="Route"/>):
///   - the lobby room and the results screen are open: everyone hears everyone, flat (whatever their mode);
///   - team radio reaches teammates at any distance (team modes: same team; co-op Zombies: everyone; free-for-all: nobody);
///   - proximity reaches living players within <see cref="Range"/> m, positioned at the talker's head;
///   - in a live match the dead hear the dead (flat) and team radio, never the living's proximity voice, and the living never
///     hear the dead's proximity voice (no calling out positions from the grave);
///   - nobody ever gets their own voice back, and bots neither talk nor listen.
/// </summary>
public static class VoiceRules
{
    /// <summary>Beyond this distance (m) a proximity voice is silent and isn't sent.</summary>
    public const float Range = 40f;

    /// <summary>Full volume within this distance (m).</summary>
    public const float NearDistance = 3f;

    /// <summary>Server relay margin past <see cref="Range"/> (players move while the frame travels).</summary>
    public const float RelayMargin = 3f;

    /// <summary>Through a wall: this much quieter, and the high end mostly gone (the EFX low-pass).</summary>
    public const float OccludedGain = 0.55f, OccludedHf = 0.2f;

    /// <summary>
    /// Server rate limit per talker: a burst of <see cref="TalkBurst"/> frames, then <see cref="TalkRate"/> a second (a little
    /// over the 50 a real mic makes, so clock drift never clips anyone). Everything past it is dropped.
    /// </summary>
    public const double TalkBurst = 10, TalkRate = 55;

    /// <summary>Per listener: at most this many frames a second relayed (about <see cref="MaxHeardTalkers"/> people at once).</summary>
    public const int MaxHeardTalkers = 6;

    public const double ListenBurst = 60, ListenRate = MaxHeardTalkers * 55;

    /// <summary>A talker counts as talking (speaker icon) for this long after their last frame.</summary>
    public const double TalkingHold = 0.3;

    /// <summary>VoiceUp payload after the message id: sequence (2), flags (1), frame.</summary>
    public const int UpBytes = 3 + VoiceCodec.FrameBytes;

    /// <summary>VoiceDown payload after the message id: talker (1), sequence (2), route (1), frame.</summary>
    public const int DownBytes = 4 + VoiceCodec.FrameBytes;

    /// <summary>VoiceUp flag: team radio.</summary>
    public const byte FlagTeam = 1;

    /// <summary>Upload bytes a second for one talker (message id included; no RakNet/UDP headers).</summary>
    public const int TalkerBytesPerSecond = (1 + UpBytes) * VoiceCodec.FramesPerSecond;

    /// <summary>Are these two on the same side for team chat and team radio?</summary>
    public static bool Mates(byte a, byte b, bool teamMode, bool coOp) => coOp || (teamMode && a < 2 && a == b);

    /// <summary>How the server relays a frame from <paramref name="talker"/> to <paramref name="listener"/>.</summary>
    public static VoiceRoute Route(in VoiceParty talker, in VoiceParty listener, bool teamRadio, MatchPhase phase, bool teamMode, bool coOp)
    {
        if (phase is MatchPhase.Lobby or MatchPhase.Ended)
            return VoiceRoute.Open;
        if (teamRadio)
            return Mates(talker.Team, listener.Team, teamMode, coOp) ? VoiceRoute.Radio : VoiceRoute.None;
        var live = phase == MatchPhase.Playing;
        var talkerDead = live && !talker.Alive;
        var listenerDead = live && !listener.Alive;
        if (talkerDead || listenerDead)
            return talkerDead && listenerDead ? VoiceRoute.Open : VoiceRoute.None;
        return Vector3.DistanceSquared(talker.Head, listener.Head) <= (Range + RelayMargin) * (Range + RelayMargin) ? VoiceRoute.Proximity : VoiceRoute.None;
    }

    /// <summary>
    /// A proximity voice's gain at <paramref name="distance"/> m: 1 up close, then an inverse-distance fade that is
    /// faded to exactly 0 at <see cref="Range"/> (a smooth tail, so it never pops off). Through a wall it is also
    /// <see cref="OccludedGain"/> quieter (<paramref name="occlusion"/>: 0 clear .. 1 blocked).
    /// </summary>
    public static float ProximityGain(float distance, float occlusion = 0f)
    {
        if (!(distance >= 0))
            distance = 0;
        if (distance >= Range)
            return 0f;
        var inv = NearDistance / MathF.Max(NearDistance, NearDistance + 0.6f * (distance - NearDistance));
        var t = distance / Range;
        var tail = 1f - t * t * t * t; // ~1 until 25 m, 0 at the range
        return inv * tail * (1f - (1f - OccludedGain) * Math.Clamp(occlusion, 0f, 1f));
    }

    /// <summary>The EFX low-pass high-frequency gain for a proximity voice (<paramref name="occlusion"/> 0..1).</summary>
    public static float ProximityHf(float distance, float occlusion) =>
        Audio.DistanceHf(distance) * (1f - (1f - OccludedHf) * Math.Clamp(occlusion, 0f, 1f));

    /// <summary>Token bucket: may one more frame go at <paramref name="now"/>? Updates the bucket when it may.</summary>
    public static bool Allow(ref double tokens, ref double last, double now, double burst, double rate)
    {
        tokens = Math.Min(burst, tokens + Math.Max(0, now - last) * rate);
        last = now;
        if (tokens < 1)
            return false;
        tokens -= 1;
        return true;
    }

    /// <summary>Settings text for a mode.</summary>
    public static string Label(VoiceMode m) => m switch
    {
        VoiceMode.TeamRadio => "Team radio",
        VoiceMode.Off => "Off",
        _ => "Proximity",
    };
}
