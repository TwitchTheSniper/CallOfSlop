using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CallOfSlop.MapKit;

/// <summary>
/// A small ordered JSON tree for map files: objects keep their key order and numbers remember whether they were written
/// as whole numbers or as decimals, so a file read with <see cref="Parse"/> and written back with <see cref="Write"/>
/// comes out byte for byte the same when it was written by the Python generators (json.dump with indent=1), and
/// semantically the same otherwise. The map kit builds maps as these trees (<see cref="Kit"/>) and the loader reads the
/// written text through <see cref="MapFile.Load"/>, so the kit can only emit what the game can read.
/// </summary>
public abstract class JNode
{
    public static JNode Parse(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!reader.Read())
            throw new MapFileException("empty JSON");
        return ReadValue(ref reader);
    }

    private static JNode ReadValue(ref Utf8JsonReader r)
    {
        switch (r.TokenType)
        {
            case JsonTokenType.StartObject:
            {
                var o = new JObject();
                while (r.Read() && r.TokenType != JsonTokenType.EndObject)
                {
                    var key = r.GetString()!;
                    r.Read();
                    o.Add(key, ReadValue(ref r));
                }
                return o;
            }
            case JsonTokenType.StartArray:
            {
                var a = new JArray();
                while (r.Read() && r.TokenType != JsonTokenType.EndArray)
                    a.Add(ReadValue(ref r));
                return a;
            }
            case JsonTokenType.String:
                return new JString(r.GetString()!);
            case JsonTokenType.Number:
            {
                var text = r.HasValueSequence ? Encoding.UTF8.GetString(System.Buffers.BuffersExtensions.ToArray(r.ValueSequence)) : Encoding.UTF8.GetString(r.ValueSpan);
                var isInt = text.IndexOfAny(['.', 'e', 'E']) < 0;
                return isInt && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
                    ? new JNumber(l)
                    : new JNumber(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture));
            }
            case JsonTokenType.True:
                return new JBool(true);
            case JsonTokenType.False:
                return new JBool(false);
            case JsonTokenType.Null:
                return JNull.Instance;
            default:
                throw new MapFileException($"unexpected JSON token {r.TokenType}");
        }
    }

    /// <summary>The tree as text in the Python generators' style (json.dump with indent=1: one value per line).</summary>
    public string Write()
    {
        var sb = new StringBuilder();
        WriteTo(sb, 0);
        sb.Append('\n');
        return sb.ToString();
    }

    public abstract void WriteTo(StringBuilder sb, int depth);

    /// <summary>Structural equality (key order and the whole-number / decimal distinction included).</summary>
    public abstract bool SameAs(JNode other);

    public static JNode From(object? v) => v switch
    {
        null => JNull.Instance,
        JNode n => n,
        bool b => new JBool(b),
        int i => new JNumber(i),
        long l => new JNumber(l),
        float f => new JNumber((double)f),
        double d => new JNumber(d),
        string s => new JString(s),
        IEnumerable<string> ss => new JArray(ss.Select(s => (JNode)new JString(s))),
        IEnumerable<object?> os => new JArray(os.Select(From)),
        IEnumerable<double> ds => new JArray(ds.Select(d => (JNode)new JNumber(d))),
        IEnumerable<int> ints => new JArray(ints.Select(i => (JNode)new JNumber(i))),
        _ => throw new MapFileException($"can't put a {v.GetType().Name} in a map file"),
    };

    protected static void Indent(StringBuilder sb, int depth) => sb.Append(' ', depth);

    /// <summary>Python's json string escaping (ensure_ascii: anything past ASCII becomes \uXXXX).</summary>
    public static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20 || c > 0x7f)
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    /// <summary>Python's repr of a float: the shortest text that reads back as the same number, always with a decimal point.</summary>
    public static string PyFloat(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v))
            throw new MapFileException("a map file can't hold NaN or infinity");
        if (v == Math.Floor(v) && Math.Abs(v) < 1e16)
            return (v == 0 && double.IsNegative(v) ? "-0" : ((long)v).ToString(CultureInfo.InvariantCulture)) + ".0";
        var s = v.ToString("R", CultureInfo.InvariantCulture);
        var e = s.IndexOf('E');
        if (e < 0)
            return s;
        // Python: 1e-05, 1.5e+17 (a sign and at least two digits).
        var mant = s[..e];
        var exp = s[(e + 1)..];
        var sign = exp[0] == '-' ? "-" : "+";
        exp = exp.TrimStart('+', '-').PadLeft(2, '0');
        return mant + "e" + sign + exp;
    }
}

public sealed class JObject : JNode
{
    public readonly List<KeyValuePair<string, JNode>> Items = new();

    public JObject() { }

    public JObject(params (string Key, object? Value)[] items)
    {
        foreach (var (k, v) in items)
            Add(k, From(v));
    }

    public int Count => Items.Count;

    public void Add(string key, JNode value) => Items.Add(new KeyValuePair<string, JNode>(key, value));

    public void Add(string key, object? value) => Add(key, From(value));

    /// <summary>Sets a key's value in place, or adds it at the end.</summary>
    public void Set(string key, JNode value)
    {
        for (var i = 0; i < Items.Count; i++)
            if (Items[i].Key == key)
            {
                Items[i] = new KeyValuePair<string, JNode>(key, value);
                return;
            }
        Add(key, value);
    }

    public void Set(string key, object? value) => Set(key, From(value));

    public bool Remove(string key) => Items.RemoveAll(kv => kv.Key == key) > 0;

    public JNode? Get(string key)
    {
        foreach (var kv in Items)
            if (kv.Key == key)
                return kv.Value;
        return null;
    }

    public bool Has(string key) => Get(key) != null;

    public JNode this[string key]
    {
        get => Get(key) ?? throw new KeyNotFoundException(key);
        set => Set(key, value);
    }

    public double Num(string key) => Get(key) is JNumber n ? n.Value : throw new MapFileException($"\"{key}\" isn't a number");

    public double Num(string key, double fallback) => Get(key) is JNumber n ? n.Value : fallback;

    public string? Str(string key) => Get(key) is JString s ? s.Value : null;

    public override void WriteTo(StringBuilder sb, int depth)
    {
        if (Items.Count == 0)
        {
            sb.Append("{}");
            return;
        }
        sb.Append("{\n");
        for (var i = 0; i < Items.Count; i++)
        {
            Indent(sb, depth + 1);
            WriteString(sb, Items[i].Key);
            sb.Append(": ");
            Items[i].Value.WriteTo(sb, depth + 1);
            sb.Append(i < Items.Count - 1 ? ",\n" : "\n");
        }
        Indent(sb, depth);
        sb.Append('}');
    }

    public override bool SameAs(JNode other)
    {
        if (other is not JObject o || o.Items.Count != Items.Count)
            return false;
        for (var i = 0; i < Items.Count; i++)
            if (Items[i].Key != o.Items[i].Key || !Items[i].Value.SameAs(o.Items[i].Value))
                return false;
        return true;
    }

    public JObject Clone()
    {
        var c = new JObject();
        foreach (var kv in Items)
            c.Add(kv.Key, kv.Value.CloneNode());
        return c;
    }

    public override string ToString() => Write().TrimEnd();
}

public sealed class JArray : JNode
{
    public readonly List<JNode> Items = new();

    public JArray() { }

    public JArray(IEnumerable<JNode> items) => Items.AddRange(items);

    public static JArray Of(params object?[] values) => new(values.Select(From));

    public int Count => Items.Count;

    public JNode this[int i] => Items[i];

    public void Add(JNode n) => Items.Add(n);

    public void Add(object? v) => Items.Add(From(v));

    public override void WriteTo(StringBuilder sb, int depth)
    {
        if (Items.Count == 0)
        {
            sb.Append("[]");
            return;
        }
        sb.Append("[\n");
        for (var i = 0; i < Items.Count; i++)
        {
            Indent(sb, depth + 1);
            Items[i].WriteTo(sb, depth + 1);
            sb.Append(i < Items.Count - 1 ? ",\n" : "\n");
        }
        Indent(sb, depth);
        sb.Append(']');
    }

    public override bool SameAs(JNode other)
    {
        if (other is not JArray a || a.Items.Count != Items.Count)
            return false;
        for (var i = 0; i < Items.Count; i++)
            if (!Items[i].SameAs(a.Items[i]))
                return false;
        return true;
    }
}

public sealed class JNumber : JNode
{
    public readonly double Value;
    public readonly bool IsInt;

    public JNumber(long v)
    {
        Value = v;
        IsInt = true;
    }

    public JNumber(double v)
    {
        Value = v;
        IsInt = false;
    }

    public override void WriteTo(StringBuilder sb, int depth) =>
        sb.Append(IsInt ? ((long)Value).ToString(CultureInfo.InvariantCulture) : PyFloat(Value));

    public override bool SameAs(JNode other) => other is JNumber n && n.IsInt == IsInt && (n.Value.Equals(Value) || n.Value == Value);
}

public sealed class JString(string value) : JNode
{
    public readonly string Value = value;

    public override void WriteTo(StringBuilder sb, int depth) => WriteString(sb, Value);

    public override bool SameAs(JNode other) => other is JString s && s.Value == Value;
}

public sealed class JBool(bool value) : JNode
{
    public readonly bool Value = value;

    public override void WriteTo(StringBuilder sb, int depth) => sb.Append(Value ? "true" : "false");

    public override bool SameAs(JNode other) => other is JBool b && b.Value == Value;
}

public sealed class JNull : JNode
{
    public static readonly JNull Instance = new();

    public override void WriteTo(StringBuilder sb, int depth) => sb.Append("null");

    public override bool SameAs(JNode other) => other is JNull;
}

public static class JNodeExtensions
{
    public static JNode CloneNode(this JNode n) => n switch
    {
        JObject o => o.Clone(),
        JArray a => new JArray(a.Items.Select(CloneNode)),
        _ => n, // leaves are immutable
    };
}
