using System.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

// The factions' homes on their worlds (docs/planets/WORLDS.md "Settlements"): a faction file's "settlement" names a
// planetoid, a place and the buildings round it.
public sealed partial class Game
{
    /// <summary>How near its spot a standing building counts as a settlement's (m).</summary>
    public const float SettlementNear = 6f;

    /// <summary>
    /// Server, once a match when the world is in (with <see cref="FragmentSitesHere"/>): the settlements of the planetoid being
    /// played. A building is found again by where it stands and is the faction's own (its Claims, so wrecking it is a deed
    /// against the faction); the first time the story comes to this world the buildings are built, and the story's Thing for
    /// the settlement notes it, so what was pulled down since isn't built again.
    /// </summary>
    private void SettlementsHere(StoryEngine s)
    {
        if (World.Map.Planetoid is not { } pl)
            return;
        for (var f = 0; f < s.Factions.Count; f++)
        {
            var def = s.Factions.Def(f);
            if (def.Settlement is not { } st || !st.Map.Equals(World.Map.Name, StringComparison.OrdinalIgnoreCase))
                continue;
            double lat = st.Lat * Math.PI / 180, lon = st.Lon * Math.PI / 180;
            var dir = new Vector3((float)(Math.Cos(lat) * Math.Sin(lon)), (float)Math.Sin(lat), (float)(Math.Cos(lat) * Math.Cos(lon)));
            var east = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, dir));
            var north = Vector3.Cross(dir, east);
            var top = pl.Center + dir * (float)(pl.Radius + pl.MaxHeight + 50);
            var key = StoryNameKey("settlement:" + def.Name);
            var fresh = s.World.Find(StoryKind.Thing, key) < 0;
            int built = 0, found = 0;
            foreach (var b in st.Buildings)
            {
                var ground = pl.GroundUnder(top + east * b.East + north * b.North);
                Construct? c = null;
                foreach (var o in Constructs.Bodies)
                    if (o.Anchored | o.Frozen && Vector3.DistanceSquared(o.Pos, ground) < SettlementNear * SettlementNear)
                        c = o;
                if (c != null)
                    found++;
                else if (fresh && ConstructBuild.FindBlueprint(b.Blueprint) is { } bp)
                {
                    c = ConstructBuild.BuildOnGround(Constructs, bp, ground, b.Yaw * MathF.PI / 180, out var why);
                    if (c == null)
                    {
                        Log($"[settlement] {def.Home}: the {b.Blueprint} wasn't built: {why}");
                        continue;
                    }
                    if (!c.Anchored)
                    {
                        c.Frozen = true;
                        c.Rebuild();
                    }
                    built++;
                }
                if (c != null)
                    s.Factions.ClaimFor(f, StoryConstruct(c.Id, b.Blueprint));
            }
            if (fresh)
                s.World.Get(StoryKind.Thing, key, def.Home.Length > 0 ? def.Home : def.Name, s.Time);
            Log($"[settlement] {(def.Home.Length > 0 ? def.Home : def.Name)} ({def.Name}) on {World.Map.Name} at latitude {st.Lat:0.#}, longitude {st.Lon:0.#}: {built} built, {found} found");
        }
    }
}
