using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Friendly hits": in a team mode, shooting, meleeing or blasting a teammate gives the shooter a
/// friendly-flagged hit confirmation (no damage, the friendly marker), while an enemy gets the normal one; free-for-all
/// never flags a hit as friendly; Zombies treats co-op players as teammates; the friendly hit sound exists and differs
/// from the enemy hit sounds; and a client turns the replicated flag into the friendly marker.
/// </summary>
public static partial class SelfTest
{
    private static void CheckFriendlyHits()
    {
        const byte F = Game.ShotHitFriendly;

        // ---- Team Deathmatch: bullets, melee and a grenade on a teammate vs an enemy ----
        using (var game = ModeGame(3, GameMode.TeamDeathmatch))
        {
            var a = game.Players[0];
            var mate = game.Players.Values.First(p => p != a && p.Team == a.Team);
            var foe = game.Players.Values.First(p => p.Team != a.Team);
            if (game.LocalId != a.Id)
                Fail($"friendly hits: the host isn't player 0 (local {game.LocalId})");
            foreach (var p in game.Players.Values)
                game.SelfTestPerks(p, PerkSet.Empty);
            var now = 10.0;

            (byte Flags, int Damage, bool Marker) Shoot(Player target)
            {
                var other = target == mate ? foe : mate;
                game.SelfTestPlace(a, new Vector3(5, 0, 5), MathF.PI);
                game.SelfTestPlace(target, new Vector3(5, 0, 8), 0);
                game.SelfTestPlace(other, new Vector3(-20, 0, -20), 0);
                game.LastHitFlags = 0;
                game.HitMarkerFriendly = false;
                now += 1;
                game.SelfTestShoot(a, Vector3.Normalize(target.Chest - a.Eye), now);
                return (game.LastHitFlags, World.MaxHealth - target.Health, game.HitMarkerFriendly && now < game.HitMarkerUntil);
            }

            (byte Flags, int Damage, bool Marker) Swing(Player target)
            {
                var other = target == mate ? foe : mate;
                game.SelfTestPlace(a, new Vector3(5, 0, 5), MathF.PI);
                game.SelfTestPlace(target, new Vector3(5, 0, 6.3f), 0);
                game.SelfTestPlace(other, new Vector3(-20, 0, -20), 0);
                game.LastHitFlags = 0;
                game.HitMarkerFriendly = false;
                now += 5;
                game.SelfTestMelee(a, MathF.PI, 0, now);
                return (game.LastHitFlags, World.MaxHealth - target.Health, game.HitMarkerFriendly && now < game.HitMarkerUntil);
            }

            (byte Flags, int Damage, bool Marker) Boom(Player target)
            {
                var other = target == mate ? foe : mate;
                game.SelfTestPlace(a, new Vector3(-20, 0, -20), 0);
                game.SelfTestPlace(target, new Vector3(5, 0, 5), 0);
                game.SelfTestPlace(other, new Vector3(10, 0, -12), 0);
                game.LastHitFlags = 0;
                game.HitMarkerFriendly = false;
                now += 1;
                game.SelfTestDetonate(a, ThrowableKind.Frag, target.Position + new Vector3(0.6f, 0.2f, 0), now);
                return (game.LastHitFlags, World.MaxHealth - target.Health, game.HitMarkerFriendly && now < game.HitMarkerUntil);
            }

            void Expect(string what, (byte Flags, int Damage, bool Marker) r, bool friendly)
            {
                Line($"{what}: flags {r.Flags}, damage {r.Damage}, friendly marker {r.Marker}");
                if (friendly && ((r.Flags & F) == 0 || (r.Flags & 1) != 0 || r.Damage != 0 || !r.Marker))
                    Fail($"{what}: expected a friendly-flagged hit with no damage and the friendly marker");
                if (!friendly && ((r.Flags & 1) == 0 || (r.Flags & F) != 0 || r.Damage <= 0 || r.Marker))
                    Fail($"{what}: expected a normal damaging hit with the normal marker");
            }

            Expect("TDM bullet on a teammate", Shoot(mate), true);
            Expect("TDM bullet on an enemy", Shoot(foe), false);
            Expect("TDM melee on a teammate", Swing(mate), true);
            Expect("TDM melee on an enemy", Swing(foe), false);
            Expect("TDM grenade on a teammate", Boom(mate), true);
            Expect("TDM grenade on an enemy", Boom(foe), false);
        }

        // ---- Free-for-all: everyone is an enemy ----
        using (var game = ModeGame(1, GameMode.FreeForAll))
        {
            var a = game.Players[0];
            var b = game.Players.Values.First(p => p != a);
            foreach (var p in game.Players.Values)
                game.SelfTestPerks(p, PerkSet.Empty);
            game.SelfTestPlace(a, new Vector3(5, 0, 5), MathF.PI);
            game.SelfTestPlace(b, new Vector3(5, 0, 8), 0);
            game.LastHitFlags = 0;
            game.SelfTestShoot(a, Vector3.Normalize(b.Chest - a.Eye), 10);
            var shot = game.LastHitFlags;
            game.SelfTestPlace(b, new Vector3(5, 0, 6.3f), 0);
            game.LastHitFlags = 0;
            game.SelfTestMelee(a, MathF.PI, 0, 20);
            var melee = game.LastHitFlags;
            game.SelfTestPlace(a, new Vector3(-20, 0, -20), 0);
            game.SelfTestPlace(b, new Vector3(5, 0, 5), 0);
            game.LastHitFlags = 0;
            game.SelfTestDetonate(a, ThrowableKind.Frag, b.Position + new Vector3(0.6f, 0.2f, 0), 30);
            var blast = game.LastHitFlags;
            Line($"FFA: bullet flags {shot}, melee flags {melee}, grenade flags {blast}");
            if (((shot | melee | blast) & F) != 0 || (shot & 1) == 0 || (melee & 1) == 0 || (blast & 1) == 0)
                Fail("FFA: a hit was flagged friendly, or a hit didn't confirm normally");
        }

        // ---- Zombies: co-op players are teammates (no friendly fire, the friendly marker) ----
        if (ZTestMap != null)
        {
            var now = 0.0;
            using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
            if (g.Local is { } me)
            {
                var mate = g.SelfTestAddHuman();
                g.SelfTestSpawn(mate);
                var at = me.Position;
                g.SelfTestPlace(me, at, MathF.PI);
                g.SelfTestPlace(mate, at + new Vector3(0, 0, 1.3f), 0);
                g.LastHitFlags = 0;
                g.SelfTestMelee(me, MathF.PI, 0, now + 5);
                var hp = mate.Health;
                Line($"Zombies: melee on a co-op teammate: flags {g.LastHitFlags}, teammate {hp} HP");
                if ((g.LastHitFlags & F) == 0 || (g.LastHitFlags & 1) != 0 || hp != World.MaxHealth)
                    Fail("Zombies: hitting a co-op teammate should be a friendly hit with no damage");
            }
            else
                Fail("Zombies: no local player");
        }

        // ---- The sound: exists, and isn't the enemy hit tick or the headshot crunch ----
        var friendly = Synth.Effect(Sfx.FriendlyHit);
        var body = Synth.Effect(Sfx.HitBody);
        var head = Synth.Effect(Sfx.Headshot);
        var (ff, fb, fh) = (Features(friendly), Features(body), Features(head));
        Line($"friendly hit sound: {friendly.Length} samples, brightness {ff.Bright:0.000} (body hit {fb.Bright:0.000}, headshot {fh.Bright:0.000}), "
             + $"length {ff.Length * 1000:0} ms (body hit {fb.Length * 1000:0} ms), envelope vs body hit {EnvelopeCorrelation(friendly, body):0.00}");
        if (friendly.Length < 200 || friendly.AsSpan().SequenceEqual(body) || friendly.AsSpan().SequenceEqual(head))
            Fail("the friendly hit sound is missing or the same as an enemy hit sound");
        if (ff.Bright > fb.Bright * 0.8f && ff.Length < fb.Length * 1.3f)
            Fail("the friendly hit sound isn't clearly duller or longer than the enemy body hit");

        // ---- Replication: a client turns the flag into the friendly marker ----
        var client = new Game(NetPeer.OfflineClient(), new Audio(), "Guest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        using (client)
        {
            client.LocalId = 3;
            client.Players[3] = new Player { Id = 3, Name = "Guest" };
            var w = new PacketWriter();
            bool Marker(double t) => client.HitMarkerFriendly && t < client.HitMarkerUntil;
            w.Begin(Msg.Shot).U8(3).U8(Weapons.DefaultPrimary.Id).U8(F).U8(0);
            client.SelfTestPacket(default(RakNetGUID), w.Span, 1);
            var shot = Marker(1.05);
            w.Begin(Msg.Shot).U8(3).U8(Weapons.DefaultPrimary.Id).U8(1 | Game.ShotHitPlayer).U8(0);
            client.SelfTestPacket(default(RakNetGUID), w.Span, 2);
            var enemy = !client.HitMarkerFriendly && 2.05 < client.HitMarkerUntil;
            w.Begin(Msg.MeleeFx).U8(3).U8(F);
            client.SelfTestPacket(default(RakNetGUID), w.Span, 3);
            var melee = Marker(3.05);
            w.Begin(Msg.Explode).U16(7).U8((byte)ThrowableKind.Frag).U8(3).Vec(new Vector3(0, 0, 0)).U8(F);
            client.SelfTestPacket(default(RakNetGUID), w.Span, 4);
            var blast = Marker(4.05);
            Line($"client: friendly marker from Shot {shot}, MeleeFx {melee}, Explode {blast}; an enemy hit after shows the normal marker {enemy}");
            if (!shot || !melee || !blast || !enemy)
                Fail("client: the replicated friendly flag didn't show the friendly marker (or an enemy hit kept it)");
        }
    }
}
