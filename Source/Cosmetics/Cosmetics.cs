using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// One box of a hat model: a unit cube transformed into head space (y = 0 the top of the head, -Z the face). Alpha under 1 is
/// glass (a part's "glass": its opacity, as the space suit helmet's visor): drawn blended after the hat's solid boxes.
/// </summary>
public readonly record struct HatBox(Matrix4x4 Local, Vector3 Color, float Alpha = 1f)
{
    public bool Glass => Alpha < 1f;
}

/// <summary>A hat model: a uniform's cap (Cosmetics/uniforms.json, Uniforms.cs), drawn by HatRender.</summary>
public sealed class HatDef
{
    /// <summary>The key ("ministry_cap").</summary>
    public string Key = "";
    /// <summary>Display name, plain ASCII.</summary>
    public string Name = "";
    /// <summary>The model: boxes in head space, built from the file's parts (a "round" part becomes several boxes).</summary>
    public IReadOnlyList<HatBox> Boxes = [];
    /// <summary>Bounds of the model in head space (metres, before scaling to the head).</summary>
    public Vector3 Min, Max;
    /// <summary>How much the hat sways with movement (0 = rigid).</summary>
    public float Wobble;

    /// <summary>Height of the model above the top of the head.</summary>
    public float Height => Max.Y;
}

/// <summary>
/// The hat models' file helpers (a part's boxes, bounds, vectors and colours), for the uniforms' caps (Uniforms.cs). The
/// prestige hats (Cosmetics/hats.json, unlocked by prestige) were removed with levelling and prestige (2026-11-17,
/// docs/removals-notes.md).
/// </summary>
public static class Cosmetics
{
    internal static void ReadPart(JsonElement p, string where, List<HatBox> boxes)
    {
        var shape = p.TryGetProperty("shape"u8, out var s) ? s.GetString() ?? "box" : "box";
        var at = Vec(p, "at", where, Vector3.Zero);
        var size = Vec(p, "size", where, null);
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
            throw new FormatException($"{where}: \"size\" must be positive");
        var color = Color(p, where);
        var alpha = 1f;
        if (p.TryGetProperty("glass"u8, out var gl))
        {
            alpha = gl.GetSingle();
            if (alpha is < 0.05f or >= 1f)
                throw new FormatException($"{where}: \"glass\" is its opacity, 0.05 to under 1");
        }
        float Deg(string name) => p.TryGetProperty(name, out var v) ? v.GetSingle() * MathF.PI / 180f : 0f;
        var tilt = Matrix4x4.CreateRotationY(Deg("tiltY")) * Matrix4x4.CreateRotationX(Deg("tiltX")) * Matrix4x4.CreateRotationZ(Deg("tiltZ"));
        var place = tilt * Matrix4x4.CreateTranslation(at);
        switch (shape)
        {
            case "box":
                boxes.Add(new HatBox(Matrix4x4.CreateScale(size) * place, color, alpha));
                break;
            case "round":
                // A union of squares turned around Y: always covers the head's own square, reads as round from any side.
                var sides = p.TryGetProperty("sides"u8, out var sd) ? sd.GetInt32() : 4;
                if (sides < 1 || sides > 8)
                    throw new FormatException($"{where}: \"sides\" is 1..8");
                for (var i = 0; i < sides; i++)
                    boxes.Add(new HatBox(Matrix4x4.CreateScale(size) * Matrix4x4.CreateRotationY(i * MathF.PI / 2 / sides) * place, color, alpha));
                break;
            default:
                throw new FormatException($"{where}: \"shape\" is \"box\" or \"round\"");
        }
    }

    internal static void Measure(HatDef def)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var b in def.Boxes)
            for (var c = 0; c < 8; c++)
            {
                var v = Vector3.Transform(new Vector3((c & 1) - 0.5f, ((c >> 1) & 1) - 0.5f, ((c >> 2) & 1) - 0.5f), b.Local);
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
        def.Min = min;
        def.Max = max;
    }

    private static string Str(JsonElement e, string name, string where) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : throw new FormatException($"{where}: needs \"{name}\" (text)");

    private static float Num(JsonElement e, string name, string where) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : throw new FormatException($"{where}: needs \"{name}\" (a number)");

    internal static Vector3 Vec(JsonElement e, string name, string where, Vector3? fallback)
    {
        if (!e.TryGetProperty(name, out var v))
            return fallback ?? throw new FormatException($"{where}: needs \"{name}\": [x, y, z]");
        if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 3)
            throw new FormatException($"{where}: \"{name}\" is [x, y, z]");
        return new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());
    }

    internal static Vector3 Color(JsonElement e, string where)
    {
        var text = e.TryGetProperty("color"u8, out var v) ? v.GetString() ?? "" : "";
        if (text.Length != 7 || text[0] != '#' || !int.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            throw new FormatException($"{where}: \"color\" is \"#rrggbb\"");
        return new Vector3((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);
    }
}
