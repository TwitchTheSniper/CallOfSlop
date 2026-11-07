namespace CallOfSlop;

/// <summary>
/// The texture paint tool on this PC (ConstructFinish.cs; docs/sandbox/CONSTRUCTS.md, "The texture paint tool"). LMB puts the
/// roller's material on the part under the crosshair; holding it and sweeping paints each part passed over once per stroke (the
/// colour paint tool's <see cref="PaintStroke"/>: a part is never acted on twice in a stroke, and the stroke ends when the button
/// goes up); RMB picks up the material of what is aimed at (read off this PC's copy, nothing is sent); R, or "Textures..." on the
/// tool wheel's ring, opens the list (UiSandboxTexture.cs). The mode (Part, Whole construct, Reset) comes from the ring. In
/// survival, a whole construct asks for a second click, as with colour. Each stroke is a request to the server
/// (ConstructEdits.Finish), which checks the tool, the reach and the contested-construct rule.
/// </summary>
public static partial class Program
{
    private static readonly SandboxTextureWheel _textureWheel = new();
    private static readonly PaintStroke _txStroke = new();
    private static ushort _txConfirmBody;
    private static long _txConfirmAt;
    private static long _txLookKey = long.MinValue;
    private static string _txLook = "";

    /// <summary>Every frame with the texture paint tool out and the mouse captured (from SandboxInput, with the crosshair's hit).</summary>
    private static void SandboxTextureInput(Game g, Player me, bool aimed, in ConstructHit hit)
    {
        if (Pressed(GameAction.Reload) && !MenusOpen)
        {
            _textureWheel.WantList = true; // (opened in the ImGui pass: DrawSandboxTextureInMatch)
            return;
        }
        var inReach = aimed && hit.Distance <= ConstructBuild.Reach && hit.RoutePlus1 == 0 && hit.Part >= 0 && hit.Part < hit.Body.PartCount;
        var fire = Down(GameAction.Fire) && !_swallowClick;
        if (!fire)
            _txStroke.Clear();
        var mode = _textureWheel.Mode;
        var finish = _textureWheel.Current;
        if (inReach)
        {
            var c = hit.Body;
            var now = c.Parts[hit.Part].Finish;
            var key = ((long)c.Id << 24) ^ ((long)hit.Part << 4) ^ ((long)mode << 1) ^ ((long)now << 40) ^ ((long)finish << 48);
            if (key != _txLookKey)
            {
                _txLookKey = key;
                var has = now != 0 ? ConstructFinish.Name(now) : "";
                var does = mode switch { PaintMode.Whole => $"{ConstructFinish.Name(finish)} on the whole construct", PaintMode.Reset => "reset its texture", _ => $"paint {ConstructFinish.Name(finish)}" };
                _txLook = SandboxKeys.Line(c.DefOf(hit.Part).Name, has, null, $"{SandboxKeys.Fire}: {does}, {SandboxKeys.Aim}: pick its texture, {SandboxKeys.Reload}: textures");
            }
            g.SandboxLookText = _txLook;
        }
        else
            _txLookKey = long.MinValue;
        if (!inReach)
            return;
        var body = hit.Body;
        var index = hit.Part;
        // The eyedropper: the part's finish, else the material of its first box that would take one.
        if (Pressed(GameAction.Aim))
        {
            var picked = PickedFinish(body, index);
            if (picked == 0)
            {
                g.SandboxSay($"a {body.DefOf(index).Name} has no texture to pick up");
                return;
            }
            _textureWheel.SetFinish(picked);
            g.SandboxSay($"picked up {ConstructFinish.Name(picked)}");
            return;
        }
        if (!fire)
            return;
        if (!_txStroke.Try(body.Id, false, index, whole: mode == PaintMode.Whole))
            return; // (this part is done in this stroke)
        if (mode == PaintMode.Whole && !g.SandboxInfiniteParts)
        {
            var t = Environment.TickCount64;
            if (_txConfirmBody != body.Id || t - _txConfirmAt > 4000)
            {
                _txConfirmBody = body.Id;
                _txConfirmAt = t;
                g.SandboxSay("texture the whole construct? click again to confirm");
                return;
            }
            _txConfirmBody = 0;
        }
        if (mode != PaintMode.Reset)
            _textureWheel.Remember(finish);
        g.SandboxTexture(body.Id, mode, index, finish);
    }

    /// <summary>What the eyedropper takes off a part: its finish, else the material its first box that takes one is drawn with (0: none the tool offers).</summary>
    private static byte PickedFinish(Construct c, int part)
    {
        ref readonly var p = ref c.Parts[part];
        if (p.Finish != 0)
            return p.Finish;
        foreach (var b in p.D.Boxes)
            if (ConstructFinish.Takes(in b) && !b.Hidden && ConstructFinish.BoxRow(in b, p.Paint, 0) is var row && ConstructFinish.Offered(row))
                return row;
        return 0;
    }

    /// <summary>Every frame (from SandboxInput): the stroke is over when the texture paint tool isn't out.</summary>
    private static void TextureIdle()
    {
        _txStroke.Clear();
        _txConfirmBody = 0;
    }

    /// <summary>The texture list over the match (R, or "Textures..." on the ring); it closes (the mouse goes back to aiming) when it should.</summary>
    private static void DrawSandboxTextureInMatch()
    {
        if (_textureWheel.WantList)
        {
            _textureWheel.WantList = false;
            if (_game is { SandboxOn: true } && !MenusOpen)
            {
                Ui.OpenSandboxTexturePicker();
                SetMouseCaptured(false);
            }
        }
        if (!Ui.SandboxTexturePickerOpen)
            return;
        var me = _game?.Local;
        var gone = _session == null || _mouseCaptured || _game is not { SandboxOn: true } || me is not { Alive: true } || _game.Phase != MatchPhase.Playing;
        if (!gone)
        {
            ref var h = ref _game!.SandboxHands.Hand(me!.Id);
            gone = h.Seated || h.Tool != ConstructTool.Texture;
        }
        if (!gone)
        {
            var open = Ui.DrawSandboxTexturePicker(_textureWheel.Current, out var chosen);
            if (chosen != 0)
                _textureWheel.SetFinish(chosen);
            if (open)
                return;
        }
        Ui.CloseSandboxTexturePicker();
        if (!MenusOpen)
            SetMouseCaptured(true);
        _swallowClick = true; // (the click that closed it mustn't also paint)
    }
}
