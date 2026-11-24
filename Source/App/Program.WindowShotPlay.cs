using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window tool's play actions (verification run 6): sit in a seat or cab and drive it with the held keys, a wreck into a
/// Recycler's intake, a survival copy and paste and what it cost, and a part's numbers. Each goes through the player's own
/// path: the use key's Use action, a part wrecked by the damage path and left in the intake for the Recycler's own scan, the
/// selector's messages (selection, Copy, Paste) that the Selector tool sends, the crosshair for the look line.
/// </summary>
public static partial class Program
{
    private static ushort _wsAdmiralLead;
    private static int _wsAdmiralGroup; // (the window tool's crewadmiral: the ship the others escort)

    private static int[]? _wsClipParts;
    private static int[]? _wsClipMade;
    private static readonly List<ushort> _wsClipMadeDef = [];
    private static int _wsClipBodies, _wsClipLoose;
    private static bool _wsClipToolAsked;

    private static bool WsPlayKnown(string action) => action.Split(' ', 2)[0].ToLowerInvariant() is "sit" or "seated" or "getout" or "recycle" or "clip" or "partinfo" or "partlook" or "hands" or "cabto" or "damage" or "makeengine" or "standon" or "aimintake" or "feetup" or "cave" or "spawnbp" or "bringon" or "dropmotion" or "shellwear" or "shellleave" or "shellfix" or "shells" or "hopper" or "shellgantry" or "shellpod" or "modsock";

    private static bool WsPlayVerb(Game g, Player me, string verb, string arg)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        switch (verb)
        {
            case "shells" when g.IsServer: // host: "shells [look|lock]": every shell in flight: kind, age, where it is from the body that fired it, its speed against it, its target; and each body's radar lock
            {
                var w = g.Constructs;
                foreach (var b in w.Bodies)
                    if (b.RadarLock != 0)
                        WsSay($"shells: body {b.Id} has radar lock on body {b.RadarLock}");
                WsSay($"shells: radar craft {g.RadarCraft?.Id ?? 0}, range {g.RadarRange:0} m, can lock {g.RadarCanLock}, local lock {g.RadarLock}, {g.RadarContactCount} contacts");
                for (var i = 0; i < g.RadarContactCount; i++)
                    if (w.Get(g.RadarContacts[i]) is { } ct && g.RadarCraft is { } rc)
                        WsSay(string.Create(ci, $"shells: contact body {ct.Id}, {Vector3.Distance(ct.WorldCom, rc.WorldCom):0} m"));
                if (arg == "look" && g.RadarContactCount > 0 && w.Get(g.RadarContacts[0]) is { } first)
                    WsLook(g, me.Eye, first.WorldCom); // ("shells look": the look turned onto the nearest contact, as the mouse would turn it)
                if (arg == "lock" && g.RadarContactCount > 0 && w.Get(g.RadarContacts[0]) is { } locked)
                    WsSay($"shells: lock {(g.RadarPick(me.Eye, Vector3.Normalize(locked.WorldCom - me.Eye)) ? "taken" : "refused")}"); // ("shells lock": the radar key's lock on the nearest contact, wherever a seat lets the look turn)
                WsSay($"shells: {w.ShellCount} in flight");
                for (var k = 0; k < w.ShellCount; k++)
                {
                    ref var sh = ref w.Shells[k];
                    var from = w.Get(sh.Gun);
                    var rel = from != null ? sh.Pos - from.WorldCom : sh.Pos;
                    var rv = from != null ? sh.Vel - from.Vel : sh.Vel;
                    WsSay(string.Create(ci, $"shells: #{sh.Id} {sh.Kind} age {sh.Age} landed {sh.Landed} from body {sh.Gun}: {rel.Length():0.0} m off ({rel:0.0}), {rv.Length():0.0} m/s against it, target {sh.Target}{(w.Get(sh.Target) is { } tg ? string.Create(ci, $" {Vector3.Distance(tg.WorldCom, sh.Pos):0} m off it, closing {Vector3.Dot(sh.Vel - tg.Vel, Vector3.Normalize(tg.WorldCom - sh.Pos)):0} m/s") : "")}"));
                }
                return true;
            }
            case "crew": // "crew": every crew's roster and its driver's shots, the body's height and speed (the host's: GameCrew.cs)
                foreach (var line in g.CrewLines())
                    Console.WriteLine($"[windowshot] crew: {line}");
                return true;
            case "crewbodies": // "crewbodies N": the host's crews have at most N bodies (the rest are records at their seats, drawn as stand-ins)
                if (g.IsServer && int.TryParse(arg, out var crewMax))
                {
                    g.Crew.MaxBodied = Math.Clamp(crewMax, 0, CrewWorld.MaxBodiedCap);
                    g.Crew.UnbodyAll(g);
                }
                return true;
            case "crewconvoy": // "crewconvoy STYLE N": the host sets down a nobody's crewed convoy of N, 60 m ahead of the player, heading across its view
                if (g.IsServer)
                {
                    var bits = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var fwd = GravityFrames.FlatForward(me.Position, me.Yaw);
                    var at = g.SandboxGroundAt(me.Position + fwd * 60);
                    var asked = g.VehicleSpawn.RequestConvoy(bits.Length > 0 ? bits[0] : "Militia", bits.Length > 1 && int.TryParse(bits[1], out var cn) ? cn : 3, 1, null, at, me.Yaw + MathF.PI * 0.5f);
                    Console.WriteLine($"[windowshot] crewconvoy: {asked ?? "asked"}");
                }
                return true;
            case "crewhit": // "crewhit [PLAYER]": host, a server shortcut: this player's (or that player id's: a joined client's) shot (DamageBy) takes 1 hp off a part of the first crewed body, as a round would
                if (g.IsServer)
                    Console.WriteLine($"[windowshot] crewhit: {g.CrewHitFirst(byte.TryParse(arg, out var hitBy) && g.Players.TryGetValue(hitBy, out var hp) ? hp : me)}");
                return true;
            case "shellwear": // "shellwear WORK PCT": host, a test state: that work (LegL, LegR, ArmL, ArmR, Power, Cpu, Optics, Plating or all) of the worn shell, or of the nearest standing one, at PCT % (GameShellWear.cs); "shellwear": the worn shell's works and its pace as this peer has them
            {
                var bits = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (bits.Length >= 2 && float.TryParse(bits[1], System.Globalization.NumberStyles.Float, ci, out var pct))
                    WsSay($"shellwear: {g.ShellWearSet(me, bits[0], pct)}");
                var line = $"in shell {me.InShell}, pace x{Game.ShellMoveMult(me):0.00}, fit x{Game.ShellFitShare(me.ShellWear):0.00}:";
                for (var k = 0; k < ShellWear.Works; k++)
                    line += $" {ShellWear.ShortOf((ShellWork)k)} {ShellWear.Pct(me.ShellWear[k])}%";
                foreach (var d in g.Constructs.Bodies)
                    if (d.PartCount == 1 && d.Parts[0].D.ShellBody)
                    {
                        ShellCond sc = default;
                        ShellWear.Read(d.Parts[0], d.Parts[0].D, ref sc);
                        WsSay($"shellwear: standing shell {d.Id} at {d.WorldCom}: L leg {ShellWear.Pct(sc[0])}%, plating {ShellWear.Pct(sc[7])}%, look {g.ShellWearLook(d, 0) ?? "(none)"}");
                    }
                var legs = Game.ShellSpare(ShellWork.LegL) is { } spare ? g.SandboxInventory(me.Id).Count(spare.Id) : -1;
                WsSay($"shellwear: {line}; spare leg actuators {legs}; look \"{g.SandboxLookText}\"");
                return true;
            }
            case "shellfix": // "shellfix": the attach tool's Fire at the nearest standing Robot Shell, as its look line sends it (CActKind.Mend), whatever the crosshair is on
                foreach (var d in g.Constructs.Bodies)
                    if (d.PartCount == 1 && d.Parts[0].D.ShellBody)
                    {
                        WsSay($"shellfix: shell {d.Id}, {System.Numerics.Vector3.Distance(me.Eye, d.WorldCom):0.0} m from the eye");
                        g.SandboxSendMend(d.Id, 0);
                        break;
                    }
                return true;
            case "hopper": // "hopper": this peer's shell frame, its size (eye, hit box, mover scale) and its ore hopper as heard; the host adds the server's hopper (GameShellHopper.cs)
            {
                var f = ShellFrames.Of(me.ShellFrame);
                using (World.Body(me.Sim))
                    WsSay(FormattableString.Invariant($"hopper: in shell {me.InShell}, frame {me.ShellFrame} ({f.Name}, x{ShellFrames.ScaleOf(me):0.00}, pace x{Game.ShellMoveMult(me):0.00}), sim frame {me.Sim.Frame}, eye {Vector3.Distance(me.Eye, me.Position):0.00} m, height {World.HeightOf(me.Sim.Stance):0.00} m, radius {World.BodyRadius:0.00} m; heard {me.HopperAmount} {RecyclingTable.Current.NameOf(me.HopperMaterial)}")
                        + (g.IsServer ? $"; server {g.SandboxHands.Hoppers[me.Id].Amount}/{g.SandboxHands.Hoppers[me.Id].Capacity} {RecyclingTable.Current.NameOf(g.SandboxHands.Hoppers[me.Id].Material)}" : "")
                        + $"; inventory stone {g.SandboxInventory(me.Id).Materials[RecyclingTable.Current.IdOf("stone")]}, dirt {g.SandboxInventory(me.Id).Materials[RecyclingTable.Current.IdOf("dirt")]}");
                foreach (var o in g.Players.Values)
                    if (o != me)
                        WsSay(string.Create(ci, $"hopper: player {o.Id} in shell {o.InShell}, frame {o.ShellFrame}, heard {o.HopperAmount} {RecyclingTable.Current.NameOf(o.HopperMaterial)}"));
                return true;
            }
            case "shellgantry": // "shellgantry setup|fit SLOT MODULE|state": the Shell Gantry (Program.WindowShotShellGantry.cs)
                WsShellGantry(g, me, arg);
                return true;
            case "shellpod": // "shellpod setup|go N [other]|dock|state": shell storage pods (Program.WindowShotShellPod.cs)
                WsShellPod(g, me, arg);
                return true;
            case "shellleave": // "shellleave": the Esc menu's Leave the shell
                g.ShellAskLeave();
                return true;
            case "crewbreak": // "crewbreak [fail]": host, a server shortcut: the first crewed body's breakdown clock runs out now (a warning; "fail": the part breaks down at once)
                if (g.IsServer)
                    Console.WriteLine($"[windowshot] crewbreak: {g.CrewBreakFirst(arg == "fail")}");
                return true;
            case "crewkill": // "crewkill JOB": host, a server shortcut: every crew member with that job on the first crewed body dies (records too: a round through the hull)
                if (g.IsServer)
                    Console.WriteLine($"[windowshot] crewkill: {g.CrewKillJob(arg)}");
                return true;
            case "crewfaction": // "crewfaction NAME": host, a test state: the first crewed body's crew is of that NPC faction kind
                if (g.IsServer)
                    Console.WriteLine($"[windowshot] crewfaction: {g.CrewSetFaction(arg)}");
                return true;
            case "crewgroup": // "crewgroup NAME": host, a test state: every crewed body's crew is of that power, formed into one patrol group
                if (g.IsServer)
                    Console.WriteLine($"[windowshot] crewgroup: {g.CrewGroupAll(arg)}");
                return true;
            case "crewown": // "crewown": host, a test state: the last crewed body becomes this player's own crew
                if (g.IsServer)
                    Console.WriteLine($"[windowshot] crewown: {g.CrewMakeOwn(me)}");
                return true;
            case "crewdisarm": // "crewdisarm": host, a server shortcut: this player is put by the first crewed body's scuttle charge and starts its disarm (the Mend act's server path)
                if (g.IsServer)
                    Console.WriteLine($"[windowshot] crewdisarm: {g.CrewDisarmFirst(me)}");
                return true;
            case "crewprisoner": // "crewprisoner release|drop|hold|hire": host: this player's order for the first prisoner on the first crewed body; a client sends it as the Crew window's button does (CActKind.Crew), to the first prisoner of a prize it has the roster of
                if (g.IsServer)
                    Console.WriteLine($"[windowshot] crewprisoner: {g.CrewPrisonerFirst(me, arg)}");
                else
                {
                    var op = arg switch { "release" => CrewOrder.Release, "drop" => CrewOrder.Drop, "hire" => CrewOrder.Hire, _ => (CrewOrder)255 };
                    var sent = false;
                    foreach (var v in g.CrewRosters.Values)
                        foreach (var m in v.Members)
                            if (!sent && op != (CrewOrder)255 && m.State == CrewState.Surrendered)
                            {
                                g.SandboxSendCrewOrder(v.Body, m.Uid, op);
                                Console.WriteLine($"[windowshot] crewprisoner: sent {op} for {m.Name} on {v.Body}");
                                sent = true;
                            }
                    if (!sent)
                        Console.WriteLine("[windowshot] crewprisoner: no prisoner in this client's rosters");
                }
                return true;
            case "crewadmiral": // "crewadmiral ships N BLUEPRINT [/ LEAD]" (host, a test state: N ships of yours with hired crews, and a lead before them) | "new [all]" | "add" | "order KIND [ID|lead|npc|ride|X Z]" | "formation NAME" | "spacing M" | "doctrine N" | "admiral hand|me" | "stop WORK X Z [S]" | "task NAME" | "disband" | "show": players as admirals (faction-controllers.md phase 6) through the real fleet act (CActKind.Fleet); X Z are metres right and ahead of this player
            {
                var aw = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var av = aw.Length > 0 ? aw[0].ToLowerInvariant() : "show";
                var grp = g.FleetGroups.Count > 0 ? g.FleetGroups[Math.Min(_wsAdmiralGroup, g.FleetGroups.Count - 1)] : null;
                var fwd = GravityFrames.FlatForward(me.Position, me.Yaw);
                var right = Vector3.Cross(fwd, GravityFrames.UpAt(me.Position));
                Vector3 Place(int k) => k + 1 < aw.Length && float.TryParse(aw[k], System.Globalization.CultureInfo.InvariantCulture, out var dx) && float.TryParse(aw[k + 1], System.Globalization.CultureInfo.InvariantCulture, out var dz)
                    ? g.SandboxGroundAt(me.Position + right * dx + fwd * dz) : default;
                ushort Free()
                {
                    foreach (var v in g.CrewRosters.Values)
                        if (v.Own && v.Owner == me.Id && g.FleetGroupOf(v.Body) == null && v.Body != _wsAdmiralLead)
                            return v.Body;
                    return 0;
                }
                ushort Target(string t) => t switch
                {
                    "lead" => _wsAdmiralLead,
                    "ride" => me.Sim.RideBody,
                    "npc" => g.CrewInfo.Keys.FirstOrDefault(b => !g.CrewRosters.ContainsKey(b)),
                    "crewed" => g.CrewRosters.Values.FirstOrDefault(v => v.Own && g.FleetGroupOf(v.Body) == null)?.Body ?? 0, // (a crewed ship of anyone's in none of this peer's groups: a client's way to the host's lead)
                    _ => ushort.TryParse(t, out var id) ? id : (ushort)0,
                };
                switch (av)
                {
                    case "ships" when g.IsServer && aw.Length > 2:
                    {
                        var rest = string.Join(' ', aw[2..]).Split('/', StringSplitOptions.TrimEntries);
                        Console.WriteLine($"[windowshot] crewadmiral: {g.FleetTestShips(me, rest[0], int.Parse(aw[1]), rest.Length > 1 ? rest[1] : null) ?? "asked"}");
                        break;
                    }
                    case "lead": // "lead [ID]": the ship the others escort (host without an id: the test state's lead); not taken by "new" or "add"
                        _wsAdmiralLead = aw.Length > 1 ? ushort.Parse(aw[1]) : g.FleetTestLead;
                        Console.WriteLine($"[windowshot] crewadmiral lead: {_wsAdmiralLead}");
                        break;
                    case "new":
                        g.SandboxFleet(FleetOp.New, body: aw.Length > 1 && aw[1] == "lead" ? _wsAdmiralLead : Free(), arg: (byte)(aw.Length > 1 && aw[1] == "all" ? 1 : 0));
                        break;
                    case "use" when aw.Length > 1: // "use N": the verbs after this work on this peer's N-th group (from 0)
                        _wsAdmiralGroup = int.Parse(aw[1]);
                        break;
                    case "add" when grp != null:
                        g.SandboxFleet(FleetOp.Add, grp.Slot, aw.Length > 1 ? Target(aw[1]) : Free());
                        break;
                    case "order" when grp != null && aw.Length > 1 && Enum.TryParse<OrderKind>(aw[1], true, out var kind):
                        g.SandboxFleet(FleetOp.Order, grp.Slot, aw.Length == 3 ? Target(aw[2]) : (ushort)0, at: Place(2), arg: (byte)kind);
                        break;
                    case "formation" when grp != null && aw.Length > 1 && Enum.TryParse<FleetFormation>(aw[1], true, out var shape):
                        g.SandboxFleet(FleetOp.Formation, grp.Slot, arg: (byte)shape);
                        break;
                    case "spacing" when grp != null && aw.Length > 1:
                        g.SandboxFleet(FleetOp.Spacing, grp.Slot, value: float.Parse(aw[1], System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case "doctrine" when grp != null && aw.Length > 1:
                        g.SandboxFleet(FleetOp.Doctrine, grp.Slot, arg: byte.Parse(aw[1]));
                        break;
                    case "task" when grp != null && aw.Length > 1 && Enum.TryParse<GroupTask>(aw[1], true, out var task):
                        g.SandboxFleet(FleetOp.Task, grp.Slot, arg: (byte)task);
                        break;
                    case "admiral" when grp != null:
                    {
                        uint uid = 0;
                        if (aw.Length > 1 && aw[1] == "hand")
                            foreach (var b in grp.Ships.Take(grp.Count))
                                if (uid == 0 && g.CrewRosters.TryGetValue(b, out var rv))
                                    uid = rv.Members.FirstOrDefault(mm => mm.Job != CrewJob.Pilot && mm.State != CrewState.Dead).Uid;
                        g.SandboxFleet(FleetOp.Admiral, grp.Slot, uid: uid);
                        break;
                    }
                    case "stop" when grp != null && aw.Length > 3 && Enum.TryParse<OrderKind>(aw[1], true, out var work):
                        g.SandboxFleet(FleetOp.Stop, grp.Slot, at: Place(2), arg: (byte)work, value: aw.Length > 4 ? float.Parse(aw[4], System.Globalization.CultureInfo.InvariantCulture) : 20);
                        break;
                    case "disband" when grp != null:
                        g.SandboxFleet(FleetOp.Disband, grp.Slot);
                        break;
                }
                Console.WriteLine($"[windowshot] crewadmiral {av}: {g.FleetGroupsLine()}");
                if (g.IsServer)
                    Console.WriteLine($"[windowshot] crewadmiral server: {g.FleetPlayersLine()}");
                return true;
            }
            case "crewfleet": // "crewfleet": host: every group, its admiral, ships, orders and strength (faction-controllers.md phase 2)
                Console.WriteLine($"[windowshot] crewfleet: {(g.IsServer ? g.CrewFleetLines() : $"{g.IntelMarks.Count} intel marks")}");
                foreach (var body in g.CrewInfo.Keys)
                    Console.WriteLine($"[windowshot] crewfleet: look line of {body}: {g.CrewLook(body)}"); // (as this peer has it: Msg.CrewSeats)
                return true;
            case "crewpowers": // "crewpowers [near]": host: each power's stock, ships and missions (near: its places laid again 300-450 m round this player); a client: the news and powers gone (faction-controllers.md phase 3)
                Console.WriteLine($"[windowshot] crewpowers: {(g.IsServer ? g.CrewPowersLine(arg) : $"{g.PowerNews.Count} news lines, last \"{g.PowerNews.LastOrDefault()}\"; powers gone {g.FactionTable.PowerGone:X4}; grid {g.FactionTable.PowerCount} powers, {g.FactionTable.PactCount} pacts")}");
                return true;
            case "crewpolitics": // "crewpolitics [found NAME|standing POWER V|pact POWER KIND|accept POWER|break POWER|call|take N|drop N]": phase 5's acts through the real faction messages (standing: host, a test state); then this peer's pacts with factions and contracts
            {
                var w = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var pv = w.Length > 0 ? w[0].ToLowerInvariant() : "";
                byte Pw(int k) => k < w.Length ? CrewPowers.Find(w[k]) : (byte)0;
                switch (pv)
                {
                    case "found" when w.Length > 1:
                        g.SandboxFaction(FactionOp.Found, text: string.Join(' ', w[1..]));
                        break;
                    case "standing" when w.Length > 2 && g.IsServer:
                        Console.WriteLine($"[windowshot] crewpolitics: {g.PoliticsStanding(me, Pw(1), int.Parse(w[2]))}");
                        break;
                    case "pact" when w.Length > 2:
                        g.SandboxFaction(FactionOp.Pact, Pw(1), (int)(w[2].ToLowerInvariant() switch { "ceasefire" => PactKind.Ceasefire, "nonaggression" => PactKind.NonAggression, "trade" => PactKind.Trade, "alliance" => PactKind.Alliance, "tribute" => PactKind.Tribute, _ => PactKind.None }));
                        break;
                    case "accept":
                        g.SandboxFaction(FactionOp.PactAnswer, Pw(1), 1);
                        break;
                    case "break":
                        g.SandboxFaction(FactionOp.PactBreak, Pw(1));
                        break;
                    case "call":
                        g.SandboxFaction(FactionOp.CallAllies);
                        break;
                    case "invite": // (the first other person here)
                        foreach (var o in g.Players.Values)
                            if (o.Id != me.Id && Game.FactionPerson(o))
                            {
                                g.SandboxFaction(FactionOp.Invite, o.Id);
                                break;
                            }
                        break;
                    case "acceptinvite":
                        g.SandboxFaction(FactionOp.Accept, g.FactionTable.Invited[me.Id]);
                        break;
                    case "take" or "drop" when w.Length > 1 && int.TryParse(w[1], out var n) && n >= 0 && n < g.ContractsCount:
                        g.SandboxFaction(pv == "take" ? FactionOp.ContractTake : FactionOp.ContractDrop, g.ContractsView[n].Body, g.ContractsView[n].Serial);
                        break;
                }
                Console.WriteLine($"[windowshot] crewpolitics: {g.PoliticsLine()}; standing: {g.CrewStandingLine(me)}; {g.TabLine}");
                return true;
            }
            case "crewterritory": // "crewterritory [near|leave]": host: planets, sites and far groups (near: a far Take group put 3.5 km from this player; leave: the far ranges shrunk so nobody counts as near: its bodies collapse); a client: its map (faction-controllers.md phase 4)
                Console.WriteLine($"[windowshot] crewterritory: {(g.IsServer ? g.CrewTerritoryLine(me, arg) : g.FactionMapLine())}");
                return true;
            case "crewofficer": // "crewofficer question|ransom": host: this player questions or ransoms the first officer prisoner
                if (g.IsServer)
                    Console.WriteLine($"[windowshot] crewofficer: {g.CrewOfficerFirst(me, arg)}");
                return true;
            case "npcstanding": // "npcstanding": how each NPC faction kind sees this player, as this peer's factions table has it
                Console.WriteLine($"[windowshot] npcstanding: {g.CrewStandingLine(me)}");
                return true;
            case "crewshoot": // "crewshoot pilot|gunner|spare": the host shoots the first crew body with that job, as a player's round does (DamagePlayer)
                if (g.IsServer && g.CrewShoot(me, arg) is { } shot)
                    Console.WriteLine($"[windowshot] crewshoot: {shot}");
                return true;
            case "sit": // "sit PART[#n]": the use key on that seat (a Seat, a Lifter Cab, a gunner's seat): the host stands by it first; a client is brought by the host ("bring PART")
            {
                var (c, part) = WsFind(g, arg);
                if (c == null || c.DefOf(part).Role != PartRole.Seat)
                {
                    WsFail($"sit: no seat \"{arg}\"");
                    return true;
                }
                if (g.IsServer)
                {
                    var feet = ConstructEdits.SeatFeet(c, part);
                    var up = GravityFrames.UpAt(feet);
                    WsEyeTo(g, me, feet + up * 1.3f);
                    WsLook(g, me.Eye, feet);
                }
                me.NoclipWanted = false;
                g.NoclipShot = false;
                g.SandboxSend(CActKind.Use, body: c.Id, part: (ushort)part); // (the use key's own action: the server's reach, seat taken, already seated checks)
                WsSay($"sit: asked to sit in {c.DefOf(part).Name} (body {c.Id} part {part}){(part == c.DriveSeat ? ", the drive seat" : "")}, {Vector3.Distance(me.Eye, ConstructEdits.SeatFeet(c, part)):0.0} m from its feet");
                _wsWait = 10;
                return true;
            }
            case "seated": // where this player sits as this peer has it, and the body's motion: speed, engine, revs, gear, throttle; the look line
            {
                ref var h = ref g.SandboxHands.Hand(me.Id);
                if (!h.Seated || g.Constructs.Get(h.SeatBody) is not { } c || h.SeatPart >= c.PartCount)
                {
                    WsSay($"seated: no (look: {g.SandboxLookText}; notice: {g.SandboxNotice})");
                    return true;
                }
                var speed = c.Vel.Length();
                WsSay(string.Create(ci, $"seated: {c.DefOf(h.SeatPart).Name} (body {c.Id} part {h.SeatPart}){(c.Driver == me.Id ? ", driving" : "")}; at {c.WorldCom:0.00}, {speed:0.00} m/s; engine running {c.EngineRunning}, rpm {c.Rpm:0}, gear {c.Gear}, throttle {c.Throttle:0.00}, steer {c.Steer:0.00}; seat keys {c.Parts[h.SeatPart].Keys:x4}; look: {g.SandboxLookText}"));
                return true;
            }
            case "getout": // the key that gets out: E (roll right) in a Lifter Cab, the use key in any other seat
            {
                ref var h = ref g.SandboxHands.Hand(me.Id);
                var cab = h.Seated && g.Constructs.Get(h.SeatBody) is { } c && h.SeatPart < c.PartCount && c.DefOf(h.SeatPart).LifterCab;
                _wsPress = cab ? GameAction.SeatRollRight : GameAction.Interact;
                WsSay($"getout: {(h.Seated ? cab ? "E in the cab" : "the use key" : "not seated (the use key anyway)")}");
                _wsWait = 10;
                return true;
            }
            case "recycle": // host: "recycle NAME|KEY [@ RECYCLER[#n]] [^ DX,DZ]": that part wrecked by the damage path and left in the intake (DX, DZ m off its middle in the part's frame: a Scrap Breaker's far square); "recycle read [RECYCLER[#n]]": the bin
            {
                var off = Vector3.Zero;
                if (arg.IndexOf(" ^ ", StringComparison.Ordinal) is var hat and >= 0)
                {
                    var xz = arg[(hat + 3)..].Split(',');
                    if (xz.Length == 2 && float.TryParse(xz[0], System.Globalization.NumberStyles.Float, ci, out var ox) && float.TryParse(xz[1], System.Globalization.NumberStyles.Float, ci, out var oz))
                        off = new Vector3(ox, 0, oz);
                    arg = arg[..hat];
                }
                var at = arg.IndexOf(" @ ", StringComparison.Ordinal);
                var read = arg == "read" || arg.StartsWith("read ", StringComparison.Ordinal);
                var what = read ? "" : at < 0 ? arg : arg[..at];
                var rspec = read ? arg.Length > 5 ? arg[5..] : "Recycler" : at < 0 ? "Recycler" : arg[(at + 3)..];
                var (rc, rp) = WsFind(g, rspec);
                if (rc == null || !rc.DefOf(rp).IsRecycler || g.Constructs.StoreOf(rc, rp) is not { } st)
                {
                    WsFail($"recycle: no Recycler \"{rspec}\"");
                    return true;
                }
                var table = RecyclingTable.Current;
                var rd0 = rc.DefOf(rp);
                if (read)
                {
                    var inside = 0;
                    foreach (var b in g.Constructs.Bodies)
                        if (b != rc && b.PartCount == 1 && ConstructRecycler.InIntake(rc, rp, b.WorldCom))
                            inside++;
                    var others = new List<string>();
                    for (var i = 0; i < rc.PartCount; i++)
                        if (i != rp && g.Constructs.StoreOf(rc, i) is { Amount: > 0 } os)
                            others.Add($"{rc.DefOf(i).Name} {os.Amount} {table.NameOf(os.Material)}");
                    WsSay($"recycle: read: the body's other stores: {(others.Count == 0 ? "empty" : string.Join(", ", others))}");
                    foreach (var b in g.Constructs.Bodies)
                        if (b != rc && b.PartCount == 1 && b.Parts[0].Broken && Vector3.Distance(b.WorldCom, rc.WorldCom) < 8)
                            WsSay($"recycle: read: a wrecked {b.DefOf(0).Name} (body {b.Id}) at {b.WorldCom:0.00}, in the intake {ConstructRecycler.InIntake(rc, rp, b.WorldCom)}{(rd0.IsBreaker ? $", in the mouth {BreakerWorks.InMouth(rc, rp, b.WorldCom, out var bx, out var bz)} at cell {bx},{bz}" : "")}, feedable {ConstructRecycler.Feedable(b)}");
                    WsSay($"recycle: read: bin {st.Amount} of {ConstructStorage.Capacity(rc, rp)}{(rd0.IsBreaker ? $" ({ConstructRefining.SizeText(rc, rp)}), wreck drawn {(st.JobDef == ConstructParts.None ? "none" : ConstructParts.Def(st.JobDef).Name)} at cell {st.JobCellX},{st.JobCellZ}" : "")}, {table.NameOf(st.Material)}; job {st.JobAmount} {table.NameOf(st.JobMaterial)}, {st.JobTicks / (float)World.TickRate:0.0} s of {st.JobFull / (float)World.TickRate:0.0} left, busy {st.Busy}, blocked {st.Blocked}, held {st.Held}; powered {ConstructRecycler.Powered(g.Constructs, rc, rp)}, line {ConstructRecycler.LineOf(g.Constructs, rc, rp, st)}; bodies in the intake {inside}");
                    return true;
                }
                if (!g.IsServer || ConstructParts.Find(what) is not { } def)
                {
                    WsFail($"recycle NAME|KEY [@ RECYCLER[#n]] (on the host): no part \"{what}\"");
                    return true;
                }
                ref var rpart = ref rc.Parts[rp];
                var rd = rc.DefOf(rp);
                rc.WorldFrame(rpart.Seg, out var q, out var t);
                var mid = t + Vector3.Transform(rpart.Pos + Rot24.Apply(rpart.Rot, (rd.IntakeMin + rd.IntakeMax) * 0.5f + off), q);
                var wreck = ConstructEdits.DropLoose(g.Constructs, def, mid, Quaternion.Identity);
                if (wreck == null)
                {
                    WsFail("recycle: no room for the part");
                    return true;
                }
                var whole = wreck.Parts[0].Health;
                g.Constructs.DamagePart(wreck, 0, whole + 1, out var dealt, raw: true); // (what a gun's round does to it: health to 0, wrecked, no armour)
                if (!g.Constructs.Bodies.Contains(wreck)) // (a part that breaks off falls away as a new body of wrecked debris)
                    foreach (var b in g.Constructs.Bodies)
                        if (b.PartCount == 1 && b.Parts[0].Broken && b.DefOf(0) == def && Vector3.Distance(b.WorldCom, mid) < 1.5f)
                            wreck = b;
                var (mat, amount) = table.YieldOf(wreck.Parts[0]);
                var cap = PressTable.Current.CostCapOf(def);
                var recipe = AssemblingTable.Current.Of(def.Id);
                WsSay(string.Create(ci, $"recycle: {def.Name} body {wreck.Id} at {wreck.WorldCom:0.00} ({(g.Constructs.Bodies.Contains(wreck) ? wreck.Parts[0].Broken ? "wrecked" : "NOT wrecked" : "gone, no debris")}, in the intake {ConstructRecycler.InIntake(rc, rp, wreck.WorldCom)}, {dealt:0} damage of {whole:0} hp); mass {def.Mass:0.##} kg, cost cap {cap:0.##} kg; yields {amount} {table.NameOf(mat)}; its recipe {(recipe == null ? "none" : recipe.Batch)}; bin before {st.Amount} {table.NameOf(st.Material)}"));
                _wsWait = 3;
                return true;
            }
            case "clip": // the selector's copy and paste through its own messages: "clip copy PART[#n] [FIRST-LAST]", "clip paste DIST [UP]", "clip cost"
                WsClip(g, me, arg);
                return true;
            case "partinfo": // a part's numbers as this peer has them (mass, health, armour, yield, recipe; the engine round it), then its look line
            {
                var (c, part) = WsFind(g, arg);
                if (c == null)
                {
                    WsFail($"partinfo: no part \"{arg}\"");
                    return true;
                }
                var d = c.DefOf(part);
                ref var p = ref c.Parts[part];
                var (mat, amount) = RecyclingTable.Current.YieldOf(p);
                WsSay(string.Create(ci, $"partinfo: {d.Name}{(d.RuntimeMade ? " (made)" : "")} (body {c.Id} part {part}): mass {c.PartMass(part):0.##} kg (def {d.Mass:0.##}), health {p.Health:0.#} of {d.Health:0.#}, armour {d.Armour:0.#}{(p.Broken ? ", wrecked" : "")}; recycles to {amount} {RecyclingTable.Current.NameOf(mat)}; cost cap {PressTable.Current.CostCapOf(d):0.##} kg; recipe {AssemblingTable.Current.Of(d.Id)?.Batch ?? "none"}; body {c.PartCount} parts, {c.Mass:0} kg"));
                WsSay($"partinfo: a Welder's join here: {(ConstructPlace.FindJoin(g.Constructs, c, part, out var jp) ? $"yes, onto body {jp.Other!.Id}" : "none (no free socket of it meets another construct's, square)")}; anchored {c.Anchored}, loose {c.Loose}, segment {c.Parts[part].Seg}");
                {
                    var links = 0;
                    for (var l = 0; l < c.LinkCount; l++)
                        if (c.Links[l].A == part || c.Links[l].B == part)
                        {
                            links++;
                            var o = c.Links[l].A == part ? c.Links[l].B : c.Links[l].A;
                            WsSay($"partinfo: link to part {o} ({c.DefOf(o).Name})");
                        }
                    var dk = c.DoorOf(part);
                    WsSay($"partinfo: {links} links; door {(dk < 0 ? "none" : c.Doors[dk].Part == part ? $"{dk} (its own)" : $"{dk}'s leaf (door part {c.Doors[dk].Part})")}; pos {p.Pos}");
                }
                if (c.ModEngines is { } mes)
                    for (var k = 0; k < c.ModCount; k++)
                    {
                        var e = mes[k];
                        e.Rpm(EngineRules.Current, out var idle, out var peak, out var max);
                        WsSay(string.Create(ci, $"partinfo: engine {k}: {ConstructEngine.Readout(e)}; {e.Kw:0.#} kW, {e.Kw * 1000 / (peak / ConstructDrive.RpmPerRadS):0} N m at {peak:0} rpm, idle {idle:0}, limiter {max:0}; stroke {e.Stroke:0.###} (built {e.BuiltStroke:0.###}), scale {e.Scale:0.###}; cylinders {e.Cylinders}, wrecked {e.Wrecked}, misfiring {e.Misfiring}, starter {(e.HasStarter ? "fitted" : "none")}, needs one {ConstructEngine.NeedsStarter(e, EngineRules.Current)}; fault {e.Fault}; running {c.EngineRunning}, rpm now {c.Rpm:0}"));
                    }
                _wsQueue.EnqueueFront("partlook");
                if (!g.SandboxHands.Hand(me.Id).Seated)
                    _wsQueue.EnqueueFront($"near {arg} 2"); // (the look line is the crosshair's within reach: stand 2 m off it)
                return true;
            }
            case "hands": // every lifter's hand as this peer has it: its state, the hand's height over the ground under it and the piece's lowest point (a crane's carry height), the piece
                foreach (var lb in g.Constructs.LiftBodies)
                    if (g.Constructs.Get(lb.Body) is { } hb)
                        for (var k = 0; k < lb.N; k++)
                        {
                            var l = lb.Lifts[k];
                            if (l.Part >= hb.PartCount || ConstructLifters.DefOf(hb, l.Part) is not { } ld)
                                continue;
                            ConstructLifters.Frame(hb, l.Part, ld, out var lp, out var lr, out _);
                            var hw = lp + Vector3.Transform(l.Moved ? ConstructLifters.Pose(ld, l, ConstructLifters.AgeNow(l), out _) : ConstructLifters.RestOf(ld), lr); // (where the piece is now, as it is drawn)
                            var over = ConstructLifters.Altitude(hw, out var upAt); // (the ground under it along its up: a planetoid's curve, a terrain's floor)
                            var piece = l.Def != 0 ? ConstructParts.Def(l.Def) : null;
                            WsSay(string.Create(ci, $"hands: {hb.DefOf(l.Part).Name} (body {hb.Id}): {l.State} {l.Why}, piece origin at {hw:0.00}, {over:0.00} m over the ground under it (up {upAt:0.00}{(World.Map.Planetoid != null ? $", planetoid {World.Map.Name}" : "")}){(piece != null ? $", holding {piece.Name} ({piece.BoundsMax.Y - piece.BoundsMin.Y:0.00} m tall)" : "")}, moves {l.Moves}"));
                        }
                return true;
            case "cabto": // "cabto PART[#n]": seated in a Lifter Cab, the look turned toward that part (as the mouse turns it) and W held until the hook is within 0.25 m of over it (20 s at most)
            {
                ref var h = ref g.SandboxHands.Hand(me.Id);
                var (tc, tp) = WsFind(g, arg);
                if (!h.Seated || g.Constructs.Get(h.SeatBody) is not { } cc || h.SeatPart >= cc.PartCount || ConstructLifters.CabLifter(g.Constructs, cc, h.SeatPart) is not { } cl
                    || g.Constructs.Get(cl.Body) is not { } lb || ConstructLifters.DefOf(lb, cl.Part) is not { } ld || tc == null)
                {
                    WsFail($"cabto: not seated in a Lifter Cab with a lifter, or no part \"{arg}\"");
                    return true;
                }
                ConstructLifters.Frame(lb, cl.Part, ld, out var lp, out var lr, out _);
                var hook = lp + Vector3.Transform(cl.Moved ? ConstructLifters.Pose(ld, cl, ConstructLifters.AgeNow(cl), out _) : ConstructLifters.RestOf(ld), lr);
                var target = WsPartSpot(tc, tp, out _);
                var off = new Vector3(target.X - hook.X, 0, target.Z - hook.Z);
                if (off.Length() < 0.25f || !WsLiftWait(20))
                {
                    _wsKeys.Clear();
                    _wsLiftSince = 0;
                    WsSay(string.Create(ci, $"cabto: the hook at {hook:0.00}, {off.Length():0.00} m across from over the {tc.DefOf(tp).Name} at {target:0.00}"));
                    _wsWait = 10;
                    return true;
                }
                WsLook(g, me.Eye, me.Eye + off); // (the mouse's turn: W moves the hook along the sitter's look)
                _wsKeys.Clear();
                _wsKeys.Add(GameAction.MoveForward);
                _wsKeysUntil = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency / 10 * (off.Length() < 1 ? 1 : 3); // (short taps near it)
                _wsQueue.EnqueueFront($"cabto {arg}");
                _wsWait = off.Length() < 1 ? 8 : 12;
                return true;
            }
            case "damage" when g.IsServer: // host: "damage PART[#n] HP": that damage dealt to the part as a round's hit deals it (armour first; at 0 it is wrecked, breaks off or stops)
            {
                var sp = arg.LastIndexOf(' ');
                var (dc, dp) = sp > 0 ? WsFind(g, arg[..sp]) : (null, -1);
                if (dc == null || !float.TryParse(arg[(sp + 1)..], System.Globalization.NumberStyles.Float, ci, out var hp))
                {
                    WsFail($"damage PART[#n] HP (on the host): \"{arg}\"");
                    return true;
                }
                var name = dc.DefOf(dp).Name;
                var before = dc.Parts[dp].Health;
                var broke = g.Constructs.DamagePart(dc, dp, hp, out var dealt);
                WsSay(string.Create(ci, $"damage: {name} (body {dc.Id} part {dp}): {dealt:0.#} dealt of {hp:0.#}, health {before:0.#} -> {(broke ? 0 : dc.Parts[dp].Health):0.#}{(broke ? ", wrecked or broken off" : "")}"));
                _wsWait = 3;
                return true;
            }
            case "makeengine": // "makeengine KW TIER [car|any]": the creative Engine tab's MAKE (the same call: prepared, then its placing ghost; then "waitghost; key Fire"): "car" is the tab's Car bay ticked, the default
            {
                var bits = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (bits.Length < 2 || !int.TryParse(bits[0], out var kw) || !int.TryParse(bits[1], out var tier))
                {
                    WsFail($"makeengine KW TIER [car|any] (\"{arg}\")");
                    return true;
                }
                var bay = bits.Length < 3 || bits[2] == "car" ? EngineKit.EngineKitVehicle.Bay : Vector3.Zero;
                g.SandboxMakeEngine(kw, tier, default, default, -1, bay, 0, ghost: true); // (the tab's MAKE: prepared on the host, then a placing ghost; place it with "waitghost; key Fire")
                WsSay(string.Create(ci, $"makeengine: asked for {kw} kW, tier {tier}, {(bay == Vector3.Zero ? "no bay" : $"the Car bay {bay.X:0.00} x {bay.Y:0.00} x {bay.Z:0.00} m")}"));
                _wsWait = 10;
                return true;
            }
            case "standon" when g.IsServer: // host, a test state: "standon NAME | PART[#n] SOCKET BY": a new body of NAME stood with its socket BY on PART's free SOCKET, not joined (as a free placement leaves it), for the Welder's join
            {
                var bar = arg.IndexOf('|');
                var rest = bar < 0 ? "" : arg[(bar + 1)..].Trim();
                var bits = rest.Split(' ');
                var (tc, tp) = bits.Length >= 3 ? WsFind(g, string.Join(' ', bits[..^2])) : (null, -1);
                if (bar < 0 || tc == null || ConstructParts.Find(arg[..bar].Trim()) is not { } sd)
                {
                    WsFail($"standon NAME | PART[#n] SOCKET BY (\"{arg}\")");
                    return true;
                }
                var ts = Array.FindIndex(tc.DefOf(tp).Sockets, x => x.Name == bits[^2]);
                var bs = Array.FindIndex(sd.Sockets, x => x.Name == bits[^1]);
                if (ts < 0 || bs < 0)
                {
                    WsFail($"standon: no socket {(ts < 0 ? bits[^2] : bits[^1])}");
                    return true;
                }
                tc.SocketAt(tp, ts, out var at, out _);
                var rot = tc.Rot;
                var body = ConstructBuild.PlaceNew(g.Constructs, sd, new FreePlacement { Pos = tc.ToWorld(at) - Vector3.Transform(sd.Sockets[bs].At, rot), Rot = rot, Anchor = true }); // (pinned: a loose one is nudged off the socket by the contact within a few frames)
                if (body != null)
                {
                    body.SocketAt(0, bs, out var sat, out _);
                    body.Pos += tc.ToWorld(at) - body.ToWorld(sat); // (PlaceNew sets a free part down by its own rule: the socket put exactly on the other)
                    body.Wake();
                    body.SocketAt(0, bs, out var sat2, out var sdir);
                    tc.SocketAt(tp, ts, out _, out var tdir);
                    WsSay($"standon: gap {Vector3.Distance(body.ToWorld(sat2), tc.ToWorld(at)) * 100:0.0} cm; its socket {sd.Sockets[bs].Kind} class {sd.Sockets[bs].Class} dir {sdir}, the other's {tc.DefOf(tp).Sockets[ts].Kind} class {tc.DefOf(tp).Sockets[ts].Class} dir {tdir}; used {tc.SocketUsed(tp, ts)}; a join now: {ConstructPlace.FindJoin(g.Constructs, body, 0, out _)} (from it), {ConstructPlace.FindJoin(g.Constructs, tc, tp, out _, body)} (from the other); turn match {(Rot24.Nearest(Quaternion.Conjugate(body.Rot) * tc.Rot, out var mt) is var _ ? mt : 0):0.0000}");
                }
                WsSay($"standon: {sd.Name} on {tc.DefOf(tp).Name} {bits[^2]}: {(body == null ? "refused" : $"body {body.Id} (a separate construct, {g.Constructs.Bodies.Count} bodies)")}");
                _wsWait = 5;
                return true;
            }
            case "aimintake": // "aimintake [UP]": the look turned to UP m over the middle of the first Recycler's intake (as the mouse turns it: a physics gun's held piece follows the look)
            {
                var upBy = float.TryParse(arg, System.Globalization.NumberStyles.Float, ci, out var ub) ? ub : 0f;
                var (ic, ip) = WsFind(g, "Recycler");
                if (ic == null || !ic.DefOf(ip).IsRecycler)
                {
                    WsFail($"aimintake: no Recycler \"{arg}\"");
                    return true;
                }
                ref var ipart = ref ic.Parts[ip];
                ic.WorldFrame(ipart.Seg, out var iq, out var it);
                var imid = it + Vector3.Transform(ipart.Pos + Rot24.Apply(ipart.Rot, (ic.DefOf(ip).IntakeMin + ic.DefOf(ip).IntakeMax) * 0.5f), iq);
                imid += GravityFrames.UpAt(imid) * upBy;
                WsLook(g, me.Eye, imid);
                WsSay(string.Create(ci, $"aimintake: the intake's middle at {imid:0.00}, {Vector3.Distance(me.Eye, imid):0.0} m from the eye"));
                _wsWait = 2;
                return true;
            }
            case "bringon": // host: "bringon PART[#n]": every other player set down on that part's top, moving as its construct moves there (a respawn aboard: ConstructRide.SpawnVel)
            {
                var (bc, bp) = WsFind(g, arg);
                if (bc == null || !g.IsServer)
                {
                    WsFail($"bringon: no part \"{arg}\" (or not the host)");
                    return true;
                }
                bc.PartBounds(bp, out var blo, out var bhi);
                var top = bc.ToWorld(new Vector3((blo.X + bhi.X) * 0.5f, bhi.Y + 0.05f, (blo.Z + bhi.Z) * 0.5f));
                foreach (var other in g.Players.Values)
                    if (other != me)
                    {
                        other.Teleport(top);
                        other.Sim.Velocity = ConstructRide.SpawnVel(g.Constructs, top, bc);
                        other.Sim.Grounded = false;
                    }
                WsSay(string.Create(ci, $"bringon: to {top:0.0} on construct {bc.Id} at {bc.Vel.Length():0.0} m/s"));
                _wsWait = 5;
                return true;
            }
            case "dropmotion": // "dropmotion": the newest body's speed against the construct this player rides (a part dropped aboard: did it come into the ship's motion?)
            {
                var nb = g.Constructs.Bodies.Count > 0 ? g.Constructs.Bodies.MaxBy(b => b.Id) : null; // (the newest: the highest id)
                var deck = g.Constructs.Get(me.Sim.RideBody);
                WsSay(string.Create(ci, $"dropmotion: newest body {nb?.Id} at {nb?.Vel.Length():0.00} m/s; the deck {(deck == null ? "none" : $"{deck.Id} at {deck.PointVel(nb?.WorldCom ?? me.Position).Length():0.00} m/s there")}"));
                return true;
            }
            case "feetup": // "feetup UP": this player's feet UP m higher, out of the tool's flight, to fall onto whatever is below (a pad's deck: riding it)
            {
                var upBy = float.TryParse(arg, System.Globalization.NumberStyles.Float, ci, out var fu) ? fu : 0.5f;
                me.NoclipWanted = false;
                g.NoclipShot = false;
                me.Teleport(me.Position + GravityFrames.UpAt(me.Position) * upBy);
                WsSay(string.Create(ci, $"feetup: feet to {me.Position:0.00}"));
                _wsWait = 30;
                return true;
            }
            case "cave": // "cave [N]": the planetoid's N-th cave (0 first) as this peer's ground has it; the host stands its player on its floor, out of the flight (run 7: a Lifter Cab under a roof)
            {
                var cw = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var cn = cw.Length > 0 && int.TryParse(cw[0], out var cnn) ? cnn : 0;
                if (World.Map.Planetoid is not { } cpl || PlanetCaves.Of(cpl).Skip(cn).FirstOrDefault() is not { } cf)
                {
                    WsFail($"cave: no cave {cn} on {World.Map.Name}");
                    return true;
                }
                var plan = PlanetCaves.Dig(cpl, cf);
                var mid = plan.Chamber;
                var floor = cpl.GroundUnder(mid); // (the dug floor under the chamber's middle, not the world's top)
                var cup = GravityFrames.UpAt(floor);
                if (g.IsServer)
                {
                    me.NoclipWanted = false;
                    g.NoclipShot = false;
                    me.Teleport(floor + cup * 0.05f);
                }
                WsSay(string.Create(ci, $"cave: {cn} at {cf.Num("lat", 0):0.00},{cf.Num("lon", 0):0.00}: chamber middle {mid:0.0}, floor {floor:0.0} ({Vector3.Distance(mid, floor):0.0} m under it), {cpl.Altitude(floor + cup):0.00} m over the ground 1 m up; the top {cpl.SurfaceRadius(Vector3.Normalize(floor - cpl.Center), out _) - Vector3.Distance(floor, cpl.Center):0.0} m over the floor; up {cup:0.00}; dug ground: {(cpl.EditsIfMade is { } ce ? $"any {ce.Any}, version {ce.Version}, generation {ce.Generation}" : "none")}, planetoid {System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(cpl)}; ops logged {g.DigOps}, read {g.DigOpsRead}"));
                _wsWait = 30;
                return true;
            }
            case "spawnbp": // "spawnbp NAME": CActKind.Blueprint, the direct spawn in front of this player that tools and tests use (not the parts menu's path: that is "ghost NAME", its placing ghost, then Fire)
            {
                var all = ConstructBuild.Blueprints;
                var bi = -1;
                for (var k = 0; k < all.Count && bi < 0; k++)
                    if (all[k].Name.Equals(arg.Trim(), StringComparison.OrdinalIgnoreCase))
                        bi = k;
                if (bi < 0)
                {
                    WsFail($"spawnbp: no blueprint \"{arg.Trim()}\"");
                    return true;
                }
                g.SandboxSend(CActKind.Blueprint, part: (ushort)bi);
                WsSay($"spawnbp: {all[bi].Name} asked for, feet {me.Position:0.00}");
                _wsWait = 60;
                return true;
            }
            case "partlook":
                WsSay($"partinfo: look: {g.SandboxLookText}");
                return true;
            case "modsock" when arg.StartsWith("style ", StringComparison.OrdinalIgnoreCase): // host: "modsock style N [DEPTH]": every Module Socket set to style N (0 proud, 1 flush, 2 well, 3 opening: the frame that punches through), as the configure tool sets it
            {
                var w = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var style = w.Length > 1 ? int.Parse(w[1], System.Globalization.CultureInfo.InvariantCulture) : 0;
                var depth = w.Length > 2 ? float.Parse(w[2], System.Globalization.CultureInfo.InvariantCulture) : -1f;
                if (!g.IsServer)
                {
                    WsFail("modsock style: on the host");
                    return true;
                }
                foreach (var mb in g.Constructs.Bodies)
                    for (var i = 0; i < mb.PartCount; i++)
                    {
                        var md = mb.DefOf(i);
                        if (md.ModPart != ModPart.Socket || md.ModStand)
                            continue;
                        ConstructConfig.Put(ref mb.Parts[i], md, CfgKey.ModStyle, style);
                        if (depth > 0)
                            ConstructConfig.Put(ref mb.Parts[i], md, CfgKey.ModDepth, depth);
                        mb.Dirty = true;
                        g.Constructs.StructureDirty.Add(mb.Id);
                        WsSay($"modsock: body {mb.Id} socket part {i} set to style {style}");
                    }
                return true;
            }
            case "modsock" when arg.StartsWith("beside ", StringComparison.OrdinalIgnoreCase): // host, a test state: "modsock beside A | B | DX DY DZ": B's construct set still, turned as A's, with part B DX DY DZ (A's frame) from part A: a ship pulled in beside a dock's Module Fitter
            {
                var w = arg[7..].Split('|', StringSplitOptions.TrimEntries);
                var d = w.Length == 3 ? w[2].Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];
                var (ac, ap) = d.Length == 3 ? WsFind(g, w[0]) : (null, -1);
                var (bc, bp) = d.Length == 3 ? WsFind(g, w[1]) : (null, -1);
                if (!g.IsServer || ac == null || bc == null || ac == bc)
                {
                    WsFail("modsock beside: A | B | DX DY DZ, two parts on two constructs, on the host");
                    return true;
                }
                var off = new Vector3(float.Parse(d[0], ci), float.Parse(d[1], ci), float.Parse(d[2], ci));
                bc.Rot = bc.PrevRot = bc.NetRot = bc.NetFromRot = ac.Rot;
                bc.Pos = bc.PrevPos = bc.NetPos = bc.NetFromPos = ac.ToWorld(ac.Parts[ap].Pos + off) - Vector3.Transform(bc.Parts[bp].Pos, bc.Rot);
                bc.Vel = bc.AngVel = Vector3.Zero;
                bc.Wake();
                WsSay($"modsock: body {bc.Id} set beside body {ac.Id}: part {bp} at {bc.ToWorld(bc.Parts[bp].Pos):0.00}");
                return true;
            }
            case "modsock" when arg.StartsWith("use ", StringComparison.OrdinalIgnoreCase): // host: "modsock use PART": F on that plate or module part (take off, or fit what is near), whatever the crosshair finds
            {
                var (uc, upart) = WsFind(g, arg[4..].Trim());
                if (!g.IsServer || uc == null)
                {
                    WsFail($"modsock use: {(uc == null ? $"no part \"{arg[4..].Trim()}\"" : "on the host")}");
                    return true;
                }
                WsSay($"modsock: use on {uc.DefOf(upart).Name} part {upart}: {g.SandboxHands.ModuleUseNow(g.Constructs, me.Id, uc, upart) ?? "done"}");
                _wsWait = 20;
                return true;
            }
            case "modsock" when arg.StartsWith("ctl ", StringComparison.OrdinalIgnoreCase): // host: "modsock ctl PART N": a plate's control (1 eject, 2 lock, 3 the gun's pull on that module part begins, 4 ends), as F or the gun sends it (ModuleGuide.cs)
            {
                var cw = arg[4..].Trim();
                var sp = cw.LastIndexOf(' ');
                var (cc, cpart) = sp > 0 ? WsFind(g, cw[..sp].Trim()) : (null, -1);
                if (!g.IsServer || cc == null || !byte.TryParse(cw[(sp + 1)..], out var ctl))
                {
                    WsFail("modsock ctl: PART N, on the host");
                    return true;
                }
                var cat = cc.ToLocal(cc.SegToWorld(cc.Parts[cpart].Seg, cc.Parts[cpart].Pos));
                WsSay($"modsock: control {ctl} on {cc.DefOf(cpart).Name} part {cpart}: {g.SandboxHands.ModuleControl(g.Constructs, me.Id, cc.Id, cpart, ctl, me.Eye, cat) ?? "done"}");
                _wsWait = 10;
                return true;
            }
            case "modsock" when arg.StartsWith("attach ", StringComparison.OrdinalIgnoreCase): // host: "modsock attach PLATE": as a rise on its Attach wire (the nearest loose module drawn in, or the fitted one ejected)
            {
                var (ac2, apart) = WsFind(g, arg[7..].Trim());
                var asi = ac2 != null ? ModuleSockets.SocketOfPlate(ac2, apart) : -1;
                if (!g.IsServer || ac2 == null || asi < 0)
                {
                    WsFail("modsock attach: a socket's plate, on the host");
                    return true;
                }
                WsSay($"modsock: attach on part {apart}: {(ac2.ModSockets[asi].Module >= 0 ? g.SandboxHands.ModuleEject(g.Constructs, 255, ac2, asi) : ModuleGuide.AttachNear(g.Constructs, ac2, asi)) ?? "done"}");
                _wsWait = 10;
                return true;
            }
            case "modsock" when arg.StartsWith("fit ", StringComparison.OrdinalIgnoreCase): // host: "modsock fit PART": the nearest loose module of that plate's size held 0.6 m off its face, then F on the plate
            {
                var (fc, fpart) = WsFind(g, arg[4..].Trim());
                if (!g.IsServer || fc == null)
                {
                    WsFail($"modsock fit: {(fc == null ? $"no part \"{arg[4..].Trim()}\"" : "on the host")}");
                    return true;
                }
                WsSay($"modsock: fit on {fc.DefOf(fpart).Name} part {fpart}: {g.SandboxHands.ModuleFitNear(g.Constructs, me.Id, fc, fpart) ?? "done"}");
                _wsWait = 20;
                return true;
            }
            case "modsock": // "modsock state": every body's module sockets and modules as this peer has them, and the notice on screen (module-sockets.md 15)
                foreach (var mb in g.Constructs.Bodies)
                {
                    if (mb.Dirty)
                        mb.Rebuild();
                    foreach (var ms in mb.ModSockets)
                        WsSay($"modsock: body {mb.Id} socket part {ms.Plate} {ModuleSocketTable.SizeText(ms.W, ms.L, ms.Round)} {ms.Kind} {ms.Style}{(ms.Keyed ? " keyed" : "")}{(ms.Stand ? " stand" : "")} above {ms.Above:0.##}: module {ms.Module}, in the way {ms.InTheWay}{(ms.WrongKind ? ", wrong kind" : "")}; depth {ms.Depth:0.##}, behind {ms.Behind:0.##}, room {ms.RoomMin} to {ms.RoomMax}, cut {ConstructConfig.Get(mb.Parts[ms.Plate], mb.DefOf(ms.Plate), CfgKey.ModCut, 0):0.#} s; look \"{ModuleSockets.LookText(mb, ms.Plate)}\"");
                    if (mb.ModSockets.Length > 0)
                        for (var rk = 0; rk < mb.RouteCount; rk++)
                            if (mb.Routes[rk].Type is RouteType.Pipe or RouteType.Feed or RouteType.Chute)
                                WsSay($"modsock: body {mb.Id} {ConstructRoutes.TypeKey(mb.Routes[rk].Type)} {rk}: {mb.DefOf(mb.Routes[rk].From).Name} -> {mb.DefOf(mb.Routes[rk].To).Name}, source {mb.DefOf(mb.Routes[rk].Source).Name}, fed {mb.Routes[rk].FedEnd}");
                    if (mb.ModSockets.Length > 0)
                        for (var wk = 0; wk < mb.WireCount; wk++)
                            WsSay($"modsock: body {mb.Id} wire {wk}: {mb.DefOf(mb.Wires[wk].From).Name}#{mb.Wires[wk].From} port {mb.Wires[wk].FromPort} -> {mb.DefOf(mb.Wires[wk].To).Name}#{mb.Wires[wk].To} port {mb.Wires[wk].ToPort}, out {mb.Parts[mb.Wires[wk].From].Out:0.##}, to's out {mb.Parts[mb.Wires[wk].To].Out:0.##}");
                    if (mb.ModSockets.Length > 0)
                        for (var gk = 0; gk < mb.GunCount; gk++)
                            WsSay($"modsock: body {mb.Id} gun {gk}: {mb.DefOf(mb.Guns[gk].Part).Name}, ring {mb.Guns[gk].Ring}, ring fed {mb.Guns[gk].RingFed}, fed {(ConstructAmmo.FedFrom(g.Constructs, mb, gk, out var fs, out var fp) && fs != null && fp >= 0 ? fs.DefOf(fp).Name : "no")}");
                    if (mb.ModSockets.Length > 0)
                        WsSay($"modsock: body {mb.Id}: {mb.PartCount} parts, power {(mb.PowerOn ? "on" : "off")}, draw {mb.PowerUse / 1000f:0.#} kW (cutting {mb.ModCutWatts / 1000f:0.#} kW)");
                    for (var mi = 0; mi < mb.Modules.Length; mi++)
                    {
                        var mm = mb.Modules[mi];
                        WsSay($"modsock: body {mb.Id} module {mi}: plug part {mm.Plug}, socket {mm.Socket}{(mm.OnStand ? " (on a stand)" : "")}{(mm.Bridged ? " BRIDGED" : "")}, {mm.Parts} parts, {mm.Mass:0} kg, tags {ModuleSockets.TagsText(mm.Tags)}, primary {ModuleSockets.TagName(mm.Primary)}{ModuleSockets.StatsText(mm)}; bounds {mm.Min:0.00} to {mm.Max:0.00}; plug at {mb.ToWorld(mb.Parts[mm.Plug].Pos):0.00}, held by {mb.GrabbedBy}");
                    }
                }
                WsSay($"modsock: notice \"{g.SandboxNotice}\"");
                return true;
        }
        return false;
    }

    private static void WsClip(Game g, Player me, string arg)
    {
        var w = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
        var rest = w.Length > 1 ? w[1] : "";
        var hands = g.SandboxHands;
        var inv = g.SandboxInventory(me.Id);
        switch (w[0].ToLowerInvariant())
        {
            case "copy" or "paste" when hands.Hand(me.Id).Tool != ConstructTool.Selector && !_wsClipToolAsked:
                g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Selector); // (the tool wheel's pick: the selector, which both need)
                _wsClipToolAsked = true;
                _wsQueue.EnqueueFront($"clip {arg}");
                _wsWait = 10;
                return;
            case "copy":
            {
                _wsClipToolAsked = false;
                var range = rest.LastIndexOf(' ') is var sp and > 0 && rest[(sp + 1)..].Split('-') is [var a, var b] && int.TryParse(a, out var from) && int.TryParse(b, out var to) ? (from, to) : (0, int.MaxValue);
                var spec = range.Item2 != int.MaxValue ? rest[..rest.LastIndexOf(' ')] : rest;
                var (c, _) = WsFind(g, spec);
                if (c == null)
                {
                    WsFail($"clip copy PART[#n] [FIRST-LAST]: no part \"{spec}\"");
                    return;
                }
                var items = new List<ushort>();
                for (var i = Math.Max(0, range.Item1); i <= Math.Min(c.PartCount - 1, range.Item2); i++)
                    items.Add((ushort)i);
                g.SandboxSelectSend(c, items.ToArray()); // (the selector's selection, in its chunks, then Copy: the server keeps the clip)
                g.SandboxSelectDo(SelAct.Copy);
                WsSay($"clip copy: {items.Count} of body {c.Id}'s {c.PartCount} parts sent and copied (tool {hands.Hand(me.Id).Tool})");
                _wsWait = 10;
                return;
            }
            case "paste":
            {
                _wsClipToolAsked = false;
                var bits = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var dist = bits.Length > 0 && float.TryParse(bits[0], System.Globalization.CultureInfo.InvariantCulture, out var dd) ? dd : 4f;
                var upBy = bits.Length > 1 && float.TryParse(bits[1], System.Globalization.CultureInfo.InvariantCulture, out var uu) ? uu : 1f;
                var yaw = g.LookYaw + (bits.Length > 2 && float.TryParse(bits[2], System.Globalization.CultureInfo.InvariantCulture, out var turn) ? turn * MathF.PI / 180 : 0); // (degrees right of the look)
                var fwd = new Vector3(MathF.Sin(yaw), 0, -MathF.Cos(yaw));
                var spot = me.Position + fwd * dist;
                spot.Y = World.Map.GroundHeight(spot.X, spot.Z) + upBy;
                _wsClipParts = new int[inv.Parts.Length];
                for (var i = 0; i < inv.Parts.Length; i++)
                    _wsClipParts[i] = inv.Parts[i];
                _wsClipMadeDef.Clear();
                _wsClipMade = new int[inv.MadeN];
                for (var k = 0; k < inv.MadeN; k++)
                {
                    _wsClipMadeDef.Add(inv.MadeDef[k]);
                    _wsClipMade[k] = inv.MadeCount[k];
                }
                _wsClipBodies = g.Constructs.Bodies.Count;
                _wsClipLoose = g.Constructs.Bodies.Count(b => b.Loose && Vector3.Distance(b.WorldCom, me.Position) < 12);
                g.SandboxPaste(new ClipPlace { Pos = spot, Rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -g.LookYaw) }); // (the selector's Paste: the server's reach, aid, limit and survival need checks)
                WsSay(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"clip paste: at {spot:0.00} ({dist:0.#} m ahead); inventory {inv.TotalParts} parts, {inv.MadeN} made kinds, {_wsClipBodies} bodies before"));
                _wsWait = 20;
                return;
            }
            case "cost":
            {
                if (_wsClipParts == null)
                {
                    WsFail("clip cost: no paste yet");
                    return;
                }
                var paid = new List<string>();
                for (var i = 0; i < inv.Parts.Length; i++)
                    if (inv.Parts[i] != _wsClipParts[i])
                        paid.Add($"{ConstructParts.Get((ushort)i)?.Name ?? $"#{i}"} {_wsClipParts[i] - inv.Parts[i]:+0;-0}");
                for (var k = 0; k < _wsClipMadeDef.Count; k++)
                {
                    var now = inv.Count(_wsClipMadeDef[k]);
                    if (now != _wsClipMade![k])
                        paid.Add($"made {ConstructParts.Def(_wsClipMadeDef[k]).Name} {_wsClipMade[k] - now:+0;-0}");
                }
                var loose = g.Constructs.Bodies.Count(b => b.Loose && Vector3.Distance(b.WorldCom, me.Position) < 12);
                var newest = g.Constructs.Bodies.Count > 0 ? g.Constructs.Bodies[^1] : null;
                WsSay($"clip cost: paid (taken out of the inventory: +) {(paid.Count == 0 ? "nothing" : string.Join(", ", paid))}; loose bodies near {_wsClipLoose} -> {loose}; bodies {_wsClipBodies} -> {g.Constructs.Bodies.Count}{(newest != null ? $", the newest body {newest.Id}: {newest.PartCount} parts" : "")}; notice: {g.SandboxNotice}");
                return;
            }
        }
        WsFail($"clip copy|paste|cost (\"{arg}\")");
    }
}
