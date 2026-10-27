using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Ammo effects on players" (Source/Game/AmmoEffectsPlayers.cs; developer answer, 2026-10-26): a gun
/// whose parts carry special ammo flags hurts players too, respecting friendly fire. On a flat lab map with scripted
/// shots (no matches): the splash hurts a bystander round the target, not one behind a wall, not the shooter, not a
/// teammate; a burn ticks its damage over the burn time and stops; the shock chain skips teammates; the pierce reaches a
/// player behind; the bounce finds the nearest other enemy; the bayonet adds damage at point blank; a shot at a teammate
/// sets nothing off; a melee build's bonk stuns and its fish knocks back; nothing allocates in the burn tick, and an effect
/// shot allocates no more than a plain one.
/// </summary>
public static partial class SelfTest
{
    private static Map AmmoFxLab()
    {
        var m = new Map { Name = "Ammo effects lab", Description = "self-test scene", HalfX = 40, HalfZ = 40 };
        m.Ground(PenGrey);
        m.Block(-20.5f, 0, -12, -19.5f, 3, -4, PenBrick); // a wall west of the target spot (shields the splash)
        for (var i = 0; i < 8; i++)
            m.Spawn(-12 + i * 3, 20);
        return m;
    }

    private static void CheckAmmoEffectsPlayers()
    {
        var baseBuild = StandardBuilds.Find("Wall Rifle");
        var baseDef = baseBuild == null ? null : WeaponBuilder.Assemble(baseBuild.Build()).Def;
        if (baseDef == null)
        {
            Fail("ammo effects on players: no Wall Rifle build to test with");
            return;
        }
        WeaponDef Gun(params string[] flags)
        {
            var d = baseDef.Clone();
            d.BuildFlags = flags;
            return d;
        }
        var results = new List<string>();
        var ok = true;
        var now = 10.0;

        // Shoots target `b` in the chest from `a` with `def` (lag compensation at the current tick).
        void Shoot(Game g, Player a, Player b, WeaponDef def)
        {
            g.UseBuiltWeapons(def, def);
            now += 2;
            var eye = a.Eye;
            g.SelfTestPenShot(a, def, eye, Vector3.Normalize(b.Chest - eye), g.ServerTick, now);
        }

        foreach (var team in new[] { false, true })
        {
            using var g = ModeGame(0, team ? GameMode.TeamDeathmatch : GameMode.FreeForAll);
            g.SelfTestChangeMap(AmmoFxLab());
            g.ServerTick = 400;
            var a = g.Players[g.LocalId];
            var b = g.SelfTestAddHuman();
            var c = g.SelfTestAddHuman();
            var d = g.SelfTestAddHuman();
            var e = g.SelfTestAddHuman();
            foreach (var p in new[] { a, b, c, d, e })
                g.SelfTestPerks(p, PerkSet.Empty);
            // Teams: a and c together; b, d and e on the other side.
            a.Team = c.Team = 0;
            b.Team = d.Team = e.Team = (byte)(team ? 1 : 0);
            if (!team)
                a.Team = b.Team = c.Team = d.Team = e.Team = Game.NoTeam;
            void Place()
            {
                g.SelfTestPlace(a, new Vector3(-15, 0, 4), 0);
                g.SelfTestPlace(b, new Vector3(-15, 0, -8), MathF.PI); // the target, 12 m in front
                g.SelfTestPlace(c, new Vector3(-16.3f, 0, -8), 0); // 1.3 m beside it (a's teammate in TDM)
                g.SelfTestPlace(d, new Vector3(-21.2f, 0, -8), 0); // 6 m off, behind the wall
                g.SelfTestPlace(e, new Vector3(-13.6f, 0, -8.6f), 0); // 1.5 m the other side (an enemy either way)
                foreach (var p in new[] { a, b, c, d, e })
                {
                    p.BurnUntil = 0;
                    p.StunnedUntil = 0;
                    p.ImmuneUntil = 0;
                }
            }
            string Hp() => $"a {a.Health} b {b.Health} c {c.Health} d {d.Health} e {e.Health}";
            var tag = team ? "TDM" : "FFA";

            // Plain gun: only the target is hurt.
            Place();
            Shoot(g, a, b, Gun());
            var plainHit = World.MaxHealth - b.Health;
            var plainOk = plainHit > 0 && c.Health == World.MaxHealth && e.Health == World.MaxHealth;

            // Explosive: the bystanders round the target (enemies) take a splash; the wall shields d; the shooter and a teammate are safe.
            Place();
            Shoot(g, a, b, Gun("explosive"));
            var splashOk = b.Health < World.MaxHealth && e.Health < World.MaxHealth && d.Health == World.MaxHealth && a.Health == World.MaxHealth
                           && (team ? c.Health == World.MaxHealth : c.Health < World.MaxHealth);
            results.Add($"{tag} plain {plainHit} ({plainOk}); explosive: {Hp()} ({splashOk})");
            ok &= plainOk && splashOk;

            // Incendiary: the target burns, ticking 4 times a second for the burn time, then stops.
            Place();
            Shoot(g, a, b, Gun("incendiary"));
            var afterHit = b.Health;
            var burning = b.BurnUntil > now;
            var t = now;
            for (var i = 0; i < 60 * 4; i++)
            {
                t += 1.0 / 60;
                g.SelfTestTickBurns(t);
            }
            var burned = afterHit - b.Health;
            var expect = (int)MathF.Round(MathF.Max(g.ZRules.BurnMin, plainHit * g.ZRules.BurnFraction));
            var burnOk = burning && burned >= expect - 4 && burned <= expect + 4 && b.BurnUntil == 0;
            results.Add($"{tag} incendiary: hit {World.MaxHealth - afterHit}, burned {burned} more over {g.ZRules.BurnSeconds} s (about {expect}), out {b.BurnUntil == 0} ({burnOk})");
            ok &= burnOk;

            // Shock: arcs to the nearest players the shooter may damage (not a teammate), stunning them.
            Place();
            Shoot(g, a, b, Gun("shock"));
            var chainOk = e.Health < World.MaxHealth && e.StunnedUntil > now && (team ? c.Health == World.MaxHealth && c.StunnedUntil == 0 : c.Health < World.MaxHealth) && d.Health == World.MaxHealth;
            results.Add($"{tag} shock: {Hp()} ({chainOk})");
            ok &= chainOk;

            // Penetrating: on through the target to the player behind it.
            Place();
            g.SelfTestPlace(e, new Vector3(-15, 0, -11), 0); // 3 m behind b, on the line
            Shoot(g, a, b, Gun("penetrating"));
            var pierceOk = e.Health < World.MaxHealth && c.Health == World.MaxHealth;
            // Bouncy: on to the nearest other enemy.
            Place();
            Shoot(g, a, b, Gun("bouncy"));
            var bounceOk = (team ? e.Health < World.MaxHealth && c.Health == World.MaxHealth : c.Health < World.MaxHealth && e.Health == World.MaxHealth);
            results.Add($"{tag} pierce ({pierceOk}), bounce: {Hp()} ({bounceOk})");
            ok &= pierceOk && bounceOk;

            if (team)
            {
                // A shot at a teammate sets nothing off (no damage, no burn, no splash on the enemies round them).
                Place();
                g.SelfTestPlace(e, new Vector3(-17.3f, 0, -8), 0);
                Shoot(g, a, c, Gun("incendiary", "explosive", "shock"));
                var ffOk = c.Health == World.MaxHealth && c.BurnUntil == 0 && e.Health == World.MaxHealth && b.Health == World.MaxHealth;
                results.Add($"TDM a teammate shot with incendiary, explosive and shock: {Hp()}, burning {c.BurnUntil > 0} ({ffOk})");
                ok &= ffOk;
            }
            else
            {
                // Bayonet: point blank hits harder; the cup holder: a sip of health per kill.
                Place();
                g.SelfTestPlace(b, new Vector3(-15, 0, 2.5f), MathF.PI);
                Shoot(g, a, b, Gun());
                var near = World.MaxHealth - b.Health;
                Place();
                g.SelfTestPlace(b, new Vector3(-15, 0, 2.5f), MathF.PI);
                Shoot(g, a, b, Gun("bayonet"));
                var bayonet = World.MaxHealth - b.Health;
                Place();
                a.Health = 50;
                b.Health = 1;
                Shoot(g, a, b, Gun("refreshing"));
                var sip = a.Health - 50;
                var extraOk = bayonet > near && !b.Alive && sip == g.ZRules.RefreshHealth;
                results.Add($"FFA bayonet {near} -> {bayonet}, cup holder +{sip} on a kill ({extraOk})");
                ok &= extraOk;

                // A melee build: the frying pan's bonk stuns, the frozen fish knocks back.
                foreach (var name in new[] { "Frying Pan", "Flying Fish" })
                {
                    var mb = StandardBuilds.Find(name);
                    var md = mb == null ? null : WeaponBuilder.Assemble(mb.Build()).Melee;
                    if (md == null)
                    {
                        Fail($"ammo effects on players: no '{name}' melee build");
                        continue;
                    }
                    Place();
                    g.SelfTestPlace(b, new Vector3(-15, 0, 3), MathF.PI);
                    a.Pitch = 0;
                    a.NextMelee = 0;
                    var knocks = g.KnocksApplied;
                    now += 2;
                    g.SelfTestBuiltMelee(a, md, now);
                    var hurt = b.Health < World.MaxHealth;
                    var stunned = b.StunnedUntil > now;
                    var knocked = g.KnocksApplied > knocks;
                    var wantStun = md.Flags.Contains("bonk");
                    var wantKnock = md.Flags.Contains("fish") || md.Flags.Contains("rocket");
                    var meleeOk = hurt && (!wantStun || stunned) && (!wantKnock || knocked || !b.Alive);
                    results.Add($"FFA {name} [{string.Join(",", md.Flags)}]: hurt {hurt}, stunned {stunned}, knocked {knocked} ({meleeOk})");
                    ok &= meleeOk;
                }

                // Allocations: the burn tick allocates nothing; an effect shot no more than a plain one.
                Place();
                Shoot(g, a, b, Gun("incendiary"));
                var t2 = now;
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 120; i++)
                    g.SelfTestTickBurns(t2 += 1.0 / 60);
                var burnAlloc = GC.GetAllocatedBytesForCurrentThread() - before;
                long ShotAlloc(WeaponDef def)
                {
                    Place();
                    Shoot(g, a, b, def); // (warm-up)
                    Place();
                    var b0 = GC.GetAllocatedBytesForCurrentThread();
                    Shoot(g, a, b, def);
                    return GC.GetAllocatedBytesForCurrentThread() - b0;
                }
                var plainDef = Gun();
                var fxDef = Gun("explosive", "shock", "bouncy", "penetrating", "incendiary");
                var plainAlloc = ShotAlloc(plainDef);
                var fxAlloc = ShotAlloc(fxDef);
                results.Add($"allocations: burn tick {burnAlloc} bytes, a plain shot {plainAlloc}, an effect shot {fxAlloc}");
                if (burnAlloc > 0 || fxAlloc > plainAlloc)
                    Fail($"special ammo on players shouldn't allocate (burn tick {burnAlloc} bytes, effect shot {fxAlloc} vs plain {plainAlloc})");
            }
        }
        var flags = string.Join(", ", Game.PlayerEffectFlags.OrderBy(f => f, StringComparer.Ordinal));
        Line($"ammo effects on players ({flags}):");
        foreach (var r in results)
            Line("  " + r);
        if (!ok)
            Fail("special ammo effects should hurt players the way they hurt zombies (splash, burn, chain, pierce, bounce, bayonet, sip, stun, knockback), never the shooter or a teammate");
    }
}
