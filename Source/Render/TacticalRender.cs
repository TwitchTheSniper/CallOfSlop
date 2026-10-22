using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Draws the tactical devices, box-built like the streak entities (a handful of boxes each, a dozen devices at most,
/// so the same per-box draws as StreakRender): the Trophy System (a squat launcher with one lamp per charge and a
/// turning sensor head), the Motion Sensor (a puck with an antenna and a blinking lamp), the Decoy (a grenade that
/// flickers while it "fires"), the Tactical Insertion (a flare stick) and the Deployable Cover's frame. Lamps are green
/// for the viewer's own and team's devices, red for enemies'. <see cref="DrawGlow"/> adds the see-through energy wall,
/// the flash and stun bursts and the insertion's glow in the blended pass.
/// </summary>
public static class TacticalRender
{
    private static readonly Vector3 Olive = new(0.3f, 0.36f, 0.24f);
    private static readonly Vector3 DarkMetal = new(0.12f, 0.13f, 0.14f);
    private static readonly Vector3 Gunmetal = new(0.24f, 0.26f, 0.28f);
    private static readonly Vector3 FriendLamp = new(0.3f, 1f, 0.45f);
    private static readonly Vector3 FoeLamp = new(1f, 0.2f, 0.12f);

    /// <summary>Is a device owned by <paramref name="owner"/> friendly to the local viewer (theirs or a teammate's)?</summary>
    public static bool IsFriendly(Game game, byte owner) =>
        game.Local is { } me && (owner == me.Id || (game.Players.TryGetValue(owner, out var o) && !game.AreEnemies(o, me)));

    /// <summary>Opaque pass: every device's body.</summary>
    public static void Draw(Renderer gfx, Game game, double now)
    {
        foreach (var e in game.TacDevices)
        {
            var friendly = IsFriendly(game, e.Owner);
            var lamp = friendly ? FriendLamp : FoeLamp;
            var hit = now - e.LastHit < 0.12;
            var rot = Matrix4x4.CreateRotationY(-e.Yaw);
            var at = Matrix4x4.CreateTranslation(e.Pos);
            switch (e.Kind)
            {
                case ThrowableKind.Trophy:
                {
                    gfx.Draw(Matrix4x4.CreateScale(0.5f, 0.12f, 0.5f) * Matrix4x4.CreateTranslation(0, 0.06f, 0) * rot * at, hit ? Vector3.One : Olive, 1f, material: Material.Metal);
                    gfx.Draw(Matrix4x4.CreateScale(0.36f, 0.3f, 0.36f) * Matrix4x4.CreateTranslation(0, 0.27f, 0) * rot * at, Olive * 0.85f, 1f, material: Material.Metal);
                    gfx.Draw(Matrix4x4.CreateScale(0.08f, 0.25f, 0.08f) * Matrix4x4.CreateTranslation(0, 0.54f, 0) * at, DarkMetal, 1f);
                    var spin = Matrix4x4.CreateRotationY((float)(now - e.Born) * 2.2f);
                    gfx.Draw(Matrix4x4.CreateScale(0.3f, 0.12f, 0.16f) * spin * Matrix4x4.CreateTranslation(e.Pos + new Vector3(0, 0.72f, 0)), Gunmetal, 1f, material: Material.Metal);
                    gfx.Draw(Matrix4x4.CreateScale(0.1f, 0.06f, 0.02f) * Matrix4x4.CreateTranslation(0, 0, -0.09f) * spin * Matrix4x4.CreateTranslation(e.Pos + new Vector3(0, 0.72f, 0)), lamp * 1.5f, 1f, mode: 2);
                    // One lamp per charge left, around the body.
                    for (var i = 0; i < TacticalGear.TrophyCharges; i++)
                    {
                        var on = i < e.Charges;
                        var a = i * MathF.Tau / TacticalGear.TrophyCharges;
                        var p = new Vector3(MathF.Sin(a) * 0.19f, 0.32f, -MathF.Cos(a) * 0.19f);
                        gfx.Draw(Matrix4x4.CreateScale(0.06f) * Matrix4x4.CreateTranslation(p) * rot * at, on ? lamp * 1.4f : DarkMetal, 1f, mode: on ? 2 : 0);
                    }
                    break;
                }
                case ThrowableKind.MotionSensor:
                {
                    gfx.Draw(Matrix4x4.CreateScale(0.26f, 0.1f, 0.26f) * Matrix4x4.CreateTranslation(0, 0.05f, 0) * at, hit ? Vector3.One : Gunmetal, 1f, material: Material.Metal);
                    gfx.Draw(Matrix4x4.CreateScale(0.02f, 0.4f, 0.02f) * Matrix4x4.CreateTranslation(0.08f, 0.3f, 0) * at, DarkMetal, 1f);
                    var blink = ((int)((now - e.Born) * 2) & 1) == 0;
                    gfx.Draw(Matrix4x4.CreateScale(0.08f, 0.04f, 0.08f) * Matrix4x4.CreateTranslation(0, 0.12f, 0) * at, lamp * (blink ? 1.6f : 0.5f), 1f, mode: blink ? 2 : 0);
                    break;
                }
                case ThrowableKind.Decoy:
                {
                    gfx.Draw(Matrix4x4.CreateScale(0.12f, 0.12f, 0.24f) * Matrix4x4.CreateTranslation(0, 0.07f, 0) * rot * at, Throwables.Get(ThrowableKind.Decoy).Color, 1f);
                    gfx.Draw(Matrix4x4.CreateScale(0.06f, 0.06f, 0.06f) * Matrix4x4.CreateTranslation(0, 0.07f, -0.14f) * rot * at, DarkMetal, 1f);
                    if (hit) // it "fired": a little muzzle flicker
                        gfx.Draw(Matrix4x4.CreateScale(0.16f) * Matrix4x4.CreateTranslation(0, 0.1f, -0.2f) * rot * at, new Vector3(1f, 0.85f, 0.4f), 1f, mode: 2);
                    break;
                }
                case ThrowableKind.Insertion:
                {
                    gfx.Draw(Matrix4x4.CreateScale(0.22f, 0.04f, 0.22f) * Matrix4x4.CreateTranslation(0, 0.02f, 0) * at, DarkMetal, 1f);
                    gfx.Draw(Matrix4x4.CreateScale(0.07f, 0.45f, 0.07f) * Matrix4x4.CreateTranslation(0, 0.25f, 0) * at, hit ? Vector3.One : Throwables.Get(ThrowableKind.Insertion).Color * 0.7f, 1f);
                    var pulse = 0.9f + 0.6f * MathF.Sin((float)(now - e.Born) * 6f);
                    gfx.Draw(Matrix4x4.CreateScale(0.1f) * Matrix4x4.CreateTranslation(0, 0.52f, 0) * at, (friendly ? FriendLamp : FoeLamp) * pulse, 1f, mode: 2);
                    break;
                }
                case ThrowableKind.Cover:
                {
                    // Frame: two posts and a foot rail; the energy panel itself is drawn in DrawGlow.
                    var h = TacticalGear.CoverHalf;
                    for (var s = -1; s <= 1; s += 2)
                        gfx.Draw(Matrix4x4.CreateScale(0.08f, h.Y * 2 + 0.06f, 0.12f) * Matrix4x4.CreateTranslation(s * h.X, h.Y + 0.03f, 0) * rot * at, hit ? Vector3.One : Gunmetal, 1f, material: Material.Metal);
                    gfx.Draw(Matrix4x4.CreateScale(h.X * 2, 0.06f, 0.2f) * Matrix4x4.CreateTranslation(0, 0.03f, 0) * rot * at, DarkMetal, 1f, material: Material.Metal);
                    gfx.Draw(Matrix4x4.CreateScale(0.12f, 0.05f, 0.03f) * Matrix4x4.CreateTranslation(0, 0.05f, -0.11f) * rot * at, lamp * 1.4f, 1f, mode: 2);
                    break;
                }
            }
        }
    }

    /// <summary>Blended pass (depth writes off): the cover's energy panel, flash and stun bursts, trophy pops.</summary>
    public static void DrawGlow(Renderer gfx, Game game, double now)
    {
        foreach (var e in game.TacDevices)
        {
            if (e.Kind == ThrowableKind.Cover)
            {
                var h = TacticalGear.CoverHalf;
                var health = Math.Clamp(e.Health / (float)Math.Max(1, e.MaxHealth), 0f, 1f);
                var friendly = IsFriendly(game, e.Owner);
                var c = friendly ? new Vector3(0.35f, 0.7f, 1f) : new Vector3(1f, 0.45f, 0.35f);
                var shimmer = 0.16f + 0.06f * MathF.Sin((float)(now - e.Born) * 5f) + (now - e.LastHit < 0.15 ? 0.25f : 0f);
                var m = Matrix4x4.CreateScale(h.X * 2 - 0.08f, h.Y * 2, 0.04f) * Matrix4x4.CreateTranslation(0, h.Y + 0.03f, 0)
                        * Matrix4x4.CreateRotationY(-e.Yaw) * Matrix4x4.CreateTranslation(e.Pos);
                gfx.Draw(m, c * (0.6f + 0.6f * health), shimmer * (0.5f + 0.5f * health) + 0.08f, mode: 2);
            }
            else if (e.Kind == ThrowableKind.Insertion)
            {
                var pulse = 0.25f + 0.15f * MathF.Sin((float)(now - e.Born) * 6f);
                var c = IsFriendly(game, e.Owner) ? FriendLamp : FoeLamp;
                gfx.Draw(Matrix4x4.CreateScale(0.5f) * Matrix4x4.CreateTranslation(e.Pos + new Vector3(0, 0.52f, 0)), c, pulse, mode: 2);
            }
        }
        foreach (var b in game.TacBursts)
        {
            var age = (float)(now - b.Born);
            switch (b.Kind)
            {
                case ThrowableKind.Flash:
                {
                    // One white pop that only fades (no flicker).
                    var a = Math.Clamp(1f - age / 0.35f, 0f, 1f);
                    gfx.Draw(Matrix4x4.CreateScale(0.6f + age * 9f) * Matrix4x4.CreateTranslation(b.Pos + new Vector3(0, 0.2f, 0)), new Vector3(1f, 1f, 0.95f), a * 0.9f, mode: 2);
                    break;
                }
                case ThrowableKind.Stun:
                {
                    var a = Math.Clamp(1f - age / 0.6f, 0f, 1f);
                    var r = 0.5f + age * 14f;
                    gfx.Draw(Matrix4x4.CreateScale(r, 0.06f, r) * Matrix4x4.CreateTranslation(b.Pos + new Vector3(0, 0.1f, 0)), new Vector3(1f, 0.85f, 0.3f), a * 0.5f, mode: 2);
                    gfx.Draw(Matrix4x4.CreateScale(0.4f + age * 3f) * Matrix4x4.CreateTranslation(b.Pos + new Vector3(0, 0.3f, 0)), new Vector3(1f, 0.9f, 0.5f), a * 0.6f, mode: 2);
                    break;
                }
                default:
                {
                    var a = Math.Clamp(1f - age / 0.3f, 0f, 1f);
                    var c = b.Kind == ThrowableKind.Trophy ? new Vector3(0.5f, 0.95f, 1f) : new Vector3(1f, 0.6f, 0.2f);
                    gfx.Draw(Matrix4x4.CreateScale(0.3f + age * 2.5f) * Matrix4x4.CreateTranslation(b.Pos), c, a * 0.8f, mode: 2);
                    break;
                }
            }
        }
    }
}
