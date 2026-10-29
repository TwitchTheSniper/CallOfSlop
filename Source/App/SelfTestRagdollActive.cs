using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Active ragdolls (Source/Physics/RagdollActive.cs, Ragdolls/Ragdolls.json "reactions"): the kill packet carries the
    /// hit part, the cause and the overkill (protocol 75) and the server fills them; a zombie's death info round-trips; the
    /// overkill thresholds pick active or limp; every reaction in the table runs and ends limp, finite, out of the floor
    /// and asleep; two PCs with slightly different views of the death pick the same reaction; the active caps hold; a
    /// legless zombie only gets a legless reaction; the drive allocates nothing.
    /// </summary>
    private static void CheckRagdollActive()
    {
        try
        {
            RagdollTuning.Reload();
            ActiveWire();
            ActiveChoice();
            ActiveRuns();
            ActiveCaps();
        }
        finally
        {
            RagdollTuning.Reload();
            Ragdolls.WorldOverride = null;
            Ragdolls.Clear();
        }
    }

    private static void ActiveWire()
    {
        // The packet: round trip of the new fields, and an old (shorter) packet reads as unknown.
        var pk = new KillPacket(3, 7, false, 12, 5, 80, new Vector3(1, 2, 3)) { HitPart = BloodPart.LegL, Cause = DeathCause.Fire, OverkillHp = 17, OverkillFraction = 0.25f };
        var w = new PacketWriter(64);
        pk.Write(w);
        var r = new PacketReader(w.Span[1..]);
        var back = KillPacket.Read(ref r);
        var r2 = new PacketReader(w.Span[1..^3]);
        var old = KillPacket.Read(ref r2);
        var trip = back.HitPart == BloodPart.LegL && back.Cause == DeathCause.Fire && back.OverkillHp == 17 && MathF.Abs(back.OverkillFraction - 0.25f) < 0.005f;
        var oldOk = old.HitPart == BloodPart.None && old.Cause == DeathCause.Unknown && old.OverkillHp == -1 && old.OverkillFraction < 0;
        Line($"  kill packet: {w.Length} bytes (protocol {ServerInfo.ProtocolVersion}); the death round-trips {trip}; a packet without it reads as unknown {oldOk}");
        if (!trip || !oldOk || ServerInfo.ProtocolVersion < 75)
            Fail("active ragdolls: the kill packet's death fields don't round-trip (or the protocol wasn't bumped)");

        // The server fills them: a leg shot that only just kills, and a headshot with plenty to spare.
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 2);
        var human = game.Players[0];
        var bots = game.Players.Values.Where(p => p.Id != 0).ToArray();
        game.SelfTestPerks(human, PerkSet.Empty);
        foreach (var b in bots)
            game.SelfTestPerks(b, PerkSet.Empty);
        game.SelfTestPlace(human, new Vector3(5, 0, 5), MathF.PI);
        game.SelfTestPlace(bots[0], new Vector3(5, 0, 8), 0);
        game.SelfTestPlace(bots[1], new Vector3(8, 0, 8), 0);
        game.SelfTestBloodShot(human, bots[0], World.MaxHealth + 10, new Vector3(4.85f, 0.5f, 8f), 1); // the left leg, 10 over
        var leg = game.LastKill;
        game.SelfTestBloodShot(human, bots[1], World.MaxHealth * 4, new Vector3(8, 1.62f, 7.8f), 2); // the head, 300 over
        var head = game.LastKill;
        Line($"  server: a leg shot 10 over: part {leg.HitPart}, {leg.Cause}, overkill {leg.OverkillHp} HP ({leg.OverkillFraction:0.00} of the hit); "
             + $"a headshot 300 over: part {head.HitPart}, overkill {head.OverkillHp} HP ({head.OverkillFraction:0.00})");
        if (leg.HitPart != BloodPart.LegL || leg.Cause != DeathCause.Bullet || leg.OverkillHp != 10 || MathF.Abs(leg.OverkillFraction - 10f / (World.MaxHealth + 10)) > 0.01f
            || head.HitPart != BloodPart.Head || !head.Headshot || head.OverkillHp < 254 || head.OverkillFraction < 0.7f)
            Fail("active ragdolls: the server's kill packet doesn't carry the hit part, cause or overkill");

        // A zombie's death info.
        var info = Game.ZDeathInfo(0.2f, true, false, true);
        var known = Game.ZDeathInfoRead(info, out var share, out var zh, out var zm, out var zb);
        var unknown = Game.ZDeathInfoRead(0, out _, out _, out _, out _);
        Line($"  zombie death info 0x{info:X2}: known {known}, overkill share {share:0.00}, head {zh}, melee {zm}, blast {zb}; 0 reads as unknown {!unknown}");
        if (!known || MathF.Abs(share - 0.2f) > 0.04f || !zh || zm || !zb || unknown)
            Fail("active ragdolls: a zombie's death info doesn't round-trip");
    }

    private static RagdollDeath NearDeath(BloodPart part, DeathCause cause, int over, float share, byte killId = 5) => new()
    {
        Feet = Vector3.Zero, Yaw = 0.3f, Stance = Stance.Stand, Victim = 7, Killer = 3, KillId = killId, Source = new Vector3(0.5f, 1.4f, -8), HasSource = true,
        GunPower = 3, HitPart = part, Cause = cause, OverkillHp = over, OverkillFraction = share, Headshot = part == BloodPart.Head,
    };

    private static string ChoiceName(RagdollActive.Choice c) => c.Reaction >= 0 ? RagdollActive.Reactions[c.Reaction].Name : "limp";

    private static void ActiveChoice()
    {
        var n = RagdollActive.Reactions.Length;
        Line($"  {n} reactions: {string.Join(", ", RagdollActive.Reactions.Select(x => x.Name))}");
        if (n < 8 || RagdollTuning.LoadErrors.Count > 0)
            Fail($"active ragdolls: the reaction table didn't load ({string.Join("; ", RagdollTuning.LoadErrors)})");

        // Thresholds: a near thing is active (longer the nearer), plenty of overkill limp, unknown limp, blasts limp.
        (string Name, RagdollDeath D, bool Active)[] cases =
        [
            ("5 HP over (5%)", NearDeath(BloodPart.Front, DeathCause.Bullet, 5, 0.05f), true),
            ("25 HP over (20%)", NearDeath(BloodPart.Front, DeathCause.Bullet, 25, 0.2f), true),
            ("40 HP over (28%)", NearDeath(BloodPart.Front, DeathCause.Bullet, 40, 0.28f), true),
            ("60 HP over (45%)", NearDeath(BloodPart.Front, DeathCause.Bullet, 60, 0.45f), false),
            ("a headshot 300 over", NearDeath(BloodPart.Head, DeathCause.Bullet, 254, 0.75f), false),
            ("a headshot 6 over", NearDeath(BloodPart.Head, DeathCause.Bullet, 6, 0.1f), true),
            ("a frag 5 over", NearDeath(BloodPart.Front, DeathCause.Explosive, 5, 0.05f) with { BlastStrength = 11, BlastAt = new Vector3(1, 0, 1), BlastRadius = 6 }, false),
            ("unknown overkill", NearDeath(BloodPart.Front, DeathCause.Bullet, -1, -1), false),
        ];
        var bad = new List<string>();
        var parts = new List<string>();
        foreach (var (name, d, active) in cases)
        {
            var c = RagdollActive.Choose(d);
            parts.Add($"{name}: {ChoiceName(c)}{(c.Reaction >= 0 ? $" {c.Duration:0.00} s" : "")}");
            if ((c.Reaction >= 0) != active)
                bad.Add(name);
        }
        var d5 = RagdollActive.Choose(cases[0].D).Duration;
        var d40 = RagdollActive.Choose(cases[2].D).Duration;
        Line("  thresholds: " + string.Join("; ", parts));
        if (bad.Count > 0 || !(d5 > d40))
            Fail($"active ragdolls: the overkill thresholds pick wrongly ({string.Join(", ", bad)}), or nearer things aren't longer");

        // Regions and causes reach their reactions.
        (string Want, RagdollDeath D)[] picks =
        [
            ("leg buckle", NearDeath(BloodPart.LegR, DeathCause.Bullet, 4, 0.05f)),
            ("arm spin", NearDeath(BloodPart.ArmL, DeathCause.Bullet, 4, 0.05f)),
            ("burning flail", NearDeath(BloodPart.None, DeathCause.Fire, 2, 0.1f)),
            ("doubling over", NearDeath(BloodPart.None, DeathCause.Poison, 2, 0.1f)),
            ("electrocuted", NearDeath(BloodPart.None, DeathCause.Shock, 2, 0.1f)),
            ("pulled down", NearDeath(BloodPart.Front, DeathCause.Zombie, 2, 0.1f)),
            ("crawler collapse", NearDeath(BloodPart.LegR, DeathCause.Bullet, 4, 0.05f) with { Legless = true }),
        ];
        var wrong = picks.Where(p => ChoiceName(RagdollActive.Choose(p.D)) != p.Want).Select(p => $"{p.Want} got {ChoiceName(RagdollActive.Choose(p.D))}").ToList();
        // The hit side follows the hit limb.
        var right = RagdollActive.Choose(picks[0].D).Side;
        var left = RagdollActive.Choose(picks[1].D).Side;
        Line($"  picks: {(wrong.Count == 0 ? "every region and cause reached its reaction" : string.Join(", ", wrong))}; right leg side {right:+0;-0}, left arm side {left:+0;-0}");
        if (wrong.Count > 0 || right != 1 || left != -1)
            Fail("active ragdolls: a hit region or cause doesn't reach its reaction (or the wrong side)");

        // Every PC picks the same: positions, yaw and speed a little different (each PC's drawn pose), the same kill.
        var same = 0;
        var variants = new HashSet<string>();
        for (var k = 0; k < 64; k++)
        {
            var a = NearDeath(BloodPart.Front, DeathCause.Bullet, k % 20, k % 20 / 120f, (byte)k) with { Killer = (byte)(k * 7) };
            var b = a with { Feet = a.Feet + new Vector3(0.13f, 0, -0.08f), Yaw = a.Yaw + 0.05f, Velocity = new Vector3(0.4f, 0, 0.2f) };
            var ca = RagdollActive.Choose(a);
            var cb = RagdollActive.Choose(b);
            if (ca == cb)
                same++;
            variants.Add(ChoiceName(ca));
        }
        Line($"  two PCs, 64 kills each seen slightly differently: the same reaction, side and time for {same}; variants picked: {string.Join(", ", variants)}");
        if (same != 64 || variants.Count < 2)
            Fail("active ragdolls: PCs with slightly different views of a death choose different reactions (or the variants never vary)");
    }

    private static void ActiveRuns()
    {
        var color = new Vector3(0.95f, 0.75f, 0.2f);
        var scene = RagdollScene([Floor, new Box(new Vector3(0, 1.5f, 4), new Vector3(4, 1.5f, 0.2f))]);
        Ragdolls.WorldOverride = scene.World;
        var problems = new List<string>();
        var lines = new List<string>();
        for (var i = 0; i < RagdollActive.Reactions.Length; i++)
        {
            var re = RagdollActive.Reactions[i];
            // A death this reaction applies to (its first cause and region), a near thing; the wall 4 m behind.
            var cause = re.Causes?[0] ?? DeathCause.Bullet;
            var region = re.Regions?[0] ?? BloodPart.Front;
            var d = NearDeath(region, cause, 3, 0.04f) with { Legless = re.Legless, Velocity = re.Moving == true ? new Vector3(0, 0, -6) : Vector3.Zero, Stance = re.Legless ? Stance.Prone : Stance.Stand };
            for (var id = 0; id < 256 && RagdollActive.Choose(d).Reaction != i; id++)
                d = d with { Killer = (byte)id };
            if (RagdollActive.Choose(d).Reaction != i)
            {
                problems.Add($"{re.Name}: no death picks it");
                continue;
            }
            Ragdolls.Clear();
            var b = Ragdolls.SpawnForTest(d, re.Legless ? RagdollLook.Zombie : RagdollLook.Soldier, 0, color);
            var started = b.Act.Active;
            var activeSteps = 0;
            var finite = true;
            var pen = 0f;
            var lowest = float.MaxValue;
            var upright = 0f;
            for (var s = 0; s < 600 && !b.Sim.Broken && !b.Sim.Asleep; s++)
            {
                Ragdolls.StepAll();
                if (b.Act.Active)
                {
                    activeSteps++;
                    if (s == 12)
                        upright = b.Sim.Pos[RagdollRig.Head].Y; // (how high the head still is after 0.2 s)
                }
                foreach (var p in b.Sim.Pos)
                    finite &= float.IsFinite(p.X + p.Y + p.Z);
                for (var k = 0; k < RagdollRig.Count; k++)
                    lowest = MathF.Min(lowest, b.Sim.Pos[k].Y - RagdollRig.Radius[k]);
                if (s % 15 == 0)
                    pen = MathF.Max(pen, RagdollPenetration(b.Sim, scene.Solids));
            }
            var ok = started && finite && !b.Sim.Broken && !b.Act.Active && !b.Sim.Driven && b.Sim.Asleep && pen < 0.05f && lowest > -0.08f && RagdollLinkError(b.Sim.Pos) < 0.03f;
            lines.Add($"{re.Name}: active {activeSteps / 60f:0.00} s, head at {upright:0.00} m after 0.2 s, {(b.Sim.Asleep ? $"asleep after {b.Sim.AwakeSteps / 60f:0.0} s" : "awake")}, inside {pen * 1000:0} mm");
            if (!ok)
                problems.Add($"{re.Name} (started {started}, finite {finite}, broken {b.Sim.Broken}, still active {b.Act.Active}, asleep {b.Sim.Asleep}, inside {pen:0.000}, lowest {lowest:0.000})");
        }
        foreach (var l in lines)
            Line("    " + l);
        if (problems.Count > 0)
            Fail($"active ragdolls: reactions that didn't run cleanly: {string.Join("; ", problems)}");

        // The active body stays up for a moment (a stagger), a limp one drops straight away.
        Ragdolls.Clear();
        var act = Ragdolls.SpawnForTest(NearDeath(BloodPart.Front, DeathCause.Bullet, 2, 0.02f), RagdollLook.Soldier, 0, color);
        var limp = Ragdolls.SpawnForTest(NearDeath(BloodPart.Front, DeathCause.Bullet, 200, 0.8f) with { Feet = new Vector3(-3, 0, 0), Victim = 8 }, RagdollLook.Soldier, 0, color);
        for (var s = 0; s < 24; s++)
            Ragdolls.StepAll();
        var actHead = act.Sim.Pos[RagdollRig.Head].Y;
        var limpHead = limp.Sim.Pos[RagdollRig.Head].Y;
        Line($"  after 0.4 s the near thing's head is at {actHead:0.00} m ({(act.Act.Active ? "still powered" : "limp")}), the big overkill's at {limpHead:0.00} m");
        if (!(actHead > limpHead + 0.15f))
            Fail("active ragdolls: a near thing doesn't stay up longer than a limp body");

        // No allocations while driving.
        Ragdolls.Clear();
        for (var i = 0; i < 4; i++)
            Ragdolls.SpawnForTest(NearDeath(BloodPart.Front, DeathCause.Fire, 2, 0.02f) with { Feet = new Vector3(i * 2 - 3, 0, -2), Victim = (byte)(20 + i) }, RagdollLook.Soldier, 0, color);
        Ragdolls.StepAll();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var s = 0; s < 40; s++)
            Ragdolls.StepAll();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Line($"  4 powered bodies for 40 steps: {bytes} bytes allocated, {Ragdolls.ActiveCount().All} still powered");
        if (bytes > 0)
            Fail($"active ragdolls: the drive allocates ({bytes} bytes)");
    }

    private static void ActiveCaps()
    {
        var color = new Vector3(0.4f, 0.9f, 0.4f);
        Ragdolls.WorldOverride = RagdollScene([Floor]).World;
        Ragdolls.Clear();
        var maxAll = (int)RagdollTuning.Get(RT.MaxActive);
        var maxZ = (int)RagdollTuning.Get(RT.MaxActiveZombies);
        for (var i = 0; i < 6; i++)
            Ragdolls.SpawnForTest(NearDeath(BloodPart.Front, DeathCause.Bullet, 2, 0.02f) with { Feet = new Vector3(i * 2 - 5, 0, 0), Victim = 255, KillId = (byte)i }, RagdollLook.Zombie, 0, color);
        var zombiesOnly = Ragdolls.ActiveCount();
        for (var i = 0; i < 8; i++)
            Ragdolls.SpawnForTest(NearDeath(BloodPart.Front, DeathCause.Bullet, 2, 0.02f) with { Feet = new Vector3(i * 2 - 7, 0, 4), Victim = (byte)(30 + i) }, RagdollLook.Soldier, 0, color);
        var all = Ragdolls.ActiveCount();
        Line($"  caps: 6 near-thing zombies -> {zombiesOnly.Zombies} powered (cap {maxZ}); then 8 soldiers -> {all.All} powered in all (cap {maxAll})");
        if (zombiesOnly.Zombies != Math.Min(6, maxZ) || all.All != maxAll || all.Zombies > maxZ)
            Fail("active ragdolls: the caps on powered bodies don't hold");
        for (var s = 0; s < 200; s++)
            Ragdolls.StepAll();
        var after = Ragdolls.ActiveCount();
        Line($"  after 3.3 s: {after.All} still powered");
        if (after.All != 0)
            Fail("active ragdolls: a powered body never went limp");
    }
}
