using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The welder's freeform mode on this PC (board item dev-sandbox-freeform-weld-fw01; ConstructFreeform.cs). Picked on
/// the tool wheel's outer ring round the welder (SandboxWeldModes; socket attach stays the default). Shown as "Freeform
/// attach". Two clicks: the first picks a part (it stays outlined), the second, on a part of another body, joins the two
/// where they are, if their boxes come within Freeform.MaxGap. RMB, or a click on nothing, drops the first pick. While
/// aiming: both parts outlined and the bead drawn at their closest points, green or red with the reason on the look line.
/// In either mode, the tool's aim (RMB) on a freeform seam cuts it free. A part in the hands goes on a socket as in any
/// mode. The server decides every join and cut (ConstructEditsFreeform.cs).
/// </summary>
public static partial class Program
{
    private static readonly SandboxWeldModes _weldModes = new();
    private static ushort _fwFirstBody; // the two-click fallback's first pick (0: none)
    private static int _fwFirstPart = -1, _fwFirstDef = -1; // (its index, stamped with its kind when picked)
    private static long _fwKey = long.MinValue;
    private static string _fwText = "";

    /// <summary>The welder welds freeform on this PC (the outer ring's pick).</summary>
    private static bool SandboxFreeformOn(in SandboxHand h) => h.Tool == ConstructTool.Welder && _weldModes.Freeform && h.Held == ConstructParts.None;

    /// <summary>Every frame with the welder out (before the held part's ghosts, which freeform mode turns off).</summary>
    private static void SandboxFreeformInput(Game g, ref SandboxHand h, Vector3 eye, Vector3 dir, bool aimed, in ConstructHit hit)
    {
        var inReach = aimed && hit.Distance <= ConstructBuild.Reach && hit.SlotPlus1 == 0 && hit.RoutePlus1 == 0;
        // Either mode: the aim on a freeform seam cuts it free.
        if (inReach && hit.Body.FreeformCount > 0 && Freeform.SeamAt(hit.Body, hit.Part, hit.Point) is var seam and >= 0)
        {
            if (NewLook(1, hit.Body.Id, seam, 0, 0))
                _fwText = SandboxKeys.Line("Freeform attachment", null, null, SandboxKeys.Do(SandboxKeys.Aim, "cut free"));
            g.SandboxLookText = _fwText;
            if (Pressed(GameAction.Aim))
            {
                g.SandboxSend(CActKind.FreeformCut, body: hit.Body.Id, part: (ushort)hit.Part, socket: (byte)seam);
                _down[(int)GameAction.Aim] = false; // (not also taking the part off)
            }
        }
        if (!SandboxFreeformOn(h))
        {
            _fwFirstBody = 0;
            return;
        }
        // The first pick of the two-click fallback, as the construct is now (it may have changed under it).
        var first = _fwFirstBody != 0 ? g.Constructs.Get(_fwFirstBody) : null;
        if (first == null || _fwFirstPart < 0 || _fwFirstPart >= first.PartCount || first.Parts[_fwFirstPart].Def != _fwFirstDef)
        {
            first = null;
            _fwFirstBody = 0;
        }
        if (first != null && Pressed(GameAction.Aim))
        {
            first = null; // (RMB: the pick goes, nothing is taken off)
            _fwFirstBody = 0;
            _down[(int)GameAction.Aim] = false;
        }
        Construct? a = null, b = null;
        int pa = -1, pb = -1;
        if (inReach && first != null && first != hit.Body)
        {
            a = first;
            pa = _fwFirstPart;
            b = hit.Body;
            pb = hit.Part;
        }
        if (first != null)
        {
            ConstructRender.WeldBodyA = first;
            ConstructRender.WeldPartA = _fwFirstPart;
        }
        if (a != null && b != null)
        {
            var gap = Freeform.Gap(a, pa, b, pb, out var onA, out var onB);
            // (the rules and the line are worked out again only when what they depend on changes: nothing allocates per frame)
            var state = (int)MathF.Min(gap * 100, 100000) ^ a.PartCount << 17 ^ b.PartCount << 24 ^ (a.FreeformCount + b.FreeformCount * 9) << 12;
            if (NewLook(2, a.Id | (long)b.Id << 16, pa, pb, state))
            {
                _fwWhy = Freeform.Rules(g.Constructs, World.Query, a, pa, b, pb, ReadOnlySpan<Vector3>.Empty, out var axle)
                         ?? (ConstructEdits.SalvageRefusal(a, g.LocalId) != null || ConstructEdits.SalvageRefusal(b, g.LocalId) != null
                             ? "that construct is someone else's" : null);
                _fwText = _fwWhy != null ? _fwWhy
                    : axle ? SandboxKeys.Line($"{a.DefOf(pa).Name} on {b.DefOf(pb).Name}", "axle, rolls", null, SandboxKeys.Do(SandboxKeys.Fire, "attach"))
                    : Freeform.FixedWheel(a, pa, b, pb) ? SandboxKeys.Line($"{a.DefOf(pa).Name} to {b.DefOf(pb).Name}", null, "won't roll: attach its axle to a suspension's", SandboxKeys.Do(SandboxKeys.Fire, "attach"))
                    : SandboxKeys.Line($"{a.DefOf(pa).Name} to {b.DefOf(pb).Name}", $"gap {gap:0.00} m", null, SandboxKeys.Do(SandboxKeys.Fire, "attach"));
            }
            var why = _fwWhy;
            ConstructRender.WeldBodyA = a;
            ConstructRender.WeldPartA = pa;
            ConstructRender.WeldBodyB = b;
            ConstructRender.WeldPartB = pb;
            ConstructRender.WeldOnA = onA;
            ConstructRender.WeldOnB = onB;
            ConstructRender.WeldValid = why == null;
            g.SandboxLookText = _fwText;
            if (Pressed(GameAction.Fire) && !_swallowClick)
            {
                if (why != null)
                    g.SandboxSay(why); // (the first pick stays: aim at something nearer)
                else
                {
                    g.SandboxSend(CActKind.FreeformWeld, body: a.Id, part: (ushort)pa, body2: b.Id, socket: (ushort)pb);
                    _sbFiring = !g.SandboxInfiniteParts; // (survival: a timed weld; letting go stops it)
                    _fwFirstBody = 0;
                }
            }
            return;
        }
        if (inReach)
        {
            if (NewLook(first == hit.Body ? 4 : 5, hit.Body.Id, hit.Part, 0, 0))
                _fwText = first == hit.Body
                    ? $"{SandboxKeys.Fire} a part on another body"
                    : SandboxKeys.Line(hit.Body.DefOf(hit.Part).Name, null, null, SandboxKeys.Do(SandboxKeys.Fire, "pick, then the part to attach it to"));
            g.SandboxLookText = _fwText;
            if (Pressed(GameAction.Fire) && !_swallowClick && first == null)
            {
                _fwFirstBody = hit.Body.Id;
                _fwFirstPart = hit.Part;
                _fwFirstDef = hit.Body.Parts[hit.Part].Def;
            }
        }
        else if (Pressed(GameAction.Fire) && !_swallowClick)
            _fwFirstBody = 0; // (a click on nothing starts again)
    }

    private static string? _fwWhy;

    /// <summary>Has what the look line says changed (the look runs every frame; the line is remade only then)?</summary>
    private static bool NewLook(int kind, long bodies, int pa, int pb, int extra)
    {
        var key = ((((long)kind * 1_000_003 + bodies) * 131 + pa) * 131 + pb) * 1_000_003 + extra;
        if (key == _fwKey)
            return false;
        _fwKey = key;
        return true;
    }
}
