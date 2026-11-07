namespace CallOfSlop;

/// <summary>
/// The material painter on this PC (PartMaterials.cs; docs/sandbox/CONSTRUCTS.md, "The material painter"). LMB remakes the part under
/// the crosshair in the material in hand; holding it and sweeping remakes each part passed over once per stroke (the paint tools'
/// <see cref="PaintStroke"/>); RMB picks up what the aimed part is made of (read off this PC's copy, nothing is sent); R, or
/// "Materials..." on the tool wheel's ring, opens the list (UiSandboxMaterial.cs). The mode (Part, Whole construct, Own material)
/// comes from the ring; in survival a whole construct asks for a second click. Each stroke is a request to the server
/// (ConstructEdits.Rematerial), which checks, charges and swaps.
/// </summary>
public static partial class Program
{
    private static readonly SandboxMaterialWheel _materialWheel = new();
    private static readonly PaintStroke _mtStroke = new();
    private static ushort _mtConfirmBody;
    private static long _mtConfirmAt;
    private static long _mtLookKey = long.MinValue;
    private static string _mtLook = "";
    private static ConstructPartDef? _mtLastAimed;

    /// <summary>Every frame with the material painter out and the mouse captured (from SandboxInput, with the crosshair's hit).</summary>
    private static void SandboxMaterialInput(Game g, Player me, bool aimed, in ConstructHit hit)
    {
        if (Pressed(GameAction.Reload) && !MenusOpen)
        {
            _materialWheel.WantList = true; // (opened in the ImGui pass: DrawSandboxMaterialInMatch)
            return;
        }
        var inReach = aimed && hit.Distance <= ConstructBuild.Reach && hit.RoutePlus1 == 0 && hit.Part >= 0 && hit.Part < hit.Body.PartCount;
        var fire = Down(GameAction.Fire) && !_swallowClick;
        if (!fire)
            _mtStroke.Clear();
        var mode = _materialWheel.Mode;
        var material = _materialWheel.Current;
        if (inReach)
        {
            var c = hit.Body;
            var d = c.DefOf(hit.Part);
            _mtLastAimed = d;
            var key = ((long)c.Id << 24) ^ ((long)hit.Part << 4) ^ ((long)mode << 1) ^ ((long)d.Id << 40) ^ ((long)(material + 1) << 56);
            if (key != _mtLookKey)
            {
                _mtLookKey = key;
                var name = SandboxMaterialWheel.NameOf(material);
                var has = PartMaterials.Paintable(d) ? $"made of {PartMaterials.MaterialOf(d)}" : "";
                var does = mode switch { PaintMode.Whole => $"the whole construct in {name}", PaintMode.Reset => "back to its own material", _ => $"remake in {name}" };
                _mtLook = SandboxKeys.Line(PartMaterials.BaseOf(d).Name, has, PartMaterials.Paintable(d) || mode == PaintMode.Whole ? null : PartMaterials.Refusal(d),
                    $"{SandboxKeys.Fire}: {does}, {SandboxKeys.Aim}: pick its material, {SandboxKeys.Reload}: materials");
            }
            g.SandboxLookText = _mtLook;
        }
        else
            _mtLookKey = long.MinValue;
        if (!inReach)
            return;
        var body = hit.Body;
        var index = hit.Part;
        // The eyedropper: what the part is made of.
        if (Pressed(GameAction.Aim))
        {
            var dd = body.DefOf(index);
            var k = PartMaterials.Paintable(dd) ? PartMaterials.IndexOf(PartMaterials.MaterialOf(dd)) : -1;
            if (k < 0)
            {
                g.SandboxSay($"a {dd.Name} isn't made of anything the material painter works");
                return;
            }
            _materialWheel.SetMaterial(k);
            g.SandboxSay($"picked up {SandboxMaterialWheel.NameOf(k)}");
            return;
        }
        if (!fire || material < 0 && mode != PaintMode.Reset)
            return;
        if (!_mtStroke.Try(body.Id, false, index, whole: mode == PaintMode.Whole))
            return; // (this part is done in this stroke)
        if (mode != PaintMode.Whole && !PartMaterials.Paintable(body.DefOf(index)))
        {
            if (PartMaterials.Refusal(body.DefOf(index)) is { } why)
                g.SandboxSay(why);
            return;
        }
        if (mode == PaintMode.Whole && !g.SandboxInfiniteParts)
        {
            var t = Environment.TickCount64;
            if (_mtConfirmBody != body.Id || t - _mtConfirmAt > 4000)
            {
                _mtConfirmBody = body.Id;
                _mtConfirmAt = t;
                g.SandboxSay($"remake the whole construct in {SandboxMaterialWheel.NameOf(material)}? click again to confirm");
                return;
            }
            _mtConfirmBody = 0;
        }
        if (mode != PaintMode.Reset)
            _materialWheel.Remember(material);
        g.SandboxMaterial(body.Id, mode, index, material);
    }

    /// <summary>Every frame (from SandboxInput): the stroke is over when the material painter isn't out.</summary>
    private static void MaterialIdle()
    {
        _mtStroke.Clear();
        _mtConfirmBody = 0;
    }

    /// <summary>The material list over the match (R, or "Materials..." on the ring); it closes (the mouse goes back to aiming) when it should.</summary>
    private static void DrawSandboxMaterialInMatch()
    {
        if (_materialWheel.WantList)
        {
            _materialWheel.WantList = false;
            if (_game is { SandboxOn: true } && !MenusOpen)
            {
                Ui.OpenSandboxMaterialPicker(_mtLastAimed);
                SetMouseCaptured(false);
            }
        }
        if (!Ui.SandboxMaterialPickerOpen)
            return;
        var me = _game?.Local;
        var gone = _session == null || _mouseCaptured || _game is not { SandboxOn: true } || me is not { Alive: true } || _game.Phase != MatchPhase.Playing;
        if (!gone)
        {
            ref var h = ref _game!.SandboxHands.Hand(me!.Id);
            gone = h.Seated || h.Tool != ConstructTool.Material;
        }
        if (!gone)
        {
            var open = Ui.DrawSandboxMaterialPicker(_materialWheel.Current, out var chosen);
            if (chosen >= 0)
                _materialWheel.SetMaterial(chosen);
            if (open)
                return;
        }
        Ui.CloseSandboxMaterialPicker();
        if (!MenusOpen)
            SetMouseCaptured(true);
        _swallowClick = true; // (the click that closed it mustn't also paint)
    }
}
