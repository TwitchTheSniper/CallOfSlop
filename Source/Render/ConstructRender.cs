using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Draws the Sandbox constructs: every part box at its body's pose (joints turned, wheels down on their springs, turning
/// and steering), damaged parts darker and wrecked ones near black; the ghost sockets of the held part (blended). The
/// tools in the hands are ToolRender.cs. Near constructs are drawn part by part (far ones culled or drawn as one box: ConstructRenderCull.cs), so
/// each box is one draw like the movers'; lit without the world-space texture, which would swim over a moving surface.
/// Glass panes (<see cref="CPartBox.Glass"/>) are left out of the opaque pass and kept for <see cref="DrawGlass"/>, which
/// draws them in the blended pass after everything opaque: back to front, without depth writes, lit and fogged by the
/// box shader like any part, with a slight tint, a little reflection at grazing angles and a highlight along the edges.
/// </summary>
public static partial class ConstructRender
{
    /// <summary>Boxes drawn last frame (smoke log).</summary>
    public static int LastBoxes { get; private set; }

    // The client's ghost sockets this frame (Program.Sandbox.cs fills them; the blended pass draws them).
    public static readonly ConstructGhost[] Ghosts = new ConstructGhost[ConstructBuild.MaxGhosts];
    public static int GhostCount;
    public static int GhostSelected = -1;
    public static Construct? GhostBody;
    public static int GhostParts; // GhostBody's part count when the ghosts were found (a tick in between that changes it makes them stale)
    public static ConstructPartDef? GhostDef;
    public static bool GhostValid; // the selected one fits as shown
    public static Vector3 GhostEase; // snappy building: the ghost part is drawn this far (the segment's frame) from where it will sit, easing in after a snap (SnapRender.cs)
    public static Vector3? PlaceAt; // free placement: a new construct's origin on the surface aimed at
    public static Quaternion PlaceRot = Quaternion.Identity; // ...its frame
    public static byte PlaceTurn; // ...the part's Rot24 turn in it
    public static bool PlaceValid; // ...green when it fits, red when not
    public static Construct? AimBody; // the part under the crosshair (outlined)
    public static int AimPart = -1;
    public static bool AimRefused; // the held tool would be refused on it (hr01): the outline is red (the reason is on the look line)
    public static int AimKeyBox = -1; // a keypad: the key under the crosshair (only it glows)
    public static bool AimOnPage; // Hands are the mouse on a screen: no glow over what the player is looking at (Program.SandboxPointer.cs)
    public static bool AimHands; // hands can work it (SandboxAim.Touch): it glows yellow, on what they work (a tool's own action lights nothing)
    public static int AimSlot = -1; // ... a stored part: the slot it lies in
    // The welder's freeform preview (board item fw01; Program.SandboxFreeform.cs): the two parts outlined, the bead at their
    // closest points, green when the weld is allowed and red when not; only the first part (the two-click fallback) in amber.
    public static Construct? WeldBodyA, WeldBodyB;
    public static int WeldPartA = -1, WeldPartB = -1;
    public static Vector3 WeldOnA, WeldOnB;
    public static bool WeldValid;
    public static bool ShowWires; // the linking tool is out: the wires are drawn (only then)
    public static ushort WireFromBody; // a wire in progress starts at this part
    public static int WireFromPart = -1;
    public static Vector3 WireEye, WireAim; // (only the wires of constructs near the eye; a wire in progress runs to the aim)
    public static int LastWires { get; private set; }

    /// <summary>The frame's clock (flames flicker, muzzle flashes fade); set before <see cref="Draw"/>.</summary>
    public static double Now;

    /// <summary>An Analog Lever this PC's player has hold of: drawn at its value at once, not eased (Program.SandboxLever.cs).</summary>
    public static Construct? LeverHotBody;
    public static int LeverHotPart = -1;
    private static double _leverDrawnAt;
    private static float _leverEase;

    /// <summary>A gun's muzzle flash (milestone 3): drawn for a moment where it fired.</summary>
    public struct Flash
    {
        public Vector3 At, Dir;
        public double Born;
        public GunFx Fx;
    }

    public static readonly Flash[] Flashes = new Flash[32];
    private static int _flashNext;
    public const double FlashSeconds = 0.06;

    /// <summary>Where the local gunner's shell gun lands as it points now (<see cref="Game.ArtilleryHudLine"/> sets it each frame).</summary>
    public static bool GunnerMarked;
    public static bool SnapGhost, SnapOk; // the hook a held bomb is near (Game.SandboxPhysGunHint)
    public static Vector3 SnapAt, SnapSize;
    public static Quaternion SnapRot;
    public static Vector3 GunnerMark;

    /// <summary>Shells in the air, and the local gunner's marker on the ground.</summary>
    private static int DrawShells(Renderer gfx, ConstructWorld world, float alpha)
    {
        var n = 0;
        int full = 0, far = 0, shapedLeft = ShellShapedBudget;
        var cull = !gfx.ViewProj.IsIdentity; // (no camera: everything)
        var fxDt = ShellFxDt();
        gfx.BeginBatch(); // (their plain boxes in the frame's instanced draw: ConstructRenderShells.cs)
        for (var k = 0; k < world.ShellCount; k++)
        {
            ref var s = ref world.Shells[k];
            if (s.Look is { } look)
            {
                if (s.Landed && s.Kind != ShellKind.Bomb)
                    continue; // (a missile is gone where it hits; a bomb lies until its fuse)
                n += DrawShellLook(gfx, ref s, look, alpha, fxDt, ref shapedLeft, ref full, ref far, cull); // (the model it had on its rack, flying nose first)
                continue;
            }
            var way = Basis(s.Vel.LengthSquared() > 1e-3f ? Vector3.Normalize(s.Vel) : Vector3.UnitY);
            if (s.Kind == ShellKind.Bomb || (s.Flags & ShellFlags.Designed) != 0 && !s.Landed && s.Size.LengthSquared() > 1e-4f)
            {
                // A bomb looks its size, and lies where it hit until its fuse runs out; a designed missile looks its size too.
                gfx.Draw(Matrix4x4.CreateScale(s.Size) * way * Matrix4x4.CreateTranslation(s.Landed ? s.Pos : Vector3.Lerp(s.Prev, s.Pos, alpha)), s.Kind == ShellKind.Bomb ? new Vector3(0.29f, 0.33f, 0.19f) : new Vector3(0.62f, 0.64f, 0.6f));
                n++;
                continue;
            }
            if (s.Landed)
                continue;
            var at = Vector3.Lerp(s.Prev, s.Pos, alpha);
            if (s.Kind == ShellKind.Flare)
                gfx.Draw(Matrix4x4.CreateScale(0.3f) * Matrix4x4.CreateTranslation(at), new Vector3(1f, 0.8f, 0.45f));
            else
                gfx.Draw(Matrix4x4.CreateScale(0.14f, 0.14f, s.Kind >= ShellKind.Rocket ? 1.1f : 0.5f) * way * Matrix4x4.CreateTranslation(at), new Vector3(0.75f, 0.68f, 0.5f));
            n++;
        }
        if (SnapGhost)
        {
            // The hook's ghost: the held bomb's box in lines, green when it will go on, red when it won't.
            var colour = SnapOk ? new Vector3(0.3f, 1f, 0.4f) : new Vector3(1f, 0.25f, 0.2f);
            var turn = Matrix4x4.CreateFromQuaternion(SnapRot);
            var h = SnapSize * 0.5f;
            const float t = 0.03f;
            for (var a = -1; a <= 1; a += 2)
                for (var b = -1; b <= 1; b += 2)
                {
                    gfx.Draw(Matrix4x4.CreateScale(SnapSize.X, t, t) * Matrix4x4.CreateTranslation(0, a * h.Y, b * h.Z) * turn * Matrix4x4.CreateTranslation(SnapAt), colour);
                    gfx.Draw(Matrix4x4.CreateScale(t, SnapSize.Y, t) * Matrix4x4.CreateTranslation(a * h.X, 0, b * h.Z) * turn * Matrix4x4.CreateTranslation(SnapAt), colour);
                    gfx.Draw(Matrix4x4.CreateScale(t, t, SnapSize.Z) * Matrix4x4.CreateTranslation(a * h.X, b * h.Y, 0) * turn * Matrix4x4.CreateTranslation(SnapAt), colour);
                    n += 3;
                }
            SnapGhost = false; // (set again each frame while it lasts)
        }
        if (GunnerMarked)
        {
            var size = MathF.Max(0.6f, Vector3.Distance(gfx.CameraPosition, GunnerMark) * 0.012f);
            gfx.Draw(Matrix4x4.CreateScale(size * 4f, size * 0.2f, size * 0.5f) * Matrix4x4.CreateTranslation(GunnerMark + new Vector3(0, size * 0.3f, 0)), new Vector3(1f, 0.4f, 0.1f));
            gfx.Draw(Matrix4x4.CreateScale(size * 0.5f, size * 0.2f, size * 4f) * Matrix4x4.CreateTranslation(GunnerMark + new Vector3(0, size * 0.32f, 0)), new Vector3(1f, 0.4f, 0.1f));
            n += 2;
        }
        gfx.EndBatch();
        if (!CameraFeeds.InPass)
            ShellDetailNext();
        (LastShellsFull, LastShellsFar, LastShellDraws) = (full, far, n);
        return n;
    }

    public static void AddFlash(Vector3 at, Vector3 dir, double now, GunFx fx)
    {
        Flashes[_flashNext] = new Flash { At = at, Dir = dir, Born = now, Fx = fx };
        CallOfSlop.Fx.Muzzle(at, dir, fx switch { GunFx.Cannon => 0.7f, GunFx.Rocket => 0.5f, GunFx.Flame => 0.3f, _ => 0.25f });
        _flashNext = (_flashNext + 1) % Flashes.Length;
    }

    // Physical storage (decision 18): the slots the held part fits on the storage part under the crosshair (ghost outlines).
    public static Construct? SlotBody;
    public static int SlotPart = -1;
    public static int SlotMask; // bit k: slot k takes the held part
    public static int SlotSelected = -1; // the one a click sets it into
    public static byte SlotRot; // the turn it would go in at
    public static int SlotCopy; // where in a shelf's pile it would stand
    public static ConstructPartDef? SlotDef; // the held part
    public static int LastStored { get; private set; } // stored parts drawn last frame (the self-test)

    public static RemoverSelection? RemoverSel; // the remover tool is out: its marked parts and links are drawn red (set each frame by its input)

    public static void ClearAim()
    {
        AidGhostBody = null;
        AidCount = 0;
        PlaneBody = null;
        GridBody = null;
        BalanceBody = null;
        RemoverSel = null;
        PartSel = null;
        SelBoxBody = null;
        SelWorldOn = false;
        SelGhostClip = null;
        SlotBody = null;
        SlotPart = -1;
        SlotMask = 0;
        SlotSelected = -1;
        SlotDef = null;
        GhostCount = 0;
        GhostSelected = -1;
        GhostBody = null;
        GhostParts = 0;
        GhostDef = null;
        PlaceAt = null;
        AimBody = null;
        AimPart = -1;
        AimRefused = false;
        AimOnPage = false;
        AimHands = false;
        AimKeyBox = -1;
        AimSlot = -1;
        AimMotion = false;
        SideOutlines = MotionBoxes = 0;
        WeldBodyA = WeldBodyB = null;
        WeldPartA = WeldPartB = -1;
        ConnectorRender.NoCollideShow = false;
        SocketWeldRender.Clear();
        ShowWires = false;
        RouteRender.ClearPreview();
        WireFromBody = 0;
        WireFromPart = -1;
    }

    /// <summary>Phase 6: the construct the local player rides in another star system (draw only it), or 0 (draw all but those away).</summary>
    public static ushort AwayOnly;

    /// <summary>Every construct at this frame's pose: <paramref name="alpha"/> blends the last two ticks (the host); a client's poses are blended already.</summary>
    public static void Draw(Renderer gfx, ConstructWorld world, float alpha, bool blendTicks)
    {
        var n = 0;
        _leverEase = 1 - MathF.Exp(-Math.Clamp((float)(Now - _leverDrawnAt), 0, 0.25f) * AnalogLever.ShownRate);
        _leverDrawnAt = Now;
        LogicDisplays.Sample(world, Now);
        GlassCount = 0;
        LastBatchBoxes = LastBatchWalked = 0;
        var cull = !gfx.ViewProj.IsIdentity; // (no camera yet, a headless renderer: everything, part by part)
        CullPlanes(gfx.ViewProj);
        PanelRender.Begin();
        if (!CameraFeeds.InPass && Matrix4x4.Invert(gfx.ViewRelative, out var viewInverse))
            PanelRender.BeginWorld(gfx.CameraPosition - gfx.Origin, Vector3.Normalize(-new Vector3(viewInverse.M31, viewInverse.M32, viewInverse.M33)));
        var cam = gfx.CameraPosition;
        int culled = 0, lod = 0;
        gfx.BeginBatch(); // (the small bodies' boxes in one instanced draw, not a draw each)
        var merge = MergeOn && gfx.MergedMeshes; // (static parts as merged meshes: ConstructRenderBatch.cs)
        BeginMergedFrame();
        var loopT0 = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (var c in world.Bodies)
        {
            if (AwayOnly != 0 ? c.Id != AwayOnly : c.SpaceAway != 0)
                continue; // (phase 6: a ship in another star system is seen only by its own riders, who see nothing else: GameSpaceJump.cs)
            var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
            var rot = blendTicks ? Quaternion.Slerp(c.PrevRot, c.Rot, alpha) : c.Rot;
            var level = cull ? CullLevel(cam, c, pos, rot) : 1; // (out of view or far: nothing, or one box: ConstructRenderCull.cs)
            if (Fx.On && (c.ThrustShow > 0 || c.SmokeLevel > 0 || c.BurnerCount > 0 || c.WheelCount > 0 || c.FlameStamp != c.RebuildStamp || c.FlameCount > 0))
                EmitFlames(c, Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos), pos, cam, blendTicks ? alpha : 1f); // (seen or not: a flame behind the camera is in view next frame)
            if (level == 0)
            {
                culled++;
                continue;
            }
            // The body's frame from the render origin (a ship far out in space: Renderer.SetCameraAt): the position less the
            // origin first, so its parts and the seated camera agree to the bit and nothing shakes. Everything drawn from
            // `body` below is relative (gfx.Relative); points worked out in the world (the steering wheel, chains) aren't.
            var origin = gfx.Origin;
            var rotM = Matrix4x4.CreateFromQuaternion(rot);
            var body = rotM * Matrix4x4.CreateTranslation(pos - origin);
            if (c.ModActive >= 0)
                EngineFx.Frame(c, origin == Vector3.Zero ? body : rotM * Matrix4x4.CreateTranslation(pos), Now, alpha); // (a modular engine: its crank turns, it shakes, it smokes; seen from afar too)
            gfx.Relative = true;
            if (level == 2)
            {
                n += DrawLod(gfx, c, body);
                gfx.Relative = false;
                lod++;
                continue;
            }
            if (!merge || !DrawMergedBody(gfx, world, c, body, blendTicks ? alpha : 1f, cull, ref n)) // (too few static boxes, or merging off: part by part)
                for (var i = 0; i < c.PartCount; i++)
                {
                    n += DrawPart(gfx, world, c, i, body, blendTicks ? alpha : 1f, 1f);
                    if (c.Parts[i].Store != 0)
                        n += DrawStored(gfx, world, c, i, body, blendTicks ? alpha : 1f);
                }
            if (c.PanelParts.Length > 0 && !CameraFeeds.InPass)
                PanelRender.Gather(gfx, world, c, body, blendTicks ? alpha : 1f, cull ? _planes : default, Now); // (display panels near the eye and facing it)
            if (c.HasLogic || c.HasVolumeStore)
                n += DrawVolumes(gfx, c, body, blendTicks ? alpha : 1f); // (cargo volumes: a logic part each)
            if (c.Frozen || c.FrozenPieces > 0)
            {
                // Frozen by the physics gun: an icy outline round each part of each frozen piece (ConstructGrab.cs): one box round the whole would read
                // as its collider, and it is not. A big construct shows the frost tint alone.
                if (c.PartCount <= FrozenOutlineParts)
                    for (var i = 0; i < c.PartCount; i++)
                    {
                        if (!c.PieceFrozen(i))
                            continue;
                        c.ShapeBounds(i, out var mn, out var mx);
                        c.SegPose(c.Parts[i].Seg, out var sq, out var st);
                        var seg = Matrix4x4.CreateFromQuaternion(sq) * Matrix4x4.CreateTranslation(st) * body;
                        Outline(gfx, (mn + mx) * 0.5f, (mx - mn) * 0.5f + new Vector3(0.02f), seg, Frost, 0.5f);
                        n += 12;
                    }
            }
            gfx.Relative = false;
            n += DrawWheel(gfx, c, alpha, blendTicks); // (from the seat's world pose)
        }
        var loopT1 = System.Diagnostics.Stopwatch.GetTimestamp();
        FlushMerged(gfx);
        gfx.EndBatch();
        LoopMs += System.Diagnostics.Stopwatch.GetElapsedTime(loopT0, loopT1).TotalMilliseconds;
        FlushMs += System.Diagnostics.Stopwatch.GetElapsedTime(loopT1).TotalMilliseconds;
        BatchSweep(gfx, world);
        LastCulled = culled;
        LastLod = lod;
        LastStored = LastStoredCount;
        LastStoredCount = 0;
        n += DrawCouplings(gfx, world, alpha, blendTicks);
        n += DrawTracks(gfx, world, alpha, blendTicks); // (tracks: links running round their wheels; instanced)
        RouteRender.Draw(gfx, world, alpha, blendTicks); // (routed links: cables, pipes, shafts, chutes; instanced)
        n += RouteRender.LastSegments;
        n += ConnectorRender.Draw(gfx, world, alpha, blendTicks); // (ropes and springs)
        n += DrawShells(gfx, world, alpha); // (artillery in the air and the gunner's marker)
        n += WorldLinkRender.Draw(gfx, world); // (world links across the ground, hoses off their reels: board item wl01)
        if (!CameraFeeds.InPass)
            PanelRender.Flush(gfx); // (the display panels of everything above, one draw: PanelRender.cs)
        // Muzzle flashes (unlit, bright, a few frames).
        for (var k = 0; k < Flashes.Length; k++)
        {
            ref var f = ref Flashes[k];
            var age = Now - f.Born;
            if (f.Born <= 0 || age < 0 || age > FlashSeconds)
                continue;
            var size = f.Fx switch { GunFx.Cannon => 0.55f, GunFx.Rocket => 0.4f, GunFx.Flame => 0.35f, _ => 0.22f } * (1f - (float)(age / FlashSeconds) * 0.5f);
            gfx.Draw(Matrix4x4.CreateScale(size * 0.6f, size * 0.6f, size) * Basis(f.Dir) * Matrix4x4.CreateTranslation(f.At + f.Dir * size * 0.5f), new Vector3(1f, 0.8f, 0.35f), 0.9f, mode: 2);
            n++;
        }
        LastBoxes = n;
    }

    /// <summary>Milliseconds spent walking the bodies (culling, merged chunks queued, other parts drawn box by box) and in the
    /// last flush (the merged draws and the box batch), added up since the reader last cleared them (the GL bench).</summary>
    public static double LoopMs, FlushMs;
    private const int FrozenOutlineParts = 24;

    private static Matrix4x4 Basis(Vector3 z)
    {
        z = z.LengthSquared() > 1e-6f ? Vector3.Normalize(z) : -Vector3.UnitZ;
        var x = MathF.Abs(z.Y) < 0.95f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z)) : Vector3.UnitX;
        var y = Vector3.Cross(z, x);
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
    }

    /// <summary>A point on part <paramref name="i"/> (in its own frame) where it is drawn this frame.</summary>
    public static Vector3 DrawnPoint(Construct c, int i, Vector3 local, float alpha, bool blendTicks)
    {
        ref var p = ref c.Parts[i];
        var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
        var rot = blendTicks ? Quaternion.Slerp(c.PrevRot, c.Rot, alpha) : c.Rot;
        var sq = Quaternion.Identity;
        var st = Vector3.Zero;
        if (p.Seg > 0 && p.Seg <= c.JointCount)
            SegPoseBlended(c, p.Seg, blendTicks ? alpha : 1f, out sq, out st);
        return pos + Vector3.Transform(st + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, local), sq), rot);
    }

    /// <summary>Boxes in the last frame's chains (the self-test).</summary>
    public static int LastChainLinks { get; private set; }

    /// <summary>
    /// The couplings (milestone 3): a chain from its hook to the eye, sagging when it is slack (links turned alternately),
    /// and a short pin through a tow ball's eye. Drawn where both ends are this frame.
    /// </summary>
    private static int DrawCouplings(Renderer gfx, ConstructWorld world, float alpha, bool blendTicks)
    {
        var n = 0;
        LastChainLinks = 0;
        for (var k = 0; k < world.CouplingCount; k++)
        {
            ref var cp = ref world.Couplings[k];
            if (world.Get(cp.BodyA) is not { } a || world.Get(cp.BodyB) is not { } b || cp.PartA >= a.PartCount || cp.PartB >= b.PartCount)
                continue;
            var pa = DrawnPoint(a, cp.PartA, a.DefOf(cp.PartA).HitchAt, alpha, blendTicks);
            var pb = DrawnPoint(b, cp.PartB, b.DefOf(cp.PartB).HitchAt, alpha, blendTicks);
            var steel = new Vector3(0.32f, 0.33f, 0.35f);
            if (cp.Length <= 0)
            {
                gfx.Draw(Matrix4x4.CreateScale(0.05f, 0.14f, 0.05f) * Matrix4x4.CreateTranslation((pa + pb) * 0.5f), steel, 1f, mode: 0); // (the pin)
                n++;
                continue;
            }
            // A slack chain sags: its middle hangs down by about how much longer it is than the span.
            var span = Vector3.Distance(pa, pb);
            var sag = span < cp.Length ? MathF.Sqrt(MathF.Max(0, cp.Length * cp.Length - span * span)) * 0.5f : 0f;
            var mid = (pa + pb) * 0.5f - new Vector3(0, sag, 0);
            var links = Math.Clamp((int)(cp.Length / 0.12f), 4, 64);
            var prev = pa;
            for (var i = 1; i <= links; i++)
            {
                var t = i / (float)links;
                var u = 1 - t;
                var at = u * u * pa + 2 * u * t * mid + t * t * pb; // (a quadratic curve through the sag)
                Link(gfx, prev, at, steel, (i & 1) == 0);
                prev = at;
                n++;
            }
            LastChainLinks += links;
        }
        return n;
    }

    /// <summary>One chain link from <paramref name="a"/> to <paramref name="b"/>, flat one way or turned a quarter.</summary>
    private static void Link(Renderer gfx, Vector3 a, Vector3 b, Vector3 colour, bool turned)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 1e-3f)
            return;
        var z = d / len;
        var x = MathF.Abs(z.Y) < 0.95f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z)) : Vector3.UnitX;
        var y = Vector3.Cross(z, x);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        var size = turned ? new Vector3(0.018f, 0.045f, len * 1.15f) : new Vector3(0.045f, 0.018f, len * 1.15f);
        gfx.Draw(Matrix4x4.CreateScale(size) * basis * Matrix4x4.CreateTranslation((a + b) * 0.5f), colour, 1f, mode: 0);
    }

    /// <summary>A hand control's moving box, out of its rest pose: a lever's handle and a knob by the value, a slider along it, a rocker and a cap by whether the part is on.</summary>
    private static Matrix4x4 ControlMove(in CPartBox box, in CPart p, Vector3 leverAxis, float leverAngle)
    {
        var pivot = p.Pos + Rot24.Apply(p.Rot, box.RigPivot);
        switch (box.Rig)
        {
            case BoxRig.Knob:
                return Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateFromAxisAngle(Rot24.Apply(p.Rot, Vector3.UnitY), leverAngle * AnalogLever.KnobTurn) * Matrix4x4.CreateTranslation(pivot);
            case BoxRig.Slide:
                return Matrix4x4.CreateTranslation(Rot24.Apply(p.Rot, Vector3.UnitZ) * (box.RigTravel * leverAngle / AnalogLever.Arc));
            case BoxRig.Flip:
                return Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateFromAxisAngle(leverAxis, p.Out >= 0.5f ? AnalogLever.FlipAngle : -AnalogLever.FlipAngle) * Matrix4x4.CreateTranslation(pivot);
            case BoxRig.Press:
                return p.Out >= 0.5f ? Matrix4x4.CreateTranslation(Rot24.Apply(p.Rot, Vector3.UnitY) * -box.RigTravel) : Matrix4x4.Identity;
            default:
                return Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateFromAxisAngle(leverAxis, leverAngle) * Matrix4x4.CreateTranslation(pivot);
        }
    }

    /// <summary>A gauge's needle (milestone 3): from its hub on the dial's face, swept 270 degrees from empty to full scale by its signal.</summary>
    private static void DrawNeedle(Renderer gfx, ConstructPartDef d, in CPart p, in Matrix4x4 partToBody)
    {
        var n = ConstructParts.DirVector(d.DialDir);
        var up = MathF.Abs(n.Y) > 0.5f ? -Vector3.UnitZ : Vector3.UnitY;
        var right = Vector3.Cross(up, n);
        var share = Math.Clamp(p.Out / d.DialMax, 0, 1);
        var a = (-135f + 270f * share) * MathF.PI / 180f;
        var dir = up * MathF.Cos(a) + right * MathF.Sin(a);
        var x = Vector3.Cross(dir, n);
        const float len = 0.075f;
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, dir.X, dir.Y, dir.Z, 0, n.X, n.Y, n.Z, 0, 0, 0, 0, 1);
        var at = d.DialAt + dir * (len * 0.45f) + n * 0.008f;
        gfx.Draw(Matrix4x4.CreateScale(0.012f, len, 0.008f) * basis * Matrix4x4.CreateTranslation(at) * partToBody, new Vector3(0.95f, 0.25f, 0.15f), 1f, mode: 2);
        gfx.Draw(Matrix4x4.CreateScale(0.02f) * Matrix4x4.CreateTranslation(d.DialAt + n * 0.008f) * partToBody, new Vector3(0.15f, 0.15f, 0.15f), 1f, mode: 0); // (the hub)
    }

    private static int DrawPart(Renderer gfx, ConstructWorld world, Construct c, int i, in Matrix4x4 body, float alpha, float opacity)
    {
        ref var p = ref c.Parts[i];
        var d = p.D;
        // A merged receiver (ConstructMerge.cs): its lead draws it N times bigger; the other members aren't drawn.
        var mergeScale = 0f;
        var mergeOrigin = Vector3.Zero;
        if (c.MergeOf[i] != 0)
        {
            if (!c.MergeLead(i))
                return 0;
            ref var m = ref c.Merges[c.MergeOf[i] - 1];
            mergeScale = m.N;
            mergeOrigin = m.Origin;
        }
        // The segment's pose (a door turned on its hinge).
        var seg = Matrix4x4.Identity;
        if (p.Seg > 0 && p.Seg <= c.JointCount)
        {
            seg = SegMatrix(c, p.Seg, alpha);
        }
        // A raycast wheel: down its suspension, turning, steering.
        var wheel = Matrix4x4.Identity;
        var spinAxis = Vector3.UnitX;
        var spin = 0f;
        var isWheel = false;
        for (var k = 0; k < c.WheelCount; k++)
        {
            ref var w = ref c.Wheels[k];
            if (w.Part != i)
                continue;
            isWheel = true;
            wheel = HubMatrix(c, k); // (steering: the wheel's segment turns it, a steering pivot)
            spin = w.Spin;
            spinAxis = c.RollAxis(k); // (w.Axle alone points opposite ways on the left and right wheels: they drew turning against each other)
            break;
        }
        // A track doesn't turn as one: its "roll" boxes do, on the spot, by how far the construct has come along its length.
        var rollRadius = 0f;
        var rollAxis = Vector3.UnitX;
        var rollDistance = 0f;
        if (isWheel && d.TrackRays > 0)
        {
            isWheel = false;
            rollRadius = d.WheelRadius;
            rollAxis = spinAxis;
            rollDistance = Vector3.Dot(c.Pos, Vector3.Transform(c.DriveFwd, c.Rot));
            for (var k = 0; k < c.WheelCount; k++)
                if (c.Wheels[k].Part == i)
                {
                    wheel = ConstructTracks.UnitPose(c, i, k); // (the whole unit lies on its rays: up its travel, tilted along)
                    rollDistance = c.Wheels[k].Spin * c.Wheels[k].Radius; // (its own track's run: the sides differ in a skid turn)
                    break;
                }
        }
        float health;
        if (mergeScale > 0)
        {
            ConstructMerge.Health(c, i, out var hp, out var most);
            health = Math.Clamp(hp / MathF.Max(1, most), 0, 1);
        }
        else
            health = Math.Clamp(p.Health / MathF.Max(1, d.Health), 0, 1);
        var shade = p.Broken ? 0.25f : 0.55f + 0.45f * health;
        var wear = p.Broken ? 1f : 1f - health; // (a material rusts and scorches by it: Surfaces.cs)
        var drawn = 0;
        var rig = d.HasRig && d.Role == PartRole.Suspension ? RigOf(c, i) : default;
        var leverAxis = Rot24.Apply(p.Rot, Vector3.UnitX);
        var leverAngle = 0f;
        if (d.Logic == CLogic.AnalogLever)
        {
            // Its handle: the value as it came, eased between the server's few updates (the hand on it sees it at once).
            var value = AnalogLever.Value(p, d);
            p.LeverShown = p.LeverShown < 0 || LeverHotBody == c && LeverHotPart == i ? value : float.Lerp(p.LeverShown, value, _leverEase);
            leverAngle = AnalogLever.Angle(p.LeverShown);
        }
        // An overloaded actuator (the strength model): tinted red, deeper the more it is past its rating (full at twice), pulsing a little.
        var strain = 0f;
        if (d.Role == PartRole.Joint && !p.Broken && c.RailJointOf(i) is var oj and >= 0 && c.Joints[oj].Over > 1)
            strain = Math.Clamp(c.Joints[oj].Over - 1, 0, 1) * (0.75f + 0.25f * MathF.Sin((float)Now * 9f));
        var lit = d.Logic == CLogic.Lamp && p.Out >= 0.5f && !p.Broken; // a lamp that is on glows (unlit, bright)
        var glow = new Vector3(1f, 0.93f, 0.6f);
        if (d.Logic == CLogic.Lamp)
        {
            // Its settings (the configure tool): the colour, the brightness and a blink; the panel's preview lights it.
            var blink = ConstructConfig.Get(p, d, CfgKey.Blink, 0);
            if (lit && blink > 0 && (Now * blink) % 1.0 >= 0.5)
                lit = false;
            var preview = PreviewBody == c && PreviewPart == i;
            if (!p.Broken && preview)
                lit = true; // (the panel's live preview: lit in the colour being picked)
            if (ConstructConfig.Has(d, CfgKey.Colour))
                glow = preview
                    ? ConstructConfig.Rgb(PreviewValue(d, CfgKey.Colour, 0xFFED99)) * (PreviewValue(d, CfgKey.Brightness, 100) / 100f)
                    : ConstructConfig.Rgb(ConstructConfig.Get(p, d, CfgKey.Colour, 0xFFED99)) * (ConstructConfig.Get(p, d, CfgKey.Brightness, 100) / 100f);
        }
        if (d.IsSpawner && ConstructSpawners.Active(c, i))
        {
            lit = true; // a spawner that is on shows its light (GameSandboxSpawns.cs)
            glow = d.Logic == CLogic.BotSpawner ? new Vector3(0.45f, 0.75f, 1f) : new Vector3(0.6f, 1f, 0.35f);
        }
        if (d.HasDial && !p.Broken)
        {
            DrawNeedle(gfx, d, p, Matrix4x4.CreateFromQuaternion(Rot24.Quat(p.Rot)) * Matrix4x4.CreateTranslation(p.Pos) * seg * body);
            drawn += 2;
        }
        // A piston's rod, from its base up to where its leaf has slid to.
        if (d.Role == PartRole.Joint && d.JointType == JointType.Piston && !d.IsRail && c.JointOf(i) is var pj and >= 0 && c.Joints[pj].Angle > 0.01f)
        {
            ref var jt = ref c.Joints[pj];
            var ext = float.Lerp(jt.PrevAngle, jt.Angle, alpha);
            c.SocketAt(i, d.LeafIndex, out var leaf, out _);
            var mid = leaf + jt.Axis * (ext * 0.5f);
            var size = new Vector3(0.12f) + Vector3.Abs(jt.Axis) * (ext - 0.12f);
            gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(mid) * seg * body, new Vector3(0.78f, 0.8f, 0.84f) * shade, 1f, mode: 0);
            drawn++;
        }
        if (d.Role == PartRole.Joint && d.JointType == JointType.Flex && c.JointOf(i) is var fj and >= 0)
            drawn += DrawFlexRings(gfx, c, i, fj, alpha, seg * body, shade);
        if (p.Burn > 0)
        {
            // On fire (incendiary rounds, milestone 3): a few flickering tongues of flame over its top.
            c.PartBounds(i, out var mn, out var mx);
            var top = new Vector3((mn.X + mx.X) * 0.5f, mx.Y, (mn.Z + mx.Z) * 0.5f);
            var w = MathF.Min(mx.X - mn.X, mx.Z - mn.Z);
            if (Fx.On)
            {
                // Particles (Source/Render/Fx/Fx.cs): the flames lick up from its top, and it smokes.
                var onBody = seg * body;
                var at = Vector3.Transform(top, onBody) + (gfx.Relative ? gfx.Origin : Vector3.Zero);
                var up = Vector3.TransformNormal(Vector3.UnitY, onBody);
                Fx.Flame(FxKind.Flame, at, up, c.Vel, MathF.Max(0.1f, w * 0.3f), 0.7f + w * 0.4f, 0.7f, _fxDt);
                for (var k = Fx.Share(5f * _fxDt); k > 0; k--)
                    Fx.Emit(FxKind.DarkSmoke, at + up * 0.3f, c.Vel + up * 1.6f, 0.2f + w * 0.2f, 2.2f);
            }
            else
            for (var f = 0; f < 3; f++)
            {
                var flick = 0.65f + 0.35f * MathF.Sin((float)Now * (13f + f * 4.3f) + i * 1.7f + f * 2.1f);
                var h = (0.25f + 0.2f * f) * flick;
                var off = new Vector3((f - 1) * w * 0.25f, h * 0.5f, (f == 1 ? 0.1f : -0.1f) * w);
                gfx.Draw(Matrix4x4.CreateScale(w * (0.45f - f * 0.08f), h, w * (0.45f - f * 0.08f)) * Matrix4x4.CreateTranslation(top + off) * seg * body,
                    f == 1 ? new Vector3(1f, 0.85f, 0.3f) : new Vector3(1f, 0.45f, 0.1f), 0.85f, mode: 2);
                drawn++;
            }
        }
        // A linear rail draws one carriage, on the section that has it, going with what hangs on it (ConstructLinear.cs).
        var railShown = !d.IsRail || c.RailShowsCarriage(i);
        var leafMoves = d.IsRail || d.Role == PartRole.Joint && d.JointType == JointType.Swivel;
        var railSeg = leafMoves && railShown && c.JointOf(i) is var rj and >= 0 ? SegMatrix(c, rj + 1, alpha) : seg;
        for (var b = 0; b < d.Boxes.Length; b++)
        {
            ref readonly var box = ref d.Boxes[b];
            if (box.Hidden || box.IsLeaf && !railShown)
                continue; // (a shaped part's stepped collision: its shape is drawn instead)
            c.BoxOf(i, b, out var centre, out var half);
            if (box.Spin && d.Rotor != null)
            {
                drawn += DrawBlades(gfx, c, i, d, in box, centre, seg * body, shade, opacity);
                continue;
            }
            Matrix4x4 model;
            if (isWheel)
            {
                // Turn about the axle through the wheel's centre.
                c.PartBounds(i, out var mn, out var mx);
                var hub = (mn + mx) * 0.5f;
                model = box.Model(p.Rot, Vector3.Zero) * Matrix4x4.CreateTranslation(centre - hub) * Matrix4x4.CreateFromAxisAngle(spinAxis, spin) * Matrix4x4.CreateTranslation(hub);
            }
            else if (box.Roll && rollRadius > 0)
                model = box.Model(p.Rot, Vector3.Zero) * Matrix4x4.CreateFromAxisAngle(rollAxis, rollDistance / half.Y) * Matrix4x4.CreateTranslation(centre); // (a track's drive wheel or roller, turning as the construct runs)
            else if (mergeScale > 0)
                model = Matrix4x4.CreateScale(mergeScale) * box.Model(p.Rot, mergeOrigin + Rot24.Apply(p.Rot, box.Center * mergeScale));
            else if (c.PartScale[i] > 1)
                model = Matrix4x4.CreateScale(c.PartScale[i]) * box.Model(p.Rot, centre); // (a gun part on a merged receiver: N times bigger)
            else
            {
                model = box.Model(p.Rot, centre);
                if (d.IsOrdnance)
                    FoldWing(d, b, in box, p.Rot, centre, ref model); // (a designed weapon's pop-out wings, folded until it drops)
            }
            if (box.IsLeaf && d.Surface is { Control: > 0 } flap)
                model *= FlapTurn(c, i, flap, in p); // (a control surface's leaf, on the hinge in the part)
            if (box.Rig >= BoxRig.Lever)
                model *= ControlMove(in box, in p, leverAxis, leverAngle);
            else if (box.Rig != BoxRig.None)
                model = rig.Model(in box, in p, model);
            if (d.HasEngineBoxes && c.EngineOf[i] != 0 && c.EngineOf[i] == c.ModActive + 1)
                EngineFx.Animate(c, i, in box, centre, ref model);
            if (c.ModActive >= 0 && c.EngineOf[i] == c.ModActive + 1)
                model *= Matrix4x4.CreateTranslation(c.EngineShake); // (a big engine rocks on its mounts)
            model = model * wheel * (box.IsLeaf && leafMoves ? railSeg : box.IsLeaf && d.JointType == JointType.Flex && d.Role == PartRole.Joint && c.JointOf(i) is var lj and >= 0 ? SegMatrix(c, c.LeafSegOf(lj), alpha) : seg) * body; // (a flexible joint's moving side goes with what hangs on it)
            if ((d.IsBeacon || d.IsTerminal) && b == d.Boxes.Length - 1 && !p.Broken && world.StoreOf(c, i) is { Busy: true })
            {
                gfx.Draw(model, BeaconLamp, opacity, mode: 2, shape: box.Shape); // (an Inventory Beacon's lamp, its last box: lit while it broadcasts)
                drawn++;
                continue;
            }
            var tint = ConstructPaint.BoxColour(p.Paint, in box) * shade; // (the paint tool: a painted part's paintable boxes, ConstructPaint.cs)
            if (lit)
                gfx.Draw(model, glow, opacity, mode: 2, shape: box.Shape);
            else if (box.Glow)
                gfx.Draw(model, box.Color * shade, opacity, mode: 2, shape: box.Shape); // (a box that always glows, "glow" in the part file)
            else if (box.IsGlass)
            {
                if (!QueueGlass(model, box.Color * shade, box.Glass * opacity, box.Shape))
                    gfx.Draw(model, box.Color * shade, opacity, mode: 0, shape: box.Shape); // (more panes than the list holds: opaque)
                continue;
            }
            else
                gfx.Draw(model, (c.Frozen || c.FrozenPieces > 0) && c.PieceFrozen(i) ? Vector3.Lerp(tint, Frost, 0.45f) : strain > 0 ? Vector3.Lerp(tint, OverloadTint, 0.2f + 0.5f * strain) : tint, opacity, mode: 0, shape: box.Shape, surface: Surfaces.Code(Surfaces.OnPaint(box.Surface, p.Paint, box.Paintable), wear)); // (frozen by the physics gun: an icy tint; overloaded: red)
            drawn++;
        }
        return drawn;
    }

    /// <summary>A control surface's leaf: its turn about the hinge in the part.</summary>
    private static Matrix4x4 FlapTurn(Construct c, int part, SurfaceDef d, in CPart p)
    {
        for (var k = 0; k < c.ControlCount; k++)
        {
            ref var s = ref c.Surfaces[c.Controls[k]];
            if (s.Part != part)
                continue;
            var at = p.Pos + Rot24.Apply(p.Rot, d.Hinge);
            var span = Vector3.Cross(s.F, s.N);
            return Matrix4x4.CreateTranslation(-at) * Matrix4x4.CreateFromAxisAngle(span, -s.Deflect) * Matrix4x4.CreateTranslation(at);
        }
        return Matrix4x4.Identity;
    }

    private const float BladeNear = 25f; // radii: past this each blade is one box
    private const float BlurOmega = 45f; // rad/s: past this a rotor's blades are a blur: at a frame rate the real angle strobes

    /// <summary>A rotor's blades: one box of the part, repeated round the hub, turned by its angle; past a speed they slow to a blur over a faint disc.</summary>
    private static int DrawBlades(Renderer gfx, Construct c, int i, ConstructPartDef d, in CPartBox box, Vector3 centre, in Matrix4x4 frame, float shade, float opacity)
    {
        var k = ConstructRotor.Of(c, i);
        if (k < 0)
            return 0;
        ref var r = ref c.Rotors[k];
        ref var p = ref c.Parts[i];
        var rd = d.Rotor!;
        var hub = p.Pos + Rot24.Apply(p.Rot, rd.At);
        var axis = Rot24.Apply(p.Rot, rd.Axis);
        var back = Matrix4x4.CreateTranslation(hub);
        var style = AirTable.Current.BladeStyles[r.Style];
        var near = !Hud.Camera.Valid || Vector3.DistanceSquared(c.Pos, Hud.Camera.Eye) < MathF.Pow(BladeNear * MathF.Max(1, rd.Radius), 2); // (four boxes a blade close to, one far off: a 16-blade disc stays 17 draws)
        var turn = Matrix4x4.CreateFromQuaternion(Rot24.Quat(p.Rot));
        Span<Matrix4x4> local = stackalloc Matrix4x4[4];
        // The blade's box, as the style shapes it: along the part's x, its chord along y, each station its width and its offset off the radius.
        var pieces = near ? 4 : 1;
        for (var s = 0; s < pieces; s++)
        {
            var st = near ? style.Stations[s] : new Vector2(style.Area, style.Stations[2].Y);
            var length = box.Half.X * 2 / pieces;
            var mid = box.Center.X - box.Half.X + length * (s + 0.5f);
            var size = new Vector3(length * 1.02f, box.Half.Y * 2 * st.X, box.Half.Z * 2);
            local[s] = Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(new Vector3(mid, box.Center.Y + st.Y * rd.Radius, box.Center.Z)) * turn * Matrix4x4.CreateTranslation(p.Pos - hub);
        }
        var fast = r.Omega > BlurOmega;
        var angle = fast ? (float)(Now * BlurOmega * 0.37f) * r.Sense : r.Angle;
        var tint = ConstructPaint.BoxColour(p.Paint, in box) * shade;
        var drawn = 0;
        for (var b = 0; b < r.Blades; b++)
        {
            if ((r.Lost >> b & 1) != 0)
                continue;
            var at = Matrix4x4.CreateFromAxisAngle(axis, angle + b * MathF.Tau / r.Blades) * back * frame;
            for (var s = 0; s < pieces; s++)
                gfx.Draw(local[s] * at, tint, opacity, mode: 0, shape: box.Shape);
            drawn += pieces;
        }
        if (fast)
        {
            // The disc: as wide as its tips swing, and as dense as its blades fill it.
            var across = rd.Radius * 2 * MathF.Sqrt(1 + style.Stations[3].Y * style.Stations[3].Y);
            var size = Vector3.One * across + Vector3.Abs(axis) * (0.02f - across);
            var solid = r.Alive * rd.Chord * style.Area / (MathF.PI * rd.Radius);
            QueueGlass(Matrix4x4.CreateScale(size) * back * frame, tint, Math.Clamp(0.1f + 0.6f * solid, 0.1f, 0.45f) * MathF.Min(1, r.Omega / (2 * BlurOmega)) * opacity, DiscShape);
            drawn++;
        }
        return drawn;
    }

    private static readonly ushort DiscShape = PartShapes.For(new PartBox { Shape = "cylinder", Size = Vector3.One, Segments = 24, Inner = -1 });

    /// <summary>A working Inventory Beacon's lamp.</summary>
    public static readonly Vector3 BeaconLamp = new(0.35f, 1f, 0.55f);

    /// <summary>An overloaded actuator's tint (the strength model, ConstructPhysics.MotorPrep).</summary>
    public static readonly Vector3 OverloadTint = new(0.95f, 0.18f, 0.12f);

    /// <summary>The frozen tint and outline (the physics gun).</summary>
    public static readonly Vector3 Frost = new(0.62f, 0.86f, 1f);


    /// <summary>What a storage part holds, where it sits (each stored part's boxes in its slot, a pile stacked up), and a bin's fill level.</summary>
    private static int DrawStored(Renderer gfx, ConstructWorld world, Construct c, int i, in Matrix4x4 body, float alpha)
    {
        if (world.StoreOf(c, i) is not { } st)
            return 0;
        ref var p = ref c.Parts[i];
        var cd = p.D;
        var seg = p.Seg > 0 && p.Seg <= c.JointCount ? SegMatrix(c, p.Seg, alpha) : Matrix4x4.Identity;
        var drawn = 0;
        for (var k = 0; k < st.SlotCount && k < cd.Slots.Length; k++)
        {
            if (ConstructParts.Get(st.Def[k]) is not { } d)
                continue;
            var shade = 0.55f + 0.45f * Math.Clamp(st.Health[k], 0, 1);
            if (cd.Slots[k].Rack && st.Count[k] > 1 && FarPile(in p, cd.Slots[k], d, st.Rot[k], st.Count[k], c, seg * body, shade, gfx))
            {
                drawn++;
                LastStoredCount++;
                continue;
            }
            var copies = ConstructStorage.DrawnCopies(cd.Slots[k], d, st.Rot[k], st.Count[k]);
            var turn = Rot24.Compose(p.Rot, st.Rot[k]);
            for (var n = 0; n < copies; n++)
                for (var b = 0; b < d.Boxes.Length; b++)
                {
                    ref readonly var box = ref d.Boxes[b];
                    if (box.Hidden)
                        continue;
                    ConstructStorage.ItemBox(p, cd.Slots[k], d, st.Rot[k], n, b, out var centre, out _);
                    if (box.IsGlass && QueueGlass(box.Model(turn, centre) * seg * body, box.Color * shade, box.Glass, box.Shape))
                        continue; // (a stored pane: see-through too)
                    gfx.Draw(box.Model(turn, centre) * seg * body, box.Color * shade, 1f, mode: 0, shape: box.Shape);
                    drawn++;
                }
            LastStoredCount++;
        }
        var table = RecyclingTable.Current;
        if (ConstructStorage.FillBox(c, i, st, out var fc, out var fh))
        {
            if (cd.IsVolumeStore)
                fh -= new Vector3(0.015f); // (the box ends on the player's walls: clear of their faces)
            drawn += DrawFill(gfx, fc, Vector3.Max(fh, new Vector3(0.01f)), st.Amount > 0 ? table.ColourOf(st.Material) : ConstructStoreVolume.PartsColour, seg * body);
        }
        if (ConstructStorage.InputBox(p, cd, st.InTotal, out var ic, out var ih))
            drawn += DrawFill(gfx, ic, ih, table.ColourOf(st.MixN > 0 ? st.MixMat![0] : st.InMaterial), seg * body); // (a production machine's input; a mixed one in its first material's colour)
        return drawn;
    }

    private const float PileFar = 30f; // metres: past this a shelf's pile is one box in its part's colour (48 lamps of 4 boxes are 192 draws)

    /// <summary>A shelf's pile seen from far off: one box. False: near enough to draw each copy.</summary>
    private static bool FarPile(in CPart p, in CSlotDef s, ConstructPartDef d, byte rot, int count, Construct c, in Matrix4x4 frame, float shade, Renderer gfx)
    {
        if (!Hud.Camera.Valid || Vector3.DistanceSquared(c.Pos, Hud.Camera.Eye) < PileFar * PileFar)
            return false;
        ConstructStorage.PileBox(p, s, d, rot, count, out var pc, out var ph);
        gfx.Draw(Matrix4x4.CreateScale(ph * 2) * Matrix4x4.CreateTranslation(pc) * frame, d.Boxes[0].Color * shade, 1f, mode: 0);
        return true;
    }

    private static int DrawFill(Renderer gfx, Vector3 fc, Vector3 fh, Vector3 colour, in Matrix4x4 frame)
    {
        gfx.Draw(Matrix4x4.CreateScale(fh * 2) * Matrix4x4.CreateTranslation(fc) * frame, colour, 1f, mode: 0);
        // A few lumps on top, so it reads as a heap.
        for (var l = 0; l < 3; l++)
        {
            var off = new Vector3((l - 1) * fh.X * 0.5f, fh.Y, (l == 1 ? -0.3f : 0.25f) * fh.Z);
            var size = new Vector3(fh.X * 0.35f, MathF.Min(0.06f, fh.Y), fh.Z * 0.35f);
            gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(fc + off) * frame, colour * (0.8f + 0.15f * l), 1f, mode: 0);
        }
        return 4;
    }

    private static int LastStoredCount;

    /// <summary>A segment's pose (rest frame -> body frame) blended between the last two ticks' joint values.</summary>
    public static Matrix4x4 SegMatrix(Construct c, int seg, float alpha)
    {
        SegPoseBlended(c, seg, alpha, out var q, out var t);
        return Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t);
    }

    /// <summary>
    /// Where a seat is drawn this frame, exactly as <see cref="Draw"/> places its boxes: the seat's turn in the world (its
    /// -Z the way the sitter faces, its +Y their up) and the sitter's feet (the seat's "at" point). A seated player is
    /// drawn, and the local camera put, from this, so they ride as part of the construct (no lag, no slide).
    /// </summary>
    public static void SeatPose(Construct c, int part, float alpha, bool blendTicks, out Quaternion rot, out Vector3 feet)
    {
        SeatPoseFrom(c, part, alpha, blendTicks, out rot, out var rel, out var pos);
        feet = pos + rel;
    }

    /// <summary>
    /// <see cref="SeatPose"/> in two halves: the construct's drawn position (<paramref name="pos"/>, as <see cref="Draw"/>
    /// places it) and the feet from there (<paramref name="rel"/>), so a camera far from the map's middle can sit on the
    /// render origin without rounding (Program.Render, Renderer.SetCameraAt).
    /// </summary>
    public static void SeatPoseFrom(Construct c, int part, float alpha, bool blendTicks, out Quaternion rot, out Vector3 rel, out Vector3 pos)
    {
        ref var p = ref c.Parts[part];
        pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
        var body = blendTicks ? Quaternion.Slerp(c.PrevRot, c.Rot, alpha) : c.Rot;
        var sq = Quaternion.Identity;
        var st = Vector3.Zero;
        if (p.Seg > 0 && p.Seg <= c.JointCount)
            SegPoseBlended(c, p.Seg, blendTicks ? alpha : 1f, out sq, out st);
        rot = Quaternion.Normalize(body * sq * Rot24.Quat(p.Rot));
        rel = Vector3.Transform(st + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, p.D.SeatAt), sq), body);
    }

    /// <summary>The steering wheel in front of a drive seat, from the sitter's feet in the seat's frame: the rim's centre (tilted back to face the driver).</summary>
    public static readonly Vector3 WheelAt = new(0, 1.12f, -0.5f);

    public const float WheelTilt = 0.55f, WheelRadius = 0.17f;

    /// <summary>Where the driver's hands hold the steering wheel (world), turned by the steering: right hand, left hand, and the rim's turn.</summary>
    public static void WheelGrips(Construct c, in Quaternion seat, Vector3 feet, out Vector3 right, out Vector3 left, out Quaternion rim)
    {
        rim = seat * Quaternion.CreateFromAxisAngle(Vector3.UnitX, WheelTilt) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -c.Steer * 2.5f);
        var centre = feet + Vector3.Transform(WheelAt, seat);
        right = centre + Vector3.Transform(new Vector3(WheelRadius, 0, 0), rim);
        left = centre + Vector3.Transform(new Vector3(-WheelRadius, 0, 0), rim);
    }

    /// <summary>The steering wheel of every drive seat (a rim of four bars, a hub and a column), turned by the steering.</summary>
    private static int DrawWheel(Renderer gfx, Construct c, float alpha, bool blendTicks)
    {
        if (c.DriveSeat < 0 || c.DriveSeat >= c.PartCount || c.Parts[c.DriveSeat].Broken || c.Parts[c.DriveSeat].D.RemoteReceiver)
            return 0;
        SeatPose(c, c.DriveSeat, alpha, blendTicks, out var seat, out var feet);
        WheelGrips(c, seat, feet, out var r, out var l, out var rim);
        var centre = (r + l) * 0.5f;
        var frame = Matrix4x4.CreateFromQuaternion(rim) * Matrix4x4.CreateTranslation(centre);
        var dark = new Vector3(0.12f, 0.12f, 0.13f);
        const float k = WheelRadius, t = 0.035f;
        gfx.Draw(Matrix4x4.CreateScale(t, k * 2 + t, t) * Matrix4x4.CreateTranslation(k, 0, 0) * frame, dark, 1f, mode: 0);
        gfx.Draw(Matrix4x4.CreateScale(t, k * 2 + t, t) * Matrix4x4.CreateTranslation(-k, 0, 0) * frame, dark, 1f, mode: 0);
        gfx.Draw(Matrix4x4.CreateScale(k * 2 + t, t, t) * Matrix4x4.CreateTranslation(0, k, 0) * frame, dark, 1f, mode: 0);
        gfx.Draw(Matrix4x4.CreateScale(k * 2 + t, t, t) * Matrix4x4.CreateTranslation(0, -k, 0) * frame, dark, 1f, mode: 0);
        gfx.Draw(Matrix4x4.CreateScale(k * 2, 0.03f, 0.02f) * frame, new Vector3(0.3f, 0.3f, 0.32f), 1f, mode: 0); // spoke
        // The column, from the hub down and forward to the seat's front.
        gfx.Draw(Matrix4x4.CreateScale(0.05f, 0.05f, 0.4f) * Matrix4x4.CreateTranslation(0, 0, -0.2f) * frame, dark, 1f, mode: 0);
        return 6;
    }

    /// <summary>The rings of a flexible joint between its two sides, each turned its share of the way (<paramref name="frame"/>: its base side's frame).</summary>
    private static int DrawFlexRings(Renderer gfx, Construct c, int i, int joint, float alpha, in Matrix4x4 frame, float shade)
    {
        ref var p = ref c.Parts[i];
        var d = p.D;
        const int rings = 3;
        SegPoseBlended(c, c.LeafSegOf(joint), alpha, out var q, out _);
        var turn = Quaternion.Identity;
        if (p.Seg > 0 && p.Seg <= c.JointCount)
        {
            SegPoseBlended(c, p.Seg, alpha, out var pq, out _);
            turn = Quaternion.Conjugate(pq);
        }
        turn = Quaternion.Normalize(turn * q);
        var pivot = p.Pos + Rot24.Apply(p.Rot, d.JointPivot);
        var full = Rot24.ApplyHalf(p.Rot, (d.BoundsMax - d.BoundsMin) * 0.5f) * 2;
        var along = ConstructParts.DirVector(Rot24.ApplyDir(p.Rot, d.JointAxis));
        var ax = Vector3.Abs(along);
        var span = Vector3.Dot(full, ax);
        var size = full * 0.86f * (Vector3.One - ax) + ax * (span * 0.1f);
        var colour = (d.FlexTwist > 0 ? new Vector3(0.55f, 0.58f, 0.6f) : new Vector3(0.15f, 0.16f, 0.17f)) * shade; // (a ball joint's steel, a rubber joint's black)
        for (var k = 0; k < rings; k++)
        {
            var t = (k + 1f) / (rings + 1);
            var ring = Quaternion.Slerp(Quaternion.Identity, turn, t);
            var at = pivot + Vector3.Transform(along * span * (t * 0.44f - 0.22f), ring);
            gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateFromQuaternion(ring) * Matrix4x4.CreateTranslation(at) * frame, colour, 1f, mode: 0);
        }
        return rings;
    }

    /// <summary>A segment's pose (rest frame -> body frame) with every joint up its chain blended between its last two ticks' values.</summary>
    public static void SegPoseBlended(Construct c, int seg, float alpha, out Quaternion q, out Vector3 t)
    {
        // Every joint up the chain at its blended value (a turntable that wrapped round blends the short way).
        Span<float> keep = stackalloc float[Construct.MaxJoints];
        for (var j = 0; j < c.JointCount; j++)
        {
            ref var jt = ref c.Joints[j];
            keep[j] = jt.Angle;
            var from = jt.PrevAngle;
            if (!jt.Slides && MathF.Abs(jt.Angle - from) > MathF.PI)
                from += MathF.Sign(jt.Angle - from) * MathF.Tau;
            jt.Angle = float.Lerp(from, jt.Angle, alpha);
        }
        c.SegPose(seg, out q, out t);
        for (var j = 0; j < c.JointCount; j++)
            c.Joints[j].Angle = keep[j];
    }

    /// <summary>A part's centre in the world (its segment as it is now).</summary>
    public static Vector3 PartCentre(Construct c, int i)
    {
        c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
        c.PartBounds(i, out var mn, out var mx);
        return t + Vector3.Transform((mn + mx) * 0.5f, q);
    }

    /// <summary>The wires, while the linking tool is out (unlit lines between the parts' centres: lit when the signal is on), and a wire in progress.</summary>
    public static void DrawWires(Renderer gfx, ConstructWorld world)
    {
        LastWires = 0;
        ConnectorRender.DrawOverlay(gfx, world); // (a rope or spring being tied, the aimed one: with the configure tool and the remover too)
        if (!ShowWires)
        {
            if (RemoverSel is { LinkCount: > 0 } marked)
                RouteRender.DrawSelected(gfx, marked, 1f, false); // (the remover's marked links, with the wires otherwise hidden)
            return;
        }
        RouteRender.DrawOverlay(gfx, world, 1f, false); // (routed links: the one being routed, the aimed one, highlight all)
        WorldLinkRender.DrawOverlay(gfx, world); // (a world link being routed, the aimed one)
        gfx.SetDepthTest(false); // (seen through the parts: the wires run inside the construct)
        foreach (var c in world.Bodies)
        {
            if (c.WireCount == 0)
                continue;
            if (Vector3.DistanceSquared(c.WorldCom, WireEye) > (25f + c.Radius) * (25f + c.Radius))
                continue;
            // The data links' signals: a small block at each input, lit while the signal is on (the cables themselves are drawn as links).
            for (var k = 0; k < c.WireCount; k++)
            {
                ref var w = ref c.Wires[k];
                if (w.From >= c.PartCount || w.To >= c.PartCount)
                    continue;
                var on = c.Parts[w.From].Out >= 0.5f;
                gfx.Draw(Matrix4x4.CreateScale(0.07f) * Matrix4x4.CreateTranslation(PartCentre(c, w.To)), on ? new Vector3(0.4f, 1f, 0.45f) : new Vector3(1f, 0.8f, 0.3f), 0.9f, mode: 2);
                LastWires++;
            }
        }
        // Wires between constructs (milestone 3): the same, in orange while off.
        for (var k = 0; k < world.XWireCount; k++)
        {
            ref var x = ref world.XWires[k];
            if (world.Get(x.BodyFrom) is not { } cf || world.Get(x.BodyTo) is not { } ct || x.From >= cf.PartCount || x.To >= ct.PartCount)
                continue;
            var a = PartCentre(cf, x.From);
            var b = PartCentre(ct, x.To);
            if (Vector3.DistanceSquared(a, WireEye) > 900f)
                continue;
            Line(gfx, a, b, cf.Parts[x.From].Out >= 0.5f ? new Vector3(0.4f, 1f, 0.45f) : new Vector3(1f, 0.55f, 0.2f), 0.9f);
            gfx.Draw(Matrix4x4.CreateScale(0.07f) * Matrix4x4.CreateTranslation(b), new Vector3(1f, 0.8f, 0.3f), 0.9f, mode: 2);
            LastWires++;
        }
        gfx.SetDepthTest(true);
    }

    /// <summary>A thin unlit box from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static void Line(Renderer gfx, Vector3 a, Vector3 b, Vector3 colour, float opacity)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 1e-3f)
            return;
        var z = d / len;
        var x = MathF.Abs(z.Y) < 0.95f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z)) : Vector3.UnitX;
        var y = Vector3.Cross(z, x);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        gfx.Draw(Matrix4x4.CreateScale(0.035f, 0.035f, len) * basis * Matrix4x4.CreateTranslation((a + b) * 0.5f), colour, opacity, mode: 2);
    }

    /// <summary>The ghosts found this frame for <paramref name="c"/> (the input sets them; the draw checks them against it).</summary>
    public static void SetGhosts(Construct c, int count)
    {
        GhostBody = c;
        GhostParts = c.PartCount;
        GhostCount = Math.Clamp(count, 0, Ghosts.Length);
    }

    /// <summary>
    /// Ghost <paramref name="k"/> still names a part and socket of <paramref name="c"/> as it is now. The input finds the
    /// ghosts before the frame's tick or packets, which may take a part off the same construct (board item rc01: taking a
    /// part off while holding one crashed here, the stale part and socket indices running off the arrays); stale ghosts
    /// are skipped for that frame.
    /// </summary>
    public static bool GhostLive(Construct c, int k)
    {
        if (k < 0 || k >= GhostCount || c.PartCount != GhostParts || c.PartCount == 0)
            return false;
        ref var g = ref Ghosts[k];
        return g.Part >= 0 && g.Part < c.PartCount && ConstructParts.Get(c.Parts[g.Part].Def) is { } pd && g.Socket >= 0 && (g.Socket < pd.Sockets.Length || c.MergeLead(g.Part)); // (a merged receiver's centre sockets come after its own)
    }

    private static bool _ghostThrew;

    /// <summary>The ghost sockets (blended, depth-tested, no depth write): small markers on every free socket the held part fits, the chosen one as the whole part.</summary>
    /// <summary>The Sandbox's spawn placement: a ghost circle where the spawn would stand, green when it can, red when not (GameSandboxSpawns.cs).</summary>
    public static void DrawSpawnGhost(Renderer gfx, Game game, double now)
    {
        if (!game.SpawnPlacing || !game.SpawnGhostShown)
            return;
        var colour = game.SpawnGhostOk ? new Vector3(0.35f, 1f, 0.5f) : new Vector3(1f, 0.35f, 0.3f);
        var radius = game.SpawnPick == SbSpawnKind.Zombies ? MathF.Min(12f, MathF.Max(1f, MathF.Sqrt(game.SpawnZombies) * 0.45f)) : 0.8f + 0.3f * MathF.Min(game.SpawnBots, 8);
        var pulse = 0.55f + 0.25f * MathF.Sin((float)now * 6f);
        const int n = 24;
        for (var k = 0; k < n; k++)
        {
            var a = k * MathF.Tau / n;
            var at = game.SpawnGhost + new Vector3(MathF.Sin(a) * radius, 0.05f, MathF.Cos(a) * radius);
            gfx.Draw(Matrix4x4.CreateScale(0.12f + radius * 0.2f, 0.04f, 0.08f) * Matrix4x4.CreateRotationY(a) * Matrix4x4.CreateTranslation(at), colour, 0.8f * pulse, mode: 2);
        }
        gfx.Draw(Matrix4x4.CreateScale(0.12f, 1.8f, 0.12f) * Matrix4x4.CreateTranslation(game.SpawnGhost + new Vector3(0, 0.9f, 0)), colour, 0.35f * pulse, mode: 2);
    }

    public static void DrawGhosts(Renderer gfx, double now)
    {
        try
        {
            DrawGhostsUnsafe(gfx, now);
        }
        catch (Exception ex)
        {
            // Never a crash over a preview: logged once, and the aim state cleared for the frame.
            if (!_ghostThrew)
                Console.WriteLine($"[sandbox] error: drawing the ghosts threw (cleared): {ex}");
            _ghostThrew = true;
            ClearAim();
        }
    }

    // ---- The build aids' ghosts (a mirror image, a line's run), the mirror plane and the gantry's grid: poses, not indices ----

    public const int MaxAidGhosts = 240;
    public static readonly AidGhost[] AidGhosts = new AidGhost[MaxAidGhosts];
    private static Construct? AidGhostBody, PlaneBody, GridBody;
    private static int AidCount, AidSeg, PlaneAx;
    private static float PlaneW, GridY;
    private static Vector3 GridAt;

    /// <summary>This frame, the first <paramref name="count"/> of <see cref="AidGhosts"/> show on a construct.</summary>
    public static void ShowAidGhosts(Construct c, int seg, int count)
    {
        AidGhostBody = c;
        AidSeg = seg;
        AidCount = Math.Min(count, MaxAidGhosts);
    }

    /// <summary>This frame, the mirror plane shows.</summary>
    public static void ShowPlane(Construct c, int ax, float w)
    {
        PlaneBody = c;
        PlaneAx = ax;
        PlaneW = w;
    }

    /// <summary>This frame, the gantry's build plane shows at <paramref name="y"/>, round <paramref name="at"/> (rest frame).</summary>
    public static void ShowGrid(Construct c, float y, Vector3 at)
    {
        GridBody = c;
        GridY = y;
        GridAt = at;
    }

    private static Construct? BalanceBody;
    public static Vector3 BalanceCom, BalanceLift, BalanceThrust, BalancePush;
    public static bool BalanceHasLift, BalanceHasThrust;

    /// <summary>This frame, a flyer's balance markers show (rest frame numbers above; the labels are Program.SandboxFlight.cs's).</summary>
    public static void ShowBalance(Construct c) => BalanceBody = c;

    private static void DrawBalance(Renderer gfx, Construct c)
    {
        var body = Matrix4x4.CreateFromQuaternion(c.Rot) * Matrix4x4.CreateTranslation(c.Pos);
        var r = c.Radius + 0.5f; // (the bars come out of the hull: a marker inside it is still seen)
        var thin = 0.02f + 0.004f * c.Radius;
        var size = 0.14f + 0.03f * c.Radius;
        var mass = new Vector3(1f, 0.85f, 0.2f);
        var lift = new Vector3(0.3f, 0.75f, 1f);
        var thrust = new Vector3(1f, 0.45f, 0.15f);
        gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(BalanceCom) * body, mass, 0.9f, mode: 2);
        gfx.Draw(Matrix4x4.CreateScale(2 * r, thin, thin) * Matrix4x4.CreateTranslation(BalanceCom) * body, mass, 0.6f, mode: 2);
        gfx.Draw(Matrix4x4.CreateScale(thin, 2 * r, thin) * Matrix4x4.CreateTranslation(BalanceCom) * body, mass, 0.6f, mode: 2);
        gfx.Draw(Matrix4x4.CreateScale(thin, thin, 2 * r) * Matrix4x4.CreateTranslation(BalanceCom) * body, mass, 0.6f, mode: 2);
        if (BalanceHasLift)
        {
            gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(BalanceLift) * body, lift, 0.9f, mode: 2);
            gfx.Draw(Matrix4x4.CreateScale(thin, 2 * r, thin) * Matrix4x4.CreateTranslation(BalanceLift) * body, lift, 0.6f, mode: 2);
        }
        if (BalanceHasThrust)
        {
            gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(BalanceThrust) * body, thrust, 0.9f, mode: 2);
            gfx.Draw(Matrix4x4.CreateScale(thin, thin, r) * Basis(BalancePush) * Matrix4x4.CreateTranslation(BalanceThrust + BalancePush * (0.5f * r)) * body, thrust, 0.6f, mode: 2);
        }
    }

    private static void DrawAidGhosts(Renderer gfx, float pulse)
    {
        if (BalanceBody is { } bc)
            DrawBalance(gfx, bc);
        if (AidGhostBody is { } c)
        {
            var seg = AidSeg <= c.JointCount ? AidSeg : 0;
            c.SegPose(seg, out var q, out var t);
            var m = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t) * Matrix4x4.CreateFromQuaternion(c.Rot) * Matrix4x4.CreateTranslation(c.Pos);
            for (var k = 0; k < AidCount; k++)
            {
                ref var g = ref AidGhosts[k];
                var def = g.Def;
                for (var b = 0; b < def.Boxes.Length; b++)
                {
                    ref readonly var box = ref def.Boxes[b];
                    if (box.Hidden || box.Detail && AidCount > 16)
                        continue;
                    gfx.Draw(Matrix4x4.CreateScale(1.01f) * box.Model(g.Rot, g.Pos + Rot24.Apply(g.Rot, box.Center)) * m, GhostBoxColour(in box, g.Ok), 0.3f * pulse * GhostGlass(box), mode: 2, shape: box.Shape);
                }
            }
        }
        if (PlaneBody is { } pc)
        {
            var body = Matrix4x4.CreateFromQuaternion(pc.Rot) * Matrix4x4.CreateTranslation(pc.Pos);
            var mid = (pc.LocalMin + pc.LocalMax) * 0.5f;
            var size = pc.LocalMax - pc.LocalMin + new Vector3(2f);
            mid[PlaneAx] = PlaneW;
            size[PlaneAx] = 0.02f;
            gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(mid) * body, new Vector3(0.3f, 0.8f, 1f), 0.1f, mode: 2);
        }
        if (GridBody is { } gc)
        {
            var body = Matrix4x4.CreateFromQuaternion(gc.Rot) * Matrix4x4.CreateTranslation(gc.Pos);
            const int half = 12;
            const float step = 1f;
            float cx = MathF.Round(GridAt.X), cz = MathF.Round(GridAt.Z), len = half * 2 * step;
            for (var k = -half; k <= half; k++)
            {
                gfx.Draw(Matrix4x4.CreateScale(0.02f, 0.02f, len) * Matrix4x4.CreateTranslation(cx + k * step, GridY, cz) * body, new Vector3(0.55f, 0.9f, 1f), 0.35f, mode: 2);
                gfx.Draw(Matrix4x4.CreateScale(len, 0.02f, 0.02f) * Matrix4x4.CreateTranslation(cx, GridY, cz + k * step) * body, new Vector3(0.55f, 0.9f, 1f), 0.35f, mode: 2);
            }
        }
    }

    private static void DrawGhostsUnsafe(Renderer gfx, double now)
    {
        var pulse = 0.55f + 0.25f * MathF.Sin((float)now * 6f);
        DrawAidGhosts(gfx, pulse);
        if (GhostBody is { } c && GhostDef is { } def)
        {
            var body = Matrix4x4.CreateFromQuaternion(c.Rot) * Matrix4x4.CreateTranslation(c.Pos);
            for (var k = 0; k < GhostCount; k++)
            {
                if (!GhostLive(c, k))
                    continue;
                ref var g = ref Ghosts[k];
                c.SocketAt(g.Part, g.Socket, out var at, out var dir);
                c.SegPose(c.Parts[g.Part].Seg, out var q, out var t);
                var seg = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t);
                var colour = k == GhostSelected ? new Vector3(0.3f, 1f, 0.45f) : new Vector3(0.35f, 0.75f, 1f);
                gfx.Draw(Matrix4x4.CreateScale(0.14f) * Matrix4x4.CreateTranslation(at + ConstructParts.DirVector(dir) * 0.04f) * seg * body, colour, k == GhostSelected ? 0.9f : 0.55f, mode: 2);
                if (k != GhostSelected)
                    continue;
                // The whole part where it would go.
                for (var b = 0; b < def.Boxes.Length; b++)
                {
                    ref readonly var box = ref def.Boxes[b];
                    if (box.Hidden)
                        continue;
                    var gs = g.Scale > 1 ? g.Scale : 1f; // (a gun part going on a merged receiver: N times bigger)
                    var bc = g.Pos + Rot24.Apply(g.Rot, box.Center * gs);
                    gfx.Draw(Matrix4x4.CreateScale(1.01f * gs) * box.Model(g.Rot, bc) * Matrix4x4.CreateTranslation(GhostEase) * seg * body, GhostBoxColour(in box, GhostValid), 0.35f * pulse * GhostGlass(box), mode: 2, shape: box.Shape);
                }
                Gizmo(gfx, def, g.Pos, g.Rot, seg * body);
            }
        }
        if (SlotBody is { } sbody && SlotDef is { } sdef && SlotPart >= 0 && SlotPart < sbody.PartCount)
        {
            // The slots the held part fits, as ghost outlines; the chosen one with the part where it would sit.
            ref var cp = ref sbody.Parts[SlotPart];
            var cd = cp.D;
            sbody.SegPose(cp.Seg, out var q, out var t);
            var m = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t) * Matrix4x4.CreateFromQuaternion(sbody.Rot) * Matrix4x4.CreateTranslation(sbody.Pos);
            for (var k = 0; k < cd.Slots.Length; k++)
            {
                if ((SlotMask & (1 << k)) == 0)
                    continue;
                ConstructStorage.SlotBox(cp, cd.Slots[k], out var sc, out var sh);
                var chosen = k == SlotSelected;
                var colour = chosen ? new Vector3(0.3f, 1f, 0.45f) : new Vector3(0.35f, 0.75f, 1f);
                Outline(gfx, sc, sh, m, colour, chosen ? 0.9f : 0.6f);
                gfx.Draw(Matrix4x4.CreateScale(sh * 2) * Matrix4x4.CreateTranslation(sc) * m, colour, (chosen ? 0.16f : 0.08f) * pulse * 1.6f, mode: 2);
                if (!chosen)
                    continue;
                var turn = Rot24.Compose(cp.Rot, SlotRot);
                for (var b = 0; b < sdef.Boxes.Length; b++)
                {
                    if (sdef.Boxes[b].Hidden)
                        continue;
                    ConstructStorage.ItemBox(cp, cd.Slots[k], sdef, SlotRot, SlotCopy, b, out var bc, out _);
                    gfx.Draw(Matrix4x4.CreateScale(1.01f) * sdef.Boxes[b].Model(turn, bc) * m, new Vector3(0.35f, 1f, 0.5f), 0.35f * pulse * GhostGlass(sdef.Boxes[b]), mode: 2, shape: sdef.Boxes[b].Shape);
                }
            }
        }
        if (PlaceAt is { } ground && GhostDef is { } nd)
        {
            var body = Matrix4x4.CreateFromQuaternion(Rot24.Quat(PlaceTurn)) * Matrix4x4.CreateFromQuaternion(PlaceRot) * Matrix4x4.CreateTranslation(ground);
            var colour = PlaceValid ? new Vector3(0.35f, 1f, 0.5f) : new Vector3(1f, 0.35f, 0.3f);
            for (var b = 0; b < nd.Boxes.Length; b++)
                if (!nd.Boxes[b].Hidden)
                    gfx.Draw(Matrix4x4.CreateScale(1.01f) * nd.Boxes[b].Model(0, nd.Boxes[b].Center) * body, nd.Boxes[b].Side != 0 ? GhostBoxColour(in nd.Boxes[b], PlaceValid) : colour, 0.3f * pulse * GhostGlass(nd.Boxes[b]), mode: 2, shape: nd.Boxes[b].Shape);
            Gizmo(gfx, nd, Vector3.Zero, PlaceTurn, Matrix4x4.CreateFromQuaternion(PlaceRot) * Matrix4x4.CreateTranslation(ground));
        }
        if (WeldBodyA is { } wa && WeldPartA >= 0 && WeldPartA < wa.PartCount)
        {
            var b = WeldBodyB is { } wb && WeldPartB >= 0 && WeldPartB < wb.PartCount ? wb : null;
            var colour = b == null ? new Vector3(1f, 0.75f, 0.2f) : WeldValid ? new Vector3(0.35f, 1f, 0.5f) : new Vector3(1f, 0.35f, 0.3f);
            Outline(gfx, wa, WeldPartA, colour, 0.3f * pulse);
            if (b != null)
            {
                Outline(gfx, b, WeldPartB, colour, 0.3f * pulse);
                Line(gfx, WeldOnA, WeldOnB, new Vector3(1f, 0.65f, 0.2f), 0.95f);
                gfx.Draw(Matrix4x4.CreateScale(0.07f) * Matrix4x4.CreateTranslation((WeldOnA + WeldOnB) * 0.5f), WeldValid ? new Vector3(1f, 0.7f, 0.25f) : colour, 0.9f * pulse, mode: 2);
            }
        }
        if (RemoverSel is { Count: > 0 } sel)
        {
            // The remover's marks: every selected part outlined red (its links are drawn by RouteRender.DrawSelected).
            var red = new Vector3(1f, 0.22f, 0.18f);
            for (var i = 0; i < sel.Count; i++)
            {
                var it = sel[i];
                if (!it.Route && it.Index < it.Ref.PartCount && it.Ref.Parts[it.Index].Def == it.Def)
                    Outline(gfx, it.Ref, it.Index, red, 0.22f + 0.12f * pulse);
            }
        }
        DrawSelection(gfx, pulse);
        if (AimBody is { } ab && AimPart >= 0 && AimPart < ab.PartCount)
        {
            if ((AimRefused || AimHands) && !AimOnPage)
                DrawAimGlow(gfx, ab, AimPart);
            if (JointSides.IsJoint(ab, AimPart, out var aj))
                DrawJointSides(gfx, ab, aj, now, pulse);
        }
        else
            _jsPart = -1;
    }

    /// <summary>
    /// The aim's glow (user, 2026-11-04): yellow on the part of it hands work, red round what a tool would be refused on. The
    /// boxes marked "use" (an Analog Lever's handle follows its value), a stored part's slot, a machine's or a bin's open
    /// region; a panel hung on a hinge, its door handle or a hand's width at its free edge (<see cref="ConstructDoor"/>); a part
    /// with none of those, or something loose (all of it comes up in the hands), glows whole.
    /// </summary>
    private static void DrawAimGlow(Renderer gfx, Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        c.WorldFrame(p.Seg, out var q, out var t);
        var frame = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t);
        var colour = AimRefused ? new Vector3(1f, 0.3f, 0.25f) : new Vector3(1f, 0.85f, 0.3f);
        var opacity = AimRefused ? 0.24f : 0.18f;
        var pad = new Vector3(0.02f);
        if (AimHands)
        {
            const float small = 0.3f; // (a cap or a handle: a thin glow would not show)
            if (AimSlot >= 0 && AimSlot < d.Slots.Length)
            {
                ConstructStorage.SlotBox(p, d.Slots[AimSlot], out var sc, out var sh);
                gfx.Draw(Matrix4x4.CreateScale((sh + pad) * 2) * Matrix4x4.CreateTranslation(sc) * frame, colour, small, mode: 2);
                return;
            }
            if (!c.Loose && d.UseBoxes.Length > 0)
            {
                DrawUseBoxes(gfx, ref p, frame, colour, small, pad);
                return;
            }
            if (!c.Loose && ConstructDoor.Swings(c, part))
            {
                // A panel hung on a hinge (no handle box of its own): the handle fitted to the door, or a hand's width at its free edge.
                if (ConstructDoor.HandleOn(c, part) is var handle and >= 0)
                {
                    ref var hp = ref c.Parts[handle];
                    c.WorldFrame(hp.Seg, out var hq, out var ht);
                    DrawUseBoxes(gfx, ref hp, Matrix4x4.CreateFromQuaternion(hq) * Matrix4x4.CreateTranslation(ht), colour, small, pad);
                }
                else
                {
                    ConstructDoor.UseArea(c, part, out var ua, out var uh);
                    gfx.Draw(Matrix4x4.CreateScale((uh + pad) * 2) * Matrix4x4.CreateTranslation(ua) * frame, colour, small, mode: 2);
                }
                return;
            }
            var input = d.IsAssembler || d.IsRefiner;
            var bin = d.IsBin && !d.IsVolumeStore;
            if (!c.Loose && (input || bin))
            {
                for (var k = 0; k < 2; k++)
                {
                    if (k == 0 ? !input : !bin)
                        continue;
                    var (lo, hi) = k == 0 ? (d.InFillMin, d.InFillMax) : (d.FillMin, d.FillMax); // (a machine's hopper, and its tray or a bin's inside)
                    var mid = p.Pos + Rot24.Apply(p.Rot, (lo + hi) * 0.5f);
                    var size = Rot24.ApplyHalf(p.Rot, (hi - lo) * 0.5f);
                    gfx.Draw(Matrix4x4.CreateScale((size + pad) * 2) * Matrix4x4.CreateTranslation(mid) * frame, colour, small, mode: 2);
                }
                return;
            }
        }
        c.ShapeBounds(part, out var mn, out var mx); // (a merged receiver: round the whole merged box)
        gfx.Draw(Matrix4x4.CreateScale(mx - mn + pad * 2) * Matrix4x4.CreateTranslation((mn + mx) * 0.5f) * frame, colour, opacity, mode: 2);
    }

    /// <summary>The glow on a part's "use" boxes (an Analog Lever's handle follows its value).</summary>
    private static void DrawUseBoxes(Renderer gfx, ref CPart p, in Matrix4x4 frame, Vector3 colour, float opacity, Vector3 pad)
    {
        var d = p.D;
        for (var k = 0; k < d.UseBoxes.Length; k++)
        {
            if (AimKeyBox >= 0 && d.UseBoxes[k] != AimKeyBox)
                continue; // (one key of a keypad)
            ref readonly var box = ref d.Boxes[d.UseBoxes[k]];
            var half = Rot24.ApplyHalf(p.Rot, box.Half);
            var at = p.Pos + Rot24.Apply(p.Rot, box.Center);
            var m = box.Model(p.Rot, Vector3.Zero) * Matrix4x4.CreateScale((half + pad) / half) * Matrix4x4.CreateTranslation(at);
            if (box.Rig == BoxRig.Lever)
            {
                var pivot = p.Pos + Rot24.Apply(p.Rot, box.RigPivot);
                m = m * Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateFromAxisAngle(Rot24.Apply(p.Rot, Vector3.UnitX), AnalogLever.Angle(p.LeverShown)) * Matrix4x4.CreateTranslation(pivot);
            }
            gfx.Draw(m * frame, colour, opacity, mode: 2, shape: box.Shape);
        }
    }

    // The aimed joint's sides (board item js01): the highlight shows for a moment after the crosshair settles on a joint part.
    private static Construct? _jsBody;
    private static int _jsPart = -1;
    private static double _jsSince;
    /// <summary>Seconds the two groups stay highlighted after the crosshair lands on a joint part.</summary>
    public const double JointHighlightSeconds = 2.5;
    /// <summary>The configure tool is out (set by the input each frame): the aimed joint swings or slides through its range as a ghost.</summary>
    public static bool AimMotion;
    /// <summary>Most parts outlined on each side (a big machine outlines its first parts only).</summary>
    public const int MaxSideOutlines = 48;
    public static int SideOutlines { get; private set; } // (the last frame; the self-test reads it)
    public static int MotionBoxes { get; private set; }

    /// <summary>
    /// The aimed joint part's two groups outlined for a moment, in two colours: the base side (steel blue: the rest of the construct) and the
    /// moving side (orange: the parts on its segment and every segment hanging from it). With the configure tool out, the moving side also
    /// swings (or slides) through the joint's range as a pale ghost. Nothing allocates.
    /// </summary>
    private static void DrawJointSides(Renderer gfx, Construct c, int joint, double now, float pulse)
    {
        SideOutlines = MotionBoxes = 0;
        var part = c.Joints[joint].Hinge;
        if (!ReferenceEquals(c, _jsBody) || part != _jsPart)
        {
            _jsBody = c;
            _jsPart = part;
            _jsSince = now;
        }
        var age = now - _jsSince;
        if (age < JointHighlightSeconds)
        {
            var fade = (float)Math.Min(1.0, (JointHighlightSeconds - age) / 0.6);
            int nb = 0, nl = 0;
            for (var i = 0; i < c.PartCount; i++)
            {
                var side = JointSides.SideOf(c, joint, i);
                if (side == 0 || (side == JointSides.Leaf ? nl : nb) >= MaxSideOutlines)
                    continue;
                if (side == JointSides.Leaf)
                    nl++;
                else
                    nb++;
                Outline(gfx, c, i, side == JointSides.Leaf ? JointSides.LeafColour : JointSides.BaseColour, (0.16f + 0.1f * pulse) * fade);
            }
            SideOutlines = nb + nl;
        }
        if (!AimMotion)
            return;
        // The motion preview: the moving side where the joint would be, sweeping between its limits (a free turntable: a quarter turn each way).
        ref var j = ref c.Joints[joint];
        float lo = j.Min, hi = j.Max;
        if (!j.Slides && !c.Parts[part].D.JointLimited)
        {
            lo = j.Angle - MathF.PI / 2;
            hi = j.Angle + MathF.PI / 2;
        }
        if (!(hi > lo))
            return;
        var target = lo + (hi - lo) * (0.5f + 0.5f * MathF.Sin((float)now * 1.6f));
        c.WorldFrame(j.ParentSeg, out var pq, out var pt);
        var axis = Vector3.Normalize(Vector3.Transform(j.Axis, pq));
        var pivot = pt + Vector3.Transform(j.Pivot, pq);
        var delta = target - j.Angle;
        var move = j.Slides
            ? Matrix4x4.CreateTranslation(axis * delta)
            : Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateFromAxisAngle(axis, delta) * Matrix4x4.CreateTranslation(pivot);
        var drawn = 0;
        for (var i = 0; i < c.PartCount && drawn < MaxSideOutlines; i++)
        {
            if (JointSides.SideOf(c, joint, i) != JointSides.Leaf)
                continue;
            c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
            c.ShapeBounds(i, out var mn, out var mx);
            gfx.Draw(Matrix4x4.CreateScale(mx - mn) * Matrix4x4.CreateTranslation((mn + mx) * 0.5f) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t) * move, new Vector3(0.45f, 0.9f, 1f), 0.2f, mode: 2);
            drawn++;
        }
        MotionBoxes = drawn;
    }

    /// <summary>A part's bounds drawn as a see-through box (the freeform weld's outlines).</summary>
    private static void Outline(Renderer gfx, Construct c, int part, Vector3 colour, float opacity)
    {
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        c.ShapeBounds(part, out var mn, out var mx);
        var m = Matrix4x4.CreateScale(mx - mn + new Vector3(0.05f)) * Matrix4x4.CreateTranslation((mn + mx) * 0.5f) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t);
        gfx.Draw(m, colour, opacity, mode: 2);
    }

    /// <summary>
    /// The ghost's orientation gizmo (board item dev-sandbox-arrow-rotate-ar02): from the part's middle, its front (-z) as
    /// an orange arrow with a head, its top (+y) green and its right (+x) red, each poking out of the part, so the rotate
    /// keys' effect reads at a glance. <paramref name="pos"/> and <paramref name="turn"/> place the part in the frame <paramref name="frame"/>.
    /// </summary>
    private static void Gizmo(Renderer gfx, ConstructPartDef def, Vector3 pos, byte turn, in Matrix4x4 frame)
    {
        var mid = pos + Rot24.Apply(turn, (def.BoundsMin + def.BoundsMax) * 0.5f);
        var half = Rot24.ApplyHalf(turn, (def.BoundsMax - def.BoundsMin) * 0.5f);
        var centre = Vector3.Transform(mid, frame);
        for (var k = 0; k < 3; k++)
        {
            var local = k == 0 ? -Vector3.UnitZ : k == 1 ? Vector3.UnitY : Vector3.UnitX;
            var d = Rot24.Apply(turn, local); // (an axis of the frame, signed)
            var reach = Vector3.Dot(Vector3.Abs(d), half) + (k == 0 ? 0.22f : 0.12f);
            var tip = Vector3.Transform(mid + d * reach, frame);
            var colour = k == 0 ? new Vector3(1f, 0.6f, 0.15f) : k == 1 ? new Vector3(0.35f, 1f, 0.4f) : new Vector3(1f, 0.3f, 0.3f);
            Line(gfx, centre, tip, colour, 0.9f);
            if (k == 0)
                gfx.Draw(Matrix4x4.CreateScale(0.09f) * Matrix4x4.CreateTranslation(tip), colour, 0.95f, mode: 2);
        }
    }

    /// <summary>
    /// A ghost box's colour: green when the part fits, red when not; a joint part's moving side (board item js01) in orange (dark red
    /// when it doesn't fit), so the ghost shows which side will mount where before it is placed.
    /// </summary>
    public static Vector3 GhostBoxColour(in CPartBox box, bool valid) => box.IsLeaf
        ? valid ? JointSides.LeafColour : new Vector3(0.75f, 0.12f, 0.08f)
        : valid ? new Vector3(0.35f, 1f, 0.5f) : new Vector3(1f, 0.35f, 0.3f);

    /// <summary>A ghost's glass pane is fainter than the rest of it (the frame round it reads, the pane shows through).</summary>
    public static float GhostGlass(in CPartBox box) => box.IsGlass ? 0.45f : 1f;

    // ---- Glass (the blended pass) -------------------------------------------------------------------------------------

    /// <summary>The most glass boxes drawn see-through in a frame (more are drawn opaque: a few hundred panes is plenty).</summary>
    public const int MaxGlass = 512;

    private struct GlassBox
    {
        public Matrix4x4 Model;
        public Vector3 Color;
        public float Alpha;
        public ushort Shape;
    }

    private static readonly GlassBox[] _glass = new GlassBox[MaxGlass];
    private static readonly float[] _glassKey = new float[MaxGlass];
    private static readonly int[] _glassOrder = new int[MaxGlass];

    /// <summary>Glass boxes <see cref="Draw"/> kept for <see cref="DrawGlass"/> this frame.</summary>
    public static int GlassCount { get; private set; }

    /// <summary>Glass boxes (and their edge highlights) the last <see cref="DrawGlass"/> drew (the self-test).</summary>
    public static int LastGlass { get; private set; }

    /// <summary>Keeps a glass box for the blended pass; false when the list is full (then it is drawn opaque).</summary>
    public static bool QueueGlass(in Matrix4x4 model, Vector3 colour, float alpha, ushort shape)
    {
        if (GlassCount >= MaxGlass)
            return false;
        _glass[GlassCount++] = new GlassBox { Model = model, Color = colour, Alpha = Math.Clamp(alpha, 0.05f, 0.95f), Shape = shape };
        return true;
    }

    /// <summary>The highlight along a pane's edges: its width (m) and how much lighter than the pane it is.</summary>
    private const float EdgeWidth = 0.012f;

    private static readonly Vector3 GlassSheen = new(0.86f, 0.92f, 0.97f);

    /// <summary>
    /// The glass <see cref="Draw"/> kept: call it in the blended pass (it turns blending on and depth writes off, and leaves
    /// them so), after everything opaque, so what is behind a pane (a player in the cab, the far side of the yard) shows
    /// through it. Farthest first (sorted by the distance from <paramref name="eye"/>), so panes behind panes blend right.
    /// Each pane is lit and fogged by the box shader (mode 0, both GL paths); it reflects a little more and takes a sheen
    /// at grazing angles, and a thin lighter strip runs along each long edge so it still reads as glass edge-on.
    /// </summary>
    public static void DrawGlass(Renderer gfx, Vector3 eye)
    {
        var n = GlassCount;
        LastGlass = 0;
        if (n == 0)
            return;
        gfx.SetBlend(true);
        gfx.SetDepthWrite(false);
        eye -= gfx.Origin; // (the panes were kept relative to the render origin, as their bodies were drawn)
        gfx.Relative = true;
        for (var k = 0; k < n; k++)
        {
            _glassOrder[k] = k;
            _glassKey[k] = -Vector3.DistanceSquared(_glass[k].Model.Translation, eye); // (farthest first)
        }
        Array.Sort(_glassKey, _glassOrder, 0, n);
        var drawn = 0;
        for (var k = 0; k < n; k++)
        {
            ref readonly var g = ref _glass[_glassOrder[k]];
            var m = g.Model;
            var centre = m.Translation;
            // The pane's thinnest axis is its normal (the model's rows are its scaled axes).
            Span<Vector3> axes = [new(m.M11, m.M12, m.M13), new(m.M21, m.M22, m.M23), new(m.M31, m.M32, m.M33)];
            var size = new Vector3(axes[0].Length(), axes[1].Length(), axes[2].Length());
            var thin = size.X <= size.Y && size.X <= size.Z ? 0 : size.Y <= size.Z ? 1 : 2;
            var normal = axes[thin] / MathF.Max(size[thin], 1e-5f);
            var view = centre - eye;
            var len = view.Length();
            var facing = len > 1e-4f ? MathF.Abs(Vector3.Dot(view / len, normal)) : 1f;
            var graze = 1f - facing;
            var sheen = graze * graze;
            var colour = Vector3.Lerp(g.Color, GlassSheen, 0.35f * sheen);
            var alpha = g.Alpha + (0.85f - g.Alpha) * 0.6f * sheen * graze;
            gfx.Draw(m, colour, alpha, mode: 0, shape: g.Shape);
            drawn++;
            if (g.Shape != 0)
                continue;
            // The edges: a thin strip along both ends of each of the pane's two long axes, a hair proud of its faces.
            for (var a = 0; a < 3; a++)
            {
                if (a == thin)
                    continue;
                var b = 3 - a - thin; // (the other long axis: the strip sits at its two ends)
                if (size[b] < EdgeWidth * 3)
                    continue;
                var w = EdgeWidth / size[b];
                for (var e = -1; e <= 1; e += 2)
                {
                    var scale = Vector3.One;
                    scale[b] = w;
                    scale[thin] = 1.08f;
                    var off = Vector3.Zero;
                    off[b] = e * (0.5f - w * 0.5f);
                    gfx.Draw(Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(off) * m, Vector3.Lerp(g.Color, GlassSheen, 0.6f), MathF.Min(0.9f, g.Alpha + 0.3f + 0.3f * graze), mode: 0);
                    drawn++;
                }
            }
        }
        gfx.Relative = false;
        LastGlass = drawn;
    }

    /// <summary>A box's twelve edges as thin bars (a slot's ghost outline).</summary>
    private static void Outline(Renderer gfx, Vector3 c, Vector3 h, in Matrix4x4 m, Vector3 colour, float opacity)
    {
        const float w = 0.012f;
        for (var axis = 0; axis < 3; axis++)
            for (var e = 0; e < 4; e++)
            {
                var size = new Vector3(w);
                size[axis] = h[axis] * 2 + w;
                var at = c;
                var a1 = (axis + 1) % 3;
                var a2 = (axis + 2) % 3;
                at[a1] += (e & 1) == 0 ? -h[a1] : h[a1];
                at[a2] += (e & 2) == 0 ? -h[a2] : h[a2];
                gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(at) * m, colour, opacity, mode: 2);
            }
    }

    /// <summary>Where a wheel's part is drawn against its place at rest (the part sits at mid-travel): up its travel, along its arm, leaning with it.</summary>
    public static Matrix4x4 HubMatrix(Construct c, int k)
    {
        c.HubMotion(k, out var shift, out var angle);
        ref var w = ref c.Wheels[k];
        var lean = angle * w.ArmTilt;
        if (lean == 0)
            return Matrix4x4.CreateTranslation(shift);
        c.PartBounds(w.Part, out var mn, out var mx);
        var hub = (mn + mx) * 0.5f;
        return Matrix4x4.CreateTranslation(-hub) * Matrix4x4.CreateFromAxisAngle(w.ArmAxis, lean) * Matrix4x4.CreateTranslation(hub + shift);
    }

    /// <summary>How a suspension's rigged boxes stand now (<see cref="BoxRig"/>): what its wheel does. Nothing on its axle: at rest.</summary>
    public static SuspensionRig RigOf(Construct c, int part)
    {
        for (var k = 0; k < c.WheelCount; k++)
            if (c.Wheels[k].Suspension == part)
            {
                c.HubMotion(k, out _, out var angle);
                return new SuspensionRig(HubMatrix(c, k), c.Wheels[k].ArmAxis, angle);
            }
        return new SuspensionRig(Matrix4x4.Identity, Vector3.UnitZ, 0);
    }

    /// <summary>The wheels' springs on a client (or the host's drawing between ticks): where each wheel meets the ground now.</summary>
    public static void FitWheels(ConstructWorld world, IWorldQuery query, Vector3 viewer, double now)
    {
        foreach (var c in world.Bodies)
        {
            if (c.WheelCount == 0 || now < c.FitAt)
                continue;
            // Rays as fast as the ticks only for a wheel that's moving and close enough for it to show (by the clock, so a
            // faster frame rate doesn't buy more of them); bodies take their turns by id.
            var gap = c.Pos == c.PrevPos && c.Rot == c.PrevRot ? FitStillGap
                : Vector3.DistanceSquared(viewer, c.Pos) > FitNear * FitNear ? FitFarGap : 1.0 / World.TickRate;
            c.FitAt = now + gap * (1 + (c.Id & 3) * 0.02);
            var up = Vector3.Transform(Vector3.UnitY, c.Rot);
            for (var k = 0; k < c.WheelCount; k++)
                ConstructPhysics.FitWheel(c, query, k, up);
        }
    }

    private const float FitNear = 60f;
    private const double FitStillGap = 0.25, FitFarGap = 0.1;
}

/// <summary>A suspension's moving boxes at one moment: the hub's move, the arm's axis and angle (<see cref="ConstructRender.RigOf"/>).</summary>
public readonly record struct SuspensionRig(Matrix4x4 Hub, Vector3 Axis, float Angle)
{
    private Matrix4x4 About(Vector3 pivot) =>
        Angle == 0 ? Matrix4x4.Identity : Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateFromAxisAngle(Axis, Angle) * Matrix4x4.CreateTranslation(pivot);

    /// <summary>A rigged box's model in the part's parent frame; <paramref name="rest"/> is its model at rest.</summary>
    public Matrix4x4 Model(in CPartBox box, in CPart p, in Matrix4x4 rest)
    {
        var pivot = p.Pos + Rot24.Apply(p.Rot, box.RigPivot);
        switch (box.Rig)
        {
            case BoxRig.Hub:
                return rest * Hub;
            case BoxRig.Arm:
                return rest * About(pivot);
            case BoxRig.Leg:
                var to = p.Pos + Rot24.Apply(p.Rot, box.LegTo);
                return box.Stretched(p.Pos + Rot24.Apply(p.Rot, box.LegFrom), Vector3.Transform(to, box.RigPivoted ? About(pivot) : Hub));
            default:
                return rest;
        }
    }
}
