using System.Numerics;

namespace CallOfSlop;

/// <summary>Settings > Video > Name tags: whose names float over their heads.</summary>
public enum NameTagMode : byte
{
    /// <summary>Teammates, plus the enemy under your crosshair (the default).</summary>
    All,

    /// <summary>Teammates only; enemies never get a tag.</summary>
    TeamOnly,

    /// <summary>No name tags at all.</summary>
    Off,
}

/// <summary>
/// The name tag rules as data (CoD style). Distances in metres, times in seconds, sizes in pixels at a UI scale of 1.0
/// (the UI scale follows the window height, so tags are the same size on 1080p, 1440p, 21:9 and 32:9 screens).
/// </summary>
public sealed class NameTagRules
{
    /// <summary>Teammates within this range are tagged whenever they're on screen, through walls too.</summary>
    public float TeamRange = 60f;

    /// <summary>Enemies are tagged only when aimed at, visible and within this range.</summary>
    public float EnemyRange = 40f;

    /// <summary>An enemy's tag stays this long (fading) after your crosshair leaves them.</summary>
    public float LingerSeconds = 0.5f;

    /// <summary>Teammate tags start fading at this fraction of <see cref="TeamRange"/> ...</summary>
    public float FadeStart = 0.5f;

    /// <summary>... down to this opacity at the edge of the range.</summary>
    public float FarAlpha = 0.35f;

    /// <summary>Hitbox padding for "aiming at" (metres), so a tag doesn't flicker at the very edge of a body.</summary>
    public float AimPadding = 0.08f;

    /// <summary>The aimed enemy's line of sight is re-checked at most this often (one ray, cached in between).</summary>
    public float LosRecheckSeconds = 0.1f;

    /// <summary>The tag's bottom sits this far above the top of the head.</summary>
    public float HeadClearance = 0.3f;

    /// <summary>Text height at <see cref="NearDistance"/> or closer.</summary>
    public float FontSize = 15f;

    /// <summary>Tags shrink from this distance ...</summary>
    public float NearDistance = 8f;

    /// <summary>... to this fraction of <see cref="FontSize"/> at <see cref="TeamRange"/>.</summary>
    public float FarScale = 0.7f;

    /// <summary>Never smaller than this (pixels at UI scale 1.0).</summary>
    public float MinFontSize = 10f;

    /// <summary>Tags are this much smaller while aiming through a magnified optic (they stay, so you know who's who).</summary>
    public float ScopeShrink = 0.75f;

    /// <summary>A tag that would overlap a nearer one (even after stacking above it) is drawn at this opacity.</summary>
    public float OverlapAlpha = 0.35f;

    /// <summary>At most this many tags at once (the nearest win).</summary>
    public int MaxTags = 32;

    /// <summary>The colour of enemy tags when there are no teams (free-for-all, Gun Game): CoD red.</summary>
    public Vector3 EnemyColour = new(1f, 0.3f, 0.25f);

    public static readonly NameTagRules Default = new();

    /// <summary>Label for the settings screen.</summary>
    public static string Label(NameTagMode m) => m switch
    {
        NameTagMode.TeamOnly => "Team only",
        NameTagMode.Off => "Off",
        _ => "All",
    };
}

/// <summary>One player the tags consider this frame (filled by <see cref="NameTags.Update(Game, Vector3, Vector3, NameTagMode, double, byte)"/>).</summary>
public struct TagCandidate
{
    public byte Id;

    /// <summary>Their damage box (as drawn this frame).</summary>
    public Box Hitbox;

    /// <summary>On your side (teammate, or everyone in Zombies).</summary>
    public bool Friendly;

    /// <summary>Shown to you through walls by your Recon Pulse or Recon Sweep: aiming at them shows their tag without line of sight.</summary>
    public bool Revealed;

    /// <summary>Always tagged (your killer, while the death cam looks at them).</summary>
    public bool Force;

    /// <summary>Health, 0..1 (the optional teammate health bar).</summary>
    public float Health01;
}

/// <summary>A tag to draw: whose, where (world point its bottom-centre sits on), how far and how opaque.</summary>
public struct NameTag
{
    public byte Id;
    public Vector3 Anchor;
    public float Distance;
    public float Alpha;
    public bool Friendly;
    public float Health01;
}

/// <summary>A tag's screen rectangle in pixels (top-left origin).</summary>
public struct TagRect
{
    public float X0, Y0, X1, Y1;

    public TagRect(float x, float y, float w, float h)
    {
        X0 = x;
        Y0 = y;
        X1 = x + w;
        Y1 = y + h;
    }

    public readonly bool Overlaps(in TagRect o) => X0 < o.X1 && o.X0 < X1 && Y0 < o.Y1 && o.Y0 < Y1;
}

/// <summary>Screen layout math for tags: size by distance, clamping to the screen, de-cluttering. Pure, for the self-test.</summary>
public static class NameTagLayout
{
    /// <summary>Text height in pixels for a tag <paramref name="distance"/> metres away at UI scale <paramref name="uiScale"/>.</summary>
    public static float FontPx(NameTagRules r, float uiScale, float distance, bool scoped)
    {
        var span = MathF.Max(r.TeamRange - r.NearDistance, 1e-3f);
        var k = Math.Clamp((distance - r.NearDistance) / span, 0f, 1f);
        var px = r.FontSize * float.Lerp(1f, r.FarScale, k) * (scoped ? r.ScopeShrink : 1f);
        return MathF.Max(px, r.MinFontSize) * uiScale;
    }

    /// <summary>Moves a <paramref name="w"/> x <paramref name="h"/> box at (x, y) so it lies inside the screen with <paramref name="pad"/> pixels to spare.</summary>
    public static (float X, float Y) Clamp(float x, float y, float w, float h, float screenW, float screenH, float pad)
    {
        x = Math.Clamp(x, pad, MathF.Max(pad, screenW - pad - w));
        y = Math.Clamp(y, pad, MathF.Max(pad, screenH - pad - h));
        return (x, y);
    }

    /// <summary>
    /// De-clutters a tag against the nearer tags already placed: if it overlaps one, it stacks just above it (once);
    /// if it still overlaps, it stays put and the returned opacity factor is <see cref="NameTagRules.OverlapAlpha"/>.
    /// </summary>
    public static float Place(ReadOnlySpan<TagRect> placed, ref TagRect r, NameTagRules rules, float gap)
    {
        var hit = -1;
        for (var i = 0; i < placed.Length && hit < 0; i++)
            if (r.Overlaps(placed[i]))
                hit = i;
        if (hit < 0)
            return 1f;
        var h = r.Y1 - r.Y0;
        var up = r;
        up.Y1 = placed[hit].Y0 - gap;
        up.Y0 = up.Y1 - h;
        if (up.Y0 >= 0)
        {
            var free = true;
            for (var i = 0; i < placed.Length && free; i++)
                if (up.Overlaps(placed[i]))
                    free = false;
            if (free)
            {
                r = up;
                return 1f;
            }
        }
        return rules.OverlapAlpha;
    }
}

/// <summary>
/// Name tags over players' heads (CoD style), decided once per frame before the HUD is drawn:
/// <list type="bullet">
/// <item>Teammates (everyone in Zombies): always, through walls, within <see cref="NameTagRules.TeamRange"/>, in their team colour, fading with distance.</item>
/// <item>Enemies (everyone in free-for-all): only the one under your crosshair, within <see cref="NameTagRules.EnemyRange"/>, and only with a
/// clear line of sight (walls and smoke hide them), lingering <see cref="NameTagRules.LingerSeconds"/> after the crosshair leaves. An enemy your
/// recon reveals through walls can be tagged by aiming at their outline.</item>
/// <item>Never your own tag, never on dead players. While you're dead, teammates stay tagged from the death camera and your killer is tagged.</item>
/// </list>
/// Cost: a loop over the players, one ray-box test each for enemies, and at most one world ray per
/// <see cref="NameTagRules.LosRecheckSeconds"/>. No allocations: fixed arrays, filled in place.
/// </summary>
public sealed class NameTags
{
    public const byte None = byte.MaxValue;
    public const int Capacity = 64;

    public NameTagRules Rules = NameTagRules.Default;

    /// <summary>This frame's tags, nearest first; the first <see cref="Count"/> are valid.</summary>
    public readonly NameTag[] Tags = new NameTag[Capacity];

    public int Count;

    /// <summary>The enemy whose tag is up (aimed now or lingering), or <see cref="None"/>.</summary>
    public byte FocusId { get; private set; } = None;

    /// <summary>Is <see cref="FocusId"/> under the crosshair this frame (not just lingering)?</summary>
    public bool FocusAimed { get; private set; }

    public double LingerUntil { get; private set; }

    /// <summary>World rays cast this frame and in total (for the self-test and the perf log).</summary>
    public int RaysThisFrame;

    public long RaysTotal;

    /// <summary>Free distance along a ray (origin, unit direction, max): the world by default; the self-test swaps in fake walls.</summary>
    public Func<Vector3, Vector3, float, float> Ray = World.RaycastWorld;

    /// <summary>Smoke clouds block enemy tags too (set by <see cref="Update(Game, Vector3, Vector3, NameTagMode, double, byte)"/>).</summary>
    public Game? SmokeSource;

    /// <summary>Smoke test only: these player ids (bit per id below 64) count as teammates, so one free-for-all screenshot shows both kinds.</summary>
    public static ulong SmokeFriends;

    private readonly TagCandidate[] _cands = new TagCandidate[Capacity];
    private byte _losId = None;
    private double _losAt = double.NegativeInfinity;
    private bool _los;

    /// <summary>Forgets the focus and the cached line of sight (new session).</summary>
    public void Reset()
    {
        Count = 0;
        FocusId = None;
        FocusAimed = false;
        _losId = None;
        _losAt = double.NegativeInfinity;
    }

    /// <summary>
    /// Builds this frame's candidates from the match and decides the tags. <paramref name="eye"/> and <paramref name="fwd"/> are the
    /// camera's (the crosshair is the screen centre); <paramref name="killerId"/> is tagged regardless (death cam) or <see cref="None"/>.
    /// </summary>
    public void Update(Game game, Vector3 eye, Vector3 fwd, NameTagMode mode, double now, byte killerId)
    {
        SmokeSource = game;
        if (game.Local is not { } local || mode == NameTagMode.Off)
        {
            Update(ReadOnlySpan<TagCandidate>.Empty, eye, fwd, false, mode, now);
            return;
        }
        var n = 0;
        var range2 = Rules.EnemyRange * Rules.EnemyRange;
        foreach (var p in game.Players.Values)
        {
            if (p.Id == game.LocalId || !p.Alive)
                continue;
            if (n >= _cands.Length)
                break;
            var friendly = !game.AreEnemies(local, p) || (p.Id < 64 && (SmokeFriends & (1UL << p.Id)) != 0);
            var hb = World.Hitbox(p.RenderPosition, p.RenderStance, p.RenderYaw);
            var revealed = !friendly && mode == NameTagMode.All && Vector3.DistanceSquared(eye, hb.Center) <= range2 && game.IsRevealed(p, now);
            _cands[n++] = new TagCandidate
            {
                Id = p.Id, Hitbox = hb, Friendly = friendly, Revealed = revealed, Force = p.Id == killerId,
                Health01 = Math.Clamp(p.Health / (float)World.MaxHealth, 0f, 1f),
            };
        }
        Update(new ReadOnlySpan<TagCandidate>(_cands, 0, n), eye, fwd, local.Alive, mode, now);
    }

    /// <summary>The rules on plain candidates (the self-test drives this directly). <paramref name="canAim"/> is false while you're dead.</summary>
    public void Update(ReadOnlySpan<TagCandidate> cands, Vector3 eye, Vector3 fwd, bool canAim, NameTagMode mode, double now)
    {
        Count = 0;
        RaysThisFrame = 0;
        FocusAimed = false;
        var r = Rules;
        var enemies = mode == NameTagMode.All;
        if (!enemies)
            FocusId = None;
        if (mode == NameTagMode.Off)
            return;

        // The enemy under the crosshair: the nearest padded hitbox the centre ray meets, then one (cached) line-of-sight ray.
        if (enemies && canAim)
        {
            var best = -1;
            var bestT = r.EnemyRange;
            var pad = new Vector3(r.AimPadding);
            for (var i = 0; i < cands.Length; i++)
            {
                if (cands[i].Friendly)
                    continue;
                var box = new Box(cands[i].Hitbox.Center, cands[i].Hitbox.Half + pad);
                if (box.Raycast(eye, fwd, bestT) is float t && t < bestT)
                {
                    best = i;
                    bestT = t;
                }
            }
            if (best >= 0 && (cands[best].Revealed || Clear(cands[best].Id, eye, eye + fwd * bestT, now)))
            {
                FocusId = cands[best].Id;
                FocusAimed = true;
                LingerUntil = now + r.LingerSeconds;
            }
        }

        // Lingering: drops when the time is up, they die or leave, or walk behind a wall (still at most one cached ray).
        if (FocusId != None && !FocusAimed)
        {
            var idx = -1;
            for (var i = 0; i < cands.Length && idx < 0; i++)
                if (cands[i].Id == FocusId && !cands[i].Friendly)
                    idx = i;
            if (idx < 0 || now > LingerUntil || (!cands[idx].Revealed && !Clear(FocusId, eye, cands[idx].Hitbox.Center, now)))
                FocusId = None;
        }

        for (var i = 0; i < cands.Length; i++)
        {
            ref readonly var c = ref cands[i];
            var head = new Vector3(c.Hitbox.Center.X, c.Hitbox.Center.Y + c.Hitbox.Half.Y, c.Hitbox.Center.Z);
            var dist = Vector3.Distance(eye, head);
            float alpha;
            if (c.Friendly)
            {
                if (!(dist <= r.TeamRange))
                    continue;
                alpha = DistanceFade(r, dist);
            }
            else if (enemies && c.Force)
                alpha = 1f;
            else if (enemies && c.Id == FocusId && dist <= r.EnemyRange)
                alpha = FocusAimed ? 1f : Math.Clamp((float)(LingerUntil - now) / MathF.Max(r.LingerSeconds, 1e-3f), 0f, 1f);
            else
                continue;
            if (Count >= Tags.Length || Count >= r.MaxTags)
                break;
            Tags[Count++] = new NameTag
            {
                Id = c.Id, Anchor = head + new Vector3(0, r.HeadClearance, 0), Distance = dist, Alpha = alpha, Friendly = c.Friendly, Health01 = c.Health01,
            };
        }

        // Nearest first (insertion sort: a handful of tags, no allocation).
        for (var i = 1; i < Count; i++)
        {
            var t = Tags[i];
            var j = i - 1;
            while (j >= 0 && Tags[j].Distance > t.Distance)
            {
                Tags[j + 1] = Tags[j];
                j--;
            }
            Tags[j + 1] = t;
        }
    }

    /// <summary>Teammate tag opacity at <paramref name="dist"/> metres: full up to <see cref="NameTagRules.FadeStart"/> of the range, then down to <see cref="NameTagRules.FarAlpha"/>.</summary>
    public static float DistanceFade(NameTagRules r, float dist)
    {
        var start = r.TeamRange * r.FadeStart;
        var k = Math.Clamp((dist - start) / MathF.Max(r.TeamRange - start, 1e-3f), 0f, 1f);
        return float.Lerp(1f, r.FarAlpha, k);
    }

    /// <summary>Is the line from <paramref name="from"/> to <paramref name="to"/> free of walls and smoke? One ray, cached per target for <see cref="NameTagRules.LosRecheckSeconds"/>.</summary>
    private bool Clear(byte id, Vector3 from, Vector3 to, double now)
    {
        if (id == _losId && now >= _losAt && now - _losAt < Rules.LosRecheckSeconds)
            return _los;
        var d = to - from;
        var len = d.Length();
        var clear = true;
        if (len > 1e-3f && float.IsFinite(len))
        {
            RaysThisFrame++;
            RaysTotal++;
            clear = Ray(from, d / len, len) >= len - 0.05f;
        }
        if (clear && SmokeSource != null && SmokeSource.SmokeBlocks(from, to, now))
            clear = false;
        _losId = id;
        _losAt = now;
        _los = clear;
        return clear;
    }

    /// <summary>The colour of a tag: team colour in team modes (and for teammates anywhere), <see cref="NameTagRules.EnemyColour"/> for enemies without teams.</summary>
    public Vector3 ColourOf(Game game, Player p, bool friendly) => game.IsTeamMode || friendly ? p.Color : Rules.EnemyColour;
}
