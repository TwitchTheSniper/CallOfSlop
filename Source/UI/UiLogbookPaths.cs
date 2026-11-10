using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The Logbook's Paths page (docs/story/STORY.md "Paths"): the player's own branch chart. Each storylet they have had
/// is a box in its plot's lane, oldest on the left, with its ends beside it: the one it came to is lit and named, the
/// others stay locked ("?"), and a line runs from the end taken to what came of it. Under it, the storylet qualities
/// as tracks (Rank, Suspicion) and a line per person from hostile to trusted, with a marker where the player stands.
/// It reads what the host sent (Game.StoryPaths, StoryTracks, StoryStandings); hovering a box says what it changed.
/// </summary>
public sealed partial class Ui
{
    private static readonly ImColor8 PathBox = new(30, 34, 41, 255), PathBoxNow = new(52, 40, 24, 255), PathLit = new(36, 62, 70, 255), PathLocked = new(24, 26, 31, 255),
        PathLine = new(70, 76, 88, 255), PathLineLit = new(96, 196, 206, 255), PathEdge = new(84, 92, 106, 255), PathEdgeNow = new(240, 150, 40, 255),
        PathText = new(226, 228, 234, 255), PathDim = new(132, 138, 150, 255), PathLane = new(255, 255, 255, 10),
        PathBad = new(214, 96, 84, 255), PathGood = new(110, 200, 130, 255), PathMid = new(150, 154, 164, 255);
    private static readonly string[] PathLanes = ["The main road", "On the side", "Trouble"];
    private static readonly string[] PathBands = ["Hostile", "Cold", "Neutral", "Warm", "Trusted"];
    private int _pathShown = -1; // how many steps the chart last drew (a new one scrolls it into view)
    private readonly int[] _pathLaneEnds = new int[3];
    private readonly float[] _pathLaneTop = new float[3];
    private readonly float2[] _pathNode = new float2[Game.StoryPathsMax]; // each step's box (its top left), this frame
    private readonly int[] _pathFrom = new int[Game.StoryPathsMax]; // the step it follows (-1 none), and by which end (-1 the box itself)
    private readonly int[] _pathFromEnd = new int[Game.StoryPathsMax];

    private void DrawLogbookPaths(Game game)
    {
        var sr = Game.StoryData.Storylets;
        var steps = game.StoryPaths;
        if (steps.Count == 0)
            ImGuiSafe.TextColored(Dim, "Nothing charted yet. What you do, and what you choose, is drawn here as it happens.");
        else
            DrawPathChart(sr, steps);
        ImGui.Spacing();
        if (game.StoryTracks.Count > 0)
        {
            ImGui.Separator();
            ImGuiSafe.TextWrapped("Your record");
            foreach (var (q, value) in game.StoryTracks)
            {
                var def = sr.Qualities[q];
                DrawPathTrack(def.Name, value, def.Min, def.Max, value.ToString(), def.Menace ? PathBad : PathLineLit, false);
            }
        }
        ImGui.Spacing();
        ImGui.Separator();
        ImGuiSafe.TextWrapped("Where you stand");
        if (game.StoryStandings.Count == 0)
            ImGuiSafe.TextColored(Dim, "Nobody has made up their mind about you yet.");
        foreach (var (name, standing) in game.StoryStandings)
        {
            var band = standing <= -60 ? 0 : standing <= -20 ? 1 : standing < 20 ? 2 : standing < 60 ? 3 : 4;
            DrawPathTrack(name, standing, -100, 100, PathBands[band], band < 2 ? PathBad : band > 2 ? PathGood : PathMid, true);
        }
    }

    /// <summary>The chart: lanes by plot, a column per storylet in the order they came.</summary>
    private void DrawPathChart(StoryletRules sr, List<StoryPathStep> steps)
    {
        float nodeW = 146 * _scale, nodeH = 42 * _scale, endW = 124 * _scale, endH = 22 * _scale, endGap = 5 * _scale, stub = 22 * _scale, colGap = 36 * _scale, pad = 10 * _scale;
        var colW = nodeW + stub + endW + colGap;
        Array.Clear(_pathLaneEnds);
        foreach (var st in steps)
        {
            var lane = (int)sr.Storylets[st.Def].Bucket is var b and >= 0 and < 3 ? b : 0;
            _pathLaneEnds[lane] = Math.Max(_pathLaneEnds[lane], Math.Max(1, sr.Storylets[st.Def].Ends.Length));
        }
        var height = pad;
        for (var l = 0; l < 3; l++)
        {
            _pathLaneTop[l] = height;
            if (_pathLaneEnds[l] > 0)
                height += Math.Max(nodeH, _pathLaneEnds[l] * (endH + endGap) - endGap) + 30 * _scale;
        }
        var width = pad * 2 + steps.Count * colW - colGap;
        var line = ImGui.GetTextLineHeight();
        if (!ImGui.BeginChild("##logpaths"u8, new float2(0, height + 34 * _scale), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar))
        {
            ImGui.EndChild();
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        var o = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new float2(width, height));
        if (_pathShown != steps.Count)
        {
            _pathShown = steps.Count;
            ImGui.SetScrollX(Math.Max(0, width - ImGui.GetWindowWidth() + 2 * pad)); // (something new: the newest in view)
        }

        // Lanes, and where each box sits.
        for (var l = 0; l < 3; l++)
        {
            if (_pathLaneEnds[l] == 0)
                continue;
            var laneH = Math.Max(nodeH, _pathLaneEnds[l] * (endH + endGap) - endGap) + 22 * _scale;
            dl.AddRectFilled(new float2(o.X, o.Y + _pathLaneTop[l]), new float2(o.X + Math.Max(width, ImGui.GetWindowWidth()), o.Y + _pathLaneTop[l] + laneH), PathLane, 4 * _scale);
            DlText(dl, new float2(ImGui.GetWindowPos().X + pad, o.Y + _pathLaneTop[l] + 2 * _scale), PathDim, PathLanes[l]); // (stays put as the chart scrolls)
        }
        for (var i = 0; i < steps.Count; i++)
        {
            var d = sr.Storylets[steps[i].Def];
            var lane = (int)d.Bucket is var b and >= 0 and < 3 ? b : 0;
            var laneH = Math.Max(nodeH, _pathLaneEnds[lane] * (endH + endGap) - endGap);
            _pathNode[i] = new float2(o.X + pad + i * colW, o.Y + _pathLaneTop[lane] + 20 * _scale + (laneH - nodeH) / 2);
            _pathFrom[i] = -1;
            _pathFromEnd[i] = -1;
        }

        // What led to what: an end taken that names the next storylet; failing that, the one before it in its lane.
        for (var i = 0; i < steps.Count; i++)
        {
            var d = sr.Storylets[steps[i].Def];
            for (var k = 0; k < d.Ends.Length; k++)
            {
                if ((steps[i].Ends >> k & 1) == 0 || d.Ends[k].Next.Length == 0)
                    continue;
                for (var j = i + 1; j < steps.Count; j++)
                    if (_pathFrom[j] < 0 && sr.Storylets[steps[j].Def].Id.Equals(d.Ends[k].Next, StringComparison.OrdinalIgnoreCase))
                    {
                        (_pathFrom[j], _pathFromEnd[j]) = (i, k);
                        break;
                    }
            }
        }
        for (var j = 1; j < steps.Count; j++)
        {
            if (_pathFrom[j] >= 0)
                continue;
            for (var i = j - 1; i >= 0; i--)
                if (sr.Storylets[steps[i].Def].Bucket == sr.Storylets[steps[j].Def].Bucket)
                {
                    _pathFrom[j] = i;
                    var d = sr.Storylets[steps[i].Def];
                    for (var k = 0; k < d.Ends.Length && _pathFromEnd[j] < 0; k++)
                        if ((steps[i].Ends >> k & 1) != 0)
                            _pathFromEnd[j] = k;
                    break;
                }
        }
        for (var j = 0; j < steps.Count; j++)
        {
            if (_pathFrom[j] < 0)
                continue;
            var i = _pathFrom[j];
            var from = _pathFromEnd[j] >= 0 ? PathEndAt(sr, steps, i, _pathFromEnd[j], nodeW, nodeH, endW, endH, endGap, stub) + new float2(endW, endH / 2) : _pathNode[i] + new float2(nodeW, nodeH / 2);
            var to = _pathNode[j] + new float2(0, nodeH / 2);
            var mid = to.X - colGap / 2;
            dl.AddLine(from, new float2(mid, from.Y), PathLineLit, 2 * _scale);
            dl.AddLine(new float2(mid, from.Y), new float2(mid, to.Y), PathLineLit, 2 * _scale);
            dl.AddLine(new float2(mid, to.Y), to, PathLineLit, 2 * _scale);
        }

        // The boxes over the lines.
        var mouse = ImGui.GetMousePos();
        var hovering = ImGui.IsWindowHovered();
        string? tip = null;
        for (var i = 0; i < steps.Count; i++)
        {
            var st = steps[i];
            var d = sr.Storylets[st.Def];
            var at = _pathNode[i];
            var p1 = at + new float2(nodeW, nodeH);
            var now = st.State != 0;
            for (var k = 0; k < d.Ends.Length; k++)
            {
                var e0 = PathEndAt(sr, steps, i, k, nodeW, nodeH, endW, endH, endGap, stub);
                var e1 = e0 + new float2(endW, endH);
                var had = (st.Ends >> k & 1) != 0;
                var col = had ? PathLineLit : PathLine;
                var midX = at.X + nodeW + stub / 2;
                dl.AddLine(new float2(p1.X, at.Y + nodeH / 2), new float2(midX, at.Y + nodeH / 2), col, (had ? 2 : 1) * _scale);
                dl.AddLine(new float2(midX, at.Y + nodeH / 2), new float2(midX, e0.Y + endH / 2), col, (had ? 2 : 1) * _scale);
                dl.AddLine(new float2(midX, e0.Y + endH / 2), new float2(e0.X, e0.Y + endH / 2), col, (had ? 2 : 1) * _scale);
                dl.AddRectFilled(e0, e1, had ? PathLit : PathLocked, 10 * _scale);
                dl.AddRect(e0, e1, had ? PathLineLit : PathEdge, 10 * _scale, ImDrawFlags.None, 1f * _scale);
                var label = st.EndLabel[k];
                var tw = had ? 0 : ImGui.CalcTextSize(label).X;
                DlText(dl, had ? new float2(e0.X + 9 * _scale, e0.Y + (endH - line) / 2) : new float2(e0.X + (endW - tw) / 2, e0.Y + (endH - line) / 2), had ? PathText : PathDim, label, new float4(e0.X, e0.Y, e1.X - 6 * _scale, e1.Y));
                if (hovering && mouse.X >= e0.X && mouse.X < e1.X && mouse.Y >= e0.Y && mouse.Y < e1.Y)
                    tip = had ? $"{label}\n{st.EndTip[k]}" : st.EndTip[k];
            }
            dl.AddRectFilled(at, p1, now ? PathBoxNow : PathBox, 4 * _scale);
            dl.AddRect(at, p1, now ? PathEdgeNow : PathEdge, 4 * _scale, ImDrawFlags.None, (now ? 2f : 1f) * _scale);
            var clip = new float4(at.X, at.Y, p1.X - 6 * _scale, p1.Y);
            DlText(dl, new float2(at.X + 8 * _scale, at.Y + 4 * _scale), PathText, st.Title, clip);
            if (st.Sub.Length > 0)
                DlText(dl, new float2(at.X + 8 * _scale, at.Y + 4 * _scale + line), now ? PathEdgeNow : PathDim, st.Sub, clip);
            if (hovering && mouse.X >= at.X && mouse.X < p1.X && mouse.Y >= at.Y && mouse.Y < p1.Y)
                tip = st.Tip.Length > 0 ? $"{st.Title}\n{st.Tip}" : st.Title;
        }
        if (tip != null)
            ImGuiSafe.WrappedTooltip(tip, 320 * _scale);
        ImGui.EndChild();
    }

    /// <summary>An end's pill (its top left): stacked beside its storylet's box, centred on it.</summary>
    private float2 PathEndAt(StoryletRules sr, List<StoryPathStep> steps, int step, int end, float nodeW, float nodeH, float endW, float endH, float endGap, float stub)
    {
        var n = sr.Storylets[steps[step].Def].Ends.Length;
        var at = _pathNode[step];
        var all = n * (endH + endGap) - endGap;
        return new float2(at.X + nodeW + stub, at.Y + nodeH / 2 - all / 2 + end * (endH + endGap));
    }

    /// <summary>A line with a marker on it: a name on the left, the value's place between min and max, and a word for it on the right.</summary>
    private void DrawPathTrack(string name, int value, int min, int max, string word, ImColor8 col, bool centre)
    {
        var dl = ImGui.GetWindowDrawList();
        var at = ImGui.GetCursorScreenPos();
        var h = ImGui.GetTextLineHeight() + 8 * _scale;
        var full = ImGui.GetContentRegionAvail().X;
        float nameW = Math.Min(170 * _scale, full * 0.34f), wordW = 76 * _scale;
        float x0 = at.X + nameW, x1 = at.X + full - wordW;
        ImGui.Dummy(new float2(full, h));
        if (x1 - x0 < 40 * _scale)
            x1 = x0 + 40 * _scale;
        var y = at.Y + h / 2;
        var t = max > min ? Math.Clamp((value - min) / (float)(max - min), 0f, 1f) : 0f;
        var x = x0 + (x1 - x0) * t;
        var zero = centre ? (x0 + x1) / 2 : x0;
        DlText(dl, new float2(at.X, at.Y + 4 * _scale), PathText, name, new float4(at.X, at.Y, x0 - 8 * _scale, at.Y + h));
        dl.AddLine(new float2(x0, y), new float2(x1, y), PathLine, 2 * _scale);
        for (var k = 0; k <= 4; k++)
        {
            var tx = x0 + (x1 - x0) * k / 4;
            dl.AddLine(new float2(tx, y - 4 * _scale), new float2(tx, y + 4 * _scale), PathLine, 1 * _scale);
        }
        dl.AddLine(new float2(zero, y), new float2(x, y), col, 3 * _scale);
        dl.AddCircleFilled(new float2(x, y), 5.5f * _scale, col);
        DlText(dl, new float2(x1 + 10 * _scale, at.Y + 4 * _scale), col, word);
    }
}
