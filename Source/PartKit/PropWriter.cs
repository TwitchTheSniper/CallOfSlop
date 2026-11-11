using System.Text;

namespace CallOfSlop.PartKit;

/// <summary>
/// The two layouts the prop generators of tools/propviz write (a port of them writes through these): <see cref="Lines"/> is
/// gen.py's write_prop (each top key on its own line as one-line JSON, then the list one item a line), <see cref="Head"/> is
/// gen_animated.py's dump (the top keys as json.dumps(indent=2), then the list one item a line). The list is "parts" for a prop,
/// "ops" for a gallery map. <c>--gen-parts --roundtrip DIR</c> proves both on committed files.
/// </summary>
public static class PropWriter
{
    public static string ListKey(PyDict d) => d.Has("parts") ? "parts" : "ops";

    public static string Lines(PyDict d)
    {
        var key = ListKey(d);
        var sb = new StringBuilder("{\n");
        foreach (var kv in d)
            if (kv.Key != key)
                sb.Append("  ").Append(PyJson.Dumps(kv.Key)).Append(": ").Append(PyJson.Dumps(kv.Value)).Append(",\n");
        sb.Append("  \"").Append(key).Append("\": [\n");
        var list = (List<object?>)d[key]!;
        for (var i = 0; i < list.Count; i++)
            sb.Append("    ").Append(PyJson.Dumps(list[i])).Append(i < list.Count - 1 ? ",\n" : "\n");
        return sb.Append("  ]\n}\n").ToString();
    }

    public static string Head(PyDict d)
    {
        var key = ListKey(d);
        var head = new PyDict();
        foreach (var kv in d)
            if (kv.Key != key)
                head[kv.Key] = kv.Value;
        var h = PyJson.Dumps(head, 2);
        var sb = new StringBuilder(h[..^2]).Append(",\n  \"").Append(key).Append("\": [\n");
        sb.Append(string.Join(",\n", ((List<object?>)d[key]!).Select(p => "    " + PyJson.Dumps(p))));
        return sb.Append("\n  ]\n}\n").ToString();
    }
}
