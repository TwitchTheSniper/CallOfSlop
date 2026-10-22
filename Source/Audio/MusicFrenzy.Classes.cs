namespace CallOfSlop;

/// <summary>
/// The frenzy's per-weapon-class character (Weapons/Frenzy/classes.json, "music" sections; see FrenzyStyleRig.cs):
/// the class of the weapon behind the local player's most recent kill picks a style that layers onto (or partly
/// replaces) the hardbass track: its own track pool when a frenzy starts, gains on the track's stems, effects on the
/// track and instrument layers that come in level by level. A class change mid-frenzy waits for the next bar line,
/// then crossfades the two styles over one bar (the track keeps playing; only its character changes). Assault Rifle
/// is the classic frenzy: the track as it is, mixed exactly as before. Allocation-free while mixing.
/// </summary>
public sealed partial class FrenzyLayer
{
    private FrenzyStyleRig[] _rigs = [];
    private MusicPlaylist[] _classPools = [];
    private FrenzyStyleRig? _cur, _prev;
    private float _xf = 1f, _xfStep;
    private readonly float[] _dry = new float[MusicPlayer.BlockFrames * 2], _alt = new float[MusicPlayer.BlockFrames * 2];
    private int _classTarget = (int)WeaponClass.AssaultRifle, _classPlaying = (int)WeaponClass.AssaultRifle;

    /// <summary>Self-test: play this track whatever the class pool says.</summary>
    internal MusicTrack? TestTrack;

    // ---- Game thread -------------------------------------------------------------------------

    /// <summary>The weapon class whose character the frenzy should have (the class of the local player's latest kill).</summary>
    public WeaponClass Class
    {
        get => (WeaponClass)Volatile.Read(ref _classTarget);
        set => Volatile.Write(ref _classTarget, (int)value >= 0 && (int)value < FrenzyStyles.ClassCount ? (int)value : (int)WeaponClass.AssaultRifle); // no Enum.IsDefined: it boxes
    }

    /// <summary>The class whose style is playing now (the one fading in during a switch). Written by the mixing thread.</summary>
    public WeaponClass ClassPlaying => (WeaponClass)Volatile.Read(ref _classPlaying);

    /// <summary>The name of the style playing now (e.g. "Western outlaw").</summary>
    public string StyleName => _rigs.Length > 0 ? _rigs[(int)ClassPlaying].Style.Name : FrenzyStyle.Plain.Name;

    // ---- Mixing thread (or the self-test) ------------------------------------------------------------

    /// <summary>The weight of the new style during a class switch (1 when no switch is under way).</summary>
    public float Crossfade => _prev == null ? 1f : _xf;

    /// <summary>Frame (since the start) where the latest class switch began (-1 = none yet).</summary>
    public long ClassChangeFrame { get; private set; } = -1;

    /// <summary>Class switches since the layer was made.</summary>
    public int ClassSwitches { get; private set; }

    private void InitClasses()
    {
        var styles = FrenzyStyles.All;
        var tracks = MusicTracks.All.Where(t => t.Style == MusicStyle.Hardbass && (t.Timeline?.SteadyDrops ?? true)).ToArray();
        _rigs = new FrenzyStyleRig[styles.Length];
        _classPools = new MusicPlaylist[styles.Length];
        for (var c = 0; c < styles.Length; c++)
        {
            _rigs[c] = new FrenzyStyleRig((WeaponClass)c, styles[c]);
            var pool = PoolTracks(tracks, styles[c].Pool);
            _classPools[c] = styles[c].Pool == FrenzyPool.Any || pool.Length == 0 ? _pool : new MusicPlaylist(pool, _seed * 31u + (uint)c * 977u);
        }
        _r.StepLogOn = true;
    }

    /// <summary>The frenzy tracks a pool allows.</summary>
    internal static MusicTrack[] PoolTracks(MusicTrack[] tracks, FrenzyPool pool) => tracks.Where(t => pool switch
    {
        FrenzyPool.Classic => t.Hardbass?.Harder == null && t.Weird == null,
        FrenzyPool.Harder => t.Hardbass?.Harder != null,
        FrenzyPool.Weird => t.Weird != null,
        FrenzyPool.Fast => t.Bpm >= 160f,
        FrenzyPool.Slow => t.Bpm <= 158f,
        _ => true,
    }).ToArray();

    /// <summary>The track a new frenzy plays: from the wanted class's pool.</summary>
    private MusicTrack NextTrack()
    {
        if (TestTrack != null)
            return TestTrack;
        var c = Volatile.Read(ref _classTarget);
        return c >= 0 && c < _classPools.Length ? _classPools[c].Next() : _pool.Next();
    }

    /// <summary>A new frenzy: the wanted class's style straight away (no crossfade).</summary>
    private void BeginClass(int level)
    {
        var c = Math.Clamp(Volatile.Read(ref _classTarget), 0, _rigs.Length - 1);
        _cur = _rigs[c];
        _prev = null;
        _xf = 1f;
        _cur.Reset(_r, level);
        Volatile.Write(ref _classPlaying, c);
    }

    /// <summary>On a bar line: finish a switch that has run its bar, or start one to the wanted class.</summary>
    private void ClassBar(int nextLevel)
    {
        if (_cur == null)
            return;
        if (_prev != null)
        {
            _prev = null; // its bar is over (the ramp ends exactly here)
            _xf = 1f;
        }
        var want = Volatile.Read(ref _classTarget);
        if (want == (int)_cur.Class || want < 0 || want >= _rigs.Length || _fading)
            return;
        _prev = _cur;
        _cur = _rigs[want];
        _cur.Reset(_r, nextLevel);
        _xf = 0f;
        _xfStep = 1f / (float)Math.Max(1.0, _r.FramesPerBar);
        ClassChangeFrame = _frames;
        ClassSwitches++;
        Volatile.Write(ref _classPlaying, want);
    }

    /// <summary>Before the track renders: the stems of the playing style(s).</summary>
    private void PreClass()
    {
        if (_cur == null)
            return;
        var s = _cur.Style;
        float sd = s.StemDrums, sb = s.StemBass, sl = s.StemLead;
        if (_prev != null)
        {
            var p = _prev.Style;
            sd = p.StemDrums + (sd - p.StemDrums) * _xf;
            sb = p.StemBass + (sb - p.StemBass) * _xf;
            sl = p.StemLead + (sl - p.StemLead) * _xf;
        }
        _r.StemDrums = sd;
        _r.StemBass = sb;
        if (sl != 1f && _r.StemLead > 0f)
            _r.StemLead *= sl;
    }

    /// <summary>After the track renders into <paramref name="buf"/>: the style(s) on top, crossfading during a switch.</summary>
    private void PostClass(Span<float> buf, int frames)
    {
        if (_cur == null || (_prev == null && _cur.Plain))
            return; // the classic frenzy: untouched
        var n2 = frames * 2;
        var dry = _dry.AsSpan(0, n2);
        buf[..n2].CopyTo(dry);
        var fpb = _r.FramesPerBar;
        if (_prev == null)
        {
            _cur.Process(dry, buf, frames, _r, _level, fpb);
            return;
        }
        var alt = _alt.AsSpan(0, n2);
        _prev.Process(dry, alt, frames, _r, _level, fpb);
        _cur.Process(dry, buf, frames, _r, _level, fpb);
        var x0 = _xf;
        var x1 = MathF.Min(1f, x0 + _xfStep * frames);
        var dx = (x1 - x0) / frames;
        for (var i = 0; i < frames; i++)
        {
            var w = MathF.Min(1f, x0 + dx * i);
            buf[i * 2] = alt[i * 2] + (buf[i * 2] - alt[i * 2]) * w;
            buf[i * 2 + 1] = alt[i * 2 + 1] + (buf[i * 2 + 1] - alt[i * 2 + 1]) * w;
        }
        _xf = x1;
        if (x1 >= 1f)
            _prev = null;
    }
}
