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
    private static string _sbJoinText = "";

    /// <summary>Sandbox with a tool out (or in a seat): the trigger and aim belong to the tools, not the gun.</summary>
    private static bool SandboxHoldsTrigger =>
        _game is { SandboxOn: true } g && g.Local is { Alive: true } me && (g.SpawnPlacing || g.SandboxHands.Hand(me.Id).Tool != ConstructTool.None || g.SandboxHands.Hand(me.Id).Seated
            || g.SandboxHands.Hand(me.Id).Held != ConstructParts.None); // (a part carried in both hands: the click sets it into storage, never fires the gun)

    /// <summary>
    /// The Sandbox inventory key (Tab by default; GameAction.SandboxInventory, live only in Sandbox, where there is no
    /// scoreboard) opens the inventory (UiSandboxParts.cs), the mouse free for it;
    /// again (or Esc) closes it (while its search box has the keyboard the game's keys are off: Esc then). Not with the chat or another menu open, in a seat, or dead; refused
    /// with a notice where the map turns the menu off.
    /// </summary>
    private static void SandboxMenuInput()
    {
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
        if (!g.SandboxPartsMenu)
        {
            g.SandboxSay("the parts menu is off on this map");
            return;
        }
        if (g.SandboxHands.Hand(me.Id).Seated)
        {
            g.SandboxSay("get out of the seat first");
            return;
        }
        Ui.OpenSandboxParts();
        SetMouseCaptured(false);
    }

    /// <summary>The Sandbox parts menu over the match; it closes (and the mouse goes back to aiming) when you die, sit, the match ends or the mode changes.</summary>
    private static void DrawSandboxPartsInMatch()
    {
        if (!Ui.SandboxPartsOpen)
            return;
        var gone = _session == null || !_game.SandboxPartsMenu || _game.Local is not { Alive: true } me || _game.Phase != MatchPhase.Playing || _game.SandboxHands.Hand(me.Id).Seated;
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
            return false;
        g.SandboxLookText = "";
        SandboxSnapFrame(g, now); // (snappy building: markers off, the ease running out, the thunk; Program.SandboxSnap.cs)
        if (g.Local is not { Alive: true } me || !_mouseCaptured || MenusOpen)
        {
            _sbFiring = _sbRemoving = false;
            return true;
        }
        ref var h = ref g.SandboxHands.Hand(me.Id);
        if (Pressed(GameAction.Weapon3))
            g.SandboxSend(CActKind.Tool, part: (byte)(h.Tool == ConstructTool.Wrench ? ConstructTool.None : ConstructTool.Wrench));
        // (4 and 5 are the kill-streak keys by default, and Sandbox has no streaks: the welder and the linking tool take them)
        if (Pressed(GameAction.Weapon4) || Pressed(GameAction.Streak1))
            g.SandboxSend(CActKind.Tool, part: (byte)(h.Tool == ConstructTool.Welder ? ConstructTool.None : ConstructTool.Welder));
        if (Pressed(GameAction.Streak2))
            g.SandboxSend(CActKind.Tool, part: (byte)(h.Tool == ConstructTool.Linker ? ConstructTool.None : ConstructTool.Linker));
        if ((Pressed(GameAction.PrimaryWeapon) || Pressed(GameAction.SecondaryWeapon)) && h.Tool != ConstructTool.None)
            g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
        if (h.Tool != ConstructTool.Linker)
        {
            g.SandboxWireFrom = "";
            if (_lkBody != 0 || _lkMoveRoute >= 0 || _lkPicker.IsOpen)
                LinkReset(); // (the linking tool put away: the link being routed goes)
        }
        ConstructRender.ShowWires = h.Tool == ConstructTool.Linker;
        ConstructRender.VolumesShown = h.Tool != ConstructTool.None; // (cargo volumes: outlined in build mode)
        if (h.Tool != ConstructTool.Configure && (_cvBody != 0 || _cvArmed))
            CargoReset();
        if (h.Tool != ConstructTool.Configure)
            ConstructRender.VolumeHotBody = null;
        if (h.Seated)
        {
            if (Pressed(GameAction.Interact))
                g.SandboxSend(CActKind.Stand);
            // The keys a seat's input readers can read besides the movement keys: fire, aim, reload.
            var keys = (Down(GameAction.Fire) ? 1 : 0) | (Down(GameAction.Aim) ? 2 : 0) | (Down(GameAction.Reload) ? 4 : 0);
            if (keys != _sbSeatKeys)
            {
                _sbSeatKeys = keys;
                g.SandboxSend(CActKind.SeatKeys, part: (byte)keys);
            }
            return true;
        }
        if (_sbSeatKeys > 0)
        {
            _sbSeatKeys = 0;
            g.SandboxSend(CActKind.SeatKeys, part: 0);
        }
        if (SandboxSpawnInput(g, me))
            return true; // placing a spawn: Fire spawns, Aim stops (Program.SandboxSpawn.cs)
        if (Pressed(GameAction.DropItem) && (h.Held != ConstructParts.None || g.Constructs.HoseHeldBy(me.Id) >= 0))
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
            _sbRackText = $"Parts rack: F to take a part (again for the next kind; {g.SandboxMenuKey}: the inventory)";
        }
        if (Pressed(GameAction.Stow))
            g.SandboxSend(CActKind.Stow);
        if (Pressed(GameAction.DrawPack))
            g.SandboxSend(CActKind.Draw);

        // What the crosshair is on.
        var eye = me.RenderEye;
        var dir = g.LookDir(g.LookYaw, g.LookPitch);
        var wall = World.RaycastWorld(eye, dir, ConstructBuild.Reach + 1);
        var aimed = g.Constructs.Raycast(eye, dir, MathF.Min(wall, ConstructBuild.Reach + 1), out var hit);
        SandboxSnapGlobal(g, aimed, hit); // (quick building on or off, Ctrl+Z, the pick tap)
        if (aimed)
        {
            ConstructRender.AimBody = hit.Body;
            ConstructRender.AimPart = hit.Part;
            var d = hit.Body.DefOf(hit.Part);
            ref var part = ref hit.Body.Parts[hit.Part];
            // (rebuilt only when what it shows changes: the look runs every frame)
            var near = hit.Distance <= ConstructBuild.UseReach;
            var fits = h.Tool != ConstructTool.None && ConstructBuild.ToolFits(h.Tool, d);
            // Salvage (milestone 3): anything but your own construct goes into the inventory; someone else's working one can't be taken apart.
            var salvage = hit.Body.Owner != me.Id && ConstructEdits.Salvageable(hit.Body, me.Id);
            var theirs = ConstructEdits.SalvageRefusal(hit.Body, me.Id) != null;
            var key = ((long)hit.Body.Id << 40) | ((long)hit.Part << 32) | ((long)(part.Broken ? 1 : 0) << 31) | ((long)(near ? 1 : 0) << 30) | ((long)(fits ? 1 : 0) << 29)
                      | ((long)(hit.Body.Driver == 255 ? 1 : 0) << 28) | ((long)(hit.Body.Loose ? 1 : 0) << 27) | (long)Math.Clamp((int)part.Health, 0, 1 << 26)
                      ^ ((long)(byte)ConstructLogic.Quantise(part.Out) << 17) ^ ((long)part.Setting << 12) ^ ((long)part.SitterPlus1 << 4) ^ (h.Tool == ConstructTool.Linker ? 1L << 62 : 0)
                      ^ ((long)g.Constructs.CouplingCount << 50) ^ ((long)(h.Tool == ConstructTool.Wrench ? 1 : 0) << 61) ^ ((long)(int)(ConstructPower.Share(hit.Body) * 100) << 53) ^ (hit.Body.PowerOn ? 1L << 60 : 0)
                      ^ (salvage ? 1L << 56 : 0) ^ (theirs ? 1L << 57 : 0) ^ (d.Configurable ? (long)BitConverter.SingleToInt32Bits(ConstructConfig.At(part, d, 0)) << 20 : 0)
                      ^ ((long)part.Rounds << 33) ^ (part.Burn > 0 ? 1L << 55 : 0) ^ (part.Reload > 0 ? 1L << 54 : 0)
                      ^ (d.Role is PartRole.Engine or PartRole.Transmission ? (long)(hit.Body.Rpm / 50) << 8 ^ (long)hit.Body.Gear << 3 : 0) // (an engine's rpm, a gearbox's gear)
                      ^ (ConstructCargo.IsVolume(d) ? (long)CarriedBy(g.Constructs, hit.Body) << 44 : 0) ^ (hit.Body.CarriedBy != 0 ? 1L << 63 : 0); // (a cargo volume's load; carried)
            if (key != _sbLookKey)
            {
                _sbLookKey = key;
                var setting = d.Configurable ? $" ({d.Config[0].Label.ToLowerInvariant()} {d.Config[0].Show(ConstructConfig.At(part, d, 0))})"
                    : d.Settings.Length > 0 ? $" ({d.SettingName} {d.SettingLabels[Math.Min(part.Setting, (byte)(d.SettingLabels.Length - 1))]})" : "";
                var signal = d.IsSignal ? $" [{(d.Logic == CLogic.Speed ? part.Out.ToString("0.0") : d.Logic is CLogic.FuelGauge or CLogic.ChargeGauge ? $"{part.Out * 100:0}%" : d.Logic == CLogic.Selector ? part.Out.ToString("0") : part.Out >= 0.5f ? "on" : part.Out <= -0.5f ? "back" : "off")}]" : "";
                var coupled = d.Role == PartRole.Hitch && g.Constructs.CouplingOf(hit.Body, hit.Part) >= 0;
                var charge = (int)MathF.Round(ConstructPower.Share(hit.Body) * 100);
                var unpowered = hit.Body.Electrical && d.Electric && !ConstructPower.On(hit.Body, hit.Part) ? " (no power)" : "";
                _sbLookText = d.Role switch
                {
                    _ when h.Tool == ConstructTool.Linker => $"{d.Name}{setting}{signal}{unpowered}{(d.HasOutput ? " - output" : "")}{(d.MaxInputs > 0 ? $" - takes {d.MaxInputs} input{(d.MaxInputs == 1 ? "" : "s")}" : "")}",
                    PartRole.Hitch when near => $"{d.Name}: {(coupled ? "coupled" : "free")}{(h.Tool == ConstructTool.Wrench ? coupled ? " - F to uncouple" : " - F to couple" : " - take out the wrench (3) to couple")}",
                    PartRole.Battery => $"{d.Name} {(part.Broken ? "(wrecked)" : $"{charge}%{(hit.Body.PowerOn ? "" : ", flat")}")}",
                    PartRole.Transmission when d.HasGears => $"{d.Name}: gear {d.SettingLabels[Math.Min(hit.Body.Gear, d.SettingLabels.Length - 1)]} ({(d.Trans == TransKind.Cvt ? $"{d.CvtLow:0.#} to {d.CvtHigh:0.#}" : string.Join(" ", d.Settings.Select(r => r.ToString("0.#"))))}:1, {d.Trans.ToString().ToLowerInvariant()}){(part.Broken ? " (wrecked)" : "")}",
                    PartRole.Engine when h.Tool == ConstructTool.None && !hit.Body.Loose => $"{d.Name}{(part.Broken ? " (wrecked)" : hit.Part == hit.Body.Engine && hit.Body.EngineRunning ? $": {hit.Body.Rpm:0} rpm, {d.EngineTorque * ConstructDrive.Curve(d, hit.Body.Rpm):0} N m" : ": off")} - {ConstructDrive.EngineLine(d)}",
                    PartRole.Differential when h.Tool == ConstructTool.None && !hit.Body.Loose => $"{d.Name}: {(d.Diff == DiffKind.Lockable ? (part.Setting == 1 ? "locked" : "open") : d.Diff.ToString().ToLowerInvariant())}, {d.DiffRatio:0.#}:1{(part.Broken ? " (wrecked)" : "")}",
                    PartRole.Transfer when h.Tool == ConstructTool.None && !hit.Body.Loose => $"{d.Name}: {ConstructDrive.TransferLine(d, part)}{(part.Broken ? " (wrecked)" : "")}",
                    PartRole.Wheel when h.Tool == ConstructTool.None && !hit.Body.Loose => $"{d.Name}: {d.WheelRadius * 2:0.0#} m, grip {d.Grip:0.0#} (dirt {d.Grip * d.OffroadGrip:0.0#}){(part.Broken ? d.FlatTyre ? " - FLAT" : " (wrecked)" : $" {part.Health:0}/{d.Health:0}")}",
                    PartRole.Suspension when h.Tool == ConstructTool.None && !hit.Body.Loose => $"{d.Name}: {d.Travel:0.0#} m travel{(d.SuspFrequency.Length > 0 ? $", {d.SettingLabels[Math.Min(part.Setting, (byte)(d.SettingLabels.Length - 1))]}" : d.ActiveSuspension ? $", height {(part.InCount > 0 ? part.In : 0.5f) * 100:0}%" : "")}{(part.Broken ? " (wrecked)" : $" {part.Health:0}/{d.Health:0}")}",
                    PartRole.Seat when near => part.SitterPlus1 != 0 || hit.Body.Driver != 255 && hit.Part == hit.Body.DriveSeat ? "Seat: taken" : hit.Part == hit.Body.DriveSeat ? "Seat: F to drive" : "Seat: F to ride",
                    PartRole.Logic when d.Logic == CLogic.Cargo => $"{d.Name}: {(ConstructCargo.IsFrozen(part) ? $"FROZEN, carrying {CarriedBy(g.Constructs, hit.Body)}" : "free")}{(near ? ConstructCargo.IsFrozen(part) ? " - F to let go" : " - F to freeze what is inside" : "")}{(h.Tool == ConstructTool.Configure ? " (configure tool: drag a face handle to resize)" : "")}",
                    PartRole.Logic when near && d.Logic is CLogic.Button or CLogic.Switch or CLogic.Lever or CLogic.Selector => $"{d.Name}{signal}{unpowered}: F to {(d.Logic == CLogic.Button ? "press" : d.Logic == CLogic.Selector ? "step it on" : "flip")}",
                    PartRole.Door or PartRole.Hinge or PartRole.Joint when near => $"{d.Name}: F to open or shut",
                    PartRole.Logic => $"{d.Name}{setting}{signal}{unpowered}",
                    PartRole.Gun when hit.Body.GunOf(hit.Part) is var gk and >= 0 => GunLook(hit.Body, gk, part),
                    PartRole.AmmoBox => $"{d.Name}: {part.Rounds} rounds{(part.Burn > 0 ? " - ON FIRE" : "")}{(part.Broken ? " (wrecked)" : "")}",
                    _ when part.Burn > 0 => $"{d.Name} - ON FIRE {part.Health:0}/{d.Health:0}",
                    _ when hit.Body.Loose && d.PhysicsOnly => $"{d.Name} - physics only ({d.PhysicsWhy}): move it with the physics gun{(hit.Body.CarriedBy != 0 ? " (carried in a cargo volume)" : "")}",
                    _ when hit.Body.Loose && near => $"{d.Name} (item): F to pick up{(hit.Body.CarriedBy != 0 ? " (carried in a cargo volume)" : "")}",
                    _ => $"{d.Name}{(d.PhysicsOnly ? " [physics only]" : "")} {(part.Broken ? "(wrecked)" : $"{part.Health:0}/{d.Health:0}")}{(!fits ? "" : theirs ? " - someone else's: disable it to salvage" : salvage ? (part.Broken ? $" - hold aim to salvage it for {d.Scrap} scrap" : " - hold aim to salvage it") : " - hold aim to take it off")}",
                };
            }
            g.SandboxLookText = _sbLookText;
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
        var feet = me.Position;
        var nearRack = World.Map.Sandbox is { } sb && Vector3.Distance(feet with { Y = 0 }, sb.Rack) <= 4.5f;
        if (nearRack && !aimed)
            g.SandboxLookText = _sbRackText;
        // The welder on a part that touches another construct: F joins the two into one.
        JoinPlan jplan = default;
        // (decision 21: the wrench bolts on a loose wrench part the same way: a physics-only engine moved into place)
        var join = h.Tool is ConstructTool.Welder or ConstructTool.Wrench && aimed && hit.Distance <= ConstructBuild.Reach && ConstructPlace.FindJoin(g.Constructs, hit.Body, hit.Part, out jplan)
                   && ConstructEdits.JoinTool(h.Tool, hit.Body, jplan.Other!);
        if (join)
        {
            var jk = hit.Body.Id | ((long)jplan.Other!.Id << 16) | ((long)hit.Part << 32) | ((long)h.Tool << 48);
            if (jk != _sbJoinKey)
            {
                _sbJoinKey = jk;
                _sbJoinText = $"{hit.Body.DefOf(hit.Part).Name}: touches construct {jplan.Other.Id} - F to {(h.Tool == ConstructTool.Wrench ? "bolt" : "weld")} the two into one";
            }
            g.SandboxLookText = _sbJoinText;
        }
        if (Pressed(GameAction.Interact) && storage != null && storeNear && (hit.SlotPlus1 > 0 || hit.Body.DefOf(hit.Part).IsBin))
        {
            // F on a stored part takes it into the hands; on a bin tips your scrap in (or takes a load out).
            if (hit.SlotPlus1 > 0)
                g.SandboxSend(CActKind.TakeStored, body: hit.Body.Id, part: (byte)hit.Part, socket: (byte)(hit.SlotPlus1 - 1));
            else
                g.SandboxSend(CActKind.Bin, body: hit.Body.Id, part: (byte)hit.Part);
        }
        else if (Pressed(GameAction.Interact) && !(h.Tool == ConstructTool.PhysicsGun && h.GrabBody != 0)) // (holding with the physics gun, F turns the held thing)
        {
            if (join)
                g.SandboxSend(CActKind.Join, body: hit.Body.Id, part: (byte)hit.Part);
            else if (aimed && hit.Distance <= ConstructBuild.UseReach && hit.Body.Loose && h.Held == ConstructParts.None)
                g.SandboxSend(CActKind.PickUp, body: hit.Body.Id);
            else if (aimed && hit.Distance <= ConstructBuild.UseReach)
                g.SandboxSend(CActKind.Use, body: hit.Body.Id, part: (byte)hit.Part);
            else if (nearRack)
                g.SandboxSend(CActKind.Rack);
        }

        // The physics gun: grab, move, turn, freeze (Program.PhysicsGun.cs).
        if (h.Tool == ConstructTool.PhysicsGun)
        {
            PhysGunInput(g, me, ref h, eye, dir, now);
            return true;
        }
        PhysGunIdle();

        // Routed links (decision 20): the look line on a link, and the wrench's or welder's repair (Program.SandboxLinks.cs).
        SandboxRouteLook(g, ref h, aimed, hit);
        // The linking tool: routing links node by node, the edit modes, settings (Program.SandboxLinks.cs).
        if (h.Tool == ConstructTool.Linker)
        {
            SandboxLinkerInput(g, me, ref h, eye, dir, aimed, hit);
            return true;
        }

        // The configure tool: fire on a part opens its settings panel; shift+fire selects more of a kind.
        if (h.Tool == ConstructTool.Configure)
        {
            if (!SandboxCargoInput(g, me, eye, dir, aimed, hit)) // (a cargo volume's face handles and box drawing first)
                SandboxConfigInput(g, me, aimed, hit);
            return true;
        }

        // Ghost sockets for the held part (a tool out only: bare hands never edit).
        var held = ConstructParts.Get(h.Held);
        // Storage first: the slots the held part fits light up (any hands); a click sets it into the chosen one.
        if (slotted && Pressed(GameAction.Fire) && !_swallowClick && ConstructRender.SlotSelected >= 0)
            g.SandboxSend(CActKind.StoreIn, body: hit.Body.Id, part: (byte)hit.Part, socket: (byte)ConstructRender.SlotSelected);
        if (h.Tool != ConstructTool.None && held != null && !slotted)
        {
            ConstructRender.GhostDef = held;
            if (aimed)
            {
                // (a loose part or a wreck too: building onto it makes it a construct, yours)
                var c = hit.Body;
                c.WorldFrame(c.Parts[hit.Part].Seg, out var q, out var t);
                var near = Vector3.Transform(hit.Point - t, Quaternion.Conjugate(q));
                var n = SandboxSnapGhosts(c, held, near);
                ConstructRender.SetGhosts(c, n);
                // The one nearest the crosshair.
                var best = -1;
                var bestD = 0.8f;
                for (var k = 0; k < n; k++)
                {
                    c.SocketAt(ConstructRender.Ghosts[k].Part, ConstructRender.Ghosts[k].Socket, out var at, out _);
                    c.SegPose(c.Parts[ConstructRender.Ghosts[k].Part].Seg, out var sq, out var st);
                    var world = c.ToWorld(st + Vector3.Transform(at, sq));
                    var along = Vector3.Dot(world - eye, dir);
                    var off = Vector3.Distance(world, eye + dir * along);
                    if (along > 0 && off < bestD)
                    {
                        bestD = off;
                        best = k;
                    }
                }
                best = SandboxSnapSelect(c, held, ref n, best); // (a gun part, wheel or door magnet-snaps to its socket on screen)
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
                        SandboxGuessAgain(gh.Variant); // R: back to the guess (smart rotation's, else the first that fits); at the guess, the next best
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
                        }
                        else
                            g.SandboxSay($"the {held.Name} can't turn that way on this socket");
                    }
                    var v = _sbVariant >= 0 && _sbVariant < total ? _sbVariant : gh.Variant;
                    ConstructRender.GhostValid = ConstructBuild.Place(c, gh.Part, gh.Socket, held, v / 4, v % 4, out var shown) == null;
                    if (ConstructRender.GhostValid)
                        gh = shown with { Variant = v };
                    SandboxSnapShown(g, c, held, in gh, now);
                    var quick = _settings.SnapBuild && g.SandboxInfiniteParts;
                    if ((Pressed(GameAction.Fire) && !_swallowClick) || SandboxSnapPaint(g, in h, c, in gh))
                    {
                        g.SandboxSend(CActKind.Attach, body: c.Id, part: (byte)gh.Part, socket: (byte)gh.Socket, variant: (byte)v);
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
            }
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
                }
                else if (SandboxTurnPressed() is { } turnKey)
                {
                    _sbFreePos = -1;
                    // The rotate keys, relative to the camera in the new construct's frame (the frame Pose makes below).
                    SandboxCamera(g, out var camRight, out var camUp);
                    var frame = ConstructPlace.Frame(surf.Normal, ConstructPlace.FrameYaw(g.LookYaw, (flags & ConstructPlace.FlagGrid) != 0), out _);
                    _sbTurn = Rot24.Compose(GhostTurn.Delta(turnKey, frame, camRight, camUp), _sbTurn);
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
                    var how = (flags & ConstructPlace.FlagAnchor) != 0 || held.Role == PartRole.Foundation ? "anchored" : "free";
                    _sbGroundText = why != null ? $"Can't set the {held.Name} down here: {why}"
                        : !ConstructBuild.ToolFits(h.Tool, held) ? $"The {held.Name} needs the {held.Tool.ToString().ToLowerInvariant()}"
                        : $"New construct: hold fire to set down the {held.Name} ({how}{((flags & ConstructPlace.FlagGrid) != 0 ? ", on the grid" : "")}; {_sbTurnText})";
                }
                g.SandboxLookText = _sbGroundText;
                if (Pressed(GameAction.Fire) && !_swallowClick)
                {
                    if (why != null)
                        g.SandboxSay(why);
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
            g.SandboxSend(CActKind.Remove, body: hit.Body.Id, part: (byte)hit.Part);
            _sbRemoving = true;
        }
        // Let go before it's done: the edit stops.
        if ((_sbFiring && !Down(GameAction.Fire)) || (_sbRemoving && !Down(GameAction.Aim)))
        {
            if (h.Edit != EditKind.None)
                g.SandboxSend(CActKind.Cancel);
            _sbFiring = _sbRemoving = false;
        }
        return true;
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
            key = key * 31 + b[(int)a];
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
        int B(GameAction a) => b[(int)a];
        var rl = B(GameAction.RollLeft);
        var rr = B(GameAction.RollRight);
        var roll = Keybinds.IsChord(rl) && Keybinds.IsChord(rr) ? $"Shift + {N(rl & ~Keybinds.ShiftFlag)} {N(rr & ~Keybinds.ShiftFlag)}" : $"{N(rl)} / {N(rr)}";
        return $"{N(B(GameAction.RotateLeft))} {N(B(GameAction.RotateRight))} {N(B(GameAction.RotateUp))} {N(B(GameAction.RotateDown))}: turn, {roll}: roll, {N(B(GameAction.Reload))}: reset";
    }

    private static long _sbStoreKey = -1;
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

    private static string StorageLook(Game g, Player me, in ConstructHit hit, CStore st, bool near, ConstructPartDef? held)
    {
        var scrap = g.SandboxInventory(me.Id).Scrap;
        var key = ((long)hit.Body.Id << 40) ^ ((long)hit.Part << 32) ^ ((long)hit.SlotPlus1 << 26) ^ ((long)st.Version << 6) ^ ((long)(held?.Id ?? 255) << 52) ^ (near ? 1L << 62 : 0) ^ ((long)Math.Min(scrap, 1 << 20) << 8);
        if (key == _sbStoreKey)
            return _sbStoreText;
        _sbStoreKey = key;
        var cd = hit.Body.DefOf(hit.Part);
        if (cd.IsBin)
        {
            var act = !near ? "" : scrap > 0 ? $" - F: tip in your {Math.Min(scrap, cd.BulkCapacity - st.Amount)} {cd.BulkMaterial}" : st.Amount > 0 ? $" - F: take {Math.Min(ConstructStorage.TakeLoad, st.Amount)} {cd.BulkMaterial}" : "";
            return _sbStoreText = $"{cd.Name}: {st.Amount}/{cd.BulkCapacity} {cd.BulkMaterial}{act}";
        }
        if (hit.SlotPlus1 > 0 && hit.SlotPlus1 - 1 < st.SlotCount && ConstructParts.Get(st.Def[hit.SlotPlus1 - 1]) is { } d)
        {
            var k = hit.SlotPlus1 - 1;
            var slot = cd.Slots[k];
            var pile = slot.Stack > 1 ? $" x{st.Count[k]} ({slot.Stack} fit)" : "";
            var take = !near ? "" : held != null ? " - put down what you hold to take it" : slot.Stack > 1 ? " - F to take one" : " - F to take it";
            return _sbStoreText = $"{d.Name}{pile} in the {cd.Name}, {st.Health[k] * 100:0}%{take}";
        }
        var used = 0;
        for (var k = 0; k < st.SlotCount; k++)
            used += st.Def[k] == ConstructParts.None ? 0 : 1;
        var what = used == 0 ? "empty" : $"{used}/{st.SlotCount} slots used ({st.Items} part{(st.Items == 1 ? "" : "s")})";
        var hint = held == null || !near ? "" : ConstructRender.SlotMask != 0 ? $" - click a lit slot to set the {held.Name} in" : $" - no free slot takes the {held.Name}";
        return _sbStoreText = $"{cd.Name}: {what}, {SlotSizes(cd)}{hint}";
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
        ConstructRender.SlotDef = held;
        _ = g;
        return true;
    }

    /// <summary>A gun under the crosshair: rounds loaded and in its boxes, reloading, or why it won't fire (the look text's cache rebuilds it only on a change).</summary>
    private static string GunLook(Construct c, int k, in CPart hitPart)
    {
        ref var g = ref c.Guns[k];
        ref readonly var p = ref g.Part < c.PartCount ? ref c.Parts[g.Part] : ref hitPart; // (a merged receiver's member: its lead's gun)
        var name = p.D.Name;
        if (g.MergeN > 1)
        {
            ConstructMerge.Health(c, g.Part, out var hp, out var most);
            name = $"{name} ({g.MergeN}x{g.MergeN} merged, {hp:0}/{most:0})";
        }
        return p.Broken ? $"{name} (wrecked)"
            : g.Problem.Length > 0 ? $"{name}: won't fire: {g.Problem}"
            : g.BoxCount == 0 && p.Rounds == 0 ? $"{name}: no ammo box on its feed"
            : $"{name}: {p.Rounds}/{g.Mag}{(p.Reload > 0 ? " reloading" : "")}, {ConstructWeapons.BoxRounds(c, k)} in the boxes{(g.Seat >= 0 ? " - a gunner's seat works it" : g.Sensor != 255 ? " - a sentry" : " - wire a signal into it to fire")}";
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
            Using = h.Edit != EditKind.None || (h.Tool == ConstructTool.Linker && _lkBody != 0) || (h.Tool == ConstructTool.PhysicsGun && h.GrabBody != 0) || (h.Tool == ConstructTool.Configure && Ui.SandboxConfigOpen), // (the linking tool: while a link is being routed; the physics gun: while it holds)
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
        ToolRender.Emit(ref o, _sbTool);
        _vmAnim.Right.Solve(_sbTool.Right, Matrix4x4.Identity, Vector3.Zero);
        _vmAnim.Left.Solve(_sbTool.Left, Matrix4x4.Identity, Vector3.Zero);
        var armColors = ArmColors.For(me.Color * 0.5f + new Vector3(0.06f));
        _vmAnim.Right.Draw(Gfx, armColors);
        _vmAnim.Left.Draw(Gfx, armColors);
        if (Matrix4x4.Invert(_worldView, out var camToWorld))
            ToolRender.NoteViewTip(_sbTool, camToWorld); // (the welder's light, next frame)
        return true;
    }
}
