using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The attach tool's Rope and Spring modes on this PC (docs/sandbox/CONSTRUCTS.md, "Ropes and springs"; picked on its ring,
/// SandboxWeldModes): LMB picks the first point on any construct, wreck, loose body or the world; LMB picks the second and the
/// server ties it. The ghost runs from the first point to the crosshair, green or red with the reason. RMB or Esc cancels; RMB
/// with no first point takes off the line under the crosshair; R steps the size (by the parts, S, M, L, XL). With a part in the
/// hands the tool fits that part as usual. The first point holds indices into a construct, so it is stamped when taken and
/// checked each frame. The configure tool's panel opens on a line (UiSandboxConnector.cs); the remover takes one with LMB.
/// </summary>
public static partial class Program
{
    private static bool _cnHave;
    private static ConnectorEnd _cnA;
    private static long _cnStamp;
    private static byte _cnSize = 255; // 255: by the parts
    private static int _cyPick; // the Cylinder mode's choice: electric S to XL, then hydraulic S to XL
    private static bool CyElectric => _cyPick < ConstructWorld.ConnectorSizes.Length;
    private static byte CySize => (byte)(_cyPick % ConstructWorld.ConnectorSizes.Length);
    private static long _cnLineKey = long.MinValue;
    private static string _cnLine = "";
    private static long _cnLookKey = long.MinValue;
    private static string _cnLook = "";

    /// <summary>The attach tool is out in its Rope or Spring mode, with nothing in the hands.</summary>
    private static bool ConnectorModeOn(in SandboxHand h) => h.Tool == ConstructTool.Welder && _weldModes.Connector != null && h.Held == ConstructParts.None && !h.Seated;

    /// <summary>Every frame before the tools: the ghost and the aimed line are set again by whoever draws them this frame, and a point held goes when the mode does.</summary>
    private static void ConnectorToolOut(in SandboxHand h)
    {
        ConnectorRender.PreviewOn = false;
        ConnectorRender.AimConnector = -1;
        if (_cnHave && !ConnectorModeOn(in h))
            ConnectorReset();
    }

    /// <summary>The point being held goes (the tool put away, the mode changed, Esc).</summary>
    private static void ConnectorReset()
    {
        _cnHave = false;
        ConnectorRender.PreviewOn = false;
    }

    /// <summary>Esc while a rope is being tied: it stops (true: the key was used).</summary>
    private static bool ConnectorEsc()
    {
        if (!_cnHave)
            return false;
        ConnectorReset();
        return true;
    }

    private static ConnectorEnd EndOnPart(Construct c, int part, Vector3 world) =>
        new() { Body = c.Id, Part = (ushort)part, Local = ConstructWorld.PartLocal(c, part, world) };

    /// <summary>What is under the crosshair: a part, or the ground or a wall. False: nothing in reach.</summary>
    private static bool ConnectorAim(Vector3 eye, Vector3 dir, bool aimed, in ConstructHit hit, out ConnectorEnd end, out Vector3 at)
    {
        end = default;
        at = default;
        var onWorld = WorldAim(eye, dir, aimed, hit, out var wh);
        if (onWorld)
        {
            at = eye + dir * wh.Distance + wh.Normal * ConstructWorld.ConnectorEndLift;
            end = new ConnectorEnd { Local = at };
            return true;
        }
        if (!aimed || hit.Distance > ConstructBuild.Reach + 1f || hit.Part < 0 || hit.Part >= hit.Body.PartCount)
            return false;
        at = hit.Point;
        end = EndOnPart(hit.Body, hit.Part, at);
        return true;
    }

    private static string TonneText(float newtons) => $"{newtons / (1000f * ConstructPhysics.GravityAccel):0.#} t";

    private static string ConnectorState(in CConnector cn, float span) => cn.Kind == ConnectorKind.Cylinder
        ? $"{span:0.00} m, {cn.Stiffness:0.00} to {cn.Damping:0.00} m"
        : cn.Kind == ConnectorKind.Rope
        ? $"{span:0.0} of {cn.Length:0.0} m, {(span >= cn.Length - 0.03f ? "taut" : "slack")}, holds {TonneText(cn.Rating)}"
        : cn.Stiffness <= 0 ? $"damper, {cn.Damping / 1000f:0.0} kN s/m"
        : $"{span:0.0} m, rests at {cn.Length:0.0} m, {cn.Stiffness / 1000f:0} kN/m";

    /// <summary>What stops a cylinder, from the links (the same on every peer), or null.</summary>
    private static string? CylinderProblem(Game g, in CConnector cn)
    {
        if (cn.Kind != ConnectorKind.Cylinder || g.Constructs.Get(cn.A.Body) is not { } c || cn.A.Part >= c.PartCount)
            return null;
        if (c.Parts[cn.A.Part].D.Hyd == HydKind.Electric)
            return ConstructPower.On(c, cn.A.Part) ? null : "no power";
        var k = c.HydHeadOf(cn.A.Part);
        return k < 0 ? null : ConstructHydraulics.Trace(c, c.HydHeads![k], out _, out _) is var s and not HydState.Ok ? ConstructHydraulics.StateText(s) : null;
    }

    /// <summary>The look line for a connector under the crosshair ("Rope: 4.2 of 4.4 m, slack, holds 6 t - RMB: take it off"), cached by what it says.</summary>
    private static string ConnectorLook(Game g, in CConnector cn, string key, string action)
    {
        g.Constructs.EndPoint(cn.A, out var a);
        g.Constructs.EndPoint(cn.B, out var b);
        var span = Vector3.Distance(a, b);
        var k = HashCode.Combine(cn.Id, (int)(span * 10), (int)(cn.Length * 10), cn.Rating, cn.Stiffness, cn.Damping, action) ^ ((long)SandboxKeys.Stamp << 32) ^ (cn.Kind == ConnectorKind.Cylinder ? (long)(CylinderProblem(g, cn)?.Length ?? 0) << 48 : 0);
        if (k != _cnLookKey)
        {
            _cnLookKey = k;
            _cnLook = SandboxKeys.Line(cn.Kind switch { ConnectorKind.Rope => "Rope", ConnectorKind.Spring => "Spring", _ => "Cylinder" }, ConnectorState(cn, span), CylinderProblem(g, cn), SandboxKeys.Do(key, action));
        }
        return _cnLook;
    }

    /// <summary>A frame of the Rope and Spring modes (from SandboxInput, behind its guard).</summary>
    private static void SandboxConnectorFrame(Game g, Vector3 eye, Vector3 dir, bool aimed, ConstructHit hit, ConnectorKind kind)
    {
        var w = g.Constructs;
        ConstructRender.AimPart = -1;
        if (aimed && !LinkHitUsable(ref hit))
            aimed = false; // (a hit on nothing the tool can act on: a part this copy doesn't have)
        var fire = Pressed(GameAction.Fire) && !_swallowClick;
        var rmb = Pressed(GameAction.Aim);
        if (Pressed(GameAction.Reload) && kind == ConnectorKind.Cylinder)
            _cyPick = (_cyPick + 1) % (2 * ConstructWorld.ConnectorSizes.Length);
        else if (Pressed(GameAction.Reload))
            _cnSize = _cnSize == 255 ? (byte)0 : _cnSize >= ConstructWorld.ConnectorSizes.Length - 1 ? (byte)255 : (byte)(_cnSize + 1);
        // The first point, against the construct as it is now.
        if (_cnHave)
        {
            string? gone = null;
            if (_cnA.Body != 0)
            {
                if (w.Get(_cnA.Body) is not { } ac)
                    gone = "the construct is gone";
                else if (LinkStamp(ac, _cnA.Part, 255, 0) != _cnStamp)
                    gone = "the construct was changed";
            }
            if (gone != null)
            {
                ConnectorReset();
                g.SandboxSay($"{ConstructWorld.ConnectorName(kind)} dropped: {gone}");
            }
        }
        var aimHas = ConnectorAim(eye, dir, aimed, hit, out var end, out var at);
        if (!_cnHave)
        {
            // Nothing held: a line under the crosshair comes first (RMB takes it off), else LMB picks the first point.
            var best = aimHas ? Vector3.Distance(eye, at) + 0.02f : ConstructBuild.Reach + 1f;
            if (w.RayConnectors(eye, dir, ref best, out var k))
            {
                ConnectorRender.AimConnector = k;
                g.SandboxLookText = ConnectorLook(g, w.Connectors[k], SandboxKeys.Aim, "take it off");
                if (rmb)
                    g.SandboxSendConnectorDelete(w.Connectors[k].Id);
            }
            if (fire && aimHas)
            {
                _cnA = end;
                _cnHave = true;
                _cnStamp = end.Body != 0 && w.Get(end.Body) is { } c ? LinkStamp(c, end.Part, 255, 0) : 0;
                BuildSounds.Play(BuildSound.LinkNode, default);
            }
            ConnectorHud(g, kind, aimHas ? "" : "aim at a construct or the ground", 0, -1, false);
            return;
        }
        if (rmb)
        {
            ConnectorReset();
            ConnectorHud(g, kind, "", 0, -1, false);
            return;
        }
        w.EndPoint(_cnA, out var pa);
        var why = aimHas ? w.CheckConnector(_cnA, end) : "aim at a construct or the ground";
        var size = kind == ConnectorKind.Cylinder ? CySize : _cnSize != 255 ? _cnSize : aimHas ? w.AutoConnectorSize(_cnA, end) : (byte)0;
        ConnectorRender.PreviewOn = true;
        ConnectorRender.PreviewOk = why == null;
        ConnectorRender.PreviewA = pa;
        ConnectorRender.PreviewB = aimHas ? at : eye + dir * (ConstructBuild.Reach + 1f);
        ConnectorRender.PreviewKind = kind;
        ConnectorRender.PreviewSize = size;
        var span = Vector3.Distance(pa, ConnectorRender.PreviewB);
        if (fire)
        {
            if (why != null)
            {
                g.SandboxSay(why);
                BuildSounds.Play(BuildSound.Refuse, default);
            }
            else
            {
                g.SandboxSendConnector(kind, kind == ConnectorKind.Cylinder ? (byte)(CySize | (CyElectric ? ConstructWorld.CylinderElectric : 0)) : _cnSize, _cnA, end);
                BuildSounds.Play(BuildSound.LinkNode, default);
                ConnectorReset();
            }
        }
        ConnectorHud(g, kind, why ?? "", span, size, _cnHave);
    }

    /// <summary>The tool's HUD line: "Rope: 4.2 m, size M, 6 t - problem - LMB: tie it, RMB: cancel".</summary>
    private static void ConnectorHud(Game g, ConnectorKind kind, string problem, float span, int size, bool held)
    {
        var key = ((long)(span * 10) << 8) ^ ((long)(int)kind << 40) ^ ((long)(size + 1) << 44) ^ (held ? 1L << 48 : 0) ^ ((long)_cnSize << 50) ^ ((long)problem.GetHashCode() << 12) ^ ((long)SandboxKeys.Stamp << 58) ^ ((long)_cyPick << 36);
        if (key != _cnLineKey)
        {
            _cnLineKey = key;
            var ram = kind == ConnectorKind.Cylinder;
            var name = kind switch { ConnectorKind.Rope => "Rope", ConnectorKind.Spring => "Spring", _ => CyElectric ? "Electric cylinder" : "Hydraulic cylinder" };
            var sizeName = ram ? $"size {ConstructWorld.ConnectorSizes[CySize].Name}" : _cnSize == 255 ? "size by the parts" : $"size {ConstructWorld.ConnectorSizes[_cnSize].Name}";
            if (held && size >= 0)
            {
                var s = ConstructWorld.ConnectorSizes[size];
                var cost = g.SandboxHands.InfiniteParts ? "" : $", {ConstructWorld.ConnectorCost(kind, (byte)size, span)} {ConstructEdits.ConnectorMaterial(kind)}";
                _cnLine = SandboxKeys.Line(name, $"{span:0.0} m, {s.Name}, {(ram ? (CyElectric ? "needs power and a Set signal" : "needs a hose from a valve") : kind == ConnectorKind.Rope ? $"holds {s.Tonnes:0.#} t" : $"{s.Stiffness / 1000f:0} kN/m")}{cost}", problem,
                    $"{SandboxKeys.Fire}: {(kind == ConnectorKind.Rope ? "tie it" : "fit it")}, {SandboxKeys.Aim}: cancel, {SandboxKeys.Reload}: size");
            }
            else
                _cnLine = SandboxKeys.Line(name, $"pick the first point, {sizeName}", problem, $"{SandboxKeys.Fire}: first point, {SandboxKeys.Reload}: size");
        }
        g.SandboxWeldHint = _cnLine;
    }

    /// <summary>The configure tool on a rope or spring: the look line, and LMB opens its panel. True: the crosshair was on one.</summary>
    private static bool ConnectorConfigInput(Game g, Vector3 eye, Vector3 dir, bool aimed, in ConstructHit hit)
    {
        var w = g.Constructs;
        ConnectorRender.AimConnector = -1;
        var best = aimed ? hit.Distance : ConstructBuild.Reach + 1f;
        if (!w.RayConnectors(eye, dir, ref best, out var k))
            return false;
        ConnectorRender.AimConnector = k;
        g.SandboxLookText = ConnectorLook(g, w.Connectors[k], SandboxKeys.Fire, "settings");
        if (Pressed(GameAction.Fire) && !_swallowClick)
        {
            Ui.OpenConnectorPanel(w.Connectors[k]);
            SetMouseCaptured(false);
        }
        return true;
    }

    /// <summary>The remover on a rope or spring: LMB takes it at once. True: the crosshair was on one.</summary>
    private static bool ConnectorRemoverInput(Game g, Vector3 eye, Vector3 dir, bool aimed, in ConstructHit hit)
    {
        var w = g.Constructs;
        ConnectorRender.AimConnector = -1;
        var best = aimed ? hit.Distance : ConstructBuild.Reach + 1f;
        if (!w.RayConnectors(eye, dir, ref best, out var k))
            return false;
        ConnectorRender.AimConnector = k;
        g.SandboxLookText = ConnectorLook(g, w.Connectors[k], SandboxKeys.Fire, "delete it");
        if (Pressed(GameAction.Fire) && !_swallowClick)
            g.SandboxSendConnectorDelete(w.Connectors[k].Id);
        return true;
    }

    /// <summary>The panel over the match; it closes (and the mouse goes back to aiming) when it should.</summary>
    private static void DrawConnectorPanelInMatch()
    {
        if (!Ui.ConnectorPanelOpen)
            return;
        var me = _game.Local;
        var gone = _session == null || _mouseCaptured || !_game.SandboxOn || me is not { Alive: true } || _game.Phase != MatchPhase.Playing
                   || _game.SandboxHands.Hand(me!.Id).Seated || _game.SandboxHands.Hand(me.Id).Tool != ConstructTool.Configure;
        if (!gone && Ui.DrawConnectorPanel(_game, me!))
            return;
        Ui.CloseConnectorPanel();
        if (!MenusOpen)
            SetMouseCaptured(true);
        _swallowClick = true;
    }
}
