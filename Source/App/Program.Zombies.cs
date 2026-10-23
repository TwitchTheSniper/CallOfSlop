using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// Drawing the Zombies mode (docs/zombies/ZOMBIES-MODE.md): the zombies (the player rig's proportions with green skin,
/// arms out in front, glowing eyes), doors and debris (they sink into the floor when bought), boarded windows, chalk
/// outlines of the wall buys, the mystery box (its gun spin, the teddy bear), perk machines, the slot upgrade, the power
/// switch, workbenches, salvage piles and parts on the floor; the workbench in a run; and a small smoke step.
/// </summary>
public static unsafe partial class Program
{
    private static readonly Vector3 ZSkin = new(0.36f, 0.52f, 0.3f), ZShirt = new(0.33f, 0.3f, 0.26f), ZPants = new(0.18f, 0.2f, 0.27f);

    /// <summary>Everything the Zombies mode draws in the world (nothing outside Zombies maps).</summary>
    private static void DrawZombiesMode(double now)
    {
        var map = World.Map;
        if (map.Zombies is not { } zm)
            return;
        var live = _game.Mode == GameMode.Zombies;
        var t = (float)now;

        // Doors and debris: solid until bought, then they sink into the floor.
        for (var d = 0; d < zm.Doors.Count; d++)
        {
            var door = zm.Doors[d];
            var open = map.IsDoorOpen(d);
            var age = (float)(now - _game.ZDoorOpenedAt[d]);
            if (open && age > 0.8f)
                continue;
            var sink = open ? Math.Clamp(age / 0.8f, 0, 1) * door.Box.Half.Y * 2 : 0;
            var b = door.Box with { Center = door.Box.Center - new Vector3(0, sink, 0) };
            if (door.Debris)
            {
                // A pile: a few tumbled boxes in the gap.
                Gfx.DrawBox(b with { Half = b.Half * new Vector3(1, 0.5f, 1), Center = b.Center - new Vector3(0, b.Half.Y * 0.5f, 0) }, new Vector3(0.38f, 0.33f, 0.28f));
                for (var k = 0; k < 4; k++)
                {
                    var o = new Vector3(MathF.Sin(k * 2.1f) * b.Half.X * 0.5f, b.Half.Y * (0.2f + 0.2f * k), MathF.Cos(k * 1.7f) * b.Half.Z * 0.5f);
                    Gfx.Draw(Matrix4x4.CreateScale(b.Half.X * 0.9f + 0.2f, 0.25f, b.Half.Z * 0.9f + 0.2f) * Matrix4x4.CreateRotationY(k * 0.7f) * Matrix4x4.CreateTranslation(b.Center + o),
                        k % 2 == 0 ? new Vector3(0.45f, 0.32f, 0.2f) : new Vector3(0.5f, 0.5f, 0.48f));
                }
            }
            else
            {
                Gfx.DrawBox(b, new Vector3(0.42f, 0.28f, 0.16f));
                Gfx.DrawBox(b with { Half = new Vector3(MathF.Max(b.Half.X, 0.06f) + 0.01f, 0.05f, MathF.Max(b.Half.Z, 0.06f) + 0.01f), Center = b.Center + new Vector3(0, b.Half.Y * 0.4f, 0) }, new Vector3(0.25f, 0.25f, 0.25f));
                Gfx.DrawBox(b with { Half = new Vector3(MathF.Max(b.Half.X, 0.06f) + 0.01f, 0.05f, MathF.Max(b.Half.Z, 0.06f) + 0.01f), Center = b.Center - new Vector3(0, b.Half.Y * 0.4f, 0) }, new Vector3(0.25f, 0.25f, 0.25f));
            }
        }

        // Windows: a frame round the opening and the boards still on it.
        for (var w = 0; w < zm.Windows.Count; w++)
        {
            var win = zm.Windows[w];
            var o = win.Opening;
            var thinX = o.Half.X <= o.Half.Z;
            var boards = live && w < _game.ZBoards.Length ? _game.ZBoards[w] : win.Boards;
            for (var k = 0; k < win.Boards; k++)
            {
                if (k >= boards)
                    continue;
                var y = o.Min.Y + (k + 0.5f) * (o.Half.Y * 2) / win.Boards;
                var tilt = MathF.Sin(k * 2.3f + w) * 0.12f;
                var size = thinX ? new Vector3(0.05f, 0.11f, o.Half.Z * 2.1f) : new Vector3(o.Half.X * 2.1f, 0.11f, 0.05f);
                var rot = thinX ? Matrix4x4.CreateRotationX(tilt) : Matrix4x4.CreateRotationZ(tilt);
                Gfx.Draw(Matrix4x4.CreateScale(size) * rot * Matrix4x4.CreateTranslation(new Vector3(o.Center.X, y, o.Center.Z)), k % 2 == 0 ? new Vector3(0.55f, 0.4f, 0.24f) : new Vector3(0.48f, 0.34f, 0.2f));
            }
        }

        // Wall buys: a chalk outline with the gun drawn on it.
        foreach (var wb in zm.WallBuys)
        {
            var rot = Matrix4x4.CreateRotationY(-wb.Yaw);
            var fwd = World.FlatForward(wb.Yaw);
            var at = wb.At + fwd * 0.02f;
            var white = new Vector3(0.92f, 0.92f, 0.88f);
            void Chalk(float x, float y, float w2, float h2) =>
                Gfx.Draw(Matrix4x4.CreateScale(w2, h2, 0.01f) * Matrix4x4.CreateTranslation(x, y, 0) * rot * Matrix4x4.CreateTranslation(at), white, 1f, mode: 2);
            Chalk(0, 0.32f, 1.3f, 0.025f);
            Chalk(0, -0.32f, 1.3f, 0.025f);
            Chalk(-0.65f, 0, 0.025f, 0.64f);
            Chalk(0.65f, 0, 0.025f, 0.64f);
            if (StandardBuilds.Find(wb.Build) is { } sb && Game.AssembleCached(sb.Build()).PreviewDef is { } def)
            {
                var side = Matrix4x4.CreateRotationY(MathF.PI / 2); // the gun lies along the wall
                DrawGun(def, Matrix4x4.CreateScale(0.9f) * side * Matrix4x4.CreateTranslation(0, -0.05f, 0.08f) * rot * Matrix4x4.CreateTranslation(at), false);
            }
        }

        // The mystery box: at its spot (the others show an empty pad).
        for (var s = 0; s < zm.BoxSpots.Count; s++)
        {
            var spot = zm.BoxSpots[s];
            var rot = Matrix4x4.CreateRotationY(-spot.Yaw);
            var here = !live || s == _game.BoxSpot;
            var props = zm.MachineProps; // a solid crate stands at every spot (Props/Zombies): the box is drawn a little bigger over it
            if (!props)
                Gfx.Draw(Matrix4x4.CreateScale(1.5f, 0.04f, 0.8f) * rot * Matrix4x4.CreateTranslation(spot.At + new Vector3(0, 0.02f, 0)), here ? new Vector3(0.35f, 0.3f, 0.2f) : new Vector3(0.25f, 0.25f, 0.25f));
            if (!here || (live && _game.BoxState == ZBoxState.Teddy && _game.BoxLeft < 1.5))
                continue;
            var open = live && _game.BoxState != ZBoxState.Idle;
            var grow = props ? 0.05f : 0f;
            Gfx.Draw(Matrix4x4.CreateScale(1.3f + grow, 0.6f + grow * 0.5f, 0.65f + grow) * rot * Matrix4x4.CreateTranslation(spot.At + new Vector3(0, 0.3f + grow * 0.25f, 0)), new Vector3(0.32f, 0.2f, 0.1f));
            Gfx.Draw(Matrix4x4.CreateScale(1.32f + grow, 0.08f, 0.67f + grow) * Matrix4x4.CreateTranslation(0, 0, open ? -0.33f : 0) * Matrix4x4.CreateRotationX(open ? 1.2f : 0) * Matrix4x4.CreateTranslation(0, 0, open ? 0.33f : 0) * rot
                     * Matrix4x4.CreateTranslation(spot.At + new Vector3(0, 0.64f + grow * 0.5f, 0)), new Vector3(0.36f, 0.24f, 0.12f));
            Gfx.Draw(Matrix4x4.CreateScale(0.3f, 0.3f, 0.01f) * Matrix4x4.CreateTranslation(0, 0, -0.33f - grow * 0.6f) * rot * Matrix4x4.CreateTranslation(spot.At + new Vector3(0, 0.32f, 0)), new Vector3(0.9f, 0.8f, 0.3f), 1f, mode: 2); // the "?"
            if (open)
            {
                Gfx.Draw(Matrix4x4.CreateScale(1.1f, 0.02f, 0.5f) * rot * Matrix4x4.CreateTranslation(spot.At + new Vector3(0, 0.62f, 0)), new Vector3(0.6f, 0.9f, 1f), 1f, mode: 2); // the glow inside
                var builds = StandardBuilds.From("box").ToList();
                var shown = _game.BoxState switch
                {
                    ZBoxState.Spinning when builds.Count > 0 => builds[(int)(now * 7) % builds.Count],
                    ZBoxState.Offering when _game.BoxBuild >= 0 && _game.BoxBuild < StandardBuilds.All.Length => StandardBuilds.All[_game.BoxBuild],
                    _ => null,
                };
                var rise = _game.BoxState == ZBoxState.Spinning ? 0.5f + 0.4f * (1 - (float)Math.Clamp(_game.BoxLeft / _game.ZRules.BoxSpinSeconds, 0, 1)) : 0.9f;
                var at = spot.At + new Vector3(0, 0.6f + rise, 0);
                if (_game.BoxState == ZBoxState.Teddy || (_game.BoxState == ZBoxState.Spinning && _game.BoxBuild == -2 && _game.BoxLeft < 0.6))
                {
                    // The teddy bear: a brown bear sitting over the box.
                    Gfx.Draw(Matrix4x4.CreateScale(0.3f, 0.35f, 0.25f) * Matrix4x4.CreateTranslation(at), new Vector3(0.55f, 0.35f, 0.18f));
                    Gfx.Draw(Matrix4x4.CreateScale(0.24f) * Matrix4x4.CreateTranslation(at + new Vector3(0, 0.3f, 0)), new Vector3(0.6f, 0.4f, 0.2f));
                    Gfx.Draw(Matrix4x4.CreateScale(0.08f) * Matrix4x4.CreateTranslation(at + new Vector3(-0.1f, 0.44f, 0)), new Vector3(0.5f, 0.3f, 0.15f));
                    Gfx.Draw(Matrix4x4.CreateScale(0.08f) * Matrix4x4.CreateTranslation(at + new Vector3(0.1f, 0.44f, 0)), new Vector3(0.5f, 0.3f, 0.15f));
                }
                else if (shown != null && Game.AssembleCached(shown.Build()).PreviewDef is { } def)
                    DrawGun(def, Matrix4x4.CreateRotationY(-spot.Yaw + MathF.PI / 2 + (_game.BoxState == ZBoxState.Offering ? MathF.Sin(t) * 0.2f : 0)) * Matrix4x4.CreateTranslation(at), false);
            }
        }

        // Perk machines: tall coloured cabinets (lit when the power is on, or when they don't need it).
        foreach (var m in zm.PerkMachines)
        {
            var perk = _game.ZRules.Perk(m.Perk);
            var c = perk?.Color ?? new Vector3(0.6f);
            var lit = !m.NeedsPower || _game.ZPower;
            var rot = Matrix4x4.CreateRotationY(-m.Yaw);
            if (zm.MachineProps)
                Gfx.Draw(Matrix4x4.CreateScale(0.8f, 0.97f, 0.01f) * Matrix4x4.CreateTranslation(0, 0, -0.337f) * rot * Matrix4x4.CreateTranslation(m.At + new Vector3(0, 0.9f, 0)), c * 0.6f); // the perk's colour on the Perk Machine prop's front
            else
                Gfx.Draw(Matrix4x4.CreateScale(0.9f, 2.0f, 0.7f) * rot * Matrix4x4.CreateTranslation(m.At + new Vector3(0, 1f, 0)), c * 0.55f);
            Gfx.Draw(Matrix4x4.CreateScale(0.7f, 0.5f, 0.02f) * Matrix4x4.CreateTranslation(0, 0, -0.36f) * rot * Matrix4x4.CreateTranslation(m.At + new Vector3(0, 1.62f, 0)),
                lit ? c * (1.1f + 0.15f * MathF.Sin(t * 3)) : c * 0.3f, 1f, mode: lit ? 2 : 0);
            Gfx.Draw(Matrix4x4.CreateScale(0.25f, 0.4f, 0.02f) * Matrix4x4.CreateTranslation(0, 0, -0.36f) * rot * Matrix4x4.CreateTranslation(m.At + new Vector3(0, 0.8f, 0)), new Vector3(0.1f, 0.1f, 0.1f));
        }

        // The weapon-slot upgrade (a crate with two gun racks), the power switch, workbenches, salvage piles.
        if (zm.SlotUpgrade is { } su)
        {
            var owned = _game.Local?.ExtraSlots == true;
            var rot = Matrix4x4.CreateRotationY(-su.Yaw);
            if (!zm.MachineProps) // (with machine props: the Weapon Locker prop)
                Gfx.Draw(Matrix4x4.CreateScale(1.0f, 1.2f, 0.6f) * rot * Matrix4x4.CreateTranslation(su.At + new Vector3(0, 0.6f, 0)), new Vector3(0.25f, 0.3f, 0.25f));
            Gfx.Draw(Matrix4x4.CreateScale(0.8f, 0.3f, 0.02f) * Matrix4x4.CreateTranslation(0, 0, -0.31f) * rot * Matrix4x4.CreateTranslation(su.At + new Vector3(0, 1.0f, 0)),
                owned ? new Vector3(0.3f, 0.9f, 0.4f) : new Vector3(1f, 0.75f, 0.2f) * (0.8f + 0.2f * MathF.Sin(t * 3)), 1f, mode: 2);
        }
        if (zm.Power is { } pw)
        {
            var rot = Matrix4x4.CreateRotationY(-pw.Yaw);
            if (!zm.MachineProps) // (with machine props: the Power Switch Box prop)
                Gfx.Draw(Matrix4x4.CreateScale(0.7f, 1.0f, 0.25f) * rot * Matrix4x4.CreateTranslation(pw.At + new Vector3(0, 1.1f, 0)), new Vector3(0.3f, 0.32f, 0.35f));
            var lever = _game.ZPower ? -0.6f : 0.6f;
            Gfx.Draw(Matrix4x4.CreateScale(0.08f, 0.45f, 0.08f) * Matrix4x4.CreateTranslation(0, 0.2f, 0) * Matrix4x4.CreateRotationX(lever) * Matrix4x4.CreateTranslation(0, 0, -0.18f) * rot
                     * Matrix4x4.CreateTranslation(pw.At + new Vector3(0, 1.1f, 0)), new Vector3(0.7f, 0.1f, 0.1f));
            Gfx.Draw(Matrix4x4.CreateScale(0.1f) * Matrix4x4.CreateTranslation(0, 0.38f, -0.14f) * rot * Matrix4x4.CreateTranslation(pw.At + new Vector3(0, 1.1f, 0)),
                _game.ZPower ? new Vector3(0.3f, 1f, 0.4f) : new Vector3(1f, 0.2f, 0.1f), 1f, mode: 2);
        }
        foreach (var b in zm.Benches)
        {
            if (zm.MachineProps)
                break; // the Zombies Workbench prop
            var rot = Matrix4x4.CreateRotationY(-b.Yaw);
            Gfx.Draw(Matrix4x4.CreateScale(1.6f, 0.08f, 0.8f) * rot * Matrix4x4.CreateTranslation(b.At + new Vector3(0, 0.9f, 0)), new Vector3(0.45f, 0.33f, 0.2f));
            foreach (var (x, z) in new[] { (-0.7f, -0.3f), (0.7f, -0.3f), (-0.7f, 0.3f), (0.7f, 0.3f) })
                Gfx.Draw(Matrix4x4.CreateScale(0.08f, 0.88f, 0.08f) * Matrix4x4.CreateTranslation(x, 0, z) * rot * Matrix4x4.CreateTranslation(b.At + new Vector3(0, 0.44f, 0)), new Vector3(0.3f, 0.3f, 0.3f));
            Gfx.Draw(Matrix4x4.CreateScale(0.5f, 0.12f, 0.3f) * Matrix4x4.CreateTranslation(0.3f, 0, 0) * rot * Matrix4x4.CreateTranslation(b.At + new Vector3(0, 1.0f, 0)), new Vector3(0.6f, 0.15f, 0.1f)); // a tool box
            Gfx.Draw(Matrix4x4.CreateScale(0.4f, 0.03f, 0.2f) * Matrix4x4.CreateTranslation(-0.35f, 0, 0) * rot * Matrix4x4.CreateTranslation(b.At + new Vector3(0, 0.96f, 0)), new Vector3(0.55f, 0.55f, 0.6f));
        }
        foreach (var s in zm.Salvage)
            for (var k = 0; k < 5; k++)
                Gfx.Draw(Matrix4x4.CreateScale(0.35f - k * 0.04f, 0.12f, 0.25f) * Matrix4x4.CreateRotationY(k * 1.3f) * Matrix4x4.CreateTranslation(s.At + new Vector3(MathF.Sin(k) * 0.15f, 0.06f + k * 0.11f, MathF.Cos(k * 2) * 0.12f)),
                    k % 2 == 0 ? new Vector3(0.45f, 0.42f, 0.38f) : new Vector3(0.5f, 0.32f, 0.2f));

        if (!live)
            return;

        // Parts on the floor: a small spinning part box in its rarity colour.
        foreach (var d in _game.ZDrops)
        {
            var c = Parts.Find(d.Part) is { } part ? PartDef.RarityColor(part.Rarity) : new Vector3(1);
            Gfx.Draw(Matrix4x4.CreateScale(0.22f) * Matrix4x4.CreateRotationY(t * 2 + d.Id) * Matrix4x4.CreateTranslation(d.At + new Vector3(0, 0.25f + 0.06f * MathF.Sin(t * 3 + d.Id), 0)), c, 1f, mode: 2);
        }

        DrawPowerUps(now); // power-ups and special ammo flashes (below)

        // Dropped builds (ZombiesDrops.cs): the weapon's own model lying over a glowing ring, slowly turning and bobbing;
        // in their last seconds the ring goes red and the whole pickup blinks.
        foreach (var wd in _game.ZWeaponDrops)
        {
            var left = wd.Until - now;
            var warn = left < _game.ZRules.BuildDropWarnSeconds;
            if (warn && ((int)(now * 4) & 1) == 0)
                continue; // the blink
            var ring = warn ? new Vector3(1f, 0.2f, 0.15f) : wd.Melee ? new Vector3(0.4f, 0.75f, 1f) : new Vector3(1f, 0.8f, 0.3f);
            var spin = t * 0.8f + wd.Id;
            for (var k = 0; k < 10; k++)
            {
                var a = spin * 0.5f + k * MathF.Tau / 10;
                Gfx.Draw(Matrix4x4.CreateScale(0.16f, 0.015f, 0.05f) * Matrix4x4.CreateRotationY(a) * Matrix4x4.CreateTranslation(wd.At + new Vector3(MathF.Sin(a) * 0.45f, 0.03f, MathF.Cos(a) * 0.45f)), ring, 0.9f, mode: 2);
            }
            Gfx.Draw(Matrix4x4.CreateScale(0.06f, 1.2f, 0.06f) * Matrix4x4.CreateTranslation(wd.At + new Vector3(0, 0.6f, 0)), ring, 0.25f, mode: 2); // a faint beam so it reads from across the room
            if (Game.AssembleCached(wd.Build).PreviewDef is { } def)
                DrawGun(def, Matrix4x4.CreateRotationZ(MathF.PI / 2 * 0.9f) * Matrix4x4.CreateRotationY(spin) * Matrix4x4.CreateTranslation(wd.At + new Vector3(0, 0.35f + 0.06f * MathF.Sin(t * 2 + wd.Id), 0)), false);
        }

        // The zombies.
        foreach (var z in _game.Horde)
            DrawZombie(z, now);
    }

    /// <summary>A zombie: legs that shuffle (or run), a hunched torso, arms out in front (swiping when it attacks), green skin, glowing eyes; dead, it tips over.</summary>
    private static void DrawZombie(Zombie z, double now)
    {
        if (z.Ragdolled && Ragdolls.Enabled)
            return; // dead: its body is a ragdoll (Source/Render/Ragdolls.cs)
        if (z.State != z.SeenState)
        {
            z.SeenState = z.State;
            z.SeenStateAt = now;
        }
        var since = (float)(now - z.SeenStateAt);
        var pos = z.RenderPos;
        var body = Matrix4x4.CreateRotationY(-z.RenderYaw);
        if (z.State == ZState.Dead)
        {
            var fall = Math.Clamp(since / 0.45f, 0, 1);
            body = Matrix4x4.CreateRotationX(fall * 1.45f) * body; // backwards
            pos.Y -= Math.Clamp(since - 0.9f, 0, 1) * 0.6f; // then it sinks away
        }
        else if (z.State == ZState.Rising)
            pos.Y -= (1 - Math.Clamp(since / 0.9f, 0, 1)) * 1.6f;
        var at = body * Matrix4x4.CreateTranslation(pos);
        var swing = MathF.Sin(z.Gait) * (z.Speed == ZSpeed.Walk ? 0.35f : 0.7f);
        var hunch = z.Speed == ZSpeed.Sprint ? 0.35f : 0.15f;
        var upper = Matrix4x4.CreateRotationX(-hunch) * Matrix4x4.CreateTranslation(0, 0.86f, 0) * at;
        BodyBloodRender.PlaceZombie(z, at, upper, swing); // blood marks by health, zombie palette (Source/Render/BodyBloodRender.cs)
        BodyBloodRender.Bind(Gfx);
        // Legs (about the hips).
        for (var side = -1; side <= 1; side += 2)
        {
            var a = side * swing;
            Gfx.Draw(Matrix4x4.CreateScale(0.22f, 0.85f, 0.24f) * Matrix4x4.CreateTranslation(0, -0.42f, 0) * Matrix4x4.CreateRotationX(a) * Matrix4x4.CreateTranslation(side * 0.15f, 0.86f, 0) * at, ZPants);
        }
        // Torso and head, leaning forward.
        BodyModel.DrawZombieUpper(Gfx, upper, ZShirt, ZSkin, BodyModel.WantsDetail(pos)); // torn shirt, belly, head (Source/Render/BodyModel.cs)
        if (z.State != ZState.Dead)
            for (var side = -1; side <= 1; side += 2)
                Gfx.Draw(Matrix4x4.CreateScale(0.07f, 0.05f, 0.02f) * Matrix4x4.CreateTranslation(side * 0.09f, 0.84f, -0.23f) * upper, new Vector3(1f, 0.55f, 0.1f), 1f, mode: 2);
        // Arms out in front (a swipe brings them down and across).
        var attack = z.State == ZState.Attack ? MathF.Sin(Math.Clamp(since / 0.5f, 0, 1) * MathF.PI) : 0f;
        var tear = z.State == ZState.Tearing ? MathF.Sin((float)now * 5f + z.Id) * 0.4f : 0f;
        for (var side = -1; side <= 1; side += 2)
        {
            var raise = 1.45f - attack * 0.9f + tear * side + (z.State == ZState.Dead ? 1f : 0f) + MathF.Sin(z.Gait * 0.5f + side) * 0.08f;
            Gfx.Draw(Matrix4x4.CreateScale(0.13f, 0.66f, 0.13f) * Matrix4x4.CreateTranslation(0, -0.33f, 0) * Matrix4x4.CreateRotationX(raise) * Matrix4x4.CreateRotationY(attack * side * 0.5f)
                     * Matrix4x4.CreateTranslation(side * 0.38f, 0.55f, 0) * upper, ZSkin * 0.92f);
        }
        BodyBloodRender.Unbind(Gfx);
        if (z.State != ZState.Dead && (z.FxBurning || z.FxZapped))
        {
            // Burning: orange licks of flame round the body; zapped: blue sparks (special ammo, ZombiesAmmoEffects.cs).
            var ft = (float)now * (z.FxBurning ? 9f : 23f) + z.Id;
            for (var k = 0; k < 4; k++)
            {
                var a = ft * 0.7f + k * 1.7f;
                var o = new Vector3(MathF.Sin(a) * 0.25f, 0.5f + 0.35f * k + 0.1f * MathF.Sin(ft + k), MathF.Cos(a) * 0.2f);
                var c = z.FxBurning ? new Vector3(1f, 0.45f + 0.25f * (k & 1), 0.08f) : new Vector3(0.45f, 0.75f, 1f);
                var sz = z.FxBurning ? new Vector3(0.12f, 0.22f, 0.12f) : new Vector3(0.06f, 0.18f, 0.06f);
                Gfx.Draw(Matrix4x4.CreateScale(sz) * Matrix4x4.CreateRotationY(a) * Matrix4x4.CreateTranslation(z.RenderPos + o), c, 0.85f, mode: 2);
            }
        }
    }

    /// <summary>Power-ups (a glowing token per kind over a green halo and a beam, blinking at the end) and the special ammo flashes.</summary>
    private static void DrawPowerUps(double now)
    {
        var t = (float)now;
        foreach (var pu in _game.ZPowerUps)
        {
            var left = pu.Until - now;
            if (left < _game.ZRules.PowerBlinkSeconds && ((int)(now * (left < 3 ? 8 : 4)) & 1) == 0)
                continue; // the blink, faster at the very end
            var c = Game.PowerColor(pu.Kind);
            var at = pu.At + new Vector3(0, 0.9f + 0.1f * MathF.Sin(t * 2.5f + pu.Id), 0);
            var spin = Matrix4x4.CreateRotationY(t * 1.8f + pu.Id);
            Gfx.Draw(Matrix4x4.CreateScale(0.62f, 0.62f, 0.08f) * spin * Matrix4x4.CreateTranslation(at), new Vector3(0.25f, 1f, 0.3f), 0.35f, mode: 2); // the green halo (World at War)
            Gfx.Draw(Matrix4x4.CreateScale(0.05f, 1.6f, 0.05f) * Matrix4x4.CreateTranslation(pu.At + new Vector3(0, 0.8f, 0)), new Vector3(0.3f, 1f, 0.35f), 0.25f, mode: 2);
            switch (pu.Kind)
            {
                case ZPowerKind.MaxAmmo: // an ammo can with rounds on top
                    PowerPart(spin, at, 0.36f, 0.24f, 0.2f, 0, -0.04f, c * 0.8f);
                    for (var k = -1; k <= 1; k++)
                        PowerPart(spin, at, 0.05f, 0.14f, 0.05f, k * 0.1f, 0.15f, new Vector3(1f, 0.8f, 0.3f));
                    break;
                case ZPowerKind.InstaKill: // a skull
                    PowerPart(spin, at, 0.3f, 0.26f, 0.24f, 0, 0.04f, c);
                    PowerPart(spin, at, 0.2f, 0.1f, 0.2f, 0, -0.13f, c * 0.9f);
                    PowerPart(spin, at, 0.07f, 0.07f, 0.26f, -0.07f, 0.05f, new Vector3(0.05f));
                    PowerPart(spin, at, 0.07f, 0.07f, 0.26f, 0.07f, 0.05f, new Vector3(0.05f));
                    break;
                case ZPowerKind.DoublePoints: // two gold coins
                    PowerPart(spin, at, 0.22f, 0.22f, 0.05f, -0.1f, 0, c);
                    PowerPart(spin, at, 0.22f, 0.22f, 0.05f, 0.1f, 0.03f, c * 0.85f);
                    break;
                case ZPowerKind.Nuke: // a round bomb with fins
                    PowerPart(spin, at, 0.26f, 0.26f, 0.26f, 0, 0, c * 0.7f);
                    PowerPart(spin, at, 0.2f, 0.06f, 0.06f, 0, 0.18f, new Vector3(0.3f));
                    PowerPart(spin, at, 0.06f, 0.18f, 0.2f, 0, 0.18f, new Vector3(0.3f));
                    break;
                default: // the Carpenter: a hammer
                    PowerPart(spin, at, 0.05f, 0.34f, 0.05f, 0, -0.04f, new Vector3(0.6f, 0.4f, 0.2f));
                    PowerPart(spin, at, 0.24f, 0.08f, 0.08f, 0, 0.14f, c);
                    break;
            }
        }
        foreach (var (time, kind, pos) in _game.ZFxList)
        {
            var age = (float)(now - time);
            if (age < 0 || age > 0.4f)
                continue;
            var k = age / 0.4f;
            var (size, col) = kind switch
            {
                ZFxKind.Splash => (0.4f + 2.2f * k, new Vector3(1f, 0.55f, 0.15f)),
                ZFxKind.Shock => (0.3f + 0.5f * k, new Vector3(0.5f, 0.8f, 1f)),
                ZFxKind.Ignite => (0.3f + 0.6f * k, new Vector3(1f, 0.4f, 0.05f)),
                ZFxKind.Party => (0.2f + 0.4f * k, new Vector3(1f, 0.4f, 0.8f)),
                ZFxKind.Fish => (0.25f + 0.4f * k, new Vector3(0.6f, 0.85f, 1f)),
                _ => (0.15f + 0.3f * k, new Vector3(1f, 1f, 0.7f)),
            };
            Gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateRotationY(k * 2f) * Matrix4x4.CreateTranslation(pos), col, 0.8f * (1 - k), mode: 2);
        }
    }

    private static void PowerPart(Matrix4x4 spin, Vector3 at, float sx, float sy, float sz, float x, float y, Vector3 col) =>
        Gfx.Draw(Matrix4x4.CreateScale(sx, sy, sz) * Matrix4x4.CreateTranslation(x, y, 0) * spin * Matrix4x4.CreateTranslation(at), col, 1f, mode: 2);

    // ---- Input -------------------------------------------------------------------------------------------

    /// <summary>Every frame of input in a match: the interact key's hold state (Zombies), and E at a workbench opens it.</summary>
    private static void ZombiesInput(double now, bool interactPressed)
    {
        if (_game.Mode != GameMode.Zombies)
            return;
        _game.SetUseHeld(_mouseCaptured && Down(GameAction.Interact) || _zSmokeUseHeld, now); // (_zSmokeUseHeld: the smoke step's box spin)
        if (interactPressed && _game.Local is { } me && _game.FindZUse(me) is { Kind: ZUseKind.Workbench })
        {
            Ui.OpenBench(_game);
            SetMouseCaptured(false);
        }
    }

    /// <summary>The workbench in a run, over the match (Esc or Close goes back to it).</summary>
    private static void DrawBenchInMatch(double now)
    {
        if (Ui.BenchRun == null)
            return;
        if (!Ui.WorkbenchOpen || _session == null || _game.Local is not { Alive: true })
        {
            Ui.CloseBench();
            SetMouseCaptured(true);
            _swallowClick = true;
            return;
        }
        Ui.DrawWorkbench(AudioSystem, now);
        if (!Ui.WorkbenchOpen)
        {
            Ui.CloseBench();
            SetMouseCaptured(true);
            _swallowClick = true;
        }
    }

    // ---- Smoke step: Zombies Test (start room, zombies at a barrier, the box open, the HUD) ----------------

    private static int _zSmokeStage;
    private static double _zSmokeStart;
    private static int _zSmokeFrames;
    private static bool ZombiesSmokeActive => _zSmokeStage > 0;
    private static bool _zSmokeUseHeld;

    /// <summary>A zombie tearing boards off a window (null if none right now).</summary>
    private static Zombie? SmokeTearingZombie(Game g) => g.Horde.FirstOrDefault(z => z.State == ZState.Tearing);

    /// <summary>Points the camera from <paramref name="eye"/> at <paramref name="at"/>.</summary>
    private static void SmokeLookAt(Game g, Vector3 at, Vector3 eye)
    {
        var to = at - eye;
        g.LookYaw = MathF.Atan2(to.X, -to.Z);
        g.LookPitch = MathF.Atan2(to.Y, new Vector2(to.X, to.Z).Length());
    }

    /// <summary>Teleports the local player just inside the window a zombie is tearing at (false if none is).</summary>
    private static bool SmokeStandAtTearingWindow(Game g)
    {
        if (SmokeTearingZombie(g) is not { } tz || World.Map.Zombies is not { } zw || tz.Window < 0 || tz.Window >= zw.Windows.Count || g.Local is not { } me)
            return false;
        var win = zw.Windows[tz.Window];
        var inward = win.Inside - win.Outside;
        inward.Y = 0;
        inward = inward.LengthSquared() > 1e-4f ? Vector3.Normalize(inward) : World.FlatForward(win.Yaw);
        me.Teleport(win.Inside + inward * 1.6f);
        return true;
    }

    private static void StartZombiesSmoke(double now)
    {
        if ((Maps.Find("Blackmoor Asylum") ?? Maps.Find("Zombies Test")) is not { } map) // the first real Zombies map (milestone 3), else the dev map
        {
            Console.WriteLine("[smoke] zombies: no Zombies map, skipped");
            return;
        }
        _settings.MapName = map.Name;
        _settings.MapRandom = false;
        _settings.Mode = GameMode.Zombies;
        _settings.Bots = 1;
        StartSession(LobbyAction.SinglePlayer);
        _zSmokeStage = _session != null ? 1 : 0;
        _zSmokeStart = now;
        Console.WriteLine($"[smoke] zombies: session {(_session != null ? "ok" : "FAILED")}");
    }

    private static void ZombiesSmokeTick(double now)
    {
        var t = now - _zSmokeStart;
        var g = _session;
        if (g == null)
        {
            _zSmokeStage = 0;
            return;
        }
        switch (_zSmokeStage)
        {
            case 1 when t > 0.5:
                g.SetReady(true);
                g.RequestRespawn();
                _zSmokeStage = 2;
                break;
            case 2 when g.Phase == MatchPhase.Playing && g.Local is { Alive: true } || t > 20:
                if (g.Local is { } host)
                    host.ImmuneUntil = now + 600; // the smoke run never goes down
                _zSmokeStart = now;
                _zSmokeFrames = _smokeFrames;
                _zSmokeStage = 3;
                break;
            case 3 when t > 1.5:
                SmokeShot("zombies_start");
                Console.WriteLine($"[smoke] zombies start room: {(_smokeFrames - _zSmokeFrames) / 1.5:0} fps, round {g.ZRound}, points {g.Local?.Let(p => g.ZP(p).Points)}");
                _zSmokeStage = 4;
                break;
            case 4 when g.Horde.Any(z => z.State == ZState.Tearing) || t > 40:
            {
                // Stand inside the window a zombie is tearing at (the start room may not see it) and look at the zombie.
                SmokeStandAtTearingWindow(g);
                _zSmokeStart = now;
                _zSmokeFrames = _smokeFrames;
                _zSmokeStage = 5;
                break;
            }
            case 5:
            {
                // Keep the camera on a zombie at a window (it moves, and the bot may kill it), then take the shot.
                if ((SmokeTearingZombie(g) ?? g.Horde.Where(z => z.Window >= 0).OrderBy(z => g.Local is { } p ? Vector3.DistanceSquared(z.Position, p.Position) : 0).FirstOrDefault()) is { } az
                    && g.Local is { } eyeOf)
                    SmokeLookAt(g, az.Position + new Vector3(0, 1.3f, 0), eyeOf.Position + new Vector3(0, World.EyeHeight, 0));
                if (t > 0.5)
                {
                    var torn = SmokeTearingZombie(g);
                    SmokeShot("zombies_barrier");
                    Console.WriteLine($"[smoke] zombies at a barrier: {g.AliveZombies} alive, {(torn != null ? $"one tearing at window {torn.Window} {Vector3.Distance(torn.Position, g.Local?.Position ?? torn.Position):0.0} m away" : "none tearing now")}, "
                                      + $"boards {string.Join(",", g.ZBoards)}, {(_smokeFrames - _zSmokeFrames) / t:0} fps, AI {(g.ZAiZombieTicks > 0 ? g.ZAiMs * 1000 / g.ZAiZombieTicks : 0):0.0} us per zombie per tick");
                    _zSmokeStart = now;
                    _zSmokeStage = 6;
                }
                break;
            }
            case 6 when t > 0.7:
            {
                // The box: walk there (teleport), buy a spin with points, look at it, hold interact (through the normal input path).
                if (World.Map.Zombies is { BoxSpots.Count: > 0 } zm && g.Local is { } me)
                {
                    var spot = zm.BoxSpots[g.BoxSpot];
                    var stand = spot.At + World.FlatForward(spot.Yaw) * 1.6f; // in front of the box (its "?" side)
                    me.Teleport(stand);
                    g.ZP(me).Points += 2000;
                    SmokeLookAt(g, spot.At + new Vector3(0, 1.1f, 0), stand + new Vector3(0, World.EyeHeight, 0));
                    if (g.IsServer) // a power-up floating beside the box, in the box shot
                        g.SpawnPowerUp(ZPowerKind.DoublePoints, spot.At + World.FlatForward(spot.Yaw + MathF.PI / 2) * 1.6f + World.FlatForward(spot.Yaw) * 0.6f, now);
                    _zSmokeUseHeld = true;
                    Console.WriteLine($"[smoke] zombies box: holding interact at the box, prompt \"{g.FindZUse(me)?.Text}\", points {g.ZP(me).Points}");
                }
                _zSmokeStart = now;
                _zSmokeStage = 7;
                break;
            }
            case 7 when t > Math.Max(0.6, g.ZRules.BuyHoldSeconds + 0.3):
            {
                _zSmokeUseHeld = false;
                if (g.BoxState == ZBoxState.Idle && g.IsServer && g.Local is { } host2)
                {
                    // The hold didn't take (input or focus): spin it through the server's interact path instead.
                    Console.WriteLine("[smoke] zombies box: the held key didn't spin the box, using the server interact path");
                    g.SelfTestUse(host2, now);
                }
                Console.WriteLine($"[smoke] zombies box: {g.BoxState} after the hold");
                _zSmokeStart = now;
                _zSmokeStage = 8;
                break;
            }
            case 8 when g.BoxState is ZBoxState.Offering or ZBoxState.Teddy || t > g.ZRules.BoxSpinSeconds + 2:
                SmokeShot("zombies_box");
                Console.WriteLine($"[smoke] zombies box: {g.BoxState}, offered {(g.BoxBuild >= 0 && g.BoxBuild < StandardBuilds.All.Length ? StandardBuilds.All[g.BoxBuild].Name : g.BoxBuild == -2 ? "the teddy bear (box moves)" : "nothing (build " + g.BoxBuild + ")")}");
                _zSmokeStart = now;
                _zSmokeStage = 9;
                break;
            case 9 when t > 1.0 && g.AliveZombies > 0 || t > 12:
            {
                // The HUD mid-round: back at a window with a zombie at it (or the nearest zombie), so the shot has
                // zombies, the round counter, points and the teammate list in it.
                SmokeStandAtTearingWindow(g);
                _zSmokeStart = now;
                _zSmokeStage = 10;
                break;
            }
            case 10:
            {
                if (g.Local is { } hudMe && (SmokeTearingZombie(g) ?? g.Horde.OrderBy(z => Vector3.DistanceSquared(z.Position, hudMe.Position)).FirstOrDefault()) is { } nz)
                    SmokeLookAt(g, nz.Position + new Vector3(0, 1.3f, 0), hudMe.Position + new Vector3(0, World.EyeHeight, 0));
                if (t < 0.4)
                    break;
                SmokeShot("zombies_hud"); // saved at the end of this frame, while the session is still up
                Console.WriteLine($"[smoke] zombies HUD: map {World.Map.Name}, round {g.ZRound}, {g.ZRemaining} left, {g.AliveZombies} alive, points {g.Local?.Let(p => g.ZP(p).Points)}, {g.Feed.Count} feed lines");
                _zSmokeStage = 11;
                break;
            }
            case 11:
                // A frame later (the HUD shot is on disk): end the session; the next smoke step starts after this.
                EndSession("");
                _settings.Mode = GameMode.FreeForAll;
                _zSmokeStage = 0;
                break;
        }
    }
}

/// <summary>Small helpers for the smoke step's log lines.</summary>
internal static class ZSmokeExt
{
    public static TR Let<T, TR>(this T v, Func<T, TR> f) => f(v);
}
