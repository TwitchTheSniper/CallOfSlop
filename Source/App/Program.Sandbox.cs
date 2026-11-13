using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Sandbox mode on this PC (docs/sandbox/CONSTRUCTS.md): the keys (3 wrench, 4 welder, 1/2 back to the guns; with a
/// tool out and a part in hand, hold the fire button to fit it on the ghost socket under the crosshair, the arrows turn it
/// (Shift + Left / Right roll it; GhostTurn.cs) and R puts it back the default way round; hold
/// aim to take the part you look at off; F takes from the rack, sits, opens doors, picks up; T drops), the look target and
/// ghost sockets, and the constructs, ghosts and tool drawn in the world. The server decides everything (GameSandbox.cs).
/// </summary>
public static unsafe partial class Program
{
    private static string? _sbPackKeys; // (made again only when the binds change)
    private static long _sbPackBinds;
    private static int _sbVariant = -1; // the aimed socket: which way round the held part goes (own socket * 4 + spin; smart rotation's guess first)
    private static int _sbLastGhostKey = -1;
    private static bool _sbFiring, _sbRemoving;
    private static long _sbLookKey = -1;
    private static long _sbAimRefusedKey = -2; // (hr01: the red outline's look line, made with the look text)
    private static string _sbAimRefusedText = "";
    private static string _sbLookText = "", _sbGroundText = "";
    private static int _sbGroundKey = -1;
    private static int _sbSeatKeys = -1;
    private static byte _sbTurn; // free placement: the held part's Rot24 turn (the rotate keys turn it, R resets it)
    private static string _sbTurnText = "";
    private static string _sbRackText = ""; // the rack's look text (names the Sandbox inventory key)
    private static int _sbMenuBind = Keybinds.Unbound; // the rotate keys for the HUD ("<- -> ^ v: turn, ...")
    private static long _sbTurnBinds = -1;
    private static string? _sbGroundWhy;
    private static readonly Vector3[] _sbFeet = new Vector3[64];
    private static long _sbJoinKey = -1;
    private static long _sbPieceKey = -1; // what a machine holds under the crosshair, and its look line
    private static string _sbPieceText = "";
    private static string _sbJoinText = "";

    /// <summary>Sandbox with a tool out (or in a seat): the trigger and aim belong to the tools, not the gun.</summary>
    private static bool SandboxHoldsTrigger =>
        _game is { SandboxOn: true } g && g.Local is { Alive: true } me && (g.SpawnPlacing || g.GhostState == BlueprintGhostState.Carrying || _bgEat || g.SandboxHands.Hand(me.Id).Tool != ConstructTool.None || g.SandboxHands.Hand(me.Id).Seated
            || g.SandboxHands.Hand(me.Id).Held != ConstructParts.None); // (a part carried in both hands: the click sets it into storage, never fires the gun)

    private static bool _sbMenuKeyEaten;

    /// <summary>
    /// The Sandbox inventory key (Tab by default; GameAction.SandboxInventory, live only in Sandbox, where there is no
    /// scoreboard) opens the inventory (UiSandboxParts.cs), the mouse free for it;
    /// again (or Esc) closes it, its search box focused or not. Not with the chat or another menu open, in a seat, or dead; refused
    /// with a notice where the map turns the menu off.
    /// </summary>
    private static void SandboxMenuInput()
    {
        if (_sbMenuKeyEaten)
        {
            _sbMenuKeyEaten = Keybinds.IsDown(_window, _settings.Binds, GameAction.SandboxInventory); // (the press that opened it, or closed it from the search box: spent until the key is up; the real key, as _down is blocked while ImGui still has the keyboard this frame)
            return;
        }
        if (_game is not { SandboxOn: true } g || !Pressed(GameAction.SandboxInventory))
            return;
        if (Ui.SandboxPartsOpen)
        {
            Ui.CloseSandboxParts();
            SetMouseCaptured(true);
            return;
        }
        if (Ui.ChatOpen || Ui.PauseOpen || Ui.MenuOpen || MenusOpen || g.Phase != MatchPhase.Playing || g.Local is not { Alive: true } me)
            return;
        if (!g.SandboxInventoryAllowed)
        {
            g.SandboxSay(g.SandboxNoMenuWhy); // (a beacon's reason when there is one near, else the map's rule)
            return;
        }
        if (g.SandboxHands.Hand(me.Id).Seated)
        {
            g.SandboxSay("get out of the seat first");
            return;
        }
        Ui.OpenSandboxParts();
        _sbMenuKeyEaten = true; // (this press opened it: a key still held mustn't read as a new press when a click takes the keyboard back from the search box)
        SetMouseCaptured(false);
    }

    /// <summary>The Sandbox parts menu over the match; it closes (and the mouse goes back to aiming) when you die, sit, the match ends or the mode changes.</summary>
    private static void DrawSandboxPartsInMatch()
    {
        if (!Ui.SandboxPartsOpen)
            return;
        var gone = _session == null || !_game.SandboxInventoryAllowed || _game.Local is not { Alive: true } me || _game.Phase != MatchPhase.Playing || _game.SandboxHands.Hand(me.Id).Seated;
        if (!gone)
        {
            Ui.SandboxCounts ??= Game.InventoryCounts.Instance; // badges from the player's inventory (GameSandboxSalvage.cs)
            Ui.DrawSandboxParts(_game, _settings);
        }
        if (gone || !Ui.SandboxPartsOpen)
        {
            Ui.CloseSandboxParts();
            if (!MenusOpen)
                SetMouseCaptured(true);
            _swallowClick = true; // (the click that picked a part mustn't also fit it)
        }
    }

    /// <summary>The Sandbox keys, every frame while the mouse is captured. Returns true when the mode is on (the normal Interact, slot 3/4 and reload keys are then its own).</summary>
    private static bool SandboxInput(double now)
    {
        ConstructRender.ClearAim();
        if (_game is not { SandboxOn: true } g)
        {
            LeverReset();
            return false;
        }
        g.SandboxLookText = "";
        if (SandboxKeys.Refresh(_settings.Binds))
            SandboxLookReset();
        SandboxRemoverTick(g, g.Local is { Alive: true } lp && g.SandboxHands.Hand(lp.Id).Tool == ConstructTool.Remover && _mouseCaptured && !MenusOpen); // (the remover's question ends when its tool isn't out; Program.SandboxRemover.cs)
        SandboxSelectTick(g, g.Local is { Alive: true } sp && g.SandboxHands.Hand(sp.Id).Tool == ConstructTool.Selector); // (Program.SandboxSelect.cs)
        SandboxTrackTick(g.Local is { Alive: true } tp && g.SandboxHands.Hand(tp.Id).Tool == ConstructTool.Tracks); // (Program.SandboxTracks.cs)
        SandboxSnapFrame(g, now); // (snappy building: markers off, the ease running out, the thunk; Program.SandboxSnap.cs)
        BlueprintGhostWatch(g); // (a menu, death, a seat or another tool drops the blueprint ghost: Program.SandboxBlueprintGhost.cs)
        if (g.Local is not { Alive: true } me || !_mouseCaptured || MenusOpen)
        {
            _sbFiring = _sbRemoving = false;
            _ln.Active = false; // (a Line Layer's stroke ends with the mouse)
            LeverLetGo(g, now);
            return true;
        }
        ref var h = ref g.SandboxHands.Hand(me.Id);
        LeverSettle(g, now); // (a value just let go stays as shown until the server's answer is in: Program.SandboxLever.cs)
        if (Pressed(GameAction.Noclip) && g.NoclipCreative)
            g.ToggleNoclip(); // creative only (GameNoclip.cs; the server has the last word)
        // (3, the wrench's key, stays bound and takes out the attach tool as well; 4 and 5 are the kill-streak keys by default, and Sandbox has no streaks: the attach tool and the linking tool take them)
        if (Pressed(GameAction.Weapon3) || Pressed(GameAction.Weapon4) || Pressed(GameAction.Streak1))
            g.SandboxSend(CActKind.Tool, part: (byte)(h.Tool == ConstructTool.Welder ? ConstructTool.None : ConstructTool.Welder));
        if (Pressed(GameAction.Streak2))
            g.SandboxSend(CActKind.Tool, part: (byte)(h.Tool == ConstructTool.Linker ? ConstructTool.None : ConstructTool.Linker));
        if ((Pressed(GameAction.PrimaryWeapon) || Pressed(GameAction.SecondaryWeapon)) && (h.Tool != ConstructTool.None || h.Held != ConstructParts.None)) // (a gun can't fire past a part in the hands: picking one puts the part away)
            g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
        if (h.Tool != ConstructTool.Welder && SocketWeldBusy)
            _swFlow.Cancel(); // (the welder put away: the socket weld being set up goes)
        if (h.Tool != ConstructTool.Welder && PointWeldBusy)
            _pwFlow.Cancel(); // (...and the point weld)
        LinkToolOut(g, h.Tool == ConstructTool.Linker);
        ConnectorToolOut(in h);
        ConstructRender.ShowWires = h.Tool == ConstructTool.Linker;
        ConstructRender.AimMotion = h.Tool == ConstructTool.Configure; // (a joint under the crosshair swings through its range as a ghost: JointSides, board item js01)
        ConstructRender.VolumesShown = h.Tool is not (ConstructTool.None or ConstructTool.Hands); // (cargo volumes: outlined in build mode)
        if (h.Tool != ConstructTool.Configure && (_cvBody != 0 || _cvArmed))
            CargoReset();
        if (h.Tool != ConstructTool.Configure)
            ConstructRender.VolumeHotBody = null;
        if (Pressed(GameAction.Interact) && g.HarpoonTearOut())
            return true; // (a head stuck in you: F tears it out, before anything else F does)
        if (h.Seated)
        {
            if (!LeverSeated(g, me, in h, now) && Pressed(GameAction.Interact)) // (an Analog Lever in reach takes the key: Program.SandboxLever.cs)
                g.SandboxSend(CActKind.Stand);
            // The keys a seat's input readers can read besides the movement keys: fire, aim, reload, roll.
            var keys = (Down(GameAction.Fire) ? 1 : 0) | (Down(GameAction.Aim) ? 2 : 0) | (Down(GameAction.Reload) ? 4 : 0) | (Down(GameAction.SeatRollLeft) ? 8 : 0) | (Down(GameAction.SeatRollRight) ? 16 : 0) | (Down(GameAction.SeatHook) ? 32 : 0) | (Down(GameAction.SeatEject) ? 64 : 0);
            if (keys != _sbSeatKeys)
            {
                _sbSeatKeys = keys;
                g.SandboxSend(CActKind.SeatKeys, part: (byte)keys);
            }
            SeatedFinger(g, me, in h, now); // (a pilot's finger on a Flight Panel: Program.SandboxFlight.cs)
            return true;
        }
        if (_sbSeatKeys > 0)
        {
            _sbSeatKeys = 0;
            g.SandboxSend(CActKind.SeatKeys, part: 0);
        }
        if (SandboxSpawnInput(g, me))
            return true; // placing a spawn: Fire spawns, Aim stops (Program.SandboxSpawn.cs)
        if (SandboxBlueprintGhostInput(g, me))
            return true; // a blueprint ghost out: Fire places it, the wheel turns it (Program.SandboxBlueprintGhost.cs)
        if (Pressed(GameAction.SandboxDrop) && (h.Held != ConstructParts.None || g.Constructs.HoseHeldBy(me.Id) >= 0))
            g.SandboxSend(CActKind.Drop); // (a held hose's receiver first: it reels back)
        // The backpack (H: stow what you hold; J: draw the next part out).
        var packBinds = (long)_settings.Bind(GameAction.Stow) << 32 ^ (uint)_settings.Bind(GameAction.DrawPack);
        if (_sbPackKeys == null || packBinds != _sbPackBinds)
        {
            _sbPackBinds = packBinds;
            _sbPackKeys = $"{Keybinds.NameOf(_settings, GameAction.Stow)}: stow, {Keybinds.NameOf(_settings, GameAction.DrawPack)}: draw";
        }
        g.SandboxPackKeys = _sbPackKeys;
        SandboxTurnKeys(g);
        if (_settings.Bind(GameAction.SandboxInventory) != _sbMenuBind || _sbRackText.Length == 0)
        {
            _sbMenuBind = _settings.Bind(GameAction.SandboxInventory);
            g.SandboxMenuKey = Keybinds.Name(_sbMenuBind);
            _sbRackText = SandboxKeys.Line("Parts rack", null, null, SandboxKeys.Do(SandboxKeys.Use, "take a part, again for the next kind"));
        }
        if (Pressed(GameAction.Stow))
            g.SandboxSend(CActKind.Stow);
        if (Pressed(GameAction.DrawPack))
            g.SandboxSend(CActKind.Draw);

        // What the crosshair is on.
        SandboxAimRay(g, me, out var eye, out var dir); // (third person: the camera's ray, from the point nearest you; Program.ThirdPerson.cs)
        var reachLook = GantryOn ? GantryMaxDist() + _gDist : ConstructBuild.Reach + 1; // (the Build Gantry's camera sees as far as its range)
        var wall = World.RaycastWorld(eye, dir, reachLook);
        var aimed = g.Constructs.Raycast(eye, dir, MathF.Min(wall, reachLook), out var hit, look: true, noTracks: h.Tool is not (ConstructTool.Tracks or ConstructTool.Configure)); // (only these act on the band: bullets hit it, other tools reach what it runs on)
        BalanceFrame(g, in h, aimed, hit, now); // (centre of mass, lift and thrust on a flyer: Program.SandboxFlight.cs)
        SandboxAidKeys(g, ref h); // (the build stations: Program.SandboxAids.cs)
        SandboxGantryFrame(g, me, aimed, hit, now);
        SandboxSnapGlobal(g, aimed, hit); // (quick building on or off, Ctrl+Z, the pick tap)
        if (aimed)
        {
            var aim = SandboxAimCall(g, me, in h, in hit); // (one predicate for the outline and the action: SandboxAim.cs)
            var touch = SandboxTouchCall(g, me, in h, in hit); // (what hands can work: the yellow glow, on that portion)
            if (aim.Show || touch.Show)
            {
                ConstructRender.AimBody = hit.Body;
                ConstructRender.AimPart = hit.Part;
                ConstructRender.AimRefused = !touch.Can && (aim.Verdict == AimVerdict.Refused || touch.Verdict == AimVerdict.Refused);
                ConstructRender.AimHands = touch.Show;
                ConstructRender.AimSlot = touch.Can ? hit.SlotPlus1 - 1 : -1;
            }
            var d = hit.Body.DefOf(hit.Part);
            ref var part = ref hit.Body.Parts[hit.Part];
            // (rebuilt only when what it shows changes: the look runs every frame)
            var near = hit.Distance <= ConstructBuild.UseReach;
            var fits = h.Tool != ConstructTool.None && ConstructBuild.ToolFits(h.Tool, d);
            // Salvage (milestone 3): anything but your own construct goes into the inventory; someone else's working one can't be taken apart.
            var salvage = !Factions.Shares(hit.Body.Owner, me.Id) && ConstructEdits.Salvageable(hit.Body, me.Id);
            var theirs = ConstructEdits.SalvageRefusal(hit.Body, me.Id) != null;
            var key = ((long)hit.Body.Id << 40) | ((long)hit.Part << 32) | ((long)(part.Broken ? 1 : 0) << 31) | ((long)(near ? 1 : 0) << 30) | ((long)(fits ? 1 : 0) << 29)
                      | ((long)(hit.Body.Driver == 255 ? 1 : 0) << 28) | ((long)(hit.Body.Loose ? 1 : 0) << 27) | (long)Math.Clamp((int)part.Health, 0, 1 << 26)
                      ^ ((long)(byte)ConstructLogic.Quantise(part.Out) << 17) ^ ((long)part.Setting << 12) ^ ((long)part.SitterPlus1 << 4) ^ (h.Tool == ConstructTool.Linker ? 1L << 62 : 0)
                      ^ ((long)g.Constructs.CouplingCount << 50) ^ ((long)(h.Tool == ConstructTool.Welder ? 1 : 0) << 61) ^ ((long)(int)(ConstructPower.Share(hit.Body) * 100) << 53) ^ (hit.Body.PowerOn ? 1L << 60 : 0)
                      ^ (salvage ? 1L << 56 : 0) ^ (theirs ? 1L << 57 : 0) ^ (d.Configurable ? (long)BitConverter.SingleToInt32Bits(ConstructConfig.At(part, d, 0)) << 20 : 0)
                      ^ ((long)part.Rounds << 33) ^ (part.Burn > 0 ? 1L << 55 : 0) ^ (part.Reload > 0 ? 1L << 54 : 0)
                      ^ (d.Role is PartRole.Engine or PartRole.Transmission or PartRole.EnginePiece ? (long)(hit.Body.Rpm / 50) << 8 ^ (long)hit.Body.Gear << 3 : 0) // (an engine's rpm, a gearbox's gear)
                      ^ (d.Piece != EnginePiece.None ? ConstructEngine.LookKey(hit.Body, hit.Part) << 20 : 0) // (a modular engine's readout)
                      ^ (ConstructCargo.IsVolume(d) ? (long)CarriedBy(g.Constructs, hit.Body) << 44 : 0) ^ (hit.Body.CarriedBy != 0 ? 1L << 63 : 0) // (a cargo volume's load; carried)
                      ^ (d.Role == PartRole.Seat || d.IsSeatModule ? (long)hit.Body.RebuildStamp * 1000003L : 0) // (a seat's or a module's links)
                      ^ (d.HasJoint ? (long)hit.Body.PartCount << 41 ^ (long)hit.Body.JointCount << 21 : 0) // (a joint's two part counts)
                      ^ (ConstructDoor.Swings(hit.Body, hit.Part) ? ConstructDoor.IsOpen(hit.Body, hit.Part) ? 3L << 45 : 1L << 45 : 0) // (a door: open or shut)
                      ^ (d.IsRail ? ConstructLinear.LookKey(hit.Body, hit.Part) << 8 : 0) // (where a rail's carriage is, and how it stands)
                      ^ (d.Role == PartRole.FluidTank ? ConstructFluid.LookKey(hit.Body, hit.Part) << 8 : 0) // (what a Fluid Tank holds, and any pipe it refuses)
                      ^ (d.Rotor != null ? ConstructRotor.LookKey(hit.Body, hit.Part) << 8 : d.Surface != null ? ConstructAero.LookKey(hit.Body, hit.Part) << 8 : 0) // (a rotor's speed and blades, a wing's stall speed)
                      ^ (aim.Verdict == AimVerdict.Refused ? (long)(aim.Why?.GetHashCode() ?? 1) << 3 : 0)
                      ^ (hit.Body.Loose && ToolUpgrades.Current.ModuleOf(d.Id) is { } module ? (g.SandboxHands.Level(me.Id, module.Stat) + 1L) * 7919L << 24 : 0) // (a tool upgrade: what you have of it)
                      ^ (h.Tool == ConstructTool.Hands ? h.Held == ConstructParts.None ? 1L << 59 : 1L << 58 : 0); // (hands out: what the click does, and whether they are full)
            if (key != _sbLookKey)
            {
                _sbLookKey = key;
                var setting = d.Configurable && d.Settings.Length > 0 ? $"{d.Config[0].Label.ToLowerInvariant()} {d.Config[0].Show(ConstructConfig.At(part, d, 0))}" // (what the linking tool's reload key steps)
                    : d.Settings.Length > 0 && d.SettingLabels.Length > 0 ? $"{d.SettingName} {d.SettingLabels[Math.Min(part.Setting, (byte)(d.SettingLabels.Length - 1))]}" : "";
                var signal = d.IsSignal ? d.Mains ? ConstructLamps.Lit(hit.Body, hit.Part) ? "on" : "off" : d.HasReading ? LogicDisplays.Look(part, d) : d.Logic == CLogic.AnalogLever ? LeverValueText(part, d) : d.Logic == CLogic.Speed ? part.Out.ToString("0.0") : d.Logic is CLogic.FuelGauge or CLogic.ChargeGauge ? $"{part.Out * 100:0}%" : d.Logic == CLogic.Selector ? part.Out.ToString("0") : part.Out >= 0.5f ? "on" : part.Out <= -0.5f ? "back" : "off" : "";
                var coupled = d.Role == PartRole.Hitch && g.Constructs.CouplingOf(hit.Body, hit.Part) >= 0;
                var charge = (int)MathF.Round(ConstructPower.Share(hit.Body) * 100);
                var unpowered = hit.Body.Electrical && d.Electric && !ConstructPower.On(hit.Body, hit.Part) ? "no power" : "";
                var wrecked = part.Broken ? "wrecked" : "";
                var hp = part.Broken ? "wrecked" : part.Health < d.Health - 0.5f ? $"{part.Health:0}/{d.Health:0} hp" : "";
                var carriedIn = hit.Body.CarriedBy != 0 ? "carried" : "";
                var bare = h.Tool == ConstructTool.None && !hit.Body.Loose;
                var use = SandboxKeys.Use;
                _sbLookText = d.Role switch
                {
                    _ when h.Tool == ConstructTool.Hands && (hit.Body.Loose || ConstructEdits.WreckedAlone(hit.Body)) => HandsLook(hit.Body, d, near, h.Held != ConstructParts.None, carriedIn),
                    PartRole.Suspension when ConstructRoutes.HalfShaftLook(hit.Body, hit.Part) is { } halfLook => SandboxKeys.Line(d.Name, halfLook),
                    PartRole.Rotor when ConstructRotor.Of(hit.Body, hit.Part) >= 0 => SandboxKeys.Line(d.Name, ConstructRotor.Note(hit.Body, hit.Part), null, null),
                    PartRole.Frame when d.Surface != null && hit.Body.SurfaceCount > 0 => SandboxKeys.Line(d.Name, ConstructAero.Note(hit.Body, hit.Part), null, null),
                    PartRole.FluidTank when !hit.Body.Loose && !ConstructEdits.WreckedAlone(hit.Body) => SandboxKeys.Line(d.Name, ConstructFluid.State(part, d), part.Burn > 0 ? "ON FIRE" : ConstructFluid.Problem(hit.Body, hit.Part), h.Tool == ConstructTool.Configure ? SandboxKeys.Do(SandboxKeys.Fire, g.SandboxInfiniteParts ? "set fluid and fill" : "see settings") : !fits || aim.Verdict != AimVerdict.Can ? null : $"hold {SandboxKeys.Aim}: {(salvage ? "salvage" : "take off")}"),
                    _ when h.Tool == ConstructTool.Linker => SandboxKeys.Line(d.Name, SandboxKeys.Join(setting, signal), unpowered.Length > 0 ? unpowered : ConstructModules.Problem(hit.Body, hit.Part)),
                    PartRole.Hitch when near => SandboxKeys.Line(d.Name, coupled ? "coupled" : "free", h.Tool == ConstructTool.Welder ? null : "needs the attach tool", h.Tool == ConstructTool.Welder ? SandboxKeys.Do(use, coupled ? "uncouple" : "couple") : null),
                    PartRole.Battery => SandboxKeys.Line(d.Name, part.Broken ? "wrecked" : hit.Body.PowerOn ? $"{charge}%" : "flat"),
                    PartRole.Transmission when d.HasGears && d.SettingLabels.Length > 0 => SandboxKeys.Line(d.Name, SandboxKeys.Join($"gear {d.SettingLabels[Math.Min(hit.Body.Gear, d.SettingLabels.Length - 1)]}", wrecked)),
                    _ when d.Piece != EnginePiece.None => ConstructEngine.Look(hit.Body, hit.Part), // (what the pieces add up to, or what is missing, with any tool in hand)
                    PartRole.Engine when bare => SandboxKeys.Line(d.Name, part.Broken ? "wrecked" : hit.Part == hit.Body.Engine && hit.Body.EngineRunning ? $"{hit.Body.Rpm:0} rpm, {d.EngineTorque * ConstructDrive.Curve(d, hit.Body.Rpm):0} N m" : "off"),
                    PartRole.Differential when bare && d.Diff == DiffKind.Steering => SandboxKeys.Line(d.Name, ConstructDrive.SteerDiffLook(hit.Body, hit.Part, out var steerWhy), part.Broken ? "wrecked" : steerWhy),
                    PartRole.Differential when bare => SandboxKeys.Line(d.Name, SandboxKeys.Join(d.Diff == DiffKind.Lockable ? part.Setting == 1 ? "locked" : "open" : "", wrecked)),
                    PartRole.Transfer when bare => SandboxKeys.Line(d.Name, SandboxKeys.Join(ConstructDrive.TransferLine(d, part), wrecked)),
                    PartRole.Wheel when bare => SandboxKeys.Line(d.Name, part.Broken && d.FlatTyre ? "flat" : hp),
                    PartRole.Suspension when bare => SandboxKeys.Line(d.Name, SandboxKeys.Join(d.SuspFrequency.Length > 0 && d.SettingLabels.Length > 0 ? d.SettingLabels[Math.Min(part.Setting, (byte)(d.SettingLabels.Length - 1))] : d.ActiveSuspension ? $"height {(part.InCount > 0 ? part.In : 0.5f) * 100:0}%" : "", hit.Body.ArmOnEnd(hit.Part) ? "stood on end: moves straight" : hp)),
                    PartRole.Seat when d.RemoteReceiver => SandboxKeys.Line(d.Name, SandboxKeys.Join(ConstructRemote.ChannelText(part, d), hp), ConstructModules.Problem(hit.Body, hit.Part)),
                    PartRole.Seat => SeatLook(hit.Body, hit.Part, d, near),
                    PartRole.Logic when d.IsSeatModule => SandboxKeys.Line(d.Name, SandboxKeys.Join(ConstructModules.Look(hit.Body, hit.Part), hp), ConstructModules.Problem(hit.Body, hit.Part)),
                    PartRole.Logic when d.Logic == CLogic.AnalogLever => SandboxKeys.Line(d.Name, signal, part.Broken ? wrecked : unpowered, near && !part.Broken ? SandboxKeys.Do(use, "drag") : null),
                    PartRole.Logic when d.Logic == CLogic.Cargo => SandboxKeys.Line(d.Name, ConstructCargo.IsFrozen(part) ? $"frozen, carrying {CarriedBy(g.Constructs, hit.Body)}" : "free", null, !near ? null : SandboxKeys.Do(use, ConstructCargo.IsFrozen(part) ? "let go" : "freeze what is inside")),
                    PartRole.Logic when near && d.Logic is CLogic.Launch => SandboxKeys.Line(d.Name, signal, unpowered, SandboxKeys.Do(use, "fire")),
                    PartRole.Logic when near && d.Logic is CLogic.Conveyor => SandboxKeys.Line(d.Name, signal, unpowered, SandboxKeys.Do(use, "run or stop")),
                    PartRole.Logic when near && d.Logic is CLogic.GravityWell or CLogic.GravityGen => SandboxKeys.Line(d.Name, SandboxKeys.Join(part.Out != 0 ? "on" : "off", signal), unpowered, SandboxKeys.Do(use, part.Out != 0 ? "switch off" : "switch on")),
                    PartRole.Logic when near && d.Logic == CLogic.KeySwitch => SandboxKeys.Line(d.Name, part.Out >= 0.5f ? "on" : "off", part.Broken ? wrecked : LogicInputs.KeyRefusal(hit.Body, me.Id, h.Held) ?? unpowered, SandboxKeys.Do(use, "turn")),
                    PartRole.Logic when near && d.Logic is CLogic.Button or CLogic.Switch or CLogic.Lever or CLogic.Selector => SandboxKeys.Line(d.Name, signal, unpowered, SandboxKeys.Do(use, d.Logic == CLogic.Button ? "press" : d.Logic == CLogic.Selector ? "step on" : "flip")),
                    PartRole.Joint when d.IsRail => ConstructLinear.Look(hit.Body, hit.Part, near),
                    PartRole.Joint when d.JointType == JointType.Swivel => ConstructLinear.SwivelLook(hit.Body, hit.Part),
                    PartRole.Hinge or PartRole.Joint when JointSides.IsJoint(hit.Body, hit.Part, out var jointAt) => JointSides.Look(hit.Body, hit.Part, jointAt, near ? SandboxKeys.Do(use, "open or shut") : null),
                    PartRole.Hinge or PartRole.Joint when near => SandboxKeys.Line(d.Name, null, null, SandboxKeys.Do(use, "open or shut")),
                    _ when ConstructDoor.Swings(hit.Body, hit.Part) => ConstructDoor.Look(hit.Body, hit.Part, near ? use : null), // (a door, or any panel hung on a hinge)
                    PartRole.Door when near => SandboxKeys.Line(d.Name, null, null, SandboxKeys.Do(use, "open or shut")),
                    PartRole.Logic => SandboxKeys.Line(d.Name, SandboxKeys.Join(setting, signal), unpowered),
                    PartRole.Gun when hit.Body.GunOf(hit.Part) is var gk and >= 0 => GunLook(hit.Body, gk, part),
                    PartRole.AmmoBox => SandboxKeys.Line(d.Name, SandboxKeys.Join($"{part.Rounds} rounds", wrecked), part.Burn > 0 ? "ON FIRE" : null),
                    _ when d.LadderTop > 0 && near && !hit.Body.Loose => SandboxKeys.Line(d.Name, null, null, SandboxKeys.Do(use, "climb")), // (a vertical ladder: up from its foot, down from its top)
                    _ when part.Burn > 0 => SandboxKeys.Line(d.Name, hp, "ON FIRE"),
                    _ when hit.Body.Loose && near && h.Tool != ConstructTool.Hands && ToolUpgrades.Current.ModuleOf(d.Id) is { } up => UpgradeLook(g, me, hit.Body, d, up.Stat, up.Level),
                    _ when hit.Body.Loose && d.PhysicsOnly => SandboxKeys.Line(d.Name, carriedIn, "physics gun only"),
                    _ when hit.Body.Loose && near => SandboxKeys.Line(d.Name, carriedIn, null, SandboxKeys.Do(use, "pick up")),
                    _ when ConstructEdits.WreckedAlone(hit.Body) => SandboxKeys.Line(d.Name, SandboxKeys.Join($"wrecked, {d.Mass:0} kg", carriedIn), "haul it to a Recycler"),
                    _ => SandboxKeys.Line(d.Name, hp, null, !fits || aim.Verdict != AimVerdict.Can ? null : $"hold {SandboxKeys.Aim}: {(salvage ? "salvage" : "take off")}"),
                };
                if (aim.Verdict == AimVerdict.Refused)
                    _sbAimRefusedText = SandboxKeys.Line(d.Name, null, aim.Why); // (red outline: why the tool can't)
                _sbAimRefusedKey = key;
            }
            g.SandboxLookText = aim.Verdict == AimVerdict.Refused && _sbAimRefusedKey == key ? _sbAimRefusedText : _sbLookText;
        }
        // Hoses (board item wl01): the look line on a reel or an inlet, and the HUD line while holding one (Program.SandboxWorldLinks.cs).
        WorldLinkRender.HandOf ??= HoseHandOf;
        HoseLook(g, me, aimed, hit, aimed && hit.Distance <= ConstructBuild.UseReach);
        // Physical storage (decision 18): a storage part, or a part stored in one, under the crosshair.
        var storage = aimed ? g.Constructs.StoreOf(hit.Body, hit.Part) : null;
        var storeNear = aimed && hit.Distance <= ConstructBuild.UseReach;
        var slotted = storage != null && storeNear && ConstructParts.Get(h.Held) is { } carried && StorageSlots(g, hit, storage, carried, eye, dir);
        if (storage != null)
            g.SandboxLookText = StorageLook(g, me, hit, storage, storeNear, ConstructParts.Get(h.Held));
        var inMachine = aimed && ConstructPress.MachineOf(hit.Body, hit.Part) >= 0;
        if (inMachine && storeNear)
        {
            var pk = (long)hit.Body.Id << 32 | (long)hit.Part << 16 | hit.Body.DefOf(hit.Part).Id;
            if (pk != _sbPieceKey)
            {
                _sbPieceKey = pk;
                _sbPieceText = SandboxKeys.Line(hit.Body.DefOf(hit.Part) is { IsOrdnance: true } ord ? ord.Description : hit.Body.DefOf(hit.Part) is { IsExtrusion: true } on ? Extrusion.Label(on.ExKey) : hit.Body.DefOf(hit.Part).Name, null, null, SandboxKeys.Do(SandboxKeys.Use, "take it out"));
            }
            g.SandboxLookText = _sbPieceText;
        }
        if (aimed && hit.RoutePlus1 == 0 && hit.Part >= 0 && hit.Part < hit.Body.PartCount && hit.Body.DefOf(hit.Part).IsRouter)
        {
            // A Conveyor Router: its last piece and where it went, or the piece it holds and why (ConstructLogistics.Look).
            var rk = ((long)hit.Body.Id << 40) ^ ((long)hit.Part << 24) ^ (hit.Body.Logi?.ShowVersion ?? 0) ^ (ConstructRecycler.Powered(g.Constructs, hit.Body, hit.Part) ? 1L << 62 : 0) ^ ((long)hit.Body.RebuildStamp << 32);
            if (rk != _sbRouterKey)
            {
                _sbRouterKey = rk;
                _sbRouterText = ConstructLogistics.Look(g.Constructs, hit.Body, hit.Part);
            }
            g.SandboxLookText = _sbRouterText;
        }
        if (aimed && hit.RoutePlus1 == 0 && hit.Part >= 0 && hit.Part < hit.Body.PartCount && hit.Body.DefOf(hit.Part).Lifter != null)
        {
            // A Loader Arm: what it does or waits on, and its reach drawn round it (ConstructLifters.Look, LifterRender.Look).
            LifterRender.Look(hit.Body.Id, hit.Part);
            var lk = ConstructLifters.LookKey(g.Constructs, hit.Body, hit.Part) ^ ((long)hit.Body.Id << 48) ^ ((long)hit.Part << 52);
            if (lk != _sbLifterKey)
            {
                _sbLifterKey = lk;
                _sbLifterText = ConstructLifters.Look(g.Constructs, hit.Body, hit.Part);
            }
            g.SandboxLookText = _sbLifterText;
        }
        var terminalTook = TerminalFrame(g, me, in h, aimed, hit, storeNear); // (a Smart Terminal under the crosshair takes the wheel, the use key and reload: Program.SandboxTerminal.cs)
        var feet = me.Position;
        var nearRack = World.Map.Sandbox is { } sb && Vector3.Distance(feet with { Y = 0 }, sb.Rack) <= 4.5f;
        if (nearRack && !aimed)
            g.SandboxLookText = _sbRackText;
        // The welder on a part that touches another construct: F joins the two into one (decision 27: only the welder
        // attaches a piece; its three-step socket weld moves one into place: Program.SandboxSocketWeld.cs).
        JoinPlan jplan = default;
        var join = h.Tool == ConstructTool.Welder && aimed && hit.Distance <= ConstructBuild.Reach && hit.Part >= 0 && hit.Body.DefOf(hit.Part).Role != PartRole.Hitch && ConstructPlace.FindJoin(g.Constructs, hit.Body, hit.Part, out jplan);
        if (join)
        {
            var jk = hit.Body.Id | ((long)jplan.Other!.Id << 16) | ((long)hit.Part << 32) | ((long)h.Tool << 48);
            if (jk != _sbJoinKey)
            {
                _sbJoinKey = jk;
                _sbJoinText = SandboxKeys.Line(hit.Body.DefOf(hit.Part).Name, "touching another construct", null, SandboxKeys.Do(SandboxKeys.Use, "attach together"));
            }
            g.SandboxLookText = _sbJoinText;
        }
        var leverTook = LeverFrame(g, in h, aimed, hit, eye, now, join, slotted); // (an Analog Lever takes the use key, and LMB with the hands out: Program.SandboxLever.cs)
        leverTook |= ControlFrame(g, me, in h, aimed, hit, join); // (keys and signs too: Program.SandboxControls.cs)
        _scrTook = PointerFrame(g, me, in h, eye, dir, aimed, hit, now); // (Hands on a screen: a mouse on a page, a finger on a panel's buttons: Program.SandboxPointer.cs)
        ConstructRender.AimOnPage = _scrTook;
        var talkTook = TalkFrame(g, me, eye, dir, aimed, aimed ? hit.Distance : float.MaxValue); // (one of the story's people under the crosshair: Program.StoryTalk.cs)
        talkTook = talkTook || LoreFrame(g, aimed, hit); // (a lore item under the crosshair, when no person is in front of it: Program.StoryLore.cs)
        var magFill = !talkTook && aimed && hit.Part >= 0 && hit.Part < hit.Body.PartCount && hit.Distance <= ConstructBuild.UseReach && ConstructParts.Get(h.Held) is { } magHeld && ConstructAmmo.IsMagazine(magHeld) && (ConstructAmmo.IsSource(hit.Body.DefOf(hit.Part)) || ConstructAmmo.IsMagazine(hit.Body.DefOf(hit.Part)));
        if (magFill)
        {
            g.SandboxLookText = SandboxKeys.Line(hit.Body.DefOf(hit.Part).Name, null, null, SandboxKeys.Do(SandboxKeys.Use, ConstructAmmo.IsSource(hit.Body.DefOf(hit.Part)) ? "fill the magazine" : "swap magazines"));
            if (Pressed(GameAction.Interact))
                g.SandboxSend(CActKind.Use, body: hit.Body.Id, part: (ushort)hit.Part);
        }
        if (talkTook)
        {
            // (the use key went to a person, or would: nothing behind them takes it)
        }
        else if (Pressed(GameAction.Interact) && !magFill && inMachine && storeNear && !(h.Tool == ConstructTool.PhysicsGun && h.GrabBody != 0))
            g.SandboxSend(CActKind.Bin, body: hit.Body.Id, part: (ushort)hit.Part); // (what a machine holds comes out: the server's UseBin)
        else if (Pressed(GameAction.Interact) && !magFill && aimed && hit.RiderPlus1 > 0 && hit.RiderPlus1 <= hit.Body.ConveyedCount && hit.Distance <= ConstructBuild.UseReach && !(h.Tool == ConstructTool.PhysicsGun && h.GrabBody != 0))
            g.SandboxSend(CActKind.Rider, body: hit.Body.Id, part: (ushort)(hit.RoutePlus1 - 1), socket: (ushort)Math.Clamp((int)MathF.Round(hit.Body.Conveyed[hit.RiderPlus1 - 1].At * 100), 0, ushort.MaxValue)); // (a piece off a conveyor, into the hands: ConstructEditsRack.cs)
        else if (Pressed(GameAction.Interact) && !magFill && storage != null && storeNear && hit.Body.DefOf(hit.Part).IsCraftTerminal && hit.RackPlus1 == 0 && !(h.Tool == ConstructTool.PhysicsGun && h.GrabBody != 0))
        {
            if (hit.Body.DefOf(hit.Part).IsPanelDesigner)
                OpenPanelDesigner(hit.Body, hit.Part); // (a Panel Designer: its own panel, Program.SandboxPanelDesigner.cs)
            else
                OpenCraftingTerminal(hit.Body, hit.Part); // (a Crafting Terminal's desk: its panel; a stack in its tray is taken as from a rack, below)
        }
        else if (Pressed(GameAction.Interact) && !magFill && storage != null && storeNear && hit.Body.DefOf(hit.Part).IsPieceRack && !(h.Tool == ConstructTool.PhysicsGun && h.GrabBody != 0))
            g.SandboxSend(ShiftHeld ? CActKind.RackDrop : CActKind.Bin, body: hit.Body.Id, part: (ushort)hit.Part); // (a Piece Rack: the piece looked at into the hands; Shift: that kind loose over the rack)
        else if (Pressed(GameAction.Interact) && !magFill && storage != null && storeNear && (hit.SlotPlus1 > 0 || hit.Body.DefOf(hit.Part).IsBin || hit.Body.DefOf(hit.Part).IsAssembler || hit.Body.DefOf(hit.Part).IsPress))
        {
            // F on a stored part takes it into the hands; on a bin tips your scrap in (or takes a load out).
            if (hit.SlotPlus1 > 0)
                g.SandboxSend(CActKind.TakeStored, body: hit.Body.Id, part: (ushort)hit.Part, socket: (byte)(hit.SlotPlus1 - 1));
            else
                g.SandboxSend(CActKind.Bin, body: hit.Body.Id, part: (ushort)hit.Part);
        }
        else if (Pressed(GameAction.Interact) && !magFill && !leverTook && !terminalTook && !(h.Tool == ConstructTool.PhysicsGun && h.GrabBody != 0)) // (holding with the physics gun, F turns the held thing)
        {
            if (aimed && hit.Distance <= ConstructBuild.UseReach + 1.4f && hit.Body.DefOf(hit.Part).IsGunBench && hit.SlotPlus1 == 0)
            {
                // The Gun Workbench: F opens the weapon builder (GameSandboxBench.cs, UiWorkbenchRun.cs); a lit slot is still F to take.
                Ui.OpenSandboxBench(g, ConstructEdits.BenchCentre(hit.Body, hit.Part));
                SetMouseCaptured(false);
            }
            else if (aimed && hit.Distance <= ConstructBuild.UseReach + 1.4f && hit.Body.DefOf(hit.Part).IsOrdnanceBench && hit.SlotPlus1 == 0)
            {
                Ui.OpenOrdnanceBench(g); // (the Ordnance Bench: UiOrdnanceBench.cs)
                SetMouseCaptured(false);
            }
            else if (join)
                g.SandboxSend(CActKind.Join, body: hit.Body.Id, part: (ushort)hit.Part);
            else if (aimed && hit.Distance <= ConstructBuild.UseReach && hit.Body is { Loose: true, PartCount: 1 } && ToolUpgrades.Current.ModuleOf(hit.Body.Parts[0].Def) != null)
                g.SandboxSend(CActKind.Use, body: hit.Body.Id, part: (ushort)hit.Part); // (a tool upgrade: F fits it, ConstructEditsUpgrades.cs)
            else if (aimed && hit.Distance <= ConstructBuild.UseReach && hit.Body.Loose && h.Held == ConstructParts.None)
                g.SandboxSend(CActKind.PickUp, body: hit.Body.Id);
            else if (aimed && hit.Distance <= ConstructBuild.UseReach)
                g.SandboxSend(CActKind.Use, body: hit.Body.Id, part: (ushort)hit.Part);
            else if (nearRack)
                g.SandboxSend(CActKind.Rack);
        }

        if (_lvHeld)
            return true; // (a hand on an Analog Lever: no tool acts until it lets go)

        // The physics gun: grab, move, turn, freeze (Program.PhysicsGun.cs).
        if (h.Tool == ConstructTool.PhysicsGun)
        {
            PhysGunInput(g, me, ref h, eye, dir, now);
            return true;
        }
        PhysGunIdle();

        // Routed links (decision 20): the look line on a link, and the wrench's or welder's repair (Program.SandboxLinks.cs).
        SandboxRouteLook(g, ref h, aimed, hit);
        SandboxCellPatch(g, ref h, aimed, hit); // (the attach tool on a holed lift cell: Program.SandboxCells.cs)
        // The linking tool: routing links node by node, the edit modes, settings (Program.SandboxLinks.cs).
        if (h.Tool == ConstructTool.Linker)
        {
            SandboxLinkerFrame(g, me, ref h, eye, dir, aimed, hit);
            return true;
        }

        // The configure tool: fire on a part opens its settings panel; shift+fire selects more of a kind.
        if (h.Tool == ConstructTool.Configure)
        {
            if (!SandboxCargoInput(g, me, eye, dir, aimed, hit) && !ConnectorConfigInput(g, eye, dir, aimed, hit)) // (a cargo volume's face handles and box drawing first, then a rope or spring)
                SandboxConfigInput(g, me, aimed, hit);
            return true;
        }

        // The remover tool: LMB marks parts and links, RMB twice deletes them (Program.SandboxRemover.cs).
        if (h.Tool == ConstructTool.Remover)
        {
            if (!ConnectorRemoverInput(g, eye, dir, aimed, hit)) // (a rope or spring goes with one click)
                SandboxRemoverInput(g, me, aimed, hit);
            return true;
        }

        // The track tool: wheels picked round a loop (Program.SandboxTracks.cs).
        if (h.Tool == ConstructTool.Tracks)
        {
            SandboxTrackInput(g, me, aimed, hit);
            return true;
        }

        // The selector: groups of parts (Program.SandboxSelect.cs).
        if (h.Tool == ConstructTool.Selector)
        {
            SandboxSelectInput(g, me, eye, dir, now);
            return true;
        }

        // The Nudge tool: the triad and arcs (Program.SandboxNudge.cs).
        if (h.Tool == ConstructTool.Nudge)
        {
            SandboxNudgeInput(g, me, eye, dir, aimed, hit);
            return true;
        }
        // The no-collide tool: two pieces stop colliding (Program.SandboxNoCollide.cs).
        if (h.Tool == ConstructTool.NoCollide)
        {
            SandboxNoCollideFrame(g, hit, aimed);
            return true;
        }
        _ncFirstBody = 0;

        // The ground drill: LMB digs, RMB puts ground back (Program.SandboxSculpt.cs).
        if (h.Tool == ConstructTool.GroundDrill)
        {
            SandboxSculptInput(g, me, eye, dir, aimed, hit, now);
            return true;
        }
        SculptIdle();

        // The paint tool: LMB paints, RMB picks the colour (Program.SandboxPaint.cs).
        if (h.Tool == ConstructTool.Paint)
        {
            SandboxPaintInput(g, me, aimed, hit);
            return true;
        }
        PaintIdle();

        // The texture paint tool: LMB paints the texture, RMB picks a part's, R opens the list (Program.SandboxTexture.cs).
        if (h.Tool == ConstructTool.Texture)
        {
            SandboxTextureInput(g, me, aimed, hit);
            return true;
        }
        TextureIdle();

        // The material painter: LMB remakes the part in the material in hand, RMB picks a part's, R opens the list (Program.SandboxMaterial.cs).
        if (h.Tool == ConstructTool.Material)
        {
            SandboxMaterialInput(g, me, aimed, hit);
            return true;
        }
        MaterialIdle();

        // The fuse tool: LMB fuses the selection or the aimed piece into one unit, RMB un-fuses (Program.SandboxFuse.cs).
        if (h.Tool == ConstructTool.Fuse)
        {
            SandboxFuseInput(g, me, aimed, hit);
            return true;
        }
        FuseIdle();

        // Hands: LMB takes what lies loose or stored, RMB drops what they hold (Program.SandboxHands.cs).
        if (h.Tool == ConstructTool.Hands)
        {
            SandboxHandsInput(g, me, in h, eye, dir, wall, aimed, hit, slotted);
            return true;
        }

        // The attach tool's Rope and Spring modes, with nothing in the hands (Program.SandboxConnectors.cs).
        if (ConnectorModeOn(in h))
        {
            SocketWeldIdle();
            PointWeldIdle();
            g.SandboxWeldHint = "";
            SandboxConnectorFrame(g, eye, dir, aimed, hit, _weldModes.Connector!.Value);
            return true;
        }
        // The welder's freeform mode and the seam cut (Program.SandboxFreeform.cs).
        if (h.Tool == ConstructTool.Welder)
            SandboxFreeformInput(g, ref h, eye, dir, aimed, hit);
        // Ghost sockets for the held part (a tool out only: bare hands never edit).
        var held = AidHeld(ConstructParts.Get(h.Held)); // (a Line Layer's or Replacer's picked part: Program.SandboxAids.cs)
        // The welder's default mode, with nothing in the hands: the three-step socket weld (Program.SandboxSocketWeld.cs).
        g.SandboxWeldHint = "";
        var socketMode = h.Tool == ConstructTool.Welder && _weldModes.Mode == SandboxWeldModes.Option.Socket && held == null && !h.Seated;
        if (socketMode && PointAttachNow)
        {
            SocketWeldIdle();
            PointWeldInput(g, eye, dir, aimed, hit); // (the point attach: Shift held at its first step; Program.SandboxPointWeld.cs, board item pw01)
        }
        else if (socketMode)
        {
            PointWeldIdle();
            SocketWeldInput(g, eye, dir, aimed, hit);
        }
        else
        {
            PointWeldIdle();
            SocketWeldIdle();
        }
        // A Line Layer's stroke holds the frame from its press to its release, wherever the crosshair goes (Program.SandboxAids.cs).
        if (_ln.Active)
        {
            SandboxLineFrame(g, in h, held, eye, dir);
            return true;
        }
        // The Replacer: the click swaps the aimed part (Program.SandboxAids.cs).
        if (_buildMode == BuildMode.Replace && SandboxReplaceInput(g, in h, aimed, hit, held))
            return true;
        // The gantry's build plane: a ray that meets nothing crosses it (Program.SandboxGantry.cs).
        if (!aimed && held != null && GantryPlaneHit(g, eye, dir, out var planeHit))
        {
            aimed = true;
            hit = planeHit;
        }
        // Storage first: the slots the held part fits light up (any hands); a click sets it into the chosen one.
        if (slotted && Pressed(GameAction.Fire) && !_swallowClick && ConstructRender.SlotSelected >= 0)
            g.SandboxSend(CActKind.StoreIn, body: hit.Body.Id, part: (ushort)hit.Part, socket: (byte)ConstructRender.SlotSelected);
        if (h.Tool != ConstructTool.None && held != null && !slotted && !SandboxFreeformOn(h) && !ConstructBuild.ToolFits(h.Tool, held))
        {
            // (hr01: the other tool's part: no ghost, no socket markers, only the reason)
            if (aimed || wall <= ConstructBuild.Reach + 0.5f)
                g.SandboxLookText = $"Needs the {held.Tool.Label()}";
        }
        else if (h.Tool != ConstructTool.None && held != null && !slotted && !SandboxFreeformOn(h)) // (freeform welding: the held part isn't fitted)
        {
            ConstructRender.GhostDef = held;
            if (aimed)
                SandboxHeldOnConstruct(g, in h, hit, held, eye, dir, now, ScreenSize, Gfx.ViewProj);
            else if (!aimed && wall <= ConstructBuild.Reach + 0.5f && World.Query.Raycast(eye, dir, ConstructBuild.Reach + 0.5f, QueryFilter.All, out var surf))
            {
                if (_settings.SnapBuild)
                {
                    SnapRender.Active = true;
                    SnapRender.Body = null;
                    SnapRender.Held = held;
                }
                // Free placement: the surface aimed at (terrain, the map, a floor): a new construct rooted at this part.
                var flags = (byte)((g.SandboxPlaceGrid ? ConstructPlace.FlagGrid : 0) | (g.SandboxPlaceAnchor && g.SandboxInfiniteParts ? ConstructPlace.FlagAnchor : 0));
                if (_settings.SmartRotation && (held.Id != _sbFreeHeld || _sbFreeN == 0))
                {
                    SandboxSmartLoad();
                    // Smart rotation: a new part in hand starts at its guess (upright and facing forward, or the way you last set it down).
                    _sbFreeHeld = held.Id;
                    _sbFreeN = SmartRotation.RankFree(held, SmartRotation.Memory, _sbFree);
                    _sbFreePos = 0;
                    _sbTurn = _sbFree[0];
                }
                else if (!_settings.SmartRotation)
                    _sbFreeN = 0;
                if (Pressed(GameAction.Reload))
                {
                    // R: back to the default orientation (smart rotation's guess); at the guess, the next best.
                    if (_sbFreeN > 0)
                    {
                        _sbFreePos = _sbFreePos >= 0 && _sbTurn == _sbFree[_sbFreePos] ? (_sbFreePos + 1) % _sbFreeN : 0;
                        _sbTurn = _sbFree[_sbFreePos];
                    }
                    else
                        _sbTurn = 0;
                    BuildSounds.Play(BuildSound.Rotate, default);
                }
                else if (SandboxTurnPressed() is { } turnKey)
                {
                    _sbFreePos = -1;
                    // The rotate keys, relative to the camera in the new construct's frame (the frame Pose makes below).
                    SandboxCamera(g, out var camRight, out var camUp);
                    var frame = ConstructPlace.Frame(surf.Normal, ConstructPlace.FrameYaw(g.LookYaw, (flags & ConstructPlace.FlagGrid) != 0), out _);
                    _sbTurn = Rot24.Compose(GhostTurn.Delta(turnKey, frame, camRight, camUp), _sbTurn);
                    BuildSounds.Play(BuildSound.Rotate, default);
                }
                var feetN = 0;
                foreach (var pl in g.Players.Values)
                    if (pl.Alive && feetN < _sbFeet.Length)
                        _sbFeet[feetN++] = pl.Position;
                var why = ConstructPlace.Pose(World.Query, held, surf.Point, surf.Normal, g.LookYaw, _sbTurn, (flags & ConstructPlace.FlagGrid) != 0, out var fp);
                if (why == null)
                {
                    why = ConstructPlace.LimitRefusal(g.Constructs, me.Id) ?? ConstructPlace.Fits(g.Constructs, World.Query, held, fp, _sbFeet.AsSpan(0, feetN));
                    ConstructRender.PlaceAt = fp.Pos;
                    ConstructRender.PlaceRot = fp.Rot;
                    ConstructRender.PlaceTurn = fp.Turn;
                    ConstructRender.PlaceValid = why == null;
                }
                var gkey = held.Id | (_sbTurn << 8) | (flags << 16) | (ConstructBuild.ToolFits(h.Tool, held) ? 1 << 20 : 0) | (int)(_sbTurnBinds & 0xff) << 21;
                if (gkey != _sbGroundKey || !ReferenceEquals(why, _sbGroundWhy))
                {
                    _sbGroundKey = gkey;
                    _sbGroundWhy = why;
                    var how = SandboxKeys.Join((flags & ConstructPlace.FlagAnchor) != 0 || held.Role == PartRole.Foundation ? "anchored" : "", (flags & ConstructPlace.FlagGrid) != 0 ? "on the grid" : "");
                    _sbGroundText = why != null ? $"Can't place here: {why}"
                        : !ConstructBuild.ToolFits(h.Tool, held) ? $"Needs the {held.Tool.Label()}"
                        : SandboxKeys.Line("New construct", how, null, $"hold {SandboxKeys.Fire}: place");
                }
                g.SandboxLookText = _sbGroundText;
                if (Pressed(GameAction.Fire) && !_swallowClick)
                {
                    if (why != null)
                    {
                        g.SandboxSay(why);
                        BuildSounds.Play(BuildSound.Refuse, default);
                    }
                    else
                    {
                        g.SandboxSend(CActKind.PlaceNew, at: surf.Point, yaw: g.LookYaw, normal: surf.Normal, variant: _sbTurn, flags: flags);
                        _sbFiring = !(_settings.SnapBuild && g.SandboxInfiniteParts);
                        SandboxSnapSent(null, held, default, surf.Point);
                        if (_sbFreeN > 0)
                        {
                            SandboxLearn(SmartRotation.PlacedFree(SmartRotation.Memory, held, _sbTurn, _sbFree[0]));
                            _sbFreeN = 0; // (the next guess counts this one)
                        }
                    }
                }
            }
        }
        // Taking a part off: hold aim on it with the tool it needs.
        if (h.Tool != ConstructTool.None && aimed && Pressed(GameAction.Aim) && !hit.Body.Loose && hit.SlotPlus1 == 0) // (a stored part is taken with F, not a tool)
        {
            g.SandboxSend(CActKind.Remove, body: hit.Body.Id, part: (ushort)hit.Part);
            _sbRemoving = true;
        }
        // Let go before it's done: the edit stops.
        if ((_sbFiring && !Down(GameAction.Fire)) || (_sbRemoving && !Down(GameAction.Aim)))
        {
            if (h.Edit != EditKind.None)
                g.SandboxSend(CActKind.Cancel);
            _sbFiring = _sbRemoving = false;
            SocketWeldRender.PullStop(); // (a timed weld let go: the piece stays where it was)
        }
        return true;
    }

    /// <summary>
    /// The held part aimed at a construct (the per-frame ghost path): its ghosts, the one chosen (the screen magnet for a gun part,
    /// wheel or door), smart rotation, the turn keys, the click that sends Attach, and the reason on the HUD when no ghost is possible.
    /// Split out so the self-test drives exactly this code with a scripted camera (board wa01: the old tests skipped the magnet's limits).
    /// </summary>
    private static void SandboxHeldOnConstruct(Game g, in SandboxHand h, in ConstructHit hit, ConstructPartDef held, Vector3 eye, Vector3 dir, double now, Vector2 screen, in Matrix4x4 viewProj)
    {
        // (a loose part or a wreck too: building onto it makes it a construct, yours)
        var c = hit.Body;
        c.WorldFrame(c.Parts[hit.Part].Seg, out var q, out var t);
        var near = Vector3.Transform(hit.Point - t, Quaternion.Conjugate(q));
        if (ConstructPhysics.RestOfDrawn(c, hit.Part, hit.Point, out var legRest))
            near = legRest; // (a walker leg or what rides it: its weld sockets are on its canonical rest pose, ConstructWalkRiders.cs)
        var n = SandboxSnapGhosts(c, held, near);
        ConstructRender.SetGhosts(c, n);
        // The one nearest the crosshair.
        var best = -1;
        var bestD = GantryOn ? MathF.Max(0.8f, hit.Distance * 0.05f) : 0.8f; // (the gantry's camera stands far off)
        for (var k = 0; k < n; k++)
        {
            c.SocketAt(ConstructRender.Ghosts[k].Part, ConstructRender.Ghosts[k].Socket, out var at, out _);
            c.SegPose(c.Parts[ConstructRender.Ghosts[k].Part].Seg, out var sq, out var st);
            var world = ConstructPhysics.RideFrameOf(c, ConstructRender.Ghosts[k].Part, ConstructRender.Ghosts[k].Socket, c.Pos, c.Rot, out var lq, out var lt)
                ? lt + Vector3.Transform(at, lq) // (on a walker leg's segment, as drawn)
                : c.ToWorld(st + Vector3.Transform(at, sq));
            var along = Vector3.Dot(world - eye, dir);
            var off = Vector3.Distance(world, eye + dir * along);
            if (along > 0 && off < bestD)
            {
                bestD = off;
                best = k;
            }
        }
        best = SandboxSnapSelect(c, held, ref n, best, near, screen, in viewProj); // (a gun part, wheel or door magnet-snaps to its socket on screen)
        if (hit.Part < c.PartCount && ConstructAmmo.SocketRefusal(c, hit.Part, held) != null)
            best = -1; // (aimed at an Ammo Socket whose gun can't take this magazine: no ghost on the gun beside it, and the reason below)
        ConstructRender.GhostSelected = best;
        if (best >= 0)
        {
            ref var gh = ref ConstructRender.Ghosts[best];
            var key = gh.Part * 64 + gh.Socket + c.Id * 4096;
            var total = held.Sockets.Length * 4;
            SandboxSmartGuess(c, held, in gh, key);
            SandboxSnapRank(held, in gh); // (only the own sockets snap building allows, the mount first)
            if (SandboxSnapSwitch(g, c, held, in gh))
            {
                // K: the next own connection point mates (set above)
            }
            else if (Pressed(GameAction.Reload))
            {
                SandboxGuessAgain(gh.Variant); // R: back to the guess (smart rotation's, else the first that fits); at the guess, the next best
                BuildSounds.Play(BuildSound.Rotate, default);
            }
            else if (SandboxTurnPressed() is { } turnKey)
            {
                // The rotate keys: a quarter turn as you see it, snapped to the construct's axes (GhostTurn.cs).
                var cv = _sbVariant;
                var from = ConstructBuild.Place(c, gh.Part, gh.Socket, held, cv / 4, cv % 4, out var cur) == null ? cur.Rot : gh.Rot;
                c.WorldFrame(c.Parts[gh.Part].Seg, out var fq, out _);
                SandboxCamera(g, out var camRight, out var camUp);
                var nv = GhostTurn.AttachVariant(c, held, gh.Part, gh.Socket, from, GhostTurn.Delta(turnKey, fq, camRight, camUp));
                if (nv >= 0 && _settings.SnapBuild && SnapBuild.OwnOrder(held).IndexOf((byte)(nv / 4)) < 0)
                    nv = -1; // (a single-purpose part turns only on its own connection)
                if (nv >= 0)
                {
                    _sbVariant = nv;
                    _sbRankPos = _sbRank.AsSpan(0, _sbRankN).IndexOf(nv);
                    BuildSounds.Play(BuildSound.Rotate, default);
                }
                else
                {
                    g.SandboxSay("can't turn that way on this socket");
                    BuildSounds.Play(BuildSound.Refuse, default);
                }
            }
            var v = _sbVariant >= 0 && _sbVariant < total ? _sbVariant : gh.Variant;
            ConstructRender.GhostValid = ConstructBuild.Place(c, gh.Part, gh.Socket, held, v / 4, v % 4, out var shown) == null;
            if (ConstructRender.GhostValid)
                gh = shown with { Variant = v };
            // (what the server's click will ask of the reach: ConstructEdits.AttachReach; the gantry's own reach is the server's to judge)
            var outOfReach = !GantryOn && ConstructEdits.AttachReach(eye, c, gh.Part, gh.Socket, in gh) != null;
            ConstructRender.GhostValid &= !outOfReach;
            SandboxSnapShown(g, c, held, in gh, now);
            SandboxJointGhostText(g, c.DefOf(gh.Part), gh.Socket, held, v / 4);
            if (outOfReach)
                g.SandboxLookText = SandboxKeys.Line(held.Name, "too far away: stand nearer", null);
            else if (held.Role == PartRole.Wheel && c.DefOf(gh.Part).Role == PartRole.Suspension && SmartRotation.AxleUp(c.DefOf(gh.Part), c.Parts[gh.Part].Rot))
                g.SandboxLookText = SandboxKeys.Line(held.Name, "lies flat, it won't roll: turn the suspension so its axle is level", null);
            var quick = _settings.SnapBuild && g.SandboxInfiniteParts;
            var pressed = Pressed(GameAction.Fire) && !_swallowClick;
            if (ConstructRender.GhostValid)
            {
                SandboxMirrorGhost(g, c, held, in gh); // (a Mirror Table: the image too; Program.SandboxAids.cs)
                if (_mirrorCount > 0 && _mirrorProblem.Length > 0)
                    g.SandboxLookText = SandboxKeys.Line("Mirror", held.Name, _mirrorProblem);
            }
            if (_buildMode == BuildMode.Line)
            {
                g.SandboxLookText = SandboxLineHint(held);
                if (pressed && ConstructRender.GhostValid)
                    SandboxLineStart(c, held, in gh, v, eye, dir); // (the Line Layer's press: Program.SandboxAids.cs)
            }
            else if (SandboxSnapPaint(g, in h, c, in gh, pressed) || pressed)
            {
                g.SandboxSend(CActKind.Attach, body: c.Id, part: (ushort)gh.Part, socket: (byte)gh.Socket, variant: (byte)v);
                _sbFiring = !quick; // (quick building fits at once: nothing to hold, so nothing to cancel)
                if (ConstructRender.GhostValid)
                {
                    c.SegPose(c.Parts[gh.Part].Seg, out var wq, out var wt);
                    SandboxSnapSent(c, held, in gh, c.ToWorld(wt + Vector3.Transform(gh.Pos, wq)));
                }
                if (_sbRankN > 0 && ConstructRender.GhostValid)
                    SandboxLearn(SmartRotation.Placed(SmartRotation.Memory, c, held, gh.Part, gh.Socket, gh.Rot, _sbRankRot[0]));
            }
        }
        else if ((held.Role == PartRole.GunPart || SnapBuild.SinglePurpose(held)) && hit.Part < c.PartCount)
        {
            // (wa01: a part aimed at something it can't go on said nothing; always say why)
            var target = c.DefOf(hit.Part);
            g.SandboxLookText = ConstructAmmo.SocketRefusal(c, hit.Part, held) is { } noMag
                ? SandboxKeys.Line(target.Name, null, noMag)
                : target.Role == PartRole.Gun && !target.IsReceiverGun && held.Role == PartRole.GunPart
                ? $"{target.Name}: finished gun - gun parts go on a builder receiver"
                : SnapRender.Cache.Count == 0
                    ? $"No free socket for it on this {(target.Role == PartRole.Gun ? "gun" : "construct")}"
                    : $"Doesn't fit the {target.Name} here";
            if (Pressed(GameAction.Fire) && !_swallowClick)
                BuildSounds.Play(BuildSound.Refuse, default);
        }
    }

    private static long _sbJointGhostKey = -1;
    private static string? _sbJointGhostText;

    /// <summary>The HUD line when the held part or the socket it goes on is a joint's: which side mounts where (board item js01). Made again only when the pairing changes.</summary>
    private static void SandboxJointGhostText(Game g, ConstructPartDef target, int targetSocket, ConstructPartDef held, int ownSocket)
    {
        var key = ((long)target.Id << 40) | ((long)held.Id << 24) | ((long)(targetSocket & 0xff) << 8) | (long)(ownSocket & 0xff);
        if (key != _sbJointGhostKey)
        {
            _sbJointGhostKey = key;
            _sbJointGhostText = JointSides.Ghost(target, targetSocket, held, ownSocket);
        }
        if (_sbJointGhostText != null)
            g.SandboxLookText = _sbJointGhostText;
    }

    /// <summary>
    /// Self-test entry: one frame of the held-part ghost path as the player's loop runs it (the crosshair ray, the hands, the tool
    /// gate, <see cref="SandboxHeldOnConstruct"/>), with a scripted eye, direction and screen. <paramref name="fire"/> is a fresh
    /// click this frame. Returns the HUD text; <paramref name="selected"/> is the chosen ghost (-1: none), <paramref name="valid"/> its validity.
    /// </summary>
    internal static string SandboxHeldFrame(Game g, Vector3 eye, Vector3 dir, Vector2 screen, in Matrix4x4 viewProj, bool fire, double now, out int selected, out bool valid)
    {
        selected = -1;
        valid = false;
        ConstructRender.ClearAim();
        g.SandboxLookText = "";
        if (g.Local is not { Alive: true } me)
            return "no local player";
        ref var h = ref g.SandboxHands.Hand(me.Id);
        var held = ConstructParts.Get(h.Held);
        var wall = World.RaycastWorld(eye, dir, ConstructBuild.Reach + 1);
        var aimed = g.Constructs.Raycast(eye, dir, MathF.Min(wall, ConstructBuild.Reach + 1), out var hit, noTracks: true);
        if (held == null || h.Tool == ConstructTool.None || !aimed)
            return $"nothing held or aimed (held {held?.Name ?? "none"}, tool {h.Tool}, aimed {aimed})";
        if (!ConstructBuild.ToolFits(h.Tool, held))
            return $"Needs the {held.Tool.Label()}";
        ConstructRender.GhostDef = held;
        _down[(int)GameAction.Fire] = fire;
        _prev[(int)GameAction.Fire] = false;
        _swallowClick = false;
        try
        {
            SandboxHeldOnConstruct(g, in h, hit, held, eye, dir, now, screen, in viewProj);
        }
        finally
        {
            _down[(int)GameAction.Fire] = false;
        }
        selected = ConstructRender.GhostSelected;
        valid = ConstructRender.GhostValid;
        return g.SandboxLookText;
    }

    /// <summary>A bind changed: every cached line names the old key, so each is made again.</summary>
    private static void SandboxLookReset()
    {
        _sbLookKey = _sbJoinKey = _sbStoreKey = -1;
        _sbAimRefusedKey = -2;
        _sbGroundKey = -1;
        _sbRackText = "";
        _sbCfgLookKey = _fwKey = _lkHudKey = _lkLookKey = _ptLookKey = _pwKey = _swKey = _hoseLookKey = long.MinValue;
        _sculptHintKey = -1;
        _spLookKey = -1;
        _pgLookKey = -1;
    }

    /// <summary>Which rotate key went down this frame (a roll first: its Shift chord hides the plain arrow anyway), or null.</summary>
    private static TurnKey? SandboxTurnPressed() =>
        Pressed(GameAction.RollLeft) ? TurnKey.RollLeft
        : Pressed(GameAction.RollRight) ? TurnKey.RollRight
        : Pressed(GameAction.RotateLeft) ? TurnKey.Left
        : Pressed(GameAction.RotateRight) ? TurnKey.Right
        : Pressed(GameAction.RotateUp) ? TurnKey.Up
        : Pressed(GameAction.RotateDown) ? TurnKey.Down
        : null;

    /// <summary>The camera's level right and up (world; a planet's up on a curved-gravity map).</summary>
    private static void SandboxCamera(Game g, out Vector3 right, out Vector3 up)
    {
        var fwd = g.LookDir(g.LookYaw, 0);
        right = g.LookDir(g.LookYaw + MathF.PI / 2, 0);
        up = Vector3.Cross(right, fwd);
    }

    /// <summary>The rotate keys' HUD text from the binds (made again only when they change).</summary>
    private static void SandboxTurnKeys(Game g)
    {
        var b = _settings.Binds;
        long key = 17;
        foreach (var a in TurnActions)
            key = key * 31 + Keybinds.First(b, a);
        if (key != _sbTurnBinds || _sbTurnText.Length == 0)
        {
            _sbTurnBinds = key;
            _sbTurnText = TurnKeysText(b);
            _sbGroundKey = -1;
        }
        g.SandboxTurnKeys = _sbTurnText;
    }

    private static readonly GameAction[] TurnActions =
        [GameAction.RotateLeft, GameAction.RotateRight, GameAction.RotateUp, GameAction.RotateDown, GameAction.RollLeft, GameAction.RollRight, GameAction.Reload];

    /// <summary>"&lt;- -&gt; ^ v: turn, Shift + &lt;- -&gt;: roll, R: reset" (the arrows drawn as ASCII arrows).</summary>
    public static string TurnKeysText(int[] b)
    {
        static string N(int code) =>
            Keybinds.IsChord(code) ? "Shift + " + N(code & ~Keybinds.ShiftFlag)
            : code == Keybinds.Key(Brutal.GlfwApi.GlfwKey.Left) ? "<-"
            : code == Keybinds.Key(Brutal.GlfwApi.GlfwKey.Right) ? "->"
            : code == Keybinds.Key(Brutal.GlfwApi.GlfwKey.Up) ? "^"
            : code == Keybinds.Key(Brutal.GlfwApi.GlfwKey.Down) ? "v"
            : Keybinds.Name(code);
        int B(GameAction a) => Keybinds.First(b, a);
        var rl = B(GameAction.RollLeft);
        var rr = B(GameAction.RollRight);
        var roll = Keybinds.IsChord(rl) && Keybinds.IsChord(rr) ? $"Shift + {N(rl & ~Keybinds.ShiftFlag)} {N(rr & ~Keybinds.ShiftFlag)}" : $"{N(rl)} / {N(rr)}";
        return $"{N(B(GameAction.RotateLeft))} {N(B(GameAction.RotateRight))} {N(B(GameAction.RotateUp))} {N(B(GameAction.RotateDown))}: turn, {roll}: roll, {N(B(GameAction.Reload))}: reset";
    }

    private static long _sbStoreKey = -1;
    /// <summary>What a gathering module is doing, or where it should go (ConstructGatherer.cs).</summary>
    internal static string GathererLook(ConstructPartDef d, CStore st, in ConstructHit hit, Game? g = null)
    {
        if (g == null || hit.Body == null)
            return GathererWhy(d, st, hit, null); // (a check with no game: the reason alone)
        // The line state's word first (as its lamp and a console show it), then the reason.
        var s = ConstructGatherer.LineOf(g.Constructs, hit.Body, hit.Part, st);
        var why = GathererWhy(d, st, hit, g).Replace(" - stopped: ", " - ", StringComparison.Ordinal);
        return s == LineState.Running ? why : $" - {ProductionLines.Word(s)}{(why.StartsWith(" - ", StringComparison.Ordinal) ? ": " + why[3..] : why)}";
    }

    /// <summary>A Recycler's look line: its line state, then the reason.</summary>
    private static string RecyclerLook(Game g, ConstructPartDef d, CStore st, in ConstructHit hit) => ConstructRecycler.LineOf(g.Constructs, hit.Body, hit.Part, st) switch
    {
        LineState.Running => ", recycling",
        LineState.Idle => " - Stopped:" + HeldLook[2..],
        LineState.Blocked => " - Blocked: output full, empty it (chute or F)",
        LineState.NoPower => " - No power",
        _ => " - Starved: feed it wrecked parts",
    };

    private static string GathererWhy(ConstructPartDef d, CStore st, in ConstructHit hit, Game? g)
    {
        if (st.Held)
            return HeldLook;
        if (st.Busy)
            return d.GatherSaw ? ", cutting" : ConstructDrillDrive.BusyLook(d, st);
        if (ConstructDrillDrive.StopLook(hit.Body, hit.Part, d, st) is { Length: > 0 } stopped)
            return stopped;
        if ((GatherStall)st.Blocked == GatherStall.BinFull || st.Amount >= d.BulkCapacity)
            return " - bin full: empty it (chute or F)";
        if ((GatherStall)st.Blocked == GatherStall.OtherMaterial)
            return $" - its bin holds {RecyclingTable.Current.NameOf(st.Material)}: empty it";
        if (g != null && hit.Body != null && st.Drive == ShaftState.None && !ConstructRecycler.Powered(g.Constructs, hit.Body, hit.Part))
            return " - needs power (a battery its circuit reaches)";
        if (World.Map.Resources is not { } field)
            return " - bring its blade to a forest";
        if (d.GatherSaw)
            return d.Tier < field.WoodMinTier ? $" - trees need a tier {field.WoodMinTier} saw" : " - bring its blade to a forest";
        ConstructGatherer.WorkBox(hit.Body, hit.Part, out var at, out _, out _);
        var hint = field.NearestUnderground(at, d.Tier);
        if (field.KindAt(at, false) is not { } kind)
            return $" - nothing below; {hint}";
        return d.Tier < kind.MinTier ? $" - {kind.Name} needs a tier {kind.MinTier} drill; {hint}" : $" - over {kind.Name}: bring its bit to the ground";
    }

    internal const string HeldLook = " - Run wire is off";

    private static string _sbStoreText = "";

    /// <summary>
    /// The storage look-at tooltip (the only storage UI): the container's name, what it holds and its room; a stored part's
    /// name and how many (a pile); a bin's level. Rebuilt only when what it shows changes (the store's version, the hands).
    /// </summary>
    /// <summary>How many bodies construct <paramref name="c"/>'s frozen cargo volumes carry (as this machine has them).</summary>
    private static int CarriedBy(ConstructWorld w, Construct c)
    {
        var n = 0;
        foreach (var b in w.Bodies)
            if (b.CarriedBy == c.Id)
                n++;
        return n;
    }

    private static long _sbRouterKey = -1;
    private static string _sbRouterText = "";
    private static long _sbLifterKey = -1;
    private static string _sbLifterText = "";

    private static string StorageLook(Game g, Player me, in ConstructHit hit, CStore st, bool near, ConstructPartDef? held)
    {
        var inv = g.SandboxInventory(me.Id);
        var carried = inv.TotalMaterials;
        var hands = g.SandboxHands.Hand(me.Id).Tool == ConstructTool.Hands; // (hands out: the click takes a stored part, as F does)
        var key = ((long)hit.Body.Id << 40) ^ ((long)hit.Part << 32) ^ ((long)hit.SlotPlus1 << 26) ^ ((long)st.Version << 6) ^ ((long)(held?.Id ?? 255) << 52) ^ (near ? 1L << 62 : 0) ^ ((long)Math.Min(carried, 1 << 20) << 8) ^ (st.Busy ? 1L << 61 : 0) ^ ((long)hit.Body.RouteCount << 48) ^ (hands ? 1L << 60 : 0);
        if (hit.Body.DefOf(hit.Part).IsVolumeStore)
            key ^= (long)ConstructStoreVolume.Capacity(hit.Body, hit.Part) * 2654435761L ^ (long)hit.Body.Parts[hit.Part].SealedFaces << 8; // (its capacity moves with the construct's bounds, not the store)
        if (hit.Body.DefOf(hit.Part) is var ad && (ad.IsAssembler || ad.IsRefiner))
            key ^= (long)ConstructConfig.Get(hit.Body.Parts[hit.Part], ad, CfgKey.Recipe, 0) * 2654435761L; // (its recipe is a setting, not the store; a refiner's set recipe too)
        if (hit.Body.DefOf(hit.Part).IsPress)
            key ^= (long)ConstructPress.LookKey(hit.Body, hit.Part) * 2654435761L; // (what is in its bays and die slot: parts, not the store)
        if (hit.Body.DefOf(hit.Part).IsPieceRack)
            key ^= ((long)hit.RackPlus1 << 8 | hit.RackLane) * 2654435761L; // (which stack is looked at: its kind names the piece F takes)
        if (StockTargets.IsStore(hit.Body.DefOf(hit.Part)))
            key ^= StockTargets.LookKey(hit.Body, hit.Part) * 0x9E3779B97F4AL; // (its Logistics settings: what it keeps and takes)
        if (hit.Body.DefOf(hit.Part).IsCraftTerminal)
            key ^= (long)((hit.Body.Parts[hit.Part].Craft?.Count ?? 0) + 1 + hit.Body.RouteCount * 64) * 40503L; // (its orders and the body's links: what its line says)
        if (hit.Body.DefOf(hit.Part).IsBuildStation)
            key ^= RouteRender.BayLook(hit.Body, hit.Part) * 2654435761L; // (a Building Station: its bay is outlined while it is looked at, and its line follows its job)
        if (hit.Body.DefOf(hit.Part).IsExtruder)
            key ^= (long)(ConstructExtruder.SizeKey(hit.Body, hit.Part) + 1) * 2654435761L; // (the piece's length, which the store doesn't change with)
        if (hit.Body.DefOf(hit.Part) is { IsRecycler: true } or { IsGatherer: true })
            key ^= (long)(ProductionLines.Raw(g.Constructs, hit.Body, hit.Part, st) ?? 0) * 0x9E3779B97F4AL; // (its line state: the mains or a shaft change it, not the store)
        if (key == _sbStoreKey)
            return _sbStoreText;
        _sbStoreKey = key;
        var cd = hit.Body.DefOf(hit.Part);
        if (cd.IsBeacon)
            return _sbStoreText = ConstructBeacons.Look(cd, st, hit.Body.Parts[hit.Part].Broken);
        if (cd.IsTerminal && hit.SlotPlus1 == 0)
            return _sbStoreText = ConstructTerminals.Look(cd, st, hit.Body.Parts[hit.Part].Broken);
        if (cd.IsVolumeStore)
            return _sbStoreText = ConstructStoreVolume.Look(hit.Body, hit.Part, st, near, inv, held);
        if (cd.IsPanelDesigner && hit.RackPlus1 == 0)
            return _sbStoreText = SandboxKeys.Line(cd.Name, $"{hit.Body.Parts[hit.Part].Craft?.Count ?? 0} order(s), {st.ItemCount} in the tray", null,
                SandboxKeys.Do(SandboxKeys.Use, st.ItemCount > 0 ? "on the tray: take a panel; elsewhere: design a panel" : "design a panel")); // (the desk is a terminal, but F opens the designer)
        if (cd.IsCraftTerminal && hit.RackPlus1 == 0 && _game is { } cg)
            return _sbStoreText = ConstructCrafting.Look(cg.Constructs, hit.Body, hit.Part, st, near);
        if (cd.IsPieceRack)
            return _sbStoreText = ConstructConveyor.Look(cd, st, near, hit.RackPlus1 > 0 && hit.RackPlus1 <= st.SlabN ? ConstructParts.Get(st.Slabs![hit.RackPlus1 - 1].Def) : null) is var rl && StockTargets.Look(hit.Body, hit.Part) is { Length: > 0 } kl ? rl + " - " + kl : rl;
        if (cd.IsRefiner)
            return _sbStoreText = ConstructRefining.Look(cd, st, near, inv, ConstructRefining.SetOf(hit.Body.Parts[hit.Part], cd));
        if (cd.IsAssembler && hit.SlotPlus1 == 0)
            return _sbStoreText = ConstructAssembling.Look(cd, st, ConstructAssembling.RecipeOf(AssemblingTable.Current, hit.Body.Parts[hit.Part], cd), near, inv, held, hit.Body.Parts[hit.Part].Orders is { Bill: false });
        if (cd.IsPress && hit.SlotPlus1 == 0)
            return _sbStoreText = ConstructPress.Look(hit.Body, hit.Part, st, near, held, Gfx.CameraPosition);
        if (cd.IsExtruder)
            return _sbStoreText = ConstructExtruder.Look(hit.Body, hit.Part, st, near, inv);
        if (cd.IsScoop)
        {
            var tip = ConstructEdits.TipMaterial(inv, cd, st);
            var action = !near ? null : tip != 0 ? SandboxKeys.Do(SandboxKeys.Use, $"tip in {Math.Min(inv.Materials[tip], cd.BulkCapacity - st.Amount)} {RecyclingTable.Current.NameOf(tip)}")
                : st.Amount > 0 ? SandboxKeys.Do(SandboxKeys.Use, $"take {Math.Min(ConstructStorage.TakeLoad, st.Amount)} {RecyclingTable.Current.NameOf(st.Material)}") : null;
            return _sbStoreText = ConstructScoops.Look(cd, st, action);
        }
        if (cd.IsBin)
        {
            var tbl = RecyclingTable.Current;
            var tip = ConstructEdits.TipMaterial(inv, cd, st);
            var act = !near ? "" : tip != 0 ? $" - {SandboxKeys.Use}: tip in {Math.Min(inv.Materials[tip], cd.BulkCapacity - st.Amount)} {tbl.NameOf(tip)}" : st.Amount > 0 ? $" - {SandboxKeys.Use}: take {Math.Min(ConstructStorage.TakeLoad, st.Amount)} {tbl.NameOf(st.Material)}" : "";
            var holds = st.Amount > 0 ? " " + tbl.NameOf(st.Material) : cd.IsRecycler || cd.IsGatherer ? "" : cd.BulkMaterial == ConstructStorage.Any ? ", any one material" : " " + cd.BulkMaterial;
            var rec = cd.IsRecycler ? RecyclerLook(g, cd, st, hit)
                : cd.IsGatherer ? GathererLook(cd, st, hit, g) : "";
            return _sbStoreText = $"{cd.Name}: {st.Amount}/{cd.BulkCapacity}{holds}{rec}{act}";
        }
        if (hit.SlotPlus1 > 0 && hit.SlotPlus1 - 1 < st.SlotCount && ConstructParts.Get(st.Def[hit.SlotPlus1 - 1]) is { } d)
        {
            var k = hit.SlotPlus1 - 1;
            var slot = cd.Slots[k];
            var most = ConstructStorage.StackOf(slot, d, st.Rot[k]);
            var pile = most > 1 ? $"{st.Count[k]}/{most}" : "";
            var hurt = st.Health[k] < 0.995f ? $"{st.Health[k] * 100:0}% health" : "";
            var take = !near ? "" : held != null ? "hands full" : "";
            return _sbStoreText = SandboxKeys.Line(PartRef.Shown(d), SandboxKeys.Join(pile, hurt), take, !near || held != null ? null : SandboxKeys.Do(hands ? SandboxKeys.Fire : SandboxKeys.Use, most > 1 ? "take one" : "take"));
        }
        var used = 0;
        for (var k = 0; k < st.SlotCount; k++)
            used += st.Def[k] == ConstructParts.None ? 0 : 1;
        var what = used == 0 ? $"empty, {SlotSizes(cd)}" : $"{used}/{st.SlotCount} slots";
        if (held == null || !near)
            return _sbStoreText = SandboxKeys.Line(cd.Name, what);
        return _sbStoreText = ConstructRender.SlotMask != 0 ? SandboxKeys.Line(cd.Name, what, null, $"{SandboxKeys.Fire} a lit slot: set it in") : SandboxKeys.Line(cd.Name, what, "no free slot takes it");
    }

    /// <summary>"2 medium slots", "3 wheel slots, ..." (the container's room, by size class).</summary>
    private static string SlotSizes(ConstructPartDef cd)
    {
        var parts = new List<string>(4);
        foreach (var label in cd.Slots.Select(x => ConstructStorage.SizeClass(x)).Distinct())
        {
            var n = cd.Slots.Count(x => ConstructStorage.SizeClass(x) == label);
            parts.Add($"{n} {label} slot{(n == 1 ? "" : "s")}");
        }
        return string.Join(", ", parts);
    }

    /// <summary>The slots of the storage part under the crosshair that take the held part (ConstructRender draws them); the chosen one is the free fitting slot nearest the crosshair. False when the part has none at all.</summary>
    private static bool StorageSlots(Game g, in ConstructHit hit, CStore st, ConstructPartDef held, Vector3 eye, Vector3 dir)
    {
        var c = hit.Body;
        var cd = c.DefOf(hit.Part);
        if (cd.IsVolumeStore)
        {
            // (no slots: the box itself; a click puts the part in, and the look line says why when it won't go)
            if (ConstructStoreVolume.Check(c, hit.Part, st, held) != SlotCheck.Ok)
                return false;
            ConstructRender.SlotSelected = 0;
            return true;
        }
        if (cd.Slots.Length == 0)
            return false;
        ref var cp = ref c.Parts[hit.Part];
        c.WorldFrame(cp.Seg, out var q, out var t);
        var mask = 0;
        var best = -1;
        var bestD = float.MaxValue;
        byte bestRot = 0;
        for (var k = 0; k < cd.Slots.Length && k < st.SlotCount; k++)
        {
            if (ConstructStorage.Check(cd, st, k, held, out var rot) != SlotCheck.Ok)
                continue;
            mask |= 1 << k;
            ConstructStorage.SlotBox(cp, cd.Slots[k], out var sc, out _);
            var world = t + Vector3.Transform(sc, q);
            var along = Vector3.Dot(world - eye, dir);
            var off = Vector3.Distance(world, eye + dir * along);
            if (hit.SlotPlus1 - 1 == k)
                off = -1; // (aiming at the pile itself)
            if (off < bestD)
            {
                bestD = off;
                best = k;
                bestRot = rot;
            }
        }
        ConstructRender.SlotBody = c;
        ConstructRender.SlotPart = hit.Part;
        ConstructRender.SlotMask = mask;
        ConstructRender.SlotSelected = best;
        ConstructRender.SlotRot = bestRot;
        ConstructRender.SlotCopy = best >= 0 && cd.Slots[best].Rack && st.Def[best] == held.Id ? st.Count[best] : 0;
        ConstructRender.SlotDef = held;
        _ = g;
        return true;
    }

    /// <summary>A gun under the crosshair: rounds loaded and in its boxes, reloading, or why it won't fire (the look text's cache rebuilds it only on a change).</summary>
    /// <summary>A seat on the look line: free or taken, why it controls nothing, what Use does.</summary>
    private static string SeatLook(Construct c, int part, ConstructPartDef d, bool near)
    {
        var taken = c.Parts[part].SitterPlus1 != 0 || c.Driver != 255 && part == c.DriveSeat;
        var broken = c.Parts[part].Broken;
        var kind = c.SeatKindOf(part);
        return SandboxKeys.Line(d.Name, broken ? "wrecked" : taken ? "taken" : kind == SeatKind.Driver ? "driver's seat" : ConstructModules.HasTurret(c, part) ? "gunner's seat" : "free",
            broken ? null : ConstructModules.Problem(c, part) ?? ConstructRemote.SeatProblem(_game.Constructs, c, part), near && !taken && !broken ? SandboxKeys.Do(SandboxKeys.Use, kind == SeatKind.Driver ? "drive" : kind == SeatKind.Gunner ? "man the gun" : "sit") : null);
    }

    private static string GunLook(Construct c, int k, in CPart hitPart)
    {
        ref var g = ref c.Guns[k];
        ref readonly var p = ref g.Part < c.PartCount ? ref c.Parts[g.Part] : ref hitPart; // (a merged receiver's member: its lead's gun)
        var name = p.D.Name;
        var hurt = "";
        if (g.MergeN > 1)
        {
            ConstructMerge.Health(c, g.Part, out var hp, out var most);
            name = $"{name} {g.MergeN}x{g.MergeN}";
            hurt = hp < most - 0.5f ? $"{hp:0}/{most:0} hp" : "";
        }
        return p.Broken ? SandboxKeys.Line(name, "wrecked")
            : g.Problem.Length > 0 ? SandboxKeys.Line(name, hurt, $"won't fire: {g.Problem}")
            : g.BoxCount == 0 && g.FeedRoute < 0 && g.FeedSocket < 0 && p.Rounds == 0 && ConstructWeapons.BoxRounds(c, k) == 0 ? SandboxKeys.Line(name, hurt, g.MagPart >= 0 ? "magazine empty: take it off and fill it at an ammo box" : "no ammo box or feed")
            : SandboxKeys.Line(name, SandboxKeys.Join($"{p.Rounds}/{g.Mag}{(p.Reload > 0 ? " reloading" : "")}", $"{ConstructAmmo.Types[ConstructAmmo.TypeOfGun(_game?.Constructs, c, k)]}, {ConstructWeapons.BoxRounds(c, k, _game?.Constructs)} to come", ConstructConfig.Has(c.DefOf(g.Part), CfgKey.Charge) && g.RoundV > 0 ? PowderCharge.Line(g.RoundV, g.Def!.ChargeV) : "", hurt), g.Seat < 0 && g.Sensor == CGun.NoSensor && p.InCount == 0 ? "wire a signal in to fire" : null);
    }

    /// <summary>The constructs, ghosts and tool (called from the world pass; nothing outside Sandbox).</summary>
    private static void DrawSandbox(double now)
    {
        if (_game is not { } g || (g.Constructs.Bodies.Count == 0 && !g.SandboxOn))
            return;
        g.SandboxNow = g.IsServer ? g.SandboxNow : now;
        // (a client's constructs were blended to this frame's pose in Game.UpdateSandboxSeats, before the camera was set)
        ConstructRender.Now = now;
        ConstructRender.Draw(Gfx, g.Constructs, g.TickAlpha, blendTicks: g.IsServer);
        DrawPhysGunBeams(g, now); // the physics guns' beams (Program.PhysicsGun.cs)
        g.SandboxSounds(now); // horns (GameSandbox.cs)
        g.ColonyAlarmSounds(now); // the penal colony's lockdown siren (GameColonyLockdown.cs)
        g.StoryBabbleSounds(now); // the story people murmur their lines (GameStoryBabble.cs)
        PartAmbience.Frame(g.Constructs, Gfx.CameraPosition, now, g.TickAlpha, g.IsServer, g.Audio); // smoke, steam, a tannoy, snow along the ground (PartAmbience.cs)
        g.EngineAudio.Pick(g.Constructs, Gfx.CameraPosition);
        g.EngineAudio.Play(g.Audio); // (modular engines: EngineSounds.cs)
        g.SandboxBuildSounds(now); // (building: GameSandboxBuildSounds.cs)
    }

    private static ToolFrame _sbTool;
    private static ConstructTool _sbToolShown;

    /// <summary>
    /// The tool in your hands, instead of the gun (Sandbox only; Source/Render/ToolRender.cs): its model in camera space
    /// with the view model's arms holding it, a carried part in the off hand, the equip drop between tools. Returns false
    /// when the gun is drawn instead; when a tool has just gone down to the gun, the gun comes up with its equip.
    /// </summary>
    private static bool DrawSandboxTool(double now, Vector3 eye, Vector3 fwd)
    {
        if (_game is not { SandboxOn: true } g || g.Local is not { Alive: true } me)
            return false;
        ref var h = ref g.SandboxHands.Hand(me.Id);
        if (h.Seated)
            return true; // hands on the wheel: no gun
        var input = new ToolRender.ViewInput
        {
            Tool = h.Tool, Held = ConstructParts.Get(h.Held), Now = now, Velocity = me.Sim.Velocity, Grounded = me.Sim.Grounded,
            Using = h.Edit != EditKind.None || (h.Tool == ConstructTool.Linker && _lkBody != 0) || (h.Tool == ConstructTool.PhysicsGun && h.GrabBody != 0) || (h.Tool == ConstructTool.Configure && Ui.SandboxConfigOpen) || (h.Tool == ConstructTool.GroundDrill && _sculpt.Busy), // (the linking tool: while a link is being routed; the physics gun: while it holds)
        };
        var drawn = ToolRender.FirstPerson(input, ref _sbTool, out var shown);
        if (shown == ConstructTool.None && _sbToolShown != ConstructTool.None && h.Tool == ConstructTool.None)
            _game.EquipUntil = Math.Max(_game.EquipUntil, now + _game.Active.Def.EquipTime); // the tool is down: the gun comes up
        _sbToolShown = shown;
        if (!drawn)
            return false;
        Gfx.ClearDepthBuffer();
        LightGrid.BeginViewModel(World.Map, _worldEye, _worldView, now); // (camera space: lit by one sample at the eye, like the gun)
        var fb = _window.FramebufferSize;
        Gfx.SetCamera(Matrix4x4.Identity, Sights.ViewModelProjection(fb.X / (float)Math.Max(fb.Y, 1)));
        var o = new ToolGfxOut(Gfx);
        Gfx.BeginBatch();
        ToolRender.Emit(ref o, _sbTool);
        _vmAnim.Right.Solve(_sbTool.Right, Matrix4x4.Identity, Vector3.Zero);
        _vmAnim.Left.Solve(_sbTool.Left, Matrix4x4.Identity, Vector3.Zero);
        var armColors = ArmColors.For(me.Color * 0.5f + new Vector3(0.06f));
        _vmAnim.Right.Draw(Gfx, armColors);
        _vmAnim.Left.Draw(Gfx, armColors);
        Gfx.EndBatch();
        if (Matrix4x4.Invert(_worldView, out var camToWorld))
            ToolRender.NoteViewTip(_sbTool, camToWorld); // (the welder's light, next frame)
        DrawToolCard(g, me, in h, shown, now);
        return true;
    }

    private static readonly PanelCard _toolCard = new();

    /// <summary>What the tool's mode is, as its ring says it ("" when it has none).</summary>
    private static string ToolModeText(ConstructTool tool) => tool switch
    {
        ConstructTool.Linker => _linkWheel.ModeName,
        ConstructTool.Nudge => (_nudgeModes.Rotating ? "rotate, " : "move, ") + (_nudgeModes.Local ? "local" : "world"),
        ConstructTool.Paint => _paintWheel.Mode switch { PaintMode.Whole => "whole construct", PaintMode.Reset => "reset colour", _ => "one part" },
        ConstructTool.Texture => _textureWheel.ModeText,
        ConstructTool.Material => _materialWheel.ModeText,
        ConstructTool.Welder => _weldModes.Mode switch { SandboxWeldModes.Option.Freeform => "freeform", SandboxWeldModes.Option.Rope => "rope", SandboxWeldModes.Option.Spring => "spring", _ => "socket" },
        _ => "",
    };

    /// <summary>
    /// The card on the screen of the tool in hand, in the view model's pass: camera space, the arms' depth, one draw. Only a tool with a screen, and only
    /// once it has it in the picture: from then the HUD leaves these lines to it (Game.SandboxToolCardShown).
    /// </summary>
    private static void DrawToolCard(Game g, Player me, in SandboxHand h, ConstructTool shown, double now)
    {
        if (ToolRender.ModelFor(h.Tool) is not { Display: { } ds } || h.Tool is ConstructTool.None or ConstructTool.Hands)
            return;
        g.SandboxToolCardFrame = CrashLog.Frame;
        if (shown != h.Tool || _sbTool.Model?.Display is not { } on)
            return; // (the last tool is still going down)
        g.SandboxFillToolCard(me, in h, _toolCard, ToolModeText(h.Tool), PanelRender.Columns(ds.Size, ds.Rows), ds.Rows, now);
        var pose = _sbTool.Pose;
        PanelRender.Begin();
        PanelRender.DrawCard((long)h.Tool, _toolCard, on.Size, on.Rows, Vector3.Transform(on.At, pose), Vector3.TransformNormal(on.Normal, pose), Vector3.TransformNormal(on.Up, pose));
        PanelRender.Flush(Gfx);
    }
}
