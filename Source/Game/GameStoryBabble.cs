using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The story people's murmur (docs/story/STORY.md "Lines"): while a line said aloud (<see cref="ChatFlags.Say"/>: a
/// person's words, the talk panel's answers, the induction's warden and guards) is shown over someone's head, they mumble
/// it in sung syllables ("mamama", a nasal "ngangaga"; Audio/SynthBabble.cs) at their mouth, and their jaw and head move
/// with it (<see cref="TalkMouth"/>, drawn by the body model). Each person has their own voice, picked from their name:
/// a pitch, a pace and the syllables they lean on. The syllables follow the text: one for every two or three letters,
/// a pause at a comma, a longer one at a full stop, a rise for a question, a lift for capitals and "!", and the note
/// falls a little through each sentence. Every machine does this for itself from the chat lines it has (host and
/// clients alike; nothing goes on the wire), each frame, without allocating.
/// </summary>
public sealed partial class Game
{
    private const int BabbleMaxSyllables = 60; // a long line murmurs this much, then the mouth stops (the words stay up)
    private const float BabbleGain = 0.55f;

    private int _babSeen; // ChatVersion handled
    private readonly string?[] _babText = new string?[256];
    private readonly int[] _babPos = new int[256], _babCount = new int[256], _babSentence = new int[256];
    private readonly double[] _babNext = new double[256], _babSylAt = new double[256];
    private readonly float[] _babSylLen = new float[256];
    private readonly uint[] _babRng = new uint[256];
    private readonly string?[] _voiceFor = new string?[256]; // the name each id's voice was made from
    private readonly float[] _voicePitch = new float[256], _voiceRate = new float[256];
    private readonly uint[] _voiceSet = new uint[256]; // per voice: the syllables it leans on (bits of Synth.BabbleVariants)
    private int _babActive; // ids with a line going (a quick skip when nobody talks)

    /// <summary>Every frame (host and clients, from the world pass): new lines said aloud start a murmur; murmurs go on.</summary>
    internal void StoryBabbleSounds(double now)
    {
        // New lines (ChatVersion counts every line added; the newest are at the end of Chat).
        var added = ChatVersion - _babSeen;
        _babSeen = ChatVersion;
        if (added < 0 || added > Chat.Count)
            added = Math.Min(Chat.Count, Math.Max(0, added));
        for (var i = Chat.Count - added; i < Chat.Count; i++)
        {
            var c = Chat[i];
            if ((c.Flags & ChatFlags.Say) != 0 && (c.Flags & ChatFlags.System) == 0 && c.From != ChatRules.SystemId && c.Text.Length > 0)
                BabbleStart(c.From, c.Text, now);
        }
        if (_babActive == 0)
            return;

        _babActive = 0;
        for (var id = 0; id < 256; id++)
        {
            var text = _babText[id];
            if (text == null)
            {
                TalkMouth.Open[id] = 0;
                continue;
            }
            if (!Players.TryGetValue((byte)id, out var p) || !p.Alive)
            {
                BabbleStop(id);
                continue;
            }
            _babActive++;
            var mouth = GravityFrames.Above(p.RenderPosition, World.PlayerHeight - 0.25f);
            var guard = 0;
            while (now >= _babNext[id] && _babText[id] != null && guard++ < 4)
                BabbleStep(id, text, mouth, now);
            // The mouth: open through each syllable (a quick open, a softer close).
            var t = (float)((now - _babSylAt[id]) / _babSylLen[id]);
            TalkMouth.Open[id] = t is >= 0 and < 1 ? MathF.Sqrt(MathF.Sin(MathF.PI * t)) : 0;
        }
    }

    /// <summary>A line said aloud by <paramref name="from"/>: their murmur starts again from its first letter.</summary>
    private void BabbleStart(byte from, string text, double now)
    {
        if (!Players.TryGetValue(from, out var p))
            return;
        BabbleVoice(from, p.Name);
        _babText[from] = text;
        _babPos[from] = 0;
        _babCount[from] = 0;
        _babSentence[from] = 0;
        _babNext[from] = now + 0.05;
        _babSylAt[from] = -1;
        _babSylLen[from] = 0.1f;
        _babRng[from] = (uint)text.Length * 2654435761u ^ (uint)from * 40503u ^ 0x9E37u;
        _babActive++;
    }

    private void BabbleStop(int id)
    {
        _babText[id] = null;
        TalkMouth.Open[id] = 0;
    }

    /// <summary>A person's voice from their name (the same on every machine): pitch, pace and favourite syllables.</summary>
    private void BabbleVoice(byte id, string name)
    {
        if (ReferenceEquals(_voiceFor[id], name))
            return;
        _voiceFor[id] = name;
        var h = 2166136261u;
        foreach (var ch in name)
            h = (h ^ ch) * 16777619u;
        uint Next() => h = h * 1664525u + 1013904223u;
        // Pitch: low rumbles to squeaks (0.7 to 1.55 of the base note), pace 9 to 14 syllables a second.
        _voicePitch[id] = 0.7f + (Next() >> 8) / (float)(1 << 24) * 0.85f;
        _voiceRate[id] = 9f + (Next() >> 8) / (float)(1 << 24) * 5f;
        // Four to six favourite syllables, always with "ma" or "nga" among them (the nasal "a" everyone has).
        var set = (Next() & 1) == 0 ? 1u : 2u;
        var want = 4 + (int)(Next() % 3);
        for (var tries = 0; tries < 32 && BitOperations.PopCount(set) < want; tries++)
            set |= 1u << (int)(Next() % Synth.BabbleVariants);
        _voiceSet[id] = set;
    }

    /// <summary>The next bit of the line: a pause at punctuation, or one syllable for the next two or three letters.</summary>
    private void BabbleStep(int id, string text, Vector3 mouth, double now)
    {
        var pos = _babPos[id];
        var rate = _voiceRate[id];
        if (pos >= text.Length || _babCount[id] >= BabbleMaxSyllables)
        {
            BabbleStop(id);
            return;
        }
        var ch = text[pos];
        if (ch is '.' or '!' or '?' or ';' or ':' or ',' or '-')
        {
            var pause = ch is ',' or '-' ? 0.15 : ch is ';' or ':' ? 0.2 : 0.3;
            while (pos < text.Length && !char.IsLetterOrDigit(text[pos]))
                pos++;
            _babPos[id] = pos;
            _babNext[id] = Math.Max(_babNext[id], now - 0.05) + pause * 11.0 / rate;
            if (ch is '.' or '!' or '?')
                _babSentence[id] = 0;
            return;
        }
        if (!char.IsLetterOrDigit(ch))
        {
            _babPos[id] = pos + 1; // (spaces and the like)
            return;
        }
        // One syllable: the next two or three letters of this word.
        var start = pos;
        var take = 2 + (int)(BabbleRand(id) % 2);
        var caps = 0;
        while (pos < text.Length && pos - start < take && char.IsLetterOrDigit(text[pos]))
        {
            if (char.IsUpper(text[pos]))
                caps++;
            pos++;
        }
        _babPos[id] = pos;
        // What comes at the end of this sentence (a question rises, an exclamation lifts the whole sentence).
        var end = ' ';
        for (var j = pos; j < text.Length && j < pos + 40; j++)
            if (text[j] is '.' or '!' or '?')
            {
                end = text[j];
                break;
            }
        var nearEnd = 0f;
        for (var j = pos; j < text.Length && j < pos + 6; j++)
            if (text[j] is '.' or '!' or '?')
            {
                nearEnd = 1f - (j - pos) / 6f;
                break;
            }
        if (pos >= text.Length)
            nearEnd = 1f;

        var n = _babSentence[id]++;
        var pitch = _voicePitch[id];
        pitch *= 1f - 0.012f * MathF.Min(n, 8); // the note falls through a sentence
        pitch *= 1f + ((BabbleRand(id) & 1023) / 1023f - 0.5f) * 0.14f; // each syllable a little different
        if (caps > 0)
            pitch *= 1.06f;
        if (end == '!')
            pitch *= 1.07f;
        if (end == '?')
            pitch *= 1f + 0.18f * nearEnd;
        else if (end == '.')
            pitch *= 1f - 0.06f * nearEnd;

        // The syllable: one of the voice's favourites, chosen by the letters (the same words murmur the same way).
        var set = _voiceSet[id];
        var count = BitOperations.PopCount(set);
        var pick = (int)(((uint)char.ToLowerInvariant(text[start]) * 7u + (uint)(pos - start) + (BabbleRand(id) & 3u)) % (uint)count);
        var syl = 0;
        for (var bit = 0; bit < Synth.BabbleVariants; bit++)
            if ((set & (1u << bit)) != 0 && pick-- == 0)
            {
                syl = bit;
                break;
            }
        Audio.Babble(syl, mouth, BabbleGain * (caps > 0 || end == '!' ? 1.15f : 1f), pitch, StoryHearing);

        var len = 1.0 / rate * (0.85 + (BabbleRand(id) & 255) / 255.0 * 0.3);
        _babSylAt[id] = now;
        _babSylLen[id] = (float)Math.Min(len, 0.16 / Math.Max(0.5f, pitch));
        _babNext[id] = Math.Max(_babNext[id], now - 0.05) + len;
        _babCount[id]++;
    }

    private uint BabbleRand(int id)
    {
        var x = _babRng[id];
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _babRng[id] = x == 0 ? 0x1234567u : x;
        return x;
    }
}

/// <summary>
/// How open each player's mouth is right now (0 shut, 1 wide), by player id: set by <see cref="Game.StoryBabbleSounds"/>
/// as the story people murmur, read by the body model (the jaw drops and the head nods with it). Presentation only.
/// </summary>
public static class TalkMouth
{
    public static readonly float[] Open = new float[256];

    /// <summary>How far the jaw drops when wide open (m, at the standing head's size).</summary>
    public const float JawDrop = 0.035f;

    /// <summary>The head's nod with the words (radians, wide open).</summary>
    public const float Nod = 0.07f;
}
