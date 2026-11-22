using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The gunsight readouts at a turret's station (ImGui background pass; Game/GameTurretView.cs, docs/sandbox/constructs/turret-camera.md). Built
/// round what a gunner needs and nothing else, as a fire-control sight is: in first person the reticle and its zoom; in both views the bore
/// marker (where the gun points now: green once it is laid on the aim, amber while the turret slews), the range to what the reticle is on and
/// whether the gun reaches it, the target's closing speed, the turret's bearing and elevation off the station's bow with a small top-down dial,
/// and the gun's rounds, reload and why it won't fire. Text is made again only when a number in it changes.
/// </summary>
public static class GunnerHud
{
    private static readonly Vector3 Sight = new(0.55f, 1f, 0.7f);
    private static readonly Vector3 Slew = new(1f, 0.72f, 0.25f);
    private static readonly Vector3 Fault = new(1f, 0.3f, 0.25f);
    private static readonly Vector3 Text = new(0.85f, 0.95f, 0.88f);
    private const float LaidDeg = 0.6f; // (degrees off the laid way that still count as on)

    private struct Line
    {
        public long Key;
        public string Text;

        public bool Stale(long key)
        {
            if (Text != null && key == Key)
                return false;
            Key = key;
            return true;
        }
    }

    private static Line _range, _bearing, _state, _gun, _ammo, _top, _closing;

    /// <summary>Draws the readouts when the local player is at a turret's station. <paramref name="firstPerson"/>: looking out of its camera (the reticle and the zoom).</summary>
    public static void Draw(Game game, float scale, bool firstPerson, float zoom)
    {
        var cam = Hud.Camera;
        if (!cam.Valid || !Hud.CanDraw() || !game.LocalTurret(out var c, out var seat, out var k) || !game.LocalTurretEye(out var eye))
            return;
        var vp = ImGui.GetMainViewport();
        float2 org = vp.WorkPos, area = vp.WorkSize;
        var dl = ImGui.GetBackgroundDrawList();
        float cx = org.X + area.X * 0.5f, cy = org.Y + area.Y * 0.5f;
        ref var g = ref c.Guns[k];
        ref var gp = ref c.Parts[g.Part];

        // ---- What the reticle is on: along the look from the turret's camera, a little past the gun's reach ----
        var look = game.LookDir(game.LookYaw, game.LookPitch);
        var reach = g.MaxRange > 0 ? g.MaxRange : World.Range;
        var far = reach * 1.25f;
        var dist = World.Query.Raycast(eye, look, far, QueryFilter.Shots);
        Construct? target = null;
        if (game.Constructs.Raycast(eye, look, dist, out var hit, c, ignoreCargo: true) && hit.Distance < dist)
        {
            dist = hit.Distance;
            target = hit.Body;
        }
        var found = dist < far - 0.5f;

        // ---- The bore: where the gun points now, against the way it is being laid ----
        ConstructWeapons.MuzzleWorld(c, k, out var muzzle, out var bore);
        var laidWay = ConstructWeapons.LaidWay(game.Constructs, c, k, World.Query);
        var off = laidWay.LengthSquared() > 1e-8f && bore.LengthSquared() > 1e-8f
            ? MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(bore), Vector3.Normalize(laidWay)), -1f, 1f)) * 180f / MathF.PI : 0f;
        var laid = off <= LaidDeg;
        var boreAt = muzzle + Vector3.Normalize(bore) * (found ? dist : reach);
        if (Hud.Project(cam.ViewProj, boreAt, area.X, area.Y, out var bs, out _, 0f))
        {
            var bp = new float2(org.X + bs.X, org.Y + bs.Y);
            var col = Hud.Colour(laid ? Sight : Slew, 0.95f);
            ImDrawListExtensions.AddCircle(dl, in bp, 7f * scale, col, 20, 2f * scale);
            dl.AddLine(new float2(bp.X - 11f * scale, bp.Y), new float2(bp.X - 7f * scale, bp.Y), col, 2f * scale);
            dl.AddLine(new float2(bp.X + 7f * scale, bp.Y), new float2(bp.X + 11f * scale, bp.Y), col, 2f * scale);
        }

        // ---- The reticle (first person: the camera's own) ----
        if (firstPerson)
        {
            var rc = Hud.Colour(Sight, 0.85f);
            var th = 1.5f * scale;
            float gap = 14f * scale, arm = 70f * scale;
            dl.AddLine(new float2(cx - arm, cy), new float2(cx - gap, cy), rc, th);
            dl.AddLine(new float2(cx + gap, cy), new float2(cx + arm, cy), rc, th);
            dl.AddLine(new float2(cx, cy + gap), new float2(cx, cy + arm), rc, th);
            dl.AddLine(new float2(cx, cy - gap), new float2(cx, cy - arm * 0.45f), rc, th);
            for (var i = 1; i <= 4; i++)
            {
                // Stadia ticks down the lower arm, and along the arms.
                var y = cy + gap + i * 12f * scale;
                var half = (i % 2 == 0 ? 7f : 4f) * scale;
                dl.AddLine(new float2(cx - half, y), new float2(cx + half, y), rc, th);
                var x = gap + i * 12f * scale;
                dl.AddLine(new float2(cx - x, cy - 3f * scale), new float2(cx - x, cy + 3f * scale), rc, th);
                dl.AddLine(new float2(cx + x, cy - 3f * scale), new float2(cx + x, cy + 3f * scale), rc, th);
            }
            dl.AddRectFilled(new float2(cx - 1.5f * scale, cy - 1.5f * scale), new float2(cx + 1.5f * scale, cy + 1.5f * scale), rc);
        }

        // ---- Left of the sight: range, closing speed, bearing and the turret dial ----
        var size = 15f * scale;
        var lx = cx - 110f * scale;
        var ly = cy - 46f * scale;
        var rangeM = found ? (int)MathF.Round(dist) : -1;
        var beyond = found && dist > reach;
        if (_range.Stale(rangeM * 4L + (beyond ? 1 : 0)))
            _range.Text = !found ? "RNG ----" : (dist >= 10000 ? $"RNG {dist / 1000f:0.0} km" : $"RNG {rangeM} m") + (beyond ? "  OUT OF REACH" : "");
        RightText(dl, lx, ly, 19f * scale, _range.Text, beyond ? Slew : Sight);
        ly += 24f * scale;
        if (target != null)
        {
            var rel = Vector3.Dot(c.Vel - target.Vel, look); // (+: closing)
            var ms = (int)MathF.Round(rel);
            if (_closing.Stale(ms))
                _closing.Text = ms == 0 ? "TGT  STEADY" : ms > 0 ? $"TGT  CLOSING {ms} m/s" : $"TGT  OPENING {-ms} m/s";
            RightText(dl, lx, ly, size, _closing.Text, Text);
            ly += 19f * scale;
        }

        // The bore in the station's frame: the hull's turn and the seat's facing (its bow).
        ref var sp = ref c.Parts[seat];
        c.WorldFrame(sp.Seg, out var sq, out _);
        var station = Quaternion.Normalize(sq * Rot24.Quat(sp.Rot));
        var lb = Vector3.Transform(Vector3.Normalize(bore), Quaternion.Conjugate(station));
        var brg = MathF.Atan2(lb.X, -lb.Z) * 180f / MathF.PI;
        if (brg < 0)
            brg += 360f;
        var elv = MathF.Asin(Math.Clamp(lb.Y, -1f, 1f)) * 180f / MathF.PI;
        var brgI = (int)MathF.Round(brg) % 360;
        var elvI = (int)MathF.Round(elv);
        if (_bearing.Stale(brgI * 1000L + elvI + 500))
            _bearing.Text = $"BRG {brgI:000}  ELV {(elvI >= 0 ? "+" : "-")}{Math.Abs(elvI):00}";
        RightText(dl, lx, ly, size, _bearing.Text, Text);
        ly += 22f * scale;

        // The dial: the station's bow up, the turret's line round it.
        var r = 30f * scale;
        var dc = new float2(lx - r, ly + r);
        ImDrawListExtensions.AddCircleFilled(dl, in dc, r, new ImColor8(8, 20, 14, 150), 36);
        ImDrawListExtensions.AddCircle(dl, in dc, r, Hud.Colour(Sight, 0.6f), 36, 1.2f * scale);
        dl.AddLine(new float2(dc.X, dc.Y - r), new float2(dc.X, dc.Y - r + 6f * scale), Hud.Colour(Sight, 0.8f), 1.5f * scale); // (the bow)
        dl.AddRectFilled(new float2(dc.X - 4f * scale, dc.Y - 12f * scale), new float2(dc.X + 4f * scale, dc.Y + 12f * scale), Hud.Colour(Sight, 0.35f)); // (the hull)
        var a = brg * MathF.PI / 180f;
        var tip = new float2(dc.X + MathF.Sin(a) * r * 0.92f, dc.Y - MathF.Cos(a) * r * 0.92f);
        dl.AddLine(dc, tip, Hud.Colour(laid ? Sight : Slew, 0.95f), 2.5f * scale);
        ImDrawListExtensions.AddCircleFilled(dl, in dc, 3f * scale, Hud.Colour(Sight, 0.95f), 12);

        // ---- Right of the sight: the gun, its rounds and reload, and whether it is laid ----
        var rx = cx + 110f * scale;
        var ry = cy - 46f * scale;
        var mod = -1;
        for (var m = 0; m < c.TurretModCount; m++)
            if (c.TurretMods[m].Part == g.Turret)
                mod = m;
        var stab = ConstructModules.Stabilised(c, seat);
        var of = mod >= 0 ? c.TurretMods[mod].Of : 1;
        var slot = mod >= 0 ? c.TurretMods[mod].Slot : 0;
        if (_gun.Stale(((long)g.Part << 24) ^ ((long)slot << 8) ^ of ^ (stab ? 1L << 40 : 0)))
            _gun.Text = (of > 1 ? $"TURRET {slot + 1}/{of}  " : "") + gp.D.Name.ToUpperInvariant() + (stab ? "  STAB" : "");
        Hud.ShadowText(dl, new float2(rx, ry), size, _gun.Text, Text, 0.95f);
        ry += 20f * scale;

        var boxes = ConstructWeapons.BoxRounds(c, k, game.Constructs);
        var fault = gp.Broken ? "WRECKED" : g.Problem.Length > 0 ? g.Problem : null;
        if (_ammo.Stale(((long)gp.Rounds << 32) ^ ((long)boxes << 4) ^ (fault?.GetHashCode() ?? 0) ^ (g.NoAmmo ? 2 : 0)))
            _ammo.Text = fault != null ? $"WON'T FIRE: {fault}" : g.NoAmmo ? "ENERGY: fires from the capacitors" : $"RDS {gp.Rounds}/{g.Mag}   BOXES {boxes}";
        Hud.ShadowText(dl, new float2(rx, ry), size, _ammo.Text, fault != null ? Fault : Text, 0.95f);
        ry += 20f * scale;

        // A bar: the load left, or the reload as it comes.
        var bw = 150f * scale;
        var bh = 5f * scale;
        var reloading = gp.Reload > 0 && g.ReloadTicks > 0;
        var fill = reloading ? 1f - gp.Reload / (float)g.ReloadTicks : g.Mag > 0 && !g.NoAmmo ? gp.Rounds / (float)g.Mag : 1f;
        dl.AddRectFilled(new float2(rx, ry), new float2(rx + bw, ry + bh), new ImColor8(0, 0, 0, 140));
        dl.AddRectFilled(new float2(rx, ry), new float2(rx + bw * Math.Clamp(fill, 0f, 1f), ry + bh), Hud.Colour(reloading ? Slew : Sight, 0.9f));
        ry += 12f * scale;

        var offI = (int)MathF.Round(off * 10f);
        if (_state.Stale(offI * 4L + (reloading ? 1 : 0) + (laid ? 2 : 0)))
            _state.Text = reloading ? "RELOADING" : laid ? "LAID" : $"SLEWING  {off:0.0} deg";
        Hud.ShadowText(dl, new float2(rx, ry), size, _state.Text, reloading || !laid ? Slew : Sight, 0.95f);

        // ---- Over the sight: the view and its zoom ----
        if (firstPerson)
        {
            var zi = (int)MathF.Round(zoom);
            if (_top.Stale(zi))
                _top.Text = $"TURRET CAMERA  {zi}x   (wheel: zoom)";
            var tw = Hud.TextWidth(_top.Text, 13f * scale);
            Hud.ShadowText(dl, new float2(cx - tw * 0.5f, cy - 120f * scale), 13f * scale, _top.Text, Sight, 0.8f);
        }
    }

    private static void RightText(ImDrawListPtr dl, float right, float y, float size, string text, Vector3 colour) =>
        Hud.ShadowText(dl, new float2(right - Hud.TextWidth(text, size), y), size, text, colour, 0.95f);
}
