using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>A speaker's settings as one value: what the sound and the configure panel's picture both work from.</summary>
public struct SpeakerShape
{
    public bool Directional, Linear;
    public float Range, Near, Rolloff, Inner, Outer, OuterGain;

    /// <summary>The level at a distance and an angle off the speaker's front (degrees, 0..180). The one rule, so the picture shows what is heard.</summary>
    public readonly float Level(float distance, float angle)
    {
        if (distance >= Range)
            return 0;
        var over = MathF.Max(0, distance - Near);
        var level = Linear ? MathF.Max(0, 1 - Rolloff * over / MathF.Max(0.01f, Range - Near)) : Near / (Near + Rolloff * over);
        level *= MediaDirector.EdgeFade(distance, Range);
        if (!Directional)
            return level;
        var inner = Inner * 0.5f;
        var outer = MathF.Max(Outer * 0.5f, inner);
        var cone = angle <= inner ? 1 : angle >= outer ? OuterGain : 1 + (OuterGain - 1) * (angle - inner) / MathF.Max(0.01f, outer - inner);
        return level * cone;
    }
}

/// <summary>
/// Speakers (docs/sandbox/MEDIA.md, "Speakers"): a Data cable from a Video Screen or Music Player into a speaker repeats its sound. A cable, so the linking tool, world links
/// between constructs, saves and blueprints are the ones every link has; cut it and the speaker has nothing to say. Each peer works out what it hears (<see cref="MediaDirector"/>).
/// </summary>
public static class ConstructSpeakers
{
    /// <summary>A part file's <c>"speaker"</c> key: how near it holds full level.</summary>
    public static void Parse(ConstructPartDef d, JsonElement root, string file)
    {
        if (ConstructParts.Opt(root, "speaker") is not { } s)
            return;
        ConstructParts.Known(s, ["near"], $"{file}, speaker");
        d.IsSpeaker = true;
        d.SpeakerNear = ConstructParts.Num(ConstructParts.Need(s, "near", $"{file}, speaker"), file, "speaker.near");
        ConstructParts.Range(d.SpeakerNear, 0.5f, 50, file, "speaker.near");
    }

    public static SpeakerShape Shape(in CPart p, ConstructPartDef d) => new()
    {
        Directional = ConstructConfig.Get(p, d, CfgKey.SpeakerMode, 0) >= 0.5f,
        Linear = ConstructConfig.Get(p, d, CfgKey.Curve, 0) >= 0.5f,
        Range = ConstructConfig.Get(p, d, CfgKey.HearRange, 40),
        Near = d.SpeakerNear,
        Rolloff = ConstructConfig.Get(p, d, CfgKey.Rolloff, 1),
        Inner = ConstructConfig.Get(p, d, CfgKey.ConeInner, 60),
        Outer = ConstructConfig.Get(p, d, CfgKey.ConeOuter, 120),
        OuterGain = ConstructConfig.Get(p, d, CfgKey.ConeGain, 0.2f),
    };

    /// <summary>The player a speaker is cabled to, or null: a local link, then a world link from another construct.</summary>
    public static Construct? Source(ConstructWorld world, Construct c, int part, out int from)
    {
        from = -1;
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (r.Type == RouteType.Data && r.To == part && r.From < c.PartCount && c.Parts[r.From].D.IsMedia)
            {
                from = r.From;
                return c;
            }
        }
        if (c.XWireIn == 0)
            return null;
        for (var k = 0; k < world.WorldLinkCount; k++)
        {
            ref var l = ref world.WorldLinks[k];
            if (l.Type == RouteType.Data && l.BodyTo == c.Id && l.To == part && world.Get(l.BodyFrom) is { } other && l.From < other.PartCount && other.Parts[l.From].D.IsMedia)
            {
                from = l.From;
                return other;
            }
        }
        return null;
    }
}
