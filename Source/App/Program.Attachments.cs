namespace CallOfSlop;

/// <summary>
/// Smoke-test step for the attachment showcase: after the first primary gunsmith shot, the gunsmith on a few of the
/// strange guns with their newest attachment in every slot (smoke_gunsmith_odd_N.bmp), then the usual steps carry on.
/// </summary>
public static unsafe partial class Program
{
    private static readonly string[] SmokeOddGuns = ["Old Faithful Musket", "Nailgun", "Pocket Derringer"];

    private static int _smokeOddGun = -1; // -1: not started; SmokeOddGuns.Length: done
    private static double _smokeOddStart;
    private static bool _smokeOddShot;
    private static Func<byte, uint>? _smokeOddKeep;

    /// <summary>The newest (last to unlock) attachment the gun takes in each slot.</summary>
    private static uint SmokeNewestKit(WeaponDef w)
    {
        uint kit = 0;
        var list = Attachments.For(w);
        for (var i = list.Count - 1; i >= 0; i--)
            if (Attachments.Slot(kit, list[i].Slot) == 0)
                kit = Attachments.With(kit, list[i].Slot, list[i].Index);
        return kit;
    }

    private static void SmokeOpenOddGun(double now)
    {
        _smokeOddStart = now;
        _smokeOddShot = false;
        if (Weapons.Find(SmokeOddGuns[_smokeOddGun]) is { } w)
            Ui.SmokeGunsmith(w.Id);
    }

    /// <summary>Runs the step; false once it's done (or there's nothing to show).</summary>
    private static bool SmokeOddGunsmithTick(double now)
    {
        if (_smokeOddGun >= SmokeOddGuns.Length)
            return false;
        if (_smokeOddGun < 0)
        {
            _smokeOddGun = 0;
            _smokeOddKeep = LoadoutStore.SmokeKit;
            LoadoutStore.SmokeKit = id => SmokeNewestKit(Weapons.Get(id));
            SmokeOpenOddGun(now);
            return true;
        }
        var t = now - _smokeOddStart;
        if (t > 0.7 && !_smokeOddShot)
        {
            _smokeOddShot = true;
            if (Weapons.Find(SmokeOddGuns[_smokeOddGun]) is { } w)
            {
                SmokeShot($"gunsmith_odd_{_smokeOddGun}");
                var kit = SmokeNewestKit(w);
                Console.WriteLine($"[smoke] gunsmith_odd_{_smokeOddGun}: {w.Name} [{Attachments.Describe(kit)}], {Attachments.For(w).Count} attachments fit, "
                    + $"model {GunModel.Mesh(Attachments.Apply(w, kit))?.Parts.Length ?? 0} boxes");
            }
            else
                Console.WriteLine($"[smoke] gunsmith_odd_{_smokeOddGun}: no weapon called {SmokeOddGuns[_smokeOddGun]}");
        }
        else if (t > 1.0)
        {
            _smokeOddGun++;
            if (_smokeOddGun >= SmokeOddGuns.Length)
            {
                LoadoutStore.SmokeKit = _smokeOddKeep;
                return false;
            }
            SmokeOpenOddGun(now);
        }
        return true;
    }
}
