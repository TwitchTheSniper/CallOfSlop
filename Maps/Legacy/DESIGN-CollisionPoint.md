# Collision Point: design notes

*The six Collision Point maps (Blueprint, Galleria, Floodgate, Trefoil, Citadel, Confluence) were a pack of their own; they are now files 25-30 of the Legacy pack, in this folder.*

Six free-for-all maps (two small, two medium, two large) built around the ideas in Güttler & Johansson, *Spatial Principles of Level-Design in Multi-Player First-Person Shooters*. The paper's central idea is the **collision point**: the place where the routes of opposing players cross. Good maps decide where these points are and build them well, with cover, height differences and several ways in. Bad maps let the fight happen somewhere unplanned. In the paper's cs_citymall the fights broke out in an open area with no cover, while the showpiece hall stood empty.

The paper's examples are team maps. For deathmatch, each map has clusters of spawns (**spawn regions**), and its collision points sit at equal walking distance between two or more regions. All six maps were checked with a top-down flow analysis: the shortest walking routes between every pair of spawns, with their overlaps shown as heat. The bright spots had to land on the cover-rich places that were designed for them, not on open ground. The other checks were spawn-to-spawn lines of sight (none, except between spawns in the same pocket) and the longest open sightlines.

The other guidelines followed throughout: no dead ends and no rooms with only one way in; a few big, recognisable landmarks plus a colour scheme for each area so players always know where they are (*level orientation*); every space has a purpose (*level architecture*); and no position that overlooks everything, so seeing without being seen always costs exposure somewhere.

## 01 Blueprint (small, 60 × 60 m)
This is the paper's "Example C" made playable: L-shaped blocks give every corner start cover, and the second "target" means the fight has two centres, not one predictable one. The north and south plinths are the high-ground collision points between neighbouring corners. They stand against the outer wall, so the edge lane has to pass over or in front of them. The low trenches east and west, where bunkers bend the edge lane through the cover, are the low-ground collision points. Fights between opposite corners meet in the ring of small L's in the middle.

## 02 Galleria (small, 72 × 60 m)
A tribute to the paper's de_shopping: an exterior ring of streets (the spawn regions) around an interior hall, the place where the paper's collisions actually happened as planned. Two corners are built up solid, so the ring is two L-shaped halves. Reaching the far side means going through the hall or taking the long way round through an alley. The hall has six doors (no single-entrance room) and two raised kiosks either side of the fountain as its two collision points. Its shop windows are staggered so that no sightline crosses the building. The east and west doors are where the flow is heaviest.

## 03 Floodgate (medium, 112 × 80 m)
This is de_dust's "divided field" for deathmatch. A 6 m floodwall splits two mirrored halves, and every cross-map route has to use one of three crossings, each with its own kind of fight. The sluice house is close quarters inside a building, the main gate is a wide plaza with staggered cover and hazard-striped pillars as the landmark, and the breach means climbing over rubble. The main gate is the primary collision point, the other two are flanks. Each half has its own palette (ochre north, teal south), and nothing overlooks the wall (the sluice house has no roof access).

## 04 Trefoil (medium, 100 × 92 m)
Three colour-coded walled gardens (red, blue, gold) are the spawn regions, and each has three exits: a spoke to the central cloister and a ring path to each neighbour. The first layout repeated cs_citymall's mistake. The flow analysis showed the routes bypassing the cloister, so walled islands now fill the space between the spokes. The short way between gardens therefore runs past the cloister's colonnades. The campanile plaza (red/blue), the ruined chapel (red/gold) and the well house (blue/gold) are the ring's collision points, each with its own landmark.

## 05 Citadel (large, 150 × 150 m)
This is de_shopping at large scale. The town quarters are the spawn regions, each with a corner square and its own accent colour, and they ring a walled citadel. Each wall has a gate and a breach, eight ways into the bailey. Players from opposite quarters meet in the bailey around the keep, which has a door on every side and no roof access. Players from neighbouring quarters meet where the town lanes cross the main street at the market cross. The lanes jog there by two house rows, so no street gives a view across the map, and every square has four ways out.

## 06 Confluence (large, 152 × 152 m)
Here the terrain makes the collision points. Four unclimbable ridge spines split the map into four valleys (pine, red sandstone, straw, slate), each with a hamlet where players spawn. Each ridge has exactly one pass, a chicane with a cairn. That pass is the collision point between the two valleys either side, the same walk from both hamlets. Every valley floor runs down to the confluence, the basin around a ruined mill, where opposite valleys meet. A knoll splits each valley floor into two routes, and the hedgerows and boulders keep the long open views in check.
