using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The attach tool's No-collide mode on this PC (docs/sandbox/CONSTRUCTS.md, "No-collide pairs"). Two clicks, as Freeform attach
/// picks: the first takes a piece (it stays outlined), the second another, and that pair stops colliding, or collides again if it
/// had stopped. A pick is the whole rigid piece under the crosshair; with Shift (the sprint key) held, that one part. RMB, or a
/// click on nothing, drops the first pick. The pairs are drawn while the mode is out. The server decides.
/// </summary>
public static partial class Program
{
    private static ushort _ncFirstBody; // the first pick (0: none)
    private static int _ncFirstPart = -1, _ncFirstDef = -1; // (its index, stamped with its kind when picked)
    private static bool _ncFirstWhole;
    private static string _ncText = "";
    private static string? _ncWhy;

    private static bool NoCollideModeOn(in SandboxHand h) => h.Tool == ConstructTool.Welder && _weldModes.Mode == SandboxWeldModes.Option.NoCollide && h.Held == ConstructParts.None;

    /// <summary>A frame of the No-collide mode (from SandboxInput, behind its guard).</summary>
    private static void SandboxNoCollideFrame(Game g, ConstructHit hit, bool aimed)
    {
        var w = g.Constructs;
        ConstructRender.AimPart = -1;
        ConnectorRender.NoCollideShow = true;
        ConnectorRender.AimNoCollide = -1;
        if (aimed && !LinkHitUsable(ref hit))
            aimed = false; // (a hit on nothing the tool can act on: a part this copy doesn't have)
        var inReach = aimed && hit.Distance <= ConstructBuild.Reach && hit.SlotPlus1 == 0 && hit.RoutePlus1 == 0;
        var whole = !Down(GameAction.Sprint) && !SweepPointHeld;
        var fire = Pressed(GameAction.Fire) && !_swallowClick;
        // The first pick, as the construct is now (it may have changed under it).
        var first = _ncFirstBody != 0 ? w.Get(_ncFirstBody) : null;
        if (first == null || _ncFirstPart < 0 || _ncFirstPart >= first.PartCount || first.Parts[_ncFirstPart].Def != _ncFirstDef)
        {
            first = null;
            _ncFirstBody = 0;
        }
        if (first != null && Pressed(GameAction.Aim))
        {
            first = null;
            _ncFirstBody = 0;
            _down[(int)GameAction.Aim] = false;
        }
        if (first != null)
        {
            ConstructRender.WeldBodyA = first;
            ConstructRender.WeldPartA = _ncFirstPart;
        }
        if (inReach && first != null)
        {
            var (a, pa, b, pb) = (first, _ncFirstPart, hit.Body, hit.Part);
            var pair = w.NoCollideOf(a, pa, b, pb);
            ConnectorRender.AimNoCollide = pair;
            if (NewLook(6, a.Id | (long)b.Id << 16, pa, pb, w.NoCollideVersion * 2 + (whole ? 1 : 0)))
            {
                _ncWhy = pair >= 0 ? null : w.CheckNoCollide(a, pa, b, pb)
                                            ?? (ConstructEdits.SalvageRefusal(a, g.LocalId) != null || ConstructEdits.SalvageRefusal(b, g.LocalId) != null ? "that construct is someone else's" : null);
                var name = $"{a.DefOf(pa).Name} and {b.DefOf(pb).Name}";
                _ncText = pair >= 0 ? SandboxKeys.Line(name, "no-collide", null, SandboxKeys.Do(SandboxKeys.Fire, "let them collide"))
                    : _ncWhy != null ? SandboxKeys.Line(name, "collide", _ncWhy)
                    : a == b ? SandboxKeys.Line(name, "one construct, they never meet", null, SandboxKeys.Do(SandboxKeys.Fire, "keep them apart if they split"))
                    : SandboxKeys.Line(name, "collide", null, SandboxKeys.Do(SandboxKeys.Fire, _ncFirstWhole && whole ? "no-collide these pieces" : "no-collide these"));
            }
            ConstructRender.WeldBodyA = a;
            ConstructRender.WeldPartA = pa;
            ConstructRender.WeldBodyB = b;
            ConstructRender.WeldPartB = pb;
            ConstructRender.WeldOnA = ConstructRender.PartCentre(a, pa);
            ConstructRender.WeldOnB = ConstructRender.PartCentre(b, pb);
            ConstructRender.WeldValid = _ncWhy == null;
            g.SandboxLookText = _ncText;
            if (fire)
            {
                if (_ncWhy != null)
                    g.SandboxSay(_ncWhy); // (the first pick stays: aim at another)
                else
                {
                    g.SandboxSendNoCollide(a.Id, (ushort)pa, _ncFirstWhole, b.Id, (ushort)pb, whole);
                    _ncFirstBody = 0;
                }
            }
            return;
        }
        if (inReach)
        {
            var on = w.NoCollidesOn(hit.Body, hit.Part);
            if (NewLook(7, hit.Body.Id, hit.Part, 0, on * 2 + (whole ? 1 : 0)))
                _ncText = SandboxKeys.Line(hit.Body.DefOf(hit.Part).Name, on > 0 ? $"no-collide with {on}" : null, null,
                    SandboxKeys.Do(SandboxKeys.Fire, whole ? $"pick this piece ({SandboxKeys.Sprint}: this part only)" : "pick this part"));
            g.SandboxLookText = _ncText;
            if (fire)
            {
                _ncFirstBody = hit.Body.Id;
                _ncFirstPart = hit.Part;
                _ncFirstDef = hit.Body.Parts[hit.Part].Def;
                _ncFirstWhole = whole;
            }
        }
        else if (fire)
            _ncFirstBody = 0; // (a click on nothing starts again)
    }
}
