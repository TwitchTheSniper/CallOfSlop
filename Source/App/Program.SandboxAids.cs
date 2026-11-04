using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>What the held part's click does (the build bar's mode). The mirror and the gantry are switches on top of it.</summary>
public enum BuildMode : byte
{
    Place, // a click fits the part, as ever
    Line, // press and drag lays a row or field (Line Layer)
    Replace, // a click swaps the aimed part (Replacer)
}

/// <summary>
/// The build stations on this PC (docs/sandbox/CONSTRUCTS.md "Build stations"): the keys, the mode, the mirror's and the line's
/// ghosts, the Replacer's aim and picker, and the build bar. The server decides everything (ConstructEditsAids.cs,
/// ConstructLine.cs, ConstructReplace.cs). Anything kept across frames that names a part is stamped and checked when used.
/// </summary>
public static unsafe partial class Program
{
    private static BuildMode _buildMode;
    private static SbLine _ln;
    private static ushort _aidPick = ConstructParts.None; // the part an aid lays when none is held (a physics-only piece never is; MMB on one, or the inventory)

    /// <summary>The part the mode works with: one picked for the aid first, else what is held. Placing: what is held.</summary>
    private static ConstructPartDef? AidHeld(ConstructPartDef? held) => _buildMode != BuildMode.Place && ConstructParts.Get(_aidPick) is { } pick ? pick : held;

    /// <summary>The inventory's click, with a mode on: a physics-only piece is picked for the aid. True when it took the click.</summary>
    internal static bool AidPick(Game g, ConstructPartDef d)
    {
        if (_buildMode == BuildMode.Place || !d.PhysicsOnly || d.IsExtrusion)
            return false;
        _aidPick = d.Id;
        g.SandboxSay($"{(_buildMode == BuildMode.Line ? "Line" : "Replace")}: {d.Name} - take out the attach tool");
        return true;
    }

    /// <summary>The pick tap, with a mode on: the aimed part's kind is what the aid lays. False: the usual pick.</summary>
    private static bool AidPickAimed(Game g, in ConstructHit hit)
    {
        if (_buildMode == BuildMode.Place || hit.Part < 0 || hit.Part >= hit.Body.PartCount)
            return false;
        var d = hit.Body.DefOf(hit.Part);
        if (!d.PhysicsOnly || d.IsExtrusion)
            return false;
        return AidPick(g, d);
    }

    /// <summary>The Line Layer's stroke: from the press to the release. Its part index is stamped with the part's own kind and place.</summary>
    private struct SbLine
    {
        public bool Active;
        public ushort Body, Part, Def;
        public byte Socket, Variant, Rot, Seg, Normal;
        public Vector3 Start, PartAt;
        public ushort PartDef;
        public float Pitch0, Pitch1, Pitch2, Pitch3, Pitch4, Pitch5; // by direction
        public byte DirU, DirV;
        public int CountU, CountV;
        public long Key;
    }

    private static float LinePitch(in SbLine l, byte d) => d switch { 0 => l.Pitch0, 1 => l.Pitch1, 2 => l.Pitch2, 3 => l.Pitch3, 4 => l.Pitch4, _ => l.Pitch5 };

    /// <summary>The key's aid was refused, or the mode's station went: the reason, as the server would say it.</summary>
    private static string AidOffText(Game g, BuildAid aid)
    {
        var v = g.SandboxAids(g.LocalId);
        var a = (int)aid;
        return BuildAids.Say(aid, v.State[a], v.Range[a], v.Distance[a]);
    }

    /// <summary>Every frame on foot with the mouse captured: the aids' keys, and a mode whose station went goes back to placing.</summary>
    private static void SandboxAidKeys(Game g, ref SandboxHand h)
    {
        var v = g.SandboxAids(g.LocalId);
        if (_ln.Active && h.Tool is ConstructTool.None or ConstructTool.Hands)
            _ln.Active = false; // (the tool put away: the stroke goes)
        if (Pressed(GameAction.SelectTool))
        {
            if (h.Tool == ConstructTool.Selector)
                g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
            else if (g.SandboxAidLive(BuildAid.Select))
                g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Selector);
            else
                g.SandboxSay(AidOffText(g, BuildAid.Select));
        }
        if (h.Tool == ConstructTool.Selector)
            return; // (its actions share these keys: Keybinds.Overlap)
        if (Pressed(GameAction.BuildMirror))
        {
            if (!g.SandboxAidLive(BuildAid.Mirror))
                g.SandboxSay(AidOffText(g, BuildAid.Mirror));
            else
                SandboxMirrorSend(g, (byte)((v.Mirror + 1) % (ConstructMirror.Axes + 1)), v.MirrorOffset);
        }
        if (Pressed(GameAction.BuildLine))
            SandboxSetMode(g, _buildMode == BuildMode.Line ? BuildMode.Place : BuildMode.Line, BuildAid.Line);
        if (Pressed(GameAction.BuildReplace))
            SandboxSetMode(g, _buildMode == BuildMode.Replace ? BuildMode.Place : BuildMode.Replace, BuildAid.Replace);
        var step = Pressed(GameAction.BuildPlaneUp) ? 1 : Pressed(GameAction.BuildPlaneDown) ? -1 : 0;
        if (step != 0 && !GantryOn && v.Mirror != 0 && g.SandboxAidLive(BuildAid.Mirror))
            SandboxMirrorSend(g, v.Mirror, v.MirrorOffset + step * 0.125f);
        if (_buildMode == BuildMode.Line && !g.SandboxAidLive(BuildAid.Line) || _buildMode == BuildMode.Replace && !g.SandboxAidLive(BuildAid.Replace))
        {
            g.SandboxSay(AidOffText(g, _buildMode == BuildMode.Line ? BuildAid.Line : BuildAid.Replace));
            _buildMode = BuildMode.Place;
            _ln.Active = false;
        }
    }

    private static void SandboxSetMode(Game g, BuildMode mode, BuildAid aid)
    {
        if (mode != BuildMode.Place && !g.SandboxAidLive(aid))
        {
            g.SandboxSay(AidOffText(g, aid));
            return;
        }
        _buildMode = mode;
        _aidPick = ConstructParts.None;
        _ln.Active = false;
    }

    private static void SandboxMirrorSend(Game g, byte axis, float offset)
    {
        var w = g.SandboxAidBegin(BuildAidOp.Mirror);
        w.U8(axis).F32(offset);
        g.SandboxAidSend(w);
    }

    private static readonly List<AidGhost> _aidWork = new(ConstructRender.MaxAidGhosts);
    private static long _mirrorKey = long.MinValue;
    private static int _mirrorCount;

    /// <summary>The held part's image while its ghost shows (worked out again only when the ghost, the plane or the construct changed).</summary>
    private static void SandboxMirrorGhost(Game g, Construct c, ConstructPartDef held, in ConstructGhost gh)
    {
        var v = g.SandboxAids(g.LocalId);
        if (v.Mirror == 0 || c.FreeformCount > 0 && c.FrameOf(gh.Seg) != 0)
        {
            _mirrorCount = 0;
            return;
        }
        ConstructMirror.Plane(c, v.Mirror, v.MirrorOffset, out var ax, out var w);
        ConstructRender.ShowPlane(c, ax, w);
        var key = HashCode.Combine(c.Id, held.Id, gh.Pos, gh.Rot, c.PartCount, ax, w);
        if (key != _mirrorKey)
        {
            _mirrorKey = key;
            _mirrorCount = 0;
            var t = ConstructMirror.Of(held, gh.Pos, gh.Rot, ax, w);
            if (!t.OnPlane)
            {
                var ok = ConstructAid.FindFit(c, t.Def, t.Pos, t.Rot, 0, out _);
                ConstructRender.AidGhosts[0] = new AidGhost { Def = t.Def, Pos = t.Pos, Rot = t.Rot, Ok = ok };
                _mirrorCount = 1;
                _mirrorProblem = !ok ? "no free socket on the other side" : !t.Exact ? "no mirrored form: turned to fit" : "";
            }
            else
                _mirrorProblem = "";
        }
        if (_mirrorCount > 0)
            ConstructRender.ShowAidGhosts(c, gh.Seg, _mirrorCount);
    }

    private static string _mirrorProblem = "";

    // ---- The Line Layer's stroke ---------------------------------------------------------------------------------

    /// <summary>The press: the stroke starts here. Nothing is sent until the release: a press with no drag is one part.</summary>
    private static void SandboxLineStart(Construct c, ConstructPartDef held, in ConstructGhost gh, int variant, Vector3 eye, Vector3 dir)
    {
        _mirrorKey = long.MinValue; // (the run's ghosts take over the list the mirror's image is kept in)
        c.WorldFrame(c.Parts[gh.Part].Seg, out var q, out _);
        var rest = Vector3.Abs(Vector3.Transform(dir, Quaternion.Conjugate(q)));
        ref var p = ref c.Parts[gh.Part];
        _ln = new SbLine
        {
            Active = true, Body = c.Id, Part = (ushort)gh.Part, Def = held.Id, Socket = (byte)gh.Socket, Variant = (byte)variant, Rot = gh.Rot, Seg = gh.Seg,
            Normal = (byte)(rest.X >= rest.Y && rest.X >= rest.Z ? 0 : rest.Y >= rest.Z ? 1 : 2), Start = gh.Pos, PartAt = p.Pos, PartDef = p.Def,
            Pitch0 = ConstructLine.Pitch(c, held, gh.Rot, gh, 0), Pitch1 = ConstructLine.Pitch(c, held, gh.Rot, gh, 1), Pitch2 = ConstructLine.Pitch(c, held, gh.Rot, gh, 2),
            Pitch3 = ConstructLine.Pitch(c, held, gh.Rot, gh, 3), Pitch4 = ConstructLine.Pitch(c, held, gh.Rot, gh, 4), Pitch5 = ConstructLine.Pitch(c, held, gh.Rot, gh, 5),
            CountU = 1, CountV = 1, Key = -1,
        };
    }

    /// <summary>A stroke is running: the drag, the ghosts when the run changed, the count on the look line, and the release sends one action.</summary>
    private static void SandboxLineFrame(Game g, in SandboxHand h, ConstructPartDef? held, Vector3 eye, Vector3 dir)
    {
        ref var l = ref _ln;
        if (g.Constructs.Get(l.Body) is not { } c || held == null || held.Id != l.Def || l.Part >= c.PartCount || c.Parts[l.Part].Def != l.PartDef || c.Parts[l.Part].Pos != l.PartAt)
        {
            l.Active = false; // (the part it started on moved or went: LinkStamp's rule)
            return;
        }
        if (Pressed(GameAction.Aim))
        {
            l.Active = false;
            g.SandboxSay("Line: cancelled");
            return;
        }
        c.WorldFrame(l.Seg, out var q, out var t);
        var inv = Quaternion.Conjugate(q);
        var from = Vector3.Transform(eye - t, inv);
        var ray = Vector3.Transform(dir, inv);
        int n = l.Normal, a = (n + 1) % 3, b = (n + 2) % 3;
        if (MathF.Abs(ray[n]) > 1e-3f && (l.Start[n] - from[n]) / ray[n] is var s and > 0)
        {
            var drag = from + ray * s - l.Start;
            var du = (byte)(a * 2 + (drag[a] >= 0 ? 0 : 1));
            var dv = (byte)(b * 2 + (drag[b] >= 0 ? 0 : 1));
            l.DirU = du;
            l.DirV = dv;
            l.CountU = 1 + (int)MathF.Round(MathF.Abs(drag[a]) / LinePitch(l, du));
            l.CountV = 1 + (int)MathF.Round(MathF.Abs(drag[b]) / LinePitch(l, dv));
            l.CountU = Math.Clamp(l.CountU, 1, ConstructLine.MaxCells);
            l.CountV = Math.Clamp(l.CountV, 1, ConstructLine.MaxCells / l.CountU);
        }
        var cells = l.CountU * l.CountV;
        var key = ((long)l.CountU << 32) | ((long)l.CountV << 16) | ((long)l.DirU << 8) | l.DirV;
        var aids = g.SandboxAids(g.LocalId);
        if (key != l.Key)
        {
            l.Key = key;
            var hasPlane = aids.Mirror != 0 && !(c.FreeformCount > 0 && c.FrameOf(l.Seg) != 0);
            int ax = 0;
            float w = 0;
            if (hasPlane)
                ConstructMirror.Plane(c, aids.Mirror, aids.MirrorOffset, out ax, out w);
            var n2 = 0;
            Vector3 u = ConstructParts.DirVector(l.DirU) * LinePitch(l, l.DirU), v = ConstructParts.DirVector(l.DirV) * LinePitch(l, l.DirV);
            var max = ConstructRender.MaxAidGhosts;
            for (var j = 0; j < l.CountV && n2 < max; j++)
                for (var i = 0; i < l.CountU && n2 < max; i++)
                {
                    var pos = ConstructWorld.Quantise(l.Start + u * i + v * j);
                    ConstructRender.AidGhosts[n2++] = new AidGhost { Def = held, Pos = pos, Rot = l.Rot, Ok = !ConstructBuild.Blocked(c, held, pos, l.Rot, 1, l.Seg) };
                    if (hasPlane && n2 < max)
                    {
                        var img = ConstructMirror.Of(held, pos, l.Rot, ax, w);
                        if (!img.OnPlane)
                            ConstructRender.AidGhosts[n2++] = new AidGhost { Def = img.Def, Pos = img.Pos, Rot = img.Rot, Ok = !ConstructBuild.Blocked(c, img.Def, img.Pos, img.Rot, 1, l.Seg) };
                    }
                }
            _lnShown = n2;
            var copies = hasPlane ? 2 : 1;
            var have = AidHave(g, h, held);
            var need = cells * copies;
            _lnText = SandboxKeys.Line("Line", l.CountV > 1 ? $"{l.CountU} x {l.CountV} = {cells} {held.Name}" : $"{cells} {held.Name}",
                have >= 0 && have < need ? $"{need - have} short" : "", $"{need * held.Mass:0} kg - release: place, {SandboxKeys.Aim}: cancel");
        }
        ConstructRender.ShowAidGhosts(c, l.Seg, _lnShown);
        g.SandboxLookText = _lnText;
        if (!Down(GameAction.Fire))
        {
            var pw = g.SandboxAidBegin(BuildAidOp.Line);
            pw.U16(l.Body).U16(l.Part).U16(l.Def).U8(l.Socket).U8(l.Variant).U8(l.DirU).F32(LinePitch(l, l.DirU)).U16((ushort)l.CountU).U8(l.DirV).F32(LinePitch(l, l.DirV)).U16((ushort)l.CountV);
            g.SandboxAidSend(pw);
            l.Active = false;
            _swallowClick = false;
        }
    }

    private static int _lnShownHint = -1;
    private static string _lnHint = "";

    /// <summary>Line mode with a ghost up: what a press and drag does.</summary>
    private static string SandboxLineHint(ConstructPartDef held)
    {
        if (held.Id != _lnShownHint)
        {
            _lnShownHint = held.Id;
            _lnHint = SandboxKeys.Line("Line", held.Name, null, $"hold {SandboxKeys.Fire} and drag: row or field");
        }
        return _lnHint;
    }

    private static int _lnShown;
    private static string _lnText = "";

    /// <summary>How many of a part the player can lay hands on (-1: creative, no limit).</summary>
    private static int AidHave(Game g, in SandboxHand h, ConstructPartDef d)
    {
        if (g.SandboxInfiniteParts)
            return -1;
        var v = g.SandboxBeacon(g.LocalId);
        if (d.PhysicsOnly)
            return g.Local is { } me ? ConstructAid.LooseCount(g.Constructs, me.Id, d, me.Eye) : 0;
        return g.SandboxInv.Of(g.LocalId).Count(d.Id) + (h.Held == d.Id ? 1 : 0) + (v.On ? v.Counts[d.Id] : 0);
    }

    // ---- The Replacer ---------------------------------------------------------------------------------------------

    private static long _replaceKey = long.MinValue;
    private static string? _replaceWhy;
    private static string _replaceText = "";

    /// <summary>The Replacer's frame: the aimed part, what it would become, and why not. True: this frame's click is taken.</summary>
    private static bool SandboxReplaceInput(Game g, in SandboxHand h, bool aimed, in ConstructHit hit, ConstructPartDef? held)
    {
        if (h.Tool == ConstructTool.None)
            return false;
        if (!aimed || hit.Part < 0 || hit.Part >= hit.Body.PartCount)
        {
            g.SandboxLookText = SandboxKeys.Line("Replace", "aim at a part");
            return true;
        }
        var c = hit.Body;
        var d = c.DefOf(hit.Part);
        ConstructRender.AimBody = c;
        ConstructRender.AimPart = hit.Part;
        ConstructRender.AimHands = false;
        var key = HashCode.Combine(c.Id, hit.Part, d.Id, held?.Id ?? -1, c.PartCount, c.LinkCount, c.RouteCount, SandboxKeys.Stamp);
        if (key != _replaceKey)
        {
            _replaceKey = key;
            _replaceWhy = held != null ? ConstructReplace.Plan(g.Constructs, c, hit.Part, held, out _) : null;
            ConstructRender.AimRefused = _replaceWhy != null;
            _replaceText = held == null ? SandboxKeys.Line("Replace", d.Name, null, SandboxKeys.Do(SandboxKeys.Fire, "pick what to fit"))
                : _replaceWhy != null ? SandboxKeys.Line("Replace", $"{d.Name} with {held.Name}", _replaceWhy)
                : SandboxKeys.Line("Replace", $"{d.Name} with {held.Name}", null, SandboxKeys.Do(SandboxKeys.Fire, "swap"));
        }
        ConstructRender.AimRefused = _replaceWhy != null;
        g.SandboxLookText = _replaceText;
        if (Pressed(GameAction.Fire) && !_swallowClick)
        {
            if (held == null)
            {
                Ui.OpenReplacePicker(g, c, hit.Part);
                SetMouseCaptured(false);
            }
            else if (_replaceWhy != null)
                g.SandboxSay(_replaceWhy);
            else
                SandboxReplaceSend(g, c.Id, hit.Part, held.Id);
        }
        return true;
    }

    internal static void SandboxReplaceSend(Game g, ushort body, int part, ushort id)
    {
        var w = g.SandboxAidBegin(BuildAidOp.Replace);
        w.U16(body).U16((ushort)part).U16(id);
        g.SandboxAidSend(w);
    }

    /// <summary>Per match and mode switch: nothing carries over.</summary>
    private static void SandboxAidsLocalReset()
    {
        _buildMode = BuildMode.Place;
        _aidPick = ConstructParts.None;
        _ln.Active = false;
        _mirrorKey = _replaceKey = long.MinValue;
        GantryLeave();
    }

    // ---- The build bar ---------------------------------------------------------------------------------------------

    private static readonly string[] _barKeys = new string[(int)BuildAid.COUNT];
    private static long _barKey = long.MinValue;
    private static string _barText = "";

    /// <summary>The line under the icons, in the tooltip's format, made only when something it shows changed.</summary>
    private static void BuildBarLine(Game g, BuildAidView v)
    {
        var key = (long)_buildMode | (long)v.Mirror << 4 | (long)(GantryOn ? 1 : 0) << 8 | (long)(_gPlaneStep & 0xfff) << 12 | (long)(SandboxKeys.Stamp & 0xff) << 24 | (long)MathF.Round(v.MirrorOffset * 8) << 32;
        if (key == _barKey)
            return;
        _barKey = key;
        var parts = new List<string>(3);
        if (_buildMode == BuildMode.Line)
            parts.Add(SandboxKeys.Line("Line", "on", null, $"hold {SandboxKeys.Fire} and drag - {SandboxKeys.BuildLine}: off"));
        if (_buildMode == BuildMode.Replace)
            parts.Add(SandboxKeys.Line("Replace", "on", null, $"{SandboxKeys.Fire}: swap - {SandboxKeys.BuildReplace}: off"));
        if (v.Mirror != 0)
            parts.Add(SandboxKeys.Line("Mirror", ConstructMirror.AxisNames[Math.Min((int)v.Mirror, 3)] + (v.MirrorOffset != 0 ? $" {v.MirrorOffset:0.###} m" : ""), null, $"{SandboxKeys.BuildMirror}: next"));
        if (GantryOn)
            parts.Add(SandboxKeys.Line("Gantry", $"plane {_gPlaneStep * GantryPlaneStep:0.##} m", null, SandboxKeys.GantryKeys));
        _barText = string.Join("   ", parts);
    }

    /// <summary>The Replacer's window over the match; it gives the mouse back when it shuts, and shuts when the player can't use it.</summary>
    private static void DrawReplaceInMatch()
    {
        if (!Ui.ReplacePickerOpen)
            return;
        var gone = _session == null || _mouseCaptured || _game is not { SandboxOn: true } g || g.Local is not { Alive: true } || g.Phase != MatchPhase.Playing;
        if (!gone && Ui.DrawReplacePicker(_game!))
            return;
        Ui.CloseReplacePicker();
        if (!MenusOpen)
            SetMouseCaptured(true);
        _swallowClick = true; // (the click that chose it mustn't also place)
    }

    /// <summary>The build bar: a small icon per aid (lit: on here; filled: in use; grey: its station is off or not near), the keys and what is running. From the Sandbox ImGui pass.</summary>
    private static void DrawBuildBar()
    {
        if (_game is not { SandboxOn: true, Local: { Alive: true } me } g || MenusOpen)
            return;
        ref readonly var h = ref g.SandboxHands.Hand(me.Id);
        if (h.Seated)
            return;
        var v = g.SandboxAids(me.Id);
        var free = BuildAids.Free(g.SandboxInfiniteParts);
        var any = false;
        for (var a = 1; a < (int)BuildAid.COUNT; a++)
            any |= v.State[a] != BeaconState.None;
        if (h.Tool is ConstructTool.None or ConstructTool.Hands && !GantryOn && (!any || free))
            return;
        BuildBarLine(g, v);
        _barKeys[(int)BuildAid.Mirror] = SandboxKeys.BuildMirror;
        _barKeys[(int)BuildAid.Line] = SandboxKeys.BuildLine;
        _barKeys[(int)BuildAid.Replace] = SandboxKeys.BuildReplace;
        _barKeys[(int)BuildAid.Gantry] = SandboxKeys.BuildGantry;
        _barKeys[(int)BuildAid.Select] = SandboxKeys.SelTool;
        _barKeys[(int)BuildAid.Blueprint] = SandboxKeys.SelSave;
        var size = ImGui.GetIO().DisplaySize;
        var s = Ui.Scale;
        float cell = 30 * s, gap = 6 * s;
        var count = (int)BuildAid.COUNT - 1;
        var x0 = size.X * 0.5f - (count * cell + (count - 1) * gap) * 0.5f;
        var y0 = size.Y - 118 * s;
        var dl = ImGui.GetForegroundDrawList();
        for (var a = 1; a <= count; a++)
        {
            var aid = (BuildAid)a;
            var live = g.SandboxAidLive(aid);
            var used = aid switch
            {
                BuildAid.Mirror => v.Mirror != 0,
                BuildAid.Line => _buildMode == BuildMode.Line,
                BuildAid.Replace => _buildMode == BuildMode.Replace,
                BuildAid.Gantry => GantryOn,
                BuildAid.Select => g.SandboxHands.Hand(g.LocalId).Tool == ConstructTool.Selector,
                BuildAid.Blueprint => _select.Mode == SelectController.Placing.Stamp,
                _ => false,
            };
            var p0 = new float2(x0 + (a - 1) * (cell + gap), y0);
            var p1 = new float2(p0.X + cell, p0.Y + cell);
            var fill = used ? new float4(0.2f, 0.62f, 0.34f, 0.92f) : live ? new float4(0.1f, 0.14f, 0.16f, 0.75f) : new float4(0.08f, 0.08f, 0.09f, 0.45f);
            var edge = live ? new float4(0.45f, 0.95f, 0.6f, 0.9f) : v.State[a] == BeaconState.None ? new float4(0.5f, 0.5f, 0.52f, 0.4f) : new float4(0.95f, 0.45f, 0.35f, 0.85f);
            dl.AddRectFilled(p0, p1, ImGui.GetColorU32(fill), 4 * s);
            dl.AddRect(p0, p1, ImGui.GetColorU32(edge), 4 * s, ImDrawFlags.None, 1.5f * s);
            dl.AddText(new float2(p0.X + 10 * s, p0.Y + 6 * s), ImGui.GetColorU32(live ? new float4(1, 1, 1, 0.95f) : new float4(0.7f, 0.7f, 0.72f, 0.6f)), BuildAids.Letter(aid));
            if (_barKeys[a] is { Length: > 0 and <= 3 } k)
                dl.AddText(new float2(p0.X + 2 * s, p1.Y + 1 * s), ImGui.GetColorU32(new float4(0.8f, 0.82f, 0.85f, 0.7f)), k);
        }
        if (_barText.Length > 0)
        {
            var tw = ImGui.CalcTextSize(_barText).X;
            dl.AddText(new float2(size.X * 0.5f - tw * 0.5f, y0 - 22 * s), ImGui.GetColorU32(new float4(1, 1, 1, 0.92f)), _barText);
        }
        if (GantryOn)
        {
            var at = new float2(_gCursor.X, _gCursor.Y);
            var col = ImGui.GetColorU32(new float4(1, 1, 1, 0.95f));
            dl.AddLine(new float2(at.X - 9 * s, at.Y), new float2(at.X + 9 * s, at.Y), col, 1.5f * s);
            dl.AddLine(new float2(at.X, at.Y - 9 * s), new float2(at.X, at.Y + 9 * s), col, 1.5f * s);
        }
    }
}
