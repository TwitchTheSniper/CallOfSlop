using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop.FactoryKit;

/// <summary>One body of a site: a blueprint by name, where its plan's origin goes (the road's front edge at its west end; metres, x east, z towards the road) and its yaw.</summary>
public sealed record SiteBody(string Blueprint, float X, float Z, float YawDegrees, string Note);

/// <summary>
/// A site file (factory-generator.md 6, the user's answer 3: one body per building plus a site file), <c>Constructs/Factories/Sites/*.json</c>, format
/// <c>call-of-slop-factory-site/1</c>: which blueprints stand together and where. The ore loop's site is the extraction post and the refinery on one road
/// line, the refinery downstream (east) of the post past a gap of open ground, and an Ore Hauler parked on the post's road under its loading skip. Written by
/// <c>--gen-factory site [--seed N]</c> from the plans themselves (never by hand); <c>--haul-check</c> spawns a site's bodies where it says.
/// Each body still spawns alone from the Blueprints tab; a whole-site spawn in play is not done yet (factory-generator-notes.md, Open).
/// </summary>
public sealed class FactorySite
{
    public const string Format = "call-of-slop-factory-site/1";
    public const float Gap = 12f; // open ground between two buildings on one road line

    public string Name = "";
    public string About = "";
    public readonly List<SiteBody> Bodies = new();

    public static string Folder => Path.Combine(AppFolders.Data, "Constructs", "Factories", "Sites");

    public static string RepoFolder => Path.GetFullPath(Path.Combine(VehicleKit.VehicleRegistry.RepoBlueprints(), "..", "Factories", "Sites"));

    public static string FileName(string name) => name.Replace(" ", "") + ".json";

    /// <summary>The ore loop's site for seed <paramref name="seed"/>: post <paramref name="seed"/>, refinery <paramref name="seed"/>, Ore Hauler 1.</summary>
    public static FactorySite OreLoop(string style, int seed)
    {
        var post = FactoryScript.Planned(new FactoryBrief { Function = "extraction", Style = style, Seed = seed });
        var skip = post.Plan.CellOf(post.P.Of("loading").First()) ?? throw new FactoryKitException("the post has no loading auger");
        var s = new FactorySite { Name = $"{style} Ore Loop {seed}" };
        s.About = $"The ore loop (factory-generator.md phase 1): {style} Extraction Post {seed} over a deposit, {style} Refinery {seed} downstream on the same road line, an Ore Hauler under the post's loading auger. Flip LOAD on the auger's post, drive east along the road to the refinery's Tipping Grate (the grate on your left, your left wheels along its edge) and flip TIP behind the cab.";
        var postName = FactoryScript.BlueprintName(new FactoryBrief { Function = "extraction", Style = style, Seed = seed });
        var refName = FactoryScript.BlueprintName(new FactoryBrief { Function = "refinery", Style = style, Seed = seed });
        s.Bodies.Add(new SiteBody(postName, 0, 0, 0, "over the deposit, its drills along the back edge"));
        s.Bodies.Add(new SiteBody(refName, post.Plan.Length + Gap, 0, 0, "downstream, the same road line"));
        // The hauler's bed middle under the auger's spout (FactoryScript.Loading: over the road's middle), facing east.
        s.Bodies.Add(new SiteBody($"{style} Ore Hauler 1", skip.X, FactoryPlan.SpoutZ, 90, "its bed's middle, facing east, under the loading auger's spout"));
        return s;
    }

    public string Text()
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"format\": \"{Format}\",\n");
        sb.Append($"  \"name\": {JsonSerializer.Serialize(Name)},\n");
        sb.Append($"  \"about\": {JsonSerializer.Serialize(About)},\n");
        sb.Append("  \"bodies\": [\n");
        for (var k = 0; k < Bodies.Count; k++)
        {
            var b = Bodies[k];
            sb.Append($"    {{ \"blueprint\": {JsonSerializer.Serialize(b.Blueprint)}, \"at\": [{FactoryData.N(b.X)}, {FactoryData.N(b.Z)}], \"yaw\": {FactoryData.N(b.YawDegrees)}, \"note\": {JsonSerializer.Serialize(b.Note)} }}{(k + 1 < Bodies.Count ? "," : "")}\n");
        }
        sb.Append("  ]\n}\n");
        return sb.ToString();
    }

    /// <summary>Reads a site file; refuses a wrong format or a body with no blueprint, with the reason.</summary>
    public static FactorySite Parse(string json, string file)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!r.TryGetProperty("format", out var f) || f.GetString() != Format)
            throw new FactoryKitException($"{file}: not a {Format} file");
        var s = new FactorySite { Name = r.GetProperty("name").GetString() ?? "", About = r.TryGetProperty("about", out var a) ? a.GetString() ?? "" : "" };
        foreach (var b in r.GetProperty("bodies").EnumerateArray())
        {
            var at = b.GetProperty("at");
            var name = b.GetProperty("blueprint").GetString();
            if (string.IsNullOrEmpty(name))
                throw new FactoryKitException($"{file}: a body names no blueprint");
            s.Bodies.Add(new SiteBody(name, at[0].GetSingle(), at[1].GetSingle(), b.TryGetProperty("yaw", out var y) ? y.GetSingle() : 0, b.TryGetProperty("note", out var n) ? n.GetString() ?? "" : ""));
        }
        return s;
    }

    /// <summary>A body's plan origin in site metres, turned by its yaw (degrees, about up): where plan point <paramref name="p"/> of that body is on the site.</summary>
    public static Vector3 OnSite(SiteBody b, Vector3 p)
    {
        var r = Quaternion.CreateFromAxisAngle(Vector3.UnitY, b.YawDegrees * MathF.PI / 180f);
        return new Vector3(b.X, 0, b.Z) + Vector3.Transform(p, r);
    }
}
