using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Sprint inaccuracy (Source/Game/SprintSpread.cs): spread standing / walking / sprinting per class, standing and walking
    /// unchanged from the old formula, the recovery window, the crosshair gap, the server's sprint floor (a tight-spread
    /// shot while sprinting is widened, through the real ServerResolveShot path too) and sprinting bots.
    /// </summary>
    private static void CheckSprintSpread()
    {
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1);
        var me = game.Local!;
        var notes = new List<string>();
        void L(string text)
        {
            Line(text);
            notes.Add(text);
        }
        game.SelfTestPerks(me, PerkSet.Empty);
        game.SelfTestPlace(me, new Vector3(5, 0, 5), 0);
        L($"recovery window {SprintSpread.RecoverySeconds:0.00} s = {SprintSpread.RecoveryTicks} ticks; ADS while sprinting: impossible (aiming drops the Sprint button), the server floor treats a sprinting shot as hip fire");

        // Puts the local player in a movement state and returns CurrentSpread.
        float Spread(WeaponDef def, float speed, bool sprint, float aimT = 0, PerkSet? perks = null)
        {
            game.Slots[0] = new WeaponState(def);
            game.ActiveSlot = 0;
            game.SelfTestPerks(me, perks ?? PerkSet.Empty);
            me.Sim.Velocity = new Vector3(0, 0, -speed);
            me.Sim.Grounded = true;
            me.Sim.Stance = Stance.Stand;
            me.Sprinting = false;
            me.SprintRecoverTicks = 0;
            SprintSpread.Tick(me, sprint ? Buttons.Forward | Buttons.Sprint : Buttons.Forward);
            game.Bloom = 0;
            game.AimT = aimT;
            return game.CurrentSpread();
        }
        // The spread formula before sprint inaccuracy (for the "unchanged" checks).
        static float OldSpread(WeaponDef def, float speed, float aimT)
        {
            var s = float.Lerp(def.HipSpreadRad, def.HipSpreadRad * def.AdsSpreadMult, aimT);
            s += MathF.Min(speed / World.WalkSpeed, 1.5f) * 0.014f * (aimT > 0 ? 0.5f : 1f);
            return Math.Clamp(s, 0f, def.MaxSpreadRad);
        }
        static float Deg(float rad) => rad * 180f / MathF.PI;
        static float Gap(float spread) => MathF.Tan(spread) / MathF.Tan(MathF.PI / 4) * 540f; // crosshair gap at 1080p, 90 deg FOV

        var classes = Enum.GetValues<WeaponClass>();
        var multByClass = new Dictionary<WeaponClass, float>();
        foreach (var c in classes)
        {
            if (Weapons.All.FirstOrDefault(w => w.Class == c) is not { } def)
                continue;
            var stand = Spread(def, 0, false);
            var walk = Spread(def, World.WalkSpeed, false);
            var sprint = Spread(def, World.SprintSpeed * def.Mobility, true);
            var held = Spread(def, 0, true); // Sprint held while standing still: not sprinting
            var standAds = Spread(def, 0, false, 1f);
            var ratio = sprint / walk;
            multByClass[c] = SprintSpread.ClassMult(c);
            L($"{c,-12} {def.Name,-22} stand {Deg(stand),5:0.00} deg  walk {Deg(walk),5:0.00} deg  sprint {Deg(sprint),5:0.00} deg (x{ratio:0.00} of walking, class x{SprintSpread.ClassMult(c):0.00})  crosshair gap {Gap(walk),4:0} -> {Gap(sprint),4:0} px");
            if (MathF.Abs(stand - OldSpread(def, 0, 0)) > 1e-6f || MathF.Abs(walk - OldSpread(def, World.WalkSpeed, 0)) > 1e-6f
                || MathF.Abs(standAds - OldSpread(def, 0, 1f)) > 1e-6f)
                Fail($"{def.Name}: standing / walking spread changed ({stand} / {walk} vs {OldSpread(def, 0, 0)} / {OldSpread(def, World.WalkSpeed, 0)})");
            if (MathF.Abs(held - stand) > 1e-6f)
                Fail($"{def.Name}: holding Sprint while standing still widened the spread");
            var want = def.HipSpreadRad * SprintSpread.ClassMult(c);
            if (sprint < want || ratio < 1.4f)
                Fail($"{def.Name}: sprinting spread {Deg(sprint):0.00} deg is not at least the class penalty ({Deg(want):0.00} deg)");
            if (Gap(sprint) < Gap(walk) * 1.4f)
                Fail($"{def.Name}: the crosshair barely blooms while sprinting ({Gap(walk):0} -> {Gap(sprint):0} px)");

            // The server: a tight cone sent while sprinting is widened; the honest predicted cone passes unchanged.
            me.Sprinting = true;
            var widened = Game.ServerClampSpread(me, def, 0.0005f);
            var honest = Game.ServerClampSpread(me, def, sprint);
            me.Sprinting = false;
            me.SprintRecoverTicks = 0;
            var normal = Game.ServerClampSpread(me, def, 0.0005f);
            if (c is WeaponClass.Sniper or WeaponClass.SMG)
                L($"  server: a 0.03 deg cone while sprinting -> {Deg(widened):0.00} deg (not sprinting -> {Deg(normal):0.000} deg, the old floor)");
            if (widened < def.HipSpreadRad * SprintSpread.ClassMult(c) * SprintSpread.ServerSlack - 1e-6f)
                Fail($"{def.Name}: the server let a tight cone through while sprinting ({Deg(widened):0.000} deg)");
            if (MathF.Abs(honest - sprint) > 1e-6f)
                Fail($"{def.Name}: the server changed an honest sprinting cone ({Deg(sprint):0.00} -> {Deg(honest):0.00} deg)");
            if (MathF.Abs(normal - Math.Clamp(0.0005f, def.MinSpreadRad, def.MaxSpreadRad)) > 1e-7f)
                Fail($"{def.Name}: the server's floor changed when not sprinting");
            // Honest walking / standing cones pass the server unchanged too.
            foreach (var s in new[] { stand, walk, standAds })
                if (MathF.Abs(Game.ServerClampSpread(me, def, s) - s) > 1e-6f)
                    Fail($"{def.Name}: the server changed an honest non-sprinting cone ({Deg(s):0.000} deg)");
        }
        if (!(multByClass.GetValueOrDefault(WeaponClass.Sniper) > multByClass.GetValueOrDefault(WeaponClass.SMG)
              && multByClass.GetValueOrDefault(WeaponClass.DMR) > multByClass.GetValueOrDefault(WeaponClass.Shotgun)
              && multByClass.GetValueOrDefault(WeaponClass.LMG) > multByClass.GetValueOrDefault(WeaponClass.SMG)))
            Fail("snipers / DMRs / LMGs should be penalised more than SMGs / shotguns");

        // Composition: Steady Aim stays multiplicative with the sprint penalty.
        var ar = Weapons.DefaultPrimary;
        var steady = new PerkSet(PerkId.None, PerkId.None, PerkId.SteadyAim);
        var sNone = Spread(ar, 0, false);
        me.Sprinting = true; // standing still but flagged sprinting: isolates the multiplier from the movement term
        var sprintNone = game.CurrentSpread();
        game.SelfTestPerks(me, steady);
        var sprintSteady = game.CurrentSpread();
        L($"Steady Aim while sprinting ({ar.Name}): {Deg(sprintNone):0.00} -> {Deg(sprintSteady):0.00} deg (x{sprintSteady / sprintNone:0.00}); not sprinting {Deg(sNone):0.00} deg");
        if (MathF.Abs(sprintSteady / sprintNone - Perks.SteadyAimHipMult) > 1e-3f)
            Fail("Steady Aim doesn't compose multiplicatively with the sprint penalty");
        game.SelfTestPerks(me, PerkSet.Empty);

        // Recovery window: sprint, then stop; the penalty fades to nothing over RecoveryTicks.
        var sniper = Weapons.All.First(w => w.Class == WeaponClass.Sniper);
        Spread(sniper, World.SprintSpeed, true);
        var trace = new List<string>();
        var ticks = 0;
        var last = game.CurrentSpread();
        var monotonic = true;
        while (SprintSpread.Amount(me) > 0 && ticks < 200)
        {
            me.Sim.Velocity = new Vector3(0, 0, -World.WalkSpeed); // slowed to a walk
            SprintSpread.Tick(me, Buttons.Forward);
            ticks++;
            var s = game.CurrentSpread();
            monotonic &= s <= last + 1e-7f;
            last = s;
            if (ticks % 6 == 0)
                trace.Add($"{ticks * World.TickDt:0.00} s: {Deg(s):0.00} deg");
        }
        var walkSniper = Spread(sniper, World.WalkSpeed, false);
        L($"recovery ({sniper.Name}): sprint {Deg(Spread(sniper, World.SprintSpeed, true)):0.00} deg, then {string.Join(", ", trace)}; back to walking ({Deg(walkSniper):0.00} deg) after {ticks} ticks = {ticks * World.TickDt:0.000} s");
        if (ticks != SprintSpread.RecoveryTicks || !monotonic || MathF.Abs(last - walkSniper) > 1e-6f)
            Fail($"recovery took {ticks} ticks (expected {SprintSpread.RecoveryTicks}), monotonic {monotonic}");
        // The server forgives the start of the window (shots and inputs arrive at different times).
        Spread(sniper, World.SprintSpeed, true);
        me.Sprinting = false; // just stopped: full window left
        var amounts = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            me.SprintRecoverTicks = SprintSpread.RecoveryTicks - i * 6;
            amounts.Add($"{SprintSpread.ServerAmount(me):0.00}/{SprintSpread.Amount(me):0.00}");
        }
        L($"server sprint amount every 0.1 s of the window (server/client): {string.Join("  ", amounts)}");
        for (var t = 0; t <= SprintSpread.RecoveryTicks; t++)
        {
            me.SprintRecoverTicks = t;
            if (SprintSpread.ServerAmount(me) > SprintSpread.Amount(me) + 1e-6f)
                Fail("the server's recovery amount is stricter than the client's");
        }
        me.SprintRecoverTicks = 0;

        // The real server path: shots sent with a 0 cone (TestServerShot), standing vs sprinting, measured from the tracers.
        me.Primary = sniper.Id;
        me.PrimaryKit = 0;
        var aim = Vector3.Normalize(new Vector3(0, 0.7f, -1)); // into the sky: nothing to stop the rays early
        float MaxDeviation(bool sprinting)
        {
            float max = 0;
            for (uint seed = 1; seed <= 40; seed++)
            {
                me.Sprinting = sprinting;
                me.SprintRecoverTicks = 0;
                var before = game.Tracers.Count;
                game.TestServerShot(sniper, aim, seed);
                foreach (var t in game.Tracers.Skip(before))
                {
                    var d = Vector3.Normalize(t.To - me.Eye);
                    max = MathF.Max(max, MathF.Acos(Math.Clamp(Vector3.Dot(d, aim), -1f, 1f)));
                }
            }
            return max;
        }
        var devStand = MaxDeviation(false);
        var devSprint = MaxDeviation(true);
        me.Sprinting = false;
        var floor = sniper.HipSpreadRad * SprintSpread.ClassMult(sniper) * SprintSpread.ServerSlack;
        L($"server path ({sniper.Name}, 40 shots sent with a 0 cone): widest pellet {Deg(devStand):0.000} deg standing, {Deg(devSprint):0.00} deg sprinting (floor {Deg(floor):0.00} deg)");
        if (devStand > sniper.MinSpreadRad + 1e-3f || devSprint < floor * 0.6f || devSprint > floor + 1e-3f)
            Fail("ServerResolveShot doesn't apply the sprint floor to a tight cone");

        // Bots: a bot sprinting through the server's input path shoots worse, and recovers.
        var bot = game.Players.Values.First(p => p.Bot != null);
        game.SelfTestPerks(bot, PerkSet.Empty);
        var botDef = bot.WeaponDef;
        var botRest = Game.BotShotSpread(bot, botDef, false, BotDifficulty.Normal);
        var sprinted = false;
        uint seq = 1;
        var spawns = World.Map.Spawns;
        for (var attempt = 0; attempt < 16 && !sprinted; attempt++)
        {
            game.SelfTestPlace(bot, spawns[attempt % spawns.Count], 0);
            var yaw = attempt * MathF.PI / 2;
            for (var i = 0; i < 90 && !bot.Sprinting; i++)
                game.SelfTestApplyCmd(bot, new InputCmd { Seq = seq++, Buttons = Buttons.Forward | Buttons.Sprint, Yaw = yaw, Weapon = bot.Weapon, SpeedPct = 100 });
            sprinted = bot.Sprinting;
        }
        var botSprint = Game.BotShotSpread(bot, botDef, false, BotDifficulty.Normal);
        var botAds = Game.BotShotSpread(bot, botDef, true, BotDifficulty.Normal);
        var botServer = Game.ServerClampSpread(bot, botDef, botAds);
        for (var i = 0; i < SprintSpread.RecoveryTicks + 30; i++)
            game.SelfTestApplyCmd(bot, new InputCmd { Seq = seq++, Yaw = 0, Weapon = bot.Weapon, SpeedPct = 100 });
        var botAfter = Game.BotShotSpread(bot, botDef, false, BotDifficulty.Normal);
        var botSpeed = new Vector2(bot.Sim.Velocity.X, bot.Sim.Velocity.Z).Length();
        L($"bot ({botDef.Name}, {botDef.Class}): hip {Deg(botRest):0.00} deg at rest, {Deg(botSprint):0.00} deg sprinting (sprinted: {sprinted}), aimed-while-sprinting {Deg(botAds):0.00} -> server {Deg(botServer):0.00} deg, {Deg(botAfter):0.00} deg after stopping");
        if (!sprinted || MathF.Abs(botSprint - botRest * SprintSpread.ClassMult(botDef)) > 1e-5f || MathF.Abs(botAfter - botRest) > 1e-6f
            || botServer < botDef.HipSpreadRad * SprintSpread.ClassMult(botDef) * SprintSpread.ServerSlack - 1e-6f)
            Fail($"bots: sprint penalty not applied or not recovered (sprinted {sprinted}, speed {botSpeed:0.0}, phase {game.Phase})");
        Line("::warning title=Sprint spread (results)::" + string.Join("%0A", notes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
    }
}
