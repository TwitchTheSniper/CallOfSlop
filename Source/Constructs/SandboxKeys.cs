namespace CallOfSlop;

/// <summary>
/// The player's binds as the Sandbox's text names them, and the one format of a look line: "Name: state - problem - F: action".
/// <see cref="Refresh"/> runs once a frame and only makes strings when a bind changed, so text built from these stays cached by
/// key: a caller that returns true from Refresh forgets its cached lines (Program.SandboxLookReset), and a cached HUD line
/// folds <see cref="Stamp"/> into its key. The defaults stand until the first Refresh (headless runs).
/// </summary>
public static class SandboxKeys
{
    public static string Use = "F", Fire = "LMB", Aim = "RMB", Reload = "R", Drop = "Delete", Menu = "Tab", Wheel = "MMB";
    public static string Sprint = "Left Shift", FreeRoute = "Left Shift", JumpKey = "Space";

    /// <summary>The selector's keys.</summary>
    public static string SelMove = "M", SelCopy = "O", SelPaint = "P", SelWeld = "U", SelSave = ",", SelTool = ".", Switch = "K";
    /// <summary>The build stations' keys (the build bar).</summary>
    public static string BuildMirror = "M", BuildLine = "O", BuildReplace = "P", BuildGantry = "U", GantryKeys = "";

    /// <summary>The key lines a seat shows.</summary>
    public static string Drive = "", DriveStart = "", DriveShift = "", Fly = "", Gunner = "", GetOut = "";

    /// <summary>The physics gun's keys.</summary>
    public static string PhysGun = "";

    /// <summary>The welders', the configure tool's and the cargo box's lines (set every frame: made here, not there).</summary>
    public static string SocketWeld1 = "", SocketWeld2 = "", SocketWeld3 = "", PointWeld1 = "", PointWeld2 = "", PointWeld3 = "", ConfigHint = "", ConfigHintSelected = "", CargoDraw = "", CargoAim = "";

    /// <summary>Bumped when a bind changes (for a cached line's key).</summary>
    public static int Stamp;

    private static readonly int[] Seen = new int[(int)GameAction.COUNT];
    private static bool _any;

    static SandboxKeys() => Compose(Keybinds.Defaults, named: false);

    /// <summary>A short name for a bind: the mouse buttons as LMB, RMB, MMB.</summary>
    private static string Short(int code) =>
        code == Keybinds.Mouse(Brutal.GlfwApi.GlfwMouseButton.Left) ? "LMB"
        : code == Keybinds.Mouse(Brutal.GlfwApi.GlfwMouseButton.Right) ? "RMB"
        : code == Keybinds.Mouse(Brutal.GlfwApi.GlfwMouseButton.Middle) ? "MMB"
        : Keybinds.Name(code);

    /// <summary>Reads the binds. True when one changed since the last call (the caller's cached text is stale).</summary>
    public static bool Refresh(int[] binds)
    {
        if (_any && binds.AsSpan().SequenceEqual(Seen))
            return false;
        _any = true;
        binds.AsSpan().CopyTo(Seen);
        Compose(binds, named: true);
        Stamp++;
        return true;
    }

    private static void Compose(int[] b, bool named)
    {
        string K(GameAction a) => named ? Short(b[(int)a]) : a switch
        {
            GameAction.MoveForward => "W", GameAction.MoveBack => "S", GameAction.MoveLeft => "A", GameAction.MoveRight => "D", GameAction.Jump => "Space",
            GameAction.CrouchHold => "Left Ctrl", GameAction.Sprint or GameAction.FreeRouting => "Left Shift", GameAction.Interact => "F", GameAction.Fire => "LMB",
            GameAction.Aim => "RMB", GameAction.Reload => "R", GameAction.SandboxDrop => "Delete", GameAction.SandboxInventory => "Tab", GameAction.ToolWheel => "MMB", GameAction.Weapon3 => "3",
            GameAction.BuildMirror => "M", GameAction.BuildLine => "O", GameAction.BuildReplace => "P", GameAction.BuildGantry => "U", GameAction.BuildPlaneUp => "Page Up", GameAction.BuildPlaneDown => "Page Down", GameAction.GantryOrbit => "Left Alt",
            GameAction.SelectMove => "M", GameAction.SelectCopy => "O", GameAction.SelectPaint => "P", GameAction.SelectWeld => "U", GameAction.SelectSave => ",", GameAction.SelectTool => ".", GameAction.SnapSwitch => "K", _ => "?",
        };
        Use = K(GameAction.Interact);
        Fire = K(GameAction.Fire);
        Aim = K(GameAction.Aim);
        Reload = K(GameAction.Reload);
        Drop = K(GameAction.SandboxDrop);
        Menu = K(GameAction.SandboxInventory);
        Wheel = K(GameAction.ToolWheel);
        Sprint = K(GameAction.Sprint);
        FreeRoute = K(GameAction.FreeRouting);
        JumpKey = K(GameAction.Jump);
        SelMove = K(GameAction.SelectMove);
        SelCopy = K(GameAction.SelectCopy);
        SelPaint = K(GameAction.SelectPaint);
        SelWeld = K(GameAction.SelectWeld);
        SelSave = K(GameAction.SelectSave);
        SelTool = K(GameAction.SelectTool);
        Switch = K(GameAction.SnapSwitch);
        GetOut = $"{Use}: get out";
        BuildMirror = K(GameAction.BuildMirror);
        BuildLine = K(GameAction.BuildLine);
        BuildReplace = K(GameAction.BuildReplace);
        BuildGantry = K(GameAction.BuildGantry);
        GantryKeys = $"{K(GameAction.GantryOrbit)}: orbit, wheel: zoom, {K(GameAction.BuildPlaneUp)}/{K(GameAction.BuildPlaneDown)}: plane, {BuildGantry}: leave";
        var steer = $"{K(GameAction.MoveLeft)}/{K(GameAction.MoveRight)}: steer";
        var brake = $"{K(GameAction.Jump)}: brake";
        Drive = $"{K(GameAction.MoveForward)}/{K(GameAction.MoveBack)}: drive, {steer}, {brake}, {GetOut}";
        DriveStart = $"{K(GameAction.MoveForward)}: start, {K(GameAction.MoveBack)}: reverse, {steer}, {brake}, {GetOut}";
        DriveShift = $"{K(GameAction.MoveForward)}/{K(GameAction.MoveBack)}: drive, {steer}, {brake}, {Reload}: gear up, {Sprint}: gear down, {GetOut}";
        Fly = $"{K(GameAction.MoveForward)}/{K(GameAction.MoveBack)}: thrust, {K(GameAction.MoveLeft)}/{K(GameAction.MoveRight)}: turn, {Sprint}: up and hold, {K(GameAction.CrouchHold)}: down, {GetOut}";
        Gunner = $"{Fire}: shoot, {GetOut}";
        const string spin = "mouse or wheel: spin (Shift: free, Ctrl: 5 deg)";
        var back = $"{Aim}: back, Esc: cancel";
        SocketWeld1 = $"Socket attach 1/3: {Fire} a socket on the piece to move, up to {SocketWeld.PullReach:0} m away, {Sprint}: point attach";
        SocketWeld2 = $"Socket attach 2/3: {Fire} a socket or flat face, {back}";
        SocketWeld3 = $"Socket attach 3/3: {spin}, {Reload}: next facing, {Fire}: attach, {back}";
        PointWeld1 = $"Point attach 1/3 ({Sprint} held): {Fire} a point on the piece to move, up to {SocketWeld.PullReach:0} m away";
        PointWeld2 = $"Point attach 2/3: {Fire} a point on the construct, {back}";
        PointWeld3 = $"Point attach 3/3: {spin}, {Reload}: flip, {Fire}: attach, {back}";
        ConfigHint = $"Shift+{Fire}: select more of a kind";
        ConfigHintSelected = $"Shift+{Fire}: select more, {Aim}: clear";
        CargoDraw = $"Hold {Fire} and drag, corner to corner";
        CargoAim = $"Aim at the construct the volume is on, {Reload}: cancel";
        PhysGun = $"hold {Fire}: grab, wheel: distance, {Use} + mouse: turn (Shift: snap), {Aim}: freeze, {Reload} {Reload}: unfreeze yours";
    }

    /// <summary>"F: take it": a key and what it does.</summary>
    public static string Do(string key, string what) => $"{key}: {what}";

    /// <summary>The look line: "Name: state - problem - action"; a null or empty piece is left out.</summary>
    public static string Line(string name, string? state = null, string? problem = null, string? action = null) =>
        name + (string.IsNullOrEmpty(state) ? "" : ": " + state) + (string.IsNullOrEmpty(problem) ? "" : " - " + problem) + (string.IsNullOrEmpty(action) ? "" : " - " + action);

    /// <summary>State words joined with commas; a null or empty one is left out.</summary>
    public static string Join(params string?[] words) => string.Join(", ", words.Where(w => !string.IsNullOrEmpty(w)));
}
