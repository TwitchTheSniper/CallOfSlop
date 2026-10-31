using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Sandbox remover" (board item dev-sandbox-remover-tool-rt01; docs/sandbox/CONSTRUCTS.md, "The remover tool"): the
    /// tool's wheel segment, model and wire byte; selecting through the real input path (the controller Program drives,
    /// with scripted keys and the game's own crosshair ray): click, click again, Shift+click, a sweep that marks each
    /// part once and a stationary press that doesn't flip, a double-click on a construct, Shift+RMB to clear; the RMB
    /// question next to the crosshair (deleting nothing) and the second RMB within 4 s deleting exactly the marked parts
    /// on the server, the cut-off ends becoming wrecks of their own; LMB, Esc, the timeout and putting the tool away
    /// cancelling the question and keeping the selection; routed links touching a deleted part going, and a marked link
    /// deleted alone; a deleted chest's contents spilling as pickups; creative free, survival only on your own, loose
    /// pieces and wrecks (never someone else's, never a defended one), nothing handed back, the host's switch; a few hundred
    /// parts in one go inside the tick budget; no allocation per frame; and a real host with a separate client agreeing.
    /// </summary>
    private static void CheckSandboxRemover()
    {
        var problems = new List<string>();
        RemoverBasics(problems);
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail($"remover: the {ConstructYardMap} map is missing");
            return;
        }
        var sw = Stopwatch.StartNew();
        RemoverOffline(yard, sb, problems);
        var offline = sw.Elapsed.TotalSeconds;
        RemoverBatch(problems);
        var batch = sw.Elapsed.TotalSeconds - offline;
        RemoverLoopback(yard, problems);
        Line($"remover: timings: offline {offline:0.0} s, batch {batch:0.0} s, loopback {sw.Elapsed.TotalSeconds - offline - batch:0.0} s");
        if (problems.Count > 0)
            Fail($"remover: {string.Join("; ", problems)}");
    }

    private static void RemoverBasics(List<string> problems)
    {
        var slot = (int)SandboxToolWheel.Slot.Remover;
        var wheel = new SandboxToolWheel();
        var model = ConstructTools.For(ConstructTool.Remover);
        var ok = SandboxToolWheel.ToolOf(slot) == ConstructTool.Remover && SandboxToolWheel.SlotOf(ConstructTool.Remover) == slot && wheel.Menu.Items[slot].Enabled
                 && ConstructToolWire.From((byte)ConstructTool.Remover) == ConstructTool.Remover && model is { Boxes.Length: > 3 } && model.Key == "remover";
        Line($"remover basics: wheel slot {slot} ({wheel.Menu.Items[slot].Label}), wire byte {(byte)ConstructTool.Remover}, model {model?.Boxes.Length ?? 0} boxes; protocol {ServerInfo.ProtocolVersion}");
        if (!ok)
            problems.Add("the wheel segment, wire byte or held model is missing");
    }

    // ---- Scene helpers -----------------------------------------------------------------------------------------

    private static readonly Vector3[] RmEyes = [new(0, 0.4f, 1.2f), new(0, 0.4f, -1.2f), new(1.2f, 0.4f, 0), new(-1.2f, 0.4f, 0), new(0, 1.5f, 0.01f), new(0, -0.3f, 1.2f)];

    /// <summary>A chain of beams: each new beam on the last part with a free fitting socket (a path, so a middle part's removal cuts it).</summary>
    private static Construct? RmChain(ConstructWorld w, Vector3 at, int n, byte owner)
    {
        var beam = ConstructParts.Need("Square Beam 0.5 m");
        var c = ConstructBuild.PlaceNew(w, beam, at, 0);
        if (c == null)
            return null;
        c.Owner = owner;
        for (var k = 1; k < n; k++)
        {
            var done = false;
            for (var on = c.PartCount - 1; on >= 0 && !done; on--)
            for (var ts = 0; ts < c.DefOf(on).Sockets.Length && !done; ts++)
            for (var ds = 0; ds < beam.Sockets.Length && !done; ds++)
                if (ConstructBuild.Place(c, on, ts, beam, ds, 0, out var g) == null && ConstructBuild.Attach(w, c, beam, g) >= 0)
                {
                    c.Rebuild();
                    done = true;
                }
            if (!done)
                return c.PartCount >= 2 ? c : null;
        }
        return c;
    }

    private static RemoverInput RmIn(long t, bool down = false, bool pressed = false, bool rmb = false, bool shift = false) =>
        new() { FireDown = down || pressed, FirePressed = pressed, AimPressed = rmb, Shift = shift, NowMs = t };

    /// <summary>One frame of the remover aimed at part <paramref name="part"/> of <paramref name="c"/> (an eye by it that sees it); false when no ray finds it.</summary>
    private static bool RmAim(Game g, Player me, Construct c, int part, in RemoverInput inp)
    {
        var target = ConstructRender.PartCentre(c, part);
        foreach (var off in RmEyes)
        {
            var eye = target + off;
            var dir = Vector3.Normalize(target - eye);
            if (g.Constructs.Raycast(eye, dir, 3f, out var hit) && hit.Body == c && hit.Part == part && hit.RoutePlus1 == 0)
            {
                Program.SandboxRemoverFrame(g, me, in inp, eye, dir);
                return true;
            }
        }
        return false;
    }

    /// <summary>A frame with the crosshair on nothing (up at the sky).</summary>
    private static void RmSky(Game g, Player me, in RemoverInput inp) => Program.SandboxRemoverFrame(g, me, in inp, me.Eye + new Vector3(0, 40, 0), Vector3.UnitY);

    private static bool RmClick(Game g, Player me, Construct c, int part, ref long t, bool shift = false)
    {
        var a = RmAim(g, me, c, part, RmIn(t, pressed: true, shift: shift));
        t += 40;
        var b = RmAim(g, me, c, part, RmIn(t, shift: shift));
        t += 500; // (apart enough that two clicks are never a double-click)
        return a && b;
    }

    private static int RmLive(ConstructWorld w, HashSet<ushort> ids) => w.Bodies.Where(b => ids.Contains(b.Id)).Sum(b => b.PartCount);

    // ---- Offline game ------------------------------------------------------------------------------------------

    private static void RemoverOffline(Map yard, SandboxMap sb, List<string> problems)
    {
        yard.RestoreProps();
        World.SetMap(yard);
        sb.DebrisSeconds = 0;
        var wasInfinite = sb.InfiniteParts;
        var wasSwitch = sb.RemoverInSurvival;
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        if (SpawnLocalSafely(g, ref now) is { } notReady)
        {
            Fail($"remover: {notReady}");
            return;
        }
        var me = g.Local!;
        var w = g.Constructs;
        var rm = Program.Remover;
        Program.SandboxRemoverTick(g, toolOut: true); // (a new game forgets the selection; this one is now the game)
        rm.Reset();
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Remover);
        Run(g, ref now, 0.1);
        if (g.SandboxHands.Hand(me.Id).Tool != ConstructTool.Remover)
        {
            problems.Add("the server did not take the remover out");
            return;
        }
        if (OpenSpot(w) is not { } spot)
        {
            Fail("remover: no open spot on the yard");
            return;
        }
        long t = 100_000;
        try
        {
            // ---- Select 3 parts (one in the middle of a chain), the question deletes nothing, the second RMB deletes exactly those ----
            var chain = RmChain(w, spot, 7, me.Id);
            if (chain == null || chain.PartCount < 7)
            {
                Fail("remover: the beam chain didn't build");
                return;
            }
            var ids = new HashSet<ushort> { chain.Id };
            var before = w.Bodies.Count;
            StandBy(me, chain.WorldCom, 2.2f, chain.WorldCom + new Vector3(0, 0, -1));
            Run(g, ref now, 0.2);
            var sel = rm.Selection;
            var pick = new[] { 2, 4, 5 };
            var defsPicked = pick.Select(i => chain.Parts[i].Def).ToArray();
            var posPicked = pick.Select(i => chain.Parts[i].Pos).ToArray();
            var clicks = pick.All(i => RmClick(g, me, chain, i, ref t));
            var three = sel.Count == 3 && sel.PartCount == 3 && g.SandboxRemoverHint.Contains("3 parts selected");
            // Click a marked one: it is unmarked; click again: marked; Shift+click unmarks too.
            RmClick(g, me, chain, 5, ref t);
            var off = sel.Count == 2 && !sel.Contains(chain.Id, false, 5);
            RmClick(g, me, chain, 5, ref t);
            RmClick(g, me, chain, 4, ref t, shift: true);
            var shiftOff = sel.Count == 2 && !sel.Contains(chain.Id, false, 4);
            RmClick(g, me, chain, 4, ref t);
            var again = sel.Count == 3;
            // A stationary press: held still for 2 s on an unmarked part, it is marked once and stays (a stationary press doesn't flip it every frame).
            var held = RmAim(g, me, chain, 6, RmIn(t, pressed: true));
            for (var f = 0; f < 120; f++)
            {
                t += 17;
                RmAim(g, me, chain, 6, RmIn(t, down: true));
            }
            var stays = sel.Count == 4 && sel.Contains(chain.Id, false, 6);
            t += 20;
            RmAim(g, me, chain, 6, RmIn(t));
            t += 100;
            RmClick(g, me, chain, 6, ref t); // (a new click on a marked part unmarks it)
            var back = sel.Count == 3 && !sel.Contains(chain.Id, false, 6);
            if (!clicks || !three || !off || !shiftOff || !again || !held || !stays || !back)
                problems.Add($"selecting went wrong (clicks {clicks}, 3 selected {three}, toggle off {off}, shift off {shiftOff}, back on {again}, stationary press {held}/{stays}, click unmarks {back}; hint \"{g.SandboxRemoverHint}\")");
            // RMB: the question, nothing deleted.
            var q0 = chain.PartCount;
            RmSky(g, me, RmIn(t, rmb: true));
            var asked = rm.PromptPending && g.SandboxPrompt == "Delete 3 parts? Right-click again to confirm" && chain.PartCount == q0 && w.Bodies.Count == before;
            Run(g, ref now, 0.1);
            // LMB cancels it and keeps the selection (that click doesn't mark anything).
            t += 500;
            RmAim(g, me, chain, 0, RmIn(t, pressed: true));
            var lmbCancel = !rm.PromptPending && g.SandboxPrompt.Length == 0 && sel.Count == 3 && !sel.Contains(chain.Id, false, 0);
            t += 40;
            RmSky(g, me, RmIn(t));
            // Esc cancels it too.
            t += 200;
            RmSky(g, me, RmIn(t, rmb: true));
            var up2 = rm.PromptPending;
            var escCancel = up2 && rm.Cancel(g) && !rm.PromptPending && sel.Count == 3 && !rm.Cancel(g);
            // The timeout: after 4 s the question is gone, and a right-click then asks again instead of deleting.
            t += 200;
            RmSky(g, me, RmIn(t, rmb: true));
            t += RemoverController.ConfirmMs + 200;
            RmSky(g, me, RmIn(t));
            var timedOut = !rm.PromptPending && sel.Count == 3 && chain.PartCount == q0;
            t += 50;
            RmSky(g, me, RmIn(t, rmb: true));
            var reasked = rm.PromptPending && chain.PartCount == q0;
            // Putting the tool away ends it (Program's per-frame tick), the selection stays.
            Program.SandboxRemoverTick(g, toolOut: false);
            var toolAway = !rm.PromptPending && g.SandboxPrompt.Length == 0 && sel.Count == 3;
            Program.SandboxRemoverTick(g, toolOut: true);
            Run(g, ref now, 0.1);
            if (!asked || !lmbCancel || !escCancel || !timedOut || !reasked || !toolAway)
                problems.Add($"the delete question went wrong (asked {asked}, LMB cancel {lmbCancel}, Esc cancel {escCancel}, timeout {timedOut}, asks again {reasked}, tool away {toolAway})");
            // The question again (the tool is back out), then the second RMB within 4 s deletes exactly the three.
            t += 50;
            RmSky(g, me, RmIn(t, rmb: true));
            var askedAgain = rm.PromptPending && chain.PartCount == q0;
            t += 1500;
            RmSky(g, me, RmIn(t, rmb: true));
            Run(g, ref now, 0.2);
            if (!askedAgain)
                problems.Add("the question didn't come back after the tool was put away and taken out");
            var main = w.Get(chain.Id);
            var pieces = w.Bodies.Where(b => b != main && b.Wreck && !before0(b)).ToList();
            bool before0(Construct b) => b.Id < chain.Id;
            var gone = main != null && sel.Count == 0 && !rm.PromptPending;
            var total = RmLive(w, new HashSet<ushort>(w.Bodies.Where(b => b == main || pieces.Contains(b)).Select(b => b.Id)));
            var split = main != null && w.Bodies.Count == before + pieces.Count && pieces.Count >= 1 && main.PartCount + pieces.Sum(p => p.PartCount) == 4;
            var none = Enumerable.Range(0, w.Bodies.Count).All(bi => w.Bodies[bi].Id < chain.Id || Enumerable.Range(0, w.Bodies[bi].PartCount).All(pi => !posPicked.Zip(defsPicked).Any(z => w.Bodies[bi].Parts[pi].Def == z.Second && w.Bodies[bi].Parts[pi].Pos == z.First && w.Bodies[bi] == main)));
            Line($"remover: 3 of 7 beams deleted: main {main?.PartCount} parts + {pieces.Count} cut-off wrecks ({string.Join("+", pieces.Select(p => p.PartCount))}), {total} left of 4 expected; bodies {before} -> {w.Bodies.Count}; log: {g.SandboxHands.Done.LastOrDefault()}");
            if (!gone || !split || !none)
                problems.Add($"the confirmed delete went wrong (selection empty {gone}, the cut-off part(s) split into wrecks and 4 parts left {split}, deleted parts gone {none})");

            // ---- A sweep marks each part once; a double-click marks a whole construct; Shift+RMB clears ----
            var chain2 = RmChain(w, spot + new Vector3(4, 0, 0), 6, me.Id);
            if (chain2 == null || chain2.PartCount < 6)
            {
                Fail("remover: the second chain didn't build");
                return;
            }
            StandBy(me, chain2.WorldCom, 2.2f, chain2.WorldCom + new Vector3(0, 0, -1));
            Run(g, ref now, 0.2);
            sel.Clear();
            t += 1000;
            var s0 = RmAim(g, me, chain2, 1, RmIn(t, pressed: true));
            foreach (var p in new[] { 1, 2, 2, 3, 1, 4 })
            {
                t += 30;
                RmAim(g, me, chain2, p, RmIn(t, down: true));
            }
            t += 30;
            RmSky(g, me, RmIn(t, down: true)); // (the stroke carries on over nothing)
            t += 30;
            RmAim(g, me, chain2, 5, RmIn(t, down: true));
            t += 30;
            RmSky(g, me, RmIn(t));
            var swept = s0 && sel.Count == 5 && new[] { 1, 2, 3, 4, 5 }.All(i => sel.Contains(chain2.Id, false, i));
            // A new stroke starting on a marked part unmarks as it sweeps.
            t += 200;
            RmAim(g, me, chain2, 3, RmIn(t, pressed: true));
            t += 30;
            RmAim(g, me, chain2, 4, RmIn(t, down: true));
            t += 30;
            RmSky(g, me, RmIn(t));
            var erased = sel.Count == 3 && !sel.Contains(chain2.Id, false, 3) && !sel.Contains(chain2.Id, false, 4);
            // Shift+RMB clears.
            t += 200;
            RmSky(g, me, RmIn(t, rmb: true, shift: true));
            var cleared = sel.Count == 0 && !rm.PromptPending;
            // RMB with nothing selected says so and asks nothing.
            t += 200;
            RmSky(g, me, RmIn(t, rmb: true));
            var nothing = !rm.PromptPending && g.SandboxNotice.Contains("nothing selected");
            // Double-click: the whole construct.
            t += 600;
            RmAim(g, me, chain2, 2, RmIn(t, pressed: true));
            t += 40;
            RmAim(g, me, chain2, 2, RmIn(t));
            t += 100;
            RmAim(g, me, chain2, 2, RmIn(t, pressed: true));
            t += 40;
            RmAim(g, me, chain2, 2, RmIn(t));
            var whole = sel.Count == chain2.PartCount && sel.PartCount == chain2.PartCount;
            // Shift+double-click takes the construct off the selection again.
            t += 700;
            RmAim(g, me, chain2, 3, RmIn(t, pressed: true, shift: true));
            t += 40;
            RmAim(g, me, chain2, 3, RmIn(t, shift: true));
            t += 100;
            RmAim(g, me, chain2, 3, RmIn(t, pressed: true, shift: true));
            t += 40;
            RmAim(g, me, chain2, 3, RmIn(t, shift: true));
            var wholeOff = sel.Count == 0;
            // A selection goes stale when the construct changes under it: another player's removal renumbers the parts.
            RmClick(g, me, chain2, 4, ref t);
            var staleDef = chain2.Parts[4].Def;
            _ = staleDef;
            w.RemovePart(chain2, 1);
            var pruned = false;
            RmSky(g, me, RmIn(t + 10));
            pruned = sel.Count == 0 || sel.Count == 1 && chain2.Parts.Length > 0 && w.Get(chain2.Id) is { } c2 && sel[0].Index < c2.PartCount && c2.Parts[sel[0].Index].Pos == sel[0].Pos;
            sel.Clear();
            if (!swept || !erased || !cleared || !nothing || !whole || !wholeOff || !pruned)
                problems.Add($"the sweep and clears went wrong (swept {swept} [{sel.Count}], unmarking stroke {erased}, Shift+RMB clears {cleared}, nothing-selected notice {nothing}, double-click {whole}, Shift+double-click {wholeOff}, stale mark {pruned})");

            // ---- Links: a marked link goes alone; deleting a part takes the links on it ----
            Construct? Buggy(float dx)
            {
                var made = ConstructBuild.FindBlueprint("Buggy") is { } bp ? ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, spot.X + dx, spot.Z), 0, out _) : null;
                if (made == null || made.RouteCount < 1)
                    return null;
                made.Owner = me.Id;
                made.Rebuild();
                StandBy(me, made.WorldCom, 2.5f, made.WorldCom + new Vector3(0, 0, -1));
                Run(g, ref now, 0.2);
                return made;
            }
            var car = Buggy(8);
            if (car == null)
                problems.Add("the Buggy (with a link) didn't build");
            else
            {
                var routes = car.RouteCount;
                var lone = routes - 1;
                var loneType = car.Routes[lone].Type;
                var loneTo = car.Routes[lone].To;
                var aimedLink = false;
                sel.Clear();
                for (var seg = 0; seg <= car.Routes[lone].Count && !aimedLink; seg++)
                {
                    var lp = ConstructRoutes.WorldPoint(car, lone, seg);
                    foreach (var scale in new[] { 0.3f, 0.6f, 1.0f })
                    foreach (var off2 in RmEyes)
                    {
                        var eye = lp + off2 * scale;
                        var dir = Vector3.Normalize(lp - eye);
                        if (aimedLink || !w.Raycast(eye, dir, 3f, out var lh) || lh.Body != car || lh.RoutePlus1 != lone + 1)
                            continue;
                        t += 300;
                        Program.SandboxRemoverFrame(g, me, RmIn(t, pressed: true), eye, dir);
                        Program.SandboxRemoverFrame(g, me, RmIn(t + 40), eye, dir);
                        aimedLink = true;
                    }
                }
                if (!aimedLink)
                {
                    Line("remover: no ray found the buggy's link; the link is marked directly");
                    sel.AddLink(car, lone);
                }
                var linkOne = sel.Count == 1 && sel[0].Route && sel[0].Index == lone && sel.LinkCount == 1;
                var parts0 = car.PartCount;
                t += 500;
                RmSky(g, me, RmIn(t, rmb: true));
                var linkAsk = rm.PromptPending && g.SandboxPrompt == "Delete 1 link? Right-click again to confirm" && car.RouteCount == routes;
                t += 100;
                RmSky(g, me, RmIn(t, rmb: true));
                Run(g, ref now, 0.2);
                var linkGone = linkAsk && car.RouteCount == routes - 1 && car.PartCount == parts0 && w.Get(car.Id) == car;
                // A part with a link on it: the link goes with it.
                var car2 = Buggy(14);
                var target = -1;
                for (var k = 0; car2 != null && k < car2.RouteCount && target < 0; k++)
                    if (car2.Routes[k].Parent == 255 && car2.Routes[k].To < car2.PartCount && car2.DefOf(car2.Routes[k].To).Role != PartRole.Seat)
                        target = car2.Routes[k].To;
                var routesBefore = car2?.RouteCount ?? 0;
                var targetName = target < 0 ? "-" : car2!.DefOf(target).Name;
                var targetTag = target < 0 ? (ushort)0 : car2!.Parts[target].Tag;
                if (target >= 0)
                {
                    sel.Clear();
                    sel.AddPart(car2!, target);
                    t += 500;
                    RmSky(g, me, RmIn(t, rmb: true));
                    t += 100;
                    RmSky(g, me, RmIn(t, rmb: true));
                    Run(g, ref now, 0.2);
                }
                var partGone = target >= 0 && Enumerable.Range(0, car2!.PartCount).All(i => car2.Parts[i].Tag != targetTag);
                var noDangling = car2 != null && Enumerable.Range(0, car2.RouteCount).All(k => car2.Routes[k].To < car2.PartCount && (car2.Routes[k].Parent != 255 || car2.Routes[k].From < car2.PartCount));
                var linksFell = target >= 0 && car2!.RouteCount < routesBefore;
                Line($"remover: links: one link selected {linkOne} (type {loneType} into {loneTo}), asked {linkAsk}, gone alone {linkGone}; deleting a {targetName} with a link on it: {routesBefore} -> {car2?.RouteCount} links");
                if (!linkOne || !linkGone || !partGone || !noDangling || !linksFell)
                    problems.Add($"links went wrong (one link selected {linkOne}, deleted alone {linkGone}, part gone {partGone}, none dangling {noDangling}, link on the part gone {linksFell})");
            }

            // ---- A chest's contents spill as pickups; the store is freed ----
            var chest = ConstructBuild.FindBlueprint("Chest") is { } cbp ? ConstructBuild.BuildOnGround(w, cbp, ConstructWorld.Ground(World.Query, spot.X - 4, spot.Z), 0, out _) : null;
            if (chest == null)
                problems.Add("the Chest didn't build");
            else
            {
                chest.Owner = me.Id;
                chest.Rebuild();
                var box = StorePart(chest, "Chest");
                var battery = ConstructParts.Need("Battery");
                var lamp = ConstructParts.Need("Lamp");
                w.SetIn(chest, box, 0, battery);
                w.SetIn(chest, box, 1, lamp);
                var stores = w.LiveStores;
                var known = w.Bodies.Select(b => b.Id).ToHashSet();
                StandBy(me, chest.WorldCom, 2.2f, chest.WorldCom + new Vector3(0, 0, -1));
                Run(g, ref now, 0.2);
                sel.Clear();
                sel.AddPart(chest, box);
                t += 500;
                RmSky(g, me, RmIn(t, rmb: true));
                t += 100;
                RmSky(g, me, RmIn(t, rmb: true));
                Run(g, ref now, 0.2);
                var spilled = w.Bodies.Where(b => !known.Contains(b.Id) && b.Loose).Select(b => b.DefOf(0).Name).OrderBy(n => n).ToList();
                var okSpill = spilled.Contains(battery.Name) && spilled.Contains(lamp.Name) && w.LiveStores == stores - 1;
                Line($"remover: a deleted chest spilled [{string.Join(", ", spilled)}] and freed its store ({stores} -> {w.LiveStores})");
                if (!okSpill)
                    problems.Add($"the chest's contents did not spill as pickups ({string.Join(", ", spilled)}; stores {stores} -> {w.LiveStores})");
            }

            // ---- Creative: someone else's construct is free to delete; survival: the ownership rules ----
            var theirs = RmChain(w, spot + new Vector3(0, 0, 6), 4, 7);
            if (theirs != null)
            {
                StandBy(me, theirs.WorldCom, 2.2f, theirs.WorldCom + new Vector3(0, 0, -1));
                Run(g, ref now, 0.2);
                sel.Clear();
                t += 500;
                var picked = RmClick(g, me, theirs, 1, ref t) && sel.Count == 1;
                RmSky(g, me, RmIn(t, rmb: true));
                t += 100;
                RmSky(g, me, RmIn(t, rmb: true));
                Run(g, ref now, 0.2);
                var creativeOk = picked && (w.Get(theirs.Id) is not { } tc || tc.PartCount < 4 || !w.Bodies.Contains(theirs));
                if (!creativeOk)
                    problems.Add($"creative: someone else's construct wasn't deleted (selected {picked}, parts {w.Get(theirs.Id)?.PartCount})");
            }
            RemoverSurvival(g, ref now, ref t, me, sb, spot, problems);
        }
        finally
        {
            sb.InfiniteParts = wasInfinite;
            sb.RemoverInSurvival = wasSwitch;
            rm.ContestedOf = null;
            rm.Reset();
        }
        RemoverAllocation(yard, problems);
    }

    private static void RemoverSurvival(Game g, ref double now, ref long t, Player me, SandboxMap sb, Vector3 spot, List<string> problems)
    {
        var w = g.Constructs;
        var rm = Program.Remover;
        var sel = rm.Selection;
        sb.InfiniteParts = false;
        sb.RemoverInSurvival = true;
        Run(g, ref now, 0.2);
        var inv = g.SandboxInv.Of(me.Id);
        var beam = ConstructParts.Need("Square Beam 0.5 m");
        var invBefore = inv.Count(beam.Id);
        var mine = RmChain(w, spot + new Vector3(0, 0, -6), 5, me.Id);
        var theirs = RmChain(w, spot + new Vector3(4, 0, -6), 4, 7);
        var wreck = RmChain(w, spot + new Vector3(8, 0, -6), 3, 7);
        var defended = RmChain(w, spot + new Vector3(12, 0, -6), 4, 9);
        var loose = ConstructBuild.PlaceNew(w, beam, spot + new Vector3(-4, 0, -6), 0);
        if (mine == null || theirs == null || wreck == null || defended == null || loose == null)
        {
            problems.Add("survival: the scene didn't build");
            return;
        }
        wreck.Wreck = true;
        rm.ContestedOf = (_, c, _) => c.Owner == 9 ? "Owned by Rival - defended" : null;
        foreach (var c in w.Bodies)
            c.Rebuild();
        var notes = new List<string>();
        // Own construct: marked, deleted, nothing handed back.
        StandBy(me, mine.WorldCom, 2.2f, mine.WorldCom + new Vector3(0, 0, -1));
        Run(g, ref now, 0.2);
        sel.Clear();
        var looseBefore = w.Bodies.Count(b => b.Loose && b.DefOf(0) == beam);
        var knownBefore = w.Bodies.Select(b => b.Id).ToHashSet();
        var okMine = RmClick(g, me, mine, 2, ref t) && sel.Count == 1;
        RmSky(g, me, RmIn(t, rmb: true));
        t += 100;
        RmSky(g, me, RmIn(t, rmb: true));
        Run(g, ref now, 0.2);
        var minePartsLeft = (w.Get(mine.Id)?.PartCount ?? 0) + w.Bodies.Where(b => !knownBefore.Contains(b.Id)).Sum(b => b.PartCount);
        var handsEmpty = g.SandboxHands.Hand(me.Id).Held == ConstructParts.None && g.SandboxHands.Pack(me.Id).Items == 0 && inv.Count(beam.Id) == invBefore;
        var looseNow = w.Bodies.Count(b => b.Loose && b.DefOf(0) == beam);
        var noRefund = handsEmpty && looseNow <= looseBefore + 4; // (the cut-off ends are wrecks; none became an item for anyone)
        if (!okMine || minePartsLeft != 4 || !noRefund)
            notes.Add($"own construct (marked {okMine}, parts left {minePartsLeft} of 4 expected, hands/inventory unchanged {handsEmpty})");
        // Someone else's working construct, a defended one: not markable, with a reason.
        foreach (var (c, why) in new[] { (theirs, "someone else's"), (defended, "defended") })
        {
            StandBy(me, c.WorldCom, 2.2f, c.WorldCom + new Vector3(0, 0, -1));
            Run(g, ref now, 0.2);
            sel.Clear();
            t += 300;
            var clicked = RmClick(g, me, c, 1, ref t);
            var refused = clicked && sel.Count == 0 && g.SandboxNotice.Contains(why);
            // The server refuses it too, if a client asked anyway.
            sel.AddPart(c, 1);
            var n = c.PartCount;
            t += 300;
            RmSky(g, me, RmIn(t, rmb: true));
            t += 100;
            RmSky(g, me, RmIn(t, rmb: true));
            Run(g, ref now, 0.2);
            var intact = w.Get(c.Id) is { } cc && cc.PartCount == n && g.SandboxNotice.Length > 0;
            if (!refused || !intact)
                notes.Add($"{why} construct (not markable {refused} [notice \"{g.SandboxNotice}\"], the server's refusal kept it whole {intact})");
        }
        // A wreck and an unowned loose piece: fine.
        StandBy(me, wreck.WorldCom, 2.2f, wreck.WorldCom + new Vector3(0, 0, -1));
        Run(g, ref now, 0.2);
        sel.Clear();
        var wreckOk = RmClick(g, me, wreck, 1, ref t) && sel.Count == 1;
        t += 300;
        RmSky(g, me, RmIn(t, rmb: true));
        t += 100;
        RmSky(g, me, RmIn(t, rmb: true));
        Run(g, ref now, 0.2);
        wreckOk &= w.Get(wreck.Id) is not { } wc || wc.PartCount < 3;
        StandBy(me, loose.WorldCom, 2.2f, loose.WorldCom + new Vector3(0, 0, -1));
        Run(g, ref now, 0.2);
        sel.Clear();
        var looseOk = RmClick(g, me, loose, 0, ref t) && sel.Count == 1;
        t += 300;
        RmSky(g, me, RmIn(t, rmb: true));
        t += 100;
        RmSky(g, me, RmIn(t, rmb: true));
        Run(g, ref now, 0.2);
        looseOk &= w.Get(loose.Id) == null;
        if (!wreckOk || !looseOk)
            notes.Add($"a wreck (deleted {wreckOk}) or an unowned loose piece (deleted {looseOk}) wasn't deletable");
        // The host's switch.
        var again = RmChain(w, spot + new Vector3(0, 0, -10), 3, me.Id);
        if (again != null)
        {
            again.Rebuild();
            sb.RemoverInSurvival = false;
            Run(g, ref now, 0.2);
            StandBy(me, again.WorldCom, 2.2f, again.WorldCom + new Vector3(0, 0, -1));
            Run(g, ref now, 0.2);
            sel.Clear();
            RmClick(g, me, again, 1, ref t);
            t += 300;
            RmSky(g, me, RmIn(t, rmb: true));
            t += 100;
            RmSky(g, me, RmIn(t, rmb: true));
            Run(g, ref now, 0.2);
            var switched = again.PartCount == 3 && w.Get(again.Id) == again && g.SandboxNotice.Contains("switched off");
            if (!switched)
                notes.Add($"the host's switch (parts {again.PartCount}, notice \"{g.SandboxNotice}\")");
            sb.RemoverInSurvival = true;
        }
        // The server's own contested rule (a defended construct whose owner is near), through the edit API.
        var hands = new ConstructEdits { InfiniteParts = false };
        hands.Hand(0).Tool = ConstructTool.Remover;
        var w2 = new ConstructWorld();
        var cc2 = RmChain(w2, Vector3.Zero, 3, 5);
        hands.AliveAt = id => id == 5 ? Vector3.Zero : null;
        hands.NameOf = _ => "Rival";
        var targets = new[] { new RemoveTarget(cc2?.Id ?? 0, false, 1) };
        var why2 = cc2 == null ? "no chain" : hands.Remover(w2, 0, new Vector3(0, 1.5f, 2), targets, out _, out _);
        var def = why2?.Contains("defended") == true;
        hands.AliveAt = id => id == 5 ? new Vector3(500, 0, 0) : null;
        var far = cc2 == null ? "no chain" : hands.Remover(w2, 0, new Vector3(0, 1.5f, 2), targets, out _, out _);
        var sayTheirs = far?.Contains("someone else's") == true;
        if (!def || !sayTheirs)
            notes.Add($"the server's contested rule (defended \"{why2}\", owner away \"{far}\")");
        Line($"remover: survival: {(notes.Count == 0 ? "own construct deleted with nothing handed back; someone else's and a defended one refused with reasons; a wreck and an unowned loose piece deleted; the host's switch honoured; the server's contested rule holds" : string.Join("; ", notes))}");
        problems.AddRange(notes.Select(n => "survival: " + n));
        sb.InfiniteParts = true;
        rm.ContestedOf = null;
        Run(g, ref now, 0.2);
    }

    // ---- A few hundred parts in one go -------------------------------------------------------------------------

    private static void RemoverBatch(List<string> problems)
    {
        var w = new ConstructWorld();
        var beam = ConstructParts.Need("Square Beam 0.5 m");
        var hands = new ConstructEdits { InfiniteParts = true };
        hands.Hand(0).Tool = ConstructTool.Remover;
        // Warm-up (the first call JITs).
        var warm = RmChain(w, new Vector3(30, 0, 30), 4, 0);
        if (warm == null)
        {
            problems.Add("batch: the warm-up chain didn't build");
            return;
        }
        hands.Remover(w, 0, new Vector3(30, 1.5f, 32), [new RemoveTarget(warm.Id, false, 1)], out _, out _);
        var targets = new List<RemoveTarget>();
        for (var i = 0; i < 200; i++)
        {
            var b = ConstructBuild.PlaceNew(w, beam, new Vector3(-3.5f + i % 14 * 0.5f, 0, -3.5f + i / 14 * 0.5f), 0);
            if (b == null)
            {
                problems.Add("batch: loose pieces didn't build");
                return;
            }
            targets.Add(new RemoveTarget(b.Id, false, 0));
        }
        var chain = RmChain(w, new Vector3(0, 0, 0.3f), 90, 0);
        if (chain == null || chain.PartCount < 80)
        {
            problems.Add($"batch: the long chain built {chain?.PartCount} parts");
            return;
        }
        var total = chain.PartCount;
        for (var i = 20; i < 80; i++)
            targets.Add(new RemoveTarget(chain.Id, false, (byte)i));
        var bodies = w.Bodies.Count;
        var sw = Stopwatch.StartNew();
        var why = hands.Remover(w, 0, new Vector3(0, 1.5f, 0), targets.ToArray(), out var parts, out _);
        sw.Stop();
        var budget = TickCpu.Available ? 8.0 : 80.0; // (CPU-time budgets only measure on Windows; elsewhere a loose sanity bound)
        var ms = sw.Elapsed.TotalMilliseconds;
        var left = w.Bodies.Count(b => b.Loose && b.DefOf(0) == beam && b.Id != chain.Id);
        var okParts = parts == 260 && why == null && w.Get(chain.Id) is { } main && main.PartCount + w.Bodies.Where(b => b.Wreck && b.Id > chain.Id).Sum(b => b.PartCount) == total - 60;
        Line($"remover: {targets.Count} targets (200 loose pieces + 60 of a {total}-beam chain) in one request: {ms:0.00} ms (budget {budget:0} ms), {parts} parts destroyed ({why ?? "ok"}), bodies {bodies} -> {w.Bodies.Count}");
        if (!okParts || ms > budget)
            problems.Add($"batch: {targets.Count} targets took {ms:0.0} ms (budget {budget:0}) and destroyed {parts}: {why}");
        _ = left;
    }

    // ---- No allocation per frame --------------------------------------------------------------------------------

    private static void RemoverAllocation(Map yard, List<string> problems)
    {
        yard.RestoreProps();
        World.SetMap(yard);
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        if (SpawnLocalSafely(g, ref now) is { } notReady)
        {
            problems.Add($"allocation: {notReady}");
            return;
        }
        var me = g.Local!;
        var w = g.Constructs;
        var rm = Program.Remover;
        rm.Reset();
        if (OpenSpot(w) is not { } spot || RmChain(w, spot, 6, me.Id) is not { } chain)
        {
            problems.Add("allocation: no chain");
            return;
        }
        StandBy(me, chain.WorldCom, 2.2f, chain.WorldCom + new Vector3(0, 0, -1));
        Run(g, ref now, 0.2);
        long t = 1_000_000;
        for (var i = 0; i < chain.PartCount; i++)
            RmClick(g, me, chain, i, ref t);
        // Steady frames: held on one part, then aimed at nothing, with a question up. Warm once, then measure.
        for (var f = 0; f < 50; f++)
        {
            RmAim(g, me, chain, 1, RmIn(t += 17, down: true));
            RmSky(g, me, RmIn(t += 17));
        }
        RmSky(g, me, RmIn(t += 17, rmb: true));
        var target = ConstructRender.PartCentre(chain, 1);
        var eye = target + RmEyes[0];
        var dir = Vector3.Normalize(target - eye);
        var wall = World.RaycastWorld(eye, dir, ConstructBuild.Reach + 1);
        var aimed = w.Raycast(eye, dir, MathF.Min(wall, ConstructBuild.Reach + 1), out var hit);
        for (var f = 0; f < 50; f++)
            rm.Frame(g, me, RmIn(t += 17, down: true), aimed, in hit);
        GC.Collect();
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        for (var f = 0; f < 2000; f++)
        {
            rm.Frame(g, me, RmIn(t += 17, down: f % 2 == 0), aimed, in hit);
            rm.Frame(g, me, RmIn(t += 17), false, in hit);
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - a0;
        Line($"remover: {bytes} B allocated over 4000 steady frames (held on a part, off a part; {rm.Selection.Count} selected)");
        if (bytes > 2048)
            problems.Add($"allocation: the remover's frame allocated {bytes} B over 4000 steady frames");
        rm.Reset();
    }

    // ---- A real host and a separate client ----------------------------------------------------------------------

    private static void RemoverLoopback(Map yard, List<string> problems)
    {
        const ushort port = 26417;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.5, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            Pump(server, [a], 0.25, ref now, clock);
        Pump(server, [a], 0.5, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp) || a.Local is not { } mine)
        {
            problems.Add("loopback: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        Pump(server, [a], 0.3, ref now, clock);
        var w = server.Constructs;
        if (OpenSpot(w) is not { } spot || RmChain(w, spot, 7, sp.Id) is not { } chain)
        {
            problems.Add("loopback: no chain");
            return;
        }
        var buggy = ConstructBuild.FindBlueprint("Buggy") is { } bp ? ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, spot.X + 6, spot.Z), 0, out _) : null;
        buggy?.Rebuild();
        sp.Teleport(ConstructWorld.Ground(World.Query, chain.WorldCom.X, chain.WorldCom.Z - 2.2f) + new Vector3(0, 0.05f, 0));
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Remover);
        Pump(server, [a], 1.0, ref now, clock);
        var toolOut = server.SandboxHands.Hand(sp.Id).Tool == ConstructTool.Remover;
        // The client must see the chain as the server has it before it aims.
        var have = false;
        for (var wait = 0; wait < 40 && !have; wait++)
        {
            Pump(server, [a], 0.1, ref now, clock);
            have = a.Constructs.Get(chain.Id) is { } cc && cc.PartCount == chain.PartCount && (buggy == null || a.Constructs.Get(buggy.Id) is { } bc && bc.PartCount == buggy.PartCount);
        }
        if (!toolOut || !have)
        {
            problems.Add($"loopback: tool out {toolOut}, the client's copy of the chain {have}");
            return;
        }
        var copy = a.Constructs.Get(chain.Id)!;
        var rm = Program.Remover;
        rm.Reset();
        long t = 5_000_000;
        var picks = new[] { 1, 3, 4 };
        var ok = picks.All(i => RmClick(a, mine, copy, i, ref t));
        var marked = rm.Selection.Count == 3;
        var before = w.Bodies.Count;
        RmSky(a, mine, RmIn(t, rmb: true));
        var asked = rm.PromptPending && a.SandboxPrompt.StartsWith("Delete 3 parts?");
        Pump(server, [a], 0.4, ref now, clock);
        var untouched = chain.PartCount == 7 && w.Bodies.Count == before;
        t += 700;
        RmSky(a, mine, RmIn(t, rmb: true));
        // Wait for the server to have deleted them and the client's copy to agree.
        var agree = false;
        string why = "";
        for (var wait = 0; wait < 60 && !agree; wait++)
        {
            Pump(server, [a], 0.1, ref now, clock);
            agree = Agree(server, a, out why) && w.Bodies.Count > before;
        }
        var serverOk = w.Get(chain.Id) is { } sc && sc.PartCount + w.Bodies.Where(b => b.Wreck && b.Id > chain.Id).Sum(b => b.PartCount) == 4;
        // A link, from the client's aim.
        var linkOk = true;
        if (buggy != null)
        {
            var routes = buggy.RouteCount;
            var bcopy = a.Constructs.Get(buggy.Id)!;
            sp.Teleport(ConstructWorld.Ground(World.Query, buggy.WorldCom.X, buggy.WorldCom.Z - 2.5f) + new Vector3(0, 0.05f, 0));
            Pump(server, [a], 0.5, ref now, clock);
            rm.Selection.Clear();
            rm.Selection.AddLink(bcopy, bcopy.RouteCount - 1);
            t += 700;
            RmSky(a, mine, RmIn(t, rmb: true));
            t += 100;
            RmSky(a, mine, RmIn(t, rmb: true));
            linkOk = false;
            for (var wait = 0; wait < 60 && !linkOk; wait++)
            {
                Pump(server, [a], 0.1, ref now, clock);
                linkOk = buggy.RouteCount == routes - 1 && a.Constructs.Get(buggy.Id) is { } nb && nb.RouteCount == routes - 1 && Agree(server, a, out _);
            }
        }
        // A client without the tool out is refused by the server.
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
        Pump(server, [a], 0.5, ref now, clock);
        var keep = w.Get(chain.Id)!;
        var kept = keep.PartCount;
        rm.Selection.Clear();
        rm.Selection.AddPart(a.Constructs.Get(chain.Id)!, 0);
        t += 700;
        RmSky(a, mine, RmIn(t, rmb: true));
        t += 100;
        RmSky(a, mine, RmIn(t, rmb: true));
        Pump(server, [a], 0.8, ref now, clock);
        var refused = w.Get(chain.Id)?.PartCount == kept && a.SandboxNotice.Contains("remover");
        Line($"remover loopback: 3 beams marked by the client {ok && marked}, question {asked} deleted nothing {untouched}; the server deleted them {serverOk} and the client's copy agrees {agree} ({why}); a link {linkOk}; no tool out refused {refused} (\"{a.SandboxNotice}\")");
        if (!ok || !marked || !asked || !untouched || !agree || !serverOk || !linkOk || !refused)
            problems.Add($"loopback: marked {ok && marked}, asked {asked}, untouched {untouched}, server {serverOk}, agree {agree} ({why}), link {linkOk}, refused {refused}");
        rm.Reset();
    }

    /// <summary>Do the client's constructs match the server's (same bodies, same part counts)?</summary>
    private static bool Agree(Game server, Game client, out string why)
    {
        var s = server.Constructs;
        var c = client.Constructs;
        if (s.Bodies.Count != c.Bodies.Count)
        {
            why = $"{s.Bodies.Count} bodies on the server, {c.Bodies.Count} on the client";
            return false;
        }
        foreach (var b in s.Bodies)
            if (c.Get(b.Id) is not { } cb || cb.PartCount != b.PartCount || cb.RouteCount != b.RouteCount)
            {
                why = $"body {b.Id} differs ({b.PartCount} parts / {b.RouteCount} links vs {c.Get(b.Id)?.PartCount} / {c.Get(b.Id)?.RouteCount})";
                return false;
            }
        why = $"{s.Bodies.Count} bodies agree";
        return true;
    }
}
