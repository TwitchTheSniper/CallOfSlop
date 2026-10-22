using System.Numerics;

namespace CallOfSlop;

/// <summary>How a weapon is reloaded by the arms (picked from the weapon's class, magazine and name).</summary>
public enum ReloadStyle : byte
{
    /// <summary>Off hand to the magazine, drop it, bring a new one, insert, slap.</summary>
    BoxMag,

    /// <summary>Pistol: mag drop, new mag up into the grip, rack the slide.</summary>
    PistolMag,

    /// <summary>Revolver: cylinder swings out, eject, speedloader, flick closed.</summary>
    Revolver,

    /// <summary>Tube shotgun: shell by shell into the loading port, then a pump.</summary>
    TubeShells,

    /// <summary>Break-action: barrels down, two shells into the breech, snap shut.</summary>
    BreakOpen,

    /// <summary>Bolt action: work the bolt with the grip hand while the off hand swaps the magazine.</summary>
    BoltAction,

    /// <summary>LMG: pull the box, seat a new one, slam the feed cover.</summary>
    LmgBox,

    /// <summary>Muzzle loader: muzzle up, ram the charge down the barrel.</summary>
    MuzzleLoad,
}

/// <summary>The shape a hand makes (see <see cref="Arms"/> for the boxes).</summary>
public enum HandShape : byte
{
    /// <summary>Fingers wrapped round a vertical bar (pistol grip, vertical foregrip, magazine).</summary>
    Wrap,

    /// <summary>Palm up under a horizontal bar (handguard, pump, forend).</summary>
    Under,

    /// <summary>The off hand cupping the strong hand on a pistol grip.</summary>
    Cup,

    /// <summary>Palm down on a surface (ledge, slide, feed cover, bolt knob).</summary>
    Flat,
}

/// <summary>Everything the arms need to know about one gun, in gun-local space (origin = grip, -Z = muzzle).</summary>
public struct ArmAnchorSet
{
    public Vector3 Grip, Support, MagWell;

    /// <summary>Where a hand works the action: slide rear, bolt knob, LMG feed cover, pump, charging handle.</summary>
    public Vector3 Action;

    public Vector3 Muzzle;

    /// <summary>The off hand's spot while aiming through a magnified optic (on the magazine or under the receiver), out of the lens.</summary>
    public Vector3 SupportAds;

    public Quaternion GripRot, SupportRot, MagRot, ActionRot;
    public HandShape SupportShape, MagShape, SupportAdsShape;
    public ReloadStyle Style;

    /// <summary>Box size of what the off hand carries during a reload (new magazine, shell, speedloader...).</summary>
    public Vector3 Carry;

    public Vector3 CarryColor;

    /// <summary>Shells loaded one by one by a tube reload.</summary>
    public int Shells;

    /// <summary>Aiming through a magnified optic (ACOG, scopes, built-in scopes): the arms keep out of the lens.</summary>
    public bool Magnified;
}

/// <summary>
/// Where the hands go on today's box guns (<see cref="GunModel"/>), for every class including the specials and the
/// attachments that change the shape (foregrips, laser, magazines, heavy barrel). Guns built from a "model" block
/// take their grip, support and magazine spots straight from the built model (<see cref="GunModel.HandPoints"/>);
/// the rest follow the same proportions the per-class models are drawn with.
/// </summary>
public static class ArmAnchors
{
    private static string? Att(WeaponDef d, AttachSlot slot) => Attachments.LookIn(d.Kit, slot);

    /// <summary>The reload animation a weapon uses.</summary>
    public static ReloadStyle StyleOf(WeaponDef d) =>
        d.Model?.Mag == "tube" && d.Class != WeaponClass.Shotgun ? ReloadStyle.TubeShells // a tube-fed lever rifle loads round by round
        : d.Class switch
        {
            WeaponClass.Pistol => d.Mag <= 6 ? ReloadStyle.Revolver : ReloadStyle.PistolMag,
            WeaponClass.Shotgun when d.Name.Contains("Double") => ReloadStyle.BreakOpen,
            WeaponClass.Shotgun when d.Mag >= 10 && Att(d, AttachSlot.Magazine) != "Extended Mag" => ReloadStyle.BoxMag, // drum
            WeaponClass.Shotgun => ReloadStyle.TubeShells,
            WeaponClass.Sniper => d.Name.Contains("Semi") ? ReloadStyle.BoxMag : ReloadStyle.BoltAction,
            WeaponClass.LMG => ReloadStyle.LmgBox,
            WeaponClass.Special when d.Mag <= 1 => ReloadStyle.MuzzleLoad,
            WeaponClass.Launcher when d.Mag > 1 => ReloadStyle.Revolver, // a drum launcher, shell by shell
            WeaponClass.Launcher when d.Model?.Receiver == "break" => ReloadStyle.BreakOpen, // break-action grenade launcher
            WeaponClass.Launcher => ReloadStyle.MuzzleLoad, // a rocket goes in the front of the tube
            _ => ReloadStyle.BoxMag,
        };

    /// <summary>Angle of the pistol grip (radians about X), which the grip hand follows.</summary>
    public static float GripAngle(WeaponDef d) =>
        GunModel.Mesh(d) is { } m ? -m.GripAngle : d.Class switch
        {
            WeaponClass.Pistol => -0.25f,
            WeaponClass.Shotgun => -0.35f,
            WeaponClass.Sniper => -0.4f,
            _ => -0.3f,
        };

    /// <summary>
    /// The three hand anchors in gun-local space (origin = grip, -Z = muzzle; the space of the pose passed to GunModel.Draw):
    /// the grip hand's spot, the off hand's spot (handguard, pump, forend, foregrip; on a pistol the off hand cups the
    /// grip, so it's the grip again) and where the off hand holds a seated magazine (box face for LMGs, the cylinder
    /// for revolvers, the loading port or breech for shotguns).
    /// </summary>
    public static (Vector3 Grip, Vector3 Support, Vector3 MagWell) HandPoints(WeaponDef d)
    {
        if (GunModel.HandPoints(d) is { } built)
            return built;
        var (recvLen, recvH, recvW, barrelLen, barrelW0, guardLen, guardH) = GunModel.Dims(d);
        var front = -recvLen * 0.62f;
        var back = recvLen * 0.38f;
        var mag = Att(d, AttachSlot.Magazine);
        var cls = d.Class;
        if (cls == WeaponClass.Pistol)
        {
            var g = new Vector3(0, -recvH * 0.5f - 0.05f, back - 0.02f);
            if (d.Mag <= 6)
                return (g, g, new Vector3(-recvH * 0.36f - 0.012f, recvH * 0.08f, back - recvLen * 0.45f)); // left side of the cylinder
            var baseY = -recvH * 0.5f - 0.11f;
            return (g, g, new Vector3(0, baseY - (mag == "Extended Mag" ? 0.03f : 0), back - 0.01f));
        }

        Vector3 grip, support, magWell;
        if (cls == WeaponClass.Shotgun)
        {
            var barrelW = barrelW0 * (Att(d, AttachSlot.Barrel) == "Heavy Barrel" ? 1.5f : 1f) * (0.9f + d.Caliber * 0.25f);
            var barrelY = recvH * 0.12f;
            grip = new Vector3(0, -recvH * 0.5f - 0.035f, back - 0.02f);
            if (d.Name.Contains("Double"))
            {
                support = new Vector3(0, barrelY - barrelW * 0.6f - 0.02f, front - barrelLen * 0.2f);
                magWell = new Vector3(0, barrelY + barrelW * 0.3f, front - 0.015f); // the breech
            }
            else
            {
                var pumpZ = front - guardLen / 2 - 0.03f;
                support = d.Name.Contains("Lever") ? new Vector3(0, barrelY - barrelW * 1.3f, pumpZ) : new Vector3(0, barrelY - barrelW * 0.95f - 0.025f, pumpZ);
                magWell = d.Mag >= 10 && mag != "Extended Mag"
                    ? new Vector3(0, -recvH * 0.5f - 0.12f, front + 0.04f) // drum
                    : new Vector3(0, -recvH * 0.25f, front + recvLen * 0.3f); // loading port
            }
        }
        else if (cls == WeaponClass.Sniper)
        {
            grip = new Vector3(0, -recvH * 0.45f - 0.04f, back - 0.02f);
            support = new Vector3(0, -recvH * 0.475f, front - barrelLen * 0.22f);
            magWell = d.Mag > 1 ? BoxMagWell(d, recvLen, recvH, recvW, front, mag) : new Vector3(recvW * 0.5f, recvH * 0.15f, back - recvLen * 0.35f);
        }
        else
        {
            grip = new Vector3(0, -recvH * 0.5f - 0.045f, back - recvLen * 0.2f);
            support = guardLen > 0 ? new Vector3(0, recvH * 0.05f - guardH * 0.5f, front - guardLen * 0.45f) : new Vector3(0, -recvH * 0.5f, front + 0.02f);
            magWell = BoxMagWell(d, recvLen, recvH, recvW, front, mag);
        }

        // Underbarrel parts sit at the same place on every long gun (see GunModel.Draw).
        var ubZ = front - guardLen * 0.45f;
        var ubY = -guardH * 0.5f;
        switch (Att(d, AttachSlot.Underbarrel))
        {
            case "Vertical Grip":
                support = new Vector3(0, ubY - 0.07f, ubZ);
                break;
            case "Angled Grip":
                support = new Vector3(0, ubY - 0.035f, ubZ + 0.01f);
                break;
            case "Laser":
                support = support with { Z = ubZ + 0.065f }; // just behind the laser box
                break;
        }
        return (grip, support, magWell);
    }

    /// <summary>Where the off hand holds a box, curved, drum or clamped magazine (GunModel.DrawMagazine).</summary>
    private static Vector3 BoxMagWell(WeaponDef d, float recvLen, float recvH, float recvW, float front, string? mag)
    {
        var magZ = front + recvLen * 0.22f;
        var magY = -recvH * 0.5f;
        var size = Math.Clamp(d.Mag / 30f, 0.5f, 1.4f);
        var ext = mag == "Extended Mag" ? 1.55f : 1f;
        if (d.Class == WeaponClass.LMG)
        {
            var big = mag == "Drum Mag" ? 1.35f : mag == "Extended Mag" ? 1.18f : 1f;
            return new Vector3(-recvW * 0.35f - recvW * 0.75f * big + 0.02f, magY - 0.06f * big, magZ); // the box's left face
        }
        if (mag == "Drum Mag")
            return new Vector3(0, magY - 0.12f, magZ);
        if (mag == "Fast Mag")
            return new Vector3(0, magY - 0.08f * size, magZ);
        if (d.Class == WeaponClass.AssaultRifle)
        {
            var h = 0.09f * size * ext;
            return new Vector3(0, magY - h * 1.2f, magZ - h * 0.2f);
        }
        var hh = (d.Class == WeaponClass.SMG ? 0.17f : 0.11f) * size * ext;
        return new Vector3(0, magY - hh * 0.6f, magZ);
    }

    /// <summary>Everything the arms need for one gun. The only caller of <see cref="HandPoints"/>.</summary>
    public static ArmAnchorSet For(WeaponDef d)
    {
        var a = new ArmAnchorSet();
        (a.Grip, a.Support, a.MagWell) = HandPoints(d);
        var (recvLen, recvH, recvW, barrelLen, _, guardLen, _) = GunModel.Dims(d);
        var top = recvH * 0.5f;
        var front = -recvLen * 0.62f;
        var back = recvLen * 0.38f;
        var built = GunModel.Mesh(d);
        if (built != null)
            (recvLen, recvH, recvW, top, front, back) = (built.RecvLen, built.RecvH, built.RecvW, built.Top, built.Front, built.Back);
        var cls = d.Class;
        a.Style = StyleOf(d);
        a.GripRot = Quaternion.CreateFromAxisAngle(Vector3.UnitX, GripAngle(d));
        // A handgun's off hand cups the grip, unless the model gives it a foregrip of its own.
        var cup = cls == WeaponClass.Pistol && (a.Support - a.Grip).Length() < 0.04f;
        a.SupportShape = cup ? HandShape.Cup : Att(d, AttachSlot.Underbarrel) == "Vertical Grip" || (built != null && cls == WeaponClass.Pistol) ? HandShape.Wrap : HandShape.Under;
        a.SupportRot = cup ? a.GripRot : Quaternion.Identity;
        a.Action = cls switch
        {
            WeaponClass.Pistol => new Vector3(0, top + 0.004f, back - 0.015f), // slide rear / hammer
            WeaponClass.Sniper when built != null => built.Spec?.Bolt == "side"
                ? new Vector3(recvW * 0.5f + 0.055f, built.Bore - 0.006f, back - 0.03f) // bolt knob
                : new Vector3(0, top + 0.01f, back - 0.03f), // charging handle
            WeaponClass.Sniper => new Vector3(recvW * 0.7f + 0.035f, recvH * 0.08f + 0.012f, back - 0.03f), // bolt knob
            WeaponClass.LMG => new Vector3(0, top + 0.01f, front + recvLen * 0.35f), // feed cover
            WeaponClass.Shotgun => a.Support, // pump
            _ => new Vector3(0, top + 0.01f, back - 0.03f), // charging handle
        };
        var barrelY = recvH * (cls == WeaponClass.Pistol ? 0.15f : 0.12f);
        var muzzleZ = cls switch
        {
            WeaponClass.Pistol => front - 0.01f,
            WeaponClass.Shotgun or WeaponClass.Sniper => front - barrelLen,
            _ => front - guardLen - barrelLen,
        };
        a.Muzzle = built != null ? new Vector3(0, built.Bore, built.MuzzleZ) : new Vector3(0, barrelY, muzzleZ);
        a.MagShape = a.Style switch
        {
            ReloadStyle.PistolMag or ReloadStyle.TubeShells => HandShape.Under,
            ReloadStyle.Revolver or ReloadStyle.BreakOpen => HandShape.Flat,
            _ => HandShape.Wrap,
        };
        a.MagRot = a.Style == ReloadStyle.Revolver ? Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2) : Quaternion.Identity;
        a.ActionRot = cls == WeaponClass.Sniper ? Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.5f) : Quaternion.Identity;
        if (a.MagShape == HandShape.Wrap && cls is not (WeaponClass.LMG or WeaponClass.Shotgun) && a.MagWell.Y < -recvH * 0.3f)
        {
            a.SupportAds = a.MagWell; // a "mag grip": the hand drops below the lens
            if (built != null)
                a.SupportAds.Y -= 0.04f; // built models give the top of the magazine: hold it lower down
            a.SupportAdsShape = HandShape.Wrap;
        }
        else
        {
            a.SupportAds = new Vector3(0, -recvH * 0.5f, front + recvLen * 0.25f);
            a.SupportAdsShape = HandShape.Under;
        }

        var size = Math.Clamp(d.Mag / 30f, 0.5f, 1.4f);
        var magColor = d.Class == WeaponClass.Special && d.Sound == SoundStyle.Toy ? Vector3.One * 0.85f : d.Accent * 0.85f;
        a.CarryColor = magColor;
        switch (a.Style)
        {
            case ReloadStyle.PistolMag:
                a.Carry = new Vector3(recvW * 0.8f, 0.1f, 0.045f);
                break;
            case ReloadStyle.LmgBox:
                a.Carry = new Vector3(recvW * 1.5f, 0.12f, 0.14f);
                break;
            case ReloadStyle.Revolver:
                a.Carry = new Vector3(recvH * 0.75f, recvH * 0.75f, 0.03f);
                a.CarryColor = new Vector3(0.4f, 0.41f, 0.44f);
                break;
            case ReloadStyle.TubeShells:
            case ReloadStyle.BreakOpen:
                a.Carry = new Vector3(0.022f, 0.022f, 0.065f);
                a.CarryColor = new Vector3(0.75f, 0.15f, 0.1f);
                break;
            case ReloadStyle.MuzzleLoad:
                a.Carry = new Vector3(0.008f, 0.26f, 0.008f); // ramrod
                a.CarryColor = new Vector3(0.35f, 0.36f, 0.38f);
                break;
            default:
            {
                var h = (cls == WeaponClass.SMG ? 0.17f : cls == WeaponClass.AssaultRifle ? 0.09f * 1.9f : 0.11f) * size;
                if (cls == WeaponClass.Shotgun)
                    h = 0.13f; // Street Sweeper drum
                a.Carry = new Vector3(recvW * 0.62f, h, 0.045f);
                break;
            }
        }
        a.Shells = Math.Clamp((int)(d.Reload / 0.7f), 2, 5);
        a.Magnified = d.Optic is OpticKind.Acog or OpticKind.Scope4 or OpticKind.Scope8 || (d.Optic == OpticKind.Iron && d.AdsZoom >= 1.8f && cls != WeaponClass.Pistol);
        return a;
    }
}
