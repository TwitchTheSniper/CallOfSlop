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
    private static int[]? _wsClipParts;
    private static int[]? _wsClipMade;
    private static readonly List<ushort> _wsClipMadeDef = [];
    private static int _wsClipBodies, _wsClipLoose;
    private static bool _wsClipToolAsked;

    private static bool WsPlayKnown(string action) => action.Split(' ', 2)[0].ToLowerInvariant() is "sit" or "seated" or "getout" or "recycle" or "clip" or "partinfo" or "partlook" or "hands" or "cabto" or "damage" or "makeengine";

    private static bool WsPlayVerb(Game g, Player me, string verb, string arg)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        switch (verb)
        {
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
            case "recycle": // host: "recycle NAME|KEY [@ RECYCLER[#n]]": that part wrecked by the damage path and left in the intake; "recycle read [RECYCLER[#n]]": the bin
            {
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
                            WsSay($"recycle: read: a wrecked {b.DefOf(0).Name} (body {b.Id}) at {b.WorldCom:0.00}, in the intake {ConstructRecycler.InIntake(rc, rp, b.WorldCom)}, feedable {ConstructRecycler.Feedable(b)}");
                    WsSay($"recycle: read: bin {st.Amount} {table.NameOf(st.Material)}; job {st.JobAmount} {table.NameOf(st.JobMaterial)}, {st.JobTicks / (float)World.TickRate:0.0} s of {st.JobFull / (float)World.TickRate:0.0} left, busy {st.Busy}, blocked {st.Blocked}, held {st.Held}; powered {ConstructRecycler.Powered(g.Constructs, rc, rp)}, line {ConstructRecycler.LineOf(g.Constructs, rc, rp, st)}; bodies in the intake {inside}");
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
                var mid = t + Vector3.Transform(rpart.Pos + Rot24.Apply(rpart.Rot, (rd.IntakeMin + rd.IntakeMax) * 0.5f), q);
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
            case "makeengine": // "makeengine KW TIER [car|any]": the creative Engine tab's request (its MAKE call, without the placing ghost): "car" is the tab's Car bay ticked, the default
            {
                var bits = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (bits.Length < 2 || !int.TryParse(bits[0], out var kw) || !int.TryParse(bits[1], out var tier))
                {
                    WsFail($"makeengine KW TIER [car|any] (\"{arg}\")");
                    return true;
                }
                var bay = bits.Length < 3 || bits[2] == "car" ? EngineKit.EngineKitVehicle.Bay : Vector3.Zero;
                g.SandboxMakeEngine(kw, tier, default, default, -1, bay, 0);
                WsSay(string.Create(ci, $"makeengine: asked for {kw} kW, tier {tier}, {(bay == Vector3.Zero ? "no bay" : $"the Car bay {bay.X:0.00} x {bay.Y:0.00} x {bay.Z:0.00} m")}"));
                _wsWait = 10;
                return true;
            }
            case "partlook":
                WsSay($"partinfo: look: {g.SandboxLookText}");
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
