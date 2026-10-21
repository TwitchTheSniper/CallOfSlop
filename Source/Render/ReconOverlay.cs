using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The see-through-walls overlay on players revealed by a Recon Pulse or a Recon Sweep (<see cref="Game.IsRevealed"/>).
/// Instead of one solid box bigger than the player, it is a light, pulsing, translucent silhouette of four boxes (torso,
/// head, two legs) laid out like the drawn body for the player's stance, each shrunk to <see cref="Inset"/> of its body
/// part, so it sits inside the character and the model stays visible through and around it. It is drawn in the revealed
/// player's own team colour (Blue / Red), or <see cref="FfaColor"/> in free-for-all modes. 4 draws per revealed player.
/// </summary>
public static class ReconOverlay
{
    /// <summary>Each overlay box is this fraction of its body part's size (about its own centre).</summary>
    public const float Inset = 0.88f;

    /// <summary>The pulsing fill alpha stays between these (translucent, never a solid box).</summary>
    public const float MinAlpha = 0.18f, MaxAlpha = 0.4f;

    /// <summary>Revealed players in modes without teams (free-for-all, Gun Game): a neutral orange.</summary>
    public static readonly Vector3 FfaColor = new(1f, 0.6f, 0.1f);

    /// <summary>The overlay colour for <paramref name="p"/>: their own team colour in team modes, else <see cref="FfaColor"/>.</summary>
    public static Vector3 ColorFor(Game game, Player p) => game.IsTeamMode && p.Team < 2 ? GameModes.TeamColors[p.Team] : FfaColor;

    /// <summary>Fill alpha at time <paramref name="now"/> (within <see cref="MinAlpha"/>..<see cref="MaxAlpha"/>).</summary>
    public static float Alpha(double now) => MinAlpha + (MaxAlpha - MinAlpha) * (0.5f + 0.5f * MathF.Sin((float)now * 8f));

    /// <summary>
    /// The silhouette boxes in the body's local frame (feet at the origin, -Z forward, as <see cref="PlayerRender"/> draws),
    /// already inset. Writes up to 4 boxes and returns how many.
    /// </summary>
    public static int LocalParts(Stance stance, Span<Box> parts)
    {
        var n = 0;
        void Add(Span<Box> into, Vector3 center, Vector3 size) => into[n++] = new Box(center, size * (0.5f * Inset));
        switch (stance)
        {
            case Stance.Prone:
                Add(parts, new Vector3(0, 0.2f, -0.27f), new Vector3(0.6f, 0.4f, 0.6f)); // body
                Add(parts, new Vector3(0, 0.3f, -0.75f), new Vector3(0.4f, 0.38f, 0.4f)); // head, out front
                Add(parts, new Vector3(-0.15f, 0.13f, 0.4f), new Vector3(0.25f, 0.25f, 0.75f)); // legs, behind
                Add(parts, new Vector3(0.15f, 0.13f, 0.4f), new Vector3(0.25f, 0.25f, 0.75f));
                break;
            case Stance.Crouch:
            {
                const float chest = 0.85f, top = 1.2f, hip = LegAnimator.CrouchPelvis - 0.04f;
                Add(parts, new Vector3(0, (chest + hip) / 2, 0), new Vector3(0.8f, chest - hip, 0.6f));
                Add(parts, new Vector3(0, (chest + top) / 2, -0.05f), new Vector3(0.45f, top - chest, 0.45f));
                Add(parts, new Vector3(-0.2f, hip / 2, 0), new Vector3(0.3f, hip, 0.5f));
                Add(parts, new Vector3(0.2f, hip / 2, 0), new Vector3(0.3f, hip, 0.5f));
                break;
            }
            default:
            {
                const float hip = LegAnimator.StandPelvis;
                var headH = World.PlayerHeight - World.HeadStart;
                Add(parts, new Vector3(0, (World.HeadStart + hip) / 2, 0), new Vector3(0.8f, World.HeadStart - hip, 0.5f));
                Add(parts, new Vector3(0, World.HeadStart + headH / 2, 0), new Vector3(0.45f, headH, 0.45f));
                Add(parts, new Vector3(-0.2f, hip / 2, 0), new Vector3(0.3f, hip, 0.3f));
                Add(parts, new Vector3(0.2f, hip / 2, 0), new Vector3(0.3f, hip, 0.3f));
                break;
            }
        }
        return n;
    }

    /// <summary>Draws the overlay on every player revealed to the local player (depth test off: through walls).</summary>
    public static void Draw(Renderer g, Game game, double now)
    {
        Span<Box> parts = stackalloc Box[4];
        var alpha = Alpha(now);
        foreach (var p in game.Players.Values)
        {
            if (!game.IsRevealed(p, now))
                continue;
            var color = ColorFor(game, p);
            var body = Matrix4x4.CreateRotationY(-p.RenderYaw) * Matrix4x4.CreateTranslation(p.RenderPosition);
            var n = LocalParts(p.RenderStance, parts);
            for (var i = 0; i < n; i++)
                g.Draw(Matrix4x4.CreateScale(parts[i].Half * 2) * Matrix4x4.CreateTranslation(parts[i].Center) * body, color, alpha, mode: 2);
        }
    }
}
