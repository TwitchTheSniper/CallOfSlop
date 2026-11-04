namespace CallOfSlop;

/// <summary>What the main menu's music player asks the mixing thread to do.</summary>
internal enum JukeboxKind : byte
{
    Stop,
    Track,
    Frenzy,
}

/// <summary>One request from the music player. Made on a click, never per frame; the newest one wins.</summary>
internal sealed record JukeboxRequest(int Seq, JukeboxKind Kind, MusicTrack? Track, int StartBar, bool Crossfade, bool SamePlay, WeaponClass Class);

/// <summary>
/// The main menu's music player (Source/UI/UiMusicPlayer.cs) on the mixing thread. While the page is open it takes the
/// decks over from the moods: the menu's own music fades out, a picked track plays on a deck through the same renderer,
/// menu or lobby level, music volume and limiter as in the game, and a frenzy entry plays through the real
/// <see cref="FrenzyLayer"/> at the level picked. A seek loads the track again at a bar (the renderer can only start
/// on a bar line). Leaving the page crossfades back into the menu playlist. With the page closed this costs one read per
/// block and the mix is unchanged. Everything here is allocation-free but the request itself.
/// </summary>
public sealed partial class MusicPlayer
{
    /// <summary>The fade of a click in the player: quick, but no pop.</summary>
    public const float JukeboxFadeSeconds = 0.12f;

    // Game thread -> mixing thread
    private int _jbOpen, _jbPaused, _jbPosted, _jbApplied, _jbNearEnd;
    private JukeboxRequest? _jbPending;
    private double _jbPosition;

    // Mixing thread
    private bool _jbOn, _jbTrackOn;
    private JukeboxRequest? _jbHold; // waiting for a deck to come free
    private float _jbGain = 1f; // the pause ramp's end value
    private double _jbStart; // where the lead deck's load point sits in the track, seconds
    private uint _jbSeed;

    /// <summary>The music player's page is open: it says what plays, not the mood. Set every frame.</summary>
    public bool JukeboxOpen
    {
        get => Volatile.Read(ref _jbOpen) != 0;
        set => Volatile.Write(ref _jbOpen, value ? 1 : 0);
    }

    /// <summary>Pause: the music stops rendering where it is, and goes on from there.</summary>
    public bool JukeboxPaused
    {
        get => Volatile.Read(ref _jbPaused) != 0;
        set => Volatile.Write(ref _jbPaused, value ? 1 : 0);
    }

    /// <summary>A request hasn't reached the mixing thread yet.</summary>
    public bool JukeboxBusy => Volatile.Read(ref _jbApplied) != _jbPosted;

    /// <summary>Seconds into the track playing (the audible sound is about 0.2 s behind: the stream's queue).</summary>
    public double JukeboxPosition => Volatile.Read(ref _jbPosition);

    /// <summary>The track is about to end (or has): time to send the next one, which crossfades in as in the game.</summary>
    public bool JukeboxNearEnd => Volatile.Read(ref _jbNearEnd) != 0;

    /// <summary>Plays a track from a bar. <paramref name="samePlay"/> keeps this play's fills (a seek).</summary>
    public void JukeboxPlay(MusicTrack track, int startBar = 0, bool crossfade = false, bool samePlay = false) =>
        JukeboxPost(JukeboxKind.Track, track, startBar, crossfade, samePlay, default);

    /// <summary>Starts a frenzy class's style, on a set track or (null) from the class's pool; the level comes through <see cref="FrenzyLayer.Target"/>.</summary>
    public void JukeboxFrenzy(WeaponClass cls, MusicTrack? track) => JukeboxPost(JukeboxKind.Frenzy, track, 0, false, false, cls);

    /// <summary>Silence, with the page still open.</summary>
    public void JukeboxStop() => JukeboxPost(JukeboxKind.Stop, null, 0, false, false, default);

    private void JukeboxPost(JukeboxKind kind, MusicTrack? track, int bar, bool crossfade, bool samePlay, WeaponClass cls)
    {
        var seq = ++_jbPosted;
        Volatile.Write(ref _jbPending, new JukeboxRequest(seq, kind, track, bar, crossfade, samePlay, cls));
    }

    // ---- Mixing thread ---------------------------------------------------------------------------

    /// <summary>True while the player runs the decks (the moods wait).</summary>
    private bool JukeboxDirector()
    {
        if (Volatile.Read(ref _jbOpen) == 0)
        {
            if (_jbOn)
                JukeboxLeave();
            return false;
        }
        if (!_jbOn)
        {
            _jbOn = true;
            _jbTrackOn = false;
            foreach (var d in _decks)
                FadeOut(d, FadeOutSeconds);
            _lead = null;
        }
        _jbHold = Interlocked.Exchange(ref _jbPending, null) ?? _jbHold;
        if (_jbHold is { } r && JukeboxApply(r))
        {
            _jbHold = null;
            Volatile.Write(ref _jbApplied, r.Seq);
        }
        return true;
    }

    /// <summary>Back to the moods: the next block's director starts the menu playlist, crossfading out of what played.</summary>
    private void JukeboxLeave()
    {
        _jbOn = false;
        _jbTrackOn = false;
        _jbHold = null;
        Interlocked.Exchange(ref _jbPending, null);
        Volatile.Write(ref _jbApplied, _jbPosted);
        Volatile.Write(ref _jbNearEnd, 0);
        if (_jbGain <= 0f)
            JukeboxCut();
        Frenzy.TestTrack = null;
        _lead = null;
        _appliedMood = -1;
        _appliedHardbassOnly = Volatile.Read(ref _hardbassOnly);
    }

    /// <summary>Paused and silent: nothing needs a fade.</summary>
    private void JukeboxCut()
    {
        foreach (var d in _decks)
        {
            d.Active = false;
            d.Gain = 0;
        }
        _lead = null;
        Frenzy.Cut();
    }

    private bool JukeboxApply(JukeboxRequest r)
    {
        if (_jbGain <= 0f)
            JukeboxCut();
        if (r.Kind != JukeboxKind.Track)
        {
            foreach (var d in _decks)
                FadeOut(d, JukeboxFadeSeconds);
            _lead = null;
            _jbTrackOn = false;
            Frenzy.FadeOutIn(JukeboxFadeSeconds);
            if (r.Kind == JukeboxKind.Frenzy)
            {
                Frenzy.TestTrack = r.Track;
                Frenzy.Class = r.Class;
            }
            else
            {
                Frenzy.Target = 0; // (the game's next frame says so too, but a slow frame would restart the layer once this fade ends)
            }
            return true;
        }

        // Both decks busy (a crossfade, or the menu music leaving): hurry the outgoing one, and wait for it.
        if (_decks[0].Active && _decks[1].Active)
        {
            foreach (var d in _decks)
                if (d != _lead)
                    FadeOut(d, JukeboxFadeSeconds);
            return false;
        }
        Frenzy.FadeOutIn(JukeboxFadeSeconds);
        Frenzy.Target = 0;
        var fade = r.Crossfade ? CrossfadeSeconds : JukeboxFadeSeconds;
        var free = FreeDeck();
        foreach (var d in _decks)
            if (d != free)
                FadeOut(d, fade);
        if (!r.SamePlay)
        {
            _seed = _seed * 1664525u + 1013904223u;
            _jbSeed = _seed;
        }
        var track = r.Track!;
        free.R.Load(track, _jbSeed, r.StartBar);
        free.Set = track.Set;
        free.Active = true;
        free.Gain = 0;
        free.Target = 1;
        free.Step = 1f / (fade * Sr);
        _lead = free;
        _jbTrackOn = true;
        _jbStart = track.BarStart(r.StartBar);
        return true;
    }

    /// <summary>Paused, and the ramp down has finished: the block stays silent and nothing renders.</summary>
    private bool JukeboxHeld(Span<float> output)
    {
        if (!_jbOn || _jbGain > 0f || Volatile.Read(ref _jbPaused) == 0)
            return false;
        if (MeterEnabled)
        {
            MeterBegin();
            MeterOutput(output);
        }
        JukeboxPublish(); // a seek while paused moves the bar
        return true;
    }

    /// <summary>Pause and resume, ramped over a block so they don't click.</summary>
    private void JukeboxRamp(Span<float> output)
    {
        var to = _jbOn && Volatile.Read(ref _jbPaused) != 0 ? 0f : 1f;
        var from = _jbGain;
        if (from == 1f && to == 1f)
            return;
        var step = (to - from) * 2f / output.Length;
        var g = from;
        for (var i = 0; i < output.Length; i += 2)
        {
            output[i] *= g;
            output[i + 1] *= g;
            g += step;
        }
        _jbGain = to;
    }

    /// <summary>Tells the page where the track has got to.</summary>
    private void JukeboxPublish()
    {
        if (!_jbOn)
            return;
        var lead = _lead is { Active: true } l ? l : null;
        if (lead != null)
            Volatile.Write(ref _jbPosition, _jbStart + lead.R.Position);
        var near = _jbTrackOn && (lead == null || lead.R.Remaining < CrossfadeSeconds);
        Volatile.Write(ref _jbNearEnd, near ? 1 : 0);
    }
}
