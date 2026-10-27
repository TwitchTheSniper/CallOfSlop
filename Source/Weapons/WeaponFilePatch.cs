using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Changes values in a weapon file's text without rewriting it (the weapon tweak window's Save, WeaponTweaks.cs): a
/// value that is there has just its text replaced; a missing one is added after the key before it in file order (inside
/// the "recoil" or "launcher" block for theirs, or a new block when the file has none). Everything else (layout, the
/// one-line recoil block, comments, the model block) stays byte for byte, so a diff shows only the tweaked numbers.
/// </summary>
public sealed class WeaponFilePatch
{
    private readonly record struct Prop(string Name, int Start, int End);

    private sealed class Block
    {
        public readonly List<Prop> Props = new();
        public int Open, Close, Added;
    }

    private readonly byte[] _src;
    private readonly string _source;
    private readonly string _nl;
    private readonly List<Prop> _top = new();
    private readonly Dictionary<string, Block> _blocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<(string Key, string Text)>> _newBlocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(int Start, int End, string Text, int Seq)> _edits = new();
    private readonly bool _bom;
    private int _rootClose = -1;

    /// <summary>The top-level keys in the order <see cref="WeaponFile.ToJson"/> writes them (the blocks where they go).</summary>
    public static readonly string[] TopOrder = BuildTopOrder();

    private static string[] BuildTopOrder()
    {
        var list = new List<string> { "format", "name", "author", "order", "unlockLevel", "defaultOptic" };
        foreach (var f in WeaponFile.Fields)
        {
            if (f.Name != nameof(WeaponDef.Name))
                list.Add(WeaponFile.Key(f));
            if (f.Name == nameof(WeaponDef.RecoilDeg))
                list.Add("recoil");
        }
        list.AddRange(["launcher", "model", "voice"]);
        return list.ToArray();
    }

    public WeaponFilePatch(string text, string source)
    {
        _source = source;
        _bom = text.Length > 0 && text[0] == '﻿';
        _src = Encoding.UTF8.GetBytes(_bom ? text[1..] : text);
        _nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        try
        {
            Parse();
        }
        catch (JsonException ex)
        {
            throw new WeaponFileException($"{source}: not valid JSON ({ex.Message})");
        }
    }

    private void Parse()
    {
        var r = new Utf8JsonReader(_src, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!r.Read() || r.TokenType != JsonTokenType.StartObject)
            throw new WeaponFileException($"{_source}: a weapon file is one JSON object");
        while (r.Read())
        {
            if (r.TokenType == JsonTokenType.EndObject)
            {
                _rootClose = (int)r.TokenStartIndex;
                return;
            }
            var name = r.GetString()!;
            r.Read();
            var start = (int)r.TokenStartIndex;
            if (r.TokenType == JsonTokenType.StartObject && name.ToLowerInvariant() is "recoil" or "launcher")
            {
                var b = new Block { Open = start };
                while (r.Read() && r.TokenType != JsonTokenType.EndObject)
                {
                    var key = r.GetString()!;
                    r.Read();
                    var s = (int)r.TokenStartIndex;
                    if (r.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                        r.Skip();
                    b.Props.Add(new Prop(key, s, (int)r.BytesConsumed));
                }
                b.Close = (int)r.TokenStartIndex;
                _blocks[name] = b;
                _top.Add(new Prop(name, start, (int)r.BytesConsumed));
                continue;
            }
            if (r.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                r.Skip();
            _top.Add(new Prop(name, start, (int)r.BytesConsumed));
        }
        throw new WeaponFileException($"{_source}: the file ends before its closing brace");
    }

    private static int Find(List<Prop> props, string key)
    {
        for (var i = 0; i < props.Count; i++)
            if (props[i].Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>Sets <paramref name="key"/> (in <paramref name="block"/>: null for the top level) to the JSON text <paramref name="json"/>.</summary>
    public void Set(string? block, string key, string json)
    {
        if (block == null)
        {
            var i = Find(_top, key);
            if (i >= 0)
                Edit(_top[i].Start, _top[i].End, json);
            else
                InsertTop(key, json);
            return;
        }
        if (_blocks.TryGetValue(block, out var b))
        {
            var i = Find(b.Props, key);
            if (i >= 0)
                Edit(b.Props[i].Start, b.Props[i].End, json);
            else if (b.Props.Count > 0)
                Edit(b.Props[^1].End, b.Props[^1].End, $", \"{key}\": {json}");
            else
                Edit(b.Open + 1, b.Open + 1, $"{(b.Added++ == 0 ? " " : ", ")}\"{key}\": {json}");
            return;
        }
        if (!_newBlocks.TryGetValue(block, out var list))
            _newBlocks[block] = list = new();
        list.Add((key, json));
    }

    private void Edit(int start, int end, string text) => _edits.Add((start, end, text, _edits.Count));

    /// <summary>A new top-level key: after the nearest key before it in <see cref="TopOrder"/> that the file has (else last).</summary>
    private void InsertTop(string key, string json)
    {
        var at = Array.FindIndex(TopOrder, k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
        for (var k = at - 1; k >= 0; k--)
            if (Find(_top, TopOrder[k]) is var i and >= 0)
            {
                Edit(_top[i].End, _top[i].End, $",{_nl}  \"{key}\": {json}");
                return;
            }
        if (_top.Count > 0)
            Edit(_top[^1].End, _top[^1].End, $",{_nl}  \"{key}\": {json}");
        else
            Edit(_rootClose, _rootClose, $"  \"{key}\": {json}{_nl}");
    }

    /// <summary>The patched text.</summary>
    public string Result()
    {
        foreach (var (block, keys) in _newBlocks)
            InsertTop(block, "{ " + string.Join(", ", keys.Select(k => $"\"{k.Key}\": {k.Text}")) + " }");
        _newBlocks.Clear();
        var sb = new StringBuilder();
        var pos = 0;
        foreach (var e in _edits.OrderBy(e => e.Start).ThenBy(e => e.Seq))
        {
            if (e.Start < pos)
                throw new WeaponFileException($"{_source}: overlapping changes");
            sb.Append(Encoding.UTF8.GetString(_src, pos, e.Start - pos)).Append(e.Text);
            pos = e.End;
        }
        sb.Append(Encoding.UTF8.GetString(_src, pos, _src.Length - pos));
        return (_bom ? "﻿" : "") + sb;
    }
}
