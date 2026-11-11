using System.Globalization;
using System.Numerics;
using System.Text;

namespace CallOfSlop.PartKit;

/// <summary>
/// An ordered JSON object that behaves as a Python dict: keys keep the order they were first set in, and setting a key again
/// keeps its place. The part kit's generators build their files from these so that <see cref="PyJson.Dumps"/> writes the same
/// bytes as the Python scripts they replace (docs/sandbox/PARTKIT.md, "Byte for byte").
/// Values: string, int or long (a Python int), double (a Python float), bool, null, <see cref="PyDict"/>, List&lt;object?&gt;.
/// </summary>
public sealed class PyDict : IEnumerable<KeyValuePair<string, object?>>
{
    private readonly List<string> _keys = [];
    private readonly Dictionary<string, object?> _values = [];

    public PyDict() { }

    public PyDict(PyDict other) => Update(other);

    public int Count => _keys.Count;
    public IReadOnlyList<string> Keys => _keys;

    public object? this[string key]
    {
        get => _values.TryGetValue(key, out var v) ? v : throw new KeyNotFoundException(key);
        set
        {
            if (!_values.ContainsKey(key))
                _keys.Add(key);
            _values[key] = value;
        }
    }

    /// <summary>dict.get: null when absent.</summary>
    public object? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;
    public bool Has(string key) => _values.ContainsKey(key);

    /// <summary>dict.update: new keys go at the end, known keys keep their place.</summary>
    public PyDict Update(PyDict other)
    {
        foreach (var kv in other)
            this[kv.Key] = kv.Value;
        return this;
    }

    /// <summary>dict.update(**keys) with keyword arguments, in their order.</summary>
    public PyDict Update(params (string Key, object? Value)[] keys)
    {
        foreach (var (k, v) in keys)
            this[k] = v;
        return this;
    }

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        foreach (var k in _keys)
            yield return new KeyValuePair<string, object?>(k, _values[k]);
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    // Collection initialiser support: new PyDict { { "a", 1 } } as well as { ["a"] = 1 }.
    public void Add(string key, object? value) => this[key] = value;
}

/// <summary>Python's number semantics where C# differs: float repr, round(x, n), format "g", sum().</summary>
public static class Py
{
    /// <summary>A Python list of any values (ints stay ints, doubles floats).</summary>
    public static List<object?> L(params object?[] items) => [.. items];

    /// <summary>Python truthiness.</summary>
    public static bool Truthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        int i => i != 0,
        long l => l != 0,
        double d => d != 0,
        string s => s.Length > 0,
        System.Collections.ICollection c => c.Count > 0,
        PyDict d => d.Count > 0,
        _ => true,
    };

    /// <summary>A Python number (int or float) as a double.</summary>
    public static double D(object? v) => v switch
    {
        double d => d,
        int i => i,
        long l => l,
        bool b => b ? 1 : 0,
        _ => throw new InvalidCastException($"not a number: {v}"),
    };

    /// <summary>repr(float): the shortest digits that read back, in Python's layout (1e-05, 0.0001, 1e+16, 3.0).</summary>
    public static string Repr(double v)
    {
        if (double.IsNaN(v))
            return "NaN";
        if (double.IsInfinity(v))
            return v > 0 ? "Infinity" : "-Infinity";
        if (v == 0)
            return double.IsNegative(v) ? "-0.0" : "0.0";
        var (neg, digits, decpt) = Shortest(v);
        var sb = new StringBuilder();
        if (neg)
            sb.Append('-');
        if (decpt <= -4 || decpt > 16)
        {
            sb.Append(digits[0]);
            if (digits.Length > 1)
                sb.Append('.').Append(digits, 1, digits.Length - 1);
            var e = decpt - 1;
            sb.Append('e').Append(e < 0 ? '-' : '+').Append(Math.Abs(e).ToString("00", CultureInfo.InvariantCulture));
        }
        else if (decpt <= 0)
            sb.Append("0.").Append('0', -decpt).Append(digits);
        else if (decpt >= digits.Length)
            sb.Append(digits).Append('0', decpt - digits.Length).Append(".0");
        else
            sb.Append(digits, 0, decpt).Append('.').Append(digits, decpt, digits.Length - decpt);
        return sb.ToString();
    }

    /// <summary>The shortest round-trip digits of v (no leading or trailing zeros) and where the point goes (Python's decpt).</summary>
    private static (bool Neg, string Digits, int Decpt) Shortest(double v)
    {
        var s = v.ToString("R", CultureInfo.InvariantCulture);
        var neg = s[0] == '-';
        if (neg)
            s = s[1..];
        var exp = 0;
        var ei = s.IndexOfAny(['E', 'e']);
        if (ei >= 0)
        {
            exp = int.Parse(s[(ei + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            s = s[..ei];
        }
        var dot = s.IndexOf('.');
        var intPart = dot >= 0 ? s[..dot] : s;
        var frac = dot >= 0 ? s[(dot + 1)..] : "";
        var digits = intPart + frac;
        var decpt = intPart.Length + exp;
        var lead = 0;
        while (lead < digits.Length - 1 && digits[lead] == '0')
            lead++;
        digits = digits[lead..];
        decpt -= lead;
        digits = digits.TrimEnd('0');
        if (digits.Length == 0)
            digits = "0";
        return (neg, digits, decpt);
    }

    /// <summary>round(x, n) for a float: the exact binary value rounded half to even at n decimals, as CPython does.</summary>
    public static double Round(double x, int n)
    {
        if (!double.IsFinite(x) || x == 0)
            return x;
        var bits = BitConverter.DoubleToInt64Bits(x);
        var neg = bits < 0;
        var expBits = (int)((bits >> 52) & 0x7FF);
        var mant = bits & 0xFFFFFFFFFFFFFL;
        int e;
        if (expBits == 0)
            e = -1074;
        else
        {
            mant |= 1L << 52;
            e = expBits - 1075;
        }
        if (e >= 0 && n >= 0)
            return x; // a whole number already
        BigInteger num = mant, den = BigInteger.One;
        if (e >= 0)
            num <<= e;
        else
            den <<= -e;
        if (n >= 0)
            num *= BigInteger.Pow(10, n);
        else
            den *= BigInteger.Pow(10, -n);
        var q = BigInteger.DivRem(num, den, out var rem);
        var twice = rem * 2;
        if (twice > den || twice == den && !q.IsEven)
            q += 1;
        var r = double.Parse(q.ToString(CultureInfo.InvariantCulture) + "E" + (-n).ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
        return neg ? -r : r;
    }

    /// <summary>round(x) with no digits: a Python int, half to even.</summary>
    public static long RoundInt(double x) => (long)Math.Round(x, MidpointRounding.ToEven);

    /// <summary>f"{v:g}" (6 significant digits, trailing zeros dropped, exponent below 1e-4 or from 1e6).</summary>
    public static string G(double v, int prec = 6)
    {
        if (v == 0)
            return double.IsNegative(v) ? "-0" : "0";
        var e = v.ToString("E" + (prec - 1), CultureInfo.InvariantCulture); // -d.dddddE+xxx, correctly rounded
        var ei = e.IndexOf('E');
        var exp = int.Parse(e[(ei + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var neg = e[0] == '-';
        var digits = e[(neg ? 1 : 0)..ei].Replace(".", "");
        var sb = new StringBuilder();
        if (neg)
            sb.Append('-');
        if (exp < -4 || exp >= prec)
        {
            var m = digits.TrimEnd('0');
            sb.Append(m[0]);
            if (m.Length > 1)
                sb.Append('.').Append(m, 1, m.Length - 1);
            sb.Append('e').Append(exp < 0 ? '-' : '+').Append(Math.Abs(exp).ToString("00", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
        var decpt = exp + 1;
        string whole, frac;
        if (decpt <= 0)
        {
            whole = "0";
            frac = new string('0', -decpt) + digits;
        }
        else
        {
            whole = digits[..decpt];
            frac = digits[decpt..];
        }
        frac = frac.TrimEnd('0');
        sb.Append(whole);
        if (frac.Length > 0)
            sb.Append('.').Append(frac);
        return sb.ToString();
    }

    /// <summary>
    /// sum() of floats as CPython 3.12+ does it: the first item added to the int 0, the rest by Neumaier's compensated sum.
    /// Empty: 0.
    /// </summary>
    public static double Sum(IEnumerable<double> items)
    {
        var first = true;
        double hi = 0, lo = 0;
        foreach (var x in items)
        {
            if (first)
            {
                hi = 0 + x; // int 0 + float (a -0.0 becomes 0.0, as in Python)
                first = false;
                continue;
            }
            var t = hi + x;
            if (Math.Abs(hi) >= Math.Abs(x))
                lo += (hi - t) + x;
            else
                lo += (x - t) + hi;
            hi = t;
        }
        return lo != 0 && double.IsFinite(lo) ? hi + lo : hi;
    }
}

/// <summary>Python's json module: <c>json.dumps</c> byte for byte and a <c>json.loads</c> that keeps ints and floats apart.</summary>
public static class PyJson
{
    /// <summary>json.dumps(v, indent=indent, separators=..., ensure_ascii=...). indent null: one line.</summary>
    public static string Dumps(object? v, int? indent = null, string? itemSep = null, string keySep = ": ", bool ensureAscii = true)
    {
        var sb = new StringBuilder();
        Write(sb, v, indent, itemSep ?? (indent is null ? ", " : ","), keySep, ensureAscii, 0);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, object? v, int? indent, string itemSep, string keySep, bool ascii, int level)
    {
        switch (v)
        {
            case null:
                sb.Append("null");
                return;
            case bool b:
                sb.Append(b ? "true" : "false");
                return;
            case string s:
                Str(sb, s, ascii);
                return;
            case int i:
                sb.Append(i.ToString(CultureInfo.InvariantCulture));
                return;
            case long l:
                sb.Append(l.ToString(CultureInfo.InvariantCulture));
                return;
            case double d:
                sb.Append(Py.Repr(d));
                return;
            case float:
                throw new ArgumentException("PyJson: use double for a Python float, not float");
            case PyDict o:
                if (o.Count == 0)
                {
                    sb.Append("{}");
                    return;
                }
                sb.Append('{');
                var first = true;
                foreach (var kv in o)
                {
                    if (!first)
                        sb.Append(itemSep);
                    first = false;
                    NewLine(sb, indent, level + 1);
                    Str(sb, kv.Key, ascii);
                    sb.Append(keySep);
                    Write(sb, kv.Value, indent, itemSep, keySep, ascii, level + 1);
                }
                NewLine(sb, indent, level);
                sb.Append('}');
                return;
            case System.Collections.IList list:
                if (list.Count == 0)
                {
                    sb.Append("[]");
                    return;
                }
                sb.Append('[');
                for (var k = 0; k < list.Count; k++)
                {
                    if (k > 0)
                        sb.Append(itemSep);
                    NewLine(sb, indent, level + 1);
                    Write(sb, list[k], indent, itemSep, keySep, ascii, level + 1);
                }
                NewLine(sb, indent, level);
                sb.Append(']');
                return;
            default:
                throw new ArgumentException($"PyJson: can't write a {v.GetType().Name}");
        }
    }

    private static void NewLine(StringBuilder sb, int? indent, int level)
    {
        if (indent is { } n)
            sb.Append('\n').Append(' ', n * level);
    }

    private static void Str(StringBuilder sb, string s, bool ascii)
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
                    if (c < 0x20 || ascii && c > 0x7e)
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    /// <summary>json.loads: objects as <see cref="PyDict"/>, arrays as List&lt;object?&gt;, a number with '.', 'e' or 'E' a double, else a long.</summary>
    public static object? Loads(string text)
    {
        var p = 0;
        var v = Value(text, ref p);
        Ws(text, ref p);
        if (p != text.Length)
            throw new FormatException($"PyJson: extra data at {p}");
        return v;
    }

    private static void Ws(string t, ref int p)
    {
        while (p < t.Length && t[p] is ' ' or '\t' or '\n' or '\r')
            p++;
    }

    private static object? Value(string t, ref int p)
    {
        Ws(t, ref p);
        if (p >= t.Length)
            throw new FormatException("PyJson: unexpected end");
        var c = t[p];
        if (c == '{')
        {
            p++;
            var o = new PyDict();
            Ws(t, ref p);
            if (t[p] == '}')
            {
                p++;
                return o;
            }
            while (true)
            {
                Ws(t, ref p);
                var key = Text(t, ref p);
                Ws(t, ref p);
                if (t[p++] != ':')
                    throw new FormatException($"PyJson: ':' expected at {p - 1}");
                o[key] = Value(t, ref p);
                Ws(t, ref p);
                var sep = t[p++];
                if (sep == '}')
                    return o;
                if (sep != ',')
                    throw new FormatException($"PyJson: ',' expected at {p - 1}");
            }
        }
        if (c == '[')
        {
            p++;
            var l = new List<object?>();
            Ws(t, ref p);
            if (t[p] == ']')
            {
                p++;
                return l;
            }
            while (true)
            {
                l.Add(Value(t, ref p));
                Ws(t, ref p);
                var sep = t[p++];
                if (sep == ']')
                    return l;
                if (sep != ',')
                    throw new FormatException($"PyJson: ',' expected at {p - 1}");
            }
        }
        if (c == '"')
            return Text(t, ref p);
        if (t.AsSpan(p).StartsWith("true"))
        {
            p += 4;
            return true;
        }
        if (t.AsSpan(p).StartsWith("false"))
        {
            p += 5;
            return false;
        }
        if (t.AsSpan(p).StartsWith("null"))
        {
            p += 4;
            return null;
        }
        var start = p;
        var isFloat = false;
        while (p < t.Length && (char.IsDigit(t[p]) || t[p] is '-' or '+' or '.' or 'e' or 'E'))
        {
            if (t[p] is '.' or 'e' or 'E')
                isFloat = true;
            p++;
        }
        if (p == start)
            throw new FormatException($"PyJson: unexpected '{c}' at {p}");
        var num = t[start..p];
        return isFloat ? (object)double.Parse(num, NumberStyles.Float, CultureInfo.InvariantCulture) : long.Parse(num, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    private static string Text(string t, ref int p)
    {
        if (t[p] != '"')
            throw new FormatException($"PyJson: string expected at {p}");
        p++;
        var sb = new StringBuilder();
        while (true)
        {
            var c = t[p++];
            if (c == '"')
                return sb.ToString();
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }
            var e = t[p++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u':
                    sb.Append((char)int.Parse(t.AsSpan(p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    p += 4;
                    break;
                default: sb.Append(e); break;
            }
        }
    }
}
