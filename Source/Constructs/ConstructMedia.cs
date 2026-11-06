using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

public enum MediaState : byte
{
    Stopped,
    Playing,
    Paused,
}

/// <summary>What a player asks a media part to do (<see cref="CActKind.Media"/>).</summary>
public enum MediaOp : byte
{
    Add, // the entry
    Remove, // the line
    Up, // the line
    Down, // the line
    Play, // from the line
    Pause,
    Resume,
    Stop,
    Next,
    Previous,
    Ended, // a peer's player came to the end of the track numbered by the seq, or (from whoever started it) can't play it
}

/// <summary>
/// The server's word on a Video Screen or Music Player: the list, what plays and when it started, in server ticks (so a joiner's browser
/// seeks to the same place). Made on the first edit: a part that never played has none. It moves with the part, as <see cref="CPart.Text"/> does.
/// </summary>
public sealed class CMedia
{
    public string[] List = [];
    public MediaState State;
    public byte Index; // the line playing
    public byte By = 255; // who started it (Settings.MediaOthers)
    public uint StartTick; // the tick at which this track was at 0 seconds
    public uint Seq; // goes up when another track starts, or this one starts again: a report of its end names it
    public float PausedAt; // seconds into the track, while paused

    public string? Entry => Index < List.Length ? List[Index] : null;

    public float Position(uint tick) => State switch
    {
        MediaState.Playing => tick >= StartTick ? (tick - StartTick) * World.TickDt : 0,
        MediaState.Paused => PausedAt,
        _ => 0,
    };
}

/// <summary>
/// Video Screens and Music Players on the server and in the files (docs/sandbox/MEDIA.md). What a peer does about them, its browser and its
/// sound, is <see cref="MediaDirector"/>'s. The list rides in CBody's tail, a blueprint step's <c>"media"</c> list and a world save's settings,
/// three characters a float under "media.0", "media.1"... as a sign's text does; what is playing isn't saved, and a world comes up stopped.
/// </summary>
public static class ConstructMedia
{
    /// <summary>The most lines in a list. Lines are short and a body's whole list goes out on every change, so this is about what a person scrolls through on a small panel.</summary>
    public const int MaxList = 32;

    private const char Separator = '\n';
    private const string SavePrefix = "media.";

    /// <summary>A part file's <c>"media"</c> key.</summary>
    public static void Parse(ConstructPartDef d, JsonElement root, string file)
    {
        if (ConstructParts.Opt(root, "media") is not { } m)
            return;
        ConstructParts.Known(m, ["hear", "near", "width", "height"], $"{file}, media");
        d.IsMedia = true;
        d.MediaHear = ConstructParts.Num(ConstructParts.Need(m, "hear", $"{file}, media"), file, "media.hear");
        d.MediaNear = ConstructParts.Num(ConstructParts.Need(m, "near", $"{file}, media"), file, "media.near");
        ConstructParts.Range(d.MediaHear, 5, 500, file, "media.hear");
        ConstructParts.Range(d.MediaNear, 0.5f, d.MediaHear, file, "media.near");
        d.MediaWidth = ConstructParts.Opt(m, "width") is { } w ? (int)ConstructParts.Num(w, file, "media.width") : MediaBlock.MaxWidth;
        d.MediaHeight = ConstructParts.Opt(m, "height") is { } h ? (int)ConstructParts.Num(h, file, "media.height") : MediaBlock.MaxHeight;
        ConstructParts.Range(d.MediaWidth, 200, MediaBlock.MaxWidth, file, "media.width");
        ConstructParts.Range(d.MediaHeight, 200, MediaBlock.MaxHeight, file, "media.height");
    }

    /// <summary>Clamps and cleans a list as it comes in from a file or a blueprint: bad lines are left out.</summary>
    public static void SetList(ref CPart p, IEnumerable<string> lines)
    {
        var list = new List<string>();
        foreach (var line in lines)
            if (MediaEntry.Clean(line) is { } e && list.Count < MaxList)
                list.Add(e);
        p.Media = list.Count == 0 ? null : new CMedia { List = list.ToArray() };
    }

    // ---- The wire ---------------------------------------------------------------------------------------------

    /// <summary>CBody's tail: every list and what it is playing.</summary>
    public static void Write(PacketWriter w, Construct c)
    {
        var at = w.Length;
        w.U16(0);
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Media is { } m)
            {
                w.U16((ushort)i).U8((byte)m.State).U8(m.Index).U8(m.By).U32(m.StartTick).U32(m.Seq).F32(m.PausedAt).U8((byte)m.List.Length);
                foreach (var line in m.List)
                    w.Str(line);
                n++;
            }
        w.PatchU16(at, (ushort)n);
    }

    public static void Read(ref PacketReader r, Construct c)
    {
        int n = r.U16();
        for (var k = 0; k < n; k++)
        {
            var part = r.U16();
            var state = r.U8();
            var m = new CMedia { Index = r.U8(), By = r.U8(), StartTick = r.U32(), Seq = r.U32(), PausedAt = r.F32() };
            int lines = r.U8();
            if (part >= c.PartCount || !c.Parts[part].D.IsMedia || state > (byte)MediaState.Paused || lines > MaxList || !float.IsFinite(m.PausedAt))
                throw new MalformedPacketException($"media on part {part}");
            m.State = (MediaState)state;
            m.List = new string[lines];
            for (var l = 0; l < lines; l++)
                m.List[l] = MediaEntry.Clean(r.Text(MediaEntry.MaxChars)) ?? throw new MalformedPacketException("a media line that isn't one");
            c.Parts[part].Media = m;
        }
    }

    // ---- The files --------------------------------------------------------------------------------------------

    public static string SaveKey(int chunk) => SavePrefix + chunk.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The whole list as one text, for a world save's chunks.</summary>
    public static string SaveText(in CPart p) => p.Media is { } m ? string.Join(Separator, m.List) : "";

    /// <summary>A world save's setting that may be a chunk of a list. They come in order and each lands at its own place, so this can run for any chunk.</summary>
    public static void LoadChunk(ref CPart p, string key, float v)
    {
        if (!key.StartsWith(SavePrefix, StringComparison.Ordinal) || !int.TryParse(key.AsSpan(SavePrefix.Length), out var chunk) || chunk is < 0 or >= MaxList * MediaEntry.MaxChars)
            return;
        p.MediaLoad ??= new StringBuilder();
        var b = p.MediaLoad;
        while (b.Length < chunk * 3)
            b.Append('\0');
        var bits = (int)v;
        for (var k = 0; k < 3; k++)
        {
            var ch = (char)(bits >> (7 * k) & 0x7F);
            if (ch != 0)
                b.Append(ch);
        }
    }

    /// <summary>After a world file's settings: the chunks into a list.</summary>
    public static void LoadDone(ref CPart p)
    {
        if (p.MediaLoad is not { } b)
            return;
        SetList(ref p, b.ToString().Split(Separator, StringSplitOptions.RemoveEmptyEntries));
        p.MediaLoad = null;
    }

    /// <summary>A blueprint step's "media" value, or "".</summary>
    public static string ToJson(in CPart p) => p.Media is { List.Length: > 0 } m ? JsonSerializer.Serialize(m.List) : "";

    // ---- The server -------------------------------------------------------------------------------------------

    /// <summary>Starts a line from the top.</summary>
    private static void Start(CMedia m, int line, byte by, uint tick)
    {
        m.Index = (byte)line;
        m.State = MediaState.Playing;
        m.By = by;
        m.StartTick = tick;
        m.PausedAt = 0;
        m.Seq++;
    }

    /// <summary>Server: the line after this one, round to the top with Loop on, or stop.</summary>
    private static void Advance(CMedia m, bool loop, byte by, uint tick)
    {
        if (m.Index + 1 < m.List.Length)
            Start(m, m.Index + 1, by, tick);
        else if (loop)
            Start(m, 0, by, tick);
        else
            Halt(m);
    }

    private static void Halt(CMedia m)
    {
        m.State = MediaState.Stopped;
        m.Index = 0;
        m.PausedAt = 0;
        m.Seq++;
    }

    /// <summary>Server: one thing a player asked. Null, or why not. Worked out on the list and nothing else, so it is the same in a test.</summary>
    public static string? Apply(ref CPart p, ConstructPartDef d, MediaOp op, int line, uint seq, string entry, byte player, uint tick)
    {
        var loop = ConstructConfig.Get(p, d, CfgKey.Loop, 0) >= 0.5f;
        if (op == MediaOp.Add)
        {
            if (MediaEntry.Clean(entry) is not { } clean)
                return "only YouTube links and the game's own tracks play here";
            var m0 = p.Media ??= new CMedia();
            if (m0.List.Length >= MaxList)
                return $"a list holds {MaxList} tracks";
            m0.List = [.. m0.List, clean];
            return null;
        }
        if (p.Media is not { } m)
            return op == MediaOp.Ended ? null : "the list is empty";
        switch (op)
        {
            case MediaOp.Remove when line < m.List.Length:
                m.List = [.. m.List[..line], .. m.List[(line + 1)..]];
                if (line == m.Index && m.State != MediaState.Stopped)
                {
                    if (line < m.List.Length)
                        Start(m, line, player, tick);
                    else if (loop && m.List.Length > 0)
                        Start(m, 0, player, tick);
                    else
                        Halt(m);
                }
                else if (line < m.Index)
                    m.Index--;
                return null;
            case MediaOp.Up when line > 0 && line < m.List.Length:
            case MediaOp.Down when line + 1 < m.List.Length:
            {
                var other = op == MediaOp.Up ? line - 1 : line + 1;
                (m.List[line], m.List[other]) = (m.List[other], m.List[line]);
                if (m.Index == line)
                    m.Index = (byte)other;
                else if (m.Index == other)
                    m.Index = (byte)line;
                return null;
            }
            case MediaOp.Play when line < m.List.Length:
                Start(m, line, player, tick);
                return null;
            case MediaOp.Pause when m.State == MediaState.Playing:
                m.PausedAt = m.Position(tick);
                m.State = MediaState.Paused;
                return null;
            case MediaOp.Resume when m.State == MediaState.Paused:
                m.StartTick = tick - (uint)(m.PausedAt * World.TickRate);
                m.State = MediaState.Playing;
                return null;
            case MediaOp.Resume when m.State == MediaState.Stopped && m.List.Length > 0:
                Start(m, 0, player, tick);
                return null;
            case MediaOp.Stop when m.State != MediaState.Stopped:
                Halt(m);
                return null;
            case MediaOp.Next when m.List.Length > 0:
                Advance(m, loop: true, player, tick);
                return null;
            case MediaOp.Previous when m.List.Length > 0:
                Start(m, m.Index > 0 ? m.Index - 1 : m.List.Length - 1, player, tick);
                return null;
            case MediaOp.Ended when seq == m.Seq && m.State == MediaState.Playing:
                Advance(m, loop, m.By, tick);
                return null;
            case MediaOp.Ended:
                return null; // (a second peer's report of the same end, or a late one)
            default:
                return "nothing to do";
        }
    }

    /// <summary>
    /// Server, a second at a time: the game's own tracks end by the clock. Links end by a peer's report, since only a browser knows how long a video is.
    /// </summary>
    public static void Tick(ConstructWorld world, uint tick)
    {
        foreach (var c in world.Bodies)
            for (var k = 0; k < c.MediaParts.Length; k++)
            {
                int i = c.MediaParts[k];
                if (i >= c.PartCount)
                    continue;
                ref var p = ref c.Parts[i];
                if (p.Media is not { State: MediaState.Playing } m || m.Entry is not { } line || MediaEntry.IsLink(line))
                    continue;
                if (MediaEntry.Track(line) is { } t && m.Position(tick) < t.Seconds)
                    continue;
                Advance(m, ConstructConfig.Get(p, p.D, CfgKey.Loop, 0) >= 0.5f, m.By, tick);
                world.StructureDirty.Add(c.Id);
            }
    }
}

public sealed partial class ConstructEdits
{
    /// <summary>Server: a player at a media part's panel, under the configure tool's rule (<see cref="PartText"/>), or a peer's report that a track ended.</summary>
    public string? MediaAct(ConstructWorld world, byte player, Vector3 eye, ushort body, ushort part, MediaOp op, int line, uint seq, string entry, uint tick, string? refusal)
    {
        if (world.Get(body) is not { } c || part >= c.PartCount)
            return op == MediaOp.Ended ? null : "that part is gone";
        var d = c.DefOf(part);
        if (!d.IsMedia)
            return op == MediaOp.Ended ? null : $"a {d.Name} doesn't play anything";
        if (op != MediaOp.Ended)
        {
            if (Contested(c, player) is { } defended)
                return defended;
            if (refusal != null)
                return refusal;
            if (Vector3.Distance(eye, ReachPoint(c, part, eye)) > ConstructConfig.Reach)
                return "too far away";
        }
        var before = c.Parts[part].Media is { } was ? (was.Seq, was.State, was.List.Length) : default;
        if (ConstructMedia.Apply(ref c.Parts[part], d, op, line, seq, entry, player, tick) is { } why)
            return why;
        if (op != MediaOp.Ended || c.Parts[part].Media is { } now && (now.Seq, now.State, now.List.Length) != before)
            world.StructureDirty.Add(c.Id);
        return null;
    }
}
