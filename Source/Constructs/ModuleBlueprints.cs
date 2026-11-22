using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>A blueprint's <c>"module"</c> block (module-sockets.md 13.3): what a saved module design is, written from its parts at save.</summary>
public sealed class ModuleBlock
{
    public int W, L;
    public bool Cells; // a cell-class module (W, L in 10 cm cells: a console faceplate)
    public bool Round; // a round module (its plug a Round Module Plug, W its diameter)
    public string Primary = "utility";
    public string[] Tags = [];
    public float Height; // proud: its height over the plug's face (m)
    public float Mass, Thrust, Made, Draw;
    public int Guns, Stores, Harvest, Parts;
}

/// <summary>
/// Module designs (module-sockets.md 4.1 and 13.3): a blueprint saved from a selection that is one module (exactly one Module
/// Plug, every part on its side) carries a <c>"module"</c> block with its size, tags, primary tag, proud height and stats.
/// The block is written from the parts at every save (a hand edit is redone at the next save) and read back as a hint: the
/// game always works a fitted module's tags and stats out from its parts.
/// </summary>
public static class ModuleBlueprints
{
    private static readonly string[] Keys = ["w", "l", "cells", "round", "primary", "tags", "styles", "maker", "stats"];

    public static ModuleBlock Parse(JsonElement e, string file)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new ConstructPartException($"{file}: \"module\" is an object (w, l, primary, tags, styles, stats)");
        ConstructParts.Known(e, Keys, $"{file}, module");
        var m = new ModuleBlock
        {
            W = (int)ConstructParts.Num(ConstructParts.Need(e, "w", $"{file}, module"), file, "module.w"),
            L = (int)ConstructParts.Num(ConstructParts.Need(e, "l", $"{file}, module"), file, "module.l"),
        };
        m.Cells = ConstructParts.Opt(e, "cells") is { ValueKind: JsonValueKind.True };
        if (m.Cells ? !ModuleSocketTable.ValidCells(m.W, m.L) : !ModuleSocketTable.ValidSize(m.W, m.L))
            throw new ConstructPartException($"{file}: \"module\" is {m.W} x {m.L}: {(m.Cells ? $"cells, 1 <= w <= l <= {ModuleSockets.MostCells}" : $"whole metres, 1 <= w <= l <= {ModuleSocketTable.MostSide}")}");
        m.Round = ConstructParts.Opt(e, "round") is { ValueKind: JsonValueKind.True };
        if (ConstructParts.Opt(e, "primary") is { ValueKind: JsonValueKind.String } p)
            m.Primary = p.GetString() ?? "utility";
        if (ConstructParts.Opt(e, "tags") is { ValueKind: JsonValueKind.Array } t)
            m.Tags = [.. t.EnumerateArray().Select(x => x.GetString() ?? "")];
        foreach (var tag in m.Tags.Append(m.Primary))
            if (Array.IndexOf(ModuleSocketTable.TagNames, tag) < 0)
                throw new ConstructPartException($"{file}: \"module\": no tag \"{tag}\" ({string.Join(", ", ModuleSocketTable.TagNames)})");
        if (ConstructParts.Opt(e, "styles") is { ValueKind: JsonValueKind.Object } st && ConstructParts.Opt(st, "proud") is { ValueKind: JsonValueKind.Object } pr
            && ConstructParts.Opt(pr, "height") is { ValueKind: JsonValueKind.Number } h)
            m.Height = h.GetSingle();
        if (ConstructParts.Opt(e, "stats") is { ValueKind: JsonValueKind.Object } s)
        {
            float F(string k) => ConstructParts.Opt(s, k) is { ValueKind: JsonValueKind.Number } v ? v.GetSingle() : 0;
            m.Mass = F("mass");
            m.Thrust = F("thrust");
            m.Made = F("made");
            m.Draw = F("draw");
            m.Guns = (int)F("guns");
            m.Stores = (int)F("stores");
            m.Harvest = (int)F("harvest");
            m.Parts = (int)F("parts");
        }
        return m;
    }

    /// <summary>
    /// The <c>"module"</c> block for a clip's body, or null when it is not one module (no plug, two plugs, or parts not on the
    /// plug's side). Worked out from its parts (the rebuild's plug flood), never from a label.
    /// </summary>
    public static string? BlockText(Construct c)
    {
        if (c.PartCount == 0)
            return null;
        ModuleSockets.Build(c);
        if (c.Modules.Length != 1 || c.ModuleOf is not { } of)
            return null;
        for (var j = 0; j < c.PartCount; j++)
            if (of[j] != 0)
                return null;
        var m = c.Modules[0];
        var pd = c.DefOf(m.Plug);
        ref var pp = ref c.Parts[m.Plug];
        var face = pp.Pos + Rot24.Apply(pp.Rot, pd.Sockets[pd.ModSocket].At);
        var up = -ConstructParts.DirVector(Rot24.ApplyDir(pp.Rot, pd.Sockets[pd.ModSocket].Dir));
        var height = 0f;
        for (var j = 0; j < c.PartCount; j++)
        {
            c.PartBounds(j, out var mn, out var mx);
            var top = MathF.Max(Vector3.Dot(mn - face, up), Vector3.Dot(mx - face, up));
            height = MathF.Max(height, top);
        }
        var ic = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(256);
        sb.Append("{ \"w\": ").Append(pd.ModW).Append(", \"l\": ").Append(pd.ModL);
        if (pd.ModCells)
            sb.Append(", \"cells\": true");
        if (pd.ModRound)
            sb.Append(", \"round\": true");
        sb.Append(", \"primary\": \"").Append(ModuleSockets.TagName(m.Primary)).Append("\", \"tags\": [");
        var first = true;
        for (var t = 0; t < (int)ModTag.COUNT; t++)
            if ((m.Tags & (1 << t)) != 0)
            {
                sb.Append(first ? "" : ", ").Append('"').Append(ModuleSocketTable.TagNames[t]).Append('"');
                first = false;
            }
        sb.Append("], \"styles\": { \"proud\": { \"height\": ").Append(MathF.Round(height, 2).ToString("0.##", ic)).Append(" } }");
        sb.Append(", \"stats\": { \"mass\": ").Append(MathF.Round(m.Mass).ToString(ic))
            .Append(", \"thrust\": ").Append(MathF.Round(m.Thrust).ToString(ic))
            .Append(", \"made\": ").Append(MathF.Round(m.Made, 1).ToString(ic))
            .Append(", \"draw\": ").Append(MathF.Round(m.Draw).ToString(ic))
            .Append(", \"guns\": ").Append(m.Guns).Append(", \"stores\": ").Append(m.Stores).Append(", \"harvest\": ").Append(m.Harvest)
            .Append(", \"parts\": ").Append(m.Parts).Append(" } }");
        return sb.ToString();
    }
}
