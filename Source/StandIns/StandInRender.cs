using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// STAND-IN (placeholder, Source/StandIns/README.md): draws the stand-in craft at this frame's server tick, a box at a
/// time (a few craft of a dozen boxes each), in the opaque pass beside the map's movers.
/// </summary>
public static class StandInRender
{
    public const float CraftFar = 3200f, PadFar = 1200f; // m from the camera past which they aren't drawn

    public const float WarpStretch = 0.45f, WarpRing = 3.2f; // s: the streak out of the warp, and how long the shock ring spreads
    public const float SoundSpeed = 343f, BoomHeard = 2500f; // m/s, and m past which a boom isn't heard
    private const int RingBoxes = 20;

    /// <summary>
    /// A ship from another world breaking into the air: a white flash where it comes in, two shock rings spreading across its
    /// path (a wide thin one and a slower one inside it), and the boom, heard as late as it is far (like thunder). The
    /// same boom, softer, when it jumps away.
    /// </summary>
    private static void Warp(Renderer gfx, Game? game, ref StandInCraft c, double tick)
    {
        var age = (float)((tick - c.Start) / World.TickRate);
        if (age >= 0 && age < WarpRing + 8f && (age < WarpRing || (c.Boomed & 1) == 0))
        {
            var at = c.Start;
            if (StandInPath.Pose(ref c, at, out var entry, out _, out _))
            {
                Boom(gfx, game, ref c, 1, entry.Translation, age, 1f);
                if (age < WarpRing)
                {
                    if (age < 0.4f)
                    {
                        var size = 4f + 90f * age * (1f - age / 0.4f) * 2f;
                        gfx.Draw(Matrix4x4.CreateScale(size, size, size * 2.5f) * entry, new Vector3(1f, 0.97f, 0.9f), 1f, mode: 2);
                    }
                    var wide = 10f + 170f * (1f - MathF.Exp(-age * 1.5f));
                    Ring(gfx, entry, wide, (1.5f + wide * 0.035f) * (1f - age / WarpRing), new Vector3(0.85f, 0.93f, 1f));
                    var inner = 5f + 70f * (1f - MathF.Exp(-age * 1.1f));
                    if (age < WarpRing * 0.7f)
                        Ring(gfx, entry, inner, (2f + inner * 0.06f) * (1f - age / (WarpRing * 0.7f)), new Vector3(1f, 0.9f, 0.75f));
                }
            }
        }
        // The jump away: the flash is the path's own (Pose); its boom is heard here.
        var jumpAt = c.Over ? c.LandsAt : c.LeavesAt + 0.8 * c.Out;
        var since = (float)((tick - jumpAt) / World.TickRate);
        if (since >= 0 && (c.Boomed & 2) == 0)
        {
            var where = c.Over ? c.Land : c.Land + StandInPath.Up(c.Land) * 450f;
            Boom(gfx, game, ref c, 2, where, since, 0.6f);
            if (c.Over && since < 0.3f)
            {
                var size = 6f + 120f * since;
                gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(where), new Vector3(1f, 0.95f, 0.85f), 1f, mode: 2);
            }
        }
    }

    /// <summary>The boom, once the sound has had time to come from <paramref name="where"/>: a low thump and a longer, lower roll under it.</summary>
    private static void Boom(Renderer gfx, Game? game, ref StandInCraft c, byte bit, Vector3 where, float age, float loud)
    {
        if ((c.Boomed & bit) != 0)
            return;
        var to = where - gfx.CameraPosition;
        var dist = to.Length();
        if (age < dist / SoundSpeed)
            return;
        c.Boomed |= bit;
        if (dist > BoomHeard || age > dist / SoundSpeed + 1.5f)
            return; // (too far, or this PC came in after it)
        // Played near the ear, from its side, as loud as its distance leaves it (the mixer's own falloff is for gunfights).
        var near = gfx.CameraPosition + (dist > 30f ? to * (30f / dist) : to);
        var gain = loud * Math.Clamp(260f / (dist + 160f), 0.12f, 1.3f);
        game?.Audio.Effect3DPitched(Sfx.Explosion, near, gain, 0.42f);
        game?.Audio.Effect3DPitched(Sfx.Explosion, near, gain * 0.6f, 0.25f);
    }

    /// <summary>A ring of glowing boxes across the craft's way, <paramref name="thick"/> m thick (it thins as it spreads, and is gone).</summary>
    private static void Ring(Renderer gfx, in Matrix4x4 frame, float radius, float thick, Vector3 colour)
    {
        if (thick < 0.05f)
            return;
        var len = MathF.Tau * radius / RingBoxes * 0.97f;
        for (var i = 0; i < RingBoxes; i++)
        {
            var a = i * MathF.Tau / RingBoxes;
            var model = Matrix4x4.CreateScale(len, thick, thick) * Matrix4x4.CreateRotationZ(a + MathF.PI / 2)
                * Matrix4x4.CreateTranslation(MathF.Cos(a) * radius, MathF.Sin(a) * radius, 0) * frame;
            gfx.Draw(model, colour, 1f, mode: 2);
        }
    }

    public static void Draw(Renderer gfx, Game? game)
    {
        if (game == null || game.StandInCount == 0 || !game.StandInsOf(World.Map))
            return;
        var tick = game.MoverRenderTick;
        var seconds = (float)(tick / World.TickRate);
        for (var i = 0; i < game.StandInCount; i++)
            DrawOne(gfx, game, ref game.StandIns[i], tick, seconds);
    }

    /// <summary>One craft at a tick (<paramref name="game"/> null: no sound).</summary>
    public static void DrawOne(Renderer gfx, Game? game, ref StandInCraft c, double tick, float seconds)
    {
        var far = c.Kind == StandInKind.Pad ? PadFar : CraftFar;
        if (Vector3.DistanceSquared(c.Land, gfx.CameraPosition) > far * far && (!c.Route || Vector3.DistanceSquared(c.From, gfx.CameraPosition) > far * far))
            return; // (too far to make out: a station's marker sooner than a ship in the sky)
        var tinted = c.Tint != Vector3.Zero;
        if (c.Jumps)
            Warp(gfx, game, ref c, tick);
        if (!StandInPath.Pose(ref c, tick, out var frame, out var thrust, out var flash))
            return;
        if (c.Jumps && tick - c.Start < WarpStretch * World.TickRate)
        {
            // Out of the warp: a long streak that snaps back into a ship.
            var k = 1f - (float)((tick - c.Start) / (WarpStretch * World.TickRate));
            frame = Matrix4x4.CreateScale(1f - 0.5f * k, 1f - 0.5f * k, 1f + 9f * k * k) * frame;
        }
        if (flash > 0)
        {
            // The jump: the craft shrinks into a white flash that swells and is gone.
            var size = 2f + 30f * flash;
            gfx.Draw(Matrix4x4.CreateScale(size, size, size * 3f) * frame, new Vector3(1f, 0.95f, 0.85f), 1f, mode: 2);
            if (flash > 0.4f)
                return;
            frame = Matrix4x4.CreateScale(1f - flash * 2f) * frame;
        }
        foreach (var box in StandInShapes.Of(c.Kind))
        {
            var model = Matrix4x4.CreateScale(box.Half * 2);
            if (box.Spin)
                model *= Matrix4x4.CreateRotationY(seconds * 25f * (0.3f + thrust));
            model *= Matrix4x4.CreateTranslation(box.Centre) * frame;
            if (box.Glow)
                gfx.Draw(model, box.Colour * (0.35f + 0.65f * thrust), 1f, mode: 2);
            else
                gfx.Draw(model, box.Livery && tinted ? c.Tint : box.Colour, 1f, mode: 0);
        }
    }
}
