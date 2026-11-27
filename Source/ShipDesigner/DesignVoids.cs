namespace CallOfSlop.ShipDesigner;

public static partial class DesignScene
{
    /// <summary>
    /// A space a piece through the skin keeps clear inside the hull (Karl, 2026-11-27: an open hangar or airlock showed a room's shell
    /// across it, so nothing could be seen or flown through): a box in the piece's own frame, O its middle on the skin, X/Y/Z unit axes
    /// (Y out of the skin), from <c>Lo</c> to <c>Hi</c> on each.
    /// </summary>
    public readonly record struct Void(double[] O, double[] X, double[] Y, double[] Z, double[] Lo, double[] Hi)
    {
        public bool Holds(double[] p)
        {
            double dx = p[0] - O[0], dy = p[1] - O[1], dz = p[2] - O[2];
            double a = dx * X[0] + dy * X[1] + dz * X[2], b = dx * Y[0] + dy * Y[1] + dz * Y[2], c = dx * Z[0] + dy * Z[1] + dz * Z[2];
            return a > Lo[0] && a < Hi[0] && b > Lo[1] && b < Hi[1] && c > Lo[2] && c < Hi[2];
        }
    }

    /// <summary>How far past an airlock's chamber its inner doorway clears a room's wall (m).</summary>
    private const double AirlockThrough = 1.0;

    /// <summary>
    /// The spaces every hangar bay and airlock keeps clear (each copy: mirrored and paired as the pieces are): a bay its whole volume, an
    /// airlock its chamber and its inner doorway on through the room's wall behind it. Room shells leave out every cell in one.
    /// </summary>
    public static List<Void> PieceVoids(ShipDesign d)
    {
        var o = new List<Void>();
        foreach (var p in d.Pieces)
        {
            if (d.Hull(p.Hull) is not { } h)
                continue;
            var air = PieceKinds.Find(p.Kind)?.Socket == "airlock";
            var bay = DesignHangar.Of(p);
            if (!air && bay is null)
                continue;
            var w = Where(h, p);
            var boxes = new List<(double[] Lo, double[] Hi)>();
            if (bay is { } b)
            {
                var (ex, ey, ez) = DesignHangar.Extent(b, w);
                boxes.Add(([-ex / 2, -ey - 0.3, -ez / 2], [ex / 2, 0.5, ez / 2]));
            }
            else
            {
                double s = p.Size, hx = p.AirH / 2 * s, hw = p.AirW / 2 * s, dz = Math.Min(p.DoorW, p.AirW) / 2 * s, dp = p.AirD * s;
                boxes.Add(([-hx, -dp - 0.05, -Math.Max(hw, dz)], [hx, 0.5, Math.Max(hw, dz)])); // (its throat and chamber)
                boxes.Add(([-hx, -dp - AirlockThrough, -dz], [hx, -dp + 0.05, dz])); // (its inner doorway, through the wall behind it)
            }
            var o0 = Add(w.O, w.Y, -p.Recess);
            var places = new List<Place> { w with { O = o0 } };
            if (p.Mirror)
                places.Add(MirrorPlace(places[0], h.CopyX(Math.Min(p.Copy, h.Copies - 1))));
            if (p.Pair && h.Copies == 2)
                for (int k = 0, n = places.Count; k < n; k++)
                    places.Add(MirrorPlace(places[k], 0));
            foreach (var pl in places)
                foreach (var (lo, hi) in boxes)
                    o.Add(new Void(pl.O, pl.X, pl.Y, pl.Z, lo, hi));
        }
        return o;
    }

    private static double[] Add(double[] a, double[] v, double s) => [a[0] + v[0] * s, a[1] + v[1] * s, a[2] + v[2] * s];

    private static Place MirrorPlace(Place w, double cx)
    {
        static double[] F(double[] v) => [-v[0], v[1], v[2]];
        return new Place([2 * cx - w.O[0], w.O[1], w.O[2]], F(w.X), F(w.Y), F(w.Z));
    }
}
