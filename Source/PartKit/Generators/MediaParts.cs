using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The media parts: Constructs/Parts/Media. Video and browser screens, the Music Player and speakers that repeat them
/// (docs/sandbox/CONSTRUCTS.md, "Media"). Was tools/constructs/media_parts.py.
/// </summary>
public sealed class MediaParts : IPartGenerator
{
    public string Name => "media_parts";

    private const string Out = "Constructs/Parts/Media";
    private const string Section = "Media";
    private const string Case = "#23272c", Bezel = "#14181c", Speaker = "#30363d", Grille = "#4a525a";

    private static PyDict Volume() => new()
    {
        ["key"] = "volume", ["label"] = "Volume", ["group"] = "General", ["min"] = 0, ["max"] = 1, ["step"] = 0.05, ["default"] = 0.8,
        ["tooltip"] = "How loud it plays, before distance.",
    };

    private static PyDict Loop() => new()
    {
        ["key"] = "loop", ["label"] = "Loop the list", ["group"] = "General", ["type"] = "toggle", ["default"] = false,
        ["tooltip"] = "After the last track it starts again from the top.",
    };

    private static PyDict Media(int hear, int near, int? width = null, int? height = null, bool web = false)
    {
        var m = new PyDict { ["hear"] = hear, ["near"] = near };
        if (web)
            m["web"] = true;
        if (width is { } w && w != 0)
        {
            m["width"] = w;
            m["height"] = height;
        }
        return m;
    }

    private static Part Video(string name, Cls cls, double w, double h, int rangeM, int hear, int near, int power, int mass, bool web = false)
    {
        var desc = $"Plays YouTube links and the game's own tracks: paste a link with the configure tool. Seen from {Py.G(rangeM)} m, heard to {Py.G(hear)} m.";
        if (web)
            desc = $"A web page on a screen: type the address with the configure tool, and use it with Hands. Private: nothing is saved. Seen from {Py.G(rangeM)} m.";
        var p = new Part(name, cls, Section, desc, role: "panel", tool: mass <= 40 ? "wrench" : "welder", group: "Logic", color: Case);
        var t = cls == Small ? 0.05 : 0.12;
        var rim = cls == Small ? 0.012 : 0.03;
        var proud = cls == Small ? 0.004 : 0.008;
        p.Add(Box([-w / 2, -h / 2, 0], [w / 2, h / 2, t]));
        p.Add(Box([-w / 2 + rim, -h / 2 + rim, -proud], [w / 2 - rim, h / 2 - rim, 0.002], Bezel, detail: true));
        p.Sock("back", [0, 0, t], "+z");
        p.Sock("-y", [0, -h / 2, t / 2], "-y");
        p.Sock("+y", [0, h / 2, t / 2], "+y");
        p.Sock("-x", [-w / 2, 0, t / 2], "-x");
        p.Sock("+x", [w / 2, 0, t / 2], "+x");
        p.Stats(mass, 50, 1, 1);
        double pw = w - 2 * rim, ph = h - 2 * rim;
        pw = Math.Min(pw, Py.Round(ph * 16 / 9, 3));
        p.Set(("power", power), ("config", web ? Py.L(Volume()) : Py.L(Volume(), Loop())), ("centreSockets", false), ("media", Media(hear, near, web: web)),
            ("panels", Py.L(new PyDict
            {
                ["source"] = "media", ["at"] = Py.L(0, 0, Py.Round(-proud - 0.003, 3)), ["size"] = Py.L(Py.Round(pw, 3), Py.Round(pw * 9 / 16, 3)),
                ["face"] = "-z", ["up"] = "+y", ["rows"] = 8, ["range"] = rangeM,
            })));
        return p;
    }

    private static Part Music()
    {
        var desc = "Plays a list of YouTube links and the game's own tracks: build the list with the configure tool, and use its screen with Hands. Heard to 50 m. The game's tracks need no browser.";
        var p = new Part("Music Player", Small, Section, desc, role: "panel", tool: "wrench", group: "Logic", color: Speaker);
        double w = 0.36, h = 0.36, t = 0.14;
        var rim = 0.008;
        p.Add(Box([-w / 2, 0, 0], [w / 2, h, t]));
        p.Add(Box([-w / 2 + rim, rim, -0.004], [w / 2 - rim, h - rim, 0.002], Bezel, detail: true));
        p.Sock("bottom", [0, 0, t / 2], "-y");
        p.Sock("back", [0, h / 2, t], "+z");
        p.Sock("top", [0, h, t / 2], "+y");
        p.Stats(6, 50, 1, 1);
        var pw = Py.Round(w - 2 * rim, 3);
        var bottom = 0.44; // YouTube's player stays in view above the controls: 16:9 across the whole width, the title and buttons under it
        p.Set(("power", 10), ("config", Py.L(Volume(), Loop())), ("centreSockets", false), ("media", Media(50, 5, 356, 200)),
            ("panels", Py.L(new PyDict
            {
                ["source"] = "media", ["at"] = Py.L(0, h / 2, -0.007), ["size"] = Py.L(pw, pw), ["face"] = "-z", ["up"] = "+y", ["rows"] = 8, ["range"] = 12,
                ["picture"] = Py.L(0, bottom, 1, 1),
                ["touch"] = Py.L(
                    new PyDict { ["action"] = "media.previous", ["at"] = Py.L(0.02, 0.03, 0.32, 0.24), ["label"] = "Prev" },
                    new PyDict { ["action"] = "media.toggle", ["at"] = Py.L(0.35, 0.03, 0.65, 0.24), ["label"] = "Play" },
                    new PyDict { ["action"] = "media.next", ["at"] = Py.L(0.68, 0.03, 0.98, 0.24), ["label"] = "Next" }),
            })));
        return p;
    }

    private static Part SpeakerPart(string name, Cls cls, double w, double h, double t, int reach, int def, int near, int power, int mass)
    {
        var desc = $"Repeats a Video Screen or Music Player: run a data cable to it with the linking tool. Range up to {Py.G(reach)} m, every way or as a cone.";
        var p = new Part(name, cls, Section, desc, role: "panel", tool: mass <= 40 ? "wrench" : "welder", group: "Logic", color: Speaker);
        var proud = cls == Small ? 0.004 : 0.012;
        p.Add(Box([-w / 2, 0, 0], [w / 2, h, t]));
        // Two drivers on a front grille, each standing a little out of the one behind it.
        p.Add(Box([-w * 0.42, h * 0.05, -proud], [w * 0.42, h * 0.95, 0.002], Bezel, detail: true));
        foreach (var (cy, r) in new[] { (h * 0.68, w * 0.26), (h * 0.27, w * 0.34) })
            p.Add(Box([-r, cy - r, -proud * 2], [r, cy + r, -proud / 2], Grille, detail: true));
        p.Sock("bottom", [0, 0, t / 2], "-y");
        p.Sock("back", [0, h / 2, t], "+z");
        p.Sock("top", [0, h, t / 2], "+y");
        p.Stats(mass, 40, 1, 1);
        var config = Py.L(
            Volume().Update(("label", "Loudness"), ("group", "Sound")),
            new PyDict { ["key"] = "hearRange", ["label"] = "Range", ["group"] = "Sound", ["min"] = 5, ["max"] = reach, ["step"] = 1, ["default"] = def, ["unit"] = "m", ["tooltip"] = "Past this it is silent." },
            new PyDict { ["key"] = "curve", ["label"] = "Falloff", ["group"] = "Sound", ["type"] = "choice", ["options"] = Py.L("Inverse", "Linear"), ["default"] = 0, ["tooltip"] = "Inverse falls fast near it, like a real speaker. Linear falls evenly to the range." },
            new PyDict { ["key"] = "rolloff", ["label"] = "Falloff rate", ["group"] = "Sound", ["min"] = 0.1, ["max"] = 4, ["step"] = 0.1, ["default"] = 1, ["tooltip"] = "How fast it falls: 1 is natural." },
            new PyDict { ["key"] = "speakerMode", ["label"] = "Pattern", ["group"] = "Direction", ["type"] = "choice", ["options"] = Py.L("Every way", "Directional"), ["default"] = 0, ["tooltip"] = "Directional aims it along the part's front." },
            new PyDict { ["key"] = "coneInner", ["label"] = "Inner cone", ["group"] = "Direction", ["min"] = 0, ["max"] = 360, ["step"] = 5, ["default"] = 60, ["unit"] = "deg", ["tooltip"] = "Full level inside this angle." },
            new PyDict { ["key"] = "coneOuter", ["label"] = "Outer cone", ["group"] = "Direction", ["min"] = 0, ["max"] = 360, ["step"] = 5, ["default"] = 150, ["unit"] = "deg", ["tooltip"] = "It fades to the outer level by this angle." },
            new PyDict { ["key"] = "coneGain", ["label"] = "Outer level", ["group"] = "Direction", ["min"] = 0, ["max"] = 1, ["step"] = 0.05, ["default"] = 0.15, ["tooltip"] = "How loud it is behind the cone." });
        p.Set(("power", power), ("config", config), ("centreSockets", false), ("speaker", new PyDict { ["near"] = near }));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out,
    [
        ("", Music()),
        ("", Video("Browser Screen", Small, 1.0, 0.6, 25, 40, 4, 40, 6, web: true)),
        ("", Video("Medium Browser Screen", Medium, 3, 1.75, 60, 80, 8, 150, 60, web: true)),
        ("", Video("Large Browser Screen", Medium, 8, 4.6, 150, 160, 16, 400, 240, web: true)),
        ("", SpeakerPart("Speaker", Small, 0.26, 0.42, 0.2, 40, 25, 3, 15, 5)),
        ("", SpeakerPart("Medium Speaker", Medium, 1, 1.6, 0.7, 100, 60, 6, 60, 40)),
        ("", SpeakerPart("Large Speaker", Medium, 2.2, 3.6, 1.4, 250, 150, 12, 200, 180)),
        ("", Video("Video Screen", Small, 1.0, 0.6, 25, 40, 4, 40, 6)),
        ("", Video("Medium Video Screen", Medium, 3, 1.75, 60, 80, 8, 150, 60)),
        ("", Video("Large Video Screen", Medium, 8, 4.6, 150, 160, 16, 400, 240)),
    ]);
}
