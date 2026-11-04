using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Main menu > MUSIC PLAYER: every piece of music in the game (<see cref="MusicCatalogue"/>), to pick and listen to. It
/// runs through the game's own music thread and renderers (Music.Jukebox.cs), so it sounds as the game does. A frenzy
/// line plays the real frenzy layer at a level picked here; levels change on the bar line, as in a match. A track seeks
/// by bars (the renderer starts on a bar line). Only the music volume is saved. No allocation per frame while it plays:
/// the rows are made once, the list again only when the filter changes, and <c>$"..."</c> into ImGui uses its frame arena.
/// </summary>
public sealed partial class Ui
{
    private const int MpFrenzyStartLevel = 4;

    private MusicEntry[] _mpAll = [];
    private int[] _mpShown = [];
    private int _mpShownCount;
    private ImInputString? _mpFilter;
    private string? _mpFilterSeen;
    private int _mpCur = -1; // the line picked (it stays picked after Stop)
    private bool _mpOn; // it plays (or is paused)
    private bool _mpLoop, _mpShuffle, _mpScroll, _mpVolumeTouched, _mpSeeking;
    private float _mpSeekTo;
    private int _mpLevel = MpFrenzyStartLevel, _mpPick;
    private readonly List<int> _mpHistory = [], _mpBag = [];
    private readonly Random _mpRng = new();

    /// <summary>The music player's page is on screen (the game hands it the music thread).</summary>
    public bool MusicPlayerOpen => _page == Page.Music;

    /// <summary>The frenzy level the player asks for, -1 when it isn't playing a frenzy line.</summary>
    public int MusicPlayerFrenzyLevel => MusicPlayerOpen && _mpOn && _mpCur >= 0 && _mpAll[_mpCur].IsFrenzy ? _mpLevel : -1;

    private void OpenMusicPlayer(Audio audio)
    {
        if (_mpAll.Length == 0)
        {
            _mpAll = MusicCatalogue.Build();
            _mpShown = new int[_mpAll.Length];
            _mpFilter = new ImInputString(64);
        }
        _mpOn = false;
        _mpSeeking = false;
        _mpVolumeTouched = false;
        _mpHistory.Clear();
        _mpBag.Clear();
        _mpFilterSeen = null;
        audio.Music.JukeboxPaused = false;
        _page = Page.Music;
    }

    private void CloseMusicPlayer(Settings s)
    {
        if (_mpVolumeTouched)
            s.Save();
        _mpOn = false;
        _page = Page.Main;
    }

    private void MpRefilter(string filter)
    {
        _mpFilterSeen = filter;
        var terms = filter.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _mpShownCount = 0;
        for (var i = 0; i < _mpAll.Length; i++)
            if (Array.TrueForAll(terms, t => _mpAll[i].Search.Contains(t, StringComparison.Ordinal)))
                _mpShown[_mpShownCount++] = i;
        _mpBag.Clear();
    }

    private void MpPlay(MusicPlayer music, int line, bool crossfade = false, bool remember = true)
    {
        if (remember && _mpOn && _mpCur >= 0 && _mpCur != line)
        {
            if (_mpHistory.Count >= 64)
                _mpHistory.RemoveAt(0);
            _mpHistory.Add(_mpCur);
        }
        _mpCur = line;
        _mpOn = true;
        _mpScroll = true;
        music.JukeboxPaused = false;
        var e = _mpAll[line];
        if (e.Track is { } track)
        {
            music.JukeboxPlay(track, 0, crossfade);
            return;
        }
        _mpPick = 0;
        music.JukeboxFrenzy(e.Class, null);
    }

    /// <summary>The line after (or before) the current one in the list as filtered, or a shuffled pick. -1 = none.</summary>
    private int MpStep(int direction, bool tracksOnly)
    {
        if (_mpShuffle && direction > 0)
        {
            if (_mpBag.Count == 0)
            {
                for (var i = 0; i < _mpShownCount; i++)
                    if (!tracksOnly || !_mpAll[_mpShown[i]].IsFrenzy)
                        _mpBag.Add(_mpShown[i]);
                for (var i = _mpBag.Count - 1; i > 0; i--)
                {
                    var j = _mpRng.Next(i + 1);
                    (_mpBag[i], _mpBag[j]) = (_mpBag[j], _mpBag[i]);
                }
                if (_mpBag.Count > 1 && _mpBag[^1] == _mpCur)
                    (_mpBag[0], _mpBag[^1]) = (_mpBag[^1], _mpBag[0]); // never the same one twice in a row
            }
            if (_mpBag.Count == 0)
                return -1;
            var pick = _mpBag[^1];
            _mpBag.RemoveAt(_mpBag.Count - 1);
            return pick;
        }
        if (_mpShownCount == 0)
            return -1;
        var at = Array.IndexOf(_mpShown, _mpCur, 0, _mpShownCount);
        if (at < 0)
            at = direction > 0 ? -1 : 0; // the current line is filtered out: from the top
        for (var n = 0; n < _mpShownCount; n++)
        {
            at = (at + direction + _mpShownCount) % _mpShownCount;
            if (!tracksOnly || !_mpAll[_mpShown[at]].IsFrenzy)
                return _mpShown[at];
        }
        return -1;
    }

    private void MpNext(MusicPlayer music)
    {
        var line = MpStep(1, tracksOnly: false);
        if (line >= 0)
            MpPlay(music, line);
    }

    private void MpPrevious(MusicPlayer music)
    {
        if (_mpOn && _mpCur >= 0 && _mpAll[_mpCur].Track != null && music.JukeboxPosition > 3)
        {
            MpPlay(music, _mpCur, remember: false); // back to the top first, as players do
            return;
        }
        if (_mpHistory.Count > 0)
        {
            var back = _mpHistory[^1];
            _mpHistory.RemoveAt(_mpHistory.Count - 1);
            MpPlay(music, back, remember: false);
            return;
        }
        var line = MpStep(-1, tracksOnly: false);
        if (line >= 0)
            MpPlay(music, line, remember: false);
    }

    /// <summary>A track is ending: loop it, or crossfade into the next one, as the game's playlists do. A frenzy line never ends.</summary>
    private void MpAdvance(MusicPlayer music)
    {
        if (!_mpOn || _mpCur < 0 || _mpAll[_mpCur].IsFrenzy || music.JukeboxBusy || !music.JukeboxNearEnd)
            return;
        var line = _mpLoop ? _mpCur : MpStep(1, tracksOnly: true);
        if (line >= 0)
            MpPlay(music, line, crossfade: true);
    }

    private void DrawMusicPlayer(Settings s, Audio audio)
    {
        var music = audio.Music;
        var filter = _mpFilter != null ? ImGuiSafe.Text(_mpFilter) : "";
        if (!ReferenceEquals(filter, _mpFilterSeen))
            MpRefilter(filter);

        var vp = ImGui.GetMainViewport();
        var width = MathF.Min(1120 * _scale, vp.WorkSize.X - 24 * _scale);
        var height = MathF.Min(820 * _scale, vp.WorkSize.Y - 64 * _scale);
        CenterNext();
        ImGui.SetNextWindowSize(new float2(width, height), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.88f);
        if (ImGui.Begin("##musicplayer"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings))
        {
            Title("Music player - every track in the game");
            ImGui.SetNextItemWidth(320 * _scale);
            ImGui.InputTextWithHint("##mpfilter"u8, "Filter: name, style, key, mode, BPM"u8, _mpFilter!);
            ImGui.SameLine();
            ImGui.TextColored(Dim, $"{_mpShownCount} of {_mpAll.Length}");

            var frame = ImGui.GetFrameHeightWithSpacing();
            var transport = 2 * frame + (64 + 62) * _scale + 3 * ImGui.GetStyle().ItemSpacing.Y;
            MpList(music, new float2(0, ImGui.GetContentRegionAvail().Y - transport));
            MpTransport(s, music);
        }
        ImGui.End();
        MpAdvance(music);
        if (_page == Page.Music && ImGui.IsKeyPressed(ImGuiKey.Escape, false) && !ImGui.GetIO().WantTextInput)
            CloseMusicPlayer(s);
    }

    private void MpList(MusicPlayer music, float2 size)
    {
        if (!ImGui.BeginTable("##mplist"u8, 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY, size))
            return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Track"u8, ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Style"u8, ImGuiTableColumnFlags.WidthFixed, 190 * _scale);
        ImGui.TableSetupColumn("Plays in"u8, ImGuiTableColumnFlags.WidthFixed, 80 * _scale);
        ImGui.TableSetupColumn("BPM"u8, ImGuiTableColumnFlags.WidthFixed, 80 * _scale);
        ImGui.TableSetupColumn("Length"u8, ImGuiTableColumnFlags.WidthFixed, 70 * _scale);
        ImGui.TableSetupColumn("Key"u8, ImGuiTableColumnFlags.WidthFixed, 190 * _scale);
        ImGui.TableHeadersRow();
        string? group = null;
        for (var n = 0; n < _mpShownCount; n++)
        {
            var i = _mpShown[n];
            var e = _mpAll[i];
            if (!ReferenceEquals(e.Group, group))
            {
                group = e.Group;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGuiSafe.TextColored(Orange, group);
            }
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (ImGui.Selectable(e.RowId, i == _mpCur, ImGuiSelectableFlags.SpanAllColumns))
                MpPlay(music, i);
            if (e.About.Length > 0 && ImGui.IsItemHovered())
                ImGuiSafe.WrappedTooltip(e.About, 420 * _scale);
            if (_mpScroll && i == _mpCur)
            {
                ImGui.SetScrollHereY();
                _mpScroll = false;
            }
            ImGui.TableNextColumn();
            ImGui.Text(e.Style);
            ImGui.TableNextColumn();
            ImGui.Text(e.Mode);
            ImGui.TableNextColumn();
            ImGui.Text(e.Bpm);
            ImGui.TableNextColumn();
            ImGui.Text(e.Length);
            ImGui.TableNextColumn();
            ImGui.Text(e.Key);
        }
        _mpScroll = false;
        ImGui.EndTable();
    }

    private void MpTransport(Settings s, MusicPlayer music)
    {
        var e = _mpCur >= 0 ? _mpAll[_mpCur] : null;
        var frenzy = _mpOn && e is { IsFrenzy: true };
        var track = _mpOn ? e?.Track : null;
        var paused = music.JukeboxPaused;
        var rendering = _mpOn && (music.JukeboxBusy || (frenzy && music.Frenzy.TrackName == null));

        // ---- What's playing ---------------------------------------------------------------------
        ImGui.Spacing();
        if (!music.Streaming)
            ImGui.TextColored(Red, "No sound device: the music isn't running."u8);
        else if (!_mpOn)
            ImGui.TextColored(Dim, e != null ? "Stopped."u8 : "Pick a track to play it."u8);
        else if (rendering)
            ImGui.TextColored(Orange, "Rendering..."u8);
        else if (frenzy)
            ImGuiSafe.TextColored(paused ? Dim : Green, $"{(paused ? "Paused" : "Now playing")}: {e!.Name} - {music.Frenzy.StyleName}, on {music.Frenzy.TrackName} at {music.Frenzy.Bpm:0} BPM");
        else
            ImGuiSafe.TextColored(paused ? Dim : Green, $"{(paused ? "Paused" : "Now playing")}: {e!.Name} - {e.Style}, {e.Bpm} BPM, {e.Key}");

        MpProgress(music, track, frenzy, rendering);

        // ---- Buttons --------------------------------------------------------------------------------
        var size = new float2(92 * _scale, 42 * _scale);
        if (ImGui.Button("Previous##mp"u8, size))
            MpPrevious(music);
        ImGui.SameLine();
        if (ImGui.Button(_mpOn && !paused ? "Pause##mp"u8 : "Play##mp"u8, size))
        {
            if (_mpOn)
                music.JukeboxPaused = !paused;
            else if (_mpCur >= 0)
                MpPlay(music, _mpCur);
            else if (_mpShownCount > 0)
                MpPlay(music, _mpShown[0]);
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(!_mpOn);
        if (ImGui.Button("Stop##mp"u8, size))
        {
            _mpOn = false;
            music.JukeboxPaused = false;
            music.JukeboxStop();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Next##mp"u8, size))
            MpNext(music);
        ImGui.SameLine(0, 24 * _scale);
        ImGui.Checkbox("Loop one##mp"u8, ref _mpLoop);
        ImGui.SameLine();
        if (ImGui.Checkbox("Shuffle##mp"u8, ref _mpShuffle))
            _mpBag.Clear();
        ImGui.SameLine(0, 24 * _scale);
        ImGui.SetNextItemWidth(130 * _scale);
        if (ImGui.SliderFloat("Volume##mp"u8, ref s.MusicVolume, 0f, 1f, "%.2f"u8))
        {
            music.Volume = s.MusicVolume;
            _mpVolumeTouched = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetItemTooltip("The game's music volume: the same slider as Settings > Audio."u8);

        // ---- Frenzy: the level and the track -----------------------------------------------------------
        if (frenzy)
        {
            ImGui.Text("Frenzy level"u8);
            for (var i = 1; i <= FrenzyMeter.MaxLevel; i++)
            {
                ImGui.SameLine();
                if (ImGui.RadioButton(i == 6 ? "6 MAX##mpl"u8 : i == 5 ? "5 FRENZY##mpl"u8 : i == 4 ? "4##mpl"u8 : i == 3 ? "3##mpl"u8 : i == 2 ? "2##mpl"u8 : "1##mpl"u8, _mpLevel == i))
                    _mpLevel = i;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetItemTooltip("Level 6 is as loud as the gunfire it plays over in a match."u8);
            ImGui.SameLine(0, 24 * _scale);
            ImGui.Text("Track"u8);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(250 * _scale);
            var pool = e!.Pool;
            if (ImGui.BeginCombo("##mpfrenzytrack"u8, _mpPick > 0 ? pool[_mpPick - 1].Name : "Any of this class"))
            {
                if (ImGui.Selectable("Any of this class"u8, _mpPick == 0) && _mpPick != 0)
                {
                    _mpPick = 0;
                    music.JukeboxFrenzy(e.Class, null);
                }
                for (var i = 0; i < pool.Length; i++)
                    if (ImGui.Selectable(pool[i].Name, _mpPick == i + 1) && _mpPick != i + 1)
                    {
                        _mpPick = i + 1;
                        music.JukeboxFrenzy(e.Class, pool[i]);
                    }
                ImGui.EndCombo();
            }
        }
        else
        {
            ImGui.TextColored(Dim, "Click a line to play it; drag the bar to seek. The next track crossfades in, as in the game."u8);
        }

        ImGui.Spacing();
        if (BigButton("BACK"))
            CloseMusicPlayer(s);
    }

    /// <summary>The progress bar: drag it to seek (on release, to the bar line under the mouse). A frenzy has no end, so it shows the level.</summary>
    private void MpProgress(MusicPlayer music, MusicTrack? track, bool frenzy, bool rendering)
    {
        var pos = ImGui.GetCursorScreenPos();
        var size = new float2(ImGui.GetContentRegionAvail().X, 24 * _scale);
        if (frenzy)
        {
            ImGui.ProgressBar(music.Frenzy.Level / (float)FrenzyMeter.MaxLevel, size, $"Level {music.Frenzy.Level} of {FrenzyMeter.MaxLevel} - loops until you stop it");
            return;
        }
        if (track == null)
        {
            ImGui.ProgressBar(0f, size, ""u8);
            return;
        }
        var seconds = (float)track.Seconds;
        var at = _mpSeeking ? _mpSeekTo : Math.Clamp((float)music.JukeboxPosition, 0f, seconds);
        if (rendering)
            ImGui.ProgressBar(at / seconds, size, "Rendering..."u8);
        else
        {
            int a = (int)at, b = (int)seconds;
            ImGui.ProgressBar(at / seconds, size, $"{a / 60}:{a % 60:00} / {b / 60}:{b % 60:00}");
        }
        ImGui.SetCursorScreenPos(pos);
        ImGui.InvisibleButton("##mpseek"u8, size);
        if (ImGui.IsItemActive())
        {
            _mpSeeking = true;
            _mpSeekTo = Math.Clamp((ImGui.GetIO().MousePos.X - pos.X) / size.X, 0f, 1f) * seconds;
        }
        if (ImGui.IsItemDeactivated() && _mpSeeking)
        {
            _mpSeeking = false;
            music.JukeboxPlay(track, Math.Clamp(track.BarAt(_mpSeekTo), 0, track.TotalBars - 1), samePlay: true);
        }
    }
}
