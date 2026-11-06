using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// What this peer does about the world's Video Screens and Music Players (docs/sandbox/MEDIA.md). A few times a second it looks for parts that are
/// playing within earshot, gives the nearest <see cref="MaxPages"/> links a browser page and the nearest game track the renderer, and keeps each page on
/// the server's clock. Every frame it only moves their sound. With nothing playing nearby a scan is a walk over the bodies with media parts,
/// the browser isn't running and no pictures are copied. Each peer does this for itself: the server only holds the list and the clock (<see cref="CMedia"/>).
/// </summary>
public static class MediaDirector
{
    /// <summary>Browser pages at once. Four 720p VP9 decodes in software is about what a gaming PC gives a side show; the rest wait, with a line saying so.</summary>
    public const int MaxPages = 4;

    /// <summary>Speakers heard at once, nearest first. Each is one more OpenAL source on a stream already decoded, so the cost is OpenAL's; this is about what a mix keeps clear.</summary>
    public const int MaxSpeakers = 8;

    private const double ScanEvery = 0.25, SyncEvery = 1, SeekEvery = 10, QuitAfter = 20;
    private const float SeekDrift = 2.5f, FadeShare = 0.2f; // seconds off the clock before a page seeks; the last share of a part's range fades to nothing

    private struct Page
    {
        public bool Open;
        public ushort Body, Part;
        public uint Seq;
        public string Entry;
        public MediaState State;
        public double NextSync, NextSeek;
        public bool EndSent;
        public float Near, Hear;
        public byte Mark;
        public double HandsUntil; // this peer's hands were on the page: until then the page leads and the server follows
    }

    private struct Near
    {
        public ushort Body, Part;
        public float Distance;
    }

    private static readonly Page[] Pages = new Page[MaxPages];
    private static readonly Near[] Closest = new Near[MaxPages];
    private static readonly Near[] Heard = new Near[MaxSpeakers];
    private static readonly ushort[] HeardBody = new ushort[MaxSpeakers], HeardPart = new ushort[MaxSpeakers];
    private static int _heard;
    private static readonly long[] NextLevelLog = new long[MaxPages + 1 + MaxSpeakers];
    private static int _closest, _waiting;
    private static double _nextScan, _idleSince = double.NaN;
    private static bool _trackOn;
    private static ushort _trackBody, _trackPart;
    private static float _trackNear, _trackHear;

    /// <summary>A peer's own switch: Settings > Audio.</summary>
    public static bool Others = true;

    /// <summary>Master level for this PC's screens, on top of each part's Volume.</summary>
    public static float Level = 1;

    public static int LivePages
    {
        get
        {
            var n = 0;
            foreach (ref var p in Pages.AsSpan())
                if (p.Open)
                    n++;
            return n;
        }
    }

    public static int PageOf(ushort body, int part)
    {
        for (var i = 0; i < Pages.Length; i++)
            if (Pages[i].Open && Pages[i].Body == body && Pages[i].Part == part)
                return i;
        return -1;
    }

    /// <summary>The part is a link this PC doesn't play by its own choice.</summary>
    public static bool Hidden(CMedia m, byte me) => !Others && m.By != me;

    /// <summary>Each frame of the windowed game, Sandbox or not (<paramref name="g"/> null: nothing's world).</summary>
    public static void Frame(Game? g, Audio audio, double now)
    {
        VideoFeeds.Upload(now);
        var world = g is { SandboxOn: true } ? g.Constructs : null;
        if (now >= _nextScan)
        {
            _nextScan = now + ScanEvery;
            Scan(g, world, audio, now);
        }
        if (world == null)
            return;
        for (var i = 0; i < Pages.Length; i++)
            if (Pages[i].Open)
                Place(world, audio, g!, i, Pages[i].Body, Pages[i].Part, Pages[i].Near, Pages[i].Hear);
        if (_trackOn)
            Place(world, audio, g!, Audio.MediaTrackVoice, _trackBody, _trackPart, _trackNear, _trackHear);
        for (var k = 0; k < _heard; k++)
            PlaceSpeaker(world, audio, g!, k);
    }

    private static void PlaceSpeaker(ConstructWorld world, Audio audio, Game g, int k)
    {
        if (world.Get(Heard[k].Body) is not { } c || Heard[k].Part >= c.PartCount || world.Get(HeardBody[k]) is not { } src || HeardPart[k] >= src.PartCount)
            return;
        ref var p = ref c.Parts[Heard[k].Part];
        var at = ConstructRender.DrawnPoint(c, Heard[k].Part, Vector3.Zero, g.TickAlpha, g.IsServer);
        var front = Vector3.Normalize(ConstructRender.DrawnPoint(c, Heard[k].Part, Rot24Front, g.TickAlpha, g.IsServer) - at);
        var to = audio.ListenerPosition - at;
        var distance = to.Length();
        var angle = distance > 1e-3f ? MathF.Acos(Math.Clamp(Vector3.Dot(front, to / distance), -1, 1)) * 180f / MathF.PI : 0;
        ref var sp = ref src.Parts[HeardPart[k]];
        var level = ConstructSpeakers.Shape(p, p.D).Level(distance, angle);
        audio.MediaSpeakerPlace(k, at, Level * ConstructConfig.Get(sp, sp.D, CfgKey.Volume, 1) * ConstructConfig.Get(p, p.D, CfgKey.Volume, 1) * level);
    }

    private static readonly Vector3 Rot24Front = -Vector3.UnitZ; // (a speaker faces its -Z)

    private static void Place(ConstructWorld world, Audio audio, Game g, int voice, ushort body, ushort part, float near, float hear)
    {
        if (world.Get(body) is not { } c || part >= c.PartCount)
            return;
        ref var p = ref c.Parts[part];
        var at = ConstructRender.DrawnPoint(c, part, Vector3.Zero, g.TickAlpha, g.IsServer);
        var volume = ConstructConfig.Get(p, p.D, CfgKey.Volume, 1);
        var distance = Vector3.Distance(at, audio.ListenerPosition);
        var fade = EdgeFade(distance, hear);
        audio.MediaPlace(voice, at, Level * volume * fade, near, hear);
        if (Environment.TickCount64 >= NextLevelLog[voice])
        {
            NextLevelLog[voice] = Environment.TickCount64 + 5000;
            Console.WriteLine($"[media] level voice {voice} (hop 7): part volume {volume:0.00} x screens volume {Level:0.00} x edge fade {fade:0.00} x Media bus {audio.Mix.Gain(MixBus.Media):0.00}, {distance:0.0} m away, heard to {hear:0} m");
        }
    }

    /// <summary>The last of a part's range: down to nothing, so the page can go without a click of silence falling on a sound still heard.</summary>
    public static float EdgeFade(float distance, float hear)
    {
        var start = hear * (1 - FadeShare);
        return distance <= start ? 1 : Math.Clamp((hear - distance) / (hear - start), 0, 1);
    }

    private static void Scan(Game? g, ConstructWorld? world, Audio audio, double now)
    {
        MediaBrowser.Tick();
        _closest = 0;
        _waiting = 0;
        Construct? track = null;
        var trackPart = 0;
        var trackDistance = float.MaxValue;
        if (world != null)
        {
            var ear = audio.ListenerPosition;
            var me = g!.Local?.Id ?? 255;
            foreach (var c in world.Bodies)
                for (var k = 0; k < c.MediaParts.Length; k++)
                {
                    int i = c.MediaParts[k];
                    if (i >= c.PartCount)
                        continue;
                    ref var p = ref c.Parts[i];
                    if (p.Broken || p.Media is not { State: not MediaState.Stopped } m || m.Entry is not { } line || !ConstructPower.On(c, i))
                        continue;
                    var distance = Vector3.Distance(ConstructRender.DrawnPoint(c, i, Vector3.Zero, g.TickAlpha, g.IsServer), ear);
                    if (distance > p.D.MediaHear)
                        continue;
                    if (!MediaEntry.IsLink(line))
                    {
                        if (distance < trackDistance)
                        {
                            track = c;
                            trackPart = i;
                            trackDistance = distance;
                        }
                    }
                    else if (!Hidden(m, me))
                        Keep(c.Id, (ushort)i, distance);
                }
            // A speaker near is reason enough for its player to play, however far off that is.
            _heard = 0;
            foreach (var c in world.Bodies)
                for (var k = 0; k < c.SpeakerParts.Length; k++)
                {
                    int i = c.SpeakerParts[k];
                    if (i >= c.PartCount)
                        continue;
                    ref var p = ref c.Parts[i];
                    if (p.Broken || !ConstructPower.On(c, i) || ConstructSpeakers.Source(world, c, i, out var from) is not { } src)
                        continue;
                    ref var sp = ref src.Parts[from];
                    if (sp.Broken || sp.Media is not { State: not MediaState.Stopped } m || m.Entry is not { } line || !ConstructPower.On(src, from))
                        continue;
                    var distance = Vector3.Distance(ConstructRender.DrawnPoint(c, i, Vector3.Zero, g.TickAlpha, g.IsServer), ear);
                    if (distance > ConstructConfig.Get(p, p.D, CfgKey.HearRange, 40))
                        continue;
                    if (!MediaEntry.IsLink(line))
                    {
                        if (distance < trackDistance)
                        {
                            track = src;
                            trackPart = from;
                            trackDistance = distance;
                        }
                    }
                    else if (!Hidden(m, me))
                        Keep(src.Id, (ushort)from, distance);
                    else
                        continue;
                    KeepSpeaker(c.Id, (ushort)i, src.Id, (ushort)from, distance);
                }
        }
        else
            _heard = 0;

        // Pages whose part isn't among the nearest any more, or has moved on to another track, go first; and all of them if the browser went.
        var browser = MediaBrowser.Problem == null;
        for (var i = 0; i < Pages.Length; i++)
        {
            ref var page = ref Pages[i];
            if (!page.Open)
                continue;
            var c = world?.Get(page.Body);
            var keep = browser && c != null && page.Part < c.PartCount && Listed(page.Body, page.Part) && c.Parts[page.Part].Media is { } m && m.Seq == page.Seq && m.Entry == page.Entry;
            if (!keep)
                ClosePage(audio, i);
        }
        for (var n = 0; n < _closest; n++)
            if (PageOf(Closest[n].Body, Closest[n].Part) < 0)
                OpenPage(g!, world!, audio, Closest[n].Body, Closest[n].Part, now);
        for (var i = 0; i < Pages.Length; i++)
            if (Pages[i].Open)
                Sync(g!, world!, i, now);

        if (track != null)
        {
            ref var p = ref track.Parts[trackPart];
            var m = p.Media!;
            _trackOn = true;
            _trackBody = track.Id;
            _trackPart = (ushort)trackPart;
            _trackNear = p.D.MediaNear;
            _trackHear = p.D.MediaHear;
            audio.MediaTrack(MediaEntry.Track(m.Entry!), m.Seq, m.Position(g!.ServerTick), m.State == MediaState.Paused);
        }
        else if (_trackOn)
        {
            _trackOn = false;
            audio.MediaTrack(null, 0, 0, false);
        }

        // Speakers follow their players' voices, whichever those are now. A cut link, a stopped player or one past the nearest is silence.
        for (var k = 0; k < MaxSpeakers; k++)
        {
            var follows = -1;
            if (k < _heard)
                follows = _trackOn && HeardBody[k] == _trackBody && HeardPart[k] == _trackPart ? Audio.MediaTrackVoice : PageOf(HeardBody[k], HeardPart[k]);
            audio.MediaSpeaker(k, follows);
        }

        // Nothing has wanted the browser for a while: it goes, and what it held.
        if (LivePages > 0)
            _idleSince = double.NaN;
        else if (MediaBrowser.Running)
        {
            if (double.IsNaN(_idleSince))
                _idleSince = now;
            else if (now - _idleSince > QuitAfter)
            {
                MediaBrowser.Quit();
                _idleSince = double.NaN;
            }
        }
    }

    private static bool Listed(ushort body, ushort part)
    {
        for (var n = 0; n < _closest; n++)
            if (Closest[n].Body == body && Closest[n].Part == part)
                return true;
        return false;
    }

    /// <summary>Into the list of the nearest, in order. One pushed off the end is waiting for a page.</summary>
    private static void Keep(ushort body, ushort part, float distance)
    {
        for (var n = 0; n < _closest; n++)
            if (Closest[n].Body == body && Closest[n].Part == part)
            {
                if (Closest[n].Distance <= distance)
                    return;
                Array.Copy(Closest, n + 1, Closest, n, _closest - n - 1); // (nearer by a speaker: it goes in again at its new place)
                _closest--;
                break;
            }
        var at = _closest;
        while (at > 0 && Closest[at - 1].Distance > distance)
            at--;
        if (at >= MaxPages)
        {
            _waiting++;
            return;
        }
        if (_closest == MaxPages)
            _waiting++;
        else
            _closest++;
        for (var n = _closest - 1; n > at; n--)
            Closest[n] = Closest[n - 1];
        Closest[at] = new Near { Body = body, Part = part, Distance = distance };
    }

    private static void KeepSpeaker(ushort body, ushort part, ushort from, ushort fromPart, float distance)
    {
        var at = _heard;
        while (at > 0 && Heard[at - 1].Distance > distance)
            at--;
        if (at >= MaxSpeakers)
            return;
        if (_heard < MaxSpeakers)
            _heard++;
        for (var n = _heard - 1; n > at; n--)
        {
            Heard[n] = Heard[n - 1];
            HeardBody[n] = HeardBody[n - 1];
            HeardPart[n] = HeardPart[n - 1];
        }
        Heard[at] = new Near { Body = body, Part = part, Distance = distance };
        HeardBody[at] = from;
        HeardPart[at] = fromPart;
    }

    private static void OpenPage(Game g, ConstructWorld world, Audio audio, ushort body, ushort part, double now)
    {
        var free = Array.FindIndex(Pages, x => !x.Open);
        if (free < 0 || world.Get(body) is not { } c)
            return;
        ref var p = ref c.Parts[part];
        var m = p.Media!;
        var d = p.D;
        if (!MediaBrowser.Open(free, MediaEntry.VideoId(m.Entry!), m.Position(g.ServerTick), d.MediaWidth, d.MediaHeight))
            return;
        VideoFeeds.Forget(free);
        Pages[free] = new Page
        {
            Open = true, Body = body, Part = part, Seq = m.Seq, Entry = m.Entry!, State = MediaState.Playing, NextSync = now + 3, NextSeek = now + 3, // (a joiner whose tick hadn't come in yet opened at 0)
            Near = d.MediaNear, Hear = d.MediaHear,
        };
        audio.MediaPage(free, MediaBrowser.Block(free));
    }

    private static void ClosePage(Audio audio, int i)
    {
        Pages[i].Open = false;
        Pages[i].Entry = "";
        audio.MediaPage(i, null);
        MediaBrowser.Close(i);
        VideoFeeds.Forget(i);
    }

    /// <summary>Holds a page to the server's word: paused as it is, near its clock, and the end of a track reported.</summary>
    private static void Sync(Game g, ConstructWorld world, int i, double now)
    {
        ref var page = ref Pages[i];
        if (world.Get(page.Body) is not { } c || c.Parts[page.Part].Media is not { } m)
            return;
        var r = MediaBrowser.Report(i);
        if (now < page.HandsUntil)
        {
            page.Mark = m.Mark;
            page.State = m.State;
            FromPage(g, ref page, m, r);
            return;
        }
        if (m.Mark != page.Mark)
        {
            page.Mark = m.Mark;
            page.NextSync = page.NextSeek = 0;
        }
        if (m.State != page.State)
        {
            page.State = m.State;
            page.NextSync = 0;
        }
        if (now < page.NextSync)
            return;
        page.NextSync = now + SyncEvery;
        var state = r.State;
        if (m.State == MediaState.Paused)
        {
            if (state == MediaReport.Playing)
                MediaBrowser.Pause(i);
            if (r.Duration > 0 && MathF.Abs(r.Time - m.PausedAt) > SeekDrift && now >= page.NextSeek)
            {
                page.NextSeek = now + 1;
                MediaBrowser.Seek(i, m.PausedAt);
            }
            return;
        }
        if (state is MediaReport.Paused or MediaReport.Cued)
            MediaBrowser.Play(i);
        var failed = r.Error != 0 && m.By == g.Local?.Id && m.List.Length > 1;
        if ((state == MediaReport.Ended || failed) && !page.EndSent)
        {
            page.EndSent = true;
            g.SandboxMedia(page.Body, page.Part, MediaOp.Ended, 0, page.Seq);
            return;
        }
        // (a live stream has no length, and its clock isn't this one)
        var clock = m.Position(g.ServerTick);
        if (state == MediaReport.Playing && r.Duration > 0 && clock < r.Duration && MathF.Abs(r.Time - clock) > SeekDrift && now >= page.NextSeek)
        {
            page.NextSeek = now + SeekEvery; // (an advert holds the player's clock back, and a seek does nothing until it is over)
            MediaBrowser.Seek(i, clock);
        }
    }

    /// <summary>
    /// The other way round: someone here is using YouTube's own controls, so what the page does goes to the server, and from there to every peer. Volume stays on this page.
    /// </summary>
    private static void FromPage(Game g, ref Page page, CMedia m, MediaReport r)
    {
        var state = r.State;
        if (state == MediaReport.Paused && m.State == MediaState.Playing)
            g.SandboxMedia(page.Body, page.Part, MediaOp.Pause);
        else if (state == MediaReport.Playing && m.State == MediaState.Paused)
            g.SandboxMedia(page.Body, page.Part, MediaOp.Resume);
        if (state is MediaReport.Playing or MediaReport.Paused && r.Duration > 0 && MathF.Abs(r.Time - m.Position(g.ServerTick)) > SeekDrift * 0.6f)
            g.SandboxMedia(page.Body, page.Part, MediaOp.Seek, seq: (uint)(r.Time * 1000));
    }

    private const double HandsLead = 3; // seconds the page leads after a click

    public static void PageMove(int page, int x, int y, bool drag) => MediaBrowser.Block(page)?.Mouse(MediaBlock.MouseMove, x, y, drag ? 1 : 0);

    public static void PageLeave(int page) => MediaBrowser.Block(page)?.Mouse(MediaBlock.MouseLeave, 0, 0, 0);

    public static void PageButton(int page, int x, int y, bool down, double now)
    {
        if (now > 0)
            Pages[page].HandsUntil = now + HandsLead;
        MediaBrowser.Block(page)?.Mouse(down ? MediaBlock.MouseDown : MediaBlock.MouseUp, x, y, 0);
    }

    public static void PageWheel(int page, int x, int y, int delta, double now)
    {
        Pages[page].HandsUntil = now + HandsLead;
        MediaBrowser.Block(page)?.Mouse(MediaBlock.MouseWheel, x, y, delta);
    }

    /// <summary>What a Video Screen's or Music Player's panel shows on this PC.</summary>
    public static void Fill(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (!ConstructPower.On(c, part))
        {
            card.State = PanelState.Fault;
            card.Add(0, "No power");
            return;
        }
        if (p.Media is not { State: not MediaState.Stopped } m || m.Entry is not { } line)
        {
            card.State = PanelState.Idle;
            card.Add(0, d.Name);
            var n = p.Media?.List.Length ?? 0;
            if (n == 0)
                card.Add(1, "Nothing in the list");
            else
                card.Add(1, n).Add(1, n == 1 ? " track, stopped" : " tracks, stopped");
            return;
        }
        if (MediaEntry.Track(line) is { } track)
        {
            card.State = m.State == MediaState.Paused ? PanelState.Idle : PanelState.Ok;
            card.Add(0, m.State == MediaState.Paused ? "Paused" : "Now playing");
            card.Add(1, track.Name);
            Buttons(card, panel, m);
            return;
        }
        var page = PageOf(c.Id, part);
        if (panel.Touch.Length > 0)
            Buttons(card, panel, m);
        if (page < 0)
        {
            card.State = PanelState.Wants;
            card.Add(0, d.Name);
            Words(card, panel, Why(m), 1);
            return;
        }
        var r = MediaBrowser.Report(page);
        card.Video = VideoFeeds.Request(page);
        card.VideoWidth = d.MediaWidth;
        card.VideoHeight = d.MediaHeight;
        card.VideoRect = panel.Picture;
        card.State = PanelState.Ok;
        if (r.Error != 0)
        {
            card.Video = 0;
            card.State = PanelState.Fault;
            card.Add(0, "Can't play this");
            Words(card, panel, ErrorText(r.Error), 1);
        }
        else if (card.Video == 0)
            card.Add(0, "Loading");
        else if (panel.Touch.Length > 0)
            card.Add(TitleRow(panel), Label(line));
    }

    /// <summary>The play button's word follows the state. Every other label is the part file's.</summary>
    private static void Buttons(PanelCard card, PanelDef panel, CMedia? m)
    {
        for (var i = 0; i < panel.Touch.Length; i++)
            if (panel.Touch[i].Action == "media.toggle")
                card.TouchLabels[i] = m is { State: MediaState.Playing } ? "Pause" : "Play";
    }

    /// <summary>The row under the picture, for what's playing.</summary>
    private static int TitleRow(PanelDef panel) => Math.Clamp((int)MathF.Ceiling((1 - panel.Picture.Y) * panel.Rows), 0, panel.Rows - 1);

    private static void Words(PanelCard card, PanelDef panel, string text, int line)
    {
        var at = line;
        var column = 0;
        card.Wrap(text, PanelRender.Columns(panel.Size, panel.Rows), line, panel.Rows - 1, 0, ref at, ref column);
    }

    private static string Why(CMedia m)
    {
        if (MediaBrowser.Problem is { } problem)
            return problem;
        return !Others ? "Other players' videos are off" : _closest >= MaxPages ? $"{MaxPages} screens already playing" : "Waiting";
    }

    private static string ErrorText(int code) => code switch
    {
        2 => "bad video id",
        5 => "the player failed",
        100 => "gone or private",
        101 or 150 => "embedding not allowed",
        153 => "no referrer (error 153)",
        _ => "error " + code,
    };

    /// <summary>The look line's word on a part, or "". Made when what it says changes.</summary>
    public static string Look(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        if (p.Media is not { State: not MediaState.Stopped } m || m.Entry is not { } line)
        {
            var n = p.Media?.List.Length ?? 0;
            return n == 0 ? "nothing in its list" : $"{n} in its list";
        }
        if (MediaEntry.Track(line) is { } track)
            return track.Name;
        if (PageOf(c.Id, part) is var page and >= 0)
        {
            var r = MediaBrowser.Report(page);
            return r.Error != 0 ? ErrorText(r.Error) : r.Title.Length > 0 ? r.Title : "loading";
        }
        return Why(m);
    }

    /// <summary>The title of a list line this PC knows of, for the panel; else the line's own words.</summary>
    public static string Label(string line)
    {
        if (MediaEntry.Track(line) is { } t)
            return t.Name;
        for (var i = 0; i < Pages.Length; i++)
            if (Pages[i].Open && Pages[i].Entry == line && MediaBrowser.Report(i).Title is { Length: > 0 } title)
                return title;
        return "YouTube " + MediaEntry.VideoId(line);
    }

    /// <summary>The game is leaving: pages closed, browser gone, sound off.</summary>
    public static void Shutdown(Audio audio)
    {
        for (var i = 0; i < Pages.Length; i++)
            if (Pages[i].Open)
                ClosePage(audio, i);
        MediaBrowser.Shutdown();
    }
}
