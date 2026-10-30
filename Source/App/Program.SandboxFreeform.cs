using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The welder's freeform mode on this PC (board item dev-sandbox-freeform-weld-fw01; ConstructFreeform.cs). Picked on
/// the tool wheel's outer ring round the welder (SandboxWeldModes; socket weld stays the default). LMB casts the
/// crosshair's ray through the first part it hits on to the next part behind it on a different body and welds the two
/// where they are; when the ray finds no second body (parts side by side) the first click picks a part (it stays
/// outlined) and a second click on a part of another body welds them. While aiming: both parts outlined and the weld bead
/// drawn at their closest points, green or red with the reason on the look line. In either mode, the welder's aim (RMB)
/// on a freeform seam cuts it free. The server decides every weld and cut (ConstructEditsFreeform.cs).
/// </summary>
public static partial class Program
{
    private static readonly SandboxWeldModes _weldModes = new();
    private static ushort _fwFirstBody; // the two-click fallback's first pick (0: none)
    private static int _fwFirstPart = -1;
    private static long _fwKey = long.MinValue;
    private static string _fwText = "";

    /// <summary>The welder welds freeform on this PC (the outer ring's pick).</summary>
    private static bool SandboxFreeformOn(in SandboxHand h) => h.Tool == ConstructTool.Welder && _weldModes.Freeform;

    /// <summary>Every frame with the welder out (before the held part's ghosts, which freeform mode turns off).</summary>
    private static void SandboxFreeformInput(Game g, ref SandboxHand h, Vector3 eye, Vector3 dir, bool aimed, in ConstructHit hit)
    {
        var inReach = aimed && hit.Distance <= ConstructBuild.Reach && hit.SlotPlus1 == 0 && hit.RoutePlus1 == 0;
        // Either mode: the aim on a freeform seam cuts it free.
        if (inReach && hit.Body.FreeformCount > 0 && Freeform.SeamAt(hit.Body, hit.Part, hit.Point) is var seam and >= 0)
        {
            if (NewLook(1, hit.Body.Id, seam, 0, 0))
                _fwText = "Freeform weld seam: aim (RMB) to cut it free";
            g.SandboxLookText = _fwText;
            if (Pressed(GameAction.Aim))
            {
                g.SandboxSend(CActKind.FreeformCut, body: hit.Body.Id, part: (byte)hit.Part, socket: (byte)seam);
                _down[(int)GameAction.Aim] = false; // (not also taking the part off)
            }
        }
        if (!_weldModes.Freeform)
        {
            _fwFirstBody = 0;
            return;
        }
        // The first pick of the two-click fallback, as the construct is now (it may have changed under it).
        var first = _fwFirstBody != 0 ? g.Constructs.Get(_fwFirstBody) : null;
        if (first == null || _fwFirstPart < 0 || _fwFirstPart >= first.PartCount)
        {
            first = null;
            _fwFirstBody = 0;
        }
        Construct? a = null, b = null;
        int pa = -1, pb = -1;
        if (inReach)
        {
            if (first != null && first != hit.Body)
            {
                a = first;
                pa = _fwFirstPart;
                b = hit.Body;
                pb = hit.Part;
            }
            else if (first == null && Freeform.Behind(g.Constructs, hit, eye, dir, ConstructBuild.Reach + 1.5f, out var behind))
            {
                // Through the first part to the next part behind it on another body.
                a = hit.Body;
                pa = hit.Part;
                b = behind.Body;
                pb = behind.Part;
            }
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
                _fwWhy = Freeform.Check(g.Constructs, a, pa, b, pb) ?? (ConstructEdits.SalvageRefusal(a, g.LocalId) != null || ConstructEdits.SalvageRefusal(b, g.LocalId) != null
                    ? "that construct is someone else's: you can only weld your own (or wrecks and loose parts)" : null);
                _fwText = _fwWhy != null ? $"Freeform weld: {_fwWhy}" : $"Freeform weld: LMB welds the {a.DefOf(pa).Name} to the {b.DefOf(pb).Name} where they are (gap {gap:0.00} m)";
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
                    g.SandboxSay(why);
                else
                {
                    g.SandboxSend(CActKind.FreeformWeld, body: a.Id, part: (byte)pa, body2: b.Id, socket: (byte)pb);
                    _sbFiring = !g.SandboxInfiniteParts; // (survival: a timed weld; letting go stops it)
                }
                _fwFirstBody = 0;
            }
            return;
        }
        if (inReach)
        {
            if (NewLook(first == hit.Body ? 4 : 5, hit.Body.Id, hit.Part, 0, 0))
                _fwText = first == hit.Body
                    ? "Freeform weld: now click a part on another body (LMB on empty space: start again)"
                    : $"Freeform weld: nothing behind the {hit.Body.DefOf(hit.Part).Name} - LMB picks it, then click the part to weld it to";
            g.SandboxLookText = _fwText;
            if (Pressed(GameAction.Fire) && !_swallowClick && first == null)
            {
                _fwFirstBody = hit.Body.Id;
                _fwFirstPart = hit.Part;
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
