using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Sandbox aim highlight" (board item dev-sandbox-highlight-relevant-hr01; Source/Constructs/SandboxAim.cs): the aim
    /// highlight shows only for what the held tool can act on, from one predicate per tool that the actions share. With the
    /// wrench out a frozen welder piece isn't highlighted and an item part is; with the physics gun out a contested enemy
    /// construct shows red with the reason; bare hands highlight a seat and not a panel; and for every tool the predicate
    /// equals the action's own check on scripted targets. Nothing allocates.
    /// </summary>
    private static void CheckSandboxAimHighlight()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"aim highlight: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        var keepRadius = ConstructGrab.ContestRadius;
        try
        {
            AimScenes();
        }
        finally
        {
            ConstructGrab.ContestRadius = keepRadius;
        }
    }

    private sealed class AimScene
    {
        public ConstructWorld W = new();
        public ConstructEdits Hands = new() { InfiniteParts = false };
        public Construct A = null!, E = null!, F = null!, L = null!;
        public int Block, Lamp, Seat, Button, ELamp;
        public Vector3 Spot;
    }

    private static int AimFit(ConstructWorld w, Construct c, string part)
    {
        var def = ConstructParts.Need(part);
        for (var on = 0; on < c.PartCount; on++)
        for (var ts = 0; ts < c.DefOf(on).Sockets.Length; ts++)
        for (var ds = 0; ds < def.Sockets.Length; ds++)
        for (var spin = 0; spin < 4; spin++)
            if (ConstructBuild.Place(c, on, ts, def, ds, spin, out var g) == null)
            {
                var i = ConstructBuild.Attach(w, c, def, g);
                c.Rebuild();
                return i;
            }
        return -1;
    }

    private static AimScene? AimNewScene()
    {
        var s = new AimScene();
        var w = s.W;
        if (OpenSpot(w) is not { } spot)
        {
            Fail("aim highlight: no open spot on the yard");
            return null;
        }
        s.Spot = spot;
        var block = ConstructParts.Need("Frame Block");
        var lamp = ConstructParts.Need("Lamp");
        s.A = ConstructBuild.PlaceNew(w, block, spot, 0)!;
        s.Block = 0;
        s.Lamp = AimFit(w, s.A, "Lamp");
        s.Seat = AimFit(w, s.A, "Seat");
        s.Button = AimFit(w, s.A, "Button");
        s.A.Owner = 0;
        s.E = ConstructBuild.PlaceNew(w, block, spot + new Vector3(3f, 0, 0), 0)!;
        s.ELamp = AimFit(w, s.E, "Lamp");
        s.E.Owner = 5;
        s.F = ConstructBuild.PlaceNew(w, block, spot + new Vector3(0, 0, 3f), 0)!;
        s.F.Owner = 0;
        s.F.Frozen = true;
        s.F.Anchored = true;
        s.L = ConstructBuild.PlaceNew(w, lamp, spot + new Vector3(-3f, 0, 0), 0)!;
        s.L.Owner = 0;
        foreach (var c in w.Bodies)
            c.Rebuild();
        var ownerAt = s.E.WorldCom + new Vector3(2f, 0, 0);
        s.Hands.AliveAt = id => id == 5 ? ownerAt : id == 0 ? spot : null;
        s.Hands.NameOf = id => id == 5 ? "Rival" : "Me";
        return s;
    }

    private static SandboxAim.Target AimT(AimScene s, Construct c, int part) => new()
    {
        World = s.W,
        Body = c,
        Part = part,
        Distance = 2f,
        Player = 0,
        Contested = s.Hands.Contested(c, 0),
        GrabWhy = s.Hands.GrabRefusal(c, 0),
        LinkType = RouteType.Data,
        PaintMode = PaintMode.Part,
    };

    private static void AimScenes()
    {
        if (AimNewScene() is not { } s)
            return;
        if (s.Lamp < 0 || s.Seat < 0 || s.Button < 0 || s.ELamp < 0 || !s.L.Loose)
        {
            Fail($"aim highlight: the scene didn't build (lamp {s.Lamp}, seat {s.Seat}, button {s.Button}, enemy lamp {s.ELamp}, loose lamp {s.L.Loose})");
            return;
        }
        var R = ConstructTool.Wrench;
        var Wd = ConstructTool.Welder;
        // The note's cases.
        var wrenchLamp = SandboxAim.CanAct(R, AimT(s, s.A, s.Lamp));
        var wrenchFrozen = SandboxAim.CanAct(R, AimT(s, s.F, 0));
        var wrenchBlock = SandboxAim.CanAct(R, AimT(s, s.A, s.Block));
        var welderFrozen = SandboxAim.CanAct(Wd, AimT(s, s.F, 0));
        var welderLamp = SandboxAim.CanAct(Wd, AimT(s, s.A, s.Lamp));
        var gunEnemy = SandboxAim.CanAct(ConstructTool.PhysicsGun, AimT(s, s.E, 0));
        var gunMine = SandboxAim.CanAct(ConstructTool.PhysicsGun, AimT(s, s.A, 0));
        var gunFrozen = SandboxAim.CanAct(ConstructTool.PhysicsGun, AimT(s, s.F, 0));
        var handsSeat = SandboxAim.CanAct(ConstructTool.None, AimT(s, s.A, s.Seat));
        var handsBlock = SandboxAim.CanAct(ConstructTool.None, AimT(s, s.A, s.Block));
        var handsLamp = SandboxAim.CanAct(ConstructTool.None, AimT(s, s.A, s.Lamp));
        var handsLoose = SandboxAim.CanAct(ConstructTool.None, AimT(s, s.L, 0));
        var wrenchEnemyLamp = SandboxAim.CanAct(R, AimT(s, s.E, s.ELamp));
        Line($"aim highlight: wrench: own lamp {wrenchLamp}, frozen frame block {wrenchFrozen}, frame block {wrenchBlock}; welder: frozen block {welderFrozen}, lamp {welderLamp}; physics gun: enemy {gunEnemy}, own {gunMine}, frozen {gunFrozen}; hands: seat {handsSeat}, frame block {handsBlock}, fitted lamp {handsLamp}, loose lamp {handsLoose}; wrench on the enemy lamp {wrenchEnemyLamp}");
        if (!wrenchLamp.Can || wrenchFrozen.Show || wrenchBlock.Show || !welderFrozen.Can || welderLamp.Show)
            Fail("aim highlight: the wrench or the welder highlights what it can't act on (or misses what it can)");
        if (gunEnemy.Verdict != AimVerdict.Refused || gunEnemy.Why != "Owned by Rival - defended" || !gunMine.Can || !gunFrozen.Can)
            Fail("aim highlight: the physics gun's contested enemy construct isn't red with the reason (or own / frozen ones aren't highlighted)");
        if (!handsSeat.Can || handsBlock.Show || handsLamp.Show || !handsLoose.Can)
            Fail("aim highlight: bare hands highlight the wrong things (a seat and a loose item yes, a panel or a fitted lamp no)");
        if (wrenchEnemyLamp.Verdict != AimVerdict.Refused)
            Fail("aim highlight: the wrench on someone else's lamp isn't refused (red)");

        // Tools that never fit the part: no highlight at all.
        var linkerBlock = SandboxAim.CanAct(ConstructTool.Linker, AimT(s, s.A, s.Block));
        var linkerBtn = SandboxAim.CanAct(ConstructTool.Linker, AimT(s, s.A, s.Button) with { LinkType = RouteType.Data });
        var cfgBlock = SandboxAim.CanAct(ConstructTool.Configure, AimT(s, s.A, s.Block));
        var cfgBtn = SandboxAim.CanAct(ConstructTool.Configure, AimT(s, s.A, s.Button));
        var cfgEnemy = SandboxAim.Configure(s.E.DefOf(s.ELamp), s.Hands.Contested(s.E, 0), null);
        var paintBlock = SandboxAim.CanAct(ConstructTool.Paint, AimT(s, s.A, s.Block));
        var paintEnemy = SandboxAim.CanAct(ConstructTool.Paint, AimT(s, s.E, 0));
        var paintFar = SandboxAim.CanAct(ConstructTool.Paint, AimT(s, s.A, s.Block) with { Distance = 9f });
        Line($"aim highlight: linker on a block {linkerBlock}, on a button {linkerBtn}; configure on a block {cfgBlock}, on a button {cfgBtn}, enemy lamp {cfgEnemy}; paint on a block {paintBlock}, enemy {paintEnemy}, out of reach {paintFar}");
        if (linkerBlock.Show || !linkerBtn.Show || cfgBlock.Show || !cfgBtn.Can || paintBlock.Verdict == AimVerdict.None || paintEnemy.Verdict != AimVerdict.Refused || paintFar.Show)
            Fail("aim highlight: the linking, configure or paint tool's highlight is wrong");

        // For every tool, the predicate equals the action's own check on scripted targets.
        var checks = 0;
        var bad = 0;
        void Same(string what, AimCall pred, string? act)
        {
            checks++;
            var ok = pred.Verdict switch
            {
                AimVerdict.Can => act == null,
                AimVerdict.Refused => act == pred.Why,
                _ => act != null,
            };
            if (!ok)
            {
                bad++;
                Fail($"aim highlight: {what}: the predicate says {pred} but the action says \"{act ?? "ok"}\"");
            }
        }
        foreach (var tool in new[] { R, Wd })
            for (var k = 0; k < 7; k++)
            {
                var sc = AimNewScene()!;
                var (c, part) = k switch { 0 => (sc.A, sc.Block), 1 => (sc.A, sc.Lamp), 2 => (sc.A, sc.Seat), 3 => (sc.E, 0), 4 => (sc.E, sc.ELamp), 5 => (sc.F, 0), _ => (sc.A, sc.Button) };
                sc.Hands.Hand(0).Tool = tool;
                var pred = SandboxAim.Remove(tool, c, part, 0, sc.Hands.Contested(c, 0));
                var eye = ConstructRender.PartCentre(c, part) + new Vector3(0, 0.3f, -2f);
                Same($"{tool} takes off {c.DefOf(part).Name} of {(c == sc.A ? "own" : c == sc.E ? "enemy" : "frozen")}", pred, sc.Hands.BeginRemove(sc.W, 0, eye, c.Id, part, 0));
            }
        for (var k = 0; k < 4; k++)
        {
            var sc = AimNewScene()!;
            var c = k switch { 0 => sc.A, 1 => sc.E, 2 => sc.F, _ => sc.L };
            sc.Hands.Hand(0).Tool = ConstructTool.PhysicsGun;
            var pred = SandboxAim.Grab(sc.Hands.GrabRefusal(c, 0));
            var eye = c.WorldCom + new Vector3(-3f, 1.2f, 0);
            var (_, yaw) = Aim(eye, c.WorldCom);
            Same($"physics gun grabs target {k}", pred, sc.Hands.Grab(sc.W, 0, eye, yaw, c.Id, c.Com));
            sc.Hands.Release(sc.W, 0);
        }
        foreach (var tool in new[] { ConstructTool.None, R })
            for (var k = 0; k < 7; k++)
            {
                var sc = AimNewScene()!;
                var (c, part) = k switch { 0 => (sc.A, sc.Block), 1 => (sc.A, sc.Lamp), 2 => (sc.A, sc.Seat), 3 => (sc.A, sc.Button), 4 => (sc.L, 0), 5 => (sc.E, sc.ELamp), _ => (sc.F, 0) };
                sc.Hands.Hand(0).Tool = tool;
                var pred = SandboxAim.Use(tool, AimT(sc, c, part));
                var eye = ConstructRender.PartCentre(c, part) + new Vector3(0, 0.3f, -1.5f);
                Same($"{tool} uses {c.DefOf(part).Name} (target {k})", pred, sc.Hands.Use(sc.W, 0, eye, c.Id, part));
            }
        for (var k = 0; k < 5; k++)
        {
            var sc = AimNewScene()!;
            var (c, part) = k switch { 0 => (sc.A, sc.Block), 1 => (sc.A, sc.Lamp), 2 => (sc.A, sc.Button), 3 => (sc.E, 0), _ => (sc.L, 0) };
            sc.Hands.Hand(0).Tool = ConstructTool.Paint;
            var pred = SandboxAim.Paint(c.DefOf(part), PaintMode.Part, sc.Hands.Contested(c, 0));
            var eye = ConstructRender.PartCentre(c, part) + new Vector3(0, 0.3f, -2f);
            Same($"paint on {c.DefOf(part).Name} (target {k})", pred, sc.Hands.Paint(sc.W, 0, eye, c.Id, PaintMode.Part, false, part, ConstructPaint.SetBit | 0x336699));
        }
        for (var k = 0; k < 2; k++)
        {
            var sc = AimNewScene()!;
            var c = k == 0 ? sc.A : sc.E;
            sc.Hands.Hand(0).Tool = ConstructTool.Configure;
            var part = k == 0 ? sc.Button : sc.ELamp;
            var pred = SandboxAim.Configure(c.DefOf(part), sc.Hands.Contested(c, 0), null);
            var act = sc.Hands.Configure(sc.W, 0, ConstructRender.PartCentre(c, part) + new Vector3(0, 0.3f, -2f), c.Id, [(byte)part], [], [], null);
            // (a configurable part of your own gets as far as "nothing to set": no refusal; a contested one is refused first)
            Same($"configure target {k}", pred.Can ? AimCall.Ok : pred, pred.Can ? (act == "nothing to set" ? null : act) : pred.Show ? act : null);
        }
        Line($"aim highlight: {checks} predicate/action comparisons, {bad} disagree");

        // The predicate allocates nothing (the reasons are the strings it is given).
        var t1 = AimT(s, s.A, s.Lamp);
        var t2 = AimT(s, s.E, 0);
        for (var i = 0; i < 20; i++)
        {
            SandboxAim.CanAct(R, t1);
            SandboxAim.CanAct(ConstructTool.PhysicsGun, t2);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2000; i++)
        {
            SandboxAim.CanAct(R, t1);
            SandboxAim.CanAct(Wd, t1);
            SandboxAim.CanAct(ConstructTool.PhysicsGun, t2);
            SandboxAim.CanAct(ConstructTool.None, t1);
            SandboxAim.CanAct(ConstructTool.Paint, t1);
        }
        var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        Line($"aim highlight: 10000 predicate calls allocated {alloc} B");
        if (alloc > 0)
            Fail($"aim highlight: the predicate allocated {alloc} B");
    }
}
