using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// A craft's radar on the HUD (ImGui background pass; ConstructRadar.cs): seated on a craft with a working radar, a
/// round scope on the right, heading up (the way you look, flattened), with every contact as a blip (a triangle up or down
/// for one 50 m or more above or below), the locked one red and ringed, and the one the radar key would take next yellow.
/// In the view each contact gets a faint diamond and the locked one red brackets and its range. Text is made again only
/// when a number in it changes.
/// </summary>
public static class RadarHud
{
    private static readonly Vector3 Scope = new(0.35f, 0.95f, 0.55f);
    private static readonly Vector3 Next = new(1f, 0.85f, 0.2f);
    private static readonly Vector3 Locked = new(1f, 0.2f, 0.15f);
    private const float Climb = 50f; // m above or below: a blip shows which way

    private static Vector3 _heading = Vector3.UnitZ;
    private static int _headKey = int.MinValue, _lockKey = int.MinValue;
    private static string _headLine = "", _keyLine = "", _lockLine = "", _lockTag = "";

    /// <summary>How far above the screen's bottom the scope must stay (pixels): the seat controls panel's top, set by it each frame (UiSeatPanel.cs).</summary>
    public static float ClearBelow;

    public static void Draw(Game game, float scale, int bind)
    {
        game.RadarUpdate(game.Local is { Alive: true } me ? me : null);
        var cam = Hud.Camera;
        if (game.RadarCraft is not { } c || !cam.Valid || !Hud.CanDraw())
            return;
        var vp = ImGui.GetMainViewport();
        float2 org = vp.WorkPos, area = vp.WorkSize;
        var dl = ImGui.GetBackgroundDrawList();
        var range = ScopeRange(game, c);

        // The flat frame: up at the craft, and the look along the ground (kept from the last frame when looking straight up or down).
        var up = GravityFrames.UpAt(c.WorldCom);
        var flat = cam.Forward - up * Vector3.Dot(cam.Forward, up);
        if (flat.LengthSquared() > 1e-4f)
            _heading = Vector3.Normalize(flat);
        var fwd = _heading;
        var right = Vector3.Cross(fwd, up);

        // What the radar key would take next: as Game.RadarPick does, without sending.
        var next = game.RadarCanLock ? game.RadarNext(cam.Eye, cam.Forward) : (ushort)0;

        // ---- The scope ----
        var r = 92f * scale;
        var centre = new float2(org.X + area.X - 24f * scale - r, org.Y + area.Y - MathF.Max(180f * scale, ClearBelow) - r);
        ImDrawListExtensions.AddCircleFilled(dl, in centre, r, new ImColor8(8, 20, 14, 170), 48);
        ImDrawListExtensions.AddCircle(dl, in centre, r, Hud.Colour(Scope, 0.8f), 48, 1.5f * scale);
        ImDrawListExtensions.AddCircle(dl, in centre, r * 2f / 3f, Hud.Colour(Scope, 0.25f), 40, 1f * scale);
        ImDrawListExtensions.AddCircle(dl, in centre, r / 3f, Hud.Colour(Scope, 0.25f), 32, 1f * scale);
        dl.AddLine(new float2(centre.X, centre.Y - r), new float2(centre.X, centre.Y - r + 8 * scale), Hud.Colour(Scope, 0.8f), 1.5f * scale); // (ahead)
        // The craft itself, a small arrow in the middle.
        ImDrawListExtensions.AddTriangleFilled(dl, new float2(centre.X, centre.Y - 5 * scale), new float2(centre.X - 4 * scale, centre.Y + 4 * scale), new float2(centre.X + 4 * scale, centre.Y + 4 * scale), Hud.Colour(Scope, 0.9f));

        Construct? held = null;
        for (var i = 0; i < game.RadarContactCount; i++)
        {
            if (game.Constructs.Get(game.RadarContacts[i]) is not { } b)
                continue;
            var rel = b.WorldCom - c.WorldCom;
            var x = Vector3.Dot(rel, right) / range;
            var y = Vector3.Dot(rel, fwd) / range;
            var len = MathF.Sqrt(x * x + y * y);
            if (len > 1f)
                (x, y) = (x / len, y / len); // (on the rim: inside the hold, past the range)
            var at = new float2(centre.X + x * r, centre.Y - y * r);
            var h = Vector3.Dot(rel, up);
            var isLock = b.Id == game.RadarLock;
            if (isLock)
                held = b;
            var col = Hud.Colour(isLock ? Locked : b.Id == next ? Next : Scope, 0.95f);
            var s = 3.5f * scale;
            if (h > Climb)
                ImDrawListExtensions.AddTriangleFilled(dl, new float2(at.X, at.Y - s * 1.3f), new float2(at.X - s, at.Y + s), new float2(at.X + s, at.Y + s), col);
            else if (h < -Climb)
                ImDrawListExtensions.AddTriangleFilled(dl, new float2(at.X - s, at.Y - s), new float2(at.X + s, at.Y - s), new float2(at.X, at.Y + s * 1.3f), col);
            else
                dl.AddRectFilled(new float2(at.X - s, at.Y - s), new float2(at.X + s, at.Y + s), col);
            if (isLock)
                ImDrawListExtensions.AddCircle(dl, in at, 8f * scale, col, 16, 1.5f * scale);

            // In the view.
            if (!Hud.Project(cam.ViewProj, b.WorldCom, area.X, area.Y, out var sp, out _, 40f))
                continue;
            var p = new float2(org.X + sp.X, org.Y + sp.Y);
            if (isLock)
            {
                var half = 20f * scale;
                var k = half * 0.35f;
                for (var sx = -1; sx <= 1; sx += 2)
                    for (var sy = -1; sy <= 1; sy += 2)
                    {
                        var corner = new float2(p.X + sx * half, p.Y + sy * half);
                        dl.AddLine(corner, new float2(corner.X - sx * k, corner.Y), col, 2.5f * scale);
                        dl.AddLine(corner, new float2(corner.X, corner.Y - sy * k), col, 2.5f * scale);
                    }
                LockText(c, b);
                Hud.ShadowText(dl, new float2(p.X - Hud.TextWidth(_lockTag, 14f * scale) / 2, p.Y + half + 4 * scale), 14f * scale, _lockTag, Locked, 1f);
            }
            else
            {
                var d = 7f * scale;
                var dc = Hud.Colour(b.Id == next ? Next : Scope, b.Id == next ? 0.8f : 0.45f);
                dl.AddLine(new float2(p.X, p.Y - d), new float2(p.X + d, p.Y), dc, 1.5f * scale);
                dl.AddLine(new float2(p.X + d, p.Y), new float2(p.X, p.Y + d), dc, 1.5f * scale);
                dl.AddLine(new float2(p.X, p.Y + d), new float2(p.X - d, p.Y), dc, 1.5f * scale);
                dl.AddLine(new float2(p.X - d, p.Y), new float2(p.X, p.Y - d), dc, 1.5f * scale);
            }
        }

        // ---- The lines under it (right-aligned with the scope) ----
        var key = (int)range * 64 + game.RadarContactCount + (bind & 0xffff) * 100003 + (game.RadarCanLock ? 0 : 1 << 30);
        if (key != _headKey)
        {
            _headKey = key;
            var n = game.RadarContactCount;
            var reach = game.RadarRange;
            _headLine = $"RADAR {(reach >= 1000 ? $"{reach / 1000f:0.#} km" : $"{reach:0} m")}, {n} contact{(n == 1 ? "" : "s")}, ring {(range >= 1000 ? $"{range / 1000f:0.#} km" : $"{range:0} m")}";
            _keyLine = game.RadarCanLock ? $"{Keybinds.Name(bind)}: lock the nearest to your aim" : "Scope only: wire it to a Fire Director to lock";
        }
        var edge = centre.X + r;
        var ty = centre.Y + r + 6 * scale;
        Hud.ShadowText(dl, new float2(edge - Hud.TextWidth(_headLine, 13f * scale), ty), 13f * scale, _headLine, Scope, 0.95f);
        ty += 16 * scale;
        if (held != null)
        {
            LockText(c, held);
            Hud.ShadowText(dl, new float2(edge - Hud.TextWidth(_lockLine, 13f * scale), ty), 13f * scale, _lockLine, Locked, 1f);
            ty += 16 * scale;
        }
        Hud.ShadowText(dl, new float2(edge - Hud.TextWidth(_keyLine, 12f * scale), ty), 12f * scale, _keyLine, Scope, 0.6f);
    }

    private static readonly float[] Scales = [250f, 500f, 1000f, 2500f, 5000f, 10000f, 25000f, 50000f, 100000f, 200000f];

    /// <summary>The scope's rim (m): the least of <see cref="Scales"/> that holds every contact, at most the radar's range.</summary>
    private static float ScopeRange(Game game, Construct c)
    {
        var far = 0f;
        for (var i = 0; i < game.RadarContactCount; i++)
            if (game.Constructs.Get(game.RadarContacts[i]) is { } b)
                far = MathF.Max(far, Vector3.Distance(b.WorldCom, c.WorldCom));
        foreach (var s in Scales)
            if (s >= far || s >= game.RadarRange)
                return MathF.Min(s, game.RadarRange);
        return game.RadarRange;
    }

    /// <summary>The locked contact's range and closing speed, for the scope's line and its tag in the view.</summary>
    private static void LockText(Construct c, Construct b)
    {
        var rel = b.WorldCom - c.WorldCom;
        var dist = rel.Length();
        var closing = dist > 1f ? -Vector3.Dot(b.Vel - c.Vel, rel / dist) : 0f;
        var m = (int)(dist / 10f) * 10;
        var cl = (int)MathF.Round(closing);
        var key = m * 4001 + cl;
        if (key == _lockKey)
            return;
        _lockKey = key;
        var tonnes = b.Mass / 1000f;
        _lockTag = $"LOCK {m} m";
        _lockLine = cl >= 0 ? $"LOCKED {m} m, {(tonnes >= 10 ? $"{tonnes:0}" : $"{tonnes:0.0}")} t, closing {cl} m/s" : $"LOCKED {m} m, {(tonnes >= 10 ? $"{tonnes:0}" : $"{tonnes:0.0}")} t, opening {-cl} m/s";
    }
}
