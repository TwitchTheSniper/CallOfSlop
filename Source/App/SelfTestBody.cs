using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "Body model" self-test section (Source/Render/BodyModel.cs): the torso and head parts stay in the old torso and head
/// boxes (so the hit boxes, which never changed, still match what you see, and the head is the headshot zone), prestige hats
/// still sit on the cranium, seeded variants are deterministic and varied (bots by personality), the draw cost per player
/// and per zombie stays within 1.5x of the old boxes, and distant bodies drop their detail parts.
/// </summary>
public static partial class SelfTest
{
    private static void CheckBodyModel()
    {
        BodyHitboxes();
        BodyFit();
        BodyVariants();
        BodyCost();
    }

    private static (Vector3 Min, Vector3 Max) PartBounds(in BodyPart p, Vector3 scale, in Matrix4x4 frame)
    {
        var (min, max) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        var local = Matrix4x4.CreateScale(p.Size * scale) * Matrix4x4.CreateTranslation(p.Center * scale);
        for (var c = 0; c < 8; c++)
        {
            var v = Vector3.Transform(new Vector3((c & 1) - 0.5f, ((c >> 1) & 1) - 0.5f, ((c >> 2) & 1) - 0.5f), local * frame);
            (min, max) = (Vector3.Min(min, v), Vector3.Max(max, v));
        }
        return (min, max);
    }

    // ---- Hit boxes are the same as before the body model (presentation only) ----
    private static void BodyHitboxes()
    {
        const float r = World.PlayerRadius;
        var want = new[]
        {
            new Box(new Vector3(0, 0.9f, 0), new Vector3(r, 0.9f, r)), new Box(new Vector3(0, 0.6f, 0), new Vector3(r, 0.6f, r)),
            new Box(new Vector3(0, 0.3f, -0.1f), new Vector3(0.3f, 0.3f, 0.9f)),
        };
        var stances = new[] { Stance.Stand, Stance.Crouch, Stance.Prone };
        for (var i = 0; i < 3; i++)
        {
            var hb = World.Hitbox(Vector3.Zero, stances[i], 0);
            if (Vector3.Distance(hb.Center, want[i].Center) > 1e-5f || Vector3.Distance(hb.Half, want[i].Half) > 1e-5f)
                Fail($"body: {stances[i]} hit box changed: {hb} (want {want[i]})");
        }
        if (World.HeadStart != 1.4f || World.PlayerHeight != 1.8f)
            Fail($"body: head zone changed ({World.HeadStart} to {World.PlayerHeight})");
        Line("  hit boxes: stand, crouch, prone unchanged; headshots above 1.40 m standing");
    }

    // ---- The parts fill the old boxes; hats fit the cranium ----
    private static void BodyFit()
    {
        var worstOut = 0f;
        for (var st = 0; st < 3; st++)
        {
            var s = BodyModel.Stances[st];
            var torsoFrame = s.TorsoFrame;
            var headFrame = Matrix4x4.CreateTranslation(s.HeadAt);
            var tMin = s.TorsoAt - s.TorsoSize / 2;
            var tMax = s.TorsoAt + s.TorsoSize / 2;
            // Torso parts: inside the old torso box (in the upper body's frame, lying or not), give or take the plate
            // carrier's pouches; the neck is left out (it reaches up into the head).
            foreach (var p in BodyModel.TorsoParts)
            {
                if (p.Paint == BodyPaint.Skin)
                    continue;
                var (min, max) = PartBounds(p, s.TorsoScale, torsoFrame);
                var o = Vector3.Max(tMin - min, max - tMax);
                var outBy = MathF.Max(o.X, MathF.Max(o.Y, o.Z));
                worstOut = MathF.Max(worstOut, outBy);
                if (outBy > 0.1f)
                    Fail($"body: stance {st} torso part {p.Center} sticks {outBy:0.00} m out of the old torso box");
            }
            // Head: the cranium's top is the old head box's top (hats sit there) and nearly as wide.
            var crTop = PartBounds(BodyModel.Cranium, s.HeadScale, Matrix4x4.Identity).Max.Y;
            var crW = BodyModel.Cranium.Size.X * s.HeadScale.X;
            if (MathF.Abs(crTop - s.HeadSize.Y / 2) > 1e-4f || crW < s.HeadSize.X * 0.95f || crW > s.HeadSize.X)
                Fail($"body: stance {st} cranium top {crTop:0.000} (head top {s.HeadSize.Y / 2:0.000}), width {crW:0.000} of {s.HeadSize.X:0.000}");
            var headLow = float.MaxValue;
            foreach (var p in BodyModel.HeadParts.Append(BodyModel.Beard))
            {
                var (min, max) = PartBounds(p, s.HeadScale, headFrame);
                headLow = MathF.Min(headLow, min.Y);
                if (max.Y > s.HeadAt.Y + s.HeadSize.Y / 2 + 1e-4f)
                    Fail($"body: stance {st} head part {p.Center} pokes above the head top");
            }
            if (st == 0 && headLow < World.HeadStart - 0.02f)
                Fail($"body: the standing face reaches down to {headLow:0.00} m, below the headshot line {World.HeadStart:0.00}");
            // Headwear: at most a few centimetres above the head (helmets), all of it hidden by a hat (hair keeps its back only).
            foreach (var hw in BodyModel.HeadwearParts)
                foreach (var p in hw)
                {
                    var (_, max) = PartBounds(p, s.HeadScale, Matrix4x4.Identity);
                    if (max.Y > s.HeadSize.Y / 2 + 0.04f)
                        Fail($"body: stance {st} headwear part {p.Center} rises {max.Y - s.HeadSize.Y / 2:0.00} m above the head");
                }
        }
        var hatted = new BodyParts();
        var torso = new BodyParts();
        var v = new BodyVariant(0, 0, Headwear.Helmet, true, 0);
        var c = BodyColors.For(Vector3.One * 0.5f, v);
        BodyModel.Build(torso, hatted, 0, v, c, hat: true);
        var top = 0f;
        for (var i = 0; i < hatted.Count; i++)
            top = MathF.Max(top, Vector3.Transform(new Vector3(0, 0.5f, 0), hatted.Local[i]).Y);
        if (top > BodyModel.Stances[0].HeadSize.Y / 2 + 1e-4f)
            Fail($"body: with a hat on, a head part reaches {top:0.000} (the hat sits at {BodyModel.Stances[0].HeadSize.Y / 2:0.000})");
        Line($"  fit: torso parts within {worstOut:0.00} m of the old torso box, cranium top = old head top (hats sit as before), face above the headshot line");
    }

    // ---- Seeded variants ----
    private static void BodyVariants()
    {
        var skins = new HashSet<int>();
        var wear = new HashSet<Headwear>();
        var beards = 0;
        for (var i = 0; i < 96; i++)
        {
            var name = "Player" + i;
            var a = BodyModel.VariantFor(name, (byte)i);
            if (a != BodyModel.VariantFor(new string(name.AsSpan()), (byte)(i + 1)))
                Fail($"body: {name}'s look isn't a function of the name");
            skins.Add(a.Skin);
            wear.Add(a.Headwear);
            beards += a.Beard ? 1 : 0;
        }
        if (skins.Count < 5 || wear.Count < 4 || beards < 10 || beards > 70)
            Fail($"body: 96 people got {skins.Count} skin tones, {wear.Count} headwear kinds, {beards} beards (want varied)");
        if (BodyModel.VariantFor("", 3) == BodyModel.VariantFor("", 4) && BodyModel.VariantFor("", 5) == BodyModel.VariantFor("", 6))
            Fail("body: nameless players all look the same (the id should seed them)");
        if (BodyModel.PersonalityOf("[BOT] Brick (Rusher)") != BotPersonality.Rusher || BodyModel.PersonalityOf("Brick (Rusher)") != null
            || BodyModel.PersonalityOf("[BOT] Brick") != null || BodyModel.PersonalityOf("[BOT] X (Nope)") != null)
            Fail("body: bot personality from the name is wrong");
        var helmets = 0;
        for (var i = 0; i < 20; i++)
            if (BodyModel.VariantFor($"[BOT] B{i} (Anchor)", 0).Headwear == Headwear.Helmet)
                helmets++;
        var rushers = 0;
        for (var i = 0; i < 20; i++)
            if (BodyModel.VariantFor($"[BOT] B{i} (Rusher)", 0).Headwear is Headwear.Bandana or Headwear.Buzz)
                rushers++;
        if (helmets != 20 || rushers != 20)
            Fail($"body: {helmets}/20 Anchors wear helmets, {rushers}/20 Rushers bandanas or buzz cuts");
        // Every variant builds within capacity.
        var t = new BodyParts();
        var h = new BodyParts();
        foreach (Headwear hw in Enum.GetValues<Headwear>())
            foreach (var beard in new[] { false, true })
                foreach (var hat in new[] { false, true })
                    for (var st = 0; st < 3; st++)
                    {
                        var v = new BodyVariant(1, 2, hw, beard, 3);
                        BodyModel.Build(t, h, st, v, BodyColors.For(new Vector3(0.2f, 0.4f, 0.9f), v), hat);
                        var want = BodyModel.HeadParts.Length + BodyModel.HeadwearCount(v, hat) + (beard ? 1 : 0);
                        if (t.Count != BodyModel.TorsoParts.Length || h.Count != want)
                            Fail($"body: {hw} beard {beard} hat {hat}: {t.Count} torso + {h.Count} head parts (want {BodyModel.TorsoParts.Length} + {want})");
                    }
        Line($"  variants: 96 people -> {skins.Count} skin tones, {wear.Count} headwear kinds, {beards} beards; Anchors 20/20 helmets, Rushers 20/20 bandana/buzz");
    }

    // ---- Cost: draws per player and per zombie vs the old boxes ----
    private static void BodyCost()
    {
        var scene = new Scene();
        var list = new DrawList();
        var p = new Player { Id = 11, Name = "[BOT] Cost (Anchor)" };
        p.Teleport(new Vector3(4, 0, 4));
        var rig = new PlayerRig(p.Id);
        scene.AddRoot(rig.Root);
        var viewer = LegAnimator.Viewer;
        int nearMax = BodyModel.MaxParts, farMax = BodyModel.TorsoParts.Count(x => !x.Detail) + BodyModel.HeadParts.Count(x => !x.Detail)
            + BodyModel.HeadwearParts.Max(x => x.Length) + 1;
        var worst = 0f;
        var minOther = int.MaxValue;
        var now = 77.0;
        var farDropped = true;
        foreach (var w in Weapons.All)
        {
            p.Primary = p.Weapon = w.Id;
            foreach (var stance in (ReadOnlySpan<Stance>)[Stance.Stand, Stance.Crouch, Stance.Prone])
            {
                now += 0.3;
                p.RenderStance = stance;
                LegAnimator.Viewer = new Vector3(float.NaN);
                rig.Pose(p, now);
                scene.UpdateTransforms();
                list.Clear();
                list.Flatten(rig.Root);
                var (torso, head) = rig.Body(BodyModel.StanceIndex(stance));
                var other = list.Count - torso.Visible - head.Visible; // legs, arms, gun, health bar
                minOther = Math.Min(minOther, other);
                worst = MathF.Max(worst, (other + nearMax) / (float)(other + 3)); // old: torso, head, visor
                LegAnimator.Viewer = p.RenderPosition + new Vector3(BodyModel.DetailRange + 5, 0, 0);
                rig.Pose(p, now);
                scene.UpdateTransforms();
                list.Clear();
                list.Flatten(rig.Root);
                if (list.Count - other != torso.Visible + head.Visible || torso.Visible >= torso.Count || head.Visible >= head.Count)
                    farDropped = false;
            }
        }
        LegAnimator.Viewer = viewer;
        if (worst > 1.5f)
            Fail($"body: a player costs up to {worst:0.00}x the old draws (budget 1.5x)");
        if (!farDropped)
            Fail("body: distant players still draw their detail parts");

        // Zombies: the old torso + head were 2 boxes of 8 (2 legs, 2 eyes, 2 arms around them).
        var rec = new DrawRecorder();
        var g = new Renderer { Recorder = rec };
        var near = BodyModel.DrawZombieUpper(g, Matrix4x4.Identity, Vector3.One * 0.3f, new Vector3(0.36f, 0.52f, 0.3f), true);
        var far = BodyModel.DrawZombieUpper(g, Matrix4x4.Identity, Vector3.One * 0.3f, new Vector3(0.36f, 0.52f, 0.3f), false);
        if (rec.Count != near + far || (6 + near) / 8f > 1.5f || far >= near)
            Fail($"body: zombie upper body {near} boxes near, {far} far ({rec.Count} recorded)");
        Line($"  cost: player body {nearMax} boxes up close / {farMax} far (was 3); worst whole player {worst:0.00}x the old draws "
             + $"(fewest other boxes {minOther}); zombie {6 + near} / {6 + far} boxes (was 8); same cube mesh, so vertices scale the same");
        Line($"[body] per-player draws: worst {worst:0.00}x; body {nearMax}/{farMax} boxes near/far, detail range {BodyModel.DetailRange:0} m");
    }
}
